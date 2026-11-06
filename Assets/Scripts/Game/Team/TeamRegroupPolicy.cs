namespace MmorpgClient.Game.Team
{
    /// <summary>
    /// 声明了跟随、服务器却还没在带着走时,隔多久再声明一次(TeamFollowC2S{follow=true})。纯逻辑,不碰网络与
    /// UnityEngine:<see cref="TeamFollowDeclaration"/> 每帧问它一次。服务器收到声明会重新走一遍跟随检查
    /// (服务端 player_team.cpp):队长在本节点别的场景就换过去,同一场景就接到队长身后(远了直接放进槽位)。
    ///
    /// 节奏:没被驱动后先等 <see cref="OutOfSightGraceSeconds"/> 秒(入场 / 瞬移后的视野重建、服务器那次检查还在路上,
    /// 都在这段时间里自己恢复);仍没被驱动再声明,之后一直没被驱动就每 <see cref="RetrySeconds"/> 秒再声明一次
    /// (队长跨节点 / 跨区、战斗中时服务器不动人,只是空跑一次读)。刚声明过(<see cref="NoteRequested"/>)则从那次起算。
    /// 队长不在线时不自动重试:服务器没有可对齐的位置。
    /// </summary>
    public sealed class TeamRegroupPolicy
    {
        public const float OutOfSightGraceSeconds = 1.5f;
        public const float RetrySeconds = 5f;

        private bool _tracking;
        private float _nextAutoAt;
        private float _lastRequestAt = float.NegativeInfinity;

        /// <summary>
        /// 每帧调用一次。返回 true 表示这一帧要再声明一次。
        /// </summary>
        /// <param name="leaderLost">想跟随、已经声明过,服务器却没在带着走。</param>
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
                // 刚声明过:这一次的结果还没回来,不另起一次。
                float earliest = _lastRequestAt + RetrySeconds;
                _nextAutoAt = now + OutOfSightGraceSeconds > earliest ? now + OutOfSightGraceSeconds : earliest;
            }
            if (now < _nextAutoAt) return false;
            _nextAutoAt = now + RetrySeconds;
            _lastRequestAt = now;
            return true;
        }

        /// <summary>别处刚声明过一次(入场后的那次、点「归队」):接下来的自动重试从这次之后再隔一个间隔。</summary>
        public void NoteRequested(float now)
        {
            _lastRequestAt = now;
            if (_tracking) _nextAutoAt = now + RetrySeconds;
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
