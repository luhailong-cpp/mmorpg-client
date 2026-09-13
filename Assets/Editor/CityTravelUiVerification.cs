#if UNITY_EDITOR
using System;
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
public static class CityTravelUiVerification
{
    public static string OutputDirectory { get; set; } = Path.GetFullPath(Path.Combine(
        Application.dataPath, "../.codex-artifacts/festival-city-travel"));

    [MenuItem("MMORPG/UI/Capture city travel screens")]
    public static void CaptureAll()
    {
        Capture(2560, 1080);
        Capture(1920, 1080);
        File.WriteAllText(Path.Combine(OutputDirectory, "capture-status.txt"),
            "地图窗口离线截图完成。这里没有创建网络连接；服务端传送及场景行走另行验证。\n");
    }

    public static void Capture(int width, int height)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("地图窗口截图需要编辑模式。");
        Directory.CreateDirectory(OutputDirectory);
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D pixels = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("CityTravelCaptureCamera").AddComponent<Camera>();
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
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
#endif
