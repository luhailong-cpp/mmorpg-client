using System;
using System.IO;
using Google.Protobuf;
using NUnit.Framework;
using Teampb;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>合并后的正式恢复与补位字段必须同时可读写，且不丢失其他未知数据。</summary>
    public sealed class TeamProtocolCompatibilityTests
    {
        [Test]
        public void GetMyTeam_PreservesRecoveryAndRegroupAcrossBothWireOrdersAndClone()
        {
            foreach (var bytes in new[] {
                new byte[] { 0x08, 0x01, 0x18, 0x01, 0x12, 0x01, 0x72 },
                new byte[] { 0x08, 0x01, 0x12, 0x01, 0x72, 0x18, 0x01 } })
            {
                var request = GetMyTeamRequest.Parser.ParseFrom(bytes);
                Assert.That(request.NotifyOnline, Is.True);
                Assert.That(request.Regroup, Is.True);
                Assert.That(request.MatchRequestId, Is.EqualTo("r"));
                var copy = GetMyTeamRequest.Parser.ParseFrom(request.Clone().ToByteArray());
                Assert.That(copy.Regroup, Is.True);
                Assert.That(copy.MatchRequestId, Is.EqualTo("r"));
                copy.Regroup = false;
                Assert.That(GetMyTeamRequest.Parser.ParseFrom(copy.ToByteArray()).Regroup, Is.False);
                Assert.That(copy.MatchRequestId, Is.EqualTo("r"));
                Assert.That(request.Regroup, Is.True, "克隆后修改不可影响原消息");
            }
        }

        [Test]
        public void StartMatch_PreservesRecoveryAndBackfillAcrossBothWireOrdersAndClone()
        {
            foreach (var bytes in new[] {
                new byte[] { 0x08, 0x07, 0x10, 0x0a, 0x20, 0x05, 0x1a, 0x01, 0x72 },
                new byte[] { 0x08, 0x07, 0x10, 0x0a, 0x1a, 0x01, 0x72, 0x20, 0x05 } })
            {
                var request = StartTeamMatchRequest.Parser.ParseFrom(bytes);
                Assert.That(request.BattleConfigId, Is.EqualTo(7u));
                Assert.That(request.ExpectedTeamId, Is.EqualTo(10ul));
                Assert.That(request.TargetTeamSize, Is.EqualTo(5u));
                Assert.That(request.RequestId, Is.EqualTo("r"));
                var copy = StartTeamMatchRequest.Parser.ParseFrom(request.Clone().ToByteArray());
                Assert.That(copy.TargetTeamSize, Is.EqualTo(5u));
                Assert.That(copy.RequestId, Is.EqualTo("r"));
                copy.TargetTeamSize = 0;
                Assert.That(StartTeamMatchRequest.Parser.ParseFrom(copy.ToByteArray()).TargetTeamSize, Is.Zero);
                Assert.That(copy.RequestId, Is.EqualTo("r"));
                Assert.That(request.TargetTeamSize, Is.EqualTo(5u));
            }
        }

        [Test]
        public void GeneratedRecoverySerialization_StaysByteIdenticalWithoutBackfillFields()
        {
            Assert.That(new GetMyTeamRequest { NotifyOnline = true, MatchRequestId = "r" }.ToByteArray(),
                Is.EqualTo(new byte[] { 0x08, 0x01, 0x12, 0x01, 0x72 }));
            Assert.That(new StartTeamMatchRequest { BattleConfigId = 7, ExpectedTeamId = 10, RequestId = "r" }.ToByteArray(),
                Is.EqualTo(new byte[] { 0x08, 0x07, 0x10, 0x0a, 0x1a, 0x01, 0x72 }));
        }

        [Test]
        public void OfficialBackfillSerialization_UsesMergedCanonicalTagsWithoutRecoveryValues()
        {
            // 正式合并契约保持恢复字段2/3，补位请求使用独立字段3/4。
            Assert.That(new GetMyTeamRequest { NotifyOnline = true, Regroup = true }.ToByteArray(),
                Is.EqualTo(new byte[] { 0x08, 0x01, 0x18, 0x01 }));
            Assert.That(new StartTeamMatchRequest { BattleConfigId = 7, ExpectedTeamId = 10, TargetTeamSize = 5 }.ToByteArray(),
                Is.EqualTo(new byte[] { 0x08, 0x07, 0x10, 0x0a, 0x20, 0x05 }));
        }

        [Test]
        public void QueueView_AllFourOfficialFieldsSurviveRoundTripAndMerge()
        {
            var bytes = new byte[] { 0x38, 0x02, 0x68, 0x05, 0x70, 0x90, 0xbf, 0x05, 0x78, 0x07, 0x80, 0x01, 0x01 };
            var parsed = TeamView.Parser.ParseFrom(bytes);
            AssertQueue(parsed);
            Assert.That(parsed.ToByteArray(), Is.EqualTo(bytes));
            var created = new TeamView {
                MatchState = TeamBackfillProtocol.QueuedState, MatchTargetSize = 5, MatchQueuedAtMs = 90000,
                MatchBattleConfigId = 7, MatchFillSupported = true
            };
            Assert.That(created.ToByteArray(), Is.EqualTo(bytes));
            var merged = new TeamView();
            merged.MergeFrom(created);
            AssertQueue(TeamView.Parser.ParseFrom(merged.Clone().ToByteArray()));
            Assert.That(new TeamView().MatchFillSupported, Is.False, "恢复分支未声明能力时不能展示补位入口");
        }

        [Test]
        public void ReplacingScalar_RemovesOnlyItsVarints_PreservingSameNumberOtherWireTypesAndUnknownFields()
        {
            var request = GetMyTeamRequest.Parser.ParseFrom(new byte[] {
                0x18, 0x00, 0x18, 0x01, // 正式Regroup字段3重复出现：最后一次生效。
                0x12, 0x01, 0x72,       // 恢复string字段2保持不变。
                0x1d, 0x01, 0x02, 0x03, 0x04, // 字段3的未知fixed32仍完整保留。
                0xa0, 0x06, 0x2a       // 另一个未知字段 100。
            });
            Assert.That(request.Regroup, Is.True);
            request.Regroup = true;
            Assert.That(CountTag(request.ToByteArray(), 0x18), Is.EqualTo(1));
            request.Regroup = false;
            var bytes = request.ToByteArray();
            Assert.That(CountTag(bytes, 0x18), Is.Zero);
            Assert.That(CountTag(bytes, 0x1d), Is.EqualTo(1));
            Assert.That(CountTag(bytes, 0x320), Is.EqualTo(1));
            Assert.That(GetMyTeamRequest.Parser.ParseFrom(bytes).MatchRequestId, Is.EqualTo("r"));
            var reparsed = GetMyTeamRequest.Parser.ParseFrom(bytes);
            reparsed.Regroup = true;
            Assert.That(CountTag(reparsed.ToByteArray(), 0x1d), Is.EqualTo(1));
            Assert.That(CountTag(reparsed.ToByteArray(), 0x320), Is.EqualTo(1));
        }

        [Test]
        public void SettingOneQueueField_DoesNotClearOtherQueueFieldsOrUnknownData()
        {
            var view = TeamView.Parser.ParseFrom(new byte[] { 0xa0, 0x06, 0x2a });
            view.MatchTargetSize = uint.MaxValue;
            view.MatchQueuedAtMs = ulong.MaxValue;
            view.MatchBattleConfigId = uint.MaxValue;
            view.MatchFillSupported = true;
            view.MatchTargetSize = 3;
            view.MatchFillSupported = false;
            var copy = TeamView.Parser.ParseFrom(view.ToByteArray());
            Assert.That(copy.MatchTargetSize, Is.EqualTo(3u));
            Assert.That(copy.MatchQueuedAtMs, Is.EqualTo(ulong.MaxValue));
            Assert.That(copy.MatchBattleConfigId, Is.EqualTo(uint.MaxValue));
            Assert.That(copy.MatchFillSupported, Is.False);
            Assert.That(CountTag(copy.ToByteArray(), 0x320), Is.EqualTo(1));
        }

        [Test]
        public void RecoveryIntentResponse_IsUnchangedByBackfillQueueView()
        {
            var response = new TeamResponse {
                Team = new TeamView { MatchState = TeamBackfillProtocol.QueuedState, MatchTargetSize = 5 },
                MatchIntent = new TeamMatchIntent { RequestId = "r", BattleId = 700, State = 4 }
            };
            var copy = TeamResponse.Parser.ParseFrom(response.ToByteArray());
            Assert.That(copy.MatchIntent.RequestId, Is.EqualTo("r"));
            Assert.That(copy.MatchIntent.BattleId, Is.EqualTo(700ul));
            Assert.That(copy.MatchIntent.State, Is.EqualTo(4u));
            Assert.That(copy.Team.MatchTargetSize, Is.EqualTo(5u));
            Assert.That(copy.Team.MatchState, Is.EqualTo(TeamBackfillProtocol.QueuedState));
        }

        private static void AssertQueue(TeamView view)
        {
            Assert.That(view.MatchState, Is.EqualTo(TeamBackfillProtocol.QueuedState));
            Assert.That(view.MatchTargetSize, Is.EqualTo(5u));
            Assert.That(view.MatchQueuedAtMs, Is.EqualTo(90000ul));
            Assert.That(view.MatchBattleConfigId, Is.EqualTo(7u));
            Assert.That(view.MatchFillSupported, Is.True);
        }

        private static int CountTag(byte[] bytes, uint expected)
        {
            int count = 0;
            using (var input = new CodedInputStream(bytes))
            {
                uint tag;
                while ((tag = input.ReadTag()) != 0) { if (tag == expected) ++count; input.SkipLastField(); }
            }
            return count;
        }
    }
}
