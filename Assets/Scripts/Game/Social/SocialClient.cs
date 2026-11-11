using System;
using Google.Protobuf;
using Chatpb;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.Social
{
    /// <summary>
    /// Live text chat only. No invented group service, profile metadata or rumor schema.
    /// 世界频道的新消息靠服务端推送 NotifyWorldChat(至多一次);打开面板、切换频道、点刷新和发送成功后拉一次历史补齐,不定时轮询。
    /// </summary>
    public sealed class SocialClient : IDisposable
    {
        public const string RecoveryMessage="聊天请求状态尚未确认，请重新登录角色后再试。";
        private readonly IBattleTransport _net; private readonly SocialState _state; private readonly Func<object> _identity;
        private object _observed; private int _generation; private bool _busy,_reconnect,_disposed;
        private SocialChannel? _queued;
        public bool Busy=>_busy; public bool RequiresReconnect=>_reconnect;
        public SocialClient(IBattleTransport net,SocialState state,Func<object> connectionIdentity=null)
        {
            _net=net??throw new ArgumentNullException(nameof(net));_state=state??throw new ArgumentNullException(nameof(state));
            if(state.IsPreview)throw new ArgumentException("Live transport cannot bind to preview state.");
            _identity=connectionIdentity??(()=>net);_observed=_identity();_net.Disconnected+=Disconnected;_state.Reset(net.PlayerId);Publish("请刷新聊天频道。");
            // 同一消息号只保留最后注册的处理器:换角色或重连后新建的 SocialClient 会替换掉旧的;旧实例 Dispose 之后它的处理器直接返回。
            _net.RegisterNotify(ClientPlayerChatRpc.NotifyWorldChatMessageId,WorldChatPushed);
        }
        // 世界频道推送(docs/design/chat-world-channel-fanout.md §4):只收当前已就绪的角色;解析失败整条丢弃,下次拉历史补上。
        private void WorldChatPushed(MessageContent content)
        {
            if(_disposed||content==null||!_net.IsReady||_net.PlayerId==0)return;
            ChatMessage message;
            try{message=ChatMessage.Parser.ParseFrom(content.SerializedMessage);}
            catch(InvalidProtocolBufferException){return;}
            _state.ApplyPushed(SocialChannel.World,message);
        }
        public void ObserveConnection()
        {
            if(_disposed)return;object id=_identity();if(ReferenceEquals(id,_observed))return;
            _observed=id;_busy=false;_reconnect=false;Reset();
        }
        public void Reset()
        {
            if(_busy)_reconnect=true;++_generation;_busy=false;_queued=null;_state.Reset(_net.PlayerId);Publish(_reconnect?RecoveryMessage:"频道已清理，请刷新。");
        }
        private void Disconnected(){_observed=_identity();_busy=false;_reconnect=false;Reset();}
        public void Refresh(SocialChannel channel)
        {
            ObserveConnection();if(_disposed)return;
            if(!Supported(channel)){Publish("此频道服务尚未开放。");return;}
            if(_busy){_queued=channel;return;}
            Request(ClientPlayerChatRpc.PullChatHistoryMessageId,new PullChatHistoryRequest{Channel=Proto(channel),Limit=50},PullChatHistoryResponse.Parser,response=>
            {if(!Accept(response.ErrorMessage))return;_state.ApplyHistory(channel,response.Messages);Publish(response.Messages.Count==0?"此频道暂无消息。":"频道历史已更新。");});
        }
        public bool Send(SocialChannel channel,string text)
        {
            text=(text??"").Trim();ObserveConnection();
            if(channel==SocialChannel.System||!Supported(channel)||!SocialState.ValidMessage(text)){Publish("请在可发言频道输入1–120字。");return false;}
            if(!Ready())return false;
            var request=new SendChatRequest{RequestId=Guid.NewGuid().ToString("N"),Message=new ChatMessage{SenderPlayerId=_net.PlayerId,Channel=Proto(channel),Content=text}};
            Request(ClientPlayerChatRpc.SendChatMessageId,request,SendChatResponse.Parser,response=>
            {
                if(!Accept(response.ErrorMessage))return;
                // Only the acknowledged draft is cleared. Text typed while the request was pending survives.
                if(_state.Draft(channel).Trim()==text)_state.SetDraft(channel,"");
                Publish("服务器已接受消息，正在更新频道。");Refresh(channel);
            });return true;
        }
        private static bool Supported(SocialChannel c)=>c==SocialChannel.World;
        private static ChatChannelType Proto(SocialChannel c)=>c==SocialChannel.World?ChatChannelType.World:c==SocialChannel.Team?ChatChannelType.Team:ChatChannelType.System;
        private bool Ready()
        {
            if(_disposed||_busy)return false;if(_reconnect){Publish(RecoveryMessage);return false;}
            if(!_net.IsReady||_net.PlayerId==0){Publish("请进入角色后使用聊天。");return false;}return true;
        }
        private bool Accept(TipInfoMessage tip)
        {if(tip==null||tip.Id==0)return true;Publish("聊天服务未完成请求（"+tip.Id+"），请稍后重试。");return false;}
        private void Publish(string message)=>_state.RuntimeStatus(_net.IsReady&&_net.PlayerId!=0,_busy,_reconnect,message);
        private void Request<T>(uint id,IMessage request,MessageParser<T> parser,Action<T> success) where T:IMessage<T>
        {
            if(!Ready())return;int generation=++_generation;ulong player=_net.PlayerId;object connection=_observed;
            _busy=true;Publish("正在同步频道，请稍候…");
            _net.Call(id,request,parser,response=>
            {
                if(!Current(generation,player,connection))return;_busy=false;success(response);
                if(!_busy&&_queued.HasValue){var next=_queued.Value;_queued=null;Refresh(next);}
            },error=>
            {
                if(!Current(generation,player,connection))return;_busy=false;_queued=null;
                bool rejected=IsDefiniteRejection(error);_reconnect=!rejected;
                Publish(rejected?"服务器暂未接受请求，请稍后刷新重试。":RecoveryMessage);
            });
        }
        private static bool IsDefiniteRejection(string error)
        {
            const string prefix="server tip=";
            if(string.IsNullOrEmpty(error)||!error.StartsWith(prefix,StringComparison.Ordinal)||!uint.TryParse(error.Substring(prefix.Length),out uint tip))return false;
            // ServiceUnavailable also represents an upstream timeout: a send may already have been stored.
            return tip==(uint)common_error.KInvalidParameter||tip==(uint)common_error.KFeatureUnavailable||tip==(uint)common_error.KRateLimitExceeded||tip==(uint)common_error.KMessageSizeExceeded;
        }
        private bool Current(int generation,ulong player,object connection)=>!_disposed&&generation==_generation&&_net.IsReady&&_net.PlayerId==player&&ReferenceEquals(connection,_identity());
        public void Dispose(){_disposed=true;++_generation;_busy=false;_queued=null;_net.Disconnected-=Disconnected;}
    }
}
