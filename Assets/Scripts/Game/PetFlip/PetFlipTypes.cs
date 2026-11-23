using System;
using System.Collections.Generic;

namespace MmorpgClient.Game.PetFlip
{
    /// <summary>灵宠翻牌核心常量与中断掩码。</summary>
    public static class PetFlipConstants
    {
        public const int BoardCardCount = 12;
        public const int PairCount = 6;
        public const int MinimumPossibleFlips = PairCount;

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
    }

    /// <summary>
    /// 6 种灵宠卡牌图案标识，独立于真实宝宝养成配置表 ID。
    /// UI 与美术可将这 6 个枚举值直接映射到对应的 6 套卡面资源。
    /// </summary>
    public enum SpiritPetCardPattern
    {
        A = 0,
        B = 1,
        C = 2,
        D = 3,
        E = 4,
        F = 5
    }

    /// <summary>单张卡牌的可见翻牌状态。</summary>
    public enum PetFlipCardState
    {
        /// <summary>背面朝上，对外不暴露图案。</summary>
        FaceDown = 0,
        /// <summary>本手翻开或失配停留亮起。</summary>
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
        /// <summary>配齐 6 对已终局，等待点击“再玩一局”。</summary>
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
        MismatchShowing = 3
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
        Rejected = 9
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
        StaleView = 12
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

    /// <summary>设备本机最佳纪录的不可变快照。</summary>
    public sealed class PetFlipLocalRecordSnapshot
    {
        public bool HasBestRecord => BestFlipCount.HasValue;
        /// <summary>当前已知本机最佳次数；暂无纪录时为 null。</summary>
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

        public PetFlipCompletionResult(
            ulong roundId,
            int flipCount,
            int matchedPairCount,
            int? previousBestFlipCount,
            int bestFlipCount,
            PetFlipRecordComparison comparison,
            PetFlipRecordPersistenceStatus recordPersistenceStatus)
        {
            RoundId = roundId;
            FlipCount = flipCount;
            MatchedPairCount = matchedPairCount;
            PreviousBestFlipCount = previousBestFlipCount;
            BestFlipCount = bestFlipCount;
            Comparison = comparison;
            RecordPersistenceStatus = recordPersistenceStatus;
        }
    }

    /// <summary>面向 UI 的完整不可变盘面与生命周期快照。</summary>
    public sealed class PetFlipSnapshot
    {
        public ulong Revision { get; }
        public ulong RoundId { get; }
        public PetFlipSessionKey Session { get; }
        public PetFlipSessionKey BoardOwnerSession { get; }
        public bool HasActiveSession => Session.IsValid && !IsDisconnected && !IsDisposed;
        public bool HasBoard => RoundId != 0;
        public PetFlipRoundPhase Phase { get; }
        public PetFlipHandState HandState { get; }
        public PetFlipInterruptFlags ActiveInterrupts { get; }
        public bool CanInteract => Phase == PetFlipRoundPhase.Playing && ActiveInterrupts == PetFlipInterruptFlags.None && HasActiveSession;
        public bool CanStartNewRound => Phase == PetFlipRoundPhase.Completed && ActiveInterrupts == PetFlipInterruptFlags.None && HasActiveSession;
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
        public int RemainingPairCount => HasBoard ? PetFlipConstants.PairCount - MatchedPairCount : 0;
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
            PetFlipCompletionResult completionResult)
        {
            Revision = revision;
            RoundId = roundId;
            Session = session;
            BoardOwnerSession = boardOwnerSession;
            Phase = phase;
            HandState = handState;
            ActiveInterrupts = activeInterrupts;
            FlipCount = flipCount;
            MatchedPairCount = matchedPairCount;
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
            SpiritPetCardPattern? matchedPattern = null)
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
        }

        public static PetFlipActionResult Rejected(PetFlipRejectReason reason, PetFlipSnapshot snapshot)
            => new PetFlipActionResult(PetFlipActionOutcome.Rejected, reason, snapshot);

        public static PetFlipActionResult Lifecycle(PetFlipActionOutcome outcome, PetFlipSnapshot snapshot)
            => new PetFlipActionResult(outcome, PetFlipRejectReason.None, snapshot);
    }
}
