using MmorpgClient.App;
using MmorpgClient.World.Tianyong;
using MmorpgClient.World.CurrentMaps;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class FestivalRegionVerificationProbeTests
    {
        [TestCase(1u, 2u, 3u, 1u)]
        [TestCase(2u, 3u, 1u, 2u)]
        [TestCase(3u, 1u, 2u, 3u)]
        public void VerificationVisitsEveryMapThroughRealTravelAndReturnsToStartingMap(
            uint currentScene, uint first, uint second, uint third)
        {
            var route = FestivalRegionAutoVerify.VerificationRoute(currentScene);
            Assert.That(route, Is.EqualTo(new[] { first, second, third }));
            Assert.That(route, Is.EquivalentTo(new uint[] { 1, 2, 3 }));
            uint previous = currentScene;
            foreach (uint next in route)
            {
                Assert.That(next, Is.Not.EqualTo(previous), "Same-scene travel cannot produce the required server notification.");
                previous = next;
            }
            Assert.That(previous, Is.EqualTo(currentScene));
        }

        [TestCase(1u)]
        [TestCase(2u)]
        [TestCase(3u)]
        public void SpawnHasSafeWasdAndConnectedClickTargets(uint sceneConfigId)
        {
            var region = CurrentMapCatalog.Find(sceneConfigId);
            Assert.That(region, Is.Not.Null);
            var nav = region.CreateNavigation();
            Assert.That(nav.IsWalkable(region.Spawn), Is.True);
            Assert.That(FestivalRegionAutoVerify.FindStraightDirection(nav, region.Spawn, out var direction), Is.True,
                "实机验收需要出生点附近有至少 8 米的宽路。");
            Assert.That(direction.sqrMagnitude, Is.GreaterThan(.5f));
            Assert.That(FestivalRegionAutoVerify.FindPathTarget(nav, region.Spawn, out var target), Is.True,
                "实机验收需要距出生点至少 8 米的连通目标。");
            Assert.That(nav.IsWalkable(target), Is.True);
            Assert.That(Vector3.Distance(region.Spawn, target), Is.GreaterThanOrEqualTo(8f));
        }

        [TestCase(1u)]
        [TestCase(1001u)]
        [TestCase(35004u)]
        public void LocalFestivalRouteVisitsNineDistinctMapsAndReturnsToMain(uint currentScene)
        {
            var route = FestivalRegionAutoVerify.LocalFestivalVerificationRoute(currentScene);
            Assert.That(route.Length, Is.EqualTo(10));
            Assert.That(route[route.Length - 1], Is.EqualTo(1u));
            Assert.That(route, Is.Unique);
            uint previous = currentScene;
            foreach (uint next in route)
            {
                Assert.That(next, Is.Not.EqualTo(previous));
                previous = next;
            }
        }

        [TestCase(1001u)]
        [TestCase(1002u)]
        [TestCase(5008u)]
        [TestCase(10000u)]
        [TestCase(19004u)]
        [TestCase(21003u)]
        [TestCase(23006u)]
        [TestCase(28007u)]
        [TestCase(35004u)]
        public void LocalFestivalProbeUsesDeliveredMaskAndSafeSmallMapRoutes(uint sceneConfigId)
        {
            var region = CurrentMapCatalog.Find(sceneConfigId);
            var nav = region.CreateNavigation();
            Assert.That(region.CanTravelOnline, Is.False);
            var rejected = FestivalRegionAutoVerify.NetworkBoundsProbePoint(region);
            Assert.That(float.IsNaN(rejected.x) || float.IsInfinity(rejected.x) ||
                float.IsNaN(rejected.z) || float.IsInfinity(rejected.z), Is.False);
            Assert.That(rejected.x, Is.GreaterThanOrEqualTo(region.EffectiveWorldRect.xMax));
            Assert.That(region.IsWalkable(rejected), Is.False);
            Assert.That(nav.IsWalkable(rejected), Is.False);
            Assert.That(FestivalRegionAutoVerify.FindStraightDirection(nav, region.Spawn, out _, 4f), Is.True);
            Assert.That(FestivalRegionAutoVerify.FindPathTarget(nav, region.Spawn, out var target, 2f), Is.True);
            Assert.That(nav.FindPath(region.Spawn, target).Count, Is.GreaterThanOrEqualTo(2));
            int walkable = 0;
            for (int x = 0; x < nav.Width; x++)
            for (int z = 0; z < nav.Depth; z++)
            {
                var point = FestivalRegionAutoVerify.NavigationProbeCenter(nav, x, z);
                Assert.That(nav.IsWalkable(point), Is.EqualTo(region.IsWalkable(point)));
                if (nav.IsWalkable(point)) walkable++;
            }
            if (sceneConfigId == 19004u) Assert.That(walkable, Is.EqualTo(48));
        }
    }
}
