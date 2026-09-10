using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui
{
    /// <summary>Native controls backed by the accepted GPT Image artwork.</summary>
    public static class QdaoRefreshArt
    {
        public const string Root = "UI/Ugui/RefreshV8/";
        public static readonly Color Jade = QdaoUguiTheme.Html("#176C5F");
        public static readonly Color Ink = QdaoUguiTheme.Html("#344D43");
        public static readonly Color Ivory = QdaoUguiTheme.Html("#FFF7DE");

        public static Sprite Load(string name) => QdaoUguiTheme.RequireSprite(Root + name);

        public static void Skin(Image image, string asset)
        {
            if (image == null) return;
            var sprite = Load(asset);
            image.sprite = sprite;
            image.color = Color.white;
            image.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = image.type == Image.Type.Simple;
            // Keep the authored borders proportional when a control is displayed
            // smaller than its source canvas; only its center stretches.
            float minimum = asset == "main_frame" || asset == "content_panel" ? 1f : 0.01f;
            image.pixelsPerUnitMultiplier = Mathf.Max(minimum,
                sprite.rect.height / Mathf.Max(1f, image.rectTransform.rect.height));
        }

        public static Image Panel(string name, UnityEngine.Transform parent, float x, float y,
            float width, float height, string asset = "content_panel")
        {
            var image = QdaoUguiFactory.CreateImage(name, parent, x, y, width, height, Load(asset));
            Skin(image, asset);
            return image;
        }

        public static Button Button(string name, UnityEngine.Transform parent, float x, float y,
            float width, float height, string asset, out Image image)
        {
            var button = QdaoUguiFactory.CreateArtButton(name, parent, x, y, width, height, Load(asset), out image);
            Skin(image, asset);
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 0.97f, 0.84f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.12f;
            button.colors = colors;
            return button;
        }
    }
}
