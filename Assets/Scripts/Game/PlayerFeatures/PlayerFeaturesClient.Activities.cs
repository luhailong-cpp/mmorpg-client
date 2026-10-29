using System;
using Google.Protobuf;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.PlayerFeatures
{
    public sealed partial class PlayerFeaturesClient
    {
        private const int ActivityAutomaticRecoveryLimit = 3;
        // 新RPC未单独配置MessageLimiter，沿默认3次/相邻两整秒；同连接所有阈值共用发送节奏。
        public const long ActivityRewardMinSendIntervalMs = 1000;
        private const long ActivityRecoveryMinIntervalMs = 2000;
        private long? _activityLastSendMs;
        private ClaimActivityVitalityRewardRequest _pendingActivityReward;
        private ulong _activityRecoveryPlayer;
        private bool _activityRecoveryBlocked;
        private int _activityRewardRequest;
        private int _activityRecoveryAttempts;
        private long _activityRecoveryAtMs;

        /// <summary>生产装配按服务器来源、角色隔离。缺失或写盘失败时不发资产请求。</summary>
        public DurableRequestFileStore ActivityRewardRecovery { get; set; }
        public bool BusyActivityReward { get; private set; }
        public string ActivityRewardError { get; private set; } = string.Empty;
        public bool HasPendingActivityReward => _pendingActivityReward != null || _activityRecoveryBlocked;
        /// <summary>只有服务端确认原领取已持久后才发布；claimed但尚未durable不算领取成功。</summary>
        public event Action<uint> OnActivityRewardClaimed;

        public void ClaimActivityVitalityReward(uint ruleId, ulong periodId, uint thresholdId, ulong configurationRevision)
        {
            if (!PrepareRequest(out var playerId)) return;
            RestoreActivityReward(playerId);
            if (BusyActivityReward) return;
            if (_activityRecoveryBlocked) { ActivityRewardFailure("领取恢复记录无法读取，请保留记录并联系支持"); return; }
            if (_pendingActivityReward != null)
            {
                // 有未决操作时仅允许查询原意图，不能借另一个宝箱生成新UUID。
                if (_pendingActivityReward.RuleId != ruleId || _pendingActivityReward.PeriodId != periodId
                    || _pendingActivityReward.ThresholdId != thresholdId
                    || _pendingActivityReward.ConfigurationRevision != configurationRevision)
                { ActivityRewardFailure("上次领取结果尚未确认，请刷新活动恢复原请求"); return; }
                RecoverActivityReward();
                return;
            }
            if (BusyMissionAction || BusyEquip || BusySort) { ActivityRewardFailure("其他资产操作正在处理中，请稍候"); return; }
            if (!CanSendActivityReward()) { ActivityRewardFailure("领取操作太快，请稍候重试"); return; }
            var snapshot = Activities?.Vitality;
            PlayerActivityVitalityReward reward = null;
            if (snapshot != null)
                foreach (var entry in snapshot.Rewards)
                    if (entry.ThresholdId == thresholdId) { reward = entry; break; }
            if (snapshot == null || !snapshot.Available || snapshot.RuleId != ruleId || snapshot.PeriodId != periodId
                || snapshot.ConfigurationRevision != configurationRevision || reward == null || !reward.CanClaim)
            { ActivityRewardFailure(reward?.UnavailableReason ?? snapshot?.UnavailableReason ?? "当前奖励不可领取，请刷新活动"); return; }
            if (ActivityRewardRecovery == null) { ActivityRewardFailure("领取恢复存储未就绪，暂不能领取"); return; }

            var request = new ClaimActivityVitalityRewardRequest
            {
                RuleId = ruleId, PeriodId = periodId, ThresholdId = thresholdId,
                ConfigurationRevision = configurationRevision, RequestId = Guid.NewGuid().ToString("D"),
            };
            try { ActivityRewardRecovery.Save(playerId, ActivityRecord(request)); }
            catch (Exception)
            {
                // 同一磁盘的另一实例可能刚写入了待确认槽；重读它，不能覆盖或换UUID。
                _activityRecoveryPlayer = 0;
                RestoreActivityReward(playerId);
                ActivityRewardFailure("无法保存领取恢复记录，本次领取尚未提交；请刷新确认原记录");
                return;
            }
            _pendingActivityReward = request;
            _activityRecoveryAttempts = 0;
            SendActivityReward(playerId);
        }

        /// <summary>重放原请求即查询原领取。设备跨日或配置变化不更换请求身份。</summary>
        public void RecoverActivityReward()
        {
            if (!PrepareRequest(out var playerId)) return;
            RestoreActivityReward(playerId);
            if (_pendingActivityReward == null || _activityRecoveryBlocked || BusyActivityReward) return;
            if (_nowMs() < _activityRecoveryAtMs) return;
            if (!CanSendActivityReward()) return;
            if (BusyMissionAction || BusyEquip || BusySort) return;
            SendActivityReward(playerId);
        }

        /// <summary>由GameClient主循环驱动；最多三次自动查询，之后保留原记录供玩家刷新重试。</summary>
        public void TickActivityRewardRecovery()
        {
            if (_disposed || !_net.IsReady || _net.PlayerId == 0 || BusyActivityReward) return;
            if (!PrepareRequest(out var playerId)) return;
            RestoreActivityReward(playerId);
            if (_pendingActivityReward == null || _activityRecoveryBlocked
                || _activityRecoveryAttempts >= ActivityAutomaticRecoveryLimit || _nowMs() < _activityRecoveryAtMs) return;
            if (!CanSendActivityReward()) return;
            if (BusyMissionAction || BusyEquip || BusySort) return;
            ++_activityRecoveryAttempts;
            SendActivityReward(playerId);
        }

        private void RestoreActivityReward(ulong playerId)
        {
            if (_activityRecoveryPlayer == playerId) return;
            _activityRecoveryPlayer = playerId;
            if (ActivityRewardRecovery == null) return;
            try
            {
                var record = ActivityRewardRecovery.Load(playerId);
                if (record.Length == 0) return;
                var request = ClaimActivityVitalityRewardRequest.Parser.ParseFrom(Convert.FromBase64String(record));
                if (request.RuleId == 0 || request.PeriodId == 0 || request.ThresholdId == 0
                    || request.ConfigurationRevision == 0 || !Guid.TryParseExact(request.RequestId, "D", out var uuid) || uuid == Guid.Empty)
                    throw new InvalidOperationException();
                _pendingActivityReward = request;
                ActivityRewardError = "有待确认的活跃奖励，正在恢复原领取";
                OnChanged?.Invoke();
            }
            catch (Exception)
            {
                _activityRecoveryBlocked = true;
                ActivityRewardError = "领取恢复记录损坏或无法读取，请保留记录并联系支持";
                OnChanged?.Invoke();
            }
        }

        private void SendActivityReward(ulong playerId)
        {
            int epoch = _epoch;
            int requestGeneration = ++_activityRewardRequest;
            var request = _pendingActivityReward.Clone();
            ++_activityRequest;
            ActivitiesLoading = false;
            BusyActivityReward = true;
            ActivityRewardError = string.Empty;
            OnChanged?.Invoke();
            if (!IsCurrent(epoch, playerId) || requestGeneration != _activityRewardRequest) return;
            _activityLastSendMs = _nowMs();
            _net.Call(SceneActivityClientPlayerRpc.ClaimActivityVitalityRewardMessageId, request,
                ClaimActivityVitalityRewardResponse.Parser,
                response =>
                {
                    if (!IsCurrent(epoch, playerId) || requestGeneration != _activityRewardRequest) return;
                    ++_activityRewardRequest; // 同一传输回调即使重复送达，也只收口一次。
                    BusyActivityReward = false;
                    // snapshot表示当前周期，成功收据表示原请求。不能据新周期快照推断旧周期领取结果。
                    if (response.RequestId != request.RequestId)
                    { DeferActivityRecovery("领取结果身份不符，请刷新恢复原请求"); return; }
                    if (response.Snapshot != null && Activities != null) Activities.Vitality = response.Snapshot;
                    // scene合同保证先查原账本，业务拒绝仅发生在提交前；传输/信封错误没有这个保证。
                    if (HasTip(response.ErrorMessage) && !TipFaults.IsFault(response.ErrorMessage.Id) && !response.Claimed)
                    {
                        try { ActivityRewardRecovery.Clear(playerId, ActivityRecord(request)); }
                        catch (Exception) { DeferActivityRecovery("领取被拒绝，本机恢复记录尚未清理，请刷新重试"); return; }
                        _pendingActivityReward = null;
                        ActivityRewardFailure(DescribeTip("无法领取活跃奖励", response.ErrorMessage));
                        if (!IsCurrent(epoch, playerId)) return;
                        RequestActivities();
                        if (IsCurrent(epoch, playerId)) RefreshActivityAssets(epoch, playerId);
                        return;
                    }
                    if (response.Claimed && response.Durable && !HasTip(response.ErrorMessage))
                    {
                        try { ActivityRewardRecovery.Clear(playerId, ActivityRecord(request)); }
                        catch (Exception) { DeferActivityRecovery("奖励已确认领取，本机恢复记录尚未清理，请刷新重试"); return; }
                        _pendingActivityReward = null;
                        _activityRecoveryAtMs = 0;
                        ActivityRewardError = string.Empty;
                        OnChanged?.Invoke();
                        if (!IsCurrent(epoch, playerId)) return;
                        OnActivityRewardClaimed?.Invoke(request.ThresholdId);
                        if (!IsCurrent(epoch, playerId)) return;
                        RefreshActivityAssets(epoch, playerId);
                        if (IsCurrent(epoch, playerId)) RequestActivities();
                        return;
                    }
                    DeferActivityRecovery(HasTip(response.ErrorMessage)
                        ? DescribeTip("领取尚未确认", response.ErrorMessage)
                        : response.Claimed ? "领取正在持久化，请稍候确认" : "领取结果尚未确认，请刷新恢复原请求");
                },
                error =>
                {
                    if (!IsCurrent(epoch, playerId) || requestGeneration != _activityRewardRequest) return;
                    ++_activityRewardRequest;
                    BusyActivityReward = false;
                    DeferActivityRecovery(TryParseEnvelopeTip(error, out uint tip)
                        ? DescribeTip("领取尚未确认", new TipInfoMessage { Id = tip })
                        : "网络异常，领取结果尚未确认；原请求已保留");
                });
        }

        private void DeferActivityRecovery(string message)
        {
            // 2/4/8秒有限自动退避；手动刷新同样尊重最小间隔，不在OnChanged里立即重发。
            _activityRecoveryAtMs = _nowMs() + ActivityRecoveryMinIntervalMs * (1L << Math.Min(_activityRecoveryAttempts, 2));
            ActivityRewardFailure(message);
        }

        private void RefreshActivityAssets(int epoch, ulong playerId)
        {
            uint selected = RequestedBagType;
            // 奖励实际发到人物背包；仓库仍是当前查看包时，不能只刷新仓库而保留旧人物库存。
            // 先使人物库存变成未知，再只读补拉；不切换查看包，也不推算本次发放数量。
            if (selected != CharacterBagType) _bags[CharacterBagType] = null;
            RequestBag(selected);
            if (selected == CharacterBagType || selected != RequestedBagType || !IsCurrent(epoch, playerId)
                || BusySort || BusyEquip || BusyMissionAction || BusyActivityReward || HasPendingActivityReward) return;
            int generation = _bagRequest;
            int equipGeneration = _equipActionRequest;
            int missionGeneration = _missionRequest;
            bool Current() => IsCurrent(epoch, playerId) && generation == _bagRequest
                && equipGeneration == _equipActionRequest
                && missionGeneration == _missionRequest
                && !BusySort && !BusyEquip && !BusyMissionAction && !BusyActivityReward && !HasPendingActivityReward;
            _net.Call(SceneBagClientPlayerRpc.GetBagMessageId,
                new GetBagRequest { BagType = CharacterBagType }, GetBagResponse.Parser,
                response =>
                {
                    if (!Current()) return;
                    if (HasTip(response.ErrorMessage) || !ValidBag(response.Bag, CharacterBagType))
                    { ActivityRewardFailure("人物背包同步失败，请刷新背包"); return; }
                    _bags[CharacterBagType] = response.Bag;
                    OnChanged?.Invoke();
                },
                error =>
                {
                    if (Current()) ActivityRewardFailure("人物背包暂未同步，请刷新背包");
                });
        }

        private void ActivityRewardFailure(string message)
        {
            ActivityRewardError = string.IsNullOrWhiteSpace(message) ? "奖励暂不可领取，请刷新活动" : message;
            OnChanged?.Invoke();
        }

        private static string ActivityRecord(ClaimActivityVitalityRewardRequest request)
            => Convert.ToBase64String(request.ToByteArray());

        private bool CanSendActivityReward()
        {
            if (!_activityLastSendMs.HasValue) return true;
            long elapsed = _nowMs() - _activityLastSendMs.Value;
            return elapsed < 0 || elapsed >= ActivityRewardMinSendIntervalMs;
        }

        private void ResetActivityRewardSession()
        {
            ++_activityRewardRequest;
            _pendingActivityReward = null;
            _activityRecoveryPlayer = 0;
            _activityRecoveryBlocked = false;
            _activityRecoveryAttempts = 0;
            _activityRecoveryAtMs = 0;
            BusyActivityReward = false;
            ActivityRewardError = string.Empty;
            // 磁盘意图跨断线保留，只有原请求的权威终态才清理。
        }
    }
}
