using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.World
{
    /// <summary>
    /// Authored action deliveries are independent of the approved standing appearance.
    /// Only requested clips are loaded; world, battle and afterimages share explicit leases.
    /// All users of the dedicated action resource root must retain a lease while displaying a frame.
    /// Shared textures are unloaded only after every clip/afterimage using them has released ownership.
    /// </summary>
    public static class QdaoActionResources
    {
        public const string ResourceRoot = "World/Characters/QdaoArchivedActions20261005";
        public const string PetResourceRoot = "World/Pets/QdaoPets20261005";

        [Serializable]
        private sealed class Manifest
        {
            public int schemaVersion;
            public string characterId;
            public float pixelsPerUnit;
            public float pivotX, pivotY;
            public int frameWidth, frameHeight;
            public Clip[] clips;
        }

        [Serializable]
        private sealed class Clip
        {
            public string action, direction;
            public int frameCount;
            public float frameDurationMs;
            public float[] frameDurationsMs;
            public int eventFrame = -1;
            public bool overridePivot;
            public float pivotX, pivotY;
            public float pixelsPerUnit;
            public int frameWidth, frameHeight;
        }

        internal sealed class Entry
        {
            public string Key;
            public int Users, EventFrame;
            public Sprite[] Frames;
            public float[] Durations;
            public float Duration, EventTime;
            public Vector2 Pivot;
            public readonly List<Texture2D> Textures = new();
        }

        private sealed class TextureOwner { public int Users; public Action<Texture2D> Release; }

        private static readonly Dictionary<string, Manifest> Manifests = new();
        private static readonly Dictionary<string, Entry> Shared = new();
        private static readonly Dictionary<Sprite, Entry> SpriteOwners = new();
        private static readonly Dictionary<Texture2D, TextureOwner> TextureOwners = new();
        public static int ResidentClipCount => Shared.Count;
        public static int ResidentTextureCount => TextureOwners.Count;
        public static event Action<Sprite> SpriteReleased;

        public sealed class Lease : IDisposable
        {
            private Entry _entry;
            internal Lease(Entry entry) { _entry = entry; entry.Users++; }
            public Sprite[] Frames => _entry?.Frames;
            public float[] FrameDurationsSeconds => _entry?.Durations;
            public float Fps => _entry != null ? _entry.Frames.Length / _entry.Duration : 0f;
            public float DurationSeconds => _entry?.Duration ?? 0f;
            public Vector2 Pivot => _entry?.Pivot ?? Vector2.zero;
            public int EventFrame => _entry?.EventFrame ?? -1;
            public float EventTimeSeconds => _entry?.EventTime ?? -1f;
            public bool IsValid => _entry != null && ValidFrames(_entry);

            public Sprite FrameAt(float elapsedSeconds, bool loop)
            {
                if (_entry == null) return null;
                var time = Mathf.Max(0f, elapsedSeconds);
                if (loop) time = Mathf.Repeat(time, _entry.Duration);
                for (var frame = 0; frame < _entry.Frames.Length - 1; frame++)
                {
                    if (time < _entry.Durations[frame]) return _entry.Frames[frame];
                    time -= _entry.Durations[frame];
                }
                return _entry.Frames[_entry.Frames.Length - 1];
            }

            public void Dispose()
            {
                var entry = _entry;
                _entry = null;
                if (entry != null && --entry.Users == 0) Release(entry);
            }
        }

        public static Lease Acquire(string characterId, string action, string direction, string resourceRoot = ResourceRoot)
        {
            var manifest = LoadManifest(characterId, resourceRoot);
            return AcquireClip(manifest, characterId, action, direction, Resources.Load<Texture2D>, Resources.UnloadAsset, true, resourceRoot);
        }

        /// <summary>Metadata only: scheduling must not preload every actor's action textures.</summary>
        public static float GetDurationSeconds(string characterId, string action, string direction, string resourceRoot = ResourceRoot)
        {
            var clip = FindClip(LoadManifest(characterId, resourceRoot), action, direction);
            if (!TryDurations(clip, out _, out var duration, out _)) return 0f;
            return duration;
        }

        public static float GetEventTimeSeconds(string characterId, string action, string direction, string resourceRoot = ResourceRoot)
        {
            var clip = FindClip(LoadManifest(characterId, resourceRoot), action, direction);
            return TryDurations(clip, out _, out _, out var eventTime) ? eventTime : -1f;
        }

        public static Lease Retain(Sprite sprite)
            => sprite != null && SpriteOwners.TryGetValue(sprite, out var entry) ? new Lease(entry) : null;

        // A deterministic loader seam for lifecycle and cadence tests without changing delivered files.
        internal static Lease AcquireWithResources(string characterId, string action, string direction,
            string manifestJson, Func<string, Texture2D> load, bool shared)
            => AcquireClip(ParseManifest(characterId, manifestJson), characterId, action, direction, load, null, shared);

        internal static Lease AcquirePetWithResources(string petId, string action, string direction,
            string manifestJson, Func<string, Texture2D> load, bool shared)
            => AcquireClip(ParseManifest(petId, manifestJson), petId, action, direction, load, null, shared, PetResourceRoot);

        internal static Lease AcquireWithOwnedResources(string characterId, string action, string direction,
            string manifestJson, Func<string, Texture2D> load, Action<Texture2D> unload, bool shared)
            => AcquireClip(ParseManifest(characterId, manifestJson), characterId, action, direction, load, unload, shared);

        private static Lease AcquireClip(Manifest manifest, string characterId, string action, string direction,
            Func<string, Texture2D> load, Action<Texture2D> unload, bool shared, string resourceRoot = ResourceRoot)
        {
            var clip = FindClip(manifest, action, direction);
            if (!ValidRoot(resourceRoot) || load == null || !TryDurations(clip, out var durations, out var duration, out var eventTime)) return null;
            var key = resourceRoot + "/" + characterId + "/" + action + "/" + direction;
            if (shared && Shared.TryGetValue(key, out var cached))
            {
                if (ValidFrames(cached)) return new Lease(cached);
                Shared.Remove(key);
            }
            var pivot = clip.overridePivot ? new Vector2(clip.pivotX, clip.pivotY) : new Vector2(manifest.pivotX, manifest.pivotY);
            var ppu = clip.pixelsPerUnit > 0 ? clip.pixelsPerUnit : manifest.pixelsPerUnit;
            if (!FinitePositive(ppu) || !ValidPivot(pivot)) return null;
            var width = clip.frameWidth > 0 ? clip.frameWidth : manifest.frameWidth > 0 ? manifest.frameWidth : 1024;
            var height = clip.frameHeight > 0 ? clip.frameHeight : manifest.frameHeight > 0 ? manifest.frameHeight : 1024;
            if (width > 4096 || height > 4096) return null;
            var entry = new Entry { Key = shared ? key : null, Frames = new Sprite[clip.frameCount],
                Durations = durations, Duration = duration, EventFrame = clip.eventFrame, EventTime = eventTime, Pivot = pivot };
            var unique = new HashSet<Texture2D>();
            for (var frame = 0; frame < clip.frameCount; frame++)
            {
                var path = key + "/" + (frame + 1).ToString("00");
                var texture = load(path);
                if (texture != null)
                {
                    if (!TextureOwners.TryGetValue(texture, out var owner))
                        TextureOwners[texture] = owner = new TextureOwner { Release = unload };
                    owner.Users++;
                    entry.Textures.Add(texture);
                }
                if (texture == null || texture.width != width || texture.height != height || !unique.Add(texture))
                {
                    Release(entry);
                    return null;
                }
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), pivot, ppu, 0, SpriteMeshType.FullRect);
                sprite.name = characterId + "_archived_" + action + "_" + direction + "_" + (frame + 1).ToString("00");
                entry.Frames[frame] = sprite;
            }
            foreach (var sprite in entry.Frames) SpriteOwners.Add(sprite, entry);
            if (shared) Shared[key] = entry;
            return new Lease(entry);
        }

        private static bool ValidFrames(Entry entry)
        {
            foreach (var sprite in entry.Frames)
                if (sprite == null || sprite.texture == null) return false;
            return true;
        }

        private static Manifest LoadManifest(string characterId, string resourceRoot = ResourceRoot)
        {
            if (!ValidIdentity(characterId) || !ValidRoot(resourceRoot)) return null;
            var key = resourceRoot + "/" + characterId;
            if (Manifests.TryGetValue(key, out var cached)) return cached;
            var text = Resources.Load<TextAsset>(key + "/manifest");
            var manifest = ParseManifest(characterId, text != null ? text.text : null);
            Manifests[key] = manifest;
            return manifest;
        }

        private static Manifest ParseManifest(string characterId, string json)
        {
            if (!ValidIdentity(characterId) || string.IsNullOrEmpty(json)) return null;
            Manifest manifest;
            try { manifest = JsonUtility.FromJson<Manifest>(json); }
            catch (ArgumentException) { return null; }
            if (manifest == null || manifest.schemaVersion != 1 || manifest.characterId != characterId ||
                !FinitePositive(manifest.pixelsPerUnit) || !ValidPivot(new Vector2(manifest.pivotX, manifest.pivotY)) ||
                !ValidDimension(manifest.frameWidth) || !ValidDimension(manifest.frameHeight) ||
                manifest.clips == null) return null;
            var keys = new HashSet<string>();
            foreach (var clip in manifest.clips)
                if (clip == null || !ValidActionDirection(clip.action, clip.direction) ||
                    !ValidDimension(clip.frameWidth) || !ValidDimension(clip.frameHeight) ||
                    !keys.Add(clip.action + "/" + clip.direction) || !TryDurations(clip, out _, out _, out _)) return null;
            return manifest;
        }

        private static Clip FindClip(Manifest manifest, string action, string direction)
        {
            if (manifest == null || !ValidActionDirection(action, direction)) return null;
            foreach (var clip in manifest.clips)
                if (clip.action == action && clip.direction == direction) return clip;
            return null;
        }

        private static bool TryDurations(Clip clip, out float[] durations, out float duration, out float eventTime)
        {
            durations = null; duration = 0f; eventTime = -1f;
            if (clip == null || clip.frameCount < 1 || clip.frameCount > 256 ||
                clip.eventFrame < -1 || clip.eventFrame >= clip.frameCount) return false;
            var variable = clip.frameDurationsMs != null && clip.frameDurationsMs.Length > 0;
            if (variable && clip.frameDurationsMs.Length != clip.frameCount) return false;
            durations = new float[clip.frameCount];
            for (var i = 0; i < durations.Length; i++)
            {
                var ms = variable ? clip.frameDurationsMs[i] : clip.frameDurationMs;
                if (!FinitePositive(ms)) return false;
                if (i == clip.eventFrame) eventTime = duration;
                durations[i] = ms / 1000f;
                duration += durations[i];
            }
            return FinitePositive(duration);
        }

        private static bool ValidIdentity(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (var ch in value)
                if (!(ch >= 'a' && ch <= 'z') && !(ch >= '0' && ch <= '9') && ch != '_' && ch != '-') return false;
            return true;
        }
        private static bool ValidActionDirection(string action, string direction)
        {
            if (action == "run") return direction == "N" || direction == "NE" || direction == "E" ||
                direction == "SE" || direction == "S" || direction == "SW" || direction == "W" || direction == "NW";
            return (action == "idle" || action == "attack" || action == "hit" || action == "cast") && (direction == "E" || direction == "W");
        }
        private static bool ValidRoot(string root) => root == ResourceRoot || root == PetResourceRoot;
        private static bool ValidDimension(int value) => value >= 0 && value <= 4096;
        private static bool FinitePositive(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool ValidPivot(Vector2 pivot) => pivot.x >= 0f && pivot.x <= 1f && pivot.y >= 0f && pivot.y <= 1f;

        private static void Release(Entry entry)
        {
            if (entry.Key != null && Shared.TryGetValue(entry.Key, out var cached) && ReferenceEquals(cached, entry)) Shared.Remove(entry.Key);
            foreach (var sprite in entry.Frames)
            {
                if (ReferenceEquals(sprite, null)) continue;
                SpriteOwners.Remove(sprite);
                SpriteReleased?.Invoke(sprite);
                if (sprite == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(sprite); else UnityEngine.Object.DestroyImmediate(sprite);
            }
            Array.Clear(entry.Frames, 0, entry.Frames.Length);
            foreach (var texture in entry.Textures)
            {
                if (!TextureOwners.TryGetValue(texture, out var owner) || --owner.Users > 0) continue;
                TextureOwners.Remove(texture);
                if (texture != null) owner.Release?.Invoke(texture);
            }
            entry.Textures.Clear();
        }
    }
}
