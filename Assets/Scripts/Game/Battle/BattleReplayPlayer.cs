using System;

namespace MmorpgClient.Game.Battle
{
    /// <summary>
    /// 战报回放纯逻辑控制器(docs/handoff/battle-report-replay-client.md §5):
    ///   - 只播不发,零 Unity / 网络依赖,全部边界在 EditMode 可直接覆盖;
    ///   - 支持从任意第 k 回合(1-based)开始播:以 rounds[k-2].state(k=1 用 start_state)为起点;
    ///   - 支持倍速(1x / 2x / 4x)、暂停/继续、上/下一回合、跳过至终态;
    ///   - 完整处理正常结束、DEADLINE_DRAW(含 rounds 为空 → final_state +「超时平局」)、truncated 截断与配表指纹提示。
    /// </summary>
    public sealed class BattleReplayPlayer
    {
        public static readonly float[] SupportedSpeeds = { 1f, 2f, 4f };
        public const string TableVersionMismatchNotice = "游戏已更新，技能显示可能与当时不同";
        public const string DeadlineDrawLabel = "超时平局";

        public BattleReplay Replay { get; }
        public int RecordedRounds => Replay.Rounds.Count;
        /// <summary>下一个即将播放的回合下标(0-based,范围 0..RecordedRounds)。</summary>
        public int NextRoundIndex { get; private set; }
        /// <summary>当前界面对应的回合序号(0 = 开战初始状态,1..RecordedRounds = 第 k 回合)。</summary>
        public int CurrentRoundNumber { get; private set; }
        public float SpeedScale { get; private set; } = 1f;
        public bool IsPaused { get; private set; }
        public bool IsFinished { get; private set; }
        public bool IsTableFingerprintMismatch { get; }
        public string TableVersionWarning => IsTableFingerprintMismatch ? TableVersionMismatchNotice : string.Empty;

        public BattleReplayPlayer(BattleReplay replay, string currentTableFingerprint = null)
        {
            Replay = replay ?? throw new ArgumentNullException(nameof(replay));
            if (replay.StartState == null)
                throw new ArgumentException("BattleReplay must contain StartState.", nameof(replay));

            IsTableFingerprintMismatch = !string.IsNullOrEmpty(currentTableFingerprint)
                && !string.IsNullOrEmpty(replay.TableFingerprint)
                && !string.Equals(currentTableFingerprint, replay.TableFingerprint, StringComparison.Ordinal);
        }

        /// <summary>返回从第 roundNumber 回合(从 1 数)开始播放所需的起始状态快照(handoff §5)。</summary>
        public BattleStateS2C InitialStateForRound(int roundNumber)
        {
            if (roundNumber <= 1 || RecordedRounds == 0) return Replay.StartState;
            int prevIndex = Math.Min(roundNumber - 2, RecordedRounds - 1);
            return Replay.Rounds[prevIndex]?.State ?? Replay.StartState;
        }

        /// <summary>播完或跳过时应落地的最终状态快照。</summary>
        public BattleStateS2C FinalDisplayState()
        {
            if (Replay.EndReason == eBattleReplayEndReason.BattleReplayEndDeadlineDraw && Replay.FinalState != null)
                return Replay.FinalState;
            if (RecordedRounds > 0 && Replay.Rounds[RecordedRounds - 1]?.State != null)
                return Replay.Rounds[RecordedRounds - 1].State;
            return Replay.StartState;
        }

        /// <summary>
        /// 跳转到第 roundNumber 回合(1-based)之前,返回该回合开打前的全量状态。
        /// 若 rounds 为空(例如 0 回合超时平局),回到 StartState 并等待首次 TryNextRound 落地 final_state。
        /// </summary>
        public BattleStateS2C SeekToRound(int roundNumber)
        {
            IsFinished = false;
            if (RecordedRounds == 0)
            {
                NextRoundIndex = 0;
                CurrentRoundNumber = 0;
                return Replay.StartState;
            }
            int clamped = Math.Max(1, Math.Min(roundNumber, RecordedRounds));
            NextRoundIndex = clamped - 1;
            CurrentRoundNumber = clamped - 1;
            return InitialStateForRound(clamped);
        }

        /// <summary>取出下一回合帧;没有更多回合时置 IsFinished = true 并返回 false。</summary>
        public bool TryNextRound(out TurnResultS2C turn)
        {
            if (IsFinished || NextRoundIndex >= RecordedRounds)
            {
                IsFinished = true;
                CurrentRoundNumber = RecordedRounds;
                turn = null;
                return false;
            }
            turn = Replay.Rounds[NextRoundIndex++];
            CurrentRoundNumber = NextRoundIndex;
            return true;
        }

        /// <summary>跳过剩余回合,直接落地最终状态与结局。</summary>
        public BattleStateS2C SkipToEnd()
        {
            NextRoundIndex = RecordedRounds;
            CurrentRoundNumber = RecordedRounds;
            IsPaused = false;
            IsFinished = true;
            return FinalDisplayState();
        }

        public void SetSpeed(float speed)
        {
            SpeedScale = speed > 0.01f ? speed : 1f;
        }

        public float CycleSpeed()
        {
            for (int i = 0; i < SupportedSpeeds.Length; i++)
            {
                if (Math.Abs(SpeedScale - SupportedSpeeds[i]) < 0.01f)
                {
                    SpeedScale = SupportedSpeeds[(i + 1) % SupportedSpeeds.Length];
                    return SpeedScale;
                }
            }
            SpeedScale = SupportedSpeeds[0];
            return SpeedScale;
        }

        public bool TogglePause()
        {
            IsPaused = !IsPaused;
            return IsPaused;
        }

        public void SetPaused(bool paused) => IsPaused = paused;

        /// <summary>回放视角我方站位:本人在开战名单里就站下方,否则默认 team 0 在下方(与观战同口径)。</summary>
        public uint ResolveViewerTeam(ulong myPlayerId)
        {
            if (myPlayerId != 0 && Replay.StartState != null)
            {
                foreach (var actor in Replay.StartState.Actors)
                {
                    if (actor != null && actor.ActorId == myPlayerId) return actor.TeamIndex;
                }
            }
            return 0;
        }

        /// <summary>回放播完 / 跳过后的结局横幅文案(handoff §5)。</summary>
        public string BuildEndingBanner(ulong myPlayerId = 0)
        {
            string outcomeText = DescribeOutcome(myPlayerId);
            if (Replay.EndReason == eBattleReplayEndReason.BattleReplayEndDeadlineDraw)
            {
                return Replay.Truncated
                    ? $"{DeadlineDrawLabel}（回放只录到第 {RecordedRounds} 回合）"
                    : DeadlineDrawLabel;
            }
            if (Replay.Truncated)
            {
                string totalPart = Replay.TotalRounds > (uint)RecordedRounds
                    ? $"（全战共 {Replay.TotalRounds} 回合）"
                    : string.Empty;
                return $"回放只录到第 {RecordedRounds} 回合{totalPart} · {outcomeText}";
            }
            return outcomeText;
        }

        private string DescribeOutcome(ulong myPlayerId)
        {
            if (Replay.Outcome == eBattleOutcome.BattleOutcomeDraw) return "平局";
            if (Replay.Outcome != eBattleOutcome.BattleOutcomeSideAWin
                && Replay.Outcome != eBattleOutcome.BattleOutcomeSideBWin)
                return "回放结束";

            bool hasSelf = false;
            uint myTeam = 0;
            if (myPlayerId != 0 && Replay.StartState != null)
            {
                foreach (var actor in Replay.StartState.Actors)
                {
                    if (actor != null && actor.ActorId == myPlayerId)
                    {
                        hasSelf = true;
                        myTeam = actor.TeamIndex;
                        break;
                    }
                }
            }
            if (hasSelf)
            {
                bool won = (Replay.Outcome == eBattleOutcome.BattleOutcomeSideAWin && myTeam == 0)
                           || (Replay.Outcome == eBattleOutcome.BattleOutcomeSideBWin && myTeam == 1);
                return won ? "回放结束：胜利" : "回放结束：失败";
            }
            return Replay.Outcome == eBattleOutcome.BattleOutcomeSideAWin
                ? "回放结束：下方队伍获胜"
                : "回放结束：上方队伍获胜";
        }
    }
}
