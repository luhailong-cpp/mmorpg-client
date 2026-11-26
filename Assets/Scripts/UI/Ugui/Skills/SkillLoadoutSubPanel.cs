using System;
using System.Collections.Generic;
using MmorpgClient.UI.Ugui.Attribute;
using MmorpgClient.UI.Ugui.Battle;
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
        private static readonly Color Ink = QdaoUguiTheme.Html("#304736");
        private static readonly Color Muted = QdaoUguiTheme.Html("#70634E");
        private static readonly Color Warn = QdaoUguiTheme.Html("#9E3828");
        private static readonly Color CardBg = QdaoUguiTheme.Html("#FFF7E7");
        private static readonly Color CardActiveBg = QdaoUguiTheme.Html("#DFE9D3");
        private static readonly Color CardConflictBg = QdaoUguiTheme.Html("#F7E5DF");

        private readonly RectTransform _root;
        private readonly RectTransform _contentRoot;
        private readonly Func<ISkillLoadoutSource> _getSource;
        private readonly Action _onCloseToStudy;
        private readonly Action<string, bool> _setParentStatus;

        private SkillBookViewState _state;
        private bool _battleLocked;
        private bool _saving;
        private uint _editingSchemeId = 1u;
        private ulong _syncedStateVersion;
        private bool _draftDirty;
        private uint _draftRoleTag;
        private readonly List<uint> _draftOpening = new();
        private readonly List<uint> _draftPassive = new();
        private readonly List<uint> _draftPursuit = new();
        private string _notice;
        private bool _noticeError;

        public bool IsVisible => _root.gameObject.activeSelf;
        public uint EditingSchemeId => _editingSchemeId;

        public SkillLoadoutSubPanel(
            UnityEngine.Transform parent,
            Func<ISkillLoadoutSource> getSource,
            Action onCloseToStudy,
            Action<string, bool> setParentStatus)
        {
            _getSource = getSource;
            _onCloseToStudy = onCloseToStudy;
            _setParentStatus = setParentStatus;

            var bg = BattleUiWidgets.CreatePanel("SkillLoadoutPanel", parent, 266f, 88f, 1360f, 764f,
                QdaoUguiTheme.Html("#FBF5E6"), true);
            var outline = bg.gameObject.AddComponent<Outline>();
            outline.effectColor = QdaoUguiTheme.Html("#CAB991");
            outline.effectDistance = new Vector2(1f, -1f);
            _root = bg.rectTransform;
            _contentRoot = QdaoUguiFactory.CreateRect("LoadoutContent", _root, 0f, 0f, 1360f, 764f);
            _root.gameObject.SetActive(false);
        }

        public void Show(SkillBookViewState state, bool battleLocked)
        {
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _notice = null;
            _noticeError = false;
            Apply(state, battleLocked, forceSyncFromServer: true);
        }

        public void Hide() => _root.gameObject.SetActive(false);

        public void Apply(SkillBookViewState state, bool battleLocked, bool forceSyncFromServer = false)
        {
            _state = state;
            _battleLocked = battleLocked;
            var loadout = state?.Snapshot?.Loadout;
            if (loadout != null && (forceSyncFromServer || !_draftDirty || _syncedStateVersion != loadout.StateVersion))
            {
                if (forceSyncFromServer)
                    _editingSchemeId = loadout.ActiveSchemeId >= 1u && loadout.ActiveSchemeId <= 3u ? loadout.ActiveSchemeId : 1u;
                SyncDraftFromScheme(loadout, _editingSchemeId);
            }
            if (IsVisible) Rebuild();
        }

        private bool CanWrite =>
            !_battleLocked && !_saving &&
            _state != null && _state.IsConnected && !_state.IsReadOnly && !_state.IsLoading && !_state.IsBusy &&
            _state.Snapshot?.Loadout != null && _getSource?.Invoke() != null;

        private void SyncDraftFromScheme(SkillLoadoutView loadout, uint schemeId)
        {
            _editingSchemeId = schemeId >= 1u && schemeId <= 3u ? schemeId : 1u;
            _syncedStateVersion = loadout?.StateVersion ?? 0UL;
            _draftDirty = false;
            _draftOpening.Clear();
            _draftPassive.Clear();
            _draftPursuit.Clear();
            var scheme = loadout?.FindScheme(_editingSchemeId);
            _draftRoleTag = scheme?.RoleTag ?? 0u;
            if (scheme != null)
            {
                _draftOpening.AddRange(scheme.OpeningSkillIds);
                _draftPassive.AddRange(scheme.PassiveSkillIds);
                _draftPursuit.AddRange(scheme.PursuitSkillIds);
            }
        }

        private void Rebuild()
        {
            for (int i = _contentRoot.childCount - 1; i >= 0; --i)
            {
                var child = _contentRoot.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }

            var snapshot = _state?.Snapshot;
            var loadout = snapshot?.Loadout;
            if (snapshot == null || loadout == null)
            {
                CreateText("LoadoutEmpty", _contentRoot, 40f, 280f, 1280f, 80f,
                    "战法构筑数据尚未同步，请返回技能页刷新重试。", 24f, Muted, TextAlignmentOptions.Center);
                var backOnly = CharacterAttributeVisuals.Button("LoadoutBackBtn", _contentRoot, 570f, 380f, 220f, 52f, "返回技能修习", false, 20f);
                backOnly.Button.onClick.AddListener(() => _onCloseToStudy?.Invoke());
                return;
            }

            // 1. 顶栏：3 套流派方案切换胶囊 + 用途标签 + 启用按钮 + 返回修习
            uint activeId = loadout.ActiveSchemeId;
            for (uint s = 1u; s <= 3u; s++)
            {
                uint captured = s;
                var sc = loadout.FindScheme(s);
                string tag = SkillLoadoutView.DescribeRoleTag(sc?.RoleTag ?? 0u);
                bool isEditing = s == _editingSchemeId;
                bool isActive = s == activeId;
                string label = $"{(isActive ? "★ " : "")}方案{s} · {tag}";
                var btn = CharacterAttributeVisuals.Button("LoadoutScheme" + s, _contentRoot,
                    18f + (s - 1u) * 212f, 14f, 200f, 46f, label, isEditing, 18f);
                btn.Button.onClick.AddListener(() =>
                {
                    if (_editingSchemeId == captured) return;
                    _notice = null;
                    _noticeError = false;
                    SyncDraftFromScheme(loadout, captured);
                    Rebuild();
                });
            }

            var tagBtn = CharacterAttributeVisuals.Button("LoadoutRoleTag", _contentRoot, 662f, 14f, 186f, 46f,
                $"标签：{SkillLoadoutView.DescribeRoleTag(_draftRoleTag)} ▸", false, 18f);
            tagBtn.SetInteractable(CanWrite);
            tagBtn.Button.onClick.AddListener(() =>
            {
                if (!CanWrite) return;
                _draftRoleTag = (_draftRoleTag + 1u) % 4u;
                _draftDirty = true;
                _notice = $"方案{_editingSchemeId}标签已调整为「{SkillLoadoutView.DescribeRoleTag(_draftRoleTag)}」，请保存生效。";
                _noticeError = false;
                Rebuild();
            });

            bool canQuickSwitch = CanWrite && !_draftDirty && _editingSchemeId != activeId;
            var switchBtn = CharacterAttributeVisuals.Button("LoadoutSwitchBtn", _contentRoot, 860f, 14f, 224f, 46f,
                _editingSchemeId == activeId ? "当前已启用" : $"直接启用方案{_editingSchemeId}", canQuickSwitch, 18f);
            switchBtn.SetInteractable(canQuickSwitch);
            switchBtn.Button.onClick.AddListener(SubmitSwitch);

            var backBtn = CharacterAttributeVisuals.Button("LoadoutBackBtn", _contentRoot, 1170f, 14f, 172f, 46f, "返回技能修习", false, 18f);
            backBtn.Button.onClick.AddListener(() => _onCloseToStudy?.Invoke());

            string modeHint = !loadout.CustomEnabled
                ? "当前未开启自定义方案（已学非主动战法默认全部生效）；保存任意方案后即切换为自定义构筑。"
                : $"当前启用：方案{activeId}  ·  正在编辑：方案{_editingSchemeId}{(_draftDirty ? "（有未保存改动）" : "")}  ·  槽位上限：开场 {loadout.MaxOpeningSlots} / 被动 {loadout.MaxPassiveSlots} / 追击 {loadout.MaxPursuitSlots}";
            CreateText("LoadoutSubHeader", _contentRoot, 20f, 68f, 1320f, 26f, modeHint, 15f, Muted);

            // 2. 中栏：5 个装配槽位卡（开场 ×1、被动 ×3、追击 ×1）
            int openingCap = Math.Max(1, (int)loadout.MaxOpeningSlots);
            int passiveCap = Math.Max(1, (int)loadout.MaxPassiveSlots);
            int pursuitCap = Math.Max(1, (int)loadout.MaxPursuitSlots);
            int totalSlots = openingCap + passiveCap + pursuitCap;
            float slotGap = 14f;
            float slotW = Mathf.Floor((1324f - slotGap * (totalSlots - 1)) / totalSlots);
            int col = 0;
            for (int i = 0; i < openingCap; i++)
                BuildSlotCard(snapshot, "开场槽", 4u, i, _draftOpening, 18f + col++ * (slotW + slotGap), 104f, slotW, 174f);
            for (int i = 0; i < passiveCap; i++)
                BuildSlotCard(snapshot, $"被动槽 {i + 1}", 2u, i, _draftPassive, 18f + col++ * (slotW + slotGap), 104f, slotW, 174f);
            for (int i = 0; i < pursuitCap; i++)
                BuildSlotCard(snapshot, "追击槽", 3u, i, _draftPursuit, 18f + col++ * (slotW + slotGap), 104f, slotW, 174f);

            // 3. 下栏：已学非主动战法池（可点击装入/替换互斥组/卸下）
            CreateText("LoadoutPoolHeading", _contentRoot, 20f, 290f, 1320f, 28f,
                "已学非主动战法（点击装入对应槽位；同专精互斥组战法将自动替换）", 17f, Ink, TextAlignmentOptions.MidlineLeft, bold: true);

            var viewport = QdaoUguiFactory.CreateRect("LoadoutPoolViewport", _contentRoot, 18f, 322f, 1324f, 366f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpBg = viewport.gameObject.AddComponent<Image>();
            vpBg.color = new Color(0.95f, 0.92f, 0.84f, 0.55f);
            var poolContent = QdaoUguiFactory.CreateRect("LoadoutPoolContent", viewport, 0f, 0f, 1324f, 366f);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = poolContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;

            var candidates = CollectLearnedNonActiveSkills(snapshot);
            if (candidates.Count == 0)
            {
                CreateText("LoadoutPoolEmpty", poolContent, 20f, 130f, 1284f, 60f,
                    "暂无已学习的被动、追击或开场战法，请先在技能页修习或前往制卷解锁。", 20f, Muted, TextAlignmentOptions.Center);
                poolContent.sizeDelta = new Vector2(1324f, 366f);
            }
            else
            {
                const int cols = 2;
                const float cellW = 648f, cellH = 74f, gapX = 16f, gapY = 10f;
                int rows = (candidates.Count + cols - 1) / cols;
                poolContent.sizeDelta = new Vector2(1324f, Mathf.Max(366f, rows * (cellH + gapY) + 12f));
                for (int i = 0; i < candidates.Count; i++)
                {
                    var entry = candidates[i];
                    var def = entry.Definition;
                    uint slotKind = ResolveSlotKind(def);
                    bool inDraft = ContainsInDraft(entry.Id);
                    uint conflictId = !inDraft && def.exclusiveGroup > 0 ? FindExclusiveConflict(snapshot, def.exclusiveGroup, entry.Id) : 0u;
                    float cx = 6f + (i % cols) * (cellW + gapX);
                    float cy = 6f + (i / cols) * (cellH + gapY);

                    var plate = BattleUiWidgets.CreatePanel("LoadoutPool_" + entry.Id, poolContent, cx, cy, cellW, cellH,
                        inDraft ? CardActiveBg : conflictId != 0 ? CardConflictBg : CardBg, true);
                    var border = plate.gameObject.AddComponent<Outline>();
                    border.effectColor = inDraft ? QdaoUguiTheme.Html("#4B8064") : conflictId != 0 ? Warn : QdaoUguiTheme.Html("#D1C6A6");
                    border.effectDistance = new Vector2(1f, -1f);

                    var btn = plate.gameObject.AddComponent<Button>();
                    btn.targetGraphic = plate;
                    btn.interactable = CanWrite;
                    var capturedEntry = entry;
                    btn.onClick.AddListener(() => ToggleSkillInDraft(snapshot, loadout, capturedEntry, slotKind));

                    var icon = QdaoUguiFactory.CreateImage("Icon", plate.transform, 10f, 11f, 52f, 52f, ResolveSkillIcon(def));
                    icon.preserveAspect = true;
                    icon.raycastTarget = false;

                    string kindTag = SlotKindLabel(slotKind);
                    string exTag = def.exclusiveGroup > 0 ? $" · 互斥组#{def.exclusiveGroup}" : string.Empty;
                    string title = $"{def.name}  Lv.{entry.Rank}";
                    CreateText("Title", plate.transform, 72f, 8f, 390f, 30f, title, 19f, Ink, TextAlignmentOptions.MidlineLeft, bold: true);
                    CreateText("Meta", plate.transform, 72f, 40f, 410f, 24f,
                        $"{kindTag}{exTag}{(entry.Equipped ? " · 服务器已装配" : "")}", 14f, conflictId != 0 ? Warn : Muted);

                    string actionText = inDraft ? "已装入（点卸下）"
                        : conflictId != 0 ? "互斥（点替换）"
                        : "点击装入";
                    CreateText("Action", plate.transform, 488f, 18f, 148f, 38f, actionText, 15f,
                        inDraft ? QdaoUguiTheme.Html("#205B46") : conflictId != 0 ? Warn : Muted, TextAlignmentOptions.MidlineRight, bold: true);
                }
            }

            // 4. 底栏：状态提示 + 重置草稿 + 保存并启用
            string footerMsg = _battleLocked ? "战斗中只读查看，不可修改或切换战法构筑。"
                : _saving ? "正在提交战法构筑，等待服务器确认…"
                : !string.IsNullOrEmpty(_notice) ? _notice
                : "主动技能无需装配全部可用；被动/追击/开场按当前启用方案生效。";
            CreateText("LoadoutFooterNotice", _contentRoot, 20f, 706f, 850f, 44f, footerMsg, 16f,
                _noticeError || _battleLocked ? Warn : Muted, TextAlignmentOptions.MidlineLeft);

            var resetBtn = CharacterAttributeVisuals.Button("LoadoutResetBtn", _contentRoot, 892f, 700f, 196f, 50f, "重置草稿", false, 19f);
            resetBtn.SetInteractable(CanWrite && _draftDirty);
            resetBtn.Button.onClick.AddListener(() =>
            {
                SyncDraftFromScheme(loadout, _editingSchemeId);
                _notice = "已恢复服务器当前方案草稿。";
                _noticeError = false;
                Rebuild();
            });

            var saveBtn = CharacterAttributeVisuals.Button("LoadoutSaveBtn", _contentRoot, 1102f, 700f, 240f, 50f, "保存并启用", true, 19f);
            saveBtn.SetInteractable(CanWrite);
            saveBtn.Button.onClick.AddListener(SubmitSave);
        }

        private void BuildSlotCard(SkillBookSnapshot snapshot, string slotTitle, uint slotKind, int slotIndex,
            List<uint> list, float x, float y, float w, float h)
        {
            uint skillId = slotIndex < list.Count ? list[slotIndex] : 0u;
            var entry = skillId != 0u ? snapshot.Find(skillId) : null;
            var def = entry?.Definition;
            bool filled = def != null;

            var plate = BattleUiWidgets.CreatePanel($"LoadoutSlot_{slotKind}_{slotIndex}", _contentRoot, x, y, w, h,
                filled ? CardActiveBg : CardBg, true);
            var border = plate.gameObject.AddComponent<Outline>();
            border.effectColor = filled ? QdaoUguiTheme.Html("#4B8064") : QdaoUguiTheme.Html("#CAB991");
            border.effectDistance = new Vector2(1f, -1f);

            var btn = plate.gameObject.AddComponent<Button>();
            btn.targetGraphic = plate;
            btn.interactable = CanWrite && filled;
            btn.onClick.AddListener(() =>
            {
                if (!CanWrite || slotIndex >= list.Count) return;
                uint removed = list[slotIndex];
                list.RemoveAt(slotIndex);
                _draftDirty = true;
                var removedName = snapshot.Find(removed)?.Definition?.name ?? $"战法#{removed}";
                _notice = $"已从{slotTitle}卸下「{removedName}」，点「保存并启用」生效。";
                _noticeError = false;
                Rebuild();
            });

            CreateText("SlotKind", plate.transform, 10f, 8f, w - 20f, 24f, slotTitle, 15f, Muted, TextAlignmentOptions.Center, bold: true);
            if (filled)
            {
                var icon = QdaoUguiFactory.CreateImage("SlotIcon", plate.transform, (w - 64f) * 0.5f, 38f, 64f, 64f, ResolveSkillIcon(def));
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                CreateText("SlotName", plate.transform, 8f, 108f, w - 16f, 28f, def.name, 17f, Ink, TextAlignmentOptions.Center, bold: true);
                string sub = def.exclusiveGroup > 0 ? $"互斥组#{def.exclusiveGroup} · 点卸下" : "点击卸下";
                CreateText("SlotSub", plate.transform, 8f, 138f, w - 16f, 24f, sub, 13f, Muted, TextAlignmentOptions.Center);
            }
            else
            {
                CreateText("SlotEmpty", plate.transform, 10f, 76f, w - 20f, 54f, "空槽\n下方点选装入", 15f, Muted, TextAlignmentOptions.Center);
            }
        }

        private void ToggleSkillInDraft(SkillBookSnapshot snapshot, SkillLoadoutView loadout, SkillEntryState entry, uint slotKind)
        {
            if (!CanWrite || entry?.Definition == null) return;
            var list = slotKind == 4u ? _draftOpening : slotKind == 3u ? _draftPursuit : _draftPassive;
            int max = (int)(slotKind == 4u ? loadout.MaxOpeningSlots : slotKind == 3u ? loadout.MaxPursuitSlots : loadout.MaxPassiveSlots);
            if (max <= 0) max = slotKind == 2u ? 3 : 1;

            if (list.Remove(entry.Id))
            {
                _draftDirty = true;
                _notice = $"已卸下「{entry.Definition.name}」。";
                _noticeError = false;
                Rebuild();
                return;
            }

            // 若与草稿中已有战法同属专精互斥组，自动替换并高亮提示
            if (entry.Definition.exclusiveGroup > 0)
            {
                uint conflict = FindExclusiveConflict(snapshot, entry.Definition.exclusiveGroup, entry.Id);
                if (conflict != 0u)
                {
                    RemoveFromAllDraftSlots(conflict);
                    string oldName = snapshot.Find(conflict)?.Definition?.name ?? $"战法#{conflict}";
                    if (list.Count < max)
                    {
                        list.Add(entry.Id);
                        _draftDirty = true;
                        _notice = $"专精互斥组#{entry.Definition.exclusiveGroup}：已替换「{oldName}」→「{entry.Definition.name}」，请保存生效。";
                        _noticeError = false;
                        Rebuild();
                        return;
                    }
                }
            }

            if (list.Count >= max)
            {
                _notice = $"{SlotKindLabel(slotKind)}槽已满（上限 {max}），请先点击上方槽位卸下再装入。";
                _noticeError = true;
                Rebuild();
                return;
            }

            list.Add(entry.Id);
            _draftDirty = true;
            _notice = $"已装入「{entry.Definition.name}」，点「保存并启用」提交服务器。";
            _noticeError = false;
            Rebuild();
        }

        private void SubmitSave()
        {
            if (!CanWrite) return;
            var source = _getSource?.Invoke();
            if (source == null) return;
            _saving = true;
            _notice = "正在保存并启用流派方案…";
            _noticeError = false;
            Rebuild();
            source.SaveLoadout(_editingSchemeId, _draftRoleTag,
                new List<uint>(_draftPassive), new List<uint>(_draftPursuit), new List<uint>(_draftOpening),
                (ok, msg) =>
                {
                    _saving = false;
                    if (ok) _draftDirty = false;
                    _notice = msg;
                    _noticeError = !ok;
                    _setParentStatus?.Invoke(msg, !ok);
                    if (IsVisible) Rebuild();
                });
        }

        private void SubmitSwitch()
        {
            if (!CanWrite || _draftDirty) return;
            var source = _getSource?.Invoke();
            if (source == null) return;
            _saving = true;
            _notice = $"正在切换启用方案{_editingSchemeId}…";
            _noticeError = false;
            Rebuild();
            source.SwitchLoadout(_editingSchemeId, (ok, msg) =>
            {
                _saving = false;
                _notice = msg;
                _noticeError = !ok;
                _setParentStatus?.Invoke(msg, !ok);
                if (IsVisible) Rebuild();
            });
        }

        private bool ContainsInDraft(uint skillId) =>
            _draftOpening.Contains(skillId) || _draftPassive.Contains(skillId) || _draftPursuit.Contains(skillId);

        private void RemoveFromAllDraftSlots(uint skillId)
        {
            _draftOpening.Remove(skillId);
            _draftPassive.Remove(skillId);
            _draftPursuit.Remove(skillId);
        }

        private uint FindExclusiveConflict(SkillBookSnapshot snapshot, uint exclusiveGroup, uint excludeSkillId)
        {
            if (exclusiveGroup == 0 || snapshot == null) return 0u;
            foreach (var list in new[] { _draftOpening, _draftPassive, _draftPursuit })
            {
                for (int i = 0; i < list.Count; i++)
                {
                    uint id = list[i];
                    if (id == excludeSkillId) continue;
                    var def = snapshot.Find(id)?.Definition;
                    if (def != null && def.exclusiveGroup == exclusiveGroup) return id;
                }
            }
            return 0u;
        }

        private static List<SkillEntryState> CollectLearnedNonActiveSkills(SkillBookSnapshot snapshot)
        {
            var result = new List<SkillEntryState>();
            foreach (var e in snapshot.Entries)
            {
                if (e?.Definition == null || e.Rank == 0) continue;
                if (IsConfigurableNonActive(e.Definition)) result.Add(e);
            }
            return result;
        }

        private static bool IsConfigurableNonActive(SkillDefinition def) =>
            def != null && (def.abilityKind == 2u || def.abilityKind == 3u || def.abilityKind == 4u || (def.abilityKind == 0u && def.IsPassive));

        private static uint ResolveSlotKind(SkillDefinition def) =>
            def.abilityKind == 4u ? 4u : def.abilityKind == 3u ? 3u : 2u;

        private static string SlotKindLabel(uint slotKind) => slotKind switch
        {
            4u => "开场",
            3u => "追击",
            _ => "被动",
        };

        private static Sprite ResolveSkillIcon(SkillDefinition def) =>
            (string.IsNullOrEmpty(def.IconResource) ? null : Resources.Load<Sprite>(def.IconResource)) ??
            SeasonEconomyUiArt.LoadGrowthSkillIcon(def.id) ??
            QdaoUguiTheme.LoadSprite("UI/Ugui/GameplayV1/icon_scroll");

        private static TMP_Text CreateText(string name, UnityEngine.Transform parent, float x, float y,
            float w, float h, string value, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool bold = false)
        {
            var t = QdaoUguiFactory.CreateText(name, parent, x, y, w, h, value, size, color, align);
            t.font = bold ? QdaoUguiTheme.ResolveFont() : QdaoUguiTypography.ResolveBodyFont();
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.richText = false;
            t.raycastTarget = false;
            return t;
        }
    }
}
