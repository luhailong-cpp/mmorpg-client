using System;
using System.Collections.Generic;
using System.Globalization;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using Teampb;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.Team
{
    /// <summary>
    /// 组队的网络真相。单飞 + 发送间隔 + 会话代次 + 连接身份校验,旧回包不能污染重连或换角;
    /// 快照按 (MembershipEpoch, Version) 排序应用,推送与回包共用同一套规则。
    /// 不依赖 TeamUiState 与 UnityEngine:界面订阅 Changed 后整份镜像本对象的只读状态。
    /// </summary>
    public sealed partial class TeamClient : IDisposable
    {
        public const string ConnectingMessage = "正在连接组队服务…";
        public const string RecoveryMessage   = "组队请求状态尚未确认，请重新登录角色后再试。";
        public const string SuspendedMessage  = "组队服务暂不可用，请稍后重新登录再试。";
        // 与 robot 的 400ms 一致,只防连点;gate 的限流按 message_id 另算,见 MessageWindowSeconds。
        public const float  MinSendIntervalSeconds      = 0.4f;
        /// <summary>
        /// 「应当在排、视图却没在排」时隔多久重拉一次队伍视图、最多几次。服务端读排队票据出错时会把视图如实下发成
        /// 没在排,并在随后约 5 秒内不再读(熔断),所以间隔要跨过 5 秒。
        /// </summary>
        public const float  QueueViewVerifyDelaySeconds = 6f;
        public const int    QueueViewVerifyAttempts     = 2;
        public const float  PanelRefreshIntervalSeconds = 30f;
        /// <summary>
        /// 同一 message_id 的本地发送窗口。gate 的 MessageLimiter 按 message_id 各自计数、时间戳取整秒,
        /// 淘汰条件是 now - 最早 > 1,所以"每秒 N 次"实际是"跨两个整秒、最长约 2 秒内只放 N 次";
        /// 再留 0.5 秒给上行抖动。撞上限流(1008)会给本会话计一次非法包,累计 50 次 gate 断开连接。
        /// </summary>
        public const float  MessageWindowSeconds        = 2.5f;
        /// <summary>后台读失败(限流、服务端读失败、回包不带视图)后,排队的读至少隔这么久再补发。</summary>
        public const float  ReadRetryDelaySeconds       = 3f;
        // data/MessageLimiter.xlsx:GetMyTeam / ListMyInvites 每秒 5 次,其余 10 个写操作每秒 3 次。
        private const int ReadQuota = 5;
        private const int WriteQuota = 3;
        private const string ServerTipPrefix = "server tip=";

        private const string NotInGameMessage = "请进入角色后再使用组队。";
        private const string BusyMessage = "上一个组队请求仍在处理中，请稍候。";
        private const string TooFastMessage = "操作太快了，请稍候再试。";
        private const string InvalidTargetMessage = "请输入有效的玩家编号。";
        private const string MatchLockedMessage = "队伍正在进入战斗，请稍候。";
        public DurableRequestFileStore MatchRecovery { get; set; }
        private StartTeamMatchRequest _pendingStart;
        private ulong _startRecoveryPlayer;
        private bool _startRecoveryBlocked;
        private bool _startHadUncertainAttempt;
        private const string MatchQueuedMessage = "队伍正在匹配中，如需重新开始请先取消匹配。";
        private bool _backfillIntentRecoverySupported;
        private const string NotMemberMessage = "该玩家不在队伍中。";

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<float> _clock;
        private readonly Action<Action<TeamSnapshotS2C>> _unsubscribeSnapshot;
        // 213 由 GameClient 解析后经事件送达;退订必须传同一个委托实例。
        private readonly Action<TeamSnapshotS2C> _snapshotHandler;
        private readonly List<TeamInvite> _invites = new List<TeamInvite>();
        private object _observedConnection;
        private int _generation;
        private bool _disposed;
        // 本会话是否已应用过一份视图;false 时下一份视图无条件接受(排序基线无效)。
        private bool _hasView;
        // 下一次 GetMyTeam 是否带 notify_online:构造 / Reset 置位,真正发出后清零。
        private bool _notifyOnlineQueued;
        // 下一次 GetMyTeam 是否带 regroup(请求归队):RequestRegroup 置位,真正发出后清零;Reset 清掉(新会话进场时服务端自己会跟随)。
        private bool _regroupQueued;
        private float _lastSendAt;
        private float _lastRefreshAt;
        // 读失败后的退避:此刻之前 DrainQueued 不补发排队的读(探针也算),避免服务端故障时每 0.4 秒打一次。
        private float _readRetryAt;
        // 「应当在排、视图却没在排」时的有界核实(见 ExpectQueuedView):剩余次数与下一次的时刻。
        private int _queueViewVerifyLeft;
        private float _queueViewVerifyAt = float.PositiveInfinity;
        private bool _startExpectsQueue;   // 在途的 StartMatch 带的目标人数大于当时的人数(补位)
        // 每个 message_id 最近几次真正发出的时刻。限流按连接算,Reset 不清(同 _lastSendAt)。
        private readonly Dictionary<uint, Queue<float>> _sentAt = new Dictionary<uint, Queue<float>>();
        // ListMyInvites 发出之后收到的邀请推送 / 撤回事件(按 TeamId)。邀请没有排序键,推送走 Kafka、
        // 回包走路由服,两路不保序:更早读的回包整表替换时,据此补回新推送的邀请、剔除已撤回的邀请。
        private readonly HashSet<ulong> _pushedInvites = new HashSet<ulong>();
        private readonly HashSet<ulong> _revokedInvites = new HashSet<ulong>();
        // 在途读取发出时从 SyncReadStarted 取到的确认委托;单飞保证同一时刻最多一个,每次发送都会重写。
        private Action _syncReadAck;

        // ── 状态(只读;调用方不得修改返回的对象)──
        public ulong PlayerId => _net.PlayerId;
        /// <summary>永不为 null;无队或未加载时 TeamId == 0。</summary>
        public TeamSnapshot Snapshot { get; private set; }
        // 同一地域探测协调器由生产UI根注入；只有已组队玩家周期维护，避免全服空闲玩家持续探测。
        public Action<Action, Action<string>> PrepareMatchNetwork { get; set; }
        private float _nextNetworkProbeAt;
        /// <summary>永不为 null;收到的邀请。</summary>
        public IReadOnlyList<TeamInvite> Invites { get; }
        /// <summary>本会话探针 GetMyTeam 已收到回包(TeamResponse),即使带了内层 tip。</summary>
        public bool HasLoaded { get; private set; }
        /// <summary>
        /// 本会话已应用过一份权威视图。服务端读失败时 GetMyTeam 只回 tip、不带视图:此时 HasLoaded 为真
        /// 而 HasView 为假,Snapshot 仍是占位的"无队",界面不能据此判定玩家不在队伍里。
        /// </summary>
        public bool HasView => _hasView;
        /// <summary>
        /// 已知名单可用于新动作的权限预检。读取失败会保留 Snapshot，但将它标成过期；
        /// 新的权威视图（包括同版本的在线态刷新）才恢复此标记。调用方还须检查连接可用与忙态。
        /// </summary>
        public bool HasFreshView { get; private set; }
        /// <summary>传输层单飞:有 Call 在途。</summary>
        public bool Busy { get; private set; }
        public TeamAction PendingAction { get; private set; }
        /// <summary>在途请求的目标:玩家编号(申请 / 审批 / 邀请 / 踢人 / 转让)或队伍编号(应答邀请);其余为 0。</summary>
        public ulong PendingTarget { get; private set; }
        public bool RequiresReconnect { get; private set; }
        /// <summary>灰度停用 / 外层错误:本连接内不再发任何 team 消息,只在连接变化时清除。</summary>
        public bool Suspended { get; private set; }
        public bool RefreshQueued { get; private set; }
        public bool InvitesQueued { get; private set; }
        /// <summary>4024 / 4025 / 4026 或 MATCH_FAILED 的 Parameters[0];界面据此给该成员行着色。</summary>
        public ulong HighlightPlayerId { get; private set; }
        /// <summary>界面状态栏文案;"" 表示交给界面默认提示。</summary>
        public string Status { get; private set; }
        public bool ServiceAvailable => HasLoaded && !Suspended && !RequiresReconnect;
        public bool HasTeam => Snapshot.TeamId != 0;
        public bool IsLeader => HasTeam && PlayerId != 0 && Snapshot.LeaderId == PlayerId;
        public bool MatchStarting => HasTeam && Snapshot.MatchStarting;
        /// <summary>队伍补位匹配中(整队在人数档队列里等凑满)。名单不锁:名单一变服务端就让全队退出匹配。</summary>
        public bool MatchQueued => HasTeam && Snapshot.MatchQueued;
        /// <summary>
        /// 每应用一份权威视图(或会话重置)加一。补位匹配态不带版本号,外部(TeamUiRoot 的排队同步)
        /// 靠它判断「这份快照是不是我上次看过的那一份」。
        /// </summary>
        public ulong ViewSerial { get; private set; }

        // 已结束的那一次补位排队的身份(队伍 + 入队时刻);0 = 没有。见 ConsumeBackfillQueue。
        private ulong _consumedQueueTeamId;
        private ulong _consumedQueueAtMs;
        /// <summary>Read-only action pacing for the UI; each action keeps its own Send quota.</summary>
        public bool InvitationCoolingDown => WriteCoolingDown(MessageIds.InviteToTeam);
        public bool CreationCoolingDown => WriteCoolingDown(MessageIds.CreateTeam);

        private bool WriteCoolingDown(uint messageId)
        {
            float now = _clock();
            return TooSoon(now) || !HasQuota(messageId, false, now);
        }

        public event Action Changed;
        /// <summary>
        /// 队伍补位匹配的推送提示(文案, 是否是「这次匹配已结束」)。Status 只显示在队伍面板里,而队员收到
        /// 「队长发起了匹配 / 某某取消了匹配 / 因为谁被撤单」时队伍面板通常没开 —— 界面层据此把它弹出来。
        /// 每条推送至多触发一次。
        /// </summary>
        public event Action<string, bool> BackfillNotice;

        /// <summary>
        /// 落地恢复的读取确认钩子(服务端 docs/design/landing-recovery-sync-digest.md §4.3,由 LandingSyncBindings.BindTeam 设置)。
        /// GetMyTeam / ListMyInvites 真正发出时调用一次(参数是 <see cref="TeamAction.Refresh"/> / <see cref="TeamAction.ListInvites"/>),
        /// 返回的委托在这次读取成功应用之后调用一次;返回 null 表示这次读取不需要确认。不改变任何读取的触发时机。
        /// 必须在发出时取、成功后才调:只有发出晚于摘要到达的读取才能推进缓存版本(L-2)。
        /// </summary>
        public Func<TeamAction, Action> SyncReadStarted { get; set; }

        public TeamClient(IBattleTransport net,
                          Func<object> connectionIdentity = null,
                          Action<Action<TeamSnapshotS2C>> subscribeSnapshot = null,
                          Action<Action<TeamSnapshotS2C>> unsubscribeSnapshot = null,
                          Func<float> clock = null)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
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

            _lastSendAt = _lastRefreshAt = _readRetryAt = float.NegativeInfinity;
            Invites = _invites.AsReadOnly();
            Snapshot = TeamViewMapper.ToSnapshot(null, PlayerId);
            RefreshQueued = InvitesQueued = _notifyOnlineQueued = true;
            Status = ConnectingMessage;

            _net.Disconnected += HandleDisconnected;
            // 一个 message id 只有一个处理器(GameClient.OnNotify 覆盖写)。213 已由 GameClient 自己注册
            // 并转成 OnTeamSnapshot 事件,这里绝不能再对它 RegisterNotify,否则会顶掉 GameClient 的处理器。
            _net.RegisterNotify(MessageIds.NotifyTeamEvent, HandleTeamEvent);
            _net.RegisterNotify(MessageIds.NotifyTeamInvite, HandleTeamInvite);
            _unsubscribeSnapshot = unsubscribeSnapshot;
            _snapshotHandler = HandleSnapshotPush;
            subscribeSnapshot?.Invoke(_snapshotHandler);
            // 构造不发请求、不触发 Changed:第一个请求(探针)由 DrainQueued 发出,保证"先挂推送,再发 GetMyTeam"。
        }

        // ── 会话 ────────────────────────────────────────────────────────────

        public void Reset()
        {
            // 零编号 gRPC 回包无法相关到已取消请求；同连接不得重用其槽位。
            if (Busy) RequiresReconnect = true;
            ++_generation;
            Busy = false; PendingAction = TeamAction.None; PendingTarget = 0; _syncReadAck = null;
            Snapshot = TeamViewMapper.ToSnapshot(null, PlayerId); _hasView = false; HasFreshView = false; HasLoaded = false; ViewSerial++;
            // 新会话重新相信服务端视图:断线重连后若仍在排,要能恢复「匹配中」。
            _consumedQueueTeamId = 0; _consumedQueueAtMs = 0;
            _queueViewVerifyLeft = 0; _queueViewVerifyAt = float.PositiveInfinity;
            _invites.Clear(); _pushedInvites.Clear(); _revokedInvites.Clear(); HighlightPlayerId = 0;
            RefreshQueued = InvitesQueued = _notifyOnlineQueued = true;
            _regroupQueued = false;
            // _lastSendAt / _sentAt 不清:限流按连接算。
            _lastRefreshAt = _readRetryAt = float.NegativeInfinity;
            Status = RequiresReconnect ? RecoveryMessage : Suspended ? SuspendedMessage : ConnectingMessage;
            Changed?.Invoke();
        }

        public void ObserveConnection()
        {
            if (_disposed) return;
            object current = _connectionIdentity();
            if (ReferenceEquals(current, _observedConnection)) return;
            // GameClient 静默换 Gate 不一定发 Disconnected；真实连接对象变化才解除隔离与停用。
            _observedConnection = current;
            Busy = false;
            RequiresReconnect = false;
            Suspended = false;
            Reset();
        }

        private void HandleDisconnected()
        {
            // 仅真实连接断开时，旧连接回包才不可能再进入下一连接。
            _observedConnection = _connectionIdentity();
            Busy = false;
            RequiresReconnect = false;
            Suspended = false;
            Reset();
        }

        /// <summary>
        /// 由界面每帧调用(只在组队可用时)。一次最多发一个请求:探针 / 冲突重拉 / 自愈优先,
        /// 其次邀请列表,最后是面板打开期间每 30 秒一次的静默刷新。
        /// </summary>
        public void DrainQueued(bool panelVisible)
        {
            RestoreStartIntent();
            RestoreActionIntent();
            if (_disposed) return;
            float now = _clock();
            // 过期邀请即使 Busy 也照样剔除。
            if (_invites.RemoveAll(invite => invite.ExpiresAt <= now) > 0) Changed?.Invoke();
            if (Busy || RequiresReconnect || Suspended || !_net.IsReady || PlayerId == 0) return;
            if (Snapshot.TeamId != 0 && PrepareMatchNetwork != null && now >= _nextNetworkProbeAt)
            {
                _nextNetworkProbeAt = now + 25;
                PrepareMatchNetwork(() => { }, error => { Status = error; Changed?.Invoke(); });
            }
            if (TooSoon(now)) return;
            // 「应当在排、视图却没在排」的有界核实(见 ExpectQueuedView):到点排一次静默刷新。
            if (_queueViewVerifyLeft > 0 && now >= _queueViewVerifyAt)
            {
                if (MatchQueued) _queueViewVerifyLeft = 0;
                else
                {
                    _queueViewVerifyLeft--;
                    _queueViewVerifyAt = now + QueueViewVerifyDelaySeconds;
                    RefreshQueued = true;
                }
            }
            // 排队的读在退避期内不补发;面板的 30 秒刷新本来就隔得够久,不受退避影响。
            bool retryReady = now >= _readRetryAt;
            if ((RefreshQueued || !HasLoaded) && retryReady) { SendRefresh(silent: true); return; }
            if (HasLoaded && ReplayActionIntent(now)) return;
            if (InvitesQueued && retryReady) { SendInvites(silent: true); return; }
            if (panelVisible && now >= _lastRefreshAt + PanelRefreshIntervalSeconds) SendRefresh(silent: true);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _net.Disconnected -= HandleDisconnected;
            _unsubscribeSnapshot?.Invoke(_snapshotHandler);
            // IBattleTransport 没有注销推送的接口;三个推送处理器开头的 _disposed 判断
            // 保证 Dispose 之后到来的推送不再改动状态。
            _disposed = true; ++_generation; Busy = false; PendingAction = TeamAction.None; PendingTarget = 0;
            _syncReadAck = null;
        }

        // ── 请求 ────────────────────────────────────────────────────────────
        // 返回 true 表示已交给传输层;false 表示本地拒绝,原因已写入 Status 或排队标志。
        // 用户写操作在 Busy / 间隔或同 id 窗口不足时直接拒绝、不排队:写请求带 expected_team_id,
        // 排队补发会基于过期视图,重复点击还会变成重复写。后台读则合并成排队标志。

        public bool Refresh() => SendRefresh(silent: false);

        public bool LoadInvites() => SendInvites(silent: false);

        public bool Create() =>
            Send(TeamAction.Create, 0, MessageIds.CreateTeam, new CreateTeamRequest(),
                TeamResponse.Parser, false,
                reply => FinishWrite(TeamAction.Create, 0, reply, "队伍已创建，可邀请道友加入。"));

        public bool ApplyJoin(ulong targetPlayerId) =>
            Send(TeamAction.Apply, targetPlayerId, MessageIds.ApplyJoinTeam,
                new ApplyJoinTeamRequest { TargetPlayerId = targetPlayerId },
                TeamResponse.Parser, false,
                reply => FinishWrite(TeamAction.Apply, targetPlayerId, reply, "入队申请已提交，等待队长处理。"));

        public bool HandleApplication(ulong applicantId, bool approve) =>
            Send(TeamAction.Decide, applicantId, MessageIds.HandleApplication,
                new HandleApplicationRequest { ApplicantId = applicantId, Approve = approve, ExpectedTeamId = Snapshot.TeamId },
                TeamResponse.Parser, false,
                reply => FinishWrite(TeamAction.Decide, applicantId, reply, approve ? "已同意入队申请。" : "已拒绝入队申请。"));

        public bool Invite(ulong targetPlayerId) =>
            Send(TeamAction.Invite, targetPlayerId, MessageIds.InviteToTeam,
                new InviteToTeamRequest { TargetPlayerId = targetPlayerId, ExpectedTeamId = Snapshot.TeamId },
                TeamResponse.Parser, false,
                reply => FinishWrite(TeamAction.Invite, targetPlayerId, reply, "邀请已发出，等待对方回应。"));

        public bool RespondInvite(ulong teamId, bool accept) =>
            Send(TeamAction.RespondInvite, teamId, MessageIds.RespondInvite,
                new RespondInviteRequest { TeamId = teamId, Accept = accept },
                TeamResponse.Parser, false,
                reply => FinishWrite(TeamAction.RespondInvite, teamId, reply, accept ? "已加入队伍。" : "已谢绝邀请。"));

        public bool Leave() =>
            Send(TeamAction.Leave, 0, MessageIds.LeaveTeam,
                new LeaveTeamRequest { ExpectedTeamId = Snapshot.TeamId },
                TeamResponse.Parser, false,
                reply => FinishWrite(TeamAction.Leave, 0, reply, "你已离开队伍。"));

        public bool Kick(ulong targetPlayerId) =>
            Send(TeamAction.Kick, targetPlayerId, MessageIds.KickMember,
                new KickMemberRequest { TargetPlayerId = targetPlayerId, ExpectedTeamId = Snapshot.TeamId },
                TeamResponse.Parser, false,
                reply => FinishWrite(TeamAction.Kick, targetPlayerId, reply, "已请离该队员。"));

        public bool TransferLeader(ulong targetPlayerId) =>
            Send(TeamAction.Transfer, targetPlayerId, MessageIds.TransferLeader,
                new TransferLeaderRequest { TargetPlayerId = targetPlayerId, ExpectedTeamId = Snapshot.TeamId },
                TeamResponse.Parser, false,
                reply => FinishWrite(TeamAction.Transfer, targetPlayerId, reply, "队长已转让。"));

        public bool Disband() =>
            Send(TeamAction.Disband, 0, MessageIds.DisbandTeam,
                new DisbandTeamRequest { ExpectedTeamId = Snapshot.TeamId },
                TeamResponse.Parser, false,
                reply => FinishWrite(TeamAction.Disband, 0, reply, "队伍已解散。"));

        /// <param name="targetTeamSize">0 为直接开局；目标人数更大时等待补位，必须由服务端明确声明支持。</param>
        public bool StartMatch(uint battleConfigId, uint targetTeamSize = 0)
        {
            RestoreStartIntent();
            if(_startRecoveryBlocked){Status="开局恢复记录无法读取，保留保护状态；请修复本机存储后重新登录。";Changed?.Invoke();return false;}
            if(_pendingStart!=null&&(_pendingStart.BattleConfigId!=battleConfigId||_pendingStart.TargetTeamSize!=targetTeamSize)){Status="上一笔队伍开局结果尚未确认，请先恢复原请求。";Changed?.Invoke();return false;}
            // 旧补位分支忽略 UUID。未知结果只能先读权威队列，不能盲目重发成为第二次开局。
            if (_pendingStart != null && _pendingStart.TargetTeamSize > 0 && _startHadUncertainAttempt
                && !_backfillIntentRecoverySupported)
            {
                Status = "上一笔队伍补位结果尚未确认，正在核实原队列。";
                RefreshQueued = true;
                Changed?.Invoke();
                return false;
            }
            if (PrepareMatchNetwork == null) return SendStartMatch(battleConfigId, targetTeamSize);
            int generation = _generation; ulong teamId = Snapshot.TeamId;
            PrepareMatchNetwork(() => { if (!_disposed && generation == _generation && Snapshot.TeamId == teamId) SendStartMatch(battleConfigId, targetTeamSize); },
                error => { if (!_disposed && generation == _generation) { Status = error; Changed?.Invoke(); } });
            return true;
        }

        private bool SendStartMatch(uint battleConfigId, uint targetTeamSize)
        {
            var request=_pendingStart??new StartTeamMatchRequest{BattleConfigId=battleConfigId,ExpectedTeamId=Snapshot.TeamId,TargetTeamSize=targetTeamSize,RequestId=Guid.NewGuid().ToString("D")};
            bool backfill = request.TargetTeamSize > Snapshot.Members.Count;
            return Send(TeamAction.StartMatch,0,MessageIds.StartTeamMatch,request,TeamResponse.Parser,false,reply=>{
                FinishWrite(TeamAction.StartMatch,0,reply,"已发起队伍战斗，正在集合队员…");
                // 统一后的服务器若返回正式恢复意图，始终以它为准，包括目标人数大于当前人数的请求。
                if (reply.MatchIntent != null) { ObserveStartIntent(reply); return; }
                // 远端补位分支尚无 MatchIntent；明确成功回包已确认入队，保留队列原文案。
                if (backfill && Ok(reply.ErrorMessage)) { ClearStartIntent(); return; }
                if(!_startHadUncertainAttempt&&reply.ErrorMessage!=null&&reply.ErrorMessage.Id!=0&&!TipFaults.IsFault(reply.ErrorMessage.Id)){ClearStartIntent();return;}
                ObserveStartIntent(reply);
            },beforeSend:()=>{
                // 只有真正越过 Busy/冷却/前置条件后再发原请求，才属于再次尝试；本地拒绝的连点不算。
                if (_pendingStart != null) _startHadUncertainAttempt = true;
                MatchRecovery?.Save(PlayerId,Convert.ToBase64String(request.ToByteArray()));
                _pendingStart=request;_startRecoveryPlayer=PlayerId;_startExpectsQueue=backfill;
            });
        }
        private void RestoreStartIntent()
        {
            if(MatchRecovery==null||PlayerId==0||_startRecoveryPlayer==PlayerId)return;
            _startRecoveryPlayer=PlayerId;_pendingStart=null;_startRecoveryBlocked=false;_startHadUncertainAttempt=false;_backfillIntentRecoverySupported=false;
            try{
                string raw=MatchRecovery.Load(PlayerId);if(raw.Length==0)return;
                var request=StartTeamMatchRequest.Parser.ParseFrom(Convert.FromBase64String(raw));
                if(request.ExpectedTeamId==0||request.BattleConfigId==0||!Guid.TryParseExact(request.RequestId,"D",out var parsed)||parsed==Guid.Empty)throw new InvalidOperationException();
                _pendingStart=request;_startHadUncertainAttempt=true;RefreshQueued=true;Status="有待确认队伍开局，正在查询原请求。";
            }catch(Exception){_startRecoveryBlocked=true;Status="开局恢复记录损坏或无法读取，保留保护状态。";}
        }
        private void ObserveStartIntent(TeamResponse response)
        {
            if(_pendingStart==null)return;
            var view=response.MatchIntent;
            if (view != null && view.RequestId == _pendingStart.RequestId) _backfillIntentRecoverySupported = true;
            // 旧补位契约没有恢复意图：当前同一队伍/副本/目标人数的权威队列足以证明所求效果已存在。
            // 不以空闲视图证明旧请求失败，也不把异队/异副本/异目标的队列当成原意图。
            var team = response.Team;
            if (view == null && _pendingStart.TargetTeamSize > 0 && team != null
                && team.TeamId == _pendingStart.ExpectedTeamId && team.MatchState == TeamBackfillProtocol.QueuedState
                && team.MatchBattleConfigId == _pendingStart.BattleConfigId && team.MatchTargetSize == _pendingStart.TargetTeamSize)
            {
                ClearStartIntent();
                return;
            }
            if(view!=null&&view.RequestId==_pendingStart.RequestId&&view.BattleId!=0&&view.State>=1&&view.State<=5){
                try{ClearStartIntent();}
                catch(Exception){Status="开局已受理，本机恢复记录尚未清理。";return;}
                Status=view.State==5?"原队伍开局已确认中止。":"原队伍开局已受理，正在恢复战斗。";
            }else{RefreshQueued=true;_readRetryAt=_clock()+ReadRetryDelaySeconds;Status="队伍开局结果尚未确认；重试会使用原请求。";}
        }
        private void ClearStartIntent(){if(_pendingStart==null)return;MatchRecovery?.Clear(PlayerId,Convert.ToBase64String(_pendingStart.ToByteArray()));_pendingStart=null;_startHadUncertainAttempt=false;_backfillIntentRecoverySupported=false;}

        // 请求体在调用 Send 之前构造:NotifyOnline / Regroup 取发送当下的排队值,Send 真正发出后才清零;
        // 服务端没走到上线广播 / 归队(限流、回包不带视图)时,由回包 / 错误处理按请求体重新置位。
        private bool SendRefresh(bool silent)
        {
            var request = new GetMyTeamRequest { NotifyOnline = _notifyOnlineQueued, Regroup = _regroupQueued, MatchRequestId = _pendingStart?.RequestId ?? string.Empty };
            return Send(TeamAction.Refresh, 0, MessageIds.GetMyTeam, request,
                TeamResponse.Parser, silent, reply => OnMyTeam(reply, silent, request));
        }

        private bool SendInvites(bool silent) =>
            Send(TeamAction.ListInvites, 0, MessageIds.ListMyInvites, new ListMyInvitesRequest(),
                ListMyInvitesResponse.Parser, silent, reply => OnMyInvites(reply, silent));

        // ── 纯函数 ──────────────────────────────────────────────────────────

        /// <summary>内层 tip 文案;null 或 Id == 0 返回 ""。文案照服务端 docs/design/team-system.md §D.4。</summary>
        public static string DescribeTip(TipInfoMessage tip)
        {
            if (tip == null || tip.Id == 0) return "";
            switch (tip.Id)
            {
                case (uint)team_error.KTeamPlayerId: return "目标玩家无效。";
                case (uint)team_error.KTeamMembersFull: return "队伍已满。";
                case (uint)team_error.KTeamMemberInTeam: return "已在队伍中。";
                case (uint)team_error.KTeamMemberNotInTeam: return "该玩家不在队伍中。";
                case (uint)team_error.KTeamKickSelf: return "不能请离自己。";
                case (uint)team_error.KTeamKickNotLeader: return "只有队长可以请离队员。";
                case (uint)team_error.KTeamAppointSelf: return "不能转让给自己。";
                case (uint)team_error.KTeamAppointLeaderNotLeader: return "只有队长可以转让队长。";
                case (uint)team_error.KTeamNotInApplicantList: return "该申请已失效。";
                case (uint)team_error.KTeamHasNotTeamId: return "队伍不存在或已解散。";
                case (uint)team_error.KTeamDismissNotLeader: return "只有队长可以解散队伍。";
                case (uint)team_error.KTeamPlayerNotFound: return "对方不在线。";
                case (uint)team_error.KTeamNotLeader: return "只有队长可以执行此操作。";
                case (uint)team_error.KTeamHomeZoneUnknown: return "角色区服信息异常，请重新登录。";
                case (uint)team_error.KTeamCrossZoneDenied: return "不能与其他区服的玩家组队。";
                case (uint)team_error.KTeamInviteNotFound: return "邀请已失效。";
                case (uint)team_error.KTeamInviteLimit: return "对方待处理的邀请过多，请稍后再试。";
                case (uint)team_error.KTeamInMatch: return "队伍正在进入战斗，请稍候。";
                case (uint)team_error.KTeamMemberOffline: return "有队员不在线。";
                case (uint)team_error.KTeamMemberInBattle: return "有队员正在战斗中。";
                case (uint)team_error.KTeamMemberNotReady: return "有队员暂时无法开战。";
                case (uint)team_error.KTeamDungeonNotOpen: return "该副本未开放组队。";
                case (uint)team_error.KTeamSizeExceeded: return "队伍人数超过该副本上限。";
                case (uint)team_error.KTeamStateChanged: return "队伍状态已变化，请重试。";
                case (uint)team_error.KTeamInternal: return "服务器繁忙，请稍后再试。";
                // 含保留码 4000 / 4009 / 4010 / 4012 / 4015 / 4016。
                default: return $"组队服务暂未完成请求（{tip.Id}），请稍后重试。";
            }
        }

        /// <summary>tip 第一个参数按十进制解析成玩家编号;缺参数或解析失败返回 0。</summary>
        public static ulong TipPlayerId(TipInfoMessage tip)
        {
            if (tip == null || tip.Parameters.Count == 0) return 0;
            return ulong.TryParse(tip.Parameters[0], NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) ? id : 0;
        }

        // ── 回包 ────────────────────────────────────────────────────────────

        /// <summary>
        /// GetMyTeam 回包:只要收到 TeamResponse 就算探针通过,即使带了内层 tip。
        /// 不带视图(服务端自由读失败 4029 / 4030,或视图读失败)时不能据此推断"无队",
        /// 保留本地快照,退避后重拉;这次服务端没走到上线广播 / 归队,NotifyOnline / Regroup 随重拉再发一次。
        /// </summary>
        private void OnMyTeam(TeamResponse reply, bool silent, GetMyTeamRequest request)
        {
            HasLoaded = true;
            bool hasView = reply.Team != null;
            TeamSnapshotOrder order = TeamSnapshotOrder.Conflict;
            if (hasView) order = ApplyView(reply.Team);
            else
            {
                RefreshQueued = true;
                _readRetryAt = _clock() + ReadRetryDelaySeconds;
                if (request.NotifyOnline) _notifyOnlineQueued = true;
                if (request.Regroup) _regroupQueued = true;
            }
            if (Ok(reply.ErrorMessage))
            {
                // 探针是静默发送的,Reset 写下的"正在连接组队服务…"要在这里收掉,否则会一直挂在状态栏上。
                if (!silent || Status == ConnectingMessage) Status = string.Empty;
                // 打开面板的无队玩家顺带刷新邀请;没带视图时"无队"只是占位,不作数。
                if (hasView && !HasTeam) InvitesQueued = true;
                // 落地恢复:带视图且不是换队冲突(冲突会排队重拉)。被判旧的视图说明本地已有更新的,同样不旧于发出时刻。
                if (hasView && order != TeamSnapshotOrder.Conflict) CompleteSyncRead();
            }
            else
            {
                Status = DescribeTip(reply.ErrorMessage);   // 静默也写
            }
            ObserveStartIntent(reply);
        }

        /// <summary>
        /// ListMyInvites 回包:成功时整表替换,失败保留旧列表并退避重拉。
        /// 回包是发出后某一刻读的,可能早于在途期间到达的推送:发出后推送来的邀请若回包里没有就补回
        /// (两边都有取到期更晚的一份,重邀会续期),发出后撤回的邀请即使回包里有也不加回。
        /// </summary>
        private void OnMyInvites(ListMyInvitesResponse reply, bool silent)
        {
            if (!Ok(reply.ErrorMessage))
            {
                Status = DescribeTip(reply.ErrorMessage);
                InvitesQueued = true;
                _readRetryAt = _clock() + ReadRetryDelaySeconds;
                return;
            }
            var pushed = new List<TeamInvite>();
            foreach (var invite in _invites)
                if (_pushedInvites.Contains(invite.TeamId)) pushed.Add(invite);
            _invites.Clear();
            float now = _clock();
            foreach (var view in reply.Invites)
            {
                var invite = TeamViewMapper.ToInvite(view, reply.ServerTimeMs, now);
                if (invite != null && !_revokedInvites.Contains(invite.TeamId)) UpsertInvite(invite);
            }
            foreach (var invite in pushed)
            {
                int index = _invites.FindIndex(existing => existing.TeamId == invite.TeamId);
                if (index < 0) _invites.Add(invite);
                else if (invite.ExpiresAt > _invites[index].ExpiresAt) _invites[index] = invite;
            }
            CompleteSyncRead();
            if (!silent) Status = string.Empty;
        }

        /// <summary>
        /// 写操作回包。不论成败,只要带视图就按排序规则应用(§H.3);服务端读取失败时 Team 为 null,
        /// 此时不清本地快照,排队重拉自愈。
        /// </summary>
        private void FinishWrite(TeamAction action, ulong target, TeamResponse reply, string successText)
        {
            // 自己这次开战被「队伍正在匹配中」拒绝、回包视图就是 QUEUED:这是对本次请求的新鲜应答,票据确实在排。
            // 这一次排队若被本机记成了已结束(ConsumeBackfillQueue),那个结论是错的 —— 例如排队中应战切磋开打、
            // 而服务端没能把排队撤掉。撤销它,视图恢复「匹配中」,TeamBackfillQueueSync 会重新采纳;
            // 否则本机既不显示匹配中、也没有取消入口,凑满时被直接拉进副本。
            if (action == TeamAction.StartMatch && reply.Team != null
                && reply.Team.MatchState == TeamBackfillProtocol.QueuedState && IsTip(reply.ErrorMessage, team_error.KTeamInMatch))
            {
                _consumedQueueTeamId = 0;
                _consumedQueueAtMs = 0;
            }
            if (reply.Team != null) ApplyView(reply.Team); else RefreshQueued = true;
            var tip = reply.ErrorMessage;
            bool ok = Ok(tip);
            // 建队幂等(§D.6):重复建队回 4003,但视图里队长就是自己 → 按成功处理。
            if (!ok && action == TeamAction.Create && IsTip(tip, team_error.KTeamMemberInTeam)
                && reply.Team != null && reply.Team.LeaderId == PlayerId)
                ok = true;
            // 重复开战回 4023 且视图已在集合:队伍正在进入战斗,不算错误。
            if (!ok && action == TeamAction.StartMatch && IsTip(tip, team_error.KTeamInMatch)
                && reply.Team?.MatchState == TeamMatchState.Starting)
            {
                Status = "队伍正在进入战斗，请稍候…";
                return;
            }
            // 同理:重复开战回 4023 而视图在补位匹配中,不算错误。
            if (!ok && action == TeamAction.StartMatch && IsTip(tip, team_error.KTeamInMatch)
                && reply.Team?.MatchState == TeamBackfillProtocol.QueuedState)
            {
                Status = "队伍正在匹配中，请稍候…";
                // 回包视图因排序规则没被应用(在途期间先到了更高版本的推送):排一次重拉,不等面板刷新。
                if (!MatchQueued) RefreshQueued = true;
                return;
            }
            if (ok)
            {
                // 以回包为准:请求在途期间有人入队,服务端按它读到的人数判定「目标 = 现有」而直接开战(视图 STARTING),
                // 这时就是直接开战,不说「进入匹配队列」也不做核实。
                bool backfillStart = action == TeamAction.StartMatch && _startExpectsQueue
                    && reply.Team?.MatchState != TeamMatchState.Starting;
                Status = action == TeamAction.StartMatch && MatchQueued
                    ? $"队伍已进入匹配队列（目标 {Snapshot.MatchTargetSize} 人），等待补位…"
                    : backfillStart ? "队伍已进入匹配队列，等待补位…" : successText;
                // 补位入队成功、回包视图却没在排(服务端读票据失败时如实下发):有界核实,拿到在排的视图才能被带入排队。
                if (backfillStart) ExpectQueuedView();
                // 接受或谢绝都会消耗这条邀请。
                if (action == TeamAction.RespondInvite) RemoveInvite(target);
                return;
            }
            Status = DescribeTip(tip);
            if (IsMemberStateTip(tip)) HighlightPlayerId = TipPlayerId(tip);
            if (action == TeamAction.RespondInvite
                && (IsTip(tip, team_error.KTeamInviteNotFound) || IsTip(tip, team_error.KTeamHasNotTeamId)))
                RemoveInvite(target);
            // 其余(4013、4004、InMatch 等)视图已在上面按规则刷新,不需要再做别的。
        }

        private void OnTransportError(string error, TeamAction action, IMessage request, bool probe, bool silent)
        {
            if (!ObserveActionTransportError(action, error)) return;
            if (error != null && error.StartsWith(ServerTipPrefix, StringComparison.Ordinal))
            {
                if (IsRateLimited(error))
                {
                    // gate 限流是对这一包的明确拒绝：回包带原请求 id，由 GameClient 精确匹配，
                    // 请求也没有到达组队服务(照 SocialClient.IsDefiniteRejection)。不停用、不要求重连;
                    // 读请求退避后重排,写请求交给玩家稍后重试。
                    if (action == TeamAction.StartMatch && _pendingStart != null && !_startHadUncertainAttempt)
                    {
                        // 首次尝试在 gate 即明确被拒绝，服务端没有执行；不能把它误当成旧补位协议的未知结果。
                        // 若此前已有不确定尝试，本次限流只证明这一包没执行，原意图仍必须保留。
                        try { ClearStartIntent(); }
                        catch (Exception)
                        {
                            _startRecoveryBlocked = true;
                            Status = "已拒绝的开局记录无法清理，请修复本机存储后重新登录。";
                            return;
                        }
                    }
                    if (action == TeamAction.Refresh) RefreshQueued = true;
                    if (action == TeamAction.ListInvites) InvitesQueued = true;
                    if (request is GetMyTeamRequest refresh)
                    {
                        if (refresh.NotifyOnline) _notifyOnlineQueued = true;
                        if (refresh.Regroup) _regroupQueued = true;
                    }
                    if (action == TeamAction.Refresh || action == TeamAction.ListInvites)
                        _readRetryAt = _clock() + ReadRetryDelaySeconds;
                    if (!silent) Status = TooFastMessage;
                    return;
                }
                // 其余外层信封错误(路由服 / gate 拒绝、非法消息):本连接内不再发 team 消息。
                Suspended = true;
                HasFreshView = false;
                Status = SuspendedMessage;
                return;
            }
            if (probe && error == "rpc timeout")
            {
                // 探针超时:组队服务很可能未上线(灰度),本连接停用,等真实断线后再探。
                Suspended = true;
                RequiresReconnect = true;
                HasFreshView = false;
                Status = SuspendedMessage;
                return;
            }
            // 写请求可能已在服务端生效；重连后按原意图与权威状态恢复，避免重复副作用。
            if (action == TeamAction.StartMatch && _pendingStart != null) _startHadUncertainAttempt = true;
            RequiresReconnect = true;
            HasFreshView = false;
            Status = RecoveryMessage;
        }

        /// <summary>外层信封错误是否为 gate 限流(common_error.KRateLimitExceeded)。</summary>
        private static bool IsRateLimited(string error) =>
            uint.TryParse(error.Substring(ServerTipPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture,
                out uint tip) && tip == (uint)common_error.KRateLimitExceeded;

        // ── 推送 ────────────────────────────────────────────────────────────
        // 三个处理器都不改 Busy / PendingAction / PendingTarget / _generation,也绝不抛异常:
        // 推送是至多一次的,抛出去会打断 gate 的收包循环。推送写下的提示会盖掉正在显示的进度文案,
        // 紧接着到达的回包再写上自己的结果文案。

        /// <summary>213:GameClient 解析后经 OnTeamSnapshot 事件送达。</summary>
        private void HandleSnapshotPush(TeamSnapshotS2C message)
        {
            if (_disposed || Suspended || PlayerId == 0) return;
            if (message?.Team == null) return;
            bool hadTeam = HasTeam, wasLeader = IsLeader;
            var order = ApplyView(message.Team);
            if (order != TeamSnapshotOrder.Accept)
            {
                if (order == TeamSnapshotOrder.Conflict) Changed?.Invoke();
                return;
            }
            switch (message.Reason)
            {
                case TeamChangeReason.Disbanded:
                    if (!HasTeam) Status = "队伍已解散。";
                    break;
                case TeamChangeReason.MemberKicked:
                    if (hadTeam && !HasTeam) Status = "你已被请离队伍。";
                    break;
                case TeamChangeReason.MemberJoined:
                    if (!hadTeam && HasTeam) Status = "已加入队伍。";
                    break;
                case TeamChangeReason.LeaderTransferred:
                case TeamChangeReason.LeaderOfflineTransferred:
                    if (!wasLeader && IsLeader) Status = "你已成为队长。";
                    break;
                case TeamChangeReason.MatchStarted:
                    Status = "队长已发起战斗，正在集合…";
                    break;
                case TeamChangeReason.MatchFailed:
                {
                    string text = DescribeTip(message.Tip);
                    Status = string.IsNullOrEmpty(text) ? "队伍开战失败，请稍后再试。" : text;
                    HighlightPlayerId = IsMemberStateTip(message.Tip) ? TipPlayerId(message.Tip) : 0;
                    break;
                }
                case TeamChangeReason.MatchEnded:
                    // 战斗界面由 BattleStartS2C 驱动,这里只收掉集合提示。
                    Status = string.Empty;
                    HighlightPlayerId = 0;
                    break;
                case TeamBackfillProtocol.MatchQueuedReason:
                    Status = MatchQueued
                        ? $"队长已发起队伍匹配（目标 {Snapshot.MatchTargetSize} 人），等待补位…"
                        : "队长已发起队伍匹配，等待补位…";
                    HighlightPlayerId = 0;
                    BackfillNotice?.Invoke(Status, false);
                    // 推送里的视图没在排(服务端读票据失败时如实下发):有界核实,拿到在排的视图才能被带入排队。
                    ExpectQueuedView();
                    break;
                case TeamBackfillProtocol.MatchQueueCanceledReason:
                {
                    // 主动取消带取消者(actor_id);系统撤单带原因(tip,参数里可能是出问题的队员)。
                    string text = DescribeTip(message.Tip);
                    if (!string.IsNullOrEmpty(text)) Status = "队伍匹配已取消：" + text;
                    else if (message.ActorId != 0 && message.ActorId == PlayerId) Status = "已取消队伍匹配。";
                    else if (message.ActorId != 0) Status = MemberName(message.ActorId) + " 取消了队伍匹配。";
                    else Status = "队伍匹配已取消。";
                    HighlightPlayerId = IsMemberStateTip(message.Tip) ? TipPlayerId(message.Tip) : 0;
                    BackfillNotice?.Invoke(Status, true);
                    break;
                }
            }
            Changed?.Invoke();
        }

        /// <summary>215:收到组队邀请。同一队伍的邀请按 TeamId 覆盖。</summary>
        private void HandleTeamInvite(MessageContent content)
        {
            if (_disposed || Suspended || PlayerId == 0 || content == null) return;
            TeamInviteS2C message;
            // 坏包只能丢:推送是至多一次的，没有重投，抛出去会打断 gate 的收包循环。
            try { message = TeamInviteS2C.Parser.ParseFrom(content.SerializedMessage); }
            catch (InvalidProtocolBufferException) { return; }
            var invite = TeamViewMapper.ToInvite(message.Invite, message.ServerTimeMs, _clock());
            if (invite == null) return;
            UpsertInvite(invite);
            _pushedInvites.Add(invite.TeamId);
            _revokedInvites.Remove(invite.TeamId);
            Status = TeamViewMapper.DisplayName(invite.Inviter) + " 邀请你加入队伍。";
            Changed?.Invoke();
        }

        /// <summary>203:申请被拒 / 邀请被撤回。其他类型忽略。</summary>
        private void HandleTeamEvent(MessageContent content)
        {
            if (_disposed || Suspended || PlayerId == 0 || content == null) return;
            TeamEventS2C message;
            try { message = TeamEventS2C.Parser.ParseFrom(content.SerializedMessage); }
            catch (InvalidProtocolBufferException) { return; }
            switch (message.Type)
            {
                case TeamEventType.ApplicationRejected:
                    Status = "对方拒绝了你的入队申请。";
                    Changed?.Invoke();
                    break;
                case TeamEventType.InviteRevoked:
                    // 不论本地有没有这条都记下:在途的 ListMyInvites 回包可能把它带回来。
                    _revokedInvites.Add(message.TeamId);
                    _pushedInvites.Remove(message.TeamId);
                    if (RemoveInvite(message.TeamId))
                    {
                        Status = "一条组队邀请已失效。";
                        Changed?.Invoke();
                    }
                    break;
            }
        }

        // ── 内部 ────────────────────────────────────────────────────────────

        /// <summary>按 §H.3 排序规则应用视图;返回排序结果。推送与回包共用。</summary>
        private TeamSnapshotOrder ApplyView(TeamView view)
        {
            bool hadTeam = HasTeam;
            var incoming = TeamViewMapper.ToSnapshot(view, PlayerId);
            var order = TeamViewMapper.Compare(_hasView ? Snapshot : null, incoming);
            StripConsumedQueue(incoming);
            if (order == TeamSnapshotOrder.Accept) { Snapshot = incoming; _hasView = true; HasFreshView = true; ViewSerial++; }
            else if (order == TeamSnapshotOrder.Conflict)
            {
                HasFreshView = false;
                RefreshQueued = true;   // epoch 相等但 TeamId 不同:丢弃并尽快重拉
            }
            if (hadTeam && !HasTeam) InvitesQueued = true;                         // 刚离队 / 被踢 / 解散:重拉邀请
            return order;
        }

        /// <summary>本次读取已成功应用:交回发出时取的确认委托(至多一次)。</summary>
        private void CompleteSyncRead()
        {
            var ack = _syncReadAck;
            _syncReadAck = null;
            ack?.Invoke();
        }

        private static bool Ok(TipInfoMessage tip) => tip == null || tip.Id == 0;

        private static bool IsTip(TipInfoMessage tip, team_error code) => tip != null && tip.Id == (uint)code;

        /// <summary>4024 / 4025 / 4026:参数里带着出问题的队员编号。</summary>
        private static bool IsMemberStateTip(TipInfoMessage tip) =>
            IsTip(tip, team_error.KTeamMemberOffline) || IsTip(tip, team_error.KTeamMemberInBattle)
            || IsTip(tip, team_error.KTeamMemberNotReady);

        /// <summary>
        /// 记下「这一次补位排队已经结束」。调用方(TeamBackfillQueueSync)是从排队票据那一侧得知的:
        /// 轮询到已不在排、或已经开战 —— 票据才是「是否在排」的事实来源,队伍视图里的匹配态只是提示,
        /// 而且不带版本号:服务端在凑满开战时不会补推视图,迟到的旧视图也可能再把它带回来。
        /// 之后凡是同一次排队(同一队伍 + 同一入队时刻)的视图,匹配态一律按「没在排」处理;
        /// 队长重新发起匹配会有新的入队时刻,不受影响。同时排一次静默的 GetMyTeam,成败都不影响这里的结论。
        /// </summary>
        public void ConsumeBackfillQueue(ulong teamId, ulong queuedAtMs)
        {
            if (_disposed || teamId == 0 || queuedAtMs == 0) return;
            _consumedQueueTeamId = teamId;
            _consumedQueueAtMs = queuedAtMs;
            RefreshQueued = true;
            if (!Snapshot.MatchQueued || Snapshot.TeamId != teamId || Snapshot.MatchQueuedAtMs != queuedAtMs) return;
            StripConsumedQueue(Snapshot);
            ViewSerial++;
            Changed?.Invoke();
        }

        /// <summary>
        /// 撤销 <see cref="ConsumeBackfillQueue"/> 记下的「这一次排队已结束」(只在身份对得上时),并排一次静默刷新:
        /// 服务端的排队票据说那一次排队其实还在(TeamBackfillQueueSync 规则 2 的核实)。刷新回来的视图由规则 1 重新采纳。
        /// </summary>
        public void ReviveBackfillQueue(ulong teamId, ulong queuedAtMs)
        {
            if (_disposed || teamId == 0 || queuedAtMs == 0) return;
            if (_consumedQueueTeamId != teamId || _consumedQueueAtMs != queuedAtMs) return;
            _consumedQueueTeamId = 0;
            _consumedQueueAtMs = 0;
            RefreshQueued = true;
        }

        /// <summary>
        /// 我们知道全队应当在排(收到「队长发起了匹配」、自己的补位入队成功了),但手里的视图没在排:
        /// 服务端读排队票据失败时会如实下发「没在排」,并在随后约 5 秒内不再读。没有在排的视图就不会被带入排队,
        /// 也就没有排队面板和取消入口 —— 所以隔 <see cref="QueueViewVerifyDelaySeconds"/> 秒静默重拉,
        /// 视图在排或次数用完就停。
        /// </summary>
        private void ExpectQueuedView()
        {
            if (MatchQueued) return;
            _queueViewVerifyLeft = QueueViewVerifyAttempts;
            _queueViewVerifyAt = _clock() + QueueViewVerifyDelaySeconds;
        }

        /// <summary>排一次静默的 GetMyTeam(下一次 DrainQueued 发出;发不出去也不报错)。</summary>
        public void RequestRefresh()
        {
            if (!_disposed) RefreshQueued = true;
        }

        /// <summary>
        /// 请求归队:排一次带 regroup 的静默 GetMyTeam(下一次 DrainQueued 发出,与其他排队的拉取合并成一条)。
        /// 服务端只在自己是队员(不是队长)时处理:与队长同一场景实例、且看不见或离得远就瞬移到队长脚下,
        /// 队长在本节点另一个场景就换过去。限流 / 回包不带视图时随重拉再带一次;不在队里时是空操作。
        /// </summary>
        public void RequestRegroup()
        {
            // 仅限制新的归队意图；已发送请求因限流或缺视图而排队的恢复仍沿用原读重试。
            if (_disposed || !HasFreshView || !ServiceAvailable || !HasTeam || IsLeader) return;
            _regroupQueued = true;
            RefreshQueued = true;
        }

        /// <summary>有一次归队请求排着还没发出(测试与诊断用)。</summary>
        public bool RegroupQueued => _regroupQueued;

        /// <summary>snapshot 若仍标着已结束的那一次排队,把匹配态抹掉。</summary>
        private void StripConsumedQueue(TeamSnapshot snapshot)
        {
            if (_consumedQueueAtMs == 0 || !snapshot.MatchQueued) return;
            if (snapshot.TeamId != _consumedQueueTeamId || snapshot.MatchQueuedAtMs != _consumedQueueAtMs) return;
            snapshot.MatchQueued = false;
            snapshot.MatchTargetSize = 0;
            snapshot.MatchQueuedAtMs = 0;
            snapshot.MatchBattleConfigId = 0;
        }

        /// <summary>队员的显示名;已不在队里时退回泛称。</summary>
        private string MemberName(ulong playerId)
        {
            foreach (var member in Snapshot.Members)
                if (member != null && member.PlayerId == playerId) return TeamViewMapper.DisplayName(member);
            return "队员";
        }

        private bool IsMember(ulong playerId)
        {
            foreach (var member in Snapshot.Members)
                if (member != null && member.PlayerId == playerId) return true;
            return false;
        }

        private void UpsertInvite(TeamInvite invite)
        {
            int index = _invites.FindIndex(existing => existing.TeamId == invite.TeamId);
            if (index >= 0) _invites[index] = invite;
            else _invites.Add(invite);
        }

        private bool RemoveInvite(ulong teamId) => _invites.RemoveAll(invite => invite.TeamId == teamId) > 0;

        private void Reject(string message) { Status = message; Changed?.Invoke(); }

        // 等价于 now - _lastSendAt < MinSendIntervalSeconds。写成"下次可发时刻"是为了让
        // 恰好相隔 0.4 秒的两次发送不被 float 减法的舍入误判为过快(例如 1.4f - 1f 略小于 0.4f)。
        private bool TooSoon(float now) => now < _lastSendAt + MinSendIntervalSeconds;

        /// <summary>
        /// 动作自身的本地前置条件(A.6);返回 null 表示通过,否则是拒绝文案。
        /// 审批 / 应答的同意与否、开战的副本编号都从已构造好的请求体里读,保持 Send 的单一入口。
        /// 离线目标不在本地拦截:转让给离线成员由界面置灰,服务端会回 4024 复核。
        /// </summary>
        private string Precondition(TeamAction action, ulong target, IMessage request)
        {
            switch (action)
            {
                case TeamAction.Create:
                    return HasTeam ? "你已在队伍中。" : null;
                case TeamAction.Apply:
                    if (target == 0 || target == PlayerId) return InvalidTargetMessage;
                    return HasTeam ? "你已在队伍中，无法申请加入其他队伍。" : null;
                case TeamAction.Decide:
                    if (!IsLeader) return "只有队长可以处理申请。";
                    if (target == 0) return InvalidTargetMessage;
                    return request is HandleApplicationRequest decide && decide.Approve && MatchStarting
                        ? MatchLockedMessage : null;
                case TeamAction.Invite:
                    if (!HasTeam) return "请先创建队伍。";
                    if (!IsLeader) return "只有队长可以邀请道友。";
                    if (target == 0 || target == PlayerId) return InvalidTargetMessage;
                    return IsMember(target) ? "对方已在队伍中。" : null;
                case TeamAction.RespondInvite:
                    if (target == 0) return "邀请已失效。";
                    return request is RespondInviteRequest respond && respond.Accept && HasTeam ? "你已在队伍中。" : null;
                case TeamAction.Leave:
                    if (!HasTeam) return "你当前不在队伍中。";
                    return MatchStarting ? MatchLockedMessage : null;
                case TeamAction.Kick:
                    if (!IsLeader) return "只有队长可以请离队员。";
                    if (target == PlayerId) return "不能请离自己。";
                    if (!IsMember(target)) return NotMemberMessage;
                    return MatchStarting ? MatchLockedMessage : null;
                case TeamAction.Transfer:
                    if (!IsLeader) return "只有队长可以转让队长。";
                    if (target == PlayerId) return "不能转让给自己。";
                    if (!IsMember(target)) return NotMemberMessage;
                    return MatchStarting ? MatchLockedMessage : null;
                case TeamAction.Disband:
                    if (!IsLeader) return "只有队长可以解散队伍。";
                    return MatchStarting ? MatchLockedMessage : null;
                case TeamAction.StartMatch:
                    if (!IsLeader) return "只有队长可以开始战斗。";
                    if (!(request is StartTeamMatchRequest start) || start.BattleConfigId == 0) return "该副本未开放组队。";
                    if (MatchQueued) return MatchQueuedMessage;
                    if (start.TargetTeamSize > Snapshot.Members.Count && !Snapshot.MatchFillSupported)
                        return "当前服务器未开放队伍补位匹配。";
                    return MatchStarting ? MatchLockedMessage : null;
                default:
                    return null;   // Refresh / ListInvites 无前置条件
            }
        }

        /// <summary>发出时写入 Status 的进度文案(A.6)。</summary>
        private static string ProgressText(TeamAction action, IMessage request)
        {
            switch (action)
            {
                case TeamAction.Refresh: return "正在同步队伍…";
                case TeamAction.ListInvites: return "正在读取邀请…";
                case TeamAction.Create: return "正在创建队伍…";
                case TeamAction.Apply: return "正在提交入队申请…";
                case TeamAction.Decide:
                    return request is HandleApplicationRequest decide && decide.Approve ? "正在同意申请…" : "正在拒绝申请…";
                case TeamAction.Invite: return "正在发送邀请…";
                case TeamAction.RespondInvite:
                    return request is RespondInviteRequest respond && respond.Accept ? "正在加入队伍…" : "正在谢绝邀请…";
                case TeamAction.Leave: return "正在离开队伍…";
                case TeamAction.Kick: return "正在请离队员…";
                case TeamAction.Transfer: return "正在转让队长…";
                case TeamAction.Disband: return "正在解散队伍…";
                case TeamAction.StartMatch: return "正在发起队伍战斗…";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// 所有请求的唯一发送入口。关卡依次为:已释放 → 连接变化 → 停用 → 待重连 → 未进游戏 →
        /// 探针未回(灰度保护:探针回来之前除了探针本身什么都不发)→ 单飞 → 发送间隔 → 同 id 发送窗口 → 动作前置条件。
        /// silent 为 true 时不写 Status(DrainQueued 的后台读)。
        /// </summary>
        private bool Send<T>(TeamAction action, ulong target, uint messageId, IMessage request,
                             MessageParser<T> parser, bool silent, Action<T> onReply,Action beforeSend=null) where T : IMessage<T>
        {
            if (_disposed) return false;
            ObserveConnection();
            if (Suspended) { if (!silent) Reject(SuspendedMessage); return false; }
            if (RequiresReconnect) { if (!silent) Reject(RecoveryMessage); return false; }
            if (!_net.IsReady || PlayerId == 0) { if (!silent) Reject(NotInGameMessage); return false; }
            if (!HasLoaded && action != TeamAction.Refresh) { if (!silent) Reject(ConnectingMessage); return false; }
            bool read = action == TeamAction.Refresh || action == TeamAction.ListInvites;
            RestoreActionIntent();
            bool replay = false;
            if (!read && action != TeamAction.StartMatch && !BindActionIntent(action, target, messageId, ref request, out replay)) return false;
            if (!read && action == TeamAction.StartMatch && (_pendingActionRequest != null || _actionRecoveryBlocked))
            { Reject("有待确认的组队操作，请先恢复原请求。"); return false; }
            float now = _clock();
            if (Busy || TooSoon(now) || !HasQuota(messageId, read, now))
            {
                // 后台读合并成排队标志,由 DrainQueued 在条件满足时补发;用户写操作直接拒绝。
                if (action == TeamAction.Refresh) RefreshQueued = true;
                else if (action == TeamAction.ListInvites) InvitesQueued = true;
                else if (!silent) Reject(Busy ? BusyMessage : TooFastMessage);
                return false;
            }
            if (!read && !replay)
            {
                string rejection = Precondition(action, target, request);
                if (rejection != null) { if (!silent) Reject(rejection); return false; }
            }

            try {
                if (!read && action != TeamAction.StartMatch) SaveActionIntent(action, target, messageId, request);
                beforeSend?.Invoke();
            }
            catch(Exception){Reject("无法保存组队原请求，尚未发送。");return false;}

            // 落地恢复:读取到这里一定会发出,此刻取确认委托;写操作清掉上一次读取残留的委托。
            _syncReadAck = read ? SyncReadStarted?.Invoke(action) : null;
            int generation = ++_generation;
            ulong player = PlayerId;
            bool probe = !HasLoaded;
            // ApplyView 接受权威视图时总是替换对象；本地匹配态修正只改原对象。
            // 缺视图的旧回包不能把请求在途期间已收到的新推送误标为过期。
            var snapshotAtSend = Snapshot;
            Busy = true; PendingAction = action; PendingTarget = target;
            _lastSendAt = now;
            RecordSend(messageId, read, now);
            if (action == TeamAction.Refresh)
            {
                _lastRefreshAt = _lastSendAt;
                RefreshQueued = false;
                _notifyOnlineQueued = false;
                _regroupQueued = false;
            }
            if (action == TeamAction.ListInvites)
            {
                InvitesQueued = false;
                // 从这一刻起的推送 / 撤回才可能晚于回包里的读。
                _pushedInvites.Clear();
                _revokedInvites.Clear();
            }
            if (!silent) { Status = ProgressText(action, request); HighlightPlayerId = 0; }
            Changed?.Invoke();
            // Call 可能同步回调(GameClientBattleTransport 在协程宿主未就绪时同步 onError,测试替身也可能),
            // 下面的写法对同步回调同样安全,依然返回 true。
            // 一个请求只结算一次:GameClient.Call 把 onResp 包在 try/catch 里,回包处理或 Changed 的订阅者
            // (界面渲染)一旦抛异常,它会接着调 onError;回包路径不改 _generation,Current 仍成立,
            // 没有这道闸,已应用的成功回包之后又会被当成传输错误,把本连接的组队停掉。
            bool settled = false;
            _net.Call(messageId, request, parser, reply =>
            {
                if (settled || !Current(generation, player)) return;
                settled = true;
                Busy = false; PendingAction = TeamAction.None; PendingTarget = 0;
                if (reply is TeamResponse teamReply && teamReply.Team == null && ReferenceEquals(Snapshot, snapshotAtSend))
                    HasFreshView = false;
                if (!read && action != TeamAction.StartMatch && reply is TeamResponse actionReply)
                    CompleteActionIntent(actionReply);
                onReply(reply);
                Changed?.Invoke();
            }, error =>
            {
                if (settled || !Current(generation, player)) return;
                settled = true;
                Busy = false; PendingAction = TeamAction.None; PendingTarget = 0;
                OnTransportError(error, action, request, probe, silent);
                Changed?.Invoke();
            });
            return true;
        }

        /// <summary>同一 message_id 在 MessageWindowSeconds 内是否还有配额(读 5 次、写 3 次)。</summary>
        private bool HasQuota(uint messageId, bool read, float now)
        {
            if (!_sentAt.TryGetValue(messageId, out var sent)) return true;
            // 写成"最早一次 + 窗口"而不是减法,理由同 TooSoon。
            return sent.Count < (read ? ReadQuota : WriteQuota) || now >= sent.Peek() + MessageWindowSeconds;
        }

        private void RecordSend(uint messageId, bool read, float now)
        {
            if (!_sentAt.TryGetValue(messageId, out var sent)) _sentAt[messageId] = sent = new Queue<float>();
            sent.Enqueue(now);
            int quota = read ? ReadQuota : WriteQuota;
            while (sent.Count > quota) sent.Dequeue();
        }

        // 单飞保证:_generation 只在发新请求(上一个回调必定已到)或 Reset / Dispose(会话边界)时变,
        // 所以世代对不上的回包一律属于旧会话,整包丢弃,连视图也不应用。
        private bool Current(int generation, ulong player) =>
            !_disposed && generation == _generation && _net.IsReady && PlayerId == player
            && ReferenceEquals(_observedConnection, _connectionIdentity());
    }
}
