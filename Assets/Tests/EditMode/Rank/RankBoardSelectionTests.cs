using System.Linq;
using MmorpgClient.Game.Rank;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Rank;
using NUnit.Framework;
using Rankpb;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Rank
{
    /// <summary>
    /// 论剑榜按模式分榜(服务端 docs/design/global-match-hierarchical-pools.md §8 R5,2026-11-12):选中的榜、按榜分开的快照、
    /// 切榜时旧榜迟到回包的去向,以及窗口的模式页签。除了钉住线上取值的第一条,榜类型都取 rank.proto 生成的 RankBoardType。
    /// 假传输层与 RankClientTests 共用(RankFakeTransport)。
    /// </summary>
    public sealed class RankBoardSelectionTests
    {
        private const uint Duel = (uint)RankBoardType.Match1V1;
        private const uint Solo5V5 = (uint)RankBoardType.Match5V5;
        private const uint PvpTeam = (uint)RankBoardType.MatchPvpTeam;

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

        [TearDown] public void TearDown() => _client.Dispose();

        /// <summary>假时钟前进到可以再发一次(越过最小发送间隔)。</summary>
        private void Later() => _now += RankClient.MinRequestIntervalSeconds + 0.01f;

        private GetRankPageRequest LastPageRequest => (GetRankPageRequest)_net.LastRequest;

        /// <summary>服务端回显的榜键:评分榜固定 GLOBAL、period "all"、第 1 代。</summary>
        private static RankBoardKey Resolved(uint boardType)
            => new() { BoardType = boardType, Scope = RankScope.Global, Period = "all", Generation = 1 };

        private static GetRankPageResponse PageOf(uint boardType, params (uint rank, ulong player, long rating)[] rows)
        {
            var response = new GetRankPageResponse
            {
                Board = Resolved(boardType), Page = 1, PageCount = 1, PageSize = RankClient.PageSize,
                TotalCount = (ulong)rows.Length, Self = new RankEntry(),
            };
            foreach (var row in rows)
                response.Entries.Add(new RankEntry { Rank = row.rank, EntityId = row.player, Score = row.rating });
            return response;
        }

        private static string EnvelopeTip(common_error tip) => RankClient.ServerTipPrefix + (uint)tip;

        /// <summary>榜类型是客户端契约,上线后不改不复用(同字段编号):这一条钉住线上取值,其余用例只用生成常量。</summary>
        [Test]
        public void BoardTypesAreTheRegisteredWireValues_AndTheDefaultTabComesFirst()
        {
            Assert.That(Duel, Is.EqualTo(1u), "1V1 沿用拆分前唯一那张评分榜的键(robot rank-smoke 读它)");
            Assert.That(Solo5V5, Is.EqualTo(2u));
            Assert.That(PvpTeam, Is.EqualTo(3u));
            Assert.That(RankClient.DefaultBoardType, Is.EqualTo(PvpTeam), "客户端「匹配 PVP」只排 PVP_TEAM,默认看这张榜");
            Assert.That(RankWindow.BoardTabs.Select(tab => tab.BoardType), Is.EqualTo(new[] { PvpTeam, Duel, Solo5V5 }),
                "三张评分榜各一个页签,默认榜在最前");
        }

        [Test]
        public void RefreshReadsTheSelectedBoardsLatestGeneration()
        {
            Assert.That(_client.SelectedBoardType, Is.EqualTo(RankClient.DefaultBoardType));
            _client.Refresh();
            var request = LastPageRequest;
            Assert.That(request.Board, Is.EqualTo(RankClient.LatestBoard(RankClient.DefaultBoardType)));
            Assert.That(request.Board.Period, Is.Empty, "空 period、代次 0 = 最新可见代次,由服务端解析");
            Assert.That(request.Board.Generation, Is.Zero);
            Assert.That(request.Page, Is.EqualTo(1));
        }

        [Test]
        public void SelectingAnotherBoardReadsItsFirstPage_AndReselectingSendsNothing()
        {
            _client.Refresh();
            _net.Reply(PageOf(PvpTeam, (1, 7, 151629)));
            Later();
            _client.SelectBoard(Duel);
            Assert.That(_client.SelectedBoardType, Is.EqualTo(Duel));
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(LastPageRequest.Board, Is.EqualTo(RankClient.LatestBoard(Duel)), "切榜读新榜的最新一代,不沿用旧榜解析出的代次");
            Assert.That(LastPageRequest.Page, Is.EqualTo(1));
            Assert.That(_client.Page, Is.Null, "新榜还没有快照,不拿旧榜的页充数");

            _net.Reply(PageOf(Duel, (1, 8, 160000)));
            Later();
            _client.SelectBoard(Duel);
            Assert.That(_net.Calls.Count, Is.EqualTo(2), "点已选中的榜不重发,刷新走「最新榜」");
            Assert.That(_client.Page.Board.BoardType, Is.EqualTo(Duel));
            Assert.That(_client.Page.Entries.Single().EntityId, Is.EqualTo(8));
        }

        /// <summary>
        /// 在途请求不作废:切榜排在它后面,界面马上换到新榜。旧榜的迟到回包只进旧榜自己的快照,不出现在新榜上,也不改状态行;
        /// 切回旧榜时,读取期间显示的是旧榜自己的那一页。
        /// </summary>
        [Test]
        public void LateReplyOfThePreviousBoardNeverShowsOnTheNewBoard()
        {
            _client.Refresh();
            int changes = 0;
            _client.Changed += () => ++changes;
            _client.SelectBoard(Solo5V5);
            Assert.That(changes, Is.EqualTo(1), "排队期间界面也要换到新榜");
            Assert.That(_client.SelectedBoardType, Is.EqualTo(Solo5V5));
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "单请求在途:新榜的请求排队");
            Assert.That(_client.Waiting, Is.True);

            _net.Reply(PageOf(PvpTeam, (1, 7, 151629)));
            Assert.That(_client.Page, Is.Null, "旧榜的回包不落到新榜的画面上");
            Assert.That(_client.Status, Is.EqualTo(RankClient.LoadingMessage), "状态行属于新榜:仍在读取");

            Later();
            _client.Tick();
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(LastPageRequest.Board, Is.EqualTo(RankClient.LatestBoard(Solo5V5)));
            _net.Reply(PageOf(Solo5V5));
            Assert.That(_client.Page.Board.BoardType, Is.EqualTo(Solo5V5));
            Assert.That(_client.Status, Is.EqualTo(RankClient.EmptyMessage));

            Later();
            _client.SelectBoard(PvpTeam);
            Assert.That(_client.Status, Is.EqualTo(RankClient.LoadingMessage));
            Assert.That(_client.Page.Board.BoardType, Is.EqualTo(PvpTeam), "切回旧榜:读取期间显示旧榜自己的快照");
            Assert.That(_client.Page.Entries.Single().Score, Is.EqualTo(151629));
        }

        [Test]
        public void PagingAndNearbyFollowTheSelectedBoardsResolvedKey()
        {
            _client.SelectBoard(Duel);
            var first = PageOf(Duel, (1, 8, 160000));
            first.PageCount = 3;
            _net.Reply(first);
            Later();
            _client.Load(2);
            Assert.That(LastPageRequest.Board, Is.EqualTo(Resolved(Duel)), "翻页沿用选中榜回包解析出的代次");
            Assert.That(LastPageRequest.Page, Is.EqualTo(2));
            _net.Reply(PageOf(Duel));
            Later();
            _client.LoadAround(RankWindow.AroundRadius);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.RankGetRankAround));
            var around = (GetRankAroundRequest)_net.LastRequest;
            Assert.That(around.Board, Is.EqualTo(Resolved(Duel)), "我的附近不跳回默认榜,也不换成最新代次");
            Assert.That(around.Radius, Is.EqualTo(RankWindow.AroundRadius));
        }

        [Test]
        public void ReplyEchoingAnotherBoardTypeIsRejected()
        {
            _client.SelectBoard(Duel);
            _net.Reply(PageOf(PvpTeam, (1, 7, 151629)));
            Assert.That(_client.Page, Is.Null, "回显的榜类型与请求不符:别的榜的数据不存进这张榜");
            Assert.That(_client.Status, Is.EqualTo(RankClient.TransportFailureMessage));
            Assert.That(_client.RequiresReconnect, Is.False, "回包已到,不是结果未知");
            Later();
            _client.SelectBoard(PvpTeam);
            Assert.That(_client.Page, Is.Null, "也没有存进它回显的那张榜");
        }

        [Test]
        public void RejectionOfThePreviousBoardDoesNotTakeOverTheNewBoardsStatus()
        {
            var notFound = new GetRankPageResponse { ErrorMessage = new TipInfoMessage { Id = (uint)rank_error.KRankBoardNotFound } };
            _client.Refresh();
            _client.SelectBoard(Solo5V5);
            _net.Reply(notFound);
            Assert.That(_client.Status, Is.EqualTo(RankClient.LoadingMessage), "旧榜的拒绝不显示在新榜上");
            Later();
            _client.Tick();
            Assert.That(LastPageRequest.Board, Is.EqualTo(RankClient.LatestBoard(Solo5V5)), "旧榜读失败照常补发排队的新榜");
            _net.Reply(notFound.Clone());
            Assert.That(_client.Status, Is.EqualTo(RankClient.DescribeTip((uint)rank_error.KRankBoardNotFound)), "新榜自己的拒绝照常显示");
        }

        [Test]
        public void RateLimitOnThePreviousBoardDropsTheQueuedSwitch_WithoutAutomaticRetry()
        {
            _client.Refresh();
            _client.SelectBoard(Duel);
            _net.Fail(EnvelopeTip(common_error.KRateLimitExceeded));
            Assert.That(_client.SelectedBoardType, Is.EqualTo(Duel), "选中的榜照旧,只是这次切换没发出去");
            Assert.That(_client.Waiting, Is.False, "限频后排队的切换作废");
            Assert.That(_client.Status, Is.EqualTo(RankClient.DescribeTip((uint)common_error.KRateLimitExceeded)));
            _now += RankClient.RateLimitBackoffSeconds + 1f;
            _client.Tick();
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "退避结束也不自己补发");
            _client.Refresh();
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(LastPageRequest.Board, Is.EqualTo(RankClient.LatestBoard(Duel)), "退避后刷新读的是选中的榜");
        }

        [Test]
        public void TimeoutDropsEveryBoardsSnapshot()
        {
            _client.Refresh();
            _net.Reply(PageOf(PvpTeam, (1, 7, 151629)));
            Later();
            _client.SelectBoard(Duel);
            _net.Fail(RankClient.TimeoutError);
            Assert.That(_client.RequiresReconnect, Is.True);
            _client.SelectBoard(PvpTeam);
            Assert.That(_net.Calls.Count, Is.EqualTo(2), "隔离到重连:切榜也不发");
            Assert.That(_client.Status, Is.EqualTo(RankClient.RecoveryMessage));
            Assert.That(_client.Page, Is.Null, "结果未知时每张榜的快照一起作废,不只是在途的那张");
        }

        [Test]
        public void PlayerSwitchReturnsToTheDefaultBoardAndForgetsEverySnapshot()
        {
            _client.SelectBoard(Duel);
            _net.Reply(PageOf(Duel, (1, 8, 160000)));
            _net.PlayerId = 42;
            _client.ObserveConnection();
            Assert.That(_client.SelectedBoardType, Is.EqualTo(RankClient.DefaultBoardType), "选中的榜不跨角色残留");
            _client.SelectBoard(Duel);
            Assert.That(_client.Page, Is.Null, "上一个角色读到的页也不残留");
            Assert.That(LastPageRequest.Board, Is.EqualTo(RankClient.LatestBoard(Duel)));
        }

        [Test]
        public void WindowTabsFollowTheSelectedBoard_AndClickingOnlyRaisesTheIntent()
        {
            var view = new GameObject("RankBoardTabsTest", typeof(RectTransform), typeof(Canvas));
            try
            {
                var window = new RankWindow(view.transform);
                uint? clicked = null;
                window.BoardSelected += boardType => clicked = boardType;
                window.Show(_client);
                AssertSelectedTab(window, RankClient.DefaultBoardType);

                Tab(window, Duel).onClick.Invoke();
                Assert.That(clicked, Is.EqualTo(Duel));
                Assert.That(_client.SelectedBoardType, Is.EqualTo(RankClient.DefaultBoardType), "切不切由 RankClient 决定,窗口不自己改");

                _client.SelectBoard(Duel);
                window.Render();
                AssertSelectedTab(window, Duel);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(view);
            }
        }

        [Test]
        public void WindowTabsStayClickableWhileLoading_ButNotWhileWaitingForAReconnect()
        {
            var view = new GameObject("RankBoardTabsReconnectTest", typeof(RectTransform), typeof(Canvas));
            try
            {
                var window = new RankWindow(view.transform);
                window.Show(_client);
                _client.Refresh();
                window.Render();
                Assert.That(RankWindow.BoardTabs.All(tab => Tab(window, tab.BoardType).interactable), Is.True,
                    "请求在途时也能切榜,只保留最后一次切换");

                _net.Fail(RankClient.TimeoutError);
                window.Render();
                Assert.That(RankWindow.BoardTabs.Any(tab => Tab(window, tab.BoardType).interactable), Is.False,
                    "结果未知、等重连时切了也读不了");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(view);
            }
        }

        private static Button Tab(RankWindow window, uint boardType)
            => window.Root.GetComponentsInChildren<Button>(true).Single(button => button.name == RankWindow.TabName(boardType));

        private static void AssertSelectedTab(RankWindow window, uint selected)
        {
            foreach (var (boardType, label) in RankWindow.BoardTabs)
            {
                var tab = Tab(window, boardType);
                bool active = boardType == selected;
                var text = tab.GetComponentInChildren<TMP_Text>(true);
                Assert.That(tab.image.sprite, Is.EqualTo(GameplayUiArt.Load(active ? "tab_selected" : "tab_normal")), label);
                Assert.That(text.color, Is.EqualTo(active ? GameplayUiArt.Cream : GameplayUiArt.Ink), label);
                Assert.That(text.text, Is.EqualTo(label));
            }
        }
    }
}
