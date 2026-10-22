using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace MmorpgClient.UI.Ugui.Skills
{
    public sealed partial class SkillPanel
    {
        // Exact relative geometry from skills HTML; outer Y keeps its title/footer inside the 1080 canvas.
        // Skills owns its 1660 x 940.296875 geometry and does not use the attribute layout helper.
        private bool _fidelityCanPreviewClasses;
        private static readonly Color SkillsInk=QdaoUguiTheme.Html("#304736"),SkillsMuted=QdaoUguiTheme.Html("#867854"),SkillsRule=QdaoUguiTheme.Html("#CAB991");
        private static readonly string[] ClassRoles={"近战物攻","法术输出","治疗辅助","高速先手"};
        private void ApplyFidelityLayout()
        {
            Place(_root,(QdaoUguiTheme.DesignWidth-1660f)*.5f,90,1660,940.296875f);
            // Existing client frame/title PNGs are byte-identical to the approved HTML assets.
            _root.GetComponent<Image>().pixelsPerUnitMultiplier=2.49f;
            Place(Find("TitlePlate"),630,-55,400,82);Place(_title.rectTransform,630,-55,400,82);
            Font(_title,38,QdaoRefreshArt.Ivory);_title.fontStyle=FontStyles.Bold;_title.characterSpacing=9;
            bool pet=_state?.Context?.IsPet==true,readOnly=_state==null||_state.IsReadOnly;
            var snapshot=_state?.Snapshot;var selected=snapshot?.Find(_selectedId);var skill=selected?.Definition;
            _title.text=pet?"宝宝技能":"四脉真诀";
            Place(Find("Close"),1600,-28,58,58);Place(Find("CloseTassel"),1640,25,24,65);
            Label("SkillsTopline",41,29,300,30,pet?"宝宝技能":"人物技能",18,SkillsMuted);
            Rule("SkillsTopRule",41,73.796875f,1578,1);Rule("SkillsClassRule",235,96.796875f,1,745.5f);
            Rule("SkillsDetailRule",1269,96.796875f,1,745.5f);Rule("SkillsCategoryRule",274,228.984375f,957,1);
            Rule("SkillsFooterRule",41,863.296875f,1578,1);
            Label("V10ClassHeading",41,101.796875f,169,28,pet?"灵宠技能":"四门传承",21,SkillsMuted);
            for(int i=0;i<_classes.Length;i++)
            {
                Button(_classes[i],41,150.796875f+i*98.28125f,169,87.28125f,27);
                _classes[i].SetVisible(!pet);
                // Catalog selection must never change the authoritative live character class.
                _classes[i].SetInteractable(!pet&&readOnly&&_fidelityCanPreviewClasses);
                bool selectedClass=snapshot?.ClassId==i+1;
                if(!selectedClass)PlainControl(_classes[i],"class-normal");
                _classes[i].Label.alignment=TextAlignmentOptions.MidlineLeft;Place(_classes[i].Label.rectTransform,16,13.9f,137,35.1f);
                _classes[i].Label.fontStyle=FontStyles.Bold;_classes[i].Label.characterSpacing=4;
                _classes[i].Label.color=selectedClass?QdaoUguiTheme.Html("#FFF0C8"):SkillsInk;
                var classRole=Label("SkillsClassRole"+i,16,54.09375f,137,19.1875f,ClassRoles[i],12,
                    QdaoUguiTheme.Html(selectedClass?"#E3EACB":"#70634E"),_classes[i].Rect,true);
                classRole.characterSpacing=1;
            }
            uint classId=snapshot?.ClassId??0;
            string role=!pet&&classId>=1&&classId<=4?ClassRoles[classId-1]:string.Empty;
            string className=!string.IsNullOrEmpty(snapshot?.Title)?snapshot.Title:pet?_state.Context.DisplayName:skill?.className??"职业技能";
            _classLabel.text=className;Place(_classLabel.rectTransform,274,97.796875f,700,36);Font(_classLabel,30,SkillsInk);_classLabel.fontStyle=FontStyles.Bold;
            Label("SkillsClassSubtitle",274,137.796875f,700,23,pet?"宠物技能 · 按实际技能分类展示":role,12,SkillsMuted);
            int learned=0;if(!readOnly&&snapshot!=null)foreach(var entry in snapshot.Entries)if(entry!=null&&entry.Rank>0)learned++;
            // HTML .rank-summary: 30px jade strong, 7px gap, 12px caption aligned to the content edge.
            var rankCaption=Label("SkillsRankSummary",1020,123.796875f,211,19.1875f,
                readOnly?"学习状态未同步":"/ "+(snapshot?.Entries.Count??0)+" 已修习",12,QdaoUguiTheme.Html("#70634E"),null,true);
            rankCaption.alignment=TextAlignmentOptions.MidlineRight;
            var rankCount=Label("SkillsRankCount",1155.28125f,111.796875f,60,30,learned.ToString(),30,QdaoUguiTheme.Html("#205B46"));
            rankCount.gameObject.SetActive(!readOnly);rankCount.alignment=TextAlignmentOptions.MidlineRight;
            rankCount.overflowMode=TextOverflowModes.Overflow;
            if(!readOnly)
            {
                float captionWidth=rankCaption.GetPreferredValues(rankCaption.text).x;
                float countWidth=rankCount.GetPreferredValues(rankCount.text).x;
                Place(rankCaption.rectTransform,1231-captionWidth,123.796875f,captionWidth,19.1875f);
                Place(rankCount.rectTransform,1231-captionWidth-7-countWidth,111.796875f,countWidth,30);
            }
            _classInfo.gameObject.SetActive(true);
            _classInfo.text=pet?"宠物技能以同步数据为准":readOnly?"仅浏览职业图鉴\n同步后显示学习状态":"当前角色等级："+(snapshot?.PlayerLevel??0)+"\n技能以服务器数据为准";
            Place(_classInfo.rectTransform,45,565,161,125);Font(_classInfo,15,SkillsMuted,true);_classInfo.textWrappingMode=TextWrappingModes.Normal;
            for(int i=0;i<_filters.Length;i++)
            {
                var filter=_filters[i];if(!filter.Rect.gameObject.activeSelf)continue;
                if(i<3)Button(filter,274+i*108,174.984375f,100,40,18);
                else Button(filter,1050+(i-3)*90,174.984375f,82,40,16);
                filter.Label.text=filter.Label.text.Replace("✓ ",string.Empty);
                bool selectedFilter=_filter==i;
                PlainControl(filter,selectedFilter?"category-selected":"category-normal");
                filter.Label.color=QdaoUguiTheme.Html(selectedFilter?"#2D6848":"#7D6A47");
                filter.Label.characterSpacing=2;
            }
            Place(Find("NameHeader"),347,241.984375f,650,20);Place(Find("RankHeader"),1033,241.984375f,63,20);Place(Find("ManaHeader"),1108,241.984375f,111,20);
            foreach(string name in new[]{"NameHeader","RankHeader","ManaHeader"})Font(Find(name)?.GetComponent<TMP_Text>(),12,SkillsMuted,true);
            Find("ManaHeader").GetComponent<TMP_Text>().text="消耗 / 目标";Find("ManaHeader").GetComponent<TMP_Text>().alignment=TextAlignmentOptions.MidlineRight;
            foreach(string name in new[]{"KindHeader","TargetHeader"})Find(name)?.gameObject.SetActive(false);
            Place(_scroll.viewport,274,269.171875f,957,ListHeight);_content.sizeDelta=new Vector2(957,Mathf.Max(ListHeight,_visible.Count*RowStep-6));
            Place(Find("SkillListScrollbar"),1237,269.171875f,10,ListHeight);Place(_empty.rectTransform,20,110,917,160);Font(_empty,22,SkillsMuted,true);
            for(int i=0;i<_rows.Count;i++)
            {
                var row=_rows[i];Place(row.Button.Rect,0,i*RowStep,957,RowHeight);row.Button.Plate.sprite=null;row.Button.Plate.type=Image.Type.Simple;
                row.Button.Plate.color=QdaoUguiTheme.Html(row.Id==_selectedId?"#DFE9D3":"#FFF7E7");
                var border=row.Button.Plate.GetComponent<Outline>()??row.Button.Plate.gameObject.AddComponent<Outline>();
                border.effectColor=QdaoUguiTheme.Html(row.Id==_selectedId?"#789770":"#D1C6A6");border.effectDistance=new Vector2(1,-1);border.useGraphicAlpha=true;
                Place(row.Icon.rectTransform,7,8,50,50);Place(row.Name.rectTransform,73,2,674,38);Font(row.Name,23,SkillsInk);row.Name.fontStyle=FontStyles.Bold;row.Name.text=row.Name.text.Replace("✓ ",string.Empty);
                Place(row.Kind.rectTransform,73,41.796875f,674,18);Font(row.Kind,11,SkillsMuted,true);
                Place(row.Rank.rectTransform,759,22.796875f,63,21);Font(row.Rank,13,SkillsInk,true);
                Place(row.Mana.rectTransform,834,12.796875f,111,22);Font(row.Mana,13,SkillsInk,true);Place(row.Target.rectTransform,834,34.796875f,111,18);Font(row.Target,11,SkillsMuted,true);
                row.Mana.alignment=row.Target.alignment=TextAlignmentOptions.MidlineRight;
            }
            Label("SkillsDetailKicker",1305,104.796875f,314,25,className+(pet?" · 灵宠术法":" · 术法真传"),12,SkillsMuted).alignment=TextAlignmentOptions.Center;
            Place(_selectedIcon.rectTransform,1357,138.984375f,210,210);Place(_selectedName.rectTransform,1305,363.984375f,314,42);Font(_selectedName,32,SkillsInk);_selectedName.fontStyle=FontStyles.Bold;_selectedName.alignment=TextAlignmentOptions.Center;
            string tags=skill==null?string.Empty:skill.KindLabel+" · "+skill.TargetLabel+(readOnly?" · 状态未同步":selected.Rank>0?" · 已修习 "+selected.Rank+" 级":" · 未学习");
            Label("SkillsDetailTags",1305,413.578125f,314,24,tags,11,SkillsMuted).alignment=TextAlignmentOptions.Center;
            Place(_descriptionScroll.viewport,1305,457.171875f,314,70);Font(_description,15,QdaoUguiTheme.Html("#596047"),true);
            float height=Mathf.Max(70,_description.GetPreferredValues(_description.text,304,0).y+4);
            _description.rectTransform.sizeDelta=new Vector2(304,height);_descriptionContent.sizeDelta=new Vector2(314,height);Place(Find("DescriptionScrollbar"),1624,457.171875f,10,70);
            Rule("SkillsMetricsTop",1305,542.171875f,314,1);Rule("SkillsMetricsBottom",1305,661.140625f,314,1);
            Label("SkillsManaLabel",1305,554,150,20,"法力消耗",11,SkillsMuted);Label("SkillsManaValue",1305,577,150,26,skill==null?"—":skill.IsPassive?"无消耗":(skill.manaPercent*100).ToString("0.#")+"%法力",14,SkillsInk,null,true);
            Label("SkillsCooldownLabel",1469,554,150,20,"冷却回合",11,SkillsMuted);Label("SkillsCooldownValue",1469,577,150,26,skill==null?"—":skill.cooldownRounds==0?"无":skill.cooldownRounds+"回合",14,SkillsInk,null,true);
            Label("SkillsKindLabel",1305,607,150,20,"技能类型",11,SkillsMuted);Label("SkillsKindValue",1305,630,150,26,skill?.KindLabel??"—",14,SkillsInk,null,true);
            Label("SkillsUnlockLabel",1469,607,150,20,"初始解锁",11,SkillsMuted);Label("SkillsUnlockValue",1469,630,150,26,skill==null?"—":skill.unlockLevel+"级",14,SkillsInk,null,true);
            Place(_condition.rectTransform,1305,695,314,48);Font(_condition,12,SkillsMuted,true);Place(_balance.rectTransform,1305,744,314,22);Font(_balance,12,SkillsInk,true);Place(_cost.rectTransform,1305,768,314,22);Font(_cost,12,SkillsInk,true);
            Button(_upgrade,1305,794.296875f,152,48,19);Button(_upgradeTen,1467,794.296875f,152,48,19);CharacterAttributeVisuals.Skin(_upgradeTen.Plate,"button_secondary");_upgradeTen.Label.color=SkillsInk;
            Button(_refresh,1509,879.296875f,110,39,16);Place(_status.rectTransform,41,879.296875f,1425,39);Font(_status,13,null,true);
            string[] tabs={"AttributeTab","AllocationTab","SkillTab"};for(int i=0;i<tabs.Length;i++)
            {
                var rect=Find(tabs[i]);if(rect==null)continue;Place(rect,1310+i*102,25,94,40);CharacterAttributeVisuals.Skin(rect.GetComponent<Image>(),i==2?"button_primary":"button_secondary");
                var label=rect.GetComponentInChildren<TMP_Text>();label.text=label.text.Replace("\n",string.Empty);Place(label.rectTransform,0,0,94,40);Font(label,18,i==2?QdaoRefreshArt.Ivory:SkillsInk);
            }
        }
        // Only these HTML controls have CSS flat backgrounds; preserve the existing production callbacks.
        private static void PlainControl(UiTextButton button,string key)
        {
            button.Plate.sprite=QdaoUguiTheme.RequireSprite("UI/Ugui/SkillsExact20261022/"+key);
            button.Plate.type=Image.Type.Sliced;button.Plate.pixelsPerUnitMultiplier=1;button.Plate.color=Color.white;
            var colors=button.Button.colors;colors.selectedColor=Color.white;button.Button.colors=colors;
        }
        private RectTransform Find(string name)
        {foreach(var rect in _root.GetComponentsInChildren<RectTransform>(true))if(rect.name==name||rect.name==name+"Plate")return rect;return null;}
        private static void Place(RectTransform rect,float x,float y,float width,float height)
        {if(rect==null)return;rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);}
        private static void Font(TMP_Text text,float size,Color? color=null,bool body=false)
        {if(text==null)return;text.font=body?QdaoUguiTypography.ResolveBodyFont():QdaoUguiTheme.ResolveFont();text.fontSize=size;text.fontStyle=FontStyles.Normal;if(color.HasValue)text.color=color.Value;}
        private static void Button(UiTextButton button,float x,float y,float width,float height,float size)
        {Place(button.Rect,x,y,width,height);Place(button.Label.rectTransform,8,0,width-16,height);Font(button.Label,size);button.Plate.pixelsPerUnitMultiplier=button.Plate.sprite==null?1:button.Plate.sprite.rect.height/height;}
        private TMP_Text Label(string name,float x,float y,float width,float height,string value,float size,Color color,UnityEngine.Transform parent=null,bool body=false)
        {
            var text=Find(name)?.GetComponent<TMP_Text>();if(text==null)text=QdaoUguiFactory.CreateText(name,parent==null?_root:parent,x,y,width,height,value,size,color);
            Place(text.rectTransform,x,y,width,height);Font(text,size,color,body);text.text=value;text.richText=false;text.raycastTarget=false;return text;
        }
        private void Rule(string name,float x,float y,float width,float height)
        {if(Find(name)!=null)return;var rule=QdaoUguiFactory.CreateImage(name,_root,x,y,width,height,null);rule.color=SkillsRule;rule.raycastTarget=false;}
    }
}
