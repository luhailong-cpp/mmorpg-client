using System;

namespace MmorpgClient.Net
{
    /// <summary>一条 gate 连接的心跳时序。调用者持锁；所有时刻均为单调时钟毫秒。</summary>
    public sealed class GateHeartbeat
    {
        private bool _announced;
        private uint _intervalMs, _timeoutMs;
        private double _lastInboundMs, _nextSendMs, _lastSendMs;
        private ulong _sequence;

        public bool Enabled => _intervalMs != 0;
        public double? RoundTripMs { get; private set; }

        /// <summary>只接受第一次成功验签的通告；重复验签不能重置判活期限或增加发送频率。</summary>
        public bool Start(uint intervalMs, uint timeoutMs, double nowMs)
        {
            if (_announced) return false;
            if (intervalMs != 0 && timeoutMs <= intervalMs)
                throw new ArgumentException("invalid gate heartbeat announcement");
            _announced = true;
            _intervalMs = intervalMs;
            _timeoutMs = timeoutMs;
            _lastInboundMs = _nextSendMs = nowMs;
            return Enabled;
        }

        public void NoteInbound(double nowMs)
        {
            if (Enabled) _lastInboundMs = nowMs;
        }

        public bool Expired(double nowMs) => Enabled && nowMs - _lastInboundMs >= _timeoutMs;

        /// <summary>迟到时只发一条，不追补停顿期间错过的节拍。</summary>
        public bool TrySend(double nowMs, out ulong sequence)
        {
            sequence = 0;
            if (!Enabled || Expired(nowMs) || nowMs < _nextSendMs) return false;
            _nextSendMs = nowMs + _intervalMs;
            _lastSendMs = nowMs;
            sequence = ++_sequence;
            return true;
        }

        public int WaitMilliseconds(double nowMs)
        {
            if (!Enabled) return System.Threading.Timeout.Infinite;
            double deadline = Math.Min(_nextSendMs, _lastInboundMs + _timeoutMs);
            return (int)Math.Max(1, Math.Min(int.MaxValue, Math.Ceiling(deadline - nowMs)));
        }

        public void NoteResponse(ulong sequence, double nowMs)
        {
            // 迟到/未知 seq 仍证明 gate 活着，但不能拿来算本次心跳的 RTT。
            if (_sequence != 0 && sequence == _sequence)
                RoundTripMs = Math.Max(0, nowMs - _lastSendMs);
        }
    }
}
