using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Guild;
using MmorpgClient.Game.Social;
using MmorpgClient.Game.Team;
using MmorpgClient.Net;

namespace MmorpgClient.Game.Sync
{
    /// <summary>
    /// 一次「摘要标脏」之后的拉取凭证(服务端 docs/design/landing-recovery-sync-digest.md §4.3 第 4 条)。
    /// 消费者在**发出**拉取时取票(<see cref="LandingSyncClient.TryBeginPull"/>),拉取成功应用之后交回
    /// <see cref="LandingSyncClient.Ack"/>。发出晚于摘要到达的拉取,读到的数据一定不旧于摘要里的版本,
    /// 所以只有这样推进缓存,缓存版本才不会领先于实际拉到的数据(L-2)。
    /// </summary>
    public readonly struct SyncPullTicket
    {
        internal SyncPullTicket(int epoch, SyncDomain domain, ulong versionReadAtLanding, bool hasVersion)
        {
            Epoch = epoch;
            Domain = domain;
            VersionReadAtLanding = versionReadAtLanding;
            HasVersion = hasVersion;
        }

        public SyncDomain Domain { get; }

        /// <summary>本次落地读到的摘要里该域的计数(摘要里缺席按 0)。<see cref="HasVersion"/> 为假时无意义。</summary>
        public ulong VersionReadAtLanding { get; }

        /// <summary>
        /// 假 = 本次落地没有可用的摘要(UNAVAILABLE,或取摘要失败后的全量拉):确认只消脏标、不写缓存计数,
        /// 因为不知道服务端此刻的计数是多少。
        /// </summary>
        public bool HasVersion { get; }

        /// <summary>标脏批次。落地变化或摘要重算之后,旧批次的票一律作废(default 值的批次是 0,永不有效)。</summary>
        internal int Epoch { get; }
    }

    /// <summary>
    /// 落地恢复:按域版本摘要的客户端半边(服务端 docs/design/landing-recovery-sync-digest.md §4.3)。
    /// 纯数据类,不依赖 UnityEngine;由 GameClient 构造并在其 Tick 里驱动。
    ///
    /// 流程:
    ///  1. 落地判定:(PlayerId, 连接身份) 变化且「在游戏中 &amp;&amp; 连接就绪」= 一次落地。覆盖首登、手动重登、
    ///     跨 zone 重定向的第二条腿;同 zone 换图连接不变,不算落地。
    ///  2. 取摘要:单向发 GetSyncDigest(应答是 Empty,同 SceneInfoC2S,不能用 Call 等应答,会干等到 RPC 超时),
    ///     结果走 NotifySyncDigest 推送。单飞;<see cref="DigestTimeoutSeconds"/> 秒超时;失败按 15 / 30 / 60 秒封顶退避。
    ///     请求被 gate 拒绝(限流等)时,带错误码的信封回到 GetSyncDigest 自己的推送处理器上,按失败处理。
    ///  3. 比对:UNAVAILABLE、代次 g 与缓存不同、或本次落地可能收到旧落地的迟到推送 → 所有域标脏;
    ///     否则逐域比较,本地没有记录或计数不等(摘要缺席按 0)即标脏。
    ///  4. 拉取与推进:标脏的域通知已注册的消费者(<see cref="RegisterConsumer"/>);消费者发出拉取时取票、拉取成功后
    ///     <see cref="Ack"/>,此时才把该域的缓存计数写成摘要值。拉取失败保留脏标,等下次落地或玩家打开面板。
    ///
    /// 隐私(设计 §3.5 第 4 条):摘要变化可能来自被动方的 bump(被删好友、被拉黑、申请被拒),
    /// **不得**据 <see cref="IsDirty"/> / <see cref="Changed"/> 显示红点或提示;红点只能来自拉回的内容。
    ///
    /// 线程与重入:全部在主线程上调用(GameClient.Tick / 收包派发)。GameClient.Tick 会在 Call 的等待循环里重入,
    /// 消费者在回调里发请求也会重入 <see cref="Tick"/>,所以状态一律先落定、再调出去。
    /// GetSyncDigest / NotifySyncDigest 两个消息号的推送处理器全仓只在这里注册(OnNotify 是覆盖语义)。
    /// </summary>
    public sealed class LandingSyncClient : IDisposable
    {
        /// <summary>GetSyncDigest 发出后等 NotifySyncDigest 的上限(秒)。</summary>
        public const float DigestTimeoutSeconds = 15f;

        /// <summary>
        /// 多久以前发出的请求,不再等它的推送(秒)。只用于判断新落地要不要全量拉(见 <see cref="BeginLanding"/>):
        /// 摘要推送不带请求编号,旧落地的请求若可能还有推送在路上,新落地收到的那一条就分不清是谁的。
        /// </summary>
        public const float StalePushHorizonSeconds = 120f;

        /// <summary>取摘要失败(超时 / 被 gate 拒绝)之后的退避(秒),第 3 次起封顶。</summary>
        private static readonly float[] RetryBackoffSeconds = { 15f, 30f, 60f };

        private static readonly SyncDomain[] KnownDomains = CollectKnownDomains();

        private sealed class Consumer : IDisposable
        {
            private LandingSyncClient _owner;
            public readonly SyncDomain Domain;
            public readonly Action<SyncPullTicket> OnDirty;
            /// <summary>本批标脏还没通知到它;由 Tick 投递。</summary>
            public bool Pending;

            public Consumer(LandingSyncClient owner, SyncDomain domain, Action<SyncPullTicket> onDirty)
            {
                _owner = owner;
                Domain = domain;
                OnDirty = onDirty;
            }

            public bool Active => _owner != null;

            public void Detach()
            {
                _owner = null;
                Pending = false;
            }

            public void Dispose()
            {
                var owner = _owner;
                Detach();
                owner?._consumers.Remove(this);
            }
        }

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<bool> _inGame;
        private readonly Func<float> _clock;
        private readonly ISyncDigestStore _store;
        private readonly Action<string> _log;
        private readonly List<Consumer> _consumers = new List<Consumer>();
        // 当前标脏的域 → 本次落地摘要里的计数(_dirtyVersionsKnown 为假时计数无意义,统一记 0)。
        private readonly Dictionary<SyncDomain, ulong> _dirty = new Dictionary<SyncDomain, ulong>();

        // ── 当前落地 ── 玩家为 0 表示不在落地状态(未进游戏 / 断线 / 重定向途中)。
        private ulong _player;
        private object _connection;
        // 当前玩家缓存的内存镜像;null = 没有缓存(或摘要不可用后已作废)。
        private SyncDigestRecord _record;
        // 本次落地开始时还有旧请求可能晚到:收到的摘要一律按全量拉处理(L-1)。
        private bool _fullPullRequired;

        // ── 取摘要请求(单飞)── 戳 = (玩家, 连接身份)。
        // 不带请求序号:摘要推送与 gate 的拒绝回包都不带请求编号(SendOneWay 也不返回 mc.Id),序号无从比较。
        // 旧落地的推送靠「本次落地已发过请求、还没拿到摘要、戳与当前落地一致」三个条件丢弃,
        // 分不清的情况由 BeginLanding 里「发出数 > 收到数」时本次全量拉兜底(L-1)。
        private bool _awaiting;
        private ulong _stampPlayer;
        private object _stampConnection;
        private int _sentThisLanding;
        private float _deadline;
        private float _nextAttemptAt = float.NegativeInfinity;
        private int _failures;
        private bool _hasDigest;
        // 全程累计:发出的请求数与收到的推送数(不论采纳与否),用来判断有没有可能晚到的旧推送。
        private long _requestsSent;
        private long _pushesReceived;
        private float _lastSendAt = float.NegativeInfinity;

        // ── 标脏 ── _dirtyValid 为真时 _dirty 才有意义;_dirtyEpoch 每次重算 +1,旧票据随之作废。
        private bool _dirtyValid;
        private bool _dirtyVersionsKnown;
        private int _dirtyEpoch = 1;

        private bool _disposed;

        /// <summary>
        /// 标脏 / 确认 / 落地复位之后触发。只给调试与界面刷新用:**不得**据此显示红点(见类注释的隐私一节)。
        /// </summary>
        public event Action Changed;

        /// <param name="net">gate 管线(请求单向发送、推送注册)。</param>
        /// <param name="connectionIdentity">当前连接的不透明身份,只做引用比较(GameClient.GateConnectionIdentity)。</param>
        /// <param name="inGame">是否已进游戏(GameClient.InGame);连接就绪由 <paramref name="net"/> 的 IsReady 给出。</param>
        /// <param name="clock">单调时钟(秒),只用来算超时与退避。</param>
        /// <param name="store">按玩家分键的本地缓存。</param>
        /// <param name="log">可选日志出口。</param>
        public LandingSyncClient(IBattleTransport net, Func<object> connectionIdentity, Func<bool> inGame,
                                 Func<float> clock, ISyncDigestStore store, Action<string> log = null)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _connectionIdentity = connectionIdentity ?? throw new ArgumentNullException(nameof(connectionIdentity));
            _inGame = inGame ?? throw new ArgumentNullException(nameof(inGame));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _log = log;
            // 一个 message id 只有一个处理器(GameClient.OnNotify 覆盖写),这两个号全仓只在这里注册。
            // Net/Generated 下若生成了同名空壳,也只有 HandlerRegistry.Register 才会挂上,而它从未被调用。
            _net.RegisterNotify(MessageIds.NotifySyncDigest, HandleDigest);
            _net.RegisterNotify(MessageIds.GetSyncDigest, HandleRequestReply);
            // 构造不发请求:第一次取摘要由落地后的 Tick 发出。
        }

        /// <summary>所有已知的域(生成的 SyncDomain 去掉 UNSPECIFIED);新增域随 proto 重新生成自动纳入。</summary>
        public IReadOnlyList<SyncDomain> Domains => KnownDomains;

        /// <summary>本次落地已拿到摘要(含 UNAVAILABLE);拿到之后本次落地不再发请求。</summary>
        public bool HasDigest => _hasDigest;

        /// <summary>取摘要请求在途。</summary>
        public bool RequestInFlight => _awaiting;

        /// <summary>
        /// 该域在本次落地是否待拉取。本次落地还没有摘要(也还没失败过)时一律为假。
        /// 只表示「需要回源拉一次」,**不是**红点依据(见类注释的隐私一节)。
        /// </summary>
        public bool IsDirty(SyncDomain domain) => !_disposed && _dirtyValid && _dirty.ContainsKey(domain);

        /// <summary>
        /// 注册某个域的消费者:该域在本次落地标脏时,由 <see cref="Tick"/> 调一次 <paramref name="onDirty"/>
        /// (每批标脏最多一次;注册晚于标脏时在下一次 Tick 补发)。消费者应当排队回源拉取,
        /// 并在发出拉取时 <see cref="TryBeginPull"/>、成功应用后 <see cref="Ack"/>。Dispose 返回值即注销。
        /// </summary>
        public IDisposable RegisterConsumer(SyncDomain domain, Action<SyncPullTicket> onDirty)
        {
            if (onDirty == null) throw new ArgumentNullException(nameof(onDirty));
            if (!IsKnown(domain)) throw new ArgumentOutOfRangeException(nameof(domain), domain, "未知的同步域");
            var consumer = new Consumer(this, domain, onDirty);
            if (_disposed)
            {
                consumer.Detach();
                return consumer;
            }
            // 不在这里同步回调:宿主往往在建好客户端之后紧接着复位它,同步发出的拉取会被那次复位作废。
            consumer.Pending = _dirtyValid && _dirty.ContainsKey(domain);
            _consumers.Add(consumer);
            return consumer;
        }

        /// <summary>
        /// 消费者**发出**拉取时调用:该域此刻待拉取则返回 true 并给出凭证。拉取成功应用之后把凭证交给 <see cref="Ack"/>;
        /// 返回 false 时这次拉取不需要确认(摘要还没到,或该域没变)。
        /// </summary>
        public bool TryBeginPull(SyncDomain domain, out SyncPullTicket ticket)
        {
            ticket = default;
            if (_disposed) return false;
            ObserveLanding();
            if (!_dirtyValid || !_dirty.TryGetValue(domain, out ulong version)) return false;
            ticket = new SyncPullTicket(_dirtyEpoch, domain, version, _dirtyVersionsKnown);
            return true;
        }

        /// <summary>
        /// 消费者拉取成功之后确认(设计里的 Ack(domain, versionReadAtLanding)):该域消脏标,
        /// 摘要可用时把缓存计数写成落地时读到的值。凭证来自已作废的批次(换了落地、摘要重算)时什么也不做,返回 false。
        /// </summary>
        public bool Ack(SyncPullTicket ticket)
        {
            if (_disposed) return false;
            ObserveLanding();
            if (!_dirtyValid || ticket.Epoch != _dirtyEpoch || ticket.HasVersion != _dirtyVersionsKnown) return false;
            if (!_dirty.TryGetValue(ticket.Domain, out ulong version) || version != ticket.VersionReadAtLanding) return false;
            _dirty.Remove(ticket.Domain);
            foreach (var consumer in _consumers)
                if (consumer.Domain == ticket.Domain) consumer.Pending = false;
            if (ticket.HasVersion && _record != null)
            {
                _record.SetCount((int)ticket.Domain, version);
                SaveRecord();
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// 由 GameClient.Tick 每帧驱动(也会在 Call 的等待循环里重入):观测落地、发请求、判超时、投递标脏通知。
        /// </summary>
        public void Tick()
        {
            if (_disposed) return;
            ObserveLanding();
            if (_player == 0) return;
            float now = _clock();
            if (_awaiting && now >= _deadline) Fail(now, "等待摘要推送超时");
            if (!_awaiting && !_hasDigest && now >= _nextAttemptAt) SendRequest(now);
            DeliverDirty();
        }

        public void Dispose()
        {
            if (_disposed) return;
            // IBattleTransport 没有注销推送的接口;两个处理器开头的 _disposed 判断保证之后到来的推送不再改动状态。
            _disposed = true;
            _awaiting = false;
            _stampConnection = null;
            _connection = null;
            _dirty.Clear();
            _dirtyValid = false;
            ++_dirtyEpoch;
            foreach (var consumer in _consumers) consumer.Detach();
            _consumers.Clear();
        }

        // ── 落地 ────────────────────────────────────────────────────────────

        private void ObserveLanding()
        {
            ulong player = 0;
            object connection = null;
            if (_inGame() && _net.IsReady)
            {
                player = _net.PlayerId;
                connection = player != 0 ? _connectionIdentity() : null;
                if (connection == null) player = 0;
            }
            if (player == _player && ReferenceEquals(connection, _connection)) return;
            BeginLanding(player, connection);
        }

        /// <summary>
        /// 身份变化:作废上一次落地的全部状态(在途请求、标脏、凭证),玩家非 0 时开始新的一次落地。
        /// </summary>
        private void BeginLanding(ulong player, object connection)
        {
            bool hadState = _player != 0 || _dirtyValid;
            _player = player;
            _connection = connection;
            _awaiting = false;
            _stampConnection = null;
            _sentThisLanding = 0;
            _hasDigest = false;
            _failures = 0;
            _nextAttemptAt = float.NegativeInfinity;
            _record = null;
            _fullPullRequired = false;
            _dirty.Clear();
            _dirtyValid = false;
            _dirtyVersionsKnown = false;
            ++_dirtyEpoch;
            foreach (var consumer in _consumers) consumer.Pending = false;

            if (player != 0)
            {
                // 摘要推送不带请求编号:scene 读完 Redis 后按 player_id 找实体、经它**当前**的会话推送,
                // 旧落地的请求若还没回,它的推送会落在新连接上,而且同一节点上一定排在新请求的推送前面。
                // 这时收到的第一条摘要可能读于本次断线窗口之前,窗口里发生的变化不在里面 —— 本次落地一律全量拉(L-1)。
                // 很久以前的请求不再等(见 StalePushHorizonSeconds),免得一条丢失的推送让以后每次落地都全量拉。
                float now = _clock();
                if (now - _lastSendAt >= StalePushHorizonSeconds) _pushesReceived = _requestsSent;
                _fullPullRequired = _requestsSent > _pushesReceived;
                _record = LoadRecord(player);
                Log(_fullPullRequired ? "落地:上一次的摘要请求可能晚到,本次全量拉" : "落地:准备取摘要");
            }
            if (hadState) Changed?.Invoke();
        }

        // ── 请求 ────────────────────────────────────────────────────────────

        private void SendRequest(float now)
        {
            // 先落状态再交给传输层:GameClient.Tick 会重入(Call 的等待循环),发送失败时传输层还会同步触发断线,
            // 在途标志与戳必须先于发送写好(设计 §4.3 第 2 条)。
            _awaiting = true;
            _stampPlayer = _player;
            _stampConnection = _connection;
            ++_sentThisLanding;
            _deadline = now + DigestTimeoutSeconds;
            _lastSendAt = now;
            ++_requestsSent;
            _net.SendOneWay(MessageIds.GetSyncDigest, new GetSyncDigestRequest());
        }

        private bool StampIsCurrent() =>
            _player != 0 && _stampPlayer == _player && ReferenceEquals(_stampConnection, _connection);

        private void Fail(float now, string reason)
        {
            _awaiting = false;
            ++_failures;
            float backoff = RetryBackoffSeconds[Math.Min(_failures, RetryBackoffSeconds.Length) - 1];
            _nextAttemptAt = now + backoff;
            Log($"取摘要失败({reason}),{backoff:0} 秒后重试");
            // 本次落地还没有任何摘要:先按全量拉处理,不让消费者干等。连到尚未实现摘要的服务端(Java 版待做,设计 §11)
            // 时功能就退化成每次落地全量拉。只在第一次失败时标脏,之后重试再失败不重复通知;重试拿到摘要时按摘要重算。
            if (!_dirtyValid) MarkAllDirty(versionsKnown: false);
        }

        /// <summary>GetSyncDigest 自己的消息号:应答是 Empty,正常没有回包;被 gate 拒绝时带错误码的信封落到这里。</summary>
        private void HandleRequestReply(MessageContent content)
        {
            if (_disposed) return;
            uint tipId = content?.ErrorMessage?.Id ?? 0;
            if (tipId == 0) return;
            // 被拒的请求不会再有推送:记作已收到,免得它让下一次落地(StalePushHorizonSeconds 之内)无谓地全量拉。
            // 拒绝回包不带请求编号,分不清是哪一次被拒;但不论是哪一次,都恰好少一条推送,计数照样对。
            ++_pushesReceived;
            ObserveLanding();
            if (!_awaiting || !StampIsCurrent()) return;
            Fail(_clock(), $"请求被拒绝 tip={tipId}");
        }

        /// <summary>NotifySyncDigest:摘要推送。</summary>
        private void HandleDigest(MessageContent content)
        {
            if (_disposed || content == null) return;
            ++_pushesReceived;
            ObserveLanding();
            // 推送不带请求编号,只能按到达时机认:本次落地发过请求、还没拿到摘要才收;否则一律是旧落地的迟到推送
            // 或重复推送。本次落地超时之后才到的推送照收:它读于本次落地之后,不会漏掉落地之前的变化。
            if (_player == 0 || _hasDigest || _sentThisLanding == 0
                || _stampPlayer != _player || !ReferenceEquals(_stampConnection, _connection))
            {
                Log("丢弃不属于本次落地请求的摘要推送");
                return;
            }
            SyncDigestS2C digest = null;
            try
            {
                digest = SyncDigestS2C.Parser.ParseFrom(content.SerializedMessage);
            }
            catch (InvalidProtocolBufferException ex)
            {
                // 坏包按「摘要不可用」处理(全量拉,只会多拉);推送是至多一次的,异常抛出去会打断收包循环。
                Log($"摘要推送无法解析,按摘要不可用处理: {ex.Message}");
            }
            _awaiting = false;
            _failures = 0;
            _hasDigest = true;
            Apply(digest);
            Changed?.Invoke();
        }

        // ── 比对 ────────────────────────────────────────────────────────────

        private void Apply(SyncDigestS2C digest)
        {
            if (digest == null || digest.Status != SyncDigestStatus.Ok)
            {
                // 摘要不可用(Redis 不可用 / 命令未发出 / 应答畸形):所有域全量拉,该玩家缓存整份作废(设计 §4.3 第 3 条)。
                Log("摘要不可用,全部域重新拉取");
                _record = null;
                DeleteRecord(_player);
                MarkAllDirty(versionsKnown: false);
                return;
            }

            var versions = new Dictionary<SyncDomain, ulong>();
            foreach (var entry in digest.Entries)
            {
                // 未知域号跳过:服务端先加了域、客户端还没更新时,没有消费者能拉它。
                if (entry != null && IsKnown(entry.Domain)) versions[entry.Domain] = entry.Version;
            }

            bool fullPull = _fullPullRequired;
            if (_record == null || _record.Generation != digest.Generation)
            {
                // 代次变了 = 摘要 hash 重建过(过期 / 清库 / 回档)或本地没有缓存:旧计数全部作废,g 改记新值(L-3)。
                _record = new SyncDigestRecord(digest.Generation);
                SaveRecord();
                fullPull = true;
            }

            ResetDirty(versionsKnown: true);
            foreach (var domain in KnownDomains)
            {
                versions.TryGetValue(domain, out ulong version); // 缺席按 0
                // 比较一律用「不相等」:回档可能让计数变小(设计 §3.2)。
                if (fullPull || !_record.TryGetCount((int)domain, out ulong cached) || cached != version)
                    _dirty[domain] = version;
            }
            MarkConsumersPending();
            Log(fullPull ? "摘要已到,全部域重新拉取" : $"摘要已到,{_dirty.Count} 个域需要拉取");
        }

        private void MarkAllDirty(bool versionsKnown)
        {
            ResetDirty(versionsKnown);
            foreach (var domain in KnownDomains) _dirty[domain] = 0;
            MarkConsumersPending();
        }

        private void ResetDirty(bool versionsKnown)
        {
            _dirty.Clear();
            ++_dirtyEpoch;
            _dirtyValid = true;
            _dirtyVersionsKnown = versionsKnown;
        }

        private void MarkConsumersPending()
        {
            foreach (var consumer in _consumers) consumer.Pending = _dirty.ContainsKey(consumer.Domain);
        }

        /// <summary>
        /// 把本批标脏通知给消费者。在 Tick 里投递而不在推送处理器里:消费者会发请求,不放在 gate 收包的调用栈上。
        /// 回调里可能重入 Tick、注册 / 注销、甚至换落地,所以先拍下名单,每次回调前按最新状态复核。
        /// </summary>
        private void DeliverDirty()
        {
            if (!_dirtyValid) return;
            List<Consumer> due = null;
            foreach (var consumer in _consumers)
                if (consumer.Pending) (due ??= new List<Consumer>()).Add(consumer);
            if (due == null) return;
            int epoch = _dirtyEpoch;
            foreach (var consumer in due)
            {
                if (_disposed || epoch != _dirtyEpoch) return;
                if (!consumer.Pending || !consumer.Active) continue;
                consumer.Pending = false;
                if (!_dirty.TryGetValue(consumer.Domain, out ulong version)) continue; // 已被别的消费者确认
                try
                {
                    consumer.OnDirty(new SyncPullTicket(epoch, consumer.Domain, version, _dirtyVersionsKnown));
                }
                catch (Exception ex)
                {
                    // 一个消费者出错不影响别的域;脏标保留,下次落地或打开面板再拉。
                    Log($"域 {consumer.Domain} 的消费者处理标脏时出错: {ex.Message}");
                }
            }
        }

        // ── 缓存读写(容错:读不到按没有缓存,写失败只会让下次多拉)──────────────

        private SyncDigestRecord LoadRecord(ulong player)
        {
            try { return _store.Load(player); }
            catch (Exception ex) { Log($"读取摘要缓存失败,按无缓存处理: {ex.Message}"); return null; }
        }

        private void SaveRecord()
        {
            if (_record == null || _player == 0) return;
            try { _store.Save(_player, _record); }
            catch (Exception ex) { Log($"写入摘要缓存失败: {ex.Message}"); }
        }

        private void DeleteRecord(ulong player)
        {
            if (player == 0) return;
            try { _store.Delete(player); }
            catch (Exception ex) { Log($"清除摘要缓存失败: {ex.Message}"); }
        }

        private void Log(string message) => _log?.Invoke(message);

        private static bool IsKnown(SyncDomain domain) =>
            domain != SyncDomain.Unspecified && Enum.IsDefined(typeof(SyncDomain), domain);

        private static SyncDomain[] CollectKnownDomains()
        {
            var all = (SyncDomain[])Enum.GetValues(typeof(SyncDomain));
            var known = new List<SyncDomain>(all.Length);
            foreach (var domain in all)
                if (domain != SyncDomain.Unspecified) known.Add(domain);
            return known.ToArray();
        }
    }

    /// <summary>
    /// 把现有的域客户端接到 <see cref="LandingSyncClient"/>(设计 §4.3 第 5 条)。由持有这些客户端的界面宿主
    /// 在建好客户端之后各调一次,销毁客户端之前 Dispose 返回值。域客户端本身不认识 LandingSyncClient,
    /// 只暴露通用钩子(SyncReadStarted / QueueRefresh / QueueSyncRefresh),所以生成的 SyncDomain 只在本文件里出现。
    /// 新域接入照此追加一个 Bind 方法。现有接线:组队(BindTeam,TeamUiRoot)、好友申请(BindFriendRequests,
    /// SocialUiRoot)、帮会四个域(BindGuild,GuildUiRoot)。
    /// </summary>
    public static class LandingSyncBindings
    {
        /// <summary>
        /// TEAM / TEAM_INVITE:TeamClient 每次落地本来就会拉 GetMyTeam / ListMyInvites,不改它的触发方式,
        /// 只在「发出晚于摘要到达」的那次读取成功后确认。落地时的探针通常早于摘要,那一次不确认,
        /// 该域就留到面板刷新或下次落地 —— 反正每次落地都会拉,代价为零。
        /// </summary>
        public static IDisposable BindTeam(LandingSyncClient sync, TeamClient team)
        {
            if (sync == null) throw new ArgumentNullException(nameof(sync));
            if (team == null) throw new ArgumentNullException(nameof(team));
            Func<TeamAction, Action> hook = action => action switch
            {
                TeamAction.Refresh => AckOnSuccess(sync, SyncDomain.Team),
                TeamAction.ListInvites => AckOnSuccess(sync, SyncDomain.TeamInvite),
                _ => null,
            };
            team.SyncReadStarted = hook;
            return new Binding(() =>
            {
                if (team.SyncReadStarted == hook) team.SyncReadStarted = null;
            });
        }

        /// <summary>
        /// FRIEND_REQUEST:标脏时排队拉一次 GetPendingRequests,任何一次发出晚于摘要的拉取成功后确认。
        /// 只拉取,不据标脏本身给任何提示(被动方隐私,设计 §3.5 第 4 条)。
        /// FRIEND_LIST / FRIEND_BLOCK 暂无落地消费者(好友列表只在组队邀请窗按需拉),每次落地都会是脏的,没人拉,代价为零。
        /// </summary>
        public static IDisposable BindFriendRequests(LandingSyncClient sync, FriendClient friends)
        {
            if (sync == null) throw new ArgumentNullException(nameof(sync));
            if (friends == null) throw new ArgumentNullException(nameof(friends));
            Func<Action> hook = () => AckOnSuccess(sync, SyncDomain.FriendRequest);
            friends.SyncReadStarted = hook;
            var registration = sync.RegisterConsumer(SyncDomain.FriendRequest, _ => friends.QueueRefresh());
            return new Binding(() =>
            {
                registration.Dispose();
                if (friends.SyncReadStarted == hook) friends.SyncReadStarted = null;
            });
        }

        /// <summary>
        /// 帮会四个域:
        ///  - GUILD:标脏时后台排队拉 GetPlayerGuild,快照成功应用或回「不在帮」时确认;
        ///  - GUILD_APPLICATION:标脏时排队。不在帮时拉 ListMyGuildApplications、本人申请落定后确认;
        ///    已入帮时不拉(服务端入帮那一刻已删光本人申请),由那次 GetPlayerGuild 拉到「在帮」快照时一并确认
        ///    (见 <see cref="GuildSnapshotAck"/>),否则在帮玩家这个域永远是脏的,每次落地都要后台多拉一次快照;
        ///  - GUILD_ECONOMY / GUILD_ACTIVITY:**只被动确认**,不注册消费者,玩家进捐献 / 商店 / 活动页时的自动拉取
        ///    发出晚于摘要就顺带确认;ECONOMY 要捐献与商店两次读取都确认(见 <see cref="EconomyAck"/>)。
        /// 经济与活动不后台拉的理由:这两页在帮会窗口之外没有任何显示,GuildClient 每次落地都清掉它们的快照、
        /// 进页必拉,后台拉只多 RPC(全帮扇出的变化会让上百人同时多拉);而且 Request 的错误路径会把 GuildClient
        /// 置成需重新登录的隔离态,玩家什么都没做就看到「请重新登录角色后再试」。要改成主动拉,在这里多注册两个消费者即可。
        /// 排队的读取由 GuildUiRoot 每帧发出(窗口隐藏时 DrainSyncQueued,可见时 DrainQueued),一次一个。
        /// 只拉取,不据标脏本身给任何提示(被动方隐私,设计 §3.5 第 4 条)。
        /// </summary>
        public static IDisposable BindGuild(LandingSyncClient sync, GuildClient guild)
        {
            if (sync == null) throw new ArgumentNullException(nameof(sync));
            if (guild == null) throw new ArgumentNullException(nameof(guild));
            var economy = new EconomyAck(sync);
            Func<GuildSyncRead, Action> hook = read => read switch
            {
                GuildSyncRead.Guild => GuildSnapshotAck(sync, guild),
                GuildSyncRead.MyApplications => AckOnSuccess(sync, SyncDomain.GuildApplication),
                GuildSyncRead.Activities => AckOnSuccess(sync, SyncDomain.GuildActivity),
                GuildSyncRead.Donations => economy.Begin(GuildSyncRead.Donations),
                GuildSyncRead.Shop => economy.Begin(GuildSyncRead.Shop),
                _ => null,
            };
            guild.SyncReadStarted = hook;
            var guildInfo = sync.RegisterConsumer(SyncDomain.Guild, _ => guild.QueueSyncRefresh(GuildSyncRead.Guild));
            var applications = sync.RegisterConsumer(SyncDomain.GuildApplication,
                _ => guild.QueueSyncRefresh(GuildSyncRead.MyApplications));
            return new Binding(() =>
            {
                guildInfo.Dispose();
                applications.Dispose();
                if (guild.SyncReadStarted == hook) guild.SyncReadStarted = null;
            });
        }

        /// <summary>发出拉取时取票;该域此刻不脏就返回 null(这次拉取不需要确认)。</summary>
        private static Action AckOnSuccess(LandingSyncClient sync, SyncDomain domain) =>
            sync.TryBeginPull(domain, out var ticket) ? () => sync.Ack(ticket) : (Action)null;

        /// <summary>
        /// GetPlayerGuild 发出时同时为 GUILD 与 GUILD_APPLICATION 各取一张票,返回复合确认;两个域此刻都不脏时返回 null。
        /// GUILD 票照常确认。GUILD_APPLICATION 票只在确认那一刻在帮(<see cref="GuildClient.Info"/> 非空)才确认:
        /// GuildClient 只在快照成功应用(在帮)与回「不在帮」两处调确认委托,读 Info 正好区分这两种结果。
        /// 在帮 = 服务端入帮那一刻已删光本人申请,这次发出晚于摘要的快照同时落定了「本人申请为空」;
        /// 不在帮时本人申请要另拉,由 ListMyGuildApplications 那次读取确认。
        /// </summary>
        private static Action GuildSnapshotAck(LandingSyncClient sync, GuildClient guild)
        {
            bool guildDirty = sync.TryBeginPull(SyncDomain.Guild, out var guildTicket);
            bool applicationsDirty = sync.TryBeginPull(SyncDomain.GuildApplication, out var applicationTicket);
            if (!guildDirty && !applicationsDirty) return null;
            return () =>
            {
                bool inGuild = guild.Info != null; // 先读:下面的 Ack 会触发 Changed,不让回调里的改动影响这次判断
                if (guildDirty) sync.Ack(guildTicket);
                if (applicationsDirty && inGuild) sync.Ack(applicationTicket);
            };
        }

        /// <summary>
        /// GUILD_ECONOMY 由捐献页与商店页两份快照组成:同一批标脏下两次读取都在摘要之后发出并成功,才确认(L-2)。
        /// 只成功一次不推进:另一页仍可能停在摘要之前的状态。批次变了(换落地、摘要重算)从头记,
        /// 比当前记录更旧的批次的完成直接忽略(它的票也已作废)。
        /// </summary>
        private sealed class EconomyAck
        {
            private readonly LandingSyncClient _sync;
            private SyncPullTicket _ticket;
            private bool _has, _donations, _shop;

            public EconomyAck(LandingSyncClient sync) => _sync = sync;

            /// <summary>读取发出时取票;该域此刻不脏就返回 null(这次读取不计入)。</summary>
            public Action Begin(GuildSyncRead part) =>
                _sync.TryBeginPull(SyncDomain.GuildEconomy, out var ticket) ? () => Complete(ticket, part) : (Action)null;

            private void Complete(SyncPullTicket ticket, GuildSyncRead part)
            {
                if (_has && ticket.Epoch < _ticket.Epoch) return;
                if (!_has || ticket.Epoch != _ticket.Epoch)
                {
                    _ticket = ticket;
                    _has = true;
                    _donations = _shop = false;
                }
                if (part == GuildSyncRead.Donations) _donations = true;
                else if (part == GuildSyncRead.Shop) _shop = true;
                if (!_donations || !_shop) return;
                _has = false;
                _sync.Ack(_ticket);
            }
        }

        private sealed class Binding : IDisposable
        {
            private Action _unbind;

            public Binding(Action unbind) => _unbind = unbind;

            public void Dispose()
            {
                var unbind = _unbind;
                _unbind = null;
                unbind?.Invoke();
            }
        }
    }
}
