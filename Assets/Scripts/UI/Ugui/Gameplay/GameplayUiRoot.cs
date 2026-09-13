using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Pet;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>City entry points and lifecycle binding for player feature windows.</summary>
    public sealed class GameplayUiRoot : MonoBehaviour
    {
        public static GameplayUiRoot Instance { get; private set; }
        private PlayerFeaturesClient _client;
        private GameplayWindow _window;
        private RectTransform _hud, _trackedRoot;
        private TMP_Text _trackedText;
        private ulong _playerId;
        private ulong _trackedMission;
        private bool _available;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[PlayerFeaturesUi]");
            DontDestroyOnLoad(go); go.AddComponent<GameplayUiRoot>();
        }
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            var canvasObject = new GameObject("PlayerFeaturesCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 180;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform, 2560, 1080);
            _hud = QdaoUguiFactory.CreateStretch("HudRoot", design, Vector4.zero);
            QdaoUguiFactory.ConfigureHudCanvas(_hud);
            string[] names = { "背包", "任务", "活动" };
            string[] icons = { "icon_bag", "icon_scroll", "icon_lantern" };
            for (int i = 0; i < 3; ++i)
            {
                var page = (GameplayPage)i;
                var b = GameplayUiArt.Button(_hud, names[i], BattleUiStyle.HudEntryX, BattleUiStyle.HudEntryY(i + 4),
                    BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, () => Toggle(page), true, fontSize: 32);
                GameplayUiArt.Art(b.transform, icons[i], 34, 12, 54, 54, true);
                var entryLabel = b.GetComponentInChildren<TMP_Text>().rectTransform;
                entryLabel.anchoredPosition = new Vector2(94, 0);
                entryLabel.sizeDelta = new Vector2(210, 80);
            }
            _trackedRoot = QdaoUguiFactory.CreateRect("TrackedMission", _hud, 68, 288, 405, 180);
            GameplayUiArt.Art(_trackedRoot, "content_panel", 0, 0, 405, 180);
            _trackedText = GameplayUiArt.Text(_trackedRoot, "", 26, 20, 355, 140, 27, wrap: true);
            var hit = _trackedRoot.gameObject.AddComponent<Button>();
            hit.targetGraphic = _trackedRoot.GetComponentInChildren<Image>();
            hit.targetGraphic.raycastTarget = true;
            hit.onClick.AddListener(() => Open(GameplayPage.Missions));
            _trackedRoot.gameObject.SetActive(false);
            _window = new GameplayWindow(design);
            _window.BagRequested += type => _client?.RequestBag(type);
            _window.SortRequested += () => _client?.SortBag();
            _window.MissionsRequested += () => _client?.RequestMissions();
            _window.ActivitiesRequested += () => _client?.RequestActivities();
            _window.MissionAcceptRequested += (scope, id) => _client?.AcceptMission(scope, id);
            _window.MissionClaimRequested += (scope, id) => _client?.ClaimMissionReward(scope, id);
            _window.TrackingChanged += Track;
            _hud.gameObject.SetActive(false);
        }
        private void Update()
        {
            var game = AppBootstrap.Instance?.GameClient;
            if (_client != game?.Features)
            {
                if (_client != null) _client.OnChanged -= Changed;
                _window.ResetSession();
                _client = game?.Features;
                if (_client != null) _client.OnChanged += Changed;
            }
            bool inGame = game != null && game.InGame && game.IsGateReady;
            if (_playerId != (inGame ? game.PlayerId : 0))
            { _window.ResetSession(); _playerId = inGame ? game.PlayerId : 0; }
            _available = inGame && _client != null && !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false);
            _hud.gameObject.SetActive(_available);
            if (!_available) { HidePanel(); return; }
            bool typing = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.GetComponentInParent<TMP_InputField>()?.isFocused == true;
            if (typing) return;
#if ENABLE_INPUT_SYSTEM
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.escapeKey.wasPressedThisFrame) HidePanel();
            else if (keys.bKey.wasPressedThisFrame) Toggle(GameplayPage.Bag);
            else if (keys.jKey.wasPressedThisFrame) Toggle(GameplayPage.Missions);
            else if (keys.oKey.wasPressedThisFrame) Toggle(GameplayPage.Activities);
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) HidePanel();
            else if (Input.GetKeyDown(KeyCode.B)) Toggle(GameplayPage.Bag);
            else if (Input.GetKeyDown(KeyCode.J)) Toggle(GameplayPage.Missions);
            else if (Input.GetKeyDown(KeyCode.O)) Toggle(GameplayPage.Activities);
#endif
        }
        public void HidePanel() => _window?.Hide();
        private void Toggle(GameplayPage page)
        { if (_window.IsVisible && _window.Page == page) HidePanel(); else Open(page); }
        private void Open(GameplayPage page)
        {
            if (!_available) return;
            Team.TeamUiRoot.Instance?.HidePanel();
            AttributeUiRoot.Instance?.HidePanel(); PetUiRoot.Instance?.HidePanel();
            Changed(); _window.Show(page);
        }
        private void Changed()
        {
            if (_client == null) return;
            _window.SetBag(_client.Bag, _client.BagLoading, _client.BagError, _client.BusySort);
            _window.SetMissions(_client.Missions, _client.MissionsLoading, _client.MissionsError, _client.BusyMissionAction);
            _window.SetActivities(_client.Activities, _client.ActivitiesLoading, _client.ActivitiesError);
            if (_trackedMission != 0 && _client.Missions != null)
            {
                PlayerMissionInfo current = null;
                foreach (var m in _client.Missions.Missions) if (GameplayUiArt.MissionKey(m) == _trackedMission) { current = m; break; }
                Track(current);
            }
        }
        private void Track(PlayerMissionInfo mission)
        {
            _trackedMission = mission == null ? 0 : GameplayUiArt.MissionKey(mission);
            _trackedRoot.gameObject.SetActive(mission != null);
            if (mission == null) return;
            string text = GameplayUiArt.MissionName(mission) + "\n";
            if (mission.Objectives.Count > 0)
            {
                var goal = mission.Objectives[0];
                text += (string.IsNullOrWhiteSpace(goal.Description) ? "当前目标" : goal.Description) + $"\n{goal.Progress} / {goal.Target}";
            }
            else text += GameplayUiArt.MissionStatus(mission.Status);
            _trackedText.text = text;
        }
        private void OnDestroy()
        {
            if (_client != null) _client.OnChanged -= Changed;
            if (Instance == this) Instance = null;
        }
    }
}
