using System;
using System.Collections.Generic;
using System.Globalization;

namespace MmorpgClient.Game.FestivalBeast
{
    /// <summary>瑞兽迎春主面板底部主按钮语义（严格按需求第三节优先级收敛）。</summary>
    public enum FestivalBeastPrimaryButtonKind
    {
        Hidden = 0,
        ConfirmingClaim = 1,
        ClaimReward = 2,
        ContinueChallenge = 3,
        StartChallenge = 4,
        Blocked = 5,
    }

    /// <summary>瑞兽迎春主面板只读视图模型（纯 C#，零 UnityEngine 依赖，UI 仅绑定展示）。</summary>
    public sealed class FestivalBeastMainPanelViewModel
    {
        public string ActivityTitle { get; }
        public string PhaseBadgeText { get; }
        public string TimeWindowText { get; }
        public string CountdownText { get; }
        public string GoalSummaryText { get; }
        public uint CurrentWins { get; }
        public uint RequiredWins { get; }
        public string ProgressText { get; }
        public float ProgressRatio { get; }
        public bool IsLoadingInitial { get; }
        public bool ShowRefreshButton { get; }
        public string StatusBannerText { get; }
        public bool StatusBannerIsError { get; }

        public string ActivePetLabel { get; }
        public string ActivePetHintText { get; }
        public string CombatTipText { get; }

        public string EarnableMaterialText { get; }
        public string AvailableMaterialText { get; }
        public bool CanJumpMaterialClaim { get; }

        public FestivalBeastPrimaryButtonKind PrimaryButtonKind { get; }
        public string PrimaryButtonText { get; }
        public bool PrimaryButtonEnabled { get; }
        public string PrimarySubtext { get; }

        public bool ShowClaimedPetReceipt { get; }
        public string ClaimedPetReceiptText { get; }
        public bool ShowAppearanceEntry { get; }
        public bool IsDemoPreview { get; }

        public FestivalBeastMainPanelViewModel(
            string activityTitle,
            string phaseBadgeText,
            string timeWindowText,
            string countdownText,
            string goalSummaryText,
            uint currentWins,
            uint requiredWins,
            string progressText,
            float progressRatio,
            bool isLoadingInitial,
            bool showRefreshButton,
            string statusBannerText,
            bool statusBannerIsError,
            string activePetLabel,
            string activePetHintText,
            string combatTipText,
            string earnableMaterialText,
            string availableMaterialText,
            bool canJumpMaterialClaim,
            FestivalBeastPrimaryButtonKind primaryButtonKind,
            string primaryButtonText,
            bool primaryButtonEnabled,
            string primarySubtext,
            bool showClaimedPetReceipt,
            string claimedPetReceiptText,
            bool showAppearanceEntry,
            bool isDemoPreview)
        {
            ActivityTitle = activityTitle;
            PhaseBadgeText = phaseBadgeText;
            TimeWindowText = timeWindowText;
            CountdownText = countdownText;
            GoalSummaryText = goalSummaryText;
            CurrentWins = currentWins;
            RequiredWins = requiredWins;
            ProgressText = progressText;
            ProgressRatio = progressRatio;
            IsLoadingInitial = isLoadingInitial;
            ShowRefreshButton = showRefreshButton;
            StatusBannerText = statusBannerText;
            StatusBannerIsError = statusBannerIsError;
            ActivePetLabel = activePetLabel;
            ActivePetHintText = activePetHintText;
            CombatTipText = combatTipText;
            EarnableMaterialText = earnableMaterialText;
            AvailableMaterialText = availableMaterialText;
            CanJumpMaterialClaim = canJumpMaterialClaim;
            PrimaryButtonKind = primaryButtonKind;
            PrimaryButtonText = primaryButtonText;
            PrimaryButtonEnabled = primaryButtonEnabled;
            PrimarySubtext = primarySubtext;
            ShowClaimedPetReceipt = showClaimedPetReceipt;
            ClaimedPetReceiptText = claimedPetReceiptText;
            ShowAppearanceEntry = showAppearanceEntry;
            IsDemoPreview = isDemoPreview;
        }
    }

    /// <summary>瑞兽迎春战斗重击预告只读视图模型。</summary>
    public sealed class FestivalBeastBattleHudViewModel
    {
        public bool Visible { get; }
        public string RoundDisplayText { get; }
        public string HeadlineText { get; }
        public string HintText { get; }
        public bool IsWarningActive { get; }
        public bool IsExecutingRound { get; }
        public bool IsCancelled { get; }
        public ulong MarkedTargetActorId { get; }
        public string TargetMarkerText { get; }

        public FestivalBeastBattleHudViewModel(
            bool visible,
            string roundDisplayText,
            string headlineText,
            string hintText,
            bool isWarningActive,
            bool isExecutingRound,
            bool isCancelled,
            ulong markedTargetActorId,
            string targetMarkerText)
        {
            Visible = visible;
            RoundDisplayText = roundDisplayText;
            HeadlineText = headlineText;
            HintText = hintText;
            IsWarningActive = isWarningActive;
            IsExecutingRound = isExecutingRound;
            IsCancelled = isCancelled;
            MarkedTargetActorId = markedTargetActorId;
            TargetMarkerText = targetMarkerText;
        }
    }

    /// <summary>瑞兽迎春结算区块只读视图模型（嵌入现有结算界面）。</summary>
    public sealed class FestivalBeastSettlementViewModel
    {
        public string HeadlineText { get; }
        public string ProgressLineText { get; }
        public string MaterialGainLineText { get; }
        public string MaterialBalanceLineText { get; }
        public string SubNoteText { get; }
        public bool ShowClaimBeastButton { get; }
        public bool ClaimBeastButtonEnabled { get; }
        public string ClaimBeastButtonText { get; }
        public bool ShowRetryButton { get; }
        public bool ShowMaterialJumpButton { get; }
        public bool IsSettlingOrUnknown { get; }

        public FestivalBeastSettlementViewModel(
            string headlineText,
            string progressLineText,
            string materialGainLineText,
            string materialBalanceLineText,
            string subNoteText,
            bool showClaimBeastButton,
            bool claimBeastButtonEnabled,
            string claimBeastButtonText,
            bool showRetryButton,
            bool showMaterialJumpButton,
            bool isSettlingOrUnknown)
        {
            HeadlineText = headlineText;
            ProgressLineText = progressLineText;
            MaterialGainLineText = materialGainLineText;
            MaterialBalanceLineText = materialBalanceLineText;
            SubNoteText = subNoteText;
            ShowClaimBeastButton = showClaimBeastButton;
            ClaimBeastButtonEnabled = claimBeastButtonEnabled;
            ClaimBeastButtonText = claimBeastButtonText;
            ShowRetryButton = showRetryButton;
            ShowMaterialJumpButton = showMaterialJumpButton;
            IsSettlingOrUnknown = isSettlingOrUnknown;
        }
    }

    /// <summary>
    /// 瑞兽迎春纯 C# UI 状态推导器（零 UnityEngine 依赖，严格只读服务端权威快照，不自行计算资格、胜场或发奖）。
    /// </summary>
    public static class FestivalBeastUiState
    {
        public const string DefaultActivityName = "瑞兽迎春";
        public const string DefaultBossName = "春禧兽";
        public const string DefaultGoalCopy = "累计通关3次，免费获得春禧兽";
        public const string DefaultCombatPrepTip = "重击前一回合会点名，人物和宝宝都可能被选中。";
        public const string BattleDefendHint = "目标可本轮防御，另一方可输出；控制可能被抵抗。";

        public static readonly IReadOnlyList<string> RulesLines = new[]
        {
            "1. 单人携 1 只存活出战宝宝免费挑战，无门票、体力与每日次数限制，失败不清空已有进度。",
            "2. 首领每 3 回合循环行动：第 2、5、8、11 回合提前点名目标，第 3、6、9、12 回合在人宠正常行动后发动 180% 单体重击；防御须本轮生效且不跨轮。",
            "3. 12 回合内击败首领获胜。仅局内补满血蓝，禁用背包道具与捕捉，局内变化不写回永久养成；关闭界面或断线不等于逃跑。",
            "4. 本期累计 3 胜获永久待领资格（单角色终身 1 只，享 1 格专属赠送位，普通宠物栏满仍可领）。胜局接全游戏共享材料账，每有效胜局至多新增 10 份可领取额度，待领余额可跨日。",
        };

        /// <summary>推导主面板展示模型，覆盖加载、失败、五阶段、在途恢复、满栏领宠、跨日材料与主按钮优先级。</summary>
        public static FestivalBeastMainPanelViewModel BuildMainPanel(
            FestivalBeastDetailSnapshot detail,
            bool detailLoading,
            bool launchBusy,
            bool claimBusy,
            bool hasPendingLaunch,
            bool hasPendingClaim,
            string statusMessage,
            bool statusIsError,
            ulong remainingMs,
            string activePetName,
            bool hasActivePet,
            bool activePetAlive,
            bool isDemoPreview = false)
        {
            string petLabel = BuildActivePetLabel(activePetName, hasActivePet, activePetAlive);
            string petHint = !hasActivePet
                ? "请前往宝宝页面出战 1 只存活宝宝"
                : !activePetAlive
                    ? "当前出战宝宝已倒地，请先恢复或更换宝宝"
                    : "局内起始血蓝补满，不改局外血蓝与养成";

            if (detail == null)
            {
                bool initial = detailLoading;
                string banner = initial
                    ? "正在加载瑞兽迎春活动数据…"
                    : !string.IsNullOrEmpty(statusMessage)
                        ? statusMessage
                        : "活动数据加载失败或尚未同步，请点击刷新重试。";
                return new FestivalBeastMainPanelViewModel(
                    DefaultActivityName,
                    initial ? "加载中" : "未同步",
                    "活动时间：等待服务端同步（北京时间 UTC+8）",
                    string.Empty,
                    DefaultGoalCopy,
                    0u, 3u, "—/3", 0f,
                    initial,
                    !initial,
                    banner,
                    !initial,
                    petLabel,
                    petHint,
                    DefaultCombatPrepTip,
                    "今日可赚材料额度：等待同步",
                    "已赚取未领取余额：等待同步",
                    false,
                    FestivalBeastPrimaryButtonKind.Blocked,
                    initial ? "加载中…" : "请先刷新",
                    false,
                    banner,
                    false,
                    string.Empty,
                    false,
                    isDemoPreview);
            }

            string title = string.IsNullOrWhiteSpace(detail.ActivityName) ? DefaultActivityName : detail.ActivityName;
            string bossName = string.IsNullOrWhiteSpace(detail.BossName) ? DefaultBossName : detail.BossName;
            uint required = detail.RequiredWins == 0u ? 3u : detail.RequiredWins;
            uint current = detail.CurrentWins > required ? required : detail.CurrentWins;
            string goalCopy = $"累计通关{required}次，免费获得{bossName}";
            string progressText = $"{current}/{required}";
            float progressRatio = required == 0u ? 0f : Math.Min(1f, (float)current / required);

            string phaseBadge = FormatPhaseBadge(detail.Phase, detail.HasPermanentEligibility && !detail.RewardClaimed);
            string timeWindow = detail.EffectiveStartAtMs > 0UL && detail.EffectiveEndAtMs > detail.EffectiveStartAtMs
                ? $"北京时间 UTC+8：{FestivalBeastModels.FormatBeijingDateTime(detail.EffectiveStartAtMs)} 至 {FestivalBeastModels.FormatBeijingDateTime(detail.EffectiveEndAtMs)}"
                : "活动时间：尚未排期（以运营公告为准，北京时间 UTC+8）";
            string countdown = FormatCountdown(detail.Phase, remainingMs);

            string earnableText = FormatEarnableMaterialText(detail.MaterialSummary);
            string availableText = FormatAvailableMaterialText(detail.MaterialSummary);
            bool canJumpMaterial = detail.MaterialSummary != null && detail.MaterialSummary.AvailableUnits > 0UL;

            ResolvePrimaryAction(
                detail, launchBusy, claimBusy, hasPendingLaunch, hasPendingClaim, bossName,
                out var btnKind, out string btnText, out bool btnEnabled, out string btnSubtext);

            bool showClaimedReceipt = detail.RewardClaimed && detail.RewardClaimDurable && detail.ClaimedPetId != 0UL;
            string claimedReceiptText = showClaimedReceipt
                ? $"已领取 {bossName}（编号 {detail.ClaimedPetIdText}，已存入专属赠送位，不会自动顶替当前出战宝宝）"
                : string.Empty;

            // 低优先级「人物外观」仅在正常闲置态显示，绝不在失败提示、领奖确认或待领奖时推销。
            bool showAppearance = btnKind != FestivalBeastPrimaryButtonKind.ClaimReward
                && btnKind != FestivalBeastPrimaryButtonKind.ConfirmingClaim
                && !statusIsError;

            string bannerText = statusMessage ?? string.Empty;
            bool bannerErr = statusIsError;
            if (string.IsNullOrEmpty(bannerText) && detail.PendingLaunch != null && detail.PendingLaunch.HasInFlight)
            {
                bannerText = detail.PendingLaunch.LaunchState == global::FestivalBeastLaunchState.FestivalBeastLaunchAdmitted
                    ? $"检测到进行中的战局（编号 {detail.PendingLaunch.BattleIdText}），请重连恢复原局。"
                    : "检测到未确认的开局请求，正在沿用原请求恢复（不会重复开局）。";
            }

            return new FestivalBeastMainPanelViewModel(
                title,
                phaseBadge,
                timeWindow,
                countdown,
                goalCopy,
                current,
                required,
                progressText,
                progressRatio,
                false,
                !detailLoading,
                bannerText,
                bannerErr,
                petLabel,
                petHint,
                DefaultCombatPrepTip,
                earnableText,
                availableText,
                canJumpMaterial,
                btnKind,
                btnText,
                btnEnabled,
                btnSubtext,
                showClaimedReceipt,
                claimedReceiptText,
                showAppearance,
                isDemoPreview);
        }

        /// <summary>推导战斗内点名预告条与目标单位标记（严格只认服务端状态，不凭控制图标自行取消）。</summary>
        public static FestivalBeastBattleHudViewModel BuildBattleHud(
            uint currentRound,
            uint roundLimit,
            FestivalBeastTelegraphSnapshot activeTelegraph,
            FestivalBeastTelegraphSnapshot lastResolvedTelegraph,
            Func<ulong, string> resolveActorName,
            string bossName = DefaultBossName)
        {
            uint effectiveLimit = roundLimit == 0u ? 12u : roundLimit;
            uint effectiveRound = currentRound == 0u ? 1u : currentRound;
            string roundText = $"第{effectiveRound}/{effectiveLimit}回合";
            string effectiveBoss = string.IsNullOrWhiteSpace(bossName) ? DefaultBossName : bossName;

            if (activeTelegraph != null)
            {
                string targetName = ResolveTargetName(activeTelegraph.TargetActorId, resolveActorName);
                if (activeTelegraph.IsUpcoming)
                {
                    return new FestivalBeastBattleHudViewModel(
                        true,
                        roundText,
                        $"下回合：{effectiveBoss}将重击【{targetName}】",
                        BattleDefendHint,
                        true,
                        false,
                        false,
                        activeTelegraph.TargetActorId,
                        "下轮重击");
                }
                if (activeTelegraph.IsExecuting)
                {
                    return new FestivalBeastBattleHudViewModel(
                        true,
                        roundText,
                        $"本回合重击【{targetName}】，在人宠正常行动后发动。",
                        BattleDefendHint,
                        true,
                        true,
                        false,
                        activeTelegraph.TargetActorId,
                        "本轮重击");
                }
            }

            if (lastResolvedTelegraph != null && lastResolvedTelegraph.ExecuteRound == effectiveRound)
            {
                string resolvedText = lastResolvedTelegraph.IsCancelledByControl
                    ? $"{effectiveBoss}重击已因控制取消（不补普攻、不延后）"
                    : lastResolvedTelegraph.IsCancelledTargetInvalid
                        ? $"被点名目标已离场，{effectiveBoss}本次重击取消"
                        : $"{effectiveBoss}本回合重击已兑现";
                return new FestivalBeastBattleHudViewModel(
                    true,
                    roundText,
                    resolvedText,
                    string.Empty,
                    false,
                    false,
                    lastResolvedTelegraph.IsCancelled,
                    0UL,
                    string.Empty);
            }

            return new FestivalBeastBattleHudViewModel(
                false, roundText, string.Empty, string.Empty, false, false, false, 0UL, string.Empty);
        }

        /// <summary>推导结算界面的瑞兽迎春进度、新增材料额度与领奖按钮态。</summary>
        public static FestivalBeastSettlementViewModel BuildSettlement(
            FestivalBeastSettlementSnapshot settlement,
            FestivalBeastDetailSnapshot detail,
            bool claimBusy,
            bool hasPendingClaim,
            string bossName = DefaultBossName)
        {
            string effectiveBoss = string.IsNullOrWhiteSpace(bossName) ? DefaultBossName : bossName;
            if (settlement == null)
            {
                return new FestivalBeastSettlementViewModel(
                    "结算处理中…",
                    "正在等待服务端确认本局结果，请勿重复开局。",
                    "材料额度以服务端确认结果为准",
                    string.Empty,
                    "关闭界面不会放弃进度或资格。",
                    false, false, string.Empty, false, false, true);
            }

            uint reqWins = settlement.RequiredWins == 0u ? 3u : settlement.RequiredWins;
            string materialGain = settlement.IsVictory
                ? $"本局新增可领取额度 +{settlement.MaterialUnitsAdded}"
                : "本局新增可领取额度 +0";
            string materialBalance = $"跨日待领材料余额：{settlement.MaterialAvailableUnitsAfter} 份（今日/当期还可赚 {settlement.MaterialEarnableRemainingAfter} 份）";
            bool showMaterialJump = settlement.MaterialAvailableUnitsAfter > 0UL;

            if (settlement.IsVoid)
            {
                return new FestivalBeastSettlementViewModel(
                    "本局已作废",
                    $"活动进度未变动：{settlement.WinsAfter}/{reqWins}",
                    materialGain,
                    materialBalance,
                    "作废不清空已有进度，可免费重试。",
                    false, false, string.Empty, detail?.CanStartChallenge == true, showMaterialJump, false);
            }

            if (!settlement.IsVictory)
            {
                return new FestivalBeastSettlementViewModel(
                    "本次未击败首领",
                    $"活动进度保持不变：{settlement.WinsAfter}/{reqWins}",
                    materialGain,
                    materialBalance,
                    "失败与逃跑不清空进度，可免费再次挑战。",
                    false, false, string.Empty, detail?.CanStartChallenge == true, showMaterialJump, false);
            }

            bool claimedDurable = detail != null && detail.RewardClaimed && detail.RewardClaimDurable;
            bool claimConfirming = claimBusy || hasPendingClaim || (detail != null && detail.IsClaimConfirming);
            bool hasUnclaimedEligibility = !claimedDurable && (settlement.NewlyEligible || (detail != null && detail.HasPermanentEligibility));

            if (claimConfirming)
            {
                return new FestivalBeastSettlementViewModel(
                    "挑战胜利 · 正在确认领取",
                    $"活动进度：{settlement.WinsBefore}/{reqWins} → {settlement.WinsAfter}/{reqWins}",
                    materialGain,
                    materialBalance,
                    "正在等待服务端持久确认发放，请稍候。",
                    true, false, "正在确认领取", false, showMaterialJump, false);
            }

            if (settlement.NewlyEligible && !claimedDurable)
            {
                return new FestivalBeastSettlementViewModel(
                    $"累计 {reqWins} 胜达成！{effectiveBoss}已可免费领取",
                    $"活动进度：{settlement.WinsBefore}/{reqWins} → {settlement.WinsAfter}/{reqWins}",
                    materialGain,
                    materialBalance,
                    "资格永久保留，享 1 格专属赠送位（领取后不自动顶替出战宝宝）。",
                    true, true, "领取神兽", false, showMaterialJump, false);
            }

            if (hasUnclaimedEligibility)
            {
                return new FestivalBeastSettlementViewModel(
                    $"挑战胜利 · {effectiveBoss}待领取",
                    $"本期胜场：{settlement.WinsAfter}/{reqWins}（永久资格已保留）",
                    materialGain,
                    materialBalance,
                    "您已拥有未领取的神兽资格，本次不重复触发首次解锁。",
                    true, true, "领取神兽", false, showMaterialJump, false);
            }

            if (claimedDurable)
            {
                return new FestivalBeastSettlementViewModel(
                    $"挑战胜利 · 已拥有{effectiveBoss}",
                    $"本期胜场：{settlement.WinsAfter}/{reqWins}（单角色终身限领 1 只）",
                    materialGain,
                    materialBalance,
                    "已领取过该神兽，本次仅结算实际可领取材料额度。",
                    false, false, string.Empty, detail?.CanStartChallenge == true, showMaterialJump, false);
            }

            return new FestivalBeastSettlementViewModel(
                "挑战胜利",
                $"活动进度：{settlement.WinsBefore}/{reqWins} → {settlement.WinsAfter}/{reqWins}",
                materialGain,
                materialBalance,
                $"再胜 {Math.Max(0, (int)reqWins - (int)settlement.WinsAfter)} 场即可免费领取{effectiveBoss}。",
                false, false, string.Empty, detail?.CanStartChallenge == true, showMaterialJump, false);
        }

        private static void ResolvePrimaryAction(
            FestivalBeastDetailSnapshot detail,
            bool launchBusy,
            bool claimBusy,
            bool hasPendingLaunch,
            bool hasPendingClaim,
            string bossName,
            out FestivalBeastPrimaryButtonKind kind,
            out string text,
            out bool enabled,
            out string subtext)
        {
            if (claimBusy || hasPendingClaim || detail.IsClaimConfirming
                || detail.ClaimBlockCode == global::FestivalBeastActionBlockCode.FestivalBeastBlockClaimConfirming)
            {
                kind = FestivalBeastPrimaryButtonKind.ConfirmingClaim;
                text = "正在确认领取";
                enabled = false;
                subtext = "奖励已提交并等待落盘确认，关闭界面不会取消或丢失资格。";
                return;
            }

            if (detail.CanClaimReward)
            {
                kind = FestivalBeastPrimaryButtonKind.ClaimReward;
                text = "领取神兽";
                enabled = true;
                subtext = detail.CurrentWins < detail.RequiredWins
                    ? $"已保留历史永久资格，可免费领取 {bossName}（普通宠物栏满也可领）。"
                    : $"累计通关已达标，点击免费领取 {bossName}（专属赠送位，不占普通栏）。";
                return;
            }

            if (launchBusy || hasPendingLaunch
                || (detail.PendingLaunch != null && detail.PendingLaunch.HasInFlight)
                || detail.StartBlockCode == global::FestivalBeastActionBlockCode.FestivalBeastBlockLaunchInFlight)
            {
                kind = FestivalBeastPrimaryButtonKind.Blocked;
                text = "开局处理中";
                enabled = false;
                subtext = "正在确认原开局请求，结果未知时不可重复开局。";
                return;
            }

            if (detail.StartBlockCode == global::FestivalBeastActionBlockCode.FestivalBeastBlockInBattle)
            {
                kind = FestivalBeastPrimaryButtonKind.Blocked;
                text = "战斗进行中";
                enabled = false;
                subtext = string.IsNullOrEmpty(detail.StartBlockReason) ? "当前已有进行中的战局。" : detail.StartBlockReason;
                return;
            }

            if (detail.RewardClaimed && detail.RewardClaimDurable)
            {
                if (detail.Phase == global::FestivalBeastPhase.Open)
                {
                    if (detail.CanStartChallenge)
                    {
                        kind = FestivalBeastPrimaryButtonKind.ContinueChallenge;
                        text = "继续挑战";
                        enabled = true;
                        subtext = $"已拥有 {bossName}，继续挑战可获取共享材料额度，不会重复发放第二只。";
                    }
                    else
                    {
                        kind = FestivalBeastPrimaryButtonKind.Blocked;
                        text = "暂不可挑战";
                        enabled = false;
                        subtext = string.IsNullOrEmpty(detail.StartBlockReason) ? "当前暂不满足出战条件。" : detail.StartBlockReason;
                    }
                    return;
                }
                // 需求第六节硬约束：已结束且已领取，不显示「继续挑战」按钮。
                kind = FestivalBeastPrimaryButtonKind.Hidden;
                text = string.Empty;
                enabled = false;
                subtext = $"本期活动已结束，您已永久拥有 {bossName}。";
                return;
            }

            if (detail.Phase == global::FestivalBeastPhase.Open && detail.CanStartChallenge)
            {
                kind = FestivalBeastPrimaryButtonKind.StartChallenge;
                text = "开始挑战";
                enabled = true;
                subtext = "单人携宠免费挑战，失败可无限重试，旧进度不清零。";
                return;
            }

            kind = FestivalBeastPrimaryButtonKind.Blocked;
            text = detail.Phase == global::FestivalBeastPhase.Open ? "暂不可挑战" : FormatPhaseBadge(detail.Phase, false);
            enabled = false;
            subtext = !string.IsNullOrEmpty(detail.StartBlockReason)
                ? detail.StartBlockReason
                : detail.Phase switch
                {
                    global::FestivalBeastPhase.Unscheduled => "活动尚未排期开放，请留意官方公告。",
                    global::FestivalBeastPhase.Upcoming => "活动尚未到达开启时间，请稍候。",
                    global::FestivalBeastPhase.ClaimShowcase => "本期挑战已结束，当前为 7 天集中领奖展示期。",
                    global::FestivalBeastPhase.History => "本期活动已结束；未满 3 胜进度不跨期累计。",
                    _ => "当前暂不可参与。",
                };
        }

        private static string FormatPhaseBadge(global::FestivalBeastPhase phase, bool hasUnclaimed) => phase switch
        {
            global::FestivalBeastPhase.Unscheduled => "未排期",
            global::FestivalBeastPhase.Upcoming => "即将开启",
            global::FestivalBeastPhase.Open => "进行中",
            global::FestivalBeastPhase.ClaimShowcase => hasUnclaimed ? "集中领奖期 · 待领" : "集中领奖期",
            global::FestivalBeastPhase.History => hasUnclaimed ? "历史待领奖" : "已结束",
            _ => "未同步",
        };

        private static string FormatCountdown(global::FestivalBeastPhase phase, ulong remainingMs)
        {
            if (remainingMs == 0UL) return string.Empty;
            ulong totalSec = remainingMs / 1000UL;
            ulong days = totalSec / 86400UL;
            ulong hours = (totalSec % 86400UL) / 3600UL;
            ulong mins = (totalSec % 3600UL) / 60UL;
            string prefix = phase switch
            {
                global::FestivalBeastPhase.Upcoming => "距开启还剩：",
                global::FestivalBeastPhase.Open => "距挑战截止：",
                global::FestivalBeastPhase.ClaimShowcase => "集中领奖展示剩余：",
                _ => string.Empty,
            };
            if (string.IsNullOrEmpty(prefix)) return string.Empty;
            return days > 0UL
                ? $"{prefix}{days}天 {hours:D2}时 {mins:D2}分"
                : $"{prefix}{hours:D2}时 {mins:D2}分";
        }

        private static string FormatEarnableMaterialText(FestivalBeastMaterialSummarySnapshot summary)
        {
            if (summary == null) return "今日还能赚取材料额度：等待同步";
            string suffix = summary.EarnableRemainingUnits == 0u
                ? "（已达上限，继续通关仍推进神兽进度）"
                : $"（每有效胜局至多 +{summary.ExpectedWinUnits} 份）";
            return summary.QuotaMode switch
            {
                global::FestivalBeastMaterialQuotaMode.FestivalMaterialQuotaDailyLegacy =>
                    $"今日还能赚取材料额度：{summary.EarnableRemainingUnits}/{summary.DailyLimit} 份{suffix}",
                global::FestivalBeastMaterialQuotaMode.FestivalMaterialQuotaStage2Weekly =>
                    $"本周还能赚取材料额度：{summary.EarnableRemainingUnits}/{summary.WeeklyLimit} 份{suffix}",
                global::FestivalBeastMaterialQuotaMode.FestivalMaterialQuotaStage1Lifetime =>
                    $"当前还能赚取材料额度：{summary.EarnableRemainingUnits} 份（首套 {summary.LifetimeEarned}/{summary.LifetimeLimit}）{suffix}",
                _ =>
                    $"今日还能赚取材料额度：{summary.EarnableRemainingUnits} 份{suffix}",
            };
        }

        private static string FormatAvailableMaterialText(FestivalBeastMaterialSummarySnapshot summary)
        {
            if (summary == null) return "已赚取、尚未领取的材料余额：等待同步";
            return $"已赚取、尚未领取的材料余额：{summary.AvailableUnits} 份（可跨日领取）";
        }

        private static string BuildActivePetLabel(string petName, bool hasPet, bool alive)
        {
            if (!hasPet) return "当前出战宝宝：未出战";
            string displayName = string.IsNullOrWhiteSpace(petName) ? "已出战宝宝" : petName.Trim();
            return alive ? $"当前出战宝宝：{displayName}" : $"当前出战宝宝：{displayName}（已倒地）";
        }

        private static string ResolveTargetName(ulong actorId, Func<ulong, string> resolveActorName)
        {
            string resolved = resolveActorName != null ? resolveActorName(actorId) : null;
            if (!string.IsNullOrWhiteSpace(resolved)) return resolved.Trim();
            return actorId == 0UL ? "目标" : $"单位#{actorId.ToString(CultureInfo.InvariantCulture)}";
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// 仅限开发/编辑器预览的只读演示状态集合（绝不连接真实写接口、不改动玩家资产）。
        /// 完整覆盖第六节全部状态以便无服或编辑器下自检布局与文案。
        /// </summary>
        public static FestivalBeastDetailSnapshot CreateDemoSnapshot(string scenarioKey)
        {
            const ulong baseStart = 1795564800000UL;
            const ulong baseEnd = baseStart + 14UL * 86400000UL;
            const ulong showcaseEnd = baseEnd + 7UL * 86400000UL;
            var resp = new global::GetFestivalBeastDetailResponse
            {
                ServerNowMs = baseStart + 2UL * 86400000UL,
                Phase = global::FestivalBeastPhase.Open,
                ActivityInstanceId = 202601u,
                ConfigRevision = 1u,
                ActivityName = DefaultActivityName,
                BossName = DefaultBossName,
                EffectiveStartAtMs = baseStart,
                EffectiveEndAtMs = baseEnd,
                ClaimShowcaseEndAtMs = showcaseEnd,
                CurrentWins = 2u,
                RequiredWins = 3u,
                RewardIdentity = "festival_beast_chunxishou_v1",
                RewardDefinitionVersion = 1u,
                RewardPetTableId = 101u,
                RewardPetModelId = 1012u,
                RewardPetName = DefaultBossName,
                CanStartChallenge = true,
                MatchedTierId = 3u,
                RoundLimit = 12u,
                HeavyDamagePercent = 180u,
                MaterialSummary = new global::FestivalBeastMaterialSummary
                {
                    QuotaMode = global::FestivalBeastMaterialQuotaMode.FestivalMaterialQuotaDailyLegacy,
                    ExpectedWinUnits = 10u,
                    EarnableRemainingUnits = 10u,
                    AvailableUnits = 30UL,
                    EarnedToday = 10u,
                    DailyLimit = 20u,
                },
            };

            switch (scenarioKey)
            {
                case "unscheduled":
                    resp.Phase = global::FestivalBeastPhase.Unscheduled;
                    resp.EffectiveStartAtMs = 0UL; resp.EffectiveEndAtMs = 0UL;
                    resp.CanStartChallenge = false;
                    resp.StartBlockCode = global::FestivalBeastActionBlockCode.FestivalBeastBlockNotOpen;
                    resp.StartBlockReason = "活动尚未排期开放。";
                    break;
                case "upcoming":
                    resp.Phase = global::FestivalBeastPhase.Upcoming;
                    resp.ServerNowMs = baseStart - 3600_000UL;
                    resp.CanStartChallenge = false;
                    resp.StartBlockCode = global::FestivalBeastActionBlockCode.FestivalBeastBlockNotOpen;
                    resp.StartBlockReason = "活动将于北京时间 2026-11-25 08:00:00 正式开放。";
                    break;
                case "claimable_showcase":
                    resp.Phase = global::FestivalBeastPhase.ClaimShowcase;
                    resp.ServerNowMs = baseEnd + 86400000UL;
                    resp.CurrentWins = 3u;
                    resp.HasPermanentEligibility = true;
                    resp.CanStartChallenge = false;
                    resp.CanClaimReward = true;
                    break;
                case "history_claimable":
                    resp.Phase = global::FestivalBeastPhase.History;
                    resp.ServerNowMs = showcaseEnd + 86400000UL;
                    resp.CurrentWins = 0u;
                    resp.HasPermanentEligibility = true;
                    resp.CanStartChallenge = false;
                    resp.CanClaimReward = true;
                    break;
                case "new_period_zero_old_eligible":
                    resp.Phase = global::FestivalBeastPhase.Open;
                    resp.ActivityInstanceId = 202701u;
                    resp.CurrentWins = 0u;
                    resp.HasPermanentEligibility = true;
                    resp.CanStartChallenge = true;
                    resp.CanClaimReward = true;
                    break;
                case "no_pet":
                    resp.CanStartChallenge = false;
                    resp.StartBlockCode = global::FestivalBeastActionBlockCode.FestivalBeastBlockPetRequired;
                    resp.StartBlockReason = "请先出战一只存活的宝宝后再挑战。";
                    break;
                case "dead_pet":
                    resp.CanStartChallenge = false;
                    resp.StartBlockCode = global::FestivalBeastActionBlockCode.FestivalBeastBlockPetDead;
                    resp.StartBlockReason = "当前出战宝宝已倒地，请先恢复状态或更换宝宝。";
                    break;
                case "in_battle":
                    resp.CanStartChallenge = false;
                    resp.StartBlockCode = global::FestivalBeastActionBlockCode.FestivalBeastBlockInBattle;
                    resp.StartBlockReason = "当前已在其他战斗中，请先结束战局。";
                    break;
                case "launch_unknown":
                    resp.CanStartChallenge = false;
                    resp.StartBlockCode = global::FestivalBeastActionBlockCode.FestivalBeastBlockLaunchInFlight;
                    resp.PendingLaunch = new global::FestivalBeastPendingLaunchInfo
                    {
                        ClientRequestId = "demo-launch-uuid-0001",
                        LaunchState = global::FestivalBeastLaunchState.FestivalBeastLaunchUnknown,
                    };
                    break;
                case "claim_confirming":
                    resp.CurrentWins = 3u;
                    resp.HasPermanentEligibility = true;
                    resp.RewardClaimed = true;
                    resp.RewardClaimDurable = false;
                    resp.ClaimRequestId = "demo-claim-uuid-0001";
                    resp.ClaimBlockCode = global::FestivalBeastActionBlockCode.FestivalBeastBlockClaimConfirming;
                    break;
                case "claimed_open":
                    resp.CurrentWins = 3u;
                    resp.HasPermanentEligibility = true;
                    resp.RewardClaimed = true;
                    resp.RewardClaimDurable = true;
                    resp.ClaimedPetId = 96000001UL;
                    resp.CanStartChallenge = true;
                    break;
                case "claimed_ended":
                    resp.Phase = global::FestivalBeastPhase.History;
                    resp.CurrentWins = 3u;
                    resp.HasPermanentEligibility = true;
                    resp.RewardClaimed = true;
                    resp.RewardClaimDurable = true;
                    resp.ClaimedPetId = 96000001UL;
                    resp.CanStartChallenge = false;
                    break;
                case "daily_cap_full_carryover_balance":
                    resp.MaterialSummary.EarnedToday = 20u;
                    resp.MaterialSummary.EarnableRemainingUnits = 0u;
                    resp.MaterialSummary.AvailableUnits = 50UL;
                    break;
            }
            return new FestivalBeastDetailSnapshot(resp, 0L);
        }
#endif
    }
}
