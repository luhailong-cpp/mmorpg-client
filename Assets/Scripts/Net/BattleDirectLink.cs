using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Google.Protobuf;

namespace MmorpgClient.Net
{
    /// <summary>
    /// 直连链路终结(<see cref="BattleDirectLink.OnClosed"/>)的结构化原因(turn-based §22 D74)。
    /// 上层据此决定收敛方式:只有 <see cref="BattleGone"/> 与 <see cref="Unreachable"/> 需要玩家可见的处理。
    /// </summary>
    public enum BattleLinkCloseKind
    {
        /// <summary>本局已结束 / 已退出观战 / 已放弃(之后的 FIN 是正常收尾)。</summary>
        Ended,
        /// <summary>宿主主动关闭(大厅断线 / 换会话 / 重定向换 gate),恢复由宿主负责。</summary>
        HostClosed,
        /// <summary>补签被服务端判定为「战斗不存在或已结束」(common_error.kInvalidParameter)。</summary>
        BattleGone,
        /// <summary>同票重连与补签预算都已用完(或补签通道不可用):本局连不上 battle 节点。</summary>
        Unreachable,
        /// <summary>
        /// 旧局尚未终结,本链路就改去服务另一局(另一局的分配包 / 重连提示 / 开局自愈 / 手动重连):
        /// 旧局从此收不到任何战斗帧(收缩后战斗帧只走直连,turn-based §22 D68)。
        /// 典型:观战 B 时进了自己的 gather,match 的 RemoveObserver 尽力而为可能丢,SpectateEnd 也可能
        /// 晚于参战分配包 —— 观战方靠这条通知退出旧局观战。它不是「战斗已结束」的权威信号,参战方不据此收场。
        /// </summary>
        Superseded,
    }

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
    /// 恢复策略(§18.2,收缩后口径见 turn-based §22 D74):
    ///   * 战斗中意外断开:票据未过期则用同一张票重连 1 次(D25 同票可重用);
    ///     再失败或票已过期 → 经大厅通道 RequestBattleTicket 补签,每局至多
    ///     <see cref="MaxReissuesPerBattle"/> 次,自动补签按 1s / 2s / 4s(±20% 抖动)退避,
    ///     以票据期限封顶;补签被判「战斗已结束」→ Closed(BattleGone),预算用完 → Closed(Unreachable)。
    ///   * 握手被拒(签名 / 期限 / 名单):同票不再重试,直接进补签。
    ///   * 换局:旧局未终结就改服务另一局时,旧局经 <see cref="OnClosed"/> 以
    ///     <see cref="BattleLinkCloseKind.Superseded"/> 通知一次(旧局从此断流;链路本身不 Closed),新局照常建连。
    ///   * 直连是战斗的唯一通路(gate 不再中继战斗,D66):链路 Closed 即本局无法收发战斗消息,
    ///     由上层提示玩家,并可经 <see cref="Retry"/> 手动重连(重置预算)。
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
        /// <summary>
        /// 每局最多补签次数(turn-based §22 D74)。收缩前 gate 兜底所以 1 次够用;
        /// 收缩后直连是唯一通路,放宽到 3 次并配合退避,扛住 battle 节点 / match 的短暂抖动。
        /// <see cref="Retry"/> / <see cref="HandleReconnectHint"/> / <see cref="EnsureBattle"/> 会重置本预算。
        /// </summary>
        public const int MaxReissuesPerBattle = 3;
        /// <summary>自动补签的退避基数(秒):第 k 次补签前等 基数 × 2^(k-1),即 1s / 2s / 4s。</summary>
        public const double ReissueBackoffBaseSeconds = 1.0;
        /// <summary>退避抖动幅度(±20%):同一 battle 节点抖动时,别让一批客户端同一时刻打 match。</summary>
        public const double ReissueBackoffJitter = 0.2;
        /// <summary>
        /// 传输层失败(而非服务端业务拒绝)的错误串前缀:「这次失败是直连没送到,不是服务端说不行」。
        /// 收缩后不再据此改走大厅重发(gate 不中继战斗,D66);上层据此区分「等直连就绪后补拉」与「服务端拒绝」。
        /// </summary>
        public const string TransportErrorPrefix = "link: ";
        /// <summary>直连未就绪时战斗 RPC 的本地快速失败错误串(带传输层前缀,不发任何网络请求)。</summary>
        public const string NotReadyError = TransportErrorPrefix + "not ready";

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
        private readonly Func<double> _random01;

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
        private double _reissueAt;        // >0 = 到点自动补签(退避中)
        private string _reissueReason;    // 退避到点时补签的原因(日志用)
        private long _seq;

        private BattleAssignedS2C _assignment;
        private ulong _targetBattleId;    // 本链路服务的战斗(分配包或重连提示带来)
        private int _sameTicketRetries;
        private int _reissues;
        // 本轮补签预算里「票据期限已过 → 立即补签」是否已用过(见 ReissueDelaySeconds)。
        // 与 _reissues 同生命周期:换局 / 宿主入口重置预算时一并重置。
        private bool _expiredTicketReissueUsed;
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

        /// <summary>
        /// 链路终结(参数:终结时服务的 battle_id、结构化原因、诊断串)。正常收尾也触发;
        /// 同一次终结只触发一次(已 Closed 再收尾不重复)。
        /// 换局时旧局若未终结,另以 <see cref="BattleLinkCloseKind.Superseded"/> 触发一次(参数为旧局 id),
        /// 此时链路本身并不 Closed —— 它已经在为新局建连;事件在链路切到新局之后发出。
        /// </summary>
        public event Action<ulong, BattleLinkCloseKind, string> OnClosed;

        /// <summary>
        /// 宿主提供的补签通道:经大厅会话调 MatchService.RequestBattleTicket(battle_id)。
        /// 失败回调带结构化原因 (tipId, detail):tipId 为服务端 tip 码(响应体或 Call 层的
        /// TipInfoMessage),传输失败 / 空分配包传 0。tipId == common_error.kInvalidParameter
        /// (match:该战斗不存在或已结束)→ Closed(BattleGone),其余按退避继续补签,预算用完 → Unreachable。
        /// 返回 false = 此刻发不出(大厅未就绪),按一次失败的补签计。回调必须回到主线程。
        /// </summary>
        public Func<ulong, Action<BattleAssignedS2C>, Action<uint, string>, bool> TicketReissuer { get; set; }

        /// <param name="factory">产出一条未连接的分帧连接(生产:new GateTcpClient(codec))。</param>
        /// <param name="log">日志汇;可空。</param>
        /// <param name="unixNowMs">票据过期判定用的 Unix 毫秒时钟;空 = 系统时钟。</param>
        /// <param name="connectRunner">
        /// 执行阻塞的 Connect 的方式;空 = 线程池(TcpClient.Connect 对不可达地址会卡到系统超时,
        /// 不能占主线程)。测试传 <c>a =&gt; a()</c> 让连接同步完成。
        /// </param>
        /// <param name="random01">
        /// 补签退避抖动用的 [0,1) 随机源;空 = System.Random。只在主线程调用。
        /// 测试传常量(0.5 = 无抖动)让退避时序确定。
        /// </param>
        public BattleDirectLink(Func<IFramedConnection> factory, Action<string> log = null,
                                Func<ulong> unixNowMs = null, Action<Action> connectRunner = null,
                                Func<double> random01 = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _log = log ?? (_ => { });
            _unixNowMs = unixNowMs ?? (() => (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _connectRunner = connectRunner ?? (a => ThreadPool.QueueUserWorkItem(_ => a()));
            if (random01 == null)
            {
                var rng = new Random();
                random01 = rng.NextDouble;
            }
            _random01 = random01;
        }

        // ── 宿主入口 ────────────────────────────────────────

        /// <summary>
        /// 收到落点分配(开局 / 观战接入的 NotifyBattleAssigned,或补签响应)。
        /// 同一局已在连接 / 已验证:只更新票据(重连时用最新的),不重新建连;
        /// 新的一局或链路已终结:关闭旧连接,按新分配建连(旧局未终结时另抛 Superseded)。
        /// 不完整的分配包(缺 battle_id / host / port)忽略 —— 补签响应不走这条静默忽略,见 SendReissue。
        /// </summary>
        public void HandleAssigned(BattleAssignedS2C assigned)
        {
            if (!IsCompleteAssignment(assigned))
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
            // 失败分支会 Finish() 掉这里刚建起来的连接(评审确认项)。退避中的补签同理作废。
            _epoch++;
            _reissueInFlight = false;
            _reissueAt = 0;
            ulong supersededBattleId = 0;
            if (!sameBattle)
            {
                // 换局:上一局的恢复预算与结束标记全部作废
                supersededBattleId = OpenBattleSupersededBy(assigned.BattleId);
                _reissues = 0;
                _expiredTicketReissueUsed = false;
                _targetBattleId = assigned.BattleId;
            }
            _assignment = assigned;
            _ended = false;
            _sameTicketRetries = 0; // 新票据:重新给 1 次同票重试
            OpenConnection();
            RaiseSuperseded(supersededBattleId);
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
            _reissueAt = 0;
            if (State == LinkState.Idle && _targetBattleId != 0)
            {
                // 正在等重连 / 补签退避倒计时:战斗已结束,不必再连
                Finish(BattleLinkCloseKind.Ended, "battle_ended");
            }
        }

        /// <summary>
        /// 大厅重连后 scene 推 NotifyBattleReconnect:手里没有可用票据(大厅断线时链路已 Close),
        /// 经大厅通道补签一张再建连(立即补签,重置两类预算)。已在该局连接 / 验证过则无事。
        /// </summary>
        public void HandleReconnectHint(ulong battleId)
        {
            if (battleId == 0) return;
            if (battleId == _targetBattleId && HasLiveConnection) return;
            // 同一局的补签已在途:重连提示可能来自两处(GameClient 重定向收尾主动补 +
            // 服务端 NotifyBattleReconnect),不能各发一次 RequestBattleTicket。
            if (battleId == _targetBattleId && _reissueInFlight) return;

            RestartWithReissue(battleId, "reconnect_hint");
        }

        /// <summary>
        /// UI「重新连接」(turn-based §22 D74):重置同票与补签两类预算,立即补签重建直连。
        /// 该局已在连接 / 已验证,或补签已在途时无事(不重复打 match)。
        /// </summary>
        public void Retry(ulong battleId)
        {
            if (battleId == 0) return;
            if (battleId == _targetBattleId && (HasLiveConnection || _reissueInFlight)) return;
            _log($"手动重连 battle_id={battleId}");
            RestartWithReissue(battleId, "manual_retry");
        }

        /// <summary>
        /// 开局自愈(BattleStart 到达时调用,turn-based §22 D74):分配包与开局包同 Kafka key 有序,
        /// 开局包到了而本链路不服务该局 = 分配包丢了,立即补签;同局且未终结(在连 / 已验证 /
        /// 恢复中)时不做事,不打扰进行中的恢复。
        /// </summary>
        public void EnsureBattle(ulong battleId)
        {
            if (battleId == 0) return;
            if (battleId == _targetBattleId && State != LinkState.Closed) return;
            _log($"开局时直连不服务该局(分配包丢失或链路已终结)battle_id={battleId} 当前={_targetBattleId} state={State},补签");
            RestartWithReissue(battleId, "ensure_battle");
        }

        /// <summary>
        /// 放弃该局直连(turn-based §22 D74):直连未就绪时退出观战用 —— 此时 StopWatchBattle
        /// 发不出去,改为本地终结链路:标记本局结束、作废在途补签与重连倒计时、关连接,
        /// 以 <see cref="BattleLinkCloseKind.Ended"/> 收尾,之后不再重连。
        /// 不是本链路服务的局时无事(分配包还没到,就没有可放弃的东西)。
        /// 已验证的链路也可放弃,但调用方应优先发 StopWatchBattle 让服务端摘掉观众。
        /// </summary>
        public void Abandon(ulong battleId)
        {
            if (battleId == 0 || battleId != _targetBattleId) return;
            if (State == LinkState.Closed) return;
            _ended = true;
            FailPending("abandoned");
            Finish(BattleLinkCloseKind.Ended, "abandoned"); // Finish 负责作废在途补签与倒计时
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
                _reissueAt = 0;
                _handshakeDeadline = 0;
                _connectDeadline = 0;
                _reissueInFlight = false;
                if (State != LinkState.Closed && _targetBattleId != 0) Finish(BattleLinkCloseKind.HostClosed, reason);
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

            // 先处理到点的重连 / 补签,再收连接结果:同步完成的连接(测试 / 本机极快建连)在同一帧内
            // 就能发出握手包,不必多等一帧。
            if (_reconnectAt > 0 && _now >= _reconnectAt)
            {
                _reconnectAt = 0;
                if (_assignment != null && !_ended) OpenConnection();
            }
            if (_reissueAt > 0 && _now >= _reissueAt)
            {
                _reissueAt = 0;
                if (_ended) Finish(BattleLinkCloseKind.Ended, "battle_ended");
                else SendReissue(_reissueReason);
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
            // 且不可配),期间 State 停在 Connecting、既不重试也不补签。battle 节点端口不可达
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
                if (_ended) { Finish(BattleLinkCloseKind.Ended, "battle_ended"); }
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

        /// <summary>请求-响应(仅 Verified 时可用;其它状态立即以 <see cref="NotReadyError"/> 失败)。</summary>
        public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                                Action<TResp> onResponse, Action<string> onError)
            where TResp : IMessage<TResp>
        {
            var conn = _conn;
            if (!IsVerified || conn == null)
            {
                onError?.Invoke(NotReadyError);
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

        /// <summary>单向发送(仅 Verified 时生效;其它状态静默丢弃 —— 分流层在调用前已判就绪并记日志)。</summary>
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
            if (assignment == null) { Finish(BattleLinkCloseKind.Unreachable, "no_assignment"); return; }

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
                    // 刻意不重置两类预算:预算按"每张票 1 次同票重连 + 每局 MaxReissuesPerBattle 次补签"
                    // 封顶,服务端接了又断的反复抖动不会变成每秒一次的无限重连;
                    // 预算耗尽 → Closed(Unreachable),由 UI 提示并提供手动重连(Retry 重置预算)。
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
            if (_ended) { Finish(BattleLinkCloseKind.Ended, "battle_ended"); return; }
            _log($"直连断开 reason={reason} battle_id={_targetBattleId}");
            ScheduleRecovery(reason);
        }

        private void FailHandshake(string reason, bool retrySameTicket)
        {
            _log($"握手失败 reason={reason} battle_id={_targetBattleId}");
            TeardownConnection();
            FailPending("handshake_failed");
            if (_ended) { Finish(BattleLinkCloseKind.Ended, "battle_ended"); return; }
            if (retrySameTicket) ScheduleRecovery(reason);
            else ScheduleReissue(reason);
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
            ScheduleReissue(reason);
        }

        /// <summary>
        /// 宿主入口(重连提示 / 手动重连 / 开局自愈)共用:作废当前连接与一切在途恢复,
        /// 重置两类预算,不等退避立即补签。
        /// </summary>
        private void RestartWithReissue(ulong battleId, string reason)
        {
            _epoch++;
            TeardownConnection();
            _assignment = null;
            _targetBattleId = battleId;
            _ended = false;
            _sameTicketRetries = 0;
            _reissues = 0;
            _reissueInFlight = false;
            _reconnectAt = 0;
            _reissueAt = 0;
            _handshakeDeadline = 0;
            _connectDeadline = 0;
            State = LinkState.Idle;
            SendReissue(reason);
        }

        /// <summary>
        /// 自动恢复路径:预算内按退避排下一次补签(第 k 次等 1s·2^(k-1) ±20%,以票据期限封顶);
        /// 预算用完 / 没有补签通道 → Closed(Unreachable)。
        /// </summary>
        private void ScheduleReissue(string reason)
        {
            if (_reissueInFlight || _reissueAt > 0)
            {
                // 补签已在途或已排队,不重复排。但调用方(ScheduleRecovery / FailHandshake)刚把连接拆了,
                // State 还停在 Verified/Handshaking —— 必须落回 Idle,否则 IsVerified 会对着
                // 一条不存在的连接返回 true,战斗 RPC 全部被路由进直连然后静默丢掉(评审确认项)。
                State = LinkState.Idle;
                return;
            }
            if (_reissues >= MaxReissuesPerBattle || TicketReissuer == null || _targetBattleId == 0)
            {
                Finish(BattleLinkCloseKind.Unreachable, $"recovery_exhausted: {reason}");
                return;
            }

            State = LinkState.Idle;
            double delay = ReissueDelaySeconds(_reissues + 1);
            if (delay <= 0)
            {
                SendReissue(reason);
                return;
            }
            _reissueAt = _now + delay;
            _reissueReason = reason;
            _log($"{delay:0.##}s 后补签({_reissues + 1}/{MaxReissuesPerBattle}) battle_id={_targetBattleId} reason={reason}");
        }

        /// <summary>
        /// 第 attempt 次(从 1 起)补签前的退避:基数 × 2^(attempt-1) × (1 ± 抖动)。
        /// 以票据期限(= 房间期限,expire_at_ms)封顶:不等到房间作废之后才去补签,
        /// 期限已过则立即补签一次,由服务端给权威答复(kInvalidParameter → BattleGone)——
        /// 刻意不凭本地时钟判定「战斗已结束」,客户端时钟偏快会把进行中的战斗误判收场。
        /// </summary>
        private double ReissueDelaySeconds(int attempt)
        {
            double delay = ReissueBackoffBaseSeconds * Math.Pow(2, attempt - 1)
                           * (1.0 + (_random01() * 2.0 - 1.0) * ReissueBackoffJitter);
            var assignment = _assignment;
            if (assignment != null && assignment.ExpireAtMs != 0)
            {
                double remaining = ((long)assignment.ExpireAtMs - (long)_unixNowMs()) / 1000.0;
                if (remaining < delay) delay = Math.Max(0, remaining);
            }
            return delay;
        }

        private void SendReissue(string reason)
        {
            if (TicketReissuer == null || _targetBattleId == 0)
            {
                Finish(BattleLinkCloseKind.Unreachable, $"reissue_unavailable: {reason}");
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
                    if (epoch != _epoch) return; // 已换局 / 已 Close / 已放弃
                    _reissueInFlight = false;
                    if (_ended) { Finish(BattleLinkCloseKind.Ended, "battle_ended"); return; }
                    // 补签在途期间链路可能已经靠别的途径重建好了(同票重连成功、
                    // 或服务端又推了一份分配包)。此时这张迟到的票没有价值,但更重要的是
                    // **不能** 让下面的失败分支或换连接动作拆掉一条正在用的连接(评审确认项)。
                    if (HasLiveConnection && battleId == _targetBattleId) return;
                    if (assigned == null || assigned.BattleId != battleId)
                    {
                        Finish(BattleLinkCloseKind.Unreachable, "reissue_mismatch");
                        return;
                    }
                    HandleAssigned(assigned);
                },
                (tipId, detail) =>
                {
                    if (epoch != _epoch) return;
                    _reissueInFlight = false;
                    // 同上:补签失败不能拆掉期间已经重建好的连接
                    if (HasLiveConnection && battleId == _targetBattleId) return;
                    HandleReissueFailed(tipId, detail);
                });
            if (!sent)
            {
                _reissueInFlight = false;
                if (HasLiveConnection && battleId == _targetBattleId) return;
                HandleReissueFailed(0, "reissue_unavailable");
            }
        }

        /// <summary>
        /// 补签失败分类(turn-based §22 D74):match 回 kInvalidParameter = 该战斗不存在或已结束
        /// (典型:终局包在断线期间丢失),收敛为 BattleGone,不再重试;其余 tip 与传输失败
        /// 视为暂时连不上,按退避继续,预算用完 → Unreachable。
        /// </summary>
        private void HandleReissueFailed(uint tipId, string detail)
        {
            _log($"补签失败 tip={tipId} detail={detail} battle_id={_targetBattleId}");
            if (_ended) { Finish(BattleLinkCloseKind.Ended, "battle_ended"); return; }
            if (tipId == (uint)common_error.KInvalidParameter)
            {
                Finish(BattleLinkCloseKind.BattleGone, $"reissue_rejected: tip={tipId}");
                return;
            }
            ScheduleReissue($"reissue_failed: tip={tipId} {detail}");
        }

        private void Finish(BattleLinkCloseKind kind, string reason)
        {
            // Closed 是终态:作废在途补签(否则它的回调还会动链路,且 _reissueInFlight 残留会让
            // 之后的重连提示 / 手动重连被当成「补签已在途」而吞掉)。重新打开一律经
            // HandleAssigned / RestartWithReissue,两者各自再 bump epoch。
            _epoch++;
            _reissueInFlight = false;
            TeardownConnection();
            _reconnectAt = 0;
            _reissueAt = 0;
            _handshakeDeadline = 0;
            _connectDeadline = 0;
            bool wasOpen = State != LinkState.Closed;
            State = LinkState.Closed;
            if (wasOpen)
            {
                _log($"链路终结 kind={kind} reason={reason} battle_id={_targetBattleId}");
                OnClosed?.Invoke(_targetBattleId, kind, reason);
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
