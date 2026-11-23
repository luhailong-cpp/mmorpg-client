#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Google.Protobuf;
using MmorpgClient.Game.Appearance;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using MmorpgClient.Table;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.World;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 易容界面换肤与人物背景/故事阅读的原生 uGUI 离线验证与截图导出工具（零真实网络与资产写入）。
/// </summary>
public static class AppearanceUiVerification
{
    public static string OutputDirectory { get; set; } =
        Path.GetFullPath(Path.Combine(Application.dataPath, "../.codex-artifacts/appearance-ui-verify"));

    private static readonly AppearanceMessageIds TestIds = new(442, 443, 444, 445, 447);

    private sealed class FixtureTransport : IBattleTransport
    {
        public ulong PlayerId { get; set; } = 9001;
        public bool IsReady { get; set; } = true;
        public event Action Disconnected;
        public int GetInfoCalls;
        public int SwitchCalls;
        public int StartTrialCalls;
        public int CancelTrialCalls;
        public PlayerAppearanceInfo Snapshot;

        private readonly Dictionary<uint, Action<MessageContent>> _notifies = new();

        public void RegisterNotify(uint messageId, Action<MessageContent> handler) => _notifies[messageId] = handler;

        public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
            Action<TResp> onResponse, Action<string> onError) where TResp : IMessage<TResp>
        {
            if (messageId == TestIds.Get)
            {
                GetInfoCalls++;
                onResponse((TResp)(IMessage)new GetAppearanceInfoResponse
                {
                    ErrorMessage = new TipInfoMessage { Id = 0 },
                    Info = Snapshot?.Clone(),
                });
                return;
            }
            if (messageId == TestIds.Switch) SwitchCalls++;
            else if (messageId == TestIds.StartTrial) StartTrialCalls++;
            else if (messageId == TestIds.CancelTrial) CancelTrialCalls++;
            onError("fixture-blocked-write");
        }

        public void SendOneWay(uint messageId, IMessage request) { }

        public void EmitDisconnect()
        {
            IsReady = false;
            Disconnected?.Invoke();
        }
    }

    [MenuItem("MMORPG/UI/易容形象/导出原生验证截图")]
    public static void CaptureAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("易容原生验证必须在 Edit Mode 执行");

        LoadCharacterAppearanceTableIfPresent();
        Directory.CreateDirectory(OutputDirectory);

        var summary = new List<string>();
        RunVerificationPass(2560, 1080, captureFullSuite: true, extraInsets: Vector4.zero, summary);
        RunVerificationPass(1280, 720, captureFullSuite: false, extraInsets: new Vector4(44f, 24f, 44f, 68f), summary);

        File.WriteAllText(Path.Combine(OutputDirectory, "verification-report.txt"),
            string.Join(Environment.NewLine, summary));
        Debug.Log("[AppearanceUiVerification] All checks and captures succeeded -> " + OutputDirectory);
    }

    private static void RunVerificationPass(int width, int height, bool captureFullSuite,
        Vector4 extraInsets, List<string> summary)
    {
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewPreviewScene();
        var previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D pixels = null;
        AppearanceUiRoot uiRoot = null;
        AppearanceClient client = null;
        GameObject hudNavToClean = null;

        try
        {
            var camera = new GameObject("AppearanceVerifyCamera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
            camera.scene = scene;
            camera.enabled = false;
            camera.transform.position = new UnityEngine.Vector3(0f, 0f, -10f);
            camera.orthographic = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = QdaoUguiTheme.Letterbox;

            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create();
            camera.targetTexture = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);

            var rootGo = new GameObject("AppearanceVerifyHost");
            SceneManager.MoveGameObjectToScene(rootGo, scene);
            uiRoot = rootGo.AddComponent<AppearanceUiRoot>();

            var transport = new FixtureTransport { Snapshot = BuildFixtureInfo(ownedCards: 2, activeTrialId: "") };
            var conn = new object();
            float clock = 100f;
            client = new AppearanceClient(transport, () => conn, () => clock, TestIds);
            uiRoot.AttachVerificationClient(client);
            if (WorldHudNavigation.Instance != null) hudNavToClean = WorldHudNavigation.Instance.gameObject;

            var canvas = uiRoot.RootCanvas;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 2f;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            uiRoot.SetExtraSafeInsetsForVerification(extraInsets);
            client.RequestInfo(force: true);
            uiRoot.ShowPanel();

            void Shoot(string name)
            {
                uiRoot.SetExtraSafeInsetsForVerification(extraInsets);
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var child in go.GetComponentsInChildren<UnityEngine.Transform>(true))
                        child.gameObject.layer = 31;
                Canvas.ForceUpdateCanvases();
                foreach (var label in uiRoot.WindowRoot.GetComponentsInChildren<TMP_Text>(true))
                    label.ForceMeshUpdate(true, true);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(OutputDirectory, $"{name}_{width}x{height}.png"), pixels.EncodeToPNG());
            }

            Button FindButton(string name)
            {
                var btn = uiRoot.WindowRoot.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == name);
                if (btn == null) throw new InvalidOperationException("未找到按钮：" + name);
                return btn;
            }

            void Click(string name)
            {
                var btn = FindButton(name);
                if (!btn.gameObject.activeInHierarchy || !btn.interactable)
                    throw new InvalidOperationException("按钮不可点击：" + name);
                btn.onClick.Invoke();
            }

            TMP_Text FindText(string name)
            {
                var label = uiRoot.WindowRoot.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == name);
                if (label == null) throw new InvalidOperationException("未找到文本：" + name);
                return label;
            }

            if (captureFullSuite)
            {
                // 验收 1：四位人物分别打开背景与故事，图文对应
                var expectedFour = new[]
                {
                    ("00_reference_topright_boy", "金发带Q道童", "陆照禾", "第四只碗", "回来带葱"),
                    ("10_crimson_spear_girl", "赤枪少女", "祝停云", "纸也赢过一回", "今天纸赢了"),
                    ("17_ghost_script_calligrapher_boy", "灵篆书生", "闻砚生", "借还栏", "名字，你来写"),
                    ("09_bamboo_archer_girl", "竹弓少女", "桑听雨", "没有画的那一页", "替我空一页吧"),
                };
                foreach (var (rawId, origName, archiveName, storyTitle, bodySnippet) in expectedFour)
                {
                    Click("AppearanceOption_" + rawId);
                    AssertTrue(uiRoot.BiographySection.activeSelf, $"应显示人物背景：{rawId}");
                    AssertTrue(FindText("AppearancePreviewName").text == origName, $"应保留原形象称呼：{origName}");
                    AssertTrue(FindText("BiographyIdentity").text.Contains(archiveName), $"人物档案应包含：{archiveName}");
                    if (rawId == "00_reference_topright_boy") Shoot("01-biography-summary-ahe");

                    Click("AppearanceReadStory");
                    AssertTrue(uiRoot.StoryOverlay.activeSelf, $"应打开故事阅读层：{rawId}");
                    AssertTrue(FindText("StoryTitle").text == storyTitle, $"故事篇名不匹配：{storyTitle}");
                    AssertTrue(uiRoot.StoryBodyText.text.Contains(bodySnippet), $"故事正文不匹配：{bodySnippet}");
                    if (rawId == "00_reference_topright_boy") Shoot("02-story-reader-ahe");

                    Click("AppearanceStoryBack");
                    AssertTrue(!uiRoot.StoryOverlay.activeSelf, "点击返回应关闭故事阅读层");
                }
                summary.Add($"[{width}x{height}] Check 1 PASS: 四位人物背景与故事图文一一对应");

                // 验收 2 & 3：跳过后可再次阅读；返回后仍选中原人物且列表位置不变
                uiRoot.ListScroll.verticalNormalizedPosition = 0.34f;
                Click("AppearanceOption_10_crimson_spear_girl");
                Click("AppearanceReadStory");
                Click("AppearanceStorySkip");
                AssertTrue(!uiRoot.StoryOverlay.activeSelf, "跳过应关闭阅读层");
                AssertTrue(uiRoot.PanelState.SelectedAppearanceId == "10_crimson_spear_girl", "跳过后仍应选中祝停云");
                AssertTrue(Mathf.Abs(uiRoot.ListScroll.verticalNormalizedPosition - 0.34f) < 0.02f, "跳过后列表滚动位置应不变");

                Click("AppearanceReadStory");
                AssertTrue(uiRoot.StoryOverlay.activeSelf, "跳过后应可再次阅读");
                AssertTrue(Mathf.Abs(uiRoot.StoryScroll.verticalNormalizedPosition - 1f) < 0.02f, "再次阅读应回到顶部");
                Click("AppearanceStoryFinish");
                AssertTrue(!uiRoot.StoryOverlay.activeSelf, "读完返回应关闭阅读层");
                AssertTrue(uiRoot.PanelState.SelectedAppearanceId == "10_crimson_spear_girl", "读完返回仍应选中祝停云");
                AssertTrue(Mathf.Abs(uiRoot.ListScroll.verticalNormalizedPosition - 0.34f) < 0.02f, "读完返回列表位置应不变");
                Shoot("03-returned-same-character-tingyun");
                summary.Add($"[{width}x{height}] Check 2 & 3 PASS: 跳过重读、读完返回仍选中祝停云且滚动位置保持 0.34");

                // 验收 4：切到无档案人物不残留上一人的正文，并截图试用倒计时态
                transport.Snapshot = BuildFixtureInfo(ownedCards: 2, activeTrialId: "01_ice_sword_girl");
                clock = 128f;
                client.RequestInfo(force: true);
                Click("AppearanceOption_01_ice_sword_girl");
                AssertTrue(!uiRoot.BiographySection.activeSelf, "无档案人物应收起人物背景区块");
                AssertTrue(string.IsNullOrEmpty(FindText("BiographyBody").text), "无档案人物不得残留上一人的背景");
                AssertTrue(string.IsNullOrEmpty(uiRoot.StoryBodyText.text), "无档案人物不得残留上一人的故事正文");
                Shoot("04-no-biography-and-trial-active");
                summary.Add($"[{width}x{height}] Check 4 PASS: 无档案人物收起背景区且零残留");

                // 验收 7：先点切换一次 → 打开故事 → 返回 → 再点切换 不会误扣卡
                transport.Snapshot = BuildFixtureInfo(ownedCards: 2, activeTrialId: "");
                client.RequestInfo(force: true);
                Click("AppearanceOption_00_reference_topright_boy");
                Click("AppearanceConfirmSwitch");
                AssertTrue(uiRoot.ConfirmOverlay.activeSelf, "首次点切换应弹出二次确认弹窗");
                Shoot("05-switch-confirm-dialog");
                Click("AppearanceConfirmCancel");
                uiRoot.PanelState.ArmLegacySwitchConfirmation("00_reference_topright_boy", clock);
                Click("AppearanceReadStory");
                AssertTrue(!uiRoot.PanelState.HasPendingSwitchConfirmation, "打开阅读层必须清除旧扣卡确认");
                Click("AppearanceStoryBack");
                Click("AppearanceConfirmSwitch");
                AssertTrue(uiRoot.ConfirmOverlay.activeSelf, "返回后首次点切换必须重进确认弹窗");
                AssertTrue(transport.SwitchCalls == 0, "不得直接扣卡发包");
                Click("AppearanceConfirmCancel");
                summary.Add($"[{width}x{height}] Check 7 PASS: 先点切换→读故事→返回→再点切换仅重进确认，SwitchCalls=0");

                // 额外视觉核验：背包 0 张卡弹出官方商城引导窗（04）
                transport.Snapshot = BuildFixtureInfo(ownedCards: 0, activeTrialId: "");
                client.RequestInfo(force: true);
                Click("AppearanceOption_00_reference_topright_boy");
                Click("AppearanceConfirmSwitch");
                AssertTrue(uiRoot.NoCardOverlay.activeSelf, "背包 0 张卡应弹缺卡商城引导");
                Shoot("06-no-card-mall-guide");
                Click("AppearanceNoCardCancel");

                // 验收 5：缺立绘仍可读人物背景与故事，显示缺图占位不借用他人立绘
                transport.Snapshot = BuildFixtureInfo(ownedCards: 2, activeTrialId: "");
                uiRoot.AttachVerificationClient(client, rawId => rawId == "09_bamboo_archer_girl" ? null : QdaoCharacterCatalog.LoadPortrait(rawId));
                client.RequestInfo(force: true);
                uiRoot.ShowPanel();
                Click("AppearanceOption_09_bamboo_archer_girl");
                var missingBox = uiRoot.WindowRoot.GetComponentsInChildren<UnityEngine.Transform>(true)
                    .First(t => t.name == "AppearancePortraitMissing").gameObject;
                AssertTrue(missingBox.activeSelf, "缺立绘时应显示缺图占位");
                AssertTrue(uiRoot.BiographySection.activeSelf, "缺立绘时仍应显示人物背景");
                Shoot("07-missing-portrait-readable");
                Click("AppearanceReadStory");
                AssertTrue(uiRoot.StoryOverlay.activeSelf && FindText("StoryTitle").text == "没有画的那一页", "缺立绘时仍可打开阅读故事");
                Click("AppearanceStoryBack");
                summary.Add($"[{width}x{height}] Check 5 PASS: 缺立绘显示占位且背景/故事正常可读");

                // 验收 6：快速切人、阅读中断线、关闭重开不串内容
                Click("AppearanceOption_00_reference_topright_boy");
                Click("AppearanceOption_10_crimson_spear_girl");
                Click("AppearanceOption_17_ghost_script_calligrapher_boy");
                Click("AppearanceReadStory");
                AssertTrue(uiRoot.StoryOverlay.activeSelf, "快速切人后应打开闻砚生故事");
                transport.EmitDisconnect();
                AssertTrue(!uiRoot.StoryOverlay.activeSelf, "断线时应立即收起故事阅读层");
                transport.IsReady = true;
                conn = new object();
                client.RequestInfo(force: true);
                uiRoot.ShowPanel();
                Click("AppearanceOption_10_crimson_spear_girl");
                AssertTrue(FindText("BiographyIdentity").text.Contains("祝停云"), "重开切回祝停云不得串成闻砚生");
                summary.Add($"[{width}x{height}] Check 6 PASS: 快速切人、断线与重开零串内容");

                // 验收 8：全程阅读无资产/试用/换形象请求
                AssertTrue(transport.SwitchCalls == 0 && transport.StartTrialCalls == 0 && transport.CancelTrialCalls == 0,
                    $"阅读过程不得产生写请求：switch={transport.SwitchCalls}, trial={transport.StartTrialCalls}, cancel={transport.CancelTrialCalls}");
                summary.Add($"[{width}x{height}] Check 8 PASS: 全程阅读写请求计数=0 (Switch=0, Trial=0, Cancel=0)");
            }
            else
            {
                // 窄横屏 1280×720 + 模拟刘海/底部手势/小游戏胶囊安全区 + 放大字号滚动至末行留白
                Click("AppearanceOption_10_crimson_spear_girl");
                Shoot("08-narrow-safearea-biography");
                Click("AppearanceReadStory");
                uiRoot.StoryBodyText.fontSize = 44f;
                uiRoot.RefreshStoryLayoutForVerification();
                Canvas.ForceUpdateCanvases();
                AssertTrue(uiRoot.StoryScroll.content.rect.height > uiRoot.StoryScroll.viewport.rect.height,
                    "窄横屏放大字号后故事正文必须提供真实纵向滚动");
                uiRoot.StoryScroll.verticalNormalizedPosition = 0f;
                Canvas.ForceUpdateCanvases();
                Shoot("09-narrow-story-enlarged-scroll-end");
                summary.Add($"[{width}x{height}] Narrow+SafeArea+EnlargedFontScroll PASS: contentH={uiRoot.StoryScroll.content.rect.height:F0} > viewportH={uiRoot.StoryScroll.viewport.rect.height:F0}");
            }
        }
        finally
        {
            client?.Dispose();
            if (uiRoot != null) UnityEngine.Object.DestroyImmediate(uiRoot.gameObject);
            if (hudNavToClean != null) UnityEngine.Object.DestroyImmediate(hudNavToClean);
            RenderTexture.active = previousTarget;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void LoadCharacterAppearanceTableIfPresent()
    {
        string pbPath = Path.Combine(Application.streamingAssetsPath, "Tables", CharacterAppearanceTableManager.BinaryFileName);
        if (File.Exists(pbPath))
            CharacterAppearanceTableManager.Instance.LoadFromBytes(File.ReadAllBytes(pbPath));
    }

    private static PlayerAppearanceInfo BuildFixtureInfo(uint ownedCards, string activeTrialId)
    {
        var info = new PlayerAppearanceInfo
        {
            PermanentAppearanceId = "08_alchemy_prodigy_boy",
            ActiveAppearanceId = string.IsNullOrEmpty(activeTrialId) ? "08_alchemy_prodigy_boy" : activeTrialId,
            TrialAppearanceId = activeTrialId ?? "",
            TrialExpiresAt = string.IsNullOrEmpty(activeTrialId) ? 0ul : 3700ul,
            ServerNow = 100ul,
            CardItemId = 3402u,
            CardItemCount = 1u,
            OwnedCardCount = ownedCards,
            TrialDurationSeconds = 3600u,
            MallSkuId = 3402u,
            MallPriceFen = 1000ul,
        };
        (uint id, string rawId, string name, bool trialUsed)[] roster =
        {
            (1, "08_alchemy_prodigy_boy", "炼丹童子", false),
            (2, "00_reference_topright_boy", "金发带Q道童", false),
            (3, "01_ice_sword_girl", "冰剑少女", !string.IsNullOrEmpty(activeTrialId)),
            (4, "02_fire_talisman_boy", "火符少年", true),
            (5, "03_lotus_healer_girl", "莲花医者", false),
            (6, "04_mountain_guardian_boy", "山岳守卫", false),
            (7, "05_celestial_musician_girl", "天音少女", false),
            (8, "09_bamboo_archer_girl", "竹弓少女", false),
            (9, "10_crimson_spear_girl", "赤枪少女", false),
            (10, "15_water_dragon_scholar_boy", "水龙书生", false),
            (11, "17_ghost_script_calligrapher_boy", "灵篆书生", false),
        };
        foreach (var (id, rawId, name, trialUsed) in roster)
        {
            info.Appearances.Add(new CharacterAppearanceEntry
            {
                ConfigId = id,
                AppearanceId = rawId,
                Name = name,
                SortOrder = id,
                TrialUsed = trialUsed,
            });
        }
        return info;
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[AppearanceUiVerification FAIL] " + message);
    }
}
#endif
