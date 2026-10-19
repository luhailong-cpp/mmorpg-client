using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle.Presentation;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 装备战斗属性带来的新事件形态(连击 / 反震 / 反击 / 抵抗)喂给分拍逻辑后的「预期演出」
    /// (服务器仓 equipment-attributes.md §4.5、§10.3;客户端 Docs/EquipmentUI-20261007.md「战斗演出兼容性」)。
    ///
    /// 事件流照服务端 turn_battle_engine.cpp 实际产出的字段构造(ExecuteAttack / ResolveBasicAttackHit /
    /// TryReflectDamage / TryCounterAttack / AddBuffToActor),用真 proto(含 hit_kind、事件类型 15)
    /// 序列化再解析一遍,然后走 <see cref="TurnPlan.Build(TurnResultS2C, uint)"/>。
    ///
    /// 这里钉的是分拍结果。拍怎么演在 BattlePresenter(要建 Unity 对象,本文件不碰),下面三个小工具
    /// 只是照它的口径做的镜像,不是调用它:
    ///  - <see cref="SegmentsOf"/>:同一单位在一拍里第 n 次出现 = 第 n 段(ApplyOutcomes 按段错拍);
    ///  - <see cref="LungeTarget"/>:出手者冲向第一条非 MANA 条目的单位(FirstTargetView);
    ///  - <see cref="HealthAfterPlayback"/>:逐拍逐条目把 target_health_after 落到条目自己的单位(HandleBeatEnd)。
    /// </summary>
    public sealed class EquipCombatEventsPlanTests
    {
        // A、C 是我方(team 0),B、D、E 是敌方(team 1)
        private const ulong A = 11, B = 22, C = 33, D = 44, E = 55;
        private const float Eps = 1e-4f;

        /// <summary>BATTLE_EVENT_RESIST。BattleEventCodes 里没有这一项:分拍逻辑不认识它,走 default 分支。</summary>
        private const int ResistCode = 15;

        private const eBattleHitKind Normal = eBattleHitKind.BattleHitNormal;
        private const eBattleHitKind Combo = eBattleHitKind.BattleHitCombo;
        private const eBattleHitKind Counter = eBattleHitKind.BattleHitCounter;
        private const eBattleHitKind Reflect = eBattleHitKind.BattleHitReflect;

        /// <summary>每个单位一个不同的法力值:引擎给 DAMAGE / MISS 填的是「target 的」法力,用它能看出条目挂在了谁身上。</summary>
        private static ulong ManaOf(ulong actorId) => 300UL + actorId;

        /// <summary>
        /// 照引擎 AppendEvent 的口径攒一回合事件:每条事件都带当前 group_id / hit_index;
        /// <see cref="BeginGroup"/> 对应 BeginEventGroup(group_id + 1、hit_index 归 0)。
        /// </summary>
        private sealed class Round
        {
            public readonly TurnResultS2C Result = new TurnResultS2C { BattleId = 1, RoundIndex = 3 };
            private uint _group;
            private uint _hitIndex;

            public Round(params ulong[] actionOrder)
            {
                Result.ActionOrder.AddRange(actionOrder);
                Result.State = new BattleStateS2C { BattleId = 1, RoundIndex = 3 };
                foreach (ulong mine in new[] { A, C }) Result.State.Actors.Add(new BattleActorState { ActorId = mine, TeamIndex = 0 });
                foreach (ulong enemy in new[] { B, D, E }) Result.State.Actors.Add(new BattleActorState { ActorId = enemy, TeamIndex = 1 });
            }

            public Round BeginGroup()
            {
                _group++;
                _hitIndex = 0;
                return this;
            }

            public Round HitIndex(uint value)
            {
                _hitIndex = value;
                return this;
            }

            private BattleEventItem Append(eBattleEventType type, ulong source, ulong target)
            {
                var ev = new BattleEventItem
                {
                    EventType = type, SourceId = source, TargetId = target, GroupId = _group, HitIndex = _hitIndex,
                };
                Result.Events.Add(ev);
                return ev;
            }

            /// <summary>ATTACK。反击那一条带 hit_kind = COUNTER,其余为 0。</summary>
            public Round Attack(ulong source, ulong target, eBattleHitKind kind = Normal)
            {
                Append(eBattleEventType.BattleEventAttack, source, target).HitKind = kind;
                return this;
            }

            /// <summary>普攻一段的 DAMAGE(ResolveBasicAttackHit):target_health_after / target_mana_after 都是 target 的。</summary>
            public Round Damage(ulong source, ulong target, ulong value, ulong healthAfter,
                eBattleHitKind kind = Normal, bool crit = false, uint skill = 0)
            {
                var ev = Append(eBattleEventType.BattleEventDamage, source, target);
                ev.Value = value;
                ev.IsCritical = crit;
                ev.TargetHealthAfter = healthAfter;
                ev.TargetManaAfter = ManaOf(target);
                ev.HitKind = kind;
                ev.SkillTableId = skill;
                return this;
            }

            /// <summary>
            /// 反震(TryReflectDamage):留在出手者那一组里的 DAMAGE,source = 受击者、target = 出手者,
            /// target_health_after 是出手者的;沿用当前 hit_index(连击段引发的反震带 1)。
            /// </summary>
            public Round ReflectBack(ulong defender, ulong attacker, ulong value, ulong attackerHealthAfter)
                => Damage(defender, attacker, value, attackerHealthAfter, Reflect);

            /// <summary>MISS(一期命中率 100% 不会出现;二期接命中表后连击段 / 反击段也可能落空)。</summary>
            public Round Miss(ulong source, ulong target, ulong targetHealth, eBattleHitKind kind = Normal)
            {
                var ev = Append(eBattleEventType.BattleEventMiss, source, target);
                ev.TargetHealthAfter = targetHealth;
                ev.TargetManaAfter = ManaOf(target);
                ev.HitKind = kind;
                return this;
            }

            /// <summary>DEATH(HandleDeath):source = target = 死者,沿用当前 group_id / hit_index。</summary>
            public Round Death(ulong who)
            {
                Append(eBattleEventType.BattleEventDeath, who, who);
                return this;
            }

            public Round Skill(ulong source, ulong primaryTarget, uint skill)
            {
                Append(eBattleEventType.BattleEventSkill, source, primaryTarget).SkillTableId = skill;
                return this;
            }

            /// <summary>耗蓝(ConsumeSkillMana):source = target = 施法者,value = 实际消耗量。</summary>
            public Round ManaCost(ulong caster, uint skill, ulong cost, ulong manaAfter)
            {
                var ev = Append(eBattleEventType.BattleEventMana, caster, caster);
                ev.SkillTableId = skill;
                ev.Value = cost;
                ev.TargetManaAfter = manaAfter;
                return this;
            }

            public Round BuffAdd(ulong caster, ulong target, uint buff)
            {
                var ev = Append(eBattleEventType.BattleEventBuffAdd, caster, target);
                ev.BuffTableId = buff;
                ev.Value = 1; // 当前叠层数
                return this;
            }

            /// <summary>抵抗(AddBuffToActor):source = 施加者,target = 抵抗者,buff_table_id = 被抵抗的 buff;没有对应的 BUFF_ADD。</summary>
            public Round Resist(ulong caster, ulong target, uint buff)
            {
                Append(eBattleEventType.BattleEventResist, caster, target).BuffTableId = buff;
                return this;
            }
        }

        /// <summary>过一遍线格式再分拍:hit_kind 与事件类型 15 都按真实收包路径解析。</summary>
        private static TurnPlan Build(Round round)
        {
            var parsed = TurnResultS2C.Parser.ParseFrom(round.Result.ToByteArray());
            return TurnPlan.Build(parsed, myTeam: 0);
        }

        private static List<int> SegmentsOf(Beat beat)
        {
            var seen = new Dictionary<ulong, int>();
            var segments = new List<int>();
            foreach (var target in beat.Targets)
            {
                seen.TryGetValue(target.ActorId, out int segment);
                seen[target.ActorId] = segment + 1;
                segments.Add(segment);
            }
            return segments;
        }

        private static ulong LungeTarget(Beat beat)
        {
            foreach (var target in beat.Targets)
            {
                if (target.Effect != TargetEffect.Mana) return target.ActorId;
            }
            return 0;
        }

        private static Dictionary<ulong, ulong> HealthAfterPlayback(TurnPlan plan)
        {
            var health = new Dictionary<ulong, ulong>();
            foreach (var beat in plan.Beats)
            {
                foreach (var target in beat.Targets)
                {
                    if (target.HasHealthAfter) health[target.ActorId] = target.HealthAfter;
                }
            }
            return health;
        }

        /// <summary>拍序列的可比较摘要(拍类型 / 出手者 / 组号 / 逐条目的单位、效果、数值、血量、段号)。</summary>
        private static string Shape(TurnPlan plan)
        {
            var beats = new List<string>();
            foreach (var beat in plan.Beats)
            {
                var targets = new List<string>();
                foreach (var t in beat.Targets)
                    targets.Add($"{t.ActorId}:{t.Effect}:{t.Value}:{t.HealthAfter}:{t.HitIndex}:{(t.IsCrit ? "crit" : "-")}:{(t.Died ? "died" : "-")}");
                beats.Add($"{beat.Kind}#{beat.ActorId}g{beat.GroupId}[{string.Join(",", targets)}]");
            }
            return string.Join(" | ", beats);
        }

        private static List<BeatKind> KindsOf(TurnPlan plan)
        {
            var kinds = new List<BeatKind>();
            foreach (var beat in plan.Beats) kinds.Add(beat.Kind);
            return kinds;
        }

        private static List<ulong> ActorsOf(TurnPlan plan)
        {
            var actors = new List<ulong>();
            foreach (var beat in plan.Beats) actors.Add(beat.ActorId);
            return actors;
        }

        private static void AssertEntry(BeatTarget entry, ulong actor, TargetEffect effect, long value, ulong healthAfter, int hitIndex)
        {
            Assert.AreEqual(actor, entry.ActorId);
            Assert.AreEqual(effect, entry.Effect);
            Assert.AreEqual(value, entry.Value);
            Assert.AreEqual(healthAfter, entry.HealthAfter);
            Assert.AreEqual(hitIndex, entry.HitIndex);
        }

        // ── 连击:同组同目标第二条 DAMAGE(hit_index = 1、hit_kind = COMBO),只有 1 条 ATTACK ──

        [Test]
        public void Combo_SecondDamageOnTheSameTarget_IsASecondSegmentOfTheSameAttackBeat()
        {
            var plan = Build(new Round(A, B)
                .BeginGroup()
                .Attack(A, B)
                .Damage(A, B, 100, 900)
                .HitIndex(1).Damage(A, B, 50, 850, Combo));

            // 预期演出:一拍,A 冲一次,B 身上先后飘两段数字(第二段错后一个 MultiHitStagger),血条 900 → 850
            Assert.AreEqual(1, plan.Count);
            var beat = plan.Beats[0];
            Assert.AreEqual(BeatKind.Attack, beat.Kind);
            Assert.AreEqual(A, beat.ActorId);
            Assert.AreEqual(1u, beat.GroupId);
            Assert.AreEqual(2, beat.Targets.Count);
            AssertEntry(beat.Targets[0], B, TargetEffect.Damage, 100, 900, 0);
            AssertEntry(beat.Targets[1], B, TargetEffect.Damage, 50, 850, 1);
            CollectionAssert.AreEqual(new[] { 0, 1 }, SegmentsOf(beat));
            Assert.AreEqual(B, LungeTarget(beat));
            Assert.AreEqual(850UL, HealthAfterPlayback(plan)[B]);
            Assert.AreEqual(TurnPlan.AttackSeconds, plan.TotalSeconds, Eps);
        }

        [Test]
        public void Combo_KillingBlowOnTheSecondSegment_DefersDeathUntilAfterTheWholeAttackBeat()
        {
            var plan = Build(new Round(A, B)
                .BeginGroup()
                .Attack(A, B)
                .Damage(A, B, 100, 40)
                .HitIndex(1).Damage(A, B, 40, 0, Combo)
                .Death(B)); // DEATH 沿用 hit_index = 1

            CollectionAssert.AreEqual(new[] { BeatKind.Attack, BeatKind.Death }, KindsOf(plan));
            var attack = plan.Beats[0];
            Assert.AreEqual(2, attack.Targets.Count); // 两段都留在攻击拍里,没有被 DEATH 拆开
            Assert.IsTrue(attack.FindTarget(B).Died);
            Assert.AreEqual(B, plan.Beats[1].ActorId);
            Assert.AreEqual(0UL, HealthAfterPlayback(plan)[B]);
        }

        // ── 反震:出手者那一组里 source = 受击者、target = 出手者的 DAMAGE(hit_kind = REFLECT) ──

        [Test]
        public void Reflect_IsOneMoreDamageEntryOnTheAttacker_InsideItsOwnAttackBeat()
        {
            var plan = Build(new Round(A, B)
                .BeginGroup()
                .Attack(A, B)
                .Damage(A, B, 100, 900)
                .ReflectBack(B, A, 50, 950));

            // 预期演出:仍是 A 的那一拍(受击者 B 不做出手动作、不另起拍);A 照常冲向 B,
            // 命中时 B 飘 -100,A 自己头上飘 -50,A 的血条走到 950(反震事件的 target_health_after 是出手者的)
            Assert.AreEqual(1, plan.Count);
            var beat = plan.Beats[0];
            Assert.AreEqual(BeatKind.Attack, beat.Kind);
            Assert.AreEqual(A, beat.ActorId);
            Assert.AreEqual(2, beat.Targets.Count);
            AssertEntry(beat.Targets[0], B, TargetEffect.Damage, 100, 900, 0);
            AssertEntry(beat.Targets[1], A, TargetEffect.Damage, 50, 950, 0);
            Assert.AreEqual(beat.ActorId, beat.Targets[1].ActorId); // 反震条目挂在出手者自己身上
            Assert.IsTrue(beat.Targets[1].HasHealthAfter);
            Assert.AreEqual(ManaOf(A), beat.Targets[1].ManaAfter);  // 带的是出手者自己的法力,不是受击者的
            Assert.AreEqual(B, LungeTarget(beat));                  // 冲向的仍是受击者,不是自己
            CollectionAssert.AreEqual(new[] { 0, 0 }, SegmentsOf(beat)); // 两个单位各自的第一段,同时结算

            var health = HealthAfterPlayback(plan);
            Assert.AreEqual(900UL, health[B]);
            Assert.AreEqual(950UL, health[A]);
        }

        [Test]
        public void Reflect_OnBothComboSegments_KeepsEachUnitsSegmentsInOrder()
        {
            var plan = Build(new Round(A, B)
                .BeginGroup()
                .Attack(A, B)
                .Damage(A, B, 100, 900)
                .ReflectBack(B, A, 50, 950)
                .HitIndex(1).Damage(A, B, 50, 850, Combo, crit: true)
                .ReflectBack(B, A, 25, 925)); // 连击段引发的反震带 hit_index = 1

            Assert.AreEqual(1, plan.Count);
            var beat = plan.Beats[0];
            Assert.AreEqual(4, beat.Targets.Count);
            AssertEntry(beat.Targets[0], B, TargetEffect.Damage, 100, 900, 0);
            AssertEntry(beat.Targets[1], A, TargetEffect.Damage, 50, 950, 0);
            AssertEntry(beat.Targets[2], B, TargetEffect.Damage, 50, 850, 1);
            AssertEntry(beat.Targets[3], A, TargetEffect.Damage, 25, 925, 1);
            // B、A 各两段:首段同时结算,第二段一起错后
            CollectionAssert.AreEqual(new[] { 0, 0, 1, 1 }, SegmentsOf(beat));
            Assert.IsTrue(beat.AnyCrit);
            Assert.IsFalse(beat.Targets[3].IsCrit); // 反震不必杀

            var health = HealthAfterPlayback(plan);
            Assert.AreEqual(850UL, health[B]);
            Assert.AreEqual(925UL, health[A]);
        }

        [Test]
        public void Reflect_KillsTheAttacker_DeathFollowsItsOwnAttackBeat_AndTheNextActionIsIntact()
        {
            var plan = Build(new Round(A, B)
                .BeginGroup()
                .Attack(A, B)
                .Damage(A, B, 100, 900)
                .ReflectBack(B, A, 30, 0)
                .Death(A) // 出手者被弹死:本次行动到此为止,不连击、不判反击
                .BeginGroup()
                .Attack(B, C)
                .Damage(B, C, 60, 940));

            // 预期演出:A 的攻击拍(B -100、A -30 血条归零)→ A 的阵亡拍 → B 的回合照常
            CollectionAssert.AreEqual(new[] { BeatKind.Attack, BeatKind.Death, BeatKind.Attack }, KindsOf(plan));
            CollectionAssert.AreEqual(new[] { A, A, B }, ActorsOf(plan));

            var attack = plan.Beats[0];
            Assert.AreEqual(2, attack.Targets.Count);
            Assert.IsTrue(attack.FindTarget(A).Died);  // 死者是本拍的条目 → DEATH 挂到整拍之后
            Assert.IsFalse(attack.FindTarget(B).Died);
            Assert.AreEqual(B, LungeTarget(attack));

            var death = plan.Beats[1];
            Assert.AreEqual(TurnPlan.DeathSeconds, death.DurationSeconds, Eps);
            Assert.AreEqual(TurnPlan.DeathOverlapSeconds, death.OverlapNextSeconds, Eps);
            Assert.IsTrue(death.ActorIsMine);

            var next = plan.Beats[2];
            Assert.AreEqual(2u, next.GroupId);
            Assert.AreEqual(1, next.Targets.Count);
            AssertEntry(next.Targets[0], C, TargetEffect.Damage, 60, 940, 0);

            var health = HealthAfterPlayback(plan);
            Assert.AreEqual(0UL, health[A]);
            Assert.AreEqual(900UL, health[B]);
            Assert.AreEqual(940UL, health[C]);
            Assert.AreEqual(TurnPlan.AttackSeconds + (TurnPlan.DeathSeconds - TurnPlan.DeathOverlapSeconds) + TurnPlan.AttackSeconds,
                plan.TotalSeconds, Eps);
        }

        [Test]
        public void Reflect_OnTheComboSegment_KillsTheAttacker_AfterBothSegmentsLanded()
        {
            var plan = Build(new Round(A, B)
                .BeginGroup()
                .Attack(A, B)
                .Damage(A, B, 100, 900)
                .HitIndex(1).Damage(A, B, 50, 850, Combo)
                .ReflectBack(B, A, 25, 0)
                .Death(A));

            CollectionAssert.AreEqual(new[] { BeatKind.Attack, BeatKind.Death }, KindsOf(plan));
            var attack = plan.Beats[0];
            Assert.AreEqual(3, attack.Targets.Count);
            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, SegmentsOf(attack));
            Assert.IsTrue(attack.FindTarget(A).Died);
            Assert.AreEqual(A, plan.Beats[1].ActorId);
            Assert.AreEqual(850UL, HealthAfterPlayback(plan)[B]);
            Assert.AreEqual(0UL, HealthAfterPlayback(plan)[A]);
        }

        // ── 反击:整次普攻之后另起一组(group_id + 1),ATTACK + DAMAGE 都带 hit_kind = COUNTER ──

        [Test]
        public void Counter_IsItsOwnAttackBeat_ByTheDefender_AndIsNotTiedToActionOrder()
        {
            // 出手序 A、C、B;B 的反击插在 A 与 C 之间,于是组数(4)多于出手数(3)
            var plan = Build(new Round(A, C, B)
                .BeginGroup().Attack(A, B).Damage(A, B, 100, 900)
                .BeginGroup().Attack(B, A, Counter).Damage(B, A, 80, 920, Counter)
                .BeginGroup().Attack(C, B).Damage(C, B, 90, 810)
                .BeginGroup().Attack(B, A).Damage(B, A, 70, 850));

            // 预期演出:四拍都是普通的攻击拍;第二拍由 B 冲向 A(与 B 自己回合的那一拍同一套演出)
            CollectionAssert.AreEqual(
                new[] { BeatKind.Attack, BeatKind.Attack, BeatKind.Attack, BeatKind.Attack }, KindsOf(plan));
            CollectionAssert.AreEqual(new[] { A, B, C, B }, ActorsOf(plan));
            CollectionAssert.AreEqual(new List<ulong> { A, C, B }, plan.ActionOrder); // 原样带出,不参与分拍
            Assert.Greater(plan.Count, plan.ActionOrder.Count);
            Assert.AreNotEqual(plan.ActionOrder[1], plan.Beats[1].ActorId); // 拍下标与出手序下标不对应

            var counter = plan.Beats[1];
            Assert.AreEqual(2u, counter.GroupId);
            Assert.AreEqual(1, counter.Targets.Count);
            AssertEntry(counter.Targets[0], A, TargetEffect.Damage, 80, 920, 0); // hit_index 归 0
            Assert.AreEqual(A, LungeTarget(counter));
            Assert.IsFalse(counter.ActorIsMine);       // 出手方按反击者(B,敌方)判,不是按原出手者
            Assert.IsTrue(plan.Beats[0].ActorIsMine);
            Assert.AreEqual(TurnPlan.AttackSeconds, counter.DurationSeconds, Eps);

            // A 的攻击拍没有被反击的伤害污染
            Assert.AreEqual(1, plan.Beats[0].Targets.Count);
            Assert.AreEqual(B, plan.Beats[0].Targets[0].ActorId);

            var health = HealthAfterPlayback(plan);
            Assert.AreEqual(850UL, health[A]);
            Assert.AreEqual(810UL, health[B]);
            Assert.AreEqual(4 * TurnPlan.AttackSeconds, plan.TotalSeconds, Eps);
        }

        [Test]
        public void Counter_KillsTheOriginalAttacker_DeathFollowsTheCounterBeat()
        {
            var plan = Build(new Round(A, C, B)
                .BeginGroup().Attack(A, B).Damage(A, B, 100, 900)
                .BeginGroup().Attack(B, A, Counter).Damage(B, A, 80, 0, Counter, crit: true).Death(A)
                .BeginGroup().Attack(C, B).Damage(C, B, 90, 810));

            CollectionAssert.AreEqual(
                new[] { BeatKind.Attack, BeatKind.Attack, BeatKind.Death, BeatKind.Attack }, KindsOf(plan));
            CollectionAssert.AreEqual(new[] { A, B, A, C }, ActorsOf(plan));
            var counter = plan.Beats[1];
            Assert.IsTrue(counter.AnyCrit); // 反击按普攻公式结算,可必杀
            Assert.IsTrue(counter.FindTarget(A).Died);
            Assert.AreEqual(1, plan.Beats[3].Targets.Count); // 后面的行动没有被阵亡拍吞掉
            Assert.AreEqual(0UL, HealthAfterPlayback(plan)[A]);
        }

        // ── 三种形态同回合 ──

        [Test]
        public void ComboReflectAndCounter_InOneRound_GiveThreeBeats_AndHealthLandsOnTheRightUnits()
        {
            var plan = Build(new Round(A, B)
                // A 的回合:首段 → 反震 → 连击段(必杀)→ 反震
                .BeginGroup()
                .Attack(A, B)
                .Damage(A, B, 100, 900)
                .ReflectBack(B, A, 50, 950)
                .HitIndex(1).Damage(A, B, 50, 850, Combo, crit: true)
                .ReflectBack(B, A, 25, 925)
                // 整次普攻结束后 B 反击
                .BeginGroup()
                .Attack(B, A, Counter)
                .Damage(B, A, 80, 845, Counter)
                // B 自己的回合:普攻 A,被 A 反震
                .BeginGroup()
                .Attack(B, A)
                .Damage(B, A, 60, 785)
                .ReflectBack(A, B, 30, 820));

            Assert.AreEqual(3, plan.Count);
            CollectionAssert.AreEqual(new[] { A, B, B }, ActorsOf(plan));
            CollectionAssert.AreEqual(new[] { BeatKind.Attack, BeatKind.Attack, BeatKind.Attack }, KindsOf(plan));
            Assert.AreEqual(2, plan.ActionOrder.Count); // 三拍、两个出手者

            var first = plan.Beats[0];
            Assert.AreEqual(4, first.Targets.Count);
            CollectionAssert.AreEqual(new[] { 0, 0, 1, 1 }, SegmentsOf(first));
            Assert.AreEqual(B, LungeTarget(first));
            Assert.IsTrue(first.AnyCrit);

            var counter = plan.Beats[1];
            Assert.AreEqual(1, counter.Targets.Count);
            AssertEntry(counter.Targets[0], A, TargetEffect.Damage, 80, 845, 0);

            var third = plan.Beats[2];
            Assert.AreEqual(2, third.Targets.Count);
            AssertEntry(third.Targets[0], A, TargetEffect.Damage, 60, 785, 0);
            AssertEntry(third.Targets[1], B, TargetEffect.Damage, 30, 820, 0); // B 自己吃反震
            Assert.AreEqual(A, LungeTarget(third));

            // 整回合播完,每个单位的血量就是服务端最后一次报给它的 target_health_after
            var health = HealthAfterPlayback(plan);
            Assert.AreEqual(785UL, health[A]);
            Assert.AreEqual(820UL, health[B]);
            Assert.AreEqual(3 * TurnPlan.AttackSeconds, plan.TotalSeconds, Eps);
        }

        // ── 抵抗:BATTLE_EVENT_RESIST = 15,出现在技能那一组里,没有对应的 BUFF_ADD ──

        private static Round AoeWithAilment(bool resisted)
        {
            var round = new Round(A, D)
                .BeginGroup()
                .Skill(A, B, 501)
                .ManaCost(A, 501, 12, 299)
                .Damage(A, B, 300, 0, skill: 501).Death(B) // 第一个目标被打死(之后不再挂 buff)
                .HitIndex(1).Damage(A, D, 60, 940, skill: 501);
            round = resisted ? round.Resist(A, D, 50) : round; // 第二个目标抵抗了异常:没有 BUFF_ADD
            return round
                .HitIndex(2).Damage(A, E, 70, 930, skill: 501).BuffAdd(A, E, 50) // 第三个目标正常挂上
                .BeginGroup().Attack(D, A).Damage(D, A, 40, 960);
        }

        [Test]
        public void Resist_InsideASkillGroup_MakesNoBeat_AndDoesNotSwallowItsNeighbours()
        {
            var plan = Build(AoeWithAilment(resisted: true));

            // 预期演出:一拍施法(B、D、E 同拍飙血,E 挂上 buff)→ B 阵亡 → D 的回合;抵抗本身没有任何表现
            CollectionAssert.AreEqual(new[] { BeatKind.Cast, BeatKind.Death, BeatKind.Attack }, KindsOf(plan));
            var cast = plan.Beats[0];
            Assert.AreEqual(501u, cast.SkillId);
            Assert.IsTrue(cast.HasActorManaAfter);
            Assert.AreEqual(299UL, cast.ActorManaAfter);
            Assert.AreEqual(4, cast.Targets.Count);
            AssertEntry(cast.Targets[0], B, TargetEffect.Damage, 300, 0, 0);
            AssertEntry(cast.Targets[1], D, TargetEffect.Damage, 60, 940, 1);
            AssertEntry(cast.Targets[2], E, TargetEffect.Damage, 70, 930, 2); // 抵抗之后的 DAMAGE 仍在同一拍
            Assert.AreEqual(E, cast.Targets[3].ActorId);
            Assert.AreEqual(TargetEffect.BuffAdd, cast.Targets[3].Effect);   // 抵抗之后的 BUFF_ADD 也没丢
            Assert.AreEqual(50u, cast.Targets[3].BuffId);
            Assert.AreEqual(50u, cast.BuffId);
            foreach (var entry in cast.Targets)
            {
                // 抵抗者 D 身上没有冒出 buff 条目
                if (entry.ActorId == D) Assert.AreEqual(TargetEffect.Damage, entry.Effect);
            }
            Assert.IsTrue(cast.FindTarget(B).Died); // 挂起的 DEATH 没有被抵抗事件提前放出来
            Assert.AreEqual(B, plan.Beats[1].ActorId);
            Assert.AreEqual(1, plan.Beats[2].Targets.Count);

            // 去掉这条 RESIST,计划一模一样
            Assert.AreEqual(Shape(Build(AoeWithAilment(resisted: false))), Shape(plan));
        }

        [Test]
        public void Resist_AsTheOnlyOutcomeOfASkill_LeavesACastBeatWithoutTargets()
        {
            var plan = Build(new Round(A, B)
                .BeginGroup()
                .Skill(A, B, 77)
                .ManaCost(A, 77, 20, 291)
                .Resist(A, B, 30)
                .Resist(B, A, 31) // target_sub_buff 回挂到施法者身上被抵抗:施加者 = 原目标
                .BeginGroup()
                .Attack(B, A)
                .Damage(B, A, 40, 960));

            // 预期演出:A 做一次施法动作、扣蓝,目标身上什么都不发生;随后 B 的回合照常
            CollectionAssert.AreEqual(new[] { BeatKind.Cast, BeatKind.Attack }, KindsOf(plan));
            var cast = plan.Beats[0];
            Assert.AreEqual(A, cast.ActorId);
            Assert.AreEqual(0, cast.Targets.Count);
            Assert.AreEqual(0u, cast.BuffId);
            Assert.AreEqual(-20, cast.ActorManaDelta);
            Assert.AreEqual(291UL, cast.ActorManaAfter);
            Assert.AreEqual(1, plan.Beats[1].Targets.Count);
            Assert.AreEqual(TurnPlan.CastSeconds + TurnPlan.AttackSeconds, plan.TotalSeconds, Eps);
        }

        [Test]
        public void Resist_AloneYieldsNoBeat_AndDoesNotBreakAdjacencyWithoutGroupIds()
        {
            var alone = TurnPlan.Build(new List<TurnEventInput>
            {
                new TurnEventInput { Type = ResistCode, SourceId = A, TargetId = B, BuffId = 50, GroupId = 1 },
            });
            Assert.AreEqual(0, alone.Count);
            Assert.AreEqual(0f, alone.TotalSeconds, Eps);

            // 无 group_id 的回退模式靠「紧随其后」并拍:夹在中间的抵抗不占位、不打断
            var fallback = TurnPlan.Build(new List<TurnEventInput>
            {
                new TurnEventInput { Type = BattleEventCodes.Skill, SourceId = A, TargetId = B, SkillId = 9 },
                new TurnEventInput { Type = ResistCode, SourceId = A, TargetId = B, BuffId = 50 },
                new TurnEventInput { Type = BattleEventCodes.Damage, SourceId = A, TargetId = B, Value = 30, HealthAfter = 70 },
            });
            Assert.AreEqual(1, fallback.Count);
            Assert.AreEqual(BeatKind.Cast, fallback.Beats[0].Kind);
            Assert.AreEqual(1, fallback.Beats[0].Targets.Count);
        }

        // ── hit_kind / 新枚举值过线格式 ──

        [Test]
        public void ResistCode_IsFifteen_AndOutsideTheCodesThePlanKnows()
        {
            Assert.AreEqual(ResistCode, (int)eBattleEventType.BattleEventResist);
            Assert.Less(BattleEventCodes.Mana, ResistCode); // 分拍认识的最大码是 MANA = 14

            var input = TurnEventInput.From(new BattleEventItem
            {
                EventType = eBattleEventType.BattleEventResist, SourceId = A, TargetId = B, BuffTableId = 50, GroupId = 2,
            });
            Assert.AreEqual(ResistCode, input.Type);
            Assert.AreEqual(50u, input.BuffId);
            Assert.AreEqual(2u, input.GroupId);
        }

        [Test]
        public void HitKind_DoesNotChangeThePlan_AndUnknownEnumValuesSurviveTheWire()
        {
            // 同一串事件,带与不带 hit_kind 分出来的拍一模一样:分拍只看 group_id / source / target / hit_index
            Round Stream(bool tagged) => new Round(A, B)
                .BeginGroup()
                .Attack(A, B)
                .Damage(A, B, 100, 900)
                .ReflectBack(B, A, 50, 950)
                .HitIndex(1).Damage(A, B, 50, 850, tagged ? Combo : Normal)
                .BeginGroup()
                .Attack(B, A, tagged ? Counter : Normal)
                .Damage(B, A, 80, 870, tagged ? Counter : Normal);

            var untagged = Stream(false);
            foreach (var ev in untagged.Result.Events) ev.HitKind = Normal; // 连反震那条也清掉
            Assert.AreEqual(Shape(Build(untagged)), Shape(Build(Stream(true))));

            // 将来服务端再加 hit_kind / 事件类型:proto3 枚举按数值原样保留,解析与分拍都不抛
            var future = new Round(A, B)
                .BeginGroup()
                .Attack(A, B)
                .Damage(A, B, 100, 900, (eBattleHitKind)9);
            future.Result.Events.Add(new BattleEventItem
            {
                EventType = (eBattleEventType)16, SourceId = B, TargetId = A, GroupId = 1, Value = 7,
            });
            future.BeginGroup().Attack(B, A).Damage(B, A, 10, 990);

            var parsed = TurnResultS2C.Parser.ParseFrom(future.Result.ToByteArray());
            Assert.AreEqual((eBattleHitKind)9, parsed.Events[1].HitKind);
            Assert.AreEqual(16, (int)parsed.Events[2].EventType);
            TurnPlan plan = null;
            Assert.DoesNotThrow(() => plan = TurnPlan.Build(parsed, myTeam: 0));
            CollectionAssert.AreEqual(new[] { BeatKind.Attack, BeatKind.Attack }, KindsOf(plan));
            Assert.AreEqual(1, plan.Beats[0].Targets.Count);
        }

        // ── 二期接命中表之后才会出现的形态:连击段 / 反击段落空 ──

        [Test]
        public void Miss_OnAComboOrCounterSegment_StaysInItsOwnBeat()
        {
            var plan = Build(new Round(A, B)
                .BeginGroup()
                .Attack(A, B)
                .Damage(A, B, 100, 900)
                .HitIndex(1).Miss(A, B, 900, Combo)
                .BeginGroup()
                .Attack(B, A, Counter)
                .Miss(B, A, 1000, Counter));

            CollectionAssert.AreEqual(new[] { BeatKind.Attack, BeatKind.Attack }, KindsOf(plan));
            var first = plan.Beats[0];
            Assert.AreEqual(2, first.Targets.Count);
            Assert.AreEqual(TargetEffect.Damage, first.Targets[0].Effect);
            Assert.AreEqual(TargetEffect.Miss, first.Targets[1].Effect);
            CollectionAssert.AreEqual(new[] { 0, 1 }, SegmentsOf(first));

            var counter = plan.Beats[1];
            Assert.AreEqual(B, counter.ActorId);
            Assert.AreEqual(1, counter.Targets.Count);
            Assert.AreEqual(TargetEffect.Miss, counter.Targets[0].Effect);
            Assert.IsFalse(counter.Targets[0].HasHealthAfter); // 落空不改血条
            Assert.AreEqual(900UL, HealthAfterPlayback(plan)[B]);
        }

        // ── 演出预算:反击多出来的拍计入总时长,压不进窗口就整回合只落终态 ──

        [Test]
        public void CounterBeats_CountTowardThePlaybackBudget_WhichCompressesOrSkipsInsteadOfOverrunning()
        {
            // 5v5 十个单位各普攻一次;withCounters 时其中两次引发反击(多两组)
            Round FiveVsFive(bool withCounters)
            {
                var round = new Round(A, B);
                for (ulong i = 0; i < 10; i++)
                {
                    ulong attacker = i % 2 == 0 ? A : B;
                    ulong defender = attacker == A ? B : A;
                    round.BeginGroup().Attack(attacker, defender).Damage(attacker, defender, 1, 500 - i);
                    if (withCounters && i < 2)
                        round.BeginGroup().Attack(defender, attacker, Counter).Damage(defender, attacker, 1, 400 - i, Counter);
                }
                return round;
            }

            var plain = Build(FiveVsFive(false));
            var countered = Build(FiveVsFive(true));
            Assert.AreEqual(10, plain.Count);
            Assert.AreEqual(12, countered.Count);
            Assert.AreEqual(plain.TotalSeconds + 2 * TurnPlan.AttackSeconds, countered.TotalSeconds, Eps);

            // 全员自动:服务端下一回合窗口 2s,被动方只留 PassiveReserveSeconds;基础倍率 1.5(BattlePresenter.AutoBattleSpeed)
            float budget = 2f - PlaybackBudget.PassiveReserveSeconds;
            foreach (var plan in new[] { plain, countered })
            {
                var decision = PlaybackBudget.DecideWithBudget(plan.TotalSeconds, budget, 1.5f);
                bool fits = plan.TotalSeconds / budget <= PlaybackBudget.MaxSpeed;
                Assert.AreEqual(!fits, decision.Skip);
                if (!decision.Skip) Assert.LessOrEqual(plan.TotalSeconds / decision.Speed, budget + Eps);
            }
        }
    }
}
