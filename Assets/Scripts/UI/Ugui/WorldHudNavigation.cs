using System.Collections.Generic;
using MmorpgClient.UI.Ugui.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MmorpgClient.UI.Ugui
{
    public enum WorldHudCategory { Cultivation, Adventure, Social, Trade }
    public enum WorldHudEntry
    {
        Role, Pet, Bag, Missions, Activities, Battle, Spectate, Rank,
        Team, Guild, Social, Mail, Trade, Map, Channel, Skills
    }

    /// <summary>
    /// Organizes the existing city entry buttons without taking ownership of their
    /// session state or actions. Slots stay under their original HUD canvas; a
    /// CanvasGroup conceals them without changing the owner's active/interactable state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldHudNavigation : MonoBehaviour
    {
        private sealed class Slot
        {
            public WorldHudEntry Id;
            public RectTransform Source, Container;
            public CanvasGroup Visibility;
            public Button Button;
            public TMP_Text Label;
            public bool Available => Source != null && Source.gameObject.activeInHierarchy;
        }

        private const float PanelX = 1420f, PanelY = 188f, PanelWidth = 700f;
        private const float TileWidth = 204f, TileHeight = 148f;
        private const string NewArt = "UI/Ugui/WorldHud20261021/";
        private static readonly string[] CategoryNames = { "修行", "历练", "仙友", "交易" };
        private static readonly string[] CategoryDescriptions =
        {
            "角色养成与灵宠相伴", "任务、活动与论剑切磋", "结伴同游，仙笺传情", "寄售寻宝，互通有无"
        };
        private static readonly string[] CategoryIcons =
        {
            "UI/Ugui/RefreshV8/round_badge_taiji", "UI/Ugui/RefreshV8/round_badge_sword",
            NewArt + "team", NewArt + "trade"
        };
        private readonly List<Slot> _slots = new();
        private readonly Button[] _tabs = new Button[4];
        private readonly Image[] _tabPlates = new Image[4];
        private readonly bool[] _categoryAvailable = new bool[4];
        private GameObject _socialNotice;
        private RectTransform _chrome, _panel;
        private GameObject _dismiss;
        private TMP_Text _title, _description;
        private Slot _skills;

        public static WorldHudNavigation Instance { get; private set; }
        public RectTransform Root { get; private set; }
        public WorldHudCategory? CurrentCategory { get; private set; }

        public static void Register(WorldHudEntry id, RectTransform rect, Button button, TMP_Text label)
        {
            if (rect == null || button == null || label == null) return;
            if (Instance == null)
            {
                var owner = new GameObject("[WorldHudNavigation]");
                if (Application.isPlaying) DontDestroyOnLoad(owner);
                var navigation = owner.AddComponent<WorldHudNavigation>();
                // Editor capture creates the same view without a running player loop.
                if (Instance == null)
                {
                    Instance = navigation;
                    navigation.Build();
                }
            }
            Instance.AddSlot(id, rect, button, label);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
                return;
            }
            Instance = this;
            Build();
        }

        private void Build()
        {
            var canvasObject = new GameObject("WorldHudCanvas", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Backplates below the independent source HUDs (140), all below windows (160+).
            canvas.sortingOrder = 139;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2560, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            Root = QdaoUguiFactory.CreateCenteredRect("WorldHudDesign", canvasObject.transform, 2560, 1080);
            _chrome = QdaoUguiFactory.CreateStretch("Navigation", Root, Vector4.zero);

            var dismiss = QdaoUguiFactory.CreateHitButton("CloseCategoriesOutside", _chrome, 0, 0, 2560, 1080);
            dismiss.onClick.AddListener(() => ShowCategory(null));
            _dismiss = dismiss.gameObject;

            _panel = QdaoUguiFactory.CreateRect("SystemCategoryPanel", _chrome,
                PanelX, PanelY, PanelWidth, 424);
            var paper = _panel.gameObject.AddComponent<Image>();
            paper.sprite = QdaoUguiTheme.RequireSprite("UI/Ugui/TeamV2/main_frame");
            paper.type = Image.Type.Sliced;
            paper.pixelsPerUnitMultiplier = 2f;
            paper.raycastTarget = true;
            var titlePlate = QdaoUguiFactory.CreateImage("CategoryTitlePlate", _panel, 24, 18, 590, 58,
                QdaoUguiTheme.RequireSprite("UI/Ugui/TeamV2/section_plate"));
            titlePlate.type = Image.Type.Sliced;
            titlePlate.pixelsPerUnitMultiplier = 2f;
            _title = QdaoUguiFactory.CreateText("CategoryTitle", _panel, 84, 26, 490, 36,
                "修行", 31, QdaoUguiTheme.Cream);
            _title.fontStyle = FontStyles.Bold;
            _description = QdaoUguiFactory.CreateText("CategoryDescription", _panel, 30, 80, 570, 30,
                CategoryDescriptions[0], 23, QdaoUguiTheme.MutedBrown);
            var close = QdaoUguiFactory.CreateArtButton("CloseCategory", _panel, 626, 22, 48, 48,
                QdaoUguiTheme.RequireSprite("UI/Ugui/TeamV2/close"), out var closeImage);
            closeImage.preserveAspect = true;
            close.onClick.AddListener(() => ShowCategory(null));
            close.gameObject.name = "CloseCategory_收起";

            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var tab = QdaoUguiFactory.CreateHitButton("WorldHudCategory_" + (WorldHudCategory)i,
                    _chrome, 1960 + i * 138, 30, 132, 140);
                var icon = QdaoUguiFactory.CreateImage("CategoryIcon", tab.transform, 22, 0, 88, 88,
                    LoadIcon(CategoryIcons[i], "UI/Ugui/RefreshV8/round_badge_taiji"));
                icon.preserveAspect = true;
                var plate = QdaoUguiFactory.CreateImage("CategoryPlaque", tab.transform, 0, 90, 132, 42,
                    QdaoUguiTheme.RequireSprite("UI/Ugui/CoreFidelity20261016/hud_button"));
                plate.preserveAspect = true;
                var name = QdaoUguiFactory.CreateText("CategoryName", tab.transform, 0, 90, 132, 42,
                    CategoryNames[i], 29, QdaoUguiTheme.Cream, TextAlignmentOptions.Center);
                name.fontStyle = FontStyles.Bold;
                if (i == (int)WorldHudCategory.Social)
                {
                    var notice = QdaoUguiFactory.CreateImage("PendingMailOrTeamNotice", tab.transform,
                        96, 0, 26, 26, QdaoUguiTheme.RequireSprite("UI/Ugui/RefreshV8/status_dot_orange"));
                    notice.preserveAspect = true;
                    _socialNotice = notice.gameObject;
                }
                tab.targetGraphic = icon;
                ConfigureFeedback(tab);
                tab.onClick.AddListener(() =>
                    ShowCategory(CurrentCategory == (WorldHudCategory)index ? null : (WorldHudCategory)index));
                _tabs[i] = tab;
                _tabPlates[i] = plate;
            }

            // A direct shortcut to the existing skill book, alongside role and pet.
            var skills = QdaoUguiFactory.CreateHitButton("SkillsEntry", Root, 0, 0, TileWidth, TileHeight);
            var skillLabel = QdaoUguiFactory.CreateText("SkillsLabel", skills.transform, 0, 0,
                TileWidth, 36, "技能", 28, QdaoCoreFidelityArt.Ink, TextAlignmentOptions.Center);
            skills.onClick.AddListener(Skills.SkillUiRoot.Open);
            _skills = CreateSlot(WorldHudEntry.Skills, (RectTransform)skills.transform, skills, skillLabel);
            skills.onClick.AddListener(() => ShowCategory(null));
            RefreshPresentation();
        }

        private void AddSlot(WorldHudEntry id, RectTransform rect, Button button, TMP_Text label)
        {
            // A root may rebuild its canvas after a scene/session change.
            _slots.RemoveAll(slot => slot.Source == null);
            foreach (var slot in _slots)
                if (slot.Source == rect) return;
            _slots.Add(CreateSlot(id, rect, button, label));
            button.onClick.AddListener(() => ShowCategory(null));
            RefreshPresentation();
        }

        private Slot CreateSlot(WorldHudEntry id, RectTransform rect, Button button, TMP_Text label)
        {
            // Keep the source HUD as an ancestor: its availability remains authoritative.
            var container = QdaoUguiFactory.CreateRect("WorldHudSlot_" + id, rect.parent,
                0, 0, TileWidth, TileHeight);
            var group = container.gameObject.AddComponent<CanvasGroup>();
            rect.SetParent(container, false);
            Place(rect, 0, 0, TileWidth, TileHeight);
            var buttonRect = (RectTransform)button.transform;
            if (buttonRect != rect) Place(buttonRect, 0, 0, TileWidth, TileHeight);
            var hit = button.GetComponent<Image>();
            // The approved 04 HUD defines these three persistent text buttons.
            // Preserve their real listeners/session state; extra systems remain in the category overlay.
            if (IsReferenceEntry(id))
            {
                Place(rect, 0, 0, BattleUiStyle.HudEntryWidth, BattleUiStyle.HudEntryHeight);
                if(buttonRect!=rect)Place(buttonRect,0,0,BattleUiStyle.HudEntryWidth,BattleUiStyle.HudEntryHeight);
                if(hit!=null)
                {
                    hit.sprite=QdaoUguiTheme.RequireSprite("UI/Ugui/CoreFidelity20261016/hud_button");
                    hit.type=Image.Type.Simple;hit.preserveAspect=false;hit.color=Color.white;
                }
                Place(label.rectTransform,32,0,BattleUiStyle.HudEntryWidth-64,BattleUiStyle.HudEntryHeight);
                QdaoUguiTypography.ApplyButton(label);
                label.fontSize=BattleUiStyle.HudEntryFontSize;label.color=QdaoUguiTheme.Html("#FFF7DE");
                label.enableAutoSizing=true;label.fontSizeMin=21;label.fontSizeMax=BattleUiStyle.HudEntryFontSize;
                label.alignment=TextAlignmentOptions.Center;label.raycastTarget=false;
                button.targetGraphic=hit;ConfigureFeedback(button);
                return new Slot { Id=id,Source=rect,Container=container,Button=button,Label=label,Visibility=group };
            }
            // Older feature entries may already contain a decorative icon.
            // Replace its drawing, keeping their labels and action objects intact.
            foreach (var decoration in button.GetComponentsInChildren<Image>(true))
                if (decoration != hit) decoration.enabled = false;
            if (hit != null)
            {
                hit.sprite = null;
                hit.type = Image.Type.Simple;
                hit.color = new Color(1, 1, 1, .001f);
                hit.raycastTarget = true;
            }
            var icon = QdaoUguiFactory.CreateImage("SystemIcon_" + id, button.transform,
                (TileWidth - 86) * .5f, 4, 86, 86, EntryIcon(id));
            icon.preserveAspect = true;
            label.transform.SetAsLastSibling();
            Place(label.rectTransform, 4, 94, TileWidth - 8, 42);
            label.fontSize = 28;
            label.enableAutoSizing = true;
            label.fontSizeMin = 21;
            label.fontSizeMax = 28;
            label.fontStyle = FontStyles.Bold;
            label.color = QdaoCoreFidelityArt.Ink;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            button.targetGraphic = icon;
            ConfigureFeedback(button);
            // Quick entries float over the world: jade plaques keep their names readable.
            if (IsQuick(id))
            {
                var plate = QdaoUguiFactory.CreateImage("QuickEntryPlaque", button.transform,
                    0, 94, TileWidth, 42,
                    QdaoUguiTheme.RequireSprite("UI/Ugui/CoreFidelity20261016/hud_button"));
                plate.preserveAspect = true;
                label.transform.SetAsLastSibling();
                label.color = QdaoUguiTheme.Cream;
            }
            return new Slot { Id = id, Source = rect, Container = container,
                Button = button, Label = label, Visibility = group };
        }

        private static void ConfigureFeedback(Button button)
        {
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1, .94f, .78f, 1);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(.68f, .82f, .72f, 1);
            colors.disabledColor = new Color(.6f, .6f, .6f, .65f);
            colors.fadeDuration = .1f;
            button.colors = colors;
        }

        public void ShowCategory(WorldHudCategory? category)
        {
            CurrentCategory = category;
            RefreshPresentation();
        }

        private void LateUpdate()
        {
#if ENABLE_INPUT_SYSTEM
            if (CurrentCategory.HasValue && Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                CurrentCategory = null;
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (CurrentCategory.HasValue && Input.GetKeyDown(KeyCode.Escape)) CurrentCategory = null;
#endif
            RefreshPresentation();
        }

        public void RefreshPresentation()
        {
            if (_chrome == null) return;
            _slots.RemoveAll(slot => slot.Source == null);
            bool available = false, roleAvailable = false;
            System.Array.Clear(_categoryAvailable, 0, _categoryAvailable.Length);
            bool socialNotice = false;
            foreach (var slot in _slots)
            {
                if (!slot.Available) continue;
                available = true;
                if (slot.Id == WorldHudEntry.Role) roleAvailable = true;
                if (!IsQuick(slot.Id)) _categoryAvailable[(int)Category(slot.Id)] = true;
                if ((slot.Id == WorldHudEntry.Team || slot.Id == WorldHudEntry.Mail) &&
                    slot.Label != null && slot.Label.text.Contains("·")) socialNotice = true;
            }
            if(roleAvailable)_categoryAvailable[(int)WorldHudCategory.Cultivation]=true;
            if (!available || (CurrentCategory.HasValue && !_categoryAvailable[(int)CurrentCategory.Value]))
                CurrentCategory = null;
            _chrome.gameObject.SetActive(available);
            _dismiss.SetActive(available && CurrentCategory.HasValue);
            _panel.gameObject.SetActive(available && CurrentCategory.HasValue);
            for (int i = 0; i < 4; i++)
            {
                _tabs[i].interactable = _categoryAvailable[i];
                _tabPlates[i].color = CurrentCategory == (WorldHudCategory)i
                    ? new Color(1, .86f, .55f) : Color.white;
            }
            _socialNotice.SetActive(socialNotice);
            int position = 0;
            // Stable explicit order, independent of RuntimeInitializeOnLoad execution order.
            for (int order = 0; order < 16; order++)
            {
                foreach (var slot in _slots)
                {
                    if ((int)slot.Id != order) continue;
                    bool show = slot.Available && (IsQuick(slot.Id) || CurrentCategory == Category(slot.Id));
                    SetShown(slot, show);
                    if (IsQuick(slot.Id)) LayoutQuick(slot);
                    else if (show) LayoutTile(slot, position++);
                }
            }
            bool skillsShown = roleAvailable && CurrentCategory == WorldHudCategory.Cultivation;
            SetShown(_skills, skillsShown);
            if (skillsShown) LayoutTile(_skills, position++);
            if (CurrentCategory.HasValue)
            {
                _title.text = CategoryNames[(int)CurrentCategory.Value];
                _description.text = CategoryDescriptions[(int)CurrentCategory.Value];
                _panel.sizeDelta = new Vector2(PanelWidth, position > 3 ? 450 : 294);
            }
        }

        private static void SetShown(Slot slot, bool visible)
        {
            if (slot == null) return;
            slot.Visibility.alpha = visible ? 1f : 0f;
            slot.Visibility.interactable = visible;
            slot.Visibility.blocksRaycasts = visible;
        }

        private static void LayoutTile(Slot slot, int index)
            => Place(slot.Container, PanelX + 32 + index % 3 * 216,
                PanelY + 126 + index / 3 * 156, TileWidth, TileHeight);

        private static void LayoutQuick(Slot slot)
        {
            switch (slot.Id)
            {
                case WorldHudEntry.Battle: Place(slot.Container,2160,176,336,81.6f); break;
                case WorldHudEntry.Spectate: Place(slot.Container,2160,280,336,81.6f); break;
                case WorldHudEntry.Role: Place(slot.Container,2160,384,336,81.6f); break;
                case WorldHudEntry.Map: Place(slot.Container, 36, 28, TileWidth, TileHeight); break;
                case WorldHudEntry.Channel: Place(slot.Container, 252, 28, TileWidth, TileHeight); break;
                case WorldHudEntry.Bag: Place(slot.Container, 2308, 880, TileWidth, TileHeight); break;
            }
        }

        private static bool IsQuick(WorldHudEntry id)
            => IsReferenceEntry(id) || id == WorldHudEntry.Bag || id == WorldHudEntry.Map || id == WorldHudEntry.Channel;

        private static bool IsReferenceEntry(WorldHudEntry id)
            => id == WorldHudEntry.Battle || id == WorldHudEntry.Spectate || id == WorldHudEntry.Role;

        private static WorldHudCategory Category(WorldHudEntry id) => id switch
        {
            WorldHudEntry.Role or WorldHudEntry.Pet or WorldHudEntry.Skills => WorldHudCategory.Cultivation,
            WorldHudEntry.Team or WorldHudEntry.Guild or WorldHudEntry.Social or WorldHudEntry.Mail => WorldHudCategory.Social,
            WorldHudEntry.Trade => WorldHudCategory.Trade,
            _ => WorldHudCategory.Adventure
        };

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = UnityEngine.Vector3.one;
        }

        private static Sprite LoadIcon(string path, string fallback)
            => Resources.Load<Sprite>(path) ?? QdaoUguiTheme.RequireSprite(fallback);

        private static Sprite EntryIcon(WorldHudEntry id)
        {
            var path = id switch
            {
                WorldHudEntry.Role => "UI/Ugui/RefreshV8/round_badge_taiji",
                WorldHudEntry.Pet => NewArt + "pet",
                WorldHudEntry.Bag => NewArt + "bag",
                WorldHudEntry.Missions => "UI/Ugui/MailV1/icon-talisman",
                WorldHudEntry.Activities => "UI/Ugui/GameplayV1/icon_lantern",
                WorldHudEntry.Battle => "UI/Ugui/RefreshV8/round_badge_sword",
                WorldHudEntry.Spectate => NewArt + "spectate",
                WorldHudEntry.Rank => NewArt + "rank",
                WorldHudEntry.Team => NewArt + "team",
                WorldHudEntry.Guild => "UI/Ugui/GuildV2/round_badge_pagoda",
                WorldHudEntry.Social => NewArt + "social",
                WorldHudEntry.Mail => "UI/Ugui/MailV1Fidelity/mail_envelope_read",
                WorldHudEntry.Trade => NewArt + "trade",
                WorldHudEntry.Map => "UI/Ugui/RefreshV8/round_badge_compass",
                WorldHudEntry.Channel => NewArt + "channel",
                _ => "UI/Ugui/MailV1/icon-scroll"
            };
            return LoadIcon(path, "UI/Ugui/RefreshV8/round_badge_taiji");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
