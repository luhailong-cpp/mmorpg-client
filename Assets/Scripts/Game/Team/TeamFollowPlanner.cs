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

        // ── 游标(队员记着自己走到轨迹的哪儿了,见带 cursor 的 Plan)────────────────
        /// <summary>
        /// 每帧里程最多后退"人这一帧实际挪动的距离 + 它"。折返点附近去程与回程叠在一起,允许多退的话,人会因为
        /// 厘米级的远近差别一帧被判到这一趟、下一帧被判到那一趟 —— 两趟的转向点一个朝折返点、一个朝队长,
        /// 两帧一个来回,人就卡在那里(2026-10-13 联机验收与离线随机扫描都抓到过)。
        /// </summary>
        public const float CursorBackSlack = 0.1f;
        /// <summary>每帧在上一帧里程往前多远的范围内找自己(不越过自己的槽位)。一帧最多走 0.5,留出折返处的跳跃与掉帧的余量。</summary>
        public const float CursorAhead = 2f;
        /// <summary>
        /// 队长后来又从身边走过(折返、绕圈回来)时,那一段更新的轨迹离自己不超过它就跳过去,
        /// 等于抄掉队长绕的那一圈。只往前跳;取小是为了不隔着一堵薄墙跳到另一侧。
        /// </summary>
        public const float JumpLateral = 0.5f;
        /// <summary>游标窗口内几处差不多一样近时(折返点附近)的判同容差:取里程离上一帧最近的,保持连续。</summary>
        public const float TrackTieTolerance = 0.05f;
        /// <summary>
        /// 重新在整条轨迹上找自己时的判同容差:几段相差不超过它就取离队首最近的一段。
        /// 远端角色的显示位置带着厘米级的摆动,来回走过同一条线的几趟不会严丝合缝;
        /// 容差太小,"最近的一段"会被摆动决定。
        /// </summary>
        public const float AcquireTieTolerance = 0.25f;
        /// <summary>
        /// 前视点沿轨迹的距离比直线距离长出这么多,就认定轨迹在前面折了回来(折返点),游标直接跳到前视点。
        /// 必须大于直角拐弯的差值(LookAhead × (1 - cos45°) ≈ 0.23),否则普通拐弯也会被当成折返而多削一截角。
        /// </summary>
        public const float FoldShortcut = 0.4f;

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

            float behind = trail.DistanceBehindHead(selfPosition, out float lateral);
            return Decide(trail, slot, moving, behind, lateral);
        }

        /// <summary>
        /// 这一帧的移动指令,带着"自己走到轨迹的哪儿了"的游标。逐帧驱动本地角色时必须用这一个:
        /// 不带游标的重载每次都在整条轨迹上重新找最近的一段,队长沿原路折返 / 来回走同一条街时轨迹有几段
        /// 重叠,选中的段会在新旧两趟之间来回跳,而两趟的行进方向相反 —— 队员就在原地来回踱步
        /// (2026-10-13 真实客户端联机验收抓到:队长向北走一段、原路走回、再拐弯,约一半的概率卡住)。
        ///
        /// 规则:
        ///   1. 有游标:只在上一帧里程附近找自己 —— 往回最多"这一帧实际挪动的距离 + CursorBackSlack",
        ///      往前最多 CursorAhead 且不越过自己的槽位。里程只会往前跳、往回只随人真的往回走,所以不会在叠在
        ///      一起的两趟之间来回;
        ///   2. 例外:更靠近队首、但还没过自己槽位的某一段(里程在窗口之后)离自己不超过 JumpLateral 时跳过去 ——
        ///      队长折返后从身边走过、或绕一圈回来,队员不必把那一圈走完;
        ///   3. 没有游标 / 轨迹重起过 / 窗口里已经找不到自己(偏离超过 OffTrailDistance):在整条轨迹上重新找,
        ///      几段差不多近时取离队首最近的。
        /// </summary>
        /// <param name="cursor">
        /// 由本方法逐帧推进。调用方第一次传 default;自己不再沿轨迹走的时候(暂离、被寻路带离、换队长)
        /// 重新置为 default。
        /// </param>
        public static TeamFollowCommand Plan(TeamFollowTrail trail, int slot, Vector3 selfPosition, bool moving,
            ref TeamFollowCursor cursor)
        {
            if (trail == null || !trail.HasHead || slot <= 0)
            {
                cursor = default;
                return new TeamFollowCommand(TeamFollowCommandKind.None);
            }

            double head = trail.HeadOdometer;
            float slotDistance = Mathf.Min(slot, MaxFollowers) * SlotSpacing;
            double odometer = 0d;
            float lateral = float.PositiveInfinity;
            bool tracked = false;
            if (cursor.Valid && cursor.Epoch == trail.Epoch)
            {
                // 停步回拉把末端收回后,队首里程可能退到游标之前。
                double hint = System.Math.Min(cursor.Odometer, head);
                // 往回只随人的实际位移挪;往前最多跳 CursorAhead,而且最远到自己的槽位 —— 轨迹折过来时,槽位旁边
                // 可能就站着队长(更新的那一段),跳过头会被当成"站到了队长脚下"、再沿轨迹往回退,来回不止。
                // 站得比槽位靠前(要退回去)时,往前一步也不跳。
                float moved = Horizontal(selfPosition, cursor.Position) + CursorBackSlack;
                double slotOdometer = head - slotDistance;
                double to = System.Math.Min(hint + CursorAhead, System.Math.Max(slotOdometer + CursorBackSlack, hint + moved));
                tracked = trail.TryProject(selfPosition, hint - moved, to, hint, TrackTieTolerance, out odometer, out lateral) &&
                          lateral <= OffTrailDistance;
                if (tracked && slotOdometer + CursorBackSlack > hint + CursorAhead &&
                    trail.TryProject(selfPosition, hint + CursorAhead, slotOdometer + CursorBackSlack, double.PositiveInfinity,
                        TrackTieTolerance, out double later, out float laterLateral) &&
                    laterLateral <= JumpLateral)
                {
                    odometer = later;
                    lateral = laterLateral;
                }
            }
            if (!tracked &&
                !trail.TryProject(selfPosition, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity,
                    AcquireTieTolerance, out odometer, out lateral))
            {
                // HasHead 时队首本身总是候选,走不到这里;留着是为了不带着未赋值的里程往下算。
                cursor = default;
                return new TeamFollowCommand(TeamFollowCommandKind.None);
            }

            // 折返点:转向点取在"沿轨迹往前(或往回)LookAhead"处,而轨迹在那儿折了回来时,那一点绕过折返点正好落回
            // 自己脚下(离折返点约 LookAhead / 2 处是个不动点)——人原地站住、或两帧一个来回,再也走不出去。
            // 往前追与往回退(站得比槽位靠前,沿轨迹退回去;后排队员在队长短距离掉头后常见)是同一个问题。
            // 沿轨迹要走的路比直线长出 FoldShortcut 以上,说明自己其实已经站在折过来的那一段旁边了:把游标直接挪到
            // 那一段上自己脚下那一点,下一段转向点就落在折过来的那一段上。直角拐弯不会触发(沿轨迹 0.8、直线至少 0.57,只差 0.23)。
            float behind = (float)(head - odometer);
            for (int fold = 0; fold < 2; fold++)
            {
                bool forward = behind > slotDistance;
                float aim = forward ? Mathf.Max(slotDistance, behind - LookAhead) : Mathf.Min(slotDistance, behind + LookAhead);
                float arc = Mathf.Abs(behind - aim);
                if (arc <= FoldShortcut) break;
                Vector3 target = trail.PointBehindHead(aim);
                float straight = Horizontal(target, selfPosition);
                if (arc - straight < FoldShortcut) break;
                // 落到折过来那一段上自己脚下那一点(转向点沿轨迹往回 straight 处附近);找不到就落在转向点上。
                double folded = head - aim;
                double nearFoot = forward ? folded - straight : folded + straight;
                double from = forward ? nearFoot - CursorBackSlack : folded;
                double to = forward ? folded : nearFoot + CursorBackSlack;
                if (trail.TryProject(selfPosition, from, to, nearFoot, TrackTieTolerance, out double foot, out float footLateral) &&
                    (forward ? foot > odometer : foot < odometer))
                {
                    odometer = foot;
                    lateral = footLateral;
                }
                else
                {
                    odometer = folded;
                    lateral = straight;
                }
                behind = (float)(head - odometer);
            }

            cursor = new TeamFollowCursor(trail.Epoch, odometer, selfPosition);
            return Decide(trail, slot, moving, behind, lateral);
        }

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>已知"落后队首多少弧长、偏离轨迹多远"之后的决策;两个 Plan 重载共用。</summary>
        private static TeamFollowCommand Decide(TeamFollowTrail trail, int slot, bool moving,
            float behind, float lateral)
        {
            float slotDistance = Mathf.Min(slot, MaxFollowers) * SlotSpacing;
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
