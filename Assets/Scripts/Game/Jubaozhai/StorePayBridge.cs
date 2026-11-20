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

        private readonly PayChannel _channel;
        private readonly StoreController _store = new StoreController();
        // 还没结束的交易,键是交易号(苹果)或购买凭证(谷歌)。
        private readonly Dictionary<string, PendingOrder> _pending = new();
        private readonly Queue<(string OrderId, PayLaunch Purchase)> _redelivered = new();
        private Task _connected;
        private string _productId = "";
        private Action<PayLaunch> _done;

        private StorePayBridge(PayChannel channel)
        {
            _channel = channel;
            _store.OnProductsFetched += ProductsFetched;
            _store.OnProductsFetchFailed += _ => Complete(new PayLaunch { Error = "读取应用商店商品失败，请稍后再试。" });
            _store.OnPurchasePending += PurchasePending;
            _store.OnPurchaseFailed += order => Complete(new PayLaunch
            { Error = order.FailureReason == PurchaseFailureReason.UserCancelled ? "已取消支付。" : "应用商店没有完成这笔购买。" });
            _store.OnPurchaseDeferred += _ => Complete(new PayLaunch { Error = "这笔购买在等待批准，批准后会自动到账。" });
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
            try
            {
                await Connected();
                if (_channel == PayChannel.Apple)
                {
                    if (!Guid.TryParse(intent.Apple.AppAccountToken, out var token)) { done(new PayLaunch { Error = "支付参数缺失，请重试。" }); return; }
                    _store.AppleStoreExtendedService?.SetAppAccountToken(token);
                }
                else
                {
                    _store.GooglePlayStoreExtendedService?.SetObfuscatedAccountId(intent.Google.ObfuscatedAccountId);
                    _store.GooglePlayStoreExtendedService?.SetObfuscatedProfileId(intent.Google.ObfuscatedProfileId);
                }
            }
            catch (Exception) { done(new PayLaunch { Error = "连接应用商店失败，请稍后再试。" }); return; }
            _productId = productId; _done = done;
            if (_store.GetProductById(productId) != null) _store.PurchaseProduct(productId);
            else _store.FetchProducts(new List<ProductDefinition> { new ProductDefinition(productId, ProductType.Consumable) });
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
            if (_done == null) return;
            if (_store.GetProductById(_productId) == null) Complete(new PayLaunch { Error = "应用商店里没有这件商品。" });
            else _store.PurchaseProduct(_productId);
        }

        // 买到了(或商店补交):正在等的那件商品交给 Launch 的调用方,其余排队等 PaymentClient 提交。
        private void PurchasePending(PendingOrder order)
        {
            var purchase = new PayLaunch();
            string orderId, productId = "";
            var items = order.CartOrdered.Items();
            if (items.Count > 0) productId = items[0].Product.definition.id;
            if (_channel == PayChannel.Apple)
            {
                purchase.AppleTransactionId = order.Info.TransactionID ?? "";
                orderId = order.Info.Apple?.AppAccountToken?.ToString() ?? "";
            }
            else
            {
                purchase.GooglePurchaseToken = GooglePurchaseToken(order.Info);
                purchase.GoogleProductId = productId;
                orderId = order.Info.Google?.ObfuscatedProfileId ?? "";
            }
            string key = Key(purchase);
            if (key.Length == 0) return; // 没有交易号也没有凭证,交不了;交易没结束,商店下次连接会再交
            _pending[key] = order;
            if (_done != null && productId == _productId) Complete(purchase);
            else _redelivered.Enqueue((orderId, purchase));
        }

        private void Complete(PayLaunch result)
        {
            var done = _done;
            _done = null; _productId = "";
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
