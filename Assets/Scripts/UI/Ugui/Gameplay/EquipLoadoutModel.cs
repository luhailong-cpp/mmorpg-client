using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.UI.Ugui.Skills;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    public enum PreBattleLoadoutClickKind
    {
        None = 0,
        Switch = 1,
        UnlockHint = 2,
        Refresh = 3,
    }

    /// <summary>战前 1..7 流派快捷条单槽不可变只读视图（纯 C#，零 UnityEngine 依赖）。</summary>
    public sealed class PreBattleLoadoutSlotView
    {
        public uint SchemeId { get; }
        public bool Unlocked { get; }
        public bool Active { get; }
        public bool CanClick { get; }
        public uint MissingCount { get; }
        public string Label { get; }
        public string SpriteKey { get; }
        public PreBattleLoadoutClickKind ClickKind { get; }

        public PreBattleLoadoutSlotView(
            uint schemeId,
            bool unlocked,
            bool active,
            bool canClick,
            uint missingCount,
            string label,
            string spriteKey,
            PreBattleLoadoutClickKind clickKind)
        {
            SchemeId = schemeId;
            Unlocked = unlocked;
            Active = active;
            CanClick = canClick;
            MissingCount = missingCount;
            Label = label ?? string.Empty;
            SpriteKey = spriteKey ?? string.Empty;
            ClickKind = clickKind;
        }
    }

    /// <summary>战前准备期 1..7 一键换流派快捷条不可变快照（供全部进房前/局间倒计时界面复用）。</summary>
    public sealed class PreBattleLoadoutBarState
    {
        public bool Synced { get; }
        public bool Loading { get; }
        public bool Switching { get; }
        public bool BlockedByPendingUnlock { get; }
        public bool BlockedByBattleState { get; }
        public uint ActiveSchemeId { get; }
        public uint UnlockedSchemeCount { get; }
        public string HeaderText { get; }
        public string StatusText { get; }
        public bool StatusIsError { get; }
        public IReadOnlyList<PreBattleLoadoutSlotView> Slots { get; }

        public PreBattleLoadoutBarState(
            bool synced,
            bool loading,
            bool switching,
            bool blockedByPendingUnlock,
            bool blockedByBattleState,
            uint activeSchemeId,
            uint unlockedSchemeCount,
            string headerText,
            string statusText,
            bool statusIsError,
            IReadOnlyList<PreBattleLoadoutSlotView> slots)
        {
            Synced = synced;
            Loading = loading;
            Switching = switching;
            BlockedByPendingUnlock = blockedByPendingUnlock;
            BlockedByBattleState = blockedByBattleState;
            ActiveSchemeId = activeSchemeId;
            UnlockedSchemeCount = unlockedSchemeCount;
            HeaderText = headerText ?? string.Empty;
            StatusText = statusText ?? string.Empty;
            StatusIsError = statusIsError;
            Slots = slots ?? Array.Empty<PreBattleLoadoutSlotView>();
        }
    }

    /// <summary>
    /// 装备流派方案预设的纯 C# 展示与防误卖模型（方案 A · 7 套三合一共享流派席位，不引用 UnityEngine）。
    /// 所有方案、席位权限、租期与缺失态均取自服务端 <see cref="EquipLoadoutSnapshot"/>。
    /// </summary>
    public static class EquipLoadoutModel
    {
        public const uint DefaultUnlockItemConfigId = 3413u;
        public const uint BaseFreeSchemes = SkillLoadoutRentPlanner.BaseFreeSchemes;
        public const uint MaxSchemes = SkillLoadoutRentPlanner.MaxSchemes;
        public const int MaxNameCodePoints = 12;
        public const string ResourceRoot = "UI/Ugui/EquipLoadoutV1/";

        public const string PanelMainKey = ResourceRoot + "panel_equip_loadout_main";
        public const string PanelUnlockKey = ResourceRoot + "panel_equip_loadout_unlock";
        public const string ExpandTalismanIconKey = ResourceRoot + "icon_loadout_expand_talisman";
        public const string TabActiveKey = ResourceRoot + "tab_loadout_active";
        public const string TabUnlockedKey = ResourceRoot + "tab_loadout_unlocked";
        public const string TabRentedKey = SkillLoadoutRentPlanner.ArtBasePath + SkillLoadoutRentPlanner.ArtTabRented;
        public const string TabLockedKey = ResourceRoot + "tab_loadout_locked";
        public const string BadgeInUseKey = ResourceRoot + "badge_loadout_in_use";
        public const string BadgeSlotMissingKey = ResourceRoot + "badge_loadout_slot_missing";

        private static readonly string[] SchemeOrdinals = { "一", "二", "三", "四", "五", "六", "七" };
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

        public static uint EffectiveFreeSchemes(EquipLoadoutSnapshot snapshot)
            => snapshot != null && snapshot.FreeSchemes >= BaseFreeSchemes ? snapshot.FreeSchemes : BaseFreeSchemes;

        public static uint EffectiveMaxSchemes(EquipLoadoutSnapshot snapshot)
            => snapshot != null && snapshot.MaxSchemeCount >= BaseFreeSchemes
                ? Math.Min(snapshot.MaxSchemeCount, MaxSchemes)
                : MaxSchemes;

        public static uint EffectiveUnlockedSchemes(EquipLoadoutSnapshot snapshot)
        {
            uint max = EffectiveMaxSchemes(snapshot);
            if (snapshot == null) return BaseFreeSchemes;
            return Math.Min(max, Math.Max(BaseFreeSchemes, snapshot.UnlockedSchemeCount));
        }

        public static bool HasSavedBindings(EquipLoadoutSchemeInfo scheme)
        {
            if (scheme == null) return false;
            for (int i = 0; i < scheme.Slots.Count; ++i)
            {
                if (scheme.Slots[i] != null && scheme.Slots[i].ItemGuid != 0UL) return true;
            }
            return false;
        }

        public static string SchemeSeatTag(EquipLoadoutSnapshot snapshot, uint schemeId, bool unlocked)
        {
            uint free = EffectiveFreeSchemes(snapshot);
            if (schemeId <= free) return "免";
            if (!unlocked) return "锁";
            uint cardBonus = snapshot?.LightCardBonusSchemes ?? 0u;
            if (schemeId <= free + cardBonus) return "卡";
            return "租";
        }

        /// <summary>将装备方案快照投影为 <see cref="SkillLoadoutView"/>，直接复用 <see cref="SkillLoadoutRentPlanner"/> 统一试算。</summary>
        public static SkillLoadoutView ToUnifiedRentView(EquipLoadoutSnapshot snapshot)
        {
            uint free = EffectiveFreeSchemes(snapshot);
            uint max = EffectiveMaxSchemes(snapshot);
            uint unlocked = EffectiveUnlockedSchemes(snapshot);
            return new SkillLoadoutView
            {
                ActiveSchemeId = snapshot != null && snapshot.ActiveSchemeId >= 1u && snapshot.ActiveSchemeId <= max
                    ? snapshot.ActiveSchemeId : 1u,
                StateVersion = snapshot?.StateVersion ?? 1UL,
                UnlockedSchemes = unlocked,
                MaxSchemes = max,
                FreeSchemes = free,
                LightCardBonusSchemes = snapshot?.LightCardBonusSchemes ?? 0u,
                RentedExtraSlots = snapshot?.RentedExtraSlots ?? 0u,
                RentExpiresAtSec = snapshot?.RentExpiresAtSec ?? 0UL,
                ServerNowSec = snapshot?.ServerNowSec ?? 0UL,
                RentUnitPriceDiamond = snapshot != null && snapshot.RentUnitPriceDiamond > 0UL
                    ? snapshot.RentUnitPriceDiamond : SkillLoadoutRentPlanner.RentUnitPriceDiamond,
                RentPeriodDays = snapshot != null && snapshot.RentPeriodDays > 0u
                    ? snapshot.RentPeriodDays : SkillLoadoutRentPlanner.RentPeriodDays,
                RentMaxRemainingDays = snapshot != null && snapshot.RentMaxRemainingDays > 0u
                    ? snapshot.RentMaxRemainingDays : SkillLoadoutRentPlanner.RentMaxRemainingDays,
                DiamondBalance = snapshot?.DiamondBalance ?? 0UL,
            };
        }

        public static string FormatTabLabel(EquipLoadoutSchemeInfo scheme, bool isActive)
        {
            if (scheme == null) return DefaultSchemeName(1u);
            if (!scheme.Unlocked)
                return HasSavedBindings(scheme) ? SchemeDisplayName(scheme) + "·锁" : "+ " + DefaultSchemeName(scheme.SchemeId);
            string name = SchemeDisplayName(scheme);
            string suffix = scheme.RoleTag > 0u && scheme.RoleTag < (uint)RoleTagNames.Length
                ? " · " + RoleTagNames[scheme.RoleTag]
                : string.Empty;
            return name + suffix;
        }

        public static string FormatCompactTabLabel(EquipLoadoutSnapshot snapshot, EquipLoadoutSchemeInfo scheme, bool isActive)
        {
            if (scheme == null) return DefaultSchemeName(1u);
            string tag = SchemeSeatTag(snapshot, scheme.SchemeId, scheme.Unlocked);
            string name = SchemeDisplayName(scheme);
            string shortName = name.Length > 4 ? name.Substring(0, 4) : name;
            string prefix = isActive ? "★" : string.Empty;
            return $"{prefix}{shortName}·{tag}";
        }

        public static string FormatQuickBarLabel(EquipLoadoutSchemeInfo scheme)
        {
            if (scheme == null) return DefaultSchemeName(1u);
            if (!scheme.Unlocked) return "+ " + DefaultSchemeName(scheme.SchemeId);
            string name = SchemeDisplayName(scheme);
            return name.Length > 4 ? name.Substring(0, 4) : name;
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
        /// 统计当前被任意已保存方案（含到期暂时锁定但仍保留配置的方案）引用的装备实例 item_guid -> 所属方案短标签列表
        /// （供背包角标与交易/解封防误卖保护使用，杜绝到期后手滑卖掉副套装备）。
        /// </summary>
        public static Dictionary<ulong, List<string>> BuildItemReferenceMap(EquipLoadoutSnapshot snapshot)
        {
            var map = new Dictionary<ulong, List<string>>();
            if (snapshot == null) return map;
            foreach (var scheme in snapshot.Schemes.OrderBy(s => s.SchemeId))
            {
                if (scheme == null) continue;
                string label = scheme.Unlocked
                    ? DefaultSchemeName(scheme.SchemeId)
                    : DefaultSchemeName(scheme.SchemeId) + "(锁)";
                foreach (var slot in scheme.Slots)
                {
                    if (slot == null || slot.ItemGuid == 0UL) continue;
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

        /// <summary>方案 A 边界：仅进房前空闲与排队等待期（scene 未挂 InBattleComp）允许经 SwitchEquipLoadout 切流派。</summary>
        public static bool IsPreBattleSwitchAllowed(BattlePhase phase)
            => phase == BattlePhase.None || phase == BattlePhase.Queued;

        /// <summary>打开备战界面时按需补拉装备流派快照（若已缓存或正在拉取则无副作用，纯 C# 无 UnityEngine 依赖）。</summary>
        public static void EnsureEquipLoadoutsRequested(PlayerFeaturesClient features)
        {
            if (features != null && features.EquipLoadouts == null && !features.EquipLoadoutsLoading)
            {
                features.RequestEquipLoadouts();
            }
        }

        /// <summary>从 <see cref="PlayerFeaturesClient"/> 与当前战斗相位推导不可变战前快捷条状态（纯 C# 便于无头单测）。</summary>
        public static PreBattleLoadoutBarState BuildPreBattleBarState(PlayerFeaturesClient features, BattlePhase battlePhase)
        {
            if (features == null)
            {
                return BuildPreBattleBarState(
                    snapshot: null,
                    loading: true,
                    switching: false,
                    hasPendingUnlock: false,
                    battlePhase: battlePhase,
                    error: string.Empty);
            }
            bool busy = features.BusyEquipLoadout || features.BusyEquip || features.BusyEquipLoadoutUnlock;
            return BuildPreBattleBarState(
                features.EquipLoadouts,
                features.EquipLoadoutsLoading,
                busy,
                features.HasPendingEquipLoadoutUnlock,
                battlePhase,
                features.EquipLoadoutError);
        }

        /// <summary>统一组装切换装备流派方案后的提示文案（装备主窗与全部战前备战入口单一真相点）。</summary>
        public static string BuildSwitchToast(SwitchEquipLoadoutResponse response)
        {
            if (response == null) return "装备方案已切换";
            var snap = response.Snapshot;
            uint activeId = snap != null && snap.ActiveSchemeId > 0u ? snap.ActiveSchemeId : 1u;
            var scheme = snap?.Schemes.FirstOrDefault(s => s != null && s.SchemeId == activeId);
            var sb = new StringBuilder();
            sb.Append(scheme != null ? $"已切换至【{SchemeDisplayName(scheme)}】" : "装备方案已切换");
            string missing = FormatMissingSlotsNotice(response.MissingSlots, scheme);
            if (!string.IsNullOrEmpty(missing)) sb.Append("；").Append(missing);
            if (response.LinkedNotices != null && response.LinkedNotices.Count > 0)
                sb.Append("；").Append(string.Join("；", response.LinkedNotices));
            return sb.ToString();
        }

        /// <summary>战前准备或倒计时期间点击未解锁/已到期锁定方案位时的轻提示（不打断当前倒计时窗口）。</summary>
        public static string FormatLockedPreBattleHint(uint schemeId, EquipLoadoutSnapshot snapshot)
        {
            string slotName = DefaultSchemeName(schemeId);
            ulong unitPrice = snapshot != null && snapshot.RentUnitPriceDiamond > 0UL
                ? snapshot.RentUnitPriceDiamond
                : SkillLoadoutRentPlanner.RentUnitPriceDiamond;
            uint periodDays = snapshot != null && snapshot.RentPeriodDays > 0u
                ? snapshot.RentPeriodDays
                : SkillLoadoutRentPlanner.RentPeriodDays;
            uint free = EffectiveFreeSchemes(snapshot);
            string freeTip = free < 3u ? "达 85 级自动永久免费 +1 套、" : string.Empty;
            return $"【{slotName}】未解锁或已到期锁定（已存配置永久保留）：{freeTip}开仙途轻享卡 +2 套，或前往「装备 → 流派方案」按套租用（{unitPrice} 灵石/套/{periodDays}天，装备+战法+加点三合一共享）";
        }

        /// <summary>推导战前准备期 1..7 一键换流派快捷条的完整不可变视图状态。</summary>
        public static PreBattleLoadoutBarState BuildPreBattleBarState(
            EquipLoadoutSnapshot snapshot,
            bool loading,
            bool switching,
            bool hasPendingUnlock,
            BattlePhase battlePhase,
            string error)
        {
            bool battleLocked = !IsPreBattleSwitchAllowed(battlePhase);
            bool synced = snapshot != null;
            uint maxSlots = EffectiveMaxSchemes(snapshot);
            uint unlockedCount = synced ? EffectiveUnlockedSchemes(snapshot) : 0u;
            uint freeSchemes = EffectiveFreeSchemes(snapshot);
            uint activeId = synced
                ? (snapshot.ActiveSchemeId >= 1u && snapshot.ActiveSchemeId <= maxSlots ? snapshot.ActiveSchemeId : 1u)
                : 0u;

            EquipLoadoutSchemeInfo activeScheme = null;
            var slots = new PreBattleLoadoutSlotView[maxSlots];
            for (int i = 0; i < (int)maxSlots; ++i)
            {
                uint schemeId = (uint)(i + 1);
                var scheme = snapshot?.Schemes.FirstOrDefault(s => s != null && s.SchemeId == schemeId);
                bool unlocked = synced && (scheme != null ? scheme.Unlocked : schemeId <= unlockedCount);
                bool active = synced && schemeId == activeId;
                if (active) activeScheme = scheme;

                uint missing = unlocked && scheme != null ? scheme.MissingCount : 0u;
                bool isRentedOrCard = unlocked && schemeId > freeSchemes;
                string spriteKey = !unlocked
                    ? TabLockedKey
                    : (active ? TabActiveKey : (isRentedOrCard ? TabRentedKey : TabUnlockedKey));

                string label;
                if (!synced)
                {
                    label = DefaultSchemeName(schemeId);
                }
                else if (!unlocked)
                {
                    label = active ? SchemeDisplayName(scheme) + "·锁" : "+ " + DefaultSchemeName(schemeId);
                }
                else
                {
                    string shortName = FormatQuickBarLabel(scheme ?? new EquipLoadoutSchemeInfo { SchemeId = schemeId, Unlocked = true });
                    label = missing > 0u ? $"{shortName}·缺{missing}" : shortName;
                }

                PreBattleLoadoutClickKind clickKind;
                bool canClick;
                if (!synced)
                {
                    clickKind = loading ? PreBattleLoadoutClickKind.None : PreBattleLoadoutClickKind.Refresh;
                    canClick = !loading && !battleLocked;
                }
                else if (!unlocked)
                {
                    clickKind = PreBattleLoadoutClickKind.UnlockHint;
                    canClick = !battleLocked;
                }
                else if (active)
                {
                    clickKind = PreBattleLoadoutClickKind.None;
                    canClick = false;
                }
                else
                {
                    clickKind = PreBattleLoadoutClickKind.Switch;
                    canClick = !switching && !hasPendingUnlock && !battleLocked;
                }

                slots[i] = new PreBattleLoadoutSlotView(
                    schemeId, unlocked, active, canClick, missing, label, spriteKey, clickKind);
            }

            string headerText = synced
                ? $"战前流派 · 当前：{FormatTabLabel(activeScheme ?? new EquipLoadoutSchemeInfo { SchemeId = activeId, Unlocked = activeId <= unlockedCount }, true)}"
                : (loading ? "战前流派 · 同步中…" : "战前流派 · 未同步");

            string statusText = string.Empty;
            bool statusIsError = false;
            if (!string.IsNullOrWhiteSpace(error))
            {
                statusText = error.Trim();
                statusIsError = true;
            }
            else if (battleLocked)
            {
                statusText = "即将进入战局，已锁定换装";
            }
            else if (hasPendingUnlock)
            {
                statusText = "方案席位确认中，暂不可切换";
                statusIsError = true;
            }
            else if (switching)
            {
                statusText = "正在切换整套流派（同步战法/加点）…";
            }
            else if (!synced)
            {
                statusText = loading ? "正在同步装备流派方案…" : "点击右侧刷新流派方案";
            }
            else if (activeScheme != null && !activeScheme.Unlocked)
            {
                statusText = "当前方案已到期锁定（只锁不删），可切回已解锁方案或续期恢复";
                statusIsError = true;
            }
            else if (activeScheme != null && activeScheme.MissingCount > 0u)
            {
                statusText = $"当前方案缺 {activeScheme.MissingCount} 槽装备，切换将跳过缺失槽";
                statusIsError = true;
            }
            else
            {
                statusText = $"点选 1..{maxSlots} 一键切整套流派（装备/战法/加点三合一共享）";
            }

            return new PreBattleLoadoutBarState(
                synced, loading, switching, hasPendingUnlock, battleLocked,
                activeId, unlockedCount, headerText, statusText, statusIsError, slots);
        }

        /// <summary>计算快捷条视图签名，供弹窗（如群仙问道赛）仅在流派状态真实变化时重建，避免打断输入焦点。</summary>
        public static int ComputePreBattleBarSignature(PreBattleLoadoutBarState state)
        {
            if (state == null) return 0;
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (state.Synced ? 1 : 0);
                hash = hash * 31 + (state.Loading ? 1 : 0);
                hash = hash * 31 + (state.Switching ? 1 : 0);
                hash = hash * 31 + (state.BlockedByPendingUnlock ? 1 : 0);
                hash = hash * 31 + (state.BlockedByBattleState ? 1 : 0);
                hash = hash * 31 + (int)state.ActiveSchemeId;
                hash = hash * 31 + (int)state.UnlockedSchemeCount;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(state.HeaderText ?? string.Empty);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(state.StatusText ?? string.Empty);
                hash = hash * 31 + (state.StatusIsError ? 1 : 0);
                for (int i = 0; i < state.Slots.Count; ++i)
                {
                    var s = state.Slots[i];
                    hash = hash * 31 + (int)s.SchemeId;
                    hash = hash * 31 + (s.Unlocked ? 1 : 0);
                    hash = hash * 31 + (s.Active ? 1 : 0);
                    hash = hash * 31 + (s.CanClick ? 1 : 0);
                    hash = hash * 31 + (int)s.MissingCount;
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(s.Label ?? string.Empty);
                }
                return hash;
            }
        }
    }
}
