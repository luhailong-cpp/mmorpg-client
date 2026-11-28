using MmorpgClient.Game;
using MmorpgClient.Game.Team;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Team
{
    /// <summary>
    /// 组队跟随的宿主:持有 <see cref="TeamFollowDriver"/> 并每帧驱动它,HUD 上放一个「暂离 / 归队」开关。
    /// 队伍状态只读 <see cref="TeamUiRoot"/> 的 TeamClient;跟随声明由驱动经 GameClient.SendTeamFollow 发给场景
    /// (服务器驱动走位,服务端 docs/design/team-follow-server-trail.md),不再走 TeamClient.RequestRegroup。
    /// 只认已经应用过权威视图的快照:探针回来之前 TeamClient.Snapshot 是占位的"无队",
    /// 那时传 null 给驱动(表示"不知道"),既不声明,也不把它当成离队。
    /// 战斗 / 观战画面亮着、没进游戏时不发声明。
    /// </summary>
    public sealed class TeamFollowUiRoot : MonoBehaviour
    {
        public static TeamFollowUiRoot Instance { get; private set; }

        /// <summary>组队跟随的本地驱动;界面只读它的状态,开关走 <see cref="ToggleAway"/>。</summary>
        public TeamFollowDriver Follow => _follow;

        /// <summary>新旧入口共用同一权限；暂离随时可点(停止驱动不能被视图挡住)，归队须先确认当前队伍。</summary>
        public bool CanToggleAway => _available && TeamHudPanel.CanToggleFollow(_follow.Status,
            TeamUiRoot.Instance?.Client?.HasFreshView == true, TeamUiRoot.Instance?.Client?.ServiceAvailable == true);

        /// <summary>与组队画布同层(TeamUiRoot 用 186)。</summary>
        public const int CanvasSortingOrder = 186;
        public const string ToggleName = "TeamFollowToggle";
        // 开关贴在组队入口右侧:左列往下已经排满(帮会 / 聚宝斋 / 仙笺 / 仙友会)。
        public const float ToggleX = TeamUiRoot.EntryX + BattleUiStyle.HudEntryWidth + 16;
        public const float ToggleY = TeamUiRoot.EntryY;
        public const float ToggleWidth = 210;
        public const string LabelAway = "暂离";
        public const string LabelRejoin = "归队";
        public const string InputHint = "正在跟随队长，点「暂离」后可以自己走。";
        public const string RecallBannerName = "TeamFollowRecallBanner";
        public const string RecallAcceptName = "TeamFollowRecallAccept";
        public const float RecallBannerDurationSeconds = 12f;
        /// <summary>跟随中玩家反复点地面时,提示最多这么久弹一次。</summary>
        public const float HintIntervalSeconds = 3f;

        private readonly TeamFollowDriver _follow = new();
        private RectTransform _hud;
        private Button _toggle;
        private TMP_Text _toggleLabel;
        private GameObject _stateBadge;
        private TMP_Text _stateBadgeLabel;
        private GameObject _recallBanner;
        private TMP_Text _recallBannerText;
        private float _recallBannerExpiresAt;
        private TeamClient _boundTeamClient;
        private TeamFollowStatus _shownStatus = TeamFollowStatus.None;
        private ProtagonistPresentationMode _shownStateBadgeMode = ProtagonistPresentationMode.Free;
        private MmorpgClient.Game.GameClient _boundGameClient;
        private bool _available;
        private float _nextHintAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[TeamFollowUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<TeamFollowUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            var canvasObject = new GameObject("TeamFollowCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform, 2560, 1080);
            _hud = QdaoUguiFactory.CreateStretch("HudRoot", design, Vector4.zero);
            QdaoUguiFactory.ConfigureHudCanvas(_hud);
            _toggle = GameplayUiArt.Button(_hud, LabelAway, ToggleX, ToggleY,
                ToggleWidth, BattleUiStyle.HudEntryHeight, ToggleAway, false, fontSize: 32);
            _toggle.name = ToggleName;
            _toggleLabel = _toggle.GetComponentInChildren<TMP_Text>();
            _toggle.gameObject.SetActive(false);
            WorldHudNavigation.Register(WorldHudEntry.TeamFollow,
                (RectTransform)_toggle.transform, _toggle, _toggleLabel);
            BuildStateBadge();
            BuildRecallBanner(design);
            _hud.gameObject.SetActive(false);
            _follow.InputRejected += ShowInputHint;
            // 联机验收排障:环境变量 MMORPG_FOLLOW_TRACE=1 时把跟随驱动的诊断行写进播放器日志([follow] 前缀)。
            if (System.Environment.GetEnvironmentVariable("MMORPG_FOLLOW_TRACE") == "1")
                _follow.Trace = message => Debug.Log("[follow] " + message);
        }

        private void Update()
        {
            if (_hud == null) return; // 重复实例:Awake 里已经请求销毁,这一帧什么都不做
            var game = AppBootstrap.Instance?.GameClient;
            // TeamUiRoot 跟着 GameClient 实例重建 TeamClient;换会话的那一帧这里可能还拿着旧的,
            // 但此时新会话还没进游戏,_available 为假,不会据旧视图发声明。
            var client = TeamUiRoot.Instance?.Client;
            BindTeamClient(client);
            bool inGame = game != null && game.InGame && game.IsGateReady;
            BindGameState(inGame ? game : null);
            _available = inGame && !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false);
            _hud.gameObject.SetActive(_available);
            RefreshStateBadge(game);

            bool trusted = client != null && client.HasView;
            // allowed 只看「是否已进游戏且不在战斗」,不与 trusted 绑在一起:托管重连接回时服务器先推 driven=true,
            // 队伍快照尚未拉回也能立刻显示「正在跟随」并允许点「暂离」撤回。
            _follow.Tick(game, trusted ? client.Snapshot : null, _available, Time.realtimeSinceStartup);
            RefreshRecallBanner();

            var status = _follow.Status;
            _toggle.interactable = CanToggleAway;
            if (status == _shownStatus) return;
            _shownStatus = status;
            _toggle.gameObject.SetActive(status != TeamFollowStatus.None);
            // 按钮写的是"点了会发生什么":暂离中 → 归队(重新声明跟随,服务器把人带回队长身后);
            // 跟随中、还没接上队长时 → 暂离。没接上时驱动会自动重新声明,想自己走就得能点暂离。
            if (_toggleLabel != null)
                _toggleLabel.text = status == TeamFollowStatus.Away ? LabelRejoin : LabelAway;
        }

        /// <summary>暂离 / 归队:暂离撤回跟随声明(服务器立刻停止驱动),归队重新声明(见 TeamFollowDeclaration)。</summary>
        public void ToggleAway()
        {
            if (!CanToggleAway) return;
            _follow.SetAway(!_follow.Away);
            // 点完之后按钮还是界面的"当前选中项":紧接着用键盘走路时,空格 / 回车会被当成再点一次,
            // 刚暂离就又声明回跟随。
            var events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject == _toggle.gameObject)
                events.SetSelectedGameObject(null);
        }

        private void ShowInputHint()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextHintAt) return;
            _nextHintAt = now + HintIntervalSeconds;
            BattleUiRoot.Instance?.ShowToast(InputHint);
        }

        private void BuildStateBadge()
        {
            if (_hud == null) return;
            var rect = QdaoUguiFactory.CreateRect("ProtagonistStateBadge", _hud,
                ToggleX, ToggleY + BattleUiStyle.HudEntryHeight + 8f, ToggleWidth, 34f);
            var bg = rect.gameObject.AddComponent<Image>();
            bg.sprite = GameplayUiArt.Load("paper_tile");
            if (bg.sprite != null && bg.sprite.border.sqrMagnitude > 0f) bg.type = Image.Type.Sliced;
            bg.color = new Color(0.14f, 0.17f, 0.16f, 0.88f);
            bg.raycastTarget = false;
            _stateBadgeLabel = QdaoUguiFactory.CreateText("Text", rect, 8f, 2f, ToggleWidth - 16f, 30f,
                string.Empty, 18f, BattleUiStyle.WarnText, TextAlignmentOptions.Center);
            _stateBadgeLabel.raycastTarget = false;
            _stateBadge = rect.gameObject;
            _stateBadge.SetActive(false);
        }

        private void BuildRecallBanner(RectTransform design)
        {
            var rect = QdaoUguiFactory.CreateRect(RecallBannerName, design, (2560f - 660f) * 0.5f, 148f, 660f, 84f);
            var bg = rect.gameObject.AddComponent<Image>();
            bg.sprite = GameplayUiArt.Load("paper_tile");
            if (bg.sprite != null && bg.sprite.border.sqrMagnitude > 0f) bg.type = Image.Type.Sliced;
            bg.color = new Color(0.11f, 0.20f, 0.16f, 0.94f);
            bool hasCustomBanner = GameplayUiArt.TryLoadAfkTeamArt("team_recall_banner_bg") != null;
            GameplayUiArt.ApplyOptionalAfkTeamSprite(bg, "team_recall_banner_bg");
            _recallBannerText = QdaoUguiFactory.CreateText("RecallText", rect,
                hasCustomBanner ? 32f : 22f, 16f, hasCustomBanner ? 348f : 360f, 52f,
                "队长发起了集合召集，是否立即归队？", 22f,
                hasCustomBanner ? GameplayUiArt.Ink : QdaoUguiTheme.Cream, TextAlignmentOptions.MidlineLeft);
            var accept = GameplayUiArt.Button(rect, "立即归队", 394f, 16f, 140f, 52f, AcceptLeaderRecall, true, fontSize: 22);
            accept.name = RecallAcceptName;
            GameplayUiArt.ApplyOptionalAfkTeamSprite(accept.image, "team_recall_btn_accept");
            var dismiss = GameplayUiArt.Button(rect, "稍后", 546f, 16f, 94f, 52f, DismissLeaderRecall, false, fontSize: 22);
            dismiss.name = "TeamFollowRecallDismiss";
            GameplayUiArt.ApplyOptionalAfkTeamSprite(dismiss.image, "team_recall_btn_dismiss");
            _recallBanner = rect.gameObject;
            _recallBanner.SetActive(false);
        }

        private void BindTeamClient(TeamClient client)
        {
            if (ReferenceEquals(_boundTeamClient, client)) return;
            UnbindTeamClient();
            if (client == null) return;
            _boundTeamClient = client;
            _boundTeamClient.LeaderRecalled += HandleLeaderRecalled;
        }

        private void UnbindTeamClient()
        {
            if (_boundTeamClient == null) return;
            _boundTeamClient.LeaderRecalled -= HandleLeaderRecalled;
            _boundTeamClient = null;
            DismissLeaderRecall();
        }

        private void HandleLeaderRecalled(ulong teamId, ulong leaderId)
        {
            if (!_available) return;
            if (_follow.Status == TeamFollowStatus.Following)
            {
                BattleUiRoot.Instance?.ShowToast("队长发起了集合召集");
                return;
            }
            if (_recallBanner == null) return;
            if (_recallBannerText != null)
                _recallBannerText.text = "队长发起了集合召集，是否立即归队？";
            _recallBannerExpiresAt = Time.realtimeSinceStartup + RecallBannerDurationSeconds;
            _recallBanner.SetActive(true);
        }

        private void AcceptLeaderRecall()
        {
            DismissLeaderRecall();
            if (!_available) return;
            _follow.SetAway(false);
            BattleUiRoot.Instance?.ShowToast("已请求归队跟随队长");
        }

        private void DismissLeaderRecall()
        {
            _recallBannerExpiresAt = 0f;
            if (_recallBanner != null && _recallBanner.activeSelf) _recallBanner.SetActive(false);
        }

        private void RefreshRecallBanner()
        {
            if (_recallBanner == null || !_recallBanner.activeSelf) return;
            if (!_available || _follow.Status == TeamFollowStatus.Following || _follow.Status == TeamFollowStatus.None ||
                Time.realtimeSinceStartup >= _recallBannerExpiresAt)
            {
                DismissLeaderRecall();
            }
        }

        private void BindGameState(MmorpgClient.Game.GameClient game)
        {
            if (ReferenceEquals(_boundGameClient, game)) return;
            UnbindGameState();
            if (game == null) return;
            _boundGameClient = game;
            _boundGameClient.OnSelfActorStateChanged += HandleSelfActorStateChanged;
        }

        private void UnbindGameState()
        {
            if (_boundGameClient == null) return;
            _boundGameClient.OnSelfActorStateChanged -= HandleSelfActorStateChanged;
            _boundGameClient = null;
        }

        private void HandleSelfActorStateChanged(SelfActorStateSnapshot prev, SelfActorStateSnapshot next)
        {
            if (BattleUiRoot.Instance?.IsBattleLayerVisible ?? false) return;
            if (!prev.IsSilenced && next.IsSilenced)
                BattleUiRoot.Instance?.ShowToast("已进入受控/沉默状态，行动与技能受限", true);
            else if (prev.IsSilenced && !next.IsSilenced)
                BattleUiRoot.Instance?.ShowToast("受控/沉默状态已解除");
        }

        private void RefreshStateBadge(MmorpgClient.Game.GameClient game)
        {
            if (_stateBadge == null || _stateBadgeLabel == null) return;
            var mode = _available && game?.Presentation != null
                ? game.Presentation.CurrentMode
                : ProtagonistPresentationMode.Free;
            if (mode == _shownStateBadgeMode) return;
            _shownStateBadgeMode = mode;
            string text = mode switch
            {
                ProtagonistPresentationMode.Controlled => "状态：沉默受控",
                ProtagonistPresentationMode.Mounted => "状态：骑乘中",
                ProtagonistPresentationMode.TeamFollow => "状态：组队跟随",
                _ => string.Empty,
            };
            bool visible = !string.IsNullOrEmpty(text);
            if (_stateBadge.activeSelf != visible) _stateBadge.SetActive(visible);
            if (visible) _stateBadgeLabel.text = text;
        }

        private void OnDisable()
        {
            UnbindTeamClient();
            UnbindGameState();
        }

        private void OnDestroy()
        {
            UnbindTeamClient();
            UnbindGameState();
            _follow.InputRejected -= ShowInputHint;
            _follow.Release();
            if (Instance == this) Instance = null;
        }
    }
}
