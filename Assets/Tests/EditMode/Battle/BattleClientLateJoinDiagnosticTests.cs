using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class BattleClientLateJoinDiagnosticTests
    {
        private const ulong Player = 1001, Battle = 7700;
        private FakeBattleTransport _net;
        private BattleClient _client;
        private List<string> _errors;
        private List<BattlePhase> _phases;

        [SetUp]
        public void SetUp()
        {
            _net = new FakeBattleTransport { PlayerId = Player };
            _client = new BattleClient(_net);
            _errors = new List<string>();
            _phases = new List<BattlePhase>();
            _client.OnError += _errors.Add;
            _client.OnPhaseChanged += _phases.Add;
        }

        private FakeBattleTransport.RecordedCall Join()
        {
            _client.JoinQueue(Match.MatchMode._1V1, 1);
            return _net.CallsOf(MessageIds.JoinQueue)[_net.CallsOf(MessageIds.JoinQueue).Count - 1];
        }

        private static Match.JoinQueueResponse Rejected() => new()
        {
            ErrorMessage = new TipInfoMessage { Id = (uint)common_error.KInvalidParameter },
        };

        private void AuthoritativeBattle(bool reconnect)
        {
            var state = new BattleStateS2C { BattleId = Battle, Outcome = eBattleOutcome.BattleOutcomeOngoing };
            state.PendingActorIds.Add(_net.PlayerId);
            if (reconnect)
            {
                _net.PushNotify(MessageIds.NotifyBattleReconnect, new BattleReconnectS2C { BattleId = Battle });
                var pulls = _net.CallsOf(MessageIds.GetBattleState);
                pulls[pulls.Count - 1].Respond(state);
            }
            else _net.PushNotify(MessageIds.NotifyBattleStart, new BattleStartS2C { BattleId = Battle, State = state });
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AuthorityMayFinishTheJoinButItsOwnExplicitRejectionIsStillReportedOnce(bool reconnect)
        {
            var join = Join();
            AuthoritativeBattle(reconnect);
            int calls = _net.Calls.Count, phases = _phases.Count;
            var state = _client.State;
            join.Respond(Rejected());
            join.Respond(Rejected());
            Assert.That(_errors, Has.Count.EqualTo(1));
            Assert.That(_errors[0], Does.Contain(((uint)common_error.KInvalidParameter).ToString()));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction));
            Assert.That(_client.State, Is.SameAs(state));
            Assert.That(_phases.Count, Is.EqualTo(phases), "诊断不触发相位变更");
            Assert.That(_net.Calls.Count, Is.EqualTo(calls), "诊断不发轮询或恢复请求");
        }

        [Test]
        public void AnAcknowledgedJoinDoesNotAcquireASecondDiagnosticCompletion()
        {
            var join = Join();
            join.Respond(new Match.JoinQueueResponse { QueueTicket = "accepted" });
            AuthoritativeBattle(true);
            join.Respond(Rejected());
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void ACancelledJoinCannotReportAnOldRejection()
        {
            var join = Join();
            _client.CancelQueue();
            _net.CallsOf(MessageIds.CancelQueue)[0].Respond(new Empty());
            join.Respond(Rejected());
            Assert.That(_errors, Is.Empty);
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));
        }

        [Test]
        public void DisconnectClearsEvenAnAlreadyGrantedDiagnosticPermission()
        {
            var join = Join();
            AuthoritativeBattle(true);
            _net.RaiseDisconnected();
            AuthoritativeBattle(true);
            join.Respond(Rejected());
            Assert.That(_errors, Is.Empty);
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction));
        }

        [Test]
        public void AnotherPlayerCannotReceiveTheOldPlayersJoinDiagnostic()
        {
            var join = Join();
            AuthoritativeBattle(true);
            _net.PlayerId = Player + 1;
            AuthoritativeBattle(true);
            join.Respond(Rejected());
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void ANewQueueOwnsItsIntentAndAnOldRejectionCannotAffectIt()
        {
            var oldJoin = Join();
            _client.CancelQueue();
            _net.CallsOf(MessageIds.CancelQueue)[0].Respond(new Empty());
            var currentJoin = Join();
            oldJoin.Respond(Rejected());
            Assert.That(_errors, Is.Empty);
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued));
            AuthoritativeBattle(true);
            oldJoin.Respond(Rejected());
            Assert.That(_errors, Is.Empty);
            currentJoin.Respond(Rejected());
            Assert.That(_errors, Has.Count.EqualTo(1), "只允许新队列的原Join自己的拒绝诊断");
        }

        [Test]
        public void ATransportFailureAfterAuthorityOnlyConsumesTheOldCompletion()
        {
            var join = Join();
            AuthoritativeBattle(true);
            join.FailWith("disconnected");
            join.Respond(Rejected());
            Assert.That(_errors, Is.Empty);
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction));
        }
    }
}
