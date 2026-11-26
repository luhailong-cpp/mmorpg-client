using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.Season;
using MmorpgClient.UI.Ugui.CoreLoop;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.EquipmentUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    public sealed partial class EquipmentWindow
    {
        private ulong _comparisonOriginal;
        private uint? _comparisonTargetSlot;
        private BagItemInfo _detailProduct;
        private ulong _detailItemId;

        private Button ChoiceRow(UnityEngine.Transform parent, string name, string text, float x, float y, float w, float h,
            Action click, bool selected = false, bool enabled = true)
        {
            var rect = QdaoUguiFactory.CreateRect(name, parent, x, y, w, h);
            var background = rect.gameObject.AddComponent<Image>();
            background.color = selected ? new Color(.73f, .86f, .65f, .95f) : new Color(.98f, .94f, .81f, .82f);
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = selected ? new Color(.87f, .64f, .22f) : new Color(.75f, .64f, .43f, .7f);
            outline.effectDistance = new Vector2(selected ? 4 : 2, -2);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = background;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var colors = button.colors; colors.highlightedColor = new Color(1, .96f, .80f);
            colors.selectedColor = new Color(.91f, 1, .83f); colors.disabledColor = new Color(.78f, .78f, .73f);
            button.colors = colors; button.interactable = enabled;
            if (click != null) button.onClick.AddListener(() => click());
            if (!string.IsNullOrEmpty(text)) Label(rect, name + "Label", text, 25, 12, w - 50, h - 24, 42, true);
            return button;
        }

        private void RenderOverview()
        {
            var slots = EquipBagModel.Slots(_client?.Equipment);
            var definitions = _client?.Equipment?.Layout?.EquipSlots;
            var official = slots.Where(s =>
                definitions?.Any(d => d.Slot == s.Slot && d.EquipKind >= 11 && d.EquipKind <= 16) == true).ToList();
            if (!_targetSlot.HasValue || !official.Any(s => s.Slot == _targetSlot.Value))
            { _targetSlot = official.Count == 0 ? (uint?)null : official[0].Slot; _selectedItem = official.FirstOrDefault().Item?.ItemId ?? 0; }
            Panel(_body, "EquipmentSlotsPaper", 130, 305, 1810, 1110);
            if (official.Count == 0)
                Label(_body, "EquipmentSlotsMissing", _client?.EquipmentLoading == true ? "正在读取装备栏…" : "装备栏尚未同步，请刷新", 300, 600, 1500, 170, 52, true);
            var duplicateKinds = new Dictionary<uint, int>();
            foreach (var view in official)
            {
                uint kind = definitions.First(d => d.Slot == view.Slot).EquipKind;
                int row, column;
                if (kind <= 14) { row = kind == 12 || kind == 14 ? 1 : 0; column = kind >= 13 ? 1 : 0; }
                else { duplicateKinds.TryGetValue(kind, out column); duplicateKinds[kind] = column + 1; row = kind == 15 ? 2 : 3; }
                // Extra server slots stay reachable without pretending they are one of the eight illustrated positions.
                if (column > 1) continue;
                float x = column == 0 ? 200 : 1250, y = 355 + row * 245;
                var slot = view;
                var button = ChoiceRow(_body, "EquipmentSlot_" + slot.Slot, "", x, y, 610, 220,
                    () => { _targetSlot = slot.Slot; _selectedItem = slot.Item?.ItemId ?? 0; _candidate = 0; Render(); }, _targetSlot == slot.Slot);
                if (slot.Item != null) Icon(button.transform, slot.Item, 15, 15, 170);
                else Label(button.transform, "EmptyEquipmentSlot", "+", 45, 35, 120, 120, 90, color: Muted);
                Label(button.transform, "EquipmentSlotName", slot.Name, 205, 20, 380, 65, 46);
                Label(button.transform, "EquipmentSlotItem", slot.Item == null ? "尚未装备" : Name(slot.Item), 205, 95, 380, 105, 36, true, Muted);
                if (slot.Item?.EnhanceMaxLevel > 0) Label(button.transform, "EquipmentEnhanceBadge", "+" + slot.Item.EnhanceLevel, 125, 10, 90, 50, 35, color: Jade);
            }
            RenderSeasonSealSingleSlot();
            var game = AppBootstrap.Instance?.GameClient;
            string appearance = game == null ? null : game.ResolveCharacterId(game.PlayerId);
            if (!string.IsNullOrEmpty(appearance))
            {
                var portrait = MmorpgClient.World.QdaoCharacterCatalog.LoadPortrait(appearance);
                if (portrait != null)
                {
                    var image = QdaoUguiFactory.CreateImage("EquipmentCurrentCharacter", _body, 820, 468, 430, 590, portrait);
                    image.preserveAspect = true;
                }
            }
            var derived = _panel?.Derived; var effects = _panel?.EquipmentEffects;
            var summary = derived == null ? "人物属性尚未同步" :
                $"气血 {derived.MaxHealth}\n物攻 {derived.PhysicalAttack}\n法攻 {derived.MagicAttack}\n治疗加成 {(effects?.OutgoingHealBps ?? 0) / 100m:0.##}%\n护盾加成 {(effects?.ShieldCapacityBps ?? 0) / 100m:0.##}%";
            Label(_body, "EquipmentCharacterSummary", summary, 840, 1070, 390, 300, 35, true, Jade);
            var current = FindItem(_selectedItem);
            Panel(_body, "EquipmentDetailPaper", 1980, 305, 1100, 1040);
            Label(_body, "EquipmentDetailHeading", "装备详情", 2110, 370, 800, 75, 56, color: Jade);
            if (current != null)
            {
                Icon(_body, current, 2100, 475, 230);
                bool hasSignature = !string.IsNullOrWhiteSpace(current.CrafterName);
                Label(_body, "EquipmentDetailName", Name(current), 2370, 495, 560, hasSignature ? 135 : 155, 48, true);
                if (hasSignature)
                {
                    var sealSprite = SeasonEconomyUiArt.LoadCraftControl("seal_master_signature");
                    if (sealSprite != null)
                    {
                        SeasonEconomyUiArt.CreateSlicedOrSimple(_body, "EquipmentDetailMasterSealBg",
                            2370, 636, 560, 64, sealSprite, new Color(0.95f, 0.91f, 0.80f, 0.95f));
                        Label(_body, "EquipmentDetailMasterSealText", $"宗师·{current.CrafterName} 亲手铸造",
                            2428, 640, 450, 56, 32, false, Jade);
                    }
                    else
                    {
                        Label(_body, "EquipmentDetailMasterSealText", $"宗师·{current.CrafterName} 亲手铸造",
                            2370, 638, 560, 60, 34, false, Jade);
                    }
                }
                TextLines(_body, "EquipmentDetailScroll", EquipmentScreenModel.DetailLines(current), 2100, 735, 840, 495);
                bool worn = _client.Equipment.Items.Any(i => i.ItemId == current.ItemId);
                bool canPolish = current.Artifact == null && current.FixedAffixSlots.Any(s => s.Selected != null);
                if (worn && canPolish)
                {
                    Button(_body, "EquipmentDetailOpen", "查看详情", 1980, 1345, 355, () => { _detailProduct = null; _detailItemId = current.ItemId; _modal = Modal.Detail; Render(); });
                    Button(_body, "EquipmentQuickPolish", "打磨换词", 2350, 1345, 355, () => OpenPolishForItem(current.ItemId, 0), true, !WriteBlocked);
                    Button(_body, "EquipmentUnequip", "卸下", 2720, 1345, 355, () => _client.UnequipItem(current.ItemId), enabled: !WriteBlocked);
                }
                else
                {
                    Button(_body, "EquipmentDetailOpen", "查看详情", 2000, 1345, 500, () => { _detailProduct = null; _detailItemId = current.ItemId; _modal = Modal.Detail; Render(); });
                    if (worn) Button(_body, "EquipmentUnequip", "卸下", 2520, 1345, 500, () => _client.UnequipItem(current.ItemId), enabled: !WriteBlocked);
                    else if (canPolish) Button(_body, "EquipmentQuickPolish", "打磨换词", 2520, 1345, 500, () => OpenPolishForItem(current.ItemId, 0), true, !WriteBlocked);
                }
            }
            else Label(_body, "EquipmentEmptyDetail", "选择左侧位置，再从背包中挑选装备。", 2110, 560, 840, 360, 48, true, Muted);
            Panel(_body, "EquipmentCandidatePaper", 3110, 305, 720, 1040);
            Label(_body, "EquipmentCandidateHeading", "背包候选", 3210, 370, 510, 75, 52, color: Jade);
            var candidates = _targetSlot.HasValue ? EquipBagModel.EquipCandidates(_client?.CharacterBag, _client?.Equipment, _targetSlot.Value) : new List<BagItemInfo>();
            var list = Scroll(_body, "EquipmentCandidates", 3190, 480, 550, 730, Math.Max(730, candidates.Count * 180));
            for (int i = 0; i < candidates.Count; i++)
            {
                var item = candidates[i];
                var button = ChoiceRow(list, "EquipmentCandidate_" + item.ItemId, "", 5, i * 180, 510, 160, () => BeginCompare(item.ItemId));
                Icon(button.transform, item, 8, 15, 120);
                Label(button.transform, "CandidateName", Name(item) + "\n未佩戴 · " + item.EquipLevel + "级", 150, 15, 335, 130, 36, true);
            }
            if (candidates.Count == 0) Label(_body, "EquipmentCandidatesEmpty", _client?.BagLoading == true ? "读取中…" : "暂无可替换装备", 3210, 600, 490, 180, 43, true, Muted);
            Button(_body, "EquipmentMaterialEntry", "材料自选", 3130, 1345, 620, OpenMaterials);
            // Occupied historic fixture slots must remain accessible through the original bag view.
            if (slots.Any(s => !official.Any(o => o.Slot == s.Slot) && s.Item != null))
                Label(_body, "EquipmentLegacySlots", "其他旧装备可在背包「已穿戴」中查看和卸下", 230, 1405, 1670, 65, 34, color: Muted);
        }

        /// <summary>在八部位永久装备旁仅放置唯一的「赛季印」单槽入口（方案 A，绝不画多部位赛季装备栏）。</summary>
        private void RenderSeasonSealSingleSlot()
        {
            var seasonState = (_season ?? AppBootstrap.Instance?.GameClient?.Season)?.Snapshot;
            bool available = seasonState != null && seasonState.Available;
            var equipped = available ? SeasonModels.FindEquippedSeal(seasonState) : null;
            var slotBtn = ChoiceRow(_body, "EquipmentSeasonSealSlot", "", 835, 330, 400, 128, () =>
            {
                Hide();
                CoreGameplayUiRoot.EnsureSpawned();
                CoreGameplayUiRoot.Instance?.OpenSeasonSeal();
            }, selected: false, enabled: true);

            SeasonV1UiArt.CreateFixedIcon(slotBtn.transform, "SeasonSealSlotFrame", 12, 14, 100, 100,
                SeasonV1UiArt.LoadControl("season_seal_slot_frame"), new Color(0.76f, 0.62f, 0.32f, 1f));
            var iconSprite = available
                ? SeasonV1UiArt.LoadSealIcon(equipped?.SealId ?? seasonState.EquippedSealId, equipped?.IconKey)
                : SeasonV1UiArt.LoadHudSealIcon();
            SeasonV1UiArt.CreateFixedIcon(slotBtn.transform, "SeasonSealSlotIcon", 24, 26, 76, 76,
                iconSprite, Jade);

            string title = available ? (equipped?.Name ?? "赛季印") : "单槽赛季印";
            string sub = available
                ? $"赛季印 · 进度 {seasonState.ProgressPoints}/{seasonState.MaxPoints}"
                : seasonState == null ? "同步赛季印中…" : "当前暂无赛季";
            Label(slotBtn.transform, "SeasonSealSlotTitle", title, 124, 16, 264, 48, 36, color: Jade);
            Label(slotBtn.transform, "SeasonSealSlotSub", sub, 124, 68, 264, 46, 26, true, Muted);
        }

        private void BeginCompare(ulong itemId)
        {
            if (!_targetSlot.HasValue) return;
            _comparisonTargetSlot = _targetSlot;
            _candidate = itemId; _comparisonOriginal = EquipBagModel.Slots(_client?.Equipment).FirstOrDefault(s => s.Slot == _targetSlot).Item?.ItemId ?? 0;
            _modal = Modal.Compare; Render();
        }

        private void RenderModal()
        {
            var shade = _modalLayer.GetComponent<Image>() ?? _modalLayer.gameObject.AddComponent<Image>();
            shade.color = new Color(.02f, .07f, .055f, .85f); shade.raycastTarget = true;
            if (_modal != Modal.InheritEnhance || SeasonEconomyUiArt.LoadTradeV2Slice("panel_09_equip_inherit") == null)
                Panel(_modalLayer, "EquipmentDialogPaper", 530, 100, 2900, 1390, true);
            switch (_modal)
            {
                case Modal.Compare: RenderComparison(); break;
                case Modal.Affix: RenderAffixPicker(); break;
                case Modal.Detail: RenderFullDetail(); break;
                case Modal.Materials: RenderMaterials(); break;
                case Modal.PolishOdds: RenderPolishOddsModal(); break;
                case Modal.Reforge: RenderReforgeModal(); break;
                case Modal.BindConfirm: RenderBindConfirmModal(); break;
                case Modal.EnhanceBindConfirm: RenderEnhanceBindConfirmModal(); break;
                case Modal.UnsealConfirm: RenderUnsealConfirmModal(); break;
                case Modal.InheritEnhance: RenderInheritEnhanceModal(); break;
                case Modal.UpgradeTier: RenderUpgradeTierModal(); break;
            }
        }

        private void RenderComparison()
        {
            var slot = EquipBagModel.Slots(_client?.Equipment).FirstOrDefault(s => s.Slot == _comparisonTargetSlot);
            var candidate = _comparisonTargetSlot.HasValue ? EquipBagModel.EquipCandidates(_client?.CharacterBag, _client?.Equipment, _comparisonTargetSlot.Value).FirstOrDefault(i => i.ItemId == _candidate) : null;
            Label(_modalLayer, "EquipmentComparisonTitle", "替换装备 · 目标位置：" + slot.Name, 720, 210, 2400, 100, 64, color: Jade);
            Label(_modalLayer, "CurrentEquipmentHeading", "当前装备  " + Name(slot.Item), 720, 350, 1130, 90, 48);
            Label(_modalLayer, "CandidateEquipmentHeading", "待穿戴  " + Name(candidate), 2060, 350, 1140, 90, 48);
            TextLines(_modalLayer, "CurrentEquipmentLines", EquipmentScreenModel.DetailLines(slot.Item), 720, 460, 1100, 480);
            TextLines(_modalLayer, "CandidateEquipmentLines", EquipmentScreenModel.DetailLines(candidate), 2060, 460, 1100, 480);
            TextLines(_modalLayer, "EquipmentComparisonDelta", EquipmentScreenModel.CompareLines(slot.Item, candidate), 740, 995, 2350, 190, 40);
            bool unchanged = _comparisonTargetSlot.HasValue && EquipBagModel.FindEquipSlot(_client?.Equipment, _comparisonTargetSlot.Value) != null &&
                (slot.Item?.ItemId ?? 0) == _comparisonOriginal;
            string reason = candidate == null ? "候选装备已变化，请重新选择" : !unchanged ? "当前装备已变化，请重新比较" : WriteBlocked ? WriteReason : "只替换此位置，同名物品按独立实例穿戴";
            Label(_modalLayer, "EquipmentComparisonReason", reason, 740, 1195, 2390, 70, 38, color: Muted);
            Button(_modalLayer, "EquipmentCompareCancel", "取消", 870, 1290, 700, CloseModal);
            Button(_modalLayer, "EquipmentCompareCommit", "穿戴到" + slot.Name, 2270, 1290, 880, () =>
            {
                if (!_comparisonTargetSlot.HasValue || WriteBlocked) return;
                uint target = _comparisonTargetSlot.Value;
                var now = EquipBagModel.Slots(_client?.Equipment).FirstOrDefault(s => s.Slot == target);
                bool exists = EquipBagModel.EquipCandidates(_client?.CharacterBag, _client?.Equipment, target).Any(i => i.ItemId == _candidate);
                if (!exists || (now.Item?.ItemId ?? 0) != _comparisonOriginal)
                { _notice = "装备已变化，请重新选择并比较"; Render(); return; }
                _client.EquipItem(_candidate, target); _selectedItem = _candidate; _modal = Modal.None; Render();
            }, true, candidate != null && unchanged && !WriteBlocked);
        }

        private void RenderFullDetail()
        {
            var item = _detailProduct ?? FindItem(_detailItemId);
            Label(_modalLayer, "FullEquipmentTitle", Name(item), 800, 210, 2200, 100, 66, color: Jade);
            if (item != null) Icon(_modalLayer, item, 760, 430, 512);
            TextLines(_modalLayer, "FullEquipmentDetails", EquipmentScreenModel.DetailLines(item), 1410, 380, 1720, 800, 45);
            Button(_modalLayer, "EquipmentDetailBack", "返回", 760, 1290, 660, CloseModal);
            if (_detailProduct == null && item != null)
            {
                var empty = item.FixedAffixSlots.FirstOrDefault(s => s.Selected == null);
                if (empty != null) Button(_modalLayer, "EquipmentInstallAffix", "安装" + EquipmentScreenModel.PoolName(empty.Pool), 1490, 1290, 820,
                    () => OpenAffix(item.ItemId, empty.Slot), true, !WriteBlocked);
                else if (item.FixedAffixSlots.Any(s => s.Selected != null))
                    Button(_modalLayer, "EquipmentOpenPolish", "打磨 / 换词条", 1490, 1290, 820,
                        () => OpenPolishForItem(item.ItemId, 0), true, !WriteBlocked);
                if (item.EquipProfile == 0) Button(_modalLayer, "EquipmentLegacyForge", "强化 / 宝石", 2390, 1290, 800, () =>
                { _modal = Modal.None; _forge.Open(item.ItemId); _legacyForge.Show(() => { _input.enabled = true; Render(); }); _input.enabled = false; }, enabled: !WriteBlocked);
                else if (EquipmentScreenModel.CanUpgradeTier(item)) Button(_modalLayer, "EquipmentOpenUpgradeTier", "无损升档", 2390, 1290, 800,
                    () => OpenUpgradeTier(item.ItemId), true, !WriteBlocked);
            }
            else if (item?.Artifact != null)
                Button(_modalLayer, "ArtifactDetailExchange", "兑换", 2390, 1290, 800, () =>
                { _modal = Modal.None; _page = EquipmentPage.Artifacts; _artifactConfig = item.ConfigId; _craft.SelectRecipe(item.ConfigId); Render(); }, true, !WriteBlocked);
        }

    }
}
