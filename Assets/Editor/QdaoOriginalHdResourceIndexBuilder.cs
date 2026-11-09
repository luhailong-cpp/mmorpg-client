using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MmorpgClient.World;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MmorpgClient.Editor.Tianyong
{
    /// <summary>Derives metadata-only V13/V14 inventories after full source validation. Never rewrites artwork.</summary>
    [InitializeOnLoad]
    public sealed class QdaoOriginalHdResourceIndexBuilder : AssetPostprocessor, IPreprocessBuildWithReport
    {
        public const string AssetRoot = "Assets/Resources/World/Characters/QdaoOriginalRosterV14";
        public const string OriginalV13AssetRoot = "Assets/Resources/" + QdaoCharacterCatalog.OriginalV13Root;
        private static readonly string[] AssetRoots = { AssetRoot, OriginalV13AssetRoot };
        private static readonly HashSet<string> Pending = new(StringComparer.Ordinal);
        private static bool queued;
        public int callbackOrder => 0;

        [Serializable] private sealed class FileRow { public string path; public string sha256; }
        [Serializable] private sealed class ManifestFiles { public FileRow[] files; public string resolution_mode; }
        [Serializable] private sealed class ActivationHashes { public string manifest_sha256; public string validation_sha256; }

        static QdaoOriginalHdResourceIndexBuilder() => EditorApplication.delayCall += RebuildAll;

        public void OnPreprocessBuild(BuildReport report) => RebuildAll();

        public static void RebuildAll()
        {
            QdaoOriginalHdResourceIndex.ResetEditorCreationInventories();
            foreach (var root in AssetRoots)
                if (Directory.Exists(root))
                    foreach (var folder in Directory.GetDirectories(root)) Rebuild(folder.Replace('\\', '/'));
            QdaoCharacterCatalog.RefreshAppearances();
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var groups = new[] { imported, deleted, moved, movedFrom };
            var invalidated = false;
            for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            foreach (var path in groups[groupIndex])
            {
                foreach (var root in AssetRoots)
                {
                    if (path == root)
                    {
                        QdaoOriginalHdResourceIndex.ResetEditorCreationInventories();
                        invalidated = true;
                        // Reset removes both versions' proofs, so restore both surviving roots.
                        foreach (var pendingRoot in AssetRoots)
                            if (Directory.Exists(pendingRoot))
                                foreach (var folder in Directory.GetDirectories(pendingRoot)) Pending.Add(folder.Replace('\\', '/'));
                        continue;
                    }
                    if (!path.StartsWith(root + "/", StringComparison.Ordinal)) continue;
                    var rest = path.Substring(root.Length + 1);
                    var separator = rest.IndexOf('/');
                    var id = separator < 0 ? rest : rest.Substring(0, separator);
                    if (string.IsNullOrEmpty(id)) continue;
                    var assetFolder = root + "/" + id;
                    // An old passing index must not be used while its new source revision is pending.
                    QdaoOriginalHdResourceIndex.InvalidateCreationInventory(assetFolder.Substring("Assets/Resources/".Length));
                    invalidated = true;
                    Pending.Add(assetFolder);
                }
            }
            // Deleted/changed source paths immediately make cached selections recheck their index.
            // A deleted root can leave no work to queue, but still changes what the UI may select.
            if (invalidated) QdaoCharacterCatalog.RefreshAppearances();
            if (Pending.Count == 0 || queued) return;
            queued = true;
            EditorApplication.delayCall += Flush;
        }

        private static void Flush()
        {
            queued = false;
            var folders = Pending.ToArray();
            Pending.Clear();
            foreach (var folder in folders) Rebuild(folder);
            QdaoCharacterCatalog.RefreshAppearances();
        }

        public static bool Rebuild(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');
            var root = AssetRoots.FirstOrDefault(candidate => folder.StartsWith(candidate + "/", StringComparison.Ordinal));
            if (root == null || folder.Substring(root.Length + 1).Contains('/')) return false;
            var id = folder.Substring(root.Length + 1);
            var definition = QdaoCharacterCatalog.Find(id);
            if (definition == null || !definition.IsOriginalRoster) return false;
            var resourceFolder = folder.Substring("Assets/Resources/".Length);
            QdaoOriginalHdResourceIndex.InvalidateCreationInventory(resourceFolder);
            var indexPath = folder + "/runtime-index.asset";
            if (!TryDescribe(folder, definition, out var manifestHash, out var activationHash, out var validationHash, out var mode, out var entries))
            {
                if (AssetDatabase.LoadAssetAtPath<QdaoOriginalHdResourceIndex>(indexPath) != null)
                    AssetDatabase.DeleteAsset(indexPath);
                return false;
            }
            var index = AssetDatabase.LoadAssetAtPath<QdaoOriginalHdResourceIndex>(indexPath);
            if (index != null && index.resolutionMode == mode && Equivalent(index, resourceFolder, manifestHash, activationHash, validationHash, entries))
            {
                QdaoOriginalHdResourceIndex.PublishEditorCreationInventory(index);
                return true;
            }
            var created = index == null;
            if (created) index = ScriptableObject.CreateInstance<QdaoOriginalHdResourceIndex>();
            index.resourceFolder = resourceFolder;
            index.manifestSha256 = manifestHash;
            index.activationSha256 = activationHash;
            index.validationSha256 = validationHash;
            index.resolutionMode = mode;
            index.entries = entries;
            if (created) AssetDatabase.CreateAsset(index, indexPath);
            else EditorUtility.SetDirty(index);
            AssetDatabase.SaveAssets();
            QdaoOriginalHdResourceIndex.PublishEditorCreationInventory(index);
            QdaoCharacterCatalog.RefreshAppearances();
            return true;
        }

        private static bool Equivalent(QdaoOriginalHdResourceIndex index, string folder, string manifestHash,
            string activationHash, string validationHash, QdaoOriginalHdResourceIndex.Entry[] rows)
        {
            if (index.resourceFolder != folder || index.manifestSha256 != manifestHash || index.activationSha256 != activationHash || index.validationSha256 != validationHash ||
                index.entries == null || index.entries.Length != rows.Length) return false;
            for (var i = 0; i < rows.Length; i++)
            {
                var a = index.entries[i]; var b = rows[i];
                // Timestamps belong to the producing machine, not the signed portable inventory.
                if (a == null || a.path != b.path || a.assetGuid != b.assetGuid || a.sha256 != b.sha256 || a.width != b.width ||
                    a.height != b.height || a.pixelsPerUnit != b.pixelsPerUnit || a.sourceBytes != b.sourceBytes) return false;
            }
            return true;
        }

        private static bool TryDescribe(string folder, QdaoCharacterCatalog.Definition definition,
            out string manifestHash, out string activationHash, out string validationHash, out string mode, out QdaoOriginalHdResourceIndex.Entry[] entries)
        {
            manifestHash = activationHash = validationHash = mode = null; entries = null;
            try
            {
                var manifestPath = folder + "/manifest.json";
                var activationPath = folder + "/appearance.json";
                var validationPath = folder + "/validation.json";
                if (!File.Exists(manifestPath) || !File.Exists(activationPath) || !File.Exists(validationPath)) return false;
                var manifest = File.ReadAllBytes(manifestPath);
                var activation = File.ReadAllBytes(activationPath);
                manifestHash = Hash(manifest); activationHash = Hash(activation);
                var approval = JsonUtility.FromJson<ActivationHashes>(Encoding.UTF8.GetString(activation).TrimStart('\uFEFF'));
                validationHash = Hash(File.ReadAllBytes(validationPath));
                if (approval == null || approval.manifest_sha256 != manifestHash || approval.validation_sha256 != validationHash) return false;
                var files = JsonUtility.FromJson<ManifestFiles>(Encoding.UTF8.GetString(manifest).TrimStart('\uFEFF'));
                var originalV13 = folder.StartsWith(OriginalV13AssetRoot + "/", StringComparison.Ordinal);
                if (files?.files == null || (files.files.Length != 137 && (!originalV13 || files.files.Length != 145))) return false;
                var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var file in files.files)
                    if (file == null || string.IsNullOrEmpty(file.path) || string.IsNullOrEmpty(file.sha256) || !hashes.TryAdd(file.path, file.sha256)) return false;
                var expected = QdaoOriginalHdResourceIndex.RequiredRelativePaths().ToArray();
                var expectedInventory = new HashSet<string>(expected, StringComparer.Ordinal);
                if (originalV13 && files.files.Length == 145)
                    foreach (var direction in new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" })
                        expectedInventory.Add("walk/" + direction + "/strip.png");
                if (!expectedInventory.SetEquals(hashes.Keys)) return false;
                mode = files.resolution_mode;
                var selected = originalV13
                    ? QdaoCharacterCatalog.SelectOriginalAppearance(definition, Encoding.UTF8.GetString(activation), manifest, (_, _, _) => true)
                    : QdaoCharacterCatalog.SelectOriginalHdAppearance(definition, Encoding.UTF8.GetString(activation), manifest, (_, _, _) => true);
                if (selected == null) return false;
                var rows = new List<QdaoOriginalHdResourceIndex.Entry>();
                foreach (var relative in expected)
                {
                    var path = folder + "/" + relative;
                    var file = new FileInfo(path);
                    var geometry = relative == "portrait.png" ? new QdaoMixedResolutionContract.Geometry(1024, 100f) :
                        selected.GeometryForResource(selected.ResourceFolder + "/" + relative.Substring(0, relative.Length - 4));
                    if (!file.Exists || !QdaoOriginalHdResourceIndex.PngSize(path, out var width, out var height) || width != geometry.Width || height != geometry.Height) return false;
                    var guid = AssetDatabase.AssetPathToGUID(path);
                    if (string.IsNullOrEmpty(guid) || AssetDatabase.GUIDToAssetPath(guid) != path) return false;
                    if (Hash(File.ReadAllBytes(path)) != hashes[relative]) return false;
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null || importer.maxTextureSize < width || importer.npotScale != TextureImporterNPOTScale.None) return false;
                    importer.GetSourceTextureWidthAndHeight(out var importedWidth, out var importedHeight);
                    if (importedWidth != width || importedHeight != height) return false;
                    rows.Add(new QdaoOriginalHdResourceIndex.Entry { path = relative, assetGuid = guid, sha256 = hashes[relative],
                        width = width, height = height, pixelsPerUnit = geometry.PixelsPerUnit, sourceBytes = file.Length, sourceWriteUtcTicks = file.LastWriteTimeUtc.Ticks });
                }
                entries = rows.ToArray();
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (ArgumentException) { return false; }
        }

        private static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
