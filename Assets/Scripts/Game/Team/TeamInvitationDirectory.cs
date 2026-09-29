using System;
using System.Collections.Generic;
using System.Globalization;
using Friendpb;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net.Generated;
using MmorpgClient.World;

namespace MmorpgClient.Game.Team
{
    public enum TeamInvitationSource { Friends, Nearby, Online, Chat }

    /// <summary>
    /// Invitation candidates come only from the friend service, AOI, received chat, or the
    /// server's explicitly acknowledged online directory. This class never sends invitations.
    /// </summary>
    public sealed partial class TeamInvitationDirectory : IDisposable
    {
        public const int PageSize = 20;
        public const float RequestIntervalSeconds = 2.5f;
        public const string RecoveryMessage = "名单请求状态尚未确认，请重新登录角色后再试。";
        private readonly IBattleTransport _net;
        private readonly Func<object> _identity;
        private readonly Func<bool> _ready;
        private readonly Func<TeamInvitationSource, IEnumerable<TeamRole>> _local;
        private readonly Func<ulong, TeamRole> _resolve;
        private readonly Func<float> _clock;
        private readonly List<TeamRole> _candidates = new List<TeamRole>();
        private readonly HashSet<ulong> _chatPlayers = new HashSet<ulong>();
        private readonly Dictionary<ulong, TeamRole> _profiles = new Dictionary<ulong, TeamRole>();
        private object _connection;
        private ulong _player;
        private int _session, _view;
        private bool _disposed, _requestBusy, _queued, _appendQueued;
        private float _lastRequest = float.NegativeInfinity;
        private string _nextCursor = "";

        public TeamInvitationSource Source { get; private set; } = TeamInvitationSource.Friends;
        public IReadOnlyList<TeamRole> Candidates { get; }
        public bool IsLoading { get; private set; }
        public bool HasMore => Source == TeamInvitationSource.Online && _nextCursor.Length > 0;
        public bool RequiresReconnect { get; private set; }
        public string Query { get; private set; } = "";
        public string Status { get; private set; } = "请选择邀请来源。";
        public event Action Changed;

        /// <summary>Pure data seam; callbacks run on the transport's Unity main thread.</summary>
        public TeamInvitationDirectory(IBattleTransport net, Func<object> connectionIdentity = null,
            Func<TeamInvitationSource, IEnumerable<TeamRole>> localCandidates = null,
            Func<ulong, TeamRole> resolveRole = null, Func<float> clock = null, Func<bool> ready = null)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _identity = connectionIdentity ?? (() => net);
            _local = localCandidates ?? (_ => Array.Empty<TeamRole>());
            _resolve = resolveRole ?? (id => new TeamRole { PlayerId = id, IsOnline = false, OnlineStatusKnown = false });
            _ready = ready ?? (() => _net.IsReady && _net.PlayerId != 0);
            if (clock == null)
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                _clock = () => (float)stopwatch.Elapsed.TotalSeconds;
            }
            else _clock = clock;
            _connection = _identity(); _player = _net.PlayerId;
            Candidates = _candidates.AsReadOnly();
            _net.Disconnected += OnDisconnected;
        }

        /// <summary>Call each frame. Also drains coalesced refreshes after the read throttle.</summary>
        public void ObserveConnection()
        {
            if (_disposed) return;
            object connection = _identity();
            if (!ReferenceEquals(connection, _connection))
            {
                _connection = connection; _requestBusy = false; RequiresReconnect = false;
                _lastRequest = float.NegativeInfinity;
                Reset();
            }
            else if (_player != _net.PlayerId) Reset();
            Drain();
        }

        public void Reset()
        {
            if (_disposed) return;
            // A cancelled zero-id reply can otherwise be mistaken for a new call on this connection.
            if (_requestBusy) RequiresReconnect = true;
            ++_session; ++_view; _player = _net.PlayerId;
            _requestBusy = _queued = _appendQueued = IsLoading = false;
            Query = _nextCursor = "";
            _candidates.Clear(); _chatPlayers.Clear(); _profiles.Clear();
            Publish(RequiresReconnect ? RecoveryMessage : "名单已清理，请刷新。");
        }

        private void OnDisconnected()
        {
            _connection = _identity(); _requestBusy = false; RequiresReconnect = false;
            _lastRequest = float.NegativeInfinity;
            Reset();
        }

        public void RememberChatPlayer(ulong playerId)
        {
            ObserveConnection();
            if (!_disposed && _ready() && playerId != 0 && playerId != _net.PlayerId)
                _chatPlayers.Add(playerId);
        }

        public void Refresh(TeamInvitationSource source, string query = "")
        {
            ObserveConnection();
            if (_disposed) return;
            ++_view; Source = source; Query = (query ?? "").Trim();
            _candidates.Clear(); _nextCursor = ""; _queued = _appendQueued = IsLoading = false;
            if (!_ready()) { Publish("请进入角色后查看邀请名单。"); return; }
            if (Query.Length > 64) { Publish("搜索内容最多 64 字。"); return; }
            if (source == TeamInvitationSource.Nearby || source == TeamInvitationSource.Chat)
            {
                ReadLocal(); return;
            }
            if (RequiresReconnect) { Publish(RecoveryMessage); return; }
            _queued = IsLoading = true;
            Publish("正在读取" + SourceName(source) + "名单…");
            Drain();
        }

        public void LoadMore()
        {
            ObserveConnection();
            if (_disposed || !HasMore || IsLoading || !_ready() || RequiresReconnect) return;
            _queued = _appendQueued = IsLoading = true;
            Publish("正在读取更多在线道友…");
            Drain();
        }

        private void ReadLocal()
        {
            try
            {
                var rows = new List<TeamRole>();
                foreach (var role in _local(Source) ?? Array.Empty<TeamRole>())
                    if (role != null) rows.Add(Enrich(role));
                if (Source == TeamInvitationSource.Chat)
                    foreach (ulong id in _chatPlayers)
                    {
                        var role = Resolve(id);
                        rows.Add(role);
                    }
                AddRows(rows, true);
                Publish(_candidates.Count > 0
                    ? (Source == TeamInvitationSource.Chat ? "最近聊过天的道友；发出邀请后会确认对方是否在线。" : "当前视野中的玩家，可刷新名单。")
                    : EmptyStatus());
            }
            catch
            {
                _candidates.Clear(); Publish("暂未读取到" + SourceName(Source) + "名单，请刷新重试。");
            }
        }

        private void Drain()
        {
            if (_disposed || !_queued || _requestBusy || RequiresReconnect || !_ready() ||
                _clock() < _lastRequest + RequestIntervalSeconds) return;
            bool append = _appendQueued;
            _queued = _appendQueued = false;
            int view = _view;
            if (Source == TeamInvitationSource.Friends)
            {
                Request(ClientPlayerFriendGetFriendListHandler.MessageId, new GetFriendListRequest(),
                    GetFriendListResponse.Parser, view, reply =>
                    {
                        if (reply == null) { Fail("好友名单暂未返回，请刷新重试。"); return; }
                        if (!AcceptTip(reply.ErrorMessage)) return;
                        var rows = new List<TeamRole>();
                        foreach (var friend in reply.Friends)
                        {
                            var role = Resolve(friend.FriendPlayerId);
                            if (!string.IsNullOrEmpty(friend.Name)) role.Name = friend.Name;
                            if (friend.Level != 0) role.Level = friend.Level;
                            if (friend.ClassId != 0) role.ClassId = friend.ClassId;
                            if (friend.Gender != 0) role.Gender = friend.Gender;
                            role.ZoneId = friend.ZoneId;
                            if (!string.IsNullOrEmpty(friend.AppearanceId) || friend.ClassId != 0 && friend.Gender != 0)
                                role.CharacterId = QdaoCharacterCatalog.ResolveRole(friend.ClassId, friend.Gender, friend.AppearanceId);
                            role.IsOnline = friend.IsOnline;
                            role.OnlineStatusKnown = true;
                            if (role.PlayerId != 0) _profiles[role.PlayerId] = role.Clone();
                            rows.Add(role);
                        }
                        AddRows(rows, true);
                        Publish(_candidates.Count > 0 ? "已同步好友在线状态；离线好友暂不可邀请。" : EmptyStatus());
                    });
            }
            else if (Source == TeamInvitationSource.Online)
            {
                string cursor = append ? _nextCursor : "";
                var request = new RecommendFriendsRequest { Limit = PageSize, OnlineOnly = true, Cursor = cursor, Query = Query };
                Request(ClientPlayerFriendRecommendFriendsHandler.MessageId, request, RecommendFriendsResponse.Parser,
                    view, reply =>
                    {
                        if (reply == null) { Fail("在线名单暂未返回，请刷新重试。"); return; }
                        if (!AcceptTip(reply.ErrorMessage)) return;
                        // Old servers ignore unknown protobuf fields and return ordinary random recommendations.
                        if (!reply.OnlineDirectory) { Fail("当前服务尚未提供在线邀请名单，请更新服务后重试。"); return; }
                        var rows = new List<TeamRole>();
                        foreach (var entry in reply.Candidates)
                        {
                            if (!entry.IsOnline) continue;
                            var role = new TeamRole
                            {
                                PlayerId = entry.CandidatePlayerId, Name = entry.Name, Level = entry.Level,
                                ClassId = entry.ClassId, Gender = entry.Gender, ZoneId = entry.ZoneId, IsOnline = true,
                                CharacterId = !string.IsNullOrEmpty(entry.AppearanceId) || entry.ClassId != 0 && entry.Gender != 0
                                    ? QdaoCharacterCatalog.ResolveRole(entry.ClassId, entry.Gender, entry.AppearanceId) : null,
                            };
                            if (role.PlayerId != 0) _profiles[role.PlayerId] = role.Clone();
                            rows.Add(role);
                        }
                        // Detect a broken cursor instead of presenting an endless load-more loop.
                        if (reply.NextCursor.Length > 0 && reply.NextCursor == cursor)
                        { Fail("在线名单分页未前进，请刷新重试。"); return; }
                        _nextCursor = reply.NextCursor;
                        AddRows(rows, false);
                        Publish(_candidates.Count > 0
                            ? (HasMore ? "已读取同区在线道友，可继续加载。" : "同区在线名单已读取完毕。")
                            : (HasMore ? "本页暂无匹配道友，请继续加载。" : EmptyStatus()));
                    });
            }
        }

        private void Request<T>(uint messageId, IMessage request, MessageParser<T> parser, int view, Action<T> completed)
            where T : IMessage<T>
        {
            int session = _session; ulong player = _net.PlayerId; object connection = _connection;
            _requestBusy = IsLoading = true; _lastRequest = _clock();
            _net.Call(messageId, request, parser, response =>
            {
                if (!Current(session, player, connection)) return;
                _requestBusy = false;
                if (view == _view) { IsLoading = false; completed(response); }
                Drain();
            }, error =>
            {
                if (!Current(session, player, connection)) return;
                _requestBusy = false;
                if (!DefiniteRejection(error)) RequiresReconnect = true;
                if (RequiresReconnect) _queued = _appendQueued = false;
                if (view == _view || RequiresReconnect && IsLoading)
                {
                    IsLoading = false;
                    Publish(RequiresReconnect ? RecoveryMessage : "服务器暂未接受名单请求，请稍后刷新重试。");
                }
                Drain();
            });
        }

        private bool Current(int session, ulong player, object connection) => !_disposed && session == _session &&
            player == _net.PlayerId && _ready() && ReferenceEquals(connection, _identity());

        private static bool DefiniteRejection(string error)
        {
            const string prefix = "server tip=";
            if (string.IsNullOrEmpty(error) || !error.StartsWith(prefix, StringComparison.Ordinal) ||
                !uint.TryParse(error.Substring(prefix.Length), out uint tip)) return false;
            return tip == (uint)common_error.KInvalidParameter || tip == (uint)common_error.KFeatureUnavailable ||
                tip == (uint)common_error.KRateLimitExceeded || tip == (uint)common_error.KMessageSizeExceeded;
        }

        private bool AcceptTip(TipInfoMessage tip)
        {
            if (tip == null || tip.Id == 0) return true;
            Fail("名单服务未完成请求（" + tip.Id + "），请稍后刷新重试。"); return false;
        }

        private void Fail(string status) { _nextCursor = ""; Publish(status); }

        private TeamRole Resolve(ulong id)
        {
            var role = _resolve(id)?.Clone() ?? new TeamRole { PlayerId = id, IsOnline = false, OnlineStatusKnown = false };
            role.PlayerId = id; return Enrich(role);
        }

        private TeamRole Enrich(TeamRole value)
        {
            var role = value.Clone();
            if (_profiles.TryGetValue(role.PlayerId, out var known))
            {
                if (string.IsNullOrEmpty(role.Name)) role.Name = known.Name;
                if (string.IsNullOrEmpty(role.CharacterId)) role.CharacterId = known.CharacterId;
                if (role.ClassId == 0) role.ClassId = known.ClassId;
                if (role.Gender == 0) role.Gender = known.Gender;
                if (role.Level == 0) role.Level = known.Level;
                // Cached profiles enrich identity only. Earlier friend/online presence cannot
                // establish whether a chat sender is still online when an invitation is sent.
            }
            return role;
        }

        private void AddRows(IEnumerable<TeamRole> rows, bool filter)
        {
            var ids = new HashSet<ulong>();
            foreach (var role in _candidates) ids.Add(role.PlayerId);
            foreach (var role in rows)
                if (role != null && role.PlayerId != 0 && role.PlayerId != _net.PlayerId &&
                    (!filter || Matches(role, Query)) && ids.Add(role.PlayerId)) _candidates.Add(role.Clone());
        }

        public static bool Matches(TeamRole role, string query) => string.IsNullOrEmpty(query) ||
            (role.Name ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            role.PlayerId.ToString(CultureInfo.InvariantCulture).IndexOf(query, StringComparison.Ordinal) >= 0;

        private string EmptyStatus() => Query.Length > 0 ? "未找到匹配的道友。" : Source == TeamInvitationSource.Chat
            ? "暂无聊天玩家；请先打开聊天频道读取消息。" : Source == TeamInvitationSource.Nearby
            ? "当前视野中没有其他玩家。" : Source == TeamInvitationSource.Friends ? "好友名单为空。" : "当前没有可邀请的在线道友。";

        private static string SourceName(TeamInvitationSource source) => source == TeamInvitationSource.Friends ? "好友" :
            source == TeamInvitationSource.Nearby ? "周围玩家" : source == TeamInvitationSource.Chat ? "聊天玩家" : "在线玩家";

        private void Publish(string status) { Status = status ?? ""; Changed?.Invoke(); }

        public void Dispose()
        {
            if (_disposed) return;
            _net.Disconnected -= OnDisconnected;
            _disposed = true; ++_session; ++_view;
            _queued = _requestBusy = IsLoading = false;
            _candidates.Clear(); _chatPlayers.Clear(); _profiles.Clear(); _nextCursor = "";
        }
    }
}
