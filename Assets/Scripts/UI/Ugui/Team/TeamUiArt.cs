using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Team
{
    /// <summary>Independent slices from the approved team UI, with native text and controls.</summary>
    public static class TeamUiArt
    {
        public const string Root = "UI/Ugui/TeamV2/";
        public const string FidelityRoot = Root + "Fidelity20261016/";
        public const string PreviewPortraitPrefix = "preview-team-";
        public static readonly Color Ink = QdaoUguiTheme.Html("#304736");
        public static readonly Color Cream = QdaoUguiTheme.Html("#FFF3D6");

        public static Sprite Load(string key)
        {
            // These two existing decorations are shared; all team panels and controls use TeamV2.
            string root = key == "team_skin" || key.StartsWith("preview_portrait_",StringComparison.Ordinal) ? FidelityRoot
                : key == "lantern" || key == "close_tassel" ? Gameplay.GameplayUiArt.Root : Root;
            return QdaoUguiTheme.RequireSprite(root + key);
        }

        public static Image Art(UnityEngine.Transform parent, string key, float x, float y, float w, float h,
            bool aspect = false)
        {
            var image = QdaoUguiFactory.CreateImage(key, parent, x, y, w, h, Load(key));
            image.type = !aspect && image.sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = aspect;
            image.pixelsPerUnitMultiplier = 1;
            return image;
        }

        public static TextMeshProUGUI Text(UnityEngine.Transform parent, string value, float x, float y, float w, float h,
            float size = 30, Color? color = null, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var label = QdaoUguiFactory.CreateText("Label", parent, x, y, w, h, value, size, color ?? Ink, alignment);
            label.richText = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        public static Button Button(UnityEngine.Transform parent, string label, float x, float y, float w, float h,
            Action click, bool primary = false, bool enabled = true, string key = null, float fontSize = 30)
        {
            key ??= primary ? "button_primary" : "button_secondary";
            var button = QdaoUguiFactory.CreateArtButton(label, parent, x, y, w, h, Load(key), out var image);
            image.type = image.sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var colors = button.colors;
            colors.highlightedColor = new Color(1, .95f, .80f);
            colors.selectedColor = new Color(1, .94f, .70f);
            colors.pressedColor = new Color(.80f, .84f, .72f);
            colors.fadeDuration = .1f;
            button.colors = colors;
            button.interactable = enabled;
            if (click != null) button.onClick.AddListener(() => click());
            // Noto's line metrics need room even on compact pagination and action buttons.
            float textHeight = Mathf.Max(h, fontSize * 1.18f);
            float padding = Mathf.Min(18f, w * .1f);
            Text(button.transform, label, padding, (h - textHeight) * .5f, w - padding * 2, textHeight, fontSize,
                primary ? Cream : Ink, TextAlignmentOptions.Center);
            return button;
        }

        public static void Clear(UnityEngine.Transform parent) => Gameplay.GameplayUiArt.Clear(parent);
    }
}
