using System;
using System.Collections.Generic;
using System.Linq;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 装备流派方案预设的纯 C# 展示与防误卖模型（不引用 UnityEngine，供 UI 与 EditMode NUnit 单测复用）。
    /// 所有方案、槽位缺失态、持有量与价格均取自服务端 <see cref="EquipLoadoutSnapshot"/>。
    /// </summary>
    public static class EquipLoadoutModel
    {
        public const uint DefaultUnlockItemConfigId = 3413u;
        public const uint MaxSchemes = 5u;
        public const int MaxNameCodePoints = 12;
        public const string ResourceRoot = "UI/Ugui/EquipLoadoutV1/";

        public const string PanelMainKey = ResourceRoot + "panel_equip_loadout_main";
        public const string PanelUnlockKey = ResourceRoot + "panel_equip_loadout_unlock";
        public const string ExpandTalismanIconKey = ResourceRoot + "icon_loadout_expand_talisman";
        public const string TabActiveKey = ResourceRoot + "tab_loadout_active";
        public const string TabUnlockedKey = ResourceRoot + "tab_loadout_unlocked";
        public const string TabLockedKey = ResourceRoot + "tab_loadout_locked";
        public const string BadgeInUseKey = ResourceRoot + "badge_loadout_in_use";
        public const string BadgeSlotMissingKey = ResourceRoot + "badge_loadout_slot_missing";

        private static readonly string[] SchemeOrdinals = { "一", "二", "三", "四", "五" };
        private static readonly string[] RoleTagNames = { "默认", "输出", "控场", "辅助", "生存" };

        public static string DefaultSchemeName(uint schemeId)
            => schemeId >= 1u && schemeId <= (uint)SchemeOrdinals.Length
                ? "方案" + SchemeOrdinals[schemeId - 1]
                : "方案" + schemeId;

        public static string RoleTagName(uint roleTag)
            => roleTag < (uint)RoleTagNames.Length ? RoleTagNames[roleTag] : RoleTagNames[0];

        public static string SchemeDisplayName(EquipLoadoutSchemeInfo scheme)
        {
            if (scheme == null) return DefaultSchemeName(1u);
            return string.IsNullOrWhiteSpace(scheme.Name) ? DefaultSchemeName(scheme.SchemeId) : scheme.Name.Trim();
        }

        public static string FormatTabLabel(EquipLoadoutSchemeInfo scheme, bool isActive)
        {
            if (scheme == null) return DefaultSchemeName(1u);
            if (!scheme.Unlocked) return "+ " + DefaultSchemeName(scheme.SchemeId);
            string name = SchemeDisplayName(scheme);
            string suffix = scheme.RoleTag > 0u && scheme.RoleTag < (uint)RoleTagNames.Length
                ? " · " + RoleTagNames[scheme.RoleTag]
                : string.Empty;
            return name + suffix;
        }

        public static string FormatQuickBarLabel(EquipLoadoutSchemeInfo scheme)
        {
            if (scheme == null) return DefaultSchemeName(1u);
            if (!scheme.Unlocked) return "+ " + DefaultSchemeName(scheme.SchemeId);
            string name = SchemeDisplayName(scheme);
            return name.Length > 6 ? name.Substring(0, 6) : name;
        }

        public static string SlotLabel(EquipLoadoutSlotView slot)
        {
            if (slot == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(slot.SlotName)) return slot.SlotName;
            return slot.Slot switch
            {
                3u => "武器",
                4u => "帽子",
                5u => "衣服",
                6u => "鞋子",
                7u => "首饰一",
                8u => "首饰二",
                9u => "法宝一",
                10u => "法宝二",
                _ => "槽位" + slot.Slot,
            };
        }

        public static string SlotStatusText(EquipLoadoutSlotStatus status) => status switch
        {
            EquipLoadoutSlotStatus.EquipLoadoutSlotEquipped => "已穿戴",
            EquipLoadoutSlotStatus.EquipLoadoutSlotInBag => "背包中",
            EquipLoadoutSlotStatus.EquipLoadoutSlotMissingOrStored => "缺失 / 不在背包",
            _ => "空槽位",
        };

        public static uint TierCostDiamond(uint targetSchemeId) => targetSchemeId switch
        {
            2u => 60u,
            3u => 120u,
            4u => 180u,
            5u => 240u,
            _ => 0u,
        };

        public static string FormatUnlockDescription(uint targetSchemeId)
            => $"解锁第 {targetSchemeId} 套装备流派方案位，支持独立保存 8 槽装备引用，一键切换流派并联动战法与加点方案；仅提供切换便利，不附带额外战力属性";

        public static string FormatUnlockTalismanLine(EquipLoadoutSnapshot snapshot)
        {
            ulong owned = snapshot?.UnlockItemOwned ?? 0UL;
            return $"优先消耗：装备方案扩容符 ×1（持有 {owned}）";
        }

        public static string FormatUnlockCurrencyLine(EquipLoadoutSnapshot snapshot, bool useBindDiamondFirst)
        {
            if (snapshot == null || snapshot.UnlockedSchemeCount >= snapshot.MaxSchemeCount)
                return "已解锁全部方案位";
            ulong cost = snapshot.NextUnlockCostDiamond;
            if (snapshot.UnlockItemOwned >= 1UL)
                return $"或消耗：{cost} 灵石（本次将优先扣除扩容符，不消耗灵石）";
            if (snapshot.NextUnlockAllowBindDiamond && useBindDiamondFirst)
            {
                ulong fromBind = Math.Min(snapshot.BindDiamondBalance, cost);
                ulong fromDiamond = cost - fromBind;
                return fromDiamond == 0UL
                    ? $"或消耗：{cost} 绑定灵石（持有绑灵 {snapshot.BindDiamondBalance} / 灵石 {snapshot.DiamondBalance}）"
                    : $"或消耗：{fromBind} 绑灵 + {fromDiamond} 灵石（持有绑灵 {snapshot.BindDiamondBalance} / 灵石 {snapshot.DiamondBalance}）";
            }
            return $"或消耗：{cost} 灵石（持有灵石 {snapshot.DiamondBalance}）";
        }

        public static bool CanAffordNextUnlock(EquipLoadoutSnapshot snapshot, bool useBindDiamondFirst)
        {
            if (snapshot == null || snapshot.UnlockedSchemeCount >= snapshot.MaxSchemeCount) return false;
            if (snapshot.UnlockItemOwned >= 1UL) return true;
            ulong cost = snapshot.NextUnlockCostDiamond;
            if (cost == 0UL) return false;
            if (snapshot.NextUnlockAllowBindDiamond && useBindDiamondFirst)
            {
                ulong fromBind = Math.Min(snapshot.BindDiamondBalance, cost);
                return snapshot.DiamondBalance >= cost - fromBind;
            }
            return snapshot.DiamondBalance >= cost;
        }

        public static string FormatMissingSlotsNotice(IEnumerable<uint> missingSlots, EquipLoadoutSchemeInfo scheme)
        {
            if (missingSlots == null) return string.Empty;
            var names = new List<string>();
            foreach (uint slotId in missingSlots)
            {
                var found = scheme?.Slots.FirstOrDefault(s => s.Slot == slotId);
                names.Add(found != null ? SlotLabel(found) : SlotLabel(new EquipLoadoutSlotView { Slot = slotId }));
            }
            return names.Count == 0
                ? string.Empty
                : $"以下槽位装备不在背包，已保持原穿戴不动：{string.Join("、", names)}";
        }

        /// <summary>
        /// 统计当前被任意已解锁方案引用的装备实例 item_guid -> 所属方案短标签列表（供背包角标与交易/解封防误卖保护使用）。
        /// </summary>
        public static Dictionary<ulong, List<string>> BuildItemReferenceMap(EquipLoadoutSnapshot snapshot)
        {
            var map = new Dictionary<ulong, List<string>>();
            if (snapshot == null) return map;
            foreach (var scheme in snapshot.Schemes.OrderBy(s => s.SchemeId))
            {
                if (!scheme.Unlocked) continue;
                string label = DefaultSchemeName(scheme.SchemeId);
                foreach (var slot in scheme.Slots)
                {
                    if (slot.ItemGuid == 0UL) continue;
                    if (!map.TryGetValue(slot.ItemGuid, out var list))
                    {
                        list = new List<string>();
                        map[slot.ItemGuid] = list;
                    }
                    if (!list.Contains(label)) list.Add(label);
                }
            }
            return map;
        }

        public static IReadOnlyList<string> ReferencedSchemesOf(EquipLoadoutSnapshot snapshot, ulong itemGuid)
        {
            if (snapshot == null || itemGuid == 0UL) return Array.Empty<string>();
            var map = BuildItemReferenceMap(snapshot);
            return map.TryGetValue(itemGuid, out var list) ? (IReadOnlyList<string>)list : Array.Empty<string>();
        }

        public static string FormatInUseBadgeText(IReadOnlyList<string> schemeLabels)
        {
            if (schemeLabels == null || schemeLabels.Count == 0) return string.Empty;
            return schemeLabels.Count == 1 ? schemeLabels[0] : $"{schemeLabels[0]}+{schemeLabels.Count - 1}";
        }

        public static string FormatItemProtectionWarning(IReadOnlyList<string> schemeLabels, string actionVerb)
        {
            if (schemeLabels == null || schemeLabels.Count == 0) return string.Empty;
            string verb = string.IsNullOrWhiteSpace(actionVerb) ? "操作" : actionVerb;
            return $"该装备正被装备方案【{string.Join("、", schemeLabels)}】引用，{verb}后对应方案将出现缺件，是否继续？";
        }

        /// <summary>前端名称草稿预检（与服务端 UTF-8 1..12 字符规则同口径，最终裁决仍在服务端）。</summary>
        public static bool ValidateNameDraft(string name, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrEmpty(name)) return true;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "方案名称不能全为空格";
                return false;
            }
            int codePoints = 0;
            for (int i = 0; i < name.Length; ++i)
            {
                char ch = name[i];
                if (ch < 0x20 || ch == 0x7F || ch == '<' || ch == '>')
                {
                    error = "方案名称不能包含控制字符或尖括号";
                    return false;
                }
                if (char.IsHighSurrogate(ch))
                {
                    if (i + 1 >= name.Length || !char.IsLowSurrogate(name[i + 1]))
                    {
                        error = "方案名称包含无效字符";
                        return false;
                    }
                    ++i;
                }
                else if (char.IsLowSurrogate(ch))
                {
                    error = "方案名称包含无效字符";
                    return false;
                }
                ++codePoints;
                if (codePoints > MaxNameCodePoints)
                {
                    error = $"方案名称最多 {MaxNameCodePoints} 个字";
                    return false;
                }
            }
            return true;
        }
    }
}
