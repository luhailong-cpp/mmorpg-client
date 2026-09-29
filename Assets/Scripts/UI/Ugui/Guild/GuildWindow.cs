using System;
using System.Collections.Generic;
using System.Text;
using Guildpb;
using MmorpgClient.Game.Guild;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Guild.GuildUiArt;

namespace MmorpgClient.UI.Ugui.Guild
{
    public enum GuildPage { Overview, Members, Ranking, Donate, Activities, Shop }

    /// <summary>可交互原生帮会窗口；文字、成员、分页与表单全部由真实数据驱动。</summary>
    public sealed class GuildWindow
    {
        public event Action RefreshRequested;
        public event Action<uint> RankRequested;
        public event Action<string> CreateRequested, AnnouncementRequested;
        // 申请制取代“加入”:排行页只发申请 / 撤回,入帮与否由帮主或长老审批。
        public event Action<ulong> ApplyRequested, CancelApplicationRequested, KickRequested, TransferRequested;
        public event Action<ulong, uint> RoleRequested;
        public event Action<ulong, bool> ReviewRequested;
        public event Action ApplicationsRequested;
        public event Action LeaveRequested, DisbandRequested;
        // 经济(B5):捐献页 / 商店页的读取与写操作,升级在总览。
        public event Action DonationsRequested, ShopRequested;
        public event Action<uint> DonateRequested, ShopBuyRequested;
        /// <summary>参数是确认框打开时的帮会等级(即玩家确认的"升至 Lv.N+1" 的 N),作为 expected_level 发出。</summary>
        public event Action<uint> UpgradeRequested;
        public bool IsVisible => _root.gameObject.activeSelf;
        public bool ModalVisible => _modal != null && _modal.gameObject.activeSelf;
        /// <summary>申请视图是否正在显示；GuildClient.DrainQueued 据此决定重拉列表还是只刷角标。</summary>
        public bool ShowingApplications => IsVisible && Page == GuildPage.Members && _showApplications;
        public GuildPage Page { get; private set; }
        public const int MembersPerPage = 5;
        public const int GoodsPerPage = 6;
        private readonly RectTransform _root, _body, _frame, _rail;
        private readonly TMP_Text _status, _summary;
        private readonly CanvasGroup _frameInput;
        private readonly Button[] _tabs = new Button[6];
        private readonly Image[] _tabMarkers = new Image[6];
        private readonly Button _refresh;
        private RectTransform _modal;
        private GuildClient _client;
        private GameObject _returnFocus, _modalReturnFocus;
        private int _memberPage;
        private bool _onlineOnly;
        private string _memberSearch = "";
        // 成员页的第二个视图(入帮申请审批)。换帮、换角都要清零,否则会带着上一个帮会的翻页进来。
        private bool _showApplications;
        private int _applicationPage;
        private ulong _guildId;
        // 商店页的分类(1 修行补给 / 2 帮会珍藏 / 3 节庆好礼)与翻页。换帮、换角都要复位。
        private int _shopCategory = 1, _shopPage;
        // 捐献页 / 商店页自动拉取的"已经拉过一次"标志:没有快照或快照过时时进页面只自动拉一次,
        // 失败了由玩家点刷新,不在每帧 Render 里反复重发。隔离期间清零,重连后允许再拉一次。
        private bool _autoDonations, _autoShop;

        public GuildWindow(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateStretch("GuildWindow", parent, Vector4.zero);
            _root.gameObject.SetActive(false);
            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .10f, .08f, .68f); shade.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            _frame = QdaoUguiFactory.CreateCenteredRect("GuildFrame", _root, 2160, 924);
            _frameInput = _frame.gameObject.AddComponent<CanvasGroup>();
            Art(_frame, "window_frame", 0, 0, 2160, 924);
            Art(_frame, "title_plate", 752.5f, -34, 655, 110, true);
            Heading(_frame, "帮 会", 882, -21, 396, 88, 48, Cream, alignment: TextAlignmentOptions.Center);
            Text(_frame, "五行奇谈 · 同道成一家", 102, 35, 620, 48, 28, Muted);
            _summary = Text(_frame, "结一方同道 · 守一盏人间灯", 1460, 35, 500, 48, 26, Muted,
                alignment: TextAlignmentOptions.MidlineRight);
            string[] titles = { "帮会总览", "帮会成员", "帮会排行", "帮会捐献", "帮会活动", "帮会商店" };
            for (int i = 0; i < _tabs.Length; i++)
            {
                GuildPage page = (GuildPage)i;
                _tabs[i] = NamedButton(_frame, "GuildTab_" + page, titles[i],
                    100 + i * 330, 108, 310, 72, () => Show(page), fontSize: 31);
                _tabMarkers[i] = Line(_tabs[i].transform, "SelectedTabMarker", 133, 62, 44, 3);
                _tabMarkers[i].color = QdaoUguiTheme.Html("#E3C681");
            }
            NamedButton(_frame, "CloseGuild", "", 2035, -14, 81, 80, Hide, key: "close_button");
            Art(_frame, "close_tassel", 2060, 66, 30, 81, true);
            Line(_frame, "TabsDivider", 100, 196, 1960);
            Line(_frame, "IdentityDivider", 520, 221, 1.5f, 579);
            Line(_frame, "FooterDivider", 100, 833, 1960);
            _rail = QdaoUguiFactory.CreateRect("GuildIdentity", _frame, 100, 214, 394, 610);
            _body = QdaoUguiFactory.CreateRect("GuildContent", _frame, 556, 214, 1508, 610);
            _status = Text(_frame, "", 104, 851, 1660, 51, 26, Muted, true);
            _refresh = NamedButton(_frame, "RefreshGuild", "刷新", 1850, 844, 210, 64, RefreshCurrentPage, true, fontSize: 28);
        }
        private bool Busy => _client == null || _client.Busy || _client.RequiresReconnect;
        /// <summary>右下角"刷新"只刷当前页的数据源;未入帮时捐献 / 商店页显示的是总览,刷的也是帮会快照。</summary>
        private void RefreshCurrentPage()
        {
            if (Busy) return;
            bool inGuild = _client?.Info != null;
            if (Page == GuildPage.Ranking) RankRequested?.Invoke(_client?.Rank?.Page ?? 1);
            else if (Page == GuildPage.Donate && inGuild) DonationsRequested?.Invoke();
            else if (Page == GuildPage.Shop && inGuild) ShopRequested?.Invoke();
            else RefreshRequested?.Invoke();
        }
        public void SetClient(GuildClient client)
        {
            ulong guildId = client?.Info?.GuildId ?? 0;
            if (_guildId != guildId)
            {
                CloseModal(); _memberPage = 0; _showApplications = false; _applicationPage = 0;
                _shopCategory = 1; _shopPage = 0; _autoDonations = _autoShop = false;
            }
            _guildId = guildId; _client = client;
            if (IsVisible) Render();
        }
        public void Show(GuildPage page = GuildPage.Overview)
        {
            bool opening = !IsVisible;
            if (opening) _returnFocus = EventSystem.current?.currentSelectedGameObject;
            CloseModal(); Page = page; _autoDonations = _autoShop = false; _root.gameObject.SetActive(true); Render();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_tabs[(int)Page].gameObject);
            if (page == GuildPage.Ranking && _client?.Rank == null && !Busy) RankRequested?.Invoke(1);
        }
        public void Hide()
        {
            CloseModal();
            bool wasVisible = IsVisible; _root.gameObject.SetActive(false);
            if (wasVisible && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_returnFocus != null && _returnFocus.activeInHierarchy ? _returnFocus : null);
            _returnFocus = null;
        }
        public void Back() { if (ModalVisible) CloseModal(); else Hide(); }
        public void ResetSession()
        {
            Hide(); _client = null; _guildId = 0;
            Page = GuildPage.Overview; _memberPage = 0; _onlineOnly = false; _memberSearch = "";
            _showApplications = false; _applicationPage = 0;
            _shopCategory = 1; _shopPage = 0; _autoDonations = _autoShop = false;
            Clear(_body); Clear(_rail);
        }
        private void Render()
        {
            string focusName = null;
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && (selected.transform.IsChildOf(_body) || selected.transform.IsChildOf(_rail))) focusName = selected.name;
            Clear(_rail); Clear(_body);
            _status.text = _client?.Status ?? "进入角色后，可在此与同道相聚。";
            _summary.text = _client?.Info == null ? "结一方同道 · 守一盏人间灯" :
                _client.Info.Members.Count + " / " + _client.Info.MaxMembers + " 位同道 · 相聚一堂";
            _refresh.interactable = !Busy;
            for (int i = 0; i < _tabs.Length; i++)
            {
                _tabs[i].image.sprite = Load(i == (int)Page ? "button_primary" : "button_secondary");
                _tabs[i].GetComponentInChildren<TMP_Text>().color = i == (int)Page ? Cream : Ink;
                _tabMarkers[i].gameObject.SetActive(i == (int)Page);
            }
            RenderIdentity();
            switch (Page)
            {
                case GuildPage.Overview: RenderOverview(); break;
                case GuildPage.Members: RenderMembers(); break;
                case GuildPage.Ranking: RenderRanking(); break;
                case GuildPage.Donate: RenderDonate(); break;
                case GuildPage.Activities: RenderUnavailable("帮会活动", "灯下团圆，山海同行。", new[] { "元宵灯会", "中秋团圆", "同道历练" },
                    new[] { "花灯待点亮，与同道共赏佳节。", "月圆时相聚，共赴团圆之约。", "集结帮会成员，一起踏上旅程。" }); break;
                case GuildPage.Shop: RenderShop(); break;
            }
            if (!string.IsNullOrEmpty(focusName) && EventSystem.current != null)
                foreach (var control in _frame.GetComponentsInChildren<Selectable>())
                    if (control.name == focusName && control.IsInteractable()) { EventSystem.current.SetSelectedGameObject(control.gameObject); break; }
            // 必须是最后一行:事件会同步走到 Request → Changed → SetClient → Render,
            // 嵌套的那次重建要发生在本次构建(含焦点恢复)全部完成之后。
            MaybeAutoRequest();
        }
        /// <summary>
        /// 进捐献页 / 商店页时,没有快照或快照已过时(帮会等级 / 本人帮贡变了、过了日 / 周切点,
        /// 见 GuildClient.DonationsNeedReload)就自动拉一次;每次进入最多一次(见 _autoDonations 注释)。
        /// 推送已排队重拉本页时让给 DrainQueued:它排在帮会快照之后,自己再拉一次会把"已入账 / 已发放"文案盖掉。
        /// </summary>
        private void MaybeAutoRequest()
        {
            if (_client == null || _client.RequiresReconnect) { _autoDonations = _autoShop = false; return; }
            ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bool donationsDue = _client.DonationsNeedReload(now), shopDue = _client.ShopNeedsReload(now);
            // 拿到新鲜快照才复位:它日后再过时,停在本页也能再自动拉一次 —— 升级、帮贡变化伴随回包 / 推送,
            // 经 Changed → Render 走到这里;跨日 / 周切点没有任何事件,由 GuildUiRoot 每帧调的 Tick 走到这里。
            // 仍过时就不复位 —— 拉取失败、或本地时钟比服务端快时拉回来的照样"过时",复位会让每次 Render / Tick 都重发。
            if (!donationsDue) _autoDonations = false;
            if (!shopDue) _autoShop = false;
            if (!IsVisible || _client.Busy || _client.Info == null) return;
            if (Page == GuildPage.Donate && donationsDue && !_autoDonations && !_client.DonationsQueued)
            { _autoDonations = true; DonationsRequested?.Invoke(); }
            else if (Page == GuildPage.Shop && shopDue && !_autoShop && !_client.ShopQueued)
            { _autoShop = true; ShopRequested?.Invoke(); }
        }
        /// <summary>
        /// 由 GuildUiRoot 每帧调用(排在 DrainQueued 之后)。停在捐献 / 商店页跨过 05:00 或周切点时,
        /// 服务端不推送、也没有回包,Render 不会发生,"今日 2/2" 与置灰的按钮会一直挂着;这里补判一次快照是否过时。
        /// 只做两次时间比较、不重建界面;不连发的约束(一份过时的快照最多自动拉一次)仍由 _autoDonations / _autoShop 保证。
        /// </summary>
        public void Tick()
        {
            if (!IsVisible || Busy || (Page != GuildPage.Donate && Page != GuildPage.Shop)) return;
            MaybeAutoRequest();
        }
        private void RenderIdentity()
        {
            QdaoUguiFactory.CreateImage("GuildCrest", _rail, 0, 4, 120, 120, GuildSprite("crest")).preserveAspect = true;
            var info = _client?.Info;
            var name = Heading(_rail, info?.Name ?? "静候同道", 136, 8, 258, 128, 32, wrap: true);
            name.name = "GuildName";
            if (info == null)
            {
                Line(_rail, "IdentityRule", 0, 149, 394);
                Text(_rail, "灯火可亲，同道可期。\n找到一处归属，\n携手闯荡山海。", 0, 187, 390, 176, 30, Muted, true);
                GuildIcon(_rail, "round_badge_lotus", 137, 361, 120);
                NamedButton(_rail, "BrowseGuilds", "寻找帮会", 22, 521, 350, 76, () => Show(GuildPage.Ranking), true);
                return;
            }
            Text(_rail, "帮会等级", 0, 144, 236, 46, 27, Muted);
            Text(_rail, "Lv." + info.Level, 248, 144, 146, 52, 34, Gold, alignment: TextAlignmentOptions.MidlineRight);
            Line(_rail, "LevelRule", 0, 202, 394);
            Text(_rail, "帮会编号", 0, 214, 394, 46, 30, Muted);
            Text(_rail, info.GuildId.ToString(), 0, 264, 394, 46, 28);
            // B3b 起 GuildInfo.leader_name 由服务端填;取名失败(fail-open)时为空,侧栏这一格照旧显示编号(§3.22)。
            Text(_rail, "帮主", 0, 314, 394, 46, 30, Muted);
            Text(_rail, string.IsNullOrWhiteSpace(info.LeaderName) ? info.LeaderId.ToString() : info.LeaderName, 0, 364, 394, 46, 28);
            Line(_rail, "LeaderRule", 0, 424, 394);
            Text(_rail, "我的身份", 0, 436, 228, 48, 27, Muted);
            Text(_rail, RoleName(_client.Role), 242, 436, 152, 48, 30, alignment: TextAlignmentOptions.MidlineRight);
            Text(_rail, "山海有归处，同道共此时。", 0, 490, 394, 46, 30, Muted);
            NamedButton(_rail, "GuildMembersEntry", "查看同道", 0, 546, 190, 64, () => Show(GuildPage.Members), fontSize: 26);
            NamedButton(_rail, "LeaveOrDisbandGuild", _client.IsLeader ? "解散帮会" : "退出帮会", 204, 546, 190, 64,
                () => Confirm(_client.IsLeader ? "确认解散帮会" : "确认退出帮会",
                    _client.IsLeader ? "解散后所有成员都将离开帮会，此操作无法恢复。" : "退出后将失去当前帮会身份。确认要离开同道吗？",
                    () => { if (_client.IsLeader) DisbandRequested?.Invoke(); else LeaveRequested?.Invoke(); }), enabled: !Busy, fontSize: 26);
        }
        private void RenderOverview()
        {
            var info = _client?.Info;
            if (info == null)
            {
                Heading(_body, "一盏灯火，一处归属", 24, 56, 1100, 88, 54);
                Text(_body, _client?.HasLoaded == true ? "你尚未加入帮会。可以浏览帮会排行，或创建自己的帮会。"
                    : _client?.RequiresReconnect == true ? GuildClient.RecoveryMessage : "正在等候帮会信息。若服务暂未响应，可点击右下角刷新。", 24, 178, 1090, 140, 32, Muted, true);
                Art(_body, "lantern", 1250, 54, 154, 275, true);
                NamedButton(_body, "OpenGuildRanking", "寻找帮会", 24, 354, 350, 88, () => Show(GuildPage.Ranking), true);
                NamedButton(_body, "OpenCreateGuild", "创建帮会", 412, 354, 350, 88, ShowCreate,
                    enabled: _client?.HasLoaded == true && !Busy);
                Line(_body, "EmptyStateRule", 24, 504, 1460);
                // MyApplications 为 null 只表示“还没拉过”,不能当成“没有申请”:此时仍显示原文案。
                var pending = _client?.MyApplications;
                Text(_body, pending != null && pending.Count > 0
                    ? "已提交 " + pending.Count + " 份入帮申请，等待审批中。"
                    : "成员信息、公告和排行将随帮会更新。", 24, 532, 1460, 55, 28, Muted);
                return;
            }
            Heading(_body, "同道相聚", 0, 0, 950, 64, 43);
            Text(_body, "一同修行，一同守护这方灯火。", 0, 68, 1440, 46, 27, Muted);
            int online = 0;
            ulong total = 0, balance = 0;
            foreach (var member in info.Members)
            {
                if (member.Online) online++;
                if (member.PlayerId == _client.PlayerId) { total = member.ContributionTotal; balance = member.ContributionBalance; }
            }
            // 四列:可用帮贡(能花的)与累计帮贡(只增,排名用)放在同一格,顺序与标签一致。
            OverviewMetric("GuildMemberCount", "帮会成员", info.Members.Count + " / " + info.MaxMembers, 0);
            OverviewMetric("GuildOnlineCount", "当前在线", online + " 位", 377);
            OverviewMetric("GuildFunds", "帮会资金", GuildClient.FormatAmount(info.Funds), 754);
            OverviewMetric("GuildMyContribution", "可用 / 累计帮贡",
                GuildClient.FormatAmount(balance) + " / " + GuildClient.FormatAmount(total), 1131, 34);
            Line(_body, "MetricsRule", 0, 238, 1508);
            GuildIcon(_body, "notice", 0, 259, 47);
            Heading(_body, "帮会公告", 66, 250, 590, 63, 36);
            // 资金够不够交给服务端判(见 GuildClient.CanUpgrade),这里只按职位与是否满级收起按钮;右端 938,不压"查看全文"。
            if (_client.IsOfficerOrLeader)
                NamedButton(_body, "UpgradeGuild", info.UpgradeCostFunds == 0 ? "已满级" : "升级帮会", 680, 250, 258, 64,
                    ShowUpgrade, enabled: _client.CanUpgrade && !Busy, fontSize: 27);
            NamedButton(_body, "ReadGuildAnnouncement", "查看全文", 958, 250, 252, 64, ShowReadAnnouncement, fontSize: 27);
            if (_client.CanEditAnnouncement)
                NamedButton(_body, "EditGuildAnnouncement", "编辑公告", 1230, 250, 278, 64, ShowAnnouncement, enabled: !Busy, fontSize: 27);
            GuildField(_body, "GuildAnnouncementPanel", 0, 326, 1508, 132);
            Text(_body, string.IsNullOrWhiteSpace(info.Announcement) ? "帮会尚未发布公告。愿同道相伴，诸事顺遂。" : info.Announcement,
                28, 340, 1452, 104, 30, wrap: true);
            Heading(_body, "同道携手 · 帮会事务", 0, 476, 1508, 48, 32, Muted);
            OverviewEntry("GuildOverviewMembers", "帮会成员", "crest", GuildPage.Members, 0);
            OverviewEntry("GuildOverviewRanking", "帮会排行", "round_badge_compass", GuildPage.Ranking, 304);
            OverviewEntry("GuildOverviewDonate", "帮会捐献", "furnace", GuildPage.Donate, 608);
            OverviewEntry("GuildOverviewActivities", "帮会活动", "round_badge_lotus", GuildPage.Activities, 912);
            OverviewEntry("GuildOverviewShop", "帮会商店", "round_badge_pagoda", GuildPage.Shop, 1216);
        }
        private void OverviewMetric(string name, string label, string value, float x, int valueFontSize = 43)
        {
            if (x > 0) Line(_body, name + "Divider", x - 24, 127, 1.5f, 95);
            Text(_body, label, x, 118, 350, 46, 30, Muted);
            var text = Text(_body, value, x, 164, 350, 64, valueFontSize);
            // 帮贡 / 资金涨到七八位数时一格放不下:只在超宽时往下缩,下限仍是 Text() 的 30 号。
            text.enableAutoSizing = true; text.fontSizeMin = 30; text.fontSizeMax = valueFontSize;
            text.name = name;
        }
        private void OverviewEntry(string name, string label, string icon, GuildPage page, float x)
        {
            var button = NamedButton(_body, name, "", x, 533, 292, 75, () => Show(page));
            GuildIcon(button.transform, icon, 14, 11, 54);
            var caption = Text(button.transform, label, 80, 0, 199, 75, 32, alignment: TextAlignmentOptions.Center);
            QdaoUguiTypography.ApplyButton(caption);
        }
        private void RenderMembers()
        {
            var info = _client?.Info;
            if (info == null) { RenderOverview(); return; }
            // 身份被降为帮众后申请视图立刻失效(服务端也会拒),自动落回成员列表。
            bool applications = _showApplications && _client.IsOfficerOrLeader;
            RenderMemberToolbar(info, applications);
            if (applications) RenderApplications();
            else RenderMemberList(info);
        }
        private void RenderMemberToolbar(GuildInfo info, bool applications)
        {
            NamedButton(_body, "OnlineGuildMembers", _onlineOnly ? "已选：仅在线" : "显示全部成员", 0, 0, 295, 66,
                () => { _onlineOnly = !_onlineOnly; _memberPage = 0; Render(); }, _onlineOnly, fontSize: 27);
            // Text() 会把字号抬到至少 30(保 1280 缩放下可读),传 26 也不会变小;8 个字约 240 宽,
            // 所以标签从 230 放宽到 260,输入框右移 30、收窄 30,右缘 1026 不动。
            Text(_body, "按名字或编号查找", 316, 3, 260, 58, 27, Muted);
            var search = Input(_body, "GuildMemberSearch", 586, 0, 440, 66, "输入名字或编号", 20);
            search.SetTextWithoutNotify(_memberSearch);
            NamedButton(_body, "SearchGuildMembers", "查找", 1040, 0, 196, 66,
                () => { _memberSearch = search.text.Trim(); _memberPage = 0; Render(); }, fontSize: 27);
            if (!_client.IsOfficerOrLeader) return;
            // 角标数字来自 GetPlayerGuild / 写响应带回的快照;收到 ApplicationReceived 推送且列表不可见时,
            // GuildClient.DrainQueued 会改发一次 Refresh 让这个数字自己更新(§17.3)。
            NamedButton(_body, "GuildApplicationsToggle",
                applications ? "返回成员" : "入帮申请 " + info.PendingApplicationCount, 1252, 0, 256, 66,
                () =>
                {
                    _showApplications = !_showApplications;
                    if (_showApplications) _applicationPage = 0;
                    Render();
                    // 切进申请视图才拉列表:关着窗口或只看成员时不该占用唯一的在途请求位。
                    if (_showApplications) ApplicationsRequested?.Invoke();
                }, enabled: !Busy, fontSize: 27);
        }
        private void RenderMemberList(GuildInfo info)
        {
            var members = new List<GuildMember>();
            string query = SearchKey(_memberSearch);
            foreach (var member in info.Members)
                if ((!_onlineOnly || member.Online) && (query.Length == 0 || MatchesMemberSearch(member, query)))
                    members.Add(member);
            members.Sort((a, b) => { int role = b.Role.CompareTo(a.Role); return role != 0 ? role : a.PlayerId.CompareTo(b.PlayerId); });
            int pages = Math.Max(1, (members.Count + MembersPerPage - 1) / MembersPerPage);
            _memberPage = Math.Max(0, Math.Min(_memberPage, pages - 1));
            if (members.Count == 0)
                Text(_body, "没有符合条件的同道。", 30, 210, 1400, 100, 38, Muted, alignment: TextAlignmentOptions.Center);
            for (int i = 0; i < MembersPerPage && _memberPage * MembersPerPage + i < members.Count; i++)
            {
                var member = members[_memberPage * MembersPerPage + i];
                float y = 86 + i * 86;
                ulong id = member.PlayerId;
                // 确认框与列表用同一份文案;空名兜底规则只在 MemberDisplayName 一处。
                string display = MemberDisplayName(member);
                GuildField(_body, "GuildListRow", 0, y, 1508, 78);
                Text(_body, display + (id == _client.PlayerId ? "（我）" : ""), 90, y + 12, 420, 56, 29);
                Text(_body, RoleName(member.Role), 524, y + 12, 150, 56, 29, Gold);
                Text(_body, "贡献  " + member.ContributionTotal, 686, y + 12, 260, 56, 28, Muted);
                Text(_body, member.Online ? "在线" : "离线", 958, y + 10, 120, 56, 28, member.Online ? Ink : Muted);
                if (_client.CanAssignRoles && id != _client.PlayerId)
                {
                    if (member.Role == GuildRoles.Member)
                        NamedButton(_body, "GuildMemberPromote_" + id, "任长老", 1096, y + 9, 128, 60,
                            () => Confirm("确认任命长老", "任命 " + display + " 为长老。当前长老 " + info.OfficerCount + "/" + info.MaxOfficers + "。",
                                () => RoleRequested?.Invoke(id, GuildRoles.Officer)),
                            enabled: !Busy && info.OfficerCount < info.MaxOfficers, fontSize: 26);
                    else if (member.Role == GuildRoles.Officer)
                        NamedButton(_body, "GuildMemberDemote_" + id, "免长老", 1096, y + 9, 128, 60,
                            () => Confirm("确认免去长老", display + " 将恢复为帮众。",
                                () => RoleRequested?.Invoke(id, GuildRoles.Member)),
                            enabled: !Busy, fontSize: 26);
                    NamedButton(_body, "GuildMemberTransfer_" + id, "转让", 1232, y + 9, 128, 60,
                        () => Confirm("确认转让帮主", "转让后 " + display + " 成为帮主，你将成为长老（长老已满则为帮众）。此操作无法撤回。",
                            () => TransferRequested?.Invoke(id)), enabled: !Busy, fontSize: 26);
                }
                // 服务端仍按 MySQL 里的职位复核;这里只是提前收起点不动的按钮。
                if (_client.CanKick(member))
                    NamedButton(_body, "GuildMemberKick_" + id, "请离", 1368, y + 9, 128, 60,
                        () => Confirm("确认请离成员", display + " 将被请离帮会。", () => KickRequested?.Invoke(id)),
                        enabled: !Busy, fontSize: 26);
            }
            // 按钮不出现或点不动时要说清原因,别让玩家以为界面坏了。三种情形互斥,只会出现一条。
            string hint = _client.CanAssignRoles && info.OfficerCount >= info.MaxOfficers
                ? "长老已满（" + info.OfficerCount + "/" + info.MaxOfficers + "），需先免去现有长老才能任命。"
                : _client.IsOfficerOrLeader && !_client.CanAssignRoles
                    ? "长老可请离帮众；任免长老与转让帮主仅帮主可用。"
                    : !_client.IsOfficerOrLeader ? "帮众可查看同道名册；管理操作仅帮主或长老可用。" : null;
            if (hint != null) Text(_body, hint, 0, 545, 800, 64, 28, Muted).name = "GuildMemberActionHint";
            Pager(_body, "Members", _memberPage + 1, pages, delta => { _memberPage += delta; Render(); }, true);
        }
        /// <summary>成员页的第二个视图：本帮待审入帮申请，仅长老 / 帮主可见。</summary>
        private void RenderApplications()
        {
            var applicants = _client.Applicants;
            int count = applicants?.Count ?? 0;
            if (applicants == null)
                Text(_body, "正在读取入帮申请…", 30, 210, 1400, 100, 38, Muted, alignment: TextAlignmentOptions.Center);
            else if (count == 0)
                Text(_body, "暂无待审申请。", 30, 210, 1400, 100, 38, Muted, alignment: TextAlignmentOptions.Center);
            int pages = Math.Max(1, (count + MembersPerPage - 1) / MembersPerPage);
            _applicationPage = Math.Max(0, Math.Min(_applicationPage, pages - 1));
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            for (int i = 0; i < MembersPerPage && _applicationPage * MembersPerPage + i < count; i++)
            {
                var applicant = applicants[_applicationPage * MembersPerPage + i];
                float y = 86 + i * 86;
                ulong id = applicant.PlayerId;
                GuildField(_body, "GuildListRow", 0, y, 1508, 78);
                Text(_body, MemberDisplayName(id, applicant.Name), 90, y + 12, 520, 56, 29);
                Text(_body, applicant.Online ? "在线" : "离线", 630, y + 12, 140, 56, 28, applicant.Online ? Ink : Muted);
                // expire_ms 是服务端时钟,本地时钟有偏差也只影响这一行的展示;服务端过期判定与它无关。
                // 不足一小时按 1 小时显示,避免出现“剩余 0 小时”。
                Text(_body, "剩余 " + Math.Max(1, (int)Math.Ceiling(((long)applicant.ExpireMs - nowMs) / 3600000.0)) + " 小时",
                    790, y + 12, 280, 56, 28, Muted);
                // 审批可反复进行(拒绝后对方还能再申请),不加确认框。
                NamedButton(_body, "GuildApplicationReject_" + id, "拒绝", 1100, y + 7, 190, 64,
                    () => ReviewRequested?.Invoke(id, false), enabled: !Busy, fontSize: 27);
                NamedButton(_body, "GuildApplicationApprove_" + id, "同意", 1306, y + 7, 190, 64,
                    () => ReviewRequested?.Invoke(id, true), true, !Busy, fontSize: 27);
            }
            Pager(_body, "Applications", _applicationPage + 1, pages, delta => { _applicationPage += delta; Render(); }, true);
        }
        private void RenderRanking()
        {
            var rank = _client?.Rank;
            Heading(_body, "同道云集 · 本区帮会排行", 16, 0, 1440, 68, 39);
            if (rank == null || rank.Entries.Count == 0)
                Text(_body, _client?.RequiresReconnect == true ? GuildClient.RecoveryMessage : Busy ? "正在等待帮会排行…" : "当前暂无排行，点击刷新或稍后再来。", 22, 240, 1440, 120, 34, Muted,
                    alignment: TextAlignmentOptions.Center);
            if (rank != null)
                for (int i = 0; i < rank.Entries.Count && i < 5; i++)
                {
                    var entry = rank.Entries[i];
                    float y = 86 + i * 86;
                    GuildField(_body, "GuildListRow", 0, y, 1508, 78);
                    Text(_body, entry.Rank.ToString("D2"), 38, y + 12, 96, 56, 31, Gold);
                    Text(_body, entry.Name, 135, y + 12, 530, 56, 31);
                    Text(_body, "Lv." + entry.Level + " · " + entry.MemberCount + " 人", 690, y + 12, 320, 56, 28, Muted);
                    Text(_body, "积分 " + entry.Score, 1020, y + 12, 260, 56, 26, Muted);
                    ulong id = entry.GuildId; string name = entry.Name;
                    // MyApplications 未加载时 HasApplied 恒为 false,按钮先显示“申请”;Browse 的回调已经
                    // 排队拉本人申请(§17.4),下一帧 DrainQueued 拉回来后这里会自己换成“撤回申请”。
                    bool applied = _client?.HasApplied(id) == true;
                    Action click;
                    if (applied)
                        click = () => Confirm("确认撤回申请", "帮会名称：" + name + "\n撤回后可重新申请。",
                            () => CancelApplicationRequested?.Invoke(id));
                    else
                        click = () => Confirm("确认申请加入", "帮会名称：" + name + "\n申请需帮主或长老审批，逾期未处理将自动失效。",
                            () => ApplyRequested?.Invoke(id));
                    NamedButton(_body, "ApplyGuild_" + id,
                        _client?.Info?.GuildId == id ? "我的帮会" : applied ? "撤回申请" : "申请",
                        1280, y + 7, 210, 64, click,
                        true, enabled: _client?.HasLoaded == true && _client.Info == null && !Busy, fontSize: 27);
                }
            int page = (int)(rank?.Page ?? 1), pageSize = (int)Math.Max(1, rank?.PageSize ?? 5);
            int pages = Math.Max(1, (int)(((rank?.TotalCount ?? 0) + (uint)pageSize - 1) / (uint)pageSize));
            Pager(_body, "Ranking", page, pages, delta => RankRequested?.Invoke((uint)Math.Max(1, page + delta)), !Busy);
        }
        // ── 捐献(B5,服务端 05-economy.md §5.35.1)──────────────────────────
        // 几何沿用"三面板":银两 / 灵石 / 建设物资。GuildUiArt.Text 把字号抬到至少 30,所以每个选项只排两行:
        // 第一行 名称:花费 + 右侧今日次数,第二行 帮贡与资金;完整数字写在确认框里。
        // 单行正文框高一律 ≥ 46:QdaoBody(Noto)30 号一行要 (74.24+18.432)×30/64 ≈ 43.4,框比它矮时
        // CreateText 默认的 Ellipsis 在第一个字就判溢出,整行一个字都不出(同 TeamWindow.Label 的注释)。
        private void RenderDonate()
        {
            var info = _client?.Info;
            if (info == null) { RenderOverview(); return; }
            Heading(_body, "帮会捐献", 12, 0, 1450, 64, 42);
            Text(_body, "聚沙成塔，同心兴帮。每日 05:00 重置次数。", 12, 80, 1450, 58, 30, Muted);
            var donations = _client.Donations;
            string[] titles = { "银两捐献", "灵石捐献", "建设物资" };
            string[] icons = { "furnace", "crest", "scroll" };
            for (int i = 0; i < 3; i++)
            {
                float x = i * 512;
                GuildField(_body, "GuildDonatePanel_" + i, x, 166, 484, 395);
                GuildIcon(_body, icons[i], x + 24, 186, 72);
                Heading(_body, titles[i], x + 110, 186, 350, 72, 34);
                if (i == 2)
                {
                    // 道具捐献放 v1.1(契约 §0-1);按钮名沿用旧的 GuildUnavailable_Donate_2,离线截图与测试都认它。
                    Text(_body, "物资捐献将在后续版本开放。", x + 34, 270, 416, 120, 28, Muted, true);
                    NamedButton(_body, "GuildUnavailable_Donate_2", "暂未开放", x + 66, 484, 352, 64, null, enabled: false, fontSize: 28);
                    continue;
                }
                if (donations != null) RenderDonateOptions(donations, (uint)i, x);
            }
            string footer;
            // 隔离(请求超时,待重新登录)时没有请求在途,不能写"读取中";判断顺序同排行页、总览空态。
            if (donations == null)
                footer = _client.RequiresReconnect ? GuildClient.RecoveryMessage
                    : _client.Busy ? "正在读取捐献信息…" : "点击右下角刷新读取捐献信息。";
            // 带上第一笔的暂时原因,不许诺"自动入账":结算中的单也可能以未成功 / 撤销收尾(余额不足未落盘时多半如此),
            // 写法与状态栏同一口径(GuildClient.DonationPendingReasonText)。
            else if (donations.PendingDonations.Count > 0)
                footer = donations.PendingDonations.Count + " 笔捐献结算中："
                    + GuildClient.DonationPendingReasonText(donations.PendingDonations[0].ReasonTipId);
            else if (donations.RecentResults.Count > 0)
                footer = "最近一笔：" + GuildClient.DonationResultText(donations.RecentResults[0]);
            else
                footer = "可用帮贡 " + GuildClient.FormatAmount(donations.ContributionBalance)
                    + "（累计 " + GuildClient.FormatAmount(donations.ContributionTotal) + "） · 帮会资金 " + GuildClient.FormatAmount(info.Funds);
            // 与 RenderUnavailable 的页脚同几何:底边 610 正好贴到正文区底部。
            FooterText(footer, 14, 564, 1480, 46).name = "GuildDonateFooter";
        }
        /// <summary>一个货币面板里的选项(服务端按 donate_id 升序下发,同货币 ≤ 2 行由配表启动校验保证)。</summary>
        private void RenderDonateOptions(GetGuildDonateOptionsResponse donations, uint currencyType, float x)
        {
            var options = new List<GuildDonateOptionView>();
            foreach (var option in donations.Options)
                if (option.CurrencyType == currencyType && options.Count < 2) options.Add(option);
            string currency = CurrencyName(currencyType);
            for (int j = 0; j < options.Count; j++)
            {
                var option = options[j];
                // 两行各 46 高、行距 48;第二个选项底边 262+106+48+46 = 462,不压 484 的按钮。
                float y0 = 262 + j * 106;
                Text(_body, option.Name + "：" + GuildClient.FormatAmount(option.CostAmount), x + 24, y0, 300, 46, 30);
                Text(_body, option.Unlocked ? "今日 " + option.UsedToday + "/" + option.DailyLimit : "Lv." + option.MinGuildLevel + " 解锁",
                    x + 310, y0, 150, 46, 30, option.Unlocked ? Muted : Gold, alignment: TextAlignmentOptions.MidlineRight);
                Text(_body, "帮贡 +" + option.ContributionGain + " · 资金 +" + GuildClient.FormatAmount(option.FundsGain),
                    x + 24, y0 + 48, 436, 46, 30, Muted);
                uint id = option.DonateId;
                string description = option.Name + "：消耗 " + GuildClient.FormatAmount(option.CostAmount) + " " + currency
                    + "，获得帮贡 +" + option.ContributionGain + "，帮会资金 +" + GuildClient.FormatAmount(option.FundsGain) + "。";
                // 一个选项独占宽按钮;两个并排,标签取名称去掉货币前缀("小捐" / "大捐")。
                string label = options.Count == 1 ? "捐献" : option.Name.Length > 2 ? option.Name.Substring(2) : option.Name;
                bool enabled = option.Unlocked && option.UsedToday < option.DailyLimit && !Busy;
                float bx = options.Count == 1 ? x + 66 : x + 24 + j * 224, bw = options.Count == 1 ? 352 : 212;
                NamedButton(_body, "GuildDonate_" + id, label, bx, 484, bw, 64,
                    () => Confirm("确认捐献", description, () => DonateRequested?.Invoke(id)), true, enabled, fontSize: 28);
            }
        }
        /// <summary>与服务端 kCurrencyGold(0) / kCurrencyDiamond(1) 同序,契约 §0-1 的叫法。</summary>
        public static string CurrencyName(uint currencyType) => currencyType switch { 0 => "银两", 1 => "灵石", _ => "货币" };

        // ── 商店(B5,§5.35.2)────────────────────────────────────────────────
        private void RenderShop()
        {
            var info = _client?.Info;
            if (info == null) { RenderOverview(); return; }
            var shop = _client.Shop;
            string[] categories = { "修行补给", "帮会珍藏", "节庆好礼" };
            for (int c = 1; c <= categories.Length; c++)
            {
                int category = c;
                NamedButton(_body, "GuildShopCategory_" + c, categories[c - 1], (c - 1) * 315, 0, 295, 66,
                    () => { _shopCategory = category; _shopPage = 0; Render(); }, c == _shopCategory, fontSize: 27);
            }
            // 余额以商店快照(MySQL 直读)为准;还没拉到时先显示帮会快照里的,免得这一格空着。
            ulong balance = shop?.ContributionBalance ?? MyContributionBalance(info);
            Text(_body, "可用帮贡 " + GuildClient.FormatAmount(balance), 960, 3, 548, 58, 30, Gold,
                alignment: TextAlignmentOptions.MidlineRight).name = "GuildShopBalance";
            if (shop == null)
            {
                // 同捐献页:隔离时没有请求在途,提示重新登录而不是"读取中"。
                Text(_body, _client.RequiresReconnect ? GuildClient.RecoveryMessage
                    : _client.Busy ? "正在读取帮会商店…" : "点击右下角刷新读取帮会商店。", 30, 210, 1400, 100, 38, Muted,
                    alignment: TextAlignmentOptions.Center).name = "GuildShopPlaceholder";
                return;
            }
            // 服务端已按 category、goods_id 排好序,这里只按分类筛。
            var goods = new List<GuildShopGoodsView>();
            foreach (var item in shop.Goods) if (item.Category == _shopCategory) goods.Add(item);
            int pages = Math.Max(1, (goods.Count + GoodsPerPage - 1) / GoodsPerPage);
            _shopPage = Math.Max(0, Math.Min(_shopPage, pages - 1));
            if (goods.Count == 0)
                Text(_body, "本类暂无可兑换的物品。", 30, 210, 1400, 100, 38, Muted, alignment: TextAlignmentOptions.Center);
            string icon = _shopCategory == 1 ? "pill" : _shopCategory == 2 ? "talisman" : "scroll";
            for (int k = 0; k < GoodsPerPage && _shopPage * GoodsPerPage + k < goods.Count; k++)
            {
                var item = goods[_shopPage * GoodsPerPage + k];
                float x = k % 3 * 512, y = 86 + k / 3 * 230;
                uint id = item.GoodsId;
                GuildField(_body, "GuildShopCard_" + id, x, y, 484, 218);
                GuildIcon(_body, icon, x + 20, y + 20, 72);
                // 单行正文框高 ≥ 46(见 RenderDonate 上方注释)。纵向 8..56 / 56..102 / 102..148 / 154..200,
                // 底边 200 < 卡高 218;帮贡与限购两行右缘 x+290,不压 x+300 起的兑换键。
                Text(_body, item.Name, x + 108, y + 8, 356, 48, 30);
                Text(_body, "每份 ×" + item.ItemCount, x + 108, y + 56, 356, 46, 30, Muted);
                Text(_body, "帮贡 " + GuildClient.FormatAmount(item.CostContribution), x + 20, y + 102, 270, 46, 30, Gold);
                Text(_body, ShopLimitText(item), x + 20, y + 154, 270, 46, 30, Muted);
                bool enabled = item.Unlocked && (item.LimitPeriod == 0 || item.UsedCount < item.LimitCount)
                    && balance >= item.CostContribution && !Busy;
                string name = item.Name; ulong cost = item.CostContribution; uint count = item.ItemCount;
                // 本期每次 1 份;多份兑换(max_buy_count)留给后续的数量选择器。
                NamedButton(_body, "GuildShopBuy_" + id, "兑换", x + 300, y + 146, 164, 58,
                    () => Confirm("确认兑换", "消耗帮贡 " + GuildClient.FormatAmount(cost) + " 兑换 " + name + " ×" + count + "？",
                        () => ShopBuyRequested?.Invoke(id)), true, enabled, fontSize: 27);
            }
            Pager(_body, "Shop", _shopPage + 1, pages, delta => { _shopPage += delta; Render(); }, true);
            string footer = shop.PendingOrders.Count > 0
                ? "待发放 " + shop.PendingOrders.Count + " 单：" + GuildClient.AssetReasonText(shop.PendingOrders[0].ReasonTipId)
                : shop.RecentOrders.Count > 0 ? "最近一单：" + ShopFooterResultText(shop.RecentOrders[0]) : "";
            FooterText(footer, 0, 545, 800, 64).name = "GuildShopFooter";
        }
        /// <summary>
        /// 商店页脚的"最近一单"。页脚宽 800(右侧 820 起是翻页键),30 号字一行只放得下约 26 个汉字,
        /// 而"兑换失败，帮贡与限购已退回：" + 原因、部分发放的整句加上前缀都超过 800,会被省略号截掉关键的尾巴。
        /// 这两种只写结论;完整文案(含拒绝原因)由 GuildClient.RefreshShop 在没有待发放时写进底部状态栏。
        /// 设计 §5.35.2 按 26 号字写的是 ShopResultText 全文,GuildUiArt.Text 把字号抬到 30 后放不下。
        /// </summary>
        public static string ShopFooterResultText(GuildShopOrderView order)
        {
            if (order?.Status == GuildAssetOrderStatus.Rejected) return "兑换失败，帮贡与限购已退回";
            if (order?.Status == GuildAssetOrderStatus.AppliedPartial) return "只发放了一部分，客服将补偿";
            return GuildClient.ShopResultText(order);
        }
        /// <summary>限购文案:未解锁先说等级,再按周期说用量。</summary>
        public static string ShopLimitText(GuildShopGoodsView item)
        {
            if (!item.Unlocked) return "帮会 Lv." + item.RequiredGuildLevel + " 解锁";
            return item.LimitPeriod switch
            {
                0 => "不限购",
                1 => "今日 " + item.UsedCount + "/" + item.LimitCount,
                2 => "本周 " + item.UsedCount + "/" + item.LimitCount,
                _ => "限购 " + item.UsedCount + "/" + item.LimitCount,
            };
        }
        private ulong MyContributionBalance(GuildInfo info)
        {
            foreach (var member in info.Members) if (member.PlayerId == _client.PlayerId) return member.ContributionBalance;
            return 0;
        }
        /// <summary>
        /// 页脚一行:超宽时以省略号收尾,不压到右侧的翻页键。省略号只是兜底 —— 两页页脚的文案都按 30 号字控制在框宽以内
        /// (EconomyFootersFitWithoutEllipsis 逐个原因码核对);商店页脚只写结论的那几种,全文见状态栏(ShopFooterResultText)。
        /// </summary>
        private TextMeshProUGUI FooterText(string value, float x, float y, float w, float h)
        {
            var text = Text(_body, value, x, y, w, h, 30, Muted);
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }
        private void ShowUpgrade()
        {
            var info = _client?.Info;
            if (Busy || info == null || !_client.CanUpgrade) return;
            // 文案按打开这一刻的等级写死,确认时也带这一刻的等级:确认框开着时推送可能把快照刷成新等级
            // (别的长老先升了),按点确认那一刻的等级发会按下一级的花费再扣一次(GuildClient.Upgrade 注释)。
            uint level = info.Level;
            Confirm("升级帮会", "需要帮会资金 " + GuildClient.FormatAmount(info.UpgradeCostFunds) + "（当前 " + GuildClient.FormatAmount(info.Funds)
                + "）。\n升级后帮会升至 Lv." + (level + 1) + "，成员上限提升。", () => UpgradeRequested?.Invoke(level));
        }
        private void RenderUnavailable(string title, string subtitle, string[] titles, string[] descriptions)
        {
            Heading(_body, title, 12, 0, 1450, 64, 42);
            Text(_body, subtitle, 12, 80, 1450, 58, 30, Muted);
            for (int i = 0; i < 3; i++)
            {
                float x = i * 512;
                GuildField(_body, "GuildUnavailablePanel_" + i, x, 166, 484, 395);
                string[] icons = Page == GuildPage.Donate ? new[] { "furnace", "crest", "scroll" } :
                    Page == GuildPage.Activities ? new[] { "lantern", "crest", "sword" } : new[] { "pill", "talisman", "scroll" };
                GuildIcon(_body, icons[i], x + 186, 190, 112);
                Heading(_body, titles[i], x + 30, 299, 424, 66, 36, alignment: TextAlignmentOptions.Center);
                Text(_body, descriptions[i], x + 34, 377, 416, 94, 28, Muted, true);
                NamedButton(_body, "GuildUnavailable_" + Page + "_" + i, "暂未开放", x + 66, 484, 352, 64, null,
                    enabled: false, fontSize: 28);
            }
            Text(_body, "开放后可在此查看规则与参与条件。", 14, 564, 1480, 46, 30, Muted);
        }
        private void Pager(UnityEngine.Transform parent, string prefix, int page, int pages, Action<int> changed, bool enabled)
        {
            NamedButton(parent, prefix + "Previous", "上一页", 820, 545, 210, 64, () => changed(-1), enabled: enabled && page > 1, fontSize: 27);
            Text(parent, page + " / " + pages, 1046, 545, 212, 64, 29, Muted, alignment: TextAlignmentOptions.Center);
            NamedButton(parent, prefix + "Next", "下一页", 1274, 545, 234, 64, () => changed(1), enabled: enabled && page < pages, fontSize: 27);
        }
        private void ShowCreate()
        {
            if (Busy || _client?.HasLoaded != true || _client.Info != null) return;
            var frame = Modal("创建帮会", "为同道起一个名字，点亮属于你们的灯火。");
            Heading(frame, "帮会名称", 66, 212, 970, 54, 30);
            var input = Input(frame, "NewGuildName", 66, 280, 968, 82, "1–24 字帮会名称", GuildClient.MaxNameLength);
            var submit = NamedButton(frame, "ConfirmCreateGuild", "确认创建", 692, 460, 340, 82,
                () => { string name = input.text.Trim(); if (name.Length == 0) return; CloseModal(); CreateRequested?.Invoke(name); },
                true, enabled: false);
            input.onValueChanged.AddListener(value => submit.interactable = value.Trim().Length > 0);
            NamedButton(frame, "CancelCreateGuild", "取消", 66, 460, 300, 82, CloseModal);
            EventSystem.current?.SetSelectedGameObject(input.gameObject);
        }
        private void ShowReadAnnouncement()
        {
            if (_client?.Info == null) return;
            var frame = Modal("帮会公告", "上下滚动可查看完整内容。");
            var input = Input(frame, "ReadOnlyGuildAnnouncement", 66, 228, 968, 196, "", 0);
            input.lineType = TMP_InputField.LineType.MultiLineNewline;
            input.readOnly = true;
            input.textComponent.textWrappingMode = TextWrappingModes.Normal;
            input.textComponent.alignment = TextAlignmentOptions.TopLeft;
            input.SetTextWithoutNotify(string.IsNullOrWhiteSpace(_client.Info.Announcement) ? "帮会尚未发布公告。" : _client.Info.Announcement);
            var close = NamedButton(frame, "CloseReadGuildAnnouncement", "知道了", 692, 460, 340, 82, CloseModal, true);
            EventSystem.current?.SetSelectedGameObject(input.gameObject);
        }
        private void ShowAnnouncement()
        {
            if (Busy || _client?.CanEditAnnouncement != true) return;
            var frame = Modal("编辑帮会公告", "帮主与长老可编辑公告，最多 " + GuildClient.MaxAnnouncementChars + " 字。");
            Heading(frame, "公告内容", 66, 192, 968, 48, 29);
            var input = Input(frame, "GuildAnnouncementInput", 66, 247, 968, 176, "写下给同道的话", GuildClient.MaxAnnouncementChars);
            input.lineType = TMP_InputField.LineType.MultiLineNewline;
            input.textComponent.textWrappingMode = TextWrappingModes.Normal;
            input.textComponent.alignment = TextAlignmentOptions.TopLeft;
            input.SetTextWithoutNotify(_client.Info.Announcement);
            NamedButton(frame, "SaveGuildAnnouncement", "保存公告", 692, 460, 340, 82,
                () => { string text = input.text; CloseModal(); AnnouncementRequested?.Invoke(text); }, true);
            NamedButton(frame, "CancelGuildAnnouncement", "取消", 66, 460, 300, 82, CloseModal);
            EventSystem.current?.SetSelectedGameObject(input.gameObject);
        }
        private void Confirm(string title, string description, Action action)
        {
            if (Busy) return;
            var frame = Modal(title, description);
            Text(frame, "请确认后再继续。", 66, 270, 968, 96, 31, Muted, true);
            var cancel = NamedButton(frame, "CancelGuildAction", "取消", 66, 460, 300, 82, CloseModal);
            EventSystem.current?.SetSelectedGameObject(cancel.gameObject);
            NamedButton(frame, "ConfirmGuildAction", "确认", 692, 460, 340, 82,
                () => { CloseModal(); action?.Invoke(); }, true);
        }
        private RectTransform Modal(string title, string description)
        {
            CloseModal(); _modalReturnFocus = EventSystem.current?.currentSelectedGameObject;
            _frameInput.interactable = false;
            _modal = QdaoUguiFactory.CreateStretch("GuildModal", _root, Vector4.zero);
            var dim = _modal.gameObject.AddComponent<Image>();
            dim.color = new Color(.03f, .10f, .08f, .74f); dim.raycastTarget = true;
            var frame = QdaoUguiFactory.CreateCenteredRect("GuildModalFrame", _modal, 1100, 610);
            Art(frame, "window_frame", 0, 0, 1100, 610);
            Heading(frame, title, 66, 50, 960, 72, 42);
            Text(frame, description, 66, 140, 968, 106, 29, Muted, true);
            return frame;
        }
        private void CloseModal()
        {
            if (_modal == null) return;
            _modal.gameObject.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(_modal.gameObject);
            else UnityEngine.Object.DestroyImmediate(_modal.gameObject);
            _modal = null;
            _frameInput.interactable = true;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_modalReturnFocus != null && _modalReturnFocus.activeInHierarchy ? _modalReturnFocus : null);
            _modalReturnFocus = null;
        }
        private static Sprite GuildSprite(string key) => Resources.Load<Sprite>("UI/Ugui/GuildV2/" + key)
            ?? throw new InvalidOperationException("缺少帮会 Sprite：UI/Ugui/GuildV2/" + key);
        private static void GuildField(UnityEngine.Transform parent, string name, float x, float y, float width, float height)
        {
            var image = QdaoUguiFactory.CreateImage(name, parent, x, y, width, height, GuildSprite("stat_field"));
            image.type = Image.Type.Sliced;
        }
        private static void GuildIcon(UnityEngine.Transform parent, string key, float x, float y, float size)
        {
            QdaoUguiFactory.CreateImage("GuildIcon_" + key, parent, x, y, size, size, GuildSprite(key)).preserveAspect = true;
        }
        private static TMP_InputField Input(UnityEngine.Transform parent, string name, float x, float y, float w, float h, string placeholder, int max)
        {
            var input = QdaoUguiFactory.CreateInputField(name, parent, x, y, w, h, placeholder, max, GuildSprite("stat_field"));
            var background = input.GetComponent<Image>();
            if (background.sprite.border.sqrMagnitude > 0) background.type = Image.Type.Sliced;
            float verticalInset = h >= 120 ? 20 : 10;
            input.textViewport.offsetMin = new Vector2(32, verticalInset);
            input.textViewport.offsetMax = new Vector2(-32, -verticalInset);
            QdaoUguiTypography.ApplyBody(input.textComponent);
            input.textComponent.fontSize = 30; input.textComponent.richText = false;
            input.textComponent.color = Ink;
            if (input.placeholder is TMP_Text hint)
            {
                QdaoUguiTypography.ApplyBody(hint);
                hint.fontSize = 30; hint.richText = false; hint.color = Muted;
            }
            return input;
        }
        private static Button NamedButton(UnityEngine.Transform parent, string name, string label, float x, float y, float w, float h,
            Action action, bool primary = false, bool enabled = true, string key = null, float fontSize = 30)
        {
            var button = Button(parent, label, x, y, w, h, action, primary, enabled, key, fontSize);
            button.name = name; return button;
        }
        // 2(副帮主)不启用,与 GuildRoles.Rank 一致落到默认的“帮众”。
        public static string RoleName(uint role) => role switch { 1 => "长老", 3 => "帮主", _ => "帮众" };

        /// <summary>
        /// 帮会里显示一个角色的唯一兜底规则:有名字显示名字,空名或纯空白回落“道友 · 编号”。
        /// 成员行、申请行与确认框都走这里,别处不要再内联一份兜底。
        /// 名字由服务端批量取名填入,取名失败时 fail-open 下发空名,所以兜底必须一直保留。
        /// </summary>
        public static string MemberDisplayName(ulong playerId, string name) =>
            string.IsNullOrWhiteSpace(name) ? "道友 · " + playerId : name;
        public static string MemberDisplayName(GuildMember member) => MemberDisplayName(member.PlayerId, member.Name);

        /// <summary>
        /// 成员查找同时匹配名字与编号。名字按服务端 playername 的 norm 口径比较(NFKC → 去首尾空白 → 转小写,
        /// 名字全服唯一也是按这个口径),所以 “alice”“ＡＬＩＣＥ” 都能找到 “Alice”,全角数字也能查编号。
        /// </summary>
        private static bool MatchesMemberSearch(GuildMember member, string query) =>
            member.PlayerId.ToString().Contains(query)
            || (!string.IsNullOrWhiteSpace(member.Name) && SearchKey(member.Name).Contains(query));
        private static string SearchKey(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string normalized;
            // TMP 输入框截断可能切开代理对,孤立代理会让 Normalize 抛 ArgumentException;查找词退回原文,不让界面崩。
            try { normalized = text.Normalize(NormalizationForm.FormKC); }
            catch (ArgumentException) { normalized = text; }
            return normalized.Trim().ToLowerInvariant();
        }
    }
}
