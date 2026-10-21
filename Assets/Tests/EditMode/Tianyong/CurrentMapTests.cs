using System;
using System.Reflection;
using MmorpgClient.World;
using MmorpgClient.World.CurrentMaps;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Object = UnityEngine.Object;
    using Vector3 = UnityEngine.Vector3;

    public sealed class CurrentMapTests
    {
        private const string Source = "{\"schemaVersion\":1,\"key\":\"main\",\"sceneId\":1," +
            "\"layoutPixels\":{\"width\":1254,\"height\":1254},\"walkmaskResolution\":150,\"cellSize\":2," +
            "\"worldRect\":{\"x\":50,\"y\":0,\"width\":300,\"height\":300}," +
            "\"spawnWorld\":{\"x\":51,\"y\":0,\"z\":299}}";

        [Test]
        public void MaskUsesNorthWestOriginMostSignificantBitAndBlockedExterior()
        {
            var bytes = new byte[2813];
            bytes[0] = 0x80;
            bytes[2812] = 0x10;
            var data = CurrentMapData.Parse("main", Source, Convert.ToBase64String(bytes));
            Assert.That(data.IsWalkable(new Vector3(51, 0, 299)), Is.True);
            Assert.That(data.IsWalkable(new Vector3(349, 0, 1)), Is.True);
            Assert.That(data.IsWalkable(new Vector3(349, 0, 0)), Is.True);
            foreach (var point in new[] { new Vector3(53, 0, 299), new Vector3(51, 0, 297),
                         new Vector3(49, 0, 299), new Vector3(350, 0, 1), new Vector3(51, 0, 300),
                         new Vector3(349, 0, -.01f), new Vector3(float.NaN, 0, 299) })
                Assert.That(data.IsWalkable(point), Is.False, point.ToString());
        }

        [Test]
        public void CellBoundariesMatchControllerGridWithOnlyNorthWestCellWalkable()
        {
            var bytes = new byte[2813];
            bytes[0] = 0x80;
            var data = CurrentMapData.Parse("main", Source, Convert.ToBase64String(bytes));
            var navigation = new TianyongNavigationGrid(CurrentMapCatalog.CellSize, data.IsWalkable);
            foreach (var point in new[] { new Vector3(50, 0, 298), new Vector3(51, 0, 298),
                         new Vector3(51, 0, 297.99f), new Vector3(52, 0, 299), new Vector3(49.99f, 0, 299) })
                Assert.That(data.IsWalkable(point), Is.EqualTo(navigation.IsWalkable(point)), point.ToString());
            Assert.That(data.IsWalkable(new Vector3(51, 0, 298)), Is.True,
                "The south edge belongs to the north cell, even though mask rows run top to bottom.");
            Assert.That(data.IsWalkable(new Vector3(51, 0, 297.99f)), Is.False);
            Assert.That(data.IsWalkable(new Vector3(52, 0, 299)), Is.False);
        }

        [TestCase("version")]
        [TestCase("identity")]
        [TestCase("layout-size")]
        [TestCase("mask-size")]
        [TestCase("cell-size")]
        [TestCase("world-bounds")]
        [TestCase("spawn-outside")]
        [TestCase("blocked-spawn")]
        [TestCase("base64")]
        [TestCase("length")]
        [TestCase("padding")]
        public void InvalidDeliveriesFailBeforeWorldConstruction(string problem)
        {
            string json = Source;
            var bytes = new byte[2813];
            bytes[0] = 0x80;
            if (problem == "version") json = json.Replace("schemaVersion\":1", "schemaVersion\":2");
            if (problem == "identity") json = json.Replace("sceneId\":1", "sceneId\":2");
            if (problem == "layout-size") json = json.Replace("1254", "4096");
            if (problem == "mask-size") json = json.Replace("walkmaskResolution\":150", "walkmaskResolution\":149");
            if (problem == "cell-size") json = json.Replace("cellSize\":2", "cellSize\":4");
            if (problem == "world-bounds") json = json.Replace("width\":300", "width\":400");
            if (problem == "spawn-outside") json = json.Replace("x\":51", "x\":400");
            if (problem == "blocked-spawn") bytes[0] = 0;
            if (problem == "length") bytes = new byte[5];
            if (problem == "padding") bytes[2812] = 1;
            string mask = problem == "base64" ? "not:base64" : Convert.ToBase64String(bytes);
            var exception = Assert.Throws<InvalidOperationException>(() => CurrentMapData.Parse("main", json, mask));
            Assert.That(exception.Message, Does.Contain("main"));
        }

        [TestCase("main", 1u)]
        [TestCase("island", 2u)]
        [TestCase("village", 3u)]
        public void DeliveredMapBuildsMatchingTextureNavigationAndNoLegacyLayers(string key, uint scene)
        {
            var definition = CurrentMapCatalog.Find(key);
            Assert.That(CurrentMapCatalog.Find(scene), Is.SameAs(definition));
            var texture = definition.LoadTexture();
            Assert.That(texture.width, Is.EqualTo(1254));
            Assert.That(texture.height, Is.EqualTo(1254));
            Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(texture.mipmapCount, Is.EqualTo(1));
            var parent = new GameObject("CurrentMapResourceTest");
            TianyongMapInstance map = null;
            try
            {
                map = CurrentMapBuilder.Build(parent.transform, definition);
                Assert.That(map.Navigation.IsWalkable(definition.Spawn), Is.True);
                Assert.That(map.Root.transform.childCount, Is.EqualTo(2));
                var ground = map.Root.transform.Find(CurrentMapBuilder.GroundName);
                Assert.That(ground.GetComponent<MeshRenderer>().sharedMaterial.mainTexture, Is.SameAs(texture));
                Assert.That(ground.GetComponent<MeshRenderer>().sharedMaterial.renderQueue,
                    Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.Geometry),
                    "Even a sprite shader fallback must render the ground before character transparency.");
                Assert.That(ground.localScale, Is.EqualTo(new Vector3(300, 300, 1)));
                Assert.That(ground.GetComponent<Collider>(), Is.Null);
                Assert.That(map.Root.GetComponentsInChildren<BoxCollider>().Length, Is.EqualTo(1));
                Assert.That(map.Root.GetComponent<CityTileStreaming>(), Is.Null);
                Assert.That(map.Root.GetComponent<CurrentMapTileStreaming>(), Is.Not.Null);
                Assert.That(map.Root.GetComponentInChildren<TianyongPaintedForeground>(), Is.Null);
                Assert.That(map.Root.GetComponentInChildren<TianyongCityNpcs>(), Is.Null);
                int walkable = 0;
                for (int row = 0; row < 150; row++)
                for (int column = 0; column < 150; column++)
                {
                    var point = new Vector3(51 + column * 2, 0, 299 - row * 2);
                    Assert.That(map.Navigation.IsWalkable(point), Is.EqualTo(definition.IsWalkable(point)));
                    if (definition.IsWalkable(point)) walkable++;
                }
                Assert.That(walkable, Is.InRange(1, 22499));
            }
            finally { map?.Dispose(); Object.DestroyImmediate(parent); }
        }

        [Test]
        public void MissingResourceLeavesNoPartialMapUnderParent()
        {
            var constructor = typeof(CurrentMapDefinition).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(string), typeof(string), typeof(uint) }, null);
            var missing = (CurrentMapDefinition)constructor.Invoke(new object[] { "__missing_delivery", "Missing", 999u });
            var parent = new GameObject("MissingCurrentMapTest");
            try
            {
                var error = Assert.Throws<InvalidOperationException>(() => CurrentMapBuilder.Build(parent.transform, missing));
                Assert.That(error.Message, Does.Contain(missing.TexturePath));
                Assert.That(parent.transform.childCount, Is.Zero);
            }
            finally { Object.DestroyImmediate(parent); }
        }

        [TestCase("main", true)]
        [TestCase("island", true)]
        [TestCase("village", true)]
        [TestCase("main", false)]
        [TestCase("island", false)]
        [TestCase("village", false)]
        public void FarOffMapServerCorrectionRecoversToThisMapsWalkableSpawn(string key, bool explicitSpawn)
        {
            var definition = CurrentMapCatalog.Find(key);
            var navigation = definition.CreateNavigation();
            var invalid = new Vector3(5f, 0f, 5f);
            Assert.That(navigation.TryFindNearestWalkable(invalid, out _), Is.False,
                "Exercise the recovery fallback, not the nearby-cell path.");
            var actor = new GameObject("CurrentMapRecoveryTest");
            try
            {
                actor.transform.position = explicitSpawn ? invalid : definition.Spawn;
                var controller = actor.AddComponent<TianyongPlayerController>();
                controller.Initialize(null, navigation, null, null, explicitSpawn ? definition.Spawn : (Vector3?)null);
                Assert.That(Vector3.Distance(controller.FeetPosition, definition.Spawn), Is.LessThan(.001f));
                Assert.That(controller.WarpFromServer(invalid), Is.True);
                Assert.That(navigation.IsWalkable(controller.FeetPosition), Is.True);
                Assert.That(Vector3.Distance(controller.FeetPosition, definition.Spawn), Is.LessThan(.001f));
            }
            finally { Object.DestroyImmediate(actor); }
        }

        [Test]
        public void RuntimeSceneRoundTripRebindsNavigationAndCannotRestoreOldArt()
        {
            var root = new GameObject("CurrentMapRuntimeTest");
            var actor = new GameObject("CurrentMapActorTest");
            var runtime = root.AddComponent<TianyongMapRuntime>();
            var view = new ActorView { Go = actor, Kind = ActorKind.Player, Entity = 41 };
            try
            {
                runtime.Initialize(null, null, null, null);
                GameObject previous = null;
                foreach (uint scene in new[] { 1u, 2u, 3u, 1u })
                {
                    Invoke(runtime, "HandleLocalPlayerChanged", (object)null);
                    Invoke(runtime, "EnterScene", scene);
                    Assert.That(previous == null, Is.True);
                    actor.transform.position = runtime.CurrentSpawn;
                    Invoke(runtime, "HandleLocalPlayerChanged", view);
                    var controller = actor.GetComponent<TianyongPlayerController>();
                    Assert.That(controller.Navigation, Is.SameAs(runtime.Map.Navigation));
                    Assert.That(controller.Navigation.IsWalkable(controller.FeetPosition), Is.True);
                    var map = runtime.Map;
                    runtime.SetTheme(TianyongTheme.Snow);
                    runtime.SetFestivalAppearance(true);
                    Assert.That(runtime.Map, Is.SameAs(map));
                    Assert.That(runtime.FestivalAppearance, Is.False);
                    Assert.That(runtime.Theme, Is.EqualTo(TianyongTheme.City));
                    previous = map.Root;
                }
                Invoke(runtime, "HandleDisconnected");
                Assert.That(runtime.Map, Is.Null);
                Assert.That(actor.GetComponent<TianyongPlayerController>().enabled, Is.False);
                Invoke(runtime, "EnterScene", 4u);
                Assert.That(runtime.Map, Is.Null);
                Assert.That(CurrentMapCatalog.Find(4u), Is.Null);
                Invoke(runtime, "EnterScene", 999u);
                Assert.That(runtime.Map, Is.Null);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(actor); }
        }

        private static void Invoke(TianyongMapRuntime runtime, string name, params object[] arguments)
            => typeof(TianyongMapRuntime).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(runtime, arguments);
    }
}
