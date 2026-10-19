using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class RegionProbeClientTests
    {
        private sealed class Connection : IFramedConnection
        {
            public bool Connected { get; private set; }
            public readonly List<IMessage> Sent = new();
            public readonly Queue<IMessage> Incoming = new();
            public event Action<IMessage> OnMessage;
            public event Action<string> OnError;
            public event Action OnDisconnected;
            public void Connect(string host, int port) { Connected = true; }
            public void Send(IMessage message) { Sent.Add(message); }
            public void Poll() { while (Incoming.Count > 0 && Connected) OnMessage?.Invoke(Incoming.Dequeue()); }
            public void Dispose() { Connected = false; }
        }
        private sealed class Recovery : IMatchJoinRecovery
        {
            public Match.JoinQueueRequest Request;
            public bool Cancel;
            public bool Broken;
            public Match.JoinQueueRequest Read(ulong playerId, out bool cancellationRequested)
            { cancellationRequested = Cancel; if(Broken)throw new InvalidOperationException("broken fixture");return Request?.PlayerId == playerId ? Request.Clone() : null; }
            public void Save(Match.JoinQueueRequest request, bool cancellationRequested) { Request = request.Clone(); Cancel = cancellationRequested; }
            public void Clear(ulong playerId) { if (Request?.PlayerId == playerId) Request = null; }
        }
        private static FakeBattleTransport.RecordedCall StartGlobal(FakeBattleTransport net, BattleClient battle)
        {
            var connection = new Connection();
            battle.RegionProbe = new RegionProbeClient(net, () => connection, work => work());
            battle.Tick(0);
            net.CallsOf(MessageIds.GetQueueStatus).Last().Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.NotQueued });
            battle.JoinQueue(Match.MatchMode._1V1, 1);
            var plan = new Match.GetNetworkProbePlanResponse { Required = true, PlanId = "plan" };
            plan.Targets.Add(new Match.NetworkProbeTarget { PhysicalRegion = 1, Host = "127.0.0.1", Port = 10000,
                Grant = new Match.SignedNetworkProbe { Payload = ByteString.CopyFromUtf8("grant"), Signature = ByteString.CopyFrom(new byte[32]) } });
            net.CallsOf(MessageIds.GetNetworkProbePlan).Last().Respond(plan);
            battle.Tick(0.01);
            for (uint i = 0; i < 3; i++) connection.Incoming.Enqueue(new Match.NetworkProbeChallenge { Nonce = ByteString.CopyFrom(new byte[32]), SampleIndex = i });
            connection.Incoming.Enqueue(new Match.NetworkProbeComplete { Receipt = new Match.SignedNetworkProbe { Payload = ByteString.CopyFromUtf8("receipt"), Signature = ByteString.CopyFrom(new byte[32]) } });
            battle.Tick(0.02);
            net.CallsOf(MessageIds.CommitNetworkProbe).Last().Respond(new Match.CommitNetworkProbeResponse { ExpiresAtMs = 100000 });
            return net.CallsOf(MessageIds.JoinQueue).Last();
        }

        [Test]
        public void EchoesChallengesAndSubmitsUnmodifiedSignedReceipt()
        {
            var net = new FakeBattleTransport(); var connection = new Connection();
            using var client = new RegionProbeClient(net, () => connection, work => work());
            string accepted = null;
            client.Prepare(plan => accepted = plan, Assert.Fail);
            var grant = new Match.SignedNetworkProbe { Payload = ByteString.CopyFromUtf8("grant"), Signature = ByteString.CopyFrom(new byte[32]) };
            var planResponse = new Match.GetNetworkProbePlanResponse { Required = true, PlanId = "plan" };
            planResponse.Targets.Add(new Match.NetworkProbeTarget { PhysicalRegion = 2, Host = "127.0.0.1", Port = 10000, Grant = grant });
            net.Calls.Single().Respond(planResponse); client.Tick(0);
            Assert.That(connection.Sent[0], Is.TypeOf<Match.NetworkProbeStart>());
            for (uint i = 0; i < 3; i++) connection.Incoming.Enqueue(new Match.NetworkProbeChallenge { Nonce = ByteString.CopyFrom(new byte[32]), SampleIndex = i });
            client.Tick(0.1);
            Assert.That(connection.Sent.OfType<Match.NetworkProbeEcho>().Select(e => e.SampleIndex), Is.EqualTo(new uint[] { 0, 1, 2 }));
            var receipt = new Match.SignedNetworkProbe { Payload = ByteString.CopyFromUtf8("server-measured"), Signature = ByteString.CopyFrom(new byte[32]) };
            connection.Incoming.Enqueue(new Match.NetworkProbeComplete { Receipt = receipt }); client.Tick(0.2);
            var commit = net.Calls.Last(); Assert.That(commit.Request, Is.TypeOf<Match.CommitNetworkProbeRequest>());
            Assert.That(((Match.CommitNetworkProbeRequest)commit.Request).Receipts[0], Is.EqualTo(receipt));
            commit.Respond(new Match.CommitNetworkProbeResponse { ExpiresAtMs = 100000 }); Assert.That(accepted, Is.EqualTo("plan"));
        }

        [Test]
        public void CancelAcknowledgementDoesNotPretendFormationWasCancelled()
        {
            var net = new FakeBattleTransport(); var battle = new BattleClient(net);
            StartGlobal(net, battle).Respond(new Match.JoinQueueResponse { QueueTicket = "ticket" });
            battle.CancelQueue();
            net.CallsOf(MessageIds.CancelQueue).Last().Respond(new Empty());
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Queued), "Empty只表示取消请求已处理，不能证明取消赢过成局");
            net.CallsOf(MessageIds.GetQueueStatus).Last().Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.Matched, QueueTicket = "ticket", BattleId = 9, RequestCancelled = true });
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Preparing));
        }

        [Test]
        public void UnknownJoinNeedsSqlCancellationProofBeforeLeavingQueue()
        {
            var net = new FakeBattleTransport(); var recovery = new Recovery();
            var battle = new BattleClient(net) { QueueRecovery = recovery };
            var join = StartGlobal(net, battle);
            var request = (Match.JoinQueueRequest)join.Request;
            Assert.That(recovery.Request.RequestId, Is.EqualTo(request.RequestId), "发包前已保存原意图");
            join.FailWith("reply lost");
            net.CallsOf(MessageIds.GetQueueStatus).Last().Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.NotQueued });
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Queued), "NotQueued不能证明迟到Join永远不会提交");
            battle.CancelQueue();
            var cancel = (Match.CancelQueueRequest)net.CallsOf(MessageIds.CancelQueue).Last().Request;
            Assert.That(cancel.RequestId, Is.EqualTo(request.RequestId)); Assert.That(cancel.QueueTicket, Is.Empty);
            Assert.That(recovery.Cancel, Is.True);
            net.CallsOf(MessageIds.GetQueueStatus).Last().Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.NotQueued });
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Queued));
            net.CallsOf(MessageIds.GetQueueStatus).Last().Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.NotQueued, RequestCancelled = true });
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.None)); Assert.That(recovery.Request, Is.Null);
            join.Respond(new Match.JoinQueueResponse { QueueTicket = "late-old-ticket" });
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.None));
        }

        [Test]
        public void RecreatedClientResumesExactUnknownCancellationIntent()
        {
            var net = new FakeBattleTransport(); var recovery = new Recovery();
            string request = Guid.NewGuid().ToString("D");
            recovery.Save(new Match.JoinQueueRequest { PlayerId = net.PlayerId, RequestId = request, Mode = Match.MatchMode._1V1 }, true);
            var battle = new BattleClient(net) { QueueRecovery = recovery, RegionProbe = new RegionProbeClient(net, () => new Connection(), work => work()) };
            battle.Tick(0);
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Queued));
            Assert.That(((Match.CancelQueueRequest)net.CallsOf(MessageIds.CancelQueue).Last().Request).RequestId, Is.EqualTo(request));
            Assert.That(((Match.GetQueueStatusRequest)net.CallsOf(MessageIds.GetQueueStatus).Last().Request).RequestId, Is.EqualTo(request));
            net.CallsOf(MessageIds.GetQueueStatus).Last().Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.NotQueued, RequestCancelled = true });
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.None));
        }
        [Test] public void CorruptRecoveryCannotBeReleasedByNotQueuedOrCancel()
        {
            var net=new FakeBattleTransport();var recovery=new Recovery{Broken=true};
            var battle=new BattleClient(net){QueueRecovery=recovery,RegionProbe=new RegionProbeClient(net,()=>new Connection(),work=>work())};
            battle.Tick(0);net.CallsOf(MessageIds.GetQueueStatus).Last().Respond(new Match.GetQueueStatusResponse{State=Match.QueueState.NotQueued});
            battle.CancelQueue();battle.JoinQueue(Match.MatchMode._1V1,1);
            Assert.That(battle.Phase,Is.EqualTo(BattlePhase.Queued));Assert.That(net.CallsOf(MessageIds.JoinQueue),Is.Empty);
        }

        [Test]
        public void LateProbePlanAfterDisconnectCannotStartMatch()
        {
            var net = new FakeBattleTransport(); var battle = new BattleClient(net);
            battle.RegionProbe = new RegionProbeClient(net, () => new Connection(), work => work());
            battle.JoinQueue(Match.MatchMode._1V1, 1); var pending = net.Calls.Single();
            net.RaiseDisconnected(); pending.Respond(new Match.GetNetworkProbePlanResponse { Required = false });
            Assert.That(net.CallsOf(MessageIds.JoinQueue), Is.Empty);
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.None));
        }

        [Test]
        public void OldQueuePollCannotRestoreCancelledGeneration()
        {
            var net = new FakeBattleTransport(); var battle = new BattleClient(net);
            StartGlobal(net, battle).Respond(new Match.JoinQueueResponse { QueueTicket = "ticket" });
            battle.Tick(4);
            var oldPoll = net.CallsOf(MessageIds.GetQueueStatus).Last();
            battle.CancelQueue();
            Assert.That(net.CallsOf(MessageIds.GetQueueStatus).Last(), Is.SameAs(oldPoll), "单在途：取消后的求证等旧查询释放后再发");
            oldPoll.Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.Ready, QueueTicket = "ticket", BattleId = 9 });
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Queued), "取消代次不接受旧查询的Ready结果");
            battle.Tick(4.1);
            net.CallsOf(MessageIds.GetQueueStatus).Last().Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.NotQueued, RequestCancelled = true });
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.None));
            oldPoll.Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.Ready, QueueTicket = "ticket", BattleId = 9 });
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.None), "旧代查询回包不能覆盖已确认取消后的状态");
        }

        [Test]
        public void AdoptedQueueWithAProductionRegionProbeStillSendsCancellation()
        {
            var net = new FakeBattleTransport();
            var battle = new BattleClient(net) { RegionProbe = new RegionProbeClient(net, () => new Connection(), work => work()) };
            Assert.That(battle.AdoptQueue("随队匹配"), Is.True);
            battle.CancelQueue();
            Assert.That(net.CallsOf(MessageIds.CancelQueue), Has.Count.EqualTo(1), "服务端已随队入队，不能只取消本地网络探测");
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Queued));
        }

        [Test]
        public void AdoptedGlobalQueuePreparingTimeoutReturnsToPollingWithoutDiscardingTheQueue()
        {
            var net = new FakeBattleTransport();
            var battle = new BattleClient(net) { RegionProbe = new RegionProbeClient(net, () => new Connection(), work => work()) };
            battle.AdoptQueue("随队匹配");
            battle.Tick(0);
            net.CallsOf(MessageIds.GetQueueStatus).Last().Respond(new Match.GetQueueStatusResponse
                { State = Match.QueueState.Matched, QueueTicket = "team-ticket", BattleId = 9 });
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Preparing));
            battle.Tick(BattleClient.PreparingTimeoutSeconds);
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Queued));
            Assert.That(battle.QueueAdopted, Is.True);
        }
    }
}
