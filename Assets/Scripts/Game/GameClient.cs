using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Google.Protobuf;
using MmorpgClient.Game.Attribute;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Pet;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.World;
using UnityEngine;

// Top-level proto types from .proto files that declare no `package` are
// emitted into the global namespace by protoc. Loginpb.* lives in the
// `Loginpb` namespace because login.proto declares `package loginpb;`.
using Loginpb;

namespace MmorpgClient.Game
{
    /// <summary>
    /// High-level facade that drives the login -> enter scene -> cast skill flow.
    /// Owns the HTTP gateway client and the persistent gate TCP connection.
    /// All callbacks fire on the Unity main thread (driven by <see cref="Tick"/>).
    ///
    /// Multi-node aware: which zone/gate/login instance serves this session is
    /// decided per-EnterZone by the gateway + login services; nothing here pins
    /// a fixed node. Server-pushed RedirectToGateNotify (cross-zone / gate
    /// migration) is handled transparently by reconnecting to the new gate.
    /// </summary>
    public sealed class GameClient
    {
        private readonly GatewayHttpClient _http;
        private readonly MuduoCodec _codec;
        private GateTcpClient _gate;

        private long _seq;

        private sealed class PendingCall
        {
            public uint MessageId;
            public Action<MessageContent> Callback;
        }

        // gRPC-backed replies (Login/CreatePlayer/EnterGame/...) come back with
        // MessageContent.id == 0 because the C++ gate's SetIfEmptyHandler only
        // fills serialized_message + message_id (cpp/nodes/gate/main.cpp).
        // Scene replies do echo the id. So matching is two-tier: exact id
        // first, then FIFO by message_id (same strategy as the Go robot).
        private readonly Dictionary<ulong, PendingCall> _pending = new();
        private readonly Dictionary<uint, List<ulong>> _pendingIdsByMsg = new();

        private readonly Dictionary<uint, Action<MessageContent>> _notifyHandlers = new();

        private long _accessTokenExpire;       // unix seconds
        private float _lastRefreshAttempt;     // realtimeSinceStartup
        private bool _enteredScene;            // set by NotifyEnterScene
        private bool _redirecting;             // RedirectToGateNotify flow active
        private ulong _redirectPlayerId;       // 重定向前的角色 id(重连后沿用,不重新选角)
        private int _redirectHops;             // 本会话已跟随的重定向次数(环路熔断,EnterZone 归零)
        private bool _disconnectNotificationSent = true;
        private bool _disconnectInProgress;

        // 管线世代:每次 EnterZone / RedirectFlow 启动时 +1。旧管线协程在每个
        // yield 恢复点比对世代,不一致立即退出(不触发任何回调),防止用户
        // 重复点「进入」或重定向并发时,多条管线互踩共享的 _gate/_pending/
        // TokenVerified/_enteredScene 状态。
        private int _pipelineGen;

        public ulong PlayerId { get; private set; }
        public string AccessToken { get; private set; }
        public string RefreshToken { get; private set; }
        public ActorWorld World { get; }
        public bool TokenVerified { get; private set; }
        public bool InGame { get; private set; }
        public ulong CurrentSceneId { get; private set; }
        public uint CurrentSceneConfigId { get; private set; }

        /// <summary>
        /// 当前 gate 连接地址 "ip:port"(assign-gate 分配或 RedirectToGateNotify 重定向后更新;
        /// 未连过为 null)。跨区验证用它当"落区证据":本地部署里各 zone 的 gate 端口不同,
        /// 两实例地址相同即说明其实进了同一个 zone(对齐服务端 robot 的 zone-placement 断言)。
        /// </summary>
        public string AssignedGate { get; private set; }

        /// <summary>
        /// 回合制战斗网络层(状态机 + battle/match 消息收发)。构造时经
        /// <see cref="GameClientBattleTransport"/> 挂到本管线(OnNotify/Call/SendOneWay),
        /// UI 路经 BattleClient.Instance 解析。
        /// </summary>
        public BattleClient Battle { get; }

        /// <summary>
        /// 观战网络层(与 Battle 平行的只读状态机,消息号不同互不干扰)。
        /// 各持一个 GameClientBattleTransport 实例:该传输无状态(纯转发本管线),
        /// 分持只为事件订阅/注册边界清晰,底层仍是同一条 gate 连接。
        /// </summary>
        public SpectateClient Spectate { get; }

        /// <summary>
        /// 角色属性加点网络层(docs/design/player-attribute-allocation.md)。与 Battle/Spectate
        /// 平行:各持一个无状态 GameClientBattleTransport,底层仍是同一条 gate 连接。
        /// 本身无定时器(纯请求-响应 + 一条 S2C 推送),不需要 Tick。
        /// </summary>
        public AttributeClient Attributes { get; }

        /// <summary>
        /// 战斗直连链路(turn-based-battle-server.md §18):客户端第二条 TCP 连接,直连 battle
        /// 节点、票据入场。落点分配经大厅 NotifyBattleAssigned 到达后由它建连;Battle / Spectate
        /// 的四条战斗 RPC 在它验证通过后经 <see cref="DirectRoutingBattleTransport"/> 分流过去,
        /// 未建立 / 已断开时自动回落 gate 中继。大厅断线即关闭(重连后由 NotifyBattleReconnect
        /// 触发补签重建)。
        /// </summary>
        public BattleDirectLink BattleLink { get; }

        /// <summary>
        /// 宝宝(宠物)网络层(docs/design/player-pet.md)。与 Attributes 平行:
        /// 各持一个无状态 GameClientBattleTransport,底层仍是同一条 gate 连接。
        /// 本身无定时器(纯请求-响应 + 一条 S2C 推送),不需要 Tick。
        /// </summary>
        public PetClient Pets { get; }

        public PlayerFeaturesClient Features { get; }

        /// <summary>gate 连接已建立且 token 校验通过(战斗排队轮询等周期请求的放行条件)。</summary>
        public bool IsGateReady => _gate != null && _gate.Connected && TokenVerified;

        /// <summary>Account used by the last EnterZone run (needed for redirect re-login).</summary>
        public string Account { get; private set; }

        public event Action<string> OnLog;
        public event Action OnDisconnected;
        /// <summary>Queue progress during EnterZone (assign-gate code=100).</summary>
        public event Action<AssignGateResult> OnQueueUpdate;
        /// <summary>Coarse progress text for the UI status line.</summary>
        public event Action<string> OnFlowStatus;
        /// <summary>Raised after the authoritative scene switch cleared the old actor world.</summary>
        public event Action<SceneInfoComp> OnSceneEntered;

        /// <summary>
        /// Coroutine starter wired by AppBootstrap. GameClient is not a
        /// MonoBehaviour, but the redirect flow needs to spawn a coroutine
        /// from inside a notify handler.
        /// </summary>
        public Func<IEnumerator, Coroutine> CoroutineRunner;

        /// <summary>选角/建角界面回填的结果(见 <see cref="PlayerChooser"/>)。</summary>
        public sealed class PlayerChoice
        {
            /// <summary>>0 = 用这个已有角色进入游戏。</summary>
            public ulong SelectedPlayerId;
            /// <summary>true = 用下面的职业/性别创建新角色并进入。</summary>
            public bool CreateNew;
            public uint ClassId;   // Class 配表 id
            public uint Gender;    // 1=男 2=女
            /// <summary>true = 放弃进入,回到选服界面。</summary>
            public bool Cancelled;
        }

        /// <summary>
        /// 选角/建角 UI 钩子。TCP Login 拿到账号角色列表后,管线把「按选中区过滤后
        /// 的角色列表」交给它,协程结束时从 PlayerChoice 读结果再继续 EnterGame。
        /// 参数:(zoneId, 该区角色列表, 待回填的结果)。
        /// 不挂接(null)时保持旧行为:区内无角色则按默认职业静默建号,有则进第一个
        /// —— 现有 EditMode 回归与无 UI 的调试路径依赖这一行为。
        /// </summary>
        public Func<uint, IReadOnlyList<AccountSimplePlayer>, PlayerChoice, IEnumerator> PlayerChooser;

        public GameClient(string gatewayBaseUrl)
        {
            _http = new GatewayHttpClient(gatewayBaseUrl);
            _codec = new MuduoCodec();
            // top-level (no package) protos emitted by protoc
            _codec.Register<ClientRequest>();
            _codec.Register<MessageContent>();
            _codec.Register<ClientTokenVerifyRequest>();
            _codec.Register<ClientTokenVerifyResponse>();
            // 战斗直连握手包(§18.2);两条连接共用同一个 codec 实例
            _codec.Register<BattleTokenVerifyRequest>();
            _codec.Register<BattleTokenVerifyResponse>();

            World = new ActorWorld();

            // 战斗直连链路要先于 WireSceneNotifyHandlers 建好:NotifyBattleAssigned 的处理器指向它
            BattleLink = new BattleDirectLink(() => new GateTcpClient(_codec), s => Log($"[battle-direct] {s}"));
            BattleLink.TicketReissuer = RequestBattleTicket;
            BattleLink.OnVerified += id =>
                Log($"[battle-direct] verified battle_id={id} role={BattleLink.Role} endpoint={BattleLink.Assignment?.Host}:{BattleLink.Assignment?.Port}");
            BattleLink.OnClosed += reason => Log($"[battle-direct] closed reason={reason}");

            WireSceneNotifyHandlers();

            // 回合制战斗:BattleClient 的 battle/match OnNotify 注册在其构造时
            // 经传输接口完成;单例挂接方式与 GameClient 一致(实例由宿主持有,
            // 静态 Instance 供 UI 层解析)。
            // Battle / Spectate 经 DirectRoutingBattleTransport 分流:四条战斗 RPC 与本人的
            // 战斗 S2C 在直连验证后走 BattleLink,其余照旧走本管线(gate 中继)。
            Battle = BattleClient.Attach(new DirectRoutingBattleTransport(new GameClientBattleTransport(this), BattleLink));
            Spectate = SpectateClient.Attach(new DirectRoutingBattleTransport(new GameClientBattleTransport(this), BattleLink));
            Attributes = AttributeClient.Attach(new GameClientBattleTransport(this));
            Pets = PetClient.Attach(new GameClientBattleTransport(this));
            Features = PlayerFeaturesClient.Attach(new GameClientBattleTransport(this));
        }

        public GatewayHttpClient Http => _http;

        public void Tick()
        {
            _gate?.Poll();
            MaybeRefreshToken();
            BattleLink?.Tick(Time.realtimeSinceStartup); // 直连握手期限/重连倒计时/请求超时由主循环驱动
            Battle?.Tick(Time.realtimeSinceStartup); // 排队轮询/准备超时由主循环驱动
            Spectate?.Tick(Time.realtimeSinceStartup); // 观战首帧超时由主循环驱动
        }

        /// <summary>
        /// 战斗票据补签(§18 D25):客户端丢票 / 大厅重连后,经大厅会话调 MatchService.RequestBattleTicket,
        /// match 定位房间所在 battle 节点由其自签。BattleLink 的 TicketReissuer 挂点。
        /// 返回 false = 此刻发不出(大厅未就绪 / 无协程宿主)。
        /// </summary>
        private bool RequestBattleTicket(ulong battleId, Action<BattleAssignedS2C> onAssigned, Action<string> onError)
        {
            var runner = CoroutineRunner;
            if (runner == null || !IsGateReady) return false;
            runner(Call(MessageIds.RequestBattleTicket,
                new RequestBattleTicketRequest { BattleId = battleId },
                RequestBattleTicketResponse.Parser,
                r =>
                {
                    if (r.ErrorMessage != null && r.ErrorMessage.Id != 0) { onError($"tip={r.ErrorMessage.Id}"); return; }
                    if (r.Assignment == null || r.Assignment.BattleId == 0) { onError("empty assignment"); return; }
                    onAssigned(r.Assignment);
                },
                onError));
            return true;
        }

        public void OnNotify(uint messageId, Action<MessageContent> handler)
            => _notifyHandlers[messageId] = handler;

        public void SetTokens(string access, string refresh, long accessExpire)
        {
            AccessToken = access;
            RefreshToken = refresh;
            _accessTokenExpire = accessExpire;
        }

        // ── EnterZone: the full multi-step pipeline ───────────────────────
        //
        //   1. POST /api/login        (zone-pinned HTTP login, queue-aware)
        //   2. POST /api/assign-gate  (queue-aware, may poll /api/queue-status)
        //   3. TCP connect + ClientTokenVerifyRequest handshake
        //   4. TCP Login (auth_type=access_token) — binds session -> account
        //   5. CreatePlayer if the account has no player yet
        //   6. EnterGame — reply only means "accepted"; success is the
        //      NotifyEnterScene push (entergamelogic.go finishes async).

        public IEnumerator EnterZone(uint zoneId, string account, string password, string deviceId,
                                     Action onSuccess, Action<string> onError)
        {
            int gen = ++_pipelineGen;
            ResetConnectionState(); // 清掉上一次失败/遗留的连接与 pending 状态
            // 重定向跳数按"一次登录会话"计:这里是新会话的起点,把熔断计数归零,
            // 否则上一条会话用掉的跳数会把这条会话的正常重定向提前掐死。
            _redirectHops = 0;
            Account = account;

            // ── 1. HTTP login (issues access/refresh tokens + player list) ──
            Status("正在登录账号…");
            GatewayLoginResult loginHttp = null;
            while (true)
            {
                GatewayLoginResult r = null;
                string err = null;
                yield return _http.Login(new GatewayLoginRequest
                {
                    zone_id = zoneId,
                    account = account,
                    password = password,
                    auth_type = "password",
                    device_id = deviceId,
                }, x => r = x, e => err = e);
                if (gen != _pipelineGen) yield break;
                if (err != null) { onError(err); yield break; }

                if (r.code == GatewayLoginResult.CodeQueueing)
                {
                    Status($"登录排队中… 前方约 {Math.Max(0, r.queue_pos)} 人");
                    yield return WaitMs(r.retry_after_ms > 0 ? r.retry_after_ms : 2000);
                    if (gen != _pipelineGen) yield break;
                    continue;
                }
                if (r.code != GatewayLoginResult.CodeOk)
                {
                    onError(DescribeLoginCode(r));
                    yield break;
                }
                loginHttp = r;
                break;
            }

            if (!string.IsNullOrEmpty(loginHttp.access_token))
                SetTokens(loginHttp.access_token, loginHttp.refresh_token, loginHttp.access_token_expire);
            Log($"http login ok, players={loginHttp.players?.Length ?? 0}");

            // ── 2. Assign gate (queue-aware) ──
            Status("正在分配服务器…");
            AssignGateResult assigned = null;
            string queueToken = null;
            while (true)
            {
                AssignGateResult r = null;
                string err = null;
                if (string.IsNullOrEmpty(queueToken))
                {
                    // 全限定:Loginpb.AssignGateRequest(proto)与 HTTP DTO 同名,
                    // 两个命名空间都在 using 里,不限定会 CS0104。
                    yield return _http.AssignGate(new MmorpgClient.Net.AssignGateRequest
                    {
                        zone_id = zoneId,
                        account = account,
                        device_id = deviceId,
                        queue_token = "",
                    }, x => r = x, e => err = e);
                }
                else
                {
                    yield return _http.QueryQueueStatus(queueToken, zoneId, x => r = x, e => err = e);
                }
                if (gen != _pipelineGen) yield break;
                if (err != null) { onError(err); yield break; }

                if (r.code == AssignGateResult.CodeQueueing)
                {
                    if (r.queue_source == "login" && !string.IsNullOrEmpty(r.queue_token))
                        queueToken = r.queue_token;
                    OnQueueUpdate?.Invoke(r);
                    Status(r.queue_source == "login"
                        ? $"排队中… {r.queue_rank + 1}/{Math.Max(r.queue_total, r.queue_rank + 1)}"
                        : "服务器繁忙,稍候重试…");
                    yield return WaitMs(r.retry_after_ms > 0 ? r.retry_after_ms : 2000);
                    if (gen != _pipelineGen) yield break;
                    continue;
                }
                if (r.code == AssignGateResult.CodeQueueExpired)
                {
                    // Queue slot lost (e.g. client slept) — restart from assign-gate.
                    queueToken = null;
                    Status("排队已过期,重新排队…");
                    continue;
                }
                if (r.code != AssignGateResult.CodeOk)
                {
                    onError(DescribeAssignCode(r));
                    yield break;
                }
                assigned = r;
                break;
            }
            Log($"assigned gate {assigned.gate_ip}:{assigned.gate_port}");

            // ── 3~6. Connect, verify, bind session, enter ──
            yield return ConnectAndEnter(gen, zoneId,
                assigned.gate_ip, (int)assigned.gate_port,
                SafeBase64(assigned.token_payload), SafeBase64(assigned.token_signature),
                password, onSuccess, onError);
        }

        /// <summary>
        /// Steps 3-6 of the pipeline, shared with the redirect flow (which
        /// receives raw token bytes from RedirectToGateNotify instead of
        /// base64 JSON fields).
        ///
        /// <paramref name="preConnected"/> 是重定向专用:那条路必须"先连上新 gate、
        /// 再关旧的",所以建连接这一步由 <see cref="RedirectFlow"/> 提前做掉,这里只接管。
        /// 传 null 时按老路自己建连接(EnterZone 首次进入,本来就没有旧连接要保)。
        /// </summary>
        private IEnumerator ConnectAndEnter(int gen, uint zoneId, string ip, int port, byte[] payload, byte[] signature,
                                            string passwordFallback,
                                            Action onSuccess, Action<string> onError,
                                            GateTcpClient preConnected = null)
        {
            // 世代已经翻篇:预建的连接没人接管了,必须自己关掉,否则泄漏一条 TCP + 两个线程。
            if (gen != _pipelineGen) { preConnected?.Dispose(); yield break; }
            Status("正在连接服务器…");
            if (preConnected != null)
            {
                AdoptGate(preConnected);
            }
            else
            {
                try { ConnectGate(ip, port); }
                catch (Exception ex) { FailPipeline(gen, onError, $"connect gate: {ex.Message}"); yield break; }
            }
            // 连上之后才认这个地址:失败时 AssignedGate 还是老 gate,
            // 跨区验证脚本读到的"落区证据"就不会是一条从没连上过的地址。
            AssignedGate = $"{ip}:{port}";

            _gate.OnMessage += DispatchInbound;
            _gate.OnDisconnected += HandleTransportDisconnected;

            try
            {
                _gate.Send(new ClientTokenVerifyRequest
                {
                    Payload = ByteString.CopyFrom(payload),
                    Signature = ByteString.CopyFrom(signature),
                });
            }
            catch (Exception ex)
            {
                FailPipeline(gen, onError, $"token verify: {ex.Message}");
                yield break;
            }
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!TokenVerified && Time.realtimeSinceStartup < deadline)
            {
                if (gen != _pipelineGen) yield break;
                Tick();
                yield return null;
            }
            if (gen != _pipelineGen) yield break;
            if (!TokenVerified) { FailPipeline(gen, onError, "token verify timeout"); yield break; }
            Log("gate token verified");

            // ── TCP Login: binds this TCP session to the account on the
            // zone's login service (writes login_session:{sid} -> account).
            // Prefer token re-auth so the password never rides the game TCP;
            // fall back to password when the HTTP step didn't issue tokens.
            Status("正在验证会话…");
            var loginReq = !string.IsNullOrEmpty(AccessToken)
                ? new LoginRequest { Account = Account, AuthType = "access_token", AuthToken = AccessToken }
                : new LoginRequest { Account = Account, Password = passwordFallback ?? "", AuthType = "password" };

            LoginResponse loginResp = null;
            yield return Call(MessageIds.Login, loginReq,
                LoginResponse.Parser, r => loginResp = r,
                e => FailPipeline(gen, onError, $"login: {e}"));
            if (gen != _pipelineGen) yield break;
            if (loginResp == null) yield break;
            if (loginResp.ErrorMessage != null && loginResp.ErrorMessage.Id != 0)
            { FailPipeline(gen, onError, $"登录失败(tip={loginResp.ErrorMessage.Id})"); yield break; }
            if (!string.IsNullOrEmpty(loginResp.AccessToken))
                SetTokens(loginResp.AccessToken, loginResp.RefreshToken, loginResp.AccessTokenExpire);
            Log($"session login ok, players={loginResp.Players.Count}");

            // ── 选角 / 建角 ──
            ulong playerId = 0;
            if (_redirecting && _redirectPlayerId != 0)
            {
                // 跨区/跨 gate 重定向:沿用当前角色,不重新弹选角
                playerId = _redirectPlayerId;
            }
            else
            {
                // 按选中区过滤;zone_id==0 的存量角色(加字段前创建)在任何区都可见,
                // 避免旧账号一夜之间"角色消失"。
                var zonePlayers = new List<AccountSimplePlayer>();
                foreach (var w in loginResp.Players)
                {
                    var p = w.Player;
                    if (p == null) continue;
                    if (zoneId == 0 || p.ZoneId == zoneId || p.ZoneId == 0)
                        zonePlayers.Add(p);
                }

                if (PlayerChooser != null)
                {
                    var choice = new PlayerChoice();
                    yield return PlayerChooser(zoneId, zonePlayers, choice);
                    if (gen != _pipelineGen) yield break;
                    if (choice.Cancelled)
                    { FailPipeline(gen, onError, "已返回选服"); yield break; }
                    if (choice.CreateNew)
                    {
                        ulong newId = 0;
                        yield return CreatePlayerCo(gen, choice.ClassId, choice.Gender,
                            loginResp.Players, id => newId = id, onError);
                        if (gen != _pipelineGen) yield break;
                        if (newId == 0) yield break; // CreatePlayerCo 已 FailPipeline
                        playerId = newId;
                    }
                    else
                    {
                        playerId = choice.SelectedPlayerId;
                    }
                }
                else if (zonePlayers.Count == 0)
                {
                    ulong newId = 0;
                    yield return CreatePlayerCo(gen, 0, 0, loginResp.Players, id => newId = id, onError);
                    if (gen != _pipelineGen) yield break;
                    if (newId == 0) yield break;
                    playerId = newId;
                }
                else
                {
                    playerId = zonePlayers[0].PlayerId;
                }
            }
            if (playerId == 0)
            { FailPipeline(gen, onError, "未选择角色"); yield break; }

            // 记住该区最近进入的角色(选角屏用来标注 [上次])
            if (!_redirecting && zoneId != 0)
                MmorpgClient.Core.ClientSettings.SetLastPlayer(zoneId, playerId);

            yield return EnterGameAndWaitScene(gen, playerId, onSuccess, onError);
        }

        /// <summary>
        /// CreatePlayer(带职业/性别)并从"全量列表响应"里 diff 出新角色 id。
        /// classId/gender 传 0 表示交给服务端取默认(配表第一个职业 / 男)。
        /// </summary>
        private IEnumerator CreatePlayerCo(int gen, uint classId, uint gender,
            Google.Protobuf.Collections.RepeatedField<AccountSimplePlayerWrapper> known,
            Action<ulong> onCreated, Action<string> onError)
        {
            Status("正在创建角色…");
            var knownIds = new HashSet<ulong>();
            foreach (var w in known)
                if (w.Player != null) knownIds.Add(w.Player.PlayerId);

            CreatePlayerResponse cpResp = null;
            yield return Call(MessageIds.CreatePlayer,
                new CreatePlayerRequest { ClassId = classId, Gender = gender },
                CreatePlayerResponse.Parser, r => cpResp = r,
                e => FailPipeline(gen, onError, $"create player: {e}"));
            if (gen != _pipelineGen) yield break;
            if (cpResp == null) yield break;
            if (cpResp.ErrorMessage != null && cpResp.ErrorMessage.Id != 0)
            { FailPipeline(gen, onError, $"创建角色失败(tip={cpResp.ErrorMessage.Id})"); yield break; }

            // 响应是账号全量角色列表:新角色 = 不在请求前列表里的那一个
            ulong newId = 0;
            foreach (var w in cpResp.Players)
                if (w.Player != null && !knownIds.Contains(w.Player.PlayerId))
                    newId = w.Player.PlayerId;
            if (newId == 0 && cpResp.Players.Count > 0)
                newId = cpResp.Players[cpResp.Players.Count - 1].Player?.PlayerId ?? 0;
            if (newId == 0)
            { FailPipeline(gen, onError, "create player returned empty list"); yield break; }

            Log($"created new player {newId} class={classId} gender={gender}");
            onCreated(newId);
        }

        /// <summary>
        /// EnterGame + wait for the NotifyEnterScene push. The EnterGame reply
        /// only means "request accepted" — the login service finishes the data
        /// load / scene routing asynchronously (entergamelogic.go).
        /// </summary>
        private IEnumerator EnterGameAndWaitScene(int gen, ulong playerId, Action onSuccess, Action<string> onError)
        {
            Status("正在进入游戏…");
            _enteredScene = false;
            EnterGameResponse egResp = null;
            yield return Call(MessageIds.EnterGame,
                new EnterGameRequest { PlayerId = playerId, RequestId = Guid.NewGuid().ToString("N") },
                EnterGameResponse.Parser, r => egResp = r,
                e => FailPipeline(gen, onError, $"enter game: {e}"));
            if (gen != _pipelineGen) yield break;
            if (egResp == null) yield break;
            if (egResp.ErrorMessage != null && egResp.ErrorMessage.Id != 0)
            { FailPipeline(gen, onError, $"进入游戏失败(tip={egResp.ErrorMessage.Id})"); yield break; }
            PlayerId = egResp.PlayerId != 0 ? egResp.PlayerId : playerId;
            Log($"enter game accepted, player_id={PlayerId}");

            // Match the production robot's authoritative scene-ready budget:
            // a saturated scene loop can legitimately delay the first push.
            float deadline = Time.realtimeSinceStartup + 60f;
            while (!_enteredScene && Time.realtimeSinceStartup < deadline)
            {
                if (gen != _pipelineGen) yield break;
                Tick();
                yield return null;
            }
            if (gen != _pipelineGen) yield break;
            if (!_enteredScene)
            {
                // Stop polling this gate before surfacing the timeout so a
                // late NotifyEnterScene cannot split InGame from the UI state.
                FailPipeline(gen, onError, "等待进入场景超时");
                yield break;
            }

            InGame = true;
            Status("进入游戏成功");
            onSuccess();
        }

        // ── Redirect (cross-zone / gate migration) ───────────────────────
        //
        // 服务端半边只做一件事:cpp/nodes/gate/handler/event/gate_event_handler.cpp
        // 的 RedirectToGateEventHandler 把 target_ip / target_port / token_payload /
        // token_signature / token_deadline 打进 RedirectToGateNotify(msg 124)推给客户端,
        // **搬迁动作全在客户端**。login 侧开关 HomeZone.RedirectOnEnterEnabled 默认关着,
        // 正是因为客户端不做下面这套动作就会卡死:login 的 EnterGame 那时已经回了成功
        // 并清掉了登录会话,老 gate 上再没有任何东西会推进这个玩家,客户端自己重试只会撞
        // kLoginSessionNotFound。
        //
        // 契约里最容易误解的一点:票据只认证**这一条 TCP**,不是跨区的登录会话转移。
        // 目标 gate 校验 HMAC-SHA256(gate_token_secret, token_payload) 与 token_signature
        // 常数时间相等,再解出 GateTokenPayload 校验 gate_node_id == 本 gate、
        // expire_timestamp > now(cpp/nodes/gate/handler/rpc/client_message_processor.cpp
        // DispatchTokenVerify)。校验通过只是把这条连接标成 verified —— 新 zone 的 login
        // 那边**没有**任何会话。所以必须完整重跑 Login + EnterGame,这正是 ConnectAndEnter
        // 干的事,**不能跳**;跳掉的客户端会连上一个哑连接,表现为"重定向后卡死"。
        //
        // 参考实现逐条对齐 robot/pkg/redirect.go 的 FollowRedirect:
        //   ① 本地校验目标(地址 + token_deadline)  ② 环路熔断  ③ 可达性探测
        //   ④ 换连接(先连新、后关旧)              ⑤ 票据原样转发  ⑥ 重跑 Login+EnterGame
        //
        // robot 的第 ⑦ 步"补投握手期间攒下的推送"(ReplayDeferred)在这里**不需要对应代码**:
        // GateTcpClient 的读线程始终把帧塞进 _inbox 这条 ConcurrentQueue,握手期间到达的推送
        // 一条都没丢;ConnectAndEnter 的等待循环每帧调 Tick() → _gate.Poll(),队列自然被排空。
        // 换句话说 inbox 本身就是 robot 那个 deferred 队列,而且不需要显式 replay。
        // (对应地,GateTcpClient.Poll 里的 _disposed 判据保证旧连接残留的帧不会漏到新会话上。)

        /// <summary>
        /// 一条登录会话允许连续跟随的重定向次数上限,与 robot 的 MaxRedirectHops 同值。
        /// 配错的归属映射(A 的映射指向 B、B 的又指回 A)会让两边 gate 互相踢皮球,
        /// 没有这道熔断就是一个不停重连的死循环。
        /// </summary>
        public const int MaxRedirectHops = 3;

        /// <summary>目标 gate 可达性探测预算(秒),与 robot redirectDialTimeout 同值。</summary>
        private const float RedirectProbeTimeoutSec = 5f;

        /// <summary>
        /// 只做本地可判的检查,不碰网络;返回 null 表示通过,否则是给人看的失败原因。
        /// 对齐 robot/pkg/redirect.go 的 RedirectTarget.Validate。
        /// </summary>
        private static string ValidateRedirectTarget(RedirectToGateNotify ev)
        {
            if (ev == null) return "empty notify";
            if (string.IsNullOrWhiteSpace(ev.TargetIp)) return "empty target_ip";
            if (ev.TargetPort == 0 || ev.TargetPort > 65535) return $"invalid target_port {ev.TargetPort}";
            // token_deadline 是 gate 侧 GateTokenPayload.expire_timestamp 的副本,单位是
            // **unix 秒**(不是本仓库常见的毫秒)。已经过期就不用白跑一趟:换完连接目标 gate
            // 必然回 token_expired 并把连接关掉,那时老连接已经没了,会话就此报废 ——
            // 不如趁还连着老 gate 的时候失败出去。
            if (ev.TokenDeadline > 0)
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (now >= ev.TokenDeadline)
                    return $"gate token already expired (deadline={ev.TokenDeadline}, now={now})";
            }
            return null;
        }

        /// <summary>
        /// 目标 gate 可达性探测:拨通就挂,只为把"地址不对"变成一条立刻可见的错误。
        ///
        /// 为什么非探不可:换连接用的 <c>TcpClient.Connect</c> 是**同步阻塞**的,拿它去连一个
        /// 被黑洞掉的 target_ip,Unity 主线程会卡满 SYN 重试(Windows 上约 21 秒),整个客户端
        /// 表现为"静默卡死" —— 与 robot 那边 muduo 拨号永不返回错误是同一类症状
        /// (见 robot/pkg/redirect.go 的 probeTCP)。先用带超时的异步连接探一次,不通就在
        /// **还连着老 gate** 的时候失败出去。
        /// </summary>
        private static IEnumerator ProbeGate(string host, int port, float timeoutSec, Action<string> onFail)
        {
            System.Net.Sockets.TcpClient probe = null;
            System.Threading.Tasks.Task task = null;
            string err = null;
            try
            {
                probe = new System.Net.Sockets.TcpClient { NoDelay = true };
                task = probe.ConnectAsync(host, port);
            }
            catch (Exception ex) { err = ex.Message; }

            if (err == null)
            {
                float deadline = Time.realtimeSinceStartup + timeoutSec;
                while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                if (!task.IsCompleted)
                {
                    err = $"connect timed out after {timeoutSec:0.#}s";
                    // 下面 Close 之后这个 Task 必然以异常收场。不观察它,.NET 会在终结器线程上
                    // 抛 UnobservedTaskException,在播放器日志里就是一条没有上下文的无主报错。
                    task.ContinueWith(t => { _ = t.Exception; },
                                      System.Threading.Tasks.TaskScheduler.Default);
                }
                else if (task.IsFaulted)
                {
                    err = task.Exception?.GetBaseException().Message ?? "connect failed";
                }
            }
            try { probe?.Close(); } catch { }
            if (err != null) onFail(err);
        }

        /// <summary>
        /// 重定向失败的统一出口。**刻意不做静默丢弃**:走到这里说明这条会话已经没救了 ——
        /// 老 gate 的登录会话在 EnterGame 成功时就被清掉,不会再有 RoutePlayer 到来,
        /// 悄悄咽下去只会让玩家卡在一条哑连接上。所以既要炸出可见的错误,也要主动断线,
        /// 把控制权交回外层(UI 回登录 / DevAutoPilot 判失败)。
        ///
        /// 换连接**之前**失败时老连接其实还活着,这里主动断掉是刻意的:重定向意味着服务端
        /// 已经认定这个玩家的归属不在本 zone,继续赖在老 gate 上没有意义。
        /// 换连接**之后**失败由 ConnectAndEnter 的 FailPipeline 走同一条路(见 RedirectFlow 的 onError)。
        /// </summary>
        private void FailRedirect(string target, string reason)
        {
            LogError($"[gate] redirect to {target} failed: {reason}");
            Status("切换服务器失败,请重新登录");
            DisconnectInternal(notify: true, forceNotification: true);
        }

        private IEnumerator RedirectFlow(RedirectToGateNotify ev)
        {
            string target = $"{ev?.TargetIp}:{ev?.TargetPort}";

            // ① 本地判据先行。这一段全部在**老连接还活着**的时候做完,失败即就地作废。
            string invalid = ValidateRedirectTarget(ev);
            if (invalid != null) { FailRedirect(target, invalid); yield break; }

            // ② 环路熔断(robot MaxRedirectHops 同值同语义)。
            if (_redirectHops >= MaxRedirectHops)
            {
                FailRedirect(target, $"hop limit reached ({MaxRedirectHops} hops), refusing to follow");
                yield break;
            }
            _redirectHops++;

            int gen = ++_pipelineGen;   // 作废任何在跑的管线,重定向接管连接
            _redirecting = true;
            _redirectPlayerId = PlayerId; // ResetConnectionState 会清 PlayerId,先捕获以沿用当前角色
            // 战斗直连要在重定向后自愈,而 ResetConnectionState 会 Close 链路并清掉 battle_id,
            // 先捕获。**不能只指望服务端推 NotifyBattleReconnect**:那条推送只在
            // enter_gs_type==LOGIN_RECONNECT(旧会话已 StateDisconnecting)或实体被重建走
            // RestoreBattleFreezeOnLogin 时发出;gate 迁移时旧会话通常仍是 StateOnline →
            // login 判 ReplaceLogin → 两条分支都不触发(scene 侧 player_battle.cpp
            // OnPlayerEnterScene 的两步守卫)。那种情况下服务端连 BindBattleEvent 也不会重发,
            // 新 gate 上没有战斗绑定,gate 中继同样断 —— 主动补签直连是唯一能自愈的一侧。
            ulong battleIdBeforeRedirect = BattleLink?.BattleId ?? 0;

            // 从这一刻起,老连接不再驱动任何状态 —— 但**先不关**(还要留着兜底,见 ③)。
            //
            // robot 那边靠"换连接必须由 RecvLoop 自己做"来保证这件事:单线程的接收循环
            // 停在处理器里,老连接期间不可能再派发任何东西。Unity 这边没有这种天然屏障:
            // 本处理器是在 _gate.Poll() 的调用栈里跑的,Poll 返回之后主循环每帧还会接着
            // 派发老连接的消息,而 ③ 的探测会 yield 几帧甚至几秒。那段时间里,老 zone 的
            // 推送是噪声,更糟的是老 gate 万一先断开,HandleTransportDisconnected 会抢在
            // 重定向完成之前把 UI 打回登录。摘掉这两个事件就把窗口封死了。
            var stale = _gate;
            if (stale != null)
            {
                stale.OnMessage -= DispatchInbound;
                stale.OnDisconnected -= HandleTransportDisconnected;
            }

            Status("正在切换服务器…");
            Log($"[gate] redirect to {target} hop={_redirectHops}/{MaxRedirectHops} " +
                $"token_payload={ev.TokenPayload.Length}B deadline={ev.TokenDeadline}");
            try
            {
                // ③ 探一次目标 gate 通不通(理由见 ProbeGate)。
                //    老连接此刻还连着(只是上面刚摘掉了事件,不再派发),探不通就在这里失败,
                //    不会出现"关了旧的又连不上新的"的裸奔窗口。
                string probeErr = null;
                yield return ProbeGate(ev.TargetIp, (int)ev.TargetPort, RedirectProbeTimeoutSec,
                                       e => probeErr = e);
                if (gen != _pipelineGen) yield break;
                if (probeErr != null)
                {
                    FailRedirect(target, $"target gate unreachable: {probeErr}");
                    yield break;
                }

                // ④ 换连接:**先把新连接建起来**,确认成功之后才关旧的
                //    (次序对齐 robot SwapConn;理由见 OpenGate 的注释)。
                GateTcpClient fresh = null;
                string connErr = null;
                try { fresh = OpenGate(ev.TargetIp, (int)ev.TargetPort); }
                catch (Exception exc) { connErr = exc.Message; }
                if (connErr != null) { FailRedirect(target, $"connect gate: {connErr}"); yield break; }

                ResetConnectionState();   // 旧连接与旧 zone 的会话状态到这一刻才清掉
                // ⑤⑥ 票据 payload / signature **原样**转发(签名是 64 个 ASCII 十六进制字符
                //     装在 bytes 里,绝不能解码),随后在新连接上完整重跑 Login + EnterGame。
                yield return ConnectAndEnter(gen, 0,
                    ev.TargetIp, (int)ev.TargetPort,
                    ev.TokenPayload.ToByteArray(), ev.TokenSignature.ToByteArray(),
                    null,
                    () =>
                    {
                        Log($"[gate] redirect complete gate={AssignedGate} player_id={PlayerId} hops={_redirectHops}");
                        if (battleIdBeforeRedirect != 0)
                        {
                            // 与服务端可能推来的 NotifyBattleReconnect 幂等(链路内部按
                            // battle_id + 在途补签去重),两者谁先到都只补签一次
                            Log($"[battle-direct] redirect 后重建直连 battle_id={battleIdBeforeRedirect}");
                            BattleLink?.HandleReconnectHint(battleIdBeforeRedirect);
                        }
                    },
                    e =>
                    {
                        // 已经换过连接了:老连接关了、老 zone 的登录会话也早没了,这条会话就是死的。
                        // FailPipeline 已经 ResetConnectionState,这里补一次带通知的断线让 UI 收场。
                        LogError($"[gate] redirect to {target} failed after swap: {e}");
                        Status("切换服务器失败,请重新登录");
                        DisconnectInternal(notify: true, forceNotification: true);
                    },
                    preConnected: fresh);
            }
            finally { _redirecting = false; }
        }

        /// <summary>
        /// Explicit C2S EnterScene. Server normally pushes NotifyEnterScene
        /// automatically after EnterGame, so use this only when switching
        /// scene at runtime (instance / dungeon / mirror).
        /// </summary>
        public IEnumerator EnterScene(uint sceneConfigId, ulong sceneId,
                                      Action onSuccess, Action<string> onError)
        {
            var req = new EnterSceneC2SRequest
            {
                SceneInfo = new SceneInfoComp
                {
                    SceneConfigId = sceneConfigId,
                    SceneId = sceneId,
                },
            };
            EnterSceneC2SResponse resp = null;
            yield return Call(MessageIds.EnterScene, req, EnterSceneC2SResponse.Parser,
                r => resp = r, e => onError($"enter scene: {e}"));
            if (resp == null) yield break;
            if (resp.ErrorMessage != null && resp.ErrorMessage.Id != 0)
            { onError($"enter scene tip={resp.ErrorMessage.Id}"); yield break; }
            onSuccess();
        }

        /// <summary>
        /// Send a fire-and-forget skill release. Server-pushed
        /// NotifySkillUsed/NotifySkillInterrupted messages drive the
        /// world-side FX through the dispatcher set up in
        /// <see cref="WireSceneNotifyHandlers"/>.
        /// </summary>
        public void ReleaseSkill(uint skillTableId, ulong targetEntity, UnityEngine.Vector3? position = null)
        {
            var req = new ReleaseSkillRequest
            {
                SkillTableId = skillTableId,
                TargetId = targetEntity,
            };
            if (position.HasValue)
            {
                req.Position = WorldCoordinateConverter.ToServerVector(position.Value);
            }
            SendOneWay(MessageIds.ReleaseSkill, req);
        }

        // ── Movement ─────────────────────────────────────────────
        //
        // Coordinate mapping: server uses UE-style (X=forward, Y=right, Z=up).
        // Unity uses (X=right, Y=up, Z=forward). We mirror SpawnActorView's
        // mapping so positions round-trip cleanly:
        //     server.X <-> unity.Z
        //     server.Y <-> unity.X
        //     server.Z <-> unity.Y

        private uint _moveInputSeq;
        private bool _isMoving;

        public uint NextInputSeq() => ++_moveInputSeq;
        public bool IsMoving => _isMoving;

        public void SendMoveStart(UnityEngine.Vector3 startPos, UnityEngine.Vector3 euler,
                                  UnityEngine.Vector3 velocity, UnityEngine.Vector3? targetPos = null)
        {
            var req = new MoveStartC2S
            {
                StartLocation  = WorldCoordinateConverter.ToServerLocation(startPos),
                Rotation       = WorldCoordinateConverter.ToServerRotation(euler),
                Velocity       = WorldCoordinateConverter.ToServerVelocity(velocity),
                TargetLocation = WorldCoordinateConverter.ToServerLocation(targetPos ?? UnityEngine.Vector3.zero),
                ClientTimeMs   = NowUnixMs(),
                InputSeq       = NextInputSeq(),
            };
            _isMoving = TrySendOneWay(MessageIds.MoveStart, req);
        }

        public void SendMoveStop(UnityEngine.Vector3 endPos, UnityEngine.Vector3 euler)
        {
            var req = new MoveStopC2S
            {
                EndLocation  = WorldCoordinateConverter.ToServerLocation(endPos),
                Rotation     = WorldCoordinateConverter.ToServerRotation(euler),
                ClientTimeMs = NowUnixMs(),
                InputSeq     = NextInputSeq(),
            };
            SendOneWay(MessageIds.MoveStop, req);
            _isMoving = false;
        }

        public void SendMoveSync(UnityEngine.Vector3 pos, UnityEngine.Vector3 euler, UnityEngine.Vector3 velocity)
        {
            var req = new MoveSyncC2S
            {
                Location     = WorldCoordinateConverter.ToServerLocation(pos),
                Rotation     = WorldCoordinateConverter.ToServerRotation(euler),
                Velocity     = WorldCoordinateConverter.ToServerVelocity(velocity),
                ClientTimeMs = NowUnixMs(),
                InputSeq     = NextInputSeq(),
            };
            SendOneWay(MessageIds.MoveSync, req);
        }

        private static ulong NowUnixMs()
            => (ulong)System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // ── internals ────────────────────────────────────────────

        private void ConnectGate(string host, int port) => AdoptGate(OpenGate(host, port));

        /// <summary>
        /// 建一条新的 gate 连接但**不接管**:不碰 _gate / _pending / TokenVerified。
        /// 抛异常时内部已经把半成品连接关掉了,调用方不用善后。
        ///
        /// 之所以要把"建连接"和"接管"拆开:重定向必须先把新连接建起来、确认成功之后
        /// 才允许关旧连接(对齐 robot/pkg/client.go GameClient.SwapConn 的次序)。
        /// 反过来先关旧的,一旦新 gate 连不上,玩家就被扔在一条已经关掉的连接上,
        /// 谁也救不回来 —— 老 gate 那边的登录会话早在 EnterGame 成功时就被清掉了。
        /// </summary>
        private GateTcpClient OpenGate(string host, int port)
        {
            var fresh = new GateTcpClient(_codec);
            fresh.OnError += e => Log($"[gate] error: {e}");
            try { fresh.Connect(host, port); }
            catch { fresh.Dispose(); throw; }
            return fresh;
        }

        /// <summary>
        /// 接管一条已经建好的连接。旧连接在这一刻(新连接确定可用之后)才关。
        /// </summary>
        private void AdoptGate(GateTcpClient fresh)
        {
            _gate?.Dispose();
            // A fresh connection means a fresh session: stale pending entries
            // from the previous gate must not swallow replies on this one.
            _pending.Clear();
            _pendingIdsByMsg.Clear();
            TokenVerified = false;
            _gate = fresh;
            _disconnectNotificationSent = false;
        }

        private static byte[] SafeBase64(string s)
        {
            if (string.IsNullOrEmpty(s)) return Array.Empty<byte>();
            // Java/Jackson emits standard padded base64; .NET wants no
            // surrounding whitespace.
            try { return Convert.FromBase64String(s.Trim()); }
            catch { return Array.Empty<byte>(); }
        }

        private static IEnumerator WaitMs(long ms)
        {
            float until = Time.realtimeSinceStartup + ms / 1000f;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        private static string DescribeLoginCode(GatewayLoginResult r) => r.code switch
        {
            GatewayLoginResult.CodeQueueTimeout => "排队超时,请稍后重试",
            GatewayLoginResult.CodeAuthRejected => "账号或密码错误",
            GatewayLoginResult.CodeRateLimited  => "操作过于频繁,请稍后重试",
            _ => $"登录失败({(string.IsNullOrEmpty(r.message) ? r.code.ToString() : r.message)})",
        };

        private static string DescribeAssignCode(AssignGateResult r) => r.code switch
        {
            AssignGateResult.CodeZoneNotFound    => "该区服不存在",
            AssignGateResult.CodeZoneUnavailable => DescribeZoneUnavailable(r.error),
            AssignGateResult.CodeRateLimited     => "操作过于频繁,请稍后重试",
            _ => $"分配服务器失败({(string.IsNullOrEmpty(r.error) ? r.code.ToString() : r.error)})",
        };

        private static string DescribeZoneUnavailable(string tag) => tag switch
        {
            "zone_maintenance" => "该区服正在维护中",
            "zone_closed"      => "该区服已关闭",
            "zone_not_open"    => "该区服尚未开放",
            _ => "该区服暂不可用",
        };

        private void FailPipeline(int gen, Action<string> onError, string message)
        {
            if (gen != _pipelineGen) return;
            ResetConnectionState();
            onError?.Invoke(message);
        }

        /// <summary>
        /// 请求-响应调用(协程,15s 超时)。public 供 BattleClient 等子系统经
        /// 传输接口复用同一条管线;回调全部落在主线程(Tick 驱动)。
        /// </summary>
        public IEnumerator Call<TResp>(uint messageId, IMessage request,
                                       MessageParser<TResp> parser,
                                       Action<TResp> onResp,
                                       Action<string> onError)
            where TResp : IMessage<TResp>
        {
            var gate = _gate;
            if (gate == null || !gate.Connected) { onError("not connected"); yield break; }

            ulong id = (ulong)Interlocked.Increment(ref _seq);
            var clientReq = new ClientRequest
            {
                Id = id,
                MessageId = messageId,
                Body = request.ToByteString(),
            };

            bool done = false;
            _pending[id] = new PendingCall
            {
                MessageId = messageId,
                Callback = mc =>
                {
                    done = true;
                    if (mc.ErrorMessage != null && mc.ErrorMessage.Id != 0)
                    {
                        onError($"server tip={mc.ErrorMessage.Id}");
                        return;
                    }
                    try { onResp(parser.ParseFrom(mc.SerializedMessage)); }
                    catch (Exception ex) { onError($"parse response: {ex.Message}"); }
                },
            };
            if (!_pendingIdsByMsg.TryGetValue(messageId, out var fifo))
                _pendingIdsByMsg[messageId] = fifo = new List<ulong>();
            fifo.Add(id);

            try
            {
                gate.Send(clientReq);
            }
            catch (Exception ex)
            {
                RemovePending(id, messageId);
                onError($"send failed: {ex.Message}");
                if (ReferenceEquals(_gate, gate))
                    HandleTransportDisconnected();
                yield break;
            }
            float deadline = Time.realtimeSinceStartup + 15f;
            while (!done && Time.realtimeSinceStartup < deadline)
            {
                Tick();
                if (!done && (!ReferenceEquals(_gate, gate) || !gate.Connected))
                {
                    RemovePending(id, messageId);
                    onError("disconnected");
                    yield break;
                }
                yield return null;
            }
            if (!done) { RemovePending(id, messageId); onError("rpc timeout"); }
        }

        private void RemovePending(ulong id, uint messageId)
        {
            _pending.Remove(id);
            if (_pendingIdsByMsg.TryGetValue(messageId, out var fifo))
                fifo.Remove(id);
        }

        /// <summary>单向发送(fire-and-forget)。public 理由同 <see cref="Call{TResp}"/>。</summary>
        public void SendOneWay(uint messageId, IMessage request)
            => TrySendOneWay(messageId, request);

        private bool TrySendOneWay(uint messageId, IMessage request)
        {
            var gate = _gate;
            if (gate == null || !gate.Connected) return false;
            ulong id = (ulong)Interlocked.Increment(ref _seq);
            try
            {
                gate.Send(new ClientRequest
                {
                    Id = id,
                    MessageId = messageId,
                    Body = request.ToByteString(),
                });
                return true;
            }
            catch (Exception ex)
            {
                Log($"[gate] send skipped: {ex.Message}");
                if (ReferenceEquals(_gate, gate))
                    HandleTransportDisconnected();
                return false;
            }
        }

        private void DispatchInbound(IMessage msg)
        {
            // Token verify response is its own top-level type, not a MessageContent.
            if (msg is ClientTokenVerifyResponse tvr)
            {
                if (tvr.Success) TokenVerified = true;
                else Log($"[gate] token rejected: {tvr.Error}");
                return;
            }
            if (msg is GateTcpClient.DisconnectedSentinel) return;
            if (msg is not MessageContent mc) return;

            // Tier 1: exact request-id match (scene路径回显 id).
            if (mc.Id != 0 && _pending.Remove(mc.Id, out var byId))
            {
                if (_pendingIdsByMsg.TryGetValue(byId.MessageId, out var f)) f.Remove(mc.Id);
                byId.Callback(mc);
                return;
            }
            // Tier 2: FIFO match by message_id (gRPC路径 id==0).
            if (mc.Id == 0 &&
                _pendingIdsByMsg.TryGetValue(mc.MessageId, out var fifo) && fifo.Count > 0)
            {
                ulong id = fifo[0];
                fifo.RemoveAt(0);
                if (_pending.Remove(id, out var byMsg))
                {
                    byMsg.Callback(mc);
                    return;
                }
            }
            if (_notifyHandlers.TryGetValue(mc.MessageId, out var nh)) nh(mc);
            else Log($"[unhandled] message_id={mc.MessageId} bytes={mc.SerializedMessage.Length}");
        }

        private void WireSceneNotifyHandlers()
        {
            RegisterMovementReply(MessageIds.MoveStart, "MoveStart");
            RegisterMovementReply(MessageIds.MoveSync, "MoveSync");
            RegisterMovementReply(MessageIds.MoveStop, "MoveStop");

            OnNotify(MessageIds.NotifyEnterScene, mc =>
            {
                var ev = EnterSceneS2C.Parser.ParseFrom(mc.SerializedMessage);
                CurrentSceneId = ev.SceneInfo?.SceneId ?? 0;
                CurrentSceneConfigId = ev.SceneInfo?.SceneConfigId ?? 0;
                _enteredScene = true;
                Log($"[scene] entered scene_id={CurrentSceneId}, config={CurrentSceneConfigId}");
                World.Clear();
                OnSceneEntered?.Invoke(ev.SceneInfo);
            });

            // Cross-zone / gate migration: the server hands us a fresh gate
            // endpoint + token; connect to the new gate, then drop the old link
            // and re-run the whole connect pipeline there. 详见 RedirectFlow 上方的注释。
            //
            // 这里是 msg 124 在本工程里**唯一**的处理器。Net/Generated/Handlers 下那个
            // SceneClientPlayerCommonRedirectToGateHandler 是 protogen 出的空壳:它要靠
            // Net/Generated/HandlerRegistry.Register(client) 才会挂上,而本工程从未调用过
            // 那个 Register(全仓唯一出现处就是它自己的定义)。真正的注册表是这里的 OnNotify。
            // ⚠️ OnNotify 是覆盖语义:一旦有人把 HandlerRegistry.Register 接上,生成的空壳
            // 会**静默盖掉**下面这个处理器,重定向就又变回"服务端推了、客户端什么都不做"。
            // 要接生成层的话,必须让它在 WireSceneNotifyHandlers 之前跑,或者干脆别接。
            OnNotify(MessageIds.RedirectToGate, mc =>
            {
                var ev = RedirectToGateNotify.Parser.ParseFrom(mc.SerializedMessage);
                if (_redirecting)
                {
                    // 已经在搬了,重复推送忽略即可 —— 玩家没有被丢下,不算静默丢弃。
                    Log($"[gate] redirect ignored (already redirecting) target={ev.TargetIp}:{ev.TargetPort}");
                    return;
                }
                if (CoroutineRunner == null)
                {
                    // 没有协程宿主 = 没人能跑这套流程。老 gate 的登录会话早已被 EnterGame 清掉,
                    // 静默丢弃就是把玩家永久钉死在一条哑连接上(重试只会撞 kLoginSessionNotFound)。
                    // 宁可炸出来并断线,让外层重连兜底。
                    LogError($"[gate] redirect dropped: no coroutine runner (target={ev.TargetIp}:{ev.TargetPort})");
                    DisconnectInternal(notify: true, forceNotification: true);
                    return;
                }
                CoroutineRunner(RedirectFlow(ev));
            });

            // 战斗落点分配(§18 D26):开局 / 观战接入时 battle 节点经大厅推来
            // host:port + 票据,直连链路据此建第二条连接。同 message_id 只能有一个处理器,
            // 这里是唯一注册点,Battle / Spectate 都不要再抢注。
            OnNotify(MessageIds.NotifyBattleAssigned, mc =>
                BattleLink.HandleAssigned(BattleAssignedS2C.Parser.ParseFrom(mc.SerializedMessage)));

            // Fire-and-forget TCP token refresh replies come back with id==0
            // and no pending entry (SendOneWay) — capture the rotated pair here.
            OnNotify(MessageIds.RefreshToken, mc =>
            {
                var r = RefreshTokenResponse.Parser.ParseFrom(mc.SerializedMessage);
                if (!string.IsNullOrEmpty(r.AccessToken))
                {
                    SetTokens(r.AccessToken, r.RefreshToken, r.AccessTokenExpire);
                    Log("access_token refreshed");
                }
            });

            OnNotify(MessageIds.NotifyActorCreate, mc =>
            {
                var ev = ActorCreateS2C.Parser.ParseFrom(mc.SerializedMessage);
                SpawnActorView(ev);
            });

            OnNotify(MessageIds.NotifyActorListCreate, mc =>
            {
                var ev = ActorListCreateS2C.Parser.ParseFrom(mc.SerializedMessage);
                foreach (var a in ev.ActorList) SpawnActorView(a);
                Log($"[scene] +{ev.ActorList.Count} actors");
            });

            OnNotify(MessageIds.NotifyActorDestroy, mc =>
            {
                var ev = ActorDestroyS2C.Parser.ParseFrom(mc.SerializedMessage);
                World.DespawnActor(ev.Entity);
            });

            OnNotify(MessageIds.NotifyActorListDestroy, mc =>
            {
                var ev = ActorListDestroyS2C.Parser.ParseFrom(mc.SerializedMessage);
                foreach (var e in ev.Entity) World.DespawnActor(e);
            });

            OnNotify(MessageIds.NotifySkillUsed, mc =>
            {
                var ev = SkillUsedS2C.Parser.ParseFrom(mc.SerializedMessage);
                Log($"[skill] caster_entity={ev.Entity} skill={ev.SkillTableId} targets={ev.TargetEntity.Count}");
                if (World.TryGetActor(ev.Entity, out var caster))
                {
                    SkillFx.PlayCast(caster.Go);
                    foreach (var tid in ev.TargetEntity)
                    {
                        if (World.TryGetActor(tid, out var tv))
                        {
                            SkillFx.PlayBeam(caster.Go.transform.position + UnityEngine.Vector3.up,
                                             tv.Go.transform.position + UnityEngine.Vector3.up,
                                             new Color(1f, 0.9f, 0.3f));
                            SkillFx.PlayHit(tv.Go, new Color(1f, 0.2f, 0.2f));
                        }
                    }
                }
            });

            OnNotify(MessageIds.NotifySkillInterrupted, mc =>
            {
                var ev = SkillInterruptedS2C.Parser.ParseFrom(mc.SerializedMessage);
                Log($"[skill] interrupted entity={ev.Entity} skill={ev.SkillTableId} reason={ev.ReasonCode}");
            });

            OnNotify(MessageIds.NotifyActorMove, mc =>
            {
                var ev = ActorMoveS2C.Parser.ParseFrom(mc.SerializedMessage);
                ApplyServerMove(ev);
            });

            OnNotify(MessageIds.NotifyActorMoveList, mc =>
            {
                var ev = ActorMoveListS2C.Parser.ParseFrom(mc.SerializedMessage);
                foreach (var m in ev.Moves) ApplyServerMove(m);
            });

            OnNotify(MessageIds.NotifyTeleport, mc =>
            {
                var ev = TeleportS2C.Parser.ParseFrom(mc.SerializedMessage);
                var pos = WorldCoordinateConverter.FromServerVector(ev.Transform?.Location);
                var euler = WorldCoordinateConverter.FromServerRotation(ev.Transform?.Rotation);
                World.Teleport(ev.Entity, pos, euler);
                Log($"[move] teleport entity={ev.Entity} reason={ev.Reason} input_seq<={ev.InputSeq}");
            });

            OnNotify(MessageIds.NotifyMoveAck, mc =>
            {
                var ev = MoveAckS2C.Parser.ParseFrom(mc.SerializedMessage);
                MoveAckCount++;
                // For now we just trust the server; a full client-prediction
                // pipeline would rewind/replay any pending input > ev.InputSeq.
                if (!World.HasLocalPlayer) return;
                var pos = WorldCoordinateConverter.FromServerLocation(ev.ServerLocation);
                var local = GetActorPos(World.LocalEntity);
                var dist = UnityEngine.Vector3.Distance(pos, local);
                // Every ack is a server-side correction (the server only sends
                // one when its verdict differs from the report), so log the
                // coordinates: diagnosing "pulled back to where?" needs them.
                Log($"[move] ack input_seq={ev.InputSeq} server={FormatServer(ev.ServerLocation)} " +
                    $"unity={FormatUnity(pos)} local={FormatUnity(local)} dist={FormatF2(dist)} moving={_isMoving}");
                // While moving, small disagreements stay inside the prediction
                // dead-band (the next report re-converges). While stationary
                // there is nothing to predict, so any server verdict is final:
                // a MoveStop that the server clamped at a wall must land the
                // actor on the server's point, not leave it 0.3-1.5 m inside.
                var settle = !_isMoving && dist > 0.05f;
                if (dist > 1.5f || settle)
                {
                    World.Teleport(World.LocalEntity, pos, GetActorEuler(World.LocalEntity));
                    MoveReconcileCount++;
                    Log($"[move] reconcile {(dist > 1.5f ? "snap" : "settle")} input_seq={ev.InputSeq} to={FormatUnity(pos)} " +
                        $"after={FormatUnity(GetActorPos(World.LocalEntity))}");
                    OnMoveReconcile?.Invoke(ev.InputSeq, pos);
                }
            });

            OnNotify(MessageIds.TipToClient, mc =>
            {
                var tip = TipInfoMessage.Parser.ParseFrom(mc.SerializedMessage);
                Log($"[tip] id={tip.Id}");
            });

            OnNotify(MessageIds.KickPlayer, _ =>
            {
                Log("[gate] kicked by server");
                Disconnect();
            });
        }

        private void RegisterMovementReply(uint messageId, string name)
        {
            OnNotify(messageId, mc =>
            {
                var tipId = mc.ErrorMessage?.Id ?? 0;
                if (tipId != 0)
                    Log($"[movement] {name} rejected: tip={tipId}");
            });
        }

        private void SpawnActorView(ActorCreateS2C ev)
        {
            var loc = ev.Transform?.Location;
            var rot = ev.Transform?.Rotation;
            // UE-style world (X forward, Y right, Z up) -> Unity (X right, Y up, Z forward).
            // Pick a stable mapping: UE.X -> Unity.Z, UE.Y -> Unity.X, UE.Z -> Unity.Y.
            var pos = WorldCoordinateConverter.FromServerVector(loc);
            var euler = WorldCoordinateConverter.FromServerRotation(rot);

            var kind = ev.ActorType switch
            {
                ActorType.Player => ActorKind.Player,
                ActorType.Npc => ActorKind.Npc,
                _ => ActorKind.Unknown,
            };
            World.SpawnActor(ev.Entity, kind, ev.ConfigId, pos, euler);

            // Local player binding: server uses `guid == player_id` for the
            // owning client's actor.
            if (kind == ActorKind.Player && ev.Guid == PlayerId && PlayerId != 0)
            {
                // The authoritative enter position: the server validates it
                // against the scene navmesh and respawns illegal saves before
                // sending this, so it should already be walkable client-side.
                Log($"[actor] self entity={ev.Entity} server={FormatServer(loc)} unity={FormatUnity(pos)}");
                World.SetLocalPlayer(ev.Entity);
            }
        }

        // ── Movement diagnostics ─────────────────────────────────
        // Counters/events for automated acceptance (DevAutoPilot -moveTest):
        // an ack means the server disagreed with a report; a reconcile means
        // the disagreement was large enough (>1.5m) to snap the local actor.

        public int MoveAckCount { get; private set; }
        public int MoveReconcileCount { get; private set; }
        public event Action<uint, UnityEngine.Vector3> OnMoveReconcile;

        // Machine-parsed by tools/run_move_test.ps1: always '.' decimals, whatever the OS locale.
        private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        private static string FormatF2(double d) => d.ToString("F2", Inv);

        private static string FormatUnity(UnityEngine.Vector3 v)
            => string.Format(Inv, "({0:F2},{1:F2},{2:F2})", v.x, v.y, v.z);

        private static string FormatServer(global::Location l)
            => l == null ? "(null)" : string.Format(Inv, "({0:F2},{1:F2},{2:F2})", l.X, l.Y, l.Z);

        private static string FormatServer(global::Vector3 v)
            => v == null ? "(null)" : string.Format(Inv, "({0:F2},{1:F2},{2:F2})", v.X, v.Y, v.Z);

        private void ApplyServerMove(ActorMoveS2C ev)
        {
            var pos = WorldCoordinateConverter.FromServerVector(ev.Transform?.Location);
            var euler = WorldCoordinateConverter.FromServerRotation(ev.Transform?.Rotation);
            var velocity = WorldCoordinateConverter.FromServerVelocity(ev.Velocity);
            World.ApplyMove(ev.Entity, pos, euler, velocity);
        }

        private UnityEngine.Vector3 GetActorPos(ulong entity)
        {
            if (!World.TryGetActor(entity, out var v) || v.Go == null)
                return UnityEngine.Vector3.zero;

            var movement = v.Go.GetComponent<MmorpgClient.World.Tianyong.TianyongPlayerController>();
            if (movement == null) return v.Go.transform.localPosition;
            return World.Root != null
                ? World.Root.InverseTransformPoint(movement.FeetPosition)
                : movement.FeetPosition;
        }

        private UnityEngine.Vector3 GetActorEuler(ulong entity)
            => World.TryGetActor(entity, out var v) && v.Go != null
                ? v.Go.transform.localEulerAngles : UnityEngine.Vector3.zero;

        private void MaybeRefreshToken()
        {
            if (!IsGateReady) return;
            if (string.IsNullOrEmpty(RefreshToken) || _accessTokenExpire == 0) return;
            long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (_accessTokenExpire - nowSec > 10 * 60) return;          // >10min left
            if (Time.realtimeSinceStartup - _lastRefreshAttempt < 60f) return; // 60s cooldown
            _lastRefreshAttempt = Time.realtimeSinceStartup;
            Log("refreshing access_token");
            // Fire-and-forget; the rotated pair is captured by the
            // MessageIds.RefreshToken notify handler above.
            SendOneWay(MessageIds.RefreshToken, new RefreshTokenRequest { RefreshToken = RefreshToken });
        }

        private void Log(string s) => OnLog?.Invoke(s);
        private void Status(string s) { OnFlowStatus?.Invoke(s); Log(s); }

        /// <summary>
        /// 会让会话报废的错误走这里,要炸得看得见:Debug.LogError 在编辑器 Console 里是红的,
        /// 也会带堆栈进播放器日志 —— 跨区验证脚本(tools/run_crosszone_pair.ps1)与
        /// tools/build_crosszone_player.ps1 都是直接 grep 播放器日志的。同时照常走 OnLog,
        /// 挂在上面的文件日志(MmorpgLogger)与 UI 一条都不少。
        /// </summary>
        private void LogError(string s)
        {
            OnLog?.Invoke(s);
            Debug.LogError("[GameClient] " + s);
        }

        private void HandleTransportDisconnected()
            => DisconnectInternal(notify: true, forceNotification: false);

        private void ResetConnectionState()
            => DisconnectInternal(notify: false, forceNotification: false);

        public void Disconnect()
            => DisconnectInternal(notify: true, forceNotification: false);

        private void DisconnectInternal(bool notify, bool forceNotification)
        {
            if (_disconnectInProgress) return;

            var hadSessionState = _gate != null || TokenVerified || InGame ||
                                  CurrentSceneId != 0 || CurrentSceneConfigId != 0 ||
                                  World.Actors.Count != 0;
            var shouldNotify = notify && !_disconnectNotificationSent &&
                               (forceNotification || hadSessionState);

            _disconnectInProgress = true;
            if (shouldNotify) _disconnectNotificationSent = true;
            try
            {
                // Make outward-facing state unavailable before disabling the
                // local movement component. Its OnDisable may attempt a final
                // MoveStop, which must now become a harmless no-op.
                var gate = _gate;
                _gate = null;
                InGame = false;
                TokenVerified = false;
                CurrentSceneId = 0;
                CurrentSceneConfigId = 0;
                PlayerId = 0;
                _enteredScene = false;
                _isMoving = false;
                _moveInputSeq = 0;
                _pending.Clear();
                _pendingIdsByMsg.Clear();
                gate?.Dispose();
                // 大厅会话没了,战斗直连的票据与路由也随之作废(§18.2:两连接互不拆台,但
                // 直连的身份来自大厅会话;重连后 scene 推 NotifyBattleReconnect 再补签重建)
                if (hadSessionState) BattleLink?.Close("lobby_disconnected");
                World.Clear();
            }
            finally
            {
                _disconnectInProgress = false;
            }

            if (shouldNotify) OnDisconnected?.Invoke();
        }
    }
}
