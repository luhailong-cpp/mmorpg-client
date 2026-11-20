using System;
using Google.Protobuf;

namespace MmorpgClient.Net
{
    /// <summary>
    /// 一条按 MuduoCodec 分帧的连接的最小抽象:连接 / 发帧 / 主线程轮询派发。
    ///
    /// 两种传输,线协议(客户端帧的字节)完全相同,由 <see cref="ClientTransport"/> 按平台选:
    /// <list type="bullet">
    ///   <item><see cref="GateTcpClient"/>:原生包与编辑器,TCP 直连(大厅与战斗直连共用,见 turn-based-battle-server.md §18.2);</item>
    ///   <item><see cref="WebSocketFramedConnection"/>:微信 / 抖音小游戏,一条二进制 WebSocket 消息承载一个客户端帧
    ///     (服务端 docs/design/minigame-websocket-gate.md)。</item>
    /// </list>
    /// EditMode 测试注入假实现驱动 <see cref="BattleDirectLink"/> 的状态机。
    ///
    /// 线程约定:TCP 的 Connect 会同步阻塞(TcpClient.Connect),调用方自行决定放到哪个线程;WebSocket 的 Connect 立即返回,
    /// 连上之前 <see cref="Connected"/> 为假、发出的帧先排队,连不上按断线报(OnDisconnected)。
    /// <see cref="Poll"/> 必须在主线程调用,三个事件都只在 Poll 内触发。
    /// </summary>
    public interface IFramedConnection : IDisposable
    {
        bool Connected { get; }

        /// <summary>收到一帧已解码的 protobuf 消息(Poll 内触发)。</summary>
        event Action<IMessage> OnMessage;

        /// <summary>读写线程报错(Poll 内触发;只用于日志,断线另走 OnDisconnected)。</summary>
        event Action<string> OnError;

        /// <summary>连接已断开(Poll 内触发,整个生命周期最多一次)。</summary>
        event Action OnDisconnected;

        void Connect(string host, int port);

        /// <summary>
        /// 带上服务端随地址一起下发的 WebSocket 地址(assign-gate 的 gate_ws_url、msg 124 的 target_ws_url、
        /// 战斗分配与测速目标的 ws_url)。TCP 实现不读它,所以缺省就是两参数的 Connect;WebSocket 实现连它,不读 host / port。
        /// </summary>
        void Connect(string host, int port, string wsUrl) => Connect(host, port);

        void Send(IMessage message);

        void Poll();

        /// <summary>
        /// 应用回到前台时关掉这条连接的底层通道,不派发任何事件:调用方紧接着自己走一次正常的断线恢复。
        /// 缺省什么都不做(测试替身没有底层通道)。
        /// </summary>
        void CloseForResume() { }
    }
}
