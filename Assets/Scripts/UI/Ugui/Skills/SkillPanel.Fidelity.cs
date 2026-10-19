using MmorpgClient.UI.Ugui.Attribute;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace MmorpgClient.UI.Ugui.Skills
{
    public sealed partial class SkillPanel
    {
        private bool _fidelityCanPreviewClasses;
        private void ApplyFidelityLayout()
        {
            AttributeFidelityLayout.Header(_root,"四脉真诀");
            AttributeFidelityLayout.Place(_title.rectTransform,770,-35.2f,738,99.2f);AttributeFidelityLayout.Font(_title,54.4f);
            _title.text=_state?.Context?.IsPet==true?"宝宝技能":"四脉真诀";
            if(AttributeFidelityLayout.Find(_root,"V10ClassHeading")==null)
                QdaoUguiFactory.CreateText("V10ClassHeading",_root,81.6f,135,270,52,"四门传承",33.6f,AttributeFidelityLayout.Muted);
            AttributeFidelityLayout.Place(_classLabel.rectTransform,430,98,1200,54);AttributeFidelityLayout.Font(_classLabel,42,AttributeFidelityLayout.Ink);
            AttributeFidelityLayout.Place(_classInfo.rectTransform,81.6f,_state?.Context?.IsPet==true?210:700,270,100);AttributeFidelityLayout.Font(_classInfo,22,AttributeFidelityLayout.Muted,true);_classInfo.textWrappingMode=TextWrappingModes.Normal;
            bool petContext=_state?.Context?.IsPet==true;
            for(int i=0;i<_classes.Length;i++)
            {
                AttributeFidelityLayout.Button(_classes[i],81.6f,210+i*118,270,104,38);
                // Live classes are visible as a read-only catalog; preview intents retain the original callback/permission.
                _classes[i].SetVisible(!petContext);_classes[i].SetInteractable(!petContext&&_state?.IsReadOnly==true&&_fidelityCanPreviewClasses);
            }
            var classHeading=AttributeFidelityLayout.Find(_root,"V10ClassHeading")?.GetComponent<TMP_Text>();if(classHeading!=null)classHeading.text=petContext?"灵宠技能":"四门传承";
            int shown=0;foreach(var filter in _filters)if(filter.Rect.gameObject.activeSelf){AttributeFidelityLayout.Button(filter,430+shown++*224,185,212,60,27);filter.Label.text=filter.Label.text.Replace("✓ ","");}
            AttributeFidelityLayout.Place(_root,"NameHeader",540,249,500,42);AttributeFidelityLayout.Place(_root,"RankHeader",1080,249,155,42);
            AttributeFidelityLayout.Place(_root,"ManaHeader",1260,249,354,42);
            foreach(string name in new[]{"KindHeader","TargetHeader"}){var rect=AttributeFidelityLayout.Find(_root,name);if(rect!=null)rect.gameObject.SetActive(false);}
            AttributeFidelityLayout.Place(_scroll.viewport,430,294,1200,500);
            _content.sizeDelta=new Vector2(1200,Mathf.Max(500,_visible.Count*86-4));
            AttributeFidelityLayout.Place(_root,"SkillListScrollbar",1640,294,14,500);
            AttributeFidelityLayout.Place(_empty.rectTransform,30,120,1140,160);AttributeFidelityLayout.Font(_empty,29,AttributeFidelityLayout.Muted,true);
            foreach(var row in _rows)
            {
                row.Button.Rect.sizeDelta=new Vector2(1200,82);row.Button.Plate.sprite=null;row.Button.Plate.type=Image.Type.Simple;
                row.Button.Plate.color=QdaoUguiTheme.Html(row.Id==_selectedId?"#DFE9D3":"#FFF7E7");
                AttributeFidelityLayout.Place(row.Icon.rectTransform,8,4,74,74);
                AttributeFidelityLayout.Place(row.Name.rectTransform,110,2,520,43);AttributeFidelityLayout.Font(row.Name,33.6f,AttributeFidelityLayout.Ink);
                row.Name.text=row.Name.text.Replace("✓ ","");
                AttributeFidelityLayout.Place(row.Kind.rectTransform,110,46,520,30);AttributeFidelityLayout.Font(row.Kind,20,AttributeFidelityLayout.Muted,true);
                AttributeFidelityLayout.Place(row.Rank.rectTransform,650,2,155,78);AttributeFidelityLayout.Font(row.Rank,24,AttributeFidelityLayout.Ink,true);
                AttributeFidelityLayout.Place(row.Mana.rectTransform,830,2,355,40);AttributeFidelityLayout.Font(row.Mana,21,AttributeFidelityLayout.Ink,true);
                AttributeFidelityLayout.Place(row.Target.rectTransform,830,43,355,35);AttributeFidelityLayout.Font(row.Target,20,AttributeFidelityLayout.Muted,true);
            }
            AttributeFidelityLayout.Place(_selectedIcon.rectTransform,1784,140,288,288);
            AttributeFidelityLayout.Place(_selectedName.rectTransform,1690,445,480,60);AttributeFidelityLayout.Font(_selectedName,44.8f,AttributeFidelityLayout.Ink);_selectedName.alignment=TextAlignmentOptions.Center;
            AttributeFidelityLayout.Place(_descriptionScroll.viewport,1690,520,480,150);
            AttributeFidelityLayout.Font(_description,24,AttributeFidelityLayout.Ink,true);
            float height=Mathf.Max(150,_description.GetPreferredValues(_description.text,460,0).y+4);
            _description.rectTransform.sizeDelta=new Vector2(460,height);_descriptionContent.sizeDelta=new Vector2(470,height);
            AttributeFidelityLayout.Place(_root,"DescriptionScrollbar",2182,520,14,150);
            AttributeFidelityLayout.Place(_condition.rectTransform,1690,687,480,72);AttributeFidelityLayout.Font(_condition,22,AttributeFidelityLayout.Muted,true);
            AttributeFidelityLayout.Place(_balance.rectTransform,1690,765,480,33);AttributeFidelityLayout.Font(_balance,22,AttributeFidelityLayout.Ink,true);
            AttributeFidelityLayout.Place(_cost.rectTransform,1690,801,480,33);AttributeFidelityLayout.Font(_cost,22,AttributeFidelityLayout.Ink,true);
            AttributeFidelityLayout.Button(_upgrade,1690,841,220,60,30);AttributeFidelityLayout.Button(_upgradeTen,1925,841,245,60,30);
            AttributeFidelityLayout.Button(_refresh,1400,841,230,60,29);
            AttributeFidelityLayout.Place(_status.rectTransform,81.6f,841,1280,60);AttributeFidelityLayout.Font(_status,23,null,true);
            string[] tabs={"AttributeTab","AllocationTab","SkillTab"};for(int i=0;i<tabs.Length;i++)
            {
                var rect=AttributeFidelityLayout.Find(_root,tabs[i]);if(rect==null)continue;
                AttributeFidelityLayout.Place(rect,1725+i*155,46.4f,145,68.8f);CharacterAttributeVisuals.Skin(rect.GetComponent<Image>(),i==2?"button_primary":"button_secondary");
                var label=rect.GetComponentInChildren<TMP_Text>();label.text=label.text.Replace("\n","");AttributeFidelityLayout.Place(label.rectTransform,0,0,145,68.8f);AttributeFidelityLayout.Font(label,29,i==2?QdaoRefreshArt.Ivory:AttributeFidelityLayout.Ink);
            }
        }
    }
}
