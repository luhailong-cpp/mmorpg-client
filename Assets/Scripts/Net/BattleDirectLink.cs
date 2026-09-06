using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Google.Protobuf;

namespace MmorpgClient.Net
{
    /// <summary>
    /// 战斗直连链路:客户端的第二条 TCP 连接,直连 battle 节点客户端面,票据入场
    /// (turn-based-battle-server.md §18,契约见 §18.2)。大厅那条连接照旧走 gate。
    ///
    /// 生命周期(全部在主线程,由 <see cref="Tick"/> 驱动):
    ///   NotifyBattleAssigned(经大厅)→ <see cref="HandleAssigned"/> → 连 host:port
    ///   → 首包 BattleTokenVerifyRequest → BattleTokenVerifyResponse.success → Verified
    ///   → 四条战斗 RPC 与本人的 S2C 从此走这条连接(分流在 DirectRoutingBattleTransport)
    ///   → 终局包 / 观战结束 / 退出观战之后服务端 FIN → 正常收尾(不重连)。
    ///
    /// 恢复策略(§18.2):
    ///   * 战斗中意外断开:票据未过期则用同一张票重连 1 次(D25 同票可重用);
    ///     再失败或票已过期 → 经大厅通道 RequestBattleTicket 补签 1 次;仍失败 → Closed。
    ///   * 握手被拒(签名 / 期限 / 名单):同票不再重试,直接补签。
    ///   * 链路 Closed 期间战斗照打:传输层自动回落到 gate 中继(D23 双通道并存)。
    ///
    /// 本类不引用 UnityEngine:时钟由宿主注入(GameClient.Tick 传 Time.realtimeSinceStartup),
    /// 连接由 <see cref="IFramedConnection"/> 工厂产出,EditMode 测试用假连接穷打状态机。
    /// </summary>
    public sealed class BattleDirectLink
    {
        public enum LinkState
        {
            /// <summary>无活动连接(初始态,或等待重连倒计时)。</summary>
            Idle,
            /// <summary>TCP 连接进行中(连接动作在后台线程,结果经队列回主线程)。</summary>
            Connecting,
            /// <summary>已连上,握手包已发,等 BattleTokenVerifyResponse。</summary>
            Handshaking,
            /// <summary>票据校验通过,战斗消息可走本链路。</summary>
            Verified,
            /// <summary>本局链路终结(正常收尾 / 恢复失败 / 宿主关闭);下一份分配包会重新打开。</summary>
            Closed,
        }

        /// <summary>握手期限(秒),与服务端 BattleClientEdge::kHandshakeTimeoutSec 同值。</summary>
        public const double HandshakeTimeoutSeconds = 10.0;
        /// <summary>
        /// 建连期限(秒)。系统 SYN 超时(Windows 约 21s)不可配且远长于一局回合的容忍度,
        /// 这里自己封顶,超时按意外断开走恢复。
        /// </summary>
        public const double ConnectTimeoutSeconds = 10.0;
        /// <summary>请求-响应超时(秒),与 GameClient.Call 同口径。</summary>
        public const double CallTimeoutSeconds = 15.0;
        /// <summary>意外断开后同票重连的等待(秒)。</summary>
        public const double ReconnectDelaySeconds = 1.0;
        /// <summary>同一张票最多重试次数(§18.2:不得超过 1 次)。</summary>
        public const int MaxSameTicketRetries = 1;
        /// <summary>每局最多补签次数;补签失败即放弃直连,战斗经 gate 回落打完。</summary>
        public const int MaxReissuesPerBattle = 1;
        /// <summary>
        /// 传输层失败(而非服务端业务拒绝)的错误串前缀。分流层据此判断
        /// 「这次失败是直连没送到,不是服务端说不行」,进而决定要不要改走大厅重发
        /// (只对幂等 RPC 这么做,见 DirectRoutingBattleTransport)。
        /// </summary>
        public const string TransportErrorPrefix = "link: ";

        private sealed class PendingCall
        {
            public double Deadline;
            public Action<MessageContent> Callback;
            public Action<string> OnError;
        }

        private sealed class ConnectResult
        {
            public int Job;
            public IFramedConnection Conn;
            public Exception Error;
        }

        private readonly Func<IFramedConnection> _factory;
        private readonly Action<Action> _connectRunner;
        private readonly Action<string> _log;
        private readonly Func<ulong> _unixNowMs;

        private readonly ConcurrentQueue<ConnectResult> _connectResults = new();
        private readonly Dictionary<ulong, PendingCall> _pending = new();
        private readonly Dictionary<uint, Action<MessageContent>> _notify = new();

        private IFramedConnection _conn;
        private int _connectJob;          // 递增:作废晚到的连接结果
        private int _epoch;               // 递增:作废晚到的补签回调
        private double _now;
        private double _handshakeDeadline;
        private double _connectDeadline;   // >0 = Connecting 的建连期限
        private double _reconnectAt;      // >0 = 到点用同票重连
        private long _seq;

        private BattleAssignedS2C _assignment;
        private ulong _targetBattleId;    // 本链路服务的战斗(分配包或重连提示带来)
        private int _sameTicketRetries;
        private int _reissues;
        private bool _reissueInFlight;
        private bool _ended;              // 本局已结束 / 已退出:之后的 FIN 是正常收尾
        private bool _closing;            // 宿主 Close 进行中:断开回调不得触发恢复

        public LinkState State { get; private set; } = LinkState.Idle;

        public bool IsVerified => State == LinkState.Verified && _conn != null;

        /// <summary>
        /// 「这条链路还有活的连接对象」——判据必须同时看 State 与 _conn。
        /// TeardownConnection 只清 _conn 不改 State,所以单看 State 会把
        /// 「已拆连接但状态还停在 Verified/Handshaking」误判成健康,导致新到的分配包
        /// 被当成幂等重推丢掉、链路再也建不起来(评审确认项)。
        /// </summary>
        private bool HasLiveConnection =>
            _conn != null &&
            (State == LinkState.Connecting || State == LinkState.Handshaking || State == LinkState.Verified);

        /// <summary>本链路服务的战斗 id;0 = 无。</summary>
        public ulong BattleId => _targetBattleId;

        public eBattleTicketRole Role => _assignment?.Role ?? eBattleTicketRole.BattleTicketRoleNone;

        /// <summary>最近一份落点分配(含票据);Close 后为 null。</summary>
        public BattleAssignedS2C Assignment => _assignment;

        /// <summary>握手通过(参数 battle_id)。重连成功也会再次触发。</summary>
        public event Action<ulong> OnVerified;

        /// <summary>链路终结(参数 reason)。正常收尾也触发。</summary>
        public event Action<string> OnClosed;

        /// <summary>
        /// 宿主提供的补签通道:经大厅会话调 MatchService.RequestBattleTicket(battle_id)。
        /// 返回 false = 此刻发不出(大厅未就绪),链路直接 Closed。回调必须回到主线程。
        /// </summary>
        public Func<ulong, Action<BattleAssignedS2C>, Action<string>, bool> TicketReissuer { get; set; }

        /// <param name="factory">产出一条未连接的分帧连接(生产:new GateTcpClient(codec))。</param>
        /// <param name="log">日志汇;可空。</param>
        /// <param name="unixNowMs">票据过期判定用的 Unix 毫秒时钟;空 = 系统时钟。</param>
        /// <param name="connectRunner">
        /// 执行阻塞的 Connect 的方式;空 = 线程池(TcpClient.Connect 对不可达地址会卡到系统超时,
        /// 不能占主线程)。测试传 <c>a =&gt; a()</c> 让连接同步完成。
        /// </param>
        public BattleDirectLink(Func<IFramedConnection> factory, Action<string> log = null,
                                Func<ulong> unixNowMs = null, Action<Action> connectRunner = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _log = log ?? (_ => { });
            _unixNowMs = unixNowMs ?? (() => (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _connectRunner = connectRunner ?? (a => ThreadPool.QueueUserWorkItem(_ => a()));
        }

        // ── 宿主入口 ────────────────────────────────────────

        /// <summary>
        /// 收到落点分配(开局 / 观战接入的 NotifyBattleAssigned,或补签响应)。
        /// 同一局已在连接 / 已验证:只更新票据(重连时用最新的),不重新建连;
        /// 新的一局或链路已终结:关闭旧连接,按新分配建连。
        /// </summary>
        public void HandleAssigned(BattleAssignedS2C assigned)
        {
            if (assigned == null || assigned.BattleId == 0 || string.IsNullOrEmpty(assigned.Host) || assigned.Port == 0)
            {
                _log("落点分配包不完整,忽略");
                return;
            }

            bool sameBattle = assigned.BattleId == _targetBattleId;
            if (sameBattle && HasLiveConnection)
            {
                _assignment = assigned; // 幂等重推 / 补签:留最新票据供重连
                return;
            }

            // 无论换局还是同局重建,都必须作废在途补签:它的回调里带着旧 epoch,
            // 失败分支会 Finish() 掉这里刚建起来的连接(评审确认项)。
            _epoch++;
            _reissueInFlight = false;
            if (!sameBattle)
            {
                // 换局:上一局的恢复预算与结束标记全部作废
                _reissues = 0;
                _targetBattleId = assigned.BattleId;
            }
            _assignment = assigned;
            _ended = false;
            _sameTicketRetries = 0; // 新票据:重新给 1 次同票重试
            OpenConnection();
        }

        /// <summary>
        /// 本局结束 / 退出观战:之后的服务端 FIN 是正常收尾,不重连、不补签。
        /// battleId=0 表示"当前这局"。
        /// </summary>
        public void HandleBattleEnded(ulong battleId)
        {
            if (battleId != 0 && battleId != _targetBattleId) return;
            _ended = true;
            _reconnectAt = 0;
            if (State == LinkState.Idle && _targetBattleId != 0)
            {
                // 正在等重连倒计时:战斗已结束,不必再连
                Finish("battle_ended");
            }
        }

        /// <summary>
        /// 大厅重连后 scene 推 NotifyBattleReconnect:手里没有可用票据(大厅断线时链路已 Close),
        /// 经大厅通道补签一张再建连。已在该局验证过则无事。
        /// </summary>
        public void HandleReconnectHint(ulong battleId)
        {
            if (battleId == 0) return;
            if (battleId == _targetBattleId && HasLiveConnection) return;
            // 同一局的补签已在途:重连提示可能来自两处(GameClient 重定向收尾主动补 +
            // 服务端 NotifyBattleReconnect),不能各发一次 RequestBattleTicket。
            if (battleId == _targetBattleId && _reissueInFlight) return;

            _epoch++;
            TeardownConnection();
            _assignment = null;
            _targetBattleId = battleId;
            _ended = false;
            _sameTicketRetries = 0;
            _reissues = 0;
            _reissueInFlight = false;
            _reconnectAt = 0;
            State = LinkState.Idle;
            TryReissue("reconnect_hint");
        }

        /// <summary>宿主主动关闭(大厅断线 / 换会话):不触发任何恢复,下一份分配包会重新打开。</summary>
        public void Close(string reason)
        {
            _closing = true;
            try
            {
                _epoch++;
                TeardownConnection();
                FailPending("closed");
                _reconnectAt = 0;
                _handshakeDeadline = 0;
                _connectDeadline = 0;
                _reissueInFlight = false;
                if (State != LinkState.Closed && _targetBattleId != 0) Finish(reason);
                State = LinkState.Closed;
            }
            finally
            {
                _closing = false;
            }
            _assignment = null;
            _targetBattleId = 0;
            _ended = false;
            _sameTicketRetries = 0;
            _reissues = 0;
        }

        /// <summary>宿主每帧驱动(主线程)。nowSeconds 单调递增即可。</summary>
        public void Tick(double nowSeconds)
        {
            _now = nowSeconds;

            // 先处理到点的重连,再收连接结果:同步完成的连接(测试 / 本机极快建连)在同一帧内
            // 就能发出握手包,不必多等一帧。
            if (_reconnectAt > 0 && _now >= _reconnectAt)
            {
                _reconnectAt = 0;
                if (_assignment != null && !_ended) OpenConnection();
            }

            while (_connectResults.TryDequeue(out var result))
            {
                if (result.Job != _connectJob || !ReferenceEquals(result.Conn, _conn))
                {
                    // 晚到的结果:该连接已被换掉,连上了也要关掉,别漏 socket
                    try { result.Conn?.Dispose(); } catch { }
                    continue;
                }
                if (result.Error != null)
                {
                    _log($"连接失败: {result.Error.Message}");
                    HandleDisconnected(result.Conn, "connect_failed");
                    continue;
                }
                BeginHandshake();
            }

            _conn?.Poll();

            // 建连期限:TcpClient.Connect 对不可达 endpoint 会卡到系统 SYN 超时(Windows 约 21s,
            // 且不可配),期间 State 停在 Connecting、既不重试也不回落。battle 节点端口不可达
            // (NodePort 未开 / Pod 刚重建)是现实场景,必须自己封顶(评审确认项)。
            // 建连期限在**第一次 Tick 观察到 Connecting 时**起算,不在 OpenConnection 里算:
            // OpenConnection 常常从 notify 处理器(Tick 之外)调用,那时 _now 还是上一帧的值,
            // 直接 _now + 超时会把期限算早,极端情况(链路从未 Tick 过)当场就判超时。
            if (State == LinkState.Connecting && _connectDeadline <= 0)
            {
                _connectDeadline = _now + ConnectTimeoutSeconds;
            }
            else if (State == LinkState.Connecting && _now >= _connectDeadline)
            {
                // 作废这条在途连接:TeardownConnection 会 Dispose 它并 bump job,
                // 卡在 Connect 上的 worker 线程返回后,它的结果在 drain 分支被判为陈旧、
                // 再 Dispose 一次(幂等),不漏 socket。
                _log($"建连超时({ConnectTimeoutSeconds:0.#}s) battle_id={_targetBattleId}");
                _connectDeadline = 0;
                TeardownConnection();
                FailPending("connect_timeout");
                if (_ended) { Finish("battle_ended"); }
                else { State = LinkState.Idle; ScheduleRecovery("connect_timeout"); }
                return;
            }

            if (State == LinkState.Handshaking && _handshakeDeadline > 0 && _now >= _handshakeDeadline)
            {
                FailHandshake("handshake_timeout", retrySameTicket: true);
            }

            if (_pending.Count > 0)
            {
                List<ulong> expired = null;
                foreach (var kv in _pending)
                {
                    if (kv.Value.Deadline <= _now) (expired ??= new List<ulong>()).Add(kv.Key);
                }
                if (expired != null)
                {
                    foreach (var id in expired)
                    {
                        if (_pending.Remove(id, out var pc)) pc.OnError?.Invoke(TransportErrorPrefix + "rpc timeout");
                    }
                }
            }
        }

        // ── IBattleTransport 用到的三件事 ─────────────────────

        /// <summary>注册本链路上的 S2C 推送处理器(同 messageId 只保留最后一次)。</summary>
        public void RegisterNotify(uint messageId, Action<MessageContent> handler)
            => _notify[messageId] = handler;

        /// <summary>请求-响应(仅 Verified 时可用;分流层保证不在其它状态调用)。</summary>
        public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                                Action<TResp> onResponse, Action<string> onError)
            where TResp : IMessage<TResp>
        {
            var conn = _conn;
            if (!IsVerified || conn == null)
            {
                onError?.Invoke(TransportErrorPrefix + "not verified");
                return;
            }

            ulong id = (ulong)(++_seq);
            _pending[id] = new PendingCall
            {
                Deadline = _now + CallTimeoutSeconds,
                OnError = onError,
                Callback = mc =>
                {
                    if (mc.ErrorMessage != null && mc.ErrorMessage.Id != 0)
                    {
                        onError?.Invoke($"server tip={mc.ErrorMessage.Id}");
                        return;
                    }
                    try { onResponse?.Invoke(parser.ParseFrom(mc.SerializedMessage)); }
                    catch (Exception ex) { onError?.Invoke($"parse response: {ex.Message}"); }
                },
            };

            if (!TrySend(conn, new ClientRequest { Id = id, MessageId = messageId, Body = request.ToByteString() }))
            {
                _pending.Remove(id);
                onError?.Invoke(TransportErrorPrefix + "send failed");
                HandleDisconnected(conn, "send_failed"); // 发不出去 = 连接已死,走恢复
            }
        }

        /// <summary>单向发送(仅 Verified 时生效;其它状态静默丢弃,由分流层回落 gate)。</summary>
        public void SendOneWay(uint messageId, IMessage request)
        {
            var conn = _conn;
            if (!IsVerified || conn == null) return;
            ulong id = (ulong)(++_seq);
            if (!TrySend(conn, new ClientRequest { Id = id, MessageId = messageId, Body = request.ToByteString() }))
            {
                HandleDisconnected(conn, "send_failed");
            }
        }

        // ── 连接生命周期 ────────────────────────────────────

        private void OpenConnection()
        {
            TeardownConnection();
            var assignment = _assignment;
            if (assignment == null) { Finish("no_assignment"); return; }

            IFramedConnection conn;
            try { conn = _factory(); }
            catch (Exception ex)
            {
                _log($"创建连接失败: {ex.Message}");
                ScheduleRecovery("factory_failed");
                return;
            }

            _conn = conn;
            State = LinkState.Connecting;
            _handshakeDeadline = 0;
            _connectDeadline = 0;   // 由 Tick 首次观察到 Connecting 时起算
            _reconnectAt = 0;
            int job = ++_connectJob;

            conn.OnMessage += msg => { if (ReferenceEquals(conn, _conn)) HandleMessage(msg); };
            conn.OnError += err => { if (ReferenceEquals(conn, _conn)) _log($"连接错误: {err}"); };
            conn.OnDisconnected += () => HandleDisconnected(conn, "disconnected");

            string host = assignment.Host;
            int port = (int)assignment.Port;
            _log($"连接 battle 节点 {host}:{port} battle_id={assignment.BattleId} role={assignment.Role}");
            _connectRunner(() =>
            {
                Exception error = null;
                try { conn.Connect(host, port); }
                catch (Exception ex) { error = ex; }
                _connectResults.Enqueue(new ConnectResult { Job = job, Conn = conn, Error = error });
            });
        }

        private void BeginHandshake()
        {
            var conn = _conn;
            var assignment = _assignment;
            if (conn == null || assignment == null) return;
            State = LinkState.Handshaking;
            _connectDeadline = 0;   // 已连上,交给握手期限
            _handshakeDeadline = _now + HandshakeTimeoutSeconds;
            // 两字段原样透传,客户端不解析 payload(§18.2)
            if (!TrySend(conn, new BattleTokenVerifyRequest
            {
                Payload = assignment.TokenPayload,
                Signature = assignment.TokenSignature,
            }))
            {
                FailHandshake("handshake_send_failed", retrySameTicket: true);
            }
        }

        private void HandleMessage(IMessage msg)
        {
            if (msg is BattleTokenVerifyResponse verify)
            {
                if (State != LinkState.Handshaking) return; // 重复应答
                if (verify.Success)
                {
                    State = LinkState.Verified;
                    _handshakeDeadline = 0;
                    // 刻意不重置同票重试预算:预算按"每张票 1 次同票重连 + 每局 1 次补签"封顶
                    //(最多 4 次建连),服务端接了又断的反复抖动不会变成每秒一次的无限重连;
                    // 预算耗尽后本局余下回合经 gate 回落打完(D23)。
                    if (verify.BattleId != 0 && verify.BattleId != _targetBattleId)
                        _log($"握手回填 battle_id={verify.BattleId} 与本地 {_targetBattleId} 不一致,以服务端为准");
                    if (verify.BattleId != 0) _targetBattleId = verify.BattleId;
                    _log($"握手成功 battle_id={_targetBattleId} role={Role}");
                    OnVerified?.Invoke(_targetBattleId);
                }
                else
                {
                    // 票据被拒:同票不再重试(签名 / 期限 / 名单问题重试无意义),直接补签
                    FailHandshake($"ticket_rejected: {verify.Error}", retrySameTicket: false);
                }
                return;
            }

            if (msg is not MessageContent mc) return;

            if (mc.Id != 0)
            {
                if (_pending.Remove(mc.Id, out var pc)) pc.Callback(mc);
                else _log($"应答无对应请求(已超时?) id={mc.Id} message_id={mc.MessageId}");
                return;
            }
            if (_notify.TryGetValue(mc.MessageId, out var handler)) handler(mc);
            else _log($"未处理的推送 message_id={mc.MessageId} bytes={mc.SerializedMessage.Length}");
        }

        private void HandleDisconnected(IFramedConnection conn, string reason)
        {
            if (!ReferenceEquals(conn, _conn)) return; // 旧连接晚到的 FIN,不能动新连接
            TeardownConnection();
            FailPending("disconnected");

            if (_closing) { return; }                 // Close 内部统一收尾
            if (_ended) { Finish("battle_ended"); return; }
            _log($"直连断开 reason={reason} battle_id={_targetBattleId}");
            ScheduleRecovery(reason);
        }

        private void FailHandshake(string reason, bool retrySameTicket)
        {
            _log($"握手失败 reason={reason} battle_id={_targetBattleId}");
            TeardownConnection();
            FailPending("handshake_failed");
            if (_ended) { Finish("battle_ended"); return; }
            if (retrySameTicket) ScheduleRecovery(reason);
            else TryReissue(reason);
        }

        private void ScheduleRecovery(string reason)
        {
            if (_assignment != null && !TicketExpired(_assignment) && _sameTicketRetries < MaxSameTicketRetries)
            {
                _sameTicketRetries++;
                State = LinkState.Idle;
                _reconnectAt = _now + ReconnectDelaySeconds;
                _log($"{ReconnectDelaySeconds:0.#}s 后用同票重连({_sameTicketRetries}/{MaxSameTicketRetries}) reason={reason}");
                return;
            }
            TryReissue(reason);
        }

        private void TryReissue(string reason)
        {
            if (_reissueInFlight)
            {
                // 补签已在途,不重复发。但调用方(ScheduleRecovery / FailHandshake)刚把连接拆了,
                // State 还停在 Verified/Handshaking —— 必须落回 Idle,否则 IsVerified 会对着
                // 一条不存在的连接返回 true,战斗 RPC 全部被路由进直连然后静默丢掉(评审确认项)。
                State = LinkState.Idle;
                return;
            }
            if (_reissues >= MaxReissuesPerBattle || TicketReissuer == null || _targetBattleId == 0)
            {
                Finish(reason);
                return;
            }

            _reissues++;
            _reissueInFlight = true;
            State = LinkState.Idle;
            int epoch = _epoch;
            ulong battleId = _targetBattleId;
            _log($"经大厅补签票据({_reissues}/{MaxReissuesPerBattle}) battle_id={battleId} reason={reason}");

            bool sent = TicketReissuer(battleId,
                assigned =>
                {
                    if (epoch != _epoch) return; // 已换局 / 已 Close
                    _reissueInFlight = false;
                    if (_ended) { Finish("battle_ended"); return; }
                    // 补签在途期间链路可能已经靠别的途径重建好了(同票重连成功、
                    // 或服务端又推了一份分配包)。此时这张迟到的票没有价值,但更重要的是
                    // **不能** 让下面的失败分支或换连接动作拆掉一条正在用的连接(评审确认项)。
                    if (HasLiveConnection && battleId == _targetBattleId) return;
                    if (assigned == null || assigned.BattleId != battleId)
                    {
                        Finish("reissue_mismatch");
                        return;
                    }
                    HandleAssigned(assigned);
                },
                error =>
                {
                    if (epoch != _epoch) return;
                    _reissueInFlight = false;
                    // 同上:补签失败不能拆掉期间已经重建好的连接
                    if (HasLiveConnection && battleId == _targetBattleId) return;
                    Finish($"reissue_failed: {error}");
                });
            if (!sent)
            {
                _reissueInFlight = false;
                if (HasLiveConnection && battleId == _targetBattleId) return;
                Finish("reissue_unavailable");
            }
        }

        private void Finish(string reason)
        {
            TeardownConnection();
            _reconnectAt = 0;
            _handshakeDeadline = 0;
            _connectDeadline = 0;
            bool wasOpen = State != LinkState.Closed;
            State = LinkState.Closed;
            if (wasOpen)
            {
                _log($"链路终结 reason={reason} battle_id={_targetBattleId}");
                OnClosed?.Invoke(reason);
            }
        }

        private void TeardownConnection()
        {
            var conn = _conn;
            if (conn == null) return;
            _conn = null;
            _connectJob++; // 作废在途的连接结果
            try { conn.Dispose(); } catch { }
        }

        private void FailPending(string reason)
        {
            if (_pending.Count == 0) return;
            var calls = new List<PendingCall>(_pending.Values);
            _pending.Clear();
            foreach (var pc in calls) pc.OnError?.Invoke(TransportErrorPrefix + reason);
        }

        /// <summary>只负责发与记日志;失败后的恢复由调用方决定(避免同一次失败触发两次恢复)。</summary>
        private bool TrySend(IFramedConnection conn, IMessage message)
        {
            try
            {
                conn.Send(message);
                return true;
            }
            catch (Exception ex)
            {
                _log($"发送失败: {ex.Message}");
                return false;
            }
        }

        private bool TicketExpired(BattleAssignedS2C assignment)
            => assignment.ExpireAtMs != 0 && assignment.ExpireAtMs <= _unixNowMs();
    }
}
