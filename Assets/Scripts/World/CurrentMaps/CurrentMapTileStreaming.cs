using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.World.CurrentMaps
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>在新布局底图上按需覆盖已完成分片；缺片不阻断进图或其它分片。</summary>
    [DisallowMultipleComponent]
    public sealed class CurrentMapTileStreaming : MonoBehaviour
    {
        // 六片共享预算包含尚未完成的请求，不能在换图时额外叠加一组预加载。
        public const int MaxResidentAndPendingTiles = 6;
        public const int MaxConcurrentLoads = 2;
        private const float GroundHeight = -.04f;
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
            public CurrentMapTilesManifest.Entry Entry;
            public Rect Rect;
            public Lease Lease;
            public GameObject Quad;
            public Material Material;
            public float LastWanted;
            public float RetryAfter;
            public bool Wanted;
            public bool Visible;
            public float Score;
        }

        // 请求不可取消。无引用的在途请求仍占全局预算，完成后立即释放，跨图不会突破上限。
        private static readonly Dictionary<string, Lease> Cache = new(StringComparer.Ordinal);
        private Tile[] _tiles = Array.Empty<Tile>();
        private readonly List<Tile> _ranked = new();
        private readonly List<int> _candidates = new();
        private CurrentMapHexTileIndex _hexIndex;
        private Matrix4x4 _hexIndexTransform;
        private Vector2? _focusPoint;
        private Vector2 _prefetchAnchor;
        private Vector2 _prefetchDirection;
        private bool _hasPrefetchAnchor;
        public CurrentMapTilesManifest Manifest { get; private set; }
        public int ResidentTileCount { get; private set; }
        public int PendingTileCount { get; private set; }
        public static int GlobalLeaseCount => Cache.Count;
        public static int GlobalPendingCount
        {
            get { int count = 0; foreach (var lease in Cache.Values) if (!lease.Completed) count++; return count; }
        }

        public void Configure(string key)
        {
            ReleaseAll();
            string root = CurrentMapTilesManifest.ResourceRoot + key + "/";
            var manifestText = Resources.Load<TextAsset>(root + "tiles");
            if (manifestText == null) return;
            try
            {
                var source = Resources.Load<TextAsset>(root + "source");
                if (source == null) throw new InvalidOperationException("当前布局 source.json 缺失。");
                Prepare(CurrentMapTilesManifest.Parse(key, source.text, manifestText.text));
            }
            catch (Exception error)
            { Debug.LogError($"[CurrentMapTiles] {key}: {error.Message} 保留当前布局底图。"); }
        }

        private void Prepare(CurrentMapTilesManifest manifest)
        {
            Manifest = manifest;
            _hasPrefetchAnchor = false;
            _prefetchDirection = Vector2.zero;
            _focusPoint = null;
            _tiles = new Tile[manifest.tiles.Length];
            for (int i = 0; i < _tiles.Length; i++)
                _tiles[i] = new Tile { Entry = manifest.tiles[i], Rect = manifest.TileWorldRect(manifest.tiles[i].row, manifest.tiles[i].column) };
            RebuildHexIndex(Matrix4x4.identity);
        }

        private void RebuildHexIndex(Matrix4x4 localToWorld)
        {
            // 只索引原有 16×16 / 14×14 分片的矩形元数据，不加载图片或重切整张地图。
            var rectangles = new Rect[_tiles.Length];
            for (int i = 0; i < _tiles.Length; i++) rectangles[i] = _tiles[i].Rect;
            _hexIndex = new CurrentMapHexTileIndex(rectangles, localToWorld);
            _hexIndexTransform = localToWorld;
        }

        public void Tick(Camera camera, Vector3? worldFocus = null)
        {
            if (Manifest == null || camera == null || !isActiveAndEnabled || !TryGetView(camera, out var view)) return;
            var localToWorld = transform.localToWorldMatrix;
            if (_hexIndexTransform != localToWorld) RebuildHexIndex(localToWorld);
            if (worldFocus.HasValue)
            {
                var local = transform.InverseTransformPoint(worldFocus.Value);
                _focusPoint = new Vector2(local.x, local.z);
            }
            else _focusPoint = null;
            TickView(view, Time.unscaledTime);
        }

        private void TickView(Rect view, float now)
        {
            UpdateCompleted(now);
            SelectTiles(view, now);
            foreach (var tile in _tiles)
                if (!tile.Wanted && tile.Lease != null && now - tile.LastWanted >= ReleaseDelay) Release(tile);
            foreach (var tile in _ranked)
            {
                if (!tile.Wanted || tile.Lease != null) continue;
                try
                {
                    MakeRoomForVisibleTile(tile, view.center);
                    var lease = Acquire(tile.Entry.resourcePath);
                    if (lease == null) continue;
                    tile.Lease = lease;
                    tile.LastWanted = now;
                }
                catch (Exception error)
                {
                    tile.RetryAfter = now + RetryDelay;
                    Debug.LogError($"[CurrentMapTiles] 请求分片失败：{tile.Entry.resourcePath}: {error.Message}");
                }
            }
            ResidentTileCount = PendingTileCount = 0;
            foreach (var tile in _tiles)
            {
                if (tile.Quad != null) ResidentTileCount++;
                if (tile.Lease != null && !tile.Lease.Completed) PendingTileCount++;
            }
        }

        private void SelectTiles(Rect view, float now)
        {
            _ranked.Clear();
            float width = Manifest.worldRect.width / Manifest.columns;
            float height = Manifest.worldRect.height / Manifest.rows;
            UpdatePrefetchDirection(view.center, Math.Min(width, height));
            var prefetchCenter = view.center + new Vector2(_prefetchDirection.x * width, _prefetchDirection.y * height) * .75f;
            foreach (var tile in _tiles)
            {
                tile.Wanted = false;
                tile.Visible = tile.Rect.Overlaps(view);
            }
            _hexIndex.Query(view, _focusPoint ?? view.center, _candidates);
            foreach (int index in _candidates)
            {
                var tile = _tiles[index];
                if (tile.RetryAfter > now) continue;
                bool visible = tile.Visible;
                // 六边形确定候选区域，纹理预算独立计数；可见块优先，再预取前进方向的邻片。
                float distance = (tile.Rect.center - (visible ? view.center : prefetchCenter)).sqrMagnitude;
                // 同优先级已驻留块有小幅滞回，避免相机在排名分界附近抖动时反复卸载。
                tile.Score = distance * (tile.Lease == null ? 1f : .85f) + (visible ? 0f : 100000000f);
                _ranked.Add(tile);
            }
            _ranked.Sort((a, b) =>
            {
                int score = a.Score.CompareTo(b.Score);
                return score != 0 ? score : string.CompareOrdinal(a.Entry.resourcePath, b.Entry.resourcePath);
            });
            for (int i = 0; i < Math.Min(MaxResidentAndPendingTiles, _ranked.Count); i++)
            { _ranked[i].Wanted = true; _ranked[i].LastWanted = now; }
        }

        private bool MakeRoomForVisibleTile(Tile requested, Vector2 viewCenter)
        {
            if (!requested.Visible || Cache.Count < MaxResidentAndPendingTiles ||
                GlobalPendingCount >= MaxConcurrentLoads || Cache.ContainsKey(requested.Entry.resourcePath)) return false;
            Tile victim = null;
            float farthest = float.NegativeInfinity;
            foreach (var tile in _tiles)
            {
                // 只有真正能释放预算的独占完成块可以提前让位；在途请求和其它地图引用继续计数。
                if (tile.Wanted || tile.Lease == null || !tile.Lease.Completed || tile.Lease.References != 1) continue;
                float score = (tile.Rect.center - viewCenter).sqrMagnitude + (tile.Visible ? 0f : 100000000f);
                if (score <= farthest) continue;
                victim = tile;
                farthest = score;
            }
            if (victim == null) return false;
            // 无压力时仍保留两秒滞后；满额时新可见块不应被已不需要的完成块挡住。
            Release(victim);
            return true;
        }

        private void UpdatePrefetchDirection(Vector2 center, float tileSpan)
        {
            if (!_hasPrefetchAnchor)
            {
                _prefetchAnchor = center;
                _hasPrefetchAnchor = true;
                return;
            }
            var delta = center - _prefetchAnchor;
            // 累积移动超过一小段格宽才换方向，避免相机微抖导致邻片反复替换。
            if (delta.sqrMagnitude < tileSpan * tileSpan * .0025f) return;
            // 传送不代表持续前进方向，落点先按距离预取。
            _prefetchDirection = delta.sqrMagnitude > tileSpan * tileSpan * 4f ? Vector2.zero : delta.normalized;
            _prefetchAnchor = center;
        }

        private void UpdateCompleted(float now)
        {
            foreach (var tile in _tiles)
            {
                if (tile.Lease == null || !tile.Lease.Completed || tile.Quad != null) continue;
                var texture = tile.Lease.Texture;
                if (texture == null || texture.width != 4096 || texture.height != 4096 ||
                    texture.format != TextureFormat.RGB24 || texture.mipmapCount != 1)
                {
                    Debug.LogError($"[CurrentMapTiles] 分片缺失或导入格式错误：{tile.Entry.resourcePath}；保留当前布局底图。");
                    Release(tile);
                    tile.RetryAfter = now + RetryDelay;
                    continue;
                }
                try { CreateQuad(tile, texture); }
                catch (Exception error)
                {
                    Debug.LogError($"[CurrentMapTiles] 创建分片失败：{tile.Entry.resourcePath}: {error.Message}");
                    Release(tile);
                    tile.RetryAfter = now + RetryDelay;
                }
            }
        }

        private bool TryGetView(Camera camera, out Rect view)
        {
            var plane = new Plane(transform.up, transform.TransformPoint(new Vector3(0f, GroundHeight, 0f)));
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int corner = 0; corner < 4; corner++)
            {
                var ray = camera.ViewportPointToRay(new Vector3(corner & 1, corner >> 1, 0f));
                if (!plane.Raycast(ray, out float distance)) { view = default; return false; }
                var point = transform.InverseTransformPoint(ray.GetPoint(distance));
                var flat = new Vector2(point.x, point.z);
                min = Vector2.Min(min, flat);
                max = Vector2.Max(max, flat);
            }
            view = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        private void CreateQuad(Tile tile, Texture2D texture)
        {
            var shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (shader == null) throw new InvalidOperationException("缺少分片纹理 Shader。");
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            tile.Quad = quad;
            quad.name = $"CurrentMapTile_r{tile.Entry.row:00}_c{tile.Entry.column:00}";
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = new Vector3(tile.Rect.center.x, GroundHeight, tile.Rect.center.y);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(tile.Rect.width, tile.Rect.height, 1f);
            var collider = quad.GetComponent<Collider>();
            collider.enabled = false;
            DestroyOwned(collider);
            tile.Material = new Material(shader) { name = quad.name, mainTexture = texture,
                renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry };
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = tile.Material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Lease Acquire(string path)
        {
            if (Cache.TryGetValue(path, out var shared)) { shared.References++; return shared; }
            if (Cache.Count >= MaxResidentAndPendingTiles || GlobalPendingCount >= MaxConcurrentLoads) return null;
            var lease = new Lease { Path = path, References = 1 };
            Cache.Add(path, lease);
            try
            {
                lease.Request = Resources.LoadAsync<Texture2D>(path);
                lease.Request.completed += _ =>
                {
                    lease.Texture = lease.Request.asset as Texture2D;
                    lease.Completed = true;
                    lease.Request = null;
                    Evict(lease);
                };
            }
            catch { Cache.Remove(path); throw; }
            return lease;
        }

        private static void Evict(Lease lease)
        {
            if (lease.References != 0 || !lease.Completed) return;
            Cache.Remove(lease.Path);
            if (lease.Texture != null) Resources.UnloadAsset(lease.Texture);
            lease.Texture = null;
        }

        private static void Release(Tile tile)
        {
            if (tile.Material != null) tile.Material.mainTexture = null;
            if (tile.Quad != null) tile.Quad.SetActive(false);
            DestroyOwned(tile.Quad);
            DestroyOwned(tile.Material);
            tile.Quad = null;
            tile.Material = null;
            if (tile.Lease == null) return;
            tile.Lease.References--;
            Evict(tile.Lease);
            tile.Lease = null;
        }

        public void ReleaseAll()
        {
            foreach (var tile in _tiles) Release(tile);
            _tiles = Array.Empty<Tile>();
            _ranked.Clear();
            _candidates.Clear();
            _hexIndex = null;
            _focusPoint = null;
            Manifest = null;
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
