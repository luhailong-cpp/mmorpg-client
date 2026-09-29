using System;
using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>
    /// 资产通道原因码(服务端 data/tip/Tip.xlsx 的 //asset_error 段,base=27000,码名见各常量)。
    /// 客户端不生成 asset_error 枚举(gen_proto 只收玩家可见的几个域,90 清单 G-02),这里按码名逐个镜像;
    /// 表里改号要同步这里。PENDING 视图里的是"最近一次暂时原因",REJECTED 里的是拒绝原因。
    /// </summary>
    public static class GuildAssetReasons
    {
        public const uint CurrencyInsufficient = 27000; // kAssetCurrencyInsufficient
        public const uint BagFull = 27001;              // kAssetBagFull
        public const uint InBattle = 27002;             // kAssetInBattle
        public const uint Frozen = 27003;               // kAssetFrozen(角色迁移 / 存盘属主切换中)
        public const uint InvalidBundle = 27004;        // kAssetInvalidBundle
        public const uint Blocked = 27005;              // kAssetBlocked
        public const uint PlayerNotHere = 27006;        // kAssetPlayerNotHere
        public const uint PartialApplied = 27007;       // kAssetPartialApplied(90 清单 X-15)
        public const uint AuthFailed = 27008;           // kAssetAuthFailed
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

        // ── 经济(B5,服务端 docs/design/guild-phase2/05-economy.md §5.34)──────────
        /// <summary>捐献页快照:选项与本人今日用量、结算中与 10 分钟内的最近结果;null = 未加载。换帮即作废。</summary>
        public GetGuildDonateOptionsResponse Donations { get; private set; }
        /// <summary>商店页快照:商品、可用帮贡、待发放与最近结果;null = 未加载。换帮即作废。</summary>
        public GetGuildShopResponse Shop { get; private set; }
        /// <summary>推送说捐献结算完了,且捐献页拉过:由 DrainQueued 重拉。</summary>
        public bool DonationsQueued { get; private set; }
        /// <summary>推送说兑换发放完了,且商店页拉过:由 DrainQueued 重拉。</summary>
        public bool ShopQueued { get; private set; }
        /// <summary>
        /// 捐献 / 兑换可能改了背包或货币。scene 没有余额推送,界面层据此重拉背包。
        /// 已离帮后才结算的那一笔也会触发(推送按"关于我"判,不要求仍在该帮)。
        /// </summary>
        public event Action AssetsChanged;
        /// <summary>
        /// 升级按钮能否点:只看职位与是否满级。资金够不够交给服务端判 —— 别的长老刚花过钱时,
        /// 本地 Info.Funds 可能是旧值,按它收起按钮会让"其实够"的人点不了。
        /// </summary>
        public bool CanUpgrade => Info != null && GuildRoles.Rank(Role) >= GuildRoles.RankOfficer && Info.UpgradeCostFunds > 0;
        // 本人结算中的指令。重拉后不在待结算列表里的那几笔 = 刚结算完,拿最近结果给文案。
        private HashSet<ulong> _pendingDonationIds = new HashSet<ulong>(), _pendingShopIds = new HashSet<ulong>();
        // 快照还在、但已知过时:同一个帮会里等级变了(解锁状态)或本人帮贡变了(可用帮贡)。
        // 只作标记、不排队重拉 —— DrainQueued 那一发不带文案,会把升级 / 捐献的结果盖成"…已更新";
        // 由窗口在进页时据 DonationsNeedReload / ShopNeedsReload 自动拉一次。本页重拉成功即清,换帮随 ClearEconomy 清。
        private bool _donationsStale, _shopStale;

        /// <summary>
        /// 捐献页快照该不该重拉:没有快照、已知过时(帮会等级 / 本人帮贡变了),或已过服务端给的下一个日切点
        /// ("今日 x/y" 不再可信)。窗口进页时据此自动拉一次。nowMs 取本地时钟:偏差只让重拉早一点或晚一点,
        /// 次数仍由服务端在事务里复核。
        /// </summary>
        public bool DonationsNeedReload(ulong nowMs) =>
            Donations == null || _donationsStale || Passed(Donations.NextDailyResetMs, nowMs);
        /// <summary>商店页同上,另看每周切点(周限购)。</summary>
        public bool ShopNeedsReload(ulong nowMs) =>
            Shop == null || _shopStale || Passed(Shop.NextDailyResetMs, nowMs) || Passed(Shop.NextWeeklyResetMs, nowMs);
        // 0 = 回包里没有切点(样例 / 测试替身),不按时间判过期。
        private static bool Passed(ulong resetMs, ulong nowMs) => resetMs != 0 && nowMs >= resetMs;

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
            ClearEconomy();
            Status = RequiresReconnect ? RecoveryMessage : "请刷新帮会信息"; Changed?.Invoke();
        }

        /// <summary>经济快照属于某一个帮会:换角、换帮、离帮都要作废,否则会带着上一个帮会的次数与帮贡进来。</summary>
        private void ClearEconomy()
        {
            Donations = null; Shop = null; DonationsQueued = ShopQueued = false;
            _donationsStale = _shopStale = false;
            _pendingDonationIds.Clear(); _pendingShopIds.Clear();
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
            // 资产结算(B5):只发给结算的那个人。先于 if 链处理:离帮后才结算的那一笔也得让背包重拉,
            // 而此时 mine 为假、下面的链会走到 return。捐献页 / 商店页只在拉过时才排队重拉。
            bool asset = aboutMe && (change.Kind == GuildChangeKind.FundsChanged || change.Kind == GuildChangeKind.DeliveryDone);
            if (asset)
            {
                AssetsChanged?.Invoke();
                if (change.Kind == GuildChangeKind.FundsChanged) DonationsQueued |= Donations != null;
                else ShopQueued |= Shop != null;
            }
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
            else if (!asset) return;
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
            if (MyApplicationsQueued) { MyApplicationsQueued = false; LoadMyApplications(); return; }
            // 经济两页排在帮会快照之后:结算推送同时排了 Refresh 与本页重拉,先落总览的资金 / 帮贡,
            // 本页的"已入账 / 已发放"文案后到,才不会被 Refresh 的"帮会信息已更新"盖掉(90 清单 X-07)。
            if (DonationsQueued) { DonationsQueued = false; if (Donations != null) RefreshDonations(); return; }
            if (ShopQueued) { ShopQueued = false; if (Shop != null) RefreshShop(); }
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
                        ClearEconomy();
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

        // ── 经济:捐献 / 升级 / 商店(B5)──────────────────────────────────────
        // "结算中 / 待发放"不是错误:服务端用视图里的 status 表达,error_message 只放真正的拒绝。
        // 写成功后都跟一发本页重拉(次数、余额、待结算列表都在那份快照里),文案经 keepStatus 带过去,
        // 否则 Request 同步写的"正在读取帮会…"与回包的默认文案会先后把结果盖掉。

        public void RefreshDonations() => RefreshDonations(null);

        private void RefreshDonations(string keepStatus)
        {
            if (Info == null) { Reject("请先加入帮会。"); return; }
            Request(MessageIds.GetGuildDonateOptions, new GetGuildDonateOptionsRequest(),
                GetGuildDonateOptionsResponse.Parser, response =>
                {
                    if (!AcceptWrite(response.ErrorMessage)) return;
                    // 上次还在结算、这次不在待结算列表里的 = 刚结算完;文案取最近结果里的那一条(可能已滑出 10 分钟窗口)。
                    string settled = null;
                    foreach (ulong opId in _pendingDonationIds)
                        if (FindDonation(response.PendingDonations, opId) == null)
                            settled ??= DonationResultText(FindDonation(response.RecentResults, opId));
                    _pendingDonationIds.Clear();
                    foreach (var pending in response.PendingDonations) _pendingDonationIds.Add(pending.OpId);
                    Donations = response; _donationsStale = false;
                    Status = keepStatus ?? settled ?? (response.PendingDonations.Count > 0
                        ? response.PendingDonations.Count + " 笔捐献结算中：" + AssetReasonText(response.PendingDonations[0].ReasonTipId)
                        : "捐献信息已更新");
                    if (settled != null) AssetsChanged?.Invoke();
                });
        }

        /// <summary>按配表选项捐一次。今日次数、余额、帮会等级都由服务端在事务里判,本地不预拦。</summary>
        public void Donate(uint donateId)
        {
            if (Info == null || donateId == 0) { Reject("请先加入帮会。"); return; }
            Request(MessageIds.DonateToGuild, new DonateToGuildRequest { DonateId = donateId },
                DonateToGuildResponse.Parser, response =>
                {
                    // 请求者仍在帮时服务端总带最新快照(含结算中):资金与帮贡以它为准。
                    if (IsValidGuild(response.Guild)) Apply(response.Guild);
                    if (!AcceptWrite(response.ErrorMessage))
                    {
                        // 未决指令过多 / 资产通道关闭:页面上的待结算列表多半已经过时,重拉一次让它收敛。
                        // 带着拒绝文案直接重拉(Busy 已复位),不走 DrainQueued —— 那一发不带文案,
                        // 待结算为空时会把拒绝原因盖成"捐献信息已更新",看着像捐成功了。
                        if (IsTip(response.ErrorMessage, guild_error.KGuildAssetPending) && Donations != null) RefreshDonations(Status);
                        return;
                    }
                    var donation = response.Donation;
                    string text;
                    if (donation == null)
                        text = "捐献已提交，结果以捐献页为准。";
                    else if (donation.Status == GuildAssetOrderStatus.Applied)
                    {
                        text = "捐献成功：帮贡 +" + donation.ContributionGain + "，帮会资金 +" + FormatAmount(donation.FundsGain);
                        AssetsChanged?.Invoke();
                    }
                    else if (donation.Status == GuildAssetOrderStatus.Pending)
                    {
                        _pendingDonationIds.Add(donation.OpId);
                        text = donation.ReasonTipId == GuildAssetReasons.CurrencyInsufficient
                            ? "余额不足，正在确认结算结果"
                            : "捐献结算中：" + AssetReasonText(donation.ReasonTipId);
                    }
                    else
                    {
                        text = DonationResultText(donation);
                        if (donation.Status == GuildAssetOrderStatus.AppliedPartial) AssetsChanged?.Invoke();
                    }
                    RefreshDonations(text);
                });
        }

        /// <summary>
        /// 帮主 / 长老花帮会资金升一级。expectedLevel 是玩家在确认框里看到的等级(proto:客户端看到的当前等级),
        /// 不是点确认那一刻的 Info.Level:确认框开着时,别的长老升级的推送会把本地快照刷成新等级,
        /// 按新等级发就会按下一级的花费再扣一次、连升两级。
        /// </summary>
        public void Upgrade(uint expectedLevel)
        {
            if (!CanUpgrade) { Reject("仅帮主或长老可升级；帮会已满级时不可升级。"); return; }
            // 本地已知等级变了:确认框里的花费与目标等级都过时,不替玩家按新等级升级。
            if (Info.Level != expectedLevel) { Reject("帮会等级已变化，请重新确认升级。"); return; }
            Request(MessageIds.UpgradeGuild, new UpgradeGuildRequest { ExpectedLevel = expectedLevel },
                UpgradeGuildResponse.Parser, response =>
                {
                    // 资金不足等业务失败也带最新快照:先落快照,资金显示随之更新,再给拒绝文案。
                    bool applied = IsValidGuild(response.Guild) && Apply(response.Guild);
                    if (!AcceptWrite(response.ErrorMessage)) return;
                    if (!applied)
                    {
                        if (!IsValidGuild(response.Guild)) Status = InvalidSnapshotText;
                        return;
                    }
                    // 等级没变 = 别的长老刚升过(expected_level 对不上),服务端不再扣钱,只回最新快照。
                    Status = Info.Level > expectedLevel ? "帮会已升至 Lv." + Info.Level : "帮会等级已是最新";
                });
        }

        public void RefreshShop() => RefreshShop(null);

        private void RefreshShop(string keepStatus)
        {
            if (Info == null) { Reject("请先加入帮会。"); return; }
            Request(MessageIds.GetGuildShop, new GetGuildShopRequest(),
                GetGuildShopResponse.Parser, response =>
                {
                    if (!AcceptWrite(response.ErrorMessage)) return;
                    string settled = null;
                    foreach (ulong opId in _pendingShopIds)
                        if (FindOrder(response.PendingOrders, opId) == null)
                            settled ??= ShopResultText(FindOrder(response.RecentOrders, opId));
                    _pendingShopIds.Clear();
                    foreach (var pending in response.PendingOrders) _pendingShopIds.Add(pending.OpId);
                    Shop = response; _shopStale = false;
                    Status = keepStatus ?? settled ?? (response.PendingOrders.Count > 0
                        ? response.PendingOrders.Count + " 单待发放：" + AssetReasonText(response.PendingOrders[0].ReasonTipId)
                        : "帮会商店已更新");
                    if (settled != null) AssetsChanged?.Invoke();
                });
        }

        /// <summary>用可用帮贡兑换商品。限购、等级、帮贡都由服务端在事务里判。</summary>
        public void Buy(uint goodsId, uint count = 1)
        {
            if (Info == null || goodsId == 0 || count == 0) { Reject("请先加入帮会。"); return; }
            Request(MessageIds.BuyGuildShopGoods, new BuyGuildShopGoodsRequest { GoodsId = goodsId, Count = count },
                BuyGuildShopGoodsResponse.Parser, response =>
                {
                    if (!AcceptWrite(response.ErrorMessage))
                    {
                        // 同 Donate:带着拒绝文案直接重拉本页,不走 DrainQueued(会盖成"帮会商店已更新")。
                        if (IsTip(response.ErrorMessage, guild_error.KGuildAssetPending) && Shop != null) RefreshShop(Status);
                        return;
                    }
                    var order = response.Order;
                    // 回包的余额是提交后的权威值;兑换回包不带帮会快照,总览里"可用帮贡"那一格就地跟上,
                    // 否则要等下一次 GetPlayerGuild 才对得上商店页。捐献页页脚的"可用帮贡"来自捐献快照,
                    // 同样过时了:下次进捐献页自动重拉。
                    var me = FindMember(PlayerId);
                    if (me != null) me.ContributionBalance = response.ContributionBalance;
                    _donationsStale = true;
                    string text;
                    if (order == null)
                        text = "兑换已提交，结果以商店页为准。";
                    else if (order.Status == GuildAssetOrderStatus.Applied)
                    {
                        text = "兑换成功，物品已放入背包";
                        AssetsChanged?.Invoke();
                    }
                    else if (order.Status == GuildAssetOrderStatus.Pending)
                    {
                        _pendingShopIds.Add(order.OpId);
                        text = "兑换已受理：" + AssetReasonText(order.ReasonTipId);
                    }
                    else
                    {
                        text = ShopResultText(order);
                        if (order.Status == GuildAssetOrderStatus.AppliedPartial) AssetsChanged?.Invoke();
                    }
                    RefreshShop(text);
                });
        }

        /// <summary>金额千分位。固定用不变区域:系统区域是德语等时 N0 会出 "12.000",与服务端文案、测试都对不上。</summary>
        public static string FormatAmount(ulong value) => value.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>资产通道原因码的中文(待结算的暂时原因,或拒绝原因)。窗口与状态栏共用。</summary>
        public static string AssetReasonText(uint reasonTipId) => reasonTipId switch
        {
            0 => "正在结算，稍后自动完成",
            GuildAssetReasons.CurrencyInsufficient => "银两或灵石不足",
            GuildAssetReasons.BagFull => "背包已满，腾出空间后自动发放",
            GuildAssetReasons.InBattle => "战斗中暂不结算，战斗结束后自动继续",
            GuildAssetReasons.Frozen => "角色迁移中，稍后自动继续",
            GuildAssetReasons.InvalidBundle => "资产指令无效",
            GuildAssetReasons.Blocked => "该物品或货币暂被限制",
            GuildAssetReasons.PlayerNotHere => "正在确认角色位置，稍后自动继续",
            GuildAssetReasons.PartialApplied => "已部分发放，客服将补偿",
            GuildAssetReasons.AuthFailed => "资产指令校验失败",
            _ => "稍后自动继续",
        };

        /// <summary>一笔捐献的结果文案;null = 已结算但已滑出"最近结果"窗口。</summary>
        public static string DonationResultText(GuildDonationView view)
        {
            if (view == null) return "有捐献已结算，请查看背包与帮会资金。";
            switch (view.Status)
            {
                case GuildAssetOrderStatus.Applied:
                    return "捐献已入账：帮贡 +" + view.ContributionGain + "，帮会资金 +" + FormatAmount(view.FundsGain);
                case GuildAssetOrderStatus.Rejected: return "捐献未成功：" + AssetReasonText(view.ReasonTipId);
                case GuildAssetOrderStatus.Aborted: return "捐献超时未结算，已撤销，次数已退回。";
                case GuildAssetOrderStatus.AppliedPartial: return "捐献只结算了一部分，已记录，客服将补偿。";
                case GuildAssetOrderStatus.Pending: return "捐献结算中：" + AssetReasonText(view.ReasonTipId);
                default: return "有捐献已结算，请查看背包与帮会资金。";
            }
        }

        /// <summary>一单兑换的结果文案;null = 已结算但已滑出"最近结果"窗口。</summary>
        public static string ShopResultText(GuildShopOrderView view)
        {
            if (view == null) return "有兑换已结算，请查看背包。";
            switch (view.Status)
            {
                case GuildAssetOrderStatus.Applied: return "兑换的物品已发放，请查看背包。";
                case GuildAssetOrderStatus.Rejected: return "兑换失败，帮贡与限购已退回：" + AssetReasonText(view.ReasonTipId);
                case GuildAssetOrderStatus.Aborted: return "兑换已撤销，帮贡与限购已退回。";
                case GuildAssetOrderStatus.AppliedPartial: return "兑换的物品只发放了一部分，已记录，客服将补偿。";
                case GuildAssetOrderStatus.Pending: return "待发放：" + AssetReasonText(view.ReasonTipId);
                default: return "有兑换已结算，请查看背包。";
            }
        }

        private static GuildDonationView FindDonation(IEnumerable<GuildDonationView> views, ulong opId)
        {
            foreach (var view in views) if (view.OpId == opId) return view;
            return null;
        }
        private static GuildShopOrderView FindOrder(IEnumerable<GuildShopOrderView> views, ulong opId)
        {
            foreach (var view in views) if (view.OpId == opId) return view;
            return null;
        }

        // ── 内部 ────────────────────────────────────────────────────────────

        private bool CanJoin() => HasLoaded && Info == null && !Busy;
        private GuildMember FindMember(ulong playerId)
        {
            if (Info == null || playerId == 0) return null;
            foreach (var member in Info.Members) if (member.PlayerId == playerId) return member;
            return null;
        }
        private ulong MyBalance(GuildInfo info)
        {
            foreach (var member in info.Members) if (member.PlayerId == PlayerId) return member.ContributionBalance;
            return 0;
        }
        private static bool IsTip(TipInfoMessage tip, guild_error code) => tip != null && tip.Id == (uint)code;
        /// <summary>
        /// tip 文案里已经写明“列表已刷新”;重拉会把 Status 改成“正在读取帮会…”,玩家就看不到失败原因了。
        /// 这里把 tip 文案同步保回去。**调用方必须传一个 keepStatus = true 的重拉**,否则回包到达时
        /// 还会再盖一次 —— 这个助手只能挡住同步那一次。
        /// </summary>
        private void ReloadKeepingTip(Action reload) { string tip = Status; reload(); Status = tip; }
        private const string InvalidSnapshotText = "服务器未返回有效帮会信息，请刷新重试。";
        private static bool IsValidGuild(GuildInfo info) => info != null && info.GuildId != 0;
        /// <summary>落一份权威快照;返回 false = 快照无效或里面没有本人,Status 已写明原因,Info 不变。</summary>
        private bool Apply(GuildInfo info)
        {
            if (!IsValidGuild(info)) { Status = InvalidSnapshotText; return false; }
            bool mine = false;
            foreach (var member in info.Members) if (member.PlayerId == PlayerId) mine = true;
            if (!mine) { Status = "帮会成员身份尚未确认，请刷新重试。"; return false; }
            // 换了帮会(含第一次入帮):上一个帮会的捐献 / 商店快照与次数作废。
            if (Info == null || Info.GuildId != info.GuildId) ClearEconomy();
            // 同一个帮会里等级或本人帮贡变了(本人 / 别的长老升级、捐献入账、活动奖励):两页快照里的
            // 解锁状态与可用帮贡随之过时,标记后由窗口在进页时重拉(见 _donationsStale 注释)。
            else if (Info.Level != info.Level || MyBalance(Info) != MyBalance(info)) _donationsStale = _shopStale = true;
            Info = info.Clone(); HasLoaded = true; Status = "帮会信息已更新";
            // 服务端在入帮时已删光本人全部申请,本地列表作废;日后退帮经 Refresh 的
            // NotInGuild 分支重新排队拉取。
            MyApplications = null;
            return true;
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
                // 经济(B5)。ZoneMerging / RankTooLow 上面 B2 已有,不能再写一遍(CS8510,90 清单 X-05)。
                (uint)guild_error.KGuildFundsInsufficient => "帮会资金不足，暂时无法升级。",
                (uint)guild_error.KGuildMaxLevel => "帮会已达最高等级。",
                (uint)guild_error.KGuildDonateLimit => "今日该项捐献次数已用完，明日 05:00 重置。",
                (uint)guild_error.KGuildCurrencyInsufficient => "银两或灵石不足，无法捐献。",
                (uint)guild_error.KGuildAssetPending => "还有未结算的帮会操作，请稍后再试。",
                (uint)guild_error.KGuildAssetRejected => "资产结算失败，本次操作已撤销。",
                (uint)guild_error.KGuildShopGoodsNotFound => "该商品已下架，请刷新商店。",
                (uint)guild_error.KGuildShopLevelTooLow => "帮会等级不足。",
                (uint)guild_error.KGuildShopLimit => "已达限购数量。",
                (uint)guild_error.KGuildContributionInsufficient => "可用帮贡不足。",
                // 捐献 / 兑换发指令号失败时回它(号段服务暂不可用),不是玩家能改的事。
                (uint)guild_error.KGuildIdGenUnavailable => "帮会服务繁忙，请稍后再试。",
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
