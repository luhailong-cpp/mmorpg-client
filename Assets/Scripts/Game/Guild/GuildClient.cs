using System;
using System.Collections.Generic;
using System.Text;
using Google.Protobuf;
using Guildpb;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;

namespace MmorpgClient.Game.Guild
{
    /// <summary>与 go/guild constants.Rank 同表:0 成员 / 1 长老 / 3 帮主;2 与其它值为 RankNone。服务端仍以 MySQL 复核。</summary>
    public static class GuildRoles
    {
        public const uint Member = 0, Officer = 1, Leader = 3;
        public const int RankNone = 0, RankMember = 1, RankOfficer = 2, RankLeader = 3;
        public static int Rank(uint role) => role switch { Member => RankMember, Officer => RankOfficer, Leader => RankLeader, _ => RankNone };
        public static bool CanKick(uint actor, uint target) => Rank(actor) >= RankOfficer && Rank(target) != RankNone && Rank(actor) > Rank(target);
    }

    /// <summary>帮会权威快照。每次请求绑定角色与会话代次，旧回包不能污染重连或换角。</summary>
    public sealed class GuildClient : IDisposable
    {
        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private object _observedConnection;
        private int _generation;
        private bool _disposed;
        // 推送带来的提示(被请离 / 解散)。推送到达时界面正忙于别的文案，Request 的
        // “正在读取帮会…”与 NotInGuild 的默认文案都会盖掉它，所以先存起来，等 Refresh
        // 回包落到最终 Status 上，且只用一次。B6 的写操作提示另起 _writeNotice(90 清单 X-06)。
        private string _pendingNotice;
        public ulong PlayerId => _net.PlayerId;
        public GuildInfo Info { get; private set; }
        public GetGuildRankResponse Rank { get; private set; }
        public bool HasLoaded { get; private set; }
        public bool Busy { get; private set; }
        public bool RequiresReconnect { get; private set; }
        public const string RecoveryMessage = "帮会请求状态尚未确认，请重新登录角色后再试。";
        // 与服务端 go/guild/internal/constants 同值；服务端仍会自行校验。
        public const int MaxNameLength = 24;
        // 公告按 UTF-8 字节限长：Gate 单包上限 1KB，按字数给到 500 个汉字的包会在 Gate 被丢弃。
        public const int MaxAnnouncementBytes = 600;
        public const int MaxAnnouncementChars = 200;
        public string Status { get; private set; } = "请刷新帮会信息";

        /// <summary>本人提交的待审入帮申请;null = 未加载(区别于“加载过但没有申请”的空列表)。</summary>
        public IReadOnlyList<GuildApplicationView> MyApplications { get; private set; }
        /// <summary>本帮待审申请人(仅长老 / 帮主可拉);null = 未加载。</summary>
        public IReadOnlyList<GuildApplicantView> Applicants { get; private set; }
        /// <summary>推送要求重拉 GetPlayerGuild;由界面每帧调用的 DrainQueued 消费。</summary>
        public bool RefreshQueued { get; private set; }
        /// <summary>推送要求重拉申请列表(或在列表不可见时重拉帮会以刷新角标)。</summary>
        public bool ApplicantsQueued { get; private set; }
        /// <summary>推送 / 退帮要求重拉本人申请列表。</summary>
        public bool MyApplicationsQueued { get; private set; }

        public bool IsLeader => Info != null && Info.LeaderId == PlayerId;
        public uint Role => FindMember(PlayerId)?.Role ?? GuildRoles.Member;
        // 对齐服务端 UpdateAnnouncementAuthorized：长老(1)和帮主(3)，不放行未实现的副帮主(2)。
        public bool CanEditAnnouncement => Info != null && GuildRoles.Rank(Role) >= GuildRoles.RankOfficer;
        public bool IsOfficerOrLeader => Info != null && GuildRoles.Rank(Role) >= GuildRoles.RankOfficer;
        public bool CanAssignRoles => Info != null && GuildRoles.Rank(Role) == GuildRoles.RankLeader;
        /// <summary>能否请离该成员:本地先判一次只为收起按钮，服务端仍会按 MySQL 里的职位复核。</summary>
        public bool CanKick(GuildMember target) =>
            Info != null && target != null && target.PlayerId != PlayerId && GuildRoles.CanKick(Role, target.Role);
        /// <summary>本人是否已向该帮会提交过待审申请(MyApplications 未加载时恒为 false)。</summary>
        public bool HasApplied(ulong guildId)
        {
            if (MyApplications != null)
                foreach (var application in MyApplications)
                    if (application.GuildId == guildId) return true;
            return false;
        }
        public event Action Changed;

        public GuildClient(IBattleTransport net, Func<object> connectionIdentity = null)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _connectionIdentity = connectionIdentity ?? (() => _net);
            _observedConnection = _connectionIdentity();
            _net.Disconnected += HandleDisconnected;
            // 一个 message id 只有一个处理器(GameClient.OnNotify 覆盖写)，帮会推送只在这里注册。
            _net.RegisterNotify(MessageIds.NotifyGuildChanged, HandleGuildChanged);
        }

        public void Reset()
        {
            // 零编号 gRPC 回包无法相关到已取消请求；同连接不得重用其槽位。
            if (Busy) RequiresReconnect = true;
            ++_generation;
            Info = null; Rank = null; HasLoaded = false; Busy = false;
            MyApplications = null; Applicants = null;
            RefreshQueued = ApplicantsQueued = MyApplicationsQueued = false; _pendingNotice = null;
            Status = RequiresReconnect ? RecoveryMessage : "请刷新帮会信息"; Changed?.Invoke();
        }

        public void ObserveConnection()
        {
            object current = _connectionIdentity();
            if (ReferenceEquals(current, _observedConnection)) return;
            // GameClient 静默换 Gate 不一定发 Disconnected；真实连接对象变化才解除隔离。
            _observedConnection = current;
            Busy = false;
            RequiresReconnect = false;
            Reset();
        }
        private void HandleDisconnected()
        {
            // 仅真实连接断开时，旧连接回包才不可能再进入下一连接。
            _observedConnection = _connectionIdentity();
            Busy = false;
            RequiresReconnect = false;
            Reset();
        }

        // ── 推送 ────────────────────────────────────────────────────────────
        // 推送只说“变了”，不改 Info:载荷里没有快照，拉取结果才是真相。

        private void HandleGuildChanged(MessageContent content)
        {
            if (_disposed || content == null) return;
            GuildChangedS2C change;
            // 坏包只能丢:推送是至多一次的，没有重投，抛出去会打断 gate 的收包循环。
            try { change = GuildChangedS2C.Parser.ParseFrom(content.SerializedMessage); }
            catch (InvalidProtocolBufferException) { return; }
            bool mine = Info != null && change.GuildId == Info.GuildId;
            bool aboutMe = change.TargetPlayerId == PlayerId;
            if (mine)
            {
                if (change.Kind == GuildChangeKind.ApplicationReceived) ApplicantsQueued = true;
                else RefreshQueued = true;
                if (change.Kind == GuildChangeKind.Disbanded) _pendingNotice = "帮会已被帮主解散。";
                else if (change.Kind == GuildChangeKind.MemberKicked && aboutMe) _pendingNotice = "你已被请离帮会。";
                if (_pendingNotice != null) Status = _pendingNotice; // 立即可见;Refresh 回包后再次落定
            }
            else if (Info == null && aboutMe && change.Kind == GuildChangeKind.MemberJoined)
            { RefreshQueued = true; Status = "入帮申请已通过，正在读取帮会信息…"; }
            else if (Info == null && aboutMe && change.Kind == GuildChangeKind.ApplicationRejected)
            { MyApplicationsQueued = true; Status = "有一份入帮申请未获通过。"; }
            else return;
            Changed?.Invoke();
        }

        /// <summary>由界面每帧调用;一次只发一个排队请求,Busy / 隔离 / 未就绪时什么也不做。</summary>
        public void DrainQueued(bool applicantsVisible)
        {
            if (_disposed || Busy || RequiresReconnect || !_net.IsReady) return;
            if (RefreshQueued) { Refresh(); return; }
            if (ApplicantsQueued && IsOfficerOrLeader)
            {
                ApplicantsQueued = false;
                // 申请视图可见 → 重拉列表;不可见 → 重拉 GetPlayerGuild,服务端重算 pending_application_count,成员页角标随之更新。
                if (applicantsVisible) LoadApplications(); else Refresh();
                return;
            }
            if (MyApplicationsQueued) { MyApplicationsQueued = false; LoadMyApplications(); }
        }

        // ── 读取 ────────────────────────────────────────────────────────────

        public void Refresh()
        {
            // Request 只在真正发出时 ++_generation:被 Busy / 隔离 / 未就绪挡掉则代次不变,
            // 此时要把排队标志放回去，交给下一帧的 DrainQueued 重试。不能用 Busy 判断——
            // 同步回调的替身(GuildUiVerification)在 Request 返回前就已把 Busy 清掉了。
            int before = _generation;
            bool queued = RefreshQueued;
            RefreshQueued = false;
            Request(MessageIds.GetPlayerGuild, new GetPlayerGuildRequest { PlayerId = PlayerId },
                GetPlayerGuildResponse.Parser, response =>
                {
                    if (response.ErrorMessage?.Id == (uint)guild_error.KGuildNotInGuild)
                    {
                        Info = null; Applicants = null; HasLoaded = true;
                        Status = _pendingNotice ?? "尚未加入帮会，和同道相聚于此。"; _pendingNotice = null;
                        // 入帮时已把本地列表清空;退帮 / 被踢 / 解散后由 DrainQueued 重拉。
                        if (MyApplications == null) MyApplicationsQueued = true;
                        return;
                    }
                    if (!Accept(response.ErrorMessage)) return;
                    Apply(response.Guild);
                    // 帮会仍在,说明“被请离 / 已解散”的提示已经过时,别再落到 Status 上。
                    _pendingNotice = null;
                });
            if (_generation == before) RefreshQueued = queued;
        }

        public void Browse(uint page = 1, uint zoneId = 0)
        {
            // 服务端只返回玩家归属区的榜单，zoneId 仅作请求提示。
            Request(MessageIds.GetGuildRank, new GetGuildRankRequest
                { Page = Math.Max(1, page), PageSize = 5, ZoneId = zoneId },
                GetGuildRankResponse.Parser, response =>
                {
                    if (!Accept(response.ErrorMessage)) return;
                    Rank = response; Status = response.Entries.Count == 0 ? "本区暂无帮会排行。" : "本区帮会排行已更新";
                    // 排行页要按“已申请 / 未申请”换按钮文案;首次打开时顺带排队拉本人申请,
                    // 不在回调里直接连发第二个请求(单请求在途)。
                    if (HasLoaded && Info == null && MyApplications == null) MyApplicationsQueued = true;
                });
        }

        /// <summary>本人的待审入帮申请;未入帮时才有意义。</summary>
        public void LoadMyApplications()
        {
            if (!HasLoaded || Info != null) { Reject("请先读取帮会状态，已入帮时没有待审申请。"); return; }
            Request(MessageIds.ListMyGuildApplications, new ListMyGuildApplicationsRequest(),
                ListMyGuildApplicationsResponse.Parser, response =>
                {
                    if (!AcceptWrite(response.ErrorMessage)) return;
                    // RepeatedField 与响应对象同生命周期,拷一份快照,避免界面持有回包内部集合。
                    MyApplications = new List<GuildApplicationView>(response.Applications);
                });
        }

        /// <summary>本帮待审申请人列表;仅长老 / 帮主可拉。</summary>
        public void LoadApplications() => LoadApplications(keepStatus: false);

        /// <summary>
        /// keepStatus = true 时本次回包**只换数据不换文案**,用于"审批成功 / 失败的提示要在列表刷新之后
        /// 仍留在屏幕上"的场景。
        ///
        /// 为什么不能靠调用顺序解决:Request 在发出时同步写一次 Status("正在读取帮会…"),回包到达时
        /// 再写一次,后者发生在**异步之后** —— 调用方无论把自己的 Status 赋值放在哪一行,都追不上它。
        /// 标志随闭包捕获,不做成实例字段:请求没发出去(权限不足被 Reject)时它跟着闭包一起消失,
        /// 不会泄漏到下一次加载。
        /// </summary>
        private void LoadApplications(bool keepStatus)
        {
            if (!IsOfficerOrLeader) { Reject("仅帮主或长老可查看入帮申请。"); return; }
            Request(MessageIds.ListGuildApplications, new ListGuildApplicationsRequest(),
                ListGuildApplicationsResponse.Parser, response =>
                {
                    if (!AcceptWrite(response.ErrorMessage)) return;
                    Applicants = new List<GuildApplicantView>(response.Applicants);
                    if (keepStatus) return;
                    Status = Applicants.Count == 0 ? "暂无待审入帮申请。" : "待审入帮申请 " + Applicants.Count + " 份";
                });
        }

        // ── 写入 ────────────────────────────────────────────────────────────
        // 写方法成功后直接应用响应里的权威快照,不再额外 Refresh:服务端在提交后
        // 读快照回带(第 2 部分 §5.3),多发一次 GetPlayerGuild 只会多一次等待与闪烁。

        public void Create(string name, uint zoneId)
        {
            name = (name ?? "").Trim();
            if (!CanJoin() || name.Length == 0 || name.Length > MaxNameLength || zoneId == 0)
            { Reject("请先确认未入帮状态，填写 1–24 字帮名并选择有效区服。"); return; }
            Request(MessageIds.CreateGuild, new CreateGuildRequest { PlayerId = PlayerId, Name = name, ZoneId = zoneId },
                CreateGuildResponse.Parser, response =>
                { if (Accept(response.ErrorMessage)) Apply(response.Guild); });
        }

        /// <summary>申请加入帮会(需帮主 / 长老审批)。不叫 Apply:与私有 Apply(GuildInfo) 同名易混。</summary>
        public void ApplyToJoin(ulong guildId)
        {
            if (!CanJoin() || guildId == 0) { Reject("已入帮时不能申请其他帮会。"); return; }
            Request(MessageIds.ApplyJoinGuild, new ApplyJoinGuildRequest { GuildId = guildId },
                ApplyJoinGuildResponse.Parser, response =>
                {
                    if (!AcceptWrite(response.ErrorMessage)) return;
                    // 先发列表请求再写文案:LoadMyApplications 会把 Status 改成“正在读取帮会…”
                    // 且回包不改文案,顺序反了玩家就看不到“申请已提交”。
                    LoadMyApplications();
                    Status = "申请已提交，等待帮主或长老审批。";
                });
        }

        /// <summary>撤回一份尚未审批的入帮申请。</summary>
        public void CancelApplication(ulong guildId)
        {
            if (!HasLoaded || Info != null || guildId == 0) { Reject("请先读取帮会状态，已入帮时不需要撤回申请。"); return; }
            Request(MessageIds.CancelGuildApplication, new CancelGuildApplicationRequest { GuildId = guildId },
                CancelGuildApplicationResponse.Parser, response =>
                {
                    if (!AcceptWrite(response.ErrorMessage))
                    {
                        // 申请已被审批 / 已过期:本地列表必然过时,重拉一次让排行页按钮收敛。
                        if (IsTip(response.ErrorMessage, guild_error.KGuildApplicationNotFound)) ReloadKeepingTip(LoadMyApplications);
                        return;
                    }
                    LoadMyApplications();
                    Status = "已撤回入帮申请。";
                });
        }

        /// <summary>任免长老。role 只允许 0(帮众)/ 1(长老);服务端仍会复核操作者职位与长老上限。</summary>
        public void SetMemberRole(ulong target, uint role)
        {
            if (!CanAssignRoles || target == 0 || target == PlayerId || (role != GuildRoles.Member && role != GuildRoles.Officer))
            { Reject("仅帮主可任免长老。"); return; }
            Request(MessageIds.SetGuildMemberRole, new SetGuildMemberRoleRequest { TargetPlayerId = target, Role = role },
                SetGuildMemberRoleResponse.Parser, response =>
                { if (AcceptWrite(response.ErrorMessage)) Apply(response.Guild); });
        }

        /// <summary>请离成员(长老可请离帮众,帮主可请离长老与帮众)。</summary>
        public void Kick(ulong target)
        {
            GuildMember member = FindMember(target);
            if (member == null || !CanKick(member)) { Reject("当前身份无权请离该成员。"); return; }
            Request(MessageIds.KickGuildMember, new KickGuildMemberRequest { TargetPlayerId = target },
                KickGuildMemberResponse.Parser, response =>
                { if (AcceptWrite(response.ErrorMessage)) Apply(response.Guild); });
        }

        /// <summary>转让帮主。不可撤回:确认框由界面负责,这里只做能否发出的判断。</summary>
        public void TransferLeader(ulong target)
        {
            if (!CanAssignRoles || target == 0 || target == PlayerId || FindMember(target) == null)
            { Reject("仅帮主可转让帮会。"); return; }
            Request(MessageIds.TransferGuildLeader, new TransferGuildLeaderRequest { TargetPlayerId = target },
                TransferGuildLeaderResponse.Parser, response =>
                { if (AcceptWrite(response.ErrorMessage)) Apply(response.Guild); });
        }

        /// <summary>审批入帮申请。通过与拒绝都回带本帮快照,之后重拉申请列表。</summary>
        public void Review(ulong applicant, bool approve)
        {
            if (!IsOfficerOrLeader || applicant == 0) { Reject("仅帮主或长老可审批入帮申请。"); return; }
            Request(MessageIds.ReviewGuildApplication, new ReviewGuildApplicationRequest
                { ApplicantPlayerId = applicant, Approve = approve },
                ReviewGuildApplicationResponse.Parser, response =>
                {
                    if (!AcceptWrite(response.ErrorMessage))
                    {
                        // 申请已失效 / 帮会已满:列表过时,重拉后让界面自己收敛。
                        if (IsTip(response.ErrorMessage, guild_error.KGuildApplicationNotFound)
                            || IsTip(response.ErrorMessage, guild_error.KGuildFull))
                            ReloadKeepingTip(() => LoadApplications(keepStatus: true));
                        return;
                    }
                    Apply(response.Guild);
                    // 先发列表请求再写文案,且让本次回包不改文案 —— 两者缺一不可:
                    // 少了前者,Request 同步写的"正在读取帮会…"会盖掉结果;少了后者,列表回包到达时
                    // 又会写成"待审入帮申请 N 份"。同文件 ApplyToJoin / CancelApplication 只需要前者,
                    // 因为 LoadMyApplications 的回包本来就不写 Status。
                    LoadApplications(keepStatus: true);
                    Status = approve ? "已同意入帮申请。" : "已拒绝入帮申请。";
                });
        }

        public void SaveAnnouncement(string text)
        {
            text = (text ?? "").Trim();
            if (Info == null || !CanEditAnnouncement) { Reject("仅帮主或长老可修改公告。"); return; }
            if (text.Length > MaxAnnouncementChars || Encoding.UTF8.GetByteCount(text) > MaxAnnouncementBytes)
            { Reject("公告最多 " + MaxAnnouncementChars + " 字，请精简后再保存。"); return; }
            Request(MessageIds.SetGuildAnnouncement, new SetAnnouncementRequest
                { GuildId = Info.GuildId, PlayerId = PlayerId, Announcement = text },
                SetAnnouncementResponse.Parser, response =>
                { if (Accept(response.ErrorMessage)) Apply(response.Guild); });
        }

        public void Leave()
        {
            if (Info == null || IsLeader) { Reject("帮主需先处理帮会，不能直接退出。"); return; }
            Request(MessageIds.LeaveGuild, new LeaveGuildRequest { PlayerId = PlayerId },
                LeaveGuildResponse.Parser, response =>
                { if (Accept(response.ErrorMessage)) Refresh(); });
        }

        public void Disband()
        {
            if (Info == null || !IsLeader) { Reject("仅帮主可解散帮会。"); return; }
            Request(MessageIds.DisbandGuild, new DisbandGuildRequest { PlayerId = PlayerId },
                DisbandGuildResponse.Parser, response =>
                { if (Accept(response.ErrorMessage)) Refresh(); });
        }

        // ── 内部 ────────────────────────────────────────────────────────────

        private bool CanJoin() => HasLoaded && Info == null && !Busy;
        private GuildMember FindMember(ulong playerId)
        {
            if (Info == null || playerId == 0) return null;
            foreach (var member in Info.Members) if (member.PlayerId == playerId) return member;
            return null;
        }
        private static bool IsTip(TipInfoMessage tip, guild_error code) => tip != null && tip.Id == (uint)code;
        /// <summary>
        /// tip 文案里已经写明“列表已刷新”;重拉会把 Status 改成“正在读取帮会…”,玩家就看不到失败原因了。
        /// 这里把 tip 文案同步保回去。**调用方必须传一个 keepStatus = true 的重拉**,否则回包到达时
        /// 还会再盖一次 —— 这个助手只能挡住同步那一次。
        /// </summary>
        private void ReloadKeepingTip(Action reload) { string tip = Status; reload(); Status = tip; }
        private void Apply(GuildInfo info)
        {
            if (info == null || info.GuildId == 0)
            { Status = "服务器未返回有效帮会信息，请刷新重试。"; return; }
            bool mine = false;
            foreach (var member in info.Members) if (member.PlayerId == PlayerId) mine = true;
            if (!mine) { Status = "帮会成员身份尚未确认，请刷新重试。"; return; }
            Info = info.Clone(); HasLoaded = true; Status = "帮会信息已更新";
            // 服务端在入帮时已删光本人全部申请,本地列表作废;日后退帮经 Refresh 的
            // NotInGuild 分支重新排队拉取。
            MyApplications = null;
        }
        private bool Accept(TipInfoMessage tip)
        {
            if (tip == null || tip.Id == 0) return true;
            Status = tip.Id switch
            {
                (uint)guild_error.KGuildAlreadyInGuild => "你已加入帮会，请刷新查看。",
                (uint)guild_error.KGuildNotFound => "帮会已不存在，请刷新列表。",
                (uint)guild_error.KGuildNotInGuild => "你尚未加入帮会。",
                (uint)guild_error.KGuildFull => "帮会成员已满，请选择其他帮会。",
                (uint)guild_error.KGuildLeaderCantLeave => "帮主不能直接退出帮会。",
                (uint)guild_error.KGuildNotLeader => "此操作仅帮主可用。",
                (uint)guild_error.KGuildNoPermission => "当前身份无权进行此操作。",
                (uint)guild_error.KGuildNotRanked => "此帮会暂未上榜。",
                (uint)guild_error.KGuildNameInvalid => "帮会名称需为 1–24 个字，且不能包含换行等特殊字符。",
                (uint)guild_error.KGuildNameTaken => "该帮会名称已被使用，请换一个。",
                (uint)guild_error.KGuildAnnouncementTooLong => "公告过长，请精简后再保存。",
                (uint)guild_error.KGuildHomeZoneUnknown => "角色所属区服尚未确认，暂时无法使用帮会，请联系管理员。",
                (uint)guild_error.KGuildZoneMerging => "区服合并维护中，帮会操作暂停，请稍后再试。",
                (uint)guild_error.KGuildTargetNotMember => "对方已不在本帮会，列表即将刷新。",
                (uint)guild_error.KGuildCannotTargetSelf => "不能对自己执行此操作。",
                (uint)guild_error.KGuildRankTooLow => "你的帮会职位不足以执行此操作。",
                (uint)guild_error.KGuildOfficerLimit => "长老人数已达当前帮会等级上限。",
                (uint)guild_error.KGuildApplicationNotFound => "申请不存在或已失效，列表已刷新。",
                (uint)guild_error.KGuildApplicationLimit => "同时进行中的入帮申请已达上限，请先撤回其他申请。",
                (uint)guild_error.KGuildApplicationQueueFull => "该帮会待审申请已满，请稍后再试。",
                (uint)guild_error.KGuildBusyRetry => "帮会操作繁忙，请稍后重试。",
                _ => $"帮会服务暂未完成请求（{tip.Id}），请稍后重试。"
            };
            return false;
        }
        /// <summary>
        /// 管理类请求的 tip 处理:除文案外,“目标已不在帮会 / 自己已不在帮会”说明本地快照
        /// 已经过时,排队让界面下一帧自动重拉。KGuildBusyRetry 只显示文案——它是业务 tip,
        /// 走的是 success 回调,连接完好,不排队也不置 RequiresReconnect。
        /// </summary>
        private bool AcceptWrite(TipInfoMessage tip)
        {
            bool ok = Accept(tip);
            if (!ok && (IsTip(tip, guild_error.KGuildTargetNotMember) || IsTip(tip, guild_error.KGuildNotInGuild)))
                RefreshQueued = true;
            return ok;
        }
        private void Reject(string message) { if (Busy) return; Status = message; Changed?.Invoke(); }

        private void Request<T>(uint id, IMessage request, MessageParser<T> parser, Action<T> success)
            where T : IMessage<T>
        {
            if (_disposed) return;
            ObserveConnection();
            if (Busy) return;
            if (RequiresReconnect) { Reject(RecoveryMessage); return; }
            if (!_net.IsReady || PlayerId == 0) { Reject("请进入角色后再打开帮会。"); return; }
            int generation = ++_generation;
            ulong player = PlayerId;
            Busy = true; Status = "正在读取帮会，请稍候…"; Changed?.Invoke();
            _net.Call(id, request, parser, response =>
            {
                if (!Current(generation, player)) return;
                Busy = false; success(response); Changed?.Invoke();
            }, error =>
            {
                if (!Current(generation, player)) return;
                Busy = false;
                // GameClient 的 gRPC FIFO 无法区分超时旧响应，必须等真实断线后再发帮会请求。
                RequiresReconnect = true;
                Status = RecoveryMessage;
                Changed?.Invoke();
            });
        }
        private bool Current(int generation, ulong player) =>
            !_disposed && generation == _generation && _net.IsReady && PlayerId == player
            && ReferenceEquals(_observedConnection, _connectionIdentity());
        public void Dispose()
        {
            _net.Disconnected -= HandleDisconnected;
            // IBattleTransport 没有注销推送的接口;HandleGuildChanged 开头的 _disposed 判断
            // 保证 Dispose 之后到来的推送不再改动状态。
            _disposed = true; ++_generation; Busy = false;
        }
    }
}
