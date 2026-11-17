#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Gameplay;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>真实装备窗口的离线编辑器夹具。只在隔离预览场景工作，不启动 Play 或连接服务器。</summary>
public static class EquipmentWindowVerification
{
    private const ulong FixturePlayer = 2026111701;
    private const ulong WornSword = 910101;
    private const ulong FirstJewelry = 910105;
    private const ulong CandidateSword = 910201;
    private const ulong CandidateJewelry = 910202;
    private static bool _running;
    public static string OutputDirectory => Path.GetFullPath(Path.Combine(Application.dataPath,
        "../.codex-artifacts/equipment-ui-20261117/screenshots"));
    private static string WorkDirectory => Directory.GetParent(OutputDirectory).FullName;
    public static string TableDirectory = Path.GetFullPath(Path.Combine(Application.dataPath,
        "../../xuanming-server-mmo-cpp/generated/tables"));

    [MenuItem("MMORPG/UI/Capture equipment 20261117")]
    public static void CaptureAll()
    {
        if (_running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Equipment capture requires an idle editor outside Play Mode.");
        _running = true;
        var report = new CaptureReport { startedUtc = DateTime.UtcNow.ToString("O"), status = "RUNNING" };
        try
        {
            Directory.CreateDirectory(OutputDirectory);
            var tables = new FixtureTables(report);
            using (var capture = new CaptureContext(1983, 793, tables, report)) CaptureStates(capture, report);
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) })
            {
                using var capture = new CaptureContext(size.x, size.y, tables, report);
                capture.Window.Show();
                CheckOverview(capture, report);
                capture.Shoot("01-overview");
            }
            Check(report.screenshots.Count == 12, "Exactly twelve native screenshots must be recorded.");
            report.status = "PASS";
            Debug.Log("EQUIPMENT_WINDOW_CAPTURE_PASSED|12 screenshots|" + OutputDirectory);
        }
        catch (Exception error)
        {
            report.status = "FAIL";
            report.failure = error.ToString();
            throw;
        }
        finally
        {
            report.finishedUtc = DateTime.UtcNow.ToString("O");
            _running = false;
            File.WriteAllText(Path.Combine(WorkDirectory, "capture.result.json"), JsonUtility.ToJson(report, true));
        }
    }

    private static void CaptureStates(CaptureContext capture, CaptureReport report)
    {
        capture.Window.Show();
        CheckOverview(capture, report);
        capture.Shoot("01-overview");

        capture.Click("EquipmentSlot_8");
        capture.Click("EquipmentCandidate_" + CandidateJewelry);
        Check(capture.Find<TMP_Text>("EquipmentComparisonTitle").text.Contains("首饰二"), "Comparison must name the second jewelry position.");
        Check(capture.Transport.Count<EquipItemRequest>() == 0, "Comparison must not equip before confirmation.");
        capture.Shoot("02-compare-jewelry-two");
        capture.Click("EquipmentCompareCommit");
        var equip = capture.Transport.Last<EquipItemRequest>();
        Check(equip.ItemId == CandidateJewelry && equip.HasTargetSlot && equip.TargetSlot == 8, "Equip confirmation must preserve the candidate GUID and explicit second slot.");
        Check(capture.Transport.Equipment.Layout.Slots.Single(s => s.Slot == 7).ItemId == FirstJewelry,
            "Replacing jewelry two must preserve jewelry one.");
        report.assertions.Add("Comparison confirmation targets slot 8 and preserves slot 7.");

        capture.Click("EquipmentTab_1");
        capture.Transport.DeferNextPreview = true;
        capture.Click("CraftSelection_3181");
        Check(capture.Client.EquipmentCraftPreviewLoading && !capture.Find<Button>("CraftCommit").interactable,
            "Craft confirmation must remain disabled while the authoritative preview is loading.");
        capture.Transport.CompleteDeferredResponse();
        capture.Find<ScrollRect>("CraftRecipeList").verticalNormalizedPosition = 0;
        Check(capture.Client.CraftPreview?.Choice.ConfigId == 3181 && capture.Find<Button>("CraftCommit").interactable,
            "Selecting a recipe must request its authoritative preview and enable confirmation.");
        capture.Shoot("03-craft-body-preview");
        capture.Click("CraftCommit");
        Check(capture.Transport.Count<CommitEquipmentCraftRequest>() == 1 && !capture.Client.HasPendingEquipmentCraft,
            "Durable body creation must issue one request and clear only the fixture recovery record.");
        var creation = capture.Transport.Last<CommitEquipmentCraftRequest>();
        Check(creation.Choice.Operation == EquipmentCraftChoice.Types.Operation.CreateItem && creation.Choice.ConfigId == 3181
            && creation.ClientRequestId != 0, "Body confirmation must retain its preview identity and request key.");

        capture.Click("CraftSelection_" + CandidateSword);
        capture.Click("CraftOwnedAffix_1");
        capture.Click("AffixOption_101");
        CheckAffix(capture, CandidateSword, 1, 101, 1);
        capture.Shoot("04a-direction-affixes");
        capture.Clock += 2000;
        capture.Transport.DeferNextCommit = true;
        capture.Click("AffixCommit");
        Check(capture.Client.HasPendingEquipmentCraft && !capture.Find<Button>("AffixCommit").interactable,
            "A pending craft request must disable the live confirmation button.");
        capture.Transport.CompleteDeferredResponse();
        Check(!capture.Client.HasPendingEquipmentCraft, "Durable direction installation must complete locally.");
        capture.Click("CraftOwnedAffix_2");
        capture.Click("AffixOption_201");
        CheckAffix(capture, CandidateSword, 2, 201, 2);
        capture.Shoot("04b-efficiency-affixes");
        capture.Clock += 2000;
        capture.Click("AffixCommit");
        capture.Click("EquipmentRefresh");
        capture.Click("CraftKind_15");
        capture.Click("CraftSelection_" + CandidateJewelry);
        capture.Click("CraftOwnedAffix_1");
        capture.Click("AffixOption_301");
        CheckAffix(capture, CandidateJewelry, 1, 301, 3);
        capture.Shoot("04c-jewelry-affixes");
        capture.Click("AffixCancel");
        report.assertions.Add("All three affix pools open through live buttons and preview the exact GUID, slot and selection; two durable installs are recorded.");
        report.assertions.Add("Preview loading and pending craft responses keep their confirmation buttons disabled.");

        capture.Click("EquipmentTab_2");
        capture.Click("EnhancementChoose_" + CandidateSword);
        Check(capture.Find<Button>("EnhancementCommit").interactable, "A funded V1 item must have an enabled enhancement action.");
        Check(capture.Find<TMP_Text>("EnhancementSuccessRate").text.Contains("100"), "V1 enhancement must display the provided guaranteed success rate.");
        capture.Shoot("05-enhancement");
        capture.Clock += 2000;
        capture.Click("EnhancementCommit");
        var enhancement = capture.Transport.Last<EnhanceEquipRequest>();
        Check(enhancement.ItemId == CandidateSword && enhancement.ExpectedLevel == 0 && enhancement.ClientRequestId != 0,
            "Enhancement must send the selected instance, expected level and a nonzero key.");
        Check(capture.Client.CharacterBag.Items.Single(i => i.ItemId == CandidateSword).EnhanceLevel == 1,
            "Enhancement response must replace the item snapshot.");
        report.assertions.Add("Enhancement button sends the current GUID/level once and consumes only in-memory fixture materials.");

        capture.Click("EquipmentTab_3");
        capture.Click("ArtifactRecipe_3301");
        Check(capture.Find<Button>("ArtifactExchange").GetComponentInChildren<TMP_Text>().text.Contains("再次"),
            "Already-owned artifacts must remain exchangeable as independent instances.");
        capture.Shoot("06-artifact-catalog");
        capture.Click("ArtifactDetails_3306");
        Check(capture.Find<TMP_Text>("FullEquipmentTitle").text.Contains("护心"), "Artifact detail must correspond to the clicked product.");
        capture.Shoot("07-artifact-detail");
        capture.Click("ArtifactDetailExchange");
        Check(capture.Client.CraftPreview?.Result?.Artifact?.EffectId == 6, "Artifact detail exchange must select the same artifact recipe.");
        capture.Clock += 2000;
        capture.Click("ArtifactExchange");
        Check(capture.Transport.Last<CommitEquipmentCraftRequest>().Choice.ConfigId == 3306,
            "Artifact exchange must retain its selected template.");
        report.assertions.Add("Artifact catalog preserves duplicate ownership; detail exchange selects and commits artifact 6.");

        capture.Click("EquipmentTab_0");
        capture.Click("EquipmentMaterialEntry");
        var claim = capture.Find<Button>("MaterialChoiceClaim");
        Check(!claim.interactable, "Material-choice claim must be disabled without a server offer.");
        int beforeClaim = capture.Transport.Calls.Count;
        claim.onClick.Invoke();
        Check(capture.Transport.Calls.Count == beforeClaim, "Unavailable material choice must not dispatch a request.");
        capture.Shoot("08-material-choice-unavailable");
        capture.Click("MaterialChoiceBack");
        capture.Click("EquipmentClose");
        Check(!capture.Window.IsVisible, "The close button must hide the live equipment window.");
        Check(capture.Client.EquipmentCraftRecovery.Load(FixturePlayer).Length == 0, "No fixture craft intent may remain pending after durable replies.");
        report.assertions.Add("Material choice stays unclaimable and sends no request; close hides the window.");
    }

    private static void CheckOverview(CaptureContext capture, CaptureReport report)
    {
        var slots = capture.Window.Root.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("EquipmentSlot_", StringComparison.Ordinal)).ToList();
        Check(slots.Count == 8, "Overview must expose eight real equipment slot buttons.");
        var worn = capture.Transport.Equipment;
        foreach (uint kind in new uint[] { 15, 16 })
        {
            var same = worn.Items.Where(item => item.EquipKind == kind).ToList();
            Check(same.Count == 2 && same[0].ConfigId == same[1].ConfigId && same[0].ItemId != same[1].ItemId,
                "Two same-named items must remain separate equipment instances.");
        }
        report.assertions.Add($"Overview {capture.Width}x{capture.Height}: eight slots, two duplicate jewelry instances and two duplicate artifact instances.");
    }

    private static void CheckAffix(CaptureContext capture, ulong item, uint slot, uint id, uint pool)
    {
        var preview = capture.Client.CraftPreview;
        Check(preview != null && preview.Choice.ItemId == item && preview.Choice.AffixSlot == slot && preview.Choice.AffixId == id,
            "Affix preview must preserve the selected instance and explicit slot.");
        Check(preview.Result.FixedAffixSlots.Single(s => s.Slot == slot).Selected?.Pool == pool,
            "Preview result must show the selected pool.");
        Check(capture.Find<Button>("AffixCommit").interactable, "A valid affix preview must enable confirmation.");
    }

    private sealed class CaptureContext : IDisposable
    {
        public readonly int Width, Height;
        public readonly FixtureTransport Transport;
        public readonly PlayerFeaturesClient Client;
        public readonly EquipmentWindow Window;
        public long Clock = 100000;
        private readonly CaptureReport _report;
        private readonly Scene _scene;
        private readonly Scene _previousScene;
        private readonly SceneState[] _userScenes;
        private readonly EventSystem _previousEvents;
        private readonly GameObject _previousFocus;
        private readonly RenderTexture _previousTarget;
        private readonly Camera _camera;
        private readonly GameObject _canvasObject;
        private readonly RenderTexture _target;
        private readonly Texture2D _pixels;
        private bool _disposed;

        public CaptureContext(int width, int height, FixtureTables tables, CaptureReport report)
        {
            Width = width; Height = height; _report = report;
            _previousScene = SceneManager.GetActiveScene();
            _userScenes = Enumerable.Range(0, SceneManager.sceneCount).Select(i => new SceneState(SceneManager.GetSceneAt(i))).ToArray();
            _previousEvents = EventSystem.current;
            _previousFocus = _previousEvents?.currentSelectedGameObject;
            _previousTarget = RenderTexture.active;
            _scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var cameraObject = PreviewRoot("EquipmentCaptureCamera", typeof(Camera));
                _camera = cameraObject.GetComponent<Camera>();
                _camera.scene = _scene; _camera.enabled = false;
                _camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
                _camera.orthographic = true; _camera.nearClipPlane = .1f; _camera.farClipPlane = 100;
                _camera.cullingMask = 1 << 31; _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = QdaoUguiTheme.Letterbox;
                _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Check(_target.Create(), "RenderTexture allocation failed.");
                _camera.targetTexture = _target;
                _pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);
                _canvasObject = PreviewRoot("EquipmentCaptureCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                var canvas = _canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = _camera; canvas.planeDistance = 2;
                canvas.scaleFactor = 1; canvas.referencePixelsPerUnit = 100;
                var events = PreviewRoot("EquipmentCaptureEvents", typeof(EventSystem)).GetComponent<EventSystem>();
                EventSystem.current = events;
                var root = QdaoUguiFactory.CreateStretch("EquipmentCaptureRoot", _canvasObject.transform, Vector4.zero);
                Transport = new FixtureTransport(tables);
                Client = new PlayerFeaturesClient(Transport) { NowMs = () => Clock,
                    EquipmentCraftRecovery = new DurableRequestFileStore(Path.Combine(WorkDirectory, "fixture-intents", Guid.NewGuid().ToString("N"))) };
                Window = new EquipmentWindow(root);
                Window.Bind(Client);
            }
            catch { Dispose(); throw; }
        }

        private GameObject PreviewRoot(string name, params Type[] components)
        {
            var root = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave, components);
            SceneManager.MoveGameObjectToScene(root, _scene);
            return root;
        }

        public T Find<T>(string name) where T : Component => Window.Root.GetComponentsInChildren<T>(true)
            .Where(component => component.gameObject.activeInHierarchy).Single(component => component.name == name);

        public void Click(string name)
        {
            var button = Find<Button>(name);
            Check(button.IsActive() && button.IsInteractable(), "Button is unavailable: " + name);
            EventSystem.current.SetSelectedGameObject(button.gameObject);
            button.onClick.Invoke();
        }

        public void Shoot(string state)
        {
            Check(Window.IsVisible, "Equipment window must be visible for capture.");
            foreach (var root in _scene.GetRootGameObjects())
                foreach (var child in root.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
            Canvas.ForceUpdateCanvases();
            foreach (var fit in _canvasObject.GetComponentsInChildren<EquipmentUiFit>(true)) fit.Fit();
            foreach (var text in _canvasObject.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
            Canvas.ForceUpdateCanvases();
            var frame = Find<RectTransform>("EquipmentComposition");
            Check(Mathf.Abs(frame.localScale.x - frame.localScale.y) < .00001f, "Composition scale must be uniform.");
            var corners = new UnityEngine.Vector3[4]; frame.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                var screen = _camera.WorldToScreenPoint(corner);
                Check(screen.x >= -.5f && screen.y >= -.5f && screen.x <= Width + .5f && screen.y <= Height + .5f,
                    "The whole composition must fit inside the render target.");
            }
            _camera.Render(); RenderTexture.active = _target;
            _pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false); _pixels.Apply(false, false);
            byte[] png = _pixels.EncodeToPNG();
            string filename = state + "_" + Width + "x" + Height + ".png";
            File.WriteAllBytes(Path.Combine(OutputDirectory, filename), png);
            _report.screenshots.Add(new Screenshot { file = filename, width = Width, height = Height,
                sha256 = Digest(png), state = state, scale = frame.localScale.x });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                Window?.Dispose(); Client?.Dispose();
                if (Transport != null) _report.requests.AddRange(Transport.Calls.Select(call =>
                    new RequestEvidence { messageId = call.MessageId, requestType = call.Request.Descriptor.Name, requestJson = call.Request.ToString() }));
            }
            finally
            {
                RenderTexture.active = _previousTarget;
                if (_pixels != null) UnityEngine.Object.DestroyImmediate(_pixels);
                if (_target != null) { _target.Release(); UnityEngine.Object.DestroyImmediate(_target); }
                if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
                EventSystem.current = _previousEvents;
                if (_previousEvents != null && _previousFocus != null && _previousFocus.activeInHierarchy)
                    _previousEvents.SetSelectedGameObject(_previousFocus);
            }
            Check(SceneManager.GetActiveScene() == _previousScene, "Capture must not change the user's active scene.");
            foreach (var state in _userScenes) state.CheckUnchanged();
        }
    }

    private sealed class SceneState
    {
        private readonly Scene _scene;
        private readonly bool _dirty;
        private readonly HashSet<GameObject> _roots;
        public SceneState(Scene scene)
        {
            _scene = scene; _dirty = scene.isDirty;
            _roots = new HashSet<GameObject>(scene.isLoaded ? scene.GetRootGameObjects() : Array.Empty<GameObject>());
        }
        public void CheckUnchanged()
        {
            Check(_scene.IsValid() && _scene.isDirty == _dirty, "Capture must preserve user scene dirty flags.");
            if (_scene.isLoaded) Check(_roots.SetEquals(_scene.GetRootGameObjects()),
                "Capture must not add or remove roots in a user scene.");
        }
    }

    /// <summary>只读取正式导出二进制到私有 protobuf 对象，不加载或替换全局表管理器。</summary>
    private sealed class FixtureTables
    {
        public readonly Dictionary<uint, ItemTable> Items;
        public readonly Dictionary<uint, EquipArtifactTable> Artifacts;
        public readonly Dictionary<uint, EquipAttributeTable> Attributes;
        public readonly Dictionary<uint, EquipEnhanceTable> Enhancement;
        public readonly List<EquipSlotTable> Slots;
        public readonly GetEquipmentCraftRulesResponse Rules;
        public FixtureTables(CaptureReport report)
        {
            byte[] Read(string file)
            {
                string path = Path.Combine(TableDirectory, file);
                byte[] bytes = File.ReadAllBytes(path);
                report.tables.Add(new TableEvidence { source = path, sha256 = Digest(bytes) });
                return bytes;
            }
            Items = ItemTableData.Parser.ParseFrom(Read("item.pb")).Data.ToDictionary(row => row.Id);
            Artifacts = EquipArtifactTableData.Parser.ParseFrom(Read("equipartifact.pb")).Data.ToDictionary(row => row.Id);
            Attributes = EquipAttributeTableData.Parser.ParseFrom(Read("equipattribute.pb")).Data.ToDictionary(row => row.Id);
            Enhancement = EquipEnhanceTableData.Parser.ParseFrom(Read("equipenhance.pb")).Data.Where(row => row.ProfileId == 1).ToDictionary(row => row.Level);
            Slots = EquipSlotTableData.Parser.ParseFrom(Read("equipslot.pb")).Data.Where(row => row.EquipKind >= 11).OrderBy(row => row.Id).ToList();
            var affixes = EquipV1AffixTableData.Parser.ParseFrom(Read("equipv1affix.pb")).Data;
            Rules = new GetEquipmentCraftRulesResponse { RulesFingerprint = ByteString.CopyFromUtf8("editor-fixture-v1") };
            foreach (var row in Items.Values.Where(row => row.EquipProfile != 0).OrderBy(row => row.Id))
                Rules.Recipes.Add(new EquipmentCraftRecipe { Product = Product(row.Id), MaterialConfigId = row.ForgeMaterialId, MaterialCount = row.ForgeMaterialCount });
            foreach (var row in affixes.OrderBy(row => row.Id))
                Rules.Affixes.Add(new EquipFixedAffixInfo { AffixId = row.Id, Pool = row.Pool, Name = row.Name, Description = row.Description,
                    ValueBps = row.ValuePermyriad, SourceCapBps = row.SourceCap, Effect = row.Effect, EffectParam = row.EffectParam,
                    Unit = row.Effect >= 7 && row.Effect <= 12 ? 2u : 1u, MaterialConfigId = row.MaterialId, MaterialCount = row.MaterialCount });
            Check(Rules.Recipes.Count == 53 && Rules.Affixes.Count == 36 && Artifacts.Count == 8 && Slots.Count == 8,
                "The fixture requires the formal 53 recipes, 36 affixes, eight artifacts and eight slots.");
        }

        public BagItemInfo Product(uint config, ulong id = 0, uint enhancement = 0)
        {
            var row = Items[config];
            var product = new BagItemInfo { ItemId = id, ConfigId = config, Count = 1, MaxStack = row.MaxStackSize,
                EquipKind = row.EquipKind, EquipProfile = row.EquipProfile, EquipLevel = row.EquipLevel,
                EquipClass = row.EquipClass, Name = row.Name, Description = row.Description, IconKey = row.IconKey };
            if (row.EquipProfile == 1 || row.EquipProfile == 2)
                for (uint slot = 1; slot <= 2; slot++) product.FixedAffixSlots.Add(new EquipFixedAffixSlotInfo { Slot = slot, Pool = row.EquipProfile == 1 ? slot : 3 });
            if (row.EquipProfile == 3)
            {
                var artifact = Artifacts[row.ArtifactId];
                product.Artifact = new EquipArtifactInfo { EffectId = artifact.Id, Name = artifact.Name, Description = artifact.Description,
                    BonusBps = artifact.BonusBps, CapBps = artifact.CapBps, PerRoundLimit = artifact.PerRoundLimit,
                    BattleLimit = artifact.BattleLimit, ActiveRounds = artifact.ActiveRounds, HealthThresholdBps = artifact.HealthThresholdBps };
            }
            if (row.EquipProfile == 1)
            {
                product.EnhanceLevel = enhancement; product.EnhanceMaxLevel = Enhancement.Keys.Max();
                product.EnhanceBonusPermyriad = enhancement == 0 ? 0 : Enhancement[enhancement].BonusPermyriad;
                if (Enhancement.TryGetValue(enhancement + 1, out var next))
                    product.EnhanceNext = new EquipEnhancePreview { NextLevel = next.Level, SuccessRate = next.SuccessRate,
                        BonusPermyriad = next.BonusPermyriad, CostGold = next.CostGold, FailLevelLoss = next.FailLevelLoss,
                        MaterialConfigId = next.MaterialId, MaterialCount = next.MaterialCount,
                        MaterialName = Items[next.MaterialId].Name, MaterialIconKey = Items[next.MaterialId].IconKey };
            }
            foreach (var basic in row.BaseAttr)
            {
                var attribute = Attributes[basic.BaseAttrId];
                bool eligible = attribute.Effect == 4 || (attribute.Effect == 3 &&
                    (attribute.EffectParam == 3 || attribute.EffectParam == 4 || attribute.EffectParam == 6));
                ulong value = basic.BaseAttrValue;
                ulong bonus = eligible ? value * product.EnhanceBonusPermyriad / 10000 : 0;
                product.BaseAttrs.Add(new EquipAttrLineInfo { AttrId = attribute.Id, Name = attribute.Name, Percent = attribute.Percent != 0,
                    Seq = (uint)product.BaseAttrs.Count, Value = value + bonus, EnhanceBonus = bonus,
                    EnhanceNextValue = product.EnhanceNext == null ? 0 : value + (eligible ? value * product.EnhanceNext.BonusPermyriad / 10000 : 0) });
            }
            return product;
        }
    }

    /// <summary>传输只在内存中记录并响应；不是服务端业务实现或规则验收替代品。</summary>
    private sealed class FixtureTransport : IBattleTransport
    {
        public sealed class CallRecord { public uint MessageId; public IMessage Request; }
        public readonly List<CallRecord> Calls = new();
        public readonly BagInfo Bag = new() { Layout = new BagLayoutInfo { BagType = 0, Capacity = 100, CanSort = true }, Currency = new CurrencyComp() };
        public readonly BagInfo Equipment = new() { Layout = new BagLayoutInfo { BagType = 2, Capacity = 11 } };
        private readonly FixtureTables _tables;
        private ulong _revision;
        private ulong _nextItem = 910300;
        private Action _deferredResponse;
        public bool DeferNextPreview;
        public bool DeferNextCommit;
        public ulong PlayerId => FixturePlayer;
        public bool IsReady => true;
        public event Action Disconnected { add { } remove { } }
        public FixtureTransport(FixtureTables tables)
        {
            _tables = tables;
            Bag.Currency.Values.Add(new ulong[] { 500000, 0, 0 });
            var configs = new uint[] { 3181, 3182, 3183, 3184, 3281, 3281, 3301, 3301 };
            for (int index = 0; index < tables.Slots.Count; index++)
            {
                var slot = tables.Slots[index];
                var item = tables.Product(configs[index], WornSword + (ulong)index);
                if (item.EquipProfile == 2)
                    foreach (var position in item.FixedAffixSlots)
                        position.Selected = tables.Rules.Affixes.Single(a => a.AffixId == (position.Slot == 1 ? 301u : 302u)).Clone();
                Equipment.Items.Add(item);
                Equipment.Layout.EquipSlots.Add(new EquipSlotInfo { Slot = slot.Id, EquipKind = slot.EquipKind, Name = slot.Name });
                Equipment.Layout.Slots.Add(new BagSlotInfo { Slot = slot.Id, ItemId = item.ItemId, Width = 1, Height = 1 });
            }
            Bag.Items.Add(tables.Product(3181, CandidateSword));
            Bag.Items.Add(tables.Product(3281, CandidateJewelry));
            foreach (uint material in new uint[] { 3001, 3002 })
            {
                var row = tables.Items[material];
                Bag.Items.Add(new BagItemInfo { ItemId = material + 990000UL, ConfigId = material, Count = 300,
                    MaxStack = row.MaxStackSize, Name = row.Name, IconKey = row.IconKey });
            }
            Relayout();
        }
        public void RegisterNotify(uint messageId, Action<MessageContent> handler) { }
        public void SendOneWay(uint messageId, IMessage request) => throw new InvalidOperationException("Fixture transport forbids one-way sends.");
        public int Count<T>() where T : IMessage => Calls.Count(call => call.Request is T);
        public T Last<T>() where T : IMessage => (T)Calls.Last(call => call.Request is T).Request;
        public void CompleteDeferredResponse()
        {
            Check(_deferredResponse != null, "A fixture response must be waiting before completion.");
            var complete = _deferredResponse;
            _deferredResponse = null;
            complete();
        }
        public void Call<TResponse>(uint messageId, IMessage request, MessageParser<TResponse> parser,
            Action<TResponse> onResponse, Action<string> onError) where TResponse : IMessage<TResponse>
        {
            Calls.Add(new CallRecord { MessageId = messageId, Request = request.Descriptor.Parser.ParseFrom(request.ToByteArray()) });
            IMessage response;
            switch (request)
            {
                case GetBagRequest get:
                    Check(messageId == SceneBagClientPlayerRpc.GetBagMessageId, "Bag query must use its generated message ID.");
                    response = new GetBagResponse { Bag = (get.BagType == 2 ? Equipment : Bag).Clone() }; break;
                case GetEquipmentCraftRulesRequest _:
                    var rules = _tables.Rules.Clone(); rules.ForgeRevision = _revision; response = rules; break;
                case PreviewEquipmentCraftRequest preview:
                    response = new PreviewEquipmentCraftResponse { Preview = Preview(preview.Choice) }; break;
                case CommitEquipmentCraftRequest commit:
                    response = Commit(commit); break;
                case EquipItemRequest equip:
                    response = Equip(equip); break;
                case EnhanceEquipRequest enhance:
                    response = Enhance(enhance); break;
                default: throw new InvalidOperationException("Unexpected fixture request: " + request.Descriptor.FullName);
            }
            // 与真实传输一样经过 protobuf 编解码，不把可变夹具对象交给客户端。
            if ((request is PreviewEquipmentCraftRequest && DeferNextPreview) ||
                (request is CommitEquipmentCraftRequest && DeferNextCommit))
            {
                Check(_deferredResponse == null, "Only one fixture response may be deferred at a time.");
                DeferNextPreview = false; DeferNextCommit = false;
                var bytes = response.ToByteArray();
                _deferredResponse = () => onResponse(parser.ParseFrom(bytes));
                return;
            }
            onResponse(parser.ParseFrom(response.ToByteArray()));
        }

        private EquipmentCraftPreview Preview(EquipmentCraftChoice choice)
        {
            BagItemInfo product;
            uint material, count;
            if (choice.Operation == EquipmentCraftChoice.Types.Operation.CreateItem)
            {
                var recipe = _tables.Rules.Recipes.Single(r => r.Product.ConfigId == choice.ConfigId);
                product = recipe.Product.Clone(); material = recipe.MaterialConfigId; count = recipe.MaterialCount;
            }
            else
            {
                product = Find(choice.ItemId).Clone();
                var affix = _tables.Rules.Affixes.Single(a => a.AffixId == choice.AffixId);
                var slot = product.FixedAffixSlots.Single(s => s.Slot == choice.AffixSlot);
                Check(slot.Selected == null && slot.Pool == affix.Pool && !product.FixedAffixSlots.Any(s => s.Selected?.AffixId == affix.AffixId),
                    "Fixture affix request must select a valid empty slot and avoid duplicates.");
                slot.Selected = affix.Clone(); material = affix.MaterialConfigId; count = affix.MaterialCount;
            }
            var row = _tables.Items[material];
            ulong owned = (ulong)Bag.Items.Where(item => item.ConfigId == material).Sum(item => (long)item.Count);
            return new EquipmentCraftPreview { Choice = choice.Clone(), Result = product, RulesFingerprint = _tables.Rules.RulesFingerprint,
                ExpectedForgeRevision = _revision, Available = owned >= count,
                Material = new EquipmentCraftMaterial { ConfigId = material, Name = row.Name, IconKey = row.IconKey, Count = count, Owned = owned, Available = owned } };
        }

        private CommitEquipmentCraftResponse Commit(CommitEquipmentCraftRequest request)
        {
            Check(request.ClientRequestId != 0 && request.ExpectedForgeRevision == _revision && request.RulesFingerprint.Equals(_tables.Rules.RulesFingerprint),
                "Fixture commit must carry the current preview identity.");
            var preview = Preview(request.Choice);
            Check(preview.Available, "Fixture commit must be funded.");
            Consume(preview.Material.ConfigId, preview.Material.Count);
            var product = preview.Result;
            if (request.Choice.Operation == EquipmentCraftChoice.Types.Operation.CreateItem)
            { product.ItemId = ++_nextItem; Bag.Items.Add(product); }
            else Replace(product);
            Relayout(); ++_revision;
            return new CommitEquipmentCraftResponse { ClientRequestId = request.ClientRequestId,
                Outcome = CommitEquipmentCraftResponse.Types.Outcome.Applied, Durable = true, ItemId = product.ItemId,
                ResultForgeRevision = _revision, CurrentForgeRevision = _revision, Bag = Bag.Clone(), Equipment = Equipment.Clone() };
        }

        private EquipItemResponse Equip(EquipItemRequest request)
        {
            Check(request.HasTargetSlot, "Fixture equip must specify its target slot.");
            var candidate = Bag.Items.Single(item => item.ItemId == request.ItemId);
            var definition = Equipment.Layout.EquipSlots.Single(slot => slot.Slot == request.TargetSlot);
            Check(definition.EquipKind == candidate.EquipKind, "Candidate kind must match the target slot.");
            var occupied = Equipment.Layout.Slots.Single(slot => slot.Slot == request.TargetSlot);
            var old = Equipment.Items.Single(item => item.ItemId == occupied.ItemId);
            Bag.Items.Remove(candidate); Equipment.Items.Remove(old); Bag.Items.Add(old); Equipment.Items.Add(candidate);
            occupied.ItemId = candidate.ItemId; Relayout();
            return new EquipItemResponse { Bag = Bag.Clone(), Equipment = Equipment.Clone() };
        }

        private EnhanceEquipResponse Enhance(EnhanceEquipRequest request)
        {
            var current = Find(request.ItemId);
            Check(current.EnhanceLevel == request.ExpectedLevel && current.EnhanceNext != null, "Fixture enhancement must match the expected level.");
            Consume(current.EnhanceNext.MaterialConfigId, current.EnhanceNext.MaterialCount);
            var enhanced = _tables.Product(current.ConfigId, current.ItemId, current.EnhanceLevel + 1);
            enhanced.FixedAffixSlots.Clear(); enhanced.FixedAffixSlots.Add(current.FixedAffixSlots.Select(s => s.Clone()));
            Replace(enhanced); Relayout();
            return new EnhanceEquipResponse { Bag = Bag.Clone(), Equipment = Equipment.Clone(), Success = true, EnhanceLevel = enhanced.EnhanceLevel };
        }

        private BagItemInfo Find(ulong id) => Bag.Items.Concat(Equipment.Items).Single(item => item.ItemId == id);
        private void Replace(BagItemInfo item)
        {
            var owner = Bag.Items.Any(entry => entry.ItemId == item.ItemId) ? Bag : Equipment;
            int index = owner.Items.IndexOf(owner.Items.Single(entry => entry.ItemId == item.ItemId));
            owner.Items[index] = item;
        }
        private void Consume(uint config, uint count)
        {
            var stack = Bag.Items.Single(item => item.ConfigId == config);
            Check(stack.Count >= count, "Fixture materials must cover the full request.");
            stack.Count -= count;
            if (stack.Count == 0) Bag.Items.Remove(stack);
        }
        private void Relayout()
        {
            Bag.Layout.Slots.Clear();
            for (int i = 0; i < Bag.Items.Count; i++)
                Bag.Layout.Slots.Add(new BagSlotInfo { Slot = (uint)i, ItemId = Bag.Items[i].ItemId, Width = 1, Height = 1 });
        }
    }

    private static string Digest(byte[] bytes)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    [Serializable] private sealed class CaptureReport
    {
        public string status, startedUtc, finishedUtc, failure;
        public string scope = "Native Unity preview-scene capture with editor-only fixtures; no server, Play Mode, production assets or pixel-parity acceptance.";
        public List<Screenshot> screenshots = new();
        public List<TableEvidence> tables = new();
        public List<RequestEvidence> requests = new();
        public List<string> assertions = new();
    }
    [Serializable] private sealed class Screenshot { public string file, sha256, state; public int width, height; public float scale; }
    [Serializable] private sealed class TableEvidence { public string source, sha256; }
    [Serializable] private sealed class RequestEvidence { public uint messageId; public string requestType, requestJson; }
}
#endif
