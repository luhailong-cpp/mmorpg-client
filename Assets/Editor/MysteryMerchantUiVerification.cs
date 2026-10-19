#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MmorpgClient.Game.MysteryMerchant;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Gameplay;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 在临时编辑器场景里离线渲染正式的神秘商人商店与 HUD 角标。不创建账号、连接或购买请求;
/// 画面里的货架都是只用于截图的示例数据。结构与 SceneChannelUiVerification 相同。
/// </summary>
public static class MysteryMerchantUiVerification
{
    public static string OutputDirectory { get; set; } = Path.GetFullPath(Path.Combine(
        Application.dataPath, "../.codex-artifacts/mystery-merchant-ui"));

    [MenuItem("MMORPG/UI/Capture mystery merchant screens (offline)")]
    public static void CaptureAll()
    {
        Capture(2560, 1080);
        Capture(1920, 1080);
        File.WriteAllText(Path.Combine(OutputDirectory, "capture-status.txt"),
            "神秘商人离线截图完成：本线货架（六件，含 6 / 5 / 4 折、已购满、货币不足）、待确认、购买中、购买成功、" +
            "被拒提示、分页（八件，两页）、在别的线（前往 3线）、已离开（下次出现倒计时）、仅角标。\n" +
            "每张图都断言了所有可见文字至少有一个可见字形、没有被截断、没有字体里缺的字。\n" +
            "所有商品只用于 Editor 截图；这里没有创建网络连接，也没有发送查询或购买请求，联机购买另行验证。\n");
    }

    public static void Capture(int width, int height)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("神秘商人截图需要编辑模式。");
        Directory.CreateDirectory(OutputDirectory);
        var previousScene = SceneManager.GetActiveScene();
        // 批处理模式下新开的编辑器停在未保存的无标题场景上,不能再叠加新场景;只在批处理时切到启动场景。
        if (Application.isBatchMode && string.IsNullOrEmpty(previousScene.path))
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity", OpenSceneMode.Single);
            previousScene = SceneManager.GetActiveScene();
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D pixels = null;
        MysteryMerchantWindow window = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("MysteryMerchantCaptureCamera").AddComponent<Camera>();
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
            var canvasObject = new GameObject("MysteryMerchantCaptureCanvas", typeof(RectTransform), typeof(Canvas),
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
            // 角标先建、商店后建:与正式界面一样,商店的遮罩压在角标之上。
            var badge = MysteryMerchantUiRoot.CreateEntry(design, null);
            var badgeLabel = badge.GetComponentInChildren<TMP_Text>(true);
            // 两次点击的最小间隔用假时钟推进(编辑模式下游戏时钟不走)。
            float clock = 0f;
            window = new MysteryMerchantWindow(design, () => clock);
            uint bought = 0;
            int travels = 0;
            window.BuyRequested += quote => bought = quote.GoodsId;
            window.TravelRequested += () => travels++;

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

            // 角标与商店都按同一份视图画,口径与 MysteryMerchantUiRoot 一致;name 为 null 时只摆好不截图。
            void Display(MysteryMerchantShopView view, string name)
            {
                string badgeText = MysteryMerchantModels.EntryLabel(view.Presence, view.ChannelNo, view.RemainingMs, view.UntilNextMs);
                badge.gameObject.SetActive(badgeText != null);
                if (badgeText != null) badgeLabel.text = badgeText;
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

            // 本线货架:六件,三档折扣都有;第 2 件已购满(置灰);第 6 件比余额贵,但余额只用于显示,按钮照样可点。
            // 顺带核对两次点击才成交,而且挨得太近的第二下(双击)不算确认。
            Display(Here(Shelf(6)), "shelf");
            ExpectDisabled(MysteryMerchantWindow.BuyButtonPrefix + 2);
            if (!Find(MysteryMerchantWindow.BuyButtonPrefix + 6).interactable)
                throw new InvalidOperationException("比余额贵的商品不该置灰(买不买得起由服务端裁决)。");
            Click(MysteryMerchantWindow.BuyButtonPrefix + 1);
            if (bought != 0) throw new InvalidOperationException("第一次点击不该发出购买。");
            Shoot("shelf-armed");
            Click(MysteryMerchantWindow.BuyButtonPrefix + 1);
            if (bought != 0) throw new InvalidOperationException("紧挨着的第二次点击(双击)不该成交。");
            clock += MysteryMerchantWindow.ConfirmMinIntervalSeconds;
            Click(MysteryMerchantWindow.BuyButtonPrefix + 1);
            if (bought != 1) throw new InvalidOperationException("隔够时间的第二次点击应当发出商品 1 的购买,实际：" + bought);

            // 购买中:所有购买按钮置灰。
            var buying = Here(Shelf(6));
            buying.BuyingGoodsId = 1;
            buying.Status = MysteryMerchantClient.BuyPendingMessage;
            Display(buying, "buying");
            ExpectDisabled(MysteryMerchantWindow.BuyButtonPrefix + 3);

            // 购买成功 / 被拒。
            var purchased = Here(Shelf(6));
            purchased.Status = MysteryMerchantModels.PurchasedText(purchased.Goods[0], 1);
            Display(purchased, "purchased");
            var rejected = Here(Shelf(6));
            rejected.Status = MysteryMerchantModels.DescribeTip((uint)mystery_merchant_error.KMysteryMerchantTooFar);
            rejected.StatusIsError = true;
            Display(rejected, "rejected-too-far");

            // 分页:八件,每页六件。
            Display(Here(Shelf(8)), "paging-page-1");
            Click(MysteryMerchantWindow.NextPageButtonName);
            Shoot("paging-page-2");
            Find(MysteryMerchantWindow.BuyButtonPrefix + 8);

            // 在别的线:只知道在几线,给「前往」。
            var elsewhere = new MysteryMerchantShopView
            {
                Presence = MysteryMerchantPresence.Elsewhere, ChannelNo = 3, Ready = true, RemainingMs = 754_000, CanTravel = true,
            };
            Display(elsewhere, "elsewhere");
            Click(MysteryMerchantWindow.TravelButtonName);
            if (travels != 1) throw new InvalidOperationException("点击「前往」应当发出一次意图。");

            // 在别的线但进不去:按钮置灰,状态行说明原因。
            var blocked = elsewhere;
            blocked.CanTravel = false;
            blocked.TravelBlockedText = "该线路已满，请选择其他线路。";
            Display(blocked, "elsewhere-blocked");
            ExpectDisabled(MysteryMerchantWindow.TravelButtonName);
            if (window.StatusText != blocked.TravelBlockedText)
                throw new InvalidOperationException("「前往」置灰时状态行应当说明原因,实际：" + window.StatusText);

            // 已离开:下一次出现的倒计时;到点了而新一轮还没排出来时显示「即将出现」。
            Display(new MysteryMerchantShopView { Presence = MysteryMerchantPresence.Away, UntilNextMs = 2_710_000 }, "away");
            Display(new MysteryMerchantShopView { Presence = MysteryMerchantPresence.Away, UntilNextMs = 0 }, "away-arriving");
            if (badgeLabel.text != "神秘商人 即将出现")
                throw new InvalidOperationException("角标文案不对：" + badgeLabel.text);

            // 仅角标:商店关着时主界面右上角的样子(背景是截图用的纯色,不是游戏画面)。
            Display(elsewhere, null);
            if (badgeLabel.text != "神秘商人 3线 12:34")
                throw new InvalidOperationException("角标文案不对：" + badgeLabel.text);
            window.Hide();
            Shoot("hud-badge");

            Debug.Log("MYSTERY_MERCHANT_UI_CAPTURE_OK|" + width + "x" + height + "|" + OutputDirectory);
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

    /// <summary>字形断言(调用前已 ForceMeshUpdate),三道检查同 SceneChannelUiVerification.AssertGlyphs。</summary>
    private static void AssertGlyphs(TMP_Text label)
    {
        if (!label.gameObject.activeInHierarchy || !label.enabled || string.IsNullOrWhiteSpace(label.text)) return;
        bool visibleGlyph = false;
        for (int character = 0; character < label.textInfo.characterCount; character++)
            visibleGlyph |= label.textInfo.characterInfo[character].isVisible;
        if (!visibleGlyph)
            throw new InvalidOperationException("神秘商人界面文字未渲染：" + label.name + " / " + label.text);
        if (label.isTextTruncated)
            throw new InvalidOperationException("神秘商人界面文字被截断（文本框太小）：" + label.name + " / " + label.text);
        if (label.font == null) return;
        var printable = new StringBuilder(label.text.Length);
        foreach (char character in label.text)
            if (!char.IsWhiteSpace(character) && !char.IsControl(character)) printable.Append(character);
        if (label.font.HasCharacters(printable.ToString(), out uint[] missing, true, false)) return;
        var codes = new List<string>();
        if (missing != null)
            foreach (uint code in missing) codes.Add("U+" + code.ToString("X4"));
        throw new InvalidOperationException("神秘商人界面文字缺字形：" + label.name + " / " + label.text +
                                            " / " + label.font.name + " 缺 " + string.Join(" ", codes));
    }

    // ── 示例数据 ────────────────────────────────────────────────────────────

    private static readonly (string Name, ulong Price, uint Percent, uint Limit, uint Bought)[] Samples =
    {
        ("回血丹", 400, 60, 5, 1),
        ("回蓝丹", 400, 50, 5, 5),
        ("精炼石", 800, 40, 3, 0),
        ("修行秘录残页", 1500, 60, 3, 2),
        ("灵兽口粮", 1000, 50, 3, 0),
        ("藏经阁手札", 12000, 40, 1, 0),
        ("玄铁护符", 6000, 60, 1, 0),
        ("培元丹", 300, 50, 5, 0),
    };

    private static List<global::MysteryMerchantGoodsInfo> Shelf(int count)
    {
        var goods = new List<global::MysteryMerchantGoodsInfo>();
        for (int i = 0; i < count && i < Samples.Length; i++)
        {
            var sample = Samples[i];
            goods.Add(new global::MysteryMerchantGoodsInfo
            {
                GoodsId = (uint)(i + 1), ItemConfigId = (uint)(10 + i), ItemCount = sample.Limit > 1 ? 10u : 1u,
                Name = sample.Name, CurrencyType = 0, OriginalPrice = sample.Price,
                Price = (sample.Price * sample.Percent + 99) / 100, DiscountPercent = sample.Percent,
                LimitCount = sample.Limit, BoughtCount = sample.Bought,
            });
        }
        return goods;
    }

    /// <summary>商人就在本线、余额 3,000 银两(比 4 折后 4,800 的藏经阁手札少;余额只用于显示,不影响按钮)。</summary>
    private static MysteryMerchantShopView Here(List<global::MysteryMerchantGoodsInfo> goods) => new MysteryMerchantShopView
    {
        Presence = MysteryMerchantPresence.Here, RoundId = 1_757_001_600, ChannelNo = 3, Goods = goods, Ready = true,
        RemainingMs = 754_000, UntilNextMs = 2_554_000, Balances = new List<ulong> { 3000 },
    };
}
#endif
