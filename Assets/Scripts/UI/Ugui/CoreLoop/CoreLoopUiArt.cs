using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.CoreLoop
{
    /// <summary>核心玩法页面共用的玉青、米白、暖金 uGUI 绘制工具。固定装饰优先取已导入的手绘切片。</summary>
    internal static class CoreLoopUiArt
    {
        private const string ResourcePrefix = "UI/Ugui/CoreLoopV1/";
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

        internal static readonly Color Ink = QdaoUguiTheme.Html("#27483C");
        internal static readonly Color Muted = QdaoUguiTheme.Html("#65786A");
        internal static readonly Color Jade = QdaoUguiTheme.Html("#285D4D");
        internal static readonly Color DeepJade = QdaoUguiTheme.Html("#174A3D");
        internal static readonly Color Ivory = QdaoUguiTheme.Html("#F7F1E2");
        internal static readonly Color Gold = QdaoUguiTheme.Html("#BD914B");
        internal static readonly Color Warning = QdaoUguiTheme.Html("#98601E");

        internal static Sprite Load(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (Sprites.TryGetValue(key, out var cached)) return cached;
            // 主参考组队界面的宽形切片已有 Unity 九宫格边距；新作固定比例原图仍原样展示。
            string path = key switch
            {
                "frame" => "UI/Ugui/TeamV2/main_frame",
                "wide-card" => "UI/Ugui/TeamV2/application_card",
                "wide-strip" => "UI/Ugui/TeamV2/member_row",
                "wide-primary" => "UI/Ugui/TeamV2/button_primary",
                "wide-secondary" => "UI/Ugui/TeamV2/button_secondary",
                "close" => "UI/Ugui/TeamV2/close",
                _ => ResourcePrefix + key,
            };
            var sprite = Resources.Load<Sprite>(path);
            Sprites.Add(key, sprite);
            return sprite;
        }

        internal static RectTransform Rect(string name, UnityEngine.Transform parent, float x, float y, float width, float height) =>
            QdaoUguiFactory.CreateRect(name, parent, x, y, width, height);

        internal static Image Image(string name, UnityEngine.Transform parent, float x, float y, float width, float height,
            string spriteKey, Color fallback, bool raycast = false)
        {
            var sprite = FittingSprite(spriteKey, width, height);
            bool sliced = false;
            if (sprite == null && (spriteKey == "card" || spriteKey == "hud-strip"))
            {
                sprite = Load(spriteKey == "card" ? "wide-card" : "wide-strip");
                sliced = sprite != null;
            }
            var image = QdaoUguiFactory.CreateImage(name, parent, x, y, width, height,
                sprite, raycast);
            image.color = image.sprite == null ? fallback : Color.white;
            image.type = sliced ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            return image;
        }

        internal static TMP_Text Text(string name, UnityEngine.Transform parent, float x, float y, float width, float height,
            string value, float fontSize, Color color, bool bold = false, bool wrap = false,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var label = QdaoUguiFactory.CreateText(name, parent, x, y, width, height, value, fontSize, color, alignment);
            if (bold) label.fontStyle = FontStyles.Bold;
            if (wrap)
            {
                label.textWrappingMode = TextWrappingModes.Normal;
                label.overflowMode = TextOverflowModes.Truncate;
            }
            return label;
        }

        internal static Button Button(string name, UnityEngine.Transform parent, float x, float y, float width, float height,
            string label, Action onClick, bool enabled = true, bool primary = true, float fontSize = 27f,
            string controlSpriteKey = null)
        {
            Image art;
            string seasonKey = !string.IsNullOrEmpty(controlSpriteKey)
                ? controlSpriteKey
                : (primary ? "btn_primary_jade" : "btn_secondary_paper");
            var sprite = SeasonEconomyUiArt.LoadControl(seasonKey);
            bool isSeasonControl = sprite != null;
            bool sliced = false;
            if (!isSeasonControl)
            {
                sprite = FittingSprite(primary ? "button-primary" : "button-secondary", width, height);
                if (sprite == null)
                {
                    sprite = Load(primary ? "wide-primary" : "wide-secondary");
                    sliced = sprite != null;
                }
            }
            var button = QdaoUguiFactory.CreateArtButton(name, parent, x, y, width, height,
                sprite, out art);
            if (isSeasonControl)
            {
                SeasonEconomyUiArt.ApplyHorizontalSlice(art, sprite, height);
            }
            else
            {
                art.color = art.sprite == null ? (primary ? Jade : Ivory) : Color.white;
                if (sliced) art.type = UnityEngine.UI.Image.Type.Sliced;
            }
            button.interactable = enabled;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            Color textColor = isSeasonControl && seasonKey == "btn_primary_gold"
                ? SeasonEconomyUiArt.GoldButtonInk
                : isSeasonControl && seasonKey == "tab_active"
                    ? Ivory
                    : isSeasonControl && (seasonKey == "tab_normal" || seasonKey == "btn_secondary_paper")
                        ? Ink
                        : (primary ? Ivory : Ink);
            float padX = isSeasonControl ? Mathf.Min(24f, width * 0.14f) : 8f;
            var text = Text(name + "Label", button.transform, padX, 0, width - padX * 2f, height,
                label, fontSize, textColor, true, false, TextAlignmentOptions.Center);
            text.enableAutoSizing = true;
            text.fontSizeMin = Mathf.Max(15, fontSize - 9);
            text.fontSizeMax = fontSize;
            return button;
        }

        internal static void Clear(UnityEngine.Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; --i)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }

        internal static void Frame(UnityEngine.Transform parent, string title, Action close)
        {
            // CoreGameplayUiRoot 的窗口层已有整屏遮罩，面板只负责有形内容。
            Image("Frame", parent, 190, 95, 2180, 923, "frame", Ivory, true);
            Image("TitlePlaque", parent, 1061, 132, 437, 152, "title-plaque", DeepJade);
            Text("Title", parent, 1093, 164, 373, 78, title, 43, Ivory, true, false, TextAlignmentOptions.Center);
            var closeButton = QdaoUguiFactory.CreateArtButton("Close", parent, 2090, 168, 74, 74,
                Load("close"), out _);
            if (close != null) closeButton.onClick.AddListener(() => close());
        }

        internal static void Card(UnityEngine.Transform parent, string name, float x, float y, float width, float height,
            bool selected = false)
        {
            var image = Image(name, parent, x, y, width, height, "card", Ivory);
            if (image.sprite == null && selected) image.color = QdaoUguiTheme.Html("#DFEEDA");
            if (image.sprite == null)
            {
                Image(name + "GoldTop", parent, x, y, width, 3, null, Gold);
                Image(name + "GoldBottom", parent, x, y + height - 3, width, 3, null, Gold);
                Image(name + "GoldLeft", parent, x, y, 3, height, null, Gold);
                Image(name + "GoldRight", parent, x + width - 3, y, 3, height, null, Gold);
            }
            if (selected)
                Image(name + "Accent", parent, x, y, width, 7, null, Gold);
        }

        internal static void Badge(UnityEngine.Transform parent, string name, float x, float y, float width, string value,
            Color color)
        {
            Image(name + "Plate", parent, x, y, width, 42, null, new Color(0.89f, 0.86f, 0.72f, 0.8f));
            // 太极徽章为 120×120；在长标签内单独等比放置，避免整图拉成横条。
            Image(name + "Icon", parent, x + 2, y + 1, 40, 40, "badge", Color.clear);
            Text(name, parent, x + 48, y + 3, width - 54, 36, value, 21, color, true,
                false, TextAlignmentOptions.Center);
        }

        private static Sprite FittingSprite(string key, float width, float height)
        {
            var sprite = Load(key);
            if (sprite == null || width <= 0 || height <= 0) return null;
            float sourceAspect = sprite.rect.width / sprite.rect.height;
            float targetAspect = width / height;
            return Mathf.Abs(sourceAspect / targetAspect - 1f) <= 0.12f ? sprite : null;
        }
    }
}
