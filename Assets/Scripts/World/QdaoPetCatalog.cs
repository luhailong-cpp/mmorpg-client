using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.World
{
    /// <summary>Pet art identities are separate from player roles and persistent pet instance IDs.</summary>
    public static class QdaoPetCatalog
    {
        public const string ResourceRoot = QdaoActionResources.PetResourceRoot;
        public const string UnknownIdentity = "unmapped-pet";

        [Serializable]
        public sealed class Entry
        {
            public string id, displayName;
            public uint[] petTableIds, modelIds;
        }
        [Serializable] private sealed class Catalog { public int schemaVersion; public Entry[] pets; }
        private static Entry[] _entries;
        private static readonly Dictionary<string, Sprite> Portraits = new();
        public static IReadOnlyList<Entry> Entries
        {
            get
            {
                if (_entries != null) return _entries;
                var json = Resources.Load<TextAsset>(ResourceRoot + "/catalog");
                Catalog catalog = null;
                try { if (json != null) catalog = JsonUtility.FromJson<Catalog>(json.text); }
                catch (ArgumentException) { }
                _entries = catalog?.schemaVersion == 1 ? catalog.pets ?? Array.Empty<Entry>() : Array.Empty<Entry>();
                return _entries;
            }
        }
        public static Entry Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var entry in Entries) if (entry != null && entry.id == id) return entry;
            return null;
        }

        /// <summary>A known table ID is authoritative; conflicting/unknown table IDs never fall through to a model.</summary>
        public static Entry Resolve(uint petTableId, uint modelId = 0)
        {
            Entry match = null;
            foreach (var entry in Entries)
            {
                if (entry == null) continue;
                var ids = petTableId != 0 ? entry.petTableIds : entry.modelIds;
                var value = petTableId != 0 ? petTableId : modelId;
                if (value == 0 || ids == null || Array.IndexOf(ids, value) < 0) continue;
                if (match != null) return null; // Ambiguous configuration must not choose arbitrary art.
                match = entry;
            }
            if (match != null && petTableId != 0 && modelId != 0 &&
                (match.modelIds == null || Array.IndexOf(match.modelIds, modelId) < 0)) return null;
            return match;
        }

        public static QdaoActionResources.Lease Acquire(string id, string action, string direction)
            => Find(id) != null ? QdaoActionResources.Acquire(id, action, direction, ResourceRoot) : null;
        public static bool HasAction(string id, string action, string direction)
            => Find(id) != null && QdaoActionResources.GetDurationSeconds(id, action, direction, ResourceRoot) > 0f;
        public static Sprite LoadPortrait(string id)
        {
            if (Find(id) == null) return null;
            if (Portraits.TryGetValue(id, out var sprite) && sprite != null) return sprite;
            var texture = Resources.Load<Texture2D>(ResourceRoot + "/" + id + "/portrait");
            if (texture == null) return null;
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100f);
            sprite.name = id + "_portrait";
            Portraits[id] = sprite;
            return sprite;
        }
    }
}
