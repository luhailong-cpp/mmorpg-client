namespace MmorpgClient.Game.Team
{
    /// <summary>
    /// 什么时候请求归队(服务端把队员瞬移到队长脚下,或换到队长所在的场景;规则在服务端 player_team.h)。纯逻辑,
    /// 不碰网络与 UnityEngine:<see cref="TeamFollowDriver"/> 每帧问它一次,要请求时由宿主交给 TeamClient.RequestRegroup。
    ///
    /// 两种来源:
    ///   - 玩家点「归队」(从暂离回来):立刻请求一次(连点只算一次)。
    ///   - 跟随中看不到队长(走散、卡住掉队、队长换了线 / 场景):先等 <see cref="OutOfSightGraceSeconds"/> 秒,
    ///     进场 / 瞬移后的视野重建、队长在视野边缘进进出出都在这段时间里自己恢复;仍看不到再请求,
    ///     之后一直看不到就每 <see cref="RetrySeconds"/> 秒再请求一次(队长跨节点 / 跨区时服务端不动人,只是空跑一次读)。
    /// 队长不在线时不自动请求:服务端没有可对齐的位置。
    /// </summary>
    public sealed class TeamRegroupPolicy
    {
        public const float OutOfSightGraceSeconds = 1.5f;
        public const float RetrySeconds = 5f;
        public const float ManualCooldownSeconds = 1f;

        private bool _tracking;
        private float _nextAutoAt;
        private float _lastRequestAt = float.NegativeInfinity;

        /// <summary>
        /// 每帧调用一次。返回 true 表示这一帧要请求归队。
        /// </summary>
        /// <param name="leaderLost">在队里、没暂离、本地角色已就绪,却看不到队长。</param>
        /// <param name="leaderOnline">队伍快照里队长在线。</param>
        /// <param name="now">单调时钟秒数。</param>
        public bool Tick(bool leaderLost, bool leaderOnline, float now)
        {
            if (!leaderLost || !leaderOnline)
            {
                _tracking = false;
                return false;
            }
            if (!_tracking)
            {
                _tracking = true;
                // 刚手动请求过:这一次的结果还没回来,不另起一次。
                float earliest = _lastRequestAt + RetrySeconds;
                _nextAutoAt = now + OutOfSightGraceSeconds > earliest ? now + OutOfSightGraceSeconds : earliest;
            }
            if (now < _nextAutoAt) return false;
            _nextAutoAt = now + RetrySeconds;
            _lastRequestAt = now;
            return true;
        }

        /// <summary>玩家点了「归队」。返回 true 表示要请求(冷却内的连点返回 false)。</summary>
        public bool Manual(float now)
        {
            if (now < _lastRequestAt + ManualCooldownSeconds) return false;
            _lastRequestAt = now;
            // 接下来要是还看不到队长,自动请求从这次之后再隔一个重试间隔。
            if (_tracking) _nextAutoAt = now + RetrySeconds;
            return true;
        }

        /// <summary>换队 / 离队 / 会话重置时调用。</summary>
        public void Reset()
        {
            _tracking = false;
            _nextAutoAt = 0f;
            _lastRequestAt = float.NegativeInfinity;
        }
    }
}
