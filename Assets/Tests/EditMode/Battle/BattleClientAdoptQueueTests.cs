using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 队伍补位匹配:BattleClient 的「被带入的排队态」(AdoptQueue / PollQueueNow)。
    /// 服务端已经把玩家随队伍放进人数档队列,本机没有发过 JoinQueue;进入 Queued 之后的轮询、取消、
    /// 凑满开战都沿用单人排队的既有实现。服务端设计:docs/design/team-backfill-matching.md §5。
    /// </summary>
    public sealed class BattleClientAdoptQueueTests
    {
        private const ulong MyId = 1001;

        private FakeBattleTransport _net;
        private BattleClient _client;
        private List<BattlePhase> _phases;
        private List<string> _errors;

        [SetUp]
        public void SetUp()
        {
            _net = new FakeBattleTransport { PlayerId = MyId };
            _client = new BattleClient(_net, null); // 直接 new,不经 Attach,避免污染单例
            _phases = new List<BattlePhase>();
            _errors = new List<string>();
            _client.OnPhaseChanged += p => _phases.Add(p);
            _client.OnError += e => _errors.Add(e);
            _client.Tick(0);
        }

        [Test]
        public void AdoptQueue_EntersQueuedWithoutSendingJoinQueue()
        {
            bool adoptedAtEvent = false;
            string hintAtEvent = null;
            _client.OnPhaseChanged += p =>
            {
                if (p != BattlePhase.Queued) return;
                adoptedAtEvent = _client.QueueAdopted;
                hintAtEvent = _client.AdoptedQueueHint;
            };

            Assert.That(_client.AdoptQueue("队伍匹配中"), Is.True);

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued));
            Assert.That(_client.QueueAdopted, Is.True);
            Assert.That(_client.AdoptedQueueHint, Is.EqualTo("队伍匹配中"));
            Assert.That(adoptedAtEvent, Is.True, "相位事件触发时标记必须已经置位(排队面板在事件里读它)");
            Assert.That(hintAtEvent, Is.EqualTo("队伍匹配中"));
            Assert.That(_net.Calls, Is.Empty, "被带入排队不发任何请求");
            Assert.That(_errors, Is.Empty);

            // 之后与自己排队一样每 3 秒轮询。
            _client.Tick(2.9);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Is.Empty);
            _client.Tick(3.0);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Has.Count.EqualTo(1));
        }

        [Test]
        public void AdoptQueue_OnlyFromIdle()
        {
            _client.JoinQueue(Match.MatchMode._1V1, 0);
            Assert.That(_client.AdoptQueue("x"), Is.False, "自己已经在排队:不覆盖");
            Assert.That(_client.QueueAdopted, Is.False);
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued));

        }

        [Test]
        public void JoinQueue_RejectedWhileAdopted()
        {
            _client.AdoptQueue("x");
            _client.JoinQueue(Match.MatchMode.PveSolo, 1);
            Assert.That(_net.CallsOf(MessageIds.JoinQueue), Is.Empty);
            Assert.That(_errors, Has.Count.EqualTo(1));
            Assert.That(_client.QueueAdopted, Is.True);
        }

        [Test]
        public void PollQueueNow_AsksServerOnNextTickInsteadOfChangingPhase()
        {
            _client.AdoptQueue("x");
            _client.Tick(1.0);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Is.Empty);

            _client.PollQueueNow();
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued), "只是提前求证,不改相位");
            _client.Tick(1.1);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Has.Count.EqualTo(1));

            // 服务端说仍在排:什么都不变。
            _net.CallsOf(MessageIds.GetQueueStatus)[0]
                .Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.Queued });
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued));
            Assert.That(_client.QueueAdopted, Is.True);

            // 空闲时调用是空操作。
            _net.RaiseDisconnected();
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));
            _net.IsReady = true;
            _client.PollQueueNow();
            _client.Tick(10);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Has.Count.EqualTo(1), "离开排队后不再轮询");
        }

        [Test]
        public void Poll_NotQueuedReturnsToIdleAndClearsFlag()
        {
            _client.AdoptQueue("x");
            _client.Tick(3.0);
            _net.CallsOf(MessageIds.GetQueueStatus)[0]
                .Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.NotQueued });

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));
            Assert.That(_client.QueueAdopted, Is.False);
        }

        [Test]
        public void Poll_MatchedKeepsFlagThroughPreparing()
        {
            _client.AdoptQueue("hint");
            _client.Tick(3.0);
            _net.CallsOf(MessageIds.GetQueueStatus)[0]
                .Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.Matched });

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Preparing));
            Assert.That(_client.QueueAdopted, Is.True, "准备阶段可能因开局失败退回排队,标记要留着");
            Assert.That(_client.AdoptedQueueHint, Is.EqualTo("hint"));
        }

        [Test]
        public void PreparingTimeout_ReturnsToQueueAndPollsInsteadOfQuitting()
        {
            _client.AdoptQueue("hint");
            _client.Tick(3.0);
            _net.CallsOf(MessageIds.GetQueueStatus)[0]
                .Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.Matched });
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Preparing));

            // 凑满后开局失败:服务端把整队放回队首,本机没有任何推送。准备超时后回到排队态并立刻求证。
            _client.Tick(3.0 + BattleClient.PreparingTimeoutSeconds);
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued));
            Assert.That(_client.QueueAdopted, Is.True);
            Assert.That(_client.AdoptedQueueHint, Is.EqualTo("hint"));
            Assert.That(_errors, Is.Empty, "不报「已退出匹配」");

            _client.Tick(3.1 + BattleClient.PreparingTimeoutSeconds);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Has.Count.EqualTo(2), "回到排队态后立刻问一次");
            _net.CallsOf(MessageIds.GetQueueStatus)[1]
                .Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.Queued });
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued), "服务端确认仍在排:继续等");
        }

        [Test]
        public void OwnQueuePreparingTimeout_StillQuitsWithError()
        {
            _client.JoinQueue(Match.MatchMode._1V1, 0);
            _client.Tick(3.0);
            _net.CallsOf(MessageIds.GetQueueStatus)[0]
                .Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.Matched });
            _client.Tick(3.0 + BattleClient.PreparingTimeoutSeconds);

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None), "自己排的队:行为不变");
            Assert.That(_errors, Has.Count.EqualTo(1));
        }

        [Test]
        public void CancelQueue_SendsEmptyTicketAndConverges()
        {
            _client.AdoptQueue("x");
            _client.CancelQueue();

            var calls = _net.CallsOf(MessageIds.CancelQueue);
            Assert.That(calls, Has.Count.EqualTo(1));
            var request = (Match.CancelQueueRequest)calls[0].Request;
            Assert.That(request.PlayerId, Is.EqualTo(MyId));
            Assert.That(request.QueueTicket, Is.Empty, "没有本人票据:服务端按身份撤掉整队的排队");

            // 回包是 Empty,分不出「整队已退出」还是「太迟」:不直接回空闲,立刻向服务端求证。
            calls[0].Respond(new Empty());
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued));
            _client.Tick(0.1);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Has.Count.EqualTo(1), "不等 3 秒的轮询周期");
            _net.CallsOf(MessageIds.GetQueueStatus)[0]
                .Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.NotQueued });

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));
            Assert.That(_client.QueueAdopted, Is.False);
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void CancelQueue_TooLateKeepsQueueStateAndTellsThePlayer()
        {
            _client.AdoptQueue("x");
            _client.CancelQueue();
            _net.CallsOf(MessageIds.CancelQueue)[0].Respond(new Empty());
            _client.Tick(0.1);

            // 服务端:队伍已凑满弹出,取消被忽略(太迟)。
            _net.CallsOf(MessageIds.GetQueueStatus)[0]
                .Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.Matched });
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Preparing));
            Assert.That(_client.QueueAdopted, Is.True);
            Assert.That(_errors, Has.Count.EqualTo(1));
            Assert.That(_errors[0], Does.Contain("取消未生效"));

            // 随后开局失败、整队回到队列:准备超时后回排队态,仍能看到自己在排、仍能再取消。
            _client.Tick(0.1 + BattleClient.PreparingTimeoutSeconds);
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued));
            _client.Tick(0.2 + BattleClient.PreparingTimeoutSeconds);
            _net.CallsOf(MessageIds.GetQueueStatus)[1]
                .Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.Queued });
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued));
            Assert.That(_errors, Has.Count.EqualTo(1), "之后的普通轮询不再重复提示");
        }

        [Test]
        public void OwnQueueCancel_StillReturnsToIdleImmediately()
        {
            _client.JoinQueue(Match.MatchMode._1V1, 0);
            _client.CancelQueue();
            _net.CallsOf(MessageIds.CancelQueue)[0].Respond(new Empty());
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None), "自己排的队:行为不变");
        }

        [Test]
        public void AdoptQueue_AfterOwnQueueDoesNotReuseOldTicket()
        {
            _client.JoinQueue(Match.MatchMode._1V1, 0);
            _net.CallsOf(MessageIds.JoinQueue)[0].Respond(new Match.JoinQueueResponse { QueueTicket = "own-ticket" });
            _client.CancelQueue();
            Assert.That(((Match.CancelQueueRequest)_net.CallsOf(MessageIds.CancelQueue)[0].Request).QueueTicket,
                Is.EqualTo("own-ticket"));
            _net.CallsOf(MessageIds.CancelQueue)[0].Respond(new Empty());
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));

            _client.AdoptQueue("x");
            _client.CancelQueue();
            var second = (Match.CancelQueueRequest)_net.CallsOf(MessageIds.CancelQueue)[1].Request;
            Assert.That(second.QueueTicket, Is.Empty, "旧票据不能带进被带入的排队");
        }

        [Test]
        public void Disconnect_ClearsAdoptedQueue()
        {
            _client.AdoptQueue("x");
            _net.RaiseDisconnected();
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));
            Assert.That(_client.QueueAdopted, Is.False);
        }

        [Test]
        public void QueuePolls_AreSingleFlight()
        {
            _client.AdoptQueue("q");
            _client.Tick(3.0);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Has.Count.EqualTo(1));

            _client.PollQueueNow();
            _client.Tick(3.1);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Has.Count.EqualTo(1), "在途时不发第二个");

            _net.CallsOf(MessageIds.GetQueueStatus)[0]
                .Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.Queued });
            _client.Tick(3.2);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Has.Count.EqualTo(2), "回来后立刻补问");
        }

        [Test]
        public void LateNotQueuedFromAnEarlierQueue_DoesNotEndTheNewOne()
        {
            _client.AdoptQueue("q1");
            _client.Tick(3.0);
            var stale = _net.CallsOf(MessageIds.GetQueueStatus)[0];
            _net.RaiseDisconnected(); // 第一次排队随断线作废,那次轮询还在途
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));
            Assert.That(_client.AdoptQueue("q2"), Is.True);

            stale.Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.NotQueued });

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued), "上一次排队的结果不能结束这一次");
            Assert.That(_client.QueueAdopted, Is.True);
            _client.Tick(3.1);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Has.Count.EqualTo(1), "丢弃后不立即重问");
            _client.Tick(6.0);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Has.Count.EqualTo(2), "这一次排队按自己的节奏(进入排队 3 秒后)问");
        }

        [Test]
        public void OwnQueue_LateStaleReplyDoesNotTriggerAnEarlyPoll()
        {
            // 自己单排:轮询在途时取消成功(回到空闲),马上又排了一次;上一次的轮询回包这才到。
            _client.JoinQueue(Match.MatchMode._1V1, 0);
            _client.Tick(3.0);
            var stale = _net.CallsOf(MessageIds.GetQueueStatus)[0];
            _client.CancelQueue();
            _net.CallsOf(MessageIds.CancelQueue)[0].Respond(new Empty());
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));
            _client.JoinQueue(Match.MatchMode._1V1, 0);

            stale.Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.Queued });
            _client.Tick(3.1);

            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Has.Count.EqualTo(1),
                "不能抢在新的 JoinQueue 被处理之前就去问(会被回「不在排」打回空闲)");
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued));
        }

        [Test]
        public void CancelVerification_IgnoresAPollSentBeforeTheCancelWasAnswered()
        {
            _client.AdoptQueue("q");
            _client.Tick(3.0);
            var early = _net.CallsOf(MessageIds.GetQueueStatus)[0]; // 点取消之前就发出的轮询
            _client.CancelQueue();
            _net.CallsOf(MessageIds.CancelQueue)[0].Respond(new Empty());

            early.Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.Queued });
            Assert.That(_errors, Is.Empty, "那次轮询不算求证,不报「取消未生效」");
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued));

            _client.Tick(3.1);
            var polls = _net.CallsOf(MessageIds.GetQueueStatus);
            Assert.That(polls, Has.Count.EqualTo(2), "取消回包之后发出的才是求证");
            polls[1].Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.NotQueued });
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));
            Assert.That(_client.AdoptedQueueEndedByTicket, Is.True);
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void ProbeQueueStatus_OnlyWhenIdleAndDroppedOnceANewQueueStarts()
        {
            var results = new List<Match.QueueState>();
            Assert.That(_client.ProbeQueueStatus(results.Add), Is.True);
            Assert.That(_client.ProbeQueueStatus(results.Add), Is.False, "已有一次在途");
            _net.CallsOf(MessageIds.GetQueueStatus)[0]
                .Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.Queued });
            Assert.That(results, Is.EqualTo(new[] { Match.QueueState.Queued }));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None), "探测不改相位");

            // 探测在途时自己排上了:结果作废;排队轮询等探测回来再发(同号请求不并发)。
            Assert.That(_client.ProbeQueueStatus(results.Add), Is.True);
            _client.JoinQueue(Match.MatchMode._1V1, 0);
            Assert.That(_client.ProbeQueueStatus(results.Add), Is.False, "排队中不探测");
            _client.Tick(3.0);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Has.Count.EqualTo(2));
            _net.CallsOf(MessageIds.GetQueueStatus)[1]
                .Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.NotQueued });
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued));
            _client.Tick(3.1);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Has.Count.EqualTo(3));
        }

        [Test]
        public void AdoptedQueueEndedByBattleStart_IsNotTicketEvidence()
        {
            // 先有一次因「已不在排」结束的被带入排队:标记为真。
            _client.AdoptQueue("q0");
            _client.Tick(3.0);
            _net.CallsOf(MessageIds.GetQueueStatus)[0]
                .Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.NotQueued });
            Assert.That(_client.AdoptedQueueEndedByTicket, Is.True);

            // 下一次被带入的排队因开战结束:标记要变回假。
            _client.AdoptQueue("q");
            var state = new BattleStateS2C { BattleId = 91, RoundIndex = 1, Outcome = eBattleOutcome.BattleOutcomeOngoing };
            state.PendingActorIds.Add(MyId);
            _net.PushNotify(MessageIds.NotifyBattleStart, new BattleStartS2C { BattleId = 91, State = state });

            Assert.That(_client.QueueAdopted, Is.False);
            Assert.That(_client.AdoptedQueueEndedByTicket, Is.False);
        }
    }
}
