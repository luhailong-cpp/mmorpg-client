using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using Google.Protobuf;
using Loginpb;
using MmorpgClient.Game;
using MmorpgClient.Net;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MmorpgClient.Tests.EditMode.Net
{
    /// <summary>
    /// 同一条连接上换角与重发的编排(GameClient.RoleSwitch.cs 的 EnterGameOnConnection / LeaveGameOnConnection / ReloginOnConnection),
    /// 走真 TCP:客户端连一个回环假 gate,假 gate 按脚本逐条认请求、回包。<see cref="EnterGameRoleSwitchTests"/> 只测纯函数
    /// ClassifyEnterGameReply;这里测它编排起来之后实际发出的请求序列、请求号与收尾(2026-11-26,评审建议)。
    /// 选角页上「进场失败 → 回选角」的次数上限(MaxEnterReselects)在完整登录管线里,要假网关 HTTP、验票、Login 全套,不在这里。
    /// </summary>
    public sealed class EnterGameRoleSwitchFlowTests
    {
        private const ulong RoleA = 7001;
        private const ulong RoleB = 7002;
        private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameClient _game;
        private GateTcpClient _gate;
        private TcpListener _listener;
        private TcpListener _gatewayListener;
        private TcpClient _peer;

        [SetUp]
        public void SetUp()
        {
            // 构造 GameClient 要一个网关地址;这些用例不走 HTTP,只占一个端口。
            _gatewayListener = new TcpListener(IPAddress.Loopback, 0); _gatewayListener.Start();
            _game = new GameClient($"http://127.0.0.1:{((IPEndPoint)_gatewayListener.LocalEndpoint).Port}");
            _game.CoroutineRunner = _ => null;
            _game.SetTokens("test-access", "test-refresh", DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600);
            SetProperty("Account", "role-switch-test"); SetProperty("TokenVerified", true);
            _listener = new TcpListener(IPAddress.Loopback, 0); _listener.Start();
            _gate = new GateTcpClient((MuduoCodec)Get("_codec"));
            _gate.Connect("127.0.0.1", ((IPEndPoint)_listener.LocalEndpoint).Port);
            _peer = _listener.AcceptTcpClient();
            Set("_gate", _gate);
            _gate.OnMessage += message => Invoke("DispatchInbound", message);
            _gate.OnDisconnected += () => Invoke("HandleTransportDisconnected");
        }

        [TearDown]
        public void TearDown()
        {
            _game?.Disconnect(); _gate?.Dispose(); _peer?.Dispose(); _listener?.Stop(); _gatewayListener?.Stop();
            if (_game?.World.Root != null) UnityEngine.Object.DestroyImmediate(_game.World.Root.gameObject);
        }

        /// <summary>
        /// 本连接绑着 A、要进 B:先 LeaveGame;C++ 版 LeaveGame 清掉登录态,EnterGame(B) 回 2028;同一连接上用 access_token 重新 Login,
        /// 再发 EnterGame(B),请求号沿用第一次的(login.proto:同一请求的重试必须沿用),受理之后本连接记成绑着 B。
        /// </summary>
        [UnityTest]
        public IEnumerator SwitchingRoleLeavesFirstThenLogsInAgainAfterSessionNotFoundAndResendsTheSameRequest()
        {
            Set("_enterRequestPlayerId", RoleA);
            var gate = Serve(
                call => Reply(call, MessageIds.LeaveGame, new LoginEmptyResponse()),
                call => Reply(call, MessageIds.EnterGame, new EnterGameResponse { ErrorMessage = new TipInfoMessage { Id = (uint)login_error.KLoginSessionNotFound } }),
                call => Reply(call, MessageIds.Login, new LoginResponse()),
                call => Reply(call, MessageIds.EnterGame, new EnterGameResponse { PlayerId = RoleB }));
            var errors = new List<string>();
            var outcome = NewOutcome();
            yield return Pump(EnterOnConnection(RoleB, errors.Add, outcome));
            gate.Finish();

            Assert.That(errors, Is.Empty);
            Assert.That(ResponseOf(outcome)?.PlayerId, Is.EqualTo(RoleB));
            Assert.That(ResponseOf(outcome).ErrorMessage, Is.Null);
            var login = LoginRequest.Parser.ParseFrom(gate.Seen[2].Body);
            Assert.That(login.AuthType, Is.EqualTo("access_token"), "重新 Login 用令牌,口令不走游戏 TCP");
            var first = EnterGameRequest.Parser.ParseFrom(gate.Seen[1].Body);
            var again = EnterGameRequest.Parser.ParseFrom(gate.Seen[3].Body);
            Assert.That(first.PlayerId, Is.EqualTo(RoleB));
            Assert.That(again.RequestId, Is.EqualTo(first.RequestId), "重新登录之后重发的是同一个进场请求");
            Assert.That(Get("_enterRequestPlayerId"), Is.EqualTo(RoleB));
            Assert.That(gate.Extra, Is.Empty, "受理之后不再发别的");
        }

        /// <summary>
        /// LeaveGame 一直结果不明(LoginRedis 故障时 login 按 fail-closed 回 1003):含第一次最多发 3 次,还确认不了就收掉管线,
        /// 不带着可能还在的绑定去进 B。
        /// </summary>
        [UnityTest]
        public IEnumerator LeaveGameThatStaysUnknownIsTriedThreeTimesThenTheConnectionIsDropped()
        {
            Set("_enterRequestPlayerId", RoleA);
            var gate = Serve(
                call => Envelope(call, MessageIds.LeaveGame, (uint)common_error.KServiceUnavailable),
                call => Envelope(call, MessageIds.LeaveGame, (uint)common_error.KServiceUnavailable),
                call => Envelope(call, MessageIds.LeaveGame, (uint)common_error.KServiceUnavailable));
            var errors = new List<string>();
            var outcome = NewOutcome();
            yield return Pump(EnterOnConnection(RoleB, errors.Add, outcome));
            gate.Finish();

            Assert.That(errors, Is.EqualTo(new[] { "无法退出当前角色，请重新登录" }));
            Assert.That(ResponseOf(outcome), Is.Null);
            Assert.That(gate.Extra, Is.Empty, "确认不了 LeaveGame 就不发 EnterGame");
        }

        /// <summary>
        /// 同一角色首发结果不明(信封 1003):原样重发一次、不先 LeaveGame;重发撞上前一次的进场链(2005,玩家锁被占)时
        /// 按「前一次多半已受理」接着等进场结果,不报失败。
        /// </summary>
        [UnityTest]
        public IEnumerator SameRoleUnknownOutcomeIsResentOnceAndInProgressWaitsForThePreviousEnter()
        {
            Set("_enterRequestPlayerId", RoleA);
            var gate = Serve(
                call => Envelope(call, MessageIds.EnterGame, (uint)common_error.KServiceUnavailable),
                call => Reply(call, MessageIds.EnterGame, new EnterGameResponse { ErrorMessage = new TipInfoMessage { Id = (uint)login_error.KLoginInProgress } }));
            var errors = new List<string>();
            var outcome = NewOutcome();
            yield return Pump(EnterOnConnection(RoleA, errors.Add, outcome));
            gate.Finish();

            Assert.That(errors, Is.Empty);
            Assert.That(ResponseOf(outcome)?.PlayerId, Is.EqualTo(RoleA), "InProgress 交给调用方去等进场推送");
            Assert.That(ResponseOf(outcome).ErrorMessage, Is.Null);
            Assert.That(EnterGameRequest.Parser.ParseFrom(gate.Seen[1].Body).RequestId,
                Is.EqualTo(EnterGameRequest.Parser.ParseFrom(gate.Seen[0].Body).RequestId), "重发沿用同一个请求号");
            Assert.That(gate.Extra, Is.Empty);
        }

        /// <summary>
        /// 同一角色重发撞上进场池满(2058,2026-11-26 起不再回 2005):前一次没有在跑的链,按拒绝交给调用方,不白等 60 秒。
        /// </summary>
        [UnityTest]
        public IEnumerator SameRoleResendThatFindsLoginBusyIsHandedBackAsARejection()
        {
            Set("_enterRequestPlayerId", RoleA);
            var gate = Serve(
                call => Envelope(call, MessageIds.EnterGame, (uint)common_error.KServiceUnavailable),
                call => Reply(call, MessageIds.EnterGame, new EnterGameResponse { ErrorMessage = new TipInfoMessage { Id = (uint)login_error.KLoginEnterGameBusy } }));
            var errors = new List<string>();
            var outcome = NewOutcome();
            yield return Pump(EnterOnConnection(RoleA, errors.Add, outcome));
            gate.Finish();

            Assert.That(errors, Is.Empty, "拒绝由调用方按码收尾,这里不收管线");
            Assert.That(ResponseOf(outcome)?.ErrorMessage?.Id, Is.EqualTo((uint)login_error.KLoginEnterGameBusy));
            Assert.That(gate.Extra, Is.Empty);
        }

        // ── 假 gate ───────────────────────────────────────────────────────────────────

        /// <summary>按脚本逐条应答的假 gate:每一步认一条请求(心跳自动回),回那一步给的包;脚本走完后再看客户端有没有多发。</summary>
        private sealed class ScriptedGate
        {
            public readonly List<ClientRequest> Seen = new();
            public readonly List<ClientRequest> Extra = new();
            public Exception Failure;
            public Thread Thread;

            public void Finish()
            {
                Assert.That(Thread.Join(5000), Is.True, "假 gate 线程没有结束");
                Assert.That(Failure, Is.Null, Failure?.ToString());
            }
        }

        private ScriptedGate Serve(params Func<ClientRequest, MessageContent>[] steps)
        {
            var gate = new ScriptedGate();
            var stream = _peer.GetStream();
            var codec = new MuduoCodec();
            codec.Register<ClientHeartbeatRequest>(); codec.Register<ClientRequest>();
            gate.Thread = new Thread(() =>
            {
                try
                {
                    stream.ReadTimeout = 5000;
                    foreach (var step in steps)
                    {
                        var call = ReadRequest(stream, codec);
                        gate.Seen.Add(call);
                        WriteFrame(stream, codec, step(call));
                    }
                    // 脚本走完:短时间内客户端不该再发请求(断开或超时都算没有)。
                    stream.ReadTimeout = 300;
                    try { gate.Extra.Add(ReadRequest(stream, codec)); }
                    catch (IOException) { }
                    catch (EndOfStreamException) { }
                }
                catch (Exception ex)
                {
                    gate.Failure = ex;
                    // 脚本对不上时断开连接,客户端那边的调用立即失败、协程走完,由 Finish 报出这里的原因(而不是等 10 秒超时)。
                    try { stream.Close(); } catch (Exception) { }
                }
            }) { IsBackground = true };
            gate.Thread.Start();
            return gate;
        }

        private static ClientRequest ReadRequest(NetworkStream stream, MuduoCodec codec)
        {
            while (true)
            {
                var message = ReadFrame(stream, codec);
                if (message is ClientHeartbeatRequest hb) { WriteFrame(stream, codec, new ClientHeartbeatResponse { Seq = hb.Seq }); continue; }
                return (ClientRequest)message;
            }
        }

        private static MessageContent Reply(ClientRequest call, uint expected, IMessage body)
        {
            Assert.That(call.MessageId, Is.EqualTo(expected), $"假 gate 期望 message_id={expected},收到 {call.MessageId}");
            return new MessageContent { Id = call.Id, MessageId = call.MessageId, SerializedMessage = body.ToByteString() };
        }

        private static MessageContent Envelope(ClientRequest call, uint expected, uint tip)
        {
            Assert.That(call.MessageId, Is.EqualTo(expected), $"假 gate 期望 message_id={expected},收到 {call.MessageId}");
            return new MessageContent { Id = call.Id, MessageId = call.MessageId, ErrorMessage = new TipInfoMessage { Id = tip } };
        }

        private static IMessage ReadFrame(NetworkStream stream, MuduoCodec codec)
        {
            byte[] ReadExactly(int count)
            {
                var data = new byte[count];
                for (int offset = 0; offset < count;)
                { int n = stream.Read(data, offset, count - offset); if (n == 0) throw new EndOfStreamException(); offset += n; }
                return data;
            }
            byte[] header = ReadExactly(4);
            int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            Assert.That(length, Is.InRange(1, 65536));
            var frame = new byte[length + 4];
            Buffer.BlockCopy(header, 0, frame, 0, 4); Buffer.BlockCopy(ReadExactly(length), 0, frame, 4, length);
            return codec.TryDecode(frame, 0, frame.Length, out _);
        }

        private static void WriteFrame(NetworkStream stream, MuduoCodec codec, IMessage message)
        { byte[] bytes = codec.Encode(message); stream.Write(bytes, 0, bytes.Length); }

        // ── 驱动私有协程 ───────────────────────────────────────────────────────────────

        private IEnumerator EnterOnConnection(ulong playerId, Action<string> onError, object outcome)
            => (IEnumerator)typeof(GameClient).GetMethod("EnterGameOnConnection", Hidden)
                .Invoke(_game, new object[] { (int)Get("_pipelineGen"), playerId, "", onError, outcome });

        private static object NewOutcome()
            => Activator.CreateInstance(typeof(GameClient).GetNestedType("EnterGameOutcome", BindingFlags.NonPublic), true);

        private static EnterGameResponse ResponseOf(object outcome)
            => (EnterGameResponse)outcome.GetType().GetField("Response").GetValue(outcome);

        /// <summary>按生产协程的语义展开嵌套协程,每步 Tick 一次(派发回包);10 秒还没走完判失败,不挂死。</summary>
        private IEnumerator Pump(IEnumerator routine)
        {
            var clock = Stopwatch.StartNew();
            var stack = new Stack<IEnumerator>(); stack.Push(routine);
            try
            {
                while (stack.Count != 0)
                {
                    Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)), "换角编排 10 秒没走完");
                    var next = stack.Peek();
                    if (!next.MoveNext()) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (next.Current is IEnumerator nested) { stack.Push(nested); continue; }
                    _game.Tick();
                    yield return null;
                }
            }
            finally { while (stack.Count != 0) (stack.Pop() as IDisposable)?.Dispose(); }
        }

        private object Get(string field) => typeof(GameClient).GetField(field, Hidden).GetValue(_game);
        private void Set(string field, object value) => typeof(GameClient).GetField(field, Hidden).SetValue(_game, value);
        private void SetProperty(string property, object value) => Set("<" + property + ">k__BackingField", value);
        private void Invoke(string method, params object[] args) => typeof(GameClient).GetMethod(method, Hidden).Invoke(_game, args);
    }
}
