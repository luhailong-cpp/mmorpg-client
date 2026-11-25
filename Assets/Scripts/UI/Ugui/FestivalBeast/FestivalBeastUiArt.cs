using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.FestivalBeast
{
    /// <summary>
    /// 瑞兽迎春 uGUI 绘制辅助工具，全部素材取自 Resources/UI/Ugui/FestivalBeastV1/（19 张 manifest 切片）。
    /// 严格遵守：sliced 九宫格、horizontal 仅横向伸缩并保持高度、progress_fill 放于 RectMask2D 轨道内、
    /// 按钮复用 Selectable Color Tint 四态与 ≥52px 触控高度。
    /// </summary>
    internal static class FestivalBeastUiArt
    {
        private const string ResourcePrefix = "UI/Ugui/FestivalBeastV1/";
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        internal static readonly Color Ink = QdaoUguiTheme.Html("#234438");
        internal static readonly Color DeepJade = QdaoUguiTheme.Html("#164638");
        internal static readonly Color Muted = QdaoUguiTheme.Html("#627668");
        internal static readonly Color Ivory = QdaoUguiTheme.Html("#FAF4E6");
        internal static readonly Color WarmGold = QdaoUguiTheme.Html("#C49446");
        internal static readonly Color Cinnabar = QdaoUguiTheme.Html("#A63A2B");
        internal static readonly Color WarnAmber = QdaoUguiTheme.Html("#9A5B1A");

        internal static Sprite Load(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var sprite = Resources.Load<Sprite>(ResourcePrefix + key);
            Cache[key] = sprite;
            return sprite;
        }

        internal static RectTransform Rect(string name, UnityEngine.Transform parent, float x, float y, float w, float h)
            => QdaoUguiFactory.CreateRect(name, parent, x, y, w, h);

        internal static Image SlicedImage(string name, UnityEngine.Transform parent, float x, float y, float w, float h,
            string spriteKey, Color fallback, bool raycast = false)
        {
            var sprite = Load(spriteKey);
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, sprite, raycast);
            img.color = sprite != null ? Color.white : fallback;
            img.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            return img;
        }

        internal static Image SimpleImage(string name, UnityEngine.Transform parent, float x, float y, float w, float h,
            string spriteKey, Color fallback, bool preserveAspect = true, bool raycast = false)
        {
            var sprite = Load(spriteKey);
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, sprite, raycast);
            img.color = sprite != null ? Color.white : fallback;
            img.type = Image.Type.Simple;
            img.preserveAspect = sprite != null && preserveAspect;
            return img;
        }

        internal static TMP_Text Text(string name, UnityEngine.Transform parent, float x, float y, float w, float h,
            string value, float fontSize, Color color, bool bold = false, bool wrap = false,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, bool autoSize = false)
        {
            var label = QdaoUguiFactory.CreateText(name, parent, x, y, w, h, value ?? string.Empty, fontSize, color, alignment);
            if (bold) label.fontStyle = FontStyles.Bold;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            if (autoSize)
            {
                label.enableAutoSizing = true;
                label.fontSizeMin = Mathf.Max(16f, fontSize - 10f);
                label.fontSizeMax = fontSize;
            }
            return label;
        }

        /// <summary>主次按钮：使用 horizontal 九宫格底图（只横向加宽、保持设计高度）与 Selectable Color Tint 四态。</summary>
        internal static Button Button(string name, UnityEngine.Transform parent, float x, float y, float w, float h,
            string label, Action onClick, bool enabled = true, bool primary = true, float fontSize = 28f)
        {
            float safeHeight = Mathf.Max(52f, h);
            var sprite = Load(primary ? "button_primary" : "button_secondary");
            var button = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, safeHeight, sprite, out var art);
            art.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            art.color = sprite != null ? Color.white : (primary ? DeepJade : Ivory);
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(1.04f, 1.04f, 0.98f, 1f),
                pressedColor = new Color(0.86f, 0.86f, 0.82f, 1f),
                selectedColor = new Color(1.02f, 1.02f, 0.95f, 1f),
                disabledColor = new Color(0.68f, 0.68f, 0.68f, 0.78f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };
            button.interactable = enabled;
            if (onClick != null) button.onClick.AddListener(() => onClick());

            var text = Text(name + "Label", button.transform, 14f, 2f, w - 28f, safeHeight - 4f,
                label, fontSize, primary ? Ivory : Ink, true, false, TextAlignmentOptions.Center, autoSize: true);
            if (!enabled)
            {
                var c = text.color;
                text.color = new Color(c.r, c.g, c.b, 0.78f);
            }
            return button;
        }

        internal static Button CloseButton(UnityEngine.Transform parent, float x, float y, Action onClick)
        {
            var sprite = Load("button_close");
            var button = QdaoUguiFactory.CreateArtButton("CloseButton", parent, x, y, 76f, 76f, sprite, out var art);
            art.type = Image.Type.Simple;
            art.preserveAspect = true;
            if (sprite == null)
            {
                art.color = DeepJade;
                Text("CloseGlyph", button.transform, 0f, 0f, 76f, 76f, "×", 40f, Ivory, true, false, TextAlignmentOptions.Center);
            }
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        /// <summary>在 RectMask2D 裁切轨道内绘制不透明绿色 progress_fill，严防溢出金边。</summary>
        internal static void ProgressBar(UnityEngine.Transform parent, float x, float y, float w, float h, float ratio)
        {
            SimpleImage("ProgressTrack", parent, x, y, w, h, "progress_track", new Color(0.84f, 0.82f, 0.72f, 1f), preserveAspect: false);
            float padX = Mathf.Max(8f, w * 0.03f);
            float padY = Mathf.Max(5f, h * 0.18f);
            float innerW = Mathf.Max(0f, w - padX * 2f);
            float innerH = Mathf.Max(4f, h - padY * 2f);
            var maskRect = Rect("ProgressFillMask", parent, x + padX, y + padY, innerW * Mathf.Clamp01(ratio), innerH);
            maskRect.gameObject.AddComponent<RectMask2D>();
            if (ratio > 0f)
            {
                SimpleImage("ProgressFill", maskRect, 0f, 0f, innerW, innerH, "progress_fill", DeepJade, preserveAspect: false);
            }
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
    }
}
