using System.Linq;
using Google.Protobuf;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui.Gameplay;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class EquipSlotTargetTests
    {
        private FakeBattleTransport _net;
        private PlayerFeaturesClient _client;

        [SetUp]
        public void SetUp()
        {
            _net = new FakeBattleTransport { PlayerId = 9101, IsReady = true };
            _client = new PlayerFeaturesClient(_net);
        }

        [TearDown]
        public void TearDown() => _client.Dispose();

        private FakeBattleTransport.RecordedCall Last => _net.Calls.Last();

        private static BagItemInfo Item(ulong id, uint kind = 55) => new BagItemInfo
        {
            ItemId = id, ConfigId = 3501, Name = "同名装备", EquipKind = kind, Count = 1, MaxStack = 1,
        };

        private static BagInfo Bag(params BagItemInfo[] items)
        {
            var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = 0, Capacity = 40 } };
            foreach (var item in items) bag.Items.Add(item);
            return bag;
        }

        private static BagInfo Equipment()
        {
            var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = 2, Capacity = 11 } };
            for (uint slot = 3; slot <= 10; ++slot)
                bag.Layout.EquipSlots.Add(new EquipSlotInfo
                {
                    Slot = slot,
                    EquipKind = slot < 7 ? slot + 8 : slot < 9 ? 55u : 66u,
                    Name = "服务器槽位" + slot,
                });
            return bag;
        }

        private static void Wear(BagInfo equipment, uint slot, BagItemInfo item)
        {
            equipment.Items.Add(item);
            equipment.Layout.Slots.Add(new BagSlotInfo { Slot = slot, ItemId = item.ItemId, Width = 1, Height = 1 });
        }

        private void Load(BagInfo bag, BagInfo equipment)
        {
            _client.RequestBag();
            Last.Respond(new GetBagResponse { Bag = bag });
            _client.RequestEquipment();
            Last.Respond(new GetBagResponse { Bag = equipment });
        }

        [TestCase(0u)]
        [TestCase(8u)]
        [TestCase(10u)]
        [TestCase(uint.MaxValue)]
        public void ExplicitTargetRetainsPresenceAndExactInstanceThroughWire(uint targetSlot)
        {
            const ulong itemId = 9007199254740993UL;
            _client.EquipItem(itemId, targetSlot);

            Assert.That(Last.MessageId, Is.EqualTo(MessageIds.EquipItem));
            var sent = (EquipItemRequest)Last.Request;
            var decoded = EquipItemRequest.Parser.ParseFrom(sent.ToByteArray());
            Assert.That(decoded.ItemId, Is.EqualTo(itemId));
            Assert.That(decoded.HasTargetSlot, Is.True, "显式0号槽也必须在协议中保留存在性");
            Assert.That(decoded.TargetSlot, Is.EqualTo(targetSlot), "业务层不得改成第一个同类槽");
        }

        [Test]
        public void LegacyCallDoesNotSetTargetSlot()
        {
            _client.EquipItem(5001);
            var decoded = EquipItemRequest.Parser.ParseFrom(Last.Request.ToByteArray());
            Assert.That(decoded.HasTargetSlot, Is.False, "旧调用继续由服务器自动选槽");
        }

        [Test]
        public void ReplacingSecondSameKindSlotPreservesFirstInstanceAndServerSnapshots()
        {
            var before = Equipment();
            Wear(before, 7, Item(7001));
            Wear(before, 8, Item(7002));
            Load(Bag(Item(5001), Item(5002)), before);
            var originalBag = _client.CharacterBag;
            var originalEquipment = _client.Equipment;

            _client.EquipItem(5001, 8);
            var request = Last;
            _client.EquipItem(5002, 7);
            Assert.That(_net.CallsOf(MessageIds.EquipItem).Count, Is.EqualTo(1), "不同目标槽也共用在途闸");
            Assert.That(_client.CharacterBag, Is.SameAs(originalBag));
            Assert.That(_client.Equipment, Is.SameAs(originalEquipment), "请求不能预先改本地占用");

            var after = Equipment();
            Wear(after, 7, Item(7001));
            Wear(after, 8, Item(5001));
            request.Respond(new EquipItemResponse { Bag = Bag(Item(5002), Item(7002)), Equipment = after });

            var slots = EquipBagModel.Slots(_client.Equipment);
            Assert.That(slots.Count, Is.EqualTo(8));
            Assert.That(slots.Single(slot => slot.Slot == 7).Item.ItemId, Is.EqualTo(7001));
            Assert.That(slots.Single(slot => slot.Slot == 8).Item.ItemId, Is.EqualTo(5001));
            Assert.That(_client.CharacterBag.Items.Select(item => item.ItemId), Is.EqualTo(new ulong[] { 5002, 7002 }));
            Assert.That(_client.BusyEquip, Is.False);
        }

        [Test]
        public void ExplicitTargetRejectionKeepsBothSnapshotsAndDoesNotRetryAnotherSlot()
        {
            Load(Bag(Item(5001)), Equipment());
            var bag = _client.CharacterBag;
            var equipment = _client.Equipment;
            _client.EquipItem(5001, 8);
            Last.Respond(new EquipItemResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipLevelNotEnough },
            });

            Assert.That(_client.CharacterBag, Is.SameAs(bag));
            Assert.That(_client.Equipment, Is.SameAs(equipment));
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_client.EquipmentError, Is.Not.Empty);
            Assert.That(_net.CallsOf(MessageIds.EquipItem).Count, Is.EqualTo(1));
        }

        [Test]
        public void InvalidInstanceDoesNotSendEvenForExplicitZeroSlot()
        {
            _client.EquipItem(0, 0);
            Assert.That(_net.Calls, Is.Empty);
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_client.EquipmentError, Is.Not.Empty);
        }

        [TestCase(7u, 55u)]
        [TestCase(8u, 55u)]
        [TestCase(9u, 66u)]
        [TestCase(10u, 66u)]
        public void CandidatesPreserveSameNameAndTemplateInstancesInEitherMatchingSlot(uint slot, uint kind)
        {
            var first = Item(5001, kind);
            var second = Item(5002, kind);
            second.EnhanceLevel = 4;
            var bag = Bag(first, second, Item(5003, 99));
            var equipment = Equipment();
            Wear(equipment, slot, Item(7001, kind));
            byte[] bagBefore = bag.ToByteArray();
            byte[] equipmentBefore = equipment.ToByteArray();

            var candidates = EquipBagModel.EquipCandidates(bag, equipment, slot);

            Assert.That(candidates.Select(item => item.ItemId), Is.EqualTo(new ulong[] { 5001, 5002 }));
            Assert.That(candidates[0], Is.SameAs(first));
            Assert.That(candidates[1].EnhanceLevel, Is.EqualTo(4));
            Assert.That(bag.ToByteArray(), Is.EqualTo(bagBefore));
            Assert.That(equipment.ToByteArray(), Is.EqualTo(equipmentBefore));
        }

        [Test]
        public void CandidatesExcludeWornDuplicateAndEmptyInstancesWithoutDeduplicatingTemplate()
        {
            var equipment = Equipment();
            Wear(equipment, 7, Item(7001));
            var empty = Item(5003);
            empty.Count = 0;
            var bag = Bag(Item(7001), Item(5001), Item(5001), Item(5002), Item(0), empty);

            Assert.That(EquipBagModel.EquipCandidates(bag, equipment, 8).Select(item => item.ItemId),
                Is.EqualTo(new ulong[] { 5001, 5002 }));
        }

        [Test]
        public void CandidatesRequireCharacterBagAndKnownInCapacitySlot()
        {
            var bag = Bag(Item(5001));
            var equipment = Equipment();
            Assert.That(EquipBagModel.EquipCandidates(null, equipment, 7), Is.Empty);
            Assert.That(EquipBagModel.EquipCandidates(bag, null, 7), Is.Empty);
            Assert.That(EquipBagModel.EquipCandidates(bag, equipment, 1), Is.Empty);
            bag.Layout.BagType = 1;
            Assert.That(EquipBagModel.EquipCandidates(bag, equipment, 7), Is.Empty, "仓库不能冒充人物背包");
            bag.Layout.BagType = 0;
            equipment.Layout.Capacity = 8;
            Assert.That(EquipBagModel.EquipCandidates(bag, equipment, 8), Is.Empty);
            equipment.Layout.BagType = 0;
            Assert.That(EquipBagModel.FindEquipSlot(equipment, 7), Is.Null);
        }

        [Test]
        public void OccupiedUnknownSlotRemainsVisibleButCannotInferCandidateType()
        {
            var equipment = Equipment();
            Wear(equipment, 1, Item(7001));
            Assert.That(EquipBagModel.Slots(equipment).Single(slot => slot.Slot == 1).Item.ItemId, Is.EqualTo(7001));
            Assert.That(EquipBagModel.EquipCandidates(Bag(Item(5001)), equipment, 1), Is.Empty);
        }

        [Test]
        public void DefinedZeroSlotCanHaveCandidates()
        {
            var equipment = Equipment();
            equipment.Layout.EquipSlots.Add(new EquipSlotInfo { Slot = 0, EquipKind = 55, Name = "自定义槽" });
            Assert.That(EquipBagModel.EquipCandidates(Bag(Item(5001)), equipment, 0).Single().ItemId, Is.EqualTo(5001));
        }
    }
}
