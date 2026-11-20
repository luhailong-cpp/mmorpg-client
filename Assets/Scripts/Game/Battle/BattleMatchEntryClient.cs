using System;
using System.Diagnostics;
using MmorpgClient.Game.Team;
using MmorpgClient.Net;

namespace MmorpgClient.Game.Battle
{
    /// <summary>根据权威队伍视图选择单人或整队匹配入口。</summary>
    public sealed class BattleMatchEntryClient
    {
        private readonly TeamClient _team;
        private readonly BattleClient _battle;
        private readonly uint _pveConfigId;
        private readonly uint _pvpConfigId;
        private readonly Func<float> _clock;
        // 与 GameClient.RpcGuard 共享同一套按消息号限流与按服务域熔断；单测无宿主时自建。
        private readonly RpcGuard _guard;
        private readonly bool _ownsGuard;

        public BattleMatchEntryClient(TeamClient team, BattleClient battle, uint pveConfigId, uint pvpConfigId,
                                      Func<float> clock = null, RpcGuard rpcGuard = null)
        {
            _team = team;
            _battle = battle;
            _pveConfigId = pveConfigId;
            _pvpConfigId = pvpConfigId;
            if (clock == null)
            {
                var stopwatch = Stopwatch.StartNew();
                _clock = () => (float)stopwatch.Elapsed.TotalSeconds;
            }
            else
            {
                _clock = clock;
            }
            _ownsGuard = rpcGuard == null;
            _guard = rpcGuard ?? new RpcGuard(_clock);
        }

        public uint CurrentSize => _team.HasTeam ? (uint)Math.Max(1, _team.Snapshot.Members.Count) : 1;
        public bool IsMember => _team.HasTeam && !_team.IsLeader;
        public bool Pending => _team.MatchNetworkPreparing || _team.PendingAction == TeamAction.StartMatch;
        public bool CanStartNow => StartBlockedReason.Length == 0;
        public bool CanCancelNow => _battle.Phase != BattlePhase.Queued || _guard.CanSend(MessageIds.CancelQueue);
        public string StartBlockedReason
        {
            get
            {
                if (!_team.HasFreshView || !_team.ServiceAvailable) return "队伍信息尚未就绪，请稍候重试。";
                if (IsMember) return "请由队长发起全队战斗。";
                if (Pending) return "正在准备匹配，请稍候重试。";
                if (_team.MatchStarting || _team.MatchQueued || _battle.Phase != BattlePhase.None)
                    return "已有匹配或战斗正在进行。";
                if (_team.HasTeam && _team.Busy) return "正在同步队伍，请稍候重试。";
                if (_team.HasTeam && _team.StartMatchCoolingDown) return "操作冷却中，请稍候再试。";
                if (!_team.HasTeam && _guard.IsCircuitOpen(MessageIds.JoinQueue)) return "匹配服务暂时繁忙，请稍候再试。";
                if (!_team.HasTeam && _guard.IsRateLimited(MessageIds.JoinQueue)) return "操作冷却中，请稍候再试。";
                return string.Empty;
            }
        }
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

            if (!ConsumeLocalQuota(MessageIds.JoinQueue)) return;
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

            if (!ConsumeLocalQuota(MessageIds.JoinQueue)) return;
            _battle.JoinQueue(Match.MatchMode.PvpTeam, _pvpConfigId, targetSize);
            ShowSoloStatus("正在发起 PVP 匹配…");
        }

        public bool Cancel()
        {
            if (_battle.Phase == BattlePhase.Queued && !ConsumeLocalQuota(MessageIds.CancelQueue))
                return false;
            Reset();
            _battle.CancelQueue();
            return true;
        }

        private bool ConsumeLocalQuota(uint messageId)
        {
            if (_guard.IsCircuitOpen(messageId)) { ShowStatus("匹配服务暂时繁忙，请稍候再试。"); return false; }
            if (_guard.IsRateLimited(messageId)) { ShowStatus("操作冷却中，请稍候再试。"); return false; }
            // 共享 GameClient.RpcGuard 时由 GameClient.CallMethod 统一记账，避免重复计数。
            if (_ownsGuard && !_guard.TryBeginCall(messageId, out _)) { ShowStatus("操作冷却中，请稍候再试。"); return false; }
            return true;
        }

        public void Reset() => Status = string.Empty;

        private bool CanStart()
        {
            string reason = StartBlockedReason;
            if (reason.Length == 0) return true;
            if (!_team.HasFreshView || !_team.ServiceAvailable) _team.RequestRefresh();
            ShowStatus(reason);
            return false;
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
