using System;
using System.Linq;
using MmorpgClient.Game.Mail;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Mail.MailUiArt;

namespace MmorpgClient.UI.Ugui.Mail
{
    /// <summary>Native mail view. Both lists scroll independently; all reward actions remain outside scroll masks.</summary>
    public sealed class MailWindow : IDisposable
    {
        public MailUiState State { get; }
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;
        public bool DialogIsOpen => _dialog != null;
        public RectTransform Root => _root;
        public RectTransform Frame { get; }
        public ScrollRect ListScroll { get; }
        public ScrollRect BodyScroll { get; }
        public event Action<string> ActivityRequested;
        public event Action Closed;
        private readonly RectTransform _root,_listContent,_bodyContent,_rewards;
        private readonly TMP_Text _summary,_title,_meta,_status,_expiry,_footer,_attachmentHeading;
        private readonly Button _close,_claim,_all,_delete,_clean,_event,_refresh;
        private readonly Button[] _tabs=new Button[4];
        private readonly CanvasGroup _frameGroup;
        private RectTransform _dialog;
        private GameObject _returnFocus,_dialogReturnFocus;
        private string _renderedId,_notice="";
        private bool _disposed;

        public MailWindow(Transform parent,MailUiState state=null)
        {
            State=state??new MailUiState();
            _root=QdaoUguiFactory.CreateStretch("MailWindow",parent,Vector4.zero);_root.gameObject.SetActive(false);
            var shade=_root.gameObject.AddComponent<Image>();shade.color=new Color(.03f,.10f,.075f,.65f);shade.raycastTarget=true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            Frame=QdaoUguiFactory.CreateCenteredRect("MailFrame",_root,2250,930);Frame.anchoredPosition=new Vector2(0,-45);
            _frameGroup=Frame.gameObject.AddComponent<CanvasGroup>();
            Art(Frame,"window_frame",0,0,2250,930);
            Art(Frame,"title_plate",800,-83,650,110);
            Label(Frame,"MailTitle","仙  笺",895,-62,460,81,54,Cream,title:true,align:TextAlignmentOptions.Center);
            Art(Frame,"lantern",56,-46,54,96,true);
            Art(Frame,"close_tassel",2194,51,32,87,true);
            Label(Frame,"GameName","五行奇谈",131,8,350,45,27,Muted,title:true);
            Label(Frame,"MailMotto","尺素传情 · 云中寄书",1650,8,455,45,25,Muted,title:true,align:TextAlignmentOptions.MidlineRight);
            _close=Button(Frame,"CloseMail","",2151,-25,78,78,Hide,key:"close_button");
            string[] names={"全部","系统","活动","奖励"};
            for(int i=0;i<4;i++){var category=(MailCategory)i;_tabs[i]=Button(Frame,"MailCategory_"+category,names[i],77+i*176,55,165,61,
                ()=>{if(DialogIsOpen)return;_notice="";State.SetCategory(category);},size:30);}
            _summary=Label(Frame,"MailSummary","",1435,64,735,47,25,Muted,align:TextAlignmentOptions.MidlineRight);
            Solid(Frame,"ToolbarDivider",77,130,2096,1,Line);
            Label(Frame,"InboxHeading","收件箱",81,151,280,48,30,title:true);
            Solid(Frame,"MailColumnDivider",714,148,1,730,Line);
            ListScroll=Scroll(Frame,"MailListScroll",79,205,606,590,out _listContent);
            _clean=Button(Frame,"MailClearRead","清理已读",77,816,282,61,ConfirmClean,size:27);
            _all=Button(Frame,"MailClaimAll","一键领取",378,816,282,61,()=>{if(DialogIsOpen)return;_notice="";State.ClaimAll();},true,27);
            _title=Label(Frame,"MailDetailTitle","",747,149,1115,54,34,title:true);
            _meta=Label(Frame,"MailDetailMeta","",747,199,1130,36,21,Muted);
            _status=Label(Frame,"MailDetailStatus","",1890,154,280,42,22,Jade,align:TextAlignmentOptions.MidlineRight);
            BodyScroll=Scroll(Frame,"MailBodyScroll",747,239,1424,461,out _bodyContent);
            Solid(Frame,"AttachmentDivider",747,714,1423,1,Line);
            _attachmentHeading=Label(Frame,"AttachmentHeading","随信好礼",747,721,360,40,25,title:true);
            _expiry=Label(Frame,"MailExpiry","",1630,722,540,40,21,Muted,align:TextAlignmentOptions.MidlineRight);
            _rewards=QdaoUguiFactory.CreateRect("MailRewards",Frame,747,768,830,112);
            _delete=Button(Frame,"MailDelete","删除",1627,810,124,64,ConfirmDelete,size:25);
            _event=Button(Frame,"MailOpenActivity","前往活动",1767,810,189,64,OpenActivity,size:27);
            _claim=Button(Frame,"MailClaimSelected","领取附件",1972,810,200,64,()=>{if(DialogIsOpen)return;_notice="";State.ClaimSelected();},true,27);
            Art(Frame,"notice_icon",77,895,24,24,true);
            _footer=Label(Frame,"MailFooter","",111,886,1840,42,21,Muted);
            _refresh=Button(Frame,"MailRefresh","刷新",2012,882,162,45,()=>{if(!DialogIsOpen)State.Refresh();},size:22);
            State.Changed+=Changed;
        }

        public void Show()
        {
            if(_disposed)return;
            bool opening=!IsVisible;if(opening)_returnFocus=EventSystem.current?.currentSelectedGameObject;
            _root.gameObject.SetActive(true);State.MarkSelectedRead();Render();
            if(opening&&EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(_close.gameObject);
        }
        public void Hide()
        {
            if(_root==null)return;bool wasVisible=IsVisible;CloseDialog(false);_root.gameObject.SetActive(false);
            if(wasVisible&&EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(_returnFocus!=null&&_returnFocus.activeInHierarchy?_returnFocus:null);
            _returnFocus=null;if(wasVisible)Closed?.Invoke();
        }
        public void Back(){if(DialogIsOpen)CloseDialog();else Hide();}
        public void ResetSession(){Hide();_notice="";_renderedId=null;State.Reset();}
        public void Tick()=>State.Tick();
        private void Changed(){if(IsVisible)Render();}
        private void Render()
        {
            float listPosition=ListScroll.verticalNormalizedPosition;
            float bodyPosition=BodyScroll.verticalNormalizedPosition;
            var focused=EventSystem.current?.currentSelectedGameObject;
            string restore=focused!=null&&focused.transform.IsChildOf(_listContent)?focused.name:null;
            Clear(_listContent);Clear(_bodyContent);Clear(_rewards);
            foreach(var tab in _tabs){bool active=tab.name=="MailCategory_"+State.Category;tab.image.sprite=Load(active?"button_primary":"button_secondary");
                var text=tab.GetComponentInChildren<TMP_Text>();if(text!=null)text.color=active?Cream:Ink;}
            _summary.text=$"未读  {State.UnreadCount}  封    ·    待领  {State.ClaimableCount}  封";
            var messages=State.VisibleMessages;
            _listContent.sizeDelta=new Vector2(_listContent.sizeDelta.x,Mathf.Max(590,messages.Count*122));
            for(int i=0;i<messages.Count;i++)RenderRow(messages[i],i);
            if(messages.Count==0){Art(_listContent,"round_badge_taiji",228,147,110,110,true);
                Label(_listContent,"MailListEmpty","暂无邮件",45,282,470,60,29,Muted,align:TextAlignmentOptions.Center);}
            var mail=State.Selected;
            bool changed=mail?.Id!=_renderedId;_renderedId=mail?.Id;
            _title.text=mail?.Title??"云中无新笺";_meta.text=mail==null?"":$"寄件人：{mail.Sender}    {Date(mail.SentAt)}";
            _status.text=mail==null?"":Status(mail);
            _expiry.text=mail==null?"":Expiry(mail);
            _claim.gameObject.SetActive(mail?.Rewards.Count>0);
            _claim.interactable=State.CanClaimSelected;
            SetLabel(_claim,mail==null?"领取附件":State.IsExpired(mail)?"已过期":mail.Claimed?"✓ 已领取":"领取附件");
            _event.gameObject.SetActive(mail!=null&&!string.IsNullOrEmpty(mail.ActivityId));
            _event.interactable=mail!=null&&!State.IsExpired(mail)&&(State.IsDemo||ActivityRequested!=null);
            _delete.interactable=State.CanAct&&mail!=null;
            _all.interactable=State.CanAct&&State.ClaimableCount>0;
            _refresh.gameObject.SetActive(!State.IsDemo);_refresh.interactable=State.CanRefresh;
            _clean.interactable=State.CanAct&&State.CleanableCount>0;
            _attachmentHeading.text=mail?.Rewards.Count>0?"随信好礼":"仙笺寄语";
            if(mail!=null){RenderBody(mail);RenderRewards(mail);}
            else {Art(_bodyContent,"round_badge_taiji",589,95,138,138,true);
                Label(_bodyContent,"MailUnavailableTitle",State.ServiceAvailable||State.IsDemo?"仙笺已整理妥当":"邮件暂未开放",80,258,1230,70,39,Jade,title:true,align:TextAlignmentOptions.Center);
                Label(_bodyContent,"MailUnavailableHint",State.ServiceAvailable||State.IsDemo?"道友，暂时没有需要查收的邮件。":"请稍后再来查收仙笺。",80,339,1230,50,27,Muted,align:TextAlignmentOptions.Center);
                _bodyContent.sizeDelta=new Vector2(_bodyContent.sizeDelta.x,461);}
            _footer.text=!string.IsNullOrEmpty(_notice)?_notice:!string.IsNullOrEmpty(State.Status)?State.Status:"阅读不会领取附件，记得在有效期内查收。";
            ListScroll.verticalNormalizedPosition=listPosition;BodyScroll.verticalNormalizedPosition=changed?1:bodyPosition;
            if(restore!=null&&!DialogIsOpen&&EventSystem.current!=null){var next=_listContent.GetComponentsInChildren<Button>().FirstOrDefault(b=>b.name==restore);
                EventSystem.current.SetSelectedGameObject(next!=null?next.gameObject:_close.gameObject);}
            else if(focused!=null&&!DialogIsOpen&&focused.transform.IsChildOf(Frame)&&
                (!focused.activeInHierarchy||focused.GetComponent<Button>()?.interactable==false)&&EventSystem.current!=null)
                EventSystem.current.SetSelectedGameObject(_close.gameObject);
        }
        private void RenderRow(MailMessage mail,int index)
        {
            bool selected=mail.Id==State.SelectedId;
            var row=Button(_listContent,"MailRow_"+mail.Id,"",0,index*122,576,110,()=>{if(DialogIsOpen)return;_notice="";State.Select(mail.Id);},key:"stat_field");
            row.image.color=selected?QdaoUguiTheme.Html("#DDE8D0"):Color.white;
            if(selected)Solid(row.transform,"SelectedEdge",0,13,4,82,Jade);
            string icon=mail.Category==MailCategory.Event?"round_badge_taiji":mail.Category==MailCategory.Reward?"icon-chest":"icon-scroll";
            Art(row.transform,icon,12,22,65,65,true);
            if(!mail.IsRead){var dot=QdaoUguiFactory.CreateImage("UnreadDot",row.transform,64,14,15,15,QdaoUguiTheme.LoadSprite(QdaoUguiTheme.StatusDotSpritePath));dot.color=QdaoUguiTheme.Html("#B75435");}
            var title=Label(row.transform,"MailRowTitle",mail.Title,89,8,467,51,27);title.fontStyle=mail.IsRead?FontStyles.Normal:FontStyles.Bold;
            Label(row.transform,"MailRowMeta",Date(mail.SentAt,true)+" · "+CategoryLabel(mail.Category),90,64,243,34,20,Muted);
            Label(row.transform,"MailRowState",Status(mail),334,64,220,34,20,Muted,align:TextAlignmentOptions.MidlineRight);
        }
        private void RenderBody(MailMessage mail)
        {
            float top=0;
            if(!string.IsNullOrEmpty(mail.BannerKey)&&Load(mail.BannerKey)!=null)
            {
                var crop=QdaoUguiFactory.CreateRect("MailEventBanner",_bodyContent,146,0,1116,294);crop.gameObject.AddComponent<RectMask2D>();
                var sprite=Load(mail.BannerKey);QdaoUguiFactory.CreateAspectFillImage("ActivityArtwork",crop,sprite,sprite.rect.width/sprite.rect.height);
                Label(crop,"ActivityKicker","中 秋 雅 集",36,24,650,45,22,Cream,title:true);
                Label(crop,"ActivityBannerTitle","月满仙山\n玉兔送福",35,69,620,145,43,Cream,true,true);
                Label(crop,"ActivityCaption","桂香伴月 · 花灯寄情",36,224,660,48,25,Cream,title:true);
                top=310;
            }
            var body=Label(_bodyContent,"MailBodyText",mail.Body,48,top,1260,100,28,Ink,true,align:TextAlignmentOptions.TopLeft);
            body.margin=Vector4.zero;body.ForceMeshUpdate();float height=body.GetPreferredValues(mail.Body,1260,float.PositiveInfinity).y+35;
            body.rectTransform.sizeDelta=new Vector2(1260,Mathf.Max(100,height));
            _bodyContent.sizeDelta=new Vector2(_bodyContent.sizeDelta.x,Mathf.Max(461,top+height+18));
        }
        private void RenderRewards(MailMessage mail)
        {
            if(mail.Rewards.Count==0){Art(_rewards,"notice_icon",0,12,43,43,true);Label(_rewards,"MailNoAttachments","这是一封通知邮件，没有附件。",60,6,735,63,25,Muted);return;}
            for(int i=0;i<mail.Rewards.Count;i++){var r=mail.Rewards[i];float x=i*111;
                Art(_rewards,"stat_field",x,0,96,90);
                var icon=Art(_rewards,r.IconKey,x+10,5,76,76,true);if(State.IsExpired(mail))icon.color=new Color(.68f,.68f,.65f,.75f);
                Solid(_rewards,"RewardCountPlate_"+i,x+38,63,54,25,Jade);
                Label(_rewards,"RewardCount_"+i,r.Count.ToString(),x+38,59,54,32,19,Cream,align:TextAlignmentOptions.Center);
                Label(_rewards,"RewardName_"+i,r.Name,x-2,91,103,32,20,Muted,align:TextAlignmentOptions.Center);
                if(mail.Claimed)Label(_rewards,"RewardClaimed_"+i,"✓",x+2,-3,37,38,26,Jade);
            }
        }
        private string Status(MailMessage mail)=>State.IsExpired(mail)?"已过期":mail.Claimed?"✓ 已领取":mail.Rewards.Count>0?"待领取":"无附件";
        private static string CategoryLabel(MailCategory category)=>category switch{MailCategory.Event=>"活动",MailCategory.Reward=>"奖励",_=>"系统"};
        private static string Date(long unix,bool dayOnly=false)
        {try{return DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString(dayOnly?"MM-dd":"MM-dd HH:mm");}catch{return "日期待同步";}}
        private string Expiry(MailMessage mail)=>mail.ExpiresAt<=0?"长期保留":State.IsExpired(mail)?"已过期 · 附件失效":
            Math.Ceiling((mail.ExpiresAt-State.Now)/86400d)+" 天后到期";
        private void ConfirmDelete()
        {
            if(DialogIsOpen)return;var mail=State.Selected;if(mail==null)return;
            if(State.HasValidAttachments(mail)){_notice="这封邮件还有可领取的附件，请先领取再删除。";Render();return;}
            string id=mail.Id;ShowDialog("删除邮件","确定删除「"+mail.Title+"」吗？\n\n删除后，这封邮件将不再显示。",()=>{
                if(State.SelectedId!=id||!State.CanDeleteSelected){_notice="邮件状态已变化，请重新确认。";Render();return;}
                _notice="";State.DeleteSelected();
            });
        }
        private void ConfirmClean()
        {
            if(DialogIsOpen||!State.CanAct)return;
            var ids=State.Messages.Where(m=>m.IsRead&&!State.HasValidAttachments(m)).Select(m=>m.Id).OrderBy(x=>x).ToArray();
            if(ids.Length==0)return;ShowDialog("清理已读",$"将清理 {ids.Length} 封已读邮件。\n\n含有效待领附件的邮件会保留。",()=>{
                var latest=State.Messages.Where(m=>m.IsRead&&!State.HasValidAttachments(m)).Select(m=>m.Id).OrderBy(x=>x);
                if(!ids.SequenceEqual(latest)){_notice="邮件状态已变化，请重新确认。";Render();return;}
                _notice="";State.ClearRead();
            });
        }
        public void OpenActivity()
        {
            var mail=State.Selected;if(DialogIsOpen||mail==null||State.IsExpired(mail))return;
            if(!State.IsDemo){ActivityRequested?.Invoke(mail.ActivityId);return;}
            ShowDialog("月满仙山 · 玉兔送福","与道友共赏一轮明月，提一盏花灯，在桂香与云海间赴团圆之约。\n\n月下祈福 · 花灯游园 · 玉兔寻宝",null,mail.BannerKey);
        }
        private void ShowDialog(string title,string message,Action confirm,string banner=null)
        {
            _dialogReturnFocus=EventSystem.current?.currentSelectedGameObject;
            _frameGroup.interactable=false;_frameGroup.blocksRaycasts=false;
            _dialog=QdaoUguiFactory.CreateStretch("MailDialog",_root,Vector4.zero);var dim=_dialog.gameObject.AddComponent<Image>();dim.color=new Color(.03f,.1f,.07f,.66f);
            bool activity=!string.IsNullOrEmpty(banner);float height=activity?745:460;
            var panel=QdaoUguiFactory.CreateCenteredRect("MailDialogPanel",_dialog,1100,height);Art(panel,"window_frame",0,0,1100,height);
            Label(panel,"MailDialogTitle",title,78,44,915,69,40,Jade,title:true);
            Button(panel,"MailDialogClose","",998,21,66,66,()=>CloseDialog(),key:"close_button");
            float top=128;
            if(activity){var crop=QdaoUguiFactory.CreateRect("MailDialogActivityArt",panel,78,126,944,295);crop.gameObject.AddComponent<RectMask2D>();
                var sprite=Load(banner);if(sprite!=null)QdaoUguiFactory.CreateAspectFillImage("ActivityArtwork",crop,sprite,sprite.rect.width/sprite.rect.height);top=443;}
            Label(panel,"MailDialogMessage",message,84,top,932,activity?137:182,28,Ink,true,align:TextAlignmentOptions.TopLeft);
            if(activity)Label(panel,"MailActivityPreviewNotice","离线设计样例 · 非真实开放活动",84,620,925,43,22,Muted);
            var cancel=Button(panel,"MailDialogCancel",confirm==null?"返回仙笺":"取消",confirm==null?423:586,height-91,236,61,()=>CloseDialog(),confirm==null,28);
            if(confirm!=null)Button(panel,"MailDialogConfirm","确认",840,height-91,181,61,()=>{CloseDialog();confirm();},true,28);
            if(EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(cancel.gameObject);
        }
        private void CloseDialog(bool restore=true)
        {
            if(_dialog==null)return;var old=_dialog.gameObject;_dialog=null;old.SetActive(false);
            if(Application.isPlaying)UnityEngine.Object.Destroy(old);else UnityEngine.Object.DestroyImmediate(old);
            _frameGroup.interactable=true;_frameGroup.blocksRaycasts=true;
            if(restore&&EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(_dialogReturnFocus!=null&&_dialogReturnFocus.activeInHierarchy?_dialogReturnFocus:_close.gameObject);
            _dialogReturnFocus=null;
        }
        public void Dispose(){if(_disposed)return;_disposed=true;State.Changed-=Changed;Hide();
            if(_root!=null){if(Application.isPlaying)UnityEngine.Object.Destroy(_root.gameObject);else UnityEngine.Object.DestroyImmediate(_root.gameObject);}}
    }
}
