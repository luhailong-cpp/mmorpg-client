using System.Collections.Generic;
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
        Team, Guild, Social, Mail, Trade, Map, Channel, Skills, MysteryMerchant, TeamFollow
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
            public RectTransform Source, Container, Proxy, Clip;
            public CanvasGroup Visibility;
            public Button Button;
            public TMP_Text Label;
            public bool Available => Source != null && Source.gameObject.activeInHierarchy;
        }

        private const float PanelX = 1420f, PanelY = 188f, PanelWidth = 700f;
        private const float TileWidth = 306f, TileHeight = 108f;
        private const float CategoryFirstX = 1512f, CategoryY = 64f;
        public const float MerchantEntryWidth = 520f;
        // Reserve the status strip beside the category tabs in the same design space.
        public static Rect MerchantEntryBounds => new Rect(
            CategoryFirstX - MerchantEntryWidth - 32f, CategoryY, MerchantEntryWidth, 81.6f);
        private static readonly WorldHudEntry[] EntryOrder =
            (WorldHudEntry[])System.Enum.GetValues(typeof(WorldHudEntry));
        private const string NewArt = "UI/Ugui/WorldHud20261022/";
        private static readonly Color Ink = QdaoUguiTheme.Html("#244C3E");
        private static readonly Color SecondaryInk = QdaoUguiTheme.Html("#6B604A");
        private static readonly string[] CategoryNames = { "修行", "历练", "仙友", "交易" };
        private static readonly string[] CategoryDescriptions =
        {
            "灵宠相伴，参悟道法", "云游问道，历练四方", "结伴同游，仙笺传情", "寄售寻宝，互通有无"
        };
        private static readonly string[] CategoryIcons =
        {
            NewArt + "skills", NewArt + "missions", NewArt + "team", NewArt + "trade"
        };
        private readonly List<Slot> _slots = new();
        private readonly Button[] _tabs = new Button[4];
        private readonly Image[] _tabPlates = new Image[4];
        private readonly TMP_Text[] _tabNames = new TMP_Text[4];
        private readonly bool[] _categoryAvailable = new bool[4];
        private readonly Sprite[] _categorySprites = new Sprite[4];
        private Sprite _normalTab, _selectedTab;
        private GameObject _socialNotice;
        private RectTransform _chrome, _panel, _close;
        private GameObject _dismiss;
        private TMP_Text _title, _description;
        private Image _categoryIllustration;
        private GameObject _locationBoard;
        private Slot _skills;
        private WorldHudLayout _layout;

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
            Root = QdaoUguiFactory.CreateStretch("WorldHudDesign", canvasObject.transform, Vector4.zero);
            _chrome = QdaoUguiFactory.CreateStretch("Navigation", Root, Vector4.zero);
            _normalTab = QdaoUguiTheme.RequireSprite(NewArt + "chrome/tab-normal");
            _selectedTab = QdaoUguiTheme.RequireSprite(NewArt + "chrome/tab-selected");

            var dismiss = QdaoUguiFactory.CreateHitButton("CloseCategoriesOutside", _chrome, 0, 0, 2560, 1080);
            dismiss.onClick.AddListener(() => ShowCategory(null));
            _dismiss = dismiss.gameObject;
            var dismissRect = (RectTransform)dismiss.transform;
            dismissRect.anchorMin = Vector2.zero;
            dismissRect.anchorMax = Vector2.one;
            dismissRect.offsetMin = dismissRect.offsetMax = Vector2.zero;

            _panel = QdaoUguiFactory.CreateRect("SystemCategoryPanel", _chrome,
                PanelX, PanelY, PanelWidth, 424);
            var paper = _panel.gameObject.AddComponent<Image>();
            paper.sprite = QdaoUguiTheme.RequireSprite(NewArt + "chrome/frame");
            paper.type = Image.Type.Sliced;
            paper.raycastTarget = true;
            _title = QdaoUguiFactory.CreateText("CategoryTitle", _panel, 112, 26, 320, 48,
                "修行", 40, Ink);
            _description = QdaoUguiFactory.CreateText("CategoryDescription", _panel, 44, 77, 500, 30,
                CategoryDescriptions[0], 23, SecondaryInk);
            _categoryIllustration = QdaoUguiFactory.CreateImage("CategoryIllustration", _panel,
                524, 18, 82, 82, EntryIcon(WorldHudEntry.Skills));
            _categoryIllustration.preserveAspect = true;
            var close = QdaoUguiFactory.CreateArtButton("CloseCategory", _panel, 626, 28, 44, 44,
                QdaoUguiTheme.RequireSprite("UI/Ugui/TeamV2/close"), out var closeImage);
            closeImage.preserveAspect = true;
            _close = (RectTransform)close.transform;
            close.onClick.AddListener(() => ShowCategory(null));
            close.gameObject.name = "CloseCategory_收起";

            for (int i = 0; i < 4; i++)
            {
                int index = i;
                _categorySprites[i] = LoadIcon(CategoryIcons[i], "UI/Ugui/RefreshV8/round_badge_taiji");
                var tab = QdaoUguiFactory.CreateArtButton("WorldHudCategory_" + (WorldHudCategory)i,
                    _chrome, CategoryFirstX + i * 154, CategoryY, 146, 88,
                    _normalTab, out var plate);
                plate.type = Image.Type.Sliced;
                var icon = QdaoUguiFactory.CreateImage("CategoryIcon", tab.transform, 10, 15, 58, 58,
                    _categorySprites[i]);
                icon.preserveAspect = true;
                var name = QdaoUguiFactory.CreateText("CategoryName", tab.transform, 66, 22, 68, 44,
                    CategoryNames[i], 31, Ink, TextAlignmentOptions.Center);
                name.fontStyle = FontStyles.Bold;
                if (i == (int)WorldHudCategory.Social)
                {
                    var notice = QdaoUguiFactory.CreateImage("PendingMailOrTeamNotice", tab.transform,
                        120, 5, 22, 22, QdaoUguiTheme.RequireSprite("UI/Ugui/RefreshV8/status_dot_orange"));
                    notice.preserveAspect = true;
                    _socialNotice = notice.gameObject;
                }
                ConfigureFeedback(tab);
                tab.onClick.AddListener(() =>
                    ShowCategory(CurrentCategory == (WorldHudCategory)index ? null : (WorldHudCategory)index));
                _tabs[i] = tab;
                _tabPlates[i] = plate;
                _tabNames[i] = name;
            }

            var location = QdaoUguiFactory.CreateImage("LocationPaper", _chrome, 36, 28, 414, 96,
                QdaoUguiTheme.RequireSprite(NewArt + "chrome/location"));
            location.type = Image.Type.Sliced;
            _locationBoard = location.gameObject;
            _layout = new WorldHudLayout(_chrome, _panel, _tabs, _locationBoard);
            Canvas.preWillRenderCanvases += SyncAllLayout;

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
            _slots.RemoveAll(slot =>
            {
                if (slot.Source != null) return false;
                _layout.Entry(slot.Id, false);
                return true;
            });
            foreach (var slot in _slots)
                if (slot.Source == rect) return;
            _slots.Add(CreateSlot(id, rect, button, label));
            button.onClick.AddListener(() => ShowCategory(null));
            RefreshPresentation();
        }

        private Slot CreateSlot(WorldHudEntry id, RectTransform rect, Button button, TMP_Text label)
        {
            // Keep the source HUD as an ancestor: its availability remains authoritative.
            RectTransform clip = null;
            var parent = rect.parent;
            if (!IsQuick(id))
            {
                clip = QdaoUguiFactory.CreateRect("WorldHudClip_" + id, parent, 0, 0, TileWidth, TileHeight);
                clip.gameObject.AddComponent<RectMask2D>();
                parent = clip;
            }
            var container = QdaoUguiFactory.CreateRect("WorldHudSlot_" + id, parent,
                0, 0, TileWidth, TileHeight);
            var group = container.gameObject.AddComponent<CanvasGroup>();
            if (clip != null)
                container.gameObject.AddComponent<WorldHudScrollRelay>().Target = _layout.TileScroll;
            rect.SetParent(container, false);
            if (id == WorldHudEntry.MysteryMerchant || id == WorldHudEntry.TeamFollow)
            {
                // Keep the merchant's countdown label and green status artwork.
                Place(rect, 0, 0, id == WorldHudEntry.MysteryMerchant ? MerchantEntryWidth : 210, 81.6f);
                return new Slot { Id = id, Source = rect, Container = container,
                    Button = button, Label = label, Visibility = group, Clip = clip,
                    Proxy = _layout.Entry(id, false) };
            }
            Place(rect, 0, 0, TileWidth, TileHeight);
            var buttonRect = (RectTransform)button.transform;
            if (buttonRect != rect) Place(buttonRect, 0, 0, TileWidth, TileHeight);
            var hit = button.GetComponent<Image>();
            // Older feature entries may already contain a decorative icon.
            // Replace its drawing, keeping their labels and action objects intact.
            foreach (var decoration in button.GetComponentsInChildren<Image>(true))
                if (decoration != hit) decoration.enabled = false;
            if (hit != null)
            {
                hit.sprite = QdaoUguiTheme.RequireSprite(NewArt + "chrome/entry-card");
                hit.type = Image.Type.Sliced;
                hit.color = Color.white;
                hit.raycastTarget = true;
            }
            var icon = QdaoUguiFactory.CreateImage("SystemIcon_" + id, button.transform,
                12, 10, 84, 84, EntryIcon(id));
            icon.preserveAspect = true;
            label.transform.SetAsLastSibling();
            Place(label.rectTransform, 102, 14, TileWidth - 118, 40);
            label.fontSize = 30;
            label.enableAutoSizing = true;
            label.fontSizeMin = 21;
            label.fontSizeMax = 30;
            label.font = QdaoUguiTheme.ResolveFont();
            label.fontStyle = FontStyles.Bold;
            label.fontWeight = FontWeight.Bold;
            label.color = Ink;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            button.targetGraphic = hit;
            ConfigureFeedback(button);
            if (IsReferenceEntry(id))
            {
                // Keep the established right-column targets while sharing the new paper skin.
                Place(rect, 0, 0, 336, 81.6f);
                if (buttonRect != rect) Place(buttonRect, 0, 0, 336, 81.6f);
                Place(icon.rectTransform, 16, 7, 68, 68);
                Place(label.rectTransform, 96, 0, 218, 81.6f);
                label.fontSize = label.fontSizeMax = 36;
            }
            else if (id == WorldHudEntry.Map || id == WorldHudEntry.Channel)
            {
                bool map = id == WorldHudEntry.Map;
                float width = map ? 246 : 156;
                Place(rect, 0, 0, width, 88);
                if (buttonRect != rect) Place(buttonRect, 0, 0, width, 88);
                if (hit != null) { hit.sprite = null; hit.color = new Color(1, 1, 1, .001f); }
                Place(icon.rectTransform, map ? 12 : 4, map ? 15 : 23, map ? 58 : 42, map ? 58 : 42);
                Place(label.rectTransform, map ? 78 : 46, 0, map ? 166 : 108, 88);
                label.fontSize = label.fontSizeMax = map ? 30 : 27;
                button.targetGraphic = icon;
            }
            else if (id == WorldHudEntry.Bag)
            {
                Place(rect, 0, 0, 204, 160);
                if (buttonRect != rect) Place(buttonRect, 0, 0, 204, 160);
                if (hit != null) { hit.sprite = null; hit.color = new Color(1, 1, 1, .001f); }
                Place(icon.rectTransform, 38, 0, 128, 128);
                var plate = QdaoUguiFactory.CreateImage("QuickEntryPlaque", button.transform,
                    28, 112, 148, 46, QdaoUguiTheme.RequireSprite(NewArt + "chrome/name-tag"));
                plate.type = Image.Type.Sliced;
                Place(label.rectTransform, 32, 114, 140, 42);
                label.alignment = TextAlignmentOptions.Center;
                label.fontSize = label.fontSizeMax = 29;
                label.transform.SetAsLastSibling();
                button.targetGraphic = icon;
            }
            else QdaoUguiFactory.CreateText("SystemPurpose", button.transform, 103, 58,
                TileWidth - 119, 28, Purpose(id), 22, SecondaryInk);
            return new Slot { Id = id, Source = rect, Container = container,
                Button = button, Label = label, Visibility = group, Clip = clip,
                Proxy = _layout.Entry(id, false) };
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
            if (CurrentCategory != category) _layout?.ResetScroll();
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
            // 原生Canvas可比托管布局/子控件存活更久；退出阶段不再刷新残留视图。
            if (_layout == null || _skills == null || _dismiss == null || _panel == null ||
                _locationBoard == null || _socialNotice == null || _title == null ||
                _description == null || _categoryIllustration == null || _close == null)
            {
                _chrome.gameObject.SetActive(false);
                return;
            }
            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] != null && _tabPlates[i] != null && _tabNames[i] != null) continue;
                _chrome.gameObject.SetActive(false);
                return;
            }
            _slots.RemoveAll(slot =>
            {
                if (slot.Source != null) return false;
                _layout.Entry(slot.Id, false);
                return true;
            });
            bool available = false, roleAvailable = false, locationAvailable = false;
            System.Array.Clear(_categoryAvailable, 0, _categoryAvailable.Length);
            bool socialNotice = false;
            foreach (var slot in _slots)
            {
                if (!slot.Available) continue;
                available = true;
                if (slot.Id == WorldHudEntry.Role) roleAvailable = true;
                if (slot.Id == WorldHudEntry.Map || slot.Id == WorldHudEntry.Channel) locationAvailable = true;
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
            _locationBoard.SetActive(locationAvailable);
            for (int i = 0; i < 4; i++)
            {
                _tabs[i].interactable = _categoryAvailable[i];
                bool selected = CurrentCategory == (WorldHudCategory)i;
                _tabPlates[i].sprite = selected ? _selectedTab : _normalTab;
                _tabNames[i].color = selected ? QdaoUguiTheme.Cream : Ink;
            }
            _socialNotice.SetActive(socialNotice);
            int position = 0;
            // Stable explicit order, independent of RuntimeInitializeOnLoad execution order.
            foreach (var entry in EntryOrder)
            {
                foreach (var slot in _slots)
                {
                    if (slot.Id != entry) continue;
                    bool show = slot.Available && (IsQuick(slot.Id) || CurrentCategory == Category(slot.Id));
                    SetShown(slot, show);
                    _layout.Entry(slot.Id, show);
                    if (!IsQuick(slot.Id) && show)
                    {
                        position++;
                    }
                }
            }
            bool skillsShown = roleAvailable && CurrentCategory == WorldHudCategory.Cultivation;
            SetShown(_skills, skillsShown);
            _layout.Entry(WorldHudEntry.Skills, skillsShown);
            if (skillsShown) position++;
            _layout.Rebuild(position, locationAvailable, available && CurrentCategory.HasValue);
            SyncAllLayout();
            WorldSideHudRoot.Instance?.ApplyResponsiveLayout(_layout.Side);
            if (CurrentCategory.HasValue)
            {
                _title.text = CategoryNames[(int)CurrentCategory.Value];
                _description.text = CategoryDescriptions[(int)CurrentCategory.Value];
                _categoryIllustration.sprite = _categorySprites[(int)CurrentCategory.Value];
                bool compact = _layout.Columns == 1;
                float width = _layout.PanelWidth;
                _title.rectTransform.sizeDelta = new Vector2(width - 190, 48);
                _description.rectTransform.sizeDelta = new Vector2(width - 88, 30);
                _description.enableAutoSizing = true;
                _description.fontSizeMin = 16;
                _description.fontSizeMax = 23;
                _categoryIllustration.gameObject.SetActive(!compact);
                Place(_close, width - 74, 28, 44, 44);
            }
        }

        private static void SetShown(Slot slot, bool visible)
        {
            if (slot == null || slot.Visibility == null) return;
            slot.Visibility.alpha = visible ? 1f : 0f;
            slot.Visibility.interactable = visible;
            slot.Visibility.blocksRaycasts = visible;
        }

        private void SyncLayout(Slot slot)
        {
            if (slot == null || slot.Container == null || _layout == null) return;
            if (slot.Clip != null) WorldHudLayout.CopyBounds(_layout.TileViewport, slot.Clip);
            WorldHudLayout.CopyBounds(slot.Proxy, slot.Container);
        }

        private void SyncAllLayout()
        {
            foreach (var slot in _slots) SyncLayout(slot);
            SyncLayout(_skills);
        }

        private static bool IsQuick(WorldHudEntry id)
            => IsReferenceEntry(id) || id == WorldHudEntry.Bag || id == WorldHudEntry.Map ||
                id == WorldHudEntry.Channel || id == WorldHudEntry.MysteryMerchant || id == WorldHudEntry.TeamFollow;

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
            var path = NewArt + id.ToString().ToLowerInvariant();
            return LoadIcon(path, "UI/Ugui/RefreshV8/round_badge_taiji");
        }

        private static string Purpose(WorldHudEntry id) => id switch
        {
            WorldHudEntry.Pet => "灵宠养成", WorldHudEntry.Skills => "道法研习",
            WorldHudEntry.Missions => "问道寻踪", WorldHudEntry.Activities => "四时雅集",
            WorldHudEntry.Rank => "论剑题名", WorldHudEntry.Team => "结伴同行",
            WorldHudEntry.Guild => "同门相聚", WorldHudEntry.Social => "道友往来",
            WorldHudEntry.Mail => "尺素传情", WorldHudEntry.Trade => "寄售寻宝",
            _ => string.Empty
        };

        private void OnDestroy()
        {
            Canvas.preWillRenderCanvases -= SyncAllLayout;
            _layout = null;
            if (Instance == this) Instance = null;
        }
    }
}
