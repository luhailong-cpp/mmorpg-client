using System;
using System.Collections.Generic;
using System.Globalization;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using Teampb;

namespace MmorpgClient.Game.Team
{
    /// <summary>
    /// 组队的网络真相。单飞 + 发送间隔 + 会话代次 + 连接身份校验,旧回包不能污染重连或换角;
    /// 快照按 (MembershipEpoch, Version) 排序应用,推送与回包共用同一套规则。
    /// 不依赖 TeamUiState 与 UnityEngine:界面订阅 Changed 后整份镜像本对象的只读状态。
    /// </summary>
    public sealed class TeamClient : IDisposable
    {
        public const string ConnectingMessage = "正在连接组队服务…";
        public const string RecoveryMessage   = "组队请求状态尚未确认，请重新登录角色后再试。";
        public const string SuspendedMessage  = "组队服务暂不可用，请稍后重新登录再试。";
        // 与 robot 的 400ms 一致,只防连点;gate 的限流按 message_id 另算,见 MessageWindowSeconds。
        public const float  MinSendIntervalSeconds      = 0.4f;
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
        private float _lastSendAt;
        private float _lastRefreshAt;
        // 读失败后的退避:此刻之前 DrainQueued 不补发排队的读(探针也算),避免服务端故障时每 0.4 秒打一次。
        private float _readRetryAt;
        // 每个 message_id 最近几次真正发出的时刻。限流按连接算,Reset 不清(同 _lastSendAt)。
        private readonly Dictionary<uint, Queue<float>> _sentAt = new Dictionary<uint, Queue<float>>();
        // ListMyInvites 发出之后收到的邀请推送 / 撤回事件(按 TeamId)。邀请没有排序键,推送走 Kafka、
        // 回包走路由服,两路不保序:更早读的回包整表替换时,据此补回新推送的邀请、剔除已撤回的邀请。
        private readonly HashSet<ulong> _pushedInvites = new HashSet<ulong>();
        private readonly HashSet<ulong> _revokedInvites = new HashSet<ulong>();

        // ── 状态(只读;调用方不得修改返回的对象)──
        public ulong PlayerId => _net.PlayerId;
        /// <summary>永不为 null;无队或未加载时 TeamId == 0。</summary>
        public TeamSnapshot Snapshot { get; private set; }
        /// <summary>永不为 null;收到的邀请。</summary>
        public IReadOnlyList<TeamInvite> Invites { get; }
        /// <summary>本会话探针 GetMyTeam 已收到回包(TeamResponse),即使带了内层 tip。</summary>
        public bool HasLoaded { get; private set; }
        /// <summary>
        /// 本会话已应用过一份权威视图。服务端读失败时 GetMyTeam 只回 tip、不带视图:此时 HasLoaded 为真
        /// 而 HasView 为假,Snapshot 仍是占位的"无队",界面不能据此判定玩家不在队伍里。
        /// </summary>
        public bool HasView => _hasView;
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
        /// <summary>Read-only invitation pacing for the UI; uses the same interval and quota as Send.</summary>
        public bool InvitationCoolingDown
        {
            get
            {
                float now = _clock();
                return TooSoon(now) || !HasQuota(MessageIds.InviteToTeam, false, now);
            }
        }

        public event Action Changed;

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
            Busy = false; PendingAction = TeamAction.None; PendingTarget = 0;
            Snapshot = TeamViewMapper.ToSnapshot(null, PlayerId); _hasView = false; HasLoaded = false;
            _invites.Clear(); _pushedInvites.Clear(); _revokedInvites.Clear(); HighlightPlayerId = 0;
            RefreshQueued = InvitesQueued = _notifyOnlineQueued = true;
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
            if (_disposed) return;
            float now = _clock();
            // 过期邀请即使 Busy 也照样剔除。
            if (_invites.RemoveAll(invite => invite.ExpiresAt <= now) > 0) Changed?.Invoke();
            if (Busy || RequiresReconnect || Suspended || !_net.IsReady || PlayerId == 0) return;
            if (TooSoon(now)) return;
            // 排队的读在退避期内不补发;面板的 30 秒刷新本来就隔得够久,不受退避影响。
            bool retryReady = now >= _readRetryAt;
            if ((RefreshQueued || !HasLoaded) && retryReady) { SendRefresh(silent: true); return; }
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

        public bool StartMatch(uint battleConfigId) =>
            Send(TeamAction.StartMatch, 0, MessageIds.StartTeamMatch,
                new StartTeamMatchRequest { BattleConfigId = battleConfigId, ExpectedTeamId = Snapshot.TeamId },
                TeamResponse.Parser, false,
                reply => FinishWrite(TeamAction.StartMatch, 0, reply, "已发起队伍战斗，正在集合队员…"));

        // 请求体在调用 Send 之前构造:NotifyOnline 取发送当下的排队值,Send 真正发出后才清零;
        // 服务端没走到上线广播(限流、回包不带视图)时,由回包 / 错误处理按请求体重新置位。
        private bool SendRefresh(bool silent)
        {
            var request = new GetMyTeamRequest { NotifyOnline = _notifyOnlineQueued };
            return Send(TeamAction.Refresh, 0, MessageIds.GetMyTeam, request,
                TeamResponse.Parser, silent, reply => OnMyTeam(reply, silent, request.NotifyOnline));
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
        /// 保留本地快照,退避后重拉;这次服务端没走到上线广播,NotifyOnline 随重拉再发一次。
        /// </summary>
        private void OnMyTeam(TeamResponse reply, bool silent, bool notifyOnline)
        {
            HasLoaded = true;
            bool hasView = reply.Team != null;
            if (hasView) ApplyView(reply.Team);
            else
            {
                RefreshQueued = true;
                _readRetryAt = _clock() + ReadRetryDelaySeconds;
                if (notifyOnline) _notifyOnlineQueued = true;
            }
            if (Ok(reply.ErrorMessage))
            {
                // 探针是静默发送的,Reset 写下的"正在连接组队服务…"要在这里收掉,否则会一直挂在状态栏上。
                if (!silent || Status == ConnectingMessage) Status = string.Empty;
                // 打开面板的无队玩家顺带刷新邀请;没带视图时"无队"只是占位,不作数。
                if (hasView && !HasTeam) InvitesQueued = true;
            }
            else
            {
                Status = DescribeTip(reply.ErrorMessage);   // 静默也写
            }
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
            if (!silent) Status = string.Empty;
        }

        /// <summary>
        /// 写操作回包。不论成败,只要带视图就按排序规则应用(§H.3);服务端读取失败时 Team 为 null,
        /// 此时不清本地快照,排队重拉自愈。
        /// </summary>
        private void FinishWrite(TeamAction action, ulong target, TeamResponse reply, string successText)
        {
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
            if (ok)
            {
                Status = successText;
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
            if (error != null && error.StartsWith(ServerTipPrefix, StringComparison.Ordinal))
            {
                if (IsRateLimited(error))
                {
                    // gate 限流是对这一包的明确拒绝:回包带原请求 id,走 GameClient 的精确匹配,不会打乱 FIFO,
                    // 请求也没有到达组队服务(照 SocialClient.IsDefiniteRejection)。不停用、不要求重连;
                    // 读请求退避后重排,写请求交给玩家稍后重试。
                    if (action == TeamAction.Refresh) RefreshQueued = true;
                    if (action == TeamAction.ListInvites) InvitesQueued = true;
                    if (request is GetMyTeamRequest refresh && refresh.NotifyOnline) _notifyOnlineQueued = true;
                    if (action == TeamAction.Refresh || action == TeamAction.ListInvites)
                        _readRetryAt = _clock() + ReadRetryDelaySeconds;
                    if (!silent) Status = TooFastMessage;
                    return;
                }
                // 其余外层信封错误(路由服 / gate 拒绝、非法消息):本连接内不再发 team 消息。
                Suspended = true;
                Status = SuspendedMessage;
                return;
            }
            if (probe && error == "rpc timeout")
            {
                // 探针超时:组队服务很可能未上线(灰度),本连接停用,等真实断线后再探。
                Suspended = true;
                RequiresReconnect = true;
                Status = SuspendedMessage;
                return;
            }
            // GameClient 的 gRPC FIFO 无法区分超时旧响应，必须等真实断线后再发组队请求。
            RequiresReconnect = true;
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
            if (order == TeamSnapshotOrder.Accept) { Snapshot = incoming; _hasView = true; }
            else if (order == TeamSnapshotOrder.Conflict) RefreshQueued = true;   // epoch 相等但 TeamId 不同:丢弃并尽快重拉
            if (hadTeam && !HasTeam) InvitesQueued = true;                         // 刚离队 / 被踢 / 解散:重拉邀请
            return order;
        }

        private static bool Ok(TipInfoMessage tip) => tip == null || tip.Id == 0;

        private static bool IsTip(TipInfoMessage tip, team_error code) => tip != null && tip.Id == (uint)code;

        /// <summary>4024 / 4025 / 4026:参数里带着出问题的队员编号。</summary>
        private static bool IsMemberStateTip(TipInfoMessage tip) =>
            IsTip(tip, team_error.KTeamMemberOffline) || IsTip(tip, team_error.KTeamMemberInBattle)
            || IsTip(tip, team_error.KTeamMemberNotReady);

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
                             MessageParser<T> parser, bool silent, Action<T> onReply) where T : IMessage<T>
        {
            if (_disposed) return false;
            ObserveConnection();
            if (Suspended) { if (!silent) Reject(SuspendedMessage); return false; }
            if (RequiresReconnect) { if (!silent) Reject(RecoveryMessage); return false; }
            if (!_net.IsReady || PlayerId == 0) { if (!silent) Reject(NotInGameMessage); return false; }
            if (!HasLoaded && action != TeamAction.Refresh) { if (!silent) Reject(ConnectingMessage); return false; }
            bool read = action == TeamAction.Refresh || action == TeamAction.ListInvites;
            float now = _clock();
            if (Busy || TooSoon(now) || !HasQuota(messageId, read, now))
            {
                // 后台读合并成排队标志,由 DrainQueued 在条件满足时补发;用户写操作直接拒绝。
                if (action == TeamAction.Refresh) RefreshQueued = true;
                else if (action == TeamAction.ListInvites) InvitesQueued = true;
                else if (!silent) Reject(Busy ? BusyMessage : TooFastMessage);
                return false;
            }
            if (!read)
            {
                string rejection = Precondition(action, target, request);
                if (rejection != null) { if (!silent) Reject(rejection); return false; }
            }

            int generation = ++_generation;
            ulong player = PlayerId;
            bool probe = !HasLoaded;
            Busy = true; PendingAction = action; PendingTarget = target;
            _lastSendAt = now;
            RecordSend(messageId, read, now);
            if (action == TeamAction.Refresh)
            {
                _lastRefreshAt = _lastSendAt;
                RefreshQueued = false;
                _notifyOnlineQueued = false;
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
