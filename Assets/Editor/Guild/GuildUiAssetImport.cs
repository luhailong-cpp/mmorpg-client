#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

/// <summary>Imports the approved, already separated guild artwork without resampling it.</summary>
public static class GuildUiAssetImport
{
    public const string Destination = "Assets/Resources/UI/Ugui/GuildV2";

    [Serializable] public sealed class SourceManifest { public SourceAsset[] assets; }
    [Serializable] public sealed class SourceAsset
    {
        public string file, sha256;
        public int[] size, nineSliceLeftTopRightBottom;
    }
    [Serializable] public sealed class ImportedAsset
    {
        public string source, assetPath, resourceKey, sha256, guid;
        public int width, height;
        public float[] borderLeftBottomRightTop;
    }
    [Serializable] public sealed class ImportReport
    {
        public string sourcePackage = "designs/guild-ui-v2";
        public string operation = "Import approved individual PNG slices, preserve source pixels, configure native Unity sprites";
        public string unityVersion;
        public bool sourcePixelsUnchanged = true;
        public ImportedAsset[] assets;
    }

    [MenuItem("MMORPG/UI/Import approved guild sprites")]
    public static void ImportApproved()
    {
        Import(Path.GetFullPath(Path.Combine(Application.dataPath, "../../image/designs/guild-ui-v2")));
    }

    public static void Import(string sourceDirectory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Import guild artwork in Edit mode.");
        sourceDirectory = Path.GetFullPath(sourceDirectory);
        var source = JsonUtility.FromJson<SourceManifest>(File.ReadAllText(Path.Combine(sourceDirectory, "asset-manifest.json")));
        var pending = new List<SourceAsset>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in source.assets)
        {
            if (!asset.file.StartsWith("assets/", StringComparison.Ordinal)) continue;
            if (asset.file != "assets/" + Path.GetFileName(asset.file) || !asset.file.EndsWith(".png", StringComparison.Ordinal))
                throw new InvalidDataException("Unexpected guild source path: " + asset.file);
            if (!names.Add(Key(asset.file))) throw new InvalidDataException("Duplicate guild resource key.");
            if (asset.size == null || asset.size.Length != 2 || asset.size[0] <= 0 || asset.size[1] <= 0)
                throw new InvalidDataException("Missing source dimensions: " + asset.file);
            if (asset.nineSliceLeftTopRightBottom != null && asset.nineSliceLeftTopRightBottom.Length != 0 && asset.nineSliceLeftTopRightBottom.Length != 4)
                throw new InvalidDataException("Invalid guild border: " + asset.file);
            if (Hash(Path.Combine(sourceDirectory, asset.file)) != asset.sha256)
                throw new InvalidDataException("Approved source hash has changed: " + asset.file);
            pending.Add(asset);
        }
        if (pending.Count != 27) throw new InvalidDataException("Expected the approved 27 guild UI images.");

        Directory.CreateDirectory(Destination);
        var imported = new List<ImportedAsset>();
        foreach (var asset in pending)
        {
            string path = Destination + "/" + Key(asset.file) + ".png";
            string previousGuid = File.Exists(path + ".meta") ? AssetDatabase.AssetPathToGUID(path) : null;
            if (!File.Exists(path) || Hash(path) != asset.sha256)
                File.Copy(Path.Combine(sourceDirectory, asset.file), path, true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Undo.RecordObject(importer, "Import approved guild sprite");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.spritePivot = new Vector2(.5f, .5f);
            importer.spriteBorder = Border(asset);
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.sRGBTexture = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 16384;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null || sprite.rect.width != asset.size[0] || sprite.rect.height != asset.size[1] || sprite.border != Border(asset))
                throw new InvalidDataException("Imported guild sprite violates size/border contract: " + path);
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (!string.IsNullOrEmpty(previousGuid) && previousGuid != guid)
                throw new InvalidDataException("Existing guild GUID changed: " + path);
            if (Hash(path) != asset.sha256) throw new InvalidDataException("Guild pixels changed during import.");
            var b = sprite.border;
            imported.Add(new ImportedAsset { source = asset.file, assetPath = path, resourceKey = "UI/Ugui/GuildV2/" + Key(asset.file),
                sha256 = asset.sha256, guid = guid, width = asset.size[0], height = asset.size[1],
                borderLeftBottomRightTop = new[] { b.x, b.y, b.z, b.w } });
        }
        File.WriteAllText(Destination + "/manifest.json", JsonUtility.ToJson(new ImportReport {
            unityVersion = Application.unityVersion, assets = imported.ToArray() }, true));
        AssetDatabase.ImportAsset(Destination + "/manifest.json", ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("GUILD_ART_IMPORT_OK|sprites=" + imported.Count + "|sourcePixelsUnchanged=true|existingGuidsPreserved=true");
    }

    private static Vector4 Border(SourceAsset asset)
    {
        var b = asset.nineSliceLeftTopRightBottom;
        return b == null || b.Length == 0 ? Vector4.zero : new Vector4(b[0], b[3], b[2], b[1]);
    }
    private static string Key(string file)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        return name switch {
            "round_badge_taiji" => "crest", "round_badge_furnace" => "furnace", "round_badge_sword" => "sword",
            "notice_icon" => "notice", "icon-pill" => "pill", "icon-scroll" => "scroll", "icon-talisman" => "talisman", _ => name
        };
    }
    private static string Hash(string path)
    {
        using var sha = SHA256.Create();
        using var input = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
    }
}
#endif
