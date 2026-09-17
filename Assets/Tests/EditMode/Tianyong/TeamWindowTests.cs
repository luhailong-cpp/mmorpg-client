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
        private const string Members = "TeamMembers";
        private const string Applications = "TeamApplications";
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

        [TestCase(TeamPage.Members)]
        [TestCase(TeamPage.Applications)]
        public void OpeningEitherEntry_ShowsMembersAndApplicationsTogetherWithoutTabs(TeamPage page)
        {
            _window.Show(page);

            Assert.That(_window.Page, Is.EqualTo(page));
            AssertRoleVisible(_snapshot.Members[0], Members);
            AssertRoleVisible(_snapshot.Applications[0], Applications);
            Assert.That(FindContainer(Members).gameObject.activeInHierarchy, Is.True);
            Assert.That(FindContainer(Applications).gameObject.activeInHierarchy, Is.True);
            Assert.That(ActiveButtons().Any(button => button.name.StartsWith("TeamTab_")), Is.False);
            Assert.That(ActiveButtons().Any(button => button.name == "TeamNextPage"
                || button.name == "TeamPreviousPage"), Is.False);
            Assert.That(ActiveText(), Does.Not.Contain("已同意"));
        }

        [Test]
        public void LegacyApprovedEntry_MapsToMembersAndNeverDisplaysApprovalHistory()
        {
            TeamRole history = Role(31, "旧会话历史道友", 70, 2, 2);
            _snapshot.Approved.Add(history);
            Apply();

            _window.Show(TeamPage.Approved);

            Assert.That(_window.Page, Is.EqualTo(TeamPage.Members));
            AssertRoleVisible(_snapshot.Members[0], Members);
            AssertRoleVisible(_snapshot.Applications[0], Applications);
            Assert.That(FindPortrait(history.PlayerId, Members), Is.Null);
            Assert.That(FindPortrait(history.PlayerId, Applications), Is.Null);
            Assert.That(ActiveText(), Does.Not.Contain(history.Name));
            Assert.That(ActiveText(), Does.Not.Contain("已同意"));
            Assert.That(ActiveButtons().Any(button => button.name.StartsWith("TeamTab_")), Is.False);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Decision_EmitsApplicantAndChoiceWithoutChangingAuthoritativeLists(bool approve)
        {
            var received = new List<(ulong playerId, bool approve)>();
            _window.DecisionRequested += (id, choice) => received.Add((id, choice));
            _window.Show();
            Click((approve ? "Approve_" : "Reject_") + "21");

            Assert.That(received.Count, Is.EqualTo(1));
            Assert.That(received[0].playerId, Is.EqualTo(21ul));
            Assert.That(received[0].approve, Is.EqualTo(approve));
            Assert.That(_state.Snapshot.Members.Count, Is.EqualTo(1));
            Assert.That(_state.Snapshot.Applications.Any(role => role.PlayerId == 21), Is.True);
            Assert.That(_state.Snapshot.Approved.Count, Is.Zero);
            Assert.That(FindPortrait(21, Members), Is.Null);
            AssertRoleVisible(_snapshot.Applications[0], Applications);
        }

        [Test]
        public void AuthoritativeApprovalUpdate_MovesApplicantToMembersWithoutApprovalHistory()
        {
            TeamRole applicant = _snapshot.Applications[0];
            _window.Show();
            Click("Approve_21");
            Assert.That(FindPortrait(21, Members), Is.Null);
            AssertRoleVisible(applicant, Applications);

            _snapshot.Applications.Remove(applicant);
            _snapshot.Members.Add(applicant);
            _snapshot.Approved.Add(applicant);
            Apply();

            Assert.That(FindPortrait(21, Applications), Is.Null);
            AssertRoleVisible(applicant, Members);
            Assert.That(ActiveButtons().Any(button => button.name == "Approve_21"
                || button.name == "Reject_21"), Is.False);
            Assert.That(ActiveText(), Does.Not.Contain("已同意"));
            Assert.That(ActiveText(Applications), Does.Contain("暂无入队申请"));
        }

        [Test]
        public void BothLists_ShowEachRolesPortraitNameLevelAndSchool()
        {
            _snapshot.Members = new List<TeamRole>
            {
                Role(41, "逐月剑客", 71, 1, 1),
                Role(42, "雨巷琴师", 62, 2, 2),
                Role(43, "青山药客", 53, 3, 1),
                Role(44, "云中鼓手", 44, 4, 2),
            };
            _snapshot.Applications = new List<TeamRole>
            {
                Role(51, "灯火剑客", 70, 1, 2),
                Role(52, "长街琴师", 61, 2, 1),
                Role(53, "月下药客", 52, 3, 2),
                Role(54, "山海鼓手", 43, 4, 1),
            };
            Apply();
            _window.Show();

            foreach (TeamRole role in _snapshot.Members) AssertRoleVisible(role, Members);
            foreach (TeamRole role in _snapshot.Applications) AssertRoleVisible(role, Applications);
            Assert.That(ActivePortraits(Members).Count(), Is.EqualTo(4));
            Assert.That(ActivePortraits(Applications).Count(), Is.EqualTo(4));
        }

        [Test]
        public void FullTeam_ShowsFiveMembersAndPreventsApprovalButAllowsRejection()
        {
            FillTeam();
            Apply();
            _window.Show();
            foreach (TeamRole role in _snapshot.Members) AssertRoleVisible(role, Members);
            Assert.That(ActivePortraits(Members).Count(), Is.EqualTo(5));
            AssertRoleVisible(_snapshot.Applications[0], Applications);
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
        public void MemberAndApplicationPagination_AreIndependentAndShowFiveAndFourRoles()
        {
            FillTeam();
            TeamRole sixth = Role(16, "第六位同行道友", 66, 2, 2);
            _snapshot.Capacity = 6;
            _snapshot.Members.Add(sixth);
            _snapshot.Applications = ApplicationRoles(9);
            Apply();
            _window.Show();
            Assert.That(_window.MemberPageIndex, Is.Zero);
            Assert.That(_window.ApplicationPageIndex, Is.Zero);
            Assert.That(ActivePortraits(Members).Count(), Is.EqualTo(5));
            Assert.That(ActivePortraits(Applications).Count(), Is.EqualTo(4));
            Assert.That(FindPortrait(sixth.PlayerId, Members), Is.Null);
            Assert.That(FindButton("MemberPreviousPage").interactable, Is.False);
            Assert.That(FindButton("ApplicationPreviousPage").interactable, Is.False);

            Click("ApplicationNextPage");
            Assert.That(_window.ApplicationPageIndex, Is.EqualTo(1));
            Assert.That(_window.MemberPageIndex, Is.Zero);
            AssertRoleVisible(_snapshot.Applications[4], Applications);
            AssertRoleVisible(_snapshot.Members[0], Members);
            Assert.That(FindPortrait(_snapshot.Applications[0].PlayerId, Applications), Is.Null);

            Click("MemberNextPage");
            Assert.That(_window.MemberPageIndex, Is.EqualTo(1));
            Assert.That(_window.ApplicationPageIndex, Is.EqualTo(1));
            Assert.That(ActivePortraits(Members).Count(), Is.EqualTo(1));
            Assert.That(ActivePortraits(Applications).Count(), Is.EqualTo(4));
            AssertRoleVisible(sixth, Members);
            AssertRoleVisible(_snapshot.Applications[4], Applications);
            Assert.That(FindButton("MemberNextPage").interactable, Is.False);
            Assert.That(FindButton("MemberPreviousPage").interactable, Is.True);

            Click("ApplicationNextPage");
            Assert.That(_window.ApplicationPageIndex, Is.EqualTo(2));
            Assert.That(_window.MemberPageIndex, Is.EqualTo(1));
            Assert.That(ActivePortraits(Applications).Count(), Is.EqualTo(1));
            AssertRoleVisible(_snapshot.Applications[8], Applications);
            AssertRoleVisible(sixth, Members);
            Assert.That(FindButton("ApplicationNextPage").interactable, Is.False);

            Click("MemberPreviousPage");
            Assert.That(_window.MemberPageIndex, Is.Zero);
            Assert.That(_window.ApplicationPageIndex, Is.EqualTo(2));
            AssertRoleVisible(_snapshot.Members[0], Members);
            Click("ApplicationPreviousPage");
            Assert.That(_window.ApplicationPageIndex, Is.EqualTo(1));
            Assert.That(_window.MemberPageIndex, Is.Zero);
        }

        [Test]
        public void SnapshotShrink_ClampsBothPaginationIndices()
        {
            FillTeam();
            _snapshot.Capacity = 6;
            _snapshot.Members.Add(Role(16, "第六位同行道友", 66, 2, 2));
            _snapshot.Applications = ApplicationRoles(9);
            Apply();
            _window.Show();
            Click("MemberNextPage");
            Click("ApplicationNextPage");
            Click("ApplicationNextPage");

            _snapshot.Capacity = 5;
            _snapshot.Members.RemoveAt(5);
            _snapshot.Applications.RemoveRange(2, 7);
            Apply();

            Assert.That(_window.MemberPageIndex, Is.Zero);
            Assert.That(_window.ApplicationPageIndex, Is.Zero);
            Assert.That(ActivePortraits(Members).Count(), Is.EqualTo(5));
            Assert.That(ActivePortraits(Applications).Count(), Is.EqualTo(2));
            Assert.That(FindButton("MemberNextPage").interactable, Is.False);
            Assert.That(FindButton("ApplicationNextPage").interactable, Is.False);
        }

        [Test]
        public void PageIndex_ReportsThePaginationForTheRequestedEntry()
        {
            FillTeam();
            _snapshot.Capacity = 6;
            _snapshot.Members.Add(Role(16, "第六位同行道友", 66, 2, 2));
            _snapshot.Applications = ApplicationRoles(9);
            Apply();
            _window.Show();
            Click("MemberNextPage");
            Click("ApplicationNextPage");
            Click("ApplicationNextPage");

            _window.Show(TeamPage.Members);
            Assert.That(_window.PageIndex, Is.EqualTo(_window.MemberPageIndex));
            _window.Show(TeamPage.Applications);
            Assert.That(_window.PageIndex, Is.EqualTo(_window.ApplicationPageIndex));
        }

        [Test]
        public void NonLeader_CanReadBothListsButCannotApproveOrReject()
        {
            _snapshot.LocalPlayerId = 12;
            Apply();
            _window.Show();
            AssertRoleVisible(_snapshot.Members[0], Members);
            AssertRoleVisible(_snapshot.Applications[0], Applications);
            AssertDecisionsDisabledAndGuarded();
        }

        [Test]
        public void UnavailableService_DisablesRefreshAndDecisionsAndShowsReason()
        {
            const string reason = "组队服务暂不可用，请稍后重试。";
            _state.SetUnavailable(reason);
            _window.SetState(_state);
            _window.Show();
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
            _window.Show();
            AssertDecisionsDisabledAndGuarded();
            AssertRefreshDisabledAndGuarded();
            Assert.That(FindButton("Approve_22").interactable, Is.False);
            Assert.That(FindButton("Reject_22").interactable, Is.False);
            Assert.That(ActiveText(), Does.Contain("处理中"));
            Assert.That(FindPortrait(21, Members), Is.Null);
            AssertRoleVisible(_snapshot.Applications[0], Applications);

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

        [Test]
        public void EmptyApplications_PreserveMemberListAndDisableApplicationPagination()
        {
            _snapshot.Applications.Clear();
            Apply();
            _window.Show();
            Assert.That(ActivePortraits(Applications), Is.Empty);
            AssertRoleVisible(_snapshot.Members[0], Members);
            Assert.That(ActiveText(Applications), Does.Contain("暂无入队申请"));
            Assert.That(FindButton("ApplicationPreviousPage").interactable, Is.False);
            Assert.That(FindButton("ApplicationNextPage").interactable, Is.False);
            Assert.That(ActiveText(), Does.Not.Contain("已同意"));
        }

        [Test]
        public void LongServerSuppliedNameAndSchool_RemainLiteralPlainTextInBothLists()
        {
            var role = Role(61, "<size=200>风起长安十里灯火照山河的道友</size>", 75, 1, 1);
            role.SchoolName = "<color=red>剑修</color>";
            _snapshot.Members = new List<TeamRole> { role };
            _snapshot.Applications = new List<TeamRole> { role };
            Apply();
            _window.Show();

            foreach (string container in new[] { Members, Applications })
            {
                var labels = FindContainer(container).GetComponentsInChildren<TMP_Text>().Where(label =>
                    label.text.Contains(role.Name) || label.text.Contains(role.SchoolName)).ToArray();
                Assert.That(labels.Length, Is.GreaterThanOrEqualTo(2));
                foreach (TMP_Text label in labels) Assert.That(label.richText, Is.False, label.name);
                AssertRoleVisible(role, container);
            }
        }

        [Test]
        public void SessionReset_HidesWindowAndClearsBothListsAndPagination()
        {
            FillTeam();
            _snapshot.Capacity = 6;
            _snapshot.Members.Add(Role(16, "第六位同行道友", 66, 2, 2));
            _snapshot.Applications = ApplicationRoles(9);
            Apply();
            _window.Show(TeamPage.Applications);
            Click("MemberNextPage");
            Click("ApplicationNextPage");

            _window.ResetSession();

            Assert.That(_window.IsVisible, Is.False);
            Assert.That(_window.Page, Is.EqualTo(TeamPage.Members));
            Assert.That(_window.PageIndex, Is.Zero);
            Assert.That(_window.MemberPageIndex, Is.Zero);
            Assert.That(_window.ApplicationPageIndex, Is.Zero);
            _window.Show();
            Assert.That(ActivePortraits(Members), Is.Empty, "A new session must not expose old members.");
            Assert.That(ActivePortraits(Applications), Is.Empty, "A new session must not expose old applicants.");
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

        private static List<TeamRole> ApplicationRoles(int count) => Enumerable.Range(1, count)
            .Select(index => Role((ulong)(40 + index), "道友" + index,
                (uint)(50 + index), (uint)((index - 1) % 4 + 1), (uint)(index % 2 + 1))).ToList();

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

        private void AssertRoleVisible(TeamRole role, string container)
        {
            Image image = FindPortrait(role.PlayerId, container);
            Assert.That(image, Is.Not.Null, "Missing portrait for " + role.PlayerId + " in " + container);
            Assert.That(image.sprite, Is.Not.Null, "Missing portrait asset for " + role.CharacterId);
            Assert.That(image.preserveAspect, Is.True);
            string text = ActiveText(container);
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

        private UnityEngine.Transform FindContainer(string name)
        {
            var container = _root.GetComponentsInChildren<UnityEngine.Transform>()
                .FirstOrDefault(candidate => candidate.name == name);
            Assert.That(container, Is.Not.Null, "Missing visible container: " + name);
            return container;
        }

        private IEnumerable<Image> ActivePortraits(string container) => FindContainer(container)
            .GetComponentsInChildren<Image>().Where(image => image.name.StartsWith("TeamPortrait_"));

        private Image FindPortrait(ulong playerId, string container) => ActivePortraits(container)
            .FirstOrDefault(image => image.name == "TeamPortrait_" + playerId);

        private string ActiveText(string container = null) => string.Join("\n",
            (container == null ? _root.transform : FindContainer(container))
            .GetComponentsInChildren<TMP_Text>().Select(label => label.text));
        private IEnumerable<Button> ActiveButtons() => _root.GetComponentsInChildren<Button>();
        private void Click(string name) => FindButton(name).onClick.Invoke();

        private Button FindButton(string name)
        {
            Button button = _root.GetComponentsInChildren<Button>(true).FirstOrDefault(candidate => candidate.name == name);
            Assert.That(button, Is.Not.Null, "未找到按钮：" + name);
            return button;
        }
    }
}