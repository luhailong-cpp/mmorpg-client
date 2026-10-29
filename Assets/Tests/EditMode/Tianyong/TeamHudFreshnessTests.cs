using System;
using MmorpgClient.Game.Team;
using NUnit.Framework;
using Teampb;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>HUD 保留已知名单时，新动作的权限不能继续使用读取失败前的旧视图。</summary>
    public sealed class TeamHudFreshnessTests
    {
        private float _now;
        private TeamFakeTransport _net;
        private TeamClient _client;
        private Action<TeamSnapshotS2C> _push;

        [SetUp]
        public void SetUp()
        {
            _now = 0;
            _net = new TeamFakeTransport();
            _client = new TeamClient(_net, () => _net.Identity, handler => _push = handler,
                handler => { if (_push == handler) _push = null; }, () => _now);
        }

        [TearDown]
        public void TearDown() => _client.Dispose();

        [Test]
        public void FirstReadWithoutViewDoesNotMakePlaceholderFresh()
        {
            Assert.That(_client.HasFreshView, Is.False);
            _client.DrainQueued(false);
            _net.Reply(ReadFailure());
            Assert.That(_client.HasLoaded, Is.True);
            Assert.That(_client.HasView, Is.False);
            Assert.That(_client.HasFreshView, Is.False);
        }

        [Test]
        public void FailedRefreshRetainsRosterButSameVersionSuccessRestoresFreshness()
        {
            Loaded();
            var previous = _client.Snapshot;
            Assert.That(_client.Refresh(), Is.True);
            _net.Reply(ReadFailure());
            Assert.That(_client.HasView, Is.True);
            Assert.That(_client.Snapshot, Is.SameAs(previous));
            Assert.That(_client.Snapshot.Members.Count, Is.EqualTo(2));
            Assert.That(_client.HasFreshView, Is.False);
            Assert.That(_client.RefreshQueued, Is.True);

            _now += TeamClient.ReadRetryDelaySeconds;
            _client.DrainQueued(false);
            _net.Reply(new TeamResponse { Team = View() });
            Assert.That(_client.HasFreshView, Is.True);
        }

        [Test]
        public void SameVersionOnlinePushRestoresFreshnessAndUpdatesMember()
        {
            Loaded();
            Assert.That(_client.Refresh(), Is.True);
            _net.Reply(ReadFailure());
            var offline = View();
            offline.Members[1].IsOnline = false;
            Push(offline);
            Assert.That(_client.HasFreshView, Is.True);
            Assert.That(_client.Snapshot.Members[1].IsOnline, Is.False);
        }

        [Test]
        public void MissingViewReadReplyDoesNotExpireNewerPushReceivedInFlight()
        {
            Loaded();
            Assert.That(_client.Refresh(), Is.True);
            Push(View(version: 2));
            _net.Reply(ReadFailure());
            Assert.That(_client.HasFreshView, Is.True);
            Assert.That(_client.Snapshot.Version, Is.EqualTo(2));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingViewWriteReplyExpiresOnlyTheSnapshotUsedToSend(bool newerPush)
        {
            Loaded();
            Assert.That(_client.Leave(), Is.True);
            if (newerPush) Push(View(version: 2));
            _net.Reply(ReadFailure());
            Assert.That(_client.HasTeam, Is.True, "无视图不代表离队已确认");
            Assert.That(_client.HasFreshView, Is.EqualTo(newerPush));
        }

        [Test]
        public void StalePushCannotRestoreExpiredPermissions()
        {
            Loaded(View(version: 2));
            Assert.That(_client.Refresh(), Is.True);
            _net.Reply(ReadFailure());
            Push(View(version: 1));
            Assert.That(_client.HasFreshView, Is.False);
            Assert.That(_client.Snapshot.Version, Is.EqualTo(2));
        }

        [Test]
        public void ConflictingTeamExpiresPermissionsUntilAuthoritativeViewResolvesIt()
        {
            Loaded();
            Push(View(teamId: 200));
            Assert.That(_client.HasFreshView, Is.False);
            Assert.That(_client.Snapshot.TeamId, Is.EqualTo(100));
            Assert.That(_client.RefreshQueued, Is.True);
            Push(View());
            Assert.That(_client.HasFreshView, Is.True);
        }

        [Test]
        public void LocalQueueCorrectionDoesNotCountAsNewAuthorityForMissingReadReply()
        {
            var queued = View();
            queued.MatchState = TeamBackfillProtocol.QueuedState;
            queued.MatchQueuedAtMs = 1234;
            queued.MatchTargetSize = 5;
            Loaded(queued);
            Assert.That(_client.Refresh(), Is.True);
            _client.ConsumeBackfillQueue(100, 1234);
            _net.Reply(ReadFailure());
            Assert.That(_client.HasFreshView, Is.False, "仅本地匹配态修正不能证明服务端权限仍然有效");
        }

        [Test]
        public void LeaveTimeoutPreservesKnownRosterAndRequiresAuthorityAfterReconnect()
        {
            Loaded();
            Assert.That(_client.Leave(), Is.True);
            _net.Fail("rpc timeout");
            Assert.That(_client.HasFreshView, Is.False);
            Assert.That(_client.HasTeam, Is.True);
            Assert.That(_client.RequiresReconnect, Is.True);
            _net.Identity = new object();
            _client.ObserveConnection();
            Assert.That(_client.HasFreshView, Is.False);
            Assert.That(_client.HasView, Is.False);
        }

        [Test]
        public void ResetWhileReadInFlightCannotRestoreFreshnessFromOldReply()
        {
            Loaded();
            Assert.That(_client.Refresh(), Is.True);
            var lateReply = _net.Pending;
            _client.Reset();
            lateReply(new TeamResponse { Team = View(version: 9) });
            Assert.That(_client.HasFreshView, Is.False);
            Assert.That(_client.HasView, Is.False);
        }

        [Test]
        public void StaleViewCannotQueueNewRegroupButFreshReadRestoresIt()
        {
            _net.PlayerId = 11;
            Loaded();
            Assert.That(_client.Refresh(), Is.True);
            _net.Reply(ReadFailure());
            _client.RequestRegroup();
            Assert.That(_client.RegroupQueued, Is.False, "旧入口和自动跟随不能在过期视图中新建归队意图");
            _now += TeamClient.ReadRetryDelaySeconds;
            _client.DrainQueued(false);
            Assert.That(((GetMyTeamRequest)_net.Calls[_net.Calls.Count - 1].Request).Regroup, Is.False);
            _net.Reply(new TeamResponse { Team = View() });
            _client.RequestRegroup();
            Assert.That(_client.RegroupQueued, Is.True, "权威读恢复后可以发起新的归队");
        }

        private void Loaded(TeamView view = null)
        {
            _client.DrainQueued(false);
            _net.Reply(new TeamResponse { Team = view ?? View() });
            _now += 1;
            _client.DrainQueued(false);
            _net.Reply(new ListMyInvitesResponse());
            _now += 1;
            Assert.That(_client.HasFreshView, Is.True);
        }

        private void Push(TeamView view) => _push(new TeamSnapshotS2C
        {
            Team = view, Reason = TeamChangeReason.MemberOnline
        });

        private static TeamResponse ReadFailure() => new TeamResponse
        {
            ErrorMessage = new TipInfoMessage { Id = (uint)team_error.KTeamStateChanged }
        };

        private static TeamView View(ulong version = 1, ulong teamId = 100)
        {
            var view = new TeamView { TeamId = teamId, LeaderId = 10, Capacity = 5, Version = version, MembershipEpoch = 5 };
            view.Members.Add(new TeamMemberView { PlayerId = 10, IsOnline = true, JoinSeq = 1 });
            view.Members.Add(new TeamMemberView { PlayerId = 11, IsOnline = true, JoinSeq = 2 });
            return view;
        }
    }
}
