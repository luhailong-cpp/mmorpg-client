#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;

using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Pet;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
public static class ClientUiQaCapture
{

    static void Invoke(Component target,string method) { target.SendMessage(method,SendMessageOptions.DontRequireReceiver); }
    static GameObject Named(GameObject root,string name) { return root.GetComponentsInChildren<UnityEngine.Transform>(true).First(t=>t.name==name).gameObject; }

    public static void CaptureCityAttribute()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Expected Edit Mode");
        Directory.CreateDirectory(ClientUiRefreshVerification.OutputDirectory);
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/World/TianyongSandbox.unity",OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        RenderTexture target = null;
        Texture2D texture = null;
        Camera camera = null;
        var previousRender = RenderTexture.active;
        RenderTexture previousTarget = null;
        try
        {
            var sandbox = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<TianyongSandboxBootstrap>(true)).Single();
            sandbox.BuildSandbox(); sandbox.SetHelpVisible(false);
            camera = sandbox.WorldCamera;
            previousTarget = camera.targetTexture;
            target = new RenderTexture(2560,1080,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            camera.targetTexture = target;
            Invoke(sandbox,"LateUpdate");
            var animator = sandbox.Player.GetComponent<QdaoBoySpriteAnimator>();
            if (animator != null)
            {
                if (sandbox.Player.transform.Find("sprite") == null) Invoke(animator,"Awake");
                Invoke(animator,"LateUpdate");
            }
            foreach (var label in sandbox.Player.GetComponentsInChildren<WorldLabelBillboard>(true)) { Invoke(label,"Awake"); Invoke(label,"LateUpdate"); }
            // The additive preview may coexist with another tagged MainCamera.
            // Align only these temporary actor visuals to the capture camera.
            var actorSprite = sandbox.Player.transform.Find("sprite");
            if (actorSprite != null) actorSprite.rotation = camera.transform.rotation;
            foreach (var label in sandbox.Player.GetComponentsInChildren<WorldLabelBillboard>(true))
            {
                label.transform.rotation = camera.transform.rotation;
                label.transform.position = sandbox.Player.transform.position - camera.transform.up * label.OffsetBelowFeet - camera.transform.forward * .2f;
            }
            var battleGo = new GameObject("[CityQaBattleUi]");
            var battle = battleGo.AddComponent<BattleUiRoot>();
            Invoke(battle,"BuildCanvas");
            Named(battleGo,"BattleEntry").SetActive(true);
            Named(battleGo,"SpectateEntry").SetActive(true);
            var attributeGo = new GameObject("[CityQaAttributeUi]");
            var attribute = attributeGo.AddComponent<AttributeUiRoot>();
            Invoke(attribute,"BuildCanvas");
            Named(attributeGo,"AttributeEntry").SetActive(true);
            var petGo = new GameObject("[CityQaPetUi]");
            var pet = petGo.AddComponent<PetUiRoot>();
            Invoke(pet,"BuildCanvas"); Named(petGo,"PetEntry").SetActive(true);
            var uiCameraGo = new GameObject("[CityQaOverlayCamera]");
            var uiCamera = uiCameraGo.AddComponent<Camera>();
            uiCamera.enabled = false;
            uiCamera.transform.position = new UnityEngine.Vector3(0,0,-10);
            uiCamera.orthographic = true;
            uiCamera.nearClipPlane = .1f;
            uiCamera.farClipPlane = 100f;
            uiCamera.cullingMask = 1 << 31;
            uiCamera.clearFlags = CameraClearFlags.Depth;
            uiCamera.targetTexture = target;
            camera.cullingMask &= ~(1 << 31);
            foreach (var root in new[]{battleGo,attributeGo,petGo})
            {
                foreach (var node in root.GetComponentsInChildren<UnityEngine.Transform>(true)) node.gameObject.layer=31;
                foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = 1f; }
            }
            texture = new Texture2D(2560,1080,TextureFormat.RGB24,false,false);
            void Shoot(string name)
            {
                Canvas.ForceUpdateCanvases();
                foreach (var root in scene.GetRootGameObjects()) foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true,true);
                Canvas.ForceUpdateCanvases(); camera.Render(); uiCamera.Render();
                RenderTexture.active = target; texture.ReadPixels(new Rect(0,0,2560,1080),0,0,false); texture.Apply(false,false);
                var path = Path.Combine(ClientUiRefreshVerification.OutputDirectory, name + ".png");
                File.WriteAllBytes(path,texture.EncodeToPNG()); Debug.Log("CAPTURE_OK|"+path);
            }
            File.WriteAllText(Path.Combine(ClientUiRefreshVerification.OutputDirectory, "city-render-details.txt"),
                "Camera="+camera.name+" rot="+camera.transform.eulerAngles+" main="+(Camera.main==null?"NULL":Camera.main.name)+" aspect="+camera.aspect+" near="+camera.nearClipPlane+" far="+camera.farClipPlane+"\n"+
                string.Join("\n",sandbox.Player.GetComponentsInChildren<Renderer>(true).Select(r=>r.name+" active="+r.gameObject.activeInHierarchy+" enabled="+r.enabled+" layer="+r.gameObject.layer+" position="+r.transform.position+" rotation="+r.transform.eulerAngles+" scale="+r.transform.lossyScale+" shader="+(r.sharedMaterial==null?"NULL":r.sharedMaterial.shader.name)+" bounds="+r.bounds+" sorting="+r.sortingOrder+(r is SpriteRenderer sr?" sprite="+(sr.sprite==null?"NULL":sr.sprite.name)+" color="+sr.color:""))));
            Shoot("06-main-city-native");
            Debug.Log("CITY_OK|PLAYER=" + sandbox.Player.name + "|TILES=" + sandbox.Map.Root.GetComponentsInChildren<MeshRenderer>(true).Count(r=>r.name.StartsWith("Tile_")) + "|SPRITES=" + sandbox.Player.GetComponentsInChildren<SpriteRenderer>(true).Length + "|WALKABLE=" + TianyongPaintedCity.IsPaintingWalkable(sandbox.Player.transform.position));

            var data = new AttributePanelInfo { ActiveSchemeId=1,MaxSchemes=3,Level=75,CreateSchemeCostGold=100000 };
            data.Schemes.Add(new AttributeSchemeInfo{SchemeId=1,Name="\u65b9\u6848\u4e00"});
            data.Schemes.Add(new AttributeSchemeInfo{SchemeId=2,Name="\u5168\u7075\u65b9\u6848"});
            data.Pools.Add(new AttributePoolInfo{PoolId=1,Name="\u5c5e\u6027\u70b9",Total=300,Remaining=10,Unlocked=true,DimensionCap=300});
            data.Pools.Add(new AttributePoolInfo{PoolId=2,Name="\u76f8\u6027\u70b9",Total=40,Remaining=5,Unlocked=true,DimensionCap=30});
            data.Pools.Add(new AttributePoolInfo{PoolId=3,Name="\u4ed9\u9b54\u70b9",Total=20,Remaining=4,Unlocked=true,DimensionCap=20});
            string[] names={"\u4f53\u8d28","\u7075\u529b","\u529b\u91cf","\u654f\u6377"};
            for(uint i=0;i<4;i++) data.Dimensions.Add(new AttributeDimensionInfo{DimensionId=101+i,PoolId=1,Name=names[i],Desc="\u4fee\u884c\u5c5e\u6027\uff0c\u63d0\u9ad8\u89d2\u8272\u6218\u6597\u80fd\u529b\u3002",Allocated=60,Value=135,Cap=300,Sort=i});
            data.Derived = new DerivedAttributeInfo{Health=5800,MaxHealth=5800,Mana=2300,MaxMana=2300,PhysicalAttack=1380,MagicAttack=2180,Speed=860,Defense=960};
            attribute.SendMessage("HandlePanel",data); Named(attributeGo,"AttributeWindow").SetActive(true);
            Shoot("07-attribute-native");
            for(uint i=4;i<8;i++) data.Dimensions.Add(new AttributeDimensionInfo{DimensionId=101+i,PoolId=1,Name=new[]{"\u6839\u9aa8","\u609f\u6027","\u8eab\u6cd5","\u5b9a\u529b"}[i-4],Desc="\u6700\u5927\u516b\u884c\u5e03\u5c40\u9a8c\u6536\u3002",Allocated=10,Value=25,Cap=300,Sort=i});
            attribute.SendMessage("HandlePanel",data); Canvas.ForceUpdateCanvases();
            foreach(var scroll in attributeGo.GetComponentsInChildren<ScrollRect>(true)) scroll.verticalNormalizedPosition = 0f;
            Shoot("07b-attribute-eight-rows-native");
            Debug.Log("ATTRIBUTE_OK|ROWS=8|NETWORK=disconnected");
        }
        finally
        {
            RenderTexture.active=previousRender;
            if(camera!=null) camera.targetTexture=previousTarget;
            if(texture!=null) UnityEngine.Object.DestroyImmediate(texture);
            if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
            if(previousScene.IsValid()&&previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if(scene.IsValid()&&scene.isLoaded) EditorSceneManager.CloseScene(scene,true);
        }
    }
}



#endif



