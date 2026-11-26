using System.Linq;
using MmorpgClient.Game.WorldTravel;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.World.CurrentMaps;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class CityTravelWindowTests
    {
        private GameObject _root;
        private CityTravelWindow _window;

        [SetUp]
        public void SetUp()
        {
            CurrentMapCatalog.ReloadDeliveries();
            _root = new GameObject("TravelWindowTest", typeof(RectTransform));
            var design = QdaoUguiFactory.CreateCenteredRect("Design", _root.transform, 2560, 1080);
            _window = new CityTravelWindow(design);
            _window.SetDestinations(CityTravelUiRoot.CreateDestinations());
        }

        [TearDown]
        public void TearDown()
        {
            _window?.Hide();
            if (_root != null) Object.DestroyImmediate(_root);
            CurrentMapCatalog.ReloadDeliveries();
        }

        [Test]
        public void CurrentDualFestivalCity_SwitchesBetweenSpringAndMidAutumnAppearances()
        {
            _window.Show(2, "spring");
            Click("OpenCityTravelOptions");
            Assert.That(_window.SelectedSceneConfigId, Is.EqualTo(2));
            Assert.That(_window.SelectedAppearance, Is.EqualTo("spring"));
            Assert.That(_window.TravelEnabled, Is.False);
            Assert.That(FindButton("DayAppearance").gameObject.activeSelf, Is.False);
            ClickAppearance("mid-autumn");
            Assert.That(_window.SelectedAppearance, Is.EqualTo("mid-autumn"));
            Assert.That(_window.TravelEnabled, Is.True);
            _window.SetState(2, "mid-autumn", false, "");
            Assert.That(_window.TravelEnabled, Is.False);
        }

        [Test]
        public void CurrentMapTravel_EmitsChosenServerDestinationWithSpringAppearance()
        {
            uint scene = 0;
            string appearance = null;
            _window.AppearanceTravelRequested += (id, value) => { scene = id; appearance = value; };
            _window.Show(1, "spring");
            Click("OpenCityTravelOptions");
            Click("CityDestination_3");
            Click("TravelToSelectedCity");
            Assert.That(scene, Is.EqualTo(3));
            Assert.That(appearance, Is.EqualTo("spring"));
        }

        [Test]
        public void PendingTravel_DisablesDestinationAndAppearanceControls()
        {
            _window.Show(1, "spring");
            Click("OpenCityTravelOptions");
            Click("CityDestination_3");
            _window.SetState(1, "spring", true, "正在传送");
            Assert.That(_window.TravelEnabled, Is.False);
            Assert.That(FindButton("CityDestination_2").interactable, Is.False);
            Assert.That(FindButton("FestivalAppearance").interactable, Is.False);
            Assert.That(_window.SelectedSceneConfigId, Is.EqualTo(3));
        }

        [Test]
        public void DynamicDestinations_OnlyListRegisteredModernIdentities()
        {
            var destinations = CityTravelUiRoot.CreateDestinations();
            Assert.That(destinations.Length, Is.EqualTo(CurrentMapCatalog.All.Count + (CurrentMapCatalog.Find(4u) == null ? 1 : 0)));
            Assert.That(destinations.Select(x => x.SceneConfigId).Distinct().Count(), Is.EqualTo(destinations.Length));
            Assert.That(destinations.Any(x => x.SceneConfigId == uint.MaxValue), Is.False);
            foreach (uint id in new uint[] { 1, 2, 3, 5 })
            {
                var destination = destinations.Single(x => x.SceneConfigId == id);
                Assert.That(destination.FestivalResourcePath, Does.StartWith(MapDeliveryContract.ResourceRoot));
                Assert.That(destination.HasFestival, Is.True);
                Assert.That(destination.OnlineAvailable, Is.True);
                Assert.That(destination.DefaultAppearance, Is.EqualTo("spring"));
            }
            var formal = destinations.Where(x => x.SceneConfigId == 21003 || x.SceneConfigId == 28007 || x.SceneConfigId == 35004).ToArray();
            Assert.That(formal.Length, Is.EqualTo(3));
            Assert.That(formal.All(x => x.Available && !x.OnlineAvailable && x.DefaultAppearance == "spring"), Is.True);
            Assert.That(formal.Count(x => x.TexturePath("spring") != null) + formal.Count(x => x.TexturePath("mid-autumn") != null), Is.EqualTo(5));
            Assert.That(formal.All(x => x.TexturePath("normal") == null && x.TexturePath("lantern") == null), Is.True);
        }

        [Test]
        public void OfflineOnlyReplacementDisablesOnlineTravelWithoutLegacyFallback()
        {
            var sha = new string('a', 64);
            CurrentMapCatalog.ApplyDeliveries(new MapDeliveryCatalog { schemaVersion = 2, maps = new[] {
                new MapDeliveryRegistration {
                    key = "island", name = "Offline island fixture", sceneId = 2, revision = "online-policy-test",
                    clientOnly = true, defaultAppearance = "spring", appearances = new[] { new MapAppearanceDelivery {
                        id = "spring", artStatus = "formal_art_delivery", layoutSha256 = sha,
                        resourceRoot = MapDeliveryContract.ResourceRoot + "island/online-policy-test/spring/" + sha + "/"
                    } }
                }
            } });
            var destinations = CityTravelUiRoot.CreateDestinations();
            var island = destinations.Single(x => x.SceneConfigId == 2);
            Assert.That(island.OnlineAvailable, Is.False);
            Assert.That(island.DefaultAppearance, Is.EqualTo("spring"));
            _window.SetDestinations(destinations);
            _window.Show(1, "spring");
            Click("OpenCityTravelOptions");
            Click("CityDestination_2");
            uint requested = 0;
            _window.AppearanceTravelRequested += (id, _) => requested = id;
            Click("TravelToSelectedCity");
            Assert.That(requested, Is.Zero);
        }

        [Test]
        public void UndeliveredFourthMap_IsUnavailableEvenForDirectButtonCallbacks()
        {
            if (CurrentMapCatalog.Find(4u) != null) Assert.Ignore("地图 4 已正式注册。");
            _window.Show(1, false);
            Assert.That(FindButton("CityDestination_4").interactable, Is.False);
            Click("CityDestination_4");
            Assert.That(_window.SelectedSceneConfigId, Is.EqualTo(1));
            uint requested = 0;
            _window.TravelRequested += (scene, _) => requested = scene;
            Click("TravelToSelectedCity");
            Assert.That(requested, Is.Zero);
            var destination = CityTravelUiRoot.CreateDestinations().Single(x => x.SceneConfigId == 4);
            Assert.That(destination.Available, Is.False);
            Assert.That(destination.Name, Does.Contain("未开放"));
        }

        [TestCase(21003u, "spring")]
        [TestCase(21003u, "mid-autumn")]
        [TestCase(28007u, "spring")]
        [TestCase(35004u, "spring")]
        [TestCase(35004u, "mid-autumn")]
        public void ClientOnlyAppearance_IsPreviewableWithoutAnyOnlineTravelCallback(uint mapId, string appearance)
        {
            int calls = 0;
            _window.TravelRequested += (_, __) => calls++;
            _window.ZoneTravelRequested += (_, __, ___) => calls++;
            _window.AppearanceTravelRequested += (_, __) => calls++;
            _window.AppearanceZoneTravelRequested += (_, __, ___) => calls++;
            _window.SetZones(new[] { new CityTravelZone { ZoneId = 2, Name = "其他区" } });
            _window.Show(1, "normal");
            Click("OpenCityTravelOptions");
            Click("CityDestination_" + mapId);
            ClickAppearance(appearance);
            Assert.That(_window.SelectedSceneConfigId, Is.EqualTo(mapId));
            Assert.That(_window.SelectedAppearance, Is.EqualTo(appearance));
            Assert.That(_window.SelectedFestival, Is.True);
            Assert.That(_window.TravelEnabled, Is.False);
            Assert.That(FindButton("DayAppearance").gameObject.activeSelf, Is.False);
            Assert.That(FindButton("TravelZoneCycle").gameObject.activeSelf, Is.False);
            // Invoke disabled/hidden callbacks directly as well as checking the visual gate.
            Click("TravelToSelectedCity");
            Click("TravelZoneCycle");
            Click("TravelToSelectedCity");
            Assert.That(calls, Is.Zero);
            var preview = _root.GetComponentsInChildren<RawImage>(true).Single(x => x.name == "SelectedMapPreview");
            Assert.That(preview.texture, Is.Not.Null);
            Assert.That(preview.color.a, Is.EqualTo(1f));
            Assert.That(preview.transform.parent.GetComponent<RectTransform>().rect.width, Is.GreaterThan(0));
            Assert.That(preview.transform.parent.GetComponent<RectTransform>().rect.height, Is.GreaterThan(0));
            Assert.That(preview.GetComponent<AspectRatioFitter>().aspectRatio,
                Is.EqualTo((float)preview.texture.width / preview.texture.height).Within(.00001f));
        }

        [Test]
        public void PendingTravel_RejectsDirectAppearanceCallbacksAndDestinationChanges()
        {
            _window.Show(1, "normal");
            Click("CityDestination_21003");
            ClickAppearance("mid-autumn");
            _window.SetState(1, "normal", true, "正在传送");
            ClickAppearance("spring");
            Click("CityDestination_35004");
            Assert.That(_window.SelectedSceneConfigId, Is.EqualTo(21003));
            Assert.That(_window.SelectedAppearance, Is.EqualTo("mid-autumn"));
            Assert.That(FindAppearanceButton("mid-autumn").interactable, Is.False);
            Assert.That(_window.TravelEnabled, Is.False);
        }

        [Test]
        public void UndeliveredHolidayButton_CannotSelectAbsentAppearance()
        {
            _window.Show(1, "normal");
            Click("CityDestination_28007");
            var autumn = FindAppearanceButton("mid-autumn");
            Assert.That(autumn.gameObject.activeSelf, Is.False);
            autumn.onClick.Invoke();
            Assert.That(_window.SelectedAppearance, Is.EqualTo("spring"));
            Assert.That(_window.TravelEnabled, Is.False);
        }

        [Test]
        public void DynamicDestinationStrip_ScrollsWhenCatalogOutgrowsViewport()
        {
            var destinations = Enumerable.Range(0, 12).Select(i => new CityTravelDestination
            {
                SceneConfigId = (uint)(100 + i), Name = "测试地图" + i,
                DayResourcePath = CurrentMapCatalog.Find(1u).TexturePath
            }).ToArray();
            _window.SetDestinations(destinations);
            var scroll = _root.GetComponentsInChildren<ScrollRect>(true).Single(x => x.name == "Destinations");
            Assert.That(scroll.horizontal, Is.True);
            Assert.That(scroll.vertical, Is.False);
            Assert.That(scroll.content.rect.width, Is.GreaterThan(scroll.viewport.rect.width));
            Assert.That(scroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            var last = FindButton("CityDestination_111").GetComponent<RectTransform>();
            Assert.That(last.anchoredPosition.x + last.rect.width, Is.LessThanOrEqualTo(scroll.content.rect.width));
        }

        [Test]
        public void OriginalMapEntrypoints_KeepTravelControlsReachableWithoutChangingTheirPurpose()
        {
            _window.Show(1, false);
            var options=FindButton("TravelToSelectedCity").transform.parent.gameObject;
            Assert.That(options.activeSelf, Is.False, "默认地图保持批准原稿构图");
            Click("OpenCityTravelOptions");
            Assert.That(options.activeSelf, Is.True);
            Click("CityDestination_3");
            Assert.That(_window.SelectedSceneConfigId, Is.EqualTo(3));
            Click("CloseCityTravelOptions");
            Assert.That(options.activeSelf, Is.False);
            int calls=0;_window.TravelRequested+=(id,festival)=>calls++;
            Click("WorldMapTrials");
            Assert.That(options.activeSelf, Is.True);
            Assert.That(calls, Is.Zero, "尚未开放的试炼入口不会假冒换景或发出错误传送");
        }

        [Test]
        public void TideBanner_UsesSnapshotAgeAndOnlyRaisesAvailableQuickTravel()
        {
            int quickTravelRequests = 0;
            _window.TideQuickTravelRequested += () => quickTravelRequests++;
            _window.Show(1, "normal");
            var banner = FindRect(_root.transform, "WorldMapTideBanner");
            var quickTravel = FindButton("WorldMapTideQuickTravel");
            Assert.That(banner.gameObject.activeInHierarchy, Is.False);

            _window.SetOverview(TideOverview(true), 10f);
            Assert.That(banner.gameObject.activeInHierarchy, Is.True);
            Assert.That(FindText("WorldMapTideHeadline"), Is.EqualTo("灵潮涌动 · 碧渊水宫"));
            Assert.That(FindText("WorldMapTideCountdown"), Is.EqualTo("剩余 34:50"));
            Assert.That(quickTravel.interactable, Is.True);
            quickTravel.onClick.Invoke();
            _window.RefreshTideBanner(11f);
            Assert.That(FindText("WorldMapTideCountdown"), Is.EqualTo("剩余 34:49"));

            _window.SetState(1, "normal", true, "正在传送");
            Assert.That(quickTravel.interactable, Is.False);
            quickTravel.onClick.Invoke();
            _window.SetState(1, "normal", false, "");
            _window.SetOverview(TideOverview(false));
            Assert.That(banner.gameObject.activeInHierarchy, Is.True);
            Assert.That(FindText("WorldMapTideHeadline"), Is.EqualTo("灵潮休歇"));
            Assert.That(FindText("WorldMapTideCountdown"), Is.EqualTo("下轮倒计时 18:20"));
            Assert.That(quickTravel.gameObject.activeInHierarchy, Is.False);
            quickTravel.onClick.Invoke();

            _window.SetOverview(TideOverview(null));
            Assert.That(banner.gameObject.activeInHierarchy, Is.False);
            Assert.That(quickTravelRequests, Is.EqualTo(1));
        }

        [Test]
        public void MapPins_OnlyCoverOnlineLandmarksAndOpenTheTravelDrawer()
        {
            _window.Show(1, "normal");
            _window.SetOverview(TideOverview(true));
            var onlinePins = _window.Root.GetComponentsInChildren<Button>(false)
                .Where(button => button.name.StartsWith("WorldMapPin_")).Select(button => button.name).ToArray();
            var expected = new[] { "WorldMapPin_1", "WorldMapPin_2", "WorldMapPin_5" };
            if (CurrentMapCatalog.FindForOnline(3)?.CanTravelOnline == true)
                expected = expected.Append("WorldMapPin_3").ToArray();
            Assert.That(onlinePins, Is.EquivalentTo(expected));

            Click("WorldMapPin_5");
            Assert.That(FindRect(_root.transform, "WorldTravelOptions").gameObject.activeInHierarchy, Is.True);
            Assert.That(_window.SelectedSceneConfigId, Is.EqualTo(5));

            var pin = FindButton("WorldMapPin_5").transform;
            var badges = new[] { "WorldMapPinHeat", "WorldMapPinTide", "WorldMapPinHotspot", "WorldMapPinMerchant" };
            foreach (string badge in badges)
                Assert.That(FindRect(pin, badge).gameObject.activeInHierarchy, Is.True, badge);
            _window.SetOverview(TideOverview(null));
            foreach (string badge in badges)
                Assert.That(FindRect(pin, badge).gameObject.activeInHierarchy, Is.False, badge);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void YuexiBayPin_FollowsCatalogOnlineAvailability(bool clientOnly)
        {
            string sha = new string('b', 64);
            CurrentMapCatalog.ApplyDeliveries(new MapDeliveryCatalog
            {
                schemaVersion = 2,
                maps = new[]
                {
                    new MapDeliveryRegistration
                    {
                        key = "yuexi-bay", name = "月汐湾", sceneId = 3, revision = "pin-policy-test",
                        clientOnly = clientOnly, defaultAppearance = "spring", appearances = new[]
                        {
                            new MapAppearanceDelivery
                            {
                                id = "spring", artStatus = "formal_art_delivery", layoutSha256 = sha,
                                resourceRoot = MapDeliveryContract.ResourceRoot + "yuexi-bay/pin-policy-test/spring/" + sha + "/"
                            }
                        }
                    }
                }
            });
            _window.SetDestinations(CityTravelUiRoot.CreateDestinations());
            _window.Show(1, "normal");
            var pin = _root.GetComponentsInChildren<Button>(false).SingleOrDefault(button => button.name == "WorldMapPin_3");
            if (clientOnly)
                Assert.That(pin, Is.Null);
            else
                Assert.That(pin?.interactable, Is.True);
        }

        [Test]
        public void DrawerTabs_ShowOnlyTheirOwnDestinationsAndGalleryCannotTravel()
        {
            _window.Show(1, "normal");
            Click("OpenCityTravelOptions");
            var all = CityTravelUiRoot.CreateDestinations();
            AssertVisibleDestinations(CityTravelUiRoot.FilterOnlineDestinations(all));

            Click("WorldMapGalleryTab");
            AssertVisibleDestinations(CityTravelUiRoot.FilterGalleryDestinations(all));
            Assert.That(_window.TravelEnabled, Is.False);
            Assert.That(FindButton("TravelZoneCycle").gameObject.activeInHierarchy, Is.False);

            Click("WorldMapTravelTab");
            AssertVisibleDestinations(CityTravelUiRoot.FilterOnlineDestinations(all));
            Assert.That(CityTravelUiRoot.FilterOnlineDestinations(all).Any(d => d.SceneConfigId == _window.SelectedSceneConfigId), Is.True);
        }

        private static WorldMapOverviewSnapshot TideOverview(bool? active)
        {
            ulong nowMs = active == false ? 6_100_000UL : 3_900_000UL;
            WorldTideSnapshotModel tide = active.HasValue
                ? new WorldTideSnapshotModel(active.Value, 3600UL, 5u, 50001UL, 1u, 3_600_000UL, 6_000_000UL, 7_200_000UL)
                : null;
            return new WorldMapOverviewSnapshot(1, nowMs, nowMs, new[]
            {
                new WorldMapOverviewItem(1, 12, 1, global::WorldMapHeatLevel.Crowded, 10001, 1, false, false),
                new WorldMapOverviewItem(2, 0, 1, global::WorldMapHeatLevel.Quiet, 20001, 1, false, false),
                new WorldMapOverviewItem(5, 3, 1, global::WorldMapHeatLevel.Active, 50001, 1, active == true, true),
            }, tide, 5u);
        }

        private void AssertVisibleDestinations(CityTravelDestination[] expected)
        {
            var actual = _root.GetComponentsInChildren<Button>(false)
                .Where(button => button.name.StartsWith("CityDestination_")).Select(button => button.name);
            Assert.That(actual, Is.EquivalentTo(expected.Select(destination => "CityDestination_" + destination.SceneConfigId)));
        }

        private static RectTransform FindRect(UnityEngine.Transform root, string name) =>
            root.GetComponentsInChildren<RectTransform>(true).Single(rect => rect.name == name);

        private string FindText(string name) =>
            _root.GetComponentsInChildren<TMP_Text>(true).Single(text => text.name == name).text;

        private void Click(string name) => FindButton(name).onClick.Invoke();

        private void ClickAppearance(string appearance) => FindAppearanceButton(appearance).onClick.Invoke();

        private Button FindAppearanceButton(string appearance)
        {
            if (appearance == "normal") return FindButton("DayAppearance");
            if (appearance == "spring") return FindButton("FestivalAppearance");
            string label = appearance == "mid-autumn" ? "中秋" : "元宵";
            return _root.GetComponentsInChildren<Button>(true)
                .Single(x => x.GetComponentInChildren<TMP_Text>(true)?.text == label);
        }

        private Button FindButton(string name)
        {
            foreach (var button in _root.GetComponentsInChildren<Button>(true))
                if (button.name == name) return button;
            Assert.Fail("未找到按钮：" + name);
            return null;
        }
    }
}
