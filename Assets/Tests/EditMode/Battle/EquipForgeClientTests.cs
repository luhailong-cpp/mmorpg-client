using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 装备强化 / 镶嵌 / 取下宝石的数据层用例(服务端 docs/design/equipment-enhance-inlay.md §6.1):
    /// 三个请求的成功路径与回包覆盖、强化结果的发布与清理(掷骰没过不是错误)、拒绝码的文案与「要不要重拉」、
    /// 发包前的本地拒绝、发包间隔、旧连接 / 旧角色的迟到回包,以及与穿脱 / 整理 / 任务操作的互斥。
    /// 穿上 / 卸下自己的用例在 <see cref="EquipClientTests"/>,这里不重复。
    /// </summary>
    public sealed class EquipForgeClientTests
    {
        private const ulong Sword = 5001;      // 人物背包里的武器:+0,两个孔都空
        private const ulong WornSword = 7001;  // 穿在身上的武器:+3,0 号孔嵌着攻击宝石
        private const ulong Nowhere = 4040;    // 两份快照里都没有的物品
        private const uint Stone = 2001;       // 强化石
        private const uint AttackGem = 2101;   // 攻击宝石(类型 1)
        private const uint DefenseGem = 2201;  // 防御宝石(类型 2)

        private FakeBattleTransport _net;
        private PlayerFeaturesClient _client;
        private List<string> _errors;
        private List<EquipEnhanceOutcome> _outcomes;
        // 通知的先后:"changed" = OnChanged,"enhance" = OnEnhanceResult。
        private List<string> _events;
        private int _changes;
        // 假时钟(毫秒)。发包间隔只认它,用例自己拨。
        private long _now;

        [SetUp]
        public void SetUp()
        {
            _net = new FakeBattleTransport { PlayerId = 9101, IsReady = true };
            _client = new PlayerFeaturesClient(_net);
            _errors = new List<string>();
            _outcomes = new List<EquipEnhanceOutcome>();
            _events = new List<string>();
            _changes = 0;
            _now = 100000;
            _client.NowMs = () => _now;
            _client.OnError += _errors.Add;
            _client.OnChanged += () => { ++_changes; _events.Add("changed"); };
            _client.OnEnhanceResult += outcome => { _outcomes.Add(outcome); _events.Add("enhance"); };
        }

        [TearDown]
        public void TearDown() => _client.Dispose();

        private FakeBattleTransport.RecordedCall Last => _net.Calls[_net.Calls.Count - 1];

        // FakeBattleTransport.Respond 按调用方的 parser 做字节往返,喂错响应类型不会报错。
        // 所以喂响应之前一律经下面三个函数,先确认最近发出去的确实是这条消息、这种请求体、这些字段。

        private FakeBattleTransport.RecordedCall LastEnhance(ulong itemId)
        {
            var call = Last;
            Assert.That(call.MessageId, Is.EqualTo(MessageIds.EnhanceEquip), "最近一次出站调用不是强化");
            Assert.That(call.Request, Is.TypeOf<EnhanceEquipRequest>());
            Assert.That(((EnhanceEquipRequest)call.Request).ItemId, Is.EqualTo(itemId));
            return call;
        }

        private FakeBattleTransport.RecordedCall LastInlay(ulong itemId, uint socket, uint gemConfigId)
        {
            var call = Last;
            Assert.That(call.MessageId, Is.EqualTo(MessageIds.InlayGem), "最近一次出站调用不是镶嵌");
            Assert.That(call.Request, Is.TypeOf<InlayGemRequest>());
            var request = (InlayGemRequest)call.Request;
            Assert.That(request.ItemId, Is.EqualTo(itemId));
            Assert.That(request.Socket, Is.EqualTo(socket));
            Assert.That(request.GemConfigId, Is.EqualTo(gemConfigId));
            return call;
        }

        private FakeBattleTransport.RecordedCall LastRemove(ulong itemId, uint socket)
        {
            var call = Last;
            Assert.That(call.MessageId, Is.EqualTo(MessageIds.RemoveGem), "最近一次出站调用不是取下");
            Assert.That(call.Request, Is.TypeOf<RemoveGemRequest>());
            var request = (RemoveGemRequest)call.Request;
            Assert.That(request.ItemId, Is.EqualTo(itemId));
            Assert.That(request.Socket, Is.EqualTo(socket));
            return call;
        }

        private FakeBattleTransport.RecordedCall LastBagRead(uint bagType)
        {
            var call = Last;
            Assert.That(call.MessageId, Is.EqualTo(SceneBagClientPlayerGetBagHandler.MessageId), "最近一次出站调用不是读背包");
            Assert.That(call.Request, Is.TypeOf<GetBagRequest>());
            Assert.That(((GetBagRequest)call.Request).BagType, Is.EqualTo(bagType));
            return call;
        }

        /// <summary>发出三种操作之一(目标都在快照里、本地检查都能过),返回核对过的那次出站调用。</summary>
        private FakeBattleTransport.RecordedCall Send(string kind)
        {
            switch (kind)
            {
                case "enhance": _client.EnhanceEquip(Sword); return LastEnhance(Sword);
                case "inlay": _client.InlayGem(Sword, 1, AttackGem); return LastInlay(Sword, 1, AttackGem);
                default: _client.RemoveGem(WornSword, 0); return LastRemove(WornSword, 0);
            }
        }

        private int ForgeCallCount()
            => _net.CallsOf(MessageIds.EnhanceEquip).Count + _net.CallsOf(MessageIds.InlayGem).Count
                + _net.CallsOf(MessageIds.RemoveGem).Count;

        /// <summary>从第 from 个出站调用起,发出的背包读取各是哪一类(bag_type)。</summary>
        private uint[] BagReadsFrom(int from)
            => _net.Calls.Skip(from).Where(call => call.MessageId == SceneBagClientPlayerGetBagHandler.MessageId)
                .Select(call => ((GetBagRequest)call.Request).BagType).ToArray();

        // ── 夹具 ────────────────────────────────────────────

        /// <summary>
        /// 一把武器:+level / 上限 maxLevel;没满级时带下一级预览(金币 cost、强化石 ×stones,stones 为 0 = 不要材料);
        /// 两个孔:0 号攻击孔(只接类型 1)、1 号通用孔(任意类型),都空。
        /// </summary>
        private static BagItemInfo Weapon(ulong id, uint level = 0, uint maxLevel = 12, ulong cost = 1000, uint stones = 1)
        {
            var item = new BagItemInfo
            {
                ItemId = id, ConfigId = 1101, Count = 1, MaxStack = 1, EquipKind = 11, Name = "青铜剑",
                EnhanceLevel = level, EnhanceMaxLevel = maxLevel,
            };
            if (maxLevel != 0 && level < maxLevel)
                item.EnhanceNext = new EquipEnhancePreview
                {
                    NextLevel = level + 1, SuccessRate = 10000, CostGold = cost,
                    MaterialConfigId = stones == 0 ? 0u : Stone, MaterialName = stones == 0 ? string.Empty : "强化石",
                    MaterialCount = stones,
                };
            item.GemSockets.Add(new EquipGemSocketInfo { Socket = 0, GemType = 1, Name = "攻击孔" });
            item.GemSockets.Add(new EquipGemSocketInfo { Socket = 1, GemType = 0, Name = "通用孔" });
            return item;
        }

        private static BagItemInfo WithGem(BagItemInfo weapon, uint socket, uint gemConfigId)
        {
            var hole = weapon.GemSockets.First(entry => entry.Socket == socket);
            hole.GemConfigId = gemConfigId;
            hole.GemName = "攻击宝石·一级";
            hole.Attr = new EquipAttrLineInfo { AttrId = 1, Name = "伤害", Value = 30 };
            return weapon;
        }

        private static BagItemInfo Stack(ulong id, uint configId, uint count, uint gemType = 0)
            => new BagItemInfo { ItemId = id, ConfigId = configId, Count = count, MaxStack = 999, GemType = gemType };

        /// <summary>人物背包:每件一格。gold 为 null = 快照不带货币。</summary>
        private static BagInfo CharacterBag(ulong? gold, params BagItemInfo[] items)
        {
            var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = 0, Capacity = 40, CanSort = true } };
            if (gold.HasValue)
            {
                bag.Currency = new CurrencyComp();
                bag.Currency.Values.Add(gold.Value);
            }
            uint slot = 0;
            foreach (var item in items)
            {
                bag.Items.Add(item);
                bag.Layout.Slots.Add(new BagSlotInfo { Slot = slot++, ItemId = item.ItemId, Width = 1, Height = 1 });
            }
            return bag;
        }

        /// <summary>装备栏:只有武器槽(3 号)。</summary>
        private static BagInfo EquipmentBag(params BagItemInfo[] worn)
        {
            var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = 2, Capacity = 7 } };
            bag.Layout.EquipSlots.Add(new EquipSlotInfo { Slot = 3, EquipKind = 11, Name = "武器" });
            uint slot = 3;
            foreach (var item in worn)
            {
                bag.Items.Add(item);
                bag.Layout.Slots.Add(new BagSlotInfo { Slot = slot++, ItemId = item.ItemId, Width = 1, Height = 1 });
            }
            return bag;
        }

        /// <summary>标准人物背包:金币、武器 5001、强化石一堆(6001)、攻击宝石一堆(6002);数量为 0 的那一堆不放。</summary>
        private static BagInfo StandardBag(ulong? gold = 50000, uint stones = 5, uint attackGems = 2, uint swordLevel = 0)
        {
            var items = new List<BagItemInfo> { Weapon(Sword, swordLevel) };
            if (stones != 0) items.Add(Stack(6001, Stone, stones));
            if (attackGems != 0) items.Add(Stack(6002, AttackGem, attackGems, gemType: 1));
            return CharacterBag(gold, items.ToArray());
        }

        /// <summary>标准装备栏:穿着武器 7001(+3),0 号孔嵌着攻击宝石。</summary>
        private static BagInfo StandardEquipment() => EquipmentBag(WithGem(Weapon(WornSword, 3), 0, AttackGem));

        private void Load(BagInfo bag, BagInfo equipment)
        {
            if (bag != null)
            {
                _client.RequestBag();
                LastBagRead(0).Respond(new GetBagResponse { Bag = bag });
            }
            if (equipment != null)
            {
                _client.RequestEquipment();
                LastBagRead(2).Respond(new GetBagResponse { Bag = equipment });
            }
        }

        /// <summary>两份标准快照都到位;之后 _net.Calls 里是两次读取。</summary>
        private void LoadBoth() => Load(StandardBag(), StandardEquipment());

        /// <summary>完成一次成功的强化(5001:+0 → +1),并把时钟拨过发包间隔,好让下一次操作不被当成连点。</summary>
        private void EnhanceOnce()
        {
            _client.EnhanceEquip(Sword);
            LastEnhance(Sword).Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4, swordLevel: 1), Equipment = StandardEquipment(),
                Success = true, EnhanceLevel = 1,
            });
            AssertOutcome(Sword, true, 1);
            _now += PlayerFeaturesClient.ForgeMinIntervalMs;
        }

        private void AssertOutcome(ulong itemId, bool success, uint level)
        {
            Assert.That(_client.LastEnhanceOutcome.HasValue, Is.True, "应当有强化结果");
            var outcome = _client.LastEnhanceOutcome.Value;
            Assert.That(outcome.ItemId, Is.EqualTo(itemId));
            Assert.That(outcome.Success, Is.EqualTo(success));
            Assert.That(outcome.Level, Is.EqualTo(level));
        }

        private static BagItemInfo Find(BagInfo bag, ulong itemId) => bag.Items.First(item => item.ItemId == itemId);

        // ── 强化 ────────────────────────────────────────────

        [TestCase(Sword)]
        [TestCase(WornSword)]
        public void EnhanceSuccessReplacesBothSnapshotsThenPublishesTheOutcome(ulong itemId)
        {
            LoadBoth();
            var bagBefore = _client.CharacterBag;
            var equipmentBefore = _client.Equipment;
            // OnChanged 到达时数据层已经处于什么状态:快照换没换、结果写没写。
            var seenInChanged = new List<(BagInfo Bag, EquipEnhanceOutcome? Outcome)>();
            _client.OnChanged += () => seenInChanged.Add((_client.CharacterBag, _client.LastEnhanceOutcome));

            _client.EnhanceEquip(itemId);
            var call = LastEnhance(itemId);
            Assert.That(_net.Calls.Count, Is.EqualTo(3));
            Assert.That(_client.BusyEquip, Is.True);
            Assert.That(_client.CharacterBag, Is.SameAs(bagBefore), "点击不得乐观修改快照");
            Assert.That(_client.Equipment, Is.SameAs(equipmentBefore));
            Assert.That(_client.LastEnhanceOutcome, Is.Null);
            Assert.That(_outcomes, Is.Empty);

            int changes = _changes;
            _events.Clear();
            seenInChanged.Clear();
            uint level = itemId == Sword ? 1u : 4u;
            call.Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4, swordLevel: itemId == Sword ? 1u : 0u),
                Equipment = EquipmentBag(WithGem(Weapon(WornSword, itemId == WornSword ? 4u : 3u), 0, AttackGem)),
                Success = true, EnhanceLevel = level,
            });

            Assert.That(_changes - changes, Is.EqualTo(1), "两份快照一起换,只通知一次");
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_client.CharacterBag, Is.Not.SameAs(bagBefore));
            Assert.That(_client.Equipment, Is.Not.SameAs(equipmentBefore));
            Assert.That(_client.CharacterBag.Currency.Values[PlayerFeaturesClient.GoldCurrencyIndex], Is.EqualTo(49000),
                "整体覆盖,不与旧快照合并");
            Assert.That(Find(_client.CharacterBag, 6001).Count, Is.EqualTo(4));
            Assert.That(Find(_client.Equipment, WornSword).EnhanceLevel, Is.EqualTo(itemId == WornSword ? 4u : 3u));
            AssertOutcome(itemId, true, level);
            Assert.That(_outcomes.Count, Is.EqualTo(1), "每次强化只发布一次结果");
            Assert.That(_outcomes[0].ItemId, Is.EqualTo(itemId));
            Assert.That(_outcomes[0].Success, Is.True);
            Assert.That(_outcomes[0].Level, Is.EqualTo(level));
            Assert.That(_events, Is.EqualTo(new[] { "changed", "enhance" }), "先通知快照变化,再发布强化结果");
            Assert.That(seenInChanged.Count, Is.EqualTo(1));
            Assert.That(seenInChanged[0].Bag, Is.SameAs(_client.CharacterBag), "OnChanged 时快照已经换好");
            Assert.That(seenInChanged[0].Outcome.HasValue, Is.True, "OnChanged 时结果已经写好");
            Assert.That(_client.EquipmentError, Is.Empty);
            Assert.That(_errors, Is.Empty);
            Assert.That(_net.Calls.Count, Is.EqualTo(3), "没有人要求刷新,成功后不多发读取");
        }

        [Test]
        public void EnhanceRollFailureIsASuccessfulResponseNotAnError()
        {
            LoadBoth();
            _client.EnhanceEquip(Sword);
            var call = LastEnhance(Sword);
            int changes = _changes;
            _events.Clear();
            // 掷骰没过:没有 tip,金币与材料已扣,等级没变;两份快照照常带回。
            call.Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4), Equipment = StandardEquipment(),
                Success = false, EnhanceLevel = 0,
            });

            Assert.That(_errors, Is.Empty, "掷骰没过不走 OnError");
            Assert.That(_client.EquipmentError, Is.Empty);
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_client.CharacterBag.Currency.Values[0], Is.EqualTo(49000), "金币已扣:必须采用带回来的快照");
            Assert.That(Find(_client.CharacterBag, 6001).Count, Is.EqualTo(4), "材料已扣");
            Assert.That(Find(_client.CharacterBag, Sword).EnhanceLevel, Is.Zero);
            AssertOutcome(Sword, false, 0);
            Assert.That(_outcomes.Count, Is.EqualTo(1));
            Assert.That(_outcomes[0].Success, Is.False);
            Assert.That(_changes - changes, Is.EqualTo(1));
            Assert.That(_events, Is.EqualTo(new[] { "changed", "enhance" }));
            Assert.That(_net.Calls.Count, Is.EqualTo(3), "不是失败:不重拉");
        }

        [Test]
        public void EnhanceFailureWithLevelLossReportsTheLevelTheServerSays()
        {
            Load(CharacterBag(50000, Weapon(Sword, 5), Stack(6001, Stone, 9)), StandardEquipment());
            _client.EnhanceEquip(Sword);
            LastEnhance(Sword).Respond(new EnhanceEquipResponse
            {
                Bag = CharacterBag(49000, Weapon(Sword, 3), Stack(6001, Stone, 8)), Equipment = StandardEquipment(),
                Success = false, EnhanceLevel = 3, // 表里配了失败掉级:客户端不推算,照服务器给的写
            });
            AssertOutcome(Sword, false, 3);
            Assert.That(_errors, Is.Empty);
        }

        // ── 镶嵌 / 取下 ─────────────────────────────────────

        [TestCase(0u)]
        [TestCase(1u)]
        public void InlayGemSendsSocketAndGemConfigAndAppliesBothSnapshots(uint socket)
        {
            LoadBoth();
            var equipmentBefore = _client.Equipment;
            _client.InlayGem(Sword, socket, AttackGem);
            var call = LastInlay(Sword, socket, AttackGem); // 0 号孔是合法值,必须原样发出
            Assert.That(_net.CallsOf(MessageIds.EnhanceEquip), Is.Empty);
            Assert.That(_net.CallsOf(MessageIds.RemoveGem), Is.Empty);
            Assert.That(_client.BusyEquip, Is.True);

            int changes = _changes;
            _events.Clear();
            call.Respond(new InlayGemResponse
            {
                Bag = CharacterBag(50000, WithGem(Weapon(Sword), socket, AttackGem), Stack(6001, Stone, 5),
                    Stack(6002, AttackGem, 1, gemType: 1)),
                Equipment = StandardEquipment(),
            });

            Assert.That(_changes - changes, Is.EqualTo(1));
            Assert.That(_client.BusyEquip, Is.False);
            var sockets = Find(_client.CharacterBag, Sword).GemSockets;
            Assert.That(sockets.First(entry => entry.Socket == socket).GemConfigId, Is.EqualTo(AttackGem));
            Assert.That(sockets.First(entry => entry.Socket != socket).GemConfigId, Is.Zero);
            Assert.That(Find(_client.CharacterBag, 6002).Count, Is.EqualTo(1), "宝石少一颗:以回包为准,不在本地扣");
            Assert.That(_client.Equipment, Is.Not.SameAs(equipmentBefore), "装备栏也整体覆盖");
            Assert.That(_client.LastEnhanceOutcome, Is.Null, "镶嵌不产生强化结果");
            Assert.That(_outcomes, Is.Empty);
            Assert.That(_events, Is.EqualTo(new[] { "changed" }));
            Assert.That(_errors, Is.Empty);
            Assert.That(_net.Calls.Count, Is.EqualTo(3));
        }

        [Test]
        public void RemoveGemSendsItemAndSocketAndAppliesBothSnapshots()
        {
            LoadBoth();
            _client.RemoveGem(WornSword, 0);
            var call = LastRemove(WornSword, 0);
            Assert.That(_net.CallsOf(MessageIds.InlayGem), Is.Empty);
            Assert.That(_client.BusyEquip, Is.True);

            int changes = _changes;
            _events.Clear();
            call.Respond(new RemoveGemResponse { Bag = StandardBag(attackGems: 3), Equipment = EquipmentBag(Weapon(WornSword, 3)) });

            Assert.That(_changes - changes, Is.EqualTo(1));
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(Find(_client.Equipment, WornSword).GemSockets[0].GemConfigId, Is.Zero, "孔空了");
            Assert.That(Find(_client.CharacterBag, 6002).Count, Is.EqualTo(3), "宝石回到背包");
            Assert.That(_client.LastEnhanceOutcome, Is.Null);
            Assert.That(_events, Is.EqualTo(new[] { "changed" }));
            Assert.That(_errors, Is.Empty);
            Assert.That(_net.Calls.Count, Is.EqualTo(3));
        }

        // ── 拒绝码:文案 ─────────────────────────────────────

        [TestCase((uint)equip_error.KEquipEnhanceUnavailable, "该装备无法强化")]
        [TestCase((uint)equip_error.KEquipEnhanceMaxLevel, "已强化到最高等级")]
        [TestCase((uint)equip_error.KEquipEnhanceMaterialNotEnough, "强化材料不足")]
        [TestCase((uint)equip_error.KEquipEnhanceGoldNotEnough, "银两不足,无法强化")]
        [TestCase((uint)equip_error.KEquipGemNotGem, "该物品不是宝石")]
        [TestCase((uint)equip_error.KEquipGemNotOwned, "背包里没有这颗宝石")]
        [TestCase((uint)equip_error.KEquipGemSocketInvalid, "该装备没有这个宝石孔")]
        [TestCase((uint)equip_error.KEquipGemSocketOccupied, "该孔位已镶有宝石,请先取下")]
        [TestCase((uint)equip_error.KEquipGemSocketEmpty, "该孔位没有宝石")]
        [TestCase((uint)equip_error.KEquipGemTypeMismatch, "宝石类型与孔位不符")]
        [TestCase((uint)equip_error.KEquipGemBagFull, "背包已满,无法取下宝石")]
        public void ForgeTipTextMirrorsTipTable(uint tipId, string text)
        {
            Assert.That(PlayerFeaturesClient.DescribeEquipTip("无法强化", new TipInfoMessage { Id = tipId }),
                Is.EqualTo("无法强化:" + text));
        }

        [Test]
        public void EachOperationReportsUnderItsOwnName()
        {
            LoadBoth();
            uint tipId = (uint)equip_error.KEquipInBattle;
            Send("enhance").Respond(new EnhanceEquipResponse { ErrorMessage = new TipInfoMessage { Id = tipId } });
            _now += PlayerFeaturesClient.ForgeMinIntervalMs;
            Send("inlay").Respond(new InlayGemResponse { ErrorMessage = new TipInfoMessage { Id = tipId } });
            _now += PlayerFeaturesClient.ForgeMinIntervalMs;
            Send("remove").Respond(new RemoveGemResponse { ErrorMessage = new TipInfoMessage { Id = tipId } });

            // 强化用「无法强化」而不是「强化失败」:后者会和掷骰没过(不是错误)混在一起。
            Assert.That(_errors, Is.EqualTo(new[]
            {
                "无法强化:战斗中无法更换装备", "镶嵌失败:战斗中无法更换装备", "取下失败:战斗中无法更换装备",
            }));
            Assert.That(_outcomes, Is.Empty);
        }

        // ── 拒绝码:要不要重拉 ───────────────────────────────

        /// <summary>规则类拒绝:服务器没动状态,本地快照也没被证明过期 —— 不补拉,带回来的数据也不采用。</summary>
        [TestCase((uint)equip_error.KEquipGemTypeMismatch, "宝石类型与孔位不符")]
        [TestCase((uint)equip_error.KEquipGemBagFull, "背包已满,无法取下宝石")]
        [TestCase((uint)equip_error.KEquipNotEquipment, "该物品不是装备")]
        [TestCase((uint)equip_error.KEquipInBattle, "战斗中无法更换装备")]
        [TestCase((uint)equip_error.KEquipFrozen, "当前状态无法更换装备")]
        public void ForgeRuleRejectionsKeepSnapshotsAndDoNotRefetch(uint tipId, string text)
        {
            LoadBoth();
            var bagBefore = _client.CharacterBag;
            var equipmentBefore = _client.Equipment;
            for (int i = 0; i < 3; ++i)
            {
                _now += PlayerFeaturesClient.ForgeMinIntervalMs;
                var call = Send("inlay");
                // 响应体里的码与信封上的码同一个判定。
                if (i == 2) call.FailWith("server tip=" + tipId);
                else call.Respond(new InlayGemResponse
                {
                    ErrorMessage = new TipInfoMessage { Id = tipId },
                    Bag = StandardBag(gold: 1), Equipment = EquipmentBag(), // 被拒绝时即使带了数据也不得采用
                });
                Assert.That(_client.BusyEquip, Is.False);
                Assert.That(_client.EquipmentError, Is.EqualTo("镶嵌失败:" + text));
            }
            Assert.That(_net.Calls.Count, Is.EqualTo(5), "只有两次初始读取和三次镶嵌");
            Assert.That(BagReadsFrom(2), Is.Empty);
            Assert.That(_client.BagLoading || _client.EquipmentLoading, Is.False);
            Assert.That(_client.CharacterBag, Is.SameAs(bagBefore));
            Assert.That(_client.Equipment, Is.SameAs(equipmentBefore));
            Assert.That(_errors.Count, Is.EqualTo(3));
        }

        [Test]
        public void GateRateLimitOnForgeDoesNotRefetch()
        {
            LoadBoth();
            uint tipId = (uint)common_error.KRateLimitExceeded;
            Send("enhance").FailWith("server tip=" + tipId); // 包没有到 scene:什么都没发生
            Assert.That(_client.EquipmentError, Is.EqualTo("无法强化（错误码 " + tipId + "）"));
            Assert.That(_net.Calls.Count, Is.EqualTo(3), "被限流时再补两个读只会雪上加霜");
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_outcomes, Is.Empty);
        }

        /// <summary>
        /// 这些码要么说明本地快照已经过期(发包前本地按快照拦过的情形,服务器却还这么说),
        /// 要么不知道服务器停在哪一步:两份快照都重新拉,失败原因留着。
        /// </summary>
        [TestCase((uint)equip_error.KEquipGemNotOwned)]
        [TestCase((uint)equip_error.KEquipGemSocketOccupied)]
        [TestCase((uint)equip_error.KEquipGemSocketEmpty)]
        [TestCase((uint)equip_error.KEquipGemSocketInvalid)]
        [TestCase((uint)equip_error.KEquipGemNotGem)]
        [TestCase((uint)equip_error.KEquipEnhanceMaxLevel)]
        [TestCase((uint)equip_error.KEquipEnhanceUnavailable)]
        [TestCase((uint)equip_error.KEquipItemNotFound)]
        [TestCase((uint)equip_error.KEquipInternalError)]
        [TestCase(27003u)] // 别的域透传过来的码
        public void ForgeRejectionsThatLeaveStateUnknownResyncBothSnapshotsAndKeepTheReason(uint tipId)
        {
            LoadBoth();
            var bagBefore = _client.CharacterBag;
            Send("remove").Respond(new RemoveGemResponse { ErrorMessage = new TipInfoMessage { Id = tipId } });

            string message = PlayerFeaturesClient.DescribeEquipTip("取下失败", new TipInfoMessage { Id = tipId });
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_errors, Is.EqualTo(new[] { message }));
            Assert.That(_client.EquipmentError, Is.EqualTo(message));
            Assert.That(BagReadsFrom(3), Is.EqualTo(new uint[] { 0, 2 }), "人物背包与装备栏各重拉一次");
            Assert.That(_net.Calls.Count, Is.EqualTo(5));
            Assert.That(_client.BagLoading && _client.EquipmentLoading, Is.True);
            Assert.That(_client.CharacterBag, Is.SameAs(bagBefore), "重拉回来之前不动旧快照");

            _net.Calls[3].Respond(new GetBagResponse { Bag = StandardBag(attackGems: 3) });
            _net.Calls[4].Respond(new GetBagResponse { Bag = EquipmentBag(Weapon(WornSword, 3)) });
            Assert.That(Find(_client.Equipment, WornSword).GemSockets[0].GemConfigId, Is.Zero, "重拉回来的是服务器的真实状态");
            Assert.That(Find(_client.CharacterBag, 6002).Count, Is.EqualTo(3));
            Assert.That(_client.BagLoading || _client.EquipmentLoading, Is.False);
            Assert.That(_client.EquipmentError, Is.EqualTo(message), "重拉成功不冲掉失败原因");
            Assert.That(_errors.Count, Is.EqualTo(1));
            Assert.That(_net.Calls.Count, Is.EqualTo(5), "重拉不再触发别的请求");
        }

        [Test]
        public void ForgeEnvelopeTipsUseTheSameTextAndClassificationAsBodyTips()
        {
            LoadBoth();
            Send("inlay").FailWith("server tip=" + (uint)equip_error.KEquipGemSocketOccupied);
            Assert.That(_client.EquipmentError, Is.EqualTo("镶嵌失败:该孔位已镶有宝石,请先取下"));
            Assert.That(BagReadsFrom(3), Is.EqualTo(new uint[] { 0, 2 }), "信封上的码同样按「快照过期」重拉");
            Assert.That(_errors.All(error => !error.Contains("server tip")), Is.True, "内部串不上屏");
        }

        /// <summary>
        /// 金币 / 材料不足的两个码:服务器的说法与发包时本地快照的判断一致 = 本来就知道不够,不重拉;
        /// 不一致 = 本地以为够(或以为缺的是另一样),快照已过期,重拉。本地看着不够也照样发包,不在本地拦。
        /// 标准武器强化一次要金币 1000、强化石 ×1;本地判断按服务器的顺序先看材料后看金币。
        /// </summary>
        [TestCase(50000ul, 0u, "material", false, TestName = "EnhanceCost_MaterialKnownShort_ServerSaysMaterial_NoRefetch")]
        [TestCase(500ul, 5u, "gold", false, TestName = "EnhanceCost_GoldKnownShort_ServerSaysGold_NoRefetch")]
        [TestCase(500ul, 0u, "material", false, TestName = "EnhanceCost_BothShort_ServerSaysMaterial_NoRefetch")]
        [TestCase(50000ul, 5u, "material", true, TestName = "EnhanceCost_LooksEnough_ServerSaysMaterial_Refetch")]
        [TestCase(50000ul, 5u, "gold", true, TestName = "EnhanceCost_LooksEnough_ServerSaysGold_Refetch")]
        [TestCase(500ul, 0u, "gold", true, TestName = "EnhanceCost_BothShort_ServerSaysGold_Refetch")]
        [TestCase(500ul, 5u, "material", true, TestName = "EnhanceCost_GoldKnownShort_ServerSaysMaterial_Refetch")]
        [TestCase(50000ul, 0u, "gold", true, TestName = "EnhanceCost_MaterialKnownShort_ServerSaysGold_Refetch")]
        public void EnhanceCostRejectionRefetchesOnlyWhenItContradictsTheLocalSnapshot(ulong gold, uint stones, string code,
            bool resync)
        {
            Load(StandardBag(gold, stones), StandardEquipment());
            uint tipId = code == "material"
                ? (uint)equip_error.KEquipEnhanceMaterialNotEnough : (uint)equip_error.KEquipEnhanceGoldNotEnough;
            string message = code == "material" ? "无法强化:强化材料不足" : "无法强化:银两不足,无法强化";
            uint[] expected = resync ? new uint[] { 0, 2 } : new uint[0];

            _client.EnhanceEquip(Sword);
            var call = LastEnhance(Sword); // 本地看着不够也照发:快照可能偏旧,够不够由服务器说了算
            call.Respond(new EnhanceEquipResponse { ErrorMessage = new TipInfoMessage { Id = tipId } });
            Assert.That(_errors, Is.EqualTo(new[] { message }));
            Assert.That(BagReadsFrom(3), Is.EqualTo(expected));
            Assert.That(_client.LastEnhanceOutcome, Is.Null);

            // 重拉回来的仍是同一份数据;信封上的同一个码走同一个判定。
            if (resync)
            {
                _net.Calls[3].Respond(new GetBagResponse { Bag = StandardBag(gold, stones) });
                _net.Calls[4].Respond(new GetBagResponse { Bag = StandardEquipment() });
            }
            int sent = _net.Calls.Count;
            _now += PlayerFeaturesClient.ForgeMinIntervalMs;
            _client.EnhanceEquip(Sword);
            LastEnhance(Sword).FailWith("server tip=" + tipId);
            Assert.That(_client.EquipmentError, Is.EqualTo(message));
            Assert.That(BagReadsFrom(sent + 1), Is.EqualTo(expected), "信封上的码同一个判定");
            Assert.That(_outcomes, Is.Empty);
        }

        /// <summary>
        /// 「该装备无法强化」:本地快照本来就没有下一级预览(实例未初始化,或下一级是必败的行 —— 服务器不给预览,
        /// 也照样拒)= 服务器的说法与本地判断一致,快照没有过期,不重拉(否则每点一次都白拉两份一模一样的快照);
        /// 本地带着预览、以为能强化,服务器却说不行 = 快照过期,重拉。
        /// </summary>
        [TestCase(false, false, TestName = "EnhanceUnavailable_NoPreviewLocally_NoRefetch")]
        [TestCase(true, true, TestName = "EnhanceUnavailable_PreviewLocally_Refetch")]
        public void EnhanceUnavailableRefetchesOnlyWhenTheLocalSnapshotOfferedAPreview(bool previewLocally, bool resync)
        {
            var sword = Weapon(Sword);
            if (!previewLocally) sword.EnhanceNext = null;
            Load(CharacterBag(50000, sword, Stack(6001, Stone, 5)), StandardEquipment());
            uint tipId = (uint)equip_error.KEquipEnhanceUnavailable;
            uint[] expected = resync ? new uint[] { 0, 2 } : new uint[0];

            _client.EnhanceEquip(Sword);
            var call = LastEnhance(Sword); // 没有预览也照发:本地只拦「未开放(上限为 0)」与「已满级」
            call.Respond(new EnhanceEquipResponse { ErrorMessage = new TipInfoMessage { Id = tipId } });
            Assert.That(_errors, Is.EqualTo(new[] { "无法强化:该装备无法强化" }));
            Assert.That(BagReadsFrom(3), Is.EqualTo(expected));
            Assert.That(_client.LastEnhanceOutcome, Is.Null);
        }

        [Test]
        public void EnhanceCostRejectionRefetchesWhenTheLocalSnapshotCouldNotTell()
        {
            // 人物背包快照不带货币:本地无从判断金币够不够,被拒之后要重拉。
            Load(StandardBag(gold: null), StandardEquipment());
            _client.EnhanceEquip(Sword);
            LastEnhance(Sword).Respond(new EnhanceEquipResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipEnhanceGoldNotEnough },
            });
            Assert.That(BagReadsFrom(3), Is.EqualTo(new uint[] { 0, 2 }));
        }

        // ── 传输失败 / 回包残缺 ─────────────────────────────

        [TestCase("enhance", "无法强化")]
        [TestCase("inlay", "镶嵌失败")]
        [TestCase("remove", "取下失败")]
        public void TransportFailureNeverResendsAndOnlyResyncs(string kind, string operation)
        {
            LoadBoth();
            var bagBefore = _client.CharacterBag;
            var equipmentBefore = _client.Equipment;
            var call = Send(kind);
            call.FailWith("rpc timeout");

            string message = operation + ":网络异常,请稍后重试";
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_errors, Is.EqualTo(new[] { message }), "传输层的内部英文串不上屏");
            Assert.That(_client.EquipmentError, Is.EqualTo(message));
            Assert.That(ForgeCallCount(), Is.EqualTo(1), "绝不自动重发:强化不是幂等操作,再发一次就是再扣一次");
            Assert.That(BagReadsFrom(3), Is.EqualTo(new uint[] { 0, 2 }), "服务器可能已经执行:只重拉快照");
            Assert.That(_net.Calls.Count, Is.EqualTo(5));
            Assert.That(_client.CharacterBag, Is.SameAs(bagBefore));
            Assert.That(_client.Equipment, Is.SameAs(equipmentBefore));
            Assert.That(_client.LastEnhanceOutcome, Is.Null);
            Assert.That(_outcomes, Is.Empty, "结果不明:不发布强化结果");

            // 重拉也失败:不循环,也不再碰强化。
            _net.Calls[3].FailWith("rpc timeout");
            _net.Calls[4].FailWith("rpc timeout");
            Assert.That(_net.Calls.Count, Is.EqualTo(5));
            Assert.That(ForgeCallCount(), Is.EqualTo(1));
            Assert.That(_client.EquipmentError, Is.EqualTo(message));
        }

        [TestCase("enhance")]
        [TestCase("inlay")]
        [TestCase("remove")]
        public void ResponseWithoutSnapshotsResyncsAndPublishesNoOutcome(string kind)
        {
            LoadBoth();
            var bagBefore = _client.CharacterBag;
            var call = Send(kind);
            // 没有 tip 却缺装备栏:操作已经生效(强化连金币材料都扣了),只是没带回来。
            switch (kind)
            {
                case "enhance":
                    call.Respond(new EnhanceEquipResponse { Bag = StandardBag(gold: 49000), Success = true, EnhanceLevel = 1 });
                    break;
                case "inlay": call.Respond(new InlayGemResponse { Bag = StandardBag(attackGems: 1) }); break;
                default: call.Respond(new RemoveGemResponse { Bag = StandardBag(attackGems: 3) }); break;
            }

            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_errors, Is.EqualTo(new[] { "服务器未返回最新的背包与装备栏" }));
            Assert.That(_client.CharacterBag, Is.SameAs(bagBefore), "不采用半份回包");
            Assert.That(BagReadsFrom(3), Is.EqualTo(new uint[] { 0, 2 }));
            Assert.That(_client.LastEnhanceOutcome, Is.Null);
            Assert.That(_outcomes, Is.Empty, "没看到快照就不发布结果,等重拉");
        }

        [Test]
        public void MistypedSnapshotsInAForgeResponseAreNotAdopted()
        {
            LoadBoth();
            var bagBefore = _client.CharacterBag;
            var equipmentBefore = _client.Equipment;
            // 两份放反。
            Send("enhance").Respond(new EnhanceEquipResponse
            {
                Bag = StandardEquipment(), Equipment = StandardBag(), Success = true, EnhanceLevel = 1,
            });
            Assert.That(_client.CharacterBag, Is.SameAs(bagBefore));
            Assert.That(_client.Equipment, Is.SameAs(equipmentBefore));
            Assert.That(_errors.Count, Is.EqualTo(1));
            Assert.That(_outcomes, Is.Empty);
        }

        // ── 发包前的本地拒绝 ─────────────────────────────────

        [Test]
        public void ZeroIdsAreRejectedBeforeSending()
        {
            LoadBoth();
            _client.EnhanceEquip(0);
            _client.InlayGem(0, 0, AttackGem);
            _client.InlayGem(Sword, 0, 0);
            _client.RemoveGem(0, 0);

            Assert.That(_errors, Is.EqualTo(new[]
            {
                "请先选择要强化的装备", "请先选择要镶嵌的装备", "请先选择要镶嵌的宝石", "请先选择要取下宝石的装备",
            }));
            Assert.That(_client.EquipmentError, Is.EqualTo("请先选择要取下宝石的装备"));
            Assert.That(_net.Calls.Count, Is.EqualTo(2), "不发包,也不重拉");
            Assert.That(_client.BusyEquip, Is.False);
        }

        [Test]
        public void ZeroIdsAreRejectedEvenBeforeAnySnapshotArrives()
        {
            _client.EnhanceEquip(0);
            _client.InlayGem(Sword, 0, 0);
            _client.RemoveGem(0, 0);
            Assert.That(_net.Calls, Is.Empty);
            Assert.That(_errors.Count, Is.EqualTo(3));
        }

        [Test]
        public void TargetMissingFromBothSnapshotsIsRejectedLocally()
        {
            LoadBoth();
            _client.EnhanceEquip(Nowhere);
            _client.InlayGem(Nowhere, 0, AttackGem);
            _client.RemoveGem(Nowhere, 0);

            Assert.That(_errors, Is.EqualTo(new[] { "无法强化:物品不存在", "镶嵌失败:物品不存在", "取下失败:物品不存在" }));
            Assert.That(_net.Calls.Count, Is.EqualTo(2), "不发包,也不重拉");
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_client.BagLoading || _client.EquipmentLoading, Is.False);
        }

        [Test]
        public void EnhanceAtMaxLevelOrWhenNotOpenIsRejectedLocally()
        {
            Load(CharacterBag(50000, Weapon(Sword, 12), Weapon(5002, 13), Weapon(5003, 0, maxLevel: 0),
                    Stack(6001, Stone, 99)),
                StandardEquipment());

            _client.EnhanceEquip(Sword); // 满级
            _client.EnhanceEquip(5002);  // 存档等级高于表上限(服务器按上限夹取):同样是满级
            _client.EnhanceEquip(5003);  // 上限为 0 = 强化未开放
            _client.EnhanceEquip(6001);  // 强化石本身不是装备:上限同样是 0

            Assert.That(_errors, Is.EqualTo(new[]
            {
                "无法强化:已强化到最高等级", "无法强化:已强化到最高等级", "无法强化:该装备无法强化", "无法强化:该装备无法强化",
            }));
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(_client.BusyEquip, Is.False);
        }

        [Test]
        public void InlayIntoAMissingOrOccupiedSocketOrWithoutTheGemIsRejectedLocally()
        {
            LoadBoth();
            _client.InlayGem(Sword, 7, AttackGem);       // 这件装备没有 7 号孔
            _client.InlayGem(WornSword, 0, AttackGem);   // 0 号孔已经嵌着一颗
            _client.InlayGem(Sword, 1, DefenseGem);      // 背包里没有防御宝石

            Assert.That(_errors, Is.EqualTo(new[]
            {
                "镶嵌失败:该装备没有这个宝石孔", "镶嵌失败:该孔位已镶有宝石,请先取下", "镶嵌失败:背包里没有这颗宝石",
            }));
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(_client.BusyEquip, Is.False);
        }

        [Test]
        public void GemCountForInlayIsSummedAcrossStacksOfTheCharacterBag()
        {
            // 同一种宝石分两堆,第一堆是 0 颗的残留:按 config 汇总,只要总数不为 0 就放行。
            Load(CharacterBag(50000, Weapon(Sword), Stack(6002, AttackGem, 0, gemType: 1), Stack(6003, AttackGem, 1, gemType: 1)),
                StandardEquipment());
            _client.InlayGem(Sword, 0, AttackGem);
            LastInlay(Sword, 0, AttackGem);
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void RemoveFromAnEmptySocketIsRejectedLocallyButAnUnlistedSocketIsLeftToTheServer()
        {
            LoadBoth();
            _client.RemoveGem(Sword, 0); // 快照里列着这个孔,而且是空的
            Assert.That(_errors, Is.EqualTo(new[] { "取下失败:该孔位没有宝石" }));
            Assert.That(_net.Calls.Count, Is.EqualTo(2));

            // 快照里没有 7 号孔:策划删掉孔位定义之后孔里的宝石仍可按孔位号取下,有没有由服务器说了算。
            _client.RemoveGem(Sword, 7);
            LastRemove(Sword, 7);
            Assert.That(_client.BusyEquip, Is.True);
            Assert.That(_errors.Count, Is.EqualTo(1));
        }

        [Test]
        public void TypeMismatchIsNotBlockedLocally()
        {
            // 0 号孔只接类型 1,手里拿的是类型 2:类型合不合由服务器裁决(被拒也不重拉),数据层不拦。
            Load(CharacterBag(50000, Weapon(Sword), Stack(6004, DefenseGem, 1, gemType: 2)), StandardEquipment());
            _client.InlayGem(Sword, 0, DefenseGem);
            LastInlay(Sword, 0, DefenseGem);
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void LocalChecksUseTheCharacterBagEvenWhileViewingTheWarehouse()
        {
            LoadBoth();
            var character = _client.CharacterBag;
            _client.RequestBag(1);
            var warehouse = new BagInfo { Layout = new BagLayoutInfo { BagType = 1, Capacity = 40, CanSort = true } };
            warehouse.Items.Add(Stack(8001, DefenseGem, 9, gemType: 2)); // 仓库里的宝石不算数
            LastBagRead(1).Respond(new GetBagResponse { Bag = warehouse });
            Assert.That(_client.Bag.Layout.BagType, Is.EqualTo(1), "当前查看的是仓库");
            Assert.That(_client.CharacterBag, Is.SameAs(character), "人物背包快照还在");

            _client.InlayGem(Sword, 1, DefenseGem);
            Assert.That(_errors, Is.EqualTo(new[] { "镶嵌失败:背包里没有这颗宝石" }), "只认人物背包里的宝石");
            Assert.That(_net.Calls.Count, Is.EqualTo(3));

            _client.InlayGem(Sword, 1, AttackGem);
            var call = LastInlay(Sword, 1, AttackGem);
            var shown = _client.Bag;
            call.Respond(new InlayGemResponse
            {
                Bag = CharacterBag(50000, WithGem(Weapon(Sword), 1, AttackGem), Stack(6001, Stone, 5),
                    Stack(6002, AttackGem, 1, gemType: 1)),
                Equipment = StandardEquipment(),
            });
            Assert.That(_client.Bag, Is.SameAs(shown), "正在看的仓库不受回包影响");
            Assert.That(Find(_client.CharacterBag, 6002).Count, Is.EqualTo(1), "人物背包照样被回包覆盖");
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void LocalRejectionIsSkippedWhileEitherSnapshotIsMissing(bool hasBag, bool hasEquipment)
        {
            Load(hasBag ? StandardBag() : null, hasEquipment ? StandardEquipment() : null);
            int sent = _net.Calls.Count;

            // 本地无从判断:目标不在快照里、孔位不存在、手里没有宝石,这些都不拦,交给服务器。
            _client.EnhanceEquip(Nowhere);
            LastEnhance(Nowhere).Respond(new EnhanceEquipResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipInBattle },
            });
            _now += PlayerFeaturesClient.ForgeMinIntervalMs;
            _client.InlayGem(Nowhere, 7, DefenseGem);
            LastInlay(Nowhere, 7, DefenseGem).Respond(new InlayGemResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipInBattle },
            });
            _now += PlayerFeaturesClient.ForgeMinIntervalMs;
            _client.RemoveGem(Nowhere, 0);
            LastRemove(Nowhere, 0);

            Assert.That(_net.Calls.Count, Is.EqualTo(sent + 3));
            Assert.That(_errors.Count, Is.EqualTo(2), "只有服务器给的两次拒绝,没有本地拒绝");
        }

        [Test]
        public void InsufficientGoldOrMaterialIsNotBlockedLocally()
        {
            Load(StandardBag(gold: 0, stones: 0), StandardEquipment());
            _client.EnhanceEquip(Sword);
            LastEnhance(Sword);
            Assert.That(_client.BusyEquip, Is.True);
            Assert.That(_errors, Is.Empty, "快照可能偏旧:金币 / 材料只给界面置灰用,不在数据层拦");
        }

        // ── 发包间隔 ────────────────────────────────────────

        [Test]
        public void ForgeClicksWithinTheMinimumIntervalAreDroppedSilently()
        {
            LoadBoth();
            _client.EnhanceEquip(Sword); // 在 _now 发出
            _now += 80;                  // 回包很快
            LastEnhance(Sword).Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4, swordLevel: 1), Equipment = StandardEquipment(),
                Success = true, EnhanceLevel = 1,
            });
            Assert.That(_client.BusyEquip, Is.False);
            int sent = _net.Calls.Count;
            int changes = _changes;

            // 上一包已经回来,但离它发出还不到最小间隔:三种操作都按连点丢弃。
            _client.EnhanceEquip(Sword);
            _client.InlayGem(Sword, 1, AttackGem);
            _client.RemoveGem(WornSword, 0);
            _client.EnhanceEquip(0); // 连本地拒绝的文案也不出:整次点击被丢弃
            _now += PlayerFeaturesClient.ForgeMinIntervalMs - 81; // 距上次发包差 1 毫秒
            _client.EnhanceEquip(Sword);

            Assert.That(_net.Calls.Count, Is.EqualTo(sent), "不发包");
            Assert.That(_errors, Is.Empty, "不报错");
            Assert.That(_changes, Is.EqualTo(changes), "不通知");
            Assert.That(_client.BusyEquip, Is.False);
            AssertOutcome(Sword, true, 1); // 被丢弃的点击不算「操作开始」,不清上一次的结果

            _now += 1; // 间隔从上一次**发包**算起(不是从回包),到点即可再发
            _client.EnhanceEquip(Sword);
            LastEnhance(Sword);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent + 1));
            Assert.That(_client.LastEnhanceOutcome, Is.Null);
        }

        [Test]
        public void IntervalIsSharedByTheThreeOperationsAndRestartsAtEverySend()
        {
            LoadBoth();
            var inlay = Send("inlay"); // t0
            inlay.Respond(new InlayGemResponse { ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipInBattle } });
            _now += PlayerFeaturesClient.ForgeMinIntervalMs;
            var remove = Send("remove"); // t0 + 一个发包间隔
            remove.Respond(new RemoveGemResponse { ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipInBattle } });
            int sent = _net.Calls.Count;

            _now += PlayerFeaturesClient.ForgeMinIntervalMs - 1; // 离 t0 已经很久,但离上一次发包(取下)差 1 毫秒
            _client.EnhanceEquip(Sword);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent), "间隔按最近一次发包算,三种操作共用");
            _now += 1;
            _client.EnhanceEquip(Sword);
            LastEnhance(Sword);
        }

        [Test]
        public void OnlyForgePacketsThatWereActuallySentStartTheInterval()
        {
            LoadBoth();
            // 本地拒绝没有发包:不起算。
            _client.InlayGem(Sword, 7, AttackGem);
            Assert.That(_errors.Count, Is.EqualTo(1));
            // 穿脱不受间隔约束,也不起算。
            _client.EquipItem(Sword);
            Assert.That(Last.MessageId, Is.EqualTo(MessageIds.EquipItem));
            Assert.That(Last.Request, Is.TypeOf<EquipItemRequest>());
            Last.Respond(new EquipItemResponse
            {
                Bag = CharacterBag(50000, WithGem(Weapon(WornSword, 3), 0, AttackGem), Stack(6001, Stone, 5)),
                Equipment = EquipmentBag(Weapon(Sword)),
            });
            Assert.That(_client.IsEquipped(Sword), Is.True);

            // 同一毫秒里的第一次强化:照发。
            _client.EnhanceEquip(Sword);
            var enhance = LastEnhance(Sword);
            enhance.Respond(new EnhanceEquipResponse
            {
                Bag = CharacterBag(49000, WithGem(Weapon(WornSword, 3), 0, AttackGem), Stack(6001, Stone, 4)),
                Equipment = EquipmentBag(Weapon(Sword, 1)), Success = true, EnhanceLevel = 1,
            });

            // 强化刚发过:再强化被丢弃,但穿脱照常(它们各有各的限流档,不归这个间隔管)。
            int sent = _net.Calls.Count;
            _client.EnhanceEquip(Sword);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
            _client.UnequipItem(Sword);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent + 1));
            Assert.That(Last.MessageId, Is.EqualTo(MessageIds.UnequipItem));
            Assert.That(((UnequipItemRequest)Last.Request).ItemId, Is.EqualTo(Sword));
        }

        [Test]
        public void ClockCanBeReplacedAndNullRestoresTheMonotonicDefault()
        {
            LoadBoth();
            Send("inlay").Respond(new InlayGemResponse { ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipInBattle } });

            // 时钟被换成读数更小的另一只(差值为负):放行,不能把人永久锁在间隔里。
            _now = 5;
            Send("remove").Respond(new RemoveGemResponse { ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipInBattle } });
            Assert.That(ForgeCallCount(), Is.EqualTo(2));

            _client.NowMs = null;
            Assert.That(_client.NowMs, Is.Not.Null, "传 null 恢复默认时钟");
            long first = _client.NowMs();
            long second = _client.NowMs();
            Assert.That(second, Is.GreaterThanOrEqualTo(first), "默认时钟单调不回退");
            Assert.That(first, Is.GreaterThanOrEqualTo(0));
        }

        /// <summary>
        /// ForgeCooldownRemainingMs 是给界面置灰用的读数,必须与闸同口径:读数 &gt; 0 的时刻点击一定被丢弃,读数为 0 的时刻一定放行。
        /// </summary>
        [Test]
        public void CooldownReadingMatchesTheGateExactly()
        {
            LoadBoth();
            Assert.That(_client.ForgeCooldownRemainingMs, Is.EqualTo(0), "还没发过:没有冷却");

            // 本地拒绝不发包,也不起算。
            _client.InlayGem(Sword, 7, AttackGem);
            Assert.That(_client.ForgeCooldownRemainingMs, Is.EqualTo(0));

            Send("remove");
            Assert.That(_client.ForgeCooldownRemainingMs, Is.EqualTo(PlayerFeaturesClient.ForgeMinIntervalMs), "从发包那一刻起算");
            Last.Respond(new RemoveGemResponse { ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipInBattle } });

            _now += 699;
            Assert.That(_client.ForgeCooldownRemainingMs, Is.EqualTo(1));
            int sent = ForgeCallCount();
            _client.EnhanceEquip(Sword);
            Assert.That(ForgeCallCount(), Is.EqualTo(sent), "读数 > 0:闸一定丢弃");

            _now += 1;
            Assert.That(_client.ForgeCooldownRemainingMs, Is.EqualTo(0));
            _client.EnhanceEquip(Sword);
            Assert.That(ForgeCallCount(), Is.EqualTo(sent + 1), "读数为 0:闸一定放行");
            LastEnhance(Sword).Respond(new EnhanceEquipResponse { ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipInBattle } });

            // 时钟读数倒退(换过时钟):闸放行,读数也为 0,不能显示成「还要等很久」。
            _now = 5;
            Assert.That(_client.ForgeCooldownRemainingMs, Is.EqualTo(0));
            // 穿脱不起算。
            _now = 1_000_000;
            _client.UnequipItem(WornSword);
            Assert.That(_client.ForgeCooldownRemainingMs, Is.EqualTo(0));
        }

        // ── 强化结果的发布与清理 ─────────────────────────────

        [TestCase("equip")]
        [TestCase("unequip")]
        [TestCase("enhance")]
        [TestCase("inlay")]
        [TestCase("remove")]
        [TestCase("local-reject")]
        public void AnyEquipActionThatStartsClearsThePreviousOutcome(string kind)
        {
            LoadBoth();
            EnhanceOnce();
            int outcomes = _outcomes.Count;
            int sent = _net.Calls.Count;
            switch (kind)
            {
                case "equip": _client.EquipItem(Sword); break;
                case "unequip": _client.UnequipItem(WornSword); break;
                case "enhance": _client.EnhanceEquip(Sword); break;
                case "inlay": _client.InlayGem(Sword, 1, AttackGem); break;
                case "remove": _client.RemoveGem(WornSword, 0); break;
                // 发包前被本地拒绝的那一次同样算「开始」:它有自己的可见结果(报错),上一次的成功不能还挂着。
                default: _client.EnhanceEquip(Nowhere); break;
            }

            Assert.That(_client.LastEnhanceOutcome, Is.Null);
            Assert.That(_client.BusyEquip, Is.EqualTo(kind != "local-reject"));
            Assert.That(_net.Calls.Count, Is.EqualTo(kind == "local-reject" ? sent : sent + 1));
            Assert.That(_outcomes.Count, Is.EqualTo(outcomes), "清空不是一次新的结果");
        }

        [Test]
        public void OutcomeIsAlsoClearedWhenTheNextActionIsBlockedBySortOrMission()
        {
            LoadBoth();
            EnhanceOnce();
            _client.SortBag();
            Assert.That(Last.MessageId, Is.EqualTo(SceneBagClientPlayerSortBagHandler.MessageId));
            _client.EnhanceEquip(Sword);
            Assert.That(_client.EquipmentError, Is.EqualTo("背包正在整理，请稍候"));
            Assert.That(_client.LastEnhanceOutcome, Is.Null, "失败原因与上一次的成功结果不同时存在");
        }

        [Test]
        public void RejectedEnhanceLeavesNoOutcomeAndFiresNoEvent()
        {
            LoadBoth();
            EnhanceOnce();
            _client.EnhanceEquip(Sword);
            Assert.That(_client.LastEnhanceOutcome, Is.Null, "在途期间没有结果");
            LastEnhance(Sword).Respond(new EnhanceEquipResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipInBattle },
                Success = true, EnhanceLevel = 2, // 被拒绝时这两个字段没有意义,不得采用
            });

            Assert.That(_client.LastEnhanceOutcome, Is.Null, "被拒绝不写结果");
            Assert.That(_outcomes.Count, Is.EqualTo(1), "只有第一次那一条");
            Assert.That(_client.EquipmentError, Is.EqualTo("无法强化:战斗中无法更换装备"));
        }

        [Test]
        public void DisconnectClearsTheOutcomeAndInFlightForge()
        {
            LoadBoth();
            EnhanceOnce();
            _client.EnhanceEquip(Sword);
            LastEnhance(Sword);
            int changes = _changes;
            _net.RaiseDisconnected();

            Assert.That(_changes, Is.GreaterThan(changes));
            Assert.That(_client.LastEnhanceOutcome, Is.Null);
            Assert.That(_client.CharacterBag, Is.Null);
            Assert.That(_client.HasEquipment || _client.BusyEquip, Is.False);
            Assert.That(_client.EquipmentError, Is.Empty);
        }

        [Test]
        public void SwitchingCharacterClearsTheOutcome()
        {
            LoadBoth();
            EnhanceOnce();
            _net.PlayerId = 9202;
            _client.RequestEquipment();
            Assert.That(_client.LastEnhanceOutcome, Is.Null, "上一个角色的强化结果不带到下一个角色");
            Assert.That(_client.CharacterBag, Is.Null);
        }

        [Test]
        public void OutcomeEventIsSkippedWhenAListenerSwitchesCharacterInsideOnChanged()
        {
            LoadBoth();
            _client.EnhanceEquip(Sword);
            var call = LastEnhance(Sword);
            _client.RequestBag(); // 在途期间被挡下的刷新:换了角色之后不该替上一个角色补发
            _client.OnChanged += () =>
            {
                if (!_client.LastEnhanceOutcome.HasValue || _net.PlayerId == 9202) return;
                _net.PlayerId = 9202;
                _client.RequestMissions(); // Calls[3]
            };
            call.Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4, swordLevel: 1), Equipment = StandardEquipment(),
                Success = true, EnhanceLevel = 1,
            });

            Assert.That(_outcomes, Is.Empty, "已经换了角色:不把上一个角色的强化结果发布出去");
            Assert.That(_client.LastEnhanceOutcome, Is.Null);
            Assert.That(BagReadsFrom(3), Is.Empty, "不替上一个角色补发读取");
            Assert.That(_net.Calls.Count, Is.EqualTo(4));
            Assert.That(_client.CharacterBag, Is.Null);
        }

        [Test]
        public void OutcomeEventIsSkippedWhenAListenerStartsAnotherEquipActionInsideOnChanged()
        {
            LoadBoth();
            _client.EnhanceEquip(WornSword);
            var call = LastEnhance(WornSword);
            bool chained = false;
            _client.OnChanged += () =>
            {
                if (chained || !_client.LastEnhanceOutcome.HasValue) return;
                chained = true;
                _client.UnequipItem(WornSword); // 穿脱不受发包间隔约束:这一次发得出去
            };
            call.Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4),
                Equipment = EquipmentBag(WithGem(Weapon(WornSword, 4), 0, AttackGem)), Success = true, EnhanceLevel = 4,
            });

            Assert.That(chained, Is.True);
            Assert.That(Last.MessageId, Is.EqualTo(MessageIds.UnequipItem));
            Assert.That(_client.BusyEquip, Is.True, "新的那一次在途");
            Assert.That(_outcomes, Is.Empty, "新操作已经开始:上一次的结果不再发布");
            Assert.That(_client.LastEnhanceOutcome, Is.Null);
            Assert.That(Find(_client.Equipment, WornSword).EnhanceLevel, Is.EqualTo(4), "快照照样已经换好");
        }

        [Test]
        public void RefreshRequestedDuringForgeIsDeferredSilentlyAndReplayedAfterTheOutcomeEvent()
        {
            LoadBoth();
            _client.EnhanceEquip(Sword); // Calls[2]
            var call = LastEnhance(Sword);
            int changes = _changes;
            _client.RequestBag();
            _client.RequestEquipment();
            Assert.That(_net.Calls.Count, Is.EqualTo(3), "回包自带两份全量:在途期间不发读取");
            Assert.That(_errors, Is.Empty, "也不报错");
            Assert.That(_changes, Is.EqualTo(changes));

            int callsSeenByListener = -1;
            _client.OnEnhanceResult += _ => callsSeenByListener = _net.Calls.Count;
            call.Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4, swordLevel: 1), Equipment = StandardEquipment(),
                Success = true, EnhanceLevel = 1,
            });

            Assert.That(callsSeenByListener, Is.EqualTo(3), "结果先发布,补发的读取在它之后");
            Assert.That(BagReadsFrom(3), Is.EqualTo(new uint[] { 0, 2 }), "被挡下的刷新在回包后补发,各一次");
            Assert.That(_client.BagLoading && _client.EquipmentLoading, Is.True);
            Assert.That(Find(_client.CharacterBag, Sword).EnhanceLevel, Is.EqualTo(1), "补发的读取回来之前先显示回包的那份");
            AssertOutcome(Sword, true, 1);
        }

        [Test]
        public void OlderReadsStayAliveWhileForgeIsInFlightAndCannotOverwriteItsResult()
        {
            LoadBoth();
            _client.RequestBag();       // Calls[2]:强化之前发出的读
            _client.RequestEquipment(); // Calls[3]
            _client.EnhanceEquip(Sword); // Calls[4]
            var call = LastEnhance(Sword);
            Assert.That(_client.BagLoading && _client.EquipmentLoading, Is.True, "在途不作废读取:万一被拒,它们还得落地");

            call.Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4, swordLevel: 1), Equipment = StandardEquipment(),
                Success = true, EnhanceLevel = 1,
            });
            Assert.That(_client.BagLoading || _client.EquipmentLoading, Is.False, "被回包作废的读取不能把加载态留着");
            var bag = _client.CharacterBag;
            var equipment = _client.Equipment;
            int changes = _changes;
            _net.Calls[2].Respond(new GetBagResponse { Bag = StandardBag() });      // 强化之前的旧数据
            _net.Calls[3].Respond(new GetBagResponse { Bag = StandardEquipment() });
            Assert.That(_client.CharacterBag, Is.SameAs(bag), "更旧的读回包不得盖掉写回包(否则金币材料又「回来」了)");
            Assert.That(_client.Equipment, Is.SameAs(equipment));
            Assert.That(_changes, Is.EqualTo(changes));
            Assert.That(_net.Calls.Count, Is.EqualTo(5));
        }

        // ── 旧连接 / 旧角色 / 上一次请求的迟到回包 ───────────

        [TestCase("enhance")]
        [TestCase("inlay")]
        [TestCase("remove")]
        public void ForgeResponseFromAnOlderEpochIsDropped(string kind)
        {
            LoadBoth();
            var stale = Send(kind);
            _net.RaiseDisconnected();

            // 重连后同一角色只重新拉了人物背包:能拦住旧回包的只有 epoch。
            _client.RequestBag();
            LastBagRead(0).Respond(new GetBagResponse { Bag = StandardBag(gold: 7) });
            var bag = _client.CharacterBag;
            int changes = _changes;

            stale.Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000), Equipment = StandardEquipment(), Success = true, EnhanceLevel = 1,
            }); // 三种回包的 bag / equipment 字段号相同,旧回调无论按哪种解析都会拿到这两份
            stale.FailWith("旧会话失败");

            Assert.That(_client.CharacterBag, Is.SameAs(bag));
            Assert.That(_client.HasEquipment, Is.False);
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_client.LastEnhanceOutcome, Is.Null);
            Assert.That(_outcomes, Is.Empty);
            Assert.That(_changes, Is.EqualTo(changes));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void NewCharacterIgnoresThePreviousCharacterForgeResponse()
        {
            LoadBoth();
            var stale = Send("enhance");
            _net.PlayerId = 9202;
            _client.RequestEquipment();

            Assert.That(_client.CharacterBag, Is.Null);
            Assert.That(_client.HasEquipment || _client.BusyEquip, Is.False);
            Assert.That(_client.EquipmentLoading, Is.True);
            var read = LastBagRead(2);
            stale.Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000), Equipment = StandardEquipment(), Success = true, EnhanceLevel = 1,
            });
            Assert.That(_client.CharacterBag, Is.Null);
            Assert.That(_client.HasEquipment, Is.False);
            Assert.That(_outcomes, Is.Empty);

            read.Respond(new GetBagResponse { Bag = EquipmentBag(Weapon(8001)) });
            Assert.That(_client.Equipment.Items[0].ItemId, Is.EqualTo(8001));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void LateCallbackOfAPreviousForgeRequestCannotEndTheCurrentOne()
        {
            LoadBoth();
            var timedOut = Send("enhance");
            timedOut.FailWith("rpc timeout");
            Assert.That(_client.BusyEquip, Is.False);

            _now += PlayerFeaturesClient.ForgeMinIntervalMs;
            _client.EnhanceEquip(Sword);
            LastEnhance(Sword);
            var bag = _client.CharacterBag;
            timedOut.Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000), Equipment = StandardEquipment(), Success = true, EnhanceLevel = 1,
            });
            timedOut.FailWith("更早的旧错误");

            Assert.That(_client.BusyEquip, Is.True, "上一次请求的迟到回调不得结束这一次");
            Assert.That(_client.EquipmentError, Is.Empty);
            Assert.That(_client.CharacterBag, Is.SameAs(bag));
            Assert.That(_client.LastEnhanceOutcome, Is.Null);
            Assert.That(_outcomes, Is.Empty);
        }

        [Test]
        public void CharacterChangeDuringBusyNotificationPreventsSendingAndDoesNotStartTheInterval()
        {
            LoadBoth();
            _client.OnChanged += () =>
            {
                if (!_client.BusyEquip || _net.PlayerId == 9202) return;
                _net.PlayerId = 9202;
                _client.RequestMissions();
            };
            _client.EnhanceEquip(Sword);
            Assert.That(ForgeCallCount(), Is.Zero, "「在途」通知里已经换了角色:旧角色的强化不得发出");
            Assert.That(_client.CharacterBag, Is.Null);
            Assert.That(_client.HasEquipment || _client.BusyEquip, Is.False);

            // 那一次没有发包,所以不起算间隔:新角色此刻(同一毫秒)的操作发得出去。
            _client.RemoveGem(WornSword, 0);
            LastRemove(WornSword, 0);
            Assert.That(ForgeCallCount(), Is.EqualTo(1));
        }

        [Test]
        public void ResponseArrivingAfterDisposeChangesNothing()
        {
            LoadBoth();
            var call = Send("enhance");
            _client.Dispose();
            int changes = _changes;
            call.Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000), Equipment = StandardEquipment(), Success = true, EnhanceLevel = 1,
            });
            Assert.That(_outcomes, Is.Empty);
            Assert.That(_changes, Is.EqualTo(changes));
            Assert.That(_client.LastEnhanceOutcome, Is.Null);
        }

        // ── 互斥 ────────────────────────────────────────────

        [Test]
        public void ForgeSharesTheSingleFlightWithEquipAndExcludesSortAndMissionActions()
        {
            LoadBoth();
            var missions = new GetMissionListResponse();
            missions.Missions.Add(new PlayerMissionInfo { MissionId = 15, CanAccept = true, Configured = true });
            _client.RequestMissions();
            Assert.That(Last.MessageId, Is.EqualTo(SceneMissionClientPlayerGetMissionListHandler.MessageId));
            Last.Respond(missions);

            // 整理在途:强化 / 镶嵌 / 取下报「稍候」,不发包。
            _client.SortBag();
            var sort = Last;
            Assert.That(sort.MessageId, Is.EqualTo(SceneBagClientPlayerSortBagHandler.MessageId));
            Assert.That(sort.Request, Is.TypeOf<SortBagRequest>());
            int sent = _net.Calls.Count;
            _client.EnhanceEquip(Sword);
            _client.InlayGem(Sword, 1, AttackGem);
            _client.RemoveGem(WornSword, 0);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_client.EquipmentError, Is.EqualTo("背包正在整理，请稍候"));
            Assert.That(_errors.Count, Is.EqualTo(3));
            sort.Respond(new SortBagResponse { Bag = StandardBag() });

            // 强化在途:穿脱与另外两种操作都是连点(不发、不报错);整理与任务操作报「稍候」。
            _client.EnhanceEquip(Sword);
            var enhance = LastEnhance(Sword);
            sent = _net.Calls.Count;
            int errors = _errors.Count;
            _client.EquipItem(Sword);
            _client.UnequipItem(WornSword);
            _client.EnhanceEquip(WornSword);
            _client.InlayGem(Sword, 1, AttackGem);
            _client.RemoveGem(WornSword, 0);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
            Assert.That(_errors.Count, Is.EqualTo(errors), "五种装备类操作共用同一个在途标志:连点不弹错");
            _client.SortBag();
            _client.AcceptMission(0, 15);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
            Assert.That(_client.BusySort || _client.BusyMissionAction, Is.False);
            Assert.That(_client.BagError, Is.Not.Empty);
            Assert.That(_client.MissionsError, Is.Not.Empty);
            enhance.Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4, swordLevel: 1), Equipment = StandardEquipment(),
                Success = true, EnhanceLevel = 1,
            });
            Assert.That(_client.BusyEquip, Is.False);

            // 穿脱在途:强化 / 镶嵌 / 取下同样只是连点。
            _now += PlayerFeaturesClient.ForgeMinIntervalMs;
            _client.UnequipItem(WornSword);
            var unequip = Last;
            Assert.That(unequip.MessageId, Is.EqualTo(MessageIds.UnequipItem));
            Assert.That(unequip.Request, Is.TypeOf<UnequipItemRequest>());
            sent = _net.Calls.Count;
            errors = _errors.Count;
            _client.EnhanceEquip(Sword);
            _client.InlayGem(Sword, 1, AttackGem);
            _client.RemoveGem(WornSword, 0);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
            Assert.That(_errors.Count, Is.EqualTo(errors));
            unequip.Respond(new UnequipItemResponse
            {
                Bag = CharacterBag(49000, Weapon(Sword, 1), WithGem(Weapon(WornSword, 3), 0, AttackGem), Stack(6001, Stone, 4),
                    Stack(6002, AttackGem, 2, gemType: 1)),
                Equipment = EquipmentBag(),
            });
            Assert.That(_client.BusyEquip, Is.False);

            // 任务操作在途:强化 / 镶嵌 / 取下报「稍候」,不发包。
            _client.AcceptMission(0, 15);
            Assert.That(_client.BusyMissionAction, Is.True);
            sent = _net.Calls.Count;
            _client.InlayGem(Sword, 1, AttackGem);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_client.EquipmentError, Is.EqualTo("任务正在处理中，请稍候"));
        }

        [TestCase(false, 9101ul)]
        [TestCase(true, 0ul)]
        public void MissingGateOrUnselectedCharacterSendsNoForgeRpc(bool ready, ulong playerId)
        {
            _net.IsReady = ready;
            _net.PlayerId = playerId;
            _client.EnhanceEquip(Sword);
            _client.InlayGem(Sword, 0, AttackGem);
            _client.RemoveGem(WornSword, 0);
            Assert.That(_net.Calls, Is.Empty);
            Assert.That(_errors.Count, Is.EqualTo(3));
            Assert.That(_errors.Distinct().Count(), Is.EqualTo(1), "三次都是同一句「尚未进入游戏」");
            Assert.That(_client.BusyEquip, Is.False);
            Assert.That(_client.LastEnhanceOutcome, Is.Null);
        }
    }
}
