using MmorpgClient.Game.Team;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>
    /// 组队跟随的声明时机(服务器驱动走位,服务端 docs/design/team-follow-server-trail.md §5):
    /// 每次入场说一次、没被驱动时按间隔重说、暂离时撤回、服务器自己开的跟随照单全收。
    /// </summary>
    public sealed class TeamFollowDeclarationTests
    {
        private const ulong Leader = 100;
        private const float Grace = TeamRegroupPolicy.OutOfSightGraceSeconds;
        private const float Retry = TeamRegroupPolicy.RetrySeconds;

        // 问一次;要发就当作发成功并记账,返回发了什么(null = 没发)。
        private static bool? Step(TeamFollowDeclaration declaration, TeamFollowWish wish, bool driven, int entry,
            float now, bool leaderOnline = true)
        {
            var decided = declaration.Decide(wish, driven, leaderOnline, entry, now);
            if (decided.HasValue) declaration.Sent(decided.Value, entry, now);
            return decided;
        }

        // ── 跟随 ────────────────────────────────────────────────────────────

        [Test]
        public void Follow_DeclaresOncePerSceneEntry()
        {
            var declaration = new TeamFollowDeclaration();
            Assert.That(Step(declaration, TeamFollowWish.Follow, false, 1, 0f), Is.True, "进场就说");
            Assert.That(Step(declaration, TeamFollowWish.Follow, false, 1, 0.1f), Is.Null, "同一次入场不重复说");
            Assert.That(Step(declaration, TeamFollowWish.Follow, true, 1, 0.2f), Is.Null, "服务器接上了");

            // 换了场景:客户端已把驱动清掉;跨节点时服务器那一侧是新实体,原来的声明不在了,要再说一次。
            Assert.That(Step(declaration, TeamFollowWish.Follow, false, 2, 3f), Is.True);
            Assert.That(Step(declaration, TeamFollowWish.Follow, false, 2, 3.1f), Is.Null);
        }

        [Test]
        public void Follow_WhenTheServerDoesNotPickItUp_RetriesAtTheRegroupInterval()
        {
            var declaration = new TeamFollowDeclaration();
            Assert.That(Step(declaration, TeamFollowWish.Follow, false, 1, 0f), Is.True);
            for (float t = 0.1f; t < Retry - 0.01f; t += 0.1f)
                Assert.That(Step(declaration, TeamFollowWish.Follow, false, 1, t), Is.Null, "上一次的结果还没回来");
            Assert.That(Step(declaration, TeamFollowWish.Follow, false, 1, Retry), Is.True, "仍没被驱动:再说一次");
            Assert.That(Step(declaration, TeamFollowWish.Follow, false, 1, Retry + 0.1f), Is.Null);
            Assert.That(Step(declaration, TeamFollowWish.Follow, false, 1, Retry + Retry), Is.True);
        }

        [Test]
        public void Follow_WithTheLeaderOffline_DoesNotRetry()
        {
            var declaration = new TeamFollowDeclaration();
            Assert.That(Step(declaration, TeamFollowWish.Follow, false, 1, 0f, leaderOnline: false), Is.True,
                "入场那一次照说:服务器看得到队长有没有托管在场景里");
            for (float t = 0.5f; t < 30f; t += 0.5f)
                Assert.That(Step(declaration, TeamFollowWish.Follow, false, 1, t, leaderOnline: false), Is.Null);
        }

        [Test]
        public void Follow_LosingTheDriveLater_RetriesAfterTheGrace()
        {
            var declaration = new TeamFollowDeclaration();
            Step(declaration, TeamFollowWish.Follow, false, 1, 0f);
            Assert.That(Step(declaration, TeamFollowWish.Follow, true, 1, 0.5f), Is.Null);
            // 服务器停止驱动(队长瞬移太远、队形作废)而这次入场已经说过:等宽限再重说,不当场连发。
            Assert.That(Step(declaration, TeamFollowWish.Follow, false, 1, 20f), Is.Null);
            Assert.That(Step(declaration, TeamFollowWish.Follow, false, 1, 20f + Grace - 0.01f), Is.Null);
            Assert.That(Step(declaration, TeamFollowWish.Follow, false, 1, 20f + Grace), Is.True);
        }

        [Test]
        public void ServerStartedFollow_IsAcceptedWithoutDeclaring()
        {
            // 挂机托管开始时服务器自己开了跟随,玩家之后接回角色:入场后服务器先推 driven=true,客户端不用再说。
            var declaration = new TeamFollowDeclaration();
            Assert.That(Step(declaration, TeamFollowWish.Follow, true, 1, 0f), Is.Null);
            Assert.That(Step(declaration, TeamFollowWish.Follow, true, 1, 30f), Is.Null);
        }

        [Test]
        public void SendFailure_IsRetriedOnTheNextFrame()
        {
            var declaration = new TeamFollowDeclaration();
            // 没连着 gate:Decide 给了声明,但没发出去,不调 Sent。
            Assert.That(declaration.Decide(TeamFollowWish.Follow, false, true, 1, 0f), Is.True);
            Assert.That(declaration.Decide(TeamFollowWish.Follow, false, true, 1, 0.02f), Is.True, "没发出去就下一帧再给");
        }

        // ── 暂离 ────────────────────────────────────────────────────────────

        [Test]
        public void Away_RetractsOnceThenOnlyWhileStillDriven()
        {
            var declaration = new TeamFollowDeclaration();
            Step(declaration, TeamFollowWish.Follow, true, 1, 0f);

            declaration.AwayChanged(true);
            Assert.That(Step(declaration, TeamFollowWish.Away, true, 1, 1f), Is.False, "切到暂离立刻撤回");
            Assert.That(Step(declaration, TeamFollowWish.Away, true, 1, 1.5f), Is.Null, "回包还在路上,不连发");
            Assert.That(Step(declaration, TeamFollowWish.Away, false, 1, 1.6f), Is.Null, "服务器停止驱动了");
            Assert.That(Step(declaration, TeamFollowWish.Away, false, 1, 60f), Is.Null, "暂离中不再发任何声明");

            // 撤回与服务器那边开的跟随交错(断线期间托管开始,带着暂离接回):还被驱动就再撤。
            Assert.That(Step(declaration, TeamFollowWish.Away, true, 2, 61f), Is.False);
            Assert.That(Step(declaration, TeamFollowWish.Away, true, 2, 61f + TeamFollowDeclaration.RetractRetrySeconds - 0.01f),
                Is.Null);
            Assert.That(Step(declaration, TeamFollowWish.Away, true, 2, 61f + TeamFollowDeclaration.RetractRetrySeconds),
                Is.False);
        }

        [Test]
        public void Away_IsRetractedEvenBeforeTheServerPickedUpTheFollow()
        {
            // 刚说完跟随、服务器还没接上就点了暂离:撤回照发,不让在路上的那次声明把人接走。
            var declaration = new TeamFollowDeclaration();
            Step(declaration, TeamFollowWish.Follow, false, 1, 0f);
            declaration.AwayChanged(true);
            Assert.That(Step(declaration, TeamFollowWish.Away, false, 1, 0.1f), Is.False);
            Assert.That(Step(declaration, TeamFollowWish.Away, false, 1, 0.2f), Is.Null);
        }

        [Test]
        public void Rejoin_DeclaresImmediatelyWithoutWaitingForASceneEntry()
        {
            var declaration = new TeamFollowDeclaration();
            Step(declaration, TeamFollowWish.Follow, true, 1, 0f);
            declaration.AwayChanged(true);
            Step(declaration, TeamFollowWish.Away, true, 1, 1f);
            Step(declaration, TeamFollowWish.Away, false, 1, 1.1f);

            declaration.AwayChanged(false);
            Assert.That(Step(declaration, TeamFollowWish.Follow, false, 1, 2f), Is.True, "点了「归队」:当场声明");
            Assert.That(Step(declaration, TeamFollowWish.Follow, false, 1, 2.1f), Is.Null);
        }

        [Test]
        public void TogglingBackBeforeTheRetractWentOut_SendsNothingStale()
        {
            var declaration = new TeamFollowDeclaration();
            Step(declaration, TeamFollowWish.Follow, true, 1, 0f);
            declaration.AwayChanged(true);
            declaration.AwayChanged(false); // 撤回还没发出去就又切回跟随
            Assert.That(Step(declaration, TeamFollowWish.Follow, true, 1, 0.1f), Is.Null, "还在被驱动:什么都不用说");
        }

        // ── 不在跟随范围 / 换队 ─────────────────────────────────────────────────

        [Test]
        public void NotAFollower_SendsNothing()
        {
            var declaration = new TeamFollowDeclaration();
            for (float t = 0f; t < 20f; t += 0.5f)
            {
                Assert.That(Step(declaration, TeamFollowWish.None, false, 1, t), Is.Null);
                Assert.That(Step(declaration, TeamFollowWish.None, true, 1, t), Is.Null,
                    "刚当上队长时服务器可能还没停止驱动:由服务器自己停,不替它撤回");
            }
        }

        [Test]
        public void Reset_ForgetsTheDeclaration()
        {
            var declaration = new TeamFollowDeclaration();
            Step(declaration, TeamFollowWish.Follow, false, 1, 0f);
            declaration.AwayChanged(true);
            declaration.Reset();
            Assert.That(Step(declaration, TeamFollowWish.Follow, false, 1, 0.5f), Is.True, "换了队伍:同一次入场也要重新说");
        }

        // ── 是不是该跟随的队员 ────────────────────────────────────────────────

        [Test]
        public void IsFollower_WithoutATeam_IsFalse()
        {
            Assert.That(TeamFollowDeclaration.IsFollower(null, 1), Is.False);
            Assert.That(TeamFollowDeclaration.IsFollower(new TeamSnapshot(), 1), Is.False);
            Assert.That(TeamFollowDeclaration.IsFollower(Team(Leader, 1), 0), Is.False);
        }

        [Test]
        public void IsFollower_TheLeader_IsFalse()
        {
            Assert.That(TeamFollowDeclaration.IsFollower(Team(Leader, 1), Leader), Is.False);
        }

        [Test]
        public void IsFollower_SomeoneNotOnTheRoster_IsFalse()
        {
            Assert.That(TeamFollowDeclaration.IsFollower(Team(Leader, 1), 999), Is.False);
        }

        [Test]
        public void IsFollower_WhenTheLeaderIsMissingFromTheRoster_IsFalse()
        {
            // 视图不自洽(队长编号指向名单外的人):不跟,等下一份视图。
            var snapshot = Team(1, 2);
            snapshot.LeaderId = Leader;
            Assert.That(TeamFollowDeclaration.IsFollower(snapshot, 1), Is.False);
        }

        [Test]
        public void IsFollower_AnyListedMemberOtherThanTheLeader_IsTrue()
        {
            var snapshot = Team(13, Leader, 11, 12);
            Assert.That(TeamFollowDeclaration.IsFollower(snapshot, 11), Is.True);
            Assert.That(TeamFollowDeclaration.IsFollower(snapshot, 13), Is.True, "排第几由服务器定,与名单顺序无关");
        }

        private static TeamSnapshot Team(params ulong[] members)
        {
            var snapshot = new TeamSnapshot { TeamId = 7, LeaderId = Leader };
            uint joinSeq = 1;
            foreach (var playerId in members)
                snapshot.Members.Add(new TeamRole { PlayerId = playerId, JoinSeq = joinSeq++, IsLeader = playerId == Leader });
            return snapshot;
        }
    }
}
