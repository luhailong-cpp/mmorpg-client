using System;
using System.Collections.Generic;

namespace MmorpgClient.Game.PetFlip
{
    /// <summary>灵宠翻牌牌组构造与校验工具；支持轻松 12 张（6 对）与进阶 16 张（8 对），正式开局走 Fisher-Yates 洗牌。</summary>
    public static class PetFlipDeckFactory
    {
        /// <summary>生成指定难度的随机洗牌牌组（12 张 6 对或 16 张 8 对）。</summary>
        public static SpiritPetCardPattern[] CreateShuffledDeck(
            Random random = null,
            PetFlipDifficulty difficulty = PetFlipDifficulty.Relaxed12)
        {
            int pairCount = PetFlipConstants.GetPairCount(difficulty);
            int cardCount = pairCount * 2;
            var rng = random ?? new Random();
            var deck = new SpiritPetCardPattern[cardCount];

            for (int p = 0; p < pairCount; p++)
            {
                deck[p] = (SpiritPetCardPattern)p;
                deck[p + pairCount] = (SpiritPetCardPattern)p;
            }

            for (int i = deck.Length - 1; i > 0; i--)
            {
                int swapIndex = rng.Next(0, i + 1);
                SpiritPetCardPattern temp = deck[i];
                deck[i] = deck[swapIndex];
                deck[swapIndex] = temp;
            }

            return deck;
        }

        /// <summary>
        /// 按本地公历日期与难度生成确定性牌组（纯本地跨平台 FNV-1a 种子，不依赖平台 HashCode 随机化），
        /// 用于“今日灵签”每日同盘挑战。
        /// </summary>
        public static SpiritPetCardPattern[] CreateDailySeededDeck(
            int year,
            int month,
            int day,
            PetFlipDifficulty difficulty = PetFlipDifficulty.Relaxed12)
        {
            if (year < 1 || month < 1 || month > 12 || day < 1 || day > 31)
                throw new ArgumentOutOfRangeException(nameof(day), "日期参数超出合法范围。");

            uint hash = 2166136261u;
            unchecked
            {
                hash = (hash ^ (uint)year) * 16777619u;
                hash = (hash ^ (uint)month) * 16777619u;
                hash = (hash ^ (uint)day) * 16777619u;
                hash = (hash ^ (uint)((int)difficulty + 1)) * 16777619u;
                hash ^= hash >> 16;
            }

            int seed = (int)(hash & 0x7FFFFFFFu);
            return CreateShuffledDeck(new Random(seed), difficulty);
        }

        /// <summary>校验牌组恰好符合对应难度的张数（12 或 16）且每种图案各出现 2 张。</summary>
        public static void ValidateDeck(
            IReadOnlyList<SpiritPetCardPattern> deck,
            PetFlipDifficulty difficulty = PetFlipDifficulty.Relaxed12)
        {
            if (deck == null)
                throw new ArgumentNullException(nameof(deck));

            int expectedPairs = PetFlipConstants.GetPairCount(difficulty);
            int expectedCards = expectedPairs * 2;
            if (deck.Count != expectedCards)
            {
                throw new ArgumentException(
                    $"难度 {difficulty} 的牌组必须恰好包含 {expectedCards} 张牌，当前为 {deck.Count} 张。",
                    nameof(deck));
            }

            var counts = new int[expectedPairs];
            for (int i = 0; i < deck.Count; i++)
            {
                int index = (int)deck[i];
                if (index < 0 || index >= expectedPairs)
                {
                    throw new ArgumentException(
                        $"槽位 {i} 存在超出难度 {difficulty} 范围的图案枚举值：{deck[i]}。",
                        nameof(deck));
                }

                counts[index]++;
            }

            for (int p = 0; p < expectedPairs; p++)
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

    /// <summary>单盘牌面一次翻牌或灵目窥牌尝试的纯规则结果。</summary>
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
    /// 支持轻松 12 张（6 对）与进阶 16 张（8 对）、失配停留先盖回再开新手、
    /// 灵目模式免费窥牌（不计次、不自动倒计时盖回）、连击追踪与全部配齐终局判定。
    /// </summary>
    public sealed class PetFlipBoardModel
    {
        private readonly SpiritPetCardPattern[] _patterns;
        private readonly PetFlipCardState[] _states;

        public PetFlipDifficulty Difficulty { get; }
        public int CardCount => _patterns.Length;
        public int PairCount { get; }
        public int FlipCount { get; private set; }
        public int MatchedPairCount { get; private set; }
        public int CurrentStreak { get; private set; }
        public int MaxStreak { get; private set; }
        public SpiritPetCardPattern? LastMatchedPattern { get; private set; }
        public int RemainingSpiritEyeCharges { get; private set; }
        public bool IsCompleted => MatchedPairCount >= PairCount;
        public PetFlipHandState HandState { get; private set; }
        public int FirstSelectedSlotIndex { get; private set; }
        public int SecondSelectedSlotIndex { get; private set; }

        public PetFlipBoardModel(
            IReadOnlyList<SpiritPetCardPattern> deck,
            PetFlipDifficulty difficulty = PetFlipDifficulty.Relaxed12,
            int initialSpiritEyeCharges = 0)
        {
            PetFlipDeckFactory.ValidateDeck(deck, difficulty);

            Difficulty = difficulty;
            PairCount = PetFlipConstants.GetPairCount(difficulty);
            int cardCount = PairCount * 2;
            _patterns = new SpiritPetCardPattern[cardCount];
            _states = new PetFlipCardState[cardCount];
            for (int i = 0; i < cardCount; i++)
            {
                _patterns[i] = deck[i];
                _states[i] = PetFlipCardState.FaceDown;
            }

            FlipCount = 0;
            MatchedPairCount = 0;
            CurrentStreak = 0;
            MaxStreak = 0;
            LastMatchedPattern = null;
            RemainingSpiritEyeCharges = initialSpiritEyeCharges > 0 ? initialSpiritEyeCharges : 0;
            HandState = PetFlipHandState.WaitingFirstCard;
            FirstSelectedSlotIndex = -1;
            SecondSelectedSlotIndex = -1;
        }

        /// <summary>
        /// 灵目模式免费窥看 1 张背面暗牌：不计次、不打断连击、不自动倒计时盖回；
        /// 下一次正常点击任一未配对牌（含窥看的那一张）时先盖回再作为新一手第 1 张翻开。
        /// </summary>
        public PetFlipBoardStepResult UseSpiritEyePeek(int slotIndex)
        {
            if (IsCompleted)
                return PetFlipBoardStepResult.Rejected(PetFlipRejectReason.RoundAlreadyCompleted);

            if (RemainingSpiritEyeCharges <= 0 || HandState != PetFlipHandState.WaitingFirstCard)
                return PetFlipBoardStepResult.Rejected(PetFlipRejectReason.SpiritEyeUnavailable);

            if (slotIndex < 0 || slotIndex >= _patterns.Length)
                return PetFlipBoardStepResult.Rejected(PetFlipRejectReason.InvalidSlotIndex);

            if (_states[slotIndex] == PetFlipCardState.Matched)
                return PetFlipBoardStepResult.Rejected(PetFlipRejectReason.CardAlreadyMatched);

            RemainingSpiritEyeCharges--;
            _states[slotIndex] = PetFlipCardState.FaceUp;
            FirstSelectedSlotIndex = slotIndex;
            SecondSelectedSlotIndex = -1;
            LastMatchedPattern = null;
            HandState = PetFlipHandState.SpiritEyePeeking;

            return new PetFlipBoardStepResult(
                PetFlipActionOutcome.SpiritEyePeeked,
                PetFlipRejectReason.None,
                flippedSlotIndex: slotIndex,
                firstHandSlotIndex: slotIndex,
                flippedPattern: _patterns[slotIndex]);
        }

        /// <summary>同步执行点击某个槽位，立即更新权威盘面与翻牌次数。</summary>
        public PetFlipBoardStepResult FlipCard(int slotIndex)
        {
            if (IsCompleted)
                return PetFlipBoardStepResult.Rejected(PetFlipRejectReason.RoundAlreadyCompleted);

            if (slotIndex < 0 || slotIndex >= _patterns.Length)
                return PetFlipBoardStepResult.Rejected(PetFlipRejectReason.InvalidSlotIndex);

            if (_states[slotIndex] == PetFlipCardState.Matched)
                return PetFlipBoardStepResult.Rejected(PetFlipRejectReason.CardAlreadyMatched);

            switch (HandState)
            {
                case PetFlipHandState.WaitingFirstCard:
                    _states[slotIndex] = PetFlipCardState.FaceUp;
                    FirstSelectedSlotIndex = slotIndex;
                    SecondSelectedSlotIndex = -1;
                    LastMatchedPattern = null;
                    HandState = PetFlipHandState.WaitingSecondCard;
                    return new PetFlipBoardStepResult(
                        PetFlipActionOutcome.FirstCardFlipped,
                        PetFlipRejectReason.None,
                        flippedSlotIndex: slotIndex,
                        firstHandSlotIndex: slotIndex,
                        flippedPattern: _patterns[slotIndex]);

                case PetFlipHandState.SpiritEyePeeking:
                {
                    int peekedSlot = FirstSelectedSlotIndex;
                    _states[peekedSlot] = PetFlipCardState.FaceDown;

                    // 先盖回灵目窥看牌，再把本次点击作为新一手第 1 张翻开（允许点刚窥看的那张）。
                    _states[slotIndex] = PetFlipCardState.FaceUp;
                    FirstSelectedSlotIndex = slotIndex;
                    SecondSelectedSlotIndex = -1;
                    LastMatchedPattern = null;
                    HandState = PetFlipHandState.WaitingSecondCard;

                    return new PetFlipBoardStepResult(
                        PetFlipActionOutcome.SpiritEyeClearedAndFirstCardFlipped,
                        PetFlipRejectReason.None,
                        flippedSlotIndex: slotIndex,
                        firstHandSlotIndex: slotIndex,
                        coveredFirstSlotIndex: peekedSlot,
                        flippedPattern: _patterns[slotIndex]);
                }

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
                    LastMatchedPattern = null;
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
                        CurrentStreak++;
                        if (CurrentStreak > MaxStreak)
                            MaxStreak = CurrentStreak;
                        LastMatchedPattern = secondPattern;
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

                    CurrentStreak = 0;
                    LastMatchedPattern = null;
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

        /// <summary>构建当前全部卡牌的不可变显示快照；暗牌不暴露图案。</summary>
        public IReadOnlyList<PetFlipCardSnapshot> BuildCardSnapshots(bool boardInteractive)
        {
            var snapshots = new PetFlipCardSnapshot[_patterns.Length];
            for (int i = 0; i < _patterns.Length; i++)
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
