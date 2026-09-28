#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chatpb;
using MmorpgClient.Game.Social;
using MmorpgClient.Game.Team;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Social;
using MmorpgClient.UI.Ugui.Team;
using MmorpgClient.World;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Offline screenshots of actual runtime controls. No GameClient, connection, or live invitation is created.</summary>
public static class TeamInvitationVerification
{
    public static string OutputDirectory = "E:/work/image/designs/team-invites-20260928/qa";

    [MenuItem("MMORPG/UI/Capture team invitation screens (offline)")]
    public static void CaptureAll()
    {
        Directory.CreateDirectory(OutputDirectory);
        Capture(2560, 1080);
        Capture(1920, 1080);
        File.WriteAllText(Path.Combine(OutputDirectory, "capture-status.txt"),
            "Actual Unity TeamInvitationWindow and SocialWindow controls rendered at 2560x1080 and 1920x1080.\n" +
            "Friends, nearby, online, chat, no-team, empty, loading, and chat-profile invitation entry.\n" +
            "All players are explicit offline screenshot fixtures; no connection or invitation was sent.\n");
    }

    public static void Capture(int width, int height)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run invitation captures in EditMode.");
        Directory.CreateDirectory(OutputDirectory);
        var previousScene = SceneManager.GetActiveScene();
        if (Application.isBatchMode && string.IsNullOrEmpty(previousScene.path))
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity", OpenSceneMode.Single);
            previousScene = SceneManager.GetActiveScene();
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D pixels = null;
        TeamInvitationWindow window = null;
        SocialWindow social = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("InvitationCaptureCamera").AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = QdaoUguiTheme.Letterbox;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create(); camera.targetTexture = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            var canvasObject = new GameObject("InvitationCaptureCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 2;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform, 2560, 1080);
            window = new TeamInvitationWindow(design);
            var state = new TeamUiState(() => 0);
            var snapshot = new TeamSnapshot { TeamId = 1001, LocalPlayerId = 11, LeaderId = 11, Capacity = 5, Version = 1 };
            snapshot.Members.Add(Role(11, "青云小道", 36, 1, 1));
            snapshot.PendingInvites.Add(Role(22, "月白", 35, 2, 1));
            state.Sync(snapshot, null, true, true, TeamAction.None, 0, 0, "");
            window.SetState(state);
            var candidates = new List<TeamRole>
            {
                Role(21,"清铃",36,4,2), Role(22,"月白",35,2,1),
                Role(23,"长街听笛",34,3,2), Role(24,"山海故人",38,1,2),
                Role(25,"竹间客",33,4,1), Role(26,"桂枝",32,3,2),
                Role(27,"踏歌行",31,2,1), Role(28,"云游子",37,1,1)
            };
            candidates[4].IsOnline = false;

            void Shoot(string name)
            {
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var child in go.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
                Canvas.ForceUpdateCanvases();
                foreach (var label in canvasObject.GetComponentsInChildren<TMP_Text>())
                {
                    label.ForceMeshUpdate(true, true);
                    if (!label.enabled || string.IsNullOrWhiteSpace(label.text?.Replace("\u200B", ""))) continue;
                    bool any = false;
                    for (int i = 0; i < label.textInfo.characterCount; i++) any |= label.textInfo.characterInfo[i].isVisible;
                    if (!any) throw new InvalidOperationException("Invitation capture text missing: " + label.name + " / " + label.text);
                }
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(OutputDirectory, name + "_" + width + "x" + height + ".png"), pixels.EncodeToPNG());
            }

            foreach (TeamInvitationSource source in Enum.GetValues(typeof(TeamInvitationSource)))
            {
                window.Show(source);
                var rows = TeamSnapshot.CloneRoles(candidates);
                if (source == TeamInvitationSource.Chat) foreach (var row in rows) { row.IsOnline = false; row.OnlineStatusKnown = false; }
                if (source == TeamInvitationSource.Nearby || source == TeamInvitationSource.Online) rows.RemoveAll(row => !row.IsOnline);
                window.SetCandidates(source, rows, false, source == TeamInvitationSource.Nearby ? "当前场景中已出现的道友。" : "", source == TeamInvitationSource.Online);
                Shoot("invitation-" + source.ToString().ToLowerInvariant());
            }
            window.Show(TeamInvitationSource.Online);
            window.SetCandidates(TeamInvitationSource.Online, new TeamRole[0], false, "", true);
            Shoot("invitation-empty-online");
            window.SetCandidates(TeamInvitationSource.Online, new TeamRole[0], true, "");
            Shoot("invitation-loading");
            var noTeam = new TeamSnapshot { LocalPlayerId = 11, MembershipEpoch = 2 };
            state.Sync(noTeam, null, true, true, TeamAction.None, 0, 0, ""); window.SetState(state);
            window.Show(TeamInvitationSource.Friends);
            window.SetCandidates(TeamInvitationSource.Friends, candidates, false, "");
            Shoot("invitation-create-team");
            window.Hide();

            // This is a fixture-only SocialState using the production profile menu, without a SocialClient.
            var socialState = new SocialState();
            socialState.Reset(11); socialState.RuntimeStatus(true, false, false, "离线截图 · 示例消息");
            socialState.ApplyHistory(SocialChannel.World, new[] {
                new ChatMessage { SenderPlayerId = 21, Channel = ChatChannelType.World, Content = "青岚竹海寻灵鹤，有道友一起吗？", SendTimeMs = 1000 },
                new ChatMessage { SenderPlayerId = 11, Channel = ChatChannelType.World, Content = "我来，收好葫芦就出发。", SendTimeMs = 2000 }
            });
            social = new SocialWindow(design, socialState);
            social.TeamInviteRequested += _ => { };
            social.Show(SocialPage.World);
            var profile = canvasObject.GetComponentsInChildren<Button>().Single(b => b.name == "SocialPortrait_21Button");
            profile.onClick.Invoke();
            Shoot("chat-profile-invitation");
            Debug.Log("TEAM_INVITATION_CAPTURE_OK|" + width + "x" + height + "|" + OutputDirectory);
        }
        finally
        {
            social?.Dispose(); window?.Hide(); window?.ResetSession();
            RenderTexture.active = previousTarget;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static TeamRole Role(ulong id, string name, uint level, uint profession, uint gender) => new TeamRole
    {
        PlayerId = id, Name = name, Level = level, ClassId = profession, Gender = gender, IsOnline = true,
        SchoolName = profession switch { 1 => "破军", 2 => "玄霄", 3 => "丹心", _ => "逐风" },
        CharacterId = QdaoCharacterCatalog.ResolveRole(profession, gender)
    };
}
#endif
