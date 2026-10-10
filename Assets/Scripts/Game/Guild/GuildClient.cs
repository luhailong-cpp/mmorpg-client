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
        // 活动写操作的结果文案里,需要补拉一次 GetPlayerGuild 的那些:点灯 / 领团圆礼成功(帮贡 / 资金变了),
        // 以及历练名单被拒且原因是"有人已离线 / 已不在帮"(成员快照过时了)。那一发会先后把 Status
        // 写成"正在读取帮会…"与"帮会信息已更新";文案先存这里,由下一个 GetPlayerGuild 回包落回 Status,只用一次。
        // 与 _pendingNotice 语义不同(那是推送带来的被请离 / 解散),不能共用一个字段(90 清单 X-06)。
        private string _writeNotice;
        // 单调时钟(毫秒):只用来量"拿到活动视图之后过了多久",与本机墙钟的快慢、时区、被改都无关。
        private readonly Func<long> _monotonicMs;
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

        // ── 活动(B6a,服务端 docs/design/guild-phase2/06-activities.md §6.37)──────────
        /// <summary>
        /// 活动页快照:每种类型至多一条视图(配表行 + 帮会进度 + 本人状态);null = 未加载。换帮即作废。
        /// 客户端不加载活动配表,名称、阈值、奖励、能不能参与(blocked_tip_id)全部以它为准。
        /// </summary>
        public GetGuildActivitiesResponse Activities { get; private set; }
        /// <summary>需要重拉活动视图(推送 / 进页 / 刷新键);由 DrainQueued 在活动页可见且空闲的那一帧发出。</summary>
        public bool ActivitiesQueued { get; private set; }
        /// <summary>
        /// 收到定向的同道历练邀请推送,而界面还没为它打开过活动页。GuildUiRoot 每帧看它:不在战斗、没有别的界面挡着、
        /// 也没在打字时打开活动页并 ConsumeTrialInvite;条件不满足就先留着。只在 TrialInviteNoticeMs 内为真 ——
        /// 推迟得太久,邀请早已过期,服务端连房间记录都不留了,这时再弹出活动页,页面上什么都没有。
        /// </summary>
        public bool TrialInvitePending => _trialInvite && _monotonicMs() - _trialInviteAtMs <= TrialInviteNoticeMs;
        /// <summary>
        /// 邀请推送的自动开页时限(毫秒),取服务端两个默认值之和:邀请有效期 30 秒(GuildRule.trial_invite_ttl_seconds)
        /// + 房间记录多留的 60 秒(06 §6.23:过期后的这段时间里视图仍带着"已过期"的房间,开页还有东西可看)。
        /// 客户端不加载 GuildRule;服务端改了有效期时,这里只影响"推迟多久之后不再自动开页",邀请本身照常显示在活动页上。
        /// </summary>
        public const long TrialInviteNoticeMs = 90000;
        // 邀请推送到达的时刻(单调时钟)与它所属的帮会。记帮会是因为推送可能先于第一次读到帮会快照(帮会窗口还没开过):
        // 那次快照落定时活动状态按"换了帮会"清空,属于这个帮会的邀请不能跟着清掉。
        private bool _trialInvite;
        private long _trialInviteAtMs;
        private ulong _trialInviteGuildId;
        // 服务端给"全员同意 → 开战"留的窗口(06 §6.25 的 launchWindowMs,proto 注释"≤5s")。视图不下发这个截止时刻,
        // 只能按视图生成时刻 + 窗口估:过了还停在"正在开战",说明开战结果的推送丢了(或那次调用没走完),重拉一次。
        private const ulong TrialLaunchWindowMs = 5000;
        // 活动快照还在、但已知过时:同一个帮会里等级变了(min_guild_level 的判定结果随之变),或刚打完一场历练
        // (见 NoteBattleEnded)。进页时由窗口重拉。
        private bool _activitiesStale;
        // 最近一次活动写操作的结果文案(点灯 / 领奖的成功提示,或任何一个活动写操作的拒绝原因)。紧随其后的活动视图重拉
        // —— 推送排的,或跨日 / 档期 / 帮会等级变化让窗口自动排的 —— 发出时若状态栏还停在这条文案上,就沿用它一次,
        // 不让那一发的默认文案把刚出来的结果盖掉。只沿用一次:之后玩家再点刷新,看到的是普通的"已更新"。
        // 历练建房 / 应答的成功提示不记在这里(原因见 ActivityWrite)。
        private string _activityResult;
        // 服务端时钟的锚:最近一份活动视图的 server_time_ms,与收到它那一刻的单调时钟读数。
        // 倒计时与"过没过日切点"都按 锚 + 流逝时间 估算,不读本机墙钟(proto:server_time_ms 的注释)。
        private ulong _activityServerMs;
        private long _activityAnchorMs;

        /// <summary>估算的服务端当前时刻(Unix 毫秒);0 = 没有活动视图,或视图没带 server_time_ms(样例 / 测试替身)。</summary>
        public ulong ActivityServerNowMs =>
            _activityServerMs == 0 ? 0 : _activityServerMs + (ulong)Math.Max(0L, _monotonicMs() - _activityAnchorMs);
        /// <summary>各视图里最早的下一个游戏日切点(UTC+8 05:00);0 = 未加载或回包没给。</summary>
        public ulong ActivityNextResetMs
        {
            get
            {
                ulong reset = 0;
                if (Activities != null)
                    for (int i = 0; i < Activities.Activities.Count; i++)
                    {
                        ulong next = Activities.Activities[i].NextResetMs;
                        if (next != 0 && (reset == 0 || next < reset)) reset = next;
                    }
                return reset;
            }
        }
        /// <summary>
        /// 活动页快照该不该重拉:没有快照、已知过时(帮会等级变了、刚打完一场历练)、服务端时钟已过下一个游戏日切点
        /// ("今日 1 / 1" 与置灰的按钮不再可信),或已过某个活动的档期切点、历练邀请房间的截止时刻(见 PassedScheduleBoundary)。
        /// serverNowMs 传 ActivityServerNowMs;传 0 表示不按时间判。
        /// </summary>
        public bool ActivitiesNeedReload(ulong serverNowMs) =>
            Activities == null || _activitiesStale || Passed(ActivityNextResetMs, serverNowMs) || PassedScheduleBoundary(serverNowMs);

        /// <summary>
        /// 档期切点:未开始的活动到了开始时刻、进行中的到了结束时刻。状态、按钮与"本期"进度都要换,同类型的下一行
        /// 也可能顶上来;这两个时刻没有任何推送。状态是服务端按拉取那一刻判的,这里只拿它与视图自带的起止时刻比,
        /// 不在客户端重算状态。估算的服务端时刻只会落后于服务端的真实时刻、不会超前(锚是视图生成的时刻,
        /// 流逝时间却从收到回包才开始算,差的是回程的网络时延),所以这里为真时服务端必已过了切点,
        /// 重拉回来的状态已经变了,不会原样再判一次过时。
        ///
        /// 历练的邀请房间(B6b)同理:房间过期是服务端在读取时按截止时刻折算的,到点**没有推送**。等待确认的房间过了
        /// expire_at_ms、"正在开战"的房间过了开战窗口,都要重拉,否则"等待同道确认""响应邀请"会一直挂在页面上。
        /// 这里只决定"该重拉了",房间是否真的结束仍以重拉回来的 state 为准,客户端不自己把房间判成过期。
        /// </summary>
        private bool PassedScheduleBoundary(ulong serverNowMs)
        {
            if (Activities == null) return false;
            for (int i = 0; i < Activities.Activities.Count; i++)
            {
                GuildActivityView view = Activities.Activities[i];
                if (view.State == GuildActivityState.Upcoming && Passed(view.StartAtMs, serverNowMs)) return true;
                if (view.State == GuildActivityState.Open && Passed(view.EndAtMs, serverNowMs)) return true;
                GuildTrialLobbyView lobby = view.TrialLobby;
                if (lobby == null) continue;
                if (lobby.State == GuildTrialLobbyState.Pending && Passed(lobby.ExpireAtMs, serverNowMs)) return true;
                // 视图没带生成时刻(样例 / 测试替身)就没法估开战窗口,不按时间判。
                if (lobby.State == GuildTrialLobbyState.Launching && view.ServerTimeMs != 0
                    && Passed(view.ServerTimeMs + TrialLaunchWindowMs, serverNowMs)) return true;
            }
            return false;
        }

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

        /// <param name="monotonicMs">单调时钟(毫秒),测试注入;默认取进程计时器。</param>
        public GuildClient(IBattleTransport net, Func<object> connectionIdentity = null, Func<long> monotonicMs = null)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _connectionIdentity = connectionIdentity ?? (() => _net);
            _monotonicMs = monotonicMs ?? DefaultMonotonicMs;
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
            RefreshQueued = ApplicantsQueued = MyApplicationsQueued = false; _pendingNotice = null; _writeNotice = null;
            ClearEconomy(); ClearActivities(); ClearTrialInvite();
            Status = RequiresReconnect ? RecoveryMessage : "请刷新帮会信息"; Changed?.Invoke();
        }

        // Stopwatch 的计时源不随系统时间调整而跳变;先除后乘,避免长时间运行后 ticks × 1000 溢出。
        private static long DefaultMonotonicMs()
        {
            long ticks = System.Diagnostics.Stopwatch.GetTimestamp(), frequency = System.Diagnostics.Stopwatch.Frequency;
            return ticks / frequency * 1000 + ticks % frequency * 1000 / frequency;
        }

        /// <summary>活动快照同样属于某一个帮会:换角、换帮、离帮都要作废(进度、今日次数、邀请房间都是那个帮会的)。</summary>
        private void ClearActivities()
        {
            Activities = null; ActivitiesQueued = false;
            _activitiesStale = false; _activityServerMs = 0; _activityAnchorMs = 0; _activityResult = null;
        }

        /// <summary>
        /// 丢掉还没被界面响应的历练邀请。换角、离帮时与 ClearActivities 一起调;换帮(含第一次读到帮会快照)时
        /// 只丢不属于新帮会的那种,见 Apply。
        /// </summary>
        private void ClearTrialInvite() { _trialInvite = false; _trialInviteAtMs = 0; _trialInviteGuildId = 0; }

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
                else
                {
                    ShopQueued |= Shop != null;
                    // 活动的物品奖励与商店兑换走同一条发放通道、发同一种推送(DELIVERY_DONE):
                    // 活动页上挂着"待发放"时顺带重拉,那一行才会消掉。
                    ActivitiesQueued |= HasPendingActivityReward();
                }
            }
            if (mine)
            {
                if (change.Kind == GuildChangeKind.ApplicationReceived) ApplicantsQueued = true;
                else if (change.Kind == GuildChangeKind.ActivityChanged)
                {
                    // 活动进度变了(本档期首次达阈值;历练邀请房间有人同意 / 拒绝 / 开战 / 结算):只重拉活动视图,不排 GetPlayerGuild。
                    // 没拉过活动页也排上:进页时就由 DrainQueued 发出,不必再等窗口自己拉。
                    ActivitiesQueued = true;
                    // 定向给本人的 = 同道历练邀请(服务端只在建房时把 target 填成被邀请人,06 §6.24 第 9 步);界面据此打开活动页。
                    if (aboutMe) NoteTrialInvite(change.GuildId);
                }
                else RefreshQueued = true;
                if (change.Kind == GuildChangeKind.Disbanded) _pendingNotice = "帮会已被帮主解散。";
                else if (change.Kind == GuildChangeKind.MemberKicked && aboutMe) _pendingNotice = "你已被请离帮会。";
                if (_pendingNotice != null) Status = _pendingNotice; // 立即可见;Refresh 回包后再次落定
            }
            else if (Info == null && aboutMe && change.Kind == GuildChangeKind.MemberJoined)
            { RefreshQueued = true; Status = "入帮申请已通过，正在读取帮会信息…"; }
            else if (Info == null && aboutMe && change.Kind == GuildChangeKind.ApplicationRejected)
            { MyApplicationsQueued = true; Status = "有一份入帮申请未获通过。"; }
            else if (Info == null && aboutMe && change.Kind == GuildChangeKind.ActivityChanged)
            {
                // 本地还没有帮会快照时来了邀请:帮会窗口这次登录还没开过(窗口关着不拉 GetPlayerGuild),这是被邀请人的常态,
                // 不能因为 Info 为空就把邀请丢掉。服务端只邀请它核对过的本帮成员,所以先补拉帮会快照,邀请照常提示;
                // 活动视图等快照落定、进了活动页再拉(那时 Info 才有,见 GuildWindow.MaybeAutoRequest)。
                // 本地快照明明是另一个帮会的(Info 非空且帮会不同)则不认:那不是发给当前这个帮会成员身份的。
                RefreshQueued = true;
                NoteTrialInvite(change.GuildId);
            }
            else if (!asset) return;
            Changed?.Invoke();
        }

        private void NoteTrialInvite(ulong guildId)
        {
            _trialInvite = true; _trialInviteAtMs = _monotonicMs(); _trialInviteGuildId = guildId;
            Status = "收到同道历练邀请。";
        }

        /// <summary>
        /// 由界面每帧调用;一次只发一个排队请求,Busy / 隔离 / 未就绪时什么也不做。
        /// activitiesVisible = 活动页正在显示:活动视图只在看得见时才重拉,不可见时标志留着,等进页再发。
        /// </summary>
        public void DrainQueued(bool applicantsVisible, bool activitiesVisible = false)
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
            // 活动视图排在帮会快照之后、经济两页之前(90 清单 X-07):点灯 / 领奖成功后先落总览的帮贡与资金,
            // 再轮到本页;标志由 RefreshActivities 自己清(没发出去会放回)。
            if (ActivitiesQueued && activitiesVisible && Info != null) { RefreshActivities(); return; }
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
                    // 活动写操作的提示只等这一个回包:无论结果如何都取走,不留给以后不相干的刷新。
                    string writeNotice = _writeNotice; _writeNotice = null;
                    if (response.ErrorMessage?.Id == (uint)guild_error.KGuildNotInGuild)
                    {
                        Info = null; Applicants = null; HasLoaded = true;
                        ClearEconomy(); ClearActivities(); ClearTrialInvite();
                        // 已不在帮会:优先说"被请离 / 已解散"(B2 的 _pendingNotice);"花灯已点亮"之类此时已无意义,丢弃。
                        Status = _pendingNotice ?? "尚未加入帮会，和同道相聚于此。"; _pendingNotice = null;
                        // 入帮时已把本地列表清空;退帮 / 被踢 / 解散后由 DrainQueued 重拉。
                        if (MyApplications == null) MyApplicationsQueued = true;
                        return;
                    }
                    if (!Accept(response.ErrorMessage)) return;
                    // 快照落定后把写操作的提示放回状态栏(否则停在"帮会信息已更新");快照无效时保留 Apply 写的原因。
                    if (Apply(response.Guild) && writeNotice != null) Status = writeNotice;
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
                        ? response.PendingDonations.Count + " 笔捐献结算中：" + DonationPendingReasonText(response.PendingDonations[0].ReasonTipId)
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
                    // 没有待发放时写最近一单的完整结果:商店页脚只有 800 宽,拒绝原因、部分发放只写得下结论
                    // (GuildWindow.ShopFooterResultText),全文靠这里。断线重连后 _pendingShopIds 已清空、
                    // settled 为空,进页自动拉取也要看得到上一单(05 §5.32 W14)。
                    Status = keepStatus ?? settled ?? (response.PendingOrders.Count > 0
                        ? response.PendingOrders.Count + " 单待发放：" + AssetReasonText(response.PendingOrders[0].ReasonTipId)
                        : response.RecentOrders.Count > 0 ? "最近一单：" + ShopResultText(response.RecentOrders[0])
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

        // ── 活动:灯会 / 团圆(B6a)、同道历练的建房与应答(B6b)────────────────────
        // 与经济页一样,"物品待发放"不是错误:背包满时奖励保持待发放、腾出空间后自动到账,
        // 服务端用视图字段(my_pending_reward_count 等)表达,error_message 只放真正的拒绝。
        // 历练是邀请确认制:StartTrial 只建一个待确认的房间,名单里的人各自 RespondTrialInvite,全员同意的那一下才开战;
        // 房间的状态(等待 / 开战中 / 已开战 / 已解散及原因)全在历练视图的 trial_lobby 里,客户端只照着显示。

        /// <summary>
        /// 请求重拉活动视图。只置标志:帮会只有一个在途请求位,打开窗口时那一发 GetPlayerGuild 多半还在路上,
        /// 这里直接发会被 Busy 丢掉、页面停在"正在读取";排队后由 DrainQueued 在空闲的那一帧发出。
        /// </summary>
        public void QueueActivities()
        {
            if (Info == null || ActivitiesQueued) return;
            ActivitiesQueued = true; Changed?.Invoke();
        }

        /// <summary>界面已响应邀请推送(打开了活动页),清掉标志。</summary>
        public void ConsumeTrialInvite() => ClearTrialInvite();

        /// <summary>
        /// 一场战斗刚结束(界面层按战斗层的显隐判断,见 GuildUiRoot)。快照里若有在途的历练 —— 房间还没解散,或本人有
        /// 进行中的历练对局 —— 它多半就是刚打完的这一场:结算推送至多一次、可能丢,结算本身也可能晚于战斗结束
        /// (走 Kafka)。标记过时,下次进活动页自动重拉一次。快照里没有在途历练时什么也不做,普通战斗不白拉。
        /// 帮会不注册战斗的推送:一个 message id 只有一个处理器,注册就会顶掉战斗模块自己的。
        /// </summary>
        public void NoteBattleEnded()
        {
            GuildActivityView trial = FindTrial();
            if (trial == null) return;
            GuildTrialLobbyView lobby = trial.TrialLobby;
            bool lobbyAlive = lobby != null && (lobby.State == GuildTrialLobbyState.Pending
                || lobby.State == GuildTrialLobbyState.Launching || lobby.State == GuildTrialLobbyState.Launched);
            if (trial.MyTrialBattleId != 0 || lobbyAlive) _activitiesStale = true;
        }

        public void RefreshActivities() => RefreshActivities(null);

        /// <summary>keepStatus 非空时本次回包只换数据不换文案(拒绝原因要活过这次重拉,做法同 RefreshDonations)。</summary>
        private void RefreshActivities(string keepStatus)
        {
            if (Info == null) { Reject("加入帮会后可参与帮会活动。"); return; }
            // 状态栏还停在上一次活动写操作的结果上:这一发沿用它(见 _activityResult)。调用方自己带了文案的不算沿用,
            // 那一次留给它后面紧跟着的排队重拉(带文案的那一发还在路上时来了推送)。
            bool carried = keepStatus == null && _activityResult != null && Status == _activityResult;
            if (carried) keepStatus = _activityResult;
            // 同 Refresh:被 Busy / 隔离 / 未就绪挡掉时代次不变,把排队标志放回去,下一帧再发。
            int before = _generation;
            bool queued = ActivitiesQueued;
            ActivitiesQueued = false;
            Request(MessageIds.GetGuildActivities, new GetGuildActivitiesRequest(),
                GetGuildActivitiesResponse.Parser, response =>
                {
                    if (!AcceptWrite(response.ErrorMessage)) return;
                    Activities = response; _activitiesStale = false;
                    // 锚跟着这份快照走:回包没带 server_time_ms 就清零(不按时间判过期),不沿用上一份的。
                    _activityServerMs = 0; _activityAnchorMs = 0;
                    for (int i = 0; i < response.Activities.Count; i++)
                        if (AnchorActivityClock(response.Activities[i])) break; // 同一次回包里各视图的 server_time_ms 相同
                    // 有在途的历练时默认文案就写它的状态(谁邀请、几人已同意、为什么解散):这类变化都是别人的操作经推送触发的重拉
                    // 带回来的,状态栏是唯一写得下完整原因(带人名)的地方,卡片上只有一行短结论(GuildWindow.TrialLine)。
                    Status = keepStatus ?? TrialStatusText(FindTrial()) ?? "帮会活动已更新";
                });
            if (_generation == before) ActivitiesQueued = queued;
            else if (carried) _activityResult = null; // 真发出去了才算用掉这一次
        }

        private const string StaleActivityText = "活动信息已过期，请刷新后重试。";

        /// <summary>元宵灯会:点一次灯。今日次数、档期、帮会等级、入帮时长都由服务端在事务里判,本地不预拦。</summary>
        public void LightLantern(uint activityId)
        {
            if (activityId == 0) { Reject(StaleActivityText); return; }
            ActivityWrite(MessageIds.LightGuildLantern, new LightGuildLanternRequest { ActivityId = activityId },
                LightGuildLanternResponse.Parser, response => response.ErrorMessage, response => response.Activity,
                "花灯已点亮，帮贡已到账。", guildChanged: true);
        }

        /// <summary>中秋团圆:领一次团圆礼(帮贡 + 物品)。在线人数由服务端在领奖那一刻数。</summary>
        public void ClaimReunion(uint activityId)
        {
            if (activityId == 0) { Reject(StaleActivityText); return; }
            ActivityWrite(MessageIds.ClaimGuildReunion, new ClaimGuildReunionRequest { ActivityId = activityId },
                ClaimGuildReunionResponse.Parser, response => response.ErrorMessage, response => response.Activity,
                "团圆礼已领取，物品稍后到账。", guildChanged: true);
        }

        /// <summary>
        /// 同道历练:建一个邀请房间(不直接开战)。members 是整支队伍,自己必须在首位(服务端也会把发起人挪到首位),
        /// 人数按视图里的 team_size_min / max(含发起人)。这里只挡"明显发不出去"的名单 —— 没有历练视图、不含自己、
        /// 人数越界、有重复或 0;在不在线、是不是本帮、入帮时长、有没有在别的房间里,都由服务端核对并指名道姓地回原因。
        /// 副本由配表定(视图的 dungeon_id),请求里没有"难度"这一项。
        /// </summary>
        public void StartTrial(uint activityId, IReadOnlyList<ulong> members)
        {
            GuildActivityView view = FindActivity(activityId);
            if (view == null || view.Type != GuildActivityType.Trial) { Reject(StaleActivityText); return; }
            if (!IsTrialRoster(members, view)) { Reject("请选择符合人数要求的在线同道。"); return; }
            var request = new StartGuildTrialRequest { ActivityId = activityId };
            for (int i = 0; i < members.Count; i++) request.MemberPlayerIds.Add(members[i]);
            ActivityWrite(MessageIds.StartGuildTrial, request, StartGuildTrialResponse.Parser,
                response => response.ErrorMessage, response => response.Activity,
                "邀请已发出，等待同道确认。", guildChanged: false);
        }

        private bool IsTrialRoster(IReadOnlyList<ulong> members, GuildActivityView view)
        {
            if (members == null || members.Count == 0 || members[0] != PlayerId) return false;
            if ((uint)members.Count < view.TeamSizeMin || (uint)members.Count > view.TeamSizeMax) return false;
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] == 0) return false;
                for (int j = 0; j < i; j++) if (members[j] == members[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// 同道历练:应答一个邀请房间。被邀请人 accept = true 同意、false 婉拒;发起人 accept = false 即取消房间。
        /// 任何一人婉拒或发起人取消,房间就解散。lobbyId 由界面传入当时看到的那个房间(确认框打开时锁定),
        /// 不取"现在视图里的房间":弹窗开着时房间可能已经换了一个,不能替玩家应答他没看过的邀请。
        /// </summary>
        public void RespondTrialInvite(ulong lobbyId, bool accept)
        {
            if (lobbyId == 0) { Reject("邀请已失效。"); return; }
            GuildTrialLobbyView lobby = FindTrial()?.TrialLobby;
            bool cancel = !accept && lobby != null && lobby.LobbyId == lobbyId && lobby.InitiatorPlayerId == PlayerId;
            string notice = accept ? "已同意同道历练。" : cancel ? "已取消同道历练邀请。" : "已婉拒同道历练邀请。";
            // 成功提示一律看回包里的房间,不按发请求那一刻的快照下结论。同意之后是"还在等别人"还是"全员到齐、已开战";
            // 婉拒 / 取消也不止一种结果:房间可能在这一下之前就已经不等人了(见 DeclinedTrialText)。
            Func<GuildActivityView, string> noticeOf;
            if (accept) noticeOf = view => AcceptedTrialText(view.TrialLobby, lobbyId);
            else noticeOf = view => DeclinedTrialText(view, lobbyId);
            ActivityWrite(MessageIds.RespondGuildTrialInvite, new RespondGuildTrialInviteRequest { LobbyId = lobbyId, Accept = accept },
                RespondGuildTrialInviteResponse.Parser, response => response.ErrorMessage, response => response.Activity,
                notice, guildChanged: false, noticeOf: noticeOf);
        }

        /// <summary>同意成功后的提示;回包里没有这个房间(或状态不认识)时返回 null,由调用方回落到固定提示。</summary>
        private string AcceptedTrialText(GuildTrialLobbyView lobby, ulong lobbyId)
        {
            if (lobby == null || lobby.LobbyId != lobbyId) return null;
            switch (lobby.State)
            {
                case GuildTrialLobbyState.Pending:
                    return "已同意同道历练，等待其他同道确认（" + lobby.AcceptedPlayerIds.Count + "/" + lobby.MemberPlayerIds.Count + "）。";
                case GuildTrialLobbyState.Launching:
                case GuildTrialLobbyState.Launched:
                    return "全员已同意，同道历练开战。";
                // 本人早就同意过、房间却已解散(别人婉拒 / 超时):服务端仍按成功回视图,提示写解散原因而不是"已同意"。
                case GuildTrialLobbyState.Ended: return LobbyEndText(lobby);
                default: return null;
            }
        }

        /// <summary>
        /// 婉拒 / 取消成功后的提示;返回 null = 用固定提示("已婉拒 / 已取消"):回包里的房间确实是本人这一下解散的,
        /// 或回包没带回这个房间(无从判断)。
        /// 服务端对"本人早已同意、房间却已不在等待确认"的应答不看 accept,一律按成功回视图(06 §6.25 表的 -3 行);
        /// 发起人建房即同意,所以他的"取消邀请"总走得到这一支 —— 最后一票刚到、正在开战或已开战(开战完成后才给他推送,
        /// 这段时间按钮还是"取消邀请"),或者别人先一步婉拒、邀请已过期、开战失败。这时房间不是被这一下解散的:
        /// 提示写房间的实际状态(与随后那一发重拉的默认文案同一句),不说"已取消",玩家不会一边读着"已取消"一边被拉进战斗。
        /// </summary>
        private string DeclinedTrialText(GuildActivityView view, ulong lobbyId)
        {
            GuildTrialLobbyView lobby = view.TrialLobby;
            if (lobby == null || lobby.LobbyId != lobbyId) return null;
            bool endedByMe = lobby.State == GuildTrialLobbyState.Ended
                && lobby.EndTipId == (uint)guild_error.KGuildTrialInviteDeclined
                && lobby.EndParameters.Count > 0 && TryPlayerId(lobby.EndParameters[0], out ulong who) && who == PlayerId;
            return endedByMe ? null : TrialStatusText(view);
        }

        /// <summary>
        /// 活动写操作的公共流程(点灯、领团圆礼、历练建房与应答)。四个写 RPC 的回包同形:
        /// error_message + 本活动提交后的视图。调用方先做各自的本地校验,再把请求交过来。
        ///
        /// guildChanged = 这次写操作当场发了帮贡 / 资金 / 物品(点灯、领奖为 true;历练建房与应答只动邀请房间,为 false)。
        /// 为 true 时:成功只回本活动的视图(proto:帮贡、资金变了由客户端再拉 GetPlayerGuild),所以把提示存进
        /// _writeNotice 并排队 Refresh —— **不在回调里连发第二个请求**,由下一帧的 DrainQueued 发出,回包后提示落回状态栏。
        ///
        /// 结果文案的去留:拒绝原因、点灯 / 领奖的成功提示记进 _activityResult,紧随其后的那一发活动视图重拉沿用它一次。
        /// 历练的成功提示**不**沿用:它后面的重拉都是别人的操作(同意 / 婉拒 / 开战)带来的,沿用就会让"邀请已发出"
        /// 盖住"某某婉拒了邀请";那几发重拉的默认文案本来就按房间状态写(RefreshActivities / TrialStatusText)。
        ///
        /// noticeOf 非空时成功提示由回包里的视图决定(返回 null 则用 notice)。
        /// </summary>
        private void ActivityWrite<T>(uint messageId, IMessage request, MessageParser<T> parser,
            Func<T, TipInfoMessage> tipOf, Func<T, GuildActivityView> viewOf, string notice, bool guildChanged,
            Func<GuildActivityView, string> noticeOf = null) where T : IMessage<T>
        {
            if (Info == null) { Reject("加入帮会后可参与帮会活动。"); return; }
            Request(messageId, request, parser, response =>
            {
                TipInfoMessage tip = tipOf(response);
                GuildActivityView view = viewOf(response);
                // 拒绝时服务端也可能带回视图(历练开战失败:房间已解散,原因在视图里),先应用再看 tip。
                bool replaced = ReplaceActivity(view);
                if (!AcceptWrite(tip))
                {
                    // 这次拒绝正是房间解散的原因(全员同意后开战失败):状态栏写成与其他成员看到的同一句(带"未能开战"的前因)。
                    GuildTrialLobbyView ended = replaced ? view.TrialLobby : null;
                    if (ended != null && ended.State == GuildTrialLobbyState.Ended && ended.EndTipId == tip.Id) Status = LobbyEndText(ended);
                    _activityResult = Status;
                    // 名单里有人已离线 / 已不在帮会:成员快照过时了(在线状态没有推送,活动页的刷新键也只刷活动视图)。
                    // 补拉一次帮会快照,选人框才不会再把他列出来;拒绝原因经 _writeNotice 活过那一发。
                    if (IsStaleRosterTip(tip)) { _writeNotice = Status; RefreshQueued = true; }
                    // 被拒又没带回视图,多半说明本页已过时(今日已参与、档期已过、人数变了、房间没了):带着拒绝文案直接重拉一次
                    // (Busy 已复位),不走排队 —— 那一发不带文案,会把拒绝原因盖成"帮会活动已更新"。
                    // 已不在帮会的不必拉:AcceptWrite 已排了 Refresh,活动快照随 NotInGuild 一起清掉。
                    if (!replaced && Activities != null && !IsTip(tip, guild_error.KGuildNotInGuild)) RefreshActivities(Status);
                    return;
                }
                string text = (noticeOf != null && view != null ? noticeOf(view) : null) ?? notice;
                Status = text; _activityResult = guildChanged ? text : null;
                if (guildChanged)
                {
                    // 有物品的奖励可能已同步进包(进不了包的稍后由 DELIVERY_DONE 推送再触发一次)。
                    // 没带回视图时不知道有没有物品,按有处理:多拉一次背包无害,漏拉则背包一直是旧的(scene 没有背包推送)。
                    if (view == null || view.RewardItems.Count > 0) AssetsChanged?.Invoke();
                    _writeNotice = text; RefreshQueued = true;
                }
                // 成功却没带回本活动的视图(服务端提交后重建视图失败,写已生效):本页带着提示重拉一次,
                // 否则按钮还停在"可点"。这是少见的兜底路径,所以直接发而不排队(排队那一发不带文案)。
                if (!replaced && Activities != null) RefreshActivities(text);
            });
        }

        /// <summary>历练名单被拒,且原因说明本地成员快照过时(有人已离线 / 已不在帮会)。</summary>
        private static bool IsStaleRosterTip(TipInfoMessage tip)
        {
            if (!IsTip(tip, guild_error.KGuildTrialTeamInvalid)) return false;
            string reason = Parameter(tip, 0);
            return reason == "offline" || reason == "not_member";
        }

        private GuildActivityView FindActivity(uint activityId)
        {
            if (Activities != null && activityId != 0)
                for (int i = 0; i < Activities.Activities.Count; i++)
                    if (Activities.Activities[i].ActivityId == activityId) return Activities.Activities[i];
            return null;
        }

        /// <summary>同道历练的视图(每种类型至多一条);没有快照或服务端没下发历练时为 null。</summary>
        private GuildActivityView FindTrial()
        {
            if (Activities != null)
                for (int i = 0; i < Activities.Activities.Count; i++)
                    if (Activities.Activities[i].Type == GuildActivityType.Trial) return Activities.Activities[i];
            return null;
        }

        /// <summary>就地换掉同一活动的视图;返回 false = 没有视图,或本地列表里没有这一条。</summary>
        private bool ReplaceActivity(GuildActivityView view)
        {
            if (Activities == null || view == null) return false;
            for (int i = 0; i < Activities.Activities.Count; i++)
                if (Activities.Activities[i].ActivityId == view.ActivityId)
                {
                    Activities.Activities[i] = view;
                    AnchorActivityClock(view);
                    return true;
                }
            return false;
        }

        /// <summary>用一份视图校准服务端时钟的锚;视图没带 server_time_ms(样例 / 测试替身)则不动,返回 false。</summary>
        private bool AnchorActivityClock(GuildActivityView view)
        {
            if (view.ServerTimeMs == 0) return false;
            _activityServerMs = view.ServerTimeMs; _activityAnchorMs = _monotonicMs();
            return true;
        }

        private bool HasPendingActivityReward()
        {
            if (Activities != null)
                for (int i = 0; i < Activities.Activities.Count; i++)
                    if (Activities.Activities[i].MyPendingRewardCount > 0) return true;
            return false;
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

        /// <summary>
        /// 结算中捐献的原因(状态栏的"N 笔捐献结算中：…"与捐献页页脚共用)。余额不足(27000)而结果尚未落盘时,
        /// 这笔多半以"未成功"收尾:写"正在确认结算结果"(同 §5.4 与 Donate 回调),不许诺入账。
        /// </summary>
        public static string DonationPendingReasonText(uint reasonTipId) =>
            reasonTipId == GuildAssetReasons.CurrencyInsufficient ? "余额不足，正在确认结算结果" : AssetReasonText(reasonTipId);

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
            if (Info == null || Info.GuildId != info.GuildId)
            {
                ClearEconomy(); ClearActivities();
                // 邀请推送可能先于这份快照到达(帮会窗口还没开过,见 HandleGuildChanged):它正是这个帮会的,留着;
                // 别的帮会的邀请随换帮作废。
                if (_trialInviteGuildId != info.GuildId) ClearTrialInvite();
            }
            else
            {
                // 同一个帮会里等级或本人帮贡变了(本人 / 别的长老升级、捐献入账、活动奖励):两页快照里的
                // 解锁状态与可用帮贡随之过时,标记后由窗口在进页时重拉(见 _donationsStale 注释)。
                if (Info.Level != info.Level || MyBalance(Info) != MyBalance(info)) _donationsStale = _shopStale = true;
                // 活动视图里的"能不能参与"是服务端按拉取那一刻的帮会等级算的;帮贡变化不影响它。
                if (Info.Level != info.Level && Activities != null) _activitiesStale = true;
            }
            Info = info.Clone(); HasLoaded = true; Status = "帮会信息已更新";
            // 服务端在入帮时已删光本人全部申请,本地列表作废;日后退帮经 Refresh 的
            // NotInGuild 分支重新排队拉取。
            MyApplications = null;
            return true;
        }
        private bool Accept(TipInfoMessage tip)
        {
            if (tip == null || tip.Id == 0) return true;
            Status = DescribeTip(tip, FindTrial()?.TrialLobby?.InitiatorPlayerId ?? 0);
            return false;
        }

        // ── 名字与历练文案 ──────────────────────────────────────────────────

        /// <summary>
        /// 帮会里显示一个角色的唯一兜底规则:有名字显示名字,空名或纯空白回落"道友 · 编号"。
        /// 名字由服务端批量取名填入,取名失败时 fail-open 下发空名,所以兜底必须一直保留。
        /// 规则放在这一层:状态栏的历练文案(本类)与窗口(GuildWindow.MemberDisplayName,转调这里)都要用,
        /// 反过来让本类去调窗口的静态方法就成了 Game 层依赖 UI 层(90 清单 Y-08 的"只留一份规则"照旧成立)。
        /// </summary>
        public static string DisplayName(ulong playerId, string name) =>
            string.IsNullOrWhiteSpace(name) ? "道友 · " + playerId : name;

        /// <summary>按当前帮会快照显示一个成员;不在快照里(刚离帮、快照过时)同样回落"道友 · 编号"。</summary>
        public string MemberDisplayName(ulong playerId) => DisplayName(playerId, FindMember(playerId)?.Name);

        /// <summary>
        /// 带上下文的 tip 文案:历练的两个码要把参数里的角色编号换成名字(查当前帮会快照),"婉拒"还要区分是不是
        /// 发起人自己取消(initiatorId = 房间发起人,0 = 不知道)。其余码与 TipText 相同。
        /// </summary>
        private string DescribeTip(TipInfoMessage tip, ulong initiatorId)
        {
            if (tip.Id == (uint)guild_error.KGuildTrialTeamInvalid)
                return TrialReasonText(Parameter(tip, 0), TrialSubject(Parameter(tip, 1)));
            if (tip.Id == (uint)guild_error.KGuildTrialInviteDeclined) return TrialDeclinedText(Parameter(tip, 0), initiatorId);
            return TipText(tip);
        }

        /// <summary>
        /// 历练文案的主语:参数里的角色编号 → "你" 或 "名字 "(带一个空格,后面直接接谓语);
        /// 编号缺失、为 0("size""duplicate" 等与具体某人无关的原因)或根本不是数字时返回 null,由文案换成不指名的说法。
        /// </summary>
        private string TrialSubject(string idText)
        {
            if (!TryPlayerId(idText, out ulong id)) return null;
            return id == PlayerId ? "你" : MemberDisplayName(id) + " ";
        }

        private static bool TryPlayerId(string text, out ulong id) =>
            ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id != 0;

        /// <summary>
        /// kGuildTrialTeamInvalid 的 reason(第一个参数,服务端 06 §6.24 的全集)写成人话。subject 见 TrialSubject,
        /// null = 不知道是谁。不认识的 reason(服务端日后新增、或参数被别的分支塞了英文说明)一律落到通用说法。
        /// </summary>
        public static string TrialReasonText(string reason, string subject)
        {
            string who = string.IsNullOrEmpty(subject) ? "有同道" : subject;
            switch (reason)
            {
                case "duplicate": return "队伍名单有重复或无效成员。";
                case "initiator_missing": return "发起人必须在队伍中。";
                case "size": return "队伍人数不符合要求。";
                case "not_member": return who + "不是本帮成员。";
                case "join_recent": return who + "入帮时间不足。";
                case "offline": return who + "当前不在线。";
                case "busy": return who + "正在响应其他历练邀请。";
                case "in_battle": return who + "正在战斗中。";
                case "not_ready": return who + "暂时无法入场（不在场景中或正在排队）。";
                default: return "队伍信息无效，请刷新后重试。";
            }
        }

        private const string TrialDeclinedGenericText = "同道历练邀请已被婉拒或取消。";

        /// <summary>kGuildTrialInviteDeclined 的参数是婉拒者的编号;他就是房间发起人时说"取消"。</summary>
        private string TrialDeclinedText(string idText, ulong initiatorId)
        {
            if (!TryPlayerId(idText, out ulong id)) return TrialDeclinedGenericText;
            string who = id == PlayerId ? "你" : MemberDisplayName(id) + " ";
            return who + (initiatorId != 0 && id == initiatorId ? "取消了同道历练。" : "婉拒了同道历练邀请。");
        }

        /// <summary>
        /// 邀请房间解散的原因(end_tip_id / end_parameters)写成一句完整的话,给状态栏用;卡片上只有一行,
        /// 写的是短结论(GuildWindow.TrialLine)。大多数码走与 Accept 相同的映射;下面几个码在"房间为什么散了"
        /// 这个语境里要换个说法:通用文案是对"我这次操作"说的(如"今日已参与,明日再来"),而这里说的往往是别人
        /// —— 次数用完的是发起人、没及时响应的是别的同道。
        /// </summary>
        public string LobbyEndText(GuildTrialLobbyView lobby)
        {
            if (lobby == null || lobby.EndTipId == 0) return "同道历练邀请已结束。";
            switch (lobby.EndTipId)
            {
                case (uint)guild_error.KGuildTrialInviteExpired: return "邀请已过期，有同道未及时响应。";
                case (uint)guild_error.KGuildActivityAlreadyClaimed: return "发起人今日次数已满，历练未能开战。";
                case (uint)guild_error.KGuildActivityNotOpen: return "活动已关闭，历练未能开战。";
                case (uint)guild_error.KGuildTrialServiceBusy: return "历练服务繁忙，未能开战，请稍后再试。";
            }
            var tip = new TipInfoMessage { Id = lobby.EndTipId };
            for (int i = 0; i < lobby.EndParameters.Count; i++) tip.Parameters.Add(lobby.EndParameters[i]);
            string text = DescribeTip(tip, lobby.InitiatorPlayerId);
            return lobby.EndTipId == (uint)guild_error.KGuildTrialTeamInvalid ? "历练未能开战：" + text : text;
        }

        /// <summary>
        /// 这个邀请房间是否正等着 me 应答:等待确认中、me 在名单里、不是发起人、还没同意。
        /// 窗口弹不弹邀请框、按钮写不写"响应邀请"、状态栏怎么说,都用这一个判断。
        /// </summary>
        public static bool AwaitsTrialResponse(GuildTrialLobbyView lobby, ulong me) =>
            lobby != null && lobby.State == GuildTrialLobbyState.Pending && lobby.InitiatorPlayerId != me
            && lobby.MemberPlayerIds.Contains(me) && !lobby.AcceptedPlayerIds.Contains(me);

        /// <summary>
        /// 历练在途状态的状态栏文案;没有在途的(没有进行中的对局,也没有房间)返回 null。
        /// 完全由视图推出,所以重拉多少次都是同一句或更新的一句,不存在"被后到的刷新盖掉"。
        /// </summary>
        private string TrialStatusText(GuildActivityView view)
        {
            if (view == null) return null;
            if (view.MyTrialBattleId != 0) return "同道历练进行中。";
            GuildTrialLobbyView lobby = view.TrialLobby;
            if (lobby == null) return null;
            switch (lobby.State)
            {
                case GuildTrialLobbyState.Pending:
                    if (AwaitsTrialResponse(lobby, PlayerId))
                        return MemberDisplayName(lobby.InitiatorPlayerId) + " 邀请你同往历练，请在活动页响应。";
                    return "同道历练邀请确认中（" + lobby.AcceptedPlayerIds.Count + "/" + lobby.MemberPlayerIds.Count + "）。";
                case GuildTrialLobbyState.Launching: return "全员已同意，正在开战…";
                case GuildTrialLobbyState.Launched: return "同道历练已开启。";
                case GuildTrialLobbyState.Ended: return LobbyEndText(lobby);
                default: return null;
            }
        }

        /// <summary>
        /// 帮会 tip 的中文文案(唯一一份映射):状态栏经 Accept → DescribeTip 用它,邀请房间的结束原因
        /// (end_tip_id / end_parameters)经 LobbyEndText 也落到它。调用方保证 tip 非空且 Id 非 0。
        /// 带参数的码按位次取 tip.Parameters;参数缺失或不是数字(不带参数的码,服务端会塞一段英文说明当唯一参数)
        /// 时回落到不带数字的通用说法,不把英文说明拼进文案。
        /// 这是不认识成员的静态版:历练的两个码在这里不带人名,带人名的版本见 DescribeTip。
        /// </summary>
        public static string TipText(TipInfoMessage tip)
        {
            return tip.Id switch
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
                // 活动(B6)。KGuildAssetPending 上面 B5 已有,不能再写一遍(CS8510,90 清单 X-05)。
                (uint)guild_error.KGuildActivityNotOpen => "该活动暂未开放。",
                (uint)guild_error.KGuildActivityAlreadyClaimed => "今日已参与，明日 05:00 后再来。",
                // 参数 [在线人数, 阈值]。
                (uint)guild_error.KGuildActivityThresholdNotReached =>
                    NumberParameter(tip, 0) != null && NumberParameter(tip, 1) != null
                        ? "同时在线的同道不足（" + NumberParameter(tip, 0) + "/" + NumberParameter(tip, 1) + "），再等等大家吧。"
                        : "同时在线的同道不足，再等等大家吧。",
                // 参数 [min_level]。
                (uint)guild_error.KGuildActivityLevelTooLow => NumberParameter(tip, 0) != null
                    ? "帮会达到 " + NumberParameter(tip, 0) + " 级后才能参与。" : "帮会等级不足，暂时不能参与。",
                // 参数 [N 小时]。
                (uint)guild_error.KGuildActivityJoinTooRecent => NumberParameter(tip, 0) != null
                    ? "入帮满 " + NumberParameter(tip, 0) + " 小时后才能参与帮会活动。" : "入帮时间不足，暂时不能参与帮会活动。",
                // 历练(B6b)。TeamInvalid 的参数是 [reason, player_id],Declined 的是 [婉拒者];这里是不带人名的说法。
                (uint)guild_error.KGuildTrialTeamInvalid => TrialReasonText(Parameter(tip, 0), null),
                (uint)guild_error.KGuildTrialInviteExpired => "邀请已失效。",
                (uint)guild_error.KGuildTrialInviteDeclined => TrialDeclinedGenericText,
                // 参数 [剩余秒数]。
                (uint)guild_error.KGuildTrialInviteCooldown => NumberParameter(tip, 0) != null
                    ? "发起太频繁，请 " + NumberParameter(tip, 0) + " 秒后再试。" : "发起太频繁，请稍后再试。",
                (uint)guild_error.KGuildTrialServiceBusy => "历练服务繁忙，请稍后再试。",
                _ => $"帮会服务暂未完成请求（{tip.Id}），请稍后重试。"
            };
        }

        /// <summary>第 index 个 tip 参数原文;没有这个位次时为空串(不抛异常,由调用方当"不认识"处理)。</summary>
        private static string Parameter(TipInfoMessage tip, int index) =>
            index < tip.Parameters.Count ? tip.Parameters[index] : "";

        /// <summary>第 index 个 tip 参数,仅当它是十进制非负整数时返回(原样,不重排格式);否则 null。</summary>
        private static string NumberParameter(TipInfoMessage tip, int index)
        {
            if (index >= tip.Parameters.Count) return null;
            string value = tip.Parameters[index];
            return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _) ? value : null;
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
