using System;
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
    /// <summary>Member and application lists share one view, driven only by authoritative snapshots.</summary>
    public sealed class TeamWindow
    {
        public const int RowsPerPage = 4;
        public const int MaxMemberCards = 5;
        public event Action RefreshRequested;
        public event Action<ulong, bool> DecisionRequested;
        public event Action Closed;
        public bool IsVisible => _root.gameObject.activeSelf;
        // Kept for callers that open a particular list. Both lists remain visible.
        public TeamPage Page { get; private set; }
        public int MemberPageIndex { get; private set; }
        public int ApplicationPageIndex { get; private set; }
        public int PageIndex => Page == TeamPage.Applications ? ApplicationPageIndex : MemberPageIndex;

        private static readonly Color BodyInk = QdaoUguiTheme.Html("#304736");
        private static readonly Color BodyMuted = QdaoUguiTheme.Html("#70634E");
        private static readonly Color Jade = QdaoUguiTheme.Html("#205B46");
        private static readonly Color Border = QdaoUguiTheme.Html("#C7B68E");
        private static TMP_FontAsset _bodyFont;
        private readonly RectTransform _root, _members, _applications;
        private readonly TMP_Text _summary, _status, _memberCount, _applicationCount;
        private readonly TMP_Text _memberPageLabel, _applicationPageLabel;
        private readonly Button _refresh, _close, _memberPrevious, _memberNext, _applicationPrevious, _applicationNext;
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
            var frame = QdaoUguiFactory.CreateCenteredRect("TeamFrame", _root, 2200, 916);
            Art(frame, "main_frame", 0, 0, 2200, 916);
            Art(frame, "title_plate", 770, -30, 660, 112);
            Text(frame, "结伴同游", 806, -20, 588, 94, 56, Cream, alignment: TextAlignmentOptions.Center);
            Art(frame, "lantern", 60, -28, 52, 98, true);
            Art(frame, "close_tassel", 2107, 91, 33, 91, true);
            _close = Control(frame, "", 2084, 20, 76, 76, Hide, key: "close");
            _close.name = "CloseTeamWindow";
            _summary = Label(frame, "队伍信息尚未同步", 86, 104, 2014, 58, 32);
            Solid(frame, "SummaryDivider", 84, 178, 2032, 1, Border);
            Text(frame, "队伍成员", 88, 195, 430, 54, 44, BodyInk);
            Text(frame, "申请列表", 1266, 195, 380, 54, 44, BodyInk);
            _memberCount = Label(frame, "", 976, 200, 200, 46, 32, BodyMuted, TextAlignmentOptions.MidlineRight);
            _applicationCount = Label(frame, "", 1930, 200, 180, 46, 32, Jade, TextAlignmentOptions.MidlineRight);
            Solid(frame, "ListDivider", 1218, 206, 1, 586, Border);
            _members = QdaoUguiFactory.CreateRect("TeamMembers", frame, 84, 266, 1090, 514);
            _applications = QdaoUguiFactory.CreateRect("TeamApplications", frame, 1264, 266, 846, 514);

            _memberPrevious = Control(frame, "上一页", 386, 790, 168, 54, () => MovePage(TeamPage.Members, -1), size: 25);
            _memberPrevious.name = "MemberPreviousPage";
            _memberPageLabel = Label(frame, "", 558, 790, 142, 54, 26, BodyMuted, TextAlignmentOptions.Center);
            _memberNext = Control(frame, "下一页", 704, 790, 168, 54, () => MovePage(TeamPage.Members, 1), size: 25);
            _memberNext.name = "MemberNextPage";
            _applicationPrevious = Control(frame, "上一页", 1442, 790, 168, 54, () => MovePage(TeamPage.Applications, -1), size: 25);
            _applicationPrevious.name = "ApplicationPreviousPage";
            _applicationPageLabel = Label(frame, "", 1614, 790, 142, 54, 26, BodyMuted, TextAlignmentOptions.Center);
            _applicationNext = Control(frame, "下一页", 1760, 790, 168, 54, () => MovePage(TeamPage.Applications, 1), size: 25);
            _applicationNext.name = "ApplicationNextPage";
            Solid(frame, "FooterDivider", 84, 850, 2032, 1, Border);
            _status = Label(frame, "", 88, 857, 1660, 48, 28, BodyMuted);
            _refresh = Control(frame, "刷新", 1894, 851, 216, 60,
                () => { if (CanAct) RefreshRequested?.Invoke(); }, size: 30);
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
            // Legacy Approved callers land on members; approval history is never rendered.
            Page = page == TeamPage.Applications ? TeamPage.Applications : TeamPage.Members;
            _root.gameObject.SetActive(true);
            Render();
            if (opening && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_close.gameObject);
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
            MemberPageIndex = ApplicationPageIndex = 0;
            Page = TeamPage.Members;
            Clear(_members);
            Clear(_applications);
        }

        private bool CanAct => _state != null && _state.ServiceAvailable && !_state.IsBusy;

        private void MovePage(TeamPage page, int delta)
        {
            Page = page;
            if (page == TeamPage.Applications) ApplicationPageIndex += delta;
            else MemberPageIndex += delta;
            Render();
        }

        private void Render()
        {
            string focusName = null;
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && (selected.transform.IsChildOf(_members) || selected.transform.IsChildOf(_applications)))
                focusName = selected.name;
            Clear(_members);
            Clear(_applications);
            var data = _state?.Snapshot;
            int members = data?.Members.Count ?? 0;
            int capacity = Math.Max(1, data?.Capacity ?? MaxMemberCards);
            int applications = data?.Applications.Count ?? 0;
            _summary.text = data != null && data.TeamId != 0
                ? $"队伍人数  {members} / {capacity}        队伍编号  {data.TeamId}        " + (_state.IsLeader ? "你是队长" : "你是队员")
                : "队伍信息尚未同步";
            _memberCount.text = $"{members} / {capacity}";
            _applicationCount.text = $"{applications} 人";
            _status.text = !string.IsNullOrWhiteSpace(_state?.Status) ? _state.Status
                : _state?.IsBusy == true ? "正在同步队伍，请稍候…"
                : _state?.ServiceAvailable != true ? "组队暂未开放，敬请期待。"
                : !_state.IsLeader ? "你是队员，入队申请由队长处理。"
                : _state.IsFull ? "队伍已满，可拒绝剩余申请。"
                : "同意申请后，道友将加入你的队伍。";
            _refresh.interactable = CanAct;
            _refresh.GetComponentInChildren<TMP_Text>().text = _state?.IsLoading == true ? "同步中…" : "刷新";
            int memberPages = 1 + (Math.Max(capacity, members) - 1) / MaxMemberCards;
            int applicationPages = Math.Max(1, (applications + RowsPerPage - 1) / RowsPerPage);
            MemberPageIndex = Mathf.Clamp(MemberPageIndex, 0, memberPages - 1);
            ApplicationPageIndex = Mathf.Clamp(ApplicationPageIndex, 0, applicationPages - 1);
            Pagination(_memberPrevious, _memberNext, _memberPageLabel, MemberPageIndex, memberPages);
            Pagination(_applicationPrevious, _applicationNext, _applicationPageLabel, ApplicationPageIndex, applicationPages);
            RenderMembers(capacity);
            RenderApplications();
            if (focusName != null && EventSystem.current != null)
            {
                Button target = null;
                foreach (var button in _applications.GetComponentsInChildren<Button>())
                    if (button.name == focusName && button.interactable) { target = button; break; }
                if (target == null)
                    foreach (var button in _applications.GetComponentsInChildren<Button>())
                        if (button.interactable) { target = button; break; }
                EventSystem.current.SetSelectedGameObject(target != null ? target.gameObject
                    : _refresh.interactable ? _refresh.gameObject : _close.gameObject);
            }
            else if (selected != null && (!selected.activeInHierarchy || selected.GetComponent<Button>()?.interactable == false)
                     && selected.transform.IsChildOf(_root) && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_close.gameObject);
        }

        private static void Pagination(Button previous, Button next, TMP_Text label, int page, int pages)
        {
            previous.interactable = page > 0;
            next.interactable = page + 1 < pages;
            previous.gameObject.SetActive(pages > 1);
            next.gameObject.SetActive(pages > 1);
            label.gameObject.SetActive(pages > 1);
            label.text = $"{page + 1} / {pages}";
        }

        private void RenderMembers(int capacity)
        {
            var data = _state?.Snapshot;
            int start = MemberPageIndex * MaxMemberCards;
            int visible = Math.Min(MaxMemberCards, Math.Max(capacity, data?.Members.Count ?? 0) - start);
            for (int i = 0; i < visible; i++)
            {
                int slot = start + i;
                var role = data != null && slot < data.Members.Count ? data.Members[slot] : null;
                bool self = role != null && role.PlayerId == data.LocalPlayerId;
                var row = Row(_members, "TeamMemberSlot_" + slot, 0, i * 104, 1090, 98,
                    self ? QdaoUguiTheme.Html("#E6ECD9") : role == null ? QdaoUguiTheme.Html("#EFEADC") : QdaoUguiTheme.Html("#FAF3E6"));
                if (role == null)
                {
                    Label(row, "+", 22, 18, 64, 64, 44, BodyMuted, TextAlignmentOptions.Center);
                    Label(row, "空余席位", 116, 22, 520, 54, 32, BodyMuted);
                    Label(row, $"席位 {slot + 1:00}", 846, 24, 216, 50, 26, BodyMuted, TextAlignmentOptions.MidlineRight);
                    continue;
                }
                Portrait(row, role, 12, 8, 82);
                Label(row, DisplayName(role), 114, 6, 496, 47, 36, bold: true).name = "TeamName_" + role.PlayerId;
                bool leader = role.PlayerId == data.LeaderId;
                Label(row, (leader ? "队长" : "队员") + (self ? " · 自己" : ""), 114, 56, 476, 33, 25,
                    leader ? QdaoUguiTheme.Html("#82601F") : BodyMuted);
                Label(row, Level(role), 628, 22, 144, 56, role.Level > 0 ? 30 : 26).name = "TeamLevel_" + role.PlayerId;
                Label(row, School(role), 778, 22, 176, 56, 30).name = "TeamSchool_" + role.PlayerId;
                Label(row, role.IsOnline ? "● 在线" : "○ 离线", 962, 25, 112, 50, 27,
                    role.IsOnline ? Jade : BodyMuted, TextAlignmentOptions.MidlineRight);
            }
        }

        private void RenderApplications()
        {
            var list = _state?.Snapshot?.Applications;
            if (list == null || list.Count == 0)
            {
                Row(_applications, "EmptyApplications", 0, 0, 846, 514, QdaoUguiTheme.Html("#FAF3E6"));
                string title = _state?.IsBusy == true ? "正在同步申请…" : "暂无入队申请";
                Label(_applications, title, 32, 162, 782, 66, 42, BodyInk, TextAlignmentOptions.Center, bold: true);
                string detail = _state?.ServiceAvailable == true
                    ? "收到申请后，可在这里查看道友资料。" : "组队开放后，可在这里查看并处理申请。";
                Label(_applications, detail, 72, 254, 702, 96, 30, BodyMuted, TextAlignmentOptions.Center, wrap: true);
                return;
            }
            int start = ApplicationPageIndex * RowsPerPage;
            int count = Math.Min(RowsPerPage, list.Count - start);
            bool compact = count > 2;
            for (int i = 0; i < count; i++)
            {
                var role = list[start + i];
                if (role == null) continue;
                var row = Row(_applications, "TeamRow_" + role.PlayerId, 0, i * (compact ? 126 : 250), 846,
                    compact ? 114 : 230, QdaoUguiTheme.Html("#FAF3E6"));
                Portrait(row, role, compact ? 14 : 22, compact ? 16 : 20, compact ? 82 : 98);
                Label(row, DisplayName(role), compact ? 114 : 142, compact ? 10 : 20, compact ? 346 : 672, 48,
                    compact ? 32 : 38, bold: true).name = "TeamName_" + role.PlayerId;
                Label(row, Level(role) + "  ·  " + School(role), compact ? 114 : 142, compact ? 65 : 80,
                    compact ? 346 : 672, 40, compact ? 26 : 30, BodyMuted).name = "TeamDetails_" + role.PlayerId;
                bool pending = _state?.PendingPlayerId == role.PlayerId;
                bool allowed = CanAct && _state.IsLeader;
                float y = compact ? 24 : 146;
                var reject = Control(row, "拒绝", compact ? 476 : 438, y, compact ? 158 : 178,
                    compact ? 66 : 64, () => Decide(role.PlayerId, false), enabled: allowed, size: compact ? 28 : 32);
                reject.name = "Reject_" + role.PlayerId;
                var approve = Control(row, pending ? "处理中" : "同意", 648, y, 174,
                    compact ? 66 : 64, () => Decide(role.PlayerId, true), primary: true,
                    enabled: allowed && !_state.IsFull, size: pending ? 27 : compact ? 28 : 32);
                approve.name = "Approve_" + role.PlayerId;
            }
        }

        private void Decide(ulong playerId, bool approve)
        {
            if (!CanAct || !_state.IsLeader || approve && _state.IsFull) return;
            foreach (var role in _state.Snapshot.Applications)
                if (role.PlayerId == playerId) { DecisionRequested?.Invoke(playerId, approve); return; }
        }

        private static void Portrait(UnityEngine.Transform parent, TeamRole role, float x, float y, float size)
        {
            string id = role.CharacterId;
            if (string.IsNullOrEmpty(id) && role.ClassId >= 1 && role.ClassId <= 4 && (role.Gender == 1 || role.Gender == 2))
                id = QdaoCharacterCatalog.ResolveRole(role.ClassId, role.Gender);
            var sprite = QdaoCharacterCatalog.LoadPortrait(id);
            var mask = QdaoUguiFactory.CreateImage("PortraitMask", parent, x, y, size, size,
                QdaoUguiTheme.RequireSprite(QdaoUguiTheme.StatusDotSpritePath));
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var portrait = QdaoUguiFactory.CreateImage("TeamPortrait_" + role.PlayerId, mask.transform,
                -size * .5f, -size * .07f, size * 2, size * 2, sprite);
            portrait.preserveAspect = true;
            portrait.gameObject.SetActive(sprite != null);
            Art(parent, "portrait_frame", x, y, size, size);
            if (sprite == null) Label(parent, "待同步", x + 3, y + size * .3f, size - 6, size * .4f,
                20, BodyMuted, TextAlignmentOptions.Center);
        }

        private static RectTransform Row(UnityEngine.Transform parent, string name, float x, float y, float w, float h, Color fill)
        {
            var row = QdaoUguiFactory.CreateRect(name, parent, x, y, w, h);
            Solid(row, "FineBorder", 0, 0, w, h, Border);
            Solid(row, "QuietPaper", 1, 1, w - 2, h - 2, fill);
            return row;
        }

        private static void Solid(UnityEngine.Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var image = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, null);
            image.color = color;
        }

        private static TMP_FontAsset BodyFont()
        {
            if (_bodyFont != null) return _bodyFont;
            var source = Resources.Load<Font>("Fonts/TeamNotoSansSC");
            if (source == null) return QdaoUguiTheme.ResolveFont();
            _bodyFont = TMP_FontAsset.CreateFontAsset(source);
            _bodyFont.name = "Team Noto Sans SC (Dynamic)";
            _bodyFont.isMultiAtlasTexturesEnabled = true;
            return _bodyFont;
        }

        private static TextMeshProUGUI Label(UnityEngine.Transform parent, string value, float x, float y, float w, float h,
            float size, Color? color = null, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft,
            bool bold = false, bool wrap = false)
        {
            // Noto CJK line metrics exceed the old KaiTi boxes; Ellipsis otherwise hides a whole line.
            h = Mathf.Max(h, size * 1.6f);
            var label = Text(parent, value, x, y, w, h, size, color ?? BodyInk, alignment: alignment);
            label.font = BodyFont();
            label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            return label;
        }

        private static Button Control(UnityEngine.Transform parent, string value, float x, float y, float w, float h,
            Action click, bool primary = false, bool enabled = true, string key = null, float size = 30)
        {
            var button = Button(parent, value, x, y, w, h, click, primary, enabled, key, size);
            button.GetComponentInChildren<TMP_Text>().font = BodyFont();
            return button;
        }

        private static string DisplayName(TeamRole role) => string.IsNullOrWhiteSpace(role.Name) ? "无名道友" : role.Name;
        private static string Level(TeamRole role) => role.Level > 0 ? $"{role.Level} 级" : "等级待同步";
        private static string School(TeamRole role) => !string.IsNullOrWhiteSpace(role.SchoolName) ? role.SchoolName
            : role.ClassId switch { 1 => "破军", 2 => "玄霄", 3 => "丹心", 4 => "逐风", _ => "门派待同步" };
    }
}
