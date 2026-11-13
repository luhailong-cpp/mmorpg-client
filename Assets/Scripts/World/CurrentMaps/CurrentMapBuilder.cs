using System;
using MmorpgClient.World.Tianyong;
using UnityEngine;

namespace MmorpgClient.World.CurrentMaps
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    /// <summary>Builds the current layout and mask without legacy art, foregrounds or NPC placements.</summary>
    public static class CurrentMapBuilder
    {
        public const string GroundName = "CurrentMapGround";

        public static TianyongMapInstance Build(Transform parent, CurrentMapDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (definition.IsAuthoredFlat) return BuildAuthored(parent, definition);
            // Validate all delivered data before adding any world object.
            var texture = definition.LoadTexture();
            var navigation = definition.CreateNavigation();
            if (!navigation.IsWalkable(definition.Spawn))
                throw new InvalidOperationException($"当前地图出生点不在导航网格上：{definition.Key}");
            var shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (shader == null) throw new InvalidOperationException("当前地图缺少可用的非光照纹理 Shader。");
            var root = new GameObject($"[CurrentMap:{definition.Key}]");
            var instance = new TianyongMapInstance(root, TianyongTheme.City, navigation);
            try
            {
                root.transform.SetParent(parent, false);
                var floor = new GameObject(TianyongMapBuilder.GroundColliderName);
                floor.transform.SetParent(root.transform, false);
                var collider = floor.AddComponent<BoxCollider>();
                var navigationBounds = definition.NavigationWorldRect;
                collider.center = new Vector3(navigationBounds.center.x, -.55f, navigationBounds.center.y);
                collider.size = new Vector3(navigationBounds.width, 1f, navigationBounds.height);

                var rect = definition.WorldRect;
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
                var material = new Material(shader) { name = "CurrentMap:" + definition.Key, mainTexture = texture };
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                instance._materials.Add(material);
                var renderer = ground.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                instance.ConfigureCurrentMapTiles(definition);
                return instance;
            }
            catch
            {
                root.SetActive(false);
                instance.Dispose();
                throw;
            }
        }

        private static TianyongMapInstance BuildAuthored(Transform parent, CurrentMapDefinition definition)
        {
            var item = definition.Authored;
            item.Validate();
            var shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (shader == null) throw new InvalidOperationException("手绘地图缺少纹理 Shader。");
            var textures = new Texture2D[item.rows, item.columns];
            for (int row = 0; row < item.rows; row++)
            for (int col = 0; col < item.columns; col++)
            {
                string path = item.textureResourceRoot + $"r{row + 1:00}_c{col + 1:00}";
                var texture = Resources.Load<Texture2D>(path);
                if (texture == null || texture.width != 1024 || texture.height != 1024)
                    throw new InvalidOperationException("手绘地图分块缺失或尺寸错误：" + path);
                textures[row, col] = texture;
            }
            var root = new GameObject($"[AuthoredMap:{definition.Key}]");
            root.SetActive(false);
            var instance = new TianyongMapInstance(root, TianyongTheme.City, null);
            try
            {
                root.transform.SetParent(parent, false);
                instance.SetAuthoredNavigation(new AuthoredNavMeshNavigation(item.navMesh, item.worldRect, item.spawn));
                var floor = new GameObject(TianyongMapBuilder.GroundColliderName);
                floor.transform.SetParent(root.transform, false);
                var collider = floor.AddComponent<BoxCollider>();
                // 手绘地图的角色脚点以导航原面 Y=0 为准，运动策略不施加重力。
                // CharacterController 的 skinWidth 是 0.06；碰撞体顶面若同在 Y=0，
                // PhysX 首步会把胶囊向上推出 0.06，超过服务端脚点 0.02 容差。
                // 地板顶面留在 -0.1，防止皮肤重叠；不移动角色、导航或渲染地面。
                collider.center = new Vector3(item.worldRect.center.x, -.6f, item.worldRect.center.y);
                collider.size = new Vector3(item.worldRect.width, 1f, item.worldRect.height);
                float tileWidth = item.worldRect.width / item.columns;
                float tileHeight = item.worldRect.height / item.rows;
                for (int row = 0; row < item.rows; row++)
                for (int col = 0; col < item.columns; col++)
                {
                    var tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    tile.name = $"r{row + 1:00}_c{col + 1:00}";
                    tile.transform.SetParent(root.transform, false);
                    tile.transform.localPosition = new Vector3(item.worldRect.xMin + (col + .5f) * tileWidth,
                        -.01f, item.worldRect.yMax - (row + .5f) * tileHeight);
                    tile.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    tile.transform.localScale = new Vector3(tileWidth, tileHeight, 1f);
                    var tileCollider = tile.GetComponent<Collider>();
                    tileCollider.enabled = false;
                    if (Application.isPlaying) UnityEngine.Object.Destroy(tileCollider);
                    else UnityEngine.Object.DestroyImmediate(tileCollider);
                    var material = new Material(shader) { name = definition.Key + ":" + tile.name, mainTexture = textures[row, col] };
                    instance._materials.Add(material);
                    var renderer = tile.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                return instance;
            }
            catch
            {
                instance.Dispose();
                throw;
            }
        }
    }
}
