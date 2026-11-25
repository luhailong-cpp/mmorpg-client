using System;
using UnityEditor;
using UnityEngine;
using MmorpgClient.World.CurrentMaps;

namespace MmorpgClient.Editor.CurrentMaps
{
    /// <summary>处理 QdaoMaps20261102 正式地图分片与总览底图导入设置。</summary>
    public sealed class CurrentMapTileImporter : AssetPostprocessor
    {
        public static bool IsTilePath(string path) => IsModernTilePath(path);

        public static bool IsModernTilePath(string path)
        {
            const string prefix = "Assets/Resources/";
            return path != null && path.StartsWith(prefix, StringComparison.Ordinal) &&
                path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && path.Contains("/tiles/") &&
                MapDeliveryContract.IsResourcePath(path.Substring(prefix.Length, path.Length - prefix.Length - 4));
        }

        public static bool IsModernLayoutPath(string path)
        {
            const string prefix = "Assets/Resources/";
            return path != null && path.StartsWith(prefix, StringComparison.Ordinal) &&
                path.EndsWith("/layout.png", StringComparison.Ordinal) &&
                MapDeliveryContract.IsResourcePath(path.Substring(prefix.Length, path.Length - prefix.Length - 4));
        }

        private void OnPreprocessTexture()
        {
            bool modernLayout = IsModernLayoutPath(assetPath);
            if (!IsTilePath(assetPath) && !modernLayout) return;
            var importer = (TextureImporter)assetImporter;
            int pixels = modernLayout ? 8192 : MapDeliveryContract.RuntimeTilePixels;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.maxTextureSize = pixels;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            // 当前独立分片没有 mip 边缘扩展，不能由 Unity 自动生成独立 mip 后声称接缝通过。
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            var defaults = importer.GetDefaultPlatformTextureSettings();
            defaults.maxTextureSize = pixels;
            defaults.format = TextureImporterFormat.RGB24;
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
