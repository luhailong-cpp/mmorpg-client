#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using MmorpgClient.Game.Mail;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Mail;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Explicit offline mail verification. Temporary additive scenes preserve the user's open scenes.</summary>
public static class MailUiVerification
{
    public static string OutputDirectory=Path.GetFullPath(Path.Combine(Application.dataPath,"../.codex-artifacts/mail-ui-v1"));
    private static GameObject _preview;
    [MenuItem("MMORPG/UI/Preview mail UI (offline)")]
    public static void PreviewOffline()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("请在编辑模式打开独立邮件预览。");
        ClosePreview();
        _preview=new GameObject("[Offline Mail Preview]");_preview.hideFlags=HideFlags.DontSave;
        var host=_preview.AddComponent<MailPreviewHost>();host.Initialize();
        foreach(var child in _preview.GetComponentsInChildren<UnityEngine.Transform>(true))child.gameObject.hideFlags=HideFlags.DontSave;
        AssemblyReloadEvents.beforeAssemblyReload+=ClosePreview;
    }
    [MenuItem("MMORPG/UI/Close mail preview")]
    public static void ClosePreview()
    {
        if(_preview!=null)UnityEngine.Object.DestroyImmediate(_preview);
        _preview=null;AssemblyReloadEvents.beforeAssemblyReload-=ClosePreview;
    }
    [MenuItem("MMORPG/UI/Capture mail screens (offline)")]
    public static void CaptureAll()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("请在编辑模式验收邮件界面。");
        Directory.CreateDirectory(OutputDirectory);
        Capture(2560,1080);Capture(1920,1080);
        File.WriteAllText(Path.Combine(OutputDirectory,"capture.json"),JsonUtility.ToJson(new CaptureReport(),true));
        Debug.Log("Mail UI captures and fixed-action bounds passed: "+OutputDirectory);
    }
    [Serializable] private sealed class CaptureReport
    {
        public string source="Native MailWindow with explicit offline fixtures";
        public int screenshots=16;
        public string[] resolutions={"2560x1080","1920x1080"};
        public bool fixedActionBoundsPassed=true;
        public bool liveServerVerification=false;
        public string generatedAtUtc=DateTime.UtcNow.ToString("O");
    }
    private static void Capture(int width,int height)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var previousTarget=RenderTexture.active;
        RenderTexture target=null;Texture2D pixels=null;MailWindow window=null;
        try
        {
            var camera=new GameObject("MailCaptureCamera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject,scene);camera.scene=scene;
            camera.enabled=false;camera.transform.position=new UnityEngine.Vector3(0,0,-10);camera.orthographic=true;
            camera.nearClipPlane=.1f;camera.farClipPlane=100;camera.cullingMask=1<<31;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=QdaoUguiTheme.Letterbox;
            target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            target.Create();camera.targetTexture=target;
            pixels=new Texture2D(width,height,TextureFormat.RGB24,false,false);
            var canvasObject=new GameObject("MailCaptureCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            SceneManager.MoveGameObjectToScene(canvasObject,scene);
            var canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;
            canvas.worldCamera=camera;canvas.planeDistance=2f;MailUiRoot.ConfigureScaler(canvasObject.GetComponent<CanvasScaler>());
            var design=QdaoUguiFactory.CreateCenteredRect("Design",canvasObject.transform,2560,1080);
            MailUiArt.Art(design,"main-city",0,0,2560,1080);
            var state=new MailUiState(()=>1800000000);window=new MailWindow(design,state);
            MailUiArt.Label(design,"OfflineMailCaptureBadge","离线邮件样例 · 不连接真实账号或背包",26,11,1180,48,24,MailUiArt.Cream);
            void Shoot(string name)
            {
                foreach(var root in scene.GetRootGameObjects())
                    foreach(var child in root.GetComponentsInChildren<UnityEngine.Transform>(true))child.gameObject.layer=31;
                Canvas.ForceUpdateCanvases();
                foreach(var label in canvasObject.GetComponentsInChildren<TMP_Text>(true))label.ForceMeshUpdate(true,true);
                Canvas.ForceUpdateCanvases();
                ValidateFixedActions(window,camera,width,height);
                camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,width,height),0,0,false);pixels.Apply(false,false);
                File.WriteAllBytes(Path.Combine(OutputDirectory,name+"_"+width+"x"+height+".png"),pixels.EncodeToPNG());
            }
            state.LoadDemo();window.Show();Shoot("01-activity-mail");
            state.Select("maintenance");Shoot("02-plain-pending");
            state.Select("claimed");Shoot("03-claimed");
            state.Select("expired");Shoot("04-expired");
            state.Select("notice");Shoot("05-notification");
            state.Select("midautumn");window.OpenActivity();Shoot("06-activity-dialog");window.Back();
            state.Select("claimed");
            window.Root.GetComponentsInChildren<Button>().Single(b=>b.name=="MailDelete").onClick.Invoke();
            Shoot("07-delete-confirmation");window.Back();
            state.Reset();Shoot("08-unavailable");
        }
        finally
        {
            window?.Dispose();RenderTexture.active=previousTarget;
            if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);
            if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
            if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    private static void ValidateFixedActions(MailWindow window,Camera camera,int width,int height)
    {
        var frame=ScreenRect(window.Frame,camera);
        foreach(var button in window.Root.GetComponentsInChildren<Button>())
        {
            if(button.name!="MailClaimAll"&&button.name!="MailClearRead"&&button.name!="MailClaimSelected"&&
               button.name!="MailDelete"&&button.name!="MailOpenActivity"&&button.name!="MailRefresh")continue;
            var rect=ScreenRect((RectTransform)button.transform,camera);
            const float tolerance=2;
            if(rect.xMin < -tolerance||rect.yMin < -tolerance||rect.xMax>width+tolerance||rect.yMax>height+tolerance||
               rect.xMin<frame.xMin-tolerance||rect.yMin<frame.yMin-tolerance||rect.xMax>frame.xMax+tolerance||rect.yMax>frame.yMax+tolerance)
                throw new InvalidOperationException(button.name+" overflows frame/screen at "+width+"x"+height+": "+rect);
            if(button.GetComponentInParent<ScrollRect>()!=null)
                throw new InvalidOperationException(button.name+" must be outside scrolling content.");
        }
    }
    private static Rect ScreenRect(RectTransform transform,Camera camera)
    {
        var corners=new UnityEngine.Vector3[4];transform.GetWorldCorners(corners);
        var points=corners.Select(c=>RectTransformUtility.WorldToScreenPoint(camera,c)).ToArray();
        return Rect.MinMaxRect(points.Min(p=>p.x),points.Min(p=>p.y),points.Max(p=>p.x),points.Max(p=>p.y));
    }
    [MenuItem("MMORPG/UI/Create mail offline preview prefab and scene")]
    public static void BuildPreviewAssets()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("请在编辑模式创建预览资源。");
        const string prefabPath="Assets/Prefabs/UI/MailOfflinePreview.prefab";
        const string scenePath="Assets/Scenes/MailOfflinePreview.unity";
        if(File.Exists(prefabPath)||File.Exists(scenePath))
            throw new InvalidOperationException("独立预览资源已存在，请直接使用，避免覆盖已有修改。");
        Directory.CreateDirectory("Assets/Prefabs/UI");Directory.CreateDirectory("Assets/Scenes");
        var previousScene=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var root=new GameObject("MailOfflinePreview");root.AddComponent<MailPreviewHost>();
            PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
            if(!EditorSceneManager.SaveScene(scene,scenePath))throw new InvalidOperationException("无法保存独立邮件预览场景。");
        }
        finally
        {
            if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
            if(previousScene.IsValid()&&previousScene.isLoaded)SceneManager.SetActiveScene(previousScene);
        }
        Debug.Log("Created explicit offline mail preview: "+prefabPath+" and "+scenePath);
    }
}
#endif
