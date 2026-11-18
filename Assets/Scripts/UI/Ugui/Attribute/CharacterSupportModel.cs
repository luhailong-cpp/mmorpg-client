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
            public string Detail;
            public SourceLine(string name, string value, string detail = null) { Name = name; Value = value; Detail = detail; }
        }
        public static string Number(ulong value) => value.ToString(CultureInfo.InvariantCulture);
        public static string Percent(uint basisPoints) => (basisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        public static string Percent(double basisPoints) => double.IsNaN(basisPoints) || double.IsInfinity(basisPoints) ? Unknown :
            (basisPoints / 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        public static string[] Attributes(AttributePanelInfo panel)
        {
            var d = panel?.Derived;
            return d == null ? Enumerable.Repeat(Unknown, 6).ToArray() : new[]
            {
                Number(d.Health) + " / " + Number(d.MaxHealth), Number(d.Mana) + " / " + Number(d.MaxMana),
                Number(d.PhysicalAttack), Number(d.MagicAttack), Number(d.Defense), Number(d.Speed)
            };
        }
        private static SupportEffectBreakdown Part(AttributePanelInfo panel, bool shield, SupportEffectSummary current, bool battle)
        {
            var summary = battle ? current : current ?? panel?.SupportEffects;
            return shield ? summary?.Shield : summary?.Healing;
        }
        public static string TotalBonus(AttributePanelInfo panel, bool shield, SupportEffectSummary current = null, bool battle = false)
        {
            var part = Part(panel, shield, current, battle);
            return part?.Complete == true ? Percent(part.TotalBonusBps) : Unknown;
        }
        public static string EquipmentBonus(AttributePanelInfo panel, bool shield) => panel?.EquipmentEffects == null
            ? Unknown : Percent(shield ? panel.EquipmentEffects.ShieldCapacityBps : panel.EquipmentEffects.OutgoingHealBps);
        public static IReadOnlyList<SourceLine> Sources(AttributePanelInfo panel, BagInfo equipment, bool shield,
            SupportEffectSummary current = null, bool battle = false)
        {
            var lines = new List<SourceLine>();
            var part = Part(panel, shield, current, battle);
            if (part != null)
            {
                foreach (var source in part.Sources)
                {
                    string kind = source.Kind == 1 ? "装备" : source.Kind == 2 ? "被动" : source.Kind == 3 ? "临时" : "条件法宝";
                    string value = Percent(source.Conditional ? source.RawBonusBps : source.EffectiveBonusBps);
                    string detail = source.Reason;
                    if (!source.Conditional && source.RawBonusBps != source.EffectiveBonusBps)
                        detail = "原始 " + Percent(source.RawBonusBps) + "；" + detail;
                    if (source.Kind == 3 && !source.Infinite) detail = "剩余 " + Number(source.RemainRounds) + " 回合；" + detail;
                    lines.Add(new SourceLine(kind + " · " + source.Name, value, detail));
                }
                lines.Add(new SourceLine("装备同类来源上限", part.EquipmentCapBps > 0 ? Percent(part.EquipmentCapBps) : Unknown, "仅限制装备层；条件法宝不计常驻总计。"));
                if (!part.Complete) lines.Add(new SourceLine("完整来源", "尚未同步"));
                else if (part.Sources.Count == 0) lines.Insert(0, new SourceLine("当前无适用加成来源", "0%"));
                return lines;
            }
            if (battle) return new[] { new SourceLine("战斗实时来源", "尚未同步") };
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
        public static string[] SkillValues(SkillBookViewState state, uint id, SupportEffectSummary current = null, bool battle = false)
        {
            var entry = FindSkill(state, id);
            var skill = entry?.Definition;
            if (skill == null) return new[] { Unknown, Unknown, Unknown, Unknown };
            string targets = skill.maxTargets == 0 || skill.TargetLabel == Unknown ? Unknown :
                skill.TargetLabel + "最多 " + Number(skill.maxTargets) + " 个";
            string mana = (skill.manaPercent * 100).ToString("0.##", CultureInfo.InvariantCulture) + "% 最大法力";
            string cooldown = Number(skill.cooldownRounds) + " 回合";
            var preview = current?.Previews.FirstOrDefault(p => p.SkillTableId == id);
            if (!battle && preview == null) preview = skill.supportPreview;
            bool known = preview?.Complete == true && preview.SkillTableId == id &&
                !double.IsNaN(preview.BaseValue) && !double.IsInfinity(preview.BaseValue);
            if (known) mana = Number(preview.ManaCost) + " 法力";
            string amount = known ? Number(preview.ReferenceValue) + "（基础 " +
                preview.BaseValue.ToString("0.##", CultureInfo.InvariantCulture) + "）" : Unknown;
            if (id == ShieldSkillId)
                return new[] { known && !preview.Learned ? "未学习 · 第1重独立护盾参考" : "为各目标提供独立容量的护盾", targets, (known ? amount : "容量未同步") + " / 直接伤害",
                    (skill.durationRounds == 0 ? Unknown : Number(skill.durationRounds) + " 回合") + " / " + mana + " / " + cooldown };
            return new[] { known ? "单目标参考 " + amount : "恢复目标气血", targets,
                known ? preview.Learned ? Number(preview.ReferenceRank) + " 重（当前生效）" : "未学习 · 第1重参考" :
                    entry.Rank == 0 ? "未学习" : Number(entry.Rank) + " 重", mana + " / " + cooldown };
        }
    }
}
