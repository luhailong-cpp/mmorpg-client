using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// BattleClient 二期扩展测试:自动战斗记忆补发(§11.2)/ 权威 is_auto 事件 /
    /// 连续战斗(Ended→AckBattleEnd 后按记忆重新入队)。
    /// 依赖生成产物:MessageIds.SetAutoBattle 与 SetAutoBattleRequest/Response,
    /// 未生成前不编译(与 BattleClientStateMachineTests 同一约定)。
    /// </summary>
    public sealed class BattleClientAutoBattleTests
    {
        private const ulong MyId = 1001;
        private const ulong EnemyId = 2002;
        private const ulong TheBattleId = 7700;

        private FakeBattleTransport _net;
        private BattleClient _client;
        private List<bool> _autoChanges;
        private List<string> _errors;

        [SetUp]
        public void SetUp()
        {
            _net = new FakeBattleTransport { PlayerId = MyId };
            _client = new BattleClient(_net); // 直接 new,不经 Attach,避免污染单例
            _autoChanges = new List<bool>();
            _errors = new List<string>();
            _client.OnAutoStateChanged += v => _autoChanges.Add(v);
            _client.OnError += e => _errors.Add(e);
        }

        // ── 工具 ────────────────────────────────────────────

        /// <summary>带 actors 的权威状态(myAuto 控制本人 is_auto)。</summary>
        private static BattleStateS2C MakeState(bool myAuto, params ulong[] pending)
        {
            var state = new BattleStateS2C
            {
                BattleId = TheBattleId,
                RoundIndex = 1,
                Outcome = eBattleOutcome.BattleOutcomeOngoing,
            };
            state.PendingActorIds.AddRange(pending);
            state.Actors.Add(new BattleActorState { ActorId = MyId, TeamIndex = 0, IsAuto = myAuto });
            state.Actors.Add(new BattleActorState { ActorId = EnemyId, TeamIndex = 1 });
            return state;
        }

        private void PushBattleStart(bool myAuto = false)
            => _net.PushNotify(MessageIds.NotifyBattleStart, new BattleStartS2C
            {
                BattleId = TheBattleId,
                State = MakeState(myAuto, MyId, EnemyId),
            });

        private void PushTurnResult(bool myAuto)
            => _net.PushNotify(MessageIds.NotifyTurnResult, new TurnResultS2C
            {
                BattleId = TheBattleId,
                RoundIndex = 1,
                State = MakeState(myAuto, MyId, EnemyId),
            });

        private void PushBattleEnd()
            => _net.PushNotify(MessageIds.NotifyBattleEnd, new BattleEndS2C
            {
                BattleId = TheBattleId,
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
            });

        // ── 自动战斗 ────────────────────────────────────────

        [Test]
        public void SetAutoBattle_OutOfBattle_OnlyLatches_ThenResendsOnBattleStart()
        {
            _client.SetAutoBattle(true); // 战斗外:纯本地记忆
            Assert.That(_client.AutoBattleLatched, Is.True);
            Assert.That(_net.CallsOf(MessageIds.SetAutoBattle), Is.Empty, "战斗外不发网络请求");
            Assert.That(_errors, Is.Empty);

            PushBattleStart();

            var calls = _net.CallsOf(MessageIds.SetAutoBattle);
            Assert.That(calls, Has.Count.EqualTo(1), "记忆开着 → BattleStart 后自动补发");
            var req = (SetAutoBattleRequest)calls[0].Request;
            Assert.That(req.BattleId, Is.EqualTo(TheBattleId));
            Assert.That(req.Enabled, Is.True);
        }

        [Test]
        public void SetAutoBattle_InBattle_SendsImmediatelyAndUpdatesLatch()
        {
            PushBattleStart();
            Assert.That(_net.CallsOf(MessageIds.SetAutoBattle), Is.Empty, "未开记忆不自动补发");

            _client.SetAutoBattle(true);
            var calls = _net.CallsOf(MessageIds.SetAutoBattle);
            Assert.That(calls, Has.Count.EqualTo(1));
            Assert.That(((SetAutoBattleRequest)calls[0].Request).Enabled, Is.True);
            Assert.That(_client.AutoBattleLatched, Is.True);

            _client.SetAutoBattle(false); // 战斗中关闭:立即发且清记忆
            calls = _net.CallsOf(MessageIds.SetAutoBattle);
            Assert.That(calls, Has.Count.EqualTo(2));
            Assert.That(((SetAutoBattleRequest)calls[1].Request).Enabled, Is.False);
            Assert.That(_client.AutoBattleLatched, Is.False);
        }

        [Test]
        public void SetAutoBattle_ErrorTip_ReportsViaOnError()
        {
            PushBattleStart();
            _client.SetAutoBattle(true);
            _net.CallsOf(MessageIds.SetAutoBattle)[0].Respond(new SetAutoBattleResponse
            {
                ErrorMessage = new TipInfoMessage { Id = 42 },
            });

            Assert.That(_errors, Has.Count.EqualTo(1));
            Assert.That(_errors[0], Does.Contain("42"));
        }

        [Test]
        public void SetAutoBattle_ErrorTip_RevertsLatchToAuthoritative()
        {
            PushBattleStart(); // 权威 is_auto = false
            _client.SetAutoBattle(true);
            Assert.That(_client.AutoBattleLatched, Is.True);

            _net.CallsOf(MessageIds.SetAutoBattle)[0].Respond(new SetAutoBattleResponse
            {
                ErrorMessage = new TipInfoMessage { Id = 42 },
            });

            Assert.That(_client.AutoBattleLatched, Is.False,
                "请求失败 → 本地意愿回滚到权威值,免得用户得点两次");
            Assert.That(_errors, Has.Count.EqualTo(1));
        }

        [Test]
        public void SetAutoBattle_TransportError_RevertsLatchToAuthoritative()
        {
            PushBattleStart();
            _client.SetAutoBattle(true);
            Assert.That(_client.AutoBattleLatched, Is.True);

            _net.CallsOf(MessageIds.SetAutoBattle)[0].FailWith("timeout");

            Assert.That(_client.AutoBattleLatched, Is.False, "传输层失败同样回滚");
            Assert.That(_errors, Has.Count.EqualTo(1));
        }

        [Test]
        public void BattleStartResend_Failure_KeepsLatch()
        {
            _client.SetAutoBattle(true); // 战斗外预设(跨场挂机记忆)
            PushBattleStart();           // 记忆开着 → 自动补发

            _net.CallsOf(MessageIds.SetAutoBattle)[0].Respond(new SetAutoBattleResponse
            {
                ErrorMessage = new TipInfoMessage { Id = 42 },
            });

            Assert.That(_client.AutoBattleLatched, Is.True,
                "补发瞬时失败不抹掉跨场挂机记忆(§11.2 连续挂机语义)");
        }

        [Test]
        public void OnAutoStateChanged_FollowsAuthoritativeState()
        {
            PushBattleStart(myAuto: false);
            Assert.That(_autoChanges, Is.Empty, "初始 false → 不误报");
            Assert.That(_client.IsMyActorAuto, Is.False);

            PushTurnResult(myAuto: true); // 服务端确认挂机
            Assert.That(_autoChanges, Is.EqualTo(new[] { true }));
            Assert.That(_client.IsMyActorAuto, Is.True);

            PushTurnResult(myAuto: true); // 同值不重复抛
            Assert.That(_autoChanges, Has.Count.EqualTo(1));

            PushBattleEnd(); // 战斗收尾:权威 auto 归 false
            Assert.That(_autoChanges, Is.EqualTo(new[] { true, false }));
            Assert.That(_client.IsMyActorAuto, Is.False);
        }

        // ── 连续战斗 ────────────────────────────────────────

        [Test]
        public void ContinuousBattle_RejoinsWithLastQueueParamsAfterAck()
        {
            _client.ContinuousBattle = true;
            _client.Tick(0);
            _client.JoinQueue(Match.MatchMode.PveSolo, 7);
            PushBattleStart();
            PushBattleEnd();

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None), "既有 Ended→None 流转不变");
            Assert.That(_net.CallsOf(MessageIds.JoinQueue), Has.Count.EqualTo(1), "Ack 前不自动入队");

            _client.AckBattleEnd(); // UI 收起结算面板

            var joins = _net.CallsOf(MessageIds.JoinQueue);
            Assert.That(joins, Has.Count.EqualTo(2), "Ack 后按记忆重新入队");
            var req = (Match.JoinQueueRequest)joins[1].Request;
            Assert.That(req.Mode, Is.EqualTo(Match.MatchMode.PveSolo));
            Assert.That(req.BattleConfigId, Is.EqualTo(7U));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued));

            _client.AckBattleEnd(); // 重复 Ack 丢弃
            Assert.That(_net.CallsOf(MessageIds.JoinQueue), Has.Count.EqualTo(2));
        }

        [Test]
        public void ContinuousBattle_Off_AckDoesNothing()
        {
            _client.Tick(0);
            _client.JoinQueue(Match.MatchMode.PveSolo, 7);
            PushBattleStart();
            PushBattleEnd();

            _client.AckBattleEnd();

            Assert.That(_net.CallsOf(MessageIds.JoinQueue), Has.Count.EqualTo(1));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));
        }

        [Test]
        public void AckBattleEnd_WithoutPrecedingEnd_IsIgnored()
        {
            _client.ContinuousBattle = true;
            _client.Tick(0);
            _client.JoinQueue(Match.MatchMode.PveSolo, 7);

            _client.AckBattleEnd(); // 排队中误 Ack

            Assert.That(_net.CallsOf(MessageIds.JoinQueue), Has.Count.EqualTo(1));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued));
        }

        [Test]
        public void Disconnect_VoidsPendingEndAck()
        {
            _client.ContinuousBattle = true;
            _client.Tick(0);
            _client.JoinQueue(Match.MatchMode.PveSolo, 7);
            PushBattleStart();
            PushBattleEnd();

            _net.RaiseDisconnected(); // 断线作废未消费的结束 Ack
            _client.AckBattleEnd();

            Assert.That(_net.CallsOf(MessageIds.JoinQueue), Has.Count.EqualTo(1),
                "断线后不允许凭旧 Ack 自动入队");
        }

        [Test]
        public void ContinuousBattle_ChallengeWithoutQueueMemory_DoesNotRejoin()
        {
            _client.ContinuousBattle = true;
            PushBattleStart(); // 切磋成局:从未 JoinQueue
            PushBattleEnd();

            _client.AckBattleEnd();

            Assert.That(_net.CallsOf(MessageIds.JoinQueue), Is.Empty,
                "无排队参数记忆 → 连续战斗不生效");
        }
    }
}
