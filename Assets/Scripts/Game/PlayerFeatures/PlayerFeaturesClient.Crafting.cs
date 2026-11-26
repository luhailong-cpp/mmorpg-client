using System;
using Google.Protobuf;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.PlayerFeatures
{
    /// <summary>定向打造与固定词条安装共用服务端预览和持久意图；客户端不计算费用、不预扣资产。</summary>
    public sealed partial class PlayerFeaturesClient
    {
        public const string EquipmentCraftPendingText = "装备打造结果尚未确认，请先恢复原请求";
        private const int CraftAutomaticRecoveryLimit = 3;
        private const long CraftMinSendIntervalMs = 1000;
        private GetEquipmentCraftRulesResponse _craftRules;
        private EquipmentCraftPreview _craftPreview;
        private CommitEquipmentCraftRequest _pendingCraft;
        private string _pendingCraftRecord;
        private ulong _craftRecoveryPlayer;
        private bool _craftRecoveryBlocked;
        private int _craftRulesRequest;
        private int _craftPreviewRequest;
        private int _craftWriteRequest;
        private int _craftRecoveryAttempts;
        private long _craftRecoveryAtMs;
        private long? _craftLastSendMs;

        /// <summary>按服务器来源和角色隔离；没有可持久写入的存储就不发请求。</summary>
        public DurableRequestFileStore EquipmentCraftRecovery { get; set; }
        public GetEquipmentCraftRulesResponse EquipmentCraftRules => _craftRules?.Clone();
        public EquipmentCraftPreview CraftPreview => _craftPreview?.Clone();
        public bool EquipmentCraftRulesLoading { get; private set; }
        public bool EquipmentCraftPreviewLoading { get; private set; }
        public bool BusyEquipmentCraft { get; private set; }
        public bool HasPendingEquipmentCraft => _pendingCraft != null || _craftRecoveryBlocked;
        public string EquipmentCraftError { get; private set; } = string.Empty;
        /// <summary>仅发布已持久的权威成功回执；重复回调不会重复发布。</summary>
        public event Action<CommitEquipmentCraftResponse> OnEquipmentCraftApplied;

        public void RequestEquipmentCraftRules()
        {
            if (!PrepareRequest(out var playerId)) return;
            int epoch = _epoch;
            int generation = ++_craftRulesRequest;
            EquipmentCraftRulesLoading = true;
            EquipmentCraftError = string.Empty;
            OnChanged?.Invoke();
            if (!IsCurrent(epoch, playerId) || generation != _craftRulesRequest) return;
            _net.Call(SceneBagClientPlayerRpc.GetEquipmentCraftRulesMessageId,
                new GetEquipmentCraftRulesRequest(), GetEquipmentCraftRulesResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _craftRulesRequest) return;
                    ++_craftRulesRequest;
                    EquipmentCraftRulesLoading = false;
                    if (HasTip(response.ErrorMessage) || response.RulesFingerprint.Length == 0)
                    { CraftFailure(HasTip(response.ErrorMessage) ? DescribeEquipTip("读取打造规则失败", response.ErrorMessage) : "打造规则不完整，请刷新"); return; }
                    _craftRules = response.Clone();
                    OnChanged?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _craftRulesRequest) return;
                    ++_craftRulesRequest;
                    EquipmentCraftRulesLoading = false;
                    CraftFailure("打造规则暂未同步，请刷新");
                });
        }

        public void PreviewEquipmentCraft(uint configId) => PreviewEquipmentCraft(new EquipmentCraftChoice
        { Operation = EquipmentCraftChoice.Types.Operation.CreateItem, ConfigId = configId });

        public void PreviewEquipAffix(ulong itemId, uint affixSlot, uint affixId) => PreviewEquipmentCraft(new EquipmentCraftChoice
        { Operation = EquipmentCraftChoice.Types.Operation.InstallAffix, ItemId = itemId, AffixSlot = affixSlot, AffixId = affixId });

        public void PreviewReforgeEquipAffix(ulong itemId, uint affixSlot, uint affixId, bool allowBoundMaterials = false) =>
            PreviewEquipmentCraft(new EquipmentCraftChoice
            {
                Operation = EquipmentCraftChoice.Types.Operation.ReforgeAffix,
                ItemId = itemId,
                AffixSlot = affixSlot,
                AffixId = affixId,
                AllowBoundMaterials = allowBoundMaterials,
            });

        public void PreviewPolishEquipAffix(ulong itemId, uint affixSlot, bool useGuaranteeCharm = false,
            bool allowBoundMaterials = false, ulong proxyMasterPlayerId = 0, uint? expectedPolishPityCount = null)
        {
            var choice = new EquipmentCraftChoice
            {
                Operation = EquipmentCraftChoice.Types.Operation.PolishAffix,
                ItemId = itemId,
                AffixSlot = affixSlot,
                UseGuaranteeCharm = useGuaranteeCharm,
                AllowBoundMaterials = allowBoundMaterials,
                ProxyMasterPlayerId = proxyMasterPlayerId,
            };
            if (expectedPolishPityCount.HasValue) choice.ExpectedPolishPityCount = expectedPolishPityCount.Value;
            PreviewEquipmentCraft(choice);
        }

        public void PreviewUpgradeEquipTier(ulong itemId, uint targetConfigId) => PreviewEquipmentCraft(new EquipmentCraftChoice
        {
            Operation = EquipmentCraftChoice.Types.Operation.UpgradeTier,
            ItemId = itemId,
            ConfigId = targetConfigId,
        });

        public void PreviewEquipmentCraft(EquipmentCraftChoice choice)
        {
            if (!PrepareRequest(out var playerId)) return;
            if (HasPendingEquipmentCraft) { CraftFailure(EquipmentCraftPendingText); return; }
            if (HasPendingMaterialReward) { CraftFailure(MaterialRewardPendingText); return; }
            // 换选项即作废上一份预览，包括非法选项；迟回不能重新激活旧选择。
            int generation = ++_craftPreviewRequest;
            _craftPreview = null;
            EquipmentCraftPreviewLoading = false;
            if (!ValidCraftChoice(choice)) { CraftFailure("请选择有效的装备或词条位置"); return; }
            int epoch = _epoch;
            var selected = choice.Clone();
            EquipmentCraftPreviewLoading = true;
            EquipmentCraftError = string.Empty;
            OnChanged?.Invoke();
            if (!IsCurrent(epoch, playerId) || generation != _craftPreviewRequest) return;
            _net.Call(SceneBagClientPlayerRpc.PreviewEquipmentCraftMessageId,
                new PreviewEquipmentCraftRequest { Choice = selected.Clone() }, PreviewEquipmentCraftResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _craftPreviewRequest) return;
                    ++_craftPreviewRequest;
                    EquipmentCraftPreviewLoading = false;
                    var preview = response.Preview;
                    if (HasTip(response.ErrorMessage)) { CraftFailure(DescribeEquipTip("打造预览失败", response.ErrorMessage)); return; }
                    if (preview?.Choice == null || !preview.Choice.Equals(selected) || preview.RulesFingerprint.Length == 0)
                    { CraftFailure("打造预览身份不符，请重新选择"); return; }
                    _craftPreview = preview.Clone();
                    OnChanged?.Invoke();
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _craftPreviewRequest) return;
                    ++_craftPreviewRequest;
                    EquipmentCraftPreviewLoading = false;
                    CraftFailure("打造预览暂未同步，请重试");
                });
        }

        /// <summary>只能提交当前权威预览；水位、摘要、选项原样落盘。未决请求只能通过恢复入口重放。</summary>
        public void CommitEquipmentCraft(EquipmentCraftPreview preview)
        {
            if (!PrepareRequest(out var playerId)) return;
            if (HasPendingEquipmentCraft) { CraftFailure(EquipmentCraftPendingText); return; }
            if (HasPendingMaterialReward) { CraftFailure(MaterialRewardPendingText); return; }
            RestoreActivityReward(playerId);
            if (CraftOtherWriteBusy || HasPendingActivityReward)
            { CraftFailure("其他资产操作正在处理中，请稍候"); return; }
            if (preview == null || _craftPreview == null || !_craftPreview.Equals(preview)
                || !preview.Available || preview.Result == null
                || (preview.Material == null && preview.Choice?.Operation != EquipmentCraftChoice.Types.Operation.UpgradeTier))
            { CraftFailure("当前预览不可提交，请重新预览"); return; }
            if (!CanSendCraft()) { CraftFailure("打造操作太快，请稍候重试"); return; }
            if (EquipmentCraftRecovery == null) { CraftFailure("打造恢复存储未就绪，暂不能提交"); return; }
            if (!ClientRequestKeys.TryCreate(out ulong key)) { CraftFailure(ClientRequestKeys.UnavailableText); return; }
            var request = new CommitEquipmentCraftRequest
            {
                Choice = preview.Choice.Clone(), RulesFingerprint = preview.RulesFingerprint,
                ExpectedForgeRevision = preview.ExpectedForgeRevision, ClientRequestId = key,
            };
            string record = Convert.ToBase64String(request.ToByteArray());
            try { EquipmentCraftRecovery.Save(playerId, record); }
            catch (Exception)
            {
                _craftRecoveryPlayer = 0;
                RestoreEquipmentCraft(playerId);
                CraftFailure("无法保存打造恢复记录，本次尚未提交；请保留并恢复原记录");
                return;
            }
            _pendingCraft = request;
            _pendingCraftRecord = record;
            _craftRecoveryAttempts = 0;
            SendEquipmentCraft(playerId);
        }

        public void RecoverEquipmentCraft()
        {
            if (!PrepareRequest(out var playerId) || !CanRecoverCraft()) return;
            SendEquipmentCraft(playerId);
        }

        /// <summary>最多三次自动恢复，随后保留原意图供显式恢复；未知水位绝不自动换键补做。</summary>
        public void TickEquipmentCraftRecovery()
        {
            // 先识别连接/角色代次；不能让旧连接的Busy挡住新连接恢复。
            if (_disposed || !_net.IsReady || _net.PlayerId == 0) return;
            if (!PrepareRequest(out var playerId) || _craftRecoveryAttempts >= CraftAutomaticRecoveryLimit || !CanRecoverCraft()) return;
            ++_craftRecoveryAttempts;
            SendEquipmentCraft(playerId);
        }

        private bool CraftOtherWriteBusy => BusyEquip || BusySort || BusyTemporary || BusyMissionAction || BusyActivityReward || BusyMaterialReward;
        private bool CanRecoverCraft() => _pendingCraft != null && !_craftRecoveryBlocked && !BusyEquipmentCraft
            && !CraftOtherWriteBusy && _nowMs() >= _craftRecoveryAtMs && CanSendCraft();

        private void RestoreEquipmentCraft(ulong playerId)
        {
            if (_craftRecoveryPlayer == playerId || EquipmentCraftRecovery == null) return;
            _craftRecoveryPlayer = playerId;
            try
            {
                string record = EquipmentCraftRecovery.Load(playerId);
                if (record.Length == 0) return;
                var request = CommitEquipmentCraftRequest.Parser.ParseFrom(Convert.FromBase64String(record));
                if (request.ClientRequestId == 0 || request.RulesFingerprint.Length == 0 || !ValidCraftChoice(request.Choice))
                    throw new InvalidOperationException();
                _pendingCraft = request;
                _pendingCraftRecord = record;
                EquipmentCraftError = EquipmentCraftPendingText;
            }
            catch (Exception)
            {
                _craftRecoveryBlocked = true;
                EquipmentCraftError = "打造恢复记录损坏或无法读取，请保留记录并联系支持";
            }
        }

        private void SendEquipmentCraft(ulong playerId)
        {
            int epoch = _epoch;
            int generation = ++_craftWriteRequest;
            var request = _pendingCraft.Clone();
            string record = _pendingCraftRecord;
            ++_craftPreviewRequest;
            ++_craftRulesRequest;
            _craftPreview = null;
            _craftRules = null;
            EquipmentCraftPreviewLoading = EquipmentCraftRulesLoading = false;
            BusyEquipmentCraft = true;
            EquipmentCraftError = string.Empty;
            OnChanged?.Invoke();
            if (!IsCurrent(epoch, playerId) || generation != _craftWriteRequest) return;
            _craftLastSendMs = _nowMs();
            _net.Call(SceneBagClientPlayerRpc.CommitEquipmentCraftMessageId, request, CommitEquipmentCraftResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _craftWriteRequest) return;
                    ++_craftWriteRequest;
                    BusyEquipmentCraft = false;
                    if (response.ClientRequestId != request.ClientRequestId)
                    { DeferCraftRecovery("打造回执身份不符，原请求已保留"); return; }
                    ++_craftRulesRequest;
                    _craftRules = null;
                    EquipmentCraftRulesLoading = false;
                    // 只相信显式提交前拒绝，UNKNOWN即使带过期业务码也不能清除原意图。
                    bool rejected = response.Outcome == CommitEquipmentCraftResponse.Types.Outcome.Rejected;
                    bool applied = response.Outcome == CommitEquipmentCraftResponse.Types.Outcome.Applied
                        && !HasTip(response.ErrorMessage) && response.ItemId != 0
                        && (request.Choice.Operation == EquipmentCraftChoice.Types.Operation.CreateItem || response.ItemId == request.Choice.ItemId)
                        && request.ExpectedForgeRevision != ulong.MaxValue
                        && response.ResultForgeRevision == request.ExpectedForgeRevision + 1
                        && response.CurrentForgeRevision >= response.ResultForgeRevision;
                    if (rejected)
                    {
                        if (!ClearCraftRecord(playerId, record)) return;
                        CraftFailure(HasTip(response.ErrorMessage) ? DescribeEquipTip("打造被拒绝", response.ErrorMessage) : "打造被拒绝，请重新预览");
                        SendDeferredReads(epoch, playerId);
                        return;
                    }
                    if (applied && ValidBag(response.Bag, CharacterBagType) && ValidBag(response.Equipment, EquipmentBagType))
                    {
                        // 权威快照可先显示；只有durable才能清意图、发布成功或允许下一笔资产操作。
                        ++_equipmentRequest;
                        EquipmentLoading = false;
                        if (ViewingEquipAffectedBag) { ++_bagRequest; BagLoading = false; BagError = string.Empty; }
                        _bags[CharacterBagType] = response.Bag;
                        _bags[EquipmentBagType] = response.Equipment;
                        _equipActionError = _equipmentReadError = string.Empty;
                        if (response.Durable)
                        {
                            if (!ClearCraftRecord(playerId, record)) return;
                            EquipmentCraftError = string.Empty;
                            OnChanged?.Invoke();
                            if (IsCurrent(epoch, playerId) && generation + 1 == _craftWriteRequest)
                                OnEquipmentCraftApplied?.Invoke(response.Clone());
                            SendDeferredReads(epoch, playerId);
                            return;
                        }
                    }
                    DeferCraftRecovery(applied ? "打造已受理，正在确认持久化；原请求已保留"
                        : "打造结果尚未确认；原请求已保留，请恢复原请求");
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _craftWriteRequest) return;
                    ++_craftWriteRequest;
                    BusyEquipmentCraft = false;
                    DeferCraftRecovery("网络异常，打造结果尚未确认；原请求已保留");
                });
        }

        private bool ClearCraftRecord(ulong playerId, string record)
        {
            try { EquipmentCraftRecovery.Clear(playerId, record); }
            catch (Exception) { DeferCraftRecovery("打造回执已收到，本机恢复记录尚未清理，请恢复原请求"); return false; }
            _pendingCraft = null;
            _pendingCraftRecord = null;
            _craftRecoveryAtMs = 0;
            return true;
        }

        private void DeferCraftRecovery(string message)
        {
            int epoch = _epoch;
            ulong playerId = _net.PlayerId;
            _craftRecoveryAtMs = _nowMs() + 2000 * (1L << Math.Min(_craftRecoveryAttempts, 2));
            CraftFailure(message);
            SendDeferredReads(epoch, playerId);
        }

        private void CraftFailure(string message)
        {
            EquipmentCraftError = message;
            OnChanged?.Invoke();
        }

        private bool CanSendCraft() => !_craftLastSendMs.HasValue || _nowMs() < _craftLastSendMs.Value
            || _nowMs() - _craftLastSendMs.Value >= CraftMinSendIntervalMs;

        internal static bool ValidCraftChoice(EquipmentCraftChoice choice)
        {
            if (choice == null) return false;
            return choice.Operation switch
            {
                EquipmentCraftChoice.Types.Operation.CreateItem =>
                    choice.ConfigId != 0 && choice.ItemId == 0 && choice.AffixSlot == 0 && choice.AffixId == 0
                    && !choice.UseGuaranteeCharm && !choice.HasExpectedPolishPityCount,
                EquipmentCraftChoice.Types.Operation.InstallAffix =>
                    choice.ConfigId == 0 && choice.ItemId != 0 && choice.AffixId != 0
                    && !choice.UseGuaranteeCharm && !choice.HasExpectedPolishPityCount,
                EquipmentCraftChoice.Types.Operation.ReforgeAffix =>
                    choice.ConfigId == 0 && choice.ItemId != 0 && choice.AffixId != 0
                    && !choice.UseGuaranteeCharm && choice.ProxyMasterPlayerId == 0 && !choice.HasExpectedPolishPityCount,
                EquipmentCraftChoice.Types.Operation.PolishAffix =>
                    choice.ConfigId == 0 && choice.ItemId != 0 && choice.AffixId == 0 && choice.SourceItemId == 0,
                EquipmentCraftChoice.Types.Operation.UnsealToTradable =>
                    choice.ConfigId == 0 && choice.ItemId != 0 && choice.SourceItemId == 0 && choice.AffixSlot == 0
                    && choice.AffixId == 0 && !choice.UseGuaranteeCharm && choice.ProxyMasterPlayerId == 0
                    && !choice.HasExpectedPolishPityCount,
                EquipmentCraftChoice.Types.Operation.InheritEnhance =>
                    choice.ConfigId == 0 && choice.ItemId != 0 && choice.SourceItemId != 0
                    && choice.SourceItemId != choice.ItemId && choice.AffixSlot == 0 && choice.AffixId == 0
                    && !choice.UseGuaranteeCharm && choice.ProxyMasterPlayerId == 0
                    && !choice.HasExpectedPolishPityCount,
                EquipmentCraftChoice.Types.Operation.UpgradeTier =>
                    choice.ConfigId != 0 && choice.ItemId != 0 && choice.SourceItemId == 0 && choice.AffixSlot == 0
                    && choice.AffixId == 0 && !choice.UseGuaranteeCharm && choice.ProxyMasterPlayerId == 0
                    && !choice.HasExpectedPolishPityCount,
                _ => false,
            };
        }

        private void ResetEquipmentCraftSession()
        {
            ++_craftRulesRequest;
            ++_craftPreviewRequest;
            ++_craftWriteRequest;
            _craftRules = null;
            _craftPreview = null;
            _pendingCraft = null;
            _pendingCraftRecord = null;
            _craftRecoveryPlayer = 0;
            _craftRecoveryBlocked = false;
            _craftRecoveryAttempts = 0;
            _craftRecoveryAtMs = 0;
            BusyEquipmentCraft = EquipmentCraftRulesLoading = EquipmentCraftPreviewLoading = false;
            EquipmentCraftError = string.Empty;
            // 会话清理不碰磁盘；重连后先恢复同一角色的原请求。
        }
    }
}
