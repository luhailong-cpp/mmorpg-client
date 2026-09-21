#if UNITY_EDITOR
using System;
using System.IO;
using Google.Protobuf;
using Guildpb;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Guild;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Guild;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>帮会正式窗口的离线样例验收；不连接服务端，不保存或覆盖用户场景。</summary>
public static class GuildUiVerification
{
    public static string OutputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../.codex-artifacts/guild-ui-v2"));
    private static GameObject _preview;
    private static GuildClient _previewClient;
    private static Func<string, bool> _captureFilter;

    [MenuItem("MMORPG/UI/Preview guild UI (offline)")]
    public static void PreviewOffline()
    {
        ClosePreview();
        _preview = new GameObject("[Offline Guild Preview]", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _preview.hideFlags = HideFlags.DontSave;
        var canvas = _preview.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 360;
        var scaler = _preview.GetComponent<CanvasScaler>(); ConfigureScaler(scaler);
        if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
        {
            var events = new GameObject("GuildPreviewEvents", typeof(EventSystem), typeof(StandaloneInputModule));
            events.transform.SetParent(_preview.transform, false);
        }
        var design = QdaoUguiFactory.CreateCenteredRect("Design", _preview.transform, 2560, 1080);
        var window = new GuildWindow(design);
        _previewClient = new GuildClient(new FixtureTransport());
        _previewClient.Changed += () => window.SetClient(_previewClient);
        window.RankRequested += page => _previewClient.Browse(page);
        window.RefreshRequested += () => _previewClient.Refresh();
        _previewClient.Refresh(); window.SetClient(_previewClient); window.Show();
        Badge(design);
        foreach (var child in _preview.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.hideFlags = HideFlags.DontSave;
        AssemblyReloadEvents.beforeAssemblyReload += ClosePreview;
    }

    [MenuItem("MMORPG/UI/Close guild preview")]
    public static void ClosePreview()
    {
        _previewClient?.Dispose(); _previewClient = null;
        if (_preview != null) UnityEngine.Object.DestroyImmediate(_preview);
        _preview = null; AssemblyReloadEvents.beforeAssemblyReload -= ClosePreview;
    }

    [MenuItem("MMORPG/UI/Capture guild screens (offline)")]
    public static void CaptureAll()
    {
        Capture(2560, 1080); Capture(1920, 1080);
        File.WriteAllText(Path.Combine(OutputDirectory, "capture.json"),
            "{\"status\":\"passed\",\"source\":\"production GuildWindow with offline fixtures\",\"screenshots\":20,\"liveServerVerification\":false}");
        Debug.Log("Guild UI capture completed: " + OutputDirectory);
    }

    public static void CaptureEmptyStates()
    {
        _captureFilter = name => name == "08-empty" || name == "09-long-name-confirmation";
        try { Capture(2560, 1080); Capture(1920, 1080); }
        finally { _captureFilter = null; }
        Debug.Log("Guild UI empty-state recapture completed: " + OutputDirectory);
    }
    private static void Capture(int width, int height)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请在编辑模式验收帮会界面。");
        Directory.CreateDirectory(OutputDirectory);
        var previousScene = SceneManager.GetActiveScene();
        if (Application.isBatchMode && string.IsNullOrEmpty(previousScene.path))
        { EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity", OpenSceneMode.Single); previousScene = SceneManager.GetActiveScene(); }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var previousTarget = RenderTexture.active;
        RenderTexture target = null; Texture2D pixels = null; GuildWindow window = null; GuildClient client = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("GuildCaptureCamera").AddComponent<Camera>();
            camera.enabled = false; camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
            camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = QdaoUguiTheme.Letterbox;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create(); camera.targetTexture = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            var canvasObject = new GameObject("GuildCaptureCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 2f;
            ConfigureScaler(canvasObject.GetComponent<CanvasScaler>());
            var design = QdaoUguiFactory.CreateCenteredRect("Design", canvasObject.transform, 2560, 1080);
            window = new GuildWindow(design);
            var net = new FixtureTransport(); client = new GuildClient(net);
            client.Changed += () => window.SetClient(client);
            window.RankRequested += page => client.Browse(page);
            client.Refresh(); window.SetClient(client); Badge(design);
            void Shoot(string name)
            {
                if (_captureFilter != null && !_captureFilter(name)) return;
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var child in go.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
                Canvas.ForceUpdateCanvases();
                foreach (var label in canvasObject.GetComponentsInChildren<TMP_Text>(true)) label.ForceMeshUpdate(true, true);
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(OutputDirectory, name + "_" + width + "x" + height + ".png"), pixels.EncodeToPNG());
            }
            foreach (GuildPage page in Enum.GetValues(typeof(GuildPage)))
            { window.Show(page); Shoot("0" + ((int)page + 1) + "-" + page.ToString().ToLowerInvariant()); }
            window.Show(GuildPage.Overview);
            foreach (var button in canvasObject.GetComponentsInChildren<Button>())
                if (button.name == "EditGuildAnnouncement") { button.onClick.Invoke(); break; }
            Shoot("07-announcement");
            window.Back(); net.HasGuild = false; client.Reset(); client.Refresh(); window.Show();
            Shoot("08-empty");
            net.LongName = true; window.Show(GuildPage.Ranking);
            foreach (var button in canvasObject.GetComponentsInChildren<Button>())
                if (button.name == "JoinGuild_88001") { button.onClick.Invoke(); break; }
            Shoot("09-long-name-confirmation"); window.Back();
            net.HasGuild = true; client.Refresh(); net.FailNext = true; client.Refresh(); window.Show();
            Shoot("10-reconnect-required");
        }
        finally
        {
            window?.Hide(); client?.Dispose(); RenderTexture.active = previousTarget;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
        }
    }
    private static void ConfigureScaler(CanvasScaler scaler)
    {
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2560, 1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
    }
    private static void Badge(UnityEngine.Transform parent) =>
        QdaoUguiFactory.CreateText("OfflineGuildBadge", parent, 200, 12, 2160, 45,
            "离线界面验收 · 示例数据 · 正式窗口由帮会服务返回数据", 25, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);

    private sealed class FixtureTransport : IBattleTransport
    {
        public ulong PlayerId => 10001;
        public bool IsReady => true;
        public bool HasGuild = true;
        public bool LongName, FailNext;
        public event Action Disconnected;
        public void RegisterNotify(uint id, Action<MessageContent> handler) { }
        public void SendOneWay(uint id, IMessage message) { }
        public void Call<T>(uint id, IMessage message, MessageParser<T> parser, Action<T> success, Action<string> error) where T : IMessage<T>
        {
            if (FailNext) { FailNext = false; error("rpc timeout"); return; }
            IMessage response;
            if (id == MessageIds.GetPlayerGuild)
                response = HasGuild ? new GetPlayerGuildResponse { Guild = Fixture() } :
                    new GetPlayerGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } };
            else if (id == MessageIds.GetGuildRank)
            {
                var ranks = new GetGuildRankResponse { Page = 1, PageSize = 5, TotalCount = 5 };
                string[] names = { "清风明月", "云水同心", "青莲仙友", "桃源结义", "星河听雨" };
                for (int i = 0; i < 5; i++) ranks.Entries.Add(new GuildRankEntry { GuildId = (ulong)(88001 + i),
                    Name = i == 0 && LongName ? "青山常在明月长明同道相守万家灯火清风归处岁岁平安" : names[i], LeaderId = (ulong)(10001 + i), Level = (uint)(5 - i), MemberCount = (uint)(32 - i * 3),
                    Score = 88600 - i * 4200, Rank = (uint)(i + 1) });
                response = ranks;
            }
            else { error("离线验收不执行服务端操作"); return; }
            success((T)response);
        }
        private static GuildInfo Fixture()
        {
            var info = new GuildInfo { GuildId = 88001, Name = "清风明月", LeaderId = 10001, Level = 5, MaxMembers = 50,
                Announcement = "同道相逢，皆是有缘。\n\n愿每一盏灯，都照亮归家的路；愿每一次同行，都不负山海与明月。\n\n帮会事务请与帮主、长老联系。", ZoneId = 1 };
            for (ulong i = 0; i < 12; i++) info.Members.Add(new GuildMember { PlayerId = 10001 + i,
                Role = i == 0 ? 3u : i == 1 ? 1u : 0u, ContributionTotal = 3560 - i * 130, Online = i < 7 });
            return info;
        }
    }
}
#endif
