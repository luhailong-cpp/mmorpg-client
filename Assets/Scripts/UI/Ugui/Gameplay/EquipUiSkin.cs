using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>Text-free GPT Image artwork; all names, values and interactions stay native uGUI.</summary>
    public static class EquipUiSkin
    {
        public const string ResourceRoot = "UI/Ugui/EquipmentV1/";
        // The painted card has a jade heading and an ivory body. Keep rarity hues readable on paper.
        public static readonly Color Paper = QdaoUguiTheme.Html("#F4EBD5");
        public static readonly Color Ink = QdaoUguiTheme.Html("#29463B");
        public static readonly Color Muted = QdaoUguiTheme.Html("#5F6855");
        public static readonly Color Jade = QdaoUguiTheme.Html("#245A48");
        public static readonly Color HeaderText = QdaoUguiTheme.Html("#FFF3D6");
        public static readonly Color HeaderMuted = QdaoUguiTheme.Html("#E4DFC0");
        public static readonly Color Blue = QdaoUguiTheme.Html("#245A92");
        public static readonly Color Pink = QdaoUguiTheme.Html("#92346C");
        public static readonly Color Gold = QdaoUguiTheme.Html("#806011");
        public static readonly Color Green = QdaoUguiTheme.Html("#286246");
        public static readonly Color Unmet = QdaoUguiTheme.Html("#A6362B");
        public static Sprite Load(string key) => Resources.Load<Sprite>(ResourceRoot + key);

        // Existing isolated fixtures use the previous dark-card palette. Normalize those known
        // colors at the presentation boundary; custom caller colors are otherwise preserved.
        public static Color PaperInk(Color color)
        {
            if (color == QdaoUguiTheme.Html("#FFF3D6")) return Ink;
            if (color == QdaoUguiTheme.Html("#5AA7FF")) return Blue;
            if (color == QdaoUguiTheme.Html("#FF7AD9")) return Pink;
            if (color == QdaoUguiTheme.Html("#FFD84A")) return Gold;
            if (color == QdaoUguiTheme.Html("#52D96A")) return Green;
            return color;
        }

        public static bool Apply(Image image, string key, Color fallback)
        {
            var sprite = Load(key);
            image.sprite = sprite;
            image.color = sprite != null ? Color.white : fallback;
            image.type = sprite != null && sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = sprite != null && sprite.border.sqrMagnitude == 0;
            image.pixelsPerUnitMultiplier = 1;
            return sprite != null;
        }

        public static Image Art(UnityEngine.Transform parent, string key, float x, float y, float w, float h,
            Color fallback, bool raycast = false)
        {
            var image = QdaoUguiFactory.CreateImage(key, parent, x, y, w, h, null, raycast);
            Apply(image, key, fallback);
            return image;
        }

        public static Button Button(UnityEngine.Transform parent, string name, string label,
            float x, float y, float w, float h, Action click, bool primary = false, bool enabled = true,
            float fontSize = 30, string artKey = null)
        {
            var button = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, null, out var plate);
            Apply(plate, artKey ?? (primary ? "button_primary" : "button_secondary"),
                primary ? Jade : Paper);
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, .94f, .75f);
            colors.selectedColor = new Color(1f, .85f, .48f);
            colors.pressedColor = new Color(.78f, .87f, .76f);
            colors.disabledColor = new Color(.67f, .67f, .62f, .65f);
            colors.fadeDuration = .10f;
            button.colors = colors;
            button.interactable = enabled;
            if (click != null) button.onClick.AddListener(() => click());
            GameplayUiArt.Text(button.transform, label, 12, 0, w - 24, h, fontSize,
                primary ? HeaderText : Ink,
                alignment: TextAlignmentOptions.Center);
            return button;
        }
    }
}
