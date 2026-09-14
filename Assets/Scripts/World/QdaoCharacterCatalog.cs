using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.World
{
    /// <summary>Approved Daoist chibi identities with independently accepted resource versions.</summary>
    public static class QdaoCharacterCatalog
    {
        public const string LegacyId = "QdaoHeadbandBoy";
        public const string DefaultId = "24_lu_dongbin";
        private const string V11Root = "World/Characters/QdaoRosterV11";
        private const string V12Root = "World/Characters/QdaoRosterV12";
        private static readonly string[] Directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        /// <summary>A complete, immutable visual contract, selected before loading any animation.</summary>
        public sealed class Appearance
        {
            public string Id { get; }
            public int Version { get; }
            public int AlignmentVersion { get; }
            public string ResourceFolder { get; }
            public int FrameCount => Version == 12 ? 8 : 4;
            public float FramesPerSecond => Version == 12 ? 1f / 0.06f : 1f / 0.12f;
            /// <summary>
            /// V12 always ships eight standing textures. A V11 character has them
            /// too once idle/&lt;DIR&gt;.png (512x512) exists for all eight directions
            /// under its V11 folder: the standing poses drawn for the V12 round are
            /// published early for characters whose costume did not change, so a
            /// stopped actor no longer freezes on walk frame 01 (a full stride in
            /// 47 of the 64 V11 direction sets, 2026-09-13). Probed once per
            /// appearance on first use; a partial set counts as absent.
            /// </summary>
            public bool HasDedicatedIdle => Version == 12 || (_v11Idle ??= ProbeV11Idle());
            private bool? _v11Idle;
            private bool ProbeV11Idle()
            {
                foreach (var direction in Directions)
                {
                    var texture = Resources.Load<Texture2D>($"{ResourceFolder}/idle/{direction}");
                    if (texture == null || texture.width != 512 || texture.height != 512) return false;
                }
                return true;
            }
            public int ContactFrame { get; }
            public string CacheKey { get; }
            internal Appearance(string id, int version, int contactFrame = 0, string revision = "", int alignmentVersion = 2)
            {
                Id = id;
                Version = version;
                AlignmentVersion = alignmentVersion;
                ContactFrame = contactFrame;
                ResourceFolder = (version == 12 ? V12Root : V11Root) + "/" + id;
                CacheKey = id + "@v" + version + ":" + revision + ":contact" + contactFrame +
                           (alignmentVersion == 2 ? "" : ":alignment" + alignmentVersion);
            }
            public string FrameResourcePath(string direction, int frameIndex)
                => $"{ResourceFolder}/walk/{direction}/{frameIndex + 1:00}";
            public string IdleResourcePath(string direction)
                => HasDedicatedIdle ? $"{ResourceFolder}/idle/{direction}" : FrameResourcePath(direction, ContactFrame);
            public string StripResourcePath(string direction)
                => Version == 12 ? $"{ResourceFolder}/walk/{direction}/strip" : $"{ResourceFolder}/walk_{direction}";
        }

        public sealed class Definition
        {
            public string Id { get; }
            public string Name { get; }
            public Appearance BaselineAppearance { get; }
            private Appearance _appearance;
            private string _activationText;
            public string ResourceFolder => ResolveAppearance().ResourceFolder;
            public int FrameCount => ResolveAppearance().FrameCount;
            public float FramesPerSecond => ResolveAppearance().FramesPerSecond;
            public bool HasDedicatedIdle => ResolveAppearance().HasDedicatedIdle;
            public int Version => ResolveAppearance().Version;
            public string FrameResourcePath(string direction, int frameIndex)
                => ResolveAppearance().FrameResourcePath(direction, frameIndex);
            public string IdleResourcePath(string direction) => ResolveAppearance().IdleResourcePath(direction);
            public Definition(string id, string name)
            {
                Id = id;
                Name = name;
                BaselineAppearance = new Appearance(id, 11);
            }
            public Appearance ResolveAppearance()
            {
                var activation = Resources.Load<TextAsset>($"{V12Root}/{Id}/appearance");
                var text = activation != null ? activation.text : null;
                if (_appearance != null && _activationText == text) return _appearance;
                var selected = SelectAppearance(this, text, TextureMatches);
                _activationText = text;
                // Retry incomplete imports: a valid metadata file is published last,
                // but Unity may still be finishing the texture import in the editor.
                _appearance = selected.Version == 12 || string.IsNullOrEmpty(text) ? selected : null;
                return selected;
            }
            internal void Invalidate() { _appearance = null; _activationText = null; }
        }

        [Serializable]
        private sealed class Activation
        {
            public int version;
            public string characterId;
            public int frameCount;
            public int alignmentVersion;
            public int frameDurationMs;
            public bool dedicatedIdle;
            public int contactFrame;
            public string status;
            public string visualReview;
            public string manifest_sha256;
            public string qc_sha256;
            public string validation_sha256;
        }

        /// <summary>
        /// Select V12 only after its approval record and every required texture agree.
        /// The loader validates texture dimensions; tests may supply an asset inventory.
        /// Missing or rejected upgrades always retain this same character's V11 art.
        /// </summary>
        public static Appearance SelectAppearance(Definition definition, string metadataJson,
            Func<string, int, int, bool> validTexture)
        {
            if (definition == null) return null;
            var fallback = definition.BaselineAppearance;
            if (string.IsNullOrWhiteSpace(metadataJson) || validTexture == null) return fallback;
            Activation record;
            try { record = JsonUtility.FromJson<Activation>(metadataJson); }
            catch (ArgumentException) { return fallback; }
            if (record == null || record.version != 12 || record.characterId != definition.Id ||
                record.frameCount != 8 || record.frameDurationMs != 60 || !record.dedicatedIdle ||
                (record.contactFrame != 0 && record.contactFrame != 4) ||
                record.status != "passed" || record.visualReview != "passed" ||
                !IsSha256(record.manifest_sha256) || !IsSha256(record.qc_sha256) || !IsSha256(record.validation_sha256))
                return fallback;
            // Missing field is the unchanged legacy v2 approval format.
            var alignmentVersion = record.alignmentVersion == 0 ? 2 : record.alignmentVersion;
            if (alignmentVersion != 2 && alignmentVersion != 3) return fallback;
            var candidate = new Appearance(definition.Id, 12, record.contactFrame, record.manifest_sha256, alignmentVersion);
            if (!validTexture(candidate.ResourceFolder + "/portrait", 1024, 1024)) return fallback;
            foreach (var direction in Directions)
            {
                if (!validTexture(candidate.IdleResourcePath(direction), 512, 512)) return fallback;
                for (var frame = 0; frame < candidate.FrameCount; frame++)
                    if (!validTexture(candidate.FrameResourcePath(direction, frame), 512, 512)) return fallback;
            }
            return candidate;
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var character in value)
                if (!Uri.IsHexDigit(character)) return false;
            return true;
        }

        private static bool TextureMatches(string path, int width, int height)
        {
            var texture = Resources.Load<Texture2D>(path);
            return texture != null && texture.width == width && texture.height == height;
        }

        /// <summary>Refresh after a completed resource import; identities stay stable and versioned caches remain separate.</summary>
        public static void RefreshAppearances()
        {
            foreach (var entry in Entries) entry.Invalidate();
        }

        private static readonly Definition[] Entries =
        {
            new("23_lantern_courier", "灯穗小使"),
            new("24_lu_dongbin", "吕洞宾"),
            new("25_lion_drum_guard", "狮鼓护卫"),
            new("26_osmanthus_healer", "桂香药婆"),
            new("27_ink_kite_ranger", "墨鸢游侠"),
            new("28_moon_rabbit_artificer", "月兔机关师"),
            new("29_he_xiangu", "何仙姑"),
            new("30_han_xiangzi", "韩湘子"),
        };
        public static IReadOnlyList<Definition> All { get; } = Array.AsReadOnly(Entries);
        private static readonly Dictionary<string, Sprite> Portraits = new();

        public static Definition Find(string id)
        {
            foreach (var entry in Entries)
                if (entry.Id == id) return entry;
            return null;
        }

        /// <summary>Existing four professions and two genders keep their gameplay unchanged.</summary>
        public static string ResolveRole(uint classId, uint gender)
        {
            var female = gender == 2;
            switch (classId)
            {
                case 1: return female ? "23_lantern_courier" : "24_lu_dongbin";
                case 2: return female ? "28_moon_rabbit_artificer" : "30_han_xiangzi";
                case 3: return female ? "29_he_xiangu" : "27_ink_kite_ranger";
                case 4: return female ? "26_osmanthus_healer" : "25_lion_drum_guard";
                default: return DefaultId;
            }
        }

        public static Sprite LoadPortrait(string id)
        {
            var entry = Find(id);
            if (entry == null) return null;
            var appearance = entry.ResolveAppearance();
            if (Portraits.TryGetValue(appearance.CacheKey, out var cached) && cached != null) return cached;
            // A revised portrait activates with its complete accepted body set;
            // incomplete upgrades retain the same identity's V11 portrait.
            var texture = Resources.Load<Texture2D>(appearance.ResourceFolder + "/portrait");
            if (texture == null) return null;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = entry.Id + "_portrait";
            Portraits[appearance.CacheKey] = sprite;
            return sprite;
        }
    }
}
