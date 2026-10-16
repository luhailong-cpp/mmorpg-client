using MmorpgClient.Game.Battle;

namespace MmorpgClient.Game.Team
{
    /// <summary>
    /// 队伍补位匹配:把「队伍视图里的匹配态」与 BattleClient 的排队态对齐
    /// (服务端设计 docs/design/team-backfill-matching.md §5)。每帧调一次 <see cref="Step"/>。
    ///
    /// 「我在不在排」有两个来源:
    ///   - 队伍视图的匹配态(随 GetMyTeam / 推送到达):只是提示。它不带版本号,可能迟到、乱序、丢失,
    ///     服务端读不到排队票据时还会如实下发成「没在排」;
    ///   - 排队票据(BattleClient 进入排队态后每 3 秒问一次 GetQueueStatus;开战推送人人必达):权威。
    ///
    /// 规则只有三条:
    ///   1. 采纳:队伍视图说在排、BattleClient 空闲 → 让 BattleClient 进入「被带入的排队态」(不发 JoinQueue)。
    ///      之后排队面板、轮询、取消、凑满开战全部沿用单人排队的实现。
    ///   2. 结束只认票据:BattleClient 离开了被带入的排队态(轮询到已不在排 / 已开战;点取消不算,
    ///      取消的回包分不出「已退出」和「太迟」,BattleClient 会先向服务端求证)→ 这一次排队
    ///      到此为止,通知 TeamClient 把它记为已结束(<see cref="TeamClient.ConsumeBackfillQueue"/>)。
    ///      此后同一次排队的旧视图既不显示成「匹配中」,也不会被再次采纳 —— 没有这一条,战斗一打完,
    ///      停在「匹配中」的旧视图会把人立刻带回排队态。
    ///      「已开战 = 结束」由服务端保证:排队中进了任何别的战斗(例如应战切磋),服务端都会立即撤掉排队。
    ///      万一服务端没撤成:规则 2 是由开战(而不是票据)触发的,BattleClient 回到空闲后向服务端探测一次
    ///      (<see cref="BattleClient.ProbeQueueStatus"/>),仍在排(Queued / Matched)就撤销这个记录、重拉视图,
    ///      由规则 1 重新采纳;Ready(刚打完的那一局留下的)按已结束处理。队长、队员都适用。
    ///   3. 视图说没在排、而 BattleClient 还在被带入的排队态 → 不直接改相位(那份视图可能是迟到的或读票失败的),
    ///      只是立刻补问一次服务端,由 GetQueueStatus 的结果决定去留;同时补拉一次队伍视图(每次采纳至多一次)。
    ///
    /// 纯逻辑,不依赖 Unity;线程模型同两个客户端(主线程)。
    /// </summary>
    public sealed class TeamBackfillQueueSync
    {
        private bool _adopted;          // 上一帧 BattleClient 是否处在被带入的排队(含凑满后的准备阶段)
        private ulong _teamId;          // 被采纳的那一次排队的身份:队伍 + 入队时刻
        private ulong _queuedAtMs;
        private ulong _seenSerial;      // 规则 3 用:同一份视图只触发一次补问
        private bool _mismatchRefreshed; // 规则 3 用:每次采纳至多补拉一次队伍视图
        private bool _probePending;      // 规则 2 由开战触发:回到空闲后向服务端核实一次(见类注释)
        private ulong _probeTeamId;
        private ulong _probeQueuedAtMs;

        /// <summary>离开游戏 / 换号时调用:忘掉在途的采纳(不触发规则 2)。</summary>
        public void Reset()
        {
            _adopted = false;
            _teamId = 0;
            _queuedAtMs = 0;
            _mismatchRefreshed = false;
            _probePending = false;
        }

        public void Step(TeamClient team, BattleClient battle)
        {
            if (team == null || battle == null) { Reset(); return; }

            bool adopted = battle.QueueAdopted;
            if (_adopted && !adopted)
            {
                team.ConsumeBackfillQueue(_teamId, _queuedAtMs); // 规则 2
                if (!battle.AdoptedQueueEndedByTicket && _teamId != 0 && _queuedAtMs != 0)
                {
                    _probePending = true;
                    _probeTeamId = _teamId;
                    _probeQueuedAtMs = _queuedAtMs;
                }
            }
            _adopted = adopted;

            // 规则 2 的核实:只在空闲时发(战斗中、自己又排上了都不发),发出即算数 —— 失败就按已结束处理,不重试。
            if (_probePending && battle.Phase == BattlePhase.None)
            {
                ulong probeTeam = _probeTeamId, probeQueuedAt = _probeQueuedAtMs;
                if (battle.ProbeQueueStatus(state =>
                    {
                        if (state == Match.QueueState.Queued || state == Match.QueueState.Matched)
                            team.ReviveBackfillQueue(probeTeam, probeQueuedAt);
                    }))
                    _probePending = false;
            }

            bool hasView = team.HasView;
            bool queued = hasView && team.MatchQueued;
            if (queued && battle.Phase == BattlePhase.None)
            {
                // 规则 1。先记身份再采纳:AdoptQueue 会同步触发相位事件。
                var snapshot = team.Snapshot;
                _teamId = snapshot.TeamId;
                _queuedAtMs = snapshot.MatchQueuedAtMs;
                _adopted = battle.AdoptQueue(QueueHint(snapshot.MatchTargetSize));
                _mismatchRefreshed = false;
            }
            else if (!queued && hasView && adopted && battle.Phase == BattlePhase.Queued
                     && team.ViewSerial != _seenSerial)
            {
                battle.PollQueueNow(); // 规则 3
                // 若服务端说仍在排,那份视图就是错的:顺手补拉一次,让面板文案归位。每次采纳只拉一次 ——
                // 服务端读不到票据时每份新视图都会是「没在排」,不限次就成了拉取循环。
                if (!_mismatchRefreshed)
                {
                    _mismatchRefreshed = true;
                    team.RequestRefresh();
                }
            }
            _seenSerial = team.ViewSerial;
        }

        /// <summary>被带入排队时排队面板上显示的说明。</summary>
        public static string QueueHint(uint targetSize) =>
            $"队伍匹配中（目标 {targetSize} 人）：凑满后全队一同进入战斗。任何一名队员取消，全队都会退出匹配。";
    }
}
