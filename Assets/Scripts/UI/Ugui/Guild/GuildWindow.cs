using System;
using System.Collections.Generic;
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
        public event Action<ulong> JoinRequested;
        public event Action LeaveRequested, DisbandRequested;
        public bool IsVisible => _root.gameObject.activeSelf;
        public bool ModalVisible => _modal != null && _modal.gameObject.activeSelf;
        public GuildPage Page { get; private set; }
        public const int MembersPerPage = 5;
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
        private ulong _guildId;

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
            _refresh = NamedButton(_frame, "RefreshGuild", "刷新", 1850, 844, 210, 64,
                () => { if (!Busy) { if (Page == GuildPage.Ranking) RankRequested?.Invoke(_client?.Rank?.Page ?? 1); else RefreshRequested?.Invoke(); } },
                true, fontSize: 28);
        }
        private bool Busy => _client == null || _client.Busy || _client.RequiresReconnect;
        public void SetClient(GuildClient client)
        {
            ulong guildId = client?.Info?.GuildId ?? 0;
            if (_guildId != guildId) { CloseModal(); _memberPage = 0; }
            _guildId = guildId; _client = client;
            if (IsVisible) Render();
        }
        public void Show(GuildPage page = GuildPage.Overview)
        {
            bool opening = !IsVisible;
            if (opening) _returnFocus = EventSystem.current?.currentSelectedGameObject;
            CloseModal(); Page = page; _root.gameObject.SetActive(true); Render();
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
                case GuildPage.Donate: RenderUnavailable("帮会捐献", "聚沙成塔，同心兴帮。", new[] { "银两捐献", "灵石捐献", "建设物资" },
                    new[] { "帮会开放捐献后，可在此共建家园。", "捐献规则将由帮会公布。", "所需物资与贡献以开放后的规则为准。" }); break;
                case GuildPage.Activities: RenderUnavailable("帮会活动", "灯下团圆，山海同行。", new[] { "元宵灯会", "中秋团圆", "同道历练" },
                    new[] { "花灯待点亮，与同道共赏佳节。", "月圆时相聚，共赴团圆之约。", "集结帮会成员，一起踏上旅程。" }); break;
                case GuildPage.Shop: RenderUnavailable("帮会商店", "汇聚同道之力，换取修行所需。", new[] { "修行补给", "帮会珍藏", "节庆好礼" },
                    new[] { "兑换品类、价格和库存将在开放后显示。", "帮会等级条件将在开放后显示。", "节庆限定兑换将在活动期间开放。" }); break;
            }
            if (!string.IsNullOrEmpty(focusName) && EventSystem.current != null)
                foreach (var control in _frame.GetComponentsInChildren<Selectable>())
                    if (control.name == focusName && control.IsInteractable()) { EventSystem.current.SetSelectedGameObject(control.gameObject); break; }
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
            Text(_rail, "Lv." + info.Level, 248, 144, 146, 46, 34, Gold, alignment: TextAlignmentOptions.MidlineRight);
            Line(_rail, "LevelRule", 0, 202, 394);
            Text(_rail, "帮会编号", 0, 214, 394, 40, 30, Muted);
            Text(_rail, info.GuildId.ToString(), 0, 258, 394, 42, 28);
            Text(_rail, "帮主编号", 0, 310, 394, 40, 30, Muted);
            Text(_rail, info.LeaderId.ToString(), 0, 354, 394, 42, 28);
            Line(_rail, "LeaderRule", 0, 412, 394);
            Text(_rail, "我的身份", 0, 430, 228, 48, 27, Muted);
            Text(_rail, RoleName(_client.Role), 242, 430, 152, 48, 30, alignment: TextAlignmentOptions.MidlineRight);
            Text(_rail, "山海有归处，同道共此时。", 0, 486, 394, 44, 30, Muted);
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
                Text(_body, "成员信息、公告和排行将随帮会更新。", 24, 532, 1460, 55, 28, Muted);
                return;
            }
            Heading(_body, "同道相聚", 0, 0, 950, 58, 43);
            Text(_body, "一同修行，一同守护这方灯火。", 0, 60, 1440, 42, 27, Muted);
            int online = 0;
            ulong contribution = 0;
            foreach (var member in info.Members)
            {
                if (member.Online) online++;
                if (member.PlayerId == _client.PlayerId) contribution = member.Contribution;
            }
            OverviewMetric("GuildMemberCount", "帮会成员", info.Members.Count + " / " + info.MaxMembers, 0);
            OverviewMetric("GuildOnlineCount", "当前在线", online + " 位", 512);
            OverviewMetric("GuildMyContribution", "我的贡献", contribution.ToString(), 1024);
            Line(_body, "MetricsRule", 0, 224, 1508);
            GuildIcon(_body, "notice", 0, 249, 47);
            Heading(_body, "帮会公告", 66, 240, 620, 63, 36);
            NamedButton(_body, "ReadGuildAnnouncement", "查看全文", 958, 240, 252, 64, ShowReadAnnouncement, fontSize: 27);
            if (_client.CanEditAnnouncement)
                NamedButton(_body, "EditGuildAnnouncement", "编辑公告", 1230, 240, 278, 64, ShowAnnouncement, enabled: !Busy, fontSize: 27);
            GuildField(_body, "GuildAnnouncementPanel", 0, 320, 1508, 138);
            Text(_body, string.IsNullOrWhiteSpace(info.Announcement) ? "帮会尚未发布公告。愿同道相伴，诸事顺遂。" : info.Announcement,
                28, 337, 1452, 104, 30, wrap: true);
            Heading(_body, "同道携手 · 帮会事务", 0, 476, 1508, 44, 32, Muted);
            OverviewEntry("GuildOverviewMembers", "帮会成员", "crest", GuildPage.Members, 0);
            OverviewEntry("GuildOverviewRanking", "帮会排行", "round_badge_compass", GuildPage.Ranking, 304);
            OverviewEntry("GuildOverviewDonate", "帮会捐献", "furnace", GuildPage.Donate, 608);
            OverviewEntry("GuildOverviewActivities", "帮会活动", "round_badge_lotus", GuildPage.Activities, 912);
            OverviewEntry("GuildOverviewShop", "帮会商店", "round_badge_pagoda", GuildPage.Shop, 1216);
        }
        private void OverviewMetric(string name, string label, string value, float x)
        {
            if (x > 0) Line(_body, name + "Divider", x - 24, 127, 1.5f, 73);
            Text(_body, label, x, 116, 460, 44, 30, Muted);
            var text = Text(_body, value, x, 164, 460, 54, 43);
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
            var members = new List<GuildMember>();
            foreach (var member in info.Members)
                if ((!_onlineOnly || member.Online) && (string.IsNullOrEmpty(_memberSearch) || member.PlayerId.ToString().Contains(_memberSearch)))
                    members.Add(member);
            members.Sort((a, b) => { int role = b.Role.CompareTo(a.Role); return role != 0 ? role : a.PlayerId.CompareTo(b.PlayerId); });
            int pages = Math.Max(1, (members.Count + MembersPerPage - 1) / MembersPerPage);
            _memberPage = Math.Max(0, Math.Min(_memberPage, pages - 1));
            NamedButton(_body, "OnlineGuildMembers", _onlineOnly ? "已选：仅在线" : "显示全部成员", 0, 0, 295, 66,
                () => { _onlineOnly = !_onlineOnly; _memberPage = 0; Render(); }, _onlineOnly, fontSize: 27);
            Text(_body, "按角色编号查找", 344, 3, 300, 58, 27, Muted);
            var search = Input(_body, "GuildMemberSearch", 640, 0, 575, 66, "输入完整编号或部分数字", 20);
            search.SetTextWithoutNotify(_memberSearch);
            NamedButton(_body, "SearchGuildMembers", "查找", 1236, 0, 272, 66,
                () => { _memberSearch = search.text.Trim(); _memberPage = 0; Render(); }, fontSize: 27);
            if (members.Count == 0)
                Text(_body, "没有符合条件的同道。", 30, 210, 1400, 100, 38, Muted, alignment: TextAlignmentOptions.Center);
            for (int i = 0; i < MembersPerPage && _memberPage * MembersPerPage + i < members.Count; i++)
            {
                var member = members[_memberPage * MembersPerPage + i];
                float y = 86 + i * 86;
                GuildField(_body, "GuildListRow", 0, y, 1508, 78);
                Text(_body, "道友 · " + member.PlayerId + (member.PlayerId == _client.PlayerId ? "（我）" : ""), 90, y + 12, 600, 56, 29);
                Text(_body, RoleName(member.Role), 724, y + 12, 205, 56, 29, Gold);
                Text(_body, "贡献  " + member.Contribution, 950, y + 12, 300, 56, 28, Muted);
                Text(_body, member.Online ? "在线" : "离线", 1330, y + 10, 162, 56, 28, member.Online ? Ink : Muted);
            }
            Pager(_body, "Members", _memberPage + 1, pages, delta => { _memberPage += delta; Render(); }, true);
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
                    NamedButton(_body, "JoinGuild_" + id, _client?.Info?.GuildId == id ? "我的帮会" : "加入",
                        1280, y + 7, 210, 64, () => Confirm("确认加入帮会", "帮会名称：" + name + "\n确认加入此帮会，与同道共赴山海？", () => JoinRequested?.Invoke(id)),
                        true, enabled: _client?.HasLoaded == true && _client.Info == null && !Busy, fontSize: 27);
                }
            int page = (int)(rank?.Page ?? 1), pageSize = (int)Math.Max(1, rank?.PageSize ?? 5);
            int pages = Math.Max(1, (int)(((rank?.TotalCount ?? 0) + (uint)pageSize - 1) / (uint)pageSize));
            Pager(_body, "Ranking", page, pages, delta => RankRequested?.Invoke((uint)Math.Max(1, page + delta)), !Busy);
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
            Text(_body, "开放后可在此查看规则与参与条件。", 14, 568, 1480, 42, 30, Muted);
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
        public static string RoleName(uint role) => role switch { 1 => "长老", 2 => "副帮主", 3 => "帮主", _ => "帮众" };
    }
}
