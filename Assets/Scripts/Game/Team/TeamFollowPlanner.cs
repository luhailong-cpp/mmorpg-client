using UnityEngine;

namespace MmorpgClient.Game.Team
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>跟随规划的结论:这一帧本地角色该做什么。</summary>
    public enum TeamFollowCommandKind
    {
        /// <summary>不跟随(无队、自己是队长、还没有轨迹):控制权不在跟随手里。</summary>
        None,
        /// <summary>已经站在自己的槽位上:原地不动。</summary>
        Hold,
        /// <summary>就在轨迹附近:朝 Point 直线走(Point 是轨迹上稍靠前的一点)。</summary>
        Steer,
        /// <summary>离轨迹太远(刚入队、被挡住走散):寻路去 Point(自己的槽位)。</summary>
        Route,
    }

    public readonly struct TeamFollowCommand
    {
        public readonly TeamFollowCommandKind Kind;
        public readonly Vector3 Point;
        /// <summary>移速倍率;只有落后较多时才大于 1。</summary>
        public readonly float SpeedScale;

        public TeamFollowCommand(TeamFollowCommandKind kind, Vector3 point = default, float speedScale = 1f)
        {
            Kind = kind;
            Point = point;
            SpeedScale = speedScale;
        }
    }

    /// <summary>
    /// 组队跟随的纯规则:谁站第几个、这一帧往哪走。不碰 Unity 运行时,可以在编辑器外直接单测。
    /// 队形是问道式的一路纵队:队长在最前,队员按入队先后排在身后,沿队长走过的路一个跟一个。
    /// </summary>
    public static class TeamFollowPlanner
    {
        /// <summary>相邻两人沿轨迹的间距(世界单位;角色身高 1.8、半径 0.38)。</summary>
        public const float SlotSpacing = 1.4f;
        /// <summary>队伍上限 5 人,最多 4 个跟随槽位。</summary>
        public const int MaxFollowers = 4;
        /// <summary>
        /// 沿轨迹往前看多远取转向点。太小会在记录点之间抖,太大拐弯会削角:直角处内切最多约
        /// 0.35 × 前视。取 0.8 时是 0.28,小于点击寻路给路径留的净空(TianyongNavigationGrid 的
        /// SmoothingClearance = 0.35),队长贴着拐角走过去的路,队员照着走不会擦进不可走的格子。
        /// </summary>
        public const float LookAhead = 0.8f;
        /// <summary>行走中:沿轨迹离槽位不超过它就停。</summary>
        public const float StopTolerance = 0.15f;
        /// <summary>
        /// 站定后:沿轨迹离槽位超过它才重新起步。比 StopTolerance 大,队长原地小幅挪动(或远端插值收尾的
        /// 抖动)不会让整队反复"起步-停步"——每一次起停都是一对网络包。
        /// </summary>
        public const float StartTolerance = 0.6f;
        /// <summary>水平偏离轨迹超过它就不再直线靠拢,改为寻路(中间可能隔着墙)。</summary>
        public const float OffTrailDistance = 2f;
        /// <summary>
        /// 落后槽位超过它就加速追赶。队长一直在走时,队员追到"落后不超过它"就恢复常速、与队长同速,
        /// 这段差距要等队长停下才收掉,所以它也是行进中队形比站定时松出来的量 —— 不能取大。
        /// </summary>
        public const float CatchUpStart = 0.3f;
        /// <summary>
        /// 追赶时的移速倍率上限。基础移速 9,乘上它是 9.9,仍在服务端的客户端速度信任上限
        /// (kMaxTrustedClientSpeed = 10)之内;再大服务端两次上报之间的积分会被截断。
        /// </summary>
        public const float MaxSpeedScale = 1.1f;

        /// <summary>
        /// 本地玩家的跟随槽位:1 表示紧跟队长,最大 MaxFollowers;0 表示不跟随
        /// (没有队伍、自己就是队长、名单里没有自己或没有队长)。
        /// 顺序取非队长成员的 JoinSeq 升序,JoinSeq 相同按玩家编号,保证每个客户端算出同一个排法。
        /// </summary>
        public static int SlotOf(TeamSnapshot snapshot, ulong localPlayerId)
        {
            if (snapshot == null || snapshot.TeamId == 0 || localPlayerId == 0) return 0;
            if (snapshot.LeaderId == 0 || snapshot.LeaderId == localPlayerId || snapshot.Members == null) return 0;

            TeamRole self = null;
            bool leaderListed = false;
            foreach (var member in snapshot.Members)
            {
                if (member == null) continue;
                if (member.PlayerId == localPlayerId) self = member;
                if (member.PlayerId == snapshot.LeaderId) leaderListed = true;
            }
            if (self == null || !leaderListed) return 0;

            int slot = 1;
            foreach (var member in snapshot.Members)
            {
                if (member == null || member.PlayerId == localPlayerId || member.PlayerId == snapshot.LeaderId) continue;
                if (member.JoinSeq < self.JoinSeq ||
                    (member.JoinSeq == self.JoinSeq && member.PlayerId < self.PlayerId)) slot++;
            }
            return Mathf.Min(slot, MaxFollowers);
        }

        /// <summary>
        /// 目标是一个固定点(而不是轨迹上的槽位)时的指令:到了就站住,没到就寻路过去。
        /// 用在槽位本身落在不可走格子里的时候 —— 调用方先把它换成最近的可走点再问这里。
        /// 寻路走到终点时离目标还有一个到达半径(约 0.34),小于起步容差,所以到了之后不会再起步。
        /// </summary>
        public static TeamFollowCommand PlanToPoint(Vector3 target, Vector3 selfPosition, bool moving)
        {
            float dx = target.x - selfPosition.x;
            float dz = target.z - selfPosition.z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);
            return distance <= (moving ? StopTolerance : StartTolerance)
                ? new TeamFollowCommand(TeamFollowCommandKind.Hold)
                : new TeamFollowCommand(TeamFollowCommandKind.Route, target);
        }

        /// <summary>
        /// 这一帧的移动指令。
        /// </summary>
        /// <param name="trail">队长的轨迹。</param>
        /// <param name="slot">SlotOf 的结果。</param>
        /// <param name="selfPosition">本地角色脚下的世界坐标。</param>
        /// <param name="moving">本地角色此刻是否在走(决定用停步还是起步的容差)。</param>
        public static TeamFollowCommand Plan(TeamFollowTrail trail, int slot, Vector3 selfPosition, bool moving)
        {
            if (trail == null || !trail.HasHead || slot <= 0) return new TeamFollowCommand(TeamFollowCommandKind.None);

            float slotDistance = Mathf.Min(slot, MaxFollowers) * SlotSpacing;
            float behind = trail.DistanceBehindHead(selfPosition, out float lateral);
            if (lateral > OffTrailDistance)
                return new TeamFollowCommand(TeamFollowCommandKind.Route, trail.PointBehindHead(slotDistance));

            // 正数 = 落在槽位后面,要往前走;负数 = 站得比槽位还靠前(刚被带到队长脚下),要沿轨迹退回去。
            float gap = behind - slotDistance;
            float tolerance = moving ? StopTolerance : StartTolerance;
            // 偏离轨迹的那一截也算进去:站在轨迹旁边一米处、弧长恰好对齐,同样没有到位。
            if (Mathf.Abs(gap) <= tolerance && lateral <= tolerance)
                return new TeamFollowCommand(TeamFollowCommandKind.Hold);

            float aim = gap > 0f
                ? Mathf.Max(slotDistance, behind - LookAhead)
                : Mathf.Min(slotDistance, behind + LookAhead);
            float speed = gap > CatchUpStart ? MaxSpeedScale : 1f;
            return new TeamFollowCommand(TeamFollowCommandKind.Steer, trail.PointBehindHead(aim), speed);
        }
    }
}
