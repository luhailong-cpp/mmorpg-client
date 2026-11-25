using System;
using System.Globalization;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.FestivalBeast
{
    /// <summary>瑞兽迎春发起挑战结果事件（只读不可变）。</summary>
    public readonly struct FestivalBeastLaunchEvent
    {
        public string ClientRequestId { get; }
        public global::FestivalBeastLaunchState LaunchState { get; }
        public ulong BattleId { get; }
        public ulong LockedPetId { get; }
        public uint MatchedTierId { get; }
        public ulong AdmittedAtMs { get; }
        public bool Replayed { get; }

        public string BattleIdText => BattleId.ToString(CultureInfo.InvariantCulture);
        public string LockedPetIdText => LockedPetId.ToString(CultureInfo.InvariantCulture);

        public FestivalBeastLaunchEvent(
            string clientRequestId,
            global::FestivalBeastLaunchState launchState,
            ulong battleId,
            ulong lockedPetId,
            uint matchedTierId,
            ulong admittedAtMs,
            bool replayed)
        {
            ClientRequestId = clientRequestId ?? string.Empty;
            LaunchState = launchState;
            BattleId = battleId;
            LockedPetId = lockedPetId;
            MatchedTierId = matchedTierId;
            AdmittedAtMs = admittedAtMs;
            Replayed = replayed;
        }
    }

    /// <summary>瑞兽迎春神兽领奖确认回执（仅在 durable=true 时发布）。</summary>
    public readonly struct FestivalBeastRewardClaimedEvent
    {
        public string ClientRequestId { get; }
        public string RewardIdentity { get; }
        public uint RewardDefinitionVersion { get; }
        public uint RewardPetTableId { get; }
        public ulong PetId { get; }
        public ulong ClaimedAtMs { get; }
        public bool Replayed { get; }
        public bool Durable { get; }

        public string PetIdText => PetId.ToString(CultureInfo.InvariantCulture);

        public FestivalBeastRewardClaimedEvent(
            string clientRequestId,
            string rewardIdentity,
            uint rewardDefinitionVersion,
            uint rewardPetTableId,
            ulong petId,
            ulong claimedAtMs,
            bool replayed,
            bool durable)
        {
            ClientRequestId = clientRequestId ?? string.Empty;
            RewardIdentity = rewardIdentity ?? string.Empty;
            RewardDefinitionVersion = rewardDefinitionVersion;
            RewardPetTableId = rewardPetTableId;
            PetId = petId;
            ClaimedAtMs = claimedAtMs;
            Replayed = replayed;
            Durable = durable;
        }
    }

    /// <summary>
    /// 瑞兽迎春非视觉客户端数据适配与恢复协调器（纯 C#，零 UnityEngine 依赖，WebGL2 单线程与 IL2CPP 安全）。
    /// 负责：
    ///   1. 拉取权威详情（GetFestivalBeastDetail，RPC 476），暴露只读快照与 UTC+8 时间窗；
    ///   2. 幂等发起挑战（StartFestivalBeastChallenge，RPC 477）与在途恢复，超时/未知复用原 UUID；
    ///   3. 幂等领取永久待领神兽（ClaimFestivalBeastReward，RPC 478），落盘确认 durable=true 才公开成功；
    ///   4. 跨设备无本地 UUID 时直接通过详情响应恢复在途开局、领奖确认态与已领 pet_id；
    ///   5. 提取战斗内首领重击预告/兑现/取消状态与战后/补查结算摘要并自动刷新详情。
    /// UI 层仅订阅事件与读取只读属性，不自行计算资格、胜场或奖励。
    /// </summary>
    public sealed class FestivalBeastClient : IDisposable
    {
        public const long MinDetailIntervalMs = 1500;
        public const long MinWriteIntervalMs = 800;
        public const long RecoveryBaseBackoffMs = 2000;
        public const int MaxAutoRecoveryAttempts = 3;

        public static FestivalBeastClient Instance { get; private set; }

        private static readonly System.Diagnostics.Stopwatch FallbackClock = System.Diagnostics.Stopwatch.StartNew();

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<long> _nowMs;

        private object _observedConnection;
        private ulong _observedPlayerId;
        private int _epoch;
        private bool _disposed;

        private bool _detailInFlight;
        private bool _detailQueued;
        private long _nextDetailAtMs = long.MinValue;
        private int _detailGeneration;

        private bool _launchInFlight;
        private int _launchGeneration;
        private long _nextLaunchAtMs = long.MinValue;
        private global::StartFestivalBeastChallengeRequest _pendingLaunchRequest;
        private bool _launchRecoveryBlocked;
        private int _launchRecoveryAttempts;
        private long _nextLaunchRecoveryAtMs;

        private bool _claimInFlight;
        private int _claimGeneration;
        private long _nextClaimAtMs = long.MinValue;
        private global::ClaimFestivalBeastRewardRequest _pendingClaimRequest;
        private bool _claimRecoveryBlocked;
        private int _claimRecoveryAttempts;
        private long _nextClaimRecoveryAtMs;

        private ulong _hydratedPlayerId;

        /// <summary>可选：本地磁盘开局恢复存储（按服务器来源与角色归属修订号隔离）。</summary>
        public DurableRequestFileStore LaunchRecoveryStore { get; set; }

        /// <summary>可选：本地磁盘领奖恢复存储（按服务器来源与角色归属修订号隔离）。</summary>
        public DurableRequestFileStore ClaimRecoveryStore { get; set; }

        public FestivalBeastDetailSnapshot Detail { get; private set; }
        public FestivalBeastTelegraphSnapshot ActiveTelegraph { get; private set; }
        public FestivalBeastTelegraphSnapshot LastResolvedTelegraph { get; private set; }
        public FestivalBeastSettlementSnapshot LastSettlement { get; private set; }

        public bool DetailLoading => _detailInFlight;
        public bool LaunchBusy => _launchInFlight;
        public bool ClaimBusy => _claimInFlight;
        public bool HasPendingLaunch => _pendingLaunchRequest != null || _launchRecoveryBlocked;
        public bool HasPendingClaim => _pendingClaimRequest != null || _claimRecoveryBlocked;
        public string PendingLaunchRequestId => _pendingLaunchRequest?.ClientRequestId ?? string.Empty;
        public string PendingClaimRequestId => _pendingClaimRequest?.ClientRequestId ?? string.Empty;

        public string StatusMessage { get; private set; } = string.Empty;
        public bool StatusIsError { get; private set; }

        public bool Ready => !_disposed && _net.IsReady && _net.PlayerId != 0;

        public event Action OnChanged;
        public event Action OnTelegraphChanged;
        public event Action<FestivalBeastLaunchEvent> OnChallengeAdmitted;
        public event Action<FestivalBeastRewardClaimedEvent> OnRewardClaimed;
        public event Action<FestivalBeastSettlementSnapshot> OnSettlementReceived;

        public FestivalBeastClient(
            IBattleTransport transport,
            Func<object> connectionIdentity,
            Func<long> nowMs = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _connectionIdentity = connectionIdentity ?? throw new ArgumentNullException(nameof(connectionIdentity));
            _nowMs = nowMs ?? new Func<long>(() => FallbackClock.ElapsedMilliseconds);
            _observedConnection = _connectionIdentity();
            _observedPlayerId = _net.PlayerId;
            _net.Disconnected += HandleDisconnected;
        }

        public static FestivalBeastClient Attach(
            IBattleTransport transport,
            Func<object> connectionIdentity,
            Func<long> nowMs = null)
        {
            Instance?.Dispose();
            var client = new FestivalBeastClient(transport, connectionIdentity, nowMs);
            Instance = client;
            return client;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _net.Disconnected -= HandleDisconnected;
            _disposed = true;
            ++_epoch;
            _detailInFlight = false;
            _launchInFlight = false;
            _claimInFlight = false;
            if (ReferenceEquals(Instance, this)) Instance = null;
        }

        /// <summary>按服务端当前时刻锚点估算指定目标时间的剩余毫秒（用于倒计时展示）。</summary>
        public ulong GetRemainingMs(ulong targetUtcMs)
        {
            if (Detail == null) return 0UL;
            return FestivalBeastModels.ComputeRemainingMs(
                targetUtcMs, Detail.ServerNowMs, _nowMs() - Detail.CapturedAtLocalMs);
        }

        /// <summary>拉取权威详情（同时执行跨设备在途开局与领奖状态恢复）。</summary>
        public void RefreshDetail(bool force = false, uint activityInstanceId = 0u)
        {
            if (_disposed) return;
            ObserveSession();
            if (!Ready) return;
            HydrateLocalRecovery(_net.PlayerId);

            long now = _nowMs();
            if (_detailInFlight)
            {
                if (force) _detailQueued = true;
                return;
            }
            if (!force && now < _nextDetailAtMs)
            {
                _detailQueued = true;
                return;
            }

            SendGetDetail(_net.PlayerId, activityInstanceId, now);
        }

        /// <summary>
        /// 发起单人挑战。若已有在途开局（来自本机磁盘或跨设备详情恢复），自动复用原 UUID 恢复，绝不生成第二个 UUID。
        /// </summary>
        public bool StartChallenge()
        {
            if (_disposed) return false;
            ObserveSession();
            if (!Ready) { SetStatus("尚未连接到游戏。", true); return false; }

            ulong playerId = _net.PlayerId;
            HydrateLocalRecovery(playerId);
            if (_launchInFlight || _claimInFlight) return false;
            if (_launchRecoveryBlocked)
            {
                SetStatus("开局恢复记录损坏或无法读取，请刷新详情恢复原请求。", true);
                return false;
            }

            long now = _nowMs();
            if (now < _nextLaunchAtMs)
            {
                SetStatus("操作太快，请稍候重试。", true);
                return false;
            }

            if (_pendingLaunchRequest != null)
            {
                SendStartChallenge(playerId, now);
                return true;
            }

            if (Detail == null || !Detail.CanStartChallenge || Detail.ActivityInstanceId == 0u || Detail.ConfigRevision == 0u)
            {
                SetStatus(Detail != null && !string.IsNullOrEmpty(Detail.StartBlockReason)
                    ? Detail.StartBlockReason
                    : "当前暂不可发起挑战，请刷新活动详情。", true);
                return false;
            }

            var req = new global::StartFestivalBeastChallengeRequest
            {
                ClientRequestId = Guid.NewGuid().ToString("D"),
                ActivityInstanceId = Detail.ActivityInstanceId,
                ConfigRevision = Detail.ConfigRevision,
            };
            if (LaunchRecoveryStore != null)
            {
                try
                {
                    LaunchRecoveryStore.Save(playerId, Encode(req));
                }
                catch (Exception)
                {
                    _hydratedPlayerId = 0;
                    HydrateLocalRecovery(playerId);
                    SetStatus("无法保存挑战恢复记录，本次挑战尚未发起，请刷新重试。", true);
                    return false;
                }
            }
            _pendingLaunchRequest = req;
            _launchRecoveryAttempts = 0;
            SendStartChallenge(playerId, now);
            return true;
        }

        /// <summary>
        /// 领取永久待领神兽奖励。若已有待确认领奖（来自本机磁盘或新设备详情恢复），复用原 UUID 确认直到 durable=true。
        /// </summary>
        public bool ClaimReward()
        {
            if (_disposed) return false;
            ObserveSession();
            if (!Ready) { SetStatus("尚未连接到游戏。", true); return false; }

            ulong playerId = _net.PlayerId;
            HydrateLocalRecovery(playerId);
            if (_claimInFlight || _launchInFlight) return false;
            if (_claimRecoveryBlocked)
            {
                SetStatus("领奖恢复记录损坏或无法读取，请刷新详情恢复原请求。", true);
                return false;
            }

            long now = _nowMs();
            if (now < _nextClaimAtMs)
            {
                SetStatus("操作太快，请稍候重试。", true);
                return false;
            }

            if (_pendingClaimRequest != null)
            {
                SendClaimReward(playerId, now);
                return true;
            }

            if (Detail == null || !Detail.CanClaimReward || string.IsNullOrEmpty(Detail.RewardIdentity))
            {
                SetStatus(Detail != null && !string.IsNullOrEmpty(Detail.ClaimBlockReason)
                    ? Detail.ClaimBlockReason
                    : "当前暂无可领取的神兽奖励，请刷新活动详情。", true);
                return false;
            }

            var req = new global::ClaimFestivalBeastRewardRequest
            {
                ClientRequestId = Guid.NewGuid().ToString("D"),
                RewardIdentity = Detail.RewardIdentity,
            };
            if (ClaimRecoveryStore != null)
            {
                try
                {
                    ClaimRecoveryStore.Save(playerId, Encode(req));
                }
                catch (Exception)
                {
                    _hydratedPlayerId = 0;
                    HydrateLocalRecovery(playerId);
                    SetStatus("无法保存领奖恢复记录，本次领取尚未提交，请刷新重试。", true);
                    return false;
                }
            }
            _pendingClaimRequest = req;
            _claimRecoveryAttempts = 0;
            SendClaimReward(playerId, now);
            return true;
        }

        /// <summary>主循环驱动（单线程安全）：执行排队详情刷新与有限次数的待确认开局/领奖自动恢复。</summary>
        public void Tick()
        {
            if (_disposed) return;
            ObserveSession();
            if (!Ready) return;

            ulong playerId = _net.PlayerId;
            HydrateLocalRecovery(playerId);
            long now = _nowMs();

            if (_detailQueued && !_detailInFlight && now >= _nextDetailAtMs)
            {
                _detailQueued = false;
                SendGetDetail(playerId, 0u, now);
            }

            if (_pendingClaimRequest != null && !_claimRecoveryBlocked && !_claimInFlight && !_launchInFlight
                && _claimRecoveryAttempts < MaxAutoRecoveryAttempts
                && now >= _nextClaimRecoveryAtMs && now >= _nextClaimAtMs)
            {
                ++_claimRecoveryAttempts;
                SendClaimReward(playerId, now);
                return;
            }

            if (_pendingLaunchRequest != null && !_launchRecoveryBlocked && !_launchInFlight && !_claimInFlight
                && _launchRecoveryAttempts < MaxAutoRecoveryAttempts
                && now >= _nextLaunchRecoveryAtMs && now >= _nextLaunchAtMs)
            {
                ++_launchRecoveryAttempts;
                SendStartChallenge(playerId, now);
            }
        }

        /// <summary>接收战斗实时状态/回合结果，提取首领重击预告与兑现/取消状态。</summary>
        public void HandleBattleState(global::BattleStateS2C state, global::TurnResultS2C turnResult = null)
        {
            if (_disposed) return;
            ObserveSession();
            FestivalBeastModels.ExtractTelegraphs(state, turnResult, out var active, out var resolved);
            bool changed = !TelegraphsEqual(ActiveTelegraph, active) || !TelegraphsEqual(LastResolvedTelegraph, resolved);
            ActiveTelegraph = active;
            LastResolvedTelegraph = resolved;
            if (changed) OnTelegraphChanged?.Invoke();
        }

        /// <summary>接收现场 BattleEndS2C，缓存结算摘要并强制刷新活动详情。</summary>
        public void HandleBattleSettled(global::BattleEndS2C battleEnd)
        {
            if (_disposed) return;
            ObserveSession();
            ActiveTelegraph = null;
            var settlement = FestivalBeastModels.ExtractSettlement(battleEnd);
            if (settlement == null) return;
            LastSettlement = settlement;
            ClearLaunchIfBattleFinished(settlement.BattleId);
            OnSettlementReceived?.Invoke(settlement);
            OnChanged?.Invoke();
            RefreshDetail(force: true);
        }

        /// <summary>接收断线补查战果 PlayerBattleResult，缓存结算摘要并强制刷新活动详情。</summary>
        public void HandleRecoveredBattleResult(global::PlayerBattleResult result)
        {
            if (_disposed) return;
            ObserveSession();
            var settlement = FestivalBeastModels.ExtractSettlement(result);
            if (settlement == null) return;
            LastSettlement = settlement;
            ClearLaunchIfBattleFinished(settlement.BattleId);
            OnSettlementReceived?.Invoke(settlement);
            OnChanged?.Invoke();
            RefreshDetail(force: true);
        }

        private void SendGetDetail(ulong playerId, uint activityInstanceId, long now)
        {
            int epoch = _epoch;
            int generation = ++_detailGeneration;
            _detailInFlight = true;
            _nextDetailAtMs = now + MinDetailIntervalMs;
            OnChanged?.Invoke();

            var req = new global::GetFestivalBeastDetailRequest { ActivityInstanceId = activityInstanceId };
            _net.Call(
                SceneActivityClientPlayerRpc.GetFestivalBeastDetailMessageId,
                req,
                global::GetFestivalBeastDetailResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _detailGeneration) return;
                    ++_detailGeneration;
                    _detailInFlight = false;
                    if (HasTip(resp.ErrorMessage))
                    {
                        SetStatus(FestivalBeastModels.DescribeTip(resp.ErrorMessage.Id), true);
                        return;
                    }
                    ApplyDetailSnapshot(playerId, resp);
                },
                err =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _detailGeneration) return;
                    ++_detailGeneration;
                    _detailInFlight = false;
                    SetStatus("暂时无法获取瑞兽迎春详情，请稍后刷新。", true);
                });
        }

        private void ApplyDetailSnapshot(ulong playerId, global::GetFestivalBeastDetailResponse resp)
        {
            var snapshot = new FestivalBeastDetailSnapshot(resp, _nowMs());
            Detail = snapshot;

            // 新设备无本地 UUID 恢复与陈旧本地槽位自愈：以服务端权威详情为准。
            if (snapshot.PendingLaunch != null && snapshot.PendingLaunch.HasInFlight
                && !string.IsNullOrEmpty(snapshot.PendingLaunch.ClientRequestId)
                && snapshot.PendingLaunch.LaunchState != global::FestivalBeastLaunchState.FestivalBeastLaunchAdmitted)
            {
                if (_pendingLaunchRequest == null)
                {
                    _pendingLaunchRequest = new global::StartFestivalBeastChallengeRequest
                    {
                        ClientRequestId = snapshot.PendingLaunch.ClientRequestId,
                        ActivityInstanceId = snapshot.PendingLaunch.ActivityInstanceId,
                        ConfigRevision = snapshot.PendingLaunch.ConfigRevision,
                    };
                }
            }
            else if (_pendingLaunchRequest != null)
            {
                // 服务端确认已无未决开局（或已进入正式战局）时，清理本机残留开局槽位。
                if (snapshot.PendingLaunch == null
                    || snapshot.PendingLaunch.LaunchState == global::FestivalBeastLaunchState.FestivalBeastLaunchAdmitted
                    || snapshot.PendingLaunch.LaunchState == global::FestivalBeastLaunchState.FestivalBeastLaunchRejected)
                {
                    ClearPendingLaunch(playerId);
                }
            }

            if (snapshot.RewardClaimed && snapshot.RewardClaimDurable)
            {
                if (_pendingClaimRequest != null
                    && (string.IsNullOrEmpty(_pendingClaimRequest.RewardIdentity)
                        || _pendingClaimRequest.RewardIdentity == snapshot.RewardIdentity))
                {
                    ClearPendingClaim(playerId);
                }
            }
            else if (snapshot.IsClaimConfirming && !string.IsNullOrEmpty(snapshot.ClaimRequestId))
            {
                if (_pendingClaimRequest == null)
                {
                    _pendingClaimRequest = new global::ClaimFestivalBeastRewardRequest
                    {
                        ClientRequestId = snapshot.ClaimRequestId,
                        RewardIdentity = snapshot.RewardIdentity,
                    };
                    _claimRecoveryAttempts = 0;
                    _nextClaimRecoveryAtMs = _nowMs() + RecoveryBaseBackoffMs;
                }
            }

            StatusMessage = string.Empty;
            StatusIsError = false;
            OnChanged?.Invoke();
        }

        private void SendStartChallenge(ulong playerId, long now)
        {
            int epoch = _epoch;
            int generation = ++_launchGeneration;
            var request = _pendingLaunchRequest.Clone();
            _launchInFlight = true;
            _nextLaunchAtMs = now + MinWriteIntervalMs;
            SetStatus("正在准备瑞兽迎春挑战…", false);

            _net.Call(
                SceneActivityClientPlayerRpc.StartFestivalBeastChallengeMessageId,
                request,
                global::StartFestivalBeastChallengeResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _launchGeneration) return;
                    ++_launchGeneration;
                    _launchInFlight = false;

                    if (resp.LaunchState == global::FestivalBeastLaunchState.FestivalBeastLaunchAdmitted && !HasTip(resp.ErrorMessage))
                    {
                        ClearPendingLaunch(playerId);
                        StatusMessage = string.Empty;
                        StatusIsError = false;
                        OnChanged?.Invoke();
                        OnChallengeAdmitted?.Invoke(new FestivalBeastLaunchEvent(
                            resp.ClientRequestId, resp.LaunchState, resp.BattleId,
                            resp.LockedPetId, resp.MatchedTierId, resp.AdmittedAtMs, resp.Replayed));
                        return;
                    }

                    if (resp.LaunchState == global::FestivalBeastLaunchState.FestivalBeastLaunchRejected
                        && (!HasTip(resp.ErrorMessage) || !TipFaults.IsFault(resp.ErrorMessage.Id)))
                    {
                        ClearPendingLaunch(playerId);
                        SetStatus(FestivalBeastModels.DescribeTip(resp.ErrorMessage?.Id ?? 0u, resp.RejectReason), true);
                        RefreshDetail(force: true);
                        return;
                    }

                    // LAUNCHING / UNKNOWN / 故障码：保留原 UUID，限次退避恢复，绝不重复开局。
                    DeferLaunchRecovery(HasTip(resp.ErrorMessage)
                        ? FestivalBeastModels.DescribeTip(resp.ErrorMessage.Id, resp.RejectReason)
                        : "挑战开局结果尚未确认，正在保留原请求重试。");
                },
                err =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _launchGeneration) return;
                    ++_launchGeneration;
                    _launchInFlight = false;
                    DeferLaunchRecovery("网络异常，开局结果尚未确认；原请求已保留。");
                });
        }

        private void SendClaimReward(ulong playerId, long now)
        {
            int epoch = _epoch;
            int generation = ++_claimGeneration;
            var request = _pendingClaimRequest.Clone();
            _claimInFlight = true;
            _nextClaimAtMs = now + MinWriteIntervalMs;
            SetStatus("正在领取瑞兽奖励…", false);

            _net.Call(
                SceneActivityClientPlayerRpc.ClaimFestivalBeastRewardMessageId,
                request,
                global::ClaimFestivalBeastRewardResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _claimGeneration) return;
                    ++_claimGeneration;
                    _claimInFlight = false;

                    bool claimedOutcome =
                        resp.Outcome == global::FestivalBeastClaimOutcome.FestivalBeastClaimApplied
                        || resp.Outcome == global::FestivalBeastClaimOutcome.FestivalBeastClaimAlreadyClaimed;

                    if (claimedOutcome && resp.Durable && !HasTip(resp.ErrorMessage))
                    {
                        ClearPendingClaim(playerId);
                        StatusMessage = "已成功领取瑞兽奖励！";
                        StatusIsError = false;
                        OnChanged?.Invoke();
                        OnRewardClaimed?.Invoke(new FestivalBeastRewardClaimedEvent(
                            resp.ClientRequestId, resp.RewardIdentity, resp.RewardDefinitionVersion,
                            resp.RewardPetTableId, resp.PetId, resp.ClaimedAtMs, resp.Replayed, resp.Durable));
                        if (IsCurrent(epoch, playerId)) RefreshDetail(force: true);
                        return;
                    }

                    if (resp.Outcome == global::FestivalBeastClaimOutcome.FestivalBeastClaimRejected
                        && (!HasTip(resp.ErrorMessage) || !TipFaults.IsFault(resp.ErrorMessage.Id)))
                    {
                        ClearPendingClaim(playerId);
                        SetStatus(FestivalBeastModels.DescribeTip(resp.ErrorMessage?.Id ?? 0u), true);
                        if (IsCurrent(epoch, playerId)) RefreshDetail(force: true);
                        return;
                    }

                    // 尚未确认 durable=true 或 UNKNOWN / 故障：保留原 UUID，持续收敛确认。
                    DeferClaimRecovery(claimedOutcome && !resp.Durable
                        ? "奖励已提交正在确认落盘，请稍候…"
                        : HasTip(resp.ErrorMessage)
                            ? FestivalBeastModels.DescribeTip(resp.ErrorMessage.Id)
                            : "领奖结果尚未确认，正在保留原请求恢复。");
                },
                err =>
                {
                    if (!IsCurrent(epoch, playerId) || generation != _claimGeneration) return;
                    ++_claimGeneration;
                    _claimInFlight = false;
                    DeferClaimRecovery("网络异常，领奖结果尚未确认；原请求已保留。");
                });
        }

        private void HydrateLocalRecovery(ulong playerId)
        {
            if (playerId == 0 || _hydratedPlayerId == playerId) return;
            _hydratedPlayerId = playerId;

            if (LaunchRecoveryStore != null)
            {
                try
                {
                    var raw = LaunchRecoveryStore.Load(playerId);
                    if (!string.IsNullOrEmpty(raw))
                    {
                        var req = global::StartFestivalBeastChallengeRequest.Parser.ParseFrom(Convert.FromBase64String(raw));
                        if (req.ActivityInstanceId == 0u || !IsValidUuid(req.ClientRequestId))
                            throw new InvalidOperationException();
                        _pendingLaunchRequest = req;
                        _launchRecoveryAttempts = 0;
                    }
                }
                catch (Exception)
                {
                    _launchRecoveryBlocked = true;
                }
            }

            if (ClaimRecoveryStore != null)
            {
                try
                {
                    var raw = ClaimRecoveryStore.Load(playerId);
                    if (!string.IsNullOrEmpty(raw))
                    {
                        var req = global::ClaimFestivalBeastRewardRequest.Parser.ParseFrom(Convert.FromBase64String(raw));
                        if (string.IsNullOrEmpty(req.RewardIdentity) || !IsValidUuid(req.ClientRequestId))
                            throw new InvalidOperationException();
                        _pendingClaimRequest = req;
                        _claimRecoveryAttempts = 0;
                    }
                }
                catch (Exception)
                {
                    _claimRecoveryBlocked = true;
                }
            }
        }

        private void ClearPendingLaunch(ulong playerId)
        {
            var req = _pendingLaunchRequest;
            _pendingLaunchRequest = null;
            _launchRecoveryBlocked = false;
            _launchRecoveryAttempts = 0;
            _nextLaunchRecoveryAtMs = 0;
            if (req != null && LaunchRecoveryStore != null && playerId != 0)
            {
                try { LaunchRecoveryStore.Clear(playerId, Encode(req)); }
                catch (Exception) { }
            }
        }

        private void ClearPendingClaim(ulong playerId)
        {
            var req = _pendingClaimRequest;
            _pendingClaimRequest = null;
            _claimRecoveryBlocked = false;
            _claimRecoveryAttempts = 0;
            _nextClaimRecoveryAtMs = 0;
            if (req != null && ClaimRecoveryStore != null && playerId != 0)
            {
                try { ClaimRecoveryStore.Clear(playerId, Encode(req)); }
                catch (Exception) { }
            }
        }

        private void ClearLaunchIfBattleFinished(ulong battleId)
        {
            if (battleId == 0UL) return;
            if (Detail?.PendingLaunch != null && Detail.PendingLaunch.BattleId == battleId)
            {
                ClearPendingLaunch(_net.PlayerId);
            }
        }

        private void DeferLaunchRecovery(string message)
        {
            _nextLaunchRecoveryAtMs = _nowMs() + RecoveryBaseBackoffMs * (1L << Math.Min(_launchRecoveryAttempts, 2));
            SetStatus(message, true);
        }

        private void DeferClaimRecovery(string message)
        {
            _nextClaimRecoveryAtMs = _nowMs() + RecoveryBaseBackoffMs * (1L << Math.Min(_claimRecoveryAttempts, 2));
            SetStatus(message, true);
        }

        private void HandleDisconnected()
        {
            ObserveSession();
            _detailInFlight = false;
            _launchInFlight = false;
            _claimInFlight = false;
            ++_detailGeneration;
            ++_launchGeneration;
            ++_claimGeneration;
            OnChanged?.Invoke();
        }

        private void ObserveSession()
        {
            object conn = _connectionIdentity();
            ulong playerId = _net.PlayerId;
            if (ReferenceEquals(conn, _observedConnection) && playerId == _observedPlayerId) return;

            bool playerChanged = playerId != _observedPlayerId;
            _observedConnection = conn;
            _observedPlayerId = playerId;
            ++_epoch;
            _detailInFlight = false;
            _detailQueued = false;
            _launchInFlight = false;
            _claimInFlight = false;
            ++_detailGeneration;
            ++_launchGeneration;
            ++_claimGeneration;
            if (playerChanged)
            {
                Detail = null;
                ActiveTelegraph = null;
                LastResolvedTelegraph = null;
                LastSettlement = null;
                _pendingLaunchRequest = null;
                _launchRecoveryBlocked = false;
                _launchRecoveryAttempts = 0;
                _pendingClaimRequest = null;
                _claimRecoveryBlocked = false;
                _claimRecoveryAttempts = 0;
                _hydratedPlayerId = 0;
                StatusMessage = string.Empty;
                StatusIsError = false;
            }
            OnChanged?.Invoke();
        }

        private bool IsCurrent(int epoch, ulong playerId)
            => !_disposed && epoch == _epoch && _net.PlayerId == playerId && ReferenceEquals(_connectionIdentity(), _observedConnection);

        private void SetStatus(string message, bool isError)
        {
            StatusMessage = message ?? string.Empty;
            StatusIsError = isError;
            OnChanged?.Invoke();
        }

        private static bool HasTip(global::TipInfoMessage tip) => tip != null && tip.Id != 0u;
        private static string Encode(IMessage message) => Convert.ToBase64String(message.ToByteArray());
        private static bool IsValidUuid(string text) => Guid.TryParseExact(text, "D", out var g) && g != Guid.Empty;

        private static bool TelegraphsEqual(FestivalBeastTelegraphSnapshot a, FestivalBeastTelegraphSnapshot b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.SourceActorId == b.SourceActorId
                && a.TargetActorId == b.TargetActorId
                && a.TelegraphRound == b.TelegraphRound
                && a.ExecuteRound == b.ExecuteRound
                && a.IntentKind == b.IntentKind
                && a.Status == b.Status
                && a.StateVersion == b.StateVersion;
        }
    }
}
