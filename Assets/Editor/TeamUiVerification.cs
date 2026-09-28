#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
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

/// <summary>在临时编辑器场景中离线渲染正式组队窗口，不连接服务器或修改当前场景。</summary>
public static class TeamUiVerification
{
    public static string OutputDirectory { get; set; } = Path.GetFullPath(Path.Combine(
        Application.dataPath, "../.codex-artifacts/team-ui-v2"));

    private static GameObject _previewCanvas;
    private static TeamWindow _previewWindow;
    private static TeamUiState _previewState;
    private static TeamSnapshot _previewSnapshot;
    private static Action _previewCompletion;
    private static double _previewCompleteAt;

    [MenuItem("MMORPG/UI/Preview team UI (offline)")]
    public static void PreviewOffline()
    {
        if (!EditorApplication.isPlaying)
            throw new InvalidOperationException("离线交互预览仅可在编辑器运行模式下打开。");
        ClosePreview();
        try
        {
            _previewCanvas = new GameObject("[Offline Team UI Preview]", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _previewCanvas.hideFlags = HideFlags.DontSave;
            UnityEngine.Object.DontDestroyOnLoad(_previewCanvas);
            var canvas = _previewCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 350;
            var scaler = _previewCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("OfflinePreviewEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(_previewCanvas.transform, false);
            }
            var design = QdaoUguiFactory.CreateCenteredRect("OfflinePreviewDesign", _previewCanvas.transform, 2560, 1080);
            _previewWindow = new TeamWindow(design);
            _previewState = new TeamUiState(() => (float)EditorApplication.timeSinceStartup);
            _previewSnapshot = CreateSnapshot();
            _previewSnapshot.Applications.RemoveRange(2, _previewSnapshot.Applications.Count - 2);
            _previewState.SetSnapshot(_previewSnapshot);
            _previewState.Changed += RefreshPreview;
            _previewWindow.DecisionRequested += PreviewDecision;
            _previewWindow.RefreshRequested += PreviewRefresh;
            _previewWindow.CreateRequested += PreviewCreate;
            _previewWindow.ApplyRequested += PreviewApply;
            _previewWindow.InviteRequested += PreviewInvite;
            _previewWindow.InviteResponseRequested += PreviewInviteResponse;
            _previewWindow.LeaveRequested += PreviewLeave;
            _previewWindow.KickRequested += PreviewKick;
            _previewWindow.TransferRequested += PreviewTransfer;
            _previewWindow.DisbandRequested += PreviewDisband;
            _previewWindow.StartMatchRequested += PreviewStartMatch;
            _previewWindow.Closed += ClosePreview;
            RefreshPreview();
            _previewWindow.Show();
            var badge = QdaoUguiFactory.CreateText("OfflinePreviewLabel", design, 200, 12, 2160, 54,
                "离线界面预览 · 示例角色", 30, QdaoUguiTheme.Html("#FFF3D6"), TextAlignmentOptions.Center);
            badge.richText = false;
            badge.raycastTarget = false;
            foreach (var child in _previewCanvas.GetComponentsInChildren<UnityEngine.Transform>(true))
                child.gameObject.hideFlags = HideFlags.DontSave;
            EditorApplication.update += UpdatePreview;
            EditorApplication.playModeStateChanged += PreviewPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += ClosePreview;
        }
        catch
        {
            ClosePreview();
            throw;
        }
    }

    [MenuItem("MMORPG/UI/Preview team UI (offline)", true)]
    private static bool CanPreviewOffline() => EditorApplication.isPlaying;

    private static void PreviewDecision(ulong playerId, bool approve)
    {
        int token = _previewState.BeginDecision(playerId, approve);
        if (token == 0) return;
        _previewCompleteAt = EditorApplication.timeSinceStartup + .3;
        _previewCompletion = () =>
        {
            TeamRole role = _previewSnapshot.Applications.Find(candidate => candidate.PlayerId == playerId);
            if (role == null)
            {
                _previewState.Fail(token, "离线预览：申请已失效。");
                return;
            }
            _previewSnapshot.Applications.Remove(role);
            if (approve) _previewSnapshot.Members.Add(role);
            // Simulates the server's commit bump so the ordering rules accept the reply.
            _previewSnapshot.Version++;
            _previewState.Complete(token, _previewSnapshot,
                approve ? "离线预览：示例道友已加入队伍。" : "离线预览：已拒绝示例申请。");
        };
    }

    private static void PreviewRefresh()
    {
        int token = _previewState.BeginRefresh();
        if (token == 0) return;
        _previewCompleteAt = EditorApplication.timeSinceStartup + .3;
        _previewCompletion = () =>
        {
            _previewSnapshot.Version++;
            _previewState.Complete(token, _previewSnapshot, "离线预览：示例列表已刷新。");
        };
    }

    // The remaining intents only log: the offline preview never opens a network connection.
    private static void PreviewCreate() => Debug.Log("离线预览：创建队伍（未发送请求）。");
    private static void PreviewApply(ulong playerId) => Debug.Log("离线预览：申请加入玩家 " + playerId + " 的队伍（未发送请求）。");
    private static void PreviewInvite(ulong playerId) => Debug.Log("离线预览：邀请玩家 " + playerId + "（未发送请求）。");
    private static void PreviewInviteResponse(ulong teamId, bool accept) =>
        Debug.Log("离线预览：" + (accept ? "加入" : "谢绝") + "队伍 " + teamId + " 的邀请（未发送请求）。");
    private static void PreviewLeave() => Debug.Log("离线预览：离开队伍（未发送请求）。");
    private static void PreviewKick(ulong playerId) => Debug.Log("离线预览：请离玩家 " + playerId + "（未发送请求）。");
    private static void PreviewTransfer(ulong playerId) => Debug.Log("离线预览：转让队长给玩家 " + playerId + "（未发送请求）。");
    private static void PreviewDisband() => Debug.Log("离线预览：解散队伍（未发送请求）。");
    private static void PreviewStartMatch() => Debug.Log("离线预览：发起队伍战斗（未发送请求）。");

    private static void RefreshPreview() => _previewWindow?.SetState(_previewState);

    private static void UpdatePreview()
    {
        if (!EditorApplication.isPlaying || _previewCanvas == null)
        {
            ClosePreview();
            return;
        }
        if (_previewCompletion == null || EditorApplication.timeSinceStartup < _previewCompleteAt) return;
        Action complete = _previewCompletion;
        _previewCompletion = null;
        complete();
    }

    private static void PreviewPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode)
            ClosePreview();
    }

    private static void ClosePreview()
    {
        EditorApplication.update -= UpdatePreview;
        EditorApplication.playModeStateChanged -= PreviewPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= ClosePreview;
        _previewCompletion = null;
        if (_previewState != null) _previewState.Changed -= RefreshPreview;
        if (_previewWindow != null)
        {
            _previewWindow.Closed -= ClosePreview;
            _previewWindow.DecisionRequested -= PreviewDecision;
            _previewWindow.RefreshRequested -= PreviewRefresh;
            _previewWindow.CreateRequested -= PreviewCreate;
            _previewWindow.ApplyRequested -= PreviewApply;
            _previewWindow.InviteRequested -= PreviewInvite;
            _previewWindow.InviteResponseRequested -= PreviewInviteResponse;
            _previewWindow.LeaveRequested -= PreviewLeave;
            _previewWindow.KickRequested -= PreviewKick;
            _previewWindow.TransferRequested -= PreviewTransfer;
            _previewWindow.DisbandRequested -= PreviewDisband;
            _previewWindow.StartMatchRequested -= PreviewStartMatch;
            if (_previewCanvas != null) _previewWindow.ResetSession();
        }
        _previewWindow = null;
        _previewState = null;
        _previewSnapshot = null;
        if (_previewCanvas != null)
        {
            _previewCanvas.SetActive(false);
            UnityEngine.Object.DestroyImmediate(_previewCanvas);
        }
        _previewCanvas = null;
    }
    [MenuItem("MMORPG/UI/Capture team screens (offline)")]
    public static void CaptureAll()
    {
        Capture(2560, 1080);
        Capture(1920, 1080);
        File.WriteAllText(Path.Combine(OutputDirectory, "capture-status.txt"),
            "组队窗口离线截图完成：成员与申请双栏、独立分页、满员、无申请、服务不可用、处理中；" +
            "无队伍（有邀请 / 无邀请）、队员视角、队长行内操作、开战集合中、队员高亮、邀请输入框、请离确认框。\n" +
            "所有角色、邀请和审批结果仅用于 Editor 截图；未创建网络连接、未发送组队请求。\n");
    }

    public static void Capture(int width, int height)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("组队窗口截图需要编辑模式。");
        Directory.CreateDirectory(OutputDirectory);
        var previousScene = SceneManager.GetActiveScene();
        // A fresh batch editor starts in an untitled scene, which blocks additive scene creation.
        // Restrict this bootstrap switch to batch mode so an interactive user's unsaved scene is preserved.
        if (Application.isBatchMode && string.IsNullOrEmpty(previousScene.path))
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity", OpenSceneMode.Single);
            previousScene = SceneManager.GetActiveScene();
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D pixels = null;
        TeamWindow window = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("TeamCaptureCamera").AddComponent<Camera>();
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
            var canvasObject = new GameObject("TeamCaptureCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 2f;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform, 2560, 1080);
            window = new TeamWindow(design);
            var state = new TeamUiState(() => 0f);

            void Shoot(string name)
            {
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var child in go.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
                Canvas.ForceUpdateCanvases();
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var label in go.GetComponentsInChildren<TMP_Text>(true))
                    {
                        label.ForceMeshUpdate(true, true);
                        // An empty input field keeps a zero-width space in its text component.
                        if (!label.gameObject.activeInHierarchy || !label.enabled ||
                            string.IsNullOrWhiteSpace(label.text?.Replace("\u200B", string.Empty))) continue;
                        bool visibleGlyph = false;
                        for (int character = 0; character < label.textInfo.characterCount; character++)
                            visibleGlyph |= label.textInfo.characterInfo[character].isVisible;
                        if (!visibleGlyph)
                            throw new InvalidOperationException("组队文字未渲染：" + label.name + " / " + label.text);
                    }
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(OutputDirectory, name + "_" + width + "x" + height + ".png"), pixels.EncodeToPNG());
            }

            void Display(TeamSnapshot snapshot, string name)
            {
                window.ResetSession();
                // Each shot is an independent session, so ordering never rejects an unrelated example team.
                state.Reset(snapshot.LocalPlayerId);
                state.SetSnapshot(snapshot);
                window.SetState(state);
                window.Show();
                Shoot(name);
            }

            // Mirrors a TeamClient state the same way TeamUiRoot does on the live path; a null name skips the shot.
            void Mirror(TeamSnapshot snapshot, List<TeamInvite> invites, string name,
                TeamAction pending = TeamAction.None, ulong pendingTarget = 0, ulong highlight = 0, string status = "")
            {
                window.ResetSession();
                state.Sync(snapshot, invites, true, true, pending, pendingTarget, highlight, status);
                window.SetState(state);
                window.Show();
                if (name != null) Shoot(name);
            }

            void Click(string buttonName)
            {
                foreach (var button in canvasObject.GetComponentsInChildren<Button>())
                    if (button.name == buttonName && button.interactable)
                    {
                        button.onClick.Invoke();
                        return;
                    }
                throw new InvalidOperationException("截图按钮不可用：" + buttonName);
            }

            var few = CreateSnapshot();
            few.Applications.RemoveRange(2, few.Applications.Count - 2);
            Display(few, "dual-lists-two-applicants");

            var populated = CreateSnapshot();
            Display(populated, "dual-lists");
            Click("ApplicationNextPage");
            Shoot("application-page-2");

            var full = CreateSnapshot();
            full.Members.Add(Role(14, "云间客", 68, 3, 2));
            full.Members.Add(Role(15, "听雨", 64, 4, 1));
            Display(full, "full-team");

            var multipage = CreateSnapshot();
            multipage.Capacity = 6;
            multipage.Members.Add(Role(14, "云间客", 68, 3, 2));
            multipage.Members.Add(Role(15, "听雨", 64, 4, 1));
            multipage.Members.Add(Role(16, "第六位同行道友", 63, 2, 2));
            multipage.Applications.Add(Role(26, "南风", 63, 2, 1));
            multipage.Applications.Add(Role(27, "星河", 62, 4, 2));
            multipage.Applications.Add(Role(28, "青山", 61, 1, 1));
            multipage.Applications.Add(Role(29, "望月", 60, 3, 1));
            Display(multipage, "dual-lists-multipage");
            Click("MemberNextPage");
            Shoot("member-page-2");
            Click("ApplicationNextPage");
            Shoot("independent-pages-2");

            var empty = new TeamSnapshot
            {
                TeamId = 1001, LeaderId = 11, LocalPlayerId = 11, Capacity = 5, MembershipEpoch = 1, Version = 1,
            };
            empty.Members.Add(Role(11, "清风", 72, 1, 1));
            Display(empty, "empty-applications");

            state.Reset(11);
            state.SetUnavailable("组队服务暂未开放，当前无法获取申请列表。");
            window.SetState(state);
            window.Show(TeamPage.Applications);
            Shoot("service-unavailable");

            state.SetSnapshot(CreateSnapshot());
            state.BeginDecision(21, true);
            window.SetState(state);
            window.Show(TeamPage.Applications);
            Shoot("decision-pending");

            var receivedInvites = new List<TeamInvite>
            {
                Invite(301, Role(31, "灯火阑珊", 68, 1, 2), 3),
                Invite(302, Role(32, "月白", 67, 2, 1), 2),
                Invite(303, Role(33, "山海故人", 66, 3, 2), 4),
            };
            Mirror(NoTeam(11), receivedInvites, "no-team-invites");
            Mirror(NoTeam(11), new List<TeamInvite>(), "no-team-empty");

            var member = CreateSnapshot();
            member.LocalPlayerId = 12;
            member.Applications.Clear();
            member.ApplicationCount = 2;
            Mirror(member, null, "member-view");

            var leader = CreateSnapshot();
            leader.Applications.RemoveRange(2, leader.Applications.Count - 2);
            Mirror(leader, null, "leader-actions");

            var starting = CreateSnapshot();
            starting.Applications.RemoveRange(2, starting.Applications.Count - 2);
            starting.MatchStarting = true;
            Mirror(starting, null, "match-starting");

            Mirror(leader, null, "highlight-member", highlight: 13, status: "有队员不在线。");

            Mirror(leader, null, null);
            Click("InviteToTeam");
            Shoot("invite-modal");

            Mirror(leader, null, null);
            Click("Kick_12");
            Shoot("confirm-kick");
            Debug.Log("TEAM_UI_CAPTURE_OK|" + width + "x" + height + "|" + OutputDirectory);
        }
        finally
        {
            window?.Hide();
            window?.ResetSession();
            RenderTexture.active = previousTarget;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static TeamSnapshot CreateSnapshot()
    {
        var snapshot = new TeamSnapshot
        {
            TeamId = 1001, LeaderId = 11, LocalPlayerId = 11, Capacity = 5, MembershipEpoch = 1, Version = 1,
        };
        snapshot.Members.Add(Role(11, "清风", 72, 1, 1));
        snapshot.Members.Add(Role(12, "桂月", 70, 4, 2));
        snapshot.Members.Add(Role(13, "长街听笛", 69, 2, 1));
        snapshot.Members[2].IsOnline = false;
        snapshot.Applications.Add(Role(21, "灯火阑珊", 68, 1, 2));
        snapshot.Applications.Add(Role(22, "月白", 67, 2, 2));
        snapshot.Applications.Add(Role(23, "山海故人", 66, 3, 1));
        snapshot.Applications.Add(Role(24, "竹影", 65, 3, 2));
        snapshot.Applications.Add(Role(25, "踏歌行", 64, 4, 1));

        return snapshot;
    }

    private static TeamSnapshot NoTeam(ulong localPlayerId) =>
        new TeamSnapshot { LocalPlayerId = localPlayerId, MembershipEpoch = 2, Version = 0 };

    private static TeamInvite Invite(ulong teamId, TeamRole inviter, uint memberCount) => new TeamInvite
    {
        TeamId = teamId, LeaderId = inviter.PlayerId, MemberCount = memberCount, Inviter = inviter, ExpiresAt = 60f,
    };

    private static TeamRole Role(ulong id, string name, uint level, uint profession, uint gender)
    {
        string[] schools = { "未知", "剑修", "法修", "丹修", "体修" };
        return new TeamRole
        {
            PlayerId = id, Name = name, Level = level, ClassId = profession, Gender = gender,
            SchoolName = schools[(int)profession],
            CharacterId = MmorpgClient.World.QdaoCharacterCatalog.ResolveRole(profession, gender),
            IsLeader = id == 11, IsOnline = true,
        };
    }
}
#endif
