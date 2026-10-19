using MmorpgClient.UI.Ugui.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Attribute
{
    // Local v10-preview geometry helpers. Existing global art/style helpers remain untouched.
    internal static class AttributeFidelityLayout
    {
        internal static readonly Color Ink=QdaoUguiTheme.Html("#344638"),Muted=QdaoUguiTheme.Html("#7B6D53"),Value=QdaoUguiTheme.Html("#2F7155");
        internal const float Width=2278.4f,Height=912f;
        internal static void Place(RectTransform rect,float x,float y,float w,float h)
        { if(rect==null)return;rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h); }
        internal static RectTransform Find(UnityEngine.Transform root,string name)
        {
            var rects=root.GetComponentsInChildren<RectTransform>(true);
            foreach(var rect in rects)if(rect.name==name)return rect;
            // BattleUiWidgets.CreateTextButton uses the plate itself as the root rect.
            foreach(var rect in rects)if(rect.name==name+"Plate")return rect;
            return null;
        }
        internal static void Place(UnityEngine.Transform root,string name,float x,float y,float w,float h)=>Place(Find(root,name),x,y,w,h);
        internal static void Font(TMP_Text text,float size,Color? color=null,bool body=false)
        {
            if(text==null)return;text.font=body?QdaoUguiTypography.ResolveBodyFont():QdaoUguiTheme.ResolveFont();
            text.fontSize=size;text.fontStyle=FontStyles.Normal;if(color.HasValue)text.color=color.Value;
        }
        internal static void Button(UiTextButton button,float x,float y,float w,float h,float size)
        {
            if(button==null)return;Place(button.Rect,x,y,w,h);Place(button.Label.rectTransform,8,0,w-16,h);Font(button.Label,size);
            button.Plate.pixelsPerUnitMultiplier=button.Plate.sprite==null?1:button.Plate.sprite.rect.height/h;
        }
        internal static void Point(UiPointRow row,float x,float y,float width)
        {
            if(row==null)return;const float h=78.4f,step=70.4f;
            Place(row.Rect,x,y,width,h);row.Rect.localScale=UnityEngine.Vector3.one;
            Place(row.NameLabel.rectTransform,0,0,110,h);Font(row.NameLabel,36.8f,Ink);
            Place(row.ValueLabel.rectTransform,110,0,136,h);Font(row.ValueLabel,35.2f,Value);
            row.ValueLabel.text=row.ValueLabel.text.Replace("#7FD98A","#28734E");
            Place((RectTransform)row.Minus.transform,260,4,step,step);Place((RectTransform)row.Plus.transform,width-step,4,step,step);
            float sx=356,sw=width-sx-step-24;
            Place((RectTransform)row.Slider.transform,sx,4,sw,step);
            var track=row.Slider.transform.GetChild(0) as RectTransform;
            if(track!=null)Place(track,0,24,sw,20.8f);
            Place((RectTransform)row.Slider.fillRect.parent,0,24,sw,20.8f);
            Place((RectTransform)row.Slider.handleRect.parent,30f,0,sw-60f,step);
            row.Slider.handleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,60f);
            row.Slider.handleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,60f);
        }
        // v10-preview panel.css: entity-tabs right:88px/top:29px, two 103x43px tabs and 10px gap.
        // Native attribute coordinates are exactly 1.6 times its 1424x570 CSS panel.
        internal static void ObjectTabs(RectTransform root, bool pet)
        {
            string[] labels = { "人物", "宝宝" };
            for (int i = 0; i < labels.Length; ++i)
            {
                string name = "ExactObjectTab_" + i;
                if (Find(root,name) != null) continue;
                int mode = i; bool selected = (i == 1) == pet;
                var button = CharacterAttributeVisuals.Button(name,root,1792 + i * 180.8f,46.4f,164.8f,68.8f,labels[i],selected,33.6f);
                CharacterAttributeVisuals.Skin(button.Plate,selected ? "tab_horizontal" : "button_secondary");
                button.Label.color = selected ? QdaoRefreshArt.Ivory : Ink;
                button.Button.interactable = selected || (i == 0 ? AttributeUiRoot.Instance != null : Pet.PetUiRoot.Instance != null);
                button.Button.onClick.AddListener(() =>
                {
                    if (mode == 0) AttributeUiRoot.Instance?.OpenPanel(true);
                    else Pet.PetUiRoot.Instance?.OpenPanel(true);
                });
                if (selected) QdaoUguiFactory.CreateText(name + "Selected",button.Rect,130,0,28,68.8f,"✓",19.2f,QdaoRefreshArt.Ivory,TextAlignmentOptions.Center);
            }
        }

        internal static void Header(RectTransform root,string title)
        {
            Place(root,140.8f,89.6f,Width,Height);
            Place(root,"TitlePlate",753.6f,-52.8f,771.2f,131.2f);
            Place(root,"Title",908.8f,-35.2f,460.8f,99.2f);
            Place(root,"Close",2169.6f,-32,88,88);Place(root,"ClosePlate",2169.6f,-32,88,88);
            Place(root,"CloseTassel",2222.4f,51.2f,38.4f,104);
            if(Find(root,"V10GameBrand")==null)QdaoUguiFactory.CreateText("V10GameBrand",root,81.6f,48,440,48,"五行奇谈",35.2f,QdaoUguiTheme.Html("#826F47"));
        }
    }
}



