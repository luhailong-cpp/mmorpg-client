using MmorpgClient.Game.Team;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class TeamFollowPlannerTests
    {
        private const float Tolerance = 0.001f;
        private const ulong Leader = 100;

        // ── 槽位 ────────────────────────────────────────────────────────────

        [Test]
        public void SlotOf_WithoutATeam_IsZero()
        {
            Assert.That(TeamFollowPlanner.SlotOf(null, 1), Is.EqualTo(0));
            Assert.That(TeamFollowPlanner.SlotOf(new TeamSnapshot(), 1), Is.EqualTo(0));
            Assert.That(TeamFollowPlanner.SlotOf(Team((Leader, 1), (1, 2)), 0), Is.EqualTo(0));
        }

        [Test]
        public void SlotOf_TheLeader_IsZero()
        {
            Assert.That(TeamFollowPlanner.SlotOf(Team((Leader, 1), (1, 2)), Leader), Is.EqualTo(0));
        }

        [Test]
        public void SlotOf_SomeoneNotOnTheRoster_IsZero()
        {
            Assert.That(TeamFollowPlanner.SlotOf(Team((Leader, 1), (1, 2)), 999), Is.EqualTo(0));
        }

        [Test]
        public void SlotOf_WhenTheLeaderIsMissingFromTheRoster_IsZero()
        {
            // 视图不自洽(队长编号指向名单外的人):不跟,等下一份视图。
            var snapshot = Team((1, 1), (2, 2));
            snapshot.LeaderId = Leader;

            Assert.That(TeamFollowPlanner.SlotOf(snapshot, 1), Is.EqualTo(0));
        }

        [Test]
        public void SlotOf_OrdersFollowersByJoinSeq_SkippingTheLeader()
        {
            // 队长是后来转让得到的(JoinSeq 3),不占跟随槽位。
            var snapshot = Team((Leader, 3), (11, 1), (12, 2), (13, 5), (14, 4));

            Assert.That(TeamFollowPlanner.SlotOf(snapshot, 11), Is.EqualTo(1));
            Assert.That(TeamFollowPlanner.SlotOf(snapshot, 12), Is.EqualTo(2));
            Assert.That(TeamFollowPlanner.SlotOf(snapshot, 14), Is.EqualTo(3));
            Assert.That(TeamFollowPlanner.SlotOf(snapshot, 13), Is.EqualTo(4));
        }

        [Test]
        public void SlotOf_DoesNotDependOnListOrder()
        {
            var snapshot = Team((13, 5), (12, 2), (Leader, 3), (14, 4), (11, 1));

            Assert.That(TeamFollowPlanner.SlotOf(snapshot, 11), Is.EqualTo(1));
            Assert.That(TeamFollowPlanner.SlotOf(snapshot, 13), Is.EqualTo(4));
        }

        [Test]
        public void SlotOf_EqualJoinSeq_BreaksTiesByPlayerId()
        {
            var snapshot = Team((Leader, 1), (22, 7), (21, 7));

            Assert.That(TeamFollowPlanner.SlotOf(snapshot, 21), Is.EqualTo(1));
            Assert.That(TeamFollowPlanner.SlotOf(snapshot, 22), Is.EqualTo(2));
        }

        // ── 规划 ────────────────────────────────────────────────────────────

        [Test]
        public void Plan_WithoutTrailOrSlot_IsNone()
        {
            var trail = StraightTrail();

            Assert.That(TeamFollowPlanner.Plan(null, 1, Vector3.zero, false).Kind, Is.EqualTo(TeamFollowCommandKind.None));
            Assert.That(TeamFollowPlanner.Plan(new TeamFollowTrail(50f), 1, Vector3.zero, false).Kind,
                Is.EqualTo(TeamFollowCommandKind.None));
            Assert.That(TeamFollowPlanner.Plan(trail, 0, Vector3.zero, false).Kind, Is.EqualTo(TeamFollowCommandKind.None));
        }

        [Test]
        public void Plan_StandingOnTheSlot_Holds()
        {
            var command = TeamFollowPlanner.Plan(StraightTrail(), 1, new Vector3(8.6f, 0f, 0f), false);

            Assert.That(command.Kind, Is.EqualTo(TeamFollowCommandKind.Hold));
        }

        [Test]
        public void Plan_FarBehindOnTheTrail_SteersAlongItAndCatchesUp()
        {
            // 队首在 x=10,槽位 1 在 8.6;自己在 5,落后 3.6。
            var command = TeamFollowPlanner.Plan(StraightTrail(), 1, new Vector3(5f, 0f, 0f), true);

            Assert.That(command.Kind, Is.EqualTo(TeamFollowCommandKind.Steer));
            TeamFollowTrailTests.AssertPoint(command.Point, 5f + TeamFollowPlanner.LookAhead, 0f);
            Assert.That(command.SpeedScale, Is.EqualTo(TeamFollowPlanner.MaxSpeedScale).Within(Tolerance));
        }

        [Test]
        public void Plan_CloseBehindTheSlot_AimsExactlyAtTheSlotAtNormalSpeed()
        {
            // 落后 0.2:不到追赶门槛(0.3),常速走完最后一小段。
            var command = TeamFollowPlanner.Plan(StraightTrail(), 1, new Vector3(8.4f, 0f, 0f), true);

            Assert.That(command.Kind, Is.EqualTo(TeamFollowCommandKind.Steer));
            TeamFollowTrailTests.AssertPoint(command.Point, 8.6f, 0f);
            Assert.That(command.SpeedScale, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void Plan_SmallGap_KeepsWalkingButDoesNotRestart()
        {
            // 离槽位 0.4:走着的继续走到位;已经站定的不为这点距离重新起步(每次起停都是一对网络包)。
            var position = new Vector3(8.2f, 0f, 0f);

            Assert.That(TeamFollowPlanner.Plan(StraightTrail(), 1, position, true).Kind, Is.EqualTo(TeamFollowCommandKind.Steer));
            Assert.That(TeamFollowPlanner.Plan(StraightTrail(), 1, position, false).Kind, Is.EqualTo(TeamFollowCommandKind.Hold));
        }

        [Test]
        public void Plan_StandingOnTheLeader_BacksOffAlongTheTrailToItsSlot()
        {
            // 刚被服务端带到队长脚下:沿轨迹退回自己的槽位,而不是叠在队长身上等他走开。
            var command = TeamFollowPlanner.Plan(StraightTrail(), 2, new Vector3(10f, 0f, 0f), false);

            Assert.That(command.Kind, Is.EqualTo(TeamFollowCommandKind.Steer));
            TeamFollowTrailTests.AssertPoint(command.Point, 10f - TeamFollowPlanner.LookAhead, 0f);
            Assert.That(command.SpeedScale, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void Plan_FarOffTheTrail_RoutesToTheSlot()
        {
            var command = TeamFollowPlanner.Plan(StraightTrail(), 1, new Vector3(5f, 0f, 5f), false);

            Assert.That(command.Kind, Is.EqualTo(TeamFollowCommandKind.Route));
            TeamFollowTrailTests.AssertPoint(command.Point, 8.6f, 0f);
        }

        [Test]
        public void Plan_BesideTheSlot_StillWalksOntoTheTrail()
        {
            // 弧长对齐但站在轨迹旁边一米:没有到位。
            var command = TeamFollowPlanner.Plan(StraightTrail(), 1, new Vector3(8.6f, 0f, 1f), false);

            Assert.That(command.Kind, Is.EqualTo(TeamFollowCommandKind.Steer));
            TeamFollowTrailTests.AssertPoint(command.Point, 8.6f, 0f);
        }

        [Test]
        public void Plan_SlotBeyondTheFormation_IsTreatedAsTheLastSlot()
        {
            float last = TeamFollowPlanner.MaxFollowers * TeamFollowPlanner.SlotSpacing;

            var command = TeamFollowPlanner.Plan(StraightTrail(), 9, new Vector3(10f - last, 0f, 0f), false);

            Assert.That(command.Kind, Is.EqualTo(TeamFollowCommandKind.Hold));
        }

        [Test]
        public void PlanToPoint_HoldsWithinToleranceAndRoutesOtherwise()
        {
            var target = new Vector3(4f, 0f, 4f);

            // 寻路走到终点时离目标还有约 0.34(路径到达半径):站定后不能为这点距离再起步。
            Assert.That(TeamFollowPlanner.PlanToPoint(target, new Vector3(4.34f, 0f, 4f), false).Kind,
                Is.EqualTo(TeamFollowCommandKind.Hold));
            var far = TeamFollowPlanner.PlanToPoint(target, new Vector3(9f, 0f, 4f), false);
            Assert.That(far.Kind, Is.EqualTo(TeamFollowCommandKind.Route));
            TeamFollowTrailTests.AssertPoint(far.Point, 4f, 4f);
            // 走着的时候用更紧的停步容差:0.34 还不算到。
            Assert.That(TeamFollowPlanner.PlanToPoint(target, new Vector3(4.34f, 0f, 4f), true).Kind,
                Is.EqualTo(TeamFollowCommandKind.Route));
            Assert.That(TeamFollowPlanner.PlanToPoint(target, new Vector3(4.1f, 0f, 4f), true).Kind,
                Is.EqualTo(TeamFollowCommandKind.Hold));
        }

        // ── 走一遍 ──────────────────────────────────────────────────────────
        // 这几条是规则层面的模拟:步进照抄 TianyongPlayerController.FollowStep 的截短规则,
        // "是否在走"用上一帧的指令代替控制器的 IsMoving;与 TeamFollowDriver 一样每个队员带着自己的游标。
        // 控制器自己的分支(被掩码挡住、寻路、服务器吸附)不在这里,要在 Unity 里联机验。

        [Test]
        public void Simulation_LeaderTurnsBack_FollowersNeverEndUpAheadOfIt()
        {
            const float dt = 1f / 60f;
            const float speed = 9f;
            var trail = new TeamFollowTrail(80f);
            var leader = new Vector3(0f, 0f, 0f);
            trail.Push(leader);
            var followers = new[] { leader, leader, leader };
            var moving = new bool[3];
            var cursors = new TeamFollowCursor[3];

            // 向东 12 米,停 1 秒,再沿原路向西 10 米,停 2 秒。
            float worstLead = 0f;
            for (int frame = 0; frame < 60 * 6; frame++)
            {
                float t = frame * dt;
                float travelled = Mathf.Min(t * speed, 12f);
                float back = Mathf.Clamp((t - 12f / speed - 1f) * speed, 0f, 10f);
                leader = new Vector3(travelled - back, 0f, 0f);
                bool leaderStanding = (t * speed >= 12f && back <= 0f) || back >= 10f;
                trail.Push(leader, allowRetract: leaderStanding);

                for (int i = 0; i < followers.Length; i++)
                {
                    var command = TeamFollowPlanner.Plan(trail, i + 1, followers[i], moving[i], ref cursors[i]);
                    moving[i] = command.Kind == TeamFollowCommandKind.Steer;
                    if (!moving[i]) continue;
                    var delta = command.Point - followers[i];
                    float distance = delta.magnitude;
                    float step = Mathf.Min(speed * command.SpeedScale * dt, distance);
                    if (distance > 0f) followers[i] += delta / distance * step;
                }

                // 回程中:队员跑到队长西边(他要去的方向)多远。队长从队伍中间穿过时短暂为正是正常的,
                // 但不能像"回程吃掉轨迹"那样一路被赶着跑在前面。
                if (back > 3f)
                    foreach (var follower in followers) worstLead = Mathf.Max(worstLead, leader.x - follower.x);
            }

            Assert.That(worstLead, Is.LessThan(0.5f));
            // 最后队长停在 x=2,三个人依次排在他东边。
            for (int i = 0; i < followers.Length; i++)
                Assert.That(followers[i].x, Is.EqualTo(2f + 1.4f * (i + 1)).Within(TeamFollowPlanner.StartTolerance));
        }


        [Test]
        public void Simulation_FollowerSettlesOnItsSlotAfterAnLShapedWalk()
        {
            const float dt = 1f / 60f;
            const float speed = 9f;
            const int slot = 2;
            var trail = new TeamFollowTrail(80f);
            var leader = new Vector3(0f, 0f, 0f);
            var follower = leader;
            bool moving = false;
            var cursor = default(TeamFollowCursor);
            int restartsAfterLeaderStopped = 0;
            float worstLateral = 0f;
            trail.Push(leader);

            // 队长先向 +x 走 12,再向 +z 走 9,然后停住;之后再给 3 秒让队员收拢。
            int walkFrames = Mathf.RoundToInt(21f / (speed * dt));
            for (int frame = 0; frame < walkFrames + 180; frame++)
            {
                bool leaderWalking = frame < walkFrames;
                if (leaderWalking)
                {
                    float travelled = (frame + 1) * speed * dt;
                    leader = travelled <= 12f ? new Vector3(travelled, 0f, 0f) : new Vector3(12f, 0f, Mathf.Min(travelled - 12f, 9f));
                }
                trail.Push(leader);

                var command = TeamFollowPlanner.Plan(trail, slot, follower, moving, ref cursor);
                Assert.That(command.Kind, Is.Not.EqualTo(TeamFollowCommandKind.Route), "全程都在轨迹上,不该触发寻路");
                bool wasMoving = moving;
                moving = command.Kind == TeamFollowCommandKind.Steer;
                if (moving)
                {
                    // 与 TianyongPlayerController.FollowStep 同一条规则:最后一步截短,不冲过转向点。
                    var delta = command.Point - follower;
                    delta.y = 0f;
                    float distance = delta.magnitude;
                    float step = Mathf.Min(speed * command.SpeedScale * dt, distance);
                    if (distance > 0f) follower += delta / distance * step;
                }
                if (!leaderWalking && moving && !wasMoving) restartsAfterLeaderStopped++;

                trail.DistanceBehindHead(follower, out float lateral);
                if (frame > 30) worstLateral = Mathf.Max(worstLateral, lateral);
            }

            float behind = trail.DistanceBehindHead(follower, out float finalLateral);
            Assert.That(moving, Is.False, "队长停下 3 秒后队员应当已经站定");
            Assert.That(behind, Is.EqualTo(slot * TeamFollowPlanner.SlotSpacing).Within(TeamFollowPlanner.StopTolerance + Tolerance));
            Assert.That(finalLateral, Is.LessThanOrEqualTo(TeamFollowPlanner.StopTolerance + Tolerance));
            // 前视 1.2 米取转向点,拐角处最多削掉一小截;削得太多说明在抄近路而不是跟着走。
            Assert.That(worstLateral, Is.LessThan(0.6f));
            Assert.That(restartsAfterLeaderStopped, Is.EqualTo(0), "队长停下后队员走到位就该站住,不能反复起步");
        }

        [Test]
        public void Simulation_FollowerThatFellBehindCatchesUpWhileTheLeaderKeepsWalking()
        {
            const float dt = 1f / 60f;
            const float speed = 9f;
            var trail = TeamFollowTrailTests.Walk(new TeamFollowTrail(80f), new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            var leader = new Vector3(10f, 0f, 0f);
            // 槽位 1 应在队长身后 1.4;起步时落后了 6 米(刚从后台切回来)。
            var follower = new Vector3(10f - 1.4f - 6f, 0f, 0f);
            bool moving = false;
            var cursor = default(TeamFollowCursor);

            // 队长继续匀速走 20 秒。追赶速度只比队长快 10%,每秒收回 0.9 米。
            float gapAfterTwoSeconds = float.NaN;
            int stops = 0;
            for (int frame = 0; frame < 20 * 60; frame++)
            {
                leader += new Vector3(speed * dt, 0f, 0f);
                trail.Push(leader);
                var command = TeamFollowPlanner.Plan(trail, 1, follower, moving, ref cursor);
                if (moving && command.Kind != TeamFollowCommandKind.Steer) stops++;
                moving = command.Kind == TeamFollowCommandKind.Steer;
                if (moving)
                {
                    var delta = command.Point - follower;
                    float distance = delta.magnitude;
                    float step = Mathf.Min(speed * command.SpeedScale * dt, distance);
                    if (distance > 0f) follower += delta / distance * step;
                }
                if (frame == 2 * 60 - 1)
                    gapAfterTwoSeconds = trail.DistanceBehindHead(follower, out _) - TeamFollowPlanner.SlotSpacing;
            }

            // 头两秒确实在以 +10% 的速度追:6 - 0.9 x 2 = 4.2。
            Assert.That(gapAfterTwoSeconds, Is.EqualTo(4.2f).Within(0.3f));
            float gap = trail.DistanceBehindHead(follower, out _) - TeamFollowPlanner.SlotSpacing;
            // 追到"落后不超过 CatchUpStart"就恢复常速,此后与队长同速、不再拉近。
            Assert.That(gap, Is.LessThanOrEqualTo(TeamFollowPlanner.CatchUpStart + 0.2f));
            // 队长一直在走,队员就该一直在走:追赶门槛若压到停步容差附近,会变成"追上-站住-落后-再起步"。
            Assert.That(stops, Is.EqualTo(0));
        }

        [Test]
        public void Simulation_LeaderDoublesBackAlongAWobblyLineThenTurns_FollowerDoesNotPaceBackAndForth()
        {
            // 2026-10-13 真实客户端联机验收抓到的现场:队长向北走 13.5、原路走回、再向东拐。
            // 远端角色的显示位置带着厘米级的摆动,去程和回程不会严丝合缝(这里各加 6 厘米、相位相反的摆动);
            // 每帧在整条轨迹上找"最近的一段"时,选中的段在去程与回程之间来回跳,两趟行进方向相反,
            // 队员就在原地一步向北一步向南(机器人记下的上报是 v=-9.9 / +9.9 交替),再也没跟上向东走的队长。
            const float dt = 1f / 60f;
            const float speed = 9f;
            const float leg = 13.5f;
            float pause = 1.5f;
            var trail = new TeamFollowTrail(80f);
            var leader = new Vector3(0f, 0f, 0f);
            var follower = leader;
            bool moving = false;
            var cursor = default(TeamFollowCursor);
            var lastDirection = Vector3.zero;
            int reversals = 0;
            float behindAfterReturn = float.NaN;
            trail.Push(leader);

            float walk = leg / speed;
            float northEnd = walk, southStart = northEnd + pause, southEnd = southStart + walk;
            float eastStart = southEnd + pause, eastEnd = eastStart + walk;
            int frames = Mathf.RoundToInt((eastEnd + 2.5f) / dt);
            for (int frame = 0; frame < frames; frame++)
            {
                float t = (frame + 1) * dt;
                bool standing;
                if (t <= northEnd)
                {
                    float z = t * speed;
                    leader = new Vector3(Wobble(z, 0f), 0f, z);
                    standing = false;
                }
                else if (t <= southStart)
                {
                    leader = new Vector3(Wobble(leg, 0f), 0f, leg);
                    standing = true;
                }
                else if (t <= southEnd)
                {
                    float z = leg - (t - southStart) * speed;
                    leader = new Vector3(Wobble(z, Mathf.PI), 0f, z);
                    standing = false;
                }
                else if (t <= eastStart)
                {
                    leader = new Vector3(Wobble(0f, Mathf.PI), 0f, 0f);
                    standing = true;
                }
                else if (t <= eastEnd)
                {
                    leader = new Vector3((t - eastStart) * speed, 0f, 0f);
                    standing = false;
                }
                else
                {
                    leader = new Vector3(leg, 0f, 0f);
                    standing = true;
                }
                trail.Push(leader, allowRetract: standing);

                var command = TeamFollowPlanner.Plan(trail, 1, follower, moving, ref cursor);
                Assert.That(command.Kind, Is.Not.EqualTo(TeamFollowCommandKind.Route), "全程都贴着轨迹,不该触发寻路");
                moving = command.Kind == TeamFollowCommandKind.Steer;
                if (moving)
                {
                    var delta = command.Point - follower;
                    delta.y = 0f;
                    float distance = delta.magnitude;
                    if (distance > 0f)
                    {
                        var direction = delta / distance;
                        if (lastDirection != Vector3.zero && Vector3.Dot(direction, lastDirection) < 0f) reversals++;
                        lastDirection = direction;
                        follower += direction * Mathf.Min(speed * command.SpeedScale * dt, distance);
                    }
                }

                // 队长走回原点、站定之后:队员应当排在他北边(他走来的那一侧),而不是被甩在后面或冲到前面。
                if (Mathf.Abs(t - (eastStart - 2f * dt)) < dt * 0.5f) behindAfterReturn = follower.z - leader.z;
            }

            // 整个过程只有一次真正的掉头:队长折返后从身边走过,队员跟着转身。
            Assert.That(reversals, Is.LessThanOrEqualTo(2), "队员在重叠的两趟轨迹之间来回踱步");
            Assert.That(behindAfterReturn, Is.EqualTo(TeamFollowPlanner.SlotSpacing).Within(TeamFollowPlanner.StartTolerance));
            Assert.That(moving, Is.False, "队长停下 2.5 秒后队员应当已经站定");
            Assert.That(follower.x, Is.EqualTo(leg - TeamFollowPlanner.SlotSpacing).Within(TeamFollowPlanner.StartTolerance));
            Assert.That(Mathf.Abs(follower.z), Is.LessThanOrEqualTo(TeamFollowPlanner.StartTolerance));
        }

        [Test]
        public void Simulation_LeaderPacesBackAndForthOnOneLine_FollowerLinesUpBehindEveryTime()
        {
            // 队长在同一条线上来回走四趟(东 6、西 6、东 6、西 6),每趟之间站 1.5 秒;四趟的显示轨迹各带不同相位的摆动,
            // 轨迹在同一条线上叠了四层。每次站定后队员都应当停在队长走来的那一侧,整个过程只在队长折返时转身。
            const float dt = 1f / 60f;
            const float speed = 9f;
            const float leg = 6f;
            const float pause = 1.5f;
            var trail = new TeamFollowTrail(80f);
            var leader = new Vector3(0f, 0f, 0f);
            var follower = leader;
            bool moving = false;
            var cursor = default(TeamFollowCursor);
            var lastDirection = Vector3.zero;
            int reversals = 0;
            trail.Push(leader);

            float walk = leg / speed;
            for (int pass = 0; pass < 4; pass++)
            {
                bool eastward = pass % 2 == 0;
                int frames = Mathf.RoundToInt((walk + pause) / dt);
                for (int frame = 0; frame < frames; frame++)
                {
                    float t = (frame + 1) * dt;
                    float along = Mathf.Min(t * speed, leg);
                    float x = eastward ? along : leg - along;
                    bool standing = t * speed >= leg;
                    leader = new Vector3(x, 0f, Wobble(x, pass * 1.3f));
                    trail.Push(leader, allowRetract: standing);

                    var command = TeamFollowPlanner.Plan(trail, 1, follower, moving, ref cursor);
                    Assert.That(command.Kind, Is.Not.EqualTo(TeamFollowCommandKind.Route), "全程都贴着轨迹,不该触发寻路");
                    moving = command.Kind == TeamFollowCommandKind.Steer;
                    if (!moving) continue;
                    var delta = command.Point - follower;
                    delta.y = 0f;
                    float distance = delta.magnitude;
                    if (distance <= 0f) continue;
                    var direction = delta / distance;
                    if (lastDirection != Vector3.zero && Vector3.Dot(direction, lastDirection) < 0f) reversals++;
                    lastDirection = direction;
                    follower += direction * Mathf.Min(speed * command.SpeedScale * dt, distance);
                }

                // 这一趟走完并站了 1.5 秒:队员站定,在队长走来的那一侧(向东走完在西边,向西走完在东边)。
                float behind = eastward ? leader.x - follower.x : follower.x - leader.x;
                Assert.That(moving, Is.False, "第 " + (pass + 1) + " 趟站定后队员还在走");
                Assert.That(behind, Is.EqualTo(TeamFollowPlanner.SlotSpacing).Within(TeamFollowPlanner.StartTolerance),
                    "第 " + (pass + 1) + " 趟站定后队员不在队长身后");
            }

            // 三次折返各转身一次;第一趟起步时从队长脚下退到槽位再跟上,另算一次。
            Assert.That(reversals, Is.LessThanOrEqualTo(5), "队员在叠了四层的轨迹上来回踱步");
        }

        [Test]
        public void Plan_WithACursor_StaysOnItsOwnPassWhenAnOlderPassIsAHairCloser()
        {
            // 去程在 x = 0.03,回程在 x = 0;队员跟着回程走,此刻偏到了 x = 0.02(离去程 1 厘米、离回程 2 厘米)。
            var trail = new TeamFollowTrail(80f);
            TeamFollowTrailTests.Walk(trail, new Vector3(0.03f, 0f, 0f), new Vector3(0.03f, 0f, 10f));
            TeamFollowTrailTests.Walk(trail, new Vector3(0f, 0f, 10f), new Vector3(0f, 0f, 2f));
            var cursor = default(TeamFollowCursor);
            // 先让游标落在回程上(站在回程正中)。
            TeamFollowPlanner.Plan(trail, 1, new Vector3(0f, 0f, 6f), true, ref cursor);

            var command = TeamFollowPlanner.Plan(trail, 1, new Vector3(0.02f, 0f, 6.1f), true, ref cursor);

            // 回程向南(z 减小):转向点在自己南边。跳到去程上的话转向点会在北边。
            Assert.That(command.Kind, Is.EqualTo(TeamFollowCommandKind.Steer));
            Assert.That(command.Point.z, Is.LessThan(6.1f));
        }

        [Test]
        public void Plan_WithACursor_JumpsForwardWhenTheLeaderComesBackPastTheFollower()
        {
            // 队长向东走 10、原路折返;队员还在去程 x = 6 处往东走。
            var trail = new TeamFollowTrail(80f);
            TeamFollowTrailTests.Walk(trail, new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            var cursor = default(TeamFollowCursor);
            TeamFollowPlanner.Plan(trail, 1, new Vector3(6f, 0f, 0f), true, ref cursor);
            double before = cursor.Odometer;

            // 回程走到 x = 8:还没到队员身边,队员继续沿去程往东。
            TeamFollowTrailTests.Walk(trail, new Vector3(10f, 0f, 0f), new Vector3(8f, 0f, 0f));
            var ahead = TeamFollowPlanner.Plan(trail, 1, new Vector3(6f, 0f, 0f), true, ref cursor);
            Assert.That(ahead.Point.x, Is.GreaterThan(6f));
            Assert.That(cursor.Odometer, Is.EqualTo(before).Within(Tolerance));

            // 回程越过队员走到 x = 3:队员跳到回程上,不必先走到折返点;此后落后 3,转向点在西边。
            TeamFollowTrailTests.Walk(trail, new Vector3(8f, 0f, 0f), new Vector3(3f, 0f, 0f));
            var turned = TeamFollowPlanner.Plan(trail, 1, new Vector3(6f, 0f, 0f), true, ref cursor);
            Assert.That(turned.Kind, Is.EqualTo(TeamFollowCommandKind.Steer));
            Assert.That(turned.Point.x, Is.LessThan(6f));
            Assert.That(trail.HeadOdometer - cursor.Odometer, Is.EqualTo(3d).Within(Tolerance));

            // 只往前跳:此后去程不再是候选,即使它近了一丁点。
            var stays = TeamFollowPlanner.Plan(trail, 1, new Vector3(5.8f, 0f, 0f), true, ref cursor);
            Assert.That(stays.Point.x, Is.LessThan(5.8f));
        }

        [Test]
        public void Plan_WithACursorFromAnOlderTrail_StartsOver()
        {
            var trail = StraightTrail();
            var cursor = default(TeamFollowCursor);
            TeamFollowPlanner.Plan(trail, 1, new Vector3(5f, 0f, 0f), true, ref cursor);

            // 轨迹重起(队长跳变 / 暂离回来):旧里程作废,在新轨迹上重新找。
            trail.Reset(new Vector3(0f, 0f, 20f), new[] { new Vector3(0f, 0f, 19.5f), new Vector3(0f, 0f, 19f) });
            var command = TeamFollowPlanner.Plan(trail, 1, new Vector3(0f, 0f, 19.2f), false, ref cursor);

            Assert.That(cursor.Valid, Is.True);
            Assert.That(cursor.Epoch, Is.EqualTo(trail.Epoch));
            Assert.That(trail.HeadOdometer - cursor.Odometer, Is.EqualTo(0.8d).Within(Tolerance));
            Assert.That(command.Kind, Is.EqualTo(TeamFollowCommandKind.Steer));
        }

        // 远端角色显示位置的摆动:6 厘米(联机现场量到的量级:同一条线上两趟的坐标差到 0.06),沿前进方向约 2 米一个周期。
        private static float Wobble(float along, float phase) => 0.06f * Mathf.Sin(along * 3f + phase);

        private static TeamFollowTrail StraightTrail()
            => TeamFollowTrailTests.Walk(new TeamFollowTrail(50f), new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));

        private static TeamSnapshot Team(params (ulong playerId, uint joinSeq)[] members)
        {
            var snapshot = new TeamSnapshot { TeamId = 7, LeaderId = Leader };
            foreach (var (playerId, joinSeq) in members)
                snapshot.Members.Add(new TeamRole { PlayerId = playerId, JoinSeq = joinSeq, IsLeader = playerId == Leader });
            return snapshot;
        }
    }
}
