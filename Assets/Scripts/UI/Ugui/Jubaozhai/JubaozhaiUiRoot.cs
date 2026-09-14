using MmorpgClient.Game;
using MmorpgClient.Game.Jubaozhai;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Pet;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.Jubaozhai
{
    /// <summary>Real-session city entry. Only a future service adapter can populate the production catalog.</summary>
    public sealed class JubaozhaiUiRoot : MonoBehaviour
    {
        public static JubaozhaiUiRoot Instance { get; private set; }
        public JubaozhaiWindow Window => _window;
        public JubaozhaiState State => _window?.State;
        public const float EntryX = 68, EntryY = 704;
        private GameClient _game;
        private JubaozhaiWindow _window;
        private RectTransform _hud;
        private ulong _player;
        private bool _available;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var root = new GameObject("[JubaozhaiUi]");
            DontDestroyOnLoad(root);
            root.AddComponent<JubaozhaiUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            var canvasObject = new GameObject("JubaozhaiCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 188;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform, 2560, 1080);
            _hud = QdaoUguiFactory.CreateStretch("HudRoot", design, Vector4.zero);
            QdaoUguiFactory.ConfigureHudCanvas(_hud);
            var entry = GameplayUiArt.Button(_hud, "聚宝斋 [U]", EntryX, EntryY,
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, Toggle, true, fontSize: 32);
            entry.name = "JubaozhaiEntry";
            _window = new JubaozhaiWindow(design);
            _hud.gameObject.SetActive(false);
        }

        private void Update()
        {
            var game = AppBootstrap.Instance?.GameClient;
            if (_game != game)
            {
                if (_game != null) _game.OnDisconnected -= ResetSession;
                ResetSession();
                _game = game;
                if (_game != null) _game.OnDisconnected += ResetSession;
            }
            bool inGame = _game != null && _game.InGame && _game.IsGateReady;
            ulong player = inGame ? _game.PlayerId : 0;
            if (_player != player) { ResetSession(); _player = player; }
            bool available = inGame && !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false);
            if (_available && !available) _window.ResetSession();
            _available = available;
            _hud.gameObject.SetActive(available);
            if (!available) { HidePanel(); return; }
            _window.Tick(Time.unscaledTime);
            if (IsTyping() || (!_window.IsVisible && GameplayInputGate.IsKeyboardBlocked)) return;
#if ENABLE_INPUT_SYSTEM
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.escapeKey.wasPressedThisFrame) _window.Back();
            else if (keys.uKey.wasPressedThisFrame) Toggle();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) _window.Back();
            else if (Input.GetKeyDown(KeyCode.U)) Toggle();
#endif
        }

        public void Toggle()
        {
            if (_window.IsVisible) { HidePanel(); return; }
            if (!_available) return;
            Team.TeamUiRoot.Instance?.HidePanel();
            Guild.GuildUiRoot.Instance?.HidePanel();
            GameplayUiRoot.Instance?.HidePanel();
            CityTravelUiRoot.Instance?.HidePanel();
            AttributeUiRoot.Instance?.HidePanel();
            PetUiRoot.Instance?.HidePanel();
            _window.Show();
        }

        public void HidePanel() => _window?.Hide();
        private void ResetSession()
        {
            _player = 0;
            _available = false;
            _window?.ResetSession();
            if (_hud != null) _hud.gameObject.SetActive(false);
        }
        public static bool IsTyping()
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            return selected?.GetComponentInParent<TMP_InputField>()?.isFocused == true ||
                   selected?.GetComponentInParent<InputField>()?.isFocused == true;
        }
        private void OnDestroy()
        {
            if (_game != null) _game.OnDisconnected -= ResetSession;
            _window?.Dispose();
            if (Instance == this) Instance = null;
        }
    }
}


