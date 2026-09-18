using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.World
{
    /// <summary>Only leased HD directions are resident. World actors, battle images and afterimages share ownership.</summary>
    public static class QdaoHdResources
    {
        private static readonly string[] Directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        internal sealed class Entry
        {
            public string Key;
            public int Direction, Users;
            public QdaoCharacterCatalog.Appearance Appearance;
            public Sprite[] Walk;
            public Sprite Idle;
            public readonly List<Texture2D> Textures = new();
        }
        private sealed class TextureOwner { public int Users; public Action<Texture2D> Release; }
        private static readonly Dictionary<string, Entry> Shared = new();
        private static readonly Dictionary<Sprite, Entry> SpriteOwners = new();
        private static readonly Dictionary<Texture2D, TextureOwner> TextureOwners = new();
        public static event Action<Sprite> SpriteReleased;
        public static int ResidentDirectionCount => Entries.Count;
        public static int ResidentTextureCount => TextureOwners.Count;
        private static readonly HashSet<Entry> Entries = new();

        public sealed class Lease : IDisposable
        {
            private Entry _entry;
            internal Lease(Entry entry) { _entry = entry; entry.Users++; }
            public int Direction => _entry?.Direction ?? -1;
            public QdaoCharacterCatalog.Appearance Appearance => _entry?.Appearance;
            public Sprite[] Walk => _entry?.Walk;
            public Sprite Idle => _entry?.Idle;
            public bool IsValid => _entry != null && Matches(_entry.Idle) && Array.TrueForAll(_entry.Walk, Matches);
            private bool Matches(Sprite sprite) => sprite != null && sprite.texture != null &&
                sprite.texture.width == _entry.Appearance.FrameWidth && sprite.texture.height == _entry.Appearance.FrameHeight &&
                sprite.rect.width == _entry.Appearance.FrameWidth && sprite.rect.height == _entry.Appearance.FrameHeight &&
                sprite.pixelsPerUnit == _entry.Appearance.PixelsPerUnit;
            public void Dispose()
            {
                var entry = _entry; _entry = null;
                if (entry != null && --entry.Users == 0) Release(entry);
            }
        }

        public static Lease Acquire(QdaoCharacterCatalog.Appearance appearance, int direction)
            => AcquireWithResources(appearance, direction, Resources.Load<Texture2D>, texture => Resources.UnloadAsset(texture), true);

        /// <summary>Uses the same validated loader for deterministic in-memory lifecycle tests; never writes approved assets.</summary>
        internal static Lease AcquireWithResources(QdaoCharacterCatalog.Appearance appearance, int direction,
            Func<string, Texture2D> load, Action<Texture2D> unload, bool shared)
        {
            if (appearance?.IsHd != true || direction < 0 || direction >= Directions.Length || load == null ||
                QdaoCharacterCatalog.IsRejectedHdAppearance(appearance)) return null;
            var key = appearance.CacheKey + ":direction" + direction;
            if (shared && Shared.TryGetValue(key, out var cached))
            {
                var lease = new Lease(cached);
                if (lease.IsValid) return lease;
                lease.Dispose();
                // Existing users keep their own lease; this broken entry is no longer reused.
                Shared.Remove(key);
            }
            var entry = new Entry { Key = shared ? key : null, Appearance = appearance, Direction = direction,
                Walk = new Sprite[appearance.FrameCount] };
            var textures = new HashSet<Texture2D>();
            bool Take(string path)
            {
                var texture = load(path);
                if (texture == null) return false;
                RetainTexture(texture, unload);
                entry.Textures.Add(texture);
                return texture.width == appearance.FrameWidth && texture.height == appearance.FrameHeight && textures.Add(texture);
            }
            for (var frame = 0; frame < appearance.FrameCount; frame++)
                if (!Take(appearance.FrameResourcePath(Directions[direction], frame))) { Release(entry); return null; }
            if (!Take(appearance.IdleResourcePath(Directions[direction]))) { Release(entry); return null; }
            for (var frame = 0; frame < appearance.FrameCount; frame++)
                entry.Walk[frame] = Create(entry.Textures[frame], appearance, appearance.Id + "_run_" + Directions[direction] + "_" + frame.ToString("00"));
            entry.Idle = Create(entry.Textures[appearance.FrameCount], appearance, appearance.Id + "_idle_" + Directions[direction] + "_00");
            foreach (var sprite in entry.Walk) SpriteOwners.Add(sprite, entry);
            SpriteOwners.Add(entry.Idle, entry);
            Entries.Add(entry);
            if (shared) Shared[key] = entry;
            return new Lease(entry);
        }

        public static Lease RetainSprite(Sprite sprite)
            => sprite != null && SpriteOwners.TryGetValue(sprite, out var entry) ? new Lease(entry) : null;

        private static Sprite Create(Texture2D texture, QdaoCharacterCatalog.Appearance appearance, string name)
        {
            var sprite = Sprite.Create(texture, new Rect(0, 0, appearance.FrameWidth, appearance.FrameHeight),
                appearance.Pivot, appearance.PixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            return sprite;
        }

        private static void RetainTexture(Texture2D texture, Action<Texture2D> release)
        {
            if (!TextureOwners.TryGetValue(texture, out var owner))
                TextureOwners[texture] = owner = new TextureOwner { Release = release };
            owner.Users++;
        }

        private static void Release(Entry entry)
        {
            Entries.Remove(entry);
            if (entry.Key != null && Shared.TryGetValue(entry.Key, out var cached) && ReferenceEquals(cached, entry)) Shared.Remove(entry.Key);
            void ReleaseSprite(Sprite sprite)
            {
                if (ReferenceEquals(sprite, null)) return;
                SpriteOwners.Remove(sprite);
                SpriteReleased?.Invoke(sprite);
                if (sprite == null) return;
                if (Application.isPlaying) UnityEngine.Object.Destroy(sprite); else UnityEngine.Object.DestroyImmediate(sprite);
            }
            foreach (var sprite in entry.Walk) ReleaseSprite(sprite);
            ReleaseSprite(entry.Idle);
            foreach (var texture in entry.Textures)
            {
                if (!TextureOwners.TryGetValue(texture, out var owner)) continue;
                if (--owner.Users != 0) continue;
                TextureOwners.Remove(texture);
                if (texture != null) owner.Release?.Invoke(texture);
            }
            entry.Textures.Clear();
        }
    }
}
