using System.Collections.Generic;
using MmorpgClient.Game.Team;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class TeamFollowTrailTests
    {
        // 0.25 可以被浮点精确表示,采样点正好落在 0.5 的整数倍上,断言不用猜舍入。
        private const float Step = 0.25f;
        private const float Tolerance = 0.001f;

        [Test]
        public void Push_FirstSample_BecomesHeadWithZeroLength()
        {
            var trail = new TeamFollowTrail(50f);
            Assert.That(trail.HasHead, Is.False);

            trail.Push(new Vector3(3f, 0f, 4f));

            Assert.That(trail.HasHead, Is.True);
            AssertPoint(trail.Head, 3f, 4f);
            Assert.That(trail.Length, Is.EqualTo(0f).Within(Tolerance));
            AssertPoint(trail.PointBehindHead(2f), 3f, 4f);
        }

        [Test]
        public void Push_StraightWalk_KeepsArcLengthAndPointsBehindHead()
        {
            var trail = Walk(new TeamFollowTrail(50f), new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));

            Assert.That(trail.Length, Is.EqualTo(10f).Within(Tolerance));
            AssertPoint(trail.PointBehindHead(0f), 10f, 0f);
            AssertPoint(trail.PointBehindHead(1.4f), 8.6f, 0f);
            AssertPoint(trail.PointBehindHead(5.6f), 4.4f, 0f);
        }

        [Test]
        public void PointBehindHead_BeyondTrail_ReturnsOldestPoint()
        {
            var trail = Walk(new TeamFollowTrail(50f), new Vector3(2f, 0f, 0f), new Vector3(6f, 0f, 0f));

            AssertPoint(trail.PointBehindHead(100f), 2f, 0f);
        }

        [Test]
        public void PointBehindHead_GoesAroundTheCornerInsteadOfCuttingIt()
        {
            var trail = new TeamFollowTrail(50f);
            Walk(trail, new Vector3(0f, 0f, 0f), new Vector3(5f, 0f, 0f));
            Walk(trail, new Vector3(5f, 0f, 0f), new Vector3(5f, 0f, 5f));

            Assert.That(trail.Length, Is.EqualTo(10f).Within(Tolerance));
            // 往回 7 米:先沿 z 退 5 米到拐角,再沿 x 退 2 米。直线抄近路会落在别处。
            AssertPoint(trail.PointBehindHead(7f), 3f, 0f);
            AssertPoint(trail.PointBehindHead(5f), 5f, 0f);
        }

        [Test]
        public void Push_OvershootThenPullBack_DropsTheSpike()
        {
            // 远端角色靠外推显示:队长停步时会先冲过头(到 11)再被拉回停点(10.2)。
            var trail = Walk(new TeamFollowTrail(50f), new Vector3(0f, 0f, 0f), new Vector3(11f, 0f, 0f));
            trail.Push(new Vector3(10.75f, 0f, 0f), allowRetract: true);
            trail.Push(new Vector3(10.5f, 0f, 0f), allowRetract: true);
            trail.Push(new Vector3(10.25f, 0f, 0f), allowRetract: true);
            trail.Push(new Vector3(10.2f, 0f, 0f), allowRetract: true);

            // 不收掉尖角的话弧长会是 11 + 0.8 = 11.8,队员要走到 11 再折回来。
            Assert.That(trail.Length, Is.EqualTo(10.2f).Within(Tolerance));
            AssertPoint(trail.PointBehindHead(1.4f), 8.8f, 0f);
        }

        [Test]
        public void Push_LeaderTurnsBackAlongItsOwnPath_KeepsFollowersBehindIt()
        {
            // 队长向东走 10 米,再沿同一条线往回走 8 米(桥面、窄街上只能这样走)。
            // 这是带着速度的真掉头,不是停步回拉:轨迹必须折成发夹留着。
            var trail = Walk(new TeamFollowTrail(50f), new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Walk(trail, new Vector3(10f, 0f, 0f), new Vector3(2f, 0f, 0f));

            Assert.That(trail.Length, Is.EqualTo(18f).Within(Tolerance));
            // 队长此刻在 x=2 往西走;四个槽位都必须在他东边(身后),不能落到他要去的西边。
            for (int slot = 1; slot <= 4; slot++)
                AssertPoint(trail.PointBehindHead(1.4f * slot), 2f + 1.4f * slot, 0f);
        }

        [Test]
        public void Push_LeaderWalksIntoTheSeededTail_DoesNotEatIt()
        {
            // 刚看到队长时在他背后(-Z)铺了尾巴,队员排在上面;队长第一步恰好朝 -Z 走。
            var tail = new List<Vector3>();
            for (int i = 1; i <= 11; i++) tail.Add(new Vector3(0f, 0f, -0.5f * i));
            var trail = new TeamFollowTrail(50f);
            trail.Reset(new Vector3(0f, 0f, 0f), tail);

            Walk(trail, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, -3f));

            // 尾巴原样留着,队长走过的 3 米接在后面:总长 5.5 + 3。
            Assert.That(trail.Length, Is.EqualTo(8.5f).Within(Tolerance));
            // 紧跟队长的槽位在他身后(北边)1.4 处,而不是被推到他前面。
            AssertPoint(trail.PointBehindHead(1.4f), 0f, -1.6f);
        }

        [Test]
        public void Push_RetractIsCapped_SoALongWalkBackIsNeverSwallowed()
        {
            // 即使调用方一直传 allowRetract(比如速度读数恰好为 0),往回 5 米也只能剪掉 3 米,其余折成发夹。
            var trail = Walk(new TeamFollowTrail(50f), new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            for (float x = 9.75f; x >= 5f - 0.001f; x -= 0.25f) trail.Push(new Vector3(x, 0f, 0f), allowRetract: true);

            // 末端从 10 剪回到 7,之后 7 → 5 这 2 米是折回来的新路。
            Assert.That(trail.Length, Is.EqualTo(7f + 2f).Within(Tolerance));
            AssertPoint(trail.PointBehindHead(2.5f), 6.5f, 0f);
        }

        [Test]
        public void Push_LongOvershootPulledBackInOneJump_IsCutAtTheStopPoint()
        {
            // 联机验收量到的现场:停步消息晚到,队长的显示位置冲过头 1.6(一帧跳出一长段),随后被拉回停点。
            // 旧做法逐点收、上限 1.5,这一截收不干净,队员把它算进距离、停得离队长只有 0.3。
            var trail = Walk(new TeamFollowTrail(50f), new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            trail.Push(new Vector3(11.6f, 0f, 0f), allowRetract: true);
            trail.Push(new Vector3(11.0f, 0f, 0f), allowRetract: true);
            trail.Push(new Vector3(10.3f, 0f, 0f), allowRetract: true);
            trail.Push(new Vector3(10f, 0f, 0f), allowRetract: true);

            Assert.That(trail.Length, Is.EqualTo(10f).Within(Tolerance));
            Assert.That(trail.HeadOdometer, Is.EqualTo(10d).Within(Tolerance));
            AssertPoint(trail.PointBehindHead(1.4f), 8.6f, 0f);
        }

        [Test]
        public void Push_StandingStill_DoesNotChangeTheTrail()
        {
            var trail = Walk(new TeamFollowTrail(50f), new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f));
            for (int i = 0; i < 100; i++) trail.Push(new Vector3(4f, 0f, 0f));

            Assert.That(trail.Length, Is.EqualTo(4f).Within(Tolerance));
            AssertPoint(trail.PointBehindHead(1.4f), 2.6f, 0f);
        }

        [Test]
        public void DistanceBehindHead_ProjectsOntoTheTrail()
        {
            var trail = Walk(new TeamFollowTrail(50f), new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));

            float behind = trail.DistanceBehindHead(new Vector3(7f, 0f, 0.3f), out float lateral);

            Assert.That(behind, Is.EqualTo(3f).Within(Tolerance));
            Assert.That(lateral, Is.EqualTo(0.3f).Within(Tolerance));
        }

        [Test]
        public void DistanceBehindHead_AheadOfHead_ClampsToHead()
        {
            var trail = Walk(new TeamFollowTrail(50f), new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));

            float behind = trail.DistanceBehindHead(new Vector3(12f, 0f, 0f), out float lateral);

            Assert.That(behind, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(lateral, Is.EqualTo(2f).Within(Tolerance));
        }

        [Test]
        public void DistanceBehindHead_WhereTheTrailCrossesItself_PrefersThePartNearerTheHead()
        {
            // 绕一圈回来穿过起点:起点处轨迹重叠,应当取离队首近的那一段(允许队员抄掉这一圈)。
            var trail = new TeamFollowTrail(100f);
            Walk(trail, new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Walk(trail, new Vector3(10f, 0f, 0f), new Vector3(10f, 0f, 10f));
            Walk(trail, new Vector3(10f, 0f, 10f), new Vector3(0f, 0f, 10f));
            Walk(trail, new Vector3(0f, 0f, 10f), new Vector3(0f, 0f, -5f));

            float behind = trail.DistanceBehindHead(new Vector3(0f, 0f, 0f), out float lateral);

            Assert.That(lateral, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(behind, Is.EqualTo(5f).Within(Tolerance));
        }

        [TestCase(0.00001f, 5f, 0.00001f)]
        [TestCase(0.01f, 45.01f, 0f)]
        public void DistanceBehindHead_NearlyOverlappingSegments_OnlyTreatsTinyDifferencesAsTies(
            float offset, float expectedBehind, float expectedLateral)
        {
            // 新旧两段几乎重合时保留新段;确实偏离 1 厘米时仍应选择更近的旧段。
            var trail = new TeamFollowTrail(100f);
            trail.Reset(new Vector3(offset, 0f, -5f), new[]
            {
                new Vector3(offset, 0f, 5f),
                new Vector3(-10f, 0f, 5f),
                new Vector3(-10f, 0f, -5f),
                new Vector3(0f, 0f, -5f),
                new Vector3(0f, 0f, 5f),
            });

            float behind = trail.DistanceBehindHead(Vector3.zero, out float lateral);

            Assert.That(behind, Is.EqualTo(expectedBehind).Within(Tolerance));
            Assert.That(lateral, Is.EqualTo(expectedLateral).Within(0.000001f));
        }

        [Test]
        public void HeadOdometer_GrowsWithTheWalkAndShrinksWhenTheEndIsRetracted()
        {
            var trail = Walk(new TeamFollowTrail(50f), new Vector3(0f, 0f, 0f), new Vector3(4.5f, 0f, 0f));
            Assert.That(trail.HeadOdometer, Is.EqualTo(4.5d).Within(Tolerance));

            // 停步回拉 0.6:末端收回,队首里程跟着退。
            trail.Push(new Vector3(3.9f, 0f, 0f), allowRetract: true);
            Assert.That(trail.HeadOdometer, Is.EqualTo(3.9d).Within(Tolerance));

            // 掉头往回走(不收回):走过的路都算里程。
            var back = Walk(new TeamFollowTrail(50f), new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f));
            Walk(back, new Vector3(4f, 0f, 0f), new Vector3(1f, 0f, 0f));
            Assert.That(back.HeadOdometer, Is.EqualTo(7d).Within(Tolerance));
        }

        [Test]
        public void HeadOdometer_SurvivesTrimmingAndRestartsWithANewEpoch()
        {
            // 裁掉旧段不改变里程(里程是绝对的,游标才不会因为裁剪而错位)。
            var trail = Walk(new TeamFollowTrail(5f), new Vector3(0f, 0f, 0f), new Vector3(40f, 0f, 0f));
            Assert.That(trail.HeadOdometer, Is.EqualTo(40d).Within(Tolerance));

            int epoch = trail.Epoch;
            trail.Reset(new Vector3(1f, 0f, 1f), null);
            Assert.That(trail.Epoch, Is.Not.EqualTo(epoch));
            Assert.That(trail.HeadOdometer, Is.EqualTo(0d).Within(Tolerance));
        }

        [Test]
        public void TryProject_OnlyLooksInsideTheOdometerWindow()
        {
            // 向北 10 再原路走回:z = 4 处去程里程 4、回程里程 16。
            var trail = Walk(new TeamFollowTrail(80f), new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 10f));
            Walk(trail, new Vector3(0f, 0f, 10f), new Vector3(0f, 0f, 0f));
            var position = new Vector3(0.2f, 0f, 4f);

            Assert.That(trail.TryProject(position, 3d, 6d, 4d, 0f, out double outbound, out float outboundLateral), Is.True);
            Assert.That(outbound, Is.EqualTo(4d).Within(Tolerance));
            Assert.That(outboundLateral, Is.EqualTo(0.2f).Within(Tolerance));

            Assert.That(trail.TryProject(position, 14d, 18d, 16d, 0f, out double inbound, out _), Is.True);
            Assert.That(inbound, Is.EqualTo(16d).Within(Tolerance));

            // 窗口只盖住去程的 6..8:最近点被夹在窗口边上,不会滑到窗口外面去。
            Assert.That(trail.TryProject(position, 6d, 8d, 6d, 0f, out double clamped, out float clampedLateral), Is.True);
            Assert.That(clamped, Is.EqualTo(6d).Within(Tolerance));
            Assert.That(clampedLateral, Is.GreaterThan(2f - Tolerance));

            // 窗口在队首之后 / 整个落在轨迹之外:没有候选。
            Assert.That(trail.TryProject(position, 25d, 30d, 25d, 0f, out _, out _), Is.False);
            Assert.That(trail.TryProject(position, -9d, -1d, -1d, 0f, out _, out _), Is.False);
        }

        [Test]
        public void TryProject_AmongNearTies_PicksTheRequestedOdometer()
        {
            // 去程在 x = 0.03,回程在 x = 0:站在 x = 0.02 处离去程更近 1 厘米。
            var trail = Walk(new TeamFollowTrail(80f), new Vector3(0.03f, 0f, 0f), new Vector3(0.03f, 0f, 10f));
            Walk(trail, new Vector3(0f, 0f, 10f), new Vector3(0f, 0f, 0f));
            var position = new Vector3(0.02f, 0f, 4f);
            double outbound = 4d, inbound = trail.HeadOdometer - 4d;

            // 不容差:严格取最近的(去程)。
            Assert.That(trail.TryProject(position, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity,
                0f, out double strict, out _), Is.True);
            Assert.That(strict, Is.EqualTo(outbound).Within(Tolerance));

            // 5 厘米内算一样近:要离队首最近的 → 回程;要离里程 4 最近的 → 去程。
            Assert.That(trail.TryProject(position, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity,
                0.05f, out double nearHead, out float nearHeadLateral), Is.True);
            Assert.That(nearHead, Is.EqualTo(inbound).Within(Tolerance));
            Assert.That(nearHeadLateral, Is.EqualTo(0.02f).Within(Tolerance));
            Assert.That(trail.TryProject(position, double.NegativeInfinity, double.PositiveInfinity, 4d,
                0.05f, out double nearHint, out _), Is.True);
            Assert.That(nearHint, Is.EqualTo(outbound).Within(Tolerance));
        }

        [Test]
        public void Reset_WithTail_LetsFollowersLineUpBehindAStandingLeader()
        {
            var tail = new List<Vector3>();
            for (int i = 1; i <= 11; i++) tail.Add(new Vector3(0f, 0f, -0.5f * i));
            var trail = new TeamFollowTrail(50f);

            trail.Reset(new Vector3(0f, 0f, 0f), tail);

            Assert.That(trail.Length, Is.EqualTo(5.5f).Within(Tolerance));
            AssertPoint(trail.PointBehindHead(1.4f), 0f, -1.4f);
            AssertPoint(trail.PointBehindHead(5.6f), 0f, -5.5f);
        }

        [Test]
        public void Reset_WithoutTail_PutsEverySlotOnTheHead()
        {
            var trail = new TeamFollowTrail(50f);

            trail.Reset(new Vector3(7f, 0f, 7f), null);

            Assert.That(trail.Length, Is.EqualTo(0f).Within(Tolerance));
            AssertPoint(trail.PointBehindHead(5.6f), 7f, 7f);
        }

        [Test]
        public void Push_LongWalk_TrimsTheOldestPart()
        {
            var trail = Walk(new TeamFollowTrail(5f), new Vector3(0f, 0f, 0f), new Vector3(40f, 0f, 0f));

            // 裁剪以记录点为单位,最多多留一个采样间距。
            Assert.That(trail.Length, Is.GreaterThanOrEqualTo(5f - Tolerance));
            Assert.That(trail.Length, Is.LessThanOrEqualTo(5f + TeamFollowTrail.SampleSpacing + Tolerance));
            Assert.That(trail.PointBehindHead(100f).x, Is.GreaterThanOrEqualTo(40f - 5f - TeamFollowTrail.SampleSpacing - Tolerance));
        }

        [Test]
        public void Clear_ForgetsTheHead()
        {
            var trail = Walk(new TeamFollowTrail(50f), new Vector3(0f, 0f, 0f), new Vector3(3f, 0f, 0f));

            trail.Clear();

            Assert.That(trail.HasHead, Is.False);
            Assert.That(trail.Length, Is.EqualTo(0f).Within(Tolerance));
        }

        /// <summary>沿直线从 from 走到 to,每 0.25 米报一次位置(含两端)。</summary>
        internal static TeamFollowTrail Walk(TeamFollowTrail trail, Vector3 from, Vector3 to)
        {
            float distance = Mathf.Sqrt((to.x - from.x) * (to.x - from.x) + (to.z - from.z) * (to.z - from.z));
            int steps = Mathf.RoundToInt(distance / Step);
            for (int i = 0; i <= steps; i++) trail.Push(Vector3.Lerp(from, to, steps == 0 ? 0f : i / (float)steps));
            return trail;
        }

        internal static void AssertPoint(Vector3 actual, float x, float z, float tolerance = Tolerance)
        {
            Assert.That(actual.x, Is.EqualTo(x).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(z).Within(tolerance));
        }
    }
}
