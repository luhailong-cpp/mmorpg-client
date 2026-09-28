using System.Collections.Generic;
using MmorpgClient.Game.Team;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class TeamUiStateTests
    {
        [Test]
        public void Timeout_AllowsRetryAndRejectsLateReply()
        {
            float now = 100f;
            var state = new TeamUiState(() => now);
            state.SetSnapshot(LeaderSnapshot());
            int oldToken = state.BeginDecision(20, true);

            Assert.That(oldToken, Is.Not.Zero);
            Assert.That(state.Tick(119.9f), Is.False);
            Assert.That(state.Tick(120f), Is.True);
            Assert.That(state.IsBusy, Is.False);
            Assert.That(state.PendingPlayerId, Is.Zero);
            Assert.That(state.Snapshot.Applications.Count, Is.EqualTo(1));
            Assert.That(state.Snapshot.Approved, Is.Empty);

            now = 121f;
            int newToken = state.BeginDecision(20, true);
            Assert.That(newToken, Is.Not.Zero.And.Not.EqualTo(oldToken));
            Assert.That(state.Complete(oldToken, LeaderSnapshot(), "旧回复"), Is.False);
            Assert.That(state.Fail(oldToken, "旧失败"), Is.False);
            Assert.That(state.IsBusy, Is.True);
            Assert.That(state.Complete(newToken, LeaderSnapshot(), "已更新"), Is.True);
            Assert.That(state.Status, Is.EqualTo("已更新"));
        }

        [Test]
        public void AuthoritativePush_KeepsInFlightRequestPending()
        {
            var state = new TeamUiState(() => 0f);
            state.SetSnapshot(LeaderSnapshot());
            int token = state.BeginRefresh();
            var updated = LeaderSnapshot();
            updated.Version = 2;
            updated.Members.Add(updated.Applications[0]);
            updated.Applications.Clear();

            Assert.That(state.SetSnapshot(updated), Is.True);

            Assert.That(state.IsBusy, Is.True, "A push must not end someone else's request.");
            Assert.That(state.Snapshot.Version, Is.EqualTo(2ul));
            Assert.That(state.Snapshot.Members.Count, Is.EqualTo(2));
            Assert.That(state.Snapshot.Applications, Is.Empty);

            var older = LeaderSnapshot();
            older.Version = 1;
            Assert.That(state.Complete(token, older, "完成"), Is.True);
            Assert.That(state.IsBusy, Is.False);
            Assert.That(state.Status, Is.EqualTo("完成"));
            Assert.That(state.Snapshot.Version, Is.EqualTo(2ul));
            Assert.That(state.Snapshot.Members.Count, Is.EqualTo(2));
            Assert.That(state.Snapshot.Applications, Is.Empty);
        }

        [Test]
        public void NonLeader_CannotApproveOrRejectApplications()
        {
            var state = new TeamUiState(() => 0f);
            var snapshot = LeaderSnapshot();
            snapshot.LocalPlayerId = 30;
            state.SetSnapshot(snapshot);

            Assert.That(state.IsLeader, Is.False);
            Assert.That(state.BeginDecision(20, true), Is.Zero);
            Assert.That(state.BeginDecision(20, false), Is.Zero);
            Assert.That(state.BeginRefresh(), Is.Not.Zero);
        }

        [Test]
        public void FullTeam_CanRejectButCannotApprove()
        {
            var state = new TeamUiState(() => 0f);
            var snapshot = LeaderSnapshot();
            snapshot.Capacity = 1;
            state.SetSnapshot(snapshot);

            Assert.That(state.IsFull, Is.True);
            Assert.That(state.BeginDecision(20, true), Is.Zero);
            int token = state.BeginDecision(20, false);
            Assert.That(token, Is.Not.Zero);
            Assert.That(state.PendingPlayerId, Is.EqualTo(20));
            Assert.That(state.Snapshot.Applications.Count, Is.EqualTo(1));
            Assert.That(state.Snapshot.Approved, Is.Empty);
        }

        [Test]
        public void PendingDecision_PreventsDuplicateOrRefreshRequests()
        {
            var state = new TeamUiState(() => 0f);
            state.SetSnapshot(LeaderSnapshot());
            int token = state.BeginDecision(20, true);

            Assert.That(state.PendingAction, Is.EqualTo(TeamAction.Decide));
            Assert.That(state.PendingTarget, Is.EqualTo(20ul));
            Assert.That(state.BeginDecision(20, true), Is.Zero);
            Assert.That(state.BeginDecision(20, false), Is.Zero);
            Assert.That(state.BeginRefresh(), Is.Zero);
            Assert.That(state.Fail(token, "服务器拒绝请求"), Is.True);
            Assert.That(state.Fail(token, "重复失败"), Is.False);
            Assert.That(state.Status, Is.EqualTo("服务器拒绝请求"));
            Assert.That(state.PendingAction, Is.EqualTo(TeamAction.None));
            Assert.That(state.BeginDecision(20, false), Is.Not.Zero);
        }

        [Test]
        public void DisconnectReset_ClearsSessionAndRejectsOldReplies()
        {
            var state = new TeamUiState(() => 0f);
            state.SetSnapshot(LeaderSnapshot());
            int token = state.BeginDecision(20, true);

            state.Reset(99);

            Assert.That(state.ServiceAvailable, Is.False);
            Assert.That(state.HasLoaded, Is.False);
            Assert.That(state.IsBusy, Is.False);
            Assert.That(state.IsLeader, Is.False);
            Assert.That(state.Snapshot.LocalPlayerId, Is.EqualTo(99));
            Assert.That(state.Snapshot.TeamId, Is.Zero);
            Assert.That(state.Snapshot.Members, Is.Empty);
            Assert.That(state.Snapshot.Applications, Is.Empty);
            Assert.That(state.Snapshot.Approved, Is.Empty);
            Assert.That(state.Complete(token, LeaderSnapshot(), "上一账号"), Is.False);
            Assert.That(state.Fail(token, "上一账号"), Is.False);
            Assert.That(state.BeginRefresh(), Is.Zero);
            Assert.That(state.BeginDecision(20, true), Is.Zero);
            Assert.That(state.Snapshot.TeamId, Is.Zero, "An old session's view must never be written back.");
        }

        [Test]
        public void ServiceUnavailable_InvalidatesPendingAndDisablesActions()
        {
            var state = new TeamUiState(() => 0f);
            state.SetSnapshot(LeaderSnapshot());
            int token = state.BeginRefresh();

            state.SetUnavailable("组队服务暂不可用");

            Assert.That(state.ServiceAvailable, Is.False);
            Assert.That(state.IsBusy, Is.False);
            Assert.That(state.BeginRefresh(), Is.Zero);
            Assert.That(state.BeginDecision(20, true), Is.Zero);
            Assert.That(state.BeginDecision(20, false), Is.Zero);
            Assert.That(state.Complete(token, LeaderSnapshot(), "迟到回复"), Is.False);
            Assert.That(state.ServiceAvailable, Is.False, "A late reply must not revive the service.");
        }

        [Test]
        public void Approval_UsesOnlyServerApprovedList()
        {
            var state = new TeamUiState(() => 0f);
            state.SetSnapshot(LeaderSnapshot());
            Assert.That(state.BeginDecision(0, true), Is.Zero);
            Assert.That(state.BeginDecision(999, true), Is.Zero);

            int token = state.BeginDecision(20, true);
            Assert.That(state.Snapshot.Approved, Is.Empty);
            var accepted = LeaderSnapshot();
            accepted.Approved.Add(accepted.Applications[0]);
            accepted.Applications.Clear();

            Assert.That(state.Complete(token, accepted, "已同意"), Is.True);
            Assert.That(state.Snapshot.Approved.Count, Is.EqualTo(1));
            Assert.That(state.Snapshot.Members.Count, Is.EqualTo(1));
            Assert.That(state.Snapshot.Applications, Is.Empty);
            Assert.That(state.Complete(token, LeaderSnapshot(), "重复回复"), Is.False);
        }

        [Test]
        public void SnapshotCopy_PreventsCallerMutationAndAcceptsAbsentLists()
        {
            var state = new TeamUiState(() => 0f);
            var snapshot = LeaderSnapshot();
            snapshot.Approved = null;
            state.SetSnapshot(snapshot);

            snapshot.Members[0].Name = "外部修改";
            snapshot.Applications.Clear();

            Assert.That(state.Snapshot.Members[0].Name, Is.EqualTo("队长"));
            Assert.That(state.Snapshot.Applications.Count, Is.EqualTo(1));
            Assert.That(state.Snapshot.Approved, Is.Empty);
        }

        [Test]
        public void Complete_LateReplyWithNewerViewAppliesWithoutChangingStatus()
        {
            float now = 0f;
            var state = new TeamUiState(() => now);
            state.SetSnapshot(LeaderSnapshot());
            int token = state.BeginRefresh();
            Assert.That(state.Tick(TeamUiState.RequestTimeoutSeconds), Is.True);
            Assert.That(state.Status, Is.EqualTo("请求超时，请重试"));
            int changes = 0;
            state.Changed += () => changes++;

            var newer = LeaderSnapshot();
            newer.Version = 2;
            newer.Members.Add(newer.Applications[0]);
            newer.Applications.Clear();

            Assert.That(state.Complete(token, newer, "迟到回复"), Is.False);
            Assert.That(state.Snapshot.Version, Is.EqualTo(2ul));
            Assert.That(state.Snapshot.Members.Count, Is.EqualTo(2));
            Assert.That(state.Snapshot.Applications, Is.Empty);
            Assert.That(state.Status, Is.EqualTo("请求超时，请重试"));
            Assert.That(state.IsBusy, Is.False);
            Assert.That(changes, Is.EqualTo(1));
        }

        [Test]
        public void Complete_NullSnapshotEndsPendingAndKeepsView()
        {
            var state = new TeamUiState(() => 0f);
            state.SetSnapshot(LeaderSnapshot());
            int token = state.BeginDecision(20, false);

            Assert.That(state.Complete(token, null, "已处理"), Is.True);

            Assert.That(state.IsBusy, Is.False);
            Assert.That(state.PendingAction, Is.EqualTo(TeamAction.None));
            Assert.That(state.Status, Is.EqualTo("已处理"));
            Assert.That(state.Snapshot.TeamId, Is.EqualTo(1ul));
            Assert.That(state.Snapshot.Applications.Count, Is.EqualTo(1));
        }

        [Test]
        public void SetSnapshot_ConflictingTeamAtSameEpochIsDropped()
        {
            var state = new TeamUiState(() => 0f);
            state.SetSnapshot(LeaderSnapshot());
            int changes = 0;
            state.Changed += () => changes++;
            var other = LeaderSnapshot();
            other.TeamId = 2;
            other.Version = 9;

            Assert.That(state.SetSnapshot(other), Is.False);

            Assert.That(state.Snapshot.TeamId, Is.EqualTo(1ul));
            Assert.That(state.Snapshot.Version, Is.Zero);
            Assert.That(changes, Is.Zero);
        }

        [Test]
        public void SetSnapshot_HigherEpochEmptyViewClearsTeam()
        {
            var state = new TeamUiState(() => 0f);
            state.SetSnapshot(LeaderSnapshot());
            Assert.That(state.IsLeader, Is.True);

            var empty = new TeamSnapshot { TeamId = 0, LocalPlayerId = 10, MembershipEpoch = 1 };
            Assert.That(state.SetSnapshot(empty), Is.True);

            Assert.That(state.HasTeam, Is.False);
            Assert.That(state.IsLeader, Is.False);
            Assert.That(state.Snapshot.MembershipEpoch, Is.EqualTo(1ul));
            Assert.That(state.Snapshot.Members, Is.Empty);
            Assert.That(state.Snapshot.Applications, Is.Empty);

            var stale = LeaderSnapshot();
            stale.Version = 99;
            Assert.That(state.SetSnapshot(stale), Is.False, "An older epoch must not restore the old team.");
            Assert.That(state.HasTeam, Is.False);
        }

        [Test]
        public void Sync_MirrorsClientStateAndDropsTokens()
        {
            var state = new TeamUiState(() => 0f);
            state.SetSnapshot(LeaderSnapshot());
            int token = state.BeginRefresh();
            Assert.That(token, Is.Not.Zero);

            state.Sync(LeaderSnapshot(), null, true, true, TeamAction.Decide, 20, 0, "");
            Assert.That(state.PendingPlayerId, Is.EqualTo(20ul));
            Assert.That(state.PendingAction, Is.EqualTo(TeamAction.Decide));
            Assert.That(state.PendingTarget, Is.EqualTo(20ul));
            Assert.That(state.IsLoading, Is.False);
            Assert.That(state.IsBusy, Is.True);
            int changes = 0;
            state.Changed += () => changes++;
            TeamSnapshot newer = LeaderSnapshot();
            newer.Version = 9;
            newer.Members.Add(new TeamRole { PlayerId = 11, Name = "旧令牌带来的队员" });
            Assert.That(state.Complete(token, newer, "旧令牌"), Is.False);
            Assert.That(state.Snapshot.Version, Is.Zero, "A token issued before Sync must not overwrite the mirror.");
            Assert.That(state.Snapshot.Members.Count, Is.EqualTo(1));
            Assert.That(changes, Is.Zero);
            Assert.That(state.Fail(token, "旧令牌"), Is.False);
            Assert.That(state.IsBusy, Is.True);
            Assert.That(state.Tick(1e6f), Is.False, "A mirrored busy state has no token and never times out.");
            Assert.That(state.IsBusy, Is.True);

            state.Sync(LeaderSnapshot(), null, true, true, TeamAction.RespondInvite, 300, 0, "");
            Assert.That(state.PendingPlayerId, Is.Zero);
            Assert.That(state.PendingTarget, Is.EqualTo(300ul));
            Assert.That(state.IsBusy, Is.True);

            state.Sync(LeaderSnapshot(), null, true, true, TeamAction.Refresh, 0, 12, "正在同步队伍…");
            Assert.That(state.IsLoading, Is.True);
            Assert.That(state.IsBusy, Is.True);
            Assert.That(state.HighlightPlayerId, Is.EqualTo(12ul));
            Assert.That(state.Status, Is.EqualTo("正在同步队伍…"));
            Assert.That(state.Tick(1e6f), Is.False);

            state.Sync(null, null, false, false, TeamAction.None, 0, 0, null);
            Assert.That(state.Snapshot, Is.Not.Null);
            Assert.That(state.Snapshot.TeamId, Is.Zero);
            Assert.That(state.Snapshot.LocalPlayerId, Is.EqualTo(10ul));
            Assert.That(state.HasLoaded, Is.False);
            Assert.That(state.ServiceAvailable, Is.False);
            Assert.That(state.IsBusy, Is.False);
            Assert.That(state.Status, Is.Empty);
        }

        [Test]
        public void Sync_CopiesInvitesDeeply()
        {
            var state = new TeamUiState(() => 0f);
            var invite = new TeamInvite
            {
                TeamId = 300, LeaderId = 30, MemberCount = 2, ExpiresAt = 60f,
                Inviter = new TeamRole { PlayerId = 30, Name = "邀请人" },
            };
            var invites = new List<TeamInvite> { invite, null };

            state.Sync(new TeamSnapshot { LocalPlayerId = 10 }, invites, true, true, TeamAction.None, 0, 0, "");
            invite.TeamId = 999;
            invite.Inviter.Name = "外部修改";
            invites.Clear();

            Assert.That(state.Invites.Count, Is.EqualTo(1));
            Assert.That(state.Invites[0].TeamId, Is.EqualTo(300ul));
            Assert.That(state.Invites[0].Inviter.Name, Is.EqualTo("邀请人"));
            Assert.That(state.HasLoaded, Is.True);
            Assert.That(state.HasTeam, Is.False);
        }

        [Test]
        public void MatchStarting_BlocksApprovalButAllowsReject()
        {
            var state = new TeamUiState(() => 0f);
            var snapshot = LeaderSnapshot();
            snapshot.MatchStarting = true;
            state.SetSnapshot(snapshot);

            Assert.That(state.MatchStarting, Is.True);
            Assert.That(state.BeginDecision(20, true), Is.Zero);
            int token = state.BeginDecision(20, false);
            Assert.That(token, Is.Not.Zero);
            Assert.That(state.PendingPlayerId, Is.EqualTo(20ul));
        }

        [Test]
        public void RequestTimeout_IsLongerThanTransportDeadline()
        {
            Assert.That(TeamUiState.RequestTimeoutSeconds, Is.GreaterThan(15f));
        }

        private static TeamSnapshot LeaderSnapshot()
        {
            return new TeamSnapshot
            {
                TeamId = 1,
                LeaderId = 10,
                LocalPlayerId = 10,
                Capacity = 5,
                Members = new List<TeamRole>
                {
                    new TeamRole { PlayerId = 10, Name = "队长", Level = 60, IsLeader = true }
                },
                Applications = new List<TeamRole>
                {
                    new TeamRole { PlayerId = 20, Name = "申请人", Level = 58, SchoolName = "金系" }
                }
            };
        }
    }
}
