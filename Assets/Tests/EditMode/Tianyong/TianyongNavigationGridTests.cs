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
