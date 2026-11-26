using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static MmorpgClient.UI.Ugui.Gameplay.EquipmentUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    public sealed partial class EquipmentWindow
    {
        private bool _craftOwnedMode;

        private void RenderCraft()
        {
            var rules = _craft.Rules;
            Panel(_body, "CraftCategoryPaper", 130, 310, 630, 1030);
            Label(_body, "CraftCategoryTitle", "选择部位", 240, 375, 420, 80, 52, color: Jade);
            string[] names = { "武器", "帽子", "衣服", "鞋子", "首饰" };
            for (int i = 0; i < names.Length; ++i)
            {
                uint kind = (uint)i + 11;
                bool exists = rules?.Recipes.Any(r => r.Product?.EquipKind == kind) == true;
                Button(_body, "CraftKind_" + kind, names[i], 220, 490 + i * 155, 440, () =>
                { if (_craft.Busy || _craft.Pending) return; _recipeKind = kind; _recipeConfig = 0; _craft.ClearSelection(); _scroll.Clear(); Render(); }, _recipeKind == kind, exists && !_craft.Pending && !_craft.Busy);
            }
            Panel(_body, "CraftRecipePaper", 790, 310, 760, 1030);
            Label(_body, "CraftRecipeTitle", _craftOwnedMode ? "选择已有装备" : "选择档位", 900, 375, 530, 80, 52, color: Jade);
            var recipes = EquipmentScreenModel.RecipeList(rules, _recipeKind);
            var owned = OwnedEquipment().Where(i => i.EquipProfile != 0 && i.EquipKind == _recipeKind).ToList();
            int count = _craftOwnedMode ? owned.Count : recipes.Count;
            var list = Scroll(_body, "CraftRecipeList", 880, 490, 590, 730, Math.Max(730, count * 170));
            for (int i = 0; i < count; ++i)
            {
                var item = _craftOwnedMode ? owned[i] : recipes[i].Product;
                bool selected = _craftOwnedMode ? _selectedItem == item.ItemId : _recipeConfig == item.ConfigId;
                var button = ChoiceRow(list, "CraftSelection_" + (_craftOwnedMode ? item.ItemId : item.ConfigId), "", 5, i * 170, 545, 150, () =>
                {
                    if (_craft.Busy || _craft.Pending) return;
                    if (_craftOwnedMode) { _selectedItem = item.ItemId; _craft.ClearSelection(); }
                    else { _recipeConfig = item.ConfigId; _craft.SelectRecipe(item.ConfigId); }
                    Render();
                }, selected, !_craft.Pending && !_craft.Busy);
                Icon(button.transform, item, 10, 15, 110);
                Label(button.transform, "CraftSelectionLabel", item.EquipLevel + "级 · " + Name(item), 145, 20, 370, 110, 38, true);
            }
            if (count == 0) Label(_body, "CraftRecipeEmpty", _craft.LoadingRules ? "正在读取…" : _craftOwnedMode ? "暂无此类装备" : "暂无可用配方，请刷新", 930, 620, 450, 220, 44, true, Muted);
            Button(_body, "CraftMode", _craftOwnedMode ? "返回打造本体" : "为已有装备装词条", 170, 1350, 1350, () =>
            { _craftOwnedMode = !_craftOwnedMode; _craft.ClearSelection(); _scroll.Clear(); Render(); }, enabled: !_craft.Pending);
            Panel(_body, "CraftPreviewPaper", 1600, 310, 2230, 1030);
            var preview = _craft.Preview;
            var product = _craftOwnedMode ? owned.FirstOrDefault(i => i.ItemId == _selectedItem) :
                preview?.Choice?.ConfigId == _recipeConfig ? preview.Result : recipes.FirstOrDefault(r => r.Product.ConfigId == _recipeConfig)?.Product;
            Label(_body, "CraftPreviewTitle", _craftOwnedMode ? "首次安装词条" : "本体预览", 1740, 375, 620, 90, 60, color: Jade);
            var craftMasterySprite = SeasonEconomyUiArt.LoadCraftControl(
                SeasonEconomyUiArt.MasteryBadgeKey(rules?.ForgeMastery?.Tier ?? 1u));
            float craftMasteryX = 2400f;
            if (craftMasterySprite != null)
            {
                var badge = QdaoUguiFactory.CreateImage("CraftMasteryIcon", _body, 2390, 372, 84, 84, craftMasterySprite, false);
                badge.preserveAspect = true;
                craftMasteryX = 2486f;
            }
            Label(_body, "CraftMasterySummary", EquipmentScreenModel.FormatForgeMastery(rules?.ForgeMastery),
                craftMasteryX, 384, 3770f - craftMasteryX, 72, 34, color: Jade);
            if (product == null)
            {
                Label(_body, "CraftSelectHint", "先选择部位，再选择装备档位。\n数值、材料和资格以当前预览为准。", 1810, 600, 1730, 300, 52, true, Muted);
                return;
            }
            Icon(_body, product, 1740, 520, 360);
            Label(_body, "CraftProductName", Name(product) + " · " + product.EquipLevel + "级", 2160, 510, 1480, 100, 56);
            TextLines(_body, "CraftProductDetails", EquipmentScreenModel.DetailLines(product), 2160, 650, 1450, 470, 42);
            if (_craftOwnedMode)
            {
                int index = 0;
                foreach (var slot in product.FixedAffixSlots.OrderBy(s => s.Slot))
                {
                    var selected = slot;
                    Button(_body, "CraftOwnedAffix_" + slot.Slot, slot.Selected == null ? "安装" + EquipmentScreenModel.PoolName(slot.Pool) : EquipmentScreenModel.PoolName(slot.Pool) + "已安装",
                        1730 + index++ * 990, 1150, 930, () => OpenAffix(product.ItemId, selected.Slot), slot.Selected == null, slot.Selected == null && !WriteBlocked);
                }
                if (product.FixedAffixSlots.Count == 0) Label(_body, "CraftNoAffix", "此装备没有可安装的固定词条位置", 1750, 1180, 1820, 80, 42, color: Muted);
            }
            else
            {
                Label(_body, "CraftSeparateSteps", "先打造本体，再选择并确认每个词条。空词条位会保留，不随机补齐。", 1740, 1150, 1840, 130, 40, true, Muted);
                RenderMaterialCost(_body, preview, 1660, 1330, 1380);
                Button(_body, "CraftCommit", _craft.Busy ? "处理中…" : "确认打造本体", 3100, 1350, 660, () => CommitRecipe(false), true, CanCommitRecipe(false));
            }
        }

        private List<BagItemInfo> OwnedEquipment()
        {
            var all = new List<BagItemInfo>();
            if (_client?.Equipment != null) all.AddRange(_client.Equipment.Items);
            if (_client?.CharacterBag != null) all.AddRange(_client.CharacterBag.Items);
            return all.Where(i => i.ItemId != 0 && i.Count != 0 && i.EquipKind != 0).GroupBy(i => i.ItemId).Select(g => g.First()).ToList();
        }

        private void RenderMaterialCost(UnityEngine.Transform parent, EquipmentCraftPreview preview, float x, float y, float width)
        {
            var material = preview?.Material;
            string text = material == null ? "选择后读取当前材料消耗与资格" :
                $"{material.Name}  需要 {material.Count} · 持有 {material.Owned} · 可用 {material.Available}" +
                (material.Available < material.Count ? $"\n缺少 {material.Count - material.Available}" : "");
            if (preview != null && !preview.Available) text += "\n" + UnavailableText(preview);
            Label(parent, "EquipmentMaterialCost", text, x, y, width, 145, 38, true, preview?.Available == true ? Jade : Muted);
        }

        private static string UnavailableText(EquipmentCraftPreview preview)
        {
            if (preview.Material != null && preview.Material.Available < preview.Material.Count) return "可用材料不足";
            // Keep unknown future server errors human-readable; do not expose numeric protocol IDs.
            return "当前暂不可操作，请检查装备等级、背包空间及战斗状态后刷新";
        }

        private void OpenAffix(ulong itemId, uint slot)
        {
            var item = FindItem(itemId);
            var position = item?.FixedAffixSlots.FirstOrDefault(s => s.Slot == slot);
            if (position == null || position.Selected != null || WriteBlocked) return;
            _affixItem = itemId; _affixSlot = slot; _craft.ClearSelection(); _modal = Modal.Affix; _scroll.Clear(); Render();
            if (_craft.Rules == null && !_craft.LoadingRules) _craft.RefreshRules();
        }

        private bool CanCommitRecipe(bool artifact)
        {
            var choice = _craft.Preview?.Choice;
            return _craft.CanCommit && !WriteBlocked && (artifact || !_craftOwnedMode) &&
                choice?.Operation == EquipmentCraftChoice.Types.Operation.CreateItem && choice.ConfigId == (artifact ? _artifactConfig : _recipeConfig);
        }
        private void CommitRecipe(bool artifact) { if (CanCommitRecipe(artifact)) _craft.Commit(); }
        private bool CanInstallAffix()
        {
            var item = FindItem(_affixItem);
            var position = item?.FixedAffixSlots.FirstOrDefault(s => s.Slot == _affixSlot);
            var choice = _craft.Preview?.Choice;
            return item != null && position != null && position.Selected == null && _craft.CanCommit && !WriteBlocked &&
                choice?.Operation == EquipmentCraftChoice.Types.Operation.InstallAffix && choice.ItemId == _affixItem && choice.AffixSlot == _affixSlot &&
                !item.FixedAffixSlots.Any(s => s.Selected?.AffixId == choice.AffixId);
        }
        private void CommitAffix() { if (CanInstallAffix()) _craft.Commit(); }

        private void RenderAffixPicker()
        {
            var item = FindItem(_affixItem);
            var position = item?.FixedAffixSlots.FirstOrDefault(s => s.Slot == _affixSlot);
            var candidates = _craft.Rules?.Affixes.Where(a => a.Pool == position?.Pool).OrderBy(a => a.AffixId).ToList() ?? new List<EquipFixedAffixInfo>();
            Label(_modalLayer, "AffixPickerTitle", "选择" + EquipmentScreenModel.PoolName(position?.Pool ?? 0), 750, 210, 2380, 100, 64, color: Jade);
            Label(_modalLayer, "AffixPickerContext", Name(item) + " · 选择1项 · 同件两个通用词条不能相同", 760, 325, 2350, 90, 40, color: Muted);
            var list = Scroll(_modalLayer, "AffixCandidateList", 740, 440, 1250, 735, Math.Max(735, candidates.Count * 160));
            var choice = _craft.Choice;
            for (int i = 0; i < candidates.Count; i++)
            {
                var affix = candidates[i];
                bool duplicate = item.FixedAffixSlots.Any(s => s.Selected?.AffixId == affix.AffixId);
                string text = affix.Name + "  " + EquipmentScreenModel.AffixValue(affix) + (duplicate ? "  本件已选" : "");
                ChoiceRow(list, "AffixOption_" + affix.AffixId, text, 5, i * 160, 1190, 140, () =>
                    _craft.SelectAffix(_affixItem, _affixSlot, affix.AffixId), choice?.AffixId == affix.AffixId,
                    !duplicate && position?.Selected == null && !_craft.Pending && !_craft.Busy);
            }
            var selected = candidates.FirstOrDefault(a => a.AffixId == choice?.AffixId);
            Label(_modalLayer, "AffixSelectedHeading", selected?.Name ?? "请选择词条", 2110, 450, 1050, 100, 52, color: Jade);
            if (selected != null)
            {
                TextLines(_modalLayer, "AffixDescription", new[] { EquipmentScreenModel.AffixValue(selected), selected.Description,
                    "首次安装后固定生效，已有词条不可在此覆盖。", "另一件首饰可以选择相同组合。" }, 2110, 590, 1060, 480, 44);
            }
            if (candidates.Count == 0) Label(_modalLayer, "AffixPickerEmpty", _craft.LoadingRules ? "正在读取词条…" : "词条未同步，请关闭后刷新", 850, 630, 1000, 200, 46, true, Muted);
            RenderMaterialCost(_modalLayer, _craft.Preview, 2110, 1090, 1050);
            Button(_modalLayer, "AffixCancel", "取消", 870, 1290, 700, CloseModal);
            Button(_modalLayer, "AffixCommit", _craft.Busy ? "安装中…" : "确认安装", 2280, 1290, 850, CommitAffix, true, CanInstallAffix());
        }

        private void RenderArtifacts()
        {
            var recipes = EquipmentScreenModel.RecipeList(_craft.Rules).Where(r => r.Product?.Artifact != null).ToList();
            var positions = EquipBagModel.Slots(_client?.Equipment).Where(s =>
                EquipBagModel.FindEquipSlot(_client?.Equipment, s.Slot)?.EquipKind == 16).ToList();
            for (int i = 0; i < positions.Count && i < 2; i++)
            {
                var slot = positions[i];
                Button(_body, "ArtifactEquippedSlot_" + slot.Slot, slot.Name + " · " + Name(slot.Item), 230 + i * 1770, 310, 1680, () =>
                { _page = EquipmentPage.Overview; _targetSlot = slot.Slot; _selectedItem = slot.Item?.ItemId ?? 0; _craft.ClearSelection(); Render(); });
            }
            var list = Scroll(_body, "ArtifactCatalog", 190, 490, 3570, 800, Math.Max(800, (recipes.Count + 3) / 4 * 400));
            for (int i = 0; i < recipes.Count; i++)
            {
                var recipe = recipes[i]; var product = recipe.Product;
                int owned = EquipmentScreenModel.OwnedCount(_client?.CharacterBag, _client?.Equipment, product.ConfigId);
                int worn = EquipmentScreenModel.EquippedCount(_client?.Equipment, product.ConfigId);
                var card = ChoiceRow(list, "ArtifactRecipe_" + product.ConfigId, "", i % 4 * 875 + 5, i / 4 * 400 + 5, 840, 370, () =>
                { if (_craft.Busy || _craft.Pending) return; _artifactConfig = product.ConfigId; _craft.SelectRecipe(product.ConfigId); Render(); }, _artifactConfig == product.ConfigId, !_craft.Pending && !_craft.Busy);
                Icon(card.transform, product, 20, 20, 200);
                Label(card.transform, "ArtifactName", Name(product), 245, 25, 560, 65, 52, color: Jade);
                Label(card.transform, "ArtifactCount", $"拥有 {owned} 件 · 已装备 {worn} 件", 245, 115, 565, 65, 36);
                Label(card.transform, "ArtifactRecipeCost", "每件需要 " + recipe.MaterialCount + " 份材料", 245, 185, 565, 65, 36, color: Muted);
                // A dedicated click target opens the full restrictions, never truncating the only copy of the effect.
                Button(card.transform, "ArtifactDetails_" + product.ConfigId, "单件详情", 320, 230, 490, () =>
                { _detailProduct = product.Clone(); _modal = Modal.Detail; Render(); });
            }
            if (recipes.Count == 0) Label(_body, "ArtifactCatalogEmpty", _craft.LoadingRules ? "正在读取法宝目录…" : "法宝目录暂未同步，请刷新", 700, 650, 2600, 180, 56, true, Muted);
            var preview = _craft.Preview;
            RenderMaterialCost(_body, preview, 230, 1340, 2450);
            bool ownedSelected = EquipmentScreenModel.OwnedCount(_client?.CharacterBag, _client?.Equipment, _artifactConfig) > 0;
            Button(_body, "ArtifactExchange", _craft.Busy ? "兑换中…" : ownedSelected ? "再次兑换" : "兑换", 2850, 1345, 900,
                () => CommitRecipe(true), true, CanCommitRecipe(true) && preview?.Result?.Artifact != null);
        }
    }
}
