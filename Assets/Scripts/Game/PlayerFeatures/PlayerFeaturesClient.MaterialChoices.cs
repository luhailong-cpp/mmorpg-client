using System;
using Google.Protobuf;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.PlayerFeatures
{
    /// <summary>材料自选领取：分配来自玩家，额度来自服务端，提交前持久保存完整原意图。</summary>
    public sealed partial class PlayerFeaturesClient
    {
        public const string MaterialRewardPendingText = "材料领取结果尚未确认，请先恢复原请求";
        private const int MaterialAutomaticRecoveryLimit = 3;
        private const long MaterialMinSendIntervalMs = 1000;
        private EquipmentMaterialRewardSnapshot _materialReward;
        private ClaimEquipmentMaterialRewardRequest _pendingMaterialReward;
        private string _pendingMaterialRecord;
        private ulong _materialRecoveryPlayer;
        private bool _materialRecoveryBlocked;
        private int _materialReadRequest, _materialWriteRequest, _materialRecoveryAttempts;
        private long _materialRecoveryAtMs;
        private long? _materialLastSendMs;

        public DurableRequestFileStore MaterialRewardRecovery { get; set; }
        public EquipmentMaterialRewardSnapshot EquipmentMaterialReward => _materialReward?.Clone();
        public bool MaterialRewardLoading { get; private set; }
        public bool BusyMaterialReward { get; private set; }
        public bool HasPendingMaterialReward => _pendingMaterialReward != null || _materialRecoveryBlocked;
        public string MaterialRewardError { get; private set; } = string.Empty;
        /// <summary>只有权威成功已持久、且本机同一意图已清理才发布一次。</summary>
        public event Action<ClaimEquipmentMaterialRewardResponse> OnMaterialRewardClaimed;

        public void RequestEquipmentMaterialReward()
        {
            if (!PrepareRequest(out var playerId)) return;
            int epoch = _epoch, generation = ++_materialReadRequest;
            MaterialRewardLoading = true;
            if (!HasPendingMaterialReward) MaterialRewardError = string.Empty;
            OnChanged?.Invoke();
            if (!IsCurrent(epoch, playerId) || generation != _materialReadRequest) return;
            _net.Call(SceneBagClientPlayerRpc.GetEquipmentMaterialRewardMessageId,
                new GetEquipmentMaterialRewardRequest(), GetEquipmentMaterialRewardResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _materialReadRequest) return;
                    ++_materialReadRequest;
                    MaterialRewardLoading = false;
                    if (HasTip(response.ErrorMessage) || response.Snapshot == null)
                    { MaterialFailure(HasTip(response.ErrorMessage) ? DescribeTip("读取材料额度失败", response.ErrorMessage) : "材料额度未同步，请刷新"); return; }
                    _materialReward = response.Snapshot.Clone();
                    OnChanged?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _materialReadRequest) return;
                    ++_materialReadRequest;
                    MaterialRewardLoading = false;
                    MaterialFailure("材料额度暂未同步，请刷新");
                });
        }

        public void ClaimEquipmentMaterialReward(uint craftingMaterialCount, uint enhancementStoneCount)
        {
            if (!PrepareRequest(out var playerId)) return;
            if (BusyMaterialReward) return;
            if (HasPendingMaterialReward) { MaterialFailure(MaterialRewardPendingText); return; }
            int epoch = _epoch;
            RestoreActivityReward(playerId);
            if (!IsCurrent(epoch, playerId)) return;
            if (HasPendingEquipmentCraft || HasPendingActivityReward || MaterialOtherWriteBusy)
            { MaterialFailure("其他资产操作尚未确认，请先完成原操作"); return; }
            var snapshot = _materialReward;
            ulong total = (ulong)craftingMaterialCount + enhancementStoneCount;
            if (MaterialRewardLoading || snapshot == null || !snapshot.Available || !snapshot.CanClaim
                || snapshot.BatchUnits == 0 || snapshot.AvailableUnits < snapshot.BatchUnits
                || total != snapshot.BatchUnits || snapshot.Revision == ulong.MaxValue
                || snapshot.CraftingMaterial == null || snapshot.EnhancementStone == null
                || snapshot.CraftingMaterial.ConfigId == 0 || snapshot.EnhancementStone.ConfigId == 0
                || snapshot.CraftingMaterial.ConfigId == snapshot.EnhancementStone.ConfigId)
            { MaterialFailure("请按当前可领取份数分配两种材料，并刷新确认额度"); return; }
            if (!CanSendMaterialReward()) { MaterialFailure("材料领取操作太快，请稍候重试"); return; }
            if (MaterialRewardRecovery == null) { MaterialFailure("材料领取恢复存储未就绪，暂不能提交"); return; }
            if (!ClientRequestKeys.TryCreate(out ulong key)) { MaterialFailure(ClientRequestKeys.UnavailableText); return; }
            var request = new ClaimEquipmentMaterialRewardRequest
            {
                ClientRequestId = key, ExpectedRevision = snapshot.Revision,
                CraftingMaterialCount = craftingMaterialCount, EnhancementStoneCount = enhancementStoneCount,
            };
            string record = Convert.ToBase64String(request.ToByteArray());
            try { MaterialRewardRecovery.Save(playerId, record); }
            catch (Exception)
            {
                _materialRecoveryPlayer = 0;
                RestoreMaterialReward(playerId);
                MaterialFailure("无法保存材料领取记录，本次尚未提交；请保留并恢复原记录");
                return;
            }
            _pendingMaterialReward = request;
            _pendingMaterialRecord = record;
            _materialRecoveryAttempts = 0;
            SendMaterialReward(playerId);
        }

        /// <summary>只重放原键、原水位、原分配；跨日与额度新增都不能改写原请求。</summary>
        public void RetryPendingMaterialReward()
        {
            if (PrepareRequest(out var playerId) && CanRecoverMaterialReward()) SendMaterialReward(playerId);
        }

        public void TickMaterialRewardRecovery()
        {
            if (_disposed || !_net.IsReady || _net.PlayerId == 0) return;
            // 先刷新连接代次，不能被旧连接遗留的 Busy 阻止恢复。
            if (!PrepareRequest(out var playerId) || _materialRecoveryAttempts >= MaterialAutomaticRecoveryLimit
                || !CanRecoverMaterialReward()) return;
            ++_materialRecoveryAttempts;
            SendMaterialReward(playerId);
        }

        private bool MaterialOtherWriteBusy => BusyEquip || BusySort || BusyTemporary || BusyMissionAction
            || BusyEquipmentCraft || BusyActivityReward;
        // 多域遗留记录按打造→材料→活跃奖励恢复；只等高优先级未决，避免相互等待。
        private bool CanRecoverMaterialReward() => _pendingMaterialReward != null && !_materialRecoveryBlocked
            && !BusyMaterialReward && !HasPendingEquipmentCraft && !MaterialOtherWriteBusy
            && _nowMs() >= _materialRecoveryAtMs && CanSendMaterialReward();

        private void RestoreMaterialReward(ulong playerId)
        {
            if (_materialRecoveryPlayer == playerId || MaterialRewardRecovery == null) return;
            _materialRecoveryPlayer = playerId;
            try
            {
                string record = MaterialRewardRecovery.Load(playerId);
                if (record.Length == 0) return;
                var request = ClaimEquipmentMaterialRewardRequest.Parser.ParseFrom(Convert.FromBase64String(record));
                if (request.ClientRequestId == 0 || request.ExpectedRevision == ulong.MaxValue
                    || (ulong)request.CraftingMaterialCount + request.EnhancementStoneCount == 0)
                    throw new InvalidOperationException();
                _pendingMaterialReward = request;
                _pendingMaterialRecord = record;
                MaterialRewardError = MaterialRewardPendingText;
            }
            catch (Exception)
            {
                _materialRecoveryBlocked = true;
                MaterialRewardError = "材料领取记录损坏或无法读取，请保留记录并联系支持";
            }
        }

        private void SendMaterialReward(ulong playerId)
        {
            int epoch = _epoch, generation = ++_materialWriteRequest;
            var request = _pendingMaterialReward.Clone();
            string record = _pendingMaterialRecord;
            ++_materialReadRequest;
            _materialReward = null;
            MaterialRewardLoading = false;
            BusyMaterialReward = true;
            MaterialRewardError = string.Empty;
            OnChanged?.Invoke();
            if (!IsCurrent(epoch, playerId) || generation != _materialWriteRequest) return;
            _materialLastSendMs = _nowMs();
            _net.Call(SceneBagClientPlayerRpc.ClaimEquipmentMaterialRewardMessageId, request,
                ClaimEquipmentMaterialRewardResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _materialWriteRequest) return;
                    ++_materialWriteRequest;
                    BusyMaterialReward = false;
                    if (response.ClientRequestId != request.ClientRequestId)
                    { DeferMaterialRecovery("材料领取回执身份不符，原请求已保留"); return; }
                    ++_materialReadRequest;
                    MaterialRewardLoading = false;
                    bool applied = response.Outcome == ClaimEquipmentMaterialRewardResponse.Types.Outcome.Applied
                        && !HasTip(response.ErrorMessage) && request.ExpectedRevision != ulong.MaxValue
                        && response.ResultRevision == request.ExpectedRevision + 1
                        && response.CurrentRevision >= response.ResultRevision;
                    if (response.Outcome == ClaimEquipmentMaterialRewardResponse.Types.Outcome.Rejected)
                    {
                        if (!ClearMaterialRecord(playerId, record)) return;
                        _materialReward = response.Snapshot?.Clone();
                        MaterialFailure(HasTip(response.ErrorMessage) ? DescribeTip("材料领取被拒绝", response.ErrorMessage) : "材料领取被拒绝，请刷新后重新分配");
                        SendDeferredReads(epoch, playerId);
                        return;
                    }
                    if (applied && ValidBag(response.Bag, CharacterBagType) && response.Snapshot != null
                        && response.Snapshot.Revision == response.CurrentRevision)
                    {
                        // 不累加本地材料。整份人物背包替换，同时使此前人物背包读失效，保留仓库查看偏好。
                        if (RequestedBagType == CharacterBagType) { ++_bagRequest; BagLoading = false; BagError = string.Empty; }
                        _bags[CharacterBagType] = response.Bag;
                        _materialReward = response.Snapshot.Clone();
                        if (response.Durable)
                        {
                            if (!ClearMaterialRecord(playerId, record)) return;
                            MaterialRewardError = string.Empty;
                            OnChanged?.Invoke();
                            if (IsCurrent(epoch, playerId) && generation + 1 == _materialWriteRequest)
                                OnMaterialRewardClaimed?.Invoke(response.Clone());
                            SendDeferredReads(epoch, playerId);
                            return;
                        }
                    }
                    if (response.Outcome == ClaimEquipmentMaterialRewardResponse.Types.Outcome.Unknown
                        && response.ErrorMessage?.Id == (uint)common_error.KServiceUnavailable
                        && response.Snapshot?.Available == true)
                    {
                        DeferMaterialRecovery("正在核验账号可交易额度，请稍后刷新再领");
                        return;
                    }
                    DeferMaterialRecovery(applied ? "材料已受理，正在确认持久化；原请求已保留"
                        : "材料领取结果尚未确认；请恢复原请求");
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _materialWriteRequest) return;
                    ++_materialWriteRequest;
                    BusyMaterialReward = false;
                    DeferMaterialRecovery("网络异常，材料领取结果尚未确认；原请求已保留");
                });
        }

        private bool ClearMaterialRecord(ulong playerId, string record)
        {
            try { MaterialRewardRecovery.Clear(playerId, record); }
            catch (Exception) { DeferMaterialRecovery("材料领取回执已收到，本机原记录尚未清理，请恢复原请求"); return false; }
            _pendingMaterialReward = null;
            _pendingMaterialRecord = null;
            _materialRecoveryAtMs = 0;
            return true;
        }

        private void DeferMaterialRecovery(string reason)
        {
            int epoch = _epoch;
            ulong playerId = _net.PlayerId;
            _materialRecoveryAtMs = _nowMs() + 2000 * (1L << Math.Min(_materialRecoveryAttempts, 2));
            MaterialFailure(reason);
            SendDeferredReads(epoch, playerId);
        }

        private void MaterialFailure(string reason)
        {
            MaterialRewardError = reason;
            OnChanged?.Invoke();
        }

        private bool CanSendMaterialReward() => !_materialLastSendMs.HasValue || _nowMs() < _materialLastSendMs.Value
            || _nowMs() - _materialLastSendMs.Value >= MaterialMinSendIntervalMs;

        private void ResetMaterialRewardSession()
        {
            ++_materialReadRequest;
            ++_materialWriteRequest;
            _materialReward = null;
            _pendingMaterialReward = null;
            _pendingMaterialRecord = null;
            _materialRecoveryPlayer = 0;
            _materialRecoveryBlocked = false;
            _materialRecoveryAttempts = 0;
            _materialRecoveryAtMs = 0;
            MaterialRewardLoading = BusyMaterialReward = false;
            MaterialRewardError = string.Empty;
            // 磁盘原意图和发送节奏跨断线/换角保留。
        }
    }
}
