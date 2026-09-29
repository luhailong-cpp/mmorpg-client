using System.Collections.Generic;
using MmorpgClient.Game.Team;
using MmorpgClient.World;
using NUnit.Framework;
using Teampb;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class TeamViewMapperTests
    {
        [Test]
        public void ToSnapshot_MapsEveryScalarAndList()
        {
            var view = new TeamView
            {
                TeamId = 100, LeaderId = 10, Capacity = 0, ZoneId = 3, Version = 7, MembershipEpoch = 9,
                MatchState = TeamMatchState.Starting, ApplicationCount = 2, ServerTimeMs = 123456
            };
            view.Members.Add(Member(10, joinSeq: 1));
            view.Members.Add(Member(11, joinSeq: 2));
            view.Applications.Add(new TeamApplicationView { Player = Member(20), AppliedAtMs = 1, ExpireAtMs = 5000 });
            view.Applications.Add(new TeamApplicationView { AppliedAtMs = 2, ExpireAtMs = 6000 });
            view.PendingInvites.Add(new TeamOutgoingInviteView { Invitee = Member(30), ExpireAtMs = 7000 });
            view.PendingInvites.Add(new TeamOutgoingInviteView { ExpireAtMs = 8000 });

            var snapshot = TeamViewMapper.ToSnapshot(view, 10);

            Assert.That(snapshot.TeamId, Is.EqualTo(100UL));
            Assert.That(snapshot.LeaderId, Is.EqualTo(10UL));
            Assert.That(snapshot.LocalPlayerId, Is.EqualTo(10UL));
            Assert.That(snapshot.Capacity, Is.EqualTo(TeamViewMapper.DefaultCapacity));
            Assert.That(snapshot.ZoneId, Is.EqualTo(3u));
            Assert.That(snapshot.Version, Is.EqualTo(7UL));
            Assert.That(snapshot.MembershipEpoch, Is.EqualTo(9UL));
            Assert.That(snapshot.ApplicationCount, Is.EqualTo(2u));
            Assert.That(snapshot.ServerTimeMs, Is.EqualTo(123456UL));
            Assert.That(snapshot.MatchStarting, Is.True);
            Assert.That(snapshot.Members.ConvertAll(role => role.PlayerId), Is.EqualTo(new[] { 10UL, 11UL }));
            Assert.That(snapshot.Applications.Count, Is.EqualTo(1));
            Assert.That(snapshot.Applications[0].PlayerId, Is.EqualTo(20UL));
            Assert.That(snapshot.Applications[0].ExpireAtMs, Is.EqualTo(5000UL));
            Assert.That(snapshot.PendingInvites.Count, Is.EqualTo(1));
            Assert.That(snapshot.PendingInvites[0].PlayerId, Is.EqualTo(30UL));
            Assert.That(snapshot.PendingInvites[0].ExpireAtMs, Is.EqualTo(7000UL));
            Assert.That(snapshot.Approved, Is.Empty);

            view.Capacity = 4;
            view.MatchState = TeamMatchState.Idle;
            var idle = TeamViewMapper.ToSnapshot(view, 10);
            Assert.That(idle.Capacity, Is.EqualTo(4));
            Assert.That(idle.MatchStarting, Is.False);
        }

        [Test]
        public void ToSnapshot_SortsMembersByJoinSeqStably()
        {
            var view = new TeamView { TeamId = 100, LeaderId = 12, MembershipEpoch = 1 };
            var wrongFlag = Member(30, joinSeq: 3);
            wrongFlag.IsLeader = true;
            view.Members.Add(wrongFlag);
            view.Members.Add(Member(11, joinSeq: 1));
            view.Members.Add(Member(12, joinSeq: 1));

            var snapshot = TeamViewMapper.ToSnapshot(view, 11);

            Assert.That(snapshot.Members.ConvertAll(role => role.PlayerId), Is.EqualTo(new[] { 11UL, 12UL, 30UL }));
            Assert.That(snapshot.Members.ConvertAll(role => role.JoinSeq), Is.EqualTo(new[] { 1u, 1u, 3u }));
            Assert.That(snapshot.Members.ConvertAll(role => role.IsLeader), Is.EqualTo(new[] { false, true, false }));
        }

        [Test]
        public void ToRole_UsesServerOnlineAndBattleFlags()
        {
            var member = new TeamMemberView
            {
                PlayerId = 42, Name = "青衫", Level = 35, ClassId = 2, Gender = 2, IsLeader = true,
                IsOnline = false, InBattle = true, ZoneId = 7, JoinSeq = 4, AppearanceId = "custom_look"
            };

            var role = TeamViewMapper.ToRole(member, 9000);

            Assert.That(role.PlayerId, Is.EqualTo(42UL));
            Assert.That(role.Name, Is.EqualTo("青衫"));
            Assert.That(role.Level, Is.EqualTo(35u));
            Assert.That(role.ClassId, Is.EqualTo(2u));
            Assert.That(role.Gender, Is.EqualTo(2u));
            Assert.That(role.IsLeader, Is.True);
            Assert.That(role.IsOnline, Is.False, "类默认值是 true,必须用服务端的值覆盖");
            Assert.That(role.InBattle, Is.True);
            Assert.That(role.ZoneId, Is.EqualTo(7u));
            Assert.That(role.JoinSeq, Is.EqualTo(4u));
            Assert.That(role.CharacterId, Is.EqualTo("custom_look"));
            Assert.That(role.SchoolName, Is.Null);
            Assert.That(role.ExpireAtMs, Is.EqualTo(9000UL));
            Assert.That(TeamViewMapper.ToRole(new TeamMemberView { PlayerId = 1 }).ExpireAtMs, Is.Zero);
            Assert.That(TeamViewMapper.ToRole(null), Is.Null);
        }

        [Test]
        public void ResolveCharacterId()
        {
            Assert.That(TeamViewMapper.ResolveCharacterId(1, 1, "not_a_known_id"), Is.EqualTo("not_a_known_id"));
            Assert.That(TeamViewMapper.ResolveCharacterId(0, 0, "persisted"), Is.EqualTo("persisted"));
            Assert.That(TeamViewMapper.ResolveCharacterId(1, 1, ""), Is.EqualTo(QdaoCharacterCatalog.ResolveRole(1, 1)));
            Assert.That(TeamViewMapper.ResolveCharacterId(1, 1, null), Is.EqualTo(QdaoCharacterCatalog.ResolveRole(1, 1)));
            Assert.That(TeamViewMapper.ResolveCharacterId(3, 0, ""), Is.EqualTo(QdaoCharacterCatalog.ResolveRole(3, 1)),
                "gender 0 的旧存档按男性处理");
            Assert.That(TeamViewMapper.ResolveCharacterId(0, 1, ""), Is.Null);
            Assert.That(TeamViewMapper.ResolveCharacterId(9, 1, ""), Is.Null);
        }

        [Test]
        public void ToSnapshot_EmptyViewKeepsEpochAndClearsLists()
        {
            var view = new TeamView
            {
                TeamId = 0, LeaderId = 10, MembershipEpoch = 12, ServerTimeMs = 555,
                MatchState = TeamMatchState.Starting, ApplicationCount = 3
            };
            view.Members.Add(Member(10, joinSeq: 1));
            view.Applications.Add(new TeamApplicationView { Player = Member(20), ExpireAtMs = 1 });
            view.PendingInvites.Add(new TeamOutgoingInviteView { Invitee = Member(30), ExpireAtMs = 1 });

            var snapshot = TeamViewMapper.ToSnapshot(view, 10);

            Assert.That(snapshot.TeamId, Is.Zero);
            Assert.That(snapshot.LeaderId, Is.Zero);
            Assert.That(snapshot.MembershipEpoch, Is.EqualTo(12UL));
            Assert.That(snapshot.ServerTimeMs, Is.EqualTo(555UL));
            Assert.That(snapshot.MatchStarting, Is.False);
            Assert.That(snapshot.ApplicationCount, Is.Zero);
            Assert.That(snapshot.Members, Is.Empty);
            Assert.That(snapshot.Applications, Is.Empty);
            Assert.That(snapshot.PendingInvites, Is.Empty);
            Assert.That(snapshot.Approved, Is.Empty);

            var none = TeamViewMapper.ToSnapshot(null, 7);
            Assert.That(none.TeamId, Is.Zero);
            Assert.That(none.LocalPlayerId, Is.EqualTo(7UL));
            Assert.That(none.Capacity, Is.EqualTo(TeamViewMapper.DefaultCapacity));
            Assert.That(none.Members, Is.Empty);
        }

        [Test]
        public void Compare_Matrix()
        {
            var current = Snapshot(100, epoch: 5, version: 4);

            Assert.That(TeamViewMapper.Compare(null, Snapshot(100, 1, 1)), Is.EqualTo(TeamSnapshotOrder.Accept));
            Assert.That(TeamViewMapper.Compare(current, Snapshot(100, 6, 1)), Is.EqualTo(TeamSnapshotOrder.Accept));
            Assert.That(TeamViewMapper.Compare(current, Snapshot(100, 4, 99)), Is.EqualTo(TeamSnapshotOrder.Stale));
            Assert.That(TeamViewMapper.Compare(current, Snapshot(100, 5, 4)), Is.EqualTo(TeamSnapshotOrder.Accept));
            Assert.That(TeamViewMapper.Compare(current, Snapshot(100, 5, 5)), Is.EqualTo(TeamSnapshotOrder.Accept));
            Assert.That(TeamViewMapper.Compare(current, Snapshot(100, 5, 3)), Is.EqualTo(TeamSnapshotOrder.Stale));
            Assert.That(TeamViewMapper.Compare(current, Snapshot(200, 5, 9)), Is.EqualTo(TeamSnapshotOrder.Conflict));
            Assert.That(TeamViewMapper.Compare(current, Snapshot(0, 5, 0)), Is.EqualTo(TeamSnapshotOrder.Conflict));
            Assert.That(TeamViewMapper.Compare(current, null), Is.EqualTo(TeamSnapshotOrder.Stale));
            Assert.That(TeamViewMapper.Compare(null, null), Is.EqualTo(TeamSnapshotOrder.Stale));
        }

        [Test]
        public void ToInvite_ComputesLocalDeadline()
        {
            var view = new TeamIncomingInviteView
            {
                TeamId = 300, LeaderId = 30, MemberCount = 2, ZoneId = 4, ExpireAtMs = 61000,
                Inviter = Member(31)
            };

            var invite = TeamViewMapper.ToInvite(view, 1000, 5f);
            Assert.That(invite.ExpiresAt, Is.EqualTo(65f).Within(0.001f));
            Assert.That(invite.TeamId, Is.EqualTo(300UL));
            Assert.That(invite.LeaderId, Is.EqualTo(30UL));
            Assert.That(invite.MemberCount, Is.EqualTo(2u));
            Assert.That(invite.ZoneId, Is.EqualTo(4u));
            Assert.That(invite.Inviter.PlayerId, Is.EqualTo(31UL));

            Assert.That(TeamViewMapper.ToInvite(view, 0, 5f).ExpiresAt,
                Is.EqualTo(5f + TeamViewMapper.DefaultInviteTtlSeconds).Within(0.001f));
            view.ExpireAtMs = 0;
            Assert.That(TeamViewMapper.ToInvite(view, 1000, 5f).ExpiresAt,
                Is.EqualTo(5f + TeamViewMapper.DefaultInviteTtlSeconds).Within(0.001f));
            view.ExpireAtMs = 500;
            Assert.That(TeamViewMapper.ToInvite(view, 1000, 5f).ExpiresAt, Is.EqualTo(5f).Within(0.001f));

            view.Inviter = null;
            var fallback = TeamViewMapper.ToInvite(view, 1000, 5f);
            Assert.That(fallback.Inviter, Is.Not.Null);
            Assert.That(fallback.Inviter.PlayerId, Is.EqualTo(30UL));

            view.TeamId = 0;
            Assert.That(TeamViewMapper.ToInvite(view, 1000, 5f), Is.Null);
            Assert.That(TeamViewMapper.ToInvite(null, 1000, 5f), Is.Null);
        }

        [Test]
        public void Clone_IsDeepAndNormalizes()
        {
            var original = new TeamSnapshot { TeamId = 1, Capacity = 0, Version = 3, MembershipEpoch = 2 };
            original.Members.Add(new TeamRole { PlayerId = 10, Name = "甲" });
            original.Members.Add(null);
            original.Applications.Add(new TeamRole { PlayerId = 20 });

            var copy = original.Clone();
            original.Members[0].Name = "乙";
            original.Members.Add(new TeamRole { PlayerId = 11 });
            original.Applications.Clear();

            Assert.That(copy.Capacity, Is.EqualTo(5));
            Assert.That(copy.Version, Is.EqualTo(3UL));
            Assert.That(copy.MembershipEpoch, Is.EqualTo(2UL));
            Assert.That(copy.Members.Count, Is.EqualTo(1), "null 元素跳过,之后对原列表的增删不影响副本");
            Assert.That(copy.Members[0].Name, Is.EqualTo("甲"));
            Assert.That(copy.Applications.Count, Is.EqualTo(1));

            var empty = new TeamSnapshot { Members = null, Applications = null, PendingInvites = null, Approved = null }.Clone();
            Assert.That(empty.Members, Is.Not.Null.And.Empty);
            Assert.That(empty.Applications, Is.Not.Null.And.Empty);
            Assert.That(empty.PendingInvites, Is.Not.Null.And.Empty);
            Assert.That(empty.Approved, Is.Not.Null.And.Empty);
            Assert.That(TeamSnapshot.CloneRoles(null), Is.Not.Null.And.Empty);

            var invite = new TeamInvite { TeamId = 300, Inviter = new TeamRole { PlayerId = 30, Name = "丙" }, ExpiresAt = 9f };
            var inviteCopy = invite.Clone();
            invite.Inviter.Name = "丁";
            invite.TeamId = 301;
            Assert.That(inviteCopy.Inviter.Name, Is.EqualTo("丙"));
            Assert.That(inviteCopy.TeamId, Is.EqualTo(300UL));
            Assert.That(inviteCopy.ExpiresAt, Is.EqualTo(9f));
            Assert.That(new TeamInvite { TeamId = 1 }.Clone().Inviter, Is.Null);
        }

        [Test]
        public void DisplayName_FallsBackToPlayerId()
        {
            Assert.That(TeamViewMapper.DisplayName(new TeamRole { PlayerId = 42, Name = "" }), Is.EqualTo("道友 42"));
            Assert.That(TeamViewMapper.DisplayName(new TeamRole { PlayerId = 42, Name = "   " }), Is.EqualTo("道友 42"));
            Assert.That(TeamViewMapper.DisplayName(new TeamRole { PlayerId = 42 }), Is.EqualTo("道友 42"));
            Assert.That(TeamViewMapper.DisplayName(null), Is.EqualTo("无名道友"));
            Assert.That(TeamViewMapper.DisplayName(new TeamRole()), Is.EqualTo("无名道友"));
            Assert.That(TeamViewMapper.DisplayName(new TeamRole { PlayerId = 42, Name = "青衫" }), Is.EqualTo("青衫"));
        }

        private static TeamMemberView Member(ulong playerId, uint joinSeq = 0) =>
            new TeamMemberView { PlayerId = playerId, ClassId = 1, Gender = 1, IsOnline = true, JoinSeq = joinSeq };

        private static TeamSnapshot Snapshot(ulong teamId, ulong epoch, ulong version) =>
            new TeamSnapshot { TeamId = teamId, MembershipEpoch = epoch, Version = version, Members = new List<TeamRole>() };
    }
}
