using System.Collections;
using System.Linq;
using MmorpgClient.Game.Team;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Team;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MmorpgClient.Tests.PlayMode
{
    /// <summary>Input blockers depend on the real MonoBehaviour enable/disable/destroy lifecycle.</summary>
    public sealed class TeamWindowPlayModeTests
    {
        private GameObject _root;
        private TeamWindow _window;
        private TeamUiState _state;
        private TeamSnapshot _snapshot;
        private GameObject _focusEventsObject;
        private EventSystem _previousEventSystem;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("TeamInputPlayModeTest", typeof(RectTransform));
            var design = QdaoUguiFactory.CreateCenteredRect("Design", _root.transform, 2560, 1080);
            _window = new TeamWindow(design);
            _state = new TeamUiState();
            _snapshot = new TeamSnapshot { TeamId = 1001, LeaderId = 11, LocalPlayerId = 11, Capacity = 6 };
            _snapshot.Members.Add(new TeamRole
            {
                PlayerId = 11, Name = "队长清风", Level = 72, ClassId = 1, Gender = 1,
                CharacterId = "24_lu_dongbin", SchoolName = "剑修", IsLeader = true,
            });
            for (int index = 12; index <= 16; index++)
                _snapshot.Members.Add(new TeamRole
                {
                    PlayerId = (ulong)index, Name = "同行道友" + index, Level = 68, ClassId = 1, Gender = 1,
                    CharacterId = "24_lu_dongbin", SchoolName = "剑修",
                });
            for (int index = 21; index <= 29; index++)
                _snapshot.Applications.Add(new TeamRole
                {
                    PlayerId = (ulong)index, Name = "申请道友" + index, Level = 65, ClassId = 1, Gender = 1,
                    CharacterId = "24_lu_dongbin", SchoolName = "剑修",
                });
            Apply();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null)
            {
                _window?.Hide();
                Object.Destroy(_root);
            }
            CleanupEventSystem();
            _window = null;
            _root = null;
            _state = null;
            _snapshot = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator OpeningPagingBothListsAndClosing_ReleasesOnlyThisWindowsInputBlocker()
        {
            bool keyboardBefore = GameplayInputGate.IsKeyboardBlocked;
            bool pointerBefore = GameplayInputGate.IsPointerBlocked;
            _window.Show();
            yield return null;
            Assert.That(_window.IsVisible, Is.True);
            Assert.That(GameplayInputGate.IsKeyboardBlocked, Is.True);
            Assert.That(GameplayInputGate.IsPointerBlocked, Is.True);

            Click("MemberNextPage");
            yield return null;
            Click("ApplicationNextPage");
            yield return null;
            Assert.That(_window.MemberPageIndex, Is.EqualTo(1));
            Assert.That(_window.ApplicationPageIndex, Is.EqualTo(1));
            Assert.That(_root.GetComponentsInChildren<UnityEngine.Transform>()
                .Any(child => child.name == "TeamMembers"), Is.True);
            Assert.That(_root.GetComponentsInChildren<UnityEngine.Transform>()
                .Any(child => child.name == "TeamApplications"), Is.True);
            Assert.That(_root.GetComponentsInChildren<Button>()
                .Any(button => button.name.StartsWith("TeamTab_")), Is.False);
            _window.Show(TeamPage.Applications);
            yield return null;
            Assert.That(GameplayInputGate.IsKeyboardBlocked, Is.True);
            Assert.That(GameplayInputGate.IsPointerBlocked, Is.True);
            Click("CloseTeamWindow");
            yield return null;
            Assert.That(_window.IsVisible, Is.False);
            Assert.That(GameplayInputGate.IsKeyboardBlocked, Is.EqualTo(keyboardBefore));
            Assert.That(GameplayInputGate.IsPointerBlocked, Is.EqualTo(pointerBefore));

            _window.Hide();
            yield return null;
            Assert.That(GameplayInputGate.IsKeyboardBlocked, Is.EqualTo(keyboardBefore));
            Assert.That(GameplayInputGate.IsPointerBlocked, Is.EqualTo(pointerBefore));
        }

        [UnityTest]
        public IEnumerator SessionReset_HidesWindowAndReleasesInputBlocker()
        {
            bool keyboardBefore = GameplayInputGate.IsKeyboardBlocked;
            bool pointerBefore = GameplayInputGate.IsPointerBlocked;
            _window.Show(TeamPage.Applications);
            yield return null;
            Assert.That(GameplayInputGate.IsKeyboardBlocked, Is.True);
            Assert.That(GameplayInputGate.IsPointerBlocked, Is.True);

            _window.ResetSession();
            yield return null;
            Assert.That(_window.IsVisible, Is.False);
            Assert.That(_window.Page, Is.EqualTo(TeamPage.Members));
            Assert.That(_window.PageIndex, Is.Zero);
            Assert.That(_window.MemberPageIndex, Is.Zero);
            Assert.That(_window.ApplicationPageIndex, Is.Zero);
            Assert.That(GameplayInputGate.IsKeyboardBlocked, Is.EqualTo(keyboardBefore));
            Assert.That(GameplayInputGate.IsPointerBlocked, Is.EqualTo(pointerBefore));
        }

        [UnityTest]
        public IEnumerator DestroyingVisibleWindow_ReleasesInputBlocker()
        {
            bool keyboardBefore = GameplayInputGate.IsKeyboardBlocked;
            bool pointerBefore = GameplayInputGate.IsPointerBlocked;
            _window.Show();
            yield return null;
            Assert.That(GameplayInputGate.IsKeyboardBlocked, Is.True);
            Assert.That(GameplayInputGate.IsPointerBlocked, Is.True);

            Object.Destroy(_root);
            _window = null;
            yield return null;
            Assert.That(_root == null, Is.True);
            Assert.That(GameplayInputGate.IsKeyboardBlocked, Is.EqualTo(keyboardBefore));
            Assert.That(GameplayInputGate.IsPointerBlocked, Is.EqualTo(pointerBefore));
            _root = null;
        }

        [UnityTest]
        public IEnumerator ClosingWindow_RestoresTheButtonThatOpenedIt()
        {
            return WithEventSystem(events =>
            {
                var opener = new GameObject("OpenTeamWindow", typeof(RectTransform), typeof(Button));
                opener.transform.SetParent(_root.transform, false);
                events.SetSelectedGameObject(opener);

                _window.Show();

                Assert.That(events.currentSelectedGameObject, Is.SameAs(FindButton("CloseTeamWindow").gameObject));
                Click("CloseTeamWindow");
                Assert.That(_window.IsVisible, Is.False);
                Assert.That(events.currentSelectedGameObject, Is.SameAs(opener));
            });
        }

        [UnityTest]
        public IEnumerator ApplicationSnapshotRebuild_PreservesButtonFocusAndDeletionSelectsAUsableControl()
        {
            return WithEventSystem(events =>
            {
                _snapshot.Capacity = 7;
                Apply();
                _window.Show();
                var original = FindButton("Approve_21");
                Assert.That(original.IsInteractable(), Is.True);
                var originalEntityId = original.gameObject.GetEntityId();
                events.SetSelectedGameObject(original.gameObject);

                _snapshot.Applications[0].Level++;
                Apply();

                var replacement = FindButton("Approve_21");
                Assert.That(replacement.gameObject.GetEntityId(), Is.Not.EqualTo(originalEntityId),
                    "The snapshot should exercise focus restoration after the row is rebuilt.");
                Assert.That(events.currentSelectedGameObject, Is.SameAs(replacement.gameObject));
                AssertSelectedButtonIsUsable(events);

                _snapshot.Applications.RemoveAll(role => role.PlayerId == 21);
                Apply();

                AssertSelectedButtonIsUsable(events);
                Assert.That(events.currentSelectedGameObject.name, Is.Not.EqualTo("Approve_21"));
                Assert.That(_root.GetComponentsInChildren<Button>()
                    .Any(button => button.name == "Approve_21"), Is.False);
            });
        }

        [UnityTest]
        public IEnumerator PaginationShrinkingToOnePage_MovesFocusAwayFromTheHiddenPageButton()
        {
            return WithEventSystem(events =>
            {
                _window.Show(TeamPage.Applications);
                Click("ApplicationNextPage");
                var previous = FindButton("ApplicationPreviousPage");
                Assert.That(previous.gameObject.activeInHierarchy, Is.True);
                Assert.That(previous.interactable, Is.True);
                events.SetSelectedGameObject(previous.gameObject);

                _snapshot.Applications.RemoveRange(1, _snapshot.Applications.Count - 1);
                Apply();

                Assert.That(_window.ApplicationPageIndex, Is.Zero);
                Assert.That(previous.gameObject.activeInHierarchy, Is.False);
                Assert.That(previous.interactable, Is.False);
                Assert.That(events.currentSelectedGameObject, Is.Not.SameAs(previous.gameObject));
                AssertSelectedButtonIsUsable(events);
            });
        }

        private IEnumerator WithEventSystem(System.Action<EventSystem> verify)
        {
            _previousEventSystem = EventSystem.current;
            _focusEventsObject = new GameObject("TeamFocusTestEventSystem", typeof(EventSystem));
            var events = _focusEventsObject.GetComponent<EventSystem>();
            try
            {
                EventSystem.current = events;
                yield return null;
                Assert.That(EventSystem.current, Is.SameAs(events),
                    "Focus tests require an EventSystem registered by the real PlayMode lifecycle.");
                verify(events);
                yield return null;
            }
            finally
            {
                _window?.Hide();
                CleanupEventSystem();
            }
        }

        private void CleanupEventSystem()
        {
            if (_focusEventsObject != null)
            {
                _focusEventsObject.SetActive(false);
                Object.Destroy(_focusEventsObject);
            }
            _focusEventsObject = null;
            if (_previousEventSystem != null) EventSystem.current = _previousEventSystem;
            _previousEventSystem = null;
        }

        private void AssertSelectedButtonIsUsable(EventSystem events)
        {
            GameObject selected = events.currentSelectedGameObject;
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected.activeInHierarchy, Is.True);
            Assert.That(selected.transform.IsChildOf(_root.transform), Is.True);
            var button = selected.GetComponent<Button>();
            Assert.That(button, Is.Not.Null);
            Assert.That(button.IsInteractable(), Is.True);
        }

        private void Apply()
        {
            _state.SetSnapshot(_snapshot);
            _window.SetState(_state);
        }

        private Button FindButton(string name)
        {
            Button button = _root.GetComponentsInChildren<Button>().FirstOrDefault(candidate => candidate.name == name);
            Assert.That(button, Is.Not.Null, "未找到按钮：" + name);
            return button;
        }
        private void Click(string name)
        {
            FindButton(name).onClick.Invoke();
        }
    }
}
