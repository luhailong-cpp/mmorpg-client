using System;
using System.IO;
using System.Security.Cryptography;
using MmorpgClient.World.CurrentMaps;
using MmorpgClient.World.NavigationAuthoring;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MmorpgClient.Editor.NavigationAuthoring
{
    /// <summary>从已锁定的共同交付生成Player可引用的资源登记，不修改作者草稿。</summary>
    public static class AuthoredFestivalRegistryGenerator
    {
        private const string Delivery = "Docs/VerificationEvidence/authored-festival-delivery-20261112/delivery.json";
        private const string DeliverySha = "f5cc6fa645f79180b5d1a3a71f279004bc6dbb5101c8c05e15a028de40bd3f79";
        private const string Output = "Assets/Resources/World/AuthoredFestival20261112/registry.asset";

        [MenuItem("Tools/Navigation/Generate Authored Festival Runtime Registry")]
        public static void Generate()
        {
            if (AssetDatabase.LoadMainAssetAtPath(Output) != null)
                throw new InvalidOperationException("运行登记已存在，不自动覆盖：" + Output);
            if (Hash(Delivery) != DeliverySha) throw new InvalidOperationException("共同交付清单SHA不一致。");
            var delivery = JObject.Parse(File.ReadAllText(Delivery));
            if ((int)delivery["schemaVersion"] != 1 || (string)delivery["result"] != "PASS")
                throw new InvalidOperationException("共同交付清单无效。");
            var maps = (JArray)delivery["maps"];
            if (maps == null || maps.Count != 2) throw new InvalidOperationException("交付清单必须含两张地图。");
            var values = new AuthoredFestivalAssets.Map[2];
            var seen = new System.Collections.Generic.HashSet<uint>();
            for (int i = 0; i < maps.Count; i++)
            {
                var row = maps[i];
                uint id = (uint)row["sceneConfigId"];
                if ((id != 26000 && id != 26008) || !seen.Add(id)) throw new InvalidOperationException("地图场景编号不匹配。");
                string path = (string)row["authoredAsset"];
                string navPath = (string)row["navMeshAsset"];
                if (Hash(path) != (string)row["authoredAssetSha256"] || Hash(navPath) != (string)row["navMeshAssetSha256"] ||
                    AssetDatabase.AssetPathToGUID(path) != (string)row["authoredAssetGuid"] ||
                    AssetDatabase.AssetPathToGUID(navPath) != (string)row["navMeshGuid"])
                    throw new InvalidOperationException("作者草稿或烘焙资源与共同交付不一致：" + id);
                var draft = AssetDatabase.LoadAssetAtPath<NavMeshAuthoringDraft>(path);
                if (draft == null || !NavMeshAuthoringAssetIO.IsBakeCurrent(draft) ||
                    draft.bakedData == null || AssetDatabase.GetAssetPath(draft.bakedData) != navPath ||
                    draft.bakedGeometrySha256 != (string)row["authoringGeometryHash"] || draft.mapKey != (string)row["mapKey"])
                    throw new InvalidOperationException("作者烘焙数据已过期或不匹配：" + id);
                foreach (var tile in draft.backgroundTiles)
                    if (tile.texture == null || AssetDatabase.GetAssetPath(tile.texture) != tile.assetPath || Hash(tile.assetPath) != tile.sha256)
                        throw new InvalidOperationException("作者底图分块与草稿不匹配：" + id);
                var spawn = (JArray)row["spawnUnity"];
                var bounds = row["worldRect"];
                if (spawn == null || spawn.Count != 3) throw new InvalidOperationException("共同出生点无效。");
                var item = new AuthoredFestivalAssets.Map
                {
                    sceneConfigId = id, mapKey = (string)row["mapKey"], appearance = (string)row["appearance"],
                    authoringGeometryHash = draft.bakedGeometrySha256, deliverySha256 = DeliverySha,
                    worldRect = new Rect((float)bounds["x"], (float)bounds["z"], (float)bounds["width"], (float)bounds["depth"]),
                    spawn = new UnityEngine.Vector3((float)spawn[0], (float)spawn[1], (float)spawn[2]),
                    navMesh = draft.bakedData, columns = draft.imageWidth / 1024, rows = draft.imageHeight / 1024,
                    textureResourceRoot = $"World/FestivalPreview20261111/{id}/{(string)row["appearance"]}/"
                };
                if (item.worldRect != draft.worldRect) throw new InvalidOperationException("作者地图范围与共同交付不一致。");
                item.Validate();
                values[i] = item;
            }
            if (!AssetDatabase.IsValidFolder("Assets/Resources/World/AuthoredFestival20261112"))
                AssetDatabase.CreateFolder("Assets/Resources/World", "AuthoredFestival20261112");
            var registry = ScriptableObject.CreateInstance<AuthoredFestivalAssets>();
            registry.maps = values;
            AssetDatabase.CreateAsset(registry, Output);
            AssetDatabase.SaveAssets();
            Debug.Log("[AuthoredFestival] 两张运行导航引用已从共同交付生成。");
        }

        private static string Hash(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
