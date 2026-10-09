using NUnit.Framework;
using MmorpgClient.UI.Ugui.Gameplay;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class EquipWishSelectionTests
    {
        [Test]
        public void MissingOptionsNeverEnablesConfirmation()
        {
            var model = new EquipWishSelection();
            model.Reset(null, new uint[] { 1 });
            Assert.That(model.CanConfirm, Is.False);
            Assert.That(model.Count, Is.Zero);
            Assert.That(model.Toggle(1), Is.False);
        }

        [Test]
        public void OnlySuppliedOptionsCanEnterTheDraftAndDuplicatesDoNotCreateExtraRows()
        {
            var model = new EquipWishSelection();
            model.Reset(new[] { new EquipWishOption(2, "防御"), null, new EquipWishOption(2, "重复"),
                new EquipWishOption(3, " "), new EquipWishOption(7, "气血") }, new uint[] { 2, 2, 3, 9 });
            Assert.That(model.Options.Count, Is.EqualTo(2));
            Assert.That(model.Options[0].Label, Is.EqualTo("防御"));
            CollectionAssert.AreEqual(new uint[] { 2 }, model.Snapshot());
            Assert.That(model.Toggle(9), Is.False);
        }

        [Test]
        public void ToggleAndClearOperateOnDraftWithoutChangingInputs()
        {
            var initial = new uint[] { 1 };
            var model = new EquipWishSelection();
            model.Reset(new[] { new EquipWishOption(1, "力量"), new EquipWishOption(2, "敏捷") }, initial);
            Assert.That(model.Toggle(1), Is.True);
            Assert.That(model.IsSelected(1), Is.False);
            model.Toggle(2); CollectionAssert.AreEqual(new uint[] { 2 }, model.Snapshot());
            CollectionAssert.AreEqual(new uint[] { 1 }, initial);
            model.Clear(); Assert.That(model.Count, Is.Zero);
            Assert.That(model.CanConfirm, Is.True, "An empty selection is a valid choice when the pool is supplied.");
        }

        [Test]
        public void ConfirmedSnapshotUsesOptionOrderAndIsIndependentFromFurtherEditing()
        {
            var model = new EquipWishSelection();
            model.Reset(new[] { new EquipWishOption(7, "气血"), new EquipWishOption(2, "防御") });
            model.Toggle(2); model.Toggle(7);
            var confirmed = model.Snapshot();
            CollectionAssert.AreEqual(new uint[] { 7, 2 }, confirmed);
            model.Clear(); CollectionAssert.AreEqual(new uint[] { 7, 2 }, confirmed);
            confirmed[0] = 99; Assert.That(model.IsSelected(99), Is.False);
        }

        [Test]
        public void ReopeningForAnotherPoolDoesNotLeakThePreviousEquipmentSelection()
        {
            var model = new EquipWishSelection();
            model.Reset(new[] { new EquipWishOption(1, "力量") }, new uint[] { 1 });
            model.Reset(new[] { new EquipWishOption(8, "防御") }, new uint[] { 1 });
            Assert.That(model.Count, Is.Zero);
            Assert.That(model.Toggle(1), Is.False);
            Assert.That(model.Toggle(8), Is.True);
        }
    }
}
