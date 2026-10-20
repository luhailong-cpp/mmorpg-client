using System.Collections.Generic;
using MmorpgClient.Game.Battle.Presentation;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// BattleEventItem.mana_delta(服务端 class-skills.md §6.3,带符号:负 = 消耗、正 = 恢复)在演出计划里的走向。
    /// 老事件不带 mana_delta 时沿用"自己扣自己按消耗"的推断(TurnPlanTests.Mana_* 覆盖)。
    /// </summary>
    public sealed class ManaDeltaPlanTests
    {
        private const ulong A = 1;
        private const ulong B = 2;
        private const int Skill = BattleEventCodes.Skill;
        private const int Mana = BattleEventCodes.Mana;

        private static TurnEventInput ManaEvent(ulong source, ulong target, long value, long delta, ulong after, uint group)
            => new TurnEventInput
            {
                Type = Mana, SourceId = source, TargetId = target, Value = value, GroupId = group,
                ManaAfter = after, HasManaAfter = true, ManaDelta = delta, HasManaDelta = true,
            };

        [Test]
        public void SelfRestoreIsPositiveNotACost()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                new TurnEventInput { Type = Skill, SourceId = A, TargetId = A, SkillId = 1204, GroupId = 1 },
                ManaEvent(A, A, 20, +20, 90, 1),
            });
            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(20, plan.Beats[0].ActorManaDelta, "太虚澄心:自身回蓝不能显示成扣蓝");
            Assert.AreEqual(90UL, plan.Beats[0].ActorManaAfter);
        }

        [Test]
        public void CostThenRestoreInOneActionAccumulatesAndKeepsTheLastValue()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                new TurnEventInput { Type = Skill, SourceId = A, TargetId = A, SkillId = 9, GroupId = 1 },
                ManaEvent(A, A, 10, -10, 90, 1),
                ManaEvent(A, A, 30, +30, 120, 1),
            });
            Assert.AreEqual(20, plan.Beats[0].ActorManaDelta, "按事件次序累计,不能只留最后一次");
            Assert.AreEqual(120UL, plan.Beats[0].ActorManaAfter, "终值以最后一条权威 target_mana_after 为准");
        }

        [Test]
        public void ZeroDeltaAtFullManaStaysZero()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                new TurnEventInput { Type = Skill, SourceId = A, TargetId = A, SkillId = 1204, GroupId = 1 },
                ManaEvent(A, A, 0, 0, 100, 1),
            });
            Assert.AreEqual(0, plan.Beats[0].ActorManaDelta);
        }

        [Test]
        public void RestoreOnAnotherUnitIsPositiveOnThatTarget()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                new TurnEventInput { Type = Skill, SourceId = A, TargetId = B, SkillId = 9, GroupId = 1 },
                ManaEvent(A, B, 25, +25, 60, 1),
            });
            var target = plan.Beats[0].FindTarget(B);
            Assert.IsNotNull(target);
            Assert.AreEqual(TargetEffect.Mana, target.Effect);
            Assert.AreEqual(25, target.Value);
            Assert.AreEqual(60UL, target.ManaAfter);
        }

        [Test]
        public void FromCopiesTheSignedDeltaOnlyWhenPresent()
        {
            var withDelta = TurnEventInput.From(new BattleEventItem
                { EventType = eBattleEventType.BattleEventMana, SourceId = A, TargetId = A, Value = 7, ManaDelta = 7, TargetManaAfter = 50 });
            Assert.IsTrue(withDelta.HasManaDelta);
            Assert.AreEqual(7, withDelta.ManaDelta);

            var legacy = TurnEventInput.From(new BattleEventItem
                { EventType = eBattleEventType.BattleEventMana, SourceId = A, TargetId = A, Value = 7, TargetManaAfter = 50 });
            Assert.IsFalse(legacy.HasManaDelta);
        }
    }
}
