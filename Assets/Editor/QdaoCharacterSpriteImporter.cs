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

        /// <summary>
        /// 战斗美术(docs/design/battle-art-prompts.md §3):帧条 / 特效 / 数字字集 / buff 图标 /
        /// 九宫底图同样要求无 mip、无压缩、保留 alpha 渐变;运行时由 BattleArtCatalog 切格。
        /// </summary>
        private const string BattleFolder = "Assets/Resources/Battle/";

        private void OnPreprocessTexture()
        {
            bool isCharacter = assetPath.StartsWith(CharacterFolder, System.StringComparison.OrdinalIgnoreCase);
            bool isBattle = assetPath.StartsWith(BattleFolder, System.StringComparison.OrdinalIgnoreCase);
            if (!isCharacter && !isBattle) return;
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
