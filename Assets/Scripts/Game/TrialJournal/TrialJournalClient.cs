using System;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.TrialJournal
{
    /// <summary>五行试炼志 V2 消息号契约（proto/scene/player_trial_journal.proto，默认取生成常量 456–458）。</summary>
    public readonly struct TrialJournalMessageIds
    {
        public uint Get { get; }
        public uint ClaimMilestone { get; }
        public uint RedeemVoucher { get; }

        public bool IsSupported => Get != 0 && ClaimMilestone != 0 && RedeemVoucher != 0;

        public TrialJournalMessageIds(uint get, uint claimMilestone, uint redeemVoucher)
        {
            Get = get;
            ClaimMilestone = claimMilestone;
            RedeemVoucher = redeemVoucher;
        }

        public static TrialJournalMessageIds Generated => new TrialJournalMessageIds(
            SceneTrialJournalClientPlayerRpc.GetTrialJournalMessageId,
            SceneTrialJournalClientPlayerRpc.ClaimTrialJournalMilestoneMessageId,
            SceneTrialJournalClientPlayerRpc.RedeemBoundArtifactVoucherMessageId);
    }

    /// <summary>里程碑领取成功回执摘要（不可变值对象，跨模块事件传递安全）。</summary>
    public readonly struct TrialMilestoneClaimedEvent
    {
        public uint MilestoneId { get; }
        public bool Replayed { get; }
        public uint GrantedSandCount { get; }
        public uint GrantedVoucherCount { get; }
        public string GrantedAppearanceId { get; }

        public TrialMilestoneClaimedEvent(
            uint milestoneId,
            bool replayed,
            uint grantedSandCount,
            uint grantedVoucherCount,
            string grantedAppearanceId)
        {
            MilestoneId = milestoneId;
            Replayed = replayed;
            GrantedSandCount = grantedSandCount;
            GrantedVoucherCount = grantedVoucherCount;
            GrantedAppearanceId = grantedAppearanceId ?? "";
        }
    }

    /// <summary>自用法宝拓印兑换成功回执摘要（不可变值对象）。</summary>
    public readonly struct TrialArtifactRedeemedEvent
    {
        public ulong CreatedItemId { get; }
        public uint ArtifactItemConfigId { get; }
        public bool Replayed { get; }

        public TrialArtifactRedeemedEvent(ulong createdItemId, uint artifactItemConfigId, bool replayed)
        {
            CreatedItemId = createdItemId;
            ArtifactItemConfigId = artifactItemConfigId;
            Replayed = replayed;
        }
    }

    /// <summary>
    /// 五行试炼志 V2 客户端网络协调器（纯 C#，WebGL2 单线程安全，IL2CPP 安全，EditMode 可直接单测）。
    /// 负责拉取权威快照、按 StateRevision 单调更新、幂等领取里程碑与自用法宝拓印兑换，以及断线/切角复位。
    /// UI 层仅订阅事件与读取只读 <see cref="Snapshot"/>，不直接发包或推算奖励。
    /// </summary>
    public sealed class TrialJournalClient : IDisposable
    {
        public const float MinSnapshotIntervalSeconds = 2.0f;
        public const float MinActionIntervalSeconds = 0.8f;

        public const string UnsupportedMessage = "当前服务器暂未开放五行试炼志。";
        public const string NotConnectedMessage = "尚未连接到游戏。";
        public const string SnapshotUnavailableMessage = "暂时无法获取五行试炼志数据。";
        public const string ClaimPendingMessage = "正在领取试炼志里程碑奖励…";
        public const string ClaimedMessage = "已领取试炼志里程碑奖励。";
        public const string RedeemPendingMessage = "正在兑换自用法宝…";
        public const string RedeemedMessage = "已成功兑换自用法宝（已放入人物背包）。";
        public const string TooFastMessage = "操作太快了，请稍候再试。";
        public const string RecoveryMessage = "网络异常，操作结果未知。请重新登录后查看。";

        private readonly IBattleTransport _net;
        private readonly TrialJournalMessageIds _messageIds;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<float> _clock;

        private object _observedConnection;
        private ulong _observedPlayerId;
        private bool _disposed;
        private int _generation;
        private float _nextSnapshotAt = float.NegativeInfinity;
        private float _nextActionAt = float.NegativeInfinity;
        private bool _snapshotQueued;

        public event Action Changed;
        public event Action<string> Notice;
        public event Action<TrialMilestoneClaimedEvent> MilestoneClaimed;
        public event Action<TrialArtifactRedeemedEvent> ArtifactRedeemed;

        public global::TrialJournalSnapshot Snapshot { get; private set; }
        public bool SnapshotPending { get; private set; }
        public bool ActionPending { get; private set; }
        public bool RequiresReconnect { get; private set; }
        public string Status { get; private set; } = "";
        public bool StatusIsError { get; private set; }

        public bool IsSupported => _messageIds.IsSupported;
        public bool Ready => IsSupported && !_disposed && _net.IsReady && _net.PlayerId != 0;
        public int ClaimableMilestoneCount => TrialJournalModels.CountClaimableMilestones(Snapshot);

        public TrialJournalClient(
            IBattleTransport transport,
            Func<object> connectionIdentity,
            Func<float> clock,
            TrialJournalMessageIds? messageIds = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _connectionIdentity = connectionIdentity ?? throw new ArgumentNullException(nameof(connectionIdentity));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _messageIds = messageIds ?? TrialJournalMessageIds.Generated;
            _observedConnection = _connectionIdentity();
            _observedPlayerId = _net.PlayerId;
            _net.Disconnected += HandleDisconnected;
            if (!IsSupported) SetStatus(UnsupportedMessage, false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _net.Disconnected -= HandleDisconnected;
            _disposed = true;
            ++_generation;
            SnapshotPending = false;
            ActionPending = false;
        }

        public void RequestSnapshot(bool force = false)
        {
            if (_disposed) return;
            if (!IsSupported) { SetStatus(UnsupportedMessage, false); return; }
            ObserveConnection();
            if (!Ready || SnapshotPending) return;
            float now = _clock();
            if (!force && now < _nextSnapshotAt)
            {
                _snapshotQueued = true;
                return;
            }
            SendGetSnapshot(now);
        }

        public bool ClaimMilestone(uint milestoneId)
        {
            if (!PrepareWriteAction(milestoneId != 0,
                TrialJournalModels.DescribeTip((uint)trial_journal_error.KTrialJournalMilestoneNotFound),
                out ulong requestKey, out float now))
            {
                return false;
            }
            _nextActionAt = now + MinActionIntervalSeconds;
            ActionPending = true;
            int generation = _generation;
            SetStatus(ClaimPendingMessage, false);
            Changed?.Invoke();

            var request = new global::ClaimTrialJournalMilestoneRequest
            {
                ClientRequestId = requestKey,
                MilestoneId = milestoneId,
            };
            int attempt = 0;
            bool resent = false;
            void Send()
            {
                int mine = ++attempt;
                _net.Call(_messageIds.ClaimMilestone, request, global::ClaimTrialJournalMilestoneResponse.Parser,
                    response =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        ActionPending = false;
                        if (response.Snapshot != null) ApplySnapshot(response.Snapshot);
                        uint tipId = response.ErrorMessage?.Id ?? 0;
                        if (tipId != 0)
                        {
                            HandleWriteTip("ClaimTrialJournalMilestone", tipId);
                            return;
                        }
                        SetStatus(ClaimedMessage, false);
                        Notice?.Invoke(ClaimedMessage);
                        MilestoneClaimed?.Invoke(new TrialMilestoneClaimedEvent(
                            milestoneId,
                            response.Replayed,
                            response.GrantedSandCount,
                            response.GrantedVoucherCount,
                            response.GrantedAppearanceId));
                        Changed?.Invoke();
                    },
                    error =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        if (!resent && ClientRequestKeys.ShouldResendOnce(error))
                        {
                            resent = true;
                            Send();
                            return;
                        }
                        HandleWriteTransportError(error, resent);
                    });
            }
            Send();
            return true;
        }

        public bool RedeemArtifactVoucher(uint artifactItemConfigId)
        {
            if (!PrepareWriteAction(artifactItemConfigId != 0,
                TrialJournalModels.DescribeTip((uint)trial_journal_error.KTrialJournalArtifactInvalid),
                out ulong requestKey, out float now))
            {
                return false;
            }
            _nextActionAt = now + MinActionIntervalSeconds;
            ActionPending = true;
            int generation = _generation;
            SetStatus(RedeemPendingMessage, false);
            Changed?.Invoke();

            var request = new global::RedeemBoundArtifactVoucherRequest
            {
                ClientRequestId = requestKey,
                ArtifactItemConfigId = artifactItemConfigId,
            };
            int attempt = 0;
            bool resent = false;
            void Send()
            {
                int mine = ++attempt;
                _net.Call(_messageIds.RedeemVoucher, request, global::RedeemBoundArtifactVoucherResponse.Parser,
                    response =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        ActionPending = false;
                        if (response.Snapshot != null) ApplySnapshot(response.Snapshot);
                        uint tipId = response.ErrorMessage?.Id ?? 0;
                        if (tipId != 0)
                        {
                            HandleWriteTip("RedeemBoundArtifactVoucher", tipId);
                            return;
                        }
                        SetStatus(RedeemedMessage, false);
                        Notice?.Invoke(RedeemedMessage);
                        ArtifactRedeemed?.Invoke(new TrialArtifactRedeemedEvent(
                            response.CreatedItemId,
                            response.ArtifactItemConfigId,
                            response.Replayed));
                        Changed?.Invoke();
                    },
                    error =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        if (!resent && ClientRequestKeys.ShouldResendOnce(error))
                        {
                            resent = true;
                            Send();
                            return;
                        }
                        HandleWriteTransportError(error, resent);
                    });
            }
            Send();
            return true;
        }

        public void HandleSceneEntered()
        {
            if (_disposed) return;
            ObserveConnection();
            if (Snapshot == null && !SnapshotPending) RequestSnapshot();
        }

        /// <summary>战斗结算落盘并回到场景后调用，强制拉取最新印记/纪录与里程碑状态。</summary>
        public void HandleBattleSettled()
        {
            if (_disposed) return;
            ObserveConnection();
            if (Ready) RequestSnapshot(force: true);
        }

        public void HandleDisconnected()
        {
            if (_disposed) return;
            Reset();
            Changed?.Invoke();
        }

        public void Tick()
        {
            if (_disposed || !IsSupported) return;
            ObserveConnection();
            float now = _clock();
            if (_snapshotQueued && Ready && !SnapshotPending && now >= _nextSnapshotAt)
                SendGetSnapshot(now);
        }

        private void SendGetSnapshot(float now)
        {
            _snapshotQueued = false;
            SnapshotPending = true;
            _nextSnapshotAt = now + MinSnapshotIntervalSeconds;
            int generation = _generation;
            Changed?.Invoke();

            _net.Call(_messageIds.Get, new global::GetTrialJournalRequest(), global::GetTrialJournalResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    SnapshotPending = false;
                    uint tipId = response.ErrorMessage?.Id ?? 0;
                    if (tipId != 0 || response.Snapshot == null)
                    {
                        SetInfoStatus(tipId != 0 ? TrialJournalModels.DescribeTip(tipId) : SnapshotUnavailableMessage, true);
                    }
                    else
                    {
                        ApplySnapshot(response.Snapshot);
                        if (Status == SnapshotUnavailableMessage) SetInfoStatus("", false);
                    }
                    Changed?.Invoke();
                },
                _ =>
                {
                    if (!IsCurrent(generation)) return;
                    SnapshotPending = false;
                    SetInfoStatus(SnapshotUnavailableMessage, true);
                    Changed?.Invoke();
                });
        }

        private bool PrepareWriteAction(bool validParam, string invalidMessage, out ulong requestKey, out float now)
        {
            requestKey = 0;
            now = 0;
            if (_disposed) return false;
            ObserveConnection();
            if (!IsSupported) return RejectLocally(UnsupportedMessage);
            if (!Ready) return RejectLocally(NotConnectedMessage);
            if (RequiresReconnect) return RejectLocally(RecoveryMessage);
            if (ActionPending) return false;
            if (!validParam) return RejectLocally(invalidMessage);
            now = _clock();
            if (now < _nextActionAt) return RejectLocally(TooFastMessage);
            if (!ClientRequestKeys.TryCreate(out requestKey)) return RejectLocally(ClientRequestKeys.UnavailableText);
            return true;
        }

        private void HandleWriteTip(string opName, uint tipId)
        {
            if (tipId == (uint)common_error.KInvalidParameter) ClientRequestKeys.ReportKeyReused(opName);
            if (tipId == (uint)common_error.KRateLimitExceeded) _nextActionAt = _clock() + MinSnapshotIntervalSeconds;
            SetStatus(TrialJournalModels.DescribeTip(tipId), true);
            if (tipId == (uint)trial_journal_error.KTrialJournalMilestoneAlreadyClaimed
                || tipId == (uint)trial_journal_error.KTrialJournalMilestoneNotReached
                || tipId == (uint)trial_journal_error.KTrialJournalVoucherNotEnough)
            {
                RequestSnapshot(force: true);
            }
            Changed?.Invoke();
        }

        private void HandleWriteTransportError(string error, bool resent)
        {
            ActionPending = false;
            uint tipId = RpcStatus.TryParseLegacyServerTip(error, out uint parsed) ? parsed : 0;
            if (!resent && tipId != 0 && tipId != (uint)common_error.KServiceUnavailable)
            {
                if (tipId == (uint)common_error.KRateLimitExceeded) _nextActionAt = _clock() + MinSnapshotIntervalSeconds;
                SetStatus(TrialJournalModels.DescribeTip(tipId), true);
            }
            else
            {
                RequiresReconnect = true;
                SetStatus(RecoveryMessage, true);
            }
            Changed?.Invoke();
        }

        private void ApplySnapshot(global::TrialJournalSnapshot incoming)
        {
            if (incoming == null) return;
            // 单调修订保护：乱序抵达的旧快照绝不覆写已拿到的更新状态。
            if (Snapshot != null && incoming.StateRevision < Snapshot.StateRevision) return;
            Snapshot = incoming;
        }

        private void ObserveConnection()
        {
            var current = _connectionIdentity();
            ulong currentPlayerId = _net.PlayerId;
            if (ReferenceEquals(current, _observedConnection) && currentPlayerId == _observedPlayerId) return;
            _observedConnection = current;
            _observedPlayerId = currentPlayerId;
            Reset();
        }

        private void Reset()
        {
            ++_generation;
            Snapshot = null;
            SnapshotPending = false;
            ActionPending = false;
            RequiresReconnect = false;
            _snapshotQueued = false;
            SetStatus(IsSupported ? "" : UnsupportedMessage, false);
        }

        private bool IsCurrent(int generation)
        {
            if (_disposed) return false;
            ObserveConnection();
            return generation == _generation;
        }

        private bool RejectLocally(string message)
        {
            SetStatus(message, true);
            Changed?.Invoke();
            return false;
        }

        private void SetInfoStatus(string message, bool isError)
        {
            if (RequiresReconnect) return;
            SetStatus(message, isError);
        }

        private void SetStatus(string message, bool isError)
        {
            Status = message ?? "";
            StatusIsError = isError && Status.Length != 0;
        }
    }
}
