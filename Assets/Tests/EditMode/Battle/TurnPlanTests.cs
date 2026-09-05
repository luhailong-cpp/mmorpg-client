using System.Collections.Generic;
using MmorpgClient.Game.Battle.Presentation;
using MmorpgClient.UI.Ugui.Battle;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// TurnPlan 生成器纯逻辑测试(turn-battle-presentation.md §6):
    /// 合并 / 分组 / 无 group_id 回退 / 时长 / 死亡单拍 / 空事件 / MANA 并入 / 真 proto 构建。
    /// 纯逻辑用例用 <see cref="TurnEventInput"/> 直接构造;proto 用例验证 group_id / hit_index /
    /// target_mana_after / action_order 从生成物直读。
    /// </summary>
    public sealed class TurnPlanTests
    {
        private const ulong A = 11, B = 22, C = 33, D = 44;
        private const float Eps = 1e-4f;

        private static TurnEventInput Ev(int type, ulong src, ulong dst, long value = 0, uint group = 0,
            bool crit = false, ulong hpAfter = 0, uint skill = 0, uint buff = 0, bool success = true)
            => new TurnEventInput
            {
                Type = type, SourceId = src, TargetId = dst, Value = value, GroupId = group,
                IsCritical = crit, HealthAfter = hpAfter, SkillId = skill, BuffId = buff, Success = success,
            };

        // ── 合并 ────────────────────────────────────────────

        [Test]
        public void AttackAndDamage_SameGroup_MergeIntoOneAttackBeat()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Attack, A, B, group: 1),
                Ev(BattleEventCodes.Damage, A, B, 120, group: 1, crit: true, hpAfter: 880),
            });

            Assert.AreEqual(1, plan.Count);
            var beat = plan.Beats[0];
            Assert.AreEqual(BeatKind.Attack, beat.Kind);
            Assert.AreEqual(A, beat.ActorId);
            Assert.AreEqual(1, beat.Targets.Count);
            Assert.AreEqual(B, beat.Targets[0].ActorId);
            Assert.AreEqual(TargetEffect.Damage, beat.Targets[0].Effect);
            Assert.AreEqual(120, beat.Targets[0].Value);
            Assert.IsTrue(beat.Targets[0].IsCrit);
            Assert.AreEqual(880UL, beat.Targets[0].HealthAfter);
            Assert.IsTrue(beat.AnyCrit);
            Assert.AreEqual(TurnPlan.AttackSeconds, beat.DurationSeconds, Eps);
            Assert.IsTrue(plan.UsedGroupIds);
        }

        [Test]
        public void Skill_MultiTargetSameGroup_ParallelInOneCastBeat()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Skill, A, 0, group: 7, skill: 501),
                Ev(BattleEventCodes.Damage, A, B, 50, group: 7, hpAfter: 100),
                Ev(BattleEventCodes.Damage, A, C, 60, group: 7, hpAfter: 90),
                Ev(BattleEventCodes.Miss, A, D, group: 7),
                Ev(BattleEventCodes.BuffAdd, A, B, group: 7, buff: 3),
            });

            Assert.AreEqual(1, plan.Count);
            var beat = plan.Beats[0];
            Assert.AreEqual(BeatKind.Cast, beat.Kind);
            Assert.AreEqual(501u, beat.SkillId);
            Assert.AreEqual(7u, beat.GroupId);
            Assert.AreEqual(4, beat.Targets.Count);
            Assert.AreEqual(TargetEffect.Miss, beat.Targets[2].Effect);
            Assert.AreEqual(D, beat.Targets[2].ActorId);
            Assert.AreEqual(TargetEffect.BuffAdd, beat.Targets[3].Effect);
            Assert.AreEqual(3u, beat.Targets[3].BuffId);
            Assert.AreEqual(TurnPlan.CastSeconds, beat.DurationSeconds, Eps);
        }

        [Test]
        public void DifferentGroup_DamageAfterSkill_BecomesSeparateHitBeat()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Skill, A, 0, group: 1, skill: 9),
                Ev(BattleEventCodes.Damage, A, B, 40, group: 1),
                Ev(BattleEventCodes.Damage, B, A, 10, group: 2), // 反伤:另一组
            });

            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual(BeatKind.Cast, plan.Beats[0].Kind);
            Assert.AreEqual(1, plan.Beats[0].Targets.Count);
            Assert.AreEqual(BeatKind.Hit, plan.Beats[1].Kind);
            Assert.AreEqual(B, plan.Beats[1].ActorId);
            Assert.AreEqual(A, plan.Beats[1].Targets[0].ActorId);
            Assert.AreEqual(TurnPlan.LoneHitSeconds, plan.Beats[1].DurationSeconds, Eps);
        }

        [Test]
        public void GroupedMode_UngroupedDamage_DoesNotMergeIntoOpenBeat()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Attack, A, B, group: 5),
                Ev(BattleEventCodes.Damage, A, B, 10, group: 0), // 分组模式下 0 视为未分组
            });

            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual(BeatKind.Hit, plan.Beats[1].Kind);
        }

        // ── 无 group_id 回退 ───────────────────────────────

        [Test]
        public void NoGroupId_ConsecutiveDamage_MergesIntoPrecedingAttack()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Attack, A, B),
                Ev(BattleEventCodes.Damage, A, B, 30, hpAfter: 70),
                Ev(BattleEventCodes.Attack, B, A),
                Ev(BattleEventCodes.Damage, B, A, 25, hpAfter: 75),
            });

            Assert.IsFalse(plan.UsedGroupIds);
            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual(BeatKind.Attack, plan.Beats[0].Kind);
            Assert.AreEqual(1, plan.Beats[0].Targets.Count);
            Assert.AreEqual(30, plan.Beats[0].Targets[0].Value);
            Assert.AreEqual(BeatKind.Attack, plan.Beats[1].Kind);
            Assert.AreEqual(B, plan.Beats[1].ActorId);
            Assert.AreEqual(A, plan.Beats[1].Targets[0].ActorId);
        }

        [Test]
        public void NoGroupId_MultipleConsecutiveDamage_AllMergeAsMultiHit()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Skill, A, 0, skill: 3),
                Ev(BattleEventCodes.Damage, A, B, 10),
                Ev(BattleEventCodes.Damage, A, C, 11),
                Ev(BattleEventCodes.Damage, A, D, 12),
            });

            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(3, plan.Beats[0].Targets.Count);
        }

        [Test]
        public void NoGroupId_DamageNotAdjacentToAttack_StandsAlone()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Attack, A, B),
                Ev(BattleEventCodes.Defend, C, C),
                Ev(BattleEventCodes.Damage, A, B, 30),
            });

            Assert.AreEqual(3, plan.Count);
            Assert.AreEqual(BeatKind.Attack, plan.Beats[0].Kind);
            Assert.AreEqual(0, plan.Beats[0].Targets.Count);
            Assert.AreEqual(BeatKind.Defend, plan.Beats[1].Kind);
            Assert.AreEqual(BeatKind.Hit, plan.Beats[2].Kind);
        }

        [Test]
        public void NoGroupId_HealFromOtherSource_DoesNotMergeIntoAttack()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Attack, A, B),
                Ev(BattleEventCodes.Heal, C, B, 20), // 别人的治疗,不吞进 A 的攻击拍
            });

            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual(BeatKind.Heal, plan.Beats[1].Kind);
            Assert.AreEqual(TurnPlan.LoneHealSeconds, plan.Beats[1].DurationSeconds, Eps);
        }

        [Test]
        public void NoGroupId_SelfBuffAfterSkill_Merges()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Skill, A, A, skill: 8),
                Ev(BattleEventCodes.BuffAdd, A, A, buff: 15),
            });

            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(BeatKind.Cast, plan.Beats[0].Kind);
            Assert.AreEqual(15u, plan.Beats[0].BuffId);
            Assert.AreEqual(TargetEffect.BuffAdd, plan.Beats[0].Targets[0].Effect);
        }

        // ── 单拍事件与时长 ──────────────────────────────────

        [Test]
        public void Death_OfBeatTarget_IsDeferredAfterTheWholeActionBeat()
        {
            // 引擎逐目标结算:DAMAGE(B) 后立即 DEATH(B),再 DAMAGE(C),全部同 group。
            // 死者是当前拍的目标 → DEATH 挂起,C 仍并入同一攻击拍,Death 拍排在整拍之后。
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Attack, A, B, group: 3),
                Ev(BattleEventCodes.Damage, A, B, 999, group: 3, hpAfter: 0),
                Ev(BattleEventCodes.Death, A, B, group: 3),
                Ev(BattleEventCodes.Damage, A, C, 5, group: 3),
            });

            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual(BeatKind.Attack, plan.Beats[0].Kind);
            Assert.AreEqual(2, plan.Beats[0].Targets.Count);
            Assert.IsTrue(plan.Beats[0].FindTarget(B).Died);
            Assert.IsFalse(plan.Beats[0].FindTarget(C).Died);
            Assert.AreEqual(BeatKind.Death, plan.Beats[1].Kind);
            Assert.AreEqual(B, plan.Beats[1].ActorId);
            Assert.AreEqual(TurnPlan.DeathSeconds, plan.Beats[1].DurationSeconds, Eps);
            Assert.AreEqual(TurnPlan.DeathOverlapSeconds, plan.Beats[1].OverlapNextSeconds, Eps);
        }

        [Test]
        public void Aoe_FirstTargetDies_AllTargetsStayInOneCastBeat_DeathsAfter()
        {
            // 复审场景:SKILL(A,g7) → DAMAGE(B) → DEATH(B) → DAMAGE(C) → DAMAGE(D) → BUFF_ADD(C) → BUFF_ADD(D)
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Skill, A, 0, group: 7, skill: 9),
                Ev(BattleEventCodes.Damage, A, B, 300, group: 7, hpAfter: 0),
                Ev(BattleEventCodes.Death, A, B, group: 7),
                Ev(BattleEventCodes.Damage, A, C, 280, group: 7, hpAfter: 120),
                Ev(BattleEventCodes.Damage, A, D, 290, group: 7, hpAfter: 110),
                Ev(BattleEventCodes.BuffAdd, A, C, group: 7, buff: 21),
                Ev(BattleEventCodes.BuffAdd, A, D, group: 7, buff: 21),
            });

            // 一拍 Cast(B/C/D 同拍飙血 + C/D 上 buff)+ 一拍 Death(B),而不是 6 拍
            Assert.AreEqual(2, plan.Count);
            var cast = plan.Beats[0];
            Assert.AreEqual(BeatKind.Cast, cast.Kind);
            Assert.AreEqual(9u, cast.SkillId);
            Assert.AreEqual(5, cast.Targets.Count);
            Assert.AreEqual(TargetEffect.Damage, cast.Targets[0].Effect);
            Assert.AreEqual(TargetEffect.Damage, cast.Targets[1].Effect);
            Assert.AreEqual(TargetEffect.Damage, cast.Targets[2].Effect);
            Assert.AreEqual(TargetEffect.BuffAdd, cast.Targets[3].Effect);
            Assert.AreEqual(TargetEffect.BuffAdd, cast.Targets[4].Effect);
            Assert.IsTrue(cast.FindTarget(B).Died);
            Assert.AreEqual(BeatKind.Death, plan.Beats[1].Kind);
            Assert.AreEqual(B, plan.Beats[1].ActorId);
            Assert.AreEqual(TurnPlan.CastSeconds + TurnPlan.DeathSeconds, plan.TotalSeconds, Eps);
        }

        [Test]
        public void MultipleDeaths_InOneAction_AreEmittedInOrder_BeforeNextAction()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Skill, A, 0, group: 1, skill: 2),
                Ev(BattleEventCodes.Damage, A, B, 500, group: 1, hpAfter: 0),
                Ev(BattleEventCodes.Death, A, B, group: 1),
                Ev(BattleEventCodes.Damage, A, C, 500, group: 1, hpAfter: 0),
                Ev(BattleEventCodes.Death, A, C, group: 1),
                Ev(BattleEventCodes.Attack, D, A, group: 2),
                Ev(BattleEventCodes.Damage, D, A, 10, group: 2),
            });

            Assert.AreEqual(4, plan.Count);
            Assert.AreEqual(BeatKind.Cast, plan.Beats[0].Kind);
            Assert.AreEqual(BeatKind.Death, plan.Beats[1].Kind);
            Assert.AreEqual(B, plan.Beats[1].ActorId);
            Assert.AreEqual(BeatKind.Death, plan.Beats[2].Kind);
            Assert.AreEqual(C, plan.Beats[2].ActorId);
            Assert.AreEqual(BeatKind.Attack, plan.Beats[3].Kind);
            Assert.AreEqual(D, plan.Beats[3].ActorId);
            Assert.AreEqual(1, plan.Beats[3].Targets.Count);
        }

        [Test]
        public void Death_OfNonTarget_ClosesCurrentBeat()
        {
            // 死者不是当前拍的目标(例如反伤致死的别人):按旧语义立刻成拍并关闭当前拍
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Attack, A, B, group: 3),
                Ev(BattleEventCodes.Damage, A, B, 10, group: 3),
                Ev(BattleEventCodes.Death, 0, C, group: 3),
                Ev(BattleEventCodes.Damage, A, D, 5, group: 3),
            });

            Assert.AreEqual(3, plan.Count);
            Assert.AreEqual(BeatKind.Attack, plan.Beats[0].Kind);
            Assert.AreEqual(BeatKind.Death, plan.Beats[1].Kind);
            Assert.AreEqual(C, plan.Beats[1].ActorId);
            Assert.AreEqual(BeatKind.Hit, plan.Beats[2].Kind);
        }

        [Test]
        public void NoGroupId_DeathOfTarget_IsAlsoDeferred()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Attack, A, B),
                Ev(BattleEventCodes.Damage, A, B, 999, hpAfter: 0),
                Ev(BattleEventCodes.Death, A, B),
                Ev(BattleEventCodes.Damage, A, C, 5),
            });

            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual(BeatKind.Attack, plan.Beats[0].Kind);
            Assert.AreEqual(2, plan.Beats[0].Targets.Count);
            Assert.AreEqual(BeatKind.Death, plan.Beats[1].Kind);
        }

        [Test]
        public void BuffTick_IsTickEffect_AndHealVsDamageIsDecidedByHealthAfter()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.BuffTick, 0, B, 50, buff: 4, hpAfter: 850), // 回血:850 > 当前 800
                Ev(BattleEventCodes.BuffTick, 0, C, 30, buff: 5, hpAfter: 70),  // 掉血:70 < 当前 100
                Ev(BattleEventCodes.BuffTick, 0, D, 30, buff: 5),               // 旧服务端无 health_after:按伤害
            });

            Assert.AreEqual(3, plan.Count);
            var heal = plan.Beats[0].Targets[0];
            Assert.AreEqual(TargetEffect.Tick, heal.Effect);
            Assert.IsTrue(heal.HasHealthAfter);
            Assert.IsTrue(TurnPlan.TickIsHeal(heal, currentHealth: 800));
            Assert.IsFalse(TurnPlan.TickIsHeal(heal, currentHealth: 850));
            var dot = plan.Beats[1].Targets[0];
            Assert.AreEqual(TargetEffect.Tick, dot.Effect);
            Assert.IsFalse(TurnPlan.TickIsHeal(dot, currentHealth: 100));
            var legacy = plan.Beats[2].Targets[0];
            Assert.IsFalse(TurnPlan.TickIsHeal(legacy, currentHealth: 100));
            Assert.IsFalse(TurnPlan.TickIsHeal(null, 0));
        }

        [Test]
        public void UnitActionLengths_FitInsideBeatDurations_SoNextBeatCannotCutTheReturn()
        {
            // 攻击冲刺+回位 0.80s ≤ 0.9s 拍;施法起手+收势 0.75s ≤ 1.4s 拍(全部走 BattleTempo 同倍率缩放)
            Assert.LessOrEqual(BattleUnitView.AttackActionSeconds, TurnPlan.AttackSeconds);
            Assert.LessOrEqual(BattleUnitView.CastActionSeconds, TurnPlan.CastSeconds);
            Assert.Less(BattleUnitView.AttackHitDelaySeconds, BattleUnitView.AttackReturnStartSeconds);
            Assert.Less(BattleUnitView.CastReleaseDelaySeconds, BattleUnitView.CastSettleStartSeconds);
        }

        [Test]
        public void BattleTempo_ScalesSecondsByCurrentSpeed()
        {
            BattleTempo.Speed = 2f;
            try
            {
                Assert.AreEqual(0.4f, BattleTempo.Scale(0.8f), Eps);
                BattleTempo.Speed = 0f; // 钳到下限,不会除零
                Assert.AreEqual(BattleTempo.MinSpeed, BattleTempo.Speed, Eps);
            }
            finally
            {
                BattleTempo.Reset();
            }
            Assert.AreEqual(1f, BattleTempo.Speed, Eps);
        }

        [Test]
        public void BuffTick_Defend_Item_Flee_HaveSpecDurations()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.BuffTick, 0, B, 7, buff: 4, hpAfter: 63),
                Ev(BattleEventCodes.BuffTick, 0, C, 7, buff: 4, hpAfter: 40),
                Ev(BattleEventCodes.Defend, A, A),
                Ev(BattleEventCodes.Item, A, A),
                Ev(BattleEventCodes.Flee, C, 0, success: false),
            });

            Assert.AreEqual(5, plan.Count);
            Assert.AreEqual(BeatKind.BuffTick, plan.Beats[0].Kind);
            Assert.AreEqual(TurnPlan.BuffTickSeconds, plan.Beats[0].DurationSeconds, Eps);
            Assert.AreEqual(4u, plan.Beats[0].BuffId);
            Assert.AreEqual(63UL, plan.Beats[0].Targets[0].HealthAfter);
            Assert.AreEqual(BeatKind.BuffTick, plan.Beats[1].Kind);
            Assert.AreEqual(BeatKind.Defend, plan.Beats[2].Kind);
            Assert.AreEqual(TurnPlan.DefendSeconds, plan.Beats[2].DurationSeconds, Eps);
            Assert.AreEqual(BeatKind.Item, plan.Beats[3].Kind);
            Assert.AreEqual(TurnPlan.ItemSeconds, plan.Beats[3].DurationSeconds, Eps);
            Assert.AreEqual(BeatKind.Flee, plan.Beats[4].Kind);
            Assert.IsFalse(plan.Beats[4].Success);
            Assert.AreEqual(TurnPlan.FleeSeconds, plan.Beats[4].DurationSeconds, Eps);
        }

        [Test]
        public void TotalSeconds_IsSumOfBeatDurations_MinusNonLastOverlaps()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Attack, A, B, group: 1),
                Ev(BattleEventCodes.Damage, A, B, 10, group: 1),
                Ev(BattleEventCodes.Skill, B, 0, group: 2, skill: 1),
                Ev(BattleEventCodes.Damage, B, A, 10, group: 2),
                Ev(BattleEventCodes.Death, B, A, group: 2),
                Ev(BattleEventCodes.BuffTick, 0, C, 1),
            });

            Assert.AreEqual(4, plan.Count);
            Assert.AreEqual(BeatKind.Death, plan.Beats[2].Kind);
            // Death 不是末拍:尾段 0.6s 与 BuffTick 重叠
            float expected = TurnPlan.AttackSeconds + TurnPlan.CastSeconds
                             + (TurnPlan.DeathSeconds - TurnPlan.DeathOverlapSeconds) + TurnPlan.BuffTickSeconds;
            Assert.AreEqual(expected, plan.TotalSeconds, Eps);
            Assert.AreEqual(TurnPlan.DeathSeconds - TurnPlan.DeathOverlapSeconds, TurnPlan.EffectiveSeconds(plan.Beats[2], false), Eps);
            Assert.AreEqual(TurnPlan.DeathSeconds, TurnPlan.EffectiveSeconds(plan.Beats[2], true), Eps);
            Assert.AreEqual(0f, TurnPlan.EffectiveSeconds(null, false), Eps);
        }

        // ── MANA ────────────────────────────────────────────

        [Test]
        public void Mana_CasterCost_MergesIntoCastBeat_WithoutExtraBeat()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Skill, A, 0, group: 4, skill: 2),
                Ev(BattleEventCodes.Mana, A, A, 30, group: 4),
                Ev(BattleEventCodes.Damage, A, B, 80, group: 4),
            });

            Assert.AreEqual(1, plan.Count);
            var beat = plan.Beats[0];
            Assert.IsTrue(beat.HasActorManaAfter);
            Assert.AreEqual(-30, beat.ActorManaDelta); // uint64 value 无符号:自耗按负
            Assert.AreEqual(1, beat.Targets.Count);    // 施法者耗蓝不占目标条目
            Assert.AreEqual(B, beat.Targets[0].ActorId);
        }

        [Test]
        public void Mana_OnTarget_AttachesToExistingTargetEntry()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Skill, A, 0, group: 4),
                Ev(BattleEventCodes.Damage, A, B, 80, group: 4),
                new TurnEventInput { Type = BattleEventCodes.Mana, SourceId = A, TargetId = B, Value = 15, GroupId = 4, ManaAfter = 5, HasManaAfter = true },
            });

            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(1, plan.Beats[0].Targets.Count);
            Assert.IsTrue(plan.Beats[0].Targets[0].HasManaAfter);
            Assert.AreEqual(5UL, plan.Beats[0].Targets[0].ManaAfter);
        }

        [Test]
        public void Mana_WithoutOpenBeat_IsItsOwnShortBeat()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Mana, 0, A, 20),
            });

            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(BeatKind.Mana, plan.Beats[0].Kind);
            Assert.AreEqual(TurnPlan.LoneManaSeconds, plan.Beats[0].DurationSeconds, Eps);
            Assert.AreEqual(TargetEffect.Mana, plan.Beats[0].Targets[0].Effect);
        }

        // ── 空 / 边界 ───────────────────────────────────────

        [Test]
        public void EmptyEvents_ProduceEmptyPlan()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>());
            Assert.AreEqual(0, plan.Count);
            Assert.AreEqual(0f, plan.TotalSeconds, Eps);

            var nullPlan = TurnPlan.Build((IReadOnlyList<TurnEventInput>)null);
            Assert.AreEqual(0, nullPlan.Count);

            var protoPlan = TurnPlan.Build((TurnResultS2C)null, 0);
            Assert.AreEqual(0, protoPlan.Count);
            Assert.AreEqual(0f, protoPlan.TotalSeconds, Eps);
        }

        [Test]
        public void UnknownEventType_IsIgnored_AndDoesNotBreakAdjacency()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Attack, A, B),
                Ev(99, A, B),
                Ev(BattleEventCodes.Damage, A, B, 5),
            });

            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(1, plan.Beats[0].Targets.Count);
        }

        [Test]
        public void ActorIsMine_FollowsTeamLookup()
        {
            var plan = TurnPlan.Build(new List<TurnEventInput>
            {
                Ev(BattleEventCodes.Attack, A, B, group: 1),
                Ev(BattleEventCodes.Attack, B, A, group: 2),
            }, id => id == A);

            Assert.IsTrue(plan.Beats[0].ActorIsMine);
            Assert.IsFalse(plan.Beats[1].ActorIsMine);
        }

        // ── 真 proto 构建 ───────────────────────────────────

        [Test]
        public void BuildFromProto_WithGroupIds_ActionOrder_AndManaAfter()
        {
            var result = new TurnResultS2C { BattleId = 1, RoundIndex = 2 };
            result.ActionOrder.Add(B);
            result.ActionOrder.Add(A);
            result.Events.Add(new BattleEventItem { EventType = eBattleEventType.BattleEventSkill, SourceId = B, SkillTableId = 77, GroupId = 1 });
            result.Events.Add(new BattleEventItem { EventType = eBattleEventType.BattleEventMana, SourceId = B, TargetId = B, Value = 12, TargetManaAfter = 88, GroupId = 1 });
            result.Events.Add(new BattleEventItem { EventType = eBattleEventType.BattleEventDamage, SourceId = B, TargetId = A, Value = 30, TargetHealthAfter = 70, GroupId = 1, HitIndex = 0 });
            result.Events.Add(new BattleEventItem { EventType = eBattleEventType.BattleEventDamage, SourceId = B, TargetId = A, Value = 31, TargetHealthAfter = 39, GroupId = 1, HitIndex = 1 });
            result.Events.Add(new BattleEventItem { EventType = eBattleEventType.BattleEventMiss, SourceId = B, TargetId = C, GroupId = 1 });
            result.Events.Add(new BattleEventItem { EventType = eBattleEventType.BattleEventAttack, SourceId = A, TargetId = B, GroupId = 2 });
            result.Events.Add(new BattleEventItem { EventType = eBattleEventType.BattleEventDamage, SourceId = A, TargetId = B, Value = 5, TargetHealthAfter = 95, GroupId = 2 });
            result.State = new BattleStateS2C { BattleId = 1, RoundIndex = 2 };
            result.State.Actors.Add(new BattleActorState { ActorId = A, TeamIndex = 0 });
            result.State.Actors.Add(new BattleActorState { ActorId = B, TeamIndex = 1 });
            result.State.Actors.Add(new BattleActorState { ActorId = C, TeamIndex = 0 });

            var plan = TurnPlan.Build(result, myTeam: 0);

            Assert.IsTrue(plan.UsedGroupIds);
            CollectionAssert.AreEqual(new List<ulong> { B, A }, plan.ActionOrder);
            Assert.AreEqual(2, plan.Count);

            var cast = plan.Beats[0];
            Assert.AreEqual(BeatKind.Cast, cast.Kind);
            Assert.AreEqual(77u, cast.SkillId);
            Assert.IsFalse(cast.ActorIsMine);
            Assert.IsTrue(cast.HasActorManaAfter);
            Assert.AreEqual(88UL, cast.ActorManaAfter);
            Assert.AreEqual(-12, cast.ActorManaDelta);
            Assert.AreEqual(3, cast.Targets.Count); // 两段命中 A + 闪避 C;施法者耗蓝不占条目
            Assert.AreEqual(0, cast.Targets[0].HitIndex);
            Assert.AreEqual(1, cast.Targets[1].HitIndex);
            Assert.AreEqual(39UL, cast.Targets[1].HealthAfter);
            Assert.IsFalse(cast.Targets[0].HasManaAfter); // 非 MANA 事件且 target_mana_after=0 → 不带
            Assert.AreEqual(TargetEffect.Miss, cast.Targets[2].Effect);

            Assert.AreEqual(BeatKind.Attack, plan.Beats[1].Kind);
            Assert.IsTrue(plan.Beats[1].ActorIsMine);
            Assert.AreEqual(TurnPlan.CastSeconds + TurnPlan.AttackSeconds, plan.TotalSeconds, Eps);
        }

        [Test]
        public void BuildFromProto_NoGroupIds_UsesFallbackMerging()
        {
            var result = new TurnResultS2C { BattleId = 1, RoundIndex = 3 };
            result.Events.Add(new BattleEventItem { EventType = eBattleEventType.BattleEventAttack, SourceId = A, TargetId = B });
            result.Events.Add(new BattleEventItem
            {
                EventType = eBattleEventType.BattleEventDamage, SourceId = A, TargetId = B,
                Value = 42, IsCritical = true, TargetHealthAfter = 58,
            });
            result.Events.Add(new BattleEventItem { EventType = eBattleEventType.BattleEventDeath, SourceId = A, TargetId = B });
            result.State = new BattleStateS2C { BattleId = 1, RoundIndex = 3 };
            result.State.Actors.Add(new BattleActorState { ActorId = A, TeamIndex = 0 });
            result.State.Actors.Add(new BattleActorState { ActorId = B, TeamIndex = 1 });

            var plan = TurnPlan.Build(result, myTeam: 0);

            Assert.AreEqual(3u, plan.RoundIndex);
            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual(BeatKind.Attack, plan.Beats[0].Kind);
            Assert.IsTrue(plan.Beats[0].ActorIsMine);
            Assert.AreEqual(1, plan.Beats[0].Targets.Count);
            Assert.AreEqual(42, plan.Beats[0].Targets[0].Value);
            Assert.IsTrue(plan.Beats[0].Targets[0].IsCrit);
            Assert.AreEqual(58UL, plan.Beats[0].Targets[0].HealthAfter);
            Assert.AreEqual(BeatKind.Death, plan.Beats[1].Kind);
            Assert.AreEqual(B, plan.Beats[1].ActorId);
            Assert.IsFalse(plan.Beats[1].ActorIsMine);
            // 旧服务端:group_id 全 0、action_order 为空
            Assert.IsFalse(plan.UsedGroupIds);
            Assert.AreEqual(0, plan.ActionOrder.Count);
        }
    }
}
