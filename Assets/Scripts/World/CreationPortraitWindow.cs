using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.World
{
    /// <summary>Owns only the current character and its two neighbours. The UI schedules each load.</summary>
    public sealed class CreationPortraitWindow : IDisposable
    {
        private readonly Func<string, QdaoCharacterCatalog.CreationPortraitLease> _acquire;
        private readonly Func<int> _resourceRevision;
        private readonly Dictionary<string, QdaoCharacterCatalog.CreationPortraitLease> _portraits = new(StringComparer.Ordinal);
        private readonly HashSet<string> _wanted = new(StringComparer.Ordinal);
        private int _revision;
        private bool _disposed;

        public int Count => _portraits.Count;
        public IReadOnlyCollection<string> CachedIds => _portraits.Keys;

        public CreationPortraitWindow(Func<string, QdaoCharacterCatalog.CreationPortraitLease> acquire = null,
            Func<int> resourceRevision = null)
        {
            _acquire = acquire ?? QdaoCharacterCatalog.AcquireCreationPortrait;
            _resourceRevision = resourceRevision ?? (() => QdaoCharacterCatalog.ResourceRevision);
            _revision = _resourceRevision();
        }

        public void SetWindow(string currentId, string previousId, string nextId)
        {
            if (_disposed) return;
            CheckRevision();
            _wanted.Clear();
            AddWanted(currentId);
            AddWanted(previousId);
            AddWanted(nextId);
            var removed = new List<string>();
            foreach (var id in _portraits.Keys) if (!_wanted.Contains(id)) removed.Add(id);
            // Release before loading the new neighbour so the window never owns a fourth entry.
            foreach (var id in removed) Release(id);
        }

        public Sprite GetOrLoad(string id)
        {
            if (_disposed || string.IsNullOrEmpty(id)) return null;
            CheckRevision();
            if (!_wanted.Contains(id)) return null;
            if (TryGet(id, out var sprite)) return sprite;
            var lease = _acquire(id);
            if (lease == null) return null;
            sprite = lease.Sprite;
            if (sprite == null) { lease.Dispose(); return null; }
            _portraits.Add(id, lease);
            return sprite;
        }

        public bool TryGet(string id, out Sprite sprite)
        {
            sprite = null;
            if (_disposed || string.IsNullOrEmpty(id)) return false;
            CheckRevision();
            if (!_portraits.TryGetValue(id, out var lease)) return false;
            sprite = lease.Sprite;
            if (sprite != null) return true;
            Release(id);
            return false;
        }

        public void Clear()
        {
            ReleaseAll();
            _wanted.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            Clear();
            _disposed = true;
        }

        private void AddWanted(string id)
        {
            if (!string.IsNullOrEmpty(id)) _wanted.Add(id);
        }

        private void CheckRevision()
        {
            var revision = _resourceRevision();
            if (_revision == revision) return;
            ReleaseAll();
            _revision = revision;
        }

        private void Release(string id)
        {
            if (!_portraits.TryGetValue(id, out var lease)) return;
            _portraits.Remove(id);
            lease.Dispose();
        }

        private void ReleaseAll()
        {
            var leases = new List<QdaoCharacterCatalog.CreationPortraitLease>(_portraits.Values);
            _portraits.Clear();
            foreach (var lease in leases) lease.Dispose();
        }
    }
}
