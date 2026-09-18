using System;
using UnityEditor;
using UnityEngine;

namespace MmorpgClient.Editor.Tianyong
{
    /// <summary>Preserve every delivered texel; platform compression requires separate visual acceptance.</summary>
    public sealed class CityTile4KImporter : AssetPostprocessor
    {
        public const string TileRoot = "Assets/Resources/World/CityTiles4K/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(TileRoot, StringComparison.OrdinalIgnoreCase) &&
                !assetPath.StartsWith("Assets/Editor/CityTiles4KReview/Tiles/", StringComparison.OrdinalIgnoreCase)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.maxTextureSize = 4096;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            // The current delivery contract has no baked mip gutters. Independent mip chains
            // would mix unrelated edge pixels; add gutters and verify seams before enabling them.
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            var defaults = importer.GetDefaultPlatformTextureSettings();
            defaults.maxTextureSize = 4096;
            defaults.format = TextureImporterFormat.RGB24;
            defaults.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(defaults);
            // A copied .meta must not silently downscale the new 4K delivery.
            foreach (var platform in new[] { "Standalone", "Android", "iPhone", "WebGL" })
            {
                var settings = importer.GetPlatformTextureSettings(platform);
                settings.overridden = false;
                importer.SetPlatformTextureSettings(settings);
            }
        }
    }
}

