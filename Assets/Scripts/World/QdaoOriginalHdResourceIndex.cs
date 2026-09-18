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
            public long sourceBytes;
            public long sourceWriteUtcTicks;
        }

        public string resourceFolder;
        public string manifestSha256;
        public string activationSha256;
        public string validationSha256;
        public Entry[] entries = Array.Empty<Entry>();
        private static readonly Dictionary<string, QdaoOriginalHdResourceIndex> Cache = new();
        private static readonly Dictionary<string, string> Validated = new();
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

        public static void ClearCache() { Cache.Clear(); Validated.Clear(); }

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
            Validated[folder] = revision; // Import/delete notifications call ClearCache before the next selection.
            return true;
        }

        public bool Validate(string folder, string manifestHash, string activationHash, Func<Entry, bool> fileMatches = null)
        {
            if (resourceFolder != folder || !Hex(manifestHash, 64) || !Hex(activationHash, 64) ||
                manifestSha256 != manifestHash || activationSha256 != activationHash || !Hex(validationSha256, 64) || entries == null || entries.Length != 137)
                return false;
            var required = new HashSet<string>(RequiredRelativePaths(), StringComparer.Ordinal);
            var guids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry == null || !required.Remove(entry.path) || entry.width != 1024 || entry.height != 1024 ||
                    !Hex(entry.assetGuid, 32) || !guids.Add(entry.assetGuid) || !Hex(entry.sha256, 64) || entry.sourceBytes <= 24) return false;
                if (fileMatches != null)
                {
                    if (!fileMatches(entry)) return false;
                }
#if UNITY_EDITOR
                else
                {
                    // isBroken on LazyLoadReference can load the texture. Use GUID/path/header checks instead.
                    var assetPath = "Assets/Resources/" + folder + "/" + entry.path;
                    if (UnityEditor.AssetDatabase.GUIDToAssetPath(entry.assetGuid) != assetPath) return false;
                    var fullPath = Path.Combine(Application.dataPath, "Resources", folder, entry.path);
                    var file = new FileInfo(fullPath);
                    if (!file.Exists || file.Length != entry.sourceBytes || file.LastWriteTimeUtc.Ticks != entry.sourceWriteUtcTicks ||
                        !PngSize(fullPath, out var width, out var height) || width != entry.width || height != entry.height) return false;
                }
#endif
            }
            return required.Count == 0;
        }

        public static bool TextureMatches(string resourcePath, int width, int height)
        {
            var root = QdaoCharacterCatalog.OriginalV14Root + "/";
            if (!resourcePath.StartsWith(root, StringComparison.Ordinal)) return false;
            var separator = resourcePath.IndexOf('/', root.Length);
            if (separator < 0) return false;
            var index = Load(resourcePath.Substring(0, separator));
            if (index == null || index.entries == null) return false;
            var relative = resourcePath.Substring(separator + 1) + ".png";
            foreach (var entry in index.entries)
                if (entry != null && entry.path == relative) return entry.width == width && entry.height == height;
            return false;
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
