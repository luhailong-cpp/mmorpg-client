#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

/// <summary>Imports the approved mail/social PNGs, preserving pixels and verified nine-slice contracts.</summary>
public static class MailSocialAssetImport
{
    [Serializable] public sealed class SourceManifest { public string package; public SourceAsset[] assets; }
    [Serializable] public sealed class SourceAsset { public string file, sha256, borderVerifiedAgainst; public int[] size; public float[] borderLeftBottomRightTop; }
    [Serializable] public sealed class ImportedAsset { public string source, assetPath, resourceKey, sha256, guid, borderVerifiedAgainst; public int width, height; public float[] borderLeftBottomRightTop; }
    [Serializable] public sealed class Report { public string package, unityVersion; public bool sourcePixelsUnchanged = true; public ImportedAsset[] assets; }

    [MenuItem("MMORPG/UI/Import approved mail and social sprites")]
    public static void ImportApproved()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Import in Edit mode.");
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../image/designs"));
        Import(Path.Combine(root, "mail-ui-v1"), "MailV1", 19);
        Import(Path.Combine(root, "social-ui-v1"), "SocialV1", 23);
    }

    private static void Import(string sourceDirectory, string resourceFolder, int expectedCount)
    {
        var manifest = JsonUtility.FromJson<SourceManifest>(File.ReadAllText(Path.Combine(sourceDirectory, "unity-slices/input-manifest.json")));
        if (manifest.assets == null || manifest.assets.Length != expectedCount) throw new InvalidDataException("Unexpected approved asset count.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in manifest.assets)
        {
            if (asset.file != "assets/" + Path.GetFileName(asset.file) || !asset.file.EndsWith(".png", StringComparison.Ordinal))
                throw new InvalidDataException("Unexpected source path: " + asset.file);
            if (!names.Add(Path.GetFileNameWithoutExtension(asset.file))) throw new InvalidDataException("Duplicate sprite key.");
            if (asset.size == null || asset.size.Length != 2 || asset.size[0] < 1 || asset.size[1] < 1 || Math.Max(asset.size[0], asset.size[1]) > 8192)
                throw new InvalidDataException("Invalid sprite dimensions.");
            if (asset.borderLeftBottomRightTop == null || asset.borderLeftBottomRightTop.Length != 4)
                throw new InvalidDataException("Invalid sprite border.");
            if (Hash(Path.Combine(sourceDirectory, asset.file)) != asset.sha256) throw new InvalidDataException("Source SHA changed: " + asset.file);
        }
        string destination = "Assets/Resources/UI/Ugui/" + resourceFolder;
        Directory.CreateDirectory(destination);
        var imported = new List<ImportedAsset>();
        foreach (var asset in manifest.assets)
        {
            string name = Path.GetFileName(asset.file);
            string path = destination + "/" + name;
            string previousGuid = File.Exists(path + ".meta") ? AssetDatabase.AssetPathToGUID(path) : null;
            if (!File.Exists(path) || Hash(path) != asset.sha256) File.Copy(Path.Combine(sourceDirectory, asset.file), path, true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
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
            importer.maxTextureSize = Math.Max(asset.size[0], asset.size[1]) > 4096 ? 8192 : 4096;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null || sprite.rect.width != asset.size[0] || sprite.rect.height != asset.size[1] || sprite.border != Border(asset))
                throw new InvalidDataException("Imported size/border mismatch: " + path);
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (!string.IsNullOrEmpty(previousGuid) && previousGuid != guid) throw new InvalidDataException("Existing GUID changed: " + path);
            if (Hash(path) != asset.sha256) throw new InvalidDataException("Imported pixels changed: " + path);
            imported.Add(new ImportedAsset { source = asset.file, assetPath = path,
                resourceKey = "UI/Ugui/" + resourceFolder + "/" + Path.GetFileNameWithoutExtension(name),
                sha256 = asset.sha256, guid = guid, width = asset.size[0], height = asset.size[1],
                borderLeftBottomRightTop = asset.borderLeftBottomRightTop, borderVerifiedAgainst = asset.borderVerifiedAgainst });
        }
        string json = JsonUtility.ToJson(new Report { package = manifest.package, unityVersion = Application.unityVersion, assets = imported.ToArray() }, true);
        File.WriteAllText(destination + "/manifest.json", json);
        File.WriteAllText(Path.Combine(sourceDirectory, "unity-slices/manifest.json"), json);
        AssetDatabase.ImportAsset(destination + "/manifest.json", ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("MAIL_SOCIAL_ART_IMPORT_OK|package=" + manifest.package + "|sprites=" + imported.Count + "|sourcePixelsUnchanged=true");
    }
    private static Vector4 Border(SourceAsset a) => new Vector4(a.borderLeftBottomRightTop[0], a.borderLeftBottomRightTop[1], a.borderLeftBottomRightTop[2], a.borderLeftBottomRightTop[3]);
    private static string Hash(string path)
    {
        using var sha = SHA256.Create();
        using var input = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
    }
}
#endif
