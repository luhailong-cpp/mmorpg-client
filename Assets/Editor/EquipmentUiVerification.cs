#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Tweening;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Isolated production-view fixtures. No session, transport, inventory mutations or refine RPC.</summary>
public static class EquipmentUiVerification
{
    public static string OutputDirectory = "E:/work/image/designs/equipment-ui-20261008/qa";

    [MenuItem("MMORPG/UI/Capture equipment artwork")]
    public static void CaptureAll()
    {
        Capture(2560,1080);
        Capture(1920,1080);
        Capture(1280,720);
        File.WriteAllText(Path.Combine(OutputDirectory,"verification.txt"),
            "PASS: native Unity uGUI capture and interaction checks at 2560x1080, 1920x1080 and 1280x720.\n"+
            "Four requested visual states plus unavailable-pool and long-attribute states.\n"+
            "Preview values are editor fixtures. No live backend, refine RPC, equip RPC or server inventory was tested.\n");
    }

    public static void Capture(int width,int height)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Requires Edit Mode.");
        Directory.CreateDirectory(OutputDirectory);
        var previousScene=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var previousTarget=RenderTexture.active;
        RenderTexture target=null; Texture2D pixels=null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera=new GameObject("EquipmentFixtureCamera").AddComponent<Camera>();
            camera.enabled=false; camera.transform.position=new UnityEngine.Vector3(0,0,-10);
            camera.orthographic=true; camera.nearClipPlane=.1f; camera.farClipPlane=100;
            camera.cullingMask=1<<31; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=QdaoUguiTheme.Letterbox;
            target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            target.Create(); camera.targetTexture=target;
            pixels=new Texture2D(width,height,TextureFormat.RGB24,false,false);
            RealtimeTween.BeginEditorCapture();
            var co=new GameObject("EquipmentFixtureCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            var canvas=co.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=2;
            var scaler=co.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(2560,1080); scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var root=QdaoUguiFactory.CreateCenteredRect("EquipmentFixtureRoot",co.transform,2560,1080);
            QdaoUguiFactory.CreateImage("Backdrop",root,0,0,2560,1080,QdaoRefreshArt.Load("sanctuary_background"));
            var window=new GameplayWindow(root); window.SetBag(Bag(),false,null,false); window.Show(GameplayPage.Bag);
            var card=new EquipDetailCard(root,1846,258);
            var wish=new EquipWishDialog(root);
            var scope=GameplayUiArt.Text(root,"编辑器效果预览 · 数值为布局样例 · 炼化尚未开放",44,1030,2200,34,24,QdaoUguiTheme.Cream);
            scope.raycastTarget=false;

            void Shoot(string name)
            {
                scope.transform.SetAsLastSibling(); RealtimeTween.AdvanceEditorCapture(1f);
                foreach(var go in scene.GetRootGameObjects())
                {
                    foreach(var tr in go.GetComponentsInChildren<UnityEngine.Transform>(true))tr.gameObject.layer=31;
                    foreach(var cc in go.GetComponentsInChildren<Canvas>(true)){cc.renderMode=RenderMode.ScreenSpaceCamera;cc.worldCamera=camera;cc.planeDistance=2;}
                }
                Canvas.ForceUpdateCanvases();
                foreach(var label in co.GetComponentsInChildren<TMP_Text>(true))label.ForceMeshUpdate(true,true);
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,width,height),0,0,false); pixels.Apply(false,false);
                File.WriteAllBytes(Path.Combine(OutputDirectory,name+"_"+width+"x"+height+".png"),pixels.EncodeToPNG());
            }
            Button Find(string name) => root.GetComponentsInChildren<Button>(true).Single(b=>b.name==name);
            void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}

            uint[] confirmed=null; bool cancelled=false;
            var initial=new uint[]{1,3,17};
            wish.Show(WeaponOptions(),initial,ids=>confirmed=ids,()=>cancelled=true,"选择期望属性 · 武器");
            Shoot("01-wish-weapon");
            Find("WishOption_2").onClick.Invoke(); Find("ConfirmWishSelection").onClick.Invoke();
            Require(confirmed!=null && confirmed.Contains(2u) && !wish.IsVisible,"Wish confirm must emit the selected IDs and close.");
            Require(initial.SequenceEqual(new uint[]{1,3,17}),"Selection must not mutate caller input.");
            wish.Show(ArmourOptions(),new uint[]{8,9,7},ids=>confirmed=ids,()=>cancelled=true,"选择期望属性 · 防具");
            Shoot("02-wish-armour");
            Find("ClearWishSelection").onClick.Invoke(); Find("ConfirmWishSelection").onClick.Invoke();
            Require(confirmed.Length==0,"Clear must confirm an empty preference.");
            wish.Show(ArmourOptions(),new uint[]{8},ids=>confirmed=ids,()=>cancelled=true); wish.Cancel();
            Require(cancelled && !wish.IsVisible,"Cancel must close and invoke cancellation.");
            var weapon=Weapon(); int actions=0; card.Show(weapon,()=>actions++); Shoot("03-weapon-detail");
            Find("EquipAction").onClick.Invoke(); Require(actions==1,"Card must delegate the equipment action.");
            card.Show(Boots(),()=>actions++); Shoot("04-boots-detail"); card.Hide();
            wish.Show(null,null,ids=>confirmed=ids); Require(!Find("ConfirmWishSelection").interactable,"Missing options must disable confirmation.");
            Shoot("05-pool-unavailable"); wish.Hide();
            var longCard=Weapon(); for(int i=0;i<20;i++)longCard.Lines.Add(new EquipDetailLine("附加属性样例 "+i,EquipDetailCard.TextColor));
            card.Show(longCard,null); Require(root.GetComponentsInChildren<ScrollRect>().Any(),"Long attributes must be scrollable.");
            Shoot("06-long-attributes");
            Debug.Log("EQUIPMENT_UI_VERIFIED|"+width+"x"+height+"|6 screenshots|"+OutputDirectory);
        }
        finally
        {
            RealtimeTween.EndEditorCapture(); RenderTexture.active=previousTarget;
            if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);
            if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
            if(previousScene.IsValid()&&previousScene.isLoaded)SceneManager.SetActiveScene(previousScene);
            if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
        }
    }

    private static EquipWishOption[] WeaponOptions() => new[]{
        O(1,"伤害"),O(3,"力量"),O(4,"体质"),O(5,"灵力"),O(6,"敏捷"),O(2,"准确"),
        O(14,"物理连击率"),O(15,"反击率"),O(12,"物理必杀率"),O(17,"所有技能上升"),O(18,"忽视所有抗异常"),O(13,"法术必杀率")};
    private static EquipWishOption[] ArmourOptions() => new[]{
        O(8,"防御"),O(9,"气血"),O(10,"法力"),O(3,"力量"),O(4,"体质"),O(5,"灵力"),O(6,"敏捷"),O(7,"所有属性"),O(16,"反震率"),
        O(19,"抗中毒"),O(20,"抗冰冻"),O(21,"抗昏睡"),O(22,"抗遗忘"),O(23,"抗混乱"),O(24,"所有抗异常"),O(25,"抗法术"),O(26,"抗物理")};
    private static EquipWishOption O(uint id,string label)=>new EquipWishOption(id,label);
    private static EquipDetailCardData Weapon()
    {
        var d=new EquipDetailCardData{Name="青云灵剑",IconKey="UI/Ugui/EquipmentV1/icon_sword",State="已穿戴",LevelRequirement="角色要求：等级 80",ClassRequirement="职业要求：无",Description="玉刃凝青霄，剑鸣伴云行。",ActionLabel="卸下",ActionEnabled=true};
        Add(d,"伤害：1288","#FFF3D6");Add(d,"准确：360","#FFF3D6");Add(d,"力量 18/24","#5AA7FF");Add(d,"所有技能上升 5/10","#5AA7FF");Add(d,"物理必杀率 12%/20%","#FF7AD9");Add(d,"伤害 240/320","#FFD84A");return d;
    }
    private static EquipDetailCardData Boots()
    {
        var d=new EquipDetailCardData{Name="踏云靴",IconKey="UI/Ugui/EquipmentV1/icon_boots",State="未穿戴",LevelRequirement="角色要求：等级 80",ClassRequirement="职业要求：无",Description="云纹藏足下，一步过青山。",ActionLabel="装备",ActionEnabled=true};
        Add(d,"防御：216","#FFF3D6");Add(d,"速度：54","#FFF3D6");Add(d,"敏捷 15/30","#5AA7FF");Add(d,"所有属性 20/24","#5AA7FF");Add(d,"气血 150/180","#FF7AD9");Add(d,"防御 48/64","#FFD84A");return d;
    }
    private static void Add(EquipDetailCardData d,string label,string color)=>d.Lines.Add(new EquipDetailLine(label,QdaoUguiTheme.Html(color)));
    private static BagInfo Bag()
    {
        var bag=new BagInfo{Layout=new BagLayoutInfo{BagType=0,Capacity=56,CanSort=true},Currency=new CurrencyComp()};
        bag.Currency.Values.Add(new ulong[]{128640,280,1680});
        string[] keys={"UI/Ugui/EquipmentV1/icon_sword","UI/Ugui/EquipmentV1/icon_boots","UI/qdao_v3/icons_weapon/012_peachwood_sword","UI/qdao_v3/icons_weapon/007_flying_sword"};
        string[] names={"青云灵剑","踏云靴","桃木灵剑","流光飞剑"};
        for(uint i=0;i<20;i++)
        {
            ulong id=900000+i;
            bag.Items.Add(new BagItemInfo{ItemId=id,ConfigId=1000+i,Name=names[i%4],IconKey=keys[i%4],Description="编辑器布局样例",Count=1,MaxStack=1,EquipKind=1});
            bag.Layout.Slots.Add(new BagSlotInfo{Slot=i,ItemId=id,Width=1,Height=1});
        }
        return bag;
    }
}
#endif
