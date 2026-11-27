using System;
using TMPro;
using UnityEngine;
using static MmorpgClient.UI.Ugui.Gameplay.EquipmentUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    public sealed partial class EquipmentWindow
    {
        private ulong _materialRevision;
        private uint _materialBatch, _materialCraft, _materialStone;
        private bool _materialSelectionReady;

        public void OpenMaterialRewardModal()
        {
            Show();
            OpenMaterials();
        }

        private void OpenMaterials()
        {
            _modal = Modal.Materials;
            _notice = string.Empty;
            _materialSelectionReady = false;
            Render();
            _client?.RequestEquipmentMaterialReward();
        }

        private void OnMaterialRewardClaimed(ClaimEquipmentMaterialRewardResponse response)
        {
            _materialSelectionReady = false;
            _notice = "材料已领取，背包已同步";
            Render();
        }

        private bool MaterialChoiceEditable(EquipmentMaterialRewardSnapshot snapshot) =>
            snapshot != null && snapshot.Available && snapshot.CanClaim && snapshot.BatchUnits > 0 &&
            _client != null && !_client.MaterialRewardLoading && !WriteBlocked;

        private void AdjustMaterial(bool crafting, bool add) => AdjustMaterialBy(crafting, add ? 1 : -1);

        private void AdjustMaterialBy(bool crafting, int delta)
        {
            var snapshot = _client?.EquipmentMaterialReward;
            if (!MaterialChoiceEditable(snapshot) || !_materialSelectionReady || snapshot.Revision != _materialRevision ||
                snapshot.BatchUnits != _materialBatch || delta == 0) return;
            uint selected = crafting ? _materialCraft : _materialStone;
            uint other = crafting ? _materialStone : _materialCraft;
            uint maxForThis = other <= _materialBatch ? _materialBatch - other : 0u;
            long target = (long)selected + delta;
            if (target < 0L) target = 0L;
            if (target > maxForThis) target = maxForThis;
            if ((uint)target == selected) return;
            if (crafting) _materialCraft = (uint)target; else _materialStone = (uint)target;
            Render();
        }

        private void ClaimMaterials()
        {
            var snapshot = _client?.EquipmentMaterialReward;
            if (!MaterialChoiceEditable(snapshot) || !_materialSelectionReady || snapshot.Revision != _materialRevision ||
                snapshot.BatchUnits != _materialBatch || (ulong)_materialCraft + _materialStone != _materialBatch) return;
            _client.ClaimEquipmentMaterialReward(_materialCraft, _materialStone);
        }

        private void RenderMaterials()
        {
            var snapshot = _client?.EquipmentMaterialReward;
            bool pending = _client?.HasPendingMaterialReward == true;
            bool loading = _client?.MaterialRewardLoading == true;
            if (snapshot != null && (!_materialSelectionReady || snapshot.Revision != _materialRevision || snapshot.BatchUnits != _materialBatch))
            {
                _materialRevision = snapshot.Revision; _materialBatch = snapshot.BatchUnits;
                _materialCraft = _materialStone = 0; _materialSelectionReady = true;
            }
            bool editable = MaterialChoiceEditable(snapshot);
            Label(_modalLayer, "EquipmentMaterialsTitle", "材料自选", 1100, 210, 1800, 100, 66, color: Jade);
            string status = !string.IsNullOrEmpty(_client?.MaterialRewardError) ? _client.MaterialRewardError :
                pending ? "正在确认上次材料领取，请勿重复领取" : loading ? "正在读取可分配份额…" :
                snapshot == null ? "材料奖励尚未同步，请刷新" : !snapshot.Available ? snapshot.UnavailableReason :
                snapshot.BatchUnits == 0 ? "暂无待领取材料。完成合格的PVE或匹配PVP可获得份额。" :
                $"本次可分配 {snapshot.BatchUnits} 份 · 待领取共 {snapshot.AvailableUnits} 份";
            Label(_modalLayer, "MaterialChoiceStatus", status, 810, 332, 2350, 108, 42, true, Muted);
            if (snapshot != null && snapshot.Available)
            {
                bool weekly = snapshot.WeeklyPhaseActive;
                uint ledgerEarned = weekly ? snapshot.EarnedThisWeek : snapshot.LifetimeEarned;
                uint ledgerLimit = weekly
                    ? (snapshot.WeeklyLimit > 0u ? snapshot.WeeklyLimit : 150u)
                    : (snapshot.LifetimeLimit > 0u ? snapshot.LifetimeLimit : 660u);
                SeasonEconomyUiArt.DrawPityBar(_modalLayer, "MaterialLedgerBar", 810, 452, 660, 92,
                    ledgerEarned, ledgerLimit, ledgerEarned >= ledgerLimit);

                uint batchTradable = Math.Min(snapshot.BatchUnits, snapshot.BatchTradableUnits);
                uint batchBound = snapshot.BatchUnits - batchTradable;
                string batchSplit = $"本批可交易 {batchTradable} 份、绑定 {batchBound} 份（可交易优先给打造材料）";
                string softPillText;
                string quotaText;
                if (weekly)
                {
                    string remaining = SeasonEconomyUiArt.FormatWeeklyResetRemaining(
                        snapshot.NextWeekResetAtMs, (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    softPillText = string.IsNullOrEmpty(remaining) ? "周一 05:00 +150 · 最多存4周" : "周一 +150(存4周) · " + remaining;
                    quotaText = $"精进材料滚存池 {ledgerEarned}/{ledgerLimit} 份 · 仙途轻享卡可解锁胜局可交易产出\n" + batchSplit;
                }
                else
                {
                    uint tradableLimit = snapshot.TradableLifetimeLimit > 0u ? snapshot.TradableLifetimeLimit : 60u;
                    softPillText = "无每日打卡 · 随时可续";
                    quotaText = $"首套永久总池 {ledgerEarned}/{ledgerLimit} · 账号共享可交易 {snapshot.TradableEarnedTotal}/{tradableLimit}\n" + batchSplit;
                }

                SeasonEconomyUiArt.CreateSlicedOrSimple(_modalLayer, "MaterialPhasePill", 1490, 458, 540, 80,
                    SeasonEconomyUiArt.LoadControl("notice_banner_soft"), new Color(0.94f, 0.91f, 0.82f, 0.96f));
                var pillLabel = Label(_modalLayer, "MaterialPhasePillText", softPillText, 1540, 464, 440, 68, 32, false, Jade);
                pillLabel.alignment = TextAlignmentOptions.Center;
                pillLabel.enableAutoSizing = true;
                pillLabel.fontSizeMin = 22;
                pillLabel.fontSizeMax = 32;

                float dailyX = 2055f;
                if (batchTradable > 0u)
                {
                    var tradableSprite = SeasonEconomyUiArt.LoadIcon("badge_tradable_tag");
                    if (tradableSprite != null)
                    {
                        var tradableImg = QdaoUguiFactory.CreateImage("MaterialTradableBadge", _modalLayer, 2050, 464, 68, 68, tradableSprite, false);
                        tradableImg.preserveAspect = true;
                        dailyX = 2130f;
                    }
                }
                Label(_modalLayer, "MaterialChoiceDaily", quotaText, dailyX, 448, 3165f - dailyX, 114, 31, true, Muted);

                RenderMaterialCard(snapshot.CraftingMaterial, true, _materialCraft, 810, editable);
                RenderMaterialCard(snapshot.EnhancementStone, false, _materialStone, 2130, editable);
            }
            Label(_modalLayer, "MaterialChoiceAllocated", pending ? "已提交材料分配，正在确认结果" :
                $"已分配 {(ulong)_materialCraft + _materialStone}/{snapshot?.BatchUnits ?? 0}", 1210, 1094, 1700, 76, 48, color: Jade);
            string reason = pending ? "领取结果确认前，分配已锁定；重新打开仍会恢复原领取。" :
                !string.IsNullOrEmpty(_notice) ? _notice :
                snapshot?.Available == true && !snapshot.CanClaim ? snapshot.UnavailableReason :
                WriteBlocked ? WriteReason : "按需要分配打造材料与强化石（绑定与可交易分堆存放，消耗优先扣绑定）。" +
                (snapshot?.WeeklyPhaseActive == true ? "未领取份额保留，可分批自选。" : string.Empty);
            Label(_modalLayer, "MaterialChoiceReason", reason, 810, 1175, 2350, 90, 36, true, Muted);
            Button(_modalLayer, "MaterialChoiceBack", "返回", 790, 1290, 580, CloseModal);
            if (pending)
                Button(_modalLayer, "MaterialChoiceRecover", "确认上次结果", 1440, 1290, 760, () => _client.RetryPendingMaterialReward(), enabled: !_client.BusyMaterialReward);
            else
                Button(_modalLayer, "MaterialChoiceRefresh", "刷新", 1440, 1290, 650, () => _client?.RequestEquipmentMaterialReward(), enabled: !loading && _client != null);
            bool canClaimNow = editable && _materialSelectionReady && (ulong)_materialCraft + _materialStone == _materialBatch;
            var claimBtn = Button(_modalLayer, "MaterialChoiceClaim", pending ? "确认结果中" : "确认领取", 2280, 1290, 850, ClaimMaterials,
                true, canClaimNow);
            if (canClaimNow)
            {
                var goldSprite = SeasonEconomyUiArt.LoadControl("btn_primary_gold");
                var claimImg = claimBtn.GetComponent<UnityEngine.UI.Image>();
                if (goldSprite != null && claimImg != null)
                {
                    SeasonEconomyUiArt.ApplyHorizontalSlice(claimImg, goldSprite, 140f);
                    var claimText = claimBtn.GetComponentInChildren<TMP_Text>();
                    if (claimText != null) claimText.color = SeasonEconomyUiArt.GoldButtonInk;
                }
            }
        }

        private void RenderMaterialCard(EquipmentCraftMaterial material, bool crafting, uint count, float x, bool editable)
        {
            if (material == null) return;
            Icon(_modalLayer, new BagItemInfo { ConfigId = material.ConfigId, Name = material.Name, IconKey = material.IconKey }, x + 25, 582, 196);
            string prefix = crafting ? "MaterialCraft" : "MaterialStone";
            Label(_modalLayer, prefix + "Name", material.Name + " · 持有 " + material.Owned, x + 245, 582, 820, 84, 44, true);
            Label(_modalLayer, prefix + "Usage", crafting ? "用于装备本体、首次词条和法宝兑换" : "用于四件基础装备的强化", x + 245, 672, 820, 108, 37, true, Muted);
            bool canSub = editable && count > 0;
            bool canAdd = editable && (ulong)_materialCraft + _materialStone < _materialBatch;
            Button(_modalLayer, prefix + "Minus5", "−5", x, 792, 355, () => AdjustMaterialBy(crafting, -5), enabled: canSub);
            Button(_modalLayer, prefix + "Plus5", "+5", x + 720, 792, 355, () => AdjustMaterialBy(crafting, 5), enabled: canAdd);
            Button(_modalLayer, prefix + "Minus", "−", x, 942, 355, () => AdjustMaterial(crafting, false), enabled: canSub);
            Label(_modalLayer, prefix + "Count", count.ToString(), x + 425, 962, 260, 100, 60, color: Jade);
            Button(_modalLayer, prefix + "Plus", "+", x + 720, 942, 355, () => AdjustMaterial(crafting, true), enabled: canAdd);
        }
    }
}
