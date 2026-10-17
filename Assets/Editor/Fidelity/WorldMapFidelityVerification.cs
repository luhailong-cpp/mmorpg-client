#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Gameplay;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>离线渲染正式地图窗口，不创建账号、连接或传送请求。</summary>
public static class WorldMapFidelityVerification
{
    public static string OutputDirectory { get; set; } = "E:/work/image/designs/world-map-original-20260925/fidelity-20261016/qa";

    [MenuItem("MMORPG/UI/World Map/Capture fidelity")]
    public static void Capture()
    {
        CaptureResolution(2560, 1080);
        CaptureResolution(1920, 1080);
        File.WriteAllText(Path.Combine(OutputDirectory, "capture-status.txt"),
            "地图窗口离线截图完成。这里没有创建网络连接；服务端传送及场景行走另行验证。\n");
    }

    private static void CaptureResolution(int width, int height)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("地图窗口截图需要编辑模式。");
        Directory.CreateDirectory(OutputDirectory);
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewPreviewScene();
        var previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D pixels = null;
        try
        {
            var camera = new GameObject("CityTravelCaptureCamera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject,scene);camera.scene=scene;
            camera.enabled = false;
            camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100f;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = QdaoUguiTheme.Letterbox;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create();
            camera.targetTexture = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            var canvasObject = new GameObject("CityTravelCaptureCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            SceneManager.MoveGameObjectToScene(canvasObject,scene);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 2f;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var root = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform, 2560, 1080);
            var window = new CityTravelWindow(root);
            window.SetDestinations(CityTravelUiRoot.CreateDestinations());

            void Shoot(string name)
            {
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var child in go.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
                Canvas.ForceUpdateCanvases();
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var label in go.GetComponentsInChildren<TMP_Text>(true)) label.ForceMeshUpdate(true, true);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(OutputDirectory, name + "_" + width + "x" + height + ".png"), pixels.EncodeToPNG());
                WriteGeometry(Path.Combine(OutputDirectory,name+"_"+width+"x"+height+".geometry.json"),width,height,root);
            }

            foreach (var destination in CityTravelUiRoot.CreateDestinations())
            {
                window.Show(destination.SceneConfigId, false);
                Shoot("scene-" + destination.SceneConfigId + "-day");
                if (!destination.HasFestival) continue;
                window.Show(destination.SceneConfigId, true);
                Shoot("scene-" + destination.SceneConfigId + "-festival");
            }
            window.SetState(4, true, true, "行程已安排，正在进入目的地…");
            Shoot("travel-pending");
            window.SetState(4, true, false, "暂时无法前往，请稍后重试。");
            Shoot("travel-error");
            window.Hide();
            Debug.Log("CITY_TRAVEL_UI_CAPTURE_OK|" + width + "x" + height + "|" + OutputDirectory);
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            if(SceneManager.GetActiveScene()!=previousScene)throw new InvalidOperationException("Active scene changed during fidelity capture.");
        }
    }

    [Serializable] private sealed class GeometryReport
    {
        public string fixture="explicit offline editor fixture", coordinateSystem="2560x1080 DesignRoot, top-left origin", unityVersion;
        public bool captureOnly=true,liveServerVerified=false;
        public int width,height;public GeometryItem[] items;
    }
    [Serializable] private sealed class GeometryItem
    {
        public string name,kind,text,resourcePath,font;public Vector2 topLeft,size;public bool interactable,isTextTruncated;
    }
    private static void WriteGeometry(string file,int width,int height,RectTransform design)
    {
        var items=new List<GeometryItem>();
        foreach(var rect in design.GetComponentsInChildren<RectTransform>(false))
        {
            var label=rect.GetComponent<TMP_Text>();var image=rect.GetComponent<Image>();var button=rect.GetComponent<Button>();
            if(label==null&&image==null&&button==null)continue;
            var corners=new UnityEngine.Vector3[4];rect.GetWorldCorners(corners);
            var lo=design.InverseTransformPoint(corners[1]);var hi=design.InverseTransformPoint(corners[3]);
            items.Add(new GeometryItem{name=rect.name,kind=button!=null?"Button":label!=null?"TMP":"Image",
                text=label==null?"":label.text,font=label?.font==null?"":label.font.name,isTextTruncated=label!=null&&label.isTextTruncated,
                resourcePath=image?.sprite==null?"":AssetDatabase.GetAssetPath(image.sprite),interactable=button!=null&&button.IsInteractable(),
                topLeft=new Vector2(lo.x-design.rect.xMin,design.rect.yMax-lo.y),size=new Vector2(hi.x-lo.x,lo.y-hi.y)});
        }
        File.WriteAllText(file,JsonUtility.ToJson(new GeometryReport{width=width,height=height,unityVersion=Application.unityVersion,items=items.ToArray()},true));
    }
}
#endif
