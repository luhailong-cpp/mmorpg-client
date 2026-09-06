using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// <see cref="BattleDirectLink"/> 状态机纯逻辑测试(turn-based-battle-server.md §18.2 契约):
    /// 分配 → 建连 → 握手 → 分发;握手被拒 / 超时 / 意外断开 / 票据过期 / 正常收尾各走哪条恢复路径,
    /// 以及恢复预算封顶。连接经 <see cref="FakeFramedConnection"/> 注入,连接动作同步完成。
    /// </summary>
    public sealed class BattleDirectLinkTests
    {
        private const ulong BattleA = 7700;
        private const ulong BattleB = 7701;
        private const ulong FarFuture = 4_000_000_000_000UL; // Unix 毫秒,远未过期

        private List<FakeFramedConnection> _conns;
        private BattleDirectLink _link;
        private ulong _unixNowMs;
        private double _now;
        private List<string> _log;
        private List<ulong> _verified;
        private List<string> _closed;
        private List<ulong> _reissueRequests;
        private Action<BattleAssignedS2C> _reissueOk;
        private Action<string> _reissueFail;
        private bool _reissueAvailable = true;

        [SetUp]
        public void SetUp()
        {
            _conns = new List<FakeFramedConnection>();
            _unixNowMs = 1_000_000;
            _now = 100;
            _log = new List<string>();
            _verified = new List<ulong>();
            _closed = new List<string>();
            _reissueRequests = new List<ulong>();
            _reissueOk = null;
            _reissueFail = null;
            _reissueAvailable = true;

            _link = new BattleDirectLink(
                factory: () => { var c = new FakeFramedConnection(); _conns.Add(c); return c; },
                log: s => _log.Add(s),
                unixNowMs: () => _unixNowMs,
                connectRunner: a => a());
            _link.OnVerified += id => _verified.Add(id);
            _link.OnClosed += r => _closed.Add(r);
            _link.TicketReissuer = (battleId, ok, fail) =>
            {
                if (!_reissueAvailable) return false;
                _reissueRequests.Add(battleId);
                _reissueOk = ok;
                _reissueFail = fail;
                return true;
            };
        }

        // ── 工具 ────────────────────────────────────────────

        private static BattleAssignedS2C MakeAssignment(ulong battleId, string ticket = "t1", string host = "10.0.0.5", uint port = 20050,
                                                        ulong expireAtMs = FarFuture,
                                                        eBattleTicketRole role = eBattleTicketRole.BattleTicketRoleParticipant)
            => new BattleAssignedS2C
            {
                BattleId = battleId,
                Host = host,
                Port = port,
                TokenPayload = ByteString.CopyFromUtf8("payload-" + ticket),
                TokenSignature = ByteString.CopyFromUtf8("sig-" + ticket),
                ExpireAtMs = expireAtMs,
                Role = role,
            };

        private FakeFramedConnection Current => _conns[_conns.Count - 1];

        private void Tick(double advance = 0)
        {
            _now += advance;
            _link.Tick(_now);
        }

        /// <summary>分配 → 建连 → 握手包已发。</summary>
        private FakeFramedConnection AssignAndConnect(BattleAssignedS2C a)
        {
            _link.HandleAssigned(a);
            Tick();
            return Current;
        }

        private void Verify(FakeFramedConnection conn, ulong battleId, bool success = true, string error = "")
        {
            conn.PushInbound(new BattleTokenVerifyResponse { Success = success, Error = error, BattleId = battleId });
            Tick();
        }

        private FakeFramedConnection AssignAndVerify(BattleAssignedS2C a)
        {
            var conn = AssignAndConnect(a);
            Verify(conn, a.BattleId);
            return conn;
        }

        private static MessageContent Reply(ulong id, uint messageId, IMessage body, uint tip = 0)
        {
            var mc = new MessageContent { Id = id, MessageId = messageId, SerializedMessage = body.ToByteString() };
            if (tip != 0) mc.ErrorMessage = new TipInfoMessage { Id = tip };
            return mc;
        }

        // ── 建连与握手 ──────────────────────────────────────

        [Test]
        public void Assigned_ConnectsToEndpoint_AndSendsTicketAsFirstFrame()
        {
            var a = MakeAssignment(BattleA);
            var conn = AssignAndConnect(a);

            Assert.AreEqual("10.0.0.5", conn.Host);
            Assert.AreEqual(20050, conn.Port);
            Assert.AreEqual(BattleDirectLink.LinkState.Handshaking, _link.State);
            Assert.AreEqual(1, conn.Sent.Count, "首包必须是握手包,之前不许发别的");
            var hs = conn.Sent[0] as BattleTokenVerifyRequest;
            Assert.IsNotNull(hs);
            Assert.AreEqual(a.TokenPayload, hs.Payload, "payload 原样透传");
            Assert.AreEqual(a.TokenSignature, hs.Signature, "signature 原样透传");
            Assert.AreEqual(BattleA, _link.BattleId);
        }

        [Test]
        public void HandshakeSuccess_BecomesVerified()
        {
            var conn = AssignAndConnect(MakeAssignment(BattleA));
            Verify(conn, BattleA);

            Assert.IsTrue(_link.IsVerified);
            Assert.AreEqual(new[] { BattleA }, _verified.ToArray());
            Assert.AreEqual(eBattleTicketRole.BattleTicketRoleParticipant, _link.Role);
            Assert.IsEmpty(_closed);
        }

        [Test]
        public void IncompleteAssignment_Ignored()
        {
            _link.HandleAssigned(new BattleAssignedS2C { BattleId = BattleA, Host = "", Port = 0 });
            Tick();
            Assert.IsEmpty(_conns);
            Assert.AreEqual(BattleDirectLink.LinkState.Idle, _link.State);
        }

        // ── 请求-响应 / 推送 ────────────────────────────────

        [Test]
        public void Call_SendsClientRequest_AndMatchesReplyById()
        {
            var conn = AssignAndVerify(MakeAssignment(BattleA));

            SubmitBattleActionResponse got = null;
            string err = null;
            _link.Call(MessageIds.SubmitBattleAction, new SubmitBattleActionRequest { BattleId = BattleA },
                SubmitBattleActionResponse.Parser, r => got = r, e => err = e);

            var req = conn.LastSent<ClientRequest>();
            Assert.IsNotNull(req);
            Assert.AreEqual(MessageIds.SubmitBattleAction, req.MessageId);
            Assert.AreNotEqual(0UL, req.Id);

            conn.PushInbound(Reply(req.Id, MessageIds.SubmitBattleAction, new SubmitBattleActionResponse()));
            Tick();
            Assert.IsNotNull(got);
            Assert.IsNull(err);
        }

        [Test]
        public void Call_TipError_GoesToOnError()
        {
            var conn = AssignAndVerify(MakeAssignment(BattleA));
            string err = null;
            _link.Call(MessageIds.GetBattleState, new GetBattleStateRequest { BattleId = BattleA },
                BattleStateS2C.Parser, _ => Assert.Fail("不该成功"), e => err = e);
            var req = conn.LastSent<ClientRequest>();
            conn.PushInbound(Reply(req.Id, MessageIds.GetBattleState, new BattleStateS2C(), tip: 5));
            Tick();
            Assert.AreEqual("server tip=5", err);
        }

        [Test]
        public void Call_TimesOutAfterCallTimeout()
        {
            AssignAndVerify(MakeAssignment(BattleA));
            string err = null;
            _link.Call(MessageIds.GetBattleState, new GetBattleStateRequest { BattleId = BattleA },
                BattleStateS2C.Parser, _ => { }, e => err = e);
            Tick(BattleDirectLink.CallTimeoutSeconds - 0.1);
            Assert.IsNull(err);
            Tick(0.2);
            Assert.AreEqual(BattleDirectLink.TransportErrorPrefix + "rpc timeout", err);
        }

        [Test]
        public void Call_BeforeVerified_FailsImmediately()
        {
            AssignAndConnect(MakeAssignment(BattleA)); // 只到 Handshaking
            string err = null;
            _link.Call(MessageIds.GetBattleState, new GetBattleStateRequest(), BattleStateS2C.Parser, _ => { }, e => err = e);
            Assert.AreEqual(BattleDirectLink.TransportErrorPrefix + "not verified", err);
        }

        [Test]
        public void Notify_DispatchedByMessageId_WhenIdIsZero()
        {
            var conn = AssignAndVerify(MakeAssignment(BattleA));
            TurnResultS2C got = null;
            _link.RegisterNotify(MessageIds.NotifyTurnResult, mc => got = TurnResultS2C.Parser.ParseFrom(mc.SerializedMessage));

            conn.PushInbound(Reply(0, MessageIds.NotifyTurnResult, new TurnResultS2C { BattleId = BattleA }));
            Tick();
            Assert.IsNotNull(got);
            Assert.AreEqual(BattleA, got.BattleId);
        }

        // ── 握手失败的恢复路径 ──────────────────────────────

        [Test]
        public void HandshakeRejected_DoesNotRetrySameTicket_RequestsReissueOnce()
        {
            var conn = AssignAndConnect(MakeAssignment(BattleA, ticket: "t1"));
            Verify(conn, 0, success: false, error: "invalid ticket signature");

            Assert.IsTrue(conn.Disposed, "被拒后本地也关连接");
            Assert.AreEqual(new[] { BattleA }, _reissueRequests.ToArray(), "同票不再重试,直接补签");
            Assert.AreEqual(1, _conns.Count, "补签回来之前不建新连接");
            Tick(BattleDirectLink.ReconnectDelaySeconds + 1);
            Assert.AreEqual(1, _conns.Count);

            // 补签回来:新票据建连、握手、验证通过
            _reissueOk(MakeAssignment(BattleA, ticket: "t2"));
            Tick();
            Assert.AreEqual(2, _conns.Count);
            var hs = Current.Sent[0] as BattleTokenVerifyRequest;
            Assert.AreEqual(ByteString.CopyFromUtf8("payload-t2"), hs.Payload);
            Verify(Current, BattleA);
            Assert.IsTrue(_link.IsVerified);
            Assert.AreEqual(1, _reissueRequests.Count);
        }

        [Test]
        public void HandshakeRejected_ReissueFails_LinkClosed()
        {
            var conn = AssignAndConnect(MakeAssignment(BattleA));
            Verify(conn, 0, success: false, error: "expired");
            _reissueFail("tip=9");

            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);
            Assert.AreEqual(1, _closed.Count);
            StringAssert.Contains("reissue_failed", _closed[0]);
        }

        [Test]
        public void HandshakeRejected_ReissueUnavailable_LinkClosed()
        {
            _reissueAvailable = false;
            var conn = AssignAndConnect(MakeAssignment(BattleA));
            Verify(conn, 0, success: false);
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);
            Assert.AreEqual(new[] { "reissue_unavailable" }, _closed.ToArray());
        }

        [Test]
        public void HandshakeTimeout_RetriesSameTicketOnce_ThenReissues()
        {
            var a = MakeAssignment(BattleA, ticket: "t1");
            var first = AssignAndConnect(a);

            Tick(BattleDirectLink.HandshakeTimeoutSeconds + 0.1);
            Assert.IsTrue(first.Disposed);
            Assert.AreEqual(BattleDirectLink.LinkState.Idle, _link.State, "等重连倒计时");
            Assert.IsEmpty(_reissueRequests);

            Tick(BattleDirectLink.ReconnectDelaySeconds + 0.1);
            Assert.AreEqual(2, _conns.Count, "同票重连 1 次");
            var hs = Current.Sent[0] as BattleTokenVerifyRequest;
            Assert.AreEqual(a.TokenPayload, hs.Payload);

            Tick(BattleDirectLink.HandshakeTimeoutSeconds + 0.1);
            Assert.AreEqual(new[] { BattleA }, _reissueRequests.ToArray(), "第二次超时才补签");
            Assert.AreEqual(2, _conns.Count);
        }

        [Test]
        public void ConnectThrows_CountsAsSameTicketRetry_ThenReissues()
        {
            // 独立链路:工厂让前两条连接的 Connect 抛错(不可达 / 被拒),第三条才连得上
            var conns = new List<FakeFramedConnection>();
            var reissues = new List<ulong>();
            var link = new BattleDirectLink(
                () =>
                {
                    var c = new FakeFramedConnection();
                    if (conns.Count < 2) c.ConnectError = new System.Net.Sockets.SocketException(10061);
                    conns.Add(c);
                    return c;
                },
                unixNowMs: () => _unixNowMs, connectRunner: a => a());
            link.TicketReissuer = (id, ok, fail) => { reissues.Add(id); return true; };

            double now = 10;
            link.HandleAssigned(MakeAssignment(BattleA));
            link.Tick(now);
            Assert.AreEqual(1, conns.Count);
            Assert.IsTrue(conns[0].Disposed);
            Assert.AreEqual(BattleDirectLink.LinkState.Idle, link.State, "连接失败按意外断开处理:同票重连 1 次");
            Assert.IsEmpty(reissues);

            now += BattleDirectLink.ReconnectDelaySeconds + 0.1;
            link.Tick(now);
            Assert.AreEqual(2, conns.Count);
            Assert.AreEqual(new[] { BattleA }, reissues.ToArray(), "第二次也失败 → 补签");
        }

        [Test]
        public void HandshakeSendFails_WhenConnectionDropsBeforeFirstFrame()
        {
            // 连接成功后、握手包发出前对端就断了:Send 抛错 → 视同意外断开走恢复
            var conns = new List<FakeFramedConnection>();
            var link = new BattleDirectLink(
                () =>
                {
                    var c = new FakeFramedConnection();
                    conns.Add(c);
                    return c;
                },
                unixNowMs: () => _unixNowMs, connectRunner: a => { a(); conns[conns.Count - 1].Dispose(); });
            link.HandleAssigned(MakeAssignment(BattleA));
            link.Tick(10);
            Assert.AreEqual(BattleDirectLink.LinkState.Idle, link.State);
            link.Tick(10 + BattleDirectLink.ReconnectDelaySeconds + 0.1);
            Assert.AreEqual(2, conns.Count, "同票重连 1 次");
        }

        // ── 意外断开 ────────────────────────────────────────

        [Test]
        public void UnexpectedDrop_WhileVerified_ReconnectsWithSameTicket()
        {
            var a = MakeAssignment(BattleA);
            var first = AssignAndVerify(a);

            first.PushDisconnect();
            Tick();
            Assert.IsFalse(_link.IsVerified);
            Assert.AreEqual(BattleDirectLink.LinkState.Idle, _link.State);
            Assert.AreEqual(1, _conns.Count, "倒计时未到不建连");

            Tick(BattleDirectLink.ReconnectDelaySeconds + 0.1);
            Assert.AreEqual(2, _conns.Count);
            var hs = Current.Sent[0] as BattleTokenVerifyRequest;
            Assert.AreEqual(a.TokenPayload, hs.Payload, "同一张票重连(D25 可重用)");
            Verify(Current, BattleA);
            Assert.IsTrue(_link.IsVerified);
            Assert.AreEqual(2, _verified.Count);
            Assert.IsEmpty(_reissueRequests);
        }

        [Test]
        public void UnexpectedDrop_FailsPendingCalls()
        {
            var conn = AssignAndVerify(MakeAssignment(BattleA));
            string err = null;
            _link.Call(MessageIds.GetBattleState, new GetBattleStateRequest(), BattleStateS2C.Parser, _ => { }, e => err = e);
            conn.PushDisconnect();
            Tick();
            Assert.AreEqual(BattleDirectLink.TransportErrorPrefix + "disconnected", err);
        }

        [Test]
        public void Drop_WithExpiredTicket_SkipsSameTicketRetry_AndReissues()
        {
            var a = MakeAssignment(BattleA, expireAtMs: 2_000_000);
            var conn = AssignAndVerify(a);
            _unixNowMs = 2_000_001; // 票据刚过期

            conn.PushDisconnect();
            Tick();
            Assert.AreEqual(new[] { BattleA }, _reissueRequests.ToArray(), "过期票不重试,直接补签");
            Tick(BattleDirectLink.ReconnectDelaySeconds + 1);
            Assert.AreEqual(1, _conns.Count);
        }

        [Test]
        public void RecoveryBudget_IsCapped_PerTicketAndPerBattle()
        {
            // 票 t1:验证 → 断 → 同票重连 → 验证 → 断 → 补签(t2)→ 验证 → 断 → 同票重连 → 验证 → 断 → 终结
            var c1 = AssignAndVerify(MakeAssignment(BattleA, ticket: "t1"));
            c1.PushDisconnect(); Tick();
            Tick(BattleDirectLink.ReconnectDelaySeconds + 0.1);
            Assert.AreEqual(2, _conns.Count);
            Verify(Current, BattleA);

            Current.PushDisconnect(); Tick();
            Assert.AreEqual(new[] { BattleA }, _reissueRequests.ToArray(), "同票预算用完 → 补签");
            _reissueOk(MakeAssignment(BattleA, ticket: "t2"));
            Tick();
            Assert.AreEqual(3, _conns.Count);
            Verify(Current, BattleA);

            Current.PushDisconnect(); Tick();
            Tick(BattleDirectLink.ReconnectDelaySeconds + 0.1);
            Assert.AreEqual(4, _conns.Count, "新票据再给 1 次同票重连");
            Verify(Current, BattleA);

            Current.PushDisconnect(); Tick();
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State, "补签也用完 → 终结,余下回合经 gate 回落");
            Assert.AreEqual(1, _reissueRequests.Count, "每局只补签 1 次");
            Assert.AreEqual(4, _conns.Count);
            Assert.AreEqual(1, _closed.Count);
        }

        // ── 正常收尾 ────────────────────────────────────────

        [Test]
        public void Ended_ThenServerFin_ClosesWithoutRecovery()
        {
            var conn = AssignAndVerify(MakeAssignment(BattleA));
            _link.HandleBattleEnded(BattleA);
            conn.PushDisconnect();
            Tick();

            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);
            Assert.AreEqual(new[] { "battle_ended" }, _closed.ToArray());
            Assert.IsEmpty(_reissueRequests);
            Tick(BattleDirectLink.ReconnectDelaySeconds + 1);
            Assert.AreEqual(1, _conns.Count, "不重连");
        }

        [Test]
        public void Ended_ForAnotherBattle_Ignored()
        {
            var conn = AssignAndVerify(MakeAssignment(BattleA));
            _link.HandleBattleEnded(BattleB);
            conn.PushDisconnect();
            Tick();
            Assert.AreEqual(BattleDirectLink.LinkState.Idle, _link.State, "不是本局结束,按意外断开走恢复");
        }

        [Test]
        public void Ended_WhileWaitingReconnect_FinishesImmediately()
        {
            var conn = AssignAndVerify(MakeAssignment(BattleA));
            conn.PushDisconnect();
            Tick();
            Assert.AreEqual(BattleDirectLink.LinkState.Idle, _link.State);
            _link.HandleBattleEnded(0); // 0 = 当前这局(经 gate 回落收到终局包)
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);
            Tick(BattleDirectLink.ReconnectDelaySeconds + 1);
            Assert.AreEqual(1, _conns.Count);
        }

        // ── 分配包的幂等与换局 ──────────────────────────────

        [Test]
        public void DuplicateAssignment_SameBattle_WhileVerified_KeepsConnection_UpdatesTicket()
        {
            AssignAndVerify(MakeAssignment(BattleA, ticket: "t1"));
            var newer = MakeAssignment(BattleA, ticket: "t2");
            _link.HandleAssigned(newer);
            Tick();

            Assert.AreEqual(1, _conns.Count);
            Assert.IsTrue(_link.IsVerified);
            Assert.AreEqual(newer.TokenPayload, _link.Assignment.TokenPayload, "留最新票据供重连");
        }

        [Test]
        public void NewBattleAssignment_ReplacesOldLink()
        {
            var old = AssignAndVerify(MakeAssignment(BattleA));
            var b = MakeAssignment(BattleB, host: "10.0.0.6", port: 20051);
            _link.HandleAssigned(b);
            Tick();

            Assert.IsTrue(old.Disposed);
            Assert.AreEqual(2, _conns.Count);
            Assert.AreEqual("10.0.0.6", Current.Host);
            Assert.AreEqual(BattleDirectLink.LinkState.Handshaking, _link.State);
            Verify(Current, BattleB);
            Assert.AreEqual(BattleB, _link.BattleId);

            // 旧连接晚到的 FIN 不能动新链路
            old.PushDisconnect();
            old.Poll();
            Assert.IsTrue(_link.IsVerified);
        }

        [Test]
        public void StaleConnectResult_FromReplacedConnection_IsIgnored()
        {
            // 用异步 runner:分配 A 后立刻换成 B,再让两个连接结果按 A、B 顺序回来
            var pendingJobs = new List<Action>();
            var conns = new List<FakeFramedConnection>();
            var link = new BattleDirectLink(() => { var c = new FakeFramedConnection(); conns.Add(c); return c; },
                unixNowMs: () => _unixNowMs, connectRunner: a => pendingJobs.Add(a));
            link.HandleAssigned(MakeAssignment(BattleA));
            link.HandleAssigned(MakeAssignment(BattleB));
            foreach (var job in pendingJobs) job();
            link.Tick(1);

            Assert.IsTrue(conns[0].Disposed, "被换掉的连接连上也要关");
            Assert.AreEqual(BattleDirectLink.LinkState.Handshaking, link.State);
            Assert.AreEqual(1, conns[1].Sent.Count, "只有当前连接发了握手");
            Assert.AreEqual(0, conns[0].Sent.Count);
        }

        // ── 宿主关闭 / 大厅重连提示 ─────────────────────────

        [Test]
        public void Close_FailsPending_DisposesConnection_NoRecovery()
        {
            var conn = AssignAndVerify(MakeAssignment(BattleA));
            string err = null;
            _link.Call(MessageIds.GetBattleState, new GetBattleStateRequest(), BattleStateS2C.Parser, _ => { }, e => err = e);

            _link.Close("lobby_disconnected");

            Assert.AreEqual(BattleDirectLink.TransportErrorPrefix + "closed", err);
            Assert.IsTrue(conn.Disposed);
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);
            Assert.AreEqual(new[] { "lobby_disconnected" }, _closed.ToArray());
            Assert.AreEqual(0UL, _link.BattleId);
            Assert.IsNull(_link.Assignment);
            Tick(BattleDirectLink.ReconnectDelaySeconds + 1);
            Assert.AreEqual(1, _conns.Count);
            Assert.IsEmpty(_reissueRequests);
        }

        [Test]
        public void Close_ThenLateReissueCallback_IsIgnored()
        {
            var conn = AssignAndConnect(MakeAssignment(BattleA));
            Verify(conn, 0, success: false); // 触发补签
            Assert.AreEqual(1, _reissueRequests.Count);
            _link.Close("lobby_disconnected");
            _reissueOk(MakeAssignment(BattleA, ticket: "late"));
            Tick();
            Assert.AreEqual(1, _conns.Count, "关闭后晚到的补签不得重建连接");
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);
        }

        [Test]
        public void Close_ThenNewAssignment_ReopensNormally()
        {
            AssignAndVerify(MakeAssignment(BattleA));
            _link.Close("lobby_disconnected");
            AssignAndVerify(MakeAssignment(BattleB));
            Assert.IsTrue(_link.IsVerified);
            Assert.AreEqual(BattleB, _link.BattleId);
        }

        [Test]
        public void ReconnectHint_RequestsReissue_ThenConnects()
        {
            _link.HandleReconnectHint(BattleA);
            Assert.AreEqual(new[] { BattleA }, _reissueRequests.ToArray());
            Assert.IsEmpty(_conns);

            _reissueOk(MakeAssignment(BattleA));
            Tick();
            Assert.AreEqual(1, _conns.Count);
            Verify(Current, BattleA);
            Assert.IsTrue(_link.IsVerified);
        }

        [Test]
        public void ReconnectHint_WhileVerifiedOnSameBattle_NoOp()
        {
            AssignAndVerify(MakeAssignment(BattleA));
            _link.HandleReconnectHint(BattleA);
            Assert.IsEmpty(_reissueRequests);
            Assert.IsTrue(_link.IsVerified);
        }

        [Test]
        public void ReconnectHint_TwiceForSameBattle_OnlyOneReissueInFlight()
        {
            // 重定向收尾主动补 + 服务端 NotifyBattleReconnect,两者都可能到达
            _link.HandleReconnectHint(BattleA);
            _link.HandleReconnectHint(BattleA);
            Assert.AreEqual(1, _reissueRequests.Count, "同一局的补签在途时不重复发");

            _reissueOk(MakeAssignment(BattleA));
            Tick();
            Verify(Current, BattleA);
            Assert.IsTrue(_link.IsVerified);
            Assert.AreEqual(1, _conns.Count);
        }

        [Test]
        public void ReconnectHint_AfterLobbyClose_RebuildsLink()
        {
            // GameClient 重定向路径:Close 清掉 battle_id,宿主用捕获的 id 触发重建
            AssignAndVerify(MakeAssignment(BattleA));
            var beforeClose = _link.BattleId;
            _link.Close("lobby_disconnected");
            Assert.AreEqual(0UL, _link.BattleId);

            _link.HandleReconnectHint(beforeClose);
            Assert.AreEqual(new[] { BattleA }, _reissueRequests.ToArray());
            _reissueOk(MakeAssignment(BattleA, ticket: "t2"));
            Tick();
            Verify(Current, BattleA);
            Assert.IsTrue(_link.IsVerified);
            Assert.AreEqual(BattleA, _link.BattleId);
        }

        [Test]
        public void ReconnectHint_ResetsBudget_AfterPreviousBattleExhausted()
        {
            // 上一局把补签预算用完并终结;新的重连提示是新的一局语境,预算重新给
            var conn = AssignAndConnect(MakeAssignment(BattleA));
            Verify(conn, 0, success: false);
            _reissueFail("x");
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);

            _link.HandleReconnectHint(BattleA);
            Assert.AreEqual(2, _reissueRequests.Count);
        }

        // ── 评审确认项的回归 ────────────────────────────────

        [Test]
        public void Connecting_TimesOut_AndRecovers()
        {
            // 连接动作永不返回(endpoint 黑洞:SYN 无应答)。Connecting 必须自己封顶,
            // 否则会卡到系统 SYN 超时(约 21s),期间既不重试也不回落。
            var conns = new List<FakeFramedConnection>();
            var reissues = new List<ulong>();
            var link = new BattleDirectLink(
                () => { var c = new FakeFramedConnection(); conns.Add(c); return c; },
                unixNowMs: () => _unixNowMs,
                connectRunner: _ => { /* 永远不完成,也不入队结果 */ });
            link.TicketReissuer = (id, ok, fail) => { reissues.Add(id); return true; };

            double now = 10;
            link.HandleAssigned(MakeAssignment(BattleA));
            link.Tick(now);
            Assert.AreEqual(BattleDirectLink.LinkState.Connecting, link.State);

            now += BattleDirectLink.ConnectTimeoutSeconds - 0.1;
            link.Tick(now);
            Assert.AreEqual(BattleDirectLink.LinkState.Connecting, link.State, "未到期不动它");

            now += 0.2;
            link.Tick(now);
            Assert.AreEqual(BattleDirectLink.LinkState.Idle, link.State, "超时按意外断开处理,排同票重连");
            Assert.IsFalse(link.IsVerified);

            now += BattleDirectLink.ReconnectDelaySeconds + 0.1;
            link.Tick(now);
            Assert.AreEqual(2, conns.Count, "同票重连 1 次");
            now += BattleDirectLink.ConnectTimeoutSeconds + 0.1;
            link.Tick(now);
            Assert.AreEqual(new[] { BattleA }, reissues.ToArray(), "第二次也超时 → 补签");
        }

        [Test]
        public void PendingCall_FailsWithTransportPrefix_OnConnectTimeout()
        {
            // 传输层失败必须带前缀,分流层据此决定是否改走大厅重发
            AssignAndVerify(MakeAssignment(BattleA));
            string err = null;
            _link.Call(MessageIds.GetBattleState, new GetBattleStateRequest(), BattleStateS2C.Parser, _ => { }, e => err = e);
            Current.PushDisconnect();
            Tick();
            StringAssert.StartsWith(BattleDirectLink.TransportErrorPrefix, err);
        }

        [Test]
        public void ReissueInFlight_LeavesStateIdle_NotStaleVerified()
        {
            // 断开 → 同票重连 → 再断开 → 补签在途;此时又断一次(或再次进入恢复),
            // TryReissue 的早退不得把 State 留在 Verified,否则 IsVerified 会对着
            // 一条不存在的连接返回 true,战斗 RPC 全被路由进直连然后静默丢掉。
            var c1 = AssignAndVerify(MakeAssignment(BattleA));
            c1.PushDisconnect(); Tick();
            Tick(BattleDirectLink.ReconnectDelaySeconds + 0.1);
            Verify(Current, BattleA);
            Assert.IsTrue(_link.IsVerified);

            Current.PushDisconnect(); Tick();          // 同票预算用完 → 补签在途
            Assert.AreEqual(1, _reissueRequests.Count);
            Assert.IsFalse(_link.IsVerified, "补签在途时不得自称已验证");
            Assert.AreEqual(BattleDirectLink.LinkState.Idle, _link.State);
        }

        [Test]
        public void LateReissueFailure_DoesNotTearDownRebuiltLink()
        {
            // 补签在途期间,服务端又推了一份分配包把链路重建好了;
            // 随后补签失败的回调不得把这条正在用的连接拆掉。
            var conn = AssignAndConnect(MakeAssignment(BattleA));
            Verify(conn, 0, success: false);            // 握手被拒 → 直接补签
            Assert.AreEqual(1, _reissueRequests.Count);

            _link.HandleAssigned(MakeAssignment(BattleA, ticket: "pushed"));
            Tick();
            Verify(Current, BattleA);
            Assert.IsTrue(_link.IsVerified);

            _reissueFail("timeout");                    // 迟到的失败回调
            Assert.IsTrue(_link.IsVerified, "已重建好的链路不能被迟到的补签失败拆掉");
            Assert.IsEmpty(_closed);
        }

        [Test]
        public void SameBattleRebuild_InvalidatesInFlightReissue()
        {
            var conn = AssignAndConnect(MakeAssignment(BattleA));
            Verify(conn, 0, success: false);
            Assert.AreEqual(1, _reissueRequests.Count);

            // 同一局重建(不是换局)也要 bump epoch:否则迟到的补签成功回调会再换一次连接
            _link.HandleAssigned(MakeAssignment(BattleA, ticket: "pushed"));
            Tick();
            Verify(Current, BattleA);
            int connsAfterRebuild = _conns.Count;

            _reissueOk(MakeAssignment(BattleA, ticket: "late"));
            Tick();
            Assert.AreEqual(connsAfterRebuild, _conns.Count, "迟到的补签票不得再换连接");
            Assert.IsTrue(_link.IsVerified);
        }

        [Test]
        public void DuplicateVerifyResponse_AfterVerified_Ignored()
        {
            var conn = AssignAndVerify(MakeAssignment(BattleA));
            conn.PushInbound(new BattleTokenVerifyResponse { Success = false, Error = "dup" });
            Tick();
            Assert.IsTrue(_link.IsVerified, "重复握手应答不改变已验证状态");
        }
    }
}
