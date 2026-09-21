using System;
using MmorpgClient.Net;
using MmorpgClient.World;
using Teampb;

namespace MmorpgClient.Game.Team
{
    /// <summary>Existing team RPCs feed the city UI with authoritative member identities.</summary>
    public sealed class TeamAppearanceTransport : IDisposable
    {
        private readonly GameClient _game;
        private readonly TeamUiState _state;
        private readonly ulong _playerId;
        private readonly object _gate;
        private TeamView _accepted;
        private bool _disposed;

        public TeamAppearanceTransport(GameClient game, TeamUiState state)
        {
            _game = game;
            _state = state;
            _playerId = game.PlayerId;
            _gate = game.GateConnectionIdentity;
            _game.OnTeamSnapshot += OnSnapshot;
        }

        public bool IsCurrent => !_disposed && _game.InGame && _game.IsGateReady &&
            _game.PlayerId == _playerId && ReferenceEquals(_gate, _game.GateConnectionIdentity);

        public void Connect()
        {
            if (!IsCurrent || _game.CoroutineRunner == null) return;
            _state.SetConnected();
            Refresh(_state.BeginRefresh(), true);
        }

        public void Refresh(int generation, bool notifyOnline = false)
        {
            if (!IsCurrent || generation == 0 || _game.CoroutineRunner == null) return;
            _game.CoroutineRunner(_game.Call(MessageIds.GetMyTeam,
                new GetMyTeamRequest { NotifyOnline = notifyOnline }, TeamResponse.Parser,
                response => Complete(generation, response),
                _ => { if (IsCurrent) _state.Fail(generation, "队伍信息暂未同步，请重试"); }));
        }

        public void Decide(int generation, ulong playerId, bool approve)
        {
            if (!IsCurrent || generation == 0 || _game.CoroutineRunner == null) return;
            _game.CoroutineRunner(_game.Call(MessageIds.HandleApplication,
                new HandleApplicationRequest
                {
                    ApplicantId = playerId,
                    Approve = approve,
                    ExpectedTeamId = _state.Snapshot.TeamId,
                }, TeamResponse.Parser, response => Complete(generation, response),
                _ => { if (IsCurrent) _state.Fail(generation, "处理申请失败，请刷新后重试"); }));
        }

        private void Complete(int generation, TeamResponse response)
        {
            if (!IsCurrent) return;
            string status = response.ErrorMessage?.Id > 0 ? "队伍状态已变化，请确认后重试" : string.Empty;
            if (response.Team == null) { _state.Fail(generation, "队伍信息暂未同步，请重试"); return; }
            // A push may have already invalidated this request's generation. Newer authoritative
            // views still apply; stale replies cannot replace a later membership/version.
            if (Accept(response.Team))
            {
                var snapshot = ToSnapshot(response.Team, _playerId);
                if (!_state.Complete(generation, snapshot, status)) _state.SetSnapshot(snapshot);
            }
            else _state.Complete(generation, _state.Snapshot, status);
        }

        private void OnSnapshot(TeamSnapshotS2C message)
        {
            if (IsCurrent && message.Team != null && Accept(message.Team))
                _state.SetSnapshot(ToSnapshot(message.Team, _playerId));
        }

        private bool Accept(TeamView view)
        {
            if (_accepted != null && !IsAtLeastAsRecent(view, _accepted)) return false;
            _accepted = view.Clone();
            return true;
        }

        public static bool IsAtLeastAsRecent(TeamView incoming, TeamView previous)
        {
            if (incoming.MembershipEpoch != previous.MembershipEpoch)
                return incoming.MembershipEpoch > previous.MembershipEpoch;
            if (incoming.Version != previous.Version) return incoming.Version > previous.Version;
            return incoming.ServerTimeMs >= previous.ServerTimeMs;
        }

        public static TeamSnapshot ToSnapshot(TeamView view, ulong localPlayerId)
        {
            var result = new TeamSnapshot
            {
                TeamId = view.TeamId, LeaderId = view.LeaderId, LocalPlayerId = localPlayerId,
                Capacity = view.Capacity > 0 ? (int)view.Capacity : 5,
            };
            foreach (var member in view.Members) result.Members.Add(ToRole(member));
            foreach (var application in view.Applications)
                if (application.Player != null) result.Applications.Add(ToRole(application.Player));
            return result;
        }

        private static TeamRole ToRole(TeamMemberView role) => new()
        {
            PlayerId = role.PlayerId, Name = role.Name, Level = role.Level,
            ClassId = role.ClassId, Gender = role.Gender,
            CharacterId = !string.IsNullOrEmpty(role.AppearanceId) || role.ClassId != 0 && role.Gender != 0
                ? QdaoCharacterCatalog.ResolveRole(role.ClassId, role.Gender, role.AppearanceId) : null,
            IsLeader = role.IsLeader, IsOnline = role.IsOnline,
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _game.OnTeamSnapshot -= OnSnapshot;
        }
    }
}
