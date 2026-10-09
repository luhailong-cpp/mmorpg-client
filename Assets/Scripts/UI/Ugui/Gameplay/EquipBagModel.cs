using System.Collections.Generic;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>装备栏里的一格:槽位定义 + 此刻占着它的那件装备(空槽为 null)。</summary>
    public readonly struct EquipSlotView
    {
        public readonly uint Slot;
        /// <summary>部位名;服务器没给时已退回「栏位 N」。</summary>
        public readonly string Name;
        public readonly BagItemInfo Item;

        public EquipSlotView(uint slot, string name, BagItemInfo item)
        {
            Slot = slot;
            Name = name;
            Item = item;
        }
    }

    /// <summary>
    /// 背包页装备相关的展示推导:槽位列表、选中项此刻落在哪个包、佩戴要求文案。
    /// 纯函数,不依赖 UnityEngine,可以脱离界面直接测。
    /// 槽位定义、部位名、占用关系全部来自服务器下发的装备栏快照(bag_type = 2),客户端零配表。
    /// </summary>
    public static class EquipBagModel
    {
        /// <summary>
        /// 装备栏的全部格子,按槽号升序(含空槽)。槽位定义取 Layout.EquipSlots,占用关系取 Layout.Slots。
        /// 被占着却不在定义里的槽(老服务器不下发定义,或表被改过)也画出来,否则那件装备点不到、卸不下。
        /// 占用记录指向 Items 里没有的物品时当空槽。
        /// </summary>
        public static List<EquipSlotView> Slots(BagInfo equipment)
        {
            var result = new List<EquipSlotView>();
            if (equipment?.Layout == null) return result;

            var items = new Dictionary<ulong, BagItemInfo>();
            foreach (var item in equipment.Items)
                if (item.ItemId != 0 && !items.ContainsKey(item.ItemId)) items[item.ItemId] = item;
            var worn = new Dictionary<uint, BagItemInfo>();
            foreach (var slot in equipment.Layout.Slots)
                if (!worn.ContainsKey(slot.Slot) && items.TryGetValue(slot.ItemId, out var item)) worn[slot.Slot] = item;

            var names = new SortedDictionary<uint, string>();
            foreach (var definition in equipment.Layout.EquipSlots)
                if (!names.ContainsKey(definition.Slot)) names[definition.Slot] = definition.Name;
            foreach (uint slot in worn.Keys)
                if (!names.ContainsKey(slot)) names[slot] = string.Empty;

            foreach (var pair in names)
                result.Add(new EquipSlotView(pair.Key, SlotName(pair.Key, pair.Value),
                    worn.TryGetValue(pair.Key, out var item) ? item : null));
            return result;
        }

        /// <summary>部位名;服务器缺配(空名)时退回带槽号的占位,不编造名称。</summary>
        public static string SlotName(uint slot, string name)
            => string.IsNullOrWhiteSpace(name) ? $"栏位 {slot}" : name;

        /// <summary>穿在身上的那件;不在装备栏里返回 null。</summary>
        public static BagItemInfo FindWorn(IReadOnlyList<EquipSlotView> slots, ulong itemId)
        {
            if (slots == null || itemId == 0) return null;
            foreach (var view in slots)
                if (view.Item != null && view.Item.ItemId == itemId) return view.Item;
            return null;
        }

        /// <summary>
        /// 选中项只按 item_id 记忆,它此刻在哪个包里每次现查:穿上 / 卸下之后同一件装备换了包,选中跟着走。
        /// 装备栏优先;哪边都找不到(被筛掉、已不在包里、另一份快照还没到)时退回第一件可见的背包物品,
        /// 没有可见物品就是 0。返回的 id 一定指向传入快照里存在的物品,不会指向已消失的对象。
        /// </summary>
        /// <param name="worn">返回的物品是否在装备栏里。</param>
        public static ulong ResolveSelection(ulong remembered, IReadOnlyList<EquipSlotView> slots,
            IEnumerable<ulong> visibleBagItems, out bool worn)
        {
            worn = FindWorn(slots, remembered) != null;
            if (worn) return remembered;
            ulong first = 0;
            if (visibleBagItems != null)
                foreach (ulong id in visibleBagItems)
                {
                    if (id == 0) continue;
                    if (id == remembered) return id;
                    if (first == 0) first = id;
                }
            return first;
        }

        /// <summary>等级要求行。全角冒号与 EquipDisplay 的基础属性行同口径。</summary>
        public static string LevelRequirement(BagItemInfo item) => $"角色要求：等级 {item.EquipLevel}";

        /// <summary>职业要求行:不限显示「无」;有限制但服务器没给职业名时退回带 id 的占位。</summary>
        public static string ClassRequirement(BagItemInfo item)
        {
            string name = item.EquipClass == 0 ? "无" :
                string.IsNullOrWhiteSpace(item.EquipClassName) ? $"职业 · {item.EquipClass}" : item.EquipClassName;
            return $"职业要求：{name}";
        }

        /// <summary>
        /// 角色等级够不够。只是给要求行标红的提示,能不能穿由服务器裁决;
        /// 不知道角色等级(0:属性面板还没拉到)时不标红。
        /// </summary>
        public static bool LevelUnmet(BagItemInfo item, uint characterLevel)
            => characterLevel != 0 && characterLevel < item.EquipLevel;
    }
}
