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
            client.BuySku(12, 1);
            net.Reply(new CommerceOrderResponse { ErrorMessage = new TipInfoMessage { Id = (uint)common_error.KServiceUnavailable } });
            Assert.That(client.HasPendingWrite, Is.True);
            client.RetryPending(); net.Reply(new CommerceOrderResponse());
            Assert.That(client.HasPendingWrite, Is.True);
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
