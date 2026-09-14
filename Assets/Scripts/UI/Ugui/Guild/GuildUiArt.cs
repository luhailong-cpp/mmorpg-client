using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Guild
{
    /// <summary>帮会设计包的独立皮肤与原生文字，不改变其他窗口的共享美术。</summary>
    internal static class GuildUiArt
    {
        public const string Root = "UI/Ugui/GuildV2/";
        public static readonly Color Ink = QdaoUguiTheme.Html("#294F41");
        public static readonly Color Muted = QdaoUguiTheme.Html("#77664D");
        public static readonly Color Cream = QdaoUguiTheme.Html("#FFF3D6");
        public static readonly Color Gold = QdaoUguiTheme.Html("#AA803E");
        public static readonly Color Rule = new Color(.66f, .56f, .36f, .42f);

        public static Sprite Load(string key) => QdaoUguiTheme.RequireSprite(Root + key);

        public static Image Art(UnityEngine.Transform parent, string key, float x, float y, float w, float h, bool aspect = false)
        {
            var image = QdaoUguiFactory.CreateImage(key, parent, x, y, w, h, Load(key));
            image.type = !aspect && image.sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = aspect;
            return image;
        }

        public static Image Line(UnityEngine.Transform parent, string name, float x, float y, float w, float h = 1.5f)
        {
            var image = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, null);
            image.color = Rule;
            return image;
        }

        public static TextMeshProUGUI Text(UnityEngine.Transform parent, string value, float x, float y, float w, float h,
            float size = 30, Color? color = null, bool wrap = false, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var label = QdaoUguiFactory.CreateText("Label", parent, x, y, w, h, value, size, color ?? Ink, alignment);
            label.richText = false;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            if (wrap) label.alignment = TextAlignmentOptions.TopLeft;
            return label;
        }

        public static Button Button(UnityEngine.Transform parent, string label, float x, float y, float w, float h,
            Action click, bool primary = false, bool enabled = true, string key = null, float fontSize = 30)
        {
            key ??= primary ? "button_primary" : "button_secondary";
            var button = QdaoUguiFactory.CreateArtButton(label, parent, x, y, w, h, Load(key), out var image);
            image.type = image.sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = key == "close_button";
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var colors = button.colors;
            colors.highlightedColor = new Color(1, .95f, .80f);
            colors.selectedColor = new Color(1, .94f, .77f);
            button.colors = colors;
            button.interactable = enabled;
            if (click != null) button.onClick.AddListener(() => click());
            Text(button.transform, label, 18, 0, w - 36, h, fontSize,
                primary ? Cream : Ink, alignment: TextAlignmentOptions.Center);
            return button;
        }

        public static void Clear(UnityEngine.Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; --i)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }
        }
    }
}
