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
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Native captures and interaction checks of the production navigation with isolated source
/// controls. No gameplay root, account, transport, request or online session is created.
/// </summary>
public static class WorldHudVerification
{
    public static string OutputDirectory { get; set; } = Path.GetFullPath(Path.Combine(
        Application.dataPath, "../.codex-artifacts/world-hud-responsive-20261105"));

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
        { WorldHudEntry.Channel, "10线 [L]" },
        { WorldHudEntry.MysteryMerchant, "神秘商人 7线 09:07" },
        { WorldHudEntry.TeamFollow, "暂离" }
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
            foreach (var size in new[] { new Vector2Int(640, 480), new Vector2Int(800, 600),
                new Vector2Int(1024, 768), new Vector2Int(1280, 720), new Vector2Int(1600, 900),
                new Vector2Int(1920, 1080), new Vector2Int(2560, 1080), new Vector2Int(3440, 1440),
                new Vector2Int(3840, 2160), new Vector2Int(720, 1280), new Vector2Int(1280, 480) })
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
        WorldSideHudRoot side = null;
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
            camera.targetDisplay = Display.activeEditorGameViewTarget;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = QdaoUguiTheme.Letterbox;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create();
            camera.targetTexture = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            var events = new GameObject("HudFixtureEvents").AddComponent<EventSystem>();

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
                var button = id == WorldHudEntry.MysteryMerchant
                    ? MysteryMerchantUiRoot.CreateEntry(design, null)
                    : id == WorldHudEntry.TeamFollow
                    ? GameplayUiArt.Button(design, "暂离", 0, 0, 210, 81.6f, null, fontSize: 32)
                    : QdaoUguiFactory.CreateArtButton("SourceButton_" + id, design,
                    0, 0, 336, 82, QdaoUguiTheme.RequireSprite("UI/Ugui/CoreFidelity20261016/hud_button"), out _);
                var text = id == WorldHudEntry.MysteryMerchant || id == WorldHudEntry.TeamFollow
                    ? button.GetComponentInChildren<TMP_Text>(true)
                    : QdaoUguiFactory.CreateText("SourceLabel", button.transform, 16, 0,
                        304, 82, label, 32, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
                text.text = label;
                var source = new Source { Id = id, Owner = owner, Rect = (RectTransform)button.transform,
                    Button = button, Label = text };
                button.onClick.AddListener(() => source.Clicks++);
                sources.Add(source);
                WorldHudNavigation.Register(id, source.Rect, button, text);
            }
            var nav = WorldHudNavigation.Instance;
            Require(nav != null && nav.Root != null, "Navigation created through production Register");
            var sideOwner = new GameObject("HudFixtureSideHud");
            side = sideOwner.AddComponent<WorldSideHudRoot>();
            side.enabled = false;
            side.EnsureBuilt();
            side.SelectPage(WorldSideHudPage.Team);

            void Refresh()
            {
                side.RefreshPresentation(sources.Any(source => source.Owner.activeSelf), nav.CurrentCategory.HasValue);
                foreach (var go in scene.GetRootGameObjects())
                {
                    foreach (var child in go.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
                    foreach (var canvas in go.GetComponentsInChildren<Canvas>(true))
                    {
                        canvas.renderMode = RenderMode.ScreenSpaceCamera;
                        canvas.worldCamera = camera;
                        canvas.planeDistance = 2f;
                        canvas.targetDisplay = camera.targetDisplay;
                    }
                }
                Canvas.ForceUpdateCanvases();
                nav.RefreshPresentation();
                Canvas.ForceUpdateCanvases();
                nav.RefreshPresentation();
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var text in go.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
                Canvas.ForceUpdateCanvases();
            }

            Rect ScreenRect(RectTransform transform)
            {
                var corners = new UnityEngine.Vector3[4];
                transform.GetWorldCorners(corners);
                var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                var max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }

            Rect VisibleScreenRect(RectTransform transform)
            {
                var rect = ScreenRect(transform);
                foreach (var mask in transform.GetComponentsInParent<RectMask2D>())
                {
                    if (!mask.isActiveAndEnabled) continue;
                    var bounds = ScreenRect((RectTransform)mask.transform);
                    rect = Rect.MinMaxRect(Mathf.Max(rect.xMin, bounds.xMin), Mathf.Max(rect.yMin, bounds.yMin),
                        Mathf.Min(rect.xMax, bounds.xMax), Mathf.Min(rect.yMax, bounds.yMax));
                }
                return rect;
            }

            void PointerClick(Button button)
            {
                Refresh();
                var previousRenderTarget = RenderTexture.active;
                try { camera.Render(); }
                finally { RenderTexture.active = previousRenderTarget; }
                var pointer = new PointerEventData(events)
                {
                    position = ScreenRect((RectTransform)button.transform).center,
                    displayIndex = camera.targetDisplay,
                    button = PointerEventData.InputButton.Left
                };
                var hits = new List<RaycastResult>();
                // BaseRaycaster's runtime OnEnable registration is not guaranteed for
                // isolated EditMode trees. Invoke each real native raycaster directly,
                // as the existing HUD layering verification does, then use the same
                // layer/order/depth precedence for this fixture's shared camera.
                var raycasters = scene.GetRootGameObjects()
                    .SelectMany(owner => owner.GetComponentsInChildren<GraphicRaycaster>())
                    .Where(raycaster => raycaster.isActiveAndEnabled).ToArray();
                foreach (var raycaster in raycasters) raycaster.Raycast(pointer, hits);
                hits.Sort((first, second) =>
                {
                    int order = SortingLayer.GetLayerValueFromID(second.sortingLayer)
                        .CompareTo(SortingLayer.GetLayerValueFromID(first.sortingLayer));
                    if (order != 0) return order;
                    order = second.sortingOrder.CompareTo(first.sortingOrder);
                    if (order != 0) return order;
                    order = second.depth.CompareTo(first.depth);
                    if (order != 0) return order;
                    order = first.distance.CompareTo(second.distance);
                    return order != 0 ? order : first.index.CompareTo(second.index);
                });
                var graphic = button.GetComponent<Graphic>();
                string Path(UnityEngine.Transform transform) => transform.parent == null ? transform.name :
                    Path(transform.parent) + "/" + transform.name;
                Require(hits.Count > 0 && hits[0].gameObject == button.gameObject,
                    button.name + " is the topmost native raycast target at its center; pointer=" + pointer.position +
                    "; target=" + ScreenRect((RectTransform)button.transform) + "; active=" + button.gameObject.activeInHierarchy +
                    "; interactable=" + button.IsInteractable() + "; depth=" + (graphic == null ? -999 : graphic.depth) +
                    "; culled=" + (graphic != null && graphic.canvasRenderer.cull) + "; canvas=" +
                    (graphic == null || graphic.canvas == null ? "none" : graphic.canvas.name + ":" + graphic.canvas.sortingOrder) +
                    "; contains=" + RectTransformUtility.RectangleContainsScreenPoint((RectTransform)button.transform, pointer.position, camera) +
                    "; graphicRaycast=" + (graphic != null && graphic.Raycast(pointer.position, camera)) +
                    "; world=" + button.transform.position + "; camera=" + camera.transform.position +
                    "; activeRaycasters=" + raycasters.Length + "; registeredRaycasters=" + RaycasterManager.GetRaycasters().Count +
                    "; hits=" + string.Join(", ", hits.Select(hit => Path(hit.gameObject.transform) + "@" + hit.sortingOrder + ":" + hit.depth)));
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
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
                var viewport = nav.Root.GetComponentInParent<Canvas>().pixelRect;
                Require(Mathf.Approximately(viewport.width, width) && Mathf.Approximately(viewport.height, height),
                    "Native layout measures the capture viewport, not the host monitor");
                void AddBounds(Rect rect, string name)
                {
                    Require(rect.width > 0 && rect.height > 0 && rect.xMin >= viewport.xMin - .5f &&
                        rect.yMin >= viewport.yMin - .5f && rect.xMax <= viewport.xMax + .5f &&
                        rect.yMax <= viewport.yMax + .5f, name + " stays inside " + width + "x" + height + ": " + rect);
                    Require(!rectangles.Any(previous => previous.Overlaps(rect)), name + " does not overlap another HUD control");
                    rectangles.Add(rect);
                }
                foreach (var tab in nav.Root.GetComponentsInChildren<Button>(true)
                    .Where(button => button.name.StartsWith("WorldHudCategory_")))
                    AddBounds(ScreenRect((RectTransform)tab.transform), tab.name);
                if (side.Panel.gameObject.activeInHierarchy)
                    AddBounds(ScreenRect(side.Panel), "Shared quest/team side panel");
                foreach (var source in sources)
                {
                    bool expected = IsShortcut(source.Id) || (category.HasValue && Members[category.Value].Contains(source.Id));
                    Require(IsVisible(source.Rect) == expected,
                        source.Id + " visibility in " + (category?.ToString() ?? "closed"));
                    Require(source.Rect.gameObject.activeSelf && source.Button.interactable,
                        source.Id + " original active/interactable state is retained");
                    if (!expected) continue;
                    var rect = VisibleScreenRect(source.Rect);
                    // Very small windows may need the documented final scale-to-fit fallback.
                    // All ordinary desktop viewport targets remain at least 40 physical pixels.
                    float minimumTarget = width >= 640 && height >= 480 && width >= height ? 40 : 24;
                    if (IsShortcut(source.Id))
                        Require(rect.width >= minimumTarget - .5f && rect.height >= minimumTarget - .5f,
                            source.Id + " minimum physical target at " + width + "x" + height + ": " + rect);
                    AddBounds(rect, source.Id.ToString());
                }
                checks.Add(width + "x" + height + " " + (category?.ToString() ?? "closed") +
                    ": visibility, original states, target bounds and nonoverlap PASS");
            }

            var merchant = sources.Find(source => source.Id == WorldHudEntry.MysteryMerchant);
            foreach (var label in new[] { "神秘商人 7线 09:07", "神秘商人 59:59 后出现", "神秘商人 未开放" })
            {
                merchant.Label.text = label;
                Refresh();
                Require(!merchant.Label.isTextTruncated, "Merchant countdown fits: " + label);
            }
            merchant.Label.text = Labels[WorldHudEntry.MysteryMerchant];
            merchant.Button.onClick.Invoke();
            Require(merchant.Clicks == 1, "Merchant retains its original click callback");
            merchant.Rect.gameObject.SetActive(false);
            Refresh();
            Require(!IsVisible(merchant.Rect), "Merchant owner can hide an unavailable entry");
            merchant.Rect.gameObject.SetActive(true);
            checks.Add(width + "x" + height + ": merchant countdown text, click callback and owner visibility PASS");

            nav.ShowCategory(null);
            CheckState(null);
            Shoot("00-closed");
            var sideScroll = side.Panel.GetComponentsInChildren<ScrollRect>(true)
                .Single(scroll => scroll.name == "TeamOverflowViewport");
            sideScroll.verticalNormalizedPosition = 0;
            Refresh();
            var footer = side.TeamContent.Root.GetComponentsInChildren<TMP_Text>(true)
                .Single(text => text.name == "TeamHudSummonReason");
            var footerBounds = ScreenRect(footer.rectTransform);
            var visibleFooter = VisibleScreenRect(footer.rectTransform);
            Require(visibleFooter.height >= footerBounds.height - .5f,
                "Short side panel can scroll to its footer without hiding the actions below the screen");
            sideScroll.verticalNormalizedPosition = 1;
            side.SetCollapsed(true);
            CheckState(null);
            var collapse = side.Panel.GetComponentsInChildren<Button>(true)
                .Single(button => button.name == "SideHudCollapse");
            PointerClick(collapse);
            Require(!side.IsCollapsed && side.SelectedPage == WorldSideHudPage.Team,
                "Native side expand click restores the selected page after responsive collapse");
            checks.Add(width + "x" + height + ": side overflow footer reachability and native collapse/expand PASS");
            foreach (WorldHudCategory category in Enum.GetValues(typeof(WorldHudCategory)))
            {
                var categoryButton = nav.Root.GetComponentsInChildren<TMP_Text>(true)
                    .Where(text => text.text == CategoryLabels[category])
                    .Select(text => text.GetComponentInParent<Button>()).FirstOrDefault(button => button != null);
                Require(categoryButton != null, category + " has a labeled Button");
                PointerClick(categoryButton);
                Require(nav.CurrentCategory == category, category + " real category click opens the matching group");
                CheckState(category);
                Shoot("0" + ((int)category + 1) + "-" + category.ToString().ToLowerInvariant());
                PointerClick(categoryButton);
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

            // Change the target of the existing camera and canvases. This deliberately keeps
            // every Button, callback, session owner and selected side page in the same UI tree.
            int originalWidth = width, originalHeight = height;
            var originalButtons = sources.Select(source => source.Button).ToArray();
            RenderTexture.active = previousTarget;
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(pixels);
            width = originalWidth == 800 ? 1920 : 800;
            height = originalWidth == 800 ? 1080 : 600;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create();
            camera.targetTexture = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            nav.ShowCategory(null);
            CheckState(null);
            Require(sources.Select(source => source.Button).SequenceEqual(originalButtons),
                "Live viewport resize keeps the existing controls");
            Require(side.SelectedPage == WorldSideHudPage.Team, "Live viewport resize keeps the selected side page");
            Shoot("05-resize-from-" + originalWidth + "x" + originalHeight);
            checks.Add(originalWidth + "x" + originalHeight + " -> " + width + "x" + height +
                ": same-tree resize, source identities, side selection and nonoverlap PASS");

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
            if (side != null) UnityEngine.Object.DestroyImmediate(side.gameObject);
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
        id == WorldHudEntry.Battle || id == WorldHudEntry.Spectate || id == WorldHudEntry.Role ||
        id == WorldHudEntry.MysteryMerchant || id == WorldHudEntry.TeamFollow;

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
