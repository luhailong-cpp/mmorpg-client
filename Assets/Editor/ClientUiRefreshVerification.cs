#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Role;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Pet;
using TMPro;
using MmorpgClient.UI.Ugui.Tweening;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Offline screenshots of production controls; fixture values never enter a live session.</summary>
public static class ClientUiRefreshVerification
{
    public static string OutputDirectory { get; set; } = Path.GetFullPath(Path.Combine(Application.dataPath,
        "../.codex-artifacts/ui-refresh-20260909"));

    [MenuItem("MMORPG/UI/Capture refreshed screens")]
    public static void CaptureAll() { Capture(2560, 1080); Capture(1920, 1080); }

    public static void Capture(int width, int height)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Capture in Edit Mode.");
        Directory.CreateDirectory(OutputDirectory);
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D pixels = null;
        try
        {
            var camera = new GameObject("RefreshCaptureCamera").AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100f;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = QdaoUguiTheme.Letterbox;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create(); camera.targetTexture = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            RealtimeTween.BeginEditorCapture();
            var canvasObject = new GameObject("RefreshCaptureCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var root = QdaoUguiFactory.CreateCenteredRect("DesignRoot", canvasObject.transform, 2560, 1080);
            void Shoot(string name)
            {
                RealtimeTween.AdvanceEditorCapture(1f);
                foreach (var go in scene.GetRootGameObjects())
                {
                    foreach (var transform in go.GetComponentsInChildren<UnityEngine.Transform>(true)) transform.gameObject.layer = 31;
                    foreach (var canvas in go.GetComponentsInChildren<Canvas>(true))
                    { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 2f; }
                }
                Canvas.ForceUpdateCanvases();
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var label in go.GetComponentsInChildren<TMP_Text>(true)) label.ForceMeshUpdate(true, true);
                Canvas.ForceUpdateCanvases(); camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(OutputDirectory, name + "_" + width + "x" + height + ".png"), pixels.EncodeToPNG());
            }
            var prefab = Resources.Load<GameObject>("UI/Ugui/Prefabs/QdaoServerSelect");
            var page = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            var view = page.GetComponent<QdaoServerSelectView>();
            view.PrepareForPreview(); Shoot("02-server");
            view.ShowLanding(true);
            Shoot("01-login");
            view.ShowCredentialForPreview(); Shoot("03-account");
            UnityEngine.Object.DestroyImmediate(page);

            RoleFlowUi.ShowEditorPreview(false); Shoot("04-character-select"); RoleFlowUi.HideEditorPreview();
            RoleFlowUi.ShowEditorPreview(true); Shoot("05-character-create"); RoleFlowUi.HideEditorPreview();

            QdaoUguiFactory.CreateImage("WorldBackdrop", root, 0, 0, 2560, 1080, QdaoRefreshArt.Load("sanctuary_background"));
            var characterRoot = QdaoUguiFactory.CreateRect("CharacterCapture", root, 0, 0, 2560, 1080);
            var attributeOwner = new GameObject("AttributeQaOwner").AddComponent<AttributeUiRoot>(); attributeOwner.enabled = false;
            var attributes = new AttributePanel(attributeOwner, characterRoot);
            attributes.ApplyPanel(CharacterFixture()); attributes.Show(); Shoot("06-character-attributes");

            UnityEngine.Object.DestroyImmediate(characterRoot.gameObject);
            var petRoot = QdaoUguiFactory.CreateRect("PetCapture", root, 0, 0, 2560, 1080);
            var petOwner = new GameObject("PetQaOwner").AddComponent<PetUiRoot>(); petOwner.enabled = false;
            var pets = new PetPanel(petOwner, petRoot);
            pets.ApplyList(PetFixture()); pets.Show(); Shoot("07-pets");

            pets.ApplyList(new PetListInfo { MaxPets = 8 }); pets.ApplyBusy(false); Shoot("07c-pets-empty");
            Debug.Log("UI_REFRESH_CAPTURE_OK|" + width + "x" + height + "|" + OutputDirectory);
        }
        finally
        {
            RoleFlowUi.HideEditorPreview();
            RealtimeTween.EndEditorCapture();
            RenderTexture.active = previousTarget;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static AttributePanelInfo CharacterFixture()
    {
        var data = new AttributePanelInfo { ActiveSchemeId = 1, MaxSchemes = 3, Level = 75, CreateSchemeCostGold = 100000 };
        data.Schemes.Add(new AttributeSchemeInfo { SchemeId = 1, Name = "方案一" });
        data.Schemes.Add(new AttributeSchemeInfo { SchemeId = 2, Name = "全灵方案" });
        data.Pools.Add(new AttributePoolInfo { PoolId = 1, Name = "属性点", Total = 300, Remaining = 10, Unlocked = true, DimensionCap = 300 });
        data.Pools.Add(new AttributePoolInfo { PoolId = 2, Name = "相性点", Total = 40, Remaining = 5, Unlocked = true, DimensionCap = 30 });
        string[] names = { "体质", "灵力", "力量", "敏捷" };
        for (uint i = 0; i < 4; i++) data.Dimensions.Add(new AttributeDimensionInfo { DimensionId = 101 + i, PoolId = 1, Name = names[i], Desc = "提高角色战斗能力。", Allocated = 60, Value = 135, Cap = 300, Sort = i });
        data.Derived = new DerivedAttributeInfo { Health = 5800, MaxHealth = 5800, Mana = 2300, MaxMana = 2300, PhysicalAttack = 1380, MagicAttack = 2180, Speed = 860, Defense = 960 };
        return data;
    }

    private static PetListInfo PetFixture()
    {
        var data = new PetListInfo { MaxPets = 8, ActivePetId = 1 };
        string[] names = { "灵狐", "石灵", "金猊", "云鹤" };
        string[] dimensions = { "体质", "灵力", "力量", "敏捷" };
        for (uint i = 0; i < 4; i++)
        {
            var pet = new PetInfo { PetId = i + 1, PetTableId = i + 1, ModelId = 1001 + i, Name = names[i], Level = 75, IsActive = i == 0, Growth = 10000, RemainingPoints = 12 };
            pet.Derived = new PetDerivedInfo { Health = 4280, MaxHealth = 4280, Mana = 2980, MaxMana = 2980, PhysicalAttack = 1026, MagicAttack = 1850, Speed = 728, Defense = 980 };
            for (uint j = 0; j < 4; j++) pet.Dimensions.Add(new PetDimensionInfo { DimensionId = 101 + j, Name = dimensions[j], Desc = "服务器属性说明。", Allocated = 50, Value = 125, Cap = 300, Sort = j });
            data.Pets.Add(pet);
        }
        return data;
    }
}
#endif
