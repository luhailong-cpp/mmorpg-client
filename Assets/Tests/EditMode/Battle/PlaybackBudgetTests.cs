using MmorpgClient.Game.Battle.Presentation;
using MmorpgClient.UI.Ugui.Battle;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 演出预算(PlaybackBudget):服务端行动窗口(手动 6s / 全员自动 2s)在广播 TurnResult 前就已起算,
    /// 客户端必须把演出压进 窗口 − 输入余量,否则多人局玩家永远拿不到手动输入。
    /// </summary>
    public sealed class PlaybackBudgetTests
    {
        private const float Eps = 1e-3f;
        private const long Now = 1_700_000_000_000L;

        [Test]
        public void Manual_5v5_AllAttacks_9s_In6sWindow_CompressesToFitWithInputReserve()
        {
            // 10 拍 × 0.9s = 9s,窗口 6s,余量 2.5s → 预算 3.5s → 2.57x,不跳过
            float plan = 10 * TurnPlan.AttackSeconds;
            var d = PlaybackBudget.Decide(plan, (ulong)(Now + 6000), Now, passive: false, baseSpeed: 1f);
            Assert.IsFalse(d.Skip);
            Assert.IsFalse(d.Unbounded);
            Assert.AreEqual(6f - PlaybackBudget.ManualInputReserveSeconds, d.BudgetSeconds, Eps);
            Assert.AreEqual(plan / 3.5f, d.Speed, Eps);
            Assert.LessOrEqual(plan / d.Speed, d.BudgetSeconds + Eps, "压缩后必须在预算内播完");
        }

        [Test]
        public void Manual_3v3_WithTwoSkills_6p4s_AlsoFits()
        {
            float plan = 4 * TurnPlan.AttackSeconds + 2 * TurnPlan.CastSeconds; // 6.4s
            var d = PlaybackBudget.Decide(plan, (ulong)(Now + 6000), Now, passive: false, baseSpeed: 1f);
            Assert.IsFalse(d.Skip);
            Assert.LessOrEqual(plan / d.Speed, 3.5f + Eps);
        }

        [Test]
        public void Auto_5v5_In2sWindow_CompressesWithinMaxSpeed_NoSkip()
        {
            // 全员自动 2s 节奏:预算 1.7s,9s 演出 → 5.3x(≤ 6x 上限),自动基础 1.5x 被更高需求覆盖
            float plan = 10 * TurnPlan.AttackSeconds;
            var d = PlaybackBudget.Decide(plan, (ulong)(Now + 2000), Now, passive: true, baseSpeed: BattlePresenter.AutoBattleSpeed);
            Assert.IsFalse(d.Skip);
            Assert.AreEqual(2f - PlaybackBudget.PassiveReserveSeconds, d.BudgetSeconds, Eps);
            Assert.AreEqual(plan / 1.7f, d.Speed, Eps);
            Assert.LessOrEqual(d.Speed, PlaybackBudget.MaxSpeed);
        }

        [Test]
        public void Auto_10v10_In2sWindow_ExceedsMaxSpeed_Skips()
        {
            // 20 拍 ≈ 18s 塞不进 1.7s(需要 10.6x > 6x):直接跳过只落终态,而不是播一半被下一回合抢占
            float plan = 20 * TurnPlan.AttackSeconds;
            var d = PlaybackBudget.Decide(plan, (ulong)(Now + 2000), Now, passive: true, baseSpeed: BattlePresenter.AutoBattleSpeed);
            Assert.IsTrue(d.Skip);
            Assert.AreEqual(PlaybackBudget.MaxSpeed, d.Speed, Eps);
        }

        [Test]
        public void PlentyOfBudget_KeepsBaseSpeed()
        {
            var manual = PlaybackBudget.Decide(2f, (ulong)(Now + 6000), Now, passive: false, baseSpeed: 1f);
            Assert.IsFalse(manual.Skip);
            Assert.AreEqual(1f, manual.Speed, Eps);

            var auto = PlaybackBudget.Decide(1f, (ulong)(Now + 6000), Now, passive: true, baseSpeed: BattlePresenter.AutoBattleSpeed);
            Assert.AreEqual(BattlePresenter.AutoBattleSpeed, auto.Speed, Eps);
        }

        [Test]
        public void NoDeadline_OrBattleEnded_IsUnbounded()
        {
            var old = PlaybackBudget.Decide(30f, 0UL, Now, passive: false, baseSpeed: 1f);
            Assert.IsTrue(old.Unbounded);
            Assert.IsFalse(old.Skip);
            Assert.AreEqual(1f, old.Speed, Eps);
            Assert.IsTrue(float.IsPositiveInfinity(old.BudgetSeconds));

            // 终局回合:窗口已无意义,末回合演出(含死亡)完整播完再弹结算
            var ended = PlaybackBudget.Decide(30f, (ulong)(Now + 100), Now, passive: true, baseSpeed: BattlePresenter.AutoBattleSpeed, unbounded: true);
            Assert.IsTrue(ended.Unbounded);
            Assert.AreEqual(BattlePresenter.AutoBattleSpeed, ended.Speed, Eps);
        }

        [Test]
        public void DeadlineAlreadyPassed_OrTinyBudget_Skips()
        {
            var late = PlaybackBudget.Decide(3f, (ulong)(Now - 500), Now, passive: false, baseSpeed: 1f);
            Assert.IsTrue(late.Skip);
            Assert.Less(late.BudgetSeconds, 0f);

            var tiny = PlaybackBudget.DecideWithBudget(3f, PlaybackBudget.MinPlayableSeconds - 0.1f, 1f);
            Assert.IsTrue(tiny.Skip);

            var justEnough = PlaybackBudget.DecideWithBudget(3f, PlaybackBudget.MinPlayableSeconds, 1f);
            Assert.IsFalse(justEnough.Skip);
            Assert.AreEqual(3f / PlaybackBudget.MinPlayableSeconds, justEnough.Speed, Eps);
        }

        [Test]
        public void EmptyPlan_NeverSkips_AndBaseSpeedIsAtLeastOne()
        {
            var d = PlaybackBudget.DecideWithBudget(0f, -5f, 0.2f);
            Assert.IsFalse(d.Skip);
            Assert.AreEqual(1f, d.Speed, Eps);
        }
    }
}
