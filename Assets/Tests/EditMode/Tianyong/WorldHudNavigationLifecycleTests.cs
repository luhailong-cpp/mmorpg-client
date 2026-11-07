using System.Reflection;
using MmorpgClient.UI.Ugui;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class WorldHudNavigationLifecycleTests
    {
        private GameObject _fixture;
        private WorldHudNavigation _navigation;

        [SetUp]
        public void SetUp()
        {
            Assert.That(WorldHudNavigation.Instance == null, Is.True);
            _fixture = new GameObject("NavigationLifecycle", typeof(RectTransform), typeof(Canvas));
            var button = QdaoUguiFactory.CreateHitButton("MissionEntry", _fixture.transform, 0, 0, 300, 100);
            var label = QdaoUguiFactory.CreateText("Label", button.transform, 0, 0, 300, 40, "任务", 24, Color.white);
            WorldHudNavigation.Register(WorldHudEntry.Missions, (RectTransform)button.transform, button, label);
            _navigation = WorldHudNavigation.Instance;
            _navigation.ShowCategory(WorldHudCategory.Adventure);
        }

        [TearDown]
        public void TearDown()
        {
            if (_navigation != null) Object.DestroyImmediate(_navigation.gameObject);
            if (_fixture != null) Object.DestroyImmediate(_fixture);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RefreshAfterNavigationTeardownHidesRemainingChrome(bool releaseLayout)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var chrome = (RectTransform)typeof(WorldHudNavigation).GetField("_chrome", flags).GetValue(_navigation);
            Assert.That(chrome.gameObject.activeSelf, Is.True);
            if (releaseLayout)
                typeof(WorldHudNavigation).GetField("_layout", flags).SetValue(_navigation, null);
            else
            {
                var tabs = (Button[])typeof(WorldHudNavigation).GetField("_tabs", flags).GetValue(_navigation);
                Object.DestroyImmediate(tabs[0].gameObject);
            }

            Assert.DoesNotThrow(_navigation.RefreshPresentation);
            Assert.DoesNotThrow(_navigation.RefreshPresentation);
            Assert.DoesNotThrow(Canvas.ForceUpdateCanvases);
            Assert.That(chrome.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void RemovedFeatureCanRegisterAgainWithWorkingCategory()
        {
            _fixture.SetActive(false);
            Assert.DoesNotThrow(_navigation.RefreshPresentation);
            Assert.That(_navigation.CurrentCategory, Is.Null);
            _fixture.SetActive(true);
            _navigation.RefreshPresentation();
            _navigation.ShowCategory(WorldHudCategory.Adventure);
            Assert.That(_navigation.CurrentCategory, Is.EqualTo(WorldHudCategory.Adventure));
        }
    }
}
