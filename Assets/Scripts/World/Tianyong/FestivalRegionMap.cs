using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Vector3 = UnityEngine.Vector3;
    using Transform = UnityEngine.Transform;

    /// <summary>同一地区的日景和节庆图共用坐标、出生点和服务端导航。</summary>
    public sealed class FestivalRegionDefinition
    {
        private byte[] _walkMask;
        internal FestivalRegionDefinition(uint id, string key, string name, string festivalName, Vector3 spawn)
        { SceneConfigId = id; Key = key; Name = name; FestivalName = festivalName; Spawn = spawn; }
        public uint SceneConfigId { get; }
        public string Key { get; }
        public string Name { get; }
        public string FestivalName { get; }
        public Vector3 Spawn { get; }
        public string TexturePath(bool festival) => $"World/FestivalRegions/{Key}/{(festival ? "festival" : "day")}";
        public string MaskPath => $"World/FestivalRegions/{Key}/walkmask";

        public bool IsWalkable(Vector3 world)
        {
            var rect = TianyongPaintedCity.PaintingWorldRect;
            if (world.x < rect.xMin || world.x >= rect.xMax || world.z <= rect.yMin || world.z > rect.yMax) return false;
            if (_walkMask == null)
            {
                var source = Resources.Load<TextAsset>(MaskPath);
                if (source == null) throw new InvalidOperationException($"地区导航缺失：{MaskPath}");
                _walkMask = Convert.FromBase64String(source.text.Trim());
                if (_walkMask.Length != (150 * 150 + 7) / 8)
                    throw new InvalidOperationException($"地区导航长度错误：{MaskPath}");
            }
            int x = Mathf.FloorToInt((world.x - rect.xMin) / 2f);
            int y = Mathf.FloorToInt((rect.yMax - world.z) / 2f);
            if (x < 0 || x >= 150 || y < 0 || y >= 150) return false;
            int index = y * 150 + x;
            return (_walkMask[index >> 3] & (0x80 >> (index & 7))) != 0;
        }
        public TianyongNavigationGrid CreateNavigation() => new(2f, IsWalkable);
    }

    /// <summary>完整原生地区图铺在世界XZ平面上；视觉外观不会改变碰撞和场景编号。</summary>
    public static class FestivalRegionMap
    {
        public static readonly IReadOnlyList<FestivalRegionDefinition> Regions = new[]
        {
            new FestivalRegionDefinition(2, "penglai", "蓬莱岛", "中秋月夜", new Vector3(220f, 0f, 170f)),
            new FestivalRegionDefinition(3, "donghai", "东海渔村", "元宵灯会", new Vector3(200f, 0f, 180f)),
            new FestivalRegionDefinition(4, "lanxian", "揽仙镇", "春节迎新", new Vector3(200f, 0f, 180f)),
        };
        public const string GroundName = "FestivalRegionGround";
        public static FestivalRegionDefinition Find(uint sceneConfigId)
        {
            foreach (var region in Regions) if (region.SceneConfigId == sceneConfigId) return region;
            return null;
        }
        public static TianyongMapInstance Build(Transform parent, FestivalRegionDefinition region, bool festival)
        {
            if (region == null) throw new ArgumentNullException(nameof(region));
            var texture = Resources.Load<Texture2D>(region.TexturePath(festival));
            if (texture == null) throw new InvalidOperationException($"地区原图缺失：{region.TexturePath(festival)}");
            var navigation = region.CreateNavigation();
            if (!navigation.IsWalkable(region.Spawn)) throw new InvalidOperationException($"地区出生点不可行走：{region.Name}");
            var root = new GameObject($"[FestivalRegion:{region.Key}:{(festival ? "festival" : "day")}]");
            root.transform.SetParent(parent, false);
            var instance = new TianyongMapInstance(root, TianyongTheme.City, navigation);
            try
            {
                var floor = new GameObject(TianyongMapBuilder.GroundColliderName);
                floor.transform.SetParent(root.transform, false);
                var collider = floor.AddComponent<BoxCollider>();
                collider.center = new Vector3(200f, -.55f, 150f);
                collider.size = new Vector3(400f, 1f, 300f);

                var rect = TianyongPaintedCity.PaintingWorldRect;
                var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
                ground.name = GroundName;
                ground.transform.SetParent(root.transform, false);
                ground.transform.localPosition = new Vector3(rect.center.x, -.045f, rect.center.y);
                ground.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                ground.transform.localScale = new Vector3(rect.width, rect.height, 1f);
                var meshCollider = ground.GetComponent<Collider>();
                meshCollider.enabled = false;
                if (Application.isPlaying) UnityEngine.Object.Destroy(meshCollider);
                else UnityEngine.Object.DestroyImmediate(meshCollider);
                var shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default");
                var material = new Material(shader) { name = $"FestivalMap:{region.Key}", mainTexture = texture };
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                instance._materials.Add(material);
                var renderer = ground.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                instance.ConfigureCityTiles(region.Key, festival ? "festival" : "day");
                return instance;
            }
            catch { instance.Dispose(); throw; }
        }

        /// <summary>原生纹理保持完整方形；切换景色只换材质贴图，保持当前导航和角色状态。</summary>
        public static void SetAppearance(TianyongMapInstance map, FestivalRegionDefinition region, bool festival, Action onApplied = null)
        {
            var texture = Resources.Load<Texture2D>(region.TexturePath(festival));
            var ground = map?.Root?.transform.Find(GroundName);
            if (texture == null || ground == null) throw new InvalidOperationException($"地区外观无法载入：{region.Name}");
            map.ConfigureCityTiles(region.Key, festival ? "festival" : "day", () =>
            {
                if (map.Root == null || ground == null) return;
                var material = ground.GetComponent<MeshRenderer>().sharedMaterial;
                var oldTexture = material.mainTexture;
                var oldName = map.Root.name;
                try
                {
                    material.mainTexture = texture;
                    map.Root.name = $"[FestivalRegion:{region.Key}:{(festival ? "festival" : "day")}]";
                    onApplied?.Invoke();
                }
                catch
                {
                    material.mainTexture = oldTexture;
                    map.Root.name = oldName;
                    throw;
                }
            });
        }
    }
}
