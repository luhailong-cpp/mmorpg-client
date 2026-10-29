#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Gameplay;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>正式活动控件的隔离布局夹具；没有网络、账号或资产写入。</summary>
public static class ActivityCenterUiVerification
{
    public static string OutputDirectory { get; set; } = Path.GetFullPath(Path.Combine(Application.dataPath, "../.codex-artifacts/activity-ui-20261022"));

    [MenuItem("MMORPG/UI/活动中心/导出原生验证截图")]
    public static void CaptureAll()
    {
        foreach (int width in new[] { 2560, 1920 }) Capture(width, 1080);
        File.WriteAllText(Path.Combine(OutputDirectory, "capture-status.json"),
            "{\"nativeControlsRendered\":true,\"resolutions\":[\"2560x1080\",\"1920x1080\"],\"screenshots\":20,\"baseStateScreenshots\":18,\"descriptionScrollEndScreenshots\":2,\"editorFixturesOnly\":true,\"liveServerVerified\":false}");
    }

    private static void Capture(int width, int height)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("活动截图必须在Edit Mode执行");
        Directory.CreateDirectory(OutputDirectory);
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewPreviewScene();
        var previousTarget = RenderTexture.active;
        RenderTexture target = null; Texture2D pixels = null;
        GameplayWindow window = null;
        try
        {
            var camera = new GameObject("ActivityFixtureCamera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, scene); camera.scene = scene;
            camera.enabled = false; camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
            camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = QdaoUguiTheme.Letterbox;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create(); camera.targetTexture = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            var canvasObject = new GameObject("ActivityFixtureCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 2;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var root = QdaoUguiFactory.CreateCenteredRect("ActivityFixtureRoot", canvasObject.transform, 2560, 1080);
            var storage = new Dictionary<string, string>();
            window = new GameplayWindow(root, activityPreferences: new ActivityPreferences(
                key => storage.TryGetValue(key, out var value) ? value : string.Empty, (key, value) => storage[key] = value));
            window.SetActivityContext(9900001);
            var missions = new GetMissionListResponse();
            missions.Missions.Add(new PlayerMissionInfo { MissionId = 980001, Scope = 5, Name = "布局样例·任务", Status = (PlayerMissionStatus)1 });
            window.SetMissions(missions, false, null);
            window.ActivityRewardClaimRequested += (_, _, _, _) => { };
            QdaoUguiFactory.CreateText("ActivityFixtureDisclosure", root, 64, 1040, 2400, 30,
                "活动中心原生控件布局夹具 · 非正式运营配置、真实角色或联网验收", 22, new Color(.98f, .94f, .84f));

            void Shoot(string state)
            {
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var child in go.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
                Canvas.ForceUpdateCanvases();
                foreach (var label in root.GetComponentsInChildren<TMP_Text>(true)) label.ForceMeshUpdate(true, true);
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(OutputDirectory, state + "_" + width + "x" + height + ".png"), pixels.EncodeToPNG());
            }
            void Click(string name)
            {
                var button = root.GetComponentsInChildren<Button>().Single(b => b.name == name);
                if (!button.interactable) throw new InvalidOperationException("夹具按钮不可用：" + name);
                button.onClick.Invoke();
            }

            void ChooseTool(string name)
            {
                if (!root.GetComponentsInChildren<Button>().Any(b => b.name == name)) Click("ActivityTools");
                Click(name);
            }

            var activities = Fixture();
            window.SetActivities(activities, false, null); window.Show(GameplayPage.Activities); Shoot("01-catalog");
            Click("ActivityTools"); Shoot("01b-tools"); Click("CloseActivityTools");
            ChooseTool("ActivityCategory_1"); ChooseTool("ActivityRewardFilter_4"); Shoot("02-filtered");
            ChooseTool("ClearActivityFilters"); Click("ActivityDetails_1"); Shoot("03-long-details");
            var description = root.GetComponentsInChildren<ScrollRect>().Single(s => s.name == "DescriptionScroll");
            if (description.content.rect.height <= description.viewport.rect.height)
                throw new InvalidOperationException("长活动说明必须提供真实垂直滚动区域");
            description.verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases(); Shoot("03b-long-details-scroll-end"); window.Back();
            Click("ActivityVitalityChest_21"); Shoot("04-reward-confirmation"); window.Back();
            activities.Vitality.Rewards[0].Claimed = true; activities.Vitality.Rewards[0].Durable = false;
            window.SetActivities(activities, false, null, false, "领取确认中，刷新将恢复原请求"); Shoot("05-pending");
            window.SetActivities(new GetActivityListResponse { Vitality = new PlayerActivityVitalitySnapshot { UnavailableReason = "正式活跃规则尚未配置" } }, false, null); Shoot("06-empty");
            window.SetActivities(null, true, null); Shoot("07-loading");
            window.SetActivities(null, false, "活动读取失败，请刷新重试", false, "原领取记录保留，刷新后继续确认"); Shoot("08-error");
        }
        finally
        {
            window?.Dispose(); RenderTexture.active = previousTarget;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static GetActivityListResponse Fixture()
    {
        var response = new GetActivityListResponse { ServerTimeMs = 1792627200000 };
        for (uint i = 1; i <= 8; ++i)
        {
            var activity = new PlayerActivityInfo { ActivityId = i, DefinitionKey = "layout-only-" + i, ConfigurationRevision = 1,
                MissionId = 980001, MissionScope = 5, EntryKind = (PlayerActivityEntryKind)1, Category = (PlayerActivityCategory)(i % 4 + 1),
                Name = "布局样例·活动" + i, Description = string.Concat(Enumerable.Repeat("本说明仅用于检查长文本滚动、奖励明细与入口可达性，不是正式玩法或运营安排。", 12)) + "\n【长说明末尾：完整可达】",
                Status = (PlayerActivityStatus)(i % 4), CanParticipate = i % 4 == 2, StartsAtMs = 1792627200000, EndsAtMs = 1792713600000,
                CompletionCountKnown = true, CompletedCount = i % 3, CompletionLimit = i + 3, CompletionPeriod = "布局样例周期", VitalityPoints = i + 1 };
            activity.RewardTypes.Add((PlayerActivityRewardType)3); activity.RewardTypes.Add((PlayerActivityRewardType)4);
            activity.Rewards.Add(new PlayerActivityRewardItem { ItemConfigId = 990001, Name = "布局样例灵符", Count = i + 1, BindingDescription = "绑定说明展示样例" });
            response.Activities.Add(activity);
        }
        response.Activities[0].Category = (PlayerActivityCategory)1;
        response.Vitality = new PlayerActivityVitalitySnapshot { Available = true, RuleId = 990001, ConfigurationRevision = 1,
            PeriodId = 1792627200000, PeriodStartsAtMs = 1792627200000, PeriodEndsAtMs = 1792713600000, Points = 37, PointsLimit = 120 };
        foreach (uint i in new uint[] { 21, 47, 83 })
        {
            var reward = new PlayerActivityVitalityReward { ThresholdId = i, RequiredPoints = i, CanClaim = i <= 37 };
            reward.Items.Add(new PlayerActivityRewardItem { ItemConfigId = 990002, Name = "布局样例宝箱", Count = 2 });
            response.Vitality.Rewards.Add(reward);
        }
        return response;
    }
}
#endif
