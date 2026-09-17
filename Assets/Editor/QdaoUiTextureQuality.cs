#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MmorpgClient.UI.EditorTools
{
    /// <summary>Keep UI source pixels intact while preserving each sprite's authoring settings.</summary>
    public sealed class QdaoUiTextureQuality : AssetPostprocessor
    {
        public const string UiRoot = "Assets/Resources/UI";
        public const int MaximumTextureSize = 16384;
        public const string ReportPath = "E:/work/output/unity-ui-readability-01a0ad5a/texture-quality.csv";

        // Run after ordinary texture importers so their lower defaults cannot reduce UI quality.
        public override int GetPostprocessOrder() => 1000000;

        private void OnPreprocessTexture()
        {
            if (IsUiTexturePath(assetPath))
                Configure((TextureImporter)assetImporter);
        }

        private static bool IsUiTexturePath(string path)
            => path.StartsWith(UiRoot + "/", StringComparison.Ordinal);

        private static List<string> PlatformNames(TextureImporter importer)
        {
            var names = new List<string>();
            string defaultName = importer.GetDefaultPlatformTextureSettings().name;
            // Read Unity's serialized entries to include overrides for platforms not installed locally.
            using var serialized = new SerializedObject(importer);
            var platforms = serialized.FindProperty("m_PlatformSettings");
            if (platforms == null || !platforms.isArray)
                throw new InvalidOperationException("Cannot enumerate UI texture platform settings: " + importer.assetPath);
            for (int index = 0; index < platforms.arraySize; index++)
            {
                var name = platforms.GetArrayElementAtIndex(index).FindPropertyRelative("m_BuildTarget");
                if (name == null)
                    throw new InvalidOperationException("Cannot identify UI texture platform setting: " + importer.assetPath);
                if (!string.IsNullOrEmpty(name.stringValue) && name.stringValue != defaultName)
                    names.Add(name.stringValue);
            }
            return names.Distinct(StringComparer.Ordinal).ToList();
        }

        private static bool Configure(TextureImporter importer)
        {
            if (!IsUiTexturePath(importer.assetPath))
                throw new ArgumentException("Only UI texture importers may be configured.", nameof(importer));

            var platforms = PlatformNames(importer);
            var defaults = importer.GetDefaultPlatformTextureSettings();
            bool changed = importer.maxTextureSize != MaximumTextureSize ||
                importer.textureCompression != TextureImporterCompression.Uncompressed ||
                importer.crunchedCompression || importer.compressionQuality != 100 ||
                importer.npotScale != TextureImporterNPOTScale.None ||
                importer.mipmapEnabled || importer.streamingMipmaps ||
                defaults.maxTextureSize != MaximumTextureSize ||
                defaults.format != TextureImporterFormat.Automatic ||
                defaults.textureCompression != TextureImporterCompression.Uncompressed ||
                defaults.crunchedCompression || defaults.compressionQuality != 100 ||
                platforms.Count != 0;

            importer.maxTextureSize = MaximumTextureSize;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.compressionQuality = 100;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.streamingMipmaps = false;
            defaults.maxTextureSize = MaximumTextureSize;
            defaults.format = TextureImporterFormat.Automatic;
            defaults.textureCompression = TextureImporterCompression.Uncompressed;
            defaults.crunchedCompression = false;
            defaults.compressionQuality = 100;
            importer.SetPlatformTextureSettings(defaults);
            foreach (string platform in platforms)
                importer.ClearPlatformTextureSettings(platform);
            return changed;
        }

        [MenuItem("MMORPG/UI/Preserve full source texture resolution")]
        public static void ApplyAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Apply UI texture quality in Edit mode.");
            if (!AssetDatabase.IsValidFolder(UiRoot))
                throw new DirectoryNotFoundException(UiRoot);

            var paths = AssetDatabase.FindAssets("", new[] { UiRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(IsUiTexturePath)
                .Distinct(StringComparer.Ordinal)
                .Where(path => AssetImporter.GetAtPath(path) is TextureImporter)
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (paths.Length == 0)
                throw new InvalidOperationException("No UI source textures were found.");

            var csv = new StringBuilder("asset_path,source_width,source_height,unity_width,unity_height,max_size,texture_format,compression,dimensions_match,sprite_settings_preserved,error\n");
            var failures = new List<string>();
            int reimported = 0, verified = 0;
            foreach (string path in paths)
            {
                int sourceWidth = 0, sourceHeight = 0, actualWidth = 0, actualHeight = 0, maxSize = 0;
                string format = "", compression = "", error = "";
                bool dimensionsMatch = false, spriteSettingsPreserved = false;
                try
                {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    importer.GetSourceTextureWidthAndHeight(out sourceWidth, out sourceHeight);
                    var border = importer.spriteBorder;
                    float pixelsPerUnit = importer.spritePixelsPerUnit;
                    bool srgb = importer.sRGBTexture;
                    var spriteMode = importer.spriteImportMode;

                    if (Configure(importer))
                    {
                        importer.SaveAndReimport();
                        reimported++;
                    }
                    importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (texture == null)
                        throw new InvalidOperationException("Imported UI Texture2D is missing.");
                    actualWidth = texture.width;
                    actualHeight = texture.height;
                    maxSize = importer.maxTextureSize;
                    format = texture.format.ToString();
                    compression = importer.textureCompression.ToString();
                    dimensionsMatch = sourceWidth > 0 && sourceHeight > 0 &&
                        sourceWidth <= MaximumTextureSize && sourceHeight <= MaximumTextureSize &&
                        actualWidth == sourceWidth && actualHeight == sourceHeight;
                    spriteSettingsPreserved = importer.spriteBorder == border &&
                        importer.spritePixelsPerUnit == pixelsPerUnit && importer.sRGBTexture == srgb &&
                        importer.spriteImportMode == spriteMode;
                    if (!dimensionsMatch)
                        throw new InvalidOperationException("Source and imported dimensions differ, or the source exceeds 16384 pixels.");
                    if (!spriteSettingsPreserved)
                        throw new InvalidOperationException("Sprite border, PPU, import mode or sRGB changed during import.");
                    if (maxSize != MaximumTextureSize ||
                        importer.textureCompression != TextureImporterCompression.Uncompressed ||
                        importer.crunchedCompression || importer.mipmapEnabled || importer.streamingMipmaps ||
                        importer.npotScale != TextureImporterNPOTScale.None ||
                        importer.GetDefaultPlatformTextureSettings().format != TextureImporterFormat.Automatic ||
                        PlatformNames(importer).Any(platform => importer.GetPlatformTextureSettings(platform).overridden))
                        throw new InvalidOperationException("UI texture quality settings were overridden during import.");
                    verified++;
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    failures.Add(path + ": " + error);
                }
                csv.AppendLine(string.Join(",", new[] {
                    Csv(path), sourceWidth.ToString(CultureInfo.InvariantCulture), sourceHeight.ToString(CultureInfo.InvariantCulture),
                    actualWidth.ToString(CultureInfo.InvariantCulture), actualHeight.ToString(CultureInfo.InvariantCulture),
                    maxSize.ToString(CultureInfo.InvariantCulture), Csv(format), Csv(compression),
                    dimensionsMatch ? "true" : "false", spriteSettingsPreserved ? "true" : "false", Csv(error) }));
                if ((verified + failures.Count) % 25 == 0)
                    Debug.Log("UI_TEXTURE_QUALITY_PROGRESS|processed=" + (verified + failures.Count) + "|total=" + paths.Length);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, csv.ToString(), new UTF8Encoding(false));
            if (failures.Count != 0)
                throw new InvalidOperationException("UI texture quality verification failed for " + failures.Count +
                    "/" + paths.Length + " textures. Report: " + ReportPath + "\n" + string.Join("\n", failures.Take(5)));
            Debug.Log("UI_TEXTURE_QUALITY_OK|textures=" + paths.Length + "|verified=" + verified +
                "|reimported=" + reimported + "|maxSize=" + MaximumTextureSize + "|report=" + ReportPath);
        }

        private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}
#endif
