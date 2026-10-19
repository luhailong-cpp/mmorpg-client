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
    /// </summary>
    public sealed class RankClient : IDisposable
    {
        public const uint PageSize = 5;
        public const float MinRequestIntervalSeconds = 0.4f;
        public const float MessageWindowSeconds = 2.5f;
        public const int SendQuota = 5;
        public const float RateLimitBackoffSeconds = MessageWindowSeconds;
        public const string RecoveryMessage = "排行榜请求状态尚未确认，请重新登录角色后再试。";
        public const string LoadingMessage = "正在读取排行榜，请稍候…";
        public const string NotInGameMessage = "请进入角色后再打开排行榜。";
        public const string TransportFailureMessage = "排行榜请求未完成，请稍后重试。";
        public const string EmptyMessage = "榜上暂无记录，打一局论剑即可上榜。";
        public const string TimeoutError = "rpc timeout";
        public const string ServerTipPrefix = "server tip=";

        private sealed class PendingRequest
        {
            public RankBoardKey Board;
            public uint Page, Size, Radius;
            public bool Around, FollowDisplayedBoard;
        }

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<float> _clock;
        private readonly Queue<float> _sentAt = new();
        private object _observedConnection;
        private ulong _observedPlayer;
        private int _generation;
        private bool _disposed;
        private PendingRequest _pending;
        private GetRankPageResponse _page;
        private GetRankAroundResponse _around;
        private float _lastSendAt = float.NegativeInfinity;
        private float _rateLimitedUntil = float.NegativeInfinity;

        public ulong PlayerId => _net.PlayerId;
        public GetRankPageResponse Page => _page?.Clone();
        public GetRankAroundResponse Around => _around?.Clone();
        public bool Busy { get; private set; }
        public bool Waiting => Busy || _pending != null;
        public bool RequiresReconnect { get; private set; }
        public string Status { get; private set; } = "";
        public uint CurrentPage => _page?.Page ?? 1;
        public ulong PageCount => Math.Max(1UL, _page?.PageCount ?? 1UL);
        public event Action Changed;

        public RankClient(IBattleTransport net, Func<object> connectionIdentity, Func<float> clock = null)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _connectionIdentity = connectionIdentity ?? (() => _net);
            _clock = clock ?? (() => UnityEngine.Time.realtimeSinceStartup);
            _observedConnection = _connectionIdentity();
            _observedPlayer = _net.PlayerId;
            _net.Disconnected += HandleDisconnected;
        }

        /// <summary>空 period/generation 表示最新可见代次，具体代次由首个回包确定。</summary>
        public static RankBoardKey LatestBoard() => new() { BoardType = 1, Scope = RankScope.Global };

        /// <summary>界面翻页：排队期间首个回包刚确定的榜代次也会被沿用。</summary>
        public void Load(uint page)
            => Request(new PendingRequest
            {
                Board = _page?.Board?.Clone() ?? _around?.Board?.Clone(),
                Page = Math.Max(1u, page), Size = PageSize, FollowDisplayedBoard = true,
            });

        /// <summary>刷新最新榜，而不是沿用已封榜的旧代次。</summary>
        public void Refresh() => ReadPage(LatestBoard(), 1, PageSize);

        public void ReadPage(RankBoardKey board, uint page = 1, uint size = 20)
        {
            if (!Valid(board)) { if (!_disposed) Show("排行榜尚未配置。"); return; }
            Request(new PendingRequest
            {
                Board = board.Clone(), Page = Math.Max(1u, page), Size = Math.Min(size == 0 ? 20u : size, 50u),
            });
        }

        public void ReadAround(RankBoardKey board, uint radius = 10)
        {
            if (!Valid(board)) { if (!_disposed) Show("排行榜尚未配置。"); return; }
            Request(new PendingRequest { Board = board.Clone(), Around = true, Radius = Math.Min(radius, 25u) });
        }

        private static bool Valid(RankBoardKey board)
            => board != null && board.BoardType != 0 && board.Scope == RankScope.Global;

        private void Request(PendingRequest request)
        {
            if (_disposed) return;
            ObserveConnection();
            if (Busy) { _pending = request; return; }
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
            var board = request.FollowDisplayedBoard
                ? (request.Board ?? _page?.Board?.Clone() ?? _around?.Board?.Clone() ?? LatestBoard()) : request.Board;
            if (request.Around)
            {
                _net.Call(MessageIds.RankGetRankAround, new GetRankAroundRequest { Board = board, Radius = request.Radius },
                    GetRankAroundResponse.Parser, response =>
                    {
                        if (!CompleteCurrent(generation, player, connection)) return;
                        Apply(response);
                        FlushPending();
                        Changed?.Invoke();
                    }, error => Fail(generation, player, connection, error));
            }
            else
            {
                _net.Call(MessageIds.RankGetRankPage, new GetRankPageRequest { Board = board, Page = request.Page, PageSize = request.Size },
                    GetRankPageResponse.Parser, response =>
                    {
                        if (!CompleteCurrent(generation, player, connection)) return;
                        Apply(response);
                        FlushPending();
                        Changed?.Invoke();
                    }, error => Fail(generation, player, connection, error));
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

        private void Fail(int generation, ulong player, object connection, string error)
        {
            if (!CompleteCurrent(generation, player, connection)) return;
            uint tip = ParseEnvelopeTip(error);
            if (error == TimeoutError || error == "timeout")
            {
                RequiresReconnect = true;
                _pending = null;
                _page = null;
                _around = null;
                Status = RecoveryMessage;
            }
            else if (tip == (uint)common_error.KRateLimitExceeded)
            {
                _pending = null;
                _rateLimitedUntil = Math.Max(_rateLimitedUntil, _clock() + RateLimitBackoffSeconds);
                Status = DescribeTip(tip);
            }
            else
            {
                _page = null;
                _around = null;
                Status = tip == 0 ? TransportFailureMessage : DescribeTip(tip);
                FlushPending();
            }
            Changed?.Invoke();
        }

        private void Apply(GetRankPageResponse response)
        {
            if (response == null || response.ErrorMessage?.Id > 0)
            {
                _page = null; _around = null;
                Status = response == null ? TransportFailureMessage : DescribeTip(response.ErrorMessage.Id);
                return;
            }
            _page = response.Clone();
            _around = null;
            Status = response.Entries.Count == 0 ? EmptyMessage : "";
        }

        private void Apply(GetRankAroundResponse response)
        {
            if (response == null || response.ErrorMessage?.Id > 0)
            {
                _page = null; _around = null;
                Status = response == null ? TransportFailureMessage : DescribeTip(response.ErrorMessage.Id);
                return;
            }
            _around = response.Clone();
            Status = response.Entries.Count == 0 ? EmptyMessage : "";
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
            _page = null;
            _around = null;
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
            _page = null;
            _around = null;
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

        public static string DescribeTip(TipInfoMessage tip) => tip == null || tip.Id == 0 ? "" : DescribeTip(tip.Id);

        public static string DescribeTip(uint tipId) => tipId switch
        {
            (uint)rank_error.KRankBoardNotFound => "本期排行榜尚未开放。",
            (uint)rank_error.KRankGenerationClosed => "本期排行榜已结算，请刷新查看。",
            (uint)rank_error.KRankRequestConflict => "排行榜代次已变化，请刷新。",
            (uint)rank_error.KRankSettlementPending => "排行榜正在结算，请稍后再试。",
            (uint)common_error.KInvalidParameter => "排行榜请求无效，请重新登录角色后再试。",
            (uint)common_error.KServiceUnavailable => "排行榜暂时不可用，请稍后再试。",
            (uint)common_error.KRateLimitExceeded => "翻页过快，请稍后再试。",
            _ => "排行榜读取失败(" + tipId.ToString(CultureInfo.InvariantCulture) + ")。",
        };

        public static uint ParseEnvelopeTip(string error) => error != null
            && error.StartsWith(ServerTipPrefix, StringComparison.Ordinal)
            && uint.TryParse(error.Substring(ServerTipPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out uint tip)
                ? tip : 0;
    }
}
