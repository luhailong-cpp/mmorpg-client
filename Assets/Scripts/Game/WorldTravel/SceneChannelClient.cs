using System;
using System.Collections.Generic;
using System.Globalization;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;

namespace MmorpgClient.Game.WorldTravel
{
    /// <summary>
    /// 分线(切线)的网络真相(服务端设计:docs/design/world-channel-switch.md §3 / §7)。
    ///
    /// 契约:
    ///  - 列线:发 SceneInfoC2S(43)。它的应答类型是 Empty、数据走推送 NotifySceneInfo(31),所以只能单向发送、
    ///    自己计等待期限,不能用 Call 等应答(会干等到 RPC 超时)。43 被 gate 限流拒绝时,带错误码的回包
    ///    因为没有在途项会落到 43 的推送处理器上(与 GameClient.RegisterMovementReply 同一条路);
    ///  - 切线:发 EnterScene(63){ scene_config_id = 当前地图, scene_id = 目标线 }。同步应答无错只表示**已受理**;
    ///    抵达只认随后的入场通知且 scene_id == 目标;受理之后才发生的失败只会以服务端提示(msg 23)到达;
    ///    两次 63 之间至少隔 <see cref="MinSwitchIntervalSeconds"/>(连点保护,理由见该常量);
    ///  - 只发意图、等权威回包,不做本地假成功:线路列表只来自服务端目录,所在线只由入场通知与目录推出;
    ///  - 入场通知 / 服务端提示由宿主转发进来(<see cref="HandleSceneEntered"/> / <see cref="HandleServerTip"/>),
    ///    本类不引用 GameClient 与 UnityEngine,EditMode 测试直接 new;
    ///  - 旧连接隔离:断线或连接身份变化时整体复位并换世代,旧连接上 63 的迟到回调按世代丢弃。
    ///
    /// 用法:宿主每帧调 <see cref="Tick"/>,界面订阅 <see cref="Changed"/> 后整份读取本对象的只读状态;
    /// 倒计时显示由界面每帧读 <see cref="CooldownRemainingSeconds"/>,本类不会每帧触发 Changed。
    /// **不要再对 31 / 43 调 RegisterNotify**:那是覆盖语义,会把这里的处理器静默顶掉。
    /// </summary>
    public sealed class SceneChannelClient : IDisposable
    {
        /// <summary>
        /// 两次 43 之间的最小间隔。gate 的 MessageLimiter 按 message_id 计数、时间戳取整秒;43 不在限流表里,
        /// 走默认配额(服务端设计 §7 的口径:跨两个整秒内 3 次),超了回 1008 并给本会话记一次非法包
        /// (累计 50 次断开连接)。2.5 秒一发与配额隔着一倍以上的余量(窗口推导同 TeamClient.MessageWindowSeconds)。
        /// </summary>
        public const float MinListIntervalSeconds = 2.5f;
        /// <summary>43 发出后等 31 的上限;到点结束等待并提示,不自动重发。</summary>
        public const float ListTimeoutSeconds = 5f;
        /// <summary>线路面板打开期间的建议刷新周期(由界面宿主按它调 <see cref="RequestList"/>)。</summary>
        public const float PanelRefreshIntervalSeconds = 5f;
        /// <summary>
        /// 两次 63 之间的最小间隔。63 同样不在限流表里(默认配额同 43),而同步拒绝一个往返就回来并解除在途:
        /// 没有这道闸,玩家连点第 4 下就撞限流、给会话记一次非法包。1 秒一发时 gate 的窗口(两个整秒)里
        /// 通常只落 2 条,上行抖动把相邻两条挤进同一秒时是 3 条,都不超配额;通常剩下的那 1 条留给
        /// 地图窗的同区换图(它与切线共用 63 这个消息号,而两个窗口是互斥的模态窗,几乎不会同时在发)。
        /// </summary>
        public const float MinSwitchIntervalSeconds = 1f;
        /// <summary>
        /// 63 被 gate 限流(1008)之后至少再等这么久才发。gate 的窗口跨两个整秒,被拒的那一包不计数;
        /// 等满 2.5 秒,窗口里已经没有旧记录(推导同 <see cref="MinListIntervalSeconds"/>,43 的退避用的也是这个值)。
        /// </summary>
        public const float RateLimitBackoffSeconds = MinListIntervalSeconds;
        /// <summary>
        /// 63 发出后、同步应答到达前的本地兜底期限(与 <see cref="CityTravelRequest.TimeoutSeconds"/> 同值)。
        /// 正常情况下传输层 15 秒内必有应答或报错,轮不到它;它只防传输层永不回调时切线状态卡死。
        /// </summary>
        public const float SwitchRequestBudgetSeconds = (float)CityTravelRequest.TimeoutSeconds;
        /// <summary>
        /// 受理之后等入场通知的上限:目标线可能在别的节点,要走「冻结、存盘、重发进场」的归属交接,
        /// 与同区换图是同一条服务端链路,所以沿用同一个预算
        /// (推导与两端同步约束见 <see cref="CityTravelRequest.AcceptedHandoffBudgetSeconds"/>,不要改成字面量)。
        /// </summary>
        public const float SwitchAcceptedBudgetSeconds = (float)CityTravelRequest.AcceptedHandoffBudgetSeconds;
        /// <summary>
        /// 一次切线「结果未知」地收了口(1003 / 应答超时 / 等待超时)之后,还认多久它的入场通知:
        /// 这段时间里目标线的入场通知到达,就按切线成功补结算(见 <see cref="HandleSceneEntered"/>)。
        /// 取服务端的交接预算:过了这么久还没到,就不会是那次切线了。
        /// </summary>
        public const float LateArrivalWindowSeconds = SwitchAcceptedBudgetSeconds;

        public const string ListUnavailableMessage  = "线路信息暂时获取不到。";
        public const string TooFastMessage          = "操作太快了，请稍候再试。";
        public const string ServerBusyMessage       = "服务器繁忙，请稍后再试。";
        public const string AlreadyOnLineMessage    = "已在该线路。";
        public const string ChangingSceneMessage    = "正在切换场景，请稍候。";
        public const string SwitchRejectedMessage   = "当前无法切线（战斗中或服务器繁忙）。";
        public const string SwitchFailedMessage     = "切线失败，请稍后再试。";
        public const string SwitchTimeoutMessage    = "切线超时，请稍后再试。";
        public const string WrongDestinationMessage = "未能进入所选线路。";

        private const string ServerTipPrefix = "server tip=";

        /// <summary>
        /// Status 是哪一类。Transient = 列线失败与本地拒绝的提示,下一次列线有了答复就收掉(否则面板开着时
        /// 一条「该线路已满」会一直挂着);Switch = 切线的进度与结论:进度与成功的结论留到下一次切线或进场,
        /// 没成的结论另有寿命(<see cref="FailureStage"/>),不会一直挂着。
        /// </summary>
        private enum StatusSource { None, Transient, Switch }

        /// <summary>
        /// 状态行上那条「切线没成」文案的寿命阶段(规则见 <see cref="ReviewSwitchFailure"/>)。
        /// None = 状态行不是这类文案;Fresh = 刚写上,之后还没有列线答复到过;Reviewed = 已经过了至少一次列线答复。
        /// </summary>
        private enum FailureStage { None, Fresh, Reviewed }

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<ulong> _currentSceneId;
        private readonly Func<uint> _currentSceneConfigId;
        private readonly Func<float> _clock;

        private object _observedConnection;
        private bool _disposed;
        // 会话世代:断线 / 连接身份变化 / Dispose 时 +1。63 的回调带着发出时的世代,对不上就整包丢弃。
        private int _generation;
        // 切线令牌:每发起或结束一次切线 +1。同一会话里上一次切线的迟到回调(入场通知抢先结算、
        // 本地超时之后才到的应答)靠它识别。
        private int _switchToken;

        private IReadOnlyList<SceneChannelLine> _lines = SceneChannelModels.NoLines;
        // 下一次允许发 43 的时刻。存「下次可发时刻」而不是「上次发送时刻」,
        // 是为了让恰好相隔一个间隔的两次发送不被 float 减法的舍入误判为过快(同 TeamClient.TooSoon)。
        private float _nextListAt = float.NegativeInfinity;
        // 43 已发出、正在等 31;到 _listDeadline 还没等到就结束等待。
        private bool _listAwaiting;
        private float _listDeadline;
        // 下一次允许发 63 的时刻(做法同 _nextListAt);断线复位时清掉。
        private float _nextSwitchAt = float.NegativeInfinity;
        private ulong _switchTargetSceneId;
        private float _switchDeadline;
        // 在途切线的同步应答已到且无错(已受理)。受理之前到的非目标入场通知不是这次切线的结果,见 HandleSceneEntered。
        private bool _switchAccepted;
        // 发起切线那一刻目录给的冷却秒数:入场通知到达时目录已被清空,只能事先记下。
        private uint _switchCooldownSeconds;
        private bool _cooldownRunning;
        private float _cooldownUntil;
        // 「结果未知」地收了口的那次切线(见 FailSwitch 的 outcomeUnknown):目标线、当时的冷却秒数、这份记录认到什么时候。
        // _lateTargetSceneId 为 0 = 没有这样的记录。下一次切线发出、任何一次进场、断线都会清掉它。
        private ulong _lateTargetSceneId;
        private uint _lateTargetChannelNo;
        private uint _lateTargetCooldownSeconds;
        private float _lateTargetUntil;
        // 状态行上那条「切线没成」文案的寿命阶段,以及它说的是哪条线(受理后被服务端提示判失败、或已追认成
        // 「N线已满 / 正在回收」时非 0)。SetStatus 每次写入都会清这三个值,所以它们非零时一定对应眼前这条文案。
        private FailureStage _failureStage;
        private ulong _failedSceneId;
        private uint _failedChannelNo;
        private StatusSource _statusSource;
        private bool _inBattle;
        private bool _travelling;
        private bool _teamFollower;

        // ── 状态(只读)──────────────────────────────────────────────────────

        /// <summary>当前地图的线路,按线号升序;永不为 null。进场 / 断线即清空,目录每到一次整批换新。</summary>
        public IReadOnlyList<SceneChannelLine> Lines => _lines;
        /// <summary>已拿到当前地图的目录。false = 还没拉到,或当前场景没有目录(副本 / 镜像 / 目录未发布)。</summary>
        public bool HasDirectory { get; private set; }
        /// <summary>
        /// 当前场景有没有「分线」这回事。false = 副本 / 镜像(入场通知或 31 里的场景信息带着镜像 / 副本配置号):
        /// 服务端对这类场景永远不带目录,所以不列线(<see cref="RequestList"/> 直接返回 false、进场也不拉),
        /// <see cref="HasDirectory"/> 保持 false。还不知道(未进场 / 刚断线)时为 true。
        /// 它分不出「目录暂时没发布」:那种情况仍是 true,由调用方自己限制补问的次数。
        /// </summary>
        public bool DirectoryApplicable { get; private set; } = true;
        /// <summary>所在线的线号;目录未就绪或目录里找不到当前 scene_id 时为 0(角标显示「线路」)。</summary>
        public uint CurrentChannelNo { get; private set; }
        /// <summary>目录里的 switch_enabled;没有目录时为 false。</summary>
        public bool SwitchEnabled { get; private set; }
        /// <summary>目录里的冷却秒数配置(0 = 无冷却);没有目录时为 0。</summary>
        public uint SwitchCooldownSeconds { get; private set; }
        /// <summary>
        /// 有一次列线尚无结论:43 已发出在等 31,或因最小间隔排着队等补发(<see cref="ListQueued"/>)。
        /// 界面的「正在获取线路」看这一个就够。
        /// </summary>
        public bool ListPending => _listAwaiting || ListQueued;
        /// <summary>有一次列线请求因最小间隔被推迟,等 <see cref="Tick"/> 到点补发。</summary>
        public bool ListQueued { get; private set; }
        /// <summary>切线请求在途(已发出,尚无结论)。</summary>
        public bool SwitchPending { get; private set; }
        /// <summary>在途切线的目标 scene_id;不在途为 0。</summary>
        public ulong SwitchTargetSceneId => _switchTargetSceneId;
        /// <summary>在途切线的目标线号;不在途为 0。</summary>
        public uint SwitchTargetChannelNo { get; private set; }
        /// <summary>界面状态行文案;"" 表示没有要说的。</summary>
        public string Status { get; private set; } = "";
        /// <summary><see cref="Status"/> 是不是一条失败 / 不可用提示(界面据此着色)。</summary>
        public bool StatusIsError { get; private set; }

        /// <summary>
        /// 本地切线冷却的剩余秒数(0 = 不在冷却)。每次读取都按时钟现算,界面可以每帧读来刷倒计时;
        /// 服务端不下发剩余秒数,重登后本地倒计时丢失,那时仍在冷却的话由服务端拒绝。
        /// </summary>
        public float CooldownRemainingSeconds
        {
            get
            {
                if (!_cooldownRunning) return 0f;
                float remaining = _cooldownUntil - _clock();
                return remaining > 0f ? remaining : 0f;
            }
        }

        // ── 宿主才知道的状态(由宿主写入;变化时触发 Changed)──────────────────

        /// <summary>战斗或观战中,或正在排队匹配。</summary>
        public bool InBattle { get => _inBattle; set => SetHostFlag(ref _inBattle, value); }
        /// <summary>正在传送 / 换图(地图窗的请求在途、跨区传送在途或正在换连接)。</summary>
        public bool Travelling { get => _travelling; set => SetHostFlag(ref _travelling, value); }
        /// <summary>在队伍里且不是队长(v1 只有队长能切线)。</summary>
        public bool TeamFollower { get => _teamFollower; set => SetHostFlag(ref _teamFollower, value); }

        /// <summary>状态变化(线路 / 在途标志 / Status / 冷却到点 / 宿主标志)。倒计时走动不触发。</summary>
        public event Action Changed;
        /// <summary>自己发起的切线已抵达;参数是到达的线号。触发时状态已全部更新,且在同一次的 Changed 之后。</summary>
        public event Action<uint> Switched;

        /// <param name="net">传输接口;构造时在它上面注册 31 / 43 两个处理器并订阅断线。</param>
        /// <param name="connectionIdentity">
        /// 当前连接的不透明标识(生产传 GameClient.GateConnectionIdentity),只做引用比较;
        /// GameClient 静默换 gate 不一定发断线通知,靠它发现。null = 不做这项检查。
        /// </param>
        /// <param name="currentSceneId">权威的当前 scene_id(GameClient.CurrentSceneId);未进场为 0。</param>
        /// <param name="currentSceneConfigId">权威的当前地图(GameClient.CurrentSceneConfigId);未进场为 0。</param>
        /// <param name="clock">单调秒数;null 时用自带的秒表。</param>
        public SceneChannelClient(IBattleTransport net,
                                  Func<object> connectionIdentity,
                                  Func<ulong> currentSceneId,
                                  Func<uint> currentSceneConfigId,
                                  Func<float> clock = null)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _currentSceneId = currentSceneId ?? throw new ArgumentNullException(nameof(currentSceneId));
            _currentSceneConfigId = currentSceneConfigId ?? throw new ArgumentNullException(nameof(currentSceneConfigId));
            _connectionIdentity = connectionIdentity ?? (() => _net);
            _observedConnection = _connectionIdentity();
            if (clock == null)
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                _clock = () => (float)stopwatch.Elapsed.TotalSeconds;
            }
            else
            {
                _clock = clock;
            }

            _net.Disconnected += HandleDisconnected;
            // 一个 message id 只有一个处理器(GameClient.OnNotify 覆盖写)。31 / 43 全仓只有这里注册:
            // Net/Generated 下的同名空壳要靠 HandlerRegistry.Register 才会挂上,而它从未被调用。
            _net.RegisterNotify(MessageIds.NotifySceneInfo, HandleSceneInfo);
            _net.RegisterNotify(MessageIds.SceneInfoC2S, HandleListReply);
            // 构造不发请求:第一次列线由进场(HandleSceneEntered)或界面触发。
        }

        public void Dispose()
        {
            if (_disposed) return;
            _net.Disconnected -= HandleDisconnected;
            // IBattleTransport 没有注销推送的接口;两个处理器开头的 _disposed 判断
            // 保证 Dispose 之后到来的推送不再改动状态(做法同 TeamClient)。
            _disposed = true;
            ++_generation;
            _listAwaiting = false;
            ListQueued = false;
            EndSwitch();
        }

        // ── 请求 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 请求刷新线路目录(43 的唯一发送出口)。返回 true = 已发出、已排队等补发、或已有请求在途;
        /// false = 本地拒绝:传输未就绪(原因写在 Status)、已释放、或当前场景没有分线
        /// (<see cref="DirectoryApplicable"/> 为 false;不写 Status,界面按 HasDirectory == false 自己说明)。
        /// 与上次发送间隔不足 <see cref="MinListIntervalSeconds"/> 时不发,只记一个待补发标记,
        /// 由 <see cref="Tick"/> 到点补发恰好一次(期间的多次调用合并)。
        /// <paramref name="force"/>:已有请求在途时也再排一次(进场后用:旧请求是在上一个场景发的,
        /// 服务端发现玩家已换场景会丢弃它的回包)。**force 不绕过最小间隔**。
        /// </summary>
        public bool RequestList(bool force = false)
        {
            if (_disposed) return false;
            ObserveConnection();
            // 副本 / 镜像没有分线:问了服务端也只回一条不带目录的 31,还白白刷新一次服务端的活跃时间
            // (它的挂机判定看的是「多久没收到客户端消息」)。
            if (!DirectoryApplicable) return false;
            if (!_net.IsReady)
            {
                // 传输未就绪直接失败,不排队:连接恢复后的第一次列线由进场通知触发。
                bool wasWaiting = ListPending;
                string statusBefore = Status;
                ListQueued = false;
                FailList(ListUnavailableMessage);
                // 界面可能每隔几秒调一次:状态没有变化就不重复通知。
                if (wasWaiting || Status != statusBefore) Changed?.Invoke();
                return false;
            }
            if (_listAwaiting && !force) return true;
            bool wasQueued = ListQueued;
            ListQueued = true;
            bool sent = TrySendList(_clock());
            if (sent || !wasQueued) Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// 请求切到 <paramref name="sceneId"/> 那条线。返回 true = 请求已交给传输层(结论看
        /// <see cref="SwitchPending"/> / <see cref="Status"/> / <see cref="Switched"/>);
        /// false = 本地拒绝,原因写在 Status(下一次列线有答复时收掉;正在切线时不改 Status,保留进度文案)。
        /// 目标必须在当前列表里、<see cref="BlockReasonFor"/> 为 None,且距上一次发出 63 不少于
        /// <see cref="MinSwitchIntervalSeconds"/>(否则提示「操作太快了」)。
        /// </summary>
        public bool RequestSwitch(ulong sceneId)
        {
            if (_disposed) return false;
            ObserveConnection();
            var line = SceneChannelModels.FindLine(_lines, sceneId);
            var reason = BlockReasonFor(line);
            uint sceneConfigId = _currentSceneConfigId();
            // 有目录就一定有当前地图(目录按地图校验过);这里只是不让 scene_config_id = 0 的请求发出去。
            if (reason == SceneChannelBlockReason.None && sceneConfigId == 0) reason = SceneChannelBlockReason.NotReady;
            if (reason != SceneChannelBlockReason.None)
            {
                if (reason != SceneChannelBlockReason.Switching)
                {
                    SetStatus(SceneChannelModels.DescribeBlock(reason, CooldownRemainingSeconds), true, StatusSource.Transient);
                    Changed?.Invoke();
                }
                return false;
            }
            float now = _clock();
            if (now < _nextSwitchAt)
            {
                // 连点保护:同步拒绝一个往返就回来并解除在途,不拦的话连点几下就撞 gate 的限流。
                SetStatus(TooFastMessage, true, StatusSource.Transient);
                Changed?.Invoke();
                return false;
            }

            int generation = _generation;
            int token = ++_switchToken;
            SwitchPending = true;
            _switchTargetSceneId = sceneId;
            SwitchTargetChannelNo = line.ChannelNo;
            _nextSwitchAt = now + MinSwitchIntervalSeconds;
            _switchDeadline = now + SwitchRequestBudgetSeconds;
            _switchCooldownSeconds = SwitchCooldownSeconds;
            // 新的一次切线发出:上一次「结果未知」的记录作废,之后的入场通知只跟这一次对。
            ClearLateTarget();
            SetStatus(SwitchingText(line.ChannelNo), false, StatusSource.Switch);

            var request = new EnterSceneC2SRequest
            {
                SceneInfo = new SceneInfoComp { SceneConfigId = sceneConfigId, SceneId = sceneId },
            };
            // 一个请求只结算一次:GameClient.Call 把 onResp 包在 try/catch 里,回包处理(含 Changed 的订阅者)
            // 一旦抛异常,它会接着调 onError;没有这道闸,已受理的切线会被那次补调判成失败。
            // Call 也可能同步回调(协程宿主未就绪 / 未连接时同步 onError),下面的写法对同步回调同样安全。
            bool settled = false;
            _net.Call(MessageIds.EnterScene, request, EnterSceneC2SResponse.Parser,
                reply =>
                {
                    if (settled || !IsCurrentSwitch(generation, token)) return;
                    settled = true;
                    OnSwitchReply(reply);
                },
                error =>
                {
                    if (settled || !IsCurrentSwitch(generation, token)) return;
                    settled = true;
                    OnSwitchTransportError(error);
                });
            // 先交给传输层再通知界面:订阅者抛异常也不会让请求发不出去。
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// 宿主每帧调用。驱动:列线等待超时、切线等待超时、冷却到点(触发一次 Changed)、到点补发排队的列线请求。
        /// 没有任何到点的事时不触发 Changed。
        /// </summary>
        public void Tick()
        {
            if (_disposed) return;
            ObserveConnection();
            float now = _clock();
            bool changed = false;
            // 列线超时排在切线超时之前:同一帧两者都到点时,状态行留下的是切线的结论。
            if (_listAwaiting && now >= _listDeadline)
            {
                FailList(ListUnavailableMessage);
                changed = true;
            }
            if (SwitchPending && now >= _switchDeadline)
            {
                // 受理后一直没等到入场通知,或传输层始终没有回调。顺手刷新一次目录:等了这么久,列表早已过时。
                FailSwitch(SwitchTimeoutMessage, refresh: true, rememberTarget: false, outcomeUnknown: true);
                changed = true;
            }
            if (_cooldownRunning && now >= _cooldownUntil)
            {
                _cooldownRunning = false;
                changed = true;
            }
            if (TrySendList(now)) changed = true;
            if (changed) Changed?.Invoke();
        }

        // ── 宿主转发的外部事件 ───────────────────────────────────────────────

        /// <summary>
        /// 入场通知(GameClient.OnSceneEntered;调用时 currentSceneId 的委托必须已返回新场景)。
        /// 不论是不是自己发起的:线路缓存作废并安排一次列线(角标要显示新场景的线号;副本 / 镜像没有分线,不列)。
        /// 切线在途时:
        ///  - scene_id == 目标 → 成功(启动冷却、触发 <see cref="Switched"/>);
        ///  - 已受理之后到了别处 → 「未能进入所选线路」;
        ///  - 同步应答还没到就到了别处 → 不结算,切线仍在途(这条通知是服务端处理本次 63 之前发出的,不是它的结果)。
        /// 切线不在途、但上一次是「结果未知」地收的口(<see cref="LateArrivalWindowSeconds"/> 之内)且到的正是那条线
        /// → 那次切线其实成了,同样按成功结算。
        /// </summary>
        public void HandleSceneEntered(SceneInfoComp scene)
        {
            if (_disposed) return;
            ObserveConnection();
            float now = _clock();
            ulong sceneId = scene?.SceneId ?? 0;
            // 通知不带场景信息时当作不知道,照常问一次。
            DirectoryApplicable = scene == null || (scene.MirrorConfigId == 0 && scene.DungeonConfigId == 0);
            bool wasPending = SwitchPending;
            bool arrivedAtTarget = wasPending && sceneId != 0 && sceneId == _switchTargetSceneId;
            bool arrivedLate = !wasPending && sceneId != 0 && sceneId == _lateTargetSceneId && now <= _lateTargetUntil;
            uint targetChannelNo = arrivedLate ? _lateTargetChannelNo : SwitchTargetChannelNo;
            uint cooldownSeconds = arrivedLate ? _lateTargetCooldownSeconds : _switchCooldownSeconds;
            // 不论到的是哪:进了场,「结果未知」的那份记录就用完了。
            ClearLateTarget();

            ClearDirectory();
            // 在途的 43 是在上一个场景发的,它的回包(或拒绝)不再等。
            _listAwaiting = false;
            bool switched = arrivedAtTarget || arrivedLate;
            if (switched)
            {
                // 入场通知可能先于同步应答到达:这里先结算,EndSwitch 换掉令牌,之后才到的应答 / 报错不再作数。
                if (wasPending) EndSwitch();
                // 服务端的冷却从它应答那一刻起算,这里从抵达起算:本地只会晚放开,不会早放开。
                if (cooldownSeconds > 0)
                {
                    _cooldownRunning = true;
                    _cooldownUntil = now + cooldownSeconds;
                }
                SetStatus(SwitchedText(targetChannelNo), false, StatusSource.Switch);
            }
            else if (wasPending && _switchAccepted)
            {
                // 已受理之后被服务端送去了别处(队伍跟随、回收中的线被改派 …)。
                EndSwitch();
                SetSwitchFailure(WrongDestinationMessage, 0, 0);
            }
            else if (!wasPending)
            {
                // 不是自己发起的进场:留着的提示说的都是上一个场景的事。
                SetStatus("", false, StatusSource.None);
            }
            else
            {
                // 切线在途、同步应答还没到,到的却不是目标线。服务端是先写回同步应答、之后才可能为这次切线发入场通知的,
                // 所以这条通知只能来自别的事(队伍跟随、回收中的线被改派、更早的一次换图),而那条 63 服务端还没处理。
                // 不结算:留给随后的同步应答 / 入场通知 / 失败提示 / 等待期限收口。进度文案原样留着。
                // (真实传输层会在 Call 的调用栈里先派发收件箱,这条通知甚至可能在 RequestSwitch 返回之前就进来。)
            }
            int generation = _generation;
            ListQueued = DirectoryApplicable;
            TrySendList(now);
            Changed?.Invoke();
            // 发 43 失败时传输层会同步触发断线并把本类复位(世代随之变化):那种情况下不再报「已切换」。
            if (switched && generation == _generation) Switched?.Invoke(targetChannelNo);
        }

        /// <summary>
        /// 服务端提示(GameClient.OnServerTip)。只在切线在途时有意义:收到 <see cref="IsSwitchFailureTip"/>
        /// 认的码 → 这次切线没成,记下目标线并刷新目录(刷新后若该线已满 / 在回收,文案会换成具体原因)。
        /// 其它提示一律放过,由等待期限兜底。
        /// </summary>
        public void HandleServerTip(TipInfoMessage tip)
        {
            if (_disposed) return;
            ObserveConnection();
            uint tipId = tip?.Id ?? 0;
            if (!SwitchPending || !IsSwitchFailureTip(tipId)) return;
            // 服务端把 scene_manager 的各种拒绝统一折成一个码,拿不到「满了还是冷却中」,所以先给通用文案。
            // 1003 是「结果未知」:这次切线可能其实成了,所以多记一份,入场通知随后到达时补结算。
            FailSwitch(SwitchFailedMessage, refresh: true, rememberTarget: true,
                outcomeUnknown: tipId == (uint)common_error.KServiceUnavailable);
            Changed?.Invoke();
        }

        /// <summary>
        /// 断线:全部复位(线路、在途标志、待补发标记、冷却、Status),并换世代隔离旧连接的迟到回调。
        /// 本类已自行订阅传输接口的断线事件;宿主再调一次也安全(幂等,没有变化时不触发 Changed)。
        /// </summary>
        public void HandleDisconnected()
        {
            if (_disposed) return;
            _observedConnection = _connectionIdentity();
            if (ResetSession()) Changed?.Invoke();
        }

        // ── 能不能切 ────────────────────────────────────────────────────────

        /// <summary>此刻判定「能不能切线」用的全部状态(本类的 + 宿主写入的)。</summary>
        public SceneChannelSwitchContext SwitchContext => new SceneChannelSwitchContext
        {
            Connected = !_disposed && _net.IsReady,
            HasDirectory = HasDirectory,
            InBattle = _inBattle,
            Travelling = _travelling,
            SwitchPending = SwitchPending,
            TeamFollower = _teamFollower,
            SwitchEnabled = SwitchEnabled,
            CooldownRemainingSeconds = CooldownRemainingSeconds,
        };

        /// <summary>整张面板都点不了的原因(None = 没有);文案用 SceneChannelModels.DescribeBlock。</summary>
        public SceneChannelBlockReason GlobalBlockReason => SceneChannelModels.EvaluateGlobal(SwitchContext);

        /// <summary>这条线此刻为什么不能点(None = 可以切);与 <see cref="RequestSwitch"/> 的前置校验是同一条规则。</summary>
        public SceneChannelBlockReason BlockReasonFor(SceneChannelLine line) =>
            SceneChannelModels.Evaluate(line, SwitchContext);

        // ── 纯函数 ──────────────────────────────────────────────────────────

        public static string SwitchingText(uint channelNo) => "正在切换到 " + SceneChannelModels.LineName(channelNo) + "…";
        public static string SwitchedText(uint channelNo) => "已切换到 " + SceneChannelModels.LineName(channelNo) + "。";
        public static string LineFullText(uint channelNo) => SceneChannelModels.LineName(channelNo) + "已满，请选择其他线路。";
        public static string LineClosingText(uint channelNo) => SceneChannelModels.LineName(channelNo) + "正在回收，请选择其他线路。";

        /// <summary>
        /// 切线同步拒绝码 → 给人看的文案(63 的响应体,或 gate 写在信封上的码)。
        /// 码只写枚举名、不写数字:号由服务端导表器发,手抄的数字下次导表就可能对不上。
        /// 认不出的码退回裸编号 —— 宁可显示得难看,也不要编一个可能是错的原因。
        /// </summary>
        public static string DescribeSwitchTip(uint tipId)
        {
            switch (tipId)
            {
                case (uint)scene_error.KEnterSceneYouInCurrentScene: return AlreadyOnLineMessage;
                case (uint)scene_error.KEnterSceneChangingScene: return ChangingSceneMessage;
                // 同步的 3023 来自 scene 节点的战斗闸;scene_manager 的拒绝(满 / 冷却 / 回收中)走受理后的提示。
                case (uint)scene_error.KEnterSceneFailed: return SwitchRejectedMessage;
                case (uint)common_error.KRateLimitExceeded: return TooFastMessage;
                case (uint)common_error.KServiceUnavailable: return ServerBusyMessage;
                default: return "切线失败（tip=" + tipId.ToString(CultureInfo.InvariantCulture) + "）。";
            }
        }

        /// <summary>
        /// 这条服务端提示是不是「在途的这次切线没成」。只认服务端在 EnterScene 受理之后会补推的三个码
        /// (服务端 player_lifecycle.cpp 的换图应答 / 传输失败分支,gate 的路由失败分支):
        ///  - kEnterSceneFailed:scene_manager 拒绝(满 / 冷却 / 回收中 / 功能关闭 …),或交接起不来、中途放弃;
        ///  - kEnterSceneChangingScene:要起跨节点交接时发现已有一次交接在途,这次请求被丢弃;
        ///  - kServiceUnavailable:scene 到 scene_manager 的调用传输失败,或 gate 路由不到 scene 节点。
        ///    这一条服务端的本意是「结果未知,别再等了」;而且 gate 对任何路由不到的请求(好友 / 聊天 / 帮会 …)
        ///    推的也是这个码、不带请求号,客户端对不上号。认它是为了不干等满 75 秒,代价是可能把一次其实成功的
        ///    切线先报成失败 —— 所以目标线的入场通知随后到达时按成功补结算(<see cref="HandleSceneEntered"/>)。
        /// 为什么不是「在途期间收到任何提示都算失败」:一条无关的提示会把一次其实成功的切线先报成失败
        /// (GameClient.IsTravelFailureTip 是同一个考虑,它因此不认 kServiceUnavailable;这里认,理由与补救见上)。
        /// 认不出的码不下结论,交给等待期限兜底。
        /// </summary>
        public static bool IsSwitchFailureTip(uint tipId) =>
            tipId == (uint)scene_error.KEnterSceneFailed ||
            tipId == (uint)scene_error.KEnterSceneChangingScene ||
            tipId == (uint)common_error.KServiceUnavailable;

        // ── 推送 ────────────────────────────────────────────────────────────
        // 两个处理器都跑在 gate 的收包分发里,绝不能抛异常:抛出去会打断同一批里后面的消息。

        /// <summary>
        /// 31:线路目录。带目录且地图与当前一致 → 更新线路;不带目录或地图对不上 → 当前场景没有目录
        /// (副本 / 镜像 / 目录未发布 / 上一张图的迟到回包)。无论哪种都结束列线等待。
        /// 所在线按「此刻」的 scene_id 重新标,所以同图上一条线的迟到回包也不会标错。
        /// 回包里当前场景的信息若带着镜像 / 副本配置号,<see cref="DirectoryApplicable"/> 随之置 false。
        /// </summary>
        private void HandleSceneInfo(MessageContent content)
        {
            if (_disposed) return;
            ObserveConnection();
            SceneInfoS2C message = null;
            if (content != null)
            {
                // 坏包只记状态:推送是至多一次的,没有重投。已有的线路不因一个坏包清掉。
                try { message = SceneInfoS2C.Parser.ParseFrom(content.SerializedMessage); }
                catch (InvalidProtocolBufferException) { message = null; }
            }
            if (message == null)
            {
                FailList(ListUnavailableMessage);
                Changed?.Invoke();
                return;
            }

            _listAwaiting = false;
            ulong currentSceneId = _currentSceneId();
            // 31 总带着当前场景的信息。宿主晚于进场才接上(错过了入场通知)时,靠它得知这里是不是副本 / 镜像;
            // 只认 scene_id 对得上的那一条,上一个场景的迟到回包不作数。
            if (currentSceneId != 0)
            {
                foreach (var info in message.SceneInfo)
                {
                    if (info == null || info.SceneId != currentSceneId) continue;
                    DirectoryApplicable = info.MirrorConfigId == 0 && info.DungeonConfigId == 0;
                    break;
                }
            }
            // 这里没有分线:排着队的补发也不必发了。
            if (!DirectoryApplicable) ListQueued = false;
            var directory = message.ChannelDirectory;
            uint sceneConfigId = _currentSceneConfigId();
            if (directory != null && sceneConfigId != 0 && directory.SceneConfigId == sceneConfigId)
            {
                _lines = SceneChannelModels.BuildLines(directory, currentSceneId);
                HasDirectory = true;
                CurrentChannelNo = SceneChannelModels.CurrentChannelNo(_lines);
                SwitchEnabled = directory.SwitchEnabled;
                SwitchCooldownSeconds = directory.SwitchCooldownSeconds;
            }
            else
            {
                ClearDirectory();
            }
            // 列线有了答复(带不带目录都算):之前的「获取不到」与本地拒绝提示不再成立,没有目录时界面按
            // HasDirectory == false 自己说明;留着的切线失败文案按它的寿命复核。
            if (_statusSource == StatusSource.Transient) SetStatus("", false, StatusSource.None);
            else ReviewSwitchFailure();
            Changed?.Invoke();
        }

        /// <summary>
        /// 43 的回包:只看信封上的错误码。SceneInfoC2S 的应答类型是 Empty,正常情况下没有回包;
        /// 被 gate 拒绝(限流 1008 等)时回包带着请求 id 回来,没有在途项可配,就落到这里。
        /// </summary>
        private void HandleListReply(MessageContent content)
        {
            if (_disposed) return;
            ObserveConnection();
            uint tipId = content?.ErrorMessage?.Id ?? 0;
            if (tipId == 0) return;
            bool rateLimited = tipId == (uint)common_error.KRateLimitExceeded;
            if (rateLimited)
            {
                // gate 明说太快了:从现在起再让出一个完整间隔,不管本地原先算的下次可发时刻是多少。
                float backoff = _clock() + MinListIntervalSeconds;
                if (backoff > _nextListAt) _nextListAt = backoff;
            }
            // 不在等待(已超时 / 已被 31 结束 / 进场时作废)的旧请求,它的拒绝不再改状态。
            if (!_listAwaiting) return;
            FailList(rateLimited ? TooFastMessage : ListUnavailableMessage);
            Changed?.Invoke();
        }

        // ── 内部 ────────────────────────────────────────────────────────────

        private void ObserveConnection()
        {
            object current = _connectionIdentity();
            if (ReferenceEquals(current, _observedConnection)) return;
            // GameClient 静默换 gate(重定向)不一定发断线通知;连接对象变了就按断线处理。
            _observedConnection = current;
            if (ResetSession()) Changed?.Invoke();
        }

        /// <summary>整体复位并换世代;返回是否有对外可见的状态被改动。宿主写入的标志归宿主管,不动。</summary>
        private bool ResetSession()
        {
            bool dirty = HasDirectory || _lines.Count != 0 || CurrentChannelNo != 0 || ListPending
                         || SwitchPending || _cooldownRunning || Status.Length != 0 || !DirectoryApplicable;
            ++_generation;
            ClearDirectory();
            // 新会话在哪个场景还不知道,等入场通知。
            DirectoryApplicable = true;
            _listAwaiting = false;
            ListQueued = false;
            // 限流按连接算:换了连接,gate 那边的计数也是新的。
            _nextListAt = float.NegativeInfinity;
            _nextSwitchAt = float.NegativeInfinity;
            _listDeadline = 0f;
            EndSwitch();
            ClearLateTarget();
            _cooldownRunning = false;
            _cooldownUntil = 0f;
            SetStatus("", false, StatusSource.None);
            return dirty;
        }

        private void ClearDirectory()
        {
            _lines = SceneChannelModels.NoLines;
            HasDirectory = false;
            CurrentChannelNo = 0;
            SwitchEnabled = false;
            SwitchCooldownSeconds = 0;
        }

        /// <summary>43 的唯一发送点。只有「有待补发标记、传输就绪、间隔已到」三者都满足才发;返回是否发出。</summary>
        private bool TrySendList(float now)
        {
            if (!ListQueued || !_net.IsReady || now < _nextListAt) return false;
            // 先落状态再交给传输层:发送失败时传输层会同步触发断线,复位必须盖在这些赋值之后。
            ListQueued = false;
            _listAwaiting = true;
            _nextListAt = now + MinListIntervalSeconds;
            _listDeadline = now + ListTimeoutSeconds;
            // 分线目录是显式开关:不带 WithChannelDirectory 的请求,服务端只回场景信息、不读目录。
            _net.SendOneWay(MessageIds.SceneInfoC2S, new SceneInfoRequest { WithChannelDirectory = true });
            return true;
        }

        /// <summary>结束对 31 的等待并写提示。排着队的补发不受影响(它还会发出去)。</summary>
        private void FailList(string message)
        {
            _listAwaiting = false;
            // 切线在途时不盖进度文案:切线的结论随后会自己写 Status。
            if (!SwitchPending) SetStatus(message, true, StatusSource.Transient);
        }

        /// <summary>63 的同步应答:响应体带拒绝码 → 立即失败;否则只是受理,把等待期限顺延到交接预算。</summary>
        private void OnSwitchReply(EnterSceneC2SResponse reply)
        {
            uint tipId = reply?.ErrorMessage?.Id ?? 0;
            if (tipId != 0)
            {
                BackOffSwitchIfRateLimited(tipId);
                // 「已在该线路」说明本地目录标错了当前线,顺手刷新;其余同步拒绝与目录新旧无关。
                bool stale = tipId == (uint)scene_error.KEnterSceneYouInCurrentScene;
                // 响应体带码 = 服务端明确没做这次切线,不是「结果未知」。
                FailSwitch(DescribeSwitchTip(tipId), refresh: stale, rememberTarget: false, outcomeUnknown: false);
                Changed?.Invoke();
                return;
            }
            _switchAccepted = true;
            // 受理不是抵达:只延不缩,对外状态不变,所以不触发 Changed。
            float until = _clock() + SwitchAcceptedBudgetSeconds;
            if (until > _switchDeadline) _switchDeadline = until;
        }

        /// <summary>
        /// 63 的传输层失败。GameClient.Call 把信封错误折成 "server tip=N",其余是 "rpc timeout" /
        /// "disconnected" / "not connected" / "send failed: …" / "parse response: …"。
        /// 信封上带码 = gate(或 scene)明确拒绝了这一包。其余的(应答超时等)服务端其实可能已经受理:
        /// 这里照样收口,但按「结果未知」记一份,之后目标线的入场通知若到达,按切线成功补结算。
        /// </summary>
        private void OnSwitchTransportError(string error)
        {
            uint tipId = ParseEnvelopeTip(error);
            BackOffSwitchIfRateLimited(tipId);
            FailSwitch(tipId != 0 ? DescribeSwitchTip(tipId) : SwitchFailedMessage, refresh: false, rememberTarget: false,
                outcomeUnknown: tipId == 0);
            Changed?.Invoke();
        }

        /// <summary>63 被 gate 限流(1008):从现在起让出一个完整的限流窗口,不管本地原先算的下次可发时刻是多少。</summary>
        private void BackOffSwitchIfRateLimited(uint tipId)
        {
            if (tipId != (uint)common_error.KRateLimitExceeded) return;
            float backoff = _clock() + RateLimitBackoffSeconds;
            if (backoff > _nextSwitchAt) _nextSwitchAt = backoff;
        }

        private static uint ParseEnvelopeTip(string error) =>
            error != null && error.StartsWith(ServerTipPrefix, StringComparison.Ordinal)
            && uint.TryParse(error.Substring(ServerTipPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture,
                out uint tipId)
                ? tipId
                : 0;

        /// <summary>
        /// 在途切线收口为「没成」。<paramref name="rememberTarget"/>:记下是哪条线没进去,下一份目录到达时据此
        /// 把笼统文案追认成「N线已满 / 正在回收」。<paramref name="outcomeUnknown"/>:这次其实可能已被服务端受理
        /// (1003 / 应答超时 / 等待超时),记一份,<see cref="LateArrivalWindowSeconds"/> 内目标线的入场通知到达时
        /// 按成功补结算 —— 否则人过去了,本地却不起冷却,紧接着再点只会吃到服务端的冷却拒绝。
        /// </summary>
        private void FailSwitch(string message, bool refresh, bool rememberTarget, bool outcomeUnknown)
        {
            ulong sceneId = _switchTargetSceneId;
            uint channelNo = SwitchTargetChannelNo;
            uint cooldownSeconds = _switchCooldownSeconds;
            EndSwitch();
            SetSwitchFailure(message, rememberTarget ? sceneId : 0, rememberTarget ? channelNo : 0);
            if (outcomeUnknown)
            {
                _lateTargetSceneId = sceneId;
                _lateTargetChannelNo = channelNo;
                _lateTargetCooldownSeconds = cooldownSeconds;
                _lateTargetUntil = _clock() + LateArrivalWindowSeconds;
            }
            if (refresh && DirectoryApplicable)
            {
                ListQueued = true;
                TrySendList(_clock());
            }
        }

        /// <summary>结束在途切线(不论成败)并换令牌,让这次切线尚未到达的回调全部失效。</summary>
        private void EndSwitch()
        {
            ++_switchToken;
            SwitchPending = false;
            _switchTargetSceneId = 0;
            SwitchTargetChannelNo = 0;
            _switchDeadline = 0f;
            _switchAccepted = false;
            _switchCooldownSeconds = 0;
        }

        private void ClearLateTarget()
        {
            _lateTargetSceneId = 0;
            _lateTargetChannelNo = 0;
            _lateTargetCooldownSeconds = 0;
            _lateTargetUntil = 0f;
        }

        /// <summary>
        /// 列线有了答复时,复核状态行上留着的「切线没成」文案(进度与成功的文案不归它管):
        ///  - 说得出是哪条线、且那条线在这份目录里已满 / 回收中 → 文案换成(或保持)具体原因,
        ///    之后每份目录都再核一次,那条线一恢复(或已不在目录里)就收掉;
        ///  - 其余情况:失败后的第一次答复先留着 —— 失败时顺手拉的目录几十毫秒就到,立刻收掉等于没提示过;
        ///    第二次起收掉。否则面板开着时(每 5 秒刷新)一条「切线失败」「3线已满」会和列表里已经恢复
        ///    流畅的那一行一直并排挂着,直到下一次切线或进场。
        /// 笼统文案只在第一次答复时追认成具体原因:那份目录是因为这次失败才去拉的,最能说明当时为什么没成。
        /// </summary>
        private void ReviewSwitchFailure()
        {
            if (_failureStage == FailureStage.None) return;
            bool first = _failureStage == FailureStage.Fresh;
            ulong sceneId = _failedSceneId;
            uint channelNo = _failedChannelNo;
            var line = SceneChannelModels.FindLine(_lines, sceneId);
            string specific = line == null ? null
                : line.Load == SceneChannelLoad.Full ? LineFullText(channelNo)
                : line.Load == SceneChannelLoad.Closing ? LineClosingText(channelNo)
                : null;
            if (specific != null)
            {
                // 继续记着这条线:下一份目录还要核它恢复了没有。
                SetSwitchFailure(specific, sceneId, channelNo, FailureStage.Reviewed);
            }
            else if (first)
            {
                // 文案原样留着;不再记是哪条线,之后不追认。
                _failureStage = FailureStage.Reviewed;
                _failedSceneId = 0;
                _failedChannelNo = 0;
            }
            else
            {
                SetStatus("", false, StatusSource.None);
            }
        }

        /// <summary>
        /// Status 的唯一写入点。任何一次写入都把「切线没成」文案的寿命状态清零:它只对那一条文案有效
        /// (要写这类文案走 <see cref="SetSwitchFailure"/>)。
        /// </summary>
        private void SetStatus(string text, bool isError, StatusSource source)
        {
            Status = text ?? "";
            StatusIsError = isError && Status.Length != 0;
            _statusSource = Status.Length == 0 ? StatusSource.None : source;
            _failureStage = FailureStage.None;
            _failedSceneId = 0;
            _failedChannelNo = 0;
        }

        /// <summary>
        /// 写一条「切线没成」的文案并记下它的寿命阶段(见 <see cref="ReviewSwitchFailure"/>)。
        /// <paramref name="sceneId"/> 非 0 = 说得出是哪条线没进去。
        /// </summary>
        private void SetSwitchFailure(string text, ulong sceneId, uint channelNo, FailureStage stage = FailureStage.Fresh)
        {
            SetStatus(text, true, StatusSource.Switch);
            _failureStage = stage;
            _failedSceneId = sceneId;
            _failedChannelNo = channelNo;
        }

        private void SetHostFlag(ref bool field, bool value)
        {
            if (field == value) return;
            field = value;
            if (!_disposed) Changed?.Invoke();
        }

        // 回调作数的条件:本类没释放、会话世代没变、这次切线仍在途且令牌没换、连接还是发出时观察到的那条。
        private bool IsCurrentSwitch(int generation, int token) =>
            !_disposed && generation == _generation && SwitchPending && token == _switchToken
            && ReferenceEquals(_observedConnection, _connectionIdentity());
    }
}
