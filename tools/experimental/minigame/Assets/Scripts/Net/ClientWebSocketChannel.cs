#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace MmorpgClient.Net
{
    /// <summary>
    /// 编辑器 / Standalone 下的 WebSocket 通道(System.Net.WebSockets.ClientWebSocket)。
    /// 用途是在 PC 上先把"经 WebSocket 登录进场景"跑通,再换到小游戏里的 jslib 通道;
    /// WebGL 没有 System.Net.WebSockets,所以整个文件在 WebGL 播放器里不参与编译。
    ///
    /// 收发都在线程池上跑,事件经并发队列交给主线程的 <see cref="TryDequeue"/>。
    /// ClientWebSocket 同一时刻只允许一个 SendAsync,所以发送走单独的串行循环。
    /// </summary>
    public sealed class ClientWebSocketChannel : IWebSocketChannel
    {
        public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

        private readonly ConcurrentQueue<WebSocketEvent> _events = new();
        private readonly ConcurrentQueue<byte[]> _outbox = new();
        private readonly SemaphoreSlim _outboxSignal = new(0);
        private readonly CancellationTokenSource _cts = new();
        private ClientWebSocket _socket;
        private int _closedFired;
        private int _disposed;

        public void Open(string url)
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(ClientWebSocketChannel));
            if (_socket != null) throw new InvalidOperationException("channel already opened");
            _socket = new ClientWebSocket();
            var uri = new Uri(url);
            _ = Task.Run(() => RunAsync(uri));
        }

        public void Send(byte[] message)
        {
            if (Volatile.Read(ref _disposed) != 0 || _socket == null) throw new InvalidOperationException("not connected");
            _outbox.Enqueue(message);
            _outboxSignal.Release();
        }

        public bool TryDequeue(out WebSocketEvent ev) => _events.TryDequeue(out ev);

        private async Task RunAsync(Uri uri)
        {
            var token = _cts.Token;
            try
            {
                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    connectCts.CancelAfter(ConnectTimeout);
                    await _socket.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);
                }
                _events.Enqueue(new WebSocketEvent(WebSocketEventKind.Opened));
                var sender = SendLoopAsync(token);
                await ReceiveLoopAsync(token).ConfigureAwait(false);
                _cts.Cancel();
                try { await sender.ConfigureAwait(false); } catch { }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    _events.Enqueue(new WebSocketEvent(WebSocketEventKind.Error, text: ex.GetBaseException().Message));
                PostClosed("connect/io failed");
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[64 * 1024];
            using var message = new MemoryStream();
            while (!token.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    var reason = $"{(int?)result.CloseStatus} {result.CloseStatusDescription}";
                    try
                    {
                        await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch { }
                    PostClosed(reason);
                    return;
                }
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                if (result.MessageType == WebSocketMessageType.Binary)
                    _events.Enqueue(new WebSocketEvent(WebSocketEventKind.Binary, message.ToArray()));
                else
                    _events.Enqueue(new WebSocketEvent(WebSocketEventKind.Error, text: "ignored text message"));
                message.SetLength(0);
            }
        }

        private async Task SendLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await _outboxSignal.WaitAsync(token).ConfigureAwait(false);
                    if (!_outbox.TryDequeue(out var frame)) continue;
                    await _socket.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    _events.Enqueue(new WebSocketEvent(WebSocketEventKind.Error, text: "send: " + ex.GetBaseException().Message));
                // 发送失败的连接不可再用;中止后接收循环随之结束并报告关闭
                try { _socket.Abort(); } catch { }
            }
        }

        private void PostClosed(string reason)
        {
            if (Interlocked.Exchange(ref _closedFired, 1) == 0)
                _events.Enqueue(new WebSocketEvent(WebSocketEventKind.Closed, text: reason));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            var socket = _socket;
            if (socket != null && socket.State == WebSocketState.Open)
            {
                // 先发正常关闭帧(桥据此及时关掉 TCP),发完再取消收发循环:
                // 取消挂起中的 ReceiveAsync 会直接把 socket 置为 Aborted,关闭帧就发不出去了。不等结果。
                _ = socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "client dispose", CancellationToken.None)
                    .ContinueWith(t => { _ = t.Exception; _cts.Cancel(); socket.Dispose(); }, TaskScheduler.Default);
                return;
            }
            _cts.Cancel();
            if (socket == null) return;
            try { socket.Abort(); } catch { }
            socket.Dispose();
        }
    }
}
#endif
