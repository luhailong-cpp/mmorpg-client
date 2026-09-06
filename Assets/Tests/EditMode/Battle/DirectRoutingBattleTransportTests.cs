using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// <see cref="DirectRoutingBattleTransport"/> 分流规则测试:四条战斗 RPC 只在直连已验证时
    /// 走直连,其余永远走大厅;S2C 两条路都能到达同一处理器;三条"本局结束"信号与重连提示
    /// 正确转给 <see cref="BattleDirectLink"/>。
    /// </summary>
    public sealed class DirectRoutingBattleTransportTests
    {
        private const ulong TheBattleId = 7700;

        private FakeBattleTransport _lobby;
        private List<FakeFramedConnection> _conns;
        private BattleDirectLink _link;
        private DirectRoutingBattleTransport _transport;
        private List<ulong> _reissueRequests;
        private double _now;

        [SetUp]
        public void SetUp()
        {
            _lobby = new FakeBattleTransport();
            _conns = new List<FakeFramedConnection>();
            _reissueRequests = new List<ulong>();
            _now = 100;
            _link = new BattleDirectLink(() => { var c = new FakeFramedConnection(); _conns.Add(c); return c; },
                unixNowMs: () => 1_000_000, connectRunner: a => a());
            _link.TicketReissuer = (id, ok, fail) => { _reissueRequests.Add(id); return true; };
            _transport = new DirectRoutingBattleTransport(_lobby, _link);
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
        public void BeforeVerified_BattleRpcGoesToLobby()
        {
            _transport.Call(MessageIds.SubmitBattleAction, new SubmitBattleActionRequest(),
                SubmitBattleActionResponse.Parser, _ => { }, _ => { });
            Assert.AreEqual(1, _lobby.Calls.Count);
            Assert.AreEqual(MessageIds.SubmitBattleAction, _lobby.Calls[0].MessageId);
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
        public void AfterLinkClosed_BattleRpcFallsBackToLobby()
        {
            var conn = ConnectAndVerify();
            _link.HandleBattleEnded(TheBattleId);
            conn.PushDisconnect();
            Tick();
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);

            _transport.Call(MessageIds.GetBattleState, new GetBattleStateRequest(), BattleStateS2C.Parser, _ => { }, _ => { });
            Assert.AreEqual(1, _lobby.Calls.Count);
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
            Assert.AreEqual(2, hits, "gate 回落到达");
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
        public void IsRetriableOnLobby_ExcludesSubmitBattleAction()
        {
            Assert.IsTrue(DirectRoutingBattleTransport.IsRetriableOnLobby(MessageIds.GetBattleState));
            Assert.IsTrue(DirectRoutingBattleTransport.IsRetriableOnLobby(MessageIds.StopWatchBattle));
            Assert.IsTrue(DirectRoutingBattleTransport.IsRetriableOnLobby(MessageIds.SetAutoBattle));
            Assert.IsFalse(DirectRoutingBattleTransport.IsRetriableOnLobby(MessageIds.SubmitBattleAction),
                "没有回合号、服务端无重复守卫,重发会落进下一回合");
        }

        [Test]
        public void IdempotentRpc_FallsBackToLobby_OnTransportFailure()
        {
            var conn = ConnectAndVerify();
            BattleStateS2C got = null;
            string err = null;
            _transport.Call(MessageIds.GetBattleState, new GetBattleStateRequest { BattleId = TheBattleId },
                BattleStateS2C.Parser, r => got = r, e => err = e);
            Assert.AreEqual(0, _lobby.Calls.Count);

            conn.PushDisconnect();   // 在途请求随断开失败(带传输层前缀)
            Tick();

            Assert.IsNull(err, "传输层失败不该直接冒给调用方");
            Assert.AreEqual(1, _lobby.Calls.Count, "改走大厅重发一次");
            Assert.AreEqual(MessageIds.GetBattleState, _lobby.Calls[0].MessageId);
            _lobby.Calls[0].Respond(new BattleStateS2C { BattleId = TheBattleId });
            Assert.IsNotNull(got);
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
