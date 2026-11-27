using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
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

        [Test]
        public void PreviousWatchReplyCannotOverwriteNewWatchRequest()
        {
            _client.Tick(0);
            _client.WatchBattle(100);
            var first = _net.CallsOf(MessageIds.WatchBattle)[0];
            _client.StopWatch();
            _client.Tick(1);
            _client.WatchBattle(200);
            var second = _net.CallsOf(MessageIds.WatchBattle)[1];
            Assert.That(((Match.WatchBattleRequest)first.Request).RequestId,
                Is.Not.EqualTo(((Match.WatchBattleRequest)second.Request).RequestId));
            first.Respond(new Match.WatchBattleResponse { BattleId = 100 });
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Requesting));
            Assert.That(_client.WatchedBattleId, Is.EqualTo(200));
            Assert.That(_errors, Is.Empty);
        }

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

        [Test]
        public void ResetClearsWatchingStateAndInvalidatesPendingWatchRequest()
        {
            UseChannel();
            EnterWatchingViaChannel();
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Watching));

            // 静默重连 / 跨区重定向关闭直连(HostClosed)但大厅不发 Disconnected:宿主 Reset 必须清掉 Watching。
            _client.Reset();
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None));
            Assert.That(_client.State, Is.Null);
            Assert.That(_client.ObserverCount, Is.Zero);
            Assert.That(_client.WatchedBattleId, Is.Zero);

            // Reset 后的 Requesting 迟到响应与错误回调都必须被忽略。
            _client.WatchBattle(TheBattleId + 1);
            var call = _net.CallsOf(MessageIds.WatchBattle)[1];
            _client.Reset();
            call.Respond(new Match.WatchBattleResponse { BattleId = TheBattleId + 1 });
            call.FailWith("disconnected");
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None));
            Assert.That(_client.WatchedBattleId, Is.Zero);
            Assert.That(_errors, Is.Empty);
        }

        // ── LOBBY 档(gate 中继 / 全服广播,§6.1 / CODEX §6) ────

        private static readonly ByteString SampleLobbyTicket = ByteString.CopyFromUtf8("signed-spectate-ticket");

        private void EnterWatchingLobby(ulong battleId = TheBattleId)
        {
            _client.Tick(0);
            _client.WatchBattle(battleId);
            _net.CallsOf(MessageIds.WatchBattle)[0].Respond(new Match.WatchBattleResponse
            {
                BattleId = battleId,
                Delivery = Match.eSpectateDelivery.SpectateDeliveryLobby,
                SpectateTicket = SampleLobbyTicket,
            });
            _net.CallsOf(GateClientSpectateRpc.SubscribeSpectateMessageId)[0]
                .Respond(new SubscribeSpectateResponse());
            PushSpectateState(battleId, observers: 0);
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Watching));
            Assert.That(_client.Delivery, Is.EqualTo(Match.eSpectateDelivery.SpectateDeliveryLobby));
        }

        [Test]
        public void LobbyDelivery_SubscribesReceives161_158_166AndSourceLost()
        {
            UseChannel();
            SpectateEndS2C lastEnd = null;
            _client.OnSpectateEnd += e => lastEnd = e;

            _client.Tick(0);
            _client.WatchBattle(TheBattleId);
            _net.CallsOf(MessageIds.WatchBattle)[0].Respond(new Match.WatchBattleResponse
            {
                BattleId = TheBattleId,
                Delivery = Match.eSpectateDelivery.SpectateDeliveryLobby,
                SpectateTicket = SampleLobbyTicket,
            });

            var subs = _net.CallsOf(GateClientSpectateRpc.SubscribeSpectateMessageId);
            Assert.That(subs, Has.Count.EqualTo(1));
            var subReq = (SubscribeSpectateRequest)subs[0].Request;
            Assert.That(subReq.BattleId, Is.EqualTo(TheBattleId));
            Assert.That(subReq.SpectateTicket, Is.EqualTo(SampleLobbyTicket));

            subs[0].Respond(new SubscribeSpectateResponse());
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Requesting), "订阅回执后仍等 161 首帧");

            // 161 首帧 → Watching;直连断开不影响 LOBBY 观战
            PushSpectateState(TheBattleId, observers: 0);
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Watching));
            _channel.RaiseLost(TheBattleId, BattleLinkCloseKind.Unreachable);
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Watching), "LOBBY 档忽略直连断开事件");
            Assert.That(_errors, Is.Empty);

            // 158 回合帧 → 166 SOURCE_LOST
            PushSpectateTurn(TheBattleId);
            Assert.That(_turns, Is.EqualTo(1));
            _net.PushNotify(MessageIds.NotifySpectateEnd, new SpectateEndS2C
            {
                BattleId = TheBattleId,
                Outcome = eBattleOutcome.BattleOutcomeOngoing,
                Reason = eSpectateEndReason.SpectateEndSourceLost,
            });
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Ended));
            Assert.That(lastEnd?.Reason, Is.EqualTo(eSpectateEndReason.SpectateEndSourceLost));
            Assert.That(SpectateClient.DescribeEndReason(lastEnd), Does.Contain("连接中断"));
            Assert.That(SpectateClient.DescribeEndReason(new SpectateEndS2C
            {
                BattleId = TheBattleId,
                Reason = (eSpectateEndReason)99,
            }), Does.Contain("已作废"), "未知结束原因按 ABORTED 处理(§6.1)");
        }

        [Test]
        public void LobbyDelivery_NewTips16021And16024_SurfaceReadableErrors()
        {
            _client.Tick(0);
            _client.WatchBattle(TheBattleId);
            _net.CallsOf(MessageIds.WatchBattle)[0].Respond(new Match.WatchBattleResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)match_error.KMatchSpectateFull },
            });
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None));
            Assert.That(_errors[0], Does.Contain("观战人数已满"));
            Assert.That(_errors[0], Does.Contain("16021"));

            _client.WatchBattle(TheBattleId);
            _net.CallsOf(MessageIds.WatchBattle)[1].Respond(new Match.WatchBattleResponse
            {
                BattleId = TheBattleId,
                Delivery = Match.eSpectateDelivery.SpectateDeliveryLobby,
                SpectateTicket = SampleLobbyTicket,
            });
            _net.CallsOf(GateClientSpectateRpc.SubscribeSpectateMessageId)[0].Respond(new SubscribeSpectateResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)match_error.KMatchSpectateTicketInvalid },
            });
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None));
            Assert.That(_errors[1], Does.Contain("观战凭证无效"));
            Assert.That(_errors[1], Does.Contain("16024"));
        }

        [Test]
        public void LobbyDelivery_StopWatchAndTimeout_SendUnsubscribeNotStopWatchBattle()
        {
            UseChannel();
            EnterWatchingLobby();

            _client.StopWatch();
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None));
            Assert.That(_net.CallsOf(MessageIds.StopWatchBattle), Is.Empty);
            Assert.That(_channel.Abandons, Is.Empty);
            var unsubs = _net.CallsOf(GateClientSpectateRpc.UnsubscribeSpectateMessageId);
            Assert.That(unsubs, Has.Count.EqualTo(1));
            Assert.That(((UnsubscribeSpectateRequest)unsubs[0].Request).BattleId, Is.EqualTo(TheBattleId));

            // 订阅在途期间主动退出:订阅回执成功后立即补发退订
            _client.WatchBattle(TheBattleId + 1);
            _net.CallsOf(MessageIds.WatchBattle)[1].Respond(new Match.WatchBattleResponse
            {
                BattleId = TheBattleId + 1,
                Delivery = Match.eSpectateDelivery.SpectateDeliveryLobby,
                SpectateTicket = SampleLobbyTicket,
            });
            _client.StopWatch();
            Assert.That(_net.CallsOf(GateClientSpectateRpc.UnsubscribeSpectateMessageId), Has.Count.EqualTo(1));
            _net.CallsOf(GateClientSpectateRpc.SubscribeSpectateMessageId)[1].Respond(new SubscribeSpectateResponse());
            Assert.That(_net.CallsOf(GateClientSpectateRpc.UnsubscribeSpectateMessageId), Has.Count.EqualTo(2));

            // LOBBY 首帧超时:已订阅时自动退订
            _client.Tick(10);
            _client.WatchBattle(TheBattleId + 2);
            _net.CallsOf(MessageIds.WatchBattle)[2].Respond(new Match.WatchBattleResponse
            {
                BattleId = TheBattleId + 2,
                Delivery = Match.eSpectateDelivery.SpectateDeliveryLobby,
                SpectateTicket = SampleLobbyTicket,
            });
            _net.CallsOf(GateClientSpectateRpc.SubscribeSpectateMessageId)[2].Respond(new SubscribeSpectateResponse());
            _client.Tick(10 + SpectateClient.FirstFrameTimeoutSeconds);
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None));
            Assert.That(_net.CallsOf(GateClientSpectateRpc.UnsubscribeSpectateMessageId), Has.Count.EqualTo(3));
        }

        [Test]
        public void FrameDedup_158ComparesOnlyWith158_And161DoesNotAdvanceTurnWatermark()
        {
            EnterWatchingLobby();
            // EnterWatchingLobby 已推 STATE(r=1);紧接着 TURN(r=1) 必须照常处理(161 不推进 158 水位)
            _net.PushNotify(MessageIds.NotifySpectateTurnResult, new TurnResultS2C
            {
                BattleId = TheBattleId,
                RoundIndex = 1,
                State = new BattleStateS2C { BattleId = TheBattleId, RoundIndex = 2, Outcome = eBattleOutcome.BattleOutcomeOngoing },
            });
            Assert.That(_turns, Is.EqualTo(1), "STATE(1) 后紧跟的 TURN(1) 不得被误判重复");
            Assert.That(_client.State.RoundIndex, Is.EqualTo(2));

            // 重复 TURN(1)(例如 L1→L2 升档重叠)必须丢弃
            _net.PushNotify(MessageIds.NotifySpectateTurnResult, new TurnResultS2C
            {
                BattleId = TheBattleId,
                RoundIndex = 1,
                State = new BattleStateS2C { BattleId = TheBattleId, RoundIndex = 2, Outcome = eBattleOutcome.BattleOutcomeOngoing },
            });
            Assert.That(_turns, Is.EqualTo(1), "重复 TURN(1) 应被去重丢弃");

            // 迟到的旧 STATE(1) 不得回退状态
            int statesBefore = _states;
            PushSpectateState(TheBattleId, observers: 5);
            Assert.That(_states, Is.EqualTo(statesBefore), "迟到的 STATE(1) 不应覆盖已推进到 r=2 的状态");
            Assert.That(_client.State.RoundIndex, Is.EqualTo(2));

            // 下一回合心跳 STATE(2) 可刷新但不推进 158 水位,随后的 TURN(2) 照常处理
            _net.PushNotify(MessageIds.NotifySpectateState, new SpectateStateS2C
            {
                State = new BattleStateS2C { BattleId = TheBattleId, RoundIndex = 2, Outcome = eBattleOutcome.BattleOutcomeOngoing },
                ObserverCount = 0,
            });
            Assert.That(_states, Is.EqualTo(statesBefore + 1));
            _net.PushNotify(MessageIds.NotifySpectateTurnResult, new TurnResultS2C
            {
                BattleId = TheBattleId,
                RoundIndex = 2,
                State = new BattleStateS2C { BattleId = TheBattleId, RoundIndex = 3, Outcome = eBattleOutcome.BattleOutcomeOngoing },
            });
            Assert.That(_turns, Is.EqualTo(2));
        }

        [Test]
        public void LobbyReconnect_ReusesOriginalRequestIdToRewatchAndResubscribe()
        {
            EnterWatchingLobby();
            var originalReq = (Match.WatchBattleRequest)_net.CallsOf(MessageIds.WatchBattle)[0].Request;

            Assert.That(_client.TryCaptureLobbyReconnectContext(out ulong battleId, out string requestId), Is.True);
            Assert.That(battleId, Is.EqualTo(TheBattleId));
            Assert.That(requestId, Is.EqualTo(originalReq.RequestId));

            // 模拟断线重置后大厅重连恢复
            _client.Reset();
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.None));

            _client.ResumeLobbyWatch(battleId, requestId);
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Requesting));
            var replayReq = (Match.WatchBattleRequest)_net.CallsOf(MessageIds.WatchBattle)[1].Request;
            Assert.That(replayReq.BattleId, Is.EqualTo(TheBattleId));
            Assert.That(replayReq.RequestId, Is.EqualTo(originalReq.RequestId), "重连重发必须复用原 request_id");

            // DIRECT 档不走 LOBBY 重连续订捕获
            _client.Reset();
            EnterWatching();
            Assert.That(_client.TryCaptureLobbyReconnectContext(out _, out _), Is.False);
        }

        [Test]
        public void TurnResult_BattleIdZero_FallsBackToStateBattleId()
        {
            EnterWatchingLobby();
            // ev.BattleId == 0 时回退校验 ev.State.BattleId,防它场帧串入
            _net.PushNotify(MessageIds.NotifySpectateTurnResult, new TurnResultS2C
            {
                BattleId = 0,
                RoundIndex = 1,
                State = new BattleStateS2C { BattleId = TheBattleId + 99, RoundIndex = 2, Outcome = eBattleOutcome.BattleOutcomeOngoing },
            });
            Assert.That(_turns, Is.EqualTo(0), "ev.BattleId=0 但 State.BattleId 是别场时必须丢弃");

            _net.PushNotify(MessageIds.NotifySpectateTurnResult, new TurnResultS2C
            {
                BattleId = 0,
                RoundIndex = 1,
                State = new BattleStateS2C { BattleId = TheBattleId, RoundIndex = 2, Outcome = eBattleOutcome.BattleOutcomeOngoing },
            });
            Assert.That(_turns, Is.EqualTo(1));
        }

        [Test]
        public void FrameDedup_SynthesizedStateRPlus1_DropsOlderTurnR_WhileNormalStateR_AcceptsTurnR()
        {
            _client.Tick(0);
            _client.WatchBattle(TheBattleId);
            _net.CallsOf(MessageIds.WatchBattle)[0].Respond(new Match.WatchBattleResponse
            {
                BattleId = TheBattleId,
                Delivery = Match.eSpectateDelivery.SpectateDeliveryLobby,
                SpectateTicket = SampleLobbyTicket,
            });
            _net.CallsOf(GateClientSpectateRpc.SubscribeSpectateMessageId)[0].Respond(new SubscribeSpectateResponse());

            // 首帧是 gate 由缓存 TURN(3) 合成的 STATE(4)
            _net.PushNotify(MessageIds.NotifySpectateState, new SpectateStateS2C
            {
                State = new BattleStateS2C { BattleId = TheBattleId, RoundIndex = 4, Outcome = eBattleOutcome.BattleOutcomeOngoing },
                ObserverCount = 0,
            });
            Assert.That(_client.Phase, Is.EqualTo(SpectatePhase.Watching));

            // 升档重叠迟到一帧已结算的 TURN(3):应丢弃不重播
            _net.PushNotify(MessageIds.NotifySpectateTurnResult, new TurnResultS2C
            {
                BattleId = TheBattleId,
                RoundIndex = 3,
                State = new BattleStateS2C { BattleId = TheBattleId, RoundIndex = 4, Outcome = eBattleOutcome.BattleOutcomeOngoing },
            });
            Assert.That(_turns, Is.EqualTo(0), "已推进到 STATE(4) 时迟到的 TURN(3) 应丢弃");

            // 紧接着的 TURN(4) 必须正常处理(§6.1)
            _net.PushNotify(MessageIds.NotifySpectateTurnResult, new TurnResultS2C
            {
                BattleId = TheBattleId,
                RoundIndex = 4,
                State = new BattleStateS2C { BattleId = TheBattleId, RoundIndex = 5, Outcome = eBattleOutcome.BattleOutcomeOngoing },
            });
            Assert.That(_turns, Is.EqualTo(1));
        }
    }
}
