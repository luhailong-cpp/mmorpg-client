using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// PVP 战报客户端单测(docs/handoff/battle-report-replay-client.md §7):
    /// 覆盖空列表、100 条保持服务端顺序、四种胜负判定、has_replay=false、单飞、NotFound 移除、
    /// Internal/限频不自动重试、断线作废在途回调、进程内回放缓存与近期战果跳回放资格判定。
    /// </summary>
    public sealed class BattleReportClientTests
    {
        private const ulong MyPlayerId = 1001;
        private const ulong OpponentId = 2002;

        private FakeBattleTransport _net;
        private object _conn;
        private BattleReportClient _client;
        private List<IReadOnlyList<Match.BattleReportSummary>> _lists;
        private List<BattleReplay> _replays;
        private List<ulong> _removed;
        private List<string> _errors;
        private List<string> _logs;

        [SetUp]
        public void SetUp()
        {
            _net = new FakeBattleTransport { PlayerId = MyPlayerId, IsReady = true };
            _conn = new object();
            _lists = new List<IReadOnlyList<Match.BattleReportSummary>>();
            _replays = new List<BattleReplay>();
            _removed = new List<ulong>();
            _errors = new List<string>();
            _logs = new List<string>();
            _client = new BattleReportClient(_net, () => _conn, log: msg => _logs.Add(msg), maxCachedReplays: 2);
            _client.OnListUpdated += list => _lists.Add(list.ToList());
            _client.OnReplayLoaded += replay => _replays.Add(replay);
            _client.OnReportRemoved += id => _removed.Add(id);
            _client.OnError += err => _errors.Add(err);
        }

        [TearDown]
        public void TearDown() => _client.Dispose();

        private List<FakeBattleTransport.RecordedCall> ListCalls =>
            _net.CallsOf(MatchServiceRpc.ListBattleReportsMessageId);

        private List<FakeBattleTransport.RecordedCall> GetCalls =>
            _net.CallsOf(MatchServiceRpc.GetBattleReportMessageId);

        private static Match.BattleReportSummary Summary(ulong battleId, eBattleOutcome outcome, uint myTeam = 0,
            bool hasReplay = true, Match.MatchMode mode = Match.MatchMode._1V1)
        {
            var s = new Match.BattleReportSummary
            {
                BattleId = battleId,
                Mode = mode,
                BattleConfigId = 1,
                FinishedAtMs = 1700000000000UL + battleId,
                Outcome = outcome,
                TotalRounds = 5,
                HasReplay = hasReplay,
            };
            s.Members.Add(new Match.BattleReportMember { PlayerId = MyPlayerId, TeamIndex = myTeam, Name = "我方", Level = 60, ClassId = 1 });
            s.Members.Add(new Match.BattleReportMember { PlayerId = OpponentId, TeamIndex = myTeam == 0 ? 1u : 0u, Name = "对手", Level = 61, ClassId = 2 });
            return s;
        }

        private static BattleReplayEnvelope ValidGzipEnvelope(ulong battleId)
        {
            var replay = new BattleReplay
            {
                BattleId = battleId,
                MatchMode = (uint)Match.MatchMode._1V1,
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                EndReason = eBattleReplayEndReason.BattleReplayEndFinished,
                TotalRounds = 1,
                StartState = new BattleStateS2C { BattleId = battleId, RoundIndex = 1 },
            };
            replay.Rounds.Add(new TurnResultS2C
            {
                BattleId = battleId,
                RoundIndex = 1,
                State = new BattleStateS2C { BattleId = battleId, RoundIndex = 1, Outcome = eBattleOutcome.BattleOutcomeSideAWin },
            });
            byte[] raw = replay.ToByteArray();
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
                gz.Write(raw, 0, raw.Length);
            return new BattleReplayEnvelope
            {
                FormatVersion = 1,
                Codec = eBattleReplayCodec.BattleReplayCodecGzip,
                RawBytes = (uint)raw.Length,
                Payload = ByteString.CopyFrom(ms.ToArray()),
            };
        }

        [Test]
        public void EmptyList_UpdatesWithZeroItems()
        {
            Assert.IsTrue(_client.RefreshList());
            Assert.AreEqual(1, ListCalls.Count);
            ListCalls[0].Respond(new Match.ListBattleReportsResponse());

            Assert.AreEqual(1, _lists.Count);
            Assert.IsEmpty(_lists[0]);
            Assert.IsEmpty(_client.Reports);
            Assert.IsEmpty(_errors);
        }

        [Test]
        public void HundredReports_PreserveExactServerOrderWithoutSortingByBattleId()
        {
            // battle_id 故意不按单调序(模拟发号器打散),验证客户端严格保留服务端顺序
            var expectedOrder = new List<ulong>(100);
            var resp = new Match.ListBattleReportsResponse();
            for (int i = 0; i < 100; i++)
            {
                ulong id = (ulong)((i * 7919 + 13) % 100000 + 1);
                expectedOrder.Add(id);
                resp.Reports.Add(Summary(id, eBattleOutcome.BattleOutcomeSideAWin, myTeam: 0, hasReplay: i % 5 != 0));
            }

            Assert.IsTrue(_client.RefreshList());
            ListCalls[0].Respond(resp);

            Assert.AreEqual(100, _client.Reports.Count);
            CollectionAssert.AreEqual(expectedOrder, _client.Reports.Select(r => r.BattleId).ToList());
            Assert.IsFalse(_client.Reports[0].HasReplay);
            Assert.IsTrue(_client.Reports[1].HasReplay);
        }

        [Test]
        public void ResolveVerdict_CoversAllFourOutcomesAndUnknown()
        {
            // A 胜我在 A -> 胜
            Assert.AreEqual(BattleReportVerdict.Win,
                BattleReportClient.ResolveVerdict(Summary(1, eBattleOutcome.BattleOutcomeSideAWin, myTeam: 0), MyPlayerId));
            // A 胜我在 B -> 负
            Assert.AreEqual(BattleReportVerdict.Lose,
                BattleReportClient.ResolveVerdict(Summary(2, eBattleOutcome.BattleOutcomeSideAWin, myTeam: 1), MyPlayerId));
            // B 胜我在 B -> 胜
            Assert.AreEqual(BattleReportVerdict.Win,
                BattleReportClient.ResolveVerdict(Summary(3, eBattleOutcome.BattleOutcomeSideBWin, myTeam: 1), MyPlayerId));
            // B 胜我在 A -> 负
            Assert.AreEqual(BattleReportVerdict.Lose,
                BattleReportClient.ResolveVerdict(Summary(4, eBattleOutcome.BattleOutcomeSideBWin, myTeam: 0), MyPlayerId));
            // 平局 -> 平
            Assert.AreEqual(BattleReportVerdict.Draw,
                BattleReportClient.ResolveVerdict(Summary(5, eBattleOutcome.BattleOutcomeDraw, myTeam: 0), MyPlayerId));
            // 不在成员列表里 -> Unknown
            Assert.AreEqual(BattleReportVerdict.Unknown,
                BattleReportClient.ResolveVerdict(Summary(6, eBattleOutcome.BattleOutcomeSideAWin, myTeam: 0), 99999UL));
        }

        [Test]
        public void SingleFlight_BlocksConcurrentListAndConcurrentGet()
        {
            Assert.IsTrue(_client.RefreshList());
            Assert.IsFalse(_client.RefreshList(), "列表请求在途时不发第二个");
            Assert.AreEqual(1, ListCalls.Count);

            Assert.IsTrue(_client.FetchReplay(501));
            Assert.IsFalse(_client.FetchReplay(502), "回放请求在途时不发第二个");
            Assert.AreEqual(1, GetCalls.Count);
            Assert.AreEqual(501UL, _client.FetchingBattleId);

            ListCalls[0].Respond(new Match.ListBattleReportsResponse());
            Assert.IsFalse(_client.IsListing);
            GetCalls[0].Respond(new Match.GetBattleReportResponse { Replay = ValidGzipEnvelope(501) });
            Assert.IsFalse(_client.IsFetching);
            Assert.AreEqual(1, _replays.Count);
        }

        [Test]
        public void NotFound_RemovesStaleReportFromListAndReportsError()
        {
            var listResp = new Match.ListBattleReportsResponse();
            listResp.Reports.Add(Summary(701, eBattleOutcome.BattleOutcomeSideAWin));
            listResp.Reports.Add(Summary(702, eBattleOutcome.BattleOutcomeSideBWin));
            _client.RefreshList();
            ListCalls[0].Respond(listResp);
            Assert.AreEqual(2, _client.Reports.Count);

            Assert.IsTrue(_client.FetchReplay(701));
            GetCalls[0].Respond(new Match.GetBattleReportResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)match_error.KMatchBattleReportNotFound },
            });

            CollectionAssert.AreEqual(new ulong[] { 701 }, _removed);
            CollectionAssert.AreEqual(new ulong[] { 702 }, _client.Reports.Select(r => r.BattleId).ToArray());
            Assert.AreEqual(1, _errors.Count);
            Assert.AreEqual(BattleReportClient.NotFoundMessage, _errors[0]);
            Assert.AreEqual(1, GetCalls.Count, "NotFound 不自动重试");
        }

        [Test]
        public void InternalErrorAndRateLimit_ReportMessageAndNeverAutoRetry()
        {
            _client.RefreshList();
            ListCalls[0].Respond(new Match.ListBattleReportsResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)match_error.KMatchInternal },
            });
            Assert.AreEqual(1, ListCalls.Count, "Internal 不自动重试");
            Assert.AreEqual(BattleReportClient.InternalErrorMessage, _errors[0]);

            _client.FetchReplay(801);
            GetCalls[0].FailWith($"server tip={(uint)common_error.KRateLimitExceeded}");
            Assert.AreEqual(1, GetCalls.Count, "限频不自动重试");
            Assert.AreEqual(BattleReportClient.RateLimitMessage, _errors[1]);

            _client.FetchReplay(802);
            GetCalls[1].FailWith("timeout");
            Assert.AreEqual(2, GetCalls.Count, "传输失败不自动重试");
            Assert.AreEqual(BattleReportClient.TransportErrorMessage, _errors[2]);
        }

        [Test]
        public void DisconnectInvalidatesInFlightRepliesAndClearsCache()
        {
            _client.RefreshList();
            _client.FetchReplay(901);

            _net.RaiseDisconnected();
            Assert.IsFalse(_client.IsListing);
            Assert.IsFalse(_client.IsFetching);

            ListCalls[0].Respond(new Match.ListBattleReportsResponse
            {
                Reports = { Summary(901, eBattleOutcome.BattleOutcomeSideAWin) },
            });
            GetCalls[0].Respond(new Match.GetBattleReportResponse { Replay = ValidGzipEnvelope(901) });

            Assert.IsEmpty(_lists, "断线前在途回包不得改状态");
            Assert.IsEmpty(_replays, "断线前在途回放不得交付");
            Assert.IsEmpty(_errors);
        }

        [Test]
        public void ReplayCache_ServesRepeatFetchWithoutExtraRpc_AndLogsCorruptDecode()
        {
            Assert.IsTrue(_client.FetchReplay(601));
            GetCalls[0].Respond(new Match.GetBattleReportResponse { Replay = ValidGzipEnvelope(601) });
            Assert.AreEqual(1, _replays.Count);

            // 命中缓存不再发网络请求
            Assert.IsTrue(_client.FetchReplay(601));
            Assert.AreEqual(1, GetCalls.Count);
            Assert.AreEqual(2, _replays.Count);

            // 解码失败记带 battle_id 的日志并报错误
            Assert.IsTrue(_client.FetchReplay(602));
            GetCalls[1].Respond(new Match.GetBattleReportResponse
            {
                Replay = new BattleReplayEnvelope { FormatVersion = 1, Codec = eBattleReplayCodec.BattleReplayCodecGzip, RawBytes = 10, Payload = ByteString.CopyFrom(new byte[] { 1, 2 }) },
            });
            Assert.AreEqual(1, _errors.Count);
            Assert.AreEqual(BattleReplayDecoder.CorruptReplayMessage, _errors[0]);
            Assert.That(_logs.Count, Is.GreaterThanOrEqualTo(1));
            StringAssert.Contains("602", _logs[0]);
        }

        [Test]
        public void CanWatchReplayFromRecovered_OnlyAllowsNonVoidPvpWithValidBattleId()
        {
            var pvp = new PlayerBattleResult { BattleId = 10, MatchMode = (uint)Match.MatchMode._1V1, Outcome = (uint)eBattleOutcome.BattleOutcomeSideAWin };
            var challenge = new PlayerBattleResult { BattleId = 11, MatchMode = (uint)Match.MatchMode.PvpChallenge, Outcome = (uint)eBattleOutcome.BattleOutcomeDraw };
            var tournament = new PlayerBattleResult { BattleId = 14, MatchMode = (uint)Match.MatchMode.SeasonTournament, Outcome = (uint)eBattleOutcome.BattleOutcomeSideAWin };
            var pve = new PlayerBattleResult { BattleId = 12, MatchMode = (uint)Match.MatchMode.PveSolo, Outcome = (uint)eBattleOutcome.BattleOutcomeSideAWin };
            var voided = new PlayerBattleResult { BattleId = 13, MatchMode = (uint)Match.MatchMode._1V1, Outcome = (uint)eBattleOutcome.BattleOutcomeVoid };
            var zeroId = new PlayerBattleResult { BattleId = 0, MatchMode = (uint)Match.MatchMode._1V1, Outcome = (uint)eBattleOutcome.BattleOutcomeSideAWin };

            Assert.IsTrue(BattleReportClient.CanWatchReplayFromRecovered(pvp));
            Assert.IsTrue(BattleReportClient.CanWatchReplayFromRecovered(challenge));
            Assert.IsTrue(BattleReportClient.CanWatchReplayFromRecovered(tournament));
            Assert.AreEqual("赛季锦标赛", BattleReportClient.ModeName(Match.MatchMode.SeasonTournament));
            Assert.IsFalse(BattleReportClient.CanWatchReplayFromRecovered(pve));
            Assert.IsFalse(BattleReportClient.CanWatchReplayFromRecovered(voided));
            Assert.IsFalse(BattleReportClient.CanWatchReplayFromRecovered(zeroId));
            Assert.IsFalse(BattleReportClient.CanWatchReplayFromRecovered(null));
        }

        [Test]
        public void MismatchedReplayBattleId_IsRejectedWithoutCaching()
        {
            Assert.IsTrue(_client.FetchReplay(701));
            GetCalls[0].Respond(new Match.GetBattleReportResponse { Replay = ValidGzipEnvelope(999) });

            Assert.IsEmpty(_replays, "battle_id 不匹配的回放不得交付播放");
            Assert.AreEqual(1, _errors.Count);
            Assert.AreEqual(BattleReplayDecoder.CorruptReplayMessage, _errors[0]);
            Assert.That(_logs.Count, Is.GreaterThanOrEqualTo(1));
            StringAssert.Contains("701", _logs[0]);
            StringAssert.Contains("999", _logs[0]);

            // 未写入缓存:再次拉取必须重新发起 RPC
            Assert.IsTrue(_client.FetchReplay(701));
            Assert.AreEqual(2, GetCalls.Count);
        }

        [Test]
        public void ClassName_MapsKnownUnknownAndZeroClassIds()
        {
            Assert.AreEqual(string.Empty, BattleReportClient.ClassName(0));
            Assert.AreEqual("破军", BattleReportClient.ClassName(1));
            Assert.AreEqual("玄霄", BattleReportClient.ClassName(2));
            Assert.AreEqual("丹心", BattleReportClient.ClassName(3));
            Assert.AreEqual("逐风", BattleReportClient.ClassName(4));
            Assert.AreEqual("职业#99", BattleReportClient.ClassName(99));
        }
    }
}
