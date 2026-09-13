using System;
using System.Collections.Generic;
using MmorpgClient.Game.Team;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Team
{
    /// <summary>Team views consume authoritative snapshots and only emit player intent.</summary>
    public sealed class TeamWindow
    {
        public const int RowsPerPage = 4;
        public const int MaxMemberCards = 5;
        public event Action RefreshRequested;
        public event Action<ulong, bool> DecisionRequested;
        public event Action Closed;
        public bool IsVisible => _root.gameObject.activeSelf;
        public TeamPage Page { get; private set; }
        public int PageIndex { get; private set; }

        private readonly RectTransform _root, _body;
        private readonly TMP_Text _summary, _status, _pageLabel;
        private readonly Button[] _tabs = new Button[3];
        private readonly Button _refresh, _previous, _next;
        private TeamUiState _state;
        private GameObject _returnFocus;

        public TeamWindow(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateStretch("TeamWindow", parent, Vector4.zero);
            _root.gameObject.SetActive(false);
            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .10f, .08f, .68f);
            shade.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            var frame = QdaoUguiFactory.CreateCenteredRect("TeamFrame", _root, 2160, 924);
            Art(frame, "main_frame", 0, 0, 2160, 924);
            Art(frame, "title_plate", 62, -21, 480, 114);
            Text(frame, "结伴同游", 100, -7, 400, 84, 49, Cream, alignment: TextAlignmentOptions.Center);
            _summary = Text(frame, "相逢即同道 · 携手赴山海", 82, 106, 1840, 46, 29, Muted);
            string[] labels = { "队伍成员", "入队申请", "已同意" };
            for (int i = 0; i < _tabs.Length; i++)
            {
                var page = (TeamPage)i;
                _tabs[i] = Button(frame, labels[i], 1070 + i * 310, 36, 286, 80,
                    () => Show(page), key: "tab_normal", fontSize: 32);
                _tabs[i].name = "TeamTab_" + page;
            }
            var close = Button(frame, "", 2036, 28, 76, 76, Hide, key: "close");
            close.name = "CloseTeamWindow";
            _body = QdaoUguiFactory.CreateRect("TeamContent", frame, 60, 170, 2040, 640);
            _status = Text(frame, "", 84, 828, 1240, 52, 27, Muted, true);
            _previous = Button(frame, "上一页", 1340, 828, 182, 64, () => MovePage(-1), fontSize: 27);
            _previous.name = "TeamPreviousPage";
            _pageLabel = Text(frame, "", 1528, 828, 150, 64, 27, Muted, alignment: TextAlignmentOptions.Center);
            _next = Button(frame, "下一页", 1684, 828, 182, 64, () => MovePage(1), fontSize: 27);
            _next.name = "TeamNextPage";
            _refresh = Button(frame, "刷新", 1890, 828, 180, 64,
                () => { if (CanAct) RefreshRequested?.Invoke(); }, true, fontSize: 28);
            _refresh.name = "RefreshTeam";
        }

        public void SetState(TeamUiState state)
        {
            _state = state;
            if (IsVisible) Render();
        }

        public void Show(TeamPage page = TeamPage.Members)
        {
            bool opening = !IsVisible;
            if (opening) _returnFocus = EventSystem.current?.currentSelectedGameObject;
            if (Page != page) PageIndex = 0;
            Page = page;
            _root.gameObject.SetActive(true);
            Render();
            if (opening && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_tabs[(int)Page].gameObject);
        }

        public void Hide()
        {
            bool visible = IsVisible;
            _root.gameObject.SetActive(false);
            if (visible && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_returnFocus != null && _returnFocus.activeInHierarchy ? _returnFocus : null);
            _returnFocus = null;
            if (visible) Closed?.Invoke();
        }

        public void ResetSession()
        {
            Hide();
            _state = null;
            PageIndex = 0;
            Page = TeamPage.Members;
            Clear(_body);
        }

        private bool CanAct => _state != null && _state.ServiceAvailable && !_state.IsBusy;
        private IReadOnlyList<TeamRole> CurrentList => Page == TeamPage.Applications
            ? _state?.Snapshot?.Applications : _state?.Snapshot?.Approved;

        private void MovePage(int delta)
        {
            PageIndex += delta;
            Render();
        }

        private void Render()
        {
            string focusName = null;
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(_body)) focusName = selected.name;
            Clear(_body);
            var data = _state?.Snapshot;
            int members = data?.Members.Count ?? 0;
            int capacity = Math.Max(1, data?.Capacity ?? MaxMemberCards);
            _summary.text = data != null && data.TeamId != 0
                ? $"队伍人数 {members} / {capacity}     ·     队伍编号 {data.TeamId}     ·     " + (_state.IsLeader ? "你是队长" : "你是队员")
                : "相逢即同道 · 携手赴山海";
            string[] titles = { $"队伍成员 {members}", $"入队申请 {data?.Applications.Count ?? 0}", $"已同意 {data?.Approved.Count ?? 0}" };
            for (int i = 0; i < _tabs.Length; i++)
            {
                bool active = i == (int)Page;
                ((Image)_tabs[i].targetGraphic).sprite = Load(active ? "tab_selected" : "tab_normal");
                var label = _tabs[i].GetComponentInChildren<TMP_Text>();
                label.text = titles[i];
                label.color = active ? Cream : Ink;
            }
            _status.text = !string.IsNullOrWhiteSpace(_state?.Status) ? _state.Status
                : _state?.IsBusy == true ? "正在同步队伍，请稍候…"
                : _state?.ServiceAvailable != true ? "组队暂未开放，敬请期待。"
                : _state.IsFull ? "队伍已满，可拒绝剩余申请。"
                : _state.IsLeader ? "同意申请后，道友将加入你的队伍。" : "入队申请由队长处理。";
            _refresh.interactable = CanAct;
            _refresh.GetComponentInChildren<TMP_Text>().text = _state?.IsLoading == true ? "同步中…" : "刷新";
            bool listing = Page != TeamPage.Members;
            int pages = listing ? Math.Max(1, ((CurrentList?.Count ?? 0) + RowsPerPage - 1) / RowsPerPage)
                : 1 + (Math.Max(capacity, members) - 1) / MaxMemberCards;
            PageIndex = Mathf.Clamp(PageIndex, 0, pages - 1);
            _pageLabel.gameObject.SetActive(listing || pages > 1);
            _previous.gameObject.SetActive(listing || pages > 1);
            _next.gameObject.SetActive(listing || pages > 1);
            _pageLabel.text = $"{PageIndex + 1} / {pages}";
            _previous.interactable = PageIndex > 0;
            _next.interactable = PageIndex + 1 < pages;
            if (listing) RenderList();
            else RenderMembers(capacity);
            if (focusName != null && EventSystem.current != null)
            {
                Button nextFocus = null;
                foreach (var button in _body.GetComponentsInChildren<Button>())
                    if (button.name == focusName && button.interactable) { nextFocus = button; break; }
                EventSystem.current.SetSelectedGameObject(nextFocus != null ? nextFocus.gameObject : _tabs[(int)Page].gameObject);
            }
        }

        private void RenderMembers(int capacity)
        {
            var data = _state?.Snapshot;
            int start = PageIndex * MaxMemberCards;
            int visibleSlots = Math.Min(MaxMemberCards, Math.Max(capacity, data?.Members.Count ?? 0) - start);
            for (int i = 0; i < visibleSlots; i++)
            {
                int slot = start + i;
                float width = 386;
                float x = (2040 - visibleSlots * 402 + 16) / 2 + i * 402;
                var card = QdaoUguiFactory.CreateRect("TeamMemberSlot_" + slot, _body, x, 12, width, 618);
                Art(card, "content_panel", 0, 0, width, 618);
                var role = data != null && slot < data.Members.Count ? data.Members[slot] : null;
                if (role == null)
                {
                    Text(card, $"席位 {slot + 1:00}", 36, 34, 314, 42, 28, Gold, alignment: TextAlignmentOptions.Center);
                    Art(card, "portrait_frame", 58, 116, 270, 270);
                    Text(card, "待结缘", 73, 186, 240, 102, 43, Muted, alignment: TextAlignmentOptions.Center);
                    Text(card, "虚位以待", 35, 433, 316, 56, 34, alignment: TextAlignmentOptions.Center);
                    Text(card, "静候同道，一路同行", 26, 512, 334, 48, 25, Muted, alignment: TextAlignmentOptions.Center);
                    continue;
                }
                bool leader = role.PlayerId == data.LeaderId;
                Text(card, leader ? "队长" : "队员", 36, 30, 314, 46, 29, leader ? Gold : Muted, alignment: TextAlignmentOptions.Center);
                Portrait(card, role, 58, 100, 270);
                Text(card, DisplayName(role), 27, 403, 332, 54, 36, alignment: TextAlignmentOptions.Center).name = "TeamName_" + role.PlayerId;
                Text(card, Level(role) + "  ·  " + School(role), 24, 474, 338, 46, 28, Muted, alignment: TextAlignmentOptions.Center).name = "TeamDetails_" + role.PlayerId;
                Text(card, (role.PlayerId == data.LocalPlayerId ? "自己 · " : "") + (role.IsOnline ? "在线" : "离线"),
                    24, 540, 338, 38, 26, role.IsOnline ? Ink : Muted, alignment: TextAlignmentOptions.Center);
            }
        }

        private void RenderList()
        {
            var list = CurrentList;
            Text(_body, Page == TeamPage.Applications ? "申请道友" : "已同意的道友", 44, 0, 640, 42, 28, Muted);
            Text(_body, "等级", 1060, 0, 160, 42, 28, Muted);
            Text(_body, "门派", 1270, 0, 250, 42, 28, Muted);
            Text(_body, Page == TeamPage.Applications ? "处理申请" : "处理结果", 1690, 0, 290, 42, 28, Muted);
            if (list == null || list.Count == 0)
            {
                Art(_body, "content_panel", 22, 58, 1996, 572);
                string title = _state?.IsLoading == true ? "正在寻找同行道友…"
                    : Page == TeamPage.Applications ? "暂无入队申请" : "暂无已同意记录";
                Text(_body, title, 280, 239, 1480, 76, 46, alignment: TextAlignmentOptions.Center);
                Text(_body, Page == TeamPage.Applications ? "收到申请后，可在这里查看角色资料并同意或拒绝。" : "同意的道友会显示在这里，当前成员可在队伍成员中查看。",
                    280, 338, 1480, 68, 29, Muted, alignment: TextAlignmentOptions.Center);
                return;
            }
            int end = Math.Min(list.Count, (PageIndex + 1) * RowsPerPage);
            for (int index = PageIndex * RowsPerPage; index < end; index++)
            {
                var role = list[index];
                if (role == null) continue;
                var row = QdaoUguiFactory.CreateRect("TeamRow_" + role.PlayerId, _body, 22,
                    58 + (index % RowsPerPage) * 146, 1996, 134);
                Art(row, "content_panel", 0, 0, 1996, 134);
                Portrait(row, role, 14, 10, 114);
                Text(row, DisplayName(role), 164, 18, 790, 48, 34).name = "TeamName_" + role.PlayerId;
                Text(row, Page == TeamPage.Applications ? "申请加入队伍" : IsMember(role.PlayerId) ? "已加入当前队伍" : "已同意入队申请",
                    164, 76, 790, 38, 26, Muted);
                Text(row, Level(role), 1038, 41, 184, 52, 30).name = "TeamLevel_" + role.PlayerId;
                Text(row, School(role), 1248, 41, 294, 52, 30).name = "TeamSchool_" + role.PlayerId;
                if (Page == TeamPage.Approved)
                {
                    Text(row, "已同意", 1590, 41, 372, 52, 31, Ink, alignment: TextAlignmentOptions.Center);
                    continue;
                }
                bool pending = _state?.PendingPlayerId == role.PlayerId;
                bool allowed = CanAct && _state.IsLeader;
                var approve = Button(row, pending ? "处理中…" : "同意", 1590, 33, 178, 68,
                    () => Decide(role.PlayerId, true), true, allowed && !_state.IsFull, fontSize: 28);
                approve.name = "Approve_" + role.PlayerId;
                var reject = Button(row, "拒绝", 1784, 33, 178, 68,
                    () => Decide(role.PlayerId, false), enabled: allowed, fontSize: 28);
                reject.name = "Reject_" + role.PlayerId;
            }
        }

        private void Decide(ulong playerId, bool approve)
        {
            if (!CanAct || !_state.IsLeader || approve && _state.IsFull) return;
            foreach (var role in _state.Snapshot.Applications)
                if (role.PlayerId == playerId) { DecisionRequested?.Invoke(playerId, approve); return; }
        }

        private bool IsMember(ulong playerId)
        {
            if (_state?.Snapshot == null) return false;
            foreach (var member in _state.Snapshot.Members)
                if (member.PlayerId == playerId) return true;
            return false;
        }

        private static void Portrait(UnityEngine.Transform parent, TeamRole role, float x, float y, float size)
        {
            Art(parent, "portrait_frame", x, y, size, size);
            string id = role.CharacterId;
            if (string.IsNullOrEmpty(id) && role.ClassId >= 1 && role.ClassId <= 4 && (role.Gender == 1 || role.Gender == 2))
                id = QdaoCharacterCatalog.ResolveRole(role.ClassId, role.Gender);
            var sprite = QdaoCharacterCatalog.LoadPortrait(id);
            var image = QdaoUguiFactory.CreateImage("TeamPortrait_" + role.PlayerId, parent,
                x + size * .07f, y + size * .07f, size * .86f, size * .86f, sprite);
            image.preserveAspect = true;
            image.gameObject.SetActive(sprite != null);
            if (sprite == null) Text(parent, "待同步", x + 8, y + size * .3f, size - 16, size * .4f,
                size > 160 ? 32 : 20, Muted, alignment: TextAlignmentOptions.Center);
        }

        private static string DisplayName(TeamRole role) => string.IsNullOrWhiteSpace(role.Name) ? "无名道友" : role.Name;
        private static string Level(TeamRole role) => role.Level > 0 ? $"{role.Level} 级" : "等级待同步";
        private static string School(TeamRole role) => !string.IsNullOrWhiteSpace(role.SchoolName) ? role.SchoolName
            : role.ClassId switch { 1 => "破军", 2 => "玄霄", 3 => "丹心", 4 => "逐风", _ => "门派待同步" };
    }
}
