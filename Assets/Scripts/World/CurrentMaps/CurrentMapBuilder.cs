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
                collider.center = new Vector3(200f, -.55f, 150f);
                collider.size = new Vector3(400f, 1f, 300f);

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
                return instance;
            }
            catch
            {
                root.SetActive(false);
                instance.Dispose();
                throw;
            }
        }
    }
}
