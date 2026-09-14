#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using MmorpgClient.Game.Jubaozhai;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Jubaozhai;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Isolated, offline validation of the actual Jubaozhai view. Never sends requests.</summary>
public static class JubaozhaiUiVerification
{
    public static string OutputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../.codex-artifacts/jubaozhai-ui"));

    [MenuItem("MMORPG/UI/Jubaozhai/Capture and verify")]
    public static void CaptureAll()
    {
        Directory.CreateDirectory(OutputDirectory);
        Capture(2560,1080);
        Capture(1920,1080);
        File.WriteAllText(Path.Combine(OutputDirectory,"capture.json"),
            "{\"status\":\"passed\",\"screenshots\":20,\"resolutions\":[\"2560x1080\",\"1920x1080\"],\"source\":\"native production JubaozhaiWindow\",\"liveServerVerified\":false}");
        Debug.Log("JUBAO_CAPTURE_OK|20 screenshots|native interactions passed");
    }
    private static void Require(bool condition,string message)
    { if(!condition) throw new InvalidOperationException("Jubaozhai QA: "+message); }
    private static void Capture(int width,int height)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
        var previous=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var active=RenderTexture.active;
        RenderTexture target=null; Texture2D pixels=null; JubaozhaiWindow window=null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera=new GameObject("JubaoCaptureCamera").AddComponent<Camera>();
            camera.enabled=false; camera.transform.position=new UnityEngine.Vector3(0,0,-10);
            camera.orthographic=true; camera.nearClipPlane=.1f;camera.farClipPlane=100;
            camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=QdaoUguiTheme.Letterbox;
            target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            target.Create();camera.targetTexture=target;
            pixels=new Texture2D(width,height,TextureFormat.RGB24,false,false);
            var canvasObject=new GameObject("JubaoCaptureCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            var canvas=canvasObject.GetComponent<Canvas>();
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=2;
            var scaler=canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(2560,1080);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var design=QdaoUguiFactory.CreateCenteredRect("Design",canvasObject.transform,2560,1080);
            QdaoUguiFactory.CreateImage("Backdrop",design,0,0,2560,1080,QdaoRefreshArt.Load("sanctuary_background"));
            long fixtureNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            window=new JubaozhaiWindow(design, new JubaozhaiState(clock: () => fixtureNow));
            window.State.LoadDemo();window.Show();
            QdaoUguiFactory.CreateText("OfflineFixtureBadge",design,300,1038,1960,32,
                "离线界面验收 · 样例商品",24,QdaoUguiTheme.Cream,TextAlignmentOptions.Center);
            Button FindButton(string name) => canvasObject.GetComponentsInChildren<Button>(true).FirstOrDefault(b=>b.name==name);
            void Click(string name)
            {
                var button=FindButton(name);Require(button!=null,"missing button "+name);
                Require(button.gameObject.activeInHierarchy && button.IsInteractable(),"inactive button "+name);
                button.onClick.Invoke();
            }
            void Shoot(string name)
            {
                foreach(var root in scene.GetRootGameObjects())
                    foreach(var child in root.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer=31;
                Canvas.ForceUpdateCanvases();
                foreach(var label in canvasObject.GetComponentsInChildren<TMP_Text>(true)) label.ForceMeshUpdate(true,true);
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,width,height),0,0,false);pixels.Apply(false,false);
                File.WriteAllBytes(Path.Combine(OutputDirectory,name+"_"+width+"x"+height+".png"),pixels.EncodeToPNG());
                foreach(var image in canvasObject.GetComponentsInChildren<Image>())
                    if(image.name.StartsWith("Art_",StringComparison.Ordinal)) Require(image.sprite!=null,"missing art "+image.name);
            }
            var categories=new[]{JubaozhaiCategory.Character,JubaozhaiCategory.Pet,JubaozhaiCategory.Weapon,JubaozhaiCategory.Armor,JubaozhaiCategory.SummoningOrder,JubaozhaiCategory.Set};
            for(int i=0;i<categories.Length;i++)
            {
                Click("Category_"+categories[i]);
                Require(window.State.Category==categories[i],"category button did not change state");
                Require(window.State.GetPage().TotalCount>0,"demo category empty "+categories[i]);
                Shoot("0"+(i+1)+"-"+categories[i]);
            }
            Click("Category_Character");
            var first=window.State.GetPage().Items[0];
            Click("JubaoRow_"+first.Id);
            Require(window.SelectedId==first.Id,"unique selection");
            Click("JubaoFavorite");Require(window.State.IsFavorite(first.Id),"favorite click");
            Click("JubaoView");Require(window.ModalVisible,"detail must open");Shoot("07-details");
            window.Back();Require(!window.ModalVisible && window.IsVisible,"ESC/back closes only modal");
            Click("JubaoFavorite");Require(!window.State.IsFavorite(first.Id),"unfavorite click");
            Click("JubaoFavoritesOnly");Require(window.State.GetPage().TotalCount==0,"favorites empty state");Shoot("08-favorites-empty");
            Click("Category_Character");
            window.State.SetFavoritesOnly(false);
            var input=canvasObject.GetComponentsInChildren<TMP_InputField>(true).First(x=>x.name=="JubaoSearch");
            input.text="不存在的商品编号";
            Click("JubaoSearchSubmit");
            Require(window.State.GetPage().TotalCount==0,"search no results");
            Require(FindButton("JubaoView").interactable==false,"no-results detail must be disabled");
            Shoot("09-search-empty");
            Click("JubaoClearSearch");Require(window.State.GetPage().TotalCount>0,"clear search restores results");
            Click("JubaoRules");Require(window.ModalVisible,"rules dialog");window.Back();
            Click("JubaoEstimate");Require(window.ModalVisible,"estimate dialog");window.Back();
            Click("JubaoRow_"+window.State.GetPage().Items[0].Id);
            Click("JubaoView");
            var changed = window.State.GetPage().Items[0];
            changed.Price = 4321.00m;
            changed.Details = "同一编号的服务端快照更新";
            changed.ExpiresAtUnixSeconds = fixtureNow + 1;
            window.State.SetListings(new[]{changed});
            Require(window.ModalVisible,"detail retained for same listing update");
            Require(canvasObject.GetComponentsInChildren<TMP_Text>().Any(t => t.text.Contains("4,321.00")),"detail price refreshed from snapshot");
            fixtureNow += 2;
            window.Tick(999);
            Require(!window.ModalVisible && string.IsNullOrEmpty(window.SelectedId),"expired listing clears detail and selection");
            window.State.Reset();window.Show();
            Require(!window.State.IsDemo && window.State.GetPage().TotalCount==0,"production starts empty");
            Shoot("10-production-empty");
            window.Back();Require(!window.IsVisible,"back closes window");
            File.WriteAllText(Path.Combine(OutputDirectory,"interactions-"+width+".txt"),
                "PASS: six category buttons; unique selection; favorite toggle; detail modal; modal Back; empty favorites; native search; empty selection disabled; clear search; rules; estimate; production empty; close.\nOffline only.\n");
        }
        finally
        {
            window?.Dispose();RenderTexture.active=active;
            if(pixels!=null) UnityEngine.Object.DestroyImmediate(pixels);
            if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
            if(previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if(scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene,true);
        }
    }
    [MenuItem("MMORPG/UI/Jubaozhai/Build offline preview scene")]
    public static void BuildPreview()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
        var previous=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var host=new GameObject("Jubaozhai Offline Preview",typeof(JubaozhaiPreviewHost));
            var camera=new GameObject("Preview Camera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.position=new UnityEngine.Vector3(0,0,-10);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=QdaoUguiTheme.Letterbox;
            Directory.CreateDirectory("Assets/Prefabs/UI");
            PrefabUtility.SaveAsPrefabAsset(host,"Assets/Prefabs/UI/JubaozhaiOfflinePreview.prefab");
            EditorSceneManager.SaveScene(scene,"Assets/Scenes/JubaozhaiPreview.unity");
        }
        finally
        {
            if(previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if(scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene,true);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("JUBAO_PREVIEW_READY|Assets/Scenes/JubaozhaiPreview.unity");
    }
    [MenuItem("MMORPG/UI/Jubaozhai/Run model tests")]
    public static void RunTests()
    {
        Directory.CreateDirectory(OutputDirectory);
        var assembly = System.Reflection.Assembly.Load("MmorpgClient.Tests.EditMode.Tianyong");
        var type = assembly.GetType("MmorpgClient.Tests.EditMode.Tianyong.JubaozhaiModelTests", true);
        int passed = 0, failed = 0;
        var records = new System.Collections.Generic.List<string>();
        foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            if (!method.GetCustomAttributes(false).Any(a => a.GetType().FullName == "NUnit.Framework.TestAttribute")) continue;
            try { method.Invoke(Activator.CreateInstance(type), null); passed++; records.Add("PASS " + method.Name); }
            catch (Exception ex) { failed++; records.Add("FAIL " + method.Name + ": " + (ex.InnerException ?? ex).Message); }
        }
        File.WriteAllLines(Path.Combine(OutputDirectory,"tests.txt"),records);
        File.WriteAllText(Path.Combine(OutputDirectory,"tests-summary.json"),
            "{\"execution\":\"NUnit assertions directly inside Unity Editor via official MCP; scene unchanged\",\"passed\":"+passed+",\"failed\":"+failed+"}");
        if(passed != 6 || failed != 0) throw new InvalidOperationException("Jubaozhai model tests: " + string.Join("; ",records));
        Debug.Log("JUBAO_TESTS|passed="+passed+"|failed="+failed);
    }
}
#endif