using System;
using Google.Protobuf;
using MmorpgClient.Net;

namespace MmorpgClient.Game.Battle
{
    /// <summary>
    /// 按消息号分流的 <see cref="IBattleTransport"/>:四条战斗 RPC(SubmitBattleAction /
    /// GetBattleState / StopWatchBattle / SetAutoBattle)在战斗直连已验证时走
    /// <see cref="BattleDirectLink"/>,其余(排队 / 切磋 / 观战匹配 / 属性)以及直连未建立时
    /// 一律走大厅传输(gate 中继,D23 双通道并存)。S2C 处理器同时挂到两条链路上,
    /// BattleClient / SpectateClient 对消息来自哪条连接不敏感(§18.2)。
    ///
    /// 顺带把三条"本局结束"信号告诉直连链路,让它区分"服务端正常 FIN"与"意外断开":
    /// NotifyBattleEnd / NotifySpectateEnd / StopWatchBattle 的应答;以及把
    /// NotifyBattleReconnect(大厅重连后 scene 推)转成"补签一张票再建直连"。
    /// </summary>
    public sealed class DirectRoutingBattleTransport : IBattleTransport
    {
        private readonly IBattleTransport _lobby;
        private readonly BattleDirectLink _link;

        public DirectRoutingBattleTransport(IBattleTransport lobby, BattleDirectLink link)
        {
            _lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
            _link = link ?? throw new ArgumentNullException(nameof(link));
        }

        public ulong PlayerId => _lobby.PlayerId;

        public bool IsReady => _lobby.IsReady;

        /// <summary>大厅断线才算断线(直连断开由链路自行恢复或回落,不作废战斗态)。</summary>
        public event Action Disconnected
        {
            add => _lobby.Disconnected += value;
            remove => _lobby.Disconnected -= value;
        }

        /// <summary>只有 BattleClientPlayer 的四条客户端 RPC 允许走直连(服务端白名单,D27)。</summary>
        public static bool IsDirectMessage(uint messageId)
            => messageId == MessageIds.SubmitBattleAction
            || messageId == MessageIds.GetBattleState
            || messageId == MessageIds.StopWatchBattle
            || messageId == MessageIds.SetAutoBattle;

        /// <summary>当前这条消息会不会走直连(测试与日志用)。</summary>
        public bool RoutesDirect(uint messageId) => IsDirectMessage(messageId) && _link.IsVerified;

        /// <summary>
        /// 直连传输层失败后可以原样改走大厅重发的 RPC —— 判据是**服务端重复执行无害**。
        ///
        /// `SubmitBattleAction` **刻意不在其中**:`SubmitBattleActionRequest` 只有
        /// {battle_id, action},没有回合号,服务端 `HandleSubmitBattleAction` 也没有
        /// 重复提交守卫(battle_room_manager.cpp 直接 `engine.SubmitAction`)。若第一次
        /// 其实已经送达、只是应答在断连中丢了,重发就会落进**下一回合**。宁可让这一手报错
        /// (引擎的回合超时会兜底成默认普攻),也不能悄悄替玩家多打一次。
        /// </summary>
        public static bool IsRetriableOnLobby(uint messageId)
            => messageId == MessageIds.GetBattleState      // 只读
            || messageId == MessageIds.StopWatchBattle     // 服务端对重复退出/未观战都回成功
            || messageId == MessageIds.SetAutoBattle;      // 置位到同一个值

        public void RegisterNotify(uint messageId, Action<MessageContent> handler)
        {
            var wrapped = Wrap(messageId, handler);
            _lobby.RegisterNotify(messageId, wrapped);
            _link.RegisterNotify(messageId, wrapped);
        }

        public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                                Action<TResp> onResponse, Action<string> onError)
            where TResp : IMessage<TResp>
        {
            if (messageId == MessageIds.StopWatchBattle)
            {
                // 退出观战成功后服务端会关这一局的直连,那是正常收尾。
                // **必须带上请求里的 battle_id**:传 0 表示「当前这局」,而 SpectateClient 的
                // 待补退出路径可能在玩家已经进入自己的战斗 A 之后才发出针对 B 的退出,
                // 那时 0 会把 A 的链路误标成已结束,后续任何一次断开都不再重连(评审确认项)。
                ulong endedBattleId = (request as StopWatchBattleRequest)?.BattleId ?? 0;
                var inner = onResponse;
                onResponse = resp =>
                {
                    _link.HandleBattleEnded(endedBattleId);
                    inner?.Invoke(resp);
                };
            }

            if (!RoutesDirect(messageId))
            {
                _lobby.Call(messageId, request, parser, onResponse, onError);
                return;
            }

            // 直连传输层失败(没送到 / 断了 / 超时)时,对幂等 RPC 改走大厅重发一次:
            // 直连是优化,不该让它的抖动变成玩家可见的失败(D23 双通道并存的本意)。
            // 服务端业务拒绝(server tip=N)不重发 —— 那是答案,不是投递失败。
            Action<string> routedOnError = onError;
            if (IsRetriableOnLobby(messageId))
            {
                routedOnError = err =>
                {
                    if (err != null && err.StartsWith(BattleDirectLink.TransportErrorPrefix, StringComparison.Ordinal))
                    {
                        _lobby.Call(messageId, request, parser, onResponse, onError);
                        return;
                    }
                    onError?.Invoke(err);
                };
            }
            _link.Call(messageId, request, parser, onResponse, routedOnError);
        }

        public void SendOneWay(uint messageId, IMessage request)
        {
            if (RoutesDirect(messageId)) _link.SendOneWay(messageId, request);
            else _lobby.SendOneWay(messageId, request);
        }

        private Action<MessageContent> Wrap(uint messageId, Action<MessageContent> handler)
        {
            if (messageId == MessageIds.NotifyBattleEnd)
            {
                return mc =>
                {
                    handler(mc);
                    _link.HandleBattleEnded(ParseBattleId(() => BattleEndS2C.Parser.ParseFrom(mc.SerializedMessage).BattleId));
                };
            }
            if (messageId == MessageIds.NotifySpectateEnd)
            {
                return mc =>
                {
                    handler(mc);
                    _link.HandleBattleEnded(ParseBattleId(() => SpectateEndS2C.Parser.ParseFrom(mc.SerializedMessage).BattleId));
                };
            }
            if (messageId == MessageIds.NotifyBattleReconnect)
            {
                return mc =>
                {
                    handler(mc);
                    _link.HandleReconnectHint(ParseBattleId(() => BattleReconnectS2C.Parser.ParseFrom(mc.SerializedMessage).BattleId));
                };
            }
            return handler;
        }

        private static ulong ParseBattleId(Func<ulong> parse)
        {
            try { return parse(); }
            catch { return 0; }
        }
    }
}
