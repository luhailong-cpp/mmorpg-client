using System.Collections;
using System.Collections.Generic;
using MmorpgClient.Core;
using MmorpgClient.Game;
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
            (1u, "剑修", "近战爆发"),
            (2u, "法修", "远程法术"),
            (3u, "丹修", "治疗辅助"),
            (4u, "体修", "坚韧防御"),
        };
        private static readonly string[] ClassBadges =
        {
            "round_badge_sword", "round_badge_water", "round_badge_furnace", "round_badge_mountain",
        };
        private static readonly string[] MalePortraits =
        {
            "02_fire_talisman_boy_v3", "02_fire_talisman_boy_v3",
            "08_alchemy_prodigy_boy_v3", "04_mountain_guardian_boy_v3",
        };
        private static readonly string[] FemalePortraits =
        {
            "01_ice_sword_girl_v3", "14_short_hair_snow_summoner_girl_v3",
            "03_lotus_healer_girl_v3", "10_crimson_spear_girl_v3",
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

        private RectTransform _createRoot;
        private readonly List<UiTextButton> _classButtons = new();
        private UiTextButton _maleButton;
        private UiTextButton _femaleButton;
        private UiTextButton _createBackButton;
        private UiTextButton _confirmCreateButton;

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
            _listHint.text = "选择一位道友，再进入仙境";
            _createRoot.gameObject.SetActive(false);
            _selectRoot.gameObject.SetActive(true);
            RebuildRows();
            bool canCreate = _players.Count < MaxRows;
            _gotoCreateButton.SetVisible(canCreate);
            if (!canCreate) _listHint.text = "角色位已满，可选择已有角色";
            _previewHint.text = "确认角色后，继续仙境之旅";
        }

        private void ShowCreateMode()
        {
            if (_players != null && _players.Count >= MaxRows) return;
            _creating = true;
            _title.text = "创建角色";
            _listHint.text = "选择职业与性别，开启修行";
            _selectRoot.gameObject.SetActive(false);
            _createRoot.gameObject.SetActive(true);
            _createBackButton.SetText(_players != null && _players.Count > 0 ? "返回选角" : "返回选服");
            _previewHint.text = "创建后将直接进入所选区服";
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
            for (int i = 0; i < count; i++)
            {
                var player = _players[i];
                if (player.PlayerId == _selectedPlayerId ||
                    (_selectedPlayerId == 0 && player.PlayerId == lastPlayed)) selectedIndex = i;
                var card = new RoleCard { PlayerId = player.PlayerId };
                card.Button = QdaoRefreshArt.Button($"Role_{player.PlayerId}", _rowContainer,
                    0f, i * 116f, 528f, 96f, "list_row_normal", out card.Plate);
                ConfigureNavigation(card.Button);
                var portrait = QdaoUguiFactory.CreateImage("Portrait", card.Plate.transform,
                    84f, 18f, 60f, 60f, ResolvePortrait(player.ClassId, player.Gender));
                portrait.preserveAspect = true;
                card.Name = Label("Name", card.Plate.transform, 160f, 17f, 290f, 34f,
                    ClassName(player.ClassId) + (player.PlayerId == lastPlayed ? " · 上次" : string.Empty), 29f, Ink);
                card.Detail = Label("Identity", card.Plate.transform, 160f, 51f, 290f, 28f,
                    $"{GenderName(player.Gender)}  ·  {ShortId(player.PlayerId)}", 21f, Wood);
                card.Check = QdaoUguiFactory.CreateImage("SelectedCheck", card.Plate.transform,
                    464f, 29f, 36f, 36f, QdaoRefreshArt.Load("check"));
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
                QdaoRefreshArt.Skin(card.Plate, selected ? "list_row_selected" : "list_row_normal");
                card.Name.color = selected ? Ivory : Ink;
                card.Detail.color = selected ? Ivory : Wood;
                card.Check.gameObject.SetActive(selected);
            }
            RefreshPreview(player.ClassId, player.Gender, player.PlayerId);
        }

        private void RefreshCreateHighlights()
        {
            for (int i = 0; i < _classButtons.Count; i++)
                SetChoiceState(_classButtons[i], Classes[i].id == _pickedClassId);
            SetChoiceState(_maleButton, _pickedGender == 1);
            SetChoiceState(_femaleButton, _pickedGender == 2);
            RefreshPreview(_pickedClassId, _pickedGender, 0);
        }

        private void RefreshPreview(uint classId, uint gender, ulong playerId)
        {
            int index = ClassIndex(classId);
            _hero.sprite = ResolvePortrait(classId, gender);
            _hero.enabled = _hero.sprite != null;
            _previewTitle.text = ClassName(classId);
            _classBadge.sprite = QdaoRefreshArt.Load(ClassBadges[index]);
            _classValue.text = ClassName(classId);
            _genderValue.text = GenderName(gender);
            _zoneValue.text = $"第 {_zoneId} 区";
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

            var scene = QdaoRefreshArt.Load("sanctuary_background") ??
                        BattleArtCatalog.LoadSprite("UI/Ugui/Native/scene_background");
            BattleUiWidgets.CreateStretchPanel("InputBlocker", _canvasGo.transform, QdaoUguiTheme.Letterbox);
            if (scene != null)
                QdaoUguiFactory.CreateAspectFillImage("Sanctuary", _canvasGo.transform, scene,
                    scene.rect.width / scene.rect.height);
            _designRoot = QdaoUguiFactory.CreateCenteredRect("RoleDesignRoot", _canvasGo.transform,
                QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);

            var ring = QdaoUguiFactory.CreateImage("CultivationCircle", _designRoot,
                740f, 768f, 1100f, 256f, BattleArtCatalog.LoadSpawnRing(true));
            ring.color = new Color(1f, 0.91f, 0.68f, 0.42f);
            _hero = QdaoUguiFactory.CreateImage("CharacterArtwork", _designRoot,
                768f, 100f, 940f, 900f, null);
            _hero.preserveAspect = true;
            var fox = QdaoUguiFactory.CreateImage("CompanionFox", _designRoot,
                1464f, 482f, 476f, 480f, QdaoRefreshArt.Load("companion_fox"));
            fox.preserveAspect = true;
            fox.enabled = fox.sprite != null;

            QdaoRefreshArt.Panel("RoleListWindow", _designRoot, 96f, 106f, 640f, 826f, "main_frame");
            var titlePlate = QdaoRefreshArt.Panel("TitleJade", _designRoot,
                112f, 74f, 610f, 112f, "primary_button_normal");
            _title = Label("Title", titlePlate.transform, 60f, 17f, 490f, 74f, "选择角色", 50f,
                Ivory, TextAlignmentOptions.Center);
            _listHint = Label("ListHint", _designRoot, 158f, 180f, 510f, 44f,
                string.Empty, 24f, Wood, TextAlignmentOptions.Center);
            BuildDetails();
            BuildSelectRoot();
            BuildCreateRoot();
            _canvasGo.SetActive(false);
        }

        private void BuildDetails()
        {
            QdaoRefreshArt.Panel("RoleDetailsWindow", _designRoot, 1940f, 184f, 532f, 626f, "content_panel");
            _classBadge = QdaoUguiFactory.CreateImage("ClassEmblem", _designRoot,
                2142f, 112f, 124f, 124f, null);
            _classBadge.preserveAspect = true;
            _previewTitle = Label("PreviewTitle", _designRoot, 2008f, 255f, 396f, 76f,
                string.Empty, 50f, Ink, TextAlignmentOptions.Center);
            _classValue = DetailRow("Class", "所属职业", 376f);
            _genderValue = DetailRow("Gender", "性别", 448f);
            _zoneValue = DetailRow("Zone", "所属区服", 520f);
            _identityValue = DetailRow("Identity", "角色编号", 592f, out _identityLabel);
            _directionValue = DetailRow("Direction", "修行方向", 664f);
            _previewHint = Label("PreviewHint", _designRoot, 2010f, 742f, 396f, 36f,
                string.Empty, 22f, Wood, TextAlignmentOptions.Center);
        }

        private TMP_Text DetailRow(string name, string caption, float y)
            => DetailRow(name, caption, y, out _);

        private TMP_Text DetailRow(string name, string caption, float y, out TMP_Text captionText)
        {
            captionText = Label(name + "Label", _designRoot, 2010f, y, 158f, 44f, caption, 28f, Wood);
            var value = Label(name + "Value", _designRoot, 2168f, y, 238f, 44f,
                string.Empty, 28f, Ink, TextAlignmentOptions.MidlineRight);
            var line = QdaoUguiFactory.CreateImage(name + "Rule", _designRoot,
                2010f, y + 54f, 396f, 1f, null);
            line.color = new Color(Gold.r, Gold.g, Gold.b, 0.32f);
            return value;
        }

        private void BuildSelectRoot()
        {
            _selectRoot = QdaoUguiFactory.CreateRect("SelectRoot", _designRoot, 0f, 0f, 2560f, 1080f);
            _rowContainer = QdaoUguiFactory.CreateRect("RoleCards", _selectRoot, 150f, 232f, 528f, 580f);
            var back = TextButton("BackToServers", _selectRoot, 208f, 944f, 416f,
                "返回选服", false);
            back.Button.onClick.AddListener(ResolveCancel);
            _gotoCreateButton = TextButton("CreateNewRole", _selectRoot, 176f, 820f, 476f,
                "＋  创建角色", false);
            _gotoCreateButton.Button.onClick.AddListener(ShowCreateMode);
            _enterButton = TextButton("EnterSanctuary", _selectRoot, 1940f, 892f, 532f,
                "进入仙境", true, 46f);
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
            Label("GenderLabel", _createRoot, 164f, 708f, 500f, 40f, "选择性别", 28f,
                Wood, TextAlignmentOptions.Center);
            _maleButton = ChoiceButton("GenderMale", _createRoot, 152f, 760f, 250f, "男");
            _femaleButton = ChoiceButton("GenderFemale", _createRoot, 422f, 760f, 250f, "女");
            _maleButton.Button.onClick.AddListener(() => { _pickedGender = 1; RefreshCreateHighlights(); });
            _femaleButton.Button.onClick.AddListener(() => { _pickedGender = 2; RefreshCreateHighlights(); });
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

        private static UiTextButton TextButton(string name, UnityEngine.Transform parent, float x, float y,
            float width, string text, bool primary, float fontSize = 34f)
        {
            var button = new UiTextButton();
            button.Button = QdaoRefreshArt.Button(name, parent, x, y, width, 112f,
                primary ? "primary_button_normal" : "tab_normal", out button.Plate);
            button.Rect = button.Plate.rectTransform;
            button.Label = Label("Label", button.Rect, 44f, 15f, width - 88f, 80f,
                text, fontSize, primary ? Ivory : Ink, TextAlignmentOptions.Center);
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
                text, 29f, Ink, TextAlignmentOptions.Center);
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
            => QdaoUguiFactory.CreateText(name, parent, x, y, width, height, text, size, color, alignment);

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

        private static Sprite ResolvePortrait(uint classId, uint gender)
        {
            int index = ClassIndex(classId);
            if (index == 0 && gender != 2)
            {
                var hero = QdaoRefreshArt.Load("hero");
                if (hero != null) return hero;
            }
            string file = gender == 2 ? FemalePortraits[index] : MalePortraits[index];
            return BattleArtCatalog.LoadSprite("UI/qdao_v3/characters/" + file);
        }

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
