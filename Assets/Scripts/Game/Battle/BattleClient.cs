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
    /// <see cref="DirectRoutingBattleTransport"/>:战斗 RPC 走战斗直连,其余接 GameClient 既有管线;
    /// EditMode 测试注入假实现)与可选的 <see cref="IBattleChannel"/>(直连就绪状态;
    /// null = 永远就绪,演出台与既有测试用),本类不引用 UnityEngine —— 时钟由宿主
    /// 经 <see cref="Tick"/> 注入(GameClient.Tick 传 Time.realtimeSinceStartup)。
    ///
    /// 边界情况(均有 EditMode 测试覆盖):
    ///  - 排队中收到开战:Queued → Preparing → WaitingAction 连跳;
    ///  - 战斗中断线:本地态整体作废回 None,重连后服务端推 NotifyBattleReconnect
    ///    → 自动 RequestState() 按权威状态恢复 WaitingAction/Resolving
    ///    (直连未就绪时推迟到直连就绪再补拉);
    ///  - 战斗直连(turn-based §22 D74):直连是战斗唯一通路,未就绪时出手本地拒绝
    ///    (SubmitAction 返回 false)、自动战斗只记意愿、补拉推迟;就绪时补拉权威状态
    ///    (开局到握手之间、断线期间的战斗帧不经大厅回落)并补发自动战斗意愿;
    ///    直连终结为 BattleGone(战斗已结束)收敛回 None,Unreachable 保持相位并抛
    ///    OnBattleChannelFailed(服务端回合超时替本人默认出手,玩家可 RetryBattleChannel);
    ///    本局未收场时链路被另一局的分配包拿走(Superseded),参战优先,立即 EnsureBattle 要回来;
    ///    补拉遇传输错误而同一局直连仍就绪(链路 Verified 时的 rpc 超时等,不会再有就绪事件)时,
    ///    按 1s / 2s / 4s(±20%)有上限退避自动重拉,换局 / 收尾 / 断线 / 下一次就绪 / 显式补拉时作废计划;
    ///  - Ended/None 后收到迟到 TurnResultS2C:丢弃;NotifyBattleEnd 可能经直连与大厅
    ///    (scene 结算推送)各到一份,按 battle_id 幂等;
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

        /// <summary>直连判定连不上(Unreachable)时 <see cref="OnBattleChannelFailed"/> 带出的文案(客户端本地字符串)。</summary>
        public const string BattleChannelFailedText = "无法连接战斗服务器,本局将由系统自动出手,可点击重新连接";

        /// <summary>直连未就绪时 <see cref="SubmitAction"/> 经 OnError 给出的文案。</summary>
        public const string BattleChannelConnectingText = "正在连接战斗服务器,请稍候…";

        /// <summary>
        /// 补拉(GetBattleState)遇传输错误、而同一局直连仍就绪时的自动重拉次数上限(turn-based §22 D74)。
        /// 链路没断就不会再有就绪事件替它补拉:不重拉的话相位停在 None,经直连到的回合结果又被相位闸丢弃。
        /// 设上限是因为链路半死时每次都要等满直连调用超时,无限重拉只会持续占 battle 的按消息号限速;
        /// 用完后由主城入口「返回战斗」(<see cref="RequestState"/>)或下一次就绪兜底。
        /// </summary>
        public const int MaxStatePullRetries = 3;
        /// <summary>自动重拉的退避基数(秒):第 k 次重拉前等 基数 × 2^(k-1),即 1s / 2s / 4s(与直连补签同一节奏)。</summary>
        public const double StatePullRetryBaseSeconds = 1.0;
        /// <summary>自动重拉的退避抖动幅度(±20%):同一 battle 节点抖动时,别让一批客户端同一时刻重拉。</summary>
        public const double StatePullRetryJitter = 0.2;

        private readonly IBattleTransport _net;
        private readonly IBattleChannel _channel;   // null = 永远就绪(演出台 / 既有测试)
        private readonly Func<double> _random01;    // 自动重拉退避抖动用的 [0,1) 随机源

        private double _now;                 // 宿主注入的时钟(秒)
        private double _nextQueuePollAt;     // 下一次排队轮询时刻
        private double _preparingDeadline;   // Preparing 超时时刻(0 = 未挂)

        private bool _cancelVerifyPending;   // 被带入的排队点了取消、等下一次轮询结果确认是否真的退出

        // 排队状态查询(GetQueueStatus:轮询与 ProbeQueueStatus)同一时刻至多一个在途。
        // 在途期间再要求轮询只记一笔「回来后再问」，避免重复请求并维持取消后的求证顺序。
        private int _queuePollSeq;           // 发号器
        private int _queuePollInFlightSeq;   // 在途那一次的序号;0 = 没有在途
        private bool _queuePollAgain;
        // 排队会话序号:每次 JoinQueue / AdoptQueue 进入排队、以及断线时自增(准备超时退回排队不算新会话)。
        // 轮询与取消的回调只处理发出时那一次排队的回包,上一次排队迟到的结果整包丢弃。
        private int _queueSession;
        private bool _endedByTicket;         // 这一次离开排队是因为服务端说「已不在排」(见 AdoptedQueueEndedByTicket)
        private ulong _battleId;             // 当前(或重连恢复中)战斗 id;0 = 无
        private string _queueTicket = string.Empty;
        public RegionProbeClient RegionProbe { get; set; }
        private bool _globalQueue;
        private bool _lastTransportReady;
        private int _queueGeneration;
        private int _queuePollSequence;
        private bool _joinSent;
        private double _refreshProbeAt;
        public IMatchJoinRecovery QueueRecovery { get; set; }
        private Match.JoinQueueRequest _joinIntent;
        private ulong _queueIntentPlayer;
        private bool _joinOutcomeUnknown;
        private bool _cancelRequested;
        private bool _recoveryBlocked;
        private sealed class JoinRequestIdentity
        {
            public int Generation, QueueSession;
            public ulong Player;
        }
        private JoinRequestIdentity _inFlightJoinRequest;
        // 权威开战/恢复可提前收尾Join；只给原请求保留一次明确拒绝诊断，不准再修改任何队列状态。
        private JoinRequestIdentity _lateJoinDiagnostic;

        // 连续战斗:最近一次显式 JoinQueue 的参数记忆(切磋不计,§11.2 只按队列参数重排)
        private Match.MatchMode _lastQueueMode;
        private uint _lastQueueConfigId;
        private uint _lastQueueTeamSize;
        private bool _hasQueueMemory;
        private bool _endAckPending;         // 战斗已结束、等 UI AckBattleEnd(连续战斗的触发闸)

        // 战斗直连(turn-based §22 D74)
        private bool _autoResendPending;     // 自动战斗意愿待直连就绪后补发(值取 AutoBattleLatched)
        private bool _channelFailed;         // 本局直连已判定连不上(Unreachable),等就绪 / 手动重连 / 收尾
        private ulong _statePullBattleId;    // 在途 GetBattleState 所属战斗;0 = 无在途(补拉去重)
        // 补拉遇传输错误后的自动重拉计划(见 MaxStatePullRetries)。计划只对当前这一局有效:
        // 换局 / 收尾 / 断线 / 下一次就绪 / 显式补拉 / 补拉成功都经 ResetStatePullRetry 作废计划并归零预算。
        private double _statePullRetryAt;    // >0 = 到点重拉(退避中)
        private int _statePullRetries;       // 本轮已排的自动重拉次数
        // 已按权威信号收场(直连 BattleGone / 补拉到已出胜负)、却还没收到 NotifyBattleEnd 的战斗:
        // 允许它的终局包(scene 结算后经大厅推)晚到,UI 仍能出结算面板。0 = 无。
        private ulong _endPendingBattleId;

        // ── 契约属性 ────────────────────────────────────────

        public BattlePhase Phase { get; private set; } = BattlePhase.None;

        /// <summary>
        /// 当前的排队不是本机 JoinQueue 发起的,而是随队伍被带进队列的(队伍补位匹配,见 <see cref="AdoptQueue"/>)。
        /// 在排队态与凑满后的准备阶段(Queued / Preparing)保持为真 —— 准备阶段可能因开局失败退回排队;
        /// 进入战斗或回到空闲时清掉。
        /// </summary>
        public bool QueueAdopted { get; private set; }

        /// <summary>被带进队列时由调用方给的说明文案(排队面板显示用);非 QueueAdopted 时为空串。</summary>
        public string AdoptedQueueHint { get; private set; } = string.Empty;

        /// <summary>
        /// 上一次被带入的排队是不是因为服务端说「已不在排」而结束的(票据证据);false = 因开战、断线等其它原因离开。
        /// 在被带入的排队结束的那一刻更新,相位事件触发时已经就绪。
        /// </summary>
        public bool AdoptedQueueEndedByTicket { get; private set; }

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

        /// <summary>
        /// 本局战斗直连已就绪,可以出手 / 切自动战斗(turn-based §22 D74)。
        /// 无直连通道(演出台 / 测试)恒为 true;有通道但不在战斗中(无 battle_id)为 false。
        /// UI 据此置灰出手按钮、显示「正在连接战斗服务器…」。本局进行中由 false 变 true 时抛
        /// <see cref="OnBattleChannelReady"/>;直连先于开局包就绪的,开局时直接为 true、不另抛事件。
        /// </summary>
        public bool IsBattleChannelReady => _channel == null || (_battleId != 0 && _channel.IsReadyFor(_battleId));

        /// <summary>
        /// 本局直连已判定连不上(Unreachable,恢复预算用完)。从 <see cref="OnBattleChannelFailed"/>
        /// 起保持 true,直到直连就绪 / <see cref="RetryBattleChannel"/> / 本局收尾;
        /// UI 重新绑定时据此恢复「无法连接战斗服务器」横幅。
        /// </summary>
        public bool IsBattleChannelFailed => _channelFailed;

        /// <summary>
        /// 有进行中的对局(含大厅重连后、直连补签 / 补拉尚未完成的恢复中对局),即持有非 0 的 battle_id。
        /// 从开局包 / NotifyBattleReconnect 起为 true,到终局包 / 直连 BattleGone / 补拉到已出胜负 / 大厅断线收尾为止。
        /// 与 <see cref="Phase"/> 的区别:大厅重连后权威状态要经直连补拉,补拉成功前(含直连已就绪而补拉以传输错误失败)相位停在 None,
        /// 只有这里能说明「本人仍在战斗中」。UI 据此在战斗屏没开时借用主城入口(「连接战斗中…」/「重新连接战斗」/
        /// 「返回战斗」,见 BattleUiRoot.DecideEntryMode;turn-based §22 D74),免得玩家点开排队面板、再被服务端以在战斗中拒绝。
        /// </summary>
        public bool HasActiveBattle => _battleId != 0;

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

        /// <summary>
        /// 本局战斗直连就绪(含断线重连成功)。触发时本类已发出状态补拉与挂起的自动战斗意愿;
        /// UI 据此收起「正在连接 / 无法连接」提示并重新评估出手按钮。
        /// </summary>
        public event Action OnBattleChannelReady;

        /// <summary>
        /// 本局直连连不上(恢复预算用完,参数为给玩家看的文案 <see cref="BattleChannelFailedText"/>)。
        /// 相位保持不变(服务端回合超时替本人默认出手);UI 常驻横幅并提供「重新连接」
        /// (调 <see cref="RetryBattleChannel"/>)。手动重连再次失败会再次触发。
        /// </summary>
        public event Action<string> OnBattleChannelFailed;

        // ── 构造/挂接 ───────────────────────────────────────

        /// <param name="transport">战斗网络传输(生产:DirectRoutingBattleTransport)。</param>
        /// <param name="channel">战斗直连就绪状态(生产:同一个 DirectRoutingBattleTransport);null = 永远就绪。</param>
        /// <param name="random01">
        /// 自动重拉退避抖动用的 [0,1) 随机源;空 = System.Random。只在主线程调用(同 BattleDirectLink)。
        /// 测试传常量(0.5 = 无抖动)让退避时序确定;时钟本就经 <see cref="Tick"/> 注入。
        /// </param>
        public BattleClient(IBattleTransport transport, IBattleChannel channel = null, Func<double> random01 = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _channel = channel;
            if (random01 == null)
            {
                var rng = new Random();
                random01 = rng.NextDouble;
            }
            _random01 = random01;
            RegisterNotifies();
            _net.Disconnected += HandleDisconnected;
            if (_channel != null)
            {
                _channel.Ready += HandleChannelReady;
                _channel.Lost += HandleChannelLost;
            }
        }

        /// <summary>生产入口:创建实例并登记为单例(GameClient 构造时调用)。channel 语义同构造函数。</summary>
        public static BattleClient Attach(IBattleTransport transport, IBattleChannel channel = null)
        {
            var client = new BattleClient(transport, channel);
            Instance = client;
            return client;
        }

        /// <summary>
        /// 宿主每帧驱动(GameClient.Tick 传 Time.realtimeSinceStartup)。
        /// 负责排队轮询、Preparing 超时与补拉的自动重拉,纯逻辑可注入假时钟测试。
        /// </summary>
        public void Tick(double nowSeconds)
        {
            _now = nowSeconds;
            RestoreQueueIntent();
            if (RegionProbe != null && !_net.IsReady && _lastTransportReady) { ++_queueGeneration; RegionProbe.Dispose(); }
            RegionProbe?.Tick(nowSeconds);
            if (RegionProbe != null && _net.IsReady && !_lastTransportReady)
            {
                if (_joinIntent != null && Phase == BattlePhase.None) SetPhase(BattlePhase.Queued);
                if (_cancelRequested) SendGlobalCancellation(); else PollQueueStatus();
            }
            _lastTransportReady = _net.IsReady;
            if (_globalQueue && !_cancelRequested && !_joinOutcomeUnknown && Phase == BattlePhase.Queued && _net.IsReady && _now >= _refreshProbeAt && RegionProbe != null && !RegionProbe.Busy)
            {
                _refreshProbeAt = _now + 25;
                RegionProbe.Prepare(_ => { }, error => OnError?.Invoke(error));
            }

            // 补拉遇传输错误后的自动重拉(见 MaxStatePullRetries)。到点时直连已不就绪(断开 / 改服务别的局):
            // 放弃本次,下一次就绪事件自会补拉并重置预算;同局补拉在途时 PullState 自行去重
            if (_statePullRetryAt > 0 && _now >= _statePullRetryAt)
            {
                _statePullRetryAt = 0;
                if (IsBattleChannelReady) PullState();
            }

            if ((Phase == BattlePhase.Queued || (_globalQueue && Phase == BattlePhase.Preparing)) && _net.IsReady && _now >= _nextQueuePollAt)
            {
                _nextQueuePollAt = _now + QueuePollIntervalSeconds;
                PollQueueStatus();
            }

            if (Phase == BattlePhase.Preparing && _preparingDeadline > 0 && _now >= _preparingDeadline)
            {
                _preparingDeadline = 0;
                if (QueueAdopted)
                {
                    // 队伍补位:凑满后开局失败(同组有人备战失败)时,服务端把整队放回队首,票据回到排队态,
                    // 而本机收不到任何推送。回到排队态并立刻问一次服务端,由 GetQueueStatus 决定去留
                    // (仍在排 / 已不在排 / 又凑满),不报「已退出匹配」。
                    SetPhase(BattlePhase.Queued);
                    _nextQueuePollAt = _now;
                }
                else if (_globalQueue)
                {
                    PollQueueStatus();
                }
                else
                {
                    OnError?.Invoke("战斗准备超时,已退出匹配");
                    SetPhase(BattlePhase.None);
                }
            }
        }

        // ── 契约方法:匹配队列 ──────────────────────────────

        /// <param name="teamSize">
        /// PVE 人数档(JoinQueueRequest.team_size,只有 PveTeam 模式读它):想凑成几人的队伍,
        /// 怪物数量跟参战人数走(每人一整组)。0 = 该副本的人数上限;其余模式传 0。
        /// </param>
        public void JoinQueue(Match.MatchMode mode, uint battleConfigId, uint teamSize = 0)
        {
            RestoreQueueIntent();
            if (Phase != BattlePhase.None)
            {
                OnError?.Invoke("当前状态不能排队(需先结束当前匹配/战斗)");
                return;
            }

            // 连续战斗的参数记忆:含自动重排本身(同值覆写无害),新排队作废未消费的结束 Ack
            _lastQueueMode = mode;
            _lastQueueConfigId = battleConfigId;
            _lastQueueTeamSize = teamSize;
            _hasQueueMemory = true;
            _endAckPending = false;

            // 先进 Queued 再发请求:失败响应回退 None;若开战抢先到达
            // (solo 即配),迟到的成功响应不允许把相位拉回 Queued。
            ClearJoinDiagnostics();
            _queueSession++;
            SetPhase(BattlePhase.Queued);
            _nextQueuePollAt = _now + QueuePollIntervalSeconds;

            var req = new Match.JoinQueueRequest
            {
                PlayerId = MyPlayerId,
                Mode = mode,
                BattleConfigId = battleConfigId,
                TeamSize = teamSize,
                RequestId = Guid.NewGuid().ToString("D"),
            };
            int generation = ++_queueGeneration;
            _queueIntentPlayer = MyPlayerId;
            _joinIntent = null; _cancelRequested = false; _joinOutcomeUnknown = false;
            _joinSent = false;
            if (RegionProbe != null)
            {
                RegionProbe.Prepare(plan =>
                {
                    if (generation != _queueGeneration || Phase != BattlePhase.Queued) return;
                    _globalQueue = plan.Length > 0; req.NetworkPlanId = plan; _refreshProbeAt = _now + 25;
                    SendJoin();
                }, error => { if (generation == _queueGeneration && Phase == BattlePhase.Queued) { OnError?.Invoke(error); SetPhase(BattlePhase.None); } });
                return;
            }
            SendJoin();

            void SendJoin()
            {
            if (_globalQueue)
            {
                try { QueueRecovery?.Save(req, false); }
                catch (Exception) { OnError?.Invoke("无法保存匹配恢复记录，本次尚未提交。请检查本机存储后重试。"); SetPhase(BattlePhase.None); return; }
                _joinIntent = req; _joinOutcomeUnknown = true;
            }
            _joinSent = true;
            _nextQueuePollAt = _now + QueuePollIntervalSeconds;
            var identity = new JoinRequestIdentity { Generation = generation, QueueSession = _queueSession, Player = req.PlayerId };
            _inFlightJoinRequest = identity;
            _net.Call(MessageIds.JoinQueue, req, Match.JoinQueueResponse.Parser,
                resp =>
                {
                    // 拒绝原因无论相位是否已变都要抛出来:典型场景是 JoinQueue 发出后
                    // NotifyBattleReconnect 才到、相位已被权威状态拉到 WaitingAction,
                    // 服务端 ErrInBattle 若被静默吞掉,自动驾驶/UI 只能空等超时。
                    bool rejected = resp.ErrorCode != 0 || HasTip(resp.ErrorMessage);
                    if (!CompleteJoinRequest(identity, out bool diagnosticAllowed))
                    {
                        if (diagnosticAllowed && rejected)
                            OnError?.Invoke(DescribeTip("加入队列失败", resp.ErrorMessage, resp.ErrorCode));
                        return;
                    }
                    if (_globalQueue && rejected)
                    {
                        // 事务提交后的依赖/回包故障仍可能表现为失败；旧票号返回则恢复那张真实票。
                        if (!string.IsNullOrEmpty(resp.QueueTicket))
                        {
                            ClearQueueIntent(); _queueTicket = resp.QueueTicket; PollQueueStatus();
                        }
                        else PollQueueStatus();
                        return;
                    }
                    if (rejected)
                        OnError?.Invoke(DescribeTip("加入队列失败", resp.ErrorMessage, resp.ErrorCode));
                    if (Phase != BattlePhase.Queued) return; // 已开战/已取消/重连恢复:迟到响应不改相位
                    if (rejected)
                    {
                        SetPhase(BattlePhase.None);
                        return;
                    }
                    _queueTicket = resp.QueueTicket ?? string.Empty;
                    if (_globalQueue) { _joinOutcomeUnknown = false; TryClearRecovery(); }
                },
                err =>
                {
                    if (!CompleteJoinRequest(identity, out _)) return;
                    OnError?.Invoke($"加入队列失败:{err}");
                    if (Phase != BattlePhase.Queued) return;
                    if (_globalQueue) PollQueueStatus(); else SetPhase(BattlePhase.None);
                });
            }
        }

        private void ClearJoinDiagnostics()
        {
            _inFlightJoinRequest = null;
            _lateJoinDiagnostic = null;
        }

        private void PreserveJoinDiagnosticForBattle()
        {
            var pending = _inFlightJoinRequest;
            if (pending == null || pending.Generation != _queueGeneration || pending.QueueSession != _queueSession
                || pending.Player != MyPlayerId || !_net.IsReady) return;
            _lateJoinDiagnostic = pending;
            _inFlightJoinRequest = null;
        }

        private bool CompleteJoinRequest(JoinRequestIdentity identity, out bool diagnosticAllowed)
        {
            bool pending = ReferenceEquals(_inFlightJoinRequest, identity);
            bool diagnostic = ReferenceEquals(_lateJoinDiagnostic, identity);
            if (pending) _inFlightJoinRequest = null;
            if (diagnostic) _lateJoinDiagnostic = null;
            bool sameSession = identity.QueueSession == _queueSession && identity.Player == MyPlayerId && _net.IsReady;
            diagnosticAllowed = diagnostic && sameSession;
            return pending && sameSession && identity.Generation == _queueGeneration;
        }

        /// <summary>
        /// 队伍补位匹配:服务端已经把我随队伍放进人数档队列(队长发的 StartTeamMatch),本机没有发过 JoinQueue。
        /// 进入 Queued,让既有的排队面板、3 秒一次的 GetQueueStatus 轮询、取消、凑满后的开战流程原样生效。
        /// 不发任何请求,不写连续战斗的重排记忆;票据为空(CancelQueue 允许空票据,服务端按本人身份撤掉整队的排队)。
        /// 只在相位 None 时生效;返回是否进入了排队态。
        /// </summary>
        public bool AdoptQueue(string hint)
        {
            RestoreQueueIntent();
            if (Phase != BattlePhase.None) return false;
            ClearJoinDiagnostics();
            ++_queueGeneration;
            _globalQueue = false;
            _joinSent = true; // 已由服务端随队入队，不能走“本机尚未发送”的取消分支。
            _cancelRequested = false;
            _queueTicket = string.Empty;
            _endAckPending = false; // 与 JoinQueue 同口径:新的排队作废未消费的结束 Ack,不让连续战斗紧跟着再单排
            _queueSession++;
            QueueAdopted = true;
            AdoptedQueueHint = hint ?? string.Empty;
            SetPhase(BattlePhase.Queued);
            _nextQueuePollAt = _now + QueuePollIntervalSeconds;
            return true;
        }

        /// <summary>
        /// 排队中时把下一次 GetQueueStatus 轮询提前到现在(下一次 Tick 发出)。外部得到「可能已经不在排」的
        /// 提示(例如队伍视图说没在排)时用它向服务端求证,而不是自己改相位。不在排队态时是空操作。
        /// </summary>
        public void PollQueueNow()
        {
            if (Phase == BattlePhase.Queued) _nextQueuePollAt = _now;
        }

        public void CancelQueue()
        {
            if (_recoveryBlocked) { OnError?.Invoke("匹配恢复记录损坏或无法读取，保留保护状态；请修复本机存储并重新登录。"); return; }
            if (Phase != BattlePhase.Queued)
            {
                OnError?.Invoke("当前不在排队中");
                return;
            }

            ClearJoinDiagnostics();
            if (RegionProbe != null && !_joinSent)
            {
                ++_queueGeneration; RegionProbe.Dispose(); SetPhase(BattlePhase.None); return;
            }
            if (_globalQueue)
            {
                try { if (_joinIntent != null) QueueRecovery?.Save(_joinIntent, true); }
                catch (Exception) { OnError?.Invoke("无法保存取消恢复记录，请检查本机存储后重试。"); return; }
                ++_queueGeneration; _cancelRequested = true;
                SendGlobalCancellation();
                return;
            }

            var req = new Match.CancelQueueRequest
            {
                PlayerId = MyPlayerId,
                QueueTicket = _queueTicket,
            };
            int session = _queueSession;
            _net.Call(MessageIds.CancelQueue, req, Empty.Parser,
                _ =>
                {
                    // 上一次排队的取消回包迟到了:与这一次排队无关。
                    if (session != _queueSession) return;
                    // 收敛:仍在排队才回 None(若期间已开战则维持战斗相位)
                    if (Phase != BattlePhase.Queued) return;
                    if (QueueAdopted)
                    {
                        // 队伍补位:回包是 Empty,分不出「整队已退出」还是「太迟」(队伍已凑满弹出,取消被忽略)。
                        // 太迟之后若开局失败,整队会回到队列 —— 此时直接回空闲,本机就再也看不到自己还在排。
                        // 所以不改相位,立刻问一次服务端:已不在排才回空闲;仍在排 / 已凑满则照实处理。
                        _cancelVerifyPending = true;
                        _nextQueuePollAt = _now;
                        return;
                    }
                    SetPhase(BattlePhase.None);
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
            // 本机新匹配还在做地域探测,尚未提交入队。此时服务端的 NotQueued
            // 不能撤销这次意图;恢复记录与随队入队仍须照常向服务端求证。
            if (Phase == BattlePhase.Queued && RegionProbe?.Busy == true && !_joinSent &&
                !QueueAdopted && _joinIntent == null && !_recoveryBlocked) return;
            // 单在途防止重复轮询；请求代次、玩家和排队会话继续隔离旧结果。
            if (_queuePollInFlightSeq != 0) { _queuePollAgain = true; return; }
            int seq = BeginQueuePoll();
            int session = _queueSession;
            var generation = _queueGeneration;
            var query = ++_queuePollSequence;
            var player = MyPlayerId;
            bool cancelVerify = _cancelVerifyPending;
            _cancelVerifyPending = false;
            var req = new Match.GetQueueStatusRequest { PlayerId = player, RequestId = _joinIntent?.RequestId ?? string.Empty };
            _net.Call(MessageIds.GetQueueStatus, req, Match.GetQueueStatusResponse.Parser,
                resp =>
                {
                    if (seq != _queuePollInFlightSeq) return;
                    EndQueuePoll(seq);
                    if (session != _queueSession || generation != _queueGeneration || query != _queuePollSequence || player != MyPlayerId)
                    {
                        // 上一次排队的迟到回包:整包丢弃。这一次排队按自己的节奏轮询,不立即重问 —— 自己单排「取消后马上再排」时,
                        // 立即重问的轮询可能比还在途的 JoinQueue 先被服务端处理,回「不在排」把新的排队打回空闲。
                        // 若这一次的轮询曾被单飞压住,EndQueuePoll 已经安排了回来后补问。
                        return;
                    }
                    OnQueueStatus?.Invoke(resp);
                    if (RegionProbe != null && resp.QueueTicket.Length > 0)
                    {
                        _queueTicket = resp.QueueTicket; _globalQueue = true;
                        _joinSent = true;
                        if (resp.State == Match.QueueState.Queued && Phase == BattlePhase.None) SetPhase(BattlePhase.Queued);
                        if (resp.BattleId != 0 && (resp.State == Match.QueueState.Ready || resp.State == Match.QueueState.Matched))
                        {
                            _battleId = resp.BattleId; EnterPreparing();
                            if (resp.State == Match.QueueState.Ready) _channel?.EnsureBattle(resp.BattleId);
                        }
                    }
                    if (_globalQueue && Phase == BattlePhase.Preparing && resp.State == Match.QueueState.NotQueued)
                    { _channel?.Abandon(_battleId); ClearBattleContext(); _endedByTicket = true; SetPhase(BattlePhase.None); return; }
                    if (Phase != BattlePhase.Queued) return; // 已开战:轮询结果只透传不改相位
                    if (cancelVerify && resp.State != Match.QueueState.NotQueued)
                        OnError?.Invoke("取消未生效:队伍已凑满正在开局,或仍在匹配中");
                    switch (resp.State)
                    {
                        case Match.QueueState.Matched:
                        case Match.QueueState.Ready:
                        case Match.QueueState.Entering:
                            // 凑单成功:停止轮询(离开 Queued 即停),等 NotifyBattleStart
                            EnterPreparing();
                            break;
                        case Match.QueueState.NotQueued:
                            if (_recoveryBlocked) return;
                            if (_globalQueue && _joinIntent != null && (_joinOutcomeUnknown || _cancelRequested) && !resp.RequestCancelled)
                            {
                                // 迟到 Join 仍可能提交。只有同一请求的撤销墓碑才证明它不会再入队。
                                if (_cancelRequested) SendGlobalCancellation();
                                return;
                            }
                            ++_queueGeneration; ClearQueueIntent();
                            _endedByTicket = true;
                            SetPhase(BattlePhase.None);
                            break;
                    }
                },
                err =>
                {
                    if (seq != _queuePollInFlightSeq) return;
                    EndQueuePoll(seq);
                    if (generation == _queueGeneration && query == _queuePollSequence && player == MyPlayerId && session == _queueSession)
                        OnError?.Invoke($"查询排队状态失败:{err}");
                });
        }

        private void SendGlobalCancellation()
        {
            int generation = _queueGeneration; ulong player = MyPlayerId;
            if (QueueAdopted) _cancelVerifyPending = true;
            if (_joinIntent == null && _queueTicket.Length == 0) { PollQueueStatus(); return; }
            _net.Call(MessageIds.CancelQueue, new Match.CancelQueueRequest { PlayerId = player, QueueTicket = _queueTicket, RequestId = _joinIntent?.RequestId ?? string.Empty }, Empty.Parser,
                _ => { if (generation == _queueGeneration && player == MyPlayerId) PollQueueStatus(); },
                _ => { if (generation == _queueGeneration && player == MyPlayerId) { OnError?.Invoke("取消结果尚未确认，正在查询。"); PollQueueStatus(); } });
            // 取消的回包只说明受理(补位 / 凑满竞态下分不出是否真的出队),而且 Java 版 gate 对 Empty 应答成功仍不回包
            // (C++ 版回空包,见服务端仓 docs/design/client-rpc-callback.md §7 R6),所以确认必须独立查询,不能等取消调用的结局。
            _nextQueuePollAt = _now + QueuePollIntervalSeconds;
            PollQueueStatus();
        }
        private void RestoreQueueIntent()
        {
            if (QueueRecovery == null || MyPlayerId == 0 || _queueIntentPlayer == MyPlayerId) return;
            _queueIntentPlayer = MyPlayerId;
            _joinIntent = null; _cancelRequested = false; _joinOutcomeUnknown = false; _recoveryBlocked = false;
            try
            {
                _joinIntent = QueueRecovery.Read(MyPlayerId, out _cancelRequested);
                if (_joinIntent != null) { _globalQueue = true; _joinSent = true; _joinOutcomeUnknown = true; if (Phase == BattlePhase.None) SetPhase(BattlePhase.Queued); }
            }
            catch (Exception) { _recoveryBlocked = true; _globalQueue = true; _joinSent = true; OnError?.Invoke("匹配恢复记录无法读取，请检查本机存储。"); SetPhase(BattlePhase.Queued); }
        }
        private void TryClearRecovery()
        {
            try { if (_queueIntentPlayer != 0) QueueRecovery?.Clear(_queueIntentPlayer); }
            catch (Exception) { OnError?.Invoke("匹配结果已确认，本机恢复记录尚未清理。"); }
        }
        private void ClearQueueIntent()
        {
            TryClearRecovery(); _joinIntent = null; _joinOutcomeUnknown = false; _cancelRequested = false;
        }

        /// <summary>
        /// 空闲时问一次服务端「我是不是还挂着排队票据」,结果交给 onResult,不改相位、不发事件。
        /// 给队伍补位匹配用:被带入的排队因开战而结束后,用它核实那一次排队是否真的结束了(TeamBackfillQueueSync)。
        /// 只在相位 None、连接就绪且没有排队查询在途时发出,返回是否发出;回包到达时若已开始新的排队 / 战斗,结果作废。
        /// 失败不回调(调用方按「没在排」处理)。
        /// </summary>
        public bool ProbeQueueStatus(Action<Match.QueueState> onResult)
        {
            if (Phase != BattlePhase.None || !_net.IsReady || _queuePollInFlightSeq != 0) return false;
            int seq = BeginQueuePoll();
            int session = _queueSession;
            int generation = _queueGeneration;
            ulong player = MyPlayerId;
            var req = new Match.GetQueueStatusRequest { PlayerId = player, RequestId = _joinIntent?.RequestId ?? string.Empty };
            _net.Call(MessageIds.GetQueueStatus, req, Match.GetQueueStatusResponse.Parser,
                resp =>
                {
                    if (seq != _queuePollInFlightSeq) return;
                    EndQueuePoll(seq);
                    if (session == _queueSession && generation == _queueGeneration && player == MyPlayerId && Phase == BattlePhase.None)
                        onResult?.Invoke(resp.State);
                },
                _ => EndQueuePoll(seq));
            return true;
        }

        private int BeginQueuePoll()
        {
            if (++_queuePollSeq == 0) ++_queuePollSeq;
            _queuePollInFlightSeq = _queuePollSeq;
            return _queuePollSeq;
        }

        // 一次排队状态查询回来了(成功或失败)。它若还是「在途的那一次」(断线会作废在途记录),解除在途;
        // 期间有人要求再问,就把下一次轮询提前到现在(还在排队态时下一次 Tick 发出)。
        private void EndQueuePoll(int seq)
        {
            if (seq != _queuePollInFlightSeq) return;
            _queuePollInFlightSeq = 0;
            if (!_queuePollAgain) return;
            _queuePollAgain = false;
            _nextQueuePollAt = _now;
        }

        // ── 契约方法:切磋(场景发起 PK) ───────────────────

        public void ChallengePlayer(ulong targetPlayerId)
        {
            if (RegionProbe != null) { RegionProbe.Prepare(plan => { _globalQueue = plan.Length > 0; SendChallengePlayer(targetPlayerId); }, error => OnError?.Invoke(error)); return; }
            SendChallengePlayer(targetPlayerId);
        }

        private void SendChallengePlayer(ulong targetPlayerId)
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
            if (accept && RegionProbe != null) { RegionProbe.Prepare(plan => { _globalQueue = plan.Length > 0; SendRespondChallenge(challengeId, true); }, error => OnError?.Invoke(error)); return; }
            SendRespondChallenge(challengeId, accept);
        }

        private void SendRespondChallenge(ulong challengeId, bool accept)
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

        /// <summary>
        /// 提交本回合行动。返回 true = 请求已发出(结果仍以 NotifyTurnResult / OnError 为准);
        /// false = 本地拒绝、未发任何网络请求(相位不对 / 指令为空 / 直连未就绪),原因已经 OnError 抛出。
        /// UI 只在返回 true 时把本回合标为「已提交」。失败不重发:请求没有回合号(D40 未做),
        /// 重发可能落进下一回合,宁可让引擎的回合超时兜底默认出手。
        /// </summary>
        public bool SubmitAction(BattleAction action)
        {
            if (Phase != BattlePhase.WaitingAction)
            {
                OnError?.Invoke("当前不能提交行动");
                return false;
            }
            if (action == null)
            {
                OnError?.Invoke("行动指令为空");
                return false;
            }
            if (!IsBattleChannelReady)
            {
                // 已判定连不上(Unreachable)时链路不会自己恢复,「请稍候」会与横幅矛盾:给失败文案
                OnError?.Invoke(_channelFailed ? BattleChannelFailedText : BattleChannelConnectingText);
                return false;
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
                        OnError?.Invoke(DescribeActionTip("提交行动失败", resp.ErrorMessage));
                    // 成功不改相位:等 NotifyTurnResult 驱动 Resolving
                },
                err => OnError?.Invoke($"提交行动失败:{err}"));
            return true;
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
        /// 直连未就绪时同样只记忆并挂起,直连就绪后按记忆补发;不报错
        /// (UI 按 <see cref="IsBattleChannelReady"/> 自行提示「战斗连接建立后生效」)。
        /// </summary>
        public void SetAutoBattle(bool enabled)
        {
            AutoBattleLatched = enabled;
            if (_battleId == 0) return; // 不在战斗:纯记忆,不报错(排队面板预设场景)
            if (Phase != BattlePhase.WaitingAction && Phase != BattlePhase.Resolving) return;
            if (!IsBattleChannelReady)
            {
                _autoResendPending = true;
                return;
            }
            SendSetAutoBattle(enabled, revertLatchOnFailure: true);
        }

        /// <summary>
        /// UI「重新连接」(turn-based §22 D74):重置直连恢复预算并立即补签重建直连。
        /// 不在战斗中 / 无直连通道 / 该局直连已在连接或已就绪时无事。
        /// 结果经 <see cref="OnBattleChannelReady"/> 或再次的 <see cref="OnBattleChannelFailed"/> 回来。
        /// </summary>
        public void RetryBattleChannel()
        {
            if (_channel == null || _battleId == 0) return;
            _channelFailed = false;
            _channel.Retry(_battleId);
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
            JoinQueue(_lastQueueMode, _lastQueueConfigId, _lastQueueTeamSize);
        }

        /// <summary>
        /// 补拉权威状态(重连/异常兜底):GetBattleState,按返回态置
        /// WaitingAction / Resolving(战斗已结束则收敛回 None)。
        /// 直连未就绪时不发请求也不报错:直连就绪时会自动补拉(turn-based §22 D74);
        /// 同局补拉在途时去重(battle 直连按消息号限速,连续重连 + UI 兜底不能叠发)。
        /// 显式补拉(大厅重连提示 / UI「返回战斗」/ 开屏兜底)是新的一轮:作废待执行的自动重拉并重置其预算,
        /// 同 BattleDirectLink 的宿主入口重置补签预算。
        /// </summary>
        public void RequestState()
        {
            if (_battleId == 0)
            {
                OnError?.Invoke("没有进行中的战斗,无法补拉状态");
                return;
            }
            ResetStatePullRetry();
            if (!IsBattleChannelReady) return;
            PullState();
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
            PreserveJoinDiagnosticForBattle();
            ++_queueGeneration; ClearQueueIntent();
            _queueTicket = string.Empty;
            _battleId = ev.BattleId != 0 ? ev.BattleId : ev.State?.BattleId ?? 0;
            State = ev.State;
            _autoResendPending = false;   // 新的一局:上一局挂起的直连相关标记作废
            _channelFailed = false;
            _endPendingBattleId = 0;
            ResetStatePullRetry();        // 换局:上一局的自动重拉计划作废

            // 排队中收到开战:补发 Preparing 让 UI 收起排队面板(Queued→Preparing→WaitingAction)
            if (Phase == BattlePhase.Queued) SetPhase(BattlePhase.Preparing);

            // 事件先于终相位:UI 先拿 BattleStartS2C 开屏,再收 WaitingAction 刷新输入区
            OnBattleStart?.Invoke(ev);

            var phase = DecidePhaseFromState(ev.State, MyPlayerId);
            if (phase == BattlePhase.None) phase = BattlePhase.WaitingAction; // 开局态不可能已结束,容错
            SetPhase(phase);
            RefreshMyAutoState();

            // 开局自愈:分配包与开局包同 key 有序,开局包到了直连却不服务该局 = 分配包丢了,补签;
            // 同局已在连 / 已验证时无事(turn-based §22 D74)
            _channel?.EnsureBattle(_battleId);

            // 自动战斗记忆补发(§11.2):新战斗引擎侧 is_auto 归零,由本地记忆续上。
            // 开局瞬间直连通常还没握手完:挂起,直连就绪时补发(不能发大厅,gate 不中继战斗)
            if (AutoBattleLatched)
            {
                if (IsBattleChannelReady) SendSetAutoBattle(true, revertLatchOnFailure: false);
                else _autoResendPending = true;
            }
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
            // 只接受当前战斗(含重连恢复中)的结束消息;无上下文的迟到消息丢弃。
            // 例外:该局已按权威信号收场(直连 BattleGone:终局包在断线期间丢失;或补拉到已出胜负),
            // scene 结算后经大厅推的 NotifyBattleEnd 晚到 —— 仍交给 UI 出结算面板;
            // 只在空闲时接受,不打断新的排队 / 战斗。
            bool lateEndOfSettledBattle = _battleId == 0 && Phase == BattlePhase.None
                                          && ev.BattleId != 0 && ev.BattleId == _endPendingBattleId;
            if (_battleId == 0 && !lateEndOfSettledBattle) return;
            if (!lateEndOfSettledBattle && ev.BattleId != 0 && ev.BattleId != _battleId) return;
            _endPendingBattleId = 0;

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
            PreserveJoinDiagnosticForBattle();
            ++_queueGeneration; ClearQueueIntent();
            // 契约:NotifyBattleReconnect 到达 → 自动 RequestState(),
            // 相位由返回的权威状态决定(WaitingAction/Resolving)。
            // 直连此刻通常已随大厅断线关闭:RequestState 推迟,分流层随后按本提示补签重建直连,
            // 就绪事件里补拉(turn-based §22 D74)。
            if (ev.BattleId != _battleId) _autoResendPending = false;
            _endPendingBattleId = 0;
            _battleId = ev.BattleId;
            _queueTicket = string.Empty;
            _preparingDeadline = 0;
            _channelFailed = false;   // 链路按提示重新补签,失败会再次通知
            RequestState();           // 内含作废旧的自动重拉计划(换局 / 同局新一轮都是新的开始)
        }

        private void HandleChallengeResult(Match.ChallengeResultS2C ev)
        {
            if (ev == null) return;
            // 发起者:对方应战成功 → 进入 gather 等 NotifyBattleStart
            //(应战者一侧已在 RespondChallenge 响应回调里进入 Preparing,此处 Phase 已非 None)
            if (ev.Accepted && Phase == BattlePhase.None) EnterPreparing();
            OnChallengeResult?.Invoke(ev);
        }

        // ── 战斗直连通道(turn-based §22 D74) ─────────────

        /// <summary>
        /// 直连就绪:D68 下开局到握手之间、断线期间的战斗帧不会经大厅回落,就绪即补拉一次
        /// 权威状态(新的一轮:作废待执行的自动重拉并重置预算);挂起的自动战斗意愿
        /// (或记忆开着而权威态未挂机)随后补发。只处理本局。
        /// </summary>
        private void HandleChannelReady(ulong battleId, eBattleTicketRole role)
        {
            if (battleId == 0 || battleId != _battleId) return;
            _channelFailed = false;
            ResetStatePullRetry();
            PullState();
            if (_autoResendPending || (AutoBattleLatched && !IsMyActorAuto))
            {
                _autoResendPending = false;
                // 补发不回滚记忆:瞬时失败不得抹掉跨场挂机意愿(同 BattleStart 补发口径)
                SendSetAutoBattle(AutoBattleLatched, revertLatchOnFailure: false);
            }
            OnBattleChannelReady?.Invoke();
        }

        /// <summary>
        /// 直连终结:BattleGone(补签被判战斗已结束,终局包多半在断线期间丢了)收敛回 None;
        /// Unreachable 保持相位(服务端回合超时替本人默认出手),抛 OnBattleChannelFailed 供 UI 常驻提示
        /// 与手动重连;Ended(正常收尾)/ HostClosed(大厅断线,由 Disconnected 统一作废)不处理;
        /// Superseded(通道被另一局的分配包拿走)不是本局结束的权威信号:本局未收终局 / BattleGone 时参战优先,
        /// 立即 EnsureBattle 要回链路 —— 否则参战中晚到的观战分配包(WatchBattle 的 double-check 自我清退竞态:
        /// AddObserver 先于清退推了 Assigned(OBSERVER))会劫走唯一的直连,本局既不重建也不进 Unreachable,
        /// 战斗屏卡在「正在连接战斗服务器…」,服务端整局替本人默认出手。链路此时指向另一局,EnsureBattle 走补签
        /// 并对那一局抛 Superseded(观战方据此收敛;服务端已清退该观众,不会乒乓)。真换局(新参战局的分配包先于
        /// 本局终局包到达)时最多多一次补签,随后新局开局包的 EnsureBattle 把链路切回新局,自愈。
        /// </summary>
        private void HandleChannelLost(ulong battleId, BattleLinkCloseKind kind, string detail)
        {
            if (battleId == 0 || battleId != _battleId) return;
            switch (kind)
            {
                case BattleLinkCloseKind.BattleGone:
                    OnError?.Invoke("战斗已结束");
                    ClearBattleContext();
                    _endPendingBattleId = battleId; // 允许 scene 结算包晚到(见 HandleBattleEnd)
                    SetPhase(BattlePhase.None);
                    RefreshMyAutoState();
                    break;
                case BattleLinkCloseKind.Unreachable:
                    _channelFailed = true;
                    OnBattleChannelFailed?.Invoke(BattleChannelFailedText);
                    break;
                case BattleLinkCloseKind.Superseded:
                    // 本局仍在进行(入口守卫:终局包 / BattleGone / 断线收尾都会把 _battleId 清零),
                    // 链路被另一局的分配包拿走:参战优先,立即要回来。相位、错误与失败事件都不动。
                    _channel?.EnsureBattle(_battleId);
                    break;
            }
        }

        private void HandleDisconnected()
        {
            ClearJoinDiagnostics();
            ++_queueGeneration;
            RegionProbe?.Dispose();
            _lastTransportReady = false;
            // 断线:本地战斗/排队态整体作废(补偿矩阵:排队票据失效;战斗照打,
            // 重连后 scene 发现 InBattleComp 会推 NotifyBattleReconnect → RequestState 恢复)。
            // 未消费的结束 Ack 一并作废:断线重连后不允许凭旧 Ack 自动入队。
            ClearBattleContext();
            State = null;
            _endAckPending = false;
            _endPendingBattleId = 0;
            // 在途的排队查询随连接作废(GameClient 会以 disconnected 失败回调):解除在途,旧会话的回包一律丢弃。
            _queuePollInFlightSeq = 0;
            _queuePollAgain = false;
            _queueSession++;
            SetPhase(BattlePhase.None);
            RefreshMyAutoState();
        }

        private void ApplyAuthoritativeState(BattleStateS2C state)
        {
            if (state == null) return;
            if (state.BattleId != 0) _battleId = state.BattleId;
            State = state;

            var phase = DecidePhaseFromState(state, MyPlayerId);
            if (phase == BattlePhase.None)
            {
                // 战斗已结束:结算由 scene 侧另行通知 —— 留着 battle_id,好让那份终局包晚到时仍被接受
                ulong settledBattleId = _battleId;
                ClearBattleContext();
                _endPendingBattleId = settledBattleId;
            }
            SetPhase(phase);
            RefreshMyAutoState();
        }

        /// <summary>
        /// 发 GetBattleState(同局在途去重)。结果只作用于发出时的那一局:期间已收尾 / 换局 / 断线,
        /// 迟到的结果作废。直连传输层失败(未就绪 / 断开 / 超时)不打扰玩家:此刻同一局直连已不就绪的,
        /// 等下一次就绪自动补拉;仍就绪的(链路 Verified 时的 rpc 超时等,不会再有就绪事件)按有上限退避自动重拉,
        /// 见 <see cref="ScheduleStatePullRetry"/>。服务端拒绝 / 无直连通道时的失败照常经 OnError 抛出,不重拉。
        /// </summary>
        private void PullState()
        {
            ulong battleId = _battleId;
            if (battleId == 0 || _statePullBattleId == battleId) return;
            _statePullBattleId = battleId;
            var req = new GetBattleStateRequest { BattleId = battleId };
            _net.Call(MessageIds.GetBattleState, req, BattleStateS2C.Parser,
                state =>
                {
                    if (_statePullBattleId == battleId) _statePullBattleId = 0;
                    if (_battleId != battleId) return;
                    ResetStatePullRetry(); // 拉到了:本轮结束,之后的失败另起一轮预算
                    ApplyAuthoritativeState(state);
                },
                err =>
                {
                    if (_statePullBattleId == battleId) _statePullBattleId = 0;
                    if (_battleId != battleId) return;
                    if (_channel != null && IsTransportError(err))
                    {
                        if (IsBattleChannelReady) ScheduleStatePullRetry();
                        return;
                    }
                    OnError?.Invoke($"拉取战斗状态失败:{err}");
                });
        }

        /// <summary>
        /// 排下一次自动重拉:本轮预算内第 k 次(从 1 起)等 基数 × 2^(k-1) × (1 ± 抖动),即 1s / 2s / 4s ±20%。
        /// 已有计划不重复排;预算用完不再排,也不打扰玩家(传输错误本就静默)——主城入口此时是可点的
        /// 「返回战斗」(BattleUiRoot.DecideEntryMode),下一次就绪也会再补拉。
        /// 计划时刻以 <see cref="Tick"/> 注入的时钟为准;回调里拿到的是上一次 Tick 的时刻,最多差一帧,无碍。
        /// </summary>
        private void ScheduleStatePullRetry()
        {
            if (_statePullRetryAt > 0 || _statePullRetries >= MaxStatePullRetries) return;
            _statePullRetries++;
            double delay = StatePullRetryBaseSeconds * Math.Pow(2, _statePullRetries - 1)
                           * (1.0 + (_random01() * 2.0 - 1.0) * StatePullRetryJitter);
            _statePullRetryAt = _now + delay; // delay ≥ 0.8s,故 >0 可作「有计划」的哨兵
        }

        /// <summary>作废待执行的自动重拉并归零本轮预算(换局 / 收尾 / 断线 / 就绪 / 显式补拉 / 补拉成功)。</summary>
        private void ResetStatePullRetry()
        {
            _statePullRetryAt = 0;
            _statePullRetries = 0;
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
            ulong battleId = _battleId;
            var req = new SetAutoBattleRequest
            {
                BattleId = battleId,
                Enabled = enabled,
            };
            _net.Call(MessageIds.SetAutoBattle, req, SetAutoBattleResponse.Parser,
                resp =>
                {
                    if (HasTip(resp.ErrorMessage))
                    {
                        OnError?.Invoke(DescribeBattleTip("设置自动战斗失败", resp.ErrorMessage));
                        RevertLatchIfNeeded(revertLatchOnFailure);
                    }
                    // 成功不改本地态:权威 is_auto 随下一帧 BattleStateS2C 回来
                },
                err =>
                {
                    // 直连传输层失败(未就绪 / 断开 / 超时):意愿挂起,直连就绪后按记忆补发,
                    // 不回滚记忆、不打扰玩家(SetAutoBattle 置位到同一个值,重发无害)
                    if (_channel != null && IsTransportError(err))
                    {
                        if (_battleId == battleId && battleId != 0) _autoResendPending = true;
                        return;
                    }
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
            _autoResendPending = false;
            _channelFailed = false;
            _statePullBattleId = 0;
            ResetStatePullRetry();      // 收尾 / 断线:自动重拉计划随本局作废
        }

        private void SetPhase(BattlePhase phase)
        {
            if (Phase == phase) return;
            if (phase != BattlePhase.Preparing) _preparingDeadline = 0;
            if (phase != BattlePhase.Queued && phase != BattlePhase.Preparing)
            {
                if (QueueAdopted) AdoptedQueueEndedByTicket = _endedByTicket;
                QueueAdopted = false;
                AdoptedQueueHint = string.Empty;
            }
            _endedByTicket = false;
            if (phase != BattlePhase.Queued) _cancelVerifyPending = false;
            Phase = phase;
            OnPhaseChanged?.Invoke(phase);
        }

        private static bool HasTip(TipInfoMessage tip) => tip != null && tip.Id != 0;

        private static bool IsTransportError(string err)
            => err != null && err.StartsWith(BattleDirectLink.TransportErrorPrefix, StringComparison.Ordinal);

        /// <summary>
        /// tip 转玩家文案。客户端尚无 tip 表加载器,已知码按生成枚举手写映射(与 JubaozhaiClient 同口径),
        /// 未知码保留裸编号便于排障。
        /// </summary>
        private static string DescribeTip(string what, TipInfoMessage tip, uint errorCode = 0)
        {
            if (tip == null || tip.Id == 0) return $"{what}(code={errorCode})";
            return tip.Id == (uint)common_error.KServiceUnavailable
                ? $"{what}:战斗服务暂不可用,请稍后再试(tip={tip.Id})"
                : $"{what}(tip={tip.Id})";
        }

        /// <summary>
        /// SetAutoBattle 专用:battle 节点对这条回 kInvalidParameter 只来自房间不存在 / 不在参战名单 /
        /// 引擎已出胜负或找不到本人,即「战斗不存在或已结束」。
        /// 出手(SubmitBattleAction)的 kInvalidParameter 还含行动校验失败,见 <see cref="DescribeActionTip"/>;
        /// 排队 / 切磋的 kInvalidParameter 是真的参数错。两者都不能套这条文案。
        /// </summary>
        private static string DescribeBattleTip(string what, TipInfoMessage tip)
            => tip != null && tip.Id == (uint)common_error.KInvalidParameter
                ? $"{what}:战斗不存在或已结束(tip={tip.Id})"
                : DescribeTip(what, tip);

        /// <summary>
        /// SubmitBattleAction 专用:battle 节点把引擎 ValidateAction 的结果原样回给提交者,
        /// kInvalidParameter 除了「房间不存在 / 不在名单 / 已出胜负」,还来自战斗仍在进行时的普通非法行动
        /// (PVP 逃跑、未拥有的技能、非战斗道具或零效果道具、PVP 用药超限、未知行动类型)。
        /// 客户端分不出是哪种,只能说「行动无效」,不能说「战斗已结束」误导玩家;
        /// 真结束了自有终局包 / 直连 BattleGone 收场。
        /// </summary>
        private static string DescribeActionTip(string what, TipInfoMessage tip)
            => tip != null && tip.Id == (uint)common_error.KInvalidParameter
                ? $"{what}:行动无效(tip={tip.Id})"
                : DescribeTip(what, tip);
    }
}
