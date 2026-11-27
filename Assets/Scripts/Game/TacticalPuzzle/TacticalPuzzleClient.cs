using System;
using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.TacticalPuzzle
{
    /// <summary>万象残局坊 V1 消息号契约（proto/match/match_service.proto，默认取生成常量 575–581）。</summary>
    public readonly struct TacticalPuzzleMessageIds
    {
        public uint List { get; }
        public uint Get { get; }
        public uint SaveDraft { get; }
        public uint Delete { get; }
        public uint StartBattle { get; }
        public uint Like { get; }
        public uint GetSolutionReplay { get; }

        public bool IsSupported =>
            List != 0u && Get != 0u && SaveDraft != 0u && Delete != 0u
            && StartBattle != 0u && Like != 0u && GetSolutionReplay != 0u;

        public TacticalPuzzleMessageIds(
            uint list, uint get, uint saveDraft, uint delete,
            uint startBattle, uint like, uint getSolutionReplay)
        {
            List = list;
            Get = get;
            SaveDraft = saveDraft;
            Delete = delete;
            StartBattle = startBattle;
            Like = like;
            GetSolutionReplay = getSolutionReplay;
        }

        public static TacticalPuzzleMessageIds Generated => new TacticalPuzzleMessageIds(
            MatchServiceRpc.ListTacticalPuzzlesMessageId,
            MatchServiceRpc.GetTacticalPuzzleMessageId,
            MatchServiceRpc.SaveTacticalPuzzleDraftMessageId,
            MatchServiceRpc.DeleteTacticalPuzzleMessageId,
            MatchServiceRpc.StartTacticalPuzzleBattleMessageId,
            MatchServiceRpc.LikeTacticalPuzzleMessageId,
            MatchServiceRpc.GetTacticalPuzzleSolutionReplayMessageId);
    }

    /// <summary>
    /// 万象残局坊 V1 客户端网络协调器（纯 C#，WebGL2 单线程安全，IL2CPP AOT 安全，EditMode 可直接单测）。
    /// 全部走大厅 gate -> match 路由（RPC 575–581），与 BattleDirectLink 无耦合；
    /// 严守连接/角色会话边界，切角或断线自动作废在途回调并清空本地缓存。
    /// </summary>
    public sealed class TacticalPuzzleClient : IDisposable
    {
        public const float MinListIntervalSeconds = 0.8f;
        public const float MinActionIntervalSeconds = 0.6f;
        public const int MaxCachedReplays = 4;

        public const string UnsupportedMessage = "当前服务器暂未开放万象残局坊。";
        public const string NotConnectedMessage = "尚未进入游戏，暂时无法操作残局坊。";
        public const string TooFastMessage = "操作太快了，请稍候再试。";
        public const string TransportErrorMessage = "网络波动，请稍后重试。";

        private readonly IBattleTransport _net;
        private readonly TacticalPuzzleMessageIds _ids;
        private readonly Func<object> _connectionIdentity;
        private readonly Func<float> _clock;

        private readonly List<Match.TacticalPuzzleSummaryView> _puzzles = new();
        private readonly Dictionary<ulong, Match.TacticalPuzzleSummaryView> _detailCache = new();
        private readonly List<(ulong PuzzleId, uint Revision, BattleReplay Replay)> _replayCache = new();

        private object _observedConnection;
        private ulong _observedPlayerId;
        private int _generation;
        private bool _disposed;
        private float _nextListAt = float.NegativeInfinity;
        private float _nextActionAt = float.NegativeInfinity;
        private bool _listDirty = true;

        public event Action Changed;
        public event Action<string> Notice;
        public event Action<BattleReplay> SolutionReplayLoaded;

        public Match.TacticalPuzzleListTab CurrentTab { get; private set; } = Match.TacticalPuzzleListTab.Hot;
        public uint CurrentTagFilterMask { get; private set; }
        public IReadOnlyList<Match.TacticalPuzzleSummaryView> Puzzles => _puzzles;
        public uint TotalCount { get; private set; }
        public Match.TacticalPuzzleProfileView Profile { get; private set; }
        public bool ListPending { get; private set; }
        public bool ActionPending { get; private set; }
        public bool ReplayPending { get; private set; }
        public string Status { get; private set; } = "";
        public bool StatusIsError { get; private set; }
        public bool IsListDirty => _listDirty;

        public ulong PlayerId => _net.PlayerId;
        public bool IsSupported => _ids.IsSupported;
        public bool Ready => IsSupported && !_disposed && _net.IsReady && _net.PlayerId != 0ul;

        public TacticalPuzzleClient(
            IBattleTransport transport,
            Func<object> connectionIdentity,
            Func<float> clock,
            TacticalPuzzleMessageIds? messageIds = null)
        {
            _net = transport ?? throw new ArgumentNullException(nameof(transport));
            _connectionIdentity = connectionIdentity ?? throw new ArgumentNullException(nameof(connectionIdentity));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _ids = messageIds ?? TacticalPuzzleMessageIds.Generated;
            _observedConnection = _connectionIdentity();
            _observedPlayerId = _net.PlayerId;
            _net.Disconnected += HandleDisconnected;
            if (!IsSupported) SetStatus(UnsupportedMessage, false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _net.Disconnected -= HandleDisconnected;
            _disposed = true;
            ++_generation;
            ClearCaches();
        }

        public void ObserveConnection()
        {
            if (_disposed) return;
            object conn = _connectionIdentity();
            ulong pid = _net.PlayerId;
            if (!ReferenceEquals(conn, _observedConnection) || pid != _observedPlayerId)
            {
                _observedConnection = conn;
                _observedPlayerId = pid;
                ++_generation;
                ClearCaches();
                Changed?.Invoke();
            }
        }

        public void HandleSceneEntered()
        {
            ObserveConnection();
            _listDirty = true;
        }

        public void HandleBattleSettled()
        {
            ObserveConnection();
            _listDirty = true;
            _detailCache.Clear();
            Changed?.Invoke();
        }

        public bool TryGetCachedPuzzle(ulong puzzleId, out Match.TacticalPuzzleSummaryView puzzle)
        {
            ObserveConnection();
            if (puzzleId != 0ul && _detailCache.TryGetValue(puzzleId, out puzzle))
                return true;
            for (int i = 0; i < _puzzles.Count; i++)
            {
                if (_puzzles[i].PuzzleId == puzzleId)
                {
                    puzzle = _puzzles[i];
                    return true;
                }
            }
            puzzle = null;
            return false;
        }

        public bool RequestList(
            Match.TacticalPuzzleListTab tab,
            uint tagFilterMask = 0u,
            uint offset = 0u,
            uint limit = 30u,
            bool force = false)
        {
            if (_disposed) return false;
            if (!IsSupported) { SetStatus(UnsupportedMessage, false); return false; }
            ObserveConnection();
            if (!Ready)
            {
                SetStatus(NotConnectedMessage, true);
                return false;
            }
            if (tab == Match.TacticalPuzzleListTab.Unspecified)
                tab = Match.TacticalPuzzleListTab.Hot;
            uint normalizedTag = tagFilterMask & TacticalPuzzleModels.ValidTagMask;
            bool filterChanged = tab != CurrentTab || normalizedTag != CurrentTagFilterMask;
            float now = _clock();
            if (!force && !filterChanged && !_listDirty && (ListPending || now < _nextListAt))
                return false;

            CurrentTab = tab;
            CurrentTagFilterMask = normalizedTag;
            ListPending = true;
            _nextListAt = now + MinListIntervalSeconds;
            int generation = _generation;
            SetStatus("", false);
            Changed?.Invoke();

            var req = new Match.ListTacticalPuzzlesRequest
            {
                Tab = tab,
                TagFilterMask = normalizedTag,
                Offset = offset,
                Limit = limit == 0u ? 30u : Math.Min(limit, 50u),
            };
            _net.Call(_ids.List, req, Match.ListTacticalPuzzlesResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(generation)) return;
                    ListPending = false;
                    uint tipId = resp?.ErrorMessage?.Id ?? 0u;
                    if (tipId != 0u)
                    {
                        string msg = TacticalPuzzleModels.DescribeTip(tipId);
                        SetStatus(msg, true);
                        Notice?.Invoke(msg);
                        return;
                    }
                    _puzzles.Clear();
                    if (resp?.Puzzles != null)
                    {
                        for (int i = 0; i < resp.Puzzles.Count; i++)
                        {
                            var item = resp.Puzzles[i];
                            if (item == null || item.PuzzleId == 0ul) continue;
                            var cloned = item.Clone();
                            _puzzles.Add(cloned);
                            CacheDetail(cloned);
                        }
                    }
                    TotalCount = resp?.TotalCount ?? (uint)_puzzles.Count;
                    Profile = resp?.Profile?.Clone();
                    _listDirty = false;
                    SetStatus("", false);
                    Changed?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(generation)) return;
                    ListPending = false;
                    string msg = DescribeTransportError(err);
                    SetStatus(msg, true);
                    Notice?.Invoke(msg);
                    Changed?.Invoke();
                });
            return true;
        }

        public bool RequestDetail(
            ulong puzzleId,
            Action<Match.TacticalPuzzleSummaryView> onLoaded,
            Action<string> onError = null)
        {
            if (_disposed || puzzleId == 0ul) return false;
            if (!IsSupported)
            {
                onError?.Invoke(UnsupportedMessage);
                return false;
            }
            ObserveConnection();
            if (!Ready)
            {
                onError?.Invoke(NotConnectedMessage);
                return false;
            }
            int generation = _generation;
            _net.Call(_ids.Get,
                new Match.GetTacticalPuzzleRequest { PuzzleId = puzzleId },
                Match.GetTacticalPuzzleResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(generation)) return;
                    uint tipId = resp?.ErrorMessage?.Id ?? 0u;
                    if (tipId != 0u)
                    {
                        string msg = TacticalPuzzleModels.DescribeTip(tipId);
                        onError?.Invoke(msg);
                        return;
                    }
                    if (resp?.Puzzle == null || resp.Puzzle.PuzzleId == 0ul)
                    {
                        onError?.Invoke(TacticalPuzzleModels.DescribeTip(16064u));
                        return;
                    }
                    var cloned = resp.Puzzle.Clone();
                    CacheDetail(cloned);
                    UpsertInList(cloned);
                    Changed?.Invoke();
                    onLoaded?.Invoke(cloned);
                },
                err =>
                {
                    if (!IsCurrent(generation)) return;
                    onError?.Invoke(DescribeTransportError(err));
                });
            return true;
        }

        public bool SaveDraft(
            ulong puzzleId,
            string title,
            string hintText,
            uint tagMask,
            global::TacticalPuzzleSpec spec,
            Action<Match.TacticalPuzzleSummaryView> onSaved = null,
            Action<string> onError = null)
        {
            if (!TacticalPuzzleModels.ValidateDraft(title, hintText, tagMask, spec, out string validationErr))
            {
                SetStatus(validationErr, true);
                Notice?.Invoke(validationErr);
                onError?.Invoke(validationErr);
                return false;
            }
            if (!PrepareWriteAction(onError, out int generation)) return false;

            var req = new Match.SaveTacticalPuzzleDraftRequest
            {
                PuzzleId = puzzleId,
                Title = (title ?? "").Trim(),
                HintText = (hintText ?? "").Trim(),
                TagMask = tagMask & TacticalPuzzleModels.ValidTagMask,
                Spec = spec.Clone(),
            };
            SetStatus("正在保存残局草稿…", false);
            Changed?.Invoke();

            _net.Call(_ids.SaveDraft, req, Match.SaveTacticalPuzzleDraftResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    uint tipId = resp?.ErrorMessage?.Id ?? 0u;
                    if (tipId != 0u)
                    {
                        string msg = TacticalPuzzleModels.DescribeTip(tipId);
                        SetStatus(msg, true);
                        Notice?.Invoke(msg);
                        onError?.Invoke(msg);
                        Changed?.Invoke();
                        return;
                    }
                    var saved = resp?.Puzzle?.Clone();
                    if (saved != null && saved.PuzzleId != 0ul)
                    {
                        CacheDetail(saved);
                        UpsertInList(saved);
                        InvalidateReplayForPuzzle(saved.PuzzleId);
                    }
                    _listDirty = true;
                    string ok = "残局草稿已保存。须本人亲手通关 1 次后自动发布全服。";
                    SetStatus(ok, false);
                    Notice?.Invoke(ok);
                    Changed?.Invoke();
                    onSaved?.Invoke(saved);
                },
                err =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    string msg = DescribeTransportError(err);
                    SetStatus(msg, true);
                    Notice?.Invoke(msg);
                    onError?.Invoke(msg);
                    Changed?.Invoke();
                });
            return true;
        }

        public bool DeletePuzzle(ulong puzzleId, Action onDeleted = null, Action<string> onError = null)
        {
            if (puzzleId == 0ul) return false;
            if (!PrepareWriteAction(onError, out int generation)) return false;

            SetStatus("正在删除残局…", false);
            Changed?.Invoke();

            _net.Call(_ids.Delete,
                new Match.DeleteTacticalPuzzleRequest { PuzzleId = puzzleId },
                Match.DeleteTacticalPuzzleResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    uint tipId = resp?.ErrorMessage?.Id ?? 0u;
                    if (tipId != 0u)
                    {
                        string msg = TacticalPuzzleModels.DescribeTip(tipId);
                        SetStatus(msg, true);
                        Notice?.Invoke(msg);
                        onError?.Invoke(msg);
                        Changed?.Invoke();
                        return;
                    }
                    _detailCache.Remove(puzzleId);
                    InvalidateReplayForPuzzle(puzzleId);
                    for (int i = _puzzles.Count - 1; i >= 0; i--)
                    {
                        if (_puzzles[i].PuzzleId == puzzleId) _puzzles.RemoveAt(i);
                    }
                    _listDirty = true;
                    string ok = "已删除该残局。";
                    SetStatus(ok, false);
                    Notice?.Invoke(ok);
                    Changed?.Invoke();
                    onDeleted?.Invoke();
                },
                err =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    string msg = DescribeTransportError(err);
                    SetStatus(msg, true);
                    Notice?.Invoke(msg);
                    onError?.Invoke(msg);
                    Changed?.Invoke();
                });
            return true;
        }

        public bool StartBattle(
            ulong puzzleId,
            bool isAuthorVerify,
            Action<ulong> onStarted = null,
            Action<string> onError = null)
        {
            if (puzzleId == 0ul) return false;
            if (!PrepareWriteAction(onError, out int generation)) return false;

            string pendingText = isAuthorVerify ? "正在进入残局作者自证战…" : "正在进入残局破局战…";
            SetStatus(pendingText, false);
            Changed?.Invoke();

            var req = new Match.StartTacticalPuzzleBattleRequest
            {
                PuzzleId = puzzleId,
                IsAuthorVerify = isAuthorVerify,
                RequestId = "puz-" + Guid.NewGuid().ToString("N"),
            };
            _net.Call(_ids.StartBattle, req, Match.StartTacticalPuzzleBattleResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    uint tipId = resp?.ErrorMessage?.Id ?? 0u;
                    if (tipId != 0u)
                    {
                        string msg = TacticalPuzzleModels.DescribeTip(tipId);
                        SetStatus(msg, true);
                        Notice?.Invoke(msg);
                        onError?.Invoke(msg);
                        Changed?.Invoke();
                        return;
                    }
                    ulong battleId = resp?.BattleId ?? 0ul;
                    _listDirty = true;
                    SetStatus("", false);
                    Changed?.Invoke();
                    onStarted?.Invoke(battleId);
                },
                err =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    string msg = DescribeTransportError(err);
                    SetStatus(msg, true);
                    Notice?.Invoke(msg);
                    onError?.Invoke(msg);
                    Changed?.Invoke();
                });
            return true;
        }

        public bool LikePuzzle(ulong puzzleId, Action<uint> onLiked = null, Action<string> onError = null)
        {
            if (puzzleId == 0ul) return false;
            if (!PrepareWriteAction(onError, out int generation)) return false;

            _net.Call(_ids.Like,
                new Match.LikeTacticalPuzzleRequest { PuzzleId = puzzleId },
                Match.LikeTacticalPuzzleResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    uint tipId = resp?.ErrorMessage?.Id ?? 0u;
                    if (tipId != 0u)
                    {
                        string msg = TacticalPuzzleModels.DescribeTip(tipId);
                        SetStatus(msg, true);
                        Notice?.Invoke(msg);
                        onError?.Invoke(msg);
                        Changed?.Invoke();
                        return;
                    }
                    uint newLikes = resp?.LikeCount ?? 0u;
                    bool selfLiked = resp?.SelfLiked ?? true;
                    ApplyLikeToCaches(puzzleId, newLikes, selfLiked);
                    string ok = "已为这道奇局点赞！";
                    SetStatus(ok, false);
                    Notice?.Invoke(ok);
                    Changed?.Invoke();
                    onLiked?.Invoke(newLikes);
                },
                err =>
                {
                    if (!IsCurrent(generation)) return;
                    ActionPending = false;
                    string msg = DescribeTransportError(err);
                    SetStatus(msg, true);
                    Notice?.Invoke(msg);
                    onError?.Invoke(msg);
                    Changed?.Invoke();
                });
            return true;
        }

        public bool RequestSolutionReplay(
            ulong puzzleId,
            uint expectedRevision = 0u,
            Action<BattleReplay> onReady = null,
            Action<string> onError = null)
        {
            if (_disposed || puzzleId == 0ul) return false;
            if (!IsSupported)
            {
                onError?.Invoke(UnsupportedMessage);
                return false;
            }
            ObserveConnection();
            if (expectedRevision != 0u && TryGetCachedReplay(puzzleId, expectedRevision, out var cached))
            {
                if (onReady != null) onReady(cached);
                else SolutionReplayLoaded?.Invoke(cached);
                return true;
            }
            if (ReplayPending)
            {
                string busy = "正在加载标准解法录像，请稍候。";
                Notice?.Invoke(busy);
                onError?.Invoke(busy);
                return false;
            }
            if (!Ready)
            {
                onError?.Invoke(NotConnectedMessage);
                return false;
            }
            ReplayPending = true;
            int generation = _generation;
            Changed?.Invoke();

            _net.Call(_ids.GetSolutionReplay,
                new Match.GetTacticalPuzzleSolutionReplayRequest { PuzzleId = puzzleId },
                Match.GetTacticalPuzzleSolutionReplayResponse.Parser,
                resp =>
                {
                    if (!IsCurrent(generation)) return;
                    ReplayPending = false;
                    uint tipId = resp?.ErrorMessage?.Id ?? 0u;
                    if (tipId != 0u)
                    {
                        string msg = TacticalPuzzleModels.DescribeTip(tipId);
                        SetStatus(msg, true);
                        Notice?.Invoke(msg);
                        onError?.Invoke(msg);
                        Changed?.Invoke();
                        return;
                    }
                    var decoded = BattleReplayDecoder.Decode(resp?.Replay);
                    if (!decoded.Success || decoded.Replay == null)
                    {
                        string msg = string.IsNullOrEmpty(decoded.Message) ? "标准解法录像解析失败。" : decoded.Message;
                        SetStatus(msg, true);
                        Notice?.Invoke(msg);
                        onError?.Invoke(msg);
                        Changed?.Invoke();
                        return;
                    }
                    CacheReplay(puzzleId, expectedRevision, decoded.Replay);
                    Changed?.Invoke();
                    if (onReady != null) onReady(decoded.Replay);
                    else SolutionReplayLoaded?.Invoke(decoded.Replay);
                },
                err =>
                {
                    if (!IsCurrent(generation)) return;
                    ReplayPending = false;
                    string msg = DescribeTransportError(err);
                    SetStatus(msg, true);
                    Notice?.Invoke(msg);
                    onError?.Invoke(msg);
                    Changed?.Invoke();
                });
            return true;
        }

        // ── 内部辅助 ─────────────────────────────────────────────

        private bool PrepareWriteAction(Action<string> onError, out int generation)
        {
            generation = _generation;
            if (_disposed) return false;
            if (!IsSupported)
            {
                SetStatus(UnsupportedMessage, false);
                onError?.Invoke(UnsupportedMessage);
                return false;
            }
            ObserveConnection();
            if (!Ready)
            {
                SetStatus(NotConnectedMessage, true);
                Notice?.Invoke(NotConnectedMessage);
                onError?.Invoke(NotConnectedMessage);
                return false;
            }
            float now = _clock();
            if (ActionPending || now < _nextActionAt)
            {
                Notice?.Invoke(TooFastMessage);
                onError?.Invoke(TooFastMessage);
                return false;
            }
            ActionPending = true;
            _nextActionAt = now + MinActionIntervalSeconds;
            generation = _generation;
            return true;
        }

        private bool IsCurrent(int generation)
        {
            if (_disposed || generation != _generation) return false;
            if (!ReferenceEquals(_connectionIdentity(), _observedConnection) || _net.PlayerId != _observedPlayerId)
            {
                ObserveConnection();
                return false;
            }
            return true;
        }

        private void HandleDisconnected()
        {
            if (_disposed) return;
            ++_generation;
            ClearCaches();
            Changed?.Invoke();
        }

        private void ClearCaches()
        {
            _puzzles.Clear();
            _detailCache.Clear();
            _replayCache.Clear();
            TotalCount = 0u;
            Profile = null;
            ListPending = false;
            ActionPending = false;
            ReplayPending = false;
            _listDirty = true;
            _nextListAt = float.NegativeInfinity;
            _nextActionAt = float.NegativeInfinity;
            Status = "";
            StatusIsError = false;
        }

        private void SetStatus(string text, bool isError)
        {
            Status = text ?? "";
            StatusIsError = isError;
        }

        private void CacheDetail(Match.TacticalPuzzleSummaryView puzzle)
        {
            if (puzzle == null || puzzle.PuzzleId == 0ul) return;
            if (_detailCache.Count >= 128 && !_detailCache.ContainsKey(puzzle.PuzzleId))
                _detailCache.Clear();
            _detailCache[puzzle.PuzzleId] = puzzle.Clone();
        }

        private void UpsertInList(Match.TacticalPuzzleSummaryView puzzle)
        {
            if (puzzle == null || puzzle.PuzzleId == 0ul) return;
            for (int i = 0; i < _puzzles.Count; i++)
            {
                if (_puzzles[i].PuzzleId == puzzle.PuzzleId)
                {
                    _puzzles[i] = puzzle.Clone();
                    return;
                }
            }
        }

        private void ApplyLikeToCaches(ulong puzzleId, uint likeCount, bool selfLiked)
        {
            if (_detailCache.TryGetValue(puzzleId, out var cached))
            {
                cached.LikeCount = likeCount;
                cached.SelfLiked = selfLiked;
            }
            for (int i = 0; i < _puzzles.Count; i++)
            {
                if (_puzzles[i].PuzzleId == puzzleId)
                {
                    _puzzles[i].LikeCount = likeCount;
                    _puzzles[i].SelfLiked = selfLiked;
                }
            }
        }

        private bool TryGetCachedReplay(ulong puzzleId, uint revision, out BattleReplay replay)
        {
            for (int i = _replayCache.Count - 1; i >= 0; i--)
            {
                var entry = _replayCache[i];
                if (entry.PuzzleId == puzzleId && entry.Revision == revision)
                {
                    _replayCache.RemoveAt(i);
                    _replayCache.Add(entry);
                    replay = entry.Replay;
                    return true;
                }
            }
            replay = null;
            return false;
        }

        private void CacheReplay(ulong puzzleId, uint revision, BattleReplay replay)
        {
            if (MaxCachedReplays <= 0 || replay == null || revision == 0u) return;
            InvalidateReplayForPuzzle(puzzleId);
            while (_replayCache.Count >= MaxCachedReplays)
                _replayCache.RemoveAt(0);
            _replayCache.Add((puzzleId, revision, replay));
        }

        private void InvalidateReplayForPuzzle(ulong puzzleId)
        {
            for (int i = _replayCache.Count - 1; i >= 0; i--)
            {
                if (_replayCache[i].PuzzleId == puzzleId)
                    _replayCache.RemoveAt(i);
            }
        }

        private static string DescribeTransportError(string err)
        {
            if (string.IsNullOrWhiteSpace(err)) return TransportErrorMessage;
            if (err.IndexOf("1008", StringComparison.Ordinal) >= 0) return TooFastMessage;
            return TransportErrorMessage;
        }
    }
}
