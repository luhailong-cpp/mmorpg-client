using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Guildpb;
using MmorpgClient.Core;
using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Guild;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MmorpgClient.Tests.PlayMode
{
    /// <summary>显式运行的真联网验收；服务必须为本机 dev/test，账号必须从未建角。</summary>
    public sealed class GuildEconomyNetworkSmokeTests
    {
        // 与服务端 robot/guild_economy_smoke.go 使用同一组配表验收值；变化时明确失败。
        private const uint DonateId = 2, GoodsId = 101, RewardItemId = 15;
        private const ulong DonationCost = 100000, ContributionGain = 120, FundsGain = 12000, ShopCost = 30;
        private const uint RewardCount = 5;
        private const double RpcSeconds = 25, SettlementSeconds = 45;
        private GameObject _root;
        private GuildEconomySmokePump _pump;
        private GameClient _game;
        private GuildClient _guild;
        private ObservedGuildTransport _net;
        private DurableRequestFileStore _store;
        private StreamWriter _journal;
        private string _output, _pending, _stage;
        private uint _zone;
        private ulong _ownGuild, _previousLastPlayer;
        private bool _restoreLastPlayer, _passed;
        // 最近一次货币快照的槽位数;写过货币之后只增不减(见 Silver)。
        private int _currencySlots;

        [UnityTest, Explicit("必须显式选择此用例并设置 MMORPG_GUILD_SMOKE_ENABLE=1；只连接本机 dev/test 服务")]
        public IEnumerator CorruptRecordRetirementAllowsRealDonationAndRedemption()
        {
            Assert.That(RequiredEnvironment("MMORPG_GUILD_SMOKE_ENABLE"), Is.EqualTo("1"), "未显式启用联网验收");
            string gateway = RequiredEnvironment("MMORPG_GUILD_SMOKE_GATEWAY");
            Assert.That(Uri.TryCreate(gateway, UriKind.Absolute, out var uri), Is.True, "网关地址必须是绝对 URL");
            Assert.That((uri.Scheme == "http" || uri.Scheme == "https") && string.IsNullOrEmpty(uri.UserInfo)
                && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
                && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip)),
                Is.True, "仅允许无凭据的 loopback 网关入口");
            string account = RequiredEnvironment("MMORPG_GUILD_SMOKE_ACCOUNT");
            Assert.That(Regex.IsMatch(account, "^robot_guild_recovery_[a-f0-9]{16,32}$"), Is.True,
                "必须使用本轮独立的 robot_guild_recovery_<16至32位随机十六进制> 账号");
            string password = RequiredEnvironment("MMORPG_GUILD_SMOKE_PASSWORD");
            Assert.That(uint.TryParse(RequiredEnvironment("MMORPG_GUILD_SMOKE_ZONE"), out _zone) && _zone != 0,
                Is.True, "必须指定有效区服");
            PrepareEvidence();
            _root = GuildEconomySmokeStartup.ClaimOwnedRoot();
            _pump = _root.GetComponent<GuildEconomySmokePump>();
            Note("启动隔离", "owned_host=true app_bootstrap=0 core_bootstrap=0 sandbox_started=false");
            string device = "guild-smoke-" + Guid.NewGuid().ToString("N");

            // PlayerChooser 仅提供所选区的角色；先用正式 HTTP 契约核对整个账号没有角色。
            GatewayLoginResult preflight = null;
            bool preflightDone = false, preflightFailed = false;
            _pump.StartCoroutine(new GatewayHttpClient(gateway).Login(new GatewayLoginRequest
            {
                zone_id = _zone, account = account, password = password, auth_type = "password", device_id = device
            }, response => { preflight = response; preflightDone = true; },
                _ => { preflightFailed = true; preflightDone = true; }));
            yield return WaitFor(() => preflightDone, "预检独立账号", RpcSeconds);
            Assert.That(preflightFailed, Is.False, "账号预检 HTTP 失败；不输出可能含凭据的响应原文");
            Assert.That(preflight, Is.Not.Null);
            Assert.That(preflight.code, Is.EqualTo(GatewayLoginResult.CodeOk), "账号预检未成功");
            Assert.That(preflight.players == null || preflight.players.Length == 0, Is.True, "拒绝使用已有角色的账号");
            preflight = null;

            Assert.That(GameObject.Find("[ActorWorld]"), Is.Null, "请在独立测试进程运行，不能接管已有游戏世界");
            _previousLastPlayer = ClientSettings.GetLastPlayer(_zone);
            _restoreLastPlayer = true;
            _game = new GameClient(gateway) { CoroutineRunner = _pump.StartCoroutine };
            _game.World.SetRootParent(_root.transform);
            _pump.Tick = _game.Tick;
            int chooserCalls = 0;
            _game.PlayerChooser = (zone, roles, choice) => ChooseNewRole(zone, roles, choice, ++chooserCalls);
            bool entered = false, loginFailed = false;
            _pump.StartCoroutine(_game.EnterZone(_zone, account, password, device,
                () => entered = true, _ => loginFailed = true));
            yield return WaitFor(() => entered || loginFailed, "正式客户端登录建角入场", 75);
            password = null;
            Assert.That(loginFailed, Is.False, "正式登录失败；未重用角色或自动重试建角");
            Assert.That(chooserCalls, Is.EqualTo(1), "必须恰好新建一个角色");
            Assert.That(_game.InGame && _game.IsGateReady && _game.PlayerId != 0, Is.True);
            Note("新角色", "player=" + _game.PlayerId.ToString(CultureInfo.InvariantCulture));

            string recovery = Path.Combine(_output, "recovery");
            _store = DurableRequestFileStore.ForOrigin(recovery, gateway);
            _net = new ObservedGuildTransport(new GameClientBattleTransport(_game), Note);
            _guild = new GuildClient(_net, () => _game.GateConnectionIdentity, economyRecovery: _store);
            yield return GuildCall(_guild.Refresh, MessageIds.GetPlayerGuild, "读取新角色帮会");
            Assert.That(_net.Response<GetPlayerGuildResponse>(MessageIds.GetPlayerGuild).ErrorMessage?.Id ?? 0,
                Is.EqualTo((uint)guild_error.KGuildNotInGuild));
            Assert.That(_guild.Info, Is.Null, "新角色不应已有帮会");
            yield return GuildCall(() => _guild.Create("恢复验收" + Guid.NewGuid().ToString("N").Substring(0, 12)),
                MessageIds.CreateGuild, "创建本次专用帮会");
            AssertTip(_net.Response<CreateGuildResponse>(MessageIds.CreateGuild).ErrorMessage);
            Assert.That(_guild.Info, Is.Not.Null);
            _ownGuild = _guild.Info.GuildId;
            Assert.That(_ownGuild, Is.Not.Zero);
            Assert.That(_guild.Info.LeaderId, Is.EqualTo(_game.PlayerId));
            Assert.That(_guild.Info.Members.Count, Is.EqualTo(1));
            Assert.That(_guild.Info.Funds, Is.Zero);
            Note("专用帮会", "guild=" + _ownGuild.ToString(CultureInfo.InvariantCulture));

            yield return ReadDonations();
            yield return ReadShop();
            AssertDonationOption();
            AssertShopGoods();
            Assert.That(DonationOption().UsedToday, Is.Zero);
            Assert.That(ShopGoods().UsedCount, Is.Zero);
            Assert.That(_guild.Donations.PendingDonations, Is.Empty);
            Assert.That(_guild.Shop.PendingOrders, Is.Empty);
            ulong fundsBefore = _guild.Info.Funds, balanceBefore = Me().ContributionBalance, totalBefore = Me().ContributionTotal;
            GetBagResponse initialBag = null;
            yield return ReadBag(response => initialBag = response);
            ulong silverBefore = SilverBeforeFirstWrite(initialBag);
            if (silverBefore < DonationCost)
            {
                GmAddCurrencyResponse granted = null;
                yield return Rpc(SceneCurrencyClientPlayerRpc.GmAddCurrencyMessageId,
                    new GmAddCurrencyRequest { CurrencyType = 0, Amount = checked((long)(DonationCost - silverBefore)) },
                    GmAddCurrencyResponse.Parser, response => granted = response, "GM仅补足自有角色银两");
                AssertTip(granted.ErrorMessage);
                Assert.That(granted.BalanceAfter, Is.EqualTo(DonationCost));
                yield return ReadBag(response => initialBag = response);
                silverBefore = Silver(initialBag);
                Assert.That(silverBefore, Is.EqualTo(DonationCost));
            }
            ulong itemsBefore = ItemCount(initialBag);
            Note("初始资产", "silver=" + silverBefore + " funds=" + fundsBefore + " contribution=" + balanceBefore
                + " contribution_total=" + totalBefore + " item_15=" + itemsBefore);

            yield return RetireCorruptRecord(Array.Empty<byte>(), "捐献前空文件");
            yield return ReadDonations();
            yield return ReadShop();
            yield return GuildCall(() => _guild.Donate(DonateId), MessageIds.DonateToGuild, "作废后新捐献");
            var donationRequest = _net.Request<DonateToGuildRequest>(MessageIds.DonateToGuild);
            var donationReply = _net.Response<DonateToGuildResponse>(MessageIds.DonateToGuild);
            Assert.That(donationRequest.ClientRequestId, Is.Not.Zero);
            Assert.That(donationRequest.ExpectedGuildId, Is.EqualTo(_ownGuild));
            AssertTip(donationReply.ErrorMessage);
            Assert.That(donationReply.Donation, Is.Not.Null);
            Assert.That(donationReply.Donation.OpId, Is.Not.Zero);
            Assert.That(donationReply.Donation.DonateId, Is.EqualTo(DonateId));
            Assert.That(donationReply.Donation.CostAmount, Is.EqualTo(DonationCost));
            Assert.That(donationReply.Donation.ContributionGain, Is.EqualTo(ContributionGain));
            Assert.That(donationReply.Donation.FundsGain, Is.EqualTo(FundsGain));
            Note("新捐献请求", "client_request=" + donationRequest.ClientRequestId + " op=" + donationReply.Donation.OpId);
            yield return WaitDonationApplied(donationReply.Donation.OpId);
            yield return ReadGuild();
            GetBagResponse afterDonation = null;
            yield return ReadBag(response => afterDonation = response);
            // 只读一次:Silver 要求槽位按时间顺序只增不减,之后别再拿这份较早的快照去比。
            ulong silverAfterDonation = Silver(afterDonation);
            Assert.That(silverAfterDonation, Is.EqualTo(silverBefore - DonationCost));
            Assert.That(ItemCount(afterDonation), Is.EqualTo(itemsBefore));
            Assert.That(_guild.Info.Funds, Is.EqualTo(fundsBefore + FundsGain));
            Assert.That(Me().ContributionBalance, Is.EqualTo(balanceBefore + ContributionGain));
            Assert.That(Me().ContributionTotal, Is.EqualTo(totalBefore + ContributionGain));
            Assert.That(DonationOption().UsedToday, Is.EqualTo(1));
            Assert.That(_guild.Donations.ContributionBalance, Is.EqualTo(balanceBefore + ContributionGain));
            Assert.That(_guild.HasPendingEconomyWrite || File.Exists(_pending), Is.False);
            Note("捐献到账", "silver=" + silverAfterDonation + " funds=" + _guild.Info.Funds
                + " contribution=" + Me().ContributionBalance + " used_today=" + DonationOption().UsedToday);

            yield return RetireCorruptRecord(Encoding.UTF8.GetBytes("本轮损坏头部，不能重发"), "兑换前坏头文件");
            yield return ReadDonations();
            yield return ReadShop();
            AssertShopGoods();
            Assert.That(ShopGoods().UsedCount, Is.Zero);
            yield return GuildCall(() => _guild.Buy(GoodsId), MessageIds.BuyGuildShopGoods, "作废后新兑换");
            var shopRequest = _net.Request<BuyGuildShopGoodsRequest>(MessageIds.BuyGuildShopGoods);
            var shopReply = _net.Response<BuyGuildShopGoodsResponse>(MessageIds.BuyGuildShopGoods);
            Assert.That(shopRequest.ClientRequestId, Is.Not.Zero.And.Not.EqualTo(donationRequest.ClientRequestId));
            Assert.That(shopRequest.ExpectedGuildId, Is.EqualTo(_ownGuild));
            AssertTip(shopReply.ErrorMessage);
            Assert.That(shopReply.Order, Is.Not.Null);
            Assert.That(shopReply.Order.OpId, Is.Not.Zero.And.Not.EqualTo(donationReply.Donation.OpId));
            Assert.That(shopReply.Order.GoodsId, Is.EqualTo(GoodsId));
            Assert.That(shopReply.Order.Count, Is.EqualTo(1));
            Assert.That(shopReply.Order.CostContribution, Is.EqualTo(ShopCost));
            Note("新兑换请求", "client_request=" + shopRequest.ClientRequestId + " op=" + shopReply.Order.OpId);
            yield return WaitShopApplied(shopReply.Order.OpId);
            yield return ReadGuild();
            GetBagResponse afterShop = null;
            yield return ReadBag(response => afterShop = response);
            Assert.That(Silver(afterShop), Is.EqualTo(silverAfterDonation));
            Assert.That(ItemCount(afterShop), Is.EqualTo(itemsBefore + RewardCount));
            Assert.That(_guild.Info.Funds, Is.EqualTo(fundsBefore + FundsGain));
            Assert.That(Me().ContributionBalance, Is.EqualTo(balanceBefore + ContributionGain - ShopCost));
            Assert.That(Me().ContributionTotal, Is.EqualTo(totalBefore + ContributionGain));
            Assert.That(_guild.Shop.ContributionBalance, Is.EqualTo(balanceBefore + ContributionGain - ShopCost));
            Assert.That(ShopGoods().UsedCount, Is.EqualTo(1));
            Assert.That(_guild.HasPendingEconomyWrite || File.Exists(_pending), Is.False);
            Assert.That(_net.Count(MessageIds.DonateToGuild), Is.EqualTo(1));
            Assert.That(_net.Count(MessageIds.BuyGuildShopGoods), Is.EqualTo(1));
            Note("兑换到账", "contribution=" + Me().ContributionBalance + " item_15=" + ItemCount(afterShop)
                + " used_count=" + ShopGoods().UsedCount + " distinct_new_keys=true");

            // 成功才解散本轮新帮；失败保留服务端状态与本机证据，不用清理覆盖验收失败。
            Assert.That(_guild.Info.GuildId, Is.EqualTo(_ownGuild));
            Assert.That(_guild.Info.LeaderId, Is.EqualTo(_game.PlayerId));
            Assert.That(_guild.Info.Members.Count, Is.EqualTo(1));
            yield return GuildCall(_guild.Disband, MessageIds.DisbandGuild, "清理本轮专用帮会");
            AssertTip(_net.Response<DisbandGuildResponse>(MessageIds.DisbandGuild).ErrorMessage);
            Assert.That(_net.Response<GetPlayerGuildResponse>(MessageIds.GetPlayerGuild).ErrorMessage?.Id ?? 0,
                Is.EqualTo((uint)guild_error.KGuildNotInGuild));
            Assert.That(_guild.Info, Is.Null);
            _passed = true;
            Note("通过", "corrupt_retirements=2 donation_writes=1 shop_writes=1 own_guild_disbanded=true");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // 不删除证据目录；异常也要断开本次连接，释放测试协程与真实世界对象。
            try
            {
                if (_pump != null) { _pump.Tick = null; _pump.StopAllCoroutines(); }
                try { _guild?.Dispose(); }
                finally { _game?.Disconnect(); }
            }
            finally
            {
                if (_restoreLastPlayer) ClientSettings.SetLastPlayer(_zone, _previousLastPlayer);
                GuildEconomySmokeStartup.ReleaseOwnedRoot();
                Note("结束", "passed=" + _passed + " last_stage=" + _stage);
                _journal?.Dispose();
                _journal = null;
            }
            yield return null;
        }

        private IEnumerator ChooseNewRole(uint zone, IReadOnlyList<AccountSimplePlayer> roles,
            GameClient.PlayerChoice choice, int invocation)
        {
            if (zone != _zone || roles.Count != 0 || invocation != 1 || !string.IsNullOrEmpty(choice.RejectHint))
            {
                choice.Cancelled = true;
                yield break;
            }
            choice.CreateNew = true;
            choice.ClassId = 1;
            choice.Gender = 1;
            choice.Name = ""; // 沿用正式无界面建角的服务端命名，不循环重试重名。
        }

        private void PrepareEvidence()
        {
            _output = Path.GetFullPath(RequiredEnvironment("MMORPG_GUILD_SMOKE_OUTPUT"));
            string logs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs")) + Path.DirectorySeparatorChar;
            Assert.That(_output.StartsWith(logs, StringComparison.OrdinalIgnoreCase), Is.True, "证据目录必须位于客户端 Logs 内");
            Assert.That(Directory.Exists(_output) || File.Exists(_output), Is.False, "本轮证据目录必须全新，防止复用旧记录");
            Directory.CreateDirectory(_output);
            _journal = new StreamWriter(new FileStream(Path.Combine(_output, "summary.txt"),
                FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
            Note("开始", "真实本机链路；只记录阶段、消息号、订单与资产数值，不记录口令或令牌");
        }

        private IEnumerator RetireCorruptRecord(byte[] bytes, string label)
        {
            Assert.That(_guild.Busy || _guild.HasPendingEconomyWrite, Is.False);
            Assert.That(_guild.Donations, Is.Not.Null);
            Assert.That(_guild.Shop, Is.Not.Null);
            _store.Save(_game.PlayerId, "本轮故障注入占位");
            _pending = Directory.GetFiles(Path.Combine(_output, "recovery"), "*.pending", SearchOption.AllDirectories).Single();
            File.WriteAllBytes(_pending, bytes);
            File.WriteAllBytes(Path.Combine(_output, label + ".bin"), bytes);
            int allBefore = _net.TotalCalls, writesBefore = _net.EconomyWrites, readsBefore = _net.Count(MessageIds.GetPlayerGuild);
            _stage = label + "首次警告";
            _guild.RecoverEconomyRequest();
            Assert.That(_guild.Busy, Is.False);
            Assert.That(_guild.HasPendingEconomyWrite, Is.True);
            Assert.That(File.ReadAllBytes(_pending), Is.EqualTo(bytes), "首次点击必须保留原字节");
            Assert.That(_net.TotalCalls, Is.EqualTo(allBefore), "首次警告不能发出任何帮会请求");
            StringAssert.Contains("损坏", _guild.Status);
            StringAssert.Contains("再点一次", _guild.Status);
            Note(_stage, "pending_retained=true guild_calls=0 economy_writes=0");

            // 紧接着再点一下(连点):警告写出不满 1 秒,不算确认(05-economy.md §5.44.2 末段)。
            string warning = _guild.Status;
            _stage = label + "连点第二下";
            _guild.RecoverEconomyRequest();
            Assert.That(File.ReadAllBytes(_pending), Is.EqualTo(bytes), "不满确认间隔的点击必须保留原字节");
            Assert.That(_net.TotalCalls, Is.EqualTo(allBefore), "不满确认间隔的点击不能发出任何帮会请求");
            Assert.That(_guild.Status, Is.EqualTo(warning), "不满确认间隔的点击不能改状态栏");
            Note(_stage, "pending_retained=true guild_calls=0");
            // 按真实时间等过确认间隔(客户端默认单调时钟);留 0.2 秒余量。期间状态栏必须仍是那句警告,否则下一击只会重新警告。
            double confirmAt = Time.realtimeSinceStartupAsDouble + GuildClient.EconomyRetireConfirmDelayMs / 1000.0 + 0.2;
            yield return WaitFor(() => Time.realtimeSinceStartupAsDouble >= confirmAt, label + "等满确认间隔", RpcSeconds);
            Assert.That(_guild.Status, Is.EqualTo(warning), "等待确认间隔期间状态栏被改写");

            _guild.RecoverEconomyRequest();
            Assert.That(File.Exists(_pending) || _guild.HasPendingEconomyWrite, Is.False);
            Assert.That(_net.TotalCalls, Is.EqualTo(allBefore + 1), "确认作废只允许一发帮会读取");
            Assert.That(_net.Count(MessageIds.GetPlayerGuild), Is.EqualTo(readsBefore + 1));
            Assert.That(_net.EconomyWrites, Is.EqualTo(writesBefore));
            yield return WaitFor(() => !_guild.Busy, label + "二次作废只读刷新", RpcSeconds);
            Assert.That(_net.Errors, Is.Zero, "真实传输失败");
            AssertTip(_net.Response<GetPlayerGuildResponse>(MessageIds.GetPlayerGuild).ErrorMessage);
            Assert.That(_guild.Info?.GuildId, Is.EqualTo(_ownGuild));
            Assert.That(_guild.Donations, Is.Null, "捐献缓存应失效");
            Assert.That(_guild.Shop, Is.Null, "商店缓存应失效");
            Assert.That(_net.TotalCalls, Is.EqualTo(allBefore + 1));
            Assert.That(_net.EconomyWrites, Is.EqualTo(writesBefore));
            StringAssert.Contains("已作废损坏", _guild.Status);
            StringAssert.Contains("原请求未重发", _guild.Status);
            StringAssert.Contains("不能据此判断是否到账", _guild.Status);
            Note(label, "pending_removed=true get_player_guild=1 economy_writes=0");
        }

        private IEnumerator ReadGuild()
        {
            yield return GuildCall(_guild.Refresh, MessageIds.GetPlayerGuild, "重新读取帮会资产");
            AssertTip(_net.Response<GetPlayerGuildResponse>(MessageIds.GetPlayerGuild).ErrorMessage);
            Assert.That(_guild.Info?.GuildId, Is.EqualTo(_ownGuild));
        }

        private IEnumerator ReadDonations(double timeout = RpcSeconds)
        {
            yield return GuildCall(_guild.RefreshDonations, MessageIds.GetGuildDonateOptions, "读取捐献页", timeout);
            AssertTip(_net.Response<GetGuildDonateOptionsResponse>(MessageIds.GetGuildDonateOptions).ErrorMessage);
            Assert.That(_guild.Donations, Is.Not.Null);
        }

        private IEnumerator ReadShop(double timeout = RpcSeconds)
        {
            yield return GuildCall(_guild.RefreshShop, MessageIds.GetGuildShop, "读取商店页", timeout);
            AssertTip(_net.Response<GetGuildShopResponse>(MessageIds.GetGuildShop).ErrorMessage);
            Assert.That(_guild.Shop, Is.Not.Null);
        }

        private IEnumerator GuildCall(Action action, uint method, string stage, double timeout = RpcSeconds)
        {
            Assert.That(_guild.Busy || _guild.RequiresReconnect, Is.False, "前一操作未结束或连接已隔离");
            int before = _net.Count(method);
            _stage = stage;
            action();
            Assert.That(_net.Count(method), Is.EqualTo(before + 1), stage + "未真正发出请求");
            yield return WaitFor(() => !_guild.Busy, stage, timeout);
            Assert.That(_net.Errors, Is.Zero, stage + "真实传输失败");
        }

        private IEnumerator WaitDonationApplied(ulong operation)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + SettlementSeconds;
            while (true)
            {
                var view = _guild.Donations.RecentResults.Concat(_guild.Donations.PendingDonations).SingleOrDefault(x => x.OpId == operation);
                if (view != null && view.Status != GuildAssetOrderStatus.Pending)
                {
                    Assert.That(view.Status, Is.EqualTo(GuildAssetOrderStatus.Applied), "捐献未完整到账");
                    Note("捐献终态", "op=" + operation + " status=APPLIED");
                    yield break;
                }
                yield return PollDelay(deadline, "等待捐献结算");
                yield return ReadDonations(Math.Min(RpcSeconds, deadline - Time.realtimeSinceStartupAsDouble));
            }
        }

        private IEnumerator WaitShopApplied(ulong operation)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + SettlementSeconds;
            while (true)
            {
                var view = _guild.Shop.RecentOrders.Concat(_guild.Shop.PendingOrders).SingleOrDefault(x => x.OpId == operation);
                if (view != null && view.Status != GuildAssetOrderStatus.Pending)
                {
                    Assert.That(view.Status, Is.EqualTo(GuildAssetOrderStatus.Applied), "兑换未完整到账");
                    Note("兑换终态", "op=" + operation + " status=APPLIED");
                    yield break;
                }
                yield return PollDelay(deadline, "等待兑换结算");
                yield return ReadShop(Math.Min(RpcSeconds, deadline - Time.realtimeSinceStartupAsDouble));
            }
        }

        private IEnumerator PollDelay(double deadline, string stage)
        {
            _stage = stage;
            double next = Time.realtimeSinceStartupAsDouble + 0.5;
            while (Time.realtimeSinceStartupAsDouble < next)
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), stage + "超时，保留订单供排查");
                yield return null;
            }
            Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), stage + "超时，保留订单供排查");
        }

        private IEnumerator ReadBag(Action<GetBagResponse> consume)
        {
            GetBagResponse result = null;
            yield return Rpc(SceneBagClientPlayerRpc.GetBagMessageId, new GetBagRequest { BagType = 0 },
                GetBagResponse.Parser, response => result = response, "读取真实背包与银两");
            AssertTip(result.ErrorMessage);
            Assert.That(result.Bag, Is.Not.Null);
            consume(result);
        }

        private IEnumerator Rpc<T>(uint method, IMessage request, MessageParser<T> parser, Action<T> consume, string stage)
            where T : class, IMessage<T>
        {
            bool done = false;
            RpcStatus status = default;
            T response = null;
            _game.CallMethod(method, request, parser, (receivedStatus, received) =>
            { status = receivedStatus; response = received; done = true; });
            yield return WaitFor(() => done, stage, RpcSeconds);
            Note(stage, "method=" + method + " rpc_code=" + status.Code + " tip=" + status.TipId);
            Assert.That(status.IsOk, Is.True, stage + "失败，状态=" + status.Code + "，tip=" + status.TipId);
            Assert.That(response, Is.Not.Null);
            consume(response);
        }

        private IEnumerator WaitFor(Func<bool> done, string stage, double seconds)
        {
            _stage = stage;
            Note(stage, "开始");
            double deadline = Time.realtimeSinceStartupAsDouble + seconds;
            while (!done())
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), stage + "超时；不自动重试写操作");
                yield return null;
            }
            Note(stage, "完成");
        }

        private GuildMember Me() => _guild.Info.Members.Single(member => member.PlayerId == _game.PlayerId);
        private GuildDonateOptionView DonationOption() => _guild.Donations.Options.Single(option => option.DonateId == DonateId);
        private GuildShopGoodsView ShopGoods() => _guild.Shop.Goods.Single(goods => goods.GoodsId == GoodsId);
        private static ulong ItemCount(GetBagResponse response) => response.Bag.Items
            .Where(item => item.ConfigId == RewardItemId).Aggregate(0UL, (sum, item) => checked(sum + item.Count));
        /// <summary>
        /// 补足前的第一次读取:同服务端 econCurrency / EnsureCurrencySlots,尚未写入的货币槽位为零,新角色的 repeated 可为空;
        /// Currency 快照缺失仍然失败,不能冒充零余额。
        /// </summary>
        private ulong SilverBeforeFirstWrite(GetBagResponse response)
        {
            Assert.That(response.Bag.Currency, Is.Not.Null);
            _currencySlots = response.Bag.Currency.Values.Count;
            return _currencySlots == 0 ? 0UL : response.Bag.Currency.Values[0];
        }

        /// <summary>
        /// 写过货币(GM 补足、捐献扣款)之后的读取:服务端写货币时已经用 EnsureCurrencySlots 补齐槽位,之后的快照槽位必须在、
        /// 且不少于上一次。空列表若也读作零,补足路径上「捐献后应为 0」与「兑换后银两不变」两条断言会在槽位丢失时空过。
        /// </summary>
        private ulong Silver(GetBagResponse response)
        {
            Assert.That(response.Bag.Currency, Is.Not.Null);
            int slots = response.Bag.Currency.Values.Count;
            Assert.That(slots, Is.Positive.And.AtLeast(_currencySlots), "货币写入后槽位缺失或变少，不能把空列表读作零余额");
            _currencySlots = slots;
            return response.Bag.Currency.Values[0];
        }

        private void AssertDonationOption()
        {
            var option = DonationOption();
            Assert.That(option.Unlocked, Is.True);
            Assert.That(option.CurrencyType, Is.Zero);
            Assert.That(option.CostAmount, Is.EqualTo(DonationCost));
            Assert.That(option.ContributionGain, Is.EqualTo(ContributionGain));
            Assert.That(option.FundsGain, Is.EqualTo(FundsGain));
            Assert.That(option.DailyLimit, Is.EqualTo(2));
        }

        private void AssertShopGoods()
        {
            var goods = ShopGoods();
            Assert.That(goods.Unlocked, Is.True);
            Assert.That(goods.ItemId, Is.EqualTo(RewardItemId));
            Assert.That(goods.ItemCount, Is.EqualTo(RewardCount));
            Assert.That(goods.CostContribution, Is.EqualTo(ShopCost));
            Assert.That(goods.LimitCount, Is.EqualTo(10));
        }

        private static void AssertTip(TipInfoMessage tip) => Assert.That(tip?.Id ?? 0, Is.Zero, "服务端业务拒绝或故障");
        private static string RequiredEnvironment(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            Assert.That(string.IsNullOrWhiteSpace(value), Is.False, "缺少环境变量 " + name);
            return value;
        }

        private void Note(string stage, string fields) => _journal?.WriteLine(
            Time.realtimeSinceStartupAsDouble.ToString("F3", CultureInfo.InvariantCulture) + " " + stage + " " + fields);

        /// <summary>只观察真实生产传输；不生成响应、不改请求、不替代网络或落库。</summary>
        private sealed class ObservedGuildTransport : IBattleTransport
        {
            private readonly IBattleTransport _inner;
            private readonly Action<string, string> _note;
            private readonly Dictionary<uint, int> _counts = new();
            private readonly Dictionary<uint, IMessage> _requests = new(), _responses = new();
            public ObservedGuildTransport(IBattleTransport inner, Action<string, string> note) { _inner = inner; _note = note; }
            public ulong PlayerId => _inner.PlayerId;
            public bool IsReady => _inner.IsReady;
            public int Errors { get; private set; }
            public int TotalCalls => _counts.Values.Sum();
            public int EconomyWrites => Count(MessageIds.DonateToGuild) + Count(MessageIds.BuyGuildShopGoods);
            public event Action Disconnected { add => _inner.Disconnected += value; remove => _inner.Disconnected -= value; }
            public int Count(uint method) => _counts.TryGetValue(method, out int count) ? count : 0;
            public T Request<T>(uint method) where T : class, IMessage => _requests[method] as T;
            public T Response<T>(uint method) where T : class, IMessage => _responses[method] as T;
            public void RegisterNotify(uint method, Action<MessageContent> handler) => _inner.RegisterNotify(method, handler);
            public void SendOneWay(uint method, IMessage request)
            {
                _counts[method] = Count(method) + 1;
                _inner.SendOneWay(method, request);
            }
            public void Call<T>(uint method, IMessage request, MessageParser<T> parser, Action<T> onResponse, Action<string> onError)
                where T : IMessage<T>
            {
                _counts[method] = Count(method) + 1;
                _requests[method] = request;
                _responses.Remove(method);
                _note("帮会发送", "method=" + method + " count=" + Count(method));
                _inner.Call(method, request, parser, response =>
                {
                    _responses[method] = response;
                    _note("帮会回包", "method=" + method);
                    onResponse(response);
                }, error =>
                {
                    Errors++;
                    _note("帮会传输失败", "method=" + method);
                    onError(error); // 错误原文只交回业务，不写入测试证据。
                });
            }
        }
    }

    /// <summary>仅在精确选择本例的独立进程中，占用现有沙盒启动隔离接缝。</summary>
    internal static class GuildEconomySmokeStartup
    {
        private const string TestName = "MmorpgClient.Tests.PlayMode.GuildEconomyNetworkSmokeTests.CorruptRecordRetirementAllowsRealDonationAndRedemption";
        private const string AssemblyName = "MmorpgClient.Tests.PlayMode.GuildNetwork";
        private static GameObject _ownedRoot;
        private static TianyongSandboxBootstrap _sandboxGuard;
        private static string _failure;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void PrepareOwnedRoot()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (!Application.isBatchMode || Environment.GetEnvironmentVariable("MMORPG_GUILD_SMOKE_ENABLE") != "1"
                || Argument(args, "-testFilter") != TestName || Argument(args, "-assemblyNames") != AssemblyName) return;
            if (_ownedRoot != null || HasBootstrapOrWorld() || FindAll<TianyongSandboxBootstrap>().Length != 0)
            {
                _failure = "启动前已有宿主、沙盒或世界，拒绝接管";
                return;
            }

            _ownedRoot = new GameObject("GuildEconomyNetworkSmoke");
            UnityEngine.Object.DontDestroyOnLoad(_ownedRoot);
            // AppBootstrap.AutoSpawn 已约定遇到沙盒便不自建客户端。该组件无 Awake，
            // 地图与离线角色都在 Start 才创建；立即禁用后只保留启动隔离标记。
            _sandboxGuard = _ownedRoot.AddComponent<TianyongSandboxBootstrap>();
            _sandboxGuard.enabled = false;
            _ownedRoot.AddComponent<GuildEconomySmokePump>();
        }

        public static GameObject ClaimOwnedRoot()
        {
            Assert.That(_failure, Is.Null, _failure);
            Assert.That(_ownedRoot, Is.Not.Null, "缺少受控启动宿主；必须独立 batchmode 且精确选择本程序集与单一用例");
            Assert.That(_ownedRoot.activeInHierarchy, Is.True);
            Assert.That(_sandboxGuard, Is.Not.Null);
            Assert.That(_sandboxGuard.gameObject, Is.SameAs(_ownedRoot), "隔离标记必须属于本轮自建宿主");
            Assert.That(_sandboxGuard.enabled, Is.False, "不允许启动离线沙盒");
            Assert.That(_sandboxGuard.Map, Is.Null);
            Assert.That(_sandboxGuard.Player, Is.Null);
            Assert.That(FindAll<TianyongSandboxBootstrap>(), Is.EqualTo(new[] { _sandboxGuard }));
            Assert.That(_ownedRoot.GetComponents<GuildEconomySmokePump>().Length, Is.EqualTo(1));
            Assert.That(HasBootstrapOrWorld(), Is.False, "检测到正式启动器或未知 ActorWorld，拒绝接管");
            return _ownedRoot;
        }

        public static void ReleaseOwnedRoot()
        {
            // 只销毁 BeforeSceneLoad 保存的自建引用，不按名字查找或清理外部对象。
            if (_ownedRoot != null) UnityEngine.Object.Destroy(_ownedRoot);
            _ownedRoot = null;
            _sandboxGuard = null;
            _failure = null;
        }

        private static bool HasBootstrapOrWorld() => FindAll<AppBootstrap>().Length != 0
            || FindAll<Bootstrap>().Length != 0 || FindAll<UnityEngine.Transform>().Any(item => item.name == "[ActorWorld]");
        private static T[] FindAll<T>() where T : UnityEngine.Object => UnityEngine.Object.FindObjectsByType<T>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        private static string Argument(string[] args, string option)
        {
            for (int index = 0; index + 1 < args.Length; index++)
                if (args[index].Equals(option, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
            return null;
        }
    }

    /// <summary>仅提供正式 GameClient 所需的 Unity 主线程 Tick 与协程生命周期。</summary>
    public sealed class GuildEconomySmokePump : MonoBehaviour
    {
        public Action Tick;
        private void Update() => Tick?.Invoke();
    }
}
