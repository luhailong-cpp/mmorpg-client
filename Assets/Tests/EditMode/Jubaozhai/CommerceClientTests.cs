using System;
using System.Collections.Generic;
using System.IO;
using Google.Protobuf;
using MmorpgClient.Game.Jubaozhai;
using NUnit.Framework;
using Trade;

namespace MmorpgClient.Tests.EditMode.Jubaozhai
{
    public sealed class CommerceClientTests
    {
        private sealed class MemoryRecovery : ICommerceRecoveryStore
        {
            public readonly Dictionary<ulong, string> Saved = new();
            public bool RefuseSave;
            public string Load(ulong player) => Saved.TryGetValue(player, out var data) ? data : "";
            public void Save(ulong player, string value) { if (RefuseSave) throw new InvalidOperationException(); Saved[player] = value; }
            public void Clear(ulong player, string expected)
            {
                if (Saved.TryGetValue(player, out var current) && current != expected) throw new InvalidOperationException();
                Saved.Remove(player);
            }
        }

        [Test]
        public void TimeoutThenNewClientReplaysIdenticalRequestAndIdBeforeClearing()
        {
            var net = new JubaozhaiFakeTransport(); var store = new MemoryRecovery();
            using (var first = new CommerceClient(net, store))
            {
                first.BuySku(12, 2);
                var original = net.LastRequest.ToByteArray();
                Assert.That(store.Load(1), Is.Not.Empty);
                net.Error(JubaozhaiClient.TimeoutError);
                Assert.That(first.RequiresReconnect, Is.True);
                first.RetryPending();
                Assert.That(net.Calls.Count, Is.EqualTo(1));
                using var reconnected = new CommerceClient(net, store);
                reconnected.RetryPending();
                Assert.That(net.LastRequest.ToByteArray(), Is.EqualTo(original));
                net.Reply(new CommerceOrderResponse { Order = new CommerceOrderView { OrderId = "12", Version = 1, State = CommerceOrderState.AwaitingPayment } });
                Assert.That(store.Load(1), Is.Empty);
                Assert.That(reconnected.Orders["12"].State, Is.EqualTo(CommerceOrderState.AwaitingPayment));
            }
        }

        [Test]
        public void RecoveryStorageFailurePreventsAnyNetworkWrite()
        {
            var net = new JubaozhaiFakeTransport(); var store = new MemoryRecovery { RefuseSave = true };
            using var client = new CommerceClient(net, store);
            client.Consign(new TradeAssetSelection { PetId = 123 }, 0, 42, 3600000);
            Assert.That(net.Calls, Is.Empty);
            Assert.That(client.Busy, Is.False);
        }

        [Test]
        public void OutstandingWriteBlocksAnotherPurchaseButReadDoesNotEraseRecovery()
        {
            var net = new JubaozhaiFakeTransport(); var store = new MemoryRecovery();
            using var client = new CommerceClient(net, store);
            client.BuyConsignment("listing"); net.Error("connection unavailable");
            string pending = store.Load(1);
            client.BuySku(45, 1);
            Assert.That(net.Calls.Count, Is.EqualTo(1));
            client.ListOrders(); net.Reply(new ListCommerceOrdersResponse());
            Assert.That(store.Load(1), Is.EqualTo(pending));
        }

        [Test]
        public void FaultOrEmptySuccessDoesNotLoseUnknownCommittedOrder()
        {
            var net = new JubaozhaiFakeTransport(); var store = new MemoryRecovery();
            using var client = new CommerceClient(net, store);
            int refreshes = 0; client.AssetRefreshRequired += () => refreshes++;
            client.BuySku(12, 1);
            net.Reply(new CommerceOrderResponse { ErrorMessage = new TipInfoMessage { Id = (uint)common_error.KServiceUnavailable } });
            Assert.That(client.HasPendingWrite, Is.True);
            client.RetryPending(); net.Reply(new CommerceOrderResponse());
            Assert.That(client.HasPendingWrite, Is.True);
            Assert.That(refreshes, Is.Zero);
        }

        [Test]
        public void SwitchingPlayerRejectsOldReplyAndNeverReplaysOtherPlayersWrite()
        {
            var net = new JubaozhaiFakeTransport(); var store = new MemoryRecovery(); object connection = new();
            using var client = new CommerceClient(net, store, () => connection);
            client.BuySku(12, 1); var oldReply = net.Pending;
            net.PlayerId = 2; connection = new object(); client.ObserveConnection();
            client.RetryPending();
            Assert.That(net.Calls.Count, Is.EqualTo(1));
            oldReply(new CommerceOrderResponse { Order = new CommerceOrderView { OrderId = "old", Version = 99 } });
            Assert.That(client.Orders, Is.Empty);
            Assert.That(store.Load(1), Is.Not.Empty);
        }

        [Test]
        public void ContractCacheDoesNotRegressAcrossOlderVersions()
        {
            var net = new JubaozhaiFakeTransport();
            using var client = new CommerceClient(net, new MemoryRecovery());
            CommerceOrderView rendered = null;
            client.Response += value => { if (value is CommerceOrderResponse reply) rendered = reply.Order; };
            client.GetOrder("order"); net.Reply(new CommerceOrderResponse { Order = new CommerceOrderView { OrderId = "order", Version = 9, State = CommerceOrderState.Delivered } });
            client.GetOrder("order"); net.Reply(new CommerceOrderResponse { Order = new CommerceOrderView { OrderId = "order", Version = 8, State = CommerceOrderState.Delivering } });
            Assert.That(client.Orders["order"].State, Is.EqualTo(CommerceOrderState.Delivered));
            Assert.That(rendered.State, Is.EqualTo(CommerceOrderState.Delivered), "UI事件也不能回退到旧合同");
        }

        [Test]
        public void LateReceiptCannotEraseAnotherPendingRequest()
        {
            var net = new JubaozhaiFakeTransport(); var store = new MemoryRecovery();
            using var client = new CommerceClient(net, store);
            client.BuySku(12, 1);
            const string another = "123:BAUG";
            store.Saved[1] = another;
            net.Reply(new CommerceOrderResponse { Order = new CommerceOrderView { OrderId = "old", Version = 1 } });
            Assert.That(store.Load(1), Is.EqualTo(another), "只能按原待确认记录清理，不能清掉另一实例的新请求");
        }

        [Test]
        public void InstanceSelectionUsesGeneratedProtoAndIsNotMutatedAfterSend()
        {
            var net = new JubaozhaiFakeTransport(); var store = new MemoryRecovery();
            using var client = new CommerceClient(net, store);
            var selection = new TradeAssetSelection(); selection.ItemUuids.Add(ulong.MaxValue);
            client.CreateLot(selection, 0, ulong.MaxValue - 2, 1, 3600000);
            selection.ItemUuids.Clear();
            var sent = (CreateAuctionLotRequest)net.LastRequest;
            Assert.That(sent.Assets.ItemUuids[0], Is.EqualTo(ulong.MaxValue));
            Assert.That(sent.OpeningPrice, Is.EqualTo(ulong.MaxValue - 2));
        }

        [Test]
        public void CompletedOrderListCoalescesAssetRefreshAndDoesNotRepeatForSameOrOlderContracts()
        {
            var net = new JubaozhaiFakeTransport();
            using var client = new CommerceClient(net, new MemoryRecovery());
            int refreshes = 0; client.AssetRefreshRequired += () => refreshes++;
            client.ListOrders(); net.Reply(new ListCommerceOrdersResponse { Orders = {
                new CommerceOrderView { OrderId = "one", Version = 8, State = CommerceOrderState.Delivering } } });
            Assert.That(refreshes, Is.Zero);
            client.ListOrders(); net.Reply(new ListCommerceOrdersResponse { Orders = {
                new CommerceOrderView { OrderId = "one", Version = 9, State = CommerceOrderState.Delivered },
                new CommerceOrderView { OrderId = "two", Version = 2, State = CommerceOrderState.Cancelled } } });
            Assert.That(refreshes, Is.EqualTo(1), "一页多个终态合并成一次权威资产读取");
            client.ListOrders(); net.Reply(new ListCommerceOrdersResponse { Orders = {
                new CommerceOrderView { OrderId = "one", Version = 9, State = CommerceOrderState.Delivered },
                new CommerceOrderView { OrderId = "two", Version = 2, State = CommerceOrderState.Cancelled } } });
            client.ListOrders(); net.Reply(new ListCommerceOrdersResponse { Orders = {
                new CommerceOrderView { OrderId = "one", Version = 8, State = CommerceOrderState.Delivering } } });
            Assert.That(refreshes, Is.EqualTo(1));Assert.That(net.Calls.Count, Is.EqualTo(4));
            Assert.That(client.Orders["one"].State, Is.EqualTo(CommerceOrderState.Delivered));
        }

        [TestCase(CommerceOrderState.Delivered)]
        [TestCase(CommerceOrderState.Cancelled)]
        [TestCase(CommerceOrderState.Refunded)]
        [TestCase(CommerceOrderState.Settled)]
        public void FirstTerminalOrderAndLaterVersionInvalidateAssets(CommerceOrderState state)
        {
            var net = new JubaozhaiFakeTransport();
            using var client = new CommerceClient(net, new MemoryRecovery());
            int refreshes = 0; client.AssetRefreshRequired += () => refreshes++;
            client.ListOrders(); net.Reply(new ListCommerceOrdersResponse { Orders = {
                new CommerceOrderView { OrderId = "one", Version = 2, State = state } } });
            client.ListOrders(); net.Reply(new ListCommerceOrdersResponse { Orders = {
                new CommerceOrderView { OrderId = "one", Version = 3, State = state } } });
            Assert.That(refreshes, Is.EqualTo(2));
        }

        [Test]
        public void MarketListWaitsForDoneAndRepeatedPageDoesNotLoopAssetReads()
        {
            var net = new JubaozhaiFakeTransport();
            using var client = new CommerceClient(net, new MemoryRecovery());
            int refreshes = 0; client.AssetRefreshRequired += () => refreshes++;
            client.ListMarket(); net.Reply(new ListMarketOrdersResponse { Orders = {
                new MarketOrderView { MarketOrderId = "one", State = "market_wait", Quantity = 10, FilledQuantity = 10 },
                new MarketOrderView { MarketOrderId = "two", State = "manual" } } });
            Assert.That(refreshes, Is.Zero, "已撮合数量不能代替子交付完成");
            client.ListMarket(); net.Reply(new ListMarketOrdersResponse { Orders = {
                new MarketOrderView { MarketOrderId = "one", State = "done" },
                new MarketOrderView { MarketOrderId = "two", State = "done" } } });
            Assert.That(refreshes, Is.EqualTo(1));
            client.ListMarket(); net.Reply(new ListMarketOrdersResponse { Orders = {
                new MarketOrderView { MarketOrderId = "two", State = "done" },
                new MarketOrderView { MarketOrderId = "one", State = "done" } } });
            Assert.That(refreshes, Is.EqualTo(1));Assert.That(net.Calls.Count, Is.EqualTo(3));
        }

        [Test]
        public void SingleOrderConfirmationAndFollowingListShareInvalidationDeduplication()
        {
            var net = new JubaozhaiFakeTransport();
            using var client = new CommerceClient(net, new MemoryRecovery());
            int refreshes = 0; client.AssetRefreshRequired += () => refreshes++;
            var order = new CommerceOrderView { OrderId = "one", Version = 2, State = CommerceOrderState.Delivered };
            client.GetOrder("one"); net.Reply(new CommerceOrderResponse { Order = order.Clone() });
            client.ListOrders(); net.Reply(new ListCommerceOrdersResponse { Orders = { order.Clone() } });
            Assert.That(refreshes, Is.EqualTo(1));
        }

        [Test]
        public void NewConnectionOrPlayerRechecksTerminalAssetsAndOldReplyCannotInvalidateThem()
        {
            var net = new JubaozhaiFakeTransport(); object connection = new();
            using var client = new CommerceClient(net, new MemoryRecovery(), () => connection);
            int refreshes = 0; client.AssetRefreshRequired += () => refreshes++;
            var order = new CommerceOrderView { OrderId = "one", Version = 2, State = CommerceOrderState.Delivered };
            client.ListOrders(); net.Reply(new ListCommerceOrdersResponse { Orders = { order.Clone() } });
            client.ListOrders(); var late = net.Pending;
            connection = new object(); client.ObserveConnection();
            late(new ListCommerceOrdersResponse { Orders = { order.Clone() } });
            Assert.That(refreshes, Is.EqualTo(1));
            client.ListOrders(); net.Reply(new ListCommerceOrdersResponse { Orders = { order.Clone() } });
            Assert.That(refreshes, Is.EqualTo(2));
            net.PlayerId = 2; client.ObserveConnection();
            client.ListOrders(); net.Reply(new ListCommerceOrdersResponse { Orders = { order.Clone() } });
            Assert.That(refreshes, Is.EqualTo(3));
            client.ListOrders(); late = net.Pending; client.Dispose();
            late(new ListCommerceOrdersResponse { Orders = {
                new CommerceOrderView { OrderId = "two", Version = 3, State = CommerceOrderState.Delivered } } });
            Assert.That(refreshes, Is.EqualTo(3));
        }

        [Test]
        public void TimedOutWriteCannotBeCompletedByLateSuccessOnSameConnection()
        {
            var net = new JubaozhaiFakeTransport(); var store = new MemoryRecovery();
            using var client = new CommerceClient(net, store);
            int refreshes = 0; client.AssetRefreshRequired += () => refreshes++;
            client.BuySku(12, 1); var late = net.Pending;
            string original = store.Load(1);
            net.Error(JubaozhaiClient.TimeoutError); string status = client.Status;
            late(new CommerceOrderResponse { Order = new CommerceOrderView { OrderId = "old", Version = 99, State = CommerceOrderState.Delivered } });
            Assert.That(store.Load(1), Is.EqualTo(original), "超时已封口，迟到成功只能由重连后的原请求恢复核实");
            Assert.That(client.RequiresReconnect, Is.True); Assert.That(client.Status, Is.EqualTo(status));
            Assert.That(client.Orders, Is.Empty); Assert.That(refreshes, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompletedWriteIgnoresDuplicateSuccessAndLateTimeout(bool lateError)
        {
            var net = new JubaozhaiFakeTransport(); var store = new MemoryRecovery();
            using var client = new CommerceClient(net, store);
            int refreshes = 0, responses = 0;
            client.AssetRefreshRequired += () => refreshes++; client.Response += _ => responses++;
            client.BuySku(12, 1); var reply = net.Pending; var error = net.Error;
            var accepted = new CommerceOrderResponse { Order = new CommerceOrderView { OrderId = "one", Version = 2, State = CommerceOrderState.Delivered } };
            reply(accepted); string status = client.Status;
            if (lateError) error(JubaozhaiClient.TimeoutError); else reply(accepted.Clone());
            Assert.That(client.RequiresReconnect, Is.False); Assert.That(client.Status, Is.EqualTo(status));
            Assert.That(store.Load(1), Is.Empty); Assert.That(responses, Is.EqualTo(1)); Assert.That(refreshes, Is.EqualTo(1));
        }

        [Test]
        public void ReconnectedOwnerReplaysOriginalWriteAndOldOwnerCannotCompleteCurrentRequest()
        {
            var net = new JubaozhaiFakeTransport(); var store = new MemoryRecovery(); object connection = new();
            using var client = new CommerceClient(net, store, () => connection);
            client.BuySku(12, 2); var oldReply = net.Pending; var oldError = net.Error;
            byte[] original = net.LastRequest.ToByteArray(); string saved = store.Load(1);
            net.Disconnect(); net.IsReady = true; connection = new object(); client.ObserveConnection();
            client.RetryPending(); var currentReply = net.Pending;
            Assert.That(net.LastRequest.ToByteArray(), Is.EqualTo(original));
            oldReply(new CommerceOrderResponse { Order = new CommerceOrderView { OrderId = "old", Version = 99, State = CommerceOrderState.Delivered } });
            oldError(JubaozhaiClient.TimeoutError);
            Assert.That(client.Busy, Is.True); Assert.That(client.RequiresReconnect, Is.False);
            Assert.That(store.Load(1), Is.EqualTo(saved)); Assert.That(client.Orders, Is.Empty);
            currentReply(new CommerceOrderResponse { Order = new CommerceOrderView { OrderId = "current", Version = 2, State = CommerceOrderState.Delivering } });
            Assert.That(store.Load(1), Is.Empty); Assert.That(client.Orders["current"].State, Is.EqualTo(CommerceOrderState.Delivering));
            client.ListOrders(); var delayedSqlList = net.Pending;
            oldReply(new CommerceOrderResponse { Order = new CommerceOrderView { OrderId = "old", Version = 99, State = CommerceOrderState.Delivered } });
            Assert.That(client.Busy, Is.True); Assert.That(client.Orders.ContainsKey("old"), Is.False);
            delayedSqlList(new ListCommerceOrdersResponse { Orders = { new CommerceOrderView { OrderId = "current", Version = 3, State = CommerceOrderState.Delivered } } });
            Assert.That(client.Orders["current"].Version, Is.EqualTo(3)); Assert.That(client.Busy, Is.False);
        }
    }

    public sealed class CommerceRecoveryFileStoreTests
    {
        private string _directory;
        [SetUp] public void SetUp() => _directory = Path.Combine(Path.GetTempPath(), "commerce-recovery-test-" + Guid.NewGuid().ToString("N"));
        [TearDown] public void TearDown()
        {
            if (!Directory.Exists(_directory)) return;
            Assert.That(Path.GetDirectoryName(Path.GetFullPath(_directory)), Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)));
            foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
            Directory.Delete(_directory);
        }
        [Test] public void ReopenRecoversExactOriginalRequestAndClearIsPlayerScoped()
        {
            var first = new CommerceFileRecoveryStore(_directory);
            first.Save(1, "123:AAEC"); first.Save(2, "456:BAUG");
            var reopened = new CommerceFileRecoveryStore(_directory);
            Assert.That(reopened.Load(1), Is.EqualTo("123:AAEC"));
            reopened.Clear(1, "123:AAEC");
            Assert.That(reopened.Load(1), Is.Empty);
            Assert.That(reopened.Load(2), Is.EqualTo("456:BAUG"));
        }
        [Test] public void SecondProcessCannotOverwriteAnotherUnknownRequest()
        {
            var first = new CommerceFileRecoveryStore(_directory); first.Save(1, "123:AAEC");
            var second = new CommerceFileRecoveryStore(_directory);
            Assert.Throws<InvalidOperationException>(() => second.Save(1, "123:BAUG"));
            Assert.That(second.Load(1), Is.EqualTo("123:AAEC"));
        }
        [Test] public void LateProcessCannotClearNewerFileRecord()
        {
            var first = new CommerceFileRecoveryStore(_directory); first.Save(1, "123:AAEC");
            var second = new CommerceFileRecoveryStore(_directory);
            second.Clear(1, "123:AAEC"); second.Save(1, "456:BAUG");
            Assert.Throws<InvalidOperationException>(() => first.Clear(1, "123:AAEC"));
            Assert.That(second.Load(1), Is.EqualTo("456:BAUG"));
        }
        [Test] public void CorruptEmptyRecordFailsClosedInsteadOfAllowingNewPurchase()
        {
            Directory.CreateDirectory(_directory); File.WriteAllText(Path.Combine(_directory, "1.pending"), "");
            var store = new CommerceFileRecoveryStore(_directory);
            Assert.That(store.Load(1), Is.Not.Empty);
            var net = new JubaozhaiFakeTransport(); using var client = new CommerceClient(net, store);
            client.BuySku(12, 1);
            Assert.That(net.Calls, Is.Empty);
        }
    }
}
