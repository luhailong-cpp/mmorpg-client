using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui.Gameplay;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// V1 强化仍使用既有协议；客户端只消费服务端资格、费用及逐项预览，
    /// 不从部位推导 profile，不在本地复制强化公式或预扣材料。
    /// </summary>
    public sealed class EquipV1EnhanceClientTests
    {
        private const ulong EquipmentId = 9007199254740993UL;
        private const ulong StoneStackId = 6001;
        // 仅用于协议夹具，不能据此分配正式模板或材料编号。
        private const uint EquipmentConfigId = 99001;
        private const uint StoneConfigId = 99002;

        private FakeBattleTransport _net;
        private PlayerFeaturesClient _client;
        private List<EquipEnhanceOutcome> _outcomes;

        [SetUp]
        public void SetUp()
        {
            _net = new FakeBattleTransport { PlayerId = 9101, IsReady = true };
            _client = new PlayerFeaturesClient(_net) { NowMs = () => 100000 };
            _outcomes = new List<EquipEnhanceOutcome>();
            _client.OnEnhanceResult += _outcomes.Add;
        }

        [TearDown]
        public void TearDown() => _client.Dispose();

        private FakeBattleTransport.RecordedCall Last => _net.Calls.Last();

        private static BagItemInfo Equipment(uint level = 0, uint kind = 11, uint maxLevel = 10)
        {
            var item = new BagItemInfo
            {
                ItemId = EquipmentId, ConfigId = EquipmentConfigId, Count = 1, MaxStack = 1,
                EquipKind = kind, EquipLevel = 80, Name = "V1装备夹具",
                EnhanceLevel = level, EnhanceMaxLevel = maxLevel,
            };
            if (maxLevel != 0 && level < maxLevel)
                item.EnhanceNext = new EquipEnhancePreview
                {
                    NextLevel = level + 1, SuccessRate = 10000, CostGold = 0,
                    MaterialConfigId = StoneConfigId, MaterialName = "强化石", MaterialCount = 2,
                    BonusPermyriad = 200, FailLevelLoss = 0,
                };
            return item;
        }

        private static BagInfo CharacterBag(uint stones, params BagItemInfo[] equipment)
        {
            // V1 不收银两，因此故意不提供货币快照，验证客户端不会另加货币门槛。
            var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = 0, Capacity = 40 } };
            bag.Items.AddRange(equipment);
            if (stones != 0)
                bag.Items.Add(new BagItemInfo
                {
                    ItemId = StoneStackId, ConfigId = StoneConfigId, Count = stones, MaxStack = 999,
                });
            for (int index = 0; index < bag.Items.Count; ++index)
                bag.Layout.Slots.Add(new BagSlotInfo
                {
                    Slot = (uint)index, ItemId = bag.Items[index].ItemId, Width = 1, Height = 1,
                });
            return bag;
        }

        private static BagInfo EquipmentBag(BagItemInfo item = null)
        {
            var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = 2, Capacity = 11 } };
            bag.Layout.EquipSlots.Add(new EquipSlotInfo { Slot = 3, EquipKind = 11, Name = "武器" });
            if (item != null)
            {
                bag.Items.Add(item);
                bag.Layout.Slots.Add(new BagSlotInfo { Slot = 3, ItemId = item.ItemId, Width = 1, Height = 1 });
            }
            return bag;
        }

        private void Load(BagInfo bag, BagInfo equipment)
        {
            _client.RequestBag();
            Assert.That(Last.Request, Is.TypeOf<GetBagRequest>());
            Assert.That(((GetBagRequest)Last.Request).BagType, Is.EqualTo(0));
            Last.Respond(new GetBagResponse { Bag = bag });
            _client.RequestEquipment();
            Assert.That(Last.Request, Is.TypeOf<GetBagRequest>());
            Assert.That(((GetBagRequest)Last.Request).BagType, Is.EqualTo(2));
            Last.Respond(new GetBagResponse { Bag = equipment });
        }

        private FakeBattleTransport.RecordedCall Enhance()
        {
            _client.EnhanceEquip(EquipmentId);
            Assert.That(Last.MessageId, Is.EqualTo(MessageIds.EnhanceEquip));
            Assert.That(Last.Request, Is.TypeOf<EnhanceEquipRequest>());
            return Last;
        }

        [Test]
        public void LevelTenWithoutNextPreviewDoesNotSendOrConsume()
        {
            Load(CharacterBag(75, Equipment(level: 10)), EquipmentBag());
            byte[] before = _client.CharacterBag.ToByteArray();

            _client.EnhanceEquip(EquipmentId);

            Assert.That(_net.CallsOf(MessageIds.EnhanceEquip), Is.Empty);
            Assert.That(EquipBagModel.CheckEnhance(_client.CharacterBag.Items[0], _client.CharacterBag),
                Is.EqualTo(EquipEnhanceCheck.MaxLevel));
            Assert.That(EquipDisplay.EnhancePreviewLines(_client.CharacterBag.Items[0]), Is.Empty);
            Assert.That(_client.CharacterBag.ToByteArray(), Is.EqualTo(before));
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_client.EquipmentError, Does.Contain("最高等级"));
        }

        [TestCase(15u)]
        [TestCase(16u)]
        public void JewelryAndArtifactUseServerDisabledEnhancement(uint kind)
        {
            Load(CharacterBag(75, Equipment(kind: kind, maxLevel: 0)), EquipmentBag());
            byte[] before = _client.CharacterBag.ToByteArray();

            _client.EnhanceEquip(EquipmentId);

            Assert.That(_net.CallsOf(MessageIds.EnhanceEquip), Is.Empty);
            Assert.That(EquipBagModel.CheckEnhance(_client.CharacterBag.Items[0], _client.CharacterBag),
                Is.EqualTo(EquipEnhanceCheck.Unavailable));
            Assert.That(_client.CharacterBag.ToByteArray(), Is.EqualTo(before));
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_client.EquipmentError, Does.Contain("无法强化"));
        }

        [Test]
        public void FirstLevelUsesServerPreviewAndSendsExpectedLevelWithoutLocalDeduction()
        {
            var item = Equipment();
            Load(CharacterBag(2, item), EquipmentBag());
            var loaded = _client.CharacterBag.Items.Single(entry => entry.ItemId == EquipmentId);
            byte[] bagBefore = _client.CharacterBag.ToByteArray();
            byte[] equipmentBefore = _client.Equipment.ToByteArray();

            Assert.That(loaded.EnhanceNext.ToByteArray(), Is.EqualTo(item.EnhanceNext.ToByteArray()));
            Assert.That(loaded.EnhanceNext.MaterialCount, Is.EqualTo(2));
            Assert.That(loaded.EnhanceNext.SuccessRate, Is.EqualTo(10000));
            Assert.That(loaded.EnhanceNext.CostGold, Is.Zero);
            Assert.That(EquipBagModel.CheckEnhance(loaded, _client.CharacterBag), Is.EqualTo(EquipEnhanceCheck.None));
            var call = Enhance();
            var request = EnhanceEquipRequest.Parser.ParseFrom(call.Request.ToByteArray());

            Assert.That(request.ItemId, Is.EqualTo(EquipmentId));
            Assert.That(request.ExpectedLevel, Is.Zero);
            Assert.That(request.ClientRequestId, Is.Not.Zero);
            Assert.That(_client.BusyEquip, Is.True);
            Assert.That(_client.CharacterBag.ToByteArray(), Is.EqualTo(bagBefore));
            Assert.That(_client.Equipment.ToByteArray(), Is.EqualTo(equipmentBefore));
            Assert.That(_outcomes, Is.Empty);
        }

        [Test]
        public void ShoesUseIndependentServerRowsForDefenseAndUnchangedSpeed()
        {
            var item = Equipment(kind: 14);
            // 故意倒序下发；位置由显式 seq 决定，数值只取服务端各行。
            item.BaseAttrs.Add(new EquipAttrLineInfo
            {
                AttrId = 12, Name = "速度", Tier = 0, Seq = 1, Value = 328, EnhanceNextValue = 328,
            });
            item.BaseAttrs.Add(new EquipAttrLineInfo
            {
                AttrId = 11, Name = "防御", Tier = 0, Seq = 0, Value = 486, EnhanceNextValue = 495,
            });
            Load(CharacterBag(2, item), EquipmentBag());
            var loaded = _client.CharacterBag.Items.Single(entry => entry.ItemId == EquipmentId);
            byte[] before = loaded.ToByteArray();

            Assert.That(EquipDisplay.EnhancePreviewLines(loaded),
                Is.EqualTo(new[] { "防御：486 → 495", "速度：328 → 328" }));
            Assert.That(EquipDisplay.FormatLine(loaded.BaseAttrs.Single(line => line.Name == "速度")),
                Is.EqualTo("速度：328"));
            Assert.That(loaded.ToByteArray(), Is.EqualTo(before));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SuccessReplacesAuthoritativeSnapshotsForBagOrWornEquipment(bool worn)
        {
            var item = Equipment();
            Load(worn ? CharacterBag(5) : CharacterBag(5, item), worn ? EquipmentBag(item) : EquipmentBag());
            var call = Enhance();
            var upgraded = Equipment(level: 1);
            upgraded.EnhanceBonusPermyriad = 200;
            upgraded.EnhanceNext.MaterialCount = 3;
            upgraded.EnhanceNext.BonusPermyriad = 400;
            var bagAfter = worn ? CharacterBag(3) : CharacterBag(3, upgraded);
            var equipmentAfter = worn ? EquipmentBag(upgraded) : EquipmentBag();

            call.Respond(new EnhanceEquipResponse
            {
                Success = true, EnhanceLevel = 1, Bag = bagAfter, Equipment = equipmentAfter,
            });

            Assert.That(_client.CharacterBag.ToByteArray(), Is.EqualTo(bagAfter.ToByteArray()));
            Assert.That(_client.Equipment.ToByteArray(), Is.EqualTo(equipmentAfter.ToByteArray()));
            Assert.That(EquipBagModel.CountOf(_client.CharacterBag, StoneConfigId), Is.EqualTo(3));
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_client.EquipmentError, Is.Empty);
            Assert.That(_outcomes.Count, Is.EqualTo(1));
            Assert.That(_outcomes[0].ItemId, Is.EqualTo(EquipmentId));
            Assert.That(_outcomes[0].Success, Is.True);
            Assert.That(_outcomes[0].Level, Is.EqualTo(1));
            Assert.That(_client.LastEnhanceOutcome.Value.Level, Is.EqualTo(1));
        }

        [TestCase((uint)equip_error.KEquipInBattle)]
        [TestCase((uint)equip_error.KEquipFrozen)]
        public void ServerRejectionKeepsInventoryAndDoesNotPublishSuccess(uint tip)
        {
            Load(CharacterBag(2, Equipment()), EquipmentBag());
            byte[] bagBefore = _client.CharacterBag.ToByteArray();
            byte[] equipmentBefore = _client.Equipment.ToByteArray();

            Enhance().Respond(new EnhanceEquipResponse { ErrorMessage = new TipInfoMessage { Id = tip } });

            Assert.That(_client.CharacterBag.ToByteArray(), Is.EqualTo(bagBefore));
            Assert.That(_client.Equipment.ToByteArray(), Is.EqualTo(equipmentBefore));
            Assert.That(_net.CallsOf(MessageIds.EnhanceEquip).Count, Is.EqualTo(1));
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_client.EquipmentError, Is.Not.Empty);
            Assert.That(_client.LastEnhanceOutcome, Is.Null);
            Assert.That(_outcomes, Is.Empty);
        }
    }
}
