using System;
using System.Collections.Generic;
using Match;
using MmorpgClient.Game.Ranked;
using MmorpgClient.Game.Season;
using MmorpgClient.Game.Team;
using MmorpgClient.Game.TrialJournal;

namespace MmorpgClient.Game.CoreLoop
{
    /// <summary>首领试炼入口卡片视图（纯 C# 只读，零 UnityEngine 依赖）。</summary>
    public sealed class BossTrialCardView
    {
        public uint BossTrialKind { get; }
        public uint NormalDungeonId { get; }
        public uint HardDungeonId { get; }
        public string BossName { get; }
        public string GuardTag { get; }
        public string CoreMechanicSummary { get; }
        public string CounterHint { get; }
        public uint NormalRewardUnits { get; }
        public uint HardRewardUnits { get; }
        public bool HardUnlocked { get; }
        public string HardLockHint { get; }
        public bool IsHardOnly => NormalDungeonId == 0u && HardDungeonId != 0u;

        public BossTrialCardView(
            uint bossTrialKind,
            uint normalDungeonId,
            uint hardDungeonId,
            string bossName,
            string guardTag,
            string coreMechanicSummary,
            string counterHint,
            uint normalRewardUnits,
            uint hardRewardUnits,
            bool hardUnlocked,
            string hardLockHint)
        {
            BossTrialKind = bossTrialKind;
            NormalDungeonId = normalDungeonId;
            HardDungeonId = hardDungeonId;
            BossName = bossName ?? "";
            GuardTag = guardTag ?? "";
            CoreMechanicSummary = coreMechanicSummary ?? "";
            CounterHint = counterHint ?? "";
            NormalRewardUnits = normalRewardUnits;
            HardRewardUnits = hardRewardUnits;
            HardUnlocked = hardUnlocked;
            HardLockHint = hardLockHint ?? "";
        }
    }

    public enum PvpScaleSlotStatus
    {
        Open = 0,
        PrimaryUpcoming = 1,
        SpecialModeClosed = 2,
        SpecialModeOpen = 3,
    }

    /// <summary>正式人宠会武 1V1–5V5 规模档位视图（以服务端 AllowedTeamSizes / PrimaryTeamSizes / SpecialTeamSizes 为真源）。</summary>
    public sealed class PvpScaleSlotView
    {
        public uint TeamSize { get; }
        public string Label { get; }
        public PvpScaleSlotStatus Status { get; }
        public bool ServerOpen { get; }
        public bool IsSpecialMode { get; }
        public bool CanEnterWithCurrentParty { get; }
        public string BadgeText { get; }
        public string BlockedReason { get; }

        public PvpScaleSlotView(
            uint teamSize,
            string label,
            PvpScaleSlotStatus status,
            bool serverOpen,
            bool isSpecialMode,
            bool canEnterWithCurrentParty,
            string badgeText,
            string blockedReason)
        {
            TeamSize = teamSize;
            Label = label ?? "";
            Status = status;
            ServerOpen = serverOpen;
            IsSpecialMode = isSpecialMode;
            CanEnterWithCurrentParty = canEnterWithCurrentParty;
            BadgeText = badgeText ?? "";
            BlockedReason = blockedReason ?? "";
        }
    }

    /// <summary>首季轮换娱乐场卡片视图（全部免费 3×3 人宠，不计正式评分）。</summary>
    public sealed class RotationCardView
    {
        public string ModeKey { get; }
        public string Title { get; }
        public string WindowLabel { get; }
        public string RulesSummary { get; }
        public string RewardSummary { get; }
        public uint DungeonConfigId { get; }
        public bool IsPvp { get; }
        public uint FixedTeamSize { get; }
        public bool UsesFreePreset { get; }
        public bool ActiveNow { get; }

        public RotationCardView(
            string modeKey,
            string title,
            string windowLabel,
            string rulesSummary,
            string rewardSummary,
            uint dungeonConfigId,
            bool isPvp,
            uint fixedTeamSize,
            bool usesFreePreset,
            bool activeNow)
        {
            ModeKey = modeKey ?? "";
            Title = title ?? "";
            WindowLabel = windowLabel ?? "";
            RulesSummary = rulesSummary ?? "";
            RewardSummary = rewardSummary ?? "";
            DungeonConfigId = dungeonConfigId;
            IsPvp = isPvp;
            FixedTeamSize = fixedTeamSize;
            UsesFreePreset = usesFreePreset;
            ActiveNow = activeNow;
        }
    }

    /// <summary>战斗内单条公开预告视图（对应 BattleTelegraphEntry，兼容单目标与 3–5 人裂地双劈多目标）。</summary>
    public sealed class BattleTelegraphView
    {
        public ulong SourceActorId { get; }
        public ulong TargetActorId { get; }
        public IReadOnlyList<ulong> TargetActorIds { get; }
        public uint ExecuteRound { get; }
        public uint IntentKind { get; }
        public uint Status { get; }
        public string IntentTitle { get; }
        public string StatusTag { get; }
        public string DetailText { get; }
        public bool ExecutesThisRound => Status == 2u;
        public bool IsDualTarget => TargetActorIds != null && TargetActorIds.Count >= 2;

        public BattleTelegraphView(
            ulong sourceActorId,
            ulong targetActorId,
            uint executeRound,
            uint intentKind,
            uint status,
            string intentTitle,
            string statusTag,
            string detailText,
            IReadOnlyList<ulong> targetActorIds = null)
        {
            SourceActorId = sourceActorId;
            TargetActorId = targetActorId;
            if (targetActorIds != null && targetActorIds.Count > 0)
                TargetActorIds = targetActorIds;
            else if (targetActorId != 0ul)
                TargetActorIds = new[] { targetActorId };
            else
                TargetActorIds = Array.Empty<ulong>();
            ExecuteRound = executeRound;
            IntentKind = intentKind;
            Status = status;
            IntentTitle = intentTitle ?? "";
            StatusTag = statusTag ?? "";
            DetailText = detailText ?? "";
        }
    }

    /// <summary>BATTLE_EVENT_BOSS_MECHANIC(22) 的纯 C# 播报视图（供战斗演出横幅/飘字层直接消费）。</summary>
    public sealed class BossMechanicBannerView
    {
        public uint MechanicKind { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public ulong Value { get; }
        public bool IsPositiveForPlayer { get; }

        public BossMechanicBannerView(uint mechanicKind, string title, string subtitle, ulong value, bool isPositiveForPlayer)
        {
            MechanicKind = mechanicKind;
            Title = title ?? "";
            Subtitle = subtitle ?? "";
            Value = value;
            IsPositiveForPlayer = isPositiveForPlayer;
        }
    }

    /// <summary>首领试炼 V2 结算页战法高光徽章视图（严格派生自 BattleTrialJournalFacts）。</summary>
    public sealed class BossTrialHighlightBadge
    {
        public string Key { get; }
        public string Title { get; }
        public string Detail { get; }

        public BossTrialHighlightBadge(string key, string title, string detail)
        {
            Key = key ?? "";
            Title = title ?? "";
            Detail = detail ?? "";
        }
    }

    /// <summary>权威战后战术复盘视图：帮助玩家 3 秒看懂胜负原因与下一局能改变的一手（零本地推算）。</summary>
    public sealed class BattleTacticalReviewView
    {
        public bool IsVictory { get; }
        public string ModeTag { get; }
        public string ReasonHeadline { get; }
        public string NextMoveTip { get; }
        public bool HasContent => !string.IsNullOrEmpty(ReasonHeadline) || !string.IsNullOrEmpty(NextMoveTip);

        public BattleTacticalReviewView(bool isVictory, string modeTag, string reasonHeadline, string nextMoveTip)
        {
            IsVictory = isVictory;
            ModeTag = modeTag ?? "";
            ReasonHeadline = reasonHeadline ?? "";
            NextMoveTip = nextMoveTip ?? "";
        }
    }

    /// <summary>首领试炼「周常五行词缀」只读展示视图（纯 C# 零 UnityEngine 依赖；战前按服务端周历预览，战中严格读取 BattleStateS2C.BossWeeklyAffixId）。</summary>
    public sealed class BossTrialWeeklyAffixView
    {
        public uint AffixId { get; }
        public string ElementKey { get; }
        public string ElementTag { get; }
        public string Title { get; }
        public string BoonText { get; }
        public string ChallengeText { get; }
        public string RecommendedLoadoutHint { get; }
        public string ShortChipText { get; }
        public string ResetCountdownText { get; }

        public BossTrialWeeklyAffixView(
            uint affixId,
            string elementKey,
            string elementTag,
            string title,
            string boonText,
            string challengeText,
            string recommendedLoadoutHint,
            string shortChipText,
            string resetCountdownText = "")
        {
            AffixId = affixId;
            ElementKey = elementKey ?? "";
            ElementTag = elementTag ?? "";
            Title = title ?? "";
            BoonText = boonText ?? "";
            ChallengeText = challengeText ?? "";
            RecommendedLoadoutHint = recommendedLoadoutHint ?? "";
            ShortChipText = shortChipText ?? "";
            ResetCountdownText = resetCountdownText ?? "";
        }
    }

    /// <summary>首领试炼周常五行词缀纯目录与 UTC+8 周一 05:00 确定性映射（与 C++/Java 双服逐毫秒一致）。</summary>
    public static class BossTrialWeeklyAffixCatalog
    {
        public const ulong DayMs = 86_400_000UL;
        public const ulong WeekMs = 7UL * DayMs;
        public const ulong FirstMonday05Utc8Ms = 3UL * DayMs + 21UL * 3_600_000UL;
        public const uint MaxAffixId = 5u;

        public static uint AffixIdForTimestamp(ulong nowMs)
        {
            if (nowMs < FirstMonday05Utc8Ms) return 0u;
            ulong weekIndex = (nowMs - FirstMonday05Utc8Ms) / WeekMs;
            return (uint)((weekIndex % MaxAffixId) + 1UL);
        }

        public static ulong NextWeekResetAtMs(ulong nowMs)
        {
            if (nowMs < FirstMonday05Utc8Ms) return FirstMonday05Utc8Ms;
            ulong weekIndex = (nowMs - FirstMonday05Utc8Ms) / WeekMs;
            return FirstMonday05Utc8Ms + (weekIndex + 1UL) * WeekMs;
        }

        public static string FormatResetCountdown(ulong nowMs, ulong nextResetMs)
        {
            if (nextResetMs <= nowMs) return "即将轮换";
            ulong remainSec = (nextResetMs - nowMs) / 1000UL;
            ulong days = remainSec / 86400UL;
            ulong hours = (remainSec % 86400UL) / 3600UL;
            if (days > 0UL) return $"{days}天{hours}小时后轮换";
            ulong mins = (remainSec % 3600UL) / 60UL;
            return hours > 0UL ? $"{hours}小时{mins}分后轮换" : $"{Math.Max(1UL, mins)}分钟后轮换";
        }

        public static BossTrialWeeklyAffixView Describe(uint affixId, ulong nowMs = 0ul, ulong explicitNextResetMs = 0ul)
        {
            if (affixId < 1u || affixId > MaxAffixId) return null;
            string countdown = "";
            if (nowMs >= FirstMonday05Utc8Ms)
            {
                ulong nextReset = explicitNextResetMs > nowMs ? explicitNextResetMs : NextWeekResetAtMs(nowMs);
                countdown = FormatResetCountdown(nowMs, nextReset);
            }
            return affixId switch
            {
                1u => new BossTrialWeeklyAffixView(
                    1u, "metal", "金", "金 · 锋芒破阵周",
                    "攻破绽暴击 +15% · 主目标聚火 +10%",
                    "敌方开局获 8% 金甲护盾",
                    "破绽爆发/破甲方案 + 单点破盾宠",
                    "周常【金·锋芒破阵】破绽暴+15%/主点+10% · 敌开局8%盾",
                    countdown),
                2u => new BossTrialWeeklyAffixView(
                    2u, "wood", "木", "木 · 灵脉涤尘周",
                    "驱散/净化回 12% 法力 · 全队两回合 +10% 伤害",
                    "首领第 2/5/8 回合末未受控且未断疗时回 4% 血",
                    "驱散 A34 / 断脉 A35 方案 + 净化灵宠",
                    "周常【木·灵脉涤尘】驱散回蓝12%/全伤+10% · 首领周期回血",
                    countdown),
                3u => new BossTrialWeeklyAffixView(
                    3u, "water", "水", "水 · 蓄势聚灵周",
                    "明牌蓄力技与毒火伤害 +20% · 对应技能耗蓝 -20%",
                    "首领偶数回合末未被控锁时额外 +1 层蓄势",
                    "蓄力爆发(E01/E02)/毒火方案 + 驱散副手",
                    "周常【水·蓄势聚灵】蓄力/毒火+20%省蓝20% · 首领偶回蓄势",
                    countdown),
                4u => new BossTrialWeeklyAffixView(
                    4u, "fire", "火", "火 · 烈焰连携周",
                    "灵宠直攻/人物追击 +18% · 打破绽附 3% 灼伤(封顶1200)",
                    "首领 <=60% 气血提前进入二阶段破釜",
                    "猛攻/连击灵宠方案 + 追击爆发方案",
                    "周常【火·烈焰连携】宠/追击+18%破绽灼伤 · 首领60%破釜",
                    countdown),
                5u => new BossTrialWeeklyAffixView(
                    5u, "earth", "土", "土 · 守御反震周",
                    "化劲破绽赐全队 8% 护盾 · 下回合全伤 +12%",
                    "首领慢重劈与强法重击基础伤害 +12%",
                    "守御/双罩/反震方案 + 护主铁壁宠",
                    "周常【土·守御反震】化劲赐全队8%盾/+12%伤 · 重劈+12%",
                    countdown),
                _ => null,
            };
        }

        public static BossTrialWeeklyAffixView ForTimestamp(ulong nowMs, ulong explicitWeekStartMs = 0ul, ulong explicitNextResetMs = 0ul)
        {
            ulong anchorMs = explicitWeekStartMs >= FirstMonday05Utc8Ms ? explicitWeekStartMs : nowMs;
            uint id = AffixIdForTimestamp(anchorMs);
            return Describe(id, nowMs, explicitNextResetMs);
        }
    }

    /// <summary>战斗内核心玩法 HUD 派生视图（预告 + 首领试炼 V2 破绽/蓄势/护势/二阶段/周常词缀 + 物法易势换势条 + 护卫救援保全目标）。</summary>
    public sealed class BattleCoreHudView
    {
        public uint RoundIndex { get; }
        public IReadOnlyList<BattleTelegraphView> Telegraphs { get; }
        public uint ActivePolarity { get; }
        public uint NextPolaritySwitchRound { get; }
        public string PolarityBannerText { get; }
        public ulong ProtectedGuardActorId { get; }
        public string ProtectedGuardBannerText { get; }
        public uint BossPhase { get; }
        public uint BossMomentumStacks { get; }
        public uint BossStanceType { get; }
        public uint BossStanceRemainRounds { get; }
        public uint BossExposedPct { get; }
        public uint BossExposedUntilRound { get; }
        public uint BossTrialKind { get; }
        public uint BattleDifficulty { get; }
        public string BossPhaseBannerText { get; }
        public string BossStatusSummaryText { get; }
        public uint BossWeeklyAffixId { get; }
        public BossTrialWeeklyAffixView BossWeeklyAffix { get; }

        public bool HasTelegraphs => Telegraphs != null && Telegraphs.Count > 0;
        public bool HasPolarity => ActivePolarity != 0;
        public bool HasProtectedGuard => ProtectedGuardActorId != 0;
        public bool IsBossTrial => CoreGameplayLoopModels.IsBossTrialKind(BossTrialKind);
        public bool IsBossPhaseTwo => BossPhase >= 2u;
        public bool IsBossExposed => BossExposedPct > 0u;
        public bool HasBossStance => BossStanceType != 0u && BossStanceRemainRounds > 0u;
        public bool HasBossWeeklyAffix => BossWeeklyAffix != null && BossWeeklyAffixId != 0u;

        public BattleCoreHudView(
            uint roundIndex,
            IReadOnlyList<BattleTelegraphView> telegraphs,
            uint activePolarity,
            uint nextPolaritySwitchRound,
            string polarityBannerText,
            ulong protectedGuardActorId,
            string protectedGuardBannerText,
            uint bossPhase = 0u,
            uint bossMomentumStacks = 0u,
            uint bossStanceType = 0u,
            uint bossStanceRemainRounds = 0u,
            uint bossExposedPct = 0u,
            uint bossExposedUntilRound = 0u,
            uint bossTrialKind = 0u,
            uint battleDifficulty = 0u,
            string bossPhaseBannerText = "",
            string bossStatusSummaryText = "",
            uint bossWeeklyAffixId = 0u)
        {
            RoundIndex = roundIndex;
            Telegraphs = telegraphs;
            ActivePolarity = activePolarity;
            NextPolaritySwitchRound = nextPolaritySwitchRound;
            PolarityBannerText = polarityBannerText ?? "";
            ProtectedGuardActorId = protectedGuardActorId;
            ProtectedGuardBannerText = protectedGuardBannerText ?? "";
            BossPhase = bossPhase;
            BossMomentumStacks = bossMomentumStacks;
            BossStanceType = bossStanceType;
            BossStanceRemainRounds = bossStanceRemainRounds;
            BossExposedPct = bossExposedPct;
            BossExposedUntilRound = bossExposedUntilRound;
            BossTrialKind = bossTrialKind;
            BattleDifficulty = battleDifficulty;
            BossPhaseBannerText = bossPhaseBannerText ?? "";
            BossStatusSummaryText = bossStatusSummaryText ?? "";
            BossWeeklyAffixId = bossWeeklyAffixId;
            BossWeeklyAffix = BossTrialWeeklyAffixCatalog.Describe(bossWeeklyAffixId);
        }
    }

    /// <summary>单局结算材料收益视图（区分基础/加量、可交易/绑定、PVP 反挂机不发奖与阶段一/二账本）。</summary>
    public sealed class CoreSettlementRewardView
    {
        public uint TotalUnitsAdded { get; }
        public uint BaseUnitsAdded { get; }
        public uint BonusUnitsAdded { get; }
        public uint TradableUnitsAdded { get; }
        public uint BoundUnitsAdded { get; }
        public bool PvpLossIneligible { get; }
        public bool WeeklyPhaseActive { get; }
        public uint LifetimeEarned { get; }
        public uint LifetimeLimit { get; }
        public uint TradableEarnedTotal { get; }
        public uint TradableLifetimeLimit { get; }
        public uint EarnedThisWeek { get; }
        public uint WeeklyLimit { get; }
        public ulong ClaimableUnits { get; }
        public string Headline { get; }
        public string DetailNote { get; }

        public CoreSettlementRewardView(
            uint totalUnitsAdded,
            uint baseUnitsAdded,
            uint bonusUnitsAdded,
            uint tradableUnitsAdded,
            uint boundUnitsAdded,
            bool pvpLossIneligible,
            bool weeklyPhaseActive,
            uint lifetimeEarned,
            uint lifetimeLimit,
            uint tradableEarnedTotal,
            uint tradableLifetimeLimit,
            uint earnedThisWeek,
            uint weeklyLimit,
            ulong claimableUnits,
            string headline,
            string detailNote)
        {
            TotalUnitsAdded = totalUnitsAdded;
            BaseUnitsAdded = baseUnitsAdded;
            BonusUnitsAdded = bonusUnitsAdded;
            TradableUnitsAdded = tradableUnitsAdded;
            BoundUnitsAdded = boundUnitsAdded;
            PvpLossIneligible = pvpLossIneligible;
            WeeklyPhaseActive = weeklyPhaseActive;
            LifetimeEarned = lifetimeEarned;
            LifetimeLimit = lifetimeLimit;
            TradableEarnedTotal = tradableEarnedTotal;
            TradableLifetimeLimit = tradableLifetimeLimit;
            EarnedThisWeek = earnedThisWeek;
            WeeklyLimit = weeklyLimit;
            ClaimableUnits = claimableUnits;
            Headline = headline ?? "";
            DetailNote = detailNote ?? "";
        }
    }

    /// <summary>低打扰、零卖数值的小额消费/交易预留位视图（严禁每日打卡红点与弹窗）。</summary>
    public sealed class SmallSpendSlotView
    {
        public string SlotKey { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public string StatusBadge { get; }
        public bool PaymentEnabled { get; }
        public bool ZeroStatGuarantee { get; }

        public SmallSpendSlotView(
            string slotKey,
            string title,
            string subtitle,
            string statusBadge,
            bool paymentEnabled,
            bool zeroStatGuarantee)
        {
            SlotKey = slotKey ?? "";
            Title = title ?? "";
            Subtitle = subtitle ?? "";
            StatusBadge = statusBadge ?? "";
            PaymentEnabled = paymentEnabled;
            ZeroStatGuarantee = zeroStatGuarantee;
        }
    }

    /// <summary>主城总览视图：只展示 3 个核心数字 + 4 个低打扰非数值预留位，无每日待清列表。</summary>
    public sealed class MainCityHubView
    {
        public string MaterialProgressLabel { get; }
        public string MaterialProgressValue { get; }
        public ulong ClaimableMaterialUnits { get; }
        public string FormalPvpLabel { get; }
        public string FormalPvpValue { get; }
        public string SeasonAndJournalLabel { get; }
        public string SeasonAndJournalValue { get; }
        public IReadOnlyList<SmallSpendSlotView> SmallSpendSlots { get; }

        public MainCityHubView(
            string materialProgressLabel,
            string materialProgressValue,
            ulong claimableMaterialUnits,
            string formalPvpLabel,
            string formalPvpValue,
            string seasonAndJournalLabel,
            string seasonAndJournalValue,
            IReadOnlyList<SmallSpendSlotView> smallSpendSlots)
        {
            MaterialProgressLabel = materialProgressLabel ?? "";
            MaterialProgressValue = materialProgressValue ?? "";
            ClaimableMaterialUnits = claimableMaterialUnits;
            FormalPvpLabel = formalPvpLabel ?? "";
            FormalPvpValue = formalPvpValue ?? "";
            SeasonAndJournalLabel = seasonAndJournalLabel ?? "";
            SeasonAndJournalValue = seasonAndJournalValue ?? "";
            SmallSpendSlots = smallSpendSlots;
        }
    }

    /// <summary>《五行奇谈》核心玩法 V1 纯 C# 视图推导工具（零 UnityEngine，EditMode 可直接单测）。</summary>
    public static class CoreGameplayLoopModels
    {
        public const uint Boss1RejuvenateNormalDungeonId = 101u;
        public const uint Boss1RejuvenateHardDungeonId = 102u;
        public const uint Boss1StanceNormalDungeonId = 103u;
        public const uint Boss1StanceHardDungeonId = 104u;
        public const uint Boss2ShieldNormalDungeonId = 105u;
        public const uint Boss2ShieldHardDungeonId = 106u;
        public const uint Boss3PoisonNormalDungeonId = 107u;
        public const uint Boss3PoisonHardDungeonId = 108u;
        public const uint GuardRescueDungeonId = 109u;
        public const uint Polarity5v5DungeonId = 110u;
        public const uint Boss1DualGuardHardDungeonId = 112u;
        public const uint BossTrialKindDualGuard = 8u;

        public const string HardLockDefaultHint = "通关任意一只普通首领试炼后开放困难难度";

        public static bool IsBossTrialKind(uint bossTrialKind) =>
            (bossTrialKind >= 1u && bossTrialKind <= 4u) || bossTrialKind == BossTrialKindDualGuard;

        public static bool IsHardBossTrialDungeon(uint dungeonConfigId) =>
            dungeonConfigId == Boss1RejuvenateHardDungeonId
            || dungeonConfigId == Boss1StanceHardDungeonId
            || dungeonConfigId == Boss2ShieldHardDungeonId
            || dungeonConfigId == Boss3PoisonHardDungeonId
            || dungeonConfigId == Boss1DualGuardHardDungeonId;

        /// <summary>生成 3 只不同机制首领（4 组普通/困难变体，Dungeon 101–108）卡片列表。</summary>
        public static IReadOnlyList<BossTrialCardView> BuildBossTrialCards(global::TrialJournalSnapshot journal)
        {
            bool hardUnlocked = journal != null && journal.HardBossTrialUnlocked;
            string lockHint = hardUnlocked ? "" : HardLockDefaultHint;
            return new[]
            {
                new BossTrialCardView(
                    1u, Boss1RejuvenateNormalDungeonId, Boss1RejuvenateHardDungeonId,
                    "重甲首领 · 回春阵", "回春护卫",
                    "群攻主点 100% / 溅射 60%；化解重劈触发破绽 +35%，打断或击杀回春触发 6% 反噬破绽。",
                    "点名重劈按防御/护盾化劲；优先集火或控断回春护卫，困难模式可用断脉咒压制净化。",
                    10u, 15u, hardUnlocked, lockHint),
                new BossTrialCardView(
                    2u, Boss1StanceNormalDungeonId, Boss1StanceHardDungeonId,
                    "重甲首领 · 护势阵", "护势护卫",
                    "护势联防阵庇护全体敌方（35%/40%），同系直伤触发轻反制叠蓄势；困难按前 2 回合顺势应变。",
                    "避开当前护势系直伤，或用驱散技能破阵触发「崩势」令首领与护卫同时陷入破绽 +35%。",
                    10u, 15u, hardUnlocked, lockHint),
                new BossTrialCardView(
                    3u, Boss2ShieldNormalDungeonId, Boss2ShieldHardDungeonId,
                    "玄盾首领", "灵障护卫",
                    "灵障护卫周期蓄盾，击破护盾立即触发「碎障破绽 +35%」；首领预告慢速法术重击，化解同样触发破绽。",
                    "主点首领聚火破盾打爆发；被法术重击点名时一键化劲防御或控断灵障护卫。",
                    10u, 15u, hardUnlocked, lockHint),
                new BossTrialCardView(
                    4u, Boss3PoisonNormalDungeonId, Boss3PoisonHardDungeonId,
                    "幽毒首领", "玄毒护卫",
                    "玄毒护卫封灵吸蓝，首领预告慢速群体玄毒；全员防御/护盾或控断触发破绽 +35%（半数化劲 +20%）。",
                    "群体玄毒当回合用一键化劲防御或控场打断触发破绽窗口；及时净化破甲与控断缚灵护卫。",
                    10u, 15u, hardUnlocked, lockHint),
            };
        }

        /// <summary>生成困难专属「重甲首领 · 双卫同阵」（Dungeon 112，boss_trial_kind = 8）第 5 张高难卡片。</summary>
        public static BossTrialCardView BuildDualGuardBossTrialCard(global::TrialJournalSnapshot journal)
        {
            bool hardUnlocked = journal != null && journal.HardBossTrialUnlocked;
            string lockHint = hardUnlocked ? "" : HardLockDefaultHint;
            return new BossTrialCardView(
                BossTrialKindDualGuard, 0u, Boss1DualGuardHardDungeonId,
                "重甲首领 · 双卫同阵", "回春 + 护势",
                "仅困难开放：回春与护势双核心护卫（75% 血）同场错峰运转（R1 护势 / R2 重劈 / R3 回春），3–5 人追加破军卫与裂地双劈。",
                "开局按需先断回春或先崩护势，双卫全灭或首领半血转二阶段决战姿态；胜局 15 份材料并解锁试炼志专属「破阵双绝」徽章。",
                0u, 15u, hardUnlocked, lockHint);
        }

        /// <summary>生成首领试炼 V2 完整 5 卡片列表（4 组常规普通/困难 + 第 5 张仅困难双卫同阵，供 V2 UI 使用）。</summary>
        public static IReadOnlyList<BossTrialCardView> BuildBossTrialCardsV2(global::TrialJournalSnapshot journal)
        {
            var baseCards = BuildBossTrialCards(journal);
            var list = new List<BossTrialCardView>(baseCards.Count + 1);
            for (int i = 0; i < baseCards.Count; i++) list.Add(baseCards[i]);
            list.Add(BuildDualGuardBossTrialCard(journal));
            return list;
        }

        /// <summary>生成正式人宠会武 1V1–5V5 五档规模卡片（严格读取服务端开放开关与主力/特殊玩法分类）。</summary>
        public static IReadOnlyList<PvpScaleSlotView> BuildFormalPvpScales(
            RankedProfileSnapshot profile,
            uint currentPartySize = 1u)
        {
            uint partySize = currentPartySize == 0 ? 1u : currentPartySize;
            var result = new List<PvpScaleSlotView>(5);
            for (uint size = 1u; size <= 5u; size++)
            {
                bool open = profile != null ? profile.IsTeamSizeOpen(size) : (size == 1u || size == 3u);
                bool special = profile != null ? profile.IsSpecialTeamSize(size) : (size == 2u || size == 4u);
                PvpScaleSlotStatus status;
                string badge;
                if (special)
                {
                    status = open ? PvpScaleSlotStatus.SpecialModeOpen : PvpScaleSlotStatus.SpecialModeClosed;
                    badge = open ? "特殊玩法 · 开放中" : "特殊玩法 · 暂未开放";
                }
                else
                {
                    status = open ? PvpScaleSlotStatus.Open : PvpScaleSlotStatus.PrimaryUpcoming;
                    badge = open ? "常驻 · 开放中" : "即将开放";
                }

                string blocked = "";
                if (!open)
                    blocked = special ? $"{size}V{size} 为特殊玩法模式，当前未开放" : $"{size}V{size} 暂未开放";
                else if (size < partySize)
                    blocked = $"当前队伍已有 {partySize} 人";
                else if (TeamClient.PvpPartySizeForbidden(size, partySize))
                    blocked = "再邀一位好友即可报名";

                result.Add(new PvpScaleSlotView(
                    size,
                    $"{size}V{size}",
                    status,
                    open,
                    special,
                    blocked.Length == 0,
                    badge,
                    blocked));
            }
            return result;
        }

        /// <summary>
        /// 按服务端权威赛季起点与当前服务器时刻推导轮换周序号（1..8）；
        /// 当首测未锁定开季时间戳（StartsAtMs == 0）或赛季不可用时返回 false，调用方不得伪造当前周次。
        /// </summary>
        public static bool TryResolveAuthoritativeSeasonWeek(global::SeasonStateSnapshot seasonState, out uint week)
        {
            week = 0u;
            if (seasonState == null || !seasonState.Available || seasonState.StartsAtMs == 0ul
                || seasonState.ServerNowMs < seasonState.StartsAtMs)
                return false;
            const ulong weekMs = 7ul * 24ul * 3600ul * 1000ul;
            ulong index = (seasonState.ServerNowMs - seasonState.StartsAtMs) / weekMs;
            week = index >= 7ul ? 8u : (uint)(index + 1ul);
            return true;
        }

        /// <summary>生成首季八周轮换娱乐场两张卡片：护卫救援（PVE 免费人宠）与物法易势（仅 5V5 公平团战免费人宠）。</summary>
        public static IReadOnlyList<RotationCardView> BuildRotationCards(uint activeSeasonWeek = 1u)
        {
            bool rescueActive = activeSeasonWeek <= 4u;
            bool polarityActive = activeSeasonWeek >= 5u && activeSeasonWeek <= 8u;
            return new[]
            {
                new RotationCardView(
                    "guard_rescue",
                    "护卫救援",
                    "首季前 4 周 · 娱乐轮换（PVE 1–5 人）",
                    "借用免费 3×3 人宠出战；击败首领同时保全回春护卫（不计正式评分）。",
                    "合格胜局：基础 10 份（含至多 1 份可交易）",
                    GuardRescueDungeonId,
                    isPvp: false,
                    fixedTeamSize: 0u,
                    usesFreePreset: true,
                    activeNow: rescueActive),
                new RotationCardView(
                    "polarity_5v5",
                    "物法易势 5V5",
                    "首季后 4 周 · 娱乐轮换（仅 5V5）",
                    "免费 3×3 人宠公平 5V5 团战，每 2 回合切换物势/法势（不计正式评分，不与正式自养排位混用）。",
                    "胜 15 份（含至多 1 份可交易）；有效负/平 10 份全绑定",
                    Polarity5v5DungeonId,
                    isPvp: true,
                    fixedTeamSize: 5u,
                    usesFreePreset: true,
                    activeNow: polarityActive),
            };
        }

        /// <summary>从 BattleStateS2C 派生战斗内公开预告、首领试炼 V2 破绽/蓄势/护势/二阶段、物法易势横幅与护卫救援保全提示。</summary>
        public static BattleCoreHudView BuildBattleCoreHud(global::BattleStateS2C state)
        {
            if (state == null)
                return new BattleCoreHudView(0u, Array.Empty<BattleTelegraphView>(), 0u, 0u, "", 0ul, "");

            var telegraphs = new List<BattleTelegraphView>(state.Telegraphs.Count);
            for (int i = 0; i < state.Telegraphs.Count; i++)
            {
                var t = state.Telegraphs[i];
                if (t == null) continue;
                string title = TelegraphIntentTitle(t.IntentKind);
                string statusTag = TelegraphStatusLabel(t.Status);
                string detail = !string.IsNullOrEmpty(t.Label)
                    ? t.Label
                    : $"第 {t.ExecuteRound} 回合 · {title}（{statusTag}）";
                IReadOnlyList<ulong> multiTargets = null;
                if (t.TargetActorIds != null && t.TargetActorIds.Count > 0)
                {
                    var copied = new ulong[t.TargetActorIds.Count];
                    for (int k = 0; k < t.TargetActorIds.Count; k++) copied[k] = t.TargetActorIds[k];
                    multiTargets = copied;
                }
                telegraphs.Add(new BattleTelegraphView(
                    t.SourceActorId, t.TargetActorId, t.ExecuteRound, t.IntentKind, t.Status,
                    title, statusTag, detail, multiTargets));
            }

            string polarityBanner = "";
            if (state.ActivePolarity == 1u)
            {
                polarityBanner = state.NextPolaritySwitchRound > 0
                    ? $"当前物势：物理直伤 +15%（第 {state.NextPolaritySwitchRound} 回合切换法势）"
                    : "当前物势：物理直伤 +15%";
            }
            else if (state.ActivePolarity == 2u)
            {
                polarityBanner = state.NextPolaritySwitchRound > 0
                    ? $"当前法势：法术直伤 +15%（第 {state.NextPolaritySwitchRound} 回合切换物势）"
                    : "当前法势：法术直伤 +15%";
            }

            string guardBanner = state.ProtectedGuardActorId != 0
                ? "救援目标：8 回合内击败首领并保全友方护卫存活（阵亡即失败）"
                : "";

            string bossPhaseBanner = "";
            string bossStatusSummary = "";
            if (IsBossTrialKind(state.BossTrialKind))
            {
                bossPhaseBanner = state.BossPhase >= 2u
                    ? "二阶段 · 决战姿态（破釜）：首领易伤 +15%，重劈每 2 回合循环"
                    : "一阶段 · 破阵迎敌（群攻主点 100% / 溅射 60%）";
                var parts = new List<string>(4);
                if (state.BossExposedPct > 0u)
                {
                    parts.Add(state.BossExposedUntilRound > 0u
                        ? $"破绽 +{state.BossExposedPct}%（至第 {state.BossExposedUntilRound} 回合末）"
                        : $"破绽 +{state.BossExposedPct}%");
                }
                if (state.BossMomentumStacks > 0u)
                    parts.Add($"蓄势 {state.BossMomentumStacks}/3（重劈 +{state.BossMomentumStacks * 12u}%）");
                if (state.BossStanceType == 1u && state.BossStanceRemainRounds > 0u)
                    parts.Add($"物防联防阵（余 {state.BossStanceRemainRounds} 回合，可驱散崩势）");
                else if (state.BossStanceType == 2u && state.BossStanceRemainRounds > 0u)
                    parts.Add($"法防联防阵（余 {state.BossStanceRemainRounds} 回合，可驱散崩势）");
                bossStatusSummary = parts.Count > 0 ? string.Join("  ·  ", parts) : "";
            }

            return new BattleCoreHudView(
                state.RoundIndex,
                telegraphs,
                state.ActivePolarity,
                state.NextPolaritySwitchRound,
                polarityBanner,
                state.ProtectedGuardActorId,
                guardBanner,
                state.BossPhase,
                state.BossMomentumStacks,
                state.BossStanceType,
                state.BossStanceRemainRounds,
                state.BossExposedPct,
                state.BossExposedUntilRound,
                state.BossTrialKind,
                state.BattleDifficulty,
                bossPhaseBanner,
                bossStatusSummary,
                state.BossWeeklyAffixId);
        }

        public static string TelegraphIntentTitle(uint intentKind) => intentKind switch
        {
            1u => "慢动作重劈",
            2u => "回春治疗",
            3u => "物防护势",
            4u => "法防护势",
            5u => "灵障蓄盾",
            6u => "玄毒封灵",
            7u => "灵障法术重击",
            8u => "玄毒群体冲击",
            9u => "春禧重击",
            _ => "首领蓄力",
        };

        /// <summary>
        /// 收集本回合适合展示「一键化劲防御」快捷按钮的本人或本人存活出战宠 actor_id 列表：
        /// 仅当单位仍在 PendingActorIds 且被本回合发动的慢速重劈/法术重击/春禧重击点名时返回。
        /// </summary>
        public static IReadOnlyList<ulong> CollectQuickParryDefendActorIds(ulong myPlayerId, global::BattleStateS2C state)
        {
            if (myPlayerId == 0ul || state == null || state.Outcome != eBattleOutcome.BattleOutcomeOngoing)
                return Array.Empty<ulong>();
            if (state.Telegraphs == null || state.Telegraphs.Count == 0
                || state.PendingActorIds == null || state.PendingActorIds.Count == 0)
                return Array.Empty<ulong>();

            var pending = new HashSet<ulong>(state.PendingActorIds);
            var threatened = new HashSet<ulong>();
            bool allOwnedThreatened = false;
            for (int i = 0; i < state.Telegraphs.Count; i++)
            {
                var t = state.Telegraphs[i];
                if (t == null) continue;
                bool executingNow = t.Status == 2u || (t.ExecuteRound != 0u && t.ExecuteRound == state.RoundIndex);
                if (!executingNow) continue;
                if (t.IntentKind == 8u)
                {
                    allOwnedThreatened = true;
                    continue;
                }
                if (t.IntentKind != 1u && t.IntentKind != 7u && t.IntentKind != 9u) continue;
                if (t.TargetActorIds != null && t.TargetActorIds.Count > 0)
                {
                    for (int k = 0; k < t.TargetActorIds.Count; k++)
                    {
                        if (t.TargetActorIds[k] != 0ul) threatened.Add(t.TargetActorIds[k]);
                    }
                }
                else if (t.TargetActorId != 0ul)
                {
                    threatened.Add(t.TargetActorId);
                }
            }
            if (!allOwnedThreatened && threatened.Count == 0) return Array.Empty<ulong>();

            var result = new List<ulong>(2);
            for (int i = 0; i < state.Actors.Count; i++)
            {
                var a = state.Actors[i];
                if (a == null || a.IsDead || a.Fled) continue;
                bool isOwned = a.ActorId == myPlayerId
                    || (a.ActorType == eBattleActorType.BattleActorTypePet && a.OwnerPlayerId == myPlayerId);
                if (!isOwned) continue;
                if (pending.Contains(a.ActorId) && (allOwnedThreatened || threatened.Contains(a.ActorId)))
                    result.Add(a.ActorId);
            }
            return result;
        }

        /// <summary>当前技能在目标选择预览时是否应展示首领试炼 V2「聚火 100% / 溅射 60%」角标。</summary>
        public static bool IsSplashFocusApplicable(global::BattleStateS2C state, uint skillMaxTargets) =>
            state != null && IsBossTrialKind(state.BossTrialKind) && skillMaxTargets > 1u;

        /// <summary>将 BATTLE_EVENT_BOSS_MECHANIC(22) 转换为 UI 演出横幅与飘字文案。</summary>
        public static BossMechanicBannerView DescribeBossMechanicEvent(global::BattleEventItem ev)
        {
            if (ev == null || ev.EventType != eBattleEventType.BattleEventBossMechanic) return null;
            return ev.MechanicKind switch
            {
                1u => new BossMechanicBannerView(1u, $"化劲破绽 +{ev.Value}%", "成功化解首领慢速重劈，全队爆发窗口开启", ev.Value, true),
                2u => new BossMechanicBannerView(2u, "断疗反噬 · 破绽 +35%", $"中断回春灵力反噬首领 {ev.Value} 点气血", ev.Value, true),
                3u => new BossMechanicBannerView(3u, $"崩势破盾 · 破绽 +{ev.Value}%",
                    ev.Value >= 35ul ? "破除敌方护势或灵障，首领陷入完整破绽窗口" : "控断敌方护卫蓄力，首领陷入破绽窗口", ev.Value, true),
                4u => new BossMechanicBannerView(4u, "阵脚震荡", ev.Value > 1ul ? $"鼓噪卫阵亡震荡首领 {ev.Value} 点气血并清空蓄势" : "破军卫阵亡削减首领 1 层蓄势", ev.Value, true),
                5u => new BossMechanicBannerView(5u, "决战姿态 · 破釜", "首领易伤 +15%，慢速重劈加快为每 2 回合循环", ev.Value, false),
                6u => new BossMechanicBannerView(6u, $"首领蓄势 {ev.Value}/3", $"下一次重劈伤害额外 +{ev.Value * 12ul}%（可驱散）", ev.Value, false),
                7u => new BossMechanicBannerView(7u, "破军破甲 -20%", "目标防御降低 20%（持续 2 回合，可净化）", ev.Value, false),
                8u => new BossMechanicBannerView(8u, "回春净益", "困难回春顺手清除了首领 1 项减益状态", ev.Value, false),
                9u => new BossMechanicBannerView(9u, "断脉气馁 -15%", "回春被断脉咒压制，首领下回合物攻降低 15%", ev.Value, true),
                10u => new BossMechanicBannerView(10u, "周常五行词缀生效",
                    ev.Value > 0ul ? $"触发本周首领试炼五行机制（效果 +{ev.Value}）" : "触发本周首领试炼五行机制",
                    ev.Value, true),
                _ => new BossMechanicBannerView(ev.MechanicKind, "首领机制触发", "", ev.Value, false),
            };
        }

        /// <summary>从终局权威 BattleSettlementData 派生首领试炼 V2 结算战法高光徽章列表。</summary>
        public static IReadOnlyList<BossTrialHighlightBadge> BuildBossTrialHighlightBadges(global::BattleSettlementData settlement)
        {
            if (settlement == null || settlement.TrialFacts == null) return Array.Empty<BossTrialHighlightBadge>();
            var facts = settlement.TrialFacts;
            var badges = new List<BossTrialHighlightBadge>(5);
            if (facts.CleaveParryCount > 0u)
                badges.Add(new BossTrialHighlightBadge("cleave_parry", $"化劲破招 ×{facts.CleaveParryCount}", "成功化解首领慢速重劈触发破绽窗口"));
            if (facts.HealInterruptBacklashCount > 0u)
                badges.Add(new BossTrialHighlightBadge("heal_backlash", $"断疗反噬 ×{facts.HealInterruptBacklashCount}", "控断或击杀回春护卫反噬首领气血"));
            if (facts.StanceBreakCount > 0u)
                badges.Add(new BossTrialHighlightBadge("stance_break", $"崩势破盾 ×{facts.StanceBreakCount}", "驱散或控断护势联防阵触发双破绽"));
            if (facts.AllSelfCleavesParried)
                badges.Add(new BossTrialHighlightBadge("parry_master", "化劲宗师", "本场被重劈点名 100% 合法化解"));
            bool won = (settlement.Outcome == eBattleOutcome.BattleOutcomeSideAWin && settlement.PlayerTeamIndex == 0u)
                || (settlement.Outcome == eBattleOutcome.BattleOutcomeSideBWin && settlement.PlayerTeamIndex == 1u);
            if (won && facts.InitialHumanCount == 1u && !settlement.Fled)
                badges.Add(new BossTrialHighlightBadge("solo_clear", "单骑破阵", "单人携宠独立攻克首领试炼"));
            return badges;
        }

        /// <summary>
        /// 从终局权威 BattleEndS2C + BattleSettlementData + BattleTrialJournalFacts 派生战后复盘卡：
        /// 明确交代胜负关键原因与「下一局可立即改变的一手」；非核心循环战斗返回 null 避免干扰。
        /// </summary>
        public static BattleTacticalReviewView BuildTacticalReview(global::BattleEndS2C end)
        {
            if (end == null) return null;
            var s = end.Settlement;
            if (s == null)
            {
                return end.MaterialPvpLossIneligible
                    ? new BattleTacticalReviewView(
                        false,
                        "对抗复盘",
                        "本局未满足有效对抗门槛",
                        "打满至少 3 回合、2 次手动操作并产生有效伤害/治疗/控制，负场也可领取绑定材料。")
                    : null;
            }

            eBattleOutcome outcome = s.Outcome != eBattleOutcome.BattleOutcomeOngoing ? s.Outcome : end.Outcome;
            if (outcome == eBattleOutcome.BattleOutcomeVoid)
            {
                return new BattleTacticalReviewView(
                    false,
                    "对局作废",
                    "本局已由服务端判定作废，不计胜负与奖励扣次",
                    "可立即重新发起挑战或匹配，无任何进度损失。");
            }
            if (s.Fled)
            {
                return new BattleTacticalReviewView(
                    false,
                    "主动撤离",
                    "主动撤离提前结束了本局战斗",
                    "调整装备词条、灵宠技能或赛季印后可随时重开，无次数惩罚。");
            }

            bool won = (outcome == eBattleOutcome.BattleOutcomeSideAWin && s.PlayerTeamIndex == 0u)
                || (outcome == eBattleOutcome.BattleOutcomeSideBWin && s.PlayerTeamIndex == 1u);
            bool draw = outcome == eBattleOutcome.BattleOutcomeDraw;
            var f = s.TrialFacts;
            uint bossKind = s.BossTrialKind;
            uint matchMode = s.MatchMode;

            // 1. 首领试炼（1..4 及 8 困难·双卫同阵）
            if (IsBossTrialKind(bossKind))
            {
                string tag = s.BattleDifficulty >= 2u ? "首领试炼 · 困难" : "首领试炼 · 普通";
                if (won)
                {
                    string reason;
                    if (f != null && f.AllSelfCleavesParried && f.CleaveParryCount > 0u)
                        reason = $"全程零漏防，化劲破招 {f.CleaveParryCount} 次打满爆发窗口";
                    else if (f != null && f.HealInterruptBacklashCount > 0u)
                        reason = $"精准控断回春触发 {f.HealInterruptBacklashCount} 次反噬破绽";
                    else if (f != null && f.StanceBreakCount > 0u)
                        reason = $"成功崩势破盾 {f.StanceBreakCount} 次压制首领联防";
                    else if (f != null && f.CleaveParryCount > 0u)
                        reason = $"通过 {f.CleaveParryCount} 次化劲破招稳步建立胜势";
                    else if (f != null && !f.SelfOrPetEverDied)
                        reason = $"人宠全员零阵亡，历时 {s.TotalRounds} 回合稳健过关";
                    else
                        reason = $"历时 {s.TotalRounds} 回合攻克首领试炼";

                    string next = s.BattleDifficulty <= 1u
                        ? "已可挑战困难难度（+15 份材料，含可交易份额）或尝试「困难·双卫同阵」。"
                        : (bossKind != 8u
                            ? "可前往「困难·双卫同阵」或携本套自养阵容进入正式人宠会武检验战力。"
                            : "高难双卫已克！可在试炼志领奖，或携带本套装备征战正式人宠会武。");
                    return new BattleTacticalReviewView(true, tag, reason, next);
                }

                if ((bossKind == 1u || bossKind == 8u) && f != null && f.BossGuardHealTotal > 0ul && f.HealInterruptBacklashCount == 0u)
                {
                    return new BattleTacticalReviewView(
                        false,
                        tag,
                        $"回春护卫累计回血 {f.BossGuardHealTotal}，拖慢了推进节奏",
                        "优先主点聚火回春护卫，或在预告回合用封灵/眩晕打断，直接触发 6% 反噬与破绽 +35%。");
                }
                if ((bossKind == 2u || bossKind == 8u) && f != null && !f.GuardStanceDispelled && f.StanceBreakCount == 0u)
                {
                    return new BattleTacticalReviewView(
                        false,
                        tag,
                        "未能及时破除护势联防阵，伤害被高额减免",
                        "携带驱散技（A34）或留控打断护卫蓄力，崩势瞬间触发全队破绽窗口。");
                }
                if (bossKind == 3u && f != null && !f.SelfTriggeredShieldBreak)
                {
                    return new BattleTacticalReviewView(
                        false,
                        tag,
                        "灵障护盾未能及时击破，压血节奏受阻",
                        "选用破盾单点技能，或在点名回合用「一键化劲防御」借破绽集中爆盾。");
                }
                if (bossKind == 4u && f != null && !f.SelfTriggeredAntiControlOrSeasonSkill)
                {
                    return new BattleTacticalReviewView(
                        false,
                        tag,
                        "玄毒与封灵持续打乱了队伍出手节奏",
                        "装备净化/解控技能或切换防控赛季印，群攻预告回合人宠同步化劲防御。");
                }
                if (f != null && f.CleaveParryCount == 0u)
                {
                    return new BattleTacticalReviewView(
                        false,
                        tag,
                        "未在慢速重劈点名回合化劲破招，错失 +35% 破绽窗口",
                        "看到「本回合发动」红框点名时直接点「一键化劲防御」，减伤同时开启全队爆发。");
                }
                if (s.TotalRounds >= 12u)
                {
                    return new BattleTacticalReviewView(
                        false,
                        tag,
                        $"苦战 {s.TotalRounds} 回合未能抢在回合上限前击杀首领",
                        "群攻主点聚火（主目标 100%）压低护卫并抓住破绽爆发，或先用材料升级武器词条。");
                }
                if (f != null && f.SelfOrPetEverDied)
                {
                    return new BattleTacticalReviewView(
                        false,
                        tag,
                        "战中出现人宠阵亡，中后期输出或续航断档",
                        "及时净化破军破甲，首领进二阶段加快重劈时提前稳住主力人宠血线。");
                }
                return new BattleTacticalReviewView(
                    false,
                    tag,
                    $"历时 {s.TotalRounds} 回合惜败首领试炼",
                    "按顶部预告做好点名化劲防御与护卫反制，或先打普通难度攒材料强化装备。");
            }

            // 2. 娱乐轮换：护卫救援（109，boss_trial_kind = 5）
            if (bossKind == 5u)
            {
                if (won)
                {
                    return new BattleTacticalReviewView(
                        true,
                        "轮换 · 护卫救援",
                        $"成功护送友方护卫存活至终局（共 {s.TotalRounds} 回合）",
                        "可换用其他免费 3×3 人宠组合试玩，或挑战正式人宠会武检验自养阵容。");
                }
                if (f != null && !f.GuardSurvived)
                {
                    return new BattleTacticalReviewView(
                        false,
                        "轮换 · 护卫救援",
                        "友方护卫未能保全导致失利",
                        "优先单点集火威胁护卫的首领，并换用带护盾/治疗的免费预设稳守护卫血线。");
                }
                return new BattleTacticalReviewView(
                    false,
                    "轮换 · 护卫救援",
                    $"护卫救援历时 {s.TotalRounds} 回合惜败",
                    "尝试铁御或灵疗免费预设，控住敌方爆发并配合队友保全护卫。");
            }

            // 3. 娱乐轮换：物法易势 5V5（110，boss_trial_kind = 6 或 MatchMode.Polarity5V5）
            if (bossKind == 6u || matchMode == (uint)MatchMode.Polarity5V5)
            {
                if (end.MaterialPvpLossIneligible)
                {
                    return new BattleTacticalReviewView(
                        false,
                        "轮换 · 物法易势 5V5",
                        "未达有效对抗门槛（需 ≥3 回合、≥2 手动且有有效贡献）",
                        "手动跟随顶部物势/法势横幅出手，打满门槛即可正常领取绑定材料。");
                }
                if (s.Surrendered)
                {
                    return new BattleTacticalReviewView(
                        false,
                        "轮换 · 物法易势 5V5",
                        "己方全队体面认输提前结束了本局团战",
                        "免费 3×3 预设可随时更换，下局紧跟每 2 回合的物/法易势切换集火目标。");
                }
                if (won)
                {
                    return new BattleTacticalReviewView(
                        true,
                        "轮换 · 物法易势 5V5",
                        $"顺势配合拿下 5V5 团战胜利（共 {s.TotalRounds} 回合）",
                        "状态正好！可以去正式人宠会武亮出你的真实养成阵容冲刺段位。");
                }
                if (f != null && !(f.TeamDealtPhysicalDirectDamage && f.TeamDealtMagicDirectDamage))
                {
                    return new BattleTacticalReviewView(
                        false,
                        "轮换 · 物法易势 5V5",
                        "队伍伤害类型较单一，逆势回合输出受压",
                        "人宠分别覆盖物理与法术输出，每 2 回合顺着当前强势势头集中爆发。");
                }
                return new BattleTacticalReviewView(
                    false,
                    "轮换 · 物法易势 5V5",
                    draw ? $"势均力敌战满 {s.TotalRounds} 回合平局" : $"团战历时 {s.TotalRounds} 回合惜败",
                    "在换势回合留好爆发技能，与队友集火同一残血目标打破僵局。");
            }

            // 4. 正式人宠会武 / 竞技 PVP
            bool isFormalPvp = matchMode == (uint)MatchMode.PvpTeam
                || matchMode == (uint)MatchMode._1V1
                || matchMode == (uint)MatchMode._3V3
                || matchMode == (uint)MatchMode._5V5
                || matchMode == (uint)MatchMode.PvpChallenge
                || matchMode == (uint)MatchMode.GuildWarPvp
                || matchMode == (uint)MatchMode.SeasonTournament;
            if (isFormalPvp)
            {
                string tag = matchMode == (uint)MatchMode.GuildWarPvp ? "帮会切磋"
                    : matchMode == (uint)MatchMode.SeasonTournament ? "群仙问道赛"
                    : "正式人宠会武";
                if (end.MaterialPvpLossIneligible)
                {
                    return new BattleTacticalReviewView(
                        false,
                        tag,
                        "未达有效对抗门槛，本次未发放负场绑定材料（不扣 660 额度）",
                        "至少打满 3 回合、2 次手动操作并产生有效伤害/治疗/控制即可正常计入。");
                }
                if (s.Surrendered)
                {
                    return new BattleTacticalReviewView(
                        false,
                        tag,
                        $"第 {s.TotalRounds} 回合全队体面认输，已正常结算场次与绑定材料",
                        "用已获材料精进词条或免费切换赛季印，下局针对对手核心人宠抢占先手。");
                }
                if (won)
                {
                    return new BattleTacticalReviewView(
                        true,
                        tag,
                        s.TotalRounds >= 12u
                            ? $"鏖战至「战意渐盛」阶段拿下胜利（{s.TotalRounds} 回合）"
                            : $"人宠协同配合拿下正式对局胜利（{s.TotalRounds} 回合）",
                        "趁手感火热继续上分，或前往打造页/聚宝斋进一步优化装备词条。");
                }
                if (draw)
                {
                    return new BattleTacticalReviewView(
                        false,
                        tag,
                        $"双方拉锯至 {s.TotalRounds} 回合达成平局",
                        "在第 12 回合「战意渐盛」开启后集中爆发并压制对手治疗，抢先击倒核心宠。");
                }
                return new BattleTacticalReviewView(
                    false,
                    tag,
                    s.TotalRounds >= 12u
                        ? $"拉锯至第 {s.TotalRounds} 回合「战意渐盛」阶段惜败"
                        : $"历时 {s.TotalRounds} 回合惜败对手",
                    (f != null && !f.SelfTriggeredAntiControlOrSeasonSkill)
                        ? "尝试切换解控或爆发类赛季印，开局优先控住或集火对手关键灵宠。"
                        : "调整人宠集火顺序，或用本局绑定材料在打造页优化装备词条后再战。");
            }

            return null;
        }

        public static string TelegraphStatusLabel(uint status) => status switch
        {
            1u => "即将发动",
            2u => "本回合发动",
            3u => "已发动",
            4u => "已受控打断",
            5u => "目标失效",
            _ => "预告中",
        };

        /// <summary>从终局推送与材料账本快照生成结算页材料拆解视图。</summary>
        public static CoreSettlementRewardView BuildSettlementRewardView(
            global::BattleEndS2C end,
            global::EquipmentMaterialRewardSnapshot ledger)
        {
            uint total = end?.MaterialCreditUnitsAdded ?? 0u;
            uint tradable = end?.MaterialTradableUnitsAdded ?? 0u;
            if (tradable > total) tradable = total;
            uint bound = total - tradable;
            uint baseUnits = total > 10u ? 10u : total;
            uint bonusUnits = total > 10u ? total - 10u : 0u;
            bool ineligible = end != null && end.MaterialPvpLossIneligible;

            bool weeklyActive = ledger != null && ledger.WeeklyPhaseActive;
            uint lifetimeEarned = ledger?.LifetimeEarned ?? 0u;
            uint lifetimeLimit = ledger != null && ledger.LifetimeLimit != 0 ? ledger.LifetimeLimit : 660u;
            uint tradableTotal = ledger?.TradableEarnedTotal ?? 0u;
            uint tradableLimit = ledger != null && ledger.TradableLifetimeLimit != 0 ? ledger.TradableLifetimeLimit : 60u;
            uint earnedWeek = ledger?.EarnedThisWeek ?? 0u;
            uint weeklyLimit = ledger != null && ledger.WeeklyLimit != 0 ? ledger.WeeklyLimit : 150u;
            ulong claimable = ledger?.AvailableUnits ?? 0ul;

            string headline;
            string detail;
            if (ineligible)
            {
                headline = "材料自选额度 +0（未达有效对抗门槛）";
                detail = "需打满至少 3 回合、至少 2 回合手动操作且有有效战斗贡献才可领取负/平场绑定材料；本次不扣除永久 660 额度。";
            }
            else if (total == 0u)
            {
                headline = "本局无材料额度新增";
                if (weeklyActive && earnedWeek >= weeklyLimit)
                    headline = $"本周精进材料额度已达上限（{earnedWeek}/{weeklyLimit}）";
                else if (!weeklyActive && lifetimeEarned >= lifetimeLimit)
                    headline = $"首套永久材料额度已满（{lifetimeEarned}/{lifetimeLimit}）";
                detail = "正式评分、试炼志与赛季印进度不受材料封顶影响。";
            }
            else
            {
                headline = bonusUnits > 0u
                    ? $"材料自选额度 +{total}（基础 {baseUnits} + 加量 {bonusUnits}）"
                    : $"材料自选额度 +{total}";
                detail = tradable > 0u
                    ? $"其中 {tradable} 份可交易、{bound} 份仅自用（全额可用于打造/百炼石）。"
                    : $"全部 {bound} 份仅自用（可用于打造材料或百炼石自选领取）。";
            }

            return new CoreSettlementRewardView(
                total, baseUnits, bonusUnits, tradable, bound, ineligible,
                weeklyActive, lifetimeEarned, lifetimeLimit,
                tradableTotal, tradableLimit, earnedWeek, weeklyLimit, claimable,
                headline, detail);
        }

        /// <summary>生成主城核心 3 数字与 4 个低打扰非数值商业化预留位（严禁每日打卡清单与卖数值）。</summary>
        public static MainCityHubView BuildMainCityHub(
            global::EquipmentMaterialRewardSnapshot materialLedger,
            RankedProfileSnapshot rankedProfile,
            global::SeasonStateSnapshot seasonState,
            global::TrialJournalSnapshot trialJournal,
            bool paymentEnabled = false,
            bool hasAfkPass = false,
            uint unlockedLoadoutSlots = 3u)
        {
            string matLabel;
            string matValue;
            ulong claimable = materialLedger?.AvailableUnits ?? 0ul;
            if (materialLedger != null && materialLedger.WeeklyPhaseActive)
            {
                uint weekLimit = materialLedger.WeeklyLimit != 0 ? materialLedger.WeeklyLimit : 150u;
                matLabel = "周精进材料";
                matValue = $"{materialLedger.EarnedThisWeek}/{weekLimit}（待领 {claimable}）";
            }
            else
            {
                uint lifeLimit = materialLedger != null && materialLedger.LifetimeLimit != 0 ? materialLedger.LifetimeLimit : 660u;
                uint lifeEarned = materialLedger?.LifetimeEarned ?? 0u;
                matLabel = "永久首套材料 · 账号可交易";
                matValue = $"{lifeEarned}/{lifeLimit}（待领 {claimable}）";
            }

            string pvpValue = rankedProfile?.CurrentProgress != null
                ? $"{RankedFormatting.TierDisplayName(rankedProfile.CurrentProgress.CurrentTier, rankedProfile.CurrentProgress.Qualified)} · {RankedFormatting.FormatRating(rankedProfile.CurrentProgress.RatingCenti)} 分"
                : "未亮段 · 1500 分";

            var equippedSeal = SeasonModels.FindEquippedSeal(seasonState);
            string sealPart = seasonState != null && seasonState.Available
                ? $"{(equippedSeal != null ? equippedSeal.Name : "赛季印")} {seasonState.ProgressPoints}/{seasonState.MaxPoints}"
                : "赛季印待同步";
            int claimableMilestones = TrialJournalModels.CountClaimableMilestones(trialJournal);
            string journalPart = claimableMilestones > 0 ? $" · 试炼志可领 {claimableMilestones}" : "";

            string payTag = paymentEnabled ? "可选 · 零数值" : "首测预览 · 未开直充";
            var slots = new[]
            {
                new SmallSpendSlotView(
                    "wardrobe",
                    "易容衣橱与灵宠外观",
                    "角色外观（易容卡 3402）、灵宠皮肤与战斗施法特效（零战斗数值）",
                    payTag,
                    paymentEnabled,
                    zeroStatGuarantee: true),
                new SmallSpendSlotView(
                    "season_journal",
                    "赛季手记 · 外观里程碑",
                    "记录本季人宠会武与试炼志外观收藏，不含任何装备属性或额外材料",
                    payTag,
                    paymentEnabled,
                    zeroStatGuarantee: true),
                new SmallSpendSlotView(
                    "convenience_loadouts",
                    "轻享省心卡与配装槽位",
                    hasAfkPass
                        ? $"省心托管生效中 · 当前配装方案槽位 {unlockedLoadoutSlots} 格（纯便利零数值）"
                        : $"离线托管便利 + 配装方案槽位 3→6 格预留（当前 {unlockedLoadoutSlots} 格）",
                    hasAfkPass ? "已生效" : payTag,
                    paymentEnabled,
                    zeroStatGuarantee: true),
                new SmallSpendSlotView(
                    "material_market",
                    "聚宝斋 · 材料次要来源",
                    "玩家间自由交易可交易打造材料与百炼石，不扣减永久 660 免费奖励",
                    "玩家流通",
                    paymentEnabled,
                    zeroStatGuarantee: true),
            };

            return new MainCityHubView(
                matLabel, matValue, claimable,
                "正式人宠会武", pvpValue,
                "赛季印与试炼志", sealPart + journalPart,
                slots);
        }
    }
}
