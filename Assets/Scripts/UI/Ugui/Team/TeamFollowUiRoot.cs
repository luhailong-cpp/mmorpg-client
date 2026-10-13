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
    /// 队伍状态只读 <see cref="TeamUiRoot"/> 的 TeamClient,自己不发任何组队请求。
    /// 只认已经应用过权威视图的快照:探针回来之前 TeamClient.Snapshot 是占位的"无队",
    /// 那时传 null 给驱动(表示"不知道"),既不接管,也不把它当成离队。
    /// 战斗 / 观战画面亮着、没进游戏时不跟随。
    /// </summary>
    public sealed class TeamFollowUiRoot : MonoBehaviour
    {
        public static TeamFollowUiRoot Instance { get; private set; }

        /// <summary>组队跟随的本地驱动;界面只读它的状态,开关走 <see cref="ToggleAway"/>。</summary>
        public TeamFollowDriver Follow => _follow;

        /// <summary>与组队画布同层(TeamUiRoot 用 186)。</summary>
        public const int CanvasSortingOrder = 186;
        public const string ToggleName = "TeamFollowToggle";
        // 开关贴在组队入口右侧:左列往下已经排满(帮会 / 聚宝斋 / 仙笺 / 仙友会)。
        public const float ToggleX = TeamUiRoot.EntryX + BattleUiStyle.HudEntryWidth + 16;
        public const float ToggleY = TeamUiRoot.EntryY;
        public const float ToggleWidth = 210;
        public const string LabelAway = "暂离";
        public const string LabelRejoin = "归队";
        public const string LabelSearching = "寻找队长";
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
            if (status == _shownStatus) return;
            _shownStatus = status;
            _toggle.gameObject.SetActive(status != TeamFollowStatus.None);
            // 按钮写的是"点了会发生什么":跟随中 → 暂离;暂离中 → 归队;看不到队长时没有可切换的东西,只说明现状。
            _toggle.interactable = status != TeamFollowStatus.LeaderOutOfSight;
            if (_toggleLabel != null)
                _toggleLabel.text = status == TeamFollowStatus.Away ? LabelRejoin
                    : status == TeamFollowStatus.LeaderOutOfSight ? LabelSearching
                    : LabelAway;
        }

        /// <summary>暂离 / 归队。纯本地开关:只决定本机要不要自动跟着队长走。</summary>
        public void ToggleAway()
        {
            if (!_available) return;
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
            _follow.Release();
            if (Instance == this) Instance = null;
        }
    }
}
