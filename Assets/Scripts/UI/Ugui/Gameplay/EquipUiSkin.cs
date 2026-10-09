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
        public static Sprite Load(string key) => Resources.Load<Sprite>(ResourceRoot + key);

        public static bool Apply(Image image, string key, Color fallback)
        {
            var sprite = Load(key);
            image.sprite = sprite;
            image.color = sprite != null ? Color.white : fallback;
            image.type = sprite != null && sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
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
                QdaoUguiTheme.Html(primary ? "#176C5F" : "#F1E6D8"));
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, .94f, .75f);
            colors.selectedColor = new Color(1f, .85f, .48f);
            colors.pressedColor = new Color(.78f, .87f, .76f);
            button.colors = colors;
            button.interactable = enabled;
            if (click != null) button.onClick.AddListener(() => click());
            GameplayUiArt.Text(button.transform, label, 12, 0, w - 24, h, fontSize,
                primary ? QdaoUguiTheme.Cream : GameplayUiArt.Ink,
                alignment: TextAlignmentOptions.Center);
            return button;
        }
    }
}
