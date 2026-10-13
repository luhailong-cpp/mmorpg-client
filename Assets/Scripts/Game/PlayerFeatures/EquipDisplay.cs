using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MmorpgClient.Game.PlayerFeatures
{
    /// <summary>
    /// 装备属性行的颜色档。数值即协议 EquipAttrLineInfo.tier
    /// (服务端 equiprules::AffixTier,docs/design/equipment-attributes.md §3.2)。
    /// </summary>
    public enum EquipAttrTier
    {
        /// <summary>基础属性(武器「伤害」、鞋「防御」「速度」),没有上限。</summary>
        Base = 0,
        Blue = 1,
        Pink = 2,
        Yellow = 3,
        /// <summary>预留:本期服务器不掷、不下发。</summary>
        Green = 4,
        /// <summary>客户端不认识的档(服务器先于客户端加了新档):按随机属性的格式显示,颜色由 UI 兜底。</summary>
        Unknown = 255,
    }

    /// <summary>
    /// 装备 tooltip 的显示口径。纯函数,不依赖 UnityEngine,可在 EditMode 里直接测。
    /// 客户端零配表:名称、上限、是否百分比全部由服务器随 <see cref="EquipAttrLineInfo"/> 下发,
    /// 这里只管排版与顺序,不推算任何数值。返回的都是纯文本——服务器下发的名称原样拼入,
    /// UI 必须一行一个 Text 且关掉 richText。
    /// 强化与镶嵌(服务端 docs/design/equipment-enhance-inlay.md §6.2)同理:强化增量、下一级预览值、
    /// 宝石属性都是服务器算好下发的,这里不拿加成万分比去算任何属性值。
    /// </summary>
    public static class EquipDisplay
    {
        public static EquipAttrTier TierOf(uint tier) => tier switch
        {
            0 => EquipAttrTier.Base,
            1 => EquipAttrTier.Blue,
            2 => EquipAttrTier.Pink,
            3 => EquipAttrTier.Yellow,
            4 => EquipAttrTier.Green,
            _ => EquipAttrTier.Unknown,
        };

        /// <summary>
        /// 一行属性的显示文本。基础属性「名称：值」;随机属性「名称 值/上限」;
        /// percent 为 true 时值与上限都带 %(「物理必杀率 8%/10%」)。
        /// 走哪种格式只看 tier:基础属性行即使带了上限也不显示。
        /// 强化过的基础属性行在值后面标出强化带来的增量「伤害：46（+6）」:值已经含增量(服务器算好的),
        /// 这里只是把 enhance_bonus 原样摆出来;未强化(增量为 0)的行与一期逐字相同。
        /// </summary>
        public static string FormatLine(EquipAttrLineInfo line)
        {
            if (line == null) return string.Empty;
            string name = AttrName(line);
            string value = FormatValue(line.Value, line.Percent);
            if (TierOf(line.Tier) == EquipAttrTier.Base)
                return line.EnhanceBonus == 0 ? $"{name}：{value}"
                    : $"{name}：{value}（+{FormatValue(line.EnhanceBonus, line.Percent)}）";
            // 随机属性没给上限(缺配)时只显示当前值,不显示「/0」。
            return line.Cap == 0 ? $"{name} {value}" : $"{name} {value}/{FormatValue(line.Cap, line.Percent)}";
        }

        /// <summary>
        /// 基础属性行的下一级预览「伤害：46 → 48」:当前值 → 强化到下一级后的值(enhance_next_value,服务器算好的)。
        /// 只对带 enhance_next 的装备的基础属性行有意义,见 <see cref="EnhancePreviewLines"/>。
        /// </summary>
        public static string FormatEnhancePreviewLine(EquipAttrLineInfo line)
        {
            if (line == null) return string.Empty;
            return $"{AttrName(line)}：{FormatValue(line.Value, line.Percent)} → {FormatValue(line.EnhanceNextValue, line.Percent)}";
        }

        /// <summary>
        /// 一件装备的下一级预览行(每条基础属性一行,seq 升序)。装备不带 enhance_next
        /// (已满级 / 未开放 / 实例未初始化 / 下一级不可用)时为空:那时各行的 enhance_next_value 是 0,不能拿来显示。
        /// </summary>
        public static List<string> EnhancePreviewLines(BagItemInfo item)
        {
            if (item?.EnhanceNext == null) return new List<string>();
            return SortLines(item.BaseAttrs).Select(FormatEnhancePreviewLine).ToList();
        }

        /// <summary>带强化等级的名称「青铜剑 +3」;0 级不加后缀。name 由调用方给(服务器缺名时的占位也由调用方决定)。</summary>
        public static string NameWithEnhance(string name, uint enhanceLevel)
            => enhanceLevel == 0 ? name : $"{name} +{enhanceLevel.ToString(CultureInfo.InvariantCulture)}";

        /// <summary>
        /// 成功率文本。入参是万分比;显示成百分数,至多两位小数并去掉末尾的 0(9000 → 90%,1250 → 12.5%,1 → 0.01%)。
        /// 全程整数运算,不经浮点。
        /// </summary>
        public static string FormatSuccessRate(uint ratePermyriad)
        {
            string whole = (ratePermyriad / 100).ToString(CultureInfo.InvariantCulture);
            uint fraction = ratePermyriad % 100;
            if (fraction == 0) return whole + "%";
            return fraction % 10 == 0
                ? $"{whole}.{(fraction / 10).ToString(CultureInfo.InvariantCulture)}%"
                : $"{whole}.{fraction.ToString("00", CultureInfo.InvariantCulture)}%";
        }

        /// <summary>
        /// 强化消耗「金币 5000、强化石 ×2」。不要材料(material_config_id 为 0 或数量为 0)时只有金币一段。
        /// 材料名服务器没给(它的 Item 行查不到)时退回带 id 的占位。
        /// </summary>
        public static string FormatEnhanceCost(EquipEnhancePreview next)
        {
            if (next == null) return string.Empty;
            string gold = $"金币 {next.CostGold.ToString(CultureInfo.InvariantCulture)}";
            if (next.MaterialConfigId == 0 || next.MaterialCount == 0) return gold;
            return $"{gold}、{ItemNameOr(next.MaterialName, next.MaterialConfigId)} ×{next.MaterialCount.ToString(CultureInfo.InvariantCulture)}";
        }

        /// <summary>物品名;为空时退回带 id 的占位,不编造名称(与 GameplayUiArt.ItemName 逐字同口径)。</summary>
        public static string ItemNameOr(string name, uint configId)
            => string.IsNullOrWhiteSpace(name) ? $"物品 · {configId.ToString(CultureInfo.InvariantCulture)}" : name;

        /// <summary>孔位显示名;服务器没配名字时退回「孔位 N」(N 从 1 数起,孔位号从 0 起)。</summary>
        public static string SocketName(EquipGemSocketInfo socket)
        {
            if (socket == null) return string.Empty;
            return string.IsNullOrWhiteSpace(socket.Name)
                ? $"孔位 {((ulong)socket.Socket + 1).ToString(CultureInfo.InvariantCulture)}" : socket.Name;
        }

        /// <summary>宝石提供的属性「伤害 +30」;用于宝石物品的说明行(gem_attr)与孔位行。</summary>
        public static string FormatGemAttr(EquipAttrLineInfo attr)
            => attr == null ? string.Empty : $"{AttrName(attr)} +{FormatValue(attr.Value, attr.Percent)}";

        /// <summary>
        /// 一个孔位的显示文本。空孔「攻击孔：未镶嵌」;已镶且生效「攻击孔：攻击宝石·一级（伤害 +30）」;
        /// 已镶但当前不生效「攻击孔：攻击宝石·一级（未生效）」。
        /// 「占用」只看 gem_config_id,「生效」只看带不带 attr —— 与服务器判占用、算加成的两个口径分别对应。
        /// </summary>
        public static string FormatSocketLine(EquipGemSocketInfo socket)
        {
            if (socket == null) return string.Empty;
            string name = SocketName(socket);
            if (socket.GemConfigId == 0) return $"{name}：未镶嵌";
            string gem = ItemNameOr(socket.GemName, socket.GemConfigId);
            return socket.Attr == null ? $"{name}：{gem}（未生效）" : $"{name}：{gem}（{FormatGemAttr(socket.Attr)}）";
        }

        /// <summary>
        /// 一件装备的全部孔位,按孔位号升序,返回新列表、不改入参。顺序由 socket 字段显式给出,不靠下标;
        /// 同一孔位号出现多条(脏数据)时只留第一条。实例未初始化的装备服务器不下发孔位,结果为空。
        /// </summary>
        public static List<EquipGemSocketInfo> SocketsOf(BagItemInfo item)
        {
            var result = new List<EquipGemSocketInfo>();
            if (item == null) return result;
            var seen = new HashSet<uint>();
            // OrderBy 是稳定排序:同号的保持传入先后,所以留下的是先出现的那一条。
            foreach (var socket in item.GemSockets.OrderBy(entry => entry.Socket))
                if (seen.Add(socket.Socket)) result.Add(socket);
            return result;
        }

        /// <summary>数值文本:百分比是整数百分点,直接接 %,不做小数换算。</summary>
        public static string FormatValue(ulong value, bool percent)
        {
            string text = value.ToString(CultureInfo.InvariantCulture);
            return percent ? text + "%" : text;
        }

        /// <summary>属性名;服务器缺配(空名)时退回带 id 的占位,不编造名称(与 GameplayUiArt.ItemName 同口径)。</summary>
        public static string AttrName(EquipAttrLineInfo line)
            => string.IsNullOrWhiteSpace(line.Name) ? $"属性 · {line.AttrId}" : line.Name;

        /// <summary>
        /// 按 (tier, seq) 升序的稳定排序,返回新列表、不改入参。显示顺序由这两个字段显式给出,
        /// repeated 的下标不承载语义;(tier, seq) 相同的行保持传入时的先后。
        /// </summary>
        public static List<EquipAttrLineInfo> SortLines(IEnumerable<EquipAttrLineInfo> lines)
        {
            if (lines == null) return new List<EquipAttrLineInfo>();
            // OrderBy / ThenBy 是稳定排序(List.Sort 不是)。
            return lines.Where(line => line != null).OrderBy(line => line.Tier).ThenBy(line => line.Seq).ToList();
        }

        /// <summary>一件装备的全部属性行,已按显示顺序排好:基础属性在前,随后蓝 / 粉 / 黄 / 绿。</summary>
        public static List<EquipAttrLineInfo> LinesOf(BagItemInfo item)
            => item == null ? new List<EquipAttrLineInfo>() : SortLines(item.BaseAttrs.Concat(item.Affixes));
    }
}
