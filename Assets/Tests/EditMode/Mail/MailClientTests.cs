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
    }
}
