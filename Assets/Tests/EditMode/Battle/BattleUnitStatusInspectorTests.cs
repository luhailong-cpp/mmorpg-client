using MmorpgClient.Game.Battle;
using MmorpgClient.UI.Ugui.Battle;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class BattleUnitStatusInspectorTests
    {
        [Test]
        public void InspectAggregatesShieldsSurfacesChargeAndProjectsBuffDetails()
        {
            var actor = new BattleActorState
            {
                ActorId = 101,
                MaxHealth = 2000,
                ChargingSkillId = 10115,
            };
            actor.Buffs.Add(new BattleBuffEntry
            {
                BuffId = 1,
                BuffTableId = 100400,
                SourceSkillId = 10040,
                RemainRounds = 2,
                RemainingCapacity = 600,
            });
            actor.Buffs.Add(new BattleBuffEntry
            {
                BuffId = 2,
                BuffTableId = 110050,
                SourceSkillId = 11005,
                RemainRounds = 3,
                RemainingCapacity = 400,
                RemainingHits = 2,
            });
            actor.Buffs.Add(new BattleBuffEntry
            {
                BuffId = 3,
                BuffTableId = 141010,
                SourceSkillId = 14101,
                RemainRounds = 4,
                Stacks = 3,
            });

            var summary = BattleUnitStatusInspector.Inspect(actor, id => id switch
            {
                10040 => "太清灵盾",
                11005 => "如意灵环",
                10115 => "蓄锋破阵",
                14101 => "连击破绽",
                _ => null,
            });

            Assert.That(summary.TotalShieldCapacity, Is.EqualTo(1000UL));
            Assert.That(summary.ShieldRatio, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(summary.IsCharging, Is.True);
            Assert.That(summary.ChargingSkillName, Is.EqualTo("蓄锋破阵"));
            Assert.That(summary.Buffs.Count, Is.EqualTo(3));
            Assert.That(summary.Buffs[0].SourceName, Is.EqualTo("太清灵盾"));
            Assert.That(summary.Buffs[1].RemainingHits, Is.EqualTo(2u));
            Assert.That(summary.Buffs[2].Stacks, Is.EqualTo(3u));

            string body = BattleScreen.FormatUnitStatusBody(actor, summary);
            Assert.That(body, Does.Contain("持久护盾：1000"));
            Assert.That(body, Does.Contain("蓄力中：蓄锋破阵"));
            Assert.That(body, Does.Contain("太清灵盾"));
            Assert.That(body, Does.Contain("余2次"));
            Assert.That(body, Does.Contain("×3层"));
        }

        [Test]
        public void FormatUnitStatusBodyShowsExactZeroAndStackedGrowingAuraBonuses()
        {
            var actor = new BattleActorState { ActorId = 101, MaxHealth = 5000 };
            actor.Buffs.Add(new BattleBuffEntry
            {
                BuffId = 10,
                BuffTableId = 120230,
                SourceSkillId = 12023,
                Layer = 1,
                Stacks = 0,
            });
            actor.Buffs.Add(new BattleBuffEntry
            {
                BuffId = 11,
                BuffTableId = 120240,
                SourceSkillId = 12024,
                Layer = 1,
                Stacks = 12,
            });

            var summary = BattleUnitStatusInspector.Inspect(actor, id => id switch
            {
                12023 => "承锋蓄势",
                12024 => "循隙破甲",
                _ => null,
            });

            Assert.That(summary.Buffs[0].RawStacks, Is.EqualTo(0u));
            Assert.That(summary.Buffs[1].RawStacks, Is.EqualTo(12u));

            string body = BattleScreen.FormatUnitStatusBody(actor, summary, buffId => buffId switch
            {
                120230 => new BuffTable { Id = 120230, BattleOp = 25, ModifierStat = 25, Ratio = 0.01, StackLimit = 300, InfiniteDuration = 1 },
                120240 => new BuffTable { Id = 120240, BattleOp = 25, ModifierStat = 26, Ratio = 0.01, StackLimit = 50, InfiniteDuration = 1 },
                _ => null,
            });

            Assert.That(body, Does.Contain("承锋蓄势  0/300层（待触发）  至战斗结束"));
            Assert.That(body, Does.Contain("循隙破甲  ×12/50层 · 无视防御+12%  至战斗结束"));
        }
    }
}
