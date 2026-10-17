using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace MmorpgClient.UI.Ugui.Attribute
{
    public sealed partial class AttributePanel
    {
        private void ApplyFidelityLayout()
        {
            AttributeFidelityLayout.Header(_root,"人物属性");
            if(AttributeFidelityLayout.Find(_root,"V10OverviewHeading")==null)
                QdaoUguiFactory.CreateText("V10OverviewHeading",_root,81.6f,145,574.4f,62,"属性总览",44.8f,AttributeFidelityLayout.Ink);
            for(int i=0;i<6;i++)
            {
                float y=254.4f+i*68.8f;
                AttributeFidelityLayout.Place(_root,$"Stat{i}Label",86.4f,y,104,56);
                AttributeFidelityLayout.Place(_root,$"Stat{i}ValuePlate",185.6f,y,470.4f,56);
                AttributeFidelityLayout.Place(_statValues[i].rectTransform,211.2f,y,419.2f,56);
                AttributeFidelityLayout.Font(_statValues[i],35.2f,AttributeFidelityLayout.Ink);
                AttributeFidelityLayout.Font(AttributeFidelityLayout.Find(_root,$"Stat{i}Label")?.GetComponent<TMP_Text>(),32,AttributeFidelityLayout.Muted);
            }
            AttributeFidelityLayout.Place(_root,"ColumnDivider",704,134.4f,20.8f,680);
            AttributeFidelityLayout.Button(_schemeButton,1928,134.4f,268.8f,75.2f,32);
            AttributeFidelityLayout.Place(_schemeDropdown,1928,211.2f,268.8f,_schemeOptions.Count*75.2f);
            AttributeFidelityLayout.Place(_root,"SchemeArrow",232,28,25.6f,19.2f);
            for(int i=0;i<_schemeOptions.Count;i++)AttributeFidelityLayout.Button(_schemeOptions[i],0,i*75.2f,268.8f,72,29);
            AttributeFidelityLayout.Button(_createSchemeButton,81.6f,755.2f,574.4f,84.8f,29);
            AttributeFidelityLayout.Place(_allocationRoot,0,0,AttributeFidelityLayout.Width,AttributeFidelityLayout.Height);
            AttributeFidelityLayout.Button(_tabs[0],784,134.4f,328,78.4f,38.4f);
            AttributeFidelityLayout.Place(_root,"RemainNotice",784,246.4f,44.8f,44.8f);
            AttributeFidelityLayout.Place(_remainText.rectTransform,846.4f,238.4f,1350.4f,68.8f);AttributeFidelityLayout.Font(_remainText,33.6f,AttributeFidelityLayout.Value);
            AttributeFidelityLayout.Place(_root,"PointsViewport",784,321.6f,1412.8f,384);
            int active=0;for(int i=0;i<_rows.Length;i++){AttributeFidelityLayout.Point(_rows[i],0,i*86.4f,1412.8f);if(_rows[i].Rect.gameObject.activeSelf)active++;}
            _rowsRoot.sizeDelta=new Vector2(1412.8f,Mathf.Max(384,active*86.4f-8));
            AttributeFidelityLayout.Place(_statusText.rectTransform,784,710.4f,1412.8f,38.4f);AttributeFidelityLayout.Font(_statusText,21,null,true);
            AttributeFidelityLayout.Button(_resetButton,784,755.2f,350,84.8f,38.4f);
            AttributeFidelityLayout.Button(_autoButton,1163,755.2f,385,84.8f,38.4f);
            AttributeFidelityLayout.Button(_confirmButton,1577,755.2f,619.8f,84.8f,38.4f);
            var navigation=new[]{_attributeNavigation,_allocationNavigation,_combatNavigation};
            for(int i=0;i<navigation.Length;i++)
            {AttributeFidelityLayout.Button(navigation[i],1560+i*155,46.4f,145,68.8f,29);if(navigation[i]!=null){navigation[i].Label.text=navigation[i].Label.text.Replace("\n","");bool selected=i==0?_page==PanelPage.Overview:i==1?_page==PanelPage.Allocation:_page==PanelPage.Combat;CharacterAttributeVisuals.Skin(navigation[i].Plate,selected?"button_primary":"button_secondary");navigation[i].Label.color=selected?QdaoRefreshArt.Ivory:AttributeFidelityLayout.Ink;}}
            var skill=AttributeFidelityLayout.Find(_root,"SkillTab");if(skill!=null){AttributeFidelityLayout.Place(skill,2012,46.4f,145,68.8f);var t=skill.GetComponentInChildren<TMP_Text>();t.text="技能";AttributeFidelityLayout.Font(t,29);CharacterAttributeVisuals.Skin(skill.GetComponent<Image>(),"button_secondary");}
            AttributeFidelityLayout.Place(_root,"EquipmentEntry",81.6f,853,150,45);
            AttributeFidelityLayout.Place(_overviewRoot,784,160,1412.8f,570);AttributeFidelityLayout.Place(_combatRoot,784,160,1412.8f,570);
        }
    }
}
