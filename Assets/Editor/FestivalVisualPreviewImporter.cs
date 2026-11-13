using System;
using UnityEditor;
using UnityEngine;

namespace MmorpgClient.Editor.CurrentMaps
{
    /// <summary>Import only the independently verified visual-preview PNGs at their actual 1024 size.</summary>
    public sealed class FestivalVisualPreviewImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            const string root = "Assets/Resources/World/FestivalPreview20261111/";
            if (!assetPath.StartsWith(root, StringComparison.Ordinal) ||
                !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.maxTextureSize = 1024;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            var settings = importer.GetDefaultPlatformTextureSettings();
            settings.maxTextureSize = 1024;
            settings.format = TextureImporterFormat.RGB24;
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(settings);
        }
    }
}
