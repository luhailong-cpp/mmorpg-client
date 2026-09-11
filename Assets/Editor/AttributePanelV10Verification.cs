#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Pet;
using MmorpgClient.UI.Ugui.Tweening;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Offline verification of the production character and pet panels. Inactive owners have
/// no client or singleton lifecycle; fixtures never enter a game session or send requests.
/// </summary>
public static class AttributePanelV10Verification
{
    public static string OutputDirectory { get; set; } = Path.GetFullPath(Path.Combine(
        Application.dataPath, "../.codex-artifacts/attribute-ui-v10-20260911"));

    private static TestRunnerApi _testApi;
    private static readonly string[] PaintedAssets =
    {
        "button_primary", "button_scheme", "button_secondary", "close_button", "close_tassel",
        "divider", "dropdown_arrow", "notice_icon", "paper_tile", "pet_card_normal", "pet_card_selected",
        "portrait_frame", "portrait_fuxiaohu", "portrait_hutuantuan", "portrait_lingyue", "portrait_yunjiujiu",
        "section_header", "slider_fill", "slider_thumb", "slider_track", "stat_field", "step_minus",
        "step_plate", "step_plus", "tab_horizontal", "tab_vertical_normal", "tab_vertical_selected",
        "title_character", "title_pet", "title_plate", "window_frame"
    };

    [MenuItem("MMORPG/UI/Verify attribute and pet v10 screens")]
    public static void CaptureAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Attribute and pet capture requires Edit Mode.");
        Directory.CreateDirectory(OutputDirectory);
        var checks = new List<string>();
        File.WriteAllText(Path.Combine(OutputDirectory, "bounds-diagnostics.txt"), "Capture started " + DateTime.UtcNow.ToString("O") + "\n\n");
        File.WriteAllText(Path.Combine(OutputDirectory, "fixture-scope.txt"),
            "Production AttributePanel and PetPanel rendered in temporary additive editor scenes.\n" +
            "Character and pet values are visual fixtures, not live account data or server configuration.\n" +
            "Owners remain inactive with no clients. No Show(), transport, AppBootstrap, or game session is used.\n" +
            "The original active scene is restored and temporary scenes are closed without saving.\n");
        try
        {
            foreach (var name in PaintedAssets)
            {
                var sprite = Resources.Load<Sprite>("UI/Ugui/AttributesPaintedV2/" + name);
                Require(sprite != null && sprite.texture != null && sprite.rect.width > 0f && sprite.rect.height > 0f,
                    "Painted resource must load: " + name);
            }
            checks.Add("PASS: all 31 painted sprites load with valid textures and dimensions.");
            Capture(2560, 1080, checks);
            Capture(1920, 1080, checks);
            File.WriteAllLines(Path.Combine(OutputDirectory, "qa-checks.txt"), checks);
            File.WriteAllText(Path.Combine(OutputDirectory, "capture-status.txt"),
                "Passed: 10 screenshots, five production panel states at each resolution.\n" +
                "Resolutions: 2560x1080 and 1920x1080.\n" +
                "States: character normal/eight rows at bottom; pets normal/empty/ten pets at bottom.\n" +
                "Checks: 31 resources, native painted images, no affinity tabs, local pet draft/reset, scroll visibility.\n");
            Debug.Log("ATTRIBUTE_V10_CAPTURE_FINISHED|Passed|" + OutputDirectory);
        }
        catch (Exception error)
        {
            checks.Add("FAIL: " + error.Message);
            File.WriteAllLines(Path.Combine(OutputDirectory, "qa-checks.txt"), checks);
            File.WriteAllText(Path.Combine(OutputDirectory, "capture-status.txt"), "Failed\n" + error);
            throw;
        }
    }

    private static void Capture(int width, int height, List<string> checks)
    {
        var previousScene = SceneManager.GetActiveScene();
        var previousTarget = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        RenderTexture target = null;
        Texture2D pixels = null;
        bool tweenCaptureStarted = false;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("AttributeV10CaptureCamera").AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new UnityEngine.Vector3(0f, 0f, -10f);
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
            RealtimeTween.BeginEditorCapture();
            tweenCaptureStarted = true;

            var canvasObject = new GameObject("AttributeV10CaptureCanvas", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 2f;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var root = QdaoUguiFactory.CreateCenteredRect("AttributeV10DesignRoot", canvasObject.transform, 2560f, 1080f);
            QdaoUguiFactory.CreateImage("WorldBackdrop", root, 0f, 0f, 2560f, 1080f,
                QdaoRefreshArt.Load("sanctuary_background"));

            void Prepare()
            {
                foreach (var go in scene.GetRootGameObjects())
                {
                    foreach (var child in go.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
                    foreach (var childCanvas in go.GetComponentsInChildren<Canvas>(true))
                    {
                        childCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                        childCanvas.worldCamera = camera;
                        childCanvas.planeDistance = 2f;
                    }
                }
                Canvas.ForceUpdateCanvases();
                foreach (var label in canvasObject.GetComponentsInChildren<TMP_Text>(true)) label.ForceMeshUpdate(true, true);
                Canvas.ForceUpdateCanvases();
            }

            void Shoot(string name)
            {
                RealtimeTween.AdvanceEditorCapture(1f);
                Prepare();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(OutputDirectory, name + "_" + width + "x" + height + ".png"),
                    pixels.EncodeToPNG());
            }

            var characterRoot = QdaoUguiFactory.CreateRect("CharacterFixture", root, 0f, 0f, 2560f, 1080f);
            var characterOwner = CreateInactiveOwner<AttributeUiRoot>("InactiveAttributeOwner", root);
            Require(characterOwner.Client == null, "Character capture owner must have no game client.");
            var attributes = new AttributePanel(characterOwner, characterRoot);
            var characterData = CharacterFixture(4);
            attributes.ApplyPanel(characterData);
            Find<RectTransform>(characterRoot, "AttributeWindow").gameObject.SetActive(true);
            ValidatePaintedWindow(characterRoot, "AttributeWindow", "title_character");
            foreach (var button in characterRoot.GetComponentsInChildren<Button>(true))
            {
                var label = button.GetComponentInChildren<TMP_Text>(true);
                var text = label == null ? string.Empty : label.text.Replace(" ", string.Empty).Replace("\n", string.Empty);
                Require(!text.Contains("相性") && !text.Contains("仙魔"), "Character window must have no affinity tabs.");
            }
            Shoot("01-character-normal");
            ValidateCharacterSliderHandles(characterRoot, 4);

            attributes.ApplyPanel(CharacterFixture(8));
            Prepare();
            var pointScroll = Find<ScrollRect>(characterRoot, "PointsViewport");
            LogScrollDiagnostics("before-bottom-set", pointScroll, Find<RectTransform>(characterRoot, "Row_定力"));
            pointScroll.verticalNormalizedPosition = 0f;
            LogScrollDiagnostics("after-bottom-set", pointScroll, Find<RectTransform>(characterRoot, "Row_定力"));
            Shoot("02-character-eight-rows-bottom");
            ValidateCharacterSliderHandles(characterRoot, 8);
            RequireFullyVisible(pointScroll.viewport, Find<RectTransform>(characterRoot, "Row_定力"));
            checks.Add("PASS " + width + "x" + height + ": character native painted controls, no affinity tabs, 60x60 handles in four/eight-row states, eighth row fully visible at scroll bottom.");
            UnityEngine.Object.DestroyImmediate(characterRoot.gameObject);
            UnityEngine.Object.DestroyImmediate(characterOwner.gameObject);

            var petRoot = QdaoUguiFactory.CreateRect("PetFixture", root, 0f, 0f, 2560f, 1080f);
            var petOwner = CreateInactiveOwner<PetUiRoot>("InactivePetOwner", root);
            Require(petOwner.Client == null, "Pet capture owner must have no game client.");
            var pets = new PetPanel(petOwner, petRoot);
            var petData = PetFixture(4);
            pets.ApplyList(petData);
            Find<RectTransform>(petRoot, "PetWindow").gameObject.SetActive(true);
            ValidatePaintedWindow(petRoot, "PetWindow", "title_pet");
            ValidateLocalPetDraft(petRoot, petData);
            pets.SetStatus(string.Empty);
            Shoot("03-pets-normal");

            pets.ApplyList(PetFixture(0));
            Shoot("04-pets-empty");
            Require(Find<TMP_Text>(petRoot, "EmptyRoster").gameObject.activeInHierarchy, "Empty pet list must display its empty state.");
            Require(!Find<Button>(petRoot, "ConfirmPlate").interactable, "Empty pet list must disable allocation confirmation.");

            pets.ApplyList(PetFixture(10));
            Prepare();
            var petScroll = Find<ScrollRect>(petRoot, "PetRosterViewport");
            petScroll.verticalNormalizedPosition = 0f;
            Shoot("05-pets-ten-bottom");
            int visibleItems = petRoot.GetComponentsInChildren<Button>(true)
                .Count(button => button.name.StartsWith("PetItem", StringComparison.Ordinal) && button.gameObject.activeInHierarchy);
            Require(visibleItems == 10, "Ten-pet fixture must create ten active roster cards.");
            RequireFullyVisible(petScroll.viewport, Find<RectTransform>(petRoot, "PetItem9Plate"));
            checks.Add("PASS " + width + "x" + height + ": pet native painted controls, draft/reset keeps fixture values unchanged, empty state, ten cards and last card fully visible.");

        }
        finally
        {
            if (tweenCaptureStarted) RealtimeTween.EndEditorCapture();
            RenderTexture.active = previousTarget;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static T CreateInactiveOwner<T>(string name, UnityEngine.Transform parent) where T : Behaviour
    {
        var owner = new GameObject(name);
        owner.SetActive(false);
        owner.transform.SetParent(parent, false);
        var component = owner.AddComponent<T>();
        component.enabled = false;
        return component;
    }

    private static T Find<T>(Component root, string name) where T : Component
        => root.GetComponentsInChildren<T>(true).Single(item => item.name == name);

    private static void ValidatePaintedWindow(Component root, string windowName, string titleName)
    {
        var frame = Find<Image>(root, windowName);
        Require(frame.sprite != null && frame.sprite.name == "window_frame" && frame.type == Image.Type.Sliced,
            windowName + " must use its imported nine-slice frame.");
        var title = Find<Image>(root, "Title");
        Require(title.sprite != null && title.sprite.name == titleName, windowName + " must use its imported title.");
        int paintedCount = root.GetComponentsInChildren<Image>(true).Count(image => image.sprite != null &&
            AssetDatabase.GetAssetPath(image.sprite).StartsWith("Assets/Resources/UI/Ugui/AttributesPaintedV2/", StringComparison.Ordinal));
        Require(paintedCount >= 20, windowName + " must render independently assembled painted controls.");
    }

    private static void ValidateCharacterSliderHandles(Component root, int expectedRows)
    {
        var sliders = root.GetComponentsInChildren<Slider>(false);
        Require(sliders.Length == expectedRows, "Character fixture must show its expected number of allocation rows.");
        foreach (var slider in sliders)
        {
            Require(slider.handleRect != null, slider.name + " must have a visible handle.");
            var rect = slider.handleRect.rect;
            Require(Mathf.Abs(rect.width - 60f) <= .05f && Mathf.Abs(rect.height - 60f) <= .05f,
                slider.name + " handle must have an actual 60x60 rect, got " + rect.size + ".");
        }
    }
    private static void ValidateLocalPetDraft(Component root, PetListInfo data)
    {
        var value = Find<TMP_Text>(root, "Row0Value");
        var remain = Find<TMP_Text>(root, "Remain");
        string committedText = value.text;
        string originalRemain = remain.text;
        uint committed = data.Pets[0].Dimensions[0].Allocated;
        uint remaining = data.Pets[0].RemainingPoints;
        var plus = Find<Button>(root, "Row0PlusPlate");
        Require(plus.interactable, "Pet fixture plus button must be usable.");
        plus.onClick.Invoke();
        Require(value.text.Contains("+1"), "Pet plus button must display an uncommitted point.");
        Require(Find<Button>(root, "ConfirmPlate").interactable, "Pet draft must enable confirmation.");
        var discard = Find<Button>(root, "DiscardPlate");
        Require(discard.interactable, "Pet draft must enable local reset.");
        discard.onClick.Invoke();
        Require(value.text == committedText && remain.text == originalRemain, "Local reset must restore committed display values.");
        Require(!discard.interactable && !Find<Button>(root, "ConfirmPlate").interactable,
            "Reset draft must disable local reset and confirmation.");
        Require(data.Pets[0].Dimensions[0].Allocated == committed && data.Pets[0].RemainingPoints == remaining,
            "Local draft and reset must not mutate authoritative fixture values.");
    }

    private static void RequireFullyVisible(RectTransform viewport, RectTransform item)
    {
        Require(item.gameObject.activeInHierarchy, item.name + " must be active.");
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, item);
        var view = viewport.rect;
        LogScrollDiagnostics("visibility-assert", viewport.GetComponent<ScrollRect>(), item);
        const float tolerance = 1f;
        Require(bounds.min.y >= view.yMin - tolerance && bounds.max.y <= view.yMax + tolerance,
            item.name + " must be fully visible inside its viewport at the bottom of the list. " +
            "viewportY=[" + view.yMin + "," + view.yMax + "] descendantBoundsY=[" + bounds.min.y + "," + bounds.max.y + "]");
    }

    private static void LogScrollDiagnostics(string stage, ScrollRect scroll, RectTransform item)
    {
        if (scroll == null) return;
        var viewport = scroll.viewport;
        var aggregate = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, item);
        var canvas = viewport.GetComponentInParent<Canvas>();
        var capture = canvas == null || canvas.worldCamera == null ? null : canvas.worldCamera.targetTexture;
        string resolution = capture == null ? "unknown" : capture.width + "x" + capture.height;
        var lines = new List<string>
        {
            DateTime.UtcNow.ToString("O") + " " + stage + " " + resolution,
            "scroll=" + scroll.name + " active=" + scroll.isActiveAndEnabled + " normalized=" + scroll.verticalNormalizedPosition + " velocity=" + scroll.velocity,
            "viewport " + DescribeRect(viewport),
            "content " + DescribeRect(scroll.content),
            "item " + DescribeRect(item),
            "aggregate bounds min=" + aggregate.min + " max=" + aggregate.max + " size=" + aggregate.size
        };
        foreach (var child in item.GetComponentsInChildren<RectTransform>(true))
        {
            var corners = new UnityEngine.Vector3[4];
            child.GetWorldCorners(corners);
            var own = new UnityEngine.Bounds(viewport.InverseTransformPoint(corners[0]), UnityEngine.Vector3.zero);
            for (int i = 1; i < corners.Length; i++) own.Encapsulate(viewport.InverseTransformPoint(corners[i]));
            lines.Add("descendant " + child.name + " active=" + child.gameObject.activeInHierarchy +
                " ownMin=" + own.min + " ownMax=" + own.max + " " + DescribeRect(child));
        }
        File.AppendAllText(Path.Combine(OutputDirectory, "bounds-diagnostics.txt"), string.Join("\n", lines) + "\n\n");
        Debug.Log("ATTRIBUTE_V10_VISIBILITY|" + stage + "|" + resolution + "|" + item.name +
            "|view=" + viewport.rect + "|content=" + scroll.content.anchoredPosition +
            "|bounds=" + aggregate + "|normalized=" + scroll.verticalNormalizedPosition);
    }

    private static string DescribeRect(RectTransform rect)
        => rect == null ? "null" : rect.name + " rect=" + rect.rect + " anchored=" + rect.anchoredPosition +
            " localPosition=" + rect.localPosition + " anchors=" + rect.anchorMin + "/" + rect.anchorMax +
            " pivot=" + rect.pivot + " sizeDelta=" + rect.sizeDelta + " scale=" + rect.localScale;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static AttributePanelInfo CharacterFixture(int dimensionCount)
    {
        var data = new AttributePanelInfo { ActiveSchemeId = 1, MaxSchemes = 3, Level = 75, CreateSchemeCostGold = 100000 };
        data.Schemes.Add(new AttributeSchemeInfo { SchemeId = 1, Name = "方案一" });
        data.Schemes.Add(new AttributeSchemeInfo { SchemeId = 2, Name = "全灵方案" });
        data.Pools.Add(new AttributePoolInfo { PoolId = 1, Name = "属性点", Total = 300, Remaining = 10, Unlocked = true, DimensionCap = 300 });
        data.Pools.Add(new AttributePoolInfo { PoolId = 2, Name = "相性点", Total = 40, Remaining = 5, Unlocked = true, DimensionCap = 30 });
        string[] names = { "体质", "灵力", "力量", "敏捷", "根骨", "悟性", "身法", "定力" };
        for (int i = 0; i < dimensionCount; i++)
            data.Dimensions.Add(new AttributeDimensionInfo
            {
                DimensionId = (uint)(101 + i), PoolId = 1, Name = names[i], Desc = "编辑器视觉样例：提高角色战斗能力。",
                Allocated = i < 4 ? 60u : 10u, Value = i < 4 ? 135u : 25u, Cap = 300, Sort = (uint)i
            });
        data.Derived = new DerivedAttributeInfo
        {
            Health = 5800, MaxHealth = 5800, Mana = 2300, MaxMana = 2300,
            PhysicalAttack = 1380, MagicAttack = 2180, Speed = 860, Defense = 960
        };
        return data;
    }

    private static PetListInfo PetFixture(int count)
    {
        var data = new PetListInfo { MaxPets = 10, ActivePetId = count == 0 ? 0UL : 1UL };
        string[] names = { "灵狐", "石灵", "金猊", "云鹤" };
        string[] dimensions = { "体质", "灵力", "力量", "敏捷" };
        for (int i = 0; i < count; i++)
        {
            var pet = new PetInfo
            {
                PetId = (ulong)(i + 1), PetTableId = (uint)(i % 4 + 1), ModelId = (uint)(1001 + i % 4),
                Name = names[i % 4] + (i < 4 ? string.Empty : "·" + (i + 1)), Level = 75, IsActive = i == 0,
                Growth = 10000, RemainingPoints = 12, TotalPoints = 212, Desc = "编辑器宝宝培养视觉样例。"
            };
            pet.Derived = new PetDerivedInfo
            {
                Health = 4280, MaxHealth = 4280, Mana = 2980, MaxMana = 2980,
                PhysicalAttack = 1026, MagicAttack = 1850, Speed = 728, Defense = 980
            };
            for (uint j = 0; j < dimensions.Length; j++)
                pet.Dimensions.Add(new PetDimensionInfo
                {
                    DimensionId = 101 + j, Name = dimensions[j], Desc = "编辑器视觉样例属性说明。",
                    Allocated = 50, Value = 125, Cap = 300, Sort = j
                });
            data.Pets.Add(pet);
        }
        return data;
    }

    [MenuItem("MMORPG/UI/Test attribute and pet panels")]
    public static void RunTests()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Attribute and pet tests require Edit Mode.");
        if (_testApi != null) throw new InvalidOperationException("Attribute and pet tests are already running.");
        Directory.CreateDirectory(OutputDirectory);
        string output = OutputDirectory;
        File.WriteAllText(Path.Combine(output, "tests-status.txt"), "Running\n");
        _testApi = ScriptableObject.CreateInstance<TestRunnerApi>();
        _testApi.RegisterCallbacks(new TestResults(output));
        _testApi.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.EditMode,
            testNames = new[]
            {
                "MmorpgClient.Tests.EditMode.Battle.AttributeClientTests",
                "MmorpgClient.Tests.EditMode.Battle.PetClientTests",
                "MmorpgClient.Tests.EditMode.Battle.PetPanelInteractionTests"
            }
        }));
    }

    private sealed class TestResults : ICallbacks
    {
        private readonly string _output;
        public TestResults(string output) => _output = output;
        public void RunStarted(ITestAdaptor tests) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            try
            {
                TestRunnerApi.SaveResultToFile(result, Path.Combine(_output, "tests.xml"));
                File.WriteAllText(Path.Combine(_output, "tests-status.txt"),
                    $"passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount} result={result.ResultState}\n");
                Debug.Log("ATTRIBUTE_V10_TESTS_FINISHED|" + result.ResultState + "|" + _output);
            }
            finally
            {
                if (_testApi != null)
                {
                    _testApi.UnregisterCallbacks(this);
                    UnityEngine.Object.DestroyImmediate(_testApi);
                    _testApi = null;
                }
            }
        }
    }
}
#endif
