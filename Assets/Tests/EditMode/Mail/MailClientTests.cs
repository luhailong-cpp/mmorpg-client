using System;
using System.Collections.Generic;
using Google.Protobuf;
using Mailpb;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Mail;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Mail
{
    public sealed class MailClientTests
    {
        private sealed class Transport:IBattleTransport
        {
            public ulong PlayerId{get;set;}=7;public bool IsReady{get;set;}=true;public event Action Disconnected;
            public readonly List<uint> Calls=new();public readonly List<IMessage> Requests=new();
            public Action<IMessage> Reply;public Action<string> Error;
            public Action<MessageContent> Notice;
            public void RegisterNotify(uint id,Action<MessageContent> handler){if(id==MessageIds.MailNotifyMailChanged)Notice=handler;}
            public void SendOneWay(uint id,IMessage request){}
            public void Call<T>(uint id,IMessage request,MessageParser<T> parser,Action<T> response,Action<string> error)where T:IMessage<T>{Calls.Add(id);Requests.Add(request);Reply=value=>response((T)value);Error=error;}
            public void Disconnect(){IsReady=false;Disconnected?.Invoke();}
        }
        private Transport _net;private MailUiState _state;private MailClient _client;private object _connection;private long _now,_monotonic;
        [SetUp]public void SetUp(){_net=new Transport();_connection=new object();_now=1800000000;_state=new MailUiState(()=>_now);_state.Reset(7);_client=new MailClient(_net,()=>_connection,_state,()=>_monotonic);}
        [TearDown]public void TearDown()=>_client.Dispose();
        private MailView View(ulong id,MailClaimState claim=MailClaimState.MailClaimUnclaimed)=>new(){MailId=id,Title="奖励",Body="来自服务器",SentMs=1,ExpireMs=(ulong)(_now+100)*1000,IsRead=true,ClaimState=claim,Attachments=new MailAttachments{Currencies={new MailCurrency{Amount=1UL<<40}}}};
        [Test]public void PagedRefreshCommitsOnlyAfterAllResponses()
        {
            _state.Refresh();_net.Reply(new ListMailsResponse{Mails={View(10)},NextBeforeMailId=10});Assert.That(_state.IsBusy,Is.True);Assert.That(_state.Messages,Is.Empty);
            Assert.That(((ListMailsRequest)_net.Requests[1]).BeforeMailId,Is.EqualTo(10));_net.Reply(new ListMailsResponse{Mails={View(9)}});Assert.That(_state.Messages.Count,Is.EqualTo(2));Assert.That(_state.Selected.Rewards[0].Count,Is.EqualTo(1L<<40));Assert.That(_state.IsBusy,Is.False);
        }
        [Test]public void PendingClaimNeverLooksClaimedOrBecomesDeletableAfterExpiry()
        {
            _state.SetSnapshot(new[]{MailClient.Map(View(10))});_state.ClaimSelected();Assert.That(_net.Calls[0],Is.EqualTo(MessageIds.MailClaimMailAttachments));Assert.That(_state.Selected.Claimed,Is.False);
            _net.Reply(new ClaimMailAttachmentsResponse{Results={new MailActionResult{MailId=10,ClaimState=MailClaimState.MailClaimPending,AssetOpId=55}}});Assert.That(_net.Calls[1],Is.EqualTo(MessageIds.MailListMails));_net.Reply(new ListMailsResponse{Mails={View(10,MailClaimState.MailClaimPending)}});
            _now+=200;_state.Tick();Assert.That(_state.CanClaimSelected,Is.False);Assert.That(_state.CanDeleteSelected,Is.False);Assert.That(_state.CleanableCount,Is.Zero);_monotonic=2000;_client.Tick(true);Assert.That(_net.Calls[2],Is.EqualTo(MessageIds.MailListMails));
        }
        [Test]public void UnknownTransportOutcomeQuarantinesFIFOAndIgnoresLateResponse()
        {
            _state.Refresh();var late=_net.Reply;_net.Error("timeout");Assert.That(_client.RequiresReconnect,Is.True);Assert.That(_state.Refresh(),Is.Zero);late(new ListMailsResponse{Mails={View(10)}});Assert.That(_state.Messages,Is.Empty);Assert.That(_net.Calls.Count,Is.EqualTo(1));
        }
        [Test]public void UIBudgetExpiryCannotOpenAnotherCallBeforeTransportSlotCloses()
        {
            _state.Refresh();_now+=10;_state.Tick();_client.Tick(true);Assert.That(_client.RequiresReconnect,Is.True);Assert.That(_state.CanRefresh,Is.False);
        }
        [Test]public void ConnectionChangeMakesOldResponseIneffective()
        {
            _state.Refresh();var late=_net.Reply;_connection=new object();late(new ListMailsResponse{Mails={View(10)}});Assert.That(_state.Messages,Is.Empty);
        }
        [Test]public void ManualAttachmentIsProtectedEvenAfterExpiry()
        {
            var mail=View(10,MailClaimState.MailClaimManual);mail.ExpireMs=1;_state.SetSnapshot(new[]{MailClient.Map(mail)});Assert.That(_state.CanDeleteSelected,Is.False);Assert.That(_state.ClaimSelected(),Is.Zero);Assert.That(_state.ClearRead(),Is.Zero);
        }
        [Test]public void UnknownFutureClaimStateRemainsProtectedAfterExpiry()
        {
            var mail=View(10,(MailClaimState)99);mail.ExpireMs=1;_state.SetSnapshot(new[]{MailClient.Map(mail)});
            Assert.That(_state.Selected.RequiresManual,Is.True);Assert.That(_state.CanDeleteSelected,Is.False);
            Assert.That(_state.ClaimSelected(),Is.Zero);Assert.That(_state.ClearRead(),Is.Zero);
        }
        [Test]public void NoticesCoalesceAndOnlyRefreshWithoutClaimingAnything()
        {
            _state.SetSnapshot(new[]{MailClient.Map(View(10))});for(int i=0;i<8;i++)_net.Notice(new MessageContent{SerializedMessage=new MailChangedEvent{MailId=11}.ToByteString()});
            Assert.That(_client.HasUnseenNotice,Is.True);_client.Tick(false);Assert.That(_net.Calls,Is.Empty);Assert.That(_state.Selected.Claimed,Is.False);
            _client.Tick(true);_client.Tick(true);Assert.That(_net.Calls.Count,Is.EqualTo(1));Assert.That(_net.Calls[0],Is.EqualTo(MessageIds.MailListMails));
            _net.Reply(new ListMailsResponse{Mails={View(11)}});Assert.That(_state.Selected.Claimed,Is.False);Assert.That(_client.HasUnseenNotice,Is.False);
        }
        [Test]public void NoticeFromPreviousConnectionCannotInvalidateCurrentMailbox()
        {
            _connection=new object();_net.Notice(new MessageContent());_client.Tick(true);Assert.That(_net.Calls,Is.Empty);Assert.That(_client.HasUnseenNotice,Is.False);
        }
        [Test]public void ClaimReplyDoesNotRefreshAssetsUntilCompletedListConfirmsClaimed()
        {
            int refreshes=0;_client.AssetRefreshRequired+=()=>refreshes++;
            _state.SetSnapshot(new[]{MailClient.Map(View(10))});_state.ClaimSelected();
            _net.Reply(new ClaimMailAttachmentsResponse{Results={new MailActionResult{MailId=10,ClaimState=MailClaimState.MailClaimClaimed,AssetOpId=55}}});
            Assert.That(refreshes,Is.Zero,"领取回包只触发列表确认，不能推算资产增量");
            _net.Reply(new ListMailsResponse{Mails={View(10,MailClaimState.MailClaimPending)}});
            Assert.That(refreshes,Is.Zero);Assert.That(_state.Selected.Claimed,Is.False);
            _state.Refresh();_net.Reply(new ListMailsResponse{Mails={View(10,MailClaimState.MailClaimClaimed)}});
            Assert.That(refreshes,Is.EqualTo(1));Assert.That(_state.Selected.Claimed,Is.True);
            _state.Refresh();_net.Reply(new ListMailsResponse{Mails={View(10,MailClaimState.MailClaimClaimed)}});
            _client.Tick(true);Assert.That(refreshes,Is.EqualTo(1));Assert.That(_net.Calls.Count,Is.EqualTo(4));
        }
        [Test]public void PagedClaimedListRefreshesAssetsOnceOnlyAfterFinalPage()
        {
            int refreshes=0;_client.AssetRefreshRequired+=()=>refreshes++;
            _state.Refresh();_net.Reply(new ListMailsResponse{Mails={View(10,MailClaimState.MailClaimClaimed)},NextBeforeMailId=10});
            Assert.That(refreshes,Is.Zero);
            _net.Reply(new ListMailsResponse{Mails={View(9,MailClaimState.MailClaimClaimed)}});
            Assert.That(refreshes,Is.EqualTo(1));
        }
        [TestCase(MailClaimState.MailClaimPending)]
        [TestCase(MailClaimState.MailClaimManual)]
        [TestCase((MailClaimState)99)]
        public void PendingManualAndUnknownListStatesNeverRefreshAssets(MailClaimState claim)
        {
            int refreshes=0;_client.AssetRefreshRequired+=()=>refreshes++;
            _state.Refresh();_net.Reply(new ListMailsResponse{Mails={View(10,claim)}});
            Assert.That(refreshes,Is.Zero);Assert.That(_state.Selected.Claimed,Is.False);
        }
        [Test]public void NewConnectionFirstClaimedListRefreshesAgainAndRejectsOldReply()
        {
            int oldRefreshes=0;_client.AssetRefreshRequired+=()=>oldRefreshes++;
            _state.Refresh();_net.Reply(new ListMailsResponse{Mails={View(10,MailClaimState.MailClaimClaimed)}});
            Assert.That(oldRefreshes,Is.EqualTo(1));
            _state.Refresh();var late=_net.Reply;
            _net.Disconnect();_client.Dispose();_connection=new object();_net.IsReady=true;_state.Reset(7);
            _client=new MailClient(_net,()=>_connection,_state,()=>_monotonic);
            int newRefreshes=0;_client.AssetRefreshRequired+=()=>newRefreshes++;
            _state.Refresh();late(new ListMailsResponse{Mails={View(10,MailClaimState.MailClaimClaimed)}});
            Assert.That(oldRefreshes,Is.EqualTo(1));Assert.That(newRefreshes,Is.Zero);
            _net.Reply(new ListMailsResponse{Mails={View(10,MailClaimState.MailClaimClaimed)}});
            Assert.That(newRefreshes,Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ConsumedClaimCallbackCannotQueueAnotherConfirmationOrQuarantineNextCall(bool lateError)
        {
            int refreshes=0;_client.AssetRefreshRequired+=()=>refreshes++;
            _state.SetSnapshot(new[]{MailClient.Map(View(10))});_state.ClaimSelected();
            var claimReply=_net.Reply;var claimError=_net.Error;
            claimReply(new ClaimMailAttachmentsResponse{Results={new MailActionResult{MailId=10,ClaimState=MailClaimState.MailClaimPending,AssetOpId=55}}});
            var listReply=_net.Reply;
            if(lateError)claimError("timeout");
            else claimReply(new ClaimMailAttachmentsResponse{Results={new MailActionResult{MailId=10,ClaimState=MailClaimState.MailClaimClaimed,AssetOpId=55}}});
            Assert.That(_net.Calls.Count,Is.EqualTo(2),"已消费的领取回调不能往同类FIFO再塞确认请求");
            Assert.That(_client.RequiresReconnect,Is.False,"领取已受理后的旧错误不能隔离正在确认的列表");
            Assert.That(_state.IsBusy,Is.True);Assert.That(refreshes,Is.Zero);
            listReply(new ListMailsResponse{Mails={View(10,MailClaimState.MailClaimPending)}});
            Assert.That(_state.Selected.DeliveryPending,Is.True);Assert.That(refreshes,Is.Zero);
        }

        [Test]public void ConsumedPageCannotReplaceOutstandingNextPage()
        {
            int refreshes=0;_client.AssetRefreshRequired+=()=>refreshes++;
            _state.Refresh();var firstPage=_net.Reply;
            firstPage(new ListMailsResponse{Mails={View(10,MailClaimState.MailClaimClaimed)},NextBeforeMailId=10});
            var lastPage=_net.Reply;
            firstPage(new ListMailsResponse{Mails={View(99,MailClaimState.MailClaimPending)},NextBeforeMailId=9});
            Assert.That(_net.Calls.Count,Is.EqualTo(2));Assert.That(_state.IsBusy,Is.True);Assert.That(_state.Messages,Is.Empty);
            lastPage(new ListMailsResponse{Mails={View(9,MailClaimState.MailClaimClaimed)}});
            Assert.That(_state.Messages.Count,Is.EqualTo(2));Assert.That(_state.Messages[0].Id,Is.EqualTo("10"));
            Assert.That(_state.Messages[1].Id,Is.EqualTo("9"));Assert.That(refreshes,Is.EqualTo(1));
        }

        [Test]public void PendingSqlReceiptAfterReconnectRejectsOldOwnerCompletionAndRefreshesOnce()
        {
            int oldRefreshes=0;_client.AssetRefreshRequired+=()=>oldRefreshes++;
            _state.Refresh();var oldList=_net.Reply;
            oldList(new ListMailsResponse{Mails={View(10,MailClaimState.MailClaimPending)}});
            Assert.That(oldRefreshes,Is.Zero);Assert.That(_state.CanClaimSelected,Is.False);
            _state.Refresh();var delayedOldList=_net.Reply;
            _net.Disconnect();_client.Dispose();_connection=new object();_net.IsReady=true;_state.Reset(7);
            _client=new MailClient(_net,()=>_connection,_state,()=>_monotonic);
            int refreshes=0;_client.AssetRefreshRequired+=()=>refreshes++;
            _state.Refresh();var currentList=_net.Reply;
            delayedOldList(new ListMailsResponse{Mails={View(10,MailClaimState.MailClaimClaimed)}});
            Assert.That(_state.IsBusy,Is.True);Assert.That(_state.Messages,Is.Empty);Assert.That(refreshes,Is.Zero);
            currentList(new ListMailsResponse{Mails={View(10,MailClaimState.MailClaimClaimed)}});
            delayedOldList(new ListMailsResponse{Mails={View(10,MailClaimState.MailClaimPending)}});
            currentList(new ListMailsResponse{Mails={View(10,MailClaimState.MailClaimClaimed)}});
            Assert.That(_state.Selected.Claimed,Is.True);Assert.That(_state.Selected.DeliveryPending,Is.False);
            Assert.That(refreshes,Is.EqualTo(1));Assert.That(oldRefreshes,Is.Zero);
        }

        [Test]public void OlderCompletedRefreshCannotOverwriteNewOutstandingOperation()
        {
            int refreshes=0;_client.AssetRefreshRequired+=()=>refreshes++;
            _state.Refresh();var oldList=_net.Reply;
            oldList(new ListMailsResponse{Mails={View(10,MailClaimState.MailClaimPending)}});
            _state.Refresh();var currentList=_net.Reply;
            oldList(new ListMailsResponse{Mails={View(99,MailClaimState.MailClaimClaimed)}});
            Assert.That(_state.IsBusy,Is.True);Assert.That(_state.Selected.Id,Is.EqualTo("10"));Assert.That(refreshes,Is.Zero);
            currentList(new ListMailsResponse{Mails={View(10,MailClaimState.MailClaimClaimed)}});
            Assert.That(_state.Selected.Id,Is.EqualTo("10"));Assert.That(refreshes,Is.EqualTo(1));
        }
    }
}
