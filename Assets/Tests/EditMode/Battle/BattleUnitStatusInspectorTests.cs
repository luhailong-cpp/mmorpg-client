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
    }
}
