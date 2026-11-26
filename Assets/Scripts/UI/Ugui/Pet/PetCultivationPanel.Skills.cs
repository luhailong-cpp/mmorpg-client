using System.Linq;
using MmorpgClient.UI.Ugui.Skills;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Pet
{
    public sealed partial class PetCultivationPanel
    {
        private Button _skillUpgrade, _skillUpgradeTen;
        private void BuildSkills(float x, float width)
        {
            Label("PetSkillsTitle", _body, x + 30, 197, width - 210, 44, "宝宝技能", 28, Ink);
            var state = SkillState;
            bool hasAuthority = state?.IsConnected == true && !state.IsReadOnly && state.Snapshot != null;
            var refresh = ActionButton("PetSkillsRefresh", _body, x + width - 166, 197, 135, 44, "刷新", false,
                RefreshSkills, null, 23);
            refresh.interactable = _client?.IsReady == true && Selected != null && state?.IsLoading != true && state?.IsBusy != true;
            bool hasPassive = hasAuthority && state.Snapshot.Entries.Any(e => e?.Definition?.IsPassive == true);
            string[] categories = hasPassive ? new[] { "主动攻击", "障碍", "辅助", "被动" } : new[] { "主动攻击", "障碍", "辅助" };
            if (_skillCategory >= categories.Length) _skillCategory = 0;
            float categoryWidth = (width - 50 - 16 * (categories.Length - 1)) / categories.Length;
            for (int i = 0; i < categories.Length; ++i)
            {
                int index = i;
                ActionButton("PetSkillFilter" + i, _body, x + 24 + i * (categoryWidth + 16), 253,
                    categoryWidth, 47, categories[i], _skillCategory == i,
                    () => { _skillCategory = index; Render(); }, _skillCategory == i ? "mail_tab_selected" : "mail_tab_normal", 24);
            }

            float listX = x + 22, listW = (width - 76) * .45f, detailX = listX + listW + 28, detailW = width - listW - 72;
            Scroll("PetSkillList", _body, listX, 320, listW, 494, out var list);
            var entries = hasAuthority ? state.Snapshot.Entries.Where(e => e?.Definition != null && SkillCategory(e.Definition) == _skillCategory).ToArray()
                : new SkillEntryState[0];
            var selected = entries.FirstOrDefault(e => e.Id == _selectedSkillId) ?? entries.FirstOrDefault();
            if (selected != null) _selectedSkillId = selected.Id;
            list.sizeDelta = new Vector2(listW, Mathf.Max(494, entries.Length * 84));
            for (int i = 0; i < entries.Length; ++i)
            {
                var entry = entries[i]; bool isSelected = selected == entry;
                var button = ActionButton("PetSkill" + entry.Id, list, 0, i * 84, listW, 74, "", isSelected,
                    () => { _selectedSkillId = entry.Id; Render(); }, isSelected ? "pet_card_selected" : "mail_row_normal");
                var parent = (RectTransform)button.transform;
                var icon = Art("PetSkillIcon" + entry.Id, parent, 14, 7, 60, 60, "stat_field");
                icon.sprite = CharacterPetArt.Skill(entry.Id, 64); icon.preserveAspect = true;
                Label("PetSkillName" + entry.Id, parent, 94, 4, listW - 112, 38, entry.Definition.name, 28, isSelected ? Cream : Ink);
                Label("PetSkillSummary" + entry.Id, parent, 94, 39, listW - 112, 30,
                    entry.Definition.KindLabel + " · " + (entry.Rank > 0 ? entry.Rank + "级" : "未学习"), 21, isSelected ? Cream : Muted);
            }
            if (entries.Length == 0)
                Label("PetSkillEmpty", list, 20, 150, listW - 40, 120,
                    Selected == null ? "请先选择宝宝" : state?.IsLoading == true ? "正在同步技能…" :
                    !string.IsNullOrEmpty(state?.Error) ? state.Error : !hasAuthority ? "技能尚未同步，请刷新" : "此分类暂无技能", 26, Muted, TextAnchor.MiddleCenter);
            Art("PetSkillDetailPanel", _body, detailX, 320, detailW, 494, "mail_row_normal");
            if (selected != null) BuildSkillDetail(selected, state, detailX, detailW);
            else Label("PetSkillDetailEmpty", _body, detailX + 30, 440, detailW - 60, 140,
                hasAuthority ? "选择技能查看详情" : "技能信息尚未同步\n—", 28, Muted, TextAnchor.MiddleCenter);
            Label("PetSkillTurnRule", _body, x + 24, 837, width - 48, 42, "每回合合计选择一招", 21, Muted);
            if (state != null) _message = state.Error ?? state.Message;
        }

        private void BuildSkillDetail(SkillEntryState entry, SkillBookViewState state, float x, float width)
        {
            var definition = entry.Definition;
            var icon = Art("PetSelectedSkillIcon", _body, x + 30, 344, 116, 116, "stat_field");
            icon.sprite = CharacterPetArt.Skill(entry.Id, 128); icon.preserveAspect = true;
            Label("PetSelectedSkillName", _body, x + 168, 344, width - 192, 58, definition.name, 36, Ink);
            Label("PetSelectedSkillKind", _body, x + 168, 406, width - 192, 40, definition.KindLabel + "技能", 24, Muted);
            Scroll("PetSkillDetail", _body, x + 36, 478, width - 72, 252, out var content);
            float textWidth = width - 72;
            string category = string.IsNullOrEmpty(definition.category) ? definition.KindLabel : definition.KindLabel + " · " + definition.category;
            Label("PetSkillCategory", content, 0, 0, textWidth, 42, "类别：" + category, 26, Ink);
            var description = Label("PetSkillDescription", content, 0, 53, textWidth, 180,
                "主要效果：" + (string.IsNullOrEmpty(definition.description) ? "—" : definition.description), 26, Ink);
            float descriptionHeight = Mathf.Max(52, description.preferredHeight);
            description.rectTransform.sizeDelta = new Vector2(textWidth, descriptionHeight);
            float y = 53 + descriptionHeight + 14;
            string targets = definition.TargetLabel == "—" || (!definition.IsPassive && definition.targetTeam != "self" && definition.maxTargets == 0) ? "—" :
                definition.TargetLabel + (definition.IsPassive || definition.targetTeam == "self" ? "" : " " + definition.maxTargets + " 个");
            Label("PetSkillTargets", content, 0, y, textWidth, 42, "目标：" + targets, 26, Ink); y += 58;
            string mana = definition.IsPassive ? "无消耗" : $"{definition.manaPercent * 100:0.##}% 法力";
            Label("PetSkillParameters", content, 0, y, textWidth, 92,
                $"等级：{entry.Rank}/{entry.RankLimit}    耗蓝：{mana}\n冷却：{definition.cooldownRounds} 回合    持续：{(definition.durationRounds == 0 ? "—" : definition.durationRounds + " 回合")}", 25, Ink);
            content.sizeDelta = new Vector2(textWidth, y + 102);
            string condition = entry.LockedReason;
            if (string.IsNullOrEmpty(condition)) condition = entry.Rank >= entry.RankLimit ? "已达当前可修习上限" :
                $"下一级：{entry.NextRankCost} 银两 · 持有 {state.Snapshot.CurrencyBalance}";
            Label("PetSkillLearnCondition", _body, x + 36, 744, width - 72, 58, condition, 21, Muted);
            _skillUpgrade = ActionButton("PetSkillUpgrade", _body, x + width - 430, 878, 190, 54, "修习一次", true,
                () => UpgradeSelected(1), null, 26);
            _skillUpgradeTen = ActionButton("PetSkillUpgradeTen", _body, x + width - 224, 878, 200, 54, "修习十次", false,
                () => UpgradeSelected(10), null, 26);
            _skillUpgrade.interactable = _skillUpgradeTen.interactable = !BattleLocked && SkillBookViewState.CanUpgrade(state, entry);
        }

        private void UpgradeSelected(uint count)
        {
            var state = SkillState;
            var entry = state?.Snapshot?.Find(_selectedSkillId);
            if (BattleLocked || !SkillBookViewState.CanUpgrade(state, entry)) return;
            _skills.Upgrade(Context, entry.Id, entry.Rank, count);
        }

        private static int SkillCategory(SkillDefinition definition) => definition.IsPassive ? 3 : definition.IsObstacle ? 1 : definition.IsSupport ? 2 : 0;
    }
}
