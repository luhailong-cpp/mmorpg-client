#if MMORPG_UNITY_IAP
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Paymentpb;
using UnityEngine;
using UnityEngine.Purchasing;

namespace MmorpgClient.Game.Jubaozhai
{
    /// <summary>
    /// App Store 与 Google Play 的支付桥(Unity IAP 5:StoreKit 2 / Play Billing)。接口按 com.unity.purchasing 5.0.4 的官方文档写。
    /// 购买前把服务端给的订单标记交给商店(苹果 appAccountToken、谷歌混淆账号),买到之后只把交易号或购买凭证交回去,由服务端向商店核验。
    /// 服务端确认记账之前不结束交易(Finish 才 ConfirmPurchase);进程被杀后,商店在下次连接时把没结束的交易再交一遍,
    /// 这些补交的交易排在 TryTakeRedelivered 里,由 PaymentClient 空闲时逐笔提交。
    /// 整个进程只有一个实例:商店连接本来就是进程级的,每个游戏会话各建一个会让旧实例继续收到购买事件。
    /// </summary>
    public sealed class StorePayBridge : PayBridge
    {
        private static StorePayBridge _shared;
        public static StorePayBridge Shared(PayChannel channel) => _shared ??= new StorePayBridge(channel);

        private static readonly IRetryPolicy ProductFetchRetry = new MaximumNumberOfAttemptsRetryPolicy(2);

        private readonly PayChannel _channel;
        private readonly StoreController _store = new StoreController();
        // 还没结束的交易,键是交易号(苹果)或购买凭证(谷歌)。
        private readonly Dictionary<string, PendingOrder> _pending = new();
        private readonly Queue<(string OrderId, PayLaunch Purchase)> _redelivered = new();
        private Task _connected;
        private string _productId = "", _expectedOrderId = "";
        private Action<PayLaunch> _done;
        private int _launchGeneration, _fetchGeneration;

        private StorePayBridge(PayChannel channel)
        {
            _channel = channel;
            _store.SetStoreReconnectionRetryPolicyOnDisconnection(new MaximumNumberOfAttemptsRetryPolicy(2));
            _store.OnStoreDisconnected += StoreDisconnected;
            _store.OnProductsFetched += ProductsFetched;
            _store.OnProductsFetchFailed += ProductsFetchFailed;
            _store.OnPurchasesFetched += PurchasesFetched;
            _store.OnPurchasePending += PurchasePending;
            _store.OnPurchaseFailed += order => Complete(new PayLaunch
            { Error = order.FailureReason == PurchaseFailureReason.UserCancelled ? "已取消支付。" : "应用商店没有完成这笔购买。" });
            _store.OnPurchaseDeferred += _ => Complete(new PayLaunch { Error = "这笔购买在等待批准，批准后会自动到账。" });
        }

        private void StoreDisconnected(StoreConnectionFailureDescription _)
        {
            _connected = null;
            Complete(new PayLaunch { Error = "应用商店连接已断开，请稍后再试。" });
        }

        // iOS StoreKit 2 启动补单只经 OnPurchasesFetched(orders.PendingOrders) 返回;
        // Google Play 两边都可能触发,由 PurchasePending 按交易键去重。
        private void PurchasesFetched(Orders orders)
        {
            if (orders?.PendingOrders == null) return;
            foreach (var pending in orders.PendingOrders) PurchasePending(pending);
        }

        // 连接一次;失败了下次再连。连上后让商店把没结束的交易再交一遍(OnPurchasePending)。
        private Task Connected()
        {
            if (_connected == null || _connected.IsFaulted || _connected.IsCanceled) _connected = Connect();
            return _connected;
        }

        private async Task Connect()
        {
            await _store.Connect();
            _store.FetchPurchases();
        }

        public override async void Prepare(Action<string> loginCode)
        {
            try { await Connected(); }
            catch (Exception) { /* Launch 会再连一次,并把失败告诉玩家 */ }
            loginCode("");
        }

        public override async void Launch(CreatePayIntentResponse intent, Action<PayLaunch> done)
        {
            string productId = (_channel == PayChannel.Apple ? intent.Apple?.ProductId : intent.Google?.ProductId) ?? "";
            if (productId.Length == 0) { done(new PayLaunch { Error = "支付参数缺失，请重试。" }); return; }
            int launch = ++_launchGeneration;
            string expectedOrderId;
            try
            {
                await Connected();
                if (launch != _launchGeneration) return;
                if (_channel == PayChannel.Apple)
                {
                    if (_store.AppleStoreExtendedService == null || !Guid.TryParse(intent.Apple?.AppAccountToken, out var token))
                    { done(new PayLaunch { Error = "支付参数或商店服务缺失，请重试。" }); return; }
                    _store.AppleStoreExtendedService.SetAppAccountToken(token);
                    expectedOrderId = token.ToString("D");
                }
                else
                {
                    string acct = intent.Google?.ObfuscatedAccountId ?? "";
                    string prof = intent.Google?.ObfuscatedProfileId ?? "";
                    if (_store.GooglePlayStoreExtendedService == null || acct.Length == 0 || prof.Length == 0)
                    { done(new PayLaunch { Error = "支付参数或商店服务缺失，请重试。" }); return; }
                    _store.GooglePlayStoreExtendedService.SetObfuscatedAccountId(acct);
                    _store.GooglePlayStoreExtendedService.SetObfuscatedProfileId(prof);
                    expectedOrderId = prof;
                }
            }
            catch (Exception)
            {
                if (launch == _launchGeneration) done(new PayLaunch { Error = "连接应用商店失败，请稍后再试。" });
                return;
            }
            if (launch != _launchGeneration) return;
            _productId = productId; _expectedOrderId = expectedOrderId; _done = done; _fetchGeneration = launch;
            if (_store.GetProductById(productId) != null) _store.PurchaseProduct(productId);
            else _store.FetchProducts(new List<ProductDefinition> { new ProductDefinition(productId, ProductType.Consumable) }, ProductFetchRetry);
        }

        public override void CancelPending()
        {
            ++_launchGeneration;
            _done = null;
            _productId = "";
            _expectedOrderId = "";
        }

        public override void Finish(PayLaunch purchase)
        {
            if (_pending.Remove(Key(purchase), out var order)) _store.ConfirmPurchase(order);
        }

        public override bool TryTakeRedelivered(out string orderId, out PayLaunch purchase)
        {
            if (_redelivered.Count == 0) { orderId = ""; purchase = default; return false; }
            (orderId, purchase) = _redelivered.Dequeue();
            return true;
        }

        private void ProductsFetched(List<Product> products)
        {
            if (_done == null || _fetchGeneration != _launchGeneration) return;
            if (_store.GetProductById(_productId) == null) Complete(new PayLaunch { Error = "应用商店里没有这件商品。" });
            else _store.PurchaseProduct(_productId);
        }

        private void ProductsFetchFailed(ProductFetchFailed _)
        {
            if (_done == null || _fetchGeneration != _launchGeneration) return;
            Complete(new PayLaunch { Error = "读取应用商店商品失败，请稍后再试。" });
        }

        // 买到了(或商店补交):与当前拉起的商品与订单标记匹配的交给 Launch 调用方,其余排队等 PaymentClient 补交。
        private void PurchasePending(PendingOrder order)
        {
            var purchase = new PayLaunch();
            string orderId, productId = "";
            var items = order.CartOrdered.Items();
            if (items.Count > 0) productId = items[0].Product.definition.id;
            if (_channel == PayChannel.Apple)
            {
                purchase.AppleTransactionId = order.Info.TransactionID ?? "";
                orderId = order.Info.Apple?.AppAccountToken is Guid token ? token.ToString("D") : "";
            }
            else
            {
                purchase.GooglePurchaseToken = GooglePurchaseToken(order.Info);
                purchase.GoogleProductId = productId;
                orderId = order.Info.Google?.ObfuscatedProfileId ?? "";
            }
            string key = Key(purchase);
            if (key.Length == 0) return; // 没有交易号也没有凭证,交不了;交易没结束,商店下次连接会再交
            bool alreadyTracked = _pending.ContainsKey(key);
            _pending[key] = order;
            bool orderMatches = string.IsNullOrEmpty(orderId) || string.IsNullOrEmpty(_expectedOrderId)
                || string.Equals(orderId, _expectedOrderId, StringComparison.OrdinalIgnoreCase);
            if (_done != null && productId == _productId && orderMatches) Complete(purchase);
            else if (!alreadyTracked) _redelivered.Enqueue((orderId, purchase));
        }

        private void Complete(PayLaunch result)
        {
            var done = _done;
            _done = null; _productId = ""; _expectedOrderId = "";
            done?.Invoke(result);
        }

        private static string Key(PayLaunch purchase) =>
            !string.IsNullOrEmpty(purchase.AppleTransactionId) ? purchase.AppleTransactionId : purchase.GooglePurchaseToken ?? "";

        // Unity 的统一收据:{"Payload":"{\"json\":\"<Google 的购买数据,含 purchaseToken>\",…}"}。解不出来时退回交易号(谷歌商店上它就是购买凭证)。
        [Serializable] private sealed class UnifiedReceipt { public string Payload; }
        [Serializable] private sealed class GooglePayload { public string json; }
        [Serializable] private sealed class GooglePurchaseData { public string purchaseToken; }

        private static string GooglePurchaseToken(IOrderInfo info)
        {
            try
            {
                var payload = JsonUtility.FromJson<GooglePayload>(JsonUtility.FromJson<UnifiedReceipt>(info.Receipt).Payload);
                string token = JsonUtility.FromJson<GooglePurchaseData>(payload.json).purchaseToken;
                if (!string.IsNullOrEmpty(token)) return token;
            }
            catch (Exception) { /* 收据形状不认得,用交易号 */ }
            return info.TransactionID ?? "";
        }
    }
}
#endif
