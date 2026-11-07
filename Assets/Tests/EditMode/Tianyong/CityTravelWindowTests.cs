using System.Linq;
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
        }

        [Test]
        public void CurrentDayOnlyCity_CannotEnableLegacyFestivalAppearance()
        {
            _window.Show(2, false);
            Click("OpenCityTravelOptions");
            Assert.That(_window.SelectedSceneConfigId, Is.EqualTo(2));
            Assert.That(_window.TravelEnabled, Is.False);
            Click("FestivalAppearance");
            Assert.That(_window.SelectedFestival, Is.False);
            Assert.That(_window.TravelEnabled, Is.False);
            Assert.That(FindButton("FestivalAppearance").gameObject.activeSelf, Is.False);
            _window.SetState(2, true, false, "");
            Assert.That(_window.TravelEnabled, Is.False);
        }

        [Test]
        public void CurrentMapTravel_EmitsChosenServerDestinationWithDayAppearance()
        {
            uint scene = 0;
            bool festival = false;
            _window.TravelRequested += (id, value) => { scene = id; festival = value; };
            _window.Show(1, false);
            Click("OpenCityTravelOptions");
            Click("CityDestination_3");
            Click("FestivalAppearance");
            Click("TravelToSelectedCity");
            Assert.That(scene, Is.EqualTo(3));
            Assert.That(festival, Is.False);
        }

        [Test]
        public void PendingTravel_DisablesDestinationAndAppearanceControls()
        {
            _window.Show(1, false);
            Click("OpenCityTravelOptions");
            Click("CityDestination_3");
            _window.SetState(1, false, true, "正在传送");
            Assert.That(_window.TravelEnabled, Is.False);
            Assert.That(FindButton("CityDestination_2").interactable, Is.False);
            Assert.That(FindButton("FestivalAppearance").interactable, Is.False);
            Assert.That(_window.SelectedSceneConfigId, Is.EqualTo(3));
        }

        [Test]
        public void DynamicDestinations_OnlyListRegisteredIdentitiesAndRetainOldCities()
        {
            var destinations = CityTravelUiRoot.CreateDestinations();
            Assert.That(destinations.Length, Is.EqualTo(CurrentMapCatalog.All.Count));
            Assert.That(destinations.Select(x => x.SceneConfigId).Distinct().Count(), Is.EqualTo(destinations.Length));
            Assert.That(destinations.Any(x => x.SceneConfigId == uint.MaxValue), Is.False);
            foreach (uint id in new uint[] { 1, 2, 3 })
            {
                var destination = destinations.Single(x => x.SceneConfigId == id);
                Assert.That(destination.DayResourcePath, Does.StartWith("World/CurrentMaps20261009/"));
                Assert.That(destination.HasFestival, Is.False);
                Assert.That(destination.OnlineAvailable, Is.True);
                Assert.That(destination.DefaultAppearance, Is.EqualTo("normal"));
            }
            var formal = destinations.Where(x => x.SceneConfigId == 21003 || x.SceneConfigId == 28007 || x.SceneConfigId == 35004).ToArray();
            Assert.That(formal.Length, Is.EqualTo(3));
            Assert.That(formal.All(x => x.Available && !x.OnlineAvailable && x.DefaultAppearance == "spring"), Is.True);
            Assert.That(formal.Count(x => x.TexturePath("spring") != null) + formal.Count(x => x.TexturePath("mid-autumn") != null), Is.EqualTo(5));
            Assert.That(formal.All(x => x.TexturePath("normal") == null && x.TexturePath("lantern") == null), Is.True);
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
