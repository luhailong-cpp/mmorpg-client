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
        private CityTileManifest _manifest;
        private Tile[] _tiles;
        private string _manifestPath;
        public CityTileManifest Manifest => _manifest;
        public int ResidentTileCount { get; private set; }
        public int PendingTileCount { get; private set; }

        public static string ManifestPath(string city, string variant)
            => $"{CityTileManifest.ResourceRoot}{city}/{variant}/manifest";

        public void Configure(string path)
        {
            if (_manifestPath == path) return;
            ReleaseAll();
            _manifestPath = path;
            var source = Resources.Load<TextAsset>(path);
            if (source == null) return; // A draft tile set must never replace the current city.
            try
            {
                var manifest = JsonUtility.FromJson<CityTileManifest>(source.text);
                if (manifest == null || !manifest.Validate(out var error))
                {
                    Debug.LogError($"[CityTileStreaming] Invalid manifest {path}; keeping fallback painting.");
                    return;
                }
                // Navigation and foreground still use the established world coordinates.
                if (manifest.WorldRect != TianyongPaintedCity.PaintingWorldRect)
                {
                    Debug.LogError($"[CityTileStreaming] {path} changes the navigation world rectangle; keeping fallback painting.");
                    return;
                }
                if (path.StartsWith(CityTileManifest.ResourceRoot + "tianyong/", StringComparison.OrdinalIgnoreCase) &&
                    !manifest.legacyForegroundCompatible)
                {
                    Debug.LogError($"[CityTileStreaming] {path} has not passed existing foreground silhouette review; keeping fallback painting.");
                    return;
                }
                _manifest = manifest;
                _tiles = new Tile[manifest.tiles.Length];
                for (int i = 0; i < _tiles.Length; i++) _tiles[i] = new Tile();
            }
            catch (Exception exception)
            { Debug.LogError($"[CityTileStreaming] Cannot read {path}: {exception.Message}"); }
        }

        /// <summary>Call after the world camera follows the actor, including after zoom/aspect changes.</summary>
        public void Tick(Camera camera)
        {
            if (_manifest == null || camera == null || !isActiveAndEnabled) return;
            if (!TryGetView(camera, out var view)) return;
            var visible = _manifest.VisibleRange(view, 0);
            var wanted = _manifest.VisibleRange(view, 1);
            float now = Time.unscaledTime;
            int pending = 0, resident = 0;
            for (int i = 0; i < _tiles.Length; i++)
            {
                var tile = _tiles[i];
                tile.Wanted = wanted.Contains(new Vector2Int(i % _manifest.columns, i / _manifest.columns));
                if (tile.Wanted) tile.LastWanted = now;
                else if (tile.Lease != null && now - tile.LastWanted >= ReleaseDelay) Release(tile);

                if (tile.Lease == null) continue;
                if (!tile.Lease.Completed) { pending++; continue; }
                var texture = tile.Lease.Texture;
                if (texture == null || texture.width != _manifest.tilePixels || texture.height != _manifest.tilePixels)
                {
                    Debug.LogError($"[CityTileStreaming] Missing or non-4K tile {_manifest.tiles[i]}; fallback retained.");
                    Release(tile);
                    tile.RetryAfter = now + RetryDelay;
                    continue;
                }
                if (tile.Quad == null) CreateQuad(i, tile, texture);
                resident++;
            }

            // On-screen tiles outrank the prefetch ring. Within each class, load nearest first.
            int inFlight = 0;
            foreach (var lease in Cache.Values) if (!lease.Completed) inFlight++;
            while (inFlight < MaxConcurrentLoads)
            {
                int candidate = -1;
                float best = float.PositiveInfinity;
                for (int i = 0; i < _tiles.Length; i++)
                {
                    var tile = _tiles[i];
                    if (!tile.Wanted || tile.Lease != null || tile.RetryAfter > now) continue;
                    int row = i / _manifest.columns, column = i % _manifest.columns;
                    var tileRect = _manifest.TileWorldRect(row, column);
                    float score = (tileRect.center - view.center).sqrMagnitude;
                    if (!visible.Contains(new Vector2Int(column, row))) score += 100000000f;
                    if (score >= best) continue;
                    candidate = i;
                    best = score;
                }
                if (candidate < 0) break;
                _tiles[candidate].Lease = Acquire(_manifest.tiles[candidate]);
                pending++;
                if (!_tiles[candidate].Lease.Completed) inFlight++;
            }
            ResidentTileCount = resident;
            PendingTileCount = pending;
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

        private void CreateQuad(int index, Tile tile, Texture2D texture)
        {
            int row = index / _manifest.columns, column = index % _manifest.columns;
            var rect = _manifest.TileWorldRect(row, column);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"City4K_r{row + 1:00}_c{column + 1:00}";
            quad.transform.SetParent(transform, false);
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

        public void ReleaseAll()
        {
            if (_tiles != null) foreach (var tile in _tiles) Release(tile);
            _tiles = null;
            _manifest = null;
            _manifestPath = null;
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
