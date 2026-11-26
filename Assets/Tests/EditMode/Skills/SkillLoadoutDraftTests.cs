using MmorpgClient.UI.Ugui.Skills;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Skills
{
    public sealed class SkillLoadoutDraftTests
    {
        [Test]
        public void FullDestinationKeepsAnEquippedConflictInAnotherCategory()
        {
            var snapshot = new SkillBookSnapshot { Loadout = new SkillLoadoutView() };
            var scheme = new SkillLoadoutSchemeView { SchemeId = 1 };
            scheme.OpeningSkillIds.Add(10);
            scheme.PassiveSkillIds.Add(20);
            snapshot.Loadout.Schemes.Add(scheme);
            snapshot.Entries.Add(Entry(10, 4, 0));
            snapshot.Entries.Add(Entry(20, 2, 8));
            var next = Entry(30, 4, 8);
            snapshot.Entries.Add(next);
            var draft = new SkillLoadoutDraft();
            draft.Reset(snapshot.Loadout, 1);

            Assert.That(draft.GetConflict(snapshot, next), Is.EqualTo(20u));
            Assert.That(draft.Toggle(snapshot, next, out uint replaced), Is.EqualTo(SkillLoadoutDraft.Change.SlotsFull));
            Assert.That(replaced, Is.Zero);
            Assert.That(draft.Opening, Is.EqualTo(new uint[] { 10 }));
            Assert.That(draft.Passive, Is.EqualTo(new uint[] { 20 }));
            Assert.That(draft.IsDirty, Is.False);
        }

        [Test]
        public void SameCategoryConflictUsesTheExistingSlotEvenAtCapacity()
        {
            var snapshot = new SkillBookSnapshot { Loadout = new SkillLoadoutView() };
            var scheme = new SkillLoadoutSchemeView { SchemeId = 1 };
            scheme.PursuitSkillIds.Add(10);
            snapshot.Loadout.Schemes.Add(scheme);
            snapshot.Entries.Add(Entry(10, 3, 8));
            var next = Entry(20, 3, 8);
            snapshot.Entries.Add(next);
            var draft = new SkillLoadoutDraft();
            draft.Reset(snapshot.Loadout, 1);

            Assert.That(draft.Toggle(snapshot, next, out uint replaced), Is.EqualTo(SkillLoadoutDraft.Change.Replaced));
            Assert.That(replaced, Is.EqualTo(10u));
            Assert.That(draft.Pursuit, Is.EqualTo(new uint[] { 20 }));
            Assert.That(draft.IsDirty, Is.True);
        }

        private static SkillEntryState Entry(uint id, uint kind, uint group) =>
            new SkillEntryState { Id = id, Rank = 1, Definition = new SkillDefinition { id = id, abilityKind = kind, exclusiveGroup = group } };
    }
}
