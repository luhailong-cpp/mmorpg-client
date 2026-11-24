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

        var failedInventories = MmorpgClient.Editor.Tianyong.QdaoOriginalHdResourceIndexBuilder.RebuildAll();
        AssertTrue(failedInventories.Count == 0, "角色资产索引校验失败：" + string.Join(", ", failedInventories));

        LoadCharacterAppearanceTableIfPresent();
        Directory.CreateDirectory(OutputDirectory);

        var summary = new List<string>();
        RunVerificationPass(2560, 1080, captureFullSuite: true, extraInsets: Vector4.zero, summary);
        RunVerificationPass(1920, 1080, captureFullSuite: false, extraInsets: Vector4.zero, summary);
        RunVerificationPass(896, 414, captureFullSuite: false, extraInsets: new Vector4(36f, 18f, 36f, 24f), summary);

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

            GameObject FindObject(string name)
            {
                var tr = uiRoot.WindowRoot.GetComponentsInChildren<UnityEngine.Transform>(true).FirstOrDefault(t => t.name == name);
                if (tr == null) throw new InvalidOperationException("未找到节点：" + name);
                return tr.gameObject;
            }

            var biographyOverlay = FindObject("AppearanceBiographyOverlay");
            var portraitOverlay = FindObject("AppearancePortraitOverlay");

            if (captureFullSuite)
            {
                // 验收 1：三位人物分别打开小传、大图预览与故事，核对图文对应及逐层返回
                var expectedThree = new[]
                {
                    ("00_reference_topright_boy", "金发带Q道童", "陆照禾", "第四只碗", "回来带葱"),
                    ("10_crimson_spear_girl", "赤枪少女", "祝停云", "纸也赢过一回", "今天纸赢了"),
                    ("09_bamboo_archer_girl", "竹弓少女", "桑听雨", "没有画的那一页", "替我空一页吧"),
                };
                foreach (var (rawId, origName, archiveName, storyTitle, bodySnippet) in expectedThree)
                {
                    Click("AppearanceOption_" + rawId);
                    AssertTrue(!FindObject("AppearancePortraitMissing").activeSelf, $"正式角色立绘应通过 AcquireCreationPortrait 正常加载：{rawId}");
                    AssertTrue(uiRoot.BiographySection.activeSelf, $"应显示人物背景：{rawId}");
                    AssertTrue(FindText("AppearancePreviewName").text.Contains(archiveName), $"人物主标题应包含档案名：{archiveName}");
                    AssertTrue(FindText("AppearanceArchiveTag").text == origName, $"副标应保留原形象称呼：{origName}");
                    AssertTrue(FindText("BiographyIdentity").text.Contains(archiveName), $"人物档案应包含：{archiveName}");
                    if (rawId == "00_reference_topright_boy") Shoot("01-biography-summary-ahe");

                    Click("AppearanceReadStory");
                    AssertTrue(biographyOverlay.activeSelf, $"点击读人物志应打开人物小传层：{rawId}");
                    AssertTrue(FindText("BiographyName").text.Contains(archiveName), $"小传标题应包含：{archiveName}");
                    if (rawId == "00_reference_topright_boy")
                    {
                        Shoot("02a-biography-overlay-ahe");
                        Click("BiographyViewPortrait");
                        AssertTrue(portraitOverlay.activeSelf, "从小传点击查看形象应打开独立大图层");
                        Shoot("02b-portrait-preview-ahe");
                        Click("PortraitReturn");
                        AssertTrue(!portraitOverlay.activeSelf && biographyOverlay.activeSelf, "独立大图返回应回到人物小传层");
                    }

                    Click("BiographyReadStory");
                    AssertTrue(uiRoot.StoryOverlay.activeSelf, $"从小传进入应打开故事阅读层：{rawId}");
                    AssertTrue(FindText("StoryTitle").text == storyTitle, $"故事篇名不匹配：{storyTitle}");
                    AssertTrue(uiRoot.StoryBodyText.text.Contains(bodySnippet), $"故事正文不匹配：{bodySnippet}");
                    if (rawId == "00_reference_topright_boy") Shoot("02c-story-reader-ahe");

                    Click("AppearanceStoryBack");
                    AssertTrue(!uiRoot.StoryOverlay.activeSelf && biographyOverlay.activeSelf, "故事普通返回应逐层回到人物小传");
                    Click("BiographyBack");
                    AssertTrue(!biographyOverlay.activeSelf, "小传返回应回到主详情");
                }
                summary.Add($"[{width}x{height}] Check 1 PASS: 三位人物小传→大图→往事图文一一对应且逐层返回正确");

                // 验收 2 & 3：跳过直接回详情且可重读；读完返回仍选中原人物且列表位置不变
                uiRoot.ListScroll.verticalNormalizedPosition = 0.34f;
                Click("AppearanceOption_10_crimson_spear_girl");
                Click("AppearanceReadStory");
                Click("BiographyReadStory");
                Click("AppearanceStorySkip");
                AssertTrue(!uiRoot.StoryOverlay.activeSelf && !biographyOverlay.activeSelf, "跳过应直接回详情并关闭全部阅读层");
                AssertTrue(uiRoot.PanelState.SelectedAppearanceId == "10_crimson_spear_girl", "跳过后仍应选中祝停云");
                AssertTrue(Mathf.Abs(uiRoot.ListScroll.verticalNormalizedPosition - 0.34f) < 0.02f, "跳过后列表滚动位置应不变");

                Click("AppearanceReadStory");
                Click("BiographyReadStory");
                AssertTrue(uiRoot.StoryOverlay.activeSelf, "跳过后应可再次阅读");
                AssertTrue(Mathf.Abs(uiRoot.StoryScroll.verticalNormalizedPosition - 1f) < 0.02f, "未滚动重开仍在顶部");
                Click("AppearanceStoryFinish");
                AssertTrue(!uiRoot.StoryOverlay.activeSelf && !biographyOverlay.activeSelf, "读完返回应直接回详情");
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

                // 验收 7：先点切换打开扣卡确认 → 点击「先读人物志」→ 返回 → 再点切换 不会误扣卡
                transport.Snapshot = BuildFixtureInfo(ownedCards: 2, activeTrialId: "");
                client.RequestInfo(force: true);
                Click("AppearanceOption_00_reference_topright_boy");
                Click("AppearanceConfirmSwitch");
                AssertTrue(uiRoot.ConfirmOverlay.activeSelf, "首次点切换应弹出二次确认弹窗");
                Shoot("05-switch-confirm-dialog");
                uiRoot.PanelState.ArmLegacySwitchConfirmation("00_reference_topright_boy", clock);
                Click("ReadBeforeSwitch");
                AssertTrue(biographyOverlay.activeSelf && !uiRoot.ConfirmOverlay.activeSelf, "点击先读人物志应进入小传并关闭确认窗");
                AssertTrue(!uiRoot.PanelState.HasPendingSwitchConfirmation, "打开阅读层必须清除旧扣卡确认");
                Click("BiographyBack");
                Click("AppearanceConfirmSwitch");
                AssertTrue(uiRoot.ConfirmOverlay.activeSelf, "返回后首次点切换必须重进确认弹窗");
                AssertTrue(transport.SwitchCalls == 0, "不得直接扣卡发包");
                Click("AppearanceConfirmCancel");
                summary.Add($"[{width}x{height}] Check 7 PASS: 扣卡确认→先读人物志→返回→再点切换仅重进确认，SwitchCalls=0");

                // 额外视觉核验：背包 0 张卡弹出官方商城引导窗（04）
                transport.Snapshot = BuildFixtureInfo(ownedCards: 0, activeTrialId: "");
                client.RequestInfo(force: true);
                Click("AppearanceOption_00_reference_topright_boy");
                Click("AppearanceConfirmSwitch");
                AssertTrue(uiRoot.NoCardOverlay.activeSelf, "背包 0 张卡应弹缺卡商城引导");
                Shoot("06-no-card-mall-guide");
                Click("AppearanceNoCardCancel");

                // 验收 5：缺立绘仍可读人物小传与故事，显示缺图占位不借用他人立绘
                transport.Snapshot = BuildFixtureInfo(ownedCards: 2, activeTrialId: "");
                uiRoot.AttachVerificationClient(client, rawId => rawId == "09_bamboo_archer_girl" ? null : QdaoCharacterCatalog.LoadPortrait(rawId));
                client.RequestInfo(force: true);
                uiRoot.ShowPanel();
                Click("AppearanceOption_09_bamboo_archer_girl");
                var missingBox = FindObject("AppearancePortraitMissing");
                AssertTrue(missingBox.activeSelf, "缺立绘时应显示缺图占位");
                AssertTrue(uiRoot.BiographySection.activeSelf, "缺立绘时仍应显示人物背景");
                Shoot("07-missing-portrait-readable");
                Click("AppearanceReadStory");
                Click("BiographyReadStory");
                AssertTrue(uiRoot.StoryOverlay.activeSelf && FindText("StoryTitle").text == "没有画的那一页", "缺立绘时仍可打开阅读故事");
                Click("AppearanceStorySkip");
                summary.Add($"[{width}x{height}] Check 5 PASS: 缺立绘显示占位且小传/故事正常可读");

                // 验收 6：快速切人、阅读中断线、关闭重开不串内容
                Click("AppearanceOption_00_reference_topright_boy");
                Click("AppearanceOption_10_crimson_spear_girl");
                Click("AppearanceOption_09_bamboo_archer_girl");
                Click("AppearanceReadStory");
                Click("BiographyReadStory");
                AssertTrue(uiRoot.StoryOverlay.activeSelf, "快速切人后应打开桑听雨故事");
                transport.EmitDisconnect();
                AssertTrue(!uiRoot.StoryOverlay.activeSelf && !biographyOverlay.activeSelf, "断线时应立即收起故事与小传层");
                transport.IsReady = true;
                conn = new object();
                client.RequestInfo(force: true);
                uiRoot.ShowPanel();
                Click("AppearanceOption_10_crimson_spear_girl");
                AssertTrue(FindText("BiographyIdentity").text.Contains("祝停云"), "重开切回祝停云不得串成桑听雨");
                summary.Add($"[{width}x{height}] Check 6 PASS: 快速切人、断线与重开零串内容");

                // 验收 8：全程阅读无资产/试用/换形象请求
                AssertTrue(transport.SwitchCalls == 0 && transport.StartTrialCalls == 0 && transport.CancelTrialCalls == 0,
                    $"阅读过程不得产生写请求：switch={transport.SwitchCalls}, trial={transport.StartTrialCalls}, cancel={transport.CancelTrialCalls}");
                summary.Add($"[{width}x{height}] Check 8 PASS: 全程阅读写请求计数=0 (Switch=0, Trial=0, Cancel=0)");

                // 验收 9：人物收藏系列筛选、未集齐禁读合篇、集齐点亮印章与打开合篇阅读（零写请求）
                AssertTrue(uiRoot.SeriesTabBarRoot.activeSelf, "存在系列进度时应显示系列筛选栏");
                Click("AppearanceSeriesTab_1");
                AssertTrue(uiRoot.SeriesBannerRoot.activeSelf, "选中留灯院旧友后应显示系列信息条");
                AssertTrue(!FindButton("AppearanceReadSeriesEpilogue").interactable, "未集齐系列时读合篇按钮必须不可点");
                Shoot("08-series-incomplete-lantern");

                Click("AppearanceSeriesTab_3");
                AssertTrue(FindButton("AppearanceReadSeriesEpilogue").interactable, "已集齐清音济世时应允许读合篇");
                uiRoot.PanelState.ArmLegacySwitchConfirmation("00_reference_topright_boy", clock);
                Click("AppearanceReadSeriesEpilogue");
                AssertTrue(uiRoot.SeriesEpilogueOverlay.activeSelf, "点击读合篇应打开系列合篇阅读层");
                AssertTrue(!uiRoot.PanelState.HasPendingSwitchConfirmation, "打开系列合篇必须清除旧扣卡确认");
                AssertTrue(FindText("SeriesEpilogueTitle").text.Contains("青莲闻琴"), "系列合篇标题应匹配权威配表");
                AssertTrue(uiRoot.SeriesEpilogueBodyText.text.Contains("莲心"), "系列合篇正文应来自权威配表");
                Shoot("09-series-completed-epilogue");
                Click("AppearanceSeriesEpilogueBack");
                AssertTrue(!uiRoot.SeriesEpilogueOverlay.activeSelf, "返回应关闭系列合篇阅读层");
                Click("AppearanceSeriesTab_All");
                AssertTrue(!uiRoot.SeriesBannerRoot.activeSelf, "切回全部时应隐藏单系列信息条");
                AssertTrue(transport.SwitchCalls == 0 && transport.StartTrialCalls == 0 && transport.CancelTrialCalls == 0,
                    "系列筛选与合篇阅读全程不得产生写请求");
                summary.Add($"[{width}x{height}] Check 9 PASS: 人物收藏系列筛选、未集齐灰态、集齐读合篇与零写请求验收通过");
            }
            else if (width < 1200 || height < 600)
            {
                // 窄横屏 896×414 + 模拟安全区：核查列表→详情分步布局、小传与放大字号故事滚动
                Shoot("10-compact-list");
                Click("AppearanceOption_10_crimson_spear_girl");
                AssertTrue(FindObject("AppearanceBackToList").activeSelf, "窄横屏选中条目后应切换至详情并显示返回列表按钮");
                Shoot("11-compact-detail-tingyun");
                Click("AppearanceReadStory");
                AssertTrue(biographyOverlay.activeSelf, "窄横屏应能打开人物小传");
                Shoot("12-compact-biography-tingyun");
                Click("BiographyReadStory");
                AssertTrue(uiRoot.StoryOverlay.activeSelf, "窄横屏应能从小传进入往事");
                uiRoot.StoryBodyText.fontSize = 44f;
                uiRoot.RefreshStoryLayoutForVerification();
                Canvas.ForceUpdateCanvases();
                AssertTrue(uiRoot.StoryScroll.content.rect.height > uiRoot.StoryScroll.viewport.rect.height,
                    "窄横屏放大字号后故事正文必须提供真实纵向滚动");
                uiRoot.StoryScroll.verticalNormalizedPosition = 0f;
                Canvas.ForceUpdateCanvases();
                Shoot("13-compact-story-scroll-end");
                Click("AppearanceStorySkip");
                Click("AppearanceBackToList");
                AssertTrue(!FindObject("AppearanceBackToList").activeSelf, "返回列表后应隐藏返回列表按钮");
                summary.Add($"[{width}x{height}] Compact+SafeArea+EnlargedFontScroll PASS: contentH={uiRoot.StoryScroll.content.rect.height:F0} > viewportH={uiRoot.StoryScroll.viewport.rect.height:F0}");
            }
            else
            {
                // 1920×1080 常规横屏复核
                Click("AppearanceOption_10_crimson_spear_girl");
                Shoot("14-standard-1920-detail-tingyun");
                summary.Add($"[{width}x{height}] Standard 1920x1080 layout capture PASS");
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
        string seriesPbPath = Path.Combine(Application.streamingAssetsPath, "Tables", AppearanceSeriesTableManager.BinaryFileName);
        if (File.Exists(seriesPbPath))
            AppearanceSeriesTableManager.Instance.LoadFromBytes(File.ReadAllBytes(seriesPbPath));
    }

    private static PlayerAppearanceInfo BuildFixtureInfo(uint ownedCards, string activeTrialId)
    {
        var info = new PlayerAppearanceInfo
        {
            PermanentAppearanceId = "05_celestial_musician_girl",
            ActiveAppearanceId = string.IsNullOrEmpty(activeTrialId) ? "05_celestial_musician_girl" : activeTrialId,
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
        (uint id, string rawId, string name, uint sortOrder, bool trialUsed, bool owned, uint seriesId)[] roster =
        {
            (2, "00_reference_topright_boy", "金发带Q道童", 1, false, false, 1u),
            (3, "01_ice_sword_girl", "冰剑少女", 2, !string.IsNullOrEmpty(activeTrialId), false, 2u),
            (4, "02_fire_talisman_boy", "火符少年", 3, true, false, 2u),
            (5, "03_lotus_healer_girl", "莲花医者", 4, false, true, 3u),
            (7, "05_celestial_musician_girl", "天音少女", 5, false, true, 3u),
            (8, "09_bamboo_archer_girl", "竹弓少女", 6, false, false, 1u),
            (9, "10_crimson_spear_girl", "赤枪少女", 7, false, false, 1u),
            (10, "15_water_dragon_scholar_boy", "水龙书生", 8, false, false, 2u),
        };
        foreach (var (id, rawId, name, sortOrder, trialUsed, owned, seriesId) in roster)
        {
            info.Appearances.Add(new CharacterAppearanceEntry
            {
                ConfigId = id,
                AppearanceId = rawId,
                Name = name,
                SortOrder = sortOrder,
                TrialUsed = trialUsed,
                Owned = owned,
                CardUnlockable = true,
                SeriesId = seriesId,
            });
        }
        var s1 = new CharacterAppearanceSeriesProgress
        {
            SeriesId = 1u,
            SeriesKey = "lantern_courtyard",
            Name = "留灯院旧友",
            SortOrder = 1u,
            OwnedCount = 0u,
            TotalCount = 3u,
            Completed = false,
            CompletionBadgeName = "留灯同袍",
            EpilogueTitle = "灯下三碗面",
        };
        s1.MemberAppearanceIds.Add("00_reference_topright_boy");
        s1.MemberAppearanceIds.Add("09_bamboo_archer_girl");
        s1.MemberAppearanceIds.Add("10_crimson_spear_girl");
        info.SeriesProgress.Add(s1);

        var s2 = new CharacterAppearanceSeriesProgress
        {
            SeriesId = 2u,
            SeriesKey = "elemental_blades",
            Name = "水火冰锋",
            SortOrder = 2u,
            OwnedCount = 0u,
            TotalCount = 3u,
            Completed = false,
            CompletionBadgeName = "三灵同契",
            EpilogueTitle = "霜火沧澜试",
        };
        s2.MemberAppearanceIds.Add("01_ice_sword_girl");
        s2.MemberAppearanceIds.Add("02_fire_talisman_boy");
        s2.MemberAppearanceIds.Add("15_water_dragon_scholar_boy");
        info.SeriesProgress.Add(s2);

        var s3 = new CharacterAppearanceSeriesProgress
        {
            SeriesId = 3u,
            SeriesKey = "lotus_melody",
            Name = "清音济世",
            SortOrder = 3u,
            OwnedCount = 2u,
            TotalCount = 2u,
            Completed = true,
            CompletionBadgeName = "清音杏林",
            EpilogueTitle = "青莲闻琴",
        };
        s3.MemberAppearanceIds.Add("03_lotus_healer_girl");
        s3.MemberAppearanceIds.Add("05_celestial_musician_girl");
        info.SeriesProgress.Add(s3);
        return info;
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[AppearanceUiVerification FAIL] " + message);
    }
}
#endif
