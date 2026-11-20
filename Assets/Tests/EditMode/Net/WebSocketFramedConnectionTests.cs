using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Net
{
    /// <summary>
    /// 小游戏用的 WebSocket 分帧连接(服务端 docs/design/minigame-websocket-gate.md W10)。底层 WebSocket 换成假驱动、时钟可拨,
    /// 钉住它与 TCP 实现不一样的那几处时序:打开之前的帧排队、心跳由 Poll 驱动、连不上按断线报且只报一次、
    /// Dispose 之后不再派发。帧的字节与 TCP 相同,由共用的 MuduoCodec 保证,这里按解码结果断言。
    /// </summary>
    public sealed class WebSocketFramedConnectionTests
    {
        private sealed class FakeDriver : IWebSocketDriver
        {
            public string Url;
            public int DisposeCalls;
            public readonly List<byte[]> Sent = new();
            private Action _onOpen;
            private Action<byte[]> _onBinary;
            private Action<string> _onClosed;

            public void Open(string url, Action onOpen, Action<byte[]> onBinary, Action<string> onClosed)
            {
                Url = url;
                _onOpen = onOpen;
                _onBinary = onBinary;
                _onClosed = onClosed;
            }

            public void Send(byte[] message) => Sent.Add(message);
            public void Dispose() => DisposeCalls++;
            public void RaiseOpen() => _onOpen();
            public void RaiseBinary(byte[] bytes) => _onBinary(bytes);
            public void RaiseClosed(string reason) => _onClosed(reason);
        }

        private const string WsUrl = "wss://ws.example.com/ws/10.244.0.5/18000";

        private MuduoCodec _codec;
        private FakeDriver _driver;
        private WebSocketFramedConnection _conn;
        private double _now;
        private List<IMessage> _messages;
        private List<string> _errors;
        private int _disconnects;

        [SetUp]
        public void SetUp()
        {
            _codec = new MuduoCodec();
            _codec.Register<ClientTokenVerifyRequest>();
            _codec.Register<ClientTokenVerifyResponse>();
            _codec.Register<ClientHeartbeatRequest>();
            _codec.Register<ClientHeartbeatResponse>();
            _codec.Register<MessageContent>();
            _now = 1000;
            NewConnection(allowDirectUrl: false);
        }

        private void NewConnection(bool allowDirectUrl)
        {
            _driver = new FakeDriver();
            _conn = new WebSocketFramedConnection(_codec, _driver, () => _now, allowDirectUrl);
            _messages = new List<IMessage>();
            _errors = new List<string>();
            _disconnects = 0;
            _conn.OnMessage += _messages.Add;
            _conn.OnError += _errors.Add;
            _conn.OnDisconnected += () => _disconnects++;
        }

        private void Open()
        {
            _conn.Connect("203.0.113.9", 30000, WsUrl);
            _driver.RaiseOpen();
            _conn.Poll();
        }

        private T DecodeSent<T>(int index) where T : class, IMessage
            => _codec.TryDecode(_driver.Sent[index], 0, _driver.Sent[index].Length, out _) as T;

        private static MessageContent Content(ulong id) => new MessageContent { Id = id, MessageId = 900001 };

        [Test]
        public void ConnectsToTheServerProvidedUrl_AndQueuesFramesUntilOpen()
        {
            _conn.Connect("203.0.113.9", 30000, WsUrl);
            Assert.That(_driver.Url, Is.EqualTo(WsUrl), "小游戏连服务端下发的 WebSocket 地址,不读 host / port");
            Assert.That(_conn.Connected, Is.False, "打开之前不算连上");

            _conn.Send(new ClientTokenVerifyRequest { Payload = ByteString.CopyFromUtf8("ticket") });
            _conn.Poll();
            Assert.That(_driver.Sent, Is.Empty, "打开之前的帧先排队");

            _driver.RaiseOpen();
            _conn.Poll();
            Assert.That(_conn.Connected, Is.True);
            Assert.That(_driver.Sent, Has.Count.EqualTo(1));
            Assert.That(DecodeSent<ClientTokenVerifyRequest>(0)?.Payload.ToStringUtf8(), Is.EqualTo("ticket"), "一条消息就是一个客户端帧,字节与 TCP 相同");

            _conn.Send(Content(7));
            Assert.That(_driver.Sent, Has.Count.EqualTo(2), "打开之后直接发");
            Assert.That(_errors, Is.Empty);
            Assert.That(_disconnects, Is.Zero);
        }

        [Test]
        public void InboundBytesAreTreatedAsAStream()
        {
            Open();
            byte[] first = _codec.Encode(Content(1));
            byte[] second = _codec.Encode(Content(2));
            var head = new byte[5];
            var rest = new byte[first.Length - 5 + second.Length];
            Buffer.BlockCopy(first, 0, head, 0, 5);
            Buffer.BlockCopy(first, 5, rest, 0, first.Length - 5);
            Buffer.BlockCopy(second, 0, rest, first.Length - 5, second.Length);

            _driver.RaiseBinary(head);
            _conn.Poll();
            Assert.That(_messages, Is.Empty, "半个帧不派发");

            _driver.RaiseBinary(rest);
            _conn.Poll();
            Assert.That(_messages.ConvertAll(m => ((MessageContent)m).Id), Is.EqualTo(new List<ulong> { 1, 2 }), "跨消息的帧接上,一条消息里的多个帧逐个派发");
        }

        [Test]
        public void HeartbeatIsDrivenByPoll_AndItsReplyIsConsumed()
        {
            Open();
            _driver.RaiseBinary(_codec.Encode(new ClientTokenVerifyResponse { Success = true, HeartbeatIntervalMs = 10000, HeartbeatTimeoutMs = 30000 }));
            _conn.Poll();
            Assert.That(_messages, Has.Count.EqualTo(1), "验签应答照常交给上层");
            Assert.That(_driver.Sent, Has.Count.EqualTo(1), "通告之后第一拍立即发");
            Assert.That(DecodeSent<ClientHeartbeatRequest>(0)?.Seq, Is.EqualTo(1UL));

            _now += 9999;
            _conn.Poll();
            Assert.That(_driver.Sent, Has.Count.EqualTo(1), "没到间隔不发");
            _now += 1;
            _conn.Poll();
            Assert.That(DecodeSent<ClientHeartbeatRequest>(1)?.Seq, Is.EqualTo(2UL));

            _now += 40;
            _driver.RaiseBinary(_codec.Encode(new ClientHeartbeatResponse { Seq = 2 }));
            _conn.Poll();
            Assert.That(_messages, Has.Count.EqualTo(1), "心跳应答由连接自己消化,不交给上层");
            Assert.That(_conn.HeartbeatRoundTripMs, Is.EqualTo(40).Within(0.001));

            _now += 30000;
            _conn.Poll();
            Assert.That(_errors, Is.EqualTo(new[] { "gate heartbeat timed out" }));
            Assert.That(_disconnects, Is.EqualTo(1), "30 秒收不到任何字节按断线报");
            Assert.That(_conn.Connected, Is.False);
            Assert.That(_driver.DisposeCalls, Is.GreaterThan(0), "判死之后关掉底层连接");
        }

        [Test]
        public void FailureToOpen_IsReportedAsOneDisconnect()
        {
            _conn.Connect("203.0.113.9", 30000, WsUrl);
            _driver.RaiseClosed("connect failed");
            _driver.RaiseClosed("connect failed again");
            _conn.Poll();
            _conn.Poll();
            Assert.That(_errors, Is.EqualTo(new[] { "websocket: connect failed" }));
            Assert.That(_disconnects, Is.EqualTo(1), "连不上按断线报,整个生命周期只报一次");
            Assert.Throws<InvalidOperationException>(() => _conn.Send(Content(1)));
        }

        [Test]
        public void OpenTimeout_IsReportedAsOneDisconnect()
        {
            _conn.Connect("203.0.113.9", 30000, WsUrl);
            _now += WebSocketFramedConnection.OpenTimeoutMs - 1;
            _conn.Poll();
            Assert.That(_disconnects, Is.Zero);
            _now += 1;
            _conn.Poll();
            _conn.Poll();
            Assert.That(_errors, Is.EqualTo(new[] { "websocket open timed out" }));
            Assert.That(_disconnects, Is.EqualTo(1));
            Assert.That(_driver.DisposeCalls, Is.GreaterThan(0));
        }

        [Test]
        public void PeerClose_DeliversEarlierFramesFirst()
        {
            Open();
            _driver.RaiseBinary(_codec.Encode(Content(1)));
            _driver.RaiseClosed(string.Empty);
            _driver.RaiseBinary(_codec.Encode(Content(2)));
            _conn.Poll();
            Assert.That(_messages, Has.Count.EqualTo(1), "断线之前收到的帧先派发,之后的不再派发");
            Assert.That(_disconnects, Is.EqualTo(1));
            Assert.That(_errors, Is.Empty, "对端正常关闭不算错误");
        }

        [Test]
        public void NothingIsDispatchedAfterDispose_IncludingFromInsideAHandler()
        {
            Open();
            byte[] both = new byte[0];
            foreach (ulong id in new ulong[] { 1, 2 })
            {
                byte[] frame = _codec.Encode(Content(id));
                var joined = new byte[both.Length + frame.Length];
                Buffer.BlockCopy(both, 0, joined, 0, both.Length);
                Buffer.BlockCopy(frame, 0, joined, both.Length, frame.Length);
                both = joined;
            }
            // 重定向就是这个时序:处理器在回调里换掉连接,排在后面的残留帧不能再作用到新会话上。
            _conn.OnMessage += _ => _conn.Dispose();
            _driver.RaiseBinary(both);
            _driver.RaiseClosed(string.Empty);
            _conn.Poll();
            _conn.Poll();
            Assert.That(_messages, Has.Count.EqualTo(1));
            Assert.That(_disconnects, Is.Zero, "Dispose 之后不派发断线");
            Assert.That(_conn.Connected, Is.False);
        }

        [Test]
        public void HandlerException_DoesNotRedeliverTheFrame()
        {
            Open();
            bool thrown = false;
            _conn.OnMessage += _ =>
            {
                if (thrown) return;
                thrown = true;
                throw new InvalidOperationException("handler failed");
            };
            _driver.RaiseBinary(_codec.Encode(Content(1)));
            _driver.RaiseBinary(_codec.Encode(Content(2)));
            Assert.Throws<InvalidOperationException>(() => _conn.Poll(), "处理器的异常照旧抛给调用方");

            _conn.Poll();
            Assert.That(_messages.ConvertAll(m => ((MessageContent)m).Id), Is.EqualTo(new List<ulong> { 1, 2 }),
                "抛异常的那一帧已经交出去,不再重复派发;排在它后面的帧照常派发");
        }

        [Test]
        public void CloseForResume_ClosesTheSocketWithoutEvents()
        {
            Open();
            _conn.CloseForResume();
            _driver.RaiseBinary(_codec.Encode(Content(1)));
            _conn.Poll();
            Assert.That(_driver.DisposeCalls, Is.GreaterThan(0));
            Assert.That(_conn.Connected, Is.False);
            Assert.That(_messages, Is.Empty);
            Assert.That(_disconnects, Is.Zero, "调用方紧接着自己走断线恢复,这里不再派发");
        }

        [Test]
        public void MissingServerUrl_FallsBackToDirectOnlyInDevelopmentBuilds()
        {
            Assert.That(WebSocketFramedConnection.ResolveUrl("10.0.0.1", 18000, WsUrl, false), Is.EqualTo(WsUrl));
            Assert.That(WebSocketFramedConnection.ResolveUrl("127.0.0.1", 18000, null, true), Is.EqualTo("ws://127.0.0.1:18000/"));
            Assert.Throws<InvalidOperationException>(() => WebSocketFramedConnection.ResolveUrl("127.0.0.1", 18000, "", false));

            // 正式包:服务端没下发地址(这台节点不接小游戏)就连接失败,不去猜一个平台不放行的地址。
            Assert.Throws<InvalidOperationException>(() => _conn.Connect("203.0.113.9", 30000, null));
            Assert.That(_driver.Url, Is.Null, "没有发起连接");

            NewConnection(allowDirectUrl: true);
            _conn.Connect("127.0.0.1", 18000);
            Assert.That(_driver.Url, Is.EqualTo("ws://127.0.0.1:18000/"), "开发包直连本机 gate 的客户端端口");
        }
    }
}
