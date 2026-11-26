using System;
using System.Globalization;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;

namespace MmorpgClient.Game.SpiritShop
{
    /// <summary>
    /// 官方灵石百宝阁 V1 客户端状态与网络适配层（RPC 509 GetSpiritShopInfo / 510 BuySpiritShopGoods）。
    ///
    /// 设计约束：
    /// - 零配表：所有商品货架、价格、绑定标记、灵石/绑定灵石双余额与限购计数完全由服务端权威下发；
    /// - 严防双扣：购买强依赖非 0 幂等键 ClientRequestKeys，结果不明（超时/1003）原样重发至多 1 次，
    ///   若重发后仍不明则置 RequiresReconnect = true，断线重连清零刷新前不再允许下单；
    ///   RpcGuard.CircuitOpenError / 1008 限流 / 业务拒绝确定未成交，绝不锁死面板；
    /// - 不依赖 UnityEngine / GameClient，支持 EditMode 假传输单测。
    /// </summary>
    public sealed class SpiritShopClient : IDisposable
    {
        public const float MinInfoIntervalSeconds = 1.0f;
        public const float MinBuyIntervalSeconds = 0.5f;
        public const float RateLimitBackoffSeconds = 1.5f;

        public const string UnsupportedMessage = "当前服务器尚未开放灵石百宝阁。";
        public const string InfoPendingMessage = "正在刷新百宝阁货架…";
        public const string BuyPendingMessage = "正在购买…";
        public const string TooFastMessage = "操作太快了，请稍候再试。";
        public const string RecoveryMessage = "网络异常，购买结果未知。请重新连接或重登后查看背包与限购额度。";

        private const string ServerTipPrefix = "server tip=";

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<float> _clock;
        private readonly SpiritShopMessageIds _messageIds;

        private object _observedConnection;
        private ulong _observedPlayerId;
        private bool _disposed;
        private int _generation;
        private float _nextInfoAt = float.NegativeInfinity;
        private float _nextBuyAt = float.NegativeInfinity;
        private bool _requeryAfterPending;
        private bool _inBattle;

        public event Action Changed;
        public event Action<global::SpiritShopGoodsView, uint> Purchased;
        public event Action<global::BagInfo> BagReceived;

        public global::SpiritShopInfo Info { get; private set; }
        public bool HasInfo => Info != null;
        public bool InfoPending { get; private set; }
        public bool InfoQueued { get; private set; }
        public bool BuyPending { get; private set; }
        public uint BuyingGoodsId { get; private set; }
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

        public ulong DiamondBalance => Info?.DiamondBalance ?? 0;
        public ulong BindDiamondBalance => Info?.BindDiamondBalance ?? 0;

        public SpiritShopClient(
            IBattleTransport transport,
            Func<object> connectionIdentity,
            Func<float> clock,
            SpiritShopMessageIds? messageIds = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _connectionIdentity = connectionIdentity ?? throw new ArgumentNullException(nameof(connectionIdentity));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _messageIds = messageIds ?? SpiritShopMessageIds.Generated;
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
            InfoPending = false;
            InfoQueued = false;
            BuyPending = false;
            BuyingGoodsId = 0;
        }

        public global::SpiritShopGoodsView FindGoods(uint goodsId) => SpiritShopModels.FindGoods(Info, goodsId);

        public global::SpiritShopLimitGroupView FindLimitGroup(uint groupId) => SpiritShopModels.FindLimitGroup(Info, groupId);

        public SpiritShopPurchaseQuote QuoteFor(uint goodsId) => SpiritShopPurchaseQuote.Of(FindGoods(goodsId));

        public void RequestInfo(bool force = false)
        {
            if (_disposed || !IsSupported) return;
            ObserveSessionBoundary();
            if (!Ready) return;
            if (InfoPending)
            {
                if (force) _requeryAfterPending = true;
                return;
            }
            float now = _clock();
            if (!force && now < _nextInfoAt)
            {
                InfoQueued = true;
                return;
            }
            SendInfo(now);
        }

        public bool BuyGoods(SpiritShopPurchaseQuote quote, uint count, Action<bool> completed = null)
        {
            if (_disposed || !IsSupported)
            {
                completed?.Invoke(false);
                return false;
            }
            ObserveSessionBoundary();
            if (BuyPending)
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
                RejectLocally(SpiritShopModels.DescribeBlock(SpiritShopBuyBlock.NotReady));
                completed?.Invoke(false);
                return false;
            }
            var goods = FindGoods(quote.GoodsId);
            var latestQuote = SpiritShopPurchaseQuote.Of(goods);
            if (goods == null || !quote.IsValid || quote != latestQuote)
            {
                RejectLocally(SpiritShopModels.DescribeTip((uint)spirit_shop_error.KSpiritShopPriceChanged));
                RequestInfo(force: true);
                completed?.Invoke(false);
                return false;
            }
            var group = FindLimitGroup(goods.LimitGroupId);
            var block = SpiritShopModels.EvaluateBuy(goods, group, count, true, _inBattle);
            if (block != SpiritShopBuyBlock.None)
            {
                RejectLocally(SpiritShopModels.DescribeBlock(block));
                completed?.Invoke(false);
                return false;
            }
            float now = _clock();
            if (now < _nextBuyAt)
            {
                RejectLocally(TooFastMessage);
                completed?.Invoke(false);
                return false;
            }
            if (!ClientRequestKeys.TryCreate(out ulong requestKey))
            {
                RejectLocally(ClientRequestKeys.UnavailableText);
                completed?.Invoke(false);
                return false;
            }

            _nextBuyAt = now + MinBuyIntervalSeconds;
            BuyPending = true;
            BuyingGoodsId = quote.GoodsId;
            int generation = _generation;
            var seen = goods.Clone();
            SetStatus(BuyPendingMessage, false);
            Changed?.Invoke();

            var request = new global::BuySpiritShopGoodsRequest
            {
                ClientRequestId = requestKey,
                GoodsId = quote.GoodsId,
                Count = count,
                ExpectedCurrencyType = quote.CurrencyType,
                ExpectedUnitPrice = quote.UnitPrice,
            };
            quote.WriteExpectedItems(request.ExpectedItems);

            int attempt = 0;
            bool resent = false;

            void Send()
            {
                int mine = ++attempt;
                _net.Call(_messageIds.BuyGoods, request, global::BuySpiritShopGoodsResponse.Parser,
                    response =>
                    {
                        if (!IsCurrent(generation) || mine != attempt) return;
                        EndBuy();
                        uint tipId = response.ErrorMessage?.Id ?? 0;
                        if (tipId != 0)
                        {
                            if (tipId == (uint)common_error.KInvalidParameter)
                                ClientRequestKeys.ReportKeyReused("BuySpiritShopGoods");
                            HandleBuyTip(tipId);
                            Changed?.Invoke();
                            completed?.Invoke(false);
                            return;
                        }
                        if (response.Info != null) Info = response.Info;
                        SetStatus(SpiritShopModels.PurchasedText(seen, count), false);
                        if (response.Bag != null) BagReceived?.Invoke(response.Bag);
                        Changed?.Invoke();
                        Purchased?.Invoke(seen, count);
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
                        EndBuy();
                        uint tipId = ParseEnvelopeTip(error);
                        if (!resent && string.Equals(error, RpcGuard.CircuitOpenError, StringComparison.Ordinal))
                        {
                            SetStatus("灵石百宝阁服务暂时繁忙，请稍后重试。", true);
                        }
                        else if (!resent && tipId != 0 && tipId != (uint)common_error.KServiceUnavailable)
                        {
                            HandleBuyTip(tipId);
                        }
                        else
                        {
                            RequiresReconnect = true;
                            SetStatus(RecoveryMessage, true);
                        }
                        Changed?.Invoke();
                        completed?.Invoke(false);
                    });
            }

            Send();
            return true;
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
            if (!InfoPending && InfoQueued && Ready && _clock() >= _nextInfoAt)
                SendInfo(_clock());
        }

        private void SendInfo(float now)
        {
            InfoQueued = false;
            _requeryAfterPending = false;
            InfoPending = true;
            _nextInfoAt = now + MinInfoIntervalSeconds;
            int generation = _generation;

            _net.Call(_messageIds.GetInfo, new global::GetSpiritShopInfoRequest(), global::GetSpiritShopInfoResponse.Parser,
                response =>
                {
                    if (!IsCurrent(generation)) return;
                    InfoPending = false;
                    uint tipId = response.ErrorMessage?.Id ?? 0;
                    if (tipId != 0)
                    {
                        if (tipId == (uint)common_error.KRateLimitExceeded)
                        {
                            float backoff = _clock() + RateLimitBackoffSeconds;
                            if (backoff > _nextInfoAt) _nextInfoAt = backoff;
                        }
                        SetStatus(SpiritShopModels.DescribeTip(tipId), true);
                        Changed?.Invoke();
                        return;
                    }
                    if (response.Info != null) Info = response.Info;
                    if (!BuyPending && Status == InfoPendingMessage) ClearStatus();
                    Changed?.Invoke();
                    if (_requeryAfterPending)
                    {
                        _requeryAfterPending = false;
                        RequestInfo(force: true);
                    }
                },
                error =>
                {
                    if (!IsCurrent(generation)) return;
                    InfoPending = false;
                    uint tipId = ParseEnvelopeTip(error);
                    if (tipId == (uint)common_error.KRateLimitExceeded)
                    {
                        float backoff = _clock() + RateLimitBackoffSeconds;
                        if (backoff > _nextInfoAt) _nextInfoAt = backoff;
                        SetStatus(SpiritShopModels.DescribeTip(tipId), true);
                    }
                    else if (string.Equals(error, RpcGuard.CircuitOpenError, StringComparison.Ordinal))
                    {
                        SetStatus("灵石百宝阁服务暂时繁忙，请稍后重试。", true);
                    }
                    else
                    {
                        SetStatus("百宝阁货架读取失败，请稍后重试。", true);
                    }
                    Changed?.Invoke();
                });
        }

        private void HandleBuyTip(uint tipId)
        {
            if (tipId == (uint)common_error.KRateLimitExceeded)
            {
                float backoff = _clock() + RateLimitBackoffSeconds;
                if (backoff > _nextBuyAt) _nextBuyAt = backoff;
            }
            SetStatus(SpiritShopModels.DescribeTip(tipId), true);
            if (SpiritShopModels.TipMeansStale(tipId)) RequestInfo(force: true);
        }

        private void EndBuy()
        {
            BuyPending = false;
            BuyingGoodsId = 0;
        }

        private bool RejectLocally(string message)
        {
            SetStatus(message, true);
            Changed?.Invoke();
            return false;
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
            Info = null;
            InfoPending = false;
            InfoQueued = false;
            _requeryAfterPending = false;
            BuyPending = false;
            BuyingGoodsId = 0;
            RequiresReconnect = false;
            _nextInfoAt = float.NegativeInfinity;
            _nextBuyAt = float.NegativeInfinity;
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
