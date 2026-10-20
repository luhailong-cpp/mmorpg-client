#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Tweening;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>真实强化界面的离线编辑器核验：只用内存假传输，不创建游戏连接或登录会话。</summary>
public static class EquipmentForgeUiVerification
{
    public static string OutputDirectory = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,
        "../Docs/Verification/forge-ui-qa-20261016/screenshots"));
    private const ulong SwordId = 5001;
    private const uint StoneId = 2001;
    private const uint AttackGemId = 2101;

    [MenuItem("MMORPG/UI/Capture equipment forge screens")]
    public static void CaptureAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("强化界面截图必须在编辑模式执行。");
        Directory.CreateDirectory(OutputDirectory);
        string evidencePath = Path.Combine(Directory.GetParent(OutputDirectory).FullName, "verification.txt");
        var evidence = new List<string>
        {
            "RUNNING",
            "原生 Unity uGUI / Camera.Render / RenderTexture，未连接服务器。",
            "使用正式 GameplayWindow、EquipForgeUiState、PlayerFeaturesClient；回包经 protobuf 序列化再解析。",
        };
        File.WriteAllLines(evidencePath, evidence);
        try
        {
            Capture(2560, 1080, evidence);
            Capture(1920, 1080, evidence);
            Capture(1280, 720, evidence);
            evidence[0] = "PASS：18 张原生 Unity 截图，三种分辨率各六个状态。";
            evidence.Add("已核验：背包更多入口、可强化、选宝石、镶嵌在途、降级确认、材料不足置灰、缺快照同步中。");
            evidence.Add("已核验：选择宝石只发一次且 item_id/socket/config_id 正确；确认前不发强化；确认后发一次；两份回包快照更新结果。");
            evidence.Add("未核验：真实账户资产、服务端规则裁决、联机延迟、鼠标命中与实机运行。");
            File.WriteAllLines(evidencePath, evidence);
            Debug.Log("EQUIPMENT_FORGE_UI_VERIFIED|18 screenshots|" + OutputDirectory);
        }
        catch (Exception error)
        {
            evidence[0] = "FAIL：截图或行为断言未完成。";
            evidence.Add(error.ToString());
            File.WriteAllLines(evidencePath, evidence);
            throw;
        }
    }

    private static void Capture(int width, int height, List<string> evidence)
    {
        var previousScene = SceneManager.GetActiveScene();
        // Batch mode starts with an untitled scene; Unity cannot open another scene additively beside it.
        // Only the isolated batch run replaces that scene. The menu command preserves the user's scene.
        bool batchUntitledScene = Application.isBatchMode && string.IsNullOrEmpty(previousScene.path);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
            batchUntitledScene ? NewSceneMode.Single : NewSceneMode.Additive);
        var previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D pixels = null;
        PlayerFeaturesClient client = null;
        EquipForgeUiState forge = null;
        GameplayWindow window = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("EquipmentForgeVerificationCamera").AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = QdaoUguiTheme.Letterbox;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create();
            camera.targetTexture = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            RealtimeTween.BeginEditorCapture();

            var canvasObject = new GameObject("EquipmentForgeVerificationCanvas", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 2;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var root = QdaoUguiFactory.CreateCenteredRect("EquipmentForgeVerificationRoot", canvasObject.transform, 2560, 1080);
            QdaoUguiFactory.CreateImage("Backdrop", root, 0, 0, 2560, 1080,
                QdaoRefreshArt.Load("sanctuary_background"));
            var scope = GameplayUiArt.Text(root, "离线界面核验 · 假传输快照 · 未连接服务器", 44, 1030, 2200, 34, 24,
                QdaoUguiTheme.Cream);
            scope.raycastTarget = false;

            var transport = new FixtureTransport();
            client = new PlayerFeaturesClient(transport);
            long now = 100000;
            client.NowMs = () => now;
            forge = new EquipForgeUiState();
            forge.Bind(client);
            window = new GameplayWindow(root, forge);
            window.SetCharacterLevel(80);

            void SyncBagPage() => window.SetBagPage(client.Bag, client.BagLoading, client.BagError, client.BusySort,
                client.Equipment, client.EquipmentLoading, client.EquipmentError, client.BusyEquip);
            client.OnChanged += SyncBagPage;

            void Load(BagInfo bag, BagInfo equipment)
            {
                client.RequestBag();
                transport.RespondBag(0, bag);
                client.RequestEquipment();
                transport.RespondBag(2, equipment);
            }

            void OpenFromBag()
            {
                window.ShowEquipment();
                Click(root, "BagSlot_0");
                Click(root, "EquipMore");
                Click(root, "EquipOpenForge");
                Require(forge.IsOpen && forge.TargetItemId == SwordId,
                    "背包更多入口必须为当前装备实例打开正式强化状态。");
            }

            void Shoot(string name)
            {
                Require(Find<RectTransform>(root, "EquipForgeDialog").gameObject.activeInHierarchy,
                    "截图时正式强化面板必须处于可见状态。");
                scope.transform.SetAsLastSibling();
                RealtimeTween.AdvanceEditorCapture(1f);
                foreach (var go in scene.GetRootGameObjects())
                {
                    foreach (var child in go.GetComponentsInChildren<UnityEngine.Transform>(true))
                        child.gameObject.layer = 31;
                    foreach (var childCanvas in go.GetComponentsInChildren<Canvas>(true))
                    {
                        childCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                        childCanvas.worldCamera = camera;
                        childCanvas.planeDistance = 2;
                    }
                }
                Canvas.ForceUpdateCanvases();
                foreach (var label in canvasObject.GetComponentsInChildren<TMP_Text>(true))
                    label.ForceMeshUpdate(true, true);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                pixels.Apply(false, false);
                string filename = name + "_" + width + "x" + height + ".png";
                File.WriteAllBytes(Path.Combine(OutputDirectory, filename), pixels.EncodeToPNG());
                evidence.Add(filename + " | " + forge.View.Stage + " | " + forge.View.StatusText);
            }

            Load(BagFixture(Weapon()), EquipmentFixture());
            OpenFromBag();
            Require(forge.View.Stage == EquipForgeStage.Ready && Find<Button>(root, "EquipForgeEnhance").interactable,
                "材料与银两足够时，强化按钮必须可点。");
            Shoot("01-ready");

            Click(root, "EquipForgeSocket_0");
            Require(forge.View.Picker != null && forge.View.Picker.Options.Any(option => option.ConfigId == AttackGemId),
                "攻击孔必须打开包含对应类型宝石的选择列表。");
            Shoot("02-gem-picker");
            int inlayCalls = transport.Count(MessageIds.InlayGem);
            Click(root, "EquipForgeGem_2101");
            Require(forge.View.Busy && forge.View.Picker == null, "选定宝石后必须进入在途状态并收起选择层。");
            Require(transport.Count(MessageIds.InlayGem) == inlayCalls + 1, "选宝石只能发出一次镶嵌请求。");
            var inlay = transport.Last<InlayGemRequest>();
            var inlayRequest = (InlayGemRequest)inlay.Request;
            Require(inlayRequest.ItemId == SwordId && inlayRequest.Socket == 0 && inlayRequest.GemConfigId == AttackGemId,
                "镶嵌必须使用当前物品实例、孔位与选中的宝石 config。");
            Require(!Find<Button>(root, "EquipForgeEnhance").interactable, "镶嵌在途时必须禁用强化。");
            Shoot("03-inlay-busy");
            inlay.Respond(new InlayGemResponse
            {
                Bag = BagFixture(WithAttackGem(Weapon()), attackGemCount: 2), Equipment = EquipmentFixture(),
            });
            Require(!forge.View.Busy && forge.View.Sockets.Single(socket => socket.Socket == 0).IsRemove,
                "镶嵌回包的两份快照必须更新孔位，并将操作切换为取下。");
            now += 1000;
            forge.Tick();

            window.Hide();
            Load(BagFixture(Weapon(level: 6, failLoss: 1)), EquipmentFixture());
            OpenFromBag();
            int enhanceCalls = transport.Count(MessageIds.EnhanceEquip);
            Click(root, "EquipForgeEnhance");
            Require(forge.View.Confirm != null && forge.View.Confirm.CanConfirm,
                "会降级的强化必须先显示可操作的确认层。");
            Require(transport.Count(MessageIds.EnhanceEquip) == enhanceCalls,
                "显示降级确认层时不得提前发出强化请求。");
            Shoot("04-enhance-confirm");
            Click(root, "EquipForgeConfirmEnhance");
            Require(transport.Count(MessageIds.EnhanceEquip) == enhanceCalls + 1 && forge.View.Busy,
                "确认降级风险后才发一次强化请求并进入在途。");
            var enhance = transport.Last<EnhanceEquipRequest>();
            Require(((EnhanceEquipRequest)enhance.Request).ItemId == SwordId, "强化请求必须保留目标实例。");
            enhance.Respond(new EnhanceEquipResponse
            {
                Bag = BagFixture(Weapon(level: 7, failLoss: 1), gold: 164000, stoneCount: 14),
                Equipment = EquipmentFixture(), Success = true, EnhanceLevel = 7,
            });
            Require(forge.View.EnhanceLevel == 7 && forge.View.StatusTone == EquipForgeTone.Success,
                "强化成功后必须采用回包等级和结果语气。");
            now += 1000;
            forge.Tick();

            window.Hide();
            Load(BagFixture(Weapon(), stoneCount: 0), EquipmentFixture());
            OpenFromBag();
            Require(!forge.View.EnhanceButton.Enabled && !Find<Button>(root, "EquipForgeEnhance").interactable,
                "强化石不足必须同步置灰界面按钮。");
            Require(forge.View.EnhanceButton.Hint.Contains("材料不足"), "置灰原因必须来自正式状态快照。");
            enhanceCalls = transport.Count(MessageIds.EnhanceEquip);
            forge.Enhance();
            Require(transport.Count(MessageIds.EnhanceEquip) == enhanceCalls, "材料不足不得发强化请求。");
            Shoot("05-materials-insufficient");

            window.Hide();
            client.OnChanged -= SyncBagPage;
            forge.Bind(null);
            client.Dispose();
            transport = new FixtureTransport();
            client = new PlayerFeaturesClient(transport);
            client.NowMs = () => now;
            forge.Bind(client);
            client.OnChanged += SyncBagPage;
            // 身上装备卡片可先到；人物背包尚未到时，仍能从正式入口打开同步中面板。
            client.RequestEquipment();
            transport.RespondBag(2, EquipmentFixture());
            window.ShowEquipment();
            Click(root, "BagCategory_3"); // Native worn-equipment view.
            Click(root, "EquipSlot_3");
            Click(root, "EquipMore");
            Click(root, "EquipOpenForge");
            Require(forge.View.Stage == EquipForgeStage.Loading && forge.TargetItemId == 7001,
                "人物背包快照缺失时，从身上装备入口打开必须呈现同步中状态。");
            var hiddenEnhance = Find<RectTransform>(root, "EquipForgeDialog")
                .GetComponentsInChildren<Button>(true).Single(button => button.name == "EquipForgeEnhance");
            Require(!hiddenEnhance.gameObject.activeInHierarchy && !hiddenEnhance.interactable,
                "同步中必须隐藏强化区且保持强化不可用。");
            Require(transport.Calls.Count(call => call.Request is GetBagRequest) == 2,
                "已读取装备栏后，打开面板必须只补拉缺少的人物背包快照。");
            Shoot("06-loading");
            window.Hide();
            Require(!forge.IsOpen, "关闭背包窗口必须关闭强化状态。");
            client.OnChanged -= SyncBagPage;
            Debug.Log("EQUIPMENT_FORGE_UI_RESOLUTION_VERIFIED|" + width + "x" + height + "|6 screenshots");
        }
        finally
        {
            window?.Dispose();
            forge?.Dispose();
            client?.Dispose();
            RealtimeTween.EndEditorCapture();
            RenderTexture.active = previousTarget;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (!batchUntitledScene && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static T Find<T>(Component root, string name) where T : Component
        => root.GetComponentsInChildren<T>(true).Where(item => item.gameObject.activeInHierarchy).Single(item => item.name == name);

    private static void Click(Component root, string name)
    {
        var button = Find<Button>(root, name);
        Require(button.gameObject.activeInHierarchy && button.interactable, name + " 必须可见且可点。");
        button.onClick.Invoke();
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static BagItemInfo Weapon(uint level = 4, uint failLoss = 0)
    {
        var weapon = new BagItemInfo
        {
            ItemId = SwordId, ConfigId = 1105, Count = 1, MaxStack = 1, EquipKind = 11, EquipLevel = 80,
            Name = "惊雷剑", IconKey = "UI/Ugui/EquipmentV1/icon_sword", Description = "离线界面核验样例",
            EnhanceLevel = level, EnhanceMaxLevel = 12,
            EnhanceNext = new EquipEnhancePreview
            {
                NextLevel = level + 1, SuccessRate = failLoss == 0 ? 9000u : 5500u,
                CostGold = 16000, FailLevelLoss = failLoss,
                MaterialConfigId = StoneId, MaterialName = "强化石", MaterialCount = 3,
            },
        };
        weapon.BaseAttrs.Add(new EquipAttrLineInfo
        { AttrId = 1, Name = "伤害", Value = 1620 + level * 20, EnhanceNextValue = 1640 + level * 20 });
        weapon.BaseAttrs.Add(new EquipAttrLineInfo
        { AttrId = 2, Name = "准确", Seq = 1, Value = 880 + level * 10, EnhanceNextValue = 890 + level * 10 });
        weapon.GemSockets.Add(new EquipGemSocketInfo { Socket = 0, GemType = 1, Name = "攻击孔" });
        weapon.GemSockets.Add(new EquipGemSocketInfo { Socket = 1, GemType = 0, Name = "通用孔" });
        return weapon;
    }

    private static BagItemInfo WithAttackGem(BagItemInfo weapon)
    {
        var socket = weapon.GemSockets.Single(item => item.Socket == 0);
        socket.GemConfigId = AttackGemId;
        socket.GemName = "攻击宝石·一级";
        socket.GemIconKey = "gem_2101";
        socket.Attr = new EquipAttrLineInfo { AttrId = 1, Name = "伤害", Value = 30 };
        return weapon;
    }

    private static BagInfo BagFixture(BagItemInfo weapon, ulong gold = 180000, uint stoneCount = 17, uint attackGemCount = 3)
    {
        var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = 0, Capacity = 56, CanSort = true }, Currency = new CurrencyComp() };
        bag.Currency.Values.Add(new ulong[] { gold, 280, 1680 });
        bag.Items.Add(weapon);
        if (stoneCount > 0) bag.Items.Add(new BagItemInfo
        { ItemId = 6001, ConfigId = StoneId, Count = stoneCount, MaxStack = 999, Name = "强化石" });
        if (attackGemCount > 0) bag.Items.Add(Gem(6002, AttackGemId, attackGemCount, 1, "攻击宝石·一级", 30));
        bag.Items.Add(Gem(6003, 2102, 2, 1, "攻击宝石·二级", 48));
        bag.Items.Add(Gem(6004, 2201, 2, 2, "防御宝石·一级", 25));
        for (int index = 0; index < bag.Items.Count; ++index)
            bag.Layout.Slots.Add(new BagSlotInfo { Slot = (uint)index, ItemId = bag.Items[index].ItemId, Width = 1, Height = 1 });
        return bag;
    }

    private static BagItemInfo Gem(ulong id, uint config, uint count, uint type, string name, ulong value)
        => new BagItemInfo
        {
            ItemId = id, ConfigId = config, Count = count, MaxStack = 999, GemType = type, Name = name,
            IconKey = "gem_" + config,
            GemAttr = new EquipAttrLineInfo { AttrId = type, Name = type == 1 ? "伤害" : "防御", Value = value },
        };

    private static BagInfo EquipmentFixture()
    {
        var equipment = new BagInfo { Layout = new BagLayoutInfo { BagType = 2, Capacity = 7 } };
        equipment.Layout.EquipSlots.Add(new EquipSlotInfo { Slot = 3, EquipKind = 11, Name = "武器" });
        var worn = WithAttackGem(Weapon(level: 3));
        worn.ItemId = 7001;
        equipment.Items.Add(worn);
        equipment.Layout.Slots.Add(new BagSlotInfo { Slot = 3, ItemId = worn.ItemId, Width = 1, Height = 1 });
        return equipment;
    }

    private sealed class FixtureTransport : IBattleTransport
    {
        public sealed class RecordedCall
        {
            public uint MessageId;
            public IMessage Request;
            public Action<IMessage> Respond;
        }

        public readonly List<RecordedCall> Calls = new List<RecordedCall>();
        public ulong PlayerId => 9101;
        public bool IsReady => true;
        public event Action Disconnected { add { } remove { } }
        public void RegisterNotify(uint messageId, Action<MessageContent> handler) { }
        public void SendOneWay(uint messageId, IMessage request)
            => throw new InvalidOperationException("离线界面核验不允许单向请求：" + messageId);

        public void Call<TResponse>(uint messageId, IMessage request, MessageParser<TResponse> parser,
            Action<TResponse> onResponse, Action<string> onError) where TResponse : IMessage<TResponse>
            => Calls.Add(new RecordedCall
            {
                MessageId = messageId, Request = request,
                Respond = response => onResponse(parser.ParseFrom(response.ToByteString())),
            });

        public void RespondBag(uint bagType, BagInfo snapshot)
        {
            var call = Calls.Last(entry => entry.Request is GetBagRequest request && request.BagType == bagType);
            Require(call.MessageId == SceneBagClientPlayerRpc.GetBagMessageId, "快照必须通过正式读背包消息响应。");
            call.Respond(new GetBagResponse { Bag = snapshot });
        }

        public RecordedCall Last<TRequest>() where TRequest : IMessage
            => Calls.Last(call => call.Request is TRequest);
        public int Count(uint messageId) => Calls.Count(call => call.MessageId == messageId);
    }
}
#endif
