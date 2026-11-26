using System;
using System.Collections.Generic;
using System.Diagnostics;
using MmorpgClient.Game.Team;
using MmorpgClient.Game.TrialJournal;
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

        public bool TeamRankedPresetsReady => !_team.HasTeam || _team.Snapshot.RankedPresetsAllConfirmed;

        /// <summary>
        /// 正式人宠会武报名入口（1V1–5V5 共用 PVP_TEAM，使用本人真实养成，开放人数档以服务端下发为准）。
        /// </summary>
        public bool StartRankedPvp(uint targetSize, uint characterPresetId, uint petPresetId, uint rankedRuleVersion,
            uint battleConfigId = 0, IReadOnlyCollection<uint> openTeamSizes = null, bool requireTeamPresetsConfirmed = false)
        {
            if (!CanStart()) return false;
            if (targetSize < CurrentSize || targetSize > 5)
            {
                ShowStatus("每方人数不能小于当前队伍人数，且不超过 5 人。");
                return false;
            }
            if (openTeamSizes != null && openTeamSizes.Count > 0 && !ContainsSize(openTeamSizes, targetSize))
            {
                ShowStatus(targetSize == 2u || targetSize == 4u
                    ? $"{targetSize}V{targetSize} 为特殊玩法模式，当前未开放。"
                    : $"当前未开放 {targetSize}V{targetSize} 匹配。");
                return false;
            }
            if (TeamClient.PvpPartySizeForbidden(targetSize, CurrentSize))
            {
                ShowStatus("再邀一位好友即可报名");
                return false;
            }
            uint configId = battleConfigId != 0 ? battleConfigId : _pvpConfigId;
            if (_team.HasTeam)
            {
                if (requireTeamPresetsConfirmed && rankedRuleVersion != 0 && !_team.Snapshot.RankedPresetsAllConfirmed)
                {
                    ShowStatus("还有队员未确认论剑战法预设。");
                    return false;
                }
                bool sent = _team.StartMatch(configId, targetSize, Match.MatchMode.PvpTeam, rankedRuleVersion);
                ShowStatus(_team.Status);
                return sent;
            }
            if (!ConsumeLocalQuota(MessageIds.JoinQueue)) return false;
            _battle.JoinQueue(Match.MatchMode.PvpTeam, configId, targetSize, characterPresetId, petPresetId, rankedRuleVersion);
            ShowSoloStatus("报名中…");
            return _battle.Phase != BattlePhase.None;
        }

        /// <summary>发起指定首领试炼或护卫救援（Dungeon 101–109，支持单人 PveSolo 或队长整队 PveTeam；109 护卫救援可附带免费 3×3 预设）。</summary>
        public bool StartBossTrial(uint dungeonConfigId, uint characterPresetId = 0, uint petPresetId = 0)
        {
            if (!CanStart()) return false;
            if (dungeonConfigId == 0)
            {
                ShowStatus("试炼副本配置无效。");
                return false;
            }
            if (_team.HasTeam)
            {
                bool sent = _team.StartMatch(dungeonConfigId);
                ShowStatus(_team.Status);
                return sent;
            }
            if (!ConsumeLocalQuota(MessageIds.JoinQueue)) return false;
            _battle.JoinQueue(Match.MatchMode.PveSolo, dungeonConfigId, 0u, characterPresetId, petPresetId, 0u);
            ShowSoloStatus(dungeonConfigId == 109u ? "正在发起护卫救援…" : "正在发起首领试炼…");
            return _battle.Phase != BattlePhase.None;
        }

        /// <summary>发起当季物法易势 5V5 娱乐团战匹配（Dungeon 110，仅 5V5，使用免费 3×3 人宠，不计正式评分）。</summary>
        public bool StartPolarity5v5(uint dungeonConfigId = 110u, uint characterPresetId = 0, uint petPresetId = 0)
        {
            if (!CanStart()) return false;
            if (dungeonConfigId == 0)
            {
                ShowStatus("物法易势副本配置无效。");
                return false;
            }
            if (_team.HasTeam)
            {
                ShowStatus("物法易势 5V5 请先离队后单人报名匹配。");
                return false;
            }
            if (!ConsumeLocalQuota(MessageIds.JoinQueue)) return false;
            _battle.JoinQueue(Match.MatchMode.Polarity5V5, dungeonConfigId, 5u, characterPresetId, petPresetId, 0u);
            ShowSoloStatus("正在报名物法易势 5V5…");
            return _battle.Phase != BattlePhase.None;
        }

        /// <summary>发起野外地图灵宠栖息地单人寻宠遇敌（Dungeon 202/203/205，仅限单人 PVE_SOLO；服务端复核当前所在野外地图）。</summary>
        public bool StartWildPetHabitat(uint dungeonConfigId)
        {
            if (dungeonConfigId == 0)
            {
                ShowStatus("野外栖息地副本配置无效。");
                return false;
            }
            // 在 CanStart 之前先拦截组队态，避免队员收到“请由队长发起全队战斗”的误导提示。
            if (_team.HasFreshView && _team.HasTeam)
            {
                ShowStatus("野外寻宠遇敌仅支持单人携宠出发，请先离队。");
                return false;
            }
            if (!CanStart()) return false;
            if (!ConsumeLocalQuota(MessageIds.JoinQueue)) return false;
            _battle.JoinQueue(Match.MatchMode.PveSolo, dungeonConfigId);
            ShowSoloStatus("正在野外寻宠遇敌…");
            return _battle.Phase != BattlePhase.None;
        }

        /// <summary>发起归元城原创 NPC 玩法对局（Dungeon 301–302 演武木桩、311–313 回合残局、321–323 人物奇谈；限单人 PVE_SOLO）。</summary>
        public bool StartNpcEncounter(uint dungeonConfigId, uint characterPresetId = 0, uint petPresetId = 0)
        {
            if (dungeonConfigId == 0)
            {
                ShowStatus("当前演武或奇谈副本配置无效。");
                return false;
            }
            // 在 CanStart 之前先拦截组队态，避免队员收到“请由队长发起全队战斗”的误导提示。
            if (_team.HasFreshView && _team.HasTeam)
            {
                ShowStatus("归元城演武、残局与人物奇谈为单人体验，请先离队或暂离后再试。");
                return false;
            }
            if (!CanStart()) return false;
            if (!ConsumeLocalQuota(MessageIds.JoinQueue)) return false;
            _battle.JoinQueue(Match.MatchMode.PveSolo, dungeonConfigId, 0u, characterPresetId, petPresetId, 0u);
            string label = dungeonConfigId >= 301u && dungeonConfigId <= 309u ? "正在进入演武木桩试招…"
                : dungeonConfigId >= 311u && dungeonConfigId <= 319u ? "正在开启演武回合残局…"
                : "正在进入归元奇谈见闻…";
            ShowSoloStatus(label);
            return _battle.Phase != BattlePhase.None;
        }

        private static bool ContainsSize(IReadOnlyCollection<uint> sizes, uint targetSize)
        {
            foreach (uint size in sizes)
            {
                if (size == targetSize) return true;
            }
            return false;
        }

        /// <summary>按五行试炼志解析出的跳转目标发起匹配（论剑排位需切到排位页选档位，不在此处盲排）。</summary>
        public bool StartFromTrialJump(TrialJumpTarget target)
        {
            if (!target.IsValid)
            {
                ShowStatus("当前试炼入口暂不可跳转。");
                return false;
            }
            switch (target.Kind)
            {
                case TrialJumpKind.BossTrial:
                    return StartBossTrial(target.DungeonConfigId);
                case TrialJumpKind.Polarity5v5:
                    return StartPolarity5v5(target.DungeonConfigId);
                case TrialJumpKind.RankedPvp:
                    ShowStatus("请前往论剑排位界面选择档位报名。");
                    return false;
                default:
                    ShowStatus("当前试炼入口暂不可跳转。");
                    return false;
            }
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
