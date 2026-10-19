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
            AttributeFidelityLayout.Place(_carryText.rectTransform,81.6f,134.4f,400,67.2f);AttributeFidelityLayout.Font(_carryText,27.2f,AttributeFidelityLayout.Muted);
            AttributeFidelityLayout.Place(_root,"PetRosterViewport",81.6f,222.4f,400,536);
            AttributeFidelityLayout.Place(_root,"RosterDivider",484.8f,134.4f,16,680);
            for(int i=0;i<_petItems.Count;i++)
            {
                var item=_petItems[i];AttributeFidelityLayout.Button(item,0,i*137.6f,400,123.2f,36.8f);
                AttributeFidelityLayout.Place(item.Rect,"PortraitFrame",14.4f,12.8f,88,88);
                AttributeFidelityLayout.Place(_petPortraits[i].rectTransform,19.2f,17.6f,78.4f,78.4f);
                AttributeFidelityLayout.Place(_petLevels[i].rectTransform,62,81,36,28);AttributeFidelityLayout.Font(_petLevels[i],16, QdaoRefreshArt.Ivory);
                AttributeFidelityLayout.Place(item.Label.rectTransform,116.8f,13,208,58);AttributeFidelityLayout.Font(item.Label,36.8f);
                AttributeFidelityLayout.Place(_petDescriptions[i].rectTransform,116.8f,73,208,34);AttributeFidelityLayout.Font(_petDescriptions[i],17.6f);
                AttributeFidelityLayout.Place(_petStates[i].rectTransform,334,30,46.4f,64);_petStates[i].rectTransform.localEulerAngles=UnityEngine.Vector3.zero;AttributeFidelityLayout.Font(_petStates[i],17.6f);
            }
            for(int i=0;i<6;i++)
            {
                float y=344+i*57.6f;
                AttributeFidelityLayout.Place(_root,$"Stat{i}Label",529.6f,y,96,49.6f);
                AttributeFidelityLayout.Place(_root,$"Stat{i}Plate",620.8f,y,401.6f,49.6f);
                AttributeFidelityLayout.Place(_statValues[i].rectTransform,638.4f,y,366.4f,49.6f);AttributeFidelityLayout.Font(_statValues[i],32,AttributeFidelityLayout.Ink);
                AttributeFidelityLayout.Font(AttributeFidelityLayout.Find(_root,$"Stat{i}Label")?.GetComponent<TMP_Text>(),28.8f,AttributeFidelityLayout.Muted);
            }
            var selectedPet=FindPet(_selectedPetId);
            var portraitRect=AttributeFidelityLayout.Find(_root,"V10SelectedPetPortrait");
            if(portraitRect==null)portraitRect=QdaoUguiFactory.CreateImage("V10SelectedPetPortrait",_root,524.8f,222,113.6f,113.6f,null).rectTransform;
            var portraitImage=portraitRect.GetComponent<Image>();portraitImage.sprite=selectedPet==null?null:PortraitAsset(selectedPet);portraitImage.enabled=portraitImage.sprite!=null;portraitImage.preserveAspect=true;
            var selectedName=AttributeFidelityLayout.Find(_root,"V10SelectedPetName")?.GetComponent<TMP_Text>();
            if(selectedName==null)selectedName=QdaoUguiFactory.CreateText("V10SelectedPetName",_root,659,222,363,51,"",38.4f,AttributeFidelityLayout.Ink);
            selectedName.text=selectedPet?.Name??"未选择宝宝";AttributeFidelityLayout.Font(selectedName,38.4f,AttributeFidelityLayout.Ink);
            AttributeFidelityLayout.Place(_levelValue.rectTransform,659,276,363,30);AttributeFidelityLayout.Font(_levelValue,19.2f,AttributeFidelityLayout.Muted);
            AttributeFidelityLayout.Place(_growthValue.rectTransform,659,307,363,30);AttributeFidelityLayout.Font(_growthValue,19.2f,AttributeFidelityLayout.Muted);
            AttributeFidelityLayout.Place(_root,"PointsHeading",1108.8f,134.4f,300,70.4f);AttributeFidelityLayout.Place(_root,"PointsTitle",1120,134.4f,275,70.4f);
            AttributeFidelityLayout.Place(_remainText.rectTransform,1108.8f,238.4f,1088,68.8f);AttributeFidelityLayout.Font(_remainText,33.6f,AttributeFidelityLayout.Value);
            if(_fidelityRows==null)
            {
                int sibling=_rows[0].Rect.GetSiblingIndex();var viewport=QdaoUguiFactory.CreateRect("V10PetPointsViewport",_root,1108.8f,321.6f,1088,384);
                viewport.SetSiblingIndex(sibling);viewport.gameObject.AddComponent<RectMask2D>();var hit=viewport.gameObject.AddComponent<Image>();hit.color=new Color(1,1,1,.001f);
                _fidelityRows=QdaoUguiFactory.CreateRect("V10PetPointsRows",viewport,0,0,1088,384);
                _fidelityScroll=viewport.gameObject.AddComponent<ScrollRect>();_fidelityScroll.viewport=viewport;_fidelityScroll.content=_fidelityRows;
                _fidelityScroll.horizontal=false;_fidelityScroll.movementType=ScrollRect.MovementType.Clamped;
                foreach(var row in _rows){row.Rect.SetParent(_fidelityRows,false);CharacterAttributeScrollFocus.Bind(row.Minus,_fidelityScroll,row.Rect);CharacterAttributeScrollFocus.Bind(row.Plus,_fidelityScroll,row.Rect);CharacterAttributeScrollFocus.Bind(row.Slider,_fidelityScroll,row.Rect);}
            }
            int active=0;for(int i=0;i<_rows.Length;i++){AttributeFidelityLayout.Point(_rows[i],0,i*86.4f,1088);if(_rows[i].Rect.gameObject.activeSelf)active++;}
            _fidelityRows.sizeDelta=new Vector2(1088,Mathf.Max(384,active*86.4f-8));
            AttributeFidelityLayout.Button(_discardButton,1108.8f,755.2f,260,84.8f,32);
            AttributeFidelityLayout.Button(_autoButton,1383,755.2f,300,84.8f,32);
            AttributeFidelityLayout.Button(_confirmButton,1697,755.2f,499.8f,84.8f,32);
            AttributeFidelityLayout.Button(_resetButton,524.8f,755.2f,230,60.8f,27.2f);AttributeFidelityLayout.Button(_summonButton,770.8f,755.2f,251.6f,60.8f,27.2f);
            AttributeFidelityLayout.Place(_root,"CultivationHelpPlate",81.6f,780,190,60.8f);
            AttributeFidelityLayout.Place(_statusText.rectTransform,1108.8f,710.4f,1088,38.4f);AttributeFidelityLayout.Font(_statusText,20,null,true);
            var hidden=AttributeFidelityLayout.Find(_root,"TaijiWatermark");if(hidden!=null)hidden.gameObject.SetActive(false);
            string[] tabs={"属性","加点","技能","坐骑","图鉴"};for(int i=0;i<tabs.Length;i++)
            {
                var rect=AttributeFidelityLayout.Find(_root,"Tab"+tabs[i]+"Plate");if(rect==null)continue;
                AttributeFidelityLayout.Place(rect,400+i*150,853,140,45);SkinPainted(rect.GetComponent<Image>(),i==1?"button_primary":"button_secondary");
                var label=rect.GetComponentInChildren<TMP_Text>();label.text=tabs[i];AttributeFidelityLayout.Place(label.rectTransform,0,0,140,45);AttributeFidelityLayout.Font(label,25);
            }
        }
    }
}

