using System;
using MmorpgClient.Game.PlayerFeatures;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 打造与固定词条窗口的选择状态。规则、预览和资产事务归客户端业务层所有，
    /// 本类只保留当前选择；关闭、换绑和销毁均不清理磁盘上的未决请求。
    /// 与 PlayerFeaturesClient 一样由客户端主线程使用。
    /// </summary>
    public sealed class EquipmentCraftUiState : IDisposable
    {
        private PlayerFeaturesClient _client;
        private EquipmentCraftChoice _choice;
        private ulong _playerId;
        private bool _disposed;
        private Action _clientChanged;
        private Action<CommitEquipmentCraftResponse> _clientApplied;

        public event Action Changed;
        /// <summary>仅在业务层确认成功已持久化后发布目标实例 ID。</summary>
        public event Action<ulong> Applied;

        public GetEquipmentCraftRulesResponse Rules => _client?.EquipmentCraftRules;
        public EquipmentCraftChoice Choice => _choice?.Clone();
        public string Error
        {
            get
            {
                if (_client == null) return string.Empty;
                string error = _client.EquipmentCraftError;
                // 尚未进入角色时请求由公共接缝拒绝，打造专用错误尚未写入。
                return error.Length != 0 || _client.SnapshotPlayerId != 0 ? error : _client.BagError;
            }
        }
        public bool LoadingRules => _client?.EquipmentCraftRulesLoading ?? false;
        public bool LoadingPreview => _choice != null && (_client?.EquipmentCraftPreviewLoading ?? false);
        public bool Pending => _client?.HasPendingEquipmentCraft ?? false;

        /// <summary>与打造互斥的任一资产写请求在途；未决回执另由 Pending 表示。</summary>
        public bool Busy => _client != null && (_client.BusyEquipmentCraft || _client.BusyEquip
            || _client.BusySort || _client.BusyTemporary || _client.BusyMissionAction || _client.BusyActivityReward || _client.BusyMaterialReward);

        /// <summary>只显示当前选择对应的权威预览；返回副本，不允许视图改写业务层缓存。</summary>
        public EquipmentCraftPreview Preview
        {
            get
            {
                var preview = _client?.CraftPreview;
                return _choice != null && _choice.Equals(preview?.Choice) ? preview : null;
            }
        }

        public bool CanCommit
        {
            get
            {
                if (_client == null || Busy || Pending || LoadingRules || LoadingPreview || _client.HasPendingActivityReward || _client.HasPendingMaterialReward)
                    return false;
                var preview = Preview;
                return preview != null && preview.Available && preview.Result != null && preview.Material != null;
            }
        }

        public void Bind(PlayerFeaturesClient client)
        {
            if (_disposed || ReferenceEquals(_client, client)) return;
            Unbind();
            _client = client;
            _choice = null;
            _playerId = client?.SnapshotPlayerId ?? 0;
            if (_client != null)
            {
                _clientChanged = () => OnClientChanged(client);
                _clientApplied = response => OnClientApplied(client, response);
                _client.OnChanged += _clientChanged;
                _client.OnEquipmentCraftApplied += _clientApplied;
            }
            Changed?.Invoke();
        }

        public void RefreshRules()
        {
            if (!_disposed && !LoadingRules) _client?.RequestEquipmentCraftRules();
        }

        public void SelectRecipe(uint configId) => Select(new EquipmentCraftChoice
        {
            Operation = EquipmentCraftChoice.Types.Operation.CreateItem,
            ConfigId = configId,
        });

        public void SelectAffix(ulong itemId, uint slot, uint affixId) => Select(new EquipmentCraftChoice
        {
            Operation = EquipmentCraftChoice.Types.Operation.InstallAffix,
            ItemId = itemId,
            AffixSlot = slot,
            AffixId = affixId,
        });

        private void Select(EquipmentCraftChoice choice)
        {
            if (_disposed || _client == null || Busy || Pending) return;
            // 重绘或重复点击不能作废已确认的权威预览；失败后仍可点同一项重试。
            if (choice.Equals(_choice) && (LoadingPreview || Preview != null)) return;
            _choice = choice;
            _client.PreviewEquipmentCraft(choice);
        }

        public void Commit()
        {
            if (!_disposed && CanCommit) _client.CommitEquipmentCraft(Preview);
        }

        public void Recover()
        {
            if (!_disposed && !Busy) _client?.RecoverEquipmentCraft();
        }

        public void ClearSelection()
        {
            if (_disposed || _choice == null) return;
            _choice = null;
            Changed?.Invoke();
        }

        private void OnClientChanged(PlayerFeaturesClient client)
        {
            if (_disposed || !ReferenceEquals(_client, client)) return;
            ulong playerId = client.SnapshotPlayerId;
            // 首次请求从 0 绑定角色时保留新选择；断线、换角的清快照通知则作废旧选择。
            if (_playerId != 0 && _playerId != playerId) _choice = null;
            _playerId = playerId;
            Changed?.Invoke();
        }

        private void OnClientApplied(PlayerFeaturesClient client, CommitEquipmentCraftResponse response)
        {
            if (_disposed || !ReferenceEquals(_client, client)) return;
            ulong playerId = _playerId;
            _choice = null;
            Changed?.Invoke();
            // 重绘监听者可能已经切角或换绑；不把旧角色的成功通知送进新窗口。
            if (!_disposed && ReferenceEquals(_client, client) && _playerId == playerId)
                Applied?.Invoke(response.ItemId);
        }

        private void Unbind()
        {
            if (_client == null) return;
            _client.OnChanged -= _clientChanged;
            _client.OnEquipmentCraftApplied -= _clientApplied;
            _clientChanged = null;
            _clientApplied = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unbind();
            _client = null;
            _choice = null;
            _playerId = 0;
            Changed = null;
            Applied = null;
        }
    }
}
