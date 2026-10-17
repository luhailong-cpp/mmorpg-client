using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Rank;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui.Rank;
using NUnit.Framework;
using Rankpb;

namespace MmorpgClient.Tests.EditMode.Rank
{
    /// <summary>
    /// RankClient 经假传输层的行为(服务端 docs/design/leaderboard-system.md §4.2 / §4.6)。
    /// 断言只用生成的消息号与 tip 枚举,不写数字。
    /// </summary>
    public sealed class RankClientTests
    {
        private RankFakeTransport _net;
        private object _connection;
        private RankClient _client;
        private float _now;

        [SetUp]
        public void SetUp()
        {
            _net = new RankFakeTransport();
            _connection = new object();
            _now = 100f;
            _client = new RankClient(_net, () => _connection, () => _now);
        }

        /// <summary>假时钟前进到可以再发一次(越过最小发送间隔)。</summary>
        private void Later() => _now += RankClient.MinRequestIntervalSeconds + 0.01f;

        private static string EnvelopeTip(common_error tip) => RankClient.ServerTipPrefix + (uint)tip;

        [TearDown] public void TearDown() => _client.Dispose();

        private static GetRankPageResponse Page(uint page, uint pageCount, uint total, params (uint rank, ulong player, string name, uint rating, ulong games)[] rows)
        {
            var response = new GetRankPageResponse { Page = page, PageCount = pageCount, PageSize = RankClient.PageSize, TotalCount = total, Self = new RankEntry() };
            foreach (var row in rows)
                response.Entries.Add(new RankEntry { Rank = row.rank, PlayerId = row.player, Name = row.name, RatingCenti = row.rating, Games = row.games });
            return response;
        }

        [Test]
        public void LoadSendsOneRequestForTheMatchRatingBoard_WithoutAnyPlayerId()
        {
            _client.Load(3);
            Assert.That(_net.Calls, Is.EqualTo(new[] { MessageIds.GetRankPage }));
            var request = (GetRankPageRequest)_net.LastRequest;
            Assert.That(request.Board, Is.EqualTo(RankBoard.MatchRating));
            Assert.That(request.Page, Is.EqualTo(3));
            Assert.That(request.PageSize, Is.EqualTo(RankClient.PageSize));
            // 身份只取 gate 会话(D-9):请求协议里根本没有 player_id 字段。
            Assert.That(GetRankPageRequest.Descriptor.Fields.InDeclarationOrder().Any(f => f.Name.Contains("player")), Is.False);
            Assert.That(_client.Busy, Is.True);
            Assert.That(_client.Status, Is.EqualTo(RankClient.LoadingMessage));
        }

        [Test]
        public void PageZeroIsTreatedAsTheFirstPage()
        {
            _client.Load(0);
            Assert.That(((GetRankPageRequest)_net.LastRequest).Page, Is.EqualTo(1));
        }

        [Test]
        public void SuccessfulReplyIsKeptAndPagingFollowsTheServer()
        {
            _client.Load(1);
            _net.Reply(Page(2, 4, 18, (6, 7, "甲", 151629, 3)));
            Assert.That(_client.Busy, Is.False);
            Assert.That(_client.Page, Is.Not.Null);
            Assert.That(_client.CurrentPage, Is.EqualTo(2), "越界页码由服务端钳制,以回包为准");
            Assert.That(_client.PageCount, Is.EqualTo(4));
            Assert.That(_client.Status, Is.Empty);
        }

        [Test]
        public void EmptyBoardIsNotAnError()
        {
            _client.Load(1);
            _net.Reply(Page(1, 1, 0));
            Assert.That(_client.Page, Is.Not.Null);
            Assert.That(_client.Page.Self.Rank, Is.Zero, "未上榜不是错误(§4.2)");
            Assert.That(_client.Status, Is.EqualTo(RankClient.EmptyMessage));
        }

        [Test]
        public void BusinessTipClearsThePageAndShowsTheReason()
        {
            _client.Load(1);
            _net.Reply(Page(1, 1, 1, (1, 7, "甲", 150000, 1)));
            Later();
            _client.Load(1);
            _net.Reply(new GetRankPageResponse { ErrorMessage = new TipInfoMessage { Id = (uint)common_error.KServiceUnavailable } });
            Assert.That(_client.Page, Is.Null, "读失败时不能把旧页挂在失败提示下面");
            Assert.That(_client.Status, Is.EqualTo(RankClient.DescribeTip((uint)common_error.KServiceUnavailable)));
            Assert.That(_client.RequiresReconnect, Is.False, "业务拒绝不串包,不需要隔离");
        }

        [Test]
        public void LoadWhileBusyIsSentOnceAfterTheReply_LastPageWins()
        {
            _client.Load(1);
            _client.Load(2);
            _client.Load(3);
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "同一功能只能单请求在途");
            Later();
            _net.Reply(Page(1, 5, 25));
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(((GetRankPageRequest)_net.LastRequest).Page, Is.EqualTo(3), "以最后一次点击为准");
        }

        [Test]
        public void TimeoutIsolatesUntilAReconnect()
        {
            _client.Load(1);
            _net.Fail(RankClient.TimeoutError);
            Assert.That(_client.RequiresReconnect, Is.True);
            Assert.That(_client.Status, Is.EqualTo(RankClient.RecoveryMessage));
            _client.Load(1);
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "隔离期间不再发同号请求(迟到回包会被误领)");
            _net.Disconnect();
            _net.IsReady = true;
            Assert.That(_client.RequiresReconnect, Is.False, "真实断线后解除隔离");
            // 新连接不继承旧连接的发送间隔:假时钟没动也能立即发。
            _client.Load(1);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
        }

        [Test]
        public void OtherTransportErrorsDoNotIsolate_AndDropTheOldPage()
        {
            _client.Load(1);
            _net.Reply(Page(1, 1, 1, (1, 7, "甲", 150000, 1)));
            Later();
            _client.Load(1);
            _net.Fail("gate not connected");
            Assert.That(_client.RequiresReconnect, Is.False);
            Assert.That(_client.Status, Is.EqualTo(RankClient.TransportFailureMessage));
            Assert.That(_client.Busy, Is.False);
            Assert.That(_client.Page, Is.Null, "与业务拒绝同一口径:读失败时不保留旧页");
        }

        [Test]
        public void EnvelopeTipIsDescribed()
        {
            _client.Load(1);
            _net.Fail(EnvelopeTip(common_error.KServiceUnavailable));
            Assert.That(_client.Status, Is.EqualTo(RankClient.DescribeTip((uint)common_error.KServiceUnavailable)));
            Assert.That(_client.RequiresReconnect, Is.False);
        }

        [Test]
        public void GateRateLimitBacksOff_WithoutAutomaticRetry()
        {
            _client.Load(1);
            _net.Reply(Page(1, 4, 18, (1, 7, "甲", 150000, 1)));
            Later();
            _client.Load(2);
            _client.Load(3); // 在途时又点了一页
            _net.Fail(EnvelopeTip(common_error.KRateLimitExceeded));
            Assert.That(_net.Calls.Count, Is.EqualTo(2), "被限频后不得补发排队的页(§4.5)");
            Assert.That(_client.Status, Is.EqualTo(RankClient.DescribeTip((uint)common_error.KRateLimitExceeded)));
            Assert.That(_client.Page, Is.Not.Null, "限频的请求没到服务端,手上这页仍是最近一次读到的");
            Assert.That(_client.Waiting, Is.False);

            Later();
            _client.Load(2);
            _client.Tick();
            Assert.That(_net.Calls.Count, Is.EqualTo(2), "退避期间的翻页只提示、不排队");
            Assert.That(_client.Status, Is.EqualTo(RankClient.DescribeTip((uint)common_error.KRateLimitExceeded)));

            _now += RankClient.RateLimitBackoffSeconds;
            _client.Tick();
            Assert.That(_net.Calls.Count, Is.EqualTo(2), "退避结束也不会自己补发");
            _client.Load(2);
            Assert.That(_net.Calls.Count, Is.EqualTo(3));
            Assert.That(((GetRankPageRequest)_net.LastRequest).Page, Is.EqualTo(2));
        }

        [Test]
        public void SendsStayWithinTheGateQuotaWindow()
        {
            // 连点翻页(每次都等回包、隔开最小间隔):前 SendQuota 次照发,下一次落在「最早一次 + 窗口」之前,只排队。
            float start = _now;
            for (int i = 0; i < RankClient.SendQuota; ++i)
            {
                _client.Load((uint)i + 1);
                Assert.That(_net.Calls.Count, Is.EqualTo(i + 1));
                _net.Reply(Page((uint)i + 1, 9, 45));
                Later();
            }
            _client.Load(RankClient.SendQuota + 1);
            Assert.That(_net.Calls.Count, Is.EqualTo(RankClient.SendQuota), "gate 按两个整秒计数:超出配额的这次排队,不发");
            _now = start + RankClient.MessageWindowSeconds - 0.01f;
            _client.Tick();
            Assert.That(_net.Calls.Count, Is.EqualTo(RankClient.SendQuota), "窗口没滚过去之前不补发");
            _now = start + RankClient.MessageWindowSeconds;
            _client.Tick();
            Assert.That(_net.Calls.Count, Is.EqualTo(RankClient.SendQuota + 1));
            Assert.That(((GetRankPageRequest)_net.LastRequest).Page, Is.EqualTo(RankClient.SendQuota + 1));
        }

        [Test]
        public void LoadsCloserThanTheMinimumIntervalAreQueuedAndSentByTick()
        {
            _client.Load(1);
            _net.Reply(Page(1, 4, 18));
            _client.Load(2);
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "离上次发送太近:排队");
            Assert.That(_client.Waiting, Is.True, "排队期间界面禁用翻页");
            _client.Load(3);
            _client.Tick();
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "没到点不补发");
            Later();
            _client.Tick();
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(((GetRankPageRequest)_net.LastRequest).Page, Is.EqualTo(3), "以最后一次点击为准");
            _client.Tick();
            Assert.That(_net.Calls.Count, Is.EqualTo(2), "补发一次后不再重复");
        }

        [Test]
        public void LateReplyAfterDisconnectIsIgnored()
        {
            _client.Load(1);
            var stale = _net.Pending;
            _net.Disconnect();
            _net.IsReady = true;
            stale(Page(1, 1, 1, (1, 7, "甲", 150000, 1)));
            Assert.That(_client.Page, Is.Null, "旧连接的回包不能落到新会话上");
        }

        [Test]
        public void GateSwapWithoutDisconnectResetsTheSession()
        {
            _client.Load(1);
            _net.Reply(Page(1, 1, 1, (1, 7, "甲", 150000, 1)));
            _connection = new object();
            _client.ObserveConnection();
            Assert.That(_client.Page, Is.Null);
            Assert.That(_client.RequiresReconnect, Is.False);
        }

        [Test]
        public void GateSwapWhileBusyClearsBusyWithoutIsolation_AndDropsTheOldReply()
        {
            _client.Load(1);
            var stale = _net.Pending;
            _connection = new object();
            _client.ObserveConnection();
            Assert.That(_client.Busy, Is.False, "换 Gate 是解除卡死的唯一路径");
            Assert.That(_client.RequiresReconnect, Is.False, "新连接上不会有旧请求的迟到回包");
            stale(Page(1, 1, 1, (1, 7, "甲", 150000, 1)));
            Assert.That(_client.Page, Is.Null, "旧连接的回包被连接校验丢掉");
            _client.Load(1);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
        }

        [Test]
        public void TimeoutDropsThePendingPage()
        {
            _client.Load(1);
            _client.Load(2);
            _net.Fail(RankClient.TimeoutError);
            Later();
            _client.Tick();
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "隔离时排队的页一并作废");
            Assert.That(_client.Waiting, Is.False);
        }

        [Test]
        public void LateReplyAfterDisposeIsIgnored()
        {
            _client.Load(1);
            var stale = _net.Pending;
            _client.Dispose();
            stale(Page(1, 1, 1, (1, 7, "甲", 150000, 1)));
            Assert.That(_client.Page, Is.Null);
            Assert.That(_client.Busy, Is.False);
        }

        [Test]
        public void ChangedFiresOnLoadReplyAndReset()
        {
            int changes = 0;
            _client.Changed += () => ++changes;
            _client.Load(1);
            Assert.That(changes, Is.EqualTo(1), "发出请求时刷新(显示读取中)");
            _net.Reply(Page(1, 1, 1, (1, 7, "甲", 150000, 1)));
            Assert.That(changes, Is.EqualTo(2), "回包后刷新");
            _client.Reset();
            Assert.That(changes, Is.EqualTo(3), "作废快照后刷新");
        }

        [Test]
        public void ResetWhileBusyIsolates()
        {
            _client.Load(1);
            _client.Reset();
            Assert.That(_client.RequiresReconnect, Is.True, "在途请求被作废后,迟到回包可能被下一个同号请求误领");
        }

        [Test]
        public void NotInGameDoesNotSend()
        {
            _net.IsReady = false;
            _client.Load(1);
            Assert.That(_net.Calls, Is.Empty);
            Assert.That(_client.Status, Is.EqualTo(RankClient.NotInGameMessage));
        }

        [Test]
        public void RatingIsFormattedFromIntegerCentiWithoutFloatingPoint()
        {
            Assert.That(RankClient.FormatRating(151629), Is.EqualTo("1516.29"));
            Assert.That(RankClient.FormatRating(150000), Is.EqualTo("1500.00"));
            Assert.That(RankClient.FormatRating(29), Is.EqualTo("0.29"));
            Assert.That(RankClient.FormatRating(5), Is.EqualTo("0.05"));
            Assert.That(RankClient.FormatRating(0), Is.EqualTo("0.00"));
        }

        [Test]
        public void EnvelopeTipParsingOnlyAcceptsTheServerTipShape()
        {
            Assert.That(RankClient.ParseEnvelopeTip(EnvelopeTip(common_error.KRateLimitExceeded)), Is.EqualTo((uint)common_error.KRateLimitExceeded));
            Assert.That(RankClient.ParseEnvelopeTip(RankClient.TimeoutError), Is.Zero);
            Assert.That(RankClient.ParseEnvelopeTip(RankClient.ServerTipPrefix), Is.Zero);
            Assert.That(RankClient.ParseEnvelopeTip(RankClient.ServerTipPrefix + "-1"), Is.Zero);
            Assert.That(RankClient.ParseEnvelopeTip(null), Is.Zero);
        }

        [Test]
        public void MissingNameFallsBackToPlayerNumber()
        {
            Assert.That(RankWindow.DisplayName(new RankEntry { PlayerId = 42, Name = "" }), Is.EqualTo("道友 · 42"), "名字补全 fail-open 时留空(§4.3)");
            Assert.That(RankWindow.DisplayName(new RankEntry { PlayerId = 42, Name = "乙" }), Is.EqualTo("乙"));
        }
    }

    internal sealed class RankFakeTransport : IBattleTransport
    {
        public ulong PlayerId { get; set; } = 1;
        public bool IsReady { get; set; } = true;
        public event Action Disconnected;
        public List<uint> Calls { get; } = new();
        public IMessage LastRequest;
        public Action<IMessage> Pending;
        public Action<string> Error;
        public void Reply(IMessage response) => Pending(response);
        public void Fail(string error) => Error(error);
        public void Disconnect() { IsReady = false; Disconnected?.Invoke(); }
        public void RegisterNotify(uint id, Action<MessageContent> action) { }
        public void SendOneWay(uint id, IMessage request) { }
        public void Call<T>(uint id, IMessage request, MessageParser<T> parser, Action<T> success, Action<string> error)
            where T : IMessage<T>
        {
            Calls.Add(id);
            LastRequest = request;
            Pending = message => success((T)message);
            Error = error;
        }
    }
}
