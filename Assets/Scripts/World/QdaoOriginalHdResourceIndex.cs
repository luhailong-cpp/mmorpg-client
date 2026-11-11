using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace MmorpgClient.World
{
    /// <summary>Build-derived inventory. Contains no texture object references and never loads pixel data.</summary>
    public sealed class QdaoOriginalHdResourceIndex : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string path;
            public string assetGuid;
            public string sha256;
            public int width;
            public int height;
            public float pixelsPerUnit;
            public long sourceBytes;
            // Legacy local build metadata; Git checkouts do not preserve this timestamp.
            public long sourceWriteUtcTicks;
        }

        public string resourceFolder;
        public string manifestSha256;
        public string activationSha256;
        public string validationSha256;
        public string resolutionMode;
        public Entry[] entries = Array.Empty<Entry>();
        // 按相对路径查条目的表。只在 entries 整个数组被替换时（重新导入、测试换数组）重建；
        // 条目加载后视为只读，就地改某个条目的字段不会刷新这张表。
        [NonSerialized] private Entry[] _lookupSource;
        [NonSerialized] private Dictionary<string, Entry> _lookup;
        private static readonly Dictionary<string, QdaoOriginalHdResourceIndex> Cache = new();
        private static readonly Dictionary<string, string> Validated = new();
        private static readonly Dictionary<string, string> CreationValidated = new();
#if UNITY_EDITOR
        // This proof is issued only after the editor builder checks every source file. It is
        // deliberately not serialized: a fresh checkout/domain reload must validate its own files.
        private static readonly Dictionary<string, string> EditorCreationVerified = new();
#endif
        private static readonly string[] Directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        public static IEnumerable<string> RequiredRelativePaths()
        {
            yield return "portrait.png";
            foreach (var direction in Directions)
            {
                yield return "idle/" + direction + ".png";
                for (var frame = 1; frame <= 16; frame++) yield return $"walk/{direction}/{frame:00}.png";
            }
        }

        public static void ClearCache() { Cache.Clear(); Validated.Clear(); CreationValidated.Clear(); }

        public static void InvalidateCreationInventory(string folder)
        {
            CreationValidated.Remove(folder);
#if UNITY_EDITOR
            EditorCreationVerified.Remove(folder);
#endif
        }

#if UNITY_EDITOR
        public static void ResetEditorCreationInventories()
        {
            EditorCreationVerified.Clear();
            CreationValidated.Clear();
        }

        /// <summary>Called by the importer only after full source hashes, GUIDs and geometry have passed.</summary>
        public static void PublishEditorCreationInventory(QdaoOriginalHdResourceIndex index)
        {
            if (index == null) return;
            EditorCreationVerified[index.resourceFolder] = Revision(index.manifestSha256, index.activationSha256, index.validationSha256);
            CreationValidated.Remove(index.resourceFolder);
        }
#endif

        private static string Revision(string manifestHash, string activationHash, string validationHash)
            => manifestHash + ":" + activationHash + ":" + validationHash;

        /// <summary>Checks the generated inventory and its small revision documents, never the PNG source files.</summary>
        public static bool IsCompleteForCreation(string folder, string manifestHash, string activationHash)
        {
            if (string.IsNullOrEmpty(manifestHash) || string.IsNullOrEmpty(activationHash)) return false;
            var index = Load(folder);
            if (index == null) return false;
            var revision = Revision(manifestHash, activationHash, index.validationSha256);
#if UNITY_EDITOR
            if (!EditorCreationVerified.TryGetValue(folder, out var verified) || verified != revision) return false;
#endif
            if (CreationValidated.TryGetValue(folder, out var prior) && prior == revision) return true;
            if (!index.Validate(folder, manifestHash, activationHash, _ => true)) return false;
            var validation = Resources.Load<TextAsset>(folder + "/validation");
            if (validation == null) return false;
            using var sha = SHA256.Create();
            var digest = BitConverter.ToString(sha.ComputeHash(validation.bytes)).Replace("-", "").ToLowerInvariant();
            if (digest != index.validationSha256) return false;
            CreationValidated[folder] = revision;
            return true;
        }

        private static QdaoOriginalHdResourceIndex Load(string folder)
        {
            if (Cache.TryGetValue(folder, out var existing) && existing != null) return existing;
            var index = Resources.Load<QdaoOriginalHdResourceIndex>(folder + "/runtime-index");
            if (index != null) Cache[folder] = index;
            return index;
        }

        public static bool IsComplete(string folder, string manifestHash, string activationHash)
        {
            if (string.IsNullOrEmpty(manifestHash) || string.IsNullOrEmpty(activationHash)) return false;
            var index = Load(folder);
            if (index == null) return false;
            var revision = manifestHash + ":" + activationHash + ":" + index.validationSha256;
            if (Validated.TryGetValue(folder, out var prior) && prior == revision) return true;
            if (!index.Validate(folder, manifestHash, activationHash)) return false;
            var validation = Resources.Load<TextAsset>(folder + "/validation");
            if (validation == null) return false;
            using var sha = SHA256.Create();
            var digest = BitConverter.ToString(sha.ComputeHash(validation.bytes)).Replace("-", "").ToLowerInvariant();
            if (digest != index.validationSha256) return false;
            // Cache only within the current Unity import revision. ClearCache invalidates this
            // after source changes, so even same-length/same-timestamp edits are hashed again.
            Validated[folder] = revision;
            return true;
        }

        public bool Validate(string folder, string manifestHash, string activationHash, Func<Entry, bool> fileMatches = null)
        {
            if (resourceFolder != folder || !Hex(manifestHash, 64) || !Hex(activationHash, 64) ||
                manifestSha256 != manifestHash || activationSha256 != activationHash || !Hex(validationSha256, 64) || entries == null || entries.Length != 137)
                return false;
            var required = new HashSet<string>(RequiredRelativePaths(), StringComparer.Ordinal);
            var guids = new HashSet<string>(StringComparer.Ordinal);
            var mixed = resolutionMode == QdaoMixedResolutionContract.Mode;
            var originalV13 = folder.StartsWith(QdaoCharacterCatalog.OriginalV13Root + "/", StringComparison.Ordinal);
            if (!string.IsNullOrEmpty(resolutionMode) && !mixed) return false;
            if (mixed && (!folder.StartsWith(QdaoCharacterCatalog.OriginalV14Root + "/", StringComparison.Ordinal) ||
                !QdaoMixedResolutionContract.AllowedIdentity(folder.Substring(QdaoCharacterCatalog.OriginalV14Root.Length + 1)))) return false;
            var oldCount = 0; var newCount = 0;
            foreach (var entry in entries)
            {
                if (entry == null || !required.Remove(entry.path) || entry.height != entry.width ||
                    (mixed && entry.path != "portrait.png" ?
                        (entry.width != 512 && entry.width != 1024) || entry.pixelsPerUnit != (entry.width == 512 ? 52f : 104f) :
                        entry.width != (originalV13 && entry.path != "portrait.png" ? 512 : 1024)) ||
                    !Hex(entry.assetGuid, 32) || !guids.Add(entry.assetGuid) || !Hex(entry.sha256, 64) || entry.sourceBytes <= 24) return false;
                if (entry.path != "portrait.png") { if (entry.width == 512) oldCount++; else newCount++; }
                if (fileMatches != null)
                {
                    if (!fileMatches(entry)) return false;
                }
#if UNITY_EDITOR
                else
                {
                    // isBroken on LazyLoadReference can load the texture. Verify the signed source bytes instead.
                    var assetPath = "Assets/Resources/" + folder + "/" + entry.path;
                    if (UnityEditor.AssetDatabase.GUIDToAssetPath(entry.assetGuid) != assetPath) return false;
                    var fullPath = Path.Combine(Application.dataPath, "Resources", folder, entry.path);
                    if (!QdaoHdSourceFileVerifier.Matches(fullPath, entry.sourceBytes, entry.width, entry.height,
                        entry.sha256)) return false;
                }
#endif
            }
            return required.Count == 0 && (!mixed || (oldCount > 0 && newCount > 0));
        }

        public static bool TextureMatches(string resourcePath, int width, int height)
        {
            var root = resourcePath.StartsWith(QdaoCharacterCatalog.OriginalV13Root + "/", StringComparison.Ordinal)
                ? QdaoCharacterCatalog.OriginalV13Root + "/" : QdaoCharacterCatalog.OriginalV14Root + "/";
            if (!resourcePath.StartsWith(root, StringComparison.Ordinal)) return false;
            var separator = resourcePath.IndexOf('/', root.Length);
            if (separator < 0) return false;
            var index = Load(resourcePath.Substring(0, separator));
            if (index == null || index.entries == null) return false;
            var entry = index.FindEntry(resourcePath.Substring(separator + 1) + ".png");
            return entry != null && entry.width == width && entry.height == height;
        }

        /// <summary>
        /// 按相对路径（如 "walk/N/01.png"）找清单条目，找不到返回 null。同一路径出现多次时以先出现的为准，
        /// 与逐项扫描的结果一致。每个人物一次解析要查 137 次，所以用表查而不是每次扫全表。
        /// </summary>
        public Entry FindEntry(string relativePath)
        {
            if (entries == null || relativePath == null) return null;
            if (_lookup == null || !ReferenceEquals(_lookupSource, entries))
            {
                var lookup = new Dictionary<string, Entry>(entries.Length, StringComparer.Ordinal);
                foreach (var entry in entries)
                    if (entry?.path != null) lookup.TryAdd(entry.path, entry);
                _lookup = lookup;
                _lookupSource = entries;
            }
            return _lookup.TryGetValue(relativePath, out var found) ? found : null;
        }

        public static bool PngSize(string path, out int width, out int height)
        {
            width = height = 0;
            try
            {
                using var stream = File.OpenRead(path);
                var header = new byte[24];
                if (stream.Read(header, 0, header.Length) != header.Length || header[0] != 137 || header[1] != 80 ||
                    header[2] != 78 || header[3] != 71 || header[4] != 13 || header[5] != 10 || header[6] != 26 || header[7] != 10 || header[12] != 73 || header[13] != 72 || header[14] != 68 || header[15] != 82) return false;
                width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                return width > 0 && height > 0;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static bool Hex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (var character in value) if (!Uri.IsHexDigit(character)) return false;
            return true;
        }
    }
}
