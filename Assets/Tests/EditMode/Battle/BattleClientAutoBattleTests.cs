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
        private FakeBattleChannel _channel;   // null = 无直连通道(永远就绪,既有用例)
        private BattleClient _client;
        private List<bool> _autoChanges;
        private List<string> _errors;

        [SetUp]
        public void SetUp() => Build(channel: null);

        /// <summary>重建被测对象;传入假直连通道即进入收缩后口径(turn-based §22 D74)。</summary>
        private void Build(FakeBattleChannel channel)
        {
            _net = new FakeBattleTransport { PlayerId = MyId };
            _channel = channel;
            _client = new BattleClient(_net, channel); // 直接 new,不经 Attach,避免污染单例
            _autoChanges = new List<bool>();
            _errors = new List<string>();
            _client.OnAutoStateChanged += v => _autoChanges.Add(v);
            _client.OnError += e => _errors.Add(e);
        }

        private void UseChannel() => Build(new FakeBattleChannel());

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

        // ── 战斗直连未就绪时的自动战斗(turn-based §22 D74) ──

        [Test]
        public void BattleStart_ChannelNotReady_AutoResendDeferredUntilReady()
        {
            UseChannel();
            _client.SetAutoBattle(true); // 跨场挂机记忆
            PushBattleStart();
            Assert.That(_net.CallsOf(MessageIds.SetAutoBattle), Is.Empty,
                "开局瞬间直连未握手:不发(大厅上会挂满 15s 超时)");
            Assert.That(_errors, Is.Empty);

            _channel.RaiseReady(TheBattleId);
            var calls = _net.CallsOf(MessageIds.SetAutoBattle);
            Assert.That(calls, Has.Count.EqualTo(1), "直连就绪后补发记忆");
            Assert.That(((SetAutoBattleRequest)calls[0].Request).Enabled, Is.True);
            Assert.That(((SetAutoBattleRequest)calls[0].Request).BattleId, Is.EqualTo(TheBattleId));
            Assert.That(_net.CallsOf(MessageIds.GetBattleState), Has.Count.EqualTo(1), "同时补拉状态");
        }

        [Test]
        public void ManualToggle_NotReady_LatchOnly_ThenSentOnReady()
        {
            UseChannel();
            PushBattleStart();

            _client.SetAutoBattle(true);
            Assert.That(_client.AutoBattleLatched, Is.True);
            Assert.That(_net.CallsOf(MessageIds.SetAutoBattle), Is.Empty, "未就绪只记意愿");
            Assert.That(_errors, Is.Empty, "不报错:UI 自行提示「战斗连接建立后生效」");

            _channel.RaiseReady(TheBattleId);
            Assert.That(_net.CallsOf(MessageIds.SetAutoBattle), Has.Count.EqualTo(1));
        }

        [Test]
        public void ManualToggleOff_NotReady_SendsFalseOnReady()
        {
            UseChannel();
            _channel.ReadyBattleId = TheBattleId;
            PushBattleStart();
            _client.SetAutoBattle(true);                         // 就绪:立即发
            PushTurnResult(myAuto: true);                        // 服务端已挂机
            _channel.RaiseLost(TheBattleId, BattleLinkCloseKind.Unreachable);

            _client.SetAutoBattle(false);                        // 直连断着时关自动
            Assert.That(_net.CallsOf(MessageIds.SetAutoBattle), Has.Count.EqualTo(1));

            _channel.RaiseReady(TheBattleId);
            var calls = _net.CallsOf(MessageIds.SetAutoBattle);
            Assert.That(calls, Has.Count.EqualTo(2));
            Assert.That(((SetAutoBattleRequest)calls[1].Request).Enabled, Is.False, "按记忆补发关闭");
        }

        [Test]
        public void SetAutoBattle_TransportError_WithChannel_KeepsLatch_ResendsOnReady()
        {
            UseChannel();
            _channel.ReadyBattleId = TheBattleId;
            PushBattleStart();
            _client.SetAutoBattle(true);
            _net.CallsOf(MessageIds.SetAutoBattle)[0].FailWith(BattleDirectLink.TransportErrorPrefix + "disconnected");

            Assert.That(_client.AutoBattleLatched, Is.True, "直连抖动不回滚意愿");
            Assert.That(_errors, Is.Empty, "不打扰玩家");

            _channel.RaiseReady(TheBattleId); // 重连成功
            Assert.That(_net.CallsOf(MessageIds.SetAutoBattle), Has.Count.EqualTo(2), "就绪后补发");
        }

        [Test]
        public void ChannelReady_AlreadyAuto_NoRedundantResend()
        {
            UseChannel();
            _channel.ReadyBattleId = TheBattleId;
            _client.SetAutoBattle(true);
            PushBattleStart();                                   // 已就绪:开局即补发
            Assert.That(_net.CallsOf(MessageIds.SetAutoBattle), Has.Count.EqualTo(1));
            PushTurnResult(myAuto: true);                        // 权威态已挂机

            _channel.RaiseReady(TheBattleId);                    // 断线重连成功
            Assert.That(_net.CallsOf(MessageIds.SetAutoBattle), Has.Count.EqualTo(1), "权威态已挂机,不重复补发");
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
        public void ContinuousBattle_RejoinKeepsPveTeamSize()
        {
            // PVE 人数档(JoinQueueRequest.team_size)也是排队参数:首发带上,连续战斗重排时原样带回,
            // 否则 3 人档打完会掉回「副本人数上限」那一档(服务端对 0 的解释)。
            _client.ContinuousBattle = true;
            _client.Tick(0);
            _client.JoinQueue(Match.MatchMode.PveTeam, 1, 3);
            var first = (Match.JoinQueueRequest)_net.CallsOf(MessageIds.JoinQueue)[0].Request;
            Assert.That(first.Mode, Is.EqualTo(Match.MatchMode.PveTeam));
            Assert.That(first.TeamSize, Is.EqualTo(3U));

            PushBattleStart();
            PushBattleEnd();
            _client.AckBattleEnd();

            var joins = _net.CallsOf(MessageIds.JoinQueue);
            Assert.That(joins, Has.Count.EqualTo(2), "Ack 后按记忆重新入队");
            var again = (Match.JoinQueueRequest)joins[1].Request;
            Assert.That(again.Mode, Is.EqualTo(Match.MatchMode.PveTeam));
            Assert.That(again.BattleConfigId, Is.EqualTo(1U));
            Assert.That(again.TeamSize, Is.EqualTo(3U), "人数档随重排带回");
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

        [Test]
        public void AdoptedTeamBattle_DoesNotRepeatAnEarlierSoloQueue()
        {
            _client.ContinuousBattle = true;
            _client.Tick(0);
            _client.JoinQueue(Match.MatchMode.PveSolo, 1);
            PushBattleStart();
            PushBattleEnd();
            Assert.That(_client.AdoptQueue("全队匹配中"), Is.True);
            PushBattleStart();
            PushBattleEnd();

            _client.AckBattleEnd();

            Assert.That(_net.CallsOf(MessageIds.JoinQueue), Has.Count.EqualTo(1),
                "随队开战须作废之前的单排记忆，不能在结算后独自排入旧队列");
        }

        [Test]
        public void ContinuousBattle_DoesNotAutoRequeueForRankedOrPvpModes()
        {
            _client.ContinuousBattle = true;
            _client.Tick(0);
            _client.JoinQueue(Match.MatchMode.PvpTeam, 5, 1);
            PushBattleStart();
            _net.PushNotify(MessageIds.NotifyBattleEnd, new BattleEndS2C
            {
                BattleId = TheBattleId,
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                SeasonId = 1,
                Settlement = new BattleSettlementData
                {
                    MatchMode = (uint)Match.MatchMode.PvpTeam,
                    CompetitiveRanked = true,
                },
            });

            int customTriggered = 0;
            _client.ContinuousMatchRequested = () => customTriggered++;
            _client.AckBattleEnd();

            Assert.That(customTriggered, Is.EqualTo(0), "正式排位结算收起不得触发连续战斗回调");
            Assert.That(_net.CallsOf(MessageIds.JoinQueue), Has.Count.EqualTo(1),
                "正式排位关闭结算页不得悄悄自动重排");
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.None));

            _client.ContinuousMatchRequested = null;
            _client.JoinQueue(Match.MatchMode.Polarity5V5, 110, 5);
            PushBattleStart();
            PushBattleEnd();
            _client.AckBattleEnd();

            Assert.That(_net.CallsOf(MessageIds.JoinQueue), Has.Count.EqualTo(2),
                "物法易势 5V5 关闭结算页同样禁止连续战斗自动续排");
        }

        [Test]
        public void SurrenderBattle_EscalationFields_And_NotifySurrenderVoteFlow()
        {
            Assert.That(DirectRoutingBattleTransport.IsDirectMessage(MessageIds.SurrenderBattle), Is.True,
                "SurrenderBattle 必须走战斗直连通道");

            var voteEvents = new List<BattleSurrenderVoteState>();
            _client.OnSurrenderVoteChanged += v => voteEvents.Add(v);

            var r1 = MakeState(false, MyId, EnemyId);
            r1.RoundIndex = 1;
            r1.RoundLimit = 20;
            r1.EscalationStartRound = 13;
            r1.EscalationStacks = 0;
            r1.SurrenderUnlockRound = 5;
            _net.PushNotify(MessageIds.NotifyBattleStart, new BattleStartS2C { BattleId = TheBattleId, State = r1 });

            Assert.That(_client.RoundLimit, Is.EqualTo(20u));
            Assert.That(_client.EscalationStartRound, Is.EqualTo(13u));
            Assert.That(_client.EscalationStacks, Is.EqualTo(0u));
            Assert.That(_client.SurrenderUnlockRound, Is.EqualTo(5u));
            Assert.That(_client.CanSurrenderNow, Is.False, "第 1 回合未满 5 回合不得允许本地点击投降");

            // 推进到第 13 回合（战意渐盛 1 层，且已解锁投降）
            var r13 = MakeState(false, MyId, EnemyId);
            r13.RoundIndex = 13;
            r13.RoundLimit = 20;
            r13.EscalationStartRound = 13;
            r13.EscalationStacks = 1;
            r13.SurrenderUnlockRound = 5;
            _net.PushNotify(MessageIds.NotifyTurnResult, new TurnResultS2C { BattleId = TheBattleId, RoundIndex = 12, State = r13 });
            _client.AckTurnPlayed();
            Assert.That(_client.EscalationStacks, Is.EqualTo(1u));
            Assert.That(_client.CanSurrenderNow, Is.True);

            bool? rpcOk = null;
            Assert.That(_client.SurrenderBattle(agree: true, ok => rpcOk = ok), Is.True);
            var calls = _net.CallsOf(MessageIds.SurrenderBattle);
            Assert.That(calls, Has.Count.EqualTo(1));
            var req = (SurrenderBattleRequest)calls[0].Request;
            Assert.That(req.BattleId, Is.EqualTo(TheBattleId));
            Assert.That(req.Agree, Is.True);

            var activeVote = new BattleSurrenderVoteState
            {
                Active = true,
                TeamIndex = 0,
                InitiatorPlayerId = MyId,
                InitiatedRound = 13,
                RequiredAgrees = 2,
            };
            activeVote.AgreedPlayerIds.Add(MyId);
            calls[0].Respond(new SurrenderBattleResponse { VoteState = activeVote });
            Assert.That(rpcOk, Is.True);
            Assert.That(_client.CurrentSurrenderVote, Is.Not.Null);
            Assert.That(_client.CurrentSurrenderVote.Active, Is.True);
            Assert.That(_client.CurrentSurrenderVote.RequiredAgrees, Is.EqualTo(2u));

            // 收到同队否决推送 -> 进入冷却至 R16，CanSurrenderNow 置 false
            var rejectedVote = new BattleSurrenderVoteState
            {
                Active = false,
                Rejected = true,
                TeamIndex = 0,
                NextAllowedRound = 16,
            };
            _net.PushNotify(MessageIds.NotifySurrenderVote, new SurrenderVoteS2C
            {
                BattleId = TheBattleId,
                VoteState = rejectedVote,
            });
            Assert.That(_client.CurrentSurrenderVote.Rejected, Is.True);
            Assert.That(_client.CurrentSurrenderVote.NextAllowedRound, Is.EqualTo(16u));
            Assert.That(_client.CanSurrenderNow, Is.False, "投降冷却期内本地应禁用投降按钮");

            PushBattleEnd();
            Assert.That(_client.CurrentSurrenderVote, Is.Null, "终局清场后必须清空投降状态");
            Assert.That(voteEvents, Has.Count.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void TacticalPrep_SwapSealAndPet_BlockActionsDuringPrep_AndRevealToRound1()
        {
            Assert.That(DirectRoutingBattleTransport.IsDirectMessage(MessageIds.UpdateBattlePrep), Is.True,
                "UpdateBattlePrep 必须走战斗直连通道");

            var prepEvents = new List<BattlePrepState>();
            _client.OnBattlePrepChanged += p => prepEvents.Add(p);

            // 1. 收到带战前 15 秒变阵态的开局包：即使 AutoBattleLatched=true 也不得在变阵期发 SetAutoBattle。
            _client.AutoBattleLatched = true;
            var prep = new BattlePrepState
            {
                Active = true,
                DeadlineMs = 999999999UL,
                DurationSeconds = 15u,
            };
            prep.SelfAvailableSeals.Add(new BattlePrepSealOption { SealId = 101u, EffectId = 1u, Name = "破阵锐印" });
            prep.SelfAvailableSeals.Add(new BattlePrepSealOption { SealId = 103u, EffectId = 6u, Name = "固阵守印" });
            prep.SelfAvailablePets.Add(new BattlePrepPetOption { PetId = 5001UL, PetTableId = 1u, PetName = "灵狐", Level = 30u });
            prep.SelfAvailablePets.Add(new BattlePrepPetOption { PetId = 5002UL, PetTableId = 2u, PetName = "石灵", Level = 30u });
            prep.TeamStates.Add(new BattlePrepPlayerState
            {
                PlayerId = MyId,
                TeamIndex = 0u,
                SelectedSealId = 101u,
                SelectedPetId = 5001UL,
                SelectedPetTableId = 1u,
                SelectedPetName = "灵狐",
                Ready = false,
            });

            var prepStartState = MakeState(false, MyId, EnemyId);
            prepStartState.RoundIndex = 1u;
            prepStartState.PendingActorIds.Clear();
            prepStartState.PrepState = prep;
            _net.PushNotify(MessageIds.NotifyBattleStart, new BattleStartS2C { BattleId = TheBattleId, State = prepStartState });

            Assert.That(_client.IsInPrepPhase, Is.True);
            Assert.That(_client.Phase, Is.EqualTo(BattlePhase.WaitingAction));
            Assert.That(_net.CallsOf(MessageIds.SetAutoBattle), Is.Empty, "变阵期不得补发自动战斗");
            Assert.That(_client.SubmitAction(new BattleAction { ActionType = eBattleActionType.BattleActionDefend }), Is.False,
                "变阵期本地必须拦截普通回合指令");

            // 2. 临场切换赛季印 103 + 首发宠 5002 并锁定出战。
            bool? prepOk = null;
            Assert.That(_client.UpdateBattlePrep(103u, 5002UL, true, ok => prepOk = ok), Is.True);
            var prepCalls = _net.CallsOf(MessageIds.UpdateBattlePrep);
            Assert.That(prepCalls, Has.Count.EqualTo(1));
            var req = (UpdateBattlePrepRequest)prepCalls[0].Request;
            Assert.That(req.SelectedSealId, Is.EqualTo(103u));
            Assert.That(req.SelectedPetId, Is.EqualTo(5002UL));
            Assert.That(req.Ready, Is.True);

            var lockedPrep = prep.Clone();
            lockedPrep.TeamStates[0].SelectedSealId = 103u;
            lockedPrep.TeamStates[0].SelectedPetId = 5002UL;
            lockedPrep.TeamStates[0].SelectedPetName = "石灵";
            lockedPrep.TeamStates[0].Ready = true;
            prepCalls[0].Respond(new UpdateBattlePrepResponse { PrepState = lockedPrep });
            Assert.That(prepOk, Is.True);
            Assert.That(_client.CurrentPrepState.TeamStates[0].SelectedSealId, Is.EqualTo(103u));
            Assert.That(_client.CurrentPrepState.TeamStates[0].Ready, Is.True);

            // 3. 收到同队实时变阵推送。
            var teamPush = lockedPrep.Clone();
            teamPush.TeamStates.Add(new BattlePrepPlayerState { PlayerId = 777UL, TeamIndex = 0u, SelectedSealId = 104u, Ready = true });
            _net.PushNotify(MessageIds.NotifyBattlePrepUpdate, new BattlePrepUpdateS2C { BattleId = TheBattleId, PrepState = teamPush });
            Assert.That(_client.CurrentPrepState.TeamStates, Has.Count.EqualTo(2));

            // 4. 双方锁定后服务端下发亮牌开局帧（PrepState 为空）：退出变阵期，恢复自动战斗补发与首回合指令提交。
            var revealedState = MakeState(false, MyId, EnemyId);
            revealedState.RoundIndex = 1u;
            _net.PushNotify(MessageIds.NotifyBattleStart, new BattleStartS2C { BattleId = TheBattleId, State = revealedState });
            Assert.That(_client.IsInPrepPhase, Is.False);
            Assert.That(_client.CurrentPrepState, Is.Null);
            Assert.That(_net.CallsOf(MessageIds.SetAutoBattle), Has.Count.EqualTo(1), "亮牌进第 1 回合后应正常补发自动战斗");
            Assert.That(_client.SubmitAction(new BattleAction { ActionType = eBattleActionType.BattleActionDefend }), Is.True);
            Assert.That(prepEvents, Has.Count.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void RankedV4_TeamTacticalPing_SendPushOverwriteRateLimitAndRoundClear()
        {
            Assert.That(DirectRoutingBattleTransport.IsDirectMessage(MessageIds.SendBattlePing), Is.True);
            Assert.That(RpcGuard.DefaultResolveService(MessageIds.SendBattlePing), Is.EqualTo("battle"));

            int pingEventCount = 0;
            _client.OnTeamPingsChanged += _ => pingEventCount++;
            PushBattleStart();

            // 1. 本人发送「集火」标点
            bool? pingOk = null;
            Assert.That(_client.SendBattlePing(EnemyId, BattlePingKind.BattlePingFocus, ok => pingOk = ok), Is.True);
            var calls = _net.CallsOf(MessageIds.SendBattlePing);
            Assert.That(calls, Has.Count.EqualTo(1));
            calls[0].Respond(new SendBattlePingResponse
            {
                Ping = new BattleTacticalPing
                {
                    SenderPlayerId = MyId,
                    TargetActorId = EnemyId,
                    Kind = BattlePingKind.BattlePingFocus,
                    RoundIndex = 1u,
                },
            });
            Assert.That(pingOk, Is.True);
            Assert.That(_client.CurrentTeamPings, Has.Count.EqualTo(1));
            Assert.That(_client.CurrentTeamPings[0].Kind, Is.EqualTo(BattlePingKind.BattlePingFocus));

            // 2. 队友推送「保核」+ 自己更新为「封控」（覆盖旧标点）
            _net.PushNotify(MessageIds.NotifyBattlePing, new BattlePingS2C
            {
                BattleId = TheBattleId,
                Ping = new BattleTacticalPing { SenderPlayerId = 777UL, TargetActorId = MyId, Kind = BattlePingKind.BattlePingProtect, RoundIndex = 1u },
            });
            _net.PushNotify(MessageIds.NotifyBattlePing, new BattlePingS2C
            {
                BattleId = TheBattleId,
                Ping = new BattleTacticalPing { SenderPlayerId = MyId, TargetActorId = EnemyId, Kind = BattlePingKind.BattlePingControl, RoundIndex = 1u },
            });
            Assert.That(_client.CurrentTeamPings, Has.Count.EqualTo(2));
            Assert.That(_client.CurrentTeamPings[0].Kind, Is.EqualTo(BattlePingKind.BattlePingControl));

            // 3. 限频返回 KRateLimitExceeded 给出明确文案
            bool? limitedOk = null;
            Assert.That(_client.SendBattlePing(EnemyId, BattlePingKind.BattlePingDispel, ok => limitedOk = ok), Is.True);
            _net.CallsOf(MessageIds.SendBattlePing)[1].Respond(new SendBattlePingResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)common_error.KRateLimitExceeded },
            });
            Assert.That(limitedOk, Is.False);
            Assert.That(_errors[_errors.Count - 1], Does.Contain("至多 2 次"));

            // 4. 回合推进新状态 TeamPings 为空 -> 自动清空旧回合标点
            PushTurnResult(myAuto: false);
            Assert.That(_client.CurrentTeamPings, Is.Empty);
            Assert.That(pingEventCount, Is.GreaterThanOrEqualTo(4));
        }
    }
}
