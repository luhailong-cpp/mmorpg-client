using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.Jubaozhai;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Jubaozhai
{
    /// <summary>
    /// JubaozhaiState 服务端分页模式的契约(聚宝斋 P1 实现规格 §10.1)。
    /// 本地模式的既有行为由 Tianyong/JubaozhaiModelTests 守住,这里只额外断言本地模式不会触发服务端事件。
    /// </summary>
    public sealed class JubaozhaiServerPagingTests
    {
        private const long Now = 1800000000;

        [Test]
        public void LocalModeNeverRaisesServerEvents_AndIgnoresServiceStatus()
        {
            var state = new JubaozhaiState(clock: () => Now);
            int queries = 0, favorites = 0, details = 0;
            state.QueryChanged += () => queries++;
            state.FavoriteRequested += (_, _) => favorites++;
            state.DetailRequested += _ => details++;
            state.SetListings(new[] { Listing("a"), Listing("b") });
            state.SetCategory(JubaozhaiCategory.Pet); state.SetCategory(JubaozhaiCategory.Character);
            state.SetSearch("名称"); state.SetSearch(""); state.SetSort(JubaozhaiSort.PriceDescending);
            state.ToggleFavorite("a"); state.RequestDetail("a");
            state.SetServiceStatus("本地模式不应生效", false);
            Assert.That(queries, Is.Zero);
            Assert.That(favorites, Is.Zero);
            Assert.That(details, Is.Zero);
            Assert.That(state.ServerPaged, Is.False);
            Assert.That(state.IsFavorite("a"), Is.True);
            Assert.That(state.ServiceStatus, Is.Empty);
            Assert.That(state.ServiceAvailable, Is.True);
            Assert.That(state.Status, Is.Empty);
        }

        [Test]
        public void EnterServerPaging_ClearsDemoDataAndRaisesExactlyOneQuery()
        {
            var state = new JubaozhaiState(clock: () => Now);
            state.LoadDemo();
            state.ToggleFavorite(state.GetPage().Items[0].Id);
            int queries = 0;
            state.QueryChanged += () => queries++;
            int before = state.QueryVersion;
            state.EnterServerPaging();
            Assert.That(state.ServerPaged, Is.True);
            Assert.That(state.IsDemo, Is.False);
            Assert.That(state.Loading, Is.True);
            Assert.That(state.FavoriteCount, Is.Zero);
            Assert.That(state.GetPage().Items, Is.Empty);
            Assert.That(state.GetPage().PageCount, Is.EqualTo(1));
            Assert.That(queries, Is.EqualTo(1));
            Assert.That(state.QueryVersion, Is.EqualTo(before + 1));
        }

        [Test]
        public void EveryFilterBumpsVersionAndReturnsToFirstPage_AndSetPageClampsToServerPageCount()
        {
            var versions = new List<int>();
            var state = ServerState(versions);
            Assert.That(state.ApplyServerPage(state.QueryVersion, new[] { Listing("1") }, 40, 1, 10, Now), Is.True);
            var filters = new Action[]
            {
                () => state.SetCategory(JubaozhaiCategory.Weapon),
                () => state.SetSubcategory("剑"),
                () => state.SetTab(JubaozhaiTab.PublicNotice),
                () => state.SetSection(JubaozhaiSection.Shelf),
                () => state.SetSearch("  青云  "),
                () => state.SetSort(JubaozhaiSort.PriceDescending),
                () => state.SetFavoritesOnly(true)
            };
            foreach (var filter in filters)
            {
                state.SetPage(3);
                Assert.That(state.PageNumber, Is.EqualTo(3));
                int before = state.QueryVersion;
                filter();
                Assert.That(state.QueryVersion, Is.EqualTo(before + 1));
                Assert.That(state.PageNumber, Is.EqualTo(1));
                Assert.That(state.Loading, Is.True);
                Assert.That(versions.Last(), Is.EqualTo(state.QueryVersion));
            }
            Assert.That(state.Search, Is.EqualTo("青云"));

            int version = state.QueryVersion;
            state.SetPage(99);
            Assert.That(state.PageNumber, Is.EqualTo(10));
            Assert.That(state.QueryVersion, Is.EqualTo(version + 1));
            state.SetPage(10);
            Assert.That(state.QueryVersion, Is.EqualTo(version + 1), "页码未变化时不应发出查询");
            state.SetPage(0);
            Assert.That(state.PageNumber, Is.EqualTo(1));
            Assert.That(state.QueryVersion, Is.EqualTo(version + 2));
        }

        [Test]
        public void StalePageOrPageAfterLeavingServerModeIsRejectedWithoutTouchingState()
        {
            var state = ServerState(new List<int>());
            int sent = state.QueryVersion;
            Assert.That(state.ApplyServerPage(sent, new[] { Listing("1", favorite: true) }, 1, 1, 1, Now), Is.True);
            state.SetSearch("新条件");
            Assert.That(state.ApplyServerPage(sent, new[] { Listing("2") }, 9, 3, 5, Now), Is.False);
            var page = state.GetPage();
            Assert.That(page.Items.Select(item => item.Id), Is.EqualTo(new[] { "1" }));
            Assert.That(page.TotalCount, Is.EqualTo(1));
            Assert.That(page.PageCount, Is.EqualTo(1));
            Assert.That(state.IsFavorite("1"), Is.True);
            Assert.That(state.Loading, Is.True, "过期回包不能结束新查询的读取状态");
            state.Reset();
            Assert.That(state.ApplyServerPage(state.QueryVersion, new[] { Listing("3") }, 1, 1, 1, Now), Is.False);
            Assert.That(state.GetPage().Items, Is.Empty);
        }

        [Test]
        public void GetPageReturnsInjectedPageVerbatim_WithoutLocalFilteringSortingOrPageRewrite()
        {
            var state = ServerState(new List<int>());
            state.SetFavoritesOnly(true);
            state.SetSearch("本地不会匹配");
            // 服务端已按条件筛好:类目、收藏、搜索词、过期与排序都不在本地复核。
            var pet = Listing("9", JubaozhaiCategory.Pet, price: 1);
            var expired = Listing("8", price: 99);
            expired.ExpiresAtUnixSeconds = Now - 10;
            Assert.That(state.ApplyServerPage(state.QueryVersion, new[] { pet, expired }, 50, 7, 13, Now), Is.True);
            var page = state.GetPage();
            Assert.That(page.Items.Select(item => item.Id), Is.EqualTo(new[] { "9", "8" }));
            Assert.That(page.TotalCount, Is.EqualTo(50));
            Assert.That(page.PageNumber, Is.EqualTo(7));
            Assert.That(page.PageCount, Is.EqualTo(13));
            state.GetPage();
            Assert.That(state.PageNumber, Is.EqualTo(7));
            page.Items[0].Name = "视图修改";
            Assert.That(state.GetPage().Items[0].Name, Is.EqualTo("名称9"));
            Assert.That(state.Loading, Is.False);
            Assert.That(state.ServiceAvailable, Is.True);
        }

        [Test]
        public void ToggleFavoriteOnlyRequests_AndApplyFavoriteIsTheOnlyWriter()
        {
            var state = ServerState(new List<int>());
            state.ApplyServerPage(state.QueryVersion, new[] { Listing("1"), Listing("2", favorite: true) }, 2, 1, 1, Now);
            Assert.That(state.IsFavorite("2"), Is.True);
            var requests = new List<(string, bool)>();
            state.FavoriteRequested += (id, favorite) => requests.Add((id, favorite));
            Assert.That(state.ToggleFavorite("1"), Is.False);
            Assert.That(state.ToggleFavorite("2"), Is.True);
            Assert.That(state.ToggleFavorite("不在本页"), Is.False);
            Assert.That(requests, Is.EqualTo(new[] { ("1", true), ("2", false) }));
            Assert.That(state.IsFavorite("1"), Is.False);
            Assert.That(state.IsFavorite("2"), Is.True);
            state.ApplyFavorite("1", true);
            state.ApplyFavorite("2", false);
            state.ApplyFavorite("不在本页", true);
            Assert.That(state.IsFavorite("1"), Is.True);
            Assert.That(state.IsFavorite("2"), Is.False);
            Assert.That(state.FavoriteCount, Is.EqualTo(1));
            Assert.That(state.GetPage().Items.Select(item => item.Favorite), Is.EqualTo(new[] { true, false }));
        }

        [Test]
        public void RemainingTimeUsesServerClock_WhenLocalClockIsADayAhead()
        {
            long local = Now + 86400;
            var state = new JubaozhaiState(clock: () => local);
            state.EnterServerPaging();
            var listing = Listing("1");
            listing.ExpiresAtUnixSeconds = Now + 2 * 86400 + 3 * 3600;
            state.ApplyServerPage(state.QueryVersion, new[] { listing }, 1, 1, 1, Now);
            Assert.That(state.RemainingTime(state.GetPage().Items[0]), Is.EqualTo("2天3时"));
            local += 3600;
            Assert.That(state.RemainingTime(state.GetPage().Items[0]), Is.EqualTo("2天2时"));
            // server_now 为 0 的页(例如失败清空页)沿用上一次的偏移。
            state.SetSearch("换条件");
            state.ApplyServerPage(state.QueryVersion, new[] { listing }, 1, 1, 1, 0);
            Assert.That(state.RemainingTime(listing), Is.EqualTo("2天2时"));
            // 退回本地模式后按本地钟计算。
            state.Reset();
            Assert.That(state.RemainingTime(listing), Is.EqualTo("1天2时"));
        }

        [TestCase("Reset")]
        [TestCase("SetListings")]
        [TestCase("LoadDemo")]
        public void LeavingServerModeRestoresLocalModeAndDropsLatePages(string exit)
        {
            var state = ServerState(new List<int>());
            int sent = state.QueryVersion;
            state.SetServiceStatus("正在读取", true);
            switch (exit)
            {
                case "Reset": state.Reset(); break;
                case "SetListings": state.SetListings(new[] { Listing("local") }); break;
                default: state.LoadDemo(); break;
            }
            Assert.That(state.ServerPaged, Is.False);
            Assert.That(state.Loading, Is.False);
            Assert.That(state.ServiceStatus, Is.Empty);
            Assert.That(state.ApplyServerPage(sent, new[] { Listing("late") }, 1, 1, 1, Now), Is.False);
            Assert.That(state.GetPage().Items.Any(item => item.Id == "late"), Is.False);
        }

        [Test]
        public void ServiceStatusTakesPriority_AndDetailIsRequestedOncePerInjectedPage()
        {
            var state = ServerState(new List<int>());
            state.SetServiceStatus("聚宝斋服务暂时不可用", false);
            Assert.That(state.Status, Is.EqualTo("聚宝斋服务暂时不可用"));
            Assert.That(state.ServiceAvailable, Is.False);
            state.ApplyServerPage(state.QueryVersion, new[] { Listing("1") }, 1, 1, 1, Now);
            Assert.That(state.Status, Is.Empty);

            var details = new List<string>();
            state.DetailRequested += id => details.Add(id);
            state.RequestDetail("1");
            state.RequestDetail("不在本页");
            Assert.That(details, Is.EqualTo(new[] { "1" }));
            state.ApplyDetail("1", "服务端描述");
            state.RequestDetail("1");
            Assert.That(details.Count, Is.EqualTo(1), "详情写回后同一页内不再重复请求");
            Assert.That(state.GetPage().Items[0].Details, Is.EqualTo("服务端描述"));
            state.SetSearch("换条件");
            state.ApplyServerPage(state.QueryVersion, new[] { Listing("1") }, 1, 1, 1, Now);
            state.RequestDetail("1");
            Assert.That(details.Count, Is.EqualTo(2), "重新注入页之后允许再次请求");
        }

        private static JubaozhaiState ServerState(List<int> versions)
        {
            var state = new JubaozhaiState(clock: () => Now);
            state.QueryChanged += () => versions.Add(state.QueryVersion);
            state.EnterServerPaging();
            return state;
        }

        private static JubaozhaiListing Listing(string id, JubaozhaiCategory category = JubaozhaiCategory.Character,
            decimal price = 100, bool favorite = false)
            => new() { Id = id, Name = "名称" + id, Category = category, Price = price, Favorite = favorite };
    }
}
