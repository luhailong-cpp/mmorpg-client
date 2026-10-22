using MmorpgClient.UI.Ugui.Attribute;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace MmorpgClient.UI.Ugui.Pet
{
    public sealed partial class PetPanel
    {
        private RectTransform _fidelityRows;
        private ScrollRect _fidelityScroll;
        private static class FidelityPetGeometry
        {
            public const float WindowW=2278.4f,WindowH=912,WindowX=140.8f,WindowY=89.6f,LeftX=81.6f,LeftW=400,
                MidX=524.8f,MidW=497.6f,RightX=1108.8f,RightW=1088,PetItemHeight=123.2f,PetItemGap=14.4f,RosterHeight=536;
        }
        private void ApplyFidelityLayout()
        {
            AttributeFidelityLayout.Header(_root,"宝宝属性");
            AttributeFidelityLayout.ObjectTabs(_root,true);
            AttributeFidelityLayout.Text(_root,"ExactRosterHeading",81.6f,134.4f,250,67.2f,"我的宝宝",36.8f,AttributeFidelityLayout.Ink).fontStyle=FontStyles.Bold;
            AttributeFidelityLayout.Place(_carryText.rectTransform,320,134.4f,161.6f,67.2f);AttributeFidelityLayout.Font(_carryText,19.2f,AttributeFidelityLayout.Muted,true);
            _carryText.alignment=TextAlignmentOptions.MidlineRight;
            _carryText.text=_list==null?"携带 — / —":$"携带 {_list.Pets.Count} / {_list.MaxPets}";
            AttributeFidelityLayout.Place(_root,"PetRosterViewport",81.6f,222.4f,400,536);
            AttributeFidelityLayout.Place(_root,"RosterDivider",1065.6f,134.4f,20.8f,692);
            var divider=AttributeFidelityLayout.Find(_root,"RosterDivider")?.GetComponent<Image>();if(divider!=null)divider.preserveAspect=false;
            for(int i=0;i<_petItems.Count;i++)
            {
                var item=_petItems[i];AttributeFidelityLayout.Button(item,0,i*137.6f,400,123.2f,36.8f);
                AttributeFidelityLayout.Place(item.Rect,"PortraitFrame",14.4f,12.8f,88,88);
                AttributeFidelityLayout.Place(_petPortraits[i].rectTransform,19.2f,17.6f,78.4f,78.4f);
                AttributeFidelityLayout.Place(_petLevels[i].rectTransform,62,81,36,28);AttributeFidelityLayout.Font(_petLevels[i],16, QdaoRefreshArt.Ivory);
                AttributeFidelityLayout.Place(item.Label.rectTransform,116.8f,13,208,58);AttributeFidelityLayout.Font(item.Label,36.8f);
                AttributeFidelityLayout.Place(_petDescriptions[i].rectTransform,116.8f,73,208,34);AttributeFidelityLayout.Font(_petDescriptions[i],17.6f);
                AttributeFidelityLayout.Place(_petStates[i].rectTransform,334,30,46.4f,64);_petStates[i].rectTransform.localEulerAngles=UnityEngine.Vector3.zero;AttributeFidelityLayout.Font(_petStates[i],17.6f);
                _petStates[i].text=_petStates[i].text.Replace("参战","参\n战").Replace("休息","休\n息");
            }
            for(int i=0;i<6;i++)
            {
                float y=355.2f+i*57.6f;
                AttributeFidelityLayout.Place(_root,$"Stat{i}Label",529.6f,y,96,49.6f);
                AttributeFidelityLayout.Place(_root,$"Stat{i}Plate",620.8f,y,401.6f,49.6f);
                AttributeFidelityLayout.Place(_statValues[i].rectTransform,638.4f,y,366.4f,49.6f);AttributeFidelityLayout.Font(_statValues[i],32,AttributeFidelityLayout.Ink);
                AttributeFidelityLayout.Font(AttributeFidelityLayout.Find(_root,$"Stat{i}Label")?.GetComponent<TMP_Text>(),28.8f,AttributeFidelityLayout.Muted);
            }
            var selectedPet=FindPet(_selectedPetId);
            AttributeFidelityLayout.Text(_root,"ExactPetOverview",524.8f,134.4f,300,43.2f,"属性总览",33.6f,AttributeFidelityLayout.Ink).fontStyle=FontStyles.Bold;
            var overviewLevel=AttributeFidelityLayout.Text(_root,"ExactPetOverviewLevel",825,134.4f,197.4f,43.2f,selectedPet==null?"等级 —":$"{selectedPet.Level}级",20.8f,AttributeFidelityLayout.Muted,true);overviewLevel.alignment=TextAlignmentOptions.MidlineRight;
            var portraitRect=AttributeFidelityLayout.Find(_root,"V10SelectedPetPortrait");
            if(portraitRect==null)portraitRect=QdaoUguiFactory.CreateImage("V10SelectedPetPortrait",_root,524.8f,204,113.6f,113.6f,null).rectTransform;
            AttributeFidelityLayout.Place(portraitRect,524.8f,204,113.6f,113.6f);
            var portraitImage=portraitRect.GetComponent<Image>();portraitImage.sprite=selectedPet==null?null:PortraitAsset(selectedPet);portraitImage.enabled=portraitImage.sprite!=null;portraitImage.preserveAspect=true;
            var selectedName=AttributeFidelityLayout.Find(_root,"V10SelectedPetName")?.GetComponent<TMP_Text>();
            if(selectedName==null)selectedName=QdaoUguiFactory.CreateText("V10SelectedPetName",_root,659.2f,194,363.2f,57.6f,"",38.4f,AttributeFidelityLayout.Ink);
            AttributeFidelityLayout.Place(selectedName.rectTransform,659.2f,194,363.2f,57.6f);
            selectedName.text=selectedPet?.Name??"未选择宝宝";AttributeFidelityLayout.Font(selectedName,38.4f,AttributeFidelityLayout.Ink);
            AttributeFidelityLayout.Place(_levelValue.rectTransform,659.2f,258,363.2f,30);AttributeFidelityLayout.Font(_levelValue,19.2f,AttributeFidelityLayout.Value);
            AttributeFidelityLayout.Place(_growthValue.rectTransform,659.2f,298,363.2f,30);AttributeFidelityLayout.Font(_growthValue,19.2f,AttributeFidelityLayout.Muted);
            AttributeFidelityLayout.Rule(_root,"ExactPetDetailRule",524.8f,331.2f,497.6f);
            AttributeFidelityLayout.Place(_root,"PointsHeading",1108.8f,134.4f,240,70.4f);AttributeFidelityLayout.Place(_root,"PointsTitle",1108.8f,134.4f,240,70.4f);
            var pointsPlate=AttributeFidelityLayout.Find(_root,"PointsHeading")?.GetComponent<Image>();if(pointsPlate!=null)SkinPainted(pointsPlate,"tab_horizontal");
            var pointsTitle=AttributeFidelityLayout.Find(_root,"PointsTitle")?.GetComponent<TMP_Text>();if(pointsTitle!=null){pointsTitle.text="属性点";AttributeFidelityLayout.Font(pointsTitle,35.2f,QdaoRefreshArt.Ivory);}
            if(AttributeFidelityLayout.Find(_root,"ExactPetNotice")==null)CreatePaintedPanel("ExactPetNotice",_root,1108.8f,258.4f,44.8f,44.8f,"notice_icon");
            AttributeFidelityLayout.Place(_remainText.rectTransform,1171.2f,246.4f,1025.6f,68.8f);AttributeFidelityLayout.Font(_remainText,33.6f,AttributeFidelityLayout.Ink);
            AttributeFidelityLayout.Summary(_root,_remainText,1108.8f,246.4f,1088,
                ()=>FindPet(_selectedPetId)==null?"暂无可分配属性点":$"剩余属性点 <size=46.4><color=#267759>{RemainingAfterPending()}</color></size>",
                ()=>PendingDelta(),()=>FindPet(_selectedPetId)!=null);
            AttributeFidelityLayout.Text(_root,"ExactPetSchemeLabel",1913.6f,140.8f,80,68.8f,"加点方案",17.6f,AttributeFidelityLayout.Muted,true);
            if(AttributeFidelityLayout.Find(_root,"ExactPetScheme")==null){var scheme=CreateAction("ExactPetScheme",_root,1993.6f,140.8f,203.2f,68.8f,"—",false);SkinPainted(scheme.Plate,"button_scheme");scheme.SetInteractable(false);}
            if(_fidelityRows==null)
            {
                int sibling=_rows[0].Rect.GetSiblingIndex();var viewport=QdaoUguiFactory.CreateRect("V10PetPointsViewport",_root,1108.8f,329.6f,1088,337.6f);
                viewport.SetSiblingIndex(sibling);viewport.gameObject.AddComponent<RectMask2D>();var hit=viewport.gameObject.AddComponent<Image>();hit.color=new Color(1,1,1,.001f);
                _fidelityRows=QdaoUguiFactory.CreateRect("V10PetPointsRows",viewport,0,0,1088,337.6f);
                _fidelityScroll=viewport.gameObject.AddComponent<ScrollRect>();_fidelityScroll.viewport=viewport;_fidelityScroll.content=_fidelityRows;
                _fidelityScroll.horizontal=false;_fidelityScroll.movementType=ScrollRect.MovementType.Clamped;
                foreach(var row in _rows){row.Rect.SetParent(_fidelityRows,false);CharacterAttributeScrollFocus.Bind(row.Minus,_fidelityScroll,row.Rect);CharacterAttributeScrollFocus.Bind(row.Plus,_fidelityScroll,row.Rect);CharacterAttributeScrollFocus.Bind(row.Slider,_fidelityScroll,row.Rect);}
            }
            int active=0;for(int i=0;i<_rows.Length;i++){AttributeFidelityLayout.Point(_rows[i],0,i*86.4f,1088,true);if(_rows[i].Rect.gameObject.activeSelf)active++;}
            _fidelityRows.sizeDelta=new Vector2(1088,Mathf.Max(337.6f,active*86.4f-8));
            AttributeFidelityLayout.Button(_discardButton,1108.8f,741.6f,294.2f,84.8f,32);
            AttributeFidelityLayout.Button(_autoButton,1417.4f,741.6f,323.7f,84.8f,32);
            AttributeFidelityLayout.Button(_confirmButton,1755.5f,741.6f,441.3f,84.8f,32);
            AttributeFidelityLayout.Button(_resetButton,620.8f,766.4f,264,60.8f,27.2f);AttributeFidelityLayout.Button(_summonButton,900.8f,766.4f,121.6f,60.8f,27.2f);
            _resetButton.Label.enableAutoSizing=true;_resetButton.Label.fontSizeMin=18;_resetButton.Label.fontSizeMax=27.2f;
            AttributeFidelityLayout.Text(_root,"ExactPetDirectionLabel",524.8f,710.4f,240,32,"培养方向",20.8f,AttributeFidelityLayout.Muted,true);
            AttributeFidelityLayout.Text(_root,"ExactPetDirectionValue",926.4f,710.4f,96,32,"—",20.8f,AttributeFidelityLayout.Value,true).alignment=TextAlignmentOptions.MidlineRight;
            AttributeFidelityLayout.Text(_root,"ExactRosterFoot",81.6f,774.4f,260,32,"与灵同行，山海不远",19.2f,AttributeFidelityLayout.Muted);
            AttributeFidelityLayout.Text(_root,"ExactRosterCount",350,774.4f,131.6f,32,_list==null?"— 只灵宠":$"{_list.Pets.Count} 只灵宠",19.2f,AttributeFidelityLayout.Muted).alignment=TextAlignmentOptions.MidlineRight;
            AttributeFidelityLayout.Place(_statusText.rectTransform,1108.8f,676.8f,1088,38.4f);AttributeFidelityLayout.Font(_statusText,19.2f,null,true);
            if(string.IsNullOrEmpty(_statusText.text))_statusText.text="分配后点击确认，才会保存本次加点";
            var hidden=AttributeFidelityLayout.Find(_root,"TaijiWatermark");if(hidden!=null)hidden.gameObject.SetActive(false);
            var menu=AttributeFidelityLayout.MoreMenu(_root,496);
            string[] tabs={"属性","加点","技能","坐骑","图鉴"};for(int i=0;i<tabs.Length;i++)
            {
                var rect=AttributeFidelityLayout.Find(_root,"Tab"+tabs[i]+"Plate");if(rect==null)continue;
                AttributeFidelityLayout.MenuAction(_root,menu,"Tab"+tabs[i]+"Plate",i);
            }
            AttributeFidelityLayout.MenuAction(_root,menu,"CultivationHelp",5);
            AttributeFidelityLayout.MenuAction(_root,menu,"Resistance",6);
            AttributeFidelityLayout.Footer(_root,"山海相伴，灵契同心",()=>SetStatus("拖动滑杆或使用加减号分配；确认后提交服务器。重置撤销未确认加点，洗点规则以服务器为准。"));
        }
    }
}

