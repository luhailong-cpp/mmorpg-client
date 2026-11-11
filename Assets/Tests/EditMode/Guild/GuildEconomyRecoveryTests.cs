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
        // 注入的单调时钟:作废确认要在警告写出满 EconomyRetireConfirmDelayMs 之后,用例用 PassConfirmDelay 表示「读完警告再点」。
        private long _monoNow;

        [SetUp] public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "guild-economy-" + Guid.NewGuid().ToString("N"));
            _files = DurableRequestFileStore.ForOrigin(_directory, "https://guild-test.invalid");
            _net = new RecoveryTransport(); _identity = new object(); _now = 1700000000000; _monoNow = 10000;
            _client = CreateClient();
            Load();
        }
        [TearDown] public void TearDown()
        {
            _client.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
        private GuildClient CreateClient(Func<long> economyUtcMs = null) => new GuildClient(_net, () => _identity,
            monotonicMs: () => _monoNow, economyRecovery: _files, economyUtcMs: economyUtcMs ?? (() => _now));
        private void Load()
        {
            _client.Refresh();
            _net.Reply(new GetPlayerGuildResponse { Guild = GuildClientTests.Fixture() });
        }
        // 玩家读完警告再点:把单调时钟拨过作废确认的最短间隔(05-economy.md §5.44.2 末段)。在确认作废的那一击之前调用。
        private void PassConfirmDelay() => _monoNow += GuildClient.EconomyRetireConfirmDelayMs;
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
        // 一条超窗记录:捐献发出后没拿到订单视图,随后本机时间走过安全恢复期。返回记录文件路径。
        private string SeedStaleRecord()
        {
            _client.Donate(2); _net.Reply(new DonateToGuildResponse());
            _now += GuildClient.EconomyReplayWindowMs;
            return Directory.GetFiles(_directory, "1.pending", SearchOption.AllDirectories).Single();
        }
        // 作废确认那一击之后的回包:损坏记录只读刷新帮会;超窗的捐献记录带着结论重拉捐献页。
        private void ReplyAfterRetirement(bool corrupt)
        {
            if (corrupt) { ReplyAfterCorruptRetirement(); return; }
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildDonateOptions));
            ReplyPageReload();
            Assert.That(_client.Status, Does.Contain("已作废超期"));
        }
        // 照 GuildUiRoot 接线:窗口的恢复与本页读取事件进客户端,客户端的 Changed 重建窗口。窗口停在捐献 / 商店页时,
        // 本页快照一清空,下一次重建末尾就当场自动补拉(GuildWindow.MaybeAutoRequest)。body 拿到的是点「刷新 / 恢复原请求」键。
        private void WithWiredWindow(GuildPage page, Action<Action> body)
        {
            var root = new GameObject("GuildRecoveryNoticeWindow", typeof(RectTransform));
            GuildWindow window = null;
            try
            {
                var design = QdaoUguiFactory.CreateCenteredRect("Design", root.transform, 2560, 1080);
                window = new GuildWindow(design);
                var wired = window;
                window.EconomyRecoveryRequested += _client.RecoverEconomyRequest;
                window.DonationsRequested += _client.RefreshDonations;
                window.ShopRequested += _client.RefreshShop;
                _client.Changed += () => wired.SetClient(_client);
                window.SetClient(_client); window.Show(page);
                var refresh = root.GetComponentsInChildren<Button>(true).Single(value => value.name == "RefreshGuild");
                body(refresh.onClick.Invoke);
            }
            finally { window?.Hide(); UnityEngine.Object.DestroyImmediate(root); }
        }
        private void ReplyEconomyPage(bool donation)
        {
            Assert.That(_net.Calls.Last(), Is.EqualTo(donation ? MessageIds.GetGuildDonateOptions : MessageIds.GetGuildShop));
            if (donation) _net.Reply(GuildClientTests.DonateFixture());
            else _net.Reply(GuildClientTests.ShopFixture(440));
        }
        // 预读发现已在别的帮(556):原帮会那一笔回 14002,记录作废,结论写进状态栏。
        private string ConcludeReplayInAnotherGuild()
        {
            Send(true); ReplyWithoutOrder(true);
            _client.RecoverEconomyRequest(); ConfirmRecoveryGuild(556);
            _net.Reply(new DonateToGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } });
            Assert.That(_client.Status, Does.Contain("已不在原帮会"));
            return _client.Status;
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
        public void QueuedApplicationsRefreshKeepsTheOldGuildConclusion(bool donation, bool hasOrder)
        {
            string conclusion = ConcludeRecoveryAfterLeaving(donation, hasOrder);
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.ListMyGuildApplications));
            _net.Reply(new ListMyGuildApplicationsResponse());
            Assert.That(_client.Status, Is.EqualTo(conclusion));
            Assert.That(_client.MyApplications, Is.Not.Null);
            Assert.That(_client.MyApplicationsQueued, Is.False);

            // 列表读取只换数据不换文案:玩家之后的操作改写了文案,再拉列表就保留新文案,不会翻出旧结论,也不停在「正在读取」。
            _client.Browse(); _net.Reply(new GetGuildRankResponse());
            string browsed = _client.Status;
            Assert.That(browsed, Is.Not.EqualTo(conclusion));
            _client.LoadMyApplications();
            _net.Reply(new ListMyGuildApplicationsResponse());
            Assert.That(_client.Status, Is.EqualTo(browsed));
        }

        [Test] public void AnotherUserActionBeforeApplicationsRefreshSupersedesTheRecoveryConclusion()
        {
            string conclusion = ConcludeRecoveryAfterLeaving(true, true);
            _client.RecoverEconomyRequest();
            string superseding = _client.Status;
            Assert.That(superseding, Does.Contain("没有待确认"));
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.ListMyGuildApplications));
            _net.Reply(new ListMyGuildApplicationsResponse());
            Assert.That(_client.Status, Is.EqualTo(superseding));
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
            Assert.That(_client.Status, Is.EqualTo("尚未加入帮会，和同道相聚于此。"));
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
            // 模拟其它进程已确认旧单并占用新请求槽；旧回包的CAS必须拒绝，且只提示记录已变化，
            // 不能说成本机故障叫玩家「恢复查账」(那一击恢复的是换进来的新请求)。
            _files.Clear(1, original); _files.Save(1, replacement);
            oldReply(new DonateToGuildResponse { Donation = new GuildDonationView
                { OpId = 9, DonateId = 2, Status = GuildAssetOrderStatus.Applied } });
            Assert.That(_files.Load(1), Is.EqualTo(replacement));
            Assert.That(_client.Status, Does.Contain("已变化").And.Not.Contain("尚未清理"));
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
            PassConfirmDelay();
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
            PassConfirmDelay();
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
            PassConfirmDelay();
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
            PassConfirmDelay();
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
            PassConfirmDelay();
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
            PassConfirmDelay();
            _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.False);
            ReplyAfterCorruptRetirement();
        }

        [Test] public void AFailedCorruptFileDeletionRequiresFreshConfirmationAfterUnlocking()
        {
            string path = SeedRawRecoveryFile(new byte[0]);
            _client.RecoverEconomyRequest();
            int before = _net.Calls.Count;
            // 允许读取和计算指纹,但不给 Delete 共享权限,确定性复现清除失败(满确认间隔后的这一击才会走到删除)。
            using (var blocker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                PassConfirmDelay();
                _client.RecoverEconomyRequest();
                Assert.That(File.Exists(path), Is.True);
                Assert.That(_client.HasPendingEconomyWrite, Is.True);
                Assert.That(_net.Calls.Count, Is.EqualTo(before));
            }
            _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.True);
            Assert.That(_client.Status, Does.Contain("再点一次"));
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            PassConfirmDelay();
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
            PassConfirmDelay();
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
            PassConfirmDelay();
            _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.False);
            ReplyAfterCorruptRetirement();
        }

        [Test] public void CorruptRetirementConclusionSurvivesNotInGuildAndOneApplicationsRefresh()
        {
            string path = SeedRawRecoveryFile(new byte[0]);
            _client.RecoverEconomyRequest(); PassConfirmDelay(); _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.False);
            ReplyAfterCorruptRetirement(false);
            string conclusion = _client.Status;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.ListMyGuildApplications));
            _net.Reply(new ListMyGuildApplicationsResponse());
            Assert.That(_client.Status, Is.EqualTo(conclusion));
            // 之后的操作改写了文案,列表重拉就保留新文案,不会把旧结论翻出来。
            _client.Browse(); _net.Reply(new GetGuildRankResponse());
            string browsed = _client.Status;
            _client.LoadMyApplications(); _net.Reply(new ListMyGuildApplicationsResponse());
            Assert.That(_client.Status, Is.EqualTo(browsed).And.Not.EqualTo(conclusion));
        }

        [Test] public void AnApplicationPushDuringRefreshSupersedesTheCorruptRetirementConclusion()
        {
            SeedRawRecoveryFile(new byte[0]);
            _client.RecoverEconomyRequest(); PassConfirmDelay(); _client.RecoverEconomyRequest();
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
            _client.RecoverEconomyRequest(); PassConfirmDelay(); _client.RecoverEconomyRequest();
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
            _client.RecoverEconomyRequest(); PassConfirmDelay(); _client.RecoverEconomyRequest();
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

            PassConfirmDelay();
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
            PassConfirmDelay();
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

        // 确认那一击读完快照之后、持锁复核之前,另一个守 .lock 协议的进程把槽位换成了窗口内的合法新请求(交接 §8.1 第 5 条)。
        // 这一击只保留新记录并提示变化,不能按存储故障引导玩家「重新确认」;下一击才由玩家主动恢复,发的是新记录自己的键。
        [TestCase(true)] [TestCase(false)]
        public void ReplacementDuringTheConfirmingClickOnlyReportsTheChange(bool corruptWarning)
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
            ulong replacementKey = oldKey == 17 ? 18ul : 17ul;
            var replacementRequest = new DonateToGuildRequest { DonateId = 3, ClientRequestId = replacementKey, ExpectedGuildId = 555 };
            string replacement = string.Join(":", "G1", "1", "555", _now.ToString(CultureInfo.InvariantCulture),
                MessageIds.DonateToGuild.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(replacementRequest.ToByteArray()));
            // 记录头校验后判恢复窗时读一次时钟,正落在「读完快照、持锁复核之前」:借这次读取按 .lock 协议换掉槽位。
            bool replaceOnNextClockRead = false;
            _client.Dispose();
            _client = CreateClient(() =>
            {
                if (replaceOnNextClockRead) { replaceOnNextClockRead = false; _files.Transition(1, warned, replacement); }
                return _now;
            });
            Load();
            _client.RecoverEconomyRequest();
            Assert.That(_client.Status, Does.Contain("再点一次"));

            int before = _net.Calls.Count;
            PassConfirmDelay();
            replaceOnNextClockRead = true;
            _client.RecoverEconomyRequest();
            Assert.That(replaceOnNextClockRead, Is.False, "确认那一击没有在两次读取之间读时钟,本用例没有构造出替换竞态");
            Assert.That(_files.Load(1), Is.EqualTo(replacement));
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Status, Does.Contain("已变化").And.Contain("未作废也未重发").And.Not.Contain("存储"));

            _client.RecoverEconomyRequest(); ConfirmRecoveryGuild();
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.DonateToGuild));
            Assert.That(_net.Requests.Last().ToByteArray(), Is.EqualTo(SavedBytes(replacement)));
            Assert.That(LastKey(), Is.EqualTo(replacementKey));
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
            PassConfirmDelay();
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
            PassConfirmDelay();
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
            PassConfirmDelay();
            _client.RecoverEconomyRequest();
            Assert.That(_files.Load(1), Is.Empty);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildDonateOptions));
            Assert.That(_net.Calls.Count(call => call == MessageIds.DonateToGuild), Is.EqualTo(1));
        }

        // 作废确认须在警告写出满 1 秒之后(05-economy.md §5.44.2 末段)。差 1 毫秒的那一击不算确认:不作废、不发包、状态栏照旧;
        // 它也不重新计时 —— 从警告写出算起刚满 1 秒的下一击就作废。
        [TestCase(true)] [TestCase(false)]
        public void AClickBeforeTheWarningHasShownForOneSecondIsNotAConfirmation(bool corrupt)
        {
            string path = corrupt ? SeedRawRecoveryFile(new byte[0]) : SeedStaleRecord();
            _client.RecoverEconomyRequest();
            string warning = _client.Status;
            Assert.That(warning, Does.Contain("再点一次"));
            int before = _net.Calls.Count;
            _monoNow += GuildClient.EconomyRetireConfirmDelayMs - 1;
            _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.True);
            Assert.That(_client.HasPendingEconomyWrite, Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Status, Is.EqualTo(warning));
            _monoNow += 1;
            _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(before + 1));
            ReplyAfterRetirement(corrupt);
        }

        // 文案变了要重新警告,1 秒从这次重新写出警告时起算,不从第一次警告算。
        [TestCase(true)] [TestCase(false)]
        public void AWarningShownAgainRestartsTheOneSecondCount(bool corrupt)
        {
            string path = corrupt ? SeedRawRecoveryFile(new byte[0]) : SeedStaleRecord();
            _client.RecoverEconomyRequest();
            _monoNow += 600;
            _client.Donate(3);
            Assert.That(_client.Status, Does.Contain("已有待确认"));
            _client.RecoverEconomyRequest();
            string warning = _client.Status;
            Assert.That(warning, Does.Contain("再点一次"));
            int before = _net.Calls.Count;
            // 距第一次警告已过 1200 ms,距这次重新写出只有 600 ms:仍不算确认。
            _monoNow += 600;
            _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Status, Is.EqualTo(warning));
            _monoNow += 400;
            _client.RecoverEconomyRequest();
            Assert.That(File.Exists(path), Is.False);
            ReplyAfterRetirement(corrupt);
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

        // 停在捐献 / 商店页作废损坏记录:作废清空了经济页快照,刷新回包后窗口当场补拉本页。作废结论要活过这一发,且只活这一次。
        // 同一用例也守住按钮连点:警告写出不满 1 秒的第二下不算确认(05-economy.md §5.44.2 末段)。
        [TestCase(true)] [TestCase(false)]
        public void CorruptRetirementConclusionSurvivesTheVisiblePageReload(bool donatePage)
        {
            WithWiredWindow(donatePage ? GuildPage.Donate : GuildPage.Shop, clickRefresh =>
            {
                ReplyEconomyPage(donatePage);
                string path = SeedRawRecoveryFile(new byte[0]);
                clickRefresh();
                Assert.That(_client.Status, Does.Contain("再点一次"));
                int beforeDoubleClick = _net.Calls.Count;
                clickRefresh();
                Assert.That(File.Exists(path), Is.True);
                Assert.That(_net.Calls.Count, Is.EqualTo(beforeDoubleClick));
                Assert.That(_client.Status, Does.Contain("再点一次"));
                PassConfirmDelay();
                clickRefresh();
                Assert.That(File.Exists(path), Is.False);
                Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
                _net.Reply(new GetPlayerGuildResponse { Guild = GuildClientTests.Fixture() });
                ReplyEconomyPage(donatePage);
                Assert.That(_client.Status, Does.Contain("已作废损坏").And.Contain("不能据此判断是否到账"));
                clickRefresh();
                ReplyEconomyPage(donatePage);
                Assert.That(_client.Status, Does.Not.Contain("已作废"));
            });
        }

        // 恢复时预读发现已在别的帮:换帮清空了两页快照,原帮会那一笔的结论要活过窗口对现帮会本页的补拉。
        [TestCase(true)] [TestCase(false)]
        public void AnotherGuildReplayConclusionSurvivesTheVisiblePageReload(bool donation)
        {
            WithWiredWindow(donation ? GuildPage.Donate : GuildPage.Shop, clickRefresh =>
            {
                ReplyEconomyPage(donation);
                Send(donation); ReplyWithoutOrder(donation);
                clickRefresh(); ConfirmRecoveryGuild(556);
                Assert.That(_net.Calls.Last(), Is.EqualTo(donation ? MessageIds.DonateToGuild : MessageIds.BuyGuildShopGoods));
                var tip = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild };
                if (donation) _net.Reply(new DonateToGuildResponse { ErrorMessage = tip });
                else _net.Reply(new BuyGuildShopGoodsResponse { ErrorMessage = tip });
                Assert.That(_files.Load(1), Is.Empty);
                ReplyEconomyPage(donation);
                Assert.That(_client.Info.GuildId, Is.EqualTo(556ul));
                Assert.That(_client.Status, Does.Contain("已不在原帮会"));
            });
        }

        // 最常见的恢复路径:写请求超时、重连后在原帮会重发,原请求被业务拒绝。重连清空了两页快照,拒绝原因要活过本页补拉。
        [TestCase(true)] [TestCase(false)]
        public void RejectedReplayAfterReconnectKeepsItsReasonThroughTheVisiblePageReload(bool donation)
        {
            WithWiredWindow(donation ? GuildPage.Donate : GuildPage.Shop, clickRefresh =>
            {
                ReplyEconomyPage(donation);
                Send(donation); _net.Error("rpc timeout");
                _identity = new object(); _client.ObserveConnection();
                clickRefresh(); ConfirmRecoveryGuild();
                var tip = new TipInfoMessage { Id = (uint)(donation ? guild_error.KGuildDonateLimit : guild_error.KGuildShopLimit) };
                if (donation) _net.Reply(new DonateToGuildResponse { ErrorMessage = tip });
                else _net.Reply(new BuyGuildShopGoodsResponse { ErrorMessage = tip });
                Assert.That(_files.Load(1), Is.Empty);
                ReplyEconomyPage(donation);
                Assert.That(_client.Status, Is.EqualTo(GuildClient.TipText(tip)));
            });
        }

        // 预读期间越过了安全恢复期:重发前才写出超窗警告,重连清空了捐献页,窗口当场补拉时把这句警告沿用回状态栏。
        // 沿用不重新计时 —— 从警告真正写出算起满 1 秒,玩家这一击就作废(补拉落定在中途,若从那时重算就还差 500 ms)。
        [TestCase(true)] [TestCase(false)]
        public void ARestoredStaleWarningKeepsItsOriginalConfirmTime(bool donation)
        {
            WithWiredWindow(donation ? GuildPage.Donate : GuildPage.Shop, clickRefresh =>
            {
                ReplyEconomyPage(donation);
                Send(donation); _net.Error("rpc timeout");
                _identity = new object(); _client.ObserveConnection();
                clickRefresh();
                _now += GuildClient.EconomyReplayWindowMs;
                ConfirmRecoveryGuild();
                _monoNow += 500;
                ReplyEconomyPage(donation);
                Assert.That(_client.Status, Does.Contain("再点一次"));
                _monoNow += 500;
                clickRefresh();
                Assert.That(_files.Load(1), Is.Empty);
                Assert.That(_net.Calls.Last(), Is.EqualTo(donation ? MessageIds.GetGuildDonateOptions : MessageIds.GetGuildShop));
            });
        }

        // 空回包仍需保留原记录,提示在同步通知窗口前登记,否则窗口当场补拉会盖掉这句恢复结论。
        [TestCase(true)] [TestCase(false)]
        public void UnconfirmedReplayConclusionSurvivesTheVisiblePageReload(bool donation)
        {
            WithWiredWindow(donation ? GuildPage.Donate : GuildPage.Shop, clickRefresh =>
            {
                ReplyEconomyPage(donation);
                Send(donation); _net.Error("rpc timeout");
                string saved = _files.Load(1);
                _identity = new object(); _client.ObserveConnection();
                clickRefresh(); ConfirmRecoveryGuild();
                ReplyWithoutOrder(donation);
                ReplyEconomyPage(donation);
                Assert.That(_client.Status, Does.Contain("尚未取得捐献或兑换的订单视图"));
                Assert.That(_client.Status, Does.Contain("恢复原请求确认结果"));
                Assert.That(_files.Load(1), Is.EqualTo(saved));
                Assert.That(_net.Calls.Count(id => id == (donation ? MessageIds.DonateToGuild : MessageIds.BuyGuildShopGoods)), Is.EqualTo(2));
            });
        }

        // 换帮也清空了活动快照:停在活动页时窗口排一次重拉(QueueActivities → DrainQueued),那一发同样沿用结论一次。
        [Test] public void AnotherGuildReplayConclusionSurvivesTheFirstActivitiesReload()
        {
            string conclusion = ConcludeReplayInAnotherGuild();
            _client.QueueActivities(); _client.DrainQueued(false, true);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildActivities));
            _net.Reply(GuildClientTests.ActivitiesFixture());
            Assert.That(_client.Status, Is.EqualTo(conclusion));
            _client.RefreshActivities(); _net.Reply(GuildClientTests.ActivitiesFixture());
            Assert.That(_client.Status, Is.Not.EqualTo(conclusion));
        }

        // 补拉在途期间来了推送、改写了状态栏:让给新通知,不再恢复旧结论(与本人申请列表同一规则)。
        [Test] public void APushDuringTheFirstPageReloadSupersedesTheRecoveryConclusion()
        {
            string conclusion = ConcludeReplayInAnotherGuild();
            _client.RefreshDonations();
            _net.Notify(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 556, TargetPlayerId = 1, Kind = GuildChangeKind.ActivityChanged });
            Assert.That(_client.Status, Does.Contain("历练邀请"));
            _net.Reply(GuildClientTests.DonateFixture());
            Assert.That(_client.Status, Is.Not.EqualTo(conclusion));
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
