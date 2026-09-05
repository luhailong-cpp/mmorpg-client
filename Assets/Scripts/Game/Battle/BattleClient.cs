using System;
using MmorpgClient.Net;

namespace MmorpgClient.Game.Battle
{
    /// <summary>回合制战斗客户端相位(BattleClient API 契约,UI 路只读)。</summary>
    public enum BattlePhase
    {
        /// <summary>空闲:未排队、未在战斗。</summary>
        None,
        /// <summary>已入匹配队列,每 3s 轮询 GetQueueStatus。</summary>
        Queued,
        /// <summary>已凑单/已应战,等服务端 gather 完成推 NotifyBattleStart。</summary>
        Preparing,
        /// <summary>回合内等待本地玩家提交行动。</summary>
        WaitingAction,
        /// <summary>收到 TurnResultS2C,UI 正在播回合表现(播完 AckTurnPlayed 回 WaitingAction)。</summary>
        Resolving,
        /// <summary>收到 BattleEndS2C,收尾后立即回 None。</summary>
        Ended,
    }

    /// <summary>
    /// 回合制战斗网络层状态机(NET 路实现,UI 路只调用/订阅 —— 见任务契约)。
    ///
    /// 设计依据:xuanming-server-mmo/docs/design/turn-based-battle-server.md
    /// (§3.1 生命周期、§3.1b 切磋、§3.2 补偿矩阵)与
    /// proto/battle/player_battle.proto、proto/match/match_service.proto。
    ///
    /// 网络依赖收敛在 <see cref="IBattleTransport"/>(生产实现
    /// <see cref="GameClientBattleTransport"/> 接 GameClient 既有管线;
    /// EditMode 测试注入假实现),本类不引用 UnityEngine —— 时钟由宿主
    /// 经 <see cref="Tick"/> 注入(GameClient.Tick 传 Time.realtimeSinceStartup)。
    ///
    /// 边界情况(均有 EditMode 测试覆盖):
    ///  - 排队中收到开战:Queued → Preparing → WaitingAction 连跳;
    ///  - 战斗中断线:本地态整体作废回 None,重连后服务端推 NotifyBattleReconnect
    ///    → 自动 RequestState() 按权威状态恢复 WaitingAction/Resolving;
    ///  - Ended/None 后收到迟到 TurnResultS2C:丢弃;
    ///  - 响应/推送里的 TipInfoMessage 错误统一走 OnError(照 GameClient 的 tip 处理,
    ///    Call 层的 MessageContent.ErrorMessage 也由 GameClient 折算成 "server tip=N" 进 onError)。
    /// </summary>
    public sealed class BattleClient
    {
        /// <summary>
        /// 单例挂接:与 GameClient 模式一致 —— 实例由 GameClient 构造时创建并持有
        /// (AppBootstrap 持有 GameClient),静态 Instance 仅供 UI 路解析。
        /// 测试直接 new,不污染 Instance。
        /// </summary>
        public static BattleClient Instance { get; private set; }

        /// <summary>Queued 期间 GetQueueStatus 轮询间隔(秒)。</summary>
        public const double QueuePollIntervalSeconds = 3.0;

        /// <summary>Preparing 相位超时(秒):gather 失败无下文时收敛回 None(补偿矩阵 §3.2)。</summary>
        public const double PreparingTimeoutSeconds = 15.0;

        private readonly IBattleTransport _net;

        private double _now;                 // 宿主注入的时钟(秒)
        private double _nextQueuePollAt;     // 下一次排队轮询时刻
        private double _preparingDeadline;   // Preparing 超时时刻(0 = 未挂)

        private ulong _battleId;             // 当前(或重连恢复中)战斗 id;0 = 无
        private string _queueTicket = string.Empty;

        // 连续战斗:最近一次显式 JoinQueue 的参数记忆(切磋不计,§11.2 只按队列参数重排)
        private Match.MatchMode _lastQueueMode;
        private uint _lastQueueConfigId;
        private bool _hasQueueMemory;
        private bool _endAckPending;         // 战斗已结束、等 UI AckBattleEnd(连续战斗的触发闸)

        // ── 契约属性 ────────────────────────────────────────

        public BattlePhase Phase { get; private set; } = BattlePhase.None;

        /// <summary>最新权威战斗状态(开战/回合结算/补拉时更新;断线清空)。</summary>
        public BattleStateS2C State { get; private set; }

        /// <summary>本地玩家 id(战斗内 actor_id 即 player_id)。</summary>
        public ulong MyPlayerId => _net.PlayerId;

        /// <summary>
        /// 自动战斗本地记忆开关(§11.2):开着时每次 BattleStart 自动补发
        /// SetAutoBattle(true),连续挂机免手点。SetAutoBattle() 会同步更新;
        /// UI 也可在战斗外直接置位预设下一场。纯本地,不触发网络。
        /// </summary>
        public bool AutoBattleLatched { get; set; }

        /// <summary>
        /// 连续战斗本地开关(§11.2):战斗结束且 UI 调 AckBattleEnd() 后,
        /// 自动按最近一次 JoinQueue 的 mode/config 重新入队。纯客户端行为。
        /// </summary>
        public bool ContinuousBattle { get; set; }

        /// <summary>
        /// 本人自动战斗的服务端权威状态(数据源 BattleStateS2C.actors[本人].is_auto;
        /// 与 AutoBattleLatched 的区别:这是渲染开关用的真值,那是本地意愿)。
        /// </summary>
        public bool IsMyActorAuto { get; private set; }

        // ── 契约事件 ────────────────────────────────────────

        public event Action<BattlePhase> OnPhaseChanged;
        public event Action<BattleStartS2C> OnBattleStart;
        public event Action<TurnResultS2C> OnTurnResult;
        public event Action<BattleEndS2C> OnBattleEnd;
        public event Action<Match.ChallengeInviteS2C> OnChallengeInvite;
        public event Action<Match.ChallengeResultS2C> OnChallengeResult;
        public event Action<Match.GetQueueStatusResponse> OnQueueStatus;
        /// <summary>本人 is_auto 权威值变化(true=服务端已挂机;UI 据此渲染「自动」按钮态)。</summary>
        public event Action<bool> OnAutoStateChanged;
        public event Action<string> OnError;

        // ── 构造/挂接 ───────────────────────────────────────

        public BattleClient(IBattleTransport transport)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            RegisterNotifies();
            _net.Disconnected += HandleDisconnected;
        }

        /// <summary>生产入口:创建实例并登记为单例(GameClient 构造时调用)。</summary>
        public static BattleClient Attach(IBattleTransport transport)
        {
            var client = new BattleClient(transport);
            Instance = client;
            return client;
        }

        /// <summary>
        /// 宿主每帧驱动(GameClient.Tick 传 Time.realtimeSinceStartup)。
        /// 负责排队轮询与 Preparing 超时,纯逻辑可注入假时钟测试。
        /// </summary>
        public void Tick(double nowSeconds)
        {
            _now = nowSeconds;

            if (Phase == BattlePhase.Queued && _net.IsReady && _now >= _nextQueuePollAt)
            {
                _nextQueuePollAt = _now + QueuePollIntervalSeconds;
                PollQueueStatus();
            }

            if (Phase == BattlePhase.Preparing && _preparingDeadline > 0 && _now >= _preparingDeadline)
            {
                _preparingDeadline = 0;
                OnError?.Invoke("战斗准备超时,已退出匹配");
                SetPhase(BattlePhase.None);
            }
        }

        // ── 契约方法:匹配队列 ──────────────────────────────

        public void JoinQueue(Match.MatchMode mode, uint battleConfigId)
        {
            if (Phase != BattlePhase.None)
            {
                OnError?.Invoke("当前状态不能排队(需先结束当前匹配/战斗)");
                return;
            }

            // 连续战斗的参数记忆:含自动重排本身(同值覆写无害),新排队作废未消费的结束 Ack
            _lastQueueMode = mode;
            _lastQueueConfigId = battleConfigId;
            _hasQueueMemory = true;
            _endAckPending = false;

            // 先进 Queued 再发请求:失败响应回退 None;若开战抢先到达
            // (solo 即配),迟到的成功响应不允许把相位拉回 Queued。
            SetPhase(BattlePhase.Queued);
            _nextQueuePollAt = _now + QueuePollIntervalSeconds;

            var req = new Match.JoinQueueRequest
            {
                PlayerId = MyPlayerId,
                Mode = mode,
                BattleConfigId = battleConfigId,
            };
            _net.Call(MessageIds.JoinQueue, req, Match.JoinQueueResponse.Parser,
                resp =>
                {
                    // 拒绝原因无论相位是否已变都要抛出来:典型场景是 JoinQueue 发出后
                    // NotifyBattleReconnect 才到、相位已被权威状态拉到 WaitingAction,
                    // 服务端 ErrInBattle 若被静默吞掉,自动驾驶/UI 只能空等超时。
                    bool rejected = resp.ErrorCode != 0 || HasTip(resp.ErrorMessage);
                    if (rejected)
                        OnError?.Invoke(DescribeTip("加入队列失败", resp.ErrorMessage, resp.ErrorCode));
                    if (Phase != BattlePhase.Queued) return; // 已开战/已取消/重连恢复:迟到响应不改相位
                    if (rejected)
                    {
                        SetPhase(BattlePhase.None);
                        return;
                    }
                    _queueTicket = resp.QueueTicket ?? string.Empty;
                },
                err =>
                {
                    OnError?.Invoke($"加入队列失败:{err}");
                    if (Phase != BattlePhase.Queued) return;
                    SetPhase(BattlePhase.None);
                });
        }

        public void CancelQueue()
        {
            if (Phase != BattlePhase.Queued)
            {
                OnError?.Invoke("当前不在排队中");
                return;
            }

            var req = new Match.CancelQueueRequest
            {
                PlayerId = MyPlayerId,
                QueueTicket = _queueTicket,
            };
            _net.Call(MessageIds.CancelQueue, req, Empty.Parser,
                _ =>
                {
                    // 收敛:仍在排队才回 None(若期间已开战则维持战斗相位)
                    if (Phase == BattlePhase.Queued) SetPhase(BattlePhase.None);
                },
                err =>
                {
                    // 取消失败(可能恰好被凑单):不强行回 None,
                    // 交由 GetQueueStatus 轮询 / NotifyBattleStart 收敛。
                    OnError?.Invoke($"取消排队失败:{err}");
                });
        }

        private void PollQueueStatus()
        {
            var req = new Match.GetQueueStatusRequest { PlayerId = MyPlayerId };
            _net.Call(MessageIds.GetQueueStatus, req, Match.GetQueueStatusResponse.Parser,
                resp =>
                {
                    OnQueueStatus?.Invoke(resp);
                    if (Phase != BattlePhase.Queued) return; // 已开战:轮询结果只透传不改相位
                    switch (resp.State)
                    {
                        case Match.QueueState.Matched:
                        case Match.QueueState.Ready:
                        case Match.QueueState.Entering:
                            // 凑单成功:停止轮询(离开 Queued 即停),等 NotifyBattleStart
                            EnterPreparing();
                            break;
                        case Match.QueueState.NotQueued:
                            // 服务端已不认识我们(取消成功/超时被清):收敛回 None
                            SetPhase(BattlePhase.None);
                            break;
                    }
                },
                err => OnError?.Invoke($"查询排队状态失败:{err}"));
        }

        // ── 契约方法:切磋(场景发起 PK) ───────────────────

        public void ChallengePlayer(ulong targetPlayerId)
        {
            var req = new Match.ChallengePlayerRequest
            {
                PlayerId = MyPlayerId,
                TargetPlayerId = targetPlayerId,
                BattleConfigId = 0, // 0 = 默认切磋规则(match_service.proto)
            };
            _net.Call(MessageIds.ChallengePlayer, req, Match.ChallengePlayerResponse.Parser,
                resp =>
                {
                    if (HasTip(resp.ErrorMessage))
                    {
                        OnError?.Invoke(DescribeTip("发起切磋失败", resp.ErrorMessage));
                        return;
                    }
                    // 成功只表示邀约已挂出:等待 NotifyChallengeResult(接受→Preparing)
                },
                err => OnError?.Invoke($"发起切磋失败:{err}"));
        }

        public void RespondChallenge(ulong challengeId, bool accept)
        {
            var req = new Match.RespondChallengeRequest
            {
                PlayerId = MyPlayerId,
                ChallengeId = challengeId,
                Accept = accept,
            };
            _net.Call(MessageIds.RespondChallenge, req, Match.RespondChallengeResponse.Parser,
                resp =>
                {
                    if (HasTip(resp.ErrorMessage))
                    {
                        OnError?.Invoke(DescribeTip("应战失败", resp.ErrorMessage));
                        return;
                    }
                    // 应战成功:进入 gather,等 NotifyBattleStart(带超时保护)
                    if (accept && Phase == BattlePhase.None) EnterPreparing();
                },
                err => OnError?.Invoke($"应战失败:{err}"));
        }

        // ── 契约方法:战斗内 ────────────────────────────────

        public void SubmitAction(BattleAction action)
        {
            if (Phase != BattlePhase.WaitingAction)
            {
                OnError?.Invoke("当前不能提交行动");
                return;
            }
            if (action == null)
            {
                OnError?.Invoke("行动指令为空");
                return;
            }

            var req = new SubmitBattleActionRequest
            {
                BattleId = _battleId,
                Action = action,
            };
            _net.Call(MessageIds.SubmitBattleAction, req, SubmitBattleActionResponse.Parser,
                resp =>
                {
                    if (HasTip(resp.ErrorMessage))
                        OnError?.Invoke(DescribeTip("提交行动失败", resp.ErrorMessage));
                    // 成功不改相位:等 NotifyTurnResult 驱动 Resolving
                },
                err => OnError?.Invoke($"提交行动失败:{err}"));
        }

        /// <summary>
        /// UI 播完一回合表现后调用(契约):Resolving → WaitingAction。
        /// Ended/None 之后的迟到 Ack 丢弃。
        /// </summary>
        public void AckTurnPlayed()
        {
            if (Phase != BattlePhase.Resolving) return;
            SetPhase(BattlePhase.WaitingAction);
        }

        /// <summary>
        /// 自动战斗开关(§11.2):同步更新本地记忆 AutoBattleLatched;
        /// 战斗中(WaitingAction/Resolving)立即发 SetAutoBattle,否则只记忆
        /// (下一场 BattleStart 自动补发)。权威状态经 OnAutoStateChanged 回来。
        /// </summary>
        public void SetAutoBattle(bool enabled)
        {
            AutoBattleLatched = enabled;
            if (_battleId == 0) return; // 不在战斗:纯记忆,不报错(排队面板预设场景)
            if (Phase != BattlePhase.WaitingAction && Phase != BattlePhase.Resolving) return;
            SendSetAutoBattle(enabled, revertLatchOnFailure: true);
        }

        /// <summary>
        /// UI 收起结算面板后调用(契约):消费结束 Ack;连续战斗开关打开且有
        /// 排队参数记忆时自动重新 JoinQueue(§11.2)。相位早已回 None(既有
        /// Ended→None 流转不变),故本方法不改相位;迟到/多余 Ack 丢弃。
        /// </summary>
        public void AckBattleEnd()
        {
            if (!_endAckPending) return;
            _endAckPending = false;
            if (!ContinuousBattle || !_hasQueueMemory) return;
            if (Phase != BattlePhase.None) return; // 已在新排队/新战斗(重连等):不抢入口
            JoinQueue(_lastQueueMode, _lastQueueConfigId);
        }

        /// <summary>
        /// 补拉权威状态(重连/异常兜底):GetBattleState,按返回态置
        /// WaitingAction / Resolving(战斗已结束则收敛回 None)。
        /// </summary>
        public void RequestState()
        {
            if (_battleId == 0)
            {
                OnError?.Invoke("没有进行中的战斗,无法补拉状态");
                return;
            }
            var req = new GetBattleStateRequest { BattleId = _battleId };
            _net.Call(MessageIds.GetBattleState, req, BattleStateS2C.Parser,
                ApplyAuthoritativeState,
                err => OnError?.Invoke($"拉取战斗状态失败:{err}"));
        }

        // ── 相位决策(纯函数,EditMode 直接测) ─────────────

        /// <summary>
        /// 按权威状态决定客户端相位(重连补拉决策):
        /// 战斗已出胜负 → None(结算走 scene,客户端无事可做);
        /// 本人在 pending_actor_ids(未提交行动)→ WaitingAction;
        /// 否则(已提交/已死亡,等他人或等结算广播)→ Resolving。
        /// </summary>
        public static BattlePhase DecidePhaseFromState(BattleStateS2C state, ulong myPlayerId)
        {
            if (state == null) return BattlePhase.None;
            // protoc 的 C# 代码生成保留 proto 原始枚举名(eBattleOutcome,小写 e 开头)
            if (state.Outcome != eBattleOutcome.BattleOutcomeOngoing) return BattlePhase.None;
            foreach (ulong id in state.PendingActorIds)
            {
                if (id == myPlayerId) return BattlePhase.WaitingAction;
            }
            return BattlePhase.Resolving;
        }

        // ── S2C 推送处理 ────────────────────────────────────

        private void RegisterNotifies()
        {
            _net.RegisterNotify(MessageIds.NotifyBattleStart,
                mc => HandleBattleStart(BattleStartS2C.Parser.ParseFrom(mc.SerializedMessage)));
            _net.RegisterNotify(MessageIds.NotifyTurnResult,
                mc => HandleTurnResult(TurnResultS2C.Parser.ParseFrom(mc.SerializedMessage)));
            _net.RegisterNotify(MessageIds.NotifyBattleEnd,
                mc => HandleBattleEnd(BattleEndS2C.Parser.ParseFrom(mc.SerializedMessage)));
            _net.RegisterNotify(MessageIds.NotifyBattleReconnect,
                mc => HandleBattleReconnect(BattleReconnectS2C.Parser.ParseFrom(mc.SerializedMessage)));
            _net.RegisterNotify(MessageIds.NotifyChallengeInvite,
                mc => OnChallengeInvite?.Invoke(Match.ChallengeInviteS2C.Parser.ParseFrom(mc.SerializedMessage)));
            _net.RegisterNotify(MessageIds.NotifyChallengeResult,
                mc => HandleChallengeResult(Match.ChallengeResultS2C.Parser.ParseFrom(mc.SerializedMessage)));
            // 注意:不注册 MessageIds.TipToClient —— 那是 GameClient 场景层的
            // 全局提示通道(OnNotify 同 id 只保留最后一次注册,抢注会踩掉场景处理)。
        }

        private void HandleBattleStart(BattleStartS2C ev)
        {
            if (ev == null) return;

            _queueTicket = string.Empty;
            _battleId = ev.BattleId != 0 ? ev.BattleId : ev.State?.BattleId ?? 0;
            State = ev.State;

            // 排队中收到开战:补发 Preparing 让 UI 收起排队面板(Queued→Preparing→WaitingAction)
            if (Phase == BattlePhase.Queued) SetPhase(BattlePhase.Preparing);

            // 事件先于终相位:UI 先拿 BattleStartS2C 开屏,再收 WaitingAction 刷新输入区
            OnBattleStart?.Invoke(ev);

            var phase = DecidePhaseFromState(ev.State, MyPlayerId);
            if (phase == BattlePhase.None) phase = BattlePhase.WaitingAction; // 开局态不可能已结束,容错
            SetPhase(phase);
            RefreshMyAutoState();

            // 自动战斗记忆补发(§11.2):新战斗引擎侧 is_auto 归零,由本地记忆续上
            if (AutoBattleLatched) SendSetAutoBattle(true, revertLatchOnFailure: false);
        }

        private void HandleTurnResult(TurnResultS2C ev)
        {
            if (ev == null) return;
            // 迟到/错发的回合结果丢弃:不在战斗中(Ended 已收尾回 None),或 battle_id 不匹配
            if (Phase != BattlePhase.WaitingAction && Phase != BattlePhase.Resolving) return;
            if (_battleId != 0 && ev.BattleId != 0 && ev.BattleId != _battleId) return;

            if (ev.State != null) State = ev.State;
            RefreshMyAutoState();
            SetPhase(BattlePhase.Resolving);
            OnTurnResult?.Invoke(ev); // UI 播完调 AckTurnPlayed() 回 WaitingAction
        }

        private void HandleBattleEnd(BattleEndS2C ev)
        {
            if (ev == null) return;
            // 只接受当前战斗(含重连恢复中)的结束消息;无上下文的迟到消息丢弃
            if (_battleId == 0) return;
            if (ev.BattleId != 0 && ev.BattleId != _battleId) return;

            SetPhase(BattlePhase.Ended);
            OnBattleEnd?.Invoke(ev);   // 先抛事件(UI 记结算/排队播完),再收尾回 None
            ClearBattleContext();
            SetPhase(BattlePhase.None);
            RefreshMyAutoState();      // 战斗上下文已清 → 权威 auto 归 false
            _endAckPending = true;     // 收尾之后才挂闸:事件回调内的同步 Ack 视为无效
        }

        private void HandleBattleReconnect(BattleReconnectS2C ev)
        {
            if (ev == null || ev.BattleId == 0) return;
            // 契约:NotifyBattleReconnect 到达 → 自动 RequestState(),
            // 相位由返回的权威状态决定(WaitingAction/Resolving)。
            _battleId = ev.BattleId;
            _queueTicket = string.Empty;
            _preparingDeadline = 0;
            RequestState();
        }

        private void HandleChallengeResult(Match.ChallengeResultS2C ev)
        {
            if (ev == null) return;
            // 发起者:对方应战成功 → 进入 gather 等 NotifyBattleStart
            //(应战者一侧已在 RespondChallenge 响应回调里进入 Preparing,此处 Phase 已非 None)
            if (ev.Accepted && Phase == BattlePhase.None) EnterPreparing();
            OnChallengeResult?.Invoke(ev);
        }

        private void HandleDisconnected()
        {
            // 断线:本地战斗/排队态整体作废(补偿矩阵:排队票据失效;战斗照打,
            // 重连后 scene 发现 InBattleComp 会推 NotifyBattleReconnect → RequestState 恢复)。
            // 未消费的结束 Ack 一并作废:断线重连后不允许凭旧 Ack 自动入队。
            ClearBattleContext();
            State = null;
            _endAckPending = false;
            SetPhase(BattlePhase.None);
            RefreshMyAutoState();
        }

        private void ApplyAuthoritativeState(BattleStateS2C state)
        {
            if (state == null) return;
            if (state.BattleId != 0) _battleId = state.BattleId;
            State = state;

            var phase = DecidePhaseFromState(state, MyPlayerId);
            if (phase == BattlePhase.None) ClearBattleContext(); // 战斗已结束:结算由 scene 侧另行通知
            SetPhase(phase);
            RefreshMyAutoState();
        }

        // ── 内部工具 ────────────────────────────────────────

        /// <summary>
        /// 发 SetAutoBattle(不含记忆更新;记忆由公开 API/补发路径各自负责)。
        /// revertLatchOnFailure:失败时把 AutoBattleLatched 回滚到权威值 IsMyActorAuto,
        /// 免得用户点一次失败后本地意愿与权威态分叉(得点两次才真正生效)。
        /// 用户显式开关传 true;BattleStart 记忆补发传 false —— 补发瞬时失败
        /// 不得抹掉跨场挂机记忆(§11.2 连续挂机语义)。
        /// </summary>
        private void SendSetAutoBattle(bool enabled, bool revertLatchOnFailure)
        {
            var req = new SetAutoBattleRequest
            {
                BattleId = _battleId,
                Enabled = enabled,
            };
            _net.Call(MessageIds.SetAutoBattle, req, SetAutoBattleResponse.Parser,
                resp =>
                {
                    if (HasTip(resp.ErrorMessage))
                    {
                        OnError?.Invoke(DescribeTip("设置自动战斗失败", resp.ErrorMessage));
                        RevertLatchIfNeeded(revertLatchOnFailure);
                    }
                    // 成功不改本地态:权威 is_auto 随下一帧 BattleStateS2C 回来
                },
                err =>
                {
                    OnError?.Invoke($"设置自动战斗失败:{err}");
                    RevertLatchIfNeeded(revertLatchOnFailure);
                });
        }

        /// <summary>
        /// SetAutoBattle 失败后的记忆回滚:_battleId==0 说明战斗已收尾,此时
        /// latch 是下一场预设,不回滚。权威值本身未变,不触发 OnAutoStateChanged。
        /// </summary>
        private void RevertLatchIfNeeded(bool revertLatchOnFailure)
        {
            if (revertLatchOnFailure && _battleId != 0)
                AutoBattleLatched = IsMyActorAuto;
        }

        /// <summary>
        /// 从权威状态重算本人 is_auto,变化时抛 OnAutoStateChanged。
        /// 战斗上下文已清(_battleId==0)时恒为 false(State 保留旧值也不误报)。
        /// </summary>
        private void RefreshMyAutoState()
        {
            bool isAuto = false;
            if (_battleId != 0 && State != null)
            {
                foreach (var actor in State.Actors)
                {
                    if (actor.ActorId != MyPlayerId) continue;
                    isAuto = actor.IsAuto;
                    break;
                }
            }
            if (isAuto == IsMyActorAuto) return;
            IsMyActorAuto = isAuto;
            OnAutoStateChanged?.Invoke(isAuto);
        }

        private void EnterPreparing()
        {
            SetPhase(BattlePhase.Preparing);
            _preparingDeadline = _now + PreparingTimeoutSeconds; // SetPhase 会清,故在其后挂
        }

        private void ClearBattleContext()
        {
            _battleId = 0;
            _queueTicket = string.Empty;
            _preparingDeadline = 0;
        }

        private void SetPhase(BattlePhase phase)
        {
            if (Phase == phase) return;
            if (phase != BattlePhase.Preparing) _preparingDeadline = 0;
            Phase = phase;
            OnPhaseChanged?.Invoke(phase);
        }

        private static bool HasTip(TipInfoMessage tip) => tip != null && tip.Id != 0;

        private static string DescribeTip(string what, TipInfoMessage tip, uint errorCode = 0)
            => tip != null && tip.Id != 0 ? $"{what}(tip={tip.Id})" : $"{what}(code={errorCode})";
    }
}
