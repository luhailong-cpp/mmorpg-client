using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.Social;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Social.SocialUiArt;

namespace MmorpgClient.UI.Ugui.Social
{
    /// <summary>Painted Sprite skin and native TMP text. Sample mutation is gated by State.IsPreview.</summary>
    public sealed class SocialWindow : IDisposable
    {
        public SocialState State { get; }
        public RectTransform Root => _root;
        public bool IsVisible => _root.gameObject.activeSelf;
        public bool HasModal => _modal != null;
        public event Action<SocialChannel> RefreshRequested;
        public event Action<SocialChannel,string> SendRequested;
        public event Action Closed;
        private readonly RectTransform _root,_frame,_body;
        private readonly CanvasGroup _frameControls;
        private readonly TMP_Text _title,_status;
        private readonly Dictionary<SocialPage,Button> _tabs=new();
        private readonly List<KeyValuePair<TMP_Text,SocialRumor>> _countdowns=new();
        private RectTransform _modal;
        private ScrollRect _chatScroll;
        private TMP_InputField _composer;
        private Button _send;
        private TMP_Text _counter,_latestLabel,_channelClock;
        private GameObject _returnFocus,_modalReturnFocus;
        private bool _forceBottom=true,_rendering,_deferred;
        private string _groupSearch="";
        private long _lastSecond=-1;
        public SocialWindow(UnityEngine.Transform parent,SocialState state)
        {
            State=state??throw new ArgumentNullException(nameof(state));
            _root=QdaoUguiFactory.CreateStretch("SocialWindow",parent,Vector4.zero);_root.gameObject.SetActive(false);
            var shade=_root.gameObject.AddComponent<Image>();shade.color=new Color(.025f,.11f,.08f,.69f);shade.raycastTarget=true;_root.gameObject.AddComponent<GameplayInputBlocker>();
            _frame=QdaoUguiFactory.CreateCenteredRect("SocialFrame",_root,2300,930);_frameControls=_frame.gameObject.AddComponent<CanvasGroup>();
            Art(_frame,"window_frame",0,0,2300,930);Art(_frame,"title_plate",804,-63,692,123);Art(_frame,"lantern",49,-35,55,96,true);Art(_frame,"close_tassel",2215,71,34,94,true);
            _title=Text(_frame,"仙友会",874,-39,552,83,54,Cream,heading:true,align:TextAlignmentOptions.Center,name:"SocialTitle");
            Text(_frame,"五 行 奇 谈",130,16,430,48,28,Muted,heading:true);Text(_frame,"山海相逢 · 同道相知",1660,18,478,43,26,Muted,heading:true,align:TextAlignmentOptions.MidlineRight);
            Button(_frame,"SocialClose","",2193,5,76,76,Hide,key:"close_button");
            int index=0;foreach(SocialPage page in Enum.GetValues(typeof(SocialPage))){var captured=page;_tabs[page]=Button(_frame,"SocialTab_"+page,PageName(page),84+index*181,91,169,65,()=>Show(captured),size:31);index++;}
            Text(_frame,State.IsPreview?"离线界面预览":"仙友传音",1640,102,494,44,25,Muted,align:TextAlignmentOptions.MidlineRight);
            Line(_frame,84,164,2132);_body=QdaoUguiFactory.CreateRect("SocialBody",_frame,84,185,2132,653);Line(_frame,84,855,2132);
            Art(_frame,"notice_icon",90,871,27,27,true);_status=Text(_frame,"",128,864,2010,48,23,Muted,name:"SocialStatus");
            State.Changed+=Changed;
        }
        private static string PageName(SocialPage p)=>p==SocialPage.Groups?"群组":p==SocialPage.World?"世界":"谣言";
        public void Show(SocialPage page=SocialPage.Groups)
        {
            bool opening=!IsVisible;if(opening)_returnFocus=EventSystem.current?.currentSelectedGameObject;
            CloseModal(false);_forceBottom=true;_root.gameObject.SetActive(true);State.SelectPage(page);
            if(opening)EventSystem.current?.SetSelectedGameObject(_tabs[page].gameObject);
            if(!State.IsPreview&&page==SocialPage.World&&State.Supports(State.Channel))RefreshRequested?.Invoke(State.Channel);
        }
        public void Hide()
        {
            bool wasVisible=IsVisible;CloseModal(false);_root.gameObject.SetActive(false);
            if(wasVisible){EventSystem.current?.SetSelectedGameObject(_returnFocus!=null&&_returnFocus.activeInHierarchy?_returnFocus:null);Closed?.Invoke();}_returnFocus=null;
        }
        public void Back(){if(HasModal)CloseModal();else Hide();}
        public void ResetSession(){Hide();Clear(_body);_composer=null;_chatScroll=null;_countdowns.Clear();_groupSearch="";}
        public void Tick(float now=0)
        {
            if(!IsVisible)return;long second=State.NowMs/1000;if(second==_lastSecond)return;_lastSecond=second;
            foreach(var pair in _countdowns)if(pair.Key!=null)pair.Key.text=pair.Value.StateLabel(State.NowMs);
            if(_channelClock!=null)_channelClock.text="频道时刻  "+TimeLabel(State.NowMs);
        }
        private void Changed(){if(!IsVisible)return;if(_modal!=null){_deferred=true;_status.text=State.Status;return;}Render();}
        private void Render()
        {
            if(_rendering)return;_rendering=true;
            var oldSelected=EventSystem.current?.currentSelectedGameObject;string focusName=oldSelected?.name;var oldInput=oldSelected?.GetComponent<TMP_InputField>();int caret=oldInput!=null?oldInput.stringPosition:0;
            bool reading=_chatScroll!=null&&_chatScroll.verticalNormalizedPosition>.025f;float scrollValue=reading?_chatScroll.content.anchoredPosition.y:0;
            Clear(_body);_composer=null;_chatScroll=null;_countdowns.Clear();_latestLabel=null;_channelClock=null;
            _title.text=State.Page==SocialPage.Groups?"仙 友 会":State.Page==SocialPage.World?"四 海 传 音":"仙 闻 谣 言";
            foreach(var pair in _tabs){var img=pair.Value.GetComponent<Image>();img.sprite=Load(State.Page==pair.Key?"button_primary":"button_secondary");var label=pair.Value.GetComponentInChildren<TMP_Text>();if(label!=null)label.color=State.Page==pair.Key?Cream:Ink;}
            if(State.Page==SocialPage.Groups)RenderGroups();else if(State.Page==SocialPage.World)RenderWorld();else RenderRumors();
            _status.text=State.Status;Canvas.ForceUpdateCanvases();
            if(_chatScroll!=null){if(_forceBottom||!reading)_chatScroll.verticalNormalizedPosition=0;else{float max=Mathf.Max(0,_chatScroll.content.rect.height-_chatScroll.viewport.rect.height);var point=_chatScroll.content.anchoredPosition;point.y=Mathf.Clamp(scrollValue,0,max);_chatScroll.content.anchoredPosition=point;}UpdateLatest();}
            if(!string.IsNullOrEmpty(focusName)){var selected=_body.GetComponentsInChildren<Selectable>().FirstOrDefault(s=>s.name==focusName&&s.IsInteractable());if(selected!=null){EventSystem.current?.SetSelectedGameObject(selected.gameObject);var nextInput=selected.GetComponent<TMP_InputField>();if(nextInput!=null&&oldInput!=null){nextInput.ActivateInputField();nextInput.stringPosition=Mathf.Min(caret,nextInput.text.Length);}}}
            _forceBottom=false;_rendering=false;Tick();
        }
        private void Empty(UnityEngine.Transform parent,string title,string message,float w,float h,string icon="round_badge_taiji")
        {
            Art(parent,icon,w*.5f-45,h*.35f-70,90,90,true);Text(parent,title,20,h*.35f+30,w-40,70,36,Jade,heading:true,align:TextAlignmentOptions.Center);
            Text(parent,message,30,h*.35f+111,w-60,135,26,Muted,wrap:true,align:TextAlignmentOptions.Center);
        }
        private void RenderWorld()
        {
            Text(_body,"传音频道",5,1,300,62,36,Jade,heading:true);Line(_body,0,64,298);
            int index=0;foreach(var channel in SocialState.ChatChannels){var c=channel;string label=(State.Channel==c?"◆ ":"")+SocialState.ChannelName(c);if(State.Unread(c)>0)label+=" · "+State.Unread(c);Button(_body,"SocialChannel_"+c,label,4,86+index*96,283,69,()=>{_forceBottom=true;State.SelectChannel(c);if(!State.IsPreview&&State.Supports(c))RefreshRequested?.Invoke(c);},State.Channel==c,size:29);index++;}
            Text(_body,State.IsPreview?"纸鹤传情\n四海同游":"此处可用世界传音\n其余所列频道待开放",7,505,286,120,25,Muted,wrap:true);
            Line(_body,322,0,1);var chat=QdaoUguiFactory.CreateRect("WorldChat",_body,346,0,1786,653);
            Text(chat,SocialState.ChannelName(State.Channel)+"传音",0,0,660,58,36,Jade,heading:true);
            Button(chat,"SocialRefresh","刷新",1613,0,167,57,()=>RefreshRequested?.Invoke(State.Channel),enabled:!State.IsPreview&&State.Supports(State.Channel)&&State.IsReady&&!State.Busy&&!State.RequiresReconnect,size:26);
            if(!State.Supports(State.Channel))
            {Empty(chat,"此频道尚未开放","当前所选频道尚未开放，道友可前往世界频道传音。",1780,574);return;}
            _chatScroll=Scroll(chat,"SocialChatScroll",0,67,1784,397,out var content);RenderMessages(content,State.Messages(State.Channel),1768);
            _chatScroll.onValueChanged.AddListener(_=>UpdateLatest());BuildComposer(chat,1782,477,false);
            var latest=Button(chat,"SocialLatest","回到最新",1485,405,278,54,()=>{_chatScroll.verticalNormalizedPosition=0;State.MarkRead(State.Channel);UpdateLatest();},size:24);
            _latestLabel=latest.GetComponentInChildren<TMP_Text>();latest.gameObject.SetActive(State.Unread(State.Channel)>0);
        }
        private void UpdateLatest()
        {
            if(_latestLabel==null||_chatScroll==null)return;
            if(_chatScroll.verticalNormalizedPosition<=.025f)State.MarkRead(State.Channel);
            bool visible=_chatScroll.verticalNormalizedPosition>.025f||State.Unread(State.Channel)>0;
            _latestLabel.transform.parent.gameObject.SetActive(visible);_latestLabel.text=State.Unread(State.Channel)>0?State.Unread(State.Channel)+"条新消息 ↓":"回到最新 ↓";
        }
        private void RenderMessages(RectTransform content,IReadOnlyList<SocialMessage> messages,float width)
        {
            if(messages.Count==0){Empty(content,"静候道友，结伴同行",State.IsPreview?"此频道暂无消息，写下第一句问候吧。":"暂无已同步消息，点击刷新查看最新传音。",width,385);content.sizeDelta=new Vector2(width,400);return;}
            float y=8;
            foreach(var m in messages)
            {
                bool self=m.Sender==State.PlayerId;var person=State.Person(m.Sender);
                float maxBubble=width-116,bubble=Mathf.Min(maxBubble,Mathf.Max(360,SocialState.TextLength(m.Text)*28*.60f+58));
                if(!string.IsNullOrEmpty(m.Card))bubble=Mathf.Max(Mathf.Min(650,maxBubble),bubble);
                var probe=Text(content,m.Text,0,0,bubble-40,100,28,Ink,wrap:true);float textH=Mathf.Max(45,probe.GetPreferredValues(m.Text,bubble-40,float.PositiveInfinity).y);probe.gameObject.SetActive(false);if(Application.isPlaying)UnityEngine.Object.Destroy(probe.gameObject);else UnityEngine.Object.DestroyImmediate(probe.gameObject);
                float cardH=string.IsNullOrEmpty(m.Card)?0:90;float height=44+textH+24+cardH;
                var row=QdaoUguiFactory.CreateRect("SocialMessage_"+m.Id,content,0,y,width,height);
                float avatarX=self?width-83:0,bubbleX=self?width-105-bubble:104;
                Portrait(row,"SocialPortrait_"+m.Sender,person.Portrait,avatarX,0,77,()=>ShowProfile(m.Sender));
                Text(row,person.Name+(person.Level>0?"  "+person.Level+"级":"")+(self?"  我":""),bubbleX,0,bubble-126,43,25,Jade,name:"Sender");Text(row,m.TimeLabel,bubbleX+bubble-116,0,116,43,20,Muted,align:TextAlignmentOptions.MidlineRight);
                var paper=Art(row,"stat_field",bubbleX,46,bubble,textH+26+cardH);paper.color=self?QdaoUguiTheme.Html("#E7EED6"):Color.white;
                Text(row,m.Text,bubbleX+20,55,bubble-40,textH+8,28,Ink,wrap:true,name:"MessageText");
                if(cardH>0){string label=m.Card=="talisman"?"太清护身符 · 查看物品":m.Card=="bamboo"?"青岚竹海（126，84）":"青云试炼 · 同游邀约";Button(row,"SocialCard_"+m.Id,label,bubbleX+18,59+textH,bubble-36,70,()=>ShowCard(m.Card),size:25);}
                y+=height+23;
            }
            content.sizeDelta=new Vector2(width,Mathf.Max(430,y));
        }
        private void BuildComposer(UnityEngine.Transform parent,float width,float y,bool group)
        {
            Line(parent,0,y-9,width);Text(parent,group?"群内传音":"传音至"+SocialState.ChannelName(State.Channel),0,y,width-470,45,26,Jade,heading:true);
            Button(parent,"SocialEmoji","表情",width-429,y,130,44,ShowEmoji,size:23,enabled:State.IsPreview||State.CanSend(State.Channel));
            Button(parent,"SocialShare","分享",width-282,y,130,44,ShowShare,size:23,enabled:State.IsPreview&&!group);
            _counter=Text(parent,"0/120",width-135,y,135,42,22,Muted,align:TextAlignmentOptions.MidlineRight);
            string draft=group?State.SelectedGroup?.Draft:State.Draft(State.Channel);
            _composer=Input(parent,"SocialComposer","与道友说句话吧……",draft,0,y+49,width-187,85,true,0);
            _composer.interactable=group?State.IsPreview:State.CanSend(State.Channel);
            _send=Button(parent,"SocialSend","发送",width-171,y+62,166,69,()=>SendComposer(group),true,size:29);
            _composer.onValueChanged.AddListener(value=>{if(group){if(State.SelectedGroup!=null)State.SelectedGroup.Draft=value;}else State.SetDraft(State.Channel,value);UpdateComposer(group);});
            var keys=_composer.gameObject.AddComponent<SocialComposerKeyboard>();keys.Field=_composer;keys.Send=()=>SendComposer(group);
            Text(parent,"Enter发送 · Shift+Enter换行",0,y+137,width-200,33,19,Muted);UpdateComposer(group);
        }
        private void UpdateComposer(bool group)
        {if(_composer==null)return;_counter.text=SocialState.TextLength(_composer.text)+"/120";_counter.color=SocialState.TextLength(_composer.text)>120?QdaoUguiTheme.Html("#A64E36"):Muted;_send.interactable=SocialState.ValidMessage(_composer.text)&&(group?State.IsPreview:State.CanSend(State.Channel));}
        private void SendComposer(bool group)
        {
            if(_composer==null||!SocialState.ValidMessage(_composer.text))return;string value=_composer.text;_forceBottom=true;
            if(group)State.SendGroup(value);else if(State.IsPreview)State.PreviewSend(State.Channel,value);else SendRequested?.Invoke(State.Channel,value);
        }
        private void RenderGroups()
        {
            if(!State.IsPreview)
            {Empty(_body,"群组服务尚未开放","仙友会界面已准备好。群组创建、邀请与管理开放后，将显示你的真实群组。",2132,650);Button(_body,"SocialGroupCreate","创建群组",886,516,360,70,null,enabled:false,size:30);return;}
            Text(_body,"我的群组",0,0,302,60,34,Jade,heading:true);Button(_body,"SocialGroupCreate","＋ 创建",310,3,150,55,ShowCreate,size:24);
            var search=Input(_body,"SocialGroupSearch","搜索群组",_groupSearch,0,72,460,57,false,24);
            search.onValueChanged.AddListener(value=>{_groupSearch=value;Render();});
            Scroll(_body,"SocialGroupList",0,151,460,424,out var list);
            float y=0;var groups=State.Groups.Where(g=>string.IsNullOrEmpty(_groupSearch)||g.Name.Contains(_groupSearch)).OrderByDescending(g=>g.Pinned).ThenBy(g=>g.Id).ToArray();
            foreach(var group in groups)
            {
                var item=QdaoUguiFactory.CreateRect("SocialGroupRow_"+group.Id,list,0,y,439,122);bool selected=State.SelectedGroupId==group.Id;
                var paper=Art(item,"stat_field",0,0,439,120);paper.color=selected?QdaoUguiTheme.Html("#E4EAD4"):Color.white;
                Art(item,"round_badge_taiji",16,22,64,64,true);Text(item,group.Name,95,8,324,50,29,selected?Jade:Ink);
                Text(item,group.Messages.LastOrDefault()?.Text??"静候道友，结伴同行",95,60,324,37,22,Muted);
                var hit=QdaoUguiFactory.CreateHitButton("SocialGroupSelect_"+group.Id,item,0,0,439,120);hit.navigation=new Navigation{mode=Navigation.Mode.Automatic};hit.onClick.AddListener(()=>{_forceBottom=true;State.SelectGroup(group.Id);});
                if(group.Pinned)Text(item,"置顶",342,94,82,30,16,Muted,align:TextAlignmentOptions.MidlineRight);y+=139;
            }
            if(groups.Length==0)Text(list,"未找到群组，试试其他名称。",8,55,419,145,26,Muted,wrap:true);
            list.sizeDelta=new Vector2(444,Mathf.Max(424,y));Text(_body,"共 "+State.Groups.Count+" 个群组 · 结交同道",6,591,450,56,24,Muted);
            Solid(_body,"GroupDivider",476,0,1,653,new Color(.65f,.56f,.38f,.42f));Solid(_body,"InfoDivider",1430,0,1,653,new Color(.65f,.56f,.38f,.42f));
            var current=State.SelectedGroup;
            if(current==null){var empty=QdaoUguiFactory.CreateRect("GroupEmpty",_body,499,0,1610,653);Empty(empty,"道友相聚，一言成缘","点击创建群组，与熟悉的道友一起云游。",1610,650);return;}
            var chat=QdaoUguiFactory.CreateRect("SocialGroupChat",_body,500,0,905,653);
            Text(chat,current.Name,0,0,900,60,36,Jade,heading:true);Text(chat,current.Members.Count+" / 50 位道友 · 本地群聊",0,55,900,43,24,Muted);
            _chatScroll=Scroll(chat,"SocialGroupChatScroll",0,106,905,351,out var content);RenderMessages(content,current.Messages,889);BuildComposer(chat,905,477,true);
            var info=QdaoUguiFactory.CreateRect("SocialGroupInfo",_body,1455,0,677,653);Text(info,"群组资料",0,0,430,60,34,Jade,heading:true);Button(info,"SocialGroupEdit","编辑",517,0,157,55,ShowEdit,size:24,enabled:current.Owner==State.PlayerId);
            Text(info,"群组公告",0,69,660,43,28,Jade,heading:true);Art(info,"stat_field",0,119,672,150);Text(info,current.Announcement,18,128,636,125,27,Ink,wrap:true);
            Text(info,"群组成员  "+current.Members.Count+" / 50",0,283,660,49,28,Jade,heading:true);Scroll(info,"SocialGroupMembers",0,341,672,159,out var members);
            int n=0;foreach(ulong id in current.Members){var p=State.Person(id);float mx=(n%2)*330,my=(n/2)*78;Portrait(members,"GroupMember_"+id,p.Portrait,mx,my,61,()=>ShowProfile(id));Text(members,p.Name+(id==current.Owner?" · 群主":""),mx+75,my-1,250,42,23,Jade);Text(members,(p.Online?"● 在线":"○ 离线")+"  "+p.Level+"级",mx+75,my+35,250,35,19,Muted);n++;}members.sizeDelta=new Vector2(656,Mathf.Max(159,((n+1)/2)*78));
            Button(info,"SocialGroupPin",current.Pinned?"✓ 群组置顶":"群组置顶",0,513,318,56,()=>State.SetGroupPinned(!current.Pinned),current.Pinned,size:24);
            Button(info,"SocialGroupAlerts",current.Alerts?"✓ 消息提醒":"消息已静音",346,513,326,56,()=>State.SetGroupAlerts(!current.Alerts),current.Alerts,size:24);
            Button(info,"SocialGroupInvite","邀请道友",0,591,318,59,ShowInvite,true,size:26);
            Button(info,"SocialGroupLeave","退出群组",346,591,326,59,ShowLeave,size:26);
        }        private void RenderRumors()
        {
            Art(_body,"notice_icon",4,8,49,49,true);Text(_body,"仙境风闻",72,0,345,60,36,Jade,heading:true);
            Text(_body,"本频道为系统传闻，无法发言",426,10,1070,53,26,Muted,name:"SocialRumorReadonlyNotice");
            Button(_body,"SocialRumorRefresh","刷新",1960,5,169,56,()=>RefreshRequested?.Invoke(SocialChannel.System),enabled:false,size:25);
            if(!State.IsPreview){Empty(_body,"系统传闻尚未开放","当前服务器未提供系统频道历史。正式界面不会装入示例传闻。",2132,576);return;}
            int index=0;foreach(RumorCategory category in Enum.GetValues(typeof(RumorCategory)))
            {var c=category;string label=c==RumorCategory.All?"全部":c==RumorCategory.Beast?"妖兽现世":c==RumorCategory.Treasure?"珍宝奇遇":"仙友捷报";Button(_body,"SocialRumorFilter_"+c,label,index*204,78,190,55,()=>State.FilterRumors(c,State.RumorSearch),State.RumorFilter==c,size:25);index++;}
            var search=Input(_body,"SocialRumorSearch","搜索传闻",State.RumorSearch,1601,78,527,55,false,40);search.onValueChanged.AddListener(value=>State.FilterRumors(State.RumorFilter,value));
            var scroll=Scroll(_body,"SocialRumorScroll",0,155,2132,440,out var content);float y=0;
            foreach(var rumor in State.FilteredRumors())
            {
                var row=QdaoUguiFactory.CreateRect("SocialRumor_"+rumor.Id,content,0,y,2112,158);Art(row,"stat_field",0,0,2112,155);Art(row,rumor.Icon??"round_badge_taiji",23,37,78,78,true);
                Text(row,"谣言",126,10,90,42,23,QdaoUguiTheme.Html("#7B5B7D"));Text(row,rumor.Title,234,8,1305,46,29,Jade,heading:true);
                Text(row,TimeLabel(rumor.TimeMs),1870,10,218,42,22,Muted,align:TextAlignmentOptions.MidlineRight);
                Text(row,rumor.Text,126,57,1760,54,28,Ink);
                var status=Text(row,rumor.StateLabel(State.NowMs),128,109,1128,43,22,Muted,name:"SocialRumorCountdown_"+rumor.Id);_countdowns.Add(new KeyValuePair<TMP_Text,SocialRumor>(status,rumor));
                if(!string.IsNullOrEmpty(rumor.Place))Button(row,"SocialRumorLocation_"+rumor.Id,rumor.Place+"（"+rumor.Coordinates+"）",1279,105,514,44,()=>ShowRumor(rumor),size:21);
                Button(row,"SocialRumorDetail_"+rumor.Id,"查看详情",1840,102,248,49,()=>ShowRumor(rumor),size:24);y+=175;
            }
            if(y==0){Empty(content,"此间暂无传闻","换个分类或关键词，再听听仙境风声。",2112,377);Button(content,"SocialRumorClear","查看全部传闻",842,302,428,60,()=>State.FilterRumors(RumorCategory.All,""),size:26);}
            content.sizeDelta=new Vector2(2116,Mathf.Max(440,y));
            _channelClock=Text(_body,"频道时刻  "+TimeLabel(State.NowMs),2,611,1060,42,23,Muted,name:"SocialChannelClock");Text(_body,"共 "+State.FilteredRumors().Count()+" 条传闻",1440,611,340,42,23,Muted,align:TextAlignmentOptions.MidlineRight);
            Button(_body,"SocialRumorLatest","回到最新 ↑",1826,604,300,51,()=>scroll.verticalNormalizedPosition=1,size:24);
        }
        private static string TimeLabel(long value){try{return DateTimeOffset.FromUnixTimeMilliseconds(value).ToLocalTime().ToString("HH:mm:ss");}catch{return "时间未知";}}
        private RectTransform Modal(string title,float w=930,float h=610)
        {
            CloseModal(false);_modalReturnFocus=EventSystem.current?.currentSelectedGameObject;
            _frameControls.interactable=false;_frameControls.blocksRaycasts=false;
            _modal=QdaoUguiFactory.CreateStretch("SocialModal",_root,Vector4.zero);var shade=_modal.gameObject.AddComponent<Image>();shade.color=new Color(.02f,.10f,.07f,.73f);shade.raycastTarget=true;
            var panel=QdaoUguiFactory.CreateCenteredRect("SocialModalPanel",_modal,w,h);Art(panel,"window_frame",0,0,w,h);Text(panel,title,76,29,w-200,83,43,Jade,heading:true);
            var close=Button(panel,"SocialModalClose","",w-99,23,65,65,()=>CloseModal(),key:"close_button");EventSystem.current?.SetSelectedGameObject(close.gameObject);
            return panel;
        }
        private void CloseModal(bool restore=true)
        {
            if(_modal!=null){var go=_modal.gameObject;go.SetActive(false);if(Application.isPlaying)UnityEngine.Object.Destroy(go);else UnityEngine.Object.DestroyImmediate(go);_modal=null;}
            _frameControls.interactable=true;_frameControls.blocksRaycasts=true;
            string focusName=_modalReturnFocus!=null?_modalReturnFocus.name:null;
            if(_deferred){_deferred=false;if(IsVisible)Render();}
            if(restore){GameObject focus=_modalReturnFocus!=null&&_modalReturnFocus.activeInHierarchy?_modalReturnFocus:null;
                if(focus==null&&!string.IsNullOrEmpty(focusName))focus=_frame.GetComponentsInChildren<Selectable>().FirstOrDefault(s=>s.name==focusName&&s.IsInteractable())?.gameObject;
                EventSystem.current?.SetSelectedGameObject(focus??_tabs[State.Page].gameObject);}
            _modalReturnFocus=null;
        }
        private void ShowProfile(ulong id)
        {
            var person=State.Person(id);var panel=Modal("仙友小传",850,555);Portrait(panel,"SocialProfilePortrait",person.Portrait,92,151,149);
            Text(panel,person.Name,283,141,473,73,37,Jade);Text(panel,person.Level>0?person.Level+"级 · "+person.Title:"角色资料尚未同步",283,223,473,76,27,Muted,wrap:true);
            Text(panel,State.IsPreview?"结缘青云观，游历山川间。愿与四海仙友，共赴一程好风光。":"当前聊天协议仅提供玩家编号，更多资料开放后显示。",84,338,682,113,27,Muted,wrap:true);
            Button(panel,"SocialModalDone","知道了",301,459,248,61,()=>CloseModal(),true,size:27);
        }
        private void ShowCard(string card)
        {
            string title=card=="talisman"?"太清护身符":card=="bamboo"?"青岚竹海":"青云试炼";
            var panel=Modal(title,890,580);Art(panel,card=="talisman"?"icon-talisman":card=="bamboo"?"round_badge_compass":"round_badge_taiji",365,136,158,158,true);
            string copy=card=="talisman"?"玉墨凝灵，太清护身。随身携带的符箓样例。":card=="bamboo"?"青岚竹海（126，84）\n竹影深处，灵气汇聚。":"青云山 · 山门前\n长风的同游邀约，结伴完成山门试炼。";
            Text(panel,copy,85,316,720,116,29,Ink,wrap:true);
            Text(panel,"本地详情预览，尚未连接游戏寻路或物品服务。",85,437,720,48,22,Muted);
            Button(panel,"SocialModalDone","知道了",321,502,248,56,()=>CloseModal(),true,size:25);
        }
        private void ShowRumor(SocialRumor rumor)
        {
            var panel=Modal(rumor.Title,960,623);Art(panel,rumor.Icon??"round_badge_taiji",405,120,150,150,true);
            Text(panel,rumor.Text,87,285,786,106,29,Ink,wrap:true);
            if(!string.IsNullOrEmpty(rumor.Place))Text(panel,rumor.Place+"（"+rumor.Coordinates+"）",87,399,786,50,27,Jade);
            var timer=Text(panel,rumor.StateLabel(State.NowMs),87,450,786,48,24,Muted,name:"SocialRumorModalCountdown");_countdowns.Add(new KeyValuePair<TMP_Text,SocialRumor>(timer,rumor));
            Text(panel,"本地传闻详情，尚未连接游戏寻路。",87,496,786,42,21,Muted);Button(panel,"SocialModalDone","知道了",349,549,262,55,()=>CloseModal(),true,size:25);
        }
        private void ShowCreate()
        {
            if(!State.IsPreview)return;var panel=Modal("创建群组",940,490);Text(panel,"群组名称",83,124,773,48,28,Jade);
            var name=Input(panel,"SocialCreateName","1–24字，与同道相聚", "",84,185,771,65,false,48);
            var error=Text(panel,"示例群组只保存在当前预览。",84,267,771,80,24,Muted,wrap:true,name:"SocialModalFeedback");
            Button(panel,"SocialCreateConfirm","创建群组",573,390,282,61,()=>{if(State.CreateGroup(name.text)){CloseModal();}else error.text=State.Status;},true,size:27);
            Button(panel,"SocialModalCancel","取消",305,390,231,61,()=>CloseModal(),size:27);EventSystem.current?.SetSelectedGameObject(name.gameObject);name.ActivateInputField();
        }
        private void ShowEdit()
        {
            var g=State.SelectedGroup;if(!State.IsPreview||g==null||g.Owner!=State.PlayerId)return;var panel=Modal("编辑群组资料",970,675);
            Text(panel,"群组名称",84,119,802,45,27,Jade);var name=Input(panel,"SocialEditName","1–24字",g.Name,84,176,802,61,false,48);
            Text(panel,"群组公告",84,253,802,45,27,Jade);var notice=Input(panel,"SocialEditAnnouncement","最多100字",g.Announcement,84,310,802,175,true,200);
            var error=Text(panel,"仅群主可编辑资料 · 本地预览",84,502,802,60,22,Muted,wrap:true,name:"SocialModalFeedback");
            Button(panel,"SocialEditConfirm","保存资料",583,579,301,61,()=>{if(State.SaveGroup(name.text,notice.text))CloseModal();else error.text="群名需1–24字且不重名，公告最多100字。";},true,size:27);
            Button(panel,"SocialModalCancel","取消",310,579,238,61,()=>CloseModal(),size:27);
        }
        private void ShowInvite()
        {
            var g=State.SelectedGroup;if(!State.IsPreview||g==null)return;var panel=Modal("邀请道友",990,668);var selected=new HashSet<ulong>();
            Text(panel,"选择想要邀请的道友",84,122,818,49,27,Muted);Scroll(panel,"SocialInviteList",84,189,818,317,out var list);int row=0;
            foreach(var person in State.People.Where(p=>!g.Members.Contains(p.Id)))
            {
                float y=row*96;Portrait(list,"InvitePortrait_"+person.Id,person.Portrait,5,y+9,70);
                Text(list,person.Name+" · "+person.Level+"级",96,y+14,409,56,28,Jade);
                Button choice=null;choice=Button(list,"SocialInviteSelect_"+person.Id,"选择",564,y+15,223,60,()=>{if(!selected.Add(person.Id))selected.Remove(person.Id);choice.GetComponentInChildren<TMP_Text>().text=selected.Contains(person.Id)?"✓ 已选择":"选择";},size:26);row++;
            }
            if(row==0)Text(list,"所有示例道友都已在群内。",12,73,783,100,29,Muted,wrap:true);list.sizeDelta=new Vector2(802,Mathf.Max(317,row*96));
            var feedback=Text(panel,"邀请将在本地示例中立即显示。",84,521,818,46,22,Muted,name:"SocialModalFeedback");
            Button(panel,"SocialInviteConfirm","邀请道友",581,586,325,60,()=>{if(selected.Count==0){feedback.text="请至少选择一位道友。";return;}if(State.Invite(selected))CloseModal();else feedback.text=State.Status;},true,size:27);
            Button(panel,"SocialModalCancel","取消",311,586,235,60,()=>CloseModal(),size:27);
        }
        private void ShowLeave()
        {
            var group=State.SelectedGroup;if(!State.IsPreview||group==null)return;var panel=Modal("退出群组",900,466);
            Text(panel,"确定退出「"+group.Name+"」吗？",84,147,732,90,32,Ink,wrap:true);
            Text(panel,"退出后，该本地群组将从列表移除。",84,251,732,86,26,Muted,wrap:true);
            Button(panel,"SocialLeaveConfirm","确认退出",531,370,284,60,()=>{State.LeaveGroup();CloseModal();},size:27);
            Button(panel,"SocialModalCancel","留下来",254,370,240,60,()=>CloseModal(),true,size:27);
        }
        private void ShowEmoji()
        {
            if(_composer==null)return;var input=_composer;var panel=Modal("仙友表情",860,399);string[] emotes={"[作揖]","[比心]","[喝茶]","[欢喜]","[抱拳]","[月圆]"};
            for(int i=0;i<emotes.Length;i++){string value=emotes[i];Button(panel,"SocialEmote_"+i,value,83+(i%3)*235,147+(i/3)*99,218,74,()=>{CloseModal();if(input!=null){input.text+=value;input.ActivateInputField();input.MoveTextEnd(false);}},size:29);}
        }
        private void ShowShare()
        {
            if(!State.IsPreview)return;var panel=Modal("分享给道友",900,397);Text(panel,"选择一张本地分享卡片",84,120,732,65,27,Muted);
            Button(panel,"SocialShareTalisman","太清护身符",84,228,348,80,()=>{CloseModal();_forceBottom=true;State.PreviewSend(State.Channel,"", "talisman");},size:28);
            Button(panel,"SocialShareCoordinates","青岚竹海坐标",466,228,348,80,()=>{CloseModal();_forceBottom=true;State.PreviewSend(State.Channel,"", "bamboo");},size:28);
        }
        public void Dispose(){State.Changed-=Changed;Hide();_countdowns.Clear();}
    }
}
