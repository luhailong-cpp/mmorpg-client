using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MmorpgClient.Game.Jubaozhai;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class JubaozhaiModelTests
    {
        private const long Now = 1800000000;

        [Test]
        public void ProductionStartsEmpty_AndDemoCannotLeakIntoLiveSession()
        {
            var state = new JubaozhaiState(clock: () => Now);
            Assert.That(state.GetPage().Items, Is.Empty);
            Assert.That(state.ServiceAvailable, Is.False);
            Assert.That(state.IsDemo, Is.False);
            state.LoadDemo();
            Assert.That(state.IsDemo, Is.True);
            Assert.That(state.ServiceAvailable, Is.False);
            var demo = state.GetPage().Items[0];
            Assert.That(demo.Id, Does.StartWith("DEMO-"));
            Assert.That(demo.Details, Does.Contain("离线演示"));
            state.ToggleFavorite(demo.Id);
            state.SetListings(new[] { Listing("live-1", "真实接口商品") });
            Assert.That(state.IsDemo, Is.False);
            Assert.That(state.ServiceAvailable, Is.True);
            Assert.That(state.FavoriteCount, Is.Zero);
            Assert.That(state.GetPage().Items.Single().Id, Is.EqualTo("live-1"));
            state.Reset();
            Assert.That(state.GetPage().Items, Is.Empty);
            Assert.That(state.ServiceAvailable, Is.False);
        }

        [Test]
        public void SearchIntersectsSchoolCategoryTabAndSection_AndMatchesNamesAndCaseInsensitiveIds()
        {
            var wanted = Listing("ROLE-AbC123", "青云小道", school: "玄霄");
            var wrongSchool = Listing("role-abc456", "青云小道", school: "破军");
            var wrongCategory = Listing("pet-abc123", "青云小道", category: JubaozhaiCategory.Pet);
            var notice = Listing("notice-abc123", "青云小道", school: "玄霄");
            notice.Tab = JubaozhaiTab.PublicNotice;
            var auction = Listing("auction-abc123", "青云小道", school: "玄霄");
            auction.Section = JubaozhaiSection.Auction;
            var state = new JubaozhaiState();
            state.SetListings(new[] { wanted, wrongSchool, wrongCategory, notice, auction });
            state.SetSubcategory("玄霄");
            state.SetSearch("  abc123  ");
            Assert.That(state.GetPage().Items.Select(item => item.Id), Is.EqualTo(new[] { wanted.Id }));
            state.SetSearch("青云");
            Assert.That(state.GetPage().Items.Single().Id, Is.EqualTo(wanted.Id));
            state.SetTab(JubaozhaiTab.PublicNotice);
            Assert.That(state.GetPage().Items.Single().Id, Is.EqualTo(notice.Id));
            state.SetTab(JubaozhaiTab.Consignment);
            state.SetSection(JubaozhaiSection.Auction);
            Assert.That(state.GetPage().Items.Single().Id, Is.EqualTo(auction.Id));
            state.SetCategory(JubaozhaiCategory.Pet);
            Assert.That(state.Subcategory, Is.Empty);
        }

        [Test]
        public void SortingIsDeterministic_AndPagesClampAfterDataShrinks()
        {
            var state = new JubaozhaiState(pageSize: 2);
            state.SetListings(new[]
            {
                Listing("c", "丙", price: 20), Listing("a", "甲", price: 10),
                Listing("b", "乙", price: 10), Listing("d", "丁", price: 30), Listing("e", "戊", price: 40)
            });
            state.SetSort(JubaozhaiSort.PriceAscending);
            Assert.That(state.GetPage().Items.Select(item => item.Id), Is.EqualTo(new[] { "a", "b" }));
            state.SetPage(int.MaxValue);
            Assert.That(state.GetPage().PageNumber, Is.EqualTo(3));
            Assert.That(state.GetPage().Items.Single().Id, Is.EqualTo("e"));
            state.SetListings(new[] { Listing("a", "甲", price: 10), Listing("b", "乙", price: 10) });
            Assert.That(state.GetPage().PageNumber, Is.EqualTo(1));
            state.SetSearch("不存在");
            var empty = state.GetPage();
            Assert.That(empty.Items, Is.Empty);
            Assert.That(empty.PageCount, Is.EqualTo(1));
            Assert.That(empty.PageNumber, Is.EqualTo(1));
        }

        [Test]
        public void FavoritesSurviveFilters_AndRemovingLastPageFavoriteClampsPagination()
        {
            var state = new JubaozhaiState(pageSize: 1);
            state.SetListings(new[] { Listing("a", "重名"), Listing("b", "重名") });
            Assert.That(state.ToggleFavorite("unknown"), Is.False);
            Assert.That(state.FavoriteCount, Is.Zero);
            state.ToggleFavorite("a"); state.ToggleFavorite("b");
            state.SetCategory(JubaozhaiCategory.Pet);
            Assert.That(state.GetPage().Items, Is.Empty);
            state.SetCategory(JubaozhaiCategory.Character);
            Assert.That(state.IsFavorite("a"), Is.True);
            state.SetFavoritesOnly(true); state.SetPage(2);
            Assert.That(state.GetPage().Items.Single().Id, Is.EqualTo("b"));
            Assert.That(state.ToggleFavorite("b"), Is.False);
            Assert.That(state.PageNumber, Is.EqualTo(1));
            Assert.That(state.GetPage().Items.Single().Id, Is.EqualTo("a"));
            state.SetListings(new[] { Listing("b", "重名") });
            Assert.That(state.FavoriteCount, Is.Zero);
            Assert.That(state.GetPage().Items, Is.Empty);
        }

        [Test]
        public void ServiceSnapshotsAreDefensiveAndAtomic_WhenCallerMutatesOrEnumerationFails()
        {
            var record = Listing("a", "原名称");
            var state = new JubaozhaiState();
            state.SetListings(new[] { record, Listing("a", "重复编号"), null });
            record.Name = "外部修改";
            var page = state.GetPage();
            Assert.That(page.TotalCount, Is.EqualTo(1));
            Assert.That(page.Items.Single().Name, Is.EqualTo("原名称"));
            page.Items[0].Name = "视图修改";
            Assert.That(state.GetPage().Items.Single().Name, Is.EqualTo("原名称"));
            Assert.Throws<InvalidOperationException>(() => state.SetListings(BrokenSnapshot()));
            Assert.That(state.GetPage().Items.Single().Id, Is.EqualTo("a"));
        }

        [Test]
        public void FinalSchoolsExpiryAndPriceRemainCorrectAcrossClocksAndLocales()
        {
            Assert.That(JubaozhaiCatalog.SubcategoriesFor(JubaozhaiCategory.Character),
                Is.EqualTo(new[] { "破军", "玄霄", "逐风", "丹心" }));
            Assert.That(JubaozhaiCatalog.AllCategories.Select(JubaozhaiCatalog.CategoryName),
                Is.EqualTo(new[] { "角色", "宠物", "武器", "防具", "套装", "法宝", "首饰", "召唤令" }));
            long now = Now;
            var state = new JubaozhaiState(clock: () => now);
            Assert.Throws<ArgumentException>(() => state.SetSubcategory("枪"));
            var unscheduled = Listing("unknown", "未提供期限");
            var expired = Listing("expired", "已结束"); expired.ExpiresAtUnixSeconds = Now - 1;
            var active = Listing("active", "进行中"); active.ExpiresAtUnixSeconds = Now + 2 * 86400 + 3 * 3600;
            state.SetListings(new[] { unscheduled, active, expired });
            state.SetSort(JubaozhaiSort.RemainingTimeAscending);
            Assert.That(state.GetPage().Items.Select(item => item.Id), Is.EqualTo(new[] { "active", "unknown" }));
            Assert.That(state.RemainingTime(unscheduled), Is.EqualTo("—"));
            Assert.That(state.RemainingTime(expired), Is.EqualTo("已结束"));
            Assert.That(state.RemainingTime(active), Is.EqualTo("2天3时"));
            now = active.ExpiresAtUnixSeconds;
            Assert.That(state.GetPage().Items.Single().Id, Is.EqualTo("unknown"));
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                Assert.That(JubaozhaiCatalog.FormatPrice(1234.5m), Is.EqualTo("1,234.50 元"));
            }
            finally { CultureInfo.CurrentCulture = originalCulture; }
        }

        private static JubaozhaiListing Listing(string id, string name, string school = "破军",
            JubaozhaiCategory category = JubaozhaiCategory.Character, decimal price = 100)
            => new() { Id = id, Name = name, School = school, Category = category, Price = price };

        private static IEnumerable<JubaozhaiListing> BrokenSnapshot()
        {
            yield return Listing("partial", "不应安装的半份数据");
            throw new InvalidOperationException("The service adapter failed while enumerating its snapshot.");
        }
    }
}