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

        private void AdjustMaterial(bool crafting, bool add)
        {
            var snapshot = _client?.EquipmentMaterialReward;
            if (!MaterialChoiceEditable(snapshot) || !_materialSelectionReady || snapshot.Revision != _materialRevision ||
                snapshot.BatchUnits != _materialBatch) return;
            uint selected = crafting ? _materialCraft : _materialStone;
            if (add)
            {
                if ((ulong)_materialCraft + _materialStone >= _materialBatch) return;
                ++selected;
            }
            else
            {
                if (selected == 0) return;
                --selected;
            }
            if (crafting) _materialCraft = selected; else _materialStone = selected;
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
            string status = pending ? "正在确认上次材料领取，请勿重复领取" : loading ? "正在读取可分配份额…" :
                !string.IsNullOrEmpty(_client?.MaterialRewardError) ? _client.MaterialRewardError :
                snapshot == null ? "材料奖励尚未同步，请刷新" : !snapshot.Available ? snapshot.UnavailableReason :
                snapshot.BatchUnits == 0 ? "暂无待领取材料。完成合格的PVE或匹配PVP可获得份额。" :
                $"本次可分配 {snapshot.BatchUnits} 份 · 待领取共 {snapshot.AvailableUnits} 份";
            Label(_modalLayer, "MaterialChoiceStatus", status, 810, 335, 2350, 125, 44, true, Muted);
            if (snapshot != null && snapshot.Available)
            {
                string quotaText = snapshot.WeeklyPhaseActive
                    ? $"本周已获得 {snapshot.EarnedThisWeek}/{snapshot.WeeklyLimit} 份（全绑定）· 未领取份额保留"
                    : $"首套总池 {snapshot.LifetimeEarned}/{snapshot.LifetimeLimit} 份（无每日限制）· 本批含可交易 {snapshot.BatchTradableUnits} 份";
                Label(_modalLayer, "MaterialChoiceDaily", quotaText, 810, 465, 2350, 100, 35, true, Muted);
                RenderMaterialCard(snapshot.CraftingMaterial, true, _materialCraft, 810, editable);
                RenderMaterialCard(snapshot.EnhancementStone, false, _materialStone, 2130, editable);
            }
            Label(_modalLayer, "MaterialChoiceAllocated", pending ? "已提交材料分配，正在确认结果" :
                $"已分配 {(ulong)_materialCraft + _materialStone}/{snapshot?.BatchUnits ?? 0}", 1210, 1090, 1700, 80, 48, color: Jade);
            string reason = pending ? "领取结果确认前，分配已锁定；重新打开仍会恢复原领取。" :
                !string.IsNullOrEmpty(_notice) ? _notice :
                snapshot?.Available == true && !snapshot.CanClaim ? snapshot.UnavailableReason :
                WriteBlocked ? WriteReason : "按需要分配打造材料与强化石，确认后存入人物背包。";
            Label(_modalLayer, "MaterialChoiceReason", reason, 810, 1175, 2350, 90, 36, true, Muted);
            Button(_modalLayer, "MaterialChoiceBack", "返回", 790, 1290, 580, CloseModal);
            if (pending)
                Button(_modalLayer, "MaterialChoiceRecover", "确认上次结果", 1440, 1290, 760, () => _client.RetryPendingMaterialReward(), enabled: !_client.BusyMaterialReward);
            else
                Button(_modalLayer, "MaterialChoiceRefresh", "刷新", 1440, 1290, 650, () => _client?.RequestEquipmentMaterialReward(), enabled: !loading && _client != null);
            Button(_modalLayer, "MaterialChoiceClaim", pending ? "确认结果中" : "确认领取", 2280, 1290, 850, ClaimMaterials,
                true, editable && _materialSelectionReady && (ulong)_materialCraft + _materialStone == _materialBatch);
        }

        private void RenderMaterialCard(EquipmentCraftMaterial material, bool crafting, uint count, float x, bool editable)
        {
            if (material == null) return;
            Icon(_modalLayer, new BagItemInfo { ConfigId = material.ConfigId, Name = material.Name, IconKey = material.IconKey }, x + 25, 585, 240);
            string prefix = crafting ? "MaterialCraft" : "MaterialStone";
            Label(_modalLayer, prefix + "Name", material.Name + " · 持有 " + material.Owned, x + 300, 590, 760, 100, 45, true);
            Label(_modalLayer, prefix + "Usage", crafting ? "用于装备本体、首次词条和法宝兑换" : "用于四件基础装备的强化", x + 300, 705, 760, 130, 39, true, Muted);
            Button(_modalLayer, prefix + "Minus", "−", x, 890, 355, () => AdjustMaterial(crafting, false), enabled: editable && count > 0);
            Label(_modalLayer, prefix + "Count", count.ToString(), x + 425, 915, 260, 100, 60, color: Jade);
            Button(_modalLayer, prefix + "Plus", "+", x + 720, 890, 355, () => AdjustMaterial(crafting, true),
                enabled: editable && (ulong)_materialCraft + _materialStone < _materialBatch);
        }
    }
}
