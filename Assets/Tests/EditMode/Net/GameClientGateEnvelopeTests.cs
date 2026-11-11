using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Google.Protobuf;
using MmorpgClient.Game;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Net
{
    /// <summary>
    /// gate 级失败回执与重定向的客户端处理(服务端仓 docs/design/client-request-ordering.md O4 / O6 / O9、§4.6、§4.7)。
    /// gate 拒绝一条请求时回 {id = 请求号, message_id = 原请求号, error_message = 码, body 为空} 的错误信封;
    /// 单向请求(应答为 Empty 的移动号等)请求号是 0,信封也带 0。客户端要做到:
    ///  - 在途调用按信封失败(ServerTip),应答类型为 Empty 时也不把空 body 当成功;
    ///  - 认领不到的信封与单向请求的信封只记日志,不抛异常、不交给推送处理器当数据用;
    ///  - gate 推 msg 124 之前给排队请求回的 1003 照常按请求号收口;124 之后不再往旧 gate 上发请求,
    ///    当场以 NotSent 失败(确定未执行,可在新连接上重试)。
    /// 用真实回环 TCP 与正式编解码,夹具写法同 GameClientReconnectTests。
    /// </summary>
    public sealed class GameClientGateEnvelopeTests
    {
        private const uint ServiceUnavailable = (uint)common_error.KServiceUnavailable;
        private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameClient _game;
        private GateTcpClient _gate;
        private TcpListener _listener;
        private TcpClient _peer;
        private MuduoCodec _peerCodec;
        private readonly List<IEnumerator> _routines = new();
        private readonly List<string> _logs = new();

        [SetUp]
        public void SetUp()
        {
            _routines.Clear(); _logs.Clear();
            // 本组用例不访问网关 HTTP,给一个不会被用到的地址即可。
            _game = new GameClient("http://127.0.0.1:1");
            _game.CoroutineRunner = routine => { _routines.Add(routine); return null; };
            _game.OnLog += _logs.Add;
            SetProperty("PlayerId", 42ul); SetProperty("InGame", true); SetProperty("TokenVerified", true);
            _listener = new TcpListener(IPAddress.Loopback, 0); _listener.Start();
            _gate = new GateTcpClient((MuduoCodec)Get("_codec"));
            _gate.Connect("127.0.0.1", ((IPEndPoint)_listener.LocalEndpoint).Port);
            _peer = _listener.AcceptTcpClient();
            _peer.ReceiveTimeout = 3000;
            _peerCodec = new MuduoCodec();
            _peerCodec.Register<ClientRequest>(); _peerCodec.Register<MessageContent>();
            Set("_gate", _gate); Set("_disconnectNotificationSent", false);
            // 与 ConnectAndEnter 挂的是同一个方法委托:RedirectFlow 的 `OnMessage -= DispatchInbound` 才摘得掉它。
            _gate.OnMessage += (Action<IMessage>)typeof(GameClient).GetMethod("DispatchInbound", Hidden)
                .CreateDelegate(typeof(Action<IMessage>), _game);
            _gate.OnDisconnected += (Action)typeof(GameClient).GetMethod("HandleTransportDisconnected", Hidden)
                .CreateDelegate(typeof(Action), _game);
        }

        [TearDown]
        public void TearDown()
        {
            _game?.Disconnect();
            foreach (var routine in _routines) (routine as IDisposable)?.Dispose();
            _gate?.Dispose(); _peer?.Dispose(); _listener?.Stop();
            if (_game?.World.Root != null) UnityEngine.Object.DestroyImmediate(_game.World.Root.gameObject);
        }

        [Test]
        public void EnvelopeErrorFailsOutstandingCallEvenWhenResponseTypeIsEmpty()
        {
            var results = new List<RpcStatus>();
            _game.CallMethod(MessageIds.CancelQueue, new global::Match.CancelQueueRequest(), global::Empty.Parser,
                (status, _) => results.Add(status));
            var rejected = ReadRequest();
            Assert.That(rejected.Id, Is.Not.Zero);
            PeerSend(Envelope(rejected.Id, rejected.MessageId, ServiceUnavailable));
            PollUntil(() => results.Count == 1, "envelope completion");
            Assert.That(results[0].Code, Is.EqualTo(RpcStatusCode.ServerTip));
            Assert.That(results[0].TipId, Is.EqualTo(ServiceUnavailable));

            // 对照:同一应答类型的成功回包同样是空 body,只有没有信封错误时才算成功。
            _game.CallMethod(MessageIds.CancelQueue, new global::Match.CancelQueueRequest(), global::Empty.Parser,
                (status, _) => results.Add(status));
            var accepted = ReadRequest();
            PeerSend(new MessageContent { Id = accepted.Id, MessageId = accepted.MessageId });
            PollUntil(() => results.Count == 2, "empty success");
            Assert.That(results[1].IsOk, Is.True);
        }

        [Test]
        public void EnvelopeForUnregisteredCallIdIsDroppedAndNeverReachesPushHandlers()
        {
            bool pushed = false;
            _game.OnNotify(MessageIds.CancelQueue, _ => pushed = true);
            var calls = (OutstandingCalls)Get("_calls");

            PeerSend(Envelope(4242, MessageIds.CancelQueue, ServiceUnavailable));
            PollUntil(() => calls.LateReplies == 1, "late envelope");

            Assert.That(pushed, Is.False, "带请求号的信封只能认领在途调用,不能当推送派发");
            Assert.That(_logs.Exists(line => line.Contains("late reply dropped id=4242")), Is.True);
        }

        [Test]
        public void OneWayMovementEnvelopeIsOnlyLogged()
        {
            _game.SendMoveSync(UnityEngine.Vector3.zero, UnityEngine.Vector3.zero, UnityEngine.Vector3.zero);
            var move = ReadRequest();
            Assert.That(move.Id, Is.Zero, "单向请求不登记在途,请求号填 0");
            Assert.That(move.MessageId, Is.EqualTo(MessageIds.MoveSync));

            // scene 未就绪 / 场景链路不受理 / LeaveGame 屏障:gate 对应答为 Empty 的移动号同样回信封。
            PeerSend(Envelope(0, MessageIds.MoveSync, ServiceUnavailable));
            PollUntil(() => _logs.Exists(line => line.Contains("MoveSync rejected: tip=1003")), "movement rejection log");
            Assert.That(_game.MoveAckCount, Is.Zero);
        }

        [Test]
        public void OneWayEnvelopeWithoutHandlerIsLoggedAsRejection()
        {
            const uint unregistered = 999_999;
            Assert.That(((IDictionary)Get("_notifyHandlers")).Contains(unregistered), Is.False);

            PeerSend(Envelope(0, unregistered, ServiceUnavailable));
            PollUntil(() => _logs.Exists(line => line.Contains("one-way request rejected message_id=999999 tip=1003")),
                "one-way rejection log");
        }

        [Test]
        public void RedirectCompletesQueuedRequestsThenStopsSendingOnOldGate()
        {
            var results = new List<RpcStatus>();
            _game.CallMethod(MessageIds.CancelQueue, new global::Match.CancelQueueRequest(), global::Empty.Parser,
                (status, _) => results.Add(status));
            _game.CallMethod(MessageIds.CancelQueue, new global::Match.CancelQueueRequest(), global::Empty.Parser,
                (status, _) => results.Add(status));
            var first = ReadRequest();
            var second = ReadRequest();
            Assert.That(_game.IsGateReady, Is.True);

            // gate 推 124 之前,把还排在 lane 里的请求按收包顺序逐条回带请求号的 1003(O9),之后才推 124。
            PeerSend(Envelope(first.Id, first.MessageId, ServiceUnavailable));
            PeerSend(Envelope(second.Id, second.MessageId, ServiceUnavailable));
            PeerSend(new MessageContent
            {
                MessageId = MessageIds.RedirectToGate,
                SerializedMessage = new RedirectToGateNotify { TargetIp = "127.0.0.1", TargetPort = 1 }.ToByteString(),
            });
            PollUntil(() => _routines.Count == 1, "redirect flow");
            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results.TrueForAll(s => s.Code == RpcStatusCode.ServerTip && s.TipId == ServiceUnavailable), Is.True);

            // Unity 的 StartCoroutine 同步跑到第一个 yield:摘掉旧连接的派发、记为已搬走,然后才去探目标 gate。
            Assert.That(_routines[0].MoveNext(), Is.True);
            Assert.That(_game.IsRedirecting, Is.True);
            Assert.That(_game.IsGateReady, Is.False, "msg 124 之后的旧连接不算就绪");

            RpcStatus late = default;
            bool completed = false;
            _game.CallMethod(MessageIds.CancelQueue, new global::Match.CancelQueueRequest(), global::Empty.Parser,
                (status, _) => { late = status; completed = true; });
            Assert.That(completed, Is.True, "发往已搬走的旧 gate 的请求要当场失败");
            Assert.That(late.Code, Is.EqualTo(RpcStatusCode.NotSent));
            Assert.That(((OutstandingCalls)Get("_calls")).Count, Is.Zero, "不登记成在途,不会拖到换连接时变成结果未知");

            _game.SendMoveSync(UnityEngine.Vector3.zero, UnityEngine.Vector3.zero, UnityEngine.Vector3.zero);
            Thread.Sleep(50);
            Assert.That(_peer.Available, Is.Zero, "旧 gate 不应再收到任何请求");
        }

        private ClientRequest ReadRequest()
        {
            var stream = _peer.GetStream();
            byte[] ReadExactly(int count)
            {
                var data = new byte[count];
                for (int offset = 0; offset < count;)
                {
                    int n = stream.Read(data, offset, count - offset);
                    if (n == 0) throw new EndOfStreamException();
                    offset += n;
                }
                return data;
            }
            byte[] header = ReadExactly(4);
            int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            Assert.That(length, Is.InRange(1, 65536));
            var frame = new byte[length + 4];
            Buffer.BlockCopy(header, 0, frame, 0, 4);
            Buffer.BlockCopy(ReadExactly(length), 0, frame, 4, length);
            return (ClientRequest)_peerCodec.TryDecode(frame, 0, frame.Length, out _);
        }

        private void PeerSend(IMessage message)
        {
            byte[] bytes = _peerCodec.Encode(message);
            _peer.GetStream().Write(bytes, 0, bytes.Length);
        }

        private static MessageContent Envelope(ulong id, uint messageId, uint tip) =>
            new MessageContent { Id = id, MessageId = messageId, ErrorMessage = new TipInfoMessage { Id = tip } };

        /// <summary>派发旧连接已收到的帧,直到条件成立;读线程是异步的,所以有界轮询(单调时钟,3 秒)。</summary>
        private void PollUntil(Func<bool> condition, string what)
        {
            var clock = Stopwatch.StartNew();
            while (true)
            {
                _gate.Poll();
                if (condition()) return;
                Assert.That(clock.ElapsedMilliseconds, Is.LessThan(3000), "timed out waiting for " + what);
                Thread.Sleep(2);
            }
        }

        private object Get(string field) => typeof(GameClient).GetField(field, Hidden).GetValue(_game);
        private void Set(string field, object value) => typeof(GameClient).GetField(field, Hidden).SetValue(_game, value);
        private void SetProperty(string property, object value) => Set("<" + property + ">k__BackingField", value);
    }
}
