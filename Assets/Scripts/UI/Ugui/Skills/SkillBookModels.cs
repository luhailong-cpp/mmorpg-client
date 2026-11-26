using System;
using System.Collections.Generic;

namespace MmorpgClient.UI.Ugui.Skills
{
    /// <summary>技能簿身份；宠物按唯一实例 ID 区分，显示名称不参与匹配。</summary>
    [Serializable]
    public sealed class SkillBookContext
    {
        public bool IsPet;
        public ulong PetId;
        public string DisplayName;
        public static SkillBookContext Character() => new() { DisplayName = "角色技能" };
        public static SkillBookContext Pet(ulong id, string name) => new()
            { IsPet = true, PetId = id, DisplayName = name ?? "宠物" };
        public bool Matches(SkillBookContext other) => other != null && IsPet == other.IsPet &&
            (!IsPet || PetId == other.PetId);
    }

    /// <summary>由业务适配器提供权威快照；Changed 必须在 Unity 主线程触发。</summary>
    public interface ISkillBookSource
    {
        event Action Changed;
        SkillBookViewState State { get; }
        void Refresh(SkillBookContext context);
        void Upgrade(SkillBookContext context, uint skillId, uint expectedRank, uint count);
    }

    /// <summary>一体化保存「战法·加点」流派方案的提交参数。</summary>
    public sealed class SkillLoadoutSaveArgs
    {
        public uint SchemeId;
        public string Name;
        public uint RoleTag;
        public IReadOnlyList<uint> PassiveSkillIds;
        public IReadOnlyList<uint> PursuitSkillIds;
        public IReadOnlyList<uint> OpeningSkillIds;
        public bool UpdateAttributeAllocation;
        public IReadOnlyDictionary<uint, uint> AttributeAllocated;
        public bool ResetAttributeIfNeeded;
    }

    /// <summary>人物「战法·加点」一体化流派方案（7 套非永久槽位：1 开场 + 3 被动 + 1 追击 + 4 维加点）写接口。</summary>
    public interface ISkillLoadoutSource
    {
        void SaveLoadout(uint schemeId, uint roleTag, IReadOnlyList<uint> passiveSkillIds,
            IReadOnlyList<uint> pursuitSkillIds, IReadOnlyList<uint> openingSkillIds,
            Action<bool, string> completed = null);
        void SaveLoadout(SkillLoadoutSaveArgs args, Action<bool, string> completed = null);
        void SwitchLoadout(uint schemeId, Action<bool, string> completed = null);
        void SwitchLoadout(uint schemeId, bool skipAttributeSync, Action<bool, string> completed = null);
        void RentLoadoutSlots(uint targetRentedSlots, uint addPeriods, ulong expectedCostDiamond,
            Action<bool, string> completed = null);
    }

    public sealed class SkillLoadoutSchemeView
    {
        public uint SchemeId;
        public string Name;
        public uint RoleTag;
        public readonly List<uint> PassiveSkillIds = new();
        public readonly List<uint> PursuitSkillIds = new();
        public readonly List<uint> OpeningSkillIds = new();
        public readonly Dictionary<uint, uint> AttributeAllocated = new();
        public uint AttributeRemaining;
        public uint AttributeTotalPoints;

        public uint GetAllocated(uint dimensionId) =>
            AttributeAllocated.TryGetValue(dimensionId, out uint value) ? value : 0u;
    }

    public sealed class SkillLoadoutView
    {
        public bool CustomEnabled;
        public uint ActiveSchemeId;
        public ulong StateVersion;
        public uint MaxPassiveSlots = 3;
        public uint MaxPursuitSlots = 1;
        public uint MaxOpeningSlots = 1;
        public uint UnlockedSchemes = 2;
        public uint MaxSchemes = 7;
        public uint FreeSchemes = 2;
        public uint LightCardBonusSchemes;
        public uint RentedExtraSlots;
        public ulong RentExpiresAtSec;
        public ulong ServerNowSec;
        public ulong RentUnitPriceDiamond = 30;
        public uint RentPeriodDays = 30;
        public uint RentMaxRemainingDays = 180;
        public ulong DiamondBalance;
        public ulong AttributeResetCostGold;
        public ulong AttributeSwitchCooldownUntil;
        public uint ActiveAttributeSchemeId = 1;
        public string LastRequestId;
        public readonly List<SkillLoadoutSchemeView> Schemes = new();

        public bool LightCardActive => LightCardBonusSchemes > 0u;
        public bool IsRentActive => RentedExtraSlots > 0u && RentExpiresAtSec > ServerNowSec;
        public ulong RemainingRentSeconds => IsRentActive ? RentExpiresAtSec - ServerNowSec : 0UL;
        public uint MaxRentableExtraSlots => SkillLoadoutRentPlanner.MaxRentableExtraSlots(
            FreeSchemes >= SkillLoadoutRentPlanner.BaseFreeSchemes ? FreeSchemes : SkillLoadoutRentPlanner.BaseFreeSchemes,
            LightCardActive);

        public SkillLoadoutSchemeView FindScheme(uint schemeId) =>
            Schemes.Find(s => s != null && s.SchemeId == schemeId);

        public static string DefaultSchemeName(uint schemeId) => schemeId switch
        {
            1 => "方案一",
            2 => "方案二",
            3 => "方案三",
            4 => "方案四",
            5 => "方案五",
            6 => "方案六",
            7 => "方案七",
            _ => "方案" + schemeId,
        };

        public static string DescribeRoleTag(uint roleTag) => roleTag switch
        {
            1 => "输出",
            2 => "控场",
            3 => "辅助",
            4 => "生存",
            _ => "默认",
        };
    }

    /// <summary>经典回合制四维加点一键流派模板（仅分配当前可用总点数，提交仍走服务端权威校验）。</summary>
    public readonly struct SkillBuildTemplate
    {
        public readonly string Label;
        public readonly string Summary;
        public readonly uint RecommendedRoleTag;
        public readonly uint WeightCon; // 101 体质
        public readonly uint WeightMag; // 102 灵力
        public readonly uint WeightStr; // 103 力量
        public readonly uint WeightAgi; // 104 敏捷

        public SkillBuildTemplate(string label, string summary, uint roleTag, uint con, uint mag, uint str, uint agi)
        {
            Label = label;
            Summary = summary;
            RecommendedRoleTag = roleTag;
            WeightCon = con;
            WeightMag = mag;
            WeightStr = str;
            WeightAgi = agi;
        }
    }

    public static class SkillBuildTemplates
    {
        public const uint DimCon = 101;
        public const uint DimMag = 102;
        public const uint DimStr = 103;
        public const uint DimAgi = 104;

        public static readonly uint[] AllDimensions = { DimCon, DimMag, DimStr, DimAgi };

        public static readonly SkillBuildTemplate[] All =
        {
            new("4力1体", "物理输出兼顾生存", 1u, 1, 0, 4, 0),
            new("4灵1体", "法系清场兼顾血线", 1u, 1, 4, 0, 0),
            new("3敏1体1力", "高速先手控场补刀", 2u, 1, 0, 1, 3),
            new("2灵2体1敏", "辅助治疗稳定出手", 3u, 2, 2, 0, 1),
            new("全敏先手", "极限抢速竞技压制", 2u, 0, 0, 0, 5),
            new("全体抗伤", "高难首领极致抗压", 4u, 5, 0, 0, 0),
        };

        public static string DimensionName(uint dimensionId) => dimensionId switch
        {
            DimCon => "体质",
            DimMag => "灵力",
            DimStr => "力量",
            DimAgi => "敏捷",
            _ => "属性#" + dimensionId,
        };
    }

    /// <summary>7 套流派方案槽位租用纯前端试算与合规校验（与服务端 skill_loadout_rules 逐项对齐；最终扣费由服务端裁决）。</summary>
    public static class SkillLoadoutRentPlanner
    {
        public const uint BaseFreeSchemes = 2;
        public const uint MaxLevelFreeSchemeUnlockLevel = 85;
        public const uint MaxLevelBonusFreeSchemes = 1;
        public const uint LightCardBonusSchemes = 2;
        public const uint MaxRentedExtraSlots = 5;
        public const uint MaxSchemes = 7;
        public const ulong RentUnitPriceDiamond = 30;
        public const uint RentPeriodDays = 30;
        public const ulong SecondsPerDay = 86400;
        public const ulong RentPeriodSeconds = RentPeriodDays * SecondsPerDay;
        public const uint RentMaxRemainingDays = 180;
        public const ulong RentMaxRemainingSeconds = RentMaxRemainingDays * SecondsPerDay;
        public const uint RentMaxPeriodsPerCall = 6;
        public const int MaxNameCodePoints = 12;

        public const string ArtBasePath = "UI/Ugui/SkillBuildLoadoutV1/";
        public const string ArtRentModalFrame = "panel_build_rent_modal";
        public const string ArtTabActive = "tab_build_active";
        public const string ArtTabUnlocked = "tab_build_unlocked";
        public const string ArtTabRented = "tab_build_rented";
        public const string ArtTabLocked = "tab_build_locked";
        public const string ArtAttrBox = "card_attr_panel_bg";
        public const string ArtTemplatePill = "pill_template_btn";
        public const string ArtSlotPassIcon = "icon_build_slot_pass";

        private static readonly string[] BlockedNameTerms =
        {
            "官方", "客服", "管理员", "系统消息", "外挂", "代充", "私服", "gm", "admin",
        };

        public readonly struct RentQuote
        {
            public readonly bool Valid;
            public readonly string Error;
            public readonly ulong ProrateCost;
            public readonly ulong RenewCost;
            public readonly ulong TotalCost;
            public readonly ulong NewExpiresAtSec;
            public readonly uint NewUnlockedSchemes;
            public readonly bool ExceedsStackCap;

            public RentQuote(bool valid, string error, ulong prorate, ulong renew, ulong newExpires,
                uint newUnlocked, bool exceedsCap)
            {
                Valid = valid;
                Error = error;
                ProrateCost = prorate;
                RenewCost = renew;
                TotalCost = prorate + renew;
                NewExpiresAtSec = newExpires;
                NewUnlockedSchemes = newUnlocked;
                ExceedsStackCap = exceedsCap;
            }
        }

        public static uint FreeSchemesForLevel(uint effectiveLevel) =>
            BaseFreeSchemes + (effectiveLevel >= MaxLevelFreeSchemeUnlockLevel ? MaxLevelBonusFreeSchemes : 0u);

        public static uint MaxRentableExtraSlots(uint freeSchemes, bool lightCardActive)
        {
            uint free = freeSchemes >= BaseFreeSchemes ? freeSchemes : BaseFreeSchemes;
            uint used = free + (lightCardActive ? LightCardBonusSchemes : 0u);
            return used >= MaxSchemes ? 0u : MaxSchemes - used;
        }

        public static uint MaxRentableExtraSlots(bool lightCardActive) =>
            MaxRentableExtraSlots(BaseFreeSchemes, lightCardActive);

        public static RentQuote Plan(SkillLoadoutView loadout, uint targetRentedSlots, uint addPeriods)
        {
            uint freeSchemes = loadout != null && loadout.FreeSchemes >= BaseFreeSchemes
                ? Math.Min(loadout.FreeSchemes, MaxSchemes)
                : BaseFreeSchemes;
            bool lightCardActive = loadout != null && loadout.LightCardActive;
            uint activeRented = loadout != null && loadout.IsRentActive ? Math.Min(loadout.RentedExtraSlots, MaxRentedExtraSlots) : 0u;
            ulong nowSec = loadout?.ServerNowSec ?? 0UL;
            ulong currentExpires = loadout?.RentExpiresAtSec ?? 0UL;
            bool rentActive = activeRented > 0u && currentExpires > nowSec;
            ulong remainingSec = rentActive ? currentExpires - nowSec : 0UL;
            ulong unitPrice = loadout != null && loadout.RentUnitPriceDiamond > 0 ? loadout.RentUnitPriceDiamond : RentUnitPriceDiamond;
            uint periodDays = loadout != null && loadout.RentPeriodDays > 0 ? loadout.RentPeriodDays : RentPeriodDays;
            uint maxDays = loadout != null && loadout.RentMaxRemainingDays > 0 ? loadout.RentMaxRemainingDays : RentMaxRemainingDays;
            ulong periodSec = periodDays * SecondsPerDay;
            ulong maxRemainSec = maxDays * SecondsPerDay;
            uint maxRentable = MaxRentableExtraSlots(freeSchemes, lightCardActive);

            if (targetRentedSlots < 1u || targetRentedSlots > maxRentable)
                return new RentQuote(false,
                    $"当前已有 {freeSchemes + (lightCardActive ? LightCardBonusSchemes : 0u)} 套免租方案位，最多可额外租用 {maxRentable} 套（总计 7 套封顶）。",
                    0, 0, 0, 0, false);

            uint minAllowed = Math.Min(activeRented, maxRentable);
            if (rentActive && targetRentedSlots < minAllowed)
                return new RentQuote(false, "租期内不可缩减已租套数，可扩容或到期后再按更少套数续租。", 0, 0, 0, 0, false);

            if (addPeriods > RentMaxPeriodsPerCall)
                return new RentQuote(false, $"单次最多叠加 {maxDays} 天租期。", 0, 0, 0, 0, true);

            if (!rentActive && addPeriods == 0u)
                return new RentQuote(false, "请至少选择 30 天租期。", 0, 0, 0, 0, false);

            if (rentActive && addPeriods == 0u && targetRentedSlots <= activeRented)
                return new RentQuote(false, "请选择增加槽位套数，或选择续期天数。", 0, 0, 0, 0, false);

            ulong addSec = addPeriods * periodSec;
            if (addPeriods > 0u && (remainingSec > maxRemainSec || maxRemainSec - remainingSec < addSec))
                return new RentQuote(false, $"续期后剩余时长将超过 {maxDays} 天上限，请选择更短续期。", 0, 0, 0, 0, true);

            uint addedSlots = (rentActive && targetRentedSlots > activeRented) ? (targetRentedSlots - activeRented) : 0u;
            ulong prorate = 0UL;
            if (addedSlots > 0u && remainingSec > 0UL)
            {
                ulong perPeriod = addedSlots * unitPrice;
                ulong fullPeriods = remainingSec / periodSec;
                ulong remSec = remainingSec % periodSec;
                prorate = fullPeriods * perPeriod
                    + (remSec == 0UL ? 0UL : (perPeriod * remSec + periodSec - 1UL) / periodSec);
                if (prorate == 0UL) prorate = 1UL;
            }
            ulong renew = targetRentedSlots * unitPrice * addPeriods;
            ulong newExpires = addPeriods > 0u ? (nowSec + remainingSec + addSec) : currentExpires;
            uint newUnlocked = Math.Min(MaxSchemes, freeSchemes + (lightCardActive ? LightCardBonusSchemes : 0u) + targetRentedSlots);
            return new RentQuote(true, null, prorate, renew, newExpires, newUnlocked, false);
        }

        public static string FormatRemaining(ulong remainingSec)
        {
            if (remainingSec == 0UL) return "未开通 / 已到期";
            ulong days = remainingSec / SecondsPerDay;
            ulong hours = (remainingSec % SecondsPerDay) / 3600UL;
            ulong minutes = (remainingSec % 3600UL) / 60UL;
            if (days > 0UL) return $"剩余 {days} 天 {hours} 小时";
            if (hours > 0UL) return $"剩余 {hours} 小时 {minutes} 分";
            return $"剩余 {Math.Max(1UL, minutes)} 分钟";
        }

        public static bool ValidateDraftName(string name, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(name)) return true; // 留空表示沿用默认名或原名
            int codePoints = 0;
            bool hasVisible = false;
            for (int i = 0; i < name.Length; )
            {
                int cp = char.ConvertToUtf32(name, i);
                i += char.IsSurrogatePair(name, i) ? 2 : 1;
                if (cp < 0x20 || cp == 0x7F || (cp >= 0x80 && cp <= 0x9F) || cp == '<' || cp == '>')
                {
                    reason = "方案名称不可包含控制字符或尖括号。";
                    return false;
                }
                codePoints++;
                if (codePoints > MaxNameCodePoints)
                {
                    reason = $"方案名称最多 {MaxNameCodePoints} 个字符。";
                    return false;
                }
                if (!char.IsWhiteSpace((char)Math.Min(cp, 0xFFFF)) && cp != 0x200B && cp != 0xFEFF)
                    hasVisible = true;
            }
            if (!hasVisible)
            {
                reason = "方案名称不能全为空白。";
                return false;
            }
            string lower = name.ToLowerInvariant();
            foreach (var term in BlockedNameTerms)
            {
                if (lower.Contains(term))
                {
                    reason = "方案名称包含受限词语，请换一个名称。";
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>仅管理未提交的一体化流派草稿（战法装配 + 四维加点 + 名称 + 用途）；最终校验与扣费由服务端裁决。</summary>
    public sealed class SkillLoadoutDraft
    {
        public enum Change { SlotsFull, Added, Removed, Replaced }

        private readonly List<uint> _passive = new();
        private readonly List<uint> _pursuit = new();
        private readonly List<uint> _opening = new();
        private readonly Dictionary<uint, uint> _attributes = new();
        private readonly Dictionary<uint, uint> _storedAttributes = new();

        public uint SchemeId { get; private set; } = 1;
        public ulong StateVersion { get; private set; }
        public string Name { get; private set; } = "方案一";
        public uint RoleTag { get; private set; }
        public bool IsDirty { get; private set; }
        public bool AttributesDirty { get; private set; }
        public bool ResetAttributeIfNeeded { get; private set; }
        public uint AttributeTotalPoints { get; private set; }
        public IReadOnlyList<uint> Passive => _passive;
        public IReadOnlyList<uint> Pursuit => _pursuit;
        public IReadOnlyList<uint> Opening => _opening;
        public IReadOnlyDictionary<uint, uint> AttributeAllocated => _attributes;

        public uint AttributeUsedPoints
        {
            get
            {
                uint used = 0;
                foreach (uint dim in SkillBuildTemplates.AllDimensions)
                    if (_attributes.TryGetValue(dim, out uint v)) used += v;
                return used;
            }
        }

        public uint AttributeRemaining =>
            AttributeTotalPoints > AttributeUsedPoints ? AttributeTotalPoints - AttributeUsedPoints : 0u;

        public bool NeedsAttributeReset
        {
            get
            {
                foreach (uint dim in SkillBuildTemplates.AllDimensions)
                {
                    uint cur = GetAttribute(dim);
                    uint stored = _storedAttributes.TryGetValue(dim, out uint sv) ? sv : 0u;
                    if (cur < stored) return true;
                }
                return false;
            }
        }

        public uint GetAttribute(uint dimensionId) =>
            _attributes.TryGetValue(dimensionId, out uint value) ? value : 0u;

        public static bool IsConfigurable(SkillDefinition definition) =>
            definition?.abilityKind is 2u or 3u or 4u;

        public void Reset(SkillLoadoutView loadout, uint schemeId)
        {
            SchemeId = schemeId;
            StateVersion = loadout?.StateVersion ?? 0;
            IsDirty = false;
            AttributesDirty = false;
            ResetAttributeIfNeeded = false;
            _passive.Clear();
            _pursuit.Clear();
            _opening.Clear();
            _attributes.Clear();
            _storedAttributes.Clear();

            var scheme = loadout?.FindScheme(schemeId);
            Name = !string.IsNullOrEmpty(scheme?.Name) ? scheme.Name : SkillLoadoutView.DefaultSchemeName(schemeId);
            RoleTag = scheme?.RoleTag ?? 0;
            AttributeTotalPoints = scheme?.AttributeTotalPoints ?? 0u;
            foreach (uint dim in SkillBuildTemplates.AllDimensions)
            {
                uint val = scheme?.GetAllocated(dim) ?? 0u;
                _attributes[dim] = val;
                _storedAttributes[dim] = val;
            }
            if (scheme == null) return;
            _passive.AddRange(scheme.PassiveSkillIds);
            _pursuit.AddRange(scheme.PursuitSkillIds);
            _opening.AddRange(scheme.OpeningSkillIds);
        }

        public void SetName(string name)
        {
            string normalized = name ?? string.Empty;
            if (Name == normalized) return;
            Name = normalized;
            IsDirty = true;
        }

        public void SetRoleTag(uint roleTag)
        {
            if (RoleTag == roleTag) return;
            RoleTag = roleTag;
            IsDirty = true;
        }

        public void SetResetAttributeIfNeeded(bool enabled)
        {
            ResetAttributeIfNeeded = enabled;
        }

        public bool AdjustAttribute(uint dimensionId, int delta)
        {
            if (delta == 0 || AttributeTotalPoints == 0u || !_attributes.ContainsKey(dimensionId)) return false;
            long current = _attributes[dimensionId];
            long next = current + delta;
            if (next < 0) next = 0;
            long maxForDim = current + AttributeRemaining;
            if (next > maxForDim) next = maxForDim;
            if (next == current) return false;
            _attributes[dimensionId] = (uint)next;
            AttributesDirty = true;
            IsDirty = true;
            if (!NeedsAttributeReset) ResetAttributeIfNeeded = false;
            return true;
        }

        public bool ApplyTemplate(int templateIndex)
        {
            if (templateIndex < 0 || templateIndex >= SkillBuildTemplates.All.Length || AttributeTotalPoints == 0u)
                return false;
            var tpl = SkillBuildTemplates.All[templateIndex];
            uint weightSum = tpl.WeightCon + tpl.WeightMag + tpl.WeightStr + tpl.WeightAgi;
            if (weightSum == 0u) return false;

            uint[] weights = { tpl.WeightCon, tpl.WeightMag, tpl.WeightStr, tpl.WeightAgi };
            uint[] dims = SkillBuildTemplates.AllDimensions;
            uint assigned = 0u;
            int primaryIdx = 0;
            for (int i = 0; i < dims.Length; i++)
            {
                if (weights[i] > weights[primaryIdx]) primaryIdx = i;
                uint share = (uint)((ulong)AttributeTotalPoints * weights[i] / weightSum);
                _attributes[dims[i]] = share;
                assigned += share;
            }
            if (AttributeTotalPoints > assigned)
                _attributes[dims[primaryIdx]] += AttributeTotalPoints - assigned;

            RoleTag = tpl.RecommendedRoleTag;
            AttributesDirty = true;
            IsDirty = true;
            if (!NeedsAttributeReset) ResetAttributeIfNeeded = false;
            return true;
        }

        /// <summary>从指定方案（含已到期只读方案）一键复制战法装配、用途与四维加点到当前编辑草稿。</summary>
        public bool CopyFromScheme(SkillLoadoutView loadout, uint sourceSchemeId)
        {
            var source = loadout?.FindScheme(sourceSchemeId);
            if (source == null || sourceSchemeId == SchemeId) return false;
            RoleTag = source.RoleTag;
            _passive.Clear();
            _passive.AddRange(source.PassiveSkillIds);
            _pursuit.Clear();
            _pursuit.AddRange(source.PursuitSkillIds);
            _opening.Clear();
            _opening.AddRange(source.OpeningSkillIds);
            if (AttributeTotalPoints > 0u)
            {
                uint budget = AttributeTotalPoints;
                foreach (uint dim in SkillBuildTemplates.AllDimensions)
                {
                    uint take = Math.Min(source.GetAllocated(dim), budget);
                    _attributes[dim] = take;
                    budget -= take;
                }
                AttributesDirty = true;
            }
            IsDirty = true;
            if (!NeedsAttributeReset) ResetAttributeIfNeeded = false;
            return true;
        }

        public bool Contains(uint skillId) =>
            _passive.Contains(skillId) || _pursuit.Contains(skillId) || _opening.Contains(skillId);

        public uint GetConflict(SkillBookSnapshot snapshot, SkillEntryState entry)
        {
            uint group = entry.Definition.exclusiveGroup;
            if (group == 0) return 0;
            foreach (var list in new[] { _opening, _passive, _pursuit })
                foreach (uint skillId in list)
                    if (skillId != entry.Id && snapshot.Find(skillId)?.Definition?.exclusiveGroup == group)
                        return skillId;
            return 0;
        }

        public bool Remove(uint skillId)
        {
            if (!_opening.Remove(skillId) && !_passive.Remove(skillId) && !_pursuit.Remove(skillId)) return false;
            IsDirty = true;
            return true;
        }

        public Change Toggle(SkillBookSnapshot snapshot, SkillEntryState entry, out uint replacedSkillId)
        {
            replacedSkillId = 0;
            if (Remove(entry.Id)) return Change.Removed;

            var kind = entry.Definition.abilityKind;
            var slots = kind == 4u ? _opening : kind == 3u ? _pursuit : _passive;
            var loadout = snapshot.Loadout;
            uint limit = kind == 4u ? loadout.MaxOpeningSlots : kind == 3u ? loadout.MaxPursuitSlots : loadout.MaxPassiveSlots;
            uint conflict = GetConflict(snapshot, entry);
            int replaceIndex = conflict == 0 ? -1 : slots.IndexOf(conflict);
            if (replaceIndex < 0 && (uint)slots.Count >= limit) return Change.SlotsFull;

            if (replaceIndex >= 0) slots[replaceIndex] = entry.Id;
            else
            {
                if (conflict != 0) Remove(conflict);
                slots.Add(entry.Id);
            }
            IsDirty = true;
            replacedSkillId = conflict;
            return conflict == 0 ? Change.Added : Change.Replaced;
        }
    }

    /// <summary>纯展示状态。默认断线；本地图鉴不能充当已学习技能或钱包。</summary>
    public sealed class SkillBookViewState
    {
        public SkillBookContext Context;
        public SkillBookSnapshot Snapshot;
        public bool IsLoading;
        public bool IsBusy;
        public bool IsConnected;
        public bool IsReadOnly;
        public string Message;
        public string Error;

        /// <summary>仅用于按钮可用性；实际学习、扣款和等级检查由业务层再次校验。</summary>
        public static bool CanUpgrade(SkillBookViewState state, SkillEntryState entry) =>
            state != null && state.IsConnected && !state.IsReadOnly && !state.IsLoading && !state.IsBusy &&
            string.IsNullOrEmpty(state.Error) &&
            state.Snapshot != null && entry?.Definition != null && !entry.IsSeasonal && !entry.NeedsCraftUnlock &&
            entry.Rank < entry.RankLimit &&
            (entry.Definition.maxRank == 0 || entry.Rank < entry.Definition.maxRank) &&
            string.IsNullOrEmpty(entry.LockedReason) && entry.NextRankCost <= state.Snapshot.CurrencyBalance;
    }

    public sealed class SkillEntryState
    {
        public uint Id;
        public SkillDefinition Definition;
        public uint Rank;
        public uint RankLimit;
        public ulong NextRankCost;
        public string LockedReason;
        public uint SeasonId;
        public uint ScrollId;
        public uint RarityTier;
        public bool CraftUnlockRequired;
        public bool UnlockedByBook;
        public bool Equipped;
        public bool IsSeasonal => SeasonId > 0u;
        public bool IsRare => RarityTier == 1;
        public bool NeedsCraftUnlock => CraftUnlockRequired && !UnlockedByBook;
    }

    /// <summary>一次完整替换的展示快照，不在 UI 中模拟升级、扣款或学习结果。</summary>
    public sealed class SkillBookSnapshot
    {
        public uint ClassId;
        public uint PlayerLevel;
        public ulong CurrencyBalance;
        public string Title;
        public SkillLoadoutView Loadout;
        public readonly List<SkillEntryState> Entries = new();
        public SkillEntryState Find(uint id) => Entries.Find(entry => entry != null && entry.Id == id);
    }

    /// <summary>静态外观和说明；数值仅供展示，战斗计算由业务层负责。</summary>
    [Serializable]
    public sealed class SkillDefinition
    {
        public uint id;
        public uint classId;
        public string className;
        public string name;
        public string type;
        public string category;
        public uint actionTags;
        public string originLabel;
        public uint unlockLevel;
        public uint maxRank;
        public string targetTeam;
        public bool allowDeadTarget;
        public uint maxTargets;
        public double manaPercent;
        public uint cooldownRounds;
        public uint durationRounds;
        public SupportSkillPreview supportPreview;
        public string description;
        public string iconPath;
        public string iconResource;
        public uint scrollId;
        public uint rarityTier;
        public uint abilityKind;
        public uint secondaryTargetPolicy;
        public uint exclusiveGroup;
        public uint skillId => id;
        public bool IsRare => rarityTier == 1;
        public string SecondaryTargetLabel => secondaryTargetPolicy switch
        {
            1 => "副目标：物攻最高",
            2 => "副目标：法攻最高",
            3 => "副目标：气血最低",
            4 => "副目标：血线比例最低",
            5 => "副目标：物防最低",
            6 => "副目标：法防最低",
            7 => "副目标：排速最低",
            _ => null,
        };
        public bool IsPassive => string.Equals(type, "passive", StringComparison.OrdinalIgnoreCase);
        public string IconResource => CharacterPetArt.SkillPath(id) ??
            (string.IsNullOrEmpty(iconResource) ? "UI/Skills/" + id : iconResource);
        public string KindLabel => !string.IsNullOrEmpty(originLabel) ? originLabel :
            IsPassive ? "被动" : IsObstacle ? "障碍" : IsSupport ? "辅助" : "主动";
        public bool IsObstacle => category == "障碍" || category == "obstacle" || category == "control" ||
            !IsPassive && (actionTags & 16u) != 0 && (actionTags & 3u) == 0;
        public bool IsSupport => category == "辅助" || category == "support" || category == "治疗" ||
            !IsPassive && (actionTags & 12u) != 0;
        public string TargetLabel => IsPassive || targetTeam == "self" ? "自身" :
            targetTeam == "ally" ? "友方" : targetTeam == "enemy" ? "敌方" : "—";
    }
}
