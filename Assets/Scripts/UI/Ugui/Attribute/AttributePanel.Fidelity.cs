using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace MmorpgClient.UI.Ugui.Attribute
{
    public sealed partial class AttributePanel
    {
        private TMP_Text _identityName, _identityLevel;

        // Account metadata can arrive independently of an attribute response. Refresh just these
        // labels while visible; no attribute rebuild, requests, or pending allocation mutations.
        internal void RefreshIdentity()
        {
            if (_identityName == null || _identityLevel == null) return;
            string name = _owner?.RoleName;
            name = string.IsNullOrWhiteSpace(name) ? "道友" : name.Trim();
            if (_identityName.text != name) _identityName.text = name;
            string profession = (_owner?.RoleClassId ?? 0) switch
            {
                1 => "破军", 2 => "玄霄", 3 => "丹心", 4 => "逐风", _ => string.Empty
            };
            string level = _panel != null && _panel.Level > 0 ? $"{_panel.Level}级" : "等级 —";
            string detail = string.IsNullOrEmpty(profession) ? level : profession + " · " + level;
            if (_identityLevel.text != detail) _identityLevel.text = detail;
        }

        private void ApplyFidelityLayout()
        {
            AttributeFidelityLayout.Header(_root,"人物属性");
            AttributeFidelityLayout.ObjectTabs(_root,false);
            // Name/class come from the active account role; AttributePanelInfo remains the level source.
            _identityName=AttributeFidelityLayout.Text(_root,"ExactIdentityName",81.6f,145.6f,330,67.2f,"道友",44.8f,AttributeFidelityLayout.Ink);
            _identityName.richText=false;_identityName.enableAutoSizing=true;
            _identityName.fontSizeMin=25.6f;_identityName.fontSizeMax=44.8f;
            _identityName.overflowMode=TextOverflowModes.Ellipsis;
            var level=_panel!=null&&_panel.Level>0?$"{_panel.Level}级":"等级 —";
            _identityLevel=AttributeFidelityLayout.Text(_root,"ExactIdentityLevel",417.6f,145.6f,238.4f,67.2f,level,20.8f,AttributeFidelityLayout.Muted,true);
            _identityLevel.richText=false;_identityLevel.alignment=TextAlignmentOptions.MidlineRight;
            RefreshIdentity();
            AttributeFidelityLayout.Rule(_root,"ExactIdentityRule",81.6f,224,574.4f);
            AttributeFidelityLayout.Text(_root,"V10OverviewHeading",81.6f,254.4f,300,43.2f,"属性总览",33.6f,AttributeFidelityLayout.Ink).fontStyle=FontStyles.Bold;
            var summary=AttributeFidelityLayout.Text(_root,"ExactOverviewLevel",400,254.4f,256,43.2f,level,20.8f,AttributeFidelityLayout.Muted,true);summary.alignment=TextAlignmentOptions.MidlineRight;
            for(int i=0;i<6;i++)
            {
                float y=318.4f+i*68.8f;
                AttributeFidelityLayout.Place(_root,$"Stat{i}Label",86.4f,y,104,56);
                AttributeFidelityLayout.Place(_root,$"Stat{i}ValuePlate",185.6f,y,470.4f,56);
                AttributeFidelityLayout.Place(_statValues[i].rectTransform,211.2f,y,419.2f,56);
                AttributeFidelityLayout.Font(_statValues[i],35.2f,AttributeFidelityLayout.Ink);
                AttributeFidelityLayout.Font(AttributeFidelityLayout.Find(_root,$"Stat{i}Label")?.GetComponent<TMP_Text>(),32,AttributeFidelityLayout.Muted);
            }
            AttributeFidelityLayout.Place(_root,"ColumnDivider",704,134.4f,20.8f,680);
            var divider=AttributeFidelityLayout.Find(_root,"ColumnDivider")?.GetComponent<Image>();if(divider!=null)divider.preserveAspect=false;
            AttributeFidelityLayout.Button(_schemeButton,1928,134.4f,268.8f,75.2f,32);
            AttributeFidelityLayout.Place(_schemeDropdown,1928,211.2f,268.8f,_schemeOptions.Count*75.2f);
            AttributeFidelityLayout.Place(_root,"SchemeArrow",232,28,25.6f,19.2f);
            for(int i=0;i<_schemeOptions.Count;i++)AttributeFidelityLayout.Button(_schemeOptions[i],0,i*75.2f,268.8f,72,29);
            AttributeFidelityLayout.Text(_root,"ExactElementLabel",81.6f,736,250,32,"五行属性",20.8f,AttributeFidelityLayout.Muted,true);
            AttributeFidelityLayout.Text(_root,"ExactElementValue",560,736,96,32,"—",20.8f,AttributeFidelityLayout.Value,true).alignment=TextAlignmentOptions.MidlineRight;
            AttributeFidelityLayout.Text(_root,"ExactDirectionLabel",81.6f,785.6f,250,32,"修行方向",20.8f,AttributeFidelityLayout.Muted,true);
            AttributeFidelityLayout.Text(_root,"ExactDirectionValue",560,785.6f,96,32,"—",20.8f,AttributeFidelityLayout.Value,true).alignment=TextAlignmentOptions.MidlineRight;
            AttributeFidelityLayout.Text(_root,"ExactSchemeLabel",1784,137.6f,128,75.2f,"加点方案",20.8f,AttributeFidelityLayout.Muted,true);
            AttributeFidelityLayout.Place(_allocationRoot,0,0,AttributeFidelityLayout.Width,AttributeFidelityLayout.Height);
            AttributeFidelityLayout.Button(_tabs[0],784,134.4f,328,78.4f,38.4f);
            AttributeFidelityLayout.Place(_root,"RemainNotice",784,258.4f,44.8f,44.8f);
            AttributeFidelityLayout.Place(_remainText.rectTransform,846.4f,246.4f,1134.4f,68.8f);AttributeFidelityLayout.Font(_remainText,33.6f,AttributeFidelityLayout.Ink);
            AttributeFidelityLayout.Summary(_root,_remainText,784,246.4f,1412.8f,
                ()=>{var pool=FindPool(_activePoolId);return pool!=null&&pool.Unlocked
                    ?$"剩余{(string.IsNullOrEmpty(pool.Name)?"属性点":pool.Name)} <size=46.4><color=#267759>{RemainingAfterPending()}</color></size>"
                    :_remainText.text;},()=>PendingDelta(),()=>FindPool(_activePoolId)?.Unlocked??false);
            AttributeFidelityLayout.Place(_root,"PointsViewport",784,329.6f,1412.8f,337.6f);
            int active=0;for(int i=0;i<_rows.Length;i++){AttributeFidelityLayout.Point(_rows[i],0,i*86.4f,1412.8f);if(_rows[i].Rect.gameObject.activeSelf)active++;}
            _rowsRoot.sizeDelta=new Vector2(1412.8f,Mathf.Max(337.6f,active*86.4f-8));
            AttributeFidelityLayout.Place(_statusText.rectTransform,784,676.8f,1412.8f,38.4f);AttributeFidelityLayout.Font(_statusText,19.2f,null,true);
            if(string.IsNullOrEmpty(_statusText.text))_statusText.text="分配后点击确认，才会保存本次加点";
            AttributeFidelityLayout.Button(_resetButton,784,731.2f,376.9f,84.8f,38.4f);
            AttributeFidelityLayout.Button(_autoButton,1189.7f,731.2f,414.6f,84.8f,38.4f);
            AttributeFidelityLayout.Button(_confirmButton,1633.1f,731.2f,563.7f,84.8f,38.4f);
            var menu=AttributeFidelityLayout.MoreMenu(_root,432);
            var navigation=new[]{_attributeNavigation,_allocationNavigation,_combatNavigation};
            for(int i=0;i<navigation.Length;i++)
            {bool selected=i==0?_page==PanelPage.Overview:i==1?_page==PanelPage.Allocation:_page==PanelPage.Combat;AttributeFidelityLayout.MenuAction(menu,navigation[i],i,selected);}
            AttributeFidelityLayout.MenuAction(_root,menu,"SkillTab",3);
            AttributeFidelityLayout.MenuAction(_root,menu,"EquipmentEntry",4);
            AttributeFidelityLayout.MenuAction(menu,_createSchemeButton,5);
            AttributeFidelityLayout.Footer(_root,"一念入道，五行随心",()=>SetStatus("拖动滑杆或使用加减号分配；确认后提交服务器。重置优先撤销本次未确认的加点。"));
            AttributeFidelityLayout.Place(_overviewRoot,784,160,1412.8f,570);AttributeFidelityLayout.Place(_combatRoot,784,160,1412.8f,570);
        }
    }
}
