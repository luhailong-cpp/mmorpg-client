using System;
using System.Collections.Generic;
using MmorpgClient.UI.Ugui.Attribute;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Skills
{
    /// <summary>
    /// 角色技能书「战法装配」子视图（3 套流派方案：开场 ×1 / 被动 ×3 / 追击 ×1）。
    /// 严格消费权威 SkillBookViewState.Snapshot.Loadout，所有保存/切换经 ISkillLoadoutSource 提交，
    /// 战斗中只读锁定写入，绝不推算服务端战斗数值。
    /// </summary>
    public sealed class SkillLoadoutSubPanel
    {
        private static readonly Color Ink = QdaoUguiTheme.Html("#201D16");
        private static readonly Color Brown = QdaoUguiTheme.Html("#513722");
        private static readonly Color JadeInk = QdaoUguiTheme.Html("#103B29");
        private static readonly Color Rule = QdaoUguiTheme.Html("#CCBA95");
        private static readonly Color Warn = QdaoUguiTheme.Html("#893827");
        private const float TableWidth = 1324f;
        private const float ListHeight = 326f;
        private const float RowHeight = 55f;
        private static readonly string[] SchemeNames = { "方案一", "方案二", "方案三", "方案四", "方案五", "方案六" };

        private readonly RectTransform _root;
        private readonly RectTransform _contentRoot;
        private readonly Func<ISkillLoadoutSource> _getSource;
        private readonly SkillLoadoutDraft _draft = new();

        private SkillBookViewState _state;
        private ISkillLoadoutSource _source;
        private ScrollRect _poolScroll;
        private bool _battleLocked;
        private bool _saving;
        private bool _hasDraftSnapshot;
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

        public void Hide() => _root.gameObject.SetActive(false);

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
            _draft.Reset(null, 1);
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

        private static uint UnlockedSchemeLimit(SkillLoadoutView loadout)
        {
            uint max = loadout != null && loadout.MaxSchemes >= 3u ? Math.Min(loadout.MaxSchemes, 6u) : 6u;
            uint unlocked = loadout != null && loadout.UnlockedSchemes >= 3u ? loadout.UnlockedSchemes : 3u;
            return Math.Min(unlocked, max);
        }

        private bool CanWrite =>
            !_battleLocked && !_saving &&
            _state?.Context?.IsPet == false && _state.IsConnected && !_state.IsReadOnly && !_state.IsLoading && !_state.IsBusy &&
            _state.Snapshot?.Loadout != null && _draft.SchemeId <= UnlockedSchemeLimit(_state.Snapshot.Loadout) &&
            _source != null && ReferenceEquals(_source, _getSource?.Invoke());

        private static uint ActiveSchemeId(SkillLoadoutView loadout) =>
            loadout.ActiveSchemeId is >= 1u and <= 6u ? loadout.ActiveSchemeId : 1u;

        private void Rebuild(bool resetScroll = false)
        {
            float poolOffset = !resetScroll && _poolScroll != null ? _poolScroll.content.anchoredPosition.y : 0;
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
            bool canChangeScheme = !_battleLocked && !_saving && !_draft.IsDirty && (_source == null || canWrite);

            uint activeId = loadout.ActiveSchemeId;
            uint maxSchemes = loadout.MaxSchemes >= 3u ? Math.Min(loadout.MaxSchemes, 6u) : 6u;
            uint unlockedSchemes = UnlockedSchemeLimit(loadout);
            for (uint s = 1u; s <= maxSchemes; s++)
            {
                uint captured = s;
                var sc = loadout.FindScheme(s);
                bool isEditing = s == _draft.SchemeId;
                bool unlocked = s <= unlockedSchemes;
                bool hasSaved = sc != null;
                string label = unlocked ? SchemeNames[s - 1] : $"方案{s}(卡)";
                var btn = CharacterAttributeVisuals.Button("LoadoutScheme" + s, _contentRoot,
                    18f + (s - 1u) * 109f, 12f, 104f, 52f, label, isEditing, 19f);
                SkillPanel.StyleGuildControl(btn, isEditing ? "guild_tab_selected" : "guild_tab_normal",
                    isEditing ? QdaoRefreshArt.Ivory : (unlocked ? Ink : Brown), preserveReadOnlyColor: true);
                btn.SetInteractable(canChangeScheme || !unlocked);
                btn.Button.onClick.AddListener(() =>
                {
                    if (_battleLocked || _saving || _draft.IsDirty) return;
                    if (!unlocked)
                    {
                        if (hasSaved) _draft.Reset(loadout, captured);
                        _notice = hasSaved
                            ? "「仙途轻享卡」已到期：该已存方案已永久保留（只读预览），续卡后即可编辑与启用。"
                            : "第 4–6 套战法方案需「仙途轻享卡」解锁（纯便利零战力），到期不丢已存方案。";
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

            CreateText("LoadoutRoleTitle", _contentRoot, 682f, 15f, 104f, 44f, "方案用途", 23f, Brown);
            for (uint role = 0; role <= 3; role++)
            {
                uint captured = role;
                bool selected = role == _draft.RoleTag;
                var tagBtn = CharacterAttributeVisuals.Button("LoadoutRoleTag" + role, _contentRoot,
                    790f + role * 107f, 15f, 100f, 46f, SkillLoadoutView.DescribeRoleTag(role), false, 22f);
                SkillPanel.StyleGuildControl(tagBtn, "guild_tab_normal", selected ? JadeInk : Ink, preserveReadOnlyColor: true);
                if (selected)
                {
                    tagBtn.Label.rectTransform.offsetMin += new Vector2(14f, 0f);
                    AddSelectedMark(tagBtn.Rect, 8f, 15f, 16f);
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

            string modeHint = loadout.CustomEnabled
                ? $"当前启用 · {SchemeNames[ActiveSchemeId(loadout) - 1]}"
                : "当前生效 · 全部已学非主动战法";
            CreateText("LoadoutSubHeader", _contentRoot, 22f, 73f, 1320f, 29f,
                modeHint + (_draft.IsDirty ? "     有未保存改动" : string.Empty), 22f, JadeInk);

            int openingCap = (int)loadout.MaxOpeningSlots;
            int passiveCap = (int)loadout.MaxPassiveSlots;
            int pursuitCap = (int)loadout.MaxPursuitSlots;
            int totalSlots = openingCap + passiveCap + pursuitCap;
            var slots = CreateGuildImage("LoadoutSlotGroup", _contentRoot, 18f, 110f, TableWidth, 164f,
                "guild_announcement", QdaoUguiTheme.Html("#F0E7D7")).rectTransform;
            float slotW = (TableWidth - 24f) / Mathf.Max(1, totalSlots);
            int col = 0;
            for (int i = 0; i < openingCap; i++)
                BuildSlotCard(snapshot, "开场槽", 4u, i, _draft.Opening, slots, col++, slotW);
            for (int i = 0; i < passiveCap; i++)
                BuildSlotCard(snapshot, $"被动槽 {i + 1}", 2u, i, _draft.Passive, slots, col++, slotW);
            for (int i = 0; i < pursuitCap; i++)
                BuildSlotCard(snapshot, "追击槽", 3u, i, _draft.Pursuit, slots, col++, slotW);

            CreateText("LoadoutPoolHeading", _contentRoot, 23f, 290f, 170f, 40f, "已学战法", 31f, JadeInk, bold: true);
            AddRule("LoadoutPoolRule", _contentRoot, 180f, 310f, 1162f, 1f);
            var header = CreateGuildImage("LoadoutPoolHeader", _contentRoot, 18f, 336f, TableWidth, 40f,
                "guild_table_header", QdaoUguiTheme.Html("#E5D8BE"));
            CreateText("SkillHeader", header.transform, 114f, 0f, 344f, 40f, "战法", 23f, Brown);
            CreateText("DetailsHeader", header.transform, 500f, 0f, 384f, 40f, "类型与策略", 23f, Brown);
            CreateText("ActionHeader", header.transform, 928f, 0f, 366f, 40f, "装配状态", 23f, Brown);

            var viewport = QdaoUguiFactory.CreateRect("LoadoutPoolViewport", _contentRoot, 18f, 376f, TableWidth, ListHeight);
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
                CreateText("LoadoutPoolEmpty", poolContent, 20f, 116f, 1284f, 60f,
                    "暂无已学战法", 24f, Brown, TextAlignmentOptions.Center);
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

                    if (inDraft) AddSelectedMark(plate.transform, 18f, 15f, 25f);
                    var icon = QdaoUguiFactory.CreateImage("Icon", plate.transform, 57f, 4f, 46f, 46f, ResolveSkillIcon(def));
                    icon.preserveAspect = true;
                    icon.raycastTarget = false;

                    string kindTag = SlotKindLabel(slotKind);
                    string exTag = def.exclusiveGroup > 0 ? " · 专精互斥" : string.Empty;
                    string targetTag = string.IsNullOrEmpty(def.SecondaryTargetLabel) ? string.Empty : " · " + def.SecondaryTargetLabel;
                    CreateText("Title", plate.transform, 114f, 0f, 344f, RowHeight, $"{def.name}   {entry.Rank}级", 25f, foreground, bold: true);
                    CreateText("Meta", plate.transform, 500f, 0f, 384f, RowHeight,
                        $"{kindTag}{targetTag}{exTag}", 22f, conflictId != 0 ? Warn : foreground);

                    string actionText = !canWrite ? (inDraft ? "已在方案内" : string.Empty) : inDraft ? "卸下"
                        : conflictId != 0 ? "点击替换" : "点击装入";
                    if (entry.Equipped)
                        CreateText("Equipped", plate.transform, 928f, 0f, 152f, RowHeight, "已装配", 22f, JadeInk);
                    CreateText("Action", plate.transform, 1090f, 0f, 204f, RowHeight, actionText, 22f,
                        conflictId != 0 ? Warn : foreground, TextAlignmentOptions.MidlineRight);
                }
            }
            poolContent.anchoredPosition = new Vector2(0, Mathf.Clamp(poolOffset, 0, poolContent.sizeDelta.y - ListHeight));

            string footerMsg = _battleLocked ? "战斗中仅可查看战法装配。"
                : _saving ? "正在提交战法构筑，等待服务器确认…"
                : _notice ?? (_draft.IsDirty ? "保存或重置草稿后可切换方案。" : string.Empty);
            CreateText("LoadoutFooterNotice", _contentRoot, 23f, 709f, 850f, 48f, footerMsg, 22f,
                _noticeError || _battleLocked ? Warn : Ink);

            var resetBtn = CharacterAttributeVisuals.Button("LoadoutResetBtn", _contentRoot, 892f, 712f, 196f, 49f, "重置草稿", false, 24f);
            SkillPanel.StyleGuildControl(resetBtn, "guild_button_secondary", Ink);
            resetBtn.SetInteractable(canWrite && _draft.IsDirty);
            resetBtn.Button.onClick.AddListener(() =>
            {
                if (!CanWrite) return;
                _draft.Reset(loadout, _draft.SchemeId);
                _notice = "已恢复所选方案的已保存装配。";
                _noticeError = false;
                Rebuild();
            });

            var saveBtn = CharacterAttributeVisuals.Button("LoadoutSaveBtn", _contentRoot, 1102f, 710f, 240f, 53f, "保存并启用", true, 25f);
            SkillPanel.StyleGuildControl(saveBtn, canWrite ? "guild_button_primary" : "guild_button_secondary",
                canWrite ? QdaoRefreshArt.Ivory : Brown, preserveReadOnlyColor: true);
            saveBtn.SetInteractable(canWrite);
            saveBtn.Button.onClick.AddListener(SubmitSave);
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
            if (column > 0) AddRule("SlotRule" + column, parent, x, 12f, 1f, 140f);
            var plate = QdaoUguiFactory.CreateImage($"LoadoutSlot_{slotKind}_{slotIndex}", parent, x, 4f, width, 154f, null);
            plate.color = new Color(1f, 1f, 1f, 0.001f);
            AddCardAction(plate, CanWrite && filled, () =>
            {
                if (!CanWrite || !_draft.Remove(skillId)) return;
                _notice = $"已从{slotTitle}卸下「{skillName}」，保存后生效。";
                _noticeError = false;
                Rebuild();
            });

            CreateText("SlotKind", plate.transform, 10f, 8f, width - 20f, 30f, slotTitle, 23f, Brown, TextAlignmentOptions.Center);
            if (filled)
            {
                var icon = QdaoUguiFactory.CreateImage("SlotIcon", plate.transform,
                    (width - 60f) * 0.5f, 40f, 60f, 60f, ResolveSkillIcon(def));
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                CreateText("SlotName", plate.transform, 8f, 101f, width - 16f, 27f, skillName, 24f, JadeInk, TextAlignmentOptions.Center, bold: true);
                string sub = def?.exclusiveGroup > 0 ? "专精互斥" : string.Empty;
                if (CanWrite) sub = sub.Length == 0 ? "点击卸下" : sub + " · 点击卸下";
                CreateText("SlotSub", plate.transform, 8f, 129f, width - 16f, 24f, sub, 18f, Brown, TextAlignmentOptions.Center);
            }
            else
            {
                CreateText("SlotEmpty", plate.transform, 10f, 64f, width - 20f, 50f, "空槽", 23f, Brown, TextAlignmentOptions.Center);
            }
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
            int operation = BeginWrite();
            _source.SaveLoadout(_draft.SchemeId, _draft.RoleTag,
                new List<uint>(_draft.Passive), new List<uint>(_draft.Pursuit), new List<uint>(_draft.Opening),
                (ok, message) => CompleteWrite(operation, ok, message, "战法方案已保存并启用。"));
        }

        private void SubmitSwitch(uint schemeId)
        {
            if (!CanWrite || _draft.IsDirty) return;
            int operation = BeginWrite();
            _source.SwitchLoadout(schemeId,
                (ok, message) => CompleteWrite(operation, ok, message, $"已启用{SchemeNames[schemeId - 1]}。"));
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
            _notice = message ?? (ok ? successMessage : "战法操作未确认，请刷新技能信息。");
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
