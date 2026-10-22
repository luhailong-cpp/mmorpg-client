using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.Jubaozhai;
using MmorpgClient.UI.Ugui.Jubaozhai;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Jubaozhai
{
    /// <summary>Only isolated test records enter this fixture; production State/client ownership remains unchanged.</summary>
    public sealed class JubaozhaiFidelityWindowTests
    {
        private const long Now = 1800000000;
        private GameObject _fixture;
        private JubaozhaiState _state;
        private JubaozhaiWindow _window;

        [SetUp]
        public void SetUp()
        {
            _fixture = new GameObject("JubaoFidelityFixture", typeof(RectTransform), typeof(Canvas));
            ((RectTransform)_fixture.transform).sizeDelta = new Vector2(2560, 1080);
            _state = new JubaozhaiState(6, () => Now);
            _window = new JubaozhaiWindow(_fixture.transform, _state);
            _window.Show();
        }

        [TearDown]
        public void TearDown()
        {
            _window?.Dispose();
            if (_fixture != null) UnityEngine.Object.DestroyImmediate(_fixture);
        }

        [Test]
        public void AuthoritativeServerPageKeepsAllRowsOrderAndPageNumberDespiteLocalSearchAndCategory()
        {
            _state.EnterServerPaging(); _state.SetCategory(JubaozhaiCategory.Weapon); _state.SetSearch("不会在本地匹配");
            var records = Enumerable.Range(1, 6).Select(i => Listing("s" + i, JubaozhaiCategory.Pet)).ToArray();
            _state.ApplyServerPage(_state.QueryVersion, records, 78, 7, 13, Now);
            var rows = _fixture.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("JubaoRow_")).ToArray();
            Assert.That(rows.Select(b => b.name), Is.EqualTo(records.Select(item => "JubaoRow_" + item.Id)));
            Assert.That(_state.PageNumber, Is.EqualTo(7)); Assert.That(_state.GetPage().PageCount, Is.EqualTo(13));
            var scroll = _fixture.GetComponentsInChildren<ScrollRect>().Single(s => s.name == "JubaoRowsViewport");
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            scroll.verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases();
            Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(0));
            Click("JubaoRow_s6"); Assert.That(_window.SelectedId, Is.EqualTo("s6"));
            Assert.That(Button("JubaoView").interactable, Is.True);
            Assert.That(Button("JubaoPreviousPage").interactable, Is.True); Assert.That(Button("JubaoNextPage").interactable, Is.True);
        }

        [Test]
        public void FavoriteWaitsForServerConfirmationAndDetailStatusRefreshPreservesUnknownOutcome()
        {
            var requests = new List<(string, bool)>(); var details = new List<string>(); int commerce = 0;
            _window.CommerceRequested += () => ++commerce;
            _state.FavoriteRequested += (id, favorite) => requests.Add((id, favorite)); _state.DetailRequested += details.Add;
            _state.EnterServerPaging(); _state.ApplyServerPage(_state.QueryVersion, new[] { Listing("a") }, 1, 1, 1, Now);
            Click("JubaoRow_a"); Click("JubaoFavorite");
            Assert.That(requests, Is.EqualTo(new[] { ("a", true) })); Assert.That(_state.IsFavorite("a"), Is.False);
            _state.ApplyFavorite("a", true); Assert.That(_state.IsFavorite("a"), Is.True);
            Click("JubaoView"); Assert.That(details, Is.EqualTo(new[] { "a" }));
            _state.ApplyDetail("a", "服务端返回的完整商品详情");
            Assert.That(details.Count, Is.EqualTo(1), "写回详情不得重复发请求");
            Assert.That(HasText("服务端返回的完整商品详情"), Is.True);
            _state.SetServiceStatus("请求超时，结果未知；正在隔离当前操作", false);
            Assert.That(_window.ModalVisible, Is.True); Assert.That(HasText("请求超时，结果未知；正在隔离当前操作"), Is.True);
            Click("JubaoModalFavorite"); Assert.That(requests.Last(), Is.EqualTo(("a", false)));
            Assert.That(_state.IsFavorite("a"), Is.True, "取消收藏也必须等待服务端确认");
            Assert.That(Button("JubaoPurchase").GetComponentInChildren<TMP_Text>().text, Is.EqualTo("查看可交易商品"));
            Click("JubaoPurchase"); Assert.That(commerce, Is.EqualTo(1), "该入口只导航至正式可交易商品窗口");
            Assert.That(requests, Is.EqualTo(new[] { ("a", true), ("a", false) }), "导航不发送额外收藏写操作");
            Assert.That(details, Is.EqualTo(new[] { "a" }), "导航不重复请求当前详情");
            Assert.That(_window.ModalVisible, Is.True, "无宿主导航订阅时仍保留结果未知提示");
        }

        [Test]
        public void AllCategoryAndSubcategoryQueriesRemainReachableAndActiveCategoryMeansAll()
        {
            _state.EnterServerPaging();
            foreach (var category in JubaozhaiCatalog.AllCategories)
            {
                Click("Category_" + category); Assert.That(_state.Category, Is.EqualTo(category));
                foreach (var sub in JubaozhaiCatalog.SubcategoriesFor(category))
                {
                    Click("Subcategory_" + sub); Assert.That(_state.Subcategory, Is.EqualTo(sub));
                }
                Click("Category_" + category); Assert.That(_state.Subcategory, Is.Empty);
            }
            Assert.That(_state.IsDemo, Is.False); Assert.That(_state.GetPage().Items, Is.Empty);
        }

        [Test]
        public void NativePageButtonsDelegateToServerQueryStateWithoutInventingLocalResults()
        {
            int queries = 0; _state.QueryChanged += () => ++queries;
            _state.EnterServerPaging(); _state.ApplyServerPage(_state.QueryVersion, new[] { Listing("first") }, 12, 1, 2, Now);
            int baseline = queries; Click("JubaoNextPage");
            Assert.That(_state.PageNumber, Is.EqualTo(2)); Assert.That(queries, Is.EqualTo(baseline + 1)); Assert.That(_state.Loading, Is.True);
            Assert.That(_state.GetPage().Items.Single().Id, Is.EqualTo("first"), "新页只由服务端回包写入");
            _state.ApplyServerPage(_state.QueryVersion, new[] { Listing("second") }, 12, 2, 2, Now);
            Assert.That(Button("JubaoNextPage").interactable, Is.False); Assert.That(Button("JubaoPreviousPage").interactable, Is.True);
            Click("JubaoPreviousPage"); Assert.That(_state.PageNumber, Is.EqualTo(1)); Assert.That(queries, Is.EqualTo(baseline + 2));
        }

        private Button Button(string name) => _fixture.GetComponentsInChildren<Button>(true).Single(b => b.name == name);
        private void Click(string name) { var button = Button(name); Assert.That(button.interactable, Is.True, name); button.onClick.Invoke(); }
        private bool HasText(string text) => _fixture.GetComponentsInChildren<TMP_Text>().Any(t => t.text == text);
        private static JubaozhaiListing Listing(string id, JubaozhaiCategory category = JubaozhaiCategory.Character)
            => new JubaozhaiListing { Id = id, Name = "测试商品" + id, Category = category, Summary = "只用于回归测试的记录", Details = "",
                Price = 123.45m, Level = 20, ExpiresAtUnixSeconds = Now + 86400, Tab = JubaozhaiTab.Consignment, Section = JubaozhaiSection.Consignment };
    }
}
