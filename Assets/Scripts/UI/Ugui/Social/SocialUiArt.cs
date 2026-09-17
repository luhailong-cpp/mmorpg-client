using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Social
{
    internal static class SocialUiArt
    {
        public const string ResourceRoot="UI/Ugui/SocialV1/";
        public static readonly Color Ink=QdaoUguiTheme.Html("#3B4B37"), Muted=QdaoUguiTheme.Html("#786B51"), Cream=QdaoUguiTheme.Html("#FFF1CF"), Jade=QdaoUguiTheme.Html("#315D40"), Gold=QdaoUguiTheme.Html("#B59B62");
        private static TMP_FontAsset _body;
        public static TMP_FontAsset BodyFont
        {
            get { if(_body!=null)return _body;var font=Resources.Load<Font>("Fonts/TeamNotoSansSC");if(font==null)return QdaoUguiTheme.ResolveFont();_body=TMP_FontAsset.CreateFontAsset(font);_body.name="Social Noto Sans SC (Dynamic)";_body.isMultiAtlasTexturesEnabled=true;return _body; }
        }
        public static Sprite Load(string key)=>QdaoUguiTheme.RequireSprite(ResourceRoot+key);
        public static Image Art(UnityEngine.Transform parent,string key,float x,float y,float w,float h,bool aspect=false)
        {
            var image=QdaoUguiFactory.CreateImage(key,parent,x,y,w,h,Load(key));image.preserveAspect=aspect;image.type=!aspect&&image.sprite.border.sqrMagnitude>0?Image.Type.Sliced:Image.Type.Simple;return image;
        }
        public static Image Solid(UnityEngine.Transform parent,string name,float x,float y,float w,float h,Color color,bool raycast=false)
        {var image=QdaoUguiFactory.CreateImage(name,parent,x,y,w,h,null,raycast);image.color=color;return image;}
        public static void Line(UnityEngine.Transform parent,float x,float y,float w)=>Solid(parent,"Rule",x,y,w,1.5f,new Color(.65f,.56f,.38f,.42f));
        public static TMP_Text Text(UnityEngine.Transform parent,string value,float x,float y,float w,float h,float size=28,Color? color=null,bool wrap=false,bool heading=false,TextAlignmentOptions align=TextAlignmentOptions.MidlineLeft,string name="Label")
        {
            h=Mathf.Max(h,size*1.65f);var text=QdaoUguiFactory.CreateText(name,parent,x,y,w,h,value,size,color??Ink,align);
            text.font=heading?QdaoUguiTheme.ResolveFont():BodyFont;text.richText=false;text.textWrappingMode=wrap?TextWrappingModes.Normal:TextWrappingModes.NoWrap;
            text.overflowMode=TextOverflowModes.Ellipsis;if(wrap)text.alignment=TextAlignmentOptions.TopLeft;return text;
        }
        public static Button Button(UnityEngine.Transform parent,string name,string label,float x,float y,float w,float h,Action clicked,bool primary=false,bool enabled=true,float size=28,string key=null)
        {
            var button=QdaoUguiFactory.CreateArtButton(name,parent,x,y,w,h,Load(key??(primary?"button_primary":"button_secondary")),out var image);
            image.type=image.sprite.border.sqrMagnitude>0?Image.Type.Sliced:Image.Type.Simple;button.interactable=enabled;button.navigation=new Navigation{mode=Navigation.Mode.Automatic};
            var colors=button.colors;colors.highlightedColor=new Color(1,.96f,.81f);colors.selectedColor=new Color(1,.95f,.75f);button.colors=colors;
            if(clicked!=null)button.onClick.AddListener(()=>clicked());
            if(!string.IsNullOrEmpty(label))Text(button.transform,label,12,(h-size*1.65f)*.5f,w-24,size*1.65f,size,primary?Cream:Ink,align:TextAlignmentOptions.Center);
            return button;
        }
        public static TMP_InputField Input(UnityEngine.Transform parent,string name,string hint,string value,float x,float y,float w,float h,bool multiline=false,int limit=120)
        {
            Solid(parent,name+"Border",x-1,y-1,w+2,h+2,Gold);Solid(parent,name+"Paper",x,y,w,h,QdaoUguiTheme.Html("#FFF8E9"));
            var input=QdaoUguiFactory.CreateInputField(name,parent,x,y,w,h,hint,limit);input.lineType=multiline?TMP_InputField.LineType.MultiLineNewline:TMP_InputField.LineType.SingleLine;
            input.textComponent.font=BodyFont;input.textComponent.fontSize=27;input.textComponent.richText=false;input.textComponent.color=Ink;input.textComponent.alignment=multiline?TextAlignmentOptions.TopLeft:TextAlignmentOptions.MidlineLeft;
            input.textComponent.textWrappingMode=multiline?TextWrappingModes.Normal:TextWrappingModes.NoWrap;
            if(input.placeholder is TMP_Text placeholder){placeholder.font=BodyFont;placeholder.fontSize=25;placeholder.color=Muted;placeholder.richText=false;}
            input.SetTextWithoutNotify(value??"");return input;
        }
        public static ScrollRect Scroll(UnityEngine.Transform parent,string name,float x,float y,float w,float h,out RectTransform content)
        {
            var rect=QdaoUguiFactory.CreateRect(name,parent,x,y,w,h);var image=rect.gameObject.AddComponent<Image>();image.color=new Color(1,1,1,.001f);
            var viewport=QdaoUguiFactory.CreateStretch("Viewport",rect,Vector4.zero);viewport.gameObject.AddComponent<RectMask2D>();
            content=QdaoUguiFactory.CreateRect("Content",viewport,0,0,w-16,h);var scroll=rect.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=48;return scroll;
        }
        public static void Portrait(UnityEngine.Transform parent,string name,string key,float x,float y,float size,Action clicked=null)
        {
            var mask=QdaoUguiFactory.CreateImage(name,parent,x,y,size,size,QdaoUguiTheme.RequireSprite(QdaoUguiTheme.StatusDotSpritePath));mask.gameObject.AddComponent<Mask>().showMaskGraphic=false;
            if(!string.IsNullOrEmpty(key))Art(mask.transform,key,-size*.5f,-size*.07f,size*2,size*2,true);
            else{Solid(mask.transform,"UnknownPortrait",0,0,size,size,QdaoUguiTheme.Html("#E7E7CD"));Text(mask.transform,"道友",0,size*.2f,size,size*.6f,size*.25f,Muted,align:TextAlignmentOptions.Center);}
            Art(parent,"portrait_frame",x,y,size,size,true);
            if(clicked!=null){var b=QdaoUguiFactory.CreateHitButton(name+"Button",parent,x,y,size,size);b.navigation=new Navigation{mode=Navigation.Mode.Automatic};b.onClick.AddListener(()=>clicked());}
        }
        public static void Clear(UnityEngine.Transform parent)
        {for(int i=parent.childCount-1;i>=0;i--){var go=parent.GetChild(i).gameObject;go.SetActive(false);if(Application.isPlaying)UnityEngine.Object.Destroy(go);else UnityEngine.Object.DestroyImmediate(go);}}
    }
}
