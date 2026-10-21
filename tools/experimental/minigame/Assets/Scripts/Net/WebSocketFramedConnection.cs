using System;
using System.Collections.Generic;
using Google.Protobuf;

namespace MmorpgClient.Net
{
    /// <summary>
    /// 经 WebSocket 承载 muduo 帧的 <see cref="IFramedConnection"/>,给 WebGL / 微信、抖音小游戏用
    /// (它们没有原始 TCP)。服务端前面有一层 WebSocket↔TCP 桥(本机联调用 tools/ws-bridge),
    /// gate / battle 看到的字节与 TCP 客户端完全一样。
    ///
    /// 线协议:上行一条 WebSocket 二进制消息 = 一个完整 muduo 帧;下行按字节流累积后用
    /// <see cref="MuduoCodec.TryDecode"/> 切帧,所以不依赖桥把帧对齐到消息边界。
    ///
    /// 与 <see cref="GateTcpClient"/> 的语义差异只有一处:<see cref="Connect"/> 不阻塞,
    /// 连接在之后某次 <see cref="Poll"/> 里才真正建好。建好之前 <see cref="Connected"/> 为 false,
    /// 这期间 <see cref="Send"/> 的帧先缓存,连上后按原顺序发出(GameClient 连上就立刻发验票包);
    /// 连不上则在 Poll 里派发 <see cref="OnDisconnected"/>,不抛异常。
    /// </summary>
    public sealed class WebSocketFramedConnection : IFramedConnection
    {
        /// <summary>连上之前最多缓存的帧数,与 GateTcpClient 的出站上限一致。</summary>
        public const int MaxPendingFrames = GateTcpClient.MaxOutboxFrames;

        private enum State { Idle, Connecting, Open, Closed }

        private readonly MuduoCodec _codec;
        private readonly IWebSocketChannel _channel;
        private readonly Func<string, int, string> _urlFor;
        private readonly Queue<byte[]> _pending = new();
        private byte[] _inbound = new byte[16 * 1024];
        private int _filled;
        private State _state;
        private bool _disposed;
        private bool _disconnectFired;

        public event Action<IMessage> OnMessage;
        public event Action<string> OnError;
        public event Action OnDisconnected;

        /// <param name="urlFor">把服务端下发的 host:port 映射成 WebSocket 地址(见 <see cref="WebSocketUrl"/>)。</param>
        public WebSocketFramedConnection(MuduoCodec codec, IWebSocketChannel channel, Func<string, int, string> urlFor)
        {
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _urlFor = urlFor ?? throw new ArgumentNullException(nameof(urlFor));
        }

        public bool Connected => _state == State.Open && !_disposed;

        /// <summary>WebSocket 地址;Connect 之前为 null。只用于日志。</summary>
        public string Url { get; private set; }

        public void Connect(string host, int port)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WebSocketFramedConnection));
            if (_state != State.Idle) throw new InvalidOperationException("connection already started");
            Url = _urlFor(host, port);
            _state = State.Connecting;
            _channel.Open(Url);
        }

        public void Send(IMessage message)
        {
            if (_disposed || _state == State.Idle || _state == State.Closed)
                throw new InvalidOperationException("not connected");
            var frame = _codec.Encode(message);
            if (_state == State.Open)
            {
                _channel.Send(frame);
                return;
            }
            if (_pending.Count >= MaxPendingFrames)
            {
                // 与 GateTcpClient 出站满时一致:报错并断开,不让内存无界增长
                OnError?.Invoke("pending queue full before websocket opened -- disconnecting");
                CloseLocally("pending queue full");
                return;
            }
            _pending.Enqueue(frame);
        }

        /// <summary>主线程每帧调用;所有事件只在这里派发。</summary>
        public void Poll()
        {
            // _disposed 检查与 GateTcpClient.Poll 相同:回调里 Dispose 了本连接(重定向换连接),
            // 后面排着的事件必须立刻停止派发。
            while (!_disposed && _state != State.Closed && _channel.TryDequeue(out var ev))
            {
                switch (ev.Kind)
                {
                    case WebSocketEventKind.Opened:
                        HandleOpened();
                        break;
                    case WebSocketEventKind.Binary:
                        HandleBinary(ev.Data);
                        break;
                    case WebSocketEventKind.Error:
                        OnError?.Invoke("websocket: " + ev.Text);
                        break;
                    case WebSocketEventKind.Closed:
                        _state = State.Closed;
                        FireDisconnected();
                        break;
                }
            }
        }

        private void HandleOpened()
        {
            if (_state != State.Connecting) return;
            _state = State.Open;
            while (!_disposed && _state == State.Open && _pending.Count > 0)
                _channel.Send(_pending.Dequeue());
        }

        private void HandleBinary(byte[] data)
        {
            if (data == null || data.Length == 0) return;
            EnsureCapacity(_filled + data.Length);
            Buffer.BlockCopy(data, 0, _inbound, _filled, data.Length);
            _filled += data.Length;

            int offset = 0;
            while (!_disposed && _state == State.Open)
            {
                IMessage msg;
                int consumed;
                try
                {
                    msg = _codec.TryDecode(_inbound, offset, _filled - offset, out consumed);
                }
                catch (Exception ex)
                {
                    // 与 GateTcpClient 读线程一致:解码失败视为连接损坏,报错后断开
                    OnError?.Invoke($"decode error: {ex.Message}");
                    CloseLocally("decode error");
                    return;
                }
                if (consumed == 0) break;
                offset += consumed;
                if (msg != null) OnMessage?.Invoke(msg);
            }
            if (offset > 0 && !_disposed)
            {
                Buffer.BlockCopy(_inbound, offset, _inbound, 0, _filled - offset);
                _filled -= offset;
            }
        }

        private void EnsureCapacity(int needed)
        {
            if (needed <= _inbound.Length) return;
            int size = _inbound.Length;
            while (size < needed) size *= 2;
            var bigger = new byte[size];
            Buffer.BlockCopy(_inbound, 0, bigger, 0, _filled);
            _inbound = bigger;
        }

        private void CloseLocally(string reason)
        {
            if (_state == State.Closed) return;
            _state = State.Closed;
            _pending.Clear();
            try { _channel.Dispose(); } catch { }
            FireDisconnected();
        }

        private void FireDisconnected()
        {
            if (_disconnectFired || _disposed) return;
            _disconnectFired = true;
            OnDisconnected?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _state = State.Closed;
            _pending.Clear();
            _channel.Dispose();
        }
    }

    /// <summary>
    /// 把服务端下发的 host:port 映射成 WebSocket 地址。模板里的占位符:
    /// {host}、{port}、{label}(连接用途:gate / battle / probe,桥日志据此区分会话)。
    /// 例:本机桥 <c>ws://127.0.0.1:9100/ws?target={host}:{port}&amp;label={label}</c>;
    /// 生产可以是 <c>wss://gate-{host}.example.com/ws</c> 这类按节点映射的域名。
    /// </summary>
    public static class WebSocketUrl
    {
        public static string Format(string template, string host, int port, string label)
        {
            if (string.IsNullOrEmpty(template)) throw new ArgumentException("websocket url template is empty");
            return template
                .Replace("{host}", Uri.EscapeDataString(host ?? string.Empty))
                .Replace("{port}", port.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Replace("{label}", Uri.EscapeDataString(label ?? string.Empty));
        }
    }
}
