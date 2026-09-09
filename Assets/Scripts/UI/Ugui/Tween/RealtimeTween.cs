using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Tweening
{
    /// <summary>战斗 UI 使用的轻量缓动曲线。</summary>
    public enum RealtimeEase
    {
        Linear,
        QuadIn,
        QuadOut,
        QuadInOut,
        CubicOut,
        SineInOut,
        BackOut
    }

    public delegate void RealtimeTweenCallback();
    public delegate void RealtimeTweenCallback1(RealtimeTweener tweener);

    /// <summary>
    /// 单个实时 tween。默认使用 unscaled delta time，因此 UI 动画不受游戏 Time.timeScale 影响。
    /// </summary>
    public sealed class RealtimeTweener
    {
        internal enum TweenKind
        {
            DelayedCall,
            Float,
            Shake
        }

        private readonly TweenKind _kind;
        private readonly Vector4 _startValue;
        private readonly Vector4 _endValue;
        private readonly float _shakeAmplitude;
        private float _duration;
        private float _delay;
        private float _elapsed;
        private int _repeat;
        private bool _yoyo;
        private bool _useUnscaledTime = true;
        private bool _started;
        private bool _killed;
        private bool _completed;
        private bool _manuallyPaused;
        private int _snapshotPauseDepth;
        private RealtimeEase _ease = RealtimeEase.QuadOut;
        private object _target;
        private RealtimeTweenCallback _onStart;
        private RealtimeTweenCallback1 _onUpdate;
        private RealtimeTweenCallback _onComplete;

        internal RealtimeTweener(TweenKind kind, Vector4 startValue, Vector4 endValue, float duration, float shakeAmplitude = 0f)
        {
            _kind = kind;
            _startValue = startValue;
            _endValue = endValue;
            _duration = Mathf.Max(0f, duration);
            _shakeAmplitude = Mathf.Max(0f, shakeAmplitude);
            Value = startValue;
        }

        public Vector4 Value { get; private set; }
        public object Target => _target;
        public bool Completed => _completed;
        public bool IsActive => !_killed && !_completed;
        public bool IsPaused => _manuallyPaused || _snapshotPauseDepth > 0;

        public RealtimeTweener SetDelay(float seconds)
        {
            _delay = Mathf.Max(0f, seconds);
            return this;
        }

        public RealtimeTweener SetDuration(float seconds)
        {
            _duration = Mathf.Max(0f, seconds);
            return this;
        }

        public RealtimeTweener SetEase(RealtimeEase ease)
        {
            _ease = ease;
            return this;
        }

        /// <param name="times">额外播放次数；-1 表示无限循环。</param>
        public RealtimeTweener SetRepeat(int times, bool yoyo = false)
        {
            _repeat = times;
            _yoyo = yoyo;
            return this;
        }

        public RealtimeTweener SetIgnoreEngineTimeScale(bool ignore)
        {
            _useUnscaledTime = ignore;
            return this;
        }

        public RealtimeTweener SetTarget(object target)
        {
            _target = target;
            return this;
        }

        public RealtimeTweener OnStart(RealtimeTweenCallback callback)
        {
            _onStart = callback;
            return this;
        }

        public RealtimeTweener OnUpdate(RealtimeTweenCallback1 callback)
        {
            _onUpdate = callback;
            return this;
        }

        public RealtimeTweener OnComplete(RealtimeTweenCallback callback)
        {
            _onComplete = callback;
            return this;
        }

        public RealtimeTweener SetPaused(bool paused)
        {
            _manuallyPaused = paused;
            return this;
        }

        /// <summary>终止且不触发完成回调。</summary>
        public void Kill()
        {
            _killed = true;
        }

        internal void PauseForSnapshot()
        {
            if (IsActive) _snapshotPauseDepth++;
        }

        internal void ResumeFromSnapshot()
        {
            if (_snapshotPauseDepth > 0) _snapshotPauseDepth--;
        }

        internal bool Tick(float unscaledDeltaTime, float scaledDeltaTime)
        {
            if (!IsActive) return false;
            if (IsPaused) return true;

            float deltaTime = _useUnscaledTime ? unscaledDeltaTime : scaledDeltaTime;
            if (deltaTime <= 0f) return true;
            _elapsed += deltaTime;

            if (_kind == TweenKind.DelayedCall)
            {
                if (_elapsed < _delay) return true;
                Complete();
                return false;
            }

            if (!_started)
            {
                if (_elapsed < _delay) return true;
                _started = true;
                InvokeSafely(_onStart, "start");
                if (_killed) return false;
            }

            bool ended;
            float normalized = EvaluateNormalized(_elapsed - _delay, out ended);
            if (_kind == TweenKind.Shake)
            {
                if (ended)
                {
                    Value = _startValue;
                }
                else
                {
                    UnityEngine.Vector3 random = UnityEngine.Random.insideUnitSphere;
                    random.x = random.x >= 0f ? 1f : -1f;
                    random.y = random.y >= 0f ? 1f : -1f;
                    random.z = random.z >= 0f ? 1f : -1f;
                    random *= _shakeAmplitude * (1f - normalized);
                    Value = _startValue + new Vector4(random.x, random.y, random.z, 0f);
                }
            }
            else
            {
                Value = Vector4.LerpUnclamped(_startValue, _endValue, EvaluateEase(_ease, normalized));
            }

            InvokeSafely(_onUpdate, this, "update");
            if (_killed) return false;
            if (!ended) return true;

            Complete();
            return false;
        }

        private float EvaluateNormalized(float activeSeconds, out bool ended)
        {
            if (_duration <= 0f)
            {
                ended = true;
                return _yoyo && _repeat > 0 && (_repeat & 1) == 1 ? 0f : 1f;
            }

            activeSeconds = Mathf.Max(0f, activeSeconds);
            if (_repeat >= 0)
            {
                float totalDuration = _duration * (_repeat + 1);
                if (activeSeconds >= totalDuration)
                {
                    ended = true;
                    return _yoyo && (_repeat & 1) == 1 ? 0f : 1f;
                }
            }

            ended = false;
            int cycle = Mathf.FloorToInt(activeSeconds / _duration);
            float local = activeSeconds - cycle * _duration;
            float normalized = Mathf.Clamp01(local / _duration);
            return _yoyo && (cycle & 1) == 1 ? 1f - normalized : normalized;
        }

        private void Complete()
        {
            if (!IsActive) return;
            _completed = true;
            InvokeSafely(_onComplete, "complete");
            _killed = true;
        }

        private static float EvaluateEase(RealtimeEase ease, float t)
        {
            switch (ease)
            {
                case RealtimeEase.Linear:
                    return t;
                case RealtimeEase.QuadIn:
                    return t * t;
                case RealtimeEase.QuadOut:
                    return -t * (t - 2f);
                case RealtimeEase.QuadInOut:
                    if ((t *= 2f) < 1f) return 0.5f * t * t;
                    t -= 1f;
                    return -0.5f * (t * (t - 2f) - 1f);
                case RealtimeEase.CubicOut:
                    t -= 1f;
                    return t * t * t + 1f;
                case RealtimeEase.SineInOut:
                    return -0.5f * (Mathf.Cos(Mathf.PI * t) - 1f);
                case RealtimeEase.BackOut:
                    const float overshoot = 1.70158f;
                    t -= 1f;
                    return t * t * ((overshoot + 1f) * t + overshoot) + 1f;
                default:
                    return t;
            }
        }

        private static void InvokeSafely(RealtimeTweenCallback callback, string phase)
        {
            if (callback == null) return;
            try { callback(); }
            catch (Exception exception) { Debug.LogException(new Exception($"Realtime tween {phase} callback failed.", exception)); }
        }

        private static void InvokeSafely(RealtimeTweenCallback1 callback, RealtimeTweener tweener, string phase)
        {
            if (callback == null) return;
            try { callback(tweener); }
            catch (Exception exception) { Debug.LogException(new Exception($"Realtime tween {phase} callback failed.", exception)); }
        }
    }

    /// <summary>
    /// 一次暂停快照。只暂停创建快照时已经活跃的 tween；之后创建的 tween 不受影响。
    /// 支持嵌套快照，每个快照仅释放自己的暂停层级。
    /// </summary>
    public sealed class RealtimeTweenPauseSnapshot : IDisposable
    {
        private List<RealtimeTweener> _tweens;

        internal RealtimeTweenPauseSnapshot(List<RealtimeTweener> tweens)
        {
            _tweens = tweens;
        }

        public int Count => _tweens?.Count ?? 0;

        public void Resume()
        {
            var tweens = _tweens;
            if (tweens == null) return;
            _tweens = null;
            for (int i = 0; i < tweens.Count; i++) tweens[i]?.ResumeFromSnapshot();
        }

        public void Dispose()
        {
            Resume();
        }
    }

    /// <summary>
    /// 无第三方依赖的 Unity tween 调度器。一个隐藏的 DontDestroyOnLoad 组件在 Update 中驱动所有实例。
    /// </summary>
    public static partial class RealtimeTween
    {
        private static readonly List<RealtimeTweener> Active = new List<RealtimeTweener>();
        private static RealtimeTweenRunner s_runner;
        private static bool s_quitting;

        public static RealtimeTweener To(float startValue, float endValue, float duration)
        {
            return Add(new RealtimeTweener(
                RealtimeTweener.TweenKind.Float,
                new Vector4(startValue, 0f, 0f, 0f),
                new Vector4(endValue, 0f, 0f, 0f),
                duration));
        }

        public static RealtimeTweener DelayedCall(float delay)
        {
            return Add(new RealtimeTweener(
                RealtimeTweener.TweenKind.DelayedCall,
                Vector4.zero,
                Vector4.zero,
                0f).SetDelay(delay));
        }

        public static RealtimeTweener Shake(UnityEngine.Vector3 startValue, float amplitude, float duration)
        {
            return Add(new RealtimeTweener(
                RealtimeTweener.TweenKind.Shake,
                new Vector4(startValue.x, startValue.y, startValue.z, 0f),
                Vector4.zero,
                duration,
                amplitude).SetEase(RealtimeEase.Linear));
        }

        public static void Kill(object target)
        {
            if (target == null) return;
            for (int i = 0; i < Active.Count; i++)
            {
                var tweener = Active[i];
                if (tweener != null && ReferenceEquals(tweener.Target, target)) tweener.Kill();
            }
        }

        public static bool IsTweening(object target)
        {
            if (target == null) return false;
            for (int i = 0; i < Active.Count; i++)
            {
                var tweener = Active[i];
                if (tweener != null && tweener.IsActive && ReferenceEquals(tweener.Target, target)) return true;
            }
            return false;
        }

        public static RealtimeTweenPauseSnapshot PauseSnapshot()
        {
            var paused = new List<RealtimeTweener>(Active.Count);
            for (int i = 0; i < Active.Count; i++)
            {
                var tweener = Active[i];
                if (tweener == null || !tweener.IsActive) continue;
                tweener.PauseForSnapshot();
                paused.Add(tweener);
            }
            return new RealtimeTweenPauseSnapshot(paused);
        }

        internal static void UpdateAll(float unscaledDeltaTime, float scaledDeltaTime)
        {
            int initialCount = Active.Count;
            for (int i = 0; i < initialCount; i++)
            {
                var tweener = Active[i];
                if (tweener != null && tweener.IsActive) tweener.Tick(unscaledDeltaTime, scaledDeltaTime);
            }

            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var tweener = Active[i];
                if (tweener == null || !tweener.IsActive) Active.RemoveAt(i);
            }
        }

        internal static void RunnerDestroyed(RealtimeTweenRunner runner)
        {
            if (ReferenceEquals(s_runner, runner)) s_runner = null;
        }

        internal static void MarkQuitting()
        {
            s_quitting = true;
        }

        private static RealtimeTweener Add(RealtimeTweener tweener)
        {
            EnsureRunner();
            Active.Add(tweener);
            return tweener;
        }

        private static void EnsureRunner()
        {
            if (s_runner != null || s_quitting || !Application.isPlaying) return;
            var go = new GameObject("[RealtimeTweenRunner]") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            s_runner = go.AddComponent<RealtimeTweenRunner>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Active.Clear();
            s_runner = null;
            s_quitting = false;
        }
    }

    [DefaultExecutionOrder(-10000)]
    internal sealed class RealtimeTweenRunner : MonoBehaviour
    {
        private void Update()
        {
            RealtimeTween.UpdateAll(Time.unscaledDeltaTime, Time.deltaTime);
        }

        private void OnApplicationQuit()
        {
            RealtimeTween.MarkQuitting();
        }

        private void OnDestroy()
        {
            RealtimeTween.RunnerDestroyed(this);
        }
    }
}
