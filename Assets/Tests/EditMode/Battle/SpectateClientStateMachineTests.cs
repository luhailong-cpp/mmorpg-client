using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// SpectateClient 状态机纯逻辑测试(请求→首帧→逐回合→结束流转 / 迟到消息
    /// 丢弃 / 断线清状态 / 首帧超时),网络依赖经 <see cref="FakeBattleTransport"/> 注入。
    /// 依赖生成产物:MessageIds 观战常量(tools/gen_messageids.ps1 重跑)与
    /// battle/match 的 C# proto 类,未生成前不编译。
    /// </summary>
    public sealed class SpectateClientStateMachineTests
    {
        private const ulong MyId = 1001;
        private const ulong ActorA = 2002;
        private const ulong TheBattleId = 7700;

        private FakeBattleTransport _net;
        private FakeBattleChannel _channel;   // null = 无直连通道(永远就绪,既有用例)
        private SpectateClient _client;
        private List<SpectatePhase> _phases;
        private List<string> _errors;
        private int _states;
        private int _turns;
        private int _ends;

        [SetUp]
        public void SetUp() => Build(channel: null);

        private void UseChannel() => Build(new FakeBattleChannel());

        /// <summary>重建被测对象;传入假直连通道即进入收缩后口径(turn-based §22 D69 / D74)。</summary>
        private void Build(FakeBattleChannel channel)
        {
            _net = new FakeBattleTransport { PlayerId = MyId };
            _channel = channel;
            _client = new SpectateClient(_net, channel); // 直接 new,不经 Attach,避免污染单例
            _phases = new List<SpectatePhase>();
            _errors = new List<string>();
            _states = _turns = _ends = 0;
            _client.OnPhaseChanged += p => _phases.Add(p);
            _client.OnError += e => _errors.Add(e);
            _client.OnSpectateState += _ => _states++;
            _client.OnSpectateTurn += _ => _turns++;
            _client.OnSpectateEnd += _ => _ends++;
        }

        // ── 工具 ────────────────────────────────────────────

        private static BattleStateS2C MakeState(ulong battleId)
        {
            var state = new BattleStateS2C
            {
                BattleId = battleId,
                RoundIndex = 1,
                Outcome = eBattleOutcome.BattleOutcomeOngoing,
            };
            state.PendingActorIds.Add(ActorA);
            return state;
        }

        private void PushSpectateState(ulong battleId, uint observers = 1)
            => _net.PushNotify(MessageIds.NotifySpectateState, new SpectateStateS2C
            {
                State = MakeState(battleId),
                ObserverCount = observers,
            });

        private void PushSpectateTurn(ulong battleId)
            => _net.PushNotify(MessageIds.NotifySpectateTurnResult, new TurnResultS2C
            {
                BattleId = battleId,
                RoundIndex = 2,
                State = MakeState(battleId),
            });

        private void PushSpectateEnd(ulong battleId)
            => _net.PushNotify(MessageIds.NotifySpectateEnd, new SpectateEndS2C
            {
                BattleId = battleId,
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                Reason = eSpectateEndReason.SpectateEndBattleFinished,
            });

        /// <summary>直接推进到 Watching(请求成功 + 首帧)。</summary>
        private void EnterWatching()
        {
            _client.Tick(0);
            _client.WatchBattle(TheBattleId);
            _net.CallsOf(MessageIds.WatchBattle)[0]
                .Respond(new Match.WatchBattleResponse { BattleId = TheBattleId });
            PushSpectateState(TheBattleId);
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Watching));
        }

        // ── 相位流转 ────────────────────────────────────────

        [Test]
        public void WatchBattle_FullFlow_RequestFirstFrameTurnsEndAck()
        {
            _client.Tick(0);
            _client.WatchBattle(TheBattleId);

            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Requesting));
            var calls = _net.CallsOf(MessageIds.WatchBattle);
            Assert.That(calls, Has.Count.EqualTo(1));
            var req = (Match.WatchBattleRequest)calls[0].Request;
            Assert.That(req.PlayerId, Is.EqualTo(MyId));
            Assert.That(req.BattleId, Is.EqualTo(TheBattleId));

            calls[0].Respond(new Match.WatchBattleResponse { BattleId = TheBattleId });
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Requesting), "响应成功仍等首帧");

            PushSpectateState(TheBattleId, observers: 3);
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Watching), "首帧到达才算进入观战");
            Assert.That(_states, Is.EqualTo(1));
            Assert.That(_client.State, Is.Not.Null);
            Assert.That(_client.ObserverCount, Is.EqualTo(3));

            PushSpectateTurn(TheBattleId);
            Assert.That(_turns, Is.EqualTo(1));
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Watching), "观众无 Resolving,只读流不改相位");

            PushSpectateEnd(TheBattleId);
            Assert.That(_ends, Is.EqualTo(1));
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Ended), "Ended 停等 UI 收尾");

            _client.AckEnd();
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None));
            Assert.That(_client.State, Is.Null, "AckEnd 后本地观战状态清空");
            Assert.That(_phases, Is.EqualTo(new[]
            {
                SpectatePhase.Requesting, SpectatePhase.Watching,
                SpectatePhase.Ended, SpectatePhase.None,
            }));
        }

        [Test]
        public void WatchRandom_ResponseBackfillsBattleId()
        {
            _client.Tick(0);
            _client.WatchRandom();

            var req = (Match.WatchBattleRequest)_net.CallsOf(MessageIds.WatchBattle)[0].Request;
            Assert.That(req.BattleId, Is.EqualTo(0UL), "随机观战 battle_id=0");

            _net.CallsOf(MessageIds.WatchBattle)[0]
                .Respond(new Match.WatchBattleResponse { BattleId = TheBattleId });
            Assert.That(_client.WatchedBattleId, Is.EqualTo(TheBattleId), "响应回填实际战斗 id");

            PushSpectateState(9999);
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Requesting), "回填后不匹配的首帧丢弃");

            PushSpectateState(TheBattleId);
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Watching));
        }

        [Test]
        public void WatchRandom_FirstFrameBeforeResponse_AdoptsFrameBattleId()
        {
            _client.Tick(0);
            _client.WatchRandom();

            // Kafka 首帧与 gRPC 响应无序:首帧抢先到达时以首帧为准
            PushSpectateState(TheBattleId);
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Watching));
            Assert.That(_client.WatchedBattleId, Is.EqualTo(TheBattleId));

            // 迟到的成功响应不改状态
            _net.CallsOf(MessageIds.WatchBattle)[0]
                .Respond(new Match.WatchBattleResponse { BattleId = TheBattleId });
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Watching));
        }

        [Test]
        public void WatchBattle_WhileNotIdle_Rejected()
        {
            _client.Tick(0);
            _client.WatchBattle(TheBattleId);
            _client.WatchBattle(8888); // Requesting 中重复发起

            Assert.That(_net.CallsOf(MessageIds.WatchBattle), Has.Count.EqualTo(1));
            Assert.That(_errors, Has.Count.EqualTo(1));
        }

        // ── 错误与超时 ──────────────────────────────────────

        [Test]
        public void WatchBattle_ErrorTip_ReturnsNone()
        {
            _client.Tick(0);
            _client.WatchBattle(TheBattleId);
            _net.CallsOf(MessageIds.WatchBattle)[0].Respond(new Match.WatchBattleResponse
            {
                ErrorMessage = new TipInfoMessage { Id = 42 },
            });

            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None));
            Assert.That(_errors, Has.Count.EqualTo(1));
            Assert.That(_errors[0], Does.Contain("42"), "TipInfoMessage 统一走 OnError");
        }

        [Test]
        public void FirstFrameTimeout_ReturnsNoneWithError()
        {
            _client.Tick(0);
            _client.WatchBattle(TheBattleId);
            _net.CallsOf(MessageIds.WatchBattle)[0]
                .Respond(new Match.WatchBattleResponse { BattleId = TheBattleId });

            _client.Tick(SpectateClient.FirstFrameTimeoutSeconds - 0.1);
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Requesting), "未到期不超时");

            _client.Tick(SpectateClient.FirstFrameTimeoutSeconds);
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None), "首帧无下文 → 超时收敛");
            Assert.That(_errors, Is.Not.Empty);

            PushSpectateState(TheBattleId);
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None), "超时后的迟到首帧丢弃");
            Assert.That(_states, Is.EqualTo(0));
        }

        // ── 迟到消息丢弃 ────────────────────────────────────

        [Test]
        public void LateMessages_WrongBattleId_Discarded()
        {
            EnterWatching();

            PushSpectateTurn(9999);
            Assert.That(_turns, Is.EqualTo(0), "其它战斗的回合结果丢弃");

            PushSpectateEnd(9999);
            Assert.That(_ends, Is.EqualTo(0), "其它战斗的结束消息丢弃");
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Watching));
        }

        [Test]
        public void LateMessages_AfterAckEnd_Discarded()
        {
            EnterWatching();
            PushSpectateEnd(TheBattleId);
            _client.AckEnd();
            _states = _turns = _ends = 0;

            PushSpectateState(TheBattleId);
            PushSpectateTurn(TheBattleId);
            PushSpectateEnd(TheBattleId);

            Assert.That(_states + _turns + _ends, Is.EqualTo(0), "None 之后一律丢弃");
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None));
        }

        [Test]
        public void SpectateEnd_WhileRequesting_IsAccepted()
        {
            _client.Tick(0);
            _client.WatchBattle(TheBattleId);

            // 首帧未到战斗先收尾(AddObserver 与结束竞速):直接进 Ended
            PushSpectateEnd(TheBattleId);
            Assert.That(_ends, Is.EqualTo(1));
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Ended));
        }

        // ── 断线 ────────────────────────────────────────────

        [Test]
        public void Disconnect_ResetsToNone_AndDropsLateMessages()
        {
            EnterWatching();
            _net.RaiseDisconnected();

            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None));
            Assert.That(_client.State, Is.Null, "断线后本地观战状态作废");

            PushSpectateTurn(TheBattleId);
            Assert.That(_turns, Is.EqualTo(0), "断线后的迟到消息丢弃");
        }

        // ── 退出与列表 ──────────────────────────────────────

        [Test]
        public void StopWatch_SendsRequestAndReturnsNoneImmediately()
        {
            EnterWatching();
            _client.StopWatch();

            var calls = _net.CallsOf(MessageIds.StopWatchBattle);
            Assert.That(calls, Has.Count.EqualTo(1));
            Assert.That(((StopWatchBattleRequest)calls[0].Request).BattleId, Is.EqualTo(TheBattleId));
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None), "本地立即退出,不等响应");

            PushSpectateTurn(TheBattleId);
            Assert.That(_turns, Is.EqualTo(0), "退出后的迟到消息丢弃");
        }

        [Test]
        public void StopWatch_WhileRandomRequesting_DefersStopUntilBattleIdKnown()
        {
            _client.Tick(0);
            _client.WatchRandom();
            _client.StopWatch(); // 随机观战响应未回:battle_id 未知

            Assert.That(_net.CallsOf(MessageIds.StopWatchBattle), Is.Empty,
                "battle_id 未知时不许发 battle_id=0 的无效退出请求");
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None), "本地立即收敛,不等回填");

            // 迟到的 WatchBattle 响应带回实际 battle_id → 补发退出
            _net.CallsOf(MessageIds.WatchBattle)[0]
                .Respond(new Match.WatchBattleResponse { BattleId = TheBattleId });

            var stops = _net.CallsOf(MessageIds.StopWatchBattle);
            Assert.That(stops, Has.Count.EqualTo(1), "回填后恰好补发一条退出请求");
            Assert.That(((StopWatchBattleRequest)stops[0].Request).BattleId, Is.EqualTo(TheBattleId));
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None), "补发不改相位");
        }

        // ── 战斗直连通道(turn-based §22 D69 / D74) ───────

        /// <summary>带通道:请求成功 → 观众直连就绪 → 服务端握手快照(首帧)到达 → Watching。</summary>
        private void EnterWatchingViaChannel()
        {
            _client.Tick(0);
            _client.WatchBattle(TheBattleId);
            _net.CallsOf(MessageIds.WatchBattle)[0]
                .Respond(new Match.WatchBattleResponse { BattleId = TheBattleId });
            _channel.RaiseReady(TheBattleId, eBattleTicketRole.BattleTicketRoleObserver);
            PushSpectateState(TheBattleId);
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Watching));
        }

        [Test]
        public void ObserverChannelReady_FirstFrameFromServerSnapshot_NoClientPull()
        {
            UseChannel();
            _client.Tick(0);
            _client.WatchBattle(TheBattleId);
            _net.CallsOf(MessageIds.WatchBattle)[0]
                .Respond(new Match.WatchBattleResponse { BattleId = TheBattleId });

            _channel.RaiseReady(TheBattleId, eBattleTicketRole.BattleTicketRoleObserver);
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Requesting), "就绪本身不算首帧");
            Assert.That(_net.CallsOf(MessageIds.GetBattleState), Is.Empty, "首帧由服务端握手快照驱动(D69),客户端不补拉");

            PushSpectateState(TheBattleId, observers: 2); // 握手成功后经直连推来的快照
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Watching));
            Assert.That(_client.ObserverCount, Is.EqualTo(2));
        }

        [Test]
        public void StopWatch_ChannelNotReady_AbandonsWithoutRequest()
        {
            UseChannel();
            _client.Tick(0);
            _client.WatchBattle(TheBattleId);
            _net.CallsOf(MessageIds.WatchBattle)[0]
                .Respond(new Match.WatchBattleResponse { BattleId = TheBattleId });

            _client.StopWatch();
            Assert.That(_net.CallsOf(MessageIds.StopWatchBattle), Is.Empty, "未就绪不发(gate 不中继战斗)");
            Assert.That(_channel.Abandons, Is.EqualTo(new[] { TheBattleId }), "放弃该局直连");
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None), "本地立即收敛");
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void StopWatch_NotReady_LateAssignmentThenReady_SendsDeferredStop()
        {
            // 退出时分配包还没到(Abandon 无事);分配包晚到、直连随后就绪 → 补发退出
            UseChannel();
            _client.Tick(0);
            _client.WatchBattle(TheBattleId);
            _net.CallsOf(MessageIds.WatchBattle)[0]
                .Respond(new Match.WatchBattleResponse { BattleId = TheBattleId });
            _client.StopWatch();

            _channel.RaiseReady(TheBattleId, eBattleTicketRole.BattleTicketRoleObserver);
            var stops = _net.CallsOf(MessageIds.StopWatchBattle);
            Assert.That(stops, Has.Count.EqualTo(1));
            Assert.That(((StopWatchBattleRequest)stops[0].Request).BattleId, Is.EqualTo(TheBattleId));

            _channel.RaiseReady(TheBattleId, eBattleTicketRole.BattleTicketRoleObserver);
            Assert.That(_net.CallsOf(MessageIds.StopWatchBattle), Has.Count.EqualTo(1), "只补发一次");
        }

        [Test]
        public void StopWatch_ThenRewatchSameBattle_NoDeferredStopOnReady()
        {
            UseChannel();
            _client.Tick(0);
            _client.WatchBattle(TheBattleId);
            _net.CallsOf(MessageIds.WatchBattle)[0]
                .Respond(new Match.WatchBattleResponse { BattleId = TheBattleId });
            _client.StopWatch();

            _client.WatchBattle(TheBattleId); // 反悔:再次观战同一场
            _channel.RaiseReady(TheBattleId, eBattleTicketRole.BattleTicketRoleObserver);
            Assert.That(_net.CallsOf(MessageIds.StopWatchBattle), Is.Empty, "新观战作废待补退出");
        }

        [Test]
        public void StopWatch_ChannelReady_SendsRequestOverChannel()
        {
            UseChannel();
            EnterWatchingViaChannel();
            _client.StopWatch();
            Assert.That(_net.CallsOf(MessageIds.StopWatchBattle), Has.Count.EqualTo(1));
            Assert.That(_channel.Abandons, Is.Empty);
        }

        [Test]
        public void RandomStopPending_ResponseArrives_NotReady_Abandons()
        {
            UseChannel();
            _client.Tick(0);
            _client.WatchRandom();
            _client.StopWatch(); // battle_id 未知:挂待补
            _net.CallsOf(MessageIds.WatchBattle)[0]
                .Respond(new Match.WatchBattleResponse { BattleId = TheBattleId });

            Assert.That(_net.CallsOf(MessageIds.StopWatchBattle), Is.Empty);
            Assert.That(_channel.Abandons, Is.EqualTo(new[] { TheBattleId }));
        }

        [Test]
        public void ChannelLost_WhileWatching_ConvergesNone()
        {
            UseChannel();
            EnterWatchingViaChannel();
            _channel.RaiseLost(TheBattleId, BattleLinkCloseKind.Unreachable);

            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None));
            Assert.That(_client.State, Is.Null);
            Assert.That(_errors, Has.Count.EqualTo(1));
            Assert.That(_errors[0], Does.Contain("无法连接战斗服务器"));
        }

        [Test]
        public void ChannelLost_BattleGone_WhileRequesting_ConvergesNone()
        {
            UseChannel();
            _client.Tick(0);
            _client.WatchBattle(TheBattleId);
            _channel.RaiseLost(TheBattleId, BattleLinkCloseKind.BattleGone);

            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None));
            Assert.That(_errors, Has.Count.EqualTo(1));
            Assert.That(_errors[0], Does.Contain("已结束"));
        }

        [Test]
        public void ChannelSuperseded_WhileWatching_ConvergesNone_CanWatchAgain()
        {
            // 观战时进了自己的 gather:参战分配包把直连换到别的局。RemoveObserver 丢了 / SpectateEnd 晚于
            // 分配包随旧连接一起丢掉,观战帧再也不会来(收缩后只走直连)—— 不能永远停在 Watching
            UseChannel();
            EnterWatchingViaChannel();
            _channel.RaiseLost(TheBattleId, BattleLinkCloseKind.Superseded);

            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None));
            Assert.That(_client.State, Is.Null);
            Assert.That(_client.WatchedBattleId, Is.EqualTo(0UL));
            Assert.That(_errors, Has.Count.EqualTo(1));

            _client.WatchBattle(TheBattleId + 1);
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Requesting), "相位闸已放开,可以再次观战");
        }

        [Test]
        public void ChannelSuperseded_WhileRequesting_ConvergesNone()
        {
            UseChannel();
            _client.Tick(0);
            _client.WatchBattle(TheBattleId);
            _channel.RaiseLost(TheBattleId, BattleLinkCloseKind.Superseded);

            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None));
            Assert.That(_errors, Has.Count.EqualTo(1));
        }

        [Test]
        public void ChannelLost_EndedHostClosedOrOtherBattle_Ignored()
        {
            UseChannel();
            EnterWatchingViaChannel();
            _channel.RaiseLost(TheBattleId, BattleLinkCloseKind.Ended);
            _channel.RaiseLost(TheBattleId, BattleLinkCloseKind.HostClosed);
            _channel.RaiseLost(9999, BattleLinkCloseKind.Unreachable);
            _channel.RaiseLost(9999, BattleLinkCloseKind.Superseded);
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Watching));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void FirstFrameTimeout_WithChannelNotReady_AbandonsLink()
        {
            UseChannel();
            _client.Tick(0);
            _client.WatchBattle(TheBattleId);
            _net.CallsOf(MessageIds.WatchBattle)[0]
                .Respond(new Match.WatchBattleResponse { BattleId = TheBattleId });

            _client.Tick(SpectateClient.FirstFrameTimeoutSeconds);
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None));
            Assert.That(_channel.Abandons, Is.EqualTo(new[] { TheBattleId }),
                "直连还在后台重连:放弃它,免得握手成功后服务端继续推帧");
            Assert.That(_net.CallsOf(MessageIds.StopWatchBattle), Is.Empty);
        }

        [Test]
        public void RefreshList_RaisesOnListUpdated()
        {
            Match.ListWatchableBattlesResponse got = null;
            _client.OnListUpdated += resp => got = resp;

            _client.RefreshList(5);
            var calls = _net.CallsOf(MessageIds.ListWatchableBattles);
            Assert.That(calls, Has.Count.EqualTo(1));
            Assert.That(((Match.ListWatchableBattlesRequest)calls[0].Request).Limit, Is.EqualTo(5U));

            var resp = new Match.ListWatchableBattlesResponse();
            resp.Battles.Add(new Match.BattleWatchSummary { BattleId = TheBattleId });
            calls[0].Respond(resp);

            Assert.That(got, Is.Not.Null);
            Assert.That(got.Battles, Has.Count.EqualTo(1));
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None), "列表拉取不改相位");
        }
    }
}
