using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 装备数据层用例(服务端 docs/design/equipment-attributes.md §3.2 / §6):
    /// 背包与装备栏分开缓存、穿上 / 卸下单飞且回包整体覆盖两份快照、在途期间的刷新与失败后的重新同步、
    /// equip_error 文案,以及 <see cref="EquipDisplay"/> 的排版与排序。
    /// </summary>
    public sealed class EquipClientTests
    {
        private FakeBattleTransport _net;
        private PlayerFeaturesClient _client;
        private List<string> _errors;
        private int _changes;

        [SetUp]
        public void SetUp()
        {
            _net = new FakeBattleTransport { PlayerId = 9101, IsReady = true };
            _client = new PlayerFeaturesClient(_net);
            _errors = new List<string>();
            _changes = 0;
            _client.OnError += _errors.Add;
            _client.OnChanged += () => ++_changes;
        }

        [TearDown]
        public void TearDown() => _client.Dispose();

        private FakeBattleTransport.RecordedCall Last => _net.Calls[_net.Calls.Count - 1];

        /// <summary>从第 from 个出站调用起,发出的背包读取各是哪一类(bag_type)。</summary>
        private uint[] BagReadsFrom(int from)
            => _net.Calls.Skip(from).Where(call => call.MessageId == SceneBagClientPlayerGetBagHandler.MessageId)
                .Select(call => ((GetBagRequest)call.Request).BagType).ToArray();

        /// <summary>人物背包 / 仓库:每件一格,从 0 号格起。</summary>
        private static BagInfo Bag(uint type, params ulong[] itemIds)
        {
            var bag = new BagInfo
            {
                Layout = new BagLayoutInfo { BagType = type, Capacity = 40, CanSort = type < 2 },
                Currency = new CurrencyComp(),
            };
            uint slot = 0;
            foreach (ulong id in itemIds)
            {
                bag.Items.Add(new BagItemInfo { ItemId = id, ConfigId = 1101, Count = 1, MaxStack = 1, EquipKind = 11 });
                bag.Layout.Slots.Add(new BagSlotInfo { Slot = slot++, ItemId = id, Width = 1, Height = 1 });
            }
            return bag;
        }

        /// <summary>装备栏:四个槽位定义(3 武器 / 4 帽子 / 5 衣服 / 6 鞋子,含空槽),已穿的从 3 号槽起占。</summary>
        private static BagInfo EquipmentBag(params ulong[] equippedIds)
        {
            var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = 2, Capacity = 7 } };
            string[] names = { "武器", "帽子", "衣服", "鞋子" };
            for (uint i = 0; i < names.Length; ++i)
                bag.Layout.EquipSlots.Add(new EquipSlotInfo { Slot = 3 + i, EquipKind = 11 + i, Name = names[i] });
            uint slot = 3;
            foreach (ulong id in equippedIds)
            {
                bag.Items.Add(new BagItemInfo { ItemId = id, ConfigId = 1101, Count = 1, MaxStack = 1, EquipKind = 11 });
                bag.Layout.Slots.Add(new BagSlotInfo { Slot = slot++, ItemId = id, Width = 1, Height = 1 });
            }
            return bag;
        }

        /// <summary>人物背包里有 5001 / 5002,身上穿着 7001。</summary>
        private void LoadBoth()
        {
            _client.RequestBag();
            Last.Respond(new GetBagResponse { Bag = Bag(0, 5001, 5002) });
            _client.RequestEquipment();
            Last.Respond(new GetBagResponse { Bag = EquipmentBag(7001) });
        }

        private static ulong[] Ids(BagInfo bag) => bag.Items.Select(item => item.ItemId).ToArray();

        // ── 分开缓存 ────────────────────────────────────────

        [Test]
        public void RequestEquipmentReadsBagTypeTwoWithoutTouchingCharacterBagOrItsInFlightRequest()
        {
            _client.RequestBag();
            _net.Calls[0].Respond(new GetBagResponse { Bag = Bag(0, 5001) });
            var bag = _client.Bag;
            _client.RequestBag(); // 人物背包的刷新在途
            _client.RequestEquipment();

            Assert.That(_net.Calls[2].MessageId, Is.EqualTo(SceneBagClientPlayerGetBagHandler.MessageId));
            Assert.That(((GetBagRequest)_net.Calls[2].Request).BagType, Is.EqualTo(PlayerFeaturesClient.EquipmentBagType));
            Assert.That(_client.Bag, Is.SameAs(bag), "请求装备栏不得清掉人物背包快照");
            Assert.That(_client.RequestedBagType, Is.Zero);
            Assert.That(_client.BagLoading && _client.EquipmentLoading, Is.True);
            Assert.That(_client.HasEquipment, Is.False);

            _net.Calls[2].Respond(new GetBagResponse { Bag = EquipmentBag(7001) });
            Assert.That(_client.EquipmentLoading, Is.False);
            Assert.That(_client.Equipment.Layout.BagType, Is.EqualTo(2));
            Assert.That(_client.Equipment.Layout.EquipSlots.Count, Is.EqualTo(4), "空槽的定义也要留着");
            Assert.That(Ids(_client.Equipment), Is.EqualTo(new ulong[] { 7001 }));
            Assert.That(_client.Bag, Is.SameAs(bag));
            Assert.That(_client.BagLoading, Is.True, "装备栏回包不得顶掉人物背包的在途请求");

            _net.Calls[1].Respond(new GetBagResponse { Bag = Bag(0, 5001, 5002) });
            Assert.That(Ids(_client.Bag), Is.EqualTo(new ulong[] { 5001, 5002 }));
            Assert.That(_client.BagLoading, Is.False);
            Assert.That(Ids(_client.Equipment), Is.EqualTo(new ulong[] { 7001 }));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void RequestBagKeepsEquipmentSnapshotAndItsInFlightRequest()
        {
            _client.RequestEquipment();
            _net.Calls[0].Respond(new GetBagResponse { Bag = EquipmentBag(7001) });
            var equipment = _client.Equipment;
            _client.RequestEquipment(); // 装备栏的刷新在途
            _client.RequestBag();

            Assert.That(_client.Equipment, Is.SameAs(equipment), "请求人物背包不得清掉装备栏快照");
            Assert.That(_client.EquipmentLoading, Is.True);
            Assert.That(_client.HasBag, Is.False);

            _net.Calls[2].Respond(new GetBagResponse { Bag = Bag(0, 5001) });
            Assert.That(Ids(_client.Bag), Is.EqualTo(new ulong[] { 5001 }));
            Assert.That(_client.Equipment, Is.SameAs(equipment));
            Assert.That(_client.EquipmentLoading, Is.True, "背包回包不得顶掉装备栏的在途请求");

            _net.Calls[1].Respond(new GetBagResponse { Bag = EquipmentBag(7002) });
            Assert.That(Ids(_client.Equipment), Is.EqualTo(new ulong[] { 7002 }));
            Assert.That(_client.EquipmentLoading, Is.False);
            Assert.That(Ids(_client.Bag), Is.EqualTo(new ulong[] { 5001 }));
        }

        [Test]
        public void SwitchingBagViewKeepsEachTypeCached()
        {
            _client.RequestBag();
            Last.Respond(new GetBagResponse { Bag = Bag(0, 5001) });
            var character = _client.Bag;

            _client.RequestBag(1);
            Assert.That(_client.HasBag, Is.False, "没拉到过的那一类不得显示别的类型的快照");
            Last.Respond(new GetBagResponse { Bag = Bag(1, 8001) });
            var warehouse = _client.Bag;

            _client.RequestBag();
            Assert.That(_client.Bag, Is.SameAs(character), "切回已拉过的类型,先显示它自己上一次的快照");
            Assert.That(_client.BagLoading, Is.True);
            _client.RequestBag(1);
            Assert.That(_client.Bag, Is.SameAs(warehouse));
        }

        [Test]
        public void EquipmentReadFailuresKeepLastGoodSnapshotAndStayOutOfBagError()
        {
            LoadBoth();
            var equipment = _client.Equipment;

            _client.RequestEquipment();
            Last.Respond(new GetBagResponse { ErrorMessage = new TipInfoMessage { Id = 5 }, Bag = EquipmentBag(9999) });
            Assert.That(_client.Equipment, Is.SameAs(equipment));
            Assert.That(_client.EquipmentError, Does.Contain("5"));
            Assert.That(_client.EquipmentLoading, Is.False);

            _client.RequestEquipment();
            Assert.That(_client.EquipmentError, Is.Empty, "重试时清掉上一次的错误");
            Last.Respond(new GetBagResponse { Bag = Bag(0, 9999) }); // 类型不符
            Assert.That(_client.Equipment, Is.SameAs(equipment));
            Assert.That(_client.EquipmentError, Is.Not.Empty);

            // 这条原因会显示在背包页状态行:传输层的内部英文串不上屏,信封上的码还原成编号。
            _client.RequestEquipment();
            Last.FailWith("rpc timeout");
            Assert.That(_client.Equipment, Is.SameAs(equipment));
            Assert.That(_client.EquipmentError, Is.EqualTo("读取装备栏失败:网络异常,请稍后重试"));
            Assert.That(_client.EquipmentLoading, Is.False);
            _client.RequestEquipment();
            Last.FailWith("server tip=1008");
            Assert.That(_client.EquipmentError, Is.EqualTo("读取装备栏失败（错误码 1008）"));
            Assert.That(_client.Equipment, Is.SameAs(equipment));
            Assert.That(_client.BagError, Is.Empty, "装备栏的错误不串到背包");
            Assert.That(_errors.Count, Is.EqualTo(4));
        }

        // ── 穿上 / 卸下 ─────────────────────────────────────

        [Test]
        public void EquipItemSuccessReplacesBothSnapshotsWholesaleAndNotifiesOnce()
        {
            LoadBoth();
            var bagBefore = _client.Bag;
            var equipmentBefore = _client.Equipment;

            _client.EquipItem(5001);
            var calls = _net.CallsOf(MessageIds.EquipItem);
            Assert.That(calls.Count, Is.EqualTo(1));
            Assert.That(((EquipItemRequest)calls[0].Request).ItemId, Is.EqualTo(5001));
            Assert.That(_client.BusyEquip, Is.True);
            Assert.That(_client.Bag, Is.SameAs(bagBefore), "点击不得乐观修改快照");
            Assert.That(_client.Equipment, Is.SameAs(equipmentBefore));
            Assert.That(_client.IsEquipped(5001), Is.False);
            Assert.That(_client.IsEquipped(7001), Is.True);

            // 武器槽被占:服务器把 7001 换下来放回背包,5001 穿上。
            int changes = _changes;
            calls[0].Respond(new EquipItemResponse { Bag = Bag(0, 5002, 7001), Equipment = EquipmentBag(5001) });

            Assert.That(_changes - changes, Is.EqualTo(1), "两份快照一起换,只通知一次");
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(Ids(_client.Bag), Is.EqualTo(new ulong[] { 5002, 7001 }), "整体覆盖,不与旧快照合并");
            Assert.That(Ids(_client.Equipment), Is.EqualTo(new ulong[] { 5001 }));
            Assert.That(_client.Equipment.Layout.Slots[0].Slot, Is.EqualTo(3));
            Assert.That(_client.Equipment.Layout.EquipSlots.Count, Is.EqualTo(4));
            Assert.That(_client.IsEquipped(5001), Is.True);
            Assert.That(_client.IsEquipped(7001), Is.False);
            Assert.That(_client.EquipmentError, Is.Empty);
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void UnequipItemUsesItsOwnMessageAndAppliesBothSnapshots()
        {
            LoadBoth();
            _client.UnequipItem(7001);
            var calls = _net.CallsOf(MessageIds.UnequipItem);
            Assert.That(calls.Count, Is.EqualTo(1));
            Assert.That(((UnequipItemRequest)calls[0].Request).ItemId, Is.EqualTo(7001));
            Assert.That(_net.CallsOf(MessageIds.EquipItem), Is.Empty);
            Assert.That(_client.BusyEquip, Is.True);

            int changes = _changes;
            calls[0].Respond(new UnequipItemResponse { Bag = Bag(0, 5001, 5002, 7001), Equipment = EquipmentBag() });
            Assert.That(_changes - changes, Is.EqualTo(1));
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(Ids(_client.Bag), Is.EqualTo(new ulong[] { 5001, 5002, 7001 }));
            Assert.That(_client.Equipment.Items, Is.Empty);
            Assert.That(_client.Equipment.Layout.EquipSlots.Count, Is.EqualTo(4));
            Assert.That(_client.IsEquipped(7001), Is.False);
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void TipRejectionReportsTableTextAndKeepsBothSnapshots()
        {
            LoadBoth();
            var bagBefore = _client.Bag;
            var equipmentBefore = _client.Equipment;

            _client.EquipItem(5001);
            Last.Respond(new EquipItemResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipLevelNotEnough },
                Bag = Bag(0, 9999), Equipment = EquipmentBag(9999), // 被拒绝时即使带了数据也不得采用
            });
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_errors.Count, Is.EqualTo(1));
            Assert.That(_errors[0], Is.EqualTo("装备失败:等级不足,无法装备"));
            Assert.That(_client.EquipmentError, Is.EqualTo("装备失败:等级不足,无法装备"));
            Assert.That(_client.Bag, Is.SameAs(bagBefore));
            Assert.That(_client.Equipment, Is.SameAs(equipmentBefore));
            Assert.That(_client.BagError, Is.Empty);

            _client.UnequipItem(7001);
            Assert.That(_client.EquipmentError, Is.Empty, "重试时清掉上一次的错误");
            Last.Respond(new UnequipItemResponse { ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipBagFull } });
            Assert.That(_errors[1], Is.EqualTo("卸下失败:背包已满,无法卸下"));
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_client.Bag, Is.SameAs(bagBefore));
            Assert.That(_client.Equipment, Is.SameAs(equipmentBefore));

            // 别的域透传过来的码:退回本类既有的裸编号口径,不编原因。
            _client.EquipItem(5001);
            Last.Respond(new EquipItemResponse { ErrorMessage = new TipInfoMessage { Id = 27003 } });
            Assert.That(_errors[2], Is.EqualTo("装备失败（错误码 27003）"));
            Assert.That(_client.Bag, Is.SameAs(bagBefore));
            Assert.That(_client.Equipment, Is.SameAs(equipmentBefore));
        }

        [TestCase((uint)equip_error.KEquipItemNotFound, "物品不存在")]
        [TestCase((uint)equip_error.KEquipNotEquipment, "该物品不是装备")]
        [TestCase((uint)equip_error.KEquipLevelNotEnough, "等级不足,无法装备")]
        [TestCase((uint)equip_error.KEquipClassMismatch, "职业不符,无法装备")]
        [TestCase((uint)equip_error.KEquipNoSlot, "没有可用的装备栏位")]
        [TestCase((uint)equip_error.KEquipNotEquipped, "该装备未穿戴")]
        [TestCase((uint)equip_error.KEquipBagFull, "背包已满,无法卸下")]
        [TestCase((uint)equip_error.KEquipInBattle, "战斗中无法更换装备")]
        [TestCase((uint)equip_error.KEquipFrozen, "当前状态无法更换装备")]
        [TestCase((uint)equip_error.KEquipGrantInvalid, "发放参数无效")]
        [TestCase((uint)equip_error.KEquipInternalError, "装备操作失败,请稍后再试")]
        public void EquipTipTextMirrorsTipTable(uint tipId, string text)
        {
            Assert.That(PlayerFeaturesClient.DescribeEquipTip("装备失败", new TipInfoMessage { Id = tipId }),
                Is.EqualTo("装备失败:" + text));
        }

        [Test]
        public void MissingTipIsNotDescribedAsAnError()
        {
            Assert.That(PlayerFeaturesClient.DescribeEquipTip("装备失败", null), Is.EqualTo("装备失败"));
            Assert.That(PlayerFeaturesClient.DescribeEquipTip("装备失败", new TipInfoMessage()), Is.EqualTo("装备失败"));
        }

        [Test]
        public void RepeatedClicksWhileInFlightSendOnlyOnePacket()
        {
            LoadBoth();
            _client.EquipItem(5001);
            int changes = _changes;
            _client.EquipItem(5001);
            _client.EquipItem(5002);
            _client.UnequipItem(7001);

            Assert.That(_net.CallsOf(MessageIds.EquipItem).Count, Is.EqualTo(1));
            Assert.That(_net.CallsOf(MessageIds.UnequipItem), Is.Empty);
            Assert.That(_errors, Is.Empty, "连点不弹错");
            Assert.That(_changes, Is.EqualTo(changes));
            Assert.That(_client.BusyEquip, Is.True);

            Last.Respond(new EquipItemResponse { Bag = Bag(0, 5002, 7001), Equipment = EquipmentBag(5001) });
            _client.UnequipItem(5001);
            Assert.That(_net.CallsOf(MessageIds.UnequipItem).Count, Is.EqualTo(1), "回包之后才发得出下一个");
        }

        [Test]
        public void OlderReadsStayAliveWhileEquipIsInFlightAndCannotOverwriteItsResult()
        {
            LoadBoth();
            _client.RequestBag();       // Calls[2]:穿上之前发出的读
            _client.RequestEquipment(); // Calls[3]
            _client.EquipItem(5001);    // Calls[4]
            Assert.That(_client.BagLoading && _client.EquipmentLoading, Is.True, "穿脱在途不作废读取:万一被拒,它们还得落地");

            _net.Calls[4].Respond(new EquipItemResponse { Bag = Bag(0, 5002, 7001), Equipment = EquipmentBag(5001) });
            Assert.That(_client.BagLoading || _client.EquipmentLoading, Is.False, "被回包作废的读取不能把加载态留着");
            var bag = _client.Bag;
            var equipment = _client.Equipment;
            int changes = _changes;
            _net.Calls[2].Respond(new GetBagResponse { Bag = Bag(0, 5001, 5002) });
            _net.Calls[3].Respond(new GetBagResponse { Bag = EquipmentBag(7001) });
            _net.Calls[2].FailWith("rpc timeout");
            _net.Calls[3].FailWith("rpc timeout");
            Assert.That(_client.Bag, Is.SameAs(bag), "更旧的读回包不得盖掉写回包");
            Assert.That(_client.Equipment, Is.SameAs(equipment));
            Assert.That(_changes, Is.EqualTo(changes));
            Assert.That(_errors, Is.Empty);
            Assert.That(_net.Calls.Count, Is.EqualTo(5), "没有人要求刷新,成功后不多发读取");
        }

        [Test]
        public void InFlightReadsFailingDuringEquipAreSupersededByItsSuccess()
        {
            LoadBoth();
            _client.RequestBag();       // Calls[2]
            _client.RequestEquipment(); // Calls[3]
            _client.EquipItem(5001);    // Calls[4]
            _net.Calls[2].FailWith("rpc timeout");
            _net.Calls[3].Respond(new GetBagResponse { ErrorMessage = new TipInfoMessage { Id = 5 } });
            Assert.That(_client.BagError, Is.Not.Empty);
            Assert.That(_client.EquipmentError, Does.Contain("5"));
            Assert.That(_client.BusyEquip, Is.True, "读取失败不结束穿脱");

            _net.Calls[4].Respond(new EquipItemResponse { Bag = Bag(0, 5002, 7001), Equipment = EquipmentBag(5001) });
            Assert.That(_client.BagError, Is.Empty, "两份快照已被回包刷新,读取时的错误不再成立");
            Assert.That(_client.EquipmentError, Is.Empty);
            Assert.That(Ids(_client.Equipment), Is.EqualTo(new ulong[] { 5001 }));
        }

        [Test]
        public void RejectedEquipLeavesInFlightReadsAliveSoBothSnapshotsStillArrive()
        {
            // 第一次打开背包页:两份读取都在途,装备栏先到,行囊没到就点了「卸下」,被拒。
            _client.RequestBag();       // Calls[0]
            _client.RequestEquipment(); // Calls[1]
            _net.Calls[1].Respond(new GetBagResponse { Bag = EquipmentBag(7001) });
            _client.UnequipItem(7001);  // Calls[2]
            _net.Calls[2].Respond(new UnequipItemResponse { ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipBagFull } });

            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_client.BagLoading, Is.True, "被拒时服务器状态没变:在途的行囊读取不作废");
            Assert.That(_net.Calls.Count, Is.EqualTo(3), "规则类拒绝不补拉");
            _net.Calls[0].Respond(new GetBagResponse { Bag = Bag(0, 5001) });
            Assert.That(Ids(_client.Bag), Is.EqualTo(new ulong[] { 5001 }), "行囊不能停在「从未读到」");
            Assert.That(_client.BagLoading, Is.False);
            Assert.That(_client.BagError, Is.Empty);
            Assert.That(_client.EquipmentError, Is.EqualTo("卸下失败:背包已满,无法卸下"), "行囊读回来不冲掉穿脱的失败原因");

            // 反过来:装备栏的刷新在途时穿上被拒,装备栏照样读得回来。
            _client.RequestEquipment(); // Calls[3]
            _client.EquipItem(5001);    // Calls[4]
            Assert.That(_client.EquipmentLoading, Is.True);
            _net.Calls[4].Respond(new EquipItemResponse { ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipLevelNotEnough } });
            _net.Calls[3].Respond(new GetBagResponse { Bag = EquipmentBag(7001, 7002) });
            Assert.That(Ids(_client.Equipment), Is.EqualTo(new ulong[] { 7001, 7002 }));
            Assert.That(_client.EquipmentLoading, Is.False);
            Assert.That(_client.EquipmentError, Is.EqualTo("装备失败:等级不足,无法装备"), "装备栏读回来也不冲掉");
            Assert.That(_net.Calls.Count, Is.EqualTo(5));
        }

        [Test]
        public void RefreshRequestedDuringEquipIsDeferredSilentlyAndReplayedAfterSuccess()
        {
            LoadBoth();
            _client.EquipItem(5001);    // Calls[2]
            int changes = _changes;
            _client.RequestBag();       // 例如回包前又点了一下「背包」页签
            _client.RequestEquipment();
            _client.RequestBag();
            Assert.That(_net.Calls.Count, Is.EqualTo(3), "回包自带两份全量:在途期间不发读取");
            Assert.That(_errors, Is.Empty, "也不报错");
            Assert.That(_client.BagError, Is.Empty);
            Assert.That(_client.EquipmentError, Is.Empty);
            Assert.That(_client.BagLoading || _client.EquipmentLoading, Is.False);
            Assert.That(_changes, Is.EqualTo(changes));

            _net.Calls[2].Respond(new EquipItemResponse { Bag = Bag(0, 5002, 7001), Equipment = EquipmentBag(5001) });
            Assert.That(BagReadsFrom(3), Is.EqualTo(new uint[] { 0, 2 }), "被挡下的刷新在回包后补发,各一次");
            Assert.That(_client.BagLoading && _client.EquipmentLoading, Is.True);
            Assert.That(Ids(_client.Bag), Is.EqualTo(new ulong[] { 5002, 7001 }), "补发的读取回来之前先显示回包的那份");

            // 那次刷新可能比回包更新(例如帮会兑换刚发了一件东西)。
            _net.Calls[3].Respond(new GetBagResponse { Bag = Bag(0, 5002, 7001, 6001) });
            _net.Calls[4].Respond(new GetBagResponse { Bag = EquipmentBag(5001) });
            Assert.That(Ids(_client.Bag), Is.EqualTo(new ulong[] { 5002, 7001, 6001 }));
            Assert.That(_client.BagLoading || _client.EquipmentLoading, Is.False);
            Assert.That(_errors, Is.Empty);

            // 标记用过即清:下一次穿脱成功不再多发读取。
            _client.UnequipItem(5001);
            Last.Respond(new UnequipItemResponse { Bag = Bag(0, 5001, 5002, 7001, 6001), Equipment = EquipmentBag() });
            Assert.That(_net.Calls.Count, Is.EqualTo(6));
        }

        [Test]
        public void RefreshDeferredDuringRejectedEquipLeavesBagErrorCleanAndKeepsTheRejectReason()
        {
            LoadBoth();
            _client.EquipItem(5001);    // Calls[2]
            _client.RequestBag();       // 在途期间的刷新:不报错
            _net.Calls[2].Respond(new EquipItemResponse { ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipLevelNotEnough } });

            Assert.That(_client.BagError, Is.Empty, "背包侧没有出过错,不得盖住穿脱的失败原因");
            Assert.That(_client.EquipmentError, Is.EqualTo("装备失败:等级不足,无法装备"));
            Assert.That(_errors, Is.EqualTo(new[] { "装备失败:等级不足,无法装备" }));
            Assert.That(BagReadsFrom(3), Is.EqualTo(new uint[] { 0 }), "只补发被挡下的那一次;规则类拒绝不另外重拉装备栏");

            _net.Calls[3].Respond(new GetBagResponse { Bag = Bag(0, 5001, 5002, 6001) });
            Assert.That(Ids(_client.Bag), Is.EqualTo(new ulong[] { 5001, 5002, 6001 }));
            Assert.That(_client.BagError, Is.Empty);
            Assert.That(_client.EquipmentError, Is.EqualTo("装备失败:等级不足,无法装备"));
        }

        [Test]
        public void SwitchingBagTypeDuringEquipStillHasToWait()
        {
            LoadBoth();
            _client.EquipItem(5001);
            _client.RequestBag(1);
            Assert.That(_net.Calls.Count, Is.EqualTo(3));
            Assert.That(_client.RequestedBagType, Is.Zero);
            Assert.That(_client.BagError, Is.Not.Empty, "换一类背包不是回包能覆盖的:照旧提示稍候");
        }

        // ── 失败后的重新同步 ─────────────────────────────────

        /// <summary>服务器状态未知、或本地快照已被证明过期的失败:两份快照都重新拉,失败原因留着。</summary>
        [TestCase("timeout", "装备失败:网络异常,请稍后重试")]
        [TestCase("parse", "装备失败:网络异常,请稍后重试")]
        [TestCase("payload", "服务器未返回更换后的背包与装备栏")]
        [TestCase("foreign", "装备失败（错误码 27003）")]
        [TestCase("envelope", "装备失败（错误码 5）")]
        [TestCase("internal", "装备失败:装备操作失败,请稍后再试")]
        [TestCase("missing", "装备失败:物品不存在")]
        public void FailuresThatLeaveStateUnknownResyncBothSnapshotsAndKeepTheReason(string kind, string message)
        {
            LoadBoth();
            _client.EquipItem(5001); // Calls[2]
            var equip = _net.Calls[2];
            switch (kind)
            {
                case "timeout": equip.FailWith("rpc timeout"); break;
                case "parse": equip.FailWith("parse response: bad wire type"); break;
                // 没有 tip 却缺快照:穿脱已经生效,只是没带回来。
                case "payload": equip.Respond(new EquipItemResponse { Bag = Bag(0, 5002, 7001) }); break;
                // 服务端在「穿脱已生效但快照建不出来」时回的就是别的域的码(player_bag_handler.cpp)。
                case "foreign": equip.Respond(new EquipItemResponse { ErrorMessage = new TipInfoMessage { Id = 27003 } }); break;
                case "envelope": equip.FailWith("server tip=5"); break;
                case "internal": equip.Respond(new EquipItemResponse { ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipInternalError } }); break;
                default: equip.Respond(new EquipItemResponse { ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipItemNotFound } }); break;
            }

            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_errors, Is.EqualTo(new[] { message }));
            Assert.That(_client.EquipmentError, Is.EqualTo(message));
            Assert.That(BagReadsFrom(3), Is.EqualTo(new uint[] { 0, 2 }), "人物背包与装备栏各重拉一次");
            Assert.That(_net.Calls.Count, Is.EqualTo(5));
            Assert.That(_client.BagLoading && _client.EquipmentLoading, Is.True);
            Assert.That(Ids(_client.Bag), Is.EqualTo(new ulong[] { 5001, 5002 }), "重拉回来之前不动旧快照");

            // 服务器其实已经执行:重拉回来的是穿上之后的状态。
            _net.Calls[3].Respond(new GetBagResponse { Bag = Bag(0, 5002, 7001) });
            _net.Calls[4].Respond(new GetBagResponse { Bag = EquipmentBag(5001) });
            Assert.That(Ids(_client.Bag), Is.EqualTo(new ulong[] { 5002, 7001 }));
            Assert.That(Ids(_client.Equipment), Is.EqualTo(new ulong[] { 5001 }));
            Assert.That(_client.BagLoading || _client.EquipmentLoading, Is.False);
            Assert.That(_client.BagError, Is.Empty);
            Assert.That(_client.EquipmentError, Is.EqualTo(message), "重拉成功不冲掉失败原因");
            Assert.That(_errors.Count, Is.EqualTo(1));

            _client.RequestEquipment();
            Assert.That(_client.EquipmentError, Is.Empty, "玩家主动刷新时收起");
        }

        /// <summary>规则类拒绝(以及 gate 限流):服务器状态没变,不补拉 —— 否则连点会把读取放大成限流。</summary>
        [TestCase((uint)equip_error.KEquipNotEquipment)]
        [TestCase((uint)equip_error.KEquipLevelNotEnough)]
        [TestCase((uint)equip_error.KEquipClassMismatch)]
        [TestCase((uint)equip_error.KEquipNoSlot)]
        [TestCase((uint)equip_error.KEquipBagFull)]
        [TestCase((uint)equip_error.KEquipInBattle)]
        [TestCase((uint)equip_error.KEquipFrozen)]
        [TestCase((uint)common_error.KRateLimitExceeded)]
        public void RuleRejectionsDoNotRefetch(uint tipId)
        {
            LoadBoth();
            for (int i = 0; i < 3; ++i)
            {
                _client.EquipItem(5001);
                // 响应体里的码与信封上的码同一个判定。
                if (i == 2) Last.FailWith("server tip=" + tipId);
                else Last.Respond(new EquipItemResponse { ErrorMessage = new TipInfoMessage { Id = tipId } });
                Assert.That(_client.BusyEquip, Is.False);
            }
            Assert.That(_net.Calls.Count, Is.EqualTo(5), "只有两次初始读取和三次穿上");
            Assert.That(BagReadsFrom(2), Is.Empty);
            Assert.That(_client.BagLoading || _client.EquipmentLoading, Is.False);
            Assert.That(_errors.Count, Is.EqualTo(3));
        }

        [Test]
        public void ResyncWhileViewingWarehouseRereadsOnlyEquipment()
        {
            LoadBoth();
            _client.RequestBag(1);
            Last.Respond(new GetBagResponse { Bag = Bag(1, 8001) });
            var warehouse = _client.Bag;

            _client.UnequipItem(7001); // Calls[3]
            Last.FailWith("rpc timeout");
            Assert.That(BagReadsFrom(4), Is.EqualTo(new uint[] { 2 }), "正在看的仓库与穿脱无关,不重拉");
            Assert.That(_client.Bag, Is.SameAs(warehouse));
            Assert.That(_client.BagLoading, Is.False);
            Assert.That(_client.BagError, Is.Empty);
            Assert.That(_client.EquipmentError, Is.EqualTo("卸下失败:网络异常,请稍后重试"));
        }

        [Test]
        public void FailedResyncReportsItsOwnErrorsWithoutLoopingOrLosingTheReason()
        {
            LoadBoth();
            var bag = _client.Bag;
            var equipment = _client.Equipment;
            _client.EquipItem(5001); // Calls[2]
            Last.FailWith("rpc timeout");
            _net.Calls[3].FailWith("rpc timeout");
            _net.Calls[4].FailWith("rpc timeout");

            Assert.That(_net.Calls.Count, Is.EqualTo(5), "重拉失败不再触发重拉");
            Assert.That(_client.Bag, Is.SameAs(bag));
            Assert.That(_client.Equipment, Is.SameAs(equipment));
            Assert.That(_client.BagLoading || _client.EquipmentLoading, Is.False);
            Assert.That(_client.BagError, Is.Not.Empty, "背包页据此提示「刷新重试」");
            Assert.That(_client.EquipmentError, Is.EqualTo("装备失败:网络异常,请稍后重试"));
        }

        [Test]
        public void CharacterChangeInsideFailureNotificationSkipsTheResync()
        {
            LoadBoth();
            _client.EquipItem(5001); // Calls[2]
            _client.OnChanged += () =>
            {
                if (_client.EquipmentError.Length == 0 || _net.PlayerId == 9202) return;
                _net.PlayerId = 9202;
                _client.RequestMissions(); // Calls[3]
            };
            Last.FailWith("rpc timeout");

            Assert.That(BagReadsFrom(3), Is.Empty, "不替上一个角色补发读取");
            Assert.That(_net.Calls.Count, Is.EqualTo(4));
            Assert.That(_client.HasBag || _client.HasEquipment || _client.BagLoading || _client.EquipmentLoading, Is.False);
            Assert.That(_client.EquipmentError, Is.Empty);
        }

        [Test]
        public void ResyncIsNotSentOnceTheSessionIsNoLongerCurrent()
        {
            LoadBoth();
            _client.EquipItem(5001); // Calls[2]
            // 失败通知里 gate 已经不可用(断线事件还没到):不补发,也不多报一条「尚未进入游戏」。
            _client.OnChanged += () => { if (_client.EquipmentError.Length != 0) _net.IsReady = false; };
            Last.FailWith("rpc timeout");

            Assert.That(_net.Calls.Count, Is.EqualTo(3));
            Assert.That(_errors, Is.EqualTo(new[] { "装备失败:网络异常,请稍后重试" }));
            Assert.That(_client.BagLoading || _client.EquipmentLoading, Is.False);
        }

        [Test]
        public void TransportAndEnvelopeErrorsAreTurnedIntoPlayerText()
        {
            LoadBoth();
            // 信封上的码(gate / 路由层)与响应体里的码走同一套文案。
            _client.EquipItem(5001);
            Last.FailWith("server tip=" + (uint)equip_error.KEquipLevelNotEnough);
            Assert.That(_client.EquipmentError, Is.EqualTo("装备失败:等级不足,无法装备"));
            _client.UnequipItem(7001);
            Last.FailWith("server tip=" + (uint)common_error.KRateLimitExceeded);
            Assert.That(_client.EquipmentError, Is.EqualTo("卸下失败（错误码 " + (uint)common_error.KRateLimitExceeded + "）"));
            Assert.That(_net.Calls.Count, Is.EqualTo(4));

            // 传输层的内部英文串一律不上屏。
            foreach (string raw in new[] { "rpc timeout", "disconnected", "not connected", "send failed: socket closed",
                         "parse response: bad wire type", "server tip=", "server tip=abc", "server tip=0", "", null })
            {
                _client.UnequipItem(7001);
                _net.CallsOf(MessageIds.UnequipItem).Last().FailWith(raw);
                Assert.That(_client.EquipmentError, Is.EqualTo("卸下失败:网络异常,请稍后重试"), raw);
                Assert.That(_client.BusyEquip, Is.False);
            }
            Assert.That(_errors.All(error => !error.Contains("rpc") && !error.Contains("server tip") && !error.Contains("parse")), Is.True);
        }

        [Test]
        public void EquipWhileViewingWarehouseLeavesThatViewAloneAndStillCachesCharacterBag()
        {
            LoadBoth();
            _client.RequestBag(1);      // Calls[2]:仓库在途
            _client.UnequipItem(7001);  // Calls[3]
            Assert.That(_client.BagLoading, Is.True, "仓库的在途读取与穿脱无关,不作废");

            _net.Calls[3].Respond(new UnequipItemResponse { Bag = Bag(0, 5001, 5002, 7001), Equipment = EquipmentBag() });
            Assert.That(_client.HasBag, Is.False, "当前看的是仓库,人物背包的回包不得冒充它");
            Assert.That(_client.BagLoading, Is.True);
            Assert.That(_client.Equipment.Items, Is.Empty);

            _net.Calls[2].Respond(new GetBagResponse { Bag = Bag(1, 8001) });
            Assert.That(_client.Bag.Layout.BagType, Is.EqualTo(1));
            Assert.That(_client.BagLoading, Is.False);

            _client.RequestBag();
            Assert.That(Ids(_client.Bag), Is.EqualTo(new ulong[] { 5001, 5002, 7001 }), "切回人物背包先看到卸下回包带回的那份");
        }

        [Test]
        public void MissingOrMistypedPayloadAndTransportFailureKeepSnapshotsAndClearBusy()
        {
            LoadBoth();
            var bagBefore = _client.Bag;
            var equipmentBefore = _client.Equipment;

            // 这几种失败之后都会重拉两份快照(见上面「失败后的重新同步」),所以「最后一个出站调用」不再是穿上那一包。
            FakeBattleTransport.RecordedCall LastEquip() => _net.CallsOf(MessageIds.EquipItem).Last();

            _client.EquipItem(5001);
            LastEquip().Respond(new EquipItemResponse { Bag = Bag(0, 5002, 7001) }); // 缺装备栏
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_errors.Count, Is.EqualTo(1));
            Assert.That(_client.Bag, Is.SameAs(bagBefore), "不采用半份回包");

            _client.EquipItem(5001);
            LastEquip().Respond(new EquipItemResponse { Bag = EquipmentBag(5001), Equipment = Bag(0, 5002, 7001) }); // 两份放反
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_errors.Count, Is.EqualTo(2));

            _client.EquipItem(5001);
            var timedOut = LastEquip();
            timedOut.FailWith("rpc timeout");
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_client.EquipmentError, Is.EqualTo("装备失败:网络异常,请稍后重试"));
            Assert.That(_client.Bag, Is.SameAs(bagBefore));
            Assert.That(_client.Equipment, Is.SameAs(equipmentBefore));

            _client.EquipItem(5001);
            timedOut.FailWith("更早的旧错误");
            timedOut.Respond(new EquipItemResponse { Bag = Bag(0, 9999), Equipment = EquipmentBag(9999) });
            Assert.That(_client.BusyEquip, Is.True, "上一次请求的迟到回调不得结束这一次");
            Assert.That(_client.EquipmentError, Is.Empty);
            Assert.That(_client.Bag, Is.SameAs(bagBefore));
            Assert.That(_client.Equipment, Is.SameAs(equipmentBefore));
        }

        [Test]
        public void EquipSortAndMissionActionsAreMutuallyExclusive()
        {
            LoadBoth();
            var missions = new GetMissionListResponse();
            missions.Missions.Add(new PlayerMissionInfo { MissionId = 15, CanAccept = true, Configured = true });
            _client.RequestMissions();
            Last.Respond(missions);

            _client.SortBag();
            int sent = _net.Calls.Count;
            _client.EquipItem(5001);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_client.EquipmentError, Is.Not.Empty);
            Last.Respond(new SortBagResponse { Bag = Bag(0, 5001, 5002) });

            _client.EquipItem(5001);
            sent = _net.Calls.Count;
            _client.SortBag();
            _client.AcceptMission(0, 15);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
            Assert.That(_client.BusySort || _client.BusyMissionAction, Is.False);
            Assert.That(_client.BagError, Is.Not.Empty);
            Assert.That(_client.MissionsError, Is.Not.Empty);
            Last.Respond(new EquipItemResponse { Bag = Bag(0, 5002, 7001), Equipment = EquipmentBag(5001) });

            _client.AcceptMission(0, 15);
            sent = _net.Calls.Count;
            _client.UnequipItem(5001);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
            Assert.That(_client.BusyMissionAction, Is.True);
            Assert.That(_client.BusyEquip, Is.False);
        }

        [TestCase(false, 9101ul)]
        [TestCase(true, 0ul)]
        public void MissingGateOrUnselectedCharacterSendsNoEquipRpc(bool ready, ulong playerId)
        {
            _net.IsReady = ready;
            _net.PlayerId = playerId;
            _client.RequestEquipment();
            _client.EquipItem(5001);
            _client.UnequipItem(7001);
            Assert.That(_net.Calls, Is.Empty);
            Assert.That(_errors.Count, Is.EqualTo(3));
            Assert.That(_client.EquipmentError, Is.Not.Empty);
            Assert.That(_client.EquipmentLoading || _client.BusyEquip, Is.False);
        }

        [Test]
        public void ZeroItemIdDoesNotSend()
        {
            LoadBoth();
            _client.EquipItem(0);
            _client.UnequipItem(0);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_errors.Count, Is.EqualTo(2));
            Assert.That(_client.IsEquipped(0), Is.False);
        }

        // ── 断线 / 换角色 ───────────────────────────────────

        [Test]
        public void DisconnectClearsBothSnapshotsAndInFlightEquip()
        {
            LoadBoth();
            _client.EquipItem(5001);
            int changes = _changes;
            _net.RaiseDisconnected();

            Assert.That(_changes, Is.GreaterThan(changes));
            Assert.That(_client.HasBag || _client.HasEquipment, Is.False);
            Assert.That(_client.BusyEquip || _client.EquipmentLoading || _client.BagLoading, Is.False);
            Assert.That(_client.EquipmentError, Is.Empty);
            Assert.That(_client.IsEquipped(7001), Is.False);
        }

        [Test]
        public void DeferredRefreshDoesNotSurviveDisconnect()
        {
            LoadBoth();
            _client.EquipItem(5001);
            _client.RequestBag();       // 在途期间被挡下的刷新
            _client.RequestEquipment();
            _net.RaiseDisconnected();

            // 重连后同一角色再穿一次:上一个会话攒下的刷新不该在这时冒出来。
            LoadBoth();
            _client.EquipItem(5001);
            int sent = _net.Calls.Count;
            Last.Respond(new EquipItemResponse { Bag = Bag(0, 5002, 7001), Equipment = EquipmentBag(5001) });
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
            Assert.That(_client.BagLoading || _client.EquipmentLoading, Is.False);
        }

        [Test]
        public void EquipResponseFromAnOlderEpochIsDropped()
        {
            LoadBoth();
            _client.EquipItem(5001);
            var stale = Last;
            _net.RaiseDisconnected();

            // 重连后同一角色只重新拉了人物背包:装备栏的请求序号没再动过,能拦住旧回包的只有 epoch。
            _client.RequestBag();
            Last.Respond(new GetBagResponse { Bag = Bag(0, 6001) });
            var bag = _client.Bag;
            int changes = _changes;

            stale.Respond(new EquipItemResponse { Bag = Bag(0, 5002, 7001), Equipment = EquipmentBag(5001) });
            stale.FailWith("旧会话失败");

            Assert.That(_client.Bag, Is.SameAs(bag));
            Assert.That(_client.HasEquipment, Is.False);
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_changes, Is.EqualTo(changes));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void EquipmentReadFromAnOlderEpochIsDropped()
        {
            _client.RequestEquipment();
            var stale = Last;
            _net.RaiseDisconnected();
            _client.RequestBag();
            Last.Respond(new GetBagResponse { Bag = Bag(0, 6001) });
            int changes = _changes;

            stale.Respond(new GetBagResponse { Bag = EquipmentBag(7001) });
            stale.FailWith("旧会话失败");

            Assert.That(_client.HasEquipment || _client.EquipmentLoading, Is.False);
            Assert.That(_client.EquipmentError, Is.Empty);
            Assert.That(_changes, Is.EqualTo(changes));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void NewCharacterDropsEquipmentAndIgnoresPreviousCharacterEquipResponse()
        {
            LoadBoth();
            _client.EquipItem(5001);
            var stale = Last;
            _net.PlayerId = 9202;
            _client.RequestEquipment();

            Assert.That(_client.HasBag || _client.HasEquipment || _client.BusyEquip, Is.False);
            Assert.That(_client.EquipmentLoading, Is.True);
            stale.Respond(new EquipItemResponse { Bag = Bag(0, 5002, 7001), Equipment = EquipmentBag(5001) });
            Assert.That(_client.HasBag || _client.HasEquipment, Is.False);

            Last.Respond(new GetBagResponse { Bag = EquipmentBag(8001) });
            Assert.That(Ids(_client.Equipment), Is.EqualTo(new ulong[] { 8001 }));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void CharacterChangeDuringBusyNotificationPreventsSendingOldEquip()
        {
            LoadBoth();
            _client.OnChanged += () =>
            {
                if (!_client.BusyEquip) return;
                _net.PlayerId = 9202;
                _client.RequestMissions();
            };
            _client.EquipItem(5001);
            Assert.That(_net.CallsOf(MessageIds.EquipItem), Is.Empty);
            Assert.That(_client.HasBag || _client.HasEquipment || _client.BusyEquip, Is.False);
        }

        // ── EquipDisplay ────────────────────────────────────

        private static EquipAttrLineInfo Line(uint tier, uint seq, string name, ulong value, ulong cap = 0,
            bool percent = false, uint attrId = 1)
            => new EquipAttrLineInfo { AttrId = attrId, Name = name, Tier = tier, Seq = seq, Value = value, Cap = cap, Percent = percent };

        private static string[] Names(IEnumerable<EquipAttrLineInfo> lines) => lines.Select(line => line.Name).ToArray();

        [Test]
        public void BaseAttributeLineShowsNameAndValueWithoutCap()
        {
            Assert.That(EquipDisplay.FormatLine(Line(0, 0, "伤害", 420)), Is.EqualTo("伤害：420"));
            Assert.That(EquipDisplay.FormatLine(Line(0, 1, "速度", 88, cap: 999)), Is.EqualTo("速度：88"),
                "基础属性行即使带了上限也不显示");
            Assert.That(EquipDisplay.FormatLine(Line(0, 0, "物理必杀率", 5, percent: true)), Is.EqualTo("物理必杀率：5%"));
        }

        [Test]
        public void AffixLineShowsValueOverCapWithPercentOnBothSides()
        {
            Assert.That(EquipDisplay.FormatLine(Line(1, 0, "力量", 3, 5)), Is.EqualTo("力量 3/5"));
            Assert.That(EquipDisplay.FormatLine(Line(2, 0, "物理必杀率", 8, 10, true)), Is.EqualTo("物理必杀率 8%/10%"));
            Assert.That(EquipDisplay.FormatLine(Line(3, 0, "气血", 640, 640)), Is.EqualTo("气血 640/640"));
            Assert.That(EquipDisplay.FormatLine(Line(4, 0, "所有属性", 16, 16)), Is.EqualTo("所有属性 16/16"));
            Assert.That(EquipDisplay.FormatLine(Line(9, 0, "新属性", 1, 2)), Is.EqualTo("新属性 1/2"), "不认识的档按随机属性排版");
            Assert.That(EquipDisplay.FormatLine(Line(1, 0, "防御", 12)), Is.EqualTo("防御 12"), "随机属性缺上限时不显示 /0");
            Assert.That(EquipDisplay.FormatLine(Line(1, 0, "抗物理", 2, percent: true)), Is.EqualTo("抗物理 2%"));
            Assert.That(EquipDisplay.FormatLine(Line(1, 0, "伤害", ulong.MaxValue, ulong.MaxValue)),
                Is.EqualTo("伤害 18446744073709551615/18446744073709551615"));
        }

        [Test]
        public void MissingNameFallsBackToAttrIdAndServerTextStaysPlain()
        {
            Assert.That(EquipDisplay.FormatLine(Line(1, 0, "", 3, 5, attrId: 17)), Is.EqualTo("属性 · 17 3/5"));
            Assert.That(EquipDisplay.FormatLine(Line(0, 0, " ", 3, attrId: 8)), Is.EqualTo("属性 · 8：3"));
            Assert.That(EquipDisplay.FormatLine(null), Is.Empty);
            // 服务器下发的名称原样拼入,不加也不解释任何富文本标记(UI 侧 richText = false)。
            Assert.That(EquipDisplay.FormatLine(Line(1, 0, "<color=red>力量</color>", 3, 5)),
                Is.EqualTo("<color=red>力量</color> 3/5"));
        }

        [TestCase(0u, EquipAttrTier.Base)]
        [TestCase(1u, EquipAttrTier.Blue)]
        [TestCase(2u, EquipAttrTier.Pink)]
        [TestCase(3u, EquipAttrTier.Yellow)]
        [TestCase(4u, EquipAttrTier.Green)]
        [TestCase(5u, EquipAttrTier.Unknown)]
        [TestCase(uint.MaxValue, EquipAttrTier.Unknown)]
        public void TierMapsToSemanticColor(uint tier, EquipAttrTier expected)
        {
            Assert.That(EquipDisplay.TierOf(tier), Is.EqualTo(expected));
        }

        [Test]
        public void SortLinesOrdersByTierThenSeqStablyWithoutMutatingInput()
        {
            var input = new List<EquipAttrLineInfo>
            {
                Line(3, 0, "黄0", 2, 5),
                Line(1, 1, "蓝1甲", 4, 5),
                Line(0, 1, "基础1", 88),
                Line(2, 0, "粉0", 3, 5),
                Line(1, 0, "蓝0", 2, 5),
                Line(0, 0, "基础0", 126),
                Line(1, 1, "蓝1乙", 1, 5), // 与「蓝1甲」同 (tier, seq):保持传入先后
            };
            var sorted = EquipDisplay.SortLines(input);

            Assert.That(Names(sorted), Is.EqualTo(new[] { "基础0", "基础1", "蓝0", "蓝1甲", "蓝1乙", "粉0", "黄0" }));
            Assert.That(sorted, Is.Not.SameAs(input));
            Assert.That(Names(input), Is.EqualTo(new[] { "黄0", "蓝1甲", "基础1", "粉0", "蓝0", "基础0", "蓝1乙" }), "不改入参");

            input.Reverse();
            Assert.That(Names(EquipDisplay.SortLines(input)),
                Is.EqualTo(new[] { "基础0", "基础1", "蓝0", "蓝1乙", "蓝1甲", "粉0", "黄0" }), "相同键的先后跟着输入走");
            Assert.That(EquipDisplay.SortLines(null), Is.Empty);
        }

        [Test]
        public void LinesOfPutsBaseAttributesFirstThenAffixesByTier()
        {
            var item = new BagItemInfo { ItemId = 5001, EquipKind = 14 };
            item.BaseAttrs.Add(Line(0, 1, "速度", 88));
            item.BaseAttrs.Add(Line(0, 0, "防御", 126));
            item.Affixes.Add(Line(3, 0, "抗物理", 2, 5, true));
            item.Affixes.Add(Line(1, 1, "体质", 4, 5));
            item.Affixes.Add(Line(2, 0, "力量", 3, 5));
            item.Affixes.Add(Line(1, 0, "敏捷", 2, 5));

            var lines = EquipDisplay.LinesOf(item);
            Assert.That(lines.Select(EquipDisplay.FormatLine).ToArray(), Is.EqualTo(new[]
            {
                "防御：126", "速度：88", "敏捷 2/5", "体质 4/5", "力量 3/5", "抗物理 2%/5%",
            }));
            Assert.That(Names(item.Affixes), Is.EqualTo(new[] { "抗物理", "体质", "力量", "敏捷" }), "不改快照里的顺序");
            Assert.That(EquipDisplay.LinesOf(new BagItemInfo()), Is.Empty);
            Assert.That(EquipDisplay.LinesOf(null), Is.Empty);
        }
    }
}
