using System;
using Google.Protobuf;

namespace MmorpgClient.Net
{
    /// <summary>
    /// 一条按 MuduoCodec 分帧的 TCP 连接的最小抽象:连接 / 发帧 / 主线程轮询派发。
    ///
    /// 生产实现是 <see cref="GateTcpClient"/>(大厅与战斗直连共用同一套线协议,
    /// 见 turn-based-battle-server.md §18.2);EditMode 测试注入假实现驱动
    /// <see cref="BattleDirectLink"/> 的状态机。
    ///
    /// 线程约定:<see cref="Connect"/> 可能同步阻塞(TcpClient.Connect),调用方自行决定
    /// 放到哪个线程;<see cref="Poll"/> 必须在主线程调用,三个事件都只在 Poll 内触发。
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

        void Send(IMessage message);

        void Poll();
    }
}
