using System;
using System.Globalization;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;

namespace MmorpgClient.Game.RewardedAd
{
    /// <summary>
    /// 零拒审激励视频广告客户端状态机与网络协调层（SceneRewardedAdClientPlayer 540 / 541 / 542）。
    ///
    /// 核心保障：
    /// 1. 零配表硬编码：总开关、日限、冷却与单次绑定银两数额全部由服务端下发；
    /// 2. 抗 30 秒全屏广告切后台断连：完整观看后如遇网络断连或超时，保留绑定到 PlayerId 的 PendingClaim，
    ///    待重连恢复在线后自动携带同一 (ClientRequestId, AdSessionId) 幂等补领，不丢奖也不双发；
    /// 3. 严守 WebGL2 单线程规则：零 Task.Run / Task.Delay / 线程，仅由外部帧时钟 Tick() 驱动。
    /// </summary>
    public sealed class RewardedAdClient : IDisposable
    {
        public const float MinInfoIntervalSeconds = 1.0f;
        public const float PendingClaimRetryIntervalSeconds = 2.0f;
        private const string ServerTipPrefix = "server tip=";

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<float> _clock;
        private readonly IRewardedAdBridge _bridge;
        private readonly RewardedAdMessageIds _messageIds;
        private readonly uint _slotId;

        private object _observedConnection;
        private ulong _observedPlayerId;
        private bool _disposed;
        private int _generation;
        private string _adUnitId = string.Empty;
        private bool _inBattle;
        private ulong _lastServerTimeMs;
        private float _lastServerTimeAtClock;
        private float _nextInfoAt = float.NegativeInfinity;
        private float _nextPendingClaimRetryAt = float.NegativeInfinity;
        private bool _infoPending;
        private bool _sessionPending;
        private bool _watchingAd;
        private bool _claimPending;
        private RewardedAdPendingClaim _pendingClaim;

        public event Action Changed;
        public event Action<global::ClaimRewardedAdResponse> Claimed;

        public global::GetRewardedAdInfoResponse Info { get; private set; }
        public string Status { get; private set; } = string.Empty;
        public bool StatusIsError { get; private set; }
        public bool Busy => _sessionPending || _watchingAd || _claimPending;
        public bool HasPendingClaim => _pendingClaim.IsValid;
        public bool IsSupported => _messageIds.IsSupported;
        public bool Ready => IsSupported && !_disposed && _net.IsReady && _net.PlayerId != 0;

        public bool InBattle
        {
            get => _inBattle;
            set
            {
                if (_inBattle == value) return;
                _inBattle = value;
                Changed?.Invoke();
            }
        }

        public RewardedAdClient(
            IBattleTransport transport,
            Func<object> connectionIdentity,
            Func<float> clock,
            IRewardedAdBridge bridge,
            uint slotId = RewardedAdModel.DefaultSlotId,
            RewardedAdMessageIds? messageIds = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _connectionIdentity = connectionIdentity ?? throw new ArgumentNullException(nameof(connectionIdentity));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _bridge = bridge ?? NullRewardedAdBridge.Instance;
            _slotId = slotId == 0 ? RewardedAdModel.DefaultSlotId : slotId;
            _messageIds = messageIds ?? RewardedAdMessageIds.Generated;
            _observedConnection = _connectionIdentity();
            _observedPlayerId = _net.PlayerId;
            _net.Disconnected += HandleDisconnected;
            _bridge.ReadyChanged += HandleBridgeReadyChanged;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _net.Disconnected -= HandleDisconnected;
            _bridge.ReadyChanged -= HandleBridgeReadyChanged;
            _bridge.CancelPending();
            _disposed = true;
            ++_generation;
            _infoPending = false;
            _sessionPending = false;
            _watchingAd = false;
            _claimPending = false;
            _pendingClaim = default;
        }

        public void ConfigureAdUnitId(string adUnitId)
        {
            string normalized = adUnitId == null ? string.Empty : adUnitId.Trim();
            if (string.Equals(_adUnitId, normalized, StringComparison.Ordinal)) return;
            _adUnitId = normalized;
            MaybePreload();
            Changed?.Invoke();
        }

        public RewardedAdViewState Snapshot() => Snapshot(_slotId);

        public RewardedAdViewState Snapshot(uint slotId)
        {
            uint resolvedSlot = slotId == 0 ? _slotId : slotId;
            ulong estServerNow = EstimateServerNowMs();
            if (!ClientDistributionPolicy.Current.RewardedAdAllowed)
            {
                return RewardedAdModel.Evaluate(
                    null,
                    resolvedSlot,
                    false,
                    false,
                    string.Empty,
                    _inBattle,
                    Busy,
                    estServerNow);
            }
            return RewardedAdModel.Evaluate(
                Info,
                resolvedSlot,
                _bridge.IsSupported,
                _bridge.IsAdReady,
                _adUnitId,
                _inBattle,
                Busy,
                estServerNow);
        }

        public void RequestInfo(bool force = false)
        {
            if (_disposed || !IsSupported) return;
            ObserveSessionBoundary();
            if (!Ready || _infoPending) return;
            float now = _clock();
            if (!force && now < _nextInfoAt) return;

            _nextInfoAt = now + MinInfoIntervalSeconds;
            _infoPending = true;
            int generation = _generation;

            var req = new global::GetRewardedAdInfoRequest
            {
                ClientPlatform = _bridge.ClientPlatform,
            };
            _net.Call(_messageIds.GetInfo, req, global::GetRewardedAdInfoResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(generation)) return;
                    _infoPending = false;
                    uint tipId = resp?.ErrorMessage?.Id ?? 0;
                    if (tipId != 0)
                    {
                        SetStatus(RewardedAdModel.DescribeTip(tipId), true);
                        Changed?.Invoke();
                        return;
                    }
                    Info = resp;
                    RecordServerClock(resp.ServerTimeMs);
                    MaybePreload();
                    Changed?.Invoke();
                },
                _ =>
                {
                    if (!IsCurrent(generation)) return;
                    _infoPending = false;
                    Changed?.Invoke();
                });
        }

        public bool StartWatch(Action<bool> completed = null)
            => StartWatch(_slotId, 0u, string.Empty, completed);

        public bool StartWatch(uint targetParamU32, string targetParamStr, Action<bool> completed = null)
            => StartWatch(_slotId, targetParamU32, targetParamStr, completed);

        public bool StartWatch(uint slotId, uint targetParamU32, string targetParamStr, Action<bool> completed = null)
        {
            uint resolvedSlot = slotId == 0 ? _slotId : slotId;
            if (_disposed || !IsSupported || !ClientDistributionPolicy.Current.RewardedAdAllowed)
            {
                completed?.Invoke(false);
                return false;
            }
            ObserveSessionBoundary();
            if (!Ready || Busy)
            {
                completed?.Invoke(false);
                return false;
            }
            var view = Snapshot(resolvedSlot);
            if (!view.CanClickWatch)
            {
                completed?.Invoke(false);
                return false;
            }

            _sessionPending = true;
            int generation = _generation;
            ulong playerId = _net.PlayerId;
            SetStatus(RewardedAdModel.WatchingStatusText, false);
            Changed?.Invoke();

            var req = new global::BeginRewardedAdSessionRequest
            {
                SlotId = resolvedSlot,
                ClientPlatform = _bridge.ClientPlatform,
            };
            _net.Call(_messageIds.BeginSession, req, global::BeginRewardedAdSessionResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(generation)) return;
                    _sessionPending = false;
                    uint tipId = resp?.ErrorMessage?.Id ?? 0;
                    if (tipId != 0 || resp == null || resp.AdSessionId == 0)
                    {
                        SetStatus(RewardedAdModel.DescribeTip(tipId), true);
                        Changed?.Invoke();
                        completed?.Invoke(false);
                        return;
                    }

                    ulong adSessionId = resp.AdSessionId;
                    _watchingAd = true;
                    Changed?.Invoke();

                    _bridge.Show(showResult =>
                    {
                        if (_disposed || playerId != _net.PlayerId) return;
                        _watchingAd = false;
                        if (!showResult.IsCompleted)
                        {
                            string msg = showResult.Status == RewardedAdShowStatus.DismissedEarly
                                ? RewardedAdModel.DismissedEarlyStatusText
                                : RewardedAdModel.UnavailableStatusText;
                            SetStatus(msg, showResult.Status != RewardedAdShowStatus.DismissedEarly);
                            MaybePreload();
                            Changed?.Invoke();
                            completed?.Invoke(false);
                            return;
                        }

                        if (!ClientRequestKeys.TryCreate(out ulong requestKey))
                        {
                            SetStatus(ClientRequestKeys.UnavailableText, true);
                            Changed?.Invoke();
                            completed?.Invoke(false);
                            return;
                        }

                        _pendingClaim = new RewardedAdPendingClaim(
                            playerId, resolvedSlot, adSessionId, requestKey, showResult.SsvProof,
                            targetParamU32, targetParamStr);
                        _nextPendingClaimRetryAt = float.NegativeInfinity;
                        TryDispatchPendingClaim(completed);
                    });
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    _sessionPending = false;
                    uint tipId = ParseEnvelopeTip(error);
                    SetStatus(tipId != 0 ? RewardedAdModel.DescribeTip(tipId) : RewardedAdModel.UnavailableStatusText, true);
                    Changed?.Invoke();
                    completed?.Invoke(false);
                });
            return true;
        }

        /// <summary>由帧循环按单调时钟调用：重连后自动补发 PendingClaim，绝不阻塞主线程。</summary>
        public void Tick()
        {
            if (_disposed || !IsSupported) return;
            ObserveSessionBoundary();
            if (_pendingClaim.IsValid && Ready && !_claimPending && !_watchingAd)
            {
                if (_pendingClaim.PlayerId != _net.PlayerId)
                {
                    _pendingClaim = default;
                    return;
                }
                if (_clock() >= _nextPendingClaimRetryAt)
                {
                    TryDispatchPendingClaim(null);
                }
            }
        }

        private void TryDispatchPendingClaim(Action<bool> completed)
        {
            if (!_pendingClaim.IsValid || _claimPending) return;
            if (!Ready)
            {
                SetStatus(RewardedAdModel.ReconnectPendingStatusText, false);
                Changed?.Invoke();
                completed?.Invoke(false);
                return;
            }

            var claim = _pendingClaim;
            _claimPending = true;
            int generation = _generation;
            SetStatus(RewardedAdModel.ClaimingStatusText, false);
            Changed?.Invoke();

            var req = new global::ClaimRewardedAdRequest
            {
                ClientRequestId = claim.ClientRequestId,
                SlotId = claim.SlotId,
                AdSessionId = claim.AdSessionId,
                SsvProof = claim.SsvProof,
                TargetParamU32 = claim.TargetParamU32,
                TargetParamStr = claim.TargetParamStr,
            };

            bool resent = false;
            int attempt = 0;

            void Send()
            {
                int mine = ++attempt;
                _net.Call(_messageIds.Claim, req, global::ClaimRewardedAdResponse.Parser,
                    resp =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        _claimPending = false;
                        uint tipId = resp?.ErrorMessage?.Id ?? 0;
                        if (tipId != 0)
                        {
                            if (tipId == (uint)common_error.KInvalidParameter)
                                ClientRequestKeys.ReportKeyReused("ClaimRewardedAd");
                            // 瞬时态（冻结/战斗/服务不可用）保留 PendingClaim 供重试；终局业务拒绝清空凭据。
                            if (!IsTransientRetryTip(tipId))
                            {
                                _pendingClaim = default;
                            }
                            else
                            {
                                _nextPendingClaimRetryAt = _clock() + PendingClaimRetryIntervalSeconds;
                            }
                            SetStatus(RewardedAdModel.DescribeTip(tipId), true);
                            Changed?.Invoke();
                            completed?.Invoke(false);
                            return;
                        }

                        _pendingClaim = default;
                        ApplyClaimSuccess(resp);
                        SetStatus(RewardedAdModel.BuildClaimedToastForKind(resp.RewardKind, resp.RewardAmount, resp.CurrencyType), false);
                        MaybePreload();
                        Changed?.Invoke();
                        Claimed?.Invoke(resp);
                        completed?.Invoke(true);
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
                        _claimPending = false;
                        uint tipId = ParseEnvelopeTip(error);
                        if (!resent && tipId != 0 && tipId != (uint)common_error.KServiceUnavailable && !IsTransientRetryTip(tipId))
                        {
                            _pendingClaim = default;
                            SetStatus(RewardedAdModel.DescribeTip(tipId), true);
                        }
                        else if (!resent && string.Equals(error, RpcGuard.CircuitOpenError, StringComparison.Ordinal))
                        {
                            _nextPendingClaimRetryAt = _clock() + PendingClaimRetryIntervalSeconds;
                            SetStatus(RewardedAdModel.ReconnectPendingStatusText, false);
                        }
                        else
                        {
                            // 断线或结果不明：保留 PendingClaim，等待重连后由 Tick() 携带同幂等键补领。
                            _nextPendingClaimRetryAt = _clock() + PendingClaimRetryIntervalSeconds;
                            SetStatus(RewardedAdModel.ReconnectPendingStatusText, false);
                        }
                        Changed?.Invoke();
                        completed?.Invoke(false);
                    });
            }

            Send();
        }

        private void ApplyClaimSuccess(global::ClaimRewardedAdResponse resp)
        {
            if (resp == null) return;
            if (Info == null)
            {
                Info = new global::GetRewardedAdInfoResponse();
            }
            Info.BindGoldBalance = resp.CurrencyBalance;
            Info.GoldBalance = resp.GoldBalance;
            if (resp.Slot != null)
            {
                bool replaced = false;
                for (int i = 0; i < Info.Slots.Count; i++)
                {
                    if (Info.Slots[i].SlotId == resp.Slot.SlotId)
                    {
                        Info.Slots[i] = resp.Slot.Clone();
                        replaced = true;
                        break;
                    }
                }
                if (!replaced) Info.Slots.Add(resp.Slot.Clone());
            }
        }

        private void MaybePreload()
        {
            if (!ClientDistributionPolicy.Current.RewardedAdAllowed || !_bridge.IsSupported || string.IsNullOrWhiteSpace(_adUnitId))
                return;
            if (Info?.Slots == null) return;
            for (int i = 0; i < Info.Slots.Count; i++)
            {
                var slot = Info.Slots[i];
                if (slot != null && slot.Enabled && slot.ClaimedToday < slot.DailyLimit)
                {
                    _bridge.Preload(_adUnitId);
                    return;
                }
            }
        }

        private void HandleBridgeReadyChanged()
        {
            if (_disposed) return;
            Changed?.Invoke();
        }

        private void HandleDisconnected()
        {
            if (_disposed) return;
            ++_generation;
            _infoPending = false;
            _sessionPending = false;
            _claimPending = false;
            // 注意：_watchingAd 与 _pendingClaim 刻意不在断线时清空，
            // 因为看 30 秒全屏广告暂停心跳触发短断连是正常场景，重连后自动补领。
            Changed?.Invoke();
        }

        private void ObserveSessionBoundary()
        {
            object conn = _connectionIdentity();
            ulong playerId = _net.PlayerId;
            bool playerChanged = playerId != 0 && _observedPlayerId != 0 && playerId != _observedPlayerId;
            if (playerChanged)
            {
                ++_generation;
                Info = null;
                _pendingClaim = default;
                _bridge.CancelPending();
                _watchingAd = false;
                _sessionPending = false;
                _claimPending = false;
                _infoPending = false;
            }
            else if (!ReferenceEquals(conn, _observedConnection))
            {
                ++_generation;
                _infoPending = false;
                _sessionPending = false;
                _claimPending = false;
            }
            _observedConnection = conn;
            if (playerId != 0) _observedPlayerId = playerId;
        }

        private bool IsCurrent(int generation) => !_disposed && generation == _generation;

        private void RecordServerClock(ulong serverTimeMs)
        {
            if (serverTimeMs == 0) return;
            _lastServerTimeMs = serverTimeMs;
            _lastServerTimeAtClock = _clock();
        }

        private ulong EstimateServerNowMs()
        {
            if (_lastServerTimeMs == 0) return 0;
            float elapsed = _clock() - _lastServerTimeAtClock;
            if (elapsed <= 0f) return _lastServerTimeMs;
            return _lastServerTimeMs + (ulong)(elapsed * 1000.0);
        }

        private void SetStatus(string text, bool isError)
        {
            Status = text ?? string.Empty;
            StatusIsError = isError;
        }

        private static bool IsTransientRetryTip(uint tipId)
            => tipId == (uint)rewarded_ad_error.KRewardedAdFrozen
                || tipId == (uint)rewarded_ad_error.KRewardedAdInBattle
                || tipId == (uint)common_error.KServiceUnavailable;

        private static uint ParseEnvelopeTip(string error)
        {
            if (string.IsNullOrEmpty(error) || !error.StartsWith(ServerTipPrefix, StringComparison.Ordinal))
                return 0;
            string digits = error.Substring(ServerTipPrefix.Length).Trim();
            return uint.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out uint tipId) ? tipId : 0;
        }
    }
}
