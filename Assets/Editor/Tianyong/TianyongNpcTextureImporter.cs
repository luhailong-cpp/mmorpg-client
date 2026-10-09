using UnityEditor;
using UnityEngine;

namespace MmorpgClient.Editor.Tianyong
{
    /// <summary>Retain the delivered transparent artwork and its clean full-body edges.</summary>
    public sealed class TianyongNpcTextureImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/World/Tianyong/Npcs/Sprites/",
                    System.StringComparison.Ordinal)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.maxTextureSize = 1024;
        }
    }
}
