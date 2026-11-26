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

        /// <summary>16-cell V13 character strips and 10-cell 512px Battle/Fx strips need the 8192 import cap so cells stay exact squares.</summary>
        public static int RequiredMaxTextureSize(string path, int width, int height)
        {
            if ((path.StartsWith(CharacterFolder + "QdaoRosterV13/", System.StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith(CharacterFolder + "QdaoOriginalRosterV13/", System.StringComparison.OrdinalIgnoreCase)) &&
                path.EndsWith("/strip.png", System.StringComparison.OrdinalIgnoreCase) &&
                path.Contains("/walk/") && width == 8192 && height == 512)
                return 8192;
            if (path.StartsWith(BattleFolder + "Fx/", System.StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith("_strip.png", System.StringComparison.OrdinalIgnoreCase) &&
                width > 4096 && width <= 8192 && height > 0 && width % height == 0)
                return 8192;
            return 4096;
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // Import completion/deletion is the retry boundary for immutable
            // runtime cache keys, including the V11 fallback and standing probes.
            foreach (var group in new[] { imported, deleted, moved, movedFrom })
            foreach (var path in group)
                if (path.StartsWith(CharacterFolder + "QdaoRosterV", System.StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(CharacterFolder + "QdaoOriginalRosterV13/", System.StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(CharacterFolder + "QdaoOriginalRosterV14/", System.StringComparison.OrdinalIgnoreCase))
                {
                    MmorpgClient.World.QdaoCharacterCatalog.RefreshAppearances();
                    return;
                }
        }

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
            importer.GetSourceTextureWidthAndHeight(out var width, out var height);
            importer.maxTextureSize = RequiredMaxTextureSize(assetPath, width, height);
        }
    }
}
