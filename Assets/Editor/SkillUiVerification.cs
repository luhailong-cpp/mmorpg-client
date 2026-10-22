#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Skills;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Editor-only fixtures for the real skill panel. Never binds a game client or player.</summary>
public static class SkillUiVerification
{
    public static string OutputDirectory { get; set; } = Path.GetFullPath(Path.Combine(Application.dataPath,
        "../Docs/VerificationEvidence/skills-ui-20261009"));

    [MenuItem("MMORPG/UI/技能界面/打开交互预览")]
    public static void OpenPreview()
    {
        RequireEditMode();
        SkillUiPreviewWindow.Open();
    }

    [MenuItem("MMORPG/UI/技能界面/导出验收截图")]
    public static void CaptureAll()
    {
        RequireEditMode();
        Directory.CreateDirectory(OutputDirectory);
        var shots = new List<string>();
        int checks = 0;
        var activeScene = SceneManager.GetActiveScene();
        var sourceRoot = SkillUiRoot.Instance;
        foreach (int width in new[] { 1920, 2560 })
        using (var preview = new SkillUiPreviewSession(width, 1080))
        {
            foreach (SkillUiFixture fixture in Enum.GetValues(typeof(SkillUiFixture)))
            {
                preview.Load(fixture);
                checks += preview.VerifyActions();
                preview.Render(validate: true);
                string filename = fixture.ToString().ToLowerInvariant() + "-" + width + "x1080.png";
                preview.Save(Path.Combine(OutputDirectory, filename));
                shots.Add(filename);
            }
            checks += preview.LoadBattle();
            preview.Render(validate: true);
            string battleName = "battle-skills-" + width + "x1080.png";
            preview.Save(Path.Combine(OutputDirectory, battleName));
            shots.Add(battleName);
            preview.ScrollBattleToBottom();
            preview.Render(validate: true);
            battleName = "battle-skills-scrolled-" + width + "x1080.png";
            preview.Save(Path.Combine(OutputDirectory, battleName));
            shots.Add(battleName);
        }
        if (SceneManager.GetActiveScene() != activeScene || SkillUiRoot.Instance != sourceRoot)
            throw new InvalidOperationException("技能预览改变了当前场景或生产技能单例。");
        checks += 2;
        File.WriteAllText(Path.Combine(OutputDirectory, "native-capture.json"), JsonUtility.ToJson(
            new CaptureReport { completedAtUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                screenshots = shots.ToArray(), assertionCount = checks }, true));
        Debug.Log("SKILL_UI_CAPTURE_OK|screenshots=" + shots.Count + "|checks=" + checks + "|" + OutputDirectory);
    }

    internal static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("请在编译完成后的 Edit Mode 打开技能界面预览。");
    }

    [Serializable]
    private sealed class CaptureReport
    {
        public string status = "passed", completedAtUtc, unityVersion;
        public bool layoutPreviewOnly = true, liveServerVerified = false, battleEffectsVerified = false;
        public string note = "真实 SkillPanel，Editor-only 示例数据；未连接账号、网络、玩家存档或技能效果。";
        public string[] screenshots;
        public int assertionCount;
    }
}

internal enum SkillUiFixture
{
    Class1, Class2, Class3, Class4, Obstacles, Pet, Loading, Empty, Error, InsufficientCoins, MaxRank, Busy, Disconnected,
}

internal sealed class SkillUiFixtureSource : ISkillBookSource, IDisposable
{
    internal static readonly string[] Labels =
    {
        "角色 · 职业一", "角色 · 职业二", "角色 · 职业三", "角色 · 职业四", "障碍 · 五项", "宠物 · 两项",
        "加载中", "空列表", "错误提示", "金币不足", "满级", "请求处理中", "未连接",
    };
    public event Action Changed;
    public SkillBookViewState State { get; private set; }
    internal SkillUiFixture Fixture { get; private set; }
    internal int UpgradeRequests { get; private set; }
    internal uint LastId, LastRank, LastCount;

    internal void Load(SkillUiFixture fixture)
    {
        Fixture = fixture;
        uint classId = fixture >= SkillUiFixture.Class1 && fixture <= SkillUiFixture.Class4 ? (uint)fixture + 1 : 1;
        var context = fixture == SkillUiFixture.Pet ? SkillBookContext.Pet(900001, "灵宠示例") : SkillBookContext.Character();
        var snapshot = new SkillBookSnapshot { ClassId = classId, PlayerLevel = 68, CurrencyBalance = 265800 };
        foreach (var definition in SkillCatalog.ForClass(classId))
            snapshot.Entries.Add(new SkillEntryState { Id = definition.skillId, Definition = definition, Rank = 3,
                RankLimit = definition.maxRank, NextRankCost = 1600 });
        if (fixture == SkillUiFixture.Obstacles)
        {
            snapshot.Entries.Clear();
            string[] names = { "狼猛蜂毒", "蛇口蜂针", "鹤顶红粉", "蝎尾蛇涎", "万蚁噬心" };
            for (int i = 0; i < names.Length; i++)
                snapshot.Entries.Add(Sample(1301u + (uint)i, names[i], "障碍", "角色技能", i == 4 ? 0u : 40u,
                    "对敌方使用，使目标进入中毒状态。此处仅展示技能说明排版；实际成功率、持续回合、伤害和限制由逻辑接入后提供。"));
        }
        if (fixture == SkillUiFixture.Pet)
        {
            snapshot.Entries.Clear();
            snapshot.Entries.Add(Sample(1203, "如意圈", "辅助", "天生技能", 1,
                "对己方使用成功后，可以使被施法成员吸收对方的法术攻击。此处仅用于布局预览，不产生吸收效果。"));
            snapshot.Entries.Add(Sample(1402, "死亡缠绵", "辅助", "天生技能", 1,
                "对己方使用，可以使被施法者在使用普通攻击时以二连击的方式攻击目标。此处仅用于布局预览，不产生连击效果。"));
        }
        if (fixture == SkillUiFixture.Empty) snapshot.Entries.Clear();
        if (fixture == SkillUiFixture.InsufficientCoins) snapshot.CurrencyBalance = 0;
        if (fixture == SkillUiFixture.MaxRank)
            foreach (var entry in snapshot.Entries) entry.Rank = entry.RankLimit;
        State = new SkillBookViewState
        {
            Context = context, Snapshot = fixture == SkillUiFixture.Loading ? null : snapshot,
            IsConnected = fixture != SkillUiFixture.Disconnected,
            IsLoading = fixture == SkillUiFixture.Loading, IsBusy = fixture == SkillUiFixture.Busy,
            Message = "布局预览 · 示例数值 · 不连接游戏逻辑",
            Error = fixture == SkillUiFixture.Error ? "布局预览：技能资料读取失败，请点击刷新重试。" : null,
        };
        Changed?.Invoke();
    }

    private static SkillEntryState Sample(uint id, string name, string category, string origin, uint rank, string description)
    {
        return new SkillEntryState
        {
            Id = id, Rank = rank, RankLimit = 88, NextRankCost = 1600,
            Definition = new SkillDefinition { id = id, classId = 1, name = name, type = "active", category = category,
                originLabel = origin, maxRank = 88, unlockLevel = 1, targetTeam = category == "障碍" ? "enemy" : "ally", maxTargets = 1,
                manaPercent = 0.03, description = description },
        };
    }

    public void Refresh(SkillBookContext context) => Load(Fixture);

    public void Upgrade(SkillBookContext context, uint skillId, uint expectedRank, uint count)
    {
        UpgradeRequests++;
        LastId = skillId; LastRank = expectedRank; LastCount = count;
        State.Message = "布局预览：已收到技能 " + skillId + "、等级 " + expectedRank + "、次数 " + count +
            " 的操作；数值保持不变，等待游戏逻辑接入。";
        Changed?.Invoke();
    }

    public void Dispose() { Changed = null; State = null; }
}

/// <summary>No SkillUiRoot, EventSystem, active scene switch, or game-client singleton is created.</summary>
internal sealed class SkillUiPreviewSession : IDisposable
{
    internal readonly SkillUiFixtureSource Source = new SkillUiFixtureSource();
    internal RenderTexture Texture { get; private set; }
    private Scene _scene;
    private Camera _camera;
    private GameObject _canvasObject;
    private SkillPanel _panel;
    private TMP_Text _badge;
    private Button _pressed;
    private Scrollbar _draggedScrollbar;
    private RectTransform _design;
    private ScrollRect _battleScroll;
    private BattleScreen _battle;
    private readonly int _width, _height;

    internal SkillUiPreviewSession(int width, int height)
    {
        _width = width; _height = height;
        _scene = EditorSceneManager.NewPreviewScene();
        try
        {
            _camera = new GameObject("SkillLayoutPreviewCamera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(_camera.gameObject, _scene);
            _camera.scene = _scene;
            _camera.enabled = false;
            _camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
            _camera.orthographic = true;
            _camera.nearClipPlane = .1f; _camera.farClipPlane = 100;
            _camera.cullingMask = 1 << 31;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = QdaoUguiTheme.Letterbox;
            Texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture.Create(); _camera.targetTexture = Texture;
            _canvasObject = new GameObject("SkillLayoutPreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            SceneManager.MoveGameObjectToScene(_canvasObject, _scene);
            var canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = _camera; canvas.planeDistance = 2;
            var scaler = _canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var root = QdaoUguiFactory.CreateCenteredRect("SkillLayoutPreviewRoot", _canvasObject.transform, 2560, 1080);
            _design = root;
            QdaoUguiFactory.CreateImage("Backdrop", root, 0, 0, 2560, 1080, QdaoRefreshArt.Load("sanctuary_background"));
            _panel = new SkillPanel(root, () => Source.Refresh(Source.State.Context),
                (id, rank, count) => Source.Upgrade(Source.State.Context, id, rank, count),
                classId => Load((SkillUiFixture)Mathf.Clamp((int)classId - 1, 0, 3)), () => _panel.Hide());
            _badge = QdaoUguiFactory.CreateText("LayoutPreviewBadge", root, 300, 1024, 1960, 42,
                "布局预览 · 示例数据 · 不连接游戏逻辑", 23, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
            Source.Changed += Apply;
        }
        catch { Dispose(); throw; }
    }

    internal void Load(SkillUiFixture fixture) { Source.Load(fixture); _panel.Show(); }
    internal int VerifyActions()
    {
        int checks = 0;
        void Require(bool pass, string message)
        {
            if (!pass) throw new InvalidOperationException(Source.Fixture + ": " + message);
            checks++;
        }
        Button Find(string name) => _canvasObject.GetComponentsInChildren<Button>(true).Single(button => button.name == name);
        var snapshot = Source.State.Snapshot;
        var entry = snapshot?.Entries.FirstOrDefault();
        bool canUpgrade = SkillPanel.CanUpgrade(Source.State, entry);
        foreach (var item in new[] { (Name: "UpgradePlate", Count: 1u), (Name: "UpgradeTenPlate", Count: 10u) })
        {
            var button = Find(item.Name);
            Require(button.IsInteractable() == canUpgrade, item.Name + " availability");
            if (!canUpgrade) continue;
            uint rank = entry.Rank;
            ulong balance = snapshot.CurrencyBalance;
            int before = Source.UpgradeRequests;
            button.onClick.Invoke();
            Require(Source.UpgradeRequests == before + 1 && Source.LastId == entry.Id &&
                Source.LastRank == rank && Source.LastCount == item.Count, item.Name + " request parameters");
            Require(entry.Rank == rank && snapshot.CurrencyBalance == balance, "UI must not mutate rank or coins");
        }
        if (snapshot != null && Source.Fixture >= SkillUiFixture.Class1 && Source.Fixture <= SkillUiFixture.Class4)
        {
            Find("Filter2Plate").onClick.Invoke();
            int visible = _canvasObject.GetComponentsInChildren<Button>().Count(button => button.name.StartsWith("SkillRow", StringComparison.Ordinal));
            Require(visible == snapshot.Entries.Count(item => item.Definition.IsPassive), "passive filter row count");
            Find("Filter0Plate").onClick.Invoke();
        }
        Load(Source.Fixture);
        return checks;
    }

    internal int LoadBattle()
    {
        _panel.Hide();
        var battle = new BattleScreen(null, _design);
        _battle = battle;
        var actor = new BattleActorState { ActorId = 1, Name = "布局预览", TeamIndex = 1 };
        actor.SkillTableIds.Add(new uint[] { 1101, 1102, 1103, 1104, 1105, 1106, 1201, 1202, 1203, 1204, 1205, 99999 });
        actor.SkillCooldownRounds[1102] = 2;
        var state = new BattleStateS2C(); state.Actors.Add(actor);
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(BattleScreen).GetField("_state", fields).SetValue(battle, state);
        typeof(BattleScreen).GetField("_myId", fields).SetValue(battle, 1UL);
        typeof(BattleScreen).GetMethod("PopulateSkillPanel", fields).Invoke(battle, null);
        var panel = (RectTransform)typeof(BattleScreen).GetField("_skillPanel", fields).GetValue(battle);
        panel.SetParent(_design, false);
        panel.gameObject.SetActive(true);
        _battleScroll = panel.GetComponentInChildren<ScrollRect>();
        var buttons = panel.GetComponentsInChildren<Button>();
        bool Has(string name) => buttons.Any(button => button.name == name + "Plate");
        Button Find(string name) => buttons.Single(button => button.name == name + "Plate");
        int checks = 0;
        void Require(bool pass, string message)
        {
            if (!pass) throw new InvalidOperationException("Battle skill layout: " + message);
            checks++;
        }
        Require(Has("Skill_99999"), "unknown skill fallback must remain visible");
        Require(!Has("Skill_1106"), "passive skill must not be offered for casting");
        Require(!Find("Skill_1102").IsInteractable(), "cooldown skill must be disabled");
        Require(Find("Skill_1101").IsInteractable(), "ready skill must remain selectable");
        Require(buttons.Count(button => button.name.StartsWith("Skill_", StringComparison.Ordinal)) > 9,
            "fixture must include more than nine castable cards");
        Require(_battleScroll != null && _battleScroll.vertical &&
            _battleScroll.content.rect.height > _battleScroll.viewport.rect.height, "overflow must be scrollable");
        _badge.text = "布局预览 · 战斗选技 · 服务端列表样例 / 冷却 / 被动隐藏 / 未知技能 / 滚动";
        return checks;
    }
    internal void ScrollBattleToBottom() => _battleScroll.verticalNormalizedPosition = 0f;
    private void Apply()
    {
        _panel.Apply(Source.State);
        _badge.text = "布局预览 · " + SkillUiFixtureSource.Labels[(int)Source.Fixture] + " · 示例数据，不连接游戏逻辑";
    }

    internal void Render(bool validate = false)
    {
        foreach (var go in _scene.GetRootGameObjects())
            foreach (var child in go.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
        Canvas.ForceUpdateCanvases();
        foreach (var label in _canvasObject.GetComponentsInChildren<TMP_Text>(true))
        {
            label.ForceMeshUpdate(true, true);
            if (validate && label.isActiveAndEnabled && !string.IsNullOrWhiteSpace(label.text))
            {
                bool hasGlyph = false;
                for (int i = 0; i < label.textInfo.characterCount; i++) hasGlyph |= label.textInfo.characterInfo[i].isVisible;
                if (!hasGlyph) throw new InvalidOperationException("技能界面文本未显示：" + label.name + " / " + label.text);
                if (label.isTextTruncated) throw new InvalidOperationException("技能界面文本被截断：" + label.name + " / " + label.text);
            }
        }
        Canvas.ForceUpdateCanvases();
        _camera.Render();
    }

    internal void Pointer(Vector2 position, bool down)
    {
        if (down)
        {
            _draggedScrollbar = _canvasObject.GetComponentsInChildren<Scrollbar>().LastOrDefault(scrollbar =>
                scrollbar.IsInteractable() && Hit((RectTransform)scrollbar.transform, position));
            if (_draggedScrollbar != null) { Drag(position); return; }
        }
        else if (_draggedScrollbar != null) { Drag(position); _draggedScrollbar = null; return; }
        Button hit = null;
        var buttons = _canvasObject.GetComponentsInChildren<Button>();
        for (int i = buttons.Length - 1; i >= 0; i--)
            if (buttons[i].isActiveAndEnabled && buttons[i].IsInteractable() &&
                Hit((RectTransform)buttons[i].transform, position))
            { hit = buttons[i]; break; }
        if (down) _pressed = hit;
        else
        {
            var pressed = _pressed; _pressed = null;
            if (hit != null && hit == pressed) hit.onClick.Invoke();
        }
    }

    private bool Hit(RectTransform rect, Vector2 position)
    {
        if (!RectTransformUtility.RectangleContainsScreenPoint(rect, position, _camera)) return false;
        foreach (var mask in rect.GetComponentsInParent<RectMask2D>())
            if (!RectTransformUtility.RectangleContainsScreenPoint(mask.rectTransform, position, _camera)) return false;
        return true;
    }

    internal void Scroll(Vector2 position, float delta)
    {
        foreach (var scroll in _canvasObject.GetComponentsInChildren<ScrollRect>())
        {
            if (!scroll.isActiveAndEnabled || !Hit(scroll.viewport, position)) continue;
            float overflow = scroll.content.rect.height - scroll.viewport.rect.height;
            if (overflow > 0) scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition - delta * scroll.scrollSensitivity / overflow);
            break;
        }
    }

    internal void Drag(Vector2 position)
    {
        if (_draggedScrollbar == null) return;
        var rect = (RectTransform)_draggedScrollbar.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, position, _camera, out var local);
        var direction = _draggedScrollbar.direction;
        float value = direction == Scrollbar.Direction.BottomToTop || direction == Scrollbar.Direction.TopToBottom ?
            Mathf.InverseLerp(rect.rect.yMin, rect.rect.yMax, local.y) : Mathf.InverseLerp(rect.rect.xMin, rect.rect.xMax, local.x);
        if (direction == Scrollbar.Direction.TopToBottom || direction == Scrollbar.Direction.RightToLeft) value = 1 - value;
        _draggedScrollbar.value = value;
    }

    internal void Save(string path)
    {
        var previous = RenderTexture.active;
        var pixels = new Texture2D(_width, _height, TextureFormat.RGB24, false, false);
        try
        {
            RenderTexture.active = Texture;
            pixels.ReadPixels(new Rect(0, 0, _width, _height), 0, 0, false);
            pixels.Apply(false, false);
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(pixels); }
    }

    public void Dispose()
    {
        Source.Changed -= Apply;
        Source.Dispose();
        _battle?.Dispose(); _battle = null;
        _panel?.Hide(); _panel = null; _pressed = null; _draggedScrollbar = null;
        if (_camera != null) _camera.targetTexture = null;
        if (Texture != null) { Texture.Release(); UnityEngine.Object.DestroyImmediate(Texture); Texture = null; }
        if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
    }
}

public sealed class SkillUiPreviewWindow : EditorWindow
{
    private SkillUiPreviewSession _preview;
    private int _fixture, _resolution;
    private string _error;

    internal static void Open()
    {
        var window = GetWindow<SkillUiPreviewWindow>("技能界面 · 布局预览");
        window.minSize = new Vector2(900, 500);
        window.Show();
    }
    private void OnEnable() { EditorApplication.playModeStateChanged += PlayModeChanged; }
    private void OnDisable() { EditorApplication.playModeStateChanged -= PlayModeChanged; DisposePreview(); }
    private void PlayModeChanged(PlayModeStateChange state) { DisposePreview(); Repaint(); }
    private void DisposePreview() { _preview?.Dispose(); _preview = null; }
    private void Recreate()
    {
        DisposePreview();
        _preview = new SkillUiPreviewSession(_resolution == 0 ? 1920 : 2560, 1080);
        _preview.Load((SkillUiFixture)_fixture);
        _error = null;
    }
    private void OnGUI()
    {
        EditorGUILayout.HelpBox("布局预览：画面中的实际按钮可点击。所有角色、宠物、数值和操作回执都是编辑器示例，不连接游戏逻辑。", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            int fixture = EditorGUILayout.Popup(_fixture, SkillUiFixtureSource.Labels, EditorStyles.toolbarPopup, GUILayout.Width(170));
            int resolution = EditorGUILayout.Popup(_resolution, new[] { "1920 × 1080", "2560 × 1080" }, EditorStyles.toolbarPopup, GUILayout.Width(130));
            if (fixture != _fixture || resolution != _resolution)
            { _fixture = fixture; _resolution = resolution; DisposePreview(); }
            if (GUILayout.Button("重置样例", EditorStyles.toolbarButton, GUILayout.Width(80))) DisposePreview();
            if (GUILayout.Button("导出全部截图", EditorStyles.toolbarButton, GUILayout.Width(110)))
            {
                try { SkillUiVerification.CaptureAll(); _error = null; }
                catch (Exception error) { _error = error.Message; Debug.LogException(error); }
            }
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorGUILayout.HelpBox("请退出 Play Mode 后查看独立布局预览。", MessageType.Info); return; }
        if (_error != null) { EditorGUILayout.HelpBox(_error, MessageType.Error); return; }
        try
        {
            if (_preview == null) Recreate();
            int width = _resolution == 0 ? 1920 : 2560;
            var area = GUILayoutUtility.GetRect(1, 10000, 1, 10000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            float scale = Mathf.Min(area.width / width, area.height / 1080f);
            var rect = new Rect(area.x + (area.width - width * scale) * .5f,
                area.y + (area.height - 1080 * scale) * .5f, width * scale, 1080 * scale);
            var current = Event.current;
            if ((current.type == EventType.MouseDown || current.type == EventType.MouseUp) && current.button == 0 && rect.Contains(current.mousePosition))
            {
                _preview.Pointer(new Vector2((current.mousePosition.x - rect.x) / scale,
                    1080 - (current.mousePosition.y - rect.y) / scale), current.type == EventType.MouseDown);
                current.Use(); Repaint();
            }
            if ((current.type == EventType.ScrollWheel || current.type == EventType.MouseDrag) && rect.Contains(current.mousePosition))
            {
                var point = new Vector2((current.mousePosition.x - rect.x) / scale, 1080 - (current.mousePosition.y - rect.y) / scale);
                if (current.type == EventType.ScrollWheel) _preview.Scroll(point, current.delta.y);
                else _preview.Drag(point);
                current.Use(); Repaint();
            }
            if (current.type == EventType.Repaint) { _preview.Render(); GUI.DrawTexture(rect, _preview.Texture, ScaleMode.StretchToFill, false); }
        }
        catch (Exception error) { _error = error.Message; DisposePreview(); Debug.LogException(error); }
    }
}
#endif
