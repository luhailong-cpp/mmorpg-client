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
        private TMP_Text _counter,_latestLabel;
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
        }
        private void Changed(){if(!IsVisible)return;if(_modal!=null){_deferred=true;_status.text=State.Status;return;}Render();}
        private void Render()
        {
            if(_rendering)return;_rendering=true;
            string focusName=EventSystem.current?.currentSelectedGameObject?.name;
            bool reading=_chatScroll!=null&&_chatScroll.verticalNormalizedPosition>.025f;float scrollValue=reading?_chatScroll.verticalNormalizedPosition:0;
            Clear(_body);_composer=null;_chatScroll=null;_countdowns.Clear();_latestLabel=null;
            _title.text=State.Page==SocialPage.Groups?"仙 友 会":State.Page==SocialPage.World?"四 海 传 音":"仙 闻 谣 言";
            foreach(var pair in _tabs){var img=pair.Value.GetComponent<Image>();img.sprite=Load(State.Page==pair.Key?"button_primary":"button_secondary");var label=pair.Value.GetComponentInChildren<TMP_Text>();if(label!=null)label.color=State.Page==pair.Key?Cream:Ink;}
            if(State.Page==SocialPage.Groups)RenderGroups();else if(State.Page==SocialPage.World)RenderWorld();else RenderRumors();
            _status.text=State.Status;Canvas.ForceUpdateCanvases();
            if(_chatScroll!=null){_chatScroll.verticalNormalizedPosition=_forceBottom||!reading?0:scrollValue;UpdateLatest();}
            if(!string.IsNullOrEmpty(focusName)){var selected=_body.GetComponentsInChildren<Selectable>().FirstOrDefault(s=>s.name==focusName&&s.IsInteractable());if(selected!=null)EventSystem.current?.SetSelectedGameObject(selected.gameObject);}
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
            Text(_body,State.IsPreview?"纸鹤传情\n四海同游":"仅世界频道已开放\n其他频道暂未开放",7,505,286,120,25,Muted,wrap:true);
            Line(_body,322,0,1);var chat=QdaoUguiFactory.CreateRect("WorldChat",_body,346,0,1786,653);
            Text(chat,SocialState.ChannelName(State.Channel)+"传音",0,0,660,58,36,Jade,heading:true);
            Button(chat,"SocialRefresh","刷新",1613,0,167,57,()=>RefreshRequested?.Invoke(State.Channel),enabled:!State.IsPreview&&State.Supports(State.Channel)&&State.IsReady&&!State.Busy&&!State.RequiresReconnect,size:26);
            if(!State.Supports(State.Channel))
            {Empty(chat,"此频道尚未开放","当前服务只开放世界文本传音，其他频道开放后可在此使用。",1780,574);return;}
            _chatScroll=Scroll(chat,"SocialChatScroll",0,67,1784,430,out var content);RenderMessages(content,State.Messages(State.Channel),1768);
            _chatScroll.onValueChanged.AddListener(_=>UpdateLatest());BuildComposer(chat,1782,510,false);
            var latest=Button(chat,"SocialLatest","回到最新",1485,438,278,54,()=>{_chatScroll.verticalNormalizedPosition=0;State.MarkRead(State.Channel);UpdateLatest();},size:24);
            _latestLabel=latest.GetComponentInChildren<TMP_Text>();latest.gameObject.SetActive(State.Unread(State.Channel)>0);
        }
        private void UpdateLatest()
        {
            if(_latestLabel==null||_chatScroll==null)return;
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
                var probe=Text(content,m.Text,0,0,bubble-40,100,28,Ink,wrap:true);float textH=Mathf.Max(45,probe.GetPreferredValues(m.Text,bubble-40,0).y);probe.gameObject.SetActive(false);if(Application.isPlaying)UnityEngine.Object.Destroy(probe.gameObject);else UnityEngine.Object.DestroyImmediate(probe.gameObject);
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
            _composer=Input(parent,"SocialComposer","与道友说句话吧……",draft,0,y+49,width-187,85,true,240);
            _composer.interactable=group?State.IsPreview:State.CanSend(State.Channel);
            _send=Button(parent,"SocialSend","发送",width-171,y+62,166,69,()=>SendComposer(group),true,size:29);
            _composer.onValueChanged.AddListener(value=>{if(group){if(State.SelectedGroup!=null)State.SelectedGroup.Draft=value;}else State.SetDraft(State.Channel,value);UpdateComposer(group);});
            var keys=_composer.gameObject.AddComponent<SocialComposerKeyboard>();keys.Field=_composer;keys.Send=()=>SendComposer(group);
            Text(parent,"Enter发送 · Shift+Enter换行",0,y+137,width-200,33,19,Muted);UpdateComposer(group);
        }
        private void UpdateComposer(bool group)
        {if(_composer==null)return;_counter.text=SocialState.TextLength(_composer.text)+"/120";_send.interactable=SocialState.ValidMessage(_composer.text)&&(group?State.IsPreview:State.CanSend(State.Channel));}
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
            _chatScroll=Scroll(chat,"SocialGroupChatScroll",0,106,905,384,out var content);RenderMessages(content,current.Messages,889);BuildComposer(chat,905,510,true);
            var info=QdaoUguiFactory.CreateRect("SocialGroupInfo",_body,1455,0,677,653);Text(info,"群组资料",0,0,430,60,34,Jade,heading:true);Button(info,"SocialGroupEdit","编辑",517,0,157,55,ShowEdit,size:24,enabled:current.Owner==State.PlayerId);
            Text(info,"群组公告",0,69,660,43,28,Jade,heading:true);Art(info,"stat_field",0,119,672,150);Text(info,current.Announcement,18,128,636,125,27,Ink,wrap:true);
            Text(info,"群组成员  "+current.Members.Count+" / 50",0,283,660,49,28,Jade,heading:true);Scroll(info,"SocialGroupMembers",0,341,672,159,out var members);
            int n=0;foreach(ulong id in current.Members){var p=State.Person(id);float mx=(n%2)*330,my=(n/2)*78;Portrait(members,"GroupMember_"+id,p.Portrait,mx,my,61,()=>ShowProfile(id));Text(members,p.Name+(id==current.Owner?" · 群主":""),mx+75,my-1,250,42,23,Jade);Text(members,(p.Online?"● 在线":"○ 离线")+"  "+p.Level+"级",mx+75,my+35,250,35,19,Muted);n++;}members.sizeDelta=new Vector2(656,Mathf.Max(159,((n+1)/2)*78));
            Button(info,"SocialGroupPin",current.Pinned?"✓ 群组置顶":"群组置顶",0,513,318,56,()=>State.SetGroupPinned(!current.Pinned),current.Pinned,size:24);
            Button(info,"SocialGroupAlerts",current.Alerts?"✓ 消息提醒":"消息已静音",346,513,326,56,()=>State.SetGroupAlerts(!current.Alerts),current.Alerts,size:24);
            Button(info,"SocialGroupInvite","邀请道友",0,591,318,59,ShowInvite,true,size:26);
            Button(info,"SocialGroupLeave","退出群组",346,591,326,59,ShowLeave,size:26);
        }