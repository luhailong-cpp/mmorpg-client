using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Game.Skills;
using MmorpgClient.UI.Ugui.Skills;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.EquipmentUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 装备 5 档品相打磨、无损换词条重铸、防误绑二次确认与合规概率公示界面。
    /// 所有消耗、品相档位、保底进度、可交易/绑定态与名匠代打费用一律取自服务端权威快照。
    /// </summary>
    public sealed partial class EquipmentWindow
    {
        private ulong _polishItemId;
        private uint _polishSlot;
        private bool _polishUseCharm;
        private bool _polishAllowBound;
        private ulong _polishProxyMasterId;
        private uint _reforgeTargetAffixId;
        private bool _reforgeAllowBound;
        private bool _bindConfirmForReforge;
        private bool _bindConfirmAcknowledged;

        public void OpenPolishForItem(ulong itemId = 0, ulong proxyMasterPlayerId = 0)
        {
            if (_disposed) return;
            if (!IsVisible) Show(EquipmentPage.Polish);
            else _page = EquipmentPage.Polish;
            _modal = Modal.None;
            _polishItemId = itemId;
            _polishUseCharm = false;
            _polishAllowBound = false;
            _reforgeAllowBound = false;
            _polishProxyMasterId = proxyMasterPlayerId;
            _craft.ClearSelection();
            SkillCraftClient.Instance?.RefreshProxyStalls();
            Render();
            if (_craft.Rules == null && !_craft.LoadingRules) _craft.RefreshRules();
        }

        private void RenderPolish()
        {
            var rules = _craft.Rules;
            var masterySprite = SeasonEconomyUiArt.LoadCraftControl(
                SeasonEconomyUiArt.MasteryBadgeKey(rules?.ForgeMastery?.Tier ?? 1u));
            float masteryLabelX = 180f;
            if (masterySprite != null)
            {
                var masteryImg = QdaoUguiFactory.CreateImage("PolishMasteryIcon", _body, 172, 274, 96, 96, masterySprite, false);
                masteryImg.preserveAspect = true;
                masteryLabelX = 282f;
            }
            Label(_body, "PolishMasteryBadge", EquipmentScreenModel.FormatForgeMastery(rules?.ForgeMastery),
                masteryLabelX, 290, 2960f - masteryLabelX, 70, 40, color: Jade);
            Button(_body, "PolishOddsButton", "品相概率", 3280, 270, 500, () =>
            {
                _modal = Modal.PolishOdds;
                Render();
                if (_craft.Rules == null && !_craft.LoadingRules) _craft.RefreshRules();
            });

            Panel(_body, "PolishItemsPaper", 130, 415, 860, 965);
            Label(_body, "PolishItemsTitle", "选择打磨装备", 200, 465, 720, 70, 48, color: Jade);
            var items = OwnedEquipment().Where(i => i.FixedAffixSlots.Count > 0 && i.Artifact == null).ToList();
            if (_polishItemId == 0 || !items.Any(i => i.ItemId == _polishItemId))
            {
                _polishItemId = items.FirstOrDefault()?.ItemId ?? 0;
                _polishSlot = 0;
            }
            var itemList = Scroll(_body, "PolishItemList", 190, 560, 740, 760, Math.Max(760, items.Count * 165));
            for (int i = 0; i < items.Count; ++i)
            {
                var item = items[i];
                bool selected = item.ItemId == _polishItemId;
                var row = ChoiceRow(itemList, "PolishItem_" + item.ItemId, "", 5, i * 165, 695, 145, () =>
                {
                    if (_craft.Busy || _craft.Pending) return;
                    _polishItemId = item.ItemId;
                    _polishSlot = 0;
                    _polishAllowBound = false;
                    _polishUseCharm = false;
                    _craft.ClearSelection();
                    Render();
                }, selected, !_craft.Pending && !_craft.Busy);
                Icon(row.transform, item, 12, 12, 115);
                var tbBadge = SeasonEconomyUiArt.LoadTradeSlice(item.Bound ? "badge_item_bound_b" : "badge_item_tradable_t");
                if (tbBadge != null)
                {
                    var badgeImg = QdaoUguiFactory.CreateImage("PolishItemTradeBadge", row.transform, 620, 36, 56, 56, tbBadge, false);
                    badgeImg.preserveAspect = true;
                }
                string bindText = item.Bound ? "仅自用" : "可交易";
                Label(row.transform, "PolishItemLabel", $"{Name(item)}\n{item.EquipLevel}级 · {bindText}", 145, 15, tbBadge != null ? 465 : 530, 115, 36, true);
            }
            if (items.Count == 0)
                Label(_body, "PolishItemsEmpty", "暂无可打磨的固定词条装备", 210, 680, 700, 160, 42, true, Muted);

            var current = items.FirstOrDefault(i => i.ItemId == _polishItemId);
            Panel(_body, "PolishSlotsPaper", 1030, 415, 1180, 965);
            Label(_body, "PolishSlotsTitle", "词条品相与重铸", 1110, 465, 980, 70, 48, color: Jade);
            if (current == null)
            {
                Label(_body, "PolishNoItemHint", "先在左侧选择一件已打造装备，再点选需要打磨或更换的词条槽。", 1110, 620, 1000, 260, 44, true, Muted);
                return;
            }

            Icon(_body, current, 1110, 550, 180);
            string header = $"{Name(current)} · {current.EquipLevel}级 · {(current.Bound ? "仅自用" : "可交易")}";
            bool hasSignature = !string.IsNullOrWhiteSpace(current.CrafterName);
            Label(_body, "PolishCurrentItem", header, 1320, 556, 820, hasSignature ? 84 : 175, 40, true);
            if (hasSignature)
            {
                var sealSprite = SeasonEconomyUiArt.LoadCraftControl("seal_master_signature");
                if (sealSprite != null)
                {
                    SeasonEconomyUiArt.CreateSlicedOrSimple(_body, "PolishMasterSealBg", 1320, 648, 640, 72,
                        sealSprite, new Color(0.95f, 0.91f, 0.80f, 0.95f));
                    Label(_body, "PolishMasterSealText", $"宗师·{current.CrafterName} 亲手铸造",
                        1386, 654, 520, 60, 34, false, Jade);
                }
                else
                {
                    Label(_body, "PolishMasterSealText", $"宗师·{current.CrafterName} 亲手铸造",
                        1320, 648, 820, 72, 36, false, Jade);
                }
            }

            var slots = current.FixedAffixSlots.OrderBy(s => s.Slot).ToList();
            if (slots.All(s => s.Slot != _polishSlot)) _polishSlot = slots.FirstOrDefault()?.Slot ?? 0;
            for (int i = 0; i < slots.Count && i < 2; ++i)
            {
                var slot = slots[i];
                var affix = slot.Selected;
                bool selected = slot.Slot == _polishSlot;
                float y = 760 + i * 285;
                string summary = affix == null
                    ? $"{EquipmentScreenModel.PoolName(slot.Pool)} {slot.Slot}：空位（请先在打造页安装词条）"
                    : $"{EquipmentScreenModel.PoolName(slot.Pool)}：{affix.Name} {EquipmentScreenModel.AffixValue(affix)}{EquipmentScreenModel.FormatAffixQualityTag(affix)}";
                if (affix != null)
                {
                    string pity = EquipmentScreenModel.FormatPolishPity(affix);
                    if (!string.IsNullOrEmpty(pity)) summary += "\n" + pity;
                    else if (affix.PolishExempt) summary += "\n速度词条固定满值，无需打磨";
                    else if (affix.QualityTier >= 5) summary += "\n已达最高极品品相（100%）";
                }
                ChoiceRow(_body, "PolishSlot_" + slot.Slot, summary, 1100, y, 1040, 165, () =>
                {
                    if (_craft.Busy || _craft.Pending) return;
                    _polishSlot = slot.Slot;
                    _polishAllowBound = false;
                    RequestPolishPreview(current, slot);
                    Render();
                }, selected, !_craft.Pending && !_craft.Busy);

                if (affix != null)
                {
                    string tagKey = SeasonEconomyUiArt.QualityTagKey(affix.QualityTier);
                    var tagSprite = !string.IsNullOrEmpty(tagKey) ? SeasonEconomyUiArt.LoadCraftControl(tagKey) : null;
                    if (tagSprite != null)
                    {
                        SeasonEconomyUiArt.CreateSlicedOrSimple(_body, "PolishQualityTag_" + slot.Slot,
                            1100, y + 178, 240, 64, tagSprite, Cream);
                        var tagLbl = Label(_body, "PolishQualityTagText_" + slot.Slot,
                            EquipmentScreenModel.QualityTierName(affix.QualityTier, affix.QualityName),
                            1140, y + 182, 160, 56, 32, false, affix.QualityTier >= 5 ? SeasonEconomyUiArt.GoldHighlight : Jade);
                        tagLbl.alignment = TMPro.TextAlignmentOptions.Center;
                    }
                    if (!affix.PolishExempt && affix.QualityTier < 5 && affix.PolishPityLimit > 0)
                    {
                        SeasonEconomyUiArt.DrawPolishPityBar(_body, "PolishSlotPityBar_" + slot.Slot,
                            1358, y + 188, 360, 44, affix.PolishPityCount, affix.PolishPityLimit);
                    }
                    Button(_body, "PolishReforgeBtn_" + slot.Slot, "更换词条", 1740, y + 170, 400, () =>
                    {
                        if (WriteBlocked) return;
                        _polishSlot = slot.Slot;
                        _reforgeTargetAffixId = 0;
                        _reforgeAllowBound = false;
                        _craft.ClearSelection();
                        _modal = Modal.Reforge;
                        Render();
                    }, false, !WriteBlocked);
                }
            }

            var selectedSlot = slots.FirstOrDefault(s => s.Slot == _polishSlot);
            var selectedAffix = selectedSlot?.Selected;
            Panel(_body, "PolishDeskPaper", 2250, 415, 1580, 965);
            Label(_body, "PolishDeskTitle", "淬炼打磨台", 2330, 465, 980, 70, 48, color: Jade);
            if (selectedAffix != null && selectedAffix.QualityTier > 0)
            {
                var deskTag = SeasonEconomyUiArt.LoadCraftControl(SeasonEconomyUiArt.QualityTagKey(selectedAffix.QualityTier));
                if (deskTag != null)
                {
                    SeasonEconomyUiArt.CreateSlicedOrSimple(_body, "PolishDeskQualityTag", 3500, 466, 250, 68, deskTag, Cream);
                    var deskTagLbl = Label(_body, "PolishDeskQualityText",
                        EquipmentScreenModel.QualityTierName(selectedAffix.QualityTier, selectedAffix.QualityName),
                        3540, 470, 170, 60, 34, false, selectedAffix.QualityTier >= 5 ? SeasonEconomyUiArt.GoldHighlight : Jade);
                    deskTagLbl.alignment = TMPro.TextAlignmentOptions.Center;
                }
            }
            Label(_body, "PolishTierLadder", "品相阶梯：凡品 80% → 良品 85% → 上品 90% → 珍品 95% → 极品 100%\n只升不降 · 累计第 8 次必出极品",
                2330, 542, 1420, 92, 34, true, Muted);
            if (selectedAffix != null && !selectedAffix.PolishExempt && selectedAffix.QualityTier < 5 && selectedAffix.PolishPityLimit > 0)
            {
                SeasonEconomyUiArt.DrawPolishPityBar(_body, "PolishDeskPityBar", 2330, 636, 700, 44,
                    selectedAffix.PolishPityCount, selectedAffix.PolishPityLimit);
                Label(_body, "PolishDeskPityText", EquipmentScreenModel.FormatPolishPity(selectedAffix),
                    3050, 636, 700, 46, 30, false,
                    selectedAffix.PolishPityCount + 1 >= selectedAffix.PolishPityLimit ? SeasonEconomyUiArt.GoldHighlight : Jade);
            }

            if (selectedAffix == null)
            {
                Label(_body, "PolishSlotEmptyHint", "所选槽位尚未安装词条，无法打磨。", 2330, 710, 1420, 120, 42, true, Muted);
                return;
            }
            if (selectedAffix.PolishExempt)
            {
                Label(_body, "PolishExemptHint", "速度词条固定满值，不参与品相随机与打磨消耗。", 2330, 710, 1420, 120, 42, true, Jade);
                return;
            }
            if (selectedAffix.QualityTier >= 5)
            {
                Label(_body, "PolishMaxHint", "该词条已达极品（100% 满值），无需继续打磨；如需换流派可点左侧「更换词条」，品相 100% 保留。",
                    2330, 710, 1420, 160, 42, true, Jade);
                return;
            }

            // 首次进入或切换参数后自动向服务端拉取权威预览，不本地猜测消耗。
            var choice = _craft.Choice;
            bool matchingChoice = choice?.Operation == EquipmentCraftChoice.Types.Operation.PolishAffix
                && choice.ItemId == current.ItemId && choice.AffixSlot == selectedSlot.Slot
                && choice.UseGuaranteeCharm == _polishUseCharm && choice.AllowBoundMaterials == _polishAllowBound
                && choice.ProxyMasterPlayerId == _polishProxyMasterId;
            if (!matchingChoice && !_craft.LoadingPreview && !_craft.Busy && !_craft.Pending && string.IsNullOrEmpty(_craft.Error))
            {
                RequestPolishPreview(current, selectedSlot);
            }

            var charmBtn = Button(_body, "PolishCharmToggle", _polishUseCharm ? "✓ 已选：定星保底符（必出极品）" : "使用定星保底符（仅自用 · 必出极品）",
                2330, 690, 1420, () =>
                {
                    if (_craft.Busy || _craft.Pending) return;
                    _polishUseCharm = !_polishUseCharm;
                    _polishAllowBound = false;
                    RequestPolishPreview(current, selectedSlot);
                    Render();
                }, _polishUseCharm, !_craft.Busy && !_craft.Pending);
            var charmIcon = SeasonEconomyUiArt.LoadCraftIcon("item_star_guarantee_charm");
            if (charmIcon != null)
            {
                var charmImg = QdaoUguiFactory.CreateImage("PolishCharmIcon", charmBtn.transform, 52, 26, 88, 88, charmIcon, false);
                charmImg.preserveAspect = true;
            }

            var forgeMasters = ActiveForgeMasters();
            string masterLabel = _polishProxyMasterId == 0
                ? "当前方式：亲自打磨（点击切换主城名匠代打）"
                : FormatSelectedForgeMaster(forgeMasters, _polishProxyMasterId);
            Button(_body, "PolishProxyCycle", masterLabel, 2330, 845, 1020, () =>
            {
                if (_craft.Busy || _craft.Pending) return;
                CycleForgeMaster(forgeMasters);
                RequestPolishPreview(current, selectedSlot);
                Render();
            }, _polishProxyMasterId != 0, !_craft.Busy && !_craft.Pending);
            Button(_body, "PolishProxyRefresh", "刷新名匠", 3380, 845, 370, () =>
            {
                SkillCraftClient.Instance?.RefreshProxyStalls();
                Render();
            });

            var preview = _craft.Preview;
            string previewText = EquipmentScreenModel.FormatCraftPreviewSummary(preview);
            if (preview != null && !preview.Available && preview.UnavailableTip != 0)
                previewText += "\n" + PlayerFeaturesClient.DescribeEquipTip("不可打磨", new TipInfoMessage { Id = preview.UnavailableTip });
            Label(_body, "PolishPreviewCost", previewText, 2330, 1005, 1420, 185, 36, true,
                preview?.Available == true ? Jade : Danger);

            if (_craft.RequiresBindConfirmation)
            {
                Button(_body, "PolishBindConfirmOpen", "允许混用绑定材料（二次确认）", 2330, 1210, 840, () =>
                {
                    _bindConfirmForReforge = false;
                    _bindConfirmAcknowledged = false;
                    _modal = Modal.BindConfirm;
                    Render();
                }, true, !WriteBlocked);
            }
            bool canPolish = _craft.CanCommit && !WriteBlocked && matchingChoice;
            Button(_body, "PolishCommit", _craft.Busy ? "打磨中…" : "打磨一次", 3220, 1210, 530,
                () => { if (canPolish) _craft.Commit(); }, true, canPolish);
        }

        private void RequestPolishPreview(BagItemInfo item, EquipFixedAffixSlotInfo slot)
        {
            var affix = slot?.Selected;
            if (item == null || slot == null || affix == null || affix.PolishExempt || affix.QualityTier >= 5) return;
            _craft.SelectPolishAffix(item.ItemId, slot.Slot, _polishUseCharm, _polishAllowBound,
                _polishProxyMasterId, affix.PolishPityCount);
        }

        private static List<CraftProxyStallInfo> ActiveForgeMasters()
        {
            var stalls = SkillCraftClient.Instance?.ProxyStalls;
            if (stalls == null) return new List<CraftProxyStallInfo>();
            return stalls.Where(s => s != null && s.ForgeMasteryTier >= SkillCraftModels.MasterTier && s.ProxyForgeFeeGold > 0).ToList();
        }

        private void CycleForgeMaster(List<CraftProxyStallInfo> masters)
        {
            if (masters.Count == 0) { _polishProxyMasterId = 0; return; }
            if (_polishProxyMasterId == 0) { _polishProxyMasterId = masters[0].MasterPlayerId; return; }
            int idx = masters.FindIndex(m => m.MasterPlayerId == _polishProxyMasterId);
            _polishProxyMasterId = idx < 0 || idx + 1 >= masters.Count ? 0 : masters[idx + 1].MasterPlayerId;
        }

        private static string FormatSelectedForgeMaster(List<CraftProxyStallInfo> masters, ulong id)
        {
            var found = masters.FirstOrDefault(m => m.MasterPlayerId == id);
            return found == null
                ? $"代打宗师 #{id}（点击切换 / 刷新）"
                : $"名匠代打：{found.MasterName}（极品率 {EquipDisplay.FormatSuccessRate(found.TopQualityRateBps)} · {found.ProxyForgeFeeGold} 银两）";
        }

        private void RenderPolishOddsModal()
        {
            Label(_modalLayer, "PolishOddsTitle", "装备词条品相概率公示", 750, 210, 2380, 100, 62, color: Jade);
            TextLines(_modalLayer, "PolishOddsLines", EquipmentScreenModel.FormatQualityOddsLines(_craft.Rules),
                750, 350, 2400, 860, 42);
            Button(_modalLayer, "PolishOddsClose", "我知道了", 1620, 1280, 700, CloseModal, true);
        }

        private void RenderReforgeModal()
        {
            var item = FindItem(_polishItemId);
            var position = item?.FixedAffixSlots.FirstOrDefault(s => s.Slot == _polishSlot);
            var currentAffix = position?.Selected;
            Label(_modalLayer, "ReforgeTitle", "更换词条（无损保留品相）", 750, 195, 2380, 90, 60, color: Jade);
            Label(_modalLayer, "ReforgeBanner",
                currentAffix == null ? "当前槽位无词条" :
                $"当前：{currentAffix.Name} {EquipmentScreenModel.AffixValue(currentAffix)}{EquipmentScreenModel.FormatAffixQualityTag(currentAffix)}  |  更换词条 100% 保留当前品相与保底进度",
                750, 300, 2400, 80, 38, true, Jade);

            var candidates = _craft.Rules?.Affixes
                .Where(a => a.Pool == position?.Pool)
                .OrderBy(a => a.AffixId).ToList() ?? new List<EquipFixedAffixInfo>();
            var list = Scroll(_modalLayer, "ReforgeCandidateList", 740, 410, 1250, 740, Math.Max(740, candidates.Count * 155));
            for (int i = 0; i < candidates.Count; ++i)
            {
                var affix = candidates[i];
                bool occupied = item != null && item.FixedAffixSlots.Any(s => s.Selected?.AffixId == affix.AffixId);
                string label = $"{affix.Name}  {EquipmentScreenModel.AffixValue(affix)}" + (occupied ? "（本件已有）" : "");
                ChoiceRow(list, "ReforgeOption_" + affix.AffixId, label, 5, i * 155, 1190, 138, () =>
                {
                    if (_craft.Busy || _craft.Pending) return;
                    _reforgeTargetAffixId = affix.AffixId;
                    _reforgeAllowBound = false;
                    _craft.SelectReforgeAffix(_polishItemId, _polishSlot, affix.AffixId, false);
                    Render();
                }, _reforgeTargetAffixId == affix.AffixId, !occupied && currentAffix != null && !_craft.Busy && !_craft.Pending);
            }

            var preview = _craft.Preview;
            string costText = EquipmentScreenModel.FormatCraftPreviewSummary(preview);
            if (preview != null && !preview.Available && preview.UnavailableTip != 0)
                costText += "\n" + PlayerFeaturesClient.DescribeEquipTip("不可更换", new TipInfoMessage { Id = preview.UnavailableTip });
            Label(_modalLayer, "ReforgePreviewCost", costText, 2080, 430, 1080, 520, 38, true,
                preview?.Available == true ? Jade : Danger);

            if (_craft.RequiresBindConfirmation)
            {
                Button(_modalLayer, "ReforgeBindConfirmOpen", "允许混用绑定材料（二次确认）", 2080, 1020, 1020, () =>
                {
                    _bindConfirmForReforge = true;
                    _bindConfirmAcknowledged = false;
                    _modal = Modal.BindConfirm;
                    Render();
                }, true, !WriteBlocked);
            }

            bool canCommit = _craft.CanCommit && !WriteBlocked
                && preview?.Choice?.Operation == EquipmentCraftChoice.Types.Operation.ReforgeAffix
                && preview.Choice.ItemId == _polishItemId && preview.Choice.AffixSlot == _polishSlot
                && preview.Choice.AffixId == _reforgeTargetAffixId;
            Button(_modalLayer, "ReforgeCancel", "取消", 870, 1280, 700, CloseModal);
            Button(_modalLayer, "ReforgeCommit", _craft.Busy ? "更换中…" : "确认更换", 2280, 1280, 850,
                () => { if (canCommit) _craft.Commit(); }, true, canCommit);
        }

        private void RenderBindConfirmModal()
        {
            Label(_modalLayer, "BindConfirmTitle", "转为仅自用二次确认", 750, 190, 2380, 90, 64, color: Danger);
            var alertBanner = SeasonEconomyUiArt.LoadTradeSlice("diff_alert_banner");
            if (alertBanner != null)
                SeasonEconomyUiArt.CreateSlicedOrSimple(_modalLayer, "BindConfirmAlertBanner", 760, 286, 2380, 96,
                    alertBanner, new Color(0.98f, 0.90f, 0.84f, 0.95f));
            var badgeT = SeasonEconomyUiArt.LoadTradeSlice("badge_item_tradable_t");
            var badgeB = SeasonEconomyUiArt.LoadTradeSlice("badge_item_bound_b");
            if (badgeT != null && badgeB != null)
            {
                var tImg = QdaoUguiFactory.CreateImage("BindConfirmFromT", _modalLayer, 1520, 296, 76, 76, badgeT, false);
                tImg.preserveAspect = true;
                var arrow = Label(_modalLayer, "BindConfirmArrow", "可交易 T  →  永久仅自用 B", 1620, 306, 620, 56, 38, false, Danger);
                arrow.alignment = TMPro.TextAlignmentOptions.Center;
                var bImg = QdaoUguiFactory.CreateImage("BindConfirmToB", _modalLayer, 2260, 296, 76, 76, badgeB, false);
                bImg.preserveAspect = true;
            }
            Label(_modalLayer, "BindConfirmBody",
                "当前装备为【可交易 T】状态；系统默认仅消耗可交易材料，当前可交易材料不足。\n\n若主动混用绑定材料（B），该装备将立即转为【绑定仅自用 B】，不可再挂集市或当面交易。",
                760, 404, 1500, 320, 42, true, Ink);

            // 右侧官方小额「解封剥离符」边界明示卡片（不洗白 B 材料）
            Panel(_modalLayer, "BindConfirmUnsealCard", 2300, 404, 840, 320);
            var unsealSprite = SeasonEconomyUiArt.LoadTradeSlice("icon_unseal_talisman");
            if (unsealSprite != null)
            {
                var uImg = QdaoUguiFactory.CreateImage("BindConfirmUnsealIcon", _modalLayer, 2336, 444, 150, 150, unsealSprite, false);
                uImg.preserveAspect = true;
            }
            Label(_modalLayer, "BindConfirmUnsealTitle", "解封剥离符规则", 2506, 434, 600, 58, 40, false, Jade);
            Label(_modalLayer, "BindConfirmUnsealDesc",
                "后续如需重新交易，仅可使用「解封剥离符」清空全部绑定养成，仅恢复纯 T 原胚；绝不会将 B 材料或绑定词条洗白为可交易。",
                2506, 496, 600, 210, 32, true, Muted);

            Button(_modalLayer, "BindConfirmToggle",
                _bindConfirmAcknowledged ? "✓ 我已知晓：混用绑定料后装备转为绑定，后续仅可剥离回纯 T 原胚" : "□ 我已知晓并允许混用绑定材料（装备将变为绑定仅自用）",
                760, 770, 2380, () =>
                {
                    _bindConfirmAcknowledged = !_bindConfirmAcknowledged;
                    Render();
                }, _bindConfirmAcknowledged);

            // 错位防手滑布局：左侧取消，右侧「长按 2 秒确认绑定」轨道
            Button(_modalLayer, "BindConfirmCancel", "返回检查", 820, 1180, 680, () =>
            {
                _modal = _bindConfirmForReforge ? Modal.Reforge : Modal.None;
                Render();
            });

            const float holdW = 1040f;
            const float holdH = 160f;
            var holdRoot = QdaoUguiFactory.CreateRect("BindConfirmHoldProceed", _modalLayer, 2060, 1170, holdW, holdH);
            var trackSprite = SeasonEconomyUiArt.LoadTradeSlice("hold_confirm_button_track");
            var trackImg = holdRoot.gameObject.AddComponent<Image>();
            trackImg.sprite = trackSprite;
            trackImg.color = trackSprite != null ? Color.white : new Color(0.84f, 0.72f, 0.52f, 1f);
            trackImg.type = Image.Type.Simple;
            trackImg.raycastTarget = true;

            float padX = 28f;
            float padY = 22f;
            float innerW = holdW - padX * 2f;
            float innerH = holdH - padY * 2f;
            var fillClip = QdaoUguiFactory.CreateRect("HoldProgressClip", holdRoot, padX, padY, 0f, innerH);
            fillClip.gameObject.AddComponent<RectMask2D>();
            var fillBar = QdaoUguiFactory.CreateImage("HoldProgressFill", fillClip, 0f, 0f, innerW, innerH, null, false);
            fillBar.color = new Color(0.76f, 0.30f, 0.20f, 0.72f);

            var holdLabel = Label(holdRoot, "HoldConfirmLabel",
                _bindConfirmAcknowledged ? "长按 2 秒确认绑定" : "请先勾选上方风险确认",
                30f, 0f, holdW - 60f, holdH, 42, false, Ink);
            holdLabel.alignment = TMPro.TextAlignmentOptions.Center;

            var holdBehaviour = holdRoot.gameObject.AddComponent<HoldToConfirmButtonBehaviour>();
            holdBehaviour.Configure(
                fillClip,
                innerW,
                holdLabel,
                trackImg,
                idleLabel: "长按 2 秒确认绑定",
                disabledLabel: "请先勾选上方风险确认",
                interactable: _bindConfirmAcknowledged,
                onCompleted: () =>
                {
                    if (!_bindConfirmAcknowledged) return;
                    if (_bindConfirmForReforge)
                    {
                        _reforgeAllowBound = true;
                        _modal = Modal.Reforge;
                        _craft.SelectReforgeAffix(_polishItemId, _polishSlot, _reforgeTargetAffixId, true);
                    }
                    else
                    {
                        _polishAllowBound = true;
                        _modal = Modal.None;
                        var item = FindItem(_polishItemId);
                        var slot = item?.FixedAffixSlots.FirstOrDefault(s => s.Slot == _polishSlot);
                        RequestPolishPreview(item, slot);
                    }
                    Render();
                });

            Label(_modalLayer, "BindConfirmHoldHint", "按住不松手，进度满 2 秒后执行；松手或移出即刻取消。",
                2060, 1344, holdW, 56, 30, false, Muted).alignment = TMPro.TextAlignmentOptions.Center;
        }
    }
}
