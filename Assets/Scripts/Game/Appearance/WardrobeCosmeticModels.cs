using System;
using System.Collections.Generic;
using System.Globalization;

namespace MmorpgClient.Game.Appearance
{
    public enum WardrobeCategoryTab
    {
        Character = 0,
        Pet = 1,
        SkillEffect = 2,
    }

    public enum WardrobeCosmeticRowState
    {
        BaseDefault = 0,
        Equipped = 1,
        Owned = 2,
        LockedPurchasable = 3,
        LockedUnavailable = 4,
    }

    public enum WardrobePrimaryActionKind
    {
        NoneDisabled = 0,
        EquipCosmetic = 1,
        RestoreDefault = 2,
        OpenPermanentWardrobeMall = 3,
    }

    /// <summary>衣柜灵宠页目标灵宠快照（只读纯数据）。</summary>
    public sealed class WardrobePetTargetOption
    {
        public ulong PetId { get; }
        public uint PetTableId { get; }
        public uint ModelId { get; }
        public string Name { get; }
        public string BaseArtKey { get; }
        public uint EquippedCosmeticId { get; }

        public WardrobePetTargetOption(
            ulong petId,
            uint petTableId,
            uint modelId,
            string name,
            string baseArtKey,
            uint equippedCosmeticId)
        {
            PetId = petId;
            PetTableId = petTableId;
            ModelId = modelId;
            Name = name ?? string.Empty;
            BaseArtKey = baseArtKey ?? string.Empty;
            EquippedCosmeticId = equippedCosmeticId;
        }
    }

    /// <summary>衣柜技能特效页目标技能快照（只读纯数据）。</summary>
    public sealed class WardrobeSkillTargetOption
    {
        public uint SkillTableId { get; }
        public string Name { get; }
        public string IconKey { get; }
        public bool Usable { get; }
        public uint EquippedCosmeticId { get; }

        public WardrobeSkillTargetOption(
            uint skillTableId,
            string name,
            string iconKey,
            bool usable,
            uint equippedCosmeticId)
        {
            SkillTableId = skillTableId;
            Name = name ?? string.Empty;
            IconKey = iconKey ?? string.Empty;
            Usable = usable;
            EquippedCosmeticId = equippedCosmeticId;
        }
    }

    /// <summary>衣柜灵宠/特效列表单行只读视图模型。</summary>
    public sealed class WardrobeCosmeticRowView
    {
        public WardrobeCategoryTab Category { get; }
        public uint ConfigId { get; }
        public string Key { get; }
        public string Name { get; }
        public uint TargetPetTableId { get; }
        public uint VisualPetTableId { get; }
        public string VisualArtKey { get; }
        public uint SkillTableId { get; }
        public string FxId { get; }
        public string ProjectileFxId { get; }
        public string TelegraphFxId { get; }
        public string TintHex { get; }
        public WardrobeCosmeticRowState State { get; }
        public string StateLabel { get; }
        public ulong MallSkuId { get; }
        public string AcquireHint { get; }
        public string Description { get; }

        public bool IsBaseDefault => ConfigId == 0u;
        public bool IsOwned => State == WardrobeCosmeticRowState.BaseDefault
            || State == WardrobeCosmeticRowState.Equipped
            || State == WardrobeCosmeticRowState.Owned;

        public WardrobeCosmeticRowView(
            WardrobeCategoryTab category,
            uint configId,
            string key,
            string name,
            uint targetPetTableId,
            uint visualPetTableId,
            string visualArtKey,
            uint skillTableId,
            string fxId,
            string projectileFxId,
            string telegraphFxId,
            string tintHex,
            WardrobeCosmeticRowState state,
            string stateLabel,
            ulong mallSkuId,
            string acquireHint,
            string description)
        {
            Category = category;
            ConfigId = configId;
            Key = key ?? string.Empty;
            Name = name ?? string.Empty;
            TargetPetTableId = targetPetTableId;
            VisualPetTableId = visualPetTableId;
            VisualArtKey = visualArtKey ?? string.Empty;
            SkillTableId = skillTableId;
            FxId = fxId ?? string.Empty;
            ProjectileFxId = projectileFxId ?? string.Empty;
            TelegraphFxId = telegraphFxId ?? string.Empty;
            TintHex = tintHex ?? string.Empty;
            State = state;
            StateLabel = stateLabel ?? string.Empty;
            MallSkuId = mallSkuId;
            AcquireHint = acquireHint ?? string.Empty;
            Description = description ?? string.Empty;
        }
    }

    /// <summary>衣柜灵宠/特效主操作按钮推导结论。</summary>
    public readonly struct WardrobePrimaryAction
    {
        public WardrobePrimaryActionKind Kind { get; }
        public string Label { get; }
        public bool Interactable { get; }
        public ulong MallSkuId { get; }
        public string DetailHint { get; }

        public WardrobePrimaryAction(
            WardrobePrimaryActionKind kind,
            string label,
            bool interactable,
            ulong mallSkuId = 0ul,
            string detailHint = "")
        {
            Kind = kind;
            Label = label ?? string.Empty;
            Interactable = interactable;
            MallSkuId = mallSkuId;
            DetailHint = detailHint ?? string.Empty;
        }
    }

    /// <summary>
    /// 衣柜「人物 / 灵宠 / 特效」三类别的纯 C# 规则与列表构造器（零 UnityEngine 依赖，EditMode 可直接单测）。
    /// 所有拥有与穿戴事实严格来自服务端 <see cref="global::PlayerAppearanceInfo"/>，
    /// 购买入口严格依据服务端在售 SKU 实时判定。
    /// </summary>
    public static class WardrobeCosmeticModels
    {
        public static uint FindEquippedPetCosmeticId(global::PlayerAppearanceInfo info, ulong petId)
        {
            if (info == null || petId == 0ul || info.EquippedPetCosmetics == null) return 0u;
            for (int i = 0; i < info.EquippedPetCosmetics.Count; i++)
            {
                var item = info.EquippedPetCosmetics[i];
                if (item != null && item.PetId == petId) return item.CosmeticId;
            }
            return 0u;
        }

        public static uint FindEquippedSkillVfxId(global::PlayerAppearanceInfo info, uint skillTableId)
        {
            if (info == null || skillTableId == 0u || info.EquippedSkillVfx == null) return 0u;
            return info.EquippedSkillVfx.TryGetValue(skillTableId, out uint id) ? id : 0u;
        }

        public static bool IsPetSpeciesCompatible(uint targetPetTableId, uint petTableId)
            => targetPetTableId == 0u || petTableId == 0u || targetPetTableId == petTableId;

        public static bool TryParseTintHex(string hex, out float r, out float g, out float b)
        {
            r = g = b = 1f;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            string s = hex.Trim();
            if (s.Length != 7 || s[0] != '#') return false;
            if (!byte.TryParse(s.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte rb)
                || !byte.TryParse(s.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte gb)
                || !byte.TryParse(s.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte bb))
                return false;
            r = rb / 255f;
            g = gb / 255f;
            b = bb / 255f;
            return true;
        }

        public static List<WardrobeCosmeticRowView> BuildPetRows(
            global::PlayerAppearanceInfo info,
            WardrobePetTargetOption targetPet,
            Func<ulong, bool> isSellableMallSku)
        {
            var rows = new List<WardrobeCosmeticRowView>();
            if (info == null) return rows;

            uint equippedId = targetPet != null
                ? FindEquippedPetCosmeticId(info, targetPet.PetId)
                : 0u;
            if (equippedId == 0u && targetPet != null) equippedId = targetPet.EquippedCosmeticId;

            bool baseActive = targetPet != null && equippedId == 0u;
            rows.Add(new WardrobeCosmeticRowView(
                WardrobeCategoryTab.Pet,
                configId: 0u,
                key: "pet_base",
                name: "原形",
                targetPetTableId: 0u,
                visualPetTableId: targetPet?.PetTableId ?? 0u,
                visualArtKey: targetPet?.BaseArtKey ?? string.Empty,
                skillTableId: 0u,
                fxId: string.Empty,
                projectileFxId: string.Empty,
                telegraphFxId: string.Empty,
                tintHex: string.Empty,
                state: baseActive ? WardrobeCosmeticRowState.Equipped : WardrobeCosmeticRowState.BaseDefault,
                stateLabel: baseActive ? "当前生效" : "基础外观",
                mallSkuId: 0ul,
                acquireHint: string.Empty,
                description: "展示当前灵宠的原本仪态。"));

            if (info.PetCosmetics == null) return rows;
            for (int i = 0; i < info.PetCosmetics.Count; i++)
            {
                var entry = info.PetCosmetics[i];
                if (entry == null) continue;
                if (targetPet != null && !IsPetSpeciesCompatible(entry.TargetPetTableId, targetPet.PetTableId))
                    continue;

                bool isEquipped = targetPet != null && equippedId == entry.ConfigId && entry.Owned;
                bool sellable = !entry.Owned && entry.MallSkuId != 0ul && (isSellableMallSku?.Invoke(entry.MallSkuId) == true);
                WardrobeCosmeticRowState state = isEquipped ? WardrobeCosmeticRowState.Equipped
                    : entry.Owned ? WardrobeCosmeticRowState.Owned
                    : sellable ? WardrobeCosmeticRowState.LockedPurchasable
                    : WardrobeCosmeticRowState.LockedUnavailable;
                string badge = state switch
                {
                    WardrobeCosmeticRowState.Equipped => "当前生效",
                    WardrobeCosmeticRowState.Owned => "已拥有",
                    _ => "未拥有",
                };
                rows.Add(new WardrobeCosmeticRowView(
                    WardrobeCategoryTab.Pet,
                    entry.ConfigId,
                    entry.CosmeticKey,
                    entry.Name,
                    entry.TargetPetTableId,
                    entry.VisualPetTableId,
                    entry.VisualArtKey,
                    skillTableId: 0u,
                    fxId: string.Empty,
                    projectileFxId: string.Empty,
                    telegraphFxId: string.Empty,
                    tintHex: string.Empty,
                    state: state,
                    stateLabel: badge,
                    mallSkuId: entry.MallSkuId,
                    acquireHint: entry.AcquireHint,
                    description: string.IsNullOrWhiteSpace(entry.Desc) ? "幻化仅改变外观。" : entry.Desc));
            }
            return rows;
        }

        public static List<WardrobeCosmeticRowView> BuildSkillEffectRows(
            global::PlayerAppearanceInfo info,
            uint filterSkillTableId,
            Func<ulong, bool> isSellableMallSku)
        {
            var rows = new List<WardrobeCosmeticRowView>();
            if (info == null) return rows;

            uint activeFilterSkillId = filterSkillTableId;
            if (activeFilterSkillId == 0u && info.SkillVfxCosmetics != null && info.SkillVfxCosmetics.Count > 0)
                activeFilterSkillId = info.SkillVfxCosmetics[0].SkillTableId;

            uint equippedForSkill = activeFilterSkillId != 0u ? FindEquippedSkillVfxId(info, activeFilterSkillId) : 0u;
            bool baseActive = activeFilterSkillId != 0u && equippedForSkill == 0u;

            rows.Add(new WardrobeCosmeticRowView(
                WardrobeCategoryTab.SkillEffect,
                configId: 0u,
                key: "skill_default",
                name: "默认表现",
                targetPetTableId: 0u,
                visualPetTableId: 0u,
                visualArtKey: string.Empty,
                skillTableId: activeFilterSkillId,
                fxId: string.Empty,
                projectileFxId: string.Empty,
                telegraphFxId: string.Empty,
                tintHex: string.Empty,
                state: baseActive ? WardrobeCosmeticRowState.Equipped : WardrobeCosmeticRowState.BaseDefault,
                stateLabel: baseActive ? "当前生效" : "基础特效",
                mallSkuId: 0ul,
                acquireHint: string.Empty,
                description: "展示该技能的默认战斗特效。"));

            if (info.SkillVfxCosmetics == null) return rows;
            for (int i = 0; i < info.SkillVfxCosmetics.Count; i++)
            {
                var entry = info.SkillVfxCosmetics[i];
                if (entry == null) continue;
                if (filterSkillTableId != 0u && entry.SkillTableId != filterSkillTableId) continue;

                uint skillEquipped = FindEquippedSkillVfxId(info, entry.SkillTableId);
                bool isEquipped = entry.Owned && skillEquipped == entry.ConfigId;
                bool sellable = !entry.Owned && entry.MallSkuId != 0ul && (isSellableMallSku?.Invoke(entry.MallSkuId) == true);
                WardrobeCosmeticRowState state = isEquipped ? WardrobeCosmeticRowState.Equipped
                    : entry.Owned ? WardrobeCosmeticRowState.Owned
                    : sellable ? WardrobeCosmeticRowState.LockedPurchasable
                    : WardrobeCosmeticRowState.LockedUnavailable;
                string badge = state switch
                {
                    WardrobeCosmeticRowState.Equipped => "当前生效",
                    WardrobeCosmeticRowState.Owned => "已拥有",
                    _ => "未拥有",
                };
                rows.Add(new WardrobeCosmeticRowView(
                    WardrobeCategoryTab.SkillEffect,
                    entry.ConfigId,
                    entry.VfxKey,
                    entry.Name,
                    targetPetTableId: 0u,
                    visualPetTableId: 0u,
                    visualArtKey: string.Empty,
                    skillTableId: entry.SkillTableId,
                    fxId: entry.FxId,
                    projectileFxId: entry.ProjectileFxId,
                    telegraphFxId: entry.TelegraphFxId,
                    tintHex: entry.TintHex,
                    state: state,
                    stateLabel: badge,
                    mallSkuId: entry.MallSkuId,
                    acquireHint: entry.AcquireHint,
                    description: string.IsNullOrWhiteSpace(entry.Desc) ? "特效仅改变战斗画面。" : entry.Desc));
            }
            return rows;
        }

        public static WardrobePrimaryAction ResolvePetPrimaryAction(
            WardrobeCosmeticRowView selected,
            WardrobePetTargetOption targetPet,
            bool canSubmit,
            bool busy,
            bool previewOnly)
        {
            if (selected == null)
                return new WardrobePrimaryAction(WardrobePrimaryActionKind.NoneDisabled, "暂无可用外观", false);
            if (busy)
                return new WardrobePrimaryAction(WardrobePrimaryActionKind.NoneDisabled, "提交中…", false);
            if (previewOnly)
                return new WardrobePrimaryAction(WardrobePrimaryActionKind.NoneDisabled, "首测预览", false, detailHint: "当前为预览模式。");

            if (selected.State == WardrobeCosmeticRowState.LockedPurchasable)
            {
                return new WardrobePrimaryAction(
                    WardrobePrimaryActionKind.OpenPermanentWardrobeMall,
                    "前往永久衣柜",
                    canSubmit && selected.MallSkuId != 0ul,
                    selected.MallSkuId,
                    "前往官方商城「永久衣柜」购买后即可穿戴。");
            }
            if (selected.State == WardrobeCosmeticRowState.LockedUnavailable)
            {
                string hint = string.IsNullOrWhiteSpace(selected.AcquireHint) ? "暂未上架" : selected.AcquireHint;
                return new WardrobePrimaryAction(WardrobePrimaryActionKind.NoneDisabled, "暂未上架", false, detailHint: hint);
            }

            if (targetPet == null)
                return new WardrobePrimaryAction(WardrobePrimaryActionKind.NoneDisabled, "暂无灵宠", false, detailHint: "拥有灵宠后即可穿戴幻化。");

            if (selected.IsBaseDefault)
            {
                bool hasMorph = selected.State != WardrobeCosmeticRowState.Equipped;
                return hasMorph
                    ? new WardrobePrimaryAction(WardrobePrimaryActionKind.RestoreDefault, "恢复原形", canSubmit)
                    : new WardrobePrimaryAction(WardrobePrimaryActionKind.NoneDisabled, "当前为原形", false);
            }

            if (selected.State == WardrobeCosmeticRowState.Equipped)
                return new WardrobePrimaryAction(WardrobePrimaryActionKind.RestoreDefault, "恢复原形", canSubmit);

            return new WardrobePrimaryAction(WardrobePrimaryActionKind.EquipCosmetic, "穿戴幻化", canSubmit);
        }

        public static WardrobePrimaryAction ResolveSkillPrimaryAction(
            WardrobeCosmeticRowView selected,
            WardrobeSkillTargetOption targetSkill,
            bool canSubmit,
            bool busy,
            bool previewOnly)
        {
            if (selected == null)
                return new WardrobePrimaryAction(WardrobePrimaryActionKind.NoneDisabled, "暂无可用特效", false);
            if (busy)
                return new WardrobePrimaryAction(WardrobePrimaryActionKind.NoneDisabled, "提交中…", false);
            if (previewOnly)
                return new WardrobePrimaryAction(WardrobePrimaryActionKind.NoneDisabled, "首测预览", false, detailHint: "当前为预览模式。");

            if (selected.State == WardrobeCosmeticRowState.LockedPurchasable)
            {
                return new WardrobePrimaryAction(
                    WardrobePrimaryActionKind.OpenPermanentWardrobeMall,
                    "前往永久衣柜",
                    canSubmit && selected.MallSkuId != 0ul,
                    selected.MallSkuId,
                    "前往官方商城「永久衣柜」购买后即可启用。");
            }
            if (selected.State == WardrobeCosmeticRowState.LockedUnavailable)
            {
                string hint = string.IsNullOrWhiteSpace(selected.AcquireHint) ? "暂未上架" : selected.AcquireHint;
                return new WardrobePrimaryAction(WardrobePrimaryActionKind.NoneDisabled, "暂未上架", false, detailHint: hint);
            }

            if (selected.IsBaseDefault)
            {
                bool hasOverride = selected.State != WardrobeCosmeticRowState.Equipped;
                return hasOverride
                    ? new WardrobePrimaryAction(WardrobePrimaryActionKind.RestoreDefault, "恢复默认", canSubmit)
                    : new WardrobePrimaryAction(WardrobePrimaryActionKind.NoneDisabled, "当前为默认", false);
            }

            if (selected.State == WardrobeCosmeticRowState.Equipped)
                return new WardrobePrimaryAction(WardrobePrimaryActionKind.RestoreDefault, "恢复默认", canSubmit);

            if (targetSkill != null && !targetSkill.Usable)
                return new WardrobePrimaryAction(WardrobePrimaryActionKind.NoneDisabled, "条件不足", false, detailHint: "需先习得对应可用技能后方可启用。");

            return new WardrobePrimaryAction(WardrobePrimaryActionKind.EquipCosmetic, "启用特效", canSubmit);
        }
    }

    /// <summary>
    /// 衣柜三类别交互状态机（零 UnityEngine 依赖）：
    /// 保留当前会话内各类别选中项、滚动位置、灵宠目标与技能筛选；支持上下文跳转、对比原形与断线/换角复位。
    /// </summary>
    public sealed class WardrobeCosmeticTabState
    {
        public WardrobeCategoryTab ActiveCategory { get; private set; } = WardrobeCategoryTab.Character;
        public uint SelectedPetCosmeticId { get; private set; }
        public uint SelectedSkillVfxId { get; private set; }
        public ulong SelectedPetId { get; private set; }
        public uint FilterSkillTableId { get; private set; }
        public bool CompareOriginalPet { get; private set; }
        public bool CompactDetailOpen { get; private set; }
        public float PetScrollNormalized { get; private set; } = 1f;
        public float SkillScrollNormalized { get; private set; } = 1f;

        public void OpenContextual(WardrobeCategoryTab category, ulong targetPetId = 0ul, uint targetSkillTableId = 0u)
        {
            ActiveCategory = category;
            CompareOriginalPet = false;
            CompactDetailOpen = false;
            if (category == WardrobeCategoryTab.Pet && targetPetId != 0ul)
                SelectedPetId = targetPetId;
            if (category == WardrobeCategoryTab.SkillEffect)
                FilterSkillTableId = targetSkillTableId;
        }

        public void SelectCategory(WardrobeCategoryTab category, float currentScrollNormalized)
        {
            SaveScroll(ActiveCategory, currentScrollNormalized);
            if (ActiveCategory == category) return;
            ActiveCategory = category;
            CompareOriginalPet = false;
            CompactDetailOpen = false;
        }

        public void SelectRow(WardrobeCategoryTab category, uint cosmeticId, float currentScrollNormalized, bool openCompactDetail)
        {
            SaveScroll(category, currentScrollNormalized);
            if (category == WardrobeCategoryTab.Pet)
            {
                if (SelectedPetCosmeticId != cosmeticId) CompareOriginalPet = false;
                SelectedPetCosmeticId = cosmeticId;
            }
            else if (category == WardrobeCategoryTab.SkillEffect)
            {
                SelectedSkillVfxId = cosmeticId;
            }
            CompactDetailOpen = openCompactDetail;
        }

        public void ExitCompactDetail()
        {
            CompactDetailOpen = false;
            CompareOriginalPet = false;
        }

        public void ToggleCompareOriginalPet() => CompareOriginalPet = !CompareOriginalPet;

        public void SelectPetTarget(ulong petId)
        {
            if (petId == 0ul || SelectedPetId == petId) return;
            SelectedPetId = petId;
            CompareOriginalPet = false;
        }

        public void CyclePetTarget(IReadOnlyList<WardrobePetTargetOption> pets)
        {
            if (pets == null || pets.Count <= 1) return;
            int idx = -1;
            for (int i = 0; i < pets.Count; i++)
            {
                if (pets[i].PetId == SelectedPetId) { idx = i; break; }
            }
            int next = (idx + 1) % pets.Count;
            SelectedPetId = pets[next].PetId;
            CompareOriginalPet = false;
        }

        public void SetFilterSkillTableId(uint skillTableId)
        {
            FilterSkillTableId = skillTableId;
        }

        public WardrobePetTargetOption SyncPetTarget(IReadOnlyList<WardrobePetTargetOption> pets, ulong activePetId)
        {
            if (pets == null || pets.Count == 0)
            {
                SelectedPetId = 0ul;
                CompareOriginalPet = false;
                return null;
            }
            for (int i = 0; i < pets.Count; i++)
                if (pets[i].PetId == SelectedPetId) return pets[i];
            if (activePetId != 0ul)
            {
                for (int i = 0; i < pets.Count; i++)
                    if (pets[i].PetId == activePetId) { SelectedPetId = activePetId; return pets[i]; }
            }
            SelectedPetId = pets[0].PetId;
            return pets[0];
        }

        public WardrobeCosmeticRowView SyncSelectedRow(WardrobeCategoryTab category, IReadOnlyList<WardrobeCosmeticRowView> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                if (category == WardrobeCategoryTab.Pet) SelectedPetCosmeticId = 0u;
                else if (category == WardrobeCategoryTab.SkillEffect) SelectedSkillVfxId = 0u;
                return null;
            }
            uint current = category == WardrobeCategoryTab.Pet ? SelectedPetCosmeticId : SelectedSkillVfxId;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].ConfigId == current) return rows[i];

            // 优先定位到当前已生效项，其次首个非原形项，最后回落第 0 项。
            WardrobeCosmeticRowView picked = rows[0];
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].State == WardrobeCosmeticRowState.Equipped) { picked = rows[i]; break; }
            }
            if (picked.IsBaseDefault && rows.Count > 1) picked = rows[1];

            if (category == WardrobeCategoryTab.Pet) SelectedPetCosmeticId = picked.ConfigId;
            else if (category == WardrobeCategoryTab.SkillEffect) SelectedSkillVfxId = picked.ConfigId;
            return picked;
        }

        public float SavedScroll(WardrobeCategoryTab category) => category switch
        {
            WardrobeCategoryTab.Pet => PetScrollNormalized,
            WardrobeCategoryTab.SkillEffect => SkillScrollNormalized,
            _ => 1f,
        };

        public void SaveScroll(WardrobeCategoryTab category, float normalized)
        {
            float clamped = normalized < 0f ? 0f : (normalized > 1f ? 1f : normalized);
            if (category == WardrobeCategoryTab.Pet) PetScrollNormalized = clamped;
            else if (category == WardrobeCategoryTab.SkillEffect) SkillScrollNormalized = clamped;
        }

        public void ResetForClose()
        {
            CompareOriginalPet = false;
            CompactDetailOpen = false;
        }

        public void ResetForRoleChangeOrDisconnect()
        {
            ActiveCategory = WardrobeCategoryTab.Character;
            SelectedPetCosmeticId = 0u;
            SelectedSkillVfxId = 0u;
            SelectedPetId = 0ul;
            FilterSkillTableId = 0u;
            CompareOriginalPet = false;
            CompactDetailOpen = false;
            PetScrollNormalized = 1f;
            SkillScrollNormalized = 1f;
        }
    }
}
