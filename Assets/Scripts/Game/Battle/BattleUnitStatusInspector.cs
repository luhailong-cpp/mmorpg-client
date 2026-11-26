using System;
using System.Collections.Generic;

namespace MmorpgClient.Game.Battle
{
    /// <summary>单条战斗状态的只读投影（供战斗状态详情弹窗与头顶护盾条直接消费，零 UnityEngine 依赖）。</summary>
    public sealed class BattleUnitBuffDetail
    {
        public ulong BuffInstanceId;
        public uint BuffTableId;
        public uint SourceSkillId;
        public string SourceName;
        public uint RemainRounds;
        public ulong RemainingCapacity;
        public uint RemainingHits;
        public uint RemainingTicks;
        public uint Stacks;
        public bool IsShield => RemainingCapacity > 0;
    }

    /// <summary>单个战斗单位的头顶持久护盾容量、蓄力状态与全部状态明细投影。</summary>
    public sealed class BattleUnitStatusSummary
    {
        public ulong ActorId;
        public ulong TotalShieldCapacity;
        public ulong MaxHealth;
        public uint ChargingSkillId;
        public string ChargingSkillName;
        public bool IsCharging => ChargingSkillId != 0;
        public float ShieldRatio => MaxHealth > 0 ? Math.Min(1f, (float)TotalShieldCapacity / MaxHealth) : 0f;
        public readonly List<BattleUnitBuffDetail> Buffs = new();
    }

    /// <summary>
    /// 战斗单位状态只读解析器：直接消费服务端 BattleActorState.Buffs 与 ChargingSkillId，
    /// 不在 UI 层重复推算护盾容量、剩余次数、回生印余次或蓄力状态。
    /// </summary>
    public static class BattleUnitStatusInspector
    {
        public static BattleUnitStatusSummary Inspect(BattleActorState actor, Func<uint, string> resolveSkillName = null)
        {
            var summary = new BattleUnitStatusSummary();
            if (actor == null) return summary;

            summary.ActorId = actor.ActorId;
            summary.MaxHealth = actor.MaxHealth;
            summary.ChargingSkillId = actor.ChargingSkillId;
            if (actor.ChargingSkillId != 0)
            {
                summary.ChargingSkillName = resolveSkillName?.Invoke(actor.ChargingSkillId) ?? $"技能 #{actor.ChargingSkillId}";
            }

            ulong totalShield = 0;
            foreach (var buff in actor.Buffs)
            {
                if (buff == null) continue;
                if (buff.RemainingCapacity > 0)
                {
                    totalShield = buff.RemainingCapacity > ulong.MaxValue - totalShield
                        ? ulong.MaxValue
                        : totalShield + buff.RemainingCapacity;
                }
                string name = buff.SourceSkillId != 0
                    ? (resolveSkillName?.Invoke(buff.SourceSkillId) ?? $"技能 #{buff.SourceSkillId}")
                    : $"状态 #{buff.BuffTableId}";
                summary.Buffs.Add(new BattleUnitBuffDetail
                {
                    BuffInstanceId = buff.BuffId,
                    BuffTableId = buff.BuffTableId,
                    SourceSkillId = buff.SourceSkillId,
                    SourceName = name,
                    RemainRounds = buff.RemainRounds,
                    RemainingCapacity = buff.RemainingCapacity,
                    RemainingHits = buff.RemainingHits,
                    RemainingTicks = buff.RemainingTicks,
                    Stacks = buff.Stacks > 0 ? buff.Stacks : buff.Layer,
                });
            }
            summary.TotalShieldCapacity = totalShield;
            return summary;
        }
    }
}
