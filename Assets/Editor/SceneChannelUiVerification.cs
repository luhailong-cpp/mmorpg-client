#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MmorpgClient.Game.WorldTravel;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Gameplay;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 在临时编辑器场景里离线渲染正式的线路面板与右上角角标。不创建账号、连接或切线请求;
/// 画面里的线路都是只用于截图的示例数据。
/// </summary>
public static class SceneChannelUiVerification
{
    public static string OutputDirectory { get; set; } = Path.GetFullPath(Path.Combine(
        Application.dataPath, "../.codex-artifacts/scene-channel-ui"));

    [MenuItem("MMORPG/UI/Capture scene channel screens (offline)")]
    public static void CaptureAll()
    {
        Capture(2560, 1080);
        Capture(1920, 1080);
        File.WriteAllText(Path.Combine(OutputDirectory, "capture-status.txt"),
            "线路面板离线截图完成：多线（十六条，含流畅 / 繁忙 / 爆满 / 回收中 / 暂不可用 / 未知与当前线）、切换中、冷却中、" +
            "失败提示（通用 / 该线已满）、加载中（无目录）、单线、队伍成员置灰、分页（二十条，两页）、仅角标（3线 [L]）。\n" +
            "每张图都断言了所有可见文字至少有一个可见字形、没有被截断、没有字体里缺的字。\n" +
            "所有线路只用于 Editor 截图；这里没有创建网络连接，也没有发送列线或切线请求，联机切线另行验证。\n");
    }

    public static void Capture(int width, int height)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("线路面板截图需要编辑模式。");
        Directory.CreateDirectory(OutputDirectory);
        var previousScene = SceneManager.GetActiveScene();
        // 批处理模式下新开的编辑器停在未保存的无标题场景上,不能再叠加新场景;只在批处理时切到启动场景,
        // 交互使用时不动用户没保存的场景(做法同 TeamUiVerification)。
        if (Application.isBatchMode && string.IsNullOrEmpty(previousScene.path))
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity", OpenSceneMode.Single);
            previousScene = SceneManager.GetActiveScene();
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D pixels = null;
        SceneChannelWindow window = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("SceneChannelCaptureCamera").AddComponent<Camera>();
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
            var canvasObject = new GameObject("SceneChannelCaptureCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 2f;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var design = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform, 2560, 1080);
            // 角标先建、面板后建:与正式界面一样,面板的遮罩压在角标之上。
            var badge = SceneChannelUiRoot.CreateEntry(design, null);
            var badgeLabel = badge.GetComponentInChildren<TMP_Text>(true);
            window = new SceneChannelWindow(design);
            ulong requested = 0;
            window.SwitchRequested += sceneId => requested = sceneId;

            void Shoot(string name)
            {
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var child in go.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
                Canvas.ForceUpdateCanvases();
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var label in go.GetComponentsInChildren<TMP_Text>(true))
                    {
                        label.ForceMeshUpdate(true, true);
                        AssertGlyphs(label);
                    }
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(OutputDirectory, name + "_" + width + "x" + height + ".png"), pixels.EncodeToPNG());
            }

            // 角标与面板都按同一份视图画,口径与 SceneChannelUiRoot 一致;name 为 null 时只摆好不截图。
            void Display(SceneChannelPanelView view, string name)
            {
                uint current = SceneChannelModels.CurrentChannelNo(view.Lines);
                string badgeText = SceneChannelUiRoot.EntryLabel(view.Context.HasDirectory, current, view.Context.SwitchPending);
                badge.gameObject.SetActive(badgeText != null);
                if (badgeText != null) badgeLabel.text = badgeText;
                // 每张图都当作一次新打开:先关再开,页码回到当前线所在的那一页。
                window.Hide();
                window.SetView(view);
                window.Show();
                if (name != null) Shoot(name);
            }

            Button Find(string buttonName)
            {
                foreach (var button in canvasObject.GetComponentsInChildren<Button>())
                    if (button.name == buttonName) return button;
                throw new InvalidOperationException("截图里找不到按钮：" + buttonName);
            }

            void Click(string buttonName)
            {
                var button = Find(buttonName);
                if (!button.interactable) throw new InvalidOperationException("截图按钮不可用：" + buttonName);
                button.onClick.Invoke();
            }

            void ExpectDisabled(string buttonName)
            {
                if (Find(buttonName).interactable)
                    throw new InvalidOperationException("这个按钮此时应该置灰：" + buttonName);
            }

            // 多线:五种状态 + 未知 + 当前线(3线)。顺带核对点击:可切的行发出它的 scene_id,不可切的行置灰。
            Display(View(Lines(16, 3), Ready()), "multi-line");
            Click(SceneChannelWindow.LineButtonPrefix + 2);
            if (requested != SceneId(2))
                throw new InvalidOperationException("点击 2线发出的 scene_id 不对：" + requested);
            ExpectDisabled(SceneChannelWindow.LineButtonPrefix + 3);   // 当前线
            ExpectDisabled(SceneChannelWindow.LineButtonPrefix + 4);   // 爆满
            ExpectDisabled(SceneChannelWindow.LineButtonPrefix + 7);   // 回收中
            ExpectDisabled(SceneChannelWindow.LineButtonPrefix + 8);   // 暂不可用

            // 切换中:整张面板置灰,目标行标「切换中…」,角标同步显示。
            var switching = Ready();
            switching.SwitchPending = true;
            var switchingView = View(Lines(16, 3), switching, SceneChannelClient.SwitchingText(5));
            switchingView.SwitchTargetSceneId = SceneId(5);
            Display(switchingView, "switching");
            ExpectDisabled(SceneChannelWindow.LineButtonPrefix + 2);
            ExpectDisabled(SceneChannelWindow.RefreshButtonName);

            // 冷却中:刚切到 5线,本地倒计时还剩 7.4 秒(状态行向上取整显示 8 秒)。
            var cooling = Ready();
            cooling.CooldownRemainingSeconds = 7.4f;
            Display(View(Lines(16, 5), cooling, SceneChannelClient.SwitchedText(5)), "cooldown");
            ExpectDisabled(SceneChannelWindow.LineButtonPrefix + 2);

            // 失败提示:服务端只回一个通用失败码;刷新目录后若那条线已满,文案换成具体原因。
            Display(View(Lines(16, 3), Ready(), SceneChannelClient.SwitchFailedMessage, true), "switch-failed");
            Display(View(Lines(16, 3), Ready(), SceneChannelClient.LineFullText(4), true), "switch-failed-line-full");

            // 加载中:还没拿到目录,角标不显示。
            var loading = new SceneChannelPanelView
            {
                Lines = SceneChannelModels.NoLines,
                Context = new SceneChannelSwitchContext { Connected = true },
                Loading = true,
            };
            Display(loading, "loading");

            Display(View(Lines(1, 1), Ready()), "single-line");

            // 队伍成员:只有队长能切线,整张面板置灰并说明原因。
            var follower = Ready();
            follower.TeamFollower = true;
            Display(View(Lines(16, 3), follower), "team-member");
            ExpectDisabled(SceneChannelWindow.LineButtonPrefix + 2);

            // 分页:二十条,每页十六条。
            Display(View(Lines(20, 3), Ready()), "paging-page-1");
            Click(SceneChannelWindow.NextPageButtonName);
            Shoot("paging-page-2");
            Find(SceneChannelWindow.LineButtonPrefix + 20);

            // 仅角标:面板关着时主界面右上角的样子(背景是截图用的纯色,不是游戏画面)。
            // 线名后面带快捷键提示,写法与其它 HUD 入口一致;放不下的话 Shoot 里的截断断言会报。
            Display(View(Lines(16, 3), Ready()), null);
            if (badgeLabel.text != "3线 [L]")
                throw new InvalidOperationException("角标文案不对：" + badgeLabel.text);
            window.Hide();
            Shoot("hud-badge");

            Debug.Log("SCENE_CHANNEL_UI_CAPTURE_OK|" + width + "x" + height + "|" + OutputDirectory);
        }
        finally
        {
            window?.Hide();
            RenderTexture.active = previousTarget;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// 字形断言(调用前已 ForceMeshUpdate)。三道:
    ///  1. 可见的非空文字至少排出一个可见字形 —— 文本框太矮时整行会被省略成空白(口径同 TeamUiVerification);
    ///  2. 文字没有被截断 —— 文本框太窄时末尾会被换成省略号,第 1 道查不出;
    ///  3. 文字里没有字体缺的字 —— 字体是动态图集,排版时用到的字会被加进字表;排完仍不在字表(连回退字体也没有)
    ///     的字,画面上是方框或空白,第 1 道同样查不出。
    /// </summary>
    private static void AssertGlyphs(TMP_Text label)
    {
        if (!label.gameObject.activeInHierarchy || !label.enabled || string.IsNullOrWhiteSpace(label.text)) return;
        bool visibleGlyph = false;
        for (int character = 0; character < label.textInfo.characterCount; character++)
            visibleGlyph |= label.textInfo.characterInfo[character].isVisible;
        if (!visibleGlyph)
            throw new InvalidOperationException("线路界面文字未渲染：" + label.name + " / " + label.text);
        if (label.isTextTruncated)
            throw new InvalidOperationException("线路界面文字被截断（文本框太小）：" + label.name + " / " + label.text);
        if (label.font == null) return;
        var printable = new StringBuilder(label.text.Length);
        foreach (char character in label.text)
            if (!char.IsWhiteSpace(character) && !char.IsControl(character)) printable.Append(character);
        if (label.font.HasCharacters(printable.ToString(), out uint[] missing, true, false)) return;
        var codes = new List<string>();
        if (missing != null)
            foreach (uint code in missing) codes.Add("U+" + code.ToString("X4"));
        throw new InvalidOperationException("线路界面文字缺字形：" + label.name + " / " + label.text +
                                            " / " + label.font.name + " 缺 " + string.Join(" ", codes));
    }

    // ── 示例数据 ────────────────────────────────────────────────────────────

    private static ulong SceneId(uint channelNo) => 9000UL + channelNo;

    /// <summary>可以切线的基准状态:已连接、目录就绪、服务端开放切线、不在冷却。</summary>
    private static SceneChannelSwitchContext Ready() =>
        new SceneChannelSwitchContext { Connected = true, HasDirectory = true, SwitchEnabled = true };

    private static SceneChannelPanelView View(IReadOnlyList<SceneChannelLine> lines, SceneChannelSwitchContext context,
        string status = "", bool isError = false) =>
        new SceneChannelPanelView { Lines = lines, Context = context, Status = status, StatusIsError = isError };

    /// <summary>线号 1..count 的示例线路;状态按线号循环铺开,前十六条里六种状态都出现。</summary>
    private static List<SceneChannelLine> Lines(int count, uint currentChannelNo)
    {
        var lines = new List<SceneChannelLine>(count);
        for (uint channelNo = 1; channelNo <= count; channelNo++)
            lines.Add(new SceneChannelLine(SceneId(channelNo), channelNo, 20 * channelNo, LoadOf(channelNo),
                channelNo == currentChannelNo));
        return lines;
    }

    private static SceneChannelLoad LoadOf(uint channelNo)
    {
        switch (channelNo)
        {
            case 4: case 11: return SceneChannelLoad.Full;
            case 7: return SceneChannelLoad.Closing;
            case 8: return SceneChannelLoad.Unavailable;
            case 16: return SceneChannelLoad.Unknown;
            default: return channelNo % 2 == 0 ? SceneChannelLoad.Busy : SceneChannelLoad.Smooth;
        }
    }
}
#endif
