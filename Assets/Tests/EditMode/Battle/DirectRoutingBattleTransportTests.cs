using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// <see cref="DirectRoutingBattleTransport"/> 分流规则测试(turn-based §22 D74 收缩后口径):
    /// 四条战斗 RPC 只走直连,未就绪时本地快速失败、绝不走大厅;其余永远走大厅;
    /// S2C 两条路都能到达同一处理器(容错);三条"本局结束"信号与重连提示正确转给
    /// <see cref="BattleDirectLink"/>;链路验证 / 终结转成 <see cref="IBattleChannel"/> 的 Ready / Lost。
    /// </summary>
    public sealed class DirectRoutingBattleTransportTests
    {
        private const ulong TheBattleId = 7700;

        private FakeBattleTransport _lobby;
        private List<FakeFramedConnection> _conns;
        private BattleDirectLink _link;
        private DirectRoutingBattleTransport _transport;
        private List<ulong> _reissueRequests;
        private Action<BattleAssignedS2C> _reissueOk;
        private Action<uint, string> _reissueFail;
        private List<string> _log;
        private double _now;

        [SetUp]
        public void SetUp()
        {
            _lobby = new FakeBattleTransport();
            _conns = new List<FakeFramedConnection>();
            _reissueRequests = new List<ulong>();
            _reissueOk = null;
            _reissueFail = null;
            _log = new List<string>();
            _now = 100;
            _link = new BattleDirectLink(() => { var c = new FakeFramedConnection(); _conns.Add(c); return c; },
                unixNowMs: () => 1_000_000, connectRunner: a => a(), random01: () => 0.5);
            _link.TicketReissuer = (id, ok, fail) =>
            {
                _reissueRequests.Add(id);
                _reissueOk = ok;
                _reissueFail = fail;
                return true;
            };
            _transport = new DirectRoutingBattleTransport(_lobby, _link, s => _log.Add(s));
        }

        private FakeFramedConnection Current => _conns[_conns.Count - 1];

        private void Tick(double advance = 0)
        {
            _now += advance;
            _link.Tick(_now);
        }

        private FakeFramedConnection ConnectAndVerify()
        {
            _link.HandleAssigned(new BattleAssignedS2C
            {
                BattleId = TheBattleId,
                Host = "10.0.0.5",
                Port = 20050,
                TokenPayload = ByteString.CopyFromUtf8("p"),
                TokenSignature = ByteString.CopyFromUtf8("s"),
                ExpireAtMs = 4_000_000_000_000UL,
                Role = eBattleTicketRole.BattleTicketRoleParticipant,
            });
            Tick();
            Current.PushInbound(new BattleTokenVerifyResponse { Success = true, BattleId = TheBattleId });
            Tick();
            Assert.IsTrue(_link.IsVerified);
            return Current;
        }

        private static MessageContent Notify(uint messageId, IMessage body)
            => new MessageContent { Id = 0, MessageId = messageId, SerializedMessage = body.ToByteString() };

        [Test]
        public void IsDirectMessage_OnlyTheFourBattleRpcs()
        {
            Assert.IsTrue(DirectRoutingBattleTransport.IsDirectMessage(MessageIds.SubmitBattleAction));
            Assert.IsTrue(DirectRoutingBattleTransport.IsDirectMessage(MessageIds.GetBattleState));
            Assert.IsTrue(DirectRoutingBattleTransport.IsDirectMessage(MessageIds.StopWatchBattle));
            Assert.IsTrue(DirectRoutingBattleTransport.IsDirectMessage(MessageIds.SetAutoBattle));
            Assert.IsFalse(DirectRoutingBattleTransport.IsDirectMessage(MessageIds.JoinQueue));
            Assert.IsFalse(DirectRoutingBattleTransport.IsDirectMessage(MessageIds.WatchBattle));
            Assert.IsFalse(DirectRoutingBattleTransport.IsDirectMessage(MessageIds.RequestBattleTicket));
        }

        [Test]
        public void BeforeVerified_BattleRpc_FailsFast_NeverLobby()
        {
            // gate 不中继战斗:大厅上的战斗请求只会换来 id=0 的 TipToClient、挂满 15s 超时
            foreach (var id in new[] { MessageIds.SubmitBattleAction, MessageIds.GetBattleState,
                                       MessageIds.StopWatchBattle, MessageIds.SetAutoBattle })
            {
                string err = null;
                _transport.Call(id, new GetBattleStateRequest(), BattleStateS2C.Parser,
                    _ => Assert.Fail("不该成功"), e => err = e);
                Assert.AreEqual(BattleDirectLink.NotReadyError, err, $"message_id={id} 同步本地失败");
            }
            Assert.AreEqual(0, _lobby.Calls.Count, "绝不走大厅");
            Assert.IsEmpty(_conns, "也不建任何连接");
        }

        [Test]
        public void Handshaking_BattleRpc_FailsFast_NeverLobby()
        {
            _link.HandleAssigned(new BattleAssignedS2C
            {
                BattleId = TheBattleId, Host = "10.0.0.5", Port = 20050,
                TokenPayload = ByteString.CopyFromUtf8("p"), TokenSignature = ByteString.CopyFromUtf8("s"),
                ExpireAtMs = 4_000_000_000_000UL, Role = eBattleTicketRole.BattleTicketRoleParticipant,
            });
            Tick(); // 握手包已发,未验证
            string err = null;
            _transport.Call(MessageIds.SetAutoBattle, new SetAutoBattleRequest(), SetAutoBattleResponse.Parser,
                _ => Assert.Fail("不该成功"), e => err = e);
            Assert.AreEqual(BattleDirectLink.NotReadyError, err);
            Assert.AreEqual(0, _lobby.Calls.Count);
            Assert.AreEqual(1, Current.Sent.Count, "直连上只有握手包");
        }

        [Test]
        public void AfterVerified_BattleRpcGoesToLink_OthersStayOnLobby()
        {
            var conn = ConnectAndVerify();

            _transport.Call(MessageIds.SubmitBattleAction, new SubmitBattleActionRequest { BattleId = TheBattleId },
                SubmitBattleActionResponse.Parser, _ => { }, _ => { });
            Assert.AreEqual(0, _lobby.Calls.Count);
            var req = conn.LastSent<ClientRequest>();
            Assert.IsNotNull(req);
            Assert.AreEqual(MessageIds.SubmitBattleAction, req.MessageId);

            _transport.Call(MessageIds.JoinQueue, new Match.JoinQueueRequest(), Match.JoinQueueResponse.Parser, _ => { }, _ => { });
            Assert.AreEqual(1, _lobby.Calls.Count, "排队走大厅");
            Assert.AreEqual(1, conn.CountSent<ClientRequest>());

            _transport.SendOneWay(MessageIds.SetAutoBattle, new SetAutoBattleRequest());
            Assert.AreEqual(2, conn.CountSent<ClientRequest>());
            Assert.AreEqual(0, _lobby.OneWays.Count);
        }

        [Test]
        public void AfterLinkClosed_BattleRpc_FailsFast_NeverLobby()
        {
            var conn = ConnectAndVerify();
            _link.HandleBattleEnded(TheBattleId);
            conn.PushDisconnect();
            Tick();
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);

            string err = null;
            _transport.Call(MessageIds.GetBattleState, new GetBattleStateRequest(), BattleStateS2C.Parser,
                _ => Assert.Fail("不该成功"), e => err = e);
            Assert.AreEqual(BattleDirectLink.NotReadyError, err);
            Assert.AreEqual(0, _lobby.Calls.Count);
        }

        [Test]
        public void SendOneWay_BeforeVerified_DroppedNotLobby()
        {
            _transport.SendOneWay(MessageIds.SetAutoBattle, new SetAutoBattleRequest { BattleId = TheBattleId });
            Assert.IsEmpty(_lobby.OneWays, "战斗单向消息未就绪即丢弃,不发大厅");
            Assert.IsNotEmpty(_log, "丢弃要留日志");

            // 非战斗消息照旧走大厅
            _transport.SendOneWay(MessageIds.JoinQueue, new Match.JoinQueueRequest());
            Assert.AreEqual(1, _lobby.OneWays.Count);
        }

        [Test]
        public void Notify_ReachesHandler_FromEitherPath()
        {
            var conn = ConnectAndVerify();
            int hits = 0;
            _transport.RegisterNotify(MessageIds.NotifyTurnResult, _ => hits++);

            conn.PushInbound(Notify(MessageIds.NotifyTurnResult, new TurnResultS2C { BattleId = TheBattleId }));
            Tick();
            Assert.AreEqual(1, hits, "直连到达");

            _lobby.PushNotify(MessageIds.NotifyTurnResult, new TurnResultS2C { BattleId = TheBattleId });
            Assert.AreEqual(2, hits, "容错:大厅上到达的同号推送(旧模式服务端 / 滚动升级窗口)也能处理");
        }

        [Test]
        public void NotifyBattleEnd_ViaLobby_SceneSettlement_ReachesHandler_AndMarksEnded()
        {
            // scene 结算后经大厅推的 NotifyBattleEnd(D68 保留的大厅下行)
            var conn = ConnectAndVerify();
            int ends = 0;
            _transport.RegisterNotify(MessageIds.NotifyBattleEnd, _ => ends++);
            _lobby.PushNotify(MessageIds.NotifyBattleEnd, new BattleEndS2C { BattleId = TheBattleId });
            Assert.AreEqual(1, ends);

            conn.PushDisconnect();
            Tick();
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State, "之后的 FIN 是正常收尾");
            Assert.IsEmpty(_reissueRequests);
        }

        [Test]
        public void NotifyBattleEnd_MarksLinkEnded_ServerFinIsCleanClose()
        {
            var conn = ConnectAndVerify();
            int ends = 0;
            _transport.RegisterNotify(MessageIds.NotifyBattleEnd, _ => ends++);

            conn.PushInbound(Notify(MessageIds.NotifyBattleEnd, new BattleEndS2C { BattleId = TheBattleId }));
            conn.PushDisconnect(); // 服务端:终局包 → FIN
            Tick();

            Assert.AreEqual(1, ends);
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);
            Assert.IsEmpty(_reissueRequests);
            Tick(BattleDirectLink.ReconnectDelaySeconds + 1);
            Assert.AreEqual(1, _conns.Count, "不重连");
        }

        [Test]
        public void NotifySpectateEnd_ViaLobby_AlsoMarksEnded()
        {
            var conn = ConnectAndVerify();
            _transport.RegisterNotify(MessageIds.NotifySpectateEnd, _ => { });
            _lobby.PushNotify(MessageIds.NotifySpectateEnd, new SpectateEndS2C { BattleId = TheBattleId });
            conn.PushDisconnect();
            Tick();
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);
            Assert.IsEmpty(_reissueRequests);
        }

        [Test]
        public void StopWatchBattle_ReplyMarksEnded_ThenFinIsCleanClose()
        {
            var conn = ConnectAndVerify();
            StopWatchBattleResponse got = null;
            _transport.Call(MessageIds.StopWatchBattle, new StopWatchBattleRequest { BattleId = TheBattleId },
                StopWatchBattleResponse.Parser, r => got = r, _ => Assert.Fail("不该失败"));
            var req = conn.LastSent<ClientRequest>();
            Assert.AreEqual(MessageIds.StopWatchBattle, req.MessageId);

            conn.PushInbound(new MessageContent
            {
                Id = req.Id,
                MessageId = MessageIds.StopWatchBattle,
                SerializedMessage = new StopWatchBattleResponse().ToByteString(),
            });
            conn.PushDisconnect();
            Tick();

            Assert.IsNotNull(got);
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);
            Assert.IsEmpty(_reissueRequests);
        }

        [Test]
        public void NotifyBattleReconnect_ViaLobby_TriggersTicketReissue()
        {
            int hits = 0;
            _transport.RegisterNotify(MessageIds.NotifyBattleReconnect, _ => hits++);
            _lobby.PushNotify(MessageIds.NotifyBattleReconnect, new BattleReconnectS2C { BattleId = TheBattleId });

            Assert.AreEqual(1, hits, "原处理器照常执行");
            Assert.AreEqual(new[] { TheBattleId }, _reissueRequests.ToArray());
        }

        [Test]
        public void NotifyBattleReconnect_WhileVerifiedOnSameBattle_NoReissue()
        {
            ConnectAndVerify();
            _transport.RegisterNotify(MessageIds.NotifyBattleReconnect, _ => { });
            _lobby.PushNotify(MessageIds.NotifyBattleReconnect, new BattleReconnectS2C { BattleId = TheBattleId });
            Assert.IsEmpty(_reissueRequests);
            Assert.IsTrue(_link.IsVerified);
        }

        // ── 评审确认项的回归 ────────────────────────────────

        [Test]
        public void StopWatchBattle_PassesRequestBattleId_NotZero()
        {
            // 链路服务的是 A;针对 B 的迟到退出观战不得把 A 标成已结束
            var conn = ConnectAndVerify();
            _transport.Call(MessageIds.StopWatchBattle, new StopWatchBattleRequest { BattleId = 9999 },
                StopWatchBattleResponse.Parser, _ => { }, _ => { });
            var req = conn.LastSent<ClientRequest>();
            conn.PushInbound(new MessageContent
            {
                Id = req.Id,
                MessageId = MessageIds.StopWatchBattle,
                SerializedMessage = new StopWatchBattleResponse().ToByteString(),
            });
            Tick();

            // A 的链路没被标记结束:随后的意外断开仍应走恢复,而不是直接终结
            conn.PushDisconnect();
            Tick();
            Assert.AreEqual(BattleDirectLink.LinkState.Idle, _link.State, "应排重连,而不是判定本局已结束");
            Assert.AreNotEqual(BattleDirectLink.LinkState.Closed, _link.State);
        }

        [Test]
        public void IdempotentRpc_TransportFailure_SurfacesError_NoLobby()
        {
            var conn = ConnectAndVerify();
            string err = null;
            _transport.Call(MessageIds.GetBattleState, new GetBattleStateRequest { BattleId = TheBattleId },
                BattleStateS2C.Parser, _ => Assert.Fail("不该成功"), e => err = e);
            Assert.AreEqual(0, _lobby.Calls.Count);

            conn.PushDisconnect();   // 在途请求随断开失败(带传输层前缀)
            Tick();

            StringAssert.StartsWith(BattleDirectLink.TransportErrorPrefix, err, "传输层失败原样交给调用方");
            Assert.AreEqual(0, _lobby.Calls.Count, "不改走大厅重发(丢帧由直连就绪时补拉恢复)");
        }

        // ── IBattleChannel ─────────────────────────────────

        [Test]
        public void LinkVerified_RaisesChannelReady_WithRole()
        {
            var ready = new List<(ulong, eBattleTicketRole)>();
            _transport.Ready += (id, role) => ready.Add((id, role));
            Assert.IsFalse(_transport.IsReadyFor(TheBattleId));

            ConnectAndVerify();

            Assert.AreEqual(1, ready.Count);
            Assert.AreEqual(TheBattleId, ready[0].Item1);
            Assert.AreEqual(eBattleTicketRole.BattleTicketRoleParticipant, ready[0].Item2);
            Assert.IsTrue(_transport.IsReadyFor(TheBattleId));
            Assert.IsFalse(_transport.IsReadyFor(9999), "别的局不算就绪");
            Assert.IsFalse(_transport.IsReadyFor(0));
        }

        [Test]
        public void LinkClosed_ReissueInvalidParameter_RaisesLostBattleGone()
        {
            var lost = new List<(ulong, BattleLinkCloseKind)>();
            _transport.Lost += (id, kind, _) => lost.Add((id, kind));
            var conn = ConnectAndVerify();

            // 断开 → 同票重连也失败 → 退避后补签 → match 回 kInvalidParameter(战斗已结束)
            conn.PushDisconnect(); Tick();
            Tick(BattleDirectLink.ReconnectDelaySeconds + 0.1);
            Current.PushDisconnect(); Tick();
            Tick(BattleDirectLink.ReissueBackoffBaseSeconds + 0.1);
            Assert.AreEqual(new[] { TheBattleId }, _reissueRequests.ToArray());
            _reissueFail((uint)common_error.KInvalidParameter, "response tip");

            Assert.AreEqual(1, lost.Count);
            Assert.AreEqual(TheBattleId, lost[0].Item1);
            Assert.AreEqual(BattleLinkCloseKind.BattleGone, lost[0].Item2);
            Assert.IsFalse(_transport.IsReadyFor(TheBattleId));
        }

        [Test]
        public void ChannelEntryPoints_ForwardToLink()
        {
            // EnsureBattle:本链路不服务该局 → 立即补签
            _transport.EnsureBattle(TheBattleId);
            Assert.AreEqual(new[] { TheBattleId }, _reissueRequests.ToArray());
            _reissueOk(new BattleAssignedS2C
            {
                BattleId = TheBattleId, Host = "10.0.0.5", Port = 20050,
                TokenPayload = ByteString.CopyFromUtf8("p"), TokenSignature = ByteString.CopyFromUtf8("s"),
                ExpireAtMs = 4_000_000_000_000UL, Role = eBattleTicketRole.BattleTicketRoleObserver,
            });
            Tick();
            Assert.AreEqual(BattleDirectLink.LinkState.Handshaking, _link.State);

            // Abandon:本地终结,以 Ended 收尾,不再重连
            var lost = new List<BattleLinkCloseKind>();
            _transport.Lost += (_, kind, __) => lost.Add(kind);
            _transport.Abandon(TheBattleId);
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);
            Assert.AreEqual(new[] { BattleLinkCloseKind.Ended }, lost.ToArray());

            // Retry:终结后手动重连 → 重置预算立即补签
            _transport.Retry(TheBattleId);
            Assert.AreEqual(2, _reissueRequests.Count);
        }

        [Test]
        public void SubmitBattleAction_DoesNotFallBack_SurfacesError()
        {
            var conn = ConnectAndVerify();
            string err = null;
            _transport.Call(MessageIds.SubmitBattleAction, new SubmitBattleActionRequest { BattleId = TheBattleId },
                SubmitBattleActionResponse.Parser, _ => Assert.Fail("不该成功"), e => err = e);
            conn.PushDisconnect();
            Tick();

            Assert.AreEqual(0, _lobby.Calls.Count, "不得替玩家重发出招");
            StringAssert.StartsWith(BattleDirectLink.TransportErrorPrefix, err);
        }

        [Test]
        public void ServerTipError_IsNotRetried()
        {
            var conn = ConnectAndVerify();
            string err = null;
            _transport.Call(MessageIds.GetBattleState, new GetBattleStateRequest { BattleId = TheBattleId },
                BattleStateS2C.Parser, _ => Assert.Fail("不该成功"), e => err = e);
            var req = conn.LastSent<ClientRequest>();
            conn.PushInbound(new MessageContent
            {
                Id = req.Id,
                MessageId = MessageIds.GetBattleState,
                SerializedMessage = new BattleStateS2C().ToByteString(),
                ErrorMessage = new TipInfoMessage { Id = 7 },
            });
            Tick();

            Assert.AreEqual(0, _lobby.Calls.Count, "业务拒绝是答案,不是投递失败");
            Assert.AreEqual("server tip=7", err);
        }

        [Test]
        public void PlayerIdAndReadiness_ComeFromLobby()
        {
            _lobby.PlayerId = 4242;
            _lobby.IsReady = false;
            Assert.AreEqual(4242UL, _transport.PlayerId);
            Assert.IsFalse(_transport.IsReady);
        }
    }
}
