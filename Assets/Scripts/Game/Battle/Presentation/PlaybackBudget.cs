using System;

namespace MmorpgClient.Game.Battle.Presentation
{
    /// <summary>
    /// 回合演出的时间预算(纯 C#,EditMode 可测)。
    ///
    /// 服务端(battle_room_manager ArmRoundTimer)在广播 TurnResult 之前就把下一回合的
    /// action_deadline_ms 挂上(手动 6s / 全员自动 2s),且不等任何客户端 Ack。客户端若按
    /// 拍时长原速串行播放(5v5 全普攻 ≈ 9s),命令环在整个窗口内都处于播放态,玩家永远拿不到
    /// 手动输入;自动/观战则每回合都被下一回合抢占。因此播放前按
    /// <c>budget = deadline − now − 余量</c> 决定倍率:预算够就原速(或自动战斗基础倍率),
    /// 不够就压缩到 <see cref="MaxSpeed"/> 以内,压缩到上限仍塞不下则直接 Skip(只落终态)。
    /// </summary>
    public static class PlaybackBudget
    {
        /// <summary>手动模式:给玩家留的选择行动余量(秒)。</summary>
        public const float ManualInputReserveSeconds = 2.5f;
        /// <summary>自动战斗/观战:无需输入,只留网络与收尾余量(秒)。</summary>
        public const float PassiveReserveSeconds = 0.3f;
        /// <summary>预算低于此值不值得播,直接 Skip。</summary>
        public const float MinPlayableSeconds = 0.6f;
        /// <summary>压缩倍率上限(再快动作就读不出来了)。</summary>
        public const float MaxSpeed = 6f;

        public struct Decision
        {
            /// <summary>建议播放倍率(≥ 基础倍率)。</summary>
            public float Speed;
            /// <summary>true = 不播表现,直接把剩余拍终态落下。</summary>
            public bool Skip;
            /// <summary>可用预算(秒);无窗口时为 +∞。</summary>
            public float BudgetSeconds;
            /// <summary>true = 服务端没给窗口(旧服务端 / 终局),不受预算约束。</summary>
            public bool Unbounded;
        }

        /// <summary>
        /// 按服务端截止时间决策。deadlineUnixMs==0 或 unbounded 视为无窗口;
        /// passive=true(自动/观战)用小余量。
        /// </summary>
        public static Decision Decide(float planSeconds, ulong deadlineUnixMs, long nowUnixMs, bool passive,
            float baseSpeed, bool unbounded = false)
        {
            if (unbounded || deadlineUnixMs == 0)
            {
                return new Decision
                {
                    Speed = Math.Max(1f, baseSpeed),
                    Skip = false,
                    BudgetSeconds = float.PositiveInfinity,
                    Unbounded = true,
                };
            }
            float reserve = passive ? PassiveReserveSeconds : ManualInputReserveSeconds;
            float budget = ((long)deadlineUnixMs - nowUnixMs) / 1000f - reserve;
            return DecideWithBudget(planSeconds, budget, baseSpeed);
        }

        /// <summary>按已算好的预算(秒)决策。</summary>
        public static Decision DecideWithBudget(float planSeconds, float budgetSeconds, float baseSpeed)
        {
            float speed = Math.Max(1f, baseSpeed);
            var decision = new Decision { Speed = speed, Skip = false, BudgetSeconds = budgetSeconds, Unbounded = false };
            if (planSeconds <= 0f) return decision;

            if (budgetSeconds < MinPlayableSeconds)
            {
                decision.Skip = true;
                decision.Speed = MaxSpeed;
                return decision;
            }

            float needed = planSeconds / budgetSeconds;
            if (needed > MaxSpeed)
            {
                // 压到上限也塞不进窗口:与其被下一回合抢占播一半,不如直接落终态
                decision.Skip = true;
                decision.Speed = MaxSpeed;
                return decision;
            }
            decision.Speed = Math.Max(speed, needed);
            return decision;
        }
    }
}
