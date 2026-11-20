using System;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AOT;
#else
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
#endif

namespace MmorpgClient.Net
{
    /// <summary><see cref="IWebSocketDriver"/> 的生产实现,按平台二选一。</summary>
    internal static class WebSocketDrivers
    {
        public static IWebSocketDriver Create() =>
#if UNITY_WEBGL && !UNITY_EDITOR
            new JsWebSocketDriver();
#else
            new ManagedWebSocketDriver();
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    /// <summary>
    /// 小游戏 / 浏览器:经 Assets/Plugins/WebGL/MmorpgMiniGame.jslib 调宿主的 WebSocket
    /// (微信 wx.connectSocket、抖音 tt.connectSocket,都没有时用浏览器的 WebSocket),不依赖两家的 Unity SDK。
    /// JS 回调都在主线程上到达。
    /// </summary>
    internal sealed class JsWebSocketDriver : IWebSocketDriver
    {
        // 回调用专门声明的委托类型,不用 Action<>:泛型委托交给原生侧在各版本 IL2CPP 上表现不一。
        private delegate void OpenCallback(int id);
        private delegate void BinaryCallback(int id, IntPtr data, int length);
        private delegate void ClosedCallback(int id, string reason);

        [DllImport("__Internal")]
        private static extern int MmorpgWebSocketOpen(string url, OpenCallback onOpen, BinaryCallback onBinary, ClosedCallback onClosed);
        [DllImport("__Internal")] private static extern void MmorpgWebSocketSend(int id, byte[] data, int length);
        [DllImport("__Internal")] private static extern void MmorpgWebSocketClose(int id);

        // JS 回调只带连接号,按号找回实例。同一时刻至多三条(大厅、战斗、观战或测速)。
        private static readonly Dictionary<int, JsWebSocketDriver> Live = new();

        private int _id;
        private Action _onOpen;
        private Action<byte[]> _onBinary;
        private Action<string> _onClosed;

        public void Open(string url, Action onOpen, Action<byte[]> onBinary, Action<string> onClosed)
        {
            _onOpen = onOpen;
            _onBinary = onBinary;
            _onClosed = onClosed;
            // JS 侧的事件都在本次调用返回之后才到(jslib 保证),先拿号再登记不会漏事件。
            _id = MmorpgWebSocketOpen(url, Opened, Binary, Closed);
            Live[_id] = this;
        }

        public void Send(byte[] message)
        {
            if (Live.ContainsKey(_id)) MmorpgWebSocketSend(_id, message, message.Length);
        }

        public void Dispose()
        {
            // 从表里摘掉之后,JS 侧迟到的事件找不到实例,不再回调。
            if (Live.Remove(_id)) MmorpgWebSocketClose(_id);
        }

        [MonoPInvokeCallback(typeof(OpenCallback))]
        private static void Opened(int id)
        {
            if (Live.TryGetValue(id, out var driver)) driver._onOpen?.Invoke();
        }

        [MonoPInvokeCallback(typeof(BinaryCallback))]
        private static void Binary(int id, IntPtr data, int length)
        {
            if (!Live.TryGetValue(id, out var driver)) return;
            var bytes = new byte[length];
            if (length > 0) Marshal.Copy(data, bytes, 0, length);
            driver._onBinary?.Invoke(bytes);
        }

        [MonoPInvokeCallback(typeof(ClosedCallback))]
        private static void Closed(int id, string reason)
        {
            if (!Live.Remove(id, out var driver)) return;
            driver._onClosed?.Invoke(reason ?? string.Empty);
        }
    }
#else
    /// <summary>
    /// 其它平台:System.Net.WebSockets.ClientWebSocket。生产包不走这里(原生包用 TCP),
    /// 只给编辑器加 MMORPG_FORCE_WS 联调用。收发在线程池上,回调因此在别的线程到达。
    /// </summary>
    internal sealed class ManagedWebSocketDriver : IWebSocketDriver
    {
        private readonly ClientWebSocket _socket = new();
        private readonly CancellationTokenSource _cancel = new();
        // 只在主线程追加:按调用顺序串成一条链。ClientWebSocket 同一时刻只许一个发送,帧的先后也不能乱。
        private Task _sendChain = Task.CompletedTask;
        private Action<string> _onClosed;
        private int _closedReported;
        private int _disposed;

        public void Open(string url, Action onOpen, Action<byte[]> onBinary, Action<string> onClosed)
        {
            var uri = new Uri(url); // 地址不合法在这里同步抛,调用方按连接失败处理
            _onClosed = onClosed;
            _ = Task.Run(() => RunAsync(uri, onOpen, onBinary));
        }

        public void Send(byte[] message)
        {
            _sendChain = _sendChain.ContinueWith(_ => SendOne(message), TaskScheduler.Default).Unwrap();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _cancel.Cancel(); } catch (ObjectDisposedException) { }
            _socket.Abort();
            _socket.Dispose();
        }

        private async Task RunAsync(Uri uri, Action onOpen, Action<byte[]> onBinary)
        {
            string reason = string.Empty;
            try
            {
                await _socket.ConnectAsync(uri, _cancel.Token).ConfigureAwait(false);
                onOpen();
                var chunk = new byte[16 * 1024];
                using var message = new MemoryStream();
                while (_socket.State == WebSocketState.Open)
                {
                    var result = await _socket.ReceiveAsync(new ArraySegment<byte>(chunk), _cancel.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    message.Write(chunk, 0, result.Count);
                    if (!result.EndOfMessage) continue;
                    // 文本消息不是本协议的内容,丢弃。
                    if (result.MessageType == WebSocketMessageType.Binary) onBinary(message.ToArray());
                    message.SetLength(0);
                }
            }
            catch (Exception ex) { reason = ex.Message; }
            ReportClosed(reason);
        }

        private async Task SendOne(byte[] message)
        {
            try
            {
                await _socket.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Binary, true, _cancel.Token).ConfigureAwait(false);
            }
            catch (Exception ex) { ReportClosed(ex.Message); }
        }

        private void ReportClosed(string reason)
        {
            // 本端 Dispose 之后不再回调;对端关闭、读写失败只报一次。
            if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _closedReported, 1) != 0) return;
            _onClosed?.Invoke(reason ?? string.Empty);
        }
    }
#endif
}
