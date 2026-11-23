using System;
using System.Collections.Generic;

namespace MmorpgClient.Game.PetFlip
{
    /// <summary>灵宠翻牌牌组构造与校验工具；正式开局始终走 Fisher-Yates 随机洗牌。</summary>
    public static class PetFlipDeckFactory
    {
        private static readonly SpiritPetCardPattern[] CanonicalPairSeed =
        {
            SpiritPetCardPattern.A, SpiritPetCardPattern.B, SpiritPetCardPattern.C,
            SpiritPetCardPattern.D, SpiritPetCardPattern.E, SpiritPetCardPattern.F,
            SpiritPetCardPattern.A, SpiritPetCardPattern.B, SpiritPetCardPattern.C,
            SpiritPetCardPattern.D, SpiritPetCardPattern.E, SpiritPetCardPattern.F
        };

        /// <summary>生成一副 12 张（6 种灵宠图案各 2 张）并随机打乱的正式牌组。</summary>
        public static SpiritPetCardPattern[] CreateShuffledDeck(Random random = null)
        {
            var rng = random ?? new Random();
            var deck = new SpiritPetCardPattern[PetFlipConstants.BoardCardCount];
            Array.Copy(CanonicalPairSeed, deck, PetFlipConstants.BoardCardCount);

            for (int i = deck.Length - 1; i > 0; i--)
            {
                int swapIndex = rng.Next(0, i + 1);
                SpiritPetCardPattern temp = deck[i];
                deck[i] = deck[swapIndex];
                deck[swapIndex] = temp;
            }

            return deck;
        }

        /// <summary>校验牌组恰好为 12 张且 6 种图案各出现 2 张。</summary>
        public static void ValidateDeck(IReadOnlyList<SpiritPetCardPattern> deck)
        {
            if (deck == null)
                throw new ArgumentNullException(nameof(deck));

            if (deck.Count != PetFlipConstants.BoardCardCount)
            {
                throw new ArgumentException(
                    $"牌组必须恰好包含 {PetFlipConstants.BoardCardCount} 张牌，当前为 {deck.Count} 张。",
                    nameof(deck));
            }

            var counts = new int[PetFlipConstants.PairCount];
            for (int i = 0; i < deck.Count; i++)
            {
                int index = (int)deck[i];
                if (index < 0 || index >= PetFlipConstants.PairCount)
                {
                    throw new ArgumentException(
                        $"槽位 {i} 存在非法图案枚举值：{deck[i]}。",
                        nameof(deck));
                }

                counts[index]++;
            }

            for (int p = 0; p < PetFlipConstants.PairCount; p++)
            {
                if (counts[p] != 2)
                {
                    throw new ArgumentException(
                        $"图案 {(SpiritPetCardPattern)p} 必须恰好出现 2 张，当前为 {counts[p]} 张。",
                        nameof(deck));
                }
            }
        }
    }

    /// <summary>单盘牌面一次翻牌尝试的纯规则结果。</summary>
    public readonly struct PetFlipBoardStepResult
    {
        public PetFlipActionOutcome Outcome { get; }
        public PetFlipRejectReason RejectReason { get; }
        public int FlippedSlotIndex { get; }
        public int FirstHandSlotIndex { get; }
        public int SecondHandSlotIndex { get; }
        public int CoveredFirstSlotIndex { get; }
        public int CoveredSecondSlotIndex { get; }
        public SpiritPetCardPattern? FlippedPattern { get; }
        public SpiritPetCardPattern? MatchedPattern { get; }
        public bool Accepted => Outcome != PetFlipActionOutcome.Rejected;

        public PetFlipBoardStepResult(
            PetFlipActionOutcome outcome,
            PetFlipRejectReason rejectReason,
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
            FlippedSlotIndex = flippedSlotIndex;
            FirstHandSlotIndex = firstHandSlotIndex;
            SecondHandSlotIndex = secondHandSlotIndex;
            CoveredFirstSlotIndex = coveredFirstSlotIndex;
            CoveredSecondSlotIndex = coveredSecondSlotIndex;
            FlippedPattern = flippedPattern;
            MatchedPattern = matchedPattern;
        }

        public static PetFlipBoardStepResult Rejected(PetFlipRejectReason reason)
            => new PetFlipBoardStepResult(PetFlipActionOutcome.Rejected, reason);
    }

    /// <summary>
    /// 灵宠翻牌单局纯 C# 规则状态机（零 UnityEngine 依赖）。
    /// 负责 12 张牌的配对、失配停留、失配后先盖回再开新手、计次与 6 对终局判定。
    /// </summary>
    public sealed class PetFlipBoardModel
    {
        private readonly SpiritPetCardPattern[] _patterns;
        private readonly PetFlipCardState[] _states;

        public int FlipCount { get; private set; }
        public int MatchedPairCount { get; private set; }
        public bool IsCompleted => MatchedPairCount >= PetFlipConstants.PairCount;
        public PetFlipHandState HandState { get; private set; }
        public int FirstSelectedSlotIndex { get; private set; }
        public int SecondSelectedSlotIndex { get; private set; }

        public PetFlipBoardModel(IReadOnlyList<SpiritPetCardPattern> deck)
        {
            PetFlipDeckFactory.ValidateDeck(deck);

            _patterns = new SpiritPetCardPattern[PetFlipConstants.BoardCardCount];
            _states = new PetFlipCardState[PetFlipConstants.BoardCardCount];
            for (int i = 0; i < PetFlipConstants.BoardCardCount; i++)
            {
                _patterns[i] = deck[i];
                _states[i] = PetFlipCardState.FaceDown;
            }

            FlipCount = 0;
            MatchedPairCount = 0;
            HandState = PetFlipHandState.WaitingFirstCard;
            FirstSelectedSlotIndex = -1;
            SecondSelectedSlotIndex = -1;
        }

        /// <summary>同步执行点击某个槽位，立即更新权威盘面与翻牌次数。</summary>
        public PetFlipBoardStepResult FlipCard(int slotIndex)
        {
            if (IsCompleted)
                return PetFlipBoardStepResult.Rejected(PetFlipRejectReason.RoundAlreadyCompleted);

            if (slotIndex < 0 || slotIndex >= PetFlipConstants.BoardCardCount)
                return PetFlipBoardStepResult.Rejected(PetFlipRejectReason.InvalidSlotIndex);

            if (_states[slotIndex] == PetFlipCardState.Matched)
                return PetFlipBoardStepResult.Rejected(PetFlipRejectReason.CardAlreadyMatched);

            switch (HandState)
            {
                case PetFlipHandState.WaitingFirstCard:
                    _states[slotIndex] = PetFlipCardState.FaceUp;
                    FirstSelectedSlotIndex = slotIndex;
                    SecondSelectedSlotIndex = -1;
                    HandState = PetFlipHandState.WaitingSecondCard;
                    return new PetFlipBoardStepResult(
                        PetFlipActionOutcome.FirstCardFlipped,
                        PetFlipRejectReason.None,
                        flippedSlotIndex: slotIndex,
                        firstHandSlotIndex: slotIndex,
                        flippedPattern: _patterns[slotIndex]);

                case PetFlipHandState.MismatchShowing:
                {
                    int coveredFirst = FirstSelectedSlotIndex;
                    int coveredSecond = SecondSelectedSlotIndex;
                    _states[coveredFirst] = PetFlipCardState.FaceDown;
                    _states[coveredSecond] = PetFlipCardState.FaceDown;

                    // 允许点击刚才失配的两张之一：先两张全部盖回，再把本次点击翻开作为新一手第一张。
                    _states[slotIndex] = PetFlipCardState.FaceUp;
                    FirstSelectedSlotIndex = slotIndex;
                    SecondSelectedSlotIndex = -1;
                    HandState = PetFlipHandState.WaitingSecondCard;

                    return new PetFlipBoardStepResult(
                        PetFlipActionOutcome.MismatchClearedAndFirstCardFlipped,
                        PetFlipRejectReason.None,
                        flippedSlotIndex: slotIndex,
                        firstHandSlotIndex: slotIndex,
                        coveredFirstSlotIndex: coveredFirst,
                        coveredSecondSlotIndex: coveredSecond,
                        flippedPattern: _patterns[slotIndex]);
                }

                case PetFlipHandState.WaitingSecondCard:
                {
                    if (slotIndex == FirstSelectedSlotIndex)
                        return PetFlipBoardStepResult.Rejected(PetFlipRejectReason.AlreadySelectedInHand);

                    int firstSlot = FirstSelectedSlotIndex;
                    _states[slotIndex] = PetFlipCardState.FaceUp;
                    FlipCount++;

                    SpiritPetCardPattern firstPattern = _patterns[firstSlot];
                    SpiritPetCardPattern secondPattern = _patterns[slotIndex];
                    if (firstPattern == secondPattern)
                    {
                        _states[firstSlot] = PetFlipCardState.Matched;
                        _states[slotIndex] = PetFlipCardState.Matched;
                        MatchedPairCount++;
                        FirstSelectedSlotIndex = -1;
                        SecondSelectedSlotIndex = -1;

                        if (IsCompleted)
                        {
                            HandState = PetFlipHandState.None;
                            return new PetFlipBoardStepResult(
                                PetFlipActionOutcome.RoundCompleted,
                                PetFlipRejectReason.None,
                                flippedSlotIndex: slotIndex,
                                firstHandSlotIndex: firstSlot,
                                secondHandSlotIndex: slotIndex,
                                flippedPattern: secondPattern,
                                matchedPattern: secondPattern);
                        }

                        HandState = PetFlipHandState.WaitingFirstCard;
                        return new PetFlipBoardStepResult(
                            PetFlipActionOutcome.PairMatched,
                            PetFlipRejectReason.None,
                            flippedSlotIndex: slotIndex,
                            firstHandSlotIndex: firstSlot,
                            secondHandSlotIndex: slotIndex,
                            flippedPattern: secondPattern,
                            matchedPattern: secondPattern);
                    }

                    SecondSelectedSlotIndex = slotIndex;
                    HandState = PetFlipHandState.MismatchShowing;
                    return new PetFlipBoardStepResult(
                        PetFlipActionOutcome.PairMismatched,
                        PetFlipRejectReason.None,
                        flippedSlotIndex: slotIndex,
                        firstHandSlotIndex: firstSlot,
                        secondHandSlotIndex: slotIndex,
                        flippedPattern: secondPattern);
                }

                default:
                    return PetFlipBoardStepResult.Rejected(PetFlipRejectReason.RoundAlreadyCompleted);
            }
        }

        /// <summary>构建当前 12 张牌的不可变显示快照；暗牌不暴露图案。</summary>
        public IReadOnlyList<PetFlipCardSnapshot> BuildCardSnapshots(bool boardInteractive)
        {
            var snapshots = new PetFlipCardSnapshot[PetFlipConstants.BoardCardCount];
            for (int i = 0; i < PetFlipConstants.BoardCardCount; i++)
            {
                PetFlipCardState state = _states[i];
                SpiritPetCardPattern? visiblePattern = state == PetFlipCardState.FaceDown
                    ? (SpiritPetCardPattern?)null
                    : _patterns[i];
                bool isClickable = boardInteractive
                    && state != PetFlipCardState.Matched
                    && !(HandState == PetFlipHandState.WaitingSecondCard && i == FirstSelectedSlotIndex);
                snapshots[i] = new PetFlipCardSnapshot(i, state, visiblePattern, isClickable);
            }

            return Array.AsReadOnly(snapshots);
        }
    }
}
