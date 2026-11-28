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
        public void InstalledQinglanTownSharesNavigationAcrossFestivalsAndEnablesOnlineTravel()
        {
            var spring = CurrentMapCatalog.Find(4u);
            Assert.That(spring, Is.Not.Null);
            Assert.That(spring.Key, Is.EqualTo("qinglan-town"));
            Assert.That(spring.Revision, Is.EqualTo("qinglan-20261127-v002"));
            Assert.That(spring.Appearance, Is.EqualTo("spring"));
            Assert.That(spring.CanTravelOnline, Is.True);
            Assert.That(CurrentMapCatalog.FindForOnline(4u), Is.SameAs(spring));
            Assert.That(CurrentMapCatalog.FindForOnline("qinglan-town"), Is.SameAs(spring));

            var midAutumn = spring.ForAppearance("mid-autumn");
            Assert.That(midAutumn, Is.Not.Null);
            Assert.That(midAutumn.CanTravelOnline, Is.True);
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

        [Test]
        public void InstalledCanglanHarborSharesNavigationAcrossFestivalsAndEnablesOnlineTravel()
        {
            var spring = CurrentMapCatalog.Find(6u);
            Assert.That(spring, Is.Not.Null);
            Assert.That(spring.Key, Is.EqualTo("canglan-harbor"));
            Assert.That(spring.Name, Is.EqualTo("沧澜港"));
            Assert.That(spring.Revision, Is.EqualTo("canglan-harbor-20261128-v001"));
            Assert.That(spring.Appearance, Is.EqualTo("spring"));
            Assert.That(spring.CanTravelOnline, Is.True);
            Assert.That(CurrentMapCatalog.FindForOnline(6u), Is.SameAs(spring));
            Assert.That(CurrentMapCatalog.FindForOnline("canglan-harbor"), Is.SameAs(spring));

            var midAutumn = spring.ForAppearance("mid-autumn");
            Assert.That(midAutumn, Is.Not.Null);
            Assert.That(midAutumn.CanTravelOnline, Is.True);
            Assert.That(spring.SharesNavigationWith(midAutumn), Is.True);
            Assert.That(spring.ForAppearance("normal"), Is.Null);
            Assert.That(spring.Spawn.x, Is.EqualTo(23.110906512290242f).Within(0.001f));
            Assert.That(spring.Spawn.y, Is.EqualTo(0f));
            Assert.That(spring.Spawn.z, Is.EqualTo(129.4218017965555f).Within(0.001f));
            Assert.That(spring.WorldRect, Is.EqualTo(new Rect(0f, -49.52916717529297f, 369.52911376953125f, 369.52911376953125f)));
            Assert.That(spring.EffectiveWorldRect.xMin, Is.EqualTo(0f));
            Assert.That(spring.EffectiveWorldRect.yMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(spring.EffectiveWorldRect.xMax, Is.LessThanOrEqualTo(320f));
            Assert.That(spring.EffectiveWorldRect.yMax, Is.LessThanOrEqualTo(320f));

            var navigation = spring.CreateNavigation();
            var destinations = new[]
            {
                new Vector3(120.889067839f, 0f, 126.221802367f), // 中央石阶
                new Vector3(148.266339535f, 0f, 120.533616179f), // 中央码头
                new Vector3(239.644502001f, 0f, 242.844505022f), // 北鱼市
                new Vector3(288.710859205f, 0f, 247.466325457f), // 北码头
                new Vector3(196.266330985f, 0f, 44.4445403606f), // 船厂前场
            };
            foreach (var target in destinations)
            {
                var path = navigation.FindPath(spring.Spawn, target);
                Assert.That(path.Count, Is.GreaterThanOrEqualTo(2), "Path missing to " + target);
                Assert.That(Vector3.Distance(path[path.Count - 1], target), Is.LessThan(2f), "Path did not reach " + target);
            }
            // 水面、北鱼市货摊、官署室内、船体与右/下填充边均应阻挡。
            Assert.That(navigation.IsWalkable(new Vector3(270.22177312672136f, 0f, 159.99997509270906f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(195.55542048290374f, 0f, 230.75541780441995f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(142.22179592549801f, 0f, 99.19998592212798f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(145.0663401044905f, 0f, 190.2217909671366f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(331.99881315231323f, 0f, 256.84799844026566f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(72.17365503311157f, 0f, -15.60754930973053f)), Is.False);

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

        [Test]
        public void InstalledLingheFerrySharesNavigationAcrossFestivalsAndEnablesOnlineTravel()
        {
            var spring = CurrentMapCatalog.Find(7u);
            Assert.That(spring, Is.Not.Null);
            Assert.That(spring.Key, Is.EqualTo("linghe-ferry"));
            Assert.That(spring.Name, Is.EqualTo("灵禾渡"));
            Assert.That(spring.Revision, Is.EqualTo("linghe-ferry-20261128-v001"));
            Assert.That(spring.Appearance, Is.EqualTo("spring"));
            Assert.That(spring.CanTravelOnline, Is.True);
            Assert.That(CurrentMapCatalog.FindForOnline(7u), Is.SameAs(spring));
            Assert.That(CurrentMapCatalog.FindForOnline("linghe-ferry"), Is.SameAs(spring));

            var midAutumn = spring.ForAppearance("mid-autumn");
            Assert.That(midAutumn, Is.Not.Null);
            Assert.That(midAutumn.CanTravelOnline, Is.True);
            Assert.That(spring.SharesNavigationWith(midAutumn), Is.True);
            Assert.That(spring.ForAppearance("normal"), Is.Null);
            Assert.That(spring.Spawn.x, Is.EqualTo(56.667327880859375f).Within(0.001f));
            Assert.That(spring.Spawn.y, Is.EqualTo(0f));
            Assert.That(spring.Spawn.z, Is.EqualTo(134.8682403564453f).Within(0.001f));
            Assert.That(spring.WorldRect, Is.EqualTo(new Rect(0f, -56.2139892578125f, 348.1640625f, 464.21875f)));
            Assert.That(spring.EffectiveWorldRect, Is.EqualTo(new Rect(0f, 0f, 340.00396728515625f, 408.0047607421875f)));

            var navigation = spring.CreateNavigation();
            var destinations = new[]
            {
                new Vector3(41.08381271362305f, 0f, 92.93441772460938f),  // 西南太极牌坊
                new Vector3(129.20150756835938f, 0f, 155.2684783935547f), // 西渡口栈桥
                new Vector3(55.53398132324219f, 0f, 268.03646087646484f), // 双粮仓西前场
                new Vector3(138.26828002929688f, 0f, 307.1369171142578f), // 西北农舍石阶
                new Vector3(170.00198364257812f, 0f, 240.26947021484375f),// 主木桥桥面
                new Vector3(279.9365997314453f, 0f, 270.8698272705078f),  // 东主屋月台
                new Vector3(250.46958923339844f, 0f, 336.3205909729004f), // 东北水车坊门前
                new Vector3(248.20289611816406f, 0f, 173.68535995483398f),// 东渡口栈桥
                new Vector3(285.60333251953125f, 0f, 194.65227127075195f),// 东岸小屋前
            };
            foreach (var target in destinations)
            {
                var path = navigation.FindPath(spring.Spawn, target);
                Assert.That(path.Count, Is.GreaterThanOrEqualTo(2), "Path missing to " + target);
                Assert.That(Vector3.Distance(path[path.Count - 1], target), Is.LessThan(2f), "Path did not reach " + target);
            }
            // 河道、乌篷船、粮仓木车、主桥护栏、东渡口凉亭以及右/底/右下透明填充边均应阻挡。
            Assert.That(navigation.IsWalkable(new Vector3(164.3352508544922f, 0f, 323.00376892089844f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(173.4020233154297f, 0f, 156.40182495117188f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(70.83415985107422f, 0f, 264.0697479248047f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(170.00198364257812f, 0f, 248.76956939697266f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(170.00198364257812f, 0f, 232.33604431152344f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(249.33624267578125f, 0f, 184.16881561279297f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(344.0840148925781f, 0f, 204.00238037109375f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(170.00198364257812f, 0f, -28.333663940429688f)), Is.False);
            Assert.That(navigation.IsWalkable(new Vector3(344.0840148925781f, 0f, -28.333663940429688f)), Is.False);

            foreach (var appearance in new[] { spring, midAutumn })
            {
                var layout = appearance.LoadTexture();
                Assert.That(layout.width, Is.EqualTo(6144));
                Assert.That(layout.height, Is.EqualTo(8192));
                Assert.That(layout.mipmapCount, Is.EqualTo(1));
                foreach (var tileId in new[] { "r01_c01", "r01_c06", "r08_c01", "r08_c06" })
                {
                    var tile = Resources.Load<Texture2D>(appearance.ResourceFolder + "tiles/" + tileId);
                    Assert.That(tile, Is.Not.Null);
                    Assert.That(tile.width, Is.EqualTo(1024));
                    Assert.That(tile.height, Is.EqualTo(1024));
                }
            }
        }

        [Test]
        public void InstalledWaterwheelCourtyardSharesNavigationAcrossFestivalsAndCoversBridgeStairsAndRoads()
        {
            var spring = CurrentMapCatalog.Find(20000u);
            Assert.That(spring, Is.Not.Null);
            Assert.That(spring.Key, Is.EqualTo("waterwheel-courtyard"));
            Assert.That(spring.Name, Is.EqualTo("水车庭院"));
            Assert.That(spring.Revision, Is.EqualTo("waterwheel-hd8-20261128"));
            Assert.That(spring.Appearance, Is.EqualTo("spring"));
            Assert.That(spring.CanTravelOnline, Is.True);
            Assert.That(CurrentMapCatalog.Find("waterwheel-courtyard"), Is.SameAs(spring));
            Assert.That(CurrentMapCatalog.FindForOnline(20000u), Is.SameAs(spring));
            Assert.That(CurrentMapCatalog.FindForOnline("waterwheel-courtyard"), Is.SameAs(spring));

            var midAutumn = spring.ForAppearance("mid-autumn");
            Assert.That(midAutumn, Is.Not.Null);
            Assert.That(midAutumn.CanTravelOnline, Is.True);
            Assert.That(spring.SharesNavigationWith(midAutumn), Is.True);
            Assert.That(spring.ForAppearance("normal"), Is.Null);
            Assert.That(spring.Spawn, Is.EqualTo(new Vector3(160f, 0f, 140f)));
            Assert.That(spring.WorldRect, Is.EqualTo(new Rect(0f, 0f, 320f, 320f)));
            Assert.That(spring.EffectiveWorldRect, Is.EqualTo(new Rect(0f, 32.578125f, 297.1484375f, 287.421875f)));

            var navigation = spring.CreateNavigation();
            var destinations = new[]
            {
                new Vector3(196.09375f, 0f, 237.1875f), // 主殿台阶顶月台
                new Vector3(126.5625f, 0f, 314.53125f), // 北上山道石阶顶
                new Vector3(79.6875f, 0f, 176.25f),     // 西铺石阶
                new Vector3(225.78125f, 0f, 132.5f),    // 东侧主路
                new Vector3(236.71875f, 0f, 87.96875f), // 东南小殿台阶顶
                new Vector3(148.4375f, 0f, 87.1875f),   // 石拱桥桥顶
                new Vector3(80.46875f, 0f, 80.546875f), // 临水亭通道
                new Vector3(42.1875f, 0f, 80.15625f),   // 西侧临水观景台
            };
            foreach (var target in destinations)
            {
                var path = navigation.FindPath(spring.Spawn, target);
                Assert.That(path.Count, Is.GreaterThanOrEqualTo(2), "Path missing to " + target);
                Assert.That(Vector3.Distance(path[path.Count - 1], target), Is.LessThan(1.5f), "Path did not reach " + target);
            }

            // 瀑布、水车、西侧河流、南侧荷池、主殿屋顶、桥北栏杆、石灯笼、临水亭玉兔灯与右/下填充边均应阻挡。
            var blocked = new[]
            {
                new Vector3(22.65625f, 0f, 273.125f),   // 瀑布
                new Vector3(52.34375f, 0f, 261.40625f), // 水车
                new Vector3(11.71875f, 0f, 124.6875f),  // 西侧河流
                new Vector3(146.484375f, 0f, 54.375f),  // 南侧荷池
                new Vector3(203.125f, 0f, 277.03125f),  // 主殿屋顶
                new Vector3(148.4375f, 0f, 94.21875f),  // 石拱桥北栏杆
                new Vector3(200.78125f, 0f, 86.40625f), // 东南石灯笼
                new Vector3(80.6640625f, 0f, 74.296875f), // 临水亭中秋玉兔灯
                new Vector3(308.59375f, 0f, 163.75f),   // 右侧有效画幅外填充区
                new Vector3(156.25f, 0f, 15.3125f),     // 下侧有效画幅外填充区
            };
            foreach (var point in blocked)
                Assert.That(navigation.IsWalkable(point), Is.False, "Expected blocked: " + point);

            foreach (var appearance in new[] { spring, midAutumn })
            {
                var layout = appearance.LoadTexture();
                Assert.That(layout.width, Is.EqualTo(4096));
                Assert.That(layout.height, Is.EqualTo(4096));
                Assert.That(layout.mipmapCount, Is.EqualTo(1));
                Assert.That(layout.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
                foreach (var tileId in new[] { "r01_c01", "r01_c02", "r02_c01", "r08_c08" })
                {
                    var tile = Resources.Load<Texture2D>(appearance.ResourceFolder + "tiles/" + tileId);
                    Assert.That(tile, Is.Not.Null, tileId);
                    Assert.That(tile.width, Is.EqualTo(1024));
                    Assert.That(tile.height, Is.EqualTo(1024));
                    Assert.That(tile.mipmapCount, Is.EqualTo(1));
                }
            }
        }

        [Test]
        public void InstalledMap19003SharesNavigationAcrossFestivalsAndCoversBridgesCourtyardsAndEntries()
        {
            var spring = CurrentMapCatalog.Find(19003u);
            Assert.That(spring, Is.Not.Null);
            Assert.That(spring.Key, Is.EqualTo("map19003"));
            Assert.That(spring.Name, Is.EqualTo("地图 19003"));
            Assert.That(spring.Revision, Is.EqualTo("pc19003-hd8-20261128"));
            Assert.That(spring.Appearance, Is.EqualTo("spring"));
            Assert.That(spring.CanTravelOnline, Is.False);
            Assert.That(CurrentMapCatalog.Find("map19003"), Is.SameAs(spring));
            Assert.That(CurrentMapCatalog.FindForOnline(19003u), Is.Null);
            Assert.That(CurrentMapCatalog.FindForOnline("map19003"), Is.Null);

            var midAutumn = spring.ForAppearance("mid-autumn");
            Assert.That(midAutumn, Is.Not.Null);
            Assert.That(midAutumn.CanTravelOnline, Is.False);
            Assert.That(spring.SharesNavigationWith(midAutumn), Is.True);
            Assert.That(spring.ForAppearance("normal"), Is.Null);
            Assert.That(spring.Spawn, Is.EqualTo(new Vector3(125f, 0f, 51.25f)));
            Assert.That(spring.WorldRect, Is.EqualTo(new Rect(0f, 0f, 240f, 120f)));
            Assert.That(spring.EffectiveWorldRect, Is.EqualTo(new Rect(0f, 0.46875f, 212.5f, 119.53125f)));

            var navigation = spring.CreateNavigation();
            var destinations = new[]
            {
                new Vector3(125.0f, 0f, 71.7578125f),      // 亭台前阶
                new Vector3(173.4375f, 0f, 59.0625f),      // 东侧主门
                new Vector3(206.25f, 0f, 58.28125f),       // 东边入口
                new Vector3(154.296875f, 0f, 98.125f),     // 东北宽石桥中段
                new Vector3(156.640625f, 0f, 116.875f),    // 北边东侧入口
                new Vector3(125.78125f, 0f, 110.8203125f), // 北岸弯道
                new Vector3(57.03125f, 0f, 82.890625f),    // 西侧短木桥中段
                new Vector3(5.46875f, 0f, 93.6328125f),    // 西边上部入口
                new Vector3(15.625f, 0f, 42.65625f),       // 西南外环步道
                new Vector3(67.1875f, 0f, 42.65625f),      // 药圃小院内东侧
                new Vector3(51.5625f, 0f, 36.2109375f),    // 药圃小院内南侧
                new Vector3(107.8125f, 0f, 5.15625f),      // 南边中部入口
                new Vector3(148.2421875f, 0f, 35.0390625f),// 主院南侧台阶
                new Vector3(183.984375f, 0f, 43.828125f),  // 东南外环步道
            };
            foreach (var target in destinations)
            {
                var path = navigation.FindPath(spring.Spawn, target);
                Assert.That(path.Count, Is.GreaterThanOrEqualTo(2), "Path missing to " + target);
                Assert.That(Vector3.Distance(path[path.Count - 1], target), Is.LessThan(1.5f), "Path did not reach " + target);
            }

            // 水渠、瀑布、桥栏、亭顶、双节装饰并集、药圃花坛、假山松树、护栏立柱与右/下填充区均应阻挡。
            var blocked = new[]
            {
                new Vector3(226.5625f, 0f, 61.40625f),     // 右侧 704px 填充区
                new Vector3(105.46875f, 0f, 0.234375f),    // 下侧 12px 填充区
                new Vector3(49.21875f, 0f, 98.125f),       // 西北瀑布水潭
                new Vector3(100.0f, 0f, 91.09375f),        // 中央弯渠水面
                new Vector3(178.125f, 0f, 95.78125f),      // 东侧水面
                new Vector3(194.53125f, 0f, 16.09375f),    // 东南瀑布
                new Vector3(56.25f, 0f, 87.96875f),        // 短木桥北护栏
                new Vector3(144.53125f, 0f, 98.125f),      // 宽石桥西护栏
                new Vector3(164.84375f, 0f, 98.125f),      // 宽石桥东护栏
                new Vector3(125.0f, 0f, 81.71875f),        // 亭台屋顶
                new Vector3(157.03125f, 0f, 65.3125f),     // 主院东侧双节桌案并集
                new Vector3(102.34375f, 0f, 59.84375f),    // 主院西侧中秋桂花兔灯
                new Vector3(50.78125f, 0f, 46.5625f),      // 药圃小院双节中央摆设并集
                new Vector3(32.03125f, 0f, 46.5625f),      // 药圃西侧花坛
                new Vector3(125.0f, 0f, 32.5f),            // 南侧假山松树
                new Vector3(176.171875f, 0f, 43.828125f),  // 东南主院护栏
            };
            foreach (var point in blocked)
                Assert.That(navigation.IsWalkable(point), Is.False, "Expected blocked: " + point);

            foreach (var appearance in new[] { spring, midAutumn })
            {
                var layout = appearance.LoadTexture();
                Assert.That(layout.width, Is.EqualTo(4096));
                Assert.That(layout.height, Is.EqualTo(2048));
                Assert.That(layout.mipmapCount, Is.EqualTo(1));
                Assert.That(layout.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
                foreach (var tileId in new[] { "r01_c01", "r01_c04", "r02_c03", "r03_c06" })
                {
                    var tile = Resources.Load<Texture2D>(appearance.ResourceFolder + "tiles/" + tileId);
                    Assert.That(tile, Is.Not.Null, tileId);
                    Assert.That(tile.width, Is.EqualTo(1024));
                    Assert.That(tile.height, Is.EqualTo(1024));
                    Assert.That(tile.mipmapCount, Is.EqualTo(1));
                }
            }
        }

        [Test]
        public void InstalledSummitSnowSharesNavigationAcrossFestivalsAndCoversStairsTerraceShrineAndLowerPoolRim()
        {
            var spring = CurrentMapCatalog.Find(18000u);
            Assert.That(spring, Is.Not.Null);
            Assert.That(spring.Key, Is.EqualTo("summit-snow"));
            Assert.That(spring.Name, Is.EqualTo("山顶雪区"));
            Assert.That(spring.Revision, Is.EqualTo("pc18000-summit-hd8-20261128"));
            Assert.That(spring.Appearance, Is.EqualTo("spring"));
            Assert.That(spring.CanTravelOnline, Is.True);
            Assert.That(CurrentMapCatalog.Find("summit-snow"), Is.SameAs(spring));
            Assert.That(CurrentMapCatalog.FindForOnline(18000u), Is.SameAs(spring));
            Assert.That(CurrentMapCatalog.FindForOnline("summit-snow"), Is.SameAs(spring));

            var midAutumn = spring.ForAppearance("mid-autumn");
            Assert.That(midAutumn, Is.Not.Null);
            Assert.That(midAutumn.CanTravelOnline, Is.True);
            Assert.That(spring.SharesNavigationWith(midAutumn), Is.True);
            Assert.That(spring.ForAppearance("normal"), Is.Null);
            Assert.That(spring.Spawn, Is.EqualTo(new Vector3(148.4375f, 0f, 249.84375f)));
            Assert.That(spring.WorldRect, Is.EqualTo(new Rect(0f, 0f, 320f, 360f)));
            Assert.That(spring.EffectiveWorldRect, Is.EqualTo(new Rect(0f, 6.484375f, 282.8125f, 353.515625f)));

            var navigation = spring.CreateNavigation();
            var destinations = new[]
            {
                new Vector3(9.375f, 0f, 352.96875f),      // 西北浮空石阶顶端
                new Vector3(121.09375f, 0f, 323.28125f),  // 山顶雪祠前院西侧
                new Vector3(153.125f, 0f, 335.0f),        // 山顶雪祠前院东侧
                new Vector3(169.53125f, 0f, 259.21875f),  // 圆台广场中心
                new Vector3(213.28125f, 0f, 269.375f),    // 圆台东侧雪台
                new Vector3(58.59375f, 0f, 212.34375f),   // 长雪道西南端
                new Vector3(127.34375f, 0f, 189.6875f),   // 中层雪台
                new Vector3(142.1875f, 0f, 133.4375f),    // 下中弯道平台
                new Vector3(146.875f, 0f, 52.1875f),      // 双石像下层雪台西侧
                new Vector3(197.65625f, 0f, 35.78125f),   // 双石像下层雪台南侧
                new Vector3(254.6875f, 0f, 61.5625f),     // 双石像下层雪台东侧
            };
            foreach (var target in destinations)
            {
                var path = navigation.FindPath(spring.Spawn, target);
                Assert.That(path.Count, Is.GreaterThanOrEqualTo(2), "Path missing to " + target);
                Assert.That(Vector3.Distance(path[path.Count - 1], target), Is.LessThan(1.5f), "Path did not reach " + target);
            }

            // 填充边、道祠主体、中秋圆台玉兔月饼案台、树干、冰瀑、双石像与雪池及左下角秋径均应阻挡。
            var blocked = new[]
            {
                new Vector3(300.78125f, 0f, 184.21875f),  // 右侧 952px 填充区
                new Vector3(140.625f, 0f, 3.359375f),     // 下侧 166px 填充区
                new Vector3(121.09375f, 0f, 346.71875f),  // 山顶道祠屋顶
                new Vector3(182.8125f, 0f, 268.59375f),   // 中秋圆台玉兔月饼案台
                new Vector3(174.609375f, 0f, 285.78125f), // 圆台北侧雪树干
                new Vector3(203.125f, 0f, 203.75f),       // 上段冰瀑
                new Vector3(203.125f, 0f, 133.4375f),     // 下段冰瀑
                new Vector3(184.375f, 0f, 70.9375f),      // 左侧石像
                new Vector3(219.53125f, 0f, 84.21875f),   // 右侧石像
                new Vector3(203.125f, 0f, 58.4375f),      // 雪池水面
                new Vector3(31.25f, 0f, 43.59375f),       // 左下未连通秋区土路
            };
            foreach (var point in blocked)
                Assert.That(navigation.IsWalkable(point), Is.False, "Expected blocked: " + point);

            foreach (var appearance in new[] { spring, midAutumn })
            {
                var layout = appearance.LoadTexture();
                Assert.That(layout.width, Is.EqualTo(4096));
                Assert.That(layout.height, Is.EqualTo(4608));
                Assert.That(layout.mipmapCount, Is.EqualTo(1));
                Assert.That(layout.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
                foreach (var tileId in new[] { "r01_c01", "r01_c08", "r05_c04", "r09_c08" })
                {
                    var tile = Resources.Load<Texture2D>(appearance.ResourceFolder + "tiles/" + tileId);
                    Assert.That(tile, Is.Not.Null, tileId);
                    Assert.That(tile.width, Is.EqualTo(1024));
                    Assert.That(tile.height, Is.EqualTo(1024));
                    Assert.That(tile.mipmapCount, Is.EqualTo(1));
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
                foreach (uint scene in new[] { 1u, 2u, 3u, 4u, 5u, 6u, 18000u, 20000u, 1u })
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
                Invoke(runtime, "EnterScene", 21003u);
                Assert.That(runtime.Map, Is.Null);
                Assert.That(CurrentMapCatalog.FindForOnline(21003u), Is.Null);
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
