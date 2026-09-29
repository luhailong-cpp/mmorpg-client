using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Team;
using MmorpgClient.Net;
using NUnit.Framework;
using Teampb;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class TeamClientTests
    {
        private float _now;
        private TeamFakeTransport _net;
        private TeamClient _client;
        private Action<TeamSnapshotS2C> _push;

        [SetUp]
        public void SetUp()
        {
            _now = 0f;
            _push = null;
            _net = new TeamFakeTransport();
            _client = new TeamClient(_net, () => _net.Identity,
                handler => _push = handler,
                handler => { if (_push == handler) _push = null; },
                () => _now);
        }

        [TearDown]
        public void TearDown() => _client.Dispose();

        [Test]
        public void Construct_RegistersEventAndInviteButNeverSnapshotNotify()
        {
            Assert.That(_net.Notifies.Keys, Is.EquivalentTo(new[] { MessageIds.NotifyTeamEvent, MessageIds.NotifyTeamInvite }));
            Assert.That(_net.Notifies.ContainsKey(MessageIds.NotifyTeamSnapshot), Is.False);
            Assert.That(_push, Is.Not.Null);
            Assert.That(_net.Calls, Is.Empty, "构造不发请求");
            Assert.That(_client.Status, Is.EqualTo(TeamClient.ConnectingMessage));
            Assert.That(_client.RefreshQueued && _client.InvitesQueued, Is.True);

            _client.Dispose();
            Assert.That(_push, Is.Null);

            int changes = 0;
            _client.Changed += () => changes++;
            string status = _client.Status;
            Assert.DoesNotThrow(() => _net.Push(MessageIds.NotifyTeamInvite, InvitePush(300, 30)));
            Assert.DoesNotThrow(() => _net.Push(MessageIds.NotifyTeamEvent,
                new TeamEventS2C { Type = TeamEventType.ApplicationRejected, TeamId = 300 }));
            Assert.That(_client.Invites, Is.Empty);
            Assert.That(_client.Status, Is.EqualTo(status));
            Assert.That(changes, Is.Zero);
            _client.DrainQueued(true);
            Assert.That(_net.Calls, Is.Empty);
        }

        [Test]
        public void Probe_IsFirstAndOnlyRequestUntilLoaded()
        {
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
            Assert.That(_net.Calls[0].Id, Is.EqualTo(MessageIds.GetMyTeam));
            Assert.That(((GetMyTeamRequest)_net.Calls[0].Request).NotifyOnline, Is.True);
            Assert.That(_client.Status, Is.EqualTo(TeamClient.ConnectingMessage), "探针静默发送,状态栏保持连接中");

            Assert.That(_client.Create(), Is.False);
            Assert.That(_client.Status, Is.EqualTo(TeamClient.ConnectingMessage));
            Assert.That(_client.LoadInvites(), Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(1));

            _net.Reply(new TeamResponse { Team = new TeamView { MembershipEpoch = 5 } });
            Assert.That(_client.HasLoaded, Is.True);
            Assert.That(_client.ServiceAvailable, Is.True);
            Assert.That(_client.HasTeam, Is.False);
            Assert.That(_client.Busy, Is.False);
            Assert.That(_client.Status, Is.Empty, "探针成功后收掉连接中提示,交给界面默认文案");

            _now = 0.3f;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "0.4 秒内不再发送");

            _now = 0.5f;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(_net.Calls[1].Id, Is.EqualTo(MessageIds.ListMyInvites));

            var invites = new ListMyInvitesResponse { ServerTimeMs = 1000 };
            invites.Invites.Add(IncomingInvite(300, 30, expireAtMs: 61000));
            invites.Invites.Add(IncomingInvite(301, 31, expireAtMs: 31000));
            _net.Reply(invites);
            Assert.That(_client.Invites.Count, Is.EqualTo(2));
            Assert.That(_client.Invites[0].ExpiresAt, Is.EqualTo(60.5f).Within(0.001f));
            Assert.That(_client.Invites[1].ExpiresAt, Is.EqualTo(30.5f).Within(0.001f));
            Assert.That(_client.InvitesQueued, Is.False);
        }

        [Test]
        public void ProbeTimeout_SuspendsTeamForTheConnection()
        {
            _client.DrainQueued(false);
            _net.Fail("rpc timeout");

            Assert.That(_client.Suspended, Is.True);
            Assert.That(_client.RequiresReconnect, Is.True);
            Assert.That(_client.Busy, Is.False);
            Assert.That(_client.ServiceAvailable, Is.False);
            Assert.That(_client.Status, Is.EqualTo(TeamClient.SuspendedMessage));

            _now = 5f;
            _client.DrainQueued(true);
            Assert.That(_client.Refresh(), Is.False);
            Assert.That(_client.Create(), Is.False);
            Assert.That(_client.ApplyJoin(42), Is.False);
            Assert.That(_client.Status, Is.EqualTo(TeamClient.SuspendedMessage));
            Assert.That(_net.Calls.Count, Is.EqualTo(1));

            _net.Disconnect();
            Assert.That(_client.Suspended, Is.False);
            Assert.That(_client.RequiresReconnect, Is.False);
            Assert.That(_client.Status, Is.EqualTo(TeamClient.ConnectingMessage));

            _net.IsReady = true; // 重连成功
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(_net.Calls[1].Id, Is.EqualTo(MessageIds.GetMyTeam));
            Assert.That(((GetMyTeamRequest)_net.Calls[1].Request).NotifyOnline, Is.True);
        }

        [Test]
        public void OuterEnvelopeError_SuspendsEvenAfterLoaded()
        {
            Loaded();
            Assert.That(_client.Kick(11), Is.True);
            _net.Fail("server tip=5");

            Assert.That(_client.Suspended, Is.True);
            Assert.That(_client.ServiceAvailable, Is.False);
            Assert.That(_client.Status, Is.EqualTo(TeamClient.SuspendedMessage));

            int calls = _net.Calls.Count;
            _now += 60f;
            _client.DrainQueued(true);
            Assert.That(_client.Refresh(), Is.False);
            Assert.That(_client.Disband(), Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));

            // 推送也不再改动状态,直到连接变化。
            _push(new TeamSnapshotS2C { Team = LeaderView(9), Reason = TeamChangeReason.Healed });
            Assert.That(_client.Snapshot.Version, Is.EqualTo(1UL));
        }

        [Test]
        public void RateLimited_IsADefiniteRejectionNotASuspension()
        {
            Loaded();
            Assert.That(_client.Kick(11), Is.True);
            // gate 限流回包带原请求 id,是对这一包的明确拒绝。
            _net.Fail("server tip=1008");

            Assert.That(_client.Suspended, Is.False);
            Assert.That(_client.RequiresReconnect, Is.False);
            Assert.That(_client.ServiceAvailable, Is.True);
            Assert.That(_client.Busy, Is.False);
            Assert.That(_client.Status, Is.EqualTo("操作太快了，请稍候再试。"));

            // 推送照常应用,下一个动作照常发出。
            _push(new TeamSnapshotS2C { Team = LeaderView(2), Reason = TeamChangeReason.MemberOnline });
            Assert.That(_client.Snapshot.Version, Is.EqualTo(2UL));
            _now += 0.5f;
            Assert.That(_client.Leave(), Is.True);
            Assert.That(LastId, Is.EqualTo(MessageIds.LeaveTeam));
            _net.Reply(new TeamResponse { Team = LeaderView(3) });

            // 被限流的读:重新排队,退避期内不补发。
            _now += 0.5f;
            Assert.That(_client.Refresh(), Is.True);
            _net.Fail("server tip=1008");
            Assert.That(_client.Suspended || _client.RequiresReconnect, Is.False);
            Assert.That(_client.RefreshQueued, Is.True);
            int calls = _net.Calls.Count;
            _now += 0.5f;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls), "退避期内不补发");
            _now += TeamClient.ReadRetryDelaySeconds;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls + 1));
            Assert.That(LastId, Is.EqualTo(MessageIds.GetMyTeam));
        }

        [Test]
        public void RateLimitedProbe_RetriesAfterBackoffWithNotifyOnline()
        {
            _client.DrainQueued(false);
            _net.Fail("server tip=1008");

            Assert.That(_client.Suspended || _client.RequiresReconnect, Is.False);
            Assert.That(_client.HasLoaded, Is.False);
            Assert.That(_client.Status, Is.EqualTo(TeamClient.ConnectingMessage), "静默探针不写限流提示");

            _now = 1f;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "退避期内不重发探针");

            _now = TeamClient.ReadRetryDelaySeconds;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(LastId, Is.EqualTo(MessageIds.GetMyTeam));
            Assert.That(LastRequest<GetMyTeamRequest>().NotifyOnline, Is.True, "请求没到组队服务,上线通知要再带一次");
        }

        [Test]
        public void SameWriteId_FourthWithinWindowIsRejectedLocally()
        {
            Loaded();   // 结束时 _now == 1.5
            for (int i = 0; i < 3; i++)
            {
                Assert.That(_client.HandleApplication(20, false), Is.True, "第 " + (i + 1) + " 次");
                _net.Reply(new TeamResponse { Team = LeaderView((ulong)(2 + i)) });
                _now += 0.5f;
            }

            // _now == 3.0:距第一次只有 1.5 秒,gate 按整秒计时会把它算进同一窗口,回 1008 并计一次非法包。
            int calls = _net.Calls.Count;
            Assert.That(_client.HandleApplication(21, false), Is.False);
            Assert.That(_client.Status, Is.EqualTo("操作太快了，请稍候再试。"));
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));

            // 配额按 message_id 各自计:别的写操作不受影响。
            Assert.That(_client.Invite(13), Is.True);
            Assert.That(LastId, Is.EqualTo(MessageIds.InviteToTeam));
            _net.Reply(new TeamResponse { Team = LeaderView(5) });

            _now = 1.5f + TeamClient.MessageWindowSeconds - 0.1f;
            Assert.That(_client.HandleApplication(21, false), Is.False);
            _now = 1.5f + TeamClient.MessageWindowSeconds;
            Assert.That(_client.HandleApplication(21, false), Is.True);
            Assert.That(LastId, Is.EqualTo(MessageIds.HandleApplication));
        }

        [Test]
        public void GetMyTeamWithoutView_IsNotNoTeamAndRetriesWithBackoff()
        {
            // 探针:服务端自由读失败(4029 / 4030)时只回 tip、不带视图。
            _client.DrainQueued(false);
            _net.Reply(Response(null, team_error.KTeamStateChanged));
            Assert.That(_client.HasLoaded, Is.True, "探针已回:组队服务在线");
            Assert.That(_client.HasView, Is.False, "占位的无队快照不作数");
            Assert.That(_client.RefreshQueued, Is.True);
            Assert.That(_client.Status, Is.EqualTo("队伍状态已变化，请重试。"));

            _now = 1f;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "退避期内不重拉,也不补拉邀请");

            _now = TeamClient.ReadRetryDelaySeconds;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(LastId, Is.EqualTo(MessageIds.GetMyTeam));
            Assert.That(LastRequest<GetMyTeamRequest>().NotifyOnline, Is.True, "上次没走到上线广播,重拉再带一次");
            _net.Reply(new TeamResponse { Team = LeaderView(1) });
            Assert.That(_client.HasView, Is.True);
            Assert.That(_client.HasTeam, Is.True);

            // 无队玩家:不带视图的成功回包不据此补拉邀请,也不动已有视图。
            _client.Reset();
            LoadedNoTeam();
            Assert.That(_client.InvitesQueued, Is.False);
            Assert.That(_client.Refresh(), Is.True);
            Assert.That(LastRequest<GetMyTeamRequest>().NotifyOnline, Is.False);
            _net.Reply(new TeamResponse());
            Assert.That(_client.HasView, Is.True);
            Assert.That(_client.Snapshot.MembershipEpoch, Is.EqualTo(5UL));
            Assert.That(_client.InvitesQueued, Is.False);
            Assert.That(_client.RefreshQueued, Is.True);
        }

        [Test]
        public void ListInvitesFailure_KeepsListAndRetriesWithBackoff()
        {
            LoadedNoTeam();
            _net.Push(MessageIds.NotifyTeamInvite, InvitePush(300, 30));
            Assert.That(_client.LoadInvites(), Is.True);
            _net.Reply(new ListMyInvitesResponse { ErrorMessage = Tip(team_error.KTeamInternal) });

            Assert.That(_client.Invites.Count, Is.EqualTo(1), "失败保留旧列表");
            Assert.That(_client.InvitesQueued, Is.True);
            Assert.That(_client.Status, Is.EqualTo("服务器繁忙，请稍后再试。"));

            int calls = _net.Calls.Count;
            _now += 0.5f;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls), "退避期内不补发");
            _now += TeamClient.ReadRetryDelaySeconds;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls + 1));
            Assert.That(LastId, Is.EqualTo(MessageIds.ListMyInvites));
        }

        [Test]
        public void InvitesReply_KeepsPushesAndRevocationsThatArrivedInFlight()
        {
            LoadedNoTeam();
            // 发出前就到的推送:服务端读在它之后,回包里没有就是已失效。
            _net.Push(MessageIds.NotifyTeamInvite, InvitePush(299, 29));
            Assert.That(_client.LoadInvites(), Is.True);
            // 在途期间:新邀请推送先到,另一条邀请被撤回。
            _net.Push(MessageIds.NotifyTeamInvite, InvitePush(300, 30));
            _net.Push(MessageIds.NotifyTeamEvent, new TeamEventS2C { Type = TeamEventType.InviteRevoked, TeamId = 301 });

            // 回包是更早读的:没有 300,还带着已撤回的 301。
            var reply = new ListMyInvitesResponse { ServerTimeMs = 1000 };
            reply.Invites.Add(IncomingInvite(301, 31, expireAtMs: 61000));
            reply.Invites.Add(IncomingInvite(302, 32, expireAtMs: 61000));
            _net.Reply(reply);

            var teams = new List<ulong>();
            foreach (var invite in _client.Invites) teams.Add(invite.TeamId);
            Assert.That(teams, Is.EquivalentTo(new ulong[] { 300, 302 }));

            // 下一次读从头算:这次回包里没有 300,就不再保留。
            _now += 0.5f;
            Assert.That(_client.LoadInvites(), Is.True);
            var next = new ListMyInvitesResponse { ServerTimeMs = 2000 };
            next.Invites.Add(IncomingInvite(302, 32, expireAtMs: 61000));
            _net.Reply(next);
            Assert.That(_client.Invites.Count, Is.EqualTo(1));
            Assert.That(_client.Invites[0].TeamId, Is.EqualTo(302UL));
        }

        [Test]
        public void ErrorAfterAppliedReply_IsIgnored()
        {
            Loaded();
            _net.WrapLikeGameClient = true;
            bool thrown = false;
            // 界面订阅者在应用回包后抛异常:GameClient.Call 会接着调 onError。
            _client.Changed += () =>
            {
                if (thrown || _client.Snapshot.Version != 2) return;
                thrown = true;
                throw new InvalidOperationException("界面渲染失败");
            };

            Assert.That(_client.Refresh(), Is.True);
            _net.Reply(new TeamResponse { Team = LeaderView(2) });

            Assert.That(thrown, Is.True);
            Assert.That(_client.RequiresReconnect, Is.False, "一个请求只结算一次");
            Assert.That(_client.Suspended, Is.False);
            Assert.That(_client.Status, Is.Not.EqualTo(TeamClient.RecoveryMessage));
            _now += 0.5f;
            Assert.That(_client.Leave(), Is.True);
        }

        [Test]
        public void TransportError_RequiresReconnectUntilConnectionChanges()
        {
            Loaded();
            Assert.That(_client.Refresh(), Is.True);
            _net.Fail("parse response: x");

            Assert.That(_client.RequiresReconnect, Is.True);
            Assert.That(_client.Suspended, Is.False);
            Assert.That(_client.Status, Is.EqualTo(TeamClient.RecoveryMessage));

            _now += 5f;
            int calls = _net.Calls.Count;
            Assert.That(_client.Leave(), Is.False);
            Assert.That(_client.Status, Is.EqualTo(TeamClient.RecoveryMessage));
            _client.DrainQueued(true);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));

            _net.Identity = new object();
            _client.ObserveConnection();
            Assert.That(_client.RequiresReconnect, Is.False);
            Assert.That(_client.HasLoaded, Is.False);
            Assert.That(_client.HasTeam, Is.False);
            Assert.That(_client.RefreshQueued, Is.True);
            Assert.That(_client.Status, Is.EqualTo(TeamClient.ConnectingMessage));

            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls + 1));
            Assert.That(LastId, Is.EqualTo(MessageIds.GetMyTeam));
            Assert.That(LastRequest<GetMyTeamRequest>().NotifyOnline, Is.True);
        }

        [Test]
        public void SingleFlight_RejectsUserWritesAndCoalescesReads()
        {
            Loaded();
            Assert.That(_client.Kick(11), Is.True);
            Assert.That(_client.PendingAction, Is.EqualTo(TeamAction.Kick));
            Assert.That(_client.PendingTarget, Is.EqualTo(11UL));
            int calls = _net.Calls.Count;

            _now += 1f;
            Assert.That(_client.Kick(11), Is.False);
            Assert.That(_client.Status, Does.Contain("仍在处理中"));
            Assert.That(_client.Refresh(), Is.False);
            Assert.That(_client.RefreshQueued, Is.True);
            Assert.That(_client.Refresh(), Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));

            _net.Reply(new TeamResponse { Team = View(100, 10, 5, 2, 10, 12) });
            Assert.That(_client.Busy, Is.False);
            Assert.That(_client.PendingAction, Is.EqualTo(TeamAction.None));

            _now += 0.5f;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls + 1), "多次排队合并成一次补发");
            Assert.That(LastId, Is.EqualTo(MessageIds.GetMyTeam));
            Assert.That(LastRequest<GetMyTeamRequest>().NotifyOnline, Is.False);
            _now += 0.5f;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls + 1));
        }

        [Test]
        public void MinSendInterval_RejectsTooFastUserActions()
        {
            Loaded();
            // 基准取 2 秒:2f + 0.4f 向上舍入,无论运行时按单精度还是双精度算中间值都恰好"满 0.4 秒"。
            _now = 2f;
            Assert.That(_client.Refresh(), Is.True);
            _net.Reply(new TeamResponse { Team = LeaderView(2) });
            int calls = _net.Calls.Count;

            Assert.That(_client.Disband(), Is.False);
            Assert.That(_client.Status, Is.EqualTo("操作太快了，请稍候再试。"));
            Assert.That(_client.Refresh(), Is.False);
            Assert.That(_client.RefreshQueued, Is.True, "后台读撞上间隔只排队");
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));

            _now += 0.4f;
            Assert.That(_client.Disband(), Is.True);
            Assert.That(LastId, Is.EqualTo(MessageIds.DisbandTeam));
        }

        [Test]
        public void SnapshotPush_DoesNotTouchPendingAndOlderReplyDoesNotOverwrite()
        {
            Loaded();
            Assert.That(_client.Kick(12), Is.True);

            _push(new TeamSnapshotS2C { Team = View(100, 10, 5, 3, 10, 11), Reason = TeamChangeReason.MemberKicked, ActorId = 10 });
            Assert.That(_client.Snapshot.Version, Is.EqualTo(3UL));
            Assert.That(_client.Snapshot.Members.Count, Is.EqualTo(2));
            Assert.That(_client.Busy, Is.True);
            Assert.That(_client.PendingAction, Is.EqualTo(TeamAction.Kick));
            Assert.That(_client.PendingTarget, Is.EqualTo(12UL));

            _net.Reply(new TeamResponse { Team = View(100, 10, 5, 2, 10, 11) });
            Assert.That(_client.Snapshot.Version, Is.EqualTo(3UL));
            Assert.That(_client.Busy, Is.False);
            Assert.That(_client.PendingAction, Is.EqualTo(TeamAction.None));
            Assert.That(_client.Status, Is.EqualTo("已请离该队员。"));
        }

        [Test]
        public void ErrorReplyWithView_IsAppliedByOrderingRules()
        {
            Loaded();
            Assert.That(_client.Kick(11), Is.True);
            _net.Reply(Response(null, team_error.KTeamInternal));
            Assert.That(_client.Snapshot.TeamId, Is.EqualTo(100UL), "服务端读取失败时不清本地快照");
            Assert.That(_client.Snapshot.Members.Count, Is.EqualTo(3));
            Assert.That(_client.RefreshQueued, Is.True, "无视图靠重拉自愈");
            Assert.That(_client.Status, Is.EqualTo("服务器繁忙，请稍后再试。"));

            _now += 0.5f;
            Assert.That(_client.Leave(), Is.True);
            _net.Reply(Response(new TeamView { MembershipEpoch = 6, ServerTimeMs = 2000 }, team_error.KTeamHasNotTeamId));
            Assert.That(_client.HasTeam, Is.False);
            Assert.That(_client.Snapshot.MembershipEpoch, Is.EqualTo(6UL));
            Assert.That(_client.Status, Is.EqualTo("队伍不存在或已解散。"));
            Assert.That(_client.InvitesQueued, Is.True);
        }

        [Test]
        public void Conflict_SameEpochDifferentTeam_DropsAndRequeuesRefresh()
        {
            Loaded();
            int changes = 0;
            _client.Changed += () => changes++;

            _push(new TeamSnapshotS2C { Team = View(200, 20, 5, 9, 20, 10), Reason = TeamChangeReason.MemberJoined });
            Assert.That(_client.Snapshot.TeamId, Is.EqualTo(100UL));
            Assert.That(_client.RefreshQueued, Is.True);
            Assert.That(changes, Is.EqualTo(1));

            int calls = _net.Calls.Count;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls + 1));
            Assert.That(LastId, Is.EqualTo(MessageIds.GetMyTeam));
        }

        [Test]
        public void KickedEmptyView_BlocksLateOldTeamReply()
        {
            Loaded();
            _push(new TeamSnapshotS2C { Team = new TeamView { MembershipEpoch = 8, ServerTimeMs = 2000 }, Reason = TeamChangeReason.MemberKicked });
            Assert.That(_client.Status, Is.EqualTo("你已被请离队伍。"));
            Assert.That(_client.HasTeam, Is.False);
            Assert.That(_client.InvitesQueued, Is.True);

            _push(new TeamSnapshotS2C { Team = View(100, 10, 7, 9, 10, 11, 12), Reason = TeamChangeReason.MemberOnline });
            Assert.That(_client.HasTeam, Is.False);
            Assert.That(_client.Snapshot.MembershipEpoch, Is.EqualTo(8UL));

            // 迟到的旧队回包同样挡住。
            Assert.That(_client.Refresh(), Is.True);
            _net.Reply(new TeamResponse { Team = View(100, 10, 7, 10, 10, 11, 12) });
            Assert.That(_client.HasTeam, Is.False);
            Assert.That(_client.Snapshot.MembershipEpoch, Is.EqualTo(8UL));
        }

        [Test]
        public void WriteRequests_CarryCurrentExpectedTeamId()
        {
            Loaded();
            ulong version = 1;

            Assert.That(_client.HandleApplication(20, true), Is.True);
            var decide = LastRequest<HandleApplicationRequest>();
            Assert.That(decide.ExpectedTeamId, Is.EqualTo(100UL));
            Assert.That(decide.ApplicantId, Is.EqualTo(20UL));
            Assert.That(decide.Approve, Is.True);
            Next(ref version);

            Assert.That(_client.Invite(13), Is.True);
            Assert.That(LastRequest<InviteToTeamRequest>().ExpectedTeamId, Is.EqualTo(100UL));
            Assert.That(LastRequest<InviteToTeamRequest>().TargetPlayerId, Is.EqualTo(13UL));
            Next(ref version);

            Assert.That(_client.Leave(), Is.True);
            Assert.That(LastRequest<LeaveTeamRequest>().ExpectedTeamId, Is.EqualTo(100UL));
            Next(ref version);

            Assert.That(_client.Kick(11), Is.True);
            Assert.That(LastRequest<KickMemberRequest>().ExpectedTeamId, Is.EqualTo(100UL));
            Assert.That(LastRequest<KickMemberRequest>().TargetPlayerId, Is.EqualTo(11UL));
            Next(ref version);

            Assert.That(_client.TransferLeader(12), Is.True);
            Assert.That(LastRequest<TransferLeaderRequest>().ExpectedTeamId, Is.EqualTo(100UL));
            Assert.That(LastRequest<TransferLeaderRequest>().TargetPlayerId, Is.EqualTo(12UL));
            Next(ref version);

            Assert.That(_client.Disband(), Is.True);
            Assert.That(LastRequest<DisbandTeamRequest>().ExpectedTeamId, Is.EqualTo(100UL));
            Next(ref version);

            Assert.That(_client.StartMatch(7), Is.True);
            Assert.That(LastRequest<StartTeamMatchRequest>().ExpectedTeamId, Is.EqualTo(100UL));
            Assert.That(LastRequest<StartTeamMatchRequest>().BattleConfigId, Is.EqualTo(7u));
            Assert.That(LastId, Is.EqualTo(MessageIds.StartTeamMatch));
        }

        [Test]
        public void CreateIdempotency_4003WithOwnLeadershipIsSuccess()
        {
            LoadedNoTeam();
            Assert.That(_client.Create(), Is.True);
            Assert.That(LastId, Is.EqualTo(MessageIds.CreateTeam));
            _net.Reply(Response(View(200, 99, 6, 1, 99, 10), team_error.KTeamMemberInTeam));
            Assert.That(_client.Status, Is.EqualTo("已在队伍中。"));

            _client.Reset();
            LoadedNoTeam();
            Assert.That(_client.Create(), Is.True);
            _net.Reply(Response(View(100, 10, 6, 1, 10), team_error.KTeamMemberInTeam));
            Assert.That(_client.Status, Is.EqualTo("队伍已创建，可邀请道友加入。"));
            Assert.That(_client.IsLeader, Is.True);
        }

        [Test]
        public void StartMatch_InMatchWithStartingViewIsNotAnError()
        {
            Loaded();
            Assert.That(_client.StartMatch(1), Is.True);
            var starting = LeaderView(2);
            starting.MatchState = TeamMatchState.Starting;
            _net.Reply(Response(starting, team_error.KTeamInMatch));

            Assert.That(_client.MatchStarting, Is.True);
            Assert.That(_client.Status, Is.EqualTo("队伍正在进入战斗，请稍候…"));
            Assert.That(_client.RequiresReconnect || _client.Suspended, Is.False);
        }

        [Test]
        public void MemberNotReady_HighlightsParameterPlayer()
        {
            Loaded();
            Assert.That(_client.StartMatch(1), Is.True);
            _net.Reply(Response(LeaderView(2), team_error.KTeamMemberNotReady, "12"));
            Assert.That(_client.HighlightPlayerId, Is.EqualTo(12UL));
            Assert.That(_client.Status, Is.EqualTo("有队员暂时无法开战。"));

            // 静默的面板刷新不清高亮。
            _now += 31f;
            _client.DrainQueued(true);
            Assert.That(LastId, Is.EqualTo(MessageIds.GetMyTeam));
            Assert.That(_client.HighlightPlayerId, Is.EqualTo(12UL));
            _net.Reply(new TeamResponse { Team = LeaderView(3) });

            // 下一个非静默请求发出时清零。
            _now += 0.5f;
            Assert.That(_client.StartMatch(1), Is.True);
            Assert.That(_client.HighlightPlayerId, Is.Zero);
            _net.Reply(Response(LeaderView(4), team_error.KTeamMemberNotReady, "abc"));
            Assert.That(_client.HighlightPlayerId, Is.Zero);

            Assert.That(TeamClient.TipPlayerId(Tip(team_error.KTeamMemberNotReady, "abc")), Is.Zero);
            Assert.That(TeamClient.TipPlayerId(Tip(team_error.KTeamMemberNotReady, "-12")), Is.Zero);
            Assert.That(TeamClient.TipPlayerId(Tip(team_error.KTeamMemberNotReady, " 12")), Is.Zero);
            Assert.That(TeamClient.TipPlayerId(Tip(team_error.KTeamMemberNotReady)), Is.Zero);
            Assert.That(TeamClient.TipPlayerId(null), Is.Zero);
            Assert.That(TeamClient.TipPlayerId(Tip(team_error.KTeamMemberOffline, "18446744073709551615")), Is.EqualTo(ulong.MaxValue));
        }

        [Test]
        public void InvitePushAndExpiry()
        {
            LoadedNoTeam();
            int changes = 0;
            _client.Changed += () => changes++;

            _net.Push(MessageIds.NotifyTeamInvite, InvitePush(300, 30));
            Assert.That(_client.Invites.Count, Is.EqualTo(1));
            Assert.That(_client.Status, Does.Contain("邀请你加入队伍"));
            Assert.That(_client.Status, Is.EqualTo("道友 30 邀请你加入队伍。"));
            Assert.That(changes, Is.EqualTo(1));

            _net.Push(MessageIds.NotifyTeamInvite, InvitePush(300, 30));
            Assert.That(_client.Invites.Count, Is.EqualTo(1), "同一队伍按 TeamId 覆盖");

            _now += 61f;
            changes = 0;
            _client.DrainQueued(false);
            Assert.That(_client.Invites, Is.Empty);
            Assert.That(changes, Is.GreaterThan(0));

            _net.Push(MessageIds.NotifyTeamInvite, InvitePush(301, 31));
            Assert.That(_client.Invites.Count, Is.EqualTo(1));
            changes = 0;
            _net.Push(MessageIds.NotifyTeamEvent, new TeamEventS2C { Type = TeamEventType.InviteRevoked, TeamId = 999 });
            Assert.That(_client.Invites.Count, Is.EqualTo(1));
            Assert.That(changes, Is.Zero, "没有项被移除时不打扰界面");
            _net.Push(MessageIds.NotifyTeamEvent, new TeamEventS2C { Type = TeamEventType.InviteRevoked, TeamId = 301 });
            Assert.That(_client.Invites, Is.Empty);
            Assert.That(_client.Status, Is.EqualTo("一条组队邀请已失效。"));
            Assert.That(changes, Is.EqualTo(1));

            _net.Push(MessageIds.NotifyTeamEvent, new TeamEventS2C { Type = TeamEventType.ApplicationRejected, TeamId = 400, ActorId = 40 });
            Assert.That(_client.Status, Is.EqualTo("对方拒绝了你的入队申请。"));
        }

        [Test]
        public void MalformedPushes_AreIgnored()
        {
            Loaded();
            _net.Push(MessageIds.NotifyTeamInvite, InvitePush(300, 30));
            string status = _client.Status;
            int changes = 0;
            _client.Changed += () => changes++;

            Assert.DoesNotThrow(() => _net.PushRaw(MessageIds.NotifyTeamEvent, new byte[] { 0xFF, 0xFF, 0xFF }));
            Assert.DoesNotThrow(() => _net.PushRaw(MessageIds.NotifyTeamInvite, new byte[] { 0xFF, 0xFF, 0xFF }));

            Assert.That(_client.Status, Is.EqualTo(status));
            Assert.That(_client.Invites.Count, Is.EqualTo(1));
            Assert.That(_client.Snapshot.TeamId, Is.EqualTo(100UL));
            Assert.That(changes, Is.Zero);
        }

        [Test]
        public void RespondInvite_RemovesInviteOnSuccessAndOnNotFound()
        {
            LoadedNoTeam();
            _net.Push(MessageIds.NotifyTeamInvite, InvitePush(300, 30));
            _net.Push(MessageIds.NotifyTeamInvite, InvitePush(301, 31));

            Assert.That(_client.RespondInvite(300, false), Is.True);
            Assert.That(_client.PendingAction, Is.EqualTo(TeamAction.RespondInvite));
            Assert.That(_client.PendingTarget, Is.EqualTo(300UL));
            Assert.That(LastRequest<RespondInviteRequest>().TeamId, Is.EqualTo(300UL));
            Assert.That(LastRequest<RespondInviteRequest>().Accept, Is.False);
            _net.Reply(new TeamResponse { Team = new TeamView { MembershipEpoch = 5 } });
            Assert.That(_client.Status, Is.EqualTo("已谢绝邀请。"));
            Assert.That(_client.Invites.Count, Is.EqualTo(1));
            Assert.That(_client.Invites[0].TeamId, Is.EqualTo(301UL));

            _now += 0.5f;
            Assert.That(_client.RespondInvite(301, true), Is.True);
            _net.Reply(Response(new TeamView { MembershipEpoch = 5 }, team_error.KTeamInviteNotFound));
            Assert.That(_client.Status, Is.EqualTo("邀请已失效。"));
            Assert.That(_client.Invites, Is.Empty);

            _net.Push(MessageIds.NotifyTeamInvite, InvitePush(302, 32));
            _now += 0.5f;
            Assert.That(_client.RespondInvite(302, true), Is.True);
            _net.Reply(new TeamResponse { Team = View(302, 32, 6, 1, 32, 10) });
            Assert.That(_client.Status, Is.EqualTo("已加入队伍。"));
            Assert.That(_client.Invites, Is.Empty);
            Assert.That(_client.Snapshot.TeamId, Is.EqualTo(302UL));
        }

        [Test]
        public void LocalPreconditions_RejectWithoutCalling()
        {
            // 队员视角(队长是 11)
            Loaded(View(100, 11, 5, 1, 10, 11, 12));
            AssertRejected(() => _client.Kick(12), "只有队长可以请离队员。");
            AssertRejected(() => _client.TransferLeader(12), "只有队长可以转让队长。");
            AssertRejected(() => _client.Disband(), "只有队长可以解散队伍。");
            AssertRejected(() => _client.StartMatch(1), "只有队长可以开始战斗。");
            AssertRejected(() => _client.Invite(13), "只有队长可以邀请道友。");
            AssertRejected(() => _client.HandleApplication(20, true), "只有队长可以处理申请。");

            // 队长视角
            _client.Reset();
            Loaded();
            AssertRejected(() => _client.Kick(10), "不能请离自己。");
            AssertRejected(() => _client.Kick(99), "该玩家不在队伍中。");
            AssertRejected(() => _client.TransferLeader(10), "不能转让给自己。");
            AssertRejected(() => _client.TransferLeader(99), "该玩家不在队伍中。");
            AssertRejected(() => _client.Invite(11), "对方已在队伍中。");
            AssertRejected(() => _client.Invite(0), "请输入有效的玩家编号。");
            AssertRejected(() => _client.Create(), "你已在队伍中。");
            AssertRejected(() => _client.ApplyJoin(0), "请输入有效的玩家编号。");
            AssertRejected(() => _client.ApplyJoin(10), "请输入有效的玩家编号。");
            AssertRejected(() => _client.ApplyJoin(42), "你已在队伍中，无法申请加入其他队伍。");
            AssertRejected(() => _client.RespondInvite(300, true), "你已在队伍中。");
            AssertRejected(() => _client.RespondInvite(0, false), "邀请已失效。");
            AssertRejected(() => _client.HandleApplication(0, true), "请输入有效的玩家编号。");
            AssertRejected(() => _client.StartMatch(0), "该副本未开放组队。");

            // 开战集合期间:名单锁定,但邀请与拒绝申请仍可发
            _client.Reset();
            var starting = LeaderView(1);
            starting.MatchState = TeamMatchState.Starting;
            Loaded(starting);
            Assert.That(_client.MatchStarting, Is.True);
            AssertRejected(() => _client.HandleApplication(20, true), "队伍正在进入战斗，请稍候。");
            AssertRejected(() => _client.Leave(), "队伍正在进入战斗，请稍候。");
            AssertRejected(() => _client.Disband(), "队伍正在进入战斗，请稍候。");
            AssertRejected(() => _client.StartMatch(1), "队伍正在进入战斗，请稍候。");
            AssertRejected(() => _client.Kick(11), "队伍正在进入战斗，请稍候。");
            AssertRejected(() => _client.TransferLeader(11), "队伍正在进入战斗，请稍候。");
            Assert.That(_client.Invite(13), Is.True);
            _net.Reply(new TeamResponse { Team = starting });
            _now += 0.5f;
            Assert.That(_client.HandleApplication(20, false), Is.True);
            _net.Reply(new TeamResponse { Team = starting });

            // 无队
            _client.Reset();
            LoadedNoTeam();
            AssertRejected(() => _client.Leave(), "你当前不在队伍中。");
            AssertRejected(() => _client.Invite(13), "请先创建队伍。");
            AssertRejected(() => _client.Kick(11), "只有队长可以请离队员。");

            // 未进游戏
            _net.PlayerId = 0;
            AssertRejected(() => _client.Create(), "请进入角色后再使用组队。");
        }

        [Test]
        public void ResetWhileBusy_DropsLateReplyAndRequiresReconnect()
        {
            Loaded();
            Assert.That(_client.Refresh(), Is.True);
            _client.Reset();

            Assert.That(_client.RequiresReconnect, Is.True);
            Assert.That(_client.Busy, Is.False);
            Assert.That(_client.Status, Is.EqualTo(TeamClient.RecoveryMessage));

            _net.Reply(new TeamResponse { Team = LeaderView(2) });
            Assert.That(_client.Snapshot.TeamId, Is.Zero);
            Assert.That(_client.HasLoaded, Is.False);

            int calls = _net.Calls.Count;
            _now += 5f;
            _client.DrainQueued(true);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));
        }

        [Test]
        public void SynchronousTransportError_StillReturnsTrueAndClearsBusy()
        {
            Loaded();
            _net.SyncError = "协程宿主未就绪";

            Assert.That(_client.Refresh(), Is.True);
            Assert.That(_client.Busy, Is.False);
            Assert.That(_client.PendingAction, Is.EqualTo(TeamAction.None));
            Assert.That(_client.RequiresReconnect, Is.True);
            Assert.That(_client.Status, Is.EqualTo(TeamClient.RecoveryMessage));
        }

        [Test]
        public void PanelRefresh_Every30SecondsOnlyWhenVisible()
        {
            Loaded();
            int calls = _net.Calls.Count;

            _now += 31f;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));

            _client.DrainQueued(true);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls + 1));
            Assert.That(LastId, Is.EqualTo(MessageIds.GetMyTeam));
            Assert.That(LastRequest<GetMyTeamRequest>().NotifyOnline, Is.False);
            string status = _client.Status;
            _net.Reply(new TeamResponse { Team = LeaderView(2) });
            Assert.That(_client.Status, Is.EqualTo(status), "静默刷新不改状态栏");

            _now += 1f;
            _client.DrainQueued(true);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls + 1));
        }

        [Test]
        public void MatchFailedPush_ShowsTipAndHighlights()
        {
            Loaded();
            _push(new TeamSnapshotS2C
            {
                Team = LeaderView(2), Reason = TeamChangeReason.MatchFailed,
                Tip = Tip(team_error.KTeamMemberOffline, "11")
            });
            Assert.That(_client.Status, Is.EqualTo("有队员不在线。"));
            Assert.That(_client.HighlightPlayerId, Is.EqualTo(11UL));

            _push(new TeamSnapshotS2C { Team = LeaderView(3), Reason = TeamChangeReason.MatchFailed });
            Assert.That(_client.Status, Is.EqualTo("队伍开战失败，请稍后再试。"));
            Assert.That(_client.HighlightPlayerId, Is.Zero);

            _push(new TeamSnapshotS2C { Team = LeaderView(4), Reason = TeamChangeReason.MatchStarted });
            Assert.That(_client.Status, Is.EqualTo("队长已发起战斗，正在集合…"));
            _push(new TeamSnapshotS2C { Team = LeaderView(5), Reason = TeamChangeReason.MatchEnded });
            Assert.That(_client.Status, Is.Empty);
        }

        [Test]
        public void DescribeTip_CoversTableAndUnknown()
        {
            var codes = new[]
            {
                team_error.KTeamPlayerId, team_error.KTeamMembersFull, team_error.KTeamMemberInTeam,
                team_error.KTeamMemberNotInTeam, team_error.KTeamKickSelf, team_error.KTeamKickNotLeader,
                team_error.KTeamAppointSelf, team_error.KTeamAppointLeaderNotLeader, team_error.KTeamNotInApplicantList,
                team_error.KTeamHasNotTeamId, team_error.KTeamDismissNotLeader, team_error.KTeamPlayerNotFound,
                team_error.KTeamNotLeader, team_error.KTeamHomeZoneUnknown, team_error.KTeamCrossZoneDenied,
                team_error.KTeamInviteNotFound, team_error.KTeamInviteLimit, team_error.KTeamInMatch,
                team_error.KTeamMemberOffline, team_error.KTeamMemberInBattle, team_error.KTeamMemberNotReady,
                team_error.KTeamDungeonNotOpen, team_error.KTeamSizeExceeded, team_error.KTeamStateChanged,
                team_error.KTeamInternal
            };
            string unknown = TeamClient.DescribeTip(new TipInfoMessage { Id = 9999 });
            Assert.That(unknown, Does.Contain("9999"));
            Assert.That(TeamClient.DescribeTip(new TipInfoMessage { Id = 4000 }), Does.Contain("4000"));

            var seen = new HashSet<string>();
            foreach (var code in codes)
            {
                string text = TeamClient.DescribeTip(new TipInfoMessage { Id = (uint)code });
                Assert.That(text, Is.Not.Empty, code.ToString());
                Assert.That(text, Is.Not.EqualTo(unknown), code.ToString());
                Assert.That(text, Does.Not.Contain("暂未完成请求"), code.ToString());
                Assert.That(seen.Add(text), Is.True, "文案重复:" + text);
            }

            Assert.That(TeamClient.DescribeTip(null), Is.EqualTo(""));
            Assert.That(TeamClient.DescribeTip(new TipInfoMessage { Id = 0 }), Is.EqualTo(""));
        }

        // ── 助手 ────────────────────────────────────────────────────────────

        /// <summary>探针 → 回复视图 → 邀请列表 → 回复空列表;结束时距上次发送已超过发送间隔。</summary>
        private void Loaded(TeamView view = null)
        {
            _now += 0.5f;
            _client.DrainQueued(false);
            Assert.That(LastId, Is.EqualTo(MessageIds.GetMyTeam), "第一个请求必须是探针");
            _net.Reply(new TeamResponse { Team = view ?? LeaderView(1) });
            _now += 0.5f;
            _client.DrainQueued(false);
            Assert.That(LastId, Is.EqualTo(MessageIds.ListMyInvites));
            _net.Reply(new ListMyInvitesResponse());
            _now += 0.5f;
            Assert.That(_client.HasLoaded && !_client.Busy, Is.True);
        }

        private void LoadedNoTeam() => Loaded(new TeamView { MembershipEpoch = 5, ServerTimeMs = 1000 });

        /// <summary>回复当前在途请求(同一队伍、version 递增),再推进时钟越过发送间隔。</summary>
        private void Next(ref ulong version)
        {
            _net.Reply(new TeamResponse { Team = LeaderView(++version) });
            _now += 0.5f;
        }

        private void AssertRejected(Func<bool> action, string expected)
        {
            int calls = _net.Calls.Count;
            Assert.That(action(), Is.False, expected);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls), expected);
            Assert.That(_client.Status, Is.EqualTo(expected));
        }

        private uint LastId => _net.Calls[_net.Calls.Count - 1].Id;

        private T LastRequest<T>() where T : class, IMessage => (T)_net.Calls[_net.Calls.Count - 1].Request;

        /// <summary>队长视图:队伍 100、队长 10、成员 10/11/12、epoch 5。</summary>
        private static TeamView LeaderView(ulong version) => View(100, 10, 5, version, 10, 11, 12);

        private static TeamView View(ulong teamId, ulong leaderId, ulong epoch, ulong version, params ulong[] members)
        {
            var view = new TeamView
            {
                TeamId = teamId, LeaderId = leaderId, Capacity = 5, ZoneId = 1,
                MembershipEpoch = epoch, Version = version, ServerTimeMs = 1000
            };
            uint seq = 0;
            foreach (var id in members)
                view.Members.Add(new TeamMemberView
                    { PlayerId = id, ClassId = 1, Gender = 1, IsOnline = true, IsLeader = id == leaderId, JoinSeq = ++seq });
            return view;
        }

        private static TeamResponse Response(TeamView view, team_error tip, params string[] parameters) =>
            new TeamResponse { Team = view, ErrorMessage = Tip(tip, parameters) };

        private static TipInfoMessage Tip(team_error code, params string[] parameters)
        {
            var tip = new TipInfoMessage { Id = (uint)code };
            tip.Parameters.AddRange(parameters);
            return tip;
        }

        private static TeamIncomingInviteView IncomingInvite(ulong teamId, ulong leaderId, ulong expireAtMs = 0) =>
            new TeamIncomingInviteView
            {
                TeamId = teamId, LeaderId = leaderId, MemberCount = 2, ZoneId = 1, ExpireAtMs = expireAtMs,
                Inviter = new TeamMemberView { PlayerId = leaderId, ClassId = 1, Gender = 1, IsOnline = true }
            };

        private static TeamInviteS2C InvitePush(ulong teamId, ulong leaderId) =>
            new TeamInviteS2C { Invite = IncomingInvite(teamId, leaderId) };
    }

    /// <summary>
    /// 组队用的假传输,照 GuildFakeTransport 扩展:记录请求体、可同步失败、可推原始字节。
    /// 回复先序列化再用 parser 解析,与 GameClient.Call 的线上路径一致。
    /// </summary>
    internal sealed class TeamFakeTransport : IBattleTransport
    {
        public ulong PlayerId { get; set; } = 10;
        public bool IsReady { get; set; } = true;
        public event Action Disconnected;
        public readonly List<(uint Id, IMessage Request)> Calls = new List<(uint Id, IMessage Request)>();
        public Action<IMessage> Pending;
        public Action<string> Error;
        /// <summary>非 null 时 Call 内同步调用 error(模拟协程宿主未就绪)。</summary>
        public string SyncError;
        /// <summary>为 true 时照 GameClient.Call:回包处理抛异常就转成 onError("parse response: …")。</summary>
        public bool WrapLikeGameClient;
        /// <summary>已注册的 S2C 推送处理器；与 GameClient.OnNotify 一样，一个 id 只留最后一次注册。</summary>
        public readonly Dictionary<uint, Action<MessageContent>> Notifies = new Dictionary<uint, Action<MessageContent>>();
        /// <summary>连接身份;换成新对象即模拟 GameClient 静默换 Gate。</summary>
        public object Identity = new object();

        public void Reply(IMessage response) => Pending(response);
        public void Fail(string error) => Error(error);
        public void Disconnect() { IsReady = false; Disconnected?.Invoke(); }
        /// <summary>模拟服务端推送；未注册的 id 会直接抛，测试里那就是接线漏了。</summary>
        public void Push(uint id, IMessage message) => PushRaw(id, message.ToByteArray());
        public void PushRaw(uint id, byte[] bytes) =>
            Notifies[id](new MessageContent { MessageId = id, SerializedMessage = ByteString.CopyFrom(bytes) });
        public void RegisterNotify(uint id, Action<MessageContent> handler) => Notifies[id] = handler;
        public void SendOneWay(uint id, IMessage request) { }

        public void Call<T>(uint id, IMessage request, MessageParser<T> parser, Action<T> onResponse, Action<string> onError)
            where T : IMessage<T>
        {
            Calls.Add((id, request));
            Pending = message =>
            {
                T parsed = parser.ParseFrom(((T)message).ToByteString());
                if (!WrapLikeGameClient) { onResponse(parsed); return; }
                try { onResponse(parsed); }
                catch (Exception ex) { onError($"parse response: {ex.Message}"); }
            };
            Error = onError;
            if (SyncError != null) onError(SyncError);
        }
    }
}
