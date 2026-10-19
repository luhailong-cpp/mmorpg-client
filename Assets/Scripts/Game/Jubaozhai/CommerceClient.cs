using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using Trade;

namespace MmorpgClient.Game.Jubaozhai
{
    // 仅保存玩家自己的待确认请求；保存必须先于网络发送。切换玩家不能重放别人的交易。
    public interface ICommerceRecoveryStore
    {
        string Load(ulong player);
        void Save(ulong player, string value);
        void Clear(ulong player, string expected);
    }

    public sealed class CommerceFileRecoveryStore : ICommerceRecoveryStore
    {
        private readonly DurableRequestFileStore _files;
        public CommerceFileRecoveryStore(string directory, string origin = null)
        { _files = origin == null ? new DurableRequestFileStore(directory) : DurableRequestFileStore.ForOrigin(directory, origin); }
        public string Load(ulong player) => _files.Load(player);
        public void Save(ulong player, string value) => _files.Save(player, value);
        public void Clear(ulong player, string expected) => _files.Clear(player, expected);
    }

    /// <summary>交易协议与恢复接缝。资产只由服务端快照更新，不在这里乐观扣款或发货。</summary>
    public sealed class CommerceClient : IDisposable
    {
        private readonly IBattleTransport _net;
        private readonly Func<object> _identity;
        private readonly ICommerceRecoveryStore _recovery;
        private readonly Dictionary<string, CommerceOrderView> _orders = new();
        // 只用于快照失效去重，不能代替资产收据；换玩家或连接必须重新观察终态。
        private readonly Dictionary<string, CommerceOrderView> _refreshedTerminalOrders = new();
        private readonly HashSet<string> _refreshedTerminalMarkets = new(StringComparer.Ordinal);
        private object _connection;
        private ulong _player;
        private int _generation;
        private bool _disposed;
        public bool Busy { get; private set; }
        public bool RequiresReconnect { get; private set; }
        public string Status { get; private set; } = "";
        public bool HasPendingWrite => _net.PlayerId != 0 && !string.IsNullOrEmpty(_recovery.Load(_net.PlayerId));
        public IReadOnlyDictionary<string, CommerceOrderView> Orders => _orders;
        public event Action Changed;
        public event Action<IMessage> Response;
        public event Action AssetRefreshRequired;

        public CommerceClient(IBattleTransport net, ICommerceRecoveryStore recovery, Func<object> identity = null)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
            _identity = identity ?? (() => net);
            _connection = _identity(); _player = net.PlayerId;
            _net.Disconnected += Disconnected;
        }

        public void ObserveConnection()
        {
            var current = _identity();
            if (ReferenceEquals(current, _connection) && _player == _net.PlayerId) return;
            bool changedConnection = !ReferenceEquals(current, _connection);
            // 同连接换角时在途零编号回包仍可能到达，必须等真正换连接才解除隔离。
            RequiresReconnect = changedConnection ? false : RequiresReconnect || Busy;
            _connection = current; _player = _net.PlayerId;
            ++_generation; Busy = false; _orders.Clear();
            _refreshedTerminalOrders.Clear(); _refreshedTerminalMarkets.Clear();
            Status = HasPendingWrite ? "有待确认交易，请恢复原请求。" : "";
            Changed?.Invoke();
        }

        private void Disconnected()
        {
            ++_generation; Busy = false; RequiresReconnect = true;
            Status = "连接已断开；交易记录已保留，重连后可恢复。";
            Changed?.Invoke();
        }

        private static string RequestId() => Guid.NewGuid().ToString("N");
        private static uint Tip(IMessage value)
        {
            var field = value.Descriptor.FindFieldByName("error_message");
            return field?.Accessor.GetValue(value) is TipInfoMessage tip ? tip.Id : 0;
        }

        private CommerceOrderView Cache(CommerceOrderView order)
        {
            if (order == null || string.IsNullOrEmpty(order.OrderId)) return order;
            if (_orders.TryGetValue(order.OrderId, out var before) && before.Version > order.Version) return before.Clone();
            _orders[order.OrderId] = order.Clone();
            return order;
        }

        private void Apply(IMessage response)
        {
            bool refreshAssets = false;
            // 缓存和UI事件必须看到同一版本；仅保护缓存仍会让按钮按旧合同重新出现。
            if (response is CommerceOrderResponse one)
            {
                one.Order = Cache(one.Order);
                ObserveTerminalOrder(one.Order);
                refreshAssets = one.Order != null;
            }
            else if (response is ListCommerceOrdersResponse list)
                for (int index = 0; index < list.Orders.Count; ++index)
                {
                    list.Orders[index] = Cache(list.Orders[index]);
                    refreshAssets |= ObserveTerminalOrder(list.Orders[index]);
                }
            else if (response is BrowseCommerceOffersResponse offers)
                foreach (var offer in offers.Offers) offer.Order = Cache(offer.Order);
            else if (response is MarketOrderResponse market)
            {
                ObserveTerminalMarket(market.Order);
                refreshAssets = market.Order != null;
            }
            else if (response is ListMarketOrdersResponse markets)
                foreach (var order in markets.Orders) refreshAssets |= ObserveTerminalMarket(order);
            else if (response is AuctionLotResponse lot) refreshAssets = lot.Lot != null;
            // 通知者读取权威背包与宠物列表，不推导“本次应增加多少”。
            if (refreshAssets) AssetRefreshRequired?.Invoke();
        }

        private bool ObserveTerminalOrder(CommerceOrderView order)
        {
            if (order == null || string.IsNullOrEmpty(order.OrderId)) return false;
            if (order.State != CommerceOrderState.Delivered && order.State != CommerceOrderState.Cancelled
                && order.State != CommerceOrderState.Refunded && order.State != CommerceOrderState.Settled) return false;
            if (_refreshedTerminalOrders.TryGetValue(order.OrderId, out var before)
                && before.State == order.State && before.Version >= order.Version) return false;
            _refreshedTerminalOrders[order.OrderId] = order.Clone();
            return true;
        }

        private bool ObserveTerminalMarket(MarketOrderView order) => order != null
            && !string.IsNullOrEmpty(order.MarketOrderId) && order.State == "done"
            && _refreshedTerminalMarkets.Add(order.MarketOrderId);

        private void Send<T>(uint method, IMessage request, MessageParser<T> parser, bool write, string replayRecord = null)
            where T : IMessage<T>
        {
            if (_disposed) return;
            ObserveConnection();
            bool replay = replayRecord != null;
            if (!_net.IsReady || _net.PlayerId == 0) { SetStatus("请进入角色后操作。"); return; }
            if (Busy) { SetStatus("正在处理，请稍候。"); return; }
            if (RequiresReconnect) { SetStatus("请求状态未确认，请重新连接后恢复。"); return; }
            if (write && HasPendingWrite && !replay) { SetStatus("请先恢复已有的待确认交易。"); return; }
            ulong player = _net.PlayerId;
            string savedRequest = replayRecord;
            if (write && !replay)
            {
                // 存原始protobuf，不再造第二份业务结构或重新生成幂等号。
                savedRequest = method.ToString(CultureInfo.InvariantCulture) + ":" + Convert.ToBase64String(request.ToByteArray());
                try { _recovery.Save(player, savedRequest); }
                catch (Exception) { SetStatus("交易请求未能安全保存，请检查本机存储后重试。"); return; }
            }
            if (write && replay && _recovery.Load(player) != savedRequest)
            { SetStatus("待确认记录已变化，请重新读取后恢复。"); return; }
            int generation = ++_generation;
            object connection = _connection;
            Busy = true; SetStatus(write ? "交易已提交，正在确认…" : "正在读取…");
            // 传输保证主线程回调；首个成功或错误先封口，迟到结果不得改恢复记录或已发布状态。
            bool completed = false;
            _net.Call(method, request, parser, response =>
            {
                if (completed || !Current(generation, player, connection)) return;
                completed = true;
                Busy = false;
                uint tip = Tip(response);
                // 故障和缺少合同的空成功都不能证明未提交，保留原请求继续幂等恢复。
                bool completeReply = response is CommerceOrderResponse order ? order.Order != null
                    : response is MarketOrderResponse market ? market.Order != null
                    : response is AuctionLotResponse lot && lot.Lot != null;
                bool uncertain = TipFaults.IsFault(tip) || (write && tip == 0 && !completeReply);
                if (write && !uncertain)
                {
                    try { _recovery.Clear(player, savedRequest); }
                    catch (Exception) { SetStatus("服务端已回复；本机恢复记录尚未清理，可安全重查原请求。"); return; }
                }
                if (uncertain) { SetStatus("服务端尚未确认最终结果，请恢复原请求。"); return; }
                Status = tip == 0 ? "已收到服务端确认。" : JubaozhaiClient.DescribeTip(tip);
                if (tip == 0 || completeReply) { Apply(response); Response?.Invoke(response); }
                Changed?.Invoke();
            }, error =>
            {
                if (completed || !Current(generation, player, connection)) return;
                completed = true;
                Busy = false;
                if (error == JubaozhaiClient.TimeoutError) RequiresReconnect = true;
                SetStatus(write ? "结果暂未确认；请恢复原请求，勿重复下单。" : "读取失败，请稍后重试。");
            });
        }

        private bool Current(int generation, ulong player, object connection) => !_disposed && generation == _generation
            && _net.IsReady && player == _net.PlayerId && ReferenceEquals(connection, _identity());
        public void SetStatus(string value) { Status = value; Changed?.Invoke(); }

        public void RetryPending()
        {
            if (_disposed || _net.PlayerId == 0) return;
            string saved = _recovery.Load(_net.PlayerId);
            int separator = saved.IndexOf(':');
            if (separator < 1 || !uint.TryParse(saved.Substring(0, separator), out uint method))
            { SetStatus(string.IsNullOrEmpty(saved) ? "没有可恢复的交易请求。" : "交易恢复记录无法读取，请保留记录并联系支持。"); return; }
            try
            {
                byte[] payload = Convert.FromBase64String(saved.Substring(separator + 1));
                switch (method)
                {
                    case MessageIds.JubaozhaiCreateOfficialOrder: Send(method, CreateOfficialOrderRequest.Parser.ParseFrom(payload), CommerceOrderResponse.Parser, true, saved); break;
                    case MessageIds.JubaozhaiCreatePlayerTrade: Send(method, CreatePlayerTradeRequest.Parser.ParseFrom(payload), CommerceOrderResponse.Parser, true, saved); break;
                    case MessageIds.JubaozhaiConfirmPlayerTrade: Send(method, ConfirmPlayerTradeRequest.Parser.ParseFrom(payload), CommerceOrderResponse.Parser, true, saved); break;
                    case MessageIds.JubaozhaiCancelCommerceOrder: Send(method, CancelCommerceOrderRequest.Parser.ParseFrom(payload), CommerceOrderResponse.Parser, true, saved); break;
                    case MessageIds.JubaozhaiCreateConsignment: Send(method, CreateConsignmentRequest.Parser.ParseFrom(payload), CommerceOrderResponse.Parser, true, saved); break;
                    case MessageIds.JubaozhaiBuyConsignment: Send(method, BuyConsignmentRequest.Parser.ParseFrom(payload), CommerceOrderResponse.Parser, true, saved); break;
                    case MessageIds.JubaozhaiCancelConsignment: Send(method, CancelConsignmentRequest.Parser.ParseFrom(payload), CommerceOrderResponse.Parser, true, saved); break;
                    case MessageIds.JubaozhaiPlaceMarketOrder: Send(method, PlaceMarketOrderRequest.Parser.ParseFrom(payload), MarketOrderResponse.Parser, true, saved); break;
                    case MessageIds.JubaozhaiCancelMarketOrder: Send(method, CancelMarketOrderRequest.Parser.ParseFrom(payload), MarketOrderResponse.Parser, true, saved); break;
                    case MessageIds.JubaozhaiCreateAuctionLot: Send(method, CreateAuctionLotRequest.Parser.ParseFrom(payload), AuctionLotResponse.Parser, true, saved); break;
                    case MessageIds.JubaozhaiPlaceAuctionBid: Send(method, PlaceAuctionBidRequest.Parser.ParseFrom(payload), AuctionLotResponse.Parser, true, saved); break;
                    case MessageIds.JubaozhaiCancelAuctionLot: Send(method, CancelAuctionLotRequest.Parser.ParseFrom(payload), AuctionLotResponse.Parser, true, saved); break;
                    default: SetStatus("交易恢复记录版本不支持，请保留记录并联系支持。"); break;
                }
            }
            catch (Exception) { SetStatus("交易恢复记录无法读取，请保留记录并联系支持。"); }
        }

        public void ListSkus(ulong after = 0) => Send(MessageIds.JubaozhaiListOfficialSkus, new ListOfficialSkusRequest { AfterSkuId = after, Limit = 8 }, ListOfficialSkusResponse.Parser, false);
        public void BrowseOffers(CommerceOrderKind kind, string cursor = "") => Send(MessageIds.JubaozhaiBrowseCommerceOffers, new BrowseCommerceOffersRequest { Kind = kind, Cursor = cursor, Limit = 8 }, BrowseCommerceOffersResponse.Parser, false);
        public void ListOrders(string cursor = "") => Send(MessageIds.JubaozhaiListCommerceOrders, new ListCommerceOrdersRequest { Cursor = cursor, Limit = 8 }, ListCommerceOrdersResponse.Parser, false);
        public void GetOrder(string id) => Send(MessageIds.JubaozhaiGetCommerceOrder, new GetCommerceOrderRequest { OrderId = id }, CommerceOrderResponse.Parser, false);
        public void CancelOrder(string id) => Send(MessageIds.JubaozhaiCancelCommerceOrder, new CancelCommerceOrderRequest { OrderId = id }, CommerceOrderResponse.Parser, true);
        public void BuySku(ulong sku, uint quantity) => Send(MessageIds.JubaozhaiCreateOfficialOrder, new CreateOfficialOrderRequest { ClientRequestId = RequestId(), SkuId = sku, Quantity = quantity }, CommerceOrderResponse.Parser, true);
        public void CreateTrade(ulong buyer, TradeAssetSelection seller, TradeAssetSelection wanted) => Send(MessageIds.JubaozhaiCreatePlayerTrade, new CreatePlayerTradeRequest { ClientRequestId = RequestId(), BuyerPlayerId = buyer, SellerAssets = seller.Clone(), BuyerAssets = wanted.Clone() }, CommerceOrderResponse.Parser, true);
        public void ConfirmTrade(string id) => Send(MessageIds.JubaozhaiConfirmPlayerTrade, new ConfirmPlayerTradeRequest { OrderId = id }, CommerceOrderResponse.Parser, true);
        public void Consign(TradeAssetSelection assets, uint currency, ulong price, ulong durationMs) => Send(MessageIds.JubaozhaiCreateConsignment, new CreateConsignmentRequest { ClientRequestId = RequestId(), Assets = assets.Clone(), CurrencyType = currency, Price = price, DurationMs = durationMs }, CommerceOrderResponse.Parser, true);
        public void BuyConsignment(string id) => Send(MessageIds.JubaozhaiBuyConsignment, new BuyConsignmentRequest { ListingId = id, ClientRequestId = RequestId() }, CommerceOrderResponse.Parser, true);
        public void CancelConsignment(string id) => Send(MessageIds.JubaozhaiCancelConsignment, new CancelConsignmentRequest { ListingId = id }, CommerceOrderResponse.Parser, true);
        public void PlaceMarket(uint market, uint config, MarketOrderSide side, uint quantity, ulong price, uint currency) => Send(MessageIds.JubaozhaiPlaceMarketOrder, new PlaceMarketOrderRequest { ClientRequestId = RequestId(), MarketId = market, ItemConfigId = config, Side = side, Quantity = quantity, UnitPrice = price, CurrencyType = currency }, MarketOrderResponse.Parser, true);
        public void CancelMarket(string id) => Send(MessageIds.JubaozhaiCancelMarketOrder, new CancelMarketOrderRequest { MarketOrderId = id }, MarketOrderResponse.Parser, true);
        public void ListMarket(string cursor = "") => Send(MessageIds.JubaozhaiListMarketOrders, new ListMarketOrdersRequest { Cursor = cursor, Limit = 8 }, ListMarketOrdersResponse.Parser, false);
        public void ListMarkets() => Send(MessageIds.JubaozhaiListTradeMarkets, new ListTradeMarketsRequest(), ListTradeMarketsResponse.Parser, false);
        public void MarketDepth(uint market, uint config) => Send(MessageIds.JubaozhaiGetMarketDepth, new GetMarketDepthRequest { MarketId = market, ItemConfigId = config, Limit = 8 }, GetMarketDepthResponse.Parser, false);
        public void CreateLot(TradeAssetSelection assets, uint currency, ulong opening, ulong increment, ulong durationMs) => Send(MessageIds.JubaozhaiCreateAuctionLot, new CreateAuctionLotRequest { ClientRequestId = RequestId(), Assets = assets.Clone(), CurrencyType = currency, OpeningPrice = opening, MinimumIncrement = increment, DurationMs = durationMs }, AuctionLotResponse.Parser, true);
        public void Bid(string id, ulong amount) => Send(MessageIds.JubaozhaiPlaceAuctionBid, new PlaceAuctionBidRequest { ClientRequestId = RequestId(), LotId = id, Amount = amount }, AuctionLotResponse.Parser, true);
        public void GetLot(string id) => Send(MessageIds.JubaozhaiGetAuctionLot, new GetAuctionLotRequest { LotId = id }, AuctionLotResponse.Parser, false);
        public void CancelLot(string id) => Send(MessageIds.JubaozhaiCancelAuctionLot, new CancelAuctionLotRequest { LotId = id }, AuctionLotResponse.Parser, true);
        public void Dispose() { if (_disposed) return; _disposed = true; ++_generation; _net.Disconnected -= Disconnected; }
    }
}
