using System.Linq;
using MmorpgClient.UI.Ugui.Gameplay;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 背包页装备相关的纯展示推导(<see cref="EquipBagModel"/>):槽位列表、选中项落在哪个包、佩戴要求文案。
    /// 不碰 UnityEngine;界面层面的用例在 EquipWindowTests。
    /// </summary>
    public sealed class EquipBagModelTests
    {
        private static BagInfo Equipment(params (uint Slot, string Name)[] definitions)
        {
            var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = 2, Capacity = 7 } };
            foreach (var (slot, name) in definitions)
                bag.Layout.EquipSlots.Add(new EquipSlotInfo { Slot = slot, EquipKind = 11 + slot, Name = name });
            return bag;
        }

        private static void Wear(BagInfo equipment, uint slot, ulong itemId)
        {
            equipment.Items.Add(new BagItemInfo { ItemId = itemId, ConfigId = 1105, Count = 1, MaxStack = 1, EquipKind = 11 });
            equipment.Layout.Slots.Add(new BagSlotInfo { Slot = slot, ItemId = itemId, Width = 1, Height = 1 });
        }

        // ── 槽位列表 ────────────────────────────────────────

        [Test]
        public void SlotsListEveryDefinitionInSlotOrderIncludingEmptyOnes()
        {
            // 服务器约定按 slot 升序下发,但顺序由字段给出,不靠下标:乱序输入也要排好。
            var equipment = Equipment((6, "鞋子"), (3, "武器"), (5, "衣服"), (4, "帽子"));
            Wear(equipment, 6, 7001);

            var slots = EquipBagModel.Slots(equipment);

            Assert.That(slots.Select(view => view.Slot), Is.EqualTo(new uint[] { 3, 4, 5, 6 }));
            Assert.That(slots.Select(view => view.Name), Is.EqualTo(new[] { "武器", "帽子", "衣服", "鞋子" }));
            Assert.That(slots.Take(3).All(view => view.Item == null), Is.True, "没穿的部位是空槽");
            Assert.That(slots[3].Item.ItemId, Is.EqualTo(7001));
        }

        [TestCase("")]
        [TestCase("  ")]
        public void UnnamedSlotFallsBackToItsNumber(string name)
        {
            var slots = EquipBagModel.Slots(Equipment((0, name), (3, "武器")));
            Assert.That(slots.Select(view => view.Name), Is.EqualTo(new[] { "栏位 0", "武器" }));
        }

        [Test]
        public void OccupiedSlotWithoutDefinitionIsStillListedSoItsItemCanBeTakenOff()
        {
            // 老服务器不下发 equip_slots,或表里删了某个槽:穿着的装备不能因此点不到。
            var equipment = Equipment((3, "武器"));
            Wear(equipment, 9, 7009);
            var slots = EquipBagModel.Slots(equipment);
            Assert.That(slots.Select(view => view.Slot), Is.EqualTo(new uint[] { 3, 9 }));
            Assert.That(slots[1].Name, Is.EqualTo("栏位 9"));
            Assert.That(slots[1].Item.ItemId, Is.EqualTo(7009));

            var legacy = new BagInfo { Layout = new BagLayoutInfo { BagType = 2 } };
            Wear(legacy, 1, 7001);
            Assert.That(EquipBagModel.Slots(legacy).Single().Item.ItemId, Is.EqualTo(7001));
        }

        [Test]
        public void InconsistentSnapshotsNeverProduceDanglingSlots()
        {
            Assert.That(EquipBagModel.Slots(null), Is.Empty);
            Assert.That(EquipBagModel.Slots(new BagInfo()), Is.Empty, "没有 Layout = 还没拉到装备栏");

            // 占用记录指向 Items 里没有的物品:当空槽;同一槽 / 同一定义重复出现取第一条。
            var equipment = Equipment((3, "武器"), (3, "重复定义"), (4, "帽子"));
            equipment.Layout.Slots.Add(new BagSlotInfo { Slot = 4, ItemId = 4040 });
            Wear(equipment, 3, 7001);
            Wear(equipment, 3, 7002);
            var slots = EquipBagModel.Slots(equipment);
            Assert.That(slots.Select(view => view.Slot), Is.EqualTo(new uint[] { 3, 4 }));
            Assert.That(slots[0].Name, Is.EqualTo("武器"));
            Assert.That(slots[0].Item.ItemId, Is.EqualTo(7001));
            Assert.That(slots[1].Item, Is.Null);
        }

        // ── 选中项跟随 ──────────────────────────────────────

        [Test]
        public void SelectionFollowsTheSameItemAcrossBags()
        {
            var before = Equipment((3, "武器"), (6, "鞋子"));
            Wear(before, 6, 7001);
            var after = Equipment((3, "武器"), (6, "鞋子"));
            Wear(after, 6, 7001);
            Wear(after, 3, 5001);

            // 穿上之前:5001 在人物背包里。
            Assert.That(EquipBagModel.ResolveSelection(5001, EquipBagModel.Slots(before), new ulong[] { 9001, 5001 }, out bool worn),
                Is.EqualTo(5001));
            Assert.That(worn, Is.False);
            // 穿上之后:背包里没有它了,它在装备栏里,选中跟过去。
            Assert.That(EquipBagModel.ResolveSelection(5001, EquipBagModel.Slots(after), new ulong[] { 9001 }, out worn),
                Is.EqualTo(5001));
            Assert.That(worn, Is.True);
            // 卸下之后又回到背包。
            Assert.That(EquipBagModel.ResolveSelection(5001, EquipBagModel.Slots(before), new ulong[] { 9001, 5001 }, out worn),
                Is.EqualTo(5001));
            Assert.That(worn, Is.False);
        }

        [Test]
        public void SelectionNeverPointsAtAnItemThatIsGone()
        {
            var equipment = Equipment((3, "武器"));
            Wear(equipment, 3, 7001);
            var slots = EquipBagModel.Slots(equipment);

            // 记住的物品哪边都不在:退回第一件可见的背包物品,且不算穿着。
            Assert.That(EquipBagModel.ResolveSelection(4444, slots, new ulong[] { 9001, 9002 }, out bool worn), Is.EqualTo(9001));
            Assert.That(worn, Is.False);
            // 还没点过任何东西(0)同理。
            Assert.That(EquipBagModel.ResolveSelection(0, slots, new ulong[] { 9002 }, out worn), Is.EqualTo(9002));
            Assert.That(worn, Is.False);
            // 没有可见物品:不选中任何东西,不会自己跳到装备栏。
            Assert.That(EquipBagModel.ResolveSelection(4444, slots, new ulong[0], out worn), Is.Zero);
            Assert.That(worn, Is.False);
            Assert.That(EquipBagModel.ResolveSelection(4444, slots, null, out worn), Is.Zero);
            Assert.That(EquipBagModel.ResolveSelection(0, null, null, out worn), Is.Zero);
            Assert.That(worn, Is.False);
        }

        [Test]
        public void WornItemStaysSelectedWhateverTheBagFilterShows()
        {
            var equipment = Equipment((3, "武器"));
            Wear(equipment, 3, 7001);
            var slots = EquipBagModel.Slots(equipment);

            // 装备栏不受背包的搜索 / 分类筛选影响;行囊还没到(null)时也能选中身上的装备。
            Assert.That(EquipBagModel.ResolveSelection(7001, slots, new ulong[] { 9001 }, out bool worn), Is.EqualTo(7001));
            Assert.That(worn, Is.True);
            Assert.That(EquipBagModel.ResolveSelection(7001, slots, null, out worn), Is.EqualTo(7001));
            Assert.That(worn, Is.True);
            // 两份快照先后到达的中间态里同一件可能两边都有:以装备栏为准。
            Assert.That(EquipBagModel.ResolveSelection(7001, slots, new ulong[] { 7001 }, out worn), Is.EqualTo(7001));
            Assert.That(worn, Is.True);
            Assert.That(EquipBagModel.FindWorn(slots, 7001).ItemId, Is.EqualTo(7001));
            Assert.That(EquipBagModel.FindWorn(slots, 9001), Is.Null);
            Assert.That(EquipBagModel.FindWorn(slots, 0), Is.Null);
        }

        // ── 佩戴要求 ────────────────────────────────────────

        [Test]
        public void RequirementLinesComeFromServerFields()
        {
            var unrestricted = new BagItemInfo { EquipLevel = 80 };
            Assert.That(EquipBagModel.LevelRequirement(unrestricted), Is.EqualTo("角色要求：等级 80"));
            Assert.That(EquipBagModel.ClassRequirement(unrestricted), Is.EqualTo("职业要求：无"));

            var named = new BagItemInfo { EquipLevel = 20, EquipClass = 3, EquipClassName = "金系" };
            Assert.That(EquipBagModel.ClassRequirement(named), Is.EqualTo("职业要求：金系"));
            // 有职业限制但服务器没给名字:显示 id 占位,不编造职业名,也不能显示成「无」。
            var unnamed = new BagItemInfo { EquipLevel = 20, EquipClass = 3 };
            Assert.That(EquipBagModel.ClassRequirement(unnamed), Is.EqualTo("职业要求：职业 · 3"));
        }

        [TestCase(0u, false, TestName = "LevelUnmet_UnknownCharacterLevelIsNotFlagged")]
        [TestCase(79u, true, TestName = "LevelUnmet_BelowRequirement")]
        [TestCase(80u, false, TestName = "LevelUnmet_ExactlyAtRequirement")]
        [TestCase(120u, false, TestName = "LevelUnmet_AboveRequirement")]
        public void LevelRequirementIsFlaggedOnlyWhenKnownAndTooLow(uint characterLevel, bool unmet)
            => Assert.That(EquipBagModel.LevelUnmet(new BagItemInfo { EquipLevel = 80 }, characterLevel), Is.EqualTo(unmet));
    }
}
