using Google.Protobuf;
using MmorpgClient.Game.Attribute;
using MmorpgClient.Game.Battle.Presentation;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>法宝回执是元数据；实际数值仍消费独立权威事件，属性面板整体保存服务器修正。</summary>
    public sealed class EquipmentBattleProtocolTests
    {
        private const ulong ActorId = 9007199254740901UL;
        private const ulong TargetId = 9007199254740902UL;
        private const ulong ArtifactId = 9007199254740993UL;

        [Test]
        public void ArtifactReceiptSurvivesWireWithOriginalIndexAndDoesNotSplitAttack()
        {
            var result = new TurnResultS2C { RoundIndex = 2 };
            result.Events.Add(new BattleEventItem
            { EventType = eBattleEventType.BattleEventAttack, SourceId = ActorId, TargetId = TargetId, GroupId = 5 });
            result.Events.Add(new BattleEventItem
            {
                EventType = eBattleEventType.BattleEventArtifact, SourceId = ActorId, TargetId = TargetId,
                ArtifactItemId = ArtifactId, ArtifactEffectId = 6, Value = 40, Success = true, GroupId = 5, HitIndex = 1,
            });
            result.Events.Add(new BattleEventItem
            {
                EventType = eBattleEventType.BattleEventDamage, SourceId = ActorId, TargetId = TargetId,
                Value = 160, TargetHealthAfter = 840, GroupId = 5,
            });

            var wire = TurnResultS2C.Parser.ParseFrom(result.ToByteArray());
            var plan = TurnPlan.Build(wire, 0);
            Assert.That(BattleEventCodes.Artifact, Is.EqualTo((int)eBattleEventType.BattleEventArtifact));
            Assert.That(plan.Beats.Count, Is.EqualTo(1));
            Assert.That(plan.TotalSeconds, Is.EqualTo(TurnPlan.AttackSeconds));
            Assert.That(plan.Beats[0].Targets.Count, Is.EqualTo(1));
            Assert.That(plan.Beats[0].Targets[0].Value, Is.EqualTo(160));
            Assert.That(plan.Beats[0].Targets[0].HealthAfter, Is.EqualTo(840));
            Assert.That(plan.ArtifactEvents.Count, Is.EqualTo(1));
            var receipt = plan.ArtifactEvents[0];
            Assert.That(receipt.EventIndex, Is.EqualTo(1));
            Assert.That(receipt.Input.ArtifactItemId, Is.EqualTo(ArtifactId));
            Assert.That(receipt.Input.ArtifactEffectId, Is.EqualTo(6));
            Assert.That(receipt.Input.SourceId, Is.EqualTo(ActorId));
            Assert.That(receipt.Input.TargetId, Is.EqualTo(TargetId));
            Assert.That(receipt.Input.GroupId, Is.EqualTo(5));
            Assert.That(receipt.Input.HitIndex, Is.EqualTo(1));
            Assert.That(receipt.Input.Value, Is.EqualTo(40));
            Assert.That(receipt.Input.Success, Is.True);
        }

        [Test]
        public void TwoSameEffectInstancesKeepIndependentOrderedReceiptsWithoutExtraBeats()
        {
            var result = new TurnResultS2C();
            foreach (ulong item in new[] { ArtifactId + 1, ArtifactId })
                result.Events.Add(new BattleEventItem
                {
                    EventType = eBattleEventType.BattleEventArtifact, SourceId = ActorId, TargetId = ActorId,
                    ArtifactItemId = item, ArtifactEffectId = 8, Value = 800, Success = true,
                });
            var plan = TurnPlan.Build(result, 0);
            Assert.That(plan.Beats, Is.Empty);
            Assert.That(plan.TotalSeconds, Is.Zero);
            Assert.That(plan.ArtifactEvents.Count, Is.EqualTo(2));
            Assert.That(plan.ArtifactEvents[0].EventIndex, Is.Zero);
            Assert.That(plan.ArtifactEvents[0].Input.ArtifactItemId, Is.EqualTo(ArtifactId + 1));
            Assert.That(plan.ArtifactEvents[1].EventIndex, Is.EqualTo(1));
            Assert.That(plan.ArtifactEvents[1].Input.ArtifactItemId, Is.EqualTo(ArtifactId));
        }

        [Test]
        public void ArtifactReceiptDoesNotLoseFollowingManaOrShieldOutcomes()
        {
            var result = new TurnResultS2C();
            result.Events.Add(new BattleEventItem
            { EventType = eBattleEventType.BattleEventDefend, SourceId = ActorId, TargetId = ActorId });
            result.Events.Add(new BattleEventItem
            {
                EventType = eBattleEventType.BattleEventArtifact, SourceId = ActorId, TargetId = ActorId,
                ArtifactItemId = ArtifactId, ArtifactEffectId = 7, Value = 100, Success = true,
            });
            result.Events.Add(new BattleEventItem
            {
                EventType = eBattleEventType.BattleEventMana, SourceId = ActorId, TargetId = ActorId,
                Value = 100, ManaDelta = 100, TargetManaAfter = 500,
            });
            result.Events.Add(new BattleEventItem
            {
                EventType = eBattleEventType.BattleEventShield, SourceId = TargetId, TargetId = ActorId,
                ArtifactItemId = ArtifactId, ArtifactEffectId = 1, Absorbed = 40, ShieldAfter = 70,
            });
            var plan = TurnPlan.Build(result, 0);
            Assert.That(plan.ArtifactEvents.Count, Is.EqualTo(1));
            Assert.That(plan.Beats.Exists(beat => beat.HasActorManaAfter && beat.ActorManaAfter == 500
                || beat.Targets.Exists(target => target.HasManaAfter && target.ManaAfter == 500)), Is.True);
            Assert.That(plan.Beats.Exists(beat => beat.Targets.Exists(target => target.Effect == TargetEffect.Shield
                && target.ShieldAfter == 70 && target.Absorbed == 40)), Is.True);
        }

        private static AttributePanelInfo Panel(uint speed)
        {
            var panel = new AttributePanelInfo
            {
                Level = 80, EquipmentEffects = new EquipmentEffects
                { PhysicalAttackBps = 3200, SpeedBps = speed, PhysicalCritBps = 800, OutgoingHealBps = 2400 },
            };
            panel.EquipmentEffects.AilmentHitBps.Add(7, 1600);
            panel.EquipmentEffects.AilmentResistBps.Add(1, 1600);
            panel.EquipmentEffects.ManaReductionBps.Add(12, 1200);
            panel.EquipmentEffects.Artifacts.Add(new EquipmentArtifactSource
            { ItemId = ArtifactId, EffectId = 6, BonusBps = 2000, CapBps = 600, BattleLimit = 1, ActiveRounds = 2 });
            return panel;
        }

        [Test]
        public void AttributeReadAndPushPreserveAllServerEquipmentEffects()
        {
            var transport = new FakeBattleTransport();
            using var client = new AttributeClient(transport);
            AttributePanelInfo observed = null;
            client.OnPanel += panel => observed = panel;
            client.RequestPanel();
            var first = Panel(100);
            transport.CallsOf(MessageIds.GetAttributePanel)[0].Respond(new GetAttributePanelResponse { Panel = first });
            Assert.That(client.Panel, Is.EqualTo(first));
            Assert.That(observed.EquipmentEffects, Is.EqualTo(first.EquipmentEffects));

            var next = Panel(200);
            transport.PushNotify(MessageIds.NotifyAttributePanelChanged, new AttributePanelChangedS2C { Panel = next });
            Assert.That(client.Panel, Is.EqualTo(next));
            Assert.That(observed.EquipmentEffects, Is.EqualTo(next.EquipmentEffects));
            Assert.That(client.Panel.EquipmentEffects.SpeedBps, Is.EqualTo(200), "全量替换，客户端不累计两次面板。");
        }
    }
}
