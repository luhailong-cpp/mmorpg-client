using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace MmorpgClient.UI.Ugui.Battle
{
    // Battle-only presentation skin. Actors, turn state, hit rectangles and protocol gates are untouched.
    internal static class BattleFidelityArt
    {
        internal static readonly Color Ink=QdaoUguiTheme.Html("#344638"),Muted=QdaoUguiTheme.Html("#70634E");
        private const string Root="UI/Ugui/AttributesPaintedV2/";
        private static void Skin(Image image,string key)
        {
            if(image==null)return;image.sprite=QdaoUguiTheme.RequireSprite(Root+key);image.color=Color.white;
            image.type=image.sprite.border.sqrMagnitude>0?Image.Type.Sliced:Image.Type.Simple;
            image.preserveAspect=image.type==Image.Type.Simple;
            image.pixelsPerUnitMultiplier=image.sprite.rect.height/Mathf.Max(1,image.rectTransform.rect.height);
        }
        internal static void Button(Button button,bool primary=false)
        {
            if(button==null)return;var image=button.targetGraphic as Image;var label=button.GetComponentInChildren<TMP_Text>(true);
            bool close=button.name.IndexOf("Close",StringComparison.OrdinalIgnoreCase)>=0;
            bool command=button.name.StartsWith("Cmd_",StringComparison.Ordinal);
            Skin(image,close?"close_button":command?"slider_thumb":primary?"button_primary":"button_secondary");
            foreach(var text in button.GetComponentsInChildren<TMP_Text>(true))
            {
                text.font=QdaoUguiTheme.ResolveFont();text.fontStyle=FontStyles.Normal;text.color=primary||command?QdaoRefreshArt.Ivory:Ink;
                if(text.name=="Detail"&&text.text.StartsWith("冷却",StringComparison.Ordinal))text.color=QdaoUguiTheme.Html("#92611D");
            }
            if(close&&label!=null)label.gameObject.SetActive(false);
        }
        internal static void ApplyPanel(RectTransform root)
        {
            if(root==null)return;Skin(root.GetComponent<Image>(),"window_frame");
            foreach(var rect in root.GetComponentsInChildren<RectTransform>(true))
                if(rect.name=="PaintedWindowFrame")rect.gameObject.SetActive(false);
            foreach(var label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                label.font=label.name.EndsWith("Title",StringComparison.Ordinal)?QdaoUguiTheme.ResolveFont():QdaoUguiTypography.ResolveBodyFont();
                label.fontStyle=FontStyles.Normal;label.color=label.name=="Title"?Ink:Muted;
            }
            foreach(var button in root.GetComponentsInChildren<Button>(true))
                Button(button,button.name.Contains("Join")||button.name.Contains("WatchRandom")||button.name.Contains("Confirm")||
                    button.name.Contains("CancelQueue")||button.GetComponentInChildren<TMP_Text>(true)?.text.StartsWith("√",StringComparison.Ordinal)==true);
            if(root.name=="BattleQueuePanel"||root.name=="SpectatePanel")
            {
                var title=root.Find("Title")?.GetComponent<TMP_Text>();
                if(root.Find("FidelityTitlePlate")==null)
                {
                    var plate=QdaoUguiFactory.CreateImage("FidelityTitlePlate",root,(root.rect.width-420)*.5f,4,420,70,QdaoUguiTheme.RequireSprite(Root+"title_plate"));
                    plate.raycastTarget=false;plate.transform.SetAsFirstSibling();
                }
                if(title!=null){title.rectTransform.anchoredPosition=new Vector2((root.rect.width-400)*.5f,-14);title.rectTransform.sizeDelta=new Vector2(400,50);title.fontSize=30;title.alignment=TextAlignmentOptions.Center;title.color=QdaoRefreshArt.Ivory;}
            }
        }
        internal static void ApplyParty(RectTransform root)
        {
            if(root==null)return;
            foreach(UnityEngine.Transform child in root)
            {
                Skin(child.GetComponent<Image>(),"pet_card_normal");
                foreach(var label in child.GetComponentsInChildren<TMP_Text>(true))
                    if(label.name=="Name"||label.name=="Level")
                    {label.font=QdaoUguiTheme.ResolveFont();label.color=label.name=="Name"?Ink:Muted;}
            }
        }
        internal static void ApplyHud(RectTransform root)
        {
            if(root==null)return;
            foreach(var image in root.GetComponentsInChildren<Image>(true))
            {
                if(image.name=="RoundFrame")Skin(image,"tab_horizontal");
                if((image.name=="Gold"||image.name=="Inner")&&image.transform.parent?.name=="RoundFrame")image.gameObject.SetActive(false);
                if(image.name=="RingBase")image.color=new Color(1,1,1,.35f);
            }
            foreach(var button in root.GetComponentsInChildren<Button>(true))
                Button(button,button.name.StartsWith("Cmd_",StringComparison.Ordinal)||button.name.Contains("ConfirmAction")||button.name.Contains("ChannelReconnect"));
            foreach(var panelName in new[]{"BattleLogPanel","SkillPanel","ItemPanel"})
            {
                var panel=root.Find(panelName) as RectTransform;if(panel!=null)ApplyPanel(panel);
            }
            foreach(var label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if(label.name=="RoundA"||label.name=="RoundB"){label.font=QdaoUguiTheme.ResolveFont();label.color=QdaoRefreshArt.Ivory;}
                if(label.name=="Hint"||label.name=="TargetHint"||label.name=="Phase")label.color=Ink;
            }
        }
    }
}
