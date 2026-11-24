using System;
using MmorpgClient.World.CurrentMaps;
using UnityEditor;
using UnityEngine;

namespace MmorpgClient.Editor.CurrentMaps
{
    /// <summary>
    /// Enforces exact native-pixel import settings for PC original and Q-Dao static background preview assets
    /// under Assets/Resources/World/PcOriginalPreview20261123/.
    /// Preserves RGBA32 alpha on .png maps (e.g. 15000, 27001, 70001, 28002 spring/midautumn) and keeps RGB24 on .jpg maps.
    /// </summary>
    public sealed class PcOriginalMapPreviewImporter : AssetPostprocessor
    {
        public const string PreviewAssetRoot = "Assets/Resources/World/PcOriginalPreview20261123/";
        public const string VariantReviewAssetRoot = "Assets/Resources/" + PcOriginalMapContract.VariantReviewRoot;

        public static bool IsPreviewTexturePath(string path)
        {
            if (string.IsNullOrEmpty(path) || (!path.StartsWith(PreviewAssetRoot, StringComparison.Ordinal) &&
                                               !path.StartsWith(VariantReviewAssetRoot, StringComparison.Ordinal)))
                return false;
            return path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        private void OnPreprocessTexture()
        {
            if (!IsPreviewTexturePath(assetPath)) return;
            bool isOverview = assetPath.EndsWith("/overview.jpg", StringComparison.OrdinalIgnoreCase) ||
                              assetPath.EndsWith("/overview.jpeg", StringComparison.OrdinalIgnoreCase) ||
                              assetPath.EndsWith("/overview.png", StringComparison.OrdinalIgnoreCase);
            bool hasAlpha = assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
            bool review = assetPath.StartsWith(VariantReviewAssetRoot, StringComparison.Ordinal);
            int maxSize = isOverview || review ? PcOriginalMapContract.DefaultOverviewMaxEdge : PcOriginalMapContract.DefaultTilePixels;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.maxTextureSize = maxSize;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.alphaSource = hasAlpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = hasAlpha;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;

            var defaults = importer.GetDefaultPlatformTextureSettings();
            defaults.maxTextureSize = maxSize;
            defaults.format = hasAlpha ? TextureImporterFormat.RGBA32 : TextureImporterFormat.RGB24;
            if (review) defaults.format = TextureImporterFormat.Automatic;
            defaults.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(defaults);

            foreach (var platform in new[] { "Standalone", "Android", "iPhone", "WebGL" })
            {
                var settings = importer.GetPlatformTextureSettings(platform);
                settings.overridden = false;
                importer.SetPlatformTextureSettings(settings);
            }
        }
    }
}
