using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui.Battle;
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

        /// <summary>
        /// 重建被测对象;传入假直连通道即进入收缩后口径(turn-based §22 D74)。
        /// random01 = 自动重拉退避抖动的随机源(空 = System.Random;0.5 = 无抖动,时序确定)。
        /// </summary>
        private void Build(FakeBattleChannel channel, System.Func<double> random01 = null)
        {
            _net = new FakeBattleTransport { PlayerId = MyId };
            _channel = channel;
            _client = new BattleClient(_net, channel, random01); // 直接 new,不经 Attach,避免污染单例
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
        public void SubmitAction_InvalidParameterTip_ReportsInvalidAction_NotBattleEnded()
        {
            // battle 节点把引擎 ValidateAction 的结果原样回给出手方:PVP 逃跑、用药超限、未拥有的技能等
            // 普通非法行动也是 kInvalidParameter,此时战斗仍在进行,不能提示「战斗已结束」
            PushBattleStart();
            Assert.That(_client.SubmitAction(new BattleAction { ActionType = eBattleActionType.BattleActionFlee }),
                Is.True);
            _net.CallsOf(MessageIds.SubmitBattleAction)[0].Respond(new SubmitBattleActionResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)common_error.KInvalidParameter },
            });

            Assert.That(_errors, Has.Count.EqualTo(1));
            Assert.That(_errors[0], Does.Not.Contain("已结束"));
            Assert.That(_errors[0], Does.Contain("行动无效"));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction), "战斗仍在进行,相位不动");
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

            // 已判定连不上:出手本地拒绝,文案与横幅一致,不能叫玩家「请稍候」
            _errors.Clear();
            Assert.That(_client.SubmitAction(new BattleAction
            {
                ActionType = eBattleActionType.BattleActionAttack,
                TargetId = EnemyId,
            }), Is.False);
            Assert.That(_errors, Is.EqualTo(new[] { BattleClient.BattleChannelFailedText }));
            Assert.That(_net.CallsOf(MessageIds.SubmitBattleAction), Is.Empty);

            _client.RetryBattleChannel();
            Assert.That(_channel.Retries, Is.EqualTo(new[] { TheBattleId }));
            Assert.That(_client.IsBattleChannelFailed, Is.False);

            // 手动重连进行中:回到「正在连接」口径
            _errors.Clear();
            Assert.That(_client.SubmitAction(new BattleAction { ActionType = eBattleActionType.BattleActionAttack }),
                Is.False);
            Assert.That(_errors, Is.EqualTo(new[] { BattleClient.BattleChannelConnectingText }));

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
            _channel.RaiseLost(TheBattleId, BattleLinkCloseKind.Superseded); // 换局不是本局结束的权威信号
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
        public void Submitted_LinkRecovers_PullToNextRound_NoPhaseEvent_NeedsScreenResync()
        {
            // 评审回归:已提交 → 直连断开(自动恢复中,未终结)→ 服务端在断连期间结算、进入下一回合且本人又在 pending →
            // 直连恢复补拉。相位前后都是 WaitingAction,不抛相位事件,也没有别的事件把新状态交给已开的战斗屏;
            // UI 只能按引用发现(BattleHudLogic.NeedsScreenResync)并补刷,否则停在旧回合的「已提交」、按钮全灰。
            // 覆盖范围:只守 BattleClient 这一侧(补拉换了新对象、相位不变)与补刷判定函数。
            // 「已提交」闸在 UI 侧(BattleScreen._submitted,由 ApplyState 在回合号变化时复位),
            // 接线 BattleUiRoot.ResyncOpenBattleScreen → BattleScreen.ApplyState 本用例不覆盖,目前只由人工验证
            UseChannel();
            PushBattleStart();
            _channel.RaiseReady(TheBattleId);
            _net.CallsOf(MessageIds.GetBattleState)[0]
                .Respond(MakeState(eBattleOutcome.BattleOutcomeOngoing, MyId, EnemyId));
            var shown = _client.State; // 屏上此刻显示的状态(就绪补拉后 UI 已按它补刷过)
            Assert.That(_client.SubmitAction(new BattleAction { ActionType = eBattleActionType.BattleActionDefend }),
                Is.True);

            _channel.ReadyBattleId = 0;       // 直连断开,自动恢复中(不抛 Lost:尚未终结)
            Assert.That(_client.IsBattleChannelReady, Is.False);
            _channel.RaiseReady(TheBattleId); // 恢复
            var pulls = _net.CallsOf(MessageIds.GetBattleState);
            Assert.That(pulls, Has.Count.EqualTo(2), "恢复即补拉:未就绪期间的回合结果被服务端丢弃、不补发");

            var next = MakeState(eBattleOutcome.BattleOutcomeOngoing, MyId, EnemyId);
            next.RoundIndex = 2;
            int phaseEvents = _phases.Count;
            pulls[1].Respond(next);

            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction), "本人在新回合 pending 里");
            Assert.That(_phases.Count, Is.EqualTo(phaseEvents), "相位不变:没有相位事件会把新状态交给屏");
            // 假传输按字节 round-trip 回调(与真实链路同构),拿到的是新解析的对象,不是 next 本身
            Assert.That(_client.State.RoundIndex, Is.EqualTo(2u));
            Assert.That(_client.State, Is.Not.SameAs(shown));
            Assert.That(BattleHudLogic.NeedsScreenResync(true, false, _client.Phase, _client.State, shown), Is.True,
                "UI 须按引用发现补拉来的新状态并补刷战斗屏");
            // 注意:这条不证明「新回合可再出手」—— BattleClient 本身没有按回合的提交闸,同一回合重复提交也返回 true。
            // 它只钉住「BattleClient 层不拦再次提交」,提交闸在 BattleScreen(见用例开头的覆盖范围说明)
            Assert.That(_client.SubmitAction(new BattleAction { ActionType = eBattleActionType.BattleActionDefend }),
                Is.True, "BattleClient 层不拦再次提交(提交闸在 BattleScreen)");
        }

        [Test]
        public void HasActiveBattle_TrueWhileReconnectRecovering_FalseAfterSettle()
        {
            // 大厅重连后权威状态要经直连补拉:补拉成功前相位停在 None,只有 HasActiveBattle 说明本人仍在战斗中
            // (UI 主城入口据此显示不可点的「连接战斗中…」/ 可点的「重新连接战斗」/ 可点的「返回战斗」,
            //  而不是能点开排队的「战斗」,见 BattleUiRoot.DecideEntryMode)
            UseChannel();
            Assert.That(_client.HasActiveBattle, Is.False, "空闲");

            _net.PushNotify(MessageIds.NotifyBattleReconnect, new BattleReconnectS2C { BattleId = TheBattleId });
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None), "补拉推迟到直连就绪,相位未动");
            Assert.That(_client.HasActiveBattle, Is.True);
            Assert.That(_client.IsBattleChannelReady, Is.False);
            Assert.That(_client.IsBattleChannelFailed, Is.False);

            // 判定连不上:仍有对局(入口「重新连接战斗」);手动重连后回到连接中
            _channel.RaiseLost(TheBattleId, BattleLinkCloseKind.Unreachable);
            Assert.That(_client.IsBattleChannelFailed, Is.True);
            Assert.That(_client.HasActiveBattle, Is.True);
            _client.RetryBattleChannel();
            Assert.That(_client.IsBattleChannelFailed, Is.False);
            Assert.That(_client.HasActiveBattle, Is.True);

            // 补签判战斗已结束(BattleGone):收场
            _channel.RaiseLost(TheBattleId, BattleLinkCloseKind.BattleGone);
            Assert.That(_client.HasActiveBattle, Is.False);

            // 开局 → 大厅断线:收场
            PushBattleStart();
            Assert.That(_client.HasActiveBattle, Is.True);
            _net.RaiseDisconnected();
            Assert.That(_client.HasActiveBattle, Is.False);

            // 开局 → 终局包:收场
            PushBattleStart();
            _net.PushNotify(MessageIds.NotifyBattleEnd, new BattleEndS2C
            {
                BattleId = TheBattleId,
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
            });
            Assert.That(_client.HasActiveBattle, Is.False);
        }

        [Test]
        public void ReconnectPull_TransportErrorWhileLinkReady_EntryOffersReturnToBattle()
        {
            // 评审回归:大厅重连后直连已就绪,就绪补拉却以传输错误失败(链路仍 Verified 时的 rpc 超时)。
            // BattleClient 不打扰玩家,链路不断就不会再有就绪事件替它补拉;它只按有上限退避自动重拉
            // (见 StatePull_TransportErrorWhileLinkReady_* 用例),退避等待期间与预算用完后相位都停在 None,
            // 期间经直连到的回合结果又被相位闸丢弃。主城入口若退回「战斗」(或换成不可点的「连接战斗中…」),
            // 玩家可能到本局结束都回不到战斗 —— 入口须给可点的「返回战斗」,点了 RequestState 补拉即恢复
            UseChannel();
            _net.PushNotify(MessageIds.NotifyBattleReconnect, new BattleReconnectS2C { BattleId = TheBattleId });
            Assert.That(EntryMode(), Is.EqualTo(BattleUiRoot.BattleEntryMode.Connecting), "直连就绪前:不可点的「连接战斗中…」");

            _channel.RaiseReady(TheBattleId);
            Assert.That(_net.CallsOf(MessageIds.GetBattleState), Has.Count.EqualTo(1), "就绪即补拉");
            _net.CallsOf(MessageIds.GetBattleState)[0].FailWith(BattleDirectLink.TransportErrorPrefix + "rpc timeout");

            Assert.That(_errors, Is.Empty, "传输错误不打扰玩家");
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));
            Assert.That(_client.HasActiveBattle, Is.True);
            Assert.That(_client.IsBattleChannelReady, Is.True, "链路仍就绪:不会再有就绪事件替它补拉");
            Assert.That(_client.IsBattleChannelFailed, Is.False);
            PushTurnResult(TheBattleId, MyId);
            Assert.That(_turnResults, Is.EqualTo(0), "相位 None:经直连到的回合结果被丢弃,不能指望它把玩家带回战斗");
            Assert.That(EntryMode(), Is.EqualTo(BattleUiRoot.BattleEntryMode.ReturnToBattle));

            // 点「返回战斗」= RequestState;连点由同局在途去重兜住
            _client.RequestState();
            _client.RequestState();
            var pulls = _net.CallsOf(MessageIds.GetBattleState);
            Assert.That(pulls, Has.Count.EqualTo(2), "再拉一次,且同局在途不叠发");
            pulls[1].Respond(MakeState(eBattleOutcome.BattleOutcomeOngoing, MyId, EnemyId));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction), "回到战斗(UI 随相位事件开屏、入口隐藏)");
            Assert.That(_errors, Is.Empty);
        }

        /// <summary>按当前 BattleClient 状态取主城入口用途(战斗屏未开,即入口可见时)。</summary>
        private BattleUiRoot.BattleEntryMode EntryMode()
            => BattleUiRoot.DecideEntryMode(false, _client.HasActiveBattle, _client.IsBattleChannelReady,
                _client.IsBattleChannelFailed, _client.Phase);

        // ── 就绪补拉遇传输错误的自动重拉(turn-based §22 D74) ─────

        /// <summary>链路仍 Verified 时直连调用超时的错误串(带传输层前缀)。</summary>
        private const string RpcTimeout = BattleDirectLink.TransportErrorPrefix + "rpc timeout";

        private int PullCount => _net.CallsOf(MessageIds.GetBattleState).Count;

        private FakeBattleTransport.RecordedCall LastPull()
        {
            var pulls = _net.CallsOf(MessageIds.GetBattleState);
            return pulls[pulls.Count - 1];
        }

        /// <summary>大厅重连 → 直连就绪 → 就绪补拉以传输错误失败(链路仍就绪)。调用前先 Tick 定好时钟。</summary>
        private void ReconnectReadyThenPullTimesOut()
        {
            _net.PushNotify(MessageIds.NotifyBattleReconnect, new BattleReconnectS2C { BattleId = TheBattleId });
            _channel.RaiseReady(TheBattleId);
            LastPull().FailWith(RpcTimeout);
        }

        [Test]
        public void StatePull_TransportErrorWhileLinkReady_AutoRetriesWithBackoff_UntilSuccess()
        {
            // 链路不断就不会再有就绪事件替它补拉:不能只靠玩家点「返回战斗」,按 1s / 2s / 4s 自动重拉
            Build(new FakeBattleChannel(), random01: () => 0.5); // 0.5 = 无抖动,时序确定
            _client.Tick(0);
            ReconnectReadyThenPullTimesOut();
            Assert.That(_errors, Is.Empty, "传输错误不打扰玩家");
            Assert.That(PullCount, Is.EqualTo(1));

            _client.Tick(0.99);
            Assert.That(PullCount, Is.EqualTo(1), "第 1 次重拉等 1s");
            _client.Tick(1.0);
            Assert.That(PullCount, Is.EqualTo(2), "到点自动重拉");
            Assert.That(((GetBattleStateRequest)LastPull().Request).BattleId, Is.EqualTo(TheBattleId));

            LastPull().FailWith(RpcTimeout);
            _client.Tick(2.99);
            Assert.That(PullCount, Is.EqualTo(2), "第 2 次重拉等 2s");
            _client.Tick(3.0);
            Assert.That(PullCount, Is.EqualTo(3));

            LastPull().Respond(MakeState(eBattleOutcome.BattleOutcomeOngoing, MyId, EnemyId));
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction), "重拉成功即回到战斗");
            Assert.That(_errors, Is.Empty);

            _client.Tick(100);
            Assert.That(PullCount, Is.EqualTo(3), "成功后不再有重拉计划");
        }

        [Test]
        public void StatePull_AutoRetry_StopsAtCap_NextReadyOrManualPullStartsNewRound()
        {
            Build(new FakeBattleChannel(), random01: () => 0.5);
            _client.Tick(0);
            ReconnectReadyThenPullTimesOut(); // 就绪补拉 @0 失败 → 第 1 次重拉排在 @1

            foreach (double at in new[] { 1.0, 3.0, 7.0 }) // 1s / 2s / 4s 逐次累加,每次都再失败
            {
                _client.Tick(at);
                LastPull().FailWith(RpcTimeout);
            }
            int capped = 1 + BattleClient.MaxStatePullRetries;
            Assert.That(PullCount, Is.EqualTo(capped), "就绪补拉 1 次 + 自动重拉到上限");

            _client.Tick(1000);
            Assert.That(PullCount, Is.EqualTo(capped), "预算用完不再重拉");
            Assert.That(_errors, Is.Empty, "用完也不打扰玩家");
            Assert.That(EntryMode(), Is.EqualTo(BattleUiRoot.BattleEntryMode.ReturnToBattle),
                "由可点的「返回战斗」兜底");

            // 下一次就绪:立即补拉并重置预算
            _channel.RaiseReady(TheBattleId);
            Assert.That(PullCount, Is.EqualTo(capped + 1));
            LastPull().FailWith(RpcTimeout);
            _client.Tick(1001);
            Assert.That(PullCount, Is.EqualTo(capped + 2), "新一轮预算:1s 后重拉");

            // 玩家点「返回战斗」(RequestState):立即补拉,作废待执行的第 2 次计划(@1003)并重置预算
            LastPull().FailWith(RpcTimeout);
            _client.RequestState();
            Assert.That(PullCount, Is.EqualTo(capped + 3));
            LastPull().FailWith(RpcTimeout);
            _client.Tick(1002);
            Assert.That(PullCount, Is.EqualTo(capped + 4), "手动补拉后预算重置:1s 后重拉,而不是等旧计划的 2s");
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void StatePull_AutoRetryPlan_ClearedOnBattleSwitchSettleDisconnectAndLinkLoss()
        {
            Build(new FakeBattleChannel(), random01: () => 0.5);
            _client.Tick(0);

            // 换局:旧计划作废 —— 即便新一局直连已就绪(开局包经直连到达),到点也不替旧计划补拉
            ReconnectReadyThenPullTimesOut(); // 重拉排在 @1
            const ulong nextBattleId = 8800;
            var nextState = MakeState(eBattleOutcome.BattleOutcomeOngoing, MyId, EnemyId);
            nextState.BattleId = nextBattleId;
            _net.PushNotify(MessageIds.NotifyBattleStart, new BattleStartS2C { BattleId = nextBattleId, State = nextState });
            _channel.ReadyBattleId = nextBattleId;
            int pulls = PullCount;
            _client.Tick(10);
            Assert.That(PullCount, Is.EqualTo(pulls), "换局:旧局的重拉计划作废");

            // 收尾(终局包):计划随本局作废
            _channel.RaiseReady(nextBattleId);
            LastPull().FailWith(RpcTimeout); // 重拉排在 @11
            _net.PushNotify(MessageIds.NotifyBattleEnd, new BattleEndS2C
            {
                BattleId = nextBattleId,
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
            });
            pulls = PullCount;
            _client.Tick(20);
            Assert.That(PullCount, Is.EqualTo(pulls), "收尾:不再重拉");
            Assert.That(_client.HasActiveBattle, Is.False);

            // 大厅断线:计划随本局作废(断线同时拆直连)
            PushBattleStart();
            _channel.RaiseReady(TheBattleId);
            LastPull().FailWith(RpcTimeout); // 重拉排在 @21
            _net.RaiseDisconnected();
            _channel.RaiseLost(TheBattleId, BattleLinkCloseKind.HostClosed);
            pulls = PullCount;
            _client.Tick(30);
            Assert.That(PullCount, Is.EqualTo(pulls), "断线:不再重拉");

            // 到点时直连已不就绪(判定连不上):放弃本次,不打到一条不存在的链路上;下一次就绪再补拉
            _net.PushNotify(MessageIds.NotifyBattleReconnect, new BattleReconnectS2C { BattleId = TheBattleId });
            _channel.RaiseReady(TheBattleId);
            LastPull().FailWith(RpcTimeout); // 重拉排在 @31
            _channel.RaiseLost(TheBattleId, BattleLinkCloseKind.Unreachable);
            pulls = PullCount;
            _client.Tick(40);
            Assert.That(PullCount, Is.EqualTo(pulls), "直连已不就绪:到点放弃");
            Assert.That(EntryMode(), Is.EqualTo(BattleUiRoot.BattleEntryMode.Reconnect));

            _channel.RaiseReady(TheBattleId);
            Assert.That(PullCount, Is.EqualTo(pulls + 1), "下一次就绪照常补拉");
            Assert.That(_errors, Is.Empty);
        }

        [TestCase(0.0, 0.79, 0.81)]    // 下沿:1s × 0.8
        [TestCase(0.999, 1.19, 1.2)]   // 上沿:1s × (1 + 0.998 × 0.2) ≈ 1.1996s
        public void StatePull_AutoRetry_FirstDelayJitterWithinTwentyPercent(double randomSample, double before, double after)
        {
            Build(new FakeBattleChannel(), random01: () => randomSample);
            _client.Tick(0);
            ReconnectReadyThenPullTimesOut();

            _client.Tick(before);
            Assert.That(PullCount, Is.EqualTo(1));
            _client.Tick(after);
            Assert.That(PullCount, Is.EqualTo(2));
        }

        [Test]
        public void StatePull_NoAutoRetry_ForServerErrorOrWhenLinkAlreadyNotReady()
        {
            // 服务端拒绝(非传输错误):照常 OnError,重拉也拿不到别的答复
            Build(new FakeBattleChannel(), random01: () => 0.5);
            _client.Tick(0);
            _net.PushNotify(MessageIds.NotifyBattleReconnect, new BattleReconnectS2C { BattleId = TheBattleId });
            _channel.RaiseReady(TheBattleId);
            LastPull().FailWith("server tip=5");
            Assert.That(_errors, Has.Count.EqualTo(1));
            _client.Tick(100);
            Assert.That(PullCount, Is.EqualTo(1), "服务端拒绝不自动重拉");

            // 失败时同一局直连已不就绪(链路断开、恢复中):不排计划,交给下一次就绪
            Build(new FakeBattleChannel(), random01: () => 0.5);
            _client.Tick(0);
            _net.PushNotify(MessageIds.NotifyBattleReconnect, new BattleReconnectS2C { BattleId = TheBattleId });
            _channel.RaiseReady(TheBattleId);
            _channel.ReadyBattleId = 0; // 链路断开(自动恢复中,未终结,不抛 Lost)
            LastPull().FailWith(BattleDirectLink.TransportErrorPrefix + "disconnected");
            _channel.ReadyBattleId = TheBattleId; // 只为证明没有排计划:静默恢复就绪后到点也不补拉
            _client.Tick(100);
            Assert.That(PullCount, Is.EqualTo(1), "失败时已不就绪:不排自动重拉");
            Assert.That(_errors, Is.Empty);
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
