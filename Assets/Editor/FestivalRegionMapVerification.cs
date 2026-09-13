#if UNITY_EDITOR
using System;
using System.IO;
using MmorpgClient.World.Tianyong;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>用正式地图构建器生成六个外观的全景与近景，不替代联网验证。</summary>
public static class FestivalRegionMapVerification
{
    public static string OutputDirectory { get; set; } = Path.GetFullPath(Path.Combine(
        Application.dataPath, "../.codex-artifacts/festival-region-map"));

    [MenuItem("MMORPG/World/Capture festival region maps")]
    public static void CaptureAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("地图构建器截图需要编辑模式。");
        Directory.CreateDirectory(OutputDirectory);
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var previousTarget = RenderTexture.active;
        TianyongMapInstance map = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var stage = new GameObject("FestivalMapCaptureStage");
            var camera = new GameObject("FestivalMapCaptureCamera").AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 1200f;
            camera.transform.rotation = Quaternion.Euler(90, 0, 0);
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.06f, .09f, .11f);
            foreach (var region in FestivalRegionMap.Regions)
            foreach (bool festival in new[] { false, true })
            {
                var texture = Resources.Load<Texture2D>(region.TexturePath(festival));
                if (texture == null || texture.width != 1254 || texture.height != 1254)
                    throw new InvalidOperationException("地图原生资源未完整导入：" + region.TexturePath(festival));
                map = FestivalRegionMap.Build(stage.transform, region, festival);
                map.UpdateVisibleChunks(region.Spawn, 12);
                foreach (var transform in stage.GetComponentsInChildren<UnityEngine.Transform>(true))
                    transform.gameObject.layer = 31;
                string name = region.Key + (festival ? "-festival" : "-day");
                camera.transform.position = new UnityEngine.Vector3(200, 500, 150);
                camera.orthographicSize = 150f;
                Capture(camera, name + "-whole", 1254, 1254);
                camera.transform.position = region.Spawn + UnityEngine.Vector3.up * 500;
                camera.orthographicSize = 35f;
                Capture(camera, name + "-near-spawn", 1280, 720);
                map.Dispose();
                map = null;
            }
            File.WriteAllText(Path.Combine(OutputDirectory, "capture-status.txt"),
                "已用正式 FestivalRegionMap.Build 输出 6 个外观的全景与出生点近景，共 12 张。\n" +
                "美术源为原生 1254×1254；近景为游戏相机放大采样，不代表新增高清细节。\n" +
                "这是编辑器构建验收，服务器传送与行走由 FestivalRegionAutoVerify 独立验收。\n");
            Debug.Log("FESTIVAL_REGION_MAP_CAPTURE_OK|12 screenshots|" + OutputDirectory);
        }
        finally
        {
            map?.Dispose();
            RenderTexture.active = previousTarget;
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void Capture(Camera camera, string name, int width, int height)
    {
        RenderTexture target = null;
        Texture2D pixels = null;
        var previous = RenderTexture.active;
        try
        {
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create();
            camera.targetTexture = target;
            camera.aspect = width / (float)height;
            camera.Render();
            RenderTexture.active = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            pixels.Apply(false, false);
            File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
        }
    }
}
#endif