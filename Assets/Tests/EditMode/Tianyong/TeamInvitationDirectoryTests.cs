using System;
using System.Collections.Generic;
using Friendpb;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Team;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class TeamInvitationDirectoryTests
    {
        private DirectoryTransport _net;
        private TeamInvitationDirectory _directory;
        private float _now;
        private List<TeamRole> _local;

        [SetUp]
        public void SetUp()
        {
            _net = new DirectoryTransport(); _now = 0; _local = new List<TeamRole>();
            _directory = new TeamInvitationDirectory(_net, () => _net.Identity, _ => _local,
                id => new TeamRole { PlayerId = id, Name = id == 2 ? "清铃" : null, IsOnline = false, OnlineStatusKnown = false }, () => _now);
        }

        [TearDown] public void TearDown() => _directory.Dispose();

        [Test]
        public void Friends_UsesRealRpc_PreservesOffline_ExcludesSelfAndDuplicates()
        {
            _directory.Refresh(TeamInvitationSource.Friends);
            Assert.That(_net.Calls[0].Id, Is.EqualTo(12));
            Assert.That(_directory.IsLoading, Is.True);
            var reply = new GetFriendListResponse();
            reply.Friends.Add(new FriendEntry { FriendPlayerId = 1, IsOnline = true });
            reply.Friends.Add(new FriendEntry { FriendPlayerId = 2, IsOnline = false });
            reply.Friends.Add(new FriendEntry { FriendPlayerId = 2, IsOnline = true });
            reply.Friends.Add(new FriendEntry { FriendPlayerId = 0, IsOnline = true });
            _net.Reply(0, reply);
            Assert.That(_directory.Candidates.Count, Is.EqualTo(1));
            Assert.That(_directory.Candidates[0].Name, Is.EqualTo("清铃"));
            Assert.That(_directory.Candidates[0].IsOnline, Is.False);
            Assert.That(_directory.Candidates[0].OnlineStatusKnown, Is.True);
            Assert.That(_directory.IsLoading, Is.False);
        }

        [Test]
        public void LocalCandidates_FilterNamesAndIds_CloneSnapshots()
        {
            var role = new TeamRole { PlayerId = 25, Name = "Cloud", IsOnline = true };
            _local.Add(role);
            _directory.Refresh(TeamInvitationSource.Nearby, "clo");
            role.Name = "changed";
            Assert.That(_directory.Candidates[0].Name, Is.EqualTo("Cloud"));
            _directory.Refresh(TeamInvitationSource.Nearby, "25");
            Assert.That(_directory.Candidates.Count, Is.EqualTo(1));
            _directory.Refresh(TeamInvitationSource.Nearby, "missing");
            Assert.That(_directory.Candidates, Is.Empty);
            Assert.That(_net.Calls, Is.Empty);
        }

        [Test]
        public void ChatSelection_IsUnknownPresence_AndResetClearsRememberedPlayers()
        {
            _directory.RememberChatPlayer(2); _directory.RememberChatPlayer(1); _directory.RememberChatPlayer(0);
            _directory.Refresh(TeamInvitationSource.Chat);
            Assert.That(_directory.Candidates.Count, Is.EqualTo(1));
            Assert.That(_directory.Candidates[0].IsOnline, Is.False);
            Assert.That(_directory.Candidates[0].OnlineStatusKnown, Is.False);
            Assert.That(_directory.Status, Does.Contain("确认"));
            _directory.Reset(); _directory.Refresh(TeamInvitationSource.Chat);
            Assert.That(_directory.Candidates, Is.Empty);
            Assert.That(_net.Calls, Is.Empty);
        }

        [Test]
        public void ChatCandidate_UsesKnownFriendPresenceAndProfile_WhenAvailable()
        {
            _directory.Refresh(TeamInvitationSource.Friends);
            var reply = new GetFriendListResponse();
            reply.Friends.Add(new FriendEntry { FriendPlayerId = 2, IsOnline = false, Name = "真名字", Level = 42, AppearanceId = "30_han_xiangzi" });
            _net.Reply(0, reply);
            Assert.That(_directory.Candidates[0].Name, Is.EqualTo("真名字"));
            Assert.That(_directory.Candidates[0].CharacterId, Is.EqualTo("30_han_xiangzi"));
            _directory.RememberChatPlayer(2); _directory.Refresh(TeamInvitationSource.Chat);
            Assert.That(_directory.Candidates[0].OnlineStatusKnown, Is.True);
            Assert.That(_directory.Candidates[0].IsOnline, Is.False);
            Assert.That(_directory.Candidates[0].Level, Is.EqualTo(42));
        }

        [Test]
        public void Online_RejectsOldServerRecommendationsWithoutCapabilityAcknowledgement()
        {
            _directory.Refresh(TeamInvitationSource.Online);
            var reply = Online(2); reply.OnlineDirectory = false;
            _net.Reply(0, reply);
            Assert.That(_directory.Candidates, Is.Empty);
            Assert.That(_directory.Status, Does.Contain("尚未提供"));
        }

        [Test]
        public void Online_UsesSearchAndCursor_PreservesEmptyPageContinuation()
        {
            _directory.Refresh(TeamInvitationSource.Online, "  清铃  ");
            var request = (RecommendFriendsRequest)_net.Calls[0].Request;
            Assert.That(_net.Calls[0].Id, Is.EqualTo(119));
            Assert.That(request.OnlineOnly, Is.True);
            Assert.That(request.Query, Is.EqualTo("清铃"));
            Assert.That(request.Cursor, Is.Empty);
            Assert.That(request.Limit, Is.EqualTo(TeamInvitationDirectory.PageSize));
            _net.Reply(0, new RecommendFriendsResponse { OnlineDirectory = true, NextCursor = "opaque-cursor" });
            Assert.That(_directory.HasMore, Is.True);
            Assert.That(_directory.Status, Does.Contain("继续加载"));
            _directory.LoadMore();
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "throttle must apply to pagination too");
            _now = 3; _directory.ObserveConnection();
            request = (RecommendFriendsRequest)_net.Calls[1].Request;
            Assert.That(request.Cursor, Is.EqualTo("opaque-cursor"));
            Assert.That(request.Query, Is.EqualTo("清铃"));
            _net.Reply(1, Online(2));
            Assert.That(_directory.Candidates.Count, Is.EqualTo(1));
            Assert.That(_directory.HasMore, Is.False);
        }

        [Test]
        public void Online_MapsAuthoritativeProfile_RejectsOfflineRows()
        {
            _directory.Refresh(TeamInvitationSource.Online);
            var reply = Online(2);
            reply.Candidates[0].Name = "清铃"; reply.Candidates[0].Level = 42;
            reply.Candidates[0].ClassId = 3; reply.Candidates[0].Gender = 2; reply.Candidates[0].ZoneId = 7;
            reply.Candidates.Add(new RecommendEntry { CandidatePlayerId = 3, IsOnline = false });
            _net.Reply(0, reply);
            var candidate = _directory.Candidates[0];
            Assert.That(_directory.Candidates.Count, Is.EqualTo(1));
            Assert.That(candidate.Name, Is.EqualTo("清铃"));
            Assert.That(candidate.Level, Is.EqualTo(42));
            Assert.That(candidate.ClassId, Is.EqualTo(3));
            Assert.That(candidate.Gender, Is.EqualTo(2));
            Assert.That(candidate.ZoneId, Is.EqualTo(7));
            Assert.That(candidate.IsOnline, Is.True);
        }

        [Test]
        public void Pagination_DeduplicatesPlayers_AndRejectsNonAdvancingCursor()
        {
            _directory.Refresh(TeamInvitationSource.Online);
            var first = Online(2); first.NextCursor = "page-2"; _net.Reply(0, first);
            _now = 3; _directory.LoadMore();
            var second = Online(2); second.Candidates.Add(new RecommendEntry { CandidatePlayerId = 3, IsOnline = true });
            second.NextCursor = "page-3"; _net.Reply(1, second);
            Assert.That(_directory.Candidates.Count, Is.EqualTo(2));
            _now = 6; _directory.LoadMore();
            _net.Reply(2, new RecommendFriendsResponse { OnlineDirectory = true, NextCursor = "page-3" });
            Assert.That(_directory.HasMore, Is.False);
            Assert.That(_directory.Status, Does.Contain("未前进"));
        }

        [Test]
        public void SwitchingToLocalSource_DiscardsStaleRemoteReply()
        {
            _directory.Refresh(TeamInvitationSource.Online);
            _local.Add(new TeamRole { PlayerId = 8 });
            _directory.Refresh(TeamInvitationSource.Nearby);
            _net.Reply(0, Online(2));
            Assert.That(_directory.Source, Is.EqualTo(TeamInvitationSource.Nearby));
            Assert.That(_directory.Candidates.Count, Is.EqualTo(1));
            Assert.That(_directory.Candidates[0].PlayerId, Is.EqualTo(8));
            Assert.That(_directory.IsLoading, Is.False);
        }

        [Test]
        public void SwitchingSearch_CoalescesUntilPreviousReply_ThenSendsLatestQuery()
        {
            _directory.Refresh(TeamInvitationSource.Online, "first");
            _directory.Refresh(TeamInvitationSource.Online, "second");
            _directory.Refresh(TeamInvitationSource.Online, "last");
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
            _now = 3; _net.Reply(0, Online(2));
            Assert.That(_directory.Candidates, Is.Empty);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(((RecommendFriendsRequest)_net.Calls[1].Request).Query, Is.EqualTo("last"));
            _net.Reply(1, Online(3));
            Assert.That(_directory.Candidates[0].PlayerId, Is.EqualTo(3));
        }

        [Test]
        public void ConnectionReplacement_ClearsProfilesAndIgnoresOldReply()
        {
            _directory.Refresh(TeamInvitationSource.Online);
            _net.Identity = new object(); _directory.ObserveConnection();
            _directory.Refresh(TeamInvitationSource.Online);
            _net.Reply(0, Online(2));
            Assert.That(_directory.Candidates, Is.Empty);
            Assert.That(_directory.IsLoading, Is.True);
            _net.Reply(1, Online(3));
            Assert.That(_directory.Candidates[0].PlayerId, Is.EqualTo(3));
        }

        [Test]
        public void CharacterChangeDuringRequest_RequiresConnectionReplacement()
        {
            _directory.Refresh(TeamInvitationSource.Online);
            _net.PlayerId = 10; _directory.ObserveConnection();
            Assert.That(_directory.RequiresReconnect, Is.True);
            _net.Reply(0, Online(2));
            Assert.That(_directory.Candidates, Is.Empty);
            _now = 5; _directory.Refresh(TeamInvitationSource.Online);
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
            _net.Identity = new object(); _directory.ObserveConnection();
            _directory.Refresh(TeamInvitationSource.Online);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
        }

        [Test]
        public void Timeout_BlocksSameConnectionRetry_ButDisconnectUnblocks()
        {
            _directory.Refresh(TeamInvitationSource.Friends); _net.Error(0, "timeout");
            Assert.That(_directory.RequiresReconnect, Is.True);
            _now = 5; _directory.Refresh(TeamInvitationSource.Friends);
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
            _net.Disconnect(); _directory.Refresh(TeamInvitationSource.Friends);
            Assert.That(_directory.RequiresReconnect, Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
        }

        [Test]
        public void DefiniteRejection_CanRetry_InnerFailureNeverLooksLikeAnEmptySuccess()
        {
            _directory.Refresh(TeamInvitationSource.Friends);
            _net.Error(0, "server tip=" + (uint)common_error.KRateLimitExceeded);
            Assert.That(_directory.RequiresReconnect, Is.False);
            Assert.That(_directory.Status, Does.Contain("暂未接受"));
            _now = 3; _directory.Refresh(TeamInvitationSource.Friends);
            _net.Reply(1, new GetFriendListResponse { ErrorMessage = new TipInfoMessage { Id = 99 } });
            Assert.That(_directory.Status, Does.Contain("99"));
            Assert.That(_directory.Status, Does.Not.Contain("名单为空"));
        }

        [Test]
        public void DisposedDirectory_IgnoresCallbacksAndNewWork()
        {
            _directory.Refresh(TeamInvitationSource.Online); _directory.Dispose();
            _net.Reply(0, Online(2)); _directory.Refresh(TeamInvitationSource.Online);
            Assert.That(_directory.Candidates, Is.Empty);
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
        }

        [Test]
        public void NotReady_DoesNotReadNetworkOrRememberChat()
        {
            _net.IsReady = false;
            _directory.Refresh(TeamInvitationSource.Online); _directory.RememberChatPlayer(2);
            Assert.That(_net.Calls, Is.Empty);
            _net.IsReady = true; _directory.Refresh(TeamInvitationSource.Chat);
            Assert.That(_directory.Candidates, Is.Empty);
        }

        [Test]
        public void ProtoRoundTrip_PreservesOnlineDirectoryContract()
        {
            var request = new RecommendFriendsRequest { OnlineOnly = true, Cursor = "opaque", Query = "清铃", Limit = 20 };
            var decoded = RecommendFriendsRequest.Parser.ParseFrom(request.ToByteArray());
            Assert.That(decoded.OnlineOnly, Is.True); Assert.That(decoded.Cursor, Is.EqualTo("opaque"));
            Assert.That(decoded.Query, Is.EqualTo("清铃"));
            var response = Online(2); response.NextCursor = "next"; response.Candidates[0].AppearanceId = "30_han_xiangzi";
            var decodedResponse = RecommendFriendsResponse.Parser.ParseFrom(response.ToByteArray());
            Assert.That(decodedResponse.OnlineDirectory, Is.True);
            Assert.That(decodedResponse.NextCursor, Is.EqualTo("next"));
            Assert.That(decodedResponse.Candidates[0].AppearanceId, Is.EqualTo("30_han_xiangzi"));
        }

        private static RecommendFriendsResponse Online(ulong id)
        {
            var response = new RecommendFriendsResponse { OnlineDirectory = true };
            response.Candidates.Add(new RecommendEntry { CandidatePlayerId = id, IsOnline = true });
            return response;
        }

        private sealed class DirectoryTransport : IBattleTransport
        {
            public ulong PlayerId { get; set; } = 1;
            public bool IsReady { get; set; } = true;
            public object Identity = new object();
            public event Action Disconnected;
            public readonly List<CallRecord> Calls = new List<CallRecord>();
            public void RegisterNotify(uint messageId, Action<MessageContent> handler) { }
            public void SendOneWay(uint messageId, IMessage request) => throw new InvalidOperationException("Directory cannot send invitations");
            public void Call<T>(uint id, IMessage request, MessageParser<T> parser, Action<T> reply, Action<string> error) where T : IMessage<T>
                => Calls.Add(new CallRecord { Id = id, Request = request, Reply = value => reply((T)value), Error = error });
            public void Reply(int index, IMessage value) => Calls[index].Reply(value);
            public void Error(int index, string message) => Calls[index].Error(message);
            public void Disconnect() => Disconnected?.Invoke();
        }

        private sealed class CallRecord
        {
            public uint Id; public IMessage Request;
            public Action<IMessage> Reply; public Action<string> Error;
        }
    }
}
