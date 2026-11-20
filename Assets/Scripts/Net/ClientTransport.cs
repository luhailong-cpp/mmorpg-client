using System;

namespace MmorpgClient.Net
{
    /// <summary>
    /// 这个安装包连 gate / battle 用哪种传输(服务端 docs/design/minigame-websocket-gate.md W10)。
    /// 小游戏(WebGL)没有原始套接字,只有 WebSocket;原生包与编辑器走 TCP。
    /// 编辑器里加编译符号 MMORPG_FORCE_WS,可以拿 WebSocket 直连本机 gate 联调(gate 的客户端端口两种连接都接)。
    /// </summary>
    public static class ClientTransport
    {
        public static bool UseWebSocket =>
#if (UNITY_WEBGL && !UNITY_EDITOR) || MMORPG_FORCE_WS
            true;
#else
            false;
#endif

        /// <summary>产出一条还没连接的分帧连接。</summary>
        public static IFramedConnection Create(MuduoCodec codec)
            => UseWebSocket ? new WebSocketFramedConnection(codec) : new GateTcpClient(codec);

        /// <summary>
        /// 给「把会阻塞的 Connect 挪出主线程」的调用方(BattleDirectLink、RegionProbeClient)用:
        /// WebSocket 的 Connect 立即返回,而 WebGL 没有线程池,所以就地执行;TCP 返回 null,由调用方照旧用线程池。
        /// </summary>
        public static Action<Action> ConnectRunner => UseWebSocket ? (Action<Action>)(work => work()) : null;
    }
}
