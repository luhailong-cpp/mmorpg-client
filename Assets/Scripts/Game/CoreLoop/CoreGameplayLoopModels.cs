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

    /// <summary>战斗内单条公开预告视图（对应 BattleTelegraphEntry）。</summary>
    public sealed class BattleTelegraphView
    {
        public ulong SourceActorId { get; }
        public ulong TargetActorId { get; }
        public uint ExecuteRound { get; }
        public uint IntentKind { get; }
        public uint Status { get; }
        public string IntentTitle { get; }
        public string StatusTag { get; }
        public string DetailText { get; }
        public bool ExecutesThisRound => Status == 2u;

        public BattleTelegraphView(
            ulong sourceActorId,
            ulong targetActorId,
            uint executeRound,
            uint intentKind,
            uint status,
            string intentTitle,
            string statusTag,
            string detailText)
        {
            SourceActorId = sourceActorId;
            TargetActorId = targetActorId;
            ExecuteRound = executeRound;
            IntentKind = intentKind;
            Status = status;
            IntentTitle = intentTitle ?? "";
            StatusTag = statusTag ?? "";
            DetailText = detailText ?? "";
        }
    }

    /// <summary>战斗内核心玩法 HUD 派生视图（预告 + 物法易势换势条 + 护卫救援保全目标）。</summary>
    public sealed class BattleCoreHudView
    {
        public uint RoundIndex { get; }
        public IReadOnlyList<BattleTelegraphView> Telegraphs { get; }
        public uint ActivePolarity { get; }
        public uint NextPolaritySwitchRound { get; }
        public string PolarityBannerText { get; }
        public ulong ProtectedGuardActorId { get; }
        public string ProtectedGuardBannerText { get; }

        public bool HasTelegraphs => Telegraphs != null && Telegraphs.Count > 0;
        public bool HasPolarity => ActivePolarity != 0;
        public bool HasProtectedGuard => ProtectedGuardActorId != 0;

        public BattleCoreHudView(
            uint roundIndex,
            IReadOnlyList<BattleTelegraphView> telegraphs,
            uint activePolarity,
            uint nextPolaritySwitchRound,
            string polarityBannerText,
            ulong protectedGuardActorId,
            string protectedGuardBannerText)
        {
            RoundIndex = roundIndex;
            Telegraphs = telegraphs;
            ActivePolarity = activePolarity;
            NextPolaritySwitchRound = nextPolaritySwitchRound;
            PolarityBannerText = polarityBannerText ?? "";
            ProtectedGuardActorId = protectedGuardActorId;
            ProtectedGuardBannerText = protectedGuardBannerText ?? "";
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

        public const string HardLockDefaultHint = "通关任意一只普通首领试炼后开放困难难度";

        public static bool IsHardBossTrialDungeon(uint dungeonConfigId) =>
            dungeonConfigId == Boss1RejuvenateHardDungeonId
            || dungeonConfigId == Boss1StanceHardDungeonId
            || dungeonConfigId == Boss2ShieldHardDungeonId
            || dungeonConfigId == Boss3PoisonHardDungeonId;

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
                    "首领提前预告慢动作重劈；回春护卫读条为首领高额回血。",
                    "被点名者防御或由宝宝保护，优先集火或控制回春护卫断疗。",
                    10u, 15u, hardUnlocked, lockHint),
                new BossTrialCardView(
                    2u, Boss1StanceNormalDungeonId, Boss1StanceHardDungeonId,
                    "重甲首领 · 护势阵", "护势护卫",
                    "首领预告重劈；护势护卫轮流张开物防护势与法防护势。",
                    "看清护势类型切换物法集火，避免把主力爆发打在免疫护势上。",
                    10u, 15u, hardUnlocked, lockHint),
                new BossTrialCardView(
                    3u, Boss2ShieldNormalDungeonId, Boss2ShieldHardDungeonId,
                    "玄盾首领", "灵障护卫",
                    "灵障护卫每两回合蓄盾，未击破护盾将在下回合引发高额反震爆发。",
                    "利用宝宝碎甲与集火破盾，在护盾引爆前打断节奏。",
                    10u, 15u, hardUnlocked, lockHint),
                new BossTrialCardView(
                    4u, Boss3PoisonNormalDungeonId, Boss3PoisonHardDungeonId,
                    "幽毒首领", "玄毒护卫",
                    "玄毒护卫释放叠毒并封灵限制出手，长线拖延压力持续上升。",
                    "及时净化叠毒与控场封灵护卫，人宠错峰恢复稳住血线。",
                    10u, 15u, hardUnlocked, lockHint),
            };
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

        /// <summary>从 BattleStateS2C 派生战斗内公开预告、物法易势横幅与护卫救援保全提示。</summary>
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
                telegraphs.Add(new BattleTelegraphView(
                    t.SourceActorId, t.TargetActorId, t.ExecuteRound, t.IntentKind, t.Status,
                    title, statusTag, detail));
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
                ? "救援目标：击败首领并保全敌方回春护卫存活"
                : "";

            return new BattleCoreHudView(
                state.RoundIndex,
                telegraphs,
                state.ActivePolarity,
                state.NextPolaritySwitchRound,
                polarityBanner,
                state.ProtectedGuardActorId,
                guardBanner);
        }

        public static string TelegraphIntentTitle(uint intentKind) => intentKind switch
        {
            1u => "慢动作重劈",
            2u => "回春治疗",
            3u => "物防护势",
            4u => "法防护势",
            5u => "灵障蓄盾",
            6u => "玄毒封灵",
            _ => "首领蓄力",
        };

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
            uint weeklyLimit = ledger != null && ledger.WeeklyLimit != 0 ? ledger.WeeklyLimit : 100u;
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
                headline = weeklyActive
                    ? $"本周精进绑定额度已达上限（{earnedWeek}/{weeklyLimit}）"
                    : (lifetimeEarned >= lifetimeLimit
                        ? $"首套永久材料额度已满（{lifetimeEarned}/{lifetimeLimit}）"
                        : "本局无材料额度新增");
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
                uint weekLimit = materialLedger.WeeklyLimit != 0 ? materialLedger.WeeklyLimit : 100u;
                matLabel = "周精进绑定材料";
                matValue = $"{materialLedger.EarnedThisWeek}/{weekLimit}（待领 {claimable}）";
            }
            else
            {
                uint lifeLimit = materialLedger != null && materialLedger.LifetimeLimit != 0 ? materialLedger.LifetimeLimit : 660u;
                uint lifeEarned = materialLedger?.LifetimeEarned ?? 0u;
                matLabel = "永久首套材料";
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
