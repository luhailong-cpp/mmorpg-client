using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.Battle;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 战报复盘分析器纯逻辑单测：列表胜率/连胜统计、胜负/模式筛选保序、
    /// 局内伤害/治疗/护盾/承伤/暴击/击败聚合、MVP 评定与高光关键回合提取。
    /// </summary>
    public sealed class BattleReplayAnalyzerTests
    {
        private const ulong MyId = 1001;
        private const ulong EnemyId = 2002;
        private const ulong PetId = 9001;

        private static Match.BattleReportSummary MakeSummary(ulong id, Match.MatchMode mode, eBattleOutcome outcome, uint myTeam = 0)
        {
            var s = new Match.BattleReportSummary
            {
                BattleId = id,
                Mode = mode,
                Outcome = outcome,
                TotalRounds = 6,
                HasReplay = true,
            };
            s.Members.Add(new Match.BattleReportMember { PlayerId = MyId, TeamIndex = myTeam, Name = "我方", Level = 60, ClassId = 1 });
            s.Members.Add(new Match.BattleReportMember { PlayerId = EnemyId, TeamIndex = myTeam == 0 ? 1u : 0u, Name = "对手", Level = 60, ClassId = 2 });
            return s;
        }

        [Test]
        public void ComputeListStats_CalculatesWinRateAndStreaks()
        {
            // 最新在前：胜、胜、胜、负、平
            var list = new List<Match.BattleReportSummary>
            {
                MakeSummary(5, Match.MatchMode._1V1, eBattleOutcome.BattleOutcomeSideAWin),
                MakeSummary(4, Match.MatchMode.GuildWarPvp, eBattleOutcome.BattleOutcomeSideAWin),
                MakeSummary(3, Match.MatchMode.SeasonTournament, eBattleOutcome.BattleOutcomeSideAWin),
                MakeSummary(2, Match.MatchMode._1V1, eBattleOutcome.BattleOutcomeSideBWin),
                MakeSummary(1, Match.MatchMode.PvpChallenge, eBattleOutcome.BattleOutcomeDraw),
            };

            var stats = BattleReplayAnalyzer.ComputeListStats(list, MyId);
            Assert.AreEqual(5, stats.Total);
            Assert.AreEqual(3, stats.Wins);
            Assert.AreEqual(1, stats.Losses);
            Assert.AreEqual(1, stats.Draws);
            Assert.AreEqual(75, stats.WinRatePercent);
            Assert.AreEqual(3, stats.CurrentStreak);
            Assert.AreEqual(3, stats.BestWinStreak);

            var losing = new List<Match.BattleReportSummary>
            {
                MakeSummary(10, Match.MatchMode._1V1, eBattleOutcome.BattleOutcomeSideBWin),
                MakeSummary(9, Match.MatchMode._1V1, eBattleOutcome.BattleOutcomeSideBWin),
                MakeSummary(8, Match.MatchMode._1V1, eBattleOutcome.BattleOutcomeSideAWin),
            };
            var loseStats = BattleReplayAnalyzer.ComputeListStats(losing, MyId);
            Assert.AreEqual(-2, loseStats.CurrentStreak);
            Assert.AreEqual(1, loseStats.BestWinStreak);
        }

        [Test]
        public void FilterReports_PreservesExactServerOrderAcrossVerdictAndMode()
        {
            var list = new List<Match.BattleReportSummary>
            {
                MakeSummary(900, Match.MatchMode.GuildWarPvp, eBattleOutcome.BattleOutcomeSideAWin),
                MakeSummary(12, Match.MatchMode._1V1, eBattleOutcome.BattleOutcomeSideBWin),
                MakeSummary(500, Match.MatchMode.GuildWarPvp, eBattleOutcome.BattleOutcomeSideBWin),
                MakeSummary(77, Match.MatchMode._1V1, eBattleOutcome.BattleOutcomeSideAWin),
            };

            var wins = BattleReplayAnalyzer.FilterReports(list, MyId, BattleReportVerdictFilter.Win, Match.MatchMode.Unspecified);
            CollectionAssert.AreEqual(new ulong[] { 900, 77 }, wins.Select(r => r.BattleId).ToArray());

            var guildWar = BattleReplayAnalyzer.FilterReports(list, MyId, BattleReportVerdictFilter.All, Match.MatchMode.GuildWarPvp);
            CollectionAssert.AreEqual(new ulong[] { 900, 500 }, guildWar.Select(r => r.BattleId).ToArray());

            var guildWarLose = BattleReplayAnalyzer.FilterReports(list, MyId, BattleReportVerdictFilter.Lose, Match.MatchMode.GuildWarPvp);
            CollectionAssert.AreEqual(new ulong[] { 500 }, guildWarLose.Select(r => r.BattleId).ToArray());
        }

        [Test]
        public void Analyze_AggregatesActorStatsMvpAndKeyHighlights()
        {
            var start = new BattleStateS2C { BattleId = 8080, RoundIndex = 1 };
            start.Actors.Add(new BattleActorState { ActorId = MyId, Name = "破军甲", TeamIndex = 0, ClassId = 1, ActorType = eBattleActorType.BattleActorTypePlayer });
            start.Actors.Add(new BattleActorState { ActorId = PetId, OwnerPlayerId = MyId, Name = "灵狐", TeamIndex = 0, ActorType = eBattleActorType.BattleActorTypePet });
            start.Actors.Add(new BattleActorState { ActorId = EnemyId, Name = "玄霄乙", TeamIndex = 1, ClassId = 2, ActorType = eBattleActorType.BattleActorTypePlayer });

            var r1 = new TurnResultS2C { BattleId = 8080, RoundIndex = 1, State = start.Clone() };
            r1.Events.Add(new BattleEventItem { EventType = eBattleEventType.BattleEventDamage, SourceId = MyId, TargetId = EnemyId, Value = 1200, Absorbed = 300, IsCritical = true });
            r1.Events.Add(new BattleEventItem { EventType = eBattleEventType.BattleEventHeal, SourceId = EnemyId, TargetId = EnemyId, Value = 900 });

            var r2 = new TurnResultS2C { BattleId = 8080, RoundIndex = 2, State = start.Clone() };
            r2.Events.Add(new BattleEventItem { EventType = eBattleEventType.BattleEventShield, SourceId = PetId, TargetId = MyId, Value = 500 });
            r2.Events.Add(new BattleEventItem { EventType = eBattleEventType.BattleEventDamage, SourceId = MyId, TargetId = EnemyId, Value = 2500, IsCritical = true });
            r2.Events.Add(new BattleEventItem { EventType = eBattleEventType.BattleEventDeath, TargetId = EnemyId });

            var replay = new BattleReplay
            {
                BattleId = 8080,
                MatchMode = (uint)Match.MatchMode.GuildWarPvp,
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                EndReason = eBattleReplayEndReason.BattleReplayEndFinished,
                TotalRounds = 2,
                StartState = start,
            };
            replay.Rounds.Add(r1);
            replay.Rounds.Add(r2);

            var analysis = BattleReplayAnalyzer.Analyze(replay);
            Assert.AreEqual(8080UL, analysis.BattleId);
            Assert.AreEqual(MyId, analysis.MvpActorId);
            Assert.AreEqual("破军甲", analysis.MvpName);
            Assert.AreEqual(4000UL, analysis.TeamADamage);
            Assert.AreEqual(500UL, analysis.TeamAHealing);
            Assert.AreEqual(900UL, analysis.TeamBHealing);

            var hero = analysis.Actors.First(a => a.ActorId == MyId);
            Assert.AreEqual(4000UL, hero.DamageDealt);
            Assert.AreEqual(2500UL, hero.PeakHit);
            Assert.AreEqual(2, hero.CritCount);
            Assert.AreEqual(1, hero.Kills);
            Assert.IsFalse(hero.IsPet);

            var pet = analysis.Actors.First(a => a.ActorId == PetId);
            Assert.IsTrue(pet.IsPet);
            Assert.AreEqual(500UL, pet.HealingDone);

            var enemy = analysis.Actors.First(a => a.ActorId == EnemyId);
            Assert.AreEqual(4000UL, enemy.DamageTaken);
            Assert.AreEqual(900UL, enemy.HealingDone);

            // 高光回合：第 1 回合续航、第 2 回合首杀（去重后按回合升序）
            Assert.GreaterOrEqual(analysis.Highlights.Count, 2);
            Assert.AreEqual(1, analysis.Highlights[0].RoundNumber);
            Assert.AreEqual(2, analysis.Highlights[1].RoundNumber);
            Assert.AreEqual(BattleReplayHighlightKind.FirstBlood, analysis.Highlights[1].Kind);
        }

        [Test]
        public void Analyze_HandlesZeroRoundDeadlineDrawGracefully()
        {
            var start = new BattleStateS2C { BattleId = 9090, RoundIndex = 1 };
            start.Actors.Add(new BattleActorState { ActorId = MyId, Name = "甲", TeamIndex = 0 });
            var replay = new BattleReplay
            {
                BattleId = 9090,
                MatchMode = (uint)Match.MatchMode._1V1,
                Outcome = eBattleOutcome.BattleOutcomeDraw,
                EndReason = eBattleReplayEndReason.BattleReplayEndDeadlineDraw,
                StartState = start,
                FinalState = start.Clone(),
            };

            var analysis = BattleReplayAnalyzer.Analyze(replay);
            Assert.AreEqual(0, analysis.RecordedRounds);
            Assert.IsEmpty(analysis.Highlights);
            Assert.AreEqual(0UL, analysis.MvpActorId);
            Assert.AreEqual(1, analysis.Actors.Count);
        }
    }
}
