using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.Social;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Social.SocialUiArt;

namespace MmorpgClient.UI.Ugui.Social
{
    public sealed partial class SocialWindow
    {
        private void RenderFidelityGroups()
        {
            FText(_fidelityContent,"SocialGroupsHeading","我的群组",216,166,285,47,30,Jade);
            FText(_fidelityContent,"SocialGroupInfoHeading","群信息",1400,166,220,44,29,Jade);
            FImage(_fidelityContent,"list_paper",164,272,404,410);
            var search=FInput(_fidelityContent,"SocialGroupSearch","搜索群组",_groupSearch,208,214,305,45,false,24);
            search.textComponent.fontSize=22;
            if(search.placeholder is TMP_Text searchHint)searchHint.fontSize=22;
            search.onValueChanged.AddListener(value=>{_groupSearch=value;Render();});
            FHit(_fidelityContent,"SocialGroupCreate","",193,692,341,68,ShowCreate,State.IsPreview);
            FText(_fidelityContent,"SocialGroupCreateLabel","创建群组",348,702,164,46,27,State.IsPreview?Ink:Muted);
            if(!State.IsPreview)
            {
                search.interactable=false;
                FText(_fidelityContent,"SocialGroupsUnavailable","群组服务尚未开放",648,381,631,66,34,Jade,align:TextAlignmentOptions.Center);
                FText(_fidelityContent,"SocialGroupsUnavailableCopy","群组开放后，将显示你的真实群组。",650,453,630,66,25,Muted,true,TextAlignmentOptions.Center);
                return;
            }

            FPageScroll(_fidelityContent,"SocialGroupList",164,272,404,410,out var list);
            var groups=State.Groups.Where(group=>string.IsNullOrEmpty(_groupSearch)||group.Name.Contains(_groupSearch))
                .OrderByDescending(group=>group.Pinned).ThenBy(group=>group.Id).ToArray();
            float y=0;
            foreach(var group in groups)
            {
                var captured=group;
                var row=QdaoUguiFactory.CreateRect("SocialGroupRow_"+group.Id,list,0,y,404,112);
                bool selected=State.SelectedGroupId==group.Id;
                string rowKey=selected?"group_row_selected":"group_row_normal";
                if(!selected&&group==State.Groups.LastOrDefault()&&State.Groups.Count>2&&Resources.Load<Sprite>(FidelityResources+"group_row_lotus")!=null)
                    rowKey="group_row_lotus";
                FImage(row,rowKey,0,0,404,112);
                FText(row,"GroupName",group.Name,116,17,214,39,25,Jade);
                FText(row,"GroupTime",group.Messages.LastOrDefault()?.TimeLabel??"",329,14,65,30,18,Muted,align:TextAlignmentOptions.MidlineRight);
                var last=group.Messages.LastOrDefault();
                string snippet=last==null?"静候道友，结伴同行":State.Person(last.Sender).Name+"："+last.Text;
                FText(row,"GroupLastMessage",snippet,116,62,274,37,20,Muted);
                if(group.Pinned)FText(row,"GroupPinned","置顶",300,72,80,25,17,Ink,align:TextAlignmentOptions.Center);
                if(group.Unread>0)FText(row,"GroupUnread",group.Unread.ToString(),353,53,36,32,20,QdaoUguiTheme.Html("#A95C3E"),align:TextAlignmentOptions.Center);
                FHit(row,"SocialGroupSelect_"+group.Id,"",0,0,404,112,()=>{_forceBottom=true;State.SelectGroup(captured.Id);});
                y+=122;
            }
            if(groups.Length==0)FText(list,"SocialGroupsEmptySearch","未找到群组，试试其他名称。",20,84,364,102,24,Muted,true,TextAlignmentOptions.Center);
            list.sizeDelta=new Vector2(404,Mathf.Max(410,y));
            var current=State.SelectedGroup;
            if(current==null)
            {
                FText(_fidelityContent,"SocialGroupsEmpty","道友相聚，一言成缘",649,377,628,69,34,Jade,align:TextAlignmentOptions.Center);
                FText(_fidelityContent,"SocialGroupsEmptyCopy","点击创建群组，与熟悉的道友一起云游。",645,448,635,78,25,Muted,true,TextAlignmentOptions.Center);
                return;
            }
            FText(_fidelityContent,"SocialSelectedGroupName",current.Name,725,168,240,43,28,Jade);
            int online=current.Members.Count(id=>State.Person(id).Online);
            FText(_fidelityContent,"SocialSelectedGroupOnline",current.Members.Count+"人 · "+online+"人在线",964,175,280,30,20,Muted);
            FText(_fidelityContent,"SocialSelectedGroupAnnouncement","群公告："+current.Announcement,776,216,527,37,21,Ink);
            FHit(_fidelityContent,"SocialGroupMore","···",1254,164,58,45,ShowEdit,current.Owner==State.PlayerId,30,Jade);
            _chatScroll=FPageScroll(_fidelityContent,"SocialGroupChatScroll",602,273,727,425,out var messages);
            FMessages(messages,current.Messages,727);
            FGroupComposer();

            FHit(_fidelityContent,"SocialGroupEdit","编辑",1677,169,74,38,ShowEdit,current.Owner==State.PlayerId,22,Ink);
            FText(_fidelityContent,"GroupNameLabel","群名称",1377,224,105,39,23,Ink);
            FText(_fidelityContent,"GroupNameValue",current.Name,1484,224,264,39,24,Jade);
            FText(_fidelityContent,"GroupAnnouncementLabel","群公告",1377,272,105,39,23,Ink);
            FText(_fidelityContent,"GroupAnnouncementValue",current.Announcement,1484,272,264,55,22,Ink,true);
            FText(_fidelityContent,"GroupMembersLabel","群成员",1377,325,105,39,23,Ink);
            FText(_fidelityContent,"GroupMembersValue",current.Members.Count+"/50",1491,326,234,38,22,Muted);
            FImage(_fidelityContent,"list_paper",1392,359,369,218);
            FPageScroll(_fidelityContent,"SocialGroupMembers",1392,359,369,218,out var members);
            int index=0;
            foreach(ulong id in current.Members)
            {
                var person=State.Person(id);
                float mx=(index%2)*202,my=(index/2)*116;
                FPortrait(members,id,mx,my,94);
                FText(members,"GroupMemberName_"+id,person.Name,mx+104,my+32,94,32,21,Jade);
                FText(members,"GroupMemberStatus_"+id,person.Online?"● 在线":"● 离线",mx+104,my+66,94,28,18,
                    person.Online?QdaoUguiTheme.Html("#2B8A49"):Muted);
                if(id==current.Owner)FText(members,"GroupOwner_"+id,"群主",mx+10,my+82,76,26,18,Ink,align:TextAlignmentOptions.Center);
                index++;
            }
            members.sizeDelta=new Vector2(369,Mathf.Max(218,((index+1)/2)*116));
            FText(_fidelityContent,"SocialGroupPinLabel","置顶群组",1437,596,254,40,22,Ink);
            FHit(_fidelityContent,"SocialGroupPin","",1374,589,388,52,()=>State.SetGroupPinned(!current.Pinned));
            FText(_fidelityContent,"SocialGroupAlertsLabel","消息提醒",1437,646,254,40,22,Ink);
            FHit(_fidelityContent,"SocialGroupAlerts","",1374,641,388,51,()=>State.SetGroupAlerts(!current.Alerts));
            if(!current.Pinned)FImage(_fidelityContent,"input_field",1718,602,27,27);
            if(!current.Alerts)FImage(_fidelityContent,"input_field",1718,653,27,27);
            FHit(_fidelityContent,"SocialGroupInvite","邀请好友",1433,702,133,58,ShowInvite,size:26,color:Cream);
            FHit(_fidelityContent,"SocialGroupLeave","退出群组",1643,702,119,58,ShowLeave,size:25,color:Ink);
        }

        private void FGroupComposer()
        {
            _composer=FInput(_fidelityContent,"SocialComposer","与同道聊聊吧…",State.SelectedGroup?.Draft,765,711,398,49,true);
            _composer.interactable=State.IsPreview&&State.SelectedGroup!=null;
            _counter=FText(_fidelityContent,"SocialCharacterCount","0/120",1075,683,87,24,16,Muted,align:TextAlignmentOptions.MidlineRight);
            _counter.gameObject.SetActive(false); // The approved group composer has no visible character counter.
            _send=FHit(_fidelityContent,"SocialSend","发送",1180,710,141,56,()=>SendComposer(true),size:29,color:Cream);
            FHit(_fidelityContent,"SocialEmoji","",609,709,65,59,ShowEmoji,State.IsPreview);
            FHit(_fidelityContent,"SocialShare","",681,709,65,59,ShowShare,false);
            _composer.onValueChanged.AddListener(value=>{if(State.SelectedGroup!=null)State.SelectedGroup.Draft=value;UpdateComposer(true);});
            var keys=_composer.gameObject.AddComponent<SocialComposerKeyboard>();keys.Field=_composer;keys.Send=()=>SendComposer(true);
            UpdateComposer(true);
        }

        private void RenderFidelityRumors()
        {
            FText(_fidelityContent,"SocialRumorCategoriesHeading","传闻分类",222,131,211,54,34,Jade);
            FText(_fidelityContent,"SocialRumorHeading","谣 言",515,140,100,43,32,Jade);
            FText(_fidelityContent,"SocialRumorSubheading","云间传闻 · 四海皆知",619,147,504,33,22,Ink);
            FText(_fidelityContent,"SocialRumorReadonlyNotice","本频道为系统传闻，无法发言。",581,192,1078,40,25,Ink);
            FImage(_fidelityContent,"list_paper",484,246,1277,486);
            int filterIndex=0;
            foreach(RumorCategory category in Enum.GetValues(typeof(RumorCategory)))
            {
                var captured=category;
                string label=category==RumorCategory.All?"全部传闻":category==RumorCategory.Beast?"妖兽现世":category==RumorCategory.Treasure?"珍宝奇遇":"仙友捷报";
                float filterY=196+filterIndex*84;
                if(State.RumorFilter!=RumorCategory.All)
                    FImage(_fidelityContent,State.RumorFilter==category?"button_selected":"button_normal",175,filterY,262,70);
                FHit(_fidelityContent,"SocialRumorFilter_"+category,label,258,filterY+5,151,57,
                    ()=>State.FilterRumors(captured,State.RumorSearch),State.IsPreview,29,State.RumorFilter==category?Cream:Ink);
                filterIndex++;
            }
            if(!State.IsPreview)
            {
                FHit(_fidelityContent,"SocialRumorRefresh","回到最新",1582,147,169,40,null,false,22,Muted);
                FText(_fidelityContent,"SocialRumorUnavailable","系统传闻尚未开放",694,401,848,69,34,Jade,align:TextAlignmentOptions.Center);
                FText(_fidelityContent,"SocialRumorUnavailableCopy","当前服务器未提供系统频道历史。",694,471,848,65,25,Muted,true,TextAlignmentOptions.Center);
                return;
            }
            var search=FInput(_fidelityContent,"SocialRumorSearch","搜索传闻",State.RumorSearch,1176,147,385,36,false,40);
            search.textComponent.fontSize=21;
            if(search.placeholder is TMP_Text placeholder)placeholder.fontSize=21;
            search.onValueChanged.AddListener(value=>State.FilterRumors(State.RumorFilter,value));
            var scroll=FPageScroll(_fidelityContent,"SocialRumorScroll",484,246,1277,486,out var content);
            FHit(_fidelityContent,"SocialRumorLatest","回到最新",1582,147,169,40,()=>scroll.verticalNormalizedPosition=1,size:22,color:Jade);
            float y=0;
            foreach(var rumor in State.FilteredRumors())
            {
                var captured=rumor;
                float height=rumor.Ended?86:129;
                var row=QdaoUguiFactory.CreateRect("SocialRumor_"+rumor.Id,content,0,y,1277,height);
                FImage(row,rumor.Ended?"rumor_row_ended":"rumor_row",0,0,1277,height);
                string category=rumor.Category==RumorCategory.Beast?"妖兽现世":rumor.Category==RumorCategory.Treasure?"珍宝奇遇":"仙友捷报";
                FText(row,"RumorSeal","谣\n言",19,rumor.Ended?8:16,44,63,25,Cream,align:TextAlignmentOptions.Center);
                FText(row,"RumorTime",FPageTime(rumor.TimeMs),16,rumor.Ended?64:91,74,26,18,Muted,align:TextAlignmentOptions.Center);
                FText(row,"RumorCategory",category,314,8,116,34,21,Ink,align:TextAlignmentOptions.Center);
                string icon=rumor.Ended?"rumor_ended":rumor.Category==RumorCategory.Beast?"rumor_beast":rumor.Category==RumorCategory.Treasure?"rumor_treasure":"rumor_victory";
                FImage(row,icon,rumor.Category==RumorCategory.Victory?108:132,6,
                    rumor.Category==RumorCategory.Victory?205:150,rumor.Ended?73:116);
                string headline=rumor.Text??rumor.Title??"仙境传闻";
                int split=headline.IndexOf('，');
                if(split>0)headline=headline.Substring(0,split);
                FText(row,"RumorHeadline",headline,452,4,620,38,28,Jade);
                FText(row,"RumorText",rumor.Text,452,42,807,38,23,Ink);
                if(!rumor.Ended)
                {
                    if(!string.IsNullOrEmpty(rumor.Place))
                    {
                        var chip=FImage(row,"button_normal",452,78,255,40);chip.color=QdaoUguiTheme.Html("#E6EFD7");
                        FHit(row,"SocialRumorLocation_"+rumor.Id,rumor.Place+"（"+rumor.Coordinates+"）",452,78,255,40,()=>ShowRumor(captured),size:20,color:Jade);
                    }
                    var timer=FText(row,"SocialRumorCountdown_"+rumor.Id,rumor.StateLabel(State.NowMs),
                        string.IsNullOrEmpty(rumor.Place)?452:722,79,string.IsNullOrEmpty(rumor.Place)?578:351,38,20,Muted);
                    _countdowns.Add(new KeyValuePair<TMP_Text,SocialRumor>(timer,rumor));
                }
                string action=rumor.Ended?"已结束":rumor.Category==RumorCategory.Beast?"查看位置":rumor.Category==RumorCategory.Treasure?"查看珍宝":"查看捷报";
                FHit(row,"SocialRumorDetail_"+rumor.Id,action,1082,rumor.Ended?15:55,177,58,
                    ()=>ShowRumor(captured),!rumor.Ended,25,rumor.Ended?Cream:Ink);
                y+=height+5;
            }
            if(y==0)
            {
                FText(content,"SocialRumorEmpty","此间暂无传闻",140,123,990,66,34,Jade,align:TextAlignmentOptions.Center);
                FText(content,"SocialRumorEmptyCopy","换个分类或关键词，再听听仙境风声。",140,194,990,62,25,Muted,align:TextAlignmentOptions.Center);
                FImage(content,"button_normal",450,280,370,60);
                FHit(content,"SocialRumorClear","查看全部传闻",450,280,370,60,()=>State.FilterRumors(RumorCategory.All,""),size:25,color:Ink);
            }
            content.sizeDelta=new Vector2(1277,Mathf.Max(486,y));
            FText(_fidelityContent,"SocialRumorFooter","系统传闻 · 离线预览",538,735,672,36,21,Muted);
            _channelClock=FText(_fidelityContent,"SocialChannelClock","频道时刻  "+FPageTime(State.NowMs),1550,735,207,36,20,Muted,align:TextAlignmentOptions.MidlineRight);
        }

        private static ScrollRect FPageScroll(UnityEngine.Transform parent,string name,float x,float y,float width,float height,out RectTransform content)
        {
            var rect=QdaoUguiFactory.CreateRect(name,parent,x,y,width,height);
            var hit=rect.gameObject.AddComponent<Image>();hit.color=new Color(1,1,1,.001f);hit.raycastTarget=true;
            var viewport=QdaoUguiFactory.CreateStretch("Viewport",rect,Vector4.zero);viewport.gameObject.AddComponent<RectMask2D>();
            content=QdaoUguiFactory.CreateRect("Content",viewport,0,0,width,height);
            var scroll=rect.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=content;
            scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=40;
            return scroll;
        }
        private static string FPageTime(long milliseconds)
        {
            try{return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToLocalTime().ToString("HH:mm");}
            catch{return "时间未知";}
        }
    }
}
