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
    ///   - OnTurnResult 事件流播完后调用 AckTurnPlayed();
    ///   - 观战(SpectateClient):入口面板 + 复用 BattleScreen 的只读模式;
    ///     观战回合播放不 Ack(只读流无该契约),新回合直接抢占旧播放。
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
        private SpectateClient _spectate;
        private bool _spectateBound;
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
        private UiTextButton _spectateEntryButton;
        private BattleQueuePanel _queuePanel;
        private SpectatePanel _spectatePanel;
        private BattleChallengePopup _challengePopup;
        private BattleScreen _battleScreen;
        private BattleResultPanel _resultPanel;

        private bool _battleOpen;
        private bool _playing;
        private bool _resultShowing;
        private BattleEndS2C _pendingEnd;
        private Coroutine _playCo;
        private Coroutine _toastCo;

        // 观战屏状态(与参战流程分离:两条播放链互不复用协程句柄)
        private bool _spectateOpen;
        private bool _spectatePlaying;
        private Coroutine _spectatePlayCo;
        private TurnResultS2C _spectatePlayingResult; // 播放被新回合抢占时用其 State 收尾
        private SpectateEndS2C _pendingSpectateEnd;

        /// <summary>供子面板取 BattleClient(可能为 null:NET 路尚未初始化)。</summary>
        public BattleClient Client => _client;

        /// <summary>供子面板取 SpectateClient(可能为 null:NET 路尚未初始化)。</summary>
        public SpectateClient Spectate => _spectate;

        /// <summary>战斗层(参战屏或观战屏)正占着屏幕。属性 UI 据此隐藏入口:
        /// 战斗中服务端本来就拒绝改属性(InBattleComp),入口先行隐藏免得点了必失败。</summary>
        public bool IsBattleLayerVisible => _battleOpen || _spectateOpen;

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
            // (观战屏开着时不抢:观战与排队/战斗由服务端 D11 互斥,不该同时发生)
            if (_clientBound && !_battleOpen && !_spectateOpen && _client.State != null &&
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
            UnbindSpectate();
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
            if (!_spectateBound)
            {
                var spectate = ResolveSpectateClient();
                if (spectate != null)
                {
                    _spectate = spectate;
                    BindSpectate();
                    _spectateBound = true;
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

        /// <summary>解析 SpectateClient 单例(挂接方式照 ResolveBattleClient)。</summary>
        private static SpectateClient ResolveSpectateClient() => SpectateClient.Instance;

        private void BindClient()
        {
            _client.OnPhaseChanged += HandlePhaseChanged;
            _client.OnBattleStart += HandleBattleStart;
            _client.OnTurnResult += HandleTurnResult;
            _client.OnBattleEnd += HandleBattleEnd;
            _client.OnChallengeInvite += HandleChallengeInvite;
            _client.OnChallengeResult += HandleChallengeResult;
            _client.OnQueueStatus += HandleQueueStatus;
            _client.OnAutoStateChanged += HandleAutoStateChanged;
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
            _client.OnAutoStateChanged -= HandleAutoStateChanged;
            _client.OnError -= HandleClientError;
            _clientBound = false;
        }

        private void BindSpectate()
        {
            _spectate.OnPhaseChanged += HandleSpectatePhaseChanged;
            _spectate.OnSpectateState += HandleSpectateState;
            _spectate.OnSpectateTurn += HandleSpectateTurn;
            _spectate.OnSpectateEnd += HandleSpectateEnd;
            _spectate.OnListUpdated += HandleSpectateList;
            _spectate.OnError += HandleSpectateError;
        }

        private void UnbindSpectate()
        {
            if (!_spectateBound || _spectate == null) return;
            _spectate.OnPhaseChanged -= HandleSpectatePhaseChanged;
            _spectate.OnSpectateState -= HandleSpectateState;
            _spectate.OnSpectateTurn -= HandleSpectateTurn;
            _spectate.OnSpectateEnd -= HandleSpectateEnd;
            _spectate.OnListUpdated -= HandleSpectateList;
            _spectate.OnError -= HandleSpectateError;
            _spectateBound = false;
        }

        // ── Canvas 构建(缩放参数与 QdaoUguiRuntime 保持一致:Expand) ──

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
            // 战斗屏 20 个单位常驻呼吸缩放:pixelPerfect 会把 ±2% 缩放吸附成整像素台阶,
            // 且每个动画元素都要做像素吸附;Unity 文档也不建议对动画 UI 开启
            canvas.pixelPerfect = false;

            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            // Expand:2560×1080 设计面在任何更窄比例(1920×1080 / 1920×1200 / 手机 19.5:9)下都不裁横向,
            // 只在上下多出空间(BattleUiLayout.VisibleDesignRect);右下命令环/取消自动/角色卡等按绝对
            // 设计坐标铺到 x=2520 的控件因此始终可见。MatchWidthOrHeight 0.5 在 1920×1080 会把两侧各裁 171px
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
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

            _spectateEntryButton = BattleUiWidgets.CreateTextButton("SpectateEntry", _hudRoot,
                2350f, 296f, 150f, 70f, "观战", 26f,
                BattleUiStyle.ButtonPlate, BattleUiStyle.ButtonText);
            _spectateEntryButton.Button.onClick.AddListener(OnSpectateEntryClicked);
            _spectateEntryButton.SetVisible(false);

            _queuePanel = new BattleQueuePanel(this, _hudRoot);
            _spectatePanel = new SpectatePanel(this, _hudRoot);
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

        private void HandleAutoStateChanged(bool isAuto)
        {
            if (_battleOpen) _battleScreen?.OnAutoStateChanged(isAuto);
        }

        // ── SpectateClient 事件 ─────────────────────────────

        private void HandleSpectatePhaseChanged(SpectatePhase phase)
        {
            switch (phase)
            {
                case SpectatePhase.Requesting:
                    _spectatePanel?.SetStatus("正在进入观战…");
                    break;
                case SpectatePhase.Watching:
                    _spectatePanel?.Hide();
                    break;
                case SpectatePhase.None:
                    // 主动退出/首帧超时/断线都收敛到 None:观战屏兜底关闭。
                    // 正常结束路径(Ended → AckEnd → None)此时屏已在 FinishSpectate 关过,幂等。
                    AbortSpectatePlayback();
                    CloseSpectate();
                    break;
            }
        }

        private void HandleSpectateState(SpectateStateS2C ev)
        {
            if (ev?.State == null) return;
            bool wasOpen = _spectateOpen;
            EnsureSpectateOpen(ev.State, ev.ObserverCount);
            if (wasOpen && _spectateOpen)
            {
                // 补帧(重发的全量快照):不重开屏,直接全量刷新
                _battleScreen.ApplyState(ev.State);
                _battleScreen.SetObserverCount(ev.ObserverCount);
            }
        }

        private void HandleSpectateTurn(TurnResultS2C result)
        {
            if (result == null) return;
            // 竞速兜底:首帧事件丢失但回合流已到,用 result.State 开屏(照 HandleTurnResult 惯例)
            if (!_spectateOpen && result.State != null)
                EnsureSpectateOpen(result.State, _spectate?.ObserverCount ?? 0);
            if (!_spectateOpen) return;

            // 观战流无 Ack 契约,服务端不等播放:新回合抢占旧播放,先用旧回合权威 State 收尾
            if (_spectatePlayCo != null)
            {
                StopCoroutine(_spectatePlayCo);
                _spectatePlayCo = null;
                _battleScreen.AbortPlayback();
                if (_spectatePlayingResult?.State != null)
                    _battleScreen.ApplyState(_spectatePlayingResult.State);
            }
            _spectatePlayingResult = result;
            _spectatePlayCo = StartCoroutine(CoPlaySpectateTurn(result));
        }

        private IEnumerator CoPlaySpectateTurn(TurnResultS2C result)
        {
            _spectatePlaying = true;
            yield return _battleScreen.CoPlayTurn(result);
            _spectatePlaying = false;
            _spectatePlayCo = null;
            _spectatePlayingResult = null;

            if (_pendingSpectateEnd != null)
            {
                var end = _pendingSpectateEnd;
                _pendingSpectateEnd = null;
                FinishSpectate(end);
            }
        }

        private void HandleSpectateEnd(SpectateEndS2C end)
        {
            if (_spectatePlaying)
            {
                // 最后一回合表现还在播,播完再收尾
                _pendingSpectateEnd = end;
                return;
            }
            FinishSpectate(end);
        }

        private void FinishSpectate(SpectateEndS2C end)
        {
            ShowToast(DescribeSpectateEnd(end));
            CloseSpectate();
            _spectate?.AckEnd(); // 契约:UI 收尾后必须 Ack,Ended → None
        }

        private static string DescribeSpectateEnd(SpectateEndS2C end)
        {
            if (end == null) return "观战结束";
            switch (end.Reason)
            {
                case eSpectateEndReason.SpectateEndRemoved:
                    return "观战结束:你已进入排队/战斗,被移出观战";
                case eSpectateEndReason.SpectateEndBattleAborted:
                    return "观战结束:战斗已作废";
                default:
                    // 观战屏固定 team 0 在下排(OpenSpectate 契约),按此翻译胜负方
                    switch (end.Outcome)
                    {
                        case eBattleOutcome.BattleOutcomeSideAWin: return "观战结束:下方队伍获胜";
                        case eBattleOutcome.BattleOutcomeSideBWin: return "观战结束:上方队伍获胜";
                        case eBattleOutcome.BattleOutcomeDraw: return "观战结束:平局";
                        default: return "观战结束";
                    }
            }
        }

        private void HandleSpectateList(Match.ListWatchableBattlesResponse resp)
        {
            _spectatePanel?.ApplyList(resp);
        }

        private void HandleSpectateError(string message)
        {
            ShowToast($"观战:{message}", true);
            if (_spectatePanel != null && _spectatePanel.IsVisible)
                _spectatePanel.SetStatus(message);
        }

        /// <summary>观战屏「退出观战」入口(BattleScreen 只依赖 BattleUiRoot,不直连 SpectateClient)。</summary>
        public void RequestStopSpectate()
        {
            // 本地立即回 None(SpectateClient 契约)→ HandleSpectatePhaseChanged 关屏
            _spectate?.StopWatch();
        }

        private void EnsureSpectateOpen(BattleStateS2C state, uint observerCount)
        {
            if (_spectateOpen || state == null || _battleScreen == null) return;
            if (_battleOpen) return; // 参战屏优先:迟到的观战帧宁可丢弃也不抢屏
            _spectateOpen = true;
            _spectatePanel?.Hide();
            _queuePanel?.Hide();
            _battleLayerGo.SetActive(true);
            _battleScreen.OpenSpectate(state, observerCount);
        }

        private void CloseSpectate()
        {
            _pendingSpectateEnd = null;
            if (!_spectateOpen) return;
            _spectateOpen = false;
            _battleScreen?.Close();
            // 战斗层与参战流程共用:仅参战屏也没占用时才熄灯
            if (_battleLayerGo != null && !_battleOpen) _battleLayerGo.SetActive(false);
        }

        private void AbortSpectatePlayback()
        {
            if (_spectatePlayCo != null) { StopCoroutine(_spectatePlayCo); _spectatePlayCo = null; }
            _spectatePlaying = false;
            _spectatePlayingResult = null;
            _pendingSpectateEnd = null;
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
            // 观战态同样作废(SpectateClient 断线自身也会回 None,此处是 UI 侧兜底)
            AbortSpectatePlayback();
            CloseSpectate();
            _spectatePanel?.Hide();
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
            // 极端时序兜底:参战开局必然晚于观战清退(D11 互斥 + Kafka 同 key 有序),
            // 观战屏若还开着先收掉,避免两条流程抢同一个 BattleScreen
            if (_spectateOpen)
            {
                AbortSpectatePlayback();
                CloseSpectate();
            }
            _battleOpen = true;
            _battleLayerGo.SetActive(true);
            _battleScreen.Open(state, _client?.MyPlayerId ?? 0);
            _battleScreen.OnPhaseChanged(_client?.Phase ?? BattlePhase.None);
            _queuePanel?.Hide();
        }

        private void CloseBattle()
        {
            // 战斗层被观战屏占用时,参战流程无屏可关(误关会拆掉观战画面)
            if (_spectateOpen && !_battleOpen) return;
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
            // 契约:结算面板收起后必须 Ack(连续战斗开着时 BattleClient 自动按记忆重排)
            _client?.AckBattleEnd();
        }

        // ── HUD ─────────────────────────────────────────────

        private void OnEntryClicked()
        {
            if (_queuePanel == null) return;
            if (!_queuePanel.IsVisible && _client != null)
                _queuePanel.SetQueueing(_client.Phase == BattlePhase.Queued);
            _queuePanel.Toggle();
        }

        private void OnSpectateEntryClicked()
        {
            if (_spectatePanel == null) return;
            _spectatePanel.Toggle();
            if (_spectatePanel.IsVisible) _spectatePanel.RefreshList();
        }

        private void RefreshEntryVisibility()
        {
            if (_entryButton == null) return;
            bool inGame = _app != null && _app.GameClient != null && _app.GameClient.InGame;
            bool spectating = _spectateBound && _spectate.Phase != SpectatePhase.None;
            bool visible = _clientBound && inGame && !_battleOpen && !_spectateOpen && !spectating;
            // FairyGUI 兼容模式(Router 存在)下,仅场景屏亮着时显示
            if (_app != null && _app.Router != null) visible = visible && s_sceneScreenActive;
            _entryButton.SetVisible(visible);
            // 观战入口在「战斗」条件之上再要求不在排队/战斗任何相位
            // (D11 服务端互斥,入口先行隐藏避免必败请求)
            bool spectateVisible = visible && _spectateBound && _client.Phase == BattlePhase.None;
            _spectateEntryButton?.SetVisible(spectateVisible);
            if (!visible && !inGame)
            {
                _queuePanel?.Hide();
                _spectatePanel?.Hide();
            }
        }

        private void HideTransientPanels()
        {
            _queuePanel?.Hide();
            _spectatePanel?.Hide();
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
