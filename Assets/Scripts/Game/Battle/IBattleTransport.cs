using System;
using Google.Protobuf;

namespace MmorpgClient.Game.Battle
{
    /// <summary>
    /// BattleClient 的薄网络接口:把「请求-响应(Call)/ 单向发送(SendOneWay)/
    /// S2C 推送注册(OnNotify)」三件事从 GameClient 中抽出来,让状态机纯逻辑
    /// 可以在 EditMode 测试里用假实现驱动。
    ///
    /// 生产环境唯一实现是 <see cref="GameClientBattleTransport"/>,它把这三件事
    /// 原样接回 GameClient 现有的 gate TCP 管线 —— 本接口只是测试缝,不另起网络栈。
    /// </summary>
    public interface IBattleTransport
    {
        /// <summary>当前登录玩家 id(EnterGame 成功后有效;战斗内 actor_id 即 player_id)。</summary>
        ulong PlayerId { get; }

        /// <summary>
        /// gate 连接已建立且 token 校验通过。排队轮询等周期性请求只在就绪时发出,
        /// 断线期间自动暂停(重连后由服务端 NotifyBattleReconnect 驱动补拉)。
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// gate 连接断开(对应 GameClient.OnDisconnected)。BattleClient 收到后
        /// 作废本地战斗/排队态;重连后由服务端 NotifyBattleReconnect 驱动恢复。
        /// </summary>
        event Action Disconnected;

        /// <summary>
        /// 注册 S2C 推送处理器。语义同 GameClient.OnNotify:同一 messageId
        /// 只保留最后一次注册的 handler。
        /// </summary>
        void RegisterNotify(uint messageId, Action<MessageContent> handler);

        /// <summary>
        /// 发起请求-响应调用(fire-and-continue:本方法立即返回,回调稍后触发)。
        /// 实现负责协程宿主与超时;回调必须回到主线程语境(GameClient.Call 天然满足)。
        /// </summary>
        void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                         Action<TResp> onResponse, Action<string> onError)
            where TResp : IMessage<TResp>;

        /// <summary>单向发送(fire-and-forget),语义同 GameClient.SendOneWay。</summary>
        void SendOneWay(uint messageId, IMessage request);
    }
}
