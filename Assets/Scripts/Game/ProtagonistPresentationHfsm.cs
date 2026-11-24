using System;
using UnityHFSM;

namespace MmorpgClient.Game
{
    /// <summary>
    /// 主角表现层顶层模式：由服务端权威 <see cref="SelfActorStateSnapshot"/> 单向收敛，客户端禁止越权裁决。
    /// 优先级（由高到低）：Controlled（沉默等受控态） > TeamFollow（服务端跟随驱动） > Mounted（骑乘） > Combat > Free。
    /// </summary>
    public enum ProtagonistPresentationMode
    {
        Free = 0,
        Combat = 1,
        Mounted = 2,
        TeamFollow = 3,
        Controlled = 4,
    }

    /// <summary>
    /// 主角表现层叶子状态：驱动角色动画融合、特效生命周期与本地按钮防误触提示。
    /// </summary>
    public enum ProtagonistPresentationState
    {
        Idle = 0,
        Move = 1,
        ActionCast = 2,
        Mounted = 3,
        TeamFollow = 4,
        Controlled = 5,
    }

    /// <summary>
    /// 主角表现层 UnityHFSM 触发器。
    /// </summary>
    public enum ProtagonistPresentationTrigger
    {
        ServerSync = 0,
        MotionChanged = 1,
        CastStarted = 2,
        CastCancelled = 3,
    }

    /// <summary>
    /// 主角纯 C# 表现层层级状态机（基于 MIT 协议的 Inspiaaa/UnityHFSM v2.3.0）：
    /// 1. 表现层归客户端、状态权威归服务端；
    /// 2. 订阅 <see cref="GameClient.OnSelfActorStateChanged"/> 单向收敛（并在 <see cref="Dispose"/> 对称退订），
    ///    收到受控/沉默/组队跟随/骑乘等权威变化时通过 <c>RequestStateChange(..., forceInstantly: true)</c> 立即强切，
    ///    绝不因 <c>ActionCast</c> 的 <c>needsExitTime</c> 拒绝服务端状态；
    /// 3. 严格单线程（零 Thread/Task.Run/Reflection.Emit），通过 <see cref="Tick"/> 在 Unity 主线程推进，兼容 WebGL2 与 IL2CPP。
    /// </summary>
    public sealed class ProtagonistPresentationHfsm : IDisposable
    {
        public const float DefaultCastPresentationDurationSec = 0.45f;

        private readonly StateMachine<string, ProtagonistPresentationMode, ProtagonistPresentationTrigger> _rootFsm;
        private readonly StateMachine<ProtagonistPresentationMode, ProtagonistPresentationState, ProtagonistPresentationTrigger> _freeSubFsm;
        private readonly StateMachine<ProtagonistPresentationMode, ProtagonistPresentationState, ProtagonistPresentationTrigger> _combatSubFsm;

        private GameClient _boundClient;
        private SelfActorStateSnapshot _serverState;
        private bool _isMoving;
        private uint _activeCastSkillId;
        private float _castRemainingSec;
        private bool _preservingCastAcrossModeSwitch;
        private bool _forcingCastAbort;
        private (bool hasValue, uint skillId, bool completed) _pendingCastEnded;

        private ProtagonistPresentationMode _lastEmittedMode;
        private ProtagonistPresentationState _lastEmittedState;

        public ProtagonistPresentationHfsm()
        {
            _freeSubFsm = BuildActionSubFsm();
            _combatSubFsm = BuildActionSubFsm();

            _rootFsm = new StateMachine<string, ProtagonistPresentationMode, ProtagonistPresentationTrigger>();
            _rootFsm.name = "Root";
            _rootFsm.AddState(ProtagonistPresentationMode.Free, _freeSubFsm);
            _rootFsm.AddState(ProtagonistPresentationMode.Combat, _combatSubFsm);
            _rootFsm.AddState(ProtagonistPresentationMode.Mounted,
                new State<ProtagonistPresentationMode, ProtagonistPresentationTrigger>());
            _rootFsm.AddState(ProtagonistPresentationMode.TeamFollow,
                new State<ProtagonistPresentationMode, ProtagonistPresentationTrigger>());
            _rootFsm.AddState(ProtagonistPresentationMode.Controlled,
                new State<ProtagonistPresentationMode, ProtagonistPresentationTrigger>());
            _rootFsm.SetStartState(ProtagonistPresentationMode.Free);
            _rootFsm.Init();

            _lastEmittedMode = CurrentMode;
            _lastEmittedState = CurrentState;
        }

        /// <summary>最近一次收敛的服务端权威快照。</summary>
        public SelfActorStateSnapshot ServerState => _serverState;

        /// <summary>当前表现层顶层模式。</summary>
        public ProtagonistPresentationMode CurrentMode => _rootFsm.ActiveStateName;

        /// <summary>当前表现层叶子状态。</summary>
        public ProtagonistPresentationState CurrentState => CurrentMode switch
        {
            ProtagonistPresentationMode.Controlled => ProtagonistPresentationState.Controlled,
            ProtagonistPresentationMode.TeamFollow => ProtagonistPresentationState.TeamFollow,
            ProtagonistPresentationMode.Mounted => ProtagonistPresentationState.Mounted,
            ProtagonistPresentationMode.Free => _freeSubFsm.ActiveStateName,
            ProtagonistPresentationMode.Combat => _combatSubFsm.ActiveStateName,
            _ => ProtagonistPresentationState.Idle,
        };

        /// <summary>UnityHFSM 当前完整层级路径（例如 Root/Combat/ActionCast）。</summary>
        public string ActiveHierarchyPath => _rootFsm.GetActiveHierarchyPath();

        /// <summary>当前是否有本地或移动输入表现。</summary>
        public bool IsMoving => _isMoving;

        /// <summary>是否已绑定到 <see cref="GameClient"/> 事件源。</summary>
        public bool IsBound => _boundClient != null;

        /// <summary>正在演出的技能表 ID（非 ActionCast 时为 0）。</summary>
        public uint ActiveCastSkillId => CurrentState == ProtagonistPresentationState.ActionCast ? _activeCastSkillId : 0u;

        /// <summary>施法/动作演出剩余时间（秒，非 ActionCast 时为 0）。</summary>
        public float RemainingCastSec => CurrentState == ProtagonistPresentationState.ActionCast ? _castRemainingSec : 0f;

        /// <summary>本地交互防误触预判：非受控且非组队跟随驱动时允许发起本地移动操作。</summary>
        public bool CanMoveLocally => CurrentMode != ProtagonistPresentationMode.Controlled
            && CurrentMode != ProtagonistPresentationMode.TeamFollow;

        /// <summary>本地技能栏防误触预判：处于常规或战斗自由态（未被沉默/跟随/骑乘占用）时允许触发施法演出。</summary>
        public bool CanStartLocalActionPresentation => CurrentMode == ProtagonistPresentationMode.Free
            || CurrentMode == ProtagonistPresentationMode.Combat;

        /// <summary>顶层模式发生变化时在状态收敛后触发（prev, next）。</summary>
        public event Action<ProtagonistPresentationMode, ProtagonistPresentationMode> OnModeChanged;

        /// <summary>叶子表现状态发生变化时在状态收敛后触发（prev, next）。</summary>
        public event Action<ProtagonistPresentationState, ProtagonistPresentationState> OnStateChanged;

        /// <summary>技能动作演出结束时触发：(skillTableId, completed)。completed=false 表示被服务端或打断强退。</summary>
        public event Action<uint, bool> OnCastPresentationEnded;

        /// <summary>按服务端权威快照解析顶层表现模式（纯函数，无副作用）。</summary>
        public static ProtagonistPresentationMode ResolveServerMode(in SelfActorStateSnapshot snapshot)
        {
            if (snapshot.IsSilenced) return ProtagonistPresentationMode.Controlled;
            if (snapshot.IsTeamFollowing) return ProtagonistPresentationMode.TeamFollow;
            if (snapshot.IsMounted) return ProtagonistPresentationMode.Mounted;
            if (snapshot.IsInCombat) return ProtagonistPresentationMode.Combat;
            return ProtagonistPresentationMode.Free;
        }

        /// <summary>绑定 <see cref="GameClient"/> 并立即按其当前 <see cref="GameClient.SelfActorState"/> 收敛；重复绑定会自动先退订旧宿主。</summary>
        public void Bind(GameClient client)
        {
            if (ReferenceEquals(_boundClient, client)) return;
            Unbind();
            if (client == null) return;
            _boundClient = client;
            _boundClient.OnSelfActorStateChanged += HandleServerStateChanged;
            ApplyServerState(client.SelfActorState);
        }

        /// <summary>对称退订事件源；不会销毁内部状态机实例，可再次 <see cref="Bind"/>。</summary>
        public void Unbind()
        {
            if (_boundClient == null) return;
            _boundClient.OnSelfActorStateChanged -= HandleServerStateChanged;
            _boundClient = null;
        }

        public void Dispose() => Unbind();

        /// <summary>根据服务端推送的主角位图快照强制收敛顶层表现模式。</summary>
        public void ApplyServerState(in SelfActorStateSnapshot snapshot)
        {
            _serverState = snapshot;
            var targetMode = ResolveServerMode(in snapshot);
            if (_rootFsm.ActiveStateName == targetMode) return;

            bool targetAllowsCast = targetMode == ProtagonistPresentationMode.Free
                || targetMode == ProtagonistPresentationMode.Combat;
            bool preserveCast = targetAllowsCast && _castRemainingSec > 0f;

            _preservingCastAcrossModeSwitch = preserveCast;
            _forcingCastAbort = !preserveCast && _castRemainingSec > 0f;
            try
            {
                // 服务端权威变化无视子状态 needsExitTime，立即强切收敛。
                _rootFsm.RequestStateChange(targetMode, forceInstantly: true);
                _rootFsm.OnLogic();
            }
            finally
            {
                _preservingCastAcrossModeSwitch = false;
                _forcingCastAbort = false;
            }

            FlushObservedChanges();
        }

        /// <summary>更新主角移动表现标志，在 Free / Combat 下同步切换 Idle 与 Move。</summary>
        public void SetMoving(bool isMoving)
        {
            if (_isMoving == isMoving) return;
            _isMoving = isMoving;
            _rootFsm.OnLogic();
            FlushObservedChanges();
        }

        /// <summary>
        /// 触发一次技能施法/动作演出（受 <see cref="CanStartLocalActionPresentation"/> 防误触限制，且在服务端权威态禁止时返回 false）。
        /// 使用 UnityHFSM <c>needsExitTime</c> 保持动作时长，期间可被服务端打断或控制态立即强切打断。
        /// </summary>
        public bool BeginCastPresentation(uint skillTableId, float durationSec = DefaultCastPresentationDurationSec)
        {
            if (skillTableId == 0u || !CanStartLocalActionPresentation) return false;

            float effectiveDuration = durationSec > 0f ? durationSec : DefaultCastPresentationDurationSec;
            if (CurrentState == ProtagonistPresentationState.ActionCast && _activeCastSkillId != 0u)
            {
                _pendingCastEnded = (true, _activeCastSkillId, false);
            }

            _activeCastSkillId = skillTableId;
            _castRemainingSec = effectiveDuration;
            _rootFsm.OnLogic();
            FlushObservedChanges();
            return CurrentState == ProtagonistPresentationState.ActionCast;
        }

        /// <summary>
        /// 响应服务端打断通知（如 <c>NotifySkillInterrupted</c>）或本地主动收口，绕过 <c>needsExitTime</c> 立即退出动作演出态。
        /// </summary>
        public bool InterruptCastPresentation()
        {
            var sub = GetActiveActionSubFsm();
            if (sub == null || sub.ActiveStateName != ProtagonistPresentationState.ActionCast) return false;

            _forcingCastAbort = true;
            try
            {
                sub.RequestStateChange(
                    _isMoving ? ProtagonistPresentationState.Move : ProtagonistPresentationState.Idle,
                    forceInstantly: true);
            }
            finally
            {
                _forcingCastAbort = false;
            }

            FlushObservedChanges();
            return true;
        }

        /// <summary>在 Unity 主线程每帧推进状态机与受 <c>needsExitTime</c> 保护的演出计时器。</summary>
        public void Tick(float deltaTimeSec)
        {
            if (deltaTimeSec > 0f && _castRemainingSec > 0f)
            {
                _castRemainingSec = Math.Max(0f, _castRemainingSec - deltaTimeSec);
            }

            _rootFsm.OnLogic();
            FlushObservedChanges();
        }

        /// <summary>换场景 / 断线清理时清空移动与施法残留，并将 HFSM 无条件复位至 Root/Free/Idle。</summary>
        public void Reset()
        {
            _serverState = default;
            _isMoving = false;
            _forcingCastAbort = _castRemainingSec > 0f || _activeCastSkillId != 0u;
            try
            {
                if (_rootFsm.ActiveStateName != ProtagonistPresentationMode.Free)
                {
                    _rootFsm.RequestStateChange(ProtagonistPresentationMode.Free, forceInstantly: true);
                }
                else if (_freeSubFsm.ActiveStateName != ProtagonistPresentationState.Idle)
                {
                    _freeSubFsm.RequestStateChange(ProtagonistPresentationState.Idle, forceInstantly: true);
                }

                _activeCastSkillId = 0u;
                _castRemainingSec = 0f;
            }
            finally
            {
                _forcingCastAbort = false;
            }

            FlushObservedChanges();
        }

        private StateMachine<ProtagonistPresentationMode, ProtagonistPresentationState, ProtagonistPresentationTrigger> BuildActionSubFsm()
        {
            var sub = new StateMachine<ProtagonistPresentationMode, ProtagonistPresentationState, ProtagonistPresentationTrigger>();
            sub.AddState(ProtagonistPresentationState.Idle,
                new State<ProtagonistPresentationState, ProtagonistPresentationTrigger>());
            sub.AddState(ProtagonistPresentationState.Move,
                new State<ProtagonistPresentationState, ProtagonistPresentationTrigger>());
            sub.AddState(ProtagonistPresentationState.ActionCast,
                new State<ProtagonistPresentationState, ProtagonistPresentationTrigger>(
                    onExit: _ => HandleActionCastExited(),
                    canExit: _ => _castRemainingSec <= 0f,
                    needsExitTime: true));

            sub.SetStartState(ProtagonistPresentationState.Idle);

            sub.AddTransition(ProtagonistPresentationState.Idle, ProtagonistPresentationState.Move,
                _ => _isMoving && _castRemainingSec <= 0f);
            sub.AddTransition(ProtagonistPresentationState.Move, ProtagonistPresentationState.Idle,
                _ => !_isMoving && _castRemainingSec <= 0f);
            sub.AddTransition(ProtagonistPresentationState.Idle, ProtagonistPresentationState.ActionCast,
                _ => _castRemainingSec > 0f);
            sub.AddTransition(ProtagonistPresentationState.Move, ProtagonistPresentationState.ActionCast,
                _ => _castRemainingSec > 0f);
            // Move 先于 Idle：演出收尾当帧按当前是否移动直接落入目标步态。
            sub.AddTransition(ProtagonistPresentationState.ActionCast, ProtagonistPresentationState.Move,
                _ => _isMoving);
            sub.AddTransition(ProtagonistPresentationState.ActionCast, ProtagonistPresentationState.Idle,
                _ => !_isMoving);

            return sub;
        }

        private StateMachine<ProtagonistPresentationMode, ProtagonistPresentationState, ProtagonistPresentationTrigger> GetActiveActionSubFsm()
            => CurrentMode switch
            {
                ProtagonistPresentationMode.Free => _freeSubFsm,
                ProtagonistPresentationMode.Combat => _combatSubFsm,
                _ => null,
            };

        private void HandleActionCastExited()
        {
            if (_preservingCastAcrossModeSwitch) return;

            uint skillId = _activeCastSkillId;
            bool completed = !_forcingCastAbort && _castRemainingSec <= 0f;
            _activeCastSkillId = 0u;
            _castRemainingSec = 0f;

            if (skillId != 0u)
            {
                _pendingCastEnded = (true, skillId, completed);
            }
        }

        private void HandleServerStateChanged(SelfActorStateSnapshot _, SelfActorStateSnapshot next)
            => ApplyServerState(in next);

        private void FlushObservedChanges()
        {
            var prevMode = _lastEmittedMode;
            var currentMode = CurrentMode;
            if (prevMode != currentMode)
            {
                _lastEmittedMode = currentMode;
                OnModeChanged?.Invoke(prevMode, currentMode);
            }

            var prevState = _lastEmittedState;
            var currentState = CurrentState;
            if (prevState != currentState)
            {
                _lastEmittedState = currentState;
                OnStateChanged?.Invoke(prevState, currentState);
            }

            if (_pendingCastEnded.hasValue)
            {
                var ended = _pendingCastEnded;
                _pendingCastEnded = default;
                OnCastPresentationEnded?.Invoke(ended.skillId, ended.completed);
            }
        }
    }
}
