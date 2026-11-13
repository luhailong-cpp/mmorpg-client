using System;
using System.Collections.Generic;
using System.Globalization;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using Rankpb;

namespace MmorpgClient.Game.Rank
{
    /// <summary>
    /// 通用榜真实读取。页、本人和附近名次均来自服务端，快照与请求板键对外隔离。
    /// 翻页沿用当前回包的榜代次，刷新重新查询最新榜；同一时刻只允许一个请求。
    /// 连点只保留最后一次意图，本地按 gate 配额节流；服务端限频后退避且不自动重试。
    /// 会话、连接、玩家和请求代次共同拒绝旧回调。旧传输适配仍按保守语义隔离超时。
    /// 按榜类型分榜(2026-11-12,服务端 docs/design/global-match-hierarchical-pools.md §8 R5):最后一次意图所在的榜就是
    /// 选中的榜,Page / Around / 名次 / 页数都取选中榜的快照;快照按榜类型分开存,回包只进它请求的那张榜。
    /// 切榜不作废在途请求(单请求在途与保守隔离契约不变),新榜的请求排在它后面;旧榜的迟到回包只进旧榜自己的快照,
    /// 不会出现在新榜的画面上,也不改状态行。
    /// </summary>
    public sealed class RankClient : IDisposable
    {
        public const uint PageSize = 5;
        public const float MinRequestIntervalSeconds = 0.4f;
        public const float MessageWindowSeconds = 2.5f;
        public const int SendQuota = 5;
        public const float RateLimitBackoffSeconds = MessageWindowSeconds;
        /// <summary>
        /// 打开论剑榜先看的榜:客户端的「匹配 PVP」入口只排 PVP_TEAM(BattleMatchEntryClient.StartPvp),玩家打完的评分在这张榜上;
        /// 1V1 / 5V5 两张榜客户端没有排队入口,放在后面的页签。
        /// </summary>
        public const uint DefaultBoardType = (uint)RankBoardType.MatchPvpTeam;
        public const string RecoveryMessage = "排行榜请求状态尚未确认，请重新登录角色后再试。";
        public const string LoadingMessage = "正在读取排行榜，请稍候…";
        public const string NotInGameMessage = "请进入角色后再打开排行榜。";
        public const string NotConfiguredMessage = "排行榜尚未配置。";
        public const string TransportFailureMessage = "排行榜请求未完成，请稍后重试。";
        public const string EmptyMessage = "榜上暂无记录，打一局本模式论剑即可上榜。";
        public const string TimeoutError = "rpc timeout";
        public const string ServerTipPrefix = "server tip=";
        private const uint MaxAroundRadius = 25;

        private sealed class PendingRequest
        {
            /// <summary>这次意图针对的榜类型:回包只进这张榜的快照。</summary>
            public uint BoardType;
            public RankBoardKey Board;
            public uint Page, Size, Radius;
            public bool Around, FollowDisplayedBoard;
        }

        /// <summary>一张榜最近一次读到的快照。读到一页时清掉附近名次,与分榜之前同一口径。</summary>
        private sealed class BoardSnapshot
        {
            public GetRankPageResponse Page;
            public GetRankAroundResponse Around;
        }

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<float> _clock;
        private readonly Queue<float> _sentAt = new();
        private readonly Dictionary<uint, BoardSnapshot> _boards = new();
        private object _observedConnection;
        private ulong _observedPlayer;
        private int _generation;
        private bool _disposed;
        private PendingRequest _pending;
        private uint _selectedBoardType = DefaultBoardType;
        private float _lastSendAt = float.NegativeInfinity;
        private float _rateLimitedUntil = float.NegativeInfinity;

        public ulong PlayerId => _net.PlayerId;
        /// <summary>界面正在看的榜类型;换角色、换连接(Reset)后回到 <see cref="DefaultBoardType"/>。</summary>
        public uint SelectedBoardType => _selectedBoardType;
        public GetRankPageResponse Page => Shown?.Page?.Clone();
        public GetRankAroundResponse Around => Shown?.Around?.Clone();
        public bool Busy { get; private set; }
        public bool Waiting => Busy || _pending != null;
        public bool RequiresReconnect { get; private set; }
        public string Status { get; private set; } = "";
        public uint CurrentPage => Shown?.Page?.Page ?? 1;
        public ulong PageCount => Math.Max(1UL, Shown?.Page?.PageCount ?? 1UL);
        /// <summary>当前快照的精确名次上限(服务端只精确数到这一名,翻页也只到这一名);没有快照时为 0。</summary>
        public ulong RankLimit => Shown?.Around?.RankLimit ?? Shown?.Page?.RankLimit ?? 0UL;
        public event Action Changed;

        private BoardSnapshot Shown => _boards.TryGetValue(_selectedBoardType, out var snapshot) ? snapshot : null;

        public RankClient(IBattleTransport net, Func<object> connectionIdentity, Func<float> clock = null)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _connectionIdentity = connectionIdentity ?? (() => _net);
            _clock = clock ?? (() => UnityEngine.Time.realtimeSinceStartup);
            _observedConnection = _connectionIdentity();
            _observedPlayer = _net.PlayerId;
            _net.Disconnected += HandleDisconnected;
        }

        /// <summary>
        /// 一种榜的最新可见代次:空 period/generation 由服务端解析成这种榜最新的一代(评分榜即 period "all"、第 1 代),
        /// 具体代次以首个回包为准。
        /// </summary>
        public static RankBoardKey LatestBoard(uint boardType) => new() { BoardType = boardType, Scope = RankScope.Global };

        /// <summary>
        /// 切到另一种榜,读它最新一代的第 1 页;点已选中的榜不重发(刷新走 <see cref="Refresh"/>)。
        /// 在途请求不作废,新榜的请求排在它后面,只保留最后一次切换。
        /// </summary>
        public void SelectBoard(uint boardType)
        {
            if (_disposed || boardType == _selectedBoardType) return;
            ReadPage(LatestBoard(boardType), 1, PageSize);
        }

        /// <summary>界面翻页:沿用选中榜正在显示的榜键;排队期间首个回包刚确定的榜代次也会被沿用。</summary>
        public void Load(uint page)
            => Request(new PendingRequest
            {
                BoardType = _selectedBoardType, Board = DisplayedBoard(_selectedBoardType),
                Page = Math.Max(1u, page), Size = PageSize, FollowDisplayedBoard = true,
            });

        /// <summary>"我的附近":与翻页一样沿用选中榜正在显示的榜键与代次,不跳到别的榜,也不换成最新代次。</summary>
        public void LoadAround(uint radius)
            => Request(new PendingRequest
            {
                BoardType = _selectedBoardType, Board = DisplayedBoard(_selectedBoardType),
                Around = true, Radius = Math.Min(radius, MaxAroundRadius), FollowDisplayedBoard = true,
            });

        /// <summary>刷新选中榜的最新一代，而不是沿用已封榜的旧代次。</summary>
        public void Refresh() => ReadPage(LatestBoard(_selectedBoardType), 1, PageSize);

        /// <summary>读指定榜的一页;这张榜同时成为选中的榜。</summary>
        public void ReadPage(RankBoardKey board, uint page = 1, uint size = 20)
        {
            if (!Valid(board)) { if (!_disposed) Show(NotConfiguredMessage); return; }
            Request(new PendingRequest
            {
                BoardType = board.BoardType, Board = board.Clone(),
                Page = Math.Max(1u, page), Size = Math.Min(size == 0 ? 20u : size, 50u),
            });
        }

        /// <summary>读指定榜里本人附近的名次;这张榜同时成为选中的榜。</summary>
        public void ReadAround(RankBoardKey board, uint radius = 10)
        {
            if (!Valid(board)) { if (!_disposed) Show(NotConfiguredMessage); return; }
            Request(new PendingRequest
            {
                BoardType = board.BoardType, Board = board.Clone(), Around = true, Radius = Math.Min(radius, MaxAroundRadius),
            });
        }

        private static bool Valid(RankBoardKey board)
            => board != null && board.BoardType != 0 && board.Scope == RankScope.Global;

        /// <summary>这种榜正在显示的榜键(带回包解析出的代次);还没有快照时为 null,发送时再取最新代次。</summary>
        private RankBoardKey DisplayedBoard(uint boardType)
        {
            if (!_boards.TryGetValue(boardType, out var snapshot)) return null;
            return snapshot.Page?.Board?.Clone() ?? snapshot.Around?.Board?.Clone();
        }

        private void Request(PendingRequest request)
        {
            if (_disposed) return;
            ObserveConnection();
            // 最后一次意图决定选中的榜。切到别的榜时在途请求照常等完,只是界面马上换到新榜(排队期间也要重画)。
            bool switched = request.BoardType != _selectedBoardType;
            _selectedBoardType = request.BoardType;
            if (Busy)
            {
                _pending = request;
                if (switched) Changed?.Invoke();
                return;
            }
            if (RequiresReconnect) { Show(RecoveryMessage); return; }
            if (!_net.IsReady || PlayerId == 0) { Show(NotInGameMessage); return; }
            float now = _clock();
            if (now < _rateLimitedUntil)
            {
                _pending = null;
                Show(DescribeTip((uint)common_error.KRateLimitExceeded));
                return;
            }
            if (!HasSendBudget(now))
            {
                _pending = request;
                Show(LoadingMessage);
                return;
            }
            _pending = null;
            RecordSend(now);
            int generation = ++_generation;
            ulong player = PlayerId;
            object connection = _observedConnection;
            Busy = true;
            Status = LoadingMessage;
            Changed?.Invoke();
            // Changed 订阅者可能退出角色、Reset 或 Dispose，不能在其后再发一个已作废的请求。
            if (_disposed || generation != _generation || !Busy || !_net.IsReady || PlayerId != player
                || !ReferenceEquals(connection, _connectionIdentity())) return;
            uint boardType = request.BoardType;
            var board = request.FollowDisplayedBoard
                ? (request.Board ?? DisplayedBoard(boardType) ?? LatestBoard(boardType)) : request.Board;
            if (request.Around)
            {
                _net.Call(MessageIds.RankGetRankAround, new GetRankAroundRequest { Board = board, Radius = request.Radius },
                    GetRankAroundResponse.Parser, response =>
                    {
                        if (!CompleteCurrent(generation, player, connection)) return;
                        Apply(boardType, response);
                        FlushPending();
                        Changed?.Invoke();
                    }, error => Fail(generation, player, connection, boardType, error));
            }
            else
            {
                _net.Call(MessageIds.RankGetRankPage, new GetRankPageRequest { Board = board, Page = request.Page, PageSize = request.Size },
                    GetRankPageResponse.Parser, response =>
                    {
                        if (!CompleteCurrent(generation, player, connection)) return;
                        Apply(boardType, response);
                        FlushPending();
                        Changed?.Invoke();
                    }, error => Fail(generation, player, connection, boardType, error));
            }
        }

        private bool CompleteCurrent(int generation, ulong player, object connection)
        {
            if (_disposed || generation != _generation || !_net.IsReady || player == 0 || PlayerId != player
                || !ReferenceEquals(connection, _connectionIdentity())) return false;
            // 完成即消耗代次；重复 success/error 回调不能再改同一请求的结果。
            ++_generation;
            Busy = false;
            return true;
        }

        private void Fail(int generation, ulong player, object connection, uint boardType, string error)
        {
            if (!CompleteCurrent(generation, player, connection)) return;
            uint tip = ParseEnvelopeTip(error);
            if (error == TimeoutError || error == "timeout")
            {
                // 结果未知:整个客户端隔离到重连,每张榜的快照一起作废,不在恢复提示下面挂任何一张旧榜。
                RequiresReconnect = true;
                _pending = null;
                _boards.Clear();
                Status = RecoveryMessage;
            }
            else if (tip == (uint)common_error.KRateLimitExceeded)
            {
                // 限频管的是整个客户端的发送,与哪张榜无关:排队的意图(包括刚切的榜)一并作废,不自动补发。
                _pending = null;
                _rateLimitedUntil = Math.Max(_rateLimitedUntil, _clock() + RateLimitBackoffSeconds);
                Status = DescribeTip(tip);
            }
            else
            {
                _boards.Remove(boardType);
                if (boardType == _selectedBoardType) Status = tip == 0 ? TransportFailureMessage : DescribeTip(tip);
                FlushPending();
            }
            Changed?.Invoke();
        }

        private void Apply(uint boardType, GetRankPageResponse response)
        {
            if (!Accept(boardType, response == null, response?.ErrorMessage, response?.Board)) return;
            var snapshot = SnapshotFor(boardType);
            snapshot.Page = response.Clone();
            snapshot.Around = null;
            if (boardType == _selectedBoardType) Status = response.Entries.Count == 0 ? EmptyMessage : "";
        }

        private void Apply(uint boardType, GetRankAroundResponse response)
        {
            if (!Accept(boardType, response == null, response?.ErrorMessage, response?.Board)) return;
            SnapshotFor(boardType).Around = response.Clone();
            if (boardType == _selectedBoardType) Status = response.Entries.Count == 0 ? EmptyMessage : "";
        }

        /// <summary>
        /// 回包能不能落进它请求的那张榜。读失败(业务拒绝、空回包)与分榜之前同一口径:作废这张榜的快照,不在失败提示下面挂旧页。
        /// 服务端成功时总会回显解析后的榜键;榜类型对不上按读失败处理,别的榜的数据不存进这张榜。状态行只在这张榜正被选中时改。
        /// </summary>
        private bool Accept(uint boardType, bool missing, TipInfoMessage error, RankBoardKey echoed)
        {
            string failure = missing ? TransportFailureMessage
                : error?.Id > 0 ? DescribeTip(error.Id)
                : echoed?.BoardType != boardType ? TransportFailureMessage
                : null;
            if (failure == null) return true;
            _boards.Remove(boardType);
            if (boardType == _selectedBoardType) Status = failure;
            return false;
        }

        private BoardSnapshot SnapshotFor(uint boardType)
        {
            if (!_boards.TryGetValue(boardType, out var snapshot)) _boards[boardType] = snapshot = new BoardSnapshot();
            return snapshot;
        }

        public void Tick()
        {
            if (_disposed) return;
            ObserveConnection();
            if (_pending == null || Busy || _clock() < _rateLimitedUntil || !HasSendBudget(_clock())) return;
            FlushPending();
        }

        private void FlushPending()
        {
            if (_pending == null || Busy || RequiresReconnect) return;
            var request = _pending;
            _pending = null;
            Request(request);
        }

        public void Reset()
        {
            if (_disposed) return;
            if (Busy) RequiresReconnect = true;
            ++_generation;
            Busy = false;
            _pending = null;
            _boards.Clear();
            // 换角色、换连接后从默认榜重新开始:选中的榜和快照一样不跨角色残留。
            _selectedBoardType = DefaultBoardType;
            Status = RequiresReconnect ? RecoveryMessage : "";
            Changed?.Invoke();
        }

        public void ObserveConnection()
        {
            if (_disposed) return;
            object connection = _connectionIdentity();
            if (ReferenceEquals(connection, _observedConnection) && PlayerId == _observedPlayer) return;
            _observedConnection = connection;
            _observedPlayer = PlayerId;
            Busy = false;
            RequiresReconnect = false;
            ClearSendThrottle();
            Reset();
        }

        private void HandleDisconnected()
        {
            _observedConnection = _connectionIdentity();
            _observedPlayer = PlayerId;
            Busy = false;
            RequiresReconnect = false;
            ClearSendThrottle();
            Reset();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _net.Disconnected -= HandleDisconnected;
            _disposed = true;
            ++_generation;
            Busy = false;
            _pending = null;
            _boards.Clear();
        }

        private bool HasSendBudget(float now) => now >= _lastSendAt + MinRequestIntervalSeconds
            && (_sentAt.Count < SendQuota || now >= _sentAt.Peek() + MessageWindowSeconds);

        private void RecordSend(float now)
        {
            _lastSendAt = now;
            _sentAt.Enqueue(now);
            while (_sentAt.Count > SendQuota) _sentAt.Dequeue();
        }

        private void ClearSendThrottle()
        {
            _sentAt.Clear();
            _lastSendAt = float.NegativeInfinity;
            _rateLimitedUntil = float.NegativeInfinity;
        }

        private void Show(string status) { Status = status; Changed?.Invoke(); }

        /// <summary>match 的 score 是分×100，始终用整数格式化，包括负分与 Int64 最小值。</summary>
        public static string FormatRating(long ratingCenti)
        {
            bool negative = ratingCenti < 0;
            ulong value = negative ? (ulong)(-(ratingCenti + 1)) + 1UL : (ulong)ratingCenti;
            return (negative ? "-" : "") + (value / 100).ToString(CultureInfo.InvariantCulture) + "."
                + (value % 100).ToString("D2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 列表里的名次。服务端只精确数到第 rank_limit 名(docs/design/leaderboard-system.md「2026-11-11 前 N 名精确、之后显示 N+」),
        /// 排在其后的条目 BeyondRankLimit = true、Rank = 0,显示「{rank_limit}+」。
        /// </summary>
        public static string FormatRowRank(RankEntry entry, ulong rankLimit) => entry.BeyondRankLimit
            ? BeyondRankLabel(rankLimit)
            : entry.Rank.ToString("D2", CultureInfo.InvariantCulture);

        /// <summary>"我的名次"那一格:精确时"第 N 名",排在上限之后显示「{rank_limit}+」。</summary>
        public static string FormatSelfRank(RankEntry self, ulong rankLimit) => self.BeyondRankLimit
            ? BeyondRankLabel(rankLimit)
            : "第 " + self.Rank.ToString(CultureInfo.InvariantCulture) + " 名";

        /// <summary>上限之后的名次标签。服务端置 BeyondRankLimit 时总会带上限;万一没带,不编数字,显示"未计名次"。</summary>
        public static string BeyondRankLabel(ulong rankLimit) => rankLimit == 0
            ? "未计名次"
            : rankLimit.ToString(CultureInfo.InvariantCulture) + "+";

        public static string DescribeTip(TipInfoMessage tip) => tip == null || tip.Id == 0 ? "" : DescribeTip(tip.Id);

        public static string DescribeTip(uint tipId) => tipId switch
        {
            (uint)rank_error.KRankBoardNotFound => "本期排行榜尚未开放。",
            (uint)rank_error.KRankGenerationClosed => "本期排行榜已结算，请刷新查看。",
            (uint)rank_error.KRankRequestConflict => "排行榜代次已变化，请刷新。",
            (uint)rank_error.KRankSettlementPending => "排行榜正在结算，请稍后再试。",
            (uint)common_error.KInvalidParameter => "排行榜请求无效，请重新登录角色后再试。",
            (uint)common_error.KServiceUnavailable => "排行榜暂时不可用，请稍后再试。",
            (uint)common_error.KRateLimitExceeded => "操作过快，请稍后再试。",
            _ => "排行榜读取失败(" + tipId.ToString(CultureInfo.InvariantCulture) + ")。",
        };

        public static uint ParseEnvelopeTip(string error) => error != null
            && error.StartsWith(ServerTipPrefix, StringComparison.Ordinal)
            && uint.TryParse(error.Substring(ServerTipPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out uint tip)
                ? tip : 0;
    }
}
