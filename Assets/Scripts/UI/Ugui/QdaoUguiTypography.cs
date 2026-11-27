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

        /// <summary>全量界面正文与控件统一收口为帮会原图同源 SimKai SDF 楷体；QdaoBody SDF 仅作为符号兜底挂载在主字库上。</summary>
        public static TMP_FontAsset ResolveBodyFont() => QdaoUguiTheme.ResolveFont();

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
            if (text == null) return;
            if (text.transform.parent != null && text.transform.parent.GetComponent<Button>() != null)
                ApplyButton(text);
            else if (text.fontSize >= 38f)
                ApplyHeading(text);
            else
                ApplyBody(text);
        }

        public static void RefreshFont(TMP_Text text)
        {
            if (text == null) return;
            QdaoUguiTheme.ApplyTypographyAuto(text);
        }

        public static void ApplyBody(TMP_Text text)
        {
            if (text == null) return;
            text.fontStyle = FontStyles.Normal;
            text.fontWeight = FontWeight.Regular;
            QdaoUguiTheme.ApplyTypography(text, QdaoUiTypographyRole.Body);
        }

        public static void ApplyHeading(TMP_Text text)
        {
            if (text == null) return;
            text.fontStyle = FontStyles.Normal;
            text.fontWeight = FontWeight.Regular;
            float luma = 0.299f * text.color.r + 0.587f * text.color.g + 0.114f * text.color.b;
            var role = luma >= 0.72f
                ? (text.fontSize >= 38f ? QdaoUiTypographyRole.Title : QdaoUiTypographyRole.Primary)
                : QdaoUiTypographyRole.Heading;
            QdaoUguiTheme.ApplyTypography(text, role);
        }

        public static void ApplyButton(TMP_Text text)
        {
            if (text == null) return;
            text.fontStyle = FontStyles.Normal;
            text.fontWeight = FontWeight.Regular;
            float luma = 0.299f * text.color.r + 0.587f * text.color.g + 0.114f * text.color.b;
            QdaoUguiTheme.ApplyTypography(text, luma >= 0.72f ? QdaoUiTypographyRole.Primary : QdaoUiTypographyRole.Heading);
        }

        public static void ResetRuntimeCache() => _bodyFont = null;
    }
}
