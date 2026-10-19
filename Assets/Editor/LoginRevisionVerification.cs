#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MmorpgClient.Net;
using MmorpgClient.UI;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Tweening;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Eight additional production-prefab views. Inactive fixture owner; no Initialize, session login or network calls.</summary>
public static class LoginRevisionVerification
{
    public static string OutputDirectory = "E:/work/image/docs/ui-fidelity-20261016/native/login-revision";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("MMORPG/UI/Capture login revision utilities")]
    public static void CaptureAll()
    {
        Capture(2560, 1080); Capture(1920, 1080);
        File.WriteAllText(Path.Combine(OutputDirectory, "verification.txt"),
            "PASS: 8 native production-prefab captures: announcement content/empty, settings and long account/masked password at 2 resolutions.\n" +
            "Inactive AppBootstrap fixture with injected Session only; no Initialize, live server, login or account mutation.\n");
    }

    public static void Capture(int width, int height)
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Requires Edit Mode.");
        Directory.CreateDirectory(OutputDirectory);
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var previousTarget = RenderTexture.active;
        RenderTexture target = null; Texture2D pixels = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("LoginRevisionCamera").AddComponent<Camera>();
            camera.enabled = false; camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
            camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = QdaoUguiTheme.Letterbox;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create(); camera.targetTexture = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            RealtimeTween.BeginEditorCapture();
            var co = new GameObject("LoginRevisionCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = co.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 2;
            var scaler = co.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var root = QdaoUguiFactory.CreateCenteredRect("LoginRevisionDesignRoot", co.transform, 2560, 1080);
            var prefab = Resources.Load<GameObject>("UI/Ugui/Prefabs/QdaoServerSelect");
            Require(prefab != null, "Production QdaoServerSelect prefab is required.");
            var page = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            var view = page.GetComponent<QdaoServerSelectView>();
            view.PrepareForPreview();
            var owner = new GameObject("InactiveLoginRevisionOwner"); owner.SetActive(false);
            var app = owner.AddComponent<AppBootstrap>(); var session = new SessionModel();
            typeof(AppBootstrap).GetField("<Session>k__BackingField", PrivateInstance).SetValue(app, session);
            typeof(QdaoServerSelectView).GetField("_app", PrivateInstance).SetValue(view, app);
            typeof(QdaoServerSelectView).GetMethod("BindEvents", PrivateInstance).Invoke(view, null);
            view.ShowLanding(true);

            T Find<T>(string name) where T : Component => page.GetComponentsInChildren<T>(true)
                .Single(item => item.name == name && item.gameObject.activeInHierarchy);
            void Click(string name)
            {
                var button = Find<Button>(name); Require(button.interactable, name + " must be clickable.");
                button.onClick.Invoke();
            }
            void Shoot(string name)
            {
                RealtimeTween.AdvanceEditorCapture(1f);
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var child in go.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
                Canvas.ForceUpdateCanvases();
                foreach (var label in page.GetComponentsInChildren<TMP_Text>(true)) label.ForceMeshUpdate(true, true);
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(OutputDirectory, name + "_" + width + "x" + height + ".png"), pixels.EncodeToPNG());
            }

            session.Announcements.Clear();
            session.Announcements.Add(new AnnouncementItem { id = 1, title = "公告摄影样例", content =
                "这是编辑器布局样例，不是正式运营公告。\n道友可在此阅读更新说明与活动安排；完整内容以服务器发布为准。\n" +
                "较长正文用于核对换行、行距及阅读区域。云山灯火相伴，愿道友修行顺遂。" });
            Click("LandingAnnouncement"); Find<RectTransform>("AnnouncementPanel");
            Require(Find<TMP_Text>("AnnouncementBody").text.Contains("公告摄影样例") ||
                Find<TMP_Text>("AnnouncementBody").text.Contains("这是编辑器布局样例"), "Announcement must use the injected session content.");
            Shoot("01-announcement-content"); Click("AnnouncementClose");
            session.Announcements.Clear(); Click("LandingAnnouncement");
            var empty = Find<TMP_Text>("AnnouncementBody");
            Require(!string.IsNullOrWhiteSpace(empty.text) && !empty.text.Contains("这是编辑器布局样例"), "Empty announcement must show its production empty state.");
            Shoot("02-announcement-empty"); Click("AnnouncementClose");
            Click("LandingSettings"); Find<RectTransform>("SettingsPanel");
            Find<Slider>("SettingsVolumeSlider"); Find<Toggle>("SettingsFullscreen");
            Shoot("03-settings"); Click("SettingsClose");
            view.ShowCredentialForPreview();
            var inputs = page.GetComponentsInChildren<TMP_InputField>().ToArray();
            var password = inputs.Single(input => input.inputType == TMP_InputField.InputType.Password || input.contentType == TMP_InputField.ContentType.Password);
            var account = inputs.Single(input => input != password);
            const string secret = "PreviewOnly_1234567890_MaskedPassword";
            account.SetTextWithoutNotify("preview_long_account_abcdefghijklmnopqrstuvwxyz_0123456789");
            password.SetTextWithoutNotify(secret); account.ForceLabelUpdate(); password.ForceLabelUpdate();
            Require(password.text.Length > 0 && !password.textComponent.text.Contains(password.text), "Password label must remain masked.");
            Shoot("04-account-long-masked");
            Debug.Log("LOGIN_REVISION_CAPTURE_OK|" + width + "x" + height + "|4 fixtures|" + OutputDirectory);
        }
        finally
        {
            RealtimeTween.EndEditorCapture(); RenderTexture.active = previousTarget;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
#endif
