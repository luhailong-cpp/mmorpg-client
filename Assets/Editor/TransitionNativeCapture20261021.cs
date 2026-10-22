#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Tweening;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Disconnected, deterministic render of the actual BattlePresenter.PlayEntrance path.</summary>
public static class TransitionNativeCapture20261021
{
    public static string OutputDirectory = "E:/work/image/qdao_ui_redesign_v5/exact-hud-battle-20261021/transition-harness/native";
    public static bool IsRunning { get; private set; }
    public static string Status { get; private set; } = "not run";
    private const int CaptureLayer = 31;
    private static readonly float[] Times = { 0f, .2f, .5f, .65f, .8f, 1.3f };

    [Serializable] private sealed class Frame
    {
        public string path;
        public int width, height;
        public float seconds, illustrationAlpha, cloudAlpha, cloudX;
        public bool illustrationAlive, cloudAlive, illustrationBlocksInput, cloudBlocksInput;
    }
    [Serializable] private sealed class Asset
    {
        public string resource, path, sha256;
    }
    [Serializable] private sealed class Report
    {
        public string capturedAtUtc, status, productionEntryPoint;
        public bool nativeCaptureRun, fullVisualAcceptance, liveServerVerified;
        public Frame[] frames;
        public Asset[] assets;
    }

    [MenuItem("Tools/Five Elements/Capture Production Entrance 20261021")]
    public static void CaptureAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Transition capture requires Edit Mode and never changes Play Mode.");
        if (IsRunning) throw new InvalidOperationException("Transition capture is already running.");
        IsRunning = true;
        Status = "capturing";
        var frames = new List<Frame>();
        Directory.CreateDirectory(OutputDirectory);
        try
        {
            CaptureResolution(2560, 1080, frames);
            CaptureResolution(1920, 1080, frames);
            Status = "captured; visual review pending";
            File.WriteAllText(Path.Combine(OutputDirectory, "capture-report.json"), JsonUtility.ToJson(new Report
            {
                capturedAtUtc = DateTime.UtcNow.ToString("O"), status = Status,
                productionEntryPoint = "BattlePresenter.PlayEntrance(Array.Empty<BattleUnitView>())",
                nativeCaptureRun = true, fullVisualAcceptance = false, liveServerVerified = false,
                frames = frames.ToArray(), assets = new[]
                {
                    DescribeAsset(BattleArtCatalog.EntryLoadingPath), DescribeAsset(BattleArtCatalog.EntryCloudsPath),
                    DescribeAsset(BattleArtCatalog.ArenaBackgroundPath),
                },
            }, true));
            Debug.Log("TRANSITION_NATIVE_CAPTURE_COMPLETE|" + OutputDirectory + "|12 frames|visual review pending");
        }
        catch (Exception exception)
        {
            Status = "failed: " + exception.Message;
            File.WriteAllText(Path.Combine(OutputDirectory, "capture-error.txt"), exception.ToString());
            throw;
        }
        finally { IsRunning = false; }
    }

    private static void CaptureResolution(int width, int height, List<Frame> frames)
    {
        var previousScene = SceneManager.GetActiveScene();
        Scene scene = default;
        GameObject root = null;
        Camera camera = null;
        RenderTexture target = null;
        Texture2D readback = null;
        BattlePresenter presenter = null;
        bool clockActive = false;
        try
        {
            // Unity cannot add a scene beside a fresh batch editor's unsaved scene.
            // Interactive captures preserve the user's current scene.
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            root = new GameObject("[TransitionNativeCapture20261021]") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(root, scene);
            var cameraObject = new GameObject("CaptureCamera");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.localPosition = new UnityEngine.Vector3(0, 0, -10);
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 5;
            camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.cullingMask = 1 << CaptureLayer;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create(); camera.targetTexture = target;
            readback = new Texture2D(width, height, TextureFormat.RGB24, false, false);

            var canvasObject = new GameObject("CaptureCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(root.transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
            canvas.planeDistance = 2; canvas.sortingOrder = 500;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referencePixelsPerUnit = 100;
            var page = QdaoUguiFactory.CreateCenteredRect("ProductionEntrancePage", canvasObject.transform, 2560, 1080);
            var arena = BattleArtCatalog.LoadSprite(BattleArtCatalog.ArenaBackgroundPath);
            if (arena == null) throw new InvalidOperationException("Production arena sprite is unavailable.");
            QdaoUguiFactory.CreateAspectFillImage("ArenaUnderEntrance", page, arena, 2560f / 1080f);
            var stage = QdaoUguiFactory.CreateRect("Stage", page, 0, 0, 2560, 1080);
            var fx = QdaoUguiFactory.CreateRect("Fx", page, 0, 0, 2560, 1080);
            var numbers = QdaoUguiFactory.CreateRect("Numbers", page, 0, 0, 2560, 1080);
            var overlay = QdaoUguiFactory.CreateRect("Overlay", page, 0, 0, 2560, 1080);
            RealtimeTween.BeginEditorCapture(); clockActive = true;
            presenter = new BattlePresenter(null, stage, fx, numbers, overlay,
                _ => null, () => Array.Empty<BattleUnitView>(), _ => string.Empty);
            presenter.PlayEntrance(Array.Empty<BattleUnitView>());
            foreach (var transform in root.GetComponentsInChildren<UnityEngine.Transform>(true)) transform.gameObject.layer = CaptureLayer;

            float age = 0;
            for (int index = 0; index < Times.Length; ++index)
            {
                float time = Times[index];
                if (time > age) RealtimeTween.AdvanceEditorCapture(time - age);
                age = time;
                Canvas.ForceUpdateCanvases();
                var frame = new Frame { width = width, height = height, seconds = time };
                var illustration = overlay.Find("EntryIllustration")?.GetComponent<Image>();
                var clouds = overlay.Find("EntryClouds")?.GetComponent<Image>();
                frame.illustrationAlive = illustration != null; frame.cloudAlive = clouds != null;
                frame.illustrationAlpha = illustration == null ? 0 : illustration.color.a;
                frame.cloudAlpha = clouds == null ? 0 : clouds.color.a;
                frame.cloudX = clouds == null ? 0 : clouds.rectTransform.anchoredPosition.x;
                frame.illustrationBlocksInput = illustration != null && illustration.raycastTarget;
                frame.cloudBlocksInput = clouds != null && clouds.raycastTarget;
                frame.path = Path.Combine(OutputDirectory, $"{index + 1:D2}-transition-t{Mathf.RoundToInt(time * 1000):D4}_{width}x{height}.png");
                var previousTarget = RenderTexture.active;
                try
                {
                    camera.Render(); RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); readback.Apply(false, false);
                    File.WriteAllBytes(frame.path, readback.EncodeToPNG());
                }
                finally { RenderTexture.active = previousTarget; }
                frames.Add(frame);
            }
        }
        finally
        {
            presenter?.Dispose();
            if (clockActive) RealtimeTween.EndEditorCapture();
            if (camera != null) camera.targetTexture = null;
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static Asset DescribeAsset(string resource)
    {
        var sprite = BattleArtCatalog.LoadSprite(resource);
        string path = AssetDatabase.GetAssetPath(sprite);
        // BattleArtCatalog also creates sprites at runtime from imported textures.
        if (string.IsNullOrEmpty(path) && sprite != null)
            path = AssetDatabase.GetAssetPath(sprite.texture);
        using (var sha = SHA256.Create())
            return new Asset { resource = resource, path = path,
                sha256 = !string.IsNullOrEmpty(path) && File.Exists(path)
                    ? string.Concat(sha.ComputeHash(File.ReadAllBytes(path)).Select(b => b.ToString("x2"))) : "unavailable" };
    }
}
#endif
