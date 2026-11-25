using MmorpgClient.Game.FestivalBeast;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class FestivalBeastUiStateTests
    {
        private const ulong StartMs = 1795564800000UL;
        private const ulong EndMs = StartMs + 14UL * 86400000UL;
        private const ulong ShowcaseEndMs = EndMs + 7UL * 86400000UL;

        private static FestivalBeastDetailSnapshot Detail(
            FestivalBeastPhase phase = FestivalBeastPhase.Open,
            uint wins = 2u,
            bool hasElig = false,
            bool claimed = false,
            bool durable = false,
            ulong petId = 0UL,
            bool canStart = true,
            FestivalBeastActionBlockCode startCode = FestivalBeastActionBlockCode.FestivalBeastBlockNone,
            string startReason = "",
            bool canClaim = false,
            FestivalBeastActionBlockCode claimCode = FestivalBeastActionBlockCode.FestivalBeastBlockNone,
            FestivalBeastPendingLaunchInfo pendingLaunch = null,
            uint earnableRemaining = 10u,
            ulong availableUnits = 30UL)
        {
            var resp = new GetFestivalBeastDetailResponse
            {
                ServerNowMs = StartMs + 86400000UL,
                Phase = phase,
                ActivityInstanceId = 202601u,
                ConfigRevision = 1u,
                ActivityName = "瑞兽迎春",
                BossName = "春禧兽",
                EffectiveStartAtMs = phase == FestivalBeastPhase.Unscheduled ? 0UL : StartMs,
                EffectiveEndAtMs = phase == FestivalBeastPhase.Unscheduled ? 0UL : EndMs,
                ClaimShowcaseEndAtMs = phase == FestivalBeastPhase.Unscheduled ? 0UL : ShowcaseEndMs,
                CurrentWins = wins,
                RequiredWins = 3u,
                RewardIdentity = "festival_beast_chunxishou_v1",
                RewardDefinitionVersion = 1u,
                RewardPetTableId = 101u,
                RewardPetName = "春禧兽",
                HasPermanentEligibility = hasElig,
                RewardClaimed = claimed,
                RewardClaimDurable = durable,
                ClaimedPetId = petId,
                ClaimRequestId = claimed ? "11111111-2222-4333-8444-555555555551" : "",
                CanStartChallenge = canStart,
                StartBlockCode = startCode,
                StartBlockReason = startReason,
                CanClaimReward = canClaim,
                ClaimBlockCode = claimCode,
                RoundLimit = 12u,
                HeavyDamagePercent = 180u,
                MaterialSummary = new FestivalBeastMaterialSummary
                {
                    QuotaMode = FestivalBeastMaterialQuotaMode.FestivalMaterialQuotaDailyLegacy,
                    ExpectedWinUnits = 10u,
                    EarnableRemainingUnits = earnableRemaining,
                    AvailableUnits = availableUnits,
                    EarnedToday = 20u - earnableRemaining,
                    DailyLimit = 20u,
                },
            };
            if (pendingLaunch != null) resp.PendingLaunch = pendingLaunch;
            return new FestivalBeastDetailSnapshot(resp, 0L);
        }

        [Test]
        public void MainPanelCoversLoadingFailureAllFivePhasesAndEndedClaimedHidesContinue()
        {
            // 首次加载与加载失败
            var loading = FestivalBeastUiState.BuildMainPanel(null, true, false, false, false, false, "", false, 0UL, "", false, false);
            Assert.That(loading.IsLoadingInitial, Is.True);
            Assert.That(loading.PrimaryButtonEnabled, Is.False);

            var failed = FestivalBeastUiState.BuildMainPanel(null, false, false, false, false, false, "网络超时", true, 0UL, "", false, false);
            Assert.That(failed.ShowRefreshButton, Is.True);
            Assert.That(failed.StatusBannerIsError, Is.True);
            Assert.That(failed.ShowAppearanceEntry, Is.False);

            // 五阶段：未排期、未开始、进行中、集中领奖期、历史待领奖
            var unsched = FestivalBeastUiState.BuildMainPanel(Detail(FestivalBeastPhase.Unscheduled, 0, canStart: false),
                false, false, false, false, false, "", false, 0UL, "青云灵狐", true, true);
            Assert.That(unsched.PhaseBadgeText, Is.EqualTo("未排期"));
            Assert.That(unsched.PrimaryButtonEnabled, Is.False);

            var upcoming = FestivalBeastUiState.BuildMainPanel(Detail(FestivalBeastPhase.Upcoming, 0, canStart: false),
                false, false, false, false, false, "", false, 3600_000UL, "青云灵狐", true, true);
            Assert.That(upcoming.PhaseBadgeText, Is.EqualTo("即将开启"));
            Assert.That(upcoming.CountdownText, Does.StartWith("距开启还剩："));

            var open = FestivalBeastUiState.BuildMainPanel(Detail(FestivalBeastPhase.Open, 2, canStart: true),
                false, false, false, false, false, "", false, 86400_000UL, "青云灵狐", true, true);
            Assert.That(open.ProgressText, Is.EqualTo("2/3"));
            Assert.That(open.PrimaryButtonKind, Is.EqualTo(FestivalBeastPrimaryButtonKind.StartChallenge));
            Assert.That(open.PrimaryButtonEnabled, Is.True);
            Assert.That(open.TimeWindowText, Does.Contain("北京时间 UTC+8"));

            var showcase = FestivalBeastUiState.BuildMainPanel(
                Detail(FestivalBeastPhase.ClaimShowcase, 3, hasElig: true, canStart: false, canClaim: true),
                false, false, false, false, false, "", false, 86400_000UL, "青云灵狐", true, true);
            Assert.That(showcase.PrimaryButtonKind, Is.EqualTo(FestivalBeastPrimaryButtonKind.ClaimReward));
            Assert.That(showcase.PrimaryButtonText, Is.EqualTo("领取神兽"));
            Assert.That(showcase.ShowAppearanceEntry, Is.False, "领奖态不推销人物外观。");

            var history = FestivalBeastUiState.BuildMainPanel(
                Detail(FestivalBeastPhase.History, 0, hasElig: true, canStart: false, canClaim: true),
                false, false, false, false, false, "", false, 0UL, "青云灵狐", true, true);
            Assert.That(history.PhaseBadgeText, Does.Contain("历史待领奖"));
            Assert.That(history.PrimaryButtonKind, Is.EqualTo(FestivalBeastPrimaryButtonKind.ClaimReward));

            // 已结束且已领取：没有继续挑战按钮
            var endedClaimed = FestivalBeastUiState.BuildMainPanel(
                Detail(FestivalBeastPhase.History, 3, hasElig: true, claimed: true, durable: true, petId: 96000001UL, canStart: false),
                false, false, false, false, false, "", false, 0UL, "青云灵狐", true, true);
            Assert.That(endedClaimed.PrimaryButtonKind, Is.EqualTo(FestivalBeastPrimaryButtonKind.Hidden));
            Assert.That(endedClaimed.ShowClaimedPetReceipt, Is.True);
            Assert.That(endedClaimed.ClaimedPetReceiptText, Does.Contain("96000001"));
            Assert.That(endedClaimed.ClaimedPetReceiptText, Does.Contain("不会自动顶替"));
        }

        [Test]
        public void MainPanelHonorsButtonPriorityCarryoverMaterialsLongNamesAndBlockStates()
        {
            // 新一期 0/3，但旧神兽永久资格仍可优先领取
            var newPeriodOldElig = FestivalBeastUiState.BuildMainPanel(
                Detail(FestivalBeastPhase.Open, wins: 0u, hasElig: true, canStart: true, canClaim: true),
                false, false, false, false, false, "", false, 86400_000UL, "青云灵狐", true, true);
            Assert.That(newPeriodOldElig.ProgressText, Is.EqualTo("0/3"));
            Assert.That(newPeriodOldElig.PrimaryButtonKind, Is.EqualTo(FestivalBeastPrimaryButtonKind.ClaimReward));

            // 领取确认中优先级最高，绝不提前展示已领取回执
            var confirming = FestivalBeastUiState.BuildMainPanel(
                Detail(FestivalBeastPhase.Open, wins: 3u, hasElig: true, claimed: true, durable: false, petId: 96000001UL,
                    canStart: false, canClaim: false, claimCode: FestivalBeastActionBlockCode.FestivalBeastBlockClaimConfirming),
                false, false, false, false, true, "", false, 86400_000UL, "青云灵狐", true, true);
            Assert.That(confirming.PrimaryButtonKind, Is.EqualTo(FestivalBeastPrimaryButtonKind.ConfirmingClaim));
            Assert.That(confirming.PrimaryButtonText, Is.EqualTo("正在确认领取"));
            Assert.That(confirming.ShowClaimedPetReceipt, Is.False);

            // 已领取且开放：继续挑战并说明不重复发第二只
            var claimedOpen = FestivalBeastUiState.BuildMainPanel(
                Detail(FestivalBeastPhase.Open, wins: 3u, hasElig: true, claimed: true, durable: true, petId: 96000001UL, canStart: true),
                false, false, false, false, false, "", false, 86400_000UL, "一二三四五六七八九十超长名字测试宝宝", true, true);
            Assert.That(claimedOpen.PrimaryButtonKind, Is.EqualTo(FestivalBeastPrimaryButtonKind.ContinueChallenge));
            Assert.That(claimedOpen.PrimaryButtonText, Is.EqualTo("继续挑战"));
            Assert.That(claimedOpen.PrimarySubtext, Does.Contain("不会重复发放第二只"));
            Assert.That(claimedOpen.ActivePetLabel, Does.Contain("一二三四五六七八九十超长名字测试宝宝"));

            // 今日已赚满 20，但历史跨日余额 50 仍可跳转领取
            var cappedDaily = FestivalBeastUiState.BuildMainPanel(
                Detail(FestivalBeastPhase.Open, wins: 1u, canStart: true, earnableRemaining: 0u, availableUnits: 50UL),
                false, false, false, false, false, "", false, 86400_000UL, "灵狐", true, true);
            Assert.That(cappedDaily.EarnableMaterialText, Does.Contain("0/20"));
            Assert.That(cappedDaily.EarnableMaterialText, Does.Contain("继续通关仍推进神兽进度"));
            Assert.That(cappedDaily.AvailableMaterialText, Does.Contain("50 份"));
            Assert.That(cappedDaily.CanJumpMaterialClaim, Is.True);

            // 无宠 / 死宠 / 开局未知 / 战斗占用阻断
            var deadPet = FestivalBeastUiState.BuildMainPanel(
                Detail(FestivalBeastPhase.Open, canStart: false, startCode: FestivalBeastActionBlockCode.FestivalBeastBlockPetDead,
                    startReason: "当前出战宝宝已倒地"),
                false, false, false, false, false, "", false, 86400_000UL, "灵狐", true, false);
            Assert.That(deadPet.PrimaryButtonEnabled, Is.False);
            Assert.That(deadPet.ActivePetLabel, Does.Contain("已倒地"));

            var launchUnknown = FestivalBeastUiState.BuildMainPanel(
                Detail(FestivalBeastPhase.Open, canStart: false,
                    pendingLaunch: new FestivalBeastPendingLaunchInfo
                    {
                        ClientRequestId = "11111111-2222-4333-8444-555555555559",
                        LaunchState = FestivalBeastLaunchState.FestivalBeastLaunchUnknown,
                    }),
                false, false, false, true, false, "", false, 86400_000UL, "灵狐", true, true);
            Assert.That(launchUnknown.PrimaryButtonEnabled, Is.False);
            Assert.That(launchUnknown.PrimaryButtonText, Is.EqualTo("开局处理中"));
        }

        [Test]
        public void BattleHudAndAllSixSettlementStatesMatchExactCopyContract()
        {
            // 战斗预告：第 2/12 回合预告 -> 第 3/12 回合兑现 -> 受控取消
            var r2 = FestivalBeastUiState.BuildBattleHud(
                2u, 12u,
                new FestivalBeastTelegraphSnapshot(9001UL, 8001UL, 2u, 3u, FestivalBeastModels.TelegraphKindFestivalHeavyStrike, FestivalBeastModels.TelegraphStatusUpcoming, 1UL),
                null,
                id => id == 8001UL ? "云霄青莲剑灵宝宝" : "主角");
            Assert.That(r2.RoundDisplayText, Is.EqualTo("第2/12回合"));
            Assert.That(r2.HeadlineText, Is.EqualTo("下回合：春禧兽将重击【云霄青莲剑灵宝宝】"));
            Assert.That(r2.HintText, Is.EqualTo("目标可本轮防御，另一方可输出；控制可能被抵抗。"));
            Assert.That(r2.MarkedTargetActorId, Is.EqualTo(8001UL));

            var r3 = FestivalBeastUiState.BuildBattleHud(
                3u, 12u,
                new FestivalBeastTelegraphSnapshot(9001UL, 8001UL, 2u, 3u, FestivalBeastModels.TelegraphKindFestivalHeavyStrike, FestivalBeastModels.TelegraphStatusExecuting, 2UL),
                null,
                id => id == 8001UL ? "云霄青莲剑灵宝宝" : "主角");
            Assert.That(r3.RoundDisplayText, Is.EqualTo("第3/12回合"));
            Assert.That(r3.HeadlineText, Is.EqualTo("本回合重击【云霄青莲剑灵宝宝】，在人宠正常行动后发动。"));
            Assert.That(r3.IsExecutingRound, Is.True);

            var r3Cancelled = FestivalBeastUiState.BuildBattleHud(
                3u, 12u,
                null,
                new FestivalBeastTelegraphSnapshot(9001UL, 8001UL, 2u, 3u, FestivalBeastModels.TelegraphKindFestivalHeavyStrike, FestivalBeastModels.TelegraphStatusCancelledByControl, 3UL),
                _ => "主角");
            Assert.That(r3Cancelled.IsCancelled, Is.True);
            Assert.That(r3Cancelled.MarkedTargetActorId, Is.EqualTo(0UL));

            // 结算 6 态
            var settling = FestivalBeastUiState.BuildSettlement(null, null, false, false);
            Assert.That(settling.IsSettlingOrUnknown, Is.True);
            Assert.That(settling.ShowRetryButton, Is.False);

            var firstThirdWin = FestivalBeastUiState.BuildSettlement(
                new FestivalBeastSettlementSnapshot(5001UL, eBattleOutcome.BattleOutcomeSideAWin, 202601u, "festival_beast_chunxishou_v1", 2u, 3u, 3u, true, 10u, 1u, 30UL, 10u),
                Detail(wins: 3u, hasElig: true, canClaim: true),
                false, false);
            Assert.That(firstThirdWin.ShowClaimBeastButton, Is.True);
            Assert.That(firstThirdWin.MaterialGainLineText, Is.EqualTo("本局新增可领取额度 +10"));
            Assert.That(firstThirdWin.MaterialGainLineText, Does.Not.Contain("已入包"));

            var alreadyEligibleAgain = FestivalBeastUiState.BuildSettlement(
                new FestivalBeastSettlementSnapshot(5002UL, eBattleOutcome.BattleOutcomeSideAWin, 202601u, "festival_beast_chunxishou_v1", 3u, 3u, 3u, false, 10u, 0u, 40UL, 0u),
                Detail(wins: 3u, hasElig: true, canClaim: true),
                false, false);
            Assert.That(alreadyEligibleAgain.ShowClaimBeastButton, Is.True);
            Assert.That(alreadyEligibleAgain.SubNoteText, Does.Contain("不重复触发首次解锁"));

            var alreadyOwned = FestivalBeastUiState.BuildSettlement(
                new FestivalBeastSettlementSnapshot(5003UL, eBattleOutcome.BattleOutcomeSideAWin, 202601u, "festival_beast_chunxishou_v1", 3u, 3u, 3u, false, 10u, 0u, 50UL, 0u),
                Detail(wins: 3u, hasElig: true, claimed: true, durable: true, petId: 96000001UL),
                false, false);
            Assert.That(alreadyOwned.ShowClaimBeastButton, Is.False);
            Assert.That(alreadyOwned.HeadlineText, Does.Contain("已拥有"));

            var defeat = FestivalBeastUiState.BuildSettlement(
                new FestivalBeastSettlementSnapshot(5004UL, eBattleOutcome.BattleOutcomeSideBWin, 202601u, "festival_beast_chunxishou_v1", 1u, 1u, 3u, false, 0u, 0u, 30UL, 10u),
                Detail(wins: 1u, canStart: true),
                false, false);
            Assert.That(defeat.ShowRetryButton, Is.True);
            Assert.That(defeat.ProgressLineText, Does.Contain("1/3"));

            var voided = FestivalBeastUiState.BuildSettlement(
                new FestivalBeastSettlementSnapshot(5005UL, eBattleOutcome.BattleOutcomeVoid, 202601u, "festival_beast_chunxishou_v1", 1u, 1u, 3u, false, 0u, 0u, 30UL, 10u),
                Detail(wins: 1u, canStart: true),
                false, false);
            Assert.That(voided.HeadlineText, Is.EqualTo("本局已作废"));
            Assert.That(voided.ShowRetryButton, Is.True);
        }
    }
}
