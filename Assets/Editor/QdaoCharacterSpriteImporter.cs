using UnityEditor;
using UnityEngine;

namespace MmorpgClient.Editor.Tianyong
{
    /// <summary>
    /// Import settings for the qdao character frame strips: keep alpha edges
    /// clean and disable mips so packed frames never bleed into each other.
    /// </summary>
    public sealed class QdaoCharacterSpriteImporter : AssetPostprocessor
    {
        private const string CharacterFolder = "Assets/Resources/World/Characters/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(CharacterFolder, System.StringComparison.OrdinalIgnoreCase)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed; // preserve crisp edges at the enlarged on-screen scale
            importer.crunchedCompression = false;
            importer.maxTextureSize = 4096; // 4096x512 HD strips
        }
    }
}
