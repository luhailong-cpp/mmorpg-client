using System;

namespace MmorpgClient.Net
{
    /// <summary>WebSocket 通道上发生的事件种类。</summary>
    public enum WebSocketEventKind
    {
        Opened,
        /// <summary>收到一条二进制消息(<see cref="WebSocketEvent.Data"/> 是完整消息体)。</summary>
        Binary,
        /// <summary>出错(<see cref="WebSocketEvent.Text"/> 是说明;只用于日志,断开另有 Closed)。</summary>
        Error,
        /// <summary>连接已关闭(<see cref="WebSocketEvent.Text"/> 是关闭码与原因;之后不会再有事件)。</summary>
        Closed,
    }

    public readonly struct WebSocketEvent
    {
        public readonly WebSocketEventKind Kind;
        public readonly byte[] Data;
        public readonly string Text;

        public WebSocketEvent(WebSocketEventKind kind, byte[] data = null, string text = null)
        {
            Kind = kind;
            Data = data;
            Text = text;
        }
    }

    /// <summary>
    /// 平台相关的 WebSocket 原始通道:只收发完整的二进制消息,不懂 muduo 分帧。
    ///
    /// 实现:编辑器 / Standalone 用 <see cref="ClientWebSocketChannel"/>(System.Net.WebSockets);
    /// WebGL / 微信、抖音小游戏用 BrowserWebSocketChannel(jslib,小游戏里由平台适配层映射到
    /// wx.connectSocket / tt.connectSocket)。测试注入假实现。
    ///
    /// 线程约定:所有方法都在主线程调用;实现可以在后台线程收数据,但事件只能经
    /// <see cref="TryDequeue"/> 交给主线程。<see cref="Open"/> 必须立即返回,不得阻塞。
    /// </summary>
    public interface IWebSocketChannel : IDisposable
    {
        void Open(string url);

        /// <summary>发送一条二进制消息。只能在 Opened 之后、Closed 之前调用。</summary>
        void Send(byte[] message);

        /// <summary>主线程轮询取出下一个事件;没有事件时返回 false。</summary>
        bool TryDequeue(out WebSocketEvent ev);
    }
}
