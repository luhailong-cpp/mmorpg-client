using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using MmorpgClient.Game;
using MmorpgClient.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MmorpgClient.Tests.EditMode.Net
{
    /// <summary>
    /// <see cref="GameClient.EnterZone"/> 的排队状态机端到端 EditMode 测试
    /// (服务端 docs/design/open-server-rate-limit-design.md §11.5 第 1~7 条与无票 429 / 续期契约)。
    ///
    /// <para>走真实的 <see cref="GatewayHttpClient"/>(<c>UnityWebRequest</c>) 与本机回环假网关;
    /// 只通过私有测试接缝替换排队等待与单调时钟,不睡墙钟,同时保留一项真实等待的中断回归用例。</para>
    /// </summary>
    public sealed class LoginQueueFlowTests
    {
        private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly LoginCredential TestCred = LoginCredential.ForPassword("queue_user", "pw");

        private GameClient _game;
        private TcpListener _gateway;
        private Thread _serverThread;
        private volatile bool _stopping;
        private Exception _serverError;
        private readonly List<CapturedRequest> _requests = new();
        private readonly List<long> _waitsMs = new();
        private readonly List<string> _statuses = new();
        private readonly object _clockLock = new();
        private double _fakeNow;
        private string _admittedGate;
        private string _error;

        private sealed class CapturedRequest
        {
            public string Path;
            public string Body;
        }

        private sealed class HttpReply
        {
            public int StatusCode = 200;
            public string Reason = "OK";
            public string Body = "{}";
            public bool DropConnection;
            public Action OnServed;

            public static HttpReply Json(string json, Action onServed = null) =>
                new HttpReply { StatusCode = 200, Reason = "OK", Body = json, OnServed = onServed };

            public static HttpReply HttpError(int status, string reason, string body = "", Action onServed = null) =>
                new HttpReply { StatusCode = status, Reason = reason, Body = body, OnServed = onServed };

            public static HttpReply Drop(Action onServed = null) =>
                new HttpReply { DropConnection = true, OnServed = onServed };
        }

        [SetUp]
        public void SetUp()
        {
            _stopping = false;
            _serverError = null;
            _requests.Clear();
            _waitsMs.Clear();
            _statuses.Clear();
            _fakeNow = 1000.0;
            _admittedGate = null;
            _error = null;

            _gateway = new TcpListener(IPAddress.Loopback, 0);
            _gateway.Start();
            int port = ((IPEndPoint)_gateway.LocalEndpoint).Port;
            _game = new GameClient($"http://127.0.0.1:{port}");
            _game.OnStatus += s => _statuses.Add(s);
            _game.OnLog += msg =>
            {
                // 放行后 EnterZone 紧接着进 ConnectAndEnter;收到放行日志立刻翻世代停下,不触碰真实 gate TCP。
                if (msg.StartsWith("assigned gate ", StringComparison.Ordinal))
                {
                    _admittedGate = msg.Substring("assigned gate ".Length);
                    _game.Disconnect();
                }
            };
            InstallFakeQueueClockAndWait();
        }

        [TearDown]
        public void TearDown()
        {
            _stopping = true;
            _game?.Disconnect();
            _gateway?.Stop();
            if (_serverThread != null)
            {
                Assert.That(_serverThread.Join(3000), Is.True, "假网关线程应在停止监听后及时退出");
                _serverThread = null;
            }
            if (_game?.World.Root != null)
                UnityEngine.Object.DestroyImmediate(_game.World.Root.gameObject);
        }

        /// <summary>
        /// §11.5 第 2、3、4、6 条:持票查询连续失败时带原票重入;重入断线 / HTTP 502 / 业务 500 / 429 / 100 ratelimit
        /// 都保票并按 2、4、8、16、30、30 秒退避;查询恢复后退避回 2 秒并正常放行。
        /// </summary>
        [UnityTest]
        public IEnumerator Rule6_ReentryFailuresKeepTokenBackOffAndAdmitAfterRecovery()
        {
            StartGateway(
                LoginOkReply(),
                // 首次分配入队,领到 T1
                HttpReply.Json("{\"code\":100,\"queue_source\":\"login\",\"queue_token\":\"T1\",\"queue_rank\":10,\"queue_total\":20,\"retry_after_ms\":2000}"),
                // 失败 1(下限 2s):查询断线 → 重入断线
                HttpReply.Drop(),
                HttpReply.Drop(),
                // 失败 2(下限 4s):查询 502 → 重入 502
                HttpReply.HttpError(502, "Bad Gateway", "bad gateway"),
                HttpReply.HttpError(502, "Bad Gateway", "bad gateway"),
                // 失败 3(下限 8s):查询 500 → 重入 500
                HttpReply.Json("{\"code\":500,\"error\":\"internal\"}"),
                HttpReply.Json("{\"code\":500,\"error\":\"internal\"}"),
                // 失败 4(下限 16s,第 4 条):查询 502 → 重入 429(不得套无票 30s 退避)
                HttpReply.HttpError(502, "Bad Gateway"),
                HttpReply.Json("{\"code\":429,\"error\":\"IP_RATE_LIMIT\"}"),
                // 失败 5(下限 30s):查询 502 → 重入 100 ratelimit(不得把退避清零)
                HttpReply.HttpError(502, "Bad Gateway"),
                HttpReply.Json("{\"code\":100,\"queue_source\":\"ratelimit\",\"retry_after_ms\":2000}"),
                // 失败 6(封顶 30s):查询 502 → 重入 502
                HttpReply.HttpError(502, "Bad Gateway"),
                HttpReply.HttpError(502, "Bad Gateway"),
                // 查询恢复:回 100 login → 连续失败清零,等待回到 2s 档
                HttpReply.Json("{\"code\":100,\"queue_source\":\"login\",\"queue_token\":\"T1\",\"queue_rank\":1,\"queue_total\":20,\"retry_after_ms\":2000}"),
                // 放行
                AdmittedReply(42000));

            yield return RunEnterZone();

            Assert.That(_serverError, Is.Null);
            Assert.That(_error, Is.Null);
            Assert.That(_admittedGate, Is.EqualTo("127.0.0.1:42000"));

            // 验证每一次查询与恢复重入都带着原票 T1
            Assert.That(_requests.Count, Is.EqualTo(16));
            Assert.That(_requests[0].Path, Is.EqualTo("/api/login"));
            Assert.That(_requests[1].Path, Is.EqualTo("/api/assign-gate"));
            Assert.That(JsonUtility.FromJson<AssignGateRequest>(_requests[1].Body).queue_token, Is.Empty);
            for (int i = 0; i < 6; i++)
            {
                var statusReq = _requests[2 + i * 2];
                var reentryReq = _requests[3 + i * 2];
                Assert.That(statusReq.Path, Is.EqualTo("/api/queue-status"), $"failure #{i + 1} status");
                Assert.That(JsonUtility.FromJson<QueueStatusRequest>(statusReq.Body).queue_token, Is.EqualTo("T1"));
                Assert.That(reentryReq.Path, Is.EqualTo("/api/assign-gate"), $"failure #{i + 1} reentry");
                Assert.That(JsonUtility.FromJson<AssignGateRequest>(reentryReq.Body).queue_token, Is.EqualTo("T1"));
            }
            Assert.That(_requests[14].Path, Is.EqualTo("/api/queue-status"));
            Assert.That(_requests[15].Path, Is.EqualTo("/api/queue-status"));

            // 等待序列:首次入队 2s → 连续失败 1..6 次(2, 4, 8, 16, 30, 30s) → 恢复后回 2s
            long[] expectedFloors = { 2000, 2000, 4000, 8000, 16000, 30000, 30000, 2000 };
            Assert.That(_waitsMs.Count, Is.EqualTo(expectedFloors.Length));
            for (int i = 0; i < expectedFloors.Length; i++)
                AssertWaitInJitterRange(_waitsMs[i], expectedFloors[i], $"wait[{i}]");
        }

        /// <summary>§11.5 第 5 条:queue-status 回 503 zone_maintenance 为终态,直接结束、不再调 assign-gate 重入。</summary>
        [UnityTest]
        public IEnumerator Rule5_QueueStatus503ZoneMaintenance_FailsImmediatelyWithoutReentry()
        {
            StartGateway(
                LoginOkReply(),
                HttpReply.Json("{\"code\":100,\"queue_source\":\"login\",\"queue_token\":\"T1\",\"queue_rank\":2,\"queue_total\":5,\"retry_after_ms\":2000}"),
                HttpReply.Json("{\"code\":503,\"error\":\"zone_maintenance\"}"));

            yield return RunEnterZone();

            Assert.That(_serverError, Is.Null);
            Assert.That(_admittedGate, Is.Null);
            Assert.That(_error, Is.Not.Null.And.Not.Empty);
            Assert.That(_requests.Count, Is.EqualTo(3), "503 终态不得触发恢复重入");
            Assert.That(_requests[2].Path, Is.EqualTo("/api/queue-status"));
        }

        /// <summary>§11.5 第 5 条:queue-status 回 404 zone_not_found 为终态,直接结束、不再调 assign-gate 重入。</summary>
        [UnityTest]
        public IEnumerator Rule5_QueueStatus404ZoneNotFound_FailsImmediatelyWithoutReentry()
        {
            StartGateway(
                LoginOkReply(),
                HttpReply.Json("{\"code\":100,\"queue_source\":\"login\",\"queue_token\":\"T1\",\"queue_rank\":2,\"queue_total\":5,\"retry_after_ms\":2000}"),
                HttpReply.Json("{\"code\":404,\"error\":\"zone_not_found\"}"));

            yield return RunEnterZone();

            Assert.That(_serverError, Is.Null);
            Assert.That(_admittedGate, Is.Null);
            Assert.That(_error, Is.Not.Null.And.Not.Empty);
            Assert.That(_requests.Count, Is.EqualTo(3), "404 终态不得触发恢复重入");
        }

        /// <summary>持票 410 丢票清退避并立刻无票重调;无票 410 直接报错退出,不紧循环。</summary>
        [UnityTest]
        public IEnumerator QueueExpired410_DropsTokenImmediatelyAndTokenless410Stops()
        {
            StartGateway(
                LoginOkReply(),
                HttpReply.Json("{\"code\":100,\"queue_source\":\"login\",\"queue_token\":\"T1\",\"queue_rank\":3,\"queue_total\":9,\"retry_after_ms\":2000}"),
                HttpReply.HttpError(502, "Bad Gateway"),
                // 恢复重入回 410:丢票并立刻无票重调(不额外等一轮)
                HttpReply.Json("{\"code\":410,\"error\":\"queue_expired\"}"),
                // 无票仍回 410:直接交外层报错,不空转
                HttpReply.Json("{\"code\":410,\"error\":\"queue_expired\"}"));

            yield return RunEnterZone();

            Assert.That(_serverError, Is.Null);
            Assert.That(_admittedGate, Is.Null);
            Assert.That(_error, Is.Not.Null.And.Not.Empty);
            Assert.That(_requests.Count, Is.EqualTo(5));
            Assert.That(JsonUtility.FromJson<AssignGateRequest>(_requests[3].Body).queue_token, Is.EqualTo("T1"));
            Assert.That(_requests[4].Path, Is.EqualTo("/api/assign-gate"));
            Assert.That(JsonUtility.FromJson<AssignGateRequest>(_requests[4].Body).queue_token, Is.Empty, "410 丢票后应无票重调");
            Assert.That(_waitsMs.Count, Is.EqualTo(1), "410 丢票后立刻重调,只发生首次入队的那一次等待");
        }

        /// <summary>§11.5「未持有 queue_token」:无票 429 依次退避 30、60、120 秒,第 4 次报错退出。</summary>
        [UnityTest]
        public IEnumerator Tokenless429_BacksOff30_60_120SecondsAndFailsOnFourth()
        {
            StartGateway(
                LoginOkReply(),
                HttpReply.Json("{\"code\":429,\"error\":\"IP_RATE_LIMIT\"}"),
                HttpReply.Json("{\"code\":429,\"error\":\"IP_RATE_LIMIT\"}"),
                HttpReply.Json("{\"code\":429,\"error\":\"IP_RATE_LIMIT\"}"),
                HttpReply.Json("{\"code\":429,\"error\":\"IP_RATE_LIMIT\"}"));

            yield return RunEnterZone();

            Assert.That(_serverError, Is.Null);
            Assert.That(_admittedGate, Is.Null);
            Assert.That(_error, Is.Not.Null.And.Not.Empty);
            Assert.That(_requests.Count, Is.EqualTo(5));
            Assert.That(_waitsMs.Count, Is.EqualTo(3));
            AssertWaitInJitterRange(_waitsMs[0], 30000, "429 retry #1");
            AssertWaitInJitterRange(_waitsMs[1], 60000, "429 retry #2");
            AssertWaitInJitterRange(_waitsMs[2], 120000, "429 retry #3");
        }

        /// <summary>§11.5 第 7 条:查询失败瞬间已到总预算时,不再发起恢复重入,直接报排队超时。</summary>
        [UnityTest]
        public IEnumerator Rule7_BudgetExhaustedDuringQueueStatusStopsBeforeReentry()
        {
            StartGateway(
                LoginOkReply(),
                HttpReply.Json("{\"code\":100,\"queue_source\":\"login\",\"queue_token\":\"T1\",\"queue_rank\":1,\"queue_total\":2,\"retry_after_ms\":2000}"),
                HttpReply.HttpError(502, "Bad Gateway", onServed: () => AdvanceClock(24 * 60 * 60 + 1)));

            yield return RunEnterZone();

            Assert.That(_serverError, Is.Null);
            Assert.That(_admittedGate, Is.Null);
            Assert.That(_error, Is.EqualTo("排队超时,请重新登录"));
            Assert.That(_requests.Count, Is.EqualTo(3), "跨过总预算后不得再发恢复重入请求");
        }

        /// <summary>§11.5 第 7 条:排队内续期耗时跨过总预算时,续期返回后不得再发 assign-gate。</summary>
        [UnityTest]
        public IEnumerator Rule7_BudgetExhaustedDuringRefreshStopsBeforeAssignGate()
        {
            long expiringSoon = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60;
            long farFuture = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 7200;
            StartGateway(
                LoginOkReply(accessExpire: expiringSoon),
                HttpReply.Json(
                    "{\"code\":0,\"access_token\":\"at-2\",\"refresh_token\":\"rt-2\",\"access_token_expire\":" + farFuture + "}",
                    onServed: () => AdvanceClock(24 * 60 * 60 + 1)));

            yield return RunEnterZone();

            Assert.That(_serverError, Is.Null);
            Assert.That(_admittedGate, Is.Null);
            Assert.That(_error, Is.EqualTo("排队超时,请重新登录"));
            Assert.That(_requests.Count, Is.EqualTo(2));
            Assert.That(_requests[1].Path, Is.EqualTo("/api/refresh-token"));
        }

        /// <summary>排队中续期遇到 429(IP 限流)只隔 60s 再试、不中断排队;续期成功后后续请求带新 access_token 并放行。</summary>
        [UnityTest]
        public IEnumerator QueueRefreshToken_Transient429RetriesAfter60sThenUsesNewToken()
        {
            long expiringSoon = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60;
            long farFuture = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 7200;
            StartGateway(
                LoginOkReply(accessExpire: expiringSoon),
                // 第 1 次续期被 IP 桶 429:不中止排队,按旧 token 照常 assign-gate 入队
                HttpReply.Json("{\"code\":429,\"message\":\"IP_RATE_LIMIT\"}"),
                HttpReply.Json("{\"code\":100,\"queue_source\":\"login\",\"queue_token\":\"T1\",\"queue_rank\":1,\"queue_total\":2,\"retry_after_ms\":60000}"),
                // 过了 60s 冷却,第 2 次续期成功换到 at-2
                HttpReply.Json("{\"code\":0,\"access_token\":\"at-2\",\"refresh_token\":\"rt-2\",\"access_token_expire\":" + farFuture + "}"),
                // 查询失败触发恢复重入:核对重入带着刚换出的 at-2
                HttpReply.HttpError(502, "Bad Gateway"),
                AdmittedReply(42001));

            yield return RunEnterZone();

            Assert.That(_serverError, Is.Null);
            Assert.That(_error, Is.Null);
            Assert.That(_admittedGate, Is.EqualTo("127.0.0.1:42001"));
            Assert.That(_requests.Count, Is.EqualTo(6));
            Assert.That(_requests[1].Path, Is.EqualTo("/api/refresh-token"));
            Assert.That(_requests[3].Path, Is.EqualTo("/api/refresh-token"));
            var reentry = JsonUtility.FromJson<AssignGateRequest>(_requests[5].Body);
            Assert.That(reentry.access_token, Is.EqualTo("at-2"), "续期成功后的恢复重入必须带新 access_token");
            Assert.That(reentry.queue_token, Is.EqualTo("T1"));
        }

        /// <summary>排队中续期收到 401 立刻报错「登录已失效,请重新登录」,不再请求 assign-gate。</summary>
        [UnityTest]
        public IEnumerator QueueRefreshToken_401FailsWithLoginExpired()
        {
            long expiringSoon = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60;
            StartGateway(
                LoginOkReply(accessExpire: expiringSoon),
                HttpReply.Json("{\"code\":401,\"message\":\"refresh token expired\"}"));

            yield return RunEnterZone();

            Assert.That(_serverError, Is.Null);
            Assert.That(_admittedGate, Is.Null);
            Assert.That(_error, Is.EqualTo("登录已失效,请重新登录"));
            Assert.That(_requests.Count, Is.EqualTo(2));
            Assert.That(_requests[1].Path, Is.EqualTo("/api/refresh-token"));
        }

        /// <summary>§11.5 第 3 条:HTTP 500 缺省 JSON(无 code 字段)按 HTTP 错误处理,绝不当 code=0 放行。</summary>
        [UnityTest]
        public IEnumerator Non200SpringErrorJsonWithoutCode_IsTreatedAsErrorNeverAdmitted()
        {
            StartGateway(
                LoginOkReply(),
                HttpReply.HttpError(500, "Internal Server Error",
                    "{\"timestamp\":\"2026-11-20T00:00:00Z\",\"status\":500,\"error\":\"Internal Server Error\"}"));

            yield return RunEnterZone();

            Assert.That(_serverError, Is.Null);
            Assert.That(_admittedGate, Is.Null, "无 code 字段的 Spring 错误页绝不能被 JsonUtility 读成 code=0 放行");
            Assert.That(_error, Does.StartWith("POST /api/assign-gate failed:"));
        }

        /// <summary>HTTP 204(UnityWebRequest.result == Success 但 responseCode != 200 且 req.error 可能为空串)必须报 HTTP 204。</summary>
        [UnityTest]
        public IEnumerator Http204NonSuccessStatus_ReportsHttpStatusCode()
        {
            StartGateway(LoginOkReply(), HttpReply.HttpError(204, "No Content", ""));

            yield return RunEnterZone();

            Assert.That(_serverError, Is.Null);
            Assert.That(_admittedGate, Is.Null);
            Assert.That(_error, Does.Contain("HTTP 204"));
        }

        /// <summary>真实 WaitQueueMs(不注入等待替换)在 30s 退避期间被 Disconnect 翻世代,应在下几帧内立刻退出。</summary>
        [UnityTest]
        public IEnumerator RealWaitQueueMs_ExitsImmediatelyWhenDisconnected()
        {
            // 关闭测试等待替换,走生产 WaitQueueMs 协程循环
            Set("_queueWaitOverride", null);
            Set("_queueClockOverride", null);
            StartGateway(
                LoginOkReply(),
                HttpReply.Json("{\"code\":429,\"error\":\"IP_RATE_LIMIT\"}"));

            var routine = _game.EnterZone(1u, TestCred, "dev-1", () => { }, e => _error = e);
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            bool cancelled = false;
            int framesAfterCancel = 0;
            try
            {
                while (stack.Count != 0)
                {
                    var top = stack.Peek();
                    if (!top.MoveNext())
                    {
                        (stack.Pop() as IDisposable)?.Dispose();
                        continue;
                    }
                    if (top.Current is IEnumerator nested)
                    {
                        stack.Push(nested);
                        continue;
                    }
                    if (top.Current is AsyncOperation op)
                    {
                        while (!op.isDone) yield return null;
                    }
                    else
                    {
                        yield return top.Current;
                    }
                    if (!cancelled && _statuses.Exists(s => s.Contains("操作过于频繁")))
                    {
                        cancelled = true;
                        _game.Disconnect();
                    }
                    else if (cancelled)
                    {
                        framesAfterCancel++;
                        Assert.That(framesAfterCancel, Is.LessThan(5), "Disconnect 翻世代后 WaitQueueMs 应立即退出,不得等满 30 秒");
                    }
                }
            }
            finally
            {
                while (stack.Count != 0) (stack.Pop() as IDisposable)?.Dispose();
            }

            Assert.That(cancelled, Is.True);
            Assert.That(_error, Is.Null);
            Assert.That(_serverError, Is.Null);
        }

        private void InstallFakeQueueClockAndWait()
        {
            Set("_queueClockOverride", (Func<double>)ReadClock);
            Set("_queueWaitOverride", (Func<long, IEnumerator>)(ms =>
            {
                _waitsMs.Add(ms);
                AdvanceClock(ms / 1000.0);
                return EmptyRoutine();
            }));
        }

        private double ReadClock()
        {
            lock (_clockLock) { return _fakeNow; }
        }

        private void AdvanceClock(double deltaSeconds)
        {
            lock (_clockLock) { _fakeNow += deltaSeconds; }
        }

        private static IEnumerator EmptyRoutine() { yield break; }

        private static HttpReply LoginOkReply(long accessExpire = 0)
        {
            long exp = accessExpire != 0 ? accessExpire : DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 7200;
            return HttpReply.Json(
                "{\"code\":0,\"access_token\":\"at-1\",\"refresh_token\":\"rt-1\",\"access_token_expire\":" + exp + "}");
        }

        private static HttpReply AdmittedReply(int gatePort) =>
            HttpReply.Json(
                "{\"code\":0,\"gate_ip\":\"127.0.0.1\",\"gate_port\":" + gatePort +
                ",\"token_payload\":\"AQID\",\"token_signature\":\"BAUG\"}");

        private static void AssertWaitInJitterRange(long actualMs, long floorMs, string label)
        {
            long maxJitter = Math.Min(floorMs, 30000L) / 5;
            Assert.That(actualMs, Is.InRange(floorMs, floorMs + maxJitter), label);
        }

        private IEnumerator RunEnterZone()
        {
            var routine = _game.EnterZone(1u, TestCred, "dev-1", () => { }, e => _error = e);
            yield return PumpRoutine(routine);
        }

        private static IEnumerator PumpRoutine(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            try
            {
                while (stack.Count != 0)
                {
                    var top = stack.Peek();
                    if (!top.MoveNext())
                    {
                        (stack.Pop() as IDisposable)?.Dispose();
                        continue;
                    }
                    if (top.Current is IEnumerator nested)
                    {
                        stack.Push(nested);
                        continue;
                    }
                    if (top.Current is AsyncOperation op)
                    {
                        while (!op.isDone) yield return null;
                    }
                    else
                    {
                        yield return top.Current;
                    }
                }
            }
            finally
            {
                while (stack.Count != 0) (stack.Pop() as IDisposable)?.Dispose();
            }
        }

        private void StartGateway(params HttpReply[] replies)
        {
            var queue = new Queue<HttpReply>(replies);
            _serverThread = new Thread(() =>
            {
                try
                {
                    while (!_stopping && queue.Count > 0)
                    {
                        TcpClient client;
                        try { client = _gateway.AcceptTcpClient(); }
                        catch (SocketException) when (_stopping) { break; }
                        catch (ObjectDisposedException) when (_stopping) { break; }

                        using (client)
                        {
                            var stream = client.GetStream();
                            stream.ReadTimeout = 5000;
                            var captured = ReadHttpRequest(stream);
                            lock (_requests) { _requests.Add(captured); }

                            var reply = queue.Dequeue();
                            reply.OnServed?.Invoke();
                            if (reply.DropConnection)
                                continue; // 直接关闭 socket,触发 UnityWebRequest 网络错误

                            byte[] bodyBytes = Encoding.UTF8.GetBytes(reply.Body ?? string.Empty);
                            string header =
                                $"HTTP/1.1 {reply.StatusCode} {reply.Reason}\r\n" +
                                $"Content-Type: application/json\r\n" +
                                $"Content-Length: {bodyBytes.Length}\r\n" +
                                "Connection: close\r\n\r\n";
                            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
                            stream.Write(headerBytes, 0, headerBytes.Length);
                            if (bodyBytes.Length > 0)
                                stream.Write(bodyBytes, 0, bodyBytes.Length);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!_stopping) _serverError = ex;
                }
            }) { IsBackground = true };
            _serverThread.Start();
        }

        private static CapturedRequest ReadHttpRequest(NetworkStream stream)
        {
            var raw = new MemoryStream();
            var buf = new byte[1024];
            int headerEnd = -1;
            while (headerEnd < 0)
            {
                int n = stream.Read(buf, 0, buf.Length);
                if (n <= 0) throw new EndOfStreamException("client closed before HTTP headers");
                raw.Write(buf, 0, n);
                headerEnd = FindHeaderEnd(raw.GetBuffer(), (int)raw.Length);
            }

            string headerText = Encoding.ASCII.GetString(raw.GetBuffer(), 0, headerEnd);
            string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] parts = lines[0].Split(' ');
            string path = parts.Length >= 2 ? parts[1] : string.Empty;
            int contentLength = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    contentLength = int.Parse(lines[i].Substring("Content-Length:".Length).Trim());
            }

            int bodyStart = headerEnd + 4;
            int alreadyRead = (int)raw.Length - bodyStart;
            var body = new byte[contentLength];
            if (alreadyRead > 0)
                Buffer.BlockCopy(raw.GetBuffer(), bodyStart, body, 0, Math.Min(alreadyRead, contentLength));
            for (int offset = alreadyRead; offset < contentLength;)
            {
                int n = stream.Read(body, offset, contentLength - offset);
                if (n <= 0) throw new EndOfStreamException("client closed before HTTP body");
                offset += n;
            }
            return new CapturedRequest { Path = path, Body = Encoding.UTF8.GetString(body) };
        }

        private static int FindHeaderEnd(byte[] data, int length)
        {
            for (int i = 0; i <= length - 4; i++)
            {
                if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n')
                    return i;
            }
            return -1;
        }

        private void Set(string field, object value) =>
            typeof(GameClient).GetField(field, Hidden).SetValue(_game, value);
    }
}
