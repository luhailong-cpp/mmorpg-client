using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 战报回放播放控制器单测(docs/handoff/battle-report-replay-client.md §5、§7):
    /// 覆盖从第 1 回合播完、从中间回合开始、DEADLINE_DRAW(含 rounds 为空)、truncated、跳过、暂停/倍速、配表指纹提示。
    /// </summary>
    public sealed class BattleReplayPlayerTests
    {
        private static BattleStateS2C MakeState(uint round, eBattleOutcome outcome = eBattleOutcome.BattleOutcomeOngoing)
        {
            var state = new BattleStateS2C { BattleId = 5001, RoundIndex = round, Outcome = outcome };
            state.Actors.Add(new BattleActorState { ActorId = 101, TeamIndex = 0, Name = "甲", MaxHealth = 100 });
            state.Actors.Add(new BattleActorState { ActorId = 202, TeamIndex = 1, Name = "乙", MaxHealth = 100 });
            return state;
        }

        private static BattleReplay MakeReplay(int recordedRounds, uint totalRounds = 0,
            eBattleOutcome outcome = eBattleOutcome.BattleOutcomeSideAWin,
            eBattleReplayEndReason endReason = eBattleReplayEndReason.BattleReplayEndFinished,
            bool truncated = false, bool withFinalState = false, string fingerprint = "fp-1")
        {
            var replay = new BattleReplay
            {
                BattleId = 5001,
                MatchMode = (uint)Match.MatchMode._1V1,
                Outcome = outcome,
                EndReason = endReason,
                TotalRounds = totalRounds == 0 ? (uint)recordedRounds : totalRounds,
                Truncated = truncated,
                TableFingerprint = fingerprint,
                StartState = MakeState(1),
            };
            for (int i = 1; i <= recordedRounds; i++)
            {
                bool isLast = i == recordedRounds && endReason == eBattleReplayEndReason.BattleReplayEndFinished && !truncated;
                replay.Rounds.Add(new TurnResultS2C
                {
                    BattleId = 5001,
                    RoundIndex = (uint)i,
                    State = MakeState((uint)i, isLast ? outcome : eBattleOutcome.BattleOutcomeOngoing),
                });
            }
            if (withFinalState)
            {
                replay.FinalState = MakeState((uint)(recordedRounds + 1), outcome);
            }
            return replay;
        }

        [Test]
        public void PlayFromRoundOneToFinish_AdvancesEveryTurnAndEndsWithOutcome()
        {
            var replay = MakeReplay(recordedRounds: 3, outcome: eBattleOutcome.BattleOutcomeSideAWin);
            var player = new BattleReplayPlayer(replay);

            var start = player.SeekToRound(1);
            Assert.AreSame(replay.StartState, start);
            Assert.IsFalse(player.IsFinished);

            var played = new List<uint>();
            while (player.TryNextRound(out var turn))
            {
                played.Add(turn.RoundIndex);
            }

            CollectionAssert.AreEqual(new uint[] { 1, 2, 3 }, played);
            Assert.IsTrue(player.IsFinished);
            Assert.AreSame(replay.Rounds[2].State, player.FinalDisplayState());
            Assert.AreEqual("回放结束：胜利", player.BuildEndingBanner(myPlayerId: 101));
            Assert.AreEqual("回放结束：失败", player.BuildEndingBanner(myPlayerId: 202));
            Assert.AreEqual("回放结束：下方队伍获胜", player.BuildEndingBanner());
        }

        [Test]
        public void StartFromMiddleRound_UsesPreviousRoundStateAndPlaysRemainingTurns()
        {
            var replay = MakeReplay(recordedRounds: 5);
            var player = new BattleReplayPlayer(replay);

            // 从第 3 回合开始播:起始快照必须是 rounds[1].State(第 2 回合结算后),下一回合播第 3 回合
            var initial = player.SeekToRound(3);
            Assert.AreSame(replay.Rounds[1].State, initial);
            Assert.IsTrue(player.TryNextRound(out var first));
            Assert.AreEqual(3u, first.RoundIndex);
            Assert.IsTrue(player.TryNextRound(out var second));
            Assert.AreEqual(4u, second.RoundIndex);
            Assert.IsTrue(player.TryNextRound(out var third));
            Assert.AreEqual(5u, third.RoundIndex);
            Assert.IsFalse(player.TryNextRound(out _));
            Assert.IsTrue(player.IsFinished);
        }

        [Test]
        public void DeadlineDraw_WithEmptyRoundsAndWithRounds_LandsOnFinalState()
        {
            // rounds 为空(开战后到整场期限还没结过一回合)
            var emptyReplay = MakeReplay(recordedRounds: 0, totalRounds: 0,
                outcome: eBattleOutcome.BattleOutcomeDraw,
                endReason: eBattleReplayEndReason.BattleReplayEndDeadlineDraw,
                withFinalState: true);
            var emptyPlayer = new BattleReplayPlayer(emptyReplay);
            Assert.AreSame(emptyReplay.StartState, emptyPlayer.SeekToRound(1));
            Assert.IsFalse(emptyPlayer.TryNextRound(out _));
            Assert.IsTrue(emptyPlayer.IsFinished);
            Assert.AreSame(emptyReplay.FinalState, emptyPlayer.FinalDisplayState());
            Assert.AreEqual(BattleReplayPlayer.DeadlineDrawLabel, emptyPlayer.BuildEndingBanner(101));

            // 有已录回合后触发 DEADLINE_DRAW
            var withRounds = MakeReplay(recordedRounds: 2, totalRounds: 2,
                outcome: eBattleOutcome.BattleOutcomeDraw,
                endReason: eBattleReplayEndReason.BattleReplayEndDeadlineDraw,
                withFinalState: true);
            var player = new BattleReplayPlayer(withRounds);
            Assert.IsTrue(player.TryNextRound(out var r1));
            Assert.AreEqual(1u, r1.RoundIndex);
            Assert.IsTrue(player.TryNextRound(out var r2));
            Assert.AreEqual(2u, r2.RoundIndex);
            Assert.IsFalse(player.TryNextRound(out _));
            Assert.AreSame(withRounds.FinalState, player.FinalDisplayState());
            Assert.AreEqual(BattleReplayPlayer.DeadlineDrawLabel, player.BuildEndingBanner());
        }

        [Test]
        public void TruncatedReplay_ReportsRecordedCountAndActualOutcome()
        {
            var replay = MakeReplay(recordedRounds: 12, totalRounds: 25,
                outcome: eBattleOutcome.BattleOutcomeSideBWin,
                endReason: eBattleReplayEndReason.BattleReplayEndFinished,
                truncated: true);
            var player = new BattleReplayPlayer(replay);

            int played = 0;
            while (player.TryNextRound(out _)) played++;
            Assert.AreEqual(12, played);
            Assert.IsTrue(player.IsFinished);

            string banner = player.BuildEndingBanner(myPlayerId: 202);
            StringAssert.Contains("回放只录到第 12 回合", banner);
            StringAssert.Contains("共 25 回合", banner);
            StringAssert.Contains("胜利", banner);
        }

        [Test]
        public void SkipToEnd_PauseAndSpeedControls_WorkDeterministically()
        {
            var replay = MakeReplay(recordedRounds: 4);
            var player = new BattleReplayPlayer(replay);

            Assert.AreEqual(1f, player.SpeedScale);
            Assert.AreEqual(2f, player.CycleSpeed());
            Assert.AreEqual(4f, player.CycleSpeed());
            Assert.AreEqual(1f, player.CycleSpeed());

            Assert.IsTrue(player.TogglePause());
            Assert.IsTrue(player.IsPaused);
            Assert.IsFalse(player.TogglePause());

            var finalState = player.SkipToEnd();
            Assert.IsTrue(player.IsFinished);
            Assert.IsFalse(player.IsPaused);
            Assert.AreSame(replay.Rounds[3].State, finalState);
            Assert.IsFalse(player.TryNextRound(out _));

            // 跳过后再回跳第 2 回合可重新播
            Assert.AreSame(replay.Rounds[0].State, player.SeekToRound(2));
            Assert.IsFalse(player.IsFinished);
            Assert.IsTrue(player.TryNextRound(out var turn));
            Assert.AreEqual(2u, turn.RoundIndex);
        }

        [Test]
        public void TableFingerprintMismatch_AndViewerTeamResolution()
        {
            var replay = MakeReplay(recordedRounds: 1, fingerprint: "fp-old");

            var mismatched = new BattleReplayPlayer(replay, currentTableFingerprint: "fp-new");
            Assert.IsTrue(mismatched.IsTableFingerprintMismatch);
            Assert.AreEqual(BattleReplayPlayer.TableVersionMismatchNotice, mismatched.TableVersionWarning);

            var matched = new BattleReplayPlayer(replay, currentTableFingerprint: "fp-old");
            Assert.IsFalse(matched.IsTableFingerprintMismatch);
            Assert.IsEmpty(matched.TableVersionWarning);

            var unavailable = new BattleReplayPlayer(replay, currentTableFingerprint: null);
            Assert.IsFalse(unavailable.IsTableFingerprintMismatch);
            Assert.IsEmpty(unavailable.TableVersionWarning);

            Assert.AreEqual(0u, matched.ResolveViewerTeam(101));
            Assert.AreEqual(1u, matched.ResolveViewerTeam(202));
            Assert.AreEqual(0u, matched.ResolveViewerTeam(999));
        }
    }
}
