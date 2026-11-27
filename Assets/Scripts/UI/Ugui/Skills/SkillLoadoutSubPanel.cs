using System;
using System.Collections.Generic;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.UI.Ugui.Jubaozhai;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Skills
{
    /// <summary>
    /// 角色技能书「战法·加点」一体化流派装配子视图（7 套非永久方案槽位：开场 ×1 / 被动 ×3 / 追击 ×1 + 1:1 四维加点）。
    /// 严格消费权威 SkillBookViewState.Snapshot.Loadout，所有保存/切换/按套租用经 ISkillLoadoutSource 提交，
    /// 战斗中只读锁定写入，绝不推算服务端战斗数值。
    /// 优先加载 UI/Ugui/SkillBuildLoadoutV1/* 专属切图，未落盘时自动回退至既有 guild_* 切图保持可玩。
    /// </summary>
    public sealed class SkillLoadoutSubPanel
    {
        private static readonly Color Ink = QdaoUguiTheme.Html("#201D16");
        private static readonly Color Brown = QdaoUguiTheme.Html("#513722");
        private static readonly Color JadeInk = QdaoUguiTheme.Html("#103B29");
        private static readonly Color Rule = QdaoUguiTheme.Html("#CCBA95");
        private static readonly Color Warn = QdaoUguiTheme.Html("#893827");
        private static readonly Color PaperLight = QdaoUguiTheme.Html("#F7EFE0");
        private static readonly Color ModalBg = QdaoUguiTheme.Html("#F5ECDC");
        private const float TableWidth = 1324f;
        private const float ListHeight = 298f;
        private const float RowHeight = 52f;

        private readonly RectTransform _root;
        private readonly RectTransform _contentRoot;
        private readonly Func<ISkillLoadoutSource> _getSource;
        private readonly SkillLoadoutDraft _draft = new();

        private SkillBookViewState _state;
        private ISkillLoadoutSource _source;
        private ScrollRect _poolScroll;
        private UiTextButton _resetBtn;
        private TMP_Text _subHeaderText;
        private bool _battleLocked;
        private bool _saving;
        private bool _hasDraftSnapshot;
        private bool _rentModalOpen;
        private bool _copyPickerOpen;
        private uint _rentTargetSlots = 1u;
        private uint _rentAddPeriods = 1u;
        private int _operation;
        private string _notice;
        private bool _noticeError;

        public SkillLoadoutSubPanel(UnityEngine.Transform parent, Func<ISkillLoadoutSource> getSource)
        {
            _getSource = getSource;
            _root = QdaoUguiFactory.CreateRect("SkillLoadoutPanel", parent, 150f, 92f, 1360f, 764f);
            _contentRoot = QdaoUguiFactory.CreateRect("LoadoutContent", _root, 0f, 0f, 1360f, 764f);
            Hide();
        }

        public void Hide()
        {
            _rentModalOpen = false;
            _copyPickerOpen = false;
            _root.gameObject.SetActive(false);
        }

        public void Reset()
        {
            Hide();
            ResetSession();
        }

        private void ResetSession()
        {
            _operation++;
            _saving = false;
            _hasDraftSnapshot = false;
            _rentModalOpen = false;
            _copyPickerOpen = false;
            _draft.Reset(null, 1u);
            _notice = null;
            _noticeError = false;
        }

        public void Apply(SkillBookViewState state, bool battleLocked, bool visible)
        {
            _state = state;
            _battleLocked = battleLocked;
            var loadout = state?.Snapshot?.Loadout;
            var source = _getSource?.Invoke();
            if (!ReferenceEquals(_source, source) || (loadout == null && _hasDraftSnapshot))
            {
                ResetSession();
                _source = source;
            }

            bool versionChanged = loadout != null && (!_hasDraftSnapshot || _draft.StateVersion != loadout.StateVersion);
            if (versionChanged)
            {
                bool changedDraft = _draft.IsDirty && !_saving;
                _draft.Reset(loadout, ActiveSchemeId(loadout));
                _hasDraftSnapshot = true;
                if (changedDraft)
                {
                    _notice = "服务器方案已更新，已重新载入当前装配。";
                    _noticeError = true;
                }
            }
            _root.gameObject.SetActive(visible);
            if (visible) Rebuild(resetScroll: versionChanged);
        }

        private static uint MaxSchemeLimit(SkillLoadoutView loadout) =>
            loadout != null && loadout.MaxSchemes >= SkillLoadoutRentPlanner.BaseFreeSchemes
                ? Math.Min(loadout.MaxSchemes, SkillLoadoutRentPlanner.MaxSchemes)
                : SkillLoadoutRentPlanner.MaxSchemes;

        private static uint UnlockedSchemeLimit(SkillLoadoutView loadout)
        {
            uint max = MaxSchemeLimit(loadout);
            uint unlocked = loadout != null && loadout.UnlockedSchemes >= SkillLoadoutRentPlanner.BaseFreeSchemes
                ? loadout.UnlockedSchemes
                : SkillLoadoutRentPlanner.BaseFreeSchemes;
            return Math.Min(unlocked, max);
        }

        private bool CanWrite =>
            !_battleLocked && !_saving &&
            _state?.Context?.IsPet == false && _state.IsConnected && !_state.IsReadOnly && !_state.IsLoading && !_state.IsBusy &&
            _state.Snapshot?.Loadout != null && _draft.SchemeId <= UnlockedSchemeLimit(_state.Snapshot.Loadout) &&
            _source != null && ReferenceEquals(_source, _getSource?.Invoke());

        private bool CanRent =>
            !_saving &&
            _state?.Context?.IsPet == false && _state.IsConnected && !_state.IsReadOnly && !_state.IsLoading && !_state.IsBusy &&
            _state.Snapshot?.Loadout != null &&
            _source != null && ReferenceEquals(_source, _getSource?.Invoke());

        private static uint ActiveSchemeId(SkillLoadoutView loadout) =>
            loadout != null && loadout.ActiveSchemeId is >= 1u and <= SkillLoadoutRentPlanner.MaxSchemes
                ? loadout.ActiveSchemeId
                : 1u;

        private void Rebuild(bool resetScroll = false)
        {
            float poolOffset = !resetScroll && _poolScroll != null ? _poolScroll.content.anchoredPosition.y : 0f;
            _resetBtn = null;
            _subHeaderText = null;
            for (int i = _contentRoot.childCount - 1; i >= 0; --i)
            {
                var child = _contentRoot.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }

            var snapshot = _state?.Snapshot;
            var loadout = snapshot?.Loadout;
            if (loadout == null) return;

            bool canWrite = CanWrite;
            bool canChangeScheme = !_battleLocked && !_saving && !_draft.IsDirty && (_source == null || CanWrite || _draft.SchemeId > UnlockedSchemeLimit(loadout));

            BuildSchemeTabsRow(loadout, canChangeScheme);
            BuildIdentityAndRoleRow(loadout, canWrite);
            BuildAttributeBoxRow(loadout, canWrite);
            BuildSkillSlotsRow(snapshot, loadout);
            BuildLearnedPoolSection(snapshot, canWrite, poolOffset);
            BuildFooterRow(loadout, canWrite);

            if (_copyPickerOpen) BuildCopyPickerOverlay(loadout);
            if (_rentModalOpen) BuildRentModalOverlay(loadout);
        }

        private void BuildSchemeTabsRow(SkillLoadoutView loadout, bool canChangeScheme)
        {
            uint activeId = ActiveSchemeId(loadout);
            uint maxSchemes = MaxSchemeLimit(loadout);
            uint unlockedSchemes = UnlockedSchemeLimit(loadout);

            for (uint s = 1u; s <= maxSchemes; s++)
            {
                uint captured = s;
                var sc = loadout.FindScheme(s);
                bool isEditing = s == _draft.SchemeId;
                bool unlocked = s <= unlockedSchemes;
                bool hasSaved = sc != null;
                string tag = SchemeStatusTag(loadout, s, unlocked);
                string baseName = !string.IsNullOrEmpty(sc?.Name) ? sc.Name : SkillLoadoutView.DefaultSchemeName(s);
                string shortName = TruncateDisplay(baseName, 4);
                string activeStar = unlocked && s == activeId ? "★" : string.Empty;
                string label = $"{activeStar}{shortName}·{tag}";

                var btn = CharacterAttributeVisuals.Button("LoadoutScheme" + s, _contentRoot,
                    18f + (s - 1u) * 124f, 6f, 118f, 46f, label, isEditing, 16f);
                bool isRentedTab = unlocked && s > loadout.FreeSchemes;
                string customArt = isEditing
                    ? SkillLoadoutRentPlanner.ArtTabActive
                    : (!unlocked
                        ? SkillLoadoutRentPlanner.ArtTabLocked
                        : (isRentedTab ? SkillLoadoutRentPlanner.ArtTabRented : SkillLoadoutRentPlanner.ArtTabUnlocked));
                string fallbackArt = isEditing ? "guild_tab_selected" : "guild_tab_normal";
                bool useLightInk = isEditing || (isRentedTab && LoadCustomArt(customArt) != null);
                StyleCustomControl(btn, customArt, fallbackArt,
                    useLightInk ? QdaoRefreshArt.Ivory : (unlocked ? Ink : Brown), preserveReadOnlyColor: true);
                if (!unlocked && LoadCustomArt(customArt) != null && btn?.Label != null)
                    btn.Label.rectTransform.offsetMax -= new Vector2(20f, 0f);
                btn.SetInteractable(canChangeScheme || !unlocked);
                btn.Button.onClick.AddListener(() =>
                {
                    if (_battleLocked || _saving) return;
                    if (_draft.IsDirty)
                    {
                        _notice = "当前草稿有未保存改动，请先保存或重置后再切换方案。";
                        _noticeError = true;
                        Rebuild();
                        return;
                    }
                    _copyPickerOpen = false;
                    if (!unlocked)
                    {
                        if (hasSaved) _draft.Reset(loadout, captured);
                        _notice = hasSaved
                            ? $"方案{captured}当前未解锁：已存装备/战法/加点配置均永久保留（只读预览），达85级免费+1套、续租或开轻享卡即恢复，也可复制到已解锁方案。"
                            : $"方案{captured}未解锁（装备+战法+加点三合一共享）：达 85 级满级自动永久免费 +1 套（共 3 套），也可开轻享卡 +2 套或点右侧「管理槽位」按套租用（30灵钻/套/30天，到期只锁不删）。";
                        _noticeError = true;
                        Rebuild(resetScroll: hasSaved);
                        return;
                    }
                    if (!canChangeScheme || _draft.SchemeId == captured) return;
                    if (CanWrite && activeId != captured)
                    {
                        SubmitSwitch(captured);
                        return;
                    }
                    _notice = null;
                    _noticeError = false;
                    _draft.Reset(loadout, captured);
                    Rebuild(resetScroll: true);
                });
            }

            string rentStatus = loadout.IsRentActive
                ? $"已开{unlockedSchemes}/{maxSchemes} · {SkillLoadoutRentPlanner.FormatRemaining(loadout.RemainingRentSeconds)}"
                : (loadout.LightCardActive
                    ? $"已开{unlockedSchemes}/{maxSchemes} · 轻享卡+2"
                    : $"已开{unlockedSchemes}/{maxSchemes} · 可按套扩展");
            CreateText("LoadoutRentSummary", _contentRoot, 890f, 6f, 208f, 46f, rentStatus, 16f, JadeInk);

            var rentBtn = CharacterAttributeVisuals.Button("LoadoutRentBtn", _contentRoot,
                1102f, 8f, 116f, 42f, "管理槽位", true, 18f);
            StyleCustomControl(rentBtn, null, "guild_button_primary", QdaoRefreshArt.Ivory, preserveReadOnlyColor: true);
            rentBtn.SetInteractable(CanRent);
            rentBtn.Button.onClick.AddListener(() => OpenRentModal(loadout));

            var cardBtn = CharacterAttributeVisuals.Button("LoadoutLightCardBtn", _contentRoot,
                1224f, 8f, 118f, 42f, loadout.LightCardActive ? "轻享卡已开" : "轻享卡+2", false, 18f);
            StyleCustomControl(cardBtn, null, "guild_button_secondary", Ink, preserveReadOnlyColor: true);
            cardBtn.SetInteractable(!_saving);
            cardBtn.Button.onClick.AddListener(() =>
            {
                if (AfkPassUiRoot.Instance != null)
                {
                    AfkPassUiRoot.Instance.ShowPanel();
                }
                else
                {
                    _notice = "请在主城「仙途轻享卡」界面查看轻享卡附赠 2 套方案权益。";
                    _noticeError = false;
                    Rebuild();
                }
            });
        }

        private void BuildIdentityAndRoleRow(SkillLoadoutView loadout, bool canWrite)
        {
            CreateText("LoadoutNameLabel", _contentRoot, 18f, 58f, 48f, 36f, "名称", 19f, Brown);
            var input = QdaoUguiFactory.CreateInputField("LoadoutNameInput", _contentRoot,
                68f, 58f, 200f, 36f, "方案名称(1-12字)", SkillLoadoutRentPlanner.MaxNameCodePoints);
            if (input.targetGraphic is Image bg) bg.color = PaperLight;
            if (input.textComponent is TMP_Text txt)
            {
                txt.fontSize = 18f;
                txt.color = Ink;
                txt.richText = false;
            }
            if (input.placeholder is TMP_Text ph)
            {
                ph.fontSize = 16f;
                ph.richText = false;
            }
            input.SetTextWithoutNotify(_draft.Name ?? string.Empty);
            input.interactable = canWrite;
            input.onValueChanged.AddListener(value =>
            {
                if (!CanWrite) return;
                _draft.SetName(value);
                _resetBtn?.SetInteractable(CanWrite && _draft.IsDirty);
                if (_subHeaderText != null) _subHeaderText.text = FormatSubHeader(loadout);
            });

            CreateText("LoadoutRoleTitle", _contentRoot, 278f, 58f, 48f, 36f, "用途", 19f, Brown);
            for (uint role = 0u; role <= 4u; role++)
            {
                uint captured = role;
                bool selected = role == _draft.RoleTag;
                var tagBtn = CharacterAttributeVisuals.Button("LoadoutRoleTag" + role, _contentRoot,
                    328f + role * 84f, 58f, 78f, 36f, SkillLoadoutView.DescribeRoleTag(role), false, 18f);
                SkillPanel.StyleGuildControl(tagBtn, "guild_tab_normal", selected ? JadeInk : Ink, preserveReadOnlyColor: true);
                if (selected)
                {
                    tagBtn.Label.rectTransform.offsetMin += new Vector2(12f, 0f);
                    AddSelectedMark(tagBtn.Rect, 6f, 11f, 14f);
                }
                tagBtn.SetInteractable(canWrite);
                tagBtn.Button.onClick.AddListener(() =>
                {
                    if (!CanWrite || _draft.RoleTag == captured) return;
                    _draft.SetRoleTag(captured);
                    _notice = "用途已调整，保存后生效。";
                    _noticeError = false;
                    Rebuild();
                });
            }

            var copyBtn = CharacterAttributeVisuals.Button("LoadoutCopyBtn", _contentRoot,
                754f, 58f, 148f, 36f, "复制其他方案", false, 17f);
            StyleCustomControl(copyBtn, null, "guild_button_secondary", Ink);
            copyBtn.SetInteractable(canWrite && HasOtherCopyableScheme(loadout));
            copyBtn.Button.onClick.AddListener(() =>
            {
                if (!CanWrite) return;
                _copyPickerOpen = !_copyPickerOpen;
                _rentModalOpen = false;
                Rebuild();
            });

            _subHeaderText = CreateText("LoadoutSubHeader", _contentRoot, 912f, 58f, 430f, 36f,
                FormatSubHeader(loadout), 17f, JadeInk);
        }

        private void BuildAttributeBoxRow(SkillLoadoutView loadout, bool canWrite)
        {
            var attrBox = CreateCustomImage("LoadoutAttrBox", _contentRoot, 18f, 102f, TableWidth, 94f,
                SkillLoadoutRentPlanner.ArtAttrBox, "guild_announcement", QdaoUguiTheme.Html("#F0E7D7")).rectTransform;

            string ptsLabel = _draft.AttributeTotalPoints > 0u
                ? $"四维加点 · 剩 {_draft.AttributeRemaining}/{_draft.AttributeTotalPoints}"
                : "四维加点 · 暂无可分配点";
            CreateText("LoadoutAttrSummary", attrBox, 12f, 6f, 232f, 36f, ptsLabel, 18f, JadeInk, bold: true);

            bool canEditAttr = canWrite && _draft.AttributeTotalPoints > 0u;
            for (int i = 0; i < SkillBuildTemplates.AllDimensions.Length; i++)
            {
                uint dim = SkillBuildTemplates.AllDimensions[i];
                float bx = 248f + i * 268f;
                uint val = _draft.GetAttribute(dim);
                CreateText($"LoadoutAttrLabel_{dim}", attrBox, bx, 6f, 102f, 36f,
                    $"{SkillBuildTemplates.DimensionName(dim)} {val}", 18f, Ink, bold: true);

                BuildAttrDeltaButton(attrBox, dim, -10, "-10", bx + 102f, 9f, canEditAttr && val > 0u);
                BuildAttrDeltaButton(attrBox, dim, -1, "-1", bx + 142f, 9f, canEditAttr && val > 0u);
                BuildAttrDeltaButton(attrBox, dim, 1, "+1", bx + 182f, 9f, canEditAttr && _draft.AttributeRemaining > 0u);
                BuildAttrDeltaButton(attrBox, dim, 10, "+10", bx + 222f, 9f, canEditAttr && _draft.AttributeRemaining > 0u);
            }

            CreateText("LoadoutTemplateTitle", attrBox, 12f, 50f, 84f, 34f, "流派模板", 17f, Brown);
            for (int i = 0; i < SkillBuildTemplates.All.Length; i++)
            {
                int idx = i;
                var tpl = SkillBuildTemplates.All[i];
                var pill = CharacterAttributeVisuals.Button("LoadoutTemplate" + i, attrBox,
                    98f + i * 138f, 50f, 132f, 34f, tpl.Label, false, 16f);
                StyleCustomControl(pill, SkillLoadoutRentPlanner.ArtTemplatePill, "guild_tab_normal", Ink);
                pill.SetInteractable(canEditAttr);
                pill.Button.onClick.AddListener(() =>
                {
                    if (!CanWrite || !_draft.ApplyTemplate(idx)) return;
                    _notice = $"已套用「{tpl.Label}」（{tpl.Summary}），保存后生效。";
                    _noticeError = false;
                    Rebuild();
                });
            }

            if (_draft.NeedsAttributeReset)
            {
                string costText = loadout.AttributeResetCostGold > 0UL ? $"耗{loadout.AttributeResetCostGold}银两" : "免费";
                string toggleLabel = (_draft.ResetAttributeIfNeeded ? "[已确认洗点] " : "[点此确认洗点] ") + $"下调加点({costText})";
                var resetAttrBtn = CharacterAttributeVisuals.Button("LoadoutResetAttrToggle", attrBox,
                    932f, 49f, 380f, 36f, toggleLabel, _draft.ResetAttributeIfNeeded, 16f);
                StyleCustomControl(resetAttrBtn, null,
                    _draft.ResetAttributeIfNeeded ? "guild_tab_selected" : "guild_button_secondary",
                    _draft.ResetAttributeIfNeeded ? QdaoRefreshArt.Ivory : Warn, preserveReadOnlyColor: true);
                resetAttrBtn.SetInteractable(canWrite);
                resetAttrBtn.Button.onClick.AddListener(() =>
                {
                    if (!CanWrite) return;
                    _draft.SetResetAttributeIfNeeded(!_draft.ResetAttributeIfNeeded);
                    _notice = _draft.ResetAttributeIfNeeded
                        ? $"已确认本次保存时洗点重配（{costText}）。"
                        : "已取消洗点确认。";
                    _noticeError = false;
                    Rebuild();
                });
            }
            else
            {
                string attrTip = loadout.AttributeSwitchCooldownUntil > loadout.ServerNowSec
                    ? $"属性切换冷却中（剩余 {loadout.AttributeSwitchCooldownUntil - loadout.ServerNowSec} 秒，战法可立即切）"
                    : "战法与本方案四维加点 1:1 绑定，切换时同步生效";
                CreateText("LoadoutAttrHint", attrBox, 932f, 50f, 380f, 34f, attrTip, 16f, Brown);
            }
        }

        private void BuildAttrDeltaButton(RectTransform parent, uint dim, int delta, string text, float x, float y, bool interactable)
        {
            string tag = delta < 0 ? "M" + (-delta) : "P" + delta;
            var btn = CharacterAttributeVisuals.Button($"LoadoutAttr_{dim}_{tag}", parent, x, y, 38f, 30f, text, false, 15f);
            SkillPanel.StyleGuildControl(btn, "guild_button_secondary", Ink);
            btn.SetInteractable(interactable);
            btn.Button.onClick.AddListener(() =>
            {
                if (!CanWrite || !_draft.AdjustAttribute(dim, delta)) return;
                _notice = $"{SkillBuildTemplates.DimensionName(dim)}已调整，保存后生效。";
                _noticeError = false;
                Rebuild();
            });
        }

        private void BuildSkillSlotsRow(SkillBookSnapshot snapshot, SkillLoadoutView loadout)
        {
            int openingCap = (int)loadout.MaxOpeningSlots;
            int passiveCap = (int)loadout.MaxPassiveSlots;
            int pursuitCap = (int)loadout.MaxPursuitSlots;
            int totalSlots = openingCap + passiveCap + pursuitCap;
            var slots = CreateGuildImage("LoadoutSlotGroup", _contentRoot, 18f, 202f, TableWidth, 128f,
                "guild_announcement", QdaoUguiTheme.Html("#F0E7D7")).rectTransform;
            float slotW = (TableWidth - 24f) / Mathf.Max(1, totalSlots);
            int col = 0;
            for (int i = 0; i < openingCap; i++)
                BuildSlotCard(snapshot, "开场槽", 4u, i, _draft.Opening, slots, col++, slotW);
            for (int i = 0; i < passiveCap; i++)
                BuildSlotCard(snapshot, $"被动槽 {i + 1}", 2u, i, _draft.Passive, slots, col++, slotW);
            for (int i = 0; i < pursuitCap; i++)
                BuildSlotCard(snapshot, "追击槽", 3u, i, _draft.Pursuit, slots, col++, slotW);
        }

        private void BuildLearnedPoolSection(SkillBookSnapshot snapshot, bool canWrite, float poolOffset)
        {
            CreateText("LoadoutPoolHeading", _contentRoot, 23f, 336f, 170f, 30f, "已学战法", 25f, JadeInk, bold: true);
            AddRule("LoadoutPoolRule", _contentRoot, 180f, 351f, 1162f, 1f);
            var header = CreateGuildImage("LoadoutPoolHeader", _contentRoot, 18f, 368f, TableWidth, 34f,
                "guild_table_header", QdaoUguiTheme.Html("#E5D8BE"));
            CreateText("SkillHeader", header.transform, 114f, 0f, 344f, 34f, "战法", 20f, Brown);
            CreateText("DetailsHeader", header.transform, 500f, 0f, 384f, 34f, "类型与策略", 20f, Brown);
            CreateText("ActionHeader", header.transform, 928f, 0f, 366f, 34f, "装配状态", 20f, Brown);

            var viewport = QdaoUguiFactory.CreateRect("LoadoutPoolViewport", _contentRoot, 18f, 402f, TableWidth, ListHeight);
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpBg = viewport.gameObject.AddComponent<Image>();
            vpBg.color = new Color(1f, 1f, 1f, 0.001f);
            var poolContent = QdaoUguiFactory.CreateRect("LoadoutPoolContent", viewport, 0f, 0f, TableWidth, ListHeight);
            var scroll = _poolScroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = poolContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;

            var candidates = CollectLearnedNonActiveSkills(snapshot);
            if (candidates.Count == 0)
            {
                CreateText("LoadoutPoolEmpty", poolContent, 20f, 108f, 1284f, 54f,
                    "暂无已学战法", 22f, Brown, TextAlignmentOptions.Center);
            }
            else
            {
                poolContent.sizeDelta = new Vector2(TableWidth, Mathf.Max(ListHeight, candidates.Count * RowHeight));
                for (int i = 0; i < candidates.Count; i++)
                {
                    var entry = candidates[i];
                    var def = entry.Definition;
                    uint slotKind = def.abilityKind;
                    bool inDraft = _draft.Contains(entry.Id);
                    uint conflictId = inDraft ? 0u : _draft.GetConflict(snapshot, entry);
                    var plate = CreateGuildImage("LoadoutPool_" + entry.Id, poolContent, 0f, i * RowHeight,
                        TableWidth, RowHeight, inDraft ? "guild_row_self" : "guild_row_normal",
                        QdaoUguiTheme.Html(inDraft ? "#DDE5D4" : "#F0E8DB"));
                    var foreground = inDraft ? JadeInk : Ink;

                    AddCardAction(plate, canWrite, () => ToggleSkillInDraft(snapshot, entry));

                    if (inDraft) AddSelectedMark(plate.transform, 18f, 14f, 24f);
                    var icon = QdaoUguiFactory.CreateImage("Icon", plate.transform, 57f, 4f, 44f, 44f, ResolveSkillIcon(def));
                    icon.preserveAspect = true;
                    icon.raycastTarget = false;

                    string kindTag = SlotKindLabel(slotKind);
                    string exTag = def.exclusiveGroup > 0 ? " · 专精互斥" : string.Empty;
                    string targetTag = string.IsNullOrEmpty(def.SecondaryTargetLabel) ? string.Empty : " · " + def.SecondaryTargetLabel;
                    CreateText("Title", plate.transform, 114f, 0f, 344f, RowHeight, $"{def.name}   {entry.Rank}级", 23f, foreground, bold: true);
                    CreateText("Meta", plate.transform, 500f, 0f, 384f, RowHeight,
                        $"{kindTag}{targetTag}{exTag}", 20f, conflictId != 0 ? Warn : foreground);

                    string actionText = !canWrite ? (inDraft ? "已在方案内" : string.Empty) : inDraft ? "卸下"
                        : conflictId != 0 ? "点击替换" : "点击装入";
                    if (entry.Equipped)
                        CreateText("Equipped", plate.transform, 928f, 0f, 152f, RowHeight, "已装配", 20f, JadeInk);
                    CreateText("Action", plate.transform, 1090f, 0f, 204f, RowHeight, actionText, 20f,
                        conflictId != 0 ? Warn : foreground, TextAlignmentOptions.MidlineRight);
                }
            }
            poolContent.anchoredPosition = new Vector2(0f, Mathf.Clamp(poolOffset, 0f, Mathf.Max(0f, poolContent.sizeDelta.y - ListHeight)));
        }

        private void BuildFooterRow(SkillLoadoutView loadout, bool canWrite)
        {
            string footerMsg = _battleLocked ? "战斗中仅可查看战法与加点装配。"
                : _saving ? "正在提交流派方案，等待服务器确认…"
                : _notice ?? (_draft.IsDirty ? "保存或重置草稿后可切换方案。" : string.Empty);
            CreateText("LoadoutFooterNotice", _contentRoot, 22f, 708f, 854f, 48f, footerMsg, 20f,
                _noticeError || _battleLocked ? Warn : Ink);

            _resetBtn = CharacterAttributeVisuals.Button("LoadoutResetBtn", _contentRoot, 892f, 710f, 196f, 48f, "重置草稿", false, 23f);
            SkillPanel.StyleGuildControl(_resetBtn, "guild_button_secondary", Ink);
            _resetBtn.SetInteractable(canWrite && _draft.IsDirty);
            _resetBtn.Button.onClick.AddListener(() =>
            {
                if (!CanWrite) return;
                _draft.Reset(loadout, _draft.SchemeId);
                _notice = "已恢复所选方案的已保存战法与加点。";
                _noticeError = false;
                Rebuild();
            });

            var saveBtn = CharacterAttributeVisuals.Button("LoadoutSaveBtn", _contentRoot, 1102f, 708f, 240f, 52f, "保存并启用", true, 24f);
            SkillPanel.StyleGuildControl(saveBtn, canWrite ? "guild_button_primary" : "guild_button_secondary",
                canWrite ? QdaoRefreshArt.Ivory : Brown, preserveReadOnlyColor: true);
            saveBtn.SetInteractable(canWrite);
            saveBtn.Button.onClick.AddListener(SubmitSave);
        }

        private void BuildCopyPickerOverlay(SkillLoadoutView loadout)
        {
            var box = CreateGuildImage("LoadoutCopyPicker", _contentRoot, 280f, 102f, 800f, 116f,
                "guild_announcement", ModalBg);
            box.raycastTarget = true;
            CreateText("CopyTitle", box.transform, 18f, 8f, 650f, 32f,
                "选择要复制到当前草稿的源方案（支持从已到期只读方案复制）：", 18f, JadeInk, bold: true);
            var cancelBtn = CharacterAttributeVisuals.Button("LoadoutCopyCancelBtn", box.transform, 708f, 8f, 76f, 32f, "关闭", false, 16f);
            SkillPanel.StyleGuildControl(cancelBtn, "guild_button_secondary", Ink);
            cancelBtn.Button.onClick.AddListener(() => { _copyPickerOpen = false; Rebuild(); });

            uint unlocked = UnlockedSchemeLimit(loadout);
            float x = 18f;
            for (uint s = 1u; s <= MaxSchemeLimit(loadout); s++)
            {
                if (s == _draft.SchemeId) continue;
                var sc = loadout.FindScheme(s);
                if (sc == null) continue;
                uint captured = s;
                string name = TruncateDisplay(!string.IsNullOrEmpty(sc.Name) ? sc.Name : SkillLoadoutView.DefaultSchemeName(s), 4);
                string label = s <= unlocked ? name : name + "(锁)";
                var btn = CharacterAttributeVisuals.Button("LoadoutCopyFrom" + s, box.transform, x, 50f, 120f, 48f, label, false, 17f);
                SkillPanel.StyleGuildControl(btn, "guild_tab_normal", s <= unlocked ? Ink : Brown);
                btn.Button.onClick.AddListener(() =>
                {
                    if (!CanWrite) return;
                    if (_draft.CopyFromScheme(loadout, captured))
                    {
                        _copyPickerOpen = false;
                        _notice = $"已将「{(!string.IsNullOrEmpty(sc.Name) ? sc.Name : SkillLoadoutView.DefaultSchemeName(captured))}」的战法与加点复制到当前草稿，保存后生效。";
                        _noticeError = false;
                        Rebuild();
                    }
                });
                x += 126f;
                if (x > 680f) break;
            }
        }

        private void BuildRentModalOverlay(SkillLoadoutView loadout)
        {
            var backdrop = QdaoUguiFactory.CreateImage("LoadoutRentBackdrop", _contentRoot, 0f, 0f, 1360f, 764f, null, raycastTarget: true);
            backdrop.color = new Color(0.07f, 0.05f, 0.04f, 0.65f);

            // panel_build_rent_modal is a 1932x814 full-window mockup backdrop with baked sub-boxes/buttons;
            // use the 9-slice card_attr_panel_bg for the 940x592 dynamic modal to prevent double-button overlap.
            var modal = CreateCustomImage("LoadoutRentModal", _contentRoot, 210f, 86f, 940f, 592f,
                SkillLoadoutRentPlanner.ArtAttrBox, "guild_announcement", ModalBg);
            modal.raycastTarget = true;

            var passIcon = LoadCustomArt(SkillLoadoutRentPlanner.ArtSlotPassIcon);
            float titleX = passIcon != null ? 74f : 28f;
            if (passIcon != null)
            {
                var iconImg = QdaoUguiFactory.CreateImage("RentModalPassIcon", modal.transform, 28f, 18f, 40f, 40f, passIcon);
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
            }
            CreateText("RentModalTitle", modal.transform, titleX, 18f, 740f, 40f,
                "方案槽位租用管理（装备+战法+加点三合一 · 7套封顶）", 25f, JadeInk, bold: true);
            var closeBtn = CharacterAttributeVisuals.Button("LoadoutRentCloseBtn", modal.transform, 836f, 16f, 82f, 42f, "关闭", false, 19f);
            SkillPanel.StyleGuildControl(closeBtn, "guild_button_secondary", Ink);
            closeBtn.Button.onClick.AddListener(() => { _rentModalOpen = false; Rebuild(); });

            uint maxRentable = loadout.MaxRentableExtraSlots;
            uint activeRented = loadout.IsRentActive ? Math.Min(loadout.RentedExtraSlots, SkillLoadoutRentPlanner.MaxRentedExtraSlots) : 0u;
            uint minAllowed = Math.Min(activeRented, maxRentable);
            uint unlocked = UnlockedSchemeLimit(loadout);

            string freeDesc = loadout.FreeSchemes >= 3u
                ? $"永久免费 {loadout.FreeSchemes} 套（基础2+满级85赠1）"
                : $"永久免费 {loadout.FreeSchemes} 套（达85级再免费+1套）";
            CreateText("RentStatusLine1", modal.transform, 28f, 68f, 884f, 30f,
                $"{freeDesc}  ·  轻享卡 +{loadout.LightCardBonusSchemes} 套  ·  已租 {activeRented} 套  ·  共解锁 {unlocked}/7 套",
                18f, Ink, bold: true);
            CreateText("RentStatusLine2", modal.transform, 28f, 100f, 884f, 28f,
                $"当前额外槽位租期：{SkillLoadoutRentPlanner.FormatRemaining(loadout.RemainingRentSeconds)}   |   当前真实灵钻余额：{loadout.DiamondBalance}",
                18f, Brown);
            AddRule("RentRule1", modal.transform, 28f, 136f, 884f, 1f);

            CreateText("RentStep1Title", modal.transform, 28f, 148f, 884f, 30f,
                $"1. 选择额外租用槽位数（单价 {loadout.RentUnitPriceDiamond} 真实灵钻/套/{loadout.RentPeriodDays}天，租期内可补差价扩容）",
                19f, JadeInk, bold: true);

            for (uint t = 1u; t <= SkillLoadoutRentPlanner.MaxRentedExtraSlots; t++)
            {
                uint captured = t;
                bool overCap = t > maxRentable;
                bool underActive = loadout.IsRentActive && t < minAllowed;
                bool enabled = !overCap && !underActive;
                bool selected = enabled && _rentTargetSlots == t;
                string sub = overCap ? "(已达7套上限)" : underActive ? "(租期不可减)" : $"(共{Math.Min(7u, loadout.FreeSchemes + loadout.LightCardBonusSchemes + t)}套)";
                var optBtn = CharacterAttributeVisuals.Button("LoadoutRentTarget" + t, modal.transform,
                    28f + (t - 1u) * 178f, 186f, 170f, 64f, $"+{t} 套\n{sub}", selected, 17f);
                StyleCustomControl(optBtn, null,
                    selected ? "guild_tab_selected" : "guild_tab_normal",
                    selected ? QdaoRefreshArt.Ivory : (enabled ? Ink : Brown), preserveReadOnlyColor: true);
                optBtn.SetInteractable(enabled);
                optBtn.Button.onClick.AddListener(() =>
                {
                    _rentTargetSlots = captured;
                    if (!loadout.IsRentActive && _rentAddPeriods == 0u) _rentAddPeriods = 1u;
                    if (loadout.IsRentActive && _rentTargetSlots <= activeRented && _rentAddPeriods == 0u) _rentAddPeriods = 1u;
                    Rebuild();
                });
            }

            CreateText("RentStep2Title", modal.transform, 28f, 266f, 884f, 30f,
                $"2. 选择续期时长（统一到期日，剩余最长可叠至 {loadout.RentMaxRemainingDays} 天，无自动续费）",
                19f, JadeInk, bold: true);

            (uint periods, string text)[] periodOptions =
            {
                (0u, "仅补差价扩容"),
                (1u, $"+{loadout.RentPeriodDays} 天"),
                (3u, $"+{loadout.RentPeriodDays * 3u} 天"),
                (6u, $"+{loadout.RentPeriodDays * 6u} 天"),
            };
            for (int i = 0; i < periodOptions.Length; i++)
            {
                uint p = periodOptions[i].periods;
                var testQuote = SkillLoadoutRentPlanner.Plan(loadout, _rentTargetSlots, p);
                bool selected = _rentAddPeriods == p;
                var pBtn = CharacterAttributeVisuals.Button("LoadoutRentPeriod" + p, modal.transform,
                    28f + i * 222f, 304f, 212f, 52f, periodOptions[i].text, selected, 18f);
                StyleCustomControl(pBtn, null,
                    selected ? "guild_tab_selected" : "guild_tab_normal",
                    selected ? QdaoRefreshArt.Ivory : (testQuote.Valid ? Ink : Brown), preserveReadOnlyColor: true);
                pBtn.SetInteractable(testQuote.Valid);
                pBtn.Button.onClick.AddListener(() => { _rentAddPeriods = p; Rebuild(); });
            }

            var quote = SkillLoadoutRentPlanner.Plan(loadout, _rentTargetSlots, _rentAddPeriods);
            var quoteBox = CreateGuildImage("LoadoutRentQuoteBox", modal.transform, 28f, 372f, 884f, 128f,
                "guild_row_normal", PaperLight);
            if (quote.Valid)
            {
                ulong newRem = quote.NewExpiresAtSec > loadout.ServerNowSec ? quote.NewExpiresAtSec - loadout.ServerNowSec : 0UL;
                CreateText("QuoteCostLine", quoteBox.transform, 18f, 10f, 848f, 32f,
                    $"本次应付：{quote.TotalCost} 真实灵钻（补差价 {quote.ProrateCost} + 续期 {quote.RenewCost}，仅扣真实灵钻）",
                    20f, JadeInk, bold: true);
                CreateText("QuoteEffectLine", quoteBox.transform, 18f, 46f, 848f, 28f,
                    $"生效后总计解锁 {quote.NewUnlockedSchemes}/7 套方案   |   新租期：{SkillLoadoutRentPlanner.FormatRemaining(newRem)}",
                    18f, Ink);
            }
            else
            {
                CreateText("QuoteErrLine", quoteBox.transform, 18f, 24f, 848f, 44f,
                    quote.Error ?? "请选择有效的租用方案。", 19f, Warn, bold: true);
            }
            CreateText("QuoteGuarantee", quoteBox.transform, 18f, 84f, 848f, 34f,
                "零战力保障：装备+战法+加点三合一共享席位、一次开通绝不重复收费，无任何属性加成；到期只锁不删，续租立即可恢复。",
                16f, Brown);

            bool enoughDiamond = quote.Valid && loadout.DiamondBalance >= quote.TotalCost;
            if (quote.Valid && !enoughDiamond)
            {
                CreateText("RentShortageText", modal.transform, 28f, 520f, 540f, 48f,
                    $"真实灵钻不足（需 {quote.TotalCost}，当前 {loadout.DiamondBalance}）", 19f, Warn, bold: true);
                var mallBtn = CharacterAttributeVisuals.Button("LoadoutRentRechargeBtn", modal.transform,
                    590f, 518f, 322f, 52f, "前往官方灵钻商城", true, 21f);
                SkillPanel.StyleGuildControl(mallBtn, "guild_button_primary", QdaoRefreshArt.Ivory);
                mallBtn.Button.onClick.AddListener(() =>
                {
                    _rentModalOpen = false;
                    if (JubaozhaiUiRoot.Instance != null)
                        JubaozhaiUiRoot.Instance.OpenOfficialMall();
                    else
                    {
                        _notice = "请在主城聚宝斋官方商城获取真实灵钻。";
                        _noticeError = false;
                        Rebuild();
                    }
                });
            }
            else
            {
                var confirmBtn = CharacterAttributeVisuals.Button("LoadoutRentConfirmBtn", modal.transform,
                    590f, 518f, 322f, 52f,
                    quote.Valid ? $"确认租用（{quote.TotalCost} 真实灵钻）" : "请调整选项", true, 21f);
                SkillPanel.StyleGuildControl(confirmBtn, quote.Valid && CanRent ? "guild_button_primary" : "guild_button_secondary",
                    quote.Valid && CanRent ? QdaoRefreshArt.Ivory : Brown, preserveReadOnlyColor: true);
                confirmBtn.SetInteractable(quote.Valid && CanRent);
                confirmBtn.Button.onClick.AddListener(() => SubmitRent(_rentTargetSlots, _rentAddPeriods, quote.TotalCost));
            }
        }

        private void BuildSlotCard(SkillBookSnapshot snapshot, string slotTitle, uint slotKind, int slotIndex,
            IReadOnlyList<uint> list, RectTransform parent, int column, float width)
        {
            uint skillId = slotIndex < list.Count ? list[slotIndex] : 0u;
            var entry = skillId != 0u ? snapshot.Find(skillId) : null;
            var def = entry?.Definition;
            bool filled = skillId != 0;
            string skillName = def?.name ?? $"战法 #{skillId}";
            float x = 12f + column * width;
            if (column > 0) AddRule("SlotRule" + column, parent, x, 10f, 1f, 108f);
            var plate = QdaoUguiFactory.CreateImage($"LoadoutSlot_{slotKind}_{slotIndex}", parent, x, 4f, width, 120f, null);
            plate.color = new Color(1f, 1f, 1f, 0.001f);
            AddCardAction(plate, CanWrite && filled, () =>
            {
                if (!CanWrite || !_draft.Remove(skillId)) return;
                _notice = $"已从{slotTitle}卸下「{skillName}」，保存后生效。";
                _noticeError = false;
                Rebuild();
            });

            CreateText("SlotKind", plate.transform, 8f, 4f, width - 16f, 24f, slotTitle, 19f, Brown, TextAlignmentOptions.Center);
            if (filled)
            {
                var icon = QdaoUguiFactory.CreateImage("SlotIcon", plate.transform,
                    (width - 46f) * 0.5f, 29f, 46f, 46f, ResolveSkillIcon(def));
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                CreateText("SlotName", plate.transform, 8f, 76f, width - 16f, 22f, skillName, 20f, JadeInk, TextAlignmentOptions.Center, bold: true);
                string sub = def?.exclusiveGroup > 0 ? "专精互斥" : string.Empty;
                if (CanWrite) sub = sub.Length == 0 ? "点击卸下" : sub + " · 卸下";
                CreateText("SlotSub", plate.transform, 8f, 98f, width - 16f, 20f, sub, 16f, Brown, TextAlignmentOptions.Center);
            }
            else
            {
                CreateText("SlotEmpty", plate.transform, 8f, 48f, width - 16f, 40f, "空槽", 20f, Brown, TextAlignmentOptions.Center);
            }
        }

        private void OpenRentModal(SkillLoadoutView loadout)
        {
            if (loadout == null) return;
            uint maxRentable = loadout.MaxRentableExtraSlots;
            uint activeRented = loadout.IsRentActive ? Math.Min(loadout.RentedExtraSlots, maxRentable) : 0u;
            if (loadout.IsRentActive)
            {
                if (activeRented < maxRentable)
                {
                    _rentTargetSlots = activeRented + 1u;
                    _rentAddPeriods = 0u;
                }
                else
                {
                    _rentTargetSlots = Math.Max(1u, activeRented);
                    _rentAddPeriods = 1u;
                }
            }
            else
            {
                _rentTargetSlots = 1u;
                _rentAddPeriods = 1u;
            }
            _rentModalOpen = true;
            _copyPickerOpen = false;
            Rebuild();
        }

        private bool HasOtherCopyableScheme(SkillLoadoutView loadout)
        {
            if (loadout == null) return false;
            foreach (var sc in loadout.Schemes)
                if (sc != null && sc.SchemeId != _draft.SchemeId) return true;
            return false;
        }

        private string FormatSubHeader(SkillLoadoutView loadout)
        {
            uint activeId = ActiveSchemeId(loadout);
            var activeScheme = loadout.FindScheme(activeId);
            string activeName = !string.IsNullOrEmpty(activeScheme?.Name)
                ? activeScheme.Name
                : SkillLoadoutView.DefaultSchemeName(activeId);
            string modeHint = loadout.CustomEnabled
                ? $"当前启用·{activeName}"
                : "当前生效·全部已学战法";
            return modeHint + (_draft.IsDirty ? "  [未保存]" : string.Empty);
        }

        private static string SchemeStatusTag(SkillLoadoutView loadout, uint schemeId, bool unlocked)
        {
            if (schemeId <= loadout.FreeSchemes) return "免";
            if (!unlocked) return "锁";
            if (schemeId <= loadout.FreeSchemes + loadout.LightCardBonusSchemes) return "卡";
            return "租";
        }

        private static string TruncateDisplay(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text ?? string.Empty;
            return text.Substring(0, maxChars);
        }

        private static Sprite LoadCustomArt(string artName) =>
            string.IsNullOrEmpty(artName) ? null : Resources.Load<Sprite>(SkillLoadoutRentPlanner.ArtBasePath + artName);

        private static void ApplyCustomSprite(Image image, Sprite sprite, Color fallback)
        {
            if (sprite != null && sprite.border.sqrMagnitude > 0f)
            {
                SeasonEconomyUiArt.ApplySlicedControlSprite(image, sprite, fallback);
                image.pixelsPerUnitMultiplier = 1f;
            }
            else
            {
                SeasonEconomyUiArt.ApplyFixedControlSprite(image, sprite, fallback);
            }
        }

        private static void StyleCustomControl(UiTextButton button, string customArt, string fallbackGuildArt,
            Color ink, bool preserveReadOnlyColor = false)
        {
            SkillPanel.StyleGuildControl(button, fallbackGuildArt, ink, preserveReadOnlyColor);
            var custom = LoadCustomArt(customArt);
            if (custom != null && button?.Plate != null)
                ApplyCustomSprite(button.Plate, custom, Color.white);
        }

        private static Image CreateCustomImage(string name, UnityEngine.Transform parent, float x, float y, float w, float h,
            string customArt, string fallbackGuildArt, Color fallback)
        {
            var image = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, null);
            var sprite = LoadCustomArt(customArt) ??
                (string.IsNullOrEmpty(fallbackGuildArt) ? null : SeasonEconomyUiArt.LoadGuildFidelityAsset(fallbackGuildArt));
            ApplyCustomSprite(image, sprite, fallback);
            return image;
        }

        private static Image CreateGuildImage(string name, UnityEngine.Transform parent, float x, float y, float w, float h,
            string art, Color fallback)
        {
            var image = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, null);
            SeasonEconomyUiArt.ApplyFixedControlSprite(image, SeasonEconomyUiArt.LoadGuildFidelityAsset(art), fallback);
            return image;
        }

        private static void AddRule(string name, UnityEngine.Transform parent, float x, float y, float w, float h)
        {
            var image = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, null);
            image.color = Rule;
        }

        internal static void AddSelectedMark(UnityEngine.Transform parent, float x, float y, float size)
        {
            var sprite = SeasonEconomyUiArt.LoadGuildFidelityAsset("guild_self_mark");
            if (sprite != null) QdaoUguiFactory.CreateImage("SelectedMark", parent, x, y, size, size, sprite);
        }

        private void ToggleSkillInDraft(SkillBookSnapshot snapshot, SkillEntryState entry)
        {
            if (!CanWrite) return;
            var change = _draft.Toggle(snapshot, entry, out uint conflictId);
            string name = entry.Definition.name;
            var previous = snapshot.Find(conflictId)?.Definition?.name;
            _notice = change switch
            {
                SkillLoadoutDraft.Change.Removed => $"已卸下「{name}」。",
                SkillLoadoutDraft.Change.Replaced => $"已将「{previous}」替换为「{name}」，保存后生效。",
                SkillLoadoutDraft.Change.Added => $"已装入「{name}」，保存后生效。",
                _ => $"{SlotKindLabel(entry.Definition.abilityKind)}槽已满，请先卸下一项战法。",
            };
            _noticeError = change == SkillLoadoutDraft.Change.SlotsFull;
            Rebuild();
        }

        private void SubmitSave()
        {
            if (!CanWrite) return;
            if (!SkillLoadoutRentPlanner.ValidateDraftName(_draft.Name, out string nameError))
            {
                _notice = nameError;
                _noticeError = true;
                Rebuild();
                return;
            }
            if (_draft.NeedsAttributeReset && !_draft.ResetAttributeIfNeeded)
            {
                _notice = "本次下调了已分配加点，请先点击上方「确认洗点」后再保存。";
                _noticeError = true;
                Rebuild();
                return;
            }
            int operation = BeginWrite();
            var args = new SkillLoadoutSaveArgs
            {
                SchemeId = _draft.SchemeId,
                Name = _draft.Name,
                RoleTag = _draft.RoleTag,
                PassiveSkillIds = new List<uint>(_draft.Passive),
                PursuitSkillIds = new List<uint>(_draft.Pursuit),
                OpeningSkillIds = new List<uint>(_draft.Opening),
                UpdateAttributeAllocation = _draft.AttributesDirty,
                AttributeAllocated = new Dictionary<uint, uint>(_draft.AttributeAllocated),
                ResetAttributeIfNeeded = _draft.ResetAttributeIfNeeded,
            };
            _source.SaveLoadout(args, (ok, message) => CompleteWrite(operation, ok, message, "战法与加点方案已保存并启用。"));
        }

        private void SubmitSwitch(uint schemeId)
        {
            if (!CanWrite || _draft.IsDirty) return;
            int operation = BeginWrite();
            _source.SwitchLoadout(schemeId, false,
                (ok, message) => CompleteWrite(operation, ok, message, $"已启用{SkillLoadoutView.DefaultSchemeName(schemeId)}。"));
        }

        private void SubmitRent(uint targetRentedSlots, uint addPeriods, ulong expectedCost)
        {
            if (!CanRent) return;
            int operation = BeginWrite();
            _source.RentLoadoutSlots(targetRentedSlots, addPeriods, expectedCost, (ok, message) =>
            {
                if (ok) _rentModalOpen = false;
                CompleteWrite(operation, ok, message, "方案槽位租用已生效。");
            });
        }

        private int BeginWrite()
        {
            _saving = true;
            Rebuild();
            return ++_operation;
        }

        private void CompleteWrite(int operation, bool ok, string message, string successMessage)
        {
            if (_root == null || operation != _operation || !ReferenceEquals(_source, _getSource?.Invoke())) return;
            _saving = false;
            var loadout = _state?.Snapshot?.Loadout;
            if (ok && loadout != null) _draft.Reset(loadout, ActiveSchemeId(loadout));
            _notice = message ?? (ok ? successMessage : "流派方案操作未确认，请刷新技能信息。");
            _noticeError = !ok;
            if (_root.gameObject.activeSelf) Rebuild(resetScroll: ok);
        }

        private static void AddCardAction(Image plate, bool enabled, Action activate)
        {
            if (!enabled) return;
            plate.raycastTarget = true;
            var button = plate.gameObject.AddComponent<Button>();
            button.targetGraphic = plate;
            button.onClick.AddListener(() => activate());
        }

        private static List<SkillEntryState> CollectLearnedNonActiveSkills(SkillBookSnapshot snapshot)
        {
            var result = new List<SkillEntryState>();
            foreach (var e in snapshot.Entries)
            {
                if (e?.Definition == null || e.Rank == 0) continue;
                if (SkillLoadoutDraft.IsConfigurable(e.Definition)) result.Add(e);
            }
            return result;
        }

        private static string SlotKindLabel(uint slotKind) => slotKind switch
        {
            4u => "开场",
            3u => "追击",
            _ => "被动",
        };

        private static Sprite ResolveSkillIcon(SkillDefinition def) =>
            (string.IsNullOrEmpty(def?.IconResource) ? null : Resources.Load<Sprite>(def.IconResource)) ??
            (def == null ? null : SeasonEconomyUiArt.LoadGrowthSkillIcon(def.id)) ??
            QdaoUguiTheme.LoadSprite("UI/Ugui/GameplayV1/icon_scroll");

        private static TMP_Text CreateText(string name, UnityEngine.Transform parent, float x, float y,
            float w, float h, string value, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool bold = false)
        {
            var t = QdaoUguiFactory.CreateText(name, parent, x, y, w, h, value, size, color, align);
            t.font = QdaoUguiTheme.ResolveFont();
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.richText = false;
            t.raycastTarget = false;
            return t;
        }
    }
}
