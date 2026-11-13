using System;
using System.Collections.Generic;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.Battle
{
    /// <summary>
    /// 近期战斗结果的客户端半边(服务端 docs/design/battle-result-recovery.md §7)。断线或离线期间结算入账的那一局,
    /// 现场的终局通知已经错过了;落地后由这里向 scene 补查,把玩家还没看过的结果交给界面补弹,界面展示之后再确认已读。
    ///
    /// 规则(与设计 §7 逐条对应):
    ///  - **落地就查一次**:落地判定与 <see cref="Sync.LandingSyncClient"/> 同一口径 —— 已进游戏且传输就绪时取
    ///    (PlayerId, 连接身份),变了就是一次新的落地。InGame 在 NotifyEnterScene 之后才为真;scene 还在异步载入实体时
    ///    丢弃请求、不回包,所以必须等进场之后再发。
    ///  - **没有战斗上下文的终局**再触发一次查询(<see cref="HandleEndWithoutContext"/>),与落地查询共用单飞:
    ///    在途时再来的触发,只在它回来之后多查一次。
    ///  - **查询失败**(传输错误、超时或回包带 tip)按 15 / 30 / 60 秒封顶退避,成功即归零。
    ///  - **去重**:进程内记着已经展示过的 battle_id。回包里 seen = false、没展示过、本次落地也还没交出去的,
    ///    按 result_seq 从小到大逐个交给 <see cref="OnRecoveredResult"/>。交出去而界面没来得及展示(比如断线)的,下次落地再交。
    ///  - **确认**:界面真正展示之后调 <see cref="NoteShown"/>(补弹的面板与有上下文的现场结算面板都调),这里发
    ///    AckBattleResults,一次至多 10 个。失败不重弹,留在待确认列表里,下次落地先补发,再查。
    ///    待确认只在内存里:进程退出就丢,最多让同一局下次再弹一次(服务端设计 §6「丢了最多让客户端再弹一次」)。
    ///
    /// 不引用 UnityEngine(时钟、连接身份、是否在游戏里都由调用方注入),EditMode 用假传输穷打。
    /// </summary>
    public sealed class BattleResultRecoveryClient : IDisposable
    {
        /// <summary>单例挂接(实例由 GameClient 持有,静态 Instance 供 UI 层解析;测试直接 new)。</summary>
        public static BattleResultRecoveryClient Instance { get; private set; }

        /// <summary>查询连续失败第 1、2、3 次(及以后)之后等多久再查(秒)。</summary>
        public static readonly IReadOnlyList<float> QueryRetryDelaysSeconds = new[] { 15f, 30f, 60f };

        /// <summary>一次确认最多带几个 battle_id(服务端多于 10 个回 1015)。</summary>
        public const int MaxBattleIdsPerAck = 10;

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<bool> _inGame;
        private readonly Func<float> _clock;
        private readonly Action<string> _log;

        // 当前这次落地;玩家 0 = 不在游戏里。epoch 在每次落地(及 Dispose)时加一,上一次落地的回包到了直接丢弃。
        private ulong _player;
        private object _connection;
        private int _epoch;
        private bool _disposed;

        private bool _queryWanted;
        private bool _queryInFlight;
        private bool _queryAgain;
        private int _queryFailures;
        private float _nextQueryAt = float.NegativeInfinity;

        private readonly HashSet<ulong> _shown = new HashSet<ulong>();
        private readonly HashSet<ulong> _raisedThisLanding = new HashSet<ulong>();
        // 按玩家分开:换了角色之后,上一个角色的待确认不能用这个角色的会话发(scene 只认会话里的本人)。
        private readonly Dictionary<ulong, List<ulong>> _pendingAck = new Dictionary<ulong, List<ulong>>();
        private bool _ackInFlight;

        /// <summary>
        /// 查到一局玩家还没看过的结果(按 result_seq 从小到大逐个触发)。界面弹出之后必须调 <see cref="NoteShown"/>,
        /// 这一局才会被确认;不调的话,下次落地还会再交一次。
        /// </summary>
        public event Action<PlayerBattleResult> OnRecoveredResult;

        /// <param name="net">gate 管线(请求-响应)。</param>
        /// <param name="connectionIdentity">当前连接的不透明身份,只做引用比较(GameClient.GateConnectionIdentity)。</param>
        /// <param name="inGame">是否已进游戏(GameClient.InGame,NotifyEnterScene 之后为真);连接就绪由 <paramref name="net"/> 的 IsReady 给出。</param>
        /// <param name="clock">单调时钟(秒),只用来算退避。</param>
        /// <param name="log">可选日志出口。</param>
        public BattleResultRecoveryClient(IBattleTransport net, Func<object> connectionIdentity, Func<bool> inGame,
                                          Func<float> clock, Action<string> log = null)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _connectionIdentity = connectionIdentity ?? throw new ArgumentNullException(nameof(connectionIdentity));
            _inGame = inGame ?? throw new ArgumentNullException(nameof(inGame));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log;
            // 构造不发请求:第一次查询由落地之后的 Tick 发出。
        }

        /// <summary>生产入口:创建实例并登记为单例(GameClient 构造时调用)。</summary>
        public static BattleResultRecoveryClient Attach(IBattleTransport net, Func<object> connectionIdentity,
                                                        Func<bool> inGame, Func<float> clock, Action<string> log = null)
        {
            Instance?.Dispose();
            var client = new BattleResultRecoveryClient(net, connectionIdentity, inGame, clock, log);
            Instance = client;
            return client;
        }

        /// <summary>这一局是不是「本局作废」(服务端记录的 outcome 是 VOID,明细全空)。</summary>
        public static bool IsVoid(PlayerBattleResult result)
            => result != null && result.Outcome == (uint)eBattleOutcome.BattleOutcomeVoid;

        /// <summary>查询在途。</summary>
        public bool QueryInFlight => _queryInFlight;

        /// <summary>当前玩家还没确认成功的 battle_id 个数。</summary>
        public int PendingAckCount => _player != 0 && _pendingAck.TryGetValue(_player, out var ids) ? ids.Count : 0;

        /// <summary>由 GameClient.Tick 每帧驱动(也会在 Call 的等待循环里重入,在途标志防重复发送)。</summary>
        public void Tick()
        {
            if (_disposed) return;
            ObserveLanding();
            if (_player == 0) return;
            if (_queryWanted && !_queryInFlight && _clock() >= _nextQueryAt) SendQuery();
        }

        /// <summary>
        /// 收到一份没有战斗上下文的终局(<see cref="BattleClient.OnBattleEndWithoutContext"/>):这一局的结果可能刚公开,再查一次。
        /// alreadyNotified = 这一局已经用别的方式告诉过玩家(作废提示),视同展示过:直接确认,补查时不再弹。
        /// </summary>
        public void HandleEndWithoutContext(ulong battleId, bool alreadyNotified)
        {
            if (_disposed) return;
            ObserveLanding();
            if (alreadyNotified && battleId != 0) NoteShown(battleId);
            if (_player == 0) return; // 不在游戏里:下次落地本来就会查
            if (_queryInFlight) _queryAgain = true;
            else _queryWanted = true;
        }

        /// <summary>
        /// 界面已经把这一局展示给玩家(补弹的面板,或有上下文的现场结算面板):记为展示过,发确认。重复调用无副作用。
        /// </summary>
        public void NoteShown(ulong battleId)
        {
            if (_disposed || battleId == 0) return;
            ObserveLanding();
            ulong player = _player != 0 ? _player : _net.PlayerId;
            if (player == 0 || !_shown.Add(battleId)) return;
            if (!_pendingAck.TryGetValue(player, out var ids))
            {
                ids = new List<ulong>();
                _pendingAck[player] = ids;
            }
            ids.Add(battleId);
            SendAck();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ++_epoch;
            _queryInFlight = false;
            _ackInFlight = false;
            OnRecoveredResult = null;
            if (Instance == this) Instance = null;
        }

        // ── 落地 ────────────────────────────────────────────

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

        private void BeginLanding(ulong player, object connection)
        {
            _player = player;
            _connection = connection;
            ++_epoch; // 上一次落地的在途查询 / 确认作废:回包到了也不再改状态
            _queryInFlight = false;
            _queryAgain = false;
            _ackInFlight = false;
            _queryFailures = 0;
            _nextQueryAt = float.NegativeInfinity;
            _raisedThisLanding.Clear();
            _queryWanted = player != 0;
            if (player == 0) return;
            // 先补发上次没确认成功的,再查(设计 §7 第 5 条)。查询由紧接着的 Tick 发出。
            SendAck();
        }

        // ── 查询 ────────────────────────────────────────────

        private void SendQuery()
        {
            int epoch = _epoch;
            _queryInFlight = true;
            _queryWanted = false;
            _queryAgain = false;
            _net.Call(SceneBattleResultClientPlayerRpc.GetRecentBattleResultsMessageId, new GetRecentBattleResultsRequest(),
                GetRecentBattleResultsResponse.Parser,
                response =>
                {
                    if (_disposed || epoch != _epoch) return;
                    _queryInFlight = false;
                    if (response == null || HasTip(response.ErrorMessage))
                    {
                        FailQuery(response == null ? "empty reply" : $"tip={response.ErrorMessage.Id}");
                        return;
                    }
                    _queryFailures = 0;
                    _nextQueryAt = float.NegativeInfinity;
                    if (_queryAgain)
                    {
                        _queryAgain = false;
                        _queryWanted = true;
                    }
                    Deliver(response.Results);
                },
                error =>
                {
                    if (_disposed || epoch != _epoch) return;
                    _queryInFlight = false;
                    FailQuery(error);
                });
        }

        private void FailQuery(string reason)
        {
            ++_queryFailures;
            float delay = QueryRetryDelaysSeconds[Math.Min(_queryFailures, QueryRetryDelaysSeconds.Count) - 1];
            _nextQueryAt = _clock() + delay;
            _queryWanted = true;
            _queryAgain = false;
            _log?.Invoke($"recent battle results query failed ({reason}); retrying in {delay:0}s");
        }

        private void Deliver(IEnumerable<PlayerBattleResult> results)
        {
            var fresh = new List<PlayerBattleResult>();
            foreach (var result in results)
            {
                if (result == null || result.BattleId == 0) continue;
                if (result.Seen)
                {
                    DropPendingAck(_player, result.BattleId); // 服务端已经记为已读:上一次确认其实到了
                    continue;
                }
                if (_shown.Contains(result.BattleId) || _raisedThisLanding.Contains(result.BattleId)) continue;
                fresh.Add(result);
            }
            fresh.Sort((a, b) => a.ResultSeq.CompareTo(b.ResultSeq));
            int epoch = _epoch;
            foreach (var result in fresh)
            {
                _raisedThisLanding.Add(result.BattleId);
                OnRecoveredResult?.Invoke(result);
                if (_disposed || epoch != _epoch) return; // 回调里断线或销毁:剩下的下次落地再交
            }
        }

        // ── 确认 ────────────────────────────────────────────

        private void SendAck()
        {
            if (_ackInFlight || _player == 0 || !_net.IsReady) return;
            if (!_pendingAck.TryGetValue(_player, out var ids) || ids.Count == 0) return;
            var batch = ids.GetRange(0, Math.Min(ids.Count, MaxBattleIdsPerAck));
            var request = new AckBattleResultsRequest();
            request.BattleIds.AddRange(batch);
            int epoch = _epoch;
            ulong player = _player;
            _ackInFlight = true;
            _net.Call(SceneBattleResultClientPlayerRpc.AckBattleResultsMessageId, request, AckBattleResultsResponse.Parser,
                response =>
                {
                    if (_disposed || epoch != _epoch) return;
                    _ackInFlight = false;
                    if (response == null || HasTip(response.ErrorMessage))
                    {
                        // 1006(冻结 / 交接中)等:不重弹,留在待确认列表里,下次落地先补发。
                        string reason = response == null ? "empty reply" : $"tip={response.ErrorMessage.Id}";
                        _log?.Invoke($"battle results acknowledgement refused ({reason}); kept for the next landing");
                        return;
                    }
                    foreach (ulong battleId in batch) DropPendingAck(player, battleId);
                    SendAck(); // 还有没发的,接着发
                },
                error =>
                {
                    if (_disposed || epoch != _epoch) return;
                    _ackInFlight = false;
                    _log?.Invoke($"battle results acknowledgement failed ({error}); kept for the next landing");
                });
        }

        private void DropPendingAck(ulong player, ulong battleId)
        {
            if (player == 0 || !_pendingAck.TryGetValue(player, out var ids)) return;
            ids.Remove(battleId);
            if (ids.Count == 0) _pendingAck.Remove(player);
        }

        private static bool HasTip(TipInfoMessage tip) => tip != null && tip.Id != 0;
    }
}
