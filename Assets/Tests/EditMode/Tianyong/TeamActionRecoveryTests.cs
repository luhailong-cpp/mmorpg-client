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
            _now = 0;
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

        [TestCase(false)]
        [TestCase(true)]
        public void FirstDefiniteRateLimitClearsTheDurableSlotAndAllowsADifferentWrite(bool doubleClick)
        {
            byte[] original = BeginApplication(out string saved);
            if (doubleClick)
            {
                int calls = _net.Calls.Count;
                Assert.That(_client.ApplyJoin(99), Is.False, "在途期间的同意图连点仅在本地被拒绝");
                Assert.That(_net.Calls.Count, Is.EqualTo(calls));
            }

            _net.Fail(RateLimitError);
            Assert.That(_store.Load(10), Is.Empty, "首次明确拒绝不能留下假未知意图");
            Assert.That(_client.RequiresReconnect || _client.Suspended, Is.False);
            Assert.That(_client.HasFreshView, Is.True, "gate明确拒绝没有使已知权威视图过期");
            _now += TeamClient.MessageWindowSeconds + 1;

            Assert.That(_client.Create(), Is.True, _client.Status);
            Assert.That(LastId, Is.EqualTo(MessageIds.CreateTeam));
            Assert.That(((CreateTeamRequest)LastRequest).RequestId,
                Is.Not.EqualTo(ApplyJoinTeamRequest.Parser.ParseFrom(original).RequestId));
            Assert.That(_store.Load(10), Is.Not.Empty.And.Not.EqualTo(saved));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RateLimitAfterTimeoutPreservesOriginalUuidAndPayloadAcrossRecovery(bool reconstruct)
        {
            byte[] original = BeginApplication(out string saved);
            _net.Fail("rpc timeout");
            Assert.That(_store.Load(10), Is.EqualTo(saved));
            if (reconstruct) Recreate();
            else ReadAfterReconnect();

            AssertApplicationReplay(original);
            AssertRateLimitPreservesIntentUntilTerminalReply(original, saved, false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InterruptedInFlightWriteRemainsUnknownWhenItsReplayIsRateLimited(bool reset)
        {
            byte[] original = BeginApplication(out string saved);
            // 原请求没有回调；不能因为下一次重发被gate拒绝就断言第一次没有执行。
            if (reset) _client.Reset();
            else _net.Disconnect();
            ReadAfterReconnect();

            AssertApplicationReplay(original);
            AssertRateLimitPreservesIntentUntilTerminalReply(original, saved, false);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void FaultThenRateLimitPreservesIntentUntilOriginalGetsTerminalReply(
            bool faultHasView, bool terminalBusinessRejection)
        {
            byte[] original = BeginApplication(out string saved);
            _net.Reply(new TeamResponse
            {
                Team = faultHasView ? NoTeamReply().Team : null,
                ErrorMessage = new TipInfoMessage { Id = (uint)team_error.KTeamInternal }
            });
            Assert.That(_store.Load(10), Is.EqualTo(saved), "fault即使附带权威视图也不是原写操作终态");
            _now += TeamClient.ReadRetryDelaySeconds + 1;
            Assert.That(_client.Refresh(), Is.True);
            _net.Reply(NoTeamReply());
            _now += 1;

            AssertApplicationReplay(original);
            AssertRateLimitPreservesIntentUntilTerminalReply(original, saved, terminalBusinessRejection);
        }

        [Test]
        public void RateLimitedGetMyTeamDoesNotClearAnUnknownWrite()
        {
            byte[] original = BeginApplication(out string saved);
            _net.Fail("rpc timeout");
            _net.Identity = new object();
            _client.ObserveConnection();
            _now += 1;
            _client.DrainQueued(false);
            Assert.That(LastId, Is.EqualTo(MessageIds.GetMyTeam));
            _net.Fail(RateLimitError);
            Assert.That(_store.Load(10), Is.EqualTo(saved), "只读探针的明确拒绝不能结算之前的写意图");

            _now += TeamClient.ReadRetryDelaySeconds + 1;
            _client.DrainQueued(false);
            Assert.That(LastId, Is.EqualTo(MessageIds.GetMyTeam));
            _net.Reply(NoTeamReply());
            _now += 1;
            Assert.That(_client.Create(), Is.False);
            AssertApplicationReplay(original);
            _net.Reply(NoTeamReply());
            Assert.That(_store.Load(10), Is.Empty);
        }

        [Test]
        public void RateLimitCleanupLockFailurePreservesTheRecordAndBlocksNewWrites()
        {
            byte[] original = BeginApplication(out string saved);
            string lockPath = Path.Combine(_directory, "10.pending.lock");
            using (var guard = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                Assert.DoesNotThrow(() => _net.Fail(RateLimitError), "真实持久锁冲突必须保留为可恢复状态");

            Assert.That(_store.Load(10), Is.EqualTo(saved));
            _now += TeamClient.MessageWindowSeconds + 1;
            int calls = _net.Calls.Count;
            Assert.That(_client.Create(), Is.False, "锁已释放也不能用新意图覆盖尚未清理的旧记录");
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));
            Assert.That(_store.Load(10), Is.EqualTo(saved));

            Recreate();
            AssertApplicationReplay(original);
            _net.Reply(NoTeamReply());
            Assert.That(_store.Load(10), Is.Empty, "恢复原UUID取得终态后才释放持久槽");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TerminalReplyCleanupFailurePreservesTheRecordAndItsErrorMessage(bool businessRejection)
        {
            byte[] original = BeginApplication(out string saved);
            var reply = NoTeamReply();
            reply.Team.MembershipEpoch = 2;
            if (businessRejection)
                reply.ErrorMessage = new TipInfoMessage { Id = (uint)team_error.KTeamPlayerNotFound };

            using (var guard = new FileStream(Path.Combine(_directory, "10.pending.lock"),
                       FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                Assert.DoesNotThrow(() => _net.Reply(reply));

            Assert.That(_client.Snapshot.MembershipEpoch, Is.EqualTo(2UL), "清理失败仍须应用权威视图");
            Assert.That(_client.HasFreshView, Is.True);
            Assert.That(_client.Status, Is.EqualTo("组队操作已确认，本机原请求尚未清理。"));
            Assert.That(_store.Load(10), Is.EqualTo(saved));
            _now += TeamClient.MessageWindowSeconds + 1;
            int calls = _net.Calls.Count;
            Assert.That(_client.Create(), Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));

            Recreate();
            AssertApplicationReplay(original);
            _net.Reply(NoTeamReply());
            Assert.That(_store.Load(10), Is.Empty, "恢复原请求取得终态后再释放记录");
        }

        private string RateLimitError => "server tip=" + (uint)common_error.KRateLimitExceeded;
        private uint LastId => _net.Calls[_net.Calls.Count - 1].Id;
        private IMessage LastRequest => _net.Calls[_net.Calls.Count - 1].Request;

        private byte[] BeginApplication(out string saved)
        {
            Assert.That(_client.ApplyJoin(99), Is.True, _client.Status);
            var request = (ApplyJoinTeamRequest)LastRequest;
            Assert.That(Guid.TryParseExact(request.RequestId, "D", out var id) && id != Guid.Empty, Is.True);
            Assert.That(request.TargetPlayerId, Is.EqualTo(99UL));
            saved = _store.Load(10);
            Assert.That(saved, Is.Not.Empty);
            return request.ToByteArray();
        }

        private void ReadAfterReconnect()
        {
            _net.IsReady = true;
            _net.Identity = new object();
            _client.ObserveConnection();
            _now += 1;
            _client.DrainQueued(false);
            Assert.That(LastId, Is.EqualTo(MessageIds.GetMyTeam));
            _net.Reply(NoTeamReply());
            _now += 1;
        }

        private void AssertApplicationReplay(byte[] original)
        {
            int calls = _net.Calls.Count;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls + 1));
            Assert.That(LastId, Is.EqualTo(MessageIds.ApplyJoinTeam));
            Assert.That(LastRequest.ToByteArray(), Is.EqualTo(original), "恢复始终重放原UUID及完整申请正文");
        }

        private void AssertRateLimitPreservesIntentUntilTerminalReply(
            byte[] original, string saved, bool terminalBusinessRejection)
        {
            _net.Fail(RateLimitError);
            Assert.That(_store.Load(10), Is.EqualTo(saved), "本次重发的明确拒绝不能撤销此前的不确定尝试");
            _now += TeamClient.MessageWindowSeconds + TeamClient.ReadRetryDelaySeconds + 1;
            int calls = _net.Calls.Count;
            Assert.That(_client.Create(), Is.False);
            Assert.That(_client.ApplyJoin(100), Is.False, "同动作换目标也不能替换未知原正文");
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));
            AssertApplicationReplay(original);

            var reply = NoTeamReply();
            if (terminalBusinessRejection)
                reply.ErrorMessage = new TipInfoMessage { Id = (uint)team_error.KTeamPlayerNotFound };
            _net.Reply(reply);
            Assert.That(_store.Load(10), Is.Empty, "原请求取得成功或业务拒绝终态后必须释放持久槽");
            _now += TeamClient.MessageWindowSeconds + 1;
            Assert.That(_client.Create(), Is.True, _client.Status);
        }

        private static TeamResponse NoTeamReply() => new TeamResponse
        {
            Team = new TeamView { MembershipEpoch = 1, ServerTimeMs = 1000 }
        };
    }
}
