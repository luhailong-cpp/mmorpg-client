using System;
using System.Collections.Generic;

namespace MmorpgClient.Game.Battle.Presentation
{
    /// <summary>
    /// 战斗事件类型码(与 proto eBattleEventType 数值一致;MISS/BLOCK/MANA 为
    /// turn-battle-presentation.md §2 的增量值 D1/D5)。用 int 常量而非 enum,
    /// 是为了让 <see cref="TurnEventInput"/> 保持与 proto 解耦、EditMode 测试可直接构造。
    /// </summary>
    public static class BattleEventCodes
    {
        public const int None = 0;
        public const int Attack = 1;
        public const int Skill = 2;
        public const int Damage = 3;
        public const int Heal = 4;
        public const int BuffAdd = 5;
        public const int BuffRemove = 6;
        public const int BuffTick = 7;
        public const int Death = 8;
        public const int Defend = 9;
        public const int Item = 10;
        public const int Flee = 11;
        public const int Miss = 12;
        public const int Block = 13;
        public const int Mana = 14;
    }

    /// <summary>一拍演出的类型。</summary>
    public enum BeatKind
    {
        /// <summary>普攻出手(含并入的命中/闪避/格挡结果)。</summary>
        Attack,
        /// <summary>技能施放(含并入的多目标伤害/治疗/上 buff)。</summary>
        Cast,
        /// <summary>孤立的伤害结算(没有前置出手事件,如反伤)。</summary>
        Hit,
        /// <summary>孤立的未命中。</summary>
        Miss,
        /// <summary>孤立的治疗。</summary>
        Heal,
        BuffAdd,
        BuffRemove,
        /// <summary>周期效果(回合末 dot/hot)。</summary>
        BuffTick,
        Death,
        Defend,
        Item,
        Flee,
        /// <summary>孤立的 MP 变化。</summary>
        Mana,
    }

    /// <summary>拍内每个目标承受的效果类型。</summary>
    public enum TargetEffect
    {
        Damage,
        Miss,
        Block,
        Heal,
        BuffAdd,
        BuffRemove,
        /// <summary>只有 MP 变化(施法者耗蓝等)。</summary>
        Mana,
        /// <summary>
        /// 周期效果结算(BUFF_TICK):proto value 为 uint64 无符号,dot 伤害与 hot 回血同类型同字段,
        /// 表现层按 HealthAfter 与当前 HP 比较决定按伤害还是治疗演出(<see cref="TurnPlan.TickIsHeal"/>)。
        /// </summary>
        Tick,
    }

    /// <summary>
    /// TurnPlan 的输入事件(与 proto 解耦的纯数据),便于 EditMode 直接构造。
    /// </summary>
    public struct TurnEventInput
    {
        public int Type;
        public ulong SourceId;
        public ulong TargetId;
        public uint SkillId;
        public uint BuffId;
        public uint ItemId;
        public long Value;
        public bool IsCritical;
        public bool Success;
        public ulong HealthAfter;
        public uint GroupId;
        public int HitIndex;
        public ulong ManaAfter;
        public bool HasManaAfter;

        public static TurnEventInput From(BattleEventItem ev)
        {
            int type = (int)ev.EventType;
            return new TurnEventInput
            {
                Type = type,
                SourceId = ev.SourceId,
                TargetId = ev.TargetId,
                SkillId = ev.SkillTableId,
                BuffId = ev.BuffTableId,
                ItemId = ev.ItemTableId,
                // proto value 为 uint64 无符号;MANA "负为消耗"的语义由 MergeMana 按 source==target 推断
                Value = unchecked((long)ev.Value),
                IsCritical = ev.IsCritical,
                Success = ev.Success,
                HealthAfter = ev.TargetHealthAfter,
                GroupId = ev.GroupId,
                HitIndex = unchecked((int)ev.HitIndex),
                ManaAfter = ev.TargetManaAfter,
                // proto3 标量无 has 位:MANA 事件恒视为带 target_mana_after;其余事件非 0 才算带
                HasManaAfter = type == BattleEventCodes.Mana || ev.TargetManaAfter != 0,
            };
        }
    }

    /// <summary>拍内单个目标的结果。</summary>
    public sealed class BeatTarget
    {
        public ulong ActorId;
        public TargetEffect Effect;
        /// <summary>伤害/治疗量(MANA 为变化量,负为消耗)。</summary>
        public long Value;
        public bool IsCrit;
        /// <summary>结算后 HP(DAMAGE/HEAL/BUFF_TICK 带;其余为 0 且 HasHealthAfter=false)。</summary>
        public ulong HealthAfter;
        public bool HasHealthAfter;
        public ulong ManaAfter;
        public bool HasManaAfter;
        public uint BuffId;
        /// <summary>多段攻击序号(D2 hit_index),旧服务端恒 0。</summary>
        public int HitIndex;
        /// <summary>BUFF_ADD 等带 success 语义的事件结果。</summary>
        public bool Success;
        /// <summary>
        /// 该目标在本拍结算中阵亡(同一行动内的 DEATH 被挂起到本拍之后单独成拍,
        /// 表现层可据此在拍末不回 idle)。
        /// </summary>
        public bool Died;
    }

    /// <summary>演出指令的最小单位:一拍(拍内多目标并行,拍间串行)。</summary>
    public sealed class Beat
    {
        public ulong ActorId;
        public BeatKind Kind;
        public readonly List<BeatTarget> Targets = new List<BeatTarget>();
        public uint SkillId;
        public uint BuffId;
        public uint ItemId;
        public float DurationSeconds;
        /// <summary>
        /// 本拍尾段可与下一拍重叠的秒数(死亡倒地渐隐时下一个出手者已可起手);
        /// 序列器按 DurationSeconds − OverlapNextSeconds 推进,最后一拍不重叠。
        /// </summary>
        public float OverlapNextSeconds;
        public uint GroupId;
        /// <summary>FLEE 等带成败的事件结果(拍级)。</summary>
        public bool Success;
        /// <summary>出手者是否我方(Build(TurnResultS2C, myTeam) 按 state.actors 判定;未知为 false)。</summary>
        public bool ActorIsMine;
        /// <summary>出手者自身的 MP 变化(施法耗蓝);HasActorManaAfter=false 表示本拍无此信息。</summary>
        public long ActorManaDelta;
        public ulong ActorManaAfter;
        public bool HasActorManaAfter;
        /// <summary>本拍对应的首个事件在 events[] 中的下标(调试/回放定位)。</summary>
        public int FirstEventIndex;

        public BeatTarget FindTarget(ulong actorId)
        {
            for (int i = 0; i < Targets.Count; i++)
            {
                if (Targets[i].ActorId == actorId) return Targets[i];
            }
            return null;
        }

        /// <summary>本拍是否含任何暴击结果(暴击顿帧/震屏用)。</summary>
        public bool AnyCrit
        {
            get
            {
                for (int i = 0; i < Targets.Count; i++)
                {
                    if (Targets[i].IsCrit) return true;
                }
                return false;
            }
        }
    }

    /// <summary>
    /// 回合演出计划:把 TurnResultS2C.events[] 编成拍序列(turn-battle-presentation.md §3)。
    ///
    /// 规则:
    ///  - ATTACK/SKILL 开一拍;其后同 group_id 的 DAMAGE/MISS/BLOCK/HEAL/BUFF_ADD/BUFF_REMOVE 并入该拍(多目标并行);
    ///  - 无 group_id(旧服务端,所有 group_id==0)时:紧随 ATTACK/SKILL 之后的 DAMAGE/MISS/BLOCK 并入前一拍;
    ///    HEAL/BUFF_* 仅当 source 与出手者相同且紧随其后时并入(自疗/自 buff 技能);
    ///  - MANA 并入当前拍(施法者耗蓝记在 ActorMana*,目标回蓝记在对应 BeatTarget);无当前拍则单独一拍;
    ///  - DEATH 单独一拍(1.2s):若死者是当前开放拍的目标(引擎逐目标结算时把 DEATH 插在同组后续
    ///    DAMAGE 之间),则挂起到该拍关闭后再依次追加,保证群攻所有目标同一拍飙血、死亡排在整拍之后;
    ///    否则结束当前拍立即成拍。Death 拍尾段 0.6s 可与下一拍重叠;
    ///  - BUFF_TICK 每条单独一拍(0.25s,目标效果 Tick,伤害/回血由表现层按 HP 判);DEFEND/ITEM/FLEE 单独一拍(0.6s);
    ///  - 时长:ATTACK 0.9s、SKILL 1.4s、孤立 Hit/Miss 0.6s、孤立 Heal 0.8s、孤立 Buff 0.5s、孤立 Mana 0.3s。
    ///
    /// 纯 C#,不引用 UnityEngine。
    /// </summary>
    public sealed class TurnPlan
    {
        public const float AttackSeconds = 0.9f;
        public const float CastSeconds = 1.4f;
        public const float DeathSeconds = 1.2f;
        /// <summary>Death 拍尾段可与下一拍重叠的秒数(倒地后的渐隐阶段)。</summary>
        public const float DeathOverlapSeconds = 0.6f;
        public const float BuffTickSeconds = 0.25f;
        public const float DefendSeconds = 0.6f;
        public const float ItemSeconds = 0.6f;
        public const float FleeSeconds = 0.6f;
        public const float LoneHitSeconds = 0.6f;
        public const float LoneMissSeconds = 0.6f;
        public const float LoneHealSeconds = 0.8f;
        public const float LoneBuffSeconds = 0.5f;
        public const float LoneManaSeconds = 0.3f;

        public readonly List<Beat> Beats = new List<Beat>();
        public uint RoundIndex;
        /// <summary>出手序(D3 action_order);旧服务端为空。</summary>
        public readonly List<ulong> ActionOrder = new List<ulong>();
        /// <summary>输入是否带 group_id(任一事件非 0)。</summary>
        public bool UsedGroupIds;

        public int Count => Beats.Count;

        /// <summary>整段演出时长(秒):各拍时长之和,扣除非末拍的尾段重叠(与 BattleSequencer 推进规则一致)。</summary>
        public float TotalSeconds
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < Beats.Count; i++) total += EffectiveSeconds(Beats[i], i == Beats.Count - 1);
                return total;
            }
        }

        /// <summary>一拍在序列中实际占用的秒数(末拍不扣重叠;下限 0)。</summary>
        public static float EffectiveSeconds(Beat beat, bool isLast)
        {
            if (beat == null) return 0f;
            float seconds = beat.DurationSeconds;
            if (!isLast && beat.OverlapNextSeconds > 0f) seconds -= beat.OverlapNextSeconds;
            return seconds > 0f ? seconds : 0f;
        }

        /// <summary>
        /// BUFF_TICK 目标是回血还是掉血:无表可查,按结算后 HP 与当前 HP 比较;
        /// 旧服务端不带 target_health_after 时按伤害处理。
        /// </summary>
        public static bool TickIsHeal(BeatTarget target, ulong currentHealth)
            => target != null && target.HasHealthAfter && target.HealthAfter > currentHealth;

        // ── 构建入口 ─────────────────────────────────────────

        /// <summary>从服务端回合结果构建;myTeam 用来给每拍标 ActorIsMine(查 result.State.actors)。</summary>
        public static TurnPlan Build(TurnResultS2C result, uint myTeam)
        {
            var inputs = new List<TurnEventInput>();
            Func<ulong, bool> isMine = null;
            uint round = 0;
            IEnumerable<ulong> order = null;

            if (result != null)
            {
                round = result.RoundIndex;
                order = result.ActionOrder; // D3 出手序,旧服务端为空
                if (result.Events != null)
                {
                    foreach (var ev in result.Events)
                    {
                        if (ev == null) continue;
                        inputs.Add(TurnEventInput.From(ev));
                    }
                }
                if (result.State != null && result.State.Actors != null)
                {
                    var teamById = new Dictionary<ulong, uint>();
                    foreach (var actor in result.State.Actors)
                    {
                        if (actor != null) teamById[actor.ActorId] = actor.TeamIndex;
                    }
                    isMine = id => teamById.TryGetValue(id, out uint team) && team == myTeam;
                }
            }

            var plan = Build(inputs, isMine);
            plan.RoundIndex = round;
            if (order != null) plan.ActionOrder.AddRange(order);
            return plan;
        }

        /// <summary>从纯数据事件构建(EditMode 测试入口;isMine 可空)。</summary>
        public static TurnPlan Build(IReadOnlyList<TurnEventInput> events, Func<ulong, bool> isMine = null)
        {
            var plan = new TurnPlan();
            if (events == null || events.Count == 0) return plan;

            bool anyGroup = false;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].GroupId != 0) { anyGroup = true; break; }
            }
            plan.UsedGroupIds = anyGroup;

            // 可继续并入目标的"开放拍"。回退规则的"紧随其后"由它保证:凡不可并入的事件
            // (DEATH/DEFEND/FLEE/BUFF_TICK/孤立 MANA)都会把它置空;未知/NONE 事件不占位、不打断。
            Beat current = null;

            // 挂起的 DEATH(死者是当前开放拍的目标):引擎在逐目标循环内对每个目标 DAMAGE 后立即追加
            // DEATH,且同一行动全部事件共用 group_id;若在这里就关闭当前拍,群攻的后续目标会被拆成孤立
            // Hit 拍(无施法动作/技能特效、拍数与时长膨胀)。改为等当前拍关闭时再依次追加 Death 拍。
            var pendingDeaths = new List<(TurnEventInput ev, int index)>();

            void FlushDeaths()
            {
                for (int d = 0; d < pendingDeaths.Count; d++)
                {
                    var (dev, dindex) = pendingDeaths[d];
                    var death = NewBeat(plan, BeatKind.Death, ResolveDeathActor(dev), DeathSeconds, dev, dindex, isMine);
                    death.OverlapNextSeconds = DeathOverlapSeconds;
                }
                pendingDeaths.Clear();
            }

            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];

                switch (ev.Type)
                {
                    case BattleEventCodes.Attack:
                    case BattleEventCodes.Skill:
                    {
                        FlushDeaths();
                        bool isSkill = ev.Type == BattleEventCodes.Skill;
                        current = NewBeat(plan, isSkill ? BeatKind.Cast : BeatKind.Attack, ev.SourceId,
                            isSkill ? CastSeconds : AttackSeconds, ev, i, isMine);
                        current.SkillId = ev.SkillId;
                        // 老引擎可能把首个目标直接写在 ATTACK/SKILL 上但没有 DAMAGE;不据此造目标,等结算事件
                        break;
                    }

                    case BattleEventCodes.Damage:
                    case BattleEventCodes.Miss:
                    case BattleEventCodes.Block:
                    {
                        var effect = ev.Type == BattleEventCodes.Damage ? TargetEffect.Damage
                            : ev.Type == BattleEventCodes.Miss ? TargetEffect.Miss : TargetEffect.Block;
                        if (CanMergeOutcome(current, ev, anyGroup))
                        {
                            AddTarget(current, ev, effect);
                        }
                        else
                        {
                            FlushDeaths();
                            var kind = ev.Type == BattleEventCodes.Miss ? BeatKind.Miss : BeatKind.Hit;
                            current = NewBeat(plan, kind, ev.SourceId,
                                kind == BeatKind.Miss ? LoneMissSeconds : LoneHitSeconds, ev, i, isMine);
                            AddTarget(current, ev, effect);
                        }
                        break;
                    }

                    case BattleEventCodes.Heal:
                    {
                        if (CanMergeSupport(current, ev, anyGroup))
                        {
                            AddTarget(current, ev, TargetEffect.Heal);
                        }
                        else
                        {
                            FlushDeaths();
                            current = NewBeat(plan, BeatKind.Heal, ev.SourceId, LoneHealSeconds, ev, i, isMine);
                            AddTarget(current, ev, TargetEffect.Heal);
                        }
                        break;
                    }

                    case BattleEventCodes.BuffAdd:
                    case BattleEventCodes.BuffRemove:
                    {
                        var effect = ev.Type == BattleEventCodes.BuffAdd ? TargetEffect.BuffAdd : TargetEffect.BuffRemove;
                        if (CanMergeSupport(current, ev, anyGroup))
                        {
                            AddTarget(current, ev, effect);
                        }
                        else
                        {
                            FlushDeaths();
                            var kind = effect == TargetEffect.BuffAdd ? BeatKind.BuffAdd : BeatKind.BuffRemove;
                            current = NewBeat(plan, kind, ev.SourceId, LoneBuffSeconds, ev, i, isMine);
                            current.BuffId = ev.BuffId;
                            AddTarget(current, ev, effect);
                        }
                        break;
                    }

                    case BattleEventCodes.Mana:
                    {
                        if (current != null)
                        {
                            MergeMana(current, ev);
                        }
                        else
                        {
                            FlushDeaths();
                            var beat = NewBeat(plan, BeatKind.Mana, ev.SourceId, LoneManaSeconds, ev, i, isMine);
                            MergeMana(beat, ev);
                            // 孤立 MANA 不开放并入,下一条事件自行开拍
                            current = null;
                        }
                        break;
                    }

                    case BattleEventCodes.BuffTick:
                    {
                        FlushDeaths();
                        var beat = NewBeat(plan, BeatKind.BuffTick, ev.SourceId, BuffTickSeconds, ev, i, isMine);
                        beat.BuffId = ev.BuffId;
                        // dot 伤害与 hot 回血同类型同字段(uint64 无符号):记为 Tick,表现层按 HealthAfter 与当前 HP 判
                        AddTarget(beat, ev, TargetEffect.Tick);
                        current = null;
                        break;
                    }

                    case BattleEventCodes.Death:
                    {
                        ulong who = ResolveDeathActor(ev);
                        var victim = CanDeferDeath(current, ev, who, anyGroup);
                        if (victim != null)
                        {
                            victim.Died = true;
                            pendingDeaths.Add((ev, i));
                        }
                        else
                        {
                            FlushDeaths();
                            var death = NewBeat(plan, BeatKind.Death, who, DeathSeconds, ev, i, isMine);
                            death.OverlapNextSeconds = DeathOverlapSeconds;
                            current = null;
                        }
                        break;
                    }

                    case BattleEventCodes.Defend:
                    {
                        FlushDeaths();
                        NewBeat(plan, BeatKind.Defend, ev.SourceId != 0 ? ev.SourceId : ev.TargetId, DefendSeconds, ev, i, isMine);
                        current = null;
                        break;
                    }

                    case BattleEventCodes.Item:
                    {
                        FlushDeaths();
                        current = NewBeat(plan, BeatKind.Item, ev.SourceId, ItemSeconds, ev, i, isMine);
                        current.ItemId = ev.ItemId;
                        // 道具的 HEAL/BUFF_ADD 结果可并入(同 group 或紧随其后且同 source)
                        break;
                    }

                    case BattleEventCodes.Flee:
                    {
                        FlushDeaths();
                        var beat = NewBeat(plan, BeatKind.Flee, ev.SourceId, FleeSeconds, ev, i, isMine);
                        beat.Success = ev.Success;
                        current = null;
                        break;
                    }

                    default:
                        // 未知/NONE:忽略
                        break;
                }
            }

            FlushDeaths();
            return plan;
        }

        // ── 合并判定 ─────────────────────────────────────────

        /// <summary>DAMAGE/MISS/BLOCK 能否并入当前拍。</summary>
        private static bool CanMergeOutcome(Beat current, in TurnEventInput ev, bool anyGroup)
        {
            if (current == null) return false;
            if (anyGroup)
            {
                // 分组模式:只认同 group_id(0 视为未分组,不并入)
                return ev.GroupId != 0 && ev.GroupId == current.GroupId;
            }
            // 回退模式:只并入紧随其后的 ATTACK/SKILL 开放拍(允许连续多条 DAMAGE = 多段/多目标)
            return current.Kind == BeatKind.Attack || current.Kind == BeatKind.Cast;
        }

        /// <summary>
        /// DEATH 能否挂起到当前拍之后:死者必须是当前开放拍里已有的目标(分组模式还要求同 group_id);
        /// 返回该目标条目(第一条),否则 null。
        /// </summary>
        private static BeatTarget CanDeferDeath(Beat current, in TurnEventInput ev, ulong victim, bool anyGroup)
        {
            if (current == null || victim == 0) return null;
            if (anyGroup && (ev.GroupId == 0 || ev.GroupId != current.GroupId)) return null;
            var target = current.FindTarget(victim);
            if (target == null || target.Effect == TargetEffect.Mana) return null;
            return target;
        }

        /// <summary>HEAL/BUFF_ADD/BUFF_REMOVE 能否并入当前拍。</summary>
        private static bool CanMergeSupport(Beat current, in TurnEventInput ev, bool anyGroup)
        {
            if (current == null) return false;
            if (anyGroup)
                return ev.GroupId != 0 && ev.GroupId == current.GroupId;
            if (current.Kind != BeatKind.Attack && current.Kind != BeatKind.Cast && current.Kind != BeatKind.Item)
                return false;
            // 无分组时要求 source 与出手者一致(或事件未填 source),避免把别人的治疗吞进本拍
            return ev.SourceId == 0 || ev.SourceId == current.ActorId;
        }

        private static Beat NewBeat(TurnPlan plan, BeatKind kind, ulong actorId, float seconds,
            in TurnEventInput ev, int eventIndex, Func<ulong, bool> isMine)
        {
            var beat = new Beat
            {
                Kind = kind,
                ActorId = actorId,
                DurationSeconds = seconds,
                GroupId = ev.GroupId,
                FirstEventIndex = eventIndex,
                ActorIsMine = isMine != null && actorId != 0 && isMine(actorId),
            };
            plan.Beats.Add(beat);
            return beat;
        }

        private static void AddTarget(Beat beat, in TurnEventInput ev, TargetEffect effect)
        {
            // 同一目标在同一拍里出现多次(多段攻击):按 hit_index 追加为独立条目,表现层可逐段飙血
            var target = new BeatTarget
            {
                ActorId = ev.TargetId,
                Effect = effect,
                Value = ev.Value,
                IsCrit = ev.IsCritical,
                HealthAfter = ev.HealthAfter,
                HasHealthAfter = effect == TargetEffect.Damage || effect == TargetEffect.Heal || effect == TargetEffect.Block
                                 || effect == TargetEffect.Tick,
                ManaAfter = ev.ManaAfter,
                HasManaAfter = ev.HasManaAfter,
                BuffId = ev.BuffId,
                HitIndex = ev.HitIndex,
                Success = ev.Success,
            };
            if (effect == TargetEffect.BuffAdd || effect == TargetEffect.BuffRemove)
            {
                if (beat.BuffId == 0) beat.BuffId = ev.BuffId;
            }
            beat.Targets.Add(target);
        }

        /// <summary>
        /// MANA 并入所属拍:目标是出手者本人 → 记为施法耗蓝(负);目标是别人 → 记到该目标
        /// (已有条目则补 ManaAfter,否则追加一条 Mana 效果条目)。
        /// </summary>
        private static void MergeMana(Beat beat, in TurnEventInput ev)
        {
            ulong who = ev.TargetId != 0 ? ev.TargetId : ev.SourceId;
            long delta = ev.Value;
            // uint64 承载的"消耗"没有符号:目标即出手者且未带符号时按消耗处理
            if (delta > 0 && who == beat.ActorId && ev.SourceId == who) delta = -delta;

            ulong manaAfter = ev.HasManaAfter ? ev.ManaAfter : (ulong)Math.Max(0L, ev.Value);
            if (who == beat.ActorId && beat.Kind != BeatKind.Mana)
            {
                beat.ActorManaDelta = delta;
                beat.ActorManaAfter = manaAfter;
                beat.HasActorManaAfter = true;
                return;
            }

            var existing = beat.FindTarget(who);
            if (existing != null)
            {
                existing.ManaAfter = manaAfter;
                existing.HasManaAfter = true;
                if (existing.Effect == TargetEffect.Mana) existing.Value = delta;
                return;
            }

            beat.Targets.Add(new BeatTarget
            {
                ActorId = who,
                Effect = TargetEffect.Mana,
                Value = delta,
                ManaAfter = manaAfter,
                HasManaAfter = true,
            });
        }

        /// <summary>DEATH 事件的死者:引擎填 target;旧数据只填 source 时兜底。</summary>
        private static ulong ResolveDeathActor(in TurnEventInput ev)
            => ev.TargetId != 0 ? ev.TargetId : ev.SourceId;
    }
}
