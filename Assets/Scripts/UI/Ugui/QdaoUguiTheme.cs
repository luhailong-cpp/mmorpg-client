using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MmorpgClient.UI.Ugui
{
    /// <summary>
    /// Shared visual constants and resource loading for the native uGUI screen.
    /// One design unit maps to one pixel at the 2560x1080 reference resolution.
    /// </summary>
    public static class QdaoUguiTheme
    {
        public const float DesignWidth = 2560f;
        public const float DesignHeight = 1080f;

        // The screen artwork contains only immutable decoration. Interactive
        // controls, localized text and semantic status lights remain separate
        // uGUI objects in the baked prefab.
        public const string ScreenArtSpritePath = "UI/Ugui/Native/screen_art_headband";
        public const string StatusDotSpritePath = "UI/Ugui/Native/status_dot_mask";
        public const string CredentialSpritePath = "UI/Ugui/Native/credential_panel_v2";
        public const string CredentialCancelSpritePath = "UI/Ugui/Native/credential_btn_cancel";
        public const string CredentialSubmitSpritePath = "UI/Ugui/Native/credential_btn_submit";

        private const string SimKaiSdfPath = "Fonts/SimKai SDF";
        private const string SimKaiFontPath = "Fonts/SimKai";

        private static readonly Dictionary<string, Sprite> Sprites = new();
        private static TMP_FontAsset _font;

        public static readonly Color DarkBrown = Html("#3D2914");
        public static readonly Color Brown = Html("#5A4025");
        public static readonly Color MutedBrown = Html("#7C6043");
        public static readonly Color SelectedRed = Html("#9C2B1A");
        public static readonly Color Cream = Html("#FFF5D4");
        public static readonly Color StatusCream = Html("#FFF0BD");
        public static readonly Color PanelPaper = Html("#F1E6D8");
        public static readonly Color Letterbox = Html("#07181D");

        public static Sprite LoadSprite(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                return null;

            if (Sprites.TryGetValue(resourcePath, out var cached) && cached != null)
                return cached;

            var sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite == null)
                Debug.LogError($"[QdaoUgui] Missing sprite: Resources/{resourcePath}.png");
            else
                Sprites[resourcePath] = sprite;
            return sprite;
        }

        public static Sprite RequireSprite(string resourcePath)
        {
            var sprite = LoadSprite(resourcePath);
            if (sprite == null)
                throw new MissingReferenceException($"Required qdao uGUI sprite is missing: Resources/{resourcePath}.png");
            return sprite;
        }

        public static TMP_FontAsset ResolveFont()
        {
            if (_font != null)
                return _font;

            _font = Resources.Load<TMP_FontAsset>(SimKaiSdfPath);
            if (_font != null)
                return _font;

            // Runtime safety net. The editor builder creates a persistent SDF
            // asset, but a clean checkout remains usable before that menu item
            // has been run.
            var sourceFont = Resources.Load<Font>(SimKaiFontPath);
            if (sourceFont == null)
            {
                Debug.LogError($"[QdaoUgui] Missing font: Resources/{SimKaiFontPath}.ttf");
                return null;
            }

            _font = TMP_FontAsset.CreateFontAsset(sourceFont);
            _font.name = "SimKai SDF (Runtime)";
            return _font;
        }

        public static void ResetRuntimeCaches()
        {
            Sprites.Clear();
            _font = null;
        }

        public static Color Html(string value)
        {
            return ColorUtility.TryParseHtmlString(value, out var color) ? color : Color.white;
        }
    }
}
