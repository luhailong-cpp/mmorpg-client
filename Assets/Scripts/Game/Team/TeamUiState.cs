using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MmorpgClient.Game.Team
{
    public enum TeamPage
    {
        Members,
        Applications,
        Approved
    }

    /// <summary>
    /// View state only. The live path mirrors TeamClient through Sync; the generation-token API
    /// remains for offline previews and tests. Only authoritative snapshots may change the lists.
    /// </summary>
    public sealed class TeamUiState
    {
        /// <summary>Longer than GameClient.Call's 15s deadline; applies to the token path only.</summary>
        public const float RequestTimeoutSeconds = 20f;

        private readonly Func<float> _clock;
        private readonly List<TeamInvite> _invites = new List<TeamInvite>();
        private int _generation;
        private int _pendingGeneration;
        private int _sessionFloor;
        private float _startedAt;
        private bool _hasAuthoritative;

        public TeamSnapshot Snapshot { get; private set; }
        public IReadOnlyList<TeamInvite> Invites => _invites;
        public bool HasLoaded { get; private set; }
        public bool ServiceAvailable { get; private set; }
        public bool IsLoading { get; private set; }
        public ulong PendingPlayerId { get; private set; }
        public TeamAction PendingAction { get; private set; }
        public ulong PendingTarget { get; private set; }
        public ulong HighlightPlayerId { get; private set; }
        public string Status { get; private set; }
        public bool IsBusy => PendingAction != TeamAction.None || IsLoading || PendingPlayerId != 0;
        public bool HasTeam => Snapshot.TeamId != 0;
        public bool IsLeader => HasTeam && Snapshot.LocalPlayerId != 0 &&
                                Snapshot.LocalPlayerId == Snapshot.LeaderId;
        public bool IsFull => Snapshot.Members.Count >= Snapshot.Capacity;
        public bool MatchStarting => HasTeam && Snapshot.MatchStarting;

        public event Action Changed;

        public TeamUiState(Func<float> clock = null)
        {
            if (clock == null)
            {
                var stopwatch = Stopwatch.StartNew();
                _clock = () => (float)stopwatch.Elapsed.TotalSeconds;
            }
            else
            {
                _clock = clock;
            }

            Snapshot = new TeamSnapshot();
            Status = "组队服务尚未连接";
        }

        public void Reset(ulong localPlayerId = 0)
        {
            InvalidatePending();
            // Tokens issued before this point belong to the previous session and never apply a view.
            _sessionFloor = _generation;
            Snapshot = new TeamSnapshot { LocalPlayerId = localPlayerId };
            _hasAuthoritative = false;
            HasLoaded = false;
            _invites.Clear();
            HighlightPlayerId = 0;
            ServiceAvailable = false;
            Status = "组队服务尚未连接";
            Changed?.Invoke();
        }

        public void SetUnavailable(string status)
        {
            InvalidatePending();
            ServiceAvailable = false;
            Status = status ?? "组队服务暂不可用";
            Changed?.Invoke();
        }

        /// <summary>The transport is ready; this does not manufacture an authoritative team snapshot.</summary>
        public void SetConnected()
        {
            ServiceAvailable = true;
            Status = "正在同步队伍…";
            Changed?.Invoke();
        }

        /// <summary>
        /// Push-style apply: ordered by epoch/version, never touches the in-flight token.
        /// Returns false when the snapshot is stale or conflicts with the current team.
        /// </summary>
        public bool SetSnapshot(TeamSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (TeamViewMapper.Compare(_hasAuthoritative ? Snapshot : null, snapshot) != TeamSnapshotOrder.Accept)
                return false;
            Snapshot = snapshot.Clone();
            _hasAuthoritative = true;
            HasLoaded = true;
            ServiceAvailable = true;
            if (!IsBusy) Status = string.Empty;
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Live-path mirror of TeamClient. The client already ordered every view and owns the
        /// session boundary, so this copies everything verbatim and drops any local token.
        /// </summary>
        public void Sync(TeamSnapshot snapshot, IReadOnlyList<TeamInvite> invites, bool loaded, bool available,
            TeamAction pendingAction, ulong pendingTarget, ulong highlightPlayerId, string status)
        {
            NextGeneration();
            _pendingGeneration = 0;
            // Tokens issued before a mirror are stale: a late Complete must not overwrite the mirrored view.
            _sessionFloor = _generation;
            Snapshot = snapshot?.Clone() ?? new TeamSnapshot { LocalPlayerId = Snapshot.LocalPlayerId };
            _hasAuthoritative = loaded;
            _invites.Clear();
            if (invites != null)
                foreach (TeamInvite invite in invites)
                    if (invite != null) _invites.Add(invite.Clone());
            HasLoaded = loaded;
            ServiceAvailable = available;
            PendingAction = pendingAction;
            PendingTarget = pendingTarget;
            IsLoading = pendingAction == TeamAction.Refresh || pendingAction == TeamAction.ListInvites;
            PendingPlayerId = pendingAction is TeamAction.Decide or TeamAction.Kick or TeamAction.Transfer
                ? pendingTarget : 0;
            HighlightPlayerId = highlightPlayerId;
            Status = status ?? string.Empty;
            Changed?.Invoke();
        }

        public int BeginRefresh()
        {
            if (!ServiceAvailable || IsBusy) return 0;
            IsLoading = true;
            PendingAction = TeamAction.Refresh;
            Status = "正在刷新组队信息…";
            return BeginRequest();
        }

        public int BeginDecision(ulong playerId, bool approve)
        {
            if (!ServiceAvailable || !IsLeader || IsBusy || playerId == 0 ||
                (approve && (IsFull || MatchStarting)) ||
                !Snapshot.Applications.Exists(role => role != null && role.PlayerId == playerId))
                return 0;

            PendingPlayerId = playerId;
            PendingAction = TeamAction.Decide;
            PendingTarget = playerId;
            Status = approve ? "正在同意申请…" : "正在拒绝申请…";
            return BeginRequest();
        }

        /// <summary>
        /// Reply path: first try the view under the ordering rules, then end the matching request.
        /// A late reply may still refresh the view but never changes Status or another request.
        /// </summary>
        public bool Complete(int generation, TeamSnapshot snapshot, string status)
        {
            bool matches = MatchesPending(generation);
            bool sameSession = generation != 0 && unchecked(generation - _sessionFloor) > 0;
            bool applied = false;
            if (snapshot != null && sameSession && ServiceAvailable &&
                TeamViewMapper.Compare(_hasAuthoritative ? Snapshot : null, snapshot) == TeamSnapshotOrder.Accept)
            {
                Snapshot = snapshot.Clone();
                _hasAuthoritative = true;
                HasLoaded = true;
                applied = true;
            }
            if (!matches)
            {
                if (applied) Changed?.Invoke();
                return false;
            }
            InvalidatePending();
            ServiceAvailable = true;
            Status = status ?? string.Empty;
            Changed?.Invoke();
            return true;
        }

        public bool Fail(int generation, string status)
        {
            if (!MatchesPending(generation)) return false;
            InvalidatePending();
            Status = status ?? "请求失败，请重试";
            Changed?.Invoke();
            return true;
        }

        public bool Tick(float now)
        {
            // Mirrored busy state carries no token and must never time out here.
            if (!IsBusy || _pendingGeneration == 0 || now - _startedAt < RequestTimeoutSeconds) return false;
            return Fail(_pendingGeneration, "请求超时，请重试");
        }

        private int BeginRequest()
        {
            _pendingGeneration = NextGeneration();
            _startedAt = _clock();
            int token = _pendingGeneration;
            Changed?.Invoke();
            return token;
        }

        private bool MatchesPending(int generation)
        {
            return IsBusy && generation != 0 && generation == _pendingGeneration;
        }

        private void InvalidatePending()
        {
            NextGeneration();
            _pendingGeneration = 0;
            IsLoading = false;
            PendingPlayerId = 0;
            PendingAction = TeamAction.None;
            PendingTarget = 0;
        }

        private int NextGeneration()
        {
            unchecked { ++_generation; }
            if (_generation == 0) ++_generation;
            return _generation;
        }
    }
}
