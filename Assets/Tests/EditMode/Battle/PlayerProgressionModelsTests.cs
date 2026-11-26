using MmorpgClient.Game.CoreLoop;
using MmorpgClient.UI.Ugui.Battle;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class PlayerProgressionModelsTests
    {
        [Test]
        public void BuildExpSnapshot_NormalLevelAndUnlockedPoints_ComputesRatioAndSummary()
        {
            var panel = new AttributePanelInfo
            {
                Level = 4u,
                TotalExp = 1000ul,
                CurrentLevelExp = 100ul,
                NextLevelExp = 500ul,
                IsMaxLevel = false,
            };
            panel.Pools.Add(new AttributePoolInfo { PoolId = 1u, Unlocked = true, Remaining = 15u });
            panel.Pools.Add(new AttributePoolInfo { PoolId = 2u, Unlocked = false, Remaining = 99u });

            var snap = PlayerProgressionModels.BuildExpSnapshot(panel);
            Assert.IsNotNull(snap);
            Assert.AreEqual(4u, snap.Level);
            Assert.AreEqual(1000ul, snap.TotalExp);
            Assert.AreEqual(100ul, snap.CurrentLevelExp);
            Assert.AreEqual(500ul, snap.NextLevelExp);
            Assert.IsFalse(snap.IsMaxLevel);
            Assert.AreEqual(15u, snap.UnallocatedPoints);
            Assert.AreEqual(0.2, snap.ProgressRatio, 1e-6);
            Assert.AreEqual("经验 100 / 500", snap.ExpProgressText);
        }

        [Test]
        public void BuildExpSnapshot_MaxLevel_ReturnsFullRatioAndMaxLabel()
        {
            var panel = new AttributePanelInfo
            {
                Level = 85u,
                TotalExp = 300000ul,
                CurrentLevelExp = 0ul,
                NextLevelExp = 0ul,
                IsMaxLevel = true,
            };
            var snap = PlayerProgressionModels.BuildExpSnapshot(panel);
            Assert.IsNotNull(snap);
            Assert.IsTrue(snap.IsMaxLevel);
            Assert.AreEqual(1.0, snap.ProgressRatio, 1e-6);
            Assert.AreEqual("已满级 · 累计经验 300,000", snap.ExpProgressText);
            Assert.AreEqual("已满级", snap.CompactExpText);
        }

        [Test]
        public void BuildSettlementView_FromBattleEnd_FormatsLevelUpAndDiminishedSilver()
        {
            var end = new BattleEndS2C
            {
                BattleId = 701ul,
                LevelBefore = 1u,
                LevelAfter = 4u,
                ExpGain = 1000ul,
                SilverGain = 600ul,
                TotalExpAfter = 1000ul,
                NextLevelTotalExp = 1400ul,
                BattleSilverDiminished = true,
            };
            var view = PlayerProgressionModels.BuildSettlementView(end);
            Assert.IsNotNull(view);
            Assert.IsTrue(view.LeveledUp);
            Assert.IsTrue(view.HasVisibleRewards);
            Assert.AreEqual("角色升级：Lv.1 → Lv.4", view.LevelUpBannerLine);
            Assert.AreEqual("经验 +1,000 · 银两 +600（本周银两已触发衰减）", view.RewardSummaryLine);
        }

        [Test]
        public void BuildSettlementView_ZeroRewardBattle_ReturnsNull()
        {
            var end = new BattleEndS2C { BattleId = 702ul, LevelBefore = 10u, LevelAfter = 10u };
            Assert.IsNull(PlayerProgressionModels.BuildSettlementView(end));

            var recovery = new PlayerBattleResult { BattleId = 702ul, LevelBefore = 10u, LevelAfter = 10u };
            Assert.IsNull(PlayerProgressionModels.BuildSettlementView(recovery));
        }

        [Test]
        public void BuildSettlementView_FromRecoveryResult_ExtractsSilverAndPreservesUlongPrecision()
        {
            var result = new PlayerBattleResult
            {
                BattleId = 703ul,
                LevelBefore = 84u,
                LevelAfter = 85u,
                ExpGain = 1300ul,
                TotalExpAfter = 5_000_000_000ul,
                NextLevelTotalExp = 0ul,
                BattleSilverDiminished = false,
            };
            result.Currencies.Add(new PlayerBattleResultCurrency { CurrencyType = 0u, Amount = 1000ul });
            result.Currencies.Add(new PlayerBattleResultCurrency { CurrencyType = 2u, Amount = 50ul });

            var view = PlayerProgressionModels.BuildSettlementView(result);
            Assert.IsNotNull(view);
            Assert.IsTrue(view.LeveledUp);
            Assert.AreEqual(1000ul, view.SilverGain);
            Assert.AreEqual(5_000_000_000ul, view.TotalExpAfter);
            Assert.AreEqual("经验 +1,300 · 银两 +1,000", view.RewardSummaryLine);
            Assert.AreEqual("角色升级：Lv.84 → Lv.85", view.LevelUpBannerLine);
        }

        [Test]
        public void BattleResultPanel_BuildRewardLinesFromBattleEnd_OverlaysSceneProgressionAndLevelUp()
        {
            var end = new BattleEndS2C
            {
                BattleId = 704ul,
                LevelBefore = 1u,
                LevelAfter = 4u,
                ExpGain = 1000ul,
                SilverGain = 120ul,
                BattleSilverDiminished = true,
                Settlement = new BattleSettlementData
                {
                    Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                    TotalRounds = 3u,
                    Health = 120,
                    Mana = 60,
                },
            };

            var lines = BattleResultPanel.BuildRewardLines(end);
            Assert.AreEqual("角色升级  Lv.1 → Lv.4", lines[0]);
            Assert.AreEqual("经验  +1000", lines[1]);
            Assert.AreEqual("银两  +120（本周银两已触发衰减）", lines[2]);
        }

        [Test]
        public void BattleResultPanel_BuildRecoveredLines_IncludesLevelUpAndDiminishedNoticeWhenPresent()
        {
            var result = new PlayerBattleResult
            {
                BattleId = 705ul,
                Outcome = (uint)eBattleOutcome.BattleOutcomeSideAWin,
                LevelBefore = 9u,
                LevelAfter = 10u,
                ExpGain = 1000ul,
                BattleSilverDiminished = true,
            };
            result.Currencies.Add(new PlayerBattleResultCurrency { CurrencyType = 0u, Amount = 120ul });

            var lines = BattleResultPanel.BuildRecoveredLines(result);
            Assert.AreEqual("角色升级  Lv.9 → Lv.10", lines[0]);
            Assert.AreEqual("经验  +1000", lines[1]);
            Assert.AreEqual("银两  +120", lines[2]);
            CollectionAssert.Contains(lines, "提示  本周战斗银两已触发衰减");
        }
    }
}
