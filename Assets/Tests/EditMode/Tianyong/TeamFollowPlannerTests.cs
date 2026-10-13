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
        // 这两条是规则层面的模拟:步进照抄 TianyongPlayerController.FollowStep 的截短规则,
        // "是否在走"用上一帧的指令代替控制器的 IsMoving。控制器自己的分支(被掩码挡住、寻路、
        // 服务器吸附)不在这里,要在 Unity 里联机验。

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
                    var command = TeamFollowPlanner.Plan(trail, i + 1, followers[i], moving[i]);
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

                var command = TeamFollowPlanner.Plan(trail, slot, follower, moving);
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

            // 队长继续匀速走 20 秒。追赶速度只比队长快 10%,每秒收回 0.9 米。
            float gapAfterTwoSeconds = float.NaN;
            int stops = 0;
            for (int frame = 0; frame < 20 * 60; frame++)
            {
                leader += new Vector3(speed * dt, 0f, 0f);
                trail.Push(leader);
                var command = TeamFollowPlanner.Plan(trail, 1, follower, moving);
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
