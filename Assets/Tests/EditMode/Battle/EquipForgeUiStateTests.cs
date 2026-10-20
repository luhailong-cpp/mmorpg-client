using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI.Ugui.Gameplay;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 强化 / 镶嵌面板的按钮逻辑(EquipForgeUiState):每个按钮能不能点、不能点的原因、点了发什么、选宝石与确认降级两步、
    /// 状态行与结果提示、发包间隔的置灰与 Tick、换绑与断线。数据层用正式的 PlayerFeaturesClient + 假传输,不碰 UnityEngine。
    /// 数据层自己的行为(本地拒绝、重拉、互斥……)在 <see cref="EquipForgeClientTests"/>,这里只验面板这一层。
    /// </summary>
    public sealed class EquipForgeUiStateTests
    {
        private const ulong Sword = 5001;      // 行囊里的武器:+0,0 号攻击孔(只接类型 1)、1 号通用孔,都空
        private const ulong WornSword = 7001;  // 穿着的武器:+3,0 号孔嵌着攻击宝石
        private const ulong Nowhere = 4040;    // 两份快照里都没有
        private const uint Stone = 2001;       // 强化石
        private const uint AttackGem = 2101;   // 攻击宝石(类型 1)
        private const uint DefenseGem = 2201;  // 防御宝石(类型 2)

        private FakeBattleTransport _net;
        private PlayerFeaturesClient _client;
        private EquipForgeUiState _ui;
        private List<EquipForgeNotice> _notices;
        private int _changes;
        private long _now;

        [SetUp]
        public void SetUp()
        {
            _net = new FakeBattleTransport { PlayerId = 9101, IsReady = true };
            _client = new PlayerFeaturesClient(_net);
            _now = 100000;
            _client.NowMs = () => _now;
            _ui = new EquipForgeUiState();
            _ui.Bind(_client);
            _notices = new List<EquipForgeNotice>();
            _changes = 0;
            _ui.Changed += () => ++_changes;
            _ui.Announced += _notices.Add;
        }

        [TearDown]
        public void TearDown()
        {
            _ui.Dispose();
            _client.Dispose();
        }

        private EquipForgeView View => _ui.View;
        private FakeBattleTransport.RecordedCall Last => _net.Calls[_net.Calls.Count - 1];
        private int ForgeCalls => _net.CallsOf(MessageIds.EnhanceEquip).Count + _net.CallsOf(MessageIds.InlayGem).Count
            + _net.CallsOf(MessageIds.RemoveGem).Count;

        // ── 夹具 ────────────────────────────────────────────

        /// <summary>
        /// 一把武器:+level / 上限 maxLevel;没满级时带下一级预览(银两 cost、强化石 ×stones、成功率 rate、失败降 failLoss 级)。
        /// 两个孔:0 号攻击孔(只接类型 1)、1 号通用孔(任意类型),都空。
        /// </summary>
        private static BagItemInfo Weapon(ulong id, uint level = 0, uint maxLevel = 12, ulong cost = 1000, uint stones = 1,
            uint rate = 10000, uint failLoss = 0)
        {
            var item = new BagItemInfo
            {
                ItemId = id, ConfigId = 1101, Count = 1, MaxStack = 1, EquipKind = 11, Name = "青铜剑", IconKey = "sword_1101",
                EnhanceLevel = level, EnhanceMaxLevel = maxLevel,
            };
            item.BaseAttrs.Add(new EquipAttrLineInfo { AttrId = 1, Name = "伤害", Tier = 0, Value = 40 + level * 2, EnhanceNextValue = 42 + level * 2 });
            if (maxLevel != 0 && level < maxLevel)
                item.EnhanceNext = new EquipEnhancePreview
                {
                    NextLevel = level + 1, SuccessRate = rate, CostGold = cost, FailLevelLoss = failLoss,
                    MaterialConfigId = stones == 0 ? 0u : Stone, MaterialName = stones == 0 ? string.Empty : "强化石",
                    MaterialCount = stones,
                };
            item.GemSockets.Add(new EquipGemSocketInfo { Socket = 0, GemType = 1, Name = "攻击孔" });
            item.GemSockets.Add(new EquipGemSocketInfo { Socket = 1, GemType = 0, Name = "通用孔" });
            return item;
        }

        /// <summary>把攻击宝石嵌进某个孔;active = false 时不带属性(已镶但不生效)。</summary>
        private static BagItemInfo WithGem(BagItemInfo weapon, uint socket, bool active = true)
        {
            var hole = weapon.GemSockets.First(entry => entry.Socket == socket);
            hole.GemConfigId = AttackGem;
            hole.GemName = "攻击宝石·一级";
            hole.GemIconKey = "gem_2101";
            hole.Attr = active ? new EquipAttrLineInfo { AttrId = 1, Name = "伤害", Value = 30 } : null;
            return weapon;
        }

        private static BagItemInfo Stack(ulong id, uint configId, uint count)
            => new BagItemInfo { ItemId = id, ConfigId = configId, Count = count, MaxStack = 999, Name = "强化石" };

        private static BagItemInfo Gem(ulong id, uint configId, uint count, uint gemType)
            => new BagItemInfo
            {
                ItemId = id, ConfigId = configId, Count = count, MaxStack = 999, GemType = gemType,
                Name = gemType == 1 ? "攻击宝石·一级" : "防御宝石·一级", IconKey = $"gem_{configId}",
                GemAttr = new EquipAttrLineInfo { AttrId = gemType, Name = gemType == 1 ? "伤害" : "防御", Value = 30 },
            };

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

        /// <summary>标准人物背包:银两、武器 5001、强化石一堆、攻击宝石一堆;数量为 0 的那一堆不放。</summary>
        private static BagInfo StandardBag(ulong? gold = 50000, uint stones = 5, uint attackGems = 2, BagItemInfo sword = null)
        {
            var items = new List<BagItemInfo> { sword ?? Weapon(Sword) };
            if (stones != 0) items.Add(Stack(6001, Stone, stones));
            if (attackGems != 0) items.Add(Gem(6002, AttackGem, attackGems, 1));
            return CharacterBag(gold, items.ToArray());
        }

        private static BagInfo StandardEquipment() => EquipmentBag(WithGem(Weapon(WornSword, 3), 0));

        private void Load(BagInfo bag, BagInfo equipment)
        {
            if (bag != null)
            {
                _client.RequestBag();
                RespondBagRead(0, bag);
            }
            if (equipment != null)
            {
                _client.RequestEquipment();
                RespondBagRead(2, equipment);
            }
        }

        private void RespondBagRead(uint bagType, BagInfo bag)
        {
            var call = Last;
            Assert.That(call.MessageId, Is.EqualTo(SceneBagClientPlayerRpc.GetBagMessageId), "最近一次出站调用不是读背包");
            Assert.That(((GetBagRequest)call.Request).BagType, Is.EqualTo(bagType));
            call.Respond(new GetBagResponse { Bag = bag });
        }

        private void LoadBoth() => Load(StandardBag(), StandardEquipment());

        private FakeBattleTransport.RecordedCall LastEnhance(ulong itemId)
        {
            Assert.That(Last.MessageId, Is.EqualTo(MessageIds.EnhanceEquip), "最近一次出站调用不是强化");
            Assert.That(((EnhanceEquipRequest)Last.Request).ItemId, Is.EqualTo(itemId));
            return Last;
        }

        private static EquipForgeSocketRow Row(EquipForgeView view, uint socket) => view.Sockets.Single(row => row.Socket == socket);

        private static void AssertButton(EquipForgeButton button, string label, bool enabled, string hint = "")
        {
            Assert.That(button.Label, Is.EqualTo(label));
            Assert.That(button.Enabled, Is.EqualTo(enabled), $"「{label}」可点 = {enabled}");
            Assert.That(button.Hint, Is.EqualTo(hint));
        }

        // ── 打开与呈现 ───────────────────────────────────────

        [Test]
        public void OpeningWithBothSnapshotsSendsNothingAndRendersEveryBlock()
        {
            LoadBoth();
            int calls = _net.Calls.Count;
            _ui.Open(Sword);

            Assert.That(_net.Calls.Count, Is.EqualTo(calls), "背包页开着时两份快照都在:打开面板不发任何请求");
            Assert.That(_ui.IsOpen, Is.True);
            Assert.That(_ui.TargetItemId, Is.EqualTo(Sword));
            Assert.That(_changes, Is.GreaterThan(0));
            Assert.That(View.Stage, Is.EqualTo(EquipForgeStage.Ready));
            Assert.That(View.ItemId, Is.EqualTo(Sword));
            Assert.That(View.StageText, Is.Empty);
            Assert.That(View.Title, Is.EqualTo("青铜剑"), "0 级不带后缀");
            Assert.That(View.IconKey, Is.EqualTo("sword_1101"));
            Assert.That(View.Worn, Is.False);
            Assert.That(View.LocationText, Is.EqualTo("行囊中"));

            Assert.That(View.EnhanceLevelText, Is.EqualTo("强化等级 +0 / +12"));
            Assert.That(View.HasNextLevel, Is.True);
            Assert.That(View.SuccessRateText, Is.EqualTo("成功率 100%"));
            Assert.That(View.CostText, Is.EqualTo("消耗 银两 1000、强化石 ×1"));
            Assert.That(View.OwnedText, Is.EqualTo("持有 银两 50000、强化石 ×5"));
            Assert.That(View.FailRuleText, Is.EqualTo("失败不降级"));
            Assert.That(View.PreviewLines, Is.EqualTo(new[] { "伤害：40 → 42" }));
            Assert.That(View.EnhanceCheck, Is.EqualTo(EquipEnhanceCheck.None));
            AssertButton(View.EnhanceButton, "强化", true);

            Assert.That(View.Sockets.Select(row => row.Socket), Is.EqualTo(new uint[] { 0, 1 }));
            Assert.That(Row(View, 0).Text, Is.EqualTo("攻击孔：未镶嵌"));
            Assert.That(Row(View, 0).Kind, Is.EqualTo(EquipForgeSocketKind.Empty));
            Assert.That(Row(View, 0).IsRemove, Is.False);
            Assert.That(Row(View, 0).ChoiceCount, Is.EqualTo(1));
            Assert.That(Row(View, 0).GemIconKey, Is.Empty);
            AssertButton(Row(View, 0).Button, "镶嵌", true);
            AssertButton(Row(View, 1).Button, "镶嵌", true);
            Assert.That(View.SocketsEmptyText, Is.Empty);

            Assert.That(View.Picker, Is.Null);
            Assert.That(View.Confirm, Is.Null);
            Assert.That(View.StatusText, Is.Empty);
            Assert.That(View.StatusTone, Is.EqualTo(EquipForgeTone.Normal));
            Assert.That(View.Busy, Is.False);
            Assert.That(View.CoolingDown, Is.False);
            AssertButton(View.RefreshButton, "刷新", true);
        }

        [Test]
        public void WornItemShowsItsLevelLocationAndARemoveButtonOnTheFilledSocket()
        {
            LoadBoth();
            _ui.Open(WornSword);

            Assert.That(View.Title, Is.EqualTo("青铜剑 +3"));
            Assert.That(View.Worn, Is.True);
            Assert.That(View.LocationText, Is.EqualTo("已穿戴"));
            Assert.That(View.EnhanceLevelText, Is.EqualTo("强化等级 +3 / +12"));
            var filled = Row(View, 0);
            Assert.That(filled.Kind, Is.EqualTo(EquipForgeSocketKind.Active));
            Assert.That(filled.Text, Is.EqualTo("攻击孔：攻击宝石·一级（伤害 +30）"));
            Assert.That(filled.GemIconKey, Is.EqualTo("gem_2101"));
            Assert.That(filled.IsRemove, Is.True);
            Assert.That(filled.ChoiceCount, Is.EqualTo(0));
            AssertButton(filled.Button, "取下", true);
            Assert.That(Row(View, 1).Kind, Is.EqualTo(EquipForgeSocketKind.Empty));
        }

        [Test]
        public void ItemWithoutSocketsSaysSo()
        {
            var plain = Weapon(Sword);
            plain.GemSockets.Clear();
            Load(StandardBag(sword: plain), StandardEquipment());
            _ui.Open(Sword);

            Assert.That(View.Sockets, Is.Empty);
            Assert.That(View.SocketsEmptyText, Is.EqualTo("这件装备没有宝石孔"));
        }

        [Test]
        public void OpeningRequestsOnlyMissingSnapshotsAndShowsLoadingUntilBothArrive()
        {
            _ui.Open(Sword);
            Assert.That(_net.Calls.Select(call => ((GetBagRequest)call.Request).BagType), Is.EqualTo(new uint[] { 0, 2 }),
                "两份都没有:各拉一次");
            Assert.That(View.Stage, Is.EqualTo(EquipForgeStage.Loading));
            Assert.That(View.StageText, Is.EqualTo("正在同步行囊与装备栏…"));
            AssertButton(View.EnhanceButton, "强化", false);
            Assert.That(View.Sockets, Is.Empty);
            Assert.That(View.RefreshButton.Enabled, Is.False, "读取在途时不给刷新");

            _net.Calls[0].Respond(new GetBagResponse { Bag = StandardBag() });
            Assert.That(View.Stage, Is.EqualTo(EquipForgeStage.Loading), "还差装备栏");
            _net.Calls[1].Respond(new GetBagResponse { Bag = StandardEquipment() });
            Assert.That(View.Stage, Is.EqualTo(EquipForgeStage.Ready));
            Assert.That(View.EnhanceButton.Enabled, Is.True);

            // 只缺装备栏时只拉装备栏。
            _ui.Close();
            _net.RaiseDisconnected();
            Load(StandardBag(), null);
            int calls = _net.Calls.Count;
            _ui.Open(Sword);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls + 1));
            Assert.That(((GetBagRequest)Last.Request).BagType, Is.EqualTo(2u));
        }

        [Test]
        public void FailedReadOffersARetryThatRequestsBothSnapshots()
        {
            _ui.Open(Sword);
            _net.Calls[0].FailWith("timeout");
            _net.Calls[1].FailWith("timeout");
            Assert.That(View.Stage, Is.EqualTo(EquipForgeStage.Loading));
            Assert.That(View.StageText, Is.EqualTo("行囊或装备栏暂未同步，请点击刷新重试"));
            AssertButton(View.RefreshButton, "刷新", true);

            int calls = _net.Calls.Count;
            _ui.Refresh();
            Assert.That(_net.Calls.Skip(calls).Select(call => ((GetBagRequest)call.Request).BagType), Is.EqualTo(new uint[] { 0, 2 }));
            Assert.That(View.StageText, Is.EqualTo("正在同步行囊与装备栏…"));
        }

        [Test]
        public void TargetThatIsInNeitherSnapshotIsReportedMissingWithEverythingDisabled()
        {
            LoadBoth();
            _ui.Open(Nowhere);

            Assert.That(View.Stage, Is.EqualTo(EquipForgeStage.Missing));
            Assert.That(View.StageText, Is.EqualTo("这件装备已不在行囊或装备栏中"));
            Assert.That(View.EnhanceButton.Enabled, Is.False);
            Assert.That(View.Sockets, Is.Empty);
            _ui.Enhance();
            _ui.BeginInlay(0);
            _ui.RemoveGem(0);
            Assert.That(ForgeCalls, Is.EqualTo(0));
        }

        [Test]
        public void OpenWithoutAClientOrWithZeroIdDoesNothing()
        {
            var unbound = new EquipForgeUiState();
            int changes = 0;
            unbound.Changed += () => ++changes;
            unbound.Open(Sword);
            Assert.That(unbound.IsOpen, Is.False);
            Assert.That(unbound.View, Is.SameAs(EquipForgeView.Closed));
            Assert.That(changes, Is.EqualTo(0));

            _ui.Open(0);
            Assert.That(_ui.IsOpen, Is.False);
            Assert.That(_net.Calls, Is.Empty);
        }

        [Test]
        public void ClosingResetsTheViewAndRaisesChangedOnce()
        {
            LoadBoth();
            _ui.Open(Sword);
            _changes = 0;
            _ui.Close();
            Assert.That(_ui.IsOpen, Is.False);
            Assert.That(View, Is.SameAs(EquipForgeView.Closed));
            Assert.That(View.Stage, Is.EqualTo(EquipForgeStage.Closed));
            Assert.That(View.EnhanceButton.Label, Is.EqualTo("强化"), "关着的快照按钮也有文字,不是 null");
            Assert.That(_changes, Is.EqualTo(1));
            _ui.Close();
            Assert.That(_changes, Is.EqualTo(1), "已经关着:不再通知");
        }

        // ── 强化按钮的置灰原因 ───────────────────────────────

        [TestCase("max", "已强化到最高等级", TestName = "EnhanceGreyedAtMaxLevel")]
        [TestCase("closed", "该装备无法强化", TestName = "EnhanceGreyedWhenEnhanceIsNotOpen")]
        [TestCase("noPreview", "该装备无法强化", TestName = "EnhanceGreyedWithoutANextLevelPreview")]
        [TestCase("material", "强化材料不足", TestName = "EnhanceGreyedWhenMaterialIsShort")]
        [TestCase("gold", "银两不足", TestName = "EnhanceGreyedWhenSilverIsShort")]
        public void EnhanceButtonGreysOutWithTheRuleReason(string situation, string hint)
        {
            BagInfo bag = situation switch
            {
                "max" => StandardBag(sword: Weapon(Sword, level: 12, maxLevel: 12)),
                "closed" => StandardBag(sword: Weapon(Sword, maxLevel: 0)),
                "noPreview" => StandardBag(sword: WithoutPreview(Weapon(Sword))),
                "material" => StandardBag(stones: 0),
                _ => StandardBag(gold: 500),
            };
            Load(bag, StandardEquipment());
            _ui.Open(Sword);

            AssertButton(View.EnhanceButton, "强化", false, hint);
            int calls = _net.Calls.Count;
            _ui.Enhance();
            Assert.That(_net.Calls.Count, Is.EqualTo(calls), "置灰的按钮被点到也不发包");
            Assert.That(View.Confirm, Is.Null);
            if (situation == "closed") Assert.That(View.EnhanceLevelText, Is.EqualTo("该装备不可强化"));
            bool hasNext = situation == "material" || situation == "gold";
            Assert.That(View.HasNextLevel, Is.EqualTo(hasNext));
            if (!hasNext)
            {
                Assert.That(View.SuccessRateText, Is.Empty);
                Assert.That(View.CostText, Is.Empty);
                Assert.That(View.PreviewLines, Is.Empty);
            }
        }

        private static BagItemInfo WithoutPreview(BagItemInfo item)
        {
            item.EnhanceNext = null;
            return item;
        }

        [Test]
        public void UnknownSilverBalanceDoesNotGreyOutAndShowsADash()
        {
            Load(StandardBag(gold: null), StandardEquipment());
            _ui.Open(Sword);

            Assert.That(View.EnhanceCheck, Is.EqualTo(EquipEnhanceCheck.Unknown));
            AssertButton(View.EnhanceButton, "强化", true);
            Assert.That(View.OwnedText, Is.EqualTo("持有 银两 —、强化石 ×5"));
        }

        [Test]
        public void CostWithoutMaterialListsOnlySilver()
        {
            Load(StandardBag(sword: Weapon(Sword, stones: 0)), StandardEquipment());
            _ui.Open(Sword);
            Assert.That(View.CostText, Is.EqualTo("消耗 银两 1000"));
            Assert.That(View.OwnedText, Is.EqualTo("持有 银两 50000"));
        }

        // ── 强化:发出、在途、结果 ───────────────────────────

        [Test]
        public void EnhanceSendsOnceShowsBusyThenTheOutcomeAndAnnouncesIt()
        {
            LoadBoth();
            _ui.Open(Sword);
            _ui.Enhance();
            var call = LastEnhance(Sword);

            Assert.That(View.Busy, Is.True);
            Assert.That(View.StatusText, Is.EqualTo("正在强化…"));
            Assert.That(View.StatusTone, Is.EqualTo(EquipForgeTone.Busy));
            AssertButton(View.EnhanceButton, "强化", false, "正在强化…");
            AssertButton(Row(View, 0).Button, "镶嵌", false, "正在强化…");
            Assert.That(View.RefreshButton.Enabled, Is.False);
            int sent = ForgeCalls;
            _ui.Enhance();
            Assert.That(ForgeCalls, Is.EqualTo(sent), "在途时再点:不发");

            call.Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4, sword: Weapon(Sword, 1)), Equipment = StandardEquipment(),
                Success = true, EnhanceLevel = 1,
            });
            Assert.That(View.Busy, Is.False);
            Assert.That(View.Title, Is.EqualTo("青铜剑 +1"), "快照换成服务器带回来的");
            Assert.That(View.OwnedText, Is.EqualTo("持有 银两 49000、强化石 ×4"));
            Assert.That(View.StatusText, Is.EqualTo("强化成功：青铜剑 +1"));
            Assert.That(View.StatusTone, Is.EqualTo(EquipForgeTone.Success));
            Assert.That(_notices.Count, Is.EqualTo(1));
            Assert.That(_notices[0].ItemId, Is.EqualTo(Sword));
            Assert.That(_notices[0].Success, Is.True);
            Assert.That(_notices[0].Level, Is.EqualTo(1u));
            Assert.That(_notices[0].Text, Is.EqualTo("强化成功：青铜剑 +1"));
        }

        [Test]
        public void RollFailureIsAFailureToneNotAnError()
        {
            LoadBoth();
            _ui.Open(Sword);
            _ui.Enhance();
            LastEnhance(Sword).Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4), Equipment = StandardEquipment(), Success = false, EnhanceLevel = 0,
            });

            Assert.That(View.StatusText, Is.EqualTo("强化未成功：青铜剑 仍为 +0，消耗已扣除"));
            Assert.That(View.StatusTone, Is.EqualTo(EquipForgeTone.Failure));
            Assert.That(_notices.Single().Success, Is.False);
        }

        [Test]
        public void ServerRejectionShowsTheReasonAsAnErrorAndAnnouncesNothing()
        {
            LoadBoth();
            _ui.Open(Sword);
            _ui.Enhance();
            LastEnhance(Sword).Respond(new EnhanceEquipResponse
            { ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipEnhanceGoldNotEnough } });

            Assert.That(View.StatusText, Is.EqualTo("无法强化:银两不足,无法强化"));
            Assert.That(View.StatusTone, Is.EqualTo(EquipForgeTone.Error));
            Assert.That(_notices, Is.Empty);
        }

        [Test]
        public void TransportFailureIsShownAndLeavesNoBusyLabel()
        {
            LoadBoth();
            _ui.Open(Sword);
            _ui.Enhance();
            LastEnhance(Sword).FailWith("timeout");

            Assert.That(View.Busy, Is.False);
            Assert.That(View.StatusText, Is.EqualTo("无法强化:网络异常,请稍后重试"));
            Assert.That(View.StatusTone, Is.EqualTo(EquipForgeTone.Error));
        }

        [Test]
        public void TransportThatFailsInsideTheCallLeavesNoBusyLabel()
        {
            // 真实传输在没连上时会在 Call 内部同步回调失败:调用返回时在途已经结束。
            var transport = new SyncFailingTransport { FailNow = MessageIds.EnhanceEquip };
            var client = new PlayerFeaturesClient(transport) { NowMs = () => _now };
            var ui = new EquipForgeUiState();
            ui.Bind(client);
            client.RequestBag();
            transport.Inner.Calls[0].Respond(new GetBagResponse { Bag = StandardBag() });
            client.RequestEquipment();
            transport.Inner.Calls[1].Respond(new GetBagResponse { Bag = StandardEquipment() });
            ui.Open(Sword);

            ui.Enhance();
            Assert.That(transport.SyncFailures, Is.EqualTo(1));
            Assert.That(ui.View.Busy, Is.False);
            Assert.That(ui.View.StatusText, Is.EqualTo("无法强化:网络异常,请稍后重试"));
            Assert.That(ui.View.StatusTone, Is.EqualTo(EquipForgeTone.Error));
            ui.Dispose();
            client.Dispose();
        }

        [TestCase(12u, 12u, "已强化到最高等级", TestName = "DirtyPreviewAtMaxLevelShowsNoNextLevel")]
        [TestCase(0u, 0u, "该装备无法强化", TestName = "DirtyPreviewWithEnhanceClosedShowsNoNextLevel")]
        public void DirtyPreviewIsIgnoredAndForeignRejectionsAreNotThePanelsError(uint level, uint maxLevel, string hint)
        {
            // 满级 / 未开放却还挂着下一级预览的脏数据:不显示成功率与消耗,按钮按规则挡住。
            var odd = Weapon(Sword, level: level, maxLevel: maxLevel);
            odd.EnhanceNext = new EquipEnhancePreview { NextLevel = level + 1, SuccessRate = 10000, CostGold = 1 };
            Load(StandardBag(sword: odd), StandardEquipment());
            _ui.Open(Sword);
            AssertButton(View.EnhanceButton, "强化", false, hint);
            Assert.That(View.HasNextLevel, Is.False);
            Assert.That(View.SuccessRateText, Is.Empty);
            Assert.That(View.CostText, Is.Empty);
            Assert.That(View.FailRuleText, Is.Empty);

            // 别处直接调数据层被本地拒绝:那不是这个面板发出的操作,状态行不认领。
            _client.EnhanceEquip(Sword);
            Assert.That(_client.EquipmentError, Is.Not.Empty, "前置:数据层确实报了错");
            Assert.That(View.StatusText, Is.Empty);
        }

        [Test]
        public void PanelOperationRejectedBeforeSendingShowsTheReason()
        {
            LoadBoth();
            _ui.Open(Sword);
            _net.IsReady = false; // 掉线:快照还在,面板仍是 Ready,数据层在发包前拒绝
            _ui.Enhance();

            Assert.That(ForgeCalls, Is.EqualTo(0));
            Assert.That(View.StatusText, Is.EqualTo("尚未进入游戏，请进入角色后再试"));
            Assert.That(View.StatusTone, Is.EqualTo(EquipForgeTone.Error));
        }

        [Test]
        public void ErrorsLeftOverFromBeforeOpeningAreNotShown()
        {
            LoadBoth();
            _client.InlayGem(Sword, 9, AttackGem); // 背包页上的一次本地拒绝,发生在打开面板之前
            Assert.That(_client.EquipmentError, Is.Not.Empty);
            _ui.Open(Sword);

            Assert.That(View.StatusText, Is.Empty, "不是这个面板里发生的事");
            Assert.That(View.StatusTone, Is.EqualTo(EquipForgeTone.Normal));
        }

        [Test]
        public void NonEquipmentErrorsDoNotReplaceTheEquipmentReason()
        {
            LoadBoth();
            _ui.Open(Sword);
            _ui.Enhance();
            LastEnhance(Sword).Respond(new EnhanceEquipResponse
            { ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipInBattle } });
            string reason = View.StatusText;
            Assert.That(reason, Is.Not.Empty);

            _client.RequestMissions();
            Last.FailWith("timeout");
            Assert.That(View.StatusText, Is.EqualTo(reason), "读任务失败不是这个面板的事");
        }

        [Test]
        public void TheNextOperationReplacesThePreviousOutcome()
        {
            LoadBoth();
            _ui.Open(Sword);
            _ui.Enhance();
            LastEnhance(Sword).Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4, sword: Weapon(Sword, 1)), Equipment = StandardEquipment(),
                Success = true, EnhanceLevel = 1,
            });
            Assert.That(View.StatusTone, Is.EqualTo(EquipForgeTone.Success));
            _now += PlayerFeaturesClient.ForgeMinIntervalMs;
            _ui.Tick();

            _ui.BeginInlay(1);
            _ui.ChooseGem(AttackGem);
            Assert.That(View.StatusText, Is.EqualTo("正在镶嵌…"));
            Last.Respond(new InlayGemResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4, attackGems: 1, sword: WithGem(Weapon(Sword, 1), 1)),
                Equipment = StandardEquipment(),
            });
            Assert.That(View.StatusText, Is.Empty, "镶嵌成功之后,「强化成功」那一句不再挂着");
            Assert.That(View.StatusTone, Is.EqualTo(EquipForgeTone.Normal));
            Assert.That(Row(View, 1).IsRemove, Is.True);
        }

        [Test]
        public void TheNextOperationReplacesThePreviousError()
        {
            LoadBoth();
            _ui.Open(Sword);
            _ui.Enhance();
            LastEnhance(Sword).Respond(new EnhanceEquipResponse
            { ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipInBattle } });
            Assert.That(View.StatusTone, Is.EqualTo(EquipForgeTone.Error));
            _now += PlayerFeaturesClient.ForgeMinIntervalMs;
            _ui.Tick();

            _ui.BeginInlay(1);
            _ui.ChooseGem(AttackGem);
            Assert.That(View.StatusText, Is.EqualTo("正在镶嵌…"));
            Assert.That(View.StatusTone, Is.EqualTo(EquipForgeTone.Busy));
            Last.Respond(new InlayGemResponse
            {
                Bag = StandardBag(attackGems: 1, sword: WithGem(Weapon(Sword), 1)), Equipment = StandardEquipment(),
            });
            Assert.That(View.StatusText, Is.Empty);
        }

        [Test]
        public void ResultIsAnnouncedEvenWhenThePanelWasClosedMeanwhile()
        {
            LoadBoth();
            _ui.Open(Sword);
            _ui.Enhance();
            var call = LastEnhance(Sword);
            _ui.Close();
            call.Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4, sword: Weapon(Sword, 1)), Equipment = StandardEquipment(),
                Success = true, EnhanceLevel = 1,
            });

            Assert.That(_notices.Single().Text, Is.EqualTo("强化成功：青铜剑 +1"));
            Assert.That(View, Is.SameAs(EquipForgeView.Closed), "关着就保持关着");
        }

        [Test]
        public void ResultsOfEnhancesNotSentByThePanelAreNotAnnounced()
        {
            LoadBoth();
            _ui.Open(Sword);
            _client.EnhanceEquip(Sword); // 别处直接调数据层
            LastEnhance(Sword).Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4, sword: Weapon(Sword, 1)), Equipment = StandardEquipment(),
                Success = true, EnhanceLevel = 1,
            });
            Assert.That(_notices, Is.Empty);
            Assert.That(View.Title, Is.EqualTo("青铜剑 +1"), "快照照常更新");
        }

        // ── 会降级的强化:先确认 ─────────────────────────────

        [Test]
        public void DegradingEnhanceAsksForConfirmationBeforeSending()
        {
            Load(StandardBag(sword: Weapon(Sword, level: 5, rate: 7500, failLoss: 1)), StandardEquipment());
            _ui.Open(Sword);
            Assert.That(View.FailRuleText, Is.EqualTo("失败降 1 级"));

            _ui.Enhance();
            Assert.That(ForgeCalls, Is.EqualTo(0), "先确认,不发包");
            var confirm = View.Confirm;
            Assert.That(confirm, Is.Not.Null);
            Assert.That(confirm.Title, Is.EqualTo("确认强化"));
            Assert.That(confirm.Message, Is.EqualTo("成功率 75%，消耗 银两 1000、强化石 ×1。\n失败会降 1 级（+5 → +4），消耗不退还。"));
            Assert.That(confirm.ConfirmLabel, Is.EqualTo("确认强化"));
            Assert.That(confirm.CancelLabel, Is.EqualTo("取消"));
            Assert.That(confirm.CanConfirm, Is.True);

            _ui.CancelConfirm();
            Assert.That(View.Confirm, Is.Null);
            Assert.That(ForgeCalls, Is.EqualTo(0));

            _ui.Enhance();
            _ui.ConfirmEnhance();
            LastEnhance(Sword).Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(sword: Weapon(Sword, level: 4, rate: 7500, failLoss: 1)), Equipment = StandardEquipment(),
                Success = false, EnhanceLevel = 4,
            });
            Assert.That(View.Confirm, Is.Null);
            Assert.That(View.StatusText, Is.EqualTo("强化未成功：青铜剑 降为 +4，消耗已扣除"));
            Assert.That(_notices.Single().Level, Is.EqualTo(4u));
        }

        [Test]
        public void ConfirmWithoutAPendingConfirmationDoesNothing()
        {
            Load(StandardBag(sword: Weapon(Sword, level: 5, failLoss: 1)), StandardEquipment());
            _ui.Open(Sword);
            _ui.ConfirmEnhance();
            Assert.That(ForgeCalls, Is.EqualTo(0));
        }

        [Test]
        public void ConfirmationIsDroppedWhenTheNextLevelNoLongerDegrades()
        {
            Load(StandardBag(sword: Weapon(Sword, level: 5, failLoss: 1)), StandardEquipment());
            _ui.Open(Sword);
            _ui.Enhance();
            Assert.That(View.Confirm, Is.Not.Null);

            _client.RequestBag();
            RespondBagRead(0, StandardBag(sword: Weapon(Sword, level: 5, failLoss: 0)));
            Assert.That(View.Confirm, Is.Null, "规则变成不降级:确认这一步收起");
            _ui.ConfirmEnhance();
            Assert.That(ForgeCalls, Is.EqualTo(0));
        }

        [Test]
        public void ConfirmationStaysButCannotBeConfirmedWhileAnotherOperationIsInFlight()
        {
            Load(StandardBag(sword: Weapon(Sword, level: 5, failLoss: 1)), StandardEquipment());
            _ui.Open(Sword);
            _ui.Enhance();
            _client.UnequipItem(WornSword); // 背包页上的一次卸下,在途

            Assert.That(View.Confirm, Is.Not.Null);
            Assert.That(View.Confirm.CanConfirm, Is.False);
            Assert.That(View.Confirm.Hint, Is.EqualTo("正在处理装备…"));
            _ui.ConfirmEnhance();
            Assert.That(ForgeCalls, Is.EqualTo(0));
            Assert.That(View.Confirm, Is.Not.Null, "不能确认时确认框保持打开");
        }

        // ── 镶嵌:选宝石 ─────────────────────────────────────

        [Test]
        public void InlayPickerListsFittingGemsByKindAndSendsTheChosenConfig()
        {
            var bag = CharacterBag(50000, Weapon(Sword), Gem(6002, AttackGem, 2, 1), Gem(6003, AttackGem, 3, 1),
                Gem(6004, DefenseGem, 1, 2));
            Load(bag, StandardEquipment());
            _ui.Open(Sword);
            Assert.That(Row(View, 0).ChoiceCount, Is.EqualTo(1), "攻击孔只接攻击宝石");
            Assert.That(Row(View, 1).ChoiceCount, Is.EqualTo(2), "通用孔两种都接");

            _ui.BeginInlay(0);
            var picker = View.Picker;
            Assert.That(picker, Is.Not.Null);
            Assert.That(picker.Socket, Is.EqualTo(0u));
            Assert.That(picker.SocketName, Is.EqualTo("攻击孔"));
            Assert.That(picker.Title, Is.EqualTo("选择要镶入「攻击孔」的宝石"));
            Assert.That(picker.CanChoose, Is.True);
            var option = picker.Options.Single();
            Assert.That(option.ConfigId, Is.EqualTo(AttackGem));
            Assert.That(option.Name, Is.EqualTo("攻击宝石·一级"));
            Assert.That(option.IconKey, Is.EqualTo("gem_2101"));
            Assert.That(option.Count, Is.EqualTo(5ul), "两堆合计");
            Assert.That(option.CountText, Is.EqualTo("×5"));
            Assert.That(option.AttrText, Is.EqualTo("伤害 +30"));
            Assert.That(ForgeCalls, Is.EqualTo(0), "选宝石这一步不发包");

            _ui.ChooseGem(DefenseGem);
            Assert.That(ForgeCalls, Is.EqualTo(0), "列表里没有的宝石:不发");
            Assert.That(View.Picker, Is.Not.Null);

            _ui.ChooseGem(AttackGem);
            Assert.That(Last.MessageId, Is.EqualTo(MessageIds.InlayGem));
            var request = (InlayGemRequest)Last.Request;
            Assert.That(request.ItemId, Is.EqualTo(Sword));
            Assert.That(request.Socket, Is.EqualTo(0u));
            Assert.That(request.GemConfigId, Is.EqualTo(AttackGem));
            Assert.That(View.Picker, Is.Null);
            Assert.That(View.StatusText, Is.EqualTo("正在镶嵌…"));

            _ui.CancelInlay();
            _ui.BeginInlay(1);
            Assert.That(View.Picker, Is.Null, "在途时开不了选宝石");
        }

        [Test]
        public void PickerOptionsAreOrderedByConfigForTheAnyTypeSocket()
        {
            var bag = CharacterBag(50000, Weapon(Sword), Gem(6004, DefenseGem, 1, 2), Gem(6002, AttackGem, 2, 1));
            Load(bag, StandardEquipment());
            _ui.Open(Sword);
            _ui.BeginInlay(1);
            Assert.That(View.Picker.Options.Select(o => o.ConfigId), Is.EqualTo(new[] { AttackGem, DefenseGem }));
        }

        [Test]
        public void InlayIsGreyWhenNoGemFitsAndThePickerCannotBeOpened()
        {
            Load(StandardBag(attackGems: 0), StandardEquipment());
            _ui.Open(Sword);
            AssertButton(Row(View, 0).Button, "镶嵌", false, "行囊里没有能嵌入这个孔的宝石");
            Assert.That(Row(View, 0).ChoiceCount, Is.EqualTo(0));
            _ui.BeginInlay(0);
            Assert.That(View.Picker, Is.Null);
        }

        [Test]
        public void PickerClosesWhenTheSocketGetsFilledOrTheGemsRunOut()
        {
            LoadBoth();
            _ui.Open(Sword);
            _ui.BeginInlay(1);
            Assert.That(View.Picker, Is.Not.Null);

            // 快照刷新后那个孔已经镶上了:选宝石这一步收起。
            _client.RequestBag();
            RespondBagRead(0, StandardBag(sword: WithGem(Weapon(Sword), 1)));
            Assert.That(View.Picker, Is.Null);
            Assert.That(Row(View, 1).IsRemove, Is.True);

            // 另一个孔:宝石用完了也收起。
            _ui.BeginInlay(0);
            Assert.That(View.Picker, Is.Not.Null);
            _client.RequestBag();
            RespondBagRead(0, StandardBag(attackGems: 0, sword: WithGem(Weapon(Sword), 1)));
            Assert.That(View.Picker, Is.Null);
            _ui.ChooseGem(AttackGem);
            Assert.That(ForgeCalls, Is.EqualTo(0));
        }

        [Test]
        public void PickerStaysOpenButCannotChooseWhileAnotherOperationIsInFlight()
        {
            LoadBoth();
            _ui.Open(Sword);
            _ui.BeginInlay(1);
            _client.UnequipItem(WornSword);

            Assert.That(View.Picker, Is.Not.Null);
            Assert.That(View.Picker.CanChoose, Is.False);
            Assert.That(View.Picker.Hint, Is.EqualTo("正在处理装备…"));
            _ui.ChooseGem(AttackGem);
            Assert.That(ForgeCalls, Is.EqualTo(0));
            Assert.That(View.Picker, Is.Not.Null, "不能选时选宝石这一步保持打开");
        }

        [Test]
        public void PickerAndConfirmationAreNeverOpenAtTheSameTime()
        {
            Load(StandardBag(sword: Weapon(Sword, level: 5, failLoss: 1)), StandardEquipment());
            _ui.Open(Sword);
            _ui.BeginInlay(1);
            _ui.Enhance();
            Assert.That(View.Confirm, Is.Not.Null);
            Assert.That(View.Picker, Is.Null);
            _ui.BeginInlay(1);
            Assert.That(View.Picker, Is.Not.Null);
            Assert.That(View.Confirm, Is.Null);
        }

        // ── 取下 ────────────────────────────────────────────

        [Test]
        public void RemoveSendsImmediatelyEvenForAGemThatIsNotActive()
        {
            Load(StandardBag(), EquipmentBag(WithGem(Weapon(WornSword, 3), 0, active: false)));
            _ui.Open(WornSword);
            var row = Row(View, 0);
            Assert.That(row.Kind, Is.EqualTo(EquipForgeSocketKind.Inactive));
            Assert.That(row.Text, Is.EqualTo("攻击孔：攻击宝石·一级（未生效）"));
            AssertButton(row.Button, "取下", true);

            _ui.RemoveGem(0);
            Assert.That(Last.MessageId, Is.EqualTo(MessageIds.RemoveGem));
            var request = (RemoveGemRequest)Last.Request;
            Assert.That(request.ItemId, Is.EqualTo(WornSword));
            Assert.That(request.Socket, Is.EqualTo(0u));
            Assert.That(View.StatusText, Is.EqualTo("正在取下宝石…"));
        }

        [Test]
        public void IntentsOnTheWrongKindOfSocketDoNothing()
        {
            LoadBoth();
            _ui.Open(WornSword);
            _ui.RemoveGem(1);   // 空孔
            _ui.RemoveGem(9);   // 没有这个孔
            _ui.BeginInlay(0);  // 已占用
            _ui.BeginInlay(9);  // 没有这个孔
            Assert.That(ForgeCalls, Is.EqualTo(0));
            Assert.That(View.Picker, Is.Null);
            Assert.That(_client.EquipmentError, Is.Empty, "面板自己挡下,没有走到数据层的本地拒绝");
            Assert.That(View.StatusText, Is.Empty);
        }

        // ── 别处的在途 / 整理 / 任务 ────────────────────────

        [Test]
        public void EquipInFlightOnTheBagPageGreysTheForgeButtons()
        {
            LoadBoth();
            _ui.Open(Sword);
            _client.UnequipItem(WornSword);

            Assert.That(View.Busy, Is.True);
            Assert.That(View.StatusText, Is.EqualTo("正在处理装备…"));
            AssertButton(View.EnhanceButton, "强化", false, "正在处理装备…");
            _ui.Enhance();
            Assert.That(ForgeCalls, Is.EqualTo(0));
        }

        [Test]
        public void SortingGreysTheButtonsAndTheRefresh()
        {
            LoadBoth();
            _ui.Open(Sword);
            _client.SortBag();

            AssertButton(View.EnhanceButton, "强化", false, "背包正在整理，请稍候");
            AssertButton(Row(View, 0).Button, "镶嵌", false, "背包正在整理，请稍候");
            AssertButton(View.RefreshButton, "刷新", false, "背包正在整理，请稍候");
        }

        [Test]
        public void MissionActionInFlightGreysTheButtons()
        {
            LoadBoth();
            _client.RequestMissions();
            Last.Respond(new GetMissionListResponse { Missions = { new PlayerMissionInfo { Scope = 0, MissionId = 1, CanAccept = true } } });
            _client.AcceptMission(0, 1);
            Assert.That(_client.BusyMissionAction, Is.True, "前置:接任务在途");
            _ui.Open(Sword);

            AssertButton(View.EnhanceButton, "强化", false, "任务正在处理中，请稍候");
        }

        // ── 发包间隔 ────────────────────────────────────────

        [Test]
        public void CooldownGreysTheButtonsUntilATickAfterTheInterval()
        {
            LoadBoth();
            _ui.Open(Sword);
            _ui.Enhance();
            LastEnhance(Sword).Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4, sword: Weapon(Sword, 1)), Equipment = StandardEquipment(),
                Success = true, EnhanceLevel = 1,
            });

            Assert.That(View.CoolingDown, Is.True);
            AssertButton(View.EnhanceButton, "强化", false, "操作太快，请稍候");
            AssertButton(Row(View, 1).Button, "镶嵌", false, "操作太快，请稍候");
            Assert.That(View.StatusText, Is.EqualTo("强化成功：青铜剑 +1"), "置灰不盖住结果");
            int sent = ForgeCalls;
            _ui.Enhance();
            Assert.That(ForgeCalls, Is.EqualTo(sent));

            _now += PlayerFeaturesClient.ForgeMinIntervalMs - 1;
            _changes = 0;
            _ui.Tick();
            Assert.That(_changes, Is.EqualTo(0), "还在冷却:Tick 不重画");

            _now += 1;
            Assert.That(View.CoolingDown, Is.True, "时间到了但还没 Tick:快照不会自己变");
            _ui.Tick();
            Assert.That(_changes, Is.EqualTo(1));
            Assert.That(View.CoolingDown, Is.False);
            AssertButton(View.EnhanceButton, "强化", true);
            _ui.Tick();
            Assert.That(_changes, Is.EqualTo(1), "没有变化:不重画");

            _ui.Enhance();
            Assert.That(ForgeCalls, Is.EqualTo(sent + 1));
        }

        [Test]
        public void TickDoesNothingWhileClosedEvenDuringTheCooldown()
        {
            LoadBoth();
            _ui.Open(Sword);
            _ui.Enhance();          // 发包:冷却从这一刻起算
            _ui.Close();
            Assert.That(_client.ForgeCooldownRemainingMs, Is.GreaterThan(0), "前置:还在冷却");
            _changes = 0;
            _ui.Tick();
            _ui.Tick();
            Assert.That(_changes, Is.EqualTo(0), "关着时冷却中也不重画");
            Assert.That(View, Is.SameAs(EquipForgeView.Closed));
        }

        // ── 目标跟着走 / 换绑 / 断线 ────────────────────────

        [Test]
        public void TargetIsFollowedWhenItIsPutOn()
        {
            LoadBoth();
            _ui.Open(Sword);
            _client.EquipItem(Sword);
            Assert.That(Last.MessageId, Is.EqualTo(MessageIds.EquipItem));
            Last.Respond(new EquipItemResponse
            {
                Bag = CharacterBag(50000, WithGem(Weapon(WornSword, 3), 0), Stack(6001, Stone, 5), Gem(6002, AttackGem, 2, 1)),
                Equipment = EquipmentBag(Weapon(Sword)),
            });

            Assert.That(View.Stage, Is.EqualTo(EquipForgeStage.Ready));
            Assert.That(View.Worn, Is.True);
            Assert.That(View.LocationText, Is.EqualTo("已穿戴"));
        }

        [Test]
        public void TargetThatDisappearsTurnsTheViewToMissingAndDropsThePicker()
        {
            LoadBoth();
            _ui.Open(Sword);
            _ui.BeginInlay(1);
            _client.RequestBag();
            RespondBagRead(0, CharacterBag(50000, Stack(6001, Stone, 5)));

            Assert.That(View.Stage, Is.EqualTo(EquipForgeStage.Missing));
            Assert.That(View.Picker, Is.Null);
            Assert.That(_ui.IsOpen, Is.True, "面板不自己关:由界面提示后关掉");

            _client.RequestBag();
            RespondBagRead(0, StandardBag());
            Assert.That(View.Stage, Is.EqualTo(EquipForgeStage.Ready));
            Assert.That(View.Picker, Is.Null, "装备回来了:选宝石这一步不自己弹回来");
        }

        [Test]
        public void BindingAnotherClientClosesThePanelAndIgnoresTheOldOne()
        {
            LoadBoth();
            _ui.Open(Sword);
            _ui.Enhance();
            var oldCall = LastEnhance(Sword);

            var otherNet = new FakeBattleTransport { PlayerId = 9102, IsReady = true };
            var other = new PlayerFeaturesClient(otherNet);
            _ui.Bind(other);
            Assert.That(_ui.IsOpen, Is.False);
            Assert.That(View, Is.SameAs(EquipForgeView.Closed));

            // 新实例的两份快照到位,面板重新打开。
            other.RequestBag();
            otherNet.Calls[0].Respond(new GetBagResponse { Bag = StandardBag() });
            other.RequestEquipment();
            otherNet.Calls[1].Respond(new GetBagResponse { Bag = StandardEquipment() });
            _ui.Open(Sword);
            Assert.That(View.Stage, Is.EqualTo(EquipForgeStage.Ready));

            _changes = 0;
            oldCall.Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(sword: Weapon(Sword, 1)), Equipment = StandardEquipment(), Success = true, EnhanceLevel = 1,
            });
            Assert.That(_changes, Is.EqualTo(0), "旧实例的回调不再进来");
            Assert.That(_notices, Is.Empty);
            Assert.That(View.Title, Is.EqualTo("青铜剑"), "画的仍是新实例的快照");

            _ui.Bind(other);
            Assert.That(_ui.IsOpen, Is.True, "绑同一个实例:什么都不变");

            _ui.Bind(null);
            _ui.Open(Sword);
            Assert.That(_ui.IsOpen, Is.False, "解绑后打不开");
            other.Dispose();
        }

        [Test]
        public void DisconnectTurnsTheViewBackToLoading()
        {
            LoadBoth();
            _ui.Open(Sword);
            _net.RaiseDisconnected();

            Assert.That(View.Stage, Is.EqualTo(EquipForgeStage.Loading));
            Assert.That(View.EnhanceButton.Enabled, Is.False);
            Assert.That(View.Busy, Is.False);
        }

        // ── 评审补充:弹层不自己弹回来 / 整理时的点击 / 别处的操作 / 刷新失败 / 降级封顶 / 换一件 ──

        [Test]
        public void ConfirmationDroppedWhileMissingDoesNotComeBackOnItsOwn()
        {
            Load(StandardBag(sword: Weapon(Sword, level: 5, failLoss: 1)), StandardEquipment());
            _ui.Open(Sword);
            _ui.Enhance();
            Assert.That(View.Confirm, Is.Not.Null);

            _client.RequestBag();
            RespondBagRead(0, CharacterBag(50000, Stack(6001, Stone, 5)));
            Assert.That(View.Stage, Is.EqualTo(EquipForgeStage.Missing));
            _client.RequestBag();
            RespondBagRead(0, StandardBag(sword: Weapon(Sword, level: 5, failLoss: 1)));
            Assert.That(View.Stage, Is.EqualTo(EquipForgeStage.Ready));
            Assert.That(View.Confirm, Is.Null, "没再点强化:确认框不自己弹回来");
            Assert.That(ForgeCalls, Is.EqualTo(0));
        }

        [Test]
        public void PickerDroppedWhenGemsRunOutDoesNotComeBackWhenTheyReturn()
        {
            LoadBoth();
            _ui.Open(Sword);
            _ui.BeginInlay(1);
            _client.RequestBag();
            RespondBagRead(0, StandardBag(attackGems: 0));
            Assert.That(View.Picker, Is.Null);
            _client.RequestBag();
            RespondBagRead(0, StandardBag());
            Assert.That(View.Picker, Is.Null, "宝石回来了:选宝石这一步不自己弹回来");
        }

        [TestCase("picker")]
        [TestCase("confirm")]
        public void ClicksWhileSortingKeepTheStepOpenAndNeverReachTheDataLayer(string step)
        {
            Load(StandardBag(sword: Weapon(Sword, level: 5, failLoss: 1)), StandardEquipment());
            _ui.Open(Sword);
            if (step == "picker") _ui.BeginInlay(1); else _ui.Enhance();
            _client.SortBag();

            if (step == "picker")
            {
                Assert.That(View.Picker.CanChoose, Is.False);
                Assert.That(View.Picker.Hint, Is.EqualTo("背包正在整理，请稍候"));
                _ui.ChooseGem(AttackGem);
                Assert.That(View.Picker, Is.Not.Null);
            }
            else
            {
                Assert.That(View.Confirm.CanConfirm, Is.False);
                _ui.ConfirmEnhance();
                Assert.That(View.Confirm, Is.Not.Null);
            }
            Assert.That(ForgeCalls, Is.EqualTo(0));
            Assert.That(_client.EquipmentError, Is.Empty, "面板自己挡下:数据层不会报「背包正在整理」");
        }

        [Test]
        public void EquipActionAfterAFinishedPanelOperationIsNotMistakenForIt()
        {
            LoadBoth();
            _ui.Open(Sword);
            _ui.Enhance();
            LastEnhance(Sword).Respond(new EnhanceEquipResponse
            {
                Bag = StandardBag(gold: 49000, stones: 4, sword: Weapon(Sword, 1)), Equipment = StandardEquipment(),
                Success = true, EnhanceLevel = 1,
            });
            _client.UnequipItem(WornSword); // 背包页上的卸下,不受发包间隔约束

            Assert.That(View.StatusText, Is.EqualTo("正在处理装备…"));
            AssertButton(View.EnhanceButton, "强化", false, "正在处理装备…");
            Last.Respond(new UnequipItemResponse
            {
                Bag = CharacterBag(49000, Weapon(Sword, 1), Stack(6001, Stone, 4), WithGem(Weapon(WornSword, 3), 0)),
                Equipment = EquipmentBag(),
            });
            Assert.That(View.StatusText, Is.Empty, "别处的装备操作开始之后,上一次强化的结果不再挂着");
        }

        [Test]
        public void BusyAndFailureOfAnotherItemAreNotShownOnThisOne()
        {
            LoadBoth();
            _ui.Open(Sword);
            _ui.Enhance();
            var stalled = LastEnhance(Sword);
            _ui.Close();
            _ui.Open(WornSword);

            Assert.That(View.StatusText, Is.EqualTo("正在处理装备…"), "在强化的是另一件");
            AssertButton(View.EnhanceButton, "强化", false, "正在处理装备…");

            stalled.FailWith("timeout");
            Assert.That(_client.EquipmentError, Is.EqualTo("无法强化:网络异常,请稍后重试"), "前置:数据层报了那件的失败");
            Assert.That(View.StatusTone, Is.Not.EqualTo(EquipForgeTone.Error), "那是另一件的失败,不挂到这件头上");
        }

        [Test]
        public void ReopeningTheSameItemWhileItsOperationIsInFlightKeepsTheSpecificLabelAndReason()
        {
            LoadBoth();
            _ui.Open(Sword);
            _ui.Enhance();
            var stalled = LastEnhance(Sword);
            _ui.Close();
            _ui.Open(Sword);

            Assert.That(View.StatusText, Is.EqualTo("正在强化…"));
            stalled.Respond(new EnhanceEquipResponse
            { ErrorMessage = new TipInfoMessage { Id = (uint)equip_error.KEquipEnhanceGoldNotEnough } });
            Assert.That(View.StatusText, Is.EqualTo("无法强化:银两不足,无法强化"));
            Assert.That(View.StatusTone, Is.EqualTo(EquipForgeTone.Error));
        }

        [Test]
        public void FailedBagRefreshIsReportedUntilTheNextRead()
        {
            LoadBoth();
            _ui.Open(Sword);
            int calls = _net.Calls.Count;
            _ui.Refresh();
            _net.Calls[calls].FailWith("timeout");
            _net.Calls[calls + 1].Respond(new GetBagResponse { Bag = StandardEquipment() });

            Assert.That(View.Stage, Is.EqualTo(EquipForgeStage.Ready), "旧快照还在");
            Assert.That(View.StatusText, Is.EqualTo("行囊同步失败，请点击刷新重试"));
            Assert.That(View.StatusTone, Is.EqualTo(EquipForgeTone.Error));
            AssertButton(View.RefreshButton, "刷新", true);

            _ui.Refresh();
            Assert.That(View.StatusText, Is.Empty, "下一次读取开始:收起");
        }

        [Test]
        public void FailedEquipmentRefreshIsReported()
        {
            LoadBoth();
            _ui.Open(Sword);
            int calls = _net.Calls.Count;
            _ui.Refresh();
            _net.Calls[calls].Respond(new GetBagResponse { Bag = StandardBag() });
            _net.Calls[calls + 1].FailWith("timeout");

            Assert.That(View.StatusText, Is.EqualTo("读取装备栏失败:网络异常,请稍后重试"));
            Assert.That(View.StatusTone, Is.EqualTo(EquipForgeTone.Error));
        }

        [Test]
        public void RefreshIsIgnoredWhileSorting()
        {
            LoadBoth();
            _ui.Open(Sword);
            _client.SortBag();
            int calls = _net.Calls.Count;
            _ui.Refresh();
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));
            Assert.That(_client.BagError, Is.Empty, "没有走到数据层的「背包正在整理」");
        }

        [Test]
        public void OnlyTheBagReadFailingStillOffersARetry()
        {
            _ui.Open(Sword);
            _net.Calls[0].FailWith("timeout");
            _net.Calls[1].Respond(new GetBagResponse { Bag = StandardEquipment() });
            Assert.That(View.Stage, Is.EqualTo(EquipForgeStage.Loading));
            Assert.That(View.StageText, Is.EqualTo("行囊或装备栏暂未同步，请点击刷新重试"));
            Assert.That(View.RefreshButton.Enabled, Is.True);
        }

        [Test]
        public void LevelLossIsCappedAtTheCurrentLevelLikeTheServer()
        {
            // +0 遇到「失败降 1 级」:服务器最多掉到 +0,实际不掉级 —— 不显示降级、不弹确认,直接发。
            Load(StandardBag(sword: Weapon(Sword, level: 0, failLoss: 1)), StandardEquipment());
            _ui.Open(Sword);
            Assert.That(View.FailRuleText, Is.EqualTo("失败不降级"));
            _ui.Enhance();
            Assert.That(View.Confirm, Is.Null);
            LastEnhance(Sword).FailWith("timeout");

            // +1 遇到「失败降 3 级」:实际只会降 1 级。
            _now += PlayerFeaturesClient.ForgeMinIntervalMs;
            _client.RequestBag();
            RespondBagRead(0, StandardBag(sword: Weapon(Sword, level: 1, failLoss: 3)));
            _client.RequestEquipment();
            RespondBagRead(2, StandardEquipment());
            Assert.That(View.FailRuleText, Is.EqualTo("失败降 1 级"));
            int sent = ForgeCalls;
            _ui.Enhance();
            Assert.That(ForgeCalls, Is.EqualTo(sent), "会降级:先确认");
            Assert.That(View.Confirm.Message, Does.EndWith("失败会降 1 级（+1 → +0），消耗不退还。"));
        }

        [Test]
        public void OpeningAnotherItemDropsThePickerAndTheConfirmation()
        {
            var wornDegrading = WithGem(Weapon(WornSword, level: 5, failLoss: 1), 0);
            Load(StandardBag(sword: Weapon(Sword, level: 5, failLoss: 1)), EquipmentBag(wornDegrading));
            _ui.Open(Sword);
            _ui.BeginInlay(1);
            _ui.Open(WornSword);
            Assert.That(View.Picker, Is.Null, "换一件:不带着上一件的选宝石这一步");

            _ui.Enhance();
            Assert.That(View.Confirm, Is.Not.Null);
            _ui.Open(Sword);
            Assert.That(View.Confirm, Is.Null, "换一件:不带着上一件的确认框");
        }

        /// <summary>真实传输没连上时会在 Call 内部同步回调 onError;其余调用照常交给 <see cref="FakeBattleTransport"/>。</summary>
        private sealed class SyncFailingTransport : IBattleTransport
        {
            public readonly FakeBattleTransport Inner = new FakeBattleTransport { PlayerId = 9101, IsReady = true };
            public uint FailNow;
            public int SyncFailures;

            public ulong PlayerId => Inner.PlayerId;
            public bool IsReady => Inner.IsReady;

            public event Action Disconnected
            {
                add => Inner.Disconnected += value;
                remove => Inner.Disconnected -= value;
            }

            public void RegisterNotify(uint messageId, Action<MessageContent> handler) => Inner.RegisterNotify(messageId, handler);

            public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                Action<TResp> onResponse, Action<string> onError) where TResp : IMessage<TResp>
            {
                if (messageId == FailNow)
                {
                    ++SyncFailures;
                    onError("not connected");
                    return;
                }
                Inner.Call(messageId, request, parser, onResponse, onError);
            }

            public void SendOneWay(uint messageId, IMessage request) => Inner.SendOneWay(messageId, request);
        }
    }
}
