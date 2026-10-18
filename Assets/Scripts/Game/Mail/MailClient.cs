using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Google.Protobuf;
using Mailpb;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;

namespace MmorpgClient.Game.Mail
{
    /// <summary>真实gate邮件适配。资产只认服务端状态，连接/角色/操作代次共同拦截旧回包。</summary>
    public sealed class MailClient : IDisposable
    {
        private readonly IBattleTransport _net;
        private readonly Func<object> _connection;
        private readonly object _boundConnection;
        private readonly ulong _player;
        private readonly MailUiState _state;
        // 只比较本连接已完成的权威列表；领取受理、PENDING 和分页中间结果都不能触发到账刷新。
        private HashSet<string> _claimedMailIds = new(StringComparer.Ordinal);
        private bool _disposed, _waiting, _queued;
        private int _generation, _uiGeneration;
        private long _nextPendingRefresh;
        private readonly Func<long> _monotonicMs;
        public bool RequiresReconnect { get; private set; }
        public bool HasUnseenNotice => _queued;
        public event Action AssetRefreshRequired;
        public MailClient(IBattleTransport net, Func<object> connection, MailUiState state, Func<long> monotonicMs=null)
        {
            _net=net??throw new ArgumentNullException(nameof(net));_connection=connection??throw new ArgumentNullException(nameof(connection));
            _state=state??throw new ArgumentNullException(nameof(state));_boundConnection=connection();_player=net.PlayerId;
            _monotonicMs=monotonicMs??(()=>System.Diagnostics.Stopwatch.GetTimestamp()*1000/System.Diagnostics.Stopwatch.Frequency);
            _state.ActionRequested+=Request;_net.Disconnected+=Disconnected;
            _net.RegisterNotify(MessageIds.MailNotifyMailChanged,_=>{if(Current())_queued=true;});
            _state.BeginOnline();
        }
        private bool Current()=>!_disposed&&!RequiresReconnect&&_net.IsReady&&_net.PlayerId==_player&&_player!=0&&ReferenceEquals(_boundConnection,_connection());
        public void Tick(bool visible)
        {
            if(!Current())return;
            // UI的10秒预算先于GameClient的15秒FIFO超时；旧槽仍在时不能再排同类请求。
            if(_waiting&&!_state.IsPending(_uiGeneration)){Quarantine();return;}
            if(!visible||_state.IsBusy)return;
            bool pending=_state.Messages.Any(m=>m.DeliveryPending);
            if(_queued||(pending&&_monotonicMs()>=_nextPendingRefresh))
            {_queued=false;_nextPendingRefresh=_monotonicMs()+2000;_state.Refresh();}
        }
        private void Request(MailRequest request)
        {
            if(!Current()){_state.Fail(request.Generation,"邮件连接尚未就绪，请重新进入角色。");return;}
            _uiGeneration=request.Generation;int generation=++_generation;
            var ids=new List<ulong>();foreach(string id in request.MessageIds){if(!ulong.TryParse(id,NumberStyles.None,CultureInfo.InvariantCulture,out ulong value)||value==0){_state.Fail(request.Generation,"邮件标识无效，请刷新。");return;}ids.Add(value);}
            if(ids.Count>100&&request.Operation!=MailOperation.ClaimAll){_state.Fail(request.Generation,"每次最多操作100封邮件，请分批领取。");return;}
            switch(request.Operation)
            {
                case MailOperation.Refresh: Fetch(generation,0,new List<MailMessage>(),"");break;
                case MailOperation.Read: Call(generation,MessageIds.MailReadMail,new ReadMailRequest{MailId=ids[0]},ReadMailResponse.Parser,r=>Fetch(generation,0,new List<MailMessage>(),DescribeTip(r.ErrorMessage)));break;
                case MailOperation.Claim:
                case MailOperation.ClaimAll:
                    ClaimBatch(generation,ids,0,"");break;
                case MailOperation.Delete:
                    var delete=new DeleteMailsRequest();delete.MailIds.Add(ids);Call(generation,MessageIds.MailDeleteMails,delete,DeleteMailsResponse.Parser,r=>Fetch(generation,0,new List<MailMessage>(),r.ProtectedMailIds.Count>0?"尚未送达或未领取的附件已保留。":DescribeTip(r.ErrorMessage)));break;
                case MailOperation.ClearRead: Call(generation,MessageIds.MailClearReadMails,new ClearReadMailsRequest(),ClearReadMailsResponse.Parser,r=>Fetch(generation,0,new List<MailMessage>(),r.ProtectedMailIds.Count>0?"需要保留的附件邮件未清理。":DescribeTip(r.ErrorMessage)));break;
            }
        }
        private void ClaimBatch(int generation,List<ulong> ids,int offset,string message)
        {
            var request=new ClaimMailAttachmentsRequest();request.MailIds.Add(ids.Skip(offset).Take(100));
            Call(generation,MessageIds.MailClaimMailAttachments,request,ClaimMailAttachmentsResponse.Parser,response=>
            {string result=DescribeTip(response.ErrorMessage);if(result.Length==0)result=response.Results.Select(x=>DescribeTip(x.ErrorMessage)).FirstOrDefault(x=>x.Length>0)??message;
                if(response.ErrorMessage?.Id>0||offset+100>=ids.Count){Fetch(generation,0,new List<MailMessage>(),result.Length==0?"附件状态已更新。":result);return;}
                ClaimBatch(generation,ids,offset+100,result);});
        }
        private void Fetch(int generation,ulong before,List<MailMessage> messages,string message)
        {
            Call(generation,MessageIds.MailListMails,new ListMailsRequest{BeforeMailId=before,Limit=100},ListMailsResponse.Parser,response=>
            {
                string failure=DescribeTip(response.ErrorMessage);if(failure.Length>0){_state.Fail(_uiGeneration,failure);return;}
                var seen=new HashSet<string>(messages.Select(m=>m.Id));foreach(var mail in response.Mails){var view=Map(mail);if(seen.Add(view.Id))messages.Add(view);}
                ulong next=response.NextBeforeMailId;
                if(next!=0)
                {
                    if((before!=0&&next>=before)||messages.Count>=1000){_state.Fail(_uiGeneration,"邮件列表超出本次同步范围，请稍后重试。");return;}
                    Fetch(generation,next,messages,message);return;
                }
                var claimed=new HashSet<string>(messages.Where(m=>m.Claimed).Select(m=>m.Id),StringComparer.Ordinal);
                bool refreshAssets=claimed.Any(id=>!_claimedMailIds.Contains(id));
                if(!_state.Complete(_uiGeneration,messages,message))return;
                _claimedMailIds=claimed;_nextPendingRefresh=_monotonicMs()+2000;
                // 新连接首次读到已领取也重拉快照；重复列表不反复读背包，不按附件计算增量。
                if(refreshAssets&&Current())AssetRefreshRequired?.Invoke();
            });
        }
        private void Call<T>(int generation,uint id,IMessage request,MessageParser<T> parser,Action<T> receive)where T:IMessage<T>
        {
            if(!Current()||generation!=_generation||!_state.IsPending(_uiGeneration))return;_waiting=true;
            _net.Call(id,request,parser,response=>
            {if(!Current()||generation!=_generation)return;_waiting=false;if(!_state.IsPending(_uiGeneration))return;receive(response);},_=>
            {if(!Current()||generation!=_generation)return;_waiting=false;Quarantine();});
        }
        private void Quarantine(){RequiresReconnect=true;++_generation;_waiting=false;_state.SetUnavailable("邮件操作结果尚未确认，请重连后刷新；附件会由服务器继续送达。");}
        private void Disconnected(){if(_disposed)return;++_generation;_waiting=false;_state.Reset();}
        public static MailMessage Map(MailView value)
        {
            var result=new MailMessage{Id=value.MailId.ToString(CultureInfo.InvariantCulture),Title=value.Title,Body=value.Body,Sender=value.Kind==MailKind.Guild?"帮会来信":value.Kind==MailKind.System?"系统来信":"奖励来信",Category=value.Kind==MailKind.Personal?MailCategory.Reward:MailCategory.System,SentAt=(long)Math.Min(value.SentMs/1000,(ulong)long.MaxValue),ExpiresAt=(long)Math.Min(value.ExpireMs/1000,(ulong)long.MaxValue),IsRead=value.IsRead,Claimed=value.ClaimState==MailClaimState.MailClaimClaimed,DeliveryPending=value.ClaimState==MailClaimState.MailClaimPending,RequiresManual=value.ClaimState==MailClaimState.MailClaimManual||(uint)value.ClaimState>(uint)MailClaimState.MailClaimManual};
            if(value.Attachments!=null){foreach(var currency in value.Attachments.Currencies)result.Rewards.Add(new MailReward{Name="货币 "+currency.CurrencyType,IconKey="round_badge_lotus",Count=currency.Amount>long.MaxValue?long.MaxValue:(long)currency.Amount});foreach(var item in value.Attachments.Items)result.Rewards.Add(new MailReward{Name="物品 "+item.ConfigId,IconKey="icon-chest",Count=item.Count});}return result;
        }
        public static string DescribeTip(TipInfoMessage tip)=>tip==null||tip.Id==0?"":tip.Id switch
        {
            (uint)mail_error.KMailNotFound=>"邮件不存在或已删除。",
            (uint)mail_error.KMailExpired=>"邮件已过期。",
            (uint)mail_error.KMailRecipientBoxFull=>"收件箱已满，请先整理邮件。",
            (uint)mail_error.KMailAttachmentInvalid=>"附件暂时无法领取，请联系支持。",
            (uint)mail_error.KMailRequestConflict=>"邮件状态已变化，请刷新后再试。",
            (uint)mail_error.KMailClaimPending=>"附件正在送达，请稍候刷新。",
            (uint)mail_error.KMailClaimManual=>"附件需要协助处理，邮件已保留。",
            (uint)mail_error.KMailAttachmentProtected=>"附件尚未处理完成，邮件已保留。",
            _=>"邮件服务暂时未完成请求，请稍后重试。"
        };
        public void Dispose(){if(_disposed)return;_disposed=true;++_generation;_state.ActionRequested-=Request;_net.Disconnected-=Disconnected;}
    }
}
