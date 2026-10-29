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
    /// 队伍状态只读 <see cref="TeamUiRoot"/> 的 TeamClient;唯一会发的组队请求是驱动要求的归队
    /// (TeamClient.RequestRegroup,排进 TeamUiRoot 每帧的 DrainQueued 里发出)。
    /// 只认已经应用过权威视图的快照:探针回来之前 TeamClient.Snapshot 是占位的"无队",
    /// 那时传 null 给驱动(表示"不知道"),既不接管,也不把它当成离队。
    /// 战斗 / 观战画面亮着、没进游戏时不跟随。
    /// </summary>
    public sealed class TeamFollowUiRoot : MonoBehaviour
    {
        public static TeamFollowUiRoot Instance { get; private set; }

        /// <summary>组队跟随的本地驱动;界面只读它的状态,开关走 <see cref="ToggleAway"/>。</summary>
        public TeamFollowDriver Follow => _follow;

        /// <summary>新旧入口共用同一权限；暂离释放本地控制，归队须先确认当前队伍。</summary>
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
        /// <summary>跟随中玩家反复点地面时,提示最多这么久弹一次。</summary>
        public const float HintIntervalSeconds = 3f;

        private readonly TeamFollowDriver _follow = new();
        private RectTransform _hud;
        private Button _toggle;
        private TMP_Text _toggleLabel;
        private TeamFollowStatus _shownStatus = TeamFollowStatus.None;
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
            _hud.gameObject.SetActive(false);
            _follow.InputRejected += ShowInputHint;
            _follow.RegroupRequested += RequestRegroup;
            // 联机验收排障:环境变量 MMORPG_FOLLOW_TRACE=1 时把跟随驱动的诊断行写进播放器日志([follow] 前缀)。
            if (System.Environment.GetEnvironmentVariable("MMORPG_FOLLOW_TRACE") == "1")
                _follow.Trace = message => Debug.Log("[follow] " + message);
        }

        private void Update()
        {
            if (_hud == null) return; // 重复实例:Awake 里已经请求销毁,这一帧什么都不做
            var game = AppBootstrap.Instance?.GameClient;
            // TeamUiRoot 跟着 GameClient 实例重建 TeamClient;换会话的那一帧这里可能还拿着旧的,
            // 但此时新会话还没进游戏,_available 为假,不会据旧视图接管。
            var client = TeamUiRoot.Instance?.Client;
            bool inGame = game != null && game.InGame && game.IsGateReady;
            _available = inGame && !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false);
            _hud.gameObject.SetActive(_available);

            bool trusted = client != null && client.HasView;
            _follow.Tick(game, trusted ? client.Snapshot : null, _available && trusted, Time.realtimeSinceStartup);

            var status = _follow.Status;
            _toggle.interactable = CanToggleAway;
            if (status == _shownStatus) return;
            _shownStatus = status;
            _toggle.gameObject.SetActive(status != TeamFollowStatus.None);
            // 按钮写的是"点了会发生什么":暂离中 → 归队(回到跟随,并请服务端把人带到队长身边);
            // 跟随中、看不到队长时 → 暂离。看不到队长时驱动会自动请求归队,想自己走就得能点暂离。
            if (_toggleLabel != null)
                _toggleLabel.text = status == TeamFollowStatus.Away ? LabelRejoin : LabelAway;
        }

        private void RequestRegroup() => TeamUiRoot.Instance?.Client?.RequestRegroup();

        /// <summary>暂离 / 归队。暂离是纯本地开关;归队时驱动另外请求服务端把人带到队长身边(见 TeamRegroupPolicy)。</summary>
        public void ToggleAway()
        {
            if (!CanToggleAway) return;
            _follow.SetAway(!_follow.Away);
            // 点完之后按钮还是界面的"当前选中项":紧接着用键盘走路时,空格 / 回车会被当成再点一次,
            // 刚暂离就又被接管回去。
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

        private void OnDestroy()
        {
            _follow.InputRejected -= ShowInputHint;
            _follow.RegroupRequested -= RequestRegroup;
            _follow.Release();
            if (Instance == this) Instance = null;
        }
    }
}
