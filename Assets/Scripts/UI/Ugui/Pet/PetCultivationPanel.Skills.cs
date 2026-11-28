using System.Linq;
using MmorpgClient.UI.Ugui.Skills;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Pet
{
    public sealed partial class PetCultivationPanel
    {
        private Button _skillUpgrade, _skillUpgradeTen;

        private void BuildSkills(float x, float width)
        {
            Label("PetSkillsTitle", _body, x + 16f, 172f, width - 178f, 40f, "宝宝技能", 32f, Green, role: QdaoUiTypographyRole.Heading);
            var state = SkillState;
            bool hasAuthority = state?.IsConnected == true && !state.IsReadOnly && state.Snapshot != null;
            var refresh = ActionButton("PetSkillsRefresh", _body, x + width - 132f, 170f, 132f, 42f, "刷新", false,
                RefreshSkills, null, 22f);
            refresh.interactable = _client?.IsReady == true && Selected != null && state?.IsLoading != true && state?.IsBusy != true;

            bool hasPassive = hasAuthority && state.Snapshot.Entries.Any(e => e?.Definition?.IsPassive == true);
            string[] categories = hasPassive ? new[] { "主动攻击", "障碍", "辅助", "被动" } : new[] { "主动攻击", "障碍", "辅助" };
            if (_skillCategory >= categories.Length) _skillCategory = 0;
            float categoryWidth = (width - 12f * (categories.Length - 1)) / categories.Length;
            for (int i = 0; i < categories.Length; ++i)
            {
                int index = i;
                ActionButton("PetSkillFilter" + i, _body, x + i * (categoryWidth + 12f), 220f,
                    categoryWidth, 44f, categories[i], _skillCategory == i,
                    () => { _skillCategory = index; Render(); }, _skillCategory == i ? "mail_tab_selected" : "button_secondary", 23f);
            }

            float listX = x, listW = Mathf.Floor((width - 20f) * .44f), detailX = listX + listW + 20f, detailW = width - listW - 20f;
            const float bodyY = 272f;
            const float bodyH = 392f;
            Scroll("PetSkillList", _body, listX, bodyY, listW, bodyH, out var list);
            var entries = hasAuthority ? state.Snapshot.Entries.Where(e => e?.Definition != null && SkillCategory(e.Definition) == _skillCategory).ToArray()
                : new SkillEntryState[0];
            var selected = entries.FirstOrDefault(e => e.Id == _selectedSkillId) ?? entries.FirstOrDefault();
            if (selected != null) _selectedSkillId = selected.Id;
            list.sizeDelta = new Vector2(listW, Mathf.Max(bodyH, entries.Length * 78f));
            for (int i = 0; i < entries.Length; ++i)
            {
                var entry = entries[i];
                bool isSelected = selected == entry;
                var button = ActionButton("PetSkill" + entry.Id, list, 0f, i * 78f, listW, 72f, "", isSelected,
                    () => { _selectedSkillId = entry.Id; Render(); }, isSelected ? "pet_card_selected" : "mail_row_normal");
                var parent = (RectTransform)button.transform;
                var icon = Art("PetSkillIcon" + entry.Id, parent, 12f, 8f, 56f, 56f, "round_badge_taiji");
                icon.sprite = CharacterPetArt.Skill(entry.Id, 64) ?? CharacterPetArt.Get("round_badge_taiji");
                icon.preserveAspect = true;
                // pet_card_selected（guild_row_self）为浅玉绿纸底，用深玉墨绿保高对比度与帮会原图一致
                Label("PetSkillName" + entry.Id, parent, 82f, 6f, listW - 96f, 34f, entry.Definition.name, 26f,
                    isSelected ? Green : RichInk, role: QdaoUiTypographyRole.Heading);
                Label("PetSkillSummary" + entry.Id, parent, 82f, 40f, listW - 96f, 26f,
                    entry.Definition.KindLabel + " · " + (entry.Rank > 0 ? entry.Rank + "级" : "未学习"), 19f,
                    isSelected ? SelectedInk : Muted);
            }
            if (entries.Length == 0)
                Label("PetSkillEmpty", list, 16f, 120f, listW - 32f, 120f,
                    Selected == null ? "请先选择宝宝" : state?.IsLoading == true ? "正在同步技能…" :
                    !string.IsNullOrEmpty(state?.Error) ? state.Error : !hasAuthority ? "技能尚未同步，请刷新" : "此分类暂无技能",
                    23f, Muted, TextAlignmentOptions.Center, wrap: true);

            Art("PetSkillDetailPanel", _body, detailX, bodyY, detailW, bodyH, "mail_row_normal");
            if (selected != null) BuildSkillDetail(selected, state, detailX, detailW, bodyY, bodyH);
            else Label("PetSkillDetailEmpty", _body, detailX + 24f, bodyY + 120f, detailW - 48f, 120f,
                hasAuthority ? "选择技能查看详情" : "技能信息尚未同步\n—", 25f, Muted, TextAlignmentOptions.Center, wrap: true);

            Label("PetSkillTurnRule", _body, x + 4f, 710f, 360f, 36f, "每回合合计选择一招", 19f, Muted);
            if (state != null) _message = state.Error ?? state.Message;
        }

        private void BuildSkillDetail(SkillEntryState entry, SkillBookViewState state, float x, float width, float topY, float panelH)
        {
            var definition = entry.Definition;
            var icon = Art("PetSelectedSkillIcon", _body, x + 22f, topY + 18f, 92f, 92f, "round_badge_taiji");
            icon.sprite = CharacterPetArt.Skill(entry.Id, 128) ?? CharacterPetArt.Get("round_badge_taiji");
            icon.preserveAspect = true;
            Label("PetSelectedSkillName", _body, x + 130f, topY + 20f, width - 150f, 46f, definition.name, 33f, Green, role: QdaoUiTypographyRole.Heading);
            Label("PetSelectedSkillKind", _body, x + 130f, topY + 68f, width - 150f, 32f, definition.KindLabel + "技能", 21f, Muted);

            float scrollY = topY + 124f;
            float scrollH = panelH - 180f;
            Scroll("PetSkillDetail", _body, x + 24f, scrollY, width - 48f, scrollH, out var content);
            float textWidth = width - 48f;
            string category = string.IsNullOrEmpty(definition.category) ? definition.KindLabel : definition.KindLabel + " · " + definition.category;
            Label("PetSkillCategory", content, 0f, 0f, textWidth, 34f, "类别：" + category, 22f, Ink);
            string descText = "主要效果：" + (string.IsNullOrEmpty(definition.description) ? "—" : definition.description);
            var description = Label("PetSkillDescription", content, 0f, 40f, textWidth, 120f, descText, 22f, Ink,
                TextAlignmentOptions.TopLeft, wrap: true);
            description.ForceMeshUpdate();
            float descriptionHeight = Mathf.Max(38f, description.GetPreferredValues(descText, textWidth, float.PositiveInfinity).y + 8f);
            description.rectTransform.sizeDelta = new Vector2(textWidth, descriptionHeight);
            float y = 40f + descriptionHeight + 10f;
            string targets = definition.TargetLabel == "—" || (!definition.IsPassive && definition.targetTeam != "self" && definition.maxTargets == 0) ? "—" :
                definition.TargetLabel + (definition.IsPassive || definition.targetTeam == "self" ? "" : " " + definition.maxTargets + " 个");
            Label("PetSkillTargets", content, 0f, y, textWidth, 34f, "目标：" + targets, 22f, Ink);
            y += 42f;
            string mana = definition.IsPassive ? "无消耗" : $"{definition.manaPercent * 100:0.##}% 法力";
            Label("PetSkillParameters", content, 0f, y, textWidth, 68f,
                $"等级：{entry.Rank}/{entry.RankLimit}    耗蓝：{mana}\n冷却：{definition.cooldownRounds} 回合    持续：{(definition.durationRounds == 0 ? "—" : definition.durationRounds + " 回合")}",
                21f, Ink, TextAlignmentOptions.TopLeft, wrap: true);
            content.sizeDelta = new Vector2(textWidth, y + 76f);

            string condition = entry.LockedReason;
            if (string.IsNullOrEmpty(condition)) condition = entry.Rank >= entry.RankLimit ? "已达当前可修习上限" :
                $"下一级：{entry.NextRankCost} 银两 · 持有 {state.Snapshot.CurrencyBalance}";
            Label("PetSkillLearnCondition", _body, x + 24f, topY + panelH - 46f, width - 48f, 36f, condition, 19f, Muted);

            _skillUpgrade = ActionButton("PetSkillUpgrade", _body, RightColX + RightColWidth - 472f, FooterPrimaryY, 250f, FooterPrimaryHeight, "修习一次", true,
                () => UpgradeSelected(1), null, 27f);
            _skillUpgradeTen = ActionButton("PetSkillUpgradeTen", _body, RightColX + RightColWidth - 208f, FooterSecondaryY, 208f, FooterSecondaryHeight, "修习十次", false,
                () => UpgradeSelected(10), null, 24f);
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
