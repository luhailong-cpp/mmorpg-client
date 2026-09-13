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
            Assert.That(state.Tick(109.9f), Is.False);
            Assert.That(state.Tick(110f), Is.True);
            Assert.That(state.IsBusy, Is.False);
            Assert.That(state.PendingPlayerId, Is.Zero);
            Assert.That(state.Snapshot.Applications.Count, Is.EqualTo(1));
            Assert.That(state.Snapshot.Approved, Is.Empty);

            now = 111f;
            int newToken = state.BeginDecision(20, true);
            Assert.That(newToken, Is.Not.Zero.And.Not.EqualTo(oldToken));
            Assert.That(state.Complete(oldToken, LeaderSnapshot(), "旧回复"), Is.False);
            Assert.That(state.Fail(oldToken, "旧失败"), Is.False);
            Assert.That(state.IsBusy, Is.True);
            Assert.That(state.Complete(newToken, LeaderSnapshot(), "已更新"), Is.True);
            Assert.That(state.Status, Is.EqualTo("已更新"));
        }

        [Test]
        public void AuthoritativePush_InvalidatesInFlightReply()
        {
            var state = new TeamUiState(() => 0f);
            state.SetSnapshot(LeaderSnapshot());
            int token = state.BeginRefresh();
            var updated = LeaderSnapshot();
            updated.Members.Add(updated.Applications[0]);
            updated.Applications.Clear();

            state.SetSnapshot(updated);

            Assert.That(state.IsBusy, Is.False);
            Assert.That(state.Complete(token, LeaderSnapshot(), "过期"), Is.False);
            Assert.That(state.Fail(token, "过期"), Is.False);
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

            Assert.That(state.BeginDecision(20, true), Is.Zero);
            Assert.That(state.BeginDecision(20, false), Is.Zero);
            Assert.That(state.BeginRefresh(), Is.Zero);
            Assert.That(state.Fail(token, "服务器拒绝请求"), Is.True);
            Assert.That(state.Fail(token, "重复失败"), Is.False);
            Assert.That(state.Status, Is.EqualTo("服务器拒绝请求"));
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
