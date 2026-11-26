using System;
using System.Collections.Generic;

namespace MmorpgClient.Game.PetFlip
{
    /// <summary>灵宠翻牌核心常量、难度规格与中断掩码。</summary>
    public static class PetFlipConstants
    {
        public const int BoardCardCount = 12;
        public const int PairCount = 6;
        public const int MinimumPossibleFlips = PairCount;

        public const int AdvancedBoardCardCount = 16;
        public const int AdvancedPairCount = 8;
        public const int MaxBoardCardCount = AdvancedBoardCardCount;
        public const int MaxPairCount = AdvancedPairCount;
        public const int InitialSpiritEyeCharges = 1;

        /// <summary>优先级高于翻牌页面的系统中断（战斗、观战、加载、强制弹窗）。</summary>
        public const PetFlipInterruptFlags PriorityInterruptMask =
            PetFlipInterruptFlags.Battle |
            PetFlipInterruptFlags.Spectate |
            PetFlipInterruptFlags.Loading |
            PetFlipInterruptFlags.ForcedModal;

        /// <summary>触发翻牌面板暂停并收起的中断集合。</summary>
        public const PetFlipInterruptFlags CollapsePanelMask =
            PetFlipInterruptFlags.PanelClosed | PriorityInterruptMask;

        /// <summary>阻止打开、恢复或翻牌的阻塞中断集合。</summary>
        public const PetFlipInterruptFlags EntryBlockedMask =
            PetFlipInterruptFlags.Disconnected | PriorityInterruptMask;

        public static int GetPairCount(PetFlipDifficulty difficulty)
            => difficulty == PetFlipDifficulty.Advanced16 ? AdvancedPairCount : PairCount;

        public static int GetCardCount(PetFlipDifficulty difficulty)
            => GetPairCount(difficulty) * 2;

        public static int GetMinimumFlips(PetFlipDifficulty difficulty)
            => GetPairCount(difficulty);
    }

    /// <summary>难度档位（纯本地，各档独立记录本机最佳）。</summary>
    public enum PetFlipDifficulty
    {
        /// <summary>轻松：12 张牌（6 对），横屏 4×3，竖屏 3×4。</summary>
        Relaxed12 = 0,
        /// <summary>进阶：16 张牌（8 对），横竖屏均 4×4。</summary>
        Advanced16 = 1
    }

    /// <summary>玩法模式（纯本地免费，各模式独立记录本机最佳，不改服务端经济）。</summary>
    public enum PetFlipGameMode
    {
        /// <summary>经典自由：每局随机洗牌，纯记忆翻牌。</summary>
        Classic = 0,
        /// <summary>今日灵签：按本地日期确定性洗牌，同日同盘，独立记录今日最佳。</summary>
        DailyChallenge = 1,
        /// <summary>灵目趣味：每局赠送 1 次免费“灵目窥牌”（翻看 1 张暗牌不计次），独立记录本机最佳。</summary>
        SpiritEye = 2
    }

    /// <summary>
    /// 8 种灵宠卡牌图案标识（轻松模式用 A..F，进阶模式用 A..H），独立于真实宝宝养成配置表 ID。
    /// UI 与美术将枚举值映射到对应的目录头像资源。
    /// </summary>
    public enum SpiritPetCardPattern
    {
        A = 0,
        B = 1,
        C = 2,
        D = 3,
        E = 4,
        F = 5,
        G = 6,
        H = 7
    }

    /// <summary>单张卡牌的可见翻牌状态。</summary>
    public enum PetFlipCardState
    {
        /// <summary>背面朝上，对外不暴露图案。</summary>
        FaceDown = 0,
        /// <summary>本手翻开、失配停留或灵目窥看亮起。</summary>
        FaceUp = 1,
        /// <summary>已配对成功，留在原位常亮。</summary>
        Matched = 2
    }

    /// <summary>灵宠翻牌当前对局阶段。</summary>
    public enum PetFlipRoundPhase
    {
        /// <summary>尚未开盘，或已因登出/换角/切服清盘。</summary>
        Idle = 0,
        /// <summary>对局进行中，允许点击未配对牌。</summary>
        Playing = 1,
        /// <summary>盘面保留但因关闭面板、切后台、断线或高优先中断暂停交互。</summary>
        Paused = 2,
        /// <summary>配齐全部对子已终局，等待点击“再玩一局”。</summary>
        Completed = 3,
        /// <summary>控制器已销毁。</summary>
        Disposed = 4
    }

    /// <summary>单手翻牌进度状态。</summary>
    public enum PetFlipHandState
    {
        None = 0,
        /// <summary>等待翻开本手第 1 张牌。</summary>
        WaitingFirstCard = 1,
        /// <summary>已翻开第 1 张牌，等待翻开第 2 张牌。</summary>
        WaitingSecondCard = 2,
        /// <summary>上一手两张牌失配并保持亮起，等待点击任一未配对牌先盖回再开新手第 1 张。</summary>
        MismatchShowing = 3,
        /// <summary>灵目模式免费窥看 1 张暗牌亮起中（不计次），下一次点击任一未配对牌先盖回再开新手第 1 张。</summary>
        SpiritEyePeeking = 4
    }

    /// <summary>可叠加的暂停与高优先中断来源位掩码。</summary>
    [Flags]
    public enum PetFlipInterruptFlags
    {
        None = 0,
        /// <summary>玩家暂时关闭面板，或被其他主城互斥面板收起。</summary>
        PanelClosed = 1 << 0,
        /// <summary>应用切入后台挂起。</summary>
        ApplicationBackground = 1 << 1,
        /// <summary>网络临时断线/自动重连中，停止操作但保留同角色盘面。</summary>
        Disconnected = 1 << 2,
        /// <summary>战斗优先，翻牌页暂停收起。</summary>
        Battle = 1 << 3,
        /// <summary>观战优先，翻牌页暂停收起。</summary>
        Spectate = 1 << 4,
        /// <summary>加载、切场景或跨服重定向中，翻牌页暂停收起。</summary>
        Loading = 1 << 5,
        /// <summary>强制弹窗优先（实名认证、防沉迷、注销确认、系统强弹窗等）。</summary>
        ForcedModal = 1 << 6
    }

    /// <summary>本局完成成绩与旧本机最佳的比较结果。</summary>
    public enum PetFlipRecordComparison
    {
        None = 0,
        /// <summary>没有旧纪录：首次纪录。</summary>
        FirstRecord = 1,
        /// <summary>少于旧纪录：刷新纪录。</summary>
        NewBest = 2,
        /// <summary>等于旧纪录：追平纪录。</summary>
        TiedBest = 3,
        /// <summary>多于旧纪录：普通完成，旧纪录不变。</summary>
        NormalCompletion = 4
    }

    /// <summary>设备本机最佳纪录的持久化状态。</summary>
    public enum PetFlipRecordPersistenceStatus
    {
        /// <summary>暂无纪录（从未保存、缺失或读取损坏）。</summary>
        NoRecord = 0,
        /// <summary>本机最佳已确认保存。</summary>
        Saved = 1,
        /// <summary>本机纪录未保存（写入存储失败，不阻止继续游玩）。</summary>
        Unsaved = 2
    }

    /// <summary>每次逻辑调用的结果类型。</summary>
    public enum PetFlipActionOutcome
    {
        StartedNewRound = 0,
        ResumedRound = 1,
        FirstCardFlipped = 2,
        MismatchClearedAndFirstCardFlipped = 3,
        PairMatched = 4,
        PairMismatched = 5,
        RoundCompleted = 6,
        Paused = 7,
        Resumed = 8,
        Rejected = 9,
        ConfigurationChanged = 10,
        SpiritEyePeeked = 11,
        SpiritEyeClearedAndFirstCardFlipped = 12
    }

    /// <summary>操作被逻辑拒绝时的明确原因。</summary>
    public enum PetFlipRejectReason
    {
        None = 0,
        Disposed = 1,
        NoActiveSession = 2,
        BlockedByInterrupt = 3,
        GamePaused = 4,
        NoActiveRound = 5,
        RoundAlreadyCompleted = 6,
        RoundNotCompleted = 7,
        InvalidSlotIndex = 8,
        CardAlreadyMatched = 9,
        AlreadySelectedInHand = 10,
        StaleRound = 11,
        StaleView = 12,
        SpiritEyeUnavailable = 13
    }

    /// <summary>当前进程内盘面绑定的角色与区服会话标识。</summary>
    public readonly struct PetFlipSessionKey : IEquatable<PetFlipSessionKey>
    {
        public ulong PlayerId { get; }
        public uint ZoneId { get; }
        public bool IsValid => PlayerId != 0 && ZoneId != 0;

        public PetFlipSessionKey(ulong playerId, uint zoneId)
        {
            PlayerId = playerId;
            ZoneId = zoneId;
        }

        public bool Equals(PetFlipSessionKey other) => PlayerId == other.PlayerId && ZoneId == other.ZoneId;
        public override bool Equals(object obj) => obj is PetFlipSessionKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked { return (PlayerId.GetHashCode() * 397) ^ (int)ZoneId; }
        }

        public static bool operator ==(PetFlipSessionKey left, PetFlipSessionKey right) => left.Equals(right);
        public static bool operator !=(PetFlipSessionKey left, PetFlipSessionKey right) => !left.Equals(right);
        public override string ToString() => $"Player={PlayerId}, Zone={ZoneId}";
    }

    /// <summary>本机最佳记录分桶键（按难度与模式隔离，绝不混榜）。</summary>
    public readonly struct PetFlipRecordBucketKey : IEquatable<PetFlipRecordBucketKey>, IComparable<PetFlipRecordBucketKey>
    {
        public PetFlipDifficulty Difficulty { get; }
        public PetFlipGameMode Mode { get; }

        public PetFlipRecordBucketKey(PetFlipDifficulty difficulty, PetFlipGameMode mode)
        {
            Difficulty = difficulty;
            Mode = mode;
        }

        public bool Equals(PetFlipRecordBucketKey other) => Difficulty == other.Difficulty && Mode == other.Mode;
        public override bool Equals(object obj) => obj is PetFlipRecordBucketKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked { return ((int)Difficulty * 397) ^ (int)Mode; }
        }

        public int CompareTo(PetFlipRecordBucketKey other)
        {
            int c = ((int)Difficulty).CompareTo((int)other.Difficulty);
            return c != 0 ? c : ((int)Mode).CompareTo((int)other.Mode);
        }

        public static bool operator ==(PetFlipRecordBucketKey left, PetFlipRecordBucketKey right) => left.Equals(right);
        public static bool operator !=(PetFlipRecordBucketKey left, PetFlipRecordBucketKey right) => !left.Equals(right);
    }

    /// <summary>单张卡牌的不可变视图快照（区分槽位位置、翻牌状态与可见图案）。</summary>
    public readonly struct PetFlipCardSnapshot
    {
        public int SlotIndex { get; }
        public PetFlipCardState State { get; }
        /// <summary>仅在正面亮起或已配对时暴露图案；背面朝上恒为 null。</summary>
        public SpiritPetCardPattern? VisiblePattern { get; }
        public bool IsClickable { get; }
        public bool IsFaceUp => State != PetFlipCardState.FaceDown;
        public bool IsMatched => State == PetFlipCardState.Matched;

        public PetFlipCardSnapshot(int slotIndex, PetFlipCardState state, SpiritPetCardPattern? visiblePattern, bool isClickable)
        {
            SlotIndex = slotIndex;
            State = state;
            VisiblePattern = state == PetFlipCardState.FaceDown ? null : visiblePattern;
            IsClickable = isClickable;
        }
    }

    /// <summary>设备本机最佳纪录的不可变快照（对应当前所选难度与模式分桶）。</summary>
    public sealed class PetFlipLocalRecordSnapshot
    {
        public bool HasBestRecord => BestFlipCount.HasValue;
        /// <summary>当前所选难度/模式已知本机最佳次数；暂无纪录时为 null。</summary>
        public int? BestFlipCount { get; }
        /// <summary>已成功落盘的本机最佳次数；首次保存失败时为 null，刷新保存失败时仍为旧落盘值。</summary>
        public int? PersistedBestFlipCount { get; }
        public PetFlipRecordPersistenceStatus PersistenceStatus { get; }
        public bool IsUnsaved => PersistenceStatus == PetFlipRecordPersistenceStatus.Unsaved;

        public PetFlipLocalRecordSnapshot(
            int? bestFlipCount,
            int? persistedBestFlipCount,
            PetFlipRecordPersistenceStatus persistenceStatus)
        {
            BestFlipCount = bestFlipCount;
            PersistedBestFlipCount = persistedBestFlipCount;
            PersistenceStatus = persistenceStatus;
        }
    }

    /// <summary>单局完成时的不可变结算快照，每局严格只计算一次。</summary>
    public sealed class PetFlipCompletionResult
    {
        public ulong RoundId { get; }
        public int FlipCount { get; }
        public int MatchedPairCount { get; }
        public int? PreviousBestFlipCount { get; }
        public int BestFlipCount { get; }
        public PetFlipRecordComparison Comparison { get; }
        public PetFlipRecordPersistenceStatus RecordPersistenceStatus { get; }
        public bool IsRecordUnsaved => RecordPersistenceStatus == PetFlipRecordPersistenceStatus.Unsaved;
        /// <summary>本局最长连续配对次数。</summary>
        public int MaxStreak { get; }
        public PetFlipDifficulty Difficulty { get; }
        public PetFlipGameMode Mode { get; }
        /// <summary>零失误一次配齐全部对子（轻松 6 次 / 进阶 8 次，一气呵成）。</summary>
        public bool IsPerfectRun => MatchedPairCount > 0 && FlipCount == MatchedPairCount;

        public PetFlipCompletionResult(
            ulong roundId,
            int flipCount,
            int matchedPairCount,
            int? previousBestFlipCount,
            int bestFlipCount,
            PetFlipRecordComparison comparison,
            PetFlipRecordPersistenceStatus recordPersistenceStatus,
            int maxStreak = 0,
            PetFlipDifficulty difficulty = PetFlipDifficulty.Relaxed12,
            PetFlipGameMode mode = PetFlipGameMode.Classic)
        {
            RoundId = roundId;
            FlipCount = flipCount;
            MatchedPairCount = matchedPairCount;
            PreviousBestFlipCount = previousBestFlipCount;
            BestFlipCount = bestFlipCount;
            Comparison = comparison;
            RecordPersistenceStatus = recordPersistenceStatus;
            MaxStreak = maxStreak < 0 ? 0 : maxStreak;
            Difficulty = difficulty;
            Mode = mode;
        }
    }

    /// <summary>面向 UI 的完整不可变盘面与生命周期快照。</summary>
    public sealed class PetFlipSnapshot
    {
        public ulong Revision { get; }
        public ulong RoundId { get; }
        public PetFlipSessionKey Session { get; }
        public PetFlipSessionKey BoardOwnerSession { get; }
        public PetFlipDifficulty Difficulty { get; }
        public PetFlipGameMode Mode { get; }
        /// <summary>今日灵签模式对应的本地公历日期键（yyyyMMdd）；非今日灵签模式为 0。</summary>
        public int DailyDateKey { get; }
        public int TotalPairCount => PetFlipConstants.GetPairCount(Difficulty);
        public bool HasActiveSession => Session.IsValid && !IsDisconnected && !IsDisposed;
        public bool HasBoard => RoundId != 0;
        public PetFlipRoundPhase Phase { get; }
        public PetFlipHandState HandState { get; }
        public PetFlipInterruptFlags ActiveInterrupts { get; }
        public bool CanInteract => Phase == PetFlipRoundPhase.Playing && ActiveInterrupts == PetFlipInterruptFlags.None && HasActiveSession;
        public bool CanStartNewRound => Phase == PetFlipRoundPhase.Completed && ActiveInterrupts == PetFlipInterruptFlags.None && HasActiveSession;
        public bool CanChangeConfiguration => (Phase == PetFlipRoundPhase.Idle || Phase == PetFlipRoundPhase.Completed) && !IsDisposed;
        public bool IsPaused => Phase == PetFlipRoundPhase.Paused;
        public bool IsDisconnected => (ActiveInterrupts & PetFlipInterruptFlags.Disconnected) != 0;
        public bool IsDisposed => Phase == PetFlipRoundPhase.Disposed;
        /// <summary>战斗、观战、加载、强制弹窗、暂关面板、未登录或已销毁时为 true，UI 应立即收起翻牌面板。</summary>
        public bool ShouldCollapsePanel =>
            IsDisposed ||
            (!HasActiveSession && !IsDisconnected) ||
            (ActiveInterrupts & PetFlipConstants.CollapsePanelMask) != 0;
        public int FlipCount { get; }
        public int MatchedPairCount { get; }
        public int RemainingPairCount => HasBoard ? TotalPairCount - MatchedPairCount : 0;
        /// <summary>当前连续配中次数；失配后清零。</summary>
        public int CurrentStreak { get; }
        /// <summary>本局迄今最长连续配中次数。</summary>
        public int MaxStreak { get; }
        /// <summary>刚完成配对时的灵宠图案；开始新一手翻牌或失配后清空。</summary>
        public SpiritPetCardPattern? LastMatchedPattern { get; }
        /// <summary>灵目模式当前剩余免费窥牌次数（经典与今日灵签模式恒为 0）。</summary>
        public int RemainingSpiritEyeCharges { get; }
        /// <summary>当前是否可触发灵目免费窥牌。</summary>
        public bool CanUseSpiritEye =>
            CanInteract
            && Mode == PetFlipGameMode.SpiritEye
            && RemainingSpiritEyeCharges > 0
            && HandState == PetFlipHandState.WaitingFirstCard;
        public int FirstSelectedSlotIndex { get; }
        public int SecondSelectedSlotIndex { get; }
        public IReadOnlyList<PetFlipCardSnapshot> Cards { get; }
        public PetFlipLocalRecordSnapshot LocalRecord { get; }
        public PetFlipCompletionResult CompletionResult { get; }

        public PetFlipSnapshot(
            ulong revision,
            ulong roundId,
            PetFlipSessionKey session,
            PetFlipSessionKey boardOwnerSession,
            PetFlipRoundPhase phase,
            PetFlipHandState handState,
            PetFlipInterruptFlags activeInterrupts,
            int flipCount,
            int matchedPairCount,
            int firstSelectedSlotIndex,
            int secondSelectedSlotIndex,
            IReadOnlyList<PetFlipCardSnapshot> cards,
            PetFlipLocalRecordSnapshot localRecord,
            PetFlipCompletionResult completionResult,
            int currentStreak = 0,
            int maxStreak = 0,
            SpiritPetCardPattern? lastMatchedPattern = null,
            PetFlipDifficulty difficulty = PetFlipDifficulty.Relaxed12,
            PetFlipGameMode mode = PetFlipGameMode.Classic,
            int dailyDateKey = 0,
            int remainingSpiritEyeCharges = 0)
        {
            Revision = revision;
            RoundId = roundId;
            Session = session;
            BoardOwnerSession = boardOwnerSession;
            Difficulty = difficulty;
            Mode = mode;
            DailyDateKey = dailyDateKey;
            Phase = phase;
            HandState = handState;
            ActiveInterrupts = activeInterrupts;
            FlipCount = flipCount;
            MatchedPairCount = matchedPairCount;
            CurrentStreak = currentStreak < 0 ? 0 : currentStreak;
            MaxStreak = maxStreak < 0 ? 0 : maxStreak;
            LastMatchedPattern = lastMatchedPattern;
            RemainingSpiritEyeCharges = remainingSpiritEyeCharges < 0 ? 0 : remainingSpiritEyeCharges;
            FirstSelectedSlotIndex = firstSelectedSlotIndex;
            SecondSelectedSlotIndex = secondSelectedSlotIndex;
            Cards = cards ?? Array.Empty<PetFlipCardSnapshot>();
            LocalRecord = localRecord ?? throw new ArgumentNullException(nameof(localRecord));
            CompletionResult = completionResult;
        }
    }

    /// <summary>单次操作返回结果，携带同步生效后的最新不可变快照与本次动作变化明细。</summary>
    public sealed class PetFlipActionResult
    {
        public bool Accepted => Outcome != PetFlipActionOutcome.Rejected;
        public PetFlipActionOutcome Outcome { get; }
        public PetFlipRejectReason RejectReason { get; }
        public int FlippedSlotIndex { get; }
        public int FirstHandSlotIndex { get; }
        public int SecondHandSlotIndex { get; }
        public int CoveredFirstSlotIndex { get; }
        public int CoveredSecondSlotIndex { get; }
        public SpiritPetCardPattern? FlippedPattern { get; }
        public SpiritPetCardPattern? MatchedPattern { get; }
        /// <summary>本次动作后的当前连续配中次数（供 UI 播连击反馈）。</summary>
        public int CurrentStreak { get; }
        public PetFlipSnapshot Snapshot { get; }

        public PetFlipActionResult(
            PetFlipActionOutcome outcome,
            PetFlipRejectReason rejectReason,
            PetFlipSnapshot snapshot,
            int flippedSlotIndex = -1,
            int firstHandSlotIndex = -1,
            int secondHandSlotIndex = -1,
            int coveredFirstSlotIndex = -1,
            int coveredSecondSlotIndex = -1,
            SpiritPetCardPattern? flippedPattern = null,
            SpiritPetCardPattern? matchedPattern = null,
            int currentStreak = 0)
        {
            Outcome = outcome;
            RejectReason = rejectReason;
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            FlippedSlotIndex = flippedSlotIndex;
            FirstHandSlotIndex = firstHandSlotIndex;
            SecondHandSlotIndex = secondHandSlotIndex;
            CoveredFirstSlotIndex = coveredFirstSlotIndex;
            CoveredSecondSlotIndex = coveredSecondSlotIndex;
            FlippedPattern = flippedPattern;
            MatchedPattern = matchedPattern;
            CurrentStreak = currentStreak < 0 ? 0 : currentStreak;
        }

        public static PetFlipActionResult Rejected(PetFlipRejectReason reason, PetFlipSnapshot snapshot)
            => new PetFlipActionResult(PetFlipActionOutcome.Rejected, reason, snapshot);

        public static PetFlipActionResult Lifecycle(PetFlipActionOutcome outcome, PetFlipSnapshot snapshot)
            => new PetFlipActionResult(outcome, PetFlipRejectReason.None, snapshot);
    }
}
