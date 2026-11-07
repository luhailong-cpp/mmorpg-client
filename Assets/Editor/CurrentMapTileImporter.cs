using System;
using UnityEditor;
using UnityEngine;
using MmorpgClient.World.CurrentMaps;

namespace MmorpgClient.Editor.CurrentMaps
{
    /// <summary>只处理当前三张地图原生分片，保留已交付的每一个像素。</summary>
    public sealed class CurrentMapTileImporter : AssetPostprocessor
    {
        public static bool IsTilePath(string path)
        {
            if (IsModernTilePath(path)) return true;
            const string root = "Assets/Resources/World/CurrentMaps20261009/";
            if (path == null || !path.StartsWith(root, StringComparison.Ordinal) ||
                !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;
            var relative = path.Substring(root.Length);
            return relative.StartsWith("main/tiles/", StringComparison.Ordinal) ||
                relative.StartsWith("island/tiles/", StringComparison.Ordinal) ||
                relative.StartsWith("village/tiles/", StringComparison.Ordinal);
        }

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
            int pixels = modernLayout ? 8192 : IsModernTilePath(assetPath) ? MapDeliveryContract.RuntimeTilePixels : 4096;
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
