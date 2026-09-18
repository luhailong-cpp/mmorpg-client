using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MmorpgClient.World
{
    /// <summary>Explicit per-frame geometry for preserved originals. Metadata is not artwork approval.</summary>
    public static class QdaoMixedResolutionContract
    {
        public const string Mode = "mixed-preserved-v1";
        public static bool AllowedIdentity(string id) => id == "04_mountain_guardian_boy" ||
            id == "05_celestial_musician_girl" || id == "06_thunder_caster_boy";

        public readonly struct Geometry
        {
            public readonly int Width, Height;
            public readonly float PixelsPerUnit;
            public Vector2 Pivot => new Vector2(.5f, .08f);
            public Geometry(int size, float pixelsPerUnit) { Width = Height = size; PixelsPerUnit = pixelsPerUnit; }
            public bool Matches(Sprite sprite) => sprite != null && sprite.texture != null &&
                sprite.texture.width == Width && sprite.texture.height == Height &&
                sprite.rect.width == Width && sprite.rect.height == Height && sprite.pixelsPerUnit == PixelsPerUnit &&
                Mathf.Abs(sprite.pivot.x / Width - .5f) < .0001f && Mathf.Abs(sprite.pivot.y / Height - .08f) < .0001f;
        }

        [Serializable] public sealed class Row
        {
            public string path, sha256, source_kind, source_sha256, preserved_sha256;
            public int width, height;
            public float pixels_per_unit;
            public float[] pivot;
            public int[] root_px, native_cell_size;
        }
        [Serializable] private sealed class Manifest
        {
            public string resolution_mode, preserved_snapshot_sha256;
            public Row[] files;
        }

        public static bool TryRead(string id, byte[] bytes, string activationMode,
            out Dictionary<string, Geometry> geometry)
        {
            geometry = null;
            Manifest manifest;
            try { manifest = JsonUtility.FromJson<Manifest>(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF')); }
            catch (ArgumentException) { return false; }
            if (manifest == null) return false;
            if (string.IsNullOrEmpty(manifest.resolution_mode)) return string.IsNullOrEmpty(activationMode);
            if (manifest.resolution_mode != Mode || activationMode != Mode || !AllowedIdentity(id) ||
                !Hash(manifest.preserved_snapshot_sha256) || manifest.files == null || manifest.files.Length != 137) return false;
            var remaining = new HashSet<string>(QdaoOriginalHdResourceIndex.RequiredRelativePaths(), StringComparer.Ordinal);
            var parsed = new Dictionary<string, Geometry>(StringComparer.Ordinal);
            var oldCount = 0; var newCount = 0;
            foreach (var row in manifest.files)
            {
                if (row == null || !remaining.Remove(row.path) || !Hash(row.sha256) || !Hash(row.source_sha256)) return false;
                if (row.path == "portrait.png")
                {
                    if (row.width != 1024 || row.height != 1024 || row.source_kind != "original-portrait") return false;
                    continue;
                }
                var legacy = row.source_kind == "preserved-v13";
                if (!legacy && row.source_kind != "native-hd") return false;
                var size = legacy ? 512 : 1024;
                if (row.width != size || row.height != size || row.pixels_per_unit != (legacy ? 52f : 104f) ||
                    row.pivot == null || row.pivot.Length != 2 || row.pivot[0] != .5f || row.pivot[1] != .08f ||
                    row.root_px == null || row.root_px.Length != 2 || row.root_px[0] != size / 2 ||
                    row.root_px[1] != (legacy ? 471 : 942) || row.native_cell_size == null || row.native_cell_size.Length != 2 ||
                    row.native_cell_size[0] < (legacy ? 1 : 1024) || row.native_cell_size[1] < (legacy ? 1 : 1024) ||
                    (legacy && row.preserved_sha256 != row.sha256)) return false;
                parsed.Add(row.path, new Geometry(size, row.pixels_per_unit));
                if (legacy) oldCount++; else newCount++;
            }
            if (remaining.Count != 0 || oldCount == 0 || newCount == 0) return false;
            geometry = parsed;
            return true;
        }

        private static bool Hash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var character in value) if (!Uri.IsHexDigit(character)) return false;
            return true;
        }
    }
}
