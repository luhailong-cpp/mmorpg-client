using System;

namespace MmorpgClient.Net
{
    public enum CircuitState : byte
    {
        Closed = 0,
        Open,
        HalfOpen,
    }

    /// <summary>
    /// 标准三态（Closed / Open / HalfOpen）轻量客户端熔断器。
    /// 仅统计下游服务故障或超时；严禁将限流（1008）或正常业务拒绝计入失败。
    /// </summary>
    public sealed class SimpleCircuitBreaker
    {
        private readonly int _failureThreshold;
        private readonly float _openDurationSeconds;
        private int _failures;
        // 供外层传输 Ok 后、内层业务回包再发现下游故障时衔接连续计数，不丢前序失败。
        private int _failuresBeforeLastSuccess;
        private float _openUntil = float.NegativeInfinity;
        private bool _probeInFlight;

        public SimpleCircuitBreaker(int failureThreshold, float openDurationSeconds)
        {
            if (failureThreshold <= 0) throw new ArgumentOutOfRangeException(nameof(failureThreshold));
            if (openDurationSeconds <= 0f || float.IsNaN(openDurationSeconds) || float.IsInfinity(openDurationSeconds))
                throw new ArgumentOutOfRangeException(nameof(openDurationSeconds));
            _failureThreshold = failureThreshold;
            _openDurationSeconds = openDurationSeconds;
        }

        public CircuitState GetState(float now)
        {
            if (_probeInFlight) return CircuitState.HalfOpen;
            if (_failures >= _failureThreshold)
                return now < _openUntil ? CircuitState.Open : CircuitState.HalfOpen;
            return CircuitState.Closed;
        }

        /// <summary>只读检查当前是否允许发起请求（Open 或 HalfOpen 已有探测在途时返回 false）。</summary>
        public bool CanAcquire(float now) => !_probeInFlight && now >= _openUntil;

        /// <summary>尝试放行一次请求；冷却结束后首次放行锁定 HalfOpen 槽位直到本次请求结算。</summary>
        public bool TryAcquire(float now)
        {
            if (!CanAcquire(now)) return false;
            if (_failures >= _failureThreshold) _probeInFlight = true;
            return true;
        }

        public void RecordSuccess()
        {
            _failuresBeforeLastSuccess = _failures;
            _failures = 0;
            _openUntil = float.NegativeInfinity;
            _probeInFlight = false;
        }

        public void RecordFailure(float now)
        {
            _probeInFlight = false;
            _failuresBeforeLastSuccess = 0;
            if (_failures < _failureThreshold) _failures++;
            if (_failures >= _failureThreshold) _openUntil = now + _openDurationSeconds;
        }

        /// <summary>外层传输成功但业务体携带下游故障时调用：衔接紧前一次 RecordSuccess 前的失败计数。</summary>
        public void RecordInnerFault(float now)
        {
            _probeInFlight = false;
            if (_failures < _failuresBeforeLastSuccess) _failures = _failuresBeforeLastSuccess;
            _failuresBeforeLastSuccess = 0;
            if (_failures < _failureThreshold) _failures++;
            if (_failures >= _failureThreshold) _openUntil = now + _openDurationSeconds;
        }

        /// <summary>探测因限流（1008）或非故障业务拒绝未触达下游，释放 HalfOpen 槽位但不计成败。</summary>
        public void ReleaseProbe() => _probeInFlight = false;

        public void Reset()
        {
            _failures = 0;
            _failuresBeforeLastSuccess = 0;
            _openUntil = float.NegativeInfinity;
            _probeInFlight = false;
        }
    }
}
