using System;
using System.Collections.Generic;
using MmorpgClient.Game.Jubaozhai;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Jubaozhai.JubaozhaiArt;

namespace MmorpgClient.UI.Ugui.Jubaozhai
{
    /// <summary>Interactive native catalog. Data and session-local favorites live in State, outside the skin.</summary>
    public sealed partial class JubaozhaiWindow : IDisposable
    {
        public JubaozhaiState State { get; }
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;
        public bool ModalVisible => _modal != null && _modal.gameObject.activeSelf;
        public string SelectedId { get; private set; } = "";
        public event Action CommerceRequested;

        private readonly RectTransform _root, _frame, _rows, _categoryContent;
        private readonly CanvasGroup _frameInput;
        private readonly ScrollRect _categoryScroll;
        private readonly TMP_InputField _search;
        private readonly TMP_Text _pageLabel, _status, _selection;
        private readonly Button _favorite, _view, _previous, _next, _favoritesOnly, _sort;
        private readonly Button[] _topTabs = new Button[2], _sections = new Button[3];
        private readonly Dictionary<JubaozhaiCategory, Button> _categoryButtons = new();
        private readonly Dictionary<string, Button> _subcategoryButtons = new();
        private readonly Dictionary<string, Button> _rowButtons = new();
        private readonly Dictionary<string, TMP_Text> _remainingLabels = new();
        private IReadOnlyList<JubaozhaiListing> _pageItems = Array.Empty<JubaozhaiListing>();
        private JubaozhaiCategory? _builtCategory;
        private RectTransform _modal;
        private Button _modalFavorite;
        private GameObject _returnFocus, _modalReturnFocus, _lastScopedFocus;
        private bool _disposed;
        private float _nextTickTime;
        private int _renderedTotal, _renderedPageNumber, _renderedPageCount;
        private JubaozhaiListing _detailSnapshot;
        private TMP_Text _modalRemainingText;
        // 详情弹窗的状态行:服务端模式下收藏被拒、详情 NotFound 等只改 Status 不改条目,弹窗不会重建,需随 Render 刷新。
        private TMP_Text _modalStatusText;

        public JubaozhaiWindow(UnityEngine.Transform parent, JubaozhaiState state = null)
        {
            State = state ?? new JubaozhaiState();
            _root = QdaoUguiFactory.CreateStretch("JubaozhaiWindow", parent, Vector4.zero);
            _root.gameObject.SetActive(false);
            var shade = _root.gameObject.AddComponent<Image>(); shade.color = new Color(.025f, .10f, .08f, .65f); shade.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            _frame = QdaoUguiFactory.CreateCenteredRect("JubaozhaiFrame", _root, 1932, 814);
            _frame.gameObject.AddComponent<JubaozhaiDesignFit>();
            _frameInput = _frame.gameObject.AddComponent<CanvasGroup>();
            NativeArt(_frame, "native-frame", 0, 0, 1932, 814);
            NativeHit(_frame, "JubaoClose", "", 1735, 60, 65, 62, Hide);
            _topTabs[0] = Button(_frame, "JubaoTab_Consignment", "寄售列表", 507, 136, 253, 56,
                () => State.SetTab(JubaozhaiTab.Consignment), key: "tab_normal", fontSize: 30);
            _topTabs[1] = Button(_frame, "JubaoTab_PublicNotice", "公示列表", 763, 136, 245, 56,
                () => State.SetTab(JubaozhaiTab.PublicNotice), key: "tab_normal", fontSize: 30);
            Button(_frame, "JubaoCommerce", "交易中心", 1027, 136, 380, 53,
                () => CommerceRequested?.Invoke(), fontSize: 27);
            Button(_frame, "JubaoEstimate", "估价", 1425, 136, 137, 53,
                () => ShowNotice("估价", "估价功能尚未开放，开放后可在此查看商品估价。"), fontSize: 27);
            Button(_frame, "JubaoRules", "规则", 1578, 136, 139, 53,
                () => ShowNotice("聚宝斋规则", "寄售、公示、拍卖和货架规则尚未开放。交易规则以正式开放后的说明为准。"), fontSize: 27);
            var clearSearch = Button(_frame, "JubaoClearSearch", "清空搜索", 200, 198, 263, 50,
                () => { _search.SetTextWithoutNotify(""); State.SetSearch(""); }, key: "tab_normal", fontSize: 28);
            NativeArt(clearSearch.transform, "category-search", 19, 5, 35, 37);
            _favoritesOnly = Button(_frame, "JubaoFavoritesOnly", "收藏", 200, 251, 263, 50,
                () => State.SetFavoritesOnly(!State.FavoritesOnly), key: "tab_normal", fontSize: 29);
            var favoritesLabel = _favoritesOnly.GetComponentInChildren<TMP_Text>(); favoritesLabel.alignment = TextAlignmentOptions.MidlineLeft;
            favoritesLabel.rectTransform.anchoredPosition = new Vector2(65, 0); favoritesLabel.rectTransform.sizeDelta = new Vector2(151, 50);
            NativeArt(_favoritesOnly.transform, "category-favorite", 19, 6, 35, 36);
            Text(_favoritesOnly.transform, "›", 224, 0, 26, 50, 31, Gold, alignment: TextAlignmentOptions.Center).name = "FavoriteMarker";
            _categoryScroll = NativeCategoryScroll(_frame, "JubaoCategories", 200, 304, 263, 431, out _categoryContent);
            NativeArt(_frame, "table-header", 500, 198, 1226, 51);
            _sort = NativeHit(_frame, "JubaoSort", "默认排序 · 切换", 580, 204, 424, 39, CycleSort, 25);
            Text(_frame, "信息", 1097, 204, 191, 39, 27, alignment: TextAlignmentOptions.Center);
            Text(_frame, "剩余时间", 1517, 204, 169, 39, 27, alignment: TextAlignmentOptions.Center);
            _rowScroll = NativeCategoryScroll(_frame, "JubaoRowsViewport", 501, 247, 1224, 451, out _rows);
            string[] sectionLabels = { "寄\n售", "拍\n卖", "货\n架" };
            float[] sectionY = { 188, 351, 512 }, sectionHeight = { 159, 160, 147 };
            for (int i = 0; i < _sections.Length; i++)
            {
                var section = (JubaozhaiSection)i;
                _sections[i] = NativeButton(_frame, "JubaoSection_" + section, sectionLabels[i], i == 2 ? "section-normal-short" : "section-normal", 1737, sectionY[i],
                    93, sectionHeight[i], () => State.SetSection(section), 35);
                _sections[i].GetComponentInChildren<TMP_Text>().textWrappingMode = TextWrappingModes.Normal;
            }
            _search = QdaoUguiFactory.CreateInputField("JubaoSearch", _frame, 504, 712, 331, 56,
                JubaozhaiCatalog.SearchPlaceholder, 64, Load("input"));
            _search.textComponent.fontSize = 25; ((TMP_Text)_search.placeholder).fontSize = 24;
            _search.textComponent.richText = false; ((TMP_Text)_search.placeholder).richText = false;
            _search.onSubmit.AddListener(value => State.SetSearch(value));
            NativeButton(_frame, "JubaoSearchSubmit", "", "search-button", 820, 707, 78, 63, () => State.SetSearch(_search.text));
            _previous = NativeButton(_frame, "JubaoPreviousPage", "", "page-previous", 985, 718, 47, 48, () => State.SetPage(State.PageNumber - 1));
            _pageLabel = Text(_frame, "1 / 1", 1036, 720, 96, 42, 25, alignment: TextAlignmentOptions.Center);
            _next = NativeButton(_frame, "JubaoNextPage", "", "page-next", 1134, 718, 47, 48, () => State.SetPage(State.PageNumber + 1));
            _favorite = Button(_frame, "JubaoFavorite", "收藏", 1295, 710, 170, 58, ToggleSelectedFavorite, fontSize: 29);
            _view = Button(_frame, "JubaoView", "查看", 1475, 704, 249, 67, ShowSelectedDetails, true, fontSize: 36);
            _selection = Text(_frame, "选择商品后可查看详情", 502, 771, 758, 19, 16, Muted);
            _selection.gameObject.SetActive(false);
            NativeCategoryScroll(_frame, "JubaoStatusScroll", 502, 771, 1224, 26, out var statusContent);
            _status = Text(statusContent, "聚宝斋尚未开放", 0, 0, 1218, 26, 20, Muted, true, TextAlignmentOptions.Center);
            State.Changed += StateChanged; Render();
        }

        public void Show()
        {
            if (_disposed) return;
            if (!IsVisible) _returnFocus = EventSystem.current?.currentSelectedGameObject;
            CloseModal();
            _root.gameObject.SetActive(true);
            Render();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_topTabs[(int)State.Tab].gameObject);
        }

        public void Hide()
        {
            if (_disposed || _root == null) return;
            bool wasVisible = IsVisible;
            CloseModal();
            _root.gameObject.SetActive(false);
            if (wasVisible && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_returnFocus != null && _returnFocus.activeInHierarchy ? _returnFocus : null);
            _returnFocus = null;
        }

        public void Back() { if (ModalVisible) CloseModal(); else Hide(); }
        public void ResetSession() { Hide(); SelectedId = ""; State.Reset(); }
        public void Dispose()
        {
            if (_disposed) return;
            Hide(); State.Changed -= StateChanged; _disposed = true;
        }

        public void Tick(float unscaledTime)
        {
            if (_disposed || !IsVisible) return;
            ConstrainFocus();
            if (unscaledTime < _nextTickTime) return;
            _nextTickTime = unscaledTime + 1;
            var page = State.GetPage();
            bool changed = page.Items.Count != _pageItems.Count || page.TotalCount != _renderedTotal ||
                page.PageNumber != _renderedPageNumber || page.PageCount != _renderedPageCount;
            for (int i = 0; !changed && i < page.Items.Count; i++) changed = page.Items[i].Id != _pageItems[i].Id;
            if (changed) { Render(); return; }
            foreach (var listing in page.Items)
                if (_remainingLabels.TryGetValue(listing.Id, out var label) && label != null) label.text = State.RemainingTime(listing);
            if (_modalRemainingText != null && _detailSnapshot != null)
                _modalRemainingText.text = "剩余时间  " + State.RemainingTime(_detailSnapshot);
        }

        private void StateChanged() { if (!_disposed) Render(); }
        private void Render()
        {
            var focused = EventSystem.current?.currentSelectedGameObject;
            string focusName = focused != null && focused.transform.IsChildOf(_frame) ? focused.name : null;
            if (_builtCategory != State.Category) BuildCategories();
            for (int i = 0; i < _topTabs.Length; i++) Select(_topTabs[i], (int)State.Tab == i);
            for (int i = 0; i < _sections.Length; i++) NativeSelect(_sections[i], (int)State.Section == i, i == 2 ? "section-normal-short" : "section-normal", i == 2 ? "section-selected-short" : "section-selected");
            foreach (var pair in _categoryButtons)
            {
                bool active = State.Category == pair.Key;
                Select(pair.Value, active);
                SetButtonLabel(pair.Value, JubaozhaiCatalog.CategoryName(pair.Key));
                var marker = pair.Value.transform.Find("CategoryMarker")?.GetComponent<TMP_Text>();
                if (marker != null) { marker.text = active ? "✓" : "›"; marker.color = active ? Cream : Gold; }
            }
            foreach (var pair in _subcategoryButtons)
            {
                bool active = State.Subcategory == pair.Key;
                Select(pair.Value, active, "tab_normal", "tab_selected");
                SetButtonLabel(pair.Value, pair.Key.Length == 0 ? "全部" : pair.Key);
            }
            if (!_search.isFocused) _search.SetTextWithoutNotify(State.Search);
            _status.text = State.IsDemo ? "离线演示\n商品与价格均为样例" : State.ServerPaged ? ServerStatusText()
                : State.ServiceAvailable ? "收藏仅在本次登录保留" : "聚宝斋尚未开放";
            LayoutNativeStatus();
            var page = State.GetPage();
            _pageItems = page.Items;
            PrepareNativeRows(page);
            _renderedTotal = page.TotalCount; _renderedPageNumber = page.PageNumber; _renderedPageCount = page.PageCount;
            bool selectedFound = false;
            foreach (var item in _pageItems) if (item.Id == SelectedId) selectedFound = true;
            if (!selectedFound) { SelectedId = ""; if (ModalVisible) CloseModal(); }
            bool refreshDetails = ModalVisible && _detailSnapshot != null && selectedFound && !SameListing(_detailSnapshot, SelectedListing());
            Clear(_rows); _rowButtons.Clear(); _remainingLabels.Clear();
            if (page.Items.Count == 0) RenderEmpty();
            else for (int i = 0; i < page.Items.Count; i++) RenderRow(page.Items[i], i);
            _pageLabel.text = page.PageNumber + " / " + page.PageCount;
            _previous.interactable = page.PageNumber > 1;
            _next.interactable = page.PageNumber < page.PageCount;
            SetButtonLabel(_favoritesOnly, "收藏");
            var favoriteMarker = _favoritesOnly.transform.Find("FavoriteMarker")?.GetComponent<TMP_Text>();
            if (favoriteMarker != null) { favoriteMarker.text = State.FavoritesOnly ? "✓" : "›"; favoriteMarker.color = State.FavoritesOnly ? Cream : Gold; }
            Select(_favoritesOnly, State.FavoritesOnly, "tab_normal", "tab_selected");
            SetButtonLabel(_sort, NativeSortLabel(State.Sort));
            UpdateSelection();
            if (refreshDetails) ShowSelectedDetails();
            if (!ModalVisible) BoundNavigation(_frame);
            if (!string.IsNullOrEmpty(focusName) && focused != null && !focused.activeInHierarchy && EventSystem.current != null)
                foreach (var control in _frame.GetComponentsInChildren<Selectable>())
                    if (control.name == focusName && control.IsInteractable()) { EventSystem.current.SetSelectedGameObject(control.gameObject); break; }
        }

        private void BuildCategories()
        {
            Clear(_categoryContent); _categoryButtons.Clear(); _subcategoryButtons.Clear();
            float y = 0, selectedY = 0;
            foreach (var category in JubaozhaiCatalog.AllCategories)
            {
                var captured = category;
                var button = Button(_categoryContent, "Category_" + captured, JubaozhaiCatalog.CategoryName(captured), 0, y, 263, 50,
                    () => { if (State.Category == captured) State.SetSubcategory(""); else State.SetCategory(captured); }, key: "tab_normal", fontSize: 29);
                var label = button.GetComponentInChildren<TMP_Text>(); label.alignment = TextAlignmentOptions.MidlineLeft;
                label.rectTransform.anchoredPosition = new Vector2(65, 0); label.rectTransform.sizeDelta = new Vector2(151, 50);
                NativeArt(button.transform, "category-" + captured.ToString().ToLowerInvariant(), 18, 5, 36, 39);
                Text(button.transform, "›", 224, 0, 26, 50, 31, Gold, alignment: TextAlignmentOptions.Center).name = "CategoryMarker";
                _categoryButtons.Add(captured, button);
                if (State.Category == captured) selectedY = y;
                y += 54;
                if (State.Category != captured || JubaozhaiCatalog.SubcategoriesFor(captured).Count == 0) continue;
                // Clicking the active category selects all its subcategories, as in the approved design.
                foreach (var subcategory in JubaozhaiCatalog.SubcategoriesFor(captured))
                {
                    string value = subcategory;
                    _subcategoryButtons[value] = Button(_categoryContent, "Subcategory_" + value, value, 7, y, 249, 43, () => State.SetSubcategory(value), key: "tab_normal", fontSize: 26);
                    y += 46;
                }
                y += 18;
            }
            _categoryContent.sizeDelta = new Vector2(263, Math.Max(431, y));
            Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_categoryScroll.transform);
            _categoryScroll.StopMovement();
            float range = Mathf.Max(0, _categoryContent.rect.height - _categoryScroll.viewport.rect.height);
            // The first category stays at the source's native y=304. Later categories scroll into reach.
            float offset = Mathf.Clamp(selectedY - 54, 0, range);
            _categoryScroll.verticalNormalizedPosition = range > 0 ? 1 - offset / range : 1;
            _builtCategory = State.Category;
        }

        private void RenderRow(JubaozhaiListing item, int index)
        {
            var row = Button(_rows, "JubaoRow_" + item.Id, "", 0, index * 114, 1224, 108,
                () => SelectListing(item.Id), key: item.Id == SelectedId ? "row_selected" : "row_normal");
            _rowButtons[item.Id] = row;
            DrawIcon(row.transform, item, 38, -6, 140);
            var name = Text(row.transform, item.Name, 224, 12, 350, 43, 31); name.fontStyle = FontStyles.Bold;
            name.enableAutoSizing = true; name.fontSizeMin = 24; name.fontSizeMax = 31;
            Text(row.transform, JubaozhaiCatalog.FormatPrice(item.Price), 226, 55, 350, 39, 30, QdaoUguiTheme.Html("#B82D25"));
            NativeSummary(row.transform, item.Summary, 590, 17, 407, 76, 27);
            _remainingLabels[item.Id] = Text(row.transform, State.RemainingTime(item), 1030, 26, 165, 48, 29, Ink, alignment: TextAlignmentOptions.Center);
            var selected = Text(row.transform, item.Id == SelectedId ? "已选择" : State.IsFavorite(item.Id) ? "已收藏" : "", 1040, 77, 145, 24,
                18, Gold, alignment: TextAlignmentOptions.Center);
            selected.name = "RowState";
        }

        private void RenderEmpty()
        {
            Art(_rows, "empty_guide", 187, 53, 409, 341, true);
            NativeArt(_rows, "empty-bubble", 504, 128, 451, 150);
            string title = State.FavoritesOnly ? "当前筛选下暂无收藏" : !string.IsNullOrWhiteSpace(State.Search) ? "未找到符合条件的商品" : JubaozhaiCatalog.EmptyTitle;
            Text(_rows, title, 579, 155, 316, 53, 29, alignment: TextAlignmentOptions.Center);
            Text(_rows, !State.IsDemo && !State.ServiceAvailable ? "聚宝斋服务尚未连接" : JubaozhaiCatalog.EmptyDescription, 579, 210, 316, 43,
                27, Muted, alignment: TextAlignmentOptions.Center);
        }

        private void SelectListing(string id)
        {
            SelectedId = id;
            foreach (var pair in _rowButtons)
            {
                pair.Value.image.sprite = Load(pair.Key == id ? "row_selected" : "row_normal");
                var state = pair.Value.transform.Find("RowState")?.GetComponent<TMP_Text>();
                if (state != null) state.text = pair.Key == id ? "已选择" : State.IsFavorite(pair.Key) ? "已收藏" : "";
            }
            UpdateSelection();
            BoundNavigation(_frame);
        }

        private JubaozhaiListing SelectedListing()
        {
            foreach (var item in State.GetPage().Items) if (item.Id == SelectedId) return item;
            return null;
        }

        private void UpdateSelection()
        {
            var listing = SelectedListing();
            _favorite.interactable = listing != null;
            _view.interactable = listing != null;
            SetButtonLabel(_favorite, listing != null && State.IsFavorite(listing.Id) ? "取消收藏" : "收藏");
            if (_modalFavorite != null) SetButtonLabel(_modalFavorite, listing != null && State.IsFavorite(listing.Id) ? "取消收藏" : "收藏");
            if (_modalStatusText != null) _modalStatusText.text = ModalStatusText();
            _selection.text = listing == null ? "选择商品后可查看详情" : "已选择 · " + listing.Name;
        }

        private void ToggleSelectedFavorite()
        {
            var listing = SelectedListing();
            if (listing != null) State.ToggleFavorite(listing.Id);
        }

        private void ShowSelectedDetails()
        {
            var item = SelectedListing();
            if (item == null) return;
            var panel = OpenModal("商品详情", 1220, 760);
            _detailSnapshot = item;
            DrawIcon(panel, item, 70, 118, 166);
            Text(panel, item.Name, 274, 124, 848, 70, 42);
            Text(panel, "编号 " + item.Id, 276, 207, 800, 44, 27, Muted);
            Text(panel, item.Summary, 70, 310, 1040, 50, 31);
            Text(panel, "价格  " + JubaozhaiCatalog.FormatPrice(item.Price), 70, 369, 640, 54, 36, Gold);
            _modalRemainingText = Text(panel, "剩余时间  " + State.RemainingTime(item), 738, 369, 412, 54, 28, Muted);
            var detailsScroll = CreateScroll(panel, "JubaoDetailScroll", 70, 449, 1080, 132, out var details);
            var body = Text(details, string.IsNullOrWhiteSpace(item.Details) ? "暂无更多商品说明。" : item.Details,
                0, 0, 1044, 128, 28, Muted, true, TextAlignmentOptions.TopLeft);
            float textHeight = Mathf.Max(128, body.GetPreferredValues(body.text, 1044, 0).y + 10);
            body.rectTransform.sizeDelta = new Vector2(1044, textHeight);
            details.sizeDelta = new Vector2(1044, textHeight);
            detailsScroll.verticalNormalizedPosition = 1;
            _modalStatusText = Text(panel, ModalStatusText(), 72, 610, 680, 40, 24, Muted);
            _modalFavorite = Button(panel, "JubaoModalFavorite", State.IsFavorite(item.Id) ? "取消收藏" : "收藏",
                70, 670, 226, 62, ToggleSelectedFavorite);
            Button(panel, "JubaoContactSeller", "联系卖家", 314, 670, 260, 62,
                () => ShowNotice("联系卖家", "联系卖家功能尚未开放，暂时无法发起联系。"));
            Button(panel, "JubaoPurchase", "查看可交易商品", 690, 670, 440, 62,
                () => CommerceRequested?.Invoke(), true);
            BoundNavigation(_modal);
            // 放在弹窗建好之后:描述写回会触发重绘本弹窗,State 按条去重,不会形成请求循环。
            if (State.ServerPaged) State.RequestDetail(item.Id);
        }

        private string ServerStatusText() => string.IsNullOrEmpty(State.Status) ? "收藏已同步至账号" : State.Status;
        // 建弹窗与 UpdateSelection 共用同一取值:本地模式结果恒定,重复赋值不改变显示。
        private string ModalStatusText() => State.IsDemo ? "离线演示商品，仅用于界面预览" : State.ServerPaged ? ServerStatusText() : "收藏仅在本次登录保留";

        private void ShowNotice(string title, string message)
        {
            var panel = OpenModal(title, 1000, 480);
            Text(panel, message, 90, 143, 820, 181, 34, Muted, true, TextAlignmentOptions.Center);
            Button(panel, "JubaoNoticeConfirm", "知道了", 350, 369, 300, 67, CloseModal, true);
            BoundNavigation(_modal);
        }

        private RectTransform OpenModal(string title, float width, float height)
        {
            CloseModal();
            _modalReturnFocus = EventSystem.current?.currentSelectedGameObject;
            _frameInput.interactable = false; _frameInput.blocksRaycasts = false;
            _modal = QdaoUguiFactory.CreateStretch("JubaoModal", _root, Vector4.zero);
            var shade = _modal.gameObject.AddComponent<Image>();
            shade.color = new Color(.015f, .055f, .04f, .66f); shade.raycastTarget = true;
            var panel = QdaoUguiFactory.CreateCenteredRect("JubaoModalPanel", _modal, width, height);
            Art(panel, "main_frame", 0, 0, width, height);
            Text(panel, title, 74, 32, width - 190, 64, 40);
            var close = Button(panel, "JubaoModalClose", "", width - 101, 28, 68, 68, CloseModal, key: "close");
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(close.gameObject);
            return panel;
        }

        private void CloseModal()
        {
            if (_modal == null) return;
            _modal.gameObject.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(_modal.gameObject);
            else UnityEngine.Object.DestroyImmediate(_modal.gameObject);
            _modal = null; _modalFavorite = null; _detailSnapshot = null; _modalRemainingText = null; _modalStatusText = null;
            _frameInput.interactable = true; _frameInput.blocksRaycasts = true;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_modalReturnFocus != null && _modalReturnFocus.activeInHierarchy ? _modalReturnFocus : null);
            _modalReturnFocus = null;
            BoundNavigation(_frame);
        }

        private void ConstrainFocus()
        {
            if (EventSystem.current == null) return;
            var scope = ModalVisible ? _modal : _frame;
            var current = EventSystem.current.currentSelectedGameObject;
            if (current == null || !current.transform.IsChildOf(scope))
            {
                var initial = ModalVisible ? _modal.GetComponentInChildren<Button>() : _topTabs[(int)State.Tab];
                if (initial != null) EventSystem.current.SetSelectedGameObject(initial.gameObject);
                return;
            }
            bool selectionChanged = current != _lastScopedFocus;
            _lastScopedFocus = current;
            if (!ModalVisible && selectionChanged && current.transform.IsChildOf(_categoryContent))
            {
                var rect = current.transform as RectTransform;
                if (rect == null) return;
                float top = -rect.anchoredPosition.y, bottom = top + rect.rect.height;
                float offset = _categoryContent.anchoredPosition.y;
                float height = _categoryScroll.viewport.rect.height;
                if (top < offset) offset = top;
                else if (bottom > offset + height) offset = bottom - height;
                _categoryContent.anchoredPosition = new Vector2(0, Mathf.Clamp(offset, 0, Mathf.Max(0, _categoryContent.rect.height - height)));
            }
        }

        // Scope every navigation edge, including same-frame navigation + submit, to the current layer.
        private static void BoundNavigation(RectTransform scope)
        {
            if (scope == null) return;
            var controls = new List<Selectable>();
            foreach (var control in scope.GetComponentsInChildren<Selectable>())
                if (control.gameObject.activeInHierarchy && control.IsInteractable()) controls.Add(control);
            for (int i = 0; i < controls.Count; i++)
            {
                var previous = controls[(i + controls.Count - 1) % controls.Count];
                var next = controls[(i + 1) % controls.Count];
                controls[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnLeft = previous, selectOnUp = previous, selectOnRight = next, selectOnDown = next };
            }
        }

        private static bool SameListing(JubaozhaiListing a, JubaozhaiListing b)
            => a != null && b != null && a.Id == b.Id && a.Name == b.Name && a.Category == b.Category &&
               a.Subcategory == b.Subcategory && a.School == b.School && a.Level == b.Level && a.Price == b.Price &&
               a.Summary == b.Summary && a.Details == b.Details && a.IconKey == b.IconKey &&
               a.ExpiresAtUnixSeconds == b.ExpiresAtUnixSeconds && a.Tab == b.Tab && a.Section == b.Section;

        private void CycleSort() => State.SetSort((JubaozhaiSort)(((int)State.Sort + 1) % 5));
        private static string SortLabel(JubaozhaiSort sort) => sort switch
        {
            JubaozhaiSort.PriceAscending => "价格从低到高", JubaozhaiSort.PriceDescending => "价格从高到低",
            JubaozhaiSort.LevelDescending => "等级从高到低", JubaozhaiSort.RemainingTimeAscending => "剩余时间从短到长", _ => "默认排序"
        };

        private static void DrawIcon(UnityEngine.Transform parent, JubaozhaiListing item, float x, float y, float size)
        {
            var icon = Load(item.IconKey);
            if (icon != null) QdaoUguiFactory.CreateImage("JubaoItemIcon", parent, x, y, size, size, icon).preserveAspect = true;
            else
            {
                Art(parent, "panel", x, y, size, size);
                Text(parent, JubaozhaiCatalog.CategoryName(item.Category), x + 4, y + 4, size - 8, size - 8,
                    size * .24f, Muted, alignment: TextAlignmentOptions.Center);
            }
            bool equipment = item.Category == JubaozhaiCategory.Weapon || item.Category == JubaozhaiCategory.Armor ||
                             item.Category == JubaozhaiCategory.Set;
            if (item.Category == JubaozhaiCategory.Character || item.Category == JubaozhaiCategory.Pet || equipment)
            {
                // The source badges were removed during slicing; native fields occupy the same exact circles.
                float levelCenterX = equipment ? .144f : .846f;
                RoundBadge(parent, "JubaoLevelBadge", item.Level.ToString(),
                    x + size * levelCenterX, y + size * .644f, size * .38f, Ink, Cream, size * .185f);
                if (item.Category == JubaozhaiCategory.Pet || equipment)
                {
                    string category = item.Category switch
                    {
                        JubaozhaiCategory.Pet => "宠", JubaozhaiCategory.Weapon => "武",
                        JubaozhaiCategory.Armor => "防", _ => "套"
                    };
                    RoundBadge(parent, "JubaoCategoryBadge", category,
                        x + size * (equipment ? .942f : .115f), y + size * .644f,
                        size * (equipment ? .38f : .33f), Gold, Cream, size * .20f);
                }
            }
            else
            {
                float badgeW = size * .46f, badgeH = size * .26f;
                Art(parent, "button_secondary", x + size - badgeW, y + size - badgeH, badgeW, badgeH);
                Text(parent, item.Level.ToString(), x + size - badgeW, y + size - badgeH, badgeW, badgeH,
                    size * .185f, Ink, alignment: TextAlignmentOptions.Center);
            }
        }

        private static void RoundBadge(UnityEngine.Transform parent, string name, string value,
            float centerX, float centerY, float diameter, Color fill, Color textColor, float fontSize)
        {
            var circle = Battle.BattleArtCatalog.CircleSprite;
            float x = centerX - diameter * .5f, y = centerY - diameter * .5f;
            var border = QdaoUguiFactory.CreateImage(name, parent, x, y, diameter, diameter, circle);
            border.color = Gold;
            float inset = Mathf.Max(2, diameter * .065f);
            var face = QdaoUguiFactory.CreateImage("Face", border.transform, inset, inset,
                diameter - inset * 2, diameter - inset * 2, circle);
            face.color = fill;
            Text(border.transform, value, inset, inset, diameter - inset * 2, diameter - inset * 2,
                fontSize, textColor, alignment: TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
        }

        private static ScrollRect CreateScroll(UnityEngine.Transform parent, string name, float x, float y, float w, float h,
            out RectTransform content)
        {
            var outer = QdaoUguiFactory.CreateRect(name, parent, x, y, w, h);
            var hit = outer.gameObject.AddComponent<Image>();
            hit.color = new Color(1, 1, 1, .001f); hit.raycastTarget = true;
            var scroll = outer.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 38;
            var viewport = QdaoUguiFactory.CreateRect("Viewport", outer, 0, 0, w - 18, h);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = QdaoUguiFactory.CreateRect("Content", viewport, 0, 0, w - 22, h);
            scroll.viewport = viewport; scroll.content = content;
            var track = QdaoUguiFactory.CreateRect("Scrollbar", outer, w - 12, 0, 10, h);
            var background = track.gameObject.AddComponent<Image>();
            background.color = new Color(.32f, .40f, .25f, .18f);
            var bar = track.gameObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            var handle = QdaoUguiFactory.CreateStretch("Handle", track, Vector4.zero);
            var handleImage = handle.gameObject.AddComponent<Image>(); handleImage.color = Gold;
            bar.targetGraphic = handleImage; bar.handleRect = handle;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll;
        }
    }
}






