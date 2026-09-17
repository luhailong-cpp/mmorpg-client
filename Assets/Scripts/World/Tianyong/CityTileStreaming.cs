using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// Optional approved 4K layer above the existing fallback painting. Loads the complete
    /// camera footprint and a neighbour ring asynchronously, then releases distant assets.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityTileStreaming : MonoBehaviour
    {
        private const float GroundHeight = -0.04f;
        private const int MaxConcurrentLoads = 2;
        private const float ReleaseDelay = 2f;
        private const float RetryDelay = 10f;

        private sealed class Lease
        {
            public string Path;
            public ResourceRequest Request;
            public Texture2D Texture;
            public int References;
            public bool Completed;
        }

        private sealed class Tile
        {
            public Lease Lease;
            public GameObject Quad;
            public Material Material;
            public float LastWanted;
            public float RetryAfter;
            public bool Wanted;
        }

        // ResourceRequest is not cancellable. Shared leases ensure an old scene's completion
        // never unloads a texture now used by a newly entered scene or another preview.
        private static readonly Dictionary<string, Lease> Cache = new(StringComparer.OrdinalIgnoreCase);
        private sealed class TileSet
        {
            public string Path;
            public CityTileManifest Manifest;
            public Tile[] Tiles;
            public GameObject Root;
            public Action Activate;
        }

        private TileSet _active;
        private TileSet _pending;
        public CityTileManifest Manifest => _active?.Manifest ?? _pending?.Manifest;
        public bool IsTransitionPending => _pending != null;
        public int ResidentTileCount { get; private set; }
        public int PendingTileCount { get; private set; }

        public static string ManifestPath(string city, string variant)
            => $"{CityTileManifest.ResourceRoot}{city}/{variant}/manifest";

        public void Configure(string path, Action activate = null)
        {
            if (_active != null && string.Equals(_active.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                CancelPending();
                activate?.Invoke();
                return;
            }
            if (_pending != null && string.Equals(_pending.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                if (activate != null) _pending.Activate = activate;
                return;
            }
            CancelPending();
            var source = Resources.Load<TextAsset>(path);
            if (source == null)
            {
                // Preserve the legacy immediate background switch when no HD layer is active.
                // Once HD is active, an absent target must not flash a low-resolution replacement.
                if (_active == null) activate?.Invoke();
                else Debug.LogError($"[CityTileStreaming] Missing manifest {path}; current appearance retained.");
                return;
            }
            try
            {
                var manifest = JsonUtility.FromJson<CityTileManifest>(source.text);
                if (manifest == null || !manifest.Validate(out var error))
                    throw new InvalidOperationException("Invalid 4K manifest.");
                if (manifest.WorldRect != TianyongPaintedCity.PaintingWorldRect)
                    throw new InvalidOperationException("The navigation world rectangle cannot change.");
                if (path.StartsWith(CityTileManifest.ResourceRoot + "tianyong/", StringComparison.OrdinalIgnoreCase) &&
                    !manifest.legacyForegroundCompatible)
                    throw new InvalidOperationException("Existing foreground silhouettes and closest-zoom detail have not passed review.");
                Prepare(path, manifest, activate);
            }
            catch (Exception exception)
            { Debug.LogError($"[CityTileStreaming] Cannot prepare {path}: {exception.Message} Current appearance retained."); }
        }

        private void Prepare(string path, CityTileManifest manifest, Action activate)
        {
            var root = new GameObject("[City4K:preparing]");
            root.transform.SetParent(transform, false);
            root.SetActive(false);
            var set = new TileSet { Path = path, Manifest = manifest, Root = root,
                Tiles = new Tile[manifest.tiles.Length], Activate = activate };
            for (int i = 0; i < set.Tiles.Length; i++) set.Tiles[i] = new Tile();
            _pending = set;
        }

        /// <summary>Use the final camera footprint of this frame, including movement, zoom and aspect changes.</summary>
        public void Tick(Camera camera)
        {
            if ((_active == null && _pending == null) || camera == null || !isActiveAndEnabled) return;
            if (!TryGetView(camera, out var view)) return;
            UpdateTiles(_active, view, false);
            if (!UpdateTiles(_pending, view, true)) CancelPending();
            // Continue the current appearance while preparing the target. Current visible tiles
            // and target visible tiles both outrank optional old-layer neighbour prefetch.
            QueueTiles(_active, view, false);
            QueueTiles(_pending, view, false);
            QueueTiles(_active, view, true);
            TryCommitPending(view);
            ResidentTileCount = 0;
            PendingTileCount = 0;
            CountTiles(_active);
            CountTiles(_pending);
        }

        private bool UpdateTiles(TileSet set, Rect view, bool preparing)
        {
            if (set == null) return true;
            var manifest = set.Manifest;
            var wanted = manifest.VisibleRange(view, preparing ? 0 : 1);
            float now = Time.unscaledTime;
            for (int i = 0; i < set.Tiles.Length; i++)
            {
                var tile = set.Tiles[i];
                tile.Wanted = wanted.Contains(new Vector2Int(i % manifest.columns, i / manifest.columns));
                if (tile.Wanted) tile.LastWanted = now;
                else if (tile.Lease != null && now - tile.LastWanted >= ReleaseDelay) Release(tile);
                if (tile.Lease == null || !tile.Lease.Completed) continue;
                var texture = tile.Lease.Texture;
                if (texture == null || texture.width != manifest.tilePixels || texture.height != manifest.tilePixels)
                {
                    Debug.LogError($"[CityTileStreaming] Missing or non-4K tile {manifest.tiles[i]}; current appearance retained.");
                    if (preparing) return false;
                    Release(tile);
                    tile.RetryAfter = now + RetryDelay;
                    continue;
                }
                if (tile.Quad == null) CreateQuad(set, i, tile, texture);
            }
            return true;
        }

        private void QueueTiles(TileSet set, Rect view, bool includeNeighbours)
        {
            if (set == null) return;
            var manifest = set.Manifest;
            var wanted = manifest.VisibleRange(view, includeNeighbours ? 1 : 0);
            var visible = manifest.VisibleRange(view, 0);
            int inFlight = 0;
            foreach (var lease in Cache.Values) if (!lease.Completed) inFlight++;
            while (inFlight < MaxConcurrentLoads)
            {
                int candidate = -1;
                float best = float.PositiveInfinity;
                for (int i = 0; i < set.Tiles.Length; i++)
                {
                    var tile = set.Tiles[i];
                    int row = i / manifest.columns, column = i % manifest.columns;
                    if (!wanted.Contains(new Vector2Int(column, row)) || tile.Lease != null || tile.RetryAfter > Time.unscaledTime) continue;
                    float score = (manifest.TileWorldRect(row, column).center - view.center).sqrMagnitude;
                    if (!visible.Contains(new Vector2Int(column, row))) score += 100000000f;
                    if (score >= best) continue;
                    candidate = i;
                    best = score;
                }
                if (candidate < 0) break;
                var selected = set.Tiles[candidate];
                selected.LastWanted = Time.unscaledTime;
                selected.Lease = Acquire(manifest.tiles[candidate]);
                if (!selected.Lease.Completed) inFlight++;
            }
        }

        private bool TryCommitPending(Rect currentView)
        {
            var next = _pending;
            if (next == null) return false;
            var required = next.Manifest.VisibleRange(currentView, 0);
            if (required.width == 0 || required.height == 0) return false;
            for (int row = required.yMin; row < required.yMax; row++)
            for (int column = required.xMin; column < required.xMax; column++)
                if (next.Tiles[row * next.Manifest.columns + column].Quad == null) return false;
            // The callback updates the backing painting and runtime theme in this same frame.
            // Run it before hiding the old layer so a failed activation keeps the old HD root.
            try { next.Activate?.Invoke(); }
            catch (Exception exception)
            {
                Debug.LogError($"[CityTileStreaming] Appearance activation failed: {exception.Message}");
                if (_pending == next) CancelPending();
                return false;
            }
            if (_pending != next) return false; // A callback may cancel/reconfigure/dispose the map.
            var previous = _active;
            _active = next;
            _pending = null;
            next.Activate = null;
            next.Root.name = "[City4K:active]";
            if (previous?.Root != null) previous.Root.SetActive(false);
            next.Root.SetActive(true);
            ReleaseSet(previous);
            return true;
        }

        private void CountTiles(TileSet set)
        {
            if (set == null) return;
            foreach (var tile in set.Tiles)
            {
                if (tile.Quad != null) ResidentTileCount++;
                if (tile.Lease != null && !tile.Lease.Completed) PendingTileCount++;
            }
        }
        private bool TryGetView(Camera camera, out Rect view)
        {
            var plane = new Plane(transform.up, transform.TransformPoint(new Vector3(0f, GroundHeight, 0f)));
            Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
            for (int corner = 0; corner < 4; corner++)
            {
                var ray = camera.ViewportPointToRay(new Vector3(corner & 1, corner >> 1, 0f));
                if (!plane.Raycast(ray, out var distance)) { view = default; return false; }
                var point = transform.InverseTransformPoint(ray.GetPoint(distance));
                var flat = new Vector2(point.x, point.z);
                min = Vector2.Min(min, flat);
                max = Vector2.Max(max, flat);
            }
            view = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        private void CreateQuad(TileSet set, int index, Tile tile, Texture2D texture)
        {
            int row = index / set.Manifest.columns, column = index % set.Manifest.columns;
            var rect = set.Manifest.TileWorldRect(row, column);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"City4K_r{row + 1:00}_c{column + 1:00}";
            quad.transform.SetParent(set.Root.transform, false);
            quad.transform.localPosition = new Vector3(rect.center.x, GroundHeight, rect.center.y);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(rect.width, rect.height, 1f);
            var collider = quad.GetComponent<Collider>();
            collider.enabled = false;
            DestroyOwned(collider);
            var shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            var material = new Material(shader) { name = quad.name, mainTexture = texture };
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            tile.Material = material;
            tile.Quad = quad;
        }

        private static Lease Acquire(string path)
        {
            if (Cache.TryGetValue(path, out var existing)) { existing.References++; return existing; }
            var lease = new Lease { Path = path, References = 1 };
            Cache.Add(path, lease);
            lease.Request = Resources.LoadAsync<Texture2D>(path);
            lease.Request.completed += _ =>
            {
                lease.Texture = lease.Request.asset as Texture2D;
                lease.Completed = true;
                lease.Request = null;
                if (lease.References == 0) Evict(lease);
            };
            return lease;
        }

        private static void Evict(Lease lease)
        {
            if (lease.References > 0 || !lease.Completed) return;
            Cache.Remove(lease.Path);
            if (lease.Texture != null) Resources.UnloadAsset(lease.Texture);
            lease.Texture = null;
        }

        private static void Release(Tile tile)
        {
            if (tile.Material != null) tile.Material.mainTexture = null;
            DestroyOwned(tile.Quad);
            DestroyOwned(tile.Material);
            tile.Quad = null;
            tile.Material = null;
            if (tile.Lease != null)
            {
                tile.Lease.References--;
                Evict(tile.Lease);
                tile.Lease = null;
            }
        }

        private static void ReleaseSet(TileSet set)
        {
            if (set == null) return;
            set.Activate = null;
            if (set.Root != null) set.Root.SetActive(false);
            foreach (var tile in set.Tiles) Release(tile);
            DestroyOwned(set.Root);
        }

        private void CancelPending()
        {
            var abandoned = _pending;
            _pending = null;
            ReleaseSet(abandoned);
        }

        public void ReleaseAll()
        {
            CancelPending();
            var previous = _active;
            _active = null;
            ReleaseSet(previous);
            ResidentTileCount = PendingTileCount = 0;
        }
        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        private void OnDestroy() => ReleaseAll();
    }
}
