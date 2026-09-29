using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// <see cref="BattleDirectLink"/> 状态机纯逻辑测试(turn-based-battle-server.md §18.2 契约,
    /// 收缩后口径 turn-based §22 D74):分配 → 建连 → 握手 → 分发;握手被拒 / 超时 / 意外断开 /
    /// 票据过期 / 正常收尾各走哪条恢复路径;补签的退避、预算封顶与失败分类(BattleGone / Unreachable);
    /// 换局时旧局的 Superseded 通知;Retry / EnsureBattle / Abandon 三个宿主入口。连接经 <see cref="FakeFramedConnection"/> 注入,
    /// 连接动作同步完成;退避抖动源固定为 0.5(无抖动),退避时长即 1s / 2s / 4s。
    /// </summary>
    public sealed class BattleDirectLinkTests
    {
        private const ulong BattleA = 7700;
        private const ulong BattleB = 7701;
        private const ulong FarFuture = 4_000_000_000_000UL; // Unix 毫秒,远未过期

        /// <summary>第 k 次自动补签前的退避(无抖动)再加一点余量。</summary>
        private static double Backoff(int attempt)
            => BattleDirectLink.ReissueBackoffBaseSeconds * Math.Pow(2, attempt - 1) + 0.1;

        private List<FakeFramedConnection> _conns;
        private BattleDirectLink _link;
        private ulong _unixNowMs;
        private double _now;
        private List<string> _log;
        private List<ulong> _verified;
        private List<string> _closed;
        private List<BattleLinkCloseKind> _closedKinds;
        private List<ulong> _closedBattleIds;
        private List<ulong> _reissueRequests;
        private Action<BattleAssignedS2C> _reissueOk;
        private Action<uint, string> _reissueFail;
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
            _closedKinds = new List<BattleLinkCloseKind>();
            _closedBattleIds = new List<ulong>();
            _reissueRequests = new List<ulong>();
            _reissueOk = null;
            _reissueFail = null;
            _reissueAvailable = true;

            _link = new BattleDirectLink(
                factory: () => { var c = new FakeFramedConnection(); _conns.Add(c); return c; },
                log: s => _log.Add(s),
                unixNowMs: () => _unixNowMs,
                connectRunner: a => a(),
                random01: () => 0.5);
            _link.OnVerified += id => _verified.Add(id);
            _link.OnClosed += (id, kind, r) =>
            {
                _closedBattleIds.Add(id);
                _closedKinds.Add(kind);
                _closed.Add(r);
            };
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
            Assert.AreEqual(BattleDirectLink.NotReadyError, err);
            StringAssert.StartsWith(BattleDirectLink.TransportErrorPrefix, err);
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
        public void HandshakeRejected_DoesNotRetrySameTicket_ReissuesAfterBackoff()
        {
            var conn = AssignAndConnect(MakeAssignment(BattleA, ticket: "t1"));
            Verify(conn, 0, success: false, error: "invalid ticket signature");

            Assert.IsTrue(conn.Disposed, "被拒后本地也关连接");
            Assert.IsEmpty(_reissueRequests, "自动补签按退避排队(第 1 次等 1s)");
            Assert.AreEqual(BattleDirectLink.LinkState.Idle, _link.State);
            Tick(Backoff(1));
            Assert.AreEqual(new[] { BattleA }, _reissueRequests.ToArray(), "同票不再重试,直接进补签");
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
        public void ReissueFails_InvalidParameter_ClosesAsBattleGone()
        {
            // match:该战斗不存在或已结束(典型:终局包在断线期间丢了)→ 不再重试
            var conn = AssignAndConnect(MakeAssignment(BattleA));
            Verify(conn, 0, success: false, error: "expired");
            Tick(Backoff(1));
            _reissueFail((uint)common_error.KInvalidParameter, "response tip");

            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);
            Assert.AreEqual(new[] { BattleLinkCloseKind.BattleGone }, _closedKinds.ToArray());
            Assert.AreEqual(new[] { BattleA }, _closedBattleIds.ToArray(), "终结事件带上服务的 battle_id");
            Tick(Backoff(3) + 1);
            Assert.AreEqual(1, _reissueRequests.Count, "BattleGone 不再补签");
        }

        [Test]
        public void ReissueFails_ServiceUnavailable_BacksOff_1s2s4s_ThenUnreachable()
        {
            var conn = AssignAndConnect(MakeAssignment(BattleA));
            Verify(conn, 0, success: false, error: "expired");

            for (int attempt = 1; attempt <= BattleDirectLink.MaxReissuesPerBattle; attempt++)
            {
                double wait = BattleDirectLink.ReissueBackoffBaseSeconds * Math.Pow(2, attempt - 1);
                Tick(wait - 0.05);
                Assert.AreEqual(attempt - 1, _reissueRequests.Count, $"第 {attempt} 次补签未到退避时刻不发");
                Tick(0.1);
                Assert.AreEqual(attempt, _reissueRequests.Count, $"第 {attempt} 次补签在 {wait}s 退避后发出");
                Assert.IsEmpty(_closed);
                _reissueFail((uint)common_error.KServiceUnavailable, "response tip");
            }

            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State, "预算用完 → 终结");
            Assert.AreEqual(new[] { BattleLinkCloseKind.Unreachable }, _closedKinds.ToArray());
            Assert.AreEqual(BattleDirectLink.MaxReissuesPerBattle, _reissueRequests.Count);
        }

        [Test]
        public void ReissueFails_TransportError_TreatedAsUnreachableAfterBudget()
        {
            // 传输失败(tipId=0)与非 kInvalidParameter 的 tip 同样按退避继续,不能当成「战斗已结束」
            var conn = AssignAndConnect(MakeAssignment(BattleA));
            Verify(conn, 0, success: false);
            for (int attempt = 1; attempt <= BattleDirectLink.MaxReissuesPerBattle; attempt++)
            {
                Tick(Backoff(attempt));
                _reissueFail(0, "rpc timeout");
            }
            Assert.AreEqual(new[] { BattleLinkCloseKind.Unreachable }, _closedKinds.ToArray());
        }

        [Test]
        public void ReissueUnavailable_CountsAsFailedAttempt_ThenUnreachable()
        {
            _reissueAvailable = false;   // 大厅未就绪:补签通道此刻发不出
            var conn = AssignAndConnect(MakeAssignment(BattleA));
            Verify(conn, 0, success: false);
            Tick(Backoff(1));
            Assert.AreNotEqual(BattleDirectLink.LinkState.Closed, _link.State, "发不出按一次失败的补签计,继续退避");
            Tick(Backoff(2));
            Tick(Backoff(3));
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);
            Assert.AreEqual(new[] { BattleLinkCloseKind.Unreachable }, _closedKinds.ToArray());
        }

        [Test]
        public void ReissueReturnsIncompleteAssignment_BacksOff_ThenUnreachable_NotStuckIdle()
        {
            // 补签「成功」却回了缺 host / port 的落点:交给 HandleAssigned 会被静默忽略,
            // 链路停在 Idle(无在途补签、无倒计时、不终结),UI 永远「正在连接」。必须按失败的补签计
            _link.HandleReconnectHint(BattleA);                  // 立即补签(第 1 次)
            Assert.AreEqual(1, _reissueRequests.Count);

            _reissueOk(MakeAssignment(BattleA, port: 0));
            Tick();
            Assert.IsEmpty(_conns, "不完整的落点不建连");
            Assert.AreEqual(BattleDirectLink.LinkState.Idle, _link.State);

            Tick(Backoff(2));
            Assert.AreEqual(2, _reissueRequests.Count, "按一次失败计,退避后继续补签");
            _reissueOk(MakeAssignment(BattleA, host: ""));
            Tick(Backoff(3));
            Assert.AreEqual(3, _reissueRequests.Count);
            _reissueOk(MakeAssignment(BattleA, port: 0));

            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State, "预算用完 → 终结,不永久停在 Idle");
            Assert.AreEqual(new[] { BattleLinkCloseKind.Unreachable }, _closedKinds.ToArray());
            Assert.IsEmpty(_conns);
        }

        [Test]
        public void ReissueBackoff_CappedByTicketExpiry()
        {
            // 票据(= 房间)0.5s 后作废:退避不等到房间作废之后,到点即补签,由服务端给权威答复
            var conn = AssignAndConnect(MakeAssignment(BattleA, expireAtMs: _unixNowMs + 500));
            Verify(conn, 0, success: false);
            Tick(0.4);
            Assert.IsEmpty(_reissueRequests);
            Tick(0.2);
            Assert.AreEqual(new[] { BattleA }, _reissueRequests.ToArray(), "退避被票据期限封顶到 0.5s");
        }

        [Test]
        public void ReissueBackoff_JitterWithinTwentyPercent()
        {
            // 抖动源取下界 0:退避 = 基数 × 2^(k-1) × (1 - 20%)
            var reissues = new List<ulong>();
            Action<uint, string> fail = null;
            var link = new BattleDirectLink(() => new FakeFramedConnection(),
                unixNowMs: () => _unixNowMs, connectRunner: a => a(), random01: () => 0.0);
            link.TicketReissuer = (id, ok, f) => { reissues.Add(id); fail = f; return true; };
            double now = 10;
            link.Tick(now);

            link.HandleReconnectHint(BattleA);   // 宿主入口:立即补签(第 1 次,不退避)
            Assert.AreEqual(1, reissues.Count);

            fail(0, "rpc timeout");              // 失败 → 第 2 次自动补签:2s × 0.8 = 1.6s
            link.Tick(now + 1.55);
            Assert.AreEqual(1, reissues.Count, "1.6s 之前不补签");
            link.Tick(now + 1.65);
            Assert.AreEqual(2, reissues.Count, "抖动下界:2s × 0.8 = 1.6s");
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
            Assert.IsEmpty(_reissueRequests, "第二次超时后进补签退避");
            Tick(Backoff(1));
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
                unixNowMs: () => _unixNowMs, connectRunner: a => a(), random01: () => 0.5);
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
            Assert.IsEmpty(reissues, "第二次也失败 → 进补签退避");
            now += Backoff(1);
            link.Tick(now);
            Assert.AreEqual(new[] { BattleA }, reissues.ToArray(), "退避到点补签");
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
            Assert.AreEqual(new[] { BattleA }, _reissueRequests.ToArray(),
                "过期票不重试,直接补签(退避被票据期限封顶为 0)");
            Tick(BattleDirectLink.ReconnectDelaySeconds + 1);
            Assert.AreEqual(1, _conns.Count);
        }

        [Test]
        public void ExpiredTicket_ConsecutiveReissueFailures_StillBackOff_2s_4s()
        {
            // 期限已过只换来第 1 次立即补签。房间期限到了不等于首次就能拿到 BattleGone
            // (match 在 battle RPC 失败时回 kServiceUnavailable),客户端时钟偏快也会落到这里:
            // 第 2、3 次必须照常退避,不能「延迟 0」连发三次当场烧完预算
            var conn = AssignAndVerify(MakeAssignment(BattleA, expireAtMs: 2_000_000));
            _unixNowMs = 2_000_001;

            conn.PushDisconnect(); Tick();
            Assert.AreEqual(1, _reissueRequests.Count, "期限已过:第 1 次立即补签,由服务端给权威答复");
            _reissueFail((uint)common_error.KServiceUnavailable, "response tip");
            Assert.AreEqual(1, _reissueRequests.Count, "第 2 次不得同步连发");

            for (int attempt = 2; attempt <= BattleDirectLink.MaxReissuesPerBattle; attempt++)
            {
                double wait = BattleDirectLink.ReissueBackoffBaseSeconds * Math.Pow(2, attempt - 1);
                Tick(wait - 0.05);
                Assert.AreEqual(attempt - 1, _reissueRequests.Count, $"第 {attempt} 次补签未到 {wait}s 退避不发");
                Tick(0.1);
                Assert.AreEqual(attempt, _reissueRequests.Count, $"第 {attempt} 次补签在 {wait}s 退避后发出");
                Assert.IsEmpty(_closed);
                _reissueFail((uint)common_error.KServiceUnavailable, "response tip");
            }

            Assert.AreEqual(new[] { BattleLinkCloseKind.Unreachable }, _closedKinds.ToArray());
        }

        [Test]
        public void ExpiredTicket_ReissuerUnavailable_DoesNotExhaustBudgetSynchronously()
        {
            // 期限已过 + 大厅此刻发不出补签(TicketReissuer 返回 false):若每次都「延迟 0」,
            // SendReissue → 失败 → 再排补签会同步递归,当场判 Unreachable。第 1 次之后必须回到正常退避
            var conn = AssignAndVerify(MakeAssignment(BattleA, expireAtMs: 2_000_000));
            _unixNowMs = 2_000_001;
            _reissueAvailable = false;

            conn.PushDisconnect(); Tick();
            Assert.AreEqual(BattleDirectLink.LinkState.Idle, _link.State, "不得同步烧完预算");
            Assert.IsEmpty(_closedKinds);

            _reissueAvailable = true;                    // 大厅恢复
            Tick(Backoff(2));
            Assert.AreEqual(new[] { BattleA }, _reissueRequests.ToArray(), "第 2 次按 2s 退避发出");
            _reissueOk(MakeAssignment(BattleA, ticket: "fresh"));
            Tick();
            Verify(Current, BattleA);
            Assert.IsTrue(_link.IsVerified);
            Assert.IsEmpty(_closedKinds);
        }

        [Test]
        public void RecoveryBudget_IsCapped_PerTicketAndPerBattle()
        {
            // 每张票:验证 → 断 → 同票重连 → 验证 → 断 → 退避补签(新票)……
            // 补签 MaxReissuesPerBattle 次之后再断两次 → 终结(Unreachable)。服务端接了又断的反复抖动
            // 不会变成无限重连:连接总数 = (1 + 补签次数) × 2。
            AssignAndVerify(MakeAssignment(BattleA, ticket: "t0"));
            Assert.AreEqual(1, _conns.Count);

            for (int reissue = 1; reissue <= BattleDirectLink.MaxReissuesPerBattle; reissue++)
            {
                Current.PushDisconnect(); Tick();
                Tick(BattleDirectLink.ReconnectDelaySeconds + 0.1);
                Assert.AreEqual(reissue * 2, _conns.Count, "新票据给 1 次同票重连");
                Verify(Current, BattleA);

                Current.PushDisconnect(); Tick();
                Assert.AreEqual(reissue - 1, _reissueRequests.Count, "同票预算用完 → 进补签退避");
                Tick(Backoff(reissue));
                Assert.AreEqual(reissue, _reissueRequests.Count, $"第 {reissue} 次补签");
                _reissueOk(MakeAssignment(BattleA, ticket: "t" + reissue));
                Tick();
                Assert.AreEqual(reissue * 2 + 1, _conns.Count);
                Verify(Current, BattleA);
                Assert.IsEmpty(_closed);
            }

            Current.PushDisconnect(); Tick();
            Tick(BattleDirectLink.ReconnectDelaySeconds + 0.1);
            Verify(Current, BattleA);
            Current.PushDisconnect(); Tick();

            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State, "补签预算也用完 → 终结");
            Assert.AreEqual(new[] { BattleLinkCloseKind.Unreachable }, _closedKinds.ToArray(),
                "连不上:由 UI 提示并提供手动重连,不再有 gate 回落");
            Assert.AreEqual(BattleDirectLink.MaxReissuesPerBattle, _reissueRequests.Count);
            Assert.AreEqual((1 + BattleDirectLink.MaxReissuesPerBattle) * 2, _conns.Count);
            Tick(Backoff(BattleDirectLink.MaxReissuesPerBattle + 1) + 1);
            Assert.AreEqual((1 + BattleDirectLink.MaxReissuesPerBattle) * 2, _conns.Count, "终结后不再建连");
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
            Assert.AreEqual(new[] { BattleLinkCloseKind.Ended }, _closedKinds.ToArray());
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
            _link.HandleBattleEnded(0); // 0 = 当前这局(经大厅收到 scene 结算推的终局包)
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);
            Assert.AreEqual(new[] { BattleLinkCloseKind.Ended }, _closedKinds.ToArray());
            Tick(BattleDirectLink.ReconnectDelaySeconds + 1);
            Assert.AreEqual(1, _conns.Count);
        }

        [Test]
        public void Ended_WhileWaitingReissueBackoff_FinishesWithoutReissue()
        {
            var conn = AssignAndConnect(MakeAssignment(BattleA));
            Verify(conn, 0, success: false);           // 握手被拒 → 补签退避中
            Assert.AreEqual(BattleDirectLink.LinkState.Idle, _link.State);
            _link.HandleBattleEnded(BattleA);
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);
            Assert.AreEqual(new[] { BattleLinkCloseKind.Ended }, _closedKinds.ToArray());
            Tick(Backoff(1) + 1);
            Assert.IsEmpty(_reissueRequests, "战斗已结束,退避到点也不补签");
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

        [Test]
        public void NewBattleAssignment_WhileOldBattleOpen_RaisesSupersededForOld()
        {
            // 观战 B 时进了自己的战斗 A:参战分配包把链路换到 A。收缩后观战帧只走直连,
            // 旧局 B 从此断流 —— 必须告诉订阅方,否则观战方永远停在 Watching
            AssignAndVerify(MakeAssignment(BattleB, role: eBattleTicketRole.BattleTicketRoleObserver));
            _link.HandleAssigned(MakeAssignment(BattleA));

            Assert.AreEqual(new[] { BattleLinkCloseKind.Superseded }, _closedKinds.ToArray());
            Assert.AreEqual(new[] { BattleB }, _closedBattleIds.ToArray(), "通知带旧局 id");
            Assert.AreEqual(BattleA, _link.BattleId, "通知发出时链路已切到新局");
            Assert.AreEqual(BattleDirectLink.LinkState.Connecting, _link.State, "链路本身不终结,照常为新局建连");

            Tick();
            Verify(Current, BattleA);
            Assert.IsTrue(_link.IsVerified);
            Assert.AreEqual(1, _closedKinds.Count, "新局不受影响");
        }

        [Test]
        public void HostEntryForOtherBattle_WhileOldBattleRecovering_RaisesSuperseded()
        {
            // EnsureBattle / Retry / 重连提示指向另一局:旧局哪怕正在恢复(未终结)也要通知
            var conn = AssignAndVerify(MakeAssignment(BattleB, role: eBattleTicketRole.BattleTicketRoleObserver));
            conn.PushDisconnect(); Tick();                       // B 同票重连倒计时中(Idle,未终结)
            _link.EnsureBattle(BattleA);
            Assert.AreEqual(new[] { BattleLinkCloseKind.Superseded }, _closedKinds.ToArray());
            Assert.AreEqual(new[] { BattleB }, _closedBattleIds.ToArray());
            Assert.AreEqual(new[] { BattleA }, _reissueRequests.ToArray(), "新局照常补签");

            _link.Retry(BattleB);                                // 再切回 B:A 的补签在途(未终结)→ A 被顶替
            Assert.AreEqual(new[] { BattleB, BattleA }, _closedBattleIds.ToArray());
            Assert.AreEqual(new[] { BattleA, BattleB }, _reissueRequests.ToArray());
        }

        [Test]
        public void Switch_FromClosedOrWithinSameBattle_NoSupersededEvent()
        {
            ExhaustReissueBudget();                              // A 已 Closed(Unreachable)
            _link.HandleAssigned(MakeAssignment(BattleB));       // 旧局已抛过终结事件,不重复
            Assert.AreEqual(new[] { BattleLinkCloseKind.Unreachable }, _closedKinds.ToArray());

            Tick();
            Verify(Current, BattleB);
            Current.PushDisconnect(); Tick();                    // B 恢复中
            _link.HandleReconnectHint(BattleB);                  // 同一局重建不是换局
            Assert.AreEqual(new[] { BattleLinkCloseKind.Unreachable }, _closedKinds.ToArray());
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
            Assert.AreEqual(new[] { BattleLinkCloseKind.HostClosed }, _closedKinds.ToArray());
            Assert.AreEqual(new[] { BattleA }, _closedBattleIds.ToArray());
            Assert.AreEqual(0UL, _link.BattleId);
            Assert.IsNull(_link.Assignment);
            Tick(BattleDirectLink.ReconnectDelaySeconds + 1);
            Assert.AreEqual(1, _conns.Count);
            Assert.IsEmpty(_reissueRequests);
        }

        [Test]
        public void Close_DuringReissueBackoff_CancelsReissue()
        {
            var conn = AssignAndConnect(MakeAssignment(BattleA));
            Verify(conn, 0, success: false);            // 补签退避中
            _link.Close("lobby_disconnected");
            Tick(Backoff(1) + 1);
            Assert.IsEmpty(_reissueRequests, "宿主关闭后退避到点也不补签");
            Assert.AreEqual(new[] { BattleLinkCloseKind.HostClosed }, _closedKinds.ToArray());
        }

        [Test]
        public void Close_ThenLateReissueCallback_IsIgnored()
        {
            var conn = AssignAndConnect(MakeAssignment(BattleA));
            Verify(conn, 0, success: false); // 触发补签(退避后发出)
            Tick(Backoff(1));
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
            // 上一局把补签预算用完并终结;新的重连提示是新的语境,预算重新给、立即补签
            ExhaustReissueBudget();
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);

            _link.HandleReconnectHint(BattleA);
            Assert.AreEqual(BattleDirectLink.MaxReissuesPerBattle + 1, _reissueRequests.Count);
        }

        [Test]
        public void ReconnectHint_DuringBackoff_ReissuesImmediately_Once()
        {
            var conn = AssignAndConnect(MakeAssignment(BattleA));
            Verify(conn, 0, success: false);            // 自动补签退避中
            _link.HandleReconnectHint(BattleA);         // 服务端推了重连提示:不等退避
            Assert.AreEqual(new[] { BattleA }, _reissueRequests.ToArray());
            Tick(Backoff(1) + 1);
            Assert.AreEqual(1, _reissueRequests.Count, "原先排着的退避补签已作废,不重复发");
        }

        // ── 宿主入口:Retry / EnsureBattle / Abandon(turn-based §22 D74) ──

        /// <summary>握手被拒后连续补签失败(kServiceUnavailable)直到预算用完 → Closed(Unreachable)。</summary>
        private void ExhaustReissueBudget()
        {
            var conn = AssignAndConnect(MakeAssignment(BattleA));
            Verify(conn, 0, success: false);
            for (int attempt = 1; attempt <= BattleDirectLink.MaxReissuesPerBattle; attempt++)
            {
                Tick(Backoff(attempt));
                _reissueFail((uint)common_error.KServiceUnavailable, "response tip");
            }
            Assert.AreEqual(new[] { BattleLinkCloseKind.Unreachable }, _closedKinds.ToArray());
        }

        [Test]
        public void Retry_AfterClosed_ResetsBudgetAndReconnects()
        {
            ExhaustReissueBudget();
            int before = _reissueRequests.Count;

            _link.Retry(BattleA);
            Assert.AreEqual(before + 1, _reissueRequests.Count, "手动重连立即补签(不等退避)");
            _reissueOk(MakeAssignment(BattleA, ticket: "manual"));
            Tick();
            Verify(Current, BattleA);
            Assert.IsTrue(_link.IsVerified);

            // 预算已重置:之后再断,仍有同票重连与补签可用
            Current.PushDisconnect(); Tick();
            Tick(BattleDirectLink.ReconnectDelaySeconds + 0.1);
            Verify(Current, BattleA);
            Current.PushDisconnect(); Tick();
            Tick(Backoff(2));
            Assert.AreEqual(before + 2, _reissueRequests.Count, "重置后的第 2 次补签按 2s 退避");
            Assert.AreEqual(1, _closedKinds.Count, "手动重连成功后没有新的终结");
        }

        [Test]
        public void Retry_WhileVerified_OrReissueInFlight_NoOp()
        {
            AssignAndVerify(MakeAssignment(BattleA));
            _link.Retry(BattleA);
            Assert.IsEmpty(_reissueRequests);
            Assert.IsTrue(_link.IsVerified);

            _link.Close("lobby_disconnected");
            _link.Retry(BattleA);
            _link.Retry(BattleA);
            Assert.AreEqual(1, _reissueRequests.Count, "补签在途时重复点击不重复打 match");
            _link.Retry(0);
            Assert.AreEqual(1, _reissueRequests.Count);
        }

        [Test]
        public void EnsureBattle_WithoutAssignment_Reissues()
        {
            // 分配包丢了:开局包到达时链路不服务该局
            _link.EnsureBattle(BattleA);
            Assert.AreEqual(new[] { BattleA }, _reissueRequests.ToArray());
            _reissueOk(MakeAssignment(BattleA));
            Tick();
            Verify(Current, BattleA);
            Assert.IsTrue(_link.IsVerified);
        }

        [Test]
        public void EnsureBattle_OtherBattleLink_SwitchesByReissue()
        {
            var old = AssignAndVerify(MakeAssignment(BattleB, role: eBattleTicketRole.BattleTicketRoleObserver));
            _link.EnsureBattle(BattleA);
            Assert.IsTrue(old.Disposed, "不服务本局的旧连接作废");
            Assert.AreEqual(new[] { BattleA }, _reissueRequests.ToArray());
            Assert.AreEqual(BattleA, _link.BattleId);
        }

        [Test]
        public void EnsureBattle_SameBattleLiveOrRecovering_NoOp()
        {
            var conn = AssignAndConnect(MakeAssignment(BattleA));   // Handshaking
            _link.EnsureBattle(BattleA);
            Assert.IsEmpty(_reissueRequests, "在连:不打扰");

            Verify(conn, BattleA);
            _link.EnsureBattle(BattleA);
            Assert.IsEmpty(_reissueRequests, "已验证:不打扰");

            Current.PushDisconnect(); Tick();                   // 同票重连倒计时中
            _link.EnsureBattle(BattleA);
            Assert.IsEmpty(_reissueRequests, "恢复中:不打扰");
            Assert.AreEqual(1, _conns.Count);
            _link.EnsureBattle(0);
            Assert.IsEmpty(_reissueRequests);
        }

        [Test]
        public void EnsureBattle_SameBattleClosed_Reissues()
        {
            ExhaustReissueBudget();
            int before = _reissueRequests.Count;
            _link.EnsureBattle(BattleA);
            Assert.AreEqual(before + 1, _reissueRequests.Count, "同局但已终结:重新补签");
        }

        [Test]
        public void Abandon_BeforeVerified_ClosesWithoutRecovery()
        {
            var conn = AssignAndConnect(MakeAssignment(BattleA, role: eBattleTicketRole.BattleTicketRoleObserver));
            string err = null;
            _link.Call(MessageIds.GetBattleState, new GetBattleStateRequest(), BattleStateS2C.Parser, _ => { }, e => err = e);

            _link.Abandon(BattleA);

            Assert.IsTrue(conn.Disposed);
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);
            Assert.AreEqual(new[] { BattleLinkCloseKind.Ended }, _closedKinds.ToArray());
            Assert.AreEqual(BattleDirectLink.NotReadyError, err, "未验证时的请求本就本地失败");
            Tick(Backoff(3) + BattleDirectLink.HandshakeTimeoutSeconds + 1);
            Assert.AreEqual(1, _conns.Count, "放弃后不再重连");
            Assert.IsEmpty(_reissueRequests, "也不补签");
        }

        [Test]
        public void Abandon_DuringReissueInFlight_IgnoresLateTicket()
        {
            var conn = AssignAndConnect(MakeAssignment(BattleA, role: eBattleTicketRole.BattleTicketRoleObserver));
            Verify(conn, 0, success: false);
            Tick(Backoff(1));
            Assert.AreEqual(1, _reissueRequests.Count);

            _link.Abandon(BattleA);
            _reissueOk(MakeAssignment(BattleA, ticket: "late"));
            Tick();
            Assert.AreEqual(1, _conns.Count, "放弃后迟到的补签票不得重建连接");
            Assert.AreEqual(BattleDirectLink.LinkState.Closed, _link.State);
        }

        [Test]
        public void Abandon_OtherBattle_NoOp()
        {
            AssignAndVerify(MakeAssignment(BattleA));
            _link.Abandon(BattleB);
            _link.Abandon(0);
            Assert.IsTrue(_link.IsVerified, "不是本链路服务的局:无事");
            Assert.IsEmpty(_closed);
        }

        // ── 评审确认项的回归 ────────────────────────────────

        [Test]
        public void Connecting_TimesOut_AndRecovers()
        {
            // 连接动作永不返回(endpoint 黑洞:SYN 无应答)。Connecting 必须自己封顶,
            // 否则会卡到系统 SYN 超时(约 21s),期间既不重试也不补签。
            var conns = new List<FakeFramedConnection>();
            var reissues = new List<ulong>();
            var link = new BattleDirectLink(
                () => { var c = new FakeFramedConnection(); conns.Add(c); return c; },
                unixNowMs: () => _unixNowMs,
                connectRunner: _ => { /* 永远不完成,也不入队结果 */ },
                random01: () => 0.5);
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
            Assert.IsEmpty(reissues, "第二次也超时 → 进补签退避");
            now += Backoff(1);
            link.Tick(now);
            Assert.AreEqual(new[] { BattleA }, reissues.ToArray(), "退避到点补签");
        }

        [Test]
        public void PendingCall_FailsWithTransportPrefix_OnConnectTimeout()
        {
            // 传输层失败必须带前缀:上层据此区分「直连没送到(等就绪补拉 / 补发)」与「服务端拒绝」
            AssignAndVerify(MakeAssignment(BattleA));
            string err = null;
            _link.Call(MessageIds.GetBattleState, new GetBattleStateRequest(), BattleStateS2C.Parser, _ => { }, e => err = e);
            Current.PushDisconnect();
            Tick();
            StringAssert.StartsWith(BattleDirectLink.TransportErrorPrefix, err);
        }

        [Test]
        public void ReissueBackoffAndInFlight_LeaveStateIdle_NotStaleVerified()
        {
            // 断开 → 同票重连 → 再断开 → 补签退避 → 补签在途:整个过程中 State 不得停在 Verified,
            // 否则 IsVerified 会对着一条不存在的连接返回 true,战斗 RPC 全被路由进直连然后静默丢掉。
            var c1 = AssignAndVerify(MakeAssignment(BattleA));
            c1.PushDisconnect(); Tick();
            Tick(BattleDirectLink.ReconnectDelaySeconds + 0.1);
            Verify(Current, BattleA);
            Assert.IsTrue(_link.IsVerified);

            Current.PushDisconnect(); Tick();          // 同票预算用完 → 补签退避
            Assert.IsFalse(_link.IsVerified, "退避中不得自称已验证");
            Assert.AreEqual(BattleDirectLink.LinkState.Idle, _link.State);

            Tick(Backoff(1));                           // 补签在途
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
            Verify(conn, 0, success: false);            // 握手被拒 → 退避后补签
            Tick(Backoff(1));
            Assert.AreEqual(1, _reissueRequests.Count);

            _link.HandleAssigned(MakeAssignment(BattleA, ticket: "pushed"));
            Tick();
            Verify(Current, BattleA);
            Assert.IsTrue(_link.IsVerified);

            _reissueFail(0, "timeout");                 // 迟到的失败回调
            Assert.IsTrue(_link.IsVerified, "已重建好的链路不能被迟到的补签失败拆掉");
            Assert.IsEmpty(_closed);
        }

        [Test]
        public void PushedAssignment_CancelsPendingReissueBackoff()
        {
            var conn = AssignAndConnect(MakeAssignment(BattleA));
            Verify(conn, 0, success: false);            // 补签退避中
            _link.HandleAssigned(MakeAssignment(BattleA, ticket: "pushed"));
            Tick();
            Verify(Current, BattleA);
            Tick(Backoff(1) + 1);
            Assert.IsEmpty(_reissueRequests, "新分配包已把链路建好,排着的补签作废");
            Assert.IsTrue(_link.IsVerified);
        }

        [Test]
        public void SameBattleRebuild_InvalidatesInFlightReissue()
        {
            var conn = AssignAndConnect(MakeAssignment(BattleA));
            Verify(conn, 0, success: false);
            Tick(Backoff(1));
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
