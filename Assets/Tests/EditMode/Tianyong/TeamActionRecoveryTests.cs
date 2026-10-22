using System;
using System.IO;
using Google.Protobuf;
using MmorpgClient.Game.Team;
using MmorpgClient.Net;
using NUnit.Framework;
using Teampb;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class TeamActionRecoveryTests
    {
        private string _directory;
        private DurableRequestFileStore _store;
        private TeamFakeTransport _net;
        private TeamClient _client;
        private float _now;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "team-action-test-" + Guid.NewGuid().ToString("N"));
            _store = new DurableRequestFileStore(_directory);
            Recreate();
        }

        private void Recreate()
        {
            _client?.Dispose();
            _net = new TeamFakeTransport();
            _client = new TeamClient(_net, () => _net.Identity, _ => { }, _ => { }, () => _now) { ActionRecovery = _store };
            _now += 1f;
            _client.DrainQueued(false);
            Assert.That(_net.Calls[0].Id, Is.EqualTo(MessageIds.GetMyTeam));
            _net.Reply(new TeamResponse { Team = new TeamView { MembershipEpoch = 1, ServerTimeMs = 1000 } });
            _now += 1f;
        }

        [TearDown]
        public void TearDown()
        {
            _client.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Test]
        public void UnknownCreateAfterReconstructionReplaysExactUuidAndPayload()
        {
            Assert.That(_client.Create(), Is.True);
            var original = _net.Calls[_net.Calls.Count - 1].Request;
            string id = (string)original.Descriptor.FindFieldByName("request_id").Accessor.GetValue(original);
            Assert.That(Guid.TryParseExact(id, "D", out var parsed) && parsed != Guid.Empty, Is.True);
            byte[] bytes = original.ToByteArray();
            _net.Fail("rpc timeout");
            Assert.That(_store.Load(10), Is.Not.Empty);
            Recreate();
            _client.DrainQueued(false);
            var replay = _net.Calls[_net.Calls.Count - 1];
            Assert.That(replay.Id, Is.EqualTo(MessageIds.CreateTeam));
            Assert.That(replay.Request.ToByteArray(), Is.EqualTo(bytes));
            _net.Reply(new TeamResponse());
            Assert.That(_store.Load(10), Is.Empty);
        }

        [Test]
        public void UnresolvedActionPreventsDifferentWriteFromReplacingOriginal()
        {
            Assert.That(_client.Create(), Is.True);
            string original = _store.Load(10);
            _net.Fail("rpc timeout");
            Recreate();
            Assert.That(_client.ApplyJoin(99), Is.False);
            Assert.That(_store.Load(10), Is.EqualTo(original));
        }

        [Test]
        public void FirstAttemptRateLimitClearsIntentAndAllowsDifferentWrite()
        {
            Assert.That(_client.Create(), Is.True);
            Assert.That(_store.Load(10), Is.Not.Empty);
            _net.Fail("server tip=" + (uint)common_error.KRateLimitExceeded);
            Assert.That(_store.Load(10), Is.Empty);
            _now += 1f;
            Assert.That(_client.ApplyJoin(99), Is.True);
        }

        [Test]
        public void RateLimitAfterReconstructionPreservesEarlierUnknownIntent()
        {
            Assert.That(_client.Create(), Is.True);
            string original = _store.Load(10);
            _net.Fail("rpc timeout");
            Recreate();
            _client.DrainQueued(false);
            Assert.That(_net.Calls[_net.Calls.Count - 1].Id, Is.EqualTo(MessageIds.CreateTeam));
            _net.Fail("server tip=" + (uint)common_error.KRateLimitExceeded);
            Assert.That(_store.Load(10), Is.EqualTo(original));
            _now += 1f;
            Assert.That(_client.ApplyJoin(99), Is.False);
            Assert.That(_store.Load(10), Is.EqualTo(original));
        }

        [Test]
        public void FaultReplyThenRateLimitDoesNotEraseUnknownIntent()
        {
            Assert.That(_client.Create(), Is.True);
            string original = _store.Load(10);
            _net.Reply(new TeamResponse { ErrorMessage = new TipInfoMessage { Id = (uint)team_error.KTeamInternal } });
            _now += 1f;
            Assert.That(_client.Create(), Is.True);
            _net.Fail("server tip=" + (uint)common_error.KRateLimitExceeded);
            Assert.That(_store.Load(10), Is.EqualTo(original));
            _now += 1f;
            Assert.That(_client.ApplyJoin(99), Is.False);
            Assert.That(_store.Load(10), Is.EqualTo(original));
        }

        [Test]
        public void DamagedRecoveryRecordIsPreservedAndBlocksWrites()
        {
            _store.Save(10, "damaged-original");
            Recreate();
            Assert.That(_client.Create(), Is.False);
            Assert.That(_store.Load(10), Is.EqualTo("damaged-original"));
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
        }
    }
}
