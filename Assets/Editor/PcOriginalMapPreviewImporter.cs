using System;
using System.IO;
using MmorpgClient.World.CurrentMaps;
using UnityEditor;
using UnityEngine;

namespace MmorpgClient.Editor.CurrentMaps
{
    /// <summary>
    /// Enforces exact native-pixel import settings for PC original, Q-Dao, and original-map
    /// static background preview assets under PcOriginalPreview20261123/, OriginalMapPreview20261124/,
    /// and PcMapVariantReview/.
    /// Preserves RGBA32 alpha when declared/present and imports opaque maps as RGB24 Uncompressed
    /// with NPOT=None so remainder edge tiles (e.g. 1024x683) keep exact dimensions.
    /// </summary>
    public sealed class PcOriginalMapPreviewImporter : AssetPostprocessor
    {
        public const string PreviewAssetRoot = "Assets/Resources/World/PcOriginalPreview20261123/";
        public const string OriginalPreviewAssetRoot = "Assets/Resources/" + PcOriginalMapContract.OriginalPreviewRoot;
        public const string VariantReviewAssetRoot = "Assets/Resources/" + PcOriginalMapContract.VariantReviewRoot;

        public static bool IsPreviewTexturePath(string path)
        {
            if (string.IsNullOrEmpty(path) || (!path.StartsWith(PreviewAssetRoot, StringComparison.Ordinal) &&
                                               !path.StartsWith(OriginalPreviewAssetRoot, StringComparison.Ordinal) &&
                                               !path.StartsWith(VariantReviewAssetRoot, StringComparison.Ordinal)))
                return false;
            return path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ResolveOriginalPreviewHasAlpha(string assetPath, TextureImporter importer)
        {
            // Lossless source PNGs may carry a fully-opaque (alpha=255) RGBA header; respect tiles.json hasAlpha
            // so opaque original maps import cleanly as RGB24 without altering verbatim source PNG bytes.
            string dir = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir))
            {
                if (dir.EndsWith("/tiles", StringComparison.OrdinalIgnoreCase))
                    dir = Path.GetDirectoryName(dir)?.Replace('\\', '/');
                string manifestPath = string.IsNullOrEmpty(dir) ? null : dir + "/tiles.json";
                if (!string.IsNullOrEmpty(manifestPath) && File.Exists(manifestPath))
                {
                    var manifest = JsonUtility.FromJson<PcOriginalTileManifest>(File.ReadAllText(manifestPath));
                    if (manifest != null) return manifest.hasAlpha;
                }
            }
            return importer.DoesSourceTextureHaveAlpha();
        }

        private void OnPreprocessTexture()
        {
            if (!IsPreviewTexturePath(assetPath)) return;
            var importer = (TextureImporter)assetImporter;
            bool isOverview = assetPath.EndsWith("/overview.jpg", StringComparison.OrdinalIgnoreCase) ||
                              assetPath.EndsWith("/overview.jpeg", StringComparison.OrdinalIgnoreCase) ||
                              assetPath.EndsWith("/overview.png", StringComparison.OrdinalIgnoreCase);
            bool isOriginalPreview = assetPath.StartsWith(OriginalPreviewAssetRoot, StringComparison.Ordinal);
            bool hasAlpha = assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                            (isOriginalPreview
                                ? ResolveOriginalPreviewHasAlpha(assetPath, importer)
                                : importer.DoesSourceTextureHaveAlpha());
            bool review = assetPath.StartsWith(VariantReviewAssetRoot, StringComparison.Ordinal);
            int maxSize = isOverview || review ? PcOriginalMapContract.DefaultOverviewMaxEdge : PcOriginalMapContract.DefaultTilePixels;

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
