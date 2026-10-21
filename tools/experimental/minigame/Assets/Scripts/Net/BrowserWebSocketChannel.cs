#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MmorpgClient.Net
{
    /// <summary>
    /// WebGL / 微信、抖音小游戏里的 WebSocket 通道,底层是 Assets/Plugins/WebGL/MmorpgWebSocket.jslib:
    /// 浏览器用标准 WebSocket;小游戏里平台适配层通常也提供 WebSocket 全局对象,
    /// 没有时退到 wx.connectSocket / tt.connectSocket。
    ///
    /// JS 侧把事件排在每条连接自己的队列里,C# 在主线程轮询取出,不从 JS 回调进 C#
    /// (省掉 IL2CPP 下 MonoPInvokeCallback 的静态回调和跨连接分发)。
    /// </summary>
    public sealed class BrowserWebSocketChannel : IWebSocketChannel
    {
        private const int KindNone = -1, KindOpened = 0, KindBinary = 1, KindError = 2, KindClosed = 3;

        [DllImport("__Internal")] private static extern int MmoWsOpen(string url);
        [DllImport("__Internal")] private static extern int MmoWsSend(int id, byte[] data, int length);
        [DllImport("__Internal")] private static extern int MmoWsPeekKind(int id);
        [DllImport("__Internal")] private static extern int MmoWsPeekLength(int id);
        [DllImport("__Internal")] private static extern void MmoWsPop(int id, byte[] buffer, int capacity);
        [DllImport("__Internal")] private static extern void MmoWsClose(int id);

        private int _id;
        private bool _disposed;

        public void Open(string url)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BrowserWebSocketChannel));
            if (_id != 0) throw new InvalidOperationException("channel already opened");
            _id = MmoWsOpen(url);
        }

        public void Send(byte[] message)
        {
            if (_disposed || _id == 0) throw new InvalidOperationException("not connected");
            if (MmoWsSend(_id, message, message.Length) == 0) throw new InvalidOperationException("websocket not open");
        }

        public bool TryDequeue(out WebSocketEvent ev)
        {
            ev = default;
            if (_disposed || _id == 0) return false;
            int kind = MmoWsPeekKind(_id);
            if (kind == KindNone) return false;
            var payload = new byte[MmoWsPeekLength(_id)];
            MmoWsPop(_id, payload, payload.Length);
            switch (kind)
            {
                case KindOpened: ev = new WebSocketEvent(WebSocketEventKind.Opened); break;
                case KindBinary: ev = new WebSocketEvent(WebSocketEventKind.Binary, payload); break;
                case KindClosed: ev = new WebSocketEvent(WebSocketEventKind.Closed, text: Encoding.UTF8.GetString(payload)); break;
                default: ev = new WebSocketEvent(WebSocketEventKind.Error, text: Encoding.UTF8.GetString(payload)); break;
            }
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_id != 0) MmoWsClose(_id);
        }
    }
}
#endif
