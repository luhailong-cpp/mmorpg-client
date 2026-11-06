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
        private void ReplyWithoutOrder(bool donation)
        {
            if (donation) _net.Reply(new DonateToGuildResponse());
            else _net.Reply(new BuyGuildShopGoodsResponse());
        }
        private void ConfirmRecoveryAfterLeaving(bool inAnotherGuild)
        {
            if (inAnotherGuild) ConfirmRecoveryGuild(556);
            else _net.Reply(new GetPlayerGuildResponse
                { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } });
        }
        private string ConcludeRecoveryAfterLeaving(bool donation, bool hasOrder)
        {
            Send(donation); ReplyWithoutOrder(donation);
            _client.RecoverEconomyRequest(); ConfirmRecoveryAfterLeaving(false);
            var tip = hasOrder ? null : new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild };
            if (donation)
                _net.Reply(new DonateToGuildResponse { ErrorMessage = tip, Donation = hasOrder
                    ? new GuildDonationView { OpId = 9, DonateId = 2, Status = GuildAssetOrderStatus.Applied } : null });
            else
                _net.Reply(new BuyGuildShopGoodsResponse { ErrorMessage = tip, Order = hasOrder
                    ? new GuildShopOrderView { OpId = 9, GoodsId = 101, Count = 2, Status = GuildAssetOrderStatus.Applied } : null });
            Assert.That(_client.Status, Does.Contain("原帮会"));
            Assert.That(_client.MyApplicationsQueued, Is.True);
            return _client.Status;
        }
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
        private string SeedRawRecoveryFile(byte[] bytes)
        {
            _files.Save(1, "损坏记录测试占位");
            string path = Directory.GetFiles(_directory, "1.pending", SearchOption.AllDirectories).Single();
            File.WriteAllBytes(path, bytes);
            return path;
        }
        private void ReplyAfterCorruptRetirement(bool inGuild = true)
        {
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
            _net.Reply(inGuild ? new GetPlayerGuildResponse { Guild = GuildClientTests.Fixture() }
                : new GetPlayerGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } });
            Assert.That(_client.Status, Does.Contain("已作废损坏"));
            Assert.That(_client.Status, Does.Contain("原请求未重发"));
            Assert.That(_client.Status, Does.Contain("不能据此判断是否到账"));
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

        [TestCase(true, false)] [TestCase(true, true)] [TestCase(false, false)] [TestCase(false, true)]
        public void RecoveryAfterLeavingReplaysOriginalBytesAndRetiresARejectedRequest(bool donation, bool inAnotherGuild)
        {
            Send(donation); string original = _files.Load(1); ulong first = LastKey(); uint method = _net.Calls.Last();
            ReplyWithoutOrder(donation);
            _client.RecoverEconomyRequest(); ConfirmRecoveryAfterLeaving(inAnotherGuild);
            Assert.That(_net.Calls.Last(), Is.EqualTo(method));
            Assert.That(_net.Requests.Last().ToByteArray(), Is.EqualTo(SavedBytes(original)));
            Assert.That(_files.Load(1), Is.EqualTo(original));

            var tip = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild };
            if (donation) _net.Reply(new DonateToGuildResponse { ErrorMessage = tip });
            else _net.Reply(new BuyGuildShopGoodsResponse { ErrorMessage = tip });
            Assert.That(_files.Load(1), Is.Empty);
            Assert.That(_client.HasPendingEconomyWrite, Is.False);
            Assert.That(_client.Status, Does.Contain("原帮会"));
            Assert.That(_net.Calls.Last(), Is.EqualTo(method));
            Assert.That(_client.RefreshQueued, Is.False);
            Assert.That(_client.DonationsQueued || _client.ShopQueued, Is.False);
            if (inAnotherGuild)
            {
                Assert.That(_client.Info.GuildId, Is.EqualTo(556ul));
                Send(donation);
                Assert.That(LastKey(), Is.Not.Zero.And.Not.EqualTo(first));
                ulong expected = donation ? ((DonateToGuildRequest)_net.Requests.Last()).ExpectedGuildId
                    : ((BuyGuildShopGoodsRequest)_net.Requests.Last()).ExpectedGuildId;
                Assert.That(expected, Is.EqualTo(556ul));
            }
            else Assert.That(_client.Info, Is.Null);
        }

        [TestCase(true, false, GuildAssetOrderStatus.Applied)]
        [TestCase(true, true, GuildAssetOrderStatus.Applied)]
        [TestCase(false, false, GuildAssetOrderStatus.Applied)]
        [TestCase(false, true, GuildAssetOrderStatus.Applied)]
        [TestCase(true, false, GuildAssetOrderStatus.AppliedPartial)]
        [TestCase(true, true, GuildAssetOrderStatus.AppliedPartial)]
        [TestCase(false, false, GuildAssetOrderStatus.AppliedPartial)]
        [TestCase(false, true, GuildAssetOrderStatus.AppliedPartial)]
        public void ReplayedOldGuildOrderRefreshesAssetsWithoutOverwritingCurrentGuild(
            bool donation, bool inAnotherGuild, GuildAssetOrderStatus status)
        {
            Send(donation); string original = _files.Load(1); uint method = _net.Calls.Last();
            ReplyWithoutOrder(donation);
            _client.RecoverEconomyRequest(); ConfirmRecoveryAfterLeaving(inAnotherGuild);
            Assert.That(_net.Calls.Last(), Is.EqualTo(method));
            Assert.That(_net.Requests.Last().ToByteArray(), Is.EqualTo(SavedBytes(original)));
            int calls = _net.Calls.Count, assetsChanged = 0;
            ulong balance = inAnotherGuild ? _client.Info.Members.Single(member => member.PlayerId == 1).ContributionBalance : 0;
            _client.AssetsChanged += () => ++assetsChanged;
            if (donation)
                _net.Reply(new DonateToGuildResponse { Guild = GuildClientTests.Fixture(), Donation = new GuildDonationView
                    { OpId = 9, DonateId = 2, Status = status } });
            else
                _net.Reply(new BuyGuildShopGoodsResponse { ContributionBalance = 9999, Order = new GuildShopOrderView
                    { OpId = 9, GoodsId = 101, Count = 2, Status = status } });

            Assert.That(_files.Load(1), Is.Empty);
            Assert.That(_client.HasPendingEconomyWrite, Is.False);
            Assert.That(_client.Status, Does.Contain("原帮会"));
            Assert.That(assetsChanged, Is.EqualTo(1));
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));
            Assert.That(_client.RefreshQueued || _client.DonationsQueued || _client.ShopQueued, Is.False);
            if (inAnotherGuild)
            {
                Assert.That(_client.Info.GuildId, Is.EqualTo(556ul));
                Assert.That(_client.Info.Members.Single(member => member.PlayerId == 1).ContributionBalance, Is.EqualTo(balance));
            }
            else Assert.That(_client.Info, Is.Null);
        }

        [TestCase(true, false)] [TestCase(true, true)] [TestCase(false, false)] [TestCase(false, true)]
        public void QueuedApplicationsRefreshPreservesTheOldGuildConclusionOnce(bool donation, bool hasOrder)
        {
            string conclusion = ConcludeRecoveryAfterLeaving(donation, hasOrder);
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.ListMyGuildApplications));
            _net.Reply(new ListMyGuildApplicationsResponse());
            Assert.That(_client.Status, Is.EqualTo(conclusion));
            Assert.That(_client.MyApplications, Is.Not.Null);
            Assert.That(_client.MyApplicationsQueued, Is.False);

            // 这次自动补拉消费了文案;以后玩家主动刷新不再恢复这笔旧请求的结论。
            _client.LoadMyApplications();
            _net.Reply(new ListMyGuildApplicationsResponse());
            Assert.That(_client.Status, Is.Not.EqualTo(conclusion));
        }

        [Test] public void AnotherUserActionBeforeApplicationsRefreshSupersedesTheRecoveryConclusion()
        {
            string conclusion = ConcludeRecoveryAfterLeaving(true, true);
            _client.RecoverEconomyRequest();
            Assert.That(_client.Status, Does.Contain("没有待确认"));
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.ListMyGuildApplications));
            _net.Reply(new ListMyGuildApplicationsResponse());
            Assert.That(_client.Status, Is.Not.EqualTo(conclusion));
        }

        [Test] public void ResetDoesNotRestoreAnOldRecoveryConclusionOnApplicationsRefresh()
        {
            string conclusion = ConcludeRecoveryAfterLeaving(false, false);
            _client.Reset();
            _client.Refresh();
            _net.Reply(new GetPlayerGuildResponse
                { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } });
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.ListMyGuildApplications));
            _net.Reply(new ListMyGuildApplicationsResponse());
            Assert.That(_client.Status, Is.Not.EqualTo(conclusion));
        }

        [Test] public void ApplicationsRefreshFailureReplacesTheRecoveryConclusion()
        {
            string conclusion = ConcludeRecoveryAfterLeaving(true, false);
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.ListMyGuildApplications));
            var tip = new TipInfoMessage { Id = (uint)guild_error.KGuildBusyRetry };
            _net.Reply(new ListMyGuildApplicationsResponse { ErrorMessage = tip });
            Assert.That(_client.Status, Is.EqualTo(GuildClient.TipText(tip)));
            Assert.That(_client.Status, Is.Not.EqualTo(conclusion));
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
        public void CorruptRecordsRequireVisibleConfirmationBeforeLocalRetirement(string corrupt)
        {
            _files.Save(1, corrupt); int before = _net.Calls.Count;
            _client.RecoverEconomyRequest();
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_files.Load(1), Is.EqualTo(corrupt));
            Assert.That(_client.Status, Does.Contain("损坏").And.Contain("再点一次"));
            Assert.That(_client.HasPendingEconomyWrite, Is.True);
            _client.RecoverEconomyRequest();
            Assert.That(_files.Load(1), Is.Empty);
            Assert.That(_client.HasPendingEconomyWrite, Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(before + 1));
            ReplyAfterCorruptRetirement();
        }

        [Test] public void ARecordCopiedFromAnotherPlayerCanOnlyRetireTheCurrentPlayersSlot()
        {
            _client.Donate(2); string original = _files.Load(1);
            _net.Reply(new DonateToGuildResponse());
            string copied = original.Replace("G1:1:", "G1:2:");
            _files.Save(2, copied);
            _files.Transition(1, original, copied); int before = _net.Calls.Count;
            _client.RecoverEconomyRequest();
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_files.Load(1), Is.EqualTo(copied));
            Assert.That(_client.Status, Does.Contain("损坏").And.Contain("再点一次"));
            _client.RecoverEconomyRequest();
            Assert.That(_files.Load(1), Is.Empty);
            Assert.That(_files.Load(2), Is.EqualTo(copied));
            Assert.That(_net.Calls.Count, Is.EqualTo(before + 1));
            Assert.That(_net.Calls.Count(call => call == MessageIds.DonateToGuild), Is.EqualTo(1));
            ReplyAfterCorruptRetirement();
        }

        [TestCase(true)] [TestCase(false)]
        public void CorruptPayloadWithinTheReplayWindowRetiresWithoutResendingOrReusingItsKey(bool donation)
        {
            LoadPage(true); LoadPage(false);
            Send(donation); ulong originalKey = LastKey(); string original = _files.Load(1);
            ReplyWithoutOrder(donation);
            var fields = original.Split(':'); fields[5] = "not-base64";
            string corrupt = string.Join(":", fields);
            _files.Transition(1, original, corrupt);
            int before = _net.Calls.Count;
            _client.RecoverEconomyRequest();
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_files.Load(1), Is.EqualTo(corrupt));
            Assert.That(_client.Status, Does.Contain("损坏").And.Contain("再点一次"));
            _client.RecoverEconomyRequest();
            Assert.That(_files.Load(1), Is.Empty);
            Assert.That(_client.Donations, Is.Null);
            Assert.That(_client.Shop, Is.Null);
            Assert.That(_net.Calls.Count, Is.EqualTo(before + 1));
            ReplyAfterCorruptRetirement();
            Send(donation);
            Assert.That(LastKey(), Is.Not.Zero.And.Not.EqualTo(originalKey));
        }

        [TestCase(0)] [TestCase(65537)]
        public void EmptyAndOversizedFilesCanBeRetiredByTheirExactBytes(int length)
        {
            byte[] original = new byte[length];
            string path = SeedRawRecoveryFile(original);
            Assert.That(_files.Load(1), Is.EqualTo("!损坏"));
            int before = _net.Calls.Count;
            _client.RecoverEconomyRequest();
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Status, Does.Contain("再点一次"));
            _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.False);
            Assert.That(_client.HasPendingEconomyWrite, Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(before + 1));
            ReplyAfterCorruptRetirement();
        }

        [TestCase(false)] [TestCase(true)]
        public void TheSameCorruptMarkerCannotConfirmDifferentRawBytes(bool sameLength)
        {
            byte[] original = new byte[sameLength ? 65537 : 0];
            string path = SeedRawRecoveryFile(original);
            _client.RecoverEconomyRequest();
            byte[] replacement = new byte[65537]; replacement[replacement.Length - 1] = 1;
            File.WriteAllBytes(path, replacement);
            // 两份记录的 Load 都是 !损坏,同长度时也只差最后一个字节;确认必须绑定原始内容。
            Assert.That(_files.Load(1), Is.EqualTo("!损坏"));
            int before = _net.Calls.Count;
            _client.RecoverEconomyRequest();
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(replacement));
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Status, Does.Contain("再点一次"));
            _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(before + 1));
            ReplyAfterCorruptRetirement();
        }

        [TestCase(false)] [TestCase(true)]
        public void UnreadableOrLockedRecoveryFilesKeepTheirSlotAndInvalidateConfirmation(bool lockGuard)
        {
            byte[] original = new byte[0];
            string path = SeedRawRecoveryFile(original);
            _client.RecoverEconomyRequest();
            int before = _net.Calls.Count;
            using (var blocker = new FileStream(lockGuard ? path + ".lock" : path,
                FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                _client.RecoverEconomyRequest(); _client.RecoverEconomyRequest();
                Assert.That(File.Exists(path), Is.True);
                Assert.That(_client.HasPendingEconomyWrite, Is.True);
                Assert.That(_net.Calls.Count, Is.EqualTo(before));
            }
            _client.RecoverEconomyRequest();
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
            Assert.That(_client.Status, Does.Contain("再点一次"));
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.False);
            ReplyAfterCorruptRetirement();
        }

        [Test] public void AFailedCorruptFileDeletionRequiresFreshConfirmationAfterUnlocking()
        {
            string path = SeedRawRecoveryFile(new byte[0]);
            _client.RecoverEconomyRequest();
            int before = _net.Calls.Count;
            // 允许读取和计算指纹,但不给 Delete 共享权限,确定性复现清除失败。
            using (var blocker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                _client.RecoverEconomyRequest();
                Assert.That(File.Exists(path), Is.True);
                Assert.That(_client.HasPendingEconomyWrite, Is.True);
                Assert.That(_net.Calls.Count, Is.EqualTo(before));
            }
            _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.True);
            Assert.That(_client.Status, Does.Contain("再点一次"));
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.False);
            ReplyAfterCorruptRetirement();
        }

        [TestCase(false)] [TestCase(true)]
        public void ReconnectionOrCharacterChangeRequiresANewCorruptRecordWarning(bool changeCharacter)
        {
            string path = SeedRawRecoveryFile(new byte[0]);
            _client.RecoverEconomyRequest();
            Assert.That(_client.Status, Does.Contain("再点一次"));
            if (changeCharacter)
            {
                _net.PlayerId = 2; _client.Reset();
                Assert.That(_client.HasPendingEconomyWrite, Is.False);
                _net.PlayerId = 1; _client.Reset();
            }
            else { _identity = new object(); _client.ObserveConnection(); }
            int before = _net.Calls.Count;
            _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.True);
            Assert.That(_client.Status, Does.Contain("再点一次"));
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.False);
            ReplyAfterCorruptRetirement();
        }

        [Test] public void ChangedStatusRequiresAnotherCorruptRecordWarningBeforeRetirement()
        {
            string path = SeedRawRecoveryFile(new byte[0]);
            _client.RecoverEconomyRequest();
            int before = _net.Calls.Count;
            _client.Donate(3);
            Assert.That(_client.Status, Does.Contain("已有待确认"));
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.True);
            Assert.That(_client.Status, Does.Contain("再点一次"));
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.False);
            ReplyAfterCorruptRetirement();
        }

        [Test] public void CorruptRetirementConclusionSurvivesNotInGuildAndOneApplicationsRefresh()
        {
            string path = SeedRawRecoveryFile(new byte[0]);
            _client.RecoverEconomyRequest(); _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.False);
            ReplyAfterCorruptRetirement(false);
            string conclusion = _client.Status;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.ListMyGuildApplications));
            _net.Reply(new ListMyGuildApplicationsResponse());
            Assert.That(_client.Status, Is.EqualTo(conclusion));
            _client.LoadMyApplications(); _net.Reply(new ListMyGuildApplicationsResponse());
            Assert.That(_client.Status, Is.Not.EqualTo(conclusion));
        }

        [Test] public void AnApplicationPushDuringRefreshSupersedesTheCorruptRetirementConclusion()
        {
            SeedRawRecoveryFile(new byte[0]);
            _client.RecoverEconomyRequest(); _client.RecoverEconomyRequest();
            ReplyAfterCorruptRetirement(false);
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.ListMyGuildApplications));
            _net.Notify(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 555, TargetPlayerId = 1, Kind = GuildChangeKind.ApplicationRejected });
            string pushedNotice = _client.Status;
            Assert.That(pushedNotice, Does.Contain("未获通过"));
            _net.Reply(new ListMyGuildApplicationsResponse());
            Assert.That(_client.Status, Is.EqualTo(pushedNotice));
        }

        [Test] public void AKickDuringCorruptRetirementRefreshTakesPrecedenceOverTheRetirementConclusion()
        {
            SeedRawRecoveryFile(new byte[0]);
            _client.RecoverEconomyRequest(); _client.RecoverEconomyRequest();
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
            _net.Notify(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 555, TargetPlayerId = 1, Kind = GuildChangeKind.MemberKicked });
            string pushedNotice = _client.Status;
            Assert.That(pushedNotice, Does.Contain("请离"));
            _net.Reply(new GetPlayerGuildResponse
                { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } });
            Assert.That(_client.Status, Is.EqualTo(pushedNotice));
            Assert.That(_client.Info, Is.Null);
            Assert.That(_client.HasPendingEconomyWrite, Is.False);
        }

        [TestCase(true)] [TestCase(false)]
        public void CorruptRetirementRefreshFailureKeepsTheFileClearedAndShowsTheReadError(bool serviceFault)
        {
            string path = SeedRawRecoveryFile(new byte[0]);
            _client.RecoverEconomyRequest(); _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.False);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
            int calls = _net.Calls.Count;
            var tip = new TipInfoMessage { Id = (uint)common_error.KServiceUnavailable };
            _net.Reply(serviceFault ? new GetPlayerGuildResponse { ErrorMessage = tip } : new GetPlayerGuildResponse());
            Assert.That(File.Exists(path), Is.False);
            Assert.That(_client.HasPendingEconomyWrite, Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));
            Assert.That(_client.Status, Does.Not.Contain("已作废"));
            if (serviceFault) Assert.That(_client.Status, Is.EqualTo(GuildClient.TipText(tip)));
            else Assert.That(_client.Status, Does.Contain("有效帮会信息"));
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

        [TestCase(true, true)] [TestCase(true, false)] [TestCase(false, true)] [TestCase(false, false)]
        public void StaleRequestRequiresTheVisibleWarningAgainBeforeRetirement(bool donation, bool expired)
        {
            LoadPage(donation);
            Send(donation); string original = _files.Load(1); ulong first = LastKey(); uint method = _net.Calls.Last();
            ReplyWithoutOrder(donation);
            _now += expired ? GuildClient.EconomyReplayWindowMs : -1;
            int before = _net.Calls.Count;
            _client.RecoverEconomyRequest();
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_files.Load(1), Is.EqualTo(original));
            Assert.That(_client.Status, Does.Contain("再点一次"));
            if (!expired) Assert.That(_client.Status, Does.Contain("校准"));

            _client.Donate(3);
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Status, Does.Contain("已有待确认"));
            _client.RecoverEconomyRequest();
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_files.Load(1), Is.EqualTo(original));
            Assert.That(_client.HasPendingEconomyWrite, Is.True);
            Assert.That(_client.Status, Does.Contain("再点一次"));

            _client.RecoverEconomyRequest();
            Assert.That(_files.Load(1), Is.Empty);
            Assert.That(_client.HasPendingEconomyWrite, Is.False);
            Assert.That(_net.Calls.Count(call => call == method), Is.EqualTo(1));
            Assert.That(_net.Calls.Last(), Is.EqualTo(donation ? MessageIds.GetGuildDonateOptions : MessageIds.GetGuildShop));
            ReplyPageReload();
            Assert.That(_client.Status, Does.Contain("已作废"));
            Send(donation);
            Assert.That(LastKey(), Is.Not.Zero.And.Not.EqualTo(first));
        }

        [TestCase(false)] [TestCase(true)]
        public void ReconnectionOrCharacterChangeRequiresANewStaleWarning(bool changeCharacter)
        {
            _client.Donate(2); string original = _files.Load(1);
            _net.Reply(new DonateToGuildResponse());
            _now += GuildClient.EconomyReplayWindowMs;
            _client.RecoverEconomyRequest();
            Assert.That(_client.Status, Does.Contain("再点一次"));
            if (changeCharacter)
            {
                _net.PlayerId = 2; _client.Reset();
                Assert.That(_client.HasPendingEconomyWrite, Is.False);
                _net.PlayerId = 1; _client.Reset();
            }
            else { _identity = new object(); _client.ObserveConnection(); }

            int before = _net.Calls.Count;
            _client.RecoverEconomyRequest();
            Assert.That(_files.Load(1), Is.EqualTo(original));
            Assert.That(_client.Status, Does.Contain("再点一次"));
            _client.RecoverEconomyRequest();
            Assert.That(_files.Load(1), Is.Empty);
            Assert.That(_client.Status, Does.Contain("已作废"));
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
        }

        [Test] public void CorrectingClockAfterWarningReplaysTheOriginalRequest()
        {
            _client.Donate(2); string original = _files.Load(1);
            _net.Reply(new DonateToGuildResponse());
            --_now; int before = _net.Calls.Count;
            _client.RecoverEconomyRequest();
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Status, Does.Contain("校准"));
            ++_now;
            _client.RecoverEconomyRequest(); ConfirmRecoveryGuild();
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.DonateToGuild));
            Assert.That(_net.Requests.Last().ToByteArray(), Is.EqualTo(SavedBytes(original)));
            Assert.That(_files.Load(1), Is.EqualTo(original));
        }

        [TestCase(true, true)] [TestCase(true, false)] [TestCase(false, true)] [TestCase(false, false)]
        public void ConfirmingAnOldWarningCannotReplayAReplacementValidRequest(bool corruptWarning, bool replacementDonation)
        {
            _client.Donate(2); ulong oldKey = LastKey(); string original = _files.Load(1);
            _net.Reply(new DonateToGuildResponse());
            string warned = original;
            if (corruptWarning)
            {
                var fields = original.Split(':'); fields[5] = "not-base64";
                warned = string.Join(":", fields);
                _files.Transition(1, original, warned);
            }
            else _now += GuildClient.EconomyReplayWindowMs;
            _client.RecoverEconomyRequest();
            Assert.That(_client.Status, Does.Contain("再点一次"));

            // 确认授权属于刚看到的旧记录,不能被另一进程替换的新合法请求借来发送。
            ulong replacementKey = oldKey == 17 ? 18ul : 17ul;
            uint method = replacementDonation ? MessageIds.DonateToGuild : MessageIds.BuyGuildShopGoods;
            IMessage request = replacementDonation
                ? new DonateToGuildRequest { DonateId = 3, ClientRequestId = replacementKey, ExpectedGuildId = 555 }
                : new BuyGuildShopGoodsRequest { GoodsId = 101, Count = 2, ClientRequestId = replacementKey, ExpectedGuildId = 555 };
            string replacement = string.Join(":", "G1", "1", "555", _now.ToString(CultureInfo.InvariantCulture),
                method.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(request.ToByteArray()));
            _files.Transition(1, warned, replacement);
            int before = _net.Calls.Count;
            _client.RecoverEconomyRequest();
            Assert.That(_files.Load(1), Is.EqualTo(replacement));
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Status, Does.Contain("变化"));

            _client.RecoverEconomyRequest(); ConfirmRecoveryGuild();
            Assert.That(_net.Calls.Last(), Is.EqualTo(method));
            Assert.That(_net.Requests.Last().ToByteArray(), Is.EqualTo(SavedBytes(replacement)));
            Assert.That(LastKey(), Is.EqualTo(replacementKey));
            Assert.That(_files.Load(1), Is.EqualTo(replacement));
        }

        [Test] public void StaleConfirmationDoesNotDeleteAReplacementRecord()
        {
            _client.Donate(2); string original = _files.Load(1);
            _net.Reply(new DonateToGuildResponse());
            _now += GuildClient.EconomyReplayWindowMs;
            _client.RecoverEconomyRequest();
            // 另一进程替换了槽位:旧警告只能确认当时看到的记录,不能授权删除新内容。
            var fields = original.Split(':');
            fields[3] = (long.Parse(fields[3], CultureInfo.InvariantCulture) - 1).ToString(CultureInfo.InvariantCulture);
            string replacement = string.Join(":", fields);
            _files.Transition(1, original, replacement);
            int before = _net.Calls.Count;
            _client.RecoverEconomyRequest();
            Assert.That(_files.Load(1), Is.EqualTo(replacement));
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Status, Does.Contain("再点一次"));
        }

        [TestCase(true)] [TestCase(false)]
        public void ExpiredPayloadNeedNotParseBeforeConfirmedRetirement(bool donation)
        {
            Send(donation); string original = _files.Load(1);
            ReplyWithoutOrder(donation);
            var fields = original.Split(':'); fields[5] = "not-base64";
            string corruptPayload = string.Join(":", fields);
            _files.Transition(1, original, corruptPayload);
            _now += GuildClient.EconomyReplayWindowMs;
            int before = _net.Calls.Count;
            _client.RecoverEconomyRequest();
            Assert.That(_files.Load(1), Is.EqualTo(corruptPayload));
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Status, Does.Contain("再点一次"));
            _client.RecoverEconomyRequest();
            Assert.That(_files.Load(1), Is.Empty);
            Assert.That(_net.Calls.Last(), Is.EqualTo(donation ? MessageIds.GetGuildDonateOptions : MessageIds.GetGuildShop));
            Assert.That(_net.Calls.Count(call => call == (donation ? MessageIds.DonateToGuild : MessageIds.BuyGuildShopGoods)), Is.EqualTo(1));
        }

        [Test] public void ExpiredRecordWithUnknownMethodRequiresCorruptionConfirmation()
        {
            string corrupt = "G1:1:555:1700000000000:0:not-base64";
            _files.Save(1, corrupt); _now += GuildClient.EconomyReplayWindowMs;
            int before = _net.Calls.Count;
            _client.RecoverEconomyRequest();
            Assert.That(_files.Load(1), Is.EqualTo(corrupt));
            Assert.That(_client.HasPendingEconomyWrite, Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Status, Does.Contain("损坏").And.Contain("再点一次"));
            _client.RecoverEconomyRequest();
            Assert.That(_files.Load(1), Is.Empty);
            Assert.That(_net.Calls.Count, Is.EqualTo(before + 1));
            ReplyAfterCorruptRetirement();
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
            Assert.That(_client.Status, Does.Contain("再点一次"));
            _client.RecoverEconomyRequest();
            Assert.That(_files.Load(1), Is.Empty);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildDonateOptions));
            Assert.That(_net.Calls.Count(call => call == MessageIds.DonateToGuild), Is.EqualTo(1));
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
            private readonly Dictionary<uint, Action<MessageContent>> _notifications = new();
            public Action<uint, IMessage> BeforeCall;
            public Action<IMessage> Pending;
            public Action<string> Error;
            public void Reply(IMessage response) => Pending(response);
            public void Notify(uint id, IMessage message) =>
                _notifications[id](new MessageContent { MessageId = id, SerializedMessage = message.ToByteString() });
            public void RegisterNotify(uint id, Action<MessageContent> callback) => _notifications[id] = callback;
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
