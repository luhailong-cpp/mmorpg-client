using System;
using MmorpgClient.Game;
using MmorpgClient.Game.Team;
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

namespace MmorpgClient.UI.Ugui.Team
{
    /// <summary>City team entry and session lifetime. A future team transport supplies snapshots.</summary>
    public sealed class TeamUiRoot : MonoBehaviour
    {
        public static TeamUiRoot Instance { get; private set; }
        public TeamWindow Window => _window;
        public TeamUiState State { get; } = new(() => Time.realtimeSinceStartup);
        // The adapter completes/fails State using this generation, never an optimistic UI edit.
        public event Action<int> RefreshRequested;
        public event Action<int, ulong, bool> DecisionRequested;
        public const float EntryX = 68;
        public const float EntryY = 496;

        private GameClient _game;
        private RectTransform _hud;
        private Button _entry;
        private TMP_Text _entryLabel;
        private TeamWindow _window;
        private ulong _playerId;
        private bool _available;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[TeamUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<TeamUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            var canvasObject = new GameObject("TeamCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 186;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform, 2560, 1080);
            _hud = QdaoUguiFactory.CreateStretch("HudRoot", design, Vector4.zero);
            QdaoUguiFactory.ConfigureHudCanvas(_hud);
            // The existing right column already fills the screen; use the free space below tracked quests.
            _entry = GameplayUiArt.Button(_hud, "组队 [T]", EntryX, EntryY,
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, Toggle, true, fontSize: 32);
            _entry.name = "TeamEntry";
            _entryLabel = _entry.GetComponentInChildren<TMP_Text>();
            _window = new TeamWindow(design);
            _window.RefreshRequested += RequestRefresh;
            _window.DecisionRequested += RequestDecision;
            State.Changed += Changed;
            State.SetUnavailable("组队暂未开放，敬请期待。");
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
            ulong playerId = inGame ? _game.PlayerId : 0;
            if (_playerId != playerId)
            {
                ResetSession();
                _playerId = playerId;
                State.Reset(playerId);
                State.SetUnavailable("组队暂未开放，敬请期待。");
            }
            _available = inGame && !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false);
            _hud.gameObject.SetActive(_available);
            State.Tick(Time.realtimeSinceStartup);
            if (!_available) { HidePanel(); return; }
            if (IsTyping() || (!_window.IsVisible && GameplayInputGate.IsKeyboardBlocked)) return;
#if ENABLE_INPUT_SYSTEM
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.escapeKey.wasPressedThisFrame) HidePanel();
            else if (keys.tKey.wasPressedThisFrame) Toggle();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) HidePanel();
            else if (Input.GetKeyDown(KeyCode.T)) Toggle();
#endif
        }

        public void Toggle()
        {
            if (_window.IsVisible) { HidePanel(); return; }
            if (!_available) return;
            Guild.GuildUiRoot.Instance?.HidePanel();
            GameplayUiRoot.Instance?.HidePanel();
            CityTravelUiRoot.Instance?.HidePanel();
            AttributeUiRoot.Instance?.HidePanel();
            PetUiRoot.Instance?.HidePanel();
            Changed();
            _window.Show();
            if (State.ServiceAvailable && !State.IsBusy) RequestRefresh();
        }

        public void HidePanel() => _window?.Hide();

        private void RequestRefresh()
        {
            if (!_available) return;
            if (RefreshRequested == null) { State.SetUnavailable("组队暂未开放，敬请期待。"); return; }
            int generation = State.BeginRefresh();
            if (generation != 0) RefreshRequested(generation);
        }

        private void RequestDecision(ulong playerId, bool approve)
        {
            if (!_available) return;
            if (DecisionRequested == null) { State.SetUnavailable("组队暂未开放，敬请期待。"); return; }
            int generation = State.BeginDecision(playerId, approve);
            if (generation != 0) DecisionRequested(generation, playerId, approve);
        }

        private void Changed()
        {
            _window?.SetState(State);
            if (_entryLabel != null)
                _entryLabel.text = State.Snapshot.Applications.Count > 0 && State.IsLeader
                    ? $"组队 · {State.Snapshot.Applications.Count}条申请" : "组队 [T]";
        }

        private void ResetSession()
        {
            _playerId = 0;
            _window?.ResetSession();
            State.Reset();
            State.SetUnavailable("组队暂未开放，敬请期待。");
        }

        private static bool IsTyping()
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            return selected != null && (selected.GetComponentInParent<TMP_InputField>()?.isFocused == true ||
                                        selected.GetComponentInParent<InputField>()?.isFocused == true);
        }

        private void OnDisable() => HidePanel();

        private void OnDestroy()
        {
            State.Changed -= Changed;
            if (_game != null) _game.OnDisconnected -= ResetSession;
            HidePanel();
            if (Instance == this) Instance = null;
        }
    }
}
