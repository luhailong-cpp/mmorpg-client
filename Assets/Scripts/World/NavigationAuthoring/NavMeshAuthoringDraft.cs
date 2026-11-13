using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace MmorpgClient.World.NavigationAuthoring
{
    [Serializable]
    public sealed class NavMeshAreaPolygon
    {
        public string name;
        public bool blocked;
        // Source-image pixels: origin at north-west, X east, Y south.
        public List<Vector2> points = new List<Vector2>();
    }

    [Serializable]
    public sealed class NavMeshBackgroundTile
    {
        public Texture2D texture;
        public int row = 1, column = 1;
        public int pixelWidth, pixelHeight;
        public string assetPath, sha256;
    }

    /// <summary>Hand-authored polygons for future image maps, independent of the formal map catalog.</summary>
    public sealed class NavMeshAuthoringDraft : ScriptableObject
    {
        // 图片地图使用显式烘焙合同；升级使旧结果失效，不重写作者的草稿内容。
        public const string BakeContractRevision = "authored-flat-v2";
        public const int TileSize = 256;
        public const float MinRegionArea = 0f;
        public const float GroundPointTolerance = .02f;
        // 仅服务端使用：Unity未公开可对应配置的垂直体素高度。
        public const float ServerCellHeight = .2f;

        public string mapKey = "new-map", displayName = "新地图";
        public Rect worldRect = new Rect(0, 0, 300, 300);
        public bool boundsConfirmed;
        public int imageWidth, imageHeight;
        public NavMeshBackgroundTile[] backgroundTiles = Array.Empty<NavMeshBackgroundTile>();
        public List<NavMeshAreaPolygon> polygons = new List<NavMeshAreaPolygon>();
        public bool spawnSet;
        public Vector2 spawn;
        public int agentTypeId;
        public float agentRadius = .4f, agentHeight = 1.8f, agentClimb = .3f, agentSlope = 45f, voxelSize = .1f;
        public NavMeshData bakedData;
        public string bakedGeometrySha256;

        public Vector2 PixelToWorld(Vector2 pixel) => new Vector2(
            worldRect.xMin + pixel.x / imageWidth * worldRect.width,
            worldRect.yMax - pixel.y / imageHeight * worldRect.height);

        public Vector2 WorldToPixel(Vector2 world) => new Vector2(
            (world.x - worldRect.xMin) / worldRect.width * imageWidth,
            (worldRect.yMax - world.y) / worldRect.height * imageHeight);

        // 与服务端原脚点探针按每轴比较，不能因体素变粗而扩大水平吸附范围。
        public static bool MatchesGroundPoint(UnityEngine.Vector3 requested, UnityEngine.Vector3 sampled) =>
            Mathf.Abs(sampled.x - requested.x) <= GroundPointTolerance &&
            Mathf.Abs(sampled.y - requested.y) <= GroundPointTolerance &&
            Mathf.Abs(sampled.z - requested.z) <= GroundPointTolerance;

        public void Validate(bool requireSpawn = false)
        {
            if (string.IsNullOrWhiteSpace(mapKey)) throw new InvalidOperationException("请填写地图标识。");
            if (imageWidth < 1 || imageHeight < 1 || backgroundTiles == null || backgroundTiles.Length == 0)
                throw new InvalidOperationException("请先选择整图或完整分块目录。");
            if (!Finite(worldRect.x) || !Finite(worldRect.y) || !Finite(worldRect.width) || !Finite(worldRect.height) ||
                worldRect.width <= 0 || worldRect.height <= 0 || worldRect.width > 100000 || worldRect.height > 100000)
                throw new InvalidOperationException("世界范围必须是有限的正尺寸，单边不超过100000单位。");
            if (Math.Abs((double)worldRect.width * imageHeight - (double)worldRect.height * imageWidth) >
                Math.Max(imageWidth, imageHeight) * .001)
                throw new InvalidOperationException("世界范围宽高比须与底图一致；请使用按底图比例计算高度。");
            if (!Finite(agentRadius) || !Finite(agentHeight) || !Finite(agentClimb) || !Finite(agentSlope) || !Finite(voxelSize) ||
                agentRadius <= 0 || agentHeight <= 0 || agentClimb < 0 || agentClimb >= agentHeight ||
                agentSlope < 0 || agentSlope > 60 || voxelSize < .01f || voxelSize > agentRadius)
                throw new InvalidOperationException("角色参数无效：半径/高度须大于0，台阶小于高度，坡度0–60，体素0.01至角色半径。");
            var cells = new HashSet<string>();
            foreach (var tile in backgroundTiles)
                if (tile == null || tile.row < 1 || tile.column < 1 || tile.pixelWidth < 1 || tile.pixelHeight < 1 ||
                    !cells.Add(tile.row + ":" + tile.column))
                    throw new InvalidOperationException("底图分块坐标或尺寸无效。");
            if (polygons == null) throw new InvalidOperationException("多边形列表为空。");
            foreach (var polygon in polygons)
            {
                if (polygon?.points == null || polygon.points.Count < 3)
                    throw new InvalidOperationException("每个区域至少需要三个顶点。");
                foreach (var point in polygon.points)
                    if (!Finite(point.x) || !Finite(point.y) || point.x < 0 || point.y < 0 || point.x > imageWidth || point.y > imageHeight)
                        throw new InvalidOperationException("区域顶点超出底图范围。");
            }
            if (spawnSet && (!Finite(spawn.x) || !Finite(spawn.y) || !worldRect.Contains(spawn)))
                throw new InvalidOperationException("出生点超出地图范围。");
            if (requireSpawn && !spawnSet) throw new InvalidOperationException("请先设置出生点。");
        }

        public NavMeshBuildSettings BuildSettings()
        {
            Validate();
            var settings = NavMesh.GetSettingsByID(agentTypeId);
            if (settings.agentTypeID == -1) throw new InvalidOperationException("选择的官方NavMesh角色类型不存在。");
            settings.agentRadius = agentRadius;
            settings.agentHeight = agentHeight;
            settings.agentClimb = agentClimb;
            settings.agentSlope = agentSlope;
            settings.overrideVoxelSize = true;
            settings.voxelSize = voxelSize;
            settings.overrideTileSize = true;
            settings.tileSize = TileSize;
            settings.minRegionArea = MinRegionArea;
            // 高度网格保留源平面的脚点高度，避免普通近似网格上浮半个水平体素。
            settings.buildHeightMesh = true;
            // 不继承全局角色类型的自动跨越设置，断开的作者区域须保持断开。
            settings.ledgeDropHeight = 0;
            settings.maxJumpAcrossDistance = 0;
            return settings;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
