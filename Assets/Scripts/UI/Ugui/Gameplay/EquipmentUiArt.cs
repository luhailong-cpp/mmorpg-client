using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 装备窗口的原始像素绘制接缝。坐标从左上角起算，整个窗口由 EquipmentUiFit 等比适配。
    /// 只加载独立图标与无字控件；使用限制见 Docs/EquipmentUI-20261117-assets.json。
    /// </summary>
    public static class EquipmentUiArt
    {
        public const float Width = 3966f;
        public const float Height = 1586f;
        public const string ResourceRoot = "UI/Ugui/Equipment20261117/";

        public static readonly Color Ink = QdaoUguiTheme.Html("#29463B");
        public static readonly Color Muted = QdaoUguiTheme.Html("#6A705C");
        public static readonly Color Jade = QdaoUguiTheme.Html("#245A48");
        public static readonly Color Cream = QdaoUguiTheme.Html("#FFF3D6");
        public static readonly Color Danger = QdaoUguiTheme.Html("#A6362B");

        // 仅供缺图时保持原有布局；图片存在时尺寸仍以 sprite.rect 为准。
        private static readonly Dictionary<string, Vector2> FixedControlSizes = new()
        {
            ["main-panel"] = new Vector2(345, 315),
            ["title-plaque"] = new Vector2(365, 170),
            ["detail-card"] = new Vector2(335, 305),
            ["comparison-card"] = new Vector2(375, 305),
            ["selection-dialog"] = new Vector2(345, 300),
            ["tab"] = new Vector2(330, 130),
            ["close-button"] = new Vector2(255, 300),
            ["equipment-slot"] = new Vector2(315, 300),
            ["empty-slot"] = new Vector2(320, 300),
            ["selected-frame"] = new Vector2(330, 300),
            ["radio-empty"] = new Vector2(210, 200),
            ["radio-selected"] = new Vector2(190, 200),
            ["checkmark"] = new Vector2(220, 190),
            ["quantity-minus"] = new Vector2(290, 190),
            ["quantity-plus"] = new Vector2(290, 190),
            ["scrollbar"] = new Vector2(120, 440),
            ["tooltip-bubble"] = new Vector2(515, 265),
            ["main-panel-nine"] = new Vector2(1774, 887),
            ["detail-card-nine"] = new Vector2(1774, 887),
        };

        public static TextMeshProUGUI Label(UnityEngine.Transform parent, string name, string text,
            float x, float y, float w, float h, float font = 44, bool wrap = false, Color? color = null)
        {
            var label = QdaoUguiFactory.CreateText(name, parent, x, y, w, h, text, font,
                color ?? Ink, wrap ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft);
            label.richText = false;
            label.enableAutoSizing = false;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            return label;
        }

        /// <summary>仅两张新版面板允许双向九宫格；目标宽须大于 380、高须大于 315。</summary>
        public static Image Panel(UnityEngine.Transform parent, string name, float x, float y,
            float w, float h, bool main = false)
        {
            var sprite = Load(ResourceRoot + "Controls/" + (main ? "main-panel-nine" : "detail-card-nine"));
            if (w <= 380 || h <= 315)
            {
                Debug.LogError($"[EquipmentUi] Panel '{name}' is smaller than its fixed nine-slice borders: {w}x{h}");
                sprite = null;
            }
            var image = Artwork(parent, name, sprite, x, y, w, h, Cream);
            if (sprite != null) Slice(image);
            return image;
        }

        /// <summary>固定控件只按原图宽高绘制；四个横条必须走专用横向绘制路径。</summary>
        public static Image Control(UnityEngine.Transform parent, string key, float x, float y)
        {
            if (key == null || !FixedControlSizes.TryGetValue(key, out var size))
                throw new ArgumentException("Control requires a fixed-size equipment control key; horizontal bars are not allowed.", nameof(key));
            var sprite = Load(ResourceRoot + "Controls/" + key);
            if (sprite != null) size = sprite.rect.size;
            var image = Artwork(parent, key, sprite, x, y, size.x, size.y, Cream);
            image.preserveAspect = true;
            return image;
        }

        /// <summary>横向三段按钮，原高 140；宽度不足原图时保持原图最小宽度。</summary>
        public static Button Button(UnityEngine.Transform parent, string name, string text, float x, float y,
            float w, Action click, bool primary = false, bool enabled = true)
        {
            var key = primary ? "primary-button" : "secondary-button";
            var sprite = Load(ResourceRoot + "Controls/" + key);
            float width = Mathf.Max(w, sprite != null ? sprite.rect.width : primary ? 355f : 350f);
            var button = QdaoUguiFactory.CreateArtButton(name, parent, x, y, width, 140, sprite, out var image);
            if (sprite != null) Slice(image);
            else image.color = primary ? Jade : Cream;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, .94f, .75f, 1f);
            colors.selectedColor = new Color(1f, .85f, .48f, 1f);
            colors.pressedColor = new Color(.78f, .87f, .76f, 1f);
            colors.disabledColor = new Color(.67f, .67f, .62f, .65f);
            colors.fadeDuration = .1f;
            button.colors = colors;
            button.interactable = enabled;
            if (click != null) button.onClick.AddListener(() => click());
            var label = Label(button.transform, "Label", text, 60, 0, width - 120, 140,
                color: primary ? Cream : Ink);
            label.alignment = TextAlignmentOptions.Center;
            return button;
        }

        public static Image Icon(UnityEngine.Transform parent, BagItemInfo item, float x, float y, float size)
        {
            string key = EquipmentScreenModel.IconKey(item);
            if (string.IsNullOrWhiteSpace(key) && item != null && !string.IsNullOrWhiteSpace(item.IconKey))
                key = item.IconKey.StartsWith("UI/", StringComparison.Ordinal)
                    ? item.IconKey : GameplayUiArt.Root + item.IconKey;
            if (string.IsNullOrWhiteSpace(key)) key = GameplayUiArt.Root + "icon_scroll";
            var image = Artwork(parent, "ItemIcon", Load(key), x, y, size, size, Cream);
            image.preserveAspect = true;
            if (item != null && SeasonEconomyUiArt.IsMaxEnhanced(item.EnhanceLevel, item.EnhanceMaxLevel))
            {
                var haloSprite = SeasonEconomyUiArt.LoadIcon(SeasonEconomyUiArt.Plus11SlotHaloIconKey);
                if (haloSprite != null)
                {
                    var halo = QdaoUguiFactory.CreateImage("Plus11SlotHalo", parent, x, y, size, size, haloSprite, false);
                    halo.preserveAspect = true;
                }
            }
            return image;
        }

        /// <summary>返回顶部原点的内容容器；调用方可在筛选后调整其高度。</summary>
        public static RectTransform Scroll(UnityEngine.Transform parent, string name,
            float x, float y, float w, float h, float contentHeight)
        {
            var viewport = QdaoUguiFactory.CreateRect(name, parent, x, y, w, h);
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = Color.white;
            hit.raycastTarget = true;
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            var content = QdaoUguiFactory.CreateRect("Content", viewport, 0, 0, w, Mathf.Max(h, contentHeight));
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 80;
            scroll.verticalNormalizedPosition = 1;
            return content;
        }

        public static void Clear(UnityEngine.Transform parent) => GameplayUiArt.Clear(parent);

        private static Image Artwork(UnityEngine.Transform parent, string name, Sprite sprite,
            float x, float y, float w, float h, Color fallback)
        {
            var image = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, sprite);
            image.color = sprite != null ? Color.white : fallback;
            return image;
        }

        private static void Slice(Image image)
        {
            image.type = Image.Type.Sliced;
            image.preserveAspect = false;
            // 无论父 Canvas 的参考 PPU 如何，边距都按本窗口的源像素工作坐标计算。
            var canvas = image.GetComponentInParent<Canvas>();
            image.pixelsPerUnitMultiplier = canvas != null && image.sprite.pixelsPerUnit > 0
                ? canvas.referencePixelsPerUnit / image.sprite.pixelsPerUnit : 1;
        }

        private static Sprite Load(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("UI/", StringComparison.Ordinal) ||
                key.Contains("..") || key.Contains(":") || key.Contains("\\") ||
                key.IndexOf("/screens/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Debug.LogError("[EquipmentUi] Missing or invalid runtime sprite key; using a plain color fallback.");
                return null;
            }
            var sprite = Resources.Load<Sprite>(key);
            if (sprite == null)
                Debug.LogError($"[EquipmentUi] Missing sprite: Resources/{key}; using a plain color fallback.");
            return sprite;
        }
    }
}
