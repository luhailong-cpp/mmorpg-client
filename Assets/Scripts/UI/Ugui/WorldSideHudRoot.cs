using System;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Team;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui
{
    public enum WorldSideHudPage { Quests, Team }

    /// <summary>任务与组队的公共显示容器；内容保留各领域唯一数据源，切页不改变业务状态。</summary>
    public sealed class WorldSideHudRoot : MonoBehaviour
    {
        public const float Width = 440, Height = 660, ContentWidth = 416, ContentHeight = 582;
        public static WorldSideHudRoot Instance { get; private set; }
        public RectTransform QuestMount { get; private set; }
        public RectTransform TeamMount { get; private set; }
        public TeamHudPanel TeamContent => _teamPanel;
        public RectTransform Panel { get; private set; }
        public WorldSideHudPage SelectedPage { get; private set; }
        public bool IsCollapsed { get; private set; }
        public event Action VisibilityChanged;

        private RectTransform _design;
        private GameObject _surface;
        private Button _quests, _team, _collapse;
        private TMP_Text _collapseText;
        private TeamHudPanel _teamPanel;
        private bool _available;
        private object _gameIdentity;
        private ulong _playerId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[WorldSideHud]");
            DontDestroyOnLoad(go);
            go.AddComponent<WorldSideHudRoot>();
        }

        private void Awake() => EnsureBuilt();

        /// <summary>幂等创建正式UGUI树；编辑模式的隔离验证也使用同一入口。</summary>
        public void EnsureBuilt()
        {
            if (Panel != null) return;
            if (Instance != null && Instance != this)
            {
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
                return;
            }
            var canvasObject = new GameObject("WorldSideHudCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 138;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            _design = QdaoUguiFactory.CreateCenteredRect("WorldSideHudDesign", canvasObject.transform, 2560, 1080);
            Panel = QdaoUguiFactory.CreateRect("WorldSideHudPanel", _design, 1704, 188, Width, Height);
            var surface = QdaoUguiFactory.CreateImage("SideHudPaper", Panel, 0, 0, Width, Height,
                QdaoUguiTheme.RequireSprite("UI/Ugui/WorldHud20261022/chrome/frame"), true);
            surface.type = Image.Type.Sliced;
            _surface = surface.gameObject;
            _quests = Tab("任务", 12, WorldSideHudPage.Quests);
            _team = Tab("组队", 186, WorldSideHudPage.Team);
            _collapse = GameplayUiArt.Button(Panel, "收起", 360, 12, 68, 44,
                () => SetCollapsed(!IsCollapsed), fontSize: 20);
            _collapse.name = "SideHudCollapse";
            _collapseText = _collapse.GetComponentInChildren<TMP_Text>();
            // 窄按钮单独缩小内边距，保证“收起／展开”不会被省略。
            _collapseText.rectTransform.anchoredPosition = Vector2.zero;
            _collapseText.rectTransform.sizeDelta = new Vector2(68, 44);
            QuestMount = QdaoUguiFactory.CreateRect("QuestMount", Panel, 12, 66, ContentWidth, ContentHeight);
            TeamMount = QdaoUguiFactory.CreateRect("TeamMount", Panel, 12, 66, ContentWidth, ContentHeight);
            QuestMount.gameObject.AddComponent<RectMask2D>();
            TeamMount.gameObject.AddComponent<RectMask2D>();
            _teamPanel = new TeamHudPanel(TeamMount);
            Instance = this;
            ApplyVisibility();
        }

        private Button Tab(string title, float x, WorldSideHudPage page)
        {
            var button = GameplayUiArt.Button(Panel, title, x, 12, 166, 44,
                () => SelectPage(page), fontSize: 26);
            button.name = "SideHudTab_" + page;
            return button;
        }

        private void LateUpdate()
        {
            if (Panel == null) return;
            var game = AppBootstrap.Instance?.GameClient;
            bool inGame = game != null && game.InGame && game.IsGateReady;
            ulong playerId = inGame ? game.PlayerId : 0;
            if (!ReferenceEquals(_gameIdentity, game) || _playerId != playerId)
            {
                _gameIdentity = game;
                _playerId = playerId;
                SelectedPage = WorldSideHudPage.Quests;
                IsCollapsed = false;
                ApplyVisibility();
            }
            _teamPanel.Bind(inGame ? TeamUiRoot.Instance : null);
            _teamPanel.Tick();
            RefreshPresentation(inGame && !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false),
                WorldHudNavigation.Instance?.CurrentCategory.HasValue ?? false);
            FitSafeArea();
        }

        /// <summary>唯一世界可用性接缝，也用于无网络的原生布局验证。</summary>
        public void RefreshPresentation(bool worldAvailable, bool categoryOpen)
        {
            bool next = worldAvailable && !categoryOpen;
            if (_available == next) return;
            _available = next;
            ApplyVisibility();
        }

        public bool IsPageVisible(WorldSideHudPage page) => _available && !IsCollapsed && SelectedPage == page;

        public void SelectPage(WorldSideHudPage page)
        {
            if (page != WorldSideHudPage.Quests && page != WorldSideHudPage.Team) return;
            if (SelectedPage == page && !IsCollapsed) return;
            SelectedPage = page;
            IsCollapsed = false;
            ApplyVisibility();
        }

        public void SetCollapsed(bool collapsed)
        {
            if (IsCollapsed == collapsed) return;
            IsCollapsed = collapsed;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            if (Panel == null) return;
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(Panel))
                EventSystem.current.SetSelectedGameObject(null);
            Panel.gameObject.SetActive(_available);
            Panel.sizeDelta = new Vector2(Width, IsCollapsed ? 66 : Height);
            _surface.SetActive(!IsCollapsed);
            _quests.gameObject.SetActive(!IsCollapsed);
            _team.gameObject.SetActive(!IsCollapsed);
            _collapseText.text = IsCollapsed ? "展开" : "收起";
            QuestMount.gameObject.SetActive(IsPageVisible(WorldSideHudPage.Quests));
            TeamMount.gameObject.SetActive(IsPageVisible(WorldSideHudPage.Team));
            StyleTab(_quests, SelectedPage == WorldSideHudPage.Quests);
            StyleTab(_team, SelectedPage == WorldSideHudPage.Team);
            VisibilityChanged?.Invoke();
        }

        private static void StyleTab(Button button, bool selected)
        {
            var image = (Image)button.targetGraphic;
            image.sprite = GameplayUiArt.Load(selected ? "tab_selected" : "tab_normal");
            image.type = image.sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            button.GetComponentInChildren<TMP_Text>().color = selected ? GameplayUiArt.Cream : GameplayUiArt.Ink;
        }

        private void FitSafeArea()
        {
            if (!_available || Screen.width <= 0 || Screen.height <= 0) return;
            Panel.anchoredPosition = new Vector2(1704, -188);
            var corners = new UnityEngine.Vector3[4];
            Panel.GetWorldCorners(corners);
            var safe = Screen.safeArea;
            var min = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            var max = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
            var delta = new Vector2(Mathf.Max(0, safe.xMin - min.x) + Mathf.Min(0, safe.xMax - max.x),
                Mathf.Max(0, safe.yMin - min.y) + Mathf.Min(0, safe.yMax - max.y));
            float scale = Panel.GetComponentInParent<Canvas>().scaleFactor;
            if (scale > 0) Panel.anchoredPosition += delta / scale;
        }

        private void OnDisable()
        {
            if (Panel == null) return;
            _available = false;
            _teamPanel?.Bind(null);
            ApplyVisibility();
        }

        private void OnDestroy()
        {
            _teamPanel?.Dispose();
            VisibilityChanged = null;
            if (Instance == this) Instance = null;
        }
    }
}
