using System;
using System.Collections.Generic;

namespace MmorpgClient.Game.PlayerFeatures
{
    /// <summary>
    /// 当前任务快照上的会话内显示偏好。只记任务身份，不复制进度，也不接取、交付或领取任务。
    /// 由 PlayerFeaturesClient 在权威全量回包后同步；断线、换角后清空。
    /// </summary>
    public sealed class MissionTrackingState
    {
        private readonly List<ulong> _tracked = new List<ulong>();
        private readonly IReadOnlyCollection<ulong> _readOnlyTracked;
        private GetMissionListResponse _snapshot;

        public MissionTrackingState() => _readOnlyTracked = _tracked.AsReadOnly();

        public event Action OnChanged;
        public IReadOnlyCollection<ulong> TrackedKeys => _readOnlyTracked;
        public ulong CurrentKey { get; private set; }
        public bool AutoTrackingAvailable => false;
        public string AutoTrackingUnavailableReason => "自动追踪规则尚未开放，可手动选择任务";

        public static ulong Key(PlayerMissionInfo mission) => mission == null ? 0 :
            ((ulong)mission.Scope << 32) | mission.MissionId;

        public static bool CanTrack(PlayerMissionInfo mission) => mission != null && mission.MissionId != 0 &&
            (mission.Status == PlayerMissionStatus.PlayerMissionActive ||
             mission.Status == PlayerMissionStatus.PlayerMissionClaimable);

        public bool IsTracked(ulong key) => key != 0 && _tracked.Contains(key);

        /// <summary>只接受当前权威快照仍允许追踪的身份；null兼容原单任务取消当前追踪事件。</summary>
        public bool Track(PlayerMissionInfo mission)
        {
            if (mission == null) return Untrack(CurrentKey);
            ulong key = Key(mission);
            if (!CanTrack(Find(key))) return false;
            bool changed = CurrentKey != key;
            if (!_tracked.Contains(key)) { _tracked.Add(key); changed = true; }
            if (!changed) return false;
            // 最近一次手选放在末尾，使取消当前后能回到此前明确手选的任务。
            _tracked.Remove(key);
            _tracked.Add(key);
            CurrentKey = key;
            OnChanged?.Invoke();
            return true;
        }

        public bool Toggle(PlayerMissionInfo mission)
        {
            if (mission == null) return Track(null);
            ulong key = Key(mission);
            return IsTracked(key) ? Untrack(key) : Track(mission);
        }

        public bool Untrack(ulong key)
        {
            if (!_tracked.Remove(key)) return false;
            if (CurrentKey == key) CurrentKey = _tracked.Count == 0 ? 0 : _tracked[_tracked.Count - 1];
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>新全量快照只清失效引用，不自动追踪新任务或按目标是否达成推导任务状态。</summary>
        public void UpdateSnapshot(GetMissionListResponse snapshot)
        {
            _snapshot = snapshot;
            bool changed = false;
            for (int i = _tracked.Count - 1; i >= 0; --i)
                if (!CanTrack(Find(_tracked[i]))) { _tracked.RemoveAt(i); changed = true; }
            if (CurrentKey != 0 && !_tracked.Contains(CurrentKey))
            {
                CurrentKey = _tracked.Count == 0 ? 0 : _tracked[_tracked.Count - 1];
                changed = true;
            }
            if (changed) OnChanged?.Invoke();
        }

        /// <summary>清除快照引用及当前主体偏好；新主体必须等待自己的权威快照。</summary>
        public void Clear()
        {
            _snapshot = null;
            bool changed = _tracked.Count != 0 || CurrentKey != 0;
            _tracked.Clear();
            CurrentKey = 0;
            if (changed) OnChanged?.Invoke();
        }

        private PlayerMissionInfo Find(ulong key)
        {
            if (key == 0 || _snapshot == null) return null;
            foreach (var mission in _snapshot.Missions)
                if (Key(mission) == key) return mission;
            return null;
        }
    }
}
