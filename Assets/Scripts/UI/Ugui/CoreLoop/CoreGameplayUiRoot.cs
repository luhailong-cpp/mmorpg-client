using System;
using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Jubaozhai;
using MmorpgClient.UI.Ugui.Pet;
using MmorpgClient.UI.Ugui.Ranked;
using MmorpgClient.UI.Ugui.Team;
using MmorpgClient.UI.Ugui.TrialJournal;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.CoreLoop
{
    /// <summary>核心玩法表现层的会话所有者；全部写操作交给纯 C# 门面。</summary>
    [DisallowMultipleComponent]
    public sealed class CoreGameplayUiRoot : MonoBehaviour
    {
        public static CoreGameplayUiRoot Instance { get; private set; }
        public const float TopSafeRightX = 2200f;

        public CoreGameplayLoopClient Core => _core;
        public GameClient Game => _game;
        public bool IsFormalPvpVisible => _windowLayer != null && _windowLayer.gameObject.activeSelf &&
            _formalPvp != null && _formalPvp.IsVisible;

        private GameClient _game;
        private BattleMatchEntryClient _matchEntry;
        private CoreGameplayLoopClient _core;
        private ulong _playerId;
        private object _connectionIdentity;

        public const int HudCanvasSortingOrder = 137;
        public const int WindowCanvasSortingOrder = 188;
        public const int BattleOverlayCanvasSortingOrder = 205;

        private Canvas _hudCanvas;
        private Canvas _windowCanvas;
        private Canvas _battleCanvas;
        private RectTransform _hudSafeRoot;
        private RectTransform _windowSafeRoot;
        private RectTransform _battleSafeRoot;
        private RectTransform _hudDesign;
        private RectTransform _windowLayer;
        private RectTransform _windowDesign;
        private RectTransform _battleDesign;
        private RectTransform _spendPreview;
        private TMP_Text _spendPreviewTitle;
        private TMP_Text _spendPreviewBody;
        private RectTransform _toastDesign;
        private TMP_Text _toastText;
        private float _toastExpiresAt;
        private string _lastToast;
        private float _lastToastAt;
        private RectTransform _marketPreviewBanner;
        private bool _marketPreviewActive;
        private Rect _lastSafePixels;
        private Rect _lastCanvasPixels;

        private CoreMainCityHubPanel _mainCity;
        private BossTrialPrepPanel _bossTrials;
        private FormalPvpLobbyPanel _formalPvp;
        private SeasonRotationPanel _rotations;
        private SeasonSealPanel _seasonSeal;
        private WuxingTowerPanel _wuxingTower;
        private CustomDuelChallengePanel _customDuel;
        private BattleCoreHudOverlay _battleHud;
        private CoreSettlementRewardSection _battleSettlement;
        private CoreSettlementRewardSection _rankedSettlement;
        private int _pendingMaterialClaimFrame = -1;
        private int _pendingAttributeAllocFrame = -1;
        private bool _lastBattleWasWuxingTower;
        private bool _pendingWuxingTowerReopen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn() => EnsureSpawned();

        public static void EnsureSpawned()
        {
            if (Instance != null) return;
            var host = new GameObject("[CoreGameplayUi]");
            DontDestroyOnLoad(host);
            host.AddComponent<CoreGameplayUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            CityPanelRegistry.Register(this, HideWindows);
            BuildCanvas();
        }

        private void OnEnable() => ApplySafeArea();

        private void Update()
        {
            ApplySafeArea();
            if (_toastDesign != null && _toastDesign.gameObject.activeSelf &&
                Time.unscaledTime >= _toastExpiresAt)
                _toastDesign.gameObject.SetActive(false);
            var game = AppBootstrap.Instance?.GameClient;
            var matchEntry = TeamUiRoot.Instance?.MatchEntry;
            bool ready = game != null && game.InGame && game.IsGateReady && game.PlayerId != 0 &&
                matchEntry != null;
            if (!ready)
            {
                if (_core != null) Unbind();
                return;
            }

            if (!ReferenceEquals(game, _game) || !ReferenceEquals(matchEntry, _matchEntry) ||
                game.PlayerId != _playerId || !ReferenceEquals(game.GateConnectionIdentity, _connectionIdentity))
                Bind(game, matchEntry);

            SyncVisibility();
            _mainCity?.Tick();
            _formalPvp?.Tick();
            _rotations?.Tick();
            SyncMarketPreview();
            if (_pendingMaterialClaimFrame >= 0 && Time.frameCount >= _pendingMaterialClaimFrame &&
                !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false))
            {
                _pendingMaterialClaimFrame = -1;
                GameplayUiRoot.Instance?.OpenEquipmentMaterials();
            }
            if (_pendingAttributeAllocFrame >= 0 && Time.frameCount >= _pendingAttributeAllocFrame &&
                !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false))
            {
                _pendingAttributeAllocFrame = -1;
                AttributeUiRoot.Instance?.OpenPanel(true);
            }
            if (_pendingWuxingTowerReopen && CanOpenWorldPanel)
            {
                _pendingWuxingTowerReopen = false;
                OpenWuxingTower();
            }
            HandleEscapeKey();
        }

        private Canvas CreateLayerCanvas(string name, int sortingOrder, bool interactive, out RectTransform safeRoot)
        {
            var canvasObject = interactive
                ? new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
                : new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            safeRoot = QdaoUguiFactory.CreateStretch(name + "SafeArea", canvasObject.transform, Vector4.zero);
            return canvas;
        }

        private void BuildCanvas()
        {
            // 三层独立画布：主城常驻 HUD (137 < WorldHud 139 < 所有业务窗 160..190)，
            // 核心玩法弹窗 (188 与排位/聚宝斋同级 < 战斗屏 200)，战斗叠加与轻提示 (205 压过战斗屏且不拦截点击)。
            _hudCanvas = CreateLayerCanvas("CoreHudCanvas", HudCanvasSortingOrder, interactive: true, out _hudSafeRoot);
            _hudDesign = QdaoUguiFactory.CreateCenteredRect("CoreHudDesign", _hudSafeRoot, 2560f, 1080f);
            AddFit(_hudDesign);

            _windowCanvas = CreateLayerCanvas("CoreWindowCanvas", WindowCanvasSortingOrder, interactive: true, out _windowSafeRoot);
            _windowLayer = QdaoUguiFactory.CreateStretch("CoreWindowLayer", _windowSafeRoot, Vector4.zero);
            var shade = _windowLayer.gameObject.AddComponent<Image>();
            shade.color = new Color(0.025f, 0.08f, 0.07f, 0.56f);
            shade.raycastTarget = true;
            _windowLayer.gameObject.AddComponent<GameplayInputBlocker>();
            _windowDesign = QdaoUguiFactory.CreateCenteredRect("CoreWindowDesign", _windowLayer, 2560f, 1080f);
            AddFit(_windowDesign);
            _windowLayer.gameObject.SetActive(false);

            _battleCanvas = CreateLayerCanvas("CoreBattleOverlayCanvas", BattleOverlayCanvasSortingOrder, interactive: false, out _battleSafeRoot);
            _battleDesign = QdaoUguiFactory.CreateCenteredRect("CoreBattleHudDesign", _battleSafeRoot, 2560f, 1080f);
            AddFit(_battleDesign);
            _battleDesign.gameObject.SetActive(false);

            _toastDesign = QdaoUguiFactory.CreateCenteredRect("CoreToastDesign", _battleSafeRoot, 2560f, 1080f);
            AddFit(_toastDesign);
            CoreLoopUiArt.Image("ToastPlate", _toastDesign, 705f, 265f, 1150f, 68f,
                "badge", new Color(0.09f, 0.28f, 0.22f, 0.95f));
            _toastText = CoreLoopUiArt.Text("ToastText", _toastDesign, 728f, 272f, 1104f, 54f,
                string.Empty, 28f, CoreLoopUiArt.Ivory, true, true, TextAlignmentOptions.Center);
            _toastDesign.gameObject.SetActive(false);

            _marketPreviewBanner = QdaoUguiFactory.CreateCenteredRect("CoreMarketPreviewBanner",
                _battleSafeRoot, 2560f, 1080f);
            AddFit(_marketPreviewBanner);
            CoreLoopUiArt.Image("MarketPreviewPlate", _marketPreviewBanner, 430f, 24f,
                1700f, 56f, null, new Color(0.94f, 0.91f, 0.81f, 0.95f));
            CoreLoopUiArt.Text("MarketPreviewText", _marketPreviewBanner, 455f, 28f,
                1650f, 46f, "万象阁 · 可交易材料玩家间流通，不扣减永久 660 免费额度",
                25f, CoreLoopUiArt.DeepJade, true, false, TextAlignmentOptions.Center);
            _marketPreviewBanner.gameObject.SetActive(false);
            ApplySafeArea();
        }

        private static void AddFit(RectTransform rect)
        {
            var fit = rect.gameObject.AddComponent<EquipmentUiFit>();
            fit.referenceSize = new Vector2(2560f, 1080f);
            fit.Fit();
        }

        private void ApplySafeArea()
        {
            if (_hudCanvas == null || _hudSafeRoot == null) return;
            var pixels = _hudCanvas.pixelRect;
            if (pixels.width <= 0 || pixels.height <= 0) return;
            var screenSafe = Screen.safeArea;
            var safe = screenSafe.width > 0 && screenSafe.height > 0
                ? Rect.MinMaxRect(Mathf.Max(pixels.xMin, screenSafe.xMin), Mathf.Max(pixels.yMin, screenSafe.yMin),
                    Mathf.Min(pixels.xMax, screenSafe.xMax), Mathf.Min(pixels.yMax, screenSafe.yMax))
                : pixels;
            if (safe.width <= 0 || safe.height <= 0) safe = pixels;
            if (pixels == _lastCanvasPixels && safe == _lastSafePixels) return;
            _lastCanvasPixels = pixels;
            _lastSafePixels = safe;
            var anchorMin = new Vector2((safe.xMin - pixels.xMin) / pixels.width,
                (safe.yMin - pixels.yMin) / pixels.height);
            var anchorMax = new Vector2((safe.xMax - pixels.xMin) / pixels.width,
                (safe.yMax - pixels.yMin) / pixels.height);
            ApplySafeAnchors(_hudSafeRoot, anchorMin, anchorMax);
            ApplySafeAnchors(_windowSafeRoot, anchorMin, anchorMax);
            ApplySafeAnchors(_battleSafeRoot, anchorMin, anchorMax);
            // 微信与抖音宿主胶囊位于右上；所有子屏顶栏与战斗倒计时只使用 x < TopSafeRightX。
            // 这块保守留白与 Screen.safeArea 同时生效，不依赖尚未装入客户端的宿主 JS 尺寸接口。
        }

        private static void ApplySafeAnchors(RectTransform root, Vector2 anchorMin, Vector2 anchorMax)
        {
            if (root == null) return;
            root.anchorMin = anchorMin;
            root.anchorMax = anchorMax;
            root.offsetMin = root.offsetMax = Vector2.zero;
        }

        private static T CreatePanel<T>(string name, UnityEngine.Transform parent) where T : Component
        {
            var rect = QdaoUguiFactory.CreateStretch(name, parent, Vector4.zero);
            rect.gameObject.SetActive(false);
            return rect.gameObject.AddComponent<T>();
        }

        private void Bind(GameClient game, BattleMatchEntryClient matchEntry)
        {
            Unbind();
            _game = game;
            _matchEntry = matchEntry;
            _playerId = game.PlayerId;
            _connectionIdentity = game.GateConnectionIdentity;
            _core = new CoreGameplayLoopClient(game.Battle, matchEntry, game.Ranked,
                game.TrialJournal, game.Season, game.Features);

            _mainCity = CreatePanel<CoreMainCityHubPanel>("CoreMainCityHubPanel", _hudDesign);
            _bossTrials = CreatePanel<BossTrialPrepPanel>("BossTrialPrepPanel", _windowDesign);
            _formalPvp = CreatePanel<FormalPvpLobbyPanel>("FormalPvpLobbyPanel", _windowDesign);
            _rotations = CreatePanel<SeasonRotationPanel>("SeasonRotationPanel", _windowDesign);
            _seasonSeal = CreatePanel<SeasonSealPanel>("SeasonSealPanel", _windowDesign);
            _wuxingTower = CreatePanel<WuxingTowerPanel>("WuxingTowerPanel", _windowDesign);
            _customDuel = CreatePanel<CustomDuelChallengePanel>("CustomDuelChallengePanel", _windowDesign);
            _battleHud = CreatePanel<BattleCoreHudOverlay>("BattleCoreHudOverlay", _battleDesign);
            BuildSpendPreview();
            _mainCity.Initialize(this, _core, game);
            _bossTrials.Initialize(this, _core, game);
            _formalPvp.Initialize(this, _core, game);
            _rotations.Initialize(this, _core, game);
            _seasonSeal.Initialize(this, _core, game);
            _wuxingTower.Initialize(this, _core, game);
            _customDuel.Initialize(this, game);
            _battleHud.Initialize(this, _core, game);

            _core.Changed += RefreshAll;
            _core.Notice += ShowToast;
            if (game.Ranked != null) game.Ranked.Changed += RefreshAll;
            if (game.TrialJournal != null)
            {
                game.TrialJournal.Changed += RefreshAll;
                game.TrialJournal.Notice += ShowToast;
            }
            if (game.Season != null)
            {
                game.Season.Changed += RefreshAll;
                game.Season.Notice += ShowToast;
            }
            if (game.Features != null)
            {
                game.Features.OnChanged += RefreshAll;
                game.Features.OnEquipLoadoutSwitched += HandleEquipLoadoutSwitched;
            }
            if (game.Attributes != null) game.Attributes.OnPanel += HandleAttributePanelChanged;
            game.Battle.OnPhaseChanged += HandleBattlePhaseChanged;
            game.Battle.OnBattleStart += HandleBattleStart;
            game.Battle.OnTurnResult += HandleTurnResult;
            game.Battle.OnStateUpdated += HandleBattleStateUpdated;
            game.Battle.OnBattleEnd += HandleBattleEnd;
            game.Battle.OnQueueStatus += HandleQueueStatus;

            if (game.Ranked?.Profile == null) game.Ranked?.RequestProfile(includeHistory: false);
            if (game.TrialJournal?.Snapshot == null) game.TrialJournal?.RequestSnapshot();
            if (game.Season?.Snapshot == null) game.Season?.RequestState();
            if (game.Features?.EquipmentMaterialReward == null) game.Features?.RequestEquipmentMaterialReward();
            PreBattleLoadoutQuickBar.EnsureRequested(game.Features);
            if (game.Attributes?.Panel == null) game.Attributes?.RequestPanel();
            RefreshAll();
            SyncVisibility();
        }

        private void Unbind()
        {
            HideWindows();
            _mainCity?.Hide();
            _battleHud?.Hide();
            _battleDesign?.gameObject.SetActive(false);
            if (_core != null)
            {
                _core.Changed -= RefreshAll;
                _core.Notice -= ShowToast;
            }
            if (_game != null)
            {
                if (_game.Ranked != null) _game.Ranked.Changed -= RefreshAll;
                if (_game.TrialJournal != null)
                {
                    _game.TrialJournal.Changed -= RefreshAll;
                    _game.TrialJournal.Notice -= ShowToast;
                }
                if (_game.Season != null)
                {
                    _game.Season.Changed -= RefreshAll;
                    _game.Season.Notice -= ShowToast;
                }
                if (_game.Features != null)
                {
                    _game.Features.OnChanged -= RefreshAll;
                    _game.Features.OnEquipLoadoutSwitched -= HandleEquipLoadoutSwitched;
                }
                if (_game.Attributes != null) _game.Attributes.OnPanel -= HandleAttributePanelChanged;
                if (_game.Battle != null)
                {
                    _game.Battle.OnPhaseChanged -= HandleBattlePhaseChanged;
                    _game.Battle.OnBattleStart -= HandleBattleStart;
                    _game.Battle.OnTurnResult -= HandleTurnResult;
                    _game.Battle.OnStateUpdated -= HandleBattleStateUpdated;
                    _game.Battle.OnBattleEnd -= HandleBattleEnd;
                    _game.Battle.OnQueueStatus -= HandleQueueStatus;
                }
            }
            _core?.Dispose();
            _core = null;
            _game = null;
            _matchEntry = null;
            _playerId = 0;
            _connectionIdentity = null;
            _pendingMaterialClaimFrame = -1;
            _pendingAttributeAllocFrame = -1;
            _lastBattleWasWuxingTower = false;
            _pendingWuxingTowerReopen = false;
            _marketPreviewActive = false;
            if (_marketPreviewBanner != null) _marketPreviewBanner.gameObject.SetActive(false);
            DestroyPanel(ref _mainCity);
            DestroyPanel(ref _bossTrials);
            DestroyPanel(ref _formalPvp);
            DestroyPanel(ref _rotations);
            DestroyPanel(ref _seasonSeal);
            DestroyPanel(ref _wuxingTower);
            DestroyPanel(ref _customDuel);
            DestroyPanel(ref _battleHud);
            DestroyPanel(ref _battleSettlement);
            DestroyPanel(ref _rankedSettlement);
            if (_spendPreview != null) Destroy(_spendPreview.gameObject);
            _spendPreview = null;
            _spendPreviewTitle = null;
            _spendPreviewBody = null;
            if (_toastDesign != null) _toastDesign.gameObject.SetActive(false);
        }

        private static void DestroyPanel<T>(ref T panel) where T : Component
        {
            if (panel != null)
            {
                panel.gameObject.SetActive(false);
                Destroy(panel.gameObject);
            }
            panel = null;
        }

        private bool HasLiveSession => this != null && isActiveAndEnabled && _core != null &&
            _game != null && _game.PlayerId == _playerId &&
            ReferenceEquals(_game.GateConnectionIdentity, _connectionIdentity);

        private void RefreshAll()
        {
            if (!HasLiveSession) return;
            if (_mainCity != null && _mainCity.IsVisible) _mainCity.Refresh();
            if (_bossTrials != null && _bossTrials.IsVisible) _bossTrials.Refresh();
            if (_formalPvp != null && _formalPvp.IsVisible) _formalPvp.Refresh();
            if (_rotations != null && _rotations.IsVisible) _rotations.Refresh();
            if (_seasonSeal != null && _seasonSeal.IsVisible) _seasonSeal.Refresh();
            if (_wuxingTower != null && _wuxingTower.IsVisible) _wuxingTower.Refresh();
            if (_customDuel != null && _customDuel.IsVisible) _customDuel.Refresh();
            if (_battleHud != null && _battleHud.IsVisible) _battleHud.Refresh();
            if (_battleSettlement != null && _battleSettlement.IsVisible) _battleSettlement.Refresh();
            if (_rankedSettlement != null && _rankedSettlement.IsVisible) _rankedSettlement.Refresh();
        }

        private void HandleAttributePanelChanged(AttributePanelInfo _) => RefreshAll();

        private void HandleEquipLoadoutSwitched(SwitchEquipLoadoutResponse response)
        {
            if (!HasLiveSession || _windowLayer == null || !_windowLayer.gameObject.activeSelf) return;
            ShowToast(EquipLoadoutModel.BuildSwitchToast(response));
        }

        private void HandleBattlePhaseChanged(BattlePhase _) { if (HasLiveSession) { SyncVisibility(); RefreshAll(); } }
        private void HandleBattleStart(global::BattleStartS2C _)
        {
            if (!HasLiveSession) return;
            var tc = _game?.Battle?.State?.TowerContext;
            if (tc != null && tc.Floor > 0u) _lastBattleWasWuxingTower = true;
            SyncVisibility();
            if (_battleHud != null && _battleHud.IsVisible) _battleHud.Refresh();
        }
        private void HandleTurnResult(global::TurnResultS2C result)
        {
            if (!HasLiveSession || _battleHud == null) return;
            if (result?.Events != null)
            {
                BossMechanicBannerView latest = null;
                for (int i = 0; i < result.Events.Count; ++i)
                {
                    var view = CoreGameplayLoopModels.DescribeBossMechanicEvent(result.Events[i]);
                    if (view != null) latest = view;
                }
                if (latest != null) _battleHud.ShowMechanicBanner(latest);
            }
            if (_battleHud.IsVisible) _battleHud.Refresh();
        }
        private void HandleBattleStateUpdated(global::BattleStateS2C state)
        {
            if (!HasLiveSession) return;
            if (state?.TowerContext != null && state.TowerContext.Floor > 0u) _lastBattleWasWuxingTower = true;
            SyncVisibility();
            if (_battleHud != null && _battleHud.IsVisible) _battleHud.Refresh();
        }
        private void HandleBattleEnd(global::BattleEndS2C _)
        {
            if (!HasLiveSession) return;
            _game?.Features?.RequestEquipmentMaterialReward();
            if (_lastBattleWasWuxingTower)
            {
                _lastBattleWasWuxingTower = false;
                _pendingWuxingTowerReopen = true;
                _game?.Season?.RequestWuxingTowerState(force: true);
            }
            SyncVisibility();
            RefreshAll();
        }
        private void HandleQueueStatus(Match.GetQueueStatusResponse _)
        {
            if (!HasLiveSession) return;
            if (_formalPvp != null && _formalPvp.IsVisible) _formalPvp.Refresh();
            if (_rotations != null && _rotations.IsVisible) _rotations.Refresh();
        }

        private void SyncVisibility()
        {
            if (_mainCity == null || _game == null) return;
            bool battleLayer = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            bool world = _game.InGame && _game.IsGateReady && !battleLayer;
            if (world)
            {
                if (!_mainCity.IsVisible) _mainCity.Show();
            }
            else
            {
                if (_mainCity.IsVisible) _mainCity.Hide();
                if (_windowLayer != null && _windowLayer.gameObject.activeSelf) HideWindows();
            }

            var phase = _game.Battle?.Phase ?? BattlePhase.None;
            bool inBattle = battleLayer && (phase == BattlePhase.Preparing ||
                phase == BattlePhase.WaitingAction || phase == BattlePhase.Resolving);
            if (_battleDesign.gameObject.activeSelf != inBattle) _battleDesign.gameObject.SetActive(inBattle);
            if (inBattle)
            {
                if (_battleHud != null && !_battleHud.IsVisible) _battleHud.Show();
            }
            else if (_battleHud != null && _battleHud.IsVisible) _battleHud.Hide();
        }

        private bool CanOpenWorldPanel => HasLiveSession && _game.InGame && _game.IsGateReady &&
            !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false);

        public void OpenHub() => HideWindows();

        public void OpenBossTrials()
        {
            if (!CanOpenWorldPanel || _bossTrials == null) return;
            CityPanelRegistry.HideOthers(this);
            HideWindows();
            _windowLayer.gameObject.SetActive(true);
            _bossTrials.Show();
            _game.TrialJournal?.RequestSnapshot();
            _bossTrials.Refresh();
        }

        public void OpenFormalPvp()
        {
            if (!CanOpenWorldPanel || _formalPvp == null) return;
            CityPanelRegistry.HideOthers(this);
            HideWindows();
            _windowLayer.gameObject.SetActive(true);
            _formalPvp.Show();
            _game.Ranked?.RequestProfile(includeHistory: true);
            _formalPvp.Refresh();
        }

        public bool EnterFormalPvp(uint size) => HasLiveSession && _core.EnterFormalPvp(size);

        public void OpenRotations()
        {
            if (!CanOpenWorldPanel || _rotations == null) return;
            CityPanelRegistry.HideOthers(this);
            HideWindows();
            _windowLayer.gameObject.SetActive(true);
            _rotations.Show();
            _rotations.Refresh();
        }

        public void OpenSeasonSeal()
        {
            if (!CanOpenWorldPanel || _seasonSeal == null) return;
            CityPanelRegistry.HideOthers(this);
            HideWindows();
            _windowLayer.gameObject.SetActive(true);
            _seasonSeal.Show();
        }

        public void OpenWuxingFormations()
        {
            if (!CanOpenWorldPanel || _seasonSeal == null) return;
            CityPanelRegistry.HideOthers(this);
            HideWindows();
            _windowLayer.gameObject.SetActive(true);
            _seasonSeal.ShowFormations();
        }

        public void OpenWuxingTower()
        {
            if (!CanOpenWorldPanel || _wuxingTower == null) return;
            CityPanelRegistry.HideOthers(this);
            HideWindows();
            _windowLayer.gameObject.SetActive(true);
            _wuxingTower.Show();
        }

        public void OpenCustomDuel(ulong presetTargetPlayerId = 0ul)
        {
            if (!CanOpenWorldPanel || _customDuel == null) return;
            CityPanelRegistry.HideOthers(this);
            HideWindows();
            _windowLayer.gameObject.SetActive(true);
            _customDuel.Show(presetTargetPlayerId);
        }

        public void OpenTrialJournal()
        {
            if (!CanOpenWorldPanel) return;
            TrialJournalUiRoot.EnsureSpawned();
            if (TrialJournalUiRoot.Instance != null)
            {
                HideWindows();
                TrialJournalUiRoot.Instance.Open();
                return;
            }
            if (_seasonSeal == null) return;
            CityPanelRegistry.HideOthers(this);
            HideWindows();
            _windowLayer.gameObject.SetActive(true);
            _seasonSeal.ShowTrialJournal();
        }

        private void BuildSpendPreview()
        {
            _spendPreview = QdaoUguiFactory.CreateRect("CoreSpendPreview", _windowDesign,
                0f, 0f, 2560f, 1080f);
            CoreLoopUiArt.Frame(_spendPreview, "首测预览", CloseCurrent);
            CoreLoopUiArt.Card(_spendPreview, "PreviewCard", 430f, 330f, 1700f, 420f);
            _spendPreviewTitle = CoreLoopUiArt.Text("PreviewTitle", _spendPreview,
                510f, 385f, 1540f, 80f, string.Empty, 42f, CoreLoopUiArt.DeepJade, true,
                false, TextAlignmentOptions.Center);
            _spendPreviewBody = CoreLoopUiArt.Text("PreviewBody", _spendPreview,
                550f, 495f, 1460f, 150f, string.Empty, 31f, CoreLoopUiArt.Ink,
                false, true, TextAlignmentOptions.Center);
            CoreLoopUiArt.Badge(_spendPreview, "PreviewBadge", 1040f, 670f, 480f,
                "首测预览 · 未开直充", CoreLoopUiArt.Warning);
            _spendPreview.gameObject.SetActive(false);
        }

        public void OpenSpendPreview(string slotKey)
        {
            if (!CanOpenWorldPanel || _spendPreview == null) return;
            if (slotKey == "wardrobe" && AppearanceUiRoot.Instance?.ShowPanelPreview() == true)
                return;
            if (slotKey == "material_market" && OpenMarketPreview()) return;
            switch (slotKey)
            {
                case "wardrobe":
                    _spendPreviewTitle.text = "衣橱外观";
                    _spendPreviewBody.text = "道家 Q 版外观与搭配预览。首测暂不开放直充购买。";
                    break;
                case "convenience_loadouts":
                    _spendPreviewTitle.text = "省心便利";
                    _spendPreviewBody.text = "离线托管便利与装备/战法/加点 7 套三合一共享流派席位（2 套永久免费 + 85 级赠 1 套 + 轻享卡 +2 套 + 按套租用，到期只锁不删、零战力加成）。";
                    break;
                case "material_market":
                    _spendPreviewTitle.text = "材料市集";
                    _spendPreviewBody.text = "可交易打造材料与百炼石供玩家间流通；绑定材料仅供自用。流通不扣永久 660 免费额度；首测没有直充入口。";
                    break;
                default: return;
            }
            CityPanelRegistry.HideOthers(this);
            HideWindows();
            _windowLayer.gameObject.SetActive(true);
            _spendPreview.gameObject.SetActive(true);
        }

        private bool OpenMarketPreview()
        {
            var market = JubaozhaiUiRoot.Instance;
            if (market?.Window == null) return false;
            if (!market.Window.IsVisible) market.Toggle();
            if (!market.Window.IsVisible) return false;
            _marketPreviewActive = true;
            SyncMarketPreview();
            return true;
        }

        private void SyncMarketPreview()
        {
            if (!_marketPreviewActive) return;
            var marketWindow = JubaozhaiUiRoot.Instance?.Window;
            if (marketWindow?.IsVisible != true)
            {
                _marketPreviewActive = false;
                if (_marketPreviewBanner != null) _marketPreviewBanner.gameObject.SetActive(false);
                return;
            }
            // 不侵入修改聚宝斋内部按钮状态，只展示非阻断提示条说明材料流通与永久 660 额度边界。
            if (_marketPreviewBanner != null)
            {
                _marketPreviewBanner.gameObject.SetActive(true);
                _marketPreviewBanner.SetAsLastSibling();
            }
        }

        public void OpenEquipment()
        {
            HideWindows();
            GameplayUiRoot.Instance?.OpenEquipment();
        }

        public void OpenPets()
        {
            HideWindows();
            PetUiRoot.Instance?.OpenPanel();
        }

        public void OpenMaterialClaim()
        {
            if (_rankedSettlement != null && _rankedSettlement.IsVisible)
            {
                RankedUiRoot.Instance?.CloseCoreSettlementForMaterialClaim();
                _pendingMaterialClaimFrame = Time.frameCount + 2;
                return;
            }
            if (_battleSettlement != null && _battleSettlement.IsVisible)
            {
                BattleUiRoot.Instance?.FinishCoreSettlementForMaterialClaim();
                _pendingMaterialClaimFrame = Time.frameCount + 2;
                return;
            }
            HideWindows();
            GameplayUiRoot.Instance?.OpenEquipmentMaterials();
        }

        public void OpenAttributeAllocation()
        {
            if (_rankedSettlement != null && _rankedSettlement.IsVisible)
            {
                RankedUiRoot.Instance?.CloseCoreSettlementForMaterialClaim();
                _pendingAttributeAllocFrame = Time.frameCount + 2;
                return;
            }
            if (_battleSettlement != null && _battleSettlement.IsVisible)
            {
                BattleUiRoot.Instance?.FinishCoreSettlementForMaterialClaim();
                _pendingAttributeAllocFrame = Time.frameCount + 2;
                return;
            }
            HideWindows();
            AttributeUiRoot.Instance?.OpenPanel(true);
        }

        public bool ShowBattleSettlement(RectTransform resultPanel)
        {
            return ShowSettlement(ref _battleSettlement, resultPanel,
                "CoreBattleSettlementReward", 40f, 420f, 820f, 170f);
        }

        public bool ShowRankedSettlement(RectTransform resultPanel)
        {
            return ShowSettlement(ref _rankedSettlement, resultPanel,
                "CoreRankedSettlementReward", 964f, 570f, 806f, 259f);
        }

        private bool ShowSettlement(ref CoreSettlementRewardSection section, RectTransform resultPanel,
            string name, float x, float y, float width, float height)
        {
            if (!HasLiveSession || resultPanel == null) return false;
            if (section == null || section.transform.parent != resultPanel)
            {
                DestroyPanel(ref section);
                var rect = QdaoUguiFactory.CreateRect(name, resultPanel, x, y, width, height);
                rect.gameObject.SetActive(false);
                section = rect.gameObject.AddComponent<CoreSettlementRewardSection>();
                section.Initialize(this, _core, _game);
            }
            section.transform.SetAsLastSibling();
            section.Refresh();
            section.Show();
            return true;
        }

        public void HideBattleSettlement() => _battleSettlement?.Hide();
        public void HideRankedSettlement() => _rankedSettlement?.Hide();

        public void OpenRankedDrawer(RankedDrawerTab tab = RankedDrawerTab.Leaderboard)
        {
            if (!CanOpenWorldPanel) return;
            RankedUiRoot.Instance?.OpenDrawer(tab);
        }

        public void OpenAppearance()
        {
            HideWindows();
            AppearanceUiRoot.Instance?.ShowPanel();
        }

        public void OpenTeam()
        {
            HideWindows();
            TeamUiRoot.Instance?.OpenTeamList();
        }

        public void OpenMaterialMarket()
        {
            HideWindows();
            JubaozhaiUiRoot.Instance?.Toggle();
        }

        public void CloseCurrent()
        {
            if (_spendPreview != null && _spendPreview.gameObject.activeSelf)
            {
                _spendPreview.gameObject.SetActive(false);
                SyncWindowLayer();
                return;
            }
            if (_seasonSeal != null && _seasonSeal.IsVisible) { _seasonSeal.Hide(); SyncWindowLayer(); return; }
            if (_wuxingTower != null && _wuxingTower.IsVisible) { _wuxingTower.Hide(); SyncWindowLayer(); return; }
            if (_customDuel != null && _customDuel.IsVisible) { _customDuel.Hide(); SyncWindowLayer(); return; }
            HideWindows();
        }

        private void HideWindows()
        {
            RankedUiRoot.Instance?.CloseDrawerIfOpen();
            _bossTrials?.Hide();
            _formalPvp?.Hide();
            _rotations?.Hide();
            _seasonSeal?.Hide();
            _wuxingTower?.Hide();
            _customDuel?.Hide();
            if (_spendPreview != null) _spendPreview.gameObject.SetActive(false);
            if (_windowLayer != null) _windowLayer.gameObject.SetActive(false);
        }

        private void SyncWindowLayer()
        {
            if (_windowLayer == null) return;
            _windowLayer.gameObject.SetActive((_bossTrials?.IsVisible ?? false) ||
                (_formalPvp?.IsVisible ?? false) || (_rotations?.IsVisible ?? false) ||
                (_seasonSeal?.IsVisible ?? false) || (_wuxingTower?.IsVisible ?? false) ||
                (_customDuel?.IsVisible ?? false) ||
                (_spendPreview != null && _spendPreview.gameObject.activeSelf));
        }

        public void ShowToast(string message)
        {
            if (!HasLiveSession || string.IsNullOrWhiteSpace(message)) return;
            if (_toastDesign == null || _toastText == null) return;
            if (string.Equals(message, _lastToast, StringComparison.Ordinal) &&
                Time.unscaledTime - _lastToastAt < 1f) return;
            _lastToast = message;
            _lastToastAt = Time.unscaledTime;
            _toastText.text = message;
            _toastExpiresAt = Time.unscaledTime + 2.7f;
            _toastDesign.gameObject.SetActive(true);
            _toastDesign.SetAsLastSibling();
        }

        private void HandleEscapeKey()
        {
            if (_windowLayer == null || !_windowLayer.gameObject.activeSelf) return;
            if (RankedUiRoot.Instance?.IsDrawerVisible == true) return;
            bool pressed = false;
#if ENABLE_INPUT_SYSTEM
            pressed = Keyboard.current?.escapeKey.wasPressedThisFrame == true;
#elif ENABLE_LEGACY_INPUT_MANAGER
            pressed = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (pressed) CloseCurrent();
        }

        private void OnDisable() => Unbind();

        private void OnDestroy()
        {
            CityPanelRegistry.Unregister(this);
            Unbind();
            if (Instance == this) Instance = null;
        }
    }
}
