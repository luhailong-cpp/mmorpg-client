using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.UI.Ugui.Gameplay;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 强化 / 镶嵌的排版与纯逻辑模型用例(服务端 docs/design/equipment-enhance-inlay.md §6.2 / §6.3):
    /// <see cref="EquipDisplay"/> 新增各行的逐字格式,<see cref="EquipBagModel"/> 的孔位视图、可嵌宝石与强化前置检查。
    /// 不碰 UnityEngine,也不碰数据层(那在 EquipForgeClientTests)。
    /// </summary>
    public sealed class EquipForgeModelTests
    {
        private const uint Stone = 2001;

        private static EquipAttrLineInfo Line(uint tier, uint seq, string name, ulong value, ulong bonus = 0, ulong next = 0,
            bool percent = false, ulong cap = 0, uint attrId = 1)
            => new EquipAttrLineInfo
            {
                AttrId = attrId, Name = name, Tier = tier, Seq = seq, Value = value, Cap = cap, Percent = percent,
                EnhanceBonus = bonus, EnhanceNextValue = next,
            };

        private static EquipGemSocketInfo Socket(uint socket, string name, uint gemType = 0, uint gemConfigId = 0,
            string gemName = "", EquipAttrLineInfo attr = null)
        {
            var info = new EquipGemSocketInfo
            {
                Socket = socket, Name = name, GemType = gemType, GemConfigId = gemConfigId, GemName = gemName,
            };
            if (attr != null) info.Attr = attr;
            return info;
        }

        private static BagItemInfo Stack(ulong id, uint configId, uint count, uint gemType = 0, string name = "")
            => new BagItemInfo { ItemId = id, ConfigId = configId, Count = count, MaxStack = 999, GemType = gemType, Name = name };

        /// <summary>人物背包;gold 为 null = 快照不带货币。</summary>
        private static BagInfo Bag(ulong? gold, params BagItemInfo[] items)
        {
            var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = 0, Capacity = 40 } };
            if (gold.HasValue)
            {
                bag.Currency = new CurrencyComp();
                bag.Currency.Values.Add(gold.Value);
            }
            bag.Items.AddRange(items);
            return bag;
        }

        /// <summary>一把 +level / 上限 maxLevel 的武器;withNext 时带下一级预览(金币 cost、强化石 ×stones)。</summary>
        private static BagItemInfo Weapon(uint level, uint maxLevel, bool withNext = true, ulong cost = 1000, uint stones = 2)
        {
            var item = new BagItemInfo
            {
                ItemId = 5001, ConfigId = 1101, Count = 1, MaxStack = 1, EquipKind = 11,
                EnhanceLevel = level, EnhanceMaxLevel = maxLevel,
            };
            if (withNext)
                item.EnhanceNext = new EquipEnhancePreview
                {
                    NextLevel = level + 1, SuccessRate = 9000, CostGold = cost,
                    MaterialConfigId = stones == 0 ? 0u : Stone, MaterialName = "强化石", MaterialCount = stones,
                };
            return item;
        }

        // ── EquipDisplay:基础属性行与强化 ────────────────────

        [Test]
        public void BaseLineShowsTheEnhanceBonusOnlyWhenThereIsOne()
        {
            Assert.That(EquipDisplay.FormatLine(Line(0, 0, "伤害", 40)), Is.EqualTo("伤害：40"), "未强化:与一期逐字相同");
            Assert.That(EquipDisplay.FormatLine(Line(0, 0, "伤害", 46, bonus: 6)), Is.EqualTo("伤害：46（+6）"));
            Assert.That(EquipDisplay.FormatLine(Line(0, 0, "伤害", 46, bonus: 6, next: 48)), Is.EqualTo("伤害：46（+6）"),
                "当前值这一行不带下一级预览");
            Assert.That(EquipDisplay.FormatLine(Line(0, 0, "物理必杀率", 6, bonus: 1, percent: true)),
                Is.EqualTo("物理必杀率：6%（+1%）"), "百分比行:值与增量都带 %");
            Assert.That(EquipDisplay.FormatLine(Line(0, 1, "速度", 88, bonus: 4, cap: 999)), Is.EqualTo("速度：88（+4）"),
                "基础属性行照旧不显示上限");
            Assert.That(EquipDisplay.FormatLine(Line(0, 0, "", 46, bonus: 6, attrId: 8)), Is.EqualTo("属性 · 8：46（+6）"));
        }

        [Test]
        public void EnhanceBonusOnAnAffixLineIsIgnored()
        {
            // 强化只放大基础属性;随机属性行即使带了 enhance_bonus(协议上那两个字段只对基础属性行有意义)也照一期排版。
            Assert.That(EquipDisplay.FormatLine(Line(1, 0, "力量", 3, bonus: 9, next: 9, cap: 5)), Is.EqualTo("力量 3/5"));
            Assert.That(EquipDisplay.FormatLine(Line(9, 0, "新属性", 1, bonus: 1)), Is.EqualTo("新属性 1"));
        }

        [Test]
        public void EnhancePreviewLineShowsCurrentAndNextValue()
        {
            Assert.That(EquipDisplay.FormatEnhancePreviewLine(Line(0, 0, "伤害", 46, bonus: 6, next: 48)), Is.EqualTo("伤害：46 → 48"));
            Assert.That(EquipDisplay.FormatEnhancePreviewLine(Line(0, 0, "伤害", 40, next: 42)), Is.EqualTo("伤害：40 → 42"));
            Assert.That(EquipDisplay.FormatEnhancePreviewLine(Line(0, 0, "物理必杀率", 6, next: 7, percent: true)),
                Is.EqualTo("物理必杀率：6% → 7%"));
            // 基础值太小、加成向下取整后没变:照服务器给的显示,不自己判断「有没有提升」。
            Assert.That(EquipDisplay.FormatEnhancePreviewLine(Line(0, 0, "防御", 12, next: 12)), Is.EqualTo("防御：12 → 12"));
            Assert.That(EquipDisplay.FormatEnhancePreviewLine(Line(0, 0, " ", 12, next: 13, attrId: 8)), Is.EqualTo("属性 · 8：12 → 13"));
            Assert.That(EquipDisplay.FormatEnhancePreviewLine(null), Is.Empty);
        }

        [Test]
        public void EnhancePreviewLinesExistOnlyWhenTheItemCarriesANextLevel()
        {
            var item = Weapon(3, 12);
            item.BaseAttrs.Add(Line(0, 1, "速度", 88, bonus: 4, next: 90));
            item.BaseAttrs.Add(Line(0, 0, "防御", 126, bonus: 6, next: 130));
            item.Affixes.Add(Line(1, 0, "力量", 3, cap: 5));

            Assert.That(EquipDisplay.EnhancePreviewLines(item), Is.EqualTo(new[] { "防御：126 → 130", "速度：88 → 90" }),
                "每条基础属性一行,seq 升序;随机属性不参与");
            Assert.That(item.BaseAttrs.Select(line => line.Name), Is.EqualTo(new[] { "速度", "防御" }), "不改快照里的顺序");

            // 满级 / 未开放 / 实例未初始化:服务器不带 enhance_next,各行的 enhance_next_value 是 0,不能显示成「→ 0」。
            item.EnhanceNext = null;
            Assert.That(EquipDisplay.EnhancePreviewLines(item), Is.Empty);
            Assert.That(EquipDisplay.EnhancePreviewLines(new BagItemInfo()), Is.Empty);
            Assert.That(EquipDisplay.EnhancePreviewLines(null), Is.Empty);
        }

        [Test]
        public void LinesOfStillReturnsOnlyBaseAndAffixLines()
        {
            var item = Weapon(3, 12);
            item.BaseAttrs.Add(Line(0, 0, "伤害", 46, bonus: 6, next: 48));
            item.Affixes.Add(Line(1, 0, "力量", 3, cap: 5));
            item.GemSockets.Add(Socket(0, "攻击孔", 1, 2101, "攻击宝石·一级", Line(0, 0, "伤害", 30)));

            // 孔位行另出函数:LinesOf 的形状不变,界面按原样喂给属性行控件。
            Assert.That(EquipDisplay.LinesOf(item).Select(EquipDisplay.FormatLine), Is.EqualTo(new[] { "伤害：46（+6）", "力量 3/5" }));
        }

        [TestCase("青铜剑", 0u, "青铜剑")]
        [TestCase("青铜剑", 3u, "青铜剑 +3")]
        [TestCase("青铜剑", 12u, "青铜剑 +12")]
        [TestCase("物品 · 1101", 1u, "物品 · 1101 +1")]
        public void NameCarriesTheEnhanceLevelSuffix(string name, uint level, string expected)
            => Assert.That(EquipDisplay.NameWithEnhance(name, level), Is.EqualTo(expected));

        [TestCase(10000u, "100%")]
        [TestCase(9000u, "90%")]
        [TestCase(1250u, "12.5%")]
        [TestCase(1234u, "12.34%")]
        [TestCase(5005u, "50.05%")]
        [TestCase(100u, "1%")]
        [TestCase(10u, "0.1%")]
        [TestCase(1u, "0.01%")]
        [TestCase(0u, "0%")]
        [TestCase(12000u, "120%")]
        public void SuccessRateIsShownAsPercentWithUpToTwoDecimals(uint permyriad, string expected)
            => Assert.That(EquipDisplay.FormatSuccessRate(permyriad), Is.EqualTo(expected));

        [Test]
        public void EnhanceCostListsGoldThenMaterial()
        {
            var next = new EquipEnhancePreview { CostGold = 5000, MaterialConfigId = Stone, MaterialName = "强化石", MaterialCount = 2 };
            Assert.That(EquipDisplay.FormatEnhanceCost(next), Is.EqualTo("金币 5000、强化石 ×2"));

            // 不要材料:config 为 0,或数量为 0。
            Assert.That(EquipDisplay.FormatEnhanceCost(new EquipEnhancePreview { CostGold = 1000 }), Is.EqualTo("金币 1000"));
            Assert.That(EquipDisplay.FormatEnhanceCost(new EquipEnhancePreview { CostGold = 1000, MaterialConfigId = Stone, MaterialName = "强化石" }),
                Is.EqualTo("金币 1000"));
            // 不收金币也照写,数值不省。
            Assert.That(EquipDisplay.FormatEnhanceCost(new EquipEnhancePreview { MaterialConfigId = Stone, MaterialName = "强化石", MaterialCount = 1 }),
                Is.EqualTo("金币 0、强化石 ×1"));
            // 材料的 Item 行查不到:服务器给空名,退回带 id 的占位,不编造名称。
            Assert.That(EquipDisplay.FormatEnhanceCost(new EquipEnhancePreview { CostGold = 5000, MaterialConfigId = Stone, MaterialCount = 2 }),
                Is.EqualTo("金币 5000、物品 · 2001 ×2"));
            Assert.That(EquipDisplay.FormatEnhanceCost(new EquipEnhancePreview { CostGold = ulong.MaxValue }), Is.EqualTo("金币 18446744073709551615"));
            Assert.That(EquipDisplay.FormatEnhanceCost(null), Is.Empty);
        }

        [Test]
        public void ItemNameFallsBackToThePlaceholderUsedElsewhere()
        {
            Assert.That(EquipDisplay.ItemNameOr("强化石", Stone), Is.EqualTo("强化石"));
            Assert.That(EquipDisplay.ItemNameOr("", Stone), Is.EqualTo("物品 · 2001"));
            Assert.That(EquipDisplay.ItemNameOr("  ", Stone), Is.EqualTo("物品 · 2001"));
            // 占位的写法与背包格子上的名称占位(GameplayUiArt.ItemName 的「物品 · N」)逐字相同:
            // 同一件东西在两处不能长得不一样。
            Assert.That(EquipDisplay.ItemNameOr(null, Stone), Is.EqualTo("物品 · 2001"));
        }

        // ── EquipDisplay:孔位与宝石 ──────────────────────────

        [Test]
        public void SocketLineCoversEmptyActiveAndInactiveGems()
        {
            Assert.That(EquipDisplay.FormatSocketLine(Socket(0, "攻击孔", 1)), Is.EqualTo("攻击孔：未镶嵌"));
            Assert.That(EquipDisplay.FormatSocketLine(Socket(0, "攻击孔", 1, 2101, "攻击宝石·一级", Line(0, 0, "伤害", 30))),
                Is.EqualTo("攻击孔：攻击宝石·一级（伤害 +30）"));
            // 占着孔却不带 attr:已镶但当前不计入属性(宝石行被删、属性不可用),仍然显示这颗宝石,好让人取下。
            Assert.That(EquipDisplay.FormatSocketLine(Socket(0, "攻击孔", 1, 2101, "攻击宝石·一级")),
                Is.EqualTo("攻击孔：攻击宝石·一级（未生效）"));
            Assert.That(EquipDisplay.FormatSocketLine(Socket(1, "通用孔", 0, 2501, "会心宝石", Line(0, 0, "物理必杀率", 3, percent: true))),
                Is.EqualTo("通用孔：会心宝石（物理必杀率 +3%）"));
            // 「占用」只看 gem_config_id:空孔即使带了残留的名字 / 属性也按空孔显示。
            Assert.That(EquipDisplay.FormatSocketLine(Socket(0, "攻击孔", 1, 0, "残留名", Line(0, 0, "伤害", 30))),
                Is.EqualTo("攻击孔：未镶嵌"));
            Assert.That(EquipDisplay.FormatSocketLine(null), Is.Empty);
        }

        [Test]
        public void SocketAndGemNamesFallBackWithoutInventingText()
        {
            // 孔位没配名字:「孔位 N」,N 从 1 数起(孔位号从 0 起)。
            Assert.That(EquipDisplay.SocketName(Socket(0, "")), Is.EqualTo("孔位 1"));
            Assert.That(EquipDisplay.SocketName(Socket(1, "  ")), Is.EqualTo("孔位 2"));
            Assert.That(EquipDisplay.SocketName(Socket(uint.MaxValue, "")), Is.EqualTo("孔位 4294967296"), "不回绕成 0");
            Assert.That(EquipDisplay.SocketName(Socket(3, "气血孔")), Is.EqualTo("气血孔"));
            Assert.That(EquipDisplay.SocketName(null), Is.Empty);
            Assert.That(EquipDisplay.FormatSocketLine(Socket(1, "")), Is.EqualTo("孔位 2：未镶嵌"));

            // 宝石的 Item 行查不到:服务器给空名。
            Assert.That(EquipDisplay.FormatSocketLine(Socket(0, "攻击孔", 1, 2101, "", Line(0, 0, "伤害", 30))),
                Is.EqualTo("攻击孔：物品 · 2101（伤害 +30）"));
            Assert.That(EquipDisplay.FormatSocketLine(Socket(0, "攻击孔", 1, 2101)), Is.EqualTo("攻击孔：物品 · 2101（未生效）"));
            // 服务器下发的名称原样拼入,不加也不解释任何富文本标记(UI 侧 richText = false)。
            Assert.That(EquipDisplay.FormatSocketLine(Socket(0, "<b>孔</b>", 1, 2101, "<color=red>石</color>", Line(0, 0, "伤害", 30))),
                Is.EqualTo("<b>孔</b>：<color=red>石</color>（伤害 +30）"));
        }

        [Test]
        public void GemAttributeLineShowsNameAndPlusValue()
        {
            Assert.That(EquipDisplay.FormatGemAttr(Line(0, 0, "伤害", 30)), Is.EqualTo("伤害 +30"));
            Assert.That(EquipDisplay.FormatGemAttr(Line(0, 0, "物理必杀率", 3, percent: true)), Is.EqualTo("物理必杀率 +3%"));
            Assert.That(EquipDisplay.FormatGemAttr(Line(0, 0, "", 30, attrId: 9)), Is.EqualTo("属性 · 9 +30"));
            Assert.That(EquipDisplay.FormatGemAttr(null), Is.Empty, "不是宝石 / 属性行不可用时服务器不带 gem_attr");
        }

        [Test]
        public void SocketsOfOrdersBySocketNumberKeepsTheFirstDuplicateAndDoesNotMutateInput()
        {
            var item = new BagItemInfo { ItemId = 5001, EquipKind = 13 };
            item.GemSockets.Add(Socket(1, "气血孔", 3));
            item.GemSockets.Add(Socket(0, "防御孔", 2));
            item.GemSockets.Add(Socket(1, "重复的 1 号孔", 0)); // 脏数据:同一孔位号两条
            item.GemSockets.Add(Socket(4, "", 0));

            var sockets = EquipDisplay.SocketsOf(item);
            Assert.That(sockets.Select(socket => socket.Socket), Is.EqualTo(new uint[] { 0, 1, 4 }), "顺序看 socket 字段,不看下标");
            Assert.That(sockets[1].Name, Is.EqualTo("气血孔"), "同号取先出现的那一条");
            Assert.That(sockets.Select(EquipDisplay.FormatSocketLine),
                Is.EqualTo(new[] { "防御孔：未镶嵌", "气血孔：未镶嵌", "孔位 5：未镶嵌" }));
            Assert.That(item.GemSockets.Select(socket => socket.Socket), Is.EqualTo(new uint[] { 1, 0, 1, 4 }), "不改快照");
            Assert.That(sockets[0], Is.SameAs(item.GemSockets[1]), "返回的是快照里的同一个对象,不拷贝");

            Assert.That(EquipDisplay.SocketsOf(new BagItemInfo()), Is.Empty, "实例未初始化的装备不带孔位");
            Assert.That(EquipDisplay.SocketsOf(null), Is.Empty);
        }

        // ── EquipBagModel:孔位视图 ───────────────────────────

        [Test]
        public void GemSocketViewsAreOrderedAndCarryNameOccupancyAndActivity()
        {
            var item = new BagItemInfo { ItemId = 5001, EquipKind = 11 };
            item.GemSockets.Add(Socket(2, "", 0, 2201, "防御宝石·一级"));                                  // 已镶但不生效
            item.GemSockets.Add(Socket(0, "攻击孔", 1, 2101, "攻击宝石·一级", Line(0, 0, "伤害", 30)));    // 已镶且生效
            item.GemSockets.Add(Socket(1, "通用孔", 0));                                                    // 空孔

            var views = EquipBagModel.GemSockets(item);
            Assert.That(views.Select(view => view.Socket), Is.EqualTo(new uint[] { 0, 1, 2 }));
            Assert.That(views.Select(view => view.Name), Is.EqualTo(new[] { "攻击孔", "通用孔", "孔位 3" }));
            Assert.That(views.Select(view => view.Occupied), Is.EqualTo(new[] { true, false, true }), "占用只看 gem_config_id");
            Assert.That(views.Select(view => view.Active), Is.EqualTo(new[] { true, false, false }), "生效只看带不带 attr");
            Assert.That(views[0].Info, Is.SameAs(item.GemSockets[1]));
            Assert.That(views.Select(view => EquipDisplay.FormatSocketLine(view.Info)), Is.EqualTo(new[]
            {
                "攻击孔：攻击宝石·一级（伤害 +30）", "通用孔：未镶嵌", "孔位 3：防御宝石·一级（未生效）",
            }));

            Assert.That(EquipBagModel.GemSockets(new BagItemInfo()), Is.Empty);
            Assert.That(EquipBagModel.GemSockets(null), Is.Empty);
            Assert.That(default(EquipGemSocketView).Occupied || default(EquipGemSocketView).Active, Is.False, "空视图不抛异常");
        }

        [Test]
        public void FindGemSocketLooksUpBySocketNumberNotByIndex()
        {
            var item = new BagItemInfo { ItemId = 5001 };
            item.GemSockets.Add(Socket(1, "通用孔"));
            item.GemSockets.Add(Socket(0, "攻击孔", 1));
            item.GemSockets.Add(Socket(1, "重复的 1 号孔"));

            Assert.That(EquipBagModel.FindGemSocket(item, 0).Name, Is.EqualTo("攻击孔"));
            Assert.That(EquipBagModel.FindGemSocket(item, 1).Name, Is.EqualTo("通用孔"), "同号取先出现的,与孔位视图留下的是同一条");
            Assert.That(EquipBagModel.FindGemSocket(item, 1), Is.SameAs(EquipBagModel.GemSockets(item)[1].Info));
            Assert.That(EquipBagModel.FindGemSocket(item, 2), Is.Null);
            Assert.That(EquipBagModel.FindGemSocket(null, 0), Is.Null);
        }

        // ── EquipBagModel:数量与可嵌宝石 ─────────────────────

        [Test]
        public void CountOfSumsEveryStackOfTheSameConfig()
        {
            var bag = Bag(100, Stack(6001, Stone, 2), Stack(6002, 2101, 7, 1), Stack(6003, Stone, 3), Stack(6004, Stone, 0));
            Assert.That(EquipBagModel.CountOf(bag, Stone), Is.EqualTo(5ul), "同一种分几堆:按 config 汇总");
            Assert.That(EquipBagModel.CountOf(bag, 2101), Is.EqualTo(7ul));
            Assert.That(EquipBagModel.CountOf(bag, 9999), Is.Zero);
            Assert.That(EquipBagModel.CountOf(null, Stone), Is.Zero, "快照还没拉到");

            var huge = Bag(0, Stack(1, Stone, uint.MaxValue), Stack(2, Stone, uint.MaxValue));
            Assert.That(EquipBagModel.CountOf(huge, Stone), Is.EqualTo(2ul * uint.MaxValue), "合计不按 32 位回绕");
        }

        [Test]
        public void GemChoicesListFittingGemsAggregatedByConfigInConfigOrder()
        {
            var bag = Bag(100,
                Stack(6001, 2201, 4, 2, "防御宝石·一级"),
                Stack(6002, 2102, 1, 1, "攻击宝石·二级"),
                Stack(6003, 2101, 2, 1, "攻击宝石·一级"),
                Stack(6004, Stone, 9),                        // 不是宝石
                Stack(6005, 2101, 3, 1, "攻击宝石·一级(第二堆)"),
                Stack(6006, 2103, 0, 1, "攻击宝石·三级"));   // 0 颗的残留堆不算

            var attack = EquipBagModel.GemChoices(Socket(0, "攻击孔", 1), bag);
            Assert.That(attack.Select(choice => choice.ConfigId), Is.EqualTo(new uint[] { 2101, 2102 }), "只列类型相符的,config 升序");
            Assert.That(attack.Select(choice => choice.Count), Is.EqualTo(new ulong[] { 5, 1 }), "同一种的几堆合成一项");
            Assert.That(attack[0].Item.ItemId, Is.EqualTo(6003), "显示信息取先出现的那一堆");
            Assert.That(attack[0].Item.Name, Is.EqualTo("攻击宝石·一级"));

            var any = EquipBagModel.GemChoices(Socket(1, "通用孔", 0), bag);
            Assert.That(any.Select(choice => choice.ConfigId), Is.EqualTo(new uint[] { 2101, 2102, 2201 }), "通用孔接任意类型的宝石");
            Assert.That(any.Select(choice => choice.Count), Is.EqualTo(new ulong[] { 5, 1, 4 }));

            Assert.That(EquipBagModel.GemChoices(Socket(0, "迅捷孔", 4), bag), Is.Empty, "没有类型相符的宝石");
        }

        [Test]
        public void GemChoicesIgnoreOccupancyAndTolerateMissingInput()
        {
            var bag = Bag(100, Stack(6003, 2101, 2, 1));
            // 只管类型合不合:占着的孔要先取下,那是调用方按 Occupied 判的事。
            var occupied = Socket(0, "攻击孔", 1, 2101, "攻击宝石·一级", Line(0, 0, "伤害", 30));
            Assert.That(EquipBagModel.GemChoices(occupied, bag).Select(choice => choice.ConfigId), Is.EqualTo(new uint[] { 2101 }));

            Assert.That(EquipBagModel.GemChoices(null, bag), Is.Empty);
            Assert.That(EquipBagModel.GemChoices(Socket(0, "攻击孔", 1), null), Is.Empty, "人物背包还没拉到");
            Assert.That(EquipBagModel.GemChoices(Socket(0, "攻击孔", 1), Bag(100)), Is.Empty);
        }

        // ── EquipBagModel:强化前置检查 ───────────────────────

        [Test]
        public void EnhanceCheckReportsWhyAnItemCannotBeEnhancedAtAll()
        {
            var rich = Bag(999999, Stack(6001, Stone, 99));
            Assert.That(EquipBagModel.CheckEnhance(null, rich), Is.EqualTo(EquipEnhanceCheck.Unavailable));
            Assert.That(EquipBagModel.CheckEnhance(Weapon(0, 0, withNext: false), rich), Is.EqualTo(EquipEnhanceCheck.Unavailable),
                "上限为 0 = 强化未开放");
            Assert.That(EquipBagModel.CheckEnhance(Stack(6001, Stone, 99), rich), Is.EqualTo(EquipEnhanceCheck.Unavailable),
                "非装备的上限也是 0");
            Assert.That(EquipBagModel.CheckEnhance(Weapon(12, 12, withNext: false), rich), Is.EqualTo(EquipEnhanceCheck.MaxLevel));
            Assert.That(EquipBagModel.CheckEnhance(Weapon(13, 12, withNext: false), rich), Is.EqualTo(EquipEnhanceCheck.MaxLevel));
            Assert.That(EquipBagModel.CheckEnhance(Weapon(12, 12), rich), Is.EqualTo(EquipEnhanceCheck.MaxLevel),
                "满级优先于「有没有预览」");
            // 没满级却没有下一级预览:实例未初始化,或下一级的表行不可用。
            Assert.That(EquipBagModel.CheckEnhance(Weapon(3, 12, withNext: false), rich), Is.EqualTo(EquipEnhanceCheck.Unavailable));
            // 这三种与背包无关:快照没到也给得出结论。
            Assert.That(EquipBagModel.CheckEnhance(Weapon(12, 12, withNext: false), null), Is.EqualTo(EquipEnhanceCheck.MaxLevel));
            Assert.That(EquipBagModel.CheckEnhance(Weapon(3, 12, withNext: false), null), Is.EqualTo(EquipEnhanceCheck.Unavailable));
        }

        [Test]
        public void EnhanceCheckComparesMaterialThenGoldAgainstTheCharacterBag()
        {
            var item = Weapon(3, 12); // 下一级:金币 1000、强化石 ×2

            Assert.That(EquipBagModel.CheckEnhance(item, Bag(1000, Stack(6001, Stone, 2))), Is.EqualTo(EquipEnhanceCheck.None),
                "刚好够");
            Assert.That(EquipBagModel.CheckEnhance(item, Bag(1000, Stack(6001, Stone, 1), Stack(6002, Stone, 1))),
                Is.EqualTo(EquipEnhanceCheck.None), "材料分两堆:按 config 汇总");
            Assert.That(EquipBagModel.CheckEnhance(item, Bag(999999, Stack(6001, Stone, 1))),
                Is.EqualTo(EquipEnhanceCheck.MaterialNotEnough));
            Assert.That(EquipBagModel.CheckEnhance(item, Bag(999999)), Is.EqualTo(EquipEnhanceCheck.MaterialNotEnough));
            Assert.That(EquipBagModel.CheckEnhance(item, Bag(999, Stack(6001, Stone, 2))), Is.EqualTo(EquipEnhanceCheck.GoldNotEnough));
            // 两样都不够:与服务器的校验顺序一致,先报材料。数据层靠这一点逐码比对「服务器的说法与本地是否一致」。
            Assert.That(EquipBagModel.CheckEnhance(item, Bag(0)), Is.EqualTo(EquipEnhanceCheck.MaterialNotEnough));
        }

        [Test]
        public void EnhanceCheckSkipsCostsTheNextLevelDoesNotAskFor()
        {
            // 不要材料(config 为 0 / 数量为 0):背包里一颗强化石都没有也不卡。
            Assert.That(EquipBagModel.CheckEnhance(Weapon(0, 12, stones: 0), Bag(1000)), Is.EqualTo(EquipEnhanceCheck.None));
            var zeroCount = Weapon(0, 12);
            zeroCount.EnhanceNext.MaterialCount = 0;
            Assert.That(EquipBagModel.CheckEnhance(zeroCount, Bag(1000)), Is.EqualTo(EquipEnhanceCheck.None));
            // 不收金币:不必读货币,快照不带货币也给得出「可以」。
            Assert.That(EquipBagModel.CheckEnhance(Weapon(0, 12, cost: 0), Bag(null, Stack(6001, Stone, 2))),
                Is.EqualTo(EquipEnhanceCheck.None));
            Assert.That(EquipBagModel.CheckEnhance(Weapon(0, 12, cost: 0, stones: 0), null), Is.EqualTo(EquipEnhanceCheck.None));
        }

        [Test]
        public void EnhanceCheckIsUnknownWhenTheSnapshotCannotTell()
        {
            var item = Weapon(3, 12);
            // 不置灰:快照不全不能把人锁在按钮外面,能不能强化交给服务器。
            Assert.That(EquipBagModel.CheckEnhance(item, null), Is.EqualTo(EquipEnhanceCheck.Unknown), "人物背包还没拉到");
            Assert.That(EquipBagModel.CheckEnhance(item, Bag(null, Stack(6001, Stone, 2))), Is.EqualTo(EquipEnhanceCheck.Unknown),
                "快照不带货币");
            var emptyCurrency = Bag(null, Stack(6001, Stone, 2));
            emptyCurrency.Currency = new CurrencyComp();
            Assert.That(EquipBagModel.CheckEnhance(item, emptyCurrency), Is.EqualTo(EquipEnhanceCheck.Unknown), "货币数组里没有金币这一项");
            // 材料是看得出来的:货币缺失不妨碍报「材料不足」。
            Assert.That(EquipBagModel.CheckEnhance(item, Bag(null, Stack(6001, Stone, 1))),
                Is.EqualTo(EquipEnhanceCheck.MaterialNotEnough));
        }

        [Test]
        public void GoldIsReadFromTheSharedCurrencyIndex()
        {
            var item = Weapon(3, 12, stones: 0);
            var bag = Bag(null);
            bag.Currency = new CurrencyComp();
            bag.Currency.Values.AddRange(new ulong[] { 500, 999999 }); // 0 号是金币,1 号是别的货币
            Assert.That(PlayerFeaturesClient.GoldCurrencyIndex, Is.Zero, "镜像服务器 kCurrencyGold");
            Assert.That(EquipBagModel.CheckEnhance(item, bag), Is.EqualTo(EquipEnhanceCheck.GoldNotEnough), "别的货币再多也不算");
            bag.Currency.Values[PlayerFeaturesClient.GoldCurrencyIndex] = 1000;
            Assert.That(EquipBagModel.CheckEnhance(item, bag), Is.EqualTo(EquipEnhanceCheck.None));
        }
    }
}
