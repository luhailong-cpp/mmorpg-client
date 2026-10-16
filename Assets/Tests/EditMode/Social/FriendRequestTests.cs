using System;
using System.Collections.Generic;
using System.Linq;
using Friendpb;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Social;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI.Ugui.Social;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Social
{
    public sealed class FriendClientTests
    {
        private FriendTransport _net;
        private FriendClient _client;
        [SetUp] public void SetUp() { _net = new FriendTransport(); _client = new FriendClient(_net, () => _net.Identity); }
        [TearDown] public void TearDown() => _client.Dispose();

        [Test]
        public void PendingListKeepsServerRequestIdAndClonesTheResponse()
        {
            _client.RefreshPending();
            Assert.That(_net.Calls.Single().Id, Is.EqualTo(ClientPlayerFriendGetPendingRequestsHandler.MessageId));
            var response = Pending(101);
            _net.Reply(0, response); response.Requests[0].RequestId = 999;
            Assert.That(_client.Pending.Single().RequestId, Is.EqualTo(101));
            Assert.That(_client.HasFreshPending, Is.True);
        }

        [TestCase(true)] [TestCase(false)]
        public void DecisionsSendObservedRequestIdAndSenderHint(bool accept)
        {
            Load(101);
            Assert.That(accept ? _client.AcceptFriend(101) : _client.RejectFriend(101), Is.True);
            var call = _net.Calls[1];
            if (accept)
            {
                var request = (AcceptFriendRequest)call.Request;
                Assert.That(request.RequestId, Is.EqualTo(101));
                Assert.That(request.FromPlayerId, Is.EqualTo(20));
                Assert.That(AcceptFriendRequest.Parser.ParseFrom(request.ToByteArray()).RequestId, Is.EqualTo(101));
            }
            else
            {
                var request = (RejectFriendRequest)call.Request;
                Assert.That(request.RequestId, Is.EqualTo(101));
                Assert.That(request.FromPlayerId, Is.EqualTo(20));
                Assert.That(RejectFriendRequest.Parser.ParseFrom(request.ToByteArray()).RequestId, Is.EqualTo(101));
            }
        }

        [TestCase(true)] [TestCase(false)]
        public void ZeroIdCannotReachDecisionRpcAndRefreshesInstead(bool accept)
        {
            Load(0);
            Assert.That(_client.Status, Does.Contain("缺少编号"));
            Assert.That(accept ? _client.AcceptFriend(0) : _client.RejectFriend(0), Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(_net.Calls.Last().Id, Is.EqualTo(ClientPlayerFriendGetPendingRequestsHandler.MessageId));
        }

        [Test]
        public void StaleUiRequestCannotOperateOnNewGenerationFromTheSameSender()
        {
            Load(101);
            _client.RefreshPending(); _net.Reply(1, Pending(202));
            Assert.That(_client.AcceptFriend(101), Is.False);
            Assert.That(_net.Calls.Last().Id, Is.EqualTo(ClientPlayerFriendGetPendingRequestsHandler.MessageId));
            Assert.That(_client.Pending.Single().RequestId, Is.EqualTo(202));
        }

        [Test]
        public void SuccessfulDecisionDoesNotDeleteBySenderAndWaitsForAuthority()
        {
            Load(101); _client.AcceptFriend(101);
            _net.Reply(1, new AcceptFriendResponse());
            Assert.That(_client.Pending.Single().RequestId, Is.EqualTo(101));
            Assert.That(_client.HasFreshPending, Is.False);
            Assert.That(_net.Calls[2].Id, Is.EqualTo(ClientPlayerFriendGetPendingRequestsHandler.MessageId));
            _net.Reply(2, Pending(202));
            Assert.That(_client.Pending.Single().RequestId, Is.EqualTo(202));
            Assert.That(_client.RejectFriend(202), Is.True);
            Assert.That(((RejectFriendRequest)_net.Calls[3].Request).RequestId, Is.EqualTo(202));
        }

        [Test]
        public void OldDecisionReplyCannotClearNewSessionApplicationFromTheSameSender()
        {
            Load(101); _client.AcceptFriend(101);
            _client.Reset(); _client.RefreshPending(); _net.Reply(2, Pending(202));
            _net.Reply(1, new AcceptFriendResponse());
            Assert.That(_client.Pending.Single().RequestId, Is.EqualTo(202));
            Assert.That(_client.HasFreshPending, Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(3));
        }

        [Test]
        public void OldReadCannotOverwriteNewSessionList()
        {
            _client.RefreshPending(); _client.Reset(); _client.RefreshPending();
            _net.Reply(1, Pending(202)); _net.Reply(0, Pending(101));
            Assert.That(_client.Pending.Single().RequestId, Is.EqualTo(202));
        }

        [Test]
        public void ConnectionChangeBeforeObservationRejectsOldCallback()
        {
            _client.RefreshPending(); _net.Identity = new object();
            _net.Reply(0, Pending(101));
            Assert.That(_client.Pending, Is.Empty);
            _client.ObserveConnection(); _client.RefreshPending(); _net.Reply(1, Pending(202));
            Assert.That(_client.Pending.Single().RequestId, Is.EqualTo(202));
        }

        [Test]
        public void PlayerChangeRejectsOldCallbackAndReloadsForNewRecipient()
        {
            _client.RefreshPending(); _net.PlayerId = 11;
            _net.Reply(0, Pending(101)); _client.ObserveConnection(); _client.RefreshPending();
            var response = Pending(202); response.Requests[0].ToPlayerId = 11; _net.Reply(1, response);
            Assert.That(_client.Pending.Single().RequestId, Is.EqualTo(202));
        }

        [Test]
        public void UnknownWriteResultPreservesApplicationAndRequiresFreshList()
        {
            Load(101); _client.AcceptFriend(101); _net.Fail(1);
            Assert.That(_client.Pending.Single().RequestId, Is.EqualTo(101));
            Assert.That(_client.Status, Does.Contain("尚未确认"));
            Assert.That(_client.HasFreshPending, Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(_client.AcceptFriend(101), Is.False);
            Assert.That(_net.Calls.Last().Id, Is.EqualTo(ClientPlayerFriendGetPendingRequestsHandler.MessageId));
        }

        [Test]
        public void BusinessRejectionIsVisibleAndDoesNotEraseApplication()
        {
            Load(101); _client.RejectFriend(101);
            uint tip = (uint)friend_error.KFriendAlreadyFriends;
            _net.Reply(1, new RejectFriendResponse { ErrorMessage = new TipInfoMessage { Id = tip } });
            Assert.That(_client.Pending.Single().RequestId, Is.EqualTo(101));
            Assert.That(_client.Status, Does.Contain(tip.ToString()));
            Assert.That(_client.HasFreshPending, Is.False);
        }

        [Test]
        public void RepeatedPendingAddWithSameServerIdIsSuccessful()
        {
            Assert.That(_client.AddFriend(20), Is.True);
            _net.Reply(0, new AddFriendResponse { RequestId = 101 });
            Assert.That(_client.LastSubmittedRequestId, Is.EqualTo(101));
            Assert.That(_client.AddFriend(20), Is.True);
            _net.Reply(1, new AddFriendResponse { RequestId = 101 });
            Assert.That(_client.LastSubmittedRequestId, Is.EqualTo(101));
            Assert.That(_client.Status, Does.Contain("已提交"));
            Assert.That(_client.Pending, Is.Empty);
        }

        [Test]
        public void MissingAddResponseIdDoesNotClaimSubmissionSuccess()
        {
            _client.AddFriend(20); _net.Reply(0, new AddFriendResponse());
            Assert.That(_client.LastSubmittedRequestId, Is.Zero);
            Assert.That(_client.Status, Does.Contain("未返回申请编号"));
        }

        [Test]
        public void PushIsOnlyARefreshHintAndCannotChangeApplicationLocally()
        {
            Load(101); _client.AcceptFriend(101); _net.Notify(); _net.Notify();
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(_client.Pending.Single().RequestId, Is.EqualTo(101));
            _net.Reply(1, new AcceptFriendResponse());
            Assert.That(_net.Calls.Count, Is.EqualTo(3));
            _net.Reply(2, Pending(202));
            Assert.That(_client.Pending.Single().RequestId, Is.EqualTo(202));
        }

        [Test]
        public void DisconnectClearsAuthorityAndDisposedClientIgnoresLateResults()
        {
            Load(101); _client.RejectFriend(101); _net.Disconnect();
            Assert.That(_client.Pending, Is.Empty);
            Assert.That(_client.HasFreshPending, Is.False);
            _client.Dispose(); _net.Reply(1, new RejectFriendResponse());
            Assert.That(_client.Pending, Is.Empty);
        }

        [Test]
        public void ProtocolFieldNumbersMatchFrozenContract()
        {
            Assert.That(FriendRequest.RequestIdFieldNumber, Is.EqualTo(5));
            Assert.That(AddFriendResponse.RequestIdFieldNumber, Is.EqualTo(2));
            Assert.That(AcceptFriendRequest.RequestIdFieldNumber, Is.EqualTo(3));
            Assert.That(RejectFriendRequest.RequestIdFieldNumber, Is.EqualTo(3));
        }

        private void Load(ulong id) { _client.RefreshPending(); _net.Reply(0, Pending(id)); }
        internal static GetPendingRequestsResponse Pending(ulong id)
        {
            var response = new GetPendingRequestsResponse();
            response.Requests.Add(new FriendRequest { RequestId = id, FromPlayerId = 20, ToPlayerId = 10 });
            return response;
        }
    }

    public sealed class FriendRequestsWindowTests
    {
        private GameObject _parent;
        private FriendTransport _net;
        private FriendClient _client;
        private FriendRequestsWindow _window;
        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("FriendWindowTest", typeof(RectTransform));
            _net = new FriendTransport(); _client = new FriendClient(_net, () => _net.Identity);
            _window = new FriendRequestsWindow(_parent.transform); _window.Bind(_client);
        }
        [TearDown]
        public void TearDown() { _window.Dispose(); _client.Dispose(); UnityEngine.Object.DestroyImmediate(_parent); }

        [Test]
        public void ActualWindowButtonSendsTheDisplayedApplicationId()
        {
            _window.Show(); _net.Reply(0, FriendClientTests.Pending(101));
            Button("AcceptFriend_101").onClick.Invoke();
            Assert.That(((AcceptFriendRequest)_net.Calls[1].Request).RequestId, Is.EqualTo(101));
        }

        [Test]
        public void SocialEntryOpensTheActualPendingApplicationWindow()
        {
            using var social = new SocialWindow(_parent.transform, new SocialState());
            social.FriendRequestsRequested += _window.Show;
            social.Show();
            social.Root.GetComponentsInChildren<Button>(true).Single(button => button.name == "SocialFriendRequests")
                .onClick.Invoke();
            Assert.That(_window.IsVisible, Is.True);
            Assert.That(_net.Calls.Single().Id, Is.EqualTo(ClientPlayerFriendGetPendingRequestsHandler.MessageId));
        }

        [Test]
        public void ManualAddButtonSendsTheEnteredPlayerId()
        {
            _window.Show(); _net.Reply(0, new GetPendingRequestsResponse());
            _window.Root.GetComponentsInChildren<TMP_InputField>(true).Single().text = "20";
            Button("FriendAdd").onClick.Invoke();
            Assert.That(((AddFriendRequest)_net.Calls[1].Request).TargetPlayerId, Is.EqualTo(20));
            _net.Reply(1, new AddFriendResponse { RequestId = 101 });
            Assert.That(_window.Root.GetComponentsInChildren<TMP_Text>().Any(text => text.text.Contains("已提交")), Is.True);
        }

        [Test]
        public void LegacyApplicationIsVisibleButCannotBeAcceptedOrRejected()
        {
            _window.Show(); _net.Reply(0, FriendClientTests.Pending(0));
            Assert.That(Button("AcceptFriend_0").interactable, Is.False);
            Assert.That(Button("RejectFriend_0").interactable, Is.False);
            Assert.That(_window.Root.GetComponentsInChildren<TMP_Text>().Any(text => text.text.Contains("缺少编号")), Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
        }

        private Button Button(string name) => _window.Root.GetComponentsInChildren<Button>(true).Single(button => button.name == name);
    }

    internal sealed class FriendTransport : IBattleTransport
    {
        internal sealed class CallRecord
        { public uint Id; public IMessage Request; public Action<IMessage> Success; public Action<string> Error; }
        public readonly List<CallRecord> Calls = new();
        private Action<MessageContent> _notify;
        public object Identity = new object();
        public ulong PlayerId { get; set; } = 10;
        public bool IsReady { get; set; } = true;
        public event Action Disconnected;
        public void Disconnect() { IsReady = false; Disconnected?.Invoke(); }
        public void Reply(int index, IMessage response) => Calls[index].Success(response);
        public void Fail(int index) => Calls[index].Error("timeout");
        public void Notify() => _notify?.Invoke(new MessageContent { SerializedMessage = new FriendEventS2C().ToByteString() });
        public void RegisterNotify(uint id, Action<MessageContent> handler) => _notify = handler;
        public void SendOneWay(uint id, IMessage request) => throw new InvalidOperationException("好友写入必须有回包。");
        public void Call<T>(uint id, IMessage request, MessageParser<T> parser, Action<T> success, Action<string> error) where T : IMessage<T>
        { Calls.Add(new CallRecord { Id = id, Request = request, Success = response => success((T)response), Error = error }); }
    }
}
