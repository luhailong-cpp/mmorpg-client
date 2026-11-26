using System;
using System.Globalization;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;

namespace MmorpgClient.Game.TravelPass
{
    /// <summary>
    /// 青云游历手记客户端状态与网络适配层（RPC 526–529，走大厅 gate -> scene 路由）。
    ///
    /// 设计约束：
    /// - 所有进度、档位奖励、双余额（灵石 / 绑定灵石）与纪念外显佩戴状态完全由服务端权威下发；
    /// - 解锁典藏与领奖强依赖非 0 幂等键 ClientRequestKeys：超时/1003 原样重发至多 1 次，
    ///   重发后仍不明则置 RequiresReconnect = true，断线重连清零刷新前禁止重复发起；
    /// - 确定未执行（熔断 / 限流 / 业务拒绝）绝不锁死界面；
    /// - 零 UnityEngine 依赖，支持 WebGL2 单线程与 EditMode 假传输单测。
    /// </summary>
    public sealed class TravelPassClient : IDisposable
    {
        public const float MinStateIntervalSeconds = 1.0f;
        public const float MinWriteIntervalSeconds = 0.5f;
        public const float RateLimitBackoffSeconds = 1.5f;

        public const string UnsupportedMessage = "当前服务器尚未开放游历手记。";
        public const string StatePendingMessage = "正在同步游历手记进度…";
        public const string UnlockPendingMessage = "正在解锁典藏手记…";
        public const string ClaimPendingMessage = "正在领取游历奖励…";
        public const string EquipPendingMessage = "正在切换游历纪念外显…";
        public const string TooFastMessage = "操作太快了，请稍候再试。";
        public const string RecoveryMessage = "网络异常，操作结果未知。请重新连接或重登后核对进度与背包。";

        private const string ServerTipPrefix = "server tip=";

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<float> _clock;
        private readonly TravelPassMessageIds _messageIds;

        private object _observedConnection;
        private ulong _observedPlayerId;
        private bool _disposed;
        private int _generation;
        private float _nextStateAt = float.NegativeInfinity;
        private float _nextWriteAt = float.NegativeInfinity;
        private bool _requeryAfterPending;
        private bool _inBattle;

        public event Action Changed;
        public event Action PremiumUnlocked;
        public event Action<uint> RewardClaimed;
        public event Action<global::BagInfo> BagReceived;

        public global::TravelPassSnapshot State { get; private set; }
        public bool HasState => State != null;
        public bool StatePending { get; private set; }
        public bool StateQueued { get; private set; }
        public bool UnlockPending { get; private set; }
        public bool ClaimPending { get; private set; }
        public bool EquipPending { get; private set; }
        public bool AnyWritePending => UnlockPending || ClaimPending || EquipPending;
        public bool RequiresReconnect { get; private set; }
        public string Status { get; private set; } = string.Empty;
        public bool StatusIsError { get; private set; }

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

        public bool IsSupported => _messageIds.IsSupported;
        public bool Ready => IsSupported && !_disposed && _net.IsReady && _net.PlayerId != 0;
        public ulong DiamondBalance => State?.DiamondBalance ?? 0;
        public ulong BindDiamondBalance => State?.BindDiamondBalance ?? 0;
        public bool HasClaimableReward => TravelPassModels.HasClaimable(State);
        public uint ClaimableCount => TravelPassModels.TotalClaimable(State);

        public TravelPassClient(
            IBattleTransport transport,
            Func<object> connectionIdentity,
            Func<float> clock,
            TravelPassMessageIds? messageIds = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _connectionIdentity = connectionIdentity ?? throw new ArgumentNullException(nameof(connectionIdentity));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _messageIds = messageIds ?? TravelPassMessageIds.Generated;
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
            StatePending = StateQueued = UnlockPending = ClaimPending = EquipPending = false;
        }

        public TravelPassUnlockQuote CurrentUnlockQuote() => TravelPassUnlockQuote.Of(State);

        public void RequestState(bool force = false, uint passId = 0)
        {
            if (_disposed || !IsSupported) return;
            ObserveSessionBoundary();
            if (!Ready) return;
            if (StatePending)
            {
                if (force) _requeryAfterPending = true;
                return;
            }
            float now = _clock();
            if (!force && now < _nextStateAt)
            {
                StateQueued = true;
                return;
            }
            SendGetState(now, passId);
        }

        public bool UnlockPremium(TravelPassUnlockQuote quote, Action<bool> completed = null)
        {
            if (!PrepareWriteGate(completed)) return false;
            var latest = TravelPassUnlockQuote.Of(State);
            if (State == null || !State.Available || !quote.IsValid || quote != latest)
            {
                RejectLocally(TravelPassModels.DescribeTip((uint)travel_pass_error.KTravelPassPriceChanged));
                RequestState(force: true);
                completed?.Invoke(false);
                return false;
            }
            if (State.PremiumUnlocked)
            {
                RejectLocally(TravelPassModels.DescribeTip((uint)travel_pass_error.KTravelPassAlreadyUnlocked));
                completed?.Invoke(false);
                return false;
            }
            if (!State.OpenForUnlock)
            {
                RejectLocally(TravelPassModels.DescribeTip((uint)travel_pass_error.KTravelPassNotOpen));
                completed?.Invoke(false);
                return false;
            }
            if (!ClientRequestKeys.TryCreate(out ulong requestKey))
            {
                RejectLocally(ClientRequestKeys.UnavailableText);
                completed?.Invoke(false);
                return false;
            }

            _nextWriteAt = _clock() + MinWriteIntervalSeconds;
            UnlockPending = true;
            int generation = _generation;
            SetStatus(UnlockPendingMessage, false);
            Changed?.Invoke();

            var request = new global::UnlockTravelPassPremiumRequest
            {
                ClientRequestId = requestKey,
                PassId = quote.PassId,
                ExpectedCurrencyType = quote.CurrencyType,
                ExpectedPrice = quote.Price,
                ExpectedConfigRevision = quote.ConfigRevision,
            };

            int attempt = 0;
            bool resent = false;

            void Send()
            {
                int mine = ++attempt;
                _net.Call(_messageIds.UnlockPremium, request, global::UnlockTravelPassPremiumResponse.Parser,
                    response =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        UnlockPending = false;
                        uint tipId = response.ErrorMessage?.Id ?? 0;
                        if (tipId != 0)
                        {
                            if (tipId == (uint)common_error.KInvalidParameter)
                                ClientRequestKeys.ReportKeyReused("UnlockTravelPassPremium");
                            HandleWriteTip(tipId);
                            Changed?.Invoke();
                            completed?.Invoke(false);
                            return;
                        }
                        if (response.Snapshot != null) State = response.Snapshot;
                        SetStatus("已解锁典藏手记（永不过期），可随时领取已达成档位奖励！", false);
                        Changed?.Invoke();
                        PremiumUnlocked?.Invoke();
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
                        UnlockPending = false;
                        HandleWriteTransportError(error, resent);
                        Changed?.Invoke();
                        completed?.Invoke(false);
                    });
            }

            Send();
            return true;
        }

        /// <summary>
        /// 领取游历手记奖励：tierIndex == 0 为一键领取全部当前可领档位，> 0 为精确领取指定档位。
        /// </summary>
        public bool ClaimReward(uint tierIndex, bool premiumTrack, Action<bool> completed = null)
        {
            if (!PrepareWriteGate(completed)) return false;
            if (State == null || !State.Available || State.PassId == 0)
            {
                RejectLocally(TravelPassModels.DescribeTip((uint)travel_pass_error.KTravelPassUnavailable));
                RequestState(force: true);
                completed?.Invoke(false);
                return false;
            }
            if (tierIndex == 0 && !HasClaimableReward)
            {
                RejectLocally(TravelPassModels.DescribeTip((uint)travel_pass_error.KTravelPassNothingToClaim));
                completed?.Invoke(false);
                return false;
            }
            if (!ClientRequestKeys.TryCreate(out ulong requestKey))
            {
                RejectLocally(ClientRequestKeys.UnavailableText);
                completed?.Invoke(false);
                return false;
            }

            _nextWriteAt = _clock() + MinWriteIntervalSeconds;
            ClaimPending = true;
            int generation = _generation;
            SetStatus(ClaimPendingMessage, false);
            Changed?.Invoke();

            var request = new global::ClaimTravelPassRewardRequest
            {
                ClientRequestId = requestKey,
                PassId = State.PassId,
                TierIndex = tierIndex,
                PremiumTrack = tierIndex > 0 && premiumTrack,
            };

            int attempt = 0;
            bool resent = false;

            void Send()
            {
                int mine = ++attempt;
                _net.Call(_messageIds.ClaimReward, request, global::ClaimTravelPassRewardResponse.Parser,
                    response =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        ClaimPending = false;
                        uint tipId = response.ErrorMessage?.Id ?? 0;
                        if (tipId != 0)
                        {
                            if (tipId == (uint)common_error.KInvalidParameter)
                                ClientRequestKeys.ReportKeyReused("ClaimTravelPassReward");
                            HandleWriteTip(tipId);
                            Changed?.Invoke();
                            completed?.Invoke(false);
                            return;
                        }
                        if (response.Snapshot != null) State = response.Snapshot;
                        uint claimed = response.ClaimedCount;
                        SetStatus(claimed > 1
                            ? $"已领取 {claimed} 档游历奖励（绑定道具已入背包）。"
                            : "已领取游历奖励（绑定道具已入背包）。", false);
                        if (response.Bag != null) BagReceived?.Invoke(response.Bag);
                        Changed?.Invoke();
                        RewardClaimed?.Invoke(claimed);
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
                        ClaimPending = false;
                        HandleWriteTransportError(error, resent);
                        Changed?.Invoke();
                        completed?.Invoke(false);
                    });
            }

            Send();
            return true;
        }

        public bool EquipCosmetic(uint cosmeticSlot, string cosmeticKey, Action<bool> completed = null)
        {
            if (_disposed || !IsSupported)
            {
                completed?.Invoke(false);
                return false;
            }
            ObserveSessionBoundary();
            if (AnyWritePending)
            {
                completed?.Invoke(false);
                return false;
            }
            if (!Ready)
            {
                RejectLocally("尚未连接到游戏。");
                completed?.Invoke(false);
                return false;
            }
            if (_inBattle)
            {
                RejectLocally(TravelPassModels.DescribeTip((uint)travel_pass_error.KTravelPassInBattle));
                completed?.Invoke(false);
                return false;
            }
            if (cosmeticSlot != TravelPassModels.CosmeticSlotFrame && cosmeticSlot != TravelPassModels.CosmeticSlotTitle)
            {
                RejectLocally(TravelPassModels.DescribeTip((uint)common_error.KInvalidParameter));
                completed?.Invoke(false);
                return false;
            }
            string normalized = cosmeticKey ?? string.Empty;
            if (normalized.Length > 0 && !TravelPassModels.HasOwnedCosmetic(State, normalized))
            {
                RejectLocally(TravelPassModels.DescribeTip((uint)travel_pass_error.KTravelPassCosmeticNotOwned));
                completed?.Invoke(false);
                return false;
            }
            float now = _clock();
            if (now < _nextWriteAt)
            {
                RejectLocally(TooFastMessage);
                completed?.Invoke(false);
                return false;
            }

            _nextWriteAt = now + MinWriteIntervalSeconds;
            EquipPending = true;
            int generation = _generation;
            SetStatus(EquipPendingMessage, false);
            Changed?.Invoke();

            var request = new global::EquipTravelPassCosmeticRequest
            {
                CosmeticSlot = cosmeticSlot,
                CosmeticKey = normalized,
            };

            _net.Call(_messageIds.EquipCosmetic, request, global::EquipTravelPassCosmeticResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    EquipPending = false;
                    uint tipId = response.ErrorMessage?.Id ?? 0;
                    if (tipId != 0)
                    {
                        HandleWriteTip(tipId);
                        Changed?.Invoke();
                        completed?.Invoke(false);
                        return;
                    }
                    if (response.Snapshot != null) State = response.Snapshot;
                    SetStatus(string.IsNullOrEmpty(normalized)
                        ? (cosmeticSlot == TravelPassModels.CosmeticSlotFrame ? "已卸下纪念头像框。" : "已卸下纪念称谓。")
                        : $"已佩戴「{TravelPassModels.ResolveCosmeticName(State, normalized)}」。", false);
                    Changed?.Invoke();
                    completed?.Invoke(true);
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    EquipPending = false;
                    uint tipId = ParseEnvelopeTip(error);
                    if (string.Equals(error, RpcGuard.CircuitOpenError, StringComparison.Ordinal))
                        SetStatus("游历手记服务暂时繁忙，请稍后重试。", true);
                    else if (tipId != 0)
                        HandleWriteTip(tipId);
                    else
                        SetStatus("外显切换失败，请稍后重试。", true);
                    Changed?.Invoke();
                    completed?.Invoke(false);
                });
            return true;
        }

        public void HandleBattleSettled()
        {
            if (_disposed || !IsSupported || !Ready) return;
            RequestState(force: false);
        }

        public void HandleSceneEntered()
        {
            if (_disposed) return;
            ObserveSessionBoundary();
        }

        public void HandleDisconnected()
        {
            if (_disposed) return;
            ResetSession();
            Changed?.Invoke();
        }

        public void Tick()
        {
            if (_disposed || !IsSupported) return;
            ObserveSessionBoundary();
            if (!StatePending && StateQueued && Ready && _clock() >= _nextStateAt)
                SendGetState(_clock(), 0);
        }

        private bool PrepareWriteGate(Action<bool> completed)
        {
            if (_disposed || !IsSupported)
            {
                completed?.Invoke(false);
                return false;
            }
            ObserveSessionBoundary();
            if (AnyWritePending)
            {
                completed?.Invoke(false);
                return false;
            }
            if (RequiresReconnect)
            {
                RejectLocally(RecoveryMessage);
                completed?.Invoke(false);
                return false;
            }
            if (!Ready)
            {
                RejectLocally("尚未连接到游戏。");
                completed?.Invoke(false);
                return false;
            }
            if (_inBattle)
            {
                RejectLocally(TravelPassModels.DescribeTip((uint)travel_pass_error.KTravelPassInBattle));
                completed?.Invoke(false);
                return false;
            }
            if (_clock() < _nextWriteAt)
            {
                RejectLocally(TooFastMessage);
                completed?.Invoke(false);
                return false;
            }
            return true;
        }

        private void SendGetState(float now, uint passId)
        {
            StateQueued = false;
            _requeryAfterPending = false;
            StatePending = true;
            _nextStateAt = now + MinStateIntervalSeconds;
            int generation = _generation;

            _net.Call(_messageIds.GetState, new global::GetTravelPassStateRequest { PassId = passId },
                global::GetTravelPassStateResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    StatePending = false;
                    uint tipId = response.ErrorMessage?.Id ?? 0;
                    if (tipId != 0)
                    {
                        if (tipId == (uint)common_error.KRateLimitExceeded)
                        {
                            float backoff = _clock() + RateLimitBackoffSeconds;
                            if (backoff > _nextStateAt) _nextStateAt = backoff;
                        }
                        SetStatus(TravelPassModels.DescribeTip(tipId), true);
                        Changed?.Invoke();
                        return;
                    }
                    if (response.Snapshot != null) State = response.Snapshot;
                    if (!AnyWritePending && Status == StatePendingMessage) ClearStatus();
                    Changed?.Invoke();
                    if (_requeryAfterPending)
                    {
                        _requeryAfterPending = false;
                        RequestState(force: true);
                    }
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    StatePending = false;
                    uint tipId = ParseEnvelopeTip(error);
                    if (tipId == (uint)common_error.KRateLimitExceeded)
                    {
                        float backoff = _clock() + RateLimitBackoffSeconds;
                        if (backoff > _nextStateAt) _nextStateAt = backoff;
                        SetStatus(TravelPassModels.DescribeTip(tipId), true);
                    }
                    else if (string.Equals(error, RpcGuard.CircuitOpenError, StringComparison.Ordinal))
                    {
                        SetStatus("游历手记服务暂时繁忙，请稍后重试。", true);
                    }
                    else
                    {
                        SetStatus("游历手记进度读取失败，请稍后重试。", true);
                    }
                    Changed?.Invoke();
                });
        }

        private void HandleWriteTip(uint tipId)
        {
            if (tipId == (uint)common_error.KRateLimitExceeded)
            {
                float backoff = _clock() + RateLimitBackoffSeconds;
                if (backoff > _nextWriteAt) _nextWriteAt = backoff;
            }
            SetStatus(TravelPassModels.DescribeTip(tipId), true);
            if (TravelPassModels.TipMeansStale(tipId)) RequestState(force: true);
        }

        private void HandleWriteTransportError(string error, bool resent)
        {
            uint tipId = ParseEnvelopeTip(error);
            if (!resent && string.Equals(error, RpcGuard.CircuitOpenError, StringComparison.Ordinal))
            {
                SetStatus("游历手记服务暂时繁忙，请稍后重试。", true);
            }
            else if (!resent && tipId != 0 && tipId != (uint)common_error.KServiceUnavailable)
            {
                HandleWriteTip(tipId);
            }
            else
            {
                RequiresReconnect = true;
                SetStatus(RecoveryMessage, true);
            }
        }

        private void RejectLocally(string message)
        {
            SetStatus(message, true);
            Changed?.Invoke();
        }

        private void ObserveSessionBoundary()
        {
            object currentConn = _connectionIdentity();
            ulong currentPlayer = _net.PlayerId;
            if (ReferenceEquals(_observedConnection, currentConn)
                && (_observedPlayerId == 0 || currentPlayer == 0 || _observedPlayerId == currentPlayer))
            {
                if (currentPlayer != 0) _observedPlayerId = currentPlayer;
                return;
            }
            _observedConnection = currentConn;
            _observedPlayerId = currentPlayer;
            ResetSession();
            Changed?.Invoke();
        }

        private void ResetSession()
        {
            ++_generation;
            State = null;
            StatePending = StateQueued = _requeryAfterPending = false;
            UnlockPending = ClaimPending = EquipPending = false;
            RequiresReconnect = false;
            _nextStateAt = float.NegativeInfinity;
            _nextWriteAt = float.NegativeInfinity;
            ClearStatus();
            if (!IsSupported) SetStatus(UnsupportedMessage, false);
        }

        private bool IsCurrent(int generation) => !_disposed && generation == _generation;

        private void SetStatus(string message, bool isError)
        {
            Status = message ?? string.Empty;
            StatusIsError = isError;
        }

        private void ClearStatus()
        {
            Status = string.Empty;
            StatusIsError = false;
        }

        private static uint ParseEnvelopeTip(string error)
        {
            if (error == null || !error.StartsWith(ServerTipPrefix, StringComparison.Ordinal)) return 0;
            return uint.TryParse(error.Substring(ServerTipPrefix.Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out uint tipId) ? tipId : 0;
        }
    }
}
