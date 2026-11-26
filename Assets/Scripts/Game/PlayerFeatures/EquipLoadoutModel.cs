using System;
using System.Collections.Generic;
using System.Text;

namespace MmorpgClient.Game.PlayerFeatures
{
    /// <summary>
    /// 装备流派方案预设纯 C# 模型（脱离 UnityEngine，供 EditMode 单测与 uGUI 共用）。
    /// 实例与布局严格分离：方案仅记录各正式装备槽位指向的真实 item_guid，不复制装备实体。
    /// </summary>
    public static class EquipLoadoutModel
    {
        public const uint MaxSchemes = 5u;
        public const int MaxSchemeNameChars = 12;
        public const uint ExpandTalismanConfigId = 3413u;

        public static string DefaultSchemeName(uint schemeId) => schemeId switch
        {
            1u => "方案一",
            2u => "方案二",
            3u => "方案三",
            4u => "方案四",
            5u => "方案五",
            _ => $"方案{schemeId}",
        };

        public static string RoleTagName(uint roleTag) => roleTag switch
        {
            1u => "输出",
            2u => "控场",
            3u => "辅助",
            4u => "生存",
            _ => "默认",
        };

        public static string FormatSchemeDisplayName(EquipLoadoutSchemeInfo scheme)
        {
            if (scheme == null) return "方案";
            string baseName = string.IsNullOrWhiteSpace(scheme.Name)
                ? DefaultSchemeName(scheme.SchemeId)
                : scheme.Name.Trim();
            return scheme.RoleTag > 0u ? $"{baseName} · {RoleTagName(scheme.RoleTag)}" : baseName;
        }

        public static string FormatTabLabel(EquipLoadoutSchemeInfo scheme, bool isActive)
        {
            if (scheme == null) return string.Empty;
            if (!scheme.Unlocked) return $"+ {DefaultSchemeName(scheme.SchemeId)}";
            string title = FormatSchemeDisplayName(scheme);
            return isActive ? $"{title} (当前)" : title;
        }

        public static string SanitizeSchemeName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string trimmed = raw.Trim();
            if (trimmed.Length <= MaxSchemeNameChars) return trimmed;
            return trimmed.Substring(0, MaxSchemeNameChars);
        }

        /// <summary>建立 装备永久 guid -> 引用该装备的已解锁方案号列表 索引（供背包/装备/解封防误操作角标复用）。</summary>
        public static Dictionary<ulong, List<uint>> BuildItemReferenceMap(EquipLoadoutSnapshot snapshot)
        {
            var map = new Dictionary<ulong, List<uint>>();
            if (snapshot?.Schemes == null) return map;
            for (int i = 0; i < snapshot.Schemes.Count; ++i)
            {
                var scheme = snapshot.Schemes[i];
                if (scheme == null || !scheme.Unlocked || scheme.Slots == null) continue;
                for (int j = 0; j < scheme.Slots.Count; ++j)
                {
                    var slot = scheme.Slots[j];
                    if (slot == null || slot.ItemGuid == 0UL) continue;
                    if (!map.TryGetValue(slot.ItemGuid, out var list))
                    {
                        list = new List<uint>(2);
                        map[slot.ItemGuid] = list;
                    }
                    if (!list.Contains(scheme.SchemeId)) list.Add(scheme.SchemeId);
                }
            }
            foreach (var kv in map) kv.Value.Sort();
            return map;
        }

        public static string FormatItemLoadoutBadge(EquipLoadoutSnapshot snapshot, ulong itemGuid)
        {
            if (itemGuid == 0UL || snapshot?.Schemes == null) return string.Empty;
            var schemes = new List<uint>(2);
            for (int i = 0; i < snapshot.Schemes.Count; ++i)
            {
                var scheme = snapshot.Schemes[i];
                if (scheme == null || !scheme.Unlocked || scheme.Slots == null) continue;
                for (int j = 0; j < scheme.Slots.Count; ++j)
                {
                    if (scheme.Slots[j]?.ItemGuid == itemGuid)
                    {
                        if (!schemes.Contains(scheme.SchemeId)) schemes.Add(scheme.SchemeId);
                        break;
                    }
                }
            }
            if (schemes.Count == 0) return string.Empty;
            schemes.Sort();
            return schemes.Count == 1
                ? DefaultSchemeName(schemes[0])
                : "方案" + string.Join("/", schemes);
        }

        public static bool CanUnlockMore(EquipLoadoutSnapshot snapshot)
            => snapshot != null && snapshot.UnlockedSchemeCount < Math.Max(1u, snapshot.MaxSchemeCount);

        public static bool CanAffordNextUnlock(EquipLoadoutSnapshot snapshot, bool useBindDiamondFirst)
        {
            if (!CanUnlockMore(snapshot)) return false;
            if (snapshot.UnlockItemOwned > 0UL) return true;
            ulong cost = snapshot.NextUnlockCostDiamond;
            if (cost == 0UL) return true;
            if (snapshot.NextUnlockAllowBindDiamond && useBindDiamondFirst)
            {
                ulong bind = snapshot.BindDiamondBalance;
                ulong remain = cost > bind ? cost - bind : 0UL;
                return snapshot.DiamondBalance >= remain;
            }
            return snapshot.DiamondBalance >= cost;
        }

        public static string FormatUnlockCostSummary(EquipLoadoutSnapshot snapshot, bool useBindDiamondFirst)
        {
            if (snapshot == null) return "正在同步方案解锁信息…";
            if (!CanUnlockMore(snapshot)) return "已解锁全部装备流派方案位。";
            uint nextSlot = snapshot.UnlockedSchemeCount + 1u;
            if (snapshot.UnlockItemOwned > 0UL)
                return $"优先消耗：装备方案扩容符 ×1（当前持有 {snapshot.UnlockItemOwned}，不扣灵石） → 解锁{DefaultSchemeName(nextSlot)}";

            ulong cost = snapshot.NextUnlockCostDiamond;
            if (snapshot.NextUnlockAllowBindDiamond && useBindDiamondFirst)
            {
                ulong spentBind = Math.Min(snapshot.BindDiamondBalance, cost);
                ulong spentDiamond = cost - spentBind;
                return $"消耗：{cost} 灵石（优先扣绑定灵石 {spentBind} + 非绑灵石 {spentDiamond}；当前绑灵 {snapshot.BindDiamondBalance} / 灵石 {snapshot.DiamondBalance}）";
            }
            return $"消耗：{cost} 非绑灵石（当前持有 {snapshot.DiamondBalance} 灵石；装备方案扩容符持有 0）";
        }

        public static string BuildSwitchResultNotice(SwitchEquipLoadoutResponse response)
        {
            if (response == null) return "装备方案已切换";
            var sb = new StringBuilder();
            uint activeId = response.Snapshot?.ActiveSchemeId ?? 0u;
            var activeScheme = FindScheme(response.Snapshot, activeId);
            sb.Append(activeScheme != null
                ? $"已切换至【{FormatSchemeDisplayName(activeScheme)}】"
                : "装备方案已切换");
            if (response.MissingSlots != null && response.MissingSlots.Count > 0)
                sb.Append($"；有 {response.MissingSlots.Count} 个槽位装备不在背包已保持原状，请重新保存");
            if (response.LinkedNotices != null && response.LinkedNotices.Count > 0)
                sb.Append("；").Append(string.Join("；", response.LinkedNotices));
            return sb.ToString();
        }

        public static EquipLoadoutSchemeInfo FindScheme(EquipLoadoutSnapshot snapshot, uint schemeId)
        {
            if (snapshot?.Schemes == null || schemeId == 0u) return null;
            for (int i = 0; i < snapshot.Schemes.Count; ++i)
            {
                var scheme = snapshot.Schemes[i];
                if (scheme != null && scheme.SchemeId == schemeId) return scheme;
            }
            return null;
        }
    }
}
