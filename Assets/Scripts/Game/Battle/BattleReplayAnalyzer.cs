using System;
using System.Collections.Generic;

namespace MmorpgClient.Game.Battle
{
    public enum BattleReportVerdictFilter : byte
    {
        All = 0,
        Win = 1,
        Lose = 2,
        Draw = 3,
    }

    public enum BattleReplayHighlightKind : byte
    {
        FirstBlood = 1,
        MultiKill = 2,
        PeakBurst = 3,
        ClutchHeal = 4,
        DecidingTurn = 5,
    }

    /// <summary>战报列表页顶部的近 N 场统计摘要（纯客户端聚合，零协议改动）。</summary>
    public readonly struct BattleReportListStats
    {
        public int Total { get; }
        public int Wins { get; }
        public int Losses { get; }
        public int Draws { get; }
        /// <summary>胜率整数百分比（0..100，无决出胜负对局时为 0）。</summary>
        public int WinRatePercent { get; }
        /// <summary>当前连胜（&gt;0）或连败（&lt;0），首局平局或空列表为 0；按服务端返回的最新在前顺序扫描。</summary>
        public int CurrentStreak { get; }
        public int BestWinStreak { get; }

        public BattleReportListStats(int total, int wins, int losses, int draws, int winRatePercent, int currentStreak, int bestWinStreak)
        {
            Total = total;
            Wins = wins;
            Losses = losses;
            Draws = draws;
            WinRatePercent = winRatePercent;
            CurrentStreak = currentStreak;
            BestWinStreak = bestWinStreak;
        }
    }

    /// <summary>单个参战单位（人物或灵宠）在整场回放中的统计数据。</summary>
    public sealed class BattleReplayActorStats
    {
        public ulong ActorId { get; }
        public ulong OwnerPlayerId { get; }
        public uint TeamIndex { get; }
        public string Name { get; set; }
        public uint ClassId { get; set; }
        public bool IsPet { get; set; }
        public ulong DamageDealt { get; set; }
        public ulong DamageTaken { get; set; }
        public ulong HealingDone { get; set; }
        public int CritCount { get; set; }
        public int Kills { get; set; }
        public ulong PeakHit { get; set; }
        public double MvpScore { get; set; }

        public BattleReplayActorStats(ulong actorId, ulong ownerPlayerId, uint teamIndex, string name, uint classId, bool isPet)
        {
            ActorId = actorId;
            OwnerPlayerId = ownerPlayerId;
            TeamIndex = teamIndex;
            Name = string.IsNullOrWhiteSpace(name) ? $"单位{actorId}" : name;
            ClassId = classId;
            IsPet = isPet;
        }
    }

    /// <summary>回放中自动提取的高光关键回合书签（点击直达该回合）。</summary>
    public readonly struct BattleReplayHighlight
    {
        public int RoundNumber { get; }
        public BattleReplayHighlightKind Kind { get; }
        public string Tag { get; }
        public string Label { get; }
        public ulong MetricValue { get; }

        public BattleReplayHighlight(int roundNumber, BattleReplayHighlightKind kind, string tag, string label, ulong metricValue)
        {
            RoundNumber = roundNumber;
            Kind = kind;
            Tag = tag;
            Label = label;
            MetricValue = metricValue;
        }
    }

    /// <summary>单场回放的完整复盘拆解结果（各单位攻防疗数据、MVP 与高光关键回合）。</summary>
    public sealed class BattleReplayAnalysis
    {
        public ulong BattleId { get; }
        public uint MatchMode { get; }
        public eBattleOutcome Outcome { get; }
        public int RecordedRounds { get; }
        public ulong MvpActorId { get; }
        public string MvpName { get; }
        public ulong TeamADamage { get; }
        public ulong TeamBDamage { get; }
        public ulong TeamAHealing { get; }
        public ulong TeamBHealing { get; }
        public ulong MaxActorDamage { get; }
        public ulong MaxActorHealing { get; }
        public IReadOnlyList<BattleReplayActorStats> Actors { get; }
        public IReadOnlyList<BattleReplayHighlight> Highlights { get; }

        public BattleReplayAnalysis(
            ulong battleId,
            uint matchMode,
            eBattleOutcome outcome,
            int recordedRounds,
            ulong mvpActorId,
            string mvpName,
            ulong teamADamage,
            ulong teamBDamage,
            ulong teamAHealing,
            ulong teamBHealing,
            ulong maxActorDamage,
            ulong maxActorHealing,
            IReadOnlyList<BattleReplayActorStats> actors,
            IReadOnlyList<BattleReplayHighlight> highlights)
        {
            BattleId = battleId;
            MatchMode = matchMode;
            Outcome = outcome;
            RecordedRounds = recordedRounds;
            MvpActorId = mvpActorId;
            MvpName = mvpName ?? string.Empty;
            TeamADamage = teamADamage;
            TeamBDamage = teamBDamage;
            TeamAHealing = teamAHealing;
            TeamBHealing = teamBHealing;
            MaxActorDamage = maxActorDamage;
            MaxActorHealing = maxActorHealing;
            Actors = actors ?? Array.Empty<BattleReplayActorStats>();
            Highlights = highlights ?? Array.Empty<BattleReplayHighlight>();
        }
    }

    /// <summary>
    /// 战报复盘纯逻辑分析器（零 Unity / 网络依赖）：
    ///   - 列表页：统计近 100 场胜率/连胜并按胜负/模式筛选（严格保持服务端原序）；
    ///   - 回放页：从 BattleReplay.Rounds 确定性聚合伤害/治疗/承伤/暴击/击杀、评定 MVP 并提取最多 5 个高光关键回合。
    /// </summary>
    public static class BattleReplayAnalyzer
    {
        public const int MaxHighlights = 5;

        public static BattleReportListStats ComputeListStats(IReadOnlyList<Match.BattleReportSummary> reports, ulong myPlayerId)
        {
            if (reports == null || reports.Count == 0)
                return default;

            int total = 0, wins = 0, losses = 0, draws = 0;
            int currentStreak = 0;
            bool streakLocked = false;
            int runWins = 0, bestWinStreak = 0;

            for (int i = 0; i < reports.Count; i++)
            {
                var summary = reports[i];
                if (summary == null || summary.BattleId == 0) continue;
                total++;
                var verdict = BattleReportClient.ResolveVerdict(summary, myPlayerId);
                switch (verdict)
                {
                    case BattleReportVerdict.Win:
                        wins++;
                        runWins++;
                        if (runWins > bestWinStreak) bestWinStreak = runWins;
                        if (!streakLocked)
                        {
                            if (currentStreak >= 0) currentStreak++;
                            else streakLocked = true;
                        }
                        break;
                    case BattleReportVerdict.Lose:
                        losses++;
                        runWins = 0;
                        if (!streakLocked)
                        {
                            if (currentStreak <= 0) currentStreak--;
                            else streakLocked = true;
                        }
                        break;
                    case BattleReportVerdict.Draw:
                        draws++;
                        runWins = 0;
                        streakLocked = true;
                        break;
                    default:
                        runWins = 0;
                        streakLocked = true;
                        break;
                }
            }

            int decided = wins + losses;
            int winRate = decided > 0 ? (int)Math.Round(wins * 100.0 / decided, MidpointRounding.AwayFromZero) : 0;
            return new BattleReportListStats(total, wins, losses, draws, winRate, currentStreak, bestWinStreak);
        }

        public static List<Match.BattleReportSummary> FilterReports(
            IReadOnlyList<Match.BattleReportSummary> reports,
            ulong myPlayerId,
            BattleReportVerdictFilter verdictFilter,
            Match.MatchMode modeFilter)
        {
            var result = new List<Match.BattleReportSummary>();
            if (reports == null || reports.Count == 0) return result;

            for (int i = 0; i < reports.Count; i++)
            {
                var summary = reports[i];
                if (summary == null || summary.BattleId == 0) continue;
                if (modeFilter != Match.MatchMode.Unspecified && summary.Mode != modeFilter)
                    continue;
                if (verdictFilter != BattleReportVerdictFilter.All)
                {
                    var verdict = BattleReportClient.ResolveVerdict(summary, myPlayerId);
                    bool match = verdictFilter switch
                    {
                        BattleReportVerdictFilter.Win => verdict == BattleReportVerdict.Win,
                        BattleReportVerdictFilter.Lose => verdict == BattleReportVerdict.Lose,
                        BattleReportVerdictFilter.Draw => verdict == BattleReportVerdict.Draw,
                        _ => true,
                    };
                    if (!match) continue;
                }
                result.Add(summary);
            }
            return result;
        }

        public static BattleReplayAnalysis Analyze(BattleReplay replay)
        {
            if (replay == null)
                return new BattleReplayAnalysis(0, 0, eBattleOutcome.BattleOutcomeOngoing, 0, 0, string.Empty, 0, 0, 0, 0, 0, 0, null, null);

            var byId = new Dictionary<ulong, BattleReplayActorStats>();
            RegisterActors(byId, replay.StartState);

            int roundCount = replay.Rounds.Count;
            int firstBloodRound = 0;
            int multiKillRound = 0;
            int maxKillsInRound = 0;
            int peakBurstRound = 0;
            ulong peakBurstDamage = 0;
            int clutchHealRound = 0;
            ulong peakRoundHeal = 0;
            bool clutchHadRevive = false;

            var lastDamager = new Dictionary<ulong, ulong>();
            for (int i = 0; i < roundCount; i++)
            {
                var turn = replay.Rounds[i];
                if (turn == null) continue;
                int roundNumber = i + 1;
                RegisterActors(byId, turn.State);
                lastDamager.Clear();

                ulong roundDamage = 0;
                ulong roundHeal = 0;
                int roundDeaths = 0;
                bool roundRevive = false;

                foreach (var ev in turn.Events)
                {
                    if (ev == null) continue;
                    switch (ev.EventType)
                    {
                        case eBattleEventType.BattleEventDamage:
                        case eBattleEventType.BattleEventBuffTick:
                        {
                            ulong amount = ev.Value + ev.Absorbed;
                            if (amount == 0) break;
                            roundDamage += amount;
                            if (ev.SourceId != 0 && byId.TryGetValue(ev.SourceId, out var src))
                            {
                                src.DamageDealt += amount;
                                if (amount > src.PeakHit) src.PeakHit = amount;
                                if (ev.IsCritical) src.CritCount++;
                            }
                            if (ev.TargetId != 0 && byId.TryGetValue(ev.TargetId, out var tgt))
                            {
                                tgt.DamageTaken += amount;
                                if (ev.SourceId != 0 && ev.SourceId != ev.TargetId)
                                    lastDamager[ev.TargetId] = ev.SourceId;
                            }
                            break;
                        }
                        case eBattleEventType.BattleEventHeal:
                        case eBattleEventType.BattleEventShield:
                        {
                            ulong amount = ev.Value;
                            if (amount == 0) break;
                            roundHeal += amount;
                            ulong healerId = ev.SourceId != 0 ? ev.SourceId : ev.TargetId;
                            if (healerId != 0 && byId.TryGetValue(healerId, out var healer))
                            {
                                healer.HealingDone += amount;
                                if (ev.IsCritical) healer.CritCount++;
                            }
                            break;
                        }
                        case eBattleEventType.BattleEventRevive:
                        {
                            roundRevive = true;
                            roundHeal += ev.Value;
                            ulong reviverId = ev.SourceId != 0 ? ev.SourceId : ev.TargetId;
                            if (reviverId != 0 && byId.TryGetValue(reviverId, out var reviver))
                                reviver.HealingDone += ev.Value;
                            break;
                        }
                        case eBattleEventType.BattleEventDeath:
                        {
                            roundDeaths++;
                            ulong killerId = ev.SourceId != 0 && ev.SourceId != ev.TargetId
                                ? ev.SourceId
                                : (ev.TargetId != 0 && lastDamager.TryGetValue(ev.TargetId, out var last) ? last : 0);
                            if (killerId != 0 && byId.TryGetValue(killerId, out var killer))
                                killer.Kills++;
                            break;
                        }
                    }
                }

                if (roundDeaths > 0 && firstBloodRound == 0)
                    firstBloodRound = roundNumber;
                if (roundDeaths >= 2 && roundDeaths > maxKillsInRound)
                {
                    maxKillsInRound = roundDeaths;
                    multiKillRound = roundNumber;
                }
                if (roundDamage > peakBurstDamage)
                {
                    peakBurstDamage = roundDamage;
                    peakBurstRound = roundNumber;
                }
                if ((roundRevive && !clutchHadRevive) || (roundRevive == clutchHadRevive && roundHeal > peakRoundHeal))
                {
                    clutchHadRevive = roundRevive;
                    peakRoundHeal = roundHeal;
                    clutchHealRound = roundNumber;
                }
            }

            RegisterActors(byId, replay.FinalState);

            ulong teamADmg = 0, teamBDmg = 0, teamAHeal = 0, teamBHeal = 0;
            ulong maxDmg = 0, maxHeal = 0;
            int winningTeam = replay.Outcome == eBattleOutcome.BattleOutcomeSideAWin
                ? 0
                : replay.Outcome == eBattleOutcome.BattleOutcomeSideBWin ? 1 : -1;

            var actorList = new List<BattleReplayActorStats>(byId.Values);
            BattleReplayActorStats mvp = null;
            foreach (var a in actorList)
            {
                if (a.TeamIndex == 0) { teamADmg += a.DamageDealt; teamAHeal += a.HealingDone; }
                else { teamBDmg += a.DamageDealt; teamBHeal += a.HealingDone; }
                if (a.DamageDealt > maxDmg) maxDmg = a.DamageDealt;
                if (a.HealingDone > maxHeal) maxHeal = a.HealingDone;

                double score = a.DamageDealt + a.HealingDone * 1.15 + a.DamageTaken * 0.35 + a.Kills * 600.0 + a.CritCount * 80.0;
                if (winningTeam >= 0 && (int)a.TeamIndex == winningTeam) score *= 1.15;
                a.MvpScore = score;

                if (score > 0 && (mvp == null || score > mvp.MvpScore || (Math.Abs(score - mvp.MvpScore) < 0.001 && a.ActorId < mvp.ActorId)))
                    mvp = a;
            }

            // 队内排序：Team 0 在前、Team 1 在后；同队按人物优先再按 MVP 评分降序、ActorId 升序
            actorList.Sort((x, y) =>
            {
                int teamCmp = x.TeamIndex.CompareTo(y.TeamIndex);
                if (teamCmp != 0) return teamCmp;
                int petCmp = x.IsPet.CompareTo(y.IsPet);
                if (petCmp != 0) return petCmp;
                int scoreCmp = y.MvpScore.CompareTo(x.MvpScore);
                if (scoreCmp != 0) return scoreCmp;
                return x.ActorId.CompareTo(y.ActorId);
            });

            var highlights = BuildHighlights(replay, roundCount, firstBloodRound, multiKillRound, maxKillsInRound,
                peakBurstRound, peakBurstDamage, clutchHealRound, peakRoundHeal, clutchHadRevive);

            return new BattleReplayAnalysis(
                replay.BattleId,
                replay.MatchMode,
                replay.Outcome,
                roundCount,
                mvp?.ActorId ?? 0,
                mvp?.Name ?? string.Empty,
                teamADmg,
                teamBDmg,
                teamAHeal,
                teamBHeal,
                maxDmg,
                maxHeal,
                actorList,
                highlights);
        }

        private static List<BattleReplayHighlight> BuildHighlights(
            BattleReplay replay,
            int roundCount,
            int firstBloodRound,
            int multiKillRound,
            int maxKillsInRound,
            int peakBurstRound,
            ulong peakBurstDamage,
            int clutchHealRound,
            ulong peakRoundHeal,
            bool clutchHadRevive)
        {
            var byRound = new Dictionary<int, BattleReplayHighlight>();
            if (roundCount <= 0) return new List<BattleReplayHighlight>();

            void TryPut(int round, BattleReplayHighlightKind kind, string tag, string label, ulong value)
            {
                if (round < 1 || round > roundCount) return;
                if (!byRound.ContainsKey(round))
                    byRound[round] = new BattleReplayHighlight(round, kind, tag, label, value);
            }

            if (firstBloodRound > 0)
                TryPut(firstBloodRound, BattleReplayHighlightKind.FirstBlood, "首杀", $"R{firstBloodRound} 首杀", 1);
            if (multiKillRound > 0)
                TryPut(multiKillRound, BattleReplayHighlightKind.MultiKill, "连斩", $"R{multiKillRound} {maxKillsInRound}连倒", (ulong)maxKillsInRound);
            if (peakBurstRound > 0 && peakBurstDamage > 0)
                TryPut(peakBurstRound, BattleReplayHighlightKind.PeakBurst, "爆发", $"R{peakBurstRound} 爆发 {FormatCompact(peakBurstDamage)}", peakBurstDamage);
            if (clutchHealRound > 0 && (clutchHadRevive || peakRoundHeal > 0))
            {
                string healText = clutchHadRevive ? $"R{clutchHealRound} 复活逆转" : $"R{clutchHealRound} 回春 {FormatCompact(peakRoundHeal)}";
                TryPut(clutchHealRound, BattleReplayHighlightKind.ClutchHeal, "续航", healText, peakRoundHeal);
            }
            if (replay.EndReason != eBattleReplayEndReason.BattleReplayEndDeadlineDraw && !replay.Truncated)
                TryPut(roundCount, BattleReplayHighlightKind.DecidingTurn, "决胜", $"R{roundCount} 决胜回合", 0);

            var sorted = new List<BattleReplayHighlight>(byRound.Values);
            sorted.Sort((a, b) => a.RoundNumber.CompareTo(b.RoundNumber));
            if (sorted.Count > MaxHighlights)
                sorted.RemoveRange(MaxHighlights, sorted.Count - MaxHighlights);
            return sorted;
        }

        private static void RegisterActors(Dictionary<ulong, BattleReplayActorStats> byId, BattleStateS2C state)
        {
            if (state == null) return;
            foreach (var actor in state.Actors)
            {
                if (actor == null || actor.ActorId == 0) continue;
                bool isPet = actor.ActorType == eBattleActorType.BattleActorTypePet || actor.OwnerPlayerId != 0;
                if (!byId.TryGetValue(actor.ActorId, out var stats))
                {
                    byId[actor.ActorId] = new BattleReplayActorStats(
                        actor.ActorId, actor.OwnerPlayerId, actor.TeamIndex, actor.Name, actor.ClassId, isPet);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(actor.Name)) stats.Name = actor.Name;
                    if (actor.ClassId != 0) stats.ClassId = actor.ClassId;
                }
            }
        }

        public static string FormatCompact(ulong value)
        {
            if (value >= 100_000UL) return $"{value / 10000.0:0.#}万";
            return value.ToString();
        }
    }
}
