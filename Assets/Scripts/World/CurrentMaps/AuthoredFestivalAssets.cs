using System;
using UnityEngine;
using UnityEngine.AI;

namespace MmorpgClient.World.CurrentMaps
{
    public sealed class AuthoredFestivalAssets : ScriptableObject
    {
        [Serializable]
        public sealed class Map
        {
            public uint sceneConfigId;
            public string mapKey, appearance, authoringGeometryHash, deliverySha256;
            public Rect worldRect;
            public UnityEngine.Vector3 spawn;
            public NavMeshData navMesh;
            public int columns, rows;
            public string textureResourceRoot;

            public void Validate()
            {
                bool a = sceneConfigId == 26000 && mapKey == "26000-mid-autumn" && appearance == "mid-autumn" &&
                    worldRect == new Rect(0, 0, 300, 240) && columns == 5 && rows == 4 &&
                    spawn == new UnityEngine.Vector3(110.921211f, 0, 33.47244f) &&
                    authoringGeometryHash == "9d2185138bdcb22cd7c296a028d28c93d24b8dd3b48ec96da36b1a7e16ac78d4";
                bool b = sceneConfigId == 26008 && mapKey == "26008-spring" && appearance == "spring" &&
                    worldRect == new Rect(0, 0, 300, 225) && columns == 4 && rows == 3 &&
                    spawn == new UnityEngine.Vector3(122.5f, 0, 127.5f) &&
                    authoringGeometryHash == "504096e2f15fb98b5de8e0e7a036c13c7d31a9e0adaa17b8927dc8607a3e6609";
                if ((!a && !b) || navMesh == null || deliverySha256 != "f5cc6fa645f79180b5d1a3a71f279004bc6dbb5101c8c05e15a028de40bd3f79" ||
                    spawn.y != 0 || !worldRect.Contains(new Vector2(spawn.x, spawn.z)) ||
                    textureResourceRoot != $"World/FestivalPreview20261111/{sceneConfigId}/{appearance}/")
                    throw new InvalidOperationException("手绘地图运行资源与交付合同不一致：" + sceneConfigId);
            }
        }

        public Map[] maps = Array.Empty<Map>();
    }
}
