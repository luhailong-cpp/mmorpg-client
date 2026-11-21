using System;
using System.Collections.Generic;
using MmorpgClient.Game.Jubaozhai;
using MmorpgClient.Net.Generated;
using NUnit.Framework;
using Paymentpb;

namespace MmorpgClient.Tests.EditMode.Jubaozhai
{
    public sealed class PaymentClientTests
    {
        private sealed class StoreBridge : PayBridge
        {
            public int Finishes;
            public override void Launch(CreatePayIntentResponse intent, Action<PayLaunch> done) => done(new PayLaunch { AppleTransactionId = "tx-1" });
            public override void Finish(PayLaunch purchase) => Finishes++;
            public bool HasRedelivery;
            public override bool TryTakeRedelivered(out string orderId, out PayLaunch purchase)
            {
                orderId = ""; purchase = new PayLaunch { AppleTransactionId = "tx-old" };
                bool had = HasRedelivery; HasRedelivery = false;
                return had;
            }
        }

        [Test]
        public void RedeliveredStoreTransactionIsSubmittedWhenIdleAndFinishedOnServerWord()
        {
            var net = new JubaozhaiFakeTransport();
            var bridge = new StoreBridge { HasRedelivery = true };
            using var client = new PaymentClient(net);
            client.Register(PayChannel.Apple, bridge);

            client.Tick(1);
            Assert.That(net.Calls[^1], Is.EqualTo(ClientPlayerPaymentRpc.SubmitPurchaseMessageId));
            var submitted = (SubmitPurchaseRequest)net.LastRequest;
            Assert.That(submitted.AppleTransactionId, Is.EqualTo("tx-old"));
            Assert.That(submitted.OrderId, Is.Empty, "补交的交易不带订单号,服务端按商店里的绑定认订单");
            net.Reply(new SubmitPurchaseResponse { FinishTransaction = true });
            Assert.That(bridge.Finishes, Is.EqualTo(1));
            Assert.That(client.PendingOrder, Is.Empty, "没有订单号就不轮询");
            client.Tick(2);
            Assert.That(net.Calls.Count, Is.EqualTo(1), "队列空了不再提交");
        }

        [Test]
        public void TwoStageChannelPollsServerAfterSdkReturnsAndSettlesOnlyOnServerState()
        {
            var net = new JubaozhaiFakeTransport();
            using var client = new PaymentClient(net);
            string settled = null;
            client.Settled += id => settled = id;

            client.Pay("order-1", PayChannel.Devpay);
            Assert.That(net.Calls[^1], Is.EqualTo(ClientPlayerPaymentRpc.CreatePayIntentMessageId));
            var request = (CreatePayIntentRequest)net.LastRequest;
            Assert.That(request.OrderId, Is.EqualTo("order-1"));
            Assert.That(request.Platform, Is.EqualTo(PayClientPlatform.Desktop));
            net.Reply(new CreatePayIntentResponse { Intent = new PayIntentView { State = PayIntentState.Created } });
            Assert.That(client.PendingOrder, Is.EqualTo("order-1"), "SDK 返回后只等服务端的结论");

            client.Tick(10);
            Assert.That(net.Calls[^1], Is.EqualTo(ClientPlayerPaymentRpc.GetPayIntentMessageId));
            net.Reply(new GetPayIntentResponse { Intent = new PayIntentView { State = PayIntentState.Created } });
            Assert.That(settled, Is.Null);
            client.Tick(10.5);
            Assert.That(net.Calls.Count, Is.EqualTo(2), "间隔没到不重复查");
            client.Pay("order-1", PayChannel.Devpay);
            Assert.That(net.Calls.Count, Is.EqualTo(2), "在途支付期间不再发起第二笔");

            client.Tick(12.1);
            net.Reply(new GetPayIntentResponse { Intent = new PayIntentView { State = PayIntentState.Paid } });
            Assert.That(settled, Is.EqualTo("order-1"));
            Assert.That(client.PendingOrder, Is.Empty);
            client.Tick(20);
            Assert.That(net.Calls.Count, Is.EqualTo(3), "有结局后停止轮询");
        }

        [Test]
        public void PollingStopsAfterTheWindowWithoutClaimingSuccess()
        {
            var net = new JubaozhaiFakeTransport();
            using var client = new PaymentClient(net);
            string settled = null;
            client.Settled += id => settled = id;
            client.Pay("order-4", PayChannel.Devpay);
            net.Reply(new CreatePayIntentResponse { Intent = new PayIntentView { State = PayIntentState.Created } });
            client.Tick(PaymentClient.PollWindowSeconds + 1);
            net.Reply(new GetPayIntentResponse { Intent = new PayIntentView { State = PayIntentState.Created } });
            Assert.That(client.PendingOrder, Is.Empty);
            Assert.That(settled, Is.Null);
            StringAssert.Contains("请勿重复支付", client.Status);
        }

        [Test]
        public void StoreReceiptGoesToServerAndTransactionFinishesOnlyWhenServerSaysSo()
        {
            var net = new JubaozhaiFakeTransport();
            var bridge = new StoreBridge();
            using var client = new PaymentClient(net);
            client.Register(PayChannel.Apple, bridge);

            client.Pay("order-2", PayChannel.Apple);
            net.Reply(new CreatePayIntentResponse { Intent = new PayIntentView { State = PayIntentState.Created }, Apple = new AppleIapParams { ProductId = "gem60" } });
            Assert.That(net.Calls[^1], Is.EqualTo(ClientPlayerPaymentRpc.SubmitPurchaseMessageId));
            var submitted = (SubmitPurchaseRequest)net.LastRequest;
            Assert.That(submitted.AppleTransactionId, Is.EqualTo("tx-1"));
            Assert.That(submitted.OrderId, Is.EqualTo("order-2"));
            Assert.That(bridge.Finishes, Is.Zero, "服务端确认之前不结束商店交易");

            net.Reply(new SubmitPurchaseResponse { Intent = new PayIntentView { State = PayIntentState.Paid }, FinishTransaction = true });
            Assert.That(bridge.Finishes, Is.EqualTo(1));
            Assert.That(client.PendingOrder, Is.Empty);
        }

        [Test]
        public void ChannelWithoutBridgeNeverReachesTheServer()
        {
            var net = new JubaozhaiFakeTransport();
            using var client = new PaymentClient(net);
            client.Pay("order-3", PayChannel.WechatPay);
            Assert.That(net.Calls, Is.Empty);
        }

        private sealed class AsyncStoreBridge : PayBridge
        {
            private readonly Queue<(string OrderId, PayLaunch Purchase)> _redelivered = new();
            private Action<PayLaunch> _done;
            private string _productId = "";
            public int CancelCalls;
            public int Finishes;
            public bool IgnoreCancel;

            public override void Launch(CreatePayIntentResponse intent, Action<PayLaunch> done)
            {
                _productId = intent.Apple?.ProductId ?? "";
                _done = done;
            }

            public override void CancelPending()
            {
                CancelCalls++;
                if (IgnoreCancel) return;
                _done = null;
                _productId = "";
            }

            public void SimulatePurchasePending(string productId, string orderId, PayLaunch purchase)
            {
                if (_done != null && productId == _productId)
                {
                    var done = _done;
                    _done = null;
                    _productId = "";
                    done(purchase);
                }
                else
                {
                    _redelivered.Enqueue((orderId, purchase));
                }
            }

            public override bool TryTakeRedelivered(out string orderId, out PayLaunch purchase)
            {
                if (_redelivered.Count == 0) { orderId = ""; purchase = default; return false; }
                (orderId, purchase) = _redelivered.Dequeue();
                return true;
            }

            public override void Finish(PayLaunch purchase) => Finishes++;
        }

        [Test]
        public void DisconnectDuringStorePaymentSheetCancelsPendingAndRedeliversReceiptOnReconnect()
        {
            var net = new JubaozhaiFakeTransport();
            var bridge = new AsyncStoreBridge();
            using var client = new PaymentClient(net);
            client.Register(PayChannel.Apple, bridge);
            string settled = null;
            client.Settled += id => settled = id;

            client.Pay("order-sheet", PayChannel.Apple);
            net.Reply(new CreatePayIntentResponse
            {
                Intent = new PayIntentView { State = PayIntentState.Created },
                Apple = new AppleIapParams { ProductId = "gem60" }
            });
            Assert.That(client.Busy, Is.True, "商店支付弹窗已打开");

            // 玩家在系统支付弹窗付款期间大厅断线:必须取消待返回回调,让后续购买凭证进入补交队列。
            net.Disconnect();
            Assert.That(client.Busy, Is.False);
            Assert.That(bridge.CancelCalls, Is.EqualTo(1));

            // 玩家完成系统扣款,商店推送未结束交易。
            bridge.SimulatePurchasePending("gem60", "order-sheet", new PayLaunch { AppleTransactionId = "tx-after-disconnect" });
            client.Tick(1);
            Assert.That(net.Calls.Count, Is.EqualTo(1), "尚未重连时不能提交也不得丢弃凭证");

            // 重连后 Tick 取出补交凭证并提交核验,直到服务端确认才 Finish。
            net.IsReady = true;
            client.Tick(2);
            Assert.That(net.Calls.Count, Is.EqualTo(2));
            Assert.That(net.Calls[^1], Is.EqualTo(ClientPlayerPaymentRpc.SubmitPurchaseMessageId));
            var submitted = (SubmitPurchaseRequest)net.LastRequest;
            Assert.That(submitted.OrderId, Is.EqualTo("order-sheet"));
            Assert.That(submitted.AppleTransactionId, Is.EqualTo("tx-after-disconnect"));
            Assert.That(bridge.Finishes, Is.Zero);

            net.Reply(new SubmitPurchaseResponse { Intent = new PayIntentView { State = PayIntentState.Paid }, FinishTransaction = true });
            Assert.That(bridge.Finishes, Is.EqualTo(1));
            Assert.That(settled, Is.EqualTo("order-sheet"));
            Assert.That(client.PendingOrder, Is.Empty);
        }

        [Test]
        public void LateStoreReceiptCallbackAfterDisconnectIsQueuedAndSubmittedOnReconnect()
        {
            var net = new JubaozhaiFakeTransport();
            var bridge = new AsyncStoreBridge { IgnoreCancel = true };
            using var client = new PaymentClient(net);
            client.Register(PayChannel.Apple, bridge);

            client.Pay("order-late-cb", PayChannel.Apple);
            net.Reply(new CreatePayIntentResponse
            {
                Intent = new PayIntentView { State = PayIntentState.Created },
                Apple = new AppleIapParams { ProductId = "gem60" }
            });
            net.Disconnect();

            // 即使渠道桥没有取消旧回调、在世代变化后直接调用 done,客户端也不能丢弃已付凭证。
            bridge.SimulatePurchasePending("gem60", "order-late-cb", new PayLaunch { AppleTransactionId = "tx-late-cb" });
            net.IsReady = true;
            client.Tick(2);

            Assert.That(net.Calls[^1], Is.EqualTo(ClientPlayerPaymentRpc.SubmitPurchaseMessageId));
            var submitted = (SubmitPurchaseRequest)net.LastRequest;
            Assert.That(submitted.OrderId, Is.EqualTo("order-late-cb"));
            Assert.That(submitted.AppleTransactionId, Is.EqualTo("tx-late-cb"));
            net.Reply(new SubmitPurchaseResponse { Intent = new PayIntentView { State = PayIntentState.Paid }, FinishTransaction = true });
            Assert.That(bridge.Finishes, Is.EqualTo(1));
        }

        [Test]
        public void ResetAndConnectionChangeStopPendingPollAndCancelActiveBridge()
        {
            object connection = new object();
            var net = new JubaozhaiFakeTransport();
            var bridge = new AsyncStoreBridge();
            using var client = new PaymentClient(net, () => connection);
            client.Register(PayChannel.Apple, bridge);

            client.Pay("order-poll", PayChannel.Devpay);
            net.Reply(new CreatePayIntentResponse { Intent = new PayIntentView { State = PayIntentState.Created } });
            Assert.That(client.PendingOrder, Is.EqualTo("order-poll"));

            client.Reset();
            Assert.That(client.PendingOrder, Is.Empty);
            Assert.That(client.Busy, Is.False);
            Assert.That(bridge.CancelCalls, Is.EqualTo(1));
            client.Tick(10);
            Assert.That(net.Calls.Count, Is.EqualTo(1), "Reset 后不再轮询上一角色的待付款订单");

            // 静默换 Gate 连接:ObserveConnection 自动中止旧连接上的在途轮询并取消待返回回调。
            client.Pay("order-silent", PayChannel.Devpay);
            net.Reply(new CreatePayIntentResponse { Intent = new PayIntentView { State = PayIntentState.Created } });
            Assert.That(client.PendingOrder, Is.EqualTo("order-silent"));

            connection = new object();
            client.ObserveConnection();
            Assert.That(client.PendingOrder, Is.Empty);
            Assert.That(bridge.CancelCalls, Is.EqualTo(2));
        }

        [Test]
        public void TransientSubmitFailureRequeuesReceiptWithBackoffAndFinishesOnRetry()
        {
            var net = new JubaozhaiFakeTransport();
            var bridge = new StoreBridge { HasRedelivery = true };
            using var client = new PaymentClient(net);
            client.Register(PayChannel.Apple, bridge);
            string settled = null;
            client.Settled += id => settled = id;

            // 第 1 次补交遇服务端繁忙(KPaymentBusy):不得丢弃凭证也不得 Finish,按 3s 退避重新入队。
            client.Tick(1);
            Assert.That(net.Calls[^1], Is.EqualTo(ClientPlayerPaymentRpc.SubmitPurchaseMessageId));
            net.Reply(new SubmitPurchaseResponse { ErrorMessage = new TipInfoMessage { Id = (uint)payment_error.KPaymentBusy } });
            Assert.That(bridge.Finishes, Is.Zero);
            client.Tick(3.5);
            Assert.That(net.Calls.Count, Is.EqualTo(1), "退避未到期不得重试");

            // 第 2 次重试遇网络超时:继续按 6s 退避重新入队。
            client.Tick(4.1);
            Assert.That(net.Calls.Count, Is.EqualTo(2));
            net.Error("timeout");
            Assert.That(bridge.Finishes, Is.Zero);
            client.Tick(9.5);
            Assert.That(net.Calls.Count, Is.EqualTo(2));

            // 第 3 次重试成功:服务端返回 FinishTransaction 且回填 OrderId,触发 Finish 与 Settled(order-recovered)。
            client.Tick(10.5);
            Assert.That(net.Calls.Count, Is.EqualTo(3));
            net.Reply(new SubmitPurchaseResponse
            {
                FinishTransaction = true,
                Intent = new PayIntentView { OrderId = "order-recovered", State = PayIntentState.Paid }
            });
            Assert.That(bridge.Finishes, Is.EqualTo(1));
            Assert.That(settled, Is.EqualTo("order-recovered"));
        }

        [Test]
        public void ResetDuringInflightSubmitPreservesReceiptForNextSession()
        {
            var net = new JubaozhaiFakeTransport();
            var bridge = new StoreBridge { HasRedelivery = true };
            using var client = new PaymentClient(net);
            client.Register(PayChannel.Apple, bridge);

            client.Tick(1);
            Assert.That(net.Calls.Count, Is.EqualTo(1));
            client.Reset();
            Assert.That(client.Busy, Is.False);
            Assert.That(bridge.Finishes, Is.Zero);

            client.Tick(2);
            Assert.That(net.Calls.Count, Is.EqualTo(2), "在途核验被 Reset 中断后凭证不得丢失");
            net.Reply(new SubmitPurchaseResponse { FinishTransaction = true });
            Assert.That(bridge.Finishes, Is.EqualTo(1));
        }

        [Test]
        public void ListChannelsDoesNotInterruptActivePaymentAndUsesCleanErrorCopy()
        {
            var net = new JubaozhaiFakeTransport();
            var bridge = new AsyncStoreBridge();
            using var client = new PaymentClient(net);
            client.Register(PayChannel.Apple, bridge);

            client.Pay("order-active", PayChannel.Apple);
            var createReply = net.Pending;
            Assert.That(client.Busy, Is.True);

            // 拉取支付方式使用独立世代,不取消在途的 CreatePayIntent。
            client.ListChannels();
            Assert.That(client.ChannelsLoading, Is.True);
            net.Error("network down");
            Assert.That(client.ChannelsLoading, Is.False);
            Assert.That(client.Status, Does.Not.Contain("请勿重复支付"));
            Assert.That(client.Busy, Is.True, "读取渠道失败不得打断在途支付");

            createReply(new CreatePayIntentResponse
            {
                Intent = new PayIntentView { State = PayIntentState.Created },
                Apple = new AppleIapParams { ProductId = "gem60" }
            });
            bridge.SimulatePurchasePending("gem60", "order-active", new PayLaunch { AppleTransactionId = "tx-keep" });
            Assert.That(net.Calls[^1], Is.EqualTo(ClientPlayerPaymentRpc.SubmitPurchaseMessageId));
        }

        [Test]
        public void CrossCharacterRedeliveryAndTerminalPollErrorsStopImmediately()
        {
            var net = new JubaozhaiFakeTransport();
            var bridge = new StoreBridge();
            using var client = new PaymentClient(net);
            client.Register(PayChannel.Apple, bridge);

            // 跨角色补交:带 orderId 但服务端返回 FinishTransaction=true 且 Intent=null,结束交易且不启动 90s 轮询。
            client.Submit("other-char-order", PayChannel.Apple, new PayLaunch { AppleTransactionId = "tx-cross" });
            net.Reply(new SubmitPurchaseResponse { FinishTransaction = true });
            Assert.That(bridge.Finishes, Is.EqualTo(1));
            Assert.That(client.PendingOrder, Is.Empty);

            // 终局错误(订单不存在)立即停止轮询,不等满 90 秒。
            client.Pay("order-missing", PayChannel.Devpay);
            net.Reply(new CreatePayIntentResponse { Intent = new PayIntentView { State = PayIntentState.Created } });
            client.Tick(1);
            net.Reply(new GetPayIntentResponse { ErrorMessage = new TipInfoMessage { Id = (uint)payment_error.KPaymentOrderNotFound } });
            Assert.That(client.PendingOrder, Is.Empty);

            // 手动停止等待。
            client.Pay("order-wait", PayChannel.Devpay);
            net.Reply(new CreatePayIntentResponse { Intent = new PayIntentView { State = PayIntentState.Created } });
            Assert.That(client.PendingOrder, Is.EqualTo("order-wait"));
            client.CancelWatching();
            Assert.That(client.PendingOrder, Is.Empty);
        }

        [Test]
        public void DescribeTipMapsAllThirteenPaymentErrors()
        {
            for (uint tip = 31000; tip <= 31012; tip++)
            {
                string copy = PaymentClient.DescribeTip(tip);
                Assert.That(copy, Is.Not.Null.And.Not.Empty, $"tip {tip} 必须有可读中文文案");
                Assert.That(copy, Does.Not.Contain(tip.ToString()), $"tip {tip} 不得退化为裸错误码");
            }
        }
    }
}
