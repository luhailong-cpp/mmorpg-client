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
    /// <summary>
    /// Derives metadata-only V13/V14 inventories after full source validation. Never rewrites artwork.
    /// 编辑器里的校验证明（IsComplete / IsCompleteForCreation 认的那一份）有三个来源：
    /// 构建前全量重算（发行包的闸门，已登记人物有一个不通过就让构建失败）、资源导入后按文件夹重算、
    /// 域重载后按本编辑器会话保存的指纹恢复（指纹对不上的文件夹仍全量重算）。
    /// </summary>
    [InitializeOnLoad]
    public sealed class QdaoOriginalHdResourceIndexBuilder : AssetPostprocessor, IPreprocessBuildWithReport
    {
        public const string AssetRoot = "Assets/Resources/World/Characters/QdaoOriginalRosterV14";
        public const string OriginalV13AssetRoot = "Assets/Resources/" + QdaoCharacterCatalog.OriginalV13Root;
        private const string IndexFileName = "runtime-index.asset";
        // 校验规则变了就改这个版本号，让编辑器会话里保存的指纹全部作废、重新全量校验。
        private const string ProofVersion = "qdao-index-proof-v1";
        private const string ProofKeyPrefix = "MmorpgClient.QdaoIndexProof:";
        private static readonly string[] AssetRoots = { AssetRoot, OriginalV13AssetRoot };
        private static readonly string[] SmallDocuments = { "manifest.json", "appearance.json", "validation.json" };
        private static readonly HashSet<string> Pending = new(StringComparer.Ordinal);
        // 本构建器刚写回的索引及其内容。导入回调看到同一路径、同样内容时不再排重建；内容不同说明是外部改动，照常重验。
        private static readonly Dictionary<string, WrittenIndex> SelfWrittenIndexes = new(StringComparer.Ordinal);
        private static bool queued;
        public int callbackOrder => 0;

        [Serializable] private sealed class FileRow { public string path; public string sha256; }
        [Serializable] private sealed class ManifestFiles { public FileRow[] files; public string resolution_mode; }
        [Serializable] private sealed class ActivationHashes { public string manifest_sha256; public string validation_sha256; }

        private sealed class WrittenIndex
        {
            public string Folder, ManifestHash, ActivationHash, ValidationHash, Mode;
            public QdaoOriginalHdResourceIndex.Entry[] Entries;
        }

        static QdaoOriginalHdResourceIndexBuilder() => EditorApplication.delayCall += RestoreOrRebuildAll;

        public void OnPreprocessBuild(BuildReport report)
        {
            // 发行包只认构建这一刻逐个源文件重算的摘要。不让它悄悄少一个人物。
            var failed = RebuildAll();
            if (failed.Count > 0)
                throw new BuildFailedException("Qdao character resources failed verification: " + string.Join(", ", failed));
        }

        /// <summary>全量重验全部人物文件夹（逐个源文件算摘要），返回校验不通过的已登记人物文件夹。</summary>
        public static IReadOnlyList<string> RebuildAll()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            QdaoOriginalHdResourceIndex.ResetEditorCreationInventories();
            var failed = new List<string>();
            var folders = 0;
            foreach (var folder in AllFolders())
            {
                folders++;
                if (!Rebuild(folder) && IsRegisteredIdentityFolder(folder)) failed.Add(folder);
            }
            QdaoCharacterCatalog.RefreshAppearances();
            Debug.Log($"[QdaoOriginalHdResourceIndexBuilder] RebuildAll verified {folders} folders in {timer.ElapsedMilliseconds} ms");
            ReportFailures(failed);
            return failed;
        }

        /// <summary>
        /// 域重载（每次重编译、每次进 Play）后调用：本编辑器会话里完整校验过、且指纹没变的文件夹直接恢复证明，
        /// 其余文件夹全量重算。编辑器重启后会话状态清空，第一次仍全量校验。
        /// </summary>
        private static void RestoreOrRebuildAll()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            QdaoOriginalHdResourceIndex.ResetEditorCreationInventories();
            var failed = new List<string>();
            int restored = 0, verified = 0;
            foreach (var folder in AllFolders())
            {
                if (TryRestoreProof(folder))
                {
                    restored++;
                    continue;
                }
                verified++;
                if (!Rebuild(folder) && IsRegisteredIdentityFolder(folder)) failed.Add(folder);
            }
            QdaoCharacterCatalog.RefreshAppearances();
            Debug.Log($"[QdaoOriginalHdResourceIndexBuilder] Domain reload restored {restored} proofs and verified {verified} folders in {timer.ElapsedMilliseconds} ms");
            ReportFailures(failed);
        }

        private static void ReportFailures(IReadOnlyCollection<string> failed)
        {
            // 编辑器里只告警：资源导入到一半很常见，这些人物在运行时显示为不可用（fail-closed）；构建时才让它失败。
            if (failed.Count > 0)
                Debug.LogWarning("[QdaoOriginalHdResourceIndexBuilder] Registered character folders failed verification and stay unavailable: " +
                                 string.Join(", ", failed));
        }

        private static IEnumerable<string> AllFolders()
        {
            foreach (var root in AssetRoots)
                if (Directory.Exists(root))
                    foreach (var folder in Directory.GetDirectories(root)) yield return folder.Replace('\\', '/');
        }

        private static bool IsRegisteredIdentityFolder(string folder)
            => QdaoCharacterCatalog.Find(Path.GetFileName(folder))?.IsOriginalRoster == true;

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var groups = new[] { imported, deleted, moved, movedFrom };
            var invalidated = false;
            for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            foreach (var path in groups[groupIndex])
            {
                // 本构建器自己写回的索引：内容与写入时一致就不再排重建，否则同一文件夹会再算一遍 137 张图。
                if (groupIndex == 0 && IsSelfWrittenIndex(path)) continue;
                foreach (var root in AssetRoots)
                {
                    if (path == root)
                    {
                        QdaoOriginalHdResourceIndex.ResetEditorCreationInventories();
                        ForgetAllProofs();
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
                    ForgetProof(assetFolder);
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
            ForgetProof(folder);
            var indexPath = folder + "/" + IndexFileName;
            if (!TryDescribe(folder, definition, out var manifestHash, out var activationHash, out var validationHash, out var mode, out var entries))
            {
                if (AssetDatabase.LoadAssetAtPath<QdaoOriginalHdResourceIndex>(indexPath) != null)
                    AssetDatabase.DeleteAsset(indexPath);
                return false;
            }
            var index = AssetDatabase.LoadAssetAtPath<QdaoOriginalHdResourceIndex>(indexPath);
            if (index != null && index.resolutionMode == mode && Equivalent(index, resourceFolder, manifestHash, activationHash, validationHash, entries))
            {
                PublishProof(folder, index);
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
            // 先登记再写：导入回调可能在 CreateAsset / SaveAssets 里同步触发。
            SelfWrittenIndexes[indexPath] = new WrittenIndex
            {
                Folder = resourceFolder, ManifestHash = manifestHash, ActivationHash = activationHash,
                ValidationHash = validationHash, Mode = mode, Entries = entries,
            };
            if (created) AssetDatabase.CreateAsset(index, indexPath);
            else EditorUtility.SetDirty(index);
            AssetDatabase.SaveAssets();
            PublishProof(folder, index);
            QdaoCharacterCatalog.RefreshAppearances();
            return true;
        }

        private static void PublishProof(string folder, QdaoOriginalHdResourceIndex index)
        {
            QdaoOriginalHdResourceIndex.PublishEditorCreationInventory(index);
            var fingerprint = SessionFingerprint(folder);
            if (fingerprint != null) SessionState.SetString(ProofKey(folder), fingerprint);
        }

        private static bool TryRestoreProof(string folder)
        {
            var saved = SessionState.GetString(ProofKey(folder), string.Empty);
            if (saved.Length == 0) return false;
            var index = AssetDatabase.LoadAssetAtPath<QdaoOriginalHdResourceIndex>(folder + "/" + IndexFileName);
            if (index == null || saved != SessionFingerprint(folder)) return false;
            QdaoOriginalHdResourceIndex.PublishEditorCreationInventory(index);
            return true;
        }

        /// <summary>
        /// 规则版本 + 三份小文档的内容摘要 + 运行时索引与 137 张图在 AssetDatabase 里的依赖哈希。
        /// 依赖哈希随源文件内容、.meta 和导入设置变化；Unity 没察觉到的源文件改动也不会进运行时用的导入结果，
        /// 构建前仍会逐个文件重算（OnPreprocessBuild）。缺任何一项都返回 null，按未校验处理。
        /// </summary>
        private static string SessionFingerprint(string folder)
        {
            var text = new StringBuilder(ProofVersion);
            foreach (var name in SmallDocuments)
            {
                var path = folder + "/" + name;
                if (!File.Exists(path)) return null;
                text.Append('|').Append(Hash(File.ReadAllBytes(path)));
            }
            foreach (var relative in QdaoOriginalHdResourceIndex.RequiredRelativePaths().Prepend(IndexFileName))
            {
                var dependencyHash = AssetDatabase.GetAssetDependencyHash(folder + "/" + relative);
                if (!dependencyHash.isValid) return null;
                text.Append('|').Append(dependencyHash.ToString());
            }
            return Hash(Encoding.UTF8.GetBytes(text.ToString()));
        }

        private static string ProofKey(string folder) => ProofKeyPrefix + folder;

        private static void ForgetProof(string folder) => SessionState.EraseString(ProofKey(folder));

        private static void ForgetAllProofs()
        {
            foreach (var folder in AllFolders()) ForgetProof(folder);
        }

        private static bool IsSelfWrittenIndex(string path)
        {
            if (!SelfWrittenIndexes.Remove(path, out var written)) return false;
            var index = AssetDatabase.LoadAssetAtPath<QdaoOriginalHdResourceIndex>(path);
            return index != null && index.resolutionMode == written.Mode &&
                   Equivalent(index, written.Folder, written.ManifestHash, written.ActivationHash, written.ValidationHash, written.Entries);
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
