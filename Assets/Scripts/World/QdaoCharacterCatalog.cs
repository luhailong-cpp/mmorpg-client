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
        private const string V13Root = "World/Characters/QdaoRosterV13";
        private static int _resourceRevision;
        private static readonly string[] Directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        /// <summary>A complete, immutable visual contract, selected before loading any animation.</summary>
        public sealed class Appearance
        {
            public string Id { get; }
            public int Version { get; }
            public int AlignmentVersion { get; }
            public string ResourceFolder { get; }
            public int FrameCount => Version == 13 ? 16 : Version == 12 ? 8 : 4;
            public int FrameDurationMs => Version == 13 ? 30 : Version == 12 ? 60 : 120;
            public float FramesPerSecond => 1000f / FrameDurationMs;
            /// <summary>
            /// V12 and V13 always ship eight standing textures. A V11 character has them
            /// too once idle/&lt;DIR&gt;.png (512x512) exists for all eight directions
            /// under its V11 folder: the standing poses drawn for the V12 round are
            /// published early for characters whose costume did not change, so a
            /// stopped actor no longer freezes on walk frame 01 (a full stride in
            /// 47 of the 64 V11 direction sets, 2026-09-13). Only a complete
            /// set is cached; partial imports remain eligible for another probe.
            /// </summary>
            public bool HasDedicatedIdle => Version >= 12 || (_v11Idle == true || ProbeV11Idle());
            private bool? _v11Idle;
            private bool ProbeV11Idle()
            {
                foreach (var direction in Directions)
                {
                    var texture = Resources.Load<Texture2D>($"{ResourceFolder}/idle/{direction}");
                    if (texture == null || texture.width != 512 || texture.height != 512) return false;
                }
                _v11Idle = true;
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
                ResourceFolder = (version == 13 ? V13Root : version == 12 ? V12Root : V11Root) + "/" + id;
                CacheKey = id + "@v" + version + ":" + revision + ":contact" + contactFrame +
                           (alignmentVersion == 2 ? "" : ":alignment" + alignmentVersion) +
                           ":frames" + FrameCount + ":ms" + FrameDurationMs + ":resources" + _resourceRevision;
            }
            public string FrameResourcePath(string direction, int frameIndex)
                => $"{ResourceFolder}/walk/{direction}/{frameIndex + 1:00}";
            public string IdleResourcePath(string direction)
                => HasDedicatedIdle ? $"{ResourceFolder}/idle/{direction}" : FrameResourcePath(direction, ContactFrame);
            public string StripResourcePath(string direction)
                => Version >= 12 ? $"{ResourceFolder}/walk/{direction}/strip" : $"{ResourceFolder}/walk_{direction}";
        }

        public sealed class Definition
        {
            public string Id { get; }
            public string Name { get; }
            public Appearance BaselineAppearance { get; private set; }
            private Appearance _appearance;
            private string _v13ActivationText;
            private string _v12ActivationText;
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
                => ResolveAppearance(LoadActivationText, TextureMatches);

            /// <summary>Resource providers also support deterministic import/reload verification.</summary>
            public Appearance ResolveAppearance(Func<string, string> metadata,
                Func<string, int, int, bool> validTexture)
            {
                var v13 = metadata($"{V13Root}/{Id}/appearance");
                var v12 = metadata($"{V12Root}/{Id}/appearance");
                if (_appearance != null && _v13ActivationText == v13 && _v12ActivationText == v12)
                    return _appearance;
                var selected = SelectAppearance(this, v13, v12, validTexture);
                _v13ActivationText = v13;
                _v12ActivationText = v12;
                // Retry an unfinished higher-priority import even when a complete
                // V12 is usable. Never memoize that temporary fallback as final.
                _appearance = selected.Version == 13 ||
                              (string.IsNullOrEmpty(v13) && (selected.Version == 12 || string.IsNullOrEmpty(v12)))
                    ? selected : null;
                return selected;
            }
            internal void Invalidate()
            {
                _appearance = null;
                _v13ActivationText = _v12ActivationText = null;
                BaselineAppearance = new Appearance(Id, 11);
            }

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

        /// <summary>Retained V12-only selector; V12 always means exactly eight 60ms poses.</summary>
        public static Appearance SelectAppearance(Definition definition, string metadataJson,
            Func<string, int, int, bool> validTexture)
            => SelectAppearance(definition, null, metadataJson, validTexture);

        /// <summary>Select a complete approved V13, then same-ID V12, then same-ID V11.</summary>
        public static Appearance SelectAppearance(Definition definition, string v13Metadata, string v12Metadata,
            Func<string, int, int, bool> validTexture)
        {
            if (definition == null) return null;
            return SelectApprovedVersion(definition, 13, v13Metadata, validTexture) ??
                   SelectApprovedVersion(definition, 12, v12Metadata, validTexture) ?? definition.BaselineAppearance;
        }

        private static Appearance SelectApprovedVersion(Definition definition, int version, string json,
            Func<string, int, int, bool> validTexture)
        {
            if (string.IsNullOrWhiteSpace(json) || validTexture == null) return null;
            Activation record;
            try { record = JsonUtility.FromJson<Activation>(json); }
            catch (ArgumentException) { return null; }
            if (record == null || record.version != version || record.characterId != definition.Id ||
                record.frameCount != (version == 13 ? 16 : 8) ||
                record.frameDurationMs != (version == 13 ? 30 : 60) || !record.dedicatedIdle ||
                (version == 13 ? record.contactFrame != 0 : record.contactFrame != 0 && record.contactFrame != 4) ||
                record.status != "passed" || record.visualReview != "passed" ||
                !IsSha256(record.manifest_sha256) || !IsSha256(record.qc_sha256) || !IsSha256(record.validation_sha256))
                return null;
            // Only V12 accepts the absent/legacy v2 alignment field. V13 requires v3.
            var alignmentVersion = record.alignmentVersion == 0 ? 2 : record.alignmentVersion;
            if (version == 13 ? alignmentVersion != 3 : alignmentVersion != 2 && alignmentVersion != 3)
                return null;
            var revision = record.manifest_sha256 + ":qc" + record.qc_sha256 + ":validation" + record.validation_sha256;
            var candidate = new Appearance(definition.Id, version, record.contactFrame, revision, alignmentVersion);
            if (!validTexture(candidate.ResourceFolder + "/portrait", 1024, 1024)) return null;
            foreach (var direction in Directions)
            {
                if (!validTexture(candidate.IdleResourcePath(direction), 512, 512)) return null;
                for (var frame = 0; frame < candidate.FrameCount; frame++)
                    if (!validTexture(candidate.FrameResourcePath(direction, frame), 512, 512)) return null;
            }
            return candidate;
        }

        /// <summary>Used when an accepted set loses a resource between inventory and actual load.</summary>
        public static Appearance SelectFallbackAppearance(Appearance rejected, string v12Metadata,
            Func<string, int, int, bool> validTexture)
        {
            var definition = rejected == null ? null : Find(rejected.Id);
            if (definition == null || rejected.Version <= 11) return null;
            return rejected.Version == 13
                ? SelectAppearance(definition, null, v12Metadata, validTexture)
                : definition.BaselineAppearance;
        }

        internal static Appearance ResolveFallbackAppearance(Appearance rejected)
        {
            // A resource vanished after selection. Other users (including the
            // portrait and battle loader) must reselect instead of keeping V13 cached.
            Find(rejected.Id)?.Invalidate();
            return SelectFallbackAppearance(rejected, LoadActivationText($"{V12Root}/{rejected.Id}/appearance"), TextureMatches);
        }

        private static string LoadActivationText(string path) => Resources.Load<TextAsset>(path)?.text;

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
            _resourceRevision++;
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
            // Recheck the actual portrait before using its sprite cache as imports
            // may complete or disappear after the catalog selected an appearance.
            var texture = Resources.Load<Texture2D>(appearance.ResourceFolder + "/portrait");
            while (texture == null || texture.width != 1024 || texture.height != 1024)
            {
                appearance = ResolveFallbackAppearance(appearance);
                if (appearance == null) return null;
                texture = Resources.Load<Texture2D>(appearance.ResourceFolder + "/portrait");
            }
            if (Portraits.TryGetValue(appearance.CacheKey, out var cached) && cached != null && cached.texture == texture)
                return cached;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = entry.Id + "_portrait";
            Portraits[appearance.CacheKey] = sprite;
            return sprite;
        }
    }
}
