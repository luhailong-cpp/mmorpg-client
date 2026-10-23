using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Google.Protobuf;
using Guildpb;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Guild;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Guild;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class GuildEconomyRecoveryTests
    {
        private string _directory;
        private DurableRequestFileStore _files;
        private RecoveryTransport _net;
        private GuildClient _client;
        private object _identity;
        private long _now;

        [SetUp] public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "guild-economy-" + Guid.NewGuid().ToString("N"));
            _files = DurableRequestFileStore.ForOrigin(_directory, "https://guild-test.invalid");
            _net = new RecoveryTransport(); _identity = new object(); _now = 1700000000000;
            _client = CreateClient();
            Load();
        }
        [TearDown] public void TearDown()
        {
            _client.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
        private GuildClient CreateClient() => new GuildClient(_net, () => _identity,
            economyRecovery: _files, economyUtcMs: () => _now);
        private void Load()
        {
            _client.Refresh();
            _net.Reply(new GetPlayerGuildResponse { Guild = GuildClientTests.Fixture() });
        }
        private void ConfirmRecoveryGuild(ulong guild = 555)
        {
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
            var info = GuildClientTests.Fixture(); info.GuildId = guild;
            _net.Reply(new GetPlayerGuildResponse { Guild = info });
        }
        private static byte[] SavedBytes(string saved) => Convert.FromBase64String(saved.Split(':')[5]);
        private void Send(bool donation) { if (donation) _client.Donate(2); else _client.Buy(101, 2); }
        private ulong LastKey() => _net.Requests.Last() switch
        {
            DonateToGuildRequest donation => donation.ClientRequestId,
            BuyGuildShopGoodsRequest order => order.ClientRequestId,
            _ => throw new AssertionException("最后一发不是捐献或兑换"),
        };
        private void LoadPage(bool donation)
        {
            if (donation) { _client.RefreshDonations(); _net.Reply(new GetGuildDonateOptionsResponse()); }
            else { _client.RefreshShop(); _net.Reply(new GetGuildShopResponse()); }
        }
        // 写回包落定后本页可能带着结果文案重拉一次(在途期间发不出新操作):替那一发回一个空快照。
        private void ReplyPageReload()
        {
            uint last = _net.Calls.Last();
            if (last == MessageIds.GetGuildDonateOptions) _net.Reply(new GetGuildDonateOptionsResponse());
            else if (last == MessageIds.GetGuildShop) _net.Reply(new GetGuildShopResponse());
        }

        [TestCase(true)] [TestCase(false)]
        public void SavesExactNonzeroRequestBeforeNetworkSend(bool donation)
        {
            bool inspected = false;
            _net.BeforeCall = (method, request) =>
            {
                inspected = true;
                string saved = _files.Load(1);
                Assert.That(saved, Is.Not.Empty);
                Assert.That(SavedBytes(saved), Is.EqualTo(request.ToByteArray()));
                Assert.That(saved.Split(':')[4], Is.EqualTo(method.ToString(CultureInfo.InvariantCulture)));
            };
            if (donation) _client.Donate(2); else _client.Buy(101, 3);
            Assert.That(inspected, Is.True);
            if (donation)
            {
                var request = (DonateToGuildRequest)_net.Requests.Last();
                Assert.That(request.ClientRequestId, Is.Not.Zero);
                Assert.That(request.ExpectedGuildId, Is.EqualTo(555ul));
            }
            else
            {
                var request = (BuyGuildShopGoodsRequest)_net.Requests.Last();
                Assert.That(request.ClientRequestId, Is.Not.Zero);
                Assert.That(request.ExpectedGuildId, Is.EqualTo(555ul));
                Assert.That(request.Count, Is.EqualTo(3u));
            }
        }

        [TestCase(true)] [TestCase(false)]
        public void NewProcessReplaysTheSameOriginalRequestAfterConnectionFailure(bool donation)
        {
            if (donation) _client.Donate(2); else _client.Buy(101, 2);
            byte[] original = _net.Requests.Last().ToByteArray();
            string saved = _files.Load(1);
            _net.Error("rpc timeout");
            _client.Dispose(); _identity = new object(); _client = CreateClient();
            _client.RecoverEconomyRequest(); ConfirmRecoveryGuild();
            Assert.That(_net.Requests.Last().ToByteArray(), Is.EqualTo(original));
            Assert.That(_files.Load(1), Is.EqualTo(saved));
        }

        [Test] public void TimeoutQuarantinesSameConnectionAndLateCallbackCannotClearAfterReconnect()
        {
            _client.Donate(2);
            var oldReply = _net.Pending; string saved = _files.Load(1);
            _net.Error("rpc timeout"); int count = _net.Calls.Count;
            _client.RecoverEconomyRequest();
            Assert.That(_net.Calls.Count, Is.EqualTo(count));
            _identity = new object(); _client.ObserveConnection();
            _client.RecoverEconomyRequest(); ConfirmRecoveryGuild();
            oldReply(new DonateToGuildResponse { Donation = new GuildDonationView
                { OpId = 9, DonateId = 2, Status = GuildAssetOrderStatus.Applied } });
            Assert.That(_files.Load(1), Is.EqualTo(saved));
            Assert.That(_client.Busy, Is.True);
        }

        [TestCase(GuildAssetOrderStatus.Pending)]
        [TestCase(GuildAssetOrderStatus.Applied)]
        [TestCase(GuildAssetOrderStatus.Rejected)]
        [TestCase(GuildAssetOrderStatus.Aborted)]
        [TestCase(GuildAssetOrderStatus.AppliedPartial)]
        public void AValidatedDonationViewReleasesTheOriginalSlot(GuildAssetOrderStatus status)
        {
            _client.Donate(2);
            _net.Reply(new DonateToGuildResponse { Guild = GuildClientTests.Fixture(), Donation = new GuildDonationView
                { OpId = 9, DonateId = 2, Status = status } });
            Assert.That(_files.Load(1), Is.Empty);
        }

        [Test] public void AValidatedShopViewReleasesTheOriginalSlot()
        {
            _client.Buy(101, 2);
            _net.Reply(new BuyGuildShopGoodsResponse { Order = new GuildShopOrderView
                { OpId = 9, GoodsId = 101, Count = 2, Status = GuildAssetOrderStatus.Pending } });
            Assert.That(_files.Load(1), Is.Empty);
        }

        [TestCase(0u)] [TestCase((uint)guild_error.KGuildBusyRetry)] [TestCase(1003u)]
        public void EmptySuccessBusyAndFaultKeepTheKeyAndBlockAnotherWrite(uint tip)
        {
            _client.Donate(2); string original = _files.Load(1);
            _net.Reply(new DonateToGuildResponse { ErrorMessage = tip == 0 ? null : new TipInfoMessage { Id = tip } });
            Assert.That(_files.Load(1), Is.EqualTo(original));
            int count = _net.Calls.Count;
            _client.Buy(101); _client.Donate(3);
            Assert.That(_net.Calls.Count, Is.EqualTo(count));
            _client.RecoverEconomyRequest(); ConfirmRecoveryGuild();
            Assert.That(_net.Requests.Last().ToByteArray(), Is.EqualTo(SavedBytes(original)));
        }

        [Test] public void BusinessRejectionWithoutAnOrderReleasesTheSlot()
        {
            _client.Donate(2);
            _net.Reply(new DonateToGuildResponse { ErrorMessage = new TipInfoMessage
                { Id = (uint)guild_error.KGuildCurrencyInsufficient } });
            Assert.That(_files.Load(1), Is.Empty);
        }

        // 契约:收到订单视图或其它业务拒绝后原键作废,玩家再点一次是新意图、新键。
        [TestCase(true, false)] [TestCase(true, true)] [TestCase(false, false)] [TestCase(false, true)]
        public void TheNextOperationAfterAnOrderViewOrABusinessRejectionUsesANewKey(bool donation, bool rejected)
        {
            Send(donation); ulong first = LastKey();
            Assert.That(first, Is.Not.Zero);
            var tip = new TipInfoMessage { Id = (uint)(donation ? guild_error.KGuildDonateLimit : guild_error.KGuildShopLimit) };
            if (donation)
                _net.Reply(rejected ? new DonateToGuildResponse { ErrorMessage = tip } : new DonateToGuildResponse
                    { Guild = GuildClientTests.Fixture(), Donation = new GuildDonationView { OpId = 9, DonateId = 2, Status = GuildAssetOrderStatus.Applied } });
            else
                _net.Reply(rejected ? new BuyGuildShopGoodsResponse { ErrorMessage = tip } : new BuyGuildShopGoodsResponse
                    { Order = new GuildShopOrderView { OpId = 9, GoodsId = 101, Count = 2, Status = GuildAssetOrderStatus.Applied } });
            ReplyPageReload();
            Assert.That(_files.Load(1), Is.Empty);

            Send(donation);
            Assert.That(LastKey(), Is.Not.Zero.And.Not.EqualTo(first));
        }

        // 契约:1005 = 这个键已绑定另一笔请求(客户端复用了键)。不得用同一个键重试;刷新本页,由玩家重新操作(新键)。
        [TestCase(true)] [TestCase(false)]
        public void InvalidParameterRetiresTheKeyWithoutResendingItAndReloadsThePage(bool donation)
        {
            LoadPage(donation);
            Send(donation); ulong first = LastKey(); uint method = _net.Calls.Last();
            var conflict = new TipInfoMessage { Id = (uint)common_error.KInvalidParameter };
            if (donation) _net.Reply(new DonateToGuildResponse { ErrorMessage = conflict });
            else _net.Reply(new BuyGuildShopGoodsResponse { ErrorMessage = conflict });

            Assert.That(_files.Load(1), Is.Empty);
            Assert.That(_net.Calls.Last(), Is.EqualTo(donation ? MessageIds.GetGuildDonateOptions : MessageIds.GetGuildShop));
            ReplyPageReload();
            Assert.That(_client.Status, Does.Contain("请求编号已失效"));

            _client.RecoverEconomyRequest();
            Assert.That(_net.Calls.Count(call => call == method), Is.EqualTo(1));
            Send(donation);
            Assert.That(LastKey(), Is.Not.Zero.And.Not.EqualTo(first));
        }

        [Test] public void WrongOrderIdentityDoesNotConfirmOrApplyTheReply()
        {
            _client.Buy(101, 2); string original = _files.Load(1);
            _net.Reply(new BuyGuildShopGoodsResponse { ContributionBalance = 9999, Order = new GuildShopOrderView
                { OpId = 9, GoodsId = 102, Count = 2, Status = GuildAssetOrderStatus.Applied } });
            Assert.That(_files.Load(1), Is.EqualTo(original));
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.BuyGuildShopGoods));
        }

        [Test] public void AuthoritativeGuildChangeCannotExecuteAnUnsubmittedOldRequest()
        {
            _client.Donate(2); string original = _files.Load(1);
            _net.Reply(new DonateToGuildResponse());
            _client.RecoverEconomyRequest(); int before = _net.Calls.Count;
            ConfirmRecoveryGuild(556);
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_files.Load(1), Is.EqualTo(original));
            Assert.That(_client.Status, Does.Contain("原帮会"));
        }

        [Test] public void LateProcessCannotClearAReplacementRecord()
        {
            _client.Donate(2); var oldReply = _net.Pending;
            string original = _files.Load(1), replacement = original + "newer";
            // 模拟其它进程已确认旧单并占用新请求槽；旧回包的CAS必须拒绝。
            _files.Clear(1, original); _files.Save(1, replacement);
            oldReply(new DonateToGuildResponse { Donation = new GuildDonationView
                { OpId = 9, DonateId = 2, Status = GuildAssetOrderStatus.Applied } });
            Assert.That(_files.Load(1), Is.EqualTo(replacement));
            Assert.That(_client.Status, Does.Contain("尚未清理"));
        }

        [Test] public void OriginAndPlayerAreSeparateRecoveryNamespaces()
        {
            _client.Donate(2);
            Assert.That(_files.Load(2), Is.Empty);
            Assert.That(DurableRequestFileStore.ForOrigin(_directory, "https://another-test.invalid").Load(1), Is.Empty);
        }

        [TestCase("!损坏")] [TestCase("G1:1:555:1700000000000:1:not-base64")]
        public void CorruptRecordsFailClosedWithoutNetworkOrDeletion(string corrupt)
        {
            _files.Save(1, corrupt); int before = _net.Calls.Count;
            _client.RecoverEconomyRequest();
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_files.Load(1), Is.EqualTo(corrupt));
        }

        [Test] public void ARecordCopiedFromAnotherPlayerCannotReplay()
        {
            _client.Donate(2); string original = _files.Load(1);
            _net.Reply(new DonateToGuildResponse());
            string copied = original.Replace("G1:1:", "G1:2:");
            _files.Transition(1, original, copied); int before = _net.Calls.Count;
            _client.RecoverEconomyRequest();
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_files.Load(1), Is.EqualTo(copied));
        }

        [TestCase(true)] [TestCase(false)]
        public void ExpiryOrClockRegressionCannotReplayBeyondTheServerDeduplicationWindow(bool expired)
        {
            _client.Donate(2); string original = _files.Load(1);
            _net.Reply(new DonateToGuildResponse());
            _now += expired ? GuildClient.EconomyReplayWindowMs : -1;
            int before = _net.Calls.Count; _client.RecoverEconomyRequest();
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_files.Load(1), Is.EqualTo(original));
            Assert.That(_client.Status, Does.Contain("安全恢复"));
        }

        [Test] public void ALongDelayedGuildReadCannotReplayAfterTheSafeWindow()
        {
            _client.Donate(2); string original = _files.Load(1);
            _net.Reply(new DonateToGuildResponse());
            _client.RecoverEconomyRequest(); int before = _net.Calls.Count;
            _now += GuildClient.EconomyReplayWindowMs;
            ConfirmRecoveryGuild();
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_files.Load(1), Is.EqualTo(original));
        }

        [Test] public void ExistingRefreshButtonExposesRecoveryWithoutCreatingAnotherPayment()
        {
            _client.Donate(2); _net.Reply(new DonateToGuildResponse());
            var root = new GameObject("GuildEconomyRecoveryWindow", typeof(RectTransform));
            GuildWindow window = null;
            try
            {
                var design = QdaoUguiFactory.CreateCenteredRect("Design", root.transform, 2560, 1080);
                window = new GuildWindow(design);
                window.EconomyRecoveryRequested += _client.RecoverEconomyRequest;
                window.SetClient(_client); window.Show();
                var button = root.GetComponentsInChildren<Button>(true).Single(value => value.name == "RefreshGuild");
                Assert.That(button.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("恢复原请求"));
                Assert.That(button.interactable, Is.True);
                int before = _net.Calls.Count; button.onClick.Invoke();
                Assert.That(_net.Calls.Count, Is.EqualTo(before + 1));
                Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
            }
            finally { window?.Hide(); UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test] public void CannotSendWhenPersistentStorageIsUnavailable()
        {
            _client.Dispose();
            string blocked = Path.Combine(_directory, "blocked");
            Directory.CreateDirectory(_directory); File.WriteAllText(blocked, "file instead of directory");
            _files = new DurableRequestFileStore(blocked); _client = CreateClient(); Load();
            int before = _net.Calls.Count; _client.Donate(2);
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Status, Does.Contain("安全保存"));
        }

        private sealed class RecoveryTransport : IBattleTransport
        {
            public ulong PlayerId { get; set; } = 1;
            public bool IsReady { get; set; } = true;
            public event Action Disconnected { add { } remove { } }
            public readonly List<uint> Calls = new();
            public readonly List<IMessage> Requests = new();
            public Action<uint, IMessage> BeforeCall;
            public Action<IMessage> Pending;
            public Action<string> Error;
            public void Reply(IMessage response) => Pending(response);
            public void RegisterNotify(uint id, Action<MessageContent> callback) { }
            public void SendOneWay(uint id, IMessage request) { }
            public void Call<T>(uint id, IMessage request, MessageParser<T> parser, Action<T> success, Action<string> error)
                where T : IMessage<T>
            {
                BeforeCall?.Invoke(id, request);
                Calls.Add(id); Requests.Add(request); Pending = message => success((T)message); Error = error;
            }
        }
    }
}
