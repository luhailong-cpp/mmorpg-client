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
    }
}
