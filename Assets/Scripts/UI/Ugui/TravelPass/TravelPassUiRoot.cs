using MmorpgClient.Game;
using MmorpgClient.Game.TravelPass;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Jubaozhai;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.TravelPass
{
    /// <summary>
    /// 青云游历手记城内独立 UI 宿主（不额外侵占主城 HUD 按钮，统一由既有「活动」UI 卡片进入）。
    /// </summary>
    public sealed class TravelPassUiRoot : MonoBehaviour
    {
        public static TravelPassUiRoot Instance { get; private set; }

        public const int CanvasSortingOrder = 186;

        private GameClient _game;
        private TravelPassClient _client;
        private TravelPassWindow _window;
        private bool _available;

        public TravelPassWindow Window => _window;
        public TravelPassClient Client => _client;
        public bool IsPanelVisible => _window != null && _window.IsVisible;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn() => EnsureSpawned();

        public static void EnsureSpawned()
        {
            if (Instance != null) return;
            var go = new GameObject("[TravelPassUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<TravelPassUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            CityPanelRegistry.Register(this, HidePanel);

            var canvasObject = new GameObject("TravelPassCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform,
                QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            _window = new TravelPassWindow(design, OpenRechargeMall);
        }

        private void Update()
        {
            if (_window == null) { Unbind(); return; }
            Bind(AppBootstrap.Instance?.GameClient);
            bool battleLayer = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            _available = _game != null && _game.InGame && _game.IsGateReady && !battleLayer && (_client?.IsSupported ?? false);
            if (!_available && _window.IsVisible)
            {
                _window.Hide();
                return;
            }
            if (!_window.IsVisible || CityPanelRegistry.IsTyping()) return;
            bool esc = false;
#if ENABLE_INPUT_SYSTEM
            esc = Keyboard.current?.escapeKey.wasPressedThisFrame == true;
#elif ENABLE_LEGACY_INPUT_MANAGER
            esc = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (esc) HidePanel();
        }

        private void OnDestroy()
        {
            CityPanelRegistry.Unregister(this);
            Unbind();
            _window?.Dispose();
            _window = null;
            if (Instance == this) Instance = null;
        }

        public void OpenWindow()
        {
            Bind(AppBootstrap.Instance?.GameClient);
            bool battleLayer = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            _available = _game != null && _game.InGame && _game.IsGateReady && !battleLayer && (_client?.IsSupported ?? false);
            if (!_available || _window == null) return;
            CityPanelRegistry.HideOthers(this);
            _window.Show();
        }

        public void HidePanel() => _window?.Hide();

        private void OpenRechargeMall() => JubaozhaiUiRoot.Instance?.OpenOfficialMall();

        private void Bind(GameClient game)
        {
            if (_game == game) return;
            _game = game;
            _client = game?.TravelPass;
            _window?.Bind(_client);
        }

        private void Unbind()
        {
            _game = null;
            _client = null;
            _window?.Bind(null);
        }
    }
}
