using System.Reflection;
using MmorpgClient.World;
using MmorpgClient.World.CurrentMaps;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class FestivalRegionMapTests
    {
        [TestCase(1u)]
        [TestCase(2u)]
        [TestCase(3u)]
        [TestCase(5u)]
        public void CurrentLayouts_KeepNativeTexturesAndMatchingPlayableGeometry(uint sceneId)
        {
            var region = CurrentMapCatalog.FindForOnline(sceneId);
            Assert.That(region, Is.Not.Null);
            var day = region.LoadTexture();
            Assert.That(day, Is.Not.Null);
            Assert.That(day.width, Is.GreaterThanOrEqualTo(1024));
            Assert.That(day.height, Is.GreaterThanOrEqualTo(1024));
            Assert.That(day.mipmapCount, Is.EqualTo(1));
            Assert.That(day.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            var parent = new GameObject("FestivalRegionResourceTest");
            TianyongMapInstance map = null;
            try
            {
                map = CurrentMapBuilder.Build(parent.transform, region);
                var navigation = map.Navigation;
                var ground = map.Root.transform.Find(CurrentMapBuilder.GroundName);
                var renderer = ground.GetComponent<MeshRenderer>();
                var material = renderer.sharedMaterial;
                var floor = map.Root.GetComponentInChildren<BoxCollider>();
                var scale = ground.localScale;
                var rect = region.WorldRect;
                Assert.That(navigation.IsWalkable(region.Spawn), Is.True);
                Assert.That(material.mainTexture, Is.SameAs(day));
                Assert.That(ground.GetComponent<Collider>(), Is.Null, "Only the separate floor may receive movement rays.");
                Assert.That(floor, Is.Not.Null);
                Assert.That(floor.enabled, Is.True);
                Assert.That(scale.x, Is.EqualTo(rect.width));
                Assert.That(scale.y, Is.EqualTo(rect.height));
                int walkable = 0, blocked = 0;
                int stepX = Mathf.Max(1, navigation.Width / 32);
                int stepZ = Mathf.Max(1, navigation.Depth / 32);
                for (int x = 0; x < navigation.Width; x += stepX)
                for (int z = 0; z < navigation.Depth; z += stepZ)
                {
                    var point = new Vector3(rect.xMin + (x + .5f) * navigation.CellSize, 0,
                        rect.yMin + (z + .5f) * navigation.CellSize);
                    Assert.That(navigation.IsWalkable(point), Is.EqualTo(region.IsWalkable(point)));
                    if (navigation.IsWalkable(point)) ++walkable; else ++blocked;
                }
                Assert.That(walkable, Is.GreaterThan(0));
                Assert.That(blocked, Is.GreaterThan(0));
                Assert.That(ground.localPosition, Is.EqualTo(new Vector3(rect.center.x, -.045f, rect.center.y)));
                Assert.That(map.Root.GetComponentInChildren<TianyongPaintedForeground>(), Is.Null,
                    "The new layout must not reuse old foreground cutouts.");
            }
            finally
            {
                map?.Dispose();
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void SceneRoundTrip_RebindsActorNavigationAndDisconnectClearsMap()
        {
            var root = new GameObject("FestivalSceneRoundTripTest");
            var actorObject = new GameObject("LocalActorTest");
            var runtime = root.AddComponent<TianyongMapRuntime>();
            var view = new ActorView { Go = actorObject, Kind = ActorKind.Player, Entity = 41 };
            try
            {
                runtime.Initialize(null, null, null, TianyongMapConfig.LoadDefault());
                GameObject previousRoot = null;
                TianyongNavigationGrid previousNavigation = null;
                foreach (uint sceneId in new uint[] { 1, 2, 3, 5, 1 })
                {
                    // GameClient clears its actor world before publishing the scene notification.
                    Invoke(runtime, "HandleLocalPlayerChanged", (object)null);
                    var oldController = actorObject.GetComponent<TianyongPlayerController>();
                    if (oldController != null) Assert.That(oldController.enabled, Is.False);
                    Invoke(runtime, "EnterScene", sceneId);
                    Assert.That(previousRoot == null, Is.True, "The previous scene must be disposed.");
                    Assert.That(runtime.ActiveSceneConfigId, Is.EqualTo(sceneId));
                    Assert.That(runtime.Map.Root.activeSelf, Is.True);
                    Assert.That(runtime.Map.Navigation, Is.Not.SameAs(previousNavigation));
                    actorObject.transform.position = runtime.CurrentSpawn;
                    Invoke(runtime, "HandleLocalPlayerChanged", view);
                    var controller = actorObject.GetComponent<TianyongPlayerController>();
                    Assert.That(controller, Is.Not.Null);
                    Assert.That(controller.enabled, Is.True);
                    Assert.That(controller.Navigation, Is.SameAs(runtime.Map.Navigation));
                    Assert.That(controller.Navigation.IsWalkable(controller.FeetPosition), Is.True);
                    Assert.That(Vector3.Distance(controller.FeetPosition, runtime.CurrentSpawn), Is.LessThan(.01f));
                    Assert.That(runtime.FestivalAppearance, Is.True, "Online Qdao dual-festival maps default to spring.");
                    var feet = controller.FeetPosition;
                    Assert.That(runtime.TrySetAppearance("mid-autumn"), Is.True);
                    Assert.That(runtime.Appearance, Is.EqualTo("mid-autumn"));
                    Assert.That(controller.Navigation, Is.SameAs(runtime.Map.Navigation));
                    Assert.That(controller.FeetPosition, Is.EqualTo(feet));
                    previousRoot = runtime.Map.Root;
                    previousNavigation = runtime.Map.Navigation;
                }
                Invoke(runtime, "HandleDisconnected");
                Assert.That(runtime.Map, Is.Null);
                Assert.That(runtime.ActiveSceneConfigId, Is.Zero);
                Assert.That(actorObject.GetComponent<TianyongPlayerController>().enabled, Is.False);
                Assert.That(previousRoot == null, Is.True);
                Invoke(runtime, "EnterScene", 3u);
                Assert.That(runtime.Appearance, Is.EqualTo("spring"), "A fresh connection must reset to default appearance.");
                Invoke(runtime, "EnterScene", 999u);
                Assert.That(runtime.Map, Is.Null, "Unsupported scenes must never retain a different map's navigation.");
                Invoke(runtime, "EnterScene", 4u);
                Assert.That(runtime.Map, Is.Null, "Undelivered current maps must not load removed legacy resources.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(actorObject);
            }
        }

        private static void Invoke(TianyongMapRuntime runtime, string method, params object[] args)
        {
            var member = typeof(TianyongMapRuntime).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(member, Is.Not.Null);
            member.Invoke(runtime, args);
        }
    }
}
