using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.PlayerFeatures;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>总装备窗口的强化页。只呈现业务快照，选择、状态订阅和生命周期由窗口管理。</summary>
    public static class EquipmentEnhancementView
    {
        private const float PanelTop = 330;
        private const float PanelHeight = 1000;
        private const float ListRowHeight = 174;

        public static void Render(RectTransform parent, PlayerFeaturesClient client, EquipForgeUiState forge,
            Action<ulong> selectItem, bool blocked,
            Action<ulong> onRequestEnhanceBindConfirm = null,
            Action<ulong> onOpenUnseal = null,
            Action<ulong> onOpenInherit = null)
        {
            var candidates = Candidates(client);
            var view = forge?.View ?? EquipForgeView.Closed;
            var selected = candidates.FirstOrDefault(item => item.ItemId == view.ItemId);
            var left = EquipmentUiArt.Panel(parent, "EnhancementInventory", 150, PanelTop, 1120, PanelHeight).transform;
            var center = EquipmentUiArt.Panel(parent, "EnhancementSelected", 1310, PanelTop, 1060, PanelHeight).transform;
            var right = EquipmentUiArt.Panel(parent, "EnhancementPreview", 2410, PanelTop, 1406, PanelHeight).transform;

            RenderCandidates(left, client, candidates, selected?.ItemId ?? 0, selectItem);
            RenderSelected(center, selected, view, candidates, blocked, onOpenUnseal, onOpenInherit);
            RenderPreview(right, selected, view);

            bool writeBlocked = WriteBlocked(client, blocked);
            bool canEnhance = selected != null && view.Stage == EquipForgeStage.Ready
                && view.EnhanceButton.Enabled && !writeBlocked;
            bool willBindOnEnhance = selected != null && view.HasNextLevel && selected.EnhanceNext?.WillBindOnEnhance == true;
            bool pityGuaranteed = selected != null && view.Stage == EquipForgeStage.Ready
                && view.HasNextLevel && selected.EnhanceNext?.PityGuaranteedNext == true;
            EquipmentUiArt.Button(parent, "EnhancementRefresh", "刷新", 150, 1350, 420,
                () => forge?.Refresh(), enabled: forge != null && view.RefreshButton.Enabled);
            string commitLabel = willBindOnEnhance
                ? "强化（混用绑定石）"
                : (pityGuaranteed ? view.EnhanceButton.Label + "（必成）" : view.EnhanceButton.Label);
            var commitBtn = EquipmentUiArt.Button(parent, "EnhancementCommit", commitLabel, 3216, 1350, 600,
                () =>
                {
                    // 控件创建后状态仍可能改变；业务层会再次校验，窗口也不把旧选择发给新目标。
                    if (selected == null || forge == null || forge.TargetItemId != selected.ItemId || WriteBlocked(client, blocked))
                        return;
                    if (willBindOnEnhance && onRequestEnhanceBindConfirm != null)
                        onRequestEnhanceBindConfirm(selected.ItemId);
                    else
                        forge.Enhance();
                }, primary: true, enabled: canEnhance);
            if (pityGuaranteed)
            {
                var goldSprite = SeasonEconomyUiArt.LoadControl("btn_primary_gold");
                var commitImg = commitBtn.GetComponent<Image>();
                if (goldSprite != null && commitImg != null)
                {
                    SeasonEconomyUiArt.ApplyHorizontalSlice(commitImg, goldSprite, 140f);
                    var commitText = commitBtn.GetComponentInChildren<TMPro.TMP_Text>();
                    if (commitText != null) commitText.color = SeasonEconomyUiArt.GoldButtonInk;
                }
            }

            string status = WriteBlockReason(client, blocked);
            if (status.Length == 0) status = view.StatusText;
            if (status.Length == 0 && selected != null && !view.EnhanceButton.Enabled) status = view.EnhanceButton.Hint;
            if (status.Length == 0 && view.Stage != EquipForgeStage.Ready && view.Stage != EquipForgeStage.Closed)
                status = view.StageText;
            EquipmentUiArt.Label(parent, "EnhancementStatus", status, 620, 1356, 2500, 126, 42, true);
        }

        private static List<BagItemInfo> Candidates(PlayerFeaturesClient client)
        {
            var result = new List<BagItemInfo>();
            var seen = new HashSet<ulong>();
            void Add(BagInfo bag)
            {
                if (bag == null) return;
                foreach (var item in bag.Items)
                    if (item.ItemId != 0 && seen.Add(item.ItemId) && item.Count != 0
                        && item.EquipProfile == 1 && item.EnhanceMaxLevel > 0) result.Add(item);
            }
            // 两份读取快照可能短暂重叠；与 Forge.FindTarget 使用同一份实例，不能混用其下一级字段。
            Add(client?.CharacterBag);
            Add(client?.Equipment);
            return result;
        }

        private static void RenderCandidates(UnityEngine.Transform parent, PlayerFeaturesClient client, List<BagItemInfo> candidates,
            ulong selectedId, Action<ulong> selectItem)
        {
            var heading = EquipmentUiArt.Label(parent, "EnhancementInventoryTitle", "选择装备 · 全部", 40, 28, 1040, 72, 48);
            var kinds = candidates.Select(item => item.EquipKind).Distinct().ToList();
            var filters = new List<uint?> { null };
            filters.AddRange(kinds.Select(kind => (uint?)kind));
            int filterRows = (filters.Count + 2) / 3;
            float listTop = 116 + filterRows * 154;
            float listHeight = PanelHeight - listTop - 44;
            var content = EquipmentUiArt.Scroll(parent, "EnhancementInventoryScroll", 40, listTop, 1040, listHeight,
                Math.Max(listHeight, candidates.Count * ListRowHeight));
            var rows = new List<RectTransform>();
            var buttons = new List<Button>();
            var worn = new HashSet<ulong>(client?.Equipment?.Items.Select(item => item.ItemId) ?? Enumerable.Empty<ulong>());
            if (client?.CharacterBag != null)
                foreach (var item in client.CharacterBag.Items) worn.Remove(item.ItemId);

            foreach (var item in candidates)
            {
                var row = QdaoUguiFactory.CreateRect("EnhancementItem_" + item.ItemId, content, 0, rows.Count * ListRowHeight, 1040, ListRowHeight);
                rows.Add(row);
                EquipmentUiArt.Icon(row, item, 0, 8, 128);
                var tbBadge = SeasonEconomyUiArt.LoadTradeSlice(item.Bound ? "badge_item_bound_b" : "badge_item_tradable_t");
                if (tbBadge != null)
                {
                    var badgeImg = QdaoUguiFactory.CreateImage("EnhancementItemTradeBadge_" + item.ItemId, row, 976, 36, 56, 56, tbBadge, false);
                    badgeImg.preserveAspect = true;
                }
                string name = EquipDisplay.NameWithEnhance(EquipDisplay.ItemNameOr(item.Name, item.ConfigId), item.EnhanceLevel);
                EquipmentUiArt.Button(row, "EnhancementChoose_" + item.ItemId, name, 148, 0, tbBadge != null ? 816 : 892,
                    () => selectItem?.Invoke(item.ItemId), primary: item.ItemId == selectedId, enabled: selectItem != null);
                string location = worn.Contains(item.ItemId) ? "已穿戴" : "行囊中";
                string tradeTag = item.Bound ? "绑定 B" : "可交易 T";
                EquipmentUiArt.Label(row, "EnhancementItemLocation", location + " · " + KindName(client, item.EquipKind) + " · " + tradeTag,
                    162, 140, 862, 30, 28);
            }

            string emptyText = client == null ? "进入角色后查看可强化装备" :
                client.CharacterBag == null || client.Equipment == null ? "行囊与装备栏尚未同步" : "暂无可强化装备";
            EquipmentUiArt.Label(content, "EnhancementInventoryEmpty", candidates.Count == 0 ? emptyText : string.Empty,
                40, 52, 960, 160, 44, true);

            void Filter(int index)
            {
                uint? kind = filters[index];
                int visible = 0;
                for (int i = 0; i < rows.Count; ++i)
                {
                    bool show = !kind.HasValue || candidates[i].EquipKind == kind.Value;
                    rows[i].gameObject.SetActive(show);
                    if (show) rows[i].anchoredPosition = new Vector2(0, -visible++ * ListRowHeight);
                }
                content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Math.Max(listHeight, visible * ListRowHeight));
                content.anchoredPosition = Vector2.zero;
                heading.text = "选择装备 · " + (kind.HasValue ? KindName(client, kind.Value) : "全部");
                for (int i = 0; i < buttons.Count; ++i) buttons[i].interactable = i != index;
            }

            for (int i = 0; i < filters.Count; ++i)
            {
                int index = i;
                string name = filters[i].HasValue ? KindName(client, filters[i].Value) : "全部";
                buttons.Add(EquipmentUiArt.Button(parent, "EnhancementFilter_" + i, name, 15 + i % 3 * 367,
                    110 + i / 3 * 154, 355, () => Filter(index)));
            }
            Filter(0);
        }

        private static void RenderSelected(UnityEngine.Transform parent, BagItemInfo item, EquipForgeView view,
            List<BagItemInfo> candidates, bool blocked, Action<ulong> onOpenUnseal, Action<ulong> onOpenInherit)
        {
            if (item == null)
            {
                EquipmentUiArt.Label(parent, "EnhancementSelectedEmpty", "从左侧选择一件装备\n查看当前属性与强化预览", 100, 360, 860, 220, 52, true);
                return;
            }
            EquipmentUiArt.Label(parent, "EnhancementSelectedName", view.Title, 60, 40, 940, 140, 54, true);
            EquipmentUiArt.Label(parent, "EnhancementSelectedLocation", view.LocationText, 60, 192, 940, 62, 42);
            EquipmentUiArt.Icon(parent, item, 260, 280, 460);
            var selectedBadge = SeasonEconomyUiArt.LoadTradeSlice(item.Bound ? "badge_item_bound_b" : "badge_item_tradable_t");
            if (selectedBadge != null)
            {
                var badge = QdaoUguiFactory.CreateImage("EnhancementSelectedTradeBadge", parent, 60, 266, 84, 84, selectedBadge, false);
                badge.preserveAspect = true;
            }
            bool isMaxEnhanced = item.EnhanceMaxLevel > 0 && item.EnhanceLevel >= item.EnhanceMaxLevel;
            if (isMaxEnhanced)
            {
                var crownSprite = SeasonEconomyUiArt.LoadIcon("badge_plus11_crown");
                if (crownSprite != null)
                {
                    var crown = QdaoUguiFactory.CreateImage("EnhancementMaxCrown", parent, 700, 260, 140, 140, crownSprite, false);
                    crown.preserveAspect = true;
                }
                EquipmentUiArt.Label(parent, "EnhancementMaxStamp", $"+{item.EnhanceLevel} 满配",
                    650, 404, 260, 52, 38, false, SeasonEconomyUiArt.GoldHighlight);
            }
            EquipmentUiArt.Label(parent, "EnhancementSelectedLevel", view.EnhanceLevelText, 60, 748, 940, 60, 44);
            string isolationRule = item.Bound
                ? "绑定装备 B · 可用「解封剥离符」清空绑定养成恢复纯 T 原胚"
                : "可交易装备 T · 默认仅耗可交易强化石，防误绑";
            EquipmentUiArt.Label(parent, "EnhancementTradeIsolationRule", isolationRule,
                60, 808, 940, 46, 26, true, item.Bound ? SeasonEconomyUiArt.SoftWarnText : EquipmentUiArt.Jade);

            bool hasHigherSource = candidates != null && candidates.Any(src =>
                src.ItemId != item.ItemId && src.EquipKind == item.EquipKind &&
                src.EquipProfile == item.EquipProfile && src.EquipLevel == item.EquipLevel &&
                src.EnhanceLevel > item.EnhanceLevel);
            if (item.Bound && onOpenUnseal != null)
            {
                EquipmentUiArt.Button(parent, "EnhancementUnsealBtn", "解封剥离 (纯T)", 60, 862, 445,
                    () => onOpenUnseal(item.ItemId), primary: false, enabled: !blocked);
            }
            if (hasHigherSource && onOpenInherit != null)
            {
                EquipmentUiArt.Button(parent, "EnhancementInheritBtn", "流派副套继承", item.Bound ? 535 : 60, 862, 465,
                    () => onOpenInherit(item.ItemId), primary: false, enabled: !blocked);
            }
        }

        private static void RenderPreview(UnityEngine.Transform parent, BagItemInfo item, EquipForgeView view)
        {
            EquipmentUiArt.Label(parent, "EnhancementPreviewTitle", "强化详情", 48, 28, 1310, 72, 48);
            if (item == null || view.Stage != EquipForgeStage.Ready)
            {
                string text = view.Stage == EquipForgeStage.Loading || view.Stage == EquipForgeStage.Missing
                    ? view.StageText : "选择装备后显示强化效果与材料消耗";
                EquipmentUiArt.Label(parent, "EnhancementPreviewEmpty", text, 64, 170, 1278, 220, 46, true);
                return;
            }

            EquipmentUiArt.Label(parent, "EnhancementLevelLimit", view.EnhanceLevelText, 48, 118, 1310, 64, 46);
            float previewTop;
            IReadOnlyList<string> lines;
            if (view.HasNextLevel)
            {
                bool hasPity = item.EnhanceNext.PityLimit > 0;
                string pitySuffix = hasPity
                    ? (item.EnhanceNext.PityGuaranteedNext
                        ? $" · 本手保底必成 ({item.EnhanceNext.CurrentPityCount}/{item.EnhanceNext.PityLimit})"
                        : $" · 保底 {item.EnhanceNext.CurrentPityCount}/{item.EnhanceNext.PityLimit} 必成")
                    : (item.EnhanceNext.SuccessRate == 10000 ? " · 必成" : string.Empty);
                string rate = view.SuccessRateText + pitySuffix;
                if (hasPity)
                {
                    EquipmentUiArt.Label(parent, "EnhancementSuccessRate", rate, 48, 192, 1310, 56, 44,
                        color: item.EnhanceNext.PityGuaranteedNext ? SeasonEconomyUiArt.GoldHighlight : (Color?)null);
                    SeasonEconomyUiArt.DrawPityBar(parent, "EnhancementPityBar", 48, 256, 660, 96,
                        item.EnhanceNext.CurrentPityCount, item.EnhanceNext.PityLimit, item.EnhanceNext.PityGuaranteedNext);
                    string pityRuleText = item.EnhanceNext.PityGuaranteedNext
                        ? $"已满保底（{item.EnhanceNext.CurrentPityCount}/{item.EnhanceNext.PityLimit}）· 本次 100% 必成"
                        : $"保底进度：{item.EnhanceNext.CurrentPityCount} / {item.EnhanceNext.PityLimit}（失败不掉级 · 永不清零）";
                    EquipmentUiArt.Label(parent, "EnhancementPityDetail", pityRuleText, 728, 262, 630, 88, 36, true,
                        item.EnhanceNext.PityGuaranteedNext ? SeasonEconomyUiArt.GoldHighlight : EquipmentUiArt.Jade);
                    EquipmentUiArt.Label(parent, "EnhancementCost", view.CostText, 48, 368, 1310, 94, 42, true);
                    EquipmentUiArt.Label(parent, "EnhancementOwned", view.OwnedText, 48, 468, 1310, 94, 42, true);
                    EquipmentUiArt.Label(parent, "EnhancementFailureRule", view.FailRuleText, 48, 568, 1310, 52, 38);
                    EquipmentUiArt.Label(parent, "EnhancementAttributesTitle", "属性预览 · 当前 → +" + item.EnhanceNext.NextLevel,
                        48, 628, 1310, 56, 42);
                    previewTop = 694;
                }
                else
                {
                    EquipmentUiArt.Label(parent, "EnhancementSuccessRate", rate, 48, 200, 1310, 60, 46);
                    EquipmentUiArt.Label(parent, "EnhancementCost", view.CostText, 48, 280, 1310, 110, 44, true);
                    EquipmentUiArt.Label(parent, "EnhancementOwned", view.OwnedText, 48, 400, 1310, 110, 44, true);
                    EquipmentUiArt.Label(parent, "EnhancementFailureRule", view.FailRuleText, 48, 520, 1310, 60, 40);
                    EquipmentUiArt.Label(parent, "EnhancementAttributesTitle", "属性预览 · 当前 → +" + item.EnhanceNext.NextLevel,
                        48, 600, 1310, 62, 44);
                    previewTop = 686;
                }
                lines = view.PreviewLines;
            }
            else
            {
                bool isMax = item.EnhanceMaxLevel > 0 && item.EnhanceLevel >= item.EnhanceMaxLevel;
                float textX = 48f;
                if (isMax)
                {
                    var crownSprite = SeasonEconomyUiArt.LoadIcon("badge_plus11_crown");
                    if (crownSprite != null)
                    {
                        var crown = QdaoUguiFactory.CreateImage("EnhancementPreviewMaxCrown", parent, 48, 210, 108, 108, crownSprite, false);
                        crown.preserveAspect = true;
                        textX = 172f;
                    }
                }
                string reason = isMax ? $"已达到 +{item.EnhanceLevel} 满配顶格上限" : view.EnhanceButton.Hint;
                EquipmentUiArt.Label(parent, "EnhancementNoNextLevel", reason, textX, 220, 1358f - textX, 120, 46, true,
                    isMax ? SeasonEconomyUiArt.GoldHighlight : (Color?)null);
                EquipmentUiArt.Label(parent, "EnhancementAttributesTitle", "当前基础属性", 48, 382, 1310, 62, 44);
                previewTop = 472;
                lines = item.BaseAttrs.OrderBy(line => line.Seq).Select(EquipDisplay.FormatLine).ToList();
            }
            float height = PanelHeight - previewTop - 44;
            var content = EquipmentUiArt.Scroll(parent, "EnhancementAttributesScroll", 48, previewTop, 1310, height,
                Math.Max(height, lines.Count * 88));
            for (int i = 0; i < lines.Count; ++i)
                EquipmentUiArt.Label(content, "EnhancementAttribute_" + i, lines[i], 0, i * 88, 1280, 80, 44, true);
        }

        private static string KindName(PlayerFeaturesClient client, uint kind)
        {
            var slot = client?.Equipment?.Layout?.EquipSlots.FirstOrDefault(definition => definition.EquipKind == kind);
            return string.IsNullOrWhiteSpace(slot?.Name) ? "部位 " + kind : slot.Name;
        }

        private static bool WriteBlocked(PlayerFeaturesClient client, bool blocked)
            => blocked || client == null || client.HasPendingEquipmentCraft || client.BusyEquipmentCraft
                || client.HasPendingActivityReward || client.BusyActivityReward || client.BusyTemporary || client.HasPendingMaterialReward;

        private static string WriteBlockReason(PlayerFeaturesClient client, bool blocked)
        {
            if (client == null) return "进入角色后才可强化装备";
            if (client.HasPendingEquipmentCraft) return PlayerFeaturesClient.EquipmentCraftPendingText;
            if (client.BusyEquipmentCraft) return "装备打造正在处理中，请稍候";
            if (client.HasPendingMaterialReward) return PlayerFeaturesClient.MaterialRewardPendingText;
            if (client.HasPendingActivityReward || client.BusyActivityReward) return "活跃奖励正在确认，请稍候";
            if (client.BusyTemporary) return "临时物品正在取回，请稍候";
            return blocked ? "其他资产操作正在处理中，仍可查看装备与预览" : string.Empty;
        }
    }
}
