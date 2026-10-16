using System;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Team;
using MmorpgClient.Net;
using NUnit.Framework;
using Teampb;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>
    /// 队伍补位匹配:队伍视图的匹配态与 BattleClient 排队态的同步规则(TeamBackfillQueueSync)。
    /// 两个客户端各用一个假传输层;每个用例里手动调 Step,相当于界面的一帧。
    /// 服务端设计:docs/design/team-backfill-matching.md §5。
    /// </summary>
    public sealed class TeamBackfillQueueSyncTests
    {
        private const ulong Me = 10;
        private const ulong QueuedAt = 5000;

        private float _now;
        private double _battleNow;
        private TeamFakeTransport _teamNet;
        private TeamFakeTransport _battleNet;
        private TeamClient _team;
        private BattleClient _battle;
        private TeamBackfillQueueSync _sync;
        private Action<TeamSnapshotS2C> _push;

        [SetUp]
        public void SetUp()
        {
            _now = 0f;
            _battleNow = 0;
            _push = null;
            _teamNet = new TeamFakeTransport { PlayerId = Me };
            _battleNet = new TeamFakeTransport { PlayerId = Me };
            _team = new TeamClient(_teamNet, () => _teamNet.Identity,
                handler => _push = handler,
                handler => { if (_push == handler) _push = null; },
                () => _now);
            _battle = new BattleClient(_battleNet);
            _battle.Tick(_battleNow);
            _sync = new TeamBackfillQueueSync();
        }

        [TearDown]
        public void TearDown() => _team.Dispose();

        [Test]
        public void QueuedView_AdoptsQueueOnBattleClient()
        {
            LoadTeam(View(1, queued: true, target: 4));
            _sync.Step(_team, _battle);

            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.Queued));
            Assert.That(_battle.QueueAdopted, Is.True);
            Assert.That(_battle.AdoptedQueueHint, Does.Contain("目标 4 人"));
            Assert.That(_battleNet.Calls, Is.Empty, "不发 JoinQueue");

            // 再走几帧:不重复采纳,也不打扰服务端。
            _sync.Step(_team, _battle);
            _sync.Step(_team, _battle);
            Assert.That(_battleNet.Calls, Is.Empty);
        }

        [Test]
        public void IdleView_DoesNothing()
        {
            LoadTeam(View(1, queued: false));
            _sync.Step(_team, _battle);
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.None));
        }

        [Test]
        public void OwnSoloQueue_IsLeftAlone()
        {
            LoadTeam(View(1, queued: false));
            _battle.JoinQueue(Match.MatchMode._1V1, 0);
            _sync.Step(_team, _battle);
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.Queued));
            Assert.That(_battle.QueueAdopted, Is.False);

            // 自己单排期间队伍视图变成「在排」:相位不是空闲,不采纳。
            _push(new TeamSnapshotS2C { Team = View(1, queued: true, target: 3), Reason = TeamChangeReason.MatchQueued });
            _sync.Step(_team, _battle);
            Assert.That(_battle.QueueAdopted, Is.False);
        }

        [Test]
        public void PollSaysNotQueued_EndsThisQueueAndNeverReadoptsIt()
        {
            LoadTeam(View(1, queued: true, target: 4));
            _sync.Step(_team, _battle);
            PollReplies(Match.QueueState.NotQueued);
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.None));

            _sync.Step(_team, _battle);
            Assert.That(_team.MatchQueued, Is.False, "票据说不在排:本地队伍快照的匹配态当场抹掉,不等服务端视图");
            Assert.That(_team.RefreshQueued, Is.True, "顺手排一次静默刷新,但不依赖它");
            _sync.Step(_team, _battle);
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.None), "同一次排队不再采纳");

            // 同一次排队的旧视图迟到(同版本、仍标着在排):不显示、不采纳。
            _push(new TeamSnapshotS2C { Team = View(1, queued: true, target: 4), Reason = TeamChangeReason.MemberOnline });
            Assert.That(_team.MatchQueued, Is.False);
            _sync.Step(_team, _battle);
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.None));

            // 队长重新发起:入队时刻不同,是新的一次排队。
            _push(new TeamSnapshotS2C
            {
                Team = View(1, queued: true, target: 5, queuedAt: QueuedAt + 60000), Reason = TeamChangeReason.MatchQueued
            });
            Assert.That(_team.MatchQueued, Is.True);
            _sync.Step(_team, _battle);
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.Queued));
            Assert.That(_battle.AdoptedQueueHint, Does.Contain("目标 5 人"));
        }

        [Test]
        public void CancelSuccess_EndsThisQueue()
        {
            LoadTeam(View(1, queued: true, target: 4));
            _sync.Step(_team, _battle);
            _battle.CancelQueue();
            _battleNet.Reply(new Empty());
            // 取消的回包分不出「已撤 / 太迟」:先求证,服务端说已不在排才算结束。
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.Queued));
            _sync.Step(_team, _battle);
            Assert.That(_team.MatchQueued, Is.True, "还没确认之前不记「已结束」");
            _battleNow += 0.1;
            _battle.Tick(_battleNow);
            _battleNet.Reply(new Match.GetQueueStatusResponse { State = Match.QueueState.NotQueued });
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.None));

            // 「匹配已取消」的推送还没到:手里的队伍视图仍是在排,不能照着它再进一次。
            _sync.Step(_team, _battle);
            Assert.That(_team.MatchQueued, Is.False);
            _sync.Step(_team, _battle);
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.None));
        }

        [Test]
        public void CancelTooLateThenRequeued_PlayerStillSeesTheQueue()
        {
            LoadTeam(View(1, queued: true, target: 4));
            _sync.Step(_team, _battle);

            // 队伍刚凑满弹出,我点了取消:服务端按「太迟」忽略,回包照样是 Empty。
            _battle.CancelQueue();
            _battleNet.Reply(new Empty());
            _battleNow += 0.1;
            _battle.Tick(_battleNow);
            _battleNet.Reply(new Match.GetQueueStatusResponse { State = Match.QueueState.Matched });
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.Preparing));
            _sync.Step(_team, _battle);
            Assert.That(_team.MatchQueued, Is.True, "这一次排队没有结束");

            // 开局因别人失败,整队回到队列:准备超时 → 回排队态 → 服务端确认仍在排。
            _battleNow += BattleClient.PreparingTimeoutSeconds;
            _battle.Tick(_battleNow);
            _battleNow += 0.1;
            _battle.Tick(_battleNow);
            _battleNet.Reply(new Match.GetQueueStatusResponse { State = Match.QueueState.Queued });
            _sync.Step(_team, _battle);

            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.Queued), "仍然看得到自己在排,取消按钮可用");
            Assert.That(_battle.QueueAdopted, Is.True);
            Assert.That(_team.MatchQueued, Is.True);
        }

        [Test]
        public void BattleStart_EndsThisQueueSoTheStaleViewIsNotReadoptedAfterTheFight()
        {
            LoadTeam(View(1, queued: true, target: 4));
            _sync.Step(_team, _battle);

            // 凑满开战:服务端不会给队伍补推视图,快照停在「在排」。
            var state = new BattleStateS2C { BattleId = 77, RoundIndex = 1, Outcome = eBattleOutcome.BattleOutcomeOngoing };
            state.PendingActorIds.Add(Me);
            _battleNet.Push(MessageIds.NotifyBattleStart, new BattleStartS2C { BattleId = 77, State = state });
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.WaitingAction));
            Assert.That(_battle.QueueAdopted, Is.False);

            _sync.Step(_team, _battle);
            Assert.That(_team.MatchQueued, Is.False, "开战即结束这一次排队:战后面板不再显示匹配中");
            Assert.That(_team.Snapshot.TeamId, Is.EqualTo(100UL), "只抹匹配态,队伍本身不动");
        }

        [Test]
        public void StartMatchRejectedAsAlreadyQueued_ReadoptsAQueueWronglyMarkedEnded()
        {
            LoadTeam(View(1, queued: true, target: 4));
            _sync.Step(_team, _battle);
            // 排队中开了一场别的战斗(切磋):本机把这一次排队记为结束。服务端此时本应同时撤掉排队,
            // 这里模拟它没撤成 —— 票据还在排。
            var state = new BattleStateS2C { BattleId = 78, RoundIndex = 1, Outcome = eBattleOutcome.BattleOutcomeOngoing };
            state.PendingActorIds.Add(Me);
            _battleNet.Push(MessageIds.NotifyBattleStart, new BattleStartS2C { BattleId = 78, State = state });
            _sync.Step(_team, _battle);
            Assert.That(_team.MatchQueued, Is.False);
            _battleNet.Push(MessageIds.NotifyBattleEnd, new BattleEndS2C { BattleId = 78 });
            _battle.AckBattleEnd();
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.None));
            _sync.Step(_team, _battle);
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.None), "同一次排队的旧视图不采纳");

            // 队长(我)再点开战:服务端回「队伍正在匹配中」+ QUEUED 视图 → 本机恢复匹配态并重新采纳。
            Assert.That(_team.StartMatch(1, 4), Is.True);
            _teamNet.Reply(new TeamResponse
            {
                Team = View(1, queued: true, target: 4),
                ErrorMessage = new TipInfoMessage { Id = (uint)team_error.KTeamInMatch }
            });
            Assert.That(_team.MatchQueued, Is.True);
            _sync.Step(_team, _battle);
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.Queued), "排队面板与取消入口回来了");
            Assert.That(_battle.QueueAdopted, Is.True);
        }

        [Test]
        public void MemberWronglyMarkedEnded_IsRevivedByTheIdleProbe()
        {
            // 本机是队员(队长是 11):没有「再点一次开战」这条兜底,只能靠回到空闲后的探测。
            LoadTeam(View(1, queued: true, target: 4, leader: 11));
            _sync.Step(_team, _battle);
            Assert.That(_battle.QueueAdopted, Is.True);
            FightAndFinish(80);
            Assert.That(_team.MatchQueued, Is.False, "开战即把这一次排队记为结束");

            _sync.Step(_team, _battle);
            Assert.That(_battleNet.Calls[_battleNet.Calls.Count - 1].Id, Is.EqualTo(MessageIds.GetQueueStatus), "空闲后探测一次");
            _battleNet.Reply(new Match.GetQueueStatusResponse { State = Match.QueueState.Queued });

            // 服务端说仍在排:撤销「已结束」并重拉,在排的视图回来后重新采纳。
            _now += 0.5f;
            _team.DrainQueued(false);
            _teamNet.Reply(new TeamResponse { Team = View(1, queued: true, target: 4, leader: 11) });
            Assert.That(_team.MatchQueued, Is.True);
            _sync.Step(_team, _battle);
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.Queued));
            Assert.That(_battle.QueueAdopted, Is.True);

            // 探测只做一次。
            int calls = _battleNet.Calls.Count;
            _sync.Step(_team, _battle);
            Assert.That(_battleNet.Calls.Count, Is.EqualTo(calls));
        }

        [Test]
        public void FinishedPartyBattle_ProbeSaysReady_StaysEnded()
        {
            LoadTeam(View(1, queued: true, target: 4, leader: 11));
            _sync.Step(_team, _battle);
            FightAndFinish(81);
            _sync.Step(_team, _battle);
            // 正常凑满开打:票据是 ready(最长留 60 秒),不能当成还在排。
            _battleNet.Reply(new Match.GetQueueStatusResponse { State = Match.QueueState.Ready });

            _now += 0.5f;
            _team.DrainQueued(false);
            _teamNet.Reply(new TeamResponse { Team = View(1, queued: true, target: 4, leader: 11) });
            Assert.That(_team.MatchQueued, Is.False, "同一次排队的旧视图仍被抹掉");
            _sync.Step(_team, _battle);
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.None));
        }

        [Test]
        public void MatchedThenPreparing_IsStillTheSameQueue()
        {
            LoadTeam(View(1, queued: true, target: 4));
            _sync.Step(_team, _battle);
            PollReplies(Match.QueueState.Matched);
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.Preparing));

            // 准备阶段不算结束:开局失败回队首时还要回到排队态。
            _sync.Step(_team, _battle);
            Assert.That(_team.MatchQueued, Is.True);

            // 准备超时 → 回排队态并求证;服务端说仍在排。
            _battleNow += BattleClient.PreparingTimeoutSeconds;
            _battle.Tick(_battleNow);
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.Queued));
            _sync.Step(_team, _battle);
            Assert.That(_team.MatchQueued, Is.True);
            Assert.That(_battle.QueueAdopted, Is.True);
        }

        [Test]
        public void IdleViewWhileAdopted_AsksServerInsteadOfDroppingTheQueue()
        {
            LoadTeam(View(1, queued: true, target: 4));
            _sync.Step(_team, _battle);

            // 一份「没在排」的同版本视图到达(迟到的回包,或服务端读票失败时如实下发的视图)。
            _push(new TeamSnapshotS2C { Team = View(1, queued: false), Reason = TeamChangeReason.MemberOnline });
            _sync.Step(_team, _battle);
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.Queued), "不直接退回空闲");
            Assert.That(_battle.QueueAdopted, Is.True);
            Assert.That(_team.RefreshQueued, Is.True, "顺手补拉一次队伍视图");

            // 立刻向服务端求证(不等 3 秒的轮询周期)。
            _battleNow += 0.1;
            _battle.Tick(_battleNow);
            Assert.That(_battleNet.Calls.Count, Is.EqualTo(1));
            Assert.That(_battleNet.Calls[0].Id, Is.EqualTo(MessageIds.GetQueueStatus));
            _battleNet.Reply(new Match.GetQueueStatusResponse { State = Match.QueueState.Queued });
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.Queued), "服务端说仍在排:继续等");

            // 同一份视图只求证一次。
            _sync.Step(_team, _battle);
            _battleNow += 0.1;
            _battle.Tick(_battleNow);
            Assert.That(_battleNet.Calls.Count, Is.EqualTo(1));

            // 补拉回来的视图还是「没在排」(服务端仍读不到票据):再求证一次,但不再补拉 —— 否则就是拉取循环。
            _now += 0.5f;
            _team.DrainQueued(false);
            _teamNet.Reply(new TeamResponse { Team = View(1, queued: false) });
            Assert.That(_team.RefreshQueued, Is.False);
            _sync.Step(_team, _battle);
            Assert.That(_team.RefreshQueued, Is.False, "每次采纳至多补拉一次");
            _battleNow += 0.1;
            _battle.Tick(_battleNow);
            Assert.That(_battleNet.Calls.Count, Is.EqualTo(2));
        }

        [Test]
        public void CanceledPush_ConvergesThroughThePoll()
        {
            LoadTeam(View(1, queued: true, target: 4));
            _sync.Step(_team, _battle);

            _push(new TeamSnapshotS2C { Team = View(1, queued: false), Reason = TeamChangeReason.MatchQueueCanceled, ActorId = 11 });
            _sync.Step(_team, _battle);
            _battleNow += 0.1;
            _battle.Tick(_battleNow);
            _battleNet.Reply(new Match.GetQueueStatusResponse { State = Match.QueueState.NotQueued });

            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.None));
            _sync.Step(_team, _battle);
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.None));
            Assert.That(_team.MatchQueued, Is.False);
        }

        [Test]
        public void SessionReset_TrustsServerViewAgain()
        {
            LoadTeam(View(1, queued: true, target: 4));
            _sync.Step(_team, _battle);
            PollReplies(Match.QueueState.NotQueued);
            _sync.Step(_team, _battle);
            Assert.That(_team.MatchQueued, Is.False);

            // 换连接 / 重登:新会话里服务端仍说在排(同一次排队),要能恢复。
            _team.Reset();
            _sync.Reset();
            LoadTeam(View(1, queued: true, target: 4));
            Assert.That(_team.MatchQueued, Is.True);
            _sync.Step(_team, _battle);
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.Queued));
        }

        [Test]
        public void MissingClients_AreTolerated()
        {
            Assert.DoesNotThrow(() => _sync.Step(null, _battle));
            Assert.DoesNotThrow(() => _sync.Step(_team, null));
            Assert.That(TeamBackfillQueueSync.QueueHint(3), Does.Contain("目标 3 人"));
        }

        // ── 辅助 ────────────────────────────────────────────────────────────

        /// <summary>队伍 100、队长 10、成员 10/11、epoch 5;queued 时带目标人数与入队时刻。</summary>
        private static TeamView View(ulong version, bool queued, uint target = 0, ulong queuedAt = QueuedAt, ulong leader = Me)
        {
            var view = new TeamView
            {
                TeamId = 100, LeaderId = leader, Capacity = 5, ZoneId = 1,
                MembershipEpoch = 5, Version = version, ServerTimeMs = 9000
            };
            view.Members.Add(new TeamMemberView { PlayerId = Me, IsOnline = true, IsLeader = leader == Me, JoinSeq = 1 });
            view.Members.Add(new TeamMemberView { PlayerId = 11, IsOnline = true, IsLeader = leader == 11, JoinSeq = 2 });
            if (queued)
            {
                view.MatchState = TeamMatchState.Queued;
                view.MatchTargetSize = target;
                view.MatchQueuedAtMs = queuedAt;
                view.MatchBattleConfigId = 1;
            }
            return view;
        }

        /// <summary>探针 → 回复视图 → 邀请列表 → 回复空列表(同 TeamClientTests.Loaded)。</summary>
        private void LoadTeam(TeamView view)
        {
            _now += 0.5f;
            _team.DrainQueued(false);
            _teamNet.Reply(new TeamResponse { Team = view });
            _now += 0.5f;
            _team.DrainQueued(false);
            _teamNet.Reply(new ListMyInvitesResponse());
            _now += 0.5f;
            Assert.That(_team.HasLoaded && _team.HasView && !_team.Busy, Is.True);
        }

        /// <summary>开一场战斗(被带入的排队随之结束,下一帧规则 2 记为已结束)并打完回到空闲。</summary>
        private void FightAndFinish(ulong battleId)
        {
            var state = new BattleStateS2C { BattleId = battleId, RoundIndex = 1, Outcome = eBattleOutcome.BattleOutcomeOngoing };
            state.PendingActorIds.Add(Me);
            _battleNet.Push(MessageIds.NotifyBattleStart, new BattleStartS2C { BattleId = battleId, State = state });
            Assert.That(_battle.QueueAdopted, Is.False);
            _sync.Step(_team, _battle);
            _battleNet.Push(MessageIds.NotifyBattleEnd, new BattleEndS2C { BattleId = battleId });
            _battle.AckBattleEnd();
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.None));
        }

        /// <summary>推进到下一次排队轮询并让服务端回复 state。</summary>
        private void PollReplies(Match.QueueState state)
        {
            int before = _battleNet.Calls.Count;
            _battleNow += BattleClient.QueuePollIntervalSeconds;
            _battle.Tick(_battleNow);
            Assert.That(_battleNet.Calls.Count, Is.EqualTo(before + 1), "应发出一次 GetQueueStatus");
            Assert.That(_battleNet.Calls[_battleNet.Calls.Count - 1].Id, Is.EqualTo(MessageIds.GetQueueStatus));
            _battleNet.Reply(new Match.GetQueueStatusResponse { State = state });
        }
    }
}
