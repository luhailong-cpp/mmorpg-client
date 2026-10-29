#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Gameplay;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>正式任务内容与07/08实际挂载的离线原生截图；受控内存Transport不连接账号或网络。</summary>
public static class QuestHudVerification
{
    public static string OutputDirectory { get; set; } = Path.GetFullPath(Path.Combine(
        Application.dataPath, "../.codex-artifacts/quest-hud-20261022/native"));

    [Serializable] private sealed class Report
    {
        public string status, unityVersion, scope;
        public string[] checks, screenshots;
    }

    [MenuItem("MMORPG/UI/截取任务追踪栏离线样例")]
    public static void CaptureAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("任务栏截图必须在编辑模式执行。");
        if (WorldSideHudRoot.Instance != null || QuestHudRoot.Instance != null)
            throw new InvalidOperationException("已有07/08 HUD宿主，拒绝替换正在运行的实例。");
        Directory.CreateDirectory(OutputDirectory);
        var checks = new List<string>();
        var screenshots = new List<string>();
        var report = new Report
        {
            status = "RUNNING", unityVersion = Application.unityVersion,
            scope = "独立内容22张：正式QuestHudContent与416×582夹具；实际装配10张：正式WorldSideHudRoot与QuestHudRoot.EnsureMounted挂载。受控任务回包仅来自内存Transport，无账号、真实网络或领奖操作；任务名称数值不代表正式配置或在线验收。"
        };
        try
        {
            Capture(2560, 1080, checks, screenshots);
            Capture(1920, 1080, checks, screenshots);
            report.status = "PASS";
            Debug.Log("QUEST_HUD_VERIFICATION_PASS|" + checks.Count + "项检查|" + screenshots.Count + "张截图|" + OutputDirectory);
        }
        catch (Exception exception)
        {
            report.status = "FAIL";
            checks.Add(exception.ToString());
            throw;
        }
        finally
        {
            report.checks = checks.ToArray();
            report.screenshots = screenshots.ToArray();
            File.WriteAllText(Path.Combine(OutputDirectory, "verification.json"), JsonUtility.ToJson(report, true));
        }
    }

    private static void Capture(int width, int height, List<string> checks, List<string> screenshots)
    {
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
            Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
        var previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D pixels = null;
        QuestHudContent content = null;
        WorldSideHudRoot sharedShell = null;
        QuestHudRoot sharedHost = null;
        PlayerFeaturesClient sharedClient = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("QuestFixtureCamera").AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100f;
            camera.cullingMask = 1 << 31;
            camera.targetDisplay = Display.activeEditorGameViewTarget;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = QdaoUguiTheme.Letterbox;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create();
            camera.targetTexture = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);

            var canvasObject = new GameObject("QuestFixtureCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 2;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("QuestFixtureDesign", canvasObject.transform, 2560, 1080);
            QdaoUguiFactory.CreateImage("Sanctuary", design, 0, 0, 2560, 1080,
                QdaoRefreshArt.Load("sanctuary_background")).raycastTarget = false;
            var captionBackground = QdaoUguiFactory.CreateRect("FixtureCaptionBackground", design, 44, 52, 1060, 164);
            captionBackground.gameObject.AddComponent<Image>().color = new Color(.035f, .12f, .11f, .9f);
            QdaoUguiFactory.CreateText("FixtureTitle", captionBackground, 24, 12, 1012, 44,
                "五行奇谈 · 任务追踪栏", 34, QdaoUguiTheme.Cream).raycastTarget = false;
            var fixtureScope = QdaoUguiFactory.CreateText("FixtureScope", captionBackground, 24, 62, 1012, 34,
                "离线视觉样例 · 正式控件 · 416×582独立挂载区", 26, QdaoUguiTheme.Cream);
            fixtureScope.raycastTarget = false;
            var stateCaption = QdaoUguiFactory.CreateText("FixtureState", captionBackground, 24, 108, 1012, 34,
                "", 25, QdaoUguiTheme.Cream);
            stateCaption.raycastTarget = false;
            var frame = QdaoUguiFactory.CreateRect("QuestFixturePanel", design, 1704, 188, 440, 622);
            frame.gameObject.AddComponent<Image>().color = GameplayUiArt.Ink;
            var paper = QdaoUguiFactory.CreateRect("QuestFixturePaper", frame, 3, 3, 434, 616);
            paper.gameObject.AddComponent<Image>().color = QdaoUguiTheme.PanelPaper;
            var mount = QdaoUguiFactory.CreateRect("QuestMountFixture", frame, 12, 20, 416, 582);
            content = new QuestHudContent(mount);
            var tracking = new MissionTrackingState();
            var scroll = content.Root.GetComponentInChildren<ScrollRect>(true);

            void Refresh()
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var child in root.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
                    foreach (var surface in root.GetComponentsInChildren<Canvas>(true))
                    {
                        surface.renderMode = RenderMode.ScreenSpaceCamera;
                        surface.worldCamera = camera;
                        surface.planeDistance = 2;
                        surface.targetDisplay = camera.targetDisplay;
                    }
                }
                Canvas.ForceUpdateCanvases();
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
                Canvas.ForceUpdateCanvases();
            }

            void Set(GetMissionListResponse snapshot, bool loading = false, string error = null)
            {
                tracking.UpdateSnapshot(snapshot);
                content.SetData(snapshot, loading, error, false, tracking);
                scroll.verticalNormalizedPosition = 1;
                Refresh();
            }

            void Shoot(string name, string caption)
            {
                stateCaption.text = caption;
                Refresh();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                pixels.Apply(false, false);
                var file = name + "_" + width + "x" + height + ".png";
                File.WriteAllBytes(Path.Combine(OutputDirectory, file), pixels.EncodeToPNG());
                screenshots.Add(file);
            }

            Set(null);
            Require(HasText(content.Root, "任务尚未同步"), "未同步状态可辨认");
            Shoot("00-unsynced", "未同步：等待当前主体的真实任务快照");
            Set(null, true);
            Require(!Button(content.Root, "QuestRefresh").interactable, "加载期间刷新不可重复触发");
            Shoot("01-loading", "首次读取：加载态与空列表分开");
            Set(new GetMissionListResponse());
            Require(HasText(content.Root, "暂无进行中的任务"), "已同步的空任务有独立空态");
            Shoot("02-empty", "成功空列表：可打开任务列表查看可接任务");

            var list = ListFixture();
            Set(list);
            Require(content.Root.GetComponentsInChildren<UnityEngine.Transform>(true).Count(t => t.name.StartsWith("QuestCard_", StringComparison.Ordinal)) == 6,
                "六个任务均创建独立卡片");
            Require(scroll.vertical && !scroll.horizontal && scroll.content.rect.height > scroll.viewport.rect.height,
                "超过四个任务使用垂直滚动");
            var firstFour = Enumerable.Range(0, 4).Select(index => (RectTransform)scroll.content.GetChild(index)).ToArray();
            Require(firstFour.All(card => -card.anchoredPosition.y + card.rect.height <= scroll.viewport.rect.height + .5f),
                "四张普通单目标卡完整位于任务视口内");
            Shoot("03-list-top", "六项任务：进行中、可领取与失败使用服务器状态");
            scroll.verticalNormalizedPosition = 0;
            Refresh();
            Require(scroll.content.anchoredPosition.y > 0, "最后一张卡可滚入可视区");
            Shoot("04-list-bottom", "六项任务底部：长名单全部可达");

            var multi = MultiObjectiveFixture();
            Set(multi);
            Button(content.Root, "QuestExpand_" + MissionTrackingState.Key(multi.Missions[0])).onClick.Invoke();
            Refresh();
            foreach (var objective in multi.Missions[0].Objectives)
                Require(content.Root.GetComponentsInChildren<TMP_Text>(true).Any(t => t.name == "QuestObjective_0_108_" + objective.ObjectiveIndex),
                    "显式目标序号" + objective.ObjectiveIndex + "未被重复条件覆盖");
            Require(HasText(content.Root, "数量待确认") && !HasText(content.Root, "0/0"), "未知目标量不伪造计数");
            Shoot("05-objectives-top", "展开全部目标：重复condition_id按objective_index区分");
            scroll.verticalNormalizedPosition = 0;
            Refresh();
            Shoot("06-objectives-bottom", "全部目标底部：长文案及未知数量仍可查看");

            Set(new GetMissionListResponse { Missions = { list.Missions[5].Clone() } });
            Require(!Button(content.Root, "QuestTrack_" + MissionTrackingState.Key(list.Missions[5])).interactable,
                "失败任务不能继续追踪");
            Require(HasText(content.Root, "活动期限已结束"), "失败原因保留服务器原文");
            Shoot("07-failed", "失败：保留原因，追踪入口不可用");

            var claimable = new PlayerMissionInfo { Scope = 2, MissionId = 12, Status = (PlayerMissionStatus)3,
                MissionType = 2, Name = "修行奖励待领取（样例）", CanClaim = true, Configured = true };
            Set(new GetMissionListResponse { Missions = { claimable } });
            uint detailsScope = uint.MaxValue, detailsId = 0;
            int detailsCount = 0;
            content.DetailsRequested += (scope, id) => { detailsScope = scope; detailsId = id; detailsCount++; };
            Button(content.Root, "QuestDetails_" + MissionTrackingState.Key(claimable)).onClick.Invoke();
            Require(detailsCount == 1 && detailsScope == 2 && detailsId == 12, "领奖详情仅发出正确scope与任务ID的详情意图");
            Require(claimable.Status == (PlayerMissionStatus)3 && claimable.CanClaim, "打开详情不推导完成或消耗领奖能力");
            Shoot("08-claimable", "可领取：只打开原任务详情，HUD不发奖");

            Set(list, error: "离线测试读取失败");
            Require(HasText(content.Root, "显示上次记录") && content.Root.GetComponentsInChildren<UnityEngine.Transform>(true)
                .Any(t => t.name == "QuestCard_0_101"), "读取失败保留旧卡片并标记过期");
            Shoot("09-stale", "读取失败：保留上次记录并提示刷新");
            Set(null, error: "离线测试读取失败");
            Require(HasText(content.Root, "同步失败，请刷新重试"), "无旧快照失败与成功空态分开");
            Shoot("10-error", "无快照失败：明确重试，不宣告无任务");
            checks.Add(width + "×" + height + "：未同步、加载、空态、四张普通卡完整可见、六任务滚动、多目标、未知数量、失败、详情与过期状态通过原生控件检查。");
            CheckFooterBounds(content.Root, camera, width, height);
            checks.Add(width + "×" + height + "：任务列表、刷新与翻页入口处于屏幕内；未创建网络或服务器写动作。");

            // 同一场景/相机继续验证真正的07壳与08宿主；独立夹具不充当实际装配证据。
            content.Dispose(); content = null;
            UnityEngine.Object.DestroyImmediate(frame.gameObject);
            fixtureScope.text = "离线实际装配 · 正式07共用壳 + 08独立宿主 · 内存回包";
            sharedShell = CreateSharedShell();
            sharedHost = new GameObject("QuestIntegrationFixtureHost").AddComponent<QuestHudRoot>();
            InvokeLifecycle(sharedHost, "Awake");
            sharedHost.EnsureMounted();
            var transport = new QuestFixtureTransport { Snapshot = ListFixture() };
            sharedClient = new PlayerFeaturesClient(transport);
            sharedHost.TickSession(sharedClient, transport.PlayerId, false, 0);
            var mountedContent = SharedContent(sharedShell);
            var firstMission = sharedClient.Missions.Missions[0];
            sharedClient.MissionTracking.Track(firstMission);
            Require(mountedContent.parent == sharedShell.QuestMount && mountedContent.gameObject.activeInHierarchy,
                "08宿主实际创建内容并挂载07当前任务区");
            Require(sharedShell.QuestMount.GetComponentsInChildren<RectTransform>(true)
                .Count(rect => rect.name == "QuestHudContent") == 1, "实际挂载仅有一份任务内容");
            Shoot("11-shared-quests", "实际挂载任务页：同一Client快照与手动追踪偏好");
            CheckFooterBounds(mountedContent, camera, width, height);
            CheckSharedBounds(sharedShell.Panel, camera, width, height, "共用栏");
            foreach (var name in new[] { "SideHudTab_Quests", "SideHudTab_Team", "SideHudCollapse" })
                CheckSharedBounds((RectTransform)Button(sharedShell.Panel, name).transform, camera, width, height, name);

            sharedShell.SelectPage(WorldSideHudPage.Team);
            transport.Snapshot = ListFixture();
            transport.Snapshot.Missions[0].Objectives[0].Progress = 7;
            sharedHost.TickSession(sharedClient, transport.PlayerId, false, 10);
            Require(!mountedContent.gameObject.activeInHierarchy && sharedShell.TeamMount.gameObject.activeInHierarchy,
                "切组队页只隐藏任务内容");
            Require(HasText(mountedContent, "7/10"), "组队页显示期间任务仍更新真实回包");
            Shoot("12-shared-team-selected", "切换组队页：隐藏的任务仍同步到7/10；无在线队伍数据");

            sharedShell.SetCollapsed(true);
            transport.Snapshot = ListFixture();
            transport.Snapshot.Missions[0].Objectives[0].Progress = 8;
            sharedHost.TickSession(sharedClient, transport.PlayerId, false, 20);
            Require(!sharedShell.TeamMount.gameObject.activeInHierarchy && !mountedContent.gameObject.activeInHierarchy,
                "折叠后两页内容均隐藏");
            Require(HasText(mountedContent, "8/10"), "折叠期间任务仍更新真实回包");
            var expand = sharedShell.Panel.GetComponentsInChildren<Button>().Single();
            Require(expand.name == "SideHudCollapse" && expand.GetComponentInChildren<TMP_Text>().text == "展开",
                "折叠后只保留实际展开入口");
            Shoot("13-shared-collapsed", "共用栏折叠：唯一展开入口可达，任务后台同步到8/10");
            CheckSharedBounds((RectTransform)expand.transform, camera, width, height, "展开入口");

            sharedShell.SetCollapsed(false);
            Require(sharedShell.SelectedPage == WorldSideHudPage.Team, "展开恢复此前选页");
            sharedShell.SelectPage(WorldSideHudPage.Quests);
            Require(SharedContent(sharedShell) == mountedContent && HasText(mountedContent, "8/10") &&
                sharedClient.MissionTracking.IsTracked(MissionTrackingState.Key(firstMission)),
                "恢复任务页保留同一内容、进度与手选偏好");
            Shoot("14-shared-quests-restored", "恢复任务页：同一内容、8/10进度与手动追踪全部保留");

            InvokeLifecycle(sharedShell, "OnDestroy");
            UnityEngine.Object.DestroyImmediate(sharedShell.gameObject);
            sharedShell = null;
            sharedHost.EnsureMounted();
            Require(mountedContent == null, "旧07壳销毁后旧内容随之销毁");
            sharedShell = CreateSharedShell();
            sharedHost.EnsureMounted();
            mountedContent = SharedContent(sharedShell);
            Require(HasText(mountedContent, "8/10") && HasText(mountedContent, "取消追踪") && transport.Reads == 3,
                "07新壳重新挂载同一Client快照与偏好，不额外读网络或新建Client");
            Shoot("15-shared-remounted", "销毁并重建07壳：08自动重挂，保留8/10与追踪偏好");
            CheckFooterBounds(mountedContent, camera, width, height);
            checks.Add(width + "×" + height + "：正式07/08挂载、页签互斥、隐藏与折叠后台同步、展开恢复、壳重建重挂和实际按钮边界通过；仅3次内存任务读取，无外部网络或写操作。");
        }
        finally
        {
            content?.Dispose();
            if (sharedHost != null)
            {
                InvokeLifecycle(sharedHost, "OnDestroy");
                UnityEngine.Object.DestroyImmediate(sharedHost.gameObject);
            }
            if (sharedShell != null)
            {
                InvokeLifecycle(sharedShell, "OnDestroy");
                UnityEngine.Object.DestroyImmediate(sharedShell.gameObject);
            }
            sharedClient?.Dispose();
            RenderTexture.active = previousTarget;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static WorldSideHudRoot CreateSharedShell()
    {
        var shell = new GameObject("QuestIntegrationFixture07Shell").AddComponent<WorldSideHudRoot>();
        shell.EnsureBuilt();
        shell.RefreshPresentation(true, false);
        return shell;
    }

    private static RectTransform SharedContent(WorldSideHudRoot shell) => shell.QuestMount
        .GetComponentsInChildren<RectTransform>(true).Single(rect => rect.name == "QuestHudContent");
    private static void InvokeLifecycle(object instance, string method) => instance.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null);
    private static void CheckSharedBounds(RectTransform rect, Camera camera, int width, int height, string name)
    {
        var corners = new UnityEngine.Vector3[4];
        rect.GetWorldCorners(corners);
        var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        var max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        Require(min.x >= -.5f && min.y >= -.5f && max.x <= width + .5f && max.y <= height + .5f,
            name + "在" + width + "×" + height + "内可达：" + min + " / " + max);
    }

    /// <summary>编辑器专用受控回包源；禁止任何非读取消息，不依赖Tests程序集或真实连接。</summary>
    private sealed class QuestFixtureTransport : IBattleTransport
    {
        public ulong PlayerId => 9981;
        public bool IsReady => true;
        public int Reads { get; private set; }
        public GetMissionListResponse Snapshot { get; set; }
        public event Action Disconnected { add { } remove { } }
        public void RegisterNotify(uint messageId, Action<MessageContent> handler) { }
        public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
            Action<TResp> onResponse, Action<string> onError) where TResp : IMessage<TResp>
        {
            if (messageId != SceneMissionClientPlayerRpc.GetMissionListMessageId || Snapshot == null)
                throw new InvalidOperationException("离线任务装配只允许受控的任务读取。");
            Reads++;
            onResponse(parser.ParseFrom(Snapshot.ToByteString()));
        }
        public void SendOneWay(uint messageId, IMessage request)
            => throw new InvalidOperationException("离线任务装配不允许发送单向或写操作。");
    }

    private static GetMissionListResponse ListFixture()
    {
        var response = new GetMissionListResponse { StatePersistent = true };
        for (uint index = 0; index < 6; index++)
        {
            var mission = new PlayerMissionInfo
            {
                Scope = 0, MissionId = 101 + index, MissionType = index < 3 ? 1U : 2U,
                Status = (PlayerMissionStatus)(index == 4 ? 3 : index == 5 ? 4 : 1),
                Name = new[] { "初入仙门（样例）", "山中修行（样例）", "长风古道寻访（样例）", "每日修行（样例）", "历练回响（样例）", "限时修行（样例）" }[index],
                Configured = true, CanClaim = index == 4,
                UnavailableReason = index == 5 ? "活动期限已结束，请查看任务详情" : ""
            };
            if (index != 5)
                mission.Objectives.Add(new MissionObjectiveInfo { ObjectiveIndex = 0, ConditionId = 12,
                    Description = "完成已确认的修行目标", Progress = index + 1, Target = 10, Completed = index == 4 });
            response.Missions.Add(mission);
        }
        return response;
    }

    private static GetMissionListResponse MultiObjectiveFixture()
    {
        var mission = new PlayerMissionInfo { Scope = 0, MissionId = 108, MissionType = 1, Status = (PlayerMissionStatus)1,
            Name = "问道长卷：多目标修行（离线样例）", Configured = true };
        for (uint index = 0; index < 8; index++)
            mission.Objectives.Add(new MissionObjectiveInfo { ObjectiveIndex = index, ConditionId = 12,
                Description = index == 6 ? "待确认数量的目标说明" : "修行阶段" + (index + 1) + "：核对来自同一任务快照的目标内容与进度，长说明支持换行。",
                Progress = index, Target = index == 6 ? 0U : 10U, Completed = index == 0 });
        return new GetMissionListResponse { StatePersistent = true, Missions = { mission } };
    }

    private static Button Button(UnityEngine.Transform root, string name) => root.GetComponentsInChildren<Button>(true)
        .Single(button => button.name == name);
    private static bool HasText(UnityEngine.Transform root, string value) => root.GetComponentsInChildren<TMP_Text>(true)
        .Any(text => text.text.Contains(value));
    private static void CheckFooterBounds(UnityEngine.Transform root, Camera camera, int width, int height)
    {
        foreach (var name in new[] { "QuestRefresh", "QuestScrollUp", "QuestScrollDown", "QuestList" })
        {
            var rect = (RectTransform)Button(root, name).transform;
            var corners = new UnityEngine.Vector3[4];
            rect.GetWorldCorners(corners);
            var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            Require(min.x >= 0 && min.y >= 0 && max.x <= width + .5f && max.y <= height + .5f,
                name + "在" + width + "×" + height + "内可达");
        }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("任务栏原生验收失败：" + message);
    }
}
#endif
