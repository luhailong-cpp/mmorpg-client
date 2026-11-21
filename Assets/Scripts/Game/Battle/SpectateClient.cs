using System;
using Google.Protobuf;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.Battle
{
    /// <summary>观战客户端相位(SpectateClient API 契约,UI 路只读)。</summary>
    public enum SpectatePhase
    {
        /// <summary>空闲:未观战。</summary>
        None,
        /// <summary>WatchBattle 已发出,等 NotifySpectateState 首帧(15s 超时)。</summary>
        Requesting,
        /// <summary>观战中:逐回合收 NotifySpectateTurnResult。</summary>
        Watching,
        /// <summary>收到 NotifySpectateEnd,等 UI 收尾后 AckEnd() 回 None。</summary>
        Ended,
    }

    /// <summary>
    /// 观战网络层状态机(只读:观众对战斗状态零写权,设计文档 §10 D8)。
    ///
    /// 与 <see cref="BattleClient"/> 是两套互不干扰的状态机(消息号不同;
    /// 生产上两者共用同一个 DirectRoutingBattleTransport 与同一条直连),
    /// 结构与惯例照搬 BattleClient:网络依赖收敛在 <see cref="IBattleTransport"/>
    /// 与可选的 <see cref="IBattleChannel"/>(直连就绪状态;null = 永远就绪),
    /// 本类不引用 UnityEngine,时钟由宿主经 <see cref="Tick"/> 注入。
    ///
    /// 首帧(turn-based §22 D69):WatchBattle 成功后服务端经大厅推 NotifyBattleAssigned(OBSERVER),
    /// 客户端直连握手成功时 battle 随即经直连推一份 SpectateStateS2C 快照 —— 首帧就是它,
    /// 客户端不必自己补拉;之后的观战帧也只从直连来。
    ///
    /// 边界情况(均有 EditMode 测试覆盖):
    ///  - 随机观战(battle_id=0):首帧可能先于 WatchBattle 响应到达(直连快照与经 gate 的
    ///    gRPC 响应两条链路无序),Requesting 期间未定 battle_id 时以先到者为准;
    ///  - 迟到/错发消息(battle_id 不匹配当前观战)丢弃;
    ///  - 首帧 15s 未到:收敛回 None 并报错(照 BattleClient 的 Preparing 超时惯例),
    ///    有直连通道时同时通知服务端退出(就绪发 StopWatchBattle,未就绪放弃直连);
    ///  - 退出观战时直连未就绪:StopWatchBattle 发不出去(gate 不中继战斗),本地收敛并放弃直连,
    ///    服务端观众名单等战斗结束时清理;若分配包晚到、直连随后就绪,再补发退出;
    ///  - 直连终结为 BattleGone / Unreachable,或直连改服务另一局(Superseded):观战无从继续,
    ///    收敛回 None 并报错;
    ///  - 断线:本地观战态整体作废回 None(观战无重连恢复,重进走 WatchBattle)。
    /// </summary>
    public sealed class SpectateClient
    {
        /// <summary>单例挂接:与 BattleClient 模式一致(实例由 GameClient 持有,静态 Instance 供 UI 路解析;测试直接 new)。</summary>
        public static SpectateClient Instance { get; private set; }

        /// <summary>Requesting 相位超时(秒):WatchBattle 后首帧无下文时收敛回 None。</summary>
        public const double FirstFrameTimeoutSeconds = 15.0;

        private readonly IBattleTransport _net;
        private readonly IBattleChannel _channel;   // null = 永远就绪(既有测试)

        private double _now;                  // 宿主注入的时钟(秒)
        private double _firstFrameDeadline;   // 首帧超时时刻(0 = 未挂)
        private ulong _battleId;              // 当前观战战斗 id;0 = 无(随机模式响应/首帧回填)
        private Match.eSpectateDelivery _delivery; // 本次观战投递档位(DIRECT / LOBBY)
        private bool _lobbySubscribed;        // LOBBY 档已完成 SubscribeSpectate
        private long _lastTurnRound = -1;     // 158 单调去重水位;161 不推进此水位(§6.1)
        private string _watchRequestId;       // 当前 WatchBattle 的幂等 request_id(大厅重连续观复用)

        // 随机观战响应未回(_battleId==0)时用户退出:battle_id 未知发不了有效
        // StopWatchBattle,置此标记等响应/首帧带回 battle_id 后补发退出。
        // 注意 ClearWatchContext 不得重置它:本地相位先收敛 None,标记要活到回填到达。
        private bool _stopPending;

        // 退出时直连未就绪、只能本地放弃的那一局:分配包若晚到、直连随后为它就绪,
        // 就在就绪时补发 StopWatchBattle(服务端摘观众并关直连)。0 = 无。同样不随 ClearWatchContext 重置。
        private ulong _unsentStopBattleId;

        // ── 契约属性 ────────────────────────────────────────

        public SpectatePhase Phase { get; private set; } = SpectatePhase.None;

        /// <summary>当前观战投递模式(NONE / DIRECT / LOBBY)。</summary>
        public Match.eSpectateDelivery Delivery => _delivery;

        /// <summary>最新权威战斗状态(首帧/回合结算更新;断线/退出清空)。</summary>
        public BattleStateS2C State { get; private set; }

        /// <summary>当前观众数(含本人,随 NotifySpectateState 更新;LOBBY 档为近似值/0=未知)。</summary>
        public uint ObserverCount { get; private set; }

        /// <summary>当前观战的战斗 id(0 = 无/随机模式尚未回填)。</summary>
        public ulong WatchedBattleId => _battleId;

        public ulong MyPlayerId => _net.PlayerId;

        // ── 契约事件 ────────────────────────────────────────

        public event Action<SpectatePhase> OnPhaseChanged;
        public event Action<SpectateStateS2C> OnSpectateState;
        public event Action<TurnResultS2C> OnSpectateTurn;
        public event Action<SpectateEndS2C> OnSpectateEnd;
        public event Action<Match.ListWatchableBattlesResponse> OnListUpdated;
        public event Action<string> OnError;

        // ── 构造/挂接 ───────────────────────────────────────

        /// <param name="transport">观战网络传输(生产:DirectRoutingBattleTransport)。</param>
        /// <param name="channel">战斗直连就绪状态(生产:同一个 DirectRoutingBattleTransport);null = 永远就绪。</param>
        public SpectateClient(IBattleTransport transport, IBattleChannel channel = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _channel = channel;
            RegisterNotifies();
            _net.Disconnected += HandleDisconnected;
            if (_channel != null)
            {
                _channel.Ready += HandleChannelReady;
                _channel.Lost += HandleChannelLost;
            }
        }

        /// <summary>生产入口:创建实例并登记为单例(GameClient 构造时调用)。channel 语义同构造函数。</summary>
        public static SpectateClient Attach(IBattleTransport transport, IBattleChannel channel = null)
        {
            var client = new SpectateClient(transport, channel);
            Instance = client;
            return client;
        }

        /// <summary>宿主每帧驱动(GameClient.Tick),只负责首帧超时。</summary>
        public void Tick(double nowSeconds)
        {
            _now = nowSeconds;

            if (Phase == SpectatePhase.Requesting && _firstFrameDeadline > 0 && _now >= _firstFrameDeadline)
            {
                _firstFrameDeadline = 0;
                ulong battleId = _battleId;
                var delivery = _delivery;
                bool lobbySubscribed = _lobbySubscribed;
                OnError?.Invoke("观战首帧超时,已退出观战");
                ClearWatchContext();
                SetPhase(SpectatePhase.None);
                if (delivery == Match.eSpectateDelivery.SpectateDeliveryLobby)
                {
                    _watchRequestId = null;
                    if (lobbySubscribed && battleId != 0) SendUnsubscribeSpectate(battleId);
                }
                else if (_channel != null)
                {
                    // 有直连通道时顺手通知服务端退出(就绪发 StopWatchBattle,未就绪放弃直连),
                    // 否则直连还在后台重连、握手成功后服务端继续对一个已离开的观众推帧;
                    // battle_id 未知(随机观战响应也没回)时挂待补退出,等回填。
                    if (battleId != 0) SendStopWatch(battleId);
                    else _stopPending = true;
                }
            }
        }

        // ── 契约方法 ────────────────────────────────────────

        /// <summary>随机观战(battle_id=0,由 match 从活跃索引挑一场)。</summary>
        public void WatchRandom() => WatchBattle(0);

        /// <summary>
        /// 观战指定战斗(battleId=0 为随机)。成功进 Requesting,收到
        /// NotifySpectateState 首帧才算进入观战(设计文档 §10.2);
        /// 服务端会拒绝排队/战斗中的玩家(D11 互斥),错误统一走 OnError。
        /// </summary>
        public void WatchBattle(ulong battleId) => StartWatchBattle(battleId, Guid.NewGuid().ToString("D"));

        /// <summary>
        /// 大厅断线重连前捕获 LOBBY 档观战上下文(设计文档 §5.3):
        /// 仅在 LOBBY 档且已确定 battle_id 与 request_id 时返回 true。
        /// </summary>
        public bool TryCaptureLobbyReconnectContext(out ulong battleId, out string requestId)
        {
            if ((Phase == SpectatePhase.Requesting || Phase == SpectatePhase.Watching)
                && _delivery == Match.eSpectateDelivery.SpectateDeliveryLobby
                && _battleId != 0
                && !string.IsNullOrEmpty(_watchRequestId))
            {
                battleId = _battleId;
                requestId = _watchRequestId;
                return true;
            }
            battleId = 0;
            requestId = null;
            return false;
        }

        /// <summary>
        /// 大厅重连成功后用原 request_id 幂等重发 WatchBattle(设计文档 §5.3 / CODEX §6),
        /// 拿新凭证重新向新 gate 发起 SubscribeSpectate。
        /// </summary>
        public void ResumeLobbyWatch(ulong battleId, string requestId)
        {
            if (Phase != SpectatePhase.None || battleId == 0 || string.IsNullOrEmpty(requestId)) return;
            StartWatchBattle(battleId, requestId);
        }

        private void StartWatchBattle(ulong battleId, string requestId)
        {
            if (Phase != SpectatePhase.None)
            {
                OnError?.Invoke("当前状态不能发起观战(需先退出当前观战)");
                return;
            }

            _stopPending = false; // 新观战作废旧的待补退出
            _unsentStopBattleId = 0;
            _battleId = battleId;
            _delivery = Match.eSpectateDelivery.SpectateDeliveryNone;
            _lobbySubscribed = false;
            _lastTurnRound = -1;
            SetPhase(SpectatePhase.Requesting);
            _firstFrameDeadline = _now + FirstFrameTimeoutSeconds; // SetPhase 会清,故在其后挂

            var req = new Match.WatchBattleRequest
            {
                PlayerId = MyPlayerId,
                BattleId = battleId,
                RequestId = requestId,
            };
            _watchRequestId = req.RequestId;
            _net.Call(MessageIds.WatchBattle, req, Match.WatchBattleResponse.Parser,
                resp =>
                {
                    if (_watchRequestId != req.RequestId) return;
                    // 用户已在响应回来前退出(随机模式待补退出):拿到 battle_id 立即补发
                    if (_stopPending)
                    {
                        _stopPending = false;
                        // LOBBY 档尚未发 SubscribeSpectate,无需退订或 StopWatchBattle
                        if (!HasTip(resp.ErrorMessage)
                            && resp.Delivery != Match.eSpectateDelivery.SpectateDeliveryLobby
                            && resp.BattleId != 0)
                            SendStopWatch(resp.BattleId);
                        return;
                    }
                    // 首帧已先到(Watching)或已超时/退出:迟到响应不改状态
                    if (Phase != SpectatePhase.Requesting) return;
                    if (HasTip(resp.ErrorMessage))
                    {
                        OnError?.Invoke(DescribeTip("观战请求失败", resp.ErrorMessage));
                        ClearWatchContext();
                        SetPhase(SpectatePhase.None);
                        return;
                    }
                    // 随机模式:响应回填实际战斗 id(若首帧未抢先回填)
                    if (_battleId == 0 && resp.BattleId != 0) _battleId = resp.BattleId;
                    _delivery = resp.Delivery;

                    if (resp.Delivery == Match.eSpectateDelivery.SpectateDeliveryLobby)
                    {
                        if (_battleId == 0 || resp.SpectateTicket == null || resp.SpectateTicket.IsEmpty)
                        {
                            OnError?.Invoke("观战请求失败:观战凭证无效");
                            ClearWatchContext();
                            SetPhase(SpectatePhase.None);
                            return;
                        }
                        SendSubscribeSpectate(_battleId, resp.SpectateTicket, req.RequestId);
                    }
                },
                err =>
                {
                    if (_watchRequestId != req.RequestId) return;
                    if (Phase != SpectatePhase.Requesting) return;
                    OnError?.Invoke($"观战请求失败:{err}");
                    ClearWatchContext();
                    SetPhase(SpectatePhase.None);
                });
        }

        /// <summary>
        /// 主动退出观战:本地立即收敛回 None(UI 响应优先),服务端移除是
        /// 尽力而为:LOBBY 档经大厅发 UnsubscribeSpectate;DIRECT 档直连已就绪 → 经直连发
        /// StopWatchBattle,直连未就绪 → 放弃直连并挂待补退出(turn-based §22 D74)。
        /// </summary>
        public void StopWatch()
        {
            if (Phase != SpectatePhase.Requesting && Phase != SpectatePhase.Watching)
            {
                OnError?.Invoke("当前不在观战中");
                return;
            }

            var delivery = _delivery;
            bool lobbySubscribed = _lobbySubscribed;
            ulong battleId = _battleId;

            if (delivery == Match.eSpectateDelivery.SpectateDeliveryLobby)
            {
                _watchRequestId = null; // 若 SubscribeSpectate 仍在途,回调见不匹配后立即补发退订
                if (lobbySubscribed && battleId != 0)
                    SendUnsubscribeSpectate(battleId);
            }
            else if (Phase == SpectatePhase.Requesting && battleId == 0)
            {
                // Requesting 且 battle_id 未知(随机观战响应未回):改挂待补标记,等响应/首帧回填后补发退出。
                _stopPending = true;
            }
            else
            {
                SendStopWatch(battleId);
            }

            ClearWatchContext();
            State = null;
            ObserverCount = 0;
            SetPhase(SpectatePhase.None);
        }

        /// <summary>拉取可观战列表(limit=0 用服务端默认条数),结果经 OnListUpdated 透传,不改相位。</summary>
        public void RefreshList(uint limit)
        {
            var req = new Match.ListWatchableBattlesRequest
            {
                PlayerId = MyPlayerId,
                Limit = limit,
            };
            _net.Call(MessageIds.ListWatchableBattles, req, Match.ListWatchableBattlesResponse.Parser,
                resp => OnListUpdated?.Invoke(resp),
                err => OnError?.Invoke($"拉取观战列表失败:{err}"));
        }

        /// <summary>UI 收尾(结束面板收起)后调用(契约):Ended → None。迟到 Ack 丢弃。</summary>
        public void AckEnd()
        {
            if (Phase != SpectatePhase.Ended) return;
            ClearWatchContext();
            State = null;
            ObserverCount = 0;
            SetPhase(SpectatePhase.None);
        }

        // ── S2C 推送处理 ────────────────────────────────────

        private void RegisterNotifies()
        {
            _net.RegisterNotify(MessageIds.NotifySpectateState,
                mc => HandleSpectateState(SpectateStateS2C.Parser.ParseFrom(mc.SerializedMessage)));
            _net.RegisterNotify(MessageIds.NotifySpectateTurnResult,
                mc => HandleSpectateTurnResult(TurnResultS2C.Parser.ParseFrom(mc.SerializedMessage)));
            _net.RegisterNotify(MessageIds.NotifySpectateEnd,
                mc => HandleSpectateEnd(SpectateEndS2C.Parser.ParseFrom(mc.SerializedMessage)));
        }

        private void HandleSpectateState(SpectateStateS2C ev)
        {
            if (ev?.State == null) return;
            // 待补退出:首帧抢先于 WatchBattle 响应带回 battle_id 时同样补发
            //(须在相位守卫前:StopWatch 已把相位收敛 None)
            if (_stopPending && ev.State.BattleId != 0)
            {
                _stopPending = false;
                SendStopWatch(ev.State.BattleId);
                return;
            }
            if (Phase != SpectatePhase.Requesting && Phase != SpectatePhase.Watching) return;

            ulong incoming = ev.State.BattleId;
            // 迟到/错发的首帧丢弃;Requesting 且 battle_id 未定(随机模式响应未回)时以首帧为准
            if (_battleId != 0 && incoming != 0 && incoming != _battleId) return;
            if (_battleId == 0 && incoming != 0) _battleId = incoming;

            // 契约 §6.1:161 不推进 158 水位(STATE(r) 在第 r 回合结算前,紧接着的 TURN(r) 必须照常处理);
            // Watching 期间若迟到的旧 STATE(r) 晚于已处理的 TURN(r)(其状态已在 r+1),丢弃以防回退状态。
            if (Phase == SpectatePhase.Watching && _lastTurnRound >= 0 && (long)ev.State.RoundIndex <= _lastTurnRound)
                return;

            State = ev.State;
            ObserverCount = ev.ObserverCount;

            // 事件先于终相位(照 BattleClient 惯例):UI 先拿首帧开屏,再收相位刷新
            OnSpectateState?.Invoke(ev);
            if (Phase == SpectatePhase.Requesting) SetPhase(SpectatePhase.Watching);
        }

        private void HandleSpectateTurnResult(TurnResultS2C ev)
        {
            if (ev == null) return;
            if (Phase != SpectatePhase.Watching) return;
            if (_battleId != 0 && ev.BattleId != 0 && ev.BattleId != _battleId) return;

            // 契约 §6.1:158 只与 158 比较 round_index,单调去重(L1→L2 升档重叠自愈)
            if (_lastTurnRound >= 0 && (long)ev.RoundIndex <= _lastTurnRound) return;
            _lastTurnRound = ev.RoundIndex;

            if (ev.State != null) State = ev.State;
            OnSpectateTurn?.Invoke(ev); // 观众无 Resolving 相位:只读流,UI 按自己的节奏播
        }

        private void HandleSpectateEnd(SpectateEndS2C ev)
        {
            if (ev == null) return;
            // Requesting 也接受:AddObserver / SubscribeSpectate 与战斗收尾可竞速(首帧未到战斗先结束)
            if (Phase != SpectatePhase.Requesting && Phase != SpectatePhase.Watching) return;
            if (_battleId != 0 && ev.BattleId != 0 && ev.BattleId != _battleId) return;

            _lobbySubscribed = false;
            SetPhase(SpectatePhase.Ended);
            OnSpectateEnd?.Invoke(ev); // UI 展示结束原因后调 AckEnd() 回 None
        }

        /// <summary>断线、静默重连或跨区重定向时作废本地观战态，避免直连因 HostClosed 关闭后相位卡在 Watching/Requesting。</summary>
        public void Reset()
        {
            // 断线/连接重置:观战态整体作废(服务端绑定随 session 失效,重进走 WatchBattle);
            // 待补退出一并作废(session 失效服务端自会清绑定,无需再补发;直连随大厅断线由宿主关闭)
            _watchRequestId = null;
            _stopPending = false;
            _unsentStopBattleId = 0;
            ClearWatchContext();
            State = null;
            ObserverCount = 0;
            SetPhase(SpectatePhase.None);
        }

        private void HandleDisconnected() => Reset();

        // ── 战斗直连通道(turn-based §22 D74) ─────────────

        /// <summary>
        /// 直连就绪。首帧不在这里合成 —— 观众握手成功时服务端会经直连推 SpectateStateS2C 快照(D69)。
        /// 这里只处理一种情形:退出观战时直连未就绪、只能本地放弃,而分配包晚到、直连随后
        /// 为那一局建起来了 —— 补发 StopWatchBattle,让服务端摘掉观众并关直连。
        /// </summary>
        private void HandleChannelReady(ulong battleId, eBattleTicketRole role)
        {
            if (battleId == 0 || battleId != _unsentStopBattleId) return;
            if (role != eBattleTicketRole.BattleTicketRoleObserver) return;
            _unsentStopBattleId = 0;
            SendStopWatch(battleId);
        }

        /// <summary>
        /// 直连终结:BattleGone(战斗已结束,观战结束包多半丢了)/ Unreachable(连不上 battle 节点)/
        /// Superseded(直连改服务另一局,典型是观战中进了自己的 gather:match 的 RemoveObserver 尽力而为
        /// 可能丢,SpectateEnd 也可能晚于参战分配包而随旧连接一起丢掉)时观战无从继续 —— 收缩后观战帧
        /// 只走直连,Watching 没有超时,不收敛就永远停在观战里、WatchBattle 也被相位闸挡住。
        /// LOBBY 档不经直连收帧,忽略直连终结事件。
        /// </summary>
        private void HandleChannelLost(ulong battleId, BattleLinkCloseKind kind, string detail)
        {
            if (Phase != SpectatePhase.Requesting && Phase != SpectatePhase.Watching) return;
            if (_delivery == Match.eSpectateDelivery.SpectateDeliveryLobby) return;
            if (battleId == 0 || battleId != _battleId) return;
            string text;
            switch (kind)
            {
                case BattleLinkCloseKind.BattleGone:
                    text = "该战斗已结束,已退出观战";
                    break;
                case BattleLinkCloseKind.Unreachable:
                    text = "无法连接战斗服务器,已退出观战";
                    break;
                case BattleLinkCloseKind.Superseded:
                    text = "已切换到其他战斗,观战结束";
                    break;
                default:
                    return;
            }
            OnError?.Invoke(text);
            ClearWatchContext();
            State = null;
            ObserverCount = 0;
            SetPhase(SpectatePhase.None);
        }

        // ── 内部工具 ────────────────────────────────────────

        private void SendSubscribeSpectate(ulong battleId, ByteString ticket, string requestId)
        {
            var req = new SubscribeSpectateRequest
            {
                BattleId = battleId,
                SpectateTicket = ticket,
            };
            _net.Call(GateClientSpectateRpc.SubscribeSpectateMessageId, req, SubscribeSpectateResponse.Parser,
                resp =>
                {
                    if (_watchRequestId != requestId)
                    {
                        // 订阅在途期间用户已退出/超时/换场:若 gate 已受理,立即补发退订释放资源
                        if (!HasTip(resp.ErrorMessage)) SendUnsubscribeSpectate(battleId);
                        return;
                    }
                    if (Phase != SpectatePhase.Requesting && Phase != SpectatePhase.Watching)
                    {
                        if (!HasTip(resp.ErrorMessage) && Phase == SpectatePhase.None)
                            SendUnsubscribeSpectate(battleId);
                        return;
                    }
                    if (HasTip(resp.ErrorMessage))
                    {
                        OnError?.Invoke(DescribeTip("观战订阅失败", resp.ErrorMessage));
                        ClearWatchContext();
                        State = null;
                        ObserverCount = 0;
                        SetPhase(SpectatePhase.None);
                        return;
                    }
                    _lobbySubscribed = true;
                },
                err =>
                {
                    if (_watchRequestId != requestId) return;
                    if (Phase != SpectatePhase.Requesting && Phase != SpectatePhase.Watching) return;
                    OnError?.Invoke($"观战订阅失败:{err}");
                    ClearWatchContext();
                    State = null;
                    ObserverCount = 0;
                    SetPhase(SpectatePhase.None);
                });
        }

        private void SendUnsubscribeSpectate(ulong battleId)
        {
            var req = new UnsubscribeSpectateRequest { BattleId = battleId };
            _net.Call(GateClientSpectateRpc.UnsubscribeSpectateMessageId, req, UnsubscribeSpectateResponse.Parser,
                resp =>
                {
                    if (HasTip(resp.ErrorMessage))
                        OnError?.Invoke(DescribeTip("退订观战失败", resp.ErrorMessage));
                },
                err => OnError?.Invoke($"退订观战失败:{err}"));
        }

        /// <summary>
        /// 通知服务端退出观战(尽力而为:失败只报错不改相位;主动退出、待补退出、首帧超时共用)。
        /// 直连未就绪时发不出去(gate 不中继战斗):改为放弃该局直连,并记下这一局,
        /// 万一分配包晚到、直连随后就绪,再在就绪时补发(见 <see cref="HandleChannelReady"/>)。
        /// </summary>
        private void SendStopWatch(ulong battleId)
        {
            if (_channel != null && !_channel.IsReadyFor(battleId))
            {
                _channel.Abandon(battleId);
                _unsentStopBattleId = battleId;
                return;
            }
            if (_unsentStopBattleId == battleId) _unsentStopBattleId = 0;
            var req = new StopWatchBattleRequest { BattleId = battleId };
            _net.Call(MessageIds.StopWatchBattle, req, StopWatchBattleResponse.Parser,
                resp =>
                {
                    if (HasTip(resp.ErrorMessage))
                        OnError?.Invoke(DescribeTip("退出观战失败", resp.ErrorMessage));
                },
                err => OnError?.Invoke($"退出观战失败:{err}"));
        }

        private void ClearWatchContext()
        {
            _battleId = 0;
            _firstFrameDeadline = 0;
            _delivery = Match.eSpectateDelivery.SpectateDeliveryNone;
            _lobbySubscribed = false;
            _lastTurnRound = -1;
        }

        private void SetPhase(SpectatePhase phase)
        {
            if (Phase == phase) return;
            if (phase != SpectatePhase.Requesting) _firstFrameDeadline = 0;
            Phase = phase;
            OnPhaseChanged?.Invoke(phase);
        }

        private static bool HasTip(TipInfoMessage tip) => tip != null && tip.Id != 0;

        private static string DescribeTip(string what, TipInfoMessage tip)
        {
            if (tip == null || tip.Id == 0) return what;
            switch ((match_error)tip.Id)
            {
                case match_error.KMatchSpectateFull:
                    return $"{what}:观战人数已满(tip={tip.Id})";
                case match_error.KMatchSpectateTicketInvalid:
                    return $"{what}:观战凭证无效(tip={tip.Id})";
                default:
                    return $"{what}(tip={tip.Id})";
            }
        }

        /// <summary>观战结束原因文案(契约 §6.1:处理 SOURCE_LOST,未知枚举值按 ABORTED 处理)。</summary>
        public static string DescribeEndReason(SpectateEndS2C end)
        {
            if (end == null) return "观战结束";
            switch (end.Reason)
            {
                case eSpectateEndReason.SpectateEndRemoved:
                    return "观战结束:你已进入排队/战斗,被移出观战";
                case eSpectateEndReason.SpectateEndSourceLost:
                    return "观战结束:战斗服务器连接中断";
                case eSpectateEndReason.SpectateEndNone:
                case eSpectateEndReason.SpectateEndBattleFinished:
                    // 观战屏固定 team 0 在下排(OpenSpectate 契约),按此翻译胜负方
                    switch (end.Outcome)
                    {
                        case eBattleOutcome.BattleOutcomeSideAWin: return "观战结束:下方队伍获胜";
                        case eBattleOutcome.BattleOutcomeSideBWin: return "观战结束:上方队伍获胜";
                        case eBattleOutcome.BattleOutcomeDraw: return "观战结束:平局";
                        default: return "观战结束";
                    }
                case eSpectateEndReason.SpectateEndBattleAborted:
                default:
                    // 契约 §6.1:未知枚举值按作废处理
                    return "观战结束:战斗已作废";
            }
        }
    }
}
