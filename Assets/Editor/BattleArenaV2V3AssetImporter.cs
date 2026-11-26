#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MmorpgClient.EditorTools
{
    /// <summary>仅处理本包清单中的美术资源，不修改业务脚本、协议或既有 GUID。</summary>
    public static class BattleArenaV2V3AssetImporter
    {
        private const string ResourceRoot = "Assets/Resources/UI/Ugui/BattleArenaV2V3/";
        private const string ManifestPath = ResourceRoot + "import-settings.json";
        private static readonly string[] Platforms = { "Standalone", "Android", "iPhone", "WebGL" };

        [Serializable]
        private sealed class ImportManifest
        {
            public string schema;
            public SpriteEntry[] sprites;
        }

        [Serializable]
        private sealed class SpriteEntry
        {
            public string file;
            public int width;
            public int height;
            public string sha256;
            public Border border;
        }

        [Serializable]
        private sealed class Border
        {
            public int left;
            public int bottom;
            public int right;
            public int top;
        }

        [MenuItem("Tools/道家UI/论剑 V2+V3/核验并应用导入设置")]
        public static void ApplyImportSettings()
        {
            try
            {
                ImportManifest manifest = ReadAndValidateManifest();
                // 全量预检完成后再触碰导入设置，避免对缺失或已变更的载荷半途操作。
                foreach (SpriteEntry entry in manifest.sprites)
                {
                    string path = ResourceRoot + entry.file;
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null)
                        throw new InvalidOperationException("Texture importer is unavailable: " + path);

                    int maxSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(entry.width, entry.height)), 32, 8192);
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spritePixelsPerUnit = 100f;
                    importer.spritePivot = new Vector2(0.5f, 0.5f);
                    importer.spriteBorder = entry.border == null
                        ? Vector4.zero
                        : new Vector4(entry.border.left, entry.border.bottom, entry.border.right, entry.border.top);
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.alphaIsTransparency = true;
                    importer.sRGBTexture = true;
                    importer.mipmapEnabled = false;
                    importer.isReadable = false;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.maxTextureSize = maxSize;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.crunchedCompression = false;

                    var settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    settings.spriteAlignment = (int)SpriteAlignment.Center;
                    importer.SetTextureSettings(settings);

                    foreach (string platformName in Platforms)
                    {
                        TextureImporterPlatformSettings platform = importer.GetPlatformTextureSettings(platformName);
                        platform.name = platformName;
                        platform.overridden = true;
                        platform.maxTextureSize = maxSize;
                        platform.format = TextureImporterFormat.RGBA32;
                        platform.textureCompression = TextureImporterCompression.Uncompressed;
                        platform.crunchedCompression = false;
                        importer.SetPlatformTextureSettings(platform);
                    }
                    importer.SaveAndReimport();

                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (sprite == null || sprite.texture.width != entry.width || sprite.texture.height != entry.height)
                        throw new InvalidOperationException("Imported texture dimensions differ from the manifest: " + path);
                }
                Debug.Log("[BattleArenaV2V3] Import settings verified; spriteCount=" + manifest.sprites.Length
                    + ". Runtime integration and device validation remain separate steps.");
            }
            catch (Exception exception)
            {
                Debug.LogError("[BattleArenaV2V3] Import failed: " + exception.Message);
                throw;
            }
        }

        private static ImportManifest ReadAndValidateManifest()
        {
            if (!File.Exists(ManifestPath))
                throw new FileNotFoundException("Import manifest is missing.", ManifestPath);
            ImportManifest manifest = JsonUtility.FromJson<ImportManifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.schema != "battle-arena-v2v3-import-v1"
                || manifest.sprites == null || manifest.sprites.Length == 0)
                throw new InvalidDataException("Import manifest schema or sprite list is invalid.");

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SpriteEntry entry in manifest.sprites)
            {
                if (entry == null || string.IsNullOrEmpty(entry.file) || Path.IsPathRooted(entry.file)
                    || entry.file.Contains("..") || entry.file.Contains("\\") || entry.file.Contains(":")
                    || !entry.file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || !names.Add(entry.file))
                    throw new InvalidDataException("Import manifest contains an invalid or duplicate sprite path.");
                string path = ResourceRoot + entry.file;
                if (!File.Exists(path)) throw new FileNotFoundException("Sprite is missing.", path);
                if (entry.width < 1 || entry.height < 1 || entry.width > 8192 || entry.height > 8192)
                    throw new InvalidDataException("Sprite dimensions are invalid: " + path);
                using (FileStream stream = File.OpenRead(path))
                {
                    var header = new byte[24];
                    if (stream.Read(header, 0, header.Length) != header.Length
                        || header[0] != 137 || header[1] != 80 || header[2] != 78 || header[3] != 71
                        || ReadBigEndianInt(header, 16) != entry.width || ReadBigEndianInt(header, 20) != entry.height)
                        throw new InvalidDataException("PNG source dimensions differ from the manifest: " + path);
                }
                using (SHA256 hash = SHA256.Create())
                using (FileStream stream = File.OpenRead(path))
                {
                    var actual = new StringBuilder(64);
                    foreach (byte value in hash.ComputeHash(stream)) actual.Append(value.ToString("x2"));
                    if (!string.Equals(actual.ToString(), entry.sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Sprite SHA256 differs from the manifest: " + path);
                }
                Border border = entry.border;
                if (border != null && (border.left < 0 || border.bottom < 0 || border.right < 0 || border.top < 0
                    || border.left + border.right >= entry.width || border.bottom + border.top >= entry.height))
                    throw new InvalidDataException("Sprite border is outside its source bounds: " + path);
            }
            return manifest;
        }

        private static int ReadBigEndianInt(byte[] data, int offset)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }
    }
}
#endif
