using System;
using System.Collections.Generic;
using MmorpgClient.Game.Social;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Social.SocialUiArt;

namespace MmorpgClient.UI.Ugui.Social
{
    public sealed partial class SocialWindow
    {
        private const string FidelityResources = "UI/Ugui/SocialV1/Fidelity20261016/";
        private RectTransform _fidelityRoot, _fidelityContent;
        private CanvasGroup _fidelityControls;
        private Image _fidelitySkin;
        private TMP_Text _fidelityStatus;
        private readonly Dictionary<SocialPage,Button> _fidelityTabs = new();

        private void RenderFidelityPage()
        {
            if (_fidelityRoot == null)
            {
                _fidelityRoot = QdaoUguiFactory.CreateCenteredRect("SocialDesign1931", _root, 1931, 814);
                _fidelityRoot.localScale = UnityEngine.Vector3.one * (2560f / 1931f);
                _fidelityControls = _fidelityRoot.gameObject.AddComponent<CanvasGroup>();
                _fidelitySkin = FImage(_fidelityRoot, "world_skin", 0, 0, 1931, 814);
                _fidelityContent = QdaoUguiFactory.CreateRect("SocialNativeContent", _fidelityRoot, 0, 0, 1931, 814);
            }
            _frame.gameObject.SetActive(false);
            _fidelityRoot.gameObject.SetActive(true);
            Clear(_fidelityContent); _fidelityTabs.Clear();
            _fidelitySkin.sprite = QdaoUguiTheme.RequireSprite(FidelityResources +
                (State.Page == SocialPage.Groups ? "groups_skin" : State.Page == SocialPage.Rumor ? "rumor_skin" : "world_skin"));
            FText(_fidelityContent,"SocialTitle","仙 友",857,24,233,61,43,Cream,align:TextAlignmentOptions.Center);
            float[] tx = { 677, 873, 1074 };
            int n = 0;
            foreach (SocialPage page in Enum.GetValues(typeof(SocialPage)))
            {
                var selected = page;
                _fidelityTabs[page] = FHit(_fidelityContent,"SocialTab_"+page,PageName(page),tx[n++],90,182,47,
                    () => Show(selected),size:27,color:State.Page==page?Cream:Ink);
            }
            FHit(_fidelityContent,"SocialClose","",1753,51,75,73,Hide);
            FHit(_fidelityContent,"SocialFriendRequests","好友申请",1590,94,145,38,
                () => FriendRequestsRequested?.Invoke(),!State.IsPreview,20);
            if(State.Page==SocialPage.World)RenderFidelityWorld();
            else if(State.Page==SocialPage.Groups)RenderFidelityGroups();
            else RenderFidelityRumors();
            // Operational feedback is independent of the art and stays outside
            // message text. Production data never comes from the design bitmap.
            _fidelityStatus=FText(_fidelityContent,"SocialStatus",State.Status,500,780,1245,24,14,Muted);
        }

        private Button ActivePageTab(SocialPage page) => _fidelityTabs.TryGetValue(page,out var tab) ? tab : _tabs[page];
        private Image FImage(UnityEngine.Transform parent,string key,float x,float y,float w,float h)
        {
            var image=QdaoUguiFactory.CreateImage(key,parent,x,y,w,h,QdaoUguiTheme.RequireSprite(FidelityResources+key));
            image.type=image.sprite.border.sqrMagnitude>0?Image.Type.Sliced:Image.Type.Simple;
            return image;
        }
        private TMP_Text FText(UnityEngine.Transform parent,string name,string value,float x,float y,float w,float h,
            float size=22,Color? color=null,bool wrap=false,TextAlignmentOptions align=TextAlignmentOptions.MidlineLeft)
        {
            var text=QdaoUguiFactory.CreateText(name,parent,x,y,w,h,value,size,color??Ink,align);
            text.font=QdaoUguiTheme.ResolveFont();text.richText=false;
            text.textWrappingMode=wrap?TextWrappingModes.Normal:TextWrappingModes.NoWrap;
            text.overflowMode=TextOverflowModes.Ellipsis;
            return text;
        }
        private Button FHit(UnityEngine.Transform parent,string name,string label,float x,float y,float w,float h,
            Action clicked,bool enabled=true,float size=24,Color? color=null)
        {
            var button=QdaoUguiFactory.CreateHitButton(name,parent,x,y,w,h);
            button.interactable=enabled;button.navigation=new Navigation{mode=Navigation.Mode.Automatic};
            if(clicked!=null)button.onClick.AddListener(()=>clicked());
            if(!string.IsNullOrEmpty(label))FText(button.transform,"Label",label,0,0,w,h,size,color,align:TextAlignmentOptions.Center);
            return button;
        }
        private TMP_InputField FInput(UnityEngine.Transform parent,string name,string hint,string value,
            float x,float y,float w,float h,bool multiline=false,int limit=0)
        {
            var input=QdaoUguiFactory.CreateInputField(name,parent,x,y,w,h,hint,limit);
            input.lineType=multiline?TMP_InputField.LineType.MultiLineNewline:TMP_InputField.LineType.SingleLine;
            input.textComponent.font=QdaoUguiTheme.ResolveFont();input.textComponent.fontSize=24;
            input.textComponent.color=Ink;input.textComponent.richText=false;
            input.textComponent.textWrappingMode=multiline?TextWrappingModes.Normal:TextWrappingModes.NoWrap;
            input.textComponent.alignment=multiline?TextAlignmentOptions.TopLeft:TextAlignmentOptions.MidlineLeft;
            if(input.placeholder is TMP_Text hintText){hintText.font=input.textComponent.font;hintText.fontSize=24;hintText.color=Muted;hintText.richText=false;}
            input.SetTextWithoutNotify(value??"");return input;
        }
        private void FPortrait(UnityEngine.Transform parent,ulong id,float x,float y,float size)
        {
            var person=State.Person(id);
            if(State.IsPreview)
                FImage(parent,"portrait_"+Mathf.Clamp((int)id,1,5),x,y,size,size);
            else
            {
                // The chat protocol does not carry a profession or portrait.
                // Do not assign an invented identity to a live player.
                var image=QdaoUguiFactory.CreateImage("UnknownPortrait",parent,x,y,size,size,
                    QdaoUguiTheme.RequireSprite(QdaoUguiTheme.StatusDotSpritePath));
                image.color=QdaoUguiTheme.Html("#ECE7D6");
                FText(parent,"UnknownPortraitLabel","道友",x,y,size,size,size*.24f,Muted,align:TextAlignmentOptions.Center);
            }
            FHit(parent,"SocialPortrait_"+id+"Button","",x,y,size,size,()=>ShowProfile(id));
        }
        private void FMessages(RectTransform content,IReadOnlyList<SocialMessage> messages,float width)
        {
            float y=4;
            foreach(var message in messages)
            {
                bool self=message.Sender==State.PlayerId;
                var person=State.Person(message.Sender);
                float bubble=Mathf.Min(width-126,Mathf.Max(220,SocialState.TextLength(message.Text)*23f+36));
                if(!string.IsNullOrEmpty(message.Card))bubble=Mathf.Max(bubble,350);
                var probe=FText(content,"Measure",message.Text,0,0,bubble-32,100,24,wrap:true);
                float textHeight=Mathf.Max(32,probe.GetPreferredValues(message.Text,bubble-32,float.PositiveInfinity).y);
                probe.gameObject.SetActive(false);
                if(Application.isPlaying)UnityEngine.Object.Destroy(probe.gameObject);else UnityEngine.Object.DestroyImmediate(probe.gameObject);
                float cardHeight=string.IsNullOrEmpty(message.Card)?0:61;
                float rowHeight=Mathf.Max(98,39+textHeight+cardHeight+18);
                var row=QdaoUguiFactory.CreateRect("SocialMessage_"+message.Id,content,0,y,width,rowHeight);
                float avatarX=self?width-100:0, bubbleX=self?width-116-bubble:117;
                FPortrait(row,message.Sender,avatarX,0,98);
                FText(row,"Sender",person.Name,bubbleX,0,Mathf.Max(140,bubble-110),32,23,Jade);
                if(person.Level>0)FText(row,"Level",person.Level+"级 · "+(person.Title??""),bubbleX+115,0,170,32,18,Muted);
                FText(row,"Time",message.TimeLabel,bubbleX+bubble-75,0,75,32,17,Muted,align:TextAlignmentOptions.MidlineRight);
                var paper=FImage(row,"message_bubble",bubbleX,34,bubble,textHeight+14+cardHeight);
                if(self){paper.color=QdaoUguiTheme.Html("#E0EFD5");paper.rectTransform.localScale=new UnityEngine.Vector3(-1,1,1);paper.rectTransform.anchoredPosition+=new Vector2(bubble,0);}
                FText(row,"MessageText",message.Text,bubbleX+18,39,bubble-36,textHeight+4,24,Ink,true,TextAlignmentOptions.TopLeft);
                if(cardHeight>0)
                {
                    var card=message.Card;
                    string label=card=="bamboo"?"青岚竹海（126，84）":card=="talisman"?"太清护身符 · 点击查看":"青云试炼 · 同游邀约";
                    FImage(row,"button_normal",bubbleX+16,43+textHeight,bubble-32,52);
                    FHit(row,"SocialCard_"+message.Id,label,bubbleX+16,43+textHeight,bubble-32,52,()=>ShowCard(card),size:21);
                }
                y+=rowHeight+10;
            }
            if(messages.Count==0)
                FText(content,"SocialEmptyMessages","静候道友，结伴同行",40,160,width-80,60,28,Muted,align:TextAlignmentOptions.Center);
            content.sizeDelta=new Vector2(width,Mathf.Max(content.parent is RectTransform rect?rect.rect.height:490,y));
        }
        private void RenderFidelityWorld()
        {
            FText(_fidelityContent,"ChannelHeading","聊天频道",184,134,251,52,34,Jade,align:TextAlignmentOptions.Center);
            FText(_fidelityContent,"WorldHeading",SocialState.ChannelName(State.Channel)+"频道",506,143,169,45,30,Jade);
            FText(_fidelityContent,"WorldDescription","与四海仙友共话仙途",667,149,680,36,22,Jade);
            FText(_fidelityContent,"CommunityNotice","文明言谈 · 仙友同行",184,703,247,50,25,Ink,align:TextAlignmentOptions.Center);
            int index=0;
            foreach(var channel in SocialState.ChatChannels)
            {
                var captured=channel;
                FHit(_fidelityContent,"SocialChannel_"+channel,SocialState.ChannelName(channel),269,197+index*84,130,65,()=>
                { _forceBottom=true;State.SelectChannel(captured);if(!State.IsPreview&&State.Supports(captured))RefreshRequested?.Invoke(captured); },
                    size:34,color:State.Channel==channel?Cream:Ink);
                index++;
            }
            if(!State.Supports(State.Channel))
            {
                FText(_fidelityContent,"SocialUnavailable","此频道尚未开放\n道友可前往世界频道传音。",650,360,900,130,30,Muted,true,TextAlignmentOptions.Center);
                return;
            }
            _chatScroll=Scroll(_fidelityContent,"SocialChatScroll",504,198,1240,492,out var content);
            FMessages(content,State.Messages(State.Channel),1224);
            _chatScroll.onValueChanged.AddListener(_=>UpdateLatest());
            var latest=FHit(_fidelityContent,"SocialLatest","最新消息",1575,151,123,38,()=>
            { _chatScroll.verticalNormalizedPosition=0;State.MarkRead(State.Channel);UpdateLatest();
                if(!State.IsPreview&&State.IsReady&&!State.Busy&&!State.RequiresReconnect)RefreshRequested?.Invoke(State.Channel); },size:22);
            _latestLabel=latest.GetComponentInChildren<TMP_Text>();
            FHit(_fidelityContent,"SocialRefresh","",1690,151,56,38,()=>RefreshRequested?.Invoke(State.Channel),
                !State.IsPreview&&State.IsReady&&!State.Busy&&!State.RequiresReconnect);
            _composer=FInput(_fidelityContent,"SocialComposer","在"+SocialState.ChannelName(State.Channel)+"频道说点什么…",State.Draft(State.Channel),
                680,708,727,50,true);
            _composer.interactable=State.CanSend(State.Channel);
            _counter=FText(_fidelityContent,"SocialCharacterCount","0/120",1415,713,87,43,22,Muted,align:TextAlignmentOptions.MidlineRight);
            _send=FHit(_fidelityContent,"SocialSend","发送",1535,701,220,68,()=>SendComposer(false),size:34,color:Cream);
            FHit(_fidelityContent,"SocialEmoji","",491,701,72,68,ShowEmoji,State.CanSend(State.Channel));
            FHit(_fidelityContent,"SocialShare","",577,701,72,68,ShowShare,State.IsPreview);
            _composer.onValueChanged.AddListener(value=>{State.SetDraft(State.Channel,value);UpdateComposer(false);});
            var keys=_composer.gameObject.AddComponent<SocialComposerKeyboard>();keys.Field=_composer;keys.Send=()=>SendComposer(false);
            UpdateComposer(false);
        }
    }
}
