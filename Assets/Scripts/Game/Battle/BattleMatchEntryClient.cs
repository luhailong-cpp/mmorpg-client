using System;
using MmorpgClient.Game.Team;

namespace MmorpgClient.Game.Battle
{
    /// <summary>根据权威队伍视图选择单人或整队匹配入口。</summary>
    public sealed class BattleMatchEntryClient
    {
        private readonly TeamClient _team;
        private readonly BattleClient _battle;
        private readonly uint _pveConfigId;
        private readonly uint _pvpConfigId;

        public BattleMatchEntryClient(TeamClient team, BattleClient battle, uint pveConfigId, uint pvpConfigId)
        {
            _team = team;
            _battle = battle;
            _pveConfigId = pveConfigId;
            _pvpConfigId = pvpConfigId;
        }

        public uint CurrentSize => _team.HasTeam ? (uint)Math.Max(1, _team.Snapshot.Members.Count) : 1;
        public bool IsMember => _team.HasTeam && !_team.IsLeader;
        public bool Pending => _team.MatchNetworkPreparing || _team.PendingAction == TeamAction.StartMatch;
        public string Status { get; private set; } = string.Empty;
        public event Action<string> Notice;

        public void StartPve()
        {
            if (!CanStart()) return;

            if (_team.HasTeam)
            {
                _team.StartMatch(_pveConfigId);
                ShowStatus(_team.Status);
                return;
            }

            _battle.JoinQueue(Match.MatchMode.PveSolo, _pveConfigId);
            ShowSoloStatus("正在发起单人 PVE…");
        }

        /// <summary>
        /// 每方 targetSize 人的 PVP 现在能不能发起:不小于队伍人数、不超过 5 人,4 人队不能打 5v5
        /// (TeamClient.PvpPartySizeForbidden)。界面用它置灰人数档,服务端仍会复核。
        /// </summary>
        public bool PvpSizeAllowed(uint targetSize) =>
            targetSize >= CurrentSize && targetSize <= 5 && !TeamClient.PvpPartySizeForbidden(targetSize, CurrentSize);

        public void StartPvp(uint targetSize)
        {
            if (!CanStart()) return;
            if (targetSize < CurrentSize || targetSize > 5)
            {
                ShowStatus("每方人数不能小于当前队伍人数，且不超过 5 人。");
                return;
            }
            if (TeamClient.PvpPartySizeForbidden(targetSize, CurrentSize))
            {
                ShowStatus(TeamClient.PvpFourStackMessage);
                return;
            }

            if (_team.HasTeam)
            {
                _team.StartMatch(_pvpConfigId, targetSize, Match.MatchMode.PvpTeam);
                ShowStatus(_team.Status);
                return;
            }

            _battle.JoinQueue(Match.MatchMode.PvpTeam, _pvpConfigId, targetSize);
            ShowSoloStatus("正在发起 PVP 匹配…");
        }

        public void Cancel() => _battle.CancelQueue();

        public void Reset() => Status = string.Empty;

        private bool CanStart()
        {
            if (!_team.HasFreshView || !_team.ServiceAvailable)
            {
                _team.RequestRefresh();
                ShowStatus("队伍信息尚未就绪，请稍候重试。");
                return false;
            }
            if (IsMember)
            {
                ShowStatus("请由队长发起全队战斗。");
                return false;
            }
            if (Pending || _team.MatchStarting || _team.MatchQueued || _battle.Phase != BattlePhase.None)
            {
                ShowStatus("已有匹配或战斗正在进行。");
                return false;
            }
            return true;
        }

        private void ShowSoloStatus(string message)
        {
            // BattleClient 已通过自己的错误事件提示同步拒绝。
            if (_battle.Phase != BattlePhase.None) ShowStatus(message);
            else Reset();
        }

        private void ShowStatus(string message)
        {
            Status = message ?? string.Empty;
            if (Status.Length != 0) Notice?.Invoke(Status);
        }
    }
}
