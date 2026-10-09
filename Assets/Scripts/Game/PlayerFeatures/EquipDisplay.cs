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
        /// </summary>
        public static string FormatLine(EquipAttrLineInfo line)
        {
            if (line == null) return string.Empty;
            string name = AttrName(line);
            string value = FormatValue(line.Value, line.Percent);
            if (TierOf(line.Tier) == EquipAttrTier.Base) return $"{name}：{value}";
            // 随机属性没给上限(缺配)时只显示当前值,不显示「/0」。
            return line.Cap == 0 ? $"{name} {value}" : $"{name} {value}/{FormatValue(line.Cap, line.Percent)}";
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
