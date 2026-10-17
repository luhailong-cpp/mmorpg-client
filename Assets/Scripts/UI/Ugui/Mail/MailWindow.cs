using System;
using System.Linq;
using MmorpgClient.Game.Mail;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Mail.MailFidelityArt;

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
        public ScrollRect RewardScroll { get; }
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
        private readonly TMP_Text _unreadCount, _inboxCount;

        public MailWindow(UnityEngine.Transform parent,MailUiState state=null)
        {
            State=state??new MailUiState();
            _root=QdaoUguiFactory.CreateStretch("MailWindow",parent,Vector4.zero);
            _root.gameObject.SetActive(false);
            var shade=_root.gameObject.AddComponent<Image>();shade.color=new Color(.03f,.10f,.075f,.65f);shade.raycastTarget=true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            // All measurements below are native skin pixels. One uniform parent scale preserves the approved art.
            Frame=QdaoUguiFactory.CreateCenteredRect("MailFrame",_root,1932,814);
            float scale=Mathf.Min(2560f/1932f,1080f/814f);
            Frame.localScale=new UnityEngine.Vector3(scale,scale,1);
            _frameGroup=Frame.gameObject.AddComponent<CanvasGroup>();
            Art(Frame,"mail_empty_frame",0,0,1932,814);
            Label(Frame,"MailTitle","仙 笺",837,37,250,50,32,Cream,title:true,align:TextAlignmentOptions.Center);
            Label(Frame,"MailSubtitle","邮件",875,84,174,27,19,Cream,title:true,align:TextAlignmentOptions.Center);
            _close=Button(Frame,"CloseMail","",1691,48,78,69,Hide,key:"mail_close");
            string[] names={"全部","系统","活动","奖励"};
            float[] tabX={226,374,499,625};
            float[] tabW={147,124,125,128};
            for(int i=0;i<4;i++)
            {
                var category=(MailCategory)i;
                _tabs[i]=Button(Frame,"MailCategory_"+category,names[i],tabX[i],89,tabW[i],56,
                    ()=>{if(DialogIsOpen)return;_notice="";State.SetCategory(category);},
                    size:24,key:"mail_tab_normal");
                var label=_tabs[i].GetComponentInChildren<TMP_Text>();
                label.rectTransform.anchoredPosition=new Vector2(i==0?53:37,-5);
                label.rectTransform.sizeDelta=new Vector2(tabW[i]-(i==0?59:42),46);
                string[] icons={"mail_category_all","mail_category_system","mail_category_event","mail_category_reward"};
                Art(_tabs[i].transform,icons[i],i==0?17:12,12,30,32,true);
            }
            Label(Frame,"MailInboxGlyph","✉",237,157,32,33,24,Muted);
            _summary=Label(Frame,"MailSummary","未读",276,151,65,40,24,Ink);
            _unreadCount=Label(Frame,"MailUnreadCount","",337,151,52,40,26,QdaoUguiTheme.Html("#B33324"));
            _inboxCount=Label(Frame,"MailInboxCount","",553,153,176,36,23,Ink,align:TextAlignmentOptions.MidlineRight);
            ListScroll=Scroll(Frame,"MailListScroll",211,200,542,486,out _listContent);
            _all=Button(Frame,"MailClaimAll","一键领取",232,696,297,70,
                ()=>{if(DialogIsOpen)return;_notice="";State.ClaimAll();},true,27,key:"mail_button_all");
            SetCaptionArea(_all,110,175);
            _clean=Button(Frame,"MailClearRead","清理已读",542,698,191,66,ConfirmClean,size:24,key:"mail_button_clean");
            SetCaptionArea(_clean,54,130);
            _title=Label(Frame,"MailDetailTitle","",797,115,883,55,33,Ink,title:true);
            _meta=Label(Frame,"MailDetailMeta","",798,169,660,37,20,Ink);
            _status=Label(Frame,"MailDetailStatus","",1467,174,241,31,17,Muted,align:TextAlignmentOptions.MidlineRight);
            BodyScroll=Scroll(Frame,"MailBodyScroll",783,205,938,354,out _bodyContent);
            _attachmentHeading=Label(Frame,"AttachmentHeading","随信附赠",1180,555,112,32,17,Muted,align:TextAlignmentOptions.Center);
            _expiry=Label(Frame,"MailExpiry","",838,729,306,41,22,Ink);
            var rewardScrollRoot=QdaoUguiFactory.CreateRect("MailRewardScroll",Frame,813,585,830,122);
            rewardScrollRoot.gameObject.AddComponent<Image>().color=new Color(1,1,1,.001f);
            RewardScroll=rewardScrollRoot.gameObject.AddComponent<ScrollRect>();
            RewardScroll.horizontal=true;RewardScroll.vertical=false;
            RewardScroll.movementType=ScrollRect.MovementType.Clamped;RewardScroll.scrollSensitivity=42;
            var viewport=QdaoUguiFactory.CreateRect("Viewport",rewardScrollRoot,0,0,830,122);
            viewport.gameObject.AddComponent<RectMask2D>();
            _rewards=QdaoUguiFactory.CreateRect("MailRewards",viewport,0,0,830,122);
            RewardScroll.content=_rewards;RewardScroll.viewport=viewport;
            _event=Button(Frame,"MailOpenActivity","前往活动",1156,716,168,56,OpenActivity,size:23,key:"mail_button_event");
            _claim=Button(Frame,"MailClaimSelected","领取附件",1326,709,234,66,
                ()=>{if(DialogIsOpen)return;_notice="";State.ClaimSelected();},true,27,key:"mail_button_claim");
            _delete=Button(Frame,"MailDelete","删除",1560,716,154,56,ConfirmDelete,size:23,key:"mail_button_delete");
            SetCaptionArea(_delete,50,96);
            _footer=Label(Frame,"MailFooter","",795,773,917,25,14,Muted);
            _refresh=Button(Frame,"MailRefresh","刷新",665,156,66,31,
                ()=>{if(!DialogIsOpen)State.Refresh();},size:17,key:"mail_tab_normal");
            State.Changed+=Changed;
        }
        private static void SetCaptionArea(Button button,float left,float width)
        {
            var text=button.GetComponentInChildren<TMP_Text>();
            text.rectTransform.anchoredPosition=new Vector2(left,-4);
            text.rectTransform.sizeDelta=new Vector2(width,button.GetComponent<RectTransform>().rect.height-8);
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
            float rewardPosition=RewardScroll.horizontalNormalizedPosition;
            var focused=EventSystem.current?.currentSelectedGameObject;
            string restore=focused!=null&&focused.transform.IsChildOf(_listContent)?focused.name:null;
            Clear(_listContent);Clear(_bodyContent);Clear(_rewards);
            foreach(var tab in _tabs)
            {
                bool active=tab.name=="MailCategory_"+State.Category;
                tab.image.sprite=Load(active?"mail_tab_selected":"mail_tab_normal");
                var text=tab.GetComponentInChildren<TMP_Text>();if(text!=null)text.color=active?Cream:Ink;
                var allGlyph=tab.transform.Find("mail_category_all");
                if(allGlyph!=null)allGlyph.gameObject.SetActive(active);
            }
            _summary.text="未读";_unreadCount.text=State.UnreadCount.ToString();
            _inboxCount.text="邮件 "+State.Messages.Count;
            var messages=State.VisibleMessages;
            _listContent.sizeDelta=new Vector2(_listContent.sizeDelta.x,Mathf.Max(486,messages.Count*80));
            for(int i=0;i<messages.Count;i++)RenderRow(messages[i],i);
            if(messages.Count==0)
                Label(_listContent,"MailListEmpty","暂无邮件",30,202,470,55,24,Muted,align:TextAlignmentOptions.Center);
            var mail=State.Selected;
            bool changed=mail?.Id!=_renderedId;_renderedId=mail?.Id;
            _title.text=mail?.Title??"云中无新笺";
            _meta.text=mail==null?"":$"寄件人：{mail.Sender}    {Date(mail.SentAt)}";
            _status.text=mail==null?"":Status(mail);
            _expiry.text=mail==null?"":Expiry(mail);
            _claim.gameObject.SetActive(mail?.Rewards.Count>0);
            _claim.interactable=State.CanClaimSelected;
            SetLabel(_claim,mail==null?"领取附件":State.IsExpired(mail)?"已过期":mail.Claimed?"✓ 已领取":"领取附件");
            _event.gameObject.SetActive(mail!=null&&!string.IsNullOrEmpty(mail.ActivityId));
            _event.interactable=mail!=null&&!State.IsExpired(mail)&&(State.IsDemo||ActivityRequested!=null);
            _delete.gameObject.SetActive(mail!=null);
            _delete.interactable=State.CanAct&&mail!=null;
            _all.interactable=State.CanAct&&State.ClaimableCount>0;
            _clean.interactable=State.CanAct&&State.CleanableCount>0;
            _refresh.gameObject.SetActive(!State.IsDemo);_refresh.interactable=State.CanRefresh;
            // Capacity is not exposed by MailUiState; never invent the original art's example "6/50".
            _inboxCount.rectTransform.anchoredPosition=new Vector2(553,-153);
            if(_refresh.gameObject.activeSelf)_inboxCount.rectTransform.sizeDelta=new Vector2(100,36);
            else _inboxCount.rectTransform.sizeDelta=new Vector2(176,36);
            _attachmentHeading.text=mail?.Rewards.Count>7?"随信附赠 · 左右滑动":mail?.Rewards.Count>0?"随信附赠":"仙笺寄语";
            if(mail!=null){RenderBody(mail);RenderRewards(mail);}
            else
            {
                Label(_bodyContent,"MailUnavailableTitle",State.ServiceAvailable||State.IsDemo?"仙笺已整理妥当":"邮件暂未开放",
                    50,105,800,65,32,Ink,title:true,align:TextAlignmentOptions.Center);
                Label(_bodyContent,"MailUnavailableHint",State.ServiceAvailable||State.IsDemo?"道友，暂时没有需要查收的邮件。":"请稍后再来查收仙笺。",
                    50,184,800,54,22,Muted,align:TextAlignmentOptions.Center);
                _bodyContent.sizeDelta=new Vector2(_bodyContent.sizeDelta.x,354);
            }
            _footer.text=!string.IsNullOrEmpty(_notice)?_notice:!string.IsNullOrEmpty(State.Status)?State.Status:"阅读不会领取附件，记得在有效期内查收。";
            ListScroll.verticalNormalizedPosition=listPosition;
            BodyScroll.verticalNormalizedPosition=changed?1:bodyPosition;
            RewardScroll.horizontalNormalizedPosition=changed?0:rewardPosition;
            if(restore!=null&&!DialogIsOpen&&EventSystem.current!=null)
            {
                var next=_listContent.GetComponentsInChildren<Button>().FirstOrDefault(b=>b.name==restore);
                EventSystem.current.SetSelectedGameObject(next!=null?next.gameObject:_close.gameObject);
            }
            else if(focused!=null&&!DialogIsOpen&&focused.transform.IsChildOf(Frame)&&
                (!focused.activeInHierarchy||focused.GetComponent<Button>()?.interactable==false)&&EventSystem.current!=null)
                EventSystem.current.SetSelectedGameObject(_close.gameObject);
        }
        private void RenderRow(MailMessage mail,int index)
        {
            bool selected=mail.Id==State.SelectedId;
            var row=Button(_listContent,"MailRow_"+mail.Id,"",0,index*80,538,80,
                ()=>{if(DialogIsOpen)return;_notice="";State.Select(mail.Id);},
                key:selected?"mail_row_selected":"mail_row_normal");
            Art(row.transform,mail.IsRead?"mail_envelope_read":"mail_envelope_unread",28,9,88,66,true);
            if(!mail.IsRead)
            {
                var dot=QdaoUguiFactory.CreateImage("UnreadDot",row.transform,100,10,14,14,
                    QdaoUguiTheme.LoadSprite(QdaoUguiTheme.StatusDotSpritePath));
                dot.color=QdaoUguiTheme.Html("#C63229");
            }
            Label(row.transform,"MailRowTitle",mail.Title,128,7,354,39,22,Ink,title:true);
            Label(row.transform,"MailRowMeta",Date(mail.SentAt,true),129,46,113,29,17,Muted);
            Label(row.transform,"MailRowState",Status(mail),250,46,260,29,17,
                State.HasValidAttachments(mail)?QdaoUguiTheme.Html("#A04428"):Muted,
                align:TextAlignmentOptions.MidlineRight);
            if(mail.Rewards.Count>0)Art(row.transform,"mail_gift",488,10,28,30,true);
        }
        private void RenderBody(MailMessage mail)
        {
            float top=0;
            if(!string.IsNullOrEmpty(mail.BannerKey)&&Load(mail.BannerKey)!=null)
            {
                bool approvedOriginal=mail.BannerKey=="event-midautumn";
                var sprite=Load(approvedOriginal?"mail_banner":mail.BannerKey);
                var crop=QdaoUguiFactory.CreateRect("MailEventBanner",_bodyContent,0,0,933,267);
                crop.gameObject.AddComponent<RectMask2D>();
                QdaoUguiFactory.CreateAspectFillImage("ActivityArtwork",crop,sprite,sprite.rect.width/sprite.rect.height);
                Label(crop,"MailEventTitle",mail.Title,32,65,520,132,43,Cream,true,true);
                top=278;
            }
            var body=Label(_bodyContent,"MailBodyText",mail.Body,14,top,887,70,21,Ink,true,align:TextAlignmentOptions.TopLeft);
            body.margin=Vector4.zero;body.ForceMeshUpdate();
            float height=body.GetPreferredValues(mail.Body,887,float.PositiveInfinity).y+22;
            body.rectTransform.sizeDelta=new Vector2(887,Mathf.Max(70,height));
            _bodyContent.sizeDelta=new Vector2(_bodyContent.sizeDelta.x,Mathf.Max(354,top+height+12));
        }
        private void RenderRewards(MailMessage mail)
        {
            const float step=145;
            float required=mail.Rewards.Count*step;
            _rewards.sizeDelta=new Vector2(Mathf.Max(830,required),122);
            if(mail.Rewards.Count==0)
            {
                Label(_rewards,"MailNoAttachments","这是一封通知邮件，没有附件。",20,24,790,54,21,Muted,align:TextAlignmentOptions.Center);
                return;
            }
            float start=required<=830?(830-required)*.5f:0;
            for(int i=0;i<mail.Rewards.Count;i++)
            {
                var r=mail.Rewards[i];float x=start+i*step;
                Art(_rewards,"mail_reward_slot",x+13,0,116,98);
                var icon=Art(_rewards,r.IconKey,x+22,7,98,85,true);
                if(State.IsExpired(mail))icon.color=new Color(.68f,.68f,.65f,.75f);
                Label(_rewards,"RewardName_"+i,r.Name+" ×"+r.Count,x-2,97,147,25,17,Ink,align:TextAlignmentOptions.Center);
                if(mail.Claimed)Label(_rewards,"RewardClaimed_"+i,"✓",x+12,-4,34,34,23,Jade);
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
