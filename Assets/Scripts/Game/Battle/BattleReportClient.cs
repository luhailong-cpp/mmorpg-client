using System;
using System.Collections.Generic;
using System.Globalization;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.Battle
{
    public enum BattleReportVerdict : byte
    {
        Unknown = 0,
        Win,
        Lose,
        Draw,
    }

    /// <summary>
    /// PVP 战报列表与回放拉取客户端(docs/handoff/battle-report-replay-client.md §2-§4):
    ///   - 两条请求(ListBattleReports=379, GetBattleReport=380)只走大厅连接(gate → match),不建 battle 直连;
    ///   - 列表与拉回放各自单飞;断线 / 换连接 / 换角色立即作废在途回调并清空本地缓存;
    ///   - NotFound(16025) 从当前列表移除过时条目;Internal(16004) / 限频(1008) / 传输失败只提示,不自动重试;
    ///   - 进程内保留少量已解码的不可变回放缓存(默认最多 4 份),避免重复点同一局触发限频。
    /// </summary>
    public sealed class BattleReportClient : IDisposable
    {
        public const int DefaultMaxCachedReplays = 4;

        public const string NotReadyMessage = "尚未进入游戏，暂时无法查看战报";
        public const string NotFoundMessage = "这一局没有回放，或已不在你的战报列表中";
        public const string InternalErrorMessage = "服务繁忙，请稍后重试";
        public const string RateLimitMessage = "操作太频繁，请稍后再试";
        public const string TransportErrorMessage = "请求失败，请稍后重试";
        private const string ServerTipPrefix = "server tip=";

        public static BattleReportClient Instance { get; private set; }

        public static BattleReportClient Attach(IBattleTransport net, Func<object> connectionIdentity,
            Action<string> log = null)
        {
            Instance?.Dispose();
            Instance = new BattleReportClient(net, connectionIdentity, log);
            return Instance;
        }

        public static void Detach(BattleReportClient client)
        {
            if (ReferenceEquals(Instance, client)) Instance = null;
            client?.Dispose();
        }

        private readonly IBattleTransport _net;
        private readonly Func<object> _connectionIdentity;
        private readonly Action<string> _log;
        private readonly int _maxCachedReplays;
        private readonly List<Match.BattleReportSummary> _reports = new List<Match.BattleReportSummary>();
        // 简单有界 LRU(列表尾 = 最近使用):每份解压后最大 1 MiB,上限固定在个位数。
        private readonly List<(ulong BattleId, BattleReplay Replay)> _replayCache =
            new List<(ulong, BattleReplay)>();
        private readonly Dictionary<ulong, Match.TrialChallengeCardView> _challengeCardCache =
            new Dictionary<ulong, Match.TrialChallengeCardView>();
        private bool _cardActionInFlight;

        private object _observedConnection;
        private ulong _observedPlayer;
        private int _generation;
        private bool _listInFlight;
        private bool _fetchInFlight;
        private ulong _fetchingBattleId;
        private bool _disposed;

        public ulong PlayerId => _net.PlayerId;
        public bool IsListing => _listInFlight;
        public bool IsFetching => _fetchInFlight;
        public ulong FetchingBattleId => _fetchingBattleId;
        public IReadOnlyList<Match.BattleReportSummary> Reports => _reports;

        public event Action<IReadOnlyList<Match.BattleReportSummary>> OnListUpdated;
        public event Action<BattleReplay> OnReplayLoaded;
        public event Action<ulong> OnReportRemoved;
        public event Action<string> OnError;

        public BattleReportClient(IBattleTransport net, Func<object> connectionIdentity = null,
            Action<string> log = null, int maxCachedReplays = DefaultMaxCachedReplays)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _connectionIdentity = connectionIdentity ?? (() => _net);
            _log = log;
            _maxCachedReplays = Math.Max(0, maxCachedReplays);
            _observedConnection = _connectionIdentity();
            _observedPlayer = _net.PlayerId;
            _net.Disconnected += HandleDisconnected;
        }

        /// <summary>拉本人最近 PVP 战报列表(最多 100 条,服务端已排好序)。单飞,在途时返回 false。</summary>
        public bool RefreshList()
        {
            if (_disposed) return false;
            ObserveSession();
            if (_listInFlight) return false;
            if (!_net.IsReady || _net.PlayerId == 0)
            {
                OnError?.Invoke(NotReadyMessage);
                return false;
            }

            _listInFlight = true;
            int generation = _generation;
            ulong player = _observedPlayer;
            object connection = _observedConnection;

            _net.Call(MatchServiceRpc.ListBattleReportsMessageId,
                new Match.ListBattleReportsRequest(),
                Match.ListBattleReportsResponse.Parser,
                resp =>
                {
                    if (!IsCurrentCall(generation, player, connection)) return;
                    _listInFlight = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        OnError?.Invoke(DescribeTip(resp.ErrorMessage.Id));
                        return;
                    }
                    _reports.Clear();
                    if (resp?.Reports != null)
                    {
                        // 服务端已按 (created_ms, battle_id) 倒序排好,原样保留,不按 battle_id 重排。
                        foreach (var item in resp.Reports)
                        {
                            if (item != null) _reports.Add(item.Clone());
                        }
                    }
                    OnListUpdated?.Invoke(_reports);
                },
                err =>
                {
                    if (!IsCurrentCall(generation, player, connection)) return;
                    _listInFlight = false;
                    OnError?.Invoke(DescribeTransportError(err));
                });
            return true;
        }

        /// <summary>取一局战报的回放并解码。单飞;命中本地缓存时直接交付。传入 onReady 时仅回调不触发自动播放。</summary>
        public bool FetchReplay(ulong battleId, Action<BattleReplay> onReady = null)
        {
            if (_disposed || battleId == 0) return false;
            ObserveSession();
            if (_fetchInFlight) return false;

            if (TryGetCachedReplay(battleId, out var cached))
            {
                if (onReady != null) onReady(cached);
                else OnReplayLoaded?.Invoke(cached);
                return true;
            }

            if (!_net.IsReady || _net.PlayerId == 0)
            {
                OnError?.Invoke(NotReadyMessage);
                return false;
            }

            _fetchInFlight = true;
            _fetchingBattleId = battleId;
            int generation = _generation;
            ulong player = _observedPlayer;
            object connection = _observedConnection;

            _net.Call(MatchServiceRpc.GetBattleReportMessageId,
                new Match.GetBattleReportRequest { BattleId = battleId },
                Match.GetBattleReportResponse.Parser,
                resp =>
                {
                    if (!IsCurrentCall(generation, player, connection)) return;
                    _fetchInFlight = false;
                    _fetchingBattleId = 0;

                    if (HasTip(resp?.ErrorMessage))
                    {
                        uint tipId = resp.ErrorMessage.Id;
                        if (tipId == (uint)match_error.KMatchBattleReportNotFound)
                        {
                            RemoveCachedReport(battleId);
                            OnError?.Invoke(NotFoundMessage);
                        }
                        else
                        {
                            OnError?.Invoke(DescribeTip(tipId));
                        }
                        return;
                    }

                    var decoded = BattleReplayDecoder.Decode(resp?.Replay);
                    if (!decoded.Success)
                    {
                        _log?.Invoke($"decode failed battle_id={battleId} error={decoded.Error}");
                        OnError?.Invoke(decoded.Message);
                        return;
                    }
                    if (decoded.Replay.BattleId != battleId)
                    {
                        _log?.Invoke($"decode failed battle_id={battleId} error=BattleIdMismatch actual={decoded.Replay.BattleId}");
                        OnError?.Invoke(BattleReplayDecoder.CorruptReplayMessage);
                        return;
                    }

                    CacheReplay(battleId, decoded.Replay);
                    if (onReady != null) onReady(decoded.Replay);
                    else OnReplayLoaded?.Invoke(decoded.Replay);
                },
                err =>
                {
                    if (!IsCurrentCall(generation, player, connection)) return;
                    _fetchInFlight = false;
                    _fetchingBattleId = 0;
                    OnError?.Invoke(DescribeTransportError(err));
                });
            return true;
        }

        /// <summary>五行试炼志 V3：将本人近期通关试炼战报签发为可分享约战帖。</summary>
        public bool CreateTrialChallengeCard(ulong battleId, Action<Match.TrialChallengeCardView> onCreated,
            Action<string> onError = null)
        {
            if (_disposed || battleId == 0ul) return false;
            ObserveSession();
            if (_cardActionInFlight) return false;
            if (!_net.IsReady || _net.PlayerId == 0)
            {
                string msg = NotReadyMessage;
                onError?.Invoke(msg);
                OnError?.Invoke(msg);
                return false;
            }

            _cardActionInFlight = true;
            int generation = _generation;
            ulong player = _observedPlayer;
            object connection = _observedConnection;
            _net.Call(MatchServiceRpc.CreateTrialChallengeCardMessageId,
                new Match.CreateTrialChallengeCardRequest { BattleId = battleId },
                Match.CreateTrialChallengeCardResponse.Parser,
                resp =>
                {
                    if (!IsCurrentCall(generation, player, connection)) return;
                    _cardActionInFlight = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        string msg = DescribeTip(resp.ErrorMessage.Id);
                        onError?.Invoke(msg);
                        OnError?.Invoke(msg);
                        return;
                    }
                    if (resp?.Card == null || resp.Card.CardId == 0ul)
                    {
                        string msg = "未能生成有效约战帖，请稍后重试";
                        onError?.Invoke(msg);
                        OnError?.Invoke(msg);
                        return;
                    }
                    var cloned = resp.Card.Clone();
                    _challengeCardCache[cloned.CardId] = cloned;
                    onCreated?.Invoke(cloned);
                },
                err =>
                {
                    if (!IsCurrentCall(generation, player, connection)) return;
                    _cardActionInFlight = false;
                    string msg = DescribeTransportError(err);
                    onError?.Invoke(msg);
                    OnError?.Invoke(msg);
                });
            return true;
        }

        /// <summary>五行试炼志 V3：按 card_id 读取权威约战帖详情（命中会话内缓存时直返）。</summary>
        public bool TryGetCachedChallengeCard(ulong cardId, out Match.TrialChallengeCardView card)
        {
            ObserveSession();
            return _challengeCardCache.TryGetValue(cardId, out card);
        }

        public bool GetTrialChallengeCard(ulong cardId, Action<Match.TrialChallengeCardView> onLoaded,
            Action<string> onError = null)
        {
            if (_disposed || cardId == 0ul) return false;
            ObserveSession();
            if (_challengeCardCache.TryGetValue(cardId, out var cached))
            {
                onLoaded?.Invoke(cached);
                return true;
            }
            if (!_net.IsReady || _net.PlayerId == 0)
            {
                onError?.Invoke(NotReadyMessage);
                return false;
            }
            int generation = _generation;
            ulong player = _observedPlayer;
            object connection = _observedConnection;
            _net.Call(MatchServiceRpc.GetTrialChallengeCardMessageId,
                new Match.GetTrialChallengeCardRequest { CardId = cardId },
                Match.GetTrialChallengeCardResponse.Parser,
                resp =>
                {
                    if (!IsCurrentCall(generation, player, connection)) return;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        onError?.Invoke(resp.ErrorMessage.Id == (uint)match_error.KMatchBattleReportNotFound
                            ? "该约战帖已过期或不存在"
                            : DescribeTip(resp.ErrorMessage.Id));
                        return;
                    }
                    if (resp?.Card == null || resp.Card.CardId == 0ul)
                    {
                        onError?.Invoke("该约战帖已过期或不存在");
                        return;
                    }
                    var cloned = resp.Card.Clone();
                    if (_challengeCardCache.Count >= 64) _challengeCardCache.Clear();
                    _challengeCardCache[cloned.CardId] = cloned;
                    onLoaded?.Invoke(cloned);
                },
                err =>
                {
                    if (!IsCurrentCall(generation, player, connection)) return;
                    onError?.Invoke(DescribeTransportError(err));
                });
            return true;
        }

        /// <summary>五行试炼志 V3：凭有效约战帖 card_id 拉取并播放受控共享的试炼回放。</summary>
        public bool FetchSharedTrialReplay(ulong cardId, Action<BattleReplay> onReady = null)
        {
            if (_disposed || cardId == 0ul) return false;
            ObserveSession();
            if (_fetchInFlight) return false;
            if (!_net.IsReady || _net.PlayerId == 0)
            {
                OnError?.Invoke(NotReadyMessage);
                return false;
            }
            _fetchInFlight = true;
            _fetchingBattleId = 0ul;
            int generation = _generation;
            ulong player = _observedPlayer;
            object connection = _observedConnection;
            _net.Call(MatchServiceRpc.GetSharedTrialReplayMessageId,
                new Match.GetSharedTrialReplayRequest { CardId = cardId },
                Match.GetSharedTrialReplayResponse.Parser,
                resp =>
                {
                    if (!IsCurrentCall(generation, player, connection)) return;
                    _fetchInFlight = false;
                    if (HasTip(resp?.ErrorMessage))
                    {
                        OnError?.Invoke(resp.ErrorMessage.Id == (uint)match_error.KMatchBattleReportNotFound
                            ? "该约战帖的回放已过期或不可用"
                            : DescribeTip(resp.ErrorMessage.Id));
                        return;
                    }
                    var decoded = BattleReplayDecoder.Decode(resp?.Replay);
                    if (!decoded.Success)
                    {
                        _log?.Invoke($"shared trial replay decode failed card_id={cardId} error={decoded.Error}");
                        OnError?.Invoke(decoded.Message);
                        return;
                    }
                    CacheReplay(decoded.Replay.BattleId, decoded.Replay);
                    if (onReady != null) onReady(decoded.Replay);
                    else OnReplayLoaded?.Invoke(decoded.Replay);
                },
                err =>
                {
                    if (!IsCurrentCall(generation, player, connection)) return;
                    _fetchInFlight = false;
                    OnError?.Invoke(DescribeTransportError(err));
                });
            return true;
        }

        public void ObserveSession()
        {
            if (_disposed) return;
            object connection = _connectionIdentity();
            ulong player = _net.PlayerId;
            if (ReferenceEquals(connection, _observedConnection) && player == _observedPlayer) return;
            ResetState(connection, player);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _net.Disconnected -= HandleDisconnected;
            _disposed = true;
            _generation++;
            _listInFlight = false;
            _fetchInFlight = false;
            _cardActionInFlight = false;
            _fetchingBattleId = 0;
            _reports.Clear();
            _replayCache.Clear();
            _challengeCardCache.Clear();
        }

        // ── 纯函数(UI 与 EditMode 共用) ─────────────────────

        /// <summary>
        /// 计算本人在一场战报里的胜负(handoff §2):
        /// 在 members 中按 player_id 找本人取 team_index,再对照 outcome。
        /// </summary>
        public static BattleReportVerdict ResolveVerdict(Match.BattleReportSummary summary, ulong myPlayerId)
        {
            if (summary == null || myPlayerId == 0) return BattleReportVerdict.Unknown;
            if (summary.Outcome == eBattleOutcome.BattleOutcomeDraw) return BattleReportVerdict.Draw;
            if (summary.Outcome != eBattleOutcome.BattleOutcomeSideAWin
                && summary.Outcome != eBattleOutcome.BattleOutcomeSideBWin)
                return BattleReportVerdict.Unknown;

            bool found = false;
            uint myTeam = 0;
            foreach (var member in summary.Members)
            {
                if (member != null && member.PlayerId == myPlayerId)
                {
                    myTeam = member.TeamIndex;
                    found = true;
                    break;
                }
            }
            if (!found) return BattleReportVerdict.Unknown;

            bool won = (summary.Outcome == eBattleOutcome.BattleOutcomeSideAWin && myTeam == 0)
                       || (summary.Outcome == eBattleOutcome.BattleOutcomeSideBWin && myTeam == 1);
            return won ? BattleReportVerdict.Win : BattleReportVerdict.Lose;
        }

        /// <summary>服务端只录 PVP(go/match/internal/logic/battle_replay_policy.go 同口径)。</summary>
        public static bool IsPvpMode(Match.MatchMode mode) => mode switch
        {
            Match.MatchMode._1V1 => true,
            Match.MatchMode._3V3 => true,
            Match.MatchMode._5V5 => true,
            Match.MatchMode.PvpChallenge => true,
            Match.MatchMode.PvpTeam => true,
            Match.MatchMode.Polarity5V5 => true,
            Match.MatchMode.GuildWarPvp => true,
            Match.MatchMode.SeasonTournament => true,
            _ => false,
        };

        public static bool IsPvpMode(uint matchMode) => IsPvpMode((Match.MatchMode)matchMode);

        /// <summary>近期战斗结果面板是否提供「看回放」入口(非作废的 PVP 局或试炼志有格 PVE 局，且有有效 battle_id)。</summary>
        public static bool CanWatchReplayFromRecovered(PlayerBattleResult result)
            => result != null && result.BattleId != 0
               && !BattleResultRecoveryClient.IsVoid(result)
               && (IsPvpMode(result.MatchMode) || result.TrialJournalEntryId != 0u);

        public static string ModeName(Match.MatchMode mode) => mode switch
        {
            Match.MatchMode._1V1 => "PVP 1v1",
            Match.MatchMode._3V3 => "PVP 3v3",
            Match.MatchMode._5V5 => "PVP 5v5",
            Match.MatchMode.PvpTeam => "PVP 组队",
            Match.MatchMode.Polarity5V5 => "物法易势 5v5",
            Match.MatchMode.GuildWarPvp => "帮战直打",
            Match.MatchMode.GuildWarSiege => "帮战踢馆",
            Match.MatchMode.SeasonTournament => "赛季锦标赛",
            Match.MatchMode.PvpChallenge => "切磋 PK",
            Match.MatchMode.PveSolo => "PVE 单人",
            Match.MatchMode.PveTeam => "PVE 组队",
            _ => $"模式{(int)mode}",
        };

        /// <summary>职业展示名(与选角/组队 UI 同口径;0 表示未带职业,大于已知表项回落为「职业#N」)。</summary>
        public static string ClassName(uint classId) => classId switch
        {
            0 => string.Empty,
            1 => "破军",
            2 => "玄霄",
            3 => "丹心",
            4 => "逐风",
            _ => $"职业#{classId.ToString(CultureInfo.InvariantCulture)}",
        };

        public static string DescribeTip(uint tipId) => tipId switch
        {
            (uint)match_error.KMatchBattleReportNotFound => NotFoundMessage,
            (uint)match_error.KMatchInternal => InternalErrorMessage,
            (uint)common_error.KRateLimitExceeded => RateLimitMessage,
            (uint)common_error.KServiceUnavailable => InternalErrorMessage,
            _ => $"操作失败({tipId.ToString(CultureInfo.InvariantCulture)})",
        };

        public static uint ParseEnvelopeTip(string error) => error != null
            && error.StartsWith(ServerTipPrefix, StringComparison.Ordinal)
            && uint.TryParse(error.Substring(ServerTipPrefix.Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out uint tip)
                ? tip : 0;

        // ── 内部 ────────────────────────────────────────────

        private void HandleDisconnected() => ResetState(_connectionIdentity(), _net.PlayerId);

        private void ResetState(object connection, ulong player)
        {
            _observedConnection = connection;
            _observedPlayer = player;
            _generation++;
            _listInFlight = false;
            _fetchInFlight = false;
            _cardActionInFlight = false;
            _fetchingBattleId = 0;
            _reports.Clear();
            _replayCache.Clear();
            _challengeCardCache.Clear();
        }

        private bool IsCurrentCall(int generation, ulong player, object connection)
            => !_disposed && generation == _generation && _net.IsReady
               && player != 0 && _net.PlayerId == player
               && ReferenceEquals(connection, _connectionIdentity());

        private static bool HasTip(TipInfoMessage tip) => tip != null && tip.Id != 0;

        private static string DescribeTransportError(string error)
        {
            uint tip = ParseEnvelopeTip(error);
            if (tip != 0) return DescribeTip(tip);
            return TransportErrorMessage;
        }

        private void RemoveCachedReport(ulong battleId)
        {
            bool removed = false;
            for (int i = _reports.Count - 1; i >= 0; i--)
            {
                if (_reports[i].BattleId == battleId)
                {
                    _reports.RemoveAt(i);
                    removed = true;
                }
            }
            for (int i = _replayCache.Count - 1; i >= 0; i--)
            {
                if (_replayCache[i].BattleId == battleId) _replayCache.RemoveAt(i);
            }
            if (removed)
            {
                OnReportRemoved?.Invoke(battleId);
                OnListUpdated?.Invoke(_reports);
            }
        }

        private bool TryGetCachedReplay(ulong battleId, out BattleReplay replay)
        {
            for (int i = 0; i < _replayCache.Count; i++)
            {
                if (_replayCache[i].BattleId != battleId) continue;
                var entry = _replayCache[i];
                // 移到尾部表示最近访问
                _replayCache.RemoveAt(i);
                _replayCache.Add(entry);
                replay = entry.Replay;
                return true;
            }
            replay = null;
            return false;
        }

        private void CacheReplay(ulong battleId, BattleReplay replay)
        {
            if (_maxCachedReplays <= 0 || replay == null) return;
            for (int i = _replayCache.Count - 1; i >= 0; i--)
            {
                if (_replayCache[i].BattleId == battleId) _replayCache.RemoveAt(i);
            }
            while (_replayCache.Count >= _maxCachedReplays) _replayCache.RemoveAt(0);
            _replayCache.Add((battleId, replay));
        }
    }
}
