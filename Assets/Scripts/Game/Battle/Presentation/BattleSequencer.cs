using System;
using System.Collections.Generic;
using FairyGUI;

namespace MmorpgClient.Game.Battle.Presentation
{
    /// <summary>
    /// 回合演出序列器:按 <see cref="TurnPlan"/> 的拍序列串行计时,拍内的并行轨
    /// (多目标同时受击、特效、飘字)由订阅者在 OnBeatStart 里自行用 GTween 铺开,
    /// 本类只负责"什么时候进下一拍"。
    ///
    ///  - 计时基于 FairyGUI GTween(Plugins/FairyGUI/Runtime/Tween),realtime
    ///    (SetIgnoreEngineTimeScale)—— 战斗 UI 不受 Time.timeScale 影响;
    ///  - Skip():立即把剩余拍全部"开始+结束"跑完(订阅者据此把终态一次性应用),然后 OnFinished;
    ///  - Abort():静默终止(观战抢占/关屏),不触发 OnFinished,只触发 OnAborted;
    ///  - 驱动方(BattleUiRoot)在 OnFinished 里调 BattleClient.AckTurnPlayed()。
    ///
    /// 注意:GTween 只在 Application.isPlaying 下由 TweenManager 自建的 TweenEngine 驱动,
    /// EditMode 里不会走时;拍序列的纯逻辑(合并/时长)由 TurnPlan 承担并在 EditMode 测。
    /// </summary>
    public sealed class BattleSequencer
    {
        /// <summary>单拍下限(秒):即便 SpeedScale 很大,也保证一帧以上让表现有机会铺开。</summary>
        public const float MinBeatSeconds = 0.05f;

        /// <summary>(beat, index) 拍开始。</summary>
        public event Action<Beat, int> OnBeatStart;
        /// <summary>(beat, index) 拍结束(时长到 / Skip)。</summary>
        public event Action<Beat, int> OnBeatEnd;
        /// <summary>整段播完(含 Skip 跑完)。</summary>
        public event Action OnFinished;
        /// <summary>被 Abort 终止。</summary>
        public event Action OnAborted;

        public bool IsRunning { get; private set; }
        public Beat CurrentBeat { get; private set; }
        public int CurrentIndex { get; private set; } = -1;
        public int BeatCount => _beats.Count;

        /// <summary>播放速度倍率(自动战斗 1.5,预算压缩时更高);仅影响之后开始的拍。</summary>
        public float SpeedScale
        {
            get => _speedScale;
            set => _speedScale = value > 0.01f ? value : 0.01f;
        }

        private readonly List<Beat> _beats = new List<Beat>();
        private GTweener _timer;
        private int _runId;          // 每次 Run/Abort 递增,旧计时回调据此作废
        private float _speedScale = 1f;

        /// <summary>开始播放(会先 Abort 上一段)。空序列立即 OnFinished。</summary>
        public void Run(IEnumerable<Beat> beats)
        {
            Abort();
            _beats.Clear();
            if (beats != null)
            {
                foreach (var beat in beats)
                {
                    if (beat != null) _beats.Add(beat);
                }
            }

            int runId = ++_runId;
            IsRunning = true;
            CurrentIndex = -1;
            CurrentBeat = null;
            Advance(runId);
        }

        public void Run(TurnPlan plan) => Run(plan?.Beats);

        /// <summary>跳过剩余表现:把当前拍与其后所有拍立刻结束,然后 OnFinished。</summary>
        public void Skip()
        {
            if (!IsRunning) return;
            int runId = _runId;
            KillTimer();

            // 当前拍先结束
            if (CurrentBeat != null)
            {
                var beat = CurrentBeat;
                int index = CurrentIndex;
                CurrentBeat = null;
                Invoke(OnBeatEnd, beat, index);
                if (runId != _runId) return; // 回调里 Run/Abort 了
            }

            // 剩余拍:开始+结束连跳(订阅者据此把终态一次性应用)
            for (int i = CurrentIndex + 1; i < _beats.Count; i++)
            {
                CurrentIndex = i;
                var beat = _beats[i];
                CurrentBeat = beat;
                Invoke(OnBeatStart, beat, i);
                if (runId != _runId) return;
                CurrentBeat = null;
                Invoke(OnBeatEnd, beat, i);
                if (runId != _runId) return;
            }

            Finish(runId);
        }

        /// <summary>静默终止:不再有任何拍回调,不触发 OnFinished。</summary>
        public void Abort()
        {
            if (!IsRunning) return;
            KillTimer();
            _runId++;
            IsRunning = false;
            CurrentBeat = null;
            try { OnAborted?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // ── 内部推进 ──────────────────────────────────────────

        private void Advance(int runId)
        {
            if (runId != _runId || !IsRunning) return;

            CurrentIndex++;
            if (CurrentIndex >= _beats.Count)
            {
                Finish(runId);
                return;
            }

            var beat = _beats[CurrentIndex];
            int index = CurrentIndex;
            CurrentBeat = beat;
            Invoke(OnBeatStart, beat, index);
            if (runId != _runId) return; // 回调里 Run/Abort 了

            // 非末拍扣掉尾段重叠(Death 倒地渐隐时下一拍已可起手),再按倍率缩放
            float seconds = TurnPlan.EffectiveSeconds(beat, index == _beats.Count - 1) / _speedScale;
            if (seconds < MinBeatSeconds) seconds = MinBeatSeconds;

            _timer = GTween.DelayedCall(seconds)
                .SetIgnoreEngineTimeScale(true)
                .SetTarget(this)
                .OnComplete(() =>
                {
                    if (runId != _runId) return;
                    _timer = null;
                    CurrentBeat = null;
                    Invoke(OnBeatEnd, beat, index);
                    Advance(runId);
                });
        }

        private void Finish(int runId)
        {
            if (runId != _runId) return;
            IsRunning = false;
            CurrentBeat = null;
            try { OnFinished?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        private void KillTimer()
        {
            if (_timer != null)
            {
                _timer.Kill();
                _timer = null;
            }
        }

        /// <summary>订阅者异常不允许打断序列(否则 BattleClient 会卡死在 Resolving 等 Ack)。</summary>
        private static void Invoke(Action<Beat, int> handler, Beat beat, int index)
        {
            if (handler == null) return;
            try { handler(beat, index); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }
    }
}
