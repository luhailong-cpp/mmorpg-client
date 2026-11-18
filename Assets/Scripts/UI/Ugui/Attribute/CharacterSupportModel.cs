using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MmorpgClient.UI.Ugui.Skills;

namespace MmorpgClient.UI.Ugui.Attribute
{
    /// <summary>人物辅助展示只整理权威快照,不重算战斗数值或把缺失来源补成零。</summary>
    public static class CharacterSupportModel
    {
        public const uint HealingSkillId = 10010;
        public const uint ShieldSkillId = 10040;
        public const string Unknown = "—";
        public sealed class SourceLine
        {
            public string Name;
            public string Value;
            public SourceLine(string name, string value) { Name = name; Value = value; }
        }
        public static string Number(ulong value) => value.ToString(CultureInfo.InvariantCulture);
        public static string Percent(uint basisPoints) => (basisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        public static string[] Attributes(AttributePanelInfo panel)
        {
            var d = panel?.Derived;
            return d == null ? Enumerable.Repeat(Unknown, 6).ToArray() : new[]
            {
                Number(d.Health) + " / " + Number(d.MaxHealth), Number(d.Mana) + " / " + Number(d.MaxMana),
                Number(d.PhysicalAttack), Number(d.MagicAttack), Number(d.Defense), Number(d.Speed)
            };
        }
        // 当前协议只下发装备分项。装备数值不能冒充装备、被动、临时状态合并后的总加成。
        public static string TotalBonus(AttributePanelInfo panel, bool shield) => Unknown;
        public static string EquipmentBonus(AttributePanelInfo panel, bool shield) => panel?.EquipmentEffects == null
            ? Unknown : Percent(shield ? panel.EquipmentEffects.ShieldCapacityBps : panel.EquipmentEffects.OutgoingHealBps);
        public static IReadOnlyList<SourceLine> Sources(AttributePanelInfo panel, BagInfo equipment, bool shield)
        {
            var lines = new List<SourceLine>();
            uint effect = shield ? 6u : 5u;
            if (equipment != null)
                foreach (var item in equipment.Items.OrderBy(i => i.ItemId))
                    foreach (var slot in item.FixedAffixSlots.OrderBy(s => s.Slot))
                    {
                        var affix = slot.Selected;
                        if (affix == null || affix.Effect != effect || affix.Unit != 1) continue;
                        string name = string.IsNullOrWhiteSpace(item.Name) ? "装备 #" + Number(item.ItemId) : item.Name;
                        lines.Add(new SourceLine(name + " · " + affix.Name, Percent(affix.ValueBps)));
                    }
            lines.Add(new SourceLine("装备生效值（含来源上限）", EquipmentBonus(panel, shield)));
            lines.Add(new SourceLine("被动", "尚未同步"));
            lines.Add(new SourceLine("临时状态", "尚未同步"));
            return lines;
        }
        public static SkillEntryState FindSkill(SkillBookViewState state, uint id) =>
            state != null && state.IsConnected && !state.IsReadOnly && state.Context != null &&
            !state.Context.IsPet && string.IsNullOrEmpty(state.Error) ? state.Snapshot?.Find(id) : null;
        public static string[] SkillValues(SkillBookViewState state, uint id)
        {
            var entry = FindSkill(state, id);
            var skill = entry?.Definition;
            if (skill == null) return new[] { Unknown, Unknown, Unknown, Unknown };
            string targets = skill.maxTargets == 0 || skill.TargetLabel == Unknown ? Unknown :
                skill.TargetLabel + "最多 " + Number(skill.maxTargets) + " 个";
            string mana = (skill.manaPercent * 100).ToString("0.##", CultureInfo.InvariantCulture) + "% 最大法力";
            string cooldown = Number(skill.cooldownRounds) + " 回合";
            if (id == ShieldSkillId)
                return new[] { "为各目标提供独立容量的护盾", targets, "容量未同步 / 直接伤害",
                    (skill.durationRounds == 0 ? Unknown : Number(skill.durationRounds) + " 回合") + " / " + mana + " / " + cooldown };
            return new[] { "恢复目标气血", targets, entry.Rank == 0 ? "未学习" : Number(entry.Rank) + " 重", mana + " / " + cooldown };
        }
    }
}
