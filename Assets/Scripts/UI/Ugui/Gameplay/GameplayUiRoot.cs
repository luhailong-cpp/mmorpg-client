using MmorpgClient.Game.Attribute;
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
        /// <summary>
        /// 强化 / 镶嵌面板的按钮逻辑。这里只负责它的生命周期:跟着 GameClient.Features 换绑、每帧 Tick、窗口关闭时关上;
        /// 面板本身(打开入口、画面、把按钮接到它的意图方法)见 Docs/EquipmentForgeUI-handoff-20261015.md。
        /// </summary>
        public EquipForgeUiState Forge => _forge;
        private readonly EquipForgeUiState _forge = new EquipForgeUiState();
        // 背包页状态行上此刻挂着的是强化结果(兜底提示)。
        private bool _forgeNoticeShown;
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
            _window = new GameplayWindow(design, _forge);
            _window.BagRequested += type => _client?.RequestBag(type);
            _window.SortRequested += () => _client?.SortBag();
            _window.EquipmentRequested += () => _client?.RequestEquipment();
            _window.EquipRequested += id => _client?.EquipItem(id);
            _window.UnequipRequested += id => _client?.UnequipItem(id);
            _window.AttributesRequested += () => AttributeUiRoot.Instance?.OpenPanel();
            _window.EquipmentShareRequested += item =>
            {
                if (!(Social.SocialUiRoot.Instance?.TryComposeEquipment(item) ?? false))
                {
                    _forgeNoticeShown = false; // 状态行换成了分享的提示,不再是强化结果
                    _window.SetEquipmentNotice(Social.SocialUiRoot.Instance?.State?.Status ?? "仙友会尚未就绪，请稍后重试。");
                }
            };
            _window.MissionsRequested += () => _client?.RequestMissions();
            _window.ActivitiesRequested += () => _client?.RequestActivities();
            _window.MissionAcceptRequested += (scope, id) => _client?.AcceptMission(scope, id);
            _window.MissionClaimRequested += (scope, id) => _client?.ClaimMissionReward(scope, id);
            _window.TrackingChanged += Track;
            // 面板长在背包窗口里:窗口关掉(含换角色 / 断线时的 ResetSession)它跟着关。
            _window.Closed += _forge.Close;
            // 强化结果的兜底提示(面板关了也照样来)。背包页正在屏幕上:写进它的状态行,下一次装备操作开始时由 Changed 收起;
            // 窗口关着或在别的页:状态行会在重开时被 Show 清掉,改用飘字。掷骰没过不是错误,不用错误色。
            _forge.Announced += notice =>
            {
                if (_window.IsVisible && _window.Page == GameplayPage.Bag)
                {
                    _forgeNoticeShown = true;
                    _window.SetEquipmentNotice(notice.Text);
                }
                else BattleUiRoot.Instance?.ShowToast(notice.Text);
            };
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
                _forge.Bind(_client);
            }
            bool inGame = game != null && game.InGame && game.IsGateReady;
            if (_playerId != (inGame ? game.PlayerId : 0))
            { _window.ResetSession(); _playerId = inGame ? game.PlayerId : 0; }
            // 装备 tooltip 的等级要求标红用:属性面板拉到过才有等级,没有就是 0(不标红)。值没变时窗口不重画。
            _window.SetCharacterLevel(AttributeClient.Instance?.Panel?.Level ?? 0);
            _available = inGame && _client != null && !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false);
            _hud.gameObject.SetActive(_available);
            if (!_available) { HidePanel(); return; }
            // 发包间隔按时间走完,数据层不为它发通知:面板开着时每帧看一眼,冷却结束才重画。
            _forge.Tick();
            // 子弹窗独占键盘；Esc 由弹窗自己的输入组件处理，避免同一按键把背包一起关掉。
            if (_window.EquipmentModalOwnsKeyboard) return;
            bool typing = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.GetComponentInParent<TMP_InputField>()?.isFocused == true;
            if (typing) return;
            if (!_window.IsVisible && MmorpgClient.World.Tianyong.GameplayInputGate.IsKeyboardBlocked) return;
#if ENABLE_INPUT_SYSTEM
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.escapeKey.wasPressedThisFrame) _window.Back();
            else if (keys.bKey.wasPressedThisFrame) Toggle(GameplayPage.Bag);
            else if (keys.jKey.wasPressedThisFrame) Toggle(GameplayPage.Missions);
            else if (keys.oKey.wasPressedThisFrame) Toggle(GameplayPage.Activities);
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) _window.Back();
            else if (Input.GetKeyDown(KeyCode.B)) Toggle(GameplayPage.Bag);
            else if (Input.GetKeyDown(KeyCode.J)) Toggle(GameplayPage.Missions);
            else if (Input.GetKeyDown(KeyCode.O)) Toggle(GameplayPage.Activities);
#endif
        }
        public void HidePanel() => _window?.Hide();
        public void OpenEquipment()
        {
            if (!_available) return;
            PrepareOpen(); Changed(); _window.ShowEquipment();
            AttributeClient.Instance?.RequestPanel();
        }
        private void Toggle(GameplayPage page)
        { if (_window.IsVisible && _window.Page == page) HidePanel(); else Open(page); }
        private void Open(GameplayPage page)
        {
            if (!_available) return;
            PrepareOpen();
            Changed(); _window.Show(page);
            if (page == GameplayPage.Bag) AttributeClient.Instance?.RequestPanel();
        }
        private static void PrepareOpen()
        {
            MysteryMerchantUiRoot.Instance?.HidePanel();
            Skills.SkillUiRoot.Instance?.HidePanel();
            Social.SocialUiRoot.Instance?.HidePanel();
            Mail.MailUiRoot.Instance?.HidePanel();
            Jubaozhai.JubaozhaiUiRoot.Instance?.HidePanel();
            CityTravelUiRoot.Instance?.HidePanel();
            Guild.GuildUiRoot.Instance?.HidePanel();
            Team.TeamUiRoot.Instance?.HidePanel();
            AttributeUiRoot.Instance?.HidePanel(); PetUiRoot.Instance?.HidePanel();
        }
        private void Changed()
        {
            if (_client == null) return;
            // 状态行上的强化结果与数据层的 LastEnhanceOutcome 同生同灭:下一次装备操作一开始(或断线 / 换角色)就收起,
            // 免得一次失败的镶嵌之后状态行上还挂着上一次的「强化成功」。
            if (_forgeNoticeShown && !_client.LastEnhanceOutcome.HasValue)
            {
                _forgeNoticeShown = false;
                _window.SetEquipmentNotice(null);
            }
            _window.SetBagPage(_client.Bag, _client.BagLoading, _client.BagError, _client.BusySort,
                _client.Equipment, _client.EquipmentLoading, _client.EquipmentError, _client.BusyEquip);
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
            _window?.Dispose();
            _forge.Dispose();
            if (Instance == this) Instance = null;
        }
        private void OnDisable() => HidePanel();
    }
}
