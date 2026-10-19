using MmorpgClient.Game.Team;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>什么时候请求归队(组队跟随 v1.1 V.9)。</summary>
    public sealed class TeamRegroupPolicyTests
    {
        private const float Grace = TeamRegroupPolicy.OutOfSightGraceSeconds;
        private const float Retry = TeamRegroupPolicy.RetrySeconds;

        [Test]
        public void LostLeader_WaitsForGraceThenRetriesAtInterval()
        {
            var policy = new TeamRegroupPolicy();
            Assert.That(policy.Tick(true, true, 10f), Is.False, "刚看不到:先等视野重建");
            Assert.That(policy.Tick(true, true, 10f + Grace - 0.01f), Is.False);
            Assert.That(policy.Tick(true, true, 10f + Grace), Is.True);
            Assert.That(policy.Tick(true, true, 10f + Grace + 0.1f), Is.False, "一次只请求一次");
            Assert.That(policy.Tick(true, true, 10f + Grace + Retry - 0.01f), Is.False);
            Assert.That(policy.Tick(true, true, 10f + Grace + Retry), Is.True, "一直看不到就隔一个间隔再请求");
        }

        [Test]
        public void SeeingTheLeaderAgain_RestartsTheGrace()
        {
            var policy = new TeamRegroupPolicy();
            policy.Tick(true, true, 0f);
            Assert.That(policy.Tick(false, true, 1f), Is.False, "又看到了");
            Assert.That(policy.Tick(true, true, 1.2f), Is.False);
            Assert.That(policy.Tick(true, true, 1.2f + Grace - 0.01f), Is.False, "重新计时,不沿用上一段");
            Assert.That(policy.Tick(true, true, 1.2f + Grace), Is.True);
        }

        [Test]
        public void OfflineLeader_NeverRequestsAutomatically()
        {
            var policy = new TeamRegroupPolicy();
            for (float t = 0f; t < 30f; t += 0.5f)
                Assert.That(policy.Tick(true, false, t), Is.False);
        }

        [Test]
        public void Manual_RequestsImmediatelyAndDebouncesDoubleClicks()
        {
            var policy = new TeamRegroupPolicy();
            Assert.That(policy.Manual(5f), Is.True);
            Assert.That(policy.Manual(5f + TeamRegroupPolicy.ManualCooldownSeconds - 0.01f), Is.False, "连点只算一次");
            Assert.That(policy.Manual(5f + TeamRegroupPolicy.ManualCooldownSeconds), Is.True);
        }

        [Test]
        public void Manual_PostponesTheNextAutomaticRequest()
        {
            var policy = new TeamRegroupPolicy();
            // 点「归队」的那一帧还看不到队长:手动这一次已经发了,自动的不能紧跟着再来一次。
            Assert.That(policy.Manual(0f), Is.True);
            Assert.That(policy.Tick(true, true, 0f), Is.False);
            Assert.That(policy.Tick(true, true, Grace), Is.False, "上一次的结果还没回来");
            Assert.That(policy.Tick(true, true, Retry - 0.01f), Is.False);
            Assert.That(policy.Tick(true, true, Retry), Is.True);

            // 已经在计时的时候手动点:自动的往后推一个间隔。
            Assert.That(policy.Manual(Retry + 1f), Is.True);
            Assert.That(policy.Tick(true, true, Retry + Retry), Is.False);
            Assert.That(policy.Tick(true, true, Retry + 1f + Retry), Is.True);
        }

        [Test]
        public void Reset_ForgetsTimersAndCooldown()
        {
            var policy = new TeamRegroupPolicy();
            Assert.That(policy.Manual(0f), Is.True);
            policy.Tick(true, true, 0.1f);
            policy.Reset();
            Assert.That(policy.Manual(0.2f), Is.True, "换队后冷却不带过去");
            policy.Reset();
            Assert.That(policy.Tick(true, true, 0.3f), Is.False);
            Assert.That(policy.Tick(true, true, 0.3f + Grace), Is.True, "换队后从头计时");
        }
    }
}
