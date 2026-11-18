using System.Linq;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Skills;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class CharacterSupportModelTests
    {
        [Test]
        public void MissingPanelAndAuthoritativeZeroRemainDifferent()
        {
            Assert.That(CharacterSupportModel.Attributes(null), Is.All.EqualTo("—"));
            var panel = new AttributePanelInfo { Derived = new DerivedAttributeInfo { Health = 0, MaxHealth = 840, PhysicalAttack = 0 } };
            var values = CharacterSupportModel.Attributes(panel);
            Assert.That(values[0], Is.EqualTo("0 / 840"));
            Assert.That(values[2], Is.EqualTo("0"));
        }
        [Test]
        public void EquipmentContributionNeverPretendsToBeCompleteTotal()
        {
            var panel = new AttributePanelInfo { EquipmentEffects = new EquipmentEffects { OutgoingHealBps = 1275, ShieldCapacityBps = 800 } };
            Assert.That(CharacterSupportModel.EquipmentBonus(panel, false), Is.EqualTo("12.75%"));
            Assert.That(CharacterSupportModel.EquipmentBonus(panel, true), Is.EqualTo("8%"));
            Assert.That(CharacterSupportModel.TotalBonus(panel, false), Is.EqualTo("—"));
            Assert.That(CharacterSupportModel.TotalBonus(panel, true), Is.EqualTo("—"));
            Assert.That(CharacterSupportModel.EquipmentBonus(null, false), Is.EqualTo("—"));
            Assert.That(CharacterSupportModel.EquipmentBonus(new AttributePanelInfo { EquipmentEffects = new EquipmentEffects() }, false), Is.EqualTo("0%"));
        }
        [Test]
        public void SourcesUseServerItemNamesAndAmountsWithoutSummingUncappedContributions()
        {
            var bag = new BagInfo();
            var item = new BagItemInfo { ItemId = 97, Name = "实际装备" };
            item.FixedAffixSlots.Add(new EquipFixedAffixSlotInfo { Slot = 1, Selected = new EquipFixedAffixInfo { Effect = 5, Unit = 1, Name = "施疗", ValueBps = 900 } });
            item.FixedAffixSlots.Add(new EquipFixedAffixSlotInfo { Slot = 2, Selected = new EquipFixedAffixInfo { Effect = 6, Unit = 1, Name = "护盾", ValueBps = 500 } });
            bag.Items.Add(item);
            var rows = CharacterSupportModel.Sources(new AttributePanelInfo { EquipmentEffects = new EquipmentEffects { OutgoingHealBps = 800 } }, bag, false);
            Assert.That(rows[0].Name, Is.EqualTo("实际装备 · 施疗"));
            Assert.That(rows[0].Value, Is.EqualTo("9%"));
            Assert.That(rows[1].Value, Is.EqualTo("8%"));
            Assert.That(rows.Where(r => r.Name == "被动" || r.Name == "临时状态").All(r => r.Value == "尚未同步"), Is.True);
        }
        [Test]
        public void MissingTargetAndPersistentDurationAreUnknownRatherThanZeroTargetsOrZeroRounds()
        {
            var state = new SkillBookViewState { Context = SkillBookContext.Character(), IsConnected = true, Snapshot = new SkillBookSnapshot() };
            state.Snapshot.Entries.Add(new SkillEntryState { Id = CharacterSupportModel.ShieldSkillId,
                Definition = new SkillDefinition { id = CharacterSupportModel.ShieldSkillId, targetTeam = "ally", manaPercent = .08, cooldownRounds = 3 } });
            var values = CharacterSupportModel.SkillValues(state, CharacterSupportModel.ShieldSkillId);
            Assert.That(values[1], Is.EqualTo("—"));
            Assert.That(values[3], Is.EqualTo("— / 8% 最大法力 / 3 回合"));
        }

        [Test]
        public void SkillDetailsRejectPetOfflineAndReadOnlySnapshots()
        {
            var state = new SkillBookViewState { Context = SkillBookContext.Character(), IsConnected = true, Snapshot = new SkillBookSnapshot() };
            state.Snapshot.Entries.Add(new SkillEntryState { Id = CharacterSupportModel.HealingSkillId, Rank = 3,
                Definition = new SkillDefinition { id = CharacterSupportModel.HealingSkillId, name = "权威治疗", maxTargets = 4, manaPercent = .06, cooldownRounds = 2, targetTeam = "ally" } });
            Assert.That(CharacterSupportModel.SkillValues(state, CharacterSupportModel.HealingSkillId), Is.EqualTo(new[] { "恢复目标气血", "友方最多 4 个", "3 重", "6% 最大法力 / 2 回合" }));
            state.Context = SkillBookContext.Pet(99, "宝宝");
            Assert.That(CharacterSupportModel.FindSkill(state, CharacterSupportModel.HealingSkillId), Is.Null);
            state.Context = SkillBookContext.Character(); state.IsConnected = false;
            Assert.That(CharacterSupportModel.FindSkill(state, CharacterSupportModel.HealingSkillId), Is.Null);
            state.IsConnected = true; state.IsReadOnly = true;
            Assert.That(CharacterSupportModel.FindSkill(state, CharacterSupportModel.HealingSkillId), Is.Null);
        }
    }
}
