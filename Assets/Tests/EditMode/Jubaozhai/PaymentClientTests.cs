using System;
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
    }
}
