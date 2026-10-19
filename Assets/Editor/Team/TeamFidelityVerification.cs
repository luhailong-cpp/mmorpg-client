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

/// <summary>
/// Captures native TeamWindow pixels and geometry only. No imports, tests, scene saves,
/// network calls, production bootstrap or Edit-mode input-blocker lifecycle assertions.
/// </summary>
public static class TeamFidelityVerification
{
    public static string OutputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath,
        "../../image/designs/team-ui-v2/fidelity-20261016/qa"));
    private const string OutputArgument = "-teamFidelityOutputDirectory";
    private const int CaptureLayer = 31;
    private static readonly long FixedNowMs = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    [Serializable] private sealed class Report
    {
        public int schemaVersion = 1;
        public string status = "running", unityVersion, startedAtUtc, completedAtUtc, outputDirectory;
        public string localTimeZone, captureMode = "EditMode / isolated PreviewScene / native TeamWindow";
        public bool captureOnly = true, liveServerVerified = false, editModeInputLifecycleVerified = false;
        public long fixtureNowMs;
        public bool activeScenePreserved, eventSystemPreserved, selectedObjectPreserved;
        public Shot[] screenshots = Array.Empty<Shot>();
        public AssetCheck[] resourceChecks = Array.Empty<AssetCheck>();
        public string[] missingAssets = Array.Empty<string>();
        public Failure[] exceptions = Array.Empty<Failure>();
        public ConsoleEntry[] console = Array.Empty<ConsoleEntry>();
    }
    [Serializable] private sealed class Shot
    {
        public string name, file, fixture, page;
        public int width, height;
        public bool captured, windowVisible;
        public long fixtureNowMs;
        public Check[] checks = Array.Empty<Check>();
        public TextRecord[] activeTexts = Array.Empty<TextRecord>();
        public ImageRecord[] activeImages = Array.Empty<ImageRecord>();
        public ButtonRecord[] activeButtons = Array.Empty<ButtonRecord>();
    }
    [Serializable] private sealed class Check { public string name; public bool passed; }
    [Serializable] private sealed class AssetCheck
    {
        public string resourceKey, assetPath;
        public bool present;
    }
    [Serializable] private sealed class Failure { public string stage, message, stacktrace; }
    [Serializable] private sealed class ConsoleEntry { public string type, message, stacktrace; }
    [Serializable] private sealed class RectRecord
    {
        public string coordinateSystem = "DesignRoot top-left origin, x right, y down; corners BL, TL, TR, BR";
        public Vector2[] corners;
        public Vector2 topLeft, size;
    }
    [Serializable] private sealed class TextRecord
    {
        public string hierarchy, text, font, fontAssetPath, alignment, wrapping, overflow;
        public float fontSize;
        public bool richText, isTextOverflowing;
        public int characterCount, lineCount;
        public Color color;
        public RectRecord rect;
    }
    [Serializable] private sealed class ImageRecord
    {
        public string hierarchy, sprite, spriteAssetPath, resourceKey, imageType;
        public bool hasSprite, preserveAspect, raycastTarget;
        public Vector4 spriteBorder;
        public Vector2 nativeSpriteSize;
        public Color color;
        public RectRecord rect;
    }
    [Serializable] private sealed class ButtonRecord
    {
        public string hierarchy, name;
        public bool interactable;
        public RectRecord rect;
    }

    [MenuItem("MMORPG/UI/Team/Capture fidelity pixels and geometry")]
    public static void Capture()
    {
        var arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < arguments.Length; i++)
            if (string.Equals(arguments[i], OutputArgument, StringComparison.OrdinalIgnoreCase))
                OutputDirectory = Path.GetFullPath(arguments[i + 1]);
        Directory.CreateDirectory(OutputDirectory);
        var report = new Report
        {
            unityVersion = Application.unityVersion,
            startedAtUtc = DateTime.UtcNow.ToString("O"),
            outputDirectory = OutputDirectory,
            fixtureNowMs = FixedNowMs,
            localTimeZone = TimeZoneInfo.Local.Id
        };
        var shots = new List<Shot>();
        var failures = new List<Failure>();
        var console = new List<ConsoleEntry>();
        var missing = new HashSet<string>(StringComparer.Ordinal);
        Scene previousScene = SceneManager.GetActiveScene();
        EventSystem previousEvents = EventSystem.current;
        GameObject previousSelection = previousEvents != null ? previousEvents.currentSelectedGameObject : null;
        void OnLog(string message, string stacktrace, LogType type)
        {
            if (type != LogType.Warning && type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (console.Count < 300) console.Add(new ConsoleEntry { type = type.ToString(), message = message, stacktrace = stacktrace });
            if (message.IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (message.IndexOf("sprite", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("font", StringComparison.OrdinalIgnoreCase) >= 0))
                missing.Add(message);
        }
        Application.logMessageReceived += OnLog;
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Fidelity capture requires stable Edit mode.");
            report.resourceChecks = CheckResourceImports(missing);
            CaptureResolution(2560, 1080, shots, failures);
            CaptureResolution(1920, 1080, shots, failures);
        }
        catch (Exception exception) { AddFailure(failures, "capture", exception); }
        finally
        {
            Application.logMessageReceived -= OnLog;
            report.activeScenePreserved = SceneManager.GetActiveScene() == previousScene;
            report.eventSystemPreserved = EventSystem.current == previousEvents;
            report.selectedObjectPreserved = previousEvents == null || previousEvents.currentSelectedGameObject == previousSelection;
            if (!report.activeScenePreserved || !report.eventSystemPreserved || !report.selectedObjectPreserved)
                AddFailure(failures, "isolation", new InvalidOperationException("Capture did not preserve the user's active scene, EventSystem or selection."));
            report.screenshots = shots.ToArray();
            report.exceptions = failures.ToArray();
            report.missingAssets = missing.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            report.console = console.ToArray();
            bool failedCheck = shots.Any(shot => shot.checks.Any(check => !check.passed));
            bool loggedError = console.Any(entry => entry.type == "Error" || entry.type == "Exception" || entry.type == "Assert");
            report.status = failures.Count == 0 && missing.Count == 0 && shots.Count == 10 && shots.All(shot => shot.captured) && !failedCheck && !loggedError
                ? "captured" : "error";
            report.completedAtUtc = DateTime.UtcNow.ToString("O");
            File.WriteAllText(Path.Combine(OutputDirectory, "fidelity-capture.json"), JsonUtility.ToJson(report, true));
        }
        if (report.status != "captured")
            throw new InvalidOperationException("Team fidelity capture needs attention. See " + Path.Combine(OutputDirectory, "fidelity-capture.json"));
        Debug.Log("TEAM_FIDELITY_CAPTURE_OK|screenshots=" + shots.Count + "|" + OutputDirectory);
    }

    private static AssetCheck[] CheckResourceImports(HashSet<string> missing)
    {
        var assets = new List<AssetCheck>();
        const string folder = "Assets/Resources/UI/Ugui/TeamV2";
        if (!AssetDatabase.IsValidFolder(folder)) missing.Add(folder);
        else
            foreach (string path in AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
                         .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal))
            {
                string key = ResourceKey(path);
                bool present = Resources.Load<Sprite>(key) != null;
                assets.Add(new AssetCheck { resourceKey = key, assetPath = path, present = present });
                if (!present) missing.Add(key);
            }
        string maskKey = QdaoUguiTheme.StatusDotSpritePath;
        var mask = Resources.Load<Sprite>(maskKey);
        assets.Add(new AssetCheck { resourceKey = maskKey, assetPath = mask == null ? "" : AssetDatabase.GetAssetPath(mask), present = mask != null });
        if (mask == null) missing.Add(maskKey);
        return assets.ToArray();
    }

    private static void CaptureResolution(int width, int height, List<Shot> shots, List<Failure> failures)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        RenderTexture previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D pixels = null;
        try
        {
            var camera = new GameObject("TeamFidelityCamera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
            camera.scene = scene;
            camera.enabled = false;
            camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100;
            camera.cullingMask = 1 << CaptureLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = QdaoUguiTheme.Letterbox;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            if (!target.Create()) throw new InvalidOperationException("Could not create the render target.");
            camera.targetTexture = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            var canvasObject = new GameObject("TeamFidelityCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 2;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform, 2560, 1080);

            // Pixel capture does not create or register an EventSystem. Its registration is a
            // PlayMode lifecycle concern; a current user's selection is restored by Hide/Dispose.

            CapturePage("01-approved-preview", true, 0);
            CapturePage("02-native-authoritative-portrait", false, 0);
            CapturePage("03-member-permissions", false, 1);
            CapturePage("04-received-invites", false, 2);
            CapturePage("05-production-unavailable", false, 3);

            void CapturePage(string name, bool preview, int scenario)
            {
                var shot = new Shot
                {
                    name = name, width = width, height = height,
                    fixture = preview ? "offline-preview" : "production-state-without-server",
                    page = scenario == 2 ? "ReceivedInvites" : "MembersAndApplications", fixtureNowMs = FixedNowMs,
                    file = Path.Combine(OutputDirectory, name + "_" + width + "x" + height + ".png")
                };
                shots.Add(shot);
                TeamWindow window = null;
                try
                {
                    var state = Fixture(preview, scenario);
                    window = new TeamWindow(design);
                    window.SetState(state);
                    window.Show();
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var child in root.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = CaptureLayer;
                    Canvas.ForceUpdateCanvases();
                    foreach (var text in canvasObject.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                    pixels.Apply(false, false);
                    File.WriteAllBytes(shot.file, pixels.EncodeToPNG());
                    shot.captured = true;
                    shot.windowVisible = window.IsVisible;
                    shot.activeTexts = canvasObject.GetComponentsInChildren<TMP_Text>(false).Where(text => text.isActiveAndEnabled).Select(text => new TextRecord
                    {
                        hierarchy = Hierarchy(text.transform, design), text = text.text,
                        font = text.font == null ? "" : text.font.name,
                        fontAssetPath = text.font == null ? "" : AssetDatabase.GetAssetPath(text.font),
                        fontSize = text.fontSize, alignment = text.alignment.ToString(), wrapping = text.textWrappingMode.ToString(),
                        overflow = text.overflowMode.ToString(), richText = text.richText, isTextOverflowing = text.isTextOverflowing,
                        characterCount = text.textInfo.characterCount, lineCount = text.textInfo.lineCount,
                        color = text.color, rect = DesignRect(text.rectTransform, design)
                    }).ToArray();
                    shot.activeImages = canvasObject.GetComponentsInChildren<Image>(false).Where(image => image.isActiveAndEnabled).Select(image => new ImageRecord
                    {
                        hierarchy = Hierarchy(image.transform, design),
                        sprite = image.sprite == null ? "" : image.sprite.name,
                        spriteAssetPath = image.sprite == null ? "" : AssetDatabase.GetAssetPath(image.sprite),
                        resourceKey = image.sprite == null ? "" : ResourceKey(AssetDatabase.GetAssetPath(image.sprite)),
                        imageType = image.type.ToString(), hasSprite = image.sprite != null,
                        preserveAspect = image.preserveAspect, raycastTarget = image.raycastTarget,
                        spriteBorder = image.sprite == null ? Vector4.zero : image.sprite.border,
                        nativeSpriteSize = image.sprite == null ? Vector2.zero : image.sprite.rect.size,
                        color = image.color, rect = DesignRect(image.rectTransform, design)
                    }).ToArray();
                    var buttons = canvasObject.GetComponentsInChildren<Button>(false).Where(button => button.isActiveAndEnabled).ToArray();
                    shot.activeButtons = buttons.Select(button => new ButtonRecord
                    {
                        hierarchy = Hierarchy(button.transform, design), name = button.name,
                        interactable = button.IsInteractable(), rect = DesignRect((RectTransform)button.transform, design)
                    }).ToArray();
                    bool HasButton(string buttonName) => buttons.Any(button => button.name == buttonName);
                    bool previewPortrait = shot.activeImages.Any(record => record.resourceKey.Contains("/preview_portrait_"));
                    bool membersManaged = buttons.Any(button => button.name.StartsWith("Kick_", StringComparison.Ordinal));
                    var teamFrame = canvasObject.GetComponentsInChildren<RectTransform>(false).First(rect => rect.name == "TeamFrame");
                    shot.checks = new[]
                    {
                        new Check { name = "native window visible", passed = window.IsVisible },
                        new Check { name = "native close and refresh exist", passed = HasButton("CloseTeamWindow") && HasButton("RefreshTeam") },
                        new Check { name = "original art rectangle is 1931 by 814", passed = Mathf.Abs(teamFrame.rect.width-1931)<.01f && Mathf.Abs(teamFrame.rect.height-814)<.01f },
                        new Check { name = "original art has one uniform scale", passed = Mathf.Abs(teamFrame.localScale.x-teamFrame.localScale.y)<.0001f && Mathf.Abs(teamFrame.localScale.x-2560f/1931f)<.0001f },
                        new Check { name = "approved preview portraits require explicit fixture marker", passed = previewPortrait == preview },
                        new Check { name = "member state has no management buttons", passed = scenario != 1 || !membersManaged && !HasButton("StartTeamMatch") },
                        new Check { name = "no team state uses invitation list", passed = scenario != 2 || !state.HasTeam && HasButton("AcceptInvite_2001") },
                        new Check { name = "production unavailable state has no invented identities", passed = scenario != 3 || !previewPortrait && state.Snapshot.Members.Count == 0 && state.Snapshot.Applications.Count == 0 && state.Invites.Count == 0 }
                    };
                }
                catch (Exception exception) { AddFailure(failures, name + "_" + width + "x" + height, exception); }
                finally
                {
                    if (window != null)
                    {
                        window.Hide();
                        var windowRoot = design.Find("TeamWindow");
                        if (windowRoot != null) UnityEngine.Object.DestroyImmediate(windowRoot.gameObject);
                    }
                }
            }
        }
        catch (Exception exception) { AddFailure(failures, "resolution_" + width + "x" + height, exception); }
        finally
        {
            RenderTexture.active = previousTarget;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    // All identities and presence below are labeled offline fixtures; production never manufactures them.
    private static TeamUiState Fixture(bool preview, int scenario)
    {
        var state = new TeamUiState(() => 0f);
        if (scenario == 3) { state.Reset(101); state.SetUnavailable("组队服务尚未连接，等待同步真实队伍信息。"); return state; }
        TeamRole Role(ulong id, string name, uint level, string school, uint classId, uint gender, int portrait, bool online = true) =>
            new TeamRole { PlayerId = id, Name = name, Level = level, SchoolName = school, ClassId = classId,
                Gender = gender, CharacterId = preview ? TeamUiArt.PreviewPortraitPrefix + portrait : null,
                IsOnline = online, OnlineStatusKnown = true };
        var snapshot = new TeamSnapshot { TeamId = 10248, LeaderId = 101, LocalPlayerId = scenario == 1 ? 102UL : 101UL,
            Capacity = 5, MembershipEpoch = 1, Version = 1, ApplicationCount = 2, MatchFillSupported = true };
        snapshot.Members.Add(Role(101, "青云小道", 28, "道童", 1, 1, 1));
        snapshot.Members.Add(Role(102, "清铃", 26, "灵修", 2, 2, 2));
        snapshot.Members.Add(Role(103, "长风", 30, "剑修", 2, 1, 3, false));
        if (scenario != 1)
        {
            snapshot.Applications.Add(Role(201, "桂枝", 27, "医师", 3, 2, 4));
            snapshot.Applications.Add(Role(202, "云游子", 29, "符师", 4, 1, 5));
        }
        if (scenario == 2)
        {
            snapshot = new TeamSnapshot { LocalPlayerId = 101, Capacity = 5 };
            state.Sync(snapshot, new List<TeamInvite> { new TeamInvite { TeamId = 2001, LeaderId = 201, MemberCount = 3,
                Inviter = Role(201, "桂枝", 27, "医师", 3, 2, 4), ExpiresAt = 300f } }, true, true, TeamAction.None, 0, 0, "");
        }
        else state.SetSnapshot(snapshot);
        return state;
    }

    private static RectRecord DesignRect(RectTransform rect, RectTransform design)
    {
        var world = new UnityEngine.Vector3[4];
        rect.GetWorldCorners(world);
        Vector2[] corners = world.Select(point => design.InverseTransformPoint(point)).Select(point =>
            new Vector2(point.x - design.rect.xMin, design.rect.yMax - point.y)).ToArray();
        float minX = corners.Min(point => point.x), minY = corners.Min(point => point.y);
        float maxX = corners.Max(point => point.x), maxY = corners.Max(point => point.y);
        return new RectRecord { corners = corners, topLeft = new Vector2(minX, minY), size = new Vector2(maxX - minX, maxY - minY) };
    }
    private static string Hierarchy(UnityEngine.Transform child, RectTransform design)
    {
        var names = new List<string>();
        while (child != null)
        {
            names.Add(child.name);
            if (child == design) break;
            child = child.parent;
        }
        names.Reverse();
        return string.Join("/", names);
    }
    private static string ResourceKey(string assetPath)
    {
        const string marker = "/Resources/";
        string normalized = (assetPath ?? "").Replace('\\', '/');
        int offset = normalized.LastIndexOf(marker, StringComparison.Ordinal);
        if (offset < 0) return "";
        string relative = normalized.Substring(offset + marker.Length);
        string extension = Path.GetExtension(relative);
        return extension.Length == 0 ? relative : relative.Substring(0, relative.Length - extension.Length);
    }
    private static void AddFailure(List<Failure> failures, string stage, Exception exception)
    {
        failures.Add(new Failure { stage = stage, message = exception.Message, stacktrace = exception.ToString() });
    }
}
#endif
