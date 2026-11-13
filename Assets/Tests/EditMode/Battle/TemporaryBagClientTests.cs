using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI.Ugui.Gameplay;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 临时格(bag_type = 3)的快照与取回(服务端 docs/design/bag-rule-policy-layering.md §6.6):
    /// 读取不改正在看的背包;取回 item_id 与 take_all 二选一;成功用回包里的两份全量整体覆盖;三个 bag_error 码的处理;
    /// 传输失败不自动重发、只重拉两份;与整理 / 装备类操作互斥;发包间隔;断线复位。末尾是显示规则 TemporaryBagModel。
    /// </summary>
    public sealed class TemporaryBagClientTests
    {
        private const ulong Potion = 3001;  // 临时格里的一堆丹药
        private const ulong Blade = 3002;   // 临时格里的一把 +3 装备

        private FakeBattleTransport _net;
        private PlayerFeaturesClient _client;
        private List<string> _errors;
        private long _now;

        [SetUp]
        public void SetUp()
        {
            _net = new FakeBattleTransport { PlayerId = 9101, IsReady = true };
            _client = new PlayerFeaturesClient(_net);
            _errors = new List<string>();
            _client.OnError += _errors.Add;
            _now = 100000;
            _client.NowMs = () => _now;
        }

        [TearDown]
        public void TearDown() => _client.Dispose();

        private FakeBattleTransport.RecordedCall Last => _net.Calls[_net.Calls.Count - 1];

        private FakeBattleTransport.RecordedCall LastRead(uint bagType)
        {
            var call = Last;
            Assert.That(call.MessageId, Is.EqualTo(SceneBagClientPlayerRpc.GetBagMessageId), "最近一次出站调用不是读背包");
            Assert.That(((GetBagRequest)call.Request).BagType, Is.EqualTo(bagType));
            return call;
        }

        private FakeBattleTransport.RecordedCall LastRetrieve()
        {
            var call = Last;
            Assert.That(call.MessageId, Is.EqualTo(SceneBagClientPlayerRpc.RetrieveTemporaryItemsMessageId), "最近一次出站调用不是取回");
            Assert.That(call.Request, Is.TypeOf<RetrieveTemporaryItemsRequest>());
            return call;
        }

        private int RetrieveCount() => _net.CallsOf(SceneBagClientPlayerRpc.RetrieveTemporaryItemsMessageId).Count;

        private uint[] BagReadsFrom(int from)
            => _net.Calls.Skip(from).Where(call => call.MessageId == SceneBagClientPlayerRpc.GetBagMessageId)
                .Select(call => ((GetBagRequest)call.Request).BagType).ToArray();

        private static BagItemInfo Stack(ulong id, uint count)
            => new BagItemInfo { ItemId = id, ConfigId = 2001, Count = count, MaxStack = 99, Name = "回春丹" };

        private static BagItemInfo Equip(ulong id, uint level)
            => new BagItemInfo { ItemId = id, ConfigId = 1101, Count = 1, MaxStack = 1, EquipKind = 11, Name = "青铜剑", EnhanceLevel = level };

        /// <summary>一个包:每件一格,格位从 0 起。</summary>
        private static BagInfo Bag(uint bagType, uint capacity, params BagItemInfo[] items)
        {
            var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = bagType, Capacity = capacity, CanSort = bagType == 0 } };
            uint slot = 0;
            foreach (var item in items)
            {
                bag.Items.Add(item);
                bag.Layout.Slots.Add(new BagSlotInfo { Slot = slot++, ItemId = item.ItemId, Width = 1, Height = 1 });
            }
            return bag;
        }

        private static BagInfo Temporary(params BagItemInfo[] items) => Bag(PlayerFeaturesClient.TemporaryBagType, 20, items);
        private static BagInfo Character(params BagItemInfo[] items) => Bag(PlayerFeaturesClient.CharacterBagType, 40, items);

        private void LoadCharacter(BagInfo bag)
        {
            _client.RequestBag();
            LastRead(0).Respond(new GetBagResponse { Bag = bag });
        }

        private void LoadTemporary(BagInfo bag)
        {
            _client.RequestTemporary();
            LastRead(PlayerFeaturesClient.TemporaryBagType).Respond(new GetBagResponse { Bag = bag });
        }

        private void LoadBoth()
        {
            LoadCharacter(Character());
            LoadTemporary(Temporary(Stack(Potion, 5), Equip(Blade, 3)));
        }

        private static RetrieveTemporaryItemsResponse Rejected(uint tipId)
            => new RetrieveTemporaryItemsResponse { ErrorMessage = new TipInfoMessage { Id = tipId } };

        // ── 读取 ──────────────────────────────────────────

        [Test]
        public void RequestTemporaryReadsBagThreeWithoutChangingTheViewedBag()
        {
            LoadCharacter(Character());
            var viewed = _client.Bag;
            _client.RequestTemporary();
            var read = LastRead(PlayerFeaturesClient.TemporaryBagType);
            Assert.That(_client.TemporaryLoading, Is.True);
            Assert.That(_client.RequestedBagType, Is.EqualTo(PlayerFeaturesClient.CharacterBagType), "背包页照旧看人物背包");

            read.Respond(new GetBagResponse { Bag = Temporary(Stack(Potion, 5)) });
            Assert.That(_client.TemporaryLoading, Is.False);
            Assert.That(_client.Temporary.Items.Single().ItemId, Is.EqualTo(Potion));
            Assert.That(_client.Bag, Is.SameAs(viewed));
            Assert.That(_client.TemporaryError, Is.Empty);
        }

        [Test]
        public void TemporaryReadFailureIsShownWithoutTheTransportString()
        {
            _client.RequestTemporary();
            LastRead(PlayerFeaturesClient.TemporaryBagType).FailWith("rpc timeout");
            Assert.That(_client.TemporaryLoading, Is.False);
            Assert.That(_client.TemporaryError, Is.EqualTo("读取临时格失败:网络异常,请稍后重试"));
            Assert.That(_errors.Single(), Does.Not.Contain("rpc timeout"));
        }

        // ── 取回 ──────────────────────────────────────────

        [Test]
        public void RetrieveOneSendsOnlyTheItemAndRetrieveAllSendsOnlyTakeAll()
        {
            LoadBoth();
            _client.RetrieveTemporaryItem(Potion);
            var one = (RetrieveTemporaryItemsRequest)LastRetrieve().Request;
            Assert.That(one.ItemId, Is.EqualTo(Potion));
            Assert.That(one.TakeAll, Is.False);
            Assert.That(_client.BusyTemporary, Is.True);
            LastRetrieve().Respond(new RetrieveTemporaryItemsResponse
            {
                Bag = Character(Stack(Potion, 5)), Temporary = Temporary(Equip(Blade, 3)),
            });

            _now += PlayerFeaturesClient.TemporaryRetrieveMinIntervalMs;
            _client.RetrieveAllTemporaryItems();
            var all = (RetrieveTemporaryItemsRequest)LastRetrieve().Request;
            Assert.That(all.ItemId, Is.Zero, "二选一:全部取回不带 item_id");
            Assert.That(all.TakeAll, Is.True);
        }

        [Test]
        public void RetrieveSuccessReplacesBothSnapshotsAndDropsOlderReads()
        {
            LoadBoth();
            _client.RequestBag();
            var olderRead = LastRead(0);
            _client.RetrieveTemporaryItem(Potion);
            var retrieve = LastRetrieve();
            _client.RequestTemporary(); // 取回在途:不发,收口后补发
            Assert.That(_net.Calls.Last(), Is.SameAs(retrieve));

            retrieve.Respond(new RetrieveTemporaryItemsResponse
            {
                Bag = Character(Stack(Potion, 5)), Temporary = Temporary(Equip(Blade, 3)),
            });
            Assert.That(_client.BusyTemporary, Is.False);
            Assert.That(_client.CharacterBag.Items.Single().ItemId, Is.EqualTo(Potion), "回包整体覆盖人物背包");
            Assert.That(_client.Temporary.Items.Single().ItemId, Is.EqualTo(Blade), "回包整体覆盖临时格");
            Assert.That(_client.BagLoading, Is.False);
            Assert.That(_errors, Is.Empty);
            LastRead(PlayerFeaturesClient.TemporaryBagType); // 在途期间攒下的刷新补发了

            var replaced = _client.CharacterBag;
            olderRead.Respond(new GetBagResponse { Bag = Character() });
            Assert.That(_client.CharacterBag, Is.SameAs(replaced), "比写回包更早发出的读取回来时已作废");
        }

        [Test]
        public void ItemNotFoundRefetchesBothBagsAndKeepsTheReason()
        {
            LoadBoth();
            var before = _client.CharacterBag;
            _client.RetrieveTemporaryItem(Potion);
            int calls = _net.Calls.Count;
            LastRetrieve().Respond(Rejected((uint)bag_error.KBagTemporaryItemNotFound));

            Assert.That(_errors, Is.EqualTo(new[] { "临时格里没有这件物品,请刷新后再试" }));
            Assert.That(_client.TemporaryError, Is.EqualTo("临时格里没有这件物品,请刷新后再试"));
            Assert.That(BagReadsFrom(calls), Is.EqualTo(new uint[] { 0, PlayerFeaturesClient.TemporaryBagType }),
                "可能上一次其实已经取回、回包丢了:两个包都重拉");
            Assert.That(_client.BusyTemporary, Is.False);
            Assert.That(_client.CharacterBag, Is.SameAs(before), "重拉回来之前不动旧快照");
            Assert.That(RetrieveCount(), Is.EqualTo(1));
        }

        [TestCase(false, "背包空间不足,无法从临时格取回;请先腾出人物背包空间")]
        [TestCase(true, "背包空间不足,无法从临时格取回;请先腾出人物背包空间,或改为逐件取回")]
        public void BagFullAsksToFreeSpaceAndDoesNotRefetch(bool takeAll, string text)
        {
            LoadBoth();
            if (takeAll) _client.RetrieveAllTemporaryItems();
            else _client.RetrieveTemporaryItem(Potion);
            int calls = _net.Calls.Count;
            LastRetrieve().Respond(Rejected((uint)bag_error.KBagTemporaryRetrieveBagFull));

            Assert.That(_client.TemporaryError, Is.EqualTo(text));
            Assert.That(BagReadsFrom(calls), Is.Empty, "两个包都没动:不重拉");
            Assert.That(_client.BusyTemporary, Is.False);
        }

        [Test]
        public void HandoffInProgressAsksToRetryLater()
        {
            LoadBoth();
            _client.RetrieveAllTemporaryItems();
            int calls = _net.Calls.Count;
            LastRetrieve().Respond(Rejected((uint)bag_error.KBagTemporaryRetrieveBusy));

            Assert.That(_client.TemporaryError, Is.EqualTo("交接或传送途中,暂时不能从临时格取回,请稍后再试"));
            Assert.That(BagReadsFrom(calls), Is.Empty);
        }

        [TestCase(false, "取回失败:网络异常,请稍后重试", TestName = "RetrieveTimeout_IsNotResent_RefetchesBothBags")]
        [TestCase(true, null, TestName = "RetrieveEnvelope1003_IsNotResent_RefetchesBothBags")]
        public void TransportFailureIsNotResentAndRefetchesBothBags(bool envelope1003, string text)
        {
            LoadBoth();
            _client.RetrieveTemporaryItem(Potion);
            int calls = _net.Calls.Count;
            uint unknown = (uint)common_error.KServiceUnavailable;
            LastRetrieve().FailWith(envelope1003 ? "server tip=" + unknown : "rpc timeout");

            Assert.That(RetrieveCount(), Is.EqualTo(1), "取回不带幂等键:结果不明不自动重发");
            Assert.That(BagReadsFrom(calls), Is.EqualTo(new uint[] { 0, PlayerFeaturesClient.TemporaryBagType }));
            Assert.That(_client.TemporaryError, Is.EqualTo(text ?? $"取回失败（错误码 {unknown}）"));
            Assert.That(_client.BusyTemporary, Is.False);
        }

        [Test]
        public void RetrieveIsSingleFlightAndExclusiveWithSortAndEquipActions()
        {
            LoadBoth();
            _client.RetrieveTemporaryItem(Potion);
            int calls = _net.Calls.Count;

            _client.RetrieveAllTemporaryItems();
            Assert.That(_net.Calls.Count, Is.EqualTo(calls), "连点:不发第二个包");
            Assert.That(_errors, Is.Empty, "也不报错");

            _client.SortBag();
            Assert.That(_client.BagError, Is.EqualTo(PlayerFeaturesClient.TemporaryBusyText));
            _client.EquipItem(5001);
            Assert.That(_client.EquipmentError, Is.EqualTo(PlayerFeaturesClient.TemporaryBusyText));
            Assert.That(_net.Calls.Count, Is.EqualTo(calls), "取回在途:整理、穿脱都不发包");

            LastRetrieve().Respond(Rejected((uint)bag_error.KBagTemporaryRetrieveBusy));
            _now += PlayerFeaturesClient.TemporaryRetrieveMinIntervalMs;
            _client.EquipItem(5001); // 穿上在途时取回反过来被挡下
            Assert.That(_client.BusyEquip, Is.True);
            calls = _net.Calls.Count;
            _client.RetrieveAllTemporaryItems();
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));
            Assert.That(_client.TemporaryError, Is.EqualTo("装备正在更换，请稍候"));
        }

        [Test]
        public void RetrieveRespectsTheMinimumSendInterval()
        {
            LoadBoth();
            _client.RetrieveTemporaryItem(Potion);
            LastRetrieve().Respond(new RetrieveTemporaryItemsResponse
            {
                Bag = Character(Stack(Potion, 5)), Temporary = Temporary(Equip(Blade, 3)),
            });
            int calls = _net.Calls.Count;
            _client.RetrieveTemporaryItem(Blade);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls), "离上一次发包不到最小间隔:不发");
            Assert.That(_client.TemporaryError, Is.EqualTo("操作太快，请稍候再试"));

            _now += PlayerFeaturesClient.TemporaryRetrieveMinIntervalMs;
            _client.RetrieveTemporaryItem(Blade);
            Assert.That(((RetrieveTemporaryItemsRequest)LastRetrieve().Request).ItemId, Is.EqualTo(Blade));
        }

        [Test]
        public void DisconnectClearsTheTemporarySnapshotAndDropsTheInFlightRetrieve()
        {
            LoadBoth();
            _client.RetrieveTemporaryItem(Potion);
            var stale = LastRetrieve();
            _net.RaiseDisconnected();
            Assert.That(_client.Temporary, Is.Null);
            Assert.That(_client.BusyTemporary, Is.False);

            stale.Respond(new RetrieveTemporaryItemsResponse
            {
                Bag = Character(Stack(Potion, 5)), Temporary = Temporary(Equip(Blade, 3)),
            });
            Assert.That(_client.Temporary, Is.Null, "旧连接的回包不采用");
            Assert.That(_client.CharacterBag, Is.Null);
        }

        // ── 显示规则 ──────────────────────────────────────

        private static BagInfo TemporaryWith(int used, uint capacity)
        {
            var items = new BagItemInfo[used];
            for (int i = 0; i < used; ++i) items[i] = Stack(10000ul + (ulong)i, 1);
            return Bag(PlayerFeaturesClient.TemporaryBagType, capacity, items);
        }

        [Test]
        public void ModelCountsOccupancyAndWarnsFromNinetyPercent()
        {
            Assert.That(TemporaryBagModel.OccupancyText(null), Is.EqualTo("临时格 —"));
            Assert.That(TemporaryBagModel.BagPageNotice(null), Is.Null, "还没拉到快照:不提醒");
            Assert.That(TemporaryBagModel.BagPageNotice(TemporaryWith(17, 20)), Is.Null, "85%:不提醒");

            var nearly = TemporaryWith(18, 20);
            Assert.That(TemporaryBagModel.IsNearlyFull(nearly), Is.True);
            Assert.That(TemporaryBagModel.IsFull(nearly), Is.False);
            Assert.That(TemporaryBagModel.OccupancyText(nearly), Is.EqualTo("临时格 18/20"));
            Assert.That(TemporaryBagModel.BagPageNotice(nearly), Is.EqualTo("临时格快满了（18/20），请及时取回"));
            Assert.That(TemporaryBagModel.Explain(nearly), Is.EqualTo(TemporaryBagModel.NearlyFullExplanation));

            var full = TemporaryWith(20, 20);
            Assert.That(TemporaryBagModel.IsFull(full), Is.True);
            Assert.That(TemporaryBagModel.BagPageNotice(full), Is.EqualTo("临时格已满（20/20），请在「工具」中取回"));
            Assert.That(TemporaryBagModel.Explain(full), Is.EqualTo(TemporaryBagModel.FullExplanation));
            Assert.That(TemporaryBagModel.FullExplanation, Does.Contain("邮件").And.Contain("自动补发"),
                "满了之后:放不下的战斗掉落改投邮件,邮件接不住时整笔延后、取回后补发");
        }

        [Test]
        public void ModelListsItemsBySlotAndKeepsItemsWithoutASlot()
        {
            var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = PlayerFeaturesClient.TemporaryBagType, Capacity = 20 } };
            bag.Items.Add(Stack(Potion, 5));
            bag.Items.Add(Equip(Blade, 3));
            bag.Items.Add(Stack(3003, 1));
            bag.Layout.Slots.Add(new BagSlotInfo { Slot = 7, ItemId = Potion });
            bag.Layout.Slots.Add(new BagSlotInfo { Slot = 2, ItemId = Blade });

            var items = TemporaryBagModel.Items(bag);
            Assert.That(items.Select(item => item.ItemId), Is.EqualTo(new[] { Blade, Potion, 3003ul }),
                "位置只认 slot;没有格位的也列出来,东西在临时格里就得能取回");
            Assert.That(TemporaryBagModel.RowText(items[0]), Is.EqualTo("青铜剑 +3"));
            Assert.That(TemporaryBagModel.RowText(items[1]), Is.EqualTo("回春丹 ×5"));
            Assert.That(TemporaryBagModel.RowText(items[2]), Is.EqualTo("回春丹"));
        }
    }
}
