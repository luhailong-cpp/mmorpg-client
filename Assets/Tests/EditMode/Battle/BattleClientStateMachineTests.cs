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
        private FakeBattleChannel _channel;   // null = 无直连通道(永远就绪,既有用例)
        private BattleClient _client;
        private List<BattlePhase> _phases;
        private List<string> _errors;
        private List<string> _channelFailures;
        private int _channelReadies;
        private int _starts;
        private int _turnResults;
        private int _ends;

        [SetUp]
        public void SetUp() => Build(channel: null);

        /// <summary>重建被测对象;传入假直连通道即进入收缩后口径(turn-based §22 D74)。</summary>
        private void Build(FakeBattleChannel channel)
        {
            _net = new FakeBattleTransport { PlayerId = MyId };
            _channel = channel;
            _client = new BattleClient(_net, channel); // 直接 new,不经 Attach,避免污染单例
            _phases = new List<BattlePhase>();
            _errors = new List<string>();
            _channelFailures = new List<string>();
            _channelReadies = 0;
            _starts = _turnResults = _ends = 0;
            _client.OnPhaseChanged += p => _phases.Add(p);
            _client.OnError += e => _errors.Add(e);
            _client.OnBattleStart += _ => _starts++;
            _client.OnTurnResult += _ => _turnResults++;
            _client.OnBattleEnd += _ => _ends++;
            _client.OnBattleChannelFailed += t => _channelFailures.Add(t);
            _client.OnBattleChannelReady += () => _channelReadies++;
        }

        private void UseChannel() => Build(new FakeBattleChannel());

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
        public void JoinQueue_WhileReconnectRestoredBattle_RejectsWithErrorAndKeepsPhase()
        {
            // 上一局被强杀后重新登录:scene 推 BattleReconnect → 权威状态把相位拉到 WaitingAction
            _net.PushNotify(MessageIds.NotifyBattleReconnect,
                new BattleReconnectS2C { BattleId = TheBattleId });
            _net.CallsOf(MessageIds.GetBattleState)[0]
                .Respond(MakeState(eBattleOutcome.BattleOutcomeOngoing, MyId, EnemyId));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction));

            _client.JoinQueue(Match.MatchMode._1V1, 1);

            Assert.That(_net.CallsOf(MessageIds.JoinQueue), Is.Empty, "前置拒绝,不发请求");
            Assert.That(_errors, Has.Count.EqualTo(1), "拒绝原因经 OnError 抛出(自动驾驶据此判失败)");
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction), "拒绝不改相位");
        }

        [Test]
        public void JoinQueue_LateRejectionAfterReconnect_StillReportsErrorWithoutTouchingPhase()
        {
            // JoinQueue 已发出、BattleReconnect 才到:相位被权威状态改成 WaitingAction,
            // 随后服务端 ErrInBattle 响应迟到 —— 拒绝原因仍要抛出,但不能把相位拉回 None
            _client.JoinQueue(Match.MatchMode._1V1, 1);
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Queued));

            _net.PushNotify(MessageIds.NotifyBattleReconnect,
                new BattleReconnectS2C { BattleId = TheBattleId });
            _net.CallsOf(MessageIds.GetBattleState)[0]
                .Respond(MakeState(eBattleOutcome.BattleOutcomeOngoing, MyId, EnemyId));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction));

            _net.CallsOf(MessageIds.JoinQueue)[0].Respond(new Match.JoinQueueResponse
            {
                ErrorCode = 7,
                ErrorMessage = new TipInfoMessage { Id = 7 },
            });

            Assert.That(_errors, Has.Count.EqualTo(1), "迟到的拒绝响应不再静默吞掉");
            Assert.That(_errors[0], Does.Contain("7"));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction), "迟到响应不改相位");
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

        // ── 迟到结果与结算包幂等 ────────────────────────────

        [Test]
        public void LateStatePull_AfterDisconnect_IsDiscarded()
        {
            _net.PushNotify(MessageIds.NotifyBattleReconnect, new BattleReconnectS2C { BattleId = TheBattleId });
            var pull = _net.CallsOf(MessageIds.GetBattleState)[0];
            _net.RaiseDisconnected();

            pull.Respond(MakeState(eBattleOutcome.BattleOutcomeOngoing, MyId, EnemyId));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None), "断线后迟到的补拉结果不得复活战斗态");
        }

        [Test]
        public void StatePull_InFlight_IsDeduplicated()
        {
            _net.PushNotify(MessageIds.NotifyBattleReconnect, new BattleReconnectS2C { BattleId = TheBattleId });
            _client.RequestState(); // UI 兜底补拉与重连补拉撞车
            Assert.That(_net.CallsOf(MessageIds.GetBattleState), Has.Count.EqualTo(1), "同局补拉在途时去重");

            _net.CallsOf(MessageIds.GetBattleState)[0]
                .Respond(MakeState(eBattleOutcome.BattleOutcomeOngoing, MyId, EnemyId));
            _client.RequestState();
            Assert.That(_net.CallsOf(MessageIds.GetBattleState), Has.Count.EqualTo(2), "在途结束后可再拉");
        }

        [Test]
        public void StatePullSaysFinished_LateSettlementEnd_StillDelivered()
        {
            // 补拉到已出胜负 → 先收敛 None;scene 结算后经大厅推的终局包晚到,仍要交给 UI
            _net.PushNotify(MessageIds.NotifyBattleReconnect, new BattleReconnectS2C { BattleId = TheBattleId });
            _net.CallsOf(MessageIds.GetBattleState)[0].Respond(MakeState(eBattleOutcome.BattleOutcomeSideAWin));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));
            _phases.Clear();

            _net.PushNotify(MessageIds.NotifyBattleEnd, new BattleEndS2C
            {
                BattleId = TheBattleId,
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
            });
            Assert.That(_ends, Is.EqualTo(1));
            Assert.That(_phases, Is.EqualTo(new[] { BattlePhase.Ended, BattlePhase.None }));

            _net.PushNotify(MessageIds.NotifyBattleEnd, new BattleEndS2C { BattleId = TheBattleId });
            Assert.That(_ends, Is.EqualTo(1), "直连与大厅各到一份:按 battle_id 幂等");
        }

        // ── 战斗直连通道(turn-based §22 D74) ─────────────

        [Test]
        public void NoChannel_IsAlwaysReady()
        {
            Assert.That(_client.IsBattleChannelReady, Is.True, "无通道(演出台 / 既有测试)视为永远就绪");
            PushBattleStart();
            Assert.That(_client.IsBattleChannelReady, Is.True);
        }

        [Test]
        public void BattleStart_ChannelNotReady_EnsuresBattle_AndReportsNotReady()
        {
            UseChannel();
            Assert.That(_client.IsBattleChannelReady, Is.False, "不在战斗中为 false");
            PushBattleStart();

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction));
            Assert.That(_channel.Ensures, Is.EqualTo(new[] { TheBattleId }), "开局自愈:分配包丢失时补签");
            Assert.That(_client.IsBattleChannelReady, Is.False);
            Assert.That(_net.Calls, Is.Empty, "开局时不发任何战斗请求");

            _channel.RaiseReady(TheBattleId);
            Assert.That(_client.IsBattleChannelReady, Is.True);
            Assert.That(_channelReadies, Is.EqualTo(1));
        }

        [Test]
        public void ChannelReady_TriggersResync_ThenDedupes()
        {
            UseChannel();
            PushBattleStart();
            _channel.RaiseReady(TheBattleId);

            var pulls = _net.CallsOf(MessageIds.GetBattleState);
            Assert.That(pulls, Has.Count.EqualTo(1), "就绪即补拉(开局到握手之间的帧不经大厅回落)");
            Assert.That(((GetBattleStateRequest)pulls[0].Request).BattleId, Is.EqualTo(TheBattleId));

            _client.RequestState();
            Assert.That(_net.CallsOf(MessageIds.GetBattleState), Has.Count.EqualTo(1), "在途去重");

            pulls[0].Respond(MakeState(eBattleOutcome.BattleOutcomeOngoing, EnemyId));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.Resolving), "按权威状态校正相位");
        }

        [Test]
        public void ChannelReady_ForOtherBattle_Ignored()
        {
            UseChannel();
            PushBattleStart();
            _channel.RaiseReady(9999, eBattleTicketRole.BattleTicketRoleObserver);
            Assert.That(_net.CallsOf(MessageIds.GetBattleState), Is.Empty);
            Assert.That(_channelReadies, Is.EqualTo(0));
            Assert.That(_client.IsBattleChannelReady, Is.False);
        }

        [Test]
        public void Reconnect_DefersStatePull_UntilChannelReady()
        {
            UseChannel();
            _net.PushNotify(MessageIds.NotifyBattleReconnect, new BattleReconnectS2C { BattleId = TheBattleId });

            Assert.That(_net.CallsOf(MessageIds.GetBattleState), Is.Empty, "直连未就绪:不发(大厅上会挂满 15s)");
            Assert.That(_errors, Is.Empty, "推迟不是错误");

            _channel.RaiseReady(TheBattleId);
            var pulls = _net.CallsOf(MessageIds.GetBattleState);
            Assert.That(pulls, Has.Count.EqualTo(1));
            pulls[0].Respond(MakeState(eBattleOutcome.BattleOutcomeOngoing, MyId, EnemyId));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction));
        }

        [Test]
        public void StatePull_TransportError_WithChannel_IsSilent_RetriedOnNextReady()
        {
            UseChannel();
            PushBattleStart();
            _channel.RaiseReady(TheBattleId);
            _net.CallsOf(MessageIds.GetBattleState)[0].FailWith(BattleDirectLink.TransportErrorPrefix + "disconnected");
            Assert.That(_errors, Is.Empty, "直连抖动不打扰玩家");

            _channel.RaiseReady(TheBattleId); // 重连成功
            Assert.That(_net.CallsOf(MessageIds.GetBattleState), Has.Count.EqualTo(2));
        }

        [Test]
        public void SubmitAction_ChannelNotReady_ReturnsFalse_NoCall()
        {
            UseChannel();
            PushBattleStart();

            bool sent = _client.SubmitAction(new BattleAction
            {
                ActionType = eBattleActionType.BattleActionAttack,
                TargetId = EnemyId,
            });
            Assert.That(sent, Is.False);
            Assert.That(_net.CallsOf(MessageIds.SubmitBattleAction), Is.Empty, "未就绪不发任何网络请求");
            Assert.That(_errors, Is.EqualTo(new[] { BattleClient.BattleChannelConnectingText }));

            _channel.RaiseReady(TheBattleId);
            sent = _client.SubmitAction(new BattleAction
            {
                ActionType = eBattleActionType.BattleActionAttack,
                TargetId = EnemyId,
            });
            Assert.That(sent, Is.True);
            Assert.That(_net.CallsOf(MessageIds.SubmitBattleAction), Has.Count.EqualTo(1));
        }

        [Test]
        public void SubmitAction_ReturnsFalse_WhenPhaseWrong()
        {
            Assert.That(_client.SubmitAction(new BattleAction()), Is.False);
            PushBattleStart();
            Assert.That(_client.SubmitAction(null), Is.False);
            Assert.That(_client.SubmitAction(new BattleAction { ActionType = eBattleActionType.BattleActionAttack }),
                Is.True, "无通道视为就绪:照常发出");
        }

        [Test]
        public void ChannelLost_BattleGone_ConvergesNone_LateSettlementStillDelivered()
        {
            UseChannel();
            PushBattleStart();
            _phases.Clear();

            _channel.RaiseLost(TheBattleId, BattleLinkCloseKind.BattleGone);
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None), "终局包丢了也不卡在 WaitingAction");
            Assert.That(_errors, Has.Count.EqualTo(1));
            Assert.That(_errors[0], Does.Contain("战斗已结束"));
            Assert.That(_channelFailures, Is.Empty);

            // scene 结算后经大厅推的终局包晚到:仍交给 UI 出结算面板
            _phases.Clear();
            _net.PushNotify(MessageIds.NotifyBattleEnd, new BattleEndS2C
            {
                BattleId = TheBattleId,
                Outcome = eBattleOutcome.BattleOutcomeSideBWin,
            });
            Assert.That(_ends, Is.EqualTo(1));
            Assert.That(_phases, Is.EqualTo(new[] { BattlePhase.Ended, BattlePhase.None }));
        }

        [Test]
        public void ChannelLost_Unreachable_KeepsPhase_RaisesFailed_RetryClears()
        {
            UseChannel();
            PushBattleStart();

            _channel.RaiseLost(TheBattleId, BattleLinkCloseKind.Unreachable);
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction), "保持相位:服务端回合超时替本人出手");
            Assert.That(_channelFailures, Is.EqualTo(new[] { BattleClient.BattleChannelFailedText }));
            Assert.That(_client.IsBattleChannelFailed, Is.True);

            _client.RetryBattleChannel();
            Assert.That(_channel.Retries, Is.EqualTo(new[] { TheBattleId }));
            Assert.That(_client.IsBattleChannelFailed, Is.False);

            _channel.RaiseReady(TheBattleId);
            Assert.That(_client.IsBattleChannelReady, Is.True);
            Assert.That(_net.CallsOf(MessageIds.GetBattleState), Has.Count.EqualTo(1), "重连成功即补拉");
        }

        [Test]
        public void ChannelLost_EndedHostClosedOrOtherBattle_Ignored()
        {
            UseChannel();
            PushBattleStart();
            _channel.RaiseLost(TheBattleId, BattleLinkCloseKind.Ended);
            _channel.RaiseLost(TheBattleId, BattleLinkCloseKind.HostClosed);
            _channel.RaiseLost(9999, BattleLinkCloseKind.BattleGone);
            _channel.RaiseLost(9999, BattleLinkCloseKind.Unreachable);

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction));
            Assert.That(_errors, Is.Empty);
            Assert.That(_channelFailures, Is.Empty);
        }

        [Test]
        public void RetryBattleChannel_WithoutBattleOrChannel_NoOp()
        {
            _client.RetryBattleChannel(); // 无通道
            UseChannel();
            _client.RetryBattleChannel(); // 不在战斗
            Assert.That(_channel.Retries, Is.Empty);
        }

        [Test]
        public void BattleStart_ChannelAlreadyReady_IsReadyImmediately()
        {
            // 直连握手先于开局包完成(开局包经直连到达的情形)
            UseChannel();
            _channel.ReadyBattleId = TheBattleId;
            PushBattleStart();
            Assert.That(_client.IsBattleChannelReady, Is.True);
            Assert.That(_client.SubmitAction(new BattleAction { ActionType = eBattleActionType.BattleActionAttack }),
                Is.True);
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
