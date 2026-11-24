using System;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game
{
    // 主角权威状态互斥标志的即时接收(服务端 SceneClientPlayerCommon.NotifySelfActorStateFlags)。
    // 行为状态(战斗/跟随/骑乘)或战斗异常状态(沉默等)在服务端发生实际位变化时立刻单播给主角本人;
    // 客户端据此刷新 UI 禁用与本地表现状态机(例如未来实时 MMO 角色动作 HFSM),不替代服务端裁决。
    public sealed partial class GameClient
    {
        private ulong _lastSelfStateSeq;

        /// <summary>主角最近一次权威状态互斥快照(入场通知与断线时复位为全 0)。</summary>
        public SelfActorStateSnapshot SelfActorState { get; private set; }

        /// <summary>当前权威行为状态位图(bit i = eActorState(i))。</summary>
        public ulong SelfActionStateMask => SelfActorState.ActionStateMask;

        /// <summary>当前权威战斗异常状态位图(bit i = eActorCombatState(i))。</summary>
        public ulong SelfCombatStateMask => SelfActorState.CombatStateMask;

        /// <summary>
        /// 主角权威状态位图实际变化时在主线程触发:(previous, current)。
        /// 乱序旧包与位图未变的重复包不会触发此事件。
        /// </summary>
        public event Action<SelfActorStateSnapshot, SelfActorStateSnapshot> OnSelfActorStateChanged;

        private void WireSelfActorStateHandlers()
        {
            SceneClientPlayerCommonRpc.OnNotifySelfActorStateFlags(
                this,
                ApplySelfActorStateFlags,
                ex => LogError($"[actor-state] drop malformed SelfActorStateFlagsS2C: {ex.Message}"));
        }

        private void ApplySelfActorStateFlags(SelfActorStateFlagsS2C msg)
        {
            if (msg == null) return;
            ApplySelfActorStateFlags(msg.ActionStateMask, msg.CombatStateMask, msg.StateSeq);
        }

        /// <summary>
        /// 应用一条服务端状态互斥快照。单调递增的 <paramref name="stateSeq"/> 防乱序旧包回退状态；
        /// 仅当行为或战斗状态位图实际变化时触发 <see cref="OnSelfActorStateChanged"/> 并返回 true。
        /// 测试程序集没有 InternalsVisibleTo,所以公开供 EditMode 单测直接喂入状态变更。
        /// </summary>
        public bool ApplySelfActorStateFlags(ulong actionStateMask, ulong combatStateMask, ulong stateSeq)
        {
            if (stateSeq != 0UL && stateSeq <= _lastSelfStateSeq)
            {
                return false;
            }
            if (stateSeq > _lastSelfStateSeq)
            {
                _lastSelfStateSeq = stateSeq;
            }

            var prev = SelfActorState;
            var next = new SelfActorStateSnapshot(actionStateMask, combatStateMask, stateSeq);
            SelfActorState = next;
            if (prev.MasksEqual(next))
            {
                return false;
            }

            Log($"[actor-state] action=0x{actionStateMask:X} combat=0x{combatStateMask:X} seq={stateSeq}");
            OnSelfActorStateChanged?.Invoke(prev, next);
            return true;
        }

        /// <summary>
        /// 入场通知 / 断线清理：复位主角权威状态位图与序号窗口，若原先非零则通知订阅方回到默认态。
        /// 若服务端角色在重连 / 换场景后仍有非零态，服务端会在 EnterSceneS2C 后立刻补推一次。
        /// 测试程序集没有 InternalsVisibleTo,所以公开供 EditMode 单测覆盖复位语义。
        /// </summary>
        public void ResetSelfActorStateFlags()
        {
            _lastSelfStateSeq = 0UL;
            var prev = SelfActorState;
            SelfActorState = default;
            if (!prev.MasksEqual(default))
            {
                OnSelfActorStateChanged?.Invoke(prev, default);
            }
            Presentation?.Reset();
        }
    }
}
