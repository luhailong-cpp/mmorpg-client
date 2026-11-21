using System;

namespace MmorpgClient.Game
{
    /// <summary>
    /// 服务端权威主角行为状态位图（对齐服务端 {@code eActorState}：bit i = eActorState(i)）。
    /// 仅作为客户端 UI 禁用、本地输入拦截与表现层状态机纠偏依据，权威判定始终在服务端。
    /// </summary>
    [Flags]
    public enum ActorActionStateFlags : ulong
    {
        None = 0UL,
        Combat = 1UL << 0,
        TeamFollow = 1UL << 1,
        Mounted = 1UL << 2,
    }

    /// <summary>
    /// 服务端权威主角战斗异常状态位图（对齐服务端 {@code eActorCombatState}：bit i = eActorCombatState(i)）。
    /// </summary>
    [Flags]
    public enum ActorCombatStateFlags : ulong
    {
        None = 0UL,
        Silence = 1UL << 1,
    }

    /// <summary>
    /// 主角权威状态互斥标志的不可变快照（由服务端 {@code SelfActorStateFlagsS2C} 即时单播刷新）。
    /// </summary>
    public readonly struct SelfActorStateSnapshot : IEquatable<SelfActorStateSnapshot>
    {
        public ulong ActionStateMask { get; }
        public ulong CombatStateMask { get; }
        public ulong StateSeq { get; }

        public ActorActionStateFlags ActionFlags => (ActorActionStateFlags)ActionStateMask;
        public ActorCombatStateFlags CombatFlags => (ActorCombatStateFlags)CombatStateMask;

        public bool IsInCombat => (ActionStateMask & (ulong)ActorActionStateFlags.Combat) != 0UL;
        public bool IsTeamFollowing => (ActionStateMask & (ulong)ActorActionStateFlags.TeamFollow) != 0UL;
        public bool IsMounted => (ActionStateMask & (ulong)ActorActionStateFlags.Mounted) != 0UL;
        public bool IsSilenced => (CombatStateMask & (ulong)ActorCombatStateFlags.Silence) != 0UL;

        public SelfActorStateSnapshot(ulong actionStateMask, ulong combatStateMask, ulong stateSeq)
        {
            ActionStateMask = actionStateMask;
            CombatStateMask = combatStateMask;
            StateSeq = stateSeq;
        }

        public bool HasAction(ActorActionStateFlags flag) => (ActionFlags & flag) == flag;

        public bool HasCombat(ActorCombatStateFlags flag) => (CombatFlags & flag) == flag;

        public bool MasksEqual(in SelfActorStateSnapshot other)
            => ActionStateMask == other.ActionStateMask && CombatStateMask == other.CombatStateMask;

        public bool Equals(SelfActorStateSnapshot other)
            => ActionStateMask == other.ActionStateMask
               && CombatStateMask == other.CombatStateMask
               && StateSeq == other.StateSeq;

        public override bool Equals(object obj) => obj is SelfActorStateSnapshot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ActionStateMask.GetHashCode();
                hash = (hash * 397) ^ CombatStateMask.GetHashCode();
                hash = (hash * 397) ^ StateSeq.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(SelfActorStateSnapshot left, SelfActorStateSnapshot right) => left.Equals(right);

        public static bool operator !=(SelfActorStateSnapshot left, SelfActorStateSnapshot right) => !left.Equals(right);
    }
}
