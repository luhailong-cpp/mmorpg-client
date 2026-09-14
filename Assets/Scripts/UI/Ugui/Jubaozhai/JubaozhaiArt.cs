using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Jubaozhai
{
    /// <summary>Native controls styled with the separate, text-free Jubaozhai slices.</summary>
    public static class JubaozhaiArt
    {
        public const string Root = "UI/Ugui/JubaozhaiV1/";
        public static readonly Color Ink = QdaoUguiTheme.Html("#294F41");
        public static readonly Color Muted = QdaoUguiTheme.Html("#77664D");
        public static readonly Color Cream = QdaoUguiTheme.Html("#FFF3D6");
        public static readonly Color Gold = QdaoUguiTheme.Html("#AA803E");
        public static readonly Color Paper = QdaoUguiTheme.Html("#F2E5CA");

        public static Sprite Load(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Contains("..") || key.Contains(":") || key.Contains("/")) return null;
            var sprite = Resources.Load<Sprite>(Root + key);
            if (sprite != null) return sprite;
            // Graceful loading while importing the skin; final resources are validated by the editor builder.
            string common = key switch
            {
                "panel" => "content_panel", "row_normal" => "content_panel",
                "row_selected" => "content_panel", "empty_guide" => "icon_scroll", _ => key
            };
            return Resources.Load<Sprite>(Gameplay.GameplayUiArt.Root + common);
        }

        public static Image Art(UnityEngine.Transform parent, string key, float x, float y, float w, float h, bool aspect = false)
        {
            var image = QdaoUguiFactory.CreateImage(key, parent, x, y, w, h, Load(key));
            image.type = !aspect && image.sprite != null && image.sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = aspect;
            image.color = image.sprite != null ? Color.white : Paper;
            return image;
        }

        public static TextMeshProUGUI Text(UnityEngine.Transform parent, string value, float x, float y, float w, float h,
            float size = 28, Color? color = null, bool wrap = false, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var text = QdaoUguiFactory.CreateText("Label", parent, x, y, w, h, value, size, color ?? Ink, alignment);
            text.richText = false;
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            return text;
        }

        public static Button Button(UnityEngine.Transform parent, string name, string label, float x, float y, float w, float h,
            Action clicked, bool primary = false, string key = null, float fontSize = 28)
        {
            key ??= primary ? "button_primary" : "button_secondary";
            var button = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, Load(key), out var image);
            image.type = image.sprite != null && image.sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            image.color = image.sprite != null ? Color.white : Paper;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, .96f, .82f);
            colors.selectedColor = new Color(1f, .94f, .74f);
            colors.disabledColor = new Color(.64f, .64f, .64f, .72f);
            button.colors = colors;
            if (clicked != null) button.onClick.AddListener(() => clicked());
            if (!string.IsNullOrEmpty(label))
                Text(button.transform, label, 16, 0, w - 32, h, fontSize,
                    primary || key == "tab_selected" ? Cream : Ink, alignment: TextAlignmentOptions.Center);
            return button;
        }

        public static void SetButtonLabel(Button button, string label)
        {
            var text = button.GetComponentInChildren<TMP_Text>();
            if (text != null) text.text = label;
        }

        public static void Select(Button button, bool selected, string normalKey = "tab_normal", string selectedKey = "tab_selected")
        {
            button.image.sprite = Load(selected ? selectedKey : normalKey);
            var text = button.GetComponentInChildren<TMP_Text>();
            if (text != null) text.color = selected ? Cream : Ink;
        }

        public static void Clear(UnityEngine.Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }
        }
    }
}

