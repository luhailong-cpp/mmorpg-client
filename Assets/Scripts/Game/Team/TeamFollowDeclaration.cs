namespace MmorpgClient.Game.Team
{
    /// <summary>这一帧玩家对组队跟随的意愿。</summary>
    public enum TeamFollowWish
    {
        /// <summary>
        /// 不在跟随范围内:没有队伍、自己是队长、队伍视图不自洽,或眼下不允许跟随(未进游戏 / 战斗画面 / 视图未知)。
        /// 什么都不发;成员关系变了时服务器自己停止驱动。
        /// </summary>
        None,
        /// <summary>在队里、不是队长、没暂离:要服务器带着自己走。</summary>
        Follow,
        /// <summary>在队里、不是队长、选了暂离:要服务器停止驱动。</summary>
        Away,
    }

    /// <summary>
    /// 什么时候发跟随声明(TeamFollowC2S)、发 true 还是 false。纯逻辑,不碰网络与 UnityEngine:
    /// <see cref="TeamFollowDriver"/> 每帧问一次 <see cref="Decide"/>,真发出去之后调 <see cref="Sent"/> 记账
    /// (没发出去就不调,下一帧再问)。
    ///
    /// 跟随状态只认服务器(TeamFollowStateS2C 的 driven):这里只决定"该不该再说一遍"。
    ///   - 跟随:每次入场(sceneEntry 变了)立刻声明一次 —— 跨节点换场景时服务器那一侧是新实体,原来的声明不在了;
    ///     同节点时声明还在,重复一次无害(服务器回一条当前状态)。已经被驱动就不说(包括服务器自己开的跟随,
    ///     如挂机月卡托管开始时)。声明过仍没被驱动(队长不在这个场景、服务器这次没接上),按 <see cref="TeamRegroupPolicy"/>
    ///     隔一段再说;队长不在线时不重试。
    ///   - 暂离:切到暂离时立刻撤回一次;之后仍被驱动(撤回与服务器那边开的跟随在路上交错)就每
    ///     <see cref="RetractRetrySeconds"/> 秒再撤一次。
    ///   - 从暂离切回跟随(点「归队」):不等下一次入场,立刻声明。
    /// </summary>
    public sealed class TeamFollowDeclaration
    {
        /// <summary>暂离中仍被服务器驱动时,两次撤回的最小间隔(撤回的回包通常几十毫秒内就到)。</summary>
        public const float RetractRetrySeconds = 1f;

        private readonly TeamRegroupPolicy _retry = new TeamRegroupPolicy();
        // 本次入场已经声明过跟随(或已经被驱动,不必再说)。
        private bool _declared;
        private int _declaredEntry;
        private bool _retractPending;
        private float _nextRetractAt = float.NegativeInfinity;

        /// <summary>
        /// 这一帧要发的声明:null = 不发;true / false = 发 TeamFollowC2S{follow}。
        /// </summary>
        /// <param name="wish">玩家此刻的意愿(<see cref="TeamFollowWish"/>)。</param>
        /// <param name="driven">服务器此刻是否在驱动本地角色(GameClient.TeamFollowDriven)。</param>
        /// <param name="leaderOnline">队伍快照里队长在线。</param>
        /// <param name="sceneEntry">入场次数(GameClient.SceneEntryEpoch),变了就说明换过一次场景。</param>
        /// <param name="now">单调时钟秒数。</param>
        public bool? Decide(TeamFollowWish wish, bool driven, bool leaderOnline, int sceneEntry, float now)
        {
            switch (wish)
            {
                case TeamFollowWish.Follow:
                    if (driven)
                    {
                        _retry.Tick(false, leaderOnline, now);
                        _declared = true;
                        _declaredEntry = sceneEntry;
                        return null;
                    }
                    if (!_declared || _declaredEntry != sceneEntry) return true;
                    return _retry.Tick(true, leaderOnline, now) ? true : (bool?)null;
                case TeamFollowWish.Away:
                    _retry.Tick(false, leaderOnline, now);
                    if (_retractPending) return false;
                    return driven && now >= _nextRetractAt ? false : (bool?)null;
                default:
                    _retry.Tick(false, leaderOnline, now);
                    return null;
            }
        }

        /// <summary><see cref="Decide"/> 给出的声明真的发出去了。</summary>
        public void Sent(bool follow, int sceneEntry, float now)
        {
            if (follow)
            {
                _declared = true;
                _declaredEntry = sceneEntry;
                _retry.NoteRequested(now);
                return;
            }
            _retractPending = false;
            _declared = false;
            _nextRetractAt = now + RetractRetrySeconds;
        }

        /// <summary>玩家切换了暂离:切到暂离 → 撤回一次;切回跟随 → 立刻声明(不等下一次入场)。</summary>
        public void AwayChanged(bool away)
        {
            if (away)
            {
                _retractPending = true;
                return;
            }
            _retractPending = false;
            _declared = false;
        }

        /// <summary>换队 / 离队时调用:上一支队伍的声明记账不带过去。</summary>
        public void Reset()
        {
            _retry.Reset();
            _declared = false;
            _declaredEntry = 0;
            _retractPending = false;
            _nextRetractAt = float.NegativeInfinity;
        }

        /// <summary>
        /// 自己是不是这支队伍里该跟随的队员:有队伍、自己不是队长,且名单里有自己也有队长
        /// (视图不自洽时不跟,等下一份视图)。排第几由服务器按入队顺序定,客户端不算。
        /// </summary>
        public static bool IsFollower(TeamSnapshot snapshot, ulong localPlayerId)
        {
            if (snapshot == null || snapshot.TeamId == 0 || localPlayerId == 0) return false;
            if (snapshot.LeaderId == 0 || snapshot.LeaderId == localPlayerId || snapshot.Members == null) return false;
            bool selfListed = false;
            bool leaderListed = false;
            foreach (var member in snapshot.Members)
            {
                if (member == null) continue;
                if (member.PlayerId == localPlayerId) selfListed = true;
                if (member.PlayerId == snapshot.LeaderId) leaderListed = true;
            }
            return selfListed && leaderListed;
        }
    }
}
