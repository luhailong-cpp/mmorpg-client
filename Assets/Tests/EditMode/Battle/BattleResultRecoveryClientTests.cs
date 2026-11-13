using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 近期战斗结果补查(服务端 docs/design/battle-result-recovery.md §7,第 6 条列的情形逐条覆盖):
    /// 假传输驱动,不连网络、不依赖 Unity 运行时;时钟、连接身份、是否在游戏里都由用例控制。
    /// </summary>
    public sealed class BattleResultRecoveryClientTests
    {
        private FakeBattleTransport _net;
        private BattleResultRecoveryClient _client;
        private object _connection;
        private bool _inGame;
        private float _now;
        private List<PlayerBattleResult> _raised;

        [SetUp]
        public void SetUp()
        {
            _net = new FakeBattleTransport();
            _connection = new object();
            _inGame = true;
            _now = 100f;
            _raised = new List<PlayerBattleResult>();
            _client = new BattleResultRecoveryClient(_net, () => _connection, () => _inGame, () => _now);
            _client.OnRecoveredResult += result => _raised.Add(result);
        }

        [TearDown]
        public void TearDown() => _client.Dispose();

        private List<FakeBattleTransport.RecordedCall> Queries =>
            _net.CallsOf(SceneBattleResultClientPlayerRpc.GetRecentBattleResultsMessageId);

        private List<FakeBattleTransport.RecordedCall> Acks =>
            _net.CallsOf(SceneBattleResultClientPlayerRpc.AckBattleResultsMessageId);

        private ulong[] RaisedIds => _raised.Select(result => result.BattleId).ToArray();

        private static ulong[] AckedIds(FakeBattleTransport.RecordedCall call) =>
            ((AckBattleResultsRequest)call.Request).BattleIds.ToArray();

        private static PlayerBattleResult Result(ulong battleId, ulong resultSeq, bool seen = false) => new PlayerBattleResult
        {
            BattleId = battleId,
            ResultSeq = resultSeq,
            Seen = seen,
            Outcome = (uint)eBattleOutcome.BattleOutcomeSideAWin,
        };

        private static GetRecentBattleResultsResponse Reply(params PlayerBattleResult[] results)
        {
            var reply = new GetRecentBattleResultsResponse();
            reply.Results.AddRange(results);
            return reply;
        }

        private static GetRecentBattleResultsResponse Refused(uint tip) =>
            new GetRecentBattleResultsResponse { ErrorMessage = new TipInfoMessage { Id = tip } };

        // 重登 / 换连接:连接身份变了就是一次新的落地。
        private void Reland() => _connection = new object();

        [Test]
        public void LandingQueriesOnceAndRaisesUnseenResultsOldestFirst()
        {
            _client.Tick();
            Assert.AreEqual(1, Queries.Count, "落地查一次");
            _client.Tick();
            Assert.AreEqual(1, Queries.Count, "在途不重发");

            Queries[0].Respond(Reply(Result(7, 2), Result(5, 1), Result(9, 3, seen: true)));

            CollectionAssert.AreEqual(new ulong[] { 5, 7 }, RaisedIds, "只交出没看过的,按 result_seq 从小到大");
            _client.Tick();
            Assert.AreEqual(1, Queries.Count, "同一次落地查过就不再查");
            Assert.AreEqual(0, Acks.Count, "界面展示之前不确认");
        }

        [Test]
        public void NothingIsQueriedBeforeEnteringTheSceneOrWhileTheTransportIsDown()
        {
            _inGame = false;
            _client.Tick();
            _inGame = true;
            _net.IsReady = false;
            _client.Tick();
            Assert.AreEqual(0, Queries.Count, "没进场(NotifyEnterScene 之前)或传输没就绪都不查");

            _net.IsReady = true;
            _client.Tick();
            Assert.AreEqual(1, Queries.Count);
        }

        [Test]
        public void ShownResultsAreAcknowledgedInBatchesOfTen()
        {
            _client.Tick();
            Queries[0].Respond(Reply());
            for (ulong id = 1; id <= 12; id++) _client.NoteShown(id);
            _client.NoteShown(3); // 重复展示无副作用

            Assert.AreEqual(1, Acks.Count, "一次只有一个确认在途");
            CollectionAssert.AreEqual(new ulong[] { 1 }, AckedIds(Acks[0]));
            Acks[0].Respond(new AckBattleResultsResponse());
            CollectionAssert.AreEqual(Enumerable.Range(2, 10).Select(i => (ulong)i).ToArray(), AckedIds(Acks[1]),
                "一次至多 10 个");
            Acks[1].Respond(new AckBattleResultsResponse());
            CollectionAssert.AreEqual(new ulong[] { 12 }, AckedIds(Acks[2]));
            Acks[2].Respond(new AckBattleResultsResponse());

            Assert.AreEqual(3, Acks.Count);
            Assert.AreEqual(0, _client.PendingAckCount);
        }

        [Test]
        public void ResultShownLiveIsAcknowledgedAndNotRaisedAgain()
        {
            _client.Tick();
            _client.NoteShown(5); // 有上下文的现场结算面板先弹了这一局
            Queries[0].Respond(Reply(Result(5, 1)));

            Assert.IsEmpty(_raised, "现场展示过的不再补弹");
            Assert.AreEqual(1, Acks.Count);
            CollectionAssert.AreEqual(new ulong[] { 5 }, AckedIds(Acks[0]));
        }

        [Test]
        public void EndWithoutContextQueriesOnceMoreAfterTheQueryInFlight()
        {
            _client.Tick();
            _client.HandleEndWithoutContext(8, alreadyNotified: false);
            _client.HandleEndWithoutContext(8, alreadyNotified: false);
            _client.Tick();
            Assert.AreEqual(1, Queries.Count, "在途时再来的触发不并发");

            Queries[0].Respond(Reply());
            _client.Tick();
            Assert.AreEqual(2, Queries.Count, "回来之后只多查一次");
            Queries[1].Respond(Reply(Result(8, 1)));
            CollectionAssert.AreEqual(new ulong[] { 8 }, RaisedIds);
            _client.Tick();
            Assert.AreEqual(2, Queries.Count);

            _client.HandleEndWithoutContext(9, alreadyNotified: false);
            _client.Tick();
            Assert.AreEqual(3, Queries.Count, "空闲时收到没有上下文的终局,下一拍就查");
        }

        [Test]
        public void NotifiedVoidEndIsAcknowledgedAndNotRaised()
        {
            _client.Tick();
            Queries[0].Respond(Reply());

            _client.HandleEndWithoutContext(11, alreadyNotified: true);

            CollectionAssert.AreEqual(new ulong[] { 11 }, AckedIds(Acks[0]), "作废提示视同展示过,直接确认");
            _client.Tick();
            var voided = Result(11, 1);
            voided.Outcome = (uint)eBattleOutcome.BattleOutcomeVoid;
            Queries[1].Respond(Reply(voided));
            Assert.IsEmpty(_raised, "补查时不再为它弹面板");
        }

        [Test]
        public void FailedQueriesBackOff15Then30Then60Seconds()
        {
            _client.Tick();
            Queries[0].FailWith("timeout");      // t=100,第 1 次失败:115 再查
            _now = 114f;
            _client.Tick();
            Assert.AreEqual(1, Queries.Count);
            _now = 115f;
            _client.Tick();
            Assert.AreEqual(2, Queries.Count, "15 秒后重试");

            Queries[1].Respond(Refused(1003));   // 回包带 tip 同样算失败,第 2 次:145 再查
            _now = 144f;
            _client.Tick();
            Assert.AreEqual(2, Queries.Count);
            _now = 145f;
            _client.Tick();
            Assert.AreEqual(3, Queries.Count, "第 2 次失败等 30 秒");

            Queries[2].FailWith("timeout");      // 第 3 次:205 再查
            _now = 204f;
            _client.Tick();
            Assert.AreEqual(3, Queries.Count);
            _now = 205f;
            _client.Tick();
            Assert.AreEqual(4, Queries.Count, "第 3 次失败等 60 秒");

            Queries[3].FailWith("timeout");      // 封顶:265 再查
            _now = 264f;
            _client.Tick();
            Assert.AreEqual(4, Queries.Count);
            _now = 265f;
            _client.Tick();
            Assert.AreEqual(5, Queries.Count, "封顶 60 秒");

            Queries[4].Respond(Reply(Result(1, 1)));
            CollectionAssert.AreEqual(new ulong[] { 1 }, RaisedIds, "成功之后照常交出结果");
        }

        [Test]
        public void FailedAcknowledgementIsResentFirstOnTheNextLanding()
        {
            _client.Tick();
            Queries[0].Respond(Reply());
            _client.NoteShown(21);
            Acks[0].FailWith("disconnected");
            Assert.AreEqual(1, _client.PendingAckCount, "失败不丢,留着");

            Reland();
            _client.Tick();

            Assert.AreEqual(2, Acks.Count, "下次落地先补发确认");
            CollectionAssert.AreEqual(new ulong[] { 21 }, AckedIds(Acks[1]));
            Assert.AreEqual(2, Queries.Count, "再查");
            Assert.Less(_net.Calls.IndexOf(Acks[1]), _net.Calls.IndexOf(Queries[1]), "确认排在查询之前");
        }

        [Test]
        public void RefusedAcknowledgementIsKeptForTheNextLanding()
        {
            _client.Tick();
            Queries[0].Respond(Reply());
            _client.NoteShown(22);
            Acks[0].Respond(new AckBattleResultsResponse { ErrorMessage = new TipInfoMessage { Id = 1006 } });

            Assert.AreEqual(1, _client.PendingAckCount, "1006(冻结 / 交接中)不重弹,留到下次落地");
            Assert.IsEmpty(_raised);
        }

        [Test]
        public void RepliesFromAnEarlierLandingAreIgnored()
        {
            _client.Tick();
            Reland();
            _client.Tick();
            Assert.AreEqual(2, Queries.Count, "重登是新的一次落地");

            Queries[0].Respond(Reply(Result(31, 1)));
            Assert.IsEmpty(_raised, "上一次落地的回包不再改状态");
            Queries[1].Respond(Reply(Result(31, 1)));
            CollectionAssert.AreEqual(new ulong[] { 31 }, RaisedIds);
        }

        [Test]
        public void RaisedButNeverShownResultIsRaisedAgainOnTheNextLanding()
        {
            _client.Tick();
            Queries[0].Respond(Reply(Result(41, 1))); // 界面没来得及弹(比如断线),没调 NoteShown
            Reland();
            _client.Tick();
            Queries[1].Respond(Reply(Result(41, 1)));

            CollectionAssert.AreEqual(new ulong[] { 41, 41 }, RaisedIds, "没展示过的,下次落地再交一次");
            Assert.AreEqual(0, Acks.Count);
        }

        [Test]
        public void ResultTheServerAlreadyMarkedSeenDropsThePendingAcknowledgement()
        {
            _client.Tick();
            Queries[0].Respond(Reply());
            _client.NoteShown(51);
            Acks[0].FailWith("timeout"); // 其实服务端已经记下了
            Reland();
            _client.Tick();               // 落地:先补发确认(在途),再查
            Queries[1].Respond(Reply(Result(51, 1, seen: true)));

            Assert.AreEqual(0, _client.PendingAckCount, "服务端说已读,本地不再补发");
            Assert.IsEmpty(_raised);
        }

        [Test]
        public void VoidResultsAreRecognised()
        {
            var voided = Result(1, 1);
            voided.Outcome = (uint)eBattleOutcome.BattleOutcomeVoid;
            Assert.IsTrue(BattleResultRecoveryClient.IsVoid(voided));
            Assert.IsFalse(BattleResultRecoveryClient.IsVoid(Result(2, 2)));
            Assert.IsFalse(BattleResultRecoveryClient.IsVoid(null));
        }
    }
}
