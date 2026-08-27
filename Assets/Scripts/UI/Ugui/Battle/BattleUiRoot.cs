using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MmorpgClient.Game.Battle;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>
    /// 回合战斗 UI 总根(UGUI 悬浮层):
    ///   - 自建独立 Canvas(sortingOrder 高于 QdaoUgui 主画布),两种 UI 模式
    ///     (uGUI / FairyGUI 兼容模式)下都可用;
    ///   - 持有排队面板、挑战弹窗、全屏战斗屏、结算屏、提示 toast;
    ///   - 只调用/订阅 BattleClient API 契约(Game/Battle/BattleClient.cs,NET 路实现),
    ///     实例解析集中在 <see cref="ResolveBattleClient"/> 一处;
    ///   - 重连:Phase 由 None 直接变 WaitingAction/Resolving 时自动打开战斗屏;
    ///   - OnTurnResult 事件流播完后调用 AckTurnPlayed()。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleUiRoot : MonoBehaviour
    {
        public static BattleUiRoot Instance { get; private set; }

        // FairyGUI 兼容模式下,仅场景屏(SceneV3Screen)激活时显示战斗入口
        private static bool s_sceneScreenActive;

        private AppBootstrap _app;
        private BattleClient _client;
        private bool _clientBound;
        private Game.GameClient _boundGameClient;

        private GameObject _canvasGo;
        private RectTransform _hudRoot;      // 入口按钮 + 排队面板
        private GameObject _battleLayerGo;   // 全屏底 + 战斗屏
        private RectTransform _battleRoot;
        private RectTransform _modalResultRoot;
        private RectTransform _modalPopupRoot;
        private Image _modalDim;
        private TMP_Text _toastText;

        private UiTextButton _entryButton;
        private BattleQueuePanel _queuePanel;
        private BattleChallengePopup _challengePopup;
        private BattleScreen _battleScreen;
        private BattleResultPanel _resultPanel;

        private bool _battleOpen;
        private bool _playing;
        private bool _resultShowing;
        private BattleEndS2C _pendingEnd;
        private Coroutine _playCo;
        private Coroutine _toastCo;

        /// <summary>供子面板取 BattleClient(可能为 null:NET 路尚未初始化)。</summary>
        public BattleClient Client => _client;

        // ── 生命周期 ────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            EnsureSpawned();
        }

        /// <summary>场景屏入口调用,保证战斗 UI 层存在(重复调用无副作用)。</summary>
        public static void EnsureSpawned()
        {
            if (Instance != null) return;
            var go = new GameObject("[BattleUi]");
            DontDestroyOnLoad(go);
            go.AddComponent<BattleUiRoot>();
        }

        /// <summary>FairyGUI 场景屏进入(SceneV3Screen.OnEnter)。</summary>
        public static void NotifySceneScreenEntered()
        {
            EnsureSpawned();
            s_sceneScreenActive = true;
        }

        /// <summary>FairyGUI 场景屏退出(SceneV3Screen.OnExit):收起悬浮面板。</summary>
        public static void NotifySceneScreenExited()
        {
            s_sceneScreenActive = false;
            Instance?.HideTransientPanels();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            ScreenRouter.ScreenChanged += HandleScreenChanged;
        }

        private void Update()
        {
            EnsureBound();
            if (_canvasGo == null) return;

            // 兜底自愈:重连补拉完成但事件时序错位时,按 Phase+State 打开战斗屏
            if (_clientBound && !_battleOpen && _client.State != null &&
                (_client.Phase == BattlePhase.WaitingAction || _client.Phase == BattlePhase.Resolving))
            {
                EnsureBattleOpen();
            }

            _queuePanel?.Tick();
            _challengePopup?.Tick();
            _battleScreen?.Tick();
            RefreshEntryVisibility();
        }

        private void OnDestroy()
        {
            ScreenRouter.ScreenChanged -= HandleScreenChanged;
            UnbindClient();
            if (_boundGameClient != null)
            {
                _boundGameClient.OnDisconnected -= HandleDisconnected;
                _boundGameClient = null;
            }
            if (Instance == this) Instance = null;
        }

        // ── 绑定 ────────────────────────────────────────────

        private void EnsureBound()
        {
            if (_app == null)
            {
                _app = FindAnyObjectByType<AppBootstrap>();
                if (_app == null) return;
            }
            if (_canvasGo == null) BuildCanvas();

            if (!_clientBound)
            {
                var client = ResolveBattleClient();
                if (client != null)
                {
                    _client = client;
                    BindClient();
                    _clientBound = true;
                }
            }
            if (_boundGameClient == null && _app.GameClient != null)
            {
                _boundGameClient = _app.GameClient;
                _boundGameClient.OnDisconnected += HandleDisconnected;
            }
        }

        /// <summary>
        /// 解析 BattleClient 单例(契约:挂接方式与 GameClient 现有模式一致)。
        /// UI 路只在此一处取实例;若 NET 路以其他方式暴露实例,仅需改本方法。
        /// </summary>
        private static BattleClient ResolveBattleClient() => BattleClient.Instance;

        private void BindClient()
        {
            _client.OnPhaseChanged += HandlePhaseChanged;
            _client.OnBattleStart += HandleBattleStart;
            _client.OnTurnResult += HandleTurnResult;
            _client.OnBattleEnd += HandleBattleEnd;
            _client.OnChallengeInvite += HandleChallengeInvite;
            _client.OnChallengeResult += HandleChallengeResult;
            _client.OnQueueStatus += HandleQueueStatus;
            _client.OnError += HandleClientError;
        }

        private void UnbindClient()
        {
            if (!_clientBound || _client == null) return;
            _client.OnPhaseChanged -= HandlePhaseChanged;
            _client.OnBattleStart -= HandleBattleStart;
            _client.OnTurnResult -= HandleTurnResult;
            _client.OnBattleEnd -= HandleBattleEnd;
            _client.OnChallengeInvite -= HandleChallengeInvite;
            _client.OnChallengeResult -= HandleChallengeResult;
            _client.OnQueueStatus -= HandleQueueStatus;
            _client.OnError -= HandleClientError;
            _clientBound = false;
        }

        // ── Canvas 构建(参数与 QdaoUguiRuntime 保持一致) ──

        private void BuildCanvas()
        {
            EnsureEventSystem();

            _canvasGo = new GameObject(
                "[BattleUgui]",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            _canvasGo.transform.SetParent(transform, false);

            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200; // QdaoUgui 主画布是 100,战斗层压在其上
            canvas.pixelPerfect = true;

            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;

            // 层级(兄弟顺序即绘制顺序):HUD < 战斗全屏 < 模态 < toast
            _hudRoot = CreateDesignRoot("HudRoot", _canvasGo.transform);

            _battleLayerGo = new GameObject("BattleLayer", typeof(RectTransform));
            var battleLayerRect = (RectTransform)_battleLayerGo.transform;
            battleLayerRect.SetParent(_canvasGo.transform, false);
            Stretch(battleLayerRect);
            var arenaBackground = BattleUiStyle.ResolveArenaBackground();
            if (arenaBackground != null)
            {
                QdaoUguiFactory.CreateAspectFillImage(
                    "BattleArenaArt",
                    battleLayerRect,
                    arenaBackground,
                    QdaoUguiTheme.DesignWidth / QdaoUguiTheme.DesignHeight);
                BattleUiWidgets.CreateStretchPanel(
                    "BattleBackdropShade", battleLayerRect, BattleUiStyle.BattleBackdropShade);
            }
            else
            {
                BattleUiWidgets.CreateStretchPanel("BattleBg", battleLayerRect, BattleUiStyle.BattleBg);
            }
            _battleRoot = CreateDesignRoot("BattleRoot", battleLayerRect);

            var modalLayer = new GameObject("ModalLayer", typeof(RectTransform));
            var modalRect = (RectTransform)modalLayer.transform;
            modalRect.SetParent(_canvasGo.transform, false);
            Stretch(modalRect);
            _modalDim = BattleUiWidgets.CreateStretchPanel("ModalDim", modalRect, BattleUiStyle.ModalDim);
            _modalResultRoot = CreateDesignRoot("ResultRoot", modalRect);
            _modalPopupRoot = CreateDesignRoot("PopupRoot", modalRect);

            var toastRoot = CreateDesignRoot("ToastRoot", _canvasGo.transform);

            // ── 内容 ──
            _entryButton = BattleUiWidgets.CreateTextButton("BattleEntry", _hudRoot,
                2350f, 210f, 150f, 70f, "战斗", 26f,
                BattleUiStyle.ButtonPlateAccent, BattleUiStyle.ButtonText);
            _entryButton.Button.onClick.AddListener(OnEntryClicked);
            _entryButton.SetVisible(false);

            _queuePanel = new BattleQueuePanel(this, _hudRoot);
            _battleScreen = new BattleScreen(this, _battleRoot);
            _resultPanel = new BattleResultPanel(_modalResultRoot, OnResultConfirmed);
            _challengePopup = new BattleChallengePopup(this, _modalPopupRoot);

            _toastText = QdaoUguiFactory.CreateText("Toast", toastRoot, 680f, 150f, 1200f, 56f,
                string.Empty, 26f, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);

            _battleLayerGo.SetActive(false);
            RefreshModalDim();
        }

        private static RectTransform CreateDesignRoot(string name, UnityEngine.Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void EnsureEventSystem()
        {
            // FairyGUI 兼容模式下场上可能没有 EventSystem(照 QdaoUguiRuntime 模式补建)
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("[EventSystem]", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(transform, false);
        }

        // ── BattleClient 事件 ───────────────────────────────

        private void HandlePhaseChanged(BattlePhase phase)
        {
            switch (phase)
            {
                case BattlePhase.Queued:
                    _queuePanel?.SetQueueing(true);
                    _queuePanel?.Show();
                    break;
                case BattlePhase.Preparing:
                    _queuePanel?.SetQueueing(false);
                    _queuePanel?.Hide();
                    ShowToast("匹配成功,战斗准备中…");
                    break;
                case BattlePhase.WaitingAction:
                case BattlePhase.Resolving:
                    // 正常开局与重连(None 直接跳到 WaitingAction/Resolving)都在此打开战斗屏
                    _queuePanel?.SetQueueing(false);
                    _queuePanel?.Hide();
                    EnsureBattleOpen();
                    if (_battleOpen) _battleScreen.OnPhaseChanged(phase);
                    break;
                case BattlePhase.Ended:
                    if (_battleOpen) _battleScreen.OnPhaseChanged(phase);
                    break;
                case BattlePhase.None:
                    _queuePanel?.SetQueueing(false);
                    // 结算面板/回合播放还在时不收屏,等玩家确认
                    if (!_resultShowing && !_playing && _pendingEnd == null)
                        CloseBattle();
                    break;
            }
        }

        private void HandleBattleStart(BattleStartS2C start)
        {
            _pendingEnd = null;
            _resultShowing = false;
            _resultPanel?.Hide();
            RefreshModalDim();
            OpenBattle(start?.State ?? _client?.State);
            ShowToast("战斗开始!");
        }

        private void HandleTurnResult(TurnResultS2C result)
        {
            EnsureBattleOpen();
            // 重连竞态:本地 State 还没补拉到,但 TurnResultS2C 自带结算后权威
            // 状态,直接用它开屏,不丢这回合的表现。
            if (!_battleOpen && result?.State != null)
                OpenBattle(result.State);
            if (!_battleOpen)
            {
                // 实在开不了屏(连 result.State 都没有):立刻 Ack,
                // 避免 BattleClient 卡死在 Resolving 等一个永远不来的播放完成。
                _client?.AckTurnPlayed();
                return;
            }
            if (_playCo != null) StopCoroutine(_playCo);
            _playCo = StartCoroutine(CoPlayTurn(result));
        }

        private IEnumerator CoPlayTurn(TurnResultS2C result)
        {
            _playing = true;
            yield return _battleScreen.CoPlayTurn(result);
            _playing = false;
            _playCo = null;

            // 契约:UI 播完回合表现后必须 Ack,BattleClient 才会回 WaitingAction
            _client?.AckTurnPlayed();

            if (_pendingEnd != null)
            {
                var end = _pendingEnd;
                _pendingEnd = null;
                ShowResult(end);
            }
            else if (_client != null && _client.Phase == BattlePhase.None && !_resultShowing)
            {
                CloseBattle();
            }
        }

        private void HandleBattleEnd(BattleEndS2C end)
        {
            if (_playing)
            {
                // 最后一回合的表现还在播,播完再弹结算
                _pendingEnd = end;
                return;
            }
            ShowResult(end);
        }

        private void HandleChallengeInvite(Match.ChallengeInviteS2C invite)
        {
            _challengePopup?.Show(invite);
        }

        private void HandleChallengeResult(Match.ChallengeResultS2C result)
        {
            string message = result != null && result.Accepted
                ? "对方已应战,战斗即将开始"
                : "切磋被拒绝或已超时";
            ShowToast(message);
            _queuePanel?.SetStatus(message);
        }

        private void HandleQueueStatus(Match.GetQueueStatusResponse status)
        {
            _queuePanel?.ApplyStatus(status);
        }

        private void HandleClientError(string message)
        {
            ShowToast($"错误:{message}", true);
            if (_queuePanel != null && _queuePanel.IsVisible)
                _queuePanel.SetStatus(message);
        }

        private void HandleDisconnected()
        {
            // 断线:战斗态整体作废(重连后由 NotifyBattleReconnect → RequestState 恢复)
            _pendingEnd = null;
            _resultShowing = false;
            if (_playCo != null) { StopCoroutine(_playCo); _playCo = null; }
            _playing = false;
            _battleScreen?.AbortPlayback();
            CloseBattle();
            _resultPanel?.Hide();
            _queuePanel?.SetQueueing(false);
            _queuePanel?.Hide();
            _challengePopup?.HideSilently();
            RefreshModalDim();
        }

        private void HandleScreenChanged(IScreen screen)
        {
            // FairyGUI 兼容模式:只有场景屏亮着才显示战斗入口;切屏时收起悬浮面板
            s_sceneScreenActive = screen is Screens.SceneV3Screen;
            if (!s_sceneScreenActive) HideTransientPanels();
        }

        // ── 战斗屏开关 ──────────────────────────────────────

        private void EnsureBattleOpen()
        {
            if (_battleOpen) return;
            var state = _client?.State;
            if (state == null)
            {
                _client?.RequestState(); // 重连兜底补拉
                return;
            }
            OpenBattle(state);
        }

        private void OpenBattle(BattleStateS2C state)
        {
            if (state == null || _battleScreen == null) return;
            _battleOpen = true;
            _battleLayerGo.SetActive(true);
            _battleScreen.Open(state, _client?.MyPlayerId ?? 0);
            _battleScreen.OnPhaseChanged(_client?.Phase ?? BattlePhase.None);
            _queuePanel?.Hide();
        }

        private void CloseBattle()
        {
            if (!_battleOpen && (_battleLayerGo == null || !_battleLayerGo.activeSelf)) return;
            _battleOpen = false;
            _battleScreen?.Close();
            if (_battleLayerGo != null) _battleLayerGo.SetActive(false);
        }

        private void ShowResult(BattleEndS2C end)
        {
            _resultShowing = true;
            _resultPanel?.Show(end, _battleScreen?.MyTeamIndex ?? 0);
            RefreshModalDim();
        }

        private void OnResultConfirmed()
        {
            _resultShowing = false;
            _resultPanel?.Hide();
            RefreshModalDim();
            CloseBattle();
        }

        // ── HUD ─────────────────────────────────────────────

        private void OnEntryClicked()
        {
            if (_queuePanel == null) return;
            if (!_queuePanel.IsVisible && _client != null)
                _queuePanel.SetQueueing(_client.Phase == BattlePhase.Queued);
            _queuePanel.Toggle();
        }

        private void RefreshEntryVisibility()
        {
            if (_entryButton == null) return;
            bool inGame = _app != null && _app.GameClient != null && _app.GameClient.InGame;
            bool visible = _clientBound && inGame && !_battleOpen;
            // FairyGUI 兼容模式(Router 存在)下,仅场景屏亮着时显示
            if (_app != null && _app.Router != null) visible = visible && s_sceneScreenActive;
            _entryButton.SetVisible(visible);
            if (!visible && !inGame) _queuePanel?.Hide();
        }

        private void HideTransientPanels()
        {
            _queuePanel?.Hide();
            _challengePopup?.HideSilently();
            RefreshModalDim();
        }

        /// <summary>模态遮罩 = 挑战弹窗或结算面板任一可见。</summary>
        public void RefreshModalDim()
        {
            if (_modalDim == null) return;
            bool visible = (_challengePopup != null && _challengePopup.IsVisible) || _resultShowing;
            if (_modalDim.gameObject.activeSelf != visible)
                _modalDim.gameObject.SetActive(visible);
        }

        // ── toast ───────────────────────────────────────────

        public void ShowToast(string message, bool isError = false)
        {
            if (_toastText == null) return;
            _toastText.text = message ?? string.Empty;
            var color = isError ? BattleUiStyle.DamageText : QdaoUguiTheme.Cream;
            color.a = 1f;
            _toastText.color = color;
            if (_toastCo != null) StopCoroutine(_toastCo);
            _toastCo = StartCoroutine(CoToast());
        }

        private IEnumerator CoToast()
        {
            yield return new WaitForSecondsRealtime(2.2f);
            const float fade = 0.5f;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < fade)
            {
                if (_toastText == null) yield break;
                var color = _toastText.color;
                color.a = 1f - (Time.realtimeSinceStartup - start) / fade;
                _toastText.color = color;
                yield return null;
            }
            if (_toastText != null) _toastText.text = string.Empty;
            _toastCo = null;
        }
    }
}
