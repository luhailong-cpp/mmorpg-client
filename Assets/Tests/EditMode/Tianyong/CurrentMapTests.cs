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
        private static readonly string Sha = new('a', 64);
        private static readonly string Source = "{\"schemaVersion\":2,\"key\":\"main\",\"sceneId\":1," +
            "\"revision\":\"test-v2\",\"appearance\":\"spring\",\"artStatus\":\"formal_tiles_overview\"," +
            "\"layoutSha256\":\"" + Sha + "\"," +
            "\"layoutPixels\":{\"width\":4096,\"height\":4096},\"walkmaskColumns\":150,\"walkmaskRows\":150,\"cellSize\":2," +
            "\"worldRect\":{\"x\":50,\"y\":0,\"width\":300,\"height\":300}," +
            "\"effectiveWorldRect\":{\"x\":50,\"y\":0,\"width\":300,\"height\":300}," +
            "\"spawnWorld\":{\"x\":51,\"y\":0,\"z\":299}}";

        [TearDown]
        public void RestoreInstalledCatalog() => CurrentMapCatalog.ReloadDeliveries();

        private static MapDeliveryRegistration Registration(string key, uint scene, bool clientOnly)
        {
            var sha = new string('a', 64);
            return new MapDeliveryRegistration
            {
                key = key, sceneId = scene, name = "Offline navigation fixture", revision = "online-policy-test",
                clientOnly = clientOnly, defaultAppearance = "spring",
                appearances = new[] { new MapAppearanceDelivery {
                    id = "spring", artStatus = "formal_art_delivery", layoutSha256 = sha,
                    resourceRoot = MapDeliveryContract.ResourceRoot + key + "/online-policy-test/spring/" + sha + "/"
                } }
            };
        }

        [Test]
        public void OfflineOnlyMapRemainsDiscoverableWithoutOnlineFallback()
        {
            CurrentMapCatalog.ApplyDeliveries(new MapDeliveryCatalog { schemaVersion = 2,
                maps = new[] { Registration("island", 2u, true), Registration("unpublished-test", 42002u, true) } });

            Assert.That(CurrentMapCatalog.Find(2u).IsModern, Is.True);
            Assert.That(CurrentMapCatalog.Find(2u).Appearance, Is.EqualTo("spring"));
            Assert.That(CurrentMapCatalog.FindForOnline(2u), Is.Null);
            Assert.That(CurrentMapCatalog.FindForOnline("island"), Is.Null);
            Assert.That(CurrentMapCatalog.FindForOnline(42002u), Is.Null);
            Assert.That(CurrentMapCatalog.FindForOnline("unpublished-test"), Is.Null);
        }

        [Test]
        public void InstalledBajingBodySharesNavigationAcrossFestivalsAndEnablesOnlineTravel()
        {
            var spring = CurrentMapCatalog.Find(2u);
            Assert.That(spring.Revision, Is.EqualTo("bajingbody20261114"));
            Assert.That(spring.Appearance, Is.EqualTo("spring"));
            Assert.That(spring.CanTravelOnline, Is.True);
            var midAutumn = spring.ForAppearance("mid-autumn");
            Assert.That(midAutumn, Is.Not.Null);
            Assert.That(spring.SharesNavigationWith(midAutumn), Is.True);
            Assert.That(spring.Spawn, Is.EqualTo(new Vector3(205, 0, 70)));
            Assert.That(spring.EffectiveWorldRect.width, Is.EqualTo(420f));
            Assert.That(spring.EffectiveWorldRect.height, Is.EqualTo(420f));
            var navigation = spring.CreateNavigation();
            var centralTaiji = new Vector3(166.66667f, 0f, 200.41667f);
            Assert.That(navigation.FindPath(spring.Spawn, centralTaiji).Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(navigation.IsWalkable(new Vector3(-12.5f, 0f, 198.33333f)), Is.False);

            foreach (var appearance in new[] { spring, midAutumn })
            {
                var layout = appearance.LoadTexture();
                Assert.That(layout.width, Is.EqualTo(4096));
                Assert.That(layout.height, Is.EqualTo(2979));
                Assert.That(layout.mipmapCount, Is.EqualTo(1));
                var tile = Resources.Load<Texture2D>(appearance.ResourceFolder + "tiles/r08_c11");
                Assert.That(tile, Is.Not.Null);
                Assert.That(tile.width, Is.EqualTo(1024));
                Assert.That(tile.height, Is.EqualTo(1024));
            }

            var online = CurrentMapCatalog.FindForOnline(2u);
            Assert.That(online, Is.SameAs(spring));
            Assert.That(online.IsModern, Is.True);
            Assert.That(online.CanTravelOnline, Is.True);
            Assert.That(online.ForAppearance("spring"), Is.Not.Null);
        }

        [Test]
        public void TemporaryMainCityReusesBiyuanAssetsAndEnablesOnlineTravel()
        {
            var main = CurrentMapCatalog.Find(1u);
            var biyuan = CurrentMapCatalog.Find(5u);
            Assert.That(main, Is.Not.Null);
            Assert.That(biyuan, Is.Not.Null);
            Assert.That(main.Key, Is.EqualTo("main"));
            Assert.That(main.AssetKey, Is.EqualTo("biyuan-water-palace"));
            Assert.That(main.AssetSceneConfigId, Is.EqualTo(5u));
            Assert.That(main.CanTravelOnline, Is.True);
            Assert.That(CurrentMapCatalog.FindForOnline(1u), Is.SameAs(main));
            Assert.That(CurrentMapCatalog.FindForOnline("main"), Is.SameAs(main));
            Assert.That(main.SharesNavigationWith(biyuan), Is.True);
            Assert.That(main.Spawn, Is.EqualTo(biyuan.Spawn));
        }

        [Test]
        public void InstalledBiyuanBodySharesNavigationAcrossFestivalsAndEnablesOnlineTravel()
        {
            var spring = CurrentMapCatalog.Find(5u);
            Assert.That(spring, Is.Not.Null);
            Assert.That(spring.Key, Is.EqualTo("biyuan-water-palace"));
            Assert.That(spring.Revision, Is.EqualTo("biyuan-body-20261123"));
            Assert.That(spring.Appearance, Is.EqualTo("spring"));
            Assert.That(spring.CanTravelOnline, Is.True);
            Assert.That(CurrentMapCatalog.FindForOnline(5u), Is.SameAs(spring));
            Assert.That(CurrentMapCatalog.FindForOnline("biyuan-water-palace"), Is.SameAs(spring));

            var midAutumn = spring.ForAppearance("mid-autumn");
            Assert.That(midAutumn, Is.Not.Null);
            Assert.That(spring.SharesNavigationWith(midAutumn), Is.True);
            Assert.That(spring.ForAppearance("normal"), Is.Null);
            Assert.That(spring.Spawn, Is.EqualTo(new Vector3(222.5f, 0f, 58.5f)));
            Assert.That(spring.WorldRect, Is.EqualTo(new Rect(0f, 0f, 320f, 256f)));
            Assert.That(spring.EffectiveWorldRect, Is.EqualTo(new Rect(0f, 8f, 320f, 240f)));

            var navigation = spring.CreateNavigation();
            var mainHallDoor = new Vector3(116.25f, 0f, 186f);
            var gongPavilion = new Vector3(261.25f, 0f, 185.375f);
            var teaPavilion = new Vector3(80f, 0f, 53.5f);
            Assert.That(navigation.FindPath(spring.Spawn, mainHallDoor).Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(navigation.FindPath(spring.Spawn, gongPavilion).Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(navigation.FindPath(spring.Spawn, teaPavilion).Count, Is.GreaterThanOrEqualTo(2));
            // 上下 128px 衬边、荷花池、东廊边水岸、茶亭屋檐、主殿屋顶均应阻挡。
            Assert.That(navigation.IsWalkable(new Vector3(160f, 0f, 252f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(160f, 0f, 4f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(200f, 0f, 143.5f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(245.625f, 0f, 144.75f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(80f, 0f, 74.75f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(112.5f, 0f, 216f)), Is.False);

            foreach (var appearance in new[] { spring, midAutumn })
            {
                var layout = appearance.LoadTexture();
                Assert.That(layout.width, Is.EqualTo(5120));
                Assert.That(layout.height, Is.EqualTo(4096));
                Assert.That(layout.mipmapCount, Is.EqualTo(1));
                var tile = Resources.Load<Texture2D>(appearance.ResourceFolder + "tiles/r04_c05");
                Assert.That(tile, Is.Not.Null);
                Assert.That(tile.width, Is.EqualTo(1024));
                Assert.That(tile.height, Is.EqualTo(1024));
            }
        }

        [Test]
        public void InstalledQinglanTownSharesNavigationAcrossFestivalsAndStaysClientOnly()
        {
            var spring = CurrentMapCatalog.Find(4u);
            Assert.That(spring, Is.Not.Null);
            Assert.That(spring.Key, Is.EqualTo("qinglan-town"));
            Assert.That(spring.Revision, Is.EqualTo("qinglan-20261127-v002"));
            Assert.That(spring.Appearance, Is.EqualTo("spring"));
            Assert.That(spring.CanTravelOnline, Is.False);
            Assert.That(CurrentMapCatalog.FindForOnline(4u), Is.Null);
            Assert.That(CurrentMapCatalog.FindForOnline("qinglan-town"), Is.Null);

            var midAutumn = spring.ForAppearance("mid-autumn");
            Assert.That(midAutumn, Is.Not.Null);
            Assert.That(midAutumn.CanTravelOnline, Is.False);
            Assert.That(spring.SharesNavigationWith(midAutumn), Is.True);
            Assert.That(spring.ForAppearance("normal"), Is.Null);
            Assert.That(spring.Spawn, Is.EqualTo(new Vector3(-83.6923828125f, 0f, -65.1943359375f)));
            Assert.That(spring.WorldRect, Is.EqualTo(new Rect(-154f, -154f, 308f, 308f)));
            Assert.That(spring.EffectiveWorldRect, Is.EqualTo(new Rect(-154f, -95.197265625f, 308f, 190.39453125f)));

            var navigation = spring.CreateNavigation();
            var centralStoneBridge = new Vector3(-22.7841796875f, 0f, -14.0615234375f);
            var neCourtyard = new Vector3(82.865234375f, 0f, 40.455078125f);
            var nwWoodBridge = new Vector3(-81.4365234375f, 0f, 40.0791015625f);
            var seWoodBridge = new Vector3(64.4423828125f, 0f, -62.9384765625f);
            Assert.That(navigation.FindPath(spring.Spawn, centralStoneBridge).Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(navigation.FindPath(spring.Spawn, neCourtyard).Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(navigation.FindPath(spring.Spawn, nwWoodBridge).Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(navigation.FindPath(spring.Spawn, seWoodBridge).Count, Is.GreaterThanOrEqualTo(2));
            // 上下透明留白、溪流、西南牌坊屋顶、西市桌摊、北圃菜地、东北主殿屋顶均应阻挡。
            Assert.That(navigation.IsWalkable(new Vector3(0f, 0f, 123.921875f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(0f, 0f, -124.22265625f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(-17.14453125f, 0f, -27.97265625f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(-108.8828125f, 0f, -57.298828125f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(-63.765625f, 0f, -11.8056640625f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(-23.912109375f, 0f, 56.998046875f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(97.15234375f, 0f, 68.27734375f)), Is.False);

            foreach (var appearance in new[] { spring, midAutumn })
            {
                var layout = appearance.LoadTexture();
                Assert.That(layout.width, Is.EqualTo(4096));
                Assert.That(layout.height, Is.EqualTo(4096));
                Assert.That(layout.mipmapCount, Is.EqualTo(1));
                foreach (var tileId in new[] { "r01_c01", "r04_c04" })
                {
                    var tile = Resources.Load<Texture2D>(appearance.ResourceFolder + "tiles/" + tileId);
                    Assert.That(tile, Is.Not.Null);
                    Assert.That(tile.width, Is.EqualTo(1024));
                    Assert.That(tile.height, Is.EqualTo(1024));
                }
            }
        }

        [TestCase("http://127.0.0.1:18286", "127.0.0.1:28200", true, true)]
        [TestCase("http://[::1]:18286", "[::1]:28200", true, true)]
        [TestCase("http://127.0.0.1:18286", "127.0.0.1:28200", false, false)]
        [TestCase("http://127.0.0.1:18285", "127.0.0.1:28200", true, false)]
        [TestCase("http://127.0.0.1:18286", "127.0.0.1:28201", true, false)]
        [TestCase("http://192.0.2.1:18286", "127.0.0.1:28200", true, false)]
        [TestCase("http://127.0.0.1:18286", "192.0.2.1:28200", true, false)]
        public void YuexiBayRequiresExactIsolatedLoopbackEndpoints(string gateway, string gate, bool development, bool expected)
        {
            var arguments = new[] { "-yuexiBayLocalTest", "-yuexiBayGatewayPort", "18286", "-yuexiBayGatePort", "28200" };
            Assert.That(FestivalMapLocalTestPolicy.CanUseYuexiBay(development, arguments, gateway, gate), Is.EqualTo(expected));
        }

        [Test]
        public void YuexiBayRejectsSharedPortsAndIncompleteOrAmbiguousOptIn()
        {
            foreach (string gatewayPort in new[] { "8081", "18086" })
            {
                var arguments = new[] { "-yuexiBayLocalTest", "-yuexiBayGatewayPort", gatewayPort, "-yuexiBayGatePort", "28200" };
                Assert.That(FestivalMapLocalTestPolicy.CanUseYuexiBay(true, arguments, $"http://127.0.0.1:{gatewayPort}", "127.0.0.1:28200"), Is.False);
            }
            var sharedGate = new[] { "-yuexiBayLocalTest", "-yuexiBayGatewayPort", "18286", "-yuexiBayGatePort", "28000" };
            Assert.That(FestivalMapLocalTestPolicy.CanUseYuexiBay(true, sharedGate, "http://127.0.0.1:18286", "127.0.0.1:28000"), Is.False);
            foreach (var arguments in new[] {
                new[] { "-festivalMapLocalTest", "-yuexiBayGatewayPort", "18286", "-yuexiBayGatePort", "28200" },
                new[] { "-yuexiBayLocalTest", "-yuexiBayGatewayPort", "18286", "-yuexiBayGatePort" },
                new[] { "-yuexiBayLocalTest", "-yuexiBayGatewayPort", "18286", "-yuexiBayGatePort", "28200", "-yuexiBayGatePort", "28200" }
            })
                Assert.That(FestivalMapLocalTestPolicy.CanUseYuexiBay(true, arguments, "http://127.0.0.1:18286", "127.0.0.1:28200"), Is.False);
        }

        [Test]
        public void YuexiBayLocalTargetDoesNotAcceptOtherVillageRevisionsOrUnregisteredAppearances()
        {
            var registration = Registration("village", 3u, true);
            var catalog = new MapDeliveryCatalog { schemaVersion = 2, maps = new[] { registration } };
            CurrentMapCatalog.ApplyDeliveries(catalog);
            Assert.That(FestivalMapLocalTestPolicy.IsYuexiBayTarget(CurrentMapCatalog.Find(3u), "spring"), Is.False);
            registration.appearances[0].resourceRoot = registration.appearances[0].resourceRoot.Replace(registration.revision, "yuexi-bay-20261115");
            registration.revision = "yuexi-bay-20261115";
            CurrentMapCatalog.ApplyDeliveries(catalog);
            Assert.That(FestivalMapLocalTestPolicy.IsYuexiBayTarget(CurrentMapCatalog.Find(3u), "spring"), Is.True);
            Assert.That(FestivalMapLocalTestPolicy.IsYuexiBayTarget(CurrentMapCatalog.Find(3u), "mid-autumn"), Is.False);
            Assert.That(CurrentMapCatalog.FindForOnline(3u), Is.Null);
            Assert.That(FestivalMapLocalTestPolicy.IsYuexiBayTarget(CurrentMapCatalog.FindForOnline(3u), "spring"), Is.False);
        }

        [Test]
        public void YuexiBayKeepsNarrowDockMovementWhenPublishedForOnlineTravel()
        {
            var catalog = JsonUtility.FromJson<MapDeliveryCatalog>(Resources.Load<TextAsset>(MapDeliveryContract.CatalogPath).text);
            var registration = Array.Find(catalog.maps, entry => entry.sceneId == 3 && entry.revision == "yuexi-bay-20261115");
            Assert.That(registration, Is.Not.Null);
            var from = new Vector3(59.1f, 0f, 44.375f);
            var to = new Vector3(59.4f, 0f, 44.375f);

            registration.clientOnly = true;
            CurrentMapCatalog.ApplyDeliveries(catalog);
            var installed = CurrentMapCatalog.Find(3u);
            Assert.That(installed.CreateNavigation().IsSegmentWalkable(from, to), Is.True);
            var oldClearance = new TianyongNavigationGrid(.5f, installed.IsWalkable, installed.WorldRect, installed.EffectiveWorldRect);
            Assert.That(oldClearance.IsSegmentWalkable(from, to), Is.False);

            registration.clientOnly = false;
            CurrentMapCatalog.ApplyDeliveries(catalog);
            var online = CurrentMapCatalog.FindForOnline(3u);
            Assert.That(online.CreateNavigation().IsSegmentWalkable(from, to), Is.True);
            Assert.That(FestivalMapLocalTestPolicy.IsYuexiBayTarget(online, "spring"), Is.False);
        }

        [Test]
        public void PublishedReplacementAndNewSceneUseTheirPublishedDefinitions()
        {
            CurrentMapCatalog.ApplyDeliveries(new MapDeliveryCatalog { schemaVersion = 2,
                maps = new[] { Registration("island", 2u, false), Registration("published-test", 42002u, false) } });
            foreach (uint id in new[] { 2u, 42002u })
            {
                Assert.That(CurrentMapCatalog.FindForOnline(id), Is.SameAs(CurrentMapCatalog.Find(id)));
                Assert.That(CurrentMapCatalog.FindForOnline(id).Appearance, Is.EqualTo("spring"));
                Assert.That(CurrentMapCatalog.FindForOnline(id).IsModern, Is.True);
            }
        }

        [Test]
        public void FirstServerEntryCannotInstallOfflineOnlyDelivery()
        {
            var root = new GameObject("OfflineReplacementFirstServerEntry");
            var runtime = root.AddComponent<TianyongMapRuntime>();
            try
            {
                runtime.Initialize(null, null, null, null);
                CurrentMapCatalog.ApplyDeliveries(new MapDeliveryCatalog { schemaVersion = 2,
                    maps = new[] { Registration("island", 2u, true) } });
                Invoke(runtime, "HandleSceneEntered", new SceneInfoComp { SceneConfigId = 2u });
                Assert.That(runtime.Map, Is.Null);
                Assert.That(runtime.ActiveSceneConfigId, Is.Zero);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ServerEntryForUnpublishedSceneWithoutLegacyLeavesNoMap()
        {
            CurrentMapCatalog.ApplyDeliveries(new MapDeliveryCatalog { schemaVersion = 2,
                maps = new[] { Registration("unpublished-test", 42002u, true) } });
            var root = new GameObject("UnpublishedServerScene");
            var runtime = root.AddComponent<TianyongMapRuntime>();
            try
            {
                runtime.Initialize(null, null, null, null);
                Invoke(runtime, "HandleSceneEntered", new SceneInfoComp { SceneConfigId = 42002u });
                Assert.That(runtime.Map, Is.Null);
                Assert.That(runtime.ActiveSceneConfigId, Is.Zero);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void MaskUsesNorthWestOriginMostSignificantBitAndBlockedExterior()
        {
            var bytes = new byte[2813];
            bytes[0] = 0x80;
            bytes[2812] = 0x10;
            var data = CurrentMapData.Parse("main", 1u, Source, Convert.ToBase64String(bytes));
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
            var data = CurrentMapData.Parse("main", 1u, Source, Convert.ToBase64String(bytes));
            var navigation = new TianyongNavigationGrid(2f, data.IsWalkable);
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
            if (problem == "version") json = json.Replace("schemaVersion\":2", "schemaVersion\":1");
            if (problem == "identity") json = json.Replace("sceneId\":1", "sceneId\":2");
            if (problem == "layout-size") json = json.Replace("4096", "9000");
            if (problem == "mask-size") json = json.Replace("walkmaskColumns\":150", "walkmaskColumns\":149");
            if (problem == "cell-size") json = json.Replace("cellSize\":2", "cellSize\":4");
            if (problem == "world-bounds") json = json.Replace("\"worldRect\":{\"x\":50,\"y\":0,\"width\":300", "\"worldRect\":{\"x\":50,\"y\":0,\"width\":400");
            if (problem == "spawn-outside") json = json.Replace("x\":51", "x\":400");
            if (problem == "blocked-spawn") bytes[0] = 0;
            if (problem == "length") bytes = new byte[5];
            if (problem == "padding") bytes[2812] = 1;
            string mask = problem == "base64" ? "not:base64" : Convert.ToBase64String(bytes);
            var exception = Assert.Throws<InvalidOperationException>(() => CurrentMapData.Parse("main", 1u, json, mask));
            Assert.That(exception.Message, Does.Contain("main"));
        }

        [TestCase("main", 1u)]
        [TestCase("island", 2u)]
        [TestCase("village", 3u)]
        [TestCase("biyuan-water-palace", 5u)]
        public void DeliveredMapBuildsMatchingTextureNavigationAndNoLegacyLayers(string key, uint scene)
        {
            var definition = CurrentMapCatalog.FindForOnline(key);
            Assert.That(definition, Is.Not.Null);
            Assert.That(CurrentMapCatalog.FindForOnline(scene), Is.SameAs(definition));
            var texture = definition.LoadTexture();
            Assert.That(texture.width, Is.GreaterThanOrEqualTo(1024));
            Assert.That(texture.height, Is.GreaterThanOrEqualTo(1024));
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
                Assert.That(ground.localScale, Is.EqualTo(new Vector3(definition.WorldRect.width, definition.WorldRect.height, 1)));
                Assert.That(ground.GetComponent<Collider>(), Is.Null);
                Assert.That(map.Root.GetComponentsInChildren<BoxCollider>().Length, Is.EqualTo(1));
                Assert.That(map.Root.GetComponent<CurrentMapTileStreaming>(), Is.Not.Null);
                Assert.That(map.Root.GetComponentInChildren<TianyongPaintedForeground>(), Is.Null);
                Assert.That(map.Root.GetComponentInChildren<TianyongCityNpcs>(), Is.Null);
            }
            finally { map?.Dispose(); Object.DestroyImmediate(parent); }
        }

        [Test]
        public void MissingResourceLeavesNoPartialMapUnderParent()
        {
            var registration = Registration("missing-delivery", 999u, false);
            var constructor = typeof(CurrentMapDefinition).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(MapDeliveryRegistration), typeof(string) }, null);
            var missing = (CurrentMapDefinition)constructor.Invoke(new object[] { registration, "spring" });
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
        [TestCase("biyuan-water-palace", true)]
        [TestCase("main", false)]
        [TestCase("island", false)]
        [TestCase("village", false)]
        [TestCase("biyuan-water-palace", false)]
        public void FarOffMapServerCorrectionRecoversToThisMapsWalkableSpawn(string key, bool explicitSpawn)
        {
            var definition = CurrentMapCatalog.FindForOnline(key);
            var navigation = definition.CreateNavigation();
            var invalid = new Vector3(-500f, 0f, -500f);
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
                foreach (uint scene in new[] { 1u, 2u, 3u, 5u, 1u })
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
                    Assert.That(runtime.Map, Is.SameAs(map));
                    Assert.That(runtime.Appearance, Is.EqualTo("spring"));
                    Assert.That(runtime.Theme, Is.EqualTo(TianyongTheme.City));
                    previous = map.Root;
                }
                Invoke(runtime, "HandleDisconnected");
                Assert.That(runtime.Map, Is.Null);
                Assert.That(actor.GetComponent<TianyongPlayerController>().enabled, Is.False);
                Invoke(runtime, "EnterScene", 4u);
                Assert.That(runtime.Map, Is.Null);
                Assert.That(CurrentMapCatalog.FindForOnline(4u), Is.Null);
                Assert.That(CurrentMapCatalog.Find(4u)?.Key, Is.EqualTo("qinglan-town"));
                Assert.That(CurrentMapCatalog.Find(4u).CanTravelOnline, Is.False);
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
