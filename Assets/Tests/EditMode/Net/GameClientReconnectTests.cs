using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Text;
using Google.Protobuf;
using MmorpgClient.Game;
using MmorpgClient.Net;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MmorpgClient.Tests.EditMode.Net
{
    public sealed class GameClientReconnectTests
    {
        private GameClient _game;
        private GateTcpClient _gate;
        private TcpListener _listener;
        private TcpListener _gatewayListener;
        private TcpClient _peer;
        private readonly List<IEnumerator> _routines = new();
        private int _disconnected;
        private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp]
        public void SetUp()
        {
            _routines.Clear(); _disconnected = 0;
            _gatewayListener = new TcpListener(IPAddress.Loopback, 0); _gatewayListener.Start();
            _game = new GameClient($"http://127.0.0.1:{((IPEndPoint)_gatewayListener.LocalEndpoint).Port}");
            _game.CoroutineRunner = routine => { _routines.Add(routine); return null; };
            _game.OnDisconnected += () => _disconnected++;
            _game.SetTokens("test-access", "test-refresh", DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600);
            SetProperty("Account", "heartbeat-test"); SetProperty("PlayerId", 42ul);
            SetProperty("CurrentZoneId", 1u); SetProperty("InGame", true); SetProperty("TokenVerified", true);
            _listener = new TcpListener(IPAddress.Loopback, 0); _listener.Start();
            var codec = (MuduoCodec)Get("_codec");
            _gate = new GateTcpClient(codec);
            _gate.Connect("127.0.0.1", ((IPEndPoint)_listener.LocalEndpoint).Port);
            _peer = _listener.AcceptTcpClient();
            Set("_gate", _gate); Set("_disconnectNotificationSent", false);
            _gate.OnMessage += message => Invoke("DispatchInbound", message);
            _gate.OnDisconnected += () => Invoke("HandleTransportDisconnected");
        }

        [TearDown]
        public void TearDown()
        {
            _game?.Disconnect(); _gate?.Dispose(); _peer?.Dispose(); _listener?.Stop(); _gatewayListener?.Stop();
            if (_game?.World.Root != null) UnityEngine.Object.DestroyImmediate(_game.World.Root.gameObject);
        }

        [Test]
        public void UnexpectedEofStartsOneRecoveryWithOriginalRoleAndKeepsFailureUiClosed()
        {
            PeerEof(); _game.Tick();
            Assert.That(_game.IsReconnecting, Is.True);
            Assert.That(_routines.Count, Is.EqualTo(1));
            Assert.That(Get("_reconnectPlayerId"), Is.EqualTo(42ul));
            Assert.That(_game.InGame, Is.False);
            Assert.That(_game.GateConnectionIdentity, Is.Null);
            Assert.That(_disconnected, Is.Zero);
            _game.Tick();
            Assert.That(_routines.Count, Is.EqualTo(1));
        }

        [Test]
        public void ExplicitLogoutCancelsQueuedRecoveryBeforeItCanIssueHttp()
        {
            PeerEof(); _game.Tick();
            var recovery = _routines[0];
            _game.Disconnect();
            Assert.That(_game.IsReconnecting, Is.False);
            Assert.That(_disconnected, Is.EqualTo(1), "等待新票据时旧会话已清掉，显式登出仍须通知界面");
            Assert.That(recovery.MoveNext(), Is.False, "登出后旧协程不能再次请求新票据");
        }

        [Test]
        public void TotalDeadlineEndsRecoveryAndNotifiesOnce()
        {
            PeerEof(); _game.Tick();
            Set("_reconnectDeadline", 0d);
            _game.Tick(); _game.Tick();
            Assert.That(_game.IsReconnecting, Is.False);
            Assert.That(_disconnected, Is.EqualTo(1));
            Assert.That(_game.DisconnectReason, Is.EqualTo("重新连接失败，请重新进入游戏"));
            Assert.That(_routines[0].MoveNext(), Is.False);
        }

        [Test]
        public void QueuedKickBeforeEofDoesNotAutoReclaimAccount()
        {
            var codec = (MuduoCodec)Get("_codec");
            byte[] frame = codec.Encode(new MessageContent
            {
                MessageId = MessageIds.KickPlayer,
                SerializedMessage = new GameKickPlayerRequest().ToByteString()
            });
            _peer.GetStream().Write(frame, 0, frame.Length);
            PeerEof(); _game.Tick();
            Assert.That(_routines, Is.Empty);
            Assert.That(_game.IsReconnecting, Is.False);
            Assert.That(_disconnected, Is.EqualTo(1));
        }

        [Test]
        public void SceneEntryResetsRedirectHopsSoRepeatedGateMigrationsAreNotCutOff()
        {
            // 一个 zone 的 gate 逐台换完,同一个玩家会被迁不止 MaxRedirectHops 次;每次迁完成功进场都从头计数,
            // 否则第 4 次迁移会撞上环路熔断、被强制断线(服务端 gate-drain-session-migration.md §7.3)。
            Set("_redirectHops", GameClient.MaxRedirectHops);
            Invoke("DispatchInbound", new MessageContent
            {
                MessageId = MessageIds.NotifyEnterScene,
                SerializedMessage = new EnterSceneS2C { SceneInfo = new SceneInfoComp { SceneId = 123, SceneConfigId = 1 } }.ToByteString()
            });
            Assert.That(Get("_redirectHops"), Is.EqualTo(0));
        }

        // ── 链式重定向:搬到新 gate 的途中、还没进场又收到一条 msg 124 ──
        // 来源是 gate 排空迁移撞上跨 zone 传送的交接(第二条腿的 EnterGame 被 login 按落点改派),或新 gate 自己也开始排空
        // (服务端 docs/design/gate-drain-session-migration.md §13)。旧实现一律当重复推送忽略,这条腿只能等满 60 秒
        // 入场超时后掉回登录界面。下面几例里协程由用例手动推进:第一次 MoveNext 停在探测目标 gate 那一步,不碰网络。

        [Test]
        public void RedirectArrivingMidRedirectIsFollowedAndInheritsRoleZoneAndBattle()
        {
            // 上一段已经换过连接:角色、所在区、战斗直连都被清掉了,只剩上一段捕获在字段里的值。
            Set("_redirecting", true); Set("_redirectPlayerId", 42ul); Set("_redirectZoneId", 7u); Set("_redirectBattleId", 9001ul);
            SetProperty("PlayerId", 0ul); SetProperty("CurrentZoneId", 0u);

            Invoke("DispatchInbound", RedirectFrame(65000));

            Assert.That(_routines.Count, Is.EqualTo(1), "链式重定向要启动新的一段,不能当重复推送忽略");
            Assert.That(_routines[0].MoveNext(), Is.True);
            Assert.That(_game.IsRedirecting, Is.True);
            Assert.That(Get("_redirectHops"), Is.EqualTo(1), "链式的每一跳照常计数,受 MaxRedirectHops 约束");
            Assert.That(Get("_redirectPlayerId"), Is.EqualTo(42ul), "沿用上一段捕获的角色,不重新弹选角");
            Assert.That(Get("_redirectZoneId"), Is.EqualTo(7u), "票据不带区号、当前区已清零:沿用上一段的区");
            Assert.That(Get("_redirectBattleId"), Is.EqualTo(9001ul), "战斗直连的 battle_id 交给这一段,进场后由它补签重建");
        }

        [Test]
        public void FreshRedirectDoesNotInheritWhatAnEarlierRedirectLeftBehind()
        {
            // 不是链式(上一次重定向早已结束):字段里的旧值与这一次无关。角色取不到就是 0,第二条腿回选角。
            Set("_redirecting", false); Set("_redirectPlayerId", 42ul); Set("_redirectBattleId", 9001ul);
            SetProperty("PlayerId", 0ul);

            Invoke("DispatchInbound", RedirectFrame(65000));

            Assert.That(_routines[0].MoveNext(), Is.True);
            Assert.That(Get("_redirectPlayerId"), Is.EqualTo(0ul));
            Assert.That(Get("_redirectBattleId"), Is.EqualTo(0ul));
        }

        [Test]
        public void SupersededRedirectLeavesTheRedirectingFlagToItsSuccessor()
        {
            Invoke("DispatchInbound", RedirectFrame(65000));
            var first = _routines[0];
            Assert.That(first.MoveNext(), Is.True);
            Invoke("DispatchInbound", RedirectFrame(65001));
            Assert.That(_routines.Count, Is.EqualTo(2));
            var second = _routines[1];
            Assert.That(second.MoveNext(), Is.True);
            Assert.That(Get("_redirectHops"), Is.EqualTo(2));

            // 上一段在下一次恢复时按管线世代退出;它退出时不能把后继那一段还在用的 _redirecting 清掉。
            Assert.That(first.MoveNext(), Is.False);
            Assert.That(_game.IsRedirecting, Is.True, "后继那一段还没结束,UI 仍应显示切换中、各功能仍应暂停发请求");

            // 后继那一段自己结束(这里用显式登出作废它)时才清。
            _game.Disconnect();
            Assert.That(second.MoveNext(), Is.False);
            Assert.That(_game.IsRedirecting, Is.False);
        }

        [Test]
        public void ChainedRedirectsAreStillCutOffAtTheHopLimit()
        {
            Set("_redirecting", true); Set("_redirectHops", GameClient.MaxRedirectHops);
            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("hop limit reached"));

            Invoke("DispatchInbound", RedirectFrame(65000));

            Assert.That(_routines[0].MoveNext(), Is.False, "到跳数上限就不再跟,防配置环路把客户端拖进不停重连");
            Assert.That(_disconnected, Is.EqualTo(1));
            Assert.That(_game.DisconnectReason, Is.EqualTo("切换服务器失败,请重新登录"));
        }

        [Test]
        public void CarryOverOnChainedRedirectPrefersFreshValuesAndOnlyInheritsWhenChained()
        {
            Assert.That(GameClient.CarryOverOnChainedRedirect(5, 9, chained: true), Is.EqualTo(5ul), "本段取到了就用本段的");
            Assert.That(GameClient.CarryOverOnChainedRedirect(0, 9, chained: true), Is.EqualTo(9ul), "链式且本段取不到:沿用上一段的");
            Assert.That(GameClient.CarryOverOnChainedRedirect(0, 9, chained: false), Is.EqualTo(0ul), "不是链式:旧值不算数");
        }

        [Test]
        public void ResumeClosesOldConnectionAndStartsRecoveryOnlyOnce()
        {
            _game.OnApplicationPause(false);
            Assert.That(_routines, Is.Empty, "Unity 启动时的 false 回调不是恢复事件");
            _game.OnApplicationPause(true); _game.OnApplicationPause(false); _game.OnApplicationPause(false);
            Assert.That(_gate.Connected, Is.False);
            Assert.That(_game.IsReconnecting, Is.True);
            Assert.That(_routines.Count, Is.EqualTo(1));
        }

        [Test]
        public void LoginFailureDoesNotAutomaticallyStartAnotherLogin()
        {
            SetProperty("InGame", false);
            PeerEof(); _game.Tick();
            Assert.That(_routines, Is.Empty);
            Assert.That(_disconnected, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator RecoveryUsesFreshTicketTokenLoginAndOriginalRoleOverRealTcp()
        {
            using var ready = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var freshListener = new TcpListener(IPAddress.Loopback, 0); freshListener.Start();
            int port = ((IPEndPoint)freshListener.LocalEndpoint).Port;
            Exception serverFailure = null;
            var logs = new List<string>();
            _game.OnLog += logs.Add;
            var codec = new MuduoCodec();
            codec.Register<ClientTokenVerifyRequest>(); codec.Register<ClientHeartbeatRequest>(); codec.Register<ClientRequest>();
            var server = new Thread(() =>
            {
                try
                {
                    using (var http = _gatewayListener.AcceptTcpClient())
                    {
                        http.GetStream().ReadTimeout = 3000;
                        using var reader = new StreamReader(http.GetStream(), Encoding.UTF8, false, 1024, true);
                        Assert.That(reader.ReadLine(), Does.StartWith("POST /api/assign-gate "));
                        int bodyLength = 0;
                        for (string line = reader.ReadLine(); !string.IsNullOrEmpty(line); line = reader.ReadLine())
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) bodyLength = int.Parse(line.Substring(15));
                        var body = new char[bodyLength];
                        for (int offset = 0; offset < body.Length;)
                        { int n = reader.Read(body, offset, body.Length - offset); if (n == 0) throw new EndOfStreamException(); offset += n; }
                        Assert.That(new string(body), Does.Contain("heartbeat-test"));
                        string json = "{\"code\":0,\"gate_ip\":\"127.0.0.1\",\"gate_port\":" + port +
                            ",\"token_payload\":\"AQID\",\"token_signature\":\"BAUG\"}";
                        byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " +
                            json.Length + "\r\nConnection: close\r\n\r\n" + json);
                        http.GetStream().Write(response, 0, response.Length);
                    }
                    using var fresh = freshListener.AcceptTcpClient();
                    var stream = fresh.GetStream(); stream.ReadTimeout = 5000;
                    var verify = (ClientTokenVerifyRequest)ReadFrame(stream, codec);
                    Assert.That(verify.Payload.ToByteArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
                    Assert.That(verify.Signature.ToByteArray(), Is.EqualTo(new byte[] { 4, 5, 6 }));
                    WriteFrame(stream, codec, new ClientTokenVerifyResponse { Success = true, HeartbeatIntervalMs = 50, HeartbeatTimeoutMs = 1000 });
                    ClientRequest ReadRequest()
                    {
                        while (true)
                        {
                            var message = ReadFrame(stream, codec);
                            if (message is ClientHeartbeatRequest hb) { WriteFrame(stream, codec, new ClientHeartbeatResponse { Seq = hb.Seq }); continue; }
                            return (ClientRequest)message;
                        }
                    }
                    var loginCall = ReadRequest();
                    Assert.That(loginCall.MessageId, Is.EqualTo(MessageIds.Login));
                    var login = Loginpb.LoginRequest.Parser.ParseFrom(loginCall.Body);
                    Assert.That(login.Account, Is.EqualTo("heartbeat-test"));
                    Assert.That(login.AuthType, Is.EqualTo("access_token"));
                    Assert.That(login.AuthToken, Is.EqualTo("test-access"));
                    Assert.That(login.Password, Is.Empty);
                    WriteFrame(stream, codec, new MessageContent { Id = loginCall.Id, MessageId = loginCall.MessageId,
                        SerializedMessage = new Loginpb.LoginResponse().ToByteString() });
                    var enterCall = ReadRequest();
                    Assert.That(enterCall.MessageId, Is.EqualTo(MessageIds.EnterGame));
                    Assert.That(Loginpb.EnterGameRequest.Parser.ParseFrom(enterCall.Body).PlayerId, Is.EqualTo(42ul));
                    WriteFrame(stream, codec, new MessageContent { Id = enterCall.Id, MessageId = enterCall.MessageId,
                        SerializedMessage = new Loginpb.EnterGameResponse { PlayerId = 42 }.ToByteString() });
                    WriteFrame(stream, codec, new MessageContent { MessageId = MessageIds.NotifyEnterScene,
                        SerializedMessage = new EnterSceneS2C { SceneInfo = new SceneInfoComp { SceneId = 123, SceneConfigId = 1 } }.ToByteString() });
                    ready.Set();
                    release.Wait(5000);
                }
                catch (Exception ex) { serverFailure = ex; ready.Set(); }
            }) { IsBackground = true };
            server.Start();
            try
            {
                _game.PlayerChooser = (_, __, ___) => throw new InvalidOperationException("恢复时不能重新选角");
                PeerEof(); _game.Tick();
                yield return PumpRecovery(_routines[0]);
                Assert.That(ready.Wait(1000), Is.True, string.Join("\n", logs));
                Assert.That(serverFailure, Is.Null);
                Assert.That(_game.IsReconnecting, Is.False);
                Assert.That(_game.InGame, Is.True);
                Assert.That(_game.PlayerId, Is.EqualTo(42ul));
                Assert.That(_game.CurrentSceneId, Is.EqualTo(123ul));
                Assert.That(_disconnected, Is.Zero);
            }
            finally { release.Set(); freshListener.Stop(); _gatewayListener.Stop(); Assert.That(server.Join(3000), Is.True); }
        }

        [UnityTest]
        public IEnumerator RecoveryRetriesPastAStoppedGateWithinTheBudget()
        {
            using var ready = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            // 先占住新 gate 的端口，再造一个已停的 gate 端口，两者不会撞号。
            var freshListener = new TcpListener(IPAddress.Loopback, 0); freshListener.Start();
            int freshPort = ((IPEndPoint)freshListener.LocalEndpoint).Port;
            var stopped = new TcpListener(IPAddress.Loopback, 0); stopped.Start();
            int stoppedPort = ((IPEndPoint)stopped.LocalEndpoint).Port;
            stopped.Stop();
            Exception serverFailure = null;
            var logs = new List<string>();
            _game.OnLog += logs.Add;
            var server = new Thread(() =>
            {
                try
                {
                    ServeAssignGate(stoppedPort); // gate 崩溃重启后，网关在旧注册过期前仍可能分到它
                    ServeAssignGate(freshPort);   // 退避后重新领票，分到新 gate
                    using var fresh = ServeEnteredGate(freshListener);
                    ready.Set();
                    release.Wait(5000);
                }
                catch (Exception ex) { serverFailure = ex; ready.Set(); }
            }) { IsBackground = true };
            server.Start();
            try
            {
                _game.PlayerChooser = (_, __, ___) => throw new InvalidOperationException("恢复时不能重新选角");
                PeerEof(); _game.Tick();
                yield return PumpRecovery(_routines[0]);
                Assert.That(ready.Wait(1000), Is.True, string.Join("\n", logs));
                Assert.That(serverFailure, Is.Null);
                Assert.That(_game.IsReconnecting, Is.False);
                Assert.That(_game.InGame, Is.True);
                Assert.That(_game.PlayerId, Is.EqualTo(42ul));
                Assert.That(_disconnected, Is.Zero, "期限内恢复成功，不能弹断线");
                Assert.That(logs.Exists(line => line.Contains("reconnect attempt 1 failed")), Is.True, string.Join("\n", logs));
            }
            finally { release.Set(); freshListener.Stop(); _gatewayListener.Stop(); Assert.That(server.Join(3000), Is.True); }
        }

        [UnityTest]
        public IEnumerator DefinitiveAssignGateResultEndsRecoveryWithoutRetry()
        {
            Exception serverFailure = null;
            var server = new Thread(() =>
            {
                try { ServeAssignGate(1, AssignGateResult.CodeQueueing); }
                catch (Exception ex) { serverFailure = ex; }
            }) { IsBackground = true };
            server.Start();
            PeerEof(); _game.Tick();
            yield return PumpRecovery(_routines[0]);
            Assert.That(server.Join(3000), Is.True);
            Assert.That(serverFailure, Is.Null);
            Assert.That(_game.IsReconnecting, Is.False);
            Assert.That(_disconnected, Is.EqualTo(1));
            Assert.That(_game.DisconnectReason, Is.EqualTo("重新连接失败，请重新进入游戏"));
            Assert.That(_gatewayListener.Pending(), Is.False, "排队是确定结论：恢复流程不能替玩家排队，也不再领第二张票");
        }

        /// <summary>假网关:收一次 assign-gate,回指定 gate 端口与结果码。</summary>
        private void ServeAssignGate(int gatePort, int code = AssignGateResult.CodeOk)
        {
            using var http = _gatewayListener.AcceptTcpClient();
            http.GetStream().ReadTimeout = 5000;
            using var reader = new StreamReader(http.GetStream(), Encoding.UTF8, false, 1024, true);
            Assert.That(reader.ReadLine(), Does.StartWith("POST /api/assign-gate "));
            int bodyLength = 0;
            for (string line = reader.ReadLine(); !string.IsNullOrEmpty(line); line = reader.ReadLine())
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) bodyLength = int.Parse(line.Substring(15));
            var body = new char[bodyLength];
            for (int offset = 0; offset < body.Length;)
            { int n = reader.Read(body, offset, body.Length - offset); if (n == 0) throw new EndOfStreamException(); offset += n; }
            Assert.That(new string(body), Does.Contain("heartbeat-test"));
            string json = "{\"code\":" + code + ",\"gate_ip\":\"127.0.0.1\",\"gate_port\":" + gatePort +
                ",\"token_payload\":\"AQID\",\"token_signature\":\"BAUG\"}";
            byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " +
                json.Length + "\r\nConnection: close\r\n\r\n" + json);
            http.GetStream().Write(response, 0, response.Length);
        }

        /// <summary>
        /// 假新 gate:验票、令牌登录、按原角色进场并推入场。返回的连接由调用方持有到断言结束,
        /// 提前关掉会被客户端当成又一次断线。
        /// </summary>
        private static TcpClient ServeEnteredGate(TcpListener listener)
        {
            var codec = new MuduoCodec();
            codec.Register<ClientTokenVerifyRequest>(); codec.Register<ClientHeartbeatRequest>(); codec.Register<ClientRequest>();
            var fresh = listener.AcceptTcpClient();
            var stream = fresh.GetStream(); stream.ReadTimeout = 5000;
            var verify = (ClientTokenVerifyRequest)ReadFrame(stream, codec);
            Assert.That(verify.Payload.ToByteArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            WriteFrame(stream, codec, new ClientTokenVerifyResponse { Success = true, HeartbeatIntervalMs = 50, HeartbeatTimeoutMs = 1000 });
            ClientRequest ReadRequest()
            {
                while (true)
                {
                    var message = ReadFrame(stream, codec);
                    if (message is ClientHeartbeatRequest hb) { WriteFrame(stream, codec, new ClientHeartbeatResponse { Seq = hb.Seq }); continue; }
                    return (ClientRequest)message;
                }
            }
            var loginCall = ReadRequest();
            Assert.That(loginCall.MessageId, Is.EqualTo(MessageIds.Login));
            Assert.That(Loginpb.LoginRequest.Parser.ParseFrom(loginCall.Body).AuthType, Is.EqualTo("access_token"));
            WriteFrame(stream, codec, new MessageContent { Id = loginCall.Id, MessageId = loginCall.MessageId,
                SerializedMessage = new Loginpb.LoginResponse().ToByteString() });
            var enterCall = ReadRequest();
            Assert.That(enterCall.MessageId, Is.EqualTo(MessageIds.EnterGame));
            Assert.That(Loginpb.EnterGameRequest.Parser.ParseFrom(enterCall.Body).PlayerId, Is.EqualTo(42ul));
            WriteFrame(stream, codec, new MessageContent { Id = enterCall.Id, MessageId = enterCall.MessageId,
                SerializedMessage = new Loginpb.EnterGameResponse { PlayerId = 42 }.ToByteString() });
            WriteFrame(stream, codec, new MessageContent { MessageId = MessageIds.NotifyEnterScene,
                SerializedMessage = new EnterSceneS2C { SceneInfo = new SceneInfoComp { SceneId = 123, SceneConfigId = 1 } }.ToByteString() });
            return fresh;
        }

        private IEnumerator PumpRecovery(IEnumerator routine)
        {
            // EditMode 不替 YieldInstruction 等待异步 HTTP；按生产协程语义显式等待 isDone。
            var stack = new Stack<IEnumerator>(); stack.Push(routine);
            try
            {
                while (stack.Count != 0)
                {
                    var next = stack.Peek();
                    if (!next.MoveNext()) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (next.Current is IEnumerator nested) { stack.Push(nested); continue; }
                    if (next.Current is UnityEngine.AsyncOperation operation)
                        while (!operation.isDone) { _game.Tick(); yield return null; }
                    else { _game.Tick(); yield return next.Current; }
                }
            }
            finally { while (stack.Count != 0) (stack.Pop() as IDisposable)?.Dispose(); }
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

        /// <summary>
        /// 一条指向本机 <paramref name="port"/> 的 msg 124。票据留空:客户端只原样转发票据,解不出区号时沿用旧值。
        /// 用例只推进到探测目标 gate 之前,这个端口不会真的被连接。
        /// </summary>
        private static MessageContent RedirectFrame(int port) => new MessageContent
        {
            MessageId = MessageIds.RedirectToGate,
            SerializedMessage = new RedirectToGateNotify { TargetIp = "127.0.0.1", TargetPort = (uint)port }.ToByteString()
        };

        private void PeerEof()
        {
            _peer.Client.Shutdown(SocketShutdown.Send);
            var reader = (Thread)typeof(GateTcpClient).GetField("_readerThread", Hidden).GetValue(_gate);
            Assert.That(reader.Join(3000), Is.True);
        }
        private object Get(string field) => typeof(GameClient).GetField(field, Hidden).GetValue(_game);
        private void Set(string field, object value) => typeof(GameClient).GetField(field, Hidden).SetValue(_game, value);
        private void SetProperty(string property, object value) => Set("<" + property + ">k__BackingField", value);
        private void Invoke(string method, params object[] args) => typeof(GameClient).GetMethod(method, Hidden).Invoke(_game, args);
    }
}
