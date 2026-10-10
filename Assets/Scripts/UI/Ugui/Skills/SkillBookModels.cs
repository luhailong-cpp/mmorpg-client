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
            state.Snapshot != null && entry?.Definition != null && entry.Rank < entry.RankLimit &&
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
    }

    /// <summary>一次完整替换的展示快照，不在 UI 中模拟升级、扣款或学习结果。</summary>
    public sealed class SkillBookSnapshot
    {
        public uint ClassId;
        public uint PlayerLevel;
        public ulong CurrencyBalance;
        public string Title;
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
        public string originLabel;
        public uint unlockLevel;
        public uint maxRank;
        public string targetTeam;
        public uint maxTargets;
        public double manaPercent;
        public uint cooldownRounds;
        public uint durationRounds;
        public string description;
        public string iconPath;
        public string iconResource;
        public uint skillId => id;
        public bool IsPassive => string.Equals(type, "passive", StringComparison.OrdinalIgnoreCase);
        public string IconResource => string.IsNullOrEmpty(iconResource) ? "UI/Skills/" + id : iconResource;
        public string KindLabel => !string.IsNullOrEmpty(originLabel) ? originLabel :
            IsPassive ? "被动" : IsObstacle ? "障碍" : IsSupport ? "辅助" : "主动";
        public bool IsObstacle => category == "障碍" || category == "obstacle" || category == "control";
        public bool IsSupport => category == "辅助" || category == "support" || category == "治疗";
        public string TargetLabel => IsPassive || targetTeam == "self" ? "自身" :
            targetTeam == "ally" ? "友方" : targetTeam == "enemy" ? "敌方" : "—";
    }
}
