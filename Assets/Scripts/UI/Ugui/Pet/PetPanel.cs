using System.Collections.Generic;
using MmorpgClient.Game.Pet;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Pet
{
    /// <summary>
    /// 宝宝属性窗：原生头像列表、两列六属性、两列数字加点控件。
    /// 服务器保有全部数值和分配规则；本地只保存待确认增量。
    /// 美术使用已交付的无字边框与独立肖像，整屏效果图不参与运行时绘制。
    /// </summary>
    public sealed class PetPanel
    {
        private const float RowHeight = 74f;
        private const int MaxRows = 6;
        private readonly PetUiRoot _owner;
        private readonly RectTransform _root;
        private readonly List<UiTextButton> _petItems = new();
        private readonly List<ulong> _petItemIds = new();
        private readonly List<Image> _petPortraits = new();
        private readonly List<TMP_Text> _petLevels = new();
        private readonly List<TMP_Text> _petDescriptions = new();
        private readonly List<TMP_Text> _petStates = new();
        private RectTransform _rosterContent;
        private TMP_Text _carryText;
        private TMP_Text _emptyText;
        private UiTextButton _summonButton;
        private readonly TMP_Text[] _statValues = new TMP_Text[6];
        private TMP_Text _levelValue;
        private TMP_Text _growthValue;
        private TMP_Text _remainText;
        private UiTextButton _autoButton;
        private readonly UiPointRow[] _rows = new UiPointRow[MaxRows];
        private UiTextButton _resetButton;
        private UiTextButton _discardButton;
        private UiTextButton _confirmButton;
        private TMP_Text _statusText;
        private RectTransform _detailsRoot;
        private TMP_Text _detailText;
        private TMP_InputField _renameInput;
        private UiTextButton _renameConfirm;
        private readonly RectTransform _tooltip;
        private readonly TMP_Text _tooltipText;
        private PetListInfo _list;
        private ulong _selectedPetId;

        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        public PetPanel(PetUiRoot owner, UnityEngine.Transform parent)
        {
            _owner = owner;
            var window = CreatePaintedPanel("PetWindow", parent, PetUiStyle.WindowX,
                PetUiStyle.WindowY, PetUiStyle.WindowW, PetUiStyle.WindowH, "window_frame");
            window.raycastTarget = true;
            _root = window.rectTransform;

            CreatePaintedPanel("TitlePlate", _root, 630f, -70f, 780f, 131f, "title_plate");
            QdaoUguiFactory.CreateImage("Title", _root, 885f, -12f, 270f, 60f,
                LoadPainted("title_pet")).preserveAspect = true;
            var close = CreateAction("Close", _root,
                PetUiStyle.WindowW - 34f, -14f, 82f, 82f, string.Empty, false);
            SkinPainted(close.Plate, "close_button");
            QdaoUguiFactory.CreateImage("CloseTassel", _root, PetUiStyle.WindowW + 34f,
                28f, 28f, 70f, LoadPainted("close_tassel")).preserveAspect = true;
            close.Button.onClick.AddListener(Hide);

            BuildLeftColumn();
            BuildMidColumn();
            BuildRightColumn();
            BuildTabs();
            BuildDetails();

            _tooltip = QdaoUguiFactory.CreateRect("Tooltip", _root, 0f, 0f, 650f, 88f);
            var tooltipPlate = _tooltip.gameObject.AddComponent<Image>();
            tooltipPlate.color = PetUiStyle.TooltipPlate;
            tooltipPlate.raycastTarget = false;
            _tooltipText = BattleUiWidgets.CreateWrappedText("TooltipText", _tooltip,
                18f, 6f, 614f, 76f, string.Empty, 26f, PetUiStyle.TooltipText);
            _tooltip.gameObject.SetActive(false);
            Hide();
            RefreshActionButtons();
        }

        private void BuildLeftColumn()
        {
            _carryText = QdaoUguiFactory.CreateText("CarryCount", _root, PetUiStyle.LeftX,
                66f, PetUiStyle.LeftW, 48f, "携带数量：—", 36f,
                PetUiStyle.FieldLabel, TextAlignmentOptions.Center);
            var viewport = QdaoUguiFactory.CreateRect("PetRosterViewport", _root,
                PetUiStyle.LeftX, 124f, PetUiStyle.LeftW, PetUiStyle.RosterHeight);
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.001f);
            hit.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            _rosterContent = QdaoUguiFactory.CreateRect("PetRosterContent", viewport,
                0f, 0f, PetUiStyle.LeftW, PetUiStyle.RosterHeight);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = _rosterContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 70f;
            _emptyText = BattleUiWidgets.CreateWrappedText("EmptyRoster", _root,
                PetUiStyle.LeftX + 30f, 344f, PetUiStyle.LeftW - 60f, 160f,
                "暂无宝宝\n获得宝宝后可在此查看与培养", 32f, PetUiStyle.HintText);
            _emptyText.alignment = TextAlignmentOptions.Center;

            var help = CreateAction("CultivationHelp", _root, PetUiStyle.LeftX,
                806f, 256f, 76f, "培养说明", false);
            help.Button.onClick.AddListener(ShowDetails);
            _summonButton = CreateAction("Summon", _root, PetUiStyle.LeftX + 274f,
                806f, 256f, 76f, "出 战", true);
            _summonButton.Button.onClick.AddListener(OnSummonClicked);

            QdaoUguiFactory.CreateImage("RosterDivider", _root, 626f, 102f,
                42f, 780f, LoadPainted("divider")).preserveAspect = true;
        }

        private void EnsurePetItems(int count)
        {
            while (_petItems.Count < count)
            {
                int index = _petItems.Count;
                var item = CreateAction($"PetItem{index}", _rosterContent, 0f,
                    index * (PetUiStyle.PetItemHeight + PetUiStyle.PetItemGap),
                    PetUiStyle.LeftW, PetUiStyle.PetItemHeight, string.Empty, false);
                SkinPainted(item.Plate, "pet_card_normal");
                item.Button.onClick.AddListener(() => OnPetItemClicked(index));
                CreatePaintedPanel("PortraitFrame", item.Rect,
                    22f, 15f, 146f, 138f, "portrait_frame");
                var portrait = QdaoUguiFactory.CreateImage("Portrait", item.Rect,
                    31f, 24f, 128f, 120f, QdaoRefreshArt.Load("round_badge_taiji"));
                portrait.preserveAspect = true;
                var level = QdaoUguiFactory.CreateText("Level", item.Rect,
                    30f, 19f, 88f, 38f, string.Empty, 29f, QdaoRefreshArt.Ivory);
                BattleUiWidgets.ApplyOutline(level, 0.2f, new Color32(50, 48, 35, 255));
                Place(item.Label.rectTransform, 190f, 25f, 290f, 50f);
                item.Label.alignment = TextAlignmentOptions.MidlineLeft;
                item.Label.fontSize = 38f;
                var desc = QdaoUguiFactory.CreateText("Description", item.Rect,
                    190f, 88f, 238f, 40f, string.Empty, 27f, PetUiStyle.HintText);
                var state = QdaoUguiFactory.CreateText("BattleState", item.Rect,
                    420f, 96f, 92f, 40f, string.Empty, 26f, PetUiStyle.HintText,
                    TextAlignmentOptions.Center);
                state.rectTransform.localEulerAngles = new UnityEngine.Vector3(0f, 0f, 8f);
                _petItems.Add(item);
                _petItemIds.Add(0);
                _petPortraits.Add(portrait);
                _petLevels.Add(level);
                _petDescriptions.Add(desc);
                _petStates.Add(state);
            }
        }

        private void BuildMidColumn()
        {
            string[] labels = { "气 血", "法 力", "物 伤", "法 伤", "速 度", "防 御" };
            int[] positions = { 0, 2, 1, 3, 4, 5 };
            for (int i = 0; i < labels.Length; i++)
            {
                int position = positions[i];
                float x = PetUiStyle.MidX + position % 2 * 592f;
                float y = 108f + position / 2 * 82f;
                QdaoUguiFactory.CreateText($"Stat{i}Label", _root, x, y, 150f, 64f,
                    labels[i], 40f, PetUiStyle.FieldLabel);
                CreateValuePlate($"Stat{i}Plate", _root, x + 154f, y, 364f, 64f);
                _statValues[i] = QdaoUguiFactory.CreateText($"Stat{i}Value", _root,
                    x + 174f, y, 324f, 64f, "—", 36f, PetUiStyle.FieldValue);
                _statValues[i].enableAutoSizing = true;
                _statValues[i].fontSizeMin = 24f;
                _statValues[i].fontSizeMax = 36f;
            }
            _levelValue = QdaoUguiFactory.CreateText("PetLevel", _root,
                PetUiStyle.MidX, 354f, 420f, 40f, "等级 —", 27f, PetUiStyle.HintText);
            _growthValue = QdaoUguiFactory.CreateText("PetGrowth", _root,
                PetUiStyle.MidX + 592f, 354f, 500f, 40f, "成长率 —", 27f, PetUiStyle.HintText);
        }

        private void BuildRightColumn()
        {
            var watermark = QdaoUguiFactory.CreateImage("TaijiWatermark", _root,
                1480f, 596f, 360f, 320f, QdaoRefreshArt.Load("round_badge_taiji"));
            watermark.preserveAspect = true;
            watermark.color = new Color(0.45f, 0.48f, 0.4f, 0.04f);
            CreatePaintedPanel("PointsHeading", _root, 1010f, 398f,
                570f, 64f, "section_header");
            QdaoUguiFactory.CreateText("PointsTitle", _root, 1030f, 398f,
                530f, 64f, "属 性 点", 38f, PetUiStyle.FieldLabel, TextAlignmentOptions.Center);

            for (int i = 0; i < MaxRows; i++)
            {
                _rows[i] = CreatePointStepper($"Row{i}", PetUiStyle.MidX + i % 2 * 592f,
                    490f + i / 2 * 92f);
                _rows[i].Bind(OnRowChanged);
                AttachRowTooltip(_rows[i]);
                _rows[i].Rect.gameObject.SetActive(false);
            }
            _remainText = QdaoUguiFactory.CreateText("Remain", _root, PetUiStyle.MidX,
                666f, 700f, 58f, "未分配　—", 36f, PetUiStyle.RemainText);
            _resetButton = CreateAction("Reset", _root, PetUiStyle.MidX,
                730f, 316f, 72f, "一键洗点", false);
            _resetButton.Button.onClick.AddListener(OnResetClicked);
            _discardButton = CreateAction("Discard", _root, PetUiStyle.MidX + 344f,
                730f, 316f, 72f, "重 置", false);
            _discardButton.Button.onClick.AddListener(OnDiscardClicked);
            _confirmButton = CreateAction("Confirm", _root, PetUiStyle.MidX + 744f,
                730f, 378f, 72f, "确认加点", true);
            _confirmButton.Button.onClick.AddListener(OnConfirmClicked);
            var resistance = CreateAction("Resistance", _root, PetUiStyle.MidX,
                814f, 316f, 72f, "抗 性", false);
            resistance.SetInteractable(false);
            var resistanceHover = resistance.Rect.gameObject.AddComponent<UiPointerHoverRelay>();
            resistanceHover.OnEnter = () => SetStatus("抗性培养暂未开放。");
            _autoButton = CreateAction("Auto", _root, PetUiStyle.MidX + 744f,
                814f, 378f, 72f, "自动加点", true);
            _autoButton.Button.onClick.AddListener(OnAutoClicked);
            _statusText = BattleUiWidgets.CreateWrappedText("Status", _root,
                PetUiStyle.MidX + 334f, 814f, 396f, 72f, string.Empty, 24f, PetUiStyle.HintText);
        }

        private UiPointRow CreatePointStepper(string name, float x, float y)
        {
            var row = new UiPointRow
            {
                Rect = QdaoUguiFactory.CreateRect(name, _root, x, y, 530f, RowHeight)
            };
            var hit = row.Rect.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.001f);
            row.Hover = row.Rect.gameObject.AddComponent<UiPointerHoverRelay>();
            row.NameLabel = QdaoUguiFactory.CreateText(name + "Name", row.Rect,
                0f, 0f, 146f, RowHeight, string.Empty, 40f, PetUiStyle.FieldLabel);
            CreateValuePlate(name + "ValuePlate", row.Rect, 234f, 7f, 176f, 60f);
            row.ValueLabel = QdaoUguiFactory.CreateText(name + "Value", row.Rect,
                240f, 7f, 164f, 60f, "0", 36f, PetUiStyle.PointValue, TextAlignmentOptions.Center);
            row.ValueLabel.richText = true;
            row.ValueLabel.enableAutoSizing = true;
            row.ValueLabel.fontSizeMin = 24f;
            row.ValueLabel.fontSizeMax = 36f;
            var minus = CreateStep(name + "Minus", row.Rect, 158f, "−");
            row.Minus = minus.Button;
            row.Minus.onClick.AddListener(() => row.Step(-1));
            var plus = CreateStep(name + "Plus", row.Rect, 428f, "+");
            row.Plus = plus.Button;
            row.Plus.onClick.AddListener(() => row.Step(1));
            // 复用既有权威上下界与待提交逻辑；宝宝效果图仅展示数字步进器。
            var range = QdaoUguiFactory.CreateRect(name + "Range", row.Rect, 0f, 0f, 1f, 1f);
            row.Slider = range.gameObject.AddComponent<Slider>();
            row.Slider.wholeNumbers = true;
            row.Slider.navigation = new Navigation { mode = Navigation.Mode.None };
            row.Slider.onValueChanged.AddListener(row.HandleSliderChanged);
            range.gameObject.SetActive(false);
            return row;
        }

        private static UiTextButton CreateStep(string name, UnityEngine.Transform parent, float x, string text)
        {
            var step = CreateAction(name, parent, x, 0f, 68f, 74f, text, false);
            step.Label.fontSize = 48f;
            step.Label.color = PetUiStyle.FieldValue;
            SkinPainted(step.Plate, "step_plate");
            return step;
        }

        private void BuildTabs()
        {
            string[] names = { "属性", "加点", "技能", "坐骑", "图鉴" };
            for (int i = 0; i < names.Length; i++)
            {
                string label = names[i];
                bool selected = i == 1;
                var tab = CreateAction("Tab" + label, _root,
                    PetUiStyle.WindowW - 6f, 124f + i * 158f, 106f, 150f,
                    label[0] + "\n" + label[1], selected);
                tab.Label.fontSize = 39f;
                SkinPainted(tab.Plate, selected ? "tab_vertical_selected" : "tab_vertical_normal");
                if (i == 0) tab.Button.onClick.AddListener(ShowDetails);
                else if (i == 1) tab.Button.onClick.AddListener(() => _detailsRoot.gameObject.SetActive(false));
                else
                {
                    tab.SetInteractable(false);
                    var hover = tab.Rect.gameObject.AddComponent<UiPointerHoverRelay>();
                    hover.OnEnter = () => SetStatus(label + "暂未开放。");
                }
            }
        }

        private void BuildDetails()
        {
            _detailsRoot = CreatePaintedPanel("PetDetails", _root, 726f, 230f,
                1120f, 530f, "window_frame").rectTransform;
            _detailsRoot.GetComponent<Image>().raycastTarget = true;
            QdaoUguiFactory.CreateText("DetailsTitle", _detailsRoot, 52f, 36f,
                920f, 58f, "宝宝培养", 40f, PetUiStyle.FieldLabel);
            var close = CreateAction("DetailsClose", _detailsRoot, 910f, 30f,
                170f, 64f, "关 闭", false);
            close.Button.onClick.AddListener(() => _detailsRoot.gameObject.SetActive(false));
            _detailText = BattleUiWidgets.CreateWrappedText("DetailsText", _detailsRoot,
                54f, 112f, 1000f, 220f, string.Empty, 31f, PetUiStyle.FieldLabel);
            _renameInput = QdaoUguiFactory.CreateInputField("RenameInput", _detailsRoot,
                54f, 358f, 634f, 76f, "输入宝宝新名字", 20, LoadPainted("stat_field"));
            SkinPainted(_renameInput.GetComponent<Image>(), "stat_field");
            _renameInput.textComponent.fontSize = 34f;
            _renameConfirm = CreateAction("RenameConfirm", _detailsRoot, 712f, 358f,
                346f, 76f, "改 名", true);
            _renameConfirm.Button.onClick.AddListener(OnRenameConfirmed);
            _detailsRoot.gameObject.SetActive(false);
        }

        private void ShowDetails()
        {
            var pet = FindPet(_selectedPetId);
            if (pet == null) { SetStatus("请先选择一只宝宝。", true); return; }
            _detailText.text = $"{pet.Name}　等级 {pet.Level}　成长率 {pet.Growth / 100f:0.##}%\n" +
                pet.Desc + "\n加点先预分配，确认后生效；重置撤回本次预分配，一键洗点重置已分配点数。";
            _renameInput.SetTextWithoutNotify(pet.Name);
            _renameConfirm.SetText(_list.RenameCostGold > 0 ? $"改名 · {_list.RenameCostGold} 金" : "改 名");
            _detailsRoot.gameObject.SetActive(true);
            _detailsRoot.SetAsLastSibling();
        }

        private void OnRenameConfirmed()
        {
            if (_owner.Client == null || (_owner.Client.Busy) || _selectedPetId == 0) return;
            if (!CanRefreshSelectedPet()) return;
            string name = (_renameInput.text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name)) { SetStatus("宝宝名字不能为空。", true); return; }
            _owner.Client.Rename(_selectedPetId, name);
            _detailsRoot.gameObject.SetActive(false);
            SetStatus("正在修改宝宝名字…");
        }

        private void OnDiscardClicked()
        {
            RebuildRows();
            RefreshActionButtons();
            SetStatus("已撤销本次预分配。");
        }

        private static void CreateValuePlate(string name, UnityEngine.Transform parent,
            float x, float y, float width, float height)
        {
            CreatePaintedPanel(name, parent, x, y, width, height, "stat_field");
        }

        private static UiTextButton CreateAction(string name, UnityEngine.Transform parent,
            float x, float y, float width, float height, string text, bool primary)
        {
            var button = new UiTextButton
            {
                Plate = CreatePaintedPanel(name + "Plate", parent, x, y, width, height,
                    primary ? "button_primary" : "button_secondary")
            };
            button.Rect = button.Plate.rectTransform;
            button.Plate.raycastTarget = true;
            button.Button = button.Plate.gameObject.AddComponent<Button>();
            BattleUiWidgets.ConfigureButtonVisual(button.Button, button.Plate);
            button.Label = QdaoUguiFactory.CreateText(name + "Text", button.Rect,
                0f, 0f, width, height, text, 35f,
                primary ? PetUiStyle.TitleText : PetUiStyle.FieldLabel, TextAlignmentOptions.Center);
            return button;
        }

        private static Sprite LoadPainted(string name)
            => QdaoUguiTheme.RequireSprite("UI/Ugui/AttributesPaintedV2/" + name);

        private static Image CreatePaintedPanel(string name, UnityEngine.Transform parent,
            float x, float y, float width, float height, string asset)
        {
            var image = QdaoUguiFactory.CreateImage(name, parent, x, y, width, height, LoadPainted(asset));
            SkinPainted(image, asset);
            return image;
        }

        private static void SkinPainted(Image image, string asset)
        {
            var sprite = LoadPainted(asset);
            image.sprite = sprite;
            image.color = Color.white;
            image.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = image.type == Image.Type.Simple;
            // 精确切图的边距随显示高度等比缩放，仅九宫格中心伸缩。
            image.pixelsPerUnitMultiplier = Mathf.Max(0.01f,
                sprite.rect.height / Mathf.Max(1f, image.rectTransform.rect.height));
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>给一行挂 hover 事件:显示服务器下发的维度说明。</summary>
        private void AttachRowTooltip(UiPointRow row)
        {
            if (row.Hover == null) return;
            row.Hover.OnEnter = () => ShowTooltipFor(row);
            row.Hover.OnExit = HideTooltip;
        }

        // ── 显隐 ────────────────────────────────────────────

        public void Show()
        {
            // 属性窗与本窗同位置同尺寸,必须互斥(见 AttributeUiRoot.HidePanel)
            AttributeUiRoot.Instance?.HidePanel();
            if (_root != null) _root.gameObject.SetActive(true);
            HideTooltip();
            SetStatus(string.Empty);
            _owner.Client?.RequestList();
        }

        public void Hide()
        {
            HideTooltip();
            if (_detailsRoot != null) _detailsRoot.gameObject.SetActive(false);
            if (_root != null) _root.gameObject.SetActive(false);
        }

        public void Toggle()
        {
            if (IsVisible) Hide();
            else Show();
        }

        public void SetStatus(string value, bool warn = false)
        {
            if (_statusText == null) return;
            _statusText.text = value ?? string.Empty;
            _statusText.color = warn ? PetUiStyle.WarnText : PetUiStyle.HintText;
        }

        // ── 数据应用 ────────────────────────────────────────

        /// <summary>服务器列表到达:整体重建界面状态,丢弃所有未提交增量。</summary>
        public void ApplyList(PetListInfo list)
        {
            if (list == null) return;
            _list = list;
            HideTooltip();
            _detailsRoot.gameObject.SetActive(false);

            // 选中项失效(第一次打开 / 宝宝被换掉)时优先选出战中的那只,否则选第一只
            if (_selectedPetId == 0 || FindPet(_selectedPetId) == null)
            {
                _selectedPetId = list.ActivePetId != 0 && FindPet(list.ActivePetId) != null
                    ? list.ActivePetId
                    : (list.Pets.Count > 0 ? list.Pets[0].PetId : 0);
            }

            RefreshPetItems();
            RefreshDerived();
            RebuildRows();
            RefreshActionButtons();
        }

        /// <summary>在途状态变化:按钮与加点行一起进 / 出 Busy(失败回包后必须解锁)。</summary>
        public void ApplyBusy(bool busy)
        {
            RefreshRowRanges();
            RefreshActionButtons();
        }

        /// <summary>自动加点建议:只填本地待提交值,玩家点「确认」才落。</summary>
        public void ApplyAutoSuggestion(ulong petId, IReadOnlyDictionary<uint, uint> suggested)
        {
            if (petId != _selectedPetId) return;
            // 推荐是按服务器权威状态算的:先把本地未提交增量全部回落(否则各行滑条还留着上一次
            // 互相挤占后的旧上界,推荐值会被夹成错的),再从 committed 起写入
            RebuildRows();
            foreach (var row in ActiveRows())
            {
                if (suggested.TryGetValue(row.DimensionId, out uint target))
                {
                    row.SetPending(target);
                }
            }
            RefreshRowRanges();
            RefreshRemainText();
            RefreshActionButtons();
            SetStatus("已按推荐方案预分配,点「确认」生效。");
        }

        private void RefreshPetItems()
        {
            EnsurePetItems(_list.Pets.Count);
            _carryText.text = $"携带数量：{_list.Pets.Count}/{_list.MaxPets}";
            _emptyText.gameObject.SetActive(_list.Pets.Count == 0);
            _rosterContent.sizeDelta = new Vector2(PetUiStyle.LeftW,
                Mathf.Max(PetUiStyle.RosterHeight, _list.Pets.Count *
                    (PetUiStyle.PetItemHeight + PetUiStyle.PetItemGap) - PetUiStyle.PetItemGap));
            for (int i = 0; i < _petItems.Count; i++)
            {
                bool visible = i < _list.Pets.Count;
                _petItems[i].SetVisible(visible);
                if (!visible) { _petItemIds[i] = 0; continue; }
                var pet = _list.Pets[i];
                _petItemIds[i] = pet.PetId;
                bool selected = pet.PetId == _selectedPetId;
                _petItems[i].SetText(pet.Name);
                SkinPainted(_petItems[i].Plate, selected ? "pet_card_selected" : "pet_card_normal");
                _petItems[i].Label.color = selected ? QdaoRefreshArt.Ivory : PetUiStyle.FieldLabel;
                _petLevels[i].text = pet.Level.ToString();
                _petPortraits[i].sprite = PortraitAsset(pet.ModelId);
                _petStates[i].text = pet.IsActive ? "参战" : "休息";
                _petStates[i].color = selected ? QdaoRefreshArt.Ivory : PetUiStyle.HintText;
                _petDescriptions[i].text = $"成长 {pet.Growth / 100f:0.##}%";
                _petDescriptions[i].color = selected ? QdaoRefreshArt.Ivory : PetUiStyle.HintText;
            }
        }

        private static Sprite PortraitAsset(uint modelId)
        {
            // data/Pet.xlsx：1001 灵狐、1004 云鹤。石灵/金猊无对应新素材，
            // 不按可改名字段或列表序号误绑葫团团/符小虎。
            return modelId switch
            {
                1001 => LoadPainted("portrait_lingyue"),
                1004 => LoadPainted("portrait_yunjiujiu"),
                _ => QdaoRefreshArt.Load("round_badge_taiji")
            };
        }

        private void RefreshDerived()
        {
            var pet = FindPet(_selectedPetId);
            if (pet == null)
            {
                _levelValue.text = "等级 —";
                _growthValue.text = "成长率 —";
                for (int i = 0; i < _statValues.Length; i++) SetStat(i, "-");
                return;
            }
            _levelValue.text = $"等级　{pet.Level}";
            // 成长率是万分比(10000 = 100%),服务器算好给,这里只做展示换算
            _growthValue.text = $"成长率　{pet.Growth / 100f:0.##}%";

            var derived = pet.Derived;
            if (derived == null)
            {
                for (int i = 0; i < _statValues.Length; i++) SetStat(i, "—");
                return;
            }
            // 气血/法力显示「当前/上限」,其余单值(与角色面板同排布)
            SetStat(0, $"{derived.Health}/{derived.MaxHealth}");
            SetStat(1, $"{derived.Mana}/{derived.MaxMana}");
            SetStat(2, derived.PhysicalAttack.ToString());
            SetStat(3, derived.MagicAttack.ToString());
            SetStat(4, derived.Speed.ToString());
            SetStat(5, derived.Defense.ToString());
        }

        private void SetStat(int index, string value)
        {
            if (index >= 0 && index < _statValues.Length && _statValues[index] != null)
            {
                _statValues[index].text = value;
            }
        }

        private void RebuildRows()
        {
            var pet = FindPet(_selectedPetId);
            bool interactable = pet != null && !(_owner.Client?.Busy ?? false);
            int index = 0;
            if (pet != null)
            {
                foreach (var dimension in pet.Dimensions)
                {
                    if (index >= MaxRows) break;
                    var row = _rows[index];
                    row.Rect.gameObject.SetActive(true);
                    row.SetFromServer(dimension.DimensionId, dimension.Name, dimension.Value,
                        dimension.Allocated, dimension.Cap, pet.RemainingPoints, interactable);
                    row.Rect.name = $"Row_{dimension.Name}";
                    index++;
                }
            }
            for (int i = index; i < MaxRows; i++)
            {
                _rows[i].Rect.gameObject.SetActive(false);
            }
            float rowSpacing = index > 4 ? 60f : 92f;
            for (int i = 0; i < index; i++)
            {
                Place(_rows[i].Rect, PetUiStyle.MidX + i % 2 * 592f,
                    (index > 4 ? 474f : 490f) + i / 2 * rowSpacing, 530f, RowHeight);
                _rows[i].Rect.localScale = index > 4 ? new UnityEngine.Vector3(1f, 0.8f, 1f) : UnityEngine.Vector3.one;
            }
            RefreshRemainText();
        }

        private IEnumerable<UiPointRow> ActiveRows()
        {
            foreach (var row in _rows)
            {
                if (row.Rect != null && row.Rect.gameObject.activeSelf) yield return row;
            }
        }

        private uint PendingDelta()
        {
            uint delta = 0;
            foreach (var row in ActiveRows())
            {
                delta += row.Pending - row.Committed;
            }
            return delta;
        }

        private uint RemainingAfterPending()
        {
            var pet = FindPet(_selectedPetId);
            if (pet == null) return 0;
            uint delta = PendingDelta();
            return pet.RemainingPoints > delta ? pet.RemainingPoints - delta : 0;
        }

        private void RefreshRowRanges()
        {
            var pet = FindPet(_selectedPetId);
            if (pet == null) return;
            uint left = RemainingAfterPending();
            bool interactable = !(_owner.Client?.Busy ?? false);
            foreach (var row in ActiveRows())
            {
                row.ApplyRange(FindCap(row.DimensionId), left, interactable);
            }
        }

        private void RefreshRemainText()
        {
            if (_remainText == null) return;
            var pet = FindPet(_selectedPetId);
            if (pet == null)
            {
                _remainText.text = "未分配　—";
                return;
            }
            uint left = RemainingAfterPending();
            uint delta = PendingDelta();
            _remainText.text = delta > 0
                ? $"未分配　{left}　<color=#2685B2>预分配 +{delta}</color>"
                : $"未分配　{left}";
        }

        private void RefreshActionButtons()
        {
            var pet = FindPet(_selectedPetId);
            bool busy = _owner.Client?.Busy ?? false;
            bool hasPet = pet != null;
            uint delta = PendingDelta();

            _confirmButton?.SetInteractable(!busy && hasPet && delta > 0);
            _autoButton?.SetInteractable(!busy && hasPet && (pet?.RemainingPoints ?? 0) > 0);
            _resetButton?.SetInteractable(!busy && hasPet);
            _resetButton?.SetText(pet != null && pet.ResetCostGold > 0 ? $"洗点 · {pet.ResetCostGold} 金" : "一键洗点");
            _discardButton?.SetInteractable(!busy && delta > 0);
            _renameConfirm?.SetInteractable(!busy && hasPet);
            if (_renameInput != null) _renameInput.interactable = !busy && hasPet;
            foreach (var item in _petItems) item.SetInteractable(!busy);

            // 出战按钮在"出战 / 收回"之间切:选中的就是出战中那只时按钮变收回
            bool selectedIsActive = pet != null && pet.IsActive;
            _summonButton?.SetText(selectedIsActive ? "收 回" : "出 战");
            _summonButton?.SetInteractable(!busy && hasPet);
        }

        private PetInfo FindPet(ulong petId)
        {
            if (_list == null) return null;
            foreach (var pet in _list.Pets)
            {
                if (pet.PetId == petId) return pet;
            }
            return null;
        }

        private uint FindCap(uint dimensionId)
        {
            var pet = FindPet(_selectedPetId);
            if (pet == null) return 0;
            foreach (var dimension in pet.Dimensions)
            {
                if (dimension.DimensionId == dimensionId) return dimension.Cap;
            }
            return 0;
        }

        private string FindDesc(uint dimensionId)
        {
            var pet = FindPet(_selectedPetId);
            if (pet == null) return string.Empty;
            foreach (var dimension in pet.Dimensions)
            {
                if (dimension.DimensionId == dimensionId)
                {
                    // 资质是宝宝独有的一条信息,直接并进 tooltip:玩家最想知道的就是
                    // "这一点加下去,我这只宝宝比别人多长多少"
                    return dimension.Aptitude > 0
                        ? $"{dimension.Desc}(资质 {dimension.Aptitude / 100f:0.##}%)"
                        : dimension.Desc;
                }
            }
            return string.Empty;
        }

        // ── 交互 ────────────────────────────────────────────

        private void OnRowChanged(UiPointRow row)
        {
            RefreshRowRanges();
            RefreshRemainText();
            RefreshActionButtons();
        }

        private void OnPetItemClicked(int index)
        {
            if (index < 0 || index >= _petItemIds.Count) return;
            ulong petId = _petItemIds[index];
            if (petId == 0 || petId == _selectedPetId) return;
            if (PendingDelta() > 0)
            {
                SetStatus("有未确认的加点,请先「确认加点」或「重置」。", true);
                return;
            }
            _selectedPetId = petId;
            HideTooltip();
            _detailsRoot.gameObject.SetActive(false);
            SetStatus(string.Empty);
            RefreshPetItems();
            RefreshDerived();
            RebuildRows();
            RefreshActionButtons();
        }

        // 改名和出战/收回的成功回包都会刷新整份列表，提交前保护尚未确认的加点。
        private bool CanRefreshSelectedPet()
        {
            if (PendingDelta() == 0) return true;
            SetStatus("有未确认的加点，请先「确认加点」或「重置」。", true);
            return false;
        }

        private void OnSummonClicked()
        {
            var client = _owner.Client;
            if (client == null) { SetStatus("宝宝模块未就绪", true); return; }
            var pet = FindPet(_selectedPetId);
            if (pet == null) { SetStatus("请先选中一只宝宝。", true); return; }
            if (!CanRefreshSelectedPet()) return;
            if (pet.IsActive)
            {
                client.Recall();
                SetStatus("正在收回…");
                return;
            }
            client.Summon(_selectedPetId);
            SetStatus("正在出战…");
        }

        private void OnConfirmClicked()
        {
            var client = _owner.Client;
            if (client == null) { SetStatus("宝宝模块未就绪", true); return; }
            var target = new Dictionary<uint, uint>();
            foreach (var row in ActiveRows())
            {
                if (row.Pending != row.Committed)
                {
                    target[row.DimensionId] = row.Pending;
                }
            }
            if (target.Count == 0)
            {
                SetStatus("没有需要提交的加点。", true);
                return;
            }
            client.Allocate(_selectedPetId, target);
            SetStatus("已提交,等待服务器确认…");
        }

        private void OnResetClicked()
        {
            var client = _owner.Client;
            if (client == null) { SetStatus("宝宝模块未就绪", true); return; }
            if (_selectedPetId == 0) { SetStatus("请先选择一只宝宝。", true); return; }
            client.ResetPoints(_selectedPetId);
            SetStatus("已请求洗点,等待服务器确认…");
        }

        private void OnAutoClicked()
        {
            var client = _owner.Client;
            if (client == null) { SetStatus("宝宝模块未就绪", true); return; }
            if (_selectedPetId == 0) { SetStatus("请先选中一只宝宝。", true); return; }
            client.AutoAllocate(_selectedPetId);
            SetStatus("正在计算推荐方案…");
        }

        private void ShowTooltipFor(UiPointRow row)
        {
            string desc = FindDesc(row.DimensionId);
            if (string.IsNullOrEmpty(desc) || _tooltip == null) return;
            _tooltipText.text = desc;
            // 贴在该行下方偏左
            var anchored = row.Rect.anchoredPosition;
            _tooltip.anchoredPosition = new Vector2(anchored.x + 60f, anchored.y - RowHeight + 4f);
            _tooltip.SetAsLastSibling();
            _tooltip.gameObject.SetActive(true);
        }

        private void HideTooltip()
        {
            if (_tooltip != null) _tooltip.gameObject.SetActive(false);
        }
    }
}
