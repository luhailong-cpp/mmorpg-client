using System;
using MmorpgClient.Game.Jubaozhai;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Jubaozhai.JubaozhaiArt;

namespace MmorpgClient.UI.Ugui.Jubaozhai
{
    public sealed partial class JubaozhaiWindow
    {
        private const string FidelityRoot = "UI/Ugui/JubaozhaiFidelity20261016/";
        private readonly ScrollRect _rowScroll;
        private string _rowsQuery = "";
        private static Sprite NativeSprite(string key) => QdaoUguiTheme.RequireSprite(FidelityRoot + key);
        private static Image NativeArt(UnityEngine.Transform parent, string key, float x, float y, float w, float h)
            => QdaoUguiFactory.CreateImage("Art_" + key, parent, x, y, w, h, NativeSprite(key));

        private static Button NativeButton(UnityEngine.Transform parent, string name, string label, string skin,
            float x, float y, float w, float h, Action clicked, float font = 28)
        {
            var button = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, NativeSprite(skin), out _);
            if (clicked != null) button.onClick.AddListener(() => clicked());
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var colors = button.colors; colors.selectedColor = Color.white; colors.highlightedColor = new Color(1, .97f, .90f); button.colors = colors;
            if (!string.IsNullOrEmpty(label)) Text(button.transform, label, 10, 0, w - 20, h, font, Ink, true, TextAlignmentOptions.Center);
            return button;
        }

        private static Button NativeHit(UnityEngine.Transform parent, string name, string label,
            float x, float y, float w, float h, Action clicked, float font = 28)
        {
            var button = QdaoUguiFactory.CreateHitButton(name, parent, x, y, w, h);
            if (clicked != null) button.onClick.AddListener(() => clicked());
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            if (!string.IsNullOrEmpty(label)) Text(button.transform, label, 4, 0, w - 8, h, font, Ink, alignment: TextAlignmentOptions.Center);
            return button;
        }

        private static void NativeSelect(Button button, bool selected, string normal, string active)
        {
            button.image.sprite = NativeSprite(selected ? active : normal);
            var label = button.GetComponentInChildren<TMP_Text>(); if (label != null) label.color = selected ? Cream : Ink;
        }

        private static string NativeSortLabel(JubaozhaiSort sort) => sort switch
        {
            JubaozhaiSort.PriceAscending => "商品 ↑", JubaozhaiSort.PriceDescending => "商品 ↓",
            JubaozhaiSort.LevelDescending => "等级 ↓", JubaozhaiSort.RemainingTimeAscending => "时间 ↑", _ => "商品 ↕"
        };

        private static ScrollRect NativeCategoryScroll(UnityEngine.Transform parent, string name, float x, float y,
            float width, float height, out RectTransform content)
        {
            var outer = QdaoUguiFactory.CreateRect(name, parent, x, y, width, height);
            var hit = outer.gameObject.AddComponent<Image>(); hit.color = new Color(1, 1, 1, .001f); hit.raycastTarget = true;
            outer.gameObject.AddComponent<RectMask2D>();
            content = QdaoUguiFactory.CreateRect(name + "Content", outer, 0, 0, width, height);
            var scroll = outer.gameObject.AddComponent<ScrollRect>(); scroll.viewport = outer; scroll.content = content;
            scroll.horizontal = false; scroll.vertical = true; scroll.inertia = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 43;
            return scroll;
        }

        private void PrepareNativeRows(JubaozhaiPage page)
        {
            string query = State.Category + "|" + State.Subcategory + "|" + State.Search + "|" + State.Sort + "|" + State.FavoritesOnly + "|" + State.Tab + "|" + State.Section + "|" + page.PageNumber;
            _rows.sizeDelta = new Vector2(1224, Math.Max(451, page.Items.Count * 114 - 6));
            if (_rowsQuery != query) { _rowScroll.StopMovement(); _rowScroll.verticalNormalizedPosition = 1; _rowsQuery = query; }
        }

        private void LayoutNativeStatus()
        {
            float height = Math.Max(61, _status.GetPreferredValues(_status.text, 375, float.PositiveInfinity).y + 5);
            _status.rectTransform.sizeDelta = new Vector2(375, height); _status.overflowMode = TextOverflowModes.Overflow;
            if (_status.rectTransform.parent is RectTransform content) content.sizeDelta = new Vector2(380, height);
        }

        private static void NativeSummary(UnityEngine.Transform parent, string summary, float x, float y, float w, float h, float font)
        {
            var scroll = NativeCategoryScroll(parent, "JubaoSummaryScroll", x, y, w, h, out var content);
            var label = Text(content, summary ?? "", 0, 0, w - 5, h, font, Ink, true, TextAlignmentOptions.TopLeft);
            label.overflowMode = TextOverflowModes.Overflow;
            float actual = Math.Max(h, label.GetPreferredValues(label.text, w - 5, float.PositiveInfinity).y + 5);
            label.rectTransform.sizeDelta = new Vector2(w - 5, actual); content.sizeDelta = new Vector2(w, actual);
            scroll.verticalNormalizedPosition = 1;
        }
    }
}
