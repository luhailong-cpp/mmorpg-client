using System.Collections.Generic;
using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.WorldTravel;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Pet;
using MmorpgClient.World.Tianyong;
using MmorpgClient.World.CurrentMaps;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>正常游戏地图入口；服务端确认地点，本地选择该地点的节庆景色。</summary>
    public sealed class CityTravelUiRoot : MonoBehaviour
    {
        public static CityTravelUiRoot Instance { get; private set; }
        public CityTravelWindow Window => _window;
        /// <summary>最近一次缓存的世界地图总览快照（无数据时为 null）。</summary>
        public WorldMapOverviewSnapshot CurrentOverview => _channelClient?.Overview;
        /// <summary>当前总览快照已历经的本地单调秒数（供灵潮倒计时估算，免额外发包）。</summary>
        public float OverviewAgeSeconds => _channelClient?.OverviewAgeSeconds ?? 0f;
        /// <summary>主界面「地图」入口是否应显示灵潮提示角标（每轮打开地图后自动消标，下轮自动重亮）。</summary>
        public bool ShouldShowTideHudBadge =>
            WorldMapOverviewModels.ShouldShowMapEntryTideBadge(_channelClient?.Overview, CurrentScene, _dismissedTideRoundId);
        private readonly CityTravelRequest _request = new();
        private AppBootstrap _app;
        private GameClient _game;
        private SceneChannelClient _channelClient;
        private RectTransform _hud;
        private Button _entry;
        private GameObject _tideHudBadge;
        private GameObject _wildHabitatBar;
        private Image _wildHabitatBgImage;
        private Image _wildHabitatIconImage;
        private Image _wildHabitatBtnImage;
        private TMP_Text _wildHabitatTitle;
        private TMP_Text _wildHabitatPets;
        private TMP_Text _wildHabitatBtnLabel;
        private Button _wildHabitatButton;
        private bool? _wildHabitatIsTideVisual;
        private string _wildHabitatNotice = "";
        private double _wildHabitatNoticeUntilSeconds;
        private CityTravelWindow _window;
        private string _status = "";
        private bool _wasAvailable;
        private bool _overviewBootstrapped;
        private ulong _dismissedTideRoundId;
        private double _nextTideBannerRefreshSeconds;
        private double _nextHudOverviewRefreshSeconds;
        // 跨区传送：在途的目标区服，以及发起时的连接标识（底层不再等待时据此判断连接是否还是原来那条）。
        // “此刻人在哪个区”不在这里记：那是连接层的事实，读 GameClient.CurrentZoneId（见下面的 CurrentZoneId）。
        // 以前这里自记过一份，只在“请求在途且入场通知来自新连接”时更新；窗口先收场、换服通知后到的时序下
        // 会记脏，可去列表随之滤掉真正的归属区，玩家在界面上回不了家。
        private uint _pendingZoneId;
        private object _gateAtRequest;
        private string _pendingAppearance = "normal";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[CityTravelUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<CityTravelUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            CityPanelRegistry.Register(this, HidePanel);
            var canvasObject = new GameObject("CityTravelCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 185;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform, 2560, 1080);
            _hud = QdaoUguiFactory.CreateStretch("HudRoot", design, Vector4.zero);
            QdaoUguiFactory.ConfigureHudCanvas(_hud);
            _entry = GameplayUiArt.Button(_hud, "地图", BattleUiStyle.HudEntryX, BattleUiStyle.HudEntryY(7),
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, Toggle, true, fontSize: 32);
            _entry.name = "CityTravelEntry";
            WorldHudNavigation.Register(WorldHudEntry.Map, (RectTransform)_entry.transform, _entry, _entry.GetComponentInChildren<TMP_Text>());
            // 导航注册会先重排地图按钮并替换旧图标，角标挂在重排后的按钮右上角。
            var tideBadge = QdaoUguiFactory.CreateImage("WorldMapEntryTideBadge", _entry.transform, 0, 0, 26, 26,
                QdaoUguiTheme.RequireSprite("UI/Ugui/WorldMapV2/hud_tide_dot"));
            tideBadge.rectTransform.anchorMin = tideBadge.rectTransform.anchorMax = tideBadge.rectTransform.pivot = Vector2.one;
            tideBadge.rectTransform.anchoredPosition = new Vector2(-6, -4);
            tideBadge.raycastTarget = false;
            tideBadge.preserveAspect = true;
            _tideHudBadge = tideBadge.gameObject;
            _tideHudBadge.SetActive(false);
            BuildWildHabitatBar();
            _window = new CityTravelWindow(design);
            _window.SetDestinations(CreateDestinations());
            _window.AppearanceTravelRequested += RequestTravel;
            _window.AppearanceZoneTravelRequested += RequestZoneTravel;
            _window.TideQuickTravelRequested += HandleTideQuickTravelRequested;
            _hud.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_window == null || _window.Root == null || _hud == null || _entry == null)
            { Bind(null); return; }
            _app = AppBootstrap.Instance;
            Bind(_app?.GameClient);
            BindChannelClient(SceneChannelUiRoot.Instance?.Client);
            bool inGame = _game != null && _game.InGame && _game.IsGateReady;
            bool available = inGame && CanTravelNow();
            _hud.gameObject.SetActive(inGame && !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false));
            _entry.interactable = available && !_request.IsPending;
            RefreshTideHudBadge();
            RefreshWildHabitatBar(available);
            double nowSeconds = Time.realtimeSinceStartupAsDouble;
            if (inGame && _channelClient != null)
            {
                // 入场首次拿到分线目录后自动拉一次总览，并在后台每 60 秒轻刷新一次，让 HUD「地图」灵潮角标在新一轮灵潮开启时无需手动开图也能亮起。
                if (!_overviewBootstrapped && _channelClient.HasDirectory)
                {
                    _overviewBootstrapped = true;
                    _nextHudOverviewRefreshSeconds = nowSeconds + 60d;
                    _channelClient.RequestOverview(force: true);
                }
                else if (_overviewBootstrapped && !_window.IsVisible && nowSeconds >= _nextHudOverviewRefreshSeconds)
                {
                    _nextHudOverviewRefreshSeconds = nowSeconds + 60d;
                    _channelClient.RequestOverview();
                }
            }
            if (_window.IsVisible && nowSeconds >= _nextTideBannerRefreshSeconds)
            {
                _window.RefreshTideBanner(OverviewAgeSeconds);
                _nextTideBannerRefreshSeconds = nowSeconds + 1d;
            }
            if (_request.Tick(Time.realtimeSinceStartup))
            {
                _pendingZoneId = 0;
                _gateAtRequest = null;
                _status = "暂未收到抵达消息，请稍后重试。";
                Refresh();
            }
            // 跨区请求已被受理，但底层已经不再等待（在它的等待预算内既没等到换服通知，也没等到失败提示），
            // 连接也还是原来那条：这趟行程不会再有下文。底层只把“传送超时”写进登录流程的状态栏，
            // 游戏内没有任何界面显示它；不在这里收场，窗口会顶着“正在传送”一直空等到跨区的等待上限。
            // 不会误伤正常流程：失败提示到达时请求已在同一调用栈里被重置；换服通知到达后“正在换连接”
            // 立刻为真，且入场通知先于换连接结束到达，那时在途的区服编号已经清零。
            if (_pendingZoneId != 0 && _request.IsPending && _request.IsAccepted && _game != null &&
                !_game.IsTravelPending && !_game.IsRedirecting &&
                ReferenceEquals(_game.GateConnectionIdentity, _gateAtRequest))
            {
                _request.Reset();
                _pendingZoneId = 0;
                _gateAtRequest = null;
                _status = "传送超时，请稍后重试。";
                Refresh();
            }
            if (!available)
            {
                // 换区途中客户端会先清掉“已进入游戏”的状态再去连新服务器，此时 available 必然为假；
                // 照旧收起窗口，就等于把“正在传送”的遮罩连同输入拦截一起撤掉，玩家会看到空场景还能乱点。
                // 跨区在途或正在换连接时保持原样（也不动 _wasAvailable，收场后仍不可用时下一帧照常收起），
                // 收场交给入场通知、服务端提示、断线和超时。
                // 判据只看底层的这两个状态，不看“有请求在途”：同区换图不换连接，它在途时变得不可用
                // 只可能是战斗或观战开始了，那时必须照旧收起，否则遮罩会盖在战斗界面上直到请求超时（受理后可长达一分多钟）。
                // 跨区的整段路程（发出请求、等换服通知、换连接、重新进场）都被这两个状态盖住，中间没有空窗。
                bool travelling = _game != null && (_game.IsRedirecting || _game.IsTravelPending);
                if (travelling) return;
                if (_wasAvailable) HidePanel();
                _wasAvailable = false;
                return;
            }
            _wasAvailable = true;
            // Our own open window owns a blocker but must still accept Escape/M.
            // A different modal keeps the map shortcut from opening through it.
            if (IsTyping() || (!_window.IsVisible && GameplayInputGate.IsKeyboardBlocked)) return;
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame) HidePanel();
            else if (keyboard.mKey.wasPressedThisFrame) Toggle();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) HidePanel();
            else if (Input.GetKeyDown(KeyCode.M)) Toggle();
#endif
        }

        public void HidePanel() => _window?.Hide();

        public void Toggle()
        {
            if (_window == null || _window.Root == null) return;
            if (_window.IsVisible) { HidePanel(); return; }
            if (_game == null || !_game.InGame || !_game.IsGateReady || !CanTravelNow()) return;
            CityPanelRegistry.HideOthers(this);
            BindChannelClient(SceneChannelUiRoot.Instance?.Client);
            MarkCurrentTideSeen();
            _channelClient?.RequestOverview();
            _window.SetZones(BuildZones());
            _window.SetDestinations(CreateDestinations());
            _window.SetOverview(CurrentOverview, OverviewAgeSeconds);
            _window.Show(CurrentScene, _app.WorldMap?.Appearance ?? "normal");
            _nextTideBannerRefreshSeconds = Time.realtimeSinceStartupAsDouble + 1d;
            Refresh();
        }

        /// <summary>主动向服务端按需刷新一次世界地图总览（复用 SceneChannelClient 最小间隔节流）。</summary>
        public bool RequestRefreshOverview()
        {
            BindChannelClient(SceneChannelUiRoot.Instance?.Client);
            return _channelClient != null && _channelClient.RequestOverview();
        }

        /// <summary>将当前这一轮灵潮标记为已查看，消除 HUD 地图入口提示角标直至下一轮。</summary>
        public void MarkCurrentTideSeen()
        {
            var tide = _channelClient?.Overview?.Tide;
            if (tide != null && tide.Active && tide.RoundId != 0UL) _dismissedTideRoundId = tide.RoundId;
            RefreshTideHudBadge();
        }

        private void RefreshTideHudBadge()
        {
            if (_tideHudBadge == null) return;
            bool visible = ShouldShowTideHudBadge;
            if (_tideHudBadge.activeSelf != visible) _tideHudBadge.SetActive(visible);
        }

        private void BuildWildHabitatBar()
        {
            var bar = QdaoUguiFactory.CreateRect("WildPetHabitatBar", _hud, 880f, 984f, 800f, 76f);
            _wildHabitatBgImage = QdaoUguiFactory.CreateImage("WildPetHabitatBg", bar, 0f, 0f, 800f, 76f,
                LoadHabitatSprite("wild_habitat_bar", "world_map_paper_panel"));
            _wildHabitatBgImage.type = Image.Type.Sliced;
            _wildHabitatBgImage.raycastTarget = true;

            _wildHabitatIconImage = QdaoUguiFactory.CreateImage("WildPetHabitatIcon", bar, 18f, 14f, 48f, 48f,
                LoadHabitatSprite("wild_habitat_icon", "seal_tide"));
            _wildHabitatIconImage.preserveAspect = true;
            _wildHabitatIconImage.raycastTarget = false;

            _wildHabitatTitle = GameplayUiArt.Text(bar, "", 78f, 8f, 504f, 30f, 22f, GameplayUiArt.Ink);
            _wildHabitatTitle.name = "WildPetHabitatTitle";
            _wildHabitatTitle.overflowMode = TextOverflowModes.Ellipsis;

            _wildHabitatPets = GameplayUiArt.Text(bar, "", 78f, 39f, 504f, 28f, 19f, GameplayUiArt.Muted);
            _wildHabitatPets.name = "WildPetHabitatPets";
            _wildHabitatPets.overflowMode = TextOverflowModes.Ellipsis;

            var btnRect = QdaoUguiFactory.CreateRect("WildPetHabitatEncounterBtn", bar, 594f, 13f, 190f, 50f);
            _wildHabitatBtnImage = btnRect.gameObject.AddComponent<Image>();
            _wildHabitatBtnImage.sprite = LoadHabitatSprite("wild_habitat_btn", "tide_quick_travel_btn");
            _wildHabitatBtnImage.type = Image.Type.Sliced;
            _wildHabitatButton = btnRect.gameObject.AddComponent<Button>();
            _wildHabitatButton.targetGraphic = _wildHabitatBtnImage;
            _wildHabitatButton.navigation = new Navigation { mode = Navigation.Mode.None };
            _wildHabitatButton.onClick.AddListener(HandleWildPetEncounterRequested);
            _wildHabitatBtnLabel = GameplayUiArt.Text(btnRect, "寻宠遇敌", 31f, 10f, 128f, 30f, 24f, GameplayUiArt.Cream,
                alignment: TextAlignmentOptions.Center);
            _wildHabitatBtnLabel.name = "WildPetHabitatEncounterLabel";
            _wildHabitatBtnLabel.raycastTarget = false;

            _wildHabitatIsTideVisual = false;
            _wildHabitatBar = bar.gameObject;
            _wildHabitatBar.SetActive(false);
        }

        private static Sprite LoadHabitatSprite(string dedicatedName, string fallbackName)
        {
            var dedicated = Resources.Load<Sprite>("UI/Ugui/WorldMapV2/" + dedicatedName);
            return dedicated != null ? dedicated : QdaoUguiTheme.RequireSprite("UI/Ugui/WorldMapV2/" + fallbackName);
        }

        private void RefreshWildHabitatBar(bool available)
        {
            if (_wildHabitatBar == null) return;
            var habitat = WorldMapOverviewModels.FindHabitat(CurrentScene);
            bool visible = available && (_window == null || !_window.IsVisible) && habitat.HasValue;
            if (_wildHabitatBar.activeSelf != visible) _wildHabitatBar.SetActive(visible);
            if (!visible) return;

            var overview = CurrentOverview;
            ulong serverNowMs = WorldMapOverviewModels.EstimateServerNowMs(overview, OverviewAgeSeconds);
            bool tideActive = WorldMapOverviewModels.IsTideActiveOnScene(overview, CurrentScene, serverNowMs);
            if (_wildHabitatIsTideVisual != tideActive)
            {
                _wildHabitatIsTideVisual = tideActive;
                if (_wildHabitatBgImage != null)
                {
                    _wildHabitatBgImage.sprite = tideActive
                        ? LoadHabitatSprite("wild_habitat_bar_tide", "wild_habitat_bar")
                        : LoadHabitatSprite("wild_habitat_bar", "world_map_paper_panel");
                }
                if (_wildHabitatIconImage != null)
                {
                    _wildHabitatIconImage.sprite = tideActive
                        ? LoadHabitatSprite("wild_habitat_icon_tide", "wild_habitat_icon")
                        : LoadHabitatSprite("wild_habitat_icon", "seal_tide");
                }
                if (_wildHabitatBtnImage != null)
                {
                    _wildHabitatBtnImage.sprite = tideActive
                        ? LoadHabitatSprite("wild_habitat_btn_tide", "wild_habitat_btn")
                        : LoadHabitatSprite("wild_habitat_btn", "tide_quick_travel_btn");
                }
                if (_wildHabitatBtnLabel != null)
                {
                    _wildHabitatBtnLabel.text = WorldMapOverviewModels.FormatWildHabitatActionLabel(overview, CurrentScene, serverNowMs);
                }
            }

            _wildHabitatTitle.text = WorldMapOverviewModels.FormatWildHabitatHeadline(overview, CurrentScene, serverNowMs);
            double now = Time.realtimeSinceStartupAsDouble;
            if (!string.IsNullOrEmpty(_wildHabitatNotice) && now < _wildHabitatNoticeUntilSeconds)
            {
                _wildHabitatPets.text = _wildHabitatNotice;
            }
            else
            {
                _wildHabitatNotice = "";
                _wildHabitatPets.text = WorldMapOverviewModels.FormatHabitatSummary(CurrentScene, overview, serverNowMs);
            }
            if (_wildHabitatButton != null)
            {
                _wildHabitatButton.interactable = !_request.IsPending && BattleUiRoot.Instance?.MatchEntry != null;
            }
        }

        private void HandleWildPetEncounterRequested()
        {
            ulong serverNowMs = WorldMapOverviewModels.EstimateServerNowMs(CurrentOverview, OverviewAgeSeconds);
            uint dungeonId = WorldMapOverviewModels.ResolveActiveHabitatDungeonId(CurrentOverview, CurrentScene, serverNowMs);
            if (dungeonId == 0u) return;
            var entry = BattleUiRoot.Instance?.MatchEntry;
            if (entry == null)
            {
                ShowWildHabitatNotice("匹配服务尚未就绪，请稍候重试。");
                return;
            }
            if (!entry.StartWildPetHabitat(dungeonId) && !string.IsNullOrEmpty(entry.Status))
            {
                ShowWildHabitatNotice(entry.Status);
            }
        }

        private void ShowWildHabitatNotice(string message)
        {
            _wildHabitatNotice = message ?? "";
            _wildHabitatNoticeUntilSeconds = Time.realtimeSinceStartupAsDouble + 3d;
            if (_wildHabitatPets != null && !string.IsNullOrEmpty(_wildHabitatNotice))
            {
                _wildHabitatPets.text = _wildHabitatNotice;
            }
        }

        private void HandleTideQuickTravelRequested()
        {
            if (RequestQuickTravelToTide(out string error)) return;
            _status = error;
            Refresh();
        }

        /// <summary>一键直达当前进行中的灵潮地图推荐聚人线（复用正式换图状态机）。</summary>
        public bool RequestQuickTravelToTide(out string error)
        {
            var tide = _channelClient?.Overview?.Tide;
            if (tide == null || !tide.Active || tide.SceneConfigId == 0u)
            {
                error = "当前暂无进行中的灵潮。";
                return false;
            }
            if (tide.SceneConfigId == CurrentScene)
            {
                error = "你已身处灵潮地图。";
                return false;
            }
            return RequestTravelToScene(tide.SceneConfigId, tide.RecommendedSceneId, out error);
        }

        /// <summary>外部入口（如神秘商人跨图一键前往）预检能否通过地图传送状态机发车。</summary>
        public bool CanAcceptDirectSceneTravel(uint sceneConfigId, out string blockedReason)
        {
            blockedReason = "";
            if (_game == null || !_game.InGame || !_game.IsGateReady || _app?.WorldMap == null)
            {
                blockedReason = "请稍候再试。";
                return false;
            }
            if (!CanTravelNow())
            {
                blockedReason = "战斗或观战中无法传送。";
                return false;
            }
            if (_request.IsPending)
            {
                blockedReason = "正在传送中，请稍候。";
                return false;
            }
            var destination = CurrentMapCatalog.FindForOnline(sceneConfigId);
            if (destination == null || !destination.CanTravelOnline)
            {
                blockedReason = MysteryMerchantUiRoot.OtherMapText;
                return false;
            }
            return true;
        }

        /// <summary>跨图直达指定大世界地图与分线（复用正式传送状态机与跨节点交接超时保护）。</summary>
        public bool RequestTravelToScene(uint sceneConfigId, ulong sceneId, out string error)
        {
            if (!CanAcceptDirectSceneTravel(sceneConfigId, out error)) return false;
            var destination = CurrentMapCatalog.FindForOnline(sceneConfigId);
            string appearance = destination.Appearance ?? "normal";
            if (destination.ForAppearance(appearance) == null) appearance = "normal";
            BeginTravel(sceneConfigId, appearance, sceneId);
            return true;
        }

        public void RequestTravel(uint sceneConfigId, bool festival)
            => RequestTravel(sceneConfigId, festival ? "spring" : "normal");

        public void RequestTravel(uint sceneConfigId, string appearance)
        {
            if (_game == null || !_game.InGame || !_game.IsGateReady || !CanTravelNow() || _request.IsPending) return;
            var destination = CurrentMapCatalog.FindForOnline(sceneConfigId);
            if (destination == null || !destination.CanTravelOnline || destination.ForAppearance(appearance) == null || _app.WorldMap == null) return;
            BeginTravel(sceneConfigId, appearance);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool IsLocalTestTravelPending => _request.IsPending;
        public string LocalTestTravelStatus => _status;

        /// <summary>显式本机开发入口，复用正式请求/超时/断线处理，正式地图窗口仍拒绝 clientOnly 图。</summary>
        public bool RequestLocalFestivalTravel(uint sceneConfigId, string appearance)
        {
            if (_game == null || !_game.InGame || !_game.IsGateReady || !CanTravelNow() || _request.IsPending ||
                _app?.WorldMap == null) return false;
            var map = CurrentMapCatalog.Find(sceneConfigId);
            if (!FestivalMapLocalTestPolicy.CanUseLocalDelivery(map, appearance, Application.isEditor || Debug.isDebugBuild,
                    System.Environment.GetCommandLineArgs(), _game.GatewayBaseUrl, _game.AssignedGate)) return false;
            Debug.Log($"[FestivalMapLocalTest] REQUEST scene={sceneConfigId} appearance={appearance} gate={_game.AssignedGate}");
            BeginTravel(sceneConfigId, appearance);
            return true;
        }

        /// <summary>手绘地图的本机联调请求；复用正式传送状态机，不改变正式地图目的地。</summary>
        public bool RequestLocalAuthoredTravel(uint sceneConfigId)
        {
            if (_game == null || !_game.InGame || !_game.IsGateReady || !CanTravelNow() || _request.IsPending ||
                _app?.WorldMap == null || !AuthoredFestivalLocalPolicy.CanUse(_game.GatewayBaseUrl, _game.AssignedGate))
                return false;
            string appearance = sceneConfigId == 26000 ? "mid-autumn" : sceneConfigId == 26008 ? "spring" : null;
            var map = CurrentMapCatalog.Find(sceneConfigId);
            if (appearance == null || map == null || !map.IsAuthoredFlat || map.ForAppearance(appearance) == null)
                return false;
            BeginTravel(sceneConfigId, appearance);
            return true;
        }
#endif

        private void BeginTravel(uint sceneConfigId, string appearance, ulong targetSceneId = 0)
        {
            if (sceneConfigId == CurrentScene && targetSceneId == 0)
            {
                _status = _app.WorldMap.TrySetAppearance(appearance)
                    ? "景色已更新，愿此行尽兴。" : "景色暂未准备好，请稍后重试。";
                Refresh();
                return;
            }
            int generation = _request.Begin(sceneConfigId, appearance != "normal", Time.realtimeSinceStartup);
            if (generation == 0) return;
            _pendingAppearance = appearance;
            _status = "正在启程，请稍候…";
            Refresh();
            // targetSceneId == 0 由服务端分配聚人线；非 0 时请求直达该分线（如跨图寻神秘商人）。
            StartCoroutine(_game.EnterScene(sceneConfigId, targetSceneId,
                () =>
                {
                    // 受理不等于马上换图：目的地落在别的节点时，服务端要先冻结玩家、存盘、再重发进场请求，
                    // 最坏一分钟左右才有结论（换图成功，或补推一条失败提示）。客户端分不清这次走没走这条路，
                    // 所以受理后一律按服务端的交接预算顺延截止时间；仍用三十秒的话会先报“未收到抵达消息”，
                    // 玩家却还被冻结着，随后又真的换了图，或失败提示到达时请求已不在途、原因看不到。
                    if (!_request.Accept(generation, Time.realtimeSinceStartup,
                            CityTravelRequest.AcceptedHandoffBudgetSeconds)) return;
                    _status = "行程已安排，正在进入目的地…";
                    Refresh();
                },
                error =>
                {
                    if (!_request.Fail(generation)) return;
                    Debug.LogWarning("[CityTravel] 传送请求失败：" + error);
                    _status = "暂时无法前往，请稍后重试。";
                    Refresh();
                }));
        }

        /// <summary>
        /// 跨区传送。与同区换图共用同一个请求状态机和“正在传送”遮罩，区别只有三点：
        /// 走另一条请求；等待上限放宽；目的地与当前地图相同也照常前往（那是别的区服的同名地图）。
        /// 请求被受理不算抵达，抵达仍然只认入场通知。
        /// </summary>
        public void RequestZoneTravel(uint zoneId, uint sceneConfigId, bool festival)
            => RequestZoneTravel(zoneId, sceneConfigId, festival ? "spring" : "normal");

        public void RequestZoneTravel(uint zoneId, uint sceneConfigId, string appearance)
        {
            if (_game == null || !_game.InGame || !_game.IsGateReady || !CanTravelNow() || _request.IsPending) return;
            var destination = CurrentMapCatalog.FindForOnline(sceneConfigId);
            if (zoneId == 0 || destination == null || !destination.CanTravelOnline || destination.ForAppearance(appearance) == null || _app.WorldMap == null) return;
            int generation = _request.Begin(sceneConfigId, appearance != "normal", Time.realtimeSinceStartup,
                CityTravelRequest.CrossZoneTimeoutSeconds);
            if (generation == 0) return;
            _pendingZoneId = zoneId;
            _pendingAppearance = appearance;
            _gateAtRequest = _game.GateConnectionIdentity;
            _status = "正在启程，请稍候…";
            Refresh();
            StartCoroutine(_game.ZoneTravel.TravelToZone(zoneId, sceneConfigId,
                () =>
                {
                    if (!_request.Accept(generation)) return;
                    _status = "行程已安排，正在前往目标区服…";
                    Refresh();
                },
                error =>
                {
                    if (!_request.Fail(generation)) return;
                    _pendingZoneId = 0;
                    _gateAtRequest = null;
                    Debug.LogWarning("[CityTravel] 跨区传送请求失败：" + error);
                    // 错误文本由底层按提示码给出（认不出的码退回带编号的兜底文本），直接给玩家看，方便反馈问题。
                    _status = "暂时无法前往，请稍后重试。（" + error + "）";
                    Refresh();
                }));
        }

        /// <summary>
        /// 此刻所在的区：以连接层为准（当前连着的那个网关属于哪个区，换服后由服务端签发的票据给出）。
        /// 还没绑定客户端、或连接层给不出（零）时，退回选区时的区服。
        /// </summary>
        private uint CurrentZoneId
        {
            get
            {
                uint connected = _game?.CurrentZoneId ?? 0u;
                return connected != 0 ? connected : (_app?.Session?.SelectedZoneId ?? 0u);
            }
        }

        /// <summary>
        /// 可前往的其他区服：取选区时网关下发的列表，去掉当前所在区和不可进入的区
        /// （口径与选区界面一致：维护、关闭、未开放不可进）。列表只在选区时刷新，可能过时，
        /// 所以这里只管展示，目标区是否存在、是否繁忙由服务端在传送请求里裁决并回提示。
        /// </summary>
        private List<CityTravelZone> BuildZones()
        {
            var result = new List<CityTravelZone>();
            var zones = _app?.Session?.Zones;
            if (zones == null) return result;
            uint here = CurrentZoneId;
            foreach (var zone in zones)
            {
                if (zone == null || zone.zone_id == 0 || zone.zone_id == here) continue;
                if (zone.status == "MAINTENANCE" || zone.status == "CLOSED" || zone.status == "PREVIEW") continue;
                result.Add(new CityTravelZone
                {
                    ZoneId = zone.zone_id,
                    Name = string.IsNullOrWhiteSpace(zone.name) ? zone.zone_id + "区" : zone.name
                });
            }
            return result;
        }

        private uint CurrentScene => _game?.CurrentSceneConfigId is > 0 ? _game.CurrentSceneConfigId : 1;

        private bool CanTravelNow()
            => (_game?.Battle == null || _game.Battle.Phase == BattlePhase.None) &&
               (_game?.Spectate == null || _game.Spectate.Phase == SpectatePhase.None) &&
               !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false);

        private void Bind(GameClient game)
        {
            if (_game == game) return;
            if (_game != null)
            {
                _game.OnSceneEntered -= HandleSceneEntered;
                _game.OnDisconnected -= HandleDisconnected;
                _game.OnServerTip -= HandleServerTip;
            }
            _request.Reset();
            _pendingZoneId = 0;
            _gateAtRequest = null;
            _dismissedTideRoundId = 0UL;
            _overviewBootstrapped = false;
            _nextHudOverviewRefreshSeconds = 0d;
            _wildHabitatNotice = "";
            _wildHabitatNoticeUntilSeconds = 0d;
            _status = "";
            HidePanel();
            _game = game;
            if (_game != null)
            {
                _game.OnSceneEntered += HandleSceneEntered;
                _game.OnDisconnected += HandleDisconnected;
                _game.OnServerTip += HandleServerTip;
            }
        }

        private void HandleSceneEntered(SceneInfoComp scene)
        {
            // 入场通知到了，这趟行程（不论同区还是跨区）到此有了结论；人在哪个区由连接层给出，这里不记。
            _pendingZoneId = 0;
            _gateAtRequest = null;
            if (_request.ReceiveScene(scene?.SceneConfigId ?? 0, out bool festival))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // 两个订阅者的回调顺序可能不同；等地图运行时完成入场，再报告手绘地图是否就绪。
                if (AuthoredMapRegistry.IsEnabled && (scene.SceneConfigId == 26000 || scene.SceneConfigId == 26008))
                {
                    _status = "已收到抵达通知，正在确认地图…";
                    StartCoroutine(ConfirmAuthoredArrival(scene.SceneConfigId, _pendingAppearance));
                    HidePanel();
                    Refresh();
                    return;
                }
#endif
                bool applied = _app.WorldMap != null && _app.WorldMap.TrySetAppearance(_pendingAppearance);
                _status = applied ? "已抵达，沿着街巷自在游历吧。" : "已抵达，所选景色暂未准备好。";
                HidePanel();
            }
            else _status = "";
            Refresh();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private System.Collections.IEnumerator ConfirmAuthoredArrival(uint sceneConfigId, string appearance)
        {
            yield return null;
            if (_game == null || !_game.InGame || _game.CurrentSceneConfigId != sceneConfigId) yield break;
            var map = _app?.WorldMap;
            bool applied = map != null && map.ActiveSceneConfigId == sceneConfigId && map.Appearance == appearance;
            _status = applied ? "已抵达，地图已就绪。" : "已抵达，但地图未就绪，请重新进入或重连。";
            Refresh();
        }
#endif

        /// <summary>
        /// 服务端提示到达时，如果有传送请求在途，判断这次行程是不是没成，没成就立刻收场。
        /// 受理之后才发生的失败（同区换图被换手门拒绝、跨区在冻结存盘之后被拒）不会出现在请求的应答里，
        /// 服务端只能补推一条提示；不接它，窗口就只能干等到超时。
        /// 跨区在途时按码收窄，判据与底层同一份（GameClient.IsTravelFailureTip）：两边脱节的话，一条无关提示
        /// 会让窗口先报失败，而底层仍在途，随后玩家又被换服通知搬走，或在等待预算内再点被“正在传送中”挡回。
        /// 同区换图维持宽判：请求在途时窗口挡着输入，几乎不会有别的提示；它不换连接，
        /// 即使误判，后果也只是遮罩提前收起，随后到达的入场通知照常生效。
        /// </summary>
        private void HandleServerTip(TipInfoMessage tip)
        {
            if (!_request.IsPending) return;
            // 连接已经在换了，说明服务端早已放行；这时的提示来自目标区的登录流程，与行程成败无关。
            if (_game != null && _game.IsRedirecting) return;
            uint tipId = tip?.Id ?? 0;
            // 跨区在途：不是传送失败码就放过去，由底层与本窗口各自的等待预算兜底。
            if (_pendingZoneId != 0 && !GameClient.IsTravelFailureTip(tipId)) return;
            _request.Reset();
            _pendingZoneId = 0;
            _gateAtRequest = null;
            // 文案按码取；认不出的码会退回带编号的兜底文本，方便玩家反馈。
            _status = GameClient.DescribeTravelTip(tipId);
            Refresh();
        }

        private void HandleDisconnected()
        {
            _request.Reset();
            _pendingZoneId = 0;
            _gateAtRequest = null;
            _dismissedTideRoundId = 0UL;
            _overviewBootstrapped = false;
            _nextHudOverviewRefreshSeconds = 0d;
            _wildHabitatNotice = "";
            _wildHabitatNoticeUntilSeconds = 0d;
            _status = "";
            HidePanel();
            Refresh();
        }

        private void Refresh()
        {
            if (_window?.Root == null) return;
            _window.SetState(CurrentScene, _app?.WorldMap?.Appearance ?? "normal",
                _request.IsPending, _status);
        }

        private static bool IsTyping() => CityPanelRegistry.IsTyping();

        private void BindChannelClient(SceneChannelClient client)
        {
            if (ReferenceEquals(_channelClient, client)) return;
            if (_channelClient != null) _channelClient.Changed -= HandleChannelClientChanged;
            _channelClient = client;
            if (_channelClient != null) _channelClient.Changed += HandleChannelClientChanged;
            if (_window != null) _window.SetOverview(CurrentOverview, OverviewAgeSeconds);
            RefreshTideHudBadge();
        }

        private void HandleChannelClientChanged()
        {
            if (_window != null && _window.IsVisible)
            {
                _window.SetOverview(CurrentOverview, OverviewAgeSeconds);
                _nextTideBannerRefreshSeconds = Time.realtimeSinceStartupAsDouble + 1d;
                MarkCurrentTideSeen();
                return;
            }
            RefreshTideHudBadge();
        }

        private void OnDestroy()
        {
            CityPanelRegistry.Unregister(this);
            if (_window != null) _window.TideQuickTravelRequested -= HandleTideQuickTravelRequested;
            BindChannelClient(null);
            Bind(null);
            if (Instance == this) Instance = null;
        }

        public static CityTravelDestination[] CreateDestinations()
        {
            var destinations = new List<CityTravelDestination>();
            foreach (var installed in CurrentMapCatalog.All)
            {
                var map = CurrentMapCatalog.FindForOnline(installed.SceneConfigId) ?? installed;
                destinations.Add(new CityTravelDestination
                {
                    SceneConfigId = map.SceneConfigId, Name = map.Name, Map = map,
                    Description = map.SceneConfigId == 1 ? "主城暂用碧渊水宫替代，石桥连廊通往太极中庭、主殿与钟鼓亭。" :
                        map.SceneConfigId == 2 ? "八景仙岛四桥贯通中央太极广场与各色亭台。" :
                        map.SceneConfigId == 3 ? "月汐湾码头停舟，渔舍临海，石阶与海湾相连。" :
                        map.SceneConfigId == 5 ? "碧水环抱重檐水宫，石桥连廊通往太极中庭、主殿与钟鼓亭。" :
                        map.CanTravelOnline ? "选择景色，沿石路自在游历。" : "景色已收录，此地图尚未开放传送。",
                    DayResourcePath = map.ForAppearance("normal")?.TexturePath,
                    FestivalResourcePath = map.ForAppearance("spring")?.TexturePath,
                    FestivalName = "春节", DefaultAppearance = map.Appearance,
                    OnlineAvailable = map.CanTravelOnline
                });
            }
            if (CurrentMapCatalog.Find(4u) == null)
                destinations.Add(new CityTravelDestination
                {
                    SceneConfigId = 4, Name = "青石渡 · 未开放", Description = "当前地图暂未开放。",
                    Available = false, OnlineAvailable = false
                });
            return destinations.ToArray();
        }

        /// <summary>筛选正式开放可联机传送的目的地（供「游历」页签或底图可交互锚点绑定）。</summary>
        public static CityTravelDestination[] FilterOnlineDestinations(IReadOnlyList<CityTravelDestination> all)
        {
            var result = new List<CityTravelDestination>();
            if (all != null)
            {
                for (int i = 0; i < all.Count; ++i)
                {
                    if (all[i] != null && all[i].Available && all[i].OnlineAvailable) result.Add(all[i]);
                }
            }
            return result.ToArray();
        }

        /// <summary>筛选仅收录预览/节庆景色、尚未开放联机传送的目的地（供「览景」页签绑定）。</summary>
        public static CityTravelDestination[] FilterGalleryDestinations(IReadOnlyList<CityTravelDestination> all)
        {
            var result = new List<CityTravelDestination>();
            if (all != null)
            {
                for (int i = 0; i < all.Count; ++i)
                {
                    if (all[i] != null && all[i].Available && !all[i].OnlineAvailable) result.Add(all[i]);
                }
            }
            return result.ToArray();
        }
    }
}
