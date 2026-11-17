using System;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Team;
using MmorpgClient.Net;
using NUnit.Framework;
using Teampb;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class TeamStartRateLimitTests
    {
        private TeamFakeTransport _net;
        private TeamClient _client;
        private float _now;

        [SetUp]
        public void SetUp()
        {
            _net = new TeamFakeTransport();
            _now = 0;
            _client = new TeamClient(_net, () => _net.Identity, _ => { }, _ => { }, () => _now);
            _client.DrainQueued(false);
            _net.Reply(new TeamResponse { Team = View(1) });
            _now += 1;
        }

        [TearDown]
        public void TearDown() => _client.Dispose();

        [Test]
        public void FirstDefiniteGateRejection_AllowsANewBackfillRequestAfterTheWindow()
        {
            Assert.That(_client.StartMatch(1, 5), Is.True);
            string refusedId = LastStart.RequestId;
            _net.Fail("server tip=" + (uint)common_error.KRateLimitExceeded);
            _now += TeamClient.MessageWindowSeconds + 1;

            Assert.That(_client.StartMatch(1, 5), Is.True, _client.Status);
            Assert.That(LastStart.RequestId, Is.Not.EqualTo(refusedId), "首次明确拒绝已清理，不留下假未知状态");
        }

        [Test]
        public void DefiniteRejectionAfterAnUncertainAttempt_DoesNotClearTheOriginalIntent()
        {
            Assert.That(_client.StartMatch(1, 5), Is.True);
            var original = LastStart.Clone();
            _net.Fail("rpc timeout");
            _net.Identity = new object();
            _client.ObserveConnection();
            _now += 4;
            _client.DrainQueued(false);
            Assert.That(((GetMyTeamRequest)_net.Calls[_net.Calls.Count - 1].Request).MatchRequestId,
                Is.EqualTo(original.RequestId));
            // 支持正式恢复的服务器答复同一请求，state 0 仍不能证明迟到的第一次尝试永远不会提交。
            _net.Reply(new TeamResponse { Team = View(2), MatchIntent = new TeamMatchIntent { RequestId = original.RequestId } });
            _now += 1;
            Assert.That(_client.StartMatch(1, 5), Is.True);
            Assert.That(LastStart, Is.EqualTo(original));
            _net.Fail("server tip=" + (uint)common_error.KRateLimitExceeded);
            _now += TeamClient.MessageWindowSeconds + 1;

            int calls = _net.Calls.Count;
            Assert.That(_client.StartMatch(2, 5), Is.False, "本次拒绝不撤销第一次可能已执行的原意图");
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));
            Assert.That(_client.Status, Does.Contain("上一笔队伍开局结果尚未确认"));
            Assert.That(_client.StartMatch(1, 5), Is.True);
            Assert.That(LastStart, Is.EqualTo(original), "后续恢复只重用原UUID与原补位参数");
        }

        [Test]
        public void LocallyRejectedDoubleClick_DoesNotTurnTheFirstGateRejectionIntoAnUnknownOutcome()
        {
            Assert.That(_client.StartMatch(1, 5), Is.True);
            string refusedId = LastStart.RequestId;
            int calls = _net.Calls.Count;
            Assert.That(_client.StartMatch(1, 5), Is.False, "第一次仍在途，第二次由Busy本地拒绝");
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));
            _net.Fail("server tip=" + (uint)common_error.KRateLimitExceeded);
            _now += TeamClient.MessageWindowSeconds + 1;

            Assert.That(_client.StartMatch(1, 5), Is.True, _client.Status);
            Assert.That(LastStart.RequestId, Is.Not.EqualTo(refusedId));
        }

        private StartTeamMatchRequest LastStart => (StartTeamMatchRequest)_net.Calls[_net.Calls.Count - 1].Request;

        [Test]
        public void MatchEntryWaitsForBackgroundReadAndCooldownWithoutDeferringTheClick()
        {
            var entry = new BattleMatchEntryClient(_client, new BattleClient(_net), 1, 1);
            _client.RequestRefresh();
            _client.DrainQueued(false);
            Assert.That(_client.Busy, Is.True);
            Assert.That(entry.CanStartNow, Is.False);
            Assert.That(entry.StartBlockedReason, Does.Contain("同步队伍"));
            int calls = _net.Calls.Count;
            entry.StartPvp(2);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));

            _net.Reply(new TeamResponse { Team = View(2) });
            Assert.That(entry.CanStartNow, Is.False, "读请求回来后仍遵守全局发送间隔");
            Assert.That(entry.StartBlockedReason, Does.Contain("冷却"));
            _now += 1;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.FindAll(call => call.Id == MessageIds.StartTeamMatch), Is.Empty,
                "被拦截的点击不会在后台请求完成后自行开战");
            if (_client.Busy) _net.Reply(new ListMyInvitesResponse());
            _now += 1;

            Assert.That(entry.CanStartNow, Is.True);
            entry.StartPvp(2);
            Assert.That(LastStart.MatchMode, Is.EqualTo(Match.MatchMode.PvpTeam));
            Assert.That(LastStart.TargetTeamSize, Is.EqualTo(2));
        }

        [Test]
        public void MatchEntryExposesTheExistingPerOperationQuotaAndDoesNotQueueARejectedClick()
        {
            var entry = new BattleMatchEntryClient(_client, new BattleClient(_net), 1, 1);
            for (ulong version = 2; version <= 4; version++)
            {
                Assert.That(_client.StartMatch(1, 2, Match.MatchMode.PvpTeam), Is.True);
                _net.Reply(new TeamResponse
                {
                    Team = View(version),
                    ErrorMessage = new TipInfoMessage { Id = (uint)team_error.KTeamRatingGapTooLarge },
                });
                _now += 0.5f;
            }
            Assert.That(entry.CanStartNow, Is.False);
            Assert.That(entry.StartBlockedReason, Does.Contain("冷却"));
            entry.StartPvp(2);
            Assert.That(_net.Calls.FindAll(call => call.Id == MessageIds.StartTeamMatch).Count, Is.EqualTo(3));
            _now += TeamClient.MessageWindowSeconds;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.FindAll(call => call.Id == MessageIds.StartTeamMatch).Count, Is.EqualTo(3));
            if (_client.Busy) _net.Reply(new ListMyInvitesResponse());
            _now += 1;
            Assert.That(entry.CanStartNow, Is.True);
            entry.StartPvp(2);
            Assert.That(_net.Calls.FindAll(call => call.Id == MessageIds.StartTeamMatch).Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitMatchPreparationFinishesBeforeQueuedBackgroundReadsCanTakeTheSendSlot()
        {
            Action ready = null;
            _client.PrepareMatchNetwork = (complete, _) => ready = complete;
            Assert.That(_client.StartMatch(1, 2, Match.MatchMode.PvpTeam), Is.True);
            Assert.That(_client.MatchNetworkPreparing, Is.True);
            _client.RequestRefresh();
            _now += 1;
            int calls = _net.Calls.Count;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));
            ready();
            Assert.That(_client.MatchNetworkPreparing, Is.False);
            Assert.That(LastStart.MatchMode, Is.EqualTo(Match.MatchMode.PvpTeam));
        }

        [Test]
        public void UncertainPvpStart_PreservesModeAndTargetWhenRecoveringTheOriginalUuid()
        {
            Assert.That(_client.StartMatch(1, 3, Match.MatchMode.PvpTeam), Is.True);
            var original = LastStart.Clone();
            _net.Fail("rpc timeout");
            _net.Identity = new object();
            _client.ObserveConnection();
            _now += 4;
            _client.DrainQueued(false);
            _net.Reply(new TeamResponse { Team = View(2), MatchIntent = new TeamMatchIntent { RequestId = original.RequestId } });
            _now += 1;
            Assert.That(_client.StartMatch(1, 3, Match.MatchMode.PveTeam), Is.False, "同副本、同人数的PVE不是原PVP意图");
            Assert.That(_client.StartMatch(1, 4, Match.MatchMode.PvpTeam), Is.False, "不得更换未知结果请求的目标人数");
            Assert.That(_client.StartMatch(1, 3, Match.MatchMode.PvpTeam), Is.True);
            Assert.That(LastStart, Is.EqualTo(original));
        }

        private TeamView View(ulong version)
        {
            var view = new TeamView { TeamId = 100, LeaderId = _net.PlayerId, Capacity = 5,
                Version = version, MembershipEpoch = 1, MatchFillSupported = true, PvpMatchSupported = true };
            view.Members.Add(new TeamMemberView { PlayerId = _net.PlayerId, IsLeader = true, IsOnline = true });
            return view;
        }
    }
}
