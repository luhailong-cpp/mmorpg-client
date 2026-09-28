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
            int token = _state.BeginDecision(21, true);
            Assert.That(token, Is.Not.Zero);
            _window.SetState(_state);
            _window.Show();
            AssertDecisionsDisabledAndGuarded();
            AssertRefreshDisabledAndGuarded();
            Assert.That(FindButton("Approve_22").interactable, Is.False);
            Assert.That(FindButton("Reject_22").interactable, Is.False);
            Assert.That(ActiveText(), Does.Contain("处理中"));
            Assert.That(FindPortrait(21, Members), Is.Null);
            AssertRoleVisible(_snapshot.Applications[0], Applications);

            // Pushes no longer end an in-flight request; only its own reply does.
            _state.Complete(token, _snapshot, null);
            _window.SetState(_state);
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

        [Test]
        public void NoTeam_ShowsCreateApplyAndInvitesColumn()
        {
            Mirror(NoTeamSnapshot(), new List<TeamInvite>());
            _window.Show();
            int creates = 0;
            _window.CreateRequested += () => creates++;

            Assert.That(FindContainer("NoTeamCard").gameObject.activeInHierarchy, Is.True);
            Assert.That(ActiveText(), Does.Contain("收到的邀请"));
            Assert.That(ActiveText(), Does.Not.Contain("申请列表"));
            Assert.That(ActiveText(), Does.Contain("我的玩家编号  11"));
            foreach (string name in new[] { "CreateTeam", "ApplyJoinTeam", "RefreshTeam" })
                Assert.That(FindButton(name).gameObject.activeInHierarchy, Is.True, name);
            foreach (string name in new[] { "LeaveTeam", "DisbandTeam", "StartTeamMatch", "InviteToTeam" })
                Assert.That(FindButton(name).gameObject.activeInHierarchy, Is.False, name);
            Assert.That(FindButton("CreateTeam").interactable, Is.True);
            Assert.That(FindButton("ApplyJoinTeam").interactable, Is.True);
            Assert.That(ActivePortraits(Members), Is.Empty);

            Click("CreateTeam");
            Assert.That(creates, Is.EqualTo(1));
            Assert.That(_window.IsModalOpen, Is.False, "Creating a team needs no confirmation.");
        }

        [Test]
        public void NoTeam_InviteRowsEmitTeamIdAndChoice()
        {
            Mirror(NoTeamSnapshot(), Invites(300));
            _window.Show();
            var received = new List<(ulong teamId, bool accept)>();
            _window.InviteResponseRequested += (teamId, accept) => received.Add((teamId, accept));

            Assert.That(ActiveText(Applications), Does.Contain("邀请你入队"));
            Assert.That(ActiveText(Applications), Does.Contain("队伍编号 300"));
            Assert.That(FindPortrait(3000, Applications), Is.Not.Null);
            Click("AcceptInvite_300");
            Click("RejectInvite_300");
            Assert.That(received.Count, Is.EqualTo(2));
            Assert.That(received[0], Is.EqualTo((300ul, true)));
            Assert.That(received[1], Is.EqualTo((300ul, false)));
            Assert.That(_window.IsModalOpen, Is.False, "Answering an invite needs no confirmation.");

            Mirror(NoTeamSnapshot(), Invites(300, 301, 302, 303, 304));
            Assert.That(ActiveText(), Does.Contain("5 条"));
            Assert.That(FindButton("ApplicationNextPage").interactable, Is.True);
            Click("ApplicationNextPage");
            Assert.That(_window.ApplicationPageIndex, Is.EqualTo(1));
            Assert.That(ActiveButtons().Any(button => button.name == "AcceptInvite_304"), Is.True);
            Assert.That(ActiveButtons().Any(button => button.name == "AcceptInvite_300"), Is.False);
        }

        [Test]
        public void NoTeam_EmptyInvitesShowsHint()
        {
            Mirror(NoTeamSnapshot(), new List<TeamInvite>());
            _window.Show();

            Assert.That(FindContainer("EmptyInvites").gameObject.activeInHierarchy, Is.True);
            Assert.That(ActiveText(Applications), Does.Contain("暂无组队邀请"));
            Assert.That(ActiveText(), Does.Contain("0 条"));
            Assert.That(FindButton("ApplicationNextPage").gameObject.activeInHierarchy, Is.False);
        }

        [Test]
        public void Member_ShowsLeaveOnlyAndApplicationCount()
        {
            _snapshot.Members.Add(Role(12, "同行道友", 66, 2, 2));
            _snapshot.LocalPlayerId = 12;
            _snapshot.Applications.Clear();
            _snapshot.ApplicationCount = 2;
            Mirror(_snapshot, null);
            _window.Show();
            int leaves = 0;
            _window.LeaveRequested += () => leaves++;

            Assert.That(ActiveButtons().Any(button => button.name.StartsWith("Kick_")
                || button.name.StartsWith("Transfer_")), Is.False);
            foreach (string name in new[] { "DisbandTeam", "StartTeamMatch", "InviteToTeam", "CreateTeam", "ApplyJoinTeam" })
                Assert.That(FindButton(name).gameObject.activeInHierarchy, Is.False, name);
            Assert.That(ActiveText(Applications), Does.Contain("入队申请由队长处理"));
            Assert.That(ActiveText(Applications), Does.Contain("队伍当前有 2 条待处理申请。"));
            Assert.That(ActiveText(), Does.Contain("你是队员"));

            var leave = FindButton("LeaveTeam");
            Assert.That(leave.gameObject.activeInHierarchy, Is.True);
            Assert.That(leave.interactable, Is.True);
            Click("LeaveTeam");
            Assert.That(_window.IsModalOpen, Is.True);
            Assert.That(ActiveText(), Does.Contain("确定离开当前队伍吗？"));
            Assert.That(leaves, Is.Zero);
            Click("TeamModalConfirm");
            Assert.That(leaves, Is.EqualTo(1));
            Assert.That(_window.IsModalOpen, Is.False);
        }

        [Test]
        public void Leader_RowActionsOnlyOnOthers()
        {
            _snapshot.Members.Add(Role(12, "同行道友", 66, 2, 2));
            TeamRole offline = Role(13, "离线道友", 65, 3, 1);
            offline.IsOnline = false;
            _snapshot.Members.Add(offline);
            Mirror(_snapshot, null);
            _window.Show();
            var kicks = new List<ulong>();
            int transfers = 0;
            _window.KickRequested += kicks.Add;
            _window.TransferRequested += _ => transfers++;

            Assert.That(ActiveButtons().Any(button => button.name == "Kick_11" || button.name == "Transfer_11"), Is.False);
            Assert.That(FindButton("Transfer_12").interactable, Is.True);
            Assert.That(FindButton("Transfer_13").interactable, Is.False);
            Assert.That(FindButton("Kick_13").interactable, Is.True);
            Click("Transfer_13");
            Assert.That(_window.IsModalOpen, Is.False, "A disabled offline transfer must not open a dialog.");

            Click("Kick_12");
            Assert.That(_window.IsModalOpen, Is.True);
            Assert.That(ActiveText(), Does.Contain("确定将 同行道友 请离队伍吗？"));
            Assert.That(kicks, Is.Empty);
            Click("TeamModalConfirm");
            Assert.That(kicks, Is.EqualTo(new List<ulong> { 12 }));
            Assert.That(_window.IsModalOpen, Is.False);

            Click("Transfer_12");
            Assert.That(_window.IsModalOpen, Is.True);
            Assert.That(ActiveText(), Does.Contain("确定将队长转让给 同行道友 吗？"));
            Click("TeamModalCancel");
            Assert.That(transfers, Is.Zero);
            Assert.That(_window.IsModalOpen, Is.False);
            Assert.That(_root.GetComponentsInChildren<UnityEngine.Transform>(true).Any(child => child.name == "TeamModal"), Is.False);
        }

        [Test]
        public void MatchStarting_DisablesRosterChangesButKeepsRejectAndInvite()
        {
            _snapshot.Members.Add(Role(12, "同行道友", 66, 2, 2));
            _snapshot.MatchStarting = true;
            Mirror(_snapshot, null);
            _window.Show();
            int events = 0;
            _window.DecisionRequested += (_, approve) => { if (approve) events++; };
            _window.KickRequested += _ => events++;
            _window.TransferRequested += _ => events++;
            _window.LeaveRequested += () => events++;
            _window.DisbandRequested += () => events++;
            _window.StartMatchRequested += () => events++;
            string[] locked = { "Approve_21", "Kick_12", "Transfer_12", "LeaveTeam", "DisbandTeam", "StartTeamMatch" };

            foreach (string name in locked)
                Assert.That(FindButton(name).interactable, Is.False, name);
            Assert.That(ButtonText("StartTeamMatch"), Is.EqualTo("集合中…"));
            Assert.That(FindButton("Reject_21").interactable, Is.True);
            Assert.That(FindButton("InviteToTeam").interactable, Is.True);
            Assert.That(ActiveText(), Does.Contain("队伍正在集合进入战斗，名单暂时锁定。"));

            foreach (string name in locked)
            {
                Click(name);
                Assert.That(_window.IsModalOpen, Is.False, name);
            }
            Assert.That(events, Is.Zero, "Callbacks must guard against direct invocation of disabled buttons.");
        }

        [Test]
        public void InvitePrompt_ValidatesDigitsAndSelf()
        {
            Mirror(_snapshot, null);
            _window.Show();
            var invites = new List<ulong>();
            _window.InviteRequested += invites.Add;

            Click("InviteToTeam");
            Assert.That(_window.IsModalOpen, Is.True);
            TMP_InputField input = FindInput("TeamTargetInput");
            Assert.That(input.contentType, Is.EqualTo(TMP_InputField.ContentType.IntegerNumber));

            input.text = "";
            Click("TeamModalConfirm");
            Assert.That(FindLabel("TeamModalError").text, Is.Not.Empty);
            Assert.That(invites, Is.Empty);
            Assert.That(_window.IsModalOpen, Is.True);

            input.text = "11";
            Click("TeamModalConfirm");
            Assert.That(FindLabel("TeamModalError").text, Is.EqualTo("不能填写自己的编号。"));
            Assert.That(invites, Is.Empty);
            Assert.That(_window.IsModalOpen, Is.True);

            input.text = "12345";
            Click("TeamModalConfirm");
            Assert.That(invites, Is.EqualTo(new List<ulong> { 12345 }));
            Assert.That(_window.IsModalOpen, Is.False);
        }

        [Test]
        public void ApplyPrompt_EmitsTargetPlayerId()
        {
            Mirror(NoTeamSnapshot(), new List<TeamInvite>());
            _window.Show();
            var applications = new List<ulong>();
            _window.ApplyRequested += applications.Add;

            Click("ApplyJoinTeam");
            Assert.That(_window.IsModalOpen, Is.True);
            TMP_InputField input = FindInput("TeamTargetInput");
            input.text = "";
            Click("TeamModalConfirm");
            Assert.That(FindLabel("TeamModalError").text, Is.Not.Empty);
            Assert.That(applications, Is.Empty);

            input.text = "11";
            Click("TeamModalConfirm");
            Assert.That(applications, Is.Empty);
            Assert.That(_window.IsModalOpen, Is.True);

            input.text = "12345";
            Click("TeamModalConfirm");
            Assert.That(applications, Is.EqualTo(new List<ulong> { 12345 }));
            Assert.That(_window.IsModalOpen, Is.False);
        }

        [Test]
        public void Busy_DisablesEveryFooterAction()
        {
            _snapshot.Members.Add(Role(12, "同行道友", 66, 2, 2));
            Mirror(_snapshot, null, TeamAction.Kick, 12);
            _window.Show();

            foreach (string name in new[] { "StartTeamMatch", "InviteToTeam", "DisbandTeam", "LeaveTeam",
                         "CreateTeam", "ApplyJoinTeam", "RefreshTeam" })
                Assert.That(FindButton(name).interactable, Is.False, name);
            Assert.That(ButtonText("Kick_12"), Is.EqualTo("处理中"));
            Assert.That(ButtonText("Transfer_12"), Is.EqualTo("转让"));
            Assert.That(FindButton("Kick_12").interactable, Is.False);
        }

        [Test]
        public void HighlightedMember_RowIsTinted()
        {
            _snapshot.Members.Add(Role(12, "同行道友", 66, 2, 2));
            Mirror(_snapshot, null, highlight: 12, status: "有队员不在线。");
            _window.Show();

            Assert.That(RowPaper("TeamMemberSlot_1").color, Is.Not.EqualTo(Color.white));
            Assert.That(RowPaper("TeamMemberSlot_0").color, Is.EqualTo(Color.white));
            Assert.That(ActiveText(), Does.Contain("有队员不在线。"));
        }

        [Test]
        public void Back_ClosesModalBeforeWindow()
        {
            _window.Show();
            Click("LeaveTeam");
            Assert.That(_window.IsModalOpen, Is.True);
            Assert.That(ActiveText(), Does.Contain("你是队长，离队后队长将自动转给在线队员。确定离开吗？"));

            _window.Back();
            Assert.That(_window.IsModalOpen, Is.False);
            Assert.That(_window.IsVisible, Is.True);

            _window.Back();
            Assert.That(_window.IsVisible, Is.False);
        }

        [Test]
        public void ResetSession_ClosesModal()
        {
            _window.Show();
            Click("InviteToTeam");
            Assert.That(_window.IsModalOpen, Is.True);

            _window.ResetSession();

            Assert.That(_window.IsModalOpen, Is.False);
            Assert.That(_window.IsVisible, Is.False);
            Assert.That(_root.GetComponentsInChildren<UnityEngine.Transform>(true).Any(child => child.name == "TeamModal"), Is.False);
        }

        [Test]
        public void EmptyName_ShowsPlayerIdFallback()
        {
            _snapshot.Members.Add(Role(12, "", 66, 2, 2));
            Mirror(_snapshot, null);
            _window.Show();

            Assert.That(ActiveText(Members), Does.Contain("道友 12"));
            Assert.That(ActiveText(Members), Does.Not.Contain("无名道友"));
        }

        private void Apply()
        {
            _state.SetSnapshot(_snapshot);
            _window.SetState(_state);
        }

        // Mirrors a client state wholesale; unlike SetSnapshot it never drops a different team.
        private void Mirror(TeamSnapshot snapshot, List<TeamInvite> invites, TeamAction pending = TeamAction.None,
            ulong target = 0, ulong highlight = 0, string status = "")
        {
            _state.Sync(snapshot, invites, true, true, pending, target, highlight, status);
            _window.SetState(_state);
        }

        private static TeamSnapshot NoTeamSnapshot() => new TeamSnapshot { LocalPlayerId = 11, MembershipEpoch = 1 };

        private static List<TeamInvite> Invites(params ulong[] teamIds) => teamIds.Select(teamId => new TeamInvite
        {
            TeamId = teamId, LeaderId = teamId * 10, MemberCount = 2, ExpiresAt = 60f,
            Inviter = Role(teamId * 10, "邀请道友" + teamId, 60, (uint)(teamId % 4 + 1), (uint)(teamId % 2 + 1)),
        }).ToList();

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

        private string ButtonText(string name) => FindButton(name).GetComponentInChildren<TMP_Text>(true).text;

        private TMP_InputField FindInput(string name)
        {
            TMP_InputField input = _root.GetComponentsInChildren<TMP_InputField>().FirstOrDefault(candidate => candidate.name == name);
            Assert.That(input, Is.Not.Null, "未找到输入框：" + name);
            return input;
        }

        private TMP_Text FindLabel(string name)
        {
            TMP_Text label = _root.GetComponentsInChildren<TMP_Text>().FirstOrDefault(candidate => candidate.name == name);
            Assert.That(label, Is.Not.Null, "未找到文字：" + name);
            return label;
        }

        private Image RowPaper(string rowName)
        {
            Image paper = FindContainer(rowName).GetComponentsInChildren<Image>()
                .FirstOrDefault(image => image.name == "member_row");
            Assert.That(paper, Is.Not.Null, "未找到行底图：" + rowName);
            return paper;
        }
    }
}