using System;
using Google.Protobuf.WellKnownTypes;
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
                _now += 0.4f;
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

        [Test]
        public void SlidingWindowLimiter_And_CircuitBreaker_EnforceThreeStateHalfOpenSemantics()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SlidingWindowLimiter(0, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SlidingWindowLimiter(1, 0f));
            var limiter = new SlidingWindowLimiter(2, 2.5f);
            Assert.That(limiter.CanAcquire(0f), Is.True);
            Assert.That(limiter.TryAcquire(0f), Is.True);
            Assert.That(limiter.TryAcquire(1f), Is.True);
            Assert.That(limiter.CanAcquire(2f), Is.False);
            Assert.That(limiter.TryAcquire(2f), Is.False);
            Assert.That(limiter.TryAcquire(2.5f), Is.True);
            limiter.Clear();
            Assert.That(limiter.CanAcquire(2.5f), Is.True);

            Assert.Throws<ArgumentOutOfRangeException>(() => new SimpleCircuitBreaker(0, 10f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimpleCircuitBreaker(2, 0f));
            var breaker = new SimpleCircuitBreaker(2, 10f);
            Assert.That(breaker.GetState(0f), Is.EqualTo(CircuitState.Closed));
            Assert.That(breaker.TryAcquire(1f), Is.True);
            breaker.RecordFailure(1f);
            Assert.That(breaker.GetState(1f), Is.EqualTo(CircuitState.Closed));

            Assert.That(breaker.TryAcquire(2f), Is.True);
            breaker.RecordFailure(2f);
            Assert.That(breaker.GetState(11.9f), Is.EqualTo(CircuitState.Open));
            Assert.That(breaker.CanAcquire(11.9f), Is.False);
            Assert.That(breaker.TryAcquire(11.9f), Is.False);

            // 冷却结束后进入 HalfOpen：只放行 1 个探测请求，并发调用被挡住。
            Assert.That(breaker.GetState(12f), Is.EqualTo(CircuitState.HalfOpen));
            Assert.That(breaker.TryAcquire(12f), Is.True, "冷却期结束后放行一次探测");
            Assert.That(breaker.TryAcquire(12.1f), Is.False, "HalfOpen 探测在途时拒绝并发放行");

            // 探测撞到 1008 限流等非下游故障：释放探测槽，不重开熔断也不直接关闭。
            breaker.ReleaseProbe();
            Assert.That(breaker.GetState(12.2f), Is.EqualTo(CircuitState.HalfOpen));
            Assert.That(breaker.TryAcquire(12.2f), Is.True);

            // 探测真失败：立即重开熔断一个完整周期。
            breaker.RecordFailure(12.2f);
            Assert.That(breaker.GetState(12.3f), Is.EqualTo(CircuitState.Open));
            Assert.That(breaker.TryAcquire(22.1f), Is.False);
            Assert.That(breaker.TryAcquire(22.2f), Is.True);

            // 探测成功：恢复 Closed。
            breaker.RecordSuccess();
            Assert.That(breaker.GetState(22.2f), Is.EqualTo(CircuitState.Closed));
            Assert.That(breaker.TryAcquire(22.2f), Is.True);
        }

        [Test]
        public void TeamClientReadBreaker_OpensOnDownstreamFaults_AndIgnoresRateLimit()
        {
            // 1008 限流不计入熔断：连续 3 次 1008 后，过退避期仍可继续后台刷新。
            for (int i = 0; i < TeamClient.ReadBreakerFailureThreshold; i++)
            {
                _client.RequestRefresh();
                _client.DrainQueued(false);
                _net.Fail("server tip=" + (uint)common_error.KRateLimitExceeded);
                _now += TeamClient.ReadRetryDelaySeconds;
            }
            int beforeRetry = _net.Calls.Count;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(beforeRetry + 1), "1008 不应触发熔断");
            _net.Reply(new TeamResponse { Team = View(2) });
            _now += TeamClient.ReadRetryDelaySeconds;

            // 下游读失败（Team == null）连续 3 次才打开熔断，15 秒内暂停后台重试。
            for (int i = 0; i < TeamClient.ReadBreakerFailureThreshold; i++)
            {
                _client.RequestRefresh();
                _client.DrainQueued(false);
                _net.Reply(new TeamResponse());
                _now += TeamClient.ReadRetryDelaySeconds;
            }
            int atOpen = _net.Calls.Count;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(atOpen), "熔断打开期跳过后台重试");

            // 冷却结束后放行一次探测；若这次探测恰好遇到 1008 限流，应释放探测槽位，过读退避期后仍可再次探测，而不是锁死或重开 15 秒。
            _now += TeamClient.ReadBreakerOpenSeconds;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(atOpen + 1), "熔断冷却后放行一次探测");
            _net.Fail("server tip=" + (uint)common_error.KRateLimitExceeded);
            _now += TeamClient.ReadRetryDelaySeconds;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(atOpen + 2), "HalfOpen 探测遇 1008 释放槽位，不重开 15 秒熔断");
            _net.Reply(new TeamResponse { Team = View(3) });
            Assert.That(_client.HasFreshView, Is.True);
        }

        [Test]
        public void SoloMatchEntry_LimitsRapidStartAndCancelWithinWindow()
        {
            float now = 0f;
            var net = new TeamFakeTransport();
            using var soloTeam = new TeamClient(net, () => net.Identity, _ => { }, _ => { }, () => now);
            soloTeam.DrainQueued(false);
            net.Reply(new TeamResponse { Team = new TeamView { Version = 1, MembershipEpoch = 1 } });
            now += 1f;

            var battle = new BattleClient(net);
            var entry = new BattleMatchEntryClient(soloTeam, battle, 1, 1, () => now);

            for (int i = 0; i < 3; i++)
            {
                Assert.That(entry.CanStartNow, Is.True);
                entry.StartPve();
                net.Reply(new Match.JoinQueueResponse { QueueTicket = "t" + i });
                Assert.That(entry.CanCancelNow, Is.True);
                Assert.That(entry.Cancel(), Is.True);
                net.Reply(new Empty());
                now += 0.2f;
            }

            Assert.That(entry.CanStartNow, Is.False);
            Assert.That(entry.StartBlockedReason, Does.Contain("冷却"));
            int joinCallsBefore = net.Calls.FindAll(c => c.Id == MessageIds.JoinQueue).Count;
            entry.StartPve();
            Assert.That(net.Calls.FindAll(c => c.Id == MessageIds.JoinQueue).Count, Is.EqualTo(joinCallsBefore));

            now += TeamClient.MessageWindowSeconds;
            Assert.That(entry.CanStartNow, Is.True);

            // 排队中快速连点 4 次取消：前 3 次发出，第 4 次在本地被拦并返回 false。
            entry.StartPve();
            net.Reply(new Match.JoinQueueResponse { QueueTicket = "t-cancel" });
            for (int i = 0; i < 3; i++)
            {
                Assert.That(entry.CanCancelNow, Is.True);
                Assert.That(entry.Cancel(), Is.True);
            }
            Assert.That(entry.CanCancelNow, Is.False);
            int cancelCallsBefore = net.Calls.FindAll(c => c.Id == MessageIds.CancelQueue).Count;
            Assert.That(entry.Cancel(), Is.False);
            Assert.That(entry.Status, Does.Contain("冷却"));
            Assert.That(net.Calls.FindAll(c => c.Id == MessageIds.CancelQueue).Count, Is.EqualTo(cancelCallsBefore));

            now += TeamClient.MessageWindowSeconds;
            Assert.That(entry.CanCancelNow, Is.True);
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
