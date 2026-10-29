#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MmorpgClient.Game.Team;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Team;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>生产组队HUD的隔离原生绘制与射线检查。所有成员为离线测试夹具，不启动账号、网络或游戏宿主。</summary>
public static class WorldSideHudVerification
{
    public static string OutputDirectory { get; set; } = Path.GetFullPath(Path.Combine(
        Application.dataPath, "../.codex-artifacts/team-hud-20261022"));

    [Serializable] private sealed class Report
    {
        public string status, unityVersion, scope;
        public string[] checks, screenshots;
    }

    [MenuItem("MMORPG/UI/Capture team shared HUD (offline)")]
    public static void CaptureAll()
    {
        var arguments = Environment.GetCommandLineArgs();
        for (int index = 0; index + 1 < arguments.Length; ++index)
            if (arguments[index] == "-teamHudOutput") { OutputDirectory = Path.GetFullPath(arguments[index + 1]); break; }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("组队HUD原生截图必须在EditMode运行。");
        if (WorldSideHudRoot.Instance != null)
            throw new InvalidOperationException("已有共享HUD正在运行，拒绝替换。");
        Directory.CreateDirectory(OutputDirectory);
        var checks = new List<string>();
        var screenshots = new List<string>();
        var report = new Report
        {
            status = "RUNNING", unityVersion = Application.unityVersion,
            scope = "生产WorldSideHudRoot/TeamHudPanel的隔离EditMode原生UI；仅测试夹具成员，无网络及在线账号。"
        };
        try
        {
            foreach (var resolution in new[] { new Vector2Int(2560, 1080), new Vector2Int(1920, 1080) })
                Capture(resolution.x, resolution.y, checks, screenshots);
            report.status = "PASS";
            Debug.Log("TEAM_HUD_VERIFICATION_PASS|" + checks.Count + " checks|" + screenshots.Count + " captures|" + OutputDirectory);
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
        var previousTarget = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
            Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
        RenderTexture target = null;
        Texture2D pixels = null;
        WorldSideHudRoot root = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("TeamHudFixtureCamera").AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100;
            camera.cullingMask = 1 << 31;
            camera.targetDisplay = Display.activeEditorGameViewTarget;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = QdaoUguiTheme.Letterbox;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create();
            camera.targetTexture = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            var events = new GameObject("TeamHudFixtureEvents").AddComponent<EventSystem>();
            var backdrop = new GameObject("TeamHudBackdrop", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var backdropCanvas = backdrop.GetComponent<Canvas>();
            backdropCanvas.sortingOrder = -100;
            var scaler = backdrop.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("TeamHudBackdropDesign", backdrop.transform, 2560, 1080);
            QdaoUguiFactory.CreateImage("Sanctuary", design, 0, 0, 2560, 1080, QdaoRefreshArt.Load("sanctuary_background"));
            QdaoUguiFactory.CreateText("OfflineScope", design, 40, 1028, 1600, 32,
                "组队共用HUD · 离线测试夹具 · 原生uGUI", 25, QdaoUguiTheme.Cream);
            root = new GameObject("TeamHudFixtureRoot").AddComponent<WorldSideHudRoot>();
            root.EnsureBuilt();
            root.SelectPage(WorldSideHudPage.Team);
            root.RefreshPresentation(true, false);
            var state = new TeamUiState(() => 0);
            var snapshot = FullTeam();
            state.SetSnapshot(snapshot);
            root.TeamContent.SetState(state, true);

            void Refresh()
            {
                foreach (var go in scene.GetRootGameObjects())
                {
                    foreach (var child in go.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
                    foreach (var canvas in go.GetComponentsInChildren<Canvas>(true))
                    {
                        canvas.renderMode = RenderMode.ScreenSpaceCamera;
                        canvas.worldCamera = camera;
                        canvas.planeDistance = 2;
                        canvas.targetDisplay = camera.targetDisplay;
                    }
                }
                Canvas.ForceUpdateCanvases();
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
                Canvas.ForceUpdateCanvases();
                camera.Render();
            }

            void Shoot(string name)
            {
                Refresh();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                pixels.Apply(false, false);
                string file = name + "_" + width + "x" + height + ".png";
                File.WriteAllBytes(Path.Combine(OutputDirectory, file), pixels.EncodeToPNG());
                screenshots.Add(file);
            }

            List<RaycastResult> HitAt(Vector2 position)
            {
                var hits = new List<RaycastResult>();
                var pointer = new PointerEventData(events) { position = position, displayIndex = camera.targetDisplay };
                foreach (var raycaster in root.GetComponentsInChildren<GraphicRaycaster>()) raycaster.Raycast(pointer, hits);
                return hits;
            }

            Vector2 PanelPoint(float x, float y) => RectTransformUtility.WorldToScreenPoint(camera,
                root.Panel.TransformPoint(new UnityEngine.Vector3(x, -y, 0)));

            void CheckBounds(RectTransform rect, string name)
            {
                var corners = new UnityEngine.Vector3[4];
                rect.GetWorldCorners(corners);
                var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                var max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                Require(min.x >= -.5f && min.y >= -.5f && max.x <= width + .5f && max.y <= height + .5f,
                    name + "在" + width + "×" + height + "画面内可达：" + min + " / " + max);
            }

            Shoot("00-team-full-top");
            var scroll = root.TeamContent.Root.GetComponentInChildren<ScrollRect>();
            Require(Mathf.Abs(scroll.verticalNormalizedPosition - 1) < .001f, "首次满队列表从顶部开始");
            var hudCanvas = root.GetComponentInChildren<Canvas>();
            Require(Mathf.Abs(hudCanvas.scaleFactor - width / 2560f) < .001f,
                "两种目标分辨率使用正确设计缩放，实际=" + hudCanvas.scaleFactor);
            CheckBounds(root.Panel, "共享栏边框");
            foreach (var button in root.GetComponentsInChildren<Button>()) CheckBounds((RectTransform)button.transform, button.name);
            Require(root.TeamMount.gameObject.activeInHierarchy && !root.QuestMount.gameObject.activeInHierarchy, "只有组队页可见");
            var summon = root.GetComponentsInChildren<Button>().Single(button => button.name == "TeamHudSummon");
            Require(!summon.interactable, "召集未定义，原生Button禁用");
            Require(HitAt(PanelPoint(4, 110)).Count > 0, "可见栏底板接收射线");
            var worldPoint = RectTransformUtility.WorldToScreenPoint(camera, design.TransformPoint(new UnityEngine.Vector3(-500, 0, 0)));
            Require(HitAt(worldPoint).Count == 0, "空白世界区域不被HUD拦截");
            checks.Add(width + "x" + height + ": 互斥页签、边框/底部按钮可达、召集禁用、真实射线边界 PASS");

            Require(scroll.content.rect.height > scroll.viewport.rect.height && scroll.vertical && !scroll.horizontal,
                "满队列表使用垂直滚动容纳全部成员");
            scroll.verticalNormalizedPosition = 0;
            Shoot("01-team-full-bottom");
            Require(Mathf.Abs(scroll.verticalNormalizedPosition) < .001f, "滚动位置确实到达底部");
            var lastMember = root.TeamContent.Root.GetComponentsInChildren<RectTransform>()
                .Single(rect => rect.name == "TeamHudMember_55");
            var lastCorners = new UnityEngine.Vector3[4];
            lastMember.GetWorldCorners(lastCorners);
            foreach (var corner in lastCorners)
            {
                var local = scroll.viewport.InverseTransformPoint(corner);
                var bounds = scroll.viewport.rect;
                Require(local.x >= bounds.xMin - .5f && local.x <= bounds.xMax + .5f &&
                    local.y >= bounds.yMin - .5f && local.y <= bounds.yMax + .5f,
                    "最后一位成员完整进入滚动视口，局部位置=" + local);
            }
            checks.Add(width + "x" + height + ": 满队成员溢出后可滚到列表底部 PASS");

            root.SetCollapsed(true);
            Shoot("02-collapsed");
            Require(!root.TeamMount.gameObject.activeInHierarchy && !root.QuestMount.gameObject.activeInHierarchy, "折叠时两内容均不显示");
            Require(HitAt(PanelPoint(4, 110)).Count == 0, "折叠后原底板区域释放射线");
            var expand = root.GetComponentsInChildren<Button>().Single();
            Require(expand.name == "SideHudCollapse" && expand.GetComponentInChildren<TMP_Text>().text == "展开", "唯一展开入口可用");
            CheckBounds((RectTransform)expand.transform, "展开入口");
            var expandRect = (RectTransform)expand.transform;
            var expandPoint = RectTransformUtility.WorldToScreenPoint(camera, expandRect.TransformPoint(expandRect.rect.center));
            Require(expand.interactable && HitAt(expandPoint).Any(hit => hit.gameObject == expand.gameObject),
                "展开入口中心实际命中可交互按钮");
            root.SetCollapsed(false);
            Require(root.SelectedPage == WorldSideHudPage.Team && root.TeamMount.gameObject.activeInHierarchy,
                "展开恢复同一组队页");
            checks.Add(width + "x" + height + ": 折叠释放底板射线，展开恢复选页且入口可达 PASS");

            state.SetSnapshot(new TeamSnapshot { LocalPlayerId = 11, MembershipEpoch = 3, Version = 1 });
            Shoot("03-no-team");
            Require(root.TeamContent.Root.GetComponentsInChildren<TMP_Text>().Any(text => text.text == "尚未加入队伍"), "明确无队伍显示空态");
            Require(!root.TeamContent.Root.GetComponentsInChildren<RectTransform>().Any(rect => rect.name.StartsWith("TeamHudMember_")),
                "无队伍时旧成员行已清空");
            Require(!root.TeamContent.Root.GetComponentsInChildren<Button>().Any(button =>
                (button.name == "TeamHudLeave" || button.name == "TeamHudSummon") && button.interactable),
                "无队伍时离队及召集禁用");
            state.Reset(11);
            Shoot("04-syncing");
            Require(root.TeamContent.Root.GetComponentsInChildren<TMP_Text>().Any(text => text.text == "正在同步队伍…"), "未同步不能冒充无队伍");
            state.SetSnapshot(snapshot);
            root.TeamContent.SetState(state, false);
            Shoot("05-stale");
            Require(root.TeamContent.Root.GetComponentsInChildren<RectTransform>().Count(rect => rect.name.StartsWith("TeamHudMember_")) == 5,
                "过期视图仍保留五位真实成员");
            Require(root.TeamContent.Root.GetComponentsInChildren<TMP_Text>().Any(text => text.text.Contains("信息待确认")),
                "过期视图明确提示信息待确认");
            Require(!root.TeamContent.Root.GetComponentsInChildren<Button>().Single(button => button.name == "TeamHudLeave").interactable,
                "过期视图不能离队");
            checks.Add(width + "x" + height + ": 无队伍/未同步区分，过期名单保留且写动作禁用 PASS");

            root.RefreshPresentation(true, true);
            Require(!root.Panel.gameObject.activeInHierarchy, "分类展开时整栏隐藏");
            root.RefreshPresentation(false, false);
            Require(!root.Panel.gameObject.activeInHierarchy, "世界不可用时整栏隐藏");
            root.RefreshPresentation(true, false);
            Require(root.TeamMount.gameObject.activeInHierarchy, "分类关闭且世界可用时恢复选页");
            checks.Add(width + "x" + height + ": 分类与世界可用性显隐生命周期 PASS");
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (root != null) UnityEngine.Object.DestroyImmediate(root.gameObject);
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static TeamSnapshot FullTeam()
    {
        var snapshot = new TeamSnapshot
        {
            TeamId = 100, LeaderId = 11, LocalPlayerId = 11, Capacity = 5, MembershipEpoch = 2, Version = 8
        };
        snapshot.Members.Add(new TeamRole { PlayerId = 11, Name = "清云道友", Level = 28, ClassId = 1, Gender = 1, JoinSeq = 1 });
        snapshot.Members.Add(new TeamRole { PlayerId = 22, Name = "很长很长的离线道友名称", Level = 26, ClassId = 2, Gender = 2, IsOnline = false, JoinSeq = 2 });
        snapshot.Members.Add(new TeamRole { PlayerId = 33, Name = "", Level = 0, ClassId = 0, Gender = 0, OnlineStatusKnown = false, JoinSeq = 3 });
        snapshot.Members.Add(new TeamRole { PlayerId = 44, Name = "丹心道友", Level = 30, ClassId = 3, Gender = 1, InBattle = true, JoinSeq = 4 });
        snapshot.Members.Add(new TeamRole { PlayerId = 55, Name = "逐风道友", Level = 32, ClassId = 4, Gender = 2, JoinSeq = 5 });
        return snapshot;
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("组队HUD原生验收失败：" + description);
    }
}
#endif
