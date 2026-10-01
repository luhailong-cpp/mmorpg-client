using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MmorpgClient.Game.Team;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Team
{
    /// <summary>Four real candidate sources share one invitation action and the existing TeamV2 skin.</summary>
    public sealed class TeamInvitationWindow
    {
        public const int PageSize = 6;
        public event Action<TeamInvitationSource, string> RefreshRequested;
        public event Action<ulong> InviteRequested;
        public event Action LoadMoreRequested;
        public event Action CreateRequested;
        public event Action Closed;
        public bool IsVisible => _root.gameObject.activeSelf;
        public TeamInvitationSource Source { get; private set; }
        public string Query => _search.text.Trim();
        public int PageIndex { get; private set; }
        private readonly RectTransform _root, _list;
        private readonly TMP_InputField _search, _manualId;
        private readonly TMP_Text _notice, _status, _page;
        private readonly Button _close, _refresh, _searchButton, _previous, _next, _manualInvite, _create;
        private readonly Dictionary<TeamInvitationSource, Button> _tabs = new();
        private readonly List<TeamRole> _candidates = new();
        private TeamUiState _state;
        private string _directoryStatus = "", _localStatus = "", _activeQuery = "";
        private bool _loading, _hasMore;
        private GameObject _returnFocus;
        private static readonly Color Ink = QdaoUguiTheme.Html("#304736");
        private static readonly Color Muted = QdaoUguiTheme.Html("#6C6854");
        private static readonly Color Jade = QdaoUguiTheme.Html("#315D40");

        public TeamInvitationWindow(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateStretch("TeamInvitationWindow", parent, Vector4.zero);
            _root.gameObject.SetActive(false);
            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .10f, .08f, .76f);
            shade.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            var frame = QdaoUguiFactory.CreateCenteredRect("TeamInvitationFrame", _root, 2200, 916);
            TeamUiArt.Art(frame, "main_frame", 0, 0, 2200, 916);
            TeamUiArt.Art(frame, "title_plate", 680, -46, 840, 148);
            TeamUiArt.Text(frame, "邀友同游", 778, -22, 644, 96, 60,
                TeamUiArt.Cream, TextAlignmentOptions.Center);
            TeamUiArt.Art(frame, "lantern", 42, -20, 52, 98, true);
            TeamUiArt.Art(frame, "close_tassel", 2107, 91, 33, 91, true);
            _close = Control(frame, "CloseTeamInvitations", "", 2084, 20, 76, 76, Hide, key: "close");
            int index = 0;
            foreach (TeamInvitationSource source in Enum.GetValues(typeof(TeamInvitationSource)))
            {
                var captured = source;
                _tabs[source] = Control(frame, "TeamInvitationTab_" + source, SourceName(source),
                    88 + index++ * 330, 120, 306, 68, () => SelectSource(captured), size: 32);
            }
            Label(frame, "同道相逢 · 结伴山海", 1478, 125, 550, 54, 30, Muted, TextAlignmentOptions.MidlineRight);
            Label(frame, "查找道友", 88, 208, 180, 54, 28, Jade);
            _search = Input(frame, "TeamInvitationSearch", 280, 202, 1176, 66, "输入道友姓名或玩家编号", 64);
            _search.onSubmit.AddListener(_ => RequestRefresh());
            _searchButton = Control(frame, "SearchTeamInvitations", "搜索", 1480, 202, 272, 66, RequestRefresh, true);
            _refresh = Control(frame, "RefreshTeamInvitations", "刷新", 1776, 202, 336, 66, RequestRefresh);
            _notice = Label(frame, "", 88, 280, 2024, 48, 26, Muted);
            _notice.name = "TeamInvitationNotice";
            _list = QdaoUguiFactory.CreateRect("TeamInvitationCandidates", frame, 88, 338, 2024, 420);
            Label(frame, "编号邀请", 88, 785, 170, 50, 26, Jade);
            _manualId = Input(frame, "TeamInvitationPlayerId", 266, 777, 428, 62, "输入玩家编号", 20);
            _manualId.contentType = TMP_InputField.ContentType.IntegerNumber;
            _manualInvite = Control(frame, "InviteTeamById", "发出邀请", 714, 777, 242, 62, InviteById, true, size: 28);
            _previous = Control(frame, "TeamInvitationPrevious", "上一页", 1334, 777, 208, 62, () => MovePage(-1), size: 26);
            _page = Label(frame, "", 1562, 781, 268, 52, 25, Muted, TextAlignmentOptions.Center);
            _next = Control(frame, "TeamInvitationNext", "下一页", 1850, 777, 262, 62, () => MovePage(1), size: 26);
            _status = Label(frame, "", 88, 851, 1492, 50, 26, Muted);
            _status.name = "TeamInvitationStatus";
            _create = Control(frame, "CreateTeamForInvitation", "创建队伍", 1614, 850, 258, 60,
                () => { if (CanCreate) CreateRequested?.Invoke(); }, true, size: 28);
            Control(frame, "BackFromTeamInvitations", "返回", 1894, 850, 218, 60, Hide, size: 28);
        }

        public static string SourceName(TeamInvitationSource source) => source switch
        {
            TeamInvitationSource.Friends => "好友邀请",
            TeamInvitationSource.Nearby => "周围玩家",
            TeamInvitationSource.Online => "在线玩家",
            _ => "聊天邀请"
        };

        public void Show(TeamInvitationSource source = TeamInvitationSource.Friends, string query = "")
        {
            if (!IsVisible) _returnFocus = EventSystem.current?.currentSelectedGameObject;
            Source = source;
            _loading = false;
            _hasMore = false;
            _candidates.Clear();
            _search.SetTextWithoutNotify(query ?? "");
            _activeQuery = Query;
            PageIndex = 0;
            _localStatus = "";
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            RequestRefresh();
            EventSystem.current?.SetSelectedGameObject(_search.gameObject);
        }

        public void Hide()
        {
            bool visible = IsVisible;
            _root.gameObject.SetActive(false);
            if (!visible) return;
            Closed?.Invoke();
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_returnFocus != null && _returnFocus.activeInHierarchy ? _returnFocus : null);
            _returnFocus = null;
        }

        public void ResetSession()
        {
            Hide();
            _state = null;
            _candidates.Clear();
            _loading = false;
            _search.SetTextWithoutNotify("");
            _manualId.SetTextWithoutNotify("");
            _directoryStatus = _localStatus = _activeQuery = "";
            PageIndex = 0;
            TeamUiArt.Clear(_list);
        }

        public void SetState(TeamUiState state)
        {
            _state = state;
            if (IsVisible) Render();
        }

        public void SetCandidates(TeamInvitationSource source, IReadOnlyList<TeamRole> candidates, bool loading, string status, bool hasMore = false)
        {
            if (source != Source) return;
            _candidates.Clear();
            if (candidates != null)
                foreach (var role in candidates)
                    if (role != null && role.PlayerId != 0 && !_candidates.Exists(r => r.PlayerId == role.PlayerId))
                        _candidates.Add(role.Clone());
            _loading = loading;
            _hasMore = hasMore;
            _directoryStatus = status ?? "";
            if (IsVisible) Render();
        }

        private bool CanCreate => _state != null && _state.ServiceAvailable && _state.HasLoaded &&
            !_state.HasTeam && !_state.IsBusy && !_state.CreationCoolingDown;

        /// <summary>Never infer success from a click: authoritative pending invitations alone show “已邀请”.</summary>
        public static string BlockReason(TeamUiState state, TeamRole role)
        {
            if (state == null || !state.ServiceAvailable || !state.HasLoaded) return "等待同步";
            if (role == null || role.PlayerId == 0) return "编号无效";
            if (role.PlayerId == state.Snapshot.LocalPlayerId) return "自己";
            if (state.Snapshot.Members.Exists(r => r != null && r.PlayerId == role.PlayerId)) return "已在队伍";
            if (state.Snapshot.PendingInvites.Exists(r => r != null && r.PlayerId == role.PlayerId)) return "已邀请";
            if (!role.IsOnline && role.OnlineStatusKnown) return "已离线";
            if (!state.HasTeam) return "先创建队伍";
            if (!state.IsLeader) return "仅队长可邀";
            if (state.IsFull) return "队伍已满";
            if (state.MatchStarting) return "集合中";
            if (state.IsBusy) return state.PendingAction == TeamAction.Invite && state.PendingTarget == role.PlayerId ? "发送中…" : "请稍候";
            if (state.InvitationCoolingDown) return "请稍候";
            return null;
        }

        private void SelectSource(TeamInvitationSource source)
        {
            if (Source == source) return;
            Source = source;
            _search.SetTextWithoutNotify("");
            _activeQuery = "";
            _candidates.Clear();
            _directoryStatus = "";
            RequestRefresh();
        }

        private void RequestRefresh()
        {
            if (_loading) return;
            PageIndex = 0;
            _localStatus = "";
            _activeQuery = Query;
            // Data providers publish their actual loading state. Offline previews may synchronously return fixtures.
            RefreshRequested?.Invoke(Source, Query);
            Render();
        }

        private void MovePage(int delta)
        {
            if (delta > 0 && _hasMore && (PageIndex + 1) * PageSize >= _candidates.Count)
            { if (!_loading) LoadMoreRequested?.Invoke(); return; }
            PageIndex += delta;
            Render();
        }

        private void Render()
        {
            string query = _activeQuery;
            var visible = _candidates.Where(role => role.PlayerId != _state?.Snapshot.LocalPlayerId &&
                (query.Length == 0 || role.PlayerId.ToString(CultureInfo.InvariantCulture).Contains(query) ||
                 (role.Name ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            int pages = Math.Max(1, (visible.Count + PageSize - 1) / PageSize);
            PageIndex = Mathf.Clamp(PageIndex, 0, pages - 1);
            string focus = EventSystem.current?.currentSelectedGameObject?.name;
            bool rowFocus = EventSystem.current?.currentSelectedGameObject?.transform.IsChildOf(_list) == true;
            TeamUiArt.Clear(_list);
            foreach (var tab in _tabs)
            {
                tab.Value.interactable = !_loading;
                tab.Value.GetComponent<Image>().sprite = TeamUiArt.Load(tab.Key == Source ? "button_primary" : "button_secondary");
                tab.Value.GetComponentInChildren<TMP_Text>().color = tab.Key == Source ? TeamUiArt.Cream : Ink;
            }
            _refresh.interactable = _searchButton.interactable = !_loading;
            _previous.interactable = PageIndex > 0;
            _next.interactable = !_loading && (PageIndex + 1 < pages || _hasMore);
            _next.GetComponentInChildren<TMP_Text>().text = PageIndex + 1 == pages && _hasMore ? "加载更多" : "下一页";
            _page.text = $"{PageIndex + 1} / {pages} 页 · {visible.Count} 位";
            _notice.text = _loading ? "正在查找道友…" : !string.IsNullOrWhiteSpace(_directoryStatus) ? _directoryStatus
                : Source == TeamInvitationSource.Chat ? "从世界聊天中选择道友；也可点击聊天头像，选择邀请组队。"
                : "选择道友发出邀请，对方同意后入队；邀请 60 秒内有效。";
            _create.gameObject.SetActive(_state?.HasLoaded == true && !_state.HasTeam);
            _create.interactable = CanCreate;
            var manual = new TeamRole { PlayerId = ulong.MaxValue, IsOnline = true };
            _manualInvite.interactable = BlockReason(_state, manual) == null;
            _status.text = !string.IsNullOrEmpty(_localStatus) ? _localStatus
                : !string.IsNullOrEmpty(_state?.Status) ? _state.Status
                : _state?.HasLoaded != true ? "正在同步队伍信息…"
                : !_state.HasTeam ? "先创建队伍，再邀请道友同行。"
                : !_state.IsLeader ? "只有队长可以邀请道友入队。"
                : _state.IsFull ? "队伍已满，暂时无法邀请。" : "邀请成功后显示“已邀请”，请等候道友答复。";
            if (visible.Count == 0)
            {
                TeamUiArt.Art(_list, "application_card", 0, 0, 2024, 420);
                Label(_list, _loading ? "正在寻访道友" : "暂无符合条件的道友", 200, 128, 1624, 72, 40, Jade, TextAlignmentOptions.Center);
                Label(_list, query.Length > 0 ? "试试其他姓名或编号，也可以清空搜索后刷新。"
                    : Source == TeamInvitationSource.Friends ? "好友上线后，可在这里发出组队邀请。"
                    : Source == TeamInvitationSource.Nearby ? "来到有其他道友的地方，再刷新周围玩家。"
                    : Source == TeamInvitationSource.Chat ? "先在世界频道查看聊天，或点击聊天头像发起邀请。"
                    : _hasMore ? "本页暂无匹配道友，可以继续加载更多。" : "稍后刷新查看在线道友。", 180, 214, 1664, 56, 28, Muted, TextAlignmentOptions.Center);
            }
            for (int i = 0; i < PageSize && PageIndex * PageSize + i < visible.Count; i++)
            {
                var role = visible[PageIndex * PageSize + i];
                var row = QdaoUguiFactory.CreateRect("TeamInvitationCandidate_" + role.PlayerId, _list,
                    i % 2 * 1028, i / 2 * 144, 996, 132);
                TeamUiArt.Art(row, "application_card", 0, 0, 996, 132);
                Portrait(row, role);
                Label(row, TeamViewMapper.DisplayName(role), 142, 7, 568, 49, 30, Jade);
                string details = (role.Level > 0 ? role.Level + " 级" : "") +
                    (!string.IsNullOrEmpty(role.SchoolName) ? "  " + role.SchoolName : "") +
                    (!role.OnlineStatusKnown ? "  近期发言 · 状态待确认" : role.IsOnline ? "  在线" : "  离线");
                Label(row, details.Trim(), 142, 50, 568, 42, 23, Muted);
                Label(row, "编号 " + role.PlayerId, 142, 87, 568, 37, 22, Muted);
                string reason = BlockReason(_state, role);
                ulong id = role.PlayerId;
                Control(row, "InviteCandidate_" + id, reason ?? "邀请", 736, 33, 232, 66,
                    () => InviteCandidate(id), reason == null, enabled: reason == null, size: 26);
            }
            if (rowFocus && EventSystem.current != null)
            {
                var candidate = _list.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == focus && b.interactable);
                EventSystem.current.SetSelectedGameObject(candidate != null ? candidate.gameObject : _close.gameObject);
            }
        }

        private void InviteCandidate(ulong id)
        {
            var role = _candidates.Find(candidate => candidate.PlayerId == id);
            string reason = BlockReason(_state, role);
            if (reason != null) { _localStatus = reason; Render(); return; }
            _localStatus = "";
            InviteRequested?.Invoke(id);
        }

        private void InviteById()
        {
            if (!ulong.TryParse(_manualId.text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) || id == 0)
            { _localStatus = "请输入正确的玩家编号（纯数字）。"; Render(); return; }
            var role = _candidates.Find(candidate => candidate.PlayerId == id) ?? new TeamRole { PlayerId = id, OnlineStatusKnown = false };
            string reason = BlockReason(_state, role);
            if (reason != null) { _localStatus = reason; Render(); return; }
            _localStatus = "";
            InviteRequested?.Invoke(id);
        }

        private static void Portrait(UnityEngine.Transform parent, TeamRole role)
        {
            string character = role.CharacterId;
            if (string.IsNullOrEmpty(character) && role.ClassId >= 1 && role.ClassId <= 4 && (role.Gender == 1 || role.Gender == 2))
                character = QdaoCharacterCatalog.ResolveRole(role.ClassId, role.Gender);
            var sprite = QdaoCharacterCatalog.LoadPortrait(character);
            var mask = QdaoUguiFactory.CreateImage("InvitationPortraitMask", parent, 20, 15, 100, 100,
                QdaoUguiTheme.RequireSprite(QdaoUguiTheme.StatusDotSpritePath));
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var portrait = QdaoUguiFactory.CreateImage("InvitationPortrait_" + role.PlayerId, mask.transform,
                -50, -7, 200, 200, sprite);
            portrait.preserveAspect = true;
            portrait.gameObject.SetActive(sprite != null);
            TeamUiArt.Art(parent, "portrait_frame", 20, 15, 100, 100);
            if (sprite == null) Label(parent, "道友", 24, 40, 92, 52, 24, Muted, TextAlignmentOptions.Center);
        }

        private static TMP_Text Label(UnityEngine.Transform parent, string text, float x, float y, float w, float h,
            float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var label = TeamUiArt.Text(parent, text, x, y, w, Mathf.Max(h, size * 1.65f), size, color, alignment);
            label.font = Social.SocialUiArt.BodyFont;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        private static Button Control(UnityEngine.Transform parent, string name, string text, float x, float y, float w, float h,
            Action click, bool primary = false, bool enabled = true, float size = 30, string key = null)
        {
            var button = TeamUiArt.Button(parent, text, x, y, w, h, click, primary, enabled, key, size);
            button.name = name;
            button.GetComponentInChildren<TMP_Text>(true).font = Social.SocialUiArt.BodyFont;
            return button;
        }

        private static TMP_InputField Input(UnityEngine.Transform parent, string name, float x, float y, float w, float h,
            string hint, int limit)
        {
            TeamUiArt.Art(parent, "application_card", x, y, w, h);
            var input = QdaoUguiFactory.CreateInputField(name, parent, x + 16, y, w - 32, h, hint, limit);
            input.textComponent.font = Social.SocialUiArt.BodyFont;
            input.textComponent.fontSize = 28;
            input.textComponent.richText = false;
            input.textComponent.color = Ink;
            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.font = Social.SocialUiArt.BodyFont;
                placeholder.fontSize = 27;
                placeholder.richText = false;
                placeholder.color = Muted;
            }
            return input;
        }
    }
}
