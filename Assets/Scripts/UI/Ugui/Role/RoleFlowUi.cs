using System.Collections;
using System.Collections.Generic;
using MmorpgClient.Core;
using MmorpgClient.Game;
using MmorpgClient.World;
using MmorpgClient.UI.Ugui.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Role
{
    /// <summary>
    /// Native character selection and creation over the painted sanctuary.
    /// The PlayerChooser coroutine remains the only hand-off to EnterGame;
    /// choosing a card previews the real account role before the enter action.
    /// </summary>
    public sealed class RoleFlowUi : MonoBehaviour
    {
        public static RoleFlowUi Instance { get; private set; }

        // These ids are accepted by the server's Class table.
        private static readonly (uint id, string name, string desc)[] Classes =
        {
            (1u, "破军", "近战物攻"),
            (2u, "玄霄", "法术输出"),
            (3u, "丹心", "治疗辅助"),
            (4u, "逐风", "高速先手"),
        };
        private static readonly string[] ClassBadges =
        {
            "round_badge_sword", "round_badge_water", "round_badge_furnace", "round_badge_mountain",
        };

        private const int MaxRows = 5;
        private static readonly Color Ink = QdaoUguiTheme.Html("#244C3E");
        private static readonly Color Wood = QdaoUguiTheme.Html("#735433");
        private static readonly Color Gold = QdaoUguiTheme.Html("#C59645");
        private static readonly Color Ivory = QdaoUguiTheme.Html("#FFF7DE");

        private sealed class RoleCard
        {
            public Image Plate;
            public TMP_Text Name;
            public TMP_Text Detail;
            public Image Check;
            public Button Button;
            public ulong PlayerId;
        }

        private GameObject _canvasGo;
        private RectTransform _designRoot;
        private TMP_Text _title;
        private TMP_Text _listHint;
        private TMP_Text _previewTitle;
        private TMP_Text _classValue;
        private TMP_Text _genderValue;
        private TMP_Text _zoneValue;
        private TMP_Text _identityValue;
        private TMP_Text _identityLabel;
        private TMP_Text _directionValue;
        private TMP_Text _previewHint;
        private Image _hero;
        private Image _classBadge;

        private RectTransform _selectRoot;
        private RectTransform _rowContainer;
        private readonly List<RoleCard> _roleCards = new();
        private UiTextButton _gotoCreateButton;
        private UiTextButton _enterButton;
        private float _roleCardHeight;

        private RectTransform _createRoot;
        private readonly List<UiTextButton> _classButtons = new();
        private UiTextButton _maleButton;
        private UiTextButton _femaleButton;
        private UiTextButton _createBackButton;
        private UiTextButton _confirmCreateButton;
        private readonly List<QdaoCharacterCatalog.Definition> _appearanceChoices = new();
        private int _appearanceChoice; // Zero keeps the existing profession/gender default.
        private TMP_Text _appearanceLabel;

        private uint _zoneId;
        private IReadOnlyList<AccountSimplePlayer> _players;
        private bool _resolved;
        private bool _creating;
        private GameClient.PlayerChoice _result;
        private uint _pickedClassId = 1;
        private uint _pickedGender = 1;
        private ulong _selectedPlayerId;

        public static RoleFlowUi Attach(AppBootstrap app)
        {
            if (Instance == null)
            {
                var go = new GameObject("[RoleUi]");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<RoleFlowUi>();
                Instance.BuildCanvas();
            }
            if (app != null && app.GameClient != null)
                app.GameClient.PlayerChooser = Instance.Choose;
            return Instance;
        }

        private IEnumerator Choose(uint zoneId, IReadOnlyList<AccountSimplePlayer> players,
                                   GameClient.PlayerChoice choice)
        {
            _zoneId = zoneId;
            _players = players ?? new List<AccountSimplePlayer>();
            _resolved = false;
            _result = choice;
            _pickedClassId = Classes[0].id;
            _pickedGender = 1;
            _appearanceChoice = 0;
            _selectedPlayerId = 0;
            _canvasGo.SetActive(true);
            if (_players.Count > 0) ShowSelectMode();
            else ShowCreateMode();
            while (!_resolved) yield return null;
            _canvasGo.SetActive(false);
        }

        private void ResolveSelect(ulong playerId)
        {
            if (_resolved || _result == null || playerId == 0) return;
            _result.SelectedPlayerId = playerId;
            _result.CreateNew = false;
            _result.Cancelled = false;
            _resolved = true;
        }

        private void ResolveCreate()
        {
            if (_resolved || _result == null || (_players != null && _players.Count >= MaxRows)) return;
            _result.CreateNew = true;
            _result.ClassId = _pickedClassId;
            _result.Gender = _pickedGender;
            _result.AppearanceId = PickedAppearanceId;
            // Imports can change while this screen is open; never submit a vanished candidate.
            if (!string.IsNullOrEmpty(_result.AppearanceId) &&
                QdaoCharacterCatalog.Find(_result.AppearanceId)?.ResolveAppearance() == null)
            {
                _previewHint.text = "所选外观资源尚未齐套，请重新选择";
                return;
            }
            _result.Cancelled = false;
            _resolved = true;
        }

        private void ResolveCancel()
        {
            if (_resolved || _result == null) return;
            _result.Cancelled = true;
            _resolved = true;
        }

        private void Update()
        {
            if (_canvasGo == null || !_canvasGo.activeSelf || _resolved) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_creating && _players != null && _players.Count > 0) ShowSelectMode();
                else ResolveCancel();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void ShowSelectMode()
        {
            _creating = false;
            _title.text = "选择角色";
            _listHint.text = string.Empty;
            _createRoot.gameObject.SetActive(false);
            _selectRoot.gameObject.SetActive(true);
            RebuildRows();
            bool canCreate = _players.Count < MaxRows;
            _gotoCreateButton.SetVisible(canCreate);
            if (!canCreate) _listHint.text = "角色位已满，可选择已有角色";
            _previewHint.text = string.Empty;
        }

        private void ShowCreateMode()
        {
            if (_players != null && _players.Count >= MaxRows) return;
            _creating = true;
            _title.text = "创建角色";
            _listHint.text = "选择职业、性别与人物外观";
            _selectRoot.gameObject.SetActive(false);
            _createRoot.gameObject.SetActive(true);
            _createBackButton.SetText(_players != null && _players.Count > 0 ? "返回选角" : "返回选服");
            _previewHint.text = "创建后将直接进入所选区服";
            RefreshAppearanceChoices();
            RefreshCreateHighlights();
            SelectForKeyboard(_classButtons[ClassIndex(_pickedClassId)].Button);
        }

        private void RebuildRows()
        {
            for (int i = _rowContainer.childCount - 1; i >= 0; i--)
            {
                var previous = _rowContainer.GetChild(i).gameObject;
                previous.SetActive(false);
                Destroy(previous);
            }
            _roleCards.Clear();
            ulong lastPlayed = ClientSettings.GetLastPlayer(_zoneId);
            int count = Mathf.Min(_players.Count, MaxRows);
            int selectedIndex = 0;
            int slots = count + (count < MaxRows ? 1 : 0);
            _roleCardHeight = Mathf.Min(224f, (662f - Mathf.Max(0, slots - 1) * 16f) / Mathf.Max(1, slots));
            if (_gotoCreateButton != null)
            {
                _gotoCreateButton.Rect.anchoredPosition = new Vector2(166f, -(246f + count * (_roleCardHeight + 16f)));
                _gotoCreateButton.Rect.sizeDelta = new Vector2(540f, _roleCardHeight);
                _gotoCreateButton.Label.rectTransform.anchoredPosition = new Vector2(42f, 0f);
                _gotoCreateButton.Label.rectTransform.sizeDelta = new Vector2(456f, _roleCardHeight);
                _gotoCreateButton.Label.fontSize = _roleCardHeight > 170f ? 44f : 34f;
                QdaoRefreshArt.Skin(_gotoCreateButton.Plate, "server_card_medium_normal");
            }
            for (int i = 0; i < count; i++)
            {
                var player = _players[i];
                if (player.PlayerId == _selectedPlayerId ||
                    (_selectedPlayerId == 0 && player.PlayerId == lastPlayed)) selectedIndex = i;
                var card = new RoleCard { PlayerId = player.PlayerId };
                card.Button = QdaoRefreshArt.Button($"Role_{player.PlayerId}", _rowContainer,
                    0f, i * (_roleCardHeight + 16f), 540f, _roleCardHeight, "server_card_medium_normal", out card.Plate);
                ConfigureNavigation(card.Button);
                float portraitSize = Mathf.Min(176f, _roleCardHeight - 36f);
                CreateCardPortrait(card.Plate.transform, 30f, (_roleCardHeight - portraitSize) * .5f,
                    portraitSize, ResolvePortrait(player.ClassId, player.Gender, player.AppearanceId));
                float textX = portraitSize + 54f;
                float textWidth = 540f - textX - 60f;
                card.Name = Label("Name", card.Plate.transform, textX, _roleCardHeight * .24f, textWidth, 60f,
                    CharacterName(player.ClassId, player.Gender, player.AppearanceId), _roleCardHeight > 170f ? 44f : 33f, Ink);
                QdaoUguiTypography.ApplyHeading(card.Name);
                card.Detail = Label("Identity", card.Plate.transform, textX, _roleCardHeight * .60f, textWidth, 44f,
                    $"{GenderName(player.Gender)} · {ShortId(player.PlayerId)}" + (player.PlayerId == lastPlayed ? " · 上次" : string.Empty),
                    _roleCardHeight > 170f ? 28f : 26f, Wood);
                QdaoUguiTypography.ApplyBody(card.Detail);
                card.Check = QdaoUguiFactory.CreateImage("SelectedCheck", card.Plate.transform,
                    482f, _roleCardHeight * .5f - 23f, 46f, 46f, QdaoRefreshArt.Load("check"));
                card.Check.gameObject.SetActive(false);
                int captured = i;
                card.Button.onClick.AddListener(() => PreviewPlayer(captured));
                _roleCards.Add(card);
            }
            _enterButton.SetInteractable(count > 0);
            if (count > 0)
            {
                PreviewPlayer(selectedIndex);
                SelectForKeyboard(_roleCards[selectedIndex].Button);
            }
            else
            {
                _selectedPlayerId = 0;
                RefreshPreview(_pickedClassId, _pickedGender, 0);
            }
        }

        private void PreviewPlayer(int index)
        {
            if (_players == null || index < 0 || index >= Mathf.Min(_players.Count, MaxRows)) return;
            var player = _players[index];
            _selectedPlayerId = player.PlayerId;
            for (int i = 0; i < _roleCards.Count; i++)
            {
                var card = _roleCards[i];
                bool selected = i == index;
                QdaoRefreshArt.Skin(card.Plate, selected ? "server_card_medium_selected" : "server_card_medium_normal");
                card.Name.color = selected ? Ivory : Ink;
                card.Detail.color = selected ? Ivory : Wood;
                card.Check.gameObject.SetActive(selected);
            }
            RefreshPreview(player.ClassId, player.Gender, player.PlayerId, player.AppearanceId);
        }

        private void RefreshCreateHighlights()
        {
            for (int i = 0; i < _classButtons.Count; i++)
                SetChoiceState(_classButtons[i], Classes[i].id == _pickedClassId);
            SetChoiceState(_maleButton, _pickedGender == 1);
            SetChoiceState(_femaleButton, _pickedGender == 2);
            _appearanceLabel.text = _appearanceChoice == 0 ? "外观：职业默认" :
                "外观：" + _appearanceChoices[_appearanceChoice - 1].Name;
            RefreshPreview(_pickedClassId, _pickedGender, 0, PickedAppearanceId);
        }

        private string PickedAppearanceId => _appearanceChoice > 0 && _appearanceChoice <= _appearanceChoices.Count
            ? _appearanceChoices[_appearanceChoice - 1].Id : string.Empty;

        private void RefreshAppearanceChoices()
        {
            string selected = PickedAppearanceId;
            _appearanceChoices.Clear();
            foreach (var entry in QdaoCharacterCatalog.RetainedOriginalAll)
                if (entry.ResolveAppearance() != null) _appearanceChoices.Add(entry);
            _appearanceChoice = 0;
            for (int i = 0; i < _appearanceChoices.Count; i++)
                if (_appearanceChoices[i].Id == selected) _appearanceChoice = i + 1;
        }

        private void CycleAppearance(int delta)
        {
            int count = _appearanceChoices.Count + 1;
            _appearanceChoice = (_appearanceChoice + delta + count) % count;
            RefreshCreateHighlights();
        }

        private void RefreshPreview(uint classId, uint gender, ulong playerId, string appearanceId = null)
        {
            int index = ClassIndex(classId);
            _hero.sprite = ResolvePortrait(classId, gender, appearanceId);
            _hero.enabled = _hero.sprite != null;
            _previewTitle.text = CharacterName(classId, gender, appearanceId);
            _classBadge.sprite = QdaoRefreshArt.Load(ClassBadges[index]);
            _classValue.text = ClassName(classId);
            _genderValue.text = GenderName(gender);
            var zone = AppBootstrap.Instance?.Session?.Zones.Find(z => z.zone_id == _zoneId);
            _zoneValue.text = zone != null && !string.IsNullOrEmpty(zone.name) ? zone.name : $"第 {_zoneId} 区";
            _identityLabel.text = _creating ? "角色状态" : "角色编号";
            _identityValue.text = _creating ? "即将踏入仙境" : ShortId(playerId);
            _directionValue.text = Classes[index].desc;
        }

        private void BuildCanvas()
        {
            EnsureEventSystem();
            _canvasGo = new GameObject("[RoleUgui]", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo.transform.SetParent(transform, false);
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 150;
            canvas.pixelPerfect = true;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referencePixelsPerUnit = 100f;

            var scene = Resources.Load<Sprite>(QdaoRefreshArt.Root + "sanctuary_background") ??
                        QdaoRefreshArt.Load("login_background") ??
                        BattleArtCatalog.LoadSprite("UI/Ugui/Native/scene_background");
            BattleUiWidgets.CreateStretchPanel("InputBlocker", _canvasGo.transform, QdaoUguiTheme.Letterbox);
            if (scene != null)
                QdaoUguiFactory.CreateAspectFillImage("Sanctuary", _canvasGo.transform, scene,
                    scene.rect.width / scene.rect.height);
            _designRoot = QdaoUguiFactory.CreateCenteredRect("RoleDesignRoot", _canvasGo.transform,
                QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);

            var ring = QdaoUguiFactory.CreateImage("CultivationCircle", _designRoot,
                748f, 780f, 1150f, 300f, BattleArtCatalog.LoadSpawnRing(true));
            ring.color = new Color(1f, 0.91f, 0.68f, 0.7f);
            ring.enabled = ring.sprite != null;
            _hero = QdaoUguiFactory.CreateImage("CharacterArtwork", _designRoot,
                985f, 256f, 720f, 830f, null);
            _hero.preserveAspect = true;

            QdaoRefreshArt.Panel("RoleListWindow", _designRoot, 112f, 142f, 624f, 814f, "main_frame");
            var titlePlate = QdaoRefreshArt.Panel("TitleJade", _designRoot,
                106f, 72f, 640f, 124f, "primary_button_normal");
            _title = Label("Title", titlePlate.transform, 65f, 20f, 510f, 84f, "选择角色", 59f,
                Ivory, TextAlignmentOptions.Center);
            QdaoUguiTypography.ApplyHeading(_title);
            QdaoUguiFactory.CreateImage("RoleTitleEmblem", _designRoot, 367f, 7f, 112f, 100f,
                QdaoRefreshArt.Load("round_badge_taiji")).preserveAspect = true;
            _listHint = Label("ListHint", _designRoot, 150f, 200f, 540f, 38f,
                string.Empty, 26f, Wood, TextAlignmentOptions.Center);
            BuildDetails();
            BuildSelectRoot();
            BuildCreateRoot();
            _canvasGo.SetActive(false);
        }

        private void BuildDetails()
        {
            QdaoRefreshArt.Panel("RoleDetailsWindow", _designRoot, 1920f, 174f, 510f, 656f, "content_panel");
            _classBadge = QdaoUguiFactory.CreateImage("ClassEmblem", _designRoot,
                2118f, 96f, 118f, 118f, null);
            _classBadge.preserveAspect = true;
            _previewTitle = Label("PreviewTitle", _designRoot, 1980f, 250f, 390f, 88f,
                string.Empty, 58f, Ink, TextAlignmentOptions.Center);
            QdaoUguiTypography.ApplyHeading(_previewTitle);
            _classValue = DetailRow("Class", "所属职业", 376f);
            _genderValue = DetailRow("Gender", "性别", 448f);
            _zoneValue = DetailRow("Zone", "所属区服", 520f);
            _identityValue = DetailRow("Identity", "角色编号", 592f, out _identityLabel);
            _directionValue = DetailRow("Direction", "修行方向", 664f);
            _previewHint = Label("PreviewHint", _designRoot, 1950f, 838f, 474f, 44f,
                string.Empty, 26f, Wood, TextAlignmentOptions.Center);
        }

        private TMP_Text DetailRow(string name, string caption, float y)
            => DetailRow(name, caption, y, out _);

        private TMP_Text DetailRow(string name, string caption, float y, out TMP_Text captionText)
        {
            captionText = Label(name + "Label", _designRoot, 1982f, y, 155f, 48f, caption, 30f, Wood);
            var value = Label(name + "Value", _designRoot, 2137f, y, 232f, 48f,
                string.Empty, 32f, Ink, TextAlignmentOptions.MidlineRight);
            QdaoUguiTypography.ApplyBody(captionText);
            QdaoUguiTypography.ApplyBody(value);
            var line = QdaoUguiFactory.CreateImage(name + "Rule", _designRoot,
                1982f, y + 54f, 387f, 1f, null);
            line.color = new Color(Gold.r, Gold.g, Gold.b, 0.32f);
            return value;
        }

        private void BuildSelectRoot()
        {
            _selectRoot = QdaoUguiFactory.CreateRect("SelectRoot", _designRoot, 0f, 0f, 2560f, 1080f);
            _rowContainer = QdaoUguiFactory.CreateRect("RoleCards", _selectRoot, 166f, 246f, 540f, 662f);
            var back = TextButton("BackToServers", _selectRoot, 245f, 963f, 360f,
                "返回选服", false);
            back.Button.onClick.AddListener(ResolveCancel);
            _gotoCreateButton = TextButton("CreateNewRole", _selectRoot, 166f, 700f, 540f,
                "＋  创建角色", false);
            _gotoCreateButton.Button.onClick.AddListener(ShowCreateMode);
            _enterButton = TextButton("EnterSanctuary", _selectRoot, 1885f, 876f, 570f,
                "进入仙境", true, 62f);
            _enterButton.Rect.sizeDelta = new Vector2(570f, 146f);
            _enterButton.Label.rectTransform.sizeDelta = new Vector2(482f, 116f);
            QdaoRefreshArt.Skin(_enterButton.Plate, "primary_button_normal");
            _enterButton.Button.onClick.AddListener(() => ResolveSelect(_selectedPlayerId));
        }

        private void BuildCreateRoot()
        {
            _createRoot = QdaoUguiFactory.CreateRect("CreateRoot", _designRoot, 0f, 0f, 2560f, 1080f);
            _classButtons.Clear();
            for (int i = 0; i < Classes.Length; i++)
            {
                var definition = Classes[i];
                var choice = ChoiceButton($"Class_{definition.id}", _createRoot,
                    150f, 246f + i * 112f, 528f, definition.name + "    " + definition.desc);
                var badge = QdaoUguiFactory.CreateImage("ClassBadge", choice.Rect,
                    84f, 17f, 62f, 62f, QdaoRefreshArt.Load(ClassBadges[i]));
                badge.preserveAspect = true;
                choice.Label.rectTransform.anchoredPosition = new Vector2(158f, -17f);
                choice.Label.rectTransform.sizeDelta = new Vector2(290f, 60f);
                uint capturedId = definition.id;
                choice.Button.onClick.AddListener(() =>
                {
                    _pickedClassId = capturedId;
                    RefreshCreateHighlights();
                });
                _classButtons.Add(choice);
            }
            Label("GenderLabel", _createRoot, 164f, 708f, 500f, 44f, "选择性别", 30f,
                Wood, TextAlignmentOptions.Center);
            _maleButton = ChoiceButton("GenderMale", _createRoot, 152f, 760f, 250f, "男");
            _femaleButton = ChoiceButton("GenderFemale", _createRoot, 422f, 760f, 250f, "女");
            _maleButton.Button.onClick.AddListener(() => { _pickedGender = 1; RefreshCreateHighlights(); });
            _femaleButton.Button.onClick.AddListener(() => { _pickedGender = 2; RefreshCreateHighlights(); });
            // Only complete approved same-ID V14/V13 packages become selectable.
            var previousAppearance = TextButton("PreviousAppearance", _createRoot, 926f, 128f, 132f, "‹", false, 52f);
            var nextAppearance = TextButton("NextAppearance", _createRoot, 1628f, 128f, 132f, "›", false, 52f);
            previousAppearance.Button.onClick.AddListener(() => CycleAppearance(-1));
            nextAppearance.Button.onClick.AddListener(() => CycleAppearance(1));
            _appearanceLabel = Label("AppearanceName", _createRoot, 1064f, 156f, 552f, 64f,
                "外观：职业默认", 34f, Ink, TextAlignmentOptions.Center);
            _createBackButton = TextButton("BackFromCreate", _createRoot, 208f, 944f, 416f, "返回", false);
            _createBackButton.Button.onClick.AddListener(() =>
            {
                if (_players != null && _players.Count > 0) ShowSelectMode();
                else ResolveCancel();
            });
            _confirmCreateButton = TextButton("ConfirmCreate", _createRoot, 1940f, 892f, 532f,
                "创建并进入", true, 40f);
            _confirmCreateButton.Button.onClick.AddListener(ResolveCreate);
        }

        private static void CreateCardPortrait(UnityEngine.Transform parent, float x, float y, float size, Sprite sprite)
        {
            var disk = QdaoUguiTheme.RequireSprite(QdaoUguiTheme.StatusDotSpritePath);
            var rim = QdaoUguiFactory.CreateImage("PortraitRim", parent, x, y, size, size, disk);
            rim.color = Gold;
            var face = QdaoUguiFactory.CreateImage("PortraitMask", rim.transform, 5f, 5f, size - 10f, size - 10f, disk);
            face.color = Ivory;
            rim.enabled = sprite != null;
            face.enabled = sprite != null;
            face.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var portrait = QdaoUguiFactory.CreateImage("Portrait", face.transform,
                -size * .30f, -size * .06f, size * 1.60f, size * 2.05f, sprite);
            portrait.preserveAspect = true;
            portrait.enabled = sprite != null;
        }

        private static UiTextButton TextButton(string name, UnityEngine.Transform parent, float x, float y,
            float width, string text, bool primary, float fontSize = 34f)
        {
            var button = new UiTextButton();
            button.Button = QdaoRefreshArt.Button(name, parent, x, y, width, 112f,
                primary ? "primary_button_normal" : "tab_normal", out button.Plate);
            button.Rect = button.Plate.rectTransform;
            button.Label = Label("Label", button.Rect, 44f, 15f, width - 88f, 80f,
                text, fontSize, primary ? Ivory : Ink, TextAlignmentOptions.Center);
            QdaoUguiTypography.ApplyButton(button.Label);
            ConfigureNavigation(button.Button);
            return button;
        }

        private static UiTextButton ChoiceButton(string name, UnityEngine.Transform parent, float x, float y,
            float width, string text)
        {
            var button = new UiTextButton();
            button.Button = QdaoRefreshArt.Button(name, parent, x, y, width, 96f,
                "list_row_normal", out button.Plate);
            button.Rect = button.Plate.rectTransform;
            button.Label = Label("Label", button.Rect, 30f, 17f, width - 60f, 60f,
                text, 32f, Ink, TextAlignmentOptions.Center);
            QdaoUguiTypography.ApplyButton(button.Label);
            ConfigureNavigation(button.Button);
            return button;
        }

        private static void SetChoiceState(UiTextButton button, bool selected)
        {
            if (button == null) return;
            QdaoRefreshArt.Skin(button.Plate, selected ? "list_row_selected" : "list_row_normal");
            button.Label.color = selected ? Ivory : Ink;
        }

        private static void ConfigureNavigation(Button button)
        {
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 0.94f, 0.78f, 1f);
            colors.selectedColor = new Color(1f, 0.9f, 0.62f, 1f);
            colors.pressedColor = new Color(0.77f, 0.85f, 0.77f, 1f);
            colors.fadeDuration = 0.1f;
            button.colors = colors;
        }

        private static TMP_Text Label(string name, UnityEngine.Transform parent, float x, float y, float width,
            float height, string text, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var label = QdaoUguiFactory.CreateText(name, parent, x, y, width, height, text, size, color, alignment);
            QdaoUguiTypography.ApplyDefault(label);
            return label;
        }

        private static void SelectForKeyboard(Button button)
        {
            if (button != null && button.gameObject.activeInHierarchy && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(button.gameObject);
        }

        private static int ClassIndex(uint classId)
        {
            for (int i = 0; i < Classes.Length; i++)
                if (Classes[i].id == classId) return i;
            return 0;
        }

        private static string ClassName(uint classId)
        {
            foreach (var definition in Classes)
                if (definition.id == classId) return definition.name;
            return "无名散修";
        }

        private static string GenderName(uint gender) => gender == 2 ? "女" : gender == 1 ? "男" : "未设定";
        private static string ShortId(ulong id)
        {
            if (id == 0) return "待创建";
            string value = id.ToString();
            return value.Length > 8 ? "…" + value.Substring(value.Length - 8) : value;
        }

        private static string CharacterName(uint classId, uint gender, string appearanceId = null)
            => QdaoCharacterCatalog.Find(QdaoCharacterCatalog.ResolveRole(classId, gender, appearanceId))?.Name
               ?? ClassName(classId);

        private static Sprite ResolvePortrait(uint classId, uint gender, string appearanceId = null)
            => QdaoCharacterCatalog.LoadPortrait(QdaoCharacterCatalog.ResolveRole(classId, gender, appearanceId));

#if UNITY_EDITOR
        private static GameObject _editorPreviewObject;
        private static GameObject _editorHiddenLiveRoot;

        /// <summary>Deterministic, disconnected preview for official Unity MCP captures.</summary>
        public static void ShowEditorPreview(bool createMode)
        {
            HideEditorPreview();
            if (Instance != null && Instance._canvasGo != null && Instance._canvasGo.activeSelf)
            {
                _editorHiddenLiveRoot = Instance._canvasGo;
                _editorHiddenLiveRoot.SetActive(false);
            }
            _editorPreviewObject = new GameObject("[RoleUiEditorPreview]") { hideFlags = HideFlags.DontSave };
            var preview = _editorPreviewObject.AddComponent<RoleFlowUi>();
            preview.BuildCanvas();
            preview._zoneId = 1;
            preview._players = new List<AccountSimplePlayer>
            {
                new AccountSimplePlayer { PlayerId = 10001UL, ClassId = 1u, Gender = 1u },
                new AccountSimplePlayer { PlayerId = 10002UL, ClassId = 3u, Gender = 2u },
            };
            preview._result = new GameClient.PlayerChoice();
            preview._selectedPlayerId = 10001UL;
            preview._canvasGo.SetActive(true);
            if (createMode) preview.ShowCreateMode();
            else preview.ShowSelectMode();
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>Remove only preview objects and restore the interrupted live selector.</summary>
        public static void HideEditorPreview()
        {
            if (_editorPreviewObject != null)
            {
                _editorPreviewObject.SetActive(false);
                if (Application.isPlaying) Destroy(_editorPreviewObject);
                else DestroyImmediate(_editorPreviewObject);
                _editorPreviewObject = null;
            }
            if (_editorHiddenLiveRoot != null)
            {
                _editorHiddenLiveRoot.SetActive(true);
                _editorHiddenLiveRoot = null;
            }
        }
#endif
        private void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("[EventSystem]", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(transform, false);
        }
    }
}
