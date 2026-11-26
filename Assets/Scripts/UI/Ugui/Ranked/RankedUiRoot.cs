using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Rank;
using MmorpgClient.Game.Ranked;
using MmorpgClient.Game.Team;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.CoreLoop;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Team;
using MmorpgClient.UI.Ugui.TrialJournal;
using MmorpgClient.World.Tianyong;
using Rankpb;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.Ranked
{
    using Transform = UnityEngine.Transform;
    /// <summary>
    /// 论剑排位首发 V1 总 UI 根：
    /// - 自动挂载并向 WorldHudNavigation 注册主城「论剑」入口；
    /// - 管理 1920x1080 安全区自适应画布，承载大厅面板、共享抽屉（排行榜/赛季记录/已获外观）与单局结算面板；
    /// - 接管 BattleUiRoot 的排位终局与重登补弹结果，确保所有权威状态（积分/场次/展示权益/日常材料额度/10元易容入口）
    ///   均直接绑定真实服务端 RPC，无任何本地假数据。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RankedUiRoot : MonoBehaviour
    {
        public static RankedUiRoot Instance { get; private set; }
        public const int CanvasSortingOrder = 188;
        private const int DrawerOverlaySortingOrder = 195;

        public bool IsLobbyVisible => CoreGameplayUiRoot.Instance?.IsFormalPvpVisible ?? false;
        public bool IsDrawerVisible => _drawer != null && _drawer.IsVisible;
        public bool IsSettlementVisible => _settlement != null && _settlement.IsVisible;
        public RankedClient Client => _ranked;

        private GameClient _game;
        private RankedClient _ranked;
        private RankClient _rankClient;
        private ulong _playerId;
        private bool _available;
        private bool _settlementFromRecovery;
        private bool _waitingForSettlementRequeue;
        private uint _pendingRequeueSize;

        private Canvas _canvas;
        private RectTransform _hud;
        private RectTransform _window;
        private RectTransform _windowSafeRoot;
        private RectTransform _fittedRoot;
        private Rect _lastSafePixels;
        private Rect _lastCanvasPixels;
        private Button _entry;

        private RankedLobbyPanel _lobby;
        private RankedSharedDrawer _drawer;
        private RankedSettlementPanel _settlement;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var go = new GameObject("[RankedUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<RankedUiRoot>();
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
            CityPanelRegistry.Register(this, HideLobbyOnly);
            BattleUiRoot.InterceptRankedBattleEnd = HandleInterceptedBattleEnd;
            BattleUiRoot.InterceptRankedRecoveredResult = HandleInterceptedRecoveredResult;
            Build();
        }

        private void OnDestroy()
        {
            UnbindClients();
            if (BattleUiRoot.InterceptRankedBattleEnd == HandleInterceptedBattleEnd)
                BattleUiRoot.InterceptRankedBattleEnd = null;
            if (BattleUiRoot.InterceptRankedRecoveredResult == HandleInterceptedRecoveredResult)
                BattleUiRoot.InterceptRankedRecoveredResult = null;
            if (Instance == this) Instance = null;
        }

        private void Build()
        {
            var canvasGo = new GameObject("RankedCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = CanvasSortingOrder;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RankedArtCatalog.DesignWidth, RankedArtCatalog.DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var hudDesign = QdaoUguiFactory.CreateCenteredRect("RankedHudDesign", canvasGo.transform, 2560f, 1080f);
            _hud = QdaoUguiFactory.CreateStretch("RankedHudRoot", hudDesign, Vector4.zero);
            QdaoUguiFactory.ConfigureHudCanvas(_hud);
            _entry = GameplayUiArt.Button(_hud, "论剑", BattleUiStyle.HudEntryX, BattleUiStyle.HudEntryY(10),
                BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight, ToggleLobby, true, fontSize: 32);
            _entry.name = "RankedEntry";
            WorldHudNavigation.Register(WorldHudEntry.Ranked, (RectTransform)_entry.transform, _entry,
                _entry.GetComponentInChildren<TMP_Text>(true));
            _hud.gameObject.SetActive(false);

            _window = QdaoUguiFactory.CreateStretch("RankedWindowBackdrop", canvasGo.transform, Vector4.zero);
            var dim = _window.gameObject.AddComponent<Image>();
            dim.color = new Color(0.03f, 0.06f, 0.05f, 0.62f);
            dim.raycastTarget = true;
            _window.gameObject.AddComponent<GameplayInputBlocker>();

            _windowSafeRoot = QdaoUguiFactory.CreateStretch("RankedWindowSafeArea", _window, Vector4.zero);
            _fittedRoot = QdaoUguiFactory.CreateCenteredRect("RankedFittedRoot", _windowSafeRoot,
                RankedArtCatalog.DesignWidth, RankedArtCatalog.DesignHeight);
            var fit = _fittedRoot.gameObject.AddComponent<EquipmentUiFit>();
            fit.referenceSize = new Vector2(RankedArtCatalog.DesignWidth, RankedArtCatalog.DesignHeight);

            _lobby = new RankedLobbyPanel(_fittedRoot);
            _drawer = new RankedSharedDrawer(_fittedRoot);
            _settlement = new RankedSettlementPanel(_fittedRoot);

            WirePanelEvents();
            ApplySafeArea();
            _window.gameObject.SetActive(false);
        }

        private void WirePanelEvents()
        {
            _lobby.CloseRequested += HideAll;
            _lobby.AppearanceEntryRequested += () =>
            {
                HideLobbyOnly();
                AppearanceUiRoot.Instance?.ShowPanel();
            };
            _lobby.DrawerRequested += tab =>
            {
                _drawer.Show(tab);
                RefreshDrawer();
            };
            _lobby.OpenTeamRequested += () =>
            {
                HideLobbyOnly();
                TeamUiRoot.Instance?.OpenTeamList();
            };
            _lobby.TeamSizeSelected += size =>
            {
                if (_ranked != null) _ranked.SelectedTeamSize = size;
                RenderAll();
            };
            _lobby.PresetSelected += (charId, petId) =>
            {
                if (_ranked == null) return;
                uint ruleVer = _ranked.Profile?.RuleVersion ?? 1u;
                _ranked.SavePreset(charId, petId, ruleVer);
                var team = TeamUiRoot.Instance?.Client;
                if (team != null && team.HasTeam)
                    team.ConfirmRankedPreset(charId, petId, ruleVer);
                RenderAll();
            };
            _lobby.TeamConfirmPresetRequested += () =>
            {
                var team = TeamUiRoot.Instance?.Client;
                if (team == null || !team.HasTeam) return;
                uint ruleVer = _ranked?.Profile?.RuleVersion ?? team.Snapshot.RankedRuleVersion;
                team.ConfirmRankedPreset(_lobby.SelectedCharacterPresetId, _lobby.SelectedPetPresetId, ruleVer);
                RenderAll();
            };
            _lobby.PrimaryActionClicked += HandleLobbyPrimaryClick;
            _lobby.TimeoutCancelClicked += HandleLobbyCancelClick;

            _drawer.Closed += HandleDrawerClosed;
            _drawer.LeaderboardModeRequested += around => RequestLeaderboard(around, 1u);
            _drawer.LeaderboardPageRequested += page => RequestLeaderboard(false, page);
            _drawer.LeaderboardRefreshRequested += () => RequestLeaderboard(false, 1u);
            _drawer.CosmeticUpdateRequested += loadout => _ranked?.UpdateCosmetic(loadout);

            _settlement.BackRequested += DismissSettlementToCityOrLobby;
            _settlement.RefreshRatingRequested += () =>
            {
                var active = _ranked?.ActiveSettlement;
                if (active != null && active.BattleId != 0) _ranked.RequestBattleResult(active.BattleId);
            };
            _settlement.OpenMaterialClaimRequested += () =>
            {
                DismissSettlementToCityOrLobby();
                GameplayUiRoot.Instance?.OpenEquipmentMaterials();
            };
            _settlement.OpenTrialJournalRequested += entryId =>
            {
                DismissSettlementToCityOrLobby();
                TrialJournalUiRoot.EnsureSpawned();
                TrialJournalUiRoot.Instance?.Open(focusEntryId: entryId);
            };
            _settlement.RequeueSameSizeRequested += size =>
            {
                _pendingRequeueSize = size;
                _waitingForSettlementRequeue = true;
                DismissSettlementToCityOrLobby();
                OpenLobby(size);
                TryRequeueAfterSettlement();
            };
        }

        private void OnEnable() => ApplySafeArea();

        private void ApplySafeArea()
        {
            if (_canvas == null || _windowSafeRoot == null) return;
            var pixels = _canvas.pixelRect;
            if (pixels.width <= 0f || pixels.height <= 0f) return;
            var screenSafe = Screen.safeArea;
            var safe = screenSafe.width > 0f && screenSafe.height > 0f
                ? Rect.MinMaxRect(Mathf.Max(pixels.xMin, screenSafe.xMin), Mathf.Max(pixels.yMin, screenSafe.yMin),
                    Mathf.Min(pixels.xMax, screenSafe.xMax), Mathf.Min(pixels.yMax, screenSafe.yMax))
                : pixels;
            if (safe.width <= 0f || safe.height <= 0f) safe = pixels;
#if UNITY_WEBGL && !UNITY_EDITOR
            // 微信/抖音小游戏右上宿主胶囊保守避让（不依赖尚未装入客户端的宿主 JS 接口）
            float capsuleTop = Mathf.Min(safe.height * 0.12f, 76f);
            safe = Rect.MinMaxRect(safe.xMin, safe.yMin, safe.xMax, Mathf.Max(safe.yMin + 1f, safe.yMax - capsuleTop));
#endif
            if (pixels == _lastCanvasPixels && safe == _lastSafePixels) return;
            _lastCanvasPixels = pixels;
            _lastSafePixels = safe;
            _windowSafeRoot.anchorMin = new Vector2((safe.xMin - pixels.xMin) / pixels.width,
                (safe.yMin - pixels.yMin) / pixels.height);
            _windowSafeRoot.anchorMax = new Vector2((safe.xMax - pixels.xMin) / pixels.width,
                (safe.yMax - pixels.yMin) / pixels.height);
            _windowSafeRoot.offsetMin = _windowSafeRoot.offsetMax = Vector2.zero;
        }

        private void Update()
        {
            ApplySafeArea();
            var game = AppBootstrap.Instance?.GameClient;
            if (!ReferenceEquals(_game, game))
            {
                RebindGame(game);
            }

            bool inGame = _game != null && _game.InGame && _game.IsGateReady;
            ulong player = inGame ? _game.PlayerId : 0UL;
            if (_playerId != player)
            {
                _playerId = player;
                _rankClient?.Reset();
                HideAll();
            }

            bool battleVisible = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            _available = inGame && !battleVisible;
            if (_hud != null) _hud.gameObject.SetActive(_available);
            if (!inGame)
            {
                HideAll();
                return;
            }
            if (battleVisible && _window.gameObject.activeSelf && !_settlement.IsVisible)
            {
                HideLobbyOnly();
            }

            _rankClient?.ObserveConnection();
            if (_drawer != null && _drawer.IsVisible && _drawer.CurrentTab == RankedDrawerTab.Leaderboard)
                _rankClient?.Tick();

            if (_waitingForSettlementRequeue && _available && !_settlement.IsVisible)
            {
                TryRequeueAfterSettlement();
            }

            if (_window.gameObject.activeSelf)
            {
                RenderAll();
                HandleEscapeKey();
            }
        }

        public void ToggleLobby()
        {
            CoreGameplayUiRoot.EnsureSpawned();
            var coreUi = CoreGameplayUiRoot.Instance;
            if (coreUi?.IsFormalPvpVisible == true) coreUi.CloseCurrent();
            else coreUi?.OpenFormalPvp();
        }

        public void OpenLobby(uint teamSize = 0)
        {
            if (_ranked == null) return;
            if (teamSize >= 1 && teamSize <= 5)
                _ranked.SelectedTeamSize = teamSize;
            bool inGame = _game != null && _game.InGame && _game.IsGateReady;
            bool battleVisible = BattleUiRoot.Instance?.IsBattleLayerVisible ?? false;
            if (!inGame || battleVisible) return;
            CoreGameplayUiRoot.EnsureSpawned();
            CoreGameplayUiRoot.Instance?.OpenFormalPvp();
        }

        /// <summary>在正式会武大厅之上打开既有论剑档案抽屉（排行榜 / 赛季记录 / 已获外观）。</summary>
        public void OpenDrawer(RankedDrawerTab tab = RankedDrawerTab.Leaderboard)
        {
            if (!_available || _ranked == null || _drawer == null) return;
            if (_settlement != null && _settlement.IsVisible) return;
            _lobby?.SetVisible(false);
            if (_canvas != null) _canvas.sortingOrder = DrawerOverlaySortingOrder;
            if (_window != null) _window.gameObject.SetActive(true);
            _ranked.RequestProfile(includeHistory: true);
            _drawer.Show(tab);
            RefreshDrawer();
        }

        public void CloseDrawerIfOpen()
        {
            if (_drawer != null && _drawer.IsVisible) _drawer.Hide();
        }

        private void HandleDrawerClosed()
        {
            if (_canvas != null) _canvas.sortingOrder = CanvasSortingOrder;
            if (_window != null && (_lobby == null || !_lobby.IsVisible) && (_settlement == null || !_settlement.IsVisible))
                _window.gameObject.SetActive(false);
        }

        private void HideLobbyOnly()
        {
            if (_settlement != null && _settlement.IsVisible) return;
            if (_waitingForSettlementRequeue)
            {
                _drawer?.Hide();
                _lobby?.SetVisible(false);
                if (_canvas != null) _canvas.sortingOrder = CanvasSortingOrder;
                if (_window != null) _window.gameObject.SetActive(false);
                return;
            }
            HideAll();
        }

        private void HideAll()
        {
            _waitingForSettlementRequeue = false;
            _drawer?.Hide();
            _settlement?.Hide();
            _lobby?.SetVisible(false);
            if (_canvas != null) _canvas.sortingOrder = CanvasSortingOrder;
            if (_window != null) _window.gameObject.SetActive(false);
        }

        private bool HandleInterceptedBattleEnd(BattleEndS2C end, uint localTeamIndex)
        {
            if (_ranked == null || !RankedClient.IsRankedBattleEnd(end)) return false;
            uint fallbackSize = _game?.Battle?.LastQueueTeamSize ?? _ranked.SelectedTeamSize;
            var state = _ranked.BeginSettlementFromBattleEnd(end, localTeamIndex, fallbackSize);
            if (state == null) return false;
            _settlementFromRecovery = false;
            ShowSettlementOverlay(state);
            _settlement?.SetTrialJournalEntry(end?.Settlement?.TrialFacts?.JournalEntryId ?? 0u);
            return true;
        }

        private bool HandleInterceptedRecoveredResult(PlayerBattleResult result)
        {
            if (_ranked == null || !RankedClient.IsRankedRecoveredResult(result)) return false;
            var state = _ranked.BeginSettlementFromRecovery(result);
            if (state == null) return false;
            _settlementFromRecovery = true;
            ShowSettlementOverlay(state);
            uint entryId = result?.TrialJournalEntryId ?? 0u;
            int litCount = result?.TrialNewlyLitSealIds?.Count ?? 0;
            _settlement?.SetTrialJournalEntry(entryId, litCount > 0 ? $"试炼印 +{litCount}" : "看试炼志");
            return true;
        }

        private void ShowSettlementOverlay(RankedBattleSettlementState state)
        {
            _drawer?.Hide();
            _lobby?.SetVisible(false);
            _window.gameObject.SetActive(true);
            _settlement.Show(state, _ranked?.Profile);
            if (_settlementFromRecovery)
            {
                CoreGameplayUiRoot.Instance?.HideRankedSettlement();
                _settlement.SetCoreSettlementVisible(false);
            }
            else
            {
                bool showCoreReward = CoreGameplayUiRoot.Instance?.ShowRankedSettlement(_settlement.Root) ?? false;
                _settlement.SetCoreSettlementVisible(showCoreReward);
            }
        }

        private void DismissSettlementToCityOrLobby()
        {
            bool fromRecovery = _settlementFromRecovery;
            _settlementFromRecovery = false;
            _ranked?.DismissSettlement();
            CoreGameplayUiRoot.Instance?.HideRankedSettlement();
            _settlement?.Hide();
            if (!_waitingForSettlementRequeue)
                _window.gameObject.SetActive(false);
            BattleUiRoot.Instance?.FinishInterceptedBattleResult(fromRecovery);
        }

        /// <summary>核心材料卡快捷领取复用本页唯一的结算确认路径。</summary>
        public void CloseCoreSettlementForMaterialClaim()
        {
            if (_settlement != null && _settlement.IsVisible)
                DismissSettlementToCityOrLobby();
        }

        private void TryRequeueAfterSettlement()
        {
            var entry = TeamUiRoot.Instance?.MatchEntry;
            var battle = _game?.Battle;
            if (entry == null || battle == null || battle.Phase != BattlePhase.None) return;
            uint size = _pendingRequeueSize >= 1 && _pendingRequeueSize <= 5
                ? _pendingRequeueSize
                : (_ranked?.SelectedTeamSize ?? _lobby.SelectedTeamSize);
            if (_ranked != null && size >= 1 && size <= 5) _ranked.SelectedTeamSize = size;
            if (CoreGameplayUiRoot.Instance?.IsFormalPvpVisible != true)
                CoreGameplayUiRoot.Instance?.OpenFormalPvp();
            if (CoreGameplayUiRoot.Instance?.EnterFormalPvp(size) != true) return;
            _waitingForSettlementRequeue = false;
            if (_window != null && (_drawer == null || !_drawer.IsVisible) && (_settlement == null || !_settlement.IsVisible))
                _window.gameObject.SetActive(false);
            RenderAll();
        }

        private void HandleLobbyPrimaryClick()
        {
            var battle = _game?.Battle;
            var entry = TeamUiRoot.Instance?.MatchEntry;
            if (battle == null || entry == null || _ranked == null) return;

            if (_lobby.ActiveStateKey == "waiting_120_seconds")
            {
                uint elapsed = battle.LastQueueStatus?.QueuedSeconds ?? 120u;
                _lobby.ContinueWaitingFromTimeout(elapsed);
                RenderAll();
                return;
            }
            if (_lobby.ActiveStateKey == "waiting")
            {
                entry.Cancel();
                RenderAll();
                return;
            }
            if (_lobby.ActiveStateKey == "idle")
            {
                CoreGameplayUiRoot.Instance?.EnterFormalPvp(_lobby.SelectedTeamSize);
                RenderAll();
            }
        }

        private void HandleLobbyCancelClick()
        {
            TeamUiRoot.Instance?.MatchEntry?.Cancel();
            RenderAll();
        }

        private void RequestLeaderboard(bool around, uint page)
        {
            if (_rankClient == null) return;
            var season = _ranked?.Profile?.CurrentSeason;
            RankBoardKey board = (season != null && !string.IsNullOrEmpty(season.SeasonKey) && season.RankGeneration != 0)
                ? RankClient.SeasonBoard(season.SeasonKey, season.RankGeneration)
                : RankClient.LatestBoard(RankClient.DefaultBoardType);
            if (around) _rankClient.ReadAround(board, 2);
            else _rankClient.ReadPage(board, page, RankClient.PageSize);
        }

        private void RebindGame(GameClient game)
        {
            UnbindClients();
            _game = game;
            if (game == null) return;
            _ranked = game.Ranked;
            if (_ranked != null) _ranked.Changed += RenderAll;
            _rankClient = new RankClient(new GameClientBattleTransport(game), () => game.GateConnectionIdentity,
                () => Time.realtimeSinceStartup, game.RpcGuard);
            _rankClient.Changed += RefreshDrawer;
        }

        private void UnbindClients()
        {
            if (_ranked != null)
            {
                _ranked.Changed -= RenderAll;
                _ranked = null;
            }
            if (_rankClient != null)
            {
                _rankClient.Changed -= RefreshDrawer;
                _rankClient.Dispose();
                _rankClient = null;
            }
        }

        private void RenderAll()
        {
            if (_window == null || !_window.gameObject.activeSelf) return;
            string playerName = _game != null ? _game.ResolveRoleName(_game.PlayerId) : string.Empty;
            bool gateReady = _game != null && _game.InGame && _game.IsGateReady;

            if (_settlement != null && _settlement.IsVisible)
            {
                _settlement.Render(_ranked?.ActiveSettlement, _ranked?.Profile);
                return;
            }

            if (_lobby != null && _lobby.IsVisible)
                _lobby.Render(_ranked, TeamUiRoot.Instance?.Client, _game?.Battle,
                    TeamUiRoot.Instance?.MatchEntry, _game?.Pets, playerName, gateReady);
            if (_drawer != null && _drawer.IsVisible) RefreshDrawer();
        }

        private void RefreshDrawer()
        {
            if (_drawer == null || !_drawer.IsVisible) return;
            string playerName = _game != null ? _game.ResolveRoleName(_game.PlayerId) : string.Empty;
            string charId = _game != null ? _game.ResolveCharacterId(_game.PlayerId) : null;
            Sprite portrait = string.IsNullOrEmpty(charId) ? null : MmorpgClient.World.QdaoCharacterCatalog.LoadPortrait(charId);
            _drawer.RenderProfile(_ranked?.Profile, playerName, _ranked?.CosmeticSaving == true, _ranked?.Status, portrait);
            _drawer.RenderLeaderboard(_rankClient, _ranked?.Profile);
        }

        private void HandleEscapeKey()
        {
            bool esc = false;
#if ENABLE_INPUT_SYSTEM
            esc = Keyboard.current?.escapeKey.wasPressedThisFrame == true;
#elif ENABLE_LEGACY_INPUT_MANAGER
            esc = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (!esc) return;
            if (_drawer != null && _drawer.IsVisible) _drawer.Hide();
            else if (_settlement != null && _settlement.IsVisible) DismissSettlementToCityOrLobby();
            else HideAll();
        }
    }
}
