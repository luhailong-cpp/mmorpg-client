using System.Collections.Generic;
using MmorpgClient.Game.PlayerFeatures;

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

    /// <summary>装备上的一个宝石孔:孔位定义 + 此刻嵌在里面的宝石(空孔时 Info.GemConfigId 为 0)。</summary>
    public readonly struct EquipGemSocketView
    {
        /// <summary>孔位号(0 起),镶嵌 / 取下请求里填的就是它。</summary>
        public readonly uint Socket;
        /// <summary>孔位名;服务器没给时已退回「孔位 N」。</summary>
        public readonly string Name;
        public readonly EquipGemSocketInfo Info;

        public EquipGemSocketView(uint socket, string name, EquipGemSocketInfo info)
        {
            Socket = socket;
            Name = name;
            Info = info;
        }

        /// <summary>孔里有没有宝石。只看 gem_config_id,与服务器判「该孔已镶」同口径:占着就得先取下才能再嵌。</summary>
        public bool Occupied => Info != null && Info.GemConfigId != 0;
        /// <summary>嵌着的宝石此刻是否计入属性(带 attr)。已镶但不生效的宝石仍然占着孔,也仍然可以取下。</summary>
        public bool Active => Occupied && Info.Attr != null;
    }

    /// <summary>可以嵌进某个孔的一种宝石,以及它在人物背包里的总数。</summary>
    public readonly struct EquipGemChoice
    {
        /// <summary>宝石的 config(镶嵌请求里的 gem_config_id)。</summary>
        public readonly uint ConfigId;
        /// <summary>人物背包里这种宝石各堆数量之和。镶嵌按 config 扣 1 颗,不指定哪一堆,所以界面按种类列、不按堆列。</summary>
        public readonly ulong Count;
        /// <summary>这种宝石的显示信息(名称 / 图标 / gem_type / gem_attr),取背包里先出现的那一堆。</summary>
        public readonly BagItemInfo Item;

        public EquipGemChoice(uint configId, ulong count, BagItemInfo item)
        {
            ConfigId = configId;
            Count = count;
            Item = item;
        }
    }

    /// <summary>
    /// 强化前置检查的结论(<see cref="EquipBagModel.CheckEnhance"/>)。只按本地快照判断,用于按钮置灰;
    /// 能不能强化由服务器裁决。
    /// </summary>
    public enum EquipEnhanceCheck
    {
        /// <summary>按本地快照看可以强化。</summary>
        None = 0,
        /// <summary>这件物品不能强化:强化未开放(上限为 0,非装备也落在这里),或服务器没给下一级预览。</summary>
        Unavailable,
        MaxLevel,
        GoldNotEnough,
        MaterialNotEnough,
        /// <summary>本地无从判断(人物背包或其中的货币还没拉到):不置灰,让服务器裁决。</summary>
        Unknown,
    }

    /// <summary>
    /// 背包页装备相关的展示推导:槽位列表、选中项此刻落在哪个包、佩戴要求文案,以及强化 / 镶嵌的孔位视图、
    /// 可嵌宝石与强化前置检查。纯函数,不依赖 UnityEngine,可以脱离界面直接测。
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

        // ── 强化与镶嵌(服务端 docs/design/equipment-enhance-inlay.md §6.3)────────
        // 这一段凡是要看「背包里有什么」的函数都显式接收人物背包快照(PlayerFeaturesClient.CharacterBag,bag_type = 0):
        // 材料、宝石、金币只认它,不认「当前查看的那一类」(那可能是仓库)。

        /// <summary>
        /// 一件装备的孔位视图,按孔位号升序(含空孔)。孔位定义与占用全部来自服务器下发的 gem_sockets;
        /// 实例未初始化的装备不带孔位,结果为空。
        /// </summary>
        public static List<EquipGemSocketView> GemSockets(BagItemInfo item)
        {
            var result = new List<EquipGemSocketView>();
            foreach (var socket in EquipDisplay.SocketsOf(item))
                result.Add(new EquipGemSocketView(socket.Socket, EquipDisplay.SocketName(socket), socket));
            return result;
        }

        /// <summary>
        /// 按孔位号找孔;这件装备没有这个孔返回 null。同号多条(脏数据)取先出现的那一条,
        /// 与 <see cref="GemSockets"/> 留下的是同一条。
        /// </summary>
        public static EquipGemSocketInfo FindGemSocket(BagItemInfo item, uint socket)
        {
            if (item == null) return null;
            foreach (var entry in item.GemSockets)
                if (entry.Socket == socket) return entry;
            return null;
        }

        /// <summary>
        /// 背包里某种物品的总数:同一种可能分几堆,服务器按 config 扣(强化材料、镶嵌的宝石都是),所以按 config 汇总。
        /// 快照还没拉到(null)时为 0。
        /// </summary>
        public static ulong CountOf(BagInfo bag, uint configId)
        {
            ulong total = 0;
            if (bag == null) return total;
            foreach (var item in bag.Items)
                if (item.ConfigId == configId) total += item.Count;
            return total;
        }

        /// <summary>
        /// 这个孔能嵌哪些宝石:人物背包里的宝石物品(gem_type != 0)中,类型被这个孔接纳的(孔的 gem_type 为 0 = 任意,
        /// 否则要相等),按 config 汇总数量,config 升序。
        /// 只管类型合不合,不看孔是否已被占用 —— 占着的孔要先取下,由调用方按 <see cref="EquipGemSocketView.Occupied"/> 判。
        /// </summary>
        public static List<EquipGemChoice> GemChoices(EquipGemSocketInfo socket, BagInfo characterBag)
        {
            var result = new List<EquipGemChoice>();
            if (socket == null || characterBag == null) return result;

            var counts = new SortedDictionary<uint, ulong>();
            var samples = new Dictionary<uint, BagItemInfo>();
            foreach (var item in characterBag.Items)
            {
                if (item.GemType == 0 || item.ConfigId == 0 || item.Count == 0) continue;
                if (socket.GemType != 0 && socket.GemType != item.GemType) continue;
                counts[item.ConfigId] = counts.TryGetValue(item.ConfigId, out ulong count) ? count + item.Count : item.Count;
                if (!samples.ContainsKey(item.ConfigId)) samples[item.ConfigId] = item;
            }
            foreach (var pair in counts)
                result.Add(new EquipGemChoice(pair.Key, pair.Value, samples[pair.Key]));
            return result;
        }

        /// <summary>
        /// 强化前置检查:按本地快照看这件装备此刻能不能强化、卡在哪。用于按钮置灰;数据层也用同一个结论判断
        /// 「金币 / 材料不足」被服务器拒绝后要不要重拉快照(PlayerFeaturesClient.IsRuleRejection)。
        /// 检查顺序与服务器一致(未开放 → 满级 → 下一级不可用 → 材料 → 金币),这样同一份数据下两边给出的原因相同。
        /// 金币读人物背包的 Currency.Values[GoldCurrencyIndex];背包或货币没拉到时回 <see cref="EquipEnhanceCheck.Unknown"/>
        /// (不置灰:快照不全不能把人锁在按钮外面)。
        /// </summary>
        public static EquipEnhanceCheck CheckEnhance(BagItemInfo item, BagInfo characterBag)
        {
            if (item == null || item.EnhanceMaxLevel == 0) return EquipEnhanceCheck.Unavailable;
            if (item.EnhanceLevel >= item.EnhanceMaxLevel) return EquipEnhanceCheck.MaxLevel;
            var next = item.EnhanceNext;
            if (next == null) return EquipEnhanceCheck.Unavailable;

            if (next.MaterialConfigId != 0 && next.MaterialCount != 0)
            {
                if (characterBag == null) return EquipEnhanceCheck.Unknown;
                if (CountOf(characterBag, next.MaterialConfigId) < next.MaterialCount) return EquipEnhanceCheck.MaterialNotEnough;
            }
            if (next.CostGold != 0)
            {
                var currency = characterBag?.Currency;
                if (currency == null || currency.Values.Count <= PlayerFeaturesClient.GoldCurrencyIndex)
                    return EquipEnhanceCheck.Unknown;
                if (currency.Values[PlayerFeaturesClient.GoldCurrencyIndex] < next.CostGold) return EquipEnhanceCheck.GoldNotEnough;
            }
            return EquipEnhanceCheck.None;
        }
    }
}
