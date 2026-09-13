using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.Team;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Team;
using MmorpgClient.World;

using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class TeamWindowTests
    {
        private GameObject _root;
        private TeamWindow _window;
        private TeamUiState _state;
        private TeamSnapshot _snapshot;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("TeamWindowTest", typeof(RectTransform));
            var design = QdaoUguiFactory.CreateCenteredRect("Design", _root.transform, 2560, 1080);
            _window = new TeamWindow(design);
            _state = new TeamUiState(() => 0f);
            _snapshot = Snapshot();
            Apply();
        }

        [TearDown]
        public void TearDown()
        {
            _window?.Hide();
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Decision_EmitsApplicantAndChoiceWithoutChangingAuthoritativeLists(bool approve)
        {
            var received = new List<(ulong playerId, bool approve)>();
            _window.DecisionRequested += (id, choice) => received.Add((id, choice));
            _window.Show(TeamPage.Applications);
            Click((approve ? "Approve_" : "Reject_") + "21");

            Assert.That(received.Count, Is.EqualTo(1));
            Assert.That(received[0].playerId, Is.EqualTo(21ul));
            Assert.That(received[0].approve, Is.EqualTo(approve));
            Assert.That(_state.Snapshot.Members.Count, Is.EqualTo(1));
            Assert.That(_state.Snapshot.Applications.Any(role => role.PlayerId == 21), Is.True);
            Assert.That(_state.Snapshot.Approved.Count, Is.Zero);
        }

        [Test]
        public void AuthoritativeApprovalUpdate_RemovesApplicationAndShowsRoleInBothApprovedAndMembers()
        {
            TeamRole applicant = _snapshot.Applications[0];
            _window.Show(TeamPage.Applications);
            Click("Approve_21");
            _snapshot.Applications.Remove(applicant);
            _snapshot.Members.Add(applicant);
            _snapshot.Approved.Add(applicant);
            Apply();

            Assert.That(FindPortrait(21), Is.Null);
            Click("TeamTab_Approved");
            Assert.That(_window.Page, Is.EqualTo(TeamPage.Approved));
            AssertRoleVisible(applicant);
            Assert.That(ActiveButtons().Any(button => button.name.StartsWith("Approve_")), Is.False);
            Assert.That(ActiveButtons().Any(button => button.name.StartsWith("Reject_")), Is.False);
            Click("TeamTab_Members");
            Assert.That(_window.Page, Is.EqualTo(TeamPage.Members));
            AssertRoleVisible(applicant);
        }

        [TestCase(TeamPage.Members)]
        [TestCase(TeamPage.Applications)]
        [TestCase(TeamPage.Approved)]
        public void EveryList_ShowsEachRolesPortraitNameLevelAndSchool(TeamPage page)
        {
            var roles = new List<TeamRole>
            {
                Role(41, "逐月剑客", 71, 1, 1),
                Role(42, "雨巷琴师", 62, 2, 2),
                Role(43, "青山药客", 53, 3, 1),
                Role(44, "云中鼓手", 44, 4, 2),
            };
            if (page == TeamPage.Members) _snapshot.Members = roles;
            else if (page == TeamPage.Applications) _snapshot.Applications = roles;
            else _snapshot.Approved = roles;
            Apply();
            _window.Show(page);

            foreach (TeamRole role in roles) AssertRoleVisible(role);
            Assert.That(ActivePortraits().Count(), Is.EqualTo(roles.Count));
        }

        [Test]
        public void FullTeam_ShowsAllFiveMembersAndPreventsApprovalButAllowsRejection()
        {
            FillTeam();
            Apply();
            _window.Show(TeamPage.Members);
            foreach (TeamRole role in _snapshot.Members) AssertRoleVisible(role);
            Assert.That(ActivePortraits().Count(), Is.EqualTo(5));
            _window.Show(TeamPage.Applications);
            int decisions = 0;
            _window.DecisionRequested += (_, _) => decisions++;

            Assert.That(FindButton("Approve_21").interactable, Is.False);
            Click("Approve_21");
            Assert.That(decisions, Is.Zero, "The callback must guard against stale or direct invocations.");
            Assert.That(FindButton("Reject_21").interactable, Is.True);
            Click("Reject_21");
            Assert.That(decisions, Is.EqualTo(1));
        }

        [Test]
        public void SixMemberCapacity_PagesMembersAndShowsSixthRoleOnSecondPage()
        {
            FillTeam();
            TeamRole sixth = Role(16, "第六位同行道友", 66, 2, 2);
            _snapshot.Capacity = 6;
            _snapshot.Members.Add(sixth);
            Apply();
            _window.Show(TeamPage.Members);
            Assert.That(_window.PageIndex, Is.Zero);
            Assert.That(ActivePortraits().Count(), Is.EqualTo(5));
            Assert.That(FindPortrait(sixth.PlayerId), Is.Null);
            Assert.That(FindButton("TeamNextPage").interactable, Is.True);

            Click("TeamNextPage");
            Assert.That(_window.PageIndex, Is.EqualTo(1));
            Assert.That(ActivePortraits().Count(), Is.EqualTo(1));
            AssertRoleVisible(sixth);
            Assert.That(FindButton("TeamNextPage").interactable, Is.False);
            Assert.That(FindButton("TeamPreviousPage").interactable, Is.True);
            Click("TeamPreviousPage");
            Assert.That(_window.PageIndex, Is.Zero);
            AssertRoleVisible(_snapshot.Members[0]);
        }

        [Test]
        public void NonLeader_CanReadApplicationsButCannotApproveOrReject()
        {
            _snapshot.LocalPlayerId = 12;
            Apply();
            _window.Show(TeamPage.Applications);
            AssertRoleVisible(_snapshot.Applications[0]);
            AssertDecisionsDisabledAndGuarded();
        }

        [Test]
        public void UnavailableService_DisablesRefreshAndDecisionsAndShowsReason()
        {
            const string reason = "组队服务暂不可用，请稍后重试。";
            _state.SetUnavailable(reason);
            _window.SetState(_state);
            _window.Show(TeamPage.Applications);
            AssertDecisionsDisabledAndGuarded();
            AssertRefreshDisabledAndGuarded();
            Assert.That(ActiveText(), Does.Contain(reason));
        }

        [Test]
        public void PendingDecision_DisablesAllDecisionsAndRefreshUntilFreshSnapshot()
        {
            _snapshot.Applications.Add(Role(22, "第二位道友", 60, 2, 2));
            Apply();
            Assert.That(_state.BeginDecision(21, true), Is.Not.Zero);
            _window.SetState(_state);
            _window.Show(TeamPage.Applications);
            AssertDecisionsDisabledAndGuarded();
            AssertRefreshDisabledAndGuarded();
            Assert.That(FindButton("Approve_22").interactable, Is.False);
            Assert.That(FindButton("Reject_22").interactable, Is.False);
            Assert.That(ActiveText(), Does.Contain("处理中"));

            Apply();
            Assert.That(FindButton("Approve_21").interactable, Is.True);
            Assert.That(FindButton("Reject_21").interactable, Is.True);
            Assert.That(FindButton("RefreshTeam").interactable, Is.True);
        }

        [Test]
        public void Refresh_EmitsIntentWhenServiceIsAvailable()
        {
            int refreshes = 0;
            _window.RefreshRequested += () => refreshes++;
            _window.Show();
            Click("RefreshTeam");
            Assert.That(refreshes, Is.EqualTo(1));
        }

        [TestCase(TeamPage.Applications)]
        [TestCase(TeamPage.Approved)]
        public void Lists_PageByFourAndClampPageWhenSnapshotShrinks(TeamPage page)
        {
            var roles = Enumerable.Range(1, 9).Select(index => Role((ulong)(40 + index), "道友" + index,
                (uint)(50 + index), (uint)((index - 1) % 4 + 1), (uint)(index % 2 + 1))).ToList();
            if (page == TeamPage.Applications) _snapshot.Applications = roles;
            else _snapshot.Approved = roles;
            Apply();
            _window.Show(page);
            Assert.That(_window.PageIndex, Is.Zero);
            Assert.That(FindButton("TeamPreviousPage").interactable, Is.False);
            Assert.That(ActivePortraits().Count(), Is.EqualTo(4));
            AssertRoleVisible(roles[0]);
            Assert.That(FindPortrait(roles[4].PlayerId), Is.Null);

            Click("TeamNextPage");
            Assert.That(_window.PageIndex, Is.EqualTo(1));
            AssertRoleVisible(roles[4]);
            Assert.That(FindPortrait(roles[0].PlayerId), Is.Null);
            Click("TeamNextPage");
            Assert.That(_window.PageIndex, Is.EqualTo(2));
            Assert.That(ActivePortraits().Count(), Is.EqualTo(1));
            AssertRoleVisible(roles[8]);
            Assert.That(FindButton("TeamNextPage").interactable, Is.False);
            Click("TeamPreviousPage");
            Assert.That(_window.PageIndex, Is.EqualTo(1));

            roles.RemoveRange(2, 7);
            Apply();
            Assert.That(_window.PageIndex, Is.Zero);
            Assert.That(ActivePortraits().Count(), Is.EqualTo(2));
            Assert.That(FindButton("TeamNextPage").interactable, Is.False);
        }

        [TestCase(TeamPage.Applications)]
        [TestCase(TeamPage.Approved)]
        public void EmptyList_ShowsEmptyMessageAndDisablesPagination(TeamPage page)
        {
            _snapshot.Applications.Clear();
            Apply();
            _window.Show(page);
            Assert.That(ActivePortraits(), Is.Empty);
            Assert.That(ActiveText(), Does.Contain(page == TeamPage.Applications ? "暂无入队申请" : "暂无已同意记录"));
            Assert.That(FindButton("TeamPreviousPage").interactable, Is.False);
            Assert.That(FindButton("TeamNextPage").interactable, Is.False);
        }

        [TestCase(TeamPage.Members)]
        [TestCase(TeamPage.Applications)]
        [TestCase(TeamPage.Approved)]
        public void LongServerSuppliedNameAndSchool_RemainLiteralPlainText(TeamPage page)
        {
            var role = Role(61, "<size=200>风起长安十里灯火照山河的道友</size>", 75, 1, 1);
            role.SchoolName = "<color=red>剑修</color>";
            _snapshot.Members = new List<TeamRole> { role };
            _snapshot.Applications = new List<TeamRole> { role };
            _snapshot.Approved = new List<TeamRole> { role };
            Apply();
            _window.Show(page);
            var labels = _root.GetComponentsInChildren<TMP_Text>().Where(label =>
                label.text.Contains(role.Name) || label.text.Contains(role.SchoolName)).ToArray();

            Assert.That(labels.Length, Is.GreaterThanOrEqualTo(2));
            foreach (TMP_Text label in labels) Assert.That(label.richText, Is.False, label.name);
            AssertRoleVisible(role);
        }

        [Test]
        public void SessionReset_HidesWindowAndClearsPriorSessionData()
        {
            _window.Show(TeamPage.Applications);
            _window.ResetSession();
            Assert.That(_window.IsVisible, Is.False);
            Assert.That(_window.Page, Is.EqualTo(TeamPage.Members));
            Assert.That(_window.PageIndex, Is.Zero);
            _window.Show(TeamPage.Applications);
            Assert.That(FindPortrait(21), Is.Null, "A new session must not expose the prior session's applicant.");
            Assert.That(FindButton("RefreshTeam").interactable, Is.False);
        }
        private void Apply()
        {
            _state.SetSnapshot(_snapshot);
            _window.SetState(_state);
        }

        private static TeamSnapshot Snapshot()
        {
            var snapshot = new TeamSnapshot { TeamId = 1001, LeaderId = 11, LocalPlayerId = 11, Capacity = 5 };
            snapshot.Members.Add(Role(11, "队长清风", 72, 1, 1));
            snapshot.Applications.Add(Role(21, "灯火道友", 68, 1, 2));
            return snapshot;
        }

        private static TeamRole Role(ulong id, string name, uint level, uint profession, uint gender)
        {
            string[] schools = { "未知", "剑修", "法修", "丹修", "体修" };
            return new TeamRole
            {
                PlayerId = id, Name = name, Level = level, ClassId = profession, Gender = gender,
                CharacterId = QdaoCharacterCatalog.ResolveRole(profession, gender), SchoolName = schools[(int)profession],
                IsLeader = id == 11, IsOnline = true,
            };
        }

        private void FillTeam()
        {
            for (int index = 2; index <= 5; index++)
                _snapshot.Members.Add(Role((ulong)(10 + index), "同行道友" + index,
                    (uint)(60 + index), (uint)((index - 1) % 4 + 1), (uint)(index % 2 + 1)));
        }

        private void AssertRoleVisible(TeamRole role)
        {
            Image image = FindPortrait(role.PlayerId);
            Assert.That(image, Is.Not.Null, "Missing portrait for " + role.PlayerId);
            Assert.That(image.sprite, Is.Not.Null, "Missing portrait asset for " + role.CharacterId);
            Assert.That(image.preserveAspect, Is.True);
            string text = ActiveText();
            Assert.That(text, Does.Contain(role.Name));
            Assert.That(text, Does.Contain(role.Level + " 级"));
            Assert.That(text, Does.Contain(role.SchoolName));
        }

        private void AssertDecisionsDisabledAndGuarded()
        {
            int decisions = 0;
            _window.DecisionRequested += (_, _) => decisions++;
            Assert.That(FindButton("Approve_21").interactable, Is.False);
            Assert.That(FindButton("Reject_21").interactable, Is.False);
            Click("Approve_21");
            Click("Reject_21");
            Assert.That(decisions, Is.Zero);
        }

        private void AssertRefreshDisabledAndGuarded()
        {
            int refreshes = 0;
            _window.RefreshRequested += () => refreshes++;
            Assert.That(FindButton("RefreshTeam").interactable, Is.False);
            Click("RefreshTeam");
            Assert.That(refreshes, Is.Zero);
        }

        private IEnumerable<Image> ActivePortraits() => _root.GetComponentsInChildren<Image>()
            .Where(image => image.name.StartsWith("TeamPortrait_"));

        private Image FindPortrait(ulong playerId) => ActivePortraits()
            .FirstOrDefault(image => image.name == "TeamPortrait_" + playerId);

        private string ActiveText() => string.Join("\n", _root.GetComponentsInChildren<TMP_Text>().Select(label => label.text));
        private IEnumerable<Button> ActiveButtons() => _root.GetComponentsInChildren<Button>();
        private void Click(string name) => FindButton(name).onClick.Invoke();

        private Button FindButton(string name)
        {
            Button button = ActiveButtons().FirstOrDefault(candidate => candidate.name == name);
            Assert.That(button, Is.Not.Null, "未找到按钮：" + name);
            return button;
        }
    }
}
