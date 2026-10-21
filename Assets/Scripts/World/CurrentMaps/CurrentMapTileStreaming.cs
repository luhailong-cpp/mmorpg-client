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
        public const int MaxResidentAndPendingTiles = 8;
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
            public float Score;
        }

        // 请求不可取消。无引用的在途请求仍占全局预算，完成后立即释放，跨图不会突破上限。
        private static readonly Dictionary<string, Lease> Cache = new(StringComparer.Ordinal);
        private Tile[] _tiles = Array.Empty<Tile>();
        private readonly List<Tile> _ranked = new();
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
            _tiles = new Tile[manifest.tiles.Length];
            for (int i = 0; i < _tiles.Length; i++)
                _tiles[i] = new Tile { Entry = manifest.tiles[i], Rect = manifest.TileWorldRect(manifest.tiles[i].row, manifest.tiles[i].column) };
        }

        public void Tick(Camera camera)
        {
            if (Manifest == null || camera == null || !isActiveAndEnabled || !TryGetView(camera, out var view)) return;
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
            var neighbours = Rect.MinMaxRect(view.xMin - width, view.yMin - height, view.xMax + width, view.yMax + height);
            foreach (var tile in _tiles)
            {
                tile.Wanted = false;
                if (!tile.Rect.Overlaps(neighbours) || tile.RetryAfter > now) continue;
                bool visible = tile.Rect.Overlaps(view);
                float distance = (tile.Rect.center - view.center).sqrMagnitude;
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
