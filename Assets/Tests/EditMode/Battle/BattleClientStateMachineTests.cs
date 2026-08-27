using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// BattleClient 状态机纯逻辑测试(相位转换 / 迟到消息丢弃 / 重连补拉决策 /
    /// 排队轮询节奏),网络依赖经 <see cref="FakeBattleTransport"/> 注入。
    /// 依赖生成产物:MessageIds 新常量(tools/gen_messageids.ps1 重跑)与
    /// battle/match 的 C# proto 类(tools/gen_proto.ps1 重跑),未生成前不编译。
    /// </summary>
    public sealed class BattleClientStateMachineTests
    {
        private const ulong MyId = 1001;
        private const ulong EnemyId = 2002;
        private const ulong TheBattleId = 7700;

        private FakeBattleTransport _net;
        private BattleClient _client;
        private List<BattlePhase> _phases;
        private List<string> _errors;
        private int _starts;
        private int _turnResults;
        private int _ends;

        [SetUp]
        public void SetUp()
        {
            _net = new FakeBattleTransport { PlayerId = MyId };
            _client = new BattleClient(_net); // 直接 new,不经 Attach,避免污染单例
            _phases = new List<BattlePhase>();
            _errors = new List<string>();
            _starts = _turnResults = _ends = 0;
            _client.OnPhaseChanged += p => _phases.Add(p);
            _client.OnError += e => _errors.Add(e);
            _client.OnBattleStart += _ => _starts++;
            _client.OnTurnResult += _ => _turnResults++;
            _client.OnBattleEnd += _ => _ends++;
        }

        // ── 工具 ────────────────────────────────────────────

        private static BattleStateS2C MakeState(eBattleOutcome outcome, params ulong[] pending)
        {
            var state = new BattleStateS2C
            {
                BattleId = TheBattleId,
                RoundIndex = 1,
                Outcome = outcome,
            };
            state.PendingActorIds.AddRange(pending);
            return state;
        }

        /// <summary>直接推开战(默认双方都待行动 → 本人 WaitingAction)。</summary>
        private void PushBattleStart()
        {
            _net.PushNotify(MessageIds.NotifyBattleStart, new BattleStartS2C
            {
                BattleId = TheBattleId,
                State = MakeState(eBattleOutcome.BattleOutcomeOngoing, MyId, EnemyId),
            });
        }

        private void PushTurnResult(ulong battleId, params ulong[] nextPending)
        {
            _net.PushNotify(MessageIds.NotifyTurnResult, new TurnResultS2C
            {
                BattleId = battleId,
                RoundIndex = 1,
                State = MakeState(eBattleOutcome.BattleOutcomeOngoing, nextPending),
            });
        }

        // ── 排队与轮询 ──────────────────────────────────────

        [Test]
        public void JoinQueue_EntersQueuedAndPollsEveryThreeSeconds()
        {
            _client.Tick(0);
            _client.JoinQueue(Match.MatchMode.PveSolo, 1);

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued));
            Assert.That(_net.CallsOf(MessageIds.JoinQueue), Has.Count.EqualTo(1));

            _net.CallsOf(MessageIds.JoinQueue)[0]
                .Respond(new Match.JoinQueueResponse { QueueTicket = "t1" });
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued), "成功响应不改相位");

            _client.Tick(2.9);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Is.Empty, "3s 前不轮询");

            _client.Tick(3.0);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Has.Count.EqualTo(1));

            _net.CallsOf(MessageIds.GetQueueStatus)[0]
                .Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.Queued });
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued));

            _client.Tick(6.0);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Has.Count.EqualTo(2), "每 3s 轮询一次");
        }

        [Test]
        public void QueuePoll_PausesWhileTransportNotReady()
        {
            _client.Tick(0);
            _client.JoinQueue(Match.MatchMode.PveSolo, 1);

            _net.IsReady = false;
            _client.Tick(3.5);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Is.Empty, "断连期间暂停轮询");

            _net.IsReady = true;
            _client.Tick(4.0);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Has.Count.EqualTo(1));
        }

        [Test]
        public void QueuePoll_MatchedStopsPollingAndEntersPreparing()
        {
            _client.Tick(0);
            _client.JoinQueue(Match.MatchMode.PveSolo, 1);
            _client.Tick(3.0);
            _net.CallsOf(MessageIds.GetQueueStatus)[0]
                .Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.Matched });

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Preparing));

            _client.Tick(6.1);
            _client.Tick(9.2);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Has.Count.EqualTo(1),
                "MATCHED 后停止轮询");
        }

        [Test]
        public void QueuePoll_NotQueuedConvergesToNone()
        {
            _client.Tick(0);
            _client.JoinQueue(Match.MatchMode.PveSolo, 1);
            _client.Tick(3.0);
            _net.CallsOf(MessageIds.GetQueueStatus)[0]
                .Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.NotQueued });

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None), "服务端已丢弃排队 → 收敛回 None");
        }

        [Test]
        public void JoinQueue_ErrorTipReportsAndReturnsNone()
        {
            _client.JoinQueue(Match.MatchMode.PveSolo, 1);
            _net.CallsOf(MessageIds.JoinQueue)[0].Respond(new Match.JoinQueueResponse
            {
                ErrorCode = 1,
                ErrorMessage = new TipInfoMessage { Id = 42 },
            });

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));
            Assert.That(_errors, Has.Count.EqualTo(1));
            Assert.That(_errors[0], Does.Contain("42"), "TipInfoMessage 统一走 OnError");
        }

        [Test]
        public void CancelQueue_ConvergesToNoneAndStopsPolling()
        {
            _client.Tick(0);
            _client.JoinQueue(Match.MatchMode.PveSolo, 1);
            _client.CancelQueue();
            _net.CallsOf(MessageIds.CancelQueue)[0].Respond(new Empty());

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));

            _client.Tick(3.5);
            Assert.That(_net.CallsOf(MessageIds.GetQueueStatus), Is.Empty, "取消后不再轮询");
        }

        [Test]
        public void PreparingTimeout_ReturnsNoneWithError()
        {
            _client.Tick(0);
            _client.JoinQueue(Match.MatchMode.PveSolo, 1);
            _client.Tick(3.0);
            _net.CallsOf(MessageIds.GetQueueStatus)[0]
                .Respond(new Match.GetQueueStatusResponse { State = Match.QueueState.Matched });
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Preparing));

            _client.Tick(3.0 + BattleClient.PreparingTimeoutSeconds - 0.1);
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Preparing), "未到期不超时");

            _client.Tick(3.0 + BattleClient.PreparingTimeoutSeconds);
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None), "gather 无下文 → 超时收敛");
            Assert.That(_errors, Is.Not.Empty);
        }

        // ── 开战 ────────────────────────────────────────────

        [Test]
        public void BattleStart_WhileQueued_TransitionsPreparingThenWaitingAction()
        {
            _client.JoinQueue(Match.MatchMode.PveSolo, 1);
            _phases.Clear();

            PushBattleStart(); // 排队中直接收到开战(solo 即配)

            Assert.That(_phases, Is.EqualTo(new[] { BattlePhase.Preparing, BattlePhase.WaitingAction }),
                "Queued → Preparing → WaitingAction 连跳");
            Assert.That(_starts, Is.EqualTo(1));
            Assert.That(_client.State, Is.Not.Null);

            // 开战后才回来的 JoinQueue 迟到响应:不得把相位拉回 Queued
            _net.CallsOf(MessageIds.JoinQueue)[0]
                .Respond(new Match.JoinQueueResponse { QueueTicket = "late" });
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction));
        }

        [Test]
        public void BattleStart_FromIdle_ChallengeFlowEntersWaitingAction()
        {
            PushBattleStart(); // 切磋成局:未经排队直接开战

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction));
            Assert.That(_starts, Is.EqualTo(1));
        }

        // ── 回合循环 ────────────────────────────────────────

        [Test]
        public void TurnResult_EntersResolving_AckReturnsWaitingAction()
        {
            PushBattleStart();
            PushTurnResult(TheBattleId, MyId, EnemyId);

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Resolving));
            Assert.That(_turnResults, Is.EqualTo(1));

            _client.AckTurnPlayed(); // UI 播完表现
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction));
        }

        [Test]
        public void SubmitAction_OnlyAllowedWhileWaitingAction()
        {
            _client.SubmitAction(new BattleAction { ActionType = eBattleActionType.BattleActionAttack });
            Assert.That(_net.CallsOf(MessageIds.SubmitBattleAction), Is.Empty, "非 WaitingAction 不发送");
            Assert.That(_errors, Is.Not.Empty);

            PushBattleStart();
            _client.SubmitAction(new BattleAction
            {
                ActionType = eBattleActionType.BattleActionAttack,
                TargetId = EnemyId,
            });
            var calls = _net.CallsOf(MessageIds.SubmitBattleAction);
            Assert.That(calls, Has.Count.EqualTo(1));
            Assert.That(((SubmitBattleActionRequest)calls[0].Request).BattleId, Is.EqualTo(TheBattleId));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction), "提交成功前后都不改相位");
        }

        [Test]
        public void AckTurnPlayed_OutsideResolving_IsIgnored()
        {
            PushBattleStart();
            _client.AckTurnPlayed(); // WaitingAction 下的误 Ack
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction));
        }

        // ── 结束与迟到消息丢弃 ──────────────────────────────

        [Test]
        public void BattleEnd_EndedThenNone_LateTurnResultDiscarded()
        {
            PushBattleStart();
            _phases.Clear();

            _net.PushNotify(MessageIds.NotifyBattleEnd, new BattleEndS2C
            {
                BattleId = TheBattleId,
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
            });

            Assert.That(_ends, Is.EqualTo(1));
            Assert.That(_phases, Is.EqualTo(new[] { BattlePhase.Ended, BattlePhase.None }),
                "Ended 收尾后回 None");

            PushTurnResult(TheBattleId, MyId); // Ended 之后的迟到回合结果
            Assert.That(_turnResults, Is.EqualTo(0), "迟到 TurnResult 丢弃");
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));
        }

        [Test]
        public void TurnResult_WrongBattleId_IsDiscarded()
        {
            PushBattleStart();
            PushTurnResult(9999, MyId); // 其它战斗的错发消息

            Assert.That(_turnResults, Is.EqualTo(0));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction));
        }

        [Test]
        public void BattleEnd_WithoutBattleContext_IsDiscarded()
        {
            _net.PushNotify(MessageIds.NotifyBattleEnd, new BattleEndS2C
            {
                BattleId = TheBattleId,
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
            });

            Assert.That(_ends, Is.EqualTo(0), "无战斗上下文的结束消息丢弃");
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));
        }

        // ── 断线与重连补拉 ──────────────────────────────────

        [Test]
        public void Disconnect_ResetsToNone_AndDropsLateMessages()
        {
            PushBattleStart();
            _net.RaiseDisconnected();

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));
            Assert.That(_client.State, Is.Null, "断线后本地权威状态作废");

            PushTurnResult(TheBattleId, MyId);
            Assert.That(_turnResults, Is.EqualTo(0), "断线后的迟到消息丢弃");
        }

        [Test]
        public void Reconnect_NotifyTriggersStatePull_PhaseFollowsAuthoritativeState()
        {
            _net.PushNotify(MessageIds.NotifyBattleReconnect,
                new BattleReconnectS2C { BattleId = TheBattleId });

            var pulls = _net.CallsOf(MessageIds.GetBattleState);
            Assert.That(pulls, Has.Count.EqualTo(1), "NotifyBattleReconnect → 自动 RequestState");
            Assert.That(((GetBattleStateRequest)pulls[0].Request).BattleId, Is.EqualTo(TheBattleId));

            pulls[0].Respond(MakeState(eBattleOutcome.BattleOutcomeOngoing, MyId, EnemyId));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction), "本人待行动 → WaitingAction");
            Assert.That(_client.State, Is.Not.Null);
        }

        [Test]
        public void Reconnect_AlreadySubmitted_EntersResolving()
        {
            _net.PushNotify(MessageIds.NotifyBattleReconnect,
                new BattleReconnectS2C { BattleId = TheBattleId });
            _net.CallsOf(MessageIds.GetBattleState)[0]
                .Respond(MakeState(eBattleOutcome.BattleOutcomeOngoing, EnemyId)); // 本人不在 pending

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Resolving));
        }

        [Test]
        public void Reconnect_BattleAlreadyFinished_ConvergesToNone()
        {
            _net.PushNotify(MessageIds.NotifyBattleReconnect,
                new BattleReconnectS2C { BattleId = TheBattleId });
            _net.CallsOf(MessageIds.GetBattleState)[0]
                .Respond(MakeState(eBattleOutcome.BattleOutcomeSideBWin));

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None), "战斗已出胜负 → 收敛回 None");
        }

        [Test]
        public void DecidePhaseFromState_PureDecisionTable()
        {
            Assert.That(BattleClient.DecidePhaseFromState(null, MyId),
                Is.EqualTo(BattlePhase.None), "空状态");
            Assert.That(
                BattleClient.DecidePhaseFromState(MakeState(eBattleOutcome.BattleOutcomeSideAWin, MyId), MyId),
                Is.EqualTo(BattlePhase.None), "已出胜负");
            Assert.That(
                BattleClient.DecidePhaseFromState(MakeState(eBattleOutcome.BattleOutcomeOngoing, MyId, EnemyId), MyId),
                Is.EqualTo(BattlePhase.WaitingAction), "本人待行动");
            Assert.That(
                BattleClient.DecidePhaseFromState(MakeState(eBattleOutcome.BattleOutcomeOngoing, EnemyId), MyId),
                Is.EqualTo(BattlePhase.Resolving), "本人已提交,等他人/等结算");
        }

        // ── 切磋 ────────────────────────────────────────────

        [Test]
        public void RespondChallenge_Accept_EntersPreparing()
        {
            _client.Tick(0);
            _client.RespondChallenge(55, accept: true);
            _net.CallsOf(MessageIds.RespondChallenge)[0]
                .Respond(new Match.RespondChallengeResponse());

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Preparing), "应战成功 → 等 gather 开战");
        }

        [Test]
        public void ChallengeResult_AcceptedByTarget_ChallengerEntersPreparing()
        {
            int results = 0;
            _client.OnChallengeResult += _ => results++;

            _net.PushNotify(MessageIds.NotifyChallengeResult, new Match.ChallengeResultS2C
            {
                ChallengeId = 55,
                Accepted = true,
                ResponderId = EnemyId,
            });

            Assert.That(results, Is.EqualTo(1));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Preparing), "发起者收到应战成功 → Preparing");
        }

        [Test]
        public void ChallengeResult_Rejected_StaysNone()
        {
            _net.PushNotify(MessageIds.NotifyChallengeResult, new Match.ChallengeResultS2C
            {
                ChallengeId = 55,
                Accepted = false,
                ResponderId = EnemyId,
            });

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));
        }

        [Test]
        public void ChallengeInvite_RaisesEventWithoutPhaseChange()
        {
            Match.ChallengeInviteS2C got = null;
            _client.OnChallengeInvite += ev => got = ev;

            _net.PushNotify(MessageIds.NotifyChallengeInvite, new Match.ChallengeInviteS2C
            {
                ChallengeId = 55,
                ChallengerId = EnemyId,
                ChallengerName = "挑战者",
            });

            Assert.That(got, Is.Not.Null);
            Assert.That(got.ChallengeId, Is.EqualTo(55UL));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None), "弹窗不改相位,应战才改");
        }

        [Test]
        public void BattleArenaBackground_RetainsFullResolutionForUgui()
        {
            var texture = Resources.Load<Texture2D>(
                "UI/Ugui/Battle/Backgrounds/qdao_battle_arena_cloud_terrace_2560x1080_v1");

            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.width, Is.EqualTo(2560));
            Assert.That(texture.height, Is.EqualTo(1080));
            Assert.That(texture.mipmapCount, Is.EqualTo(1),
                "Fullscreen UI artwork must not select a softer mip level.");
            Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
        }
    }
}
