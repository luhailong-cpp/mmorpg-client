using TMPro;
using UnityEngine;
namespace MmorpgClient.UI.Ugui.Gameplay
{
 public sealed partial class GameplayWindow
 {
  private static void ApplyOriginalLettering(UnityEngine.Transform parent,TMP_Text semantic,string value,float x,float y,float w,float h,float font,Color color,TextAlignmentOptions alignment)
  {
   string key;float sourceWidth,sourceHeight;
   switch(value)
   {
    case "全部":key="exact-fixed-0";sourceWidth=91;sourceHeight=43;break;
    case "装备":key="exact-fixed-1";sourceWidth=84;sourceHeight=41;break;
    case "丹药":key="exact-fixed-2";sourceWidth=88;sourceHeight=42;break;
    case "材料":key="exact-fixed-3";sourceWidth=84;sourceHeight=42;break;
    case "整理":key="exact-fixed-4";sourceWidth=64;sourceHeight=30;break;
    case "出售":key="exact-fixed-5";sourceWidth=64;sourceHeight=42;break;
    case "已接任务":key="exact-fixed-6";sourceWidth=137;sourceHeight=42;break;
    case "可接任务":key="exact-fixed-7";sourceWidth=138;sourceHeight=42;break;
    case "自动追踪":key="exact-fixed-8";sourceWidth=118;sourceHeight=38;break;
    case "查看地图":key="exact-fixed-9";sourceWidth=145;sourceHeight=41;break;
    case "前往任务":key="exact-fixed-10";sourceWidth=164;sourceHeight=47;break;
    case "任务目标":key="exact-fixed-11";sourceWidth=137;sourceHeight=41;break;
    case "任务奖励":key="exact-fixed-12";sourceWidth=138;sourceHeight=34;break;
    case "今日推荐":key="exact-fixed-13";sourceWidth=137;sourceHeight=43;break;
    case "每日活动":key="exact-fixed-14";sourceWidth=140;sourceHeight=43;break;
    case "限时活动":key="exact-fixed-15";sourceWidth=140;sourceHeight=43;break;
    case "节庆活动":key="exact-fixed-16";sourceWidth=140;sourceHeight=45;break;
    case "活动日历":key="exact-fixed-17";sourceWidth=124;sourceHeight=40;break;
    case "前往参与":key="exact-fixed-18";sourceWidth=138;sourceHeight=43;break;
    default:return;
   }
   float scale=Mathf.Min(font/34f,Mathf.Min((w-6)/sourceWidth,(h-4)/sourceHeight));
   float left=alignment==TextAlignmentOptions.Center ? x+(w-sourceWidth*scale)/2 : x;
   var art=QdaoUguiFactory.CreateImage("OriginalLettering_"+key,parent,left,y+(h-sourceHeight*scale)/2,sourceWidth*scale,sourceHeight*scale,NativeSprite(key));
   art.color=color;art.raycastTarget=false;semantic.enabled=false;
  }
 }
}
