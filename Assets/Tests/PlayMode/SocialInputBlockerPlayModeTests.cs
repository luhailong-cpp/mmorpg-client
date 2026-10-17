using System.Collections;
using System.Linq;
using MmorpgClient.Game.Social;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Social;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MmorpgClient.Tests.PlayMode
{
    /// <summary>Exercises actual enable/disable/destroy callbacks in PlayMode, independently from pixel capture.</summary>
    public sealed class SocialInputBlockerPlayModeTests
    {
        private GameObject _root, _eventsObject, _otherBlocker;
        private SocialWindow _window;
        private EventSystem _previousEvents, _events;
        private bool _keyboardBefore, _pointerBefore;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousEvents = EventSystem.current;
            _eventsObject = new GameObject("SocialInputTestEvents", typeof(EventSystem));
            _events = _eventsObject.GetComponent<EventSystem>();
            EventSystem.current = _events;
            yield return null;
            Assert.That(EventSystem.current, Is.SameAs(_events));
            _events.SetSelectedGameObject(null);
            _keyboardBefore = GameplayInputGate.IsKeyboardBlocked;
            _pointerBefore = GameplayInputGate.IsPointerBlocked;
            Assert.That(_keyboardBefore, Is.False, "Run in an isolated test scene without a pre-existing modal blocker.");
            Assert.That(_pointerBefore, Is.False, "Run in an isolated test scene without a pre-existing modal blocker.");
            _root = new GameObject("SocialInputTestRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            _root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", _root.transform, 2560, 1080);
            _window = new SocialWindow(design, new SocialState(preview: true));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _window?.Dispose();
            _window = null;
            if (_root != null) Object.Destroy(_root);
            if (_otherBlocker != null) Object.Destroy(_otherBlocker);
            if (_eventsObject != null)
            {
                _eventsObject.SetActive(false);
                Object.Destroy(_eventsObject);
            }
            EventSystem.current = _previousEvents;
            _root = null; _otherBlocker = null; _eventsObject = null; _events = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator ShowingEveryPageAndClosing_RestoresInputAndOpenerFocus()
        {
            var opener = new GameObject("SocialInputTestOpener", typeof(RectTransform), typeof(Button));
            opener.transform.SetParent(_root.transform, false);
            _events.SetSelectedGameObject(opener);
            foreach (SocialPage page in new[] { SocialPage.Groups, SocialPage.World, SocialPage.Rumor })
            {
                _window.Show(page, refreshHistory: false);
                yield return null;
                Assert.That(_window.IsVisible, Is.True);
                AssertBlocked();
            }
            FindButton("SocialClose").onClick.Invoke();
            yield return null;
            Assert.That(_window.IsVisible, Is.False);
            Assert.That(_events.currentSelectedGameObject, Is.SameAs(opener));
            AssertRestored();
            _window.Hide();
            yield return null;
            AssertRestored();
        }

        [UnityTest]
        public IEnumerator BackClosesOnlyNestedModal_AndSessionResetReleasesWindowBlocker()
        {
            _window.Show(SocialPage.Groups, refreshHistory: false);
            yield return null;
            FindButton("SocialGroupEdit").onClick.Invoke();
            yield return null;
            Assert.That(_window.HasModal, Is.True);
            AssertBlocked();
            _window.Back();
            yield return null;
            Assert.That(_window.HasModal, Is.False);
            Assert.That(_window.IsVisible, Is.True);
            AssertBlocked();
            _window.ResetSession();
            yield return null;
            Assert.That(_window.IsVisible, Is.False);
            AssertRestored();
        }

        [UnityTest]
        public IEnumerator DestroyingVisibleRoot_ReleasesItsInputBlocker()
        {
            _window.Show(SocialPage.World, refreshHistory: false);
            yield return null;
            AssertBlocked();
            // Destroy the view first to exercise the MonoBehaviour lifecycle itself, without Hide/Dispose.
            Object.Destroy(_root);
            _window = null;
            yield return null;
            _events.SetSelectedGameObject(null);
            Assert.That(_root == null, Is.True);
            _root = null;
            AssertRestored();
        }

        [UnityTest]
        public IEnumerator ClosingSocial_DoesNotReleaseAnotherWindowsInputBlocker()
        {
            _otherBlocker = new GameObject("IndependentModalInputBlocker", typeof(GameplayInputBlocker));
            yield return null;
            AssertBlocked();
            _window.Show(SocialPage.World, refreshHistory: false);
            yield return null;
            _window.Hide();
            yield return null;
            AssertBlocked();
            _otherBlocker.SetActive(false);
            yield return null;
            _events.SetSelectedGameObject(null);
            AssertRestored();
        }

        private Button FindButton(string name)
        {
            var button = _root.GetComponentsInChildren<Button>(false).FirstOrDefault(candidate => candidate.name == name);
            Assert.That(button, Is.Not.Null, name);
            Assert.That(button.IsInteractable(), Is.True, name);
            return button;
        }
        private static void AssertBlocked()
        {
            Assert.That(GameplayInputGate.IsKeyboardBlocked, Is.True);
            Assert.That(GameplayInputGate.IsPointerBlocked, Is.True);
        }
        private void AssertRestored()
        {
            Assert.That(GameplayInputGate.IsKeyboardBlocked, Is.EqualTo(_keyboardBefore));
            Assert.That(GameplayInputGate.IsPointerBlocked, Is.EqualTo(_pointerBefore));
        }
    }
}
