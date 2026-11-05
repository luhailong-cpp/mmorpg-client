using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui
{
    using Vector3 = UnityEngine.Vector3;
    /// <summary>
    /// Native uGUI layout for the world HUD. Layout groups own placement; proxies
    /// let feature controls retain their original session/canvas ancestors.
    /// </summary>
    public sealed class WorldHudLayout
    {
        private const float Margin = 32, Gap = 16;
        private readonly Canvas _canvas;
        private readonly CanvasScaler _scaler;
        private readonly RectTransform _safe, _header, _topRow, _secondRow, _categories;
        private readonly RectTransform _body, _actions, _panel, _tiles;
        private readonly GridLayoutGroup _grid;
        private readonly ScrollRect _scroll;
        private readonly Dictionary<WorldHudEntry, RectTransform> _proxies = new();
        private static readonly Vector3[] Corners = new Vector3[4];
        private Rect _lastPixels, _lastSafe;
        private int _lastTileCount = -1;
        private bool _dirty = true, _lastLocation, _lastCategory;
        public RectTransform Location { get; }
        public RectTransform Side { get; }
        public RectTransform TileViewport { get; }
        public ScrollRect TileScroll => _scroll;
        public float PanelWidth { get; private set; }
        public int Columns { get; private set; } = 2;

        public WorldHudLayout(RectTransform root, RectTransform panel, Button[] tabs, GameObject locationPaper)
        {
            _canvas = root.GetComponentInParent<Canvas>();
            _scaler = _canvas.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            _safe = QdaoUguiFactory.CreateStretch("HudSafeArea", root, Vector4.zero);
            _header = QdaoUguiFactory.CreateRect("HudHeader", _safe, Margin, Margin, 1, 96);
            var headerGroup = _header.gameObject.AddComponent<VerticalLayoutGroup>();
            Configure(headerGroup);
            headerGroup.spacing = Gap;
            _topRow = Row("HudTopRow", _header, 96);
            Location = Element("LocationLayout", _topRow, 414, 96);
            var locationGroup = Location.gameObject.AddComponent<HorizontalLayoutGroup>();
            Configure(locationGroup);
            locationGroup.padding = new RectOffset(4, 4, 4, 4);
            locationGroup.spacing = 2;
            locationPaper.transform.SetParent(Location, false);
            var paperRect = (RectTransform)locationPaper.transform;
            paperRect.anchorMin = Vector2.zero;
            paperRect.anchorMax = Vector2.one;
            paperRect.offsetMin = paperRect.offsetMax = Vector2.zero;
            locationPaper.AddComponent<LayoutElement>().ignoreLayout = true;
            _proxies[WorldHudEntry.Map] = Element("MapLayout", Location, 246, 88);
            _proxies[WorldHudEntry.Channel] = Element("ChannelLayout", Location, 156, 88);
            var spacer = Element("HeaderFlexibleSpace", _topRow, 0, 0);
            spacer.GetComponent<LayoutElement>().flexibleWidth = 1;
            _proxies[WorldHudEntry.MysteryMerchant] = Element("MerchantLayout", _topRow, 520, 81.6f);
            _categories = Element("CategoryTabsLayout", _topRow, 608, 88);
            var categoryGroup = _categories.gameObject.AddComponent<HorizontalLayoutGroup>();
            Configure(categoryGroup);
            categoryGroup.spacing = 8;
            foreach (var tab in tabs)
            {
                tab.transform.SetParent(_categories, false);
                Size((RectTransform)tab.transform, 146, 88);
            }
            _secondRow = Row("HudSecondRow", _header, 88);
            _secondRow.gameObject.SetActive(false);
            _body = QdaoUguiFactory.CreateRect("HudBody", _safe, Margin, 144, 1, 1);
            _actions = QdaoUguiFactory.CreateRect("QuickActionsLayout", _body, 0, 0, 336, 1);
            var actionsGroup = _actions.gameObject.AddComponent<VerticalLayoutGroup>();
            Configure(actionsGroup);
            actionsGroup.spacing = Gap;
            foreach (var id in new[] { WorldHudEntry.Battle, WorldHudEntry.Spectate, WorldHudEntry.Role, WorldHudEntry.TeamFollow })
                _proxies[id] = Element(id + "Layout", _actions, id == WorldHudEntry.TeamFollow ? 210 : 336, 81.6f);
            var bottomSpace = Element("ActionsFlexibleSpace", _actions, 0, Gap);
            bottomSpace.GetComponent<LayoutElement>().flexibleHeight = 1;
            _proxies[WorldHudEntry.Bag] = Element("BagLayout", _actions, 204, 160);
            Side = QdaoUguiFactory.CreateRect("SideHudLayout", _body, 0, 0, 440, 660);
            _panel = panel;
            _panel.SetParent(_body, false);
            TileViewport = QdaoUguiFactory.CreateRect("CategoryViewport", panel, 36, 122, 628, 240);
            var hit = TileViewport.gameObject.AddComponent<Image>();
            hit.color = new Color(1, 1, 1, .001f);
            TileViewport.gameObject.AddComponent<RectMask2D>();
            _tiles = QdaoUguiFactory.CreateRect("CategoryGrid", TileViewport, 0, 0, 628, 240);
            _grid = _tiles.gameObject.AddComponent<GridLayoutGroup>();
            _grid.cellSize = new Vector2(306, 108);
            _grid.spacing = new Vector2(16, 12);
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = 2;
            var fitter = _tiles.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = TileViewport.gameObject.AddComponent<ScrollRect>();
            _scroll = scroll;
            scroll.viewport = TileViewport;
            scroll.content = _tiles;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36;
            AddScrollbar(scroll);
            foreach (WorldHudEntry id in System.Enum.GetValues(typeof(WorldHudEntry)))
                if (!_proxies.ContainsKey(id)) Entry(id, false);
            foreach (var proxy in _proxies.Values) proxy.gameObject.SetActive(false);
        }

        public void ResetScroll()
        {
            _scroll.velocity = Vector2.zero;
            _scroll.verticalNormalizedPosition = 1;
            _tiles.anchoredPosition = Vector2.zero;
        }

        public RectTransform Entry(WorldHudEntry id, bool shown)
        {
            if (!_proxies.TryGetValue(id, out var proxy))
            {
                proxy = Element(id + "Layout", _tiles, 306, 108);
                _proxies.Add(id, proxy);
            }
            if (proxy.gameObject.activeSelf != shown)
            {
                proxy.gameObject.SetActive(shown);
                _dirty = true;
            }
            return proxy;
        }

        public void Rebuild(int tileCount, bool locationAvailable, bool categoryOpen)
        {
            // pixelRect follows the current window or capture target. Screen.safeArea
            // applies to overlay players, not a camera's offscreen RenderTexture.
            var pixels = _canvas.pixelRect;
            if (pixels.width <= 0 || pixels.height <= 0) return;
            var safe = pixels;
            if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay && Screen.safeArea.width > 0)
                safe = Rect.MinMaxRect(Mathf.Max(pixels.xMin, Screen.safeArea.xMin),
                    Mathf.Max(pixels.yMin, Screen.safeArea.yMin), Mathf.Min(pixels.xMax, Screen.safeArea.xMax),
                    Mathf.Min(pixels.yMax, Screen.safeArea.yMax));
            if (!_dirty && pixels == _lastPixels && safe == _lastSafe && tileCount == _lastTileCount &&
                locationAvailable == _lastLocation && categoryOpen == _lastCategory) return;
            _lastPixels = pixels; _lastSafe = safe; _lastTileCount = tileCount;
            _lastLocation = locationAvailable; _lastCategory = categoryOpen; _dirty = false;
            float scale = Mathf.Min(Mathf.Min(Mathf.Clamp(pixels.height / 1080f, .65f, 2f),
                safe.width / 1040f), safe.height / 900f);
            scale = Mathf.Max(.001f, scale);
            _scaler.scaleFactor = scale;
            _canvas.scaleFactor = scale;
            _safe.anchorMin = new Vector2((safe.xMin - pixels.xMin) / pixels.width,
                (safe.yMin - pixels.yMin) / pixels.height);
            _safe.anchorMax = new Vector2((safe.xMax - pixels.xMin) / pixels.width,
                (safe.yMax - pixels.yMin) / pixels.height);
            Canvas.ForceUpdateCanvases();
            float width = Mathf.Max(1, safe.width / scale - Margin * 2);
            float height = Mathf.Max(1, safe.height / scale - Margin * 2);
            Location.gameObject.SetActive(locationAvailable);
            bool merchant = _proxies[WorldHudEntry.MysteryMerchant].gameObject.activeSelf;
            float firstRowWidth = (locationAvailable ? 414 + Gap : 0) + (merchant ? 520 + Gap : 0) + 608;
            bool wrap = firstRowWidth > width;
            var categoryParent = wrap ? _secondRow : _topRow;
            if (_categories.parent != categoryParent) _categories.SetParent(categoryParent, false);
            _secondRow.gameObject.SetActive(wrap);
            float headerHeight = 96 + (wrap ? Gap + 88 : 0);
            Place(_header, Margin, Margin, width, headerHeight);
            Place(_body, Margin, Margin + headerHeight + Gap, width, height - headerHeight - Gap);
            float bodyHeight = height - headerHeight - Gap;
            Place(_actions, width - 336, 0, 336, bodyHeight);
            Place(Side, width - 336 - 24 - 440, 0, 440, Mathf.Min(660, bodyHeight));
            float panelSpace = width - 336 - 24;
            Columns = panelSpace >= 700 ? 2 : 1;
            PanelWidth = Columns == 2 ? 700 : 378;
            float contentHeight = Mathf.Ceil(tileCount / (float)Columns) * 120 - 12;
            float panelHeight = Mathf.Min(bodyHeight, 146 + Mathf.Max(0, contentHeight));
            Place(_panel, panelSpace - PanelWidth, 0, PanelWidth, panelHeight);
            Place(TileViewport, 36, 122, PanelWidth - 72, Mathf.Max(1, panelHeight - 146));
            Place((RectTransform)_scroll.verticalScrollbar.transform, PanelWidth - 28, 122, 12,
                Mathf.Max(1, panelHeight - 146));
            _tiles.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, PanelWidth - 72);
            _grid.constraintCount = Columns;
            _panel.gameObject.SetActive(categoryOpen);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_header);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_actions);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_tiles);
        }

        /// <summary>Copy a layout slot across independent canvases without changing ownership.</summary>
        public static void CopyBounds(RectTransform source, RectTransform target, bool copySize = true)
        {
            if (source == null || target == null || target.parent == null) return;
            source.GetWorldCorners(Corners);
            var parent = target.parent;
            var topLeft = parent.InverseTransformPoint(Corners[1]);
            var bottomRight = parent.InverseTransformPoint(Corners[3]);
            var anchor = new Vector2(0, 1);
            if (target.anchorMin != anchor) target.anchorMin = anchor;
            if (target.anchorMax != anchor) target.anchorMax = anchor;
            if (target.pivot != anchor) target.pivot = anchor;
            if (copySize && target.sizeDelta != source.rect.size) target.sizeDelta = source.rect.size;
            if (target.localPosition != topLeft) target.localPosition = topLeft;
            if (target.localRotation != Quaternion.identity) target.localRotation = Quaternion.identity;
            var scale = new Vector3((bottomRight.x - topLeft.x) / Mathf.Max(.001f, source.rect.width),
                (topLeft.y - bottomRight.y) / Mathf.Max(.001f, source.rect.height), 1);
            if (target.localScale != scale) target.localScale = scale;
        }

        private static void Configure(HorizontalOrVerticalLayoutGroup group)
        {
            group.childAlignment = TextAnchor.UpperRight;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = group.childForceExpandHeight = false;
        }

        private static RectTransform Row(string name, RectTransform parent, float height)
        {
            var rect = Element(name, parent, 0, height);
            rect.GetComponent<LayoutElement>().flexibleWidth = 1;
            var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            Configure(group);
            group.spacing = Gap;
            return rect;
        }

        private static RectTransform Element(string name, RectTransform parent, float width, float height)
        {
            var rect = QdaoUguiFactory.CreateRect(name, parent, 0, 0, width, height);
            Size(rect, width, height);
            return rect;
        }

        private static void Size(RectTransform rect, float width, float height)
        {
            var element = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            element.minWidth = element.preferredWidth = width;
            element.minHeight = element.preferredHeight = height;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void AddScrollbar(ScrollRect scroll)
        {
            var rail = QdaoUguiFactory.CreateRect("CategoryScrollbar", scroll.viewport.parent, 0, 0, 12, 1);
            var railImage = rail.gameObject.AddComponent<Image>();
            railImage.color = QdaoUguiTheme.Html("#E7D6B2");
            var handle = QdaoUguiFactory.CreateStretch("Handle", rail, Vector4.zero);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = QdaoUguiTheme.Html("#AA803E");
            var bar = rail.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle;
            bar.targetGraphic = handleImage;
            bar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }
    }
}
