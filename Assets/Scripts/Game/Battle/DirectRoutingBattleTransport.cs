using System;
using Google.Protobuf;
using MmorpgClient.Net;

namespace MmorpgClient.Game.Battle
{
    /// <summary>
    /// 战斗消息的分流层(turn-based §22 D66 / D74 收缩后口径):直连是战斗的唯一通路。
    ///
    /// 上行:四条战斗 RPC(SubmitBattleAction / GetBattleState / StopWatchBattle / SetAutoBattle)
    /// 只走 <see cref="BattleDirectLink"/>;直连未就绪时**本地快速失败**(<see cref="BattleDirectLink.NotReadyError"/>),
    /// 单向发送直接丢弃并记日志,**绝不走大厅** —— gate 两种模式都拒绝战斗消息,只回一条 id=0 的
    /// TipToClient,大厅上的请求只会挂满 15s 超时。直连传输层失败也原样交给调用方,不改走大厅重发;
    /// 丢帧恢复由 BattleClient 在直连就绪(<see cref="Ready"/>)时 GetBattleState 补拉。
    /// 其余消息(排队 / 切磋 / 观战匹配 / RequestBattleTicket)照旧走大厅。
    ///
    /// 下行:S2C 处理器同时挂到两条链路上(容错,无成本):大厅只承载 NotifyBattleAssigned /
    /// NotifyBattleStart / NotifyBattleReconnect,以及 scene 结算后推的 NotifyBattleEnd(D68);
    /// 其余战斗帧只从直连来。BattleClient / SpectateClient 对消息来自哪条连接不敏感,按 battle_id 幂等。
    ///
    /// 顺带把三条"本局结束"信号告诉直连链路,让它区分"服务端正常 FIN"与"意外断开":
    /// NotifyBattleEnd / NotifySpectateEnd / StopWatchBattle 的应答;以及把
    /// NotifyBattleReconnect(大厅重连后 scene 推)转成"补签一张票再建直连"。
    ///
    /// 同时实现 <see cref="IBattleChannel"/>:把链路的验证 / 终结转成 Ready / Lost 事件,
    /// 并转发 Retry / EnsureBattle / Abandon。
    /// </summary>
    public sealed class DirectRoutingBattleTransport : IBattleTransport, IBattleChannel
    {
        private readonly IBattleTransport _lobby;
        private readonly BattleDirectLink _link;
        private readonly Action<string> _log;

        /// <param name="lobby">大厅传输(生产:GameClientBattleTransport)。</param>
        /// <param name="link">战斗直连链路;本实例订阅它的 OnVerified / OnClosed,两者同寿命。</param>
        /// <param name="log">日志汇(未就绪丢弃等诊断);可空。</param>
        public DirectRoutingBattleTransport(IBattleTransport lobby, BattleDirectLink link, Action<string> log = null)
        {
            _lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
            _link = link ?? throw new ArgumentNullException(nameof(link));
            _log = log ?? (_ => { });
            _link.OnVerified += battleId => Ready?.Invoke(battleId, _link.Role);
            _link.OnClosed += (battleId, kind, reason) => Lost?.Invoke(battleId, kind, reason);
        }

        public ulong PlayerId => _lobby.PlayerId;

        public bool IsReady => _lobby.IsReady;

        /// <summary>大厅断线才算断线(直连断开由链路自行恢复,终结经 <see cref="Lost"/> 通知,不作废战斗态)。</summary>
        public event Action Disconnected
        {
            add => _lobby.Disconnected += value;
            remove => _lobby.Disconnected -= value;
        }

        // ── IBattleChannel ──────────────────────────────────

        public event Action<ulong, eBattleTicketRole> Ready;

        public event Action<ulong, BattleLinkCloseKind, string> Lost;

        public bool IsReadyFor(ulong battleId)
            => battleId != 0 && _link.IsVerified && _link.BattleId == battleId;

        public void Retry(ulong battleId) => _link.Retry(battleId);

        public void EnsureBattle(ulong battleId) => _link.EnsureBattle(battleId);

        public void Abandon(ulong battleId) => _link.Abandon(battleId);

        // ── IBattleTransport ────────────────────────────────

        /// <summary>只有 BattleClientPlayer 的四条客户端 RPC 走直连(服务端白名单,D27)。</summary>
        public static bool IsDirectMessage(uint messageId)
            => messageId == MessageIds.SubmitBattleAction
            || messageId == MessageIds.GetBattleState
            || messageId == MessageIds.StopWatchBattle
            || messageId == MessageIds.SetAutoBattle;

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

            if (!IsDirectMessage(messageId))
            {
                _lobby.Call(messageId, request, parser, onResponse, onError);
                return;
            }

            if (!_link.IsVerified)
            {
                // 未就绪:本地快速失败,不碰大厅(gate 拒绝战斗消息且不回带 id 的应答)
                _log($"直连未就绪,战斗请求本地失败 message_id={messageId} link_state={_link.State} battle_id={_link.BattleId}");
                onError?.Invoke(BattleDirectLink.NotReadyError);
                return;
            }

            // 传输层失败(没送到 / 断了 / 超时)原样交给调用方:不改走大厅重发。
            // SubmitBattleAction 尤其不能重发 —— 请求没有回合号、服务端无重复守卫(D40 未做),
            // 第一次若已送达只是应答丢了,重发会落进下一回合。
            _link.Call(messageId, request, parser, onResponse, onError);
        }

        public void SendOneWay(uint messageId, IMessage request)
        {
            if (!IsDirectMessage(messageId))
            {
                _lobby.SendOneWay(messageId, request);
                return;
            }
            if (!_link.IsVerified)
            {
                _log($"直连未就绪,丢弃战斗单向消息 message_id={messageId} link_state={_link.State} battle_id={_link.BattleId}");
                return;
            }
            _link.SendOneWay(messageId, request);
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
