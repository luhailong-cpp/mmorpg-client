using System.Collections.Generic;
using System.Linq;
using Chatpb;
using MmorpgClient.Game.Social;
using MmorpgClient.Game.Team;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Social;
using MmorpgClient.UI.Ugui.Team;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class TeamInvitationWindowTests
    {
        private GameObject _root;
        private TeamInvitationWindow _window;
        private TeamUiState _state;
        private TeamSnapshot _snapshot;
        private readonly TeamRole _friend = new TeamRole { PlayerId = 21, Name = "清铃", Level = 32, IsOnline = true };

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("TeamInvitationTest", typeof(RectTransform));
            var design = QdaoUguiFactory.CreateCenteredRect("Design", _root.transform, 2560, 1080);
            _window = new TeamInvitationWindow(design);
            _state = new TeamUiState(() => 0);
            _snapshot = new TeamSnapshot { TeamId = 100, LeaderId = 11, LocalPlayerId = 11, Version = 1, Capacity = 5 };
            _snapshot.Members.Add(new TeamRole { PlayerId = 11, Name = "清风" });
            Sync();
            _window.Show();
            _window.SetCandidates(TeamInvitationSource.Friends, new[] { _friend }, false, "");
        }

        [TearDown]
        public void TearDown()
        {
            _window?.Hide();
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void AllFourSourcesRouteRefreshAndPreserveTheSearchQuery()
        {
            var requests = new List<(TeamInvitationSource source, string query)>();
            _window.RefreshRequested += (source, query) => requests.Add((source, query));
            foreach (TeamInvitationSource source in System.Enum.GetValues(typeof(TeamInvitationSource)))
            {
                _window.Show(source, "清铃");
                Assert.That(requests.Last(), Is.EqualTo((source, "清铃")));
                Assert.That(Button("TeamInvitationTab_" + source), Is.Not.Null);
            }
            Input("TeamInvitationSearch").text = "21";
            Button("SearchTeamInvitations").onClick.Invoke();
            Assert.That(requests.Last().query, Is.EqualTo("21"));
        }

        [Test]
        public void InviteEmitsTheStablePlayerIdWithoutManufacturingPendingInvite()
        {
            var targets = new List<ulong>();
            _window.InviteRequested += targets.Add;
            Button("InviteCandidate_21").onClick.Invoke();
            Assert.That(targets, Is.EqualTo(new ulong[] { 21 }));
            Assert.That(_state.Snapshot.PendingInvites, Is.Empty);
            Assert.That(Button("InviteCandidate_21").GetComponentInChildren<TMP_Text>().text, Is.EqualTo("邀请"));
            _snapshot.PendingInvites.Add(_friend.Clone());
            Sync();
            Assert.That(Button("InviteCandidate_21").interactable, Is.False);
            Assert.That(Button("InviteCandidate_21").GetComponentInChildren<TMP_Text>().text, Is.EqualTo("已邀请"));
            Button("InviteCandidate_21").onClick.Invoke();
            Assert.That(targets.Count, Is.EqualTo(1));
        }

        [Test]
        public void BackgroundRefreshCooldownDisablesInvitationUntilTheClientCanSend()
        {
            float now = 1f;
            var transport = new TeamFakeTransport { PlayerId = 11 };
            var client = new TeamClient(transport, clock: () => now);
            var liveObject = new GameObject("LiveTeamInvitationCooldownTest");
            var live = liveObject.AddComponent<TeamUiRoot>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            if (live.InvitationWindow == null) typeof(TeamUiRoot).GetMethod("Awake", flags).Invoke(live, null);
            typeof(TeamUiRoot).GetField("_client", flags).SetValue(live, client);
            var sync = typeof(TeamUiRoot).GetMethod("SyncState", flags);
            void SyncLive() => sync.Invoke(live, null);
            Button InviteButton() => live.GetComponentsInChildren<Button>()
                .Last(button => button.name == "InviteCandidate_21" && button.gameObject.activeInHierarchy);
            var view = new Teampb.TeamView { TeamId = 100, LeaderId = 11, Version = 1, Capacity = 5 };
            view.Members.Add(new Teampb.TeamMemberView { PlayerId = 11, IsOnline = true, IsLeader = true });
            try
            {
                client.Refresh();
                transport.Reply(new Teampb.TeamResponse { Team = view });
                now = 31f;
                client.Refresh();
                transport.Reply(new Teampb.TeamResponse { Team = view });
                now += .1f;
                Assert.That(client.Busy, Is.False, "The background refresh already replied.");
                Assert.That(client.Invite(21), Is.False, "The shared 400ms send interval still rejects an invite.");
                int calls = transport.Calls.Count, intents = 0;
                SyncLive();
                live.InvitationWindow.Show();
                live.InvitationWindow.SetCandidates(TeamInvitationSource.Friends, new[] { _friend }, false, "");
                live.InvitationWindow.InviteRequested += _ => intents++;
                Assert.That(InviteButton().interactable, Is.False, "The UI must reflect the real send cooldown after Busy clears.");
                Assert.That(InviteButton().GetComponentInChildren<TMP_Text>().text, Is.EqualTo("请稍候"));
                InviteButton().onClick.Invoke();
                Assert.That(intents, Is.Zero);
                Assert.That(transport.Calls.Count, Is.EqualTo(calls));
                now = 31f + TeamClient.MinSendIntervalSeconds + .001f;
                SyncLive();
                Assert.That(InviteButton().interactable, Is.True, "The invitation recovers when the same production interval expires.");
                InviteButton().onClick.Invoke();
                Assert.That(intents, Is.EqualTo(1));
                Assert.That(client.Invite(21), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(liveObject);
                client.Dispose();
            }
        }

        [Test]
        public void OfflineFullNonleaderAndBusyPreventDirectButtonInvocation()
        {
            int requests = 0;
            _window.InviteRequested += _ => requests++;
            var offline = _friend.Clone(); offline.IsOnline = false;
            _window.SetCandidates(TeamInvitationSource.Friends, new[] { offline }, false, "");
            Button("InviteCandidate_21").onClick.Invoke();
            _window.SetCandidates(TeamInvitationSource.Friends, new[] { _friend }, false, "");
            _snapshot.Capacity = 1; Sync();
            Button("InviteCandidate_21").onClick.Invoke();
            _snapshot.Capacity = 5; _snapshot.LeaderId = 12; Sync();
            Button("InviteCandidate_21").onClick.Invoke();
            _snapshot.LeaderId = 11; Sync(TeamAction.Refresh);
            Button("InviteCandidate_21").onClick.Invoke();
            Assert.That(requests, Is.Zero);
        }

        [Test]
        public void UnknownChatPresenceIsLabelledAndServerMayDecideTheInvitation()
        {
            var person = _friend.Clone(); person.IsOnline = false; person.OnlineStatusKnown = false;
            _window.Show(TeamInvitationSource.Chat);
            _window.SetCandidates(TeamInvitationSource.Chat, new[] { person }, false, "");
            int requests = 0; _window.InviteRequested += _ => requests++;
            Assert.That(ActiveText(), Does.Contain("状态待确认"));
            Assert.That(Button("InviteCandidate_21").interactable, Is.True);
            Button("InviteCandidate_21").onClick.Invoke();
            Assert.That(requests, Is.EqualTo(1));
            person.OnlineStatusKnown = true;
            _window.SetCandidates(TeamInvitationSource.Chat, new[] { person }, false, "");
            Assert.That(Button("InviteCandidate_21").interactable, Is.False);
            Button("InviteCandidate_21").onClick.Invoke();
            Assert.That(requests, Is.EqualTo(1), "Known offline friends stay disabled even when reached through chat.");
        }

        [Test]
        public void NoTeamCanCreateButCannotInviteUntilAuthoritativeTeamArrives()
        {
            _snapshot.TeamId = 0; _snapshot.LeaderId = 0; _snapshot.Members.Clear(); Sync();
            int created = 0, invited = 0;
            _window.CreateRequested += () => created++;
            _window.InviteRequested += _ => invited++;
            Button("InviteCandidate_21").onClick.Invoke();
            Button("CreateTeamForInvitation").onClick.Invoke();
            Assert.That(created, Is.EqualTo(1));
            Assert.That(invited, Is.Zero);
            Assert.That(_state.HasTeam, Is.False);
        }

        [Test]
        public void EmptyOnlinePageStillAllowsLoadingTheNextServerPage()
        {
            _window.Show(TeamInvitationSource.Online);
            _window.SetCandidates(TeamInvitationSource.Online, new TeamRole[0], false, "", true);
            int loads = 0; _window.LoadMoreRequested += () => loads++;
            Assert.That(Button("TeamInvitationNext").interactable, Is.True);
            Button("TeamInvitationNext").onClick.Invoke();
            Assert.That(loads, Is.EqualTo(1));
        }

        [Test]
        public void ManualIdRejectsSelfAndInvalidNumbersThenUsesSameInvitationIntent()
        {
            var targets = new List<ulong>(); _window.InviteRequested += targets.Add;
            Input("TeamInvitationPlayerId").SetTextWithoutNotify("0"); Button("InviteTeamById").onClick.Invoke();
            Input("TeamInvitationPlayerId").SetTextWithoutNotify("11"); Button("InviteTeamById").onClick.Invoke();
            Input("TeamInvitationPlayerId").SetTextWithoutNotify("21"); Button("InviteTeamById").onClick.Invoke();
            Assert.That(targets, Is.EqualTo(new ulong[] { 21 }));
        }

        [Test]
        public void CoveredTeamPanelDoesNotParticipateInKeyboardNavigation()
        {
            var team = new TeamWindow(_root.transform);
            team.SetState(_state); team.Show();
            try
            {
                var invite = Button("InviteToTeam");
                Assert.That(invite.IsInteractable(), Is.True);
                team.SetCovered(true);
                Assert.That(invite.IsInteractable(), Is.False);
                team.SetCovered(false);
                Assert.That(invite.IsInteractable(), Is.True);
            }
            finally { team.Hide(); }
        }

        [Test]
        public void ChatAvatarProfileRoutesAnInvitationForTheMessageSender()
        {
            var state = new SocialState();
            state.Reset(11); state.RuntimeStatus(true, false, false, "");
            state.ApplyHistory(SocialChannel.World, new[] { new ChatMessage
                { SenderPlayerId = 21, Channel = ChatChannelType.World, Content = "一起游历吗？", SendTimeMs = 1 } });
            var social = new SocialWindow(_root.transform, state);
            ulong target = 0; social.TeamInviteRequested += id => target = id;
            try
            {
                social.Show(SocialPage.World);
                Button("SocialPortrait_21Button").onClick.Invoke();
                Button("SocialInviteToTeam").onClick.Invoke();
                Assert.That(target, Is.EqualTo(21ul));
            }
            finally { social.Dispose(); }
        }

        private void Sync(TeamAction pending = TeamAction.None)
        {
            _snapshot.Version++;
            _state.Sync(_snapshot, null, true, true, pending, 0, 0, "");
            _window.SetState(_state);
        }
        private Button Button(string name) => _root.GetComponentsInChildren<Button>(true).Last(b => b.name == name && b.gameObject.activeInHierarchy);
        private TMP_InputField Input(string name) => _root.GetComponentsInChildren<TMP_InputField>(true).Single(i => i.name == name);
        private string ActiveText() => string.Join("\n", _root.GetComponentsInChildren<TMP_Text>().Select(t => t.text));
    }
}
