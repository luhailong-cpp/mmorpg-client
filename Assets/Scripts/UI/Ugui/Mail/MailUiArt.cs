using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Mail
{
    public static class MailUiArt
    {
        public const string Root = "UI/Ugui/MailV1/";
        public static readonly Color Ink = QdaoUguiTheme.Html("#394D36"), Muted = QdaoUguiTheme.Html("#786A50"),
            Jade = QdaoUguiTheme.Html("#285B40"), Cream = QdaoUguiTheme.Html("#FFF1CD"),
            Gold = QdaoUguiTheme.Html("#B59A64"), Line = QdaoUguiTheme.Html("#C7B58B");
        private static TMP_FontAsset _body;
        public static TMP_FontAsset BodyFont
        {
            get {
                if (_body != null) return _body;
                var font = Resources.Load<Font>("Fonts/TeamNotoSansSC");
                if (font == null) return QdaoUguiTheme.ResolveFont();
                _body = TMP_FontAsset.CreateFontAsset(font); _body.name = "Mail Noto Sans SC (Dynamic)";
                _body.isMultiAtlasTexturesEnabled = true; return _body;
            }
        }
        public static Sprite Load(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Contains("/") || key.Contains("\\") || key.Contains("..") || key.Contains(":")) return null;
            return Resources.Load<Sprite>(Root + key);
        }
        public static Image Art(Transform parent,string key,float x,float y,float w,float h,bool aspect=false)
        {
            var image=QdaoUguiFactory.CreateImage(key,parent,x,y,w,h,Load(key));
            image.type=!aspect&&image.sprite!=null&&image.sprite.border.sqrMagnitude>0?Image.Type.Sliced:Image.Type.Simple;
            image.preserveAspect=aspect; image.raycastTarget=false; return image;
        }
        public static TextMeshProUGUI Label(Transform parent,string name,string value,float x,float y,float w,float h,
            float size=28,Color? color=null,bool wrap=false,bool title=false,TextAlignmentOptions align=TextAlignmentOptions.MidlineLeft)
        {
            var text=QdaoUguiFactory.CreateText(name,parent,x,y,w,Mathf.Max(h,size*1.6f),value,size,color??Ink,align);
            text.font=title?QdaoUguiTheme.ResolveFont():BodyFont; text.richText=false;
            text.textWrappingMode=wrap?TextWrappingModes.Normal:TextWrappingModes.NoWrap;
            text.overflowMode=wrap?TextOverflowModes.Overflow:TextOverflowModes.Ellipsis; return text;
        }
        public static Button Button(Transform parent,string name,string label,float x,float y,float w,float h,
            Action click,bool primary=false,float size=28,string key=null)
        {
            key??=primary?"button_primary":"button_secondary";
            var button=QdaoUguiFactory.CreateArtButton(name,parent,x,y,w,h,Load(key),out var image);
            image.type=image.sprite!=null&&image.sprite.border.sqrMagnitude>0?Image.Type.Sliced:Image.Type.Simple;
            button.navigation=new Navigation {mode=Navigation.Mode.Automatic};
            var colors=button.colors;colors.highlightedColor=new Color(1,.96f,.82f);
            colors.selectedColor=new Color(1,.93f,.68f);colors.disabledColor=new Color(.68f,.68f,.64f,.75f);button.colors=colors;
            if(click!=null)button.onClick.AddListener(()=>click());
            if(!string.IsNullOrEmpty(label))Label(button.transform,"ButtonLabel",label,14,(h-size*1.6f)/2,w-28,size*1.6f,size,
                primary?Cream:Ink,align:TextAlignmentOptions.Center);
            return button;
        }
        public static void SetLabel(Button button,string value)
        {var text=button.GetComponentInChildren<TMP_Text>();if(text!=null)text.text=value;}
        public static Image Solid(Transform parent,string name,float x,float y,float w,float h,Color color,bool hit=false)
        {var image=QdaoUguiFactory.CreateImage(name,parent,x,y,w,h,null,hit);image.color=color;return image;}
        public static void Clear(Transform parent)
        {
            for(int i=parent.childCount-1;i>=0;i--){var child=parent.GetChild(i).gameObject;child.SetActive(false);
                if(Application.isPlaying)UnityEngine.Object.Destroy(child);else UnityEngine.Object.DestroyImmediate(child);}
        }
        public static ScrollRect Scroll(Transform parent,string name,float x,float y,float w,float h,out RectTransform content)
        {
            var outer=QdaoUguiFactory.CreateRect(name,parent,x,y,w,h);
            var hit=outer.gameObject.AddComponent<Image>();hit.color=new Color(1,1,1,.001f);
            var scroll=outer.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.vertical=true;
            scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=42;
            var viewport=QdaoUguiFactory.CreateRect("Viewport",outer,0,0,w-18,h);viewport.gameObject.AddComponent<RectMask2D>();
            content=QdaoUguiFactory.CreateRect("Content",viewport,0,0,w-22,h);
            scroll.viewport=viewport;scroll.content=content;
            var track=QdaoUguiFactory.CreateRect("Scrollbar",outer,w-12,0,9,h);track.gameObject.AddComponent<Image>().color=new Color(.45f,.42f,.3f,.13f);
            var bar=track.gameObject.AddComponent<Scrollbar>();bar.direction=Scrollbar.Direction.BottomToTop;
            var handle=QdaoUguiFactory.CreateStretch("Handle",track,Vector4.zero);var image=handle.gameObject.AddComponent<Image>();image.color=Gold;
            bar.targetGraphic=image;bar.handleRect=handle;scroll.verticalScrollbar=bar;
            scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;return scroll;
        }
    }
}
