using System;
using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.FestivalBeast;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Pet;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.FestivalBeast
{
    /// <summary>
    /// 瑞兽迎春独立 UI 根容器（[RuntimeInitializeOnLoadMethod] 自动挂载，零场景改动）。
    /// 负责安全区适配（SafeArea）、CityPanelRegistry 互斥、GameClient.FestivalBeast 事件订阅、
    /// 主面板 / 规则弹窗 / 战斗重击预告条 / 结算嵌入块的生命周期管理。
    /// 关闭界面绝不清在途请求、不放弃资格、也不等于逃跑。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FestivalBeastUiRoot : MonoBehaviour
    {
        public static FestivalBeastUiRoot Instance { get; private set; }

        private GameClient _game;
        private FestivalBeastClient _client;
        private ulong _playerId;
        private object _connectionIdentity;

        private Canvas _canvas;
        private RectTransform _safeRoot;
        private RectTransform _windowLayer;
        private RectTransform _windowDesign;
        private RectTransform _battleDesign;
        private Rect _lastSafePixels;
        private Rect _lastCanvasPixels;

        private FestivalBeastMainPanel _mainPanel;
        private FestivalBeastRulesDialog _rulesDialog;
        private FestivalBeastBattleOverlay _battleOverlay;
        private FestivalBeastSettlementSection _settlementSection;
        private FestivalBeastSettlementSnapshot _activeSettlement;
        private float _nextCountdownRefreshAt;
        private int _pendingReturnActionFrame = -1;
        private int _pendingReturnActionKind; // 1=open activity, 2=open material claim

        public bool IsWindowVisible => _windowLayer != null && _windowLayer.gameObject.activeSelf;

        /// <summary>当前战斗是否为瑞兽迎春战局（供 BattleScreen 显示「第 N/12 回合」）。</summary>
        public bool IsActiveFestivalBeastBattle
        {
            get
            {
                if (_game == null || _client == null) return false;
                var state = _game.Battle?.State;
                if (state != null)
                {
                    for (int i = 0; i < state.Telegraphs.Count; ++i)
                        if (state.Telegraphs[i].IntentKind == FestivalBeastModels.TelegraphKindFestivalHeavyStrike)
                            return true;
                    for (int i = 0; i < state.LastResolvedTelegraphs.Count; ++i)
                        if (state.LastResolvedTelegraphs[i].IntentKind == FestivalBeastModels.TelegraphKindFestivalHeavyStrike)
                            return true;
                }
                if (_client.ActiveTelegraph != null || _client.LastResolvedTelegraph != null) return true;
                var pending = _client.Detail?.PendingLaunch;
                ulong currentBattleId = state?.BattleId ?? 0UL;
                return pending != null && pending.BattleId != 0UL && pending.BattleId == currentBattleId;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn() => EnsureSpawned();

        public static void EnsureSpawned()
        {
            if (Instance != null) return;
            var host = new GameObject("[FestivalBeastUi]");
            DontDestroyOnLoad(host);
            host.AddComponent<FestivalBeastUiRoot>();
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
            var game = AppBootstrap.Instance?.GameClient;
            bool ready = game != null && game.InGame && game.IsGateReady && game.PlayerId != 0 && game.FestivalBeast != null;
            if (!ready)
            {
                if (_client != null) Unbind();
                return;
            }

            if (!ReferenceEquals(game, _game) || game.PlayerId != _playerId || !ReferenceEquals(game.GateConnectionIdentity, _connectionIdentity))
            {
                Bind(game);
            }

            SyncBattleOverlay();

            if (_pendingReturnActionFrame >= 0 && Time.frameCount >= _pendingReturnActionFrame
                && !(BattleUiRoot.Instance?.IsBattleLayerVisible ?? false))
            {
                int action = _pendingReturnActionKind;
                _pendingReturnActionFrame = -1;
                _pendingReturnActionKind = 0;
                if (action == 1) OpenMainWindow();
                else if (action == 2) GameplayUiRoot.Instance?.OpenEquipmentMaterials();
            }

            if (IsWindowVisible && _mainPanel != null && _mainPanel.IsVisible && Time.unscaledTime >= _nextCountdownRefreshAt)
            {
                _nextCountdownRefreshAt = Time.unscaledTime + 1f;
                RefreshMainPanel();
            }

            HandleEscapeKey();
        }

        public void OpenMainWindow()
        {
            if (_game == null || _client == null || !_game.InGame || !_game.IsGateReady) return;
            if (BattleUiRoot.Instance?.IsBattleLayerVisible ?? false) return;

            CityPanelRegistry.HideOthers(this);
            HideWindows();
            _windowLayer.gameObject.SetActive(true);
            _client.RefreshDetail(force: true);
            if (_game.Pets != null && !_game.Pets.HasList) _game.Pets.RequestList();
            _mainPanel.Show(BuildCurrentMainPanelViewModel());
            _nextCountdownRefreshAt = Time.unscaledTime + 1f;
        }

        public void OpenRules()
        {
            if (_windowLayer == null || _rulesDialog == null) return;
            _windowLayer.gameObject.SetActive(true);
            _rulesDialog.Show();
            _rulesDialog.transform.SetAsLastSibling();
        }

        public void CloseRules()
        {
            _rulesDialog?.Hide();
            if (_mainPanel == null || !_mainPanel.IsVisible)
            {
                if (_windowLayer != null) _windowLayer.gameObject.SetActive(false);
            }
        }

        public void HideWindows()
        {
            _rulesDialog?.Hide();
            _mainPanel?.Hide();
            if (_windowLayer != null) _windowLayer.gameObject.SetActive(false);
        }

        /// <summary>在既有 BattleResultPanel 内展示瑞兽迎春结算区块（支持现场与离线补查）。</summary>
        public bool ShowBattleSettlement(RectTransform resultPanel, BattleEndS2C end)
        {
            var snap = FestivalBeastModels.ExtractSettlement(end);
            if (snap == null) return false;
            return ShowSettlementInternal(resultPanel, snap);
        }

        public bool ShowRecoveredBattleSettlement(RectTransform resultPanel, PlayerBattleResult result)
        {
            var snap = FestivalBeastModels.ExtractSettlement(result);
            if (snap == null) return false;
            return ShowSettlementInternal(resultPanel, snap);
        }

        public void HideBattleSettlement()
        {
            _activeSettlement = null;
            _settlementSection?.Hide();
        }

        private bool ShowSettlementInternal(RectTransform resultPanel, FestivalBeastSettlementSnapshot snap)
        {
            if (_game == null || _client == null || resultPanel == null) return false;
            _activeSettlement = snap;
            if (_settlementSection == null || _settlementSection.transform.parent != resultPanel)
            {
                if (_settlementSection != null) Destroy(_settlementSection.gameObject);
                var rect = QdaoUguiFactory.CreateRect("FestivalBeastSettlement", resultPanel, 40f, 416f, 820f, 176f);
                rect.gameObject.SetActive(false);
                _settlementSection = rect.gameObject.AddComponent<FestivalBeastSettlementSection>();
                _settlementSection.Configure(OnClaimFromSettlement, OnReturnActivityFromSettlement, OnOpenMaterialFromSettlement);
            }
            _settlementSection.transform.SetAsLastSibling();
            RefreshSettlementSection();
            return true;
        }

        private void BuildCanvas()
        {
            var canvasObj = new GameObject("FestivalBeastCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObj.transform.SetParent(transform, false);
            _canvas = canvasObj.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 215;

            var scaler = canvasObj.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            _safeRoot = QdaoUguiFactory.CreateStretch("FestivalSafeArea", canvasObj.transform, Vector4.zero);

            _windowLayer = QdaoUguiFactory.CreateStretch("FestivalWindowLayer", _safeRoot, Vector4.zero);
            var shade = _windowLayer.gameObject.AddComponent<Image>();
            shade.color = new Color(0.02f, 0.08f, 0.06f, 0.58f);
            shade.raycastTarget = true;
            _windowDesign = QdaoUguiFactory.CreateCenteredRect("FestivalWindowDesign", _windowLayer, 2560f, 1080f);
            AddFit(_windowDesign);
            _windowLayer.gameObject.SetActive(false);

            var mainObj = QdaoUguiFactory.CreateRect("FestivalBeastMainPanel", _windowDesign, 0f, 0f, 2560f, 1080f);
            _mainPanel = mainObj.gameObject.AddComponent<FestivalBeastMainPanel>();
            _mainPanel.Configure(
                HideWindows,
                OpenRules,
                () => _client?.RefreshDetail(force: true),
                OnPrimaryActionClicked,
                OpenPets,
                OpenMaterialClaim,
                OpenAppearance);
            _mainPanel.Hide();

            var rulesObj = QdaoUguiFactory.CreateRect("FestivalBeastRulesDialog", _windowDesign, 0f, 0f, 2560f, 1080f);
            _rulesDialog = rulesObj.gameObject.AddComponent<FestivalBeastRulesDialog>();
            _rulesDialog.Configure(CloseRules);
            _rulesDialog.Hide();

            _battleDesign = QdaoUguiFactory.CreateCenteredRect("FestivalBattleDesign", _safeRoot, 2560f, 1080f);
            AddFit(_battleDesign);
            var battleObj = QdaoUguiFactory.CreateRect("FestivalBeastBattleOverlay", _battleDesign, 0f, 0f, 2560f, 1080f);
            _battleOverlay = battleObj.gameObject.AddComponent<FestivalBeastBattleOverlay>();
            _battleOverlay.Hide();
            _battleDesign.gameObject.SetActive(false);
        }

        private void Bind(GameClient game)
        {
            Unbind();
            _game = game;
            _client = game.FestivalBeast;
            _playerId = game.PlayerId;
            _connectionIdentity = game.GateConnectionIdentity;

            _client.OnChanged += HandleClientChanged;
            _client.OnTelegraphChanged += HandleTelegraphChanged;
            _client.OnRewardClaimed += HandleRewardClaimed;
            _client.OnChallengeAdmitted += HandleChallengeAdmitted;
            if (_game.Pets != null) _game.Pets.OnList += HandlePetsChanged;
            if (_game.Battle != null)
            {
                _game.Battle.OnBattleStart += HandleBattleChanged;
                _game.Battle.OnTurnResult += HandleTurnChanged;
                _game.Battle.OnBattleEnd += HandleBattleEnded;
            }
        }

        private void Unbind()
        {
            if (_client != null)
            {
                _client.OnChanged -= HandleClientChanged;
                _client.OnTelegraphChanged -= HandleTelegraphChanged;
                _client.OnRewardClaimed -= HandleRewardClaimed;
                _client.OnChallengeAdmitted -= HandleChallengeAdmitted;
            }
            if (_game?.Pets != null) _game.Pets.OnList -= HandlePetsChanged;
            if (_game?.Battle != null)
            {
                _game.Battle.OnBattleStart -= HandleBattleChanged;
                _game.Battle.OnTurnResult -= HandleTurnChanged;
                _game.Battle.OnBattleEnd -= HandleBattleEnded;
            }
            _client = null;
            _game = null;
            _playerId = 0UL;
            _connectionIdentity = null;
            HideWindows();
            HideBattleSettlement();
            _battleOverlay?.Hide();
            if (_battleDesign != null) _battleDesign.gameObject.SetActive(false);
        }

        private void HandleClientChanged()
        {
            if (IsWindowVisible && _mainPanel != null && _mainPanel.IsVisible) RefreshMainPanel();
            if (_settlementSection != null && _settlementSection.IsVisible) RefreshSettlementSection();
        }

        private void HandleTelegraphChanged() => SyncBattleOverlay();
        private void HandlePetsChanged(global::PetListInfo _)
        {
            if (IsWindowVisible && _mainPanel != null && _mainPanel.IsVisible) RefreshMainPanel();
        }

        private void HandleBattleChanged(global::BattleStartS2C _) => SyncBattleOverlay();
        private void HandleTurnChanged(global::TurnResultS2C _) => SyncBattleOverlay();
        private void HandleBattleEnded(global::BattleEndS2C _)
        {
            SyncBattleOverlay();
            if (_settlementSection != null && _settlementSection.IsVisible) RefreshSettlementSection();
        }

        private void HandleChallengeAdmitted(FestivalBeastLaunchEvent ev)
        {
            HideWindows();
            BattleUiRoot.Instance?.ShowToast("已进入瑞兽迎春挑战！");
        }

        private void HandleRewardClaimed(FestivalBeastRewardClaimedEvent ev)
        {
            // 仅在 durable=true 时收到此事件；通知宠物列表刷新并提示（绝不自动顶替当前出战宝宝）。
            _game?.Pets?.RequestList();
            BattleUiRoot.Instance?.ShowToast($"已获得春禧兽（编号 {ev.PetIdText}），已放入专属赠送位！");
            RefreshMainPanel();
            if (_settlementSection != null && _settlementSection.IsVisible) RefreshSettlementSection();
        }

        private void OnPrimaryActionClicked()
        {
            if (_client == null) return;
            var vm = BuildCurrentMainPanelViewModel();
            if (vm.PrimaryButtonKind == FestivalBeastPrimaryButtonKind.ClaimReward)
            {
                _client.ClaimReward();
                return;
            }
            if (vm.PrimaryButtonKind == FestivalBeastPrimaryButtonKind.StartChallenge
                || vm.PrimaryButtonKind == FestivalBeastPrimaryButtonKind.ContinueChallenge)
            {
                _client.StartChallenge();
            }
        }

        private void OnClaimFromSettlement()
        {
            _client?.ClaimReward();
            RefreshSettlementSection();
        }

        private void OnReturnActivityFromSettlement()
        {
            HideBattleSettlement();
            BattleUiRoot.Instance?.FinishCoreSettlementForMaterialClaim();
            _pendingReturnActionKind = 1;
            _pendingReturnActionFrame = Time.frameCount + 2;
        }

        private void OnOpenMaterialFromSettlement()
        {
            HideBattleSettlement();
            BattleUiRoot.Instance?.FinishCoreSettlementForMaterialClaim();
            _pendingReturnActionKind = 2;
            _pendingReturnActionFrame = Time.frameCount + 2;
        }

        private void OpenPets()
        {
            HideWindows();
            PetUiRoot.EnsureSpawned();
            PetUiRoot.Instance?.OpenPanel();
        }

        private void OpenMaterialClaim()
        {
            HideWindows();
            GameplayUiRoot.Instance?.OpenEquipmentMaterials();
        }

        private void OpenAppearance()
        {
            HideWindows();
            AppearanceUiRoot.Instance?.ShowPanel();
        }

        private void RefreshMainPanel()
        {
            if (_mainPanel == null || !_mainPanel.IsVisible) return;
            _mainPanel.Render(BuildCurrentMainPanelViewModel());
        }

        private void RefreshSettlementSection()
        {
            if (_settlementSection == null || _activeSettlement == null || _client == null) return;
            var vm = FestivalBeastUiState.BuildSettlement(
                _activeSettlement, _client.Detail, _client.ClaimBusy, _client.HasPendingClaim,
                _client.Detail?.BossName ?? FestivalBeastUiState.DefaultBossName);
            _settlementSection.Show(vm);
        }

        private void SyncBattleOverlay()
        {
            if (_battleDesign == null || _battleOverlay == null || _game == null || _client == null) return;
            bool battleLayer = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            var phase = _game.Battle?.Phase ?? BattlePhase.None;
            bool inActiveBattle = battleLayer && IsActiveFestivalBeastBattle
                && (phase == BattlePhase.Preparing || phase == BattlePhase.WaitingAction || phase == BattlePhase.Resolving);

            if (!inActiveBattle)
            {
                if (_battleOverlay.IsVisible) _battleOverlay.Hide();
                if (_battleDesign.gameObject.activeSelf) _battleDesign.gameObject.SetActive(false);
                return;
            }

            if (IsWindowVisible) HideWindows();
            if (!_battleDesign.gameObject.activeSelf) _battleDesign.gameObject.SetActive(true);
            if (!_battleOverlay.IsVisible) _battleOverlay.Show();

            var state = _game.Battle?.State;
            uint round = state?.RoundIndex ?? 1u;
            uint limit = _client.Detail?.RoundLimit ?? 12u;
            var vm = FestivalBeastUiState.BuildBattleHud(
                round,
                limit,
                _client.ActiveTelegraph,
                _client.LastResolvedTelegraph,
                ResolveBattleActorName,
                _client.Detail?.BossName ?? FestivalBeastUiState.DefaultBossName);
            _battleOverlay.Render(vm);
        }

        private string ResolveBattleActorName(ulong actorId)
        {
            if (actorId == 0UL || _game?.Battle?.State == null) return string.Empty;
            var actors = _game.Battle.State.Actors;
            for (int i = 0; i < actors.Count; ++i)
            {
                var a = actors[i];
                if (a.ActorId != actorId) continue;
                if (!string.IsNullOrWhiteSpace(a.Name)) return a.Name;
                return a.ActorType == global::eBattleActorType.BattleActorTypePet ? "出战宝宝" : "主角";
            }
            return string.Empty;
        }

        private FestivalBeastMainPanelViewModel BuildCurrentMainPanelViewModel()
        {
            var detail = _client?.Detail;
            ulong targetMs = detail == null ? 0UL : detail.Phase switch
            {
                global::FestivalBeastPhase.Upcoming => detail.EffectiveStartAtMs,
                global::FestivalBeastPhase.Open => detail.EffectiveEndAtMs,
                global::FestivalBeastPhase.ClaimShowcase => detail.ClaimShowcaseEndAtMs,
                _ => 0UL,
            };
            ulong remainingMs = targetMs != 0UL && _client != null ? _client.GetRemainingMs(targetMs) : 0UL;
            ResolveActivePetInfo(out string petName, out bool hasPet, out bool petAlive);
            return FestivalBeastUiState.BuildMainPanel(
                detail,
                _client?.DetailLoading ?? false,
                _client?.LaunchBusy ?? false,
                _client?.ClaimBusy ?? false,
                _client?.HasPendingLaunch ?? false,
                _client?.HasPendingClaim ?? false,
                _client?.StatusMessage,
                _client?.StatusIsError ?? false,
                remainingMs,
                petName,
                hasPet,
                petAlive);
        }

        private void ResolveActivePetInfo(out string petName, out bool hasPet, out bool petAlive)
        {
            petName = string.Empty;
            hasPet = false;
            petAlive = false;
            var list = _game?.Pets?.Pets;
            if (list == null || list.ActivePetId == 0UL) return;
            for (int i = 0; i < list.Pets.Count; ++i)
            {
                var p = list.Pets[i];
                if (p.PetId != list.ActivePetId) continue;
                hasPet = true;
                petName = !string.IsNullOrWhiteSpace(p.Name) ? p.Name : "已出战宝宝";
                bool derivedAlive = p.Derived == null || p.Derived.Health > 0UL;
                petAlive = derivedAlive && (_client?.Detail?.StartBlockCode != global::FestivalBeastActionBlockCode.FestivalBeastBlockPetDead);
                return;
            }
        }

        private void ApplySafeArea()
        {
            if (_safeRoot == null || _canvas == null) return;
            var safe = Screen.safeArea;
            var pixelRect = _canvas.pixelRect;
            if (safe == _lastSafePixels && pixelRect == _lastCanvasPixels) return;
            _lastSafePixels = safe;
            _lastCanvasPixels = pixelRect;
            if (pixelRect.width <= 0f || pixelRect.height <= 0f) return;

            var min = new Vector2(
                Mathf.Clamp01((safe.xMin - pixelRect.xMin) / pixelRect.width),
                Mathf.Clamp01((safe.yMin - pixelRect.yMin) / pixelRect.height));
            var max = new Vector2(
                Mathf.Clamp01((safe.xMax - pixelRect.xMin) / pixelRect.width),
                Mathf.Clamp01((safe.yMax - pixelRect.yMin) / pixelRect.height));
            _safeRoot.anchorMin = min;
            _safeRoot.anchorMax = max;
            _safeRoot.offsetMin = Vector2.zero;
            _safeRoot.offsetMax = Vector2.zero;
        }

        private static void AddFit(RectTransform rect)
        {
            if (rect == null) return;
            var fit = rect.gameObject.GetComponent<GameplayDesignFit>() ?? rect.gameObject.AddComponent<GameplayDesignFit>();
            fit.enabled = true;
        }

        private void HandleEscapeKey()
        {
            if (!IsWindowVisible) return;
            bool pressed = false;
#if ENABLE_INPUT_SYSTEM
            pressed = Keyboard.current?.escapeKey.wasPressedThisFrame == true;
#elif ENABLE_LEGACY_INPUT_MANAGER
            pressed = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (!pressed) return;
            if (_rulesDialog != null && _rulesDialog.IsVisible) CloseRules();
            else HideWindows();
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
