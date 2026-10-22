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
        internal static TMP_Text Text(RectTransform root,string name,float x,float y,float w,float h,string value,float size,Color color,bool body=false)
        {
            var text=Find(root,name)?.GetComponent<TMP_Text>();
            if(text==null)text=QdaoUguiFactory.CreateText(name,root,x,y,w,h,value,size,color);
            Place(text.rectTransform,x,y,w,h);Font(text,size,color,body);text.text=value;return text;
        }
        internal static void Rule(RectTransform root,string name,float x,float y,float w)
        {
            if(Find(root,name)!=null)return;
            var image=QdaoUguiFactory.CreateImage(name,root,x,y,w,1.6f,null);image.color=QdaoUguiTheme.Html("#C5B88F");
        }
        internal static void Button(UiTextButton button,float x,float y,float w,float h,float size)
        {
            if(button==null)return;Place(button.Rect,x,y,w,h);Place(button.Label.rectTransform,8,0,w-16,h);Font(button.Label,size);
            button.Plate.pixelsPerUnitMultiplier=button.Plate.sprite==null?1:button.Plate.sprite.rect.height/h;
        }
        internal static void Point(UiPointRow row,float x,float y,float width,bool pet=false)
        {
            if(row==null)return;const float h=78.4f,step=70.4f;
            Place(row.Rect,x,y,width,h);row.Rect.localScale=UnityEngine.Vector3.one;
            float labelWidth=pet?150.4f:185.6f,minusX=pet?168:211.2f,sx=pet?256:307.2f,gap=pet?17.6f:25.6f;
            Place(row.NameLabel.rectTransform,0,0,pet?92:116,h);Font(row.NameLabel,pet?33.6f:38.4f,Ink);
            Place(row.ValueLabel.rectTransform,pet?92:116,0,labelWidth-(pet?92:116),h);Font(row.ValueLabel,pet?35.2f:38.4f,Value);
            row.ValueLabel.alignment=TextAlignmentOptions.MidlineRight;
            row.ValueLabel.fontSizeMax=pet?35.2f:38.4f;
            row.ValueLabel.text=row.ValueLabel.text.Replace("#7FD98A","#28734E");
            Place((RectTransform)row.Minus.transform,minusX,4,step,step);Place((RectTransform)row.Plus.transform,width-step,4,step,step);
            float sw=width-sx-step-gap;
            Place((RectTransform)row.Slider.transform,sx,4,sw,step);
            var track=row.Slider.transform.GetChild(0) as RectTransform;
            if(track!=null){Place(track,0,24.8f,sw,20.8f);Track(track.GetComponent<Image>(),false);}
            Place((RectTransform)row.Slider.fillRect.parent,0,24.8f,sw,20.8f);Track(row.Slider.fillRect.GetComponent<Image>(),true);
            Place((RectTransform)row.Slider.handleRect.parent,26.4f,0,sw-52.8f,step);
            row.Slider.handleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,52.8f);
            row.Slider.handleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,52.8f);
        }
        private static void Track(Image image,bool fill)
        {
            if(image==null)return;
            // The older painted strip had only six opaque rows inside a 23px sprite.
            // These tight 21px raster crops retain the approved HTML's complete rail and fill.
            image.sprite=QdaoUguiTheme.RequireSprite("UI/Ugui/AttributeExact20261022/"+(fill?"slider_fill":"slider_track"));
            image.color=Color.white;image.preserveAspect=false;image.type=Image.Type.Sliced;
            image.fillCenter=true;image.useSpriteMesh=false;
            image.pixelsPerUnitMultiplier=image.sprite.rect.height/20.8f;
        }
        internal static void Summary(RectTransform root,TMP_Text source,float x,float y,float width,
            System.Func<string> text,System.Func<uint> pending,System.Func<bool> available)
        {
            // Keep the business status text intact for its existing consumers; render a separate view.
            source.enabled=false;
            var display=Text(root,"ExactRemain",x+62.4f,y,width-224,68.8f,string.Empty,33.6f,Ink);
            var badge=Find(root,"ExactPendingBadge");
            if(badge==null)
            {
                badge=QdaoUguiFactory.CreateRect("ExactPendingBadge",root,x+width-144,y+13.6f,144,41.6f);
                var border=QdaoUguiTheme.Html("#C3B490");
                QdaoUguiFactory.CreateImage("Top",badge,0,0,144,1.6f,null).color=border;
                QdaoUguiFactory.CreateImage("Bottom",badge,0,40,144,1.6f,null).color=border;
                QdaoUguiFactory.CreateImage("Left",badge,0,0,1.6f,41.6f,null).color=border;
                QdaoUguiFactory.CreateImage("Right",badge,142.4f,0,1.6f,41.6f,null).color=border;
                Text(badge,"Label",8,0,128,41.6f,"待分配",19.2f,Muted,true).alignment=TextAlignmentOptions.Center;
            }
            var view=root.GetComponent<AttributeFidelitySummary>();
            if(view==null)view=root.gameObject.AddComponent<AttributeFidelitySummary>();
            view.Source=source;view.Display=display;view.Badge=badge;view.ResolveText=text;view.ResolvePending=pending;view.ResolveAvailable=available;
            view.Refresh();
        }
        // Secondary production actions live in a collapsible overlay, outside the approved footer.
        internal static RectTransform MoreMenu(RectTransform root,float height)
        {
            var menu=Find(root,"ExactMoreMenu");if(menu!=null)return menu;
            var entry=CharacterAttributeVisuals.Button("ExactMoreEntry",root,360,49,136,52,"更多",false,25.6f);
            menu=CharacterAttributeVisuals.Panel("ExactMoreMenu",root,81.6f,110,420,height,"window_frame").rectTransform;
            menu.GetComponent<Image>().raycastTarget=true;menu.gameObject.SetActive(false);
            entry.Button.onClick.AddListener(()=>{menu.gameObject.SetActive(!menu.gameObject.activeSelf);if(menu.gameObject.activeSelf)menu.SetAsLastSibling();});
            return menu;
        }
        internal static void MenuAction(RectTransform menu,UiTextButton button,int index,bool selected=false)
        {
            if(button==null)return;
            if(button.Rect.parent!=menu){button.Rect.SetParent(menu,false);button.Button.onClick.AddListener(()=>menu.gameObject.SetActive(false));}
            Button(button,24,24+index*64,372,56,27.2f);
            button.Label.enableAutoSizing=true;button.Label.fontSizeMin=18;button.Label.fontSizeMax=27.2f;
            button.Label.text=button.Label.text.Replace("\n","");
            CharacterAttributeVisuals.Skin(button.Plate,selected?"button_primary":"button_secondary");button.Label.color=selected?QdaoRefreshArt.Ivory:Ink;
        }
        internal static void MenuAction(RectTransform root,RectTransform menu,string name,int index)
        {
            var rect=Find(root,name);if(rect==null)return;
            if(rect.parent!=menu){rect.SetParent(menu,false);rect.GetComponent<UnityEngine.UI.Button>()?.onClick.AddListener(()=>menu.gameObject.SetActive(false));}
            Place(rect,24,24+index*64,372,56);
            var plate=rect.GetComponent<Image>();if(plate!=null)CharacterAttributeVisuals.Skin(plate,"button_secondary");
            var label=rect.GetComponentInChildren<TMP_Text>();if(label!=null){label.text=label.text.Replace("\n","");Place(label.rectTransform,8,0,356,56);Font(label,27.2f,Ink);}
        }
        internal static void Footer(RectTransform root,string value,System.Action help)
        {
            Text(root,"ExactFooter",89.6f,838.4f,900,49.6f,value,20.8f,QdaoUguiTheme.Html("#8C7E61"));
            if(Find(root,"ExactHelp")!=null)return;
            var button=QdaoUguiFactory.CreateHitButton("ExactHelp",root,2070,838.4f,119,49.6f);
            Text((RectTransform)button.transform,"Label",0,0,119,49.6f,"加点说明",20.8f,Value).fontStyle=FontStyles.Underline;
            button.onClick.AddListener(()=>help());
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
                if(selected){var check=QdaoUguiFactory.CreateImage(name+"Selected",button.Rect,132,23,22,22,QdaoRefreshArt.Load("check"));check.preserveAspect=true;}
            }
        }

        internal static void Header(RectTransform root,string title)
        {
            Place(root,140.8f,89.6f,Width,Height);
            Place(root,"TitlePlate",753.6f,-52.8f,771.2f,131.2f);
            Place(root,"Title",994.4f,-14.4f,289.6f,54.4f);
            var titleImage=Find(root,"Title")?.GetComponent<Image>();if(titleImage!=null)titleImage.preserveAspect=false;
            Place(root,"Close",2169.6f,-32,88,88);Place(root,"ClosePlate",2169.6f,-32,88,88);
            Place(root,"CloseTassel",2222.4f,51.2f,38.4f,104);
            if(Find(root,"V10GameBrand")==null)QdaoUguiFactory.CreateText("V10GameBrand",root,81.6f,48,440,48,"五行奇谈",35.2f,QdaoUguiTheme.Html("#826F47"));
        }
    }

    // Presentation observes the existing model: no allocation values, permissions, or RPCs change.
    // Pet auto suggestions also refresh without rebuilding the layout, so update before rendering.
    internal sealed class AttributeFidelitySummary : MonoBehaviour
    {
        internal TMP_Text Source;
        internal TMP_Text Display;
        internal RectTransform Badge;
        internal System.Func<string> ResolveText;
        internal System.Func<uint> ResolvePending;
        internal System.Func<bool> ResolveAvailable;
        private void LateUpdate()=>Refresh();
        internal void Refresh()
        {
            if(Source==null||Display==null||Badge==null||ResolveText==null)return;
            var value=ResolveText();if(Display.text!=value)Display.text=value;
            bool available=ResolveAvailable();if(Badge.gameObject.activeSelf!=available)Badge.gameObject.SetActive(available);
            var label=Badge.GetComponentInChildren<TMP_Text>(true);
            var pending=ResolvePending();var caption=pending>0?$"待确认 {pending}":"待分配";
            if(label.text!=caption)label.text=caption;
        }
    }
}



