using System;
using MmorpgClient.UI.Ugui.Skills;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Attribute
{
    using Transform = UnityEngine.Transform;
    using Vector2 = UnityEngine.Vector2;
    /// <summary>04/05/06动态人物页。独立控件按已交付render坐标重建,窗外不加场景或遮罩。</summary>
    public sealed class CharacterSupportWindow
    {
        private static readonly Color Ink = Hex("34483b"), Green = Hex("1e604c"), Muted = Hex("655840"), Cream = Hex("fbf2d7");
        private readonly AttributeUiRoot _owner;
        private readonly RectTransform _root, _overview, _effects, _details, _popup, _sourceRows, _renamePopup;
        private readonly Text _title, _overviewName, _overviewLevel, _overviewExpText, _detailName, _detailLevel, _skillName, _skillCategory;
        private readonly Text[] _values = new Text[6], _detailValues = new Text[4], _detailLabels = new Text[4];
        private readonly Text _healing, _shield, _rules, _rulesSecond, _status, _sourceTitle, _sourceTotal, _renameStatus;
        private readonly Image _overviewPortrait, _detailPortrait, _skillIcon, _overviewExpFill;
        private readonly Button _healChoice, _shieldChoice;
        private readonly Button _allocation, _allocationTab, _renameButton, _renameConfirmButton;
        private readonly TMP_InputField _renameInput;
        private AttributePanelInfo _panel;
        private SkillBookViewState _lastBook;
        private BagInfo _lastEquipment;
        private SupportEffectSummary _lastSupport;
        private string _identity, _appearance;
        private uint _skillId;
        private bool _sourceOpen, _sourceShield, _renameOpen;
        private bool _battleReadOnly;
        public RectTransform Root => _root;
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        public CharacterSupportWindow(AttributeUiRoot owner, Transform parent)
        {
            _owner = owner;
            _root = Rect("CharacterSupportWindow", parent, 0, 0, 2560, 1080);
            Picture("WindowFrame", _root, 133, 94, 2294, 921, "window_frame", true, true);
            _root.gameObject.AddComponent<MmorpgClient.World.Tianyong.GameplayInputBlocker>();
            Picture("TitlePlate", _root, 955, 54, 650, 110, "title_plate");
            Picture("Taiji", _root, 1252, 23, 56, 56, "round_badge_taiji");
            _title = Label("Title", _root, 955, 81, 650, 58, "人物属性", 46, Cream, TextAnchor.UpperCenter);
            Action("Close", _root, 2350, 106, 61, 72, "", "close-jade", Hide, false);

            _overview = Rect("OverviewPage", _root, 0, 0, 2560, 1080);
            Picture("BaseCard", _overview, 179, 182, 799, 695, "application_card", true);
            Plate(_overview, "基础属性", 168, 160);
            _overviewPortrait = Portrait(_overview, 218, 217, 133);
            _overviewName = Label("RoleName", _overview, 380, 238, 390, 52, "—", 40, Ink);
            _renameButton = Action("RenameRole", _overview, 782, 236, 152, 54, "改 名", "button_secondary", OpenRenamePopup);
            _overviewLevel = Label("RoleLevel", _overview, 382, 294, 500, 34, "等级 —", 25, Muted);
            var expTrack = Rect("RoleExpTrack", _overview, 382, 332, 460, 8).gameObject.AddComponent<Image>();
            expTrack.color = new Color(0.76f, 0.78f, 0.70f, 0.90f); expTrack.raycastTarget = false;
            _overviewExpFill = Rect("RoleExpFill", _overview, 382, 332, 0, 8).gameObject.AddComponent<Image>();
            _overviewExpFill.color = Green; _overviewExpFill.raycastTarget = false; _overviewExpFill.enabled = false;
            _overviewExpText = Label("RoleExpText", _overview, 382, 342, 550, 26, "经验 —", 20, Muted);
            string[] names = { "气血", "法力", "物理攻击", "法术攻击", "防御", "速度" };
            for (int i = 0; i < names.Length; ++i)
            {
                float x = 215 + i % 2 * 364, y = 371 + i / 2 * 122;
                Picture("AttributeRow" + i, _overview, x, y, 339, 101, "mail_row_normal", true);
                Label("AttributeName" + i, _overview, x + 24, y + 12, 291, 36, names[i], 28, Ink);
                _values[i] = Label("AttributeValue" + i, _overview, x + 24, y + 51, 291, 36, "—", 27, Green);
            }
            _allocation = Action("Allocation", _overview, 215, 779, 218, 57, "四维加点", "button_secondary", OpenAllocation);
            Action("SwitchAppearance", _overview, 455, 779, 218, 57, "易容形象", "button_secondary", OpenAppearancePanel);
            _effects = Rect("SupportEffects", _overview, 0, 0, 2560, 1080);
            Picture("EffectsCard", _effects, 1008, 182, 1369, 695, "application_card", true);
            Plate(_effects, "辅助效果", 997, 160);
            _healing = EffectRow(false, 235);
            _shield = EffectRow(true, 386);
            Label("HealingExplanation", _effects, 1067, 568, 1240, 34, "施放治疗加成影响你为目标恢复的气血。", 25, Muted);
            Label("ShieldExplanation", _effects, 1067, 615, 1240, 34, "护盾容量加成影响你为目标施加的护盾容量。", 25, Muted);
            Label("SourceExplanation", _effects, 1067, 662, 1240, 62, "治疗与护盾分别结算；装备来源上限不限制被动与临时效果。未知来源显示—。", 25, Muted);

            _details = Rect("SupportSkillDetails", _root, 0, 0, 2560, 1080);
            Picture("SkillListCard", _details, 172, 178, 535, 706, "application_card", true);
            Plate(_details, "人物技能", 163, 154);
            Picture("SkillDetailCard", _details, 732, 178, 1526, 706, "application_card", true);
            Plate(_details, "技能详情", 723, 154);
            Picture("IdentityCard", _details, 188, 215, 503, 146, "mail_row_normal", true);
            _detailPortrait = Portrait(_details, 202, 223, 130);
            _detailName = Label("DetailRoleName", _details, 356, 237, 310, 44, "—", 34, Green);
            _detailLevel = Label("DetailRoleLevel", _details, 356, 292, 310, 34, "等级 —", 24, Muted);
            Label("SkillListTitle", _details, 211, 392, 420, 40, "技能详情", 30, Green);
            _healChoice = SkillChoice(false, 443);
            _shieldChoice = SkillChoice(true, 586);
            Action("AttributeTab", _details, 2277, 204, 112, 94, "属性", "mail_tab_normal", ShowOverview);
            _allocationTab = Action("AllocationTab", _details, 2277, 320, 112, 94, "加点", "mail_tab_normal", OpenAllocation);
            Action("SkillBookTab", _details, 2277, 436, 112, 94, "技能", "mail_tab_selected", () => { Hide(); SkillUiRoot.Open(); });
            _skillIcon = Picture("SkillIcon", _details, 785, 220, 144, 144, null);
            _skillName = Label("SkillName", _details, 962, 220, 960, 56, "—", 43, Green);
            _skillCategory = Label("SkillCategory", _details, 965, 281, 960, 38, "—", 27, Muted);
            Action("EffectSources", _details, 1972, 237, 214, 60, "效果来源", "button_secondary", () => ShowSource(_skillId == CharacterSupportModel.ShieldSkillId));
            for (int i = 0; i < 4; ++i)
            {
                float y = 395 + i * 75;
                Picture("SkillDetailRow" + i, _details, 783, y, 1406, 63, "mail_row_normal", true, false, .85f);
                _detailLabels[i] = Label("SkillDetailLabel" + i, _details, 810, y + 16, 450, 35, "—", 25, Muted);
                _detailValues[i] = Label("SkillDetailValue" + i, _details, 1285, y + 15, 875, 38, "—", 27, Ink);
            }
            Plate(_details, "技能规则", 775, 718);
            _rules = Label("PrimaryRule", _details, 806, 784, 1400, 42, "", 28, Green);
            _rulesSecond = Label("SecondaryRule", _details, 806, 830, 1400, 38, "", 25, Ink);
            _status = Label("Status", _root, 805, 925, 1500, 52, "", 25, Muted);

            _popup = Rect("EffectSourcePopup", _root, 0, 0, 2560, 1080);
            Picture("SourceFrame", _popup, 1020, 209, 1315, 635, "window_frame", true, true, .66f);
            Picture("SourceTitlePlate", _popup, 1447, 170, 468, 79, "title_plate");
            Label("SourceHeading", _popup, 1447, 189, 468, 43, "效果来源", 32, Cream, TextAnchor.UpperCenter);
            Action("CloseSources", _popup, 2260, 230, 45, 53, "", "close-jade", CloseSource, false);
            _sourceTitle = Label("SourceEffectName", _popup, 1093, 284, 1000, 48, "", 36, Ink);
            Label("ContributionLabel", _popup, 2030, 296, 199, 32, "贡献值", 23, Muted, TextAnchor.UpperRight);
            var viewport = Rect("SourceViewport", _popup, 1093, 344, 1153, 300);
            var hit = viewport.gameObject.AddComponent<Image>(); hit.color = new Color(1, 1, 1, 0.001f);
            viewport.gameObject.AddComponent<RectMask2D>();
            _sourceRows = Rect("SourceRows", viewport, 0, 0, 1153, 300);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = _sourceRows;
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 45;
            Picture("TotalBackground", _popup, 1093, 658, 1153, 61, "mail_tab_selected", true);
            Label("TotalLabel", _popup, 1121, 670, 700, 40, "合计", 29, Cream);
            _sourceTotal = Label("SourceTotal", _popup, 1900, 670, 322, 40, "—", 29, Cream, TextAnchor.UpperRight);
            Label("SourceNote", _popup, 1121, 744, 1090, 65, "单件贡献不等于封顶后的生效值；未同步项目不按零计入合计。", 21, Muted);

            _renamePopup = Rect("RoleRenamePopup", _root, 0, 0, 2560, 1080);
            var renameScrim = _renamePopup.gameObject.AddComponent<Image>();
            renameScrim.color = new Color(0.02f, 0.04f, 0.03f, 0.56f);
            Picture("RenameFrame", _renamePopup, 830, 310, 900, 440, "window_frame", true, true, .66f);
            Picture("RenameTitlePlate", _renamePopup, 1046, 271, 468, 79, "title_plate");
            Label("RenameHeading", _renamePopup, 1046, 290, 468, 43, "修改角色名", 32, Cream, TextAnchor.UpperCenter);
            Action("CloseRename", _renamePopup, 1656, 331, 45, 53, "", "close-jade", HideRenamePopup, false);
            Label("RenameRuleHint", _renamePopup, 900, 376, 760, 62,
                "需消耗背包中的「改名卡」，每次改名冷却 7 天，旧名保留保护 7 天；角色名仅限汉字、字母与数字。", 23, Muted);
            _renameInput = QdaoUguiFactory.CreateInputField("RoleRenameInput", _renamePopup, 900, 456, 760, 74,
                "请输入新的角色名", 32, CharacterPetArt.Get("mail_row_normal"));
            var renameBg = _renameInput.GetComponent<Image>();
            if (renameBg != null) { renameBg.type = Image.Type.Sliced; renameBg.pixelsPerUnitMultiplier = 1.15f; }
            _renameInput.textComponent.fontSize = 30f;
            _renameStatus = Label("RenameStatus", _renamePopup, 900, 548, 760, 54, string.Empty, 23, Green);
            _renameConfirmButton = Action("ConfirmRoleRename", _renamePopup, 950, 624, 300, 62, "确认改名", "button_primary", SubmitRoleRename);
            Action("CancelRoleRename", _renamePopup, 1310, 624, 300, 62, "取 消", "button_secondary", HideRenamePopup);
            Hide();
        }

        public void ShowOverview() { _skillId = 0; _sourceOpen = false; _renameOpen = false; Open(); }
        public void ShowSkill(uint id)
        {
            if (id != CharacterSupportModel.HealingSkillId && id != CharacterSupportModel.ShieldSkillId) return;
            _skillId = id; _sourceOpen = false; _renameOpen = false; Open();
            var source = _owner?.SkillSource;
            if (source != null && !(source.State?.IsBusy ?? false)) source.Refresh(SkillBookContext.Character());
        }
        public void ShowSource(bool shield)
        {
            _sourceShield = shield; _sourceOpen = true; _renameOpen = false; Open();
            _owner?.FeatureSource?.RequestEquipment();
        }
        private void Open()
        {
            _owner?.HideLegacyPanel();
            CityPanelRegistry.HideOthers(_owner);
            _root.gameObject.SetActive(true);
            RefreshView();
        }
        private void OpenAllocation() { if (_owner?.BattleReadOnly == true) return; Hide(); _owner?.OpenPanel(true); }
        private void OpenAppearancePanel() { Hide(); MmorpgClient.UI.Ugui.Gameplay.AppearanceUiRoot.Instance?.ShowPanel(); }
        public void Hide() { _sourceOpen = false; _renameOpen = false; _root.gameObject.SetActive(false); }
        public void Back() { if (_renameOpen) HideRenamePopup(); else if (_sourceOpen) CloseSource(); else Hide(); }
        private void CloseSource() { _sourceOpen = false; RefreshView(); }
        public void ResetSession() { _panel = null; _lastBook = null; _lastEquipment = null; _lastSupport = null; _identity = _appearance = null; _renameOpen = false; Hide(); }
        private void OpenRenamePopup()
        {
            if (_battleReadOnly || string.IsNullOrEmpty(_identity)) return;
            _renameOpen = true;
            _renameInput.SetTextWithoutNotify(_identity);
            _renameStatus.color = Muted;
            _renameStatus.text = string.Empty;
            _renameConfirmButton.interactable = true;
            RefreshView();
            _renameInput.ActivateInputField();
        }
        private void HideRenamePopup()
        {
            _renameOpen = false;
            RefreshView();
        }
        private void SubmitRoleRename()
        {
            if (_owner == null) return;
            _renameConfirmButton.interactable = false;
            bool sent = _owner.TryRequestRoleRename(_renameInput.text, (ok, msg) =>
            {
                _renameStatus.color = ok ? Green : Muted;
                _renameStatus.text = msg ?? string.Empty;
                if (!ok) _renameConfirmButton.interactable = true;
            });
            if (!sent) _renameConfirmButton.interactable = true;
        }
        public void HandleRoleRenameOutcome(bool succeeded, string message)
        {
            if (_renameConfirmButton != null) _renameConfirmButton.interactable = !_battleReadOnly;
            if (_renameStatus != null)
            {
                _renameStatus.color = succeeded ? Green : Muted;
                _renameStatus.text = message ?? string.Empty;
            }
            if (succeeded)
            {
                _renameOpen = false;
                RefreshView();
            }
            SetStatus(message);
        }
        public void ApplyPanel(AttributePanelInfo panel) { _panel = panel; if (IsVisible) RefreshView(); }
        public void SetStatus(string message) { _status.text = message ?? string.Empty; }
        public void RefreshState()
        {
            if (!IsVisible) return;
            string identity = _owner?.RoleName, appearance = _owner?.RoleAppearanceId;
            var book = _owner?.SkillSource?.State;
            var equipment = _owner?.FeatureSource?.Equipment;
            var support = _owner?.BattleReadOnly == true ? _owner.BattleSupportEffects : _panel?.SupportEffects;
            if (!ReferenceEquals(_lastSupport, support) || _battleReadOnly != (_owner?.BattleReadOnly == true) || _identity != identity || _appearance != appearance || !ReferenceEquals(_lastBook, book) || !ReferenceEquals(_lastEquipment, equipment))
                RefreshView();
        }
        private void RefreshView()
        {
            _battleReadOnly = _owner?.BattleReadOnly == true;
            _lastSupport = _battleReadOnly ? _owner?.BattleSupportEffects : _panel?.SupportEffects;
            _allocation.interactable = _allocationTab.interactable = !_battleReadOnly;
            _lastBook = _owner?.SkillSource?.State;
            var features = _owner?.FeatureSource;
            _lastEquipment = features != null && features.SnapshotPlayerId == _owner?.Client?.MyPlayerId ? features.Equipment : null;
            _identity = _owner?.RoleName; _appearance = _owner?.RoleAppearanceId;
            _overviewName.text = _detailName.text = string.IsNullOrWhiteSpace(_identity) ? "—" : _identity;
            _overviewLevel.text = _detailLevel.text = _panel == null ? "等级 —" : CharacterSupportModel.Number(_panel.Level) + "级";
            var expSnap = MmorpgClient.Game.CoreLoop.PlayerProgressionModels.BuildExpSnapshot(_panel);
            _overviewExpText.text = expSnap == null ? "经验 —" : expSnap.ExpProgressText;
            float expFillW = expSnap == null ? 0f : 460f * Mathf.Clamp01((float)expSnap.ProgressRatio);
            _overviewExpFill.rectTransform.sizeDelta = new Vector2(expFillW, 8f);
            _overviewExpFill.enabled = expFillW > 0f;
            var portrait = string.IsNullOrEmpty(_appearance) ? null : MmorpgClient.World.QdaoCharacterCatalog.LoadPortrait(_appearance);
            _overviewPortrait.sprite = _detailPortrait.sprite = portrait;
            _overviewPortrait.enabled = _detailPortrait.enabled = portrait != null;
            var values = CharacterSupportModel.Attributes(_panel);
            for (int i = 0; i < values.Length; ++i) _values[i].text = values[i];
            _healing.text = CharacterSupportModel.TotalBonus(_panel, false, _lastSupport, _battleReadOnly);
            _shield.text = CharacterSupportModel.TotalBonus(_panel, true, _lastSupport, _battleReadOnly);
            bool detail = _skillId != 0 && !_sourceOpen;
            _title.text = detail ? "人物技能" : "人物属性";
            _overview.gameObject.SetActive(!detail); _effects.gameObject.SetActive(!_sourceOpen);
            _details.gameObject.SetActive(detail); _popup.gameObject.SetActive(_sourceOpen);
            _renamePopup.gameObject.SetActive(_renameOpen);
            _renameButton.interactable = !_battleReadOnly && !string.IsNullOrEmpty(_identity);
            if (_battleReadOnly && _renameOpen) { _renameOpen = false; _renamePopup.gameObject.SetActive(false); }
            if (detail) RefreshSkill();
            if (_sourceOpen) RefreshSources();
            _status.text = _battleReadOnly ? "战斗中仅可查看，人物加点与技能修习已锁定。" : _panel == null ? "正在同步人物属性…" : detail ?
                (CharacterSupportModel.FindSkill(_lastBook, _skillId) == null ? "技能信息尚未同步，可返回技能簿刷新。" : "数值来自已同步技能资料，未下发的容量不作推算。") : string.Empty;
        }
        private void RefreshSkill()
        {
            bool shield = _skillId == CharacterSupportModel.ShieldSkillId;
            var entry = CharacterSupportModel.FindSkill(_lastBook, _skillId);
            _skillName.text = entry?.Definition?.name ?? (shield ? "太清灵盾" : "青莲润生");
            _skillCategory.text = shield ? "主动 · 护盾" : "主动 · 治疗";
            _skillIcon.sprite = CharacterPetArt.Skill(_skillId, 256); _skillIcon.enabled = _skillIcon.sprite != null;
            var labels = shield ? new[] { "主要效果", "作用目标", "护盾容量 / 吸收类型", "持续回合 / 耗蓝 / 冷却" } :
                new[] { "主要效果", "作用目标", "技能等级", "耗蓝 / 冷却" };
            var values = CharacterSupportModel.SkillValues(_lastBook, _skillId, _lastSupport, _battleReadOnly);
            for (int i = 0; i < 4; ++i) { _detailLabels[i].text = labels[i]; _detailValues[i].text = values[i]; }
            SelectChoice(_healChoice, !shield); SelectChoice(_shieldChoice, shield);
            _rules.text = shield ? "独立施放，占用本回合普通行动；不依赖治疗或溢出。" : "过量治疗不转为护盾。";
            _rulesSecond.text = shield ? "参考容量不含条件法宝；各目标独立，不读取目标缺血或减疗。" :
                "参考值不含目标增减疗、缺血封顶或条件法宝；副目标由服务器随机选取。";
        }
        private void RefreshSources()
        {
            _sourceTitle.text = _sourceShield ? "护盾容量加成" : "施放治疗加成";
            _sourceTotal.text = CharacterSupportModel.TotalBonus(_panel, _sourceShield, _lastSupport, _battleReadOnly);
            for (int i = _sourceRows.childCount - 1; i >= 0; --i)
            { var child = _sourceRows.GetChild(i); child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
            var sources = CharacterSupportModel.Sources(_panel, _lastEquipment, _sourceShield, _lastSupport, _battleReadOnly);
            float y = 0;
            for (int i = 0; i < sources.Count; ++i)
            {
                bool detailed = !string.IsNullOrEmpty(sources[i].Detail);
                float height = detailed ? 91 : 58;
                Picture("SourceRow" + i, _sourceRows, 0, y, 1153, height - 3, "mail_row_normal", true);
                Label("SourceName" + i, _sourceRows, 28, y + 12, 870, 34, sources[i].Name, 25, Ink);
                Label("SourceValue" + i, _sourceRows, 920, y + 11, 209, 34, sources[i].Value, 25, Green, TextAnchor.UpperRight);
                if (detailed) Label("SourceReason" + i, _sourceRows, 28, y + 48, 1095, 34, sources[i].Detail, 21, Muted);
                y += height;
            }
            _sourceRows.sizeDelta = new Vector2(1153, Mathf.Max(300, y));
        }
        private Text EffectRow(bool shield, float y)
        {
            Action(shield ? "ShieldDetail" : "HealingDetail", _effects, 1036, y, 1315, 127, "", "mail_row_normal",
                () => ShowSkill(shield ? CharacterSupportModel.ShieldSkillId : CharacterSupportModel.HealingSkillId));
            Label(shield ? "ShieldLabel" : "HealingLabel", _effects, 1071, y + 44, 620, 44,
                shield ? "护盾容量加成" : "施放治疗加成", 34, Ink);
            var value = Label(shield ? "ShieldBonus" : "HealingBonus", _effects, 1690, y + 40, 260, 49, "—", 38, Green, TextAnchor.UpperRight);
            Action(shield ? "ShieldSources" : "HealingSources", _effects, 2041, y + 34, 253, 58, "查看来源", "button_primary", () => ShowSource(shield));
            return value;
        }
        private Button SkillChoice(bool shield, float y)
        {
            uint id = shield ? CharacterSupportModel.ShieldSkillId : CharacterSupportModel.HealingSkillId;
            var button = Action(shield ? "ShieldSkill" : "HealSkill", _details, 188, y, 503, 124, "", "mail_row_normal", () => ShowSkill(id));
            var icon = Picture("Icon", button.transform, 19, 14, 96, 96, null); icon.sprite = CharacterPetArt.Skill(id, 128); icon.enabled = icon.sprite != null;
            Label("Name", button.transform, 138, 20, 340, 41, shield ? "太清灵盾" : "青莲润生", 30, Green);
            Label("Category", button.transform, 138, 67, 340, 34, shield ? "主动 · 护盾" : "主动 · 治疗", 24, Muted);
            return button;
        }
        private static void SelectChoice(Button button, bool selected)
        {
            button.image.sprite = CharacterPetArt.Get(selected ? "pet_card_selected" : "mail_row_normal");
            foreach (var label in button.GetComponentsInChildren<Text>()) label.color = selected ? Cream : label.name == "Name" ? Green : Muted;
        }
        private static Image Portrait(Transform parent, float x, float y, float size)
        {
            var clip = Rect("PortraitClip", parent, x + 5, y + 5, size - 10, size - 10);
            clip.gameObject.AddComponent<CanvasRenderer>();
            clip.gameObject.AddComponent<CharacterSupportPortraitMask>();
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var portrait = Picture("Portrait", clip, 0, 0, size - 10, size - 10, null);
            portrait.preserveAspect = true;
            Picture("PortraitRing", parent, x, y, size, size, "portrait_frame");
            return portrait;
        }
        private static void Plate(Transform parent, string text, float x, float y)
        {
            Picture(text + "Plate", parent, x, y, 282, 48, "section_plate");
            Label(text + "Heading", parent, x + 4, y + 8, 230, 36, text, 28, Cream, TextAnchor.UpperCenter);
        }
        private static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform)); var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); return rect;
        }
        private static Image Picture(string name, Transform parent, float x, float y, float w, float h, string key, bool sliced = false, bool raycast = false, float borderScale = 1f)
        {
            var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>();
            image.sprite = key == null ? null : CharacterPetArt.Get(key); image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = 1f / (key == "application_card" ? .85f : borderScale);
            image.raycastTarget = raycast; image.enabled = image.sprite != null; return image;
        }
        private static Text Label(string name, Transform parent, float x, float y, float w, float h, string text, int size, Color color, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var label = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Text>();
            label.font = CharacterPetArt.Font; label.fontSize = size; label.color = color; label.text = text;
            label.supportRichText = false; label.raycastTarget = false; label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true; label.resizeTextMinSize = Mathf.Max(16, size - 8); label.resizeTextMaxSize = size;
            return label;
        }
        private static Button Action(string name, Transform parent, float x, float y, float w, float h, string caption, string key, UnityEngine.Events.UnityAction callback, bool sliced = true)
        {
            var image = Picture(name, parent, x, y, w, h, key, sliced, true);
            if (key == "button_primary" || key == "button_secondary") image.pixelsPerUnitMultiplier = 1.25f;
            else if (key == "mail_tab_selected" || key == "mail_tab_normal") image.pixelsPerUnitMultiplier = 1f / .9f;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var colors = button.colors; colors.highlightedColor = new Color(1, .96f, .78f); colors.pressedColor = new Color(.75f, .85f, .77f); button.colors = colors;
            button.onClick.AddListener(callback);
            if (!string.IsNullOrEmpty(caption)) Label("Label", button.transform, 8, 0, w - 16, h, caption, 27,
                key == "button_primary" || key == "mail_tab_selected" ? Cream : Ink, TextAnchor.MiddleCenter);
            return button;
        }
        private static Color Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out var color); return color; }
    }

    /// <summary>原生圆形头像裁剪,不把预览人物或背景烘焙进控件。</summary>
    internal sealed class CharacterSupportPortraitMask : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var bounds = rectTransform.rect; var center = bounds.center;
            mesh.AddVert(center, Color.white, new Vector2(.5f, .5f));
            const int segments = 64;
            for (int i = 0; i <= segments; ++i)
            {
                float angle = i * Mathf.PI * 2 / segments;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                mesh.AddVert(center + Vector2.Scale(direction, bounds.size * .5f), Color.white, direction * .5f + Vector2.one * .5f);
                if (i > 0) mesh.AddTriangle(0, i, i + 1);
            }
        }
    }
}
