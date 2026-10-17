#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Tweening;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Production UI integration fixtures. No account, game session or live requests are created.</summary>
public static class EquipmentIntegrationVerification
{
    public static string OutputDirectory = "E:/work/image/designs/equipment-ui-20261009/integration-qa";

    [MenuItem("MMORPG/UI/Capture connected equipment screens")]
    public static void CaptureAll()
    {
        Directory.CreateDirectory(OutputDirectory);
        File.WriteAllText(Path.Combine(OutputDirectory, "verification.txt"), "RUNNING\n");
        Capture(2560, 1080);
        Capture(1920, 1080);
        Capture(1280, 720);
        File.WriteAllText(Path.Combine(OutputDirectory, "verification.txt"),
            "PASS: 18 native Unity uGUI screenshots at three resolutions.\n" +
            "Checked equipment filter, equipped/unequipped actions, detached share event, more dialog, unavailable wish pool, layered back, " +
            "attribute navigation event, equipment bonuses and server combat attributes.\n" +
            "All values are editor fixtures; no session, inventory mutation, server request or real battle was tested.\n");
    }

    public static void Capture(int width, int height)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Requires Edit Mode.");
        Directory.CreateDirectory(OutputDirectory);
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D pixels = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("EquipmentIntegrationCamera").AddComponent<Camera>();
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

            var canvasObject = new GameObject("EquipmentIntegrationCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 2;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var root = QdaoUguiFactory.CreateCenteredRect("EquipmentIntegrationRoot", canvasObject.transform, 2560, 1080);
            QdaoUguiFactory.CreateImage("Backdrop", root, 0, 0, 2560, 1080, QdaoRefreshArt.Load("sanctuary_background"));
            var scope = GameplayUiArt.Text(root, "编辑器联动预览 · 数值为布局样例 · 未连接游戏服务器", 44, 1030, 2200, 34, 24,
                QdaoUguiTheme.Cream);
            scope.raycastTarget = false;

            void Shoot(string name)
            {
                scope.transform.SetAsLastSibling();
                RealtimeTween.AdvanceEditorCapture(1f);
                foreach (var go in scene.GetRootGameObjects())
                {
                    foreach (var child in go.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
                    foreach (var childCanvas in go.GetComponentsInChildren<Canvas>(true))
                    {
                        childCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                        childCanvas.worldCamera = camera;
                        childCanvas.planeDistance = 2;
                    }
                }
                Canvas.ForceUpdateCanvases();
                foreach (var text in canvasObject.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(OutputDirectory, name + "_" + width + "x" + height + ".png"), pixels.EncodeToPNG());
            }

            var window = new GameplayWindow(root);
            var bag = BagFixture();
            var equipment = EquipmentFixture();
            window.SetBagPage(bag, false, null, false, equipment, false, null, false);
            window.SetCharacterLevel(80);
            window.ShowEquipment();
            Require(!root.GetComponentsInChildren<Button>().Any(button => button.name == "BagSlot_0"),
                "Equipment filtering must hide the non-equipment fixture.");
            Click(root, "BagSlot_1");
            Require(Find<TMP_Text>(root, "TooltipState").text == "未穿戴", "Bag item detail must show unequipped state.");
            ulong equipped = 0, unequipped = 0;
            window.EquipRequested += id => equipped = id;
            window.UnequipRequested += id => unequipped = id;
            BagItemInfo shared = null;
            window.EquipmentShareRequested += item => shared = item;
            Click(root, "EquipShare");
            Require(shared != null && shared.ItemId == 5001 && !ReferenceEquals(shared, bag.Items[1]),
                "Share must delegate a detached equipment snapshot without sending a message.");
            Click(root, "EquipAction");
            Require(equipped == 5001, "Equip must delegate the selected item instance.");
            Shoot("01-bag-equipment-detail");

            Click(root, "BagCategory_3"); // 已穿戴 is the native equipment view.
            Click(root, "EquipSlot_6");
            Require(Find<TMP_Text>(root, "TooltipState").text == "已穿戴", "Equipment slot must show worn state.");
            Click(root, "EquipAction");
            Require(unequipped == 7001, "Unequip must delegate the selected worn instance.");
            Shoot("02-worn-equipment-detail");

            Click(root, "EquipMore");
            Require(window.HasEquipmentModal && Find<RectTransform>(root, "EquipActionsDialog").gameObject.activeInHierarchy,
                "More must open the production actions dialog.");
            Shoot("03-equipment-more");
            Click(root, "EquipOpenWish");
            Require(window.HasEquipmentModal && Find<RectTransform>(root, "EquipWishDialog").gameObject.activeInHierarchy,
                "Wish selection must open from the production actions dialog.");
            Require(!Find<Button>(root, "ConfirmWishSelection").interactable,
                "A missing server attribute pool must disable confirmation.");
            Shoot("04-wish-pool-unavailable");
            window.Back();
            Require(window.IsVisible && !window.HasEquipmentModal, "Back must close the modal before the bag window.");
            int attributeRequests = 0;
            window.AttributesRequested += () => ++attributeRequests;
            Click(root, "EquipMore");
            Click(root, "EquipViewAttributes");
            Require(attributeRequests == 1 && !window.HasEquipmentModal, "More must delegate attribute navigation and close its modal.");
            window.Hide();

            var characterRoot = QdaoUguiFactory.CreateRect("CharacterFixture", root, 0, 0, 2560, 1080);
            var ownerObject = new GameObject("InactiveEquipmentAttributeOwner");
            ownerObject.SetActive(false);
            ownerObject.transform.SetParent(root, false);
            var owner = ownerObject.AddComponent<AttributeUiRoot>();
            owner.enabled = false;
            Require(owner.Client == null, "Fixture owner must have no client.");
            var panel = new AttributePanel(owner, characterRoot);
            var data = CharacterFixture();
            panel.ApplyPanel(data);
            characterRoot.GetComponentsInChildren<RectTransform>(true).Single(item => item.name == "AttributeWindow").gameObject.SetActive(true);
            Require(Find<TMP_Text>(characterRoot, "Row0Value").text == "152(+12)", "Allocation rows must display equipment bonuses without adding them twice.");
            Require(Find<Button>(characterRoot, "EquipmentEntryPlate").interactable, "Character equipment entry must be available.");
            Shoot("05-character-equipment-bonus");
            Click(characterRoot, "CombatTabPlate");
            Require(Find<RectTransform>(characterRoot, "CombatPage").gameObject.activeInHierarchy, "Combat navigation must show the combat page.");
            Require(Find<TMP_Text>(characterRoot, "CombatValue_0").text == "8%", "Server percentage attributes must retain their units.");
            Require(Find<TMP_Text>(characterRoot, "CombatValue_5").text == "5", "Server scalar attributes must not gain a percent sign.");
            Require(characterRoot.GetComponentsInChildren<TMP_Text>().Count(label => label.name.StartsWith("CombatName_")) == data.Combat.Count,
                "Every supplied combat attribute must be rendered.");
            Shoot("06-character-combat-attributes");
            Require(data.Dimensions[0].Bonus == 12 && data.Dimensions[0].Allocated == 60,
                "Navigation and display must not mutate authoritative fixture data.");
            var combatTab = Find<Button>(characterRoot, "CombatTabPlate");
            panel.ResetSession();
            Require(!combatTab.gameObject.activeSelf, "Session reset must clear combat navigation.");
            Debug.Log("EQUIPMENT_INTEGRATION_VERIFIED|" + width + "x" + height + "|6 screenshots|" + OutputDirectory);
        }
        finally
        {
            RealtimeTween.EndEditorCapture();
            RenderTexture.active = previousTarget;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static T Find<T>(Component root, string name) where T : Component
        => root.GetComponentsInChildren<T>(true).Where(item => item.gameObject.activeInHierarchy).Single(item => item.name == name);
    private static void Click(Component root, string name)
    {
        var button = Find<Button>(root, name);
        Require(button.gameObject.activeInHierarchy && button.interactable, name + " must be clickable.");
        button.onClick.Invoke();
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static BagInfo BagFixture()
    {
        var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = 0, Capacity = 56, CanSort = true }, Currency = new CurrencyComp() };
        bag.Currency.Values.Add(new ulong[] { 128640, 280, 1680 });
        bag.Items.Add(new BagItemInfo { ItemId = 9001, ConfigId = 101, Name = "回春草", Count = 3, MaxStack = 99 });
        bag.Layout.Slots.Add(new BagSlotInfo { Slot = 0, ItemId = 9001, Width = 1, Height = 1 });
        var sword = new BagItemInfo { ItemId = 5001, ConfigId = 1105, Name = "惊雷剑", Count = 1, MaxStack = 1,
            EquipKind = 11, EquipLevel = 80, IconKey = "UI/Ugui/EquipmentV1/icon_sword", Description = "编辑器布局样例" };
        sword.BaseAttrs.Add(Line(1, "伤害", 0, 0, 1620));
        sword.Affixes.Add(Line(17, "所有技能上升", 1, 0, 5, 5));
        sword.Affixes.Add(Line(12, "物理必杀率", 1, 1, 8, 10, true));
        sword.Affixes.Add(Line(3, "力量", 1, 2, 15, 20));
        sword.Affixes.Add(Line(1, "伤害", 2, 0, 900, 1200));
        sword.Affixes.Add(Line(2, "准确", 3, 0, 1200, 1600));
        bag.Items.Add(sword);
        bag.Layout.Slots.Add(new BagSlotInfo { Slot = 1, ItemId = 5001, Width = 1, Height = 1 });
        return bag;
    }

    private static BagInfo EquipmentFixture()
    {
        var equipment = new BagInfo { Layout = new BagLayoutInfo { BagType = 2, Capacity = 7 } };
        string[] names = { "武器", "帽子", "衣服", "鞋子" };
        for (uint i = 0; i < 4; i++) equipment.Layout.EquipSlots.Add(new EquipSlotInfo { Slot = i + 3, EquipKind = i + 11, Name = names[i] });
        var boots = new BagItemInfo { ItemId = 7001, ConfigId = 1405, Name = "惊雷靴", Count = 1, MaxStack = 1,
            EquipKind = 14, EquipLevel = 80, IconKey = "UI/Ugui/EquipmentV1/icon_boots", Description = "编辑器布局样例" };
        boots.BaseAttrs.Add(Line(8, "防御", 0, 0, 486));
        boots.BaseAttrs.Add(Line(11, "速度", 0, 1, 328));
        boots.Affixes.Add(Line(6, "敏捷", 1, 0, 15, 20));
        boots.Affixes.Add(Line(7, "所有属性", 1, 1, 12, 16));
        boots.Affixes.Add(Line(26, "抗物理", 3, 0, 4, 5, true));
        equipment.Items.Add(boots);
        equipment.Layout.Slots.Add(new BagSlotInfo { Slot = 6, ItemId = 7001, Width = 1, Height = 1 });
        return equipment;
    }

    private static EquipAttrLineInfo Line(uint id, string name, uint tier, uint seq, ulong value, ulong cap = 0, bool percent = false)
        => new EquipAttrLineInfo { AttrId = id, Name = name, Tier = tier, Seq = seq, Value = value, Cap = cap, Percent = percent };

    private static AttributePanelInfo CharacterFixture()
    {
        var data = new AttributePanelInfo { ActiveSchemeId = 1, MaxSchemes = 3, Level = 80, CreateSchemeCostGold = 100000 };
        data.Schemes.Add(new AttributeSchemeInfo { SchemeId = 1, Name = "方案一" });
        data.Pools.Add(new AttributePoolInfo { PoolId = 1, Name = "属性点", Total = 320, Remaining = 10, Unlocked = true, DimensionCap = 320 });
        string[] names = { "体质", "灵力", "力量", "敏捷" };
        for (uint i = 0; i < 4; i++) data.Dimensions.Add(new AttributeDimensionInfo { DimensionId = 101 + i, PoolId = 1,
            Name = names[i], Desc = "编辑器样例：装备加成由服务器提供", Value = 152 + i * 15, Allocated = 60, Bonus = 12 + i * 15, Cap = 320, Sort = i });
        data.Derived = new DerivedAttributeInfo { Health = 5800, MaxHealth = 5800, Mana = 2300, MaxMana = 2300,
            PhysicalAttack = 1380, MagicAttack = 2180, Speed = 860, Defense = 960 };
        string[] combatNames = { "物理必杀率", "法术必杀率", "连击率", "反击率", "反震率", "所有技能上升", "忽视所有抗异常",
            "抗中毒", "抗冰冻", "抗昏睡", "抗遗忘", "抗混乱", "所有抗异常", "法术抗性", "物理抗性" };
        for (uint i = 0; i < combatNames.Length; i++) data.Combat.Add(new CombatAttributeInfo { CombatId = i + 1,
            Name = combatNames[i], Value = i == 5 ? 5UL : 8 + i, Percent = i != 5, Sort = i });
        return data;
    }
}
#endif
