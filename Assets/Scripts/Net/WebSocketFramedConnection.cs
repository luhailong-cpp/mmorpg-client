using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Google.Protobuf;

namespace MmorpgClient.Net
{
    /// <summary>
    /// 一条 WebSocket 的收发面,<see cref="WebSocketFramedConnection"/> 只经它碰底层。
    /// 两个生产实现见 WebSocketDrivers.cs:小游戏 / 浏览器(JS 桥)与其它平台(ClientWebSocket,编辑器联调用);测试注入假实现。
    /// </summary>
    public interface IWebSocketDriver : IDisposable
    {
        /// <summary>
        /// 开始建连,立即返回。三个回调可以在任意线程上到达(WebGL 在主线程,其它平台在线程池),调用方只把它们入队。
        /// 连不上、对端关闭、出错都报 onClosed(带原因,可为空串),至多一次;本端 Dispose 之后不再回调。
        /// </summary>
        void Open(string url, Action onOpen, Action<byte[]> onBinary, Action<string> onClosed);

        /// <summary>发一条二进制消息。只在 onOpen 之后调用。</summary>
        void Send(byte[] message);
    }

    /// <summary>
    /// 走 WebSocket 的分帧连接:微信 / 抖音小游戏连 gate 与 battle 用(服务端 docs/design/minigame-websocket-gate.md W3、W10)。
    /// 一条二进制消息承载一个客户端帧,字节与 <see cref="GateTcpClient"/> 在 TCP 上发的完全相同,编解码共用 <see cref="MuduoCodec"/>。
    ///
    /// 与 TCP 实现的差别只在时序:
    ///  - Connect 立即返回。打开之前 <see cref="Connected"/> 为假,发出的帧先排队、打开时按序发出;
    ///    <see cref="OpenTimeoutMs"/> 内没打开或打开失败,按断线报(OnDisconnected)。
    ///  - 没有后台线程(WebGL 没有线程):心跳在 <see cref="Poll"/> 里发。主线程长时间不 Poll,心跳就停,
    ///    gate 按空闲超时关掉连接,之后走正常的断线恢复。小游戏切到后台同理。
    /// 除了驱动回调入队的那一条队列,所有成员只在主线程访问。
    /// </summary>
    public sealed class WebSocketFramedConnection : IFramedConnection
    {
        /// <summary>从 Connect 到打开的期限。</summary>
        public const double OpenTimeoutMs = 10_000;

        /// <summary>打开之前最多排多少帧。握手只有验票一帧,排到这个数说明调用方在连上之前就开始发业务请求了。</summary>
        public const int MaxPendingFrames = 64;

        private enum InboundKind { Opened, Binary, Closed }

        private readonly struct Inbound
        {
            public readonly InboundKind Kind;
            public readonly byte[] Bytes;
            public readonly string Reason;

            public Inbound(InboundKind kind, byte[] bytes = null, string reason = null)
            {
                Kind = kind;
                Bytes = bytes;
                Reason = reason;
            }
        }

        private readonly MuduoCodec _codec;
        private readonly IWebSocketDriver _driver;
        private readonly Func<double> _nowMs;
        private readonly bool _allowDirectUrl;
        // 驱动回调(可能在别的线程)只往这里入队,Poll 在主线程出队。
        private readonly ConcurrentQueue<Inbound> _inbound = new();
        private readonly Queue<byte[]> _pendingFrames = new();
        private readonly GateHeartbeat _heartbeat = new();
        // 收到的字节按流拼起来再解:服务端一条消息一个帧,但协议不靠这一点(同 TCP)。
        private byte[] _buffer = new byte[4096];
        private int _filled;
        private bool _started;
        private bool _open;
        private bool _closed;   // 断线已经派发(或被 CloseForResume 收掉)
        private bool _disposed;
        private double _openDeadlineMs;

        public event Action<IMessage> OnMessage;
        public event Action<string> OnError;
        public event Action OnDisconnected;

        public bool Connected => _open && !_closed && !_disposed;
        public double? HeartbeatRoundTripMs => _heartbeat.RoundTripMs;

        public WebSocketFramedConnection(MuduoCodec codec)
            : this(codec, WebSocketDrivers.Create(), null, UnityEngine.Debug.isDebugBuild) { }

        /// <param name="driver">底层 WebSocket;本对象负责 Dispose。</param>
        /// <param name="nowMs">单调时钟毫秒;空 = Stopwatch。测试传可拨的时钟。</param>
        /// <param name="allowDirectUrl">服务端没下发 WebSocket 地址时,是否回落到直连 ws://host:port/(只给开发包)。</param>
        public WebSocketFramedConnection(MuduoCodec codec, IWebSocketDriver driver, Func<double> nowMs, bool allowDirectUrl)
        {
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _nowMs = nowMs ?? (() => Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency));
            _allowDirectUrl = allowDirectUrl;
        }

        public void Connect(string host, int port) => Connect(host, port, null);

        public void Connect(string host, int port, string wsUrl)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WebSocketFramedConnection));
            if (_started) throw new InvalidOperationException("connection already started");
            string url = ResolveUrl(host, port, wsUrl, _allowDirectUrl);
            _started = true;
            _openDeadlineMs = _nowMs() + OpenTimeoutMs;
            _driver.Open(url,
                () => _inbound.Enqueue(new Inbound(InboundKind.Opened)),
                bytes => _inbound.Enqueue(new Inbound(InboundKind.Binary, bytes)),
                reason => _inbound.Enqueue(new Inbound(InboundKind.Closed, reason: reason)));
        }

        /// <summary>
        /// 小游戏平台只许连后台登记过的 wss 域名,所以正式包只用服务端下发的地址;没下发 = 这台节点不接小游戏,不猜。
        /// 开发包(编辑器、Development Build)回落到直连:本机没有边缘,gate / battle 的客户端端口自己就接 WebSocket。
        /// </summary>
        public static string ResolveUrl(string host, int port, string wsUrl, bool allowDirectUrl)
        {
            if (!string.IsNullOrEmpty(wsUrl)) return wsUrl;
            if (allowDirectUrl && !string.IsNullOrEmpty(host) && port > 0 && port <= 65535) return $"ws://{host}:{port}/";
            throw new InvalidOperationException("the server did not provide a WebSocket address for this node");
        }

        public void Send(IMessage message)
        {
            if (!_started || _closed || _disposed) throw new InvalidOperationException("not connected");
            byte[] frame = _codec.Encode(message);
            if (_open) { _driver.Send(frame); return; }
            if (_pendingFrames.Count >= MaxPendingFrames)
                throw new InvalidOperationException("too many frames queued before the WebSocket opened");
            _pendingFrames.Enqueue(frame);
        }

        public void Poll()
        {
            if (!_started) return;
            double now = _nowMs();
            // 每派发一次事件就重查 _disposed / _closed:处理器可能在回调里换掉或关掉这条连接(重定向),
            // 排在后面的残留消息不能再派发(同 GateTcpClient.Poll)。
            while (!_disposed && !_closed && _inbound.TryDequeue(out var item))
            {
                switch (item.Kind)
                {
                    case InboundKind.Opened:
                        _open = true;
                        while (_pendingFrames.Count > 0) _driver.Send(_pendingFrames.Dequeue());
                        break;
                    case InboundKind.Binary:
                        _heartbeat.NoteInbound(now);
                        Append(item.Bytes);
                        Decode(now);
                        break;
                    case InboundKind.Closed:
                        if (!string.IsNullOrEmpty(item.Reason)) OnError?.Invoke($"websocket: {item.Reason}");
                        Disconnect();
                        break;
                }
            }
            if (_disposed || _closed) return;

            if (!_open)
            {
                if (now >= _openDeadlineMs) Fail("websocket open timed out");
                return;
            }
            if (_heartbeat.Expired(now)) { Fail("gate heartbeat timed out"); return; }
            if (_heartbeat.TrySend(now, out ulong sequence))
                _driver.Send(_codec.Encode(new ClientHeartbeatRequest { Seq = sequence }));
        }

        public void CloseForResume()
        {
            _closed = true;
            _driver.Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _driver.Dispose();
        }

        private void Append(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;
            if (_filled + bytes.Length > _buffer.Length)
            {
                var bigger = new byte[Math.Max(_buffer.Length * 2, _filled + bytes.Length)];
                Buffer.BlockCopy(_buffer, 0, bigger, 0, _filled);
                _buffer = bigger;
            }
            Buffer.BlockCopy(bytes, 0, _buffer, _filled, bytes.Length);
            _filled += bytes.Length;
        }

        private void Decode(double now)
        {
            int offset = 0;
            try
            {
                while (!_disposed && !_closed)
                {
                    IMessage message;
                    int consumed;
                    try { message = _codec.TryDecode(_buffer, offset, _filled - offset, out consumed); }
                    catch (Exception ex) { Fail($"decode error: {ex.Message}"); return; }
                    if (consumed == 0) break;
                    offset += consumed;
                    // 验签通告与心跳应答由连接自己消化,与 GateTcpClient 的读线程同一口径。
                    try
                    {
                        if (message is ClientTokenVerifyResponse verify && verify.Success)
                            _heartbeat.Start(verify.HeartbeatIntervalMs, verify.HeartbeatTimeoutMs, now);
                        else if (message is BattleTokenVerifyResponse battleVerify && battleVerify.Success)
                            _heartbeat.Start(battleVerify.HeartbeatIntervalMs, battleVerify.HeartbeatTimeoutMs, now);
                    }
                    catch (ArgumentException ex) { Fail($"heartbeat: {ex.Message}"); return; }
                    if (message is ClientHeartbeatResponse heartbeat) _heartbeat.NoteResponse(heartbeat.Seq, now);
                    else if (message != null) OnMessage?.Invoke(message);
                }
            }
            finally
            {
                // 放在 finally:上层处理器抛异常时,已经交出去的帧也要从缓冲里移走。否则下一次收包会从头再解,
                // 同一帧反复派发、反复抛,排在它后面的帧永远轮不到(TCP 实现的帧出队即消费,没有这个问题)。
                if (offset > 0)
                {
                    Buffer.BlockCopy(_buffer, offset, _buffer, 0, _filled - offset);
                    _filled -= offset;
                }
            }
        }

        private void Fail(string error)
        {
            OnError?.Invoke(error);
            _driver.Dispose();
            Disconnect();
        }

        private void Disconnect()
        {
            if (_closed) return;
            _closed = true;
            _open = false;
            if (!_disposed) OnDisconnected?.Invoke();
        }
    }
}
