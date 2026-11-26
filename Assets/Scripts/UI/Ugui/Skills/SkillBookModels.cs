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

    /// <summary>人物战法构筑装配（3 套流派方案：3 被动 + 1 追击 + 1 开场）写接口，与 ISkillBookSource 隔离避免破坏旧夹具。</summary>
    public interface ISkillLoadoutSource
    {
        void SaveLoadout(uint schemeId, uint roleTag, IReadOnlyList<uint> passiveSkillIds,
            IReadOnlyList<uint> pursuitSkillIds, IReadOnlyList<uint> openingSkillIds,
            Action<bool, string> completed = null);
        void SwitchLoadout(uint schemeId, Action<bool, string> completed = null);
    }

    public sealed class SkillLoadoutSchemeView
    {
        public uint SchemeId;
        public uint RoleTag;
        public readonly List<uint> PassiveSkillIds = new();
        public readonly List<uint> PursuitSkillIds = new();
        public readonly List<uint> OpeningSkillIds = new();
    }

    public sealed class SkillLoadoutView
    {
        public bool CustomEnabled;
        public uint ActiveSchemeId;
        public ulong StateVersion;
        public uint MaxPassiveSlots = 3;
        public uint MaxPursuitSlots = 1;
        public uint MaxOpeningSlots = 1;
        public uint UnlockedSchemes = 3;
        public uint MaxSchemes = 6;
        public string LastRequestId;
        public readonly List<SkillLoadoutSchemeView> Schemes = new();

        public SkillLoadoutSchemeView FindScheme(uint schemeId) =>
            Schemes.Find(s => s != null && s.SchemeId == schemeId);

        public static string DescribeRoleTag(uint roleTag) => roleTag switch
        {
            1 => "输出",
            2 => "控场",
            3 => "辅助",
            _ => "默认",
        };
    }

    /// <summary>仅管理未提交的装配预览；槽位上限与互斥组来自技能书快照。</summary>
    public sealed class SkillLoadoutDraft
    {
        public enum Change { SlotsFull, Added, Removed, Replaced }

        private readonly List<uint> _passive = new();
        private readonly List<uint> _pursuit = new();
        private readonly List<uint> _opening = new();

        public uint SchemeId { get; private set; } = 1;
        public ulong StateVersion { get; private set; }
        public uint RoleTag { get; private set; }
        public bool IsDirty { get; private set; }
        public IReadOnlyList<uint> Passive => _passive;
        public IReadOnlyList<uint> Pursuit => _pursuit;
        public IReadOnlyList<uint> Opening => _opening;

        public static bool IsConfigurable(SkillDefinition definition) =>
            definition?.abilityKind is 2u or 3u or 4u;

        public void Reset(SkillLoadoutView loadout, uint schemeId)
        {
            SchemeId = schemeId;
            StateVersion = loadout?.StateVersion ?? 0;
            IsDirty = false;
            _passive.Clear();
            _pursuit.Clear();
            _opening.Clear();
            var scheme = loadout?.FindScheme(schemeId);
            RoleTag = scheme?.RoleTag ?? 0;
            if (scheme == null) return;
            _passive.AddRange(scheme.PassiveSkillIds);
            _pursuit.AddRange(scheme.PursuitSkillIds);
            _opening.AddRange(scheme.OpeningSkillIds);
        }

        public void SetRoleTag(uint roleTag)
        {
            if (RoleTag == roleTag) return;
            RoleTag = roleTag;
            IsDirty = true;
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
