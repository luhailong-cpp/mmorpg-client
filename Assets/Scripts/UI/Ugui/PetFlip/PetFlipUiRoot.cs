using MmorpgClient.Game;
using MmorpgClient.Game.PetFlip;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Role;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.PetFlip
{
    /// <summary>
    /// 灵宠翻牌主城 uGUI 根宿主：
    /// - 自动挂接并向 <see cref="WorldHudNavigation"/> 的「历练」分类注册直接入口“灵宠翻牌”（无红点、不建小游戏大厅）；
    /// - 通过 <see cref="PetFlipClient.AttachView"/> 绑定长寿命规则控制器；面板销毁/重建仅调用 <see cref="PetFlipClient.DetachView"/>，绝不销毁盘面；
    /// - 同步战斗/观战/强制弹窗等 UI 级中断并即时收起面板，绝不暂停联网世界或修改 <c>Time.timeScale</c>；
    /// - 自动适配安全区（含刘海与小游戏右上角胶囊避让）及横屏（4×3）/窄竖屏（3×4）重排。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PetFlipUiRoot : MonoBehaviour
    {
        public static PetFlipUiRoot Instance { get; private set; }

        /// <summary>主画布排序：高于常规主城面板（180–184），低于论剑（188）与战斗层（200）。</summary>
        public const int CanvasSortingOrder = 186;
        public const string EntryName = "PetFlipEntry";

        public PetFlipClient Client => _client;
        public bool IsPanelVisible => _window != null && _window.gameObject.activeSelf;
        /// <summary>供外部强制弹窗宿主或测试直接声明强制弹窗占屏状态。</summary>
        public bool ExternalForcedModalActive { get; set; }

        private GameClient _game;
        private PetFlipClient _client;
        private ulong _viewToken;
        private bool _usingInjectedClient;

        private Canvas _canvas;
        private RectTransform _hud;
        private RectTransform _window;
        private RectTransform _fittedRoot;
        private EquipmentUiFit _fit;
        private Button _entry;
        private PetFlipPanelView _view;

        private PetFlipAssetLoadState _assetState = PetFlipAssetLoadState.NotLoaded;
        private PetFlipResolvedAssets _assets;
        private bool _available;
        private bool _lastPortrait;
        private float _lastCanvasWidth;
        private float _lastCanvasHeight;
        private bool _lastBattleLayerSynced;
        private bool _lastForcedModalSynced;
        private QdaoServerSelectView _cachedServerSelect;
        private float _nextModalLookupUnscaledTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            if (FindAnyObjectByType<TianyongSandboxBootstrap>() != null) return;
            var go = new GameObject("[PetFlipUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<PetFlipUiRoot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
                return;
            }

            Instance = this;
            CityPanelRegistry.Register(this, HidePanel);
            BuildUi();
        }

        private void OnDestroy()
        {
            CityPanelRegistry.Unregister(this);
            _view?.CancelAnimations();
            DetachClientView(pausePanel: true);
            if (Instance == this) Instance = null;
        }

        /// <summary>供自动化测试或独立预览场景直接注入隔离控制器与资源包。</summary>
        public void BindForTesting(PetFlipClient client, PetFlipResolvedAssets assets = null, PetFlipAssetLoadState? overrideAssetState = null)
        {
            DetachClientView(pausePanel: false);
            _usingInjectedClient = client != null;
            _client = client;
            if (_client != null)
                _viewToken = _client.AttachView(HandleSnapshotChanged);

            if (assets != null)
            {
                _assets = assets;
                _assetState = overrideAssetState ?? PetFlipAssetLoadState.Ready;
                _view?.SetAssets(assets);
            }
            else if (overrideAssetState.HasValue)
            {
                _assetState = overrideAssetState.Value;
            }

            if (IsPanelVisible)
                RenderCurrent();
        }

        private void BuildUi()
        {
            var canvasGo = new GameObject("PetFlipCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = CanvasSortingOrder;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var hudDesign = QdaoUguiFactory.CreateCenteredRect("PetFlipHudDesign", canvasGo.transform,
                QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            _hud = QdaoUguiFactory.CreateStretch("PetFlipHudRoot", hudDesign, Vector4.zero);
            QdaoUguiFactory.ConfigureHudCanvas(_hud);
            _entry = GameplayUiArt.Button(_hud, PetFlipUiPresentation.EntryButtonText,
                BattleUiStyle.HudEntryX, BattleUiStyle.HudEntryY(11),
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight,
                TogglePanel, true, fontSize: 32);
            _entry.name = EntryName;
            WorldHudNavigation.Register(WorldHudEntry.PetFlip, (RectTransform)_entry.transform, _entry,
                _entry.GetComponentInChildren<TMP_Text>(true));
            _hud.gameObject.SetActive(false);

            _window = QdaoUguiFactory.CreateStretch("PetFlipWindowBackdrop", canvasGo.transform, Vector4.zero);
            var backdrop = _window.gameObject.AddComponent<Image>();
            backdrop.color = new Color(0.025f, 0.085f, 0.068f, 0.68f);
            backdrop.raycastTarget = true;
            _window.gameObject.AddComponent<GameplayInputBlocker>();

            // 安全区避让容器（小游戏右上角胶囊与异形屏内边距）
            var safeInset = QdaoUguiFactory.CreateStretch("PetFlipSafeInset", _window, new Vector4(16f, 16f, 16f, 24f));

            _fittedRoot = QdaoUguiFactory.CreateCenteredRect("PetFlipFittedRoot", safeInset,
                PetFlipUiPresentation.LandscapeWidth, PetFlipUiPresentation.LandscapeHeight);
            _fit = _fittedRoot.gameObject.AddComponent<EquipmentUiFit>();
            _fit.referenceSize = new Vector2(PetFlipUiPresentation.LandscapeWidth, PetFlipUiPresentation.LandscapeHeight);

            _view = new PetFlipPanelView(_fittedRoot);
            _view.StartClicked += HandleStartClicked;
            _view.PlayAgainClicked += HandlePlayAgainClicked;
            _view.RetryLoadClicked += HandleRetryLoadClicked;
            _view.CloseClicked += HidePanel;
            _view.CardSlotClicked += HandleCardSlotClicked;
            _view.SpiritEyeSlotClicked += HandleSpiritEyeSlotClicked;
            _view.ConfigurationSelected += HandleConfigurationSelected;
            _view.ReturnToEntryClicked += HandleReturnToEntryClicked;

            _window.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_window == null || _hud == null)
            {
                DetachClientView(pausePanel: false);
                return;
            }

            if (!_usingInjectedClient)
                SyncProductionClientBinding();

            bool battleLayer = BattleUiRoot.Instance != null && BattleUiRoot.Instance.IsBattleLayerVisible;
            bool forcedModal = IsForcedModalActive();

            if (_client != null && !_client.IsDisposed)
            {
                if (forcedModal != _lastForcedModalSynced)
                {
                    _lastForcedModalSynced = forcedModal;
                    _client.SetInterrupt(PetFlipInterruptFlags.ForcedModal, forcedModal);
                }

                if (battleLayer != _lastBattleLayerSynced)
                {
                    _lastBattleLayerSynced = battleLayer;
                    _client.SetInterrupt(PetFlipInterruptFlags.Battle, battleLayer);
                }
            }

            PetFlipSnapshot snapshot = _client != null ? _client.GetSnapshot() : null;
            bool sessionReady = snapshot != null && (snapshot.HasActiveSession || snapshot.IsDisconnected);
            _available = sessionReady && !battleLayer && !forcedModal && (_client != null && !_client.IsDisposed);

            if (_hud.gameObject.activeSelf != _available)
                _hud.gameObject.SetActive(_available);

            if (!_available && IsPanelVisible)
            {
                CollapseFromInterrupt();
                return;
            }

            if (!IsPanelVisible)
                return;

            if (snapshot != null && snapshot.ShouldCollapsePanel)
            {
                CollapseFromInterrupt();
                return;
            }

            SyncOrientationIfNeeded();
            HandleEscapeShortcut();
        }

        public void TogglePanel()
        {
            if (IsPanelVisible) HidePanel();
            else ShowPanel();
        }

        /// <summary>
        /// 打开灵宠翻牌界面：
        /// - 若尚未加载或上次加载失败，重新校验 6 张灵宠头像与核心框体；
        /// - 若已有内存盘，直接调用 <see cref="PetFlipClient.StartOrResume"/> 续盘，不强迫重看说明页；
        /// - 若暂无盘，进入入口说明页等待玩家点击“开始翻牌”。
        /// </summary>
        public void ShowPanel()
        {
            if (_window == null || _client == null || _client.IsDisposed)
                return;

            bool battleLayer = BattleUiRoot.Instance != null && BattleUiRoot.Instance.IsBattleLayerVisible;
            if (battleLayer || IsForcedModalActive())
                return;

            PetFlipSnapshot preOpen = _client.GetSnapshot();
            if (!preOpen.HasActiveSession)
                return;

            if ((preOpen.ActiveInterrupts & PetFlipConstants.EntryBlockedMask) != 0)
                return;

            CityPanelRegistry.HideOthers(this);
            EnsureAssetsLoaded();

            _window.gameObject.SetActive(true);
            SyncOrientationIfNeeded(force: true);

            // 有未完成或已完成内存盘且资源就绪时，直接续盘，跳过说明页。
            if (_assetState == PetFlipAssetLoadState.Ready && preOpen.HasBoard)
            {
                _client.StartOrResume(_viewToken);
            }
            else if (_assetState == PetFlipAssetLoadState.Ready && (preOpen.ActiveInterrupts & PetFlipInterruptFlags.PanelClosed) != 0)
            {
                _client.Resume(PetFlipInterruptFlags.PanelClosed, _viewToken);
            }

            RenderCurrent();
        }

        /// <summary>玩家主动关闭或被主城互斥面板收起：取消动画、暂停面板，保留同角色进程内盘面。</summary>
        public void HidePanel()
        {
            _view?.CancelAnimations();
            if (_window != null && _window.gameObject.activeSelf)
                _window.gameObject.SetActive(false);

            if (_client != null && !_client.IsDisposed && _viewToken != 0)
                _client.Pause(PetFlipInterruptFlags.PanelClosed, _viewToken);
        }

        private void CollapseFromInterrupt()
        {
            _view?.CancelAnimations();
            if (_window != null && _window.gameObject.activeSelf)
                _window.gameObject.SetActive(false);

            // 高优先中断收起后同时标记 PanelClosed，避免战斗或强制弹窗结束后未经点击自动弹出。
            if (_client != null && !_client.IsDisposed && _viewToken != 0)
            {
                PetFlipSnapshot snap = _client.GetSnapshot();
                if (snap.HasBoard && (snap.ActiveInterrupts & PetFlipInterruptFlags.PanelClosed) == 0)
                    _client.Pause(PetFlipInterruptFlags.PanelClosed, _viewToken);
            }
        }

        private void HandleStartClicked()
        {
            if (_client == null || _assetState != PetFlipAssetLoadState.Ready)
                return;

            PetFlipActionResult result = _client.StartOrResume(_viewToken);
            if (!result.Accepted)
                RenderCurrent();
        }

        private void HandlePlayAgainClicked()
        {
            if (_client == null || _assetState != PetFlipAssetLoadState.Ready)
                return;

            ulong roundId = _client.GetSnapshot().RoundId;
            PetFlipActionResult result = _client.StartNewRound(expectedRoundId: roundId, viewToken: _viewToken);
            if (!result.Accepted)
                RenderCurrent();
        }

        private void HandleRetryLoadClicked()
        {
            _assetState = PetFlipAssetLoadState.Loading;
            RenderCurrent();
            EnsureAssetsLoaded(forceReload: true);

            if (_assetState == PetFlipAssetLoadState.Ready && _client != null && _client.GetSnapshot().HasBoard)
                _client.StartOrResume(_viewToken);

            RenderCurrent();
        }

        private void HandleCardSlotClicked(int slotIndex)
        {
            if (_client == null || _assetState != PetFlipAssetLoadState.Ready)
                return;

            ulong currentRoundId = _client.GetSnapshot().RoundId;
            PetFlipActionResult result = _client.FlipCard(slotIndex, expectedRoundId: currentRoundId, viewToken: _viewToken);
            if (result.Accepted)
                _view?.PlayVisualFeedback(result);
        }

        private void HandleSpiritEyeSlotClicked(int slotIndex)
        {
            if (_client == null || _assetState != PetFlipAssetLoadState.Ready)
                return;

            ulong currentRoundId = _client.GetSnapshot().RoundId;
            PetFlipActionResult result = _client.UseSpiritEyePeek(slotIndex, expectedRoundId: currentRoundId, viewToken: _viewToken);
            if (result.Accepted)
                _view?.PlayVisualFeedback(result);
            else
                RenderCurrent();
        }

        private void HandleConfigurationSelected(PetFlipDifficulty difficulty, PetFlipGameMode mode)
        {
            if (_client == null || _assetState != PetFlipAssetLoadState.Ready)
                return;

            PetFlipActionResult result = _client.SelectConfiguration(difficulty, mode, viewToken: _viewToken);
            if (!result.Accepted)
                RenderCurrent();
        }

        private void HandleReturnToEntryClicked()
        {
            if (_client == null || _assetState != PetFlipAssetLoadState.Ready)
                return;

            PetFlipActionResult result = _client.ReturnToEntryAfterCompletion(viewToken: _viewToken);
            if (!result.Accepted)
                RenderCurrent();
        }

        private void HandleSnapshotChanged(PetFlipSnapshot snapshot)
        {
            if (this == null || _window == null)
                return;

            if (!IsPanelVisible)
                return;

            if (snapshot.ShouldCollapsePanel)
            {
                CollapseFromInterrupt();
                return;
            }

            _view?.Render(snapshot, _assetState);
        }

        private void EnsureAssetsLoaded(bool forceReload = false)
        {
            if (!forceReload && _assetState == PetFlipAssetLoadState.Ready && _assets != null)
                return;

            _assetState = PetFlipAssetLoadState.Loading;
            if (PetFlipArtCatalog.TryLoadAndValidate(out PetFlipResolvedAssets loaded, out string reason))
            {
                _assets = loaded;
                _assetState = PetFlipAssetLoadState.Ready;
                _view?.SetAssets(_assets);
            }
            else
            {
                _assets = null;
                _assetState = PetFlipAssetLoadState.Failed;
                Debug.LogWarning($"[pet-flip-ui] asset load validation failed: {reason}");
            }
        }

        private void SyncProductionClientBinding()
        {
            GameClient game = AppBootstrap.Instance != null ? AppBootstrap.Instance.GameClient : null;
            PetFlipClient target = game != null ? game.PetFlip : PetFlipClient.Instance;
            if (ReferenceEquals(target, _client) && ReferenceEquals(game, _game))
                return;

            DetachClientView(pausePanel: false);
            _game = game;
            _client = target;
            if (_client != null && !_client.IsDisposed)
                _viewToken = _client.AttachView(HandleSnapshotChanged);
        }

        private void DetachClientView(bool pausePanel)
        {
            if (_client != null && _viewToken != 0)
                _client.DetachView(_viewToken, pausePanel);
            _viewToken = 0;
        }

        private void RenderCurrent()
        {
            if (_view == null) return;
            PetFlipSnapshot snapshot = _client != null ? _client.GetSnapshot() : null;
            _view.Render(snapshot, _assetState);
        }

        private void SyncOrientationIfNeeded(bool force = false)
        {
            if (_canvas == null || _fit == null || _view == null)
                return;

            Rect pixelRect = _canvas.pixelRect;
            float w = pixelRect.width;
            float h = pixelRect.height;
            if (w <= 0f || h <= 0f)
                return;

            bool portrait = PetFlipUiPresentation.IsPortraitOrientation(w, h);
            if (!force && portrait == _lastPortrait && Mathf.Approximately(w, _lastCanvasWidth) && Mathf.Approximately(h, _lastCanvasHeight))
                return;

            _lastPortrait = portrait;
            _lastCanvasWidth = w;
            _lastCanvasHeight = h;

            _fit.referenceSize = portrait
                ? new Vector2(PetFlipUiPresentation.PortraitWidth, PetFlipUiPresentation.PortraitHeight)
                : new Vector2(PetFlipUiPresentation.LandscapeWidth, PetFlipUiPresentation.LandscapeHeight);
            _view.ApplyOrientationLayout(portrait);
            _fit.Fit();
            RenderCurrent();
        }

        private bool IsForcedModalActive()
        {
            if (ExternalForcedModalActive)
                return true;

            if (_cachedServerSelect == null && Time.unscaledTime >= _nextModalLookupUnscaledTime)
            {
                _nextModalLookupUnscaledTime = Time.unscaledTime + 1f;
                _cachedServerSelect = FindAnyObjectByType<QdaoServerSelectView>(FindObjectsInactive.Include);
            }
            if (_cachedServerSelect != null && _cachedServerSelect.gameObject.activeInHierarchy)
                return true;

            if (RoleFlowUi.Instance != null && RoleFlowUi.Instance.isActiveAndEnabled)
            {
                var canvas = RoleFlowUi.Instance.GetComponentInChildren<Canvas>(false);
                if (canvas != null && canvas.gameObject.activeInHierarchy)
                    return true;
            }

            return false;
        }

        private void HandleEscapeShortcut()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                HidePanel();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape))
                HidePanel();
#endif
        }
    }
}
