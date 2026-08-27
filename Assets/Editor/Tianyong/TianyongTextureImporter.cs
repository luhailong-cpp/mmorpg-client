using UnityEditor;
using UnityEngine;

namespace MmorpgClient.Editor.Tianyong
{
    /// <summary>Stable production import settings for repeatable world materials.</summary>
    public sealed class TianyongTextureImporter : AssetPostprocessor
    {
        private const string TextureFolder = "Assets/Resources/World/Tianyong/Textures/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(TextureFolder, System.StringComparison.OrdinalIgnoreCase)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;
            importer.anisoLevel = 4;
            importer.mipmapEnabled = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.sRGBTexture = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.crunchedCompression = false;
            importer.compressionQuality = 75;
            importer.maxTextureSize = 2048;
        }
    }
}
