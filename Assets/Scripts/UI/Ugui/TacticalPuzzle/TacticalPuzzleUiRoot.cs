using System;
using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.TacticalPuzzle;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.CoreLoop;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Social;
using MmorpgClient.World.Tianyong;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui.TacticalPuzzle
{
    /// <summary>
    /// 万象残局坊 V1 UI 总根：
    /// - 1920×1080 基准安全区画布 + Screen.safeArea + 小游戏右上 176×72 胶囊避让；
    /// - 严格对称订阅/退订 <see cref="GameClient.TacticalPuzzle"/> 事件；
    /// - 监听 (PlayerId, GateConnectionIdentity) 会话边界，换角自动重置视图；
    /// - 绝不在主城 HUD 添加红点催促，由归元城齐问锋、聊天 #PUZ 卡片与残局结算按需唤起。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TacticalPuzzleUiRoot : MonoBehaviour
    {
        public static TacticalPuzzleUiRoot Instance { get; private set; }
        public const int CanvasSortingOrder = 216;

        public bool IsVisible => _backdrop != null && _backdrop.gameObject.activeSelf;

        private GameClient _game;
        private TacticalPuzzleClient _client;
        private ulong _boundPlayerId;
        private object _boundConnection;

        private Canvas _canvas;
        private RectTransform _safeRoot;
        private RectTransform _backdrop;
        private RectTransform _fittedRoot;
        private TacticalPuzzleWindow _window;
        private Rect _lastSafePixels;
        private Rect _lastCanvasPixels;
        private bool _dirty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn() => EnsureSpawned();

        public static void EnsureSpawned()
        {
            if (Instance != null) return;
            var go = new GameObject("[TacticalPuzzleUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<TacticalPuzzleUiRoot>();
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

        private void OnDisable()
        {
            UnbindClient();
        }

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
            var canvasGo = new GameObject("TacticalPuzzleCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = CanvasSortingOrder;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(TacticalPuzzleUiArt.DesignWidth, TacticalPuzzleUiArt.DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            _safeRoot = QdaoUguiFactory.CreateStretch("TacticalPuzzleSafeArea", canvasGo.transform, Vector4.zero);
            _backdrop = QdaoUguiFactory.CreateStretch("TacticalPuzzleBackdrop", _safeRoot, Vector4.zero);
            var shade = _backdrop.gameObject.AddComponent<Image>();
            shade.color = TacticalPuzzleUiArt.BackdropShade;
            shade.raycastTarget = true;
            _backdrop.gameObject.AddComponent<GameplayInputBlocker>();

            _fittedRoot = QdaoUguiFactory.CreateCenteredRect("TacticalPuzzleFittedRoot", _backdrop,
                TacticalPuzzleUiArt.DesignWidth, TacticalPuzzleUiArt.DesignHeight);
            var fit = _fittedRoot.gameObject.AddComponent<EquipmentUiFit>();
            fit.referenceSize = new Vector2(TacticalPuzzleUiArt.DesignWidth, TacticalPuzzleUiArt.DesignHeight);

            _window = new TacticalPuzzleWindow(_fittedRoot);
            _window.CloseRequested += Hide;
            _window.FilterChanged += HandleFilterChanged;
            _window.RefreshRequested += HandleManualRefresh;
            _window.StartBattleRequested += HandleStartBattle;
            _window.WatchSolutionRequested += HandleWatchSolution;
            _window.LikeRequested += HandleLike;
            _window.ShareRequested += HandleShare;
            _window.DeleteRequested += HandleDelete;
            _window.SaveDraftRequested += HandleSaveDraft;

            _backdrop.gameObject.SetActive(false);
            ApplySafeArea();
        }

        /// <summary>打开万象残局坊主界面（可选高亮某道残局或切换初始页签）。</summary>
        public void Open(ulong focusPuzzleId = 0ul, Match.TacticalPuzzleListTab? tab = null)
        {
            SyncBinding();
            if (_backdrop == null || _window == null) return;
            if (BattleUiRoot.Instance?.IsBattleLayerVisible == true) return;
            CityPanelRegistry.HideOthers(this);
            _window.FocusPuzzle(focusPuzzleId);
            _backdrop.gameObject.SetActive(true);
            ApplySafeArea();
            var targetTab = tab ?? (_client?.CurrentTab ?? Match.TacticalPuzzleListTab.Hot);
            if (targetTab == Match.TacticalPuzzleListTab.Unspecified) targetTab = Match.TacticalPuzzleListTab.Hot;
            _client?.RequestList(targetTab, _client.CurrentTagFilterMask, 0u, 30u, force: _client.IsListDirty || tab.HasValue);
            RenderNow();
        }

        /// <summary>直接发起指定残局的挑战或作者自证战（供聊天 #PUZ 卡片与结算再试一次调用）。</summary>
        public void StartPuzzleChallenge(ulong puzzleId, bool isAuthorVerify = false)
        {
            if (puzzleId == 0ul) return;
            SyncBinding();
            if (_client == null || !_client.Ready)
            {
                ShowToast(TacticalPuzzleClient.NotConnectedMessage);
                return;
            }
            HandleStartBattle(puzzleId, isAuthorVerify);
        }

        /// <summary>直接拉取并播放指定残局的作者标准解法录像（供聊天 #PUZ 卡片与结算页调用）。</summary>
        public void WatchSolutionById(ulong puzzleId, uint expectedRevision = 0u)
        {
            if (puzzleId == 0ul) return;
            SyncBinding();
            if (_client == null || !_client.Ready)
            {
                ShowToast(TacticalPuzzleClient.NotConnectedMessage);
                return;
            }
            ShowToast("正在拉取作者标准解法录像…");
            _client.RequestSolutionReplay(puzzleId, expectedRevision,
                replay => PlaySolutionReplay(replay),
                err => ShowToast(err));
        }

        /// <summary>直接为指定残局点赞（供结算页快捷按钮调用）。</summary>
        public void LikePuzzleById(ulong puzzleId)
        {
            if (puzzleId == 0ul) return;
            SyncBinding();
            _client?.LikePuzzle(puzzleId);
        }

        /// <summary>将残局卡片草稿填入仙友会世界频道，仍须玩家确认发送。</summary>
        public void ShareDraftToWorldChat(string draft)
        {
            if (string.IsNullOrWhiteSpace(draft)) return;
            Hide();
            if (SocialUiRoot.Instance == null || !SocialUiRoot.Instance.TryComposeTacticalPuzzleCard(draft))
            {
                ShowToast("残局约战卡已生成，请打开仙友会世界频道发送。");
            }
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
            var client = game?.TacticalPuzzle;
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
                    _client.SolutionReplayLoaded += PlaySolutionReplay;
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
                _client.SolutionReplayLoaded -= PlaySolutionReplay;
            }
            _client = null;
            _game = null;
        }

        private void HandleClientChanged() => _dirty = true;

        private void HandleClientNotice(string message)
        {
            ShowToast(message);
            _dirty = true;
        }

        private void HandleFilterChanged(Match.TacticalPuzzleListTab tab, uint tagMask)
        {
            _client?.RequestList(tab, tagMask, 0u, 30u, force: true);
            RenderNow();
        }

        private void HandleManualRefresh()
        {
            if (_client == null) return;
            _client.RequestList(_client.CurrentTab, _client.CurrentTagFilterMask, 0u, 30u, force: true);
            RenderNow();
        }

        private void HandleStartBattle(ulong puzzleId, bool isAuthorVerify)
        {
            if (_client == null || puzzleId == 0ul) return;
            _client.StartBattle(puzzleId, isAuthorVerify,
                _ => Hide(),
                err => ShowToast(err));
        }

        private void HandleWatchSolution(Match.TacticalPuzzleSummaryView puzzle)
        {
            if (puzzle == null || puzzle.PuzzleId == 0ul) return;
            WatchSolutionById(puzzle.PuzzleId, puzzle.SpecRevision);
        }

        private void HandleLike(ulong puzzleId)
        {
            _client?.LikePuzzle(puzzleId);
        }

        private void HandleShare(Match.TacticalPuzzleSummaryView puzzle)
        {
            string draft = TacticalPuzzleModels.FormatChatShareDraft(puzzle);
            ShareDraftToWorldChat(draft);
        }

        private void HandleDelete(ulong puzzleId)
        {
            _client?.DeletePuzzle(puzzleId, () =>
            {
                if (_client != null)
                    _client.RequestList(_client.CurrentTab, _client.CurrentTagFilterMask, 0u, 30u, force: true);
            });
        }

        private void HandleSaveDraft(
            ulong puzzleId,
            string title,
            string hintText,
            uint tagMask,
            global::TacticalPuzzleSpec spec,
            bool startVerifyImmediately)
        {
            if (_client == null) return;
            _client.SaveDraft(puzzleId, title, hintText, tagMask, spec,
                saved =>
                {
                    _window?.CloseEditor();
                    if (startVerifyImmediately && saved != null && saved.PuzzleId != 0ul)
                    {
                        HandleStartBattle(saved.PuzzleId, true);
                    }
                    else
                    {
                        _client.RequestList(Match.TacticalPuzzleListTab.MyCreated, 0u, 0u, 30u, force: true);
                    }
                });
        }

        private void PlaySolutionReplay(BattleReplay replay)
        {
            if (replay == null) return;
            Hide();
            BattleUiRoot.EnsureSpawned();
            BattleUiRoot.Instance?.PlayExternalReplay(replay);
        }

        private void RenderNow()
        {
            _window?.Render(_client);
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
            if (!pressed || CityPanelRegistry.IsTyping()) return;
            if (_window != null && _window.DismissModalIfOpen()) return;
            Hide();
        }

        private static void ShowToast(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            if (CoreGameplayUiRoot.Instance != null) CoreGameplayUiRoot.Instance.ShowToast(message);
            else BattleUiRoot.Instance?.ShowToast(message);
        }
    }
}
