using System;
using System.Collections.Generic;
using System.Linq;
using Chatpb;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Social;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Social
{
    public sealed class SocialStateTests
    {
        [Test] public void ProductionStartsEmptyAndCannotLoadSamples()
        {var s=new SocialState();Assert.That(s.Groups,Is.Empty);Assert.That(s.Rumors,Is.Empty);Assert.That(s.Messages(SocialChannel.World),Is.Empty);Assert.Throws<InvalidOperationException>(()=>s.LoadDemo());Assert.That(s.PreviewSend(SocialChannel.World,"你好"),Is.False);}
        [Test] public void ExplicitPreviewContainsThreeCompleteViews()
        {var s=new SocialState(true);Assert.That(s.Groups.Count,Is.EqualTo(2));Assert.That(s.Messages(SocialChannel.World).Count,Is.EqualTo(6));Assert.That(s.Rumors.Count,Is.EqualTo(5));Assert.That(s.People.Count(),Is.EqualTo(6));}
        [TestCase(SocialChannel.Current)] [TestCase(SocialChannel.Guild)] [TestCase(SocialChannel.Team)] [TestCase(SocialChannel.System)]
        public void CurrentServerDoesNotPretendUnsupportedChannelsExist(SocialChannel c)
        {var s=new SocialState();s.RuntimeStatus(true,false,false,"");Assert.That(s.Supports(c),Is.False);Assert.That(s.CanSend(c),Is.False);}
        [Test] public void DraftsAreIndependentAcrossChannels()
        {var s=new SocialState(true);s.SetDraft(SocialChannel.World,"世界草稿");s.SelectChannel(SocialChannel.Guild);s.SetDraft(SocialChannel.Guild,"同门草稿");s.SelectChannel(SocialChannel.World);Assert.That(s.Draft(s.Channel),Is.EqualTo("世界草稿"));Assert.That(s.Draft(SocialChannel.Guild),Is.EqualTo("同门草稿"));}
        [Test] public void MessageValidationUsesUnicodeCodePointsWithoutTruncatingComposition()
        {string emoji=char.ConvertFromUtf32(0x1F33F);string value=string.Concat(Enumerable.Repeat(emoji,120));Assert.That(SocialState.TextLength(value),Is.EqualTo(120));Assert.That(SocialState.ValidMessage(value),Is.True);Assert.That(SocialState.ValidMessage(value+emoji),Is.False);Assert.That(SocialState.ValidMessage(" \n "),Is.False);}
        [Test] public void SharingCardPreservesUnsentComposerDraft()
        {var s=new SocialState(true);s.SetDraft(SocialChannel.World,"稍后发送的问候");Assert.That(s.PreviewSend(SocialChannel.World,"","bamboo"),Is.True);Assert.That(s.Draft(SocialChannel.World),Is.EqualTo("稍后发送的问候"));Assert.That(s.Messages(SocialChannel.World).Last().Card,Is.EqualTo("bamboo"));}
        [Test] public void GroupInvitesDeduplicateAndPreserveExistingMembers()
        {var s=new SocialState(true);Assert.That(s.Invite(new ulong[]{2,5,5,6}),Is.True);Assert.That(s.SelectedGroup.Members.Count,Is.EqualTo(6));Assert.That(s.SelectedGroup.Members.Distinct().Count(),Is.EqualTo(6));}
        [Test] public void GroupCreateChecksDuplicateAndBlankNames()
        {var s=new SocialState(true);Assert.That(s.CreateGroup(" "),Is.False);Assert.That(s.CreateGroup("青云同游"),Is.False);Assert.That(s.CreateGroup("松风雅集"),Is.True);Assert.That(s.SelectedGroup.Name,Is.EqualTo("松风雅集"));Assert.That(s.SelectedGroup.Members,Is.EqualTo(new ulong[]{1}));}
        [Test] public void RapidCreateAndLeaveCannotReuseAnotherGroupsIdentity()
        {var s=new SocialState(true,()=>1000000);s.CreateGroup("甲");long first=s.SelectedGroupId;s.CreateGroup("乙");s.SelectGroup(first);s.LeaveGroup();s.CreateGroup("丙");Assert.That(s.Groups.Select(g=>g.Id).Distinct().Count(),Is.EqualTo(s.Groups.Count));}
        [Test] public void NonOwnerCannotEditGroupAnnouncement()
        {var s=new SocialState(true);s.SelectGroup(102);string original=s.SelectedGroup.Announcement;Assert.That(s.SaveGroup("修改","改公告"),Is.False);Assert.That(s.SelectedGroup.Announcement,Is.EqualTo(original));}
        [Test] public void GroupPinMuteAndLeaveAreIsolatedToSelectedGroup()
        {var s=new SocialState(true);s.SetGroupPinned(false);s.SetGroupAlerts(false);Assert.That(s.SelectedGroup.Pinned,Is.False);Assert.That(s.SelectedGroup.Alerts,Is.False);Assert.That(s.LeaveGroup(),Is.True);Assert.That(s.Groups.Count,Is.EqualTo(1));Assert.That(s.SelectedGroup.Id,Is.EqualTo(102));}
        [Test] public void GroupSendValidatesAndClearsOnlyItsOwnDraft()
        {var s=new SocialState(true);s.SetDraft(SocialChannel.World,"世界保留");s.SelectedGroup.Draft="群聊问候";Assert.That(s.SendGroup("群聊问候"),Is.True);Assert.That(s.SelectedGroup.Draft,Is.Empty);Assert.That(s.Draft(SocialChannel.World),Is.EqualTo("世界保留"));Assert.That(s.SelectedGroup.Messages.Last().Text,Is.EqualTo("群聊问候"));}
        [Test] public void RumorFiltersAndCountdownAreReadOnlyAndClockDriven()
        {long now=1000000;var s=new SocialState(true,()=>now);s.FilterRumors(RumorCategory.Beast,"");Assert.That(s.FilteredRumors().Count(),Is.EqualTo(2));Assert.That(s.Rumors[0].StateLabel(now),Does.Contain("03:00"));now+=181000;Assert.That(s.Rumors[0].StateLabel(now),Does.Contain("现身时刻已到"));Assert.That(s.Rumors[0].Text,Does.Not.Contain("3分钟后"));s.FilterRumors(RumorCategory.Treasure,"不存在");Assert.That(s.FilteredRumors(),Is.Empty);Assert.That(s.PreviewSend(SocialChannel.System,"不能说话"),Is.False);}
        [Test] public void AuthoritativeHistoryFiltersWrongChannelAndSortsOldestFirst()
        {var s=new SocialState();s.Reset(8);s.ApplyHistory(SocialChannel.World,new[]{new ChatMessage{Channel=ChatChannelType.World,Content="新",SendTimeMs=20,SenderPlayerId=9},new ChatMessage{Channel=ChatChannelType.Team,Content="不属于世界",SendTimeMs=15},new ChatMessage{Channel=ChatChannelType.World,Content="旧",SendTimeMs=10,SenderPlayerId=8}});Assert.That(s.Messages(SocialChannel.World).Select(m=>m.Text),Is.EqualTo(new[]{"旧","新"}));Assert.That(s.Messages(SocialChannel.World)[0].SenderName,Is.EqualTo("我"));}
        [Test] public void SessionResetClearsEveryDraftMessageGroupAndRumor()
        {var s=new SocialState(true);s.SetDraft(SocialChannel.Team,"旧草稿");s.Reset(99);Assert.That(s.PlayerId,Is.EqualTo(99));Assert.That(s.Groups,Is.Empty);Assert.That(s.Rumors,Is.Empty);foreach(var c in SocialState.ChatChannels){Assert.That(s.Draft(c),Is.Empty);Assert.That(s.Messages(c),Is.Empty);}}
    }
    public sealed class SocialClientTests
    {
        private SocialFakeTransport _net;private SocialState _state;private SocialClient _client;private object _connection;
        [SetUp]public void SetUp(){_connection=new object();_net=new SocialFakeTransport();_state=new SocialState();_client=new SocialClient(_net,_state,()=>_connection);}
        [TearDown]public void TearDown()=>_client.Dispose();
        [Test]public void RefreshUsesExistingMessageNumberChannelAndFiftyItemLimit()
        {_client.Refresh(SocialChannel.World);Assert.That(_net.Calls.Single().Id,Is.EqualTo(ClientPlayerChatPullChatHistoryHandler.MessageId));var request=(PullChatHistoryRequest)_net.Calls[0].Request;Assert.That(request.Channel,Is.EqualTo(ChatChannelType.World));Assert.That(request.Limit,Is.EqualTo(50));Assert.That(request.PeerPlayerId,Is.Zero);}
        [TestCase(SocialChannel.Current)][TestCase(SocialChannel.Guild)][TestCase(SocialChannel.Team)][TestCase(SocialChannel.System)]
        public void UnsupportedChannelsNeverSendRpc(SocialChannel channel)
        {_client.Refresh(channel);Assert.That(_client.Send(channel,"问候"),Is.False);Assert.That(_net.Calls,Is.Empty);}
        [Test]public void EmptyOrOverLimitTextNeverReachesTransport()
        {Assert.That(_client.Send(SocialChannel.World," "),Is.False);Assert.That(_client.Send(SocialChannel.World,new string('字',121)),Is.False);Assert.That(_net.Calls,Is.Empty);}
        [Test]public void SendUsesBusinessRequestIdButDoesNotInsertOptimisticMessage()
        {_state.SetDraft(SocialChannel.World,"你好");Assert.That(_client.Send(SocialChannel.World,"你好"),Is.True);var request=(SendChatRequest)_net.Calls[0].Request;Assert.That(_net.Calls[0].Id,Is.EqualTo(ClientPlayerChatSendChatHandler.MessageId));Assert.That(request.RequestId.Length,Is.EqualTo(32));Assert.That(request.Message.Content,Is.EqualTo("你好"));Assert.That(_state.Messages(SocialChannel.World),Is.Empty);Assert.That(_state.Draft(SocialChannel.World),Is.EqualTo("你好"));}
        [Test]public void SuccessfulEmptyProtobufAckClearsAcknowledgedDraftAndPullsHistory()
        {_state.SetDraft(SocialChannel.World,"你好");_client.Send(SocialChannel.World,"你好");_net.Calls[0].Success(new SendChatResponse());Assert.That(_state.Draft(SocialChannel.World),Is.Empty);Assert.That(_net.Calls.Count,Is.EqualTo(2));Assert.That(_net.Calls[1].Id,Is.EqualTo(ClientPlayerChatPullChatHistoryHandler.MessageId));Assert.That(_state.Messages(SocialChannel.World),Is.Empty);var response=new PullChatHistoryResponse();response.Messages.Add(new ChatMessage{SenderPlayerId=11,Channel=ChatChannelType.World,Content="你好",SendTimeMs=123});_net.Calls[1].Success(response);Assert.That(_state.Messages(SocialChannel.World).Single().Text,Is.EqualTo("你好"));}
        [Test]public void NewDraftTypedDuringSendIsPreservedAfterAck()
        {_state.SetDraft(SocialChannel.World,"旧消息");_client.Send(SocialChannel.World,"旧消息");_state.SetDraft(SocialChannel.World,"下一句");_net.Calls[0].Success(new SendChatResponse());Assert.That(_state.Draft(SocialChannel.World),Is.EqualTo("下一句"));}
        [Test]public void InnerServerRejectionPreservesDraftAndAllowsRetry()
        {_state.SetDraft(SocialChannel.World,"你好");_client.Send(SocialChannel.World,"你好");_net.Calls[0].Success(new SendChatResponse{ErrorMessage=new TipInfoMessage{Id=(uint)common_error.KRateLimitExceeded}});Assert.That(_state.Draft(SocialChannel.World),Is.EqualTo("你好"));Assert.That(_client.RequiresReconnect,Is.False);Assert.That(_state.Status,Does.Contain("1008"));}
        [TestCase(common_error.KInvalidParameter)][TestCase(common_error.KFeatureUnavailable)][TestCase(common_error.KRateLimitExceeded)][TestCase(common_error.KMessageSizeExceeded)]public void DefiniteOuterServerTipDoesNotRequireRelogin(common_error tip)
        {_client.Refresh(SocialChannel.World);_net.Calls[0].Error("server tip="+(uint)tip);Assert.That(_client.RequiresReconnect,Is.False);_client.Refresh(SocialChannel.World);Assert.That(_net.Calls.Count,Is.EqualTo(2));}
        [Test]public void OuterServiceUnavailableDoesNotClaimRejectionOrRetryWithNewBusinessId()
        {_state.SetDraft(SocialChannel.World,"回包未知");_client.Send(SocialChannel.World,"回包未知");_net.Calls[0].Error("server tip="+(uint)common_error.KServiceUnavailable);Assert.That(_client.RequiresReconnect,Is.True);Assert.That(_state.Draft(SocialChannel.World),Is.EqualTo("回包未知"));Assert.That(_state.Messages(SocialChannel.World),Is.Empty);Assert.That(_state.Status,Does.Contain("尚未确认"));Assert.That(_client.Send(SocialChannel.World,"回包未知"),Is.False);Assert.That(_net.Calls.Count,Is.EqualTo(1));}
        [Test]public void TimeoutDoesNotClaimSuccessAndBlocksAmbiguousRetryUntilConnectionChanges()
        {_client.Send(SocialChannel.World,"未确认消息");_net.Calls[0].Error("timeout");Assert.That(_client.RequiresReconnect,Is.True);Assert.That(_state.Messages(SocialChannel.World),Is.Empty);_client.Send(SocialChannel.World,"未确认消息");Assert.That(_net.Calls.Count,Is.EqualTo(1));_connection=new object();_client.ObserveConnection();Assert.That(_client.RequiresReconnect,Is.False);_client.Refresh(SocialChannel.World);Assert.That(_net.Calls.Count,Is.EqualTo(2));}
        [Test]public void ResetRejectsLateReplyAndDoesNotReusePendingSlot()
        {_client.Refresh(SocialChannel.World);_client.Reset();_net.Calls[0].Success(History("旧会话消息"));Assert.That(_state.Messages(SocialChannel.World),Is.Empty);Assert.That(_client.RequiresReconnect,Is.True);}
        [Test]public void ConnectionChangeIsRejectedBeforeNextRootObservation()
        {_client.Refresh(SocialChannel.World);_connection=new object();_net.Calls[0].Success(History("旧连接消息"));Assert.That(_state.Messages(SocialChannel.World),Is.Empty);}
        [Test]public void PlayerChangeRejectsOldReply()
        {_client.Refresh(SocialChannel.World);_net.PlayerId=22;_net.Calls[0].Success(History("旧角色消息"));Assert.That(_state.Messages(SocialChannel.World),Is.Empty);_client.Reset();Assert.That(_state.PlayerId,Is.EqualTo(22));}
        [Test]public void ConcurrentRefreshesCoalesceWithoutDuplicateRequest()
        {_client.Refresh(SocialChannel.World);_client.Refresh(SocialChannel.World);_client.Refresh(SocialChannel.World);Assert.That(_net.Calls.Count,Is.EqualTo(1));_net.Calls[0].Success(History("真实消息"));Assert.That(_net.Calls.Count,Is.EqualTo(2));}
        [Test]public void DisconnectedTransportCannotSend()
        {_net.IsReady=false;Assert.That(_client.Send(SocialChannel.World,"你好"),Is.False);Assert.That(_net.Calls,Is.Empty);}
        [Test]public void LiveClientRejectsPreviewState()
        {Assert.Throws<ArgumentException>(()=>new SocialClient(_net,new SocialState(true)));}
        private static PullChatHistoryResponse History(string text){var response=new PullChatHistoryResponse();response.Messages.Add(new ChatMessage{Channel=ChatChannelType.World,SenderPlayerId=44,Content=text,SendTimeMs=1000});return response;}
    }
    internal sealed class SocialFakeTransport:IBattleTransport
    {
        internal sealed class CallRecord {public uint Id;public IMessage Request;public Action<IMessage> Success;public Action<string> Error;}
        public readonly List<CallRecord> Calls=new();public ulong PlayerId{get;set;}=11;public bool IsReady{get;set;}=true;public event Action Disconnected;
        public void Disconnect(){IsReady=false;Disconnected?.Invoke();}
        public void RegisterNotify(uint id,Action<MessageContent> handler){}
        public void SendOneWay(uint id,IMessage request)=>throw new InvalidOperationException("Social must use acknowledged RPCs.");
        public void Call<T>(uint id,IMessage request,MessageParser<T> parser,Action<T> success,Action<string> error)where T:IMessage<T>
        {Calls.Add(new CallRecord{Id=id,Request=request,Success=response=>success((T)response),Error=error});}
    }
}
