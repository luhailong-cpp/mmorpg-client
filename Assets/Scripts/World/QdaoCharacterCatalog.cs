using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
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
        public const string OriginalV13Root = "World/Characters/QdaoOriginalRosterV13";
        public const string OriginalV14Root = "World/Characters/QdaoOriginalRosterV14";
        private static int _resourceRevision;
        private static readonly string[] Directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        /// <summary>A complete, immutable visual contract, selected before loading any animation.</summary>
        public sealed class Appearance
        {
            public string Id { get; }
            public int Version { get; }
            public int AlignmentVersion { get; }
            public string ResourceFolder { get; }
            public bool IsOriginalRoster { get; }
            public bool IsHd => IsOriginalRoster && Version == 14;
            public int FrameWidth => IsHd ? 1024 : 512;
            public int FrameHeight => FrameWidth;
            public int PortraitWidth => 1024;
            public int PortraitHeight => 1024;
            public float PixelsPerUnit => IsHd ? 104f : 52f;
            public Vector2 Pivot => new Vector2(0.5f, 0.08f);
            public int FrameCount => Version == 13 || Version == 14 ? 16 : Version == 12 ? 8 : 4;
            public int FrameDurationMs => Version == 13 || Version == 14 ? 30 : Version == 12 ? 60 : 120;
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
            internal Appearance(string id, int version, int contactFrame = 0, string revision = "", int alignmentVersion = 2,
                string resourceRoot = null)
            {
                Id = id;
                Version = version;
                AlignmentVersion = alignmentVersion;
                ContactFrame = contactFrame;
                IsOriginalRoster = resourceRoot == OriginalV13Root || resourceRoot == OriginalV14Root;
                ResourceFolder = (resourceRoot ?? (version == 13 ? V13Root : version == 12 ? V12Root : V11Root)) + "/" + id;
                CacheKey = id + "@v" + version + ":" + revision + ":contact" + contactFrame +
                           (alignmentVersion == 2 ? "" : ":alignment" + alignmentVersion) +
                           ":frames" + FrameCount + ":ms" + FrameDurationMs + ":resources" + _resourceRevision +
                           (IsOriginalRoster ? ":original-roster" : "") + ":size" + FrameWidth + "x" + FrameHeight;
            }
            public string FrameResourcePath(string direction, int frameIndex)
                => $"{ResourceFolder}/walk/{direction}/{frameIndex + 1:00}";
            public string IdleResourcePath(string direction)
                => HasDedicatedIdle ? $"{ResourceFolder}/idle/{direction}" : FrameResourcePath(direction, ContactFrame);
            public string StripResourcePath(string direction)
                => IsHd ? null : Version >= 12 ? $"{ResourceFolder}/walk/{direction}/strip" : $"{ResourceFolder}/walk_{direction}";
        }

        public sealed class Definition
        {
            public string Id { get; }
            public string Name { get; }
            public bool IsOriginalRoster { get; }
            public Appearance BaselineAppearance { get; private set; }
            private Appearance _appearance;
            private string _v13ActivationText;
            private string _v12ActivationText;
            private string _originalManifestHash;
            private string _v14ActivationText;
            private string _originalHdManifestHash;
            private string _rejectedHdActivationText;
            private string _rejectedHdManifestHash;
            private string _rejectedHdCacheKey;
            public string ResourceFolder => ResolveAppearance()?.ResourceFolder;
            public int FrameCount => ResolveAppearance()?.FrameCount ?? 0;
            public float FramesPerSecond => ResolveAppearance()?.FramesPerSecond ?? 0f;
            public bool HasDedicatedIdle => ResolveAppearance()?.HasDedicatedIdle ?? false;
            public int Version => ResolveAppearance()?.Version ?? 0;
            public string FrameResourcePath(string direction, int frameIndex)
                => ResolveAppearance()?.FrameResourcePath(direction, frameIndex);
            public string IdleResourcePath(string direction) => ResolveAppearance()?.IdleResourcePath(direction);
            public Definition(string id, string name) : this(id, name, false) { }
            internal Definition(string id, string name, bool originalRoster)
            {
                Id = id;
                Name = name;
                IsOriginalRoster = originalRoster;
                BaselineAppearance = originalRoster ? null : new Appearance(id, 11);
            }
            public Appearance ResolveAppearance()
                => ResolveAppearance(LoadActivationText, TextureMatches, LoadResourceBytes, QdaoOriginalHdResourceIndex.IsComplete);

            /// <summary>Resource providers also support deterministic import/reload verification.</summary>
            public Appearance ResolveAppearance(Func<string, string> metadata,
                Func<string, int, int, bool> validTexture, Func<string, byte[]> resourceBytes = null,
                Func<string, string, string, bool> validHdIndex = null)
            {
                if (IsOriginalRoster)
                {
                    var folder = OriginalV13Root + "/" + Id;
                    var hdFolder = OriginalV14Root + "/" + Id;
                    var activation = metadata(folder + "/appearance");
                    var hdActivation = metadata(hdFolder + "/appearance");
                    byte[] Manifest(string path)
                    {
                        if (resourceBytes != null) return resourceBytes(path);
                        var value = metadata(path);
                        return value == null ? null : Encoding.UTF8.GetBytes(value);
                    }
                    var manifest = Manifest(folder + "/manifest");
                    var hdManifest = Manifest(hdFolder + "/manifest");
                    var manifestHash = HashBytes(manifest);
                    var hdManifestHash = HashBytes(hdManifest);
                    var hdActivationBytes = resourceBytes?.Invoke(hdFolder + "/appearance");
                    var hdActivationHash = hdActivationBytes != null ? HashBytes(hdActivationBytes) :
                        hdActivation == null ? null : HashBytes(Encoding.UTF8.GetBytes(hdActivation));
                    var rejectedHdRevision = !string.IsNullOrEmpty(hdActivation) && _rejectedHdActivationText == hdActivation &&
                        _rejectedHdManifestHash == hdManifestHash;
                    var hdInventoryValid = !rejectedHdRevision && (validHdIndex == null || validHdIndex(hdFolder,
                        hdManifestHash, hdActivationHash));
                    if (_appearance != null && _v13ActivationText == activation && _v14ActivationText == hdActivation &&
                        _originalManifestHash == manifestHash && _originalHdManifestHash == hdManifestHash &&
                        (!_appearance.IsHd || hdInventoryValid)) return _appearance;
                    _v13ActivationText = activation;
                    _v14ActivationText = hdActivation;
                    _originalManifestHash = manifestHash;
                    _originalHdManifestHash = hdManifestHash;
                    var originalSelection = (hdInventoryValid ? SelectOriginalHdAppearance(this, hdActivation, hdManifest, validTexture) : null) ??
                        SelectOriginalAppearance(this, activation, manifest, validTexture);
                    // An incomplete higher-priority HD import must be retried even while V13 is usable.
                    _appearance = originalSelection != null && (originalSelection.IsHd || string.IsNullOrEmpty(hdActivation) || rejectedHdRevision) ? originalSelection : null;
                    return originalSelection;
                }
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
                _v13ActivationText = _v12ActivationText = _originalManifestHash = _v14ActivationText = _originalHdManifestHash = null;
                _rejectedHdActivationText = _rejectedHdManifestHash = _rejectedHdCacheKey = null;
                BaselineAppearance = IsOriginalRoster ? null : new Appearance(Id, 11);
            }

            internal void RejectHdRevision(Appearance rejected)
            {
                // An actor may still hold an older lease while a new import is already selected elsewhere.
                // Its failed old load must never reject the newer approved revision for every consumer.
                if (_appearance != null && _appearance.IsHd && rejected != null &&
                    _appearance.CacheKey == rejected.CacheKey) RejectCurrentHdRevision();
            }

            internal bool HasRejectedHd(Appearance appearance) => appearance?.IsHd == true &&
                _rejectedHdCacheKey != null && _rejectedHdCacheKey == appearance.CacheKey;

            internal void RejectCurrentHdRevision()
            {
                var rejectedKey = _appearance?.CacheKey;
                var folder = OriginalV14Root + "/" + Id;
                var activation = _v14ActivationText ?? LoadActivationText(folder + "/appearance");
                var manifestHash = _originalHdManifestHash ?? HashBytes(LoadResourceBytes(folder + "/manifest"));
                Invalidate();
                _rejectedHdActivationText = activation;
                _rejectedHdManifestHash = manifestHash;
                _rejectedHdCacheKey = rejectedKey;
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
            public int cycleDurationMs;
            public int frameWidth;
            public int frameHeight;
            public int portraitWidth;
            public int portraitHeight;
            public float pixelsPerUnit;
            public float pivotX;
            public float pivotY;
            public string sourceCommit;
            public string sourceFamily;
        }

        [Serializable]
        private sealed class OriginalManifest
        {
            public int version;
            public string character_id;
            public string status;
            public string visual_review;
            public int frame_count;
            public int frame_duration_ms;
            public int cycle_duration_ms;
            public bool dedicated_idle;
            public int contact_frame;
            public OriginalAlignment alignment;
            public int[] frame_size;
            public int[] portrait_size;
            public OriginalRuntimeGeometry runtime_geometry;
        }

        [Serializable]
        private sealed class OriginalRuntimeGeometry
        {
            public int reference_frame_size;
            public float pixels_per_unit;
            public float[] pivot;
        }

        [Serializable]
        private sealed class OriginalAlignment
        {
            public int alignment_version;
            public int[] root_px;
        }

        /// <summary>Original 00-22 identities require their own approved, SHA-bound v2 manifest.</summary>
        public static Appearance SelectOriginalAppearance(Definition definition, string metadataJson,
            byte[] manifestBytes, Func<string, int, int, bool> validTexture)
        {
            if (definition == null || !definition.IsOriginalRoster) return null;
            return SelectApprovedVersion(definition, 13, metadataJson, validTexture, manifestBytes);
        }

        public static Appearance SelectOriginalHdAppearance(Definition definition, string metadataJson,
            byte[] manifestBytes, Func<string, int, int, bool> validTexture)
        {
            if (definition == null || !definition.IsOriginalRoster) return null;
            return SelectApprovedVersion(definition, 14, metadataJson, validTexture, manifestBytes);
        }

        private static bool OriginalManifestMatches(string id, int version, byte[] bytes, string expectedSha)
        {
            if (bytes == null || !string.Equals(HashBytes(bytes), expectedSha, StringComparison.OrdinalIgnoreCase)) return false;
            OriginalManifest manifest;
            try { manifest = JsonUtility.FromJson<OriginalManifest>(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF')); }
            catch (ArgumentException) { return false; }
            return manifest != null && manifest.version == version && manifest.character_id == id &&
                   manifest.status == "passed" && manifest.visual_review == "passed" &&
                   manifest.frame_count == 16 && manifest.frame_duration_ms == 30 && manifest.cycle_duration_ms == 480 &&
                   manifest.dedicated_idle && manifest.contact_frame == 0 &&
                   manifest.alignment != null && manifest.alignment.alignment_version == 2 &&
                   manifest.alignment.root_px != null && manifest.alignment.root_px.Length == 2 &&
                   manifest.alignment.root_px[0] == (version == 14 ? 512 : 256) &&
                   manifest.alignment.root_px[1] == (version == 14 ? 942 : 471) &&
                   (version != 14 || (manifest.frame_size != null && manifest.frame_size.Length == 2 &&
                    manifest.frame_size[0] == 1024 && manifest.frame_size[1] == 1024 &&
                    manifest.portrait_size != null && manifest.portrait_size.Length == 2 &&
                    manifest.portrait_size[0] == 1024 && manifest.portrait_size[1] == 1024 &&
                    manifest.runtime_geometry != null && manifest.runtime_geometry.reference_frame_size == 512 &&
                    Mathf.Abs(manifest.runtime_geometry.pixels_per_unit - 104f) < .0001f &&
                    manifest.runtime_geometry.pivot != null && manifest.runtime_geometry.pivot.Length == 2 &&
                    Mathf.Abs(manifest.runtime_geometry.pivot[0] - .5f) < .0001f &&
                    Mathf.Abs(manifest.runtime_geometry.pivot[1] - .08f) < .0001f));
        }

        private static string HashBytes(byte[] bytes)
        {
            if (bytes == null) return null;
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Retained V12-only selector; V12 always means exactly eight 60ms poses.</summary>
        public static Appearance SelectAppearance(Definition definition, string metadataJson,
            Func<string, int, int, bool> validTexture)
            => SelectAppearance(definition, null, metadataJson, validTexture);

        /// <summary>Select a complete approved V13, then same-ID V12, then same-ID V11.</summary>
        public static Appearance SelectAppearance(Definition definition, string v13Metadata, string v12Metadata,
            Func<string, int, int, bool> validTexture)
        {
            if (definition == null || definition.IsOriginalRoster) return null;
            return SelectApprovedVersion(definition, 13, v13Metadata, validTexture) ??
                   SelectApprovedVersion(definition, 12, v12Metadata, validTexture) ?? definition.BaselineAppearance;
        }

        private static Appearance SelectApprovedVersion(Definition definition, int version, string json,
            Func<string, int, int, bool> validTexture, byte[] originalManifest = null)
        {
            if (string.IsNullOrWhiteSpace(json) || validTexture == null) return null;
            Activation record;
            try { record = JsonUtility.FromJson<Activation>(json); }
            catch (ArgumentException) { return null; }
            if (record == null || record.version != version || record.characterId != definition.Id ||
                record.frameCount != (version >= 13 ? 16 : 8) ||
                record.frameDurationMs != (version >= 13 ? 30 : 60) || !record.dedicatedIdle ||
                (version >= 13 ? record.contactFrame != 0 : record.contactFrame != 0 && record.contactFrame != 4) ||
                record.status != "passed" || record.visualReview != "passed" ||
                !IsSha256(record.manifest_sha256) || !IsSha256(record.qc_sha256) || !IsSha256(record.validation_sha256))
                return null;
            // The original 00-22 pack uses feet-aligned v2. Lu's separate V13
            // family keeps its strict fixed-head v3 contract; neither relaxes the other.
            var alignmentVersion = record.alignmentVersion == 0 ? 2 : record.alignmentVersion;
            if (definition.IsOriginalRoster)
            {
                if ((version != 13 && version != 14) || record.alignmentVersion != 2 ||
                    !OriginalManifestMatches(definition.Id, version, originalManifest, record.manifest_sha256)) return null;
                if (version == 14 && (record.cycleDurationMs != 480 || record.frameWidth != 1024 || record.frameHeight != 1024 ||
                    record.portraitWidth != 1024 || record.portraitHeight != 1024 ||
                    record.pixelsPerUnit != 104f || record.pivotX != .5f || record.pivotY != .08f || record.sourceCommit != "9adcf9291e4a867601868889a5965f3cd48630ba" ||
                    record.sourceFamily != "original-00-22")) return null;
            }
            else if (version == 13 ? alignmentVersion != 3 : alignmentVersion != 2 && alignmentVersion != 3)
                return null;
            var revision = record.manifest_sha256 + ":qc" + record.qc_sha256 + ":validation" + record.validation_sha256;
            var candidate = new Appearance(definition.Id, version, record.contactFrame, revision, alignmentVersion,
                definition.IsOriginalRoster ? (version == 14 ? OriginalV14Root : OriginalV13Root) : null);
            if (!validTexture(candidate.ResourceFolder + "/portrait", candidate.PortraitWidth, candidate.PortraitHeight)) return null;
            foreach (var direction in Directions)
            {
                if (!validTexture(candidate.IdleResourcePath(direction), candidate.FrameWidth, candidate.FrameHeight)) return null;
                for (var frame = 0; frame < candidate.FrameCount; frame++)
                    if (!validTexture(candidate.FrameResourcePath(direction, frame), candidate.FrameWidth, candidate.FrameHeight)) return null;
            }
            return candidate;
        }

        /// <summary>Used when an accepted set loses a resource between inventory and actual load.</summary>
        public static Appearance SelectFallbackAppearance(Appearance rejected, string v12Metadata,
            Func<string, int, int, bool> validTexture)
        {
            var definition = rejected == null ? null : Find(rejected.Id);
            if (definition == null || rejected.Version <= 11 || rejected.IsOriginalRoster) return null;
            return rejected.Version == 13
                ? SelectAppearance(definition, null, v12Metadata, validTexture)
                : definition.BaselineAppearance;
        }

        internal static bool IsRejectedHdAppearance(Appearance appearance)
            => appearance != null && Find(appearance.Id)?.HasRejectedHd(appearance) == true;

        internal static Appearance ResolveFallbackAppearance(Appearance rejected)
        {
            // A resource vanished after selection. Other users (including the
            // portrait and battle loader) must reselect instead of keeping V13 cached.
            if (rejected.IsHd) Find(rejected.Id)?.RejectHdRevision(rejected);
            else Find(rejected.Id)?.Invalidate();
            if (rejected.IsOriginalRoster)
            {
                if (!rejected.IsHd) return null;
                return SelectOriginalAppearance(Find(rejected.Id),
                    LoadActivationText($"{OriginalV13Root}/{rejected.Id}/appearance"),
                    LoadResourceBytes($"{OriginalV13Root}/{rejected.Id}/manifest"), TextureMatches);
            }
            return SelectFallbackAppearance(rejected, LoadActivationText($"{V12Root}/{rejected.Id}/appearance"), TextureMatches);
        }

        private static string LoadActivationText(string path) => Resources.Load<TextAsset>(path)?.text;
        private static byte[] LoadResourceBytes(string path) => Resources.Load<TextAsset>(path)?.bytes;

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var character in value)
                if (!Uri.IsHexDigit(character)) return false;
            return true;
        }

        private static bool TextureMatches(string path, int width, int height)
        {
            if (path.StartsWith(OriginalV14Root + "/", StringComparison.Ordinal))
                return QdaoOriginalHdResourceIndex.TextureMatches(path, width, height);
            var texture = Resources.Load<Texture2D>(path);
            return texture != null && texture.width == width && texture.height == height;
        }

        /// <summary>Refresh after a completed resource import; identities stay stable and versioned caches remain separate.</summary>
        public static void RefreshAppearances()
        {
            _resourceRevision++;
            QdaoOriginalHdResourceIndex.ClearCache();
            foreach (var entry in Entries) entry.Invalidate();
            foreach (var entry in OriginalEntries) entry.Invalidate();
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
        private static readonly Definition[] OriginalEntries =
        {
            new("00_reference_topright_boy", "金发带Q道童", true),
            new("01_ice_sword_girl", "冰剑少女", true),
            new("02_fire_talisman_boy", "火符少年", true),
            new("03_lotus_healer_girl", "莲花医者", true),
            new("04_mountain_guardian_boy", "山岳守卫", true),
            new("05_celestial_musician_girl", "天音少女", true),
            new("06_thunder_caster_boy", "雷法少年", true),
            new("07_moon_shadow_assassin_girl", "月影少女", true),
            new("08_alchemy_prodigy_boy", "炼丹童子", true),
            new("09_bamboo_archer_girl", "竹弓少女", true),
            new("10_crimson_spear_girl", "赤枪少女", true),
            new("11_jade_fist_flat_top_boy", "玉拳少年", true),
            new("12_iron_saber_flat_top_boy", "铁刀少年", true),
            new("13_short_hair_wind_blade_girl", "风刃少女", true),
            new("14_short_hair_snow_summoner_girl", "唤雪少女", true),
            new("15_water_dragon_scholar_boy", "水龙书生", true),
            new("16_golden_bell_dancer_girl", "金铃舞者", true),
            new("17_ghost_script_calligrapher_boy", "灵篆书生", true),
            new("18_desert_sun_monk_girl", "沙海日轮少女", true),
            new("19_spirit_beast_tamer_boy", "御兽少年", true),
            new("20_star_formation_master_girl", "星阵少女", true),
            new("21_lidazui_hair_cook_boy", "厨道童子", true),
            new("22_lidazui_hair_waiter_saber_boy", "执刀小侍", true),
        };
        /// <summary>Original identities from restored commit 9adcf929; the second hero PNG is an alias, not a 24th identity.</summary>
        public static IReadOnlyList<Definition> OriginalAll { get; } = Array.AsReadOnly(OriginalEntries);

        /// <summary>Existing eight characters plus only fully approved/loaded originals; used by playable selection.</summary>
        public static IReadOnlyList<Definition> AvailableAll => SelectAvailableAppearances(entry => entry.ResolveAppearance());

        public static IReadOnlyList<Definition> SelectAvailableAppearances(Func<Definition, Appearance> resolve)
        {
            if (resolve == null) throw new ArgumentNullException(nameof(resolve));
            var result = new List<Definition>(Entries);
            foreach (var entry in OriginalEntries)
            {
                var appearance = resolve(entry);
                if (appearance != null && appearance.IsOriginalRoster && appearance.Id == entry.Id) result.Add(entry);
            }
            return result.AsReadOnly();
        }

        public static IReadOnlyList<Definition> All { get; } = Array.AsReadOnly(Entries);
        private static readonly Dictionary<string, Sprite> Portraits = new();

        public static Definition Find(string id)
        {
            foreach (var entry in Entries)
                if (entry.Id == id) return entry;
            foreach (var entry in OriginalEntries)
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
            if (appearance == null) return null;
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
