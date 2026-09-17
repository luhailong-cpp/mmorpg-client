#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MmorpgClient.Game.Social;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Social;
using MmorpgClient.World.Tianyong;
using UnityEngine.EventSystems;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Captures and exercises the native social window in a temporary offline scene.</summary>
public static class SocialUiVerification
{
    public static string OutputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../image/designs/social-ui-v1/unity-slices/qa"));
    [Serializable] private sealed class Report
    {
        public string status = "passed", unityVersion, completedAtUtc;
        public bool liveServerVerified = false;
        public string[] screenshots, checks;
    }
    private static readonly List<string> Shots = new();
    private static readonly List<string> Checks = new();

    [MenuItem("MMORPG/UI/Social/Capture and verify native windows")]
    public static void CaptureAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Stable Edit mode is required.");
        Directory.CreateDirectory(OutputDirectory);
        Shots.Clear(); Checks.Clear();
        Capture(2560, 1080);
        Capture(1920, 1080);
        File.WriteAllText(Path.Combine(OutputDirectory, "native-capture.json"), JsonUtility.ToJson(
            new Report { unityVersion = Application.unityVersion, completedAtUtc = DateTime.UtcNow.ToString("O"),
                screenshots = Shots.ToArray(), checks = Checks.ToArray() }, true));
        Debug.Log("SOCIAL_NATIVE_CAPTURE_OK|screenshots=" + Shots.Count + "|checks=" + Checks.Count);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Social native QA: " + message);
        Checks.Add(message);
    }

    private static void Capture(int width, int height)
    {
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewPreviewScene();
        var previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D pixels = null;
        SocialWindow window = null;
        try
        {
            var camera = new GameObject("SocialCaptureCamera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
            camera.scene = scene;
            camera.enabled = false;
            camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = QdaoUguiTheme.Letterbox;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create(); camera.targetTexture = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            var canvasObject = new GameObject("SocialCaptureCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 2;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("SocialCaptureDesign", canvasObject.transform, 2560, 1080);
            QdaoUguiFactory.CreateImage("Backdrop", design, 0, 0, 2560, 1080, QdaoUguiTheme.RequireSprite("UI/Ugui/SocialV1/main-city"));
            if (EventSystem.current == null)
            {
                var events = new GameObject("SocialVerificationEvents", typeof(EventSystem), typeof(StandaloneInputModule));
                SceneManager.MoveGameObjectToScene(events, scene);
            }
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var state = new SocialState(preview: true, clock: () => nowMs);
            window = new SocialWindow(design, state);
            QdaoUguiFactory.CreateText("OfflineFixtureBadge", design, 300, 1035, 1960, 42,
                "离线界面验收 · 示例角色与消息", 24, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);

            void Shoot(string name)
            {
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var child in root.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
                Canvas.ForceUpdateCanvases();
                foreach (var text in canvasObject.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
                Canvas.ForceUpdateCanvases();
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); pixels.Apply(false, false);
                string filename = name + "_" + width + "x" + height + ".png";
                File.WriteAllBytes(Path.Combine(OutputDirectory, filename), pixels.EncodeToPNG());
                Shots.Add(filename);
                Require(window.IsVisible, width + ": window visible for " + name);
            }

            Button FindButton(string name) => canvasObject.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name && b.gameObject.activeInHierarchy);
            TMP_InputField FindInput(string name) => canvasObject.GetComponentsInChildren<TMP_InputField>(true).FirstOrDefault(i => i.name == name && i.gameObject.activeInHierarchy);
            void Click(string name)
            {
                var button = FindButton(name);
                Require(button != null && button.IsInteractable(), width + ": clickable " + name);
                EventSystem.current?.SetSelectedGameObject(button.gameObject);
                button.onClick.Invoke();
            }
            void Type(string name, string value)
            {
                var input = FindInput(name);
                Require(input != null && input.interactable, width + ": editable " + name);
                input.text = value;
            }
            ScrollRect ChatScroll() => canvasObject.GetComponentsInChildren<ScrollRect>().First(s => s.name == "SocialChatScroll");
            bool hadBlocker = GameplayInputGate.IsKeyboardBlocked;
            window.Show(SocialPage.Groups); Shoot("01-groups");
            Require(GameplayInputGate.IsKeyboardBlocked, width + ": visible window blocks world input");
            bool pin = state.SelectedGroup.Pinned;
            Click("SocialGroupPin"); Require(state.SelectedGroup.Pinned != pin, width + ": pin click changes selected group");
            Click("SocialGroupPin");
            bool alerts = state.SelectedGroup.Alerts;
            Click("SocialGroupAlerts"); Require(state.SelectedGroup.Alerts != alerts, width + ": alert click changes selected group");
            Click("SocialGroupAlerts");

            Click("SocialGroupEdit");
            Require(window.HasModal, width + ": group editor opens");
            Type("SocialEditAnnouncement", "今夜同游青岚竹海，道友们记得带上回春丹。");
            Shoot("04-group-edit");
            Click("SocialEditConfirm");
            Require(!window.HasModal && state.SelectedGroup.Announcement.Contains("回春丹"), width + ": group editor saves through native control");

            int memberCount = state.SelectedGroup.Members.Count;
            Click("SocialGroupInvite");
            Require(FindButton("SocialInviteSelect_1") == null && FindButton("SocialInviteSelect_2") == null, width + ": invite excludes current members");
            Click("SocialInviteSelect_5"); Shoot("05-group-invite");
            Click("SocialInviteConfirm");
            Require(!window.HasModal && state.SelectedGroup.Members.Count == memberCount + 1 && state.SelectedGroup.Members.Contains(5UL),
                width + ": invitation updates selected group once");

            int groupCount = state.Groups.Count;
            Click("SocialGroupCreate");
            Type("SocialCreateName", "月下听风");
            Shoot("06-group-create");
            Click("SocialCreateConfirm");
            Require(state.Groups.Count == groupCount + 1 && state.SelectedGroup.Name == "月下听风", width + ": create selects the new group");
            Click("SocialGroupLeave"); window.Back();
            Require(!window.HasModal && state.Groups.Count == groupCount + 1, width + ": cancelling leave retains group");
            Click("SocialGroupLeave"); Click("SocialLeaveConfirm");
            Require(state.Groups.Count == groupCount, width + ": confirmed leave removes the local group");
            state.SelectGroup(101);
            Type("SocialComposer", "收好葫芦再出发");
            Click("SocialTab_World"); Shoot("02-world");
            Click("SocialTab_Groups");
            Require(FindInput("SocialComposer").text == "收好葫芦再出发", width + ": group draft survives page navigation");

            Click("SocialTab_World");
            Type("SocialComposer", "   ");
            Require(!FindButton("SocialSend").interactable, width + ": blank message cannot be sent");
            Type("SocialComposer", new string('道', 121));
            Require(!FindButton("SocialSend").interactable, width + ": overlong message cannot be sent");
            Type("SocialComposer", "道友好，月下同游。");
            int messages = state.Messages(SocialChannel.World).Count;
            Click("SocialSend");
            Require(state.Messages(SocialChannel.World).Count == messages + 1 && FindInput("SocialComposer").text == "",
                width + ": native send appends one local message and clears draft");
            Require(!FindButton("SocialSend").interactable, width + ": empty repeat send is disabled");
            Type("SocialComposer", "世界频道草稿");
            Click("SocialChannel_Team");
            Type("SocialComposer", "队伍频道草稿");
            Click("SocialChannel_World");
            Require(FindInput("SocialComposer").text == "世界频道草稿", width + ": channel drafts remain separate");
            Type("SocialComposer", "");
            Canvas.ForceUpdateCanvases();
            ChatScroll().verticalNormalizedPosition = .8f;
            float beforeY = ChatScroll().content.anchoredPosition.y;
            state.PreviewIncoming();
            Canvas.ForceUpdateCanvases();
            Require(Mathf.Abs(ChatScroll().content.anchoredPosition.y - beforeY) < 1f, width + ": incoming message preserves reading pixel offset");
            Click("SocialLatest");
            Require(ChatScroll().verticalNormalizedPosition < .01f, width + ": new message control returns to latest");

            Click("SocialEmoji");
            Click("SocialEmote_0");
            Require(FindInput("SocialComposer").text.Contains("[作揖]"), width + ": emoji picker inserts native text");
            Type("SocialComposer", "");
            Click("SocialShare"); Click("SocialShareCoordinates");
            Require(state.Messages(SocialChannel.World).Last().Card == "bamboo", width + ": local coordinate card is added");
            Click("SocialCard_" + state.Messages(SocialChannel.World).Last().Id);
            Require(window.HasModal, width + ": share card opens detail");
            window.Back(); Require(window.IsVisible && !window.HasModal, width + ": Back closes only the nested dialog");

            Click("SocialTab_Rumor"); Shoot("03-rumor");
            Require(FindInput("SocialComposer") == null, width + ": rumor has no active message composer");
            Click("SocialRumorFilter_Beast");
            Require(state.FilteredRumors().Count() == 2, width + ": rumor category filters rows");
            Type("SocialRumorSearch", "不存在的传闻");
            Require(!state.FilteredRumors().Any(), width + ": rumor search empty state");
            Click("SocialRumorClear");
            Require(state.FilteredRumors().Count() == 5, width + ": clear search restores all sample rumors");
            Click("SocialRumorDetail_r1"); Shoot("07-rumor-detail");
            Require(window.HasModal, width + ": rumor detail opens");
            nowMs += 180001; window.Tick();
            var timer = canvasObject.GetComponentsInChildren<TMP_Text>().First(t => t.name == "SocialRumorModalCountdown");
            Require(timer.text == state.Rumors.First(r => r.Id == "r1").StateLabel(nowMs) && !timer.text.Contains("02:"),
                width + ": open rumor detail updates when countdown ends");
            window.Back();
            window.Hide(); Require(!window.IsVisible, width + ": window closes");
            Require(GameplayInputGate.IsKeyboardBlocked == hadBlocker, width + ": close restores world input gate");

            var retired = window.Root.gameObject;
            window.Dispose(); UnityEngine.Object.DestroyImmediate(retired);
            var production = new SocialState();
            window = new SocialWindow(design, production);
            window.Show(SocialPage.Groups); Shoot("08-production-groups");
            Require(production.Groups.Count == 0 && !FindButton("SocialGroupCreate").interactable, width + ": production does not load fake groups");
            window.Show(SocialPage.World);
            Require(production.Messages(SocialChannel.World).Count == 0 && !FindButton("SocialSend").interactable,
                width + ": production cannot send before a session is ready");
            window.Show(SocialPage.Rumor); Shoot("09-production-rumor");
            Require(production.Rumors.Count == 0 && FindInput("SocialComposer") == null,
                width + ": production rumor remains empty and read only");
            window.Hide();
        }
        finally
        {
            window?.Dispose();
            RenderTexture.active = previousTarget;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [MenuItem("MMORPG/UI/Social/Build offline preview scene and prefab")]
    public static void BuildPreviewAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Stable Edit mode is required.");
        const string scenePath = "Assets/Scenes/SocialPreview.unity";
        const string prefabPath = "Assets/Prefabs/UI/SocialOfflinePreview.prefab";
        Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
        Directory.CreateDirectory(Path.GetDirectoryName(prefabPath));
        if (File.Exists(scenePath) || File.Exists(prefabPath))
            throw new InvalidOperationException("Offline preview assets already exist; use them without overwriting local changes.");
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var host = new GameObject("Social Offline Preview", typeof(SocialPreviewHost));
            PrefabUtility.SaveAsPrefabAsset(host, prefabPath);
            if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("Could not save social preview scene.");
        }
        finally
        {
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
        AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("SOCIAL_PREVIEW_ASSETS_OK|" + scenePath);
    }
}
#endif
