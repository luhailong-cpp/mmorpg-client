using System;
using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.TrialJournal;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.CoreLoop;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Ranked;
using MmorpgClient.UI.Ugui.Team;
using MmorpgClient.World.Tianyong;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.TrialJournal
{
    /// <summary>
    /// 五行试炼志 V2 UI 总根：
    /// - 自动挂载独立安全区画布（1920×1080 基准 + Screen.safeArea + 小游戏右上胶囊避让）；
    /// - 严格订阅/对称退订 <see cref="GameClient.TrialJournal"/> 的 Changed / Notice / MilestoneClaimed / ArtifactRedeemed；
    /// - 监听 (PlayerId, GateConnectionIdentity) 会话边界，切角自动清空旧视图；
    /// - 绝不在主城 HUD 添加催促红点，入口由首领试炼页、主城闲时卡、赛季印页签与战斗结算页按需唤起。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrialJournalUiRoot : MonoBehaviour
    {
        public static TrialJournalUiRoot Instance { get; private set; }
        public const int CanvasSortingOrder = 215;

        public bool IsVisible => _backdrop != null && _backdrop.gameObject.activeSelf;

        private GameClient _game;
        private TrialJournalClient _client;
        private ulong _boundPlayerId;
        private object _boundConnection;

        private Canvas _canvas;
        private RectTransform _safeRoot;
        private RectTransform _backdrop;
        private RectTransform _fittedRoot;
        private TrialJournalWindow _window;
        private Rect _lastSafePixels;
        private Rect _lastCanvasPixels;
        private bool _dirty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn() => EnsureSpawned();

        public static void EnsureSpawned()
        {
            if (Instance != null) return;
            var go = new GameObject("[TrialJournalUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<TrialJournalUiRoot>();
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
            CityPanelRegistry.Register(this, Hide);
            BuildCanvas();
        }

        private void OnEnable() => ApplySafeArea();

        private void OnDestroy()
        {
            CityPanelRegistry.Unregister(this);
            UnbindClient();
            _window?.Dispose();
            _window = null;
            if (Instance == this) Instance = null;
        }

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("TrialJournalCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = CanvasSortingOrder;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(TrialJournalUiArt.DesignWidth, TrialJournalUiArt.DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            _safeRoot = QdaoUguiFactory.CreateStretch("TrialJournalSafeArea", canvasGo.transform, Vector4.zero);
            _backdrop = QdaoUguiFactory.CreateStretch("TrialJournalBackdrop", _safeRoot, Vector4.zero);
            var shade = _backdrop.gameObject.AddComponent<Image>();
            shade.color = TrialJournalUiArt.BackdropShade;
            shade.raycastTarget = true;
            _backdrop.gameObject.AddComponent<GameplayInputBlocker>();

            _fittedRoot = QdaoUguiFactory.CreateCenteredRect("TrialJournalFittedRoot", _backdrop,
                TrialJournalUiArt.DesignWidth, TrialJournalUiArt.DesignHeight);
            var fit = _fittedRoot.gameObject.AddComponent<EquipmentUiFit>();
            fit.referenceSize = new Vector2(TrialJournalUiArt.DesignWidth, TrialJournalUiArt.DesignHeight);

            _window = new TrialJournalWindow(_fittedRoot);
            _window.CloseRequested += Hide;
            _window.JumpRequested += HandleJumpRequested;
            _window.ClaimMilestoneRequested += HandleClaimMilestone;
            _window.RedeemVoucherRequested += HandleRedeemVoucher;
            _window.RefreshRequested += HandleManualRefresh;
            _window.OpenAppearanceRequested += HandleOpenAppearance;

            _backdrop.gameObject.SetActive(false);
            ApplySafeArea();
        }

        /// <summary>打开五行试炼志（可选直达指定关卡格或卷册）。</summary>
        public void Open(uint focusEntryId = 0u, uint focusChapterId = 0u)
        {
            SyncBinding();
            if (_backdrop == null || _window == null) return;
            if (BattleUiRoot.Instance?.IsBattleLayerVisible == true) return;
            CityPanelRegistry.HideOthers(this);
            _window.FocusTarget(_client?.Snapshot, focusEntryId, focusChapterId);
            _backdrop.gameObject.SetActive(true);
            ApplySafeArea();
            _client?.RequestSnapshot(force: false);
            RenderNow();
        }

        public void Hide()
        {
            if (_backdrop == null) return;
            _window?.DismissModalIfOpen();
            _backdrop.gameObject.SetActive(false);
        }

        private void Update()
        {
            ApplySafeArea();
            SyncBinding();
            if (!IsVisible) return;
            if (BattleUiRoot.Instance?.IsBattleLayerVisible == true)
            {
                Hide();
                return;
            }
            if (_dirty)
            {
                _dirty = false;
                RenderNow();
            }
            HandleEscapeKey();
        }

        private void SyncBinding()
        {
            var game = AppBootstrap.Instance?.GameClient;
            var client = game?.TrialJournal;
            if (!ReferenceEquals(game, _game) || !ReferenceEquals(client, _client)
                || (game != null && (game.PlayerId != _boundPlayerId || !ReferenceEquals(game.GateConnectionIdentity, _boundConnection))))
            {
                bool roleChanged = game == null || game.PlayerId != _boundPlayerId;
                UnbindClient();
                _game = game;
                _client = client;
                _boundPlayerId = game?.PlayerId ?? 0ul;
                _boundConnection = game?.GateConnectionIdentity;
                if (roleChanged) _window?.ResetForSession();
                if (_client != null)
                {
                    _client.Changed += HandleClientChanged;
                    _client.Notice += HandleClientNotice;
                    _client.MilestoneClaimed += HandleMilestoneClaimed;
                    _client.ArtifactRedeemed += HandleArtifactRedeemed;
                }
                _dirty = true;
            }
        }

        private void UnbindClient()
        {
            if (_client != null)
            {
                _client.Changed -= HandleClientChanged;
                _client.Notice -= HandleClientNotice;
                _client.MilestoneClaimed -= HandleMilestoneClaimed;
                _client.ArtifactRedeemed -= HandleArtifactRedeemed;
            }
            _client = null;
            _game = null;
        }

        private void HandleClientChanged() => _dirty = true;

        private void HandleClientNotice(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                CoreGameplayUiRoot.Instance?.ShowToast(message);
            _dirty = true;
        }

        private void HandleMilestoneClaimed(TrialMilestoneClaimedEvent ev)
        {
            if (!string.IsNullOrEmpty(ev.GrantedAppearanceId))
            {
                CoreGameplayUiRoot.Instance?.ShowToast("已免费永久解锁「星阵宗师」零战力专属形象！");
            }
            _dirty = true;
        }

        private void HandleArtifactRedeemed(TrialArtifactRedeemedEvent ev)
        {
            CoreGameplayUiRoot.Instance?.ShowToast("自用法宝兑换成功，已放入人物背包。");
            _dirty = true;
        }

        private void HandleJumpRequested(global::TrialJournalEntryView entry)
        {
            if (entry == null || !entry.CanEnter) return;
            if (!TrialJournalModels.TryParseJumpTarget(entry.JumpTarget, out var target))
            {
                CoreGameplayUiRoot.Instance?.ShowToast("当前试炼入口暂不可跳转。");
                return;
            }
            if (target.Kind == TrialJumpKind.RankedPvp)
            {
                Hide();
                if (RankedUiRoot.Instance != null) RankedUiRoot.Instance.OpenLobby();
                else CoreGameplayUiRoot.Instance?.OpenFormalPvp();
                return;
            }
            var matchEntry = TeamUiRoot.Instance?.MatchEntry;
            if (matchEntry == null)
            {
                CoreGameplayUiRoot.Instance?.ShowToast("战斗匹配模块尚未就绪，请稍候重试。");
                return;
            }
            if (matchEntry.StartFromTrialJump(target))
            {
                Hide();
                if (!string.IsNullOrEmpty(matchEntry.Status))
                    CoreGameplayUiRoot.Instance?.ShowToast(matchEntry.Status);
            }
            else if (!string.IsNullOrEmpty(matchEntry.Status))
            {
                CoreGameplayUiRoot.Instance?.ShowToast(matchEntry.Status);
            }
        }

        private void HandleClaimMilestone(uint milestoneId)
        {
            _client?.ClaimMilestone(milestoneId);
            _dirty = true;
        }

        private void HandleRedeemVoucher(uint artifactItemConfigId)
        {
            _client?.RedeemArtifactVoucher(artifactItemConfigId);
            _dirty = true;
        }

        private void HandleManualRefresh()
        {
            _client?.RequestSnapshot(force: false);
            RenderNow();
        }

        private void HandleOpenAppearance()
        {
            Hide();
            AppearanceUiRoot.Instance?.ShowPanel();
        }

        private void RenderNow()
        {
            if (_window == null) return;
            _window.Render(
                liveSnapshot: _client?.Snapshot,
                snapshotPending: _client?.SnapshotPending ?? false,
                actionPending: _client?.ActionPending ?? false,
                ready: _client?.Ready ?? false,
                requiresReconnect: _client?.RequiresReconnect ?? false,
                clientStatus: _client?.Status ?? "",
                statusIsError: _client?.StatusIsError ?? false);
        }

        private void ApplySafeArea()
        {
            if (_canvas == null || _safeRoot == null) return;
            var pixels = _canvas.pixelRect;
            if (pixels.width <= 0f || pixels.height <= 0f) return;
            var screenSafe = Screen.safeArea;
            var safe = screenSafe.width > 0f && screenSafe.height > 0f
                ? Rect.MinMaxRect(
                    Mathf.Max(pixels.xMin, screenSafe.xMin),
                    Mathf.Max(pixels.yMin, screenSafe.yMin),
                    Mathf.Min(pixels.xMax, screenSafe.xMax),
                    Mathf.Min(pixels.yMax, screenSafe.yMax))
                : pixels;
            if (safe.width <= 0f || safe.height <= 0f) safe = pixels;
            if (pixels == _lastCanvasPixels && safe == _lastSafePixels) return;
            _lastCanvasPixels = pixels;
            _lastSafePixels = safe;
            _safeRoot.anchorMin = new Vector2((safe.xMin - pixels.xMin) / pixels.width,
                (safe.yMin - pixels.yMin) / pixels.height);
            _safeRoot.anchorMax = new Vector2((safe.xMax - pixels.xMin) / pixels.width,
                (safe.yMax - pixels.yMin) / pixels.height);
            _safeRoot.offsetMin = _safeRoot.offsetMax = Vector2.zero;
            // 窄横屏 / 小游戏端把关闭按钮切到左上，严格避让右上小游戏胶囊区（UNITY_UGUI_HANDOFF.md）
            bool compact = safe.width < 1100f || safe.height < 520f;
            _window?.SetCompactCloseOnLeft(compact);
        }

        private void HandleEscapeKey()
        {
            bool pressed = false;
#if ENABLE_INPUT_SYSTEM
            pressed = Keyboard.current?.escapeKey.wasPressedThisFrame == true;
#elif ENABLE_LEGACY_INPUT_MANAGER
            pressed = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (!pressed) return;
            if (_window != null && _window.DismissModalIfOpen()) return;
            Hide();
        }
    }
}
