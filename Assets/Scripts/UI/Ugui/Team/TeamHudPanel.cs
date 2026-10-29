using System;
using MmorpgClient.Game.Team;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.World;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Team
{
    /// <summary>共用世界栏中的轻量队伍视图；只读现有队伍真值，动作回到原组队及跟随宿主。</summary>
    public sealed class TeamHudPanel : IDisposable
    {
        public const string SummonUnavailableReason = "召集暂不可用：尚未定义全员召集规则。";
        private const float RowHeight = 76;
        private const float ListHeight = 270;
        private const float RowWidth = 396;
        private readonly RectTransform _root, _rows;
        private readonly TMP_Text _summary, _status, _followStatus;
        private readonly Button _list, _leave, _summon, _follow;
        private readonly TMP_Text _followLabel;
        private readonly ScrollRect _scroll;
        private TeamUiRoot _owner;
        private TeamUiState _state;
        private bool _fresh, _disposed;
        private TeamFollowStatus _shownFollow = (TeamFollowStatus)(-1);

        public RectTransform Root => _root;

        public TeamHudPanel(RectTransform mount)
        {
            if (mount == null) throw new ArgumentNullException(nameof(mount));
            _root = QdaoUguiFactory.CreateRect("TeamHudPanel", mount, 0, 0, 416, 582);
            // 旧TeamV2/title_plate边缘带示例人数，使用已核查无文字的玉绿页签底板。
            var title = QdaoUguiFactory.CreateImage("TeamHudTitlePlate", _root, 0, 0, 416, 44,
                QdaoUguiTheme.RequireSprite("UI/Ugui/WorldHud20261022/chrome/tab-selected"));
            title.type = Image.Type.Sliced;
            Label("结伴同行", "TeamHudTitle", _root, 18, 2, 380, 40, 27,
                TeamUiArt.Cream, TextAlignmentOptions.Center);
            _summary = Label("正在同步队伍…", "TeamHudSummary", _root, 8, 48, 400, 30, 23);
            _status = Label("", "TeamHudStatus", _root, 8, 82, 400, 44, 19, GameplayUiArt.Muted);
            _status.textWrappingMode = TextWrappingModes.Normal;
            _status.alignment = TextAlignmentOptions.TopLeft;

            var viewport = QdaoUguiFactory.CreateRect("TeamHudViewport", _root, 0, 130, 416, ListHeight);
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(1, 1, 1, .001f);
            hit.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            _rows = QdaoUguiFactory.CreateRect("TeamHudRows", viewport, 0, 0, RowWidth, ListHeight);
            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.content = _rows;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 36;
            var rail = QdaoUguiFactory.CreateRect("TeamHudScrollbar", viewport, 402, 0, 14, ListHeight);
            var railImage = rail.gameObject.AddComponent<Image>();
            railImage.color = QdaoUguiTheme.Html("#E7D6B2");
            var handle = QdaoUguiFactory.CreateStretch("Handle", rail, new Vector4(2, 2, 2, 2));
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = GameplayUiArt.Gold;
            var scrollbar = rail.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            _scroll.verticalScrollbar = scrollbar;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            _list = Control("列表", "TeamHudList", 0, 414, 132, OpenList);
            _leave = Control("离队", "TeamHudLeave", 142, 414, 132, Leave);
            _summon = Control("召集", "TeamHudSummon", 284, 414, 132, null, true);
            _summon.interactable = false;
            _follow = Control(TeamFollowUiRoot.LabelAway, "TeamHudFollow", 0, 474, 132, ToggleFollow);
            _followLabel = _follow.GetComponentInChildren<TMP_Text>(true);
            _followStatus = Label("", "TeamHudFollowStatus", _root, 146, 476, 268, 42, 20, GameplayUiArt.Muted);
            var reason = Label(SummonUnavailableReason, "TeamHudSummonReason", _root, 8, 532, 400, 45, 18, GameplayUiArt.Muted);
            reason.textWrappingMode = TextWrappingModes.Normal;
            reason.alignment = TextAlignmentOptions.TopLeft;
            Render();
        }

        /// <summary>同一宿主反复调用不会增加订阅；换宿主先解绑并清空旧主体显示。</summary>
        public void Bind(TeamUiRoot root)
        {
            if (_disposed || (ReferenceEquals(_owner, root) && (_state == null || root != null))) return;
            if (_state != null) _state.Changed -= StateChanged;
            _owner = root;
            _state = root != null ? root.State : null;
            _fresh = root != null && root.Client != null && root.Client.HasFreshView;
            if (_state != null) _state.Changed += StateChanged;
            _shownFollow = (TeamFollowStatus)(-1);
            _scroll.verticalNormalizedPosition = 1;
            Render();
        }

        /// <summary>显式离线视图入口，用于原生截图和UI测试；不创建网络客户端。</summary>
        public void SetState(TeamUiState state, bool fresh)
        {
            if (_disposed) return;
            if (_state != null) _state.Changed -= StateChanged;
            _owner = null;
            _state = state;
            _fresh = fresh;
            if (_state != null) _state.Changed += StateChanged;
            Render();
        }

        public void Tick()
        {
            if (_disposed) return;
            if (_owner != null)
            {
                bool fresh = _owner.Client != null && _owner.Client.HasFreshView;
                if (_fresh != fresh) { _fresh = fresh; Render(); }
            }
            RenderFollow();
        }

        private void StateChanged()
        {
            if (_owner != null) _fresh = _owner.Client != null && _owner.Client.HasFreshView;
            Render();
        }

        private bool CanLeave => _state != null && _state.HasLoaded && _state.HasTeam &&
            _fresh && _state.ServiceAvailable && !_state.IsBusy && !_state.MatchStarting;

        private void Render()
        {
            if (_disposed || _root == null) return;
            // 空列表的归一化位置会被ScrollRect返回为0；首次出现溢出名单时应从顶部开始。
            float position = _rows.rect.height > _scroll.viewport.rect.height + .5f ? _scroll.verticalNormalizedPosition : 1;
            GameplayUiArt.Clear(_rows);
            bool loaded = _state != null && _state.HasLoaded;
            bool hasTeam = loaded && _state.HasTeam;
            var snapshot = _state?.Snapshot;
            _summary.text = !loaded ? "正在同步队伍…" : !hasTeam ? "尚未加入队伍" :
                $"队伍 {snapshot.Members.Count} / {snapshot.Capacity}" + (_state.IsLeader ? " · 你是队长" : " · 队员");
            _status.text = StatusText();
            // 大窗也是查看失败原因与重新同步的入口；业务服务不可用不应封死只读导航。
            _list.interactable = _owner != null;
            _leave.interactable = CanLeave;
            _summon.interactable = false;
            int rowCount = 0;
            if (hasTeam)
            {
                // TeamViewMapper已按显式JoinSeq稳定排序；行名使用玩家身份，不从下标推导成员身份。
                foreach (var role in snapshot.Members)
                {
                    if (role == null) continue;
                    MemberRow(role, snapshot, rowCount++ * RowHeight);
                }
                int emptyCount = Math.Max(0, snapshot.Capacity - snapshot.Members.Count);
                for (int i = 0; i < emptyCount; ++i)
                    EmptyRow(i, rowCount++ * RowHeight);
            }
            else
            {
                Label(!loaded ? "等待服务端同步队伍资料" : "打开列表，寻找结伴同行的道友。", "TeamHudEmpty",
                    _rows, 26, 78, 344, 86, 24, GameplayUiArt.Muted, TextAlignmentOptions.Center)
                    .textWrappingMode = TextWrappingModes.Normal;
            }
            _rows.sizeDelta = new Vector2(RowWidth, Mathf.Max(ListHeight, rowCount * RowHeight));
            _scroll.verticalNormalizedPosition = position;
            _shownFollow = (TeamFollowStatus)(-1);
            RenderFollow();
        }

        private string StatusText()
        {
            if (_state == null) return "组队服务尚未连接。";
            if (!_state.ServiceAvailable) return string.IsNullOrWhiteSpace(_state.Status) ? "组队服务尚未连接。" : _state.Status;
            if (!_state.HasLoaded) return string.IsNullOrWhiteSpace(_state.Status) ? "正在等待首份队伍视图…" : _state.Status;
            if (!_fresh) return string.IsNullOrWhiteSpace(_state.Status) ? "队伍信息待确认，正在重新同步；操作已暂停。" :
                _state.Status + "（信息待确认，操作已暂停）";
            if (_state.IsLoading) return "正在刷新队伍资料…";
            if (_state.IsBusy) return string.IsNullOrWhiteSpace(_state.Status) ? "队伍操作处理中…" : _state.Status;
            if (!string.IsNullOrWhiteSpace(_state.Status)) return _state.Status;
            if (_state.MatchStarting) return "正在集合开战，队伍名单已锁定。";
            if (_state.MatchQueued) return $"正在等待补位 · 目标 {_state.Snapshot.MatchTargetSize} 人";
            return _state.HasTeam ? "队伍资料已同步" : "可在列表中创建队伍、申请或处理邀请。";
        }

        private void MemberRow(TeamRole role, TeamSnapshot snapshot, float y)
        {
            var row = QdaoUguiFactory.CreateRect("TeamHudMember_" + role.PlayerId, _rows, 0, y, RowWidth, 72);
            TeamUiArt.Art(row, "member_row", 0, 0, RowWidth, 72);
            Portrait(row, role);
            Label(TeamViewMapper.DisplayName(role), "TeamHudName_" + role.PlayerId, row, 82, 5, 214, 32, 24);
            string level = role.Level > 0 ? $"{role.Level}级" : "等级未知";
            string school = !string.IsNullOrWhiteSpace(role.SchoolName) ? role.SchoolName :
                role.ClassId switch { 1 => "破军", 2 => "玄霄", 3 => "丹心", 4 => "逐风", _ => "门派未知" };
            Label(level + " · " + school, "TeamHudDetail_" + role.PlayerId, row, 82, 38, 214, 28, 18, GameplayUiArt.Muted);
            bool leader = role.PlayerId == snapshot.LeaderId;
            bool self = role.PlayerId == snapshot.LocalPlayerId;
            Label(leader ? (self ? "队长·自己" : "队长") : self ? "自己" : "队员", "TeamHudRole_" + role.PlayerId,
                row, 296, 6, 90, 30, 18, GameplayUiArt.Ink, TextAlignmentOptions.MidlineRight);
            string presence = !role.OnlineStatusKnown ? "状态未知" : !role.IsOnline ? "离线" : role.InBattle ? "战斗中" : "在线";
            Label(presence, "TeamHudPresence_" + role.PlayerId, row, 296, 39, 90, 28, 18,
                role.OnlineStatusKnown && role.IsOnline ? GameplayUiArt.Ink : GameplayUiArt.Muted, TextAlignmentOptions.MidlineRight);
        }

        private void Portrait(RectTransform row, TeamRole role)
        {
            string id = role.CharacterId;
            if (string.IsNullOrEmpty(id) && role.ClassId >= 1 && role.ClassId <= 4 && (role.Gender == 1 || role.Gender == 2))
                id = QdaoCharacterCatalog.ResolveRole(role.ClassId, role.Gender);
            var sprite = QdaoCharacterCatalog.LoadPortrait(id);
            var circle = QdaoUguiTheme.RequireSprite(QdaoUguiTheme.StatusDotSpritePath);
            var border = QdaoUguiFactory.CreateImage("TeamHudPortraitBorder", row, 7, 4, 64, 64, circle);
            border.color = GameplayUiArt.Gold;
            var mask = QdaoUguiFactory.CreateImage("TeamHudPortraitMask", row, 9, 6, 60, 60, circle);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var portrait = QdaoUguiFactory.CreateImage("TeamHudPortrait_" + role.PlayerId, mask.transform,
                -30, -4.2f, 120, 120, sprite);
            portrait.preserveAspect = true;
            portrait.gameObject.SetActive(sprite != null);
            if (sprite == null) Label("待同步", "TeamHudPortraitUnknown_" + role.PlayerId,
                row, 10, 22, 58, 28, 16, GameplayUiArt.Muted, TextAlignmentOptions.Center);
        }

        private void EmptyRow(int index, float y)
        {
            var row = QdaoUguiFactory.CreateRect("TeamHudEmptySlot_" + index, _rows, 0, y, RowWidth, 72);
            TeamUiArt.Art(row, "member_row", 0, 0, RowWidth, 72);
            var invite = TeamUiArt.Button(row, "+", 11, 10, 52, 52, OpenList, false,
                _owner != null && _state?.ServiceAvailable == true, fontSize: 30);
            invite.name = "TeamHudEmptySlotAction_" + index;
            Label("空余席位", "TeamHudEmptySlotLabel", row, 82, 8, 280, 30, 23);
            Label("打开列表邀请道友", "TeamHudEmptySlotHint", row, 82, 38, 280, 26, 18, GameplayUiArt.Muted);
        }

        private void RenderFollow()
        {
            var status = TeamFollowUiRoot.Instance != null ? TeamFollowUiRoot.Instance.Follow.Status : TeamFollowStatus.None;
            if (_shownFollow == status) return;
            _shownFollow = status;
            bool member = _state != null && _state.HasLoaded && _state.HasTeam && !_state.IsLeader;
            // 暂离只释放本地输入，过期视图也不能阻止玩家停下跟随；归队可能请求服务端移动，须有新鲜视图。
            _follow.interactable = _owner != null && member && (TeamFollowUiRoot.Instance?.CanToggleAway ?? false);
            _followLabel.text = status == TeamFollowStatus.Away ? TeamFollowUiRoot.LabelRejoin : TeamFollowUiRoot.LabelAway;
            _followStatus.text = status switch
            {
                TeamFollowStatus.Following => "正在跟随队长",
                TeamFollowStatus.Away => "已暂离 · 可以自己走",
                TeamFollowStatus.LeaderOutOfSight => "队长暂时不在视野内",
                _ => _state?.IsLeader == true ? "你是队长，无需跟随" : "跟随暂不可用"
            };
        }

        private void OpenList() => _owner?.OpenTeamList();

        /// <summary>暂离只释放本地输入；归队会提交服务端意图，必须有新鲜视图及可用服务。</summary>
        public static bool CanToggleFollow(TeamFollowStatus status, bool fresh, bool serviceAvailable) => status switch
        {
            TeamFollowStatus.Following or TeamFollowStatus.LeaderOutOfSight => true,
            TeamFollowStatus.Away => fresh && serviceAvailable,
            _ => false
        };

        private void Leave() { if (CanLeave) _owner?.RequestLeaveConfirmation(); }
        private void ToggleFollow()
        {
            if (!_follow.interactable) return;
            TeamFollowUiRoot.Instance?.ToggleAway();
            var events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject == _follow.gameObject)
                events.SetSelectedGameObject(null);
            _shownFollow = (TeamFollowStatus)(-1);
            RenderFollow();
        }

        private Button Control(string caption, string name, float x, float y, float width, Action action, bool primary = false)
        {
            var button = TeamUiArt.Button(_root, caption, x, y, width, 44, action, primary, fontSize: 23);
            button.name = name;
            return button;
        }

        private static TMP_Text Label(string value, string name, UnityEngine.Transform parent, float x, float y,
            float width, float height, float size, Color? color = null, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var text = GameplayUiArt.Text(parent, value, x, y, width, height, size, color ?? GameplayUiArt.Ink, alignment: alignment);
            text.name = name;
            return text;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_state != null) _state.Changed -= StateChanged;
            _state = null;
            _owner = null;
            if (_root == null) return;
            _root.gameObject.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(_root.gameObject);
            else UnityEngine.Object.DestroyImmediate(_root.gameObject);
        }
    }
}
