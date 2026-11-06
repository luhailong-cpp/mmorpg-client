using System;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Net
{
    public sealed class GateHeartbeatTests
    {
        [Test]
        public void FirstHeartbeatIsImmediateAndTicksWithoutBusinessTraffic()
        {
            var state = new GateHeartbeat();
            Assert.That(state.Start(10000, 30000, 123), Is.True);
            Assert.That(state.TrySend(123, out var seq), Is.True);
            Assert.That(seq, Is.EqualTo(1));
            Assert.That(state.TrySend(10122, out _), Is.False);
            Assert.That(state.TrySend(10123, out seq), Is.True);
            Assert.That(seq, Is.EqualTo(2));
        }

        [Test]
        public void NoAnnouncementMeansNoSendOrExpiry()
        {
            var state = new GateHeartbeat();
            Assert.That(state.TrySend(100000, out _), Is.False);
            Assert.That(state.Expired(100000), Is.False);
            Assert.That(state.Start(0, 0, 100000), Is.False);
            Assert.That(state.TrySend(200000, out _), Is.False);
            Assert.That(state.Expired(200000), Is.False);
        }

        [Test]
        public void DuplicateVerificationDoesNotRescheduleFirstHeartbeatOrTimeout()
        {
            var state = new GateHeartbeat();
            state.Start(10000, 30000, 0);
            state.TrySend(0, out _);
            Assert.That(state.Start(10000, 30000, 5000), Is.False);
            Assert.That(state.TrySend(5000, out _), Is.False);
            Assert.That(state.Expired(30000), Is.True);
        }

        [Test]
        public void EveryInboundByteRestartsTimeoutButNeverPostponesSend()
        {
            var state = new GateHeartbeat();
            state.Start(10000, 30000, 0);
            state.TrySend(0, out _);
            state.NoteInbound(9999);
            Assert.That(state.TrySend(10000, out _), Is.True);
            Assert.That(state.Expired(39998.99), Is.False);
            Assert.That(state.Expired(39999), Is.True);
        }

        [Test]
        public void DeadlineClosesWithoutOneLastHeartbeat()
        {
            var state = new GateHeartbeat();
            state.Start(10000, 30000, 0);
            Assert.That(state.Expired(29999.99), Is.False);
            Assert.That(state.Expired(30000), Is.True);
            Assert.That(state.TrySend(30000, out _), Is.False);
        }

        [Test]
        public void LateWorkerSendsOnceWithoutCatchUpBurst()
        {
            var state = new GateHeartbeat();
            state.Start(10000, 30000, 0);
            state.TrySend(0, out _);
            Assert.That(state.TrySend(25000, out var seq), Is.True);
            Assert.That(seq, Is.EqualTo(2));
            Assert.That(state.TrySend(25000, out _), Is.False);
            state.NoteInbound(25001);
            Assert.That(state.WaitMilliseconds(25001), Is.EqualTo(9999));
        }

        [Test]
        public void MatchingReplyMeasuresRttAndUnknownSequenceDoesNotChangeIt()
        {
            var state = new GateHeartbeat();
            state.Start(10000, 30000, 0);
            state.TrySend(0, out var seq);
            state.NoteResponse(seq, 17);
            Assert.That(state.RoundTripMs, Is.EqualTo(17));
            state.NoteResponse(999, 500);
            Assert.That(state.RoundTripMs, Is.EqualTo(17));
        }

        [TestCase(10000u, 0u)]
        [TestCase(10000u, 10000u)]
        [TestCase(10000u, 9999u)]
        public void InvalidAnnouncementFailsInsteadOfBusyLoop(uint interval, uint timeout)
            => Assert.Throws<ArgumentException>(() => new GateHeartbeat().Start(interval, timeout, 0));
    }
}
