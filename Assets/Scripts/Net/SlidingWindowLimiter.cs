using System;
using System.Collections.Generic;

namespace MmorpgClient.Net
{
    /// <summary>
    /// 客户端内存滑动窗口限流器，与服务端 gate 按消息号的滑动窗口口径一致。
    /// <see cref="CanAcquire"/> 只读检查（供界面置灰，不消耗配额）；<see cref="TryAcquire"/> 在真正发包时记账。
    /// </summary>
    public sealed class SlidingWindowLimiter
    {
        private readonly int _maxCount;
        private readonly float _windowSeconds;
        private readonly Queue<float> _timestamps;

        public SlidingWindowLimiter(int maxCount, float windowSeconds)
        {
            if (maxCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
            if (windowSeconds <= 0f || float.IsNaN(windowSeconds) || float.IsInfinity(windowSeconds))
                throw new ArgumentOutOfRangeException(nameof(windowSeconds));
            _maxCount = maxCount;
            _windowSeconds = windowSeconds;
            _timestamps = new Queue<float>(maxCount);
        }

        public int MaxCount => _maxCount;
        public float WindowSeconds => _windowSeconds;

        /// <summary>当前时刻是否还有配额；不消耗额度。</summary>
        public bool CanAcquire(float now)
        {
            Evict(now);
            return _timestamps.Count < _maxCount;
        }

        /// <summary>尝试消耗一次发送配额；超限返回 false。</summary>
        public bool TryAcquire(float now)
        {
            if (!CanAcquire(now)) return false;
            _timestamps.Enqueue(now);
            return true;
        }

        public void Clear() => _timestamps.Clear();

        private void Evict(float now)
        {
            while (_timestamps.Count > 0 && now >= _timestamps.Peek() + _windowSeconds)
                _timestamps.Dequeue();
        }
    }
}
