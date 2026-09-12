#if UNITY_EDITOR
using System;
using System.IO;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Tweening;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Editor-only visual fixtures for production uGUI controls. No gameplay owner, session,
/// account, transport, event subscriber, or live data is created by this capture.
/// Fixture IDs, inventory counts, mission progress and festival names are not game configuration.
/// </summary>
public static class GameplayUiVerification
{
    public static string OutputDirectory { get; set; } = Path.GetFullPath(Path.Combine(
        Application.dataPath, "../.codex-artifacts/gameplay-ui-20260911"));

    [MenuItem("MMORPG/UI/Capture gameplay screens")]
    public static void CaptureAll()
    {
        Capture(2560, 1080);
        Capture(1920, 1080);
        File.WriteAllText(Path.Combine(OutputDirectory, "capture-status.txt"),
            "Completed 26 native uGUI screenshots: thirteen states at each resolution.\n" +
            "Editor fixtures only; no live account, server request, inventory mutation or activity schedule.\n");
    }

    public static void Capture(int width, int height)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Gameplay visual capture requires Edit Mode.");
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        Directory.CreateDirectory(OutputDirectory);
        File.WriteAllText(Path.Combine(OutputDirectory, "fixture-scope.txt"),
            "These screenshots render the production GameplayWindow in an isolated additive editor scene.\n" +
            "All item IDs, display names, quantities, currency and mission progress are visual fixtures.\n" +
            "The three festival names are unscheduled visual examples, not published operating events.\n" +
            "No GameplayUiRoot, AppBootstrap, session, game client or network transport is instantiated.\n");
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D pixels = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("GameplayFixtureCamera").AddComponent<Camera>();
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
            RealtimeTween.BeginEditorCapture();

            var canvasObject = new GameObject("GameplayFixtureCanvas", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 2f;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var root = QdaoUguiFactory.CreateCenteredRect("FixtureDesignRoot", canvasObject.transform, 2560, 1080);
            QdaoUguiFactory.CreateImage("SanctuaryBackdrop", root, 0, 0, 2560, 1080,
                QdaoRefreshArt.Load("sanctuary_background"));
            // This is a plain C# view. Its request events have no subscribers.
            var window = new GameplayWindow(root);
            var fixtureLabel = QdaoUguiFactory.CreateText("EditorFixtureLabel", root, 44, 1030, 1800, 34,
                "编辑器视觉样例 · 非线上角色数据与活动配置", 24, new Color(.97f, .93f, .81f),
                TextAlignmentOptions.MidlineLeft);
            fixtureLabel.raycastTarget = false;

            void Shoot(string name)
            {
                RealtimeTween.AdvanceEditorCapture(1f);
                foreach (var go in scene.GetRootGameObjects())
                {
                    foreach (var child in go.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
                    foreach (var childCanvas in go.GetComponentsInChildren<Canvas>(true))
                    {
                        childCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                        childCanvas.worldCamera = camera;
                        childCanvas.planeDistance = 2f;
                    }
                }
                Canvas.ForceUpdateCanvases();
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var label in go.GetComponentsInChildren<TMP_Text>(true)) label.ForceMeshUpdate(true, true);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(OutputDirectory, name + "_" + width + "x" + height + ".png"),
                    pixels.EncodeToPNG());
            }

            window.SetBag(BagFixture(), false, null, false);
            window.Show(GameplayPage.Bag); Shoot("01-bag-filled");
            window.SetMissions(MissionFixture(), false, null);
            window.Show(GameplayPage.Missions); Shoot("02-missions-filled");
            window.SetActivities(ActivityFixture(), false, null);
            window.Show(GameplayPage.Activities); Shoot("03-activities-filled");

            window.SetBag(EmptyBagFixture(), false, null, false);
            window.Show(GameplayPage.Bag); Shoot("04-bag-empty");
            window.SetMissions(new GetMissionListResponse(), false, null);
            window.Show(GameplayPage.Missions); Shoot("05-missions-empty");
            window.SetActivities(new GetActivityListResponse(), false, null);
            window.Show(GameplayPage.Activities); Shoot("06-activities-empty");
            window.SetBag(null, false, "Editor fixture: deliberate synchronization failure.", false);
            window.Show(GameplayPage.Bag); Shoot("07-bag-error");
            window.SetBag(null, true, null, false);
            window.Show(GameplayPage.Bag); Shoot("08-bag-loading");
            var actionFixture = new GetMissionListResponse { StatePersistent = true };
            var actionMission = MissionFixture().Missions[0].Clone();
            actionMission.Status = PlayerMissionStatus.PlayerMissionNotAccepted;
            actionMission.CanAccept = true; actionMission.RewardId = 990001;
            actionFixture.Missions.Add(actionMission);
            window.SetMissions(actionFixture, false, null);
            window.Show(GameplayPage.Missions); Shoot("09-mission-accept");
            actionMission.Status = PlayerMissionStatus.PlayerMissionClaimable;
            actionMission.CanAccept = false; actionMission.CanClaim = true;
            foreach (var objective in actionMission.Objectives) { objective.Progress = objective.Target; objective.Completed = true; }
            window.SetMissions(actionFixture, false, null); Shoot("10-mission-claim");
            window.SetMissions(actionFixture, false, null, true); Shoot("11-mission-busy");
            window.SetMissions(actionFixture, false, "行囊已满，请整理后重试"); Shoot("12-mission-error");
            var scheduleFixture = ActivityFixture();
            var openActivity = scheduleFixture.Activities[0];
            openActivity.MissionId = 930001; openActivity.Status = PlayerActivityStatus.PlayerActivityOpen;
            openActivity.CanParticipate = true; openActivity.UnavailableReason = "";
            openActivity.StartsAtMs = 1790956800000; openActivity.EndsAtMs = 1791561600000;
            openActivity.Description = "服务器时间展示样例；日期仅用于检查布局，不是正式活动排期。";
            window.SetMissions(new GetMissionListResponse(), false, null);
            window.SetActivities(scheduleFixture, false, null);
            window.Show(GameplayPage.Activities); Shoot("13-activity-participate");
            Debug.Log("GAMEPLAY_UI_CAPTURE_OK|" + width + "x" + height + "|13 fixtures|" + OutputDirectory);
        }
        finally
        {
            RealtimeTween.EndEditorCapture();
            RenderTexture.active = previousTarget;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static BagInfo EmptyBagFixture()
    {
        var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = 0, Capacity = 56, CanSort = true }, Currency = new CurrencyComp() };
        bag.Currency.Values.Add(new ulong[] { 0, 0, 0 });
        return bag;
    }

    private static BagInfo BagFixture()
    {
        var bag = EmptyBagFixture();
        bag.Currency.Values.Clear();
        bag.Currency.Values.Add(new ulong[] { 128640, 280, 1680 });
        string[] icons = {
            "001_daoist_saber", "007_flying_sword", "012_peachwood_sword", "013_daoist_whisk",
            "018_medicine_gourd_flask", "019_lotus_lamp", "036_bagua_mirror", "037_luopan_compass",
            "038_talisman_scroll", "039_cinnabar_seal", "040_alchemy_furnace_golden_pills", "042_spirit_pearl",
            "043_jade_pendant", "068_queen_mother_peach", "069_peach_basket", "071_scripture_scroll",
            "089_immortal_peach_branch", "094_moon_disc"
        };
        string[] names = {
            "青云道刀", "流光飞剑", "桃木灵剑", "清风拂尘", "纳灵葫芦", "莲心花灯",
            "八卦宝镜", "观星罗盘", "朱砂符卷", "辟邪印", "丹炉", "凝光灵珠",
            "青玉佩", "蟠桃", "桃香竹篮", "道藏残卷", "迎春桃枝", "桂月玉盘"
        };
        for (uint i = 0; i < icons.Length; i++)
        {
            ulong id = 900000UL + i;
            bag.Items.Add(new BagItemInfo {
                ItemId = id, ConfigId = 900000 + i, Name = names[i], Count = i < 4 ? 1U : 3U + i * 2,
                MaxStack = i < 4 ? 1U : 99U, EquipKind = i < 4 ? 1U : 0U,
                Description = i == 0 ? "玉绿与暖金相映，云纹细刻其上。\n此为编辑器美术验收样例。" : "仅用于界面排版与图标显示的视觉样例。",
                IconKey = "UI/qdao_v3/icons_weapon/" + icons[i]
            });
            bag.Layout.Slots.Add(new BagSlotInfo { Slot = i, ItemId = id, Width = 1, Height = 1 });
        }
        return bag;
    }

    private static GetMissionListResponse MissionFixture()
    {
        var result = new GetMissionListResponse { StatePersistent = false };
        string[] names = { "初入仙山", "桃林访友", "灯下问道", "采露凝珠", "云间寻踪", "修习五行" };
        PlayerMissionStatus[] statuses = { PlayerMissionStatus.PlayerMissionActive, PlayerMissionStatus.PlayerMissionActive,
            PlayerMissionStatus.PlayerMissionCompleted, PlayerMissionStatus.PlayerMissionClaimable,
            PlayerMissionStatus.PlayerMissionNotAccepted, PlayerMissionStatus.PlayerMissionFailed };
        for (uint i = 0; i < names.Length; ++i)
        {
            var mission = new PlayerMissionInfo {
                MissionId = 910000 + i, Name = names[i], Status = statuses[i], Configured = true,
                Description = "循着山间清风，记录修行途中每一点收获。\n此处目标与进度仅用于编辑器视觉验收。"
            };
            bool completed = i == 2 || i == 3;
            mission.Objectives.Add(new MissionObjectiveInfo { ObjectiveIndex = 0, Description = "记录沿途的修行见闻", Progress = completed ? 5U : i == 0 ? 3U : i == 5 ? 2U : i, Target = 5, Completed = completed });
            mission.Objectives.Add(new MissionObjectiveInfo { ObjectiveIndex = 1, Description = "整理道法札记", Progress = completed || i == 0 ? 1U : 0U, Target = 1, Completed = completed || i == 0 });
            mission.Objectives.Add(new MissionObjectiveInfo { ObjectiveIndex = 2, Description = "完成今日修习", Progress = completed ? 3U : 0U, Target = 3, Completed = completed });
            result.Missions.Add(mission);
        }
        return result;
    }

    private static GetActivityListResponse ActivityFixture()
    {
        var result = new GetActivityListResponse();
        string[] names = { "迎春祈福", "元宵灯会", "中秋赏月" };
        string[] icons = { "tassel", "lantern", "moon_rabbit" };
        for (uint i = 0; i < names.Length; ++i)
            result.Activities.Add(new PlayerActivityInfo {
                ActivityId = 920000 + i, Name = names[i], IconKey = "UI/Ugui/GameplayV1/" + icons[i],
                Status = PlayerActivityStatus.PlayerActivityUnscheduled, StartsAtMs = 0, EndsAtMs = 0,
                CanParticipate = false, UnavailableReason = "视觉样例，未配置运营排期。",
                Description = "灯火与月色相伴，静候道友相聚。本卡为未排期的节庆视觉样例。"
            });
        return result;
    }
}
#endif
