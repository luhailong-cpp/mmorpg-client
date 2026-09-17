using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui
{
    /// <summary>Readable body copy and controls, with calligraphic display headings.</summary>
    public static class QdaoUguiTypography
    {
        public const string BodyFontResourcePath = "Fonts/QdaoBody SDF";
        public const string BodyFontSourcePath = "Fonts/TeamNotoSansSC";
        private static TMP_FontAsset _bodyFont;

        public static TMP_FontAsset ResolveBodyFont()
        {
            if (_bodyFont != null) return _bodyFont;
            _bodyFont = Resources.Load<TMP_FontAsset>(BodyFontResourcePath);
            if (_bodyFont != null) return _bodyFont;
            var source = Resources.Load<Font>(BodyFontSourcePath);
            if (source == null)
            {
                Debug.LogError("[QdaoTypography] Missing bundled Chinese body font: " + BodyFontSourcePath);
                return QdaoUguiTheme.ResolveFont();
            }
            _bodyFont = CreateBodyFont(source);
            return _bodyFont;
        }

        public static TMP_FontAsset CreateBodyFont(Font source)
        {
            var font = TMP_FontAsset.CreateFontAsset(source, 64, 8, GlyphRenderMode.SDFAA,
                2048, 2048, AtlasPopulationMode.Dynamic, true);
            font.name = "QdaoBody SDF";
            font.isMultiAtlasTexturesEnabled = true;
            return font;
        }

        public static void ApplyDefault(TMP_Text text)
        {
            if (text.transform.parent != null && text.transform.parent.GetComponent<Button>() != null)
                ApplyButton(text);
            else if (text.fontSize >= 42f)
                ApplyHeading(text);
            else
                ApplyBody(text);
        }

        // Newly baked labels already carry their role in the font family and weight.
        // Keep those choices when rebinding after a reload; migrate older prefabs by default.
        public static void RefreshFont(TMP_Text text)
        {
            if (text.font != null && text.font.name == "QdaoBody SDF")
            {
                text.font = ResolveBodyFont();
                return;
            }
            if (text.font == QdaoUguiTheme.ResolveFont() && (text.fontStyle & FontStyles.Bold) != 0)
            {
                text.font = QdaoUguiTheme.ResolveFont();
                return;
            }
            ApplyDefault(text);
        }
        public static void ApplyBody(TMP_Text text)
        {
            text.font = ResolveBodyFont();
            text.fontStyle = FontStyles.Normal;
            text.fontWeight = FontWeight.Regular;
        }

        public static void ApplyHeading(TMP_Text text)
        {
            text.font = QdaoUguiTheme.ResolveFont();
            text.fontStyle = FontStyles.Bold;
            text.fontWeight = FontWeight.Bold;
        }

        public static void ApplyButton(TMP_Text text)
        {
            text.font = ResolveBodyFont();
            text.fontStyle = FontStyles.Bold;
            text.fontWeight = FontWeight.Bold;
        }

        public static void ResetRuntimeCache() => _bodyFont = null;
    }
}
