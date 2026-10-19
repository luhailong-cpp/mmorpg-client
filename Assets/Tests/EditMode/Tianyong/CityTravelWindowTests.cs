using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
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
            Click("CityDestination_3");
            _window.SetState(1, false, true, "正在传送");
            Assert.That(_window.TravelEnabled, Is.False);
            Assert.That(FindButton("CityDestination_2").interactable, Is.False);
            Assert.That(FindButton("FestivalAppearance").interactable, Is.False);
            Assert.That(_window.SelectedSceneConfigId, Is.EqualTo(3));
        }

        [Test]
        public void UndeliveredFourthMap_IsUnavailableEvenForDirectButtonCallbacks()
        {
            _window.Show(1, false);
            Assert.That(FindButton("CityDestination_4").interactable, Is.False);
            Click("CityDestination_4");
            Assert.That(_window.SelectedSceneConfigId, Is.EqualTo(1));
            uint requested = 0;
            _window.TravelRequested += (scene, _) => requested = scene;
            Click("TravelToSelectedCity");
            Assert.That(requested, Is.Zero);
            var destinations = CityTravelUiRoot.CreateDestinations();
            Assert.That(destinations[3].Available, Is.False);
            Assert.That(destinations[3].Name, Does.Contain("未开放"));
            for (int i = 0; i < 3; i++)
            {
                Assert.That(destinations[i].DayResourcePath, Does.StartWith("World/CurrentMaps20261009/"));
                Assert.That(destinations[i].HasFestival, Is.False);
            }
        }

        private void Click(string name) => FindButton(name).onClick.Invoke();

        private Button FindButton(string name)
        {
            foreach (var button in _root.GetComponentsInChildren<Button>(true))
                if (button.name == name) return button;
            Assert.Fail("未找到按钮：" + name);
            return null;
        }
    }
}
