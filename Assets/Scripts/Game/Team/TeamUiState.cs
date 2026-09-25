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

    [Serializable]
    public sealed class TeamRole
    {
        public ulong PlayerId;
        public string Name;
        public uint Level;
        public uint ClassId;
        public uint Gender;
        public string SchoolName;
        public string CharacterId;
        public bool IsLeader;
        public bool IsOnline = true;
    }

    /// <summary>Authoritative team data supplied by the team service.</summary>
    [Serializable]
    public sealed class TeamSnapshot
    {
        public ulong TeamId;
        public ulong LeaderId;
        public ulong LocalPlayerId;
        public int Capacity = 5;
        public List<TeamRole> Members = new List<TeamRole>();
        public List<TeamRole> Applications = new List<TeamRole>();
        public List<TeamRole> Approved = new List<TeamRole>();
    }

    /// <summary>
    /// View state only: requests need a service adapter, and only authoritative
    /// snapshots may change the member, application or approved lists.
    /// </summary>
    public sealed class TeamUiState
    {
        public const float RequestTimeoutSeconds = 10f;

        private readonly Func<float> _clock;
        private int _generation;
        private int _pendingGeneration;
        private float _startedAt;

        public TeamSnapshot Snapshot { get; private set; }
        public bool ServiceAvailable { get; private set; }
        public bool IsLoading { get; private set; }
        public ulong PendingPlayerId { get; private set; }
        public string Status { get; private set; }
        public bool IsBusy => IsLoading || PendingPlayerId != 0;
        public bool IsLeader => Snapshot.LocalPlayerId != 0 &&
                                Snapshot.LocalPlayerId == Snapshot.LeaderId;
        public bool IsFull => Snapshot.Members.Count >= Snapshot.Capacity;

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
            Snapshot = new TeamSnapshot { LocalPlayerId = localPlayerId };
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

        public void SetSnapshot(TeamSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            InvalidatePending();
            Snapshot = CopySnapshot(snapshot);
            ServiceAvailable = true;
            Status = string.Empty;
            Changed?.Invoke();
        }

        public int BeginRefresh()
        {
            if (!ServiceAvailable || IsBusy) return 0;
            IsLoading = true;
            Status = "正在刷新组队信息…";
            return BeginRequest();
        }

        public int BeginDecision(ulong playerId, bool approve)
        {
            if (!ServiceAvailable || !IsLeader || IsBusy || playerId == 0 ||
                (approve && IsFull) ||
                !Snapshot.Applications.Exists(role => role.PlayerId == playerId))
                return 0;

            PendingPlayerId = playerId;
            Status = approve ? "正在同意申请…" : "正在拒绝申请…";
            return BeginRequest();
        }

        public bool Complete(int generation, TeamSnapshot snapshot, string status)
        {
            if (!MatchesPending(generation) || snapshot == null) return false;
            InvalidatePending();
            Snapshot = CopySnapshot(snapshot);
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
            if (!IsBusy || now - _startedAt < RequestTimeoutSeconds) return false;
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
        }

        private int NextGeneration()
        {
            unchecked { ++_generation; }
            if (_generation == 0) ++_generation;
            return _generation;
        }

        private static TeamSnapshot CopySnapshot(TeamSnapshot source)
        {
            return new TeamSnapshot
            {
                TeamId = source.TeamId,
                LeaderId = source.LeaderId,
                LocalPlayerId = source.LocalPlayerId,
                Capacity = source.Capacity > 0 ? source.Capacity : 5,
                Members = CopyRoles(source.Members),
                Applications = CopyRoles(source.Applications),
                Approved = CopyRoles(source.Approved)
            };
        }

        private static List<TeamRole> CopyRoles(List<TeamRole> source)
        {
            var roles = new List<TeamRole>(source?.Count ?? 0);
            if (source == null) return roles;
            foreach (TeamRole role in source)
            {
                if (role == null) continue;
                roles.Add(new TeamRole
                {
                    PlayerId = role.PlayerId,
                    Name = role.Name,
                    Level = role.Level,
                    ClassId = role.ClassId,
                    Gender = role.Gender,
                    SchoolName = role.SchoolName,
                    CharacterId = role.CharacterId,
                    IsLeader = role.IsLeader,
                    IsOnline = role.IsOnline
                });
            }
            return roles;
        }
    }
}
