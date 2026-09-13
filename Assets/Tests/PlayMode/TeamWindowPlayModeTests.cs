using System.Collections;
using System.Linq;
using MmorpgClient.Game.Team;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Team;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MmorpgClient.Tests.PlayMode
{
    /// <summary>Input blockers depend on the real MonoBehaviour enable/disable/destroy lifecycle.</summary>
    public sealed class TeamWindowPlayModeTests
    {
        private GameObject _root;
        private TeamWindow _window;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("TeamInputPlayModeTest", typeof(RectTransform));
            var design = QdaoUguiFactory.CreateCenteredRect("Design", _root.transform, 2560, 1080);
            _window = new TeamWindow(design);
            var state = new TeamUiState();
            var snapshot = new TeamSnapshot { TeamId = 1001, LeaderId = 11, LocalPlayerId = 11, Capacity = 5 };
            snapshot.Members.Add(new TeamRole
            {
                PlayerId = 11, Name = "队长清风", Level = 72, ClassId = 1, Gender = 1,
                CharacterId = "24_lu_dongbin", SchoolName = "剑修", IsLeader = true,
            });
            state.SetSnapshot(snapshot);
            _window.SetState(state);
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
            _window = null;
            _root = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator OpeningSwitchingTabsAndClosing_ReleasesOnlyThisWindowsInputBlocker()
        {
            bool keyboardBefore = GameplayInputGate.IsKeyboardBlocked;
            bool pointerBefore = GameplayInputGate.IsPointerBlocked;
            _window.Show();
            yield return null;
            Assert.That(_window.IsVisible, Is.True);
            Assert.That(GameplayInputGate.IsKeyboardBlocked, Is.True);
            Assert.That(GameplayInputGate.IsPointerBlocked, Is.True);

            Click("TeamTab_Applications");
            yield return null;
            Click("TeamTab_Approved");
            yield return null;
            Assert.That(GameplayInputGate.IsKeyboardBlocked, Is.True);
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

        private void Click(string name)
        {
            Button button = _root.GetComponentsInChildren<Button>().FirstOrDefault(candidate => candidate.name == name);
            Assert.That(button, Is.Not.Null, "未找到按钮：" + name);
            button.onClick.Invoke();
        }
    }
}
