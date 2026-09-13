using System.Reflection;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class FestivalRegionMapTests
    {
        [TestCase(2u)]
        [TestCase(3u)]
        [TestCase(4u)]
        public void DayAndFestival_KeepNativeSquareTexturesAndIdenticalPlayableGeometry(uint sceneId)
        {
            var region = FestivalRegionMap.Find(sceneId);
            Assert.That(region, Is.Not.Null);
            var day = Resources.Load<Texture2D>(region.TexturePath(false));
            var festival = Resources.Load<Texture2D>(region.TexturePath(true));
            foreach (var texture in new[] { day, festival })
            {
                Assert.That(texture, Is.Not.Null);
                Assert.That(texture.width, Is.EqualTo(1254));
                Assert.That(texture.height, Is.EqualTo(1254));
                Assert.That(texture.mipmapCount, Is.EqualTo(1));
                Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            }
            Assert.That(festival, Is.Not.SameAs(day));
            var parent = new GameObject("FestivalRegionResourceTest");
            TianyongMapInstance map = null;
            try
            {
                map = FestivalRegionMap.Build(parent.transform, region, false);
                var navigation = map.Navigation;
                var ground = map.Root.transform.Find(FestivalRegionMap.GroundName);
                var mesh = ground.GetComponent<MeshFilter>().sharedMesh;
                var renderer = ground.GetComponent<MeshRenderer>();
                var material = renderer.sharedMaterial;
                var floor = map.Root.GetComponentInChildren<BoxCollider>();
                var position = ground.localPosition;
                var rotation = ground.localRotation;
                var scale = ground.localScale;
                Assert.That(navigation.IsWalkable(region.Spawn), Is.True);
                Assert.That(material.mainTexture, Is.SameAs(day));
                Assert.That(ground.GetComponent<Collider>(), Is.Null, "Only the separate floor may receive movement rays.");
                Assert.That(floor, Is.Not.Null);
                Assert.That(floor.enabled, Is.True);
                Assert.That(scale.x, Is.EqualTo(300f));
                Assert.That(scale.y, Is.EqualTo(300f));
                int walkable = 0, blocked = 0;
                for (int x = 0; x < navigation.Width; ++x)
                for (int z = 0; z < navigation.Depth; ++z)
                {
                    var point = new Vector3((x + .5f) * navigation.CellSize, 0,
                        (z + .5f) * navigation.CellSize);
                    Assert.That(navigation.IsWalkable(point), Is.EqualTo(region.IsWalkable(point)));
                    if (navigation.IsWalkable(point)) ++walkable; else ++blocked;
                }
                Assert.That(walkable, Is.GreaterThan(0));
                Assert.That(blocked, Is.GreaterThan(0));
                FestivalRegionMap.SetAppearance(map, region, true);
                Assert.That(renderer.sharedMaterial, Is.SameAs(material));
                Assert.That(material.mainTexture, Is.SameAs(festival));
                Assert.That(map.Navigation, Is.SameAs(navigation));
                Assert.That(ground.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
                Assert.That(ground.localPosition, Is.EqualTo(position));
                Assert.That(ground.localRotation, Is.EqualTo(rotation));
                Assert.That(ground.localScale, Is.EqualTo(scale));
                Assert.That(map.Root.GetComponentInChildren<BoxCollider>(), Is.SameAs(floor));
                FestivalRegionMap.SetAppearance(map, region, false);
                Assert.That(material.mainTexture, Is.SameAs(day));
                Assert.That(map.Navigation, Is.SameAs(navigation));
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
                foreach (uint sceneId in new uint[] { 1, 2, 3, 4, 1 })
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
                    if (sceneId != 1)
                    {
                        var navigation = controller.Navigation;
                        var feet = controller.FeetPosition;
                        runtime.SetFestivalAppearance(true);
                        Assert.That(runtime.FestivalAppearance, Is.True);
                        Assert.That(controller.Navigation, Is.SameAs(navigation));
                        Assert.That(controller.FeetPosition, Is.EqualTo(feet));
                    }
                    previousRoot = runtime.Map.Root;
                    previousNavigation = runtime.Map.Navigation;
                }
                Assert.That(runtime.FestivalAppearance, Is.False, "Returning to Tianyong must clear regional appearance.");
                Invoke(runtime, "HandleDisconnected");
                Assert.That(runtime.Map, Is.Null);
                Assert.That(runtime.ActiveSceneConfigId, Is.Zero);
                Assert.That(actorObject.GetComponent<TianyongPlayerController>().enabled, Is.False);
                Assert.That(previousRoot == null, Is.True);
                Invoke(runtime, "EnterScene", 3u);
                Assert.That(runtime.FestivalAppearance, Is.False, "A fresh connection must not retain the old night appearance.");
                Invoke(runtime, "EnterScene", 999u);
                Assert.That(runtime.Map, Is.Null, "Unsupported scenes must never retain a different map's navigation.");
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
