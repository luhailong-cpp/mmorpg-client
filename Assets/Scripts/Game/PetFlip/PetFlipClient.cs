using System;

namespace MmorpgClient.Game.PetFlip
{
    /// <summary>
    /// 灵宠翻牌本地小游戏主控制器（纯 C#，零 UnityEngine 依赖）。
    ///
    /// 职责：
    /// 1. 管理当前进程内唯一活跃盘面（挂接在长寿命 <see cref="GameClient"/> 上，面板销毁重建不丢盘、不开多盘）；
    /// 2. 驱动本机设备级最佳次数的读取、比较、持久化与保存失败降级状态；
    /// 3. 协调暂关面板、切后台、临时断线、明确登出、换角色、切服以及战斗/观战/加载/强制弹窗优先收起；
    /// 4. 通过 <c>viewToken</c>、<c>expectedRoundId</c> 与同步状态机防御重复点击、迟到回调与面板重建。
    /// </summary>
    public sealed class PetFlipClient : IDisposable
    {
        /// <summary>生产单例挂接（由 GameClient 构造时通过 Attach 登记，测试直接构造实例不污染此字段）。</summary>
        public static PetFlipClient Instance { get; private set; }

        private readonly IPetFlipBestRecordStore _store;
        private readonly Func<SpiritPetCardPattern[]> _deckGenerator;
        private readonly Random _random;

        private int? _bestFlipCount;
        private int? _persistedBestFlipCount;
        private PetFlipRecordPersistenceStatus _recordPersistenceStatus;

        private PetFlipSessionKey _currentSession;
        private PetFlipSessionKey _boardOwnerSession;
        private PetFlipBoardModel _board;
        private PetFlipCompletionResult _completionResult;
        private ulong _roundId;
        private ulong _nextRoundId;
        private ulong _revision;

        // 宿主（GameClient）驱动的中断位与 UI/外部驱动的中断位分开维护，避免每帧同步覆盖 UI 强制弹窗或加载遮罩。
        private PetFlipInterruptFlags _hostInterrupts;
        private PetFlipInterruptFlags _externalInterrupts;

        private ulong _activeViewToken;
        private ulong _nextViewToken;
        private Action<PetFlipSnapshot> _activeViewCallback;
        private bool _isDisposed;

        /// <summary>任意可见盘面、次数、暂停态、会话或本机记录变化时触发（载荷为不可变快照）。</summary>
        public event Action<PetFlipSnapshot> OnSnapshotChanged;

        /// <summary>配齐 6 对完成一局时触发，每局严格只触发一次。</summary>
        public event Action<PetFlipCompletionResult> OnRoundCompleted;

        public bool IsDisposed => _isDisposed;

        /// <summary>正式构造函数：始终使用随机洗牌；store 为空时退回到纯内存存储。</summary>
        public PetFlipClient(IPetFlipBestRecordStore store = null, Random random = null)
            : this(store, random, null)
        {
        }

        private PetFlipClient(
            IPetFlipBestRecordStore store,
            Random random,
            Func<SpiritPetCardPattern[]> deckGeneratorOverride)
        {
            _store = store ?? new InMemoryPetFlipBestRecordStore();
            _random = random ?? new Random();
            _deckGenerator = deckGeneratorOverride ?? (() => PetFlipDeckFactory.CreateShuffledDeck(_random));

            LoadInitialBestRecord();
        }

        /// <summary>生产入口：创建实例并登记为全局 <see cref="Instance"/>（由 GameClient 构造调用）。</summary>
        public static PetFlipClient Attach(IPetFlipBestRecordStore store = null)
        {
            var client = new PetFlipClient(store);
            Instance = client;
            return client;
        }

        /// <summary>仅用于自动化测试：注入固定或可控牌组生成器，不污染静态单例。</summary>
        public static PetFlipClient CreateForTesting(
            IPetFlipBestRecordStore store,
            Func<SpiritPetCardPattern[]> deckGenerator)
        {
            if (deckGenerator == null)
                throw new ArgumentNullException(nameof(deckGenerator));

            return new PetFlipClient(store, null, deckGenerator);
        }

        // ── 面板视图绑定（防面板重建泄漏与迟到回调） ────────────────────────

        /// <summary>
        /// 绑定当前 UI 面板实例并返回单调递增的 <c>viewToken</c>。
        /// 若旧面板尚未销毁就被重建，新绑定会立即作废旧令牌并替换回调，避免双面板或旧回调窜扰。
        /// </summary>
        public ulong AttachView(Action<PetFlipSnapshot> onSnapshotChanged = null)
        {
            if (_isDisposed)
                return 0;

            _activeViewToken = ++_nextViewToken;
            _activeViewCallback = onSnapshotChanged;
            return _activeViewToken;
        }

        /// <summary>
        /// 解绑 UI 面板（供面板 OnDestroy / OnDisable 调用）。
        /// 仅解绑并按需标记 <see cref="PetFlipInterruptFlags.PanelClosed"/>，保留当前进程内盘面供重建后继续。
        /// 若传入已过期的旧 <c>viewToken</c>（乱序重建场景），安全忽略。
        /// </summary>
        public void DetachView(ulong viewToken, bool pausePanel = true)
        {
            if (_isDisposed || viewToken == 0 || viewToken != _activeViewToken)
                return;

            _activeViewToken = 0;
            _activeViewCallback = null;

            if (pausePanel && _board != null)
            {
                PetFlipInterruptFlags previous = EffectiveInterrupts;
                _externalInterrupts |= PetFlipInterruptFlags.PanelClosed;
                if (EffectiveInterrupts != previous)
                {
                    _revision++;
                    EmitSnapshotChanged(BuildSnapshot());
                }
            }
        }

        /// <summary>供 UI 动画或协程回调在触碰控件前校验自身视图令牌是否仍有效。</summary>
        public bool IsActiveView(ulong viewToken)
            => !_isDisposed && viewToken != 0 && viewToken == _activeViewToken;

        // ── 核心 UI 调用面 ────────────────────────────────────────────────────

        /// <summary>获取当前完整不可变盘面、次数、终局结果与本机纪录状态。</summary>
        public PetFlipSnapshot GetSnapshot() => BuildSnapshot();

        /// <summary>
        /// 开始或恢复一局：
        /// - 无盘时为当前登录角色随机开新盘；
        /// - 有进行中/暂停盘时清除 <see cref="PetFlipInterruptFlags.PanelClosed"/> 并恢复原盘；
        /// - 有已完成盘时恢复终局结果展示，不重复算分、不自动洗牌重开。
        /// </summary>
        public PetFlipActionResult StartOrResume(ulong? viewToken = null)
        {
            if (TryRejectCommonGuard(viewToken, out PetFlipRejectReason guardReject))
                return PetFlipActionResult.Rejected(guardReject, BuildSnapshot());

            PetFlipInterruptFlags nonPanelInterrupts = EffectiveInterrupts & ~PetFlipInterruptFlags.PanelClosed;
            if (nonPanelInterrupts != PetFlipInterruptFlags.None)
                return PetFlipActionResult.Rejected(PetFlipRejectReason.BlockedByInterrupt, BuildSnapshot());

            if (!_currentSession.IsValid)
                return PetFlipActionResult.Rejected(PetFlipRejectReason.NoActiveSession, BuildSnapshot());

            bool panelWasClosed = (_externalInterrupts & PetFlipInterruptFlags.PanelClosed) != 0;
            _externalInterrupts &= ~PetFlipInterruptFlags.PanelClosed;

            if (_board != null && _boardOwnerSession != _currentSession)
                ClearBoardStateOnly();

            if (_board == null)
            {
                DealFreshBoard();
                _revision++;
                PetFlipSnapshot started = BuildSnapshot();
                EmitSnapshotChanged(started);
                return PetFlipActionResult.Lifecycle(PetFlipActionOutcome.StartedNewRound, started);
            }

            if (panelWasClosed)
                _revision++;

            PetFlipSnapshot resumed = BuildSnapshot();
            if (panelWasClosed)
                EmitSnapshotChanged(resumed);

            return PetFlipActionResult.Lifecycle(PetFlipActionOutcome.ResumedRound, resumed);
        }

        /// <summary>点击明确牌位（0..11）。所有配对、计次、盖回与终局结算同步落定，不依赖 UI 动画结束。</summary>
        public PetFlipActionResult FlipCard(int slotIndex, ulong? expectedRoundId = null, ulong? viewToken = null)
        {
            if (TryRejectCommonGuard(viewToken, out PetFlipRejectReason guardReject))
                return PetFlipActionResult.Rejected(guardReject, BuildSnapshot());

            PetFlipInterruptFlags activeInterrupts = EffectiveInterrupts;
            if ((activeInterrupts & PetFlipConstants.EntryBlockedMask) != 0)
                return PetFlipActionResult.Rejected(PetFlipRejectReason.BlockedByInterrupt, BuildSnapshot());

            if (!_currentSession.IsValid)
                return PetFlipActionResult.Rejected(PetFlipRejectReason.NoActiveSession, BuildSnapshot());

            if (_board == null)
                return PetFlipActionResult.Rejected(PetFlipRejectReason.NoActiveRound, BuildSnapshot());

            if (expectedRoundId.HasValue && expectedRoundId.Value != _roundId)
                return PetFlipActionResult.Rejected(PetFlipRejectReason.StaleRound, BuildSnapshot());

            if (activeInterrupts != PetFlipInterruptFlags.None)
                return PetFlipActionResult.Rejected(PetFlipRejectReason.GamePaused, BuildSnapshot());

            if (_board.IsCompleted)
                return PetFlipActionResult.Rejected(PetFlipRejectReason.RoundAlreadyCompleted, BuildSnapshot());

            PetFlipBoardStepResult step = _board.FlipCard(slotIndex);
            if (!step.Accepted)
                return PetFlipActionResult.Rejected(step.RejectReason, BuildSnapshot());

            PetFlipCompletionResult newlyCompleted = null;
            if (step.Outcome == PetFlipActionOutcome.RoundCompleted && _completionResult == null)
            {
                newlyCompleted = SettleCompletedRoundOnce();
                _completionResult = newlyCompleted;
            }

            _revision++;
            PetFlipSnapshot snapshot = BuildSnapshot();
            EmitSnapshotChanged(snapshot);
            if (newlyCompleted != null)
                OnRoundCompleted?.Invoke(newlyCompleted);

            return new PetFlipActionResult(
                step.Outcome,
                PetFlipRejectReason.None,
                snapshot,
                step.FlippedSlotIndex,
                step.FirstHandSlotIndex,
                step.SecondHandSlotIndex,
                step.CoveredFirstSlotIndex,
                step.CoveredSecondSlotIndex,
                step.FlippedPattern,
                step.MatchedPattern);
        }

        /// <summary>仅在当前局已完成（配齐 6 对）且无中断时生成新盘；盘中禁止洗牌重开。</summary>
        public PetFlipActionResult StartNewRound(ulong? expectedRoundId = null, ulong? viewToken = null)
        {
            if (TryRejectCommonGuard(viewToken, out PetFlipRejectReason guardReject))
                return PetFlipActionResult.Rejected(guardReject, BuildSnapshot());

            PetFlipInterruptFlags activeInterrupts = EffectiveInterrupts;
            if ((activeInterrupts & PetFlipConstants.EntryBlockedMask) != 0)
                return PetFlipActionResult.Rejected(PetFlipRejectReason.BlockedByInterrupt, BuildSnapshot());

            if (!_currentSession.IsValid)
                return PetFlipActionResult.Rejected(PetFlipRejectReason.NoActiveSession, BuildSnapshot());

            if (activeInterrupts != PetFlipInterruptFlags.None)
                return PetFlipActionResult.Rejected(PetFlipRejectReason.GamePaused, BuildSnapshot());

            if (_board == null)
                return PetFlipActionResult.Rejected(PetFlipRejectReason.NoActiveRound, BuildSnapshot());

            if (expectedRoundId.HasValue && expectedRoundId.Value != _roundId)
                return PetFlipActionResult.Rejected(PetFlipRejectReason.StaleRound, BuildSnapshot());

            if (!_board.IsCompleted)
                return PetFlipActionResult.Rejected(PetFlipRejectReason.RoundNotCompleted, BuildSnapshot());

            DealFreshBoard();
            _revision++;
            PetFlipSnapshot snapshot = BuildSnapshot();
            EmitSnapshotChanged(snapshot);
            return PetFlipActionResult.Lifecycle(PetFlipActionOutcome.StartedNewRound, snapshot);
        }

        /// <summary>暂停并叠加指定中断原因（如暂关面板、切后台、战斗、观战、加载、强制弹窗）。</summary>
        public PetFlipActionResult Pause(PetFlipInterruptFlags reason, ulong? viewToken = null)
        {
            if (TryRejectCommonGuard(viewToken, out PetFlipRejectReason guardReject))
                return PetFlipActionResult.Rejected(guardReject, BuildSnapshot());

            if (reason == PetFlipInterruptFlags.None)
                return PetFlipActionResult.Lifecycle(PetFlipActionOutcome.Paused, BuildSnapshot());

            PetFlipInterruptFlags previous = EffectiveInterrupts;
            _externalInterrupts |= reason;
            if (EffectiveInterrupts != previous)
            {
                _revision++;
                PetFlipSnapshot changed = BuildSnapshot();
                EmitSnapshotChanged(changed);
                return PetFlipActionResult.Lifecycle(PetFlipActionOutcome.Paused, changed);
            }

            return PetFlipActionResult.Lifecycle(PetFlipActionOutcome.Paused, BuildSnapshot());
        }

        /// <summary>解除指定外部中断原因；仅当全部中断位清零且会话在线时恢复为可交互。</summary>
        public PetFlipActionResult Resume(PetFlipInterruptFlags reason, ulong? viewToken = null)
        {
            if (TryRejectCommonGuard(viewToken, out PetFlipRejectReason guardReject))
                return PetFlipActionResult.Rejected(guardReject, BuildSnapshot());

            if (reason == PetFlipInterruptFlags.None)
                return PetFlipActionResult.Lifecycle(PetFlipActionOutcome.Resumed, BuildSnapshot());

            PetFlipInterruptFlags previous = EffectiveInterrupts;
            _externalInterrupts &= ~reason;
            if (EffectiveInterrupts != previous)
            {
                _revision++;
                PetFlipSnapshot changed = BuildSnapshot();
                EmitSnapshotChanged(changed);
                return PetFlipActionResult.Lifecycle(PetFlipActionOutcome.Resumed, changed);
            }

            return PetFlipActionResult.Lifecycle(PetFlipActionOutcome.Resumed, BuildSnapshot());
        }

        /// <summary>幂等开关单个外部中断位（供 UI 同步强制弹窗、UI 级加载遮罩等状态）。</summary>
        public void SetInterrupt(PetFlipInterruptFlags flag, bool isActive)
        {
            if (_isDisposed || flag == PetFlipInterruptFlags.None)
                return;

            PetFlipInterruptFlags previous = EffectiveInterrupts;
            if (isActive)
                _externalInterrupts |= flag;
            else
                _externalInterrupts &= ~flag;

            if (EffectiveInterrupts != previous)
            {
                _revision++;
                EmitSnapshotChanged(BuildSnapshot());
            }
        }

        // ── 宿主与会话生命周期同步 ────────────────────────────────────────────

        /// <summary>
        /// 由 <see cref="GameClient.Tick"/> 每帧同步宿主状态（无变化时零堆分配）。
        /// 精确区分：同角色在线续盘、临时断线冻结、重定向/加载收起，以及换角色/切服清盘。
        /// </summary>
        public void SyncHostState(
            ulong playerId,
            uint zoneId,
            bool isInGameConnected,
            bool isReconnecting,
            bool isInBattle,
            bool isSpectating,
            bool isLoading)
        {
            if (_isDisposed)
                return;

            PetFlipInterruptFlags nextHostInterrupts = PetFlipInterruptFlags.None;
            if (isReconnecting) nextHostInterrupts |= PetFlipInterruptFlags.Disconnected;
            if (isInBattle) nextHostInterrupts |= PetFlipInterruptFlags.Battle;
            if (isSpectating) nextHostInterrupts |= PetFlipInterruptFlags.Spectate;
            if (isLoading) nextHostInterrupts |= PetFlipInterruptFlags.Loading;

            PetFlipSessionKey nextSession;
            bool shouldDiscardBoard = false;

            if (isInGameConnected && playerId != 0 && zoneId != 0)
            {
                nextSession = new PetFlipSessionKey(playerId, zoneId);
                nextHostInterrupts &= ~PetFlipInterruptFlags.Disconnected;
                if (_board != null && _boardOwnerSession != nextSession)
                    shouldDiscardBoard = true;
            }
            else if (isReconnecting || isLoading)
            {
                // 临时断线重连或切线迁移期间：暂失在线态，但保留 _board 与 _boardOwnerSession，等待落回核对。
                nextSession = default;
            }
            else
            {
                nextSession = default;
                if (_board != null)
                    shouldDiscardBoard = true;
            }

            if (nextSession == _currentSession
                && nextHostInterrupts == _hostInterrupts
                && !shouldDiscardBoard)
            {
                return;
            }

            _currentSession = nextSession;
            _hostInterrupts = nextHostInterrupts;
            if (shouldDiscardBoard)
            {
                ClearBoardStateOnly();
                _externalInterrupts &= ~PetFlipInterruptFlags.PanelClosed;
            }

            _revision++;
            EmitSnapshotChanged(BuildSnapshot());
        }

        /// <summary>通知临时断线：冻结操作，但保留当前角色未完成盘等待会话恢复。</summary>
        public void NotifyTemporaryDisconnect()
        {
            if (_isDisposed)
                return;

            PetFlipSessionKey previousSession = _currentSession;
            PetFlipInterruptFlags previousInterrupts = EffectiveInterrupts;
            _currentSession = default;
            _hostInterrupts |= PetFlipInterruptFlags.Disconnected;

            if (_currentSession != previousSession || EffectiveInterrupts != previousInterrupts)
            {
                _revision++;
                EmitSnapshotChanged(BuildSnapshot());
            }
        }

        /// <summary>
        /// 通知角色会话就绪/恢复：
        /// - 若与保留盘面的 (PlayerId, ZoneId) 相同：解除断线冻结并保留原盘；
        /// - 若切换了角色或区服：立即丢弃旧盘。
        /// </summary>
        public void NotifySessionRestored(ulong playerId, uint zoneId)
        {
            if (_isDisposed)
                return;

            if (playerId == 0 || zoneId == 0)
            {
                NotifyLoggedOut();
                return;
            }

            var nextSession = new PetFlipSessionKey(playerId, zoneId);
            PetFlipSessionKey previousSession = _currentSession;
            PetFlipInterruptFlags previousInterrupts = EffectiveInterrupts;
            bool boardDiscarded = false;

            if (_board != null && _boardOwnerSession != nextSession)
            {
                ClearBoardStateOnly();
                _externalInterrupts &= ~PetFlipInterruptFlags.PanelClosed;
                boardDiscarded = true;
            }

            _currentSession = nextSession;
            _hostInterrupts &= ~(PetFlipInterruptFlags.Disconnected | PetFlipInterruptFlags.Loading);
            _externalInterrupts &= ~PetFlipInterruptFlags.Disconnected;

            if (boardDiscarded || _currentSession != previousSession || EffectiveInterrupts != previousInterrupts)
            {
                _revision++;
                EmitSnapshotChanged(BuildSnapshot());
            }
        }

        /// <summary>通知明确登出、退出角色或重连失败回登录页：立即丢弃内存中盘面（不删除设备本机最佳）。</summary>
        public void NotifyLoggedOut()
        {
            if (_isDisposed)
                return;

            const PetFlipInterruptFlags sessionScopedExternal =
                PetFlipInterruptFlags.PanelClosed |
                PetFlipInterruptFlags.Disconnected |
                PetFlipInterruptFlags.Battle |
                PetFlipInterruptFlags.Spectate |
                PetFlipInterruptFlags.Loading;

            bool hadState = _board != null
                || _currentSession.IsValid
                || _hostInterrupts != PetFlipInterruptFlags.None
                || (_externalInterrupts & sessionScopedExternal) != PetFlipInterruptFlags.None;

            if (!hadState)
                return;

            ClearBoardStateOnly();
            _currentSession = default;
            _hostInterrupts = PetFlipInterruptFlags.None;
            _externalInterrupts &= ~sessionScopedExternal;
            _revision++;
            EmitSnapshotChanged(BuildSnapshot());
        }

        /// <summary>通知宿主切后台/回前台（由 GameClient.OnApplicationPause 转发）。</summary>
        public void NotifyApplicationPause(bool isPaused)
            => SetInterrupt(PetFlipInterruptFlags.ApplicationBackground, isPaused);

        /// <summary>销毁控制器（进程或 AppBootstrap 关闭时调用）：丢弃内存盘面与全部订阅，不删本机持久化纪录。</summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            ClearBoardStateOnly();
            _currentSession = default;
            _hostInterrupts = PetFlipInterruptFlags.None;
            _externalInterrupts = PetFlipInterruptFlags.None;
            _activeViewToken = 0;
            _activeViewCallback = null;
            OnSnapshotChanged = null;
            OnRoundCompleted = null;
            _revision++;

            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        // ── 内部辅助 ──────────────────────────────────────────────────────────

        private PetFlipInterruptFlags EffectiveInterrupts => _hostInterrupts | _externalInterrupts;

        private bool TryRejectCommonGuard(ulong? viewToken, out PetFlipRejectReason reason)
        {
            if (_isDisposed)
            {
                reason = PetFlipRejectReason.Disposed;
                return true;
            }

            if (viewToken.HasValue && (viewToken.Value == 0 || viewToken.Value != _activeViewToken))
            {
                reason = PetFlipRejectReason.StaleView;
                return true;
            }

            reason = PetFlipRejectReason.None;
            return false;
        }

        private void DealFreshBoard()
        {
            SpiritPetCardPattern[] deck = _deckGenerator();
            _board = new PetFlipBoardModel(deck);
            _roundId = ++_nextRoundId;
            _boardOwnerSession = _currentSession;
            _completionResult = null;
        }

        private void ClearBoardStateOnly()
        {
            _board = null;
            _boardOwnerSession = default;
            _completionResult = null;
            _roundId = 0;
        }

        private void LoadInitialBestRecord()
        {
            try
            {
                if (_store.TryReadRaw(out string rawValue)
                    && PetFlipBestRecordCodec.TryDecode(rawValue, out int bestFlips))
                {
                    _bestFlipCount = bestFlips;
                    _persistedBestFlipCount = bestFlips;
                    _recordPersistenceStatus = PetFlipRecordPersistenceStatus.Saved;
                    return;
                }
            }
            catch (Exception)
            {
                // 存储读取异常按暂无纪录兜底，不阻止游玩。
            }

            _bestFlipCount = null;
            _persistedBestFlipCount = null;
            _recordPersistenceStatus = PetFlipRecordPersistenceStatus.NoRecord;
        }

        private PetFlipCompletionResult SettleCompletedRoundOnce()
        {
            int flips = _board.FlipCount;
            int? previousBest = _bestFlipCount;
            PetFlipRecordComparison comparison;

            if (!previousBest.HasValue)
            {
                comparison = PetFlipRecordComparison.FirstRecord;
                _bestFlipCount = flips;
                PersistNewBest(flips);
            }
            else if (flips < previousBest.Value)
            {
                comparison = PetFlipRecordComparison.NewBest;
                _bestFlipCount = flips;
                PersistNewBest(flips);
            }
            else if (flips == previousBest.Value)
            {
                comparison = PetFlipRecordComparison.TiedBest;
            }
            else
            {
                comparison = PetFlipRecordComparison.NormalCompletion;
            }

            return new PetFlipCompletionResult(
                _roundId,
                flips,
                _board.MatchedPairCount,
                previousBest,
                _bestFlipCount.Value,
                comparison,
                _recordPersistenceStatus);
        }

        private void PersistNewBest(int newBestFlips)
        {
            bool saved;
            try
            {
                string encoded = PetFlipBestRecordCodec.Encode(newBestFlips);
                saved = _store.TryWriteRaw(encoded);
            }
            catch (Exception)
            {
                saved = false;
            }

            if (saved)
            {
                _persistedBestFlipCount = newBestFlips;
                _recordPersistenceStatus = PetFlipRecordPersistenceStatus.Saved;
            }
            else
            {
                _recordPersistenceStatus = PetFlipRecordPersistenceStatus.Unsaved;
            }
        }

        private PetFlipSnapshot BuildSnapshot()
        {
            PetFlipInterruptFlags activeInterrupts = EffectiveInterrupts;
            PetFlipRoundPhase phase = DerivePhase(activeInterrupts);
            bool hasActiveSession = _currentSession.IsValid
                && (activeInterrupts & PetFlipInterruptFlags.Disconnected) == 0
                && !_isDisposed;
            bool boardInteractive = phase == PetFlipRoundPhase.Playing
                && activeInterrupts == PetFlipInterruptFlags.None
                && hasActiveSession;

            var localRecord = new PetFlipLocalRecordSnapshot(
                _bestFlipCount,
                _persistedBestFlipCount,
                _recordPersistenceStatus);

            if (_board == null)
            {
                return new PetFlipSnapshot(
                    _revision,
                    0,
                    _currentSession,
                    default,
                    phase,
                    PetFlipHandState.None,
                    activeInterrupts,
                    0,
                    0,
                    -1,
                    -1,
                    Array.Empty<PetFlipCardSnapshot>(),
                    localRecord,
                    null);
            }

            return new PetFlipSnapshot(
                _revision,
                _roundId,
                _currentSession,
                _boardOwnerSession,
                phase,
                _board.HandState,
                activeInterrupts,
                _board.FlipCount,
                _board.MatchedPairCount,
                _board.FirstSelectedSlotIndex,
                _board.SecondSelectedSlotIndex,
                _board.BuildCardSnapshots(boardInteractive),
                localRecord,
                _completionResult);
        }

        private PetFlipRoundPhase DerivePhase(PetFlipInterruptFlags activeInterrupts)
        {
            if (_isDisposed)
                return PetFlipRoundPhase.Disposed;

            if (_board == null)
                return PetFlipRoundPhase.Idle;

            if (_board.IsCompleted)
                return PetFlipRoundPhase.Completed;

            if (activeInterrupts != PetFlipInterruptFlags.None || !_currentSession.IsValid)
                return PetFlipRoundPhase.Paused;

            return PetFlipRoundPhase.Playing;
        }

        private void EmitSnapshotChanged(PetFlipSnapshot snapshot)
        {
            _activeViewCallback?.Invoke(snapshot);
            OnSnapshotChanged?.Invoke(snapshot);
        }
    }
}
