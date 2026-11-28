using System.Collections.Generic;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class TianyongNavigationGridTests
    {
        [Test]
        public void MapDefinition_UsesAuthoritativeFourHundredByThreeHundredWorld()
        {
            Assert.That(TianyongMapDefinition.Width, Is.EqualTo(400f));
            Assert.That(TianyongMapDefinition.Depth, Is.EqualTo(300f));

            var grid = new TianyongNavigationGrid();
            Assert.That(grid.CellSize, Is.EqualTo(TianyongMapDefinition.NavigationCellSize));
            Assert.That(grid.Width, Is.EqualTo(100));
            Assert.That(grid.Depth, Is.EqualTo(75));
        }

        [Test]
        public void DefaultSpawn_IsInsideMapAndWalkable()
        {
            var grid = new TianyongNavigationGrid();
            var spawn = TianyongMapDefinition.DefaultSpawn;

            Assert.That(TianyongMapDefinition.ContainsXZ(spawn), Is.True);
            Assert.That(grid.IsWalkable(spawn), Is.True);
            Assert.That(
                ContainsInclusive(TianyongMapDefinition.FestivalFountainFootprint, spawn),
                Is.False,
                "Default spawn must stay clear of the central fountain.");
        }

        [Test]
        public void PreciseMovement_AllowsBothLegsOfNarrowTurnAndRejectsTheChord()
        {
            var bounds = new Rect(0f, 0f, 2f, 2f);
            var grid = new TianyongNavigationGrid(.5f, p => p.z < .5f || p.x >= 1.5f,
                bounds, bounds, preciseNetworkMovement: true);
            var start = new Vector3(.25f, 0f, .25f);
            var turn = new Vector3(1.75f, 0f, .25f);
            var destination = new Vector3(1.75f, 0f, 1.75f);

            Assert.That(grid.IsSegmentWalkable(start, turn), Is.True);
            Assert.That(grid.IsSegmentWalkable(turn, destination), Is.True);
            Assert.That(grid.IsSegmentWalkable(destination, turn), Is.True);
            Assert.That(grid.IsSegmentWalkable(start, destination), Is.False);
        }

        [Test]
        public void PreciseMovement_DiagonalRequiresBothSideCellsAtAGridCorner()
        {
            var bounds = new Rect(0f, 0f, 1f, 1f);
            var grid = new TianyongNavigationGrid(.5f, p => p.x < .5f || p.z >= .5f,
                bounds, bounds, preciseNetworkMovement: true);
            var low = new Vector3(.25f, 0f, .25f);
            var high = new Vector3(.75f, 0f, .75f);

            Assert.That(grid.IsSegmentWalkable(low, high), Is.False);
            Assert.That(grid.IsSegmentWalkable(high, low), Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void CrossCanalPath_UsesEachRequestedStoneBridge(int bridgeIndex)
        {
            var grid = new TianyongNavigationGrid();
            var bridge = TianyongMapDefinition.Bridges[bridgeIndex];
            var path = grid.FindPath(
                new Vector3(bridge.center.x, 0f, TianyongMapDefinition.Canal.yMin - 8f),
                new Vector3(bridge.center.x, 0f, TianyongMapDefinition.Canal.yMax + 8f));

            Assert.That(path, Is.Not.Empty,
                $"A route through bridge {bridgeIndex} should connect both canal banks.");

            var sampledCanal = false;
            foreach (var point in SamplePath(path, 1f))
            {
                if (!ContainsInclusive(TianyongMapDefinition.Canal, point))
                    continue;

                sampledCanal = true;
                Assert.That(
                    ContainsInclusive(bridge, point),
                    Is.True,
                    $"Route for bridge {bridgeIndex} entered canal outside its deck at {point}.");
            }

            Assert.That(sampledCanal, Is.True,
                $"Route for bridge {bridgeIndex} must actually cross the canal.");
        }

        [Test]
        public void PathAcrossBuildingDistrict_DoesNotCutThroughBlockedFootprints()
        {
            var grid = new TianyongNavigationGrid();
            var path = grid.FindPath(
                new Vector3(220f, 0f, 94f),
                new Vector3(295f, 0f, 94f));

            Assert.That(path, Is.Not.Empty);
            foreach (var point in SamplePath(path, 0.75f))
            {
                Assert.That(
                    grid.IsWalkable(point),
                    Is.True,
                    $"Smoothed path cut through blocked navigation at {point}.");
            }
        }

        [Test]
        public void TargetInsideBuilding_IsProjectedToWalkableApproachPoint()
        {
            var grid = new TianyongNavigationGrid();
            var blockedTarget = FindBuilding("FujiaBank").Position;

            Assert.That(grid.IsWalkable(blockedTarget), Is.False, "Fixture target must be inside a building.");

            var path = grid.FindPath(TianyongMapDefinition.DefaultSpawn, blockedTarget);

            Assert.That(path, Is.Not.Empty);
            Assert.That(
                grid.IsWalkable(path[path.Count - 1]),
                Is.True,
                "A blocked click target must resolve to its nearest walkable navigation cell.");
            Assert.That(
                ContainsInclusive(FindBuilding("FujiaBank").Footprint(2.5f), path[path.Count - 1]),
                Is.False,
                "The resolved approach point must remain outside the padded building footprint.");
        }

        [Test]
        public void ApproachPath_LargeObstacleBeyondEightCells_ResolvesTrueEuclideanNearestShore()
        {
            // 60x60 世界（cell=1），中央 40x40 巨大湖泊（x,z ∈ [10,50)），远超旧 8 格上限。
            var bounds = new Rect(0f, 0f, 60f, 60f);
            var grid = new TianyongNavigationGrid(1f,
                p => p.x < 10f || p.x >= 50f || p.z < 10f || p.z >= 50f,
                bounds, bounds, preciseNetworkMovement: true);
            var start = new Vector3(5.5f, 0f, 30.5f);
            // 点在湖中偏东侧（距东岸 x=50.5 仅 15.5m，距西岸 25.5m，距南北岸 20.5m）。
            var clickInLake = new Vector3(35f, 0f, 30.5f);
            Assert.That(grid.IsWalkable(clickInLake), Is.False);
            Assert.That(grid.TryFindNearestWalkable(clickInLake, out _), Is.False,
                "旧有界脱困查询仍须保持 8 格上限，不破坏既有契约。");

            var path = grid.FindApproachPath(start, clickInLake);
            Assert.That(path.Count, Is.GreaterThanOrEqualTo(2));
            var end = path[path.Count - 1];
            Assert.That(grid.IsWalkable(end), Is.True);
            Assert.That(end.x, Is.EqualTo(50.5f).Within(0.01f), "必须选到真正欧氏最近的东岸而非左下偏置点。");
            Assert.That(end.z, Is.EqualTo(30.5f).Within(0.01f));
            for (var i = 1; i < path.Count; i++)
                Assert.That(grid.IsSegmentWalkable(path[i - 1], path[i]), Is.True);
        }

        [Test]
        public void ApproachPath_DisconnectedIsland_RoutesToNearestReachableShoreWhileStrictFindPathRejects()
        {
            // 左岸 x∈[0,10)，中间宽河 x∈[10,25) 阻挡，右岸孤岛 x∈[25,35)。
            var bounds = new Rect(0f, 0f, 35f, 20f);
            var grid = new TianyongNavigationGrid(1f,
                p => p.x < 10f || p.x >= 25f,
                bounds, bounds, preciseNetworkMovement: true);
            var start = new Vector3(2.5f, 0f, 10.5f);
            var islandPoint = new Vector3(30.5f, 0f, 14.5f);

            Assert.That(grid.IsWalkable(islandPoint), Is.True);
            Assert.That(grid.FindPath(start, islandPoint), Is.Empty, "严格 FindPath 必须继续拒绝跨孤岛目标。");

            var approach = grid.FindApproachPath(start, islandPoint);
            Assert.That(approach.Count, Is.GreaterThanOrEqualTo(2));
            var end = approach[approach.Count - 1];
            Assert.That(end.x, Is.EqualTo(9.5f).Within(0.01f), "必须停在起点所在左岸最靠东边缘。");
            Assert.That(end.z, Is.EqualTo(14.5f).Within(0.01f), "必须对齐目标的纵向坐标达到最小欧氏距离。");
            for (var i = 1; i < approach.Count; i++)
                Assert.That(grid.IsSegmentWalkable(approach[i - 1], approach[i]), Is.True);

            // 已经站在最近岸边时再点孤岛，返回单点路径表示已贴到最近处。
            var alreadyThere = grid.FindApproachPath(end, islandPoint);
            Assert.That(alreadyThere.Count, Is.EqualTo(1));
            Assert.That(grid.FindApproachPath(start, new Vector3(float.NaN, 0f, 0f)), Is.Empty);
        }

        private static TianyongBuildingSpec FindBuilding(string name)
        {
            foreach (var building in TianyongMapDefinition.Buildings)
            {
                if (building.Name == name)
                    return building;
            }

            Assert.Fail($"Missing Tianyong building fixture: {name}");
            return default;
        }

        private static IEnumerable<Vector3> SamplePath(IReadOnlyList<Vector3> path, float spacing)
        {
            if (path.Count == 0)
                yield break;

            yield return path[0];
            for (var segment = 1; segment < path.Count; segment++)
            {
                var from = path[segment - 1];
                var to = path[segment];
                var sampleCount = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / spacing));
                for (var sample = 1; sample <= sampleCount; sample++)
                    yield return Vector3.Lerp(from, to, sample / (float)sampleCount);
            }
        }

        private static bool ContainsInclusive(Rect rect, Vector3 point)
            => point.x >= rect.xMin && point.x <= rect.xMax &&
               point.z >= rect.yMin && point.z <= rect.yMax;
    }
}
