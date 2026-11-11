using System;
using System.IO;
using System.Linq;
using Google.Protobuf;
using Guildpb;
using MmorpgClient.Game.Guild;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>
    /// 帮会请求失败的两类收口(服务端仓 docs/design/client-request-ordering.md §4.4、§4.7):
    /// 确定未执行(本地没发出、gate / 路由服转发前的信封)只解除 Busy、可以重试;
    /// 结果未知(超时、断线、1003、解析失败)锁到换连接,写请求不在结果未知时重发,捐献 / 兑换恢复时沿用原 client_request_id。
    /// 错误串与 GameClient.Call 交给 onError 的原文同形(RpcStatus.ToLegacyError)。
    /// </summary>
    public sealed class GuildRequestFailureTests
    {
        private GuildFakeTransport _net;
        private GuildClient _client;
        private string _recoveryDirectory;

        [SetUp] public void SetUp()
        {
            _recoveryDirectory = Path.Combine(Path.GetTempPath(), "guild-request-failure-" + Guid.NewGuid().ToString("N"));
            _net = new GuildFakeTransport();
            _client = new GuildClient(_net, economyRecovery: new DurableRequestFileStore(_recoveryDirectory));
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = GuildClientTests.Fixture() });
        }
        [TearDown] public void TearDown()
        {
            _client.Dispose();
            if (Directory.Exists(_recoveryDirectory)) Directory.Delete(_recoveryDirectory, true);
        }

        private void Send(bool donation) { if (donation) _client.Donate(2); else _client.Buy(101, 2); }
        private static uint WriteId(bool donation) => donation ? MessageIds.DonateToGuild : MessageIds.BuyGuildShopGoods;
        private static ulong Key(IMessage request) => request switch
        {
            DonateToGuildRequest donation => donation.ClientRequestId,
            BuyGuildShopGoodsRequest order => order.ClientRequestId,
            _ => throw new AssertionException("最后一发不是捐献或兑换"),
        };

        [TestCase("server tip=1008")]              // lane 满 / 限流
        [TestCase("server tip=1010")]              // 包体超限
        [TestCase("server tip=1006")]              // GM 闸
        [TestCase("server tip=1005")]              // 路由服转发前拒绝
        [TestCase("server tip=2028")]              // 发出前未登录 / 入队身份失配
        [TestCase("not connected")]                // NotSent:未连接
        [TestCase("gate redirected")]              // NotSent:msg 124 之后的旧 gate
        [TestCase("send failed: not connected")]   // NotSent:发送前抛异常
        public void DefinitelyNotExecutedFailureOnlyReleasesBusy(string error)
        {
            _client.Refresh(); _net.Error(error);
            Assert.That(_client.Busy, Is.False);
            Assert.That(_client.RequiresReconnect, Is.False);
            Assert.That(_client.Status, Is.EqualTo(GuildClient.NotExecutedMessage));
            Assert.That(_client.Info.GuildId, Is.EqualTo(555ul));
            int before = _net.Calls.Count;
            _client.Refresh();
            Assert.That(_net.Calls.Count, Is.EqualTo(before + 1));
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
        }

        [TestCase("rpc timeout")]
        [TestCase("disconnected")]
        [TestCase("server tip=1003")]              // 排队超时与下游失败同码,客户端分不出,保守按结果未知
        [TestCase("parse response: truncated")]
        [TestCase("server tip=")]
        [TestCase("server tip=1008x")]
        [TestCase("离线验收不执行服务端操作")]     // 不认识的串
        public void UnknownOutcomeFailureLocksUntilConnectionChanges(string error)
        {
            _client.Refresh(); _net.Error(error);
            Assert.That(_client.Busy, Is.False);
            Assert.That(_client.RequiresReconnect, Is.True);
            Assert.That(_client.Status, Is.EqualTo(GuildClient.RecoveryMessage));
            int before = _net.Calls.Count;
            _client.Refresh();
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            _net.Disconnect(); _net.IsReady = true; _client.Refresh();
            Assert.That(_client.RequiresReconnect, Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(before + 1));
        }

        [Test] public void DefinitelyRejectedWriteCanBeRetriedButUnknownWriteIsNotResent()
        {
            _client.Kick(2); _net.Error("server tip=1008");
            Assert.That(_client.RequiresReconnect, Is.False);
            _client.Kick(2);
            Assert.That(_net.Calls.Count(id => id == MessageIds.KickGuildMember), Is.EqualTo(2));

            // 第二发结果未知:同一个写与别的写都不再发,直到换连接。
            _net.Error("server tip=1003");
            Assert.That(_client.RequiresReconnect, Is.True);
            int before = _net.Calls.Count;
            _client.Kick(2); _client.SetMemberRole(3, GuildRoles.Officer); _client.SaveAnnouncement("结果未知时不发写");
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Status, Is.EqualTo(GuildClient.RecoveryMessage));
        }

        [TestCase(true)] [TestCase(false)]
        public void DefinitelyRejectedEconomyWriteKeepsRecordAndRetriesWithTheSameKey(bool donation)
        {
            Send(donation);
            ulong key = Key(_net.Requests.Last());
            _net.Error("server tip=1008");
            Assert.That(_client.RequiresReconnect, Is.False);
            Assert.That(_client.HasPendingEconomyWrite, Is.True);
            // 本机记录还在:再点捐献 / 兑换不另起新键,只能恢复原请求。
            int before = _net.Calls.Count;
            Send(donation);
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            _client.RecoverEconomyRequest();
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
            _net.Reply(new GetPlayerGuildResponse { Guild = GuildClientTests.Fixture() });
            Assert.That(_net.Calls.Last(), Is.EqualTo(WriteId(donation)));
            Assert.That(Key(_net.Requests.Last()), Is.EqualTo(key));
        }

        [TestCase(true)] [TestCase(false)]
        public void UnknownEconomyWriteWaitsForReconnectThenReplaysTheSameKey(bool donation)
        {
            Send(donation);
            ulong key = Key(_net.Requests.Last());
            _net.Error("server tip=1003");
            Assert.That(_client.RequiresReconnect, Is.True);
            int before = _net.Calls.Count;
            _client.RecoverEconomyRequest(); Send(donation);
            Assert.That(_net.Calls.Count, Is.EqualTo(before));

            _net.Disconnect(); _net.IsReady = true;
            _client.RecoverEconomyRequest();
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
            _net.Reply(new GetPlayerGuildResponse { Guild = GuildClientTests.Fixture() });
            Assert.That(_net.Calls.Last(), Is.EqualTo(WriteId(donation)));
            Assert.That(Key(_net.Requests.Last()), Is.EqualTo(key));
            Assert.That(_net.Calls.Count(id => id == WriteId(donation)), Is.EqualTo(2));
        }
    }
}
