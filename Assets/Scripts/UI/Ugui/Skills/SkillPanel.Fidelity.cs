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
        private static readonly Color SkillsInk=QdaoUguiTheme.Html("#25231C"),SkillsMuted=QdaoUguiTheme.Html("#654A34"),SkillsRule=QdaoUguiTheme.Html("#CCBA95");
        private static readonly Color SkillsJadeInk=QdaoUguiTheme.Html("#184833");
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
            PlaceClose();
            var closeImage = Find("Close")?.GetComponent<Image>();
            if (closeImage != null)
            {
                if (!HasLoadout || !ApplyGuildSprite(closeImage, "guild_close"))
                    CharacterAttributeVisuals.Skin(closeImage, "close_button");
                else closeImage.preserveAspect = true;
            }
            Label("SkillsTopline",41,29,300,30,pet?"宝宝技能":"人物技能",22,SkillsInk);
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
                else if(i==5)Button(filter,598,174.984375f,112,40,17);
                else Button(filter,1050+(i-3)*90,174.984375f,82,40,16);
                filter.Label.text=filter.Label.text.Replace("✓ ",string.Empty);
                bool selectedFilter=_filter==i;
                PlainControl(filter,selectedFilter?"category-selected":"category-normal");
                filter.Label.color=QdaoUguiTheme.Html(selectedFilter?"#2D6848":"#7D6A47");
                filter.Label.characterSpacing=2;
            }
            var studyHeader = _studyRoot.Find("StudyTableHeader") as RectTransform;
            if (studyHeader == null && HasLoadout)
            {
                var header = QdaoUguiFactory.CreateImage("StudyTableHeader", _studyRoot, 274, 234, 957, 33, null);
                ApplyGuildSprite(header, "guild_table_header");
                studyHeader = header.rectTransform;
                studyHeader.SetAsFirstSibling();
            }
            if (studyHeader != null) studyHeader.gameObject.SetActive(HasLoadout);
            Place(Find("NameHeader"),HasLoadout?380:347,236,650,28);Place(Find("RankHeader"),1033,236,63,28);Place(Find("ManaHeader"),1108,236,111,28);
            foreach(string name in new[]{"NameHeader","RankHeader","ManaHeader"})Font(Find(name)?.GetComponent<TMP_Text>(),HasLoadout?19:12,SkillsMuted,!HasLoadout);
            Find("ManaHeader").GetComponent<TMP_Text>().text="消耗 / 目标";Find("ManaHeader").GetComponent<TMP_Text>().alignment=TextAlignmentOptions.MidlineRight;
            foreach(string name in new[]{"KindHeader","TargetHeader"})Find(name)?.gameObject.SetActive(false);
            Place(_scroll.viewport,274,269.171875f,957,ListHeight);_content.sizeDelta=new Vector2(957,Mathf.Max(ListHeight,_visible.Count*RowStep-6));
            Place(Find("SkillListScrollbar"),1237,269.171875f,10,ListHeight);Place(_empty.rectTransform,20,110,917,160);Font(_empty,22,SkillsMuted,true);
            for(int i=0;i<_rows.Count;i++)
            {
                var row = _rows[i];
                bool selectedRow = row.Id == _selectedId;
                Place(row.Button.Rect, 0, i * RowStep, 957, RowHeight);
                bool paintedRow = HasLoadout && ApplyGuildSprite(row.Button.Plate, selectedRow ? "guild_row_self" : "guild_row_normal");
                var border = row.Button.Plate.GetComponent<Outline>();
                if (paintedRow)
                {
                    if (border != null) border.enabled = false;
                }
                else
                {
                    row.Button.Plate.sprite = null;
                    row.Button.Plate.type = Image.Type.Simple;
                    row.Button.Plate.color = QdaoUguiTheme.Html(selectedRow ? "#DFE9D3" : "#FFF7E7");
                    border ??= row.Button.Plate.gameObject.AddComponent<Outline>();
                    border.enabled = true;
                    border.effectColor = QdaoUguiTheme.Html(selectedRow ? "#789770" : "#D1C6A6");
                    border.effectDistance = new Vector2(1, -1);
                    border.useGraphicAlpha = true;
                }
                var mark = row.Button.Rect.Find("SelectedMark");
                if (paintedRow && selectedRow && mark == null)
                {
                    SkillLoadoutSubPanel.AddSelectedMark(row.Button.Rect, 12, 22, 22);
                    mark = row.Button.Rect.Find("SelectedMark");
                }
                if (mark != null) mark.gameObject.SetActive(paintedRow && selectedRow);
                float textX = paintedRow ? 106 : 73;
                Place(row.Icon.rectTransform,paintedRow?43:7,8,50,50);
                if(row.SeasonCornerBadge!=null)Place(row.SeasonCornerBadge,28,0,22,22);
                Place(row.Name.rectTransform,textX,2,544,36);Font(row.Name,paintedRow?25:23,selectedRow?SkillsJadeInk:SkillsInk);row.Name.text=row.Name.text.Replace("✓ ",string.Empty);
                Place(row.Kind.rectTransform,textX,36,612,26);Font(row.Kind,paintedRow?18:11,SkillsMuted,!paintedRow);
                Place(row.Rank.rectTransform,759,21,63,26);Font(row.Rank,paintedRow?18:13,SkillsInk,!paintedRow);
                Place(row.Mana.rectTransform,834,10,111,25);Font(row.Mana,paintedRow?17:13,SkillsInk,!paintedRow);
                Place(row.Target.rectTransform,834,35,111,23);Font(row.Target,paintedRow?17:11,SkillsMuted,!paintedRow);
                row.Mana.alignment=row.Target.alignment=TextAlignmentOptions.MidlineRight;
            }
            Label("SkillsDetailKicker",1305,104.796875f,314,25,className+(pet?" · 灵宠术法":" · 术法真传"),12,SkillsMuted).alignment=TextAlignmentOptions.Center;
            Place(_selectedIcon.rectTransform,1357,138.984375f,210,210);Place(_selectedName.rectTransform,1305,363.984375f,314,42);Font(_selectedName,32,SkillsInk);_selectedName.fontStyle=FontStyles.Bold;_selectedName.alignment=TextAlignmentOptions.Center;
            string tags=skill==null?string.Empty:skill.KindLabel+" · "+skill.TargetLabel
                +(readOnly?" · 状态未同步":selected.Rank>0?" · 已修习 "+selected.Rank+" 级":" · 未学习");
            Label("SkillsDetailTags",1305,409,314,24,tags,HasLoadout?17:11,SkillsMuted,!HasLoadout).alignment=TextAlignmentOptions.Center;
            var modifierTags = new System.Collections.Generic.List<string>(3);
            if (!string.IsNullOrEmpty(skill?.SecondaryTargetLabel)) modifierTags.Add(skill.SecondaryTargetLabel);
            if (skill?.exclusiveGroup > 0) modifierTags.Add("专精互斥");
            if (selected != null && ShowEquipped(selected, pet)) modifierTags.Add("已装配");
            var modifiers = Label("SkillsDetailModifierTags",1305,432,314,24,string.Join(" · ", modifierTags),HasLoadout?18:13,SkillsJadeInk,!HasLoadout);
            modifiers.alignment=TextAlignmentOptions.Center;
            modifiers.enableAutoSizing = HasLoadout;
            modifiers.fontSizeMin = 14;
            modifiers.fontSizeMax = 18;
            Place(_descriptionScroll.viewport,1305,457.171875f,314,70);Font(_description,HasLoadout?18:15,SkillsInk,!HasLoadout);
            float height=Mathf.Max(70,_description.GetPreferredValues(_description.text,304,0).y+4);
            _description.rectTransform.sizeDelta=new Vector2(304,height);_descriptionContent.sizeDelta=new Vector2(314,height);Place(Find("DescriptionScrollbar"),1624,457.171875f,10,70);
            Rule("SkillsMetricsTop",1305,542.171875f,314,1);Rule("SkillsMetricsBottom",1305,661.140625f,314,1);
            Label("SkillsManaLabel",1305,554,150,20,"法力消耗",11,SkillsMuted);Label("SkillsManaValue",1305,577,150,26,skill==null?"—":skill.IsPassive?"无消耗":(skill.manaPercent*100).ToString("0.#")+"%法力",14,SkillsInk,null,true);
            Label("SkillsCooldownLabel",1469,554,150,20,"冷却回合",11,SkillsMuted);Label("SkillsCooldownValue",1469,577,150,26,skill==null?"—":skill.cooldownRounds==0?"无":skill.cooldownRounds+"回合",14,SkillsInk,null,true);
            Label("SkillsKindLabel",1305,607,150,20,"技能类型",11,SkillsMuted);Label("SkillsKindValue",1305,630,150,26,skill?.KindLabel??"—",14,SkillsInk,null,true);
            Label("SkillsUnlockLabel",1469,607,150,20,"初始解锁",11,SkillsMuted);Label("SkillsUnlockValue",1469,630,150,26,skill==null?"—":skill.unlockLevel+"级",14,SkillsInk,null,true);
            Place(_condition.rectTransform,1305,695,314,48);Font(_condition,12,SkillsMuted,true);Place(_balance.rectTransform,1305,744,314,22);Font(_balance,12,SkillsInk,true);Place(_cost.rectTransform,1305,768,314,22);Font(_cost,12,SkillsInk,true);
            Button(_upgrade,1305,794.296875f,152,48,19);Button(_upgradeTen,1467,794.296875f,152,48,19);CharacterAttributeVisuals.Skin(_upgradeTen.Plate,"button_secondary");_upgradeTen.Label.color=SkillsInk;
            if(_seasonFreeBadge!=null)Place(_seasonFreeBadge,1372,796,180,44);
            Button(_refresh,1509,879.296875f,110,39,16);
            bool showSkillVfx = _skillVfxBtn != null && _skillVfxBtn.Rect.gameObject.activeSelf;
            if (showSkillVfx)
            {
                Button(_skillVfxBtn, 1365, 879.296875f, 134, 39, 16);
                CharacterAttributeVisuals.Skin(_skillVfxBtn.Plate, "button_secondary");
                _skillVfxBtn.Label.color = SkillsInk;
            }
            Place(_status.rectTransform,41,879.296875f,showSkillVfx ? 1310 : 1425,39);Font(_status,13,null,true);
            string[] tabs={"AttributeTab","AllocationTab","SkillTab"};for(int i=0;i<tabs.Length;i++)
            {
                var rect=Find(tabs[i]);if(rect==null)continue;
                bool studyTab = i == 2 && HasLoadout;
                float width = studyTab ? 118 : 94, tabHeight = studyTab ? 44 : 40;
                Place(rect,studyTab ? 1036 : 1310+i*102,studyTab ? 23 : 25,width,tabHeight);
                bool active = i==2&&!_loadoutMode;
                if (!HasLoadout || !ApplyGuildSprite(rect.GetComponent<Image>(), active ? "guild_tab_selected" : "guild_tab_normal"))
                    CharacterAttributeVisuals.Skin(rect.GetComponent<Image>(),active?"button_primary":"button_secondary");
                var label=rect.GetComponentInChildren<TMP_Text>();label.text=label.text.Replace("\n",string.Empty);Place(label.rectTransform,0,0,width,tabHeight);Font(label,HasLoadout?21:18,active?QdaoRefreshArt.Ivory:SkillsInk);
            }
            if(HasLoadout)
            {
                Button(_loadoutTab,1164,23,128,44,21);
                StyleGuildControl(_loadoutTab,_loadoutMode?"guild_tab_selected":"guild_tab_normal",
                    _loadoutMode?QdaoRefreshArt.Ivory:SkillsInk);
            }
        }
        internal static void StyleGuildControl(UiTextButton button, string art, Color ink, bool preserveReadOnlyColor = false)
        {
            ApplyGuildSprite(button.Plate, art);
            Font(button.Label, button.Label.fontSize, ink);
            button.Label.fontStyle = FontStyles.Bold;
            var colors = button.Button.colors;
            colors.highlightedColor = QdaoUguiTheme.Html("#FFF6E6");
            colors.selectedColor = Color.white;
            if (preserveReadOnlyColor) colors.disabledColor = Color.white;
            button.Button.colors = colors;
        }

        private static bool ApplyGuildSprite(Image plate, string art)
        {
            var sprite = SeasonEconomyUiArt.LoadGuildFidelityAsset(art);
            if (plate == null || sprite == null) return false;
            SeasonEconomyUiArt.ApplyFixedControlSprite(plate, sprite, Color.white);
            return true;
        }
        private void PlaceClose()
        {
            Place(Find("Close"), _closeOnLeft ? 27 : 1600, -28, 58, 58);
            Place(Find("CloseTassel"), _closeOnLeft ? 2 : 1640, 25, 24, 65);
        }
        // Only these HTML controls have CSS flat backgrounds; preserve the existing production callbacks.
        private static void PlainControl(UiTextButton button,string key)
        {
            button.Plate.sprite=QdaoUguiTheme.RequireSprite("UI/Ugui/SkillsExact20261022/"+key);
            button.Plate.type=Image.Type.Sliced;button.Plate.pixelsPerUnitMultiplier=1;button.Plate.color=Color.white;
            var colors=button.Button.colors;colors.selectedColor=Color.white;button.Button.colors=colors;
        }
        private RectTransform Find(string name)
            => (_root.Find(name) ?? _root.Find(name + "Plate") ??
                _studyRoot.Find(name) ?? _studyRoot.Find(name + "Plate")) as RectTransform;
        private static void Place(RectTransform rect,float x,float y,float width,float height)
        {if(rect==null)return;rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);}
        private static void Font(TMP_Text text,float size,Color? color=null,bool body=false)
        {if(text==null)return;text.fontSize=size;text.fontStyle=FontStyles.Normal;if(color.HasValue)text.color=color.Value;QdaoUguiTheme.ApplyTypographyAuto(text);}
        private static void Button(UiTextButton button,float x,float y,float width,float height,float size)
        {Place(button.Rect,x,y,width,height);Place(button.Label.rectTransform,8,0,width-16,height);Font(button.Label,size);button.Plate.pixelsPerUnitMultiplier=button.Plate.sprite==null?1:button.Plate.sprite.rect.height/height;}
        private TMP_Text Label(string name,float x,float y,float width,float height,string value,float size,Color color,UnityEngine.Transform parent=null,bool body=false)
        {
            parent ??= name == "SkillsTopline" ? _root : _studyRoot;
            var text=parent.Find(name)?.GetComponent<TMP_Text>();if(text==null)text=QdaoUguiFactory.CreateText(name,parent,x,y,width,height,value,size,color);
            Place(text.rectTransform,x,y,width,height);Font(text,size,color,body);text.text=value;text.richText=false;text.raycastTarget=false;return text;
        }
        private TMP_Text Label(string name,float x,float y,float width,float height,string value,float size,Color color,bool body)
            => Label(name,x,y,width,height,value,size,color,null,body);
        private void Rule(string name,float x,float y,float width,float height)
        {
            if(Find(name)!=null)return;
            var parent = name == "SkillsTopRule" || name == "SkillsFooterRule" ? _root : _studyRoot;
            var rule=QdaoUguiFactory.CreateImage(name,parent,x,y,width,height,null);rule.color=SkillsRule;rule.raycastTarget=false;
        }
    }
}
