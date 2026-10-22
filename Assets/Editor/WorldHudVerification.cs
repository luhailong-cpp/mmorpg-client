#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MmorpgClient.UI.Ugui;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Native captures and interaction checks of the production navigation with isolated source
/// controls. No gameplay root, account, transport, request or online session is created.
/// </summary>
public static class WorldHudVerification
{
    public static string OutputDirectory { get; set; } = Path.GetFullPath(Path.Combine(
        Application.dataPath, "../.codex-artifacts/world-hud-20261022"));

    [Serializable] private sealed class Report
    {
        public string status, unityVersion, scope;
        public string[] checks, screenshots;
    }

    private sealed class Source
    {
        public WorldHudEntry Id;
        public GameObject Owner;
        public RectTransform Rect;
        public Button Button;
        public TMP_Text Label;
        public int Clicks;
    }

    private static readonly Dictionary<WorldHudEntry, string> Labels = new()
    {
        { WorldHudEntry.Role, "角色" }, { WorldHudEntry.Pet, "宝宝" },
        { WorldHudEntry.Bag, "背包" }, { WorldHudEntry.Missions, "任务" },
        { WorldHudEntry.Activities, "活动" }, { WorldHudEntry.Battle, "战斗" },
        { WorldHudEntry.Spectate, "观战" }, { WorldHudEntry.Rank, "论剑榜" },
        { WorldHudEntry.Team, "组队 [T]" }, { WorldHudEntry.Guild, "帮会 [G]" },
        { WorldHudEntry.Social, "仙友会" }, { WorldHudEntry.Mail, "仙笺" },
        { WorldHudEntry.Trade, "聚宝斋" }, { WorldHudEntry.Map, "地图 [M]" },
        { WorldHudEntry.Channel, "10线 [L]" }
    };

    private static readonly Dictionary<WorldHudCategory, WorldHudEntry[]> Members = new()
    {
        { WorldHudCategory.Cultivation, new[] { WorldHudEntry.Role, WorldHudEntry.Pet } },
        { WorldHudCategory.Adventure, new[] { WorldHudEntry.Missions, WorldHudEntry.Activities,
            WorldHudEntry.Battle, WorldHudEntry.Spectate, WorldHudEntry.Rank } },
        { WorldHudCategory.Social, new[] { WorldHudEntry.Team, WorldHudEntry.Guild,
            WorldHudEntry.Social, WorldHudEntry.Mail } },
        { WorldHudCategory.Trade, new[] { WorldHudEntry.Trade } }
    };

    private static readonly Dictionary<WorldHudCategory, string> CategoryLabels = new()
    {
        { WorldHudCategory.Cultivation, "修行" }, { WorldHudCategory.Adventure, "历练" },
        { WorldHudCategory.Social, "仙友" }, { WorldHudCategory.Trade, "交易" }
    };

    [MenuItem("MMORPG/UI/Capture categorized world HUD")]
    public static void CaptureAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("World HUD capture requires Edit Mode.");
        if (WorldHudNavigation.Instance != null)
            throw new InvalidOperationException("An existing world HUD is active; refusing to replace it.");

        Directory.CreateDirectory(OutputDirectory);
        var checks = new List<string>();
        var screenshots = new List<string>();
        var report = new Report
        {
            status = "RUNNING", unityVersion = Application.unityVersion,
            scope = "Production WorldHudNavigation with isolated source controls; no network or live account."
        };
        try
        {
            foreach (var size in new[] { new Vector2Int(2560, 1080), new Vector2Int(1920, 1080),
                new Vector2Int(1600, 900), new Vector2Int(1280, 720) })
                Capture(size.x, size.y, checks, screenshots);
            report.status = "PASS";
            Debug.Log("WORLD_HUD_VERIFICATION_PASS|" + checks.Count + " checks|" +
                screenshots.Count + " native captures|" + OutputDirectory);
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
            File.WriteAllText(Path.Combine(OutputDirectory, "verification.json"),
                JsonUtility.ToJson(report, true));
        }
    }

    private static void Capture(int width, int height, List<string> checks, List<string> screenshots)
    {
        var previousScene = SceneManager.GetActiveScene();
        // A fresh batch editor starts with an unsaved untitled scene, which Unity
        // refuses to accompany with an additive scene. Its disposable fixture can
        // replace that batch-only scene; an interactive editor keeps its scene intact.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
            Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
        var previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D pixels = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("HudFixtureCamera").AddComponent<Camera>();
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

            var backgroundCanvas = CreateCanvas("BackdropCanvas", -100);
            var backgroundRoot = QdaoUguiFactory.CreateCenteredRect("BackdropDesign",
                backgroundCanvas.transform, 2560, 1080);
            QdaoUguiFactory.CreateImage("Sanctuary", backgroundRoot, 0, 0, 2560, 1080,
                QdaoRefreshArt.Load("sanctuary_background"));
            QdaoUguiFactory.CreateText("Scope", backgroundRoot, 40, 1028, 1600, 32,
                "主界面分类入口 · 原生 uGUI 视觉样例", 25, QdaoUguiTheme.Cream);

            var sources = new List<Source>();
            foreach (WorldHudEntry id in Enum.GetValues(typeof(WorldHudEntry)))
            {
                if (!Labels.TryGetValue(id, out var label)) continue;
                var owner = CreateCanvas("Source_" + id, 140);
                var design = QdaoUguiFactory.CreateCenteredRect("SourceDesign", owner.transform, 2560, 1080);
                var button = QdaoUguiFactory.CreateArtButton("SourceButton_" + id, design,
                    0, 0, 336, 82, QdaoUguiTheme.RequireSprite("UI/Ugui/CoreFidelity20261016/hud_button"), out _);
                var text = QdaoUguiFactory.CreateText("SourceLabel", button.transform, 16, 0,
                    304, 82, label, 32, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
                var source = new Source { Id = id, Owner = owner, Rect = (RectTransform)button.transform,
                    Button = button, Label = text };
                button.onClick.AddListener(() => source.Clicks++);
                sources.Add(source);
                WorldHudNavigation.Register(id, source.Rect, button, text);
            }
            var nav = WorldHudNavigation.Instance;
            Require(nav != null && nav.Root != null, "Navigation created through production Register");
            foreach(var source in sources.Where(source=>source.Id==WorldHudEntry.Battle||source.Id==WorldHudEntry.Spectate||source.Id==WorldHudEntry.Role))
            {
                int index=source.Id==WorldHudEntry.Battle?0:source.Id==WorldHudEntry.Spectate?1:2;
                var frame=(RectTransform)source.Rect.parent;
                Require(frame.anchoredPosition==new Vector2(2160,-176-index*104),source.Id+" retains approved 04 position");
                Require(frame.sizeDelta==new Vector2(336,81.6f),source.Id+" retains approved 04 dimensions");
                Require(Mathf.Approximately(source.Label.fontSize,36),source.Id+" retains approved 04 label size");
            }

            void Refresh()
            {
                nav.RefreshPresentation();
                foreach (var go in scene.GetRootGameObjects())
                {
                    foreach (var child in go.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
                    foreach (var canvas in go.GetComponentsInChildren<Canvas>(true))
                    {
                        canvas.renderMode = RenderMode.ScreenSpaceCamera;
                        canvas.worldCamera = camera;
                        canvas.planeDistance = 2f;
                    }
                }
                Canvas.ForceUpdateCanvases();
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var text in go.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
                Canvas.ForceUpdateCanvases();
            }

            void Shoot(string name)
            {
                Refresh();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                pixels.Apply(false, false);
                var file = name + "_" + width + "x" + height + ".png";
                File.WriteAllBytes(Path.Combine(OutputDirectory, file), pixels.EncodeToPNG());
                screenshots.Add(file);
            }

            void CheckState(WorldHudCategory? category)
            {
                Refresh();
                var rectangles = new List<Rect>();
                foreach (var source in sources)
                {
                    bool expected = IsShortcut(source.Id) || (category.HasValue && Members[category.Value].Contains(source.Id));
                    Require(IsVisible(source.Rect) == expected,
                        source.Id + " visibility in " + (category?.ToString() ?? "closed"));
                    Require(source.Rect.gameObject.activeSelf && source.Button.interactable,
                        source.Id + " original active/interactable state is retained");
                    if (!expected) continue;
                    var corners = new UnityEngine.Vector3[4];
                    source.Rect.GetWorldCorners(corners);
                    var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                    var max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                    var rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                    Require(rect.width >= 40 && rect.height >= 40 && rect.xMin >= -.5f && rect.yMin >= -.5f &&
                        rect.xMax <= width + .5f && rect.yMax <= height + .5f,
                        source.Id + " usable target stays inside " + width + "x" + height + ": " + rect);
                    Require(!rectangles.Any(previous => previous.Overlaps(rect)), source.Id + " does not overlap a system entry");
                    rectangles.Add(rect);
                }
                checks.Add(width + "x" + height + " " + (category?.ToString() ?? "closed") +
                    ": visibility, original states, target bounds and nonoverlap PASS");
            }

            nav.ShowCategory(null);
            CheckState(null);
            Shoot("00-closed");
            foreach (WorldHudCategory category in Enum.GetValues(typeof(WorldHudCategory)))
            {
                var categoryButton = nav.Root.GetComponentsInChildren<TMP_Text>(true)
                    .Where(text => text.text == CategoryLabels[category])
                    .Select(text => text.GetComponentInParent<Button>()).FirstOrDefault(button => button != null);
                Require(categoryButton != null, category + " has a labeled Button");
                categoryButton.onClick.Invoke();
                Require(nav.CurrentCategory == category, category + " real category click opens the matching group");
                CheckState(category);
                Shoot("0" + ((int)category + 1) + "-" + category.ToString().ToLowerInvariant());
                categoryButton.onClick.Invoke();
                Require(!nav.CurrentCategory.HasValue, category + " repeated click collapses the group");
            }

            var battle = sources.Find(source => source.Id == WorldHudEntry.Battle);
            nav.ShowCategory(WorldHudCategory.Adventure);
            battle.Label.text = "重新连接战斗";
            battle.Button.interactable = false;
            Refresh();
            Require(battle.Label.text == "重新连接战斗" && !battle.Button.interactable,
                "Dynamic reconnect label and disabled state survive refresh");
            battle.Button.interactable = true;
            battle.Rect.gameObject.SetActive(false);
            Refresh();
            Require(!battle.Rect.gameObject.activeSelf, "Hidden source remains hidden after category refresh");
            battle.Rect.gameObject.SetActive(true);
            Refresh();
            battle.Button.onClick.Invoke();
            Require(battle.Clicks == 1, "Original source listener fires exactly once");
            checks.Add(width + "x" + height + ": source dynamic label, disabled state, active state and callback PASS");

            nav.ShowCategory(null);
            var notice = nav.Root.GetComponentsInChildren<UnityEngine.Transform>(true)
                .FirstOrDefault(child => child.name == "PendingMailOrTeamNotice");
            Require(notice != null, "Social category owns the real pending-notice indicator");
            Refresh();
            Require(!notice.gameObject.activeSelf, "No notification is fabricated for quiet source labels");
            var mail = sources.Find(source => source.Id == WorldHudEntry.Mail);
            mail.Label.text = "仙笺 [N] · 3";
            Refresh();
            Require(notice.gameObject.activeSelf, "Unread mail remains discoverable while category is collapsed");
            mail.Label.text = Labels[WorldHudEntry.Mail];
            var team = sources.Find(source => source.Id == WorldHudEntry.Team);
            team.Label.text = "组队 · 2条申请";
            Refresh();
            Require(notice.gameObject.activeSelf, "Team applications remain discoverable while category is collapsed");
            team.Label.text = Labels[WorldHudEntry.Team];
            Refresh();
            Require(!notice.gameObject.activeSelf, "Category notification clears with source notices");
            checks.Add(width + "x" + height + ": mail/team notice aggregation and clearing PASS");

            nav.ShowCategory(WorldHudCategory.Social);
            foreach (var source in sources) source.Owner.SetActive(false);
            Refresh();
            Require(sources.All(source => !IsVisible(source.Rect)), "Session-owned source canvases remain hidden");
            Require(!nav.CurrentCategory.HasValue, "Loss of source availability closes the expanded group");
            checks.Add(width + "x" + height + ": availability lifecycle PASS");
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (WorldHudNavigation.Instance != null) UnityEngine.Object.DestroyImmediate(WorldHudNavigation.Instance.gameObject);
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static GameObject CreateCanvas(string name, int order)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2560, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        return go;
    }

    private static bool IsShortcut(WorldHudEntry id) => id == WorldHudEntry.Bag || id == WorldHudEntry.Map || id == WorldHudEntry.Channel ||
        id == WorldHudEntry.Battle || id == WorldHudEntry.Spectate || id == WorldHudEntry.Role;

    private static bool IsVisible(RectTransform rect)
    {
        if (!rect.gameObject.activeInHierarchy) return false;
        foreach (var group in rect.GetComponentsInParent<CanvasGroup>(true))
            if (group.alpha < .01f) return false;
        return true;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("HUD acceptance failed: " + message);
    }
}
#endif
