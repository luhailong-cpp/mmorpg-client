using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.Pet;
using MmorpgClient.UI.Ugui.Guild;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Pet
{
    public sealed partial class PetCultivationPanel
    {
        private readonly Dictionary<uint, uint> _entryTarget = new();
        private PetEntriesInfo _entryBaseline, _entryPreview;
        private int _entryPreviewVersion;
        private bool _entrySaving;
        private readonly List<EntryRow> _entryRows = new();
        private readonly List<Button> _entryPresetButtons = new();
        private static readonly string[] EntryPresetNames = { "方案一", "方案二", "方案三" };
        private Button _entryTagButton, _entryClearAll;
        private Text _entryBudget;
        private RectTransform _entryDetailDialog, _entryTagDialog;
        private sealed class EntryRow { public PetEntryInfo Data; public Text Value, Benefit; public Button Minus, Plus; }
        private PetEntriesInfo EntryState => Selected?.Entries?.ConfigVersion > 0 ? Selected.Entries : null;
        private bool HasEntryDraft => _entryBaseline != null && _entryBaseline.Entries.Any(e => EntryTarget(e.Id) != e.Allocated);
        private uint EntryTarget(uint id) => _entryTarget.TryGetValue(id, out var value) ? value : _entryBaseline?.Entries.FirstOrDefault(e => e.Id == id)?.Allocated ?? EntryState?.Entries.FirstOrDefault(e => e.Id == id)?.Allocated ?? 0;
        private bool EntryConflict => _entryBaseline != null && (EntryState?.PetId != _entryBaseline.PetId || EntryState.StateVersion != _entryBaseline.StateVersion || EntryState.ConfigVersion != _entryBaseline.ConfigVersion);
        private ulong EntryUsed => (EntryState?.UnknownUsedPoints ?? 0) + (EntryState?.Entries.Aggregate<PetEntryInfo, ulong>(0, (sum, e) => sum + EntryTarget(e.Id)) ?? 0);
        private bool EntryEditable => CanWrite && !_entrySaving && EntryState != null && !EntryConflict && _client?.HasPendingEntries(_selectedPetId) != true && _client?.HasPendingPresetSwitch(_selectedPetId) != true;
        private uint ActiveEntryPresetSlot => EntryState?.ActivePresetSlot is >= 1 and <= 3 ? EntryState.ActivePresetSlot : 1u;
        private bool CanChangeEntryPreset => CanWrite && !_entrySaving && EntryState != null && !_client.HasPendingEntries(_selectedPetId);
        private bool CanClearEntries => CanChangeEntryPreset && !_client.HasPendingPresetSwitch(_selectedPetId) &&
            !EntryConflict && EntryState.RemainingPoints < EntryState.TotalPoints;

        private void ClearEntryDraft()
        {
            _entrySaving = false; _entryBaseline = null; _entryTarget.Clear(); _entryPreview = null; ++_entryPreviewVersion;
        }
        private void BuildEntryPoints(float x, float width)
        {
            _entryRows.Clear(); _entryPresetButtons.Clear(); _entryBudget = null; _entryTagButton = _entryClearAll = null;
            if (EntryState == null) { BuildUnavailableEntries(x, width); return; }
            Label("EntryPointsTitle", _body, x + 27, 184, 146, 43, "条目点", 33, Green);
            _entryBudget = Label("EntryPointsBudget", _body, x + 180, 186, width - 209, 42, "", 26, Ink, TextAnchor.MiddleRight);
            GuildUiArt.Line(_body, "EntryHeadingRule", x + 141, 205, width * .15f);

            // 顶部 3 套条目方案切换条 + 用途标签切换（消费权威 Presets 与 ActivePresetSlot）
            uint activeSlot = ActiveEntryPresetSlot;
            uint activeTag = FindPresetSummary(activeSlot)?.Tag ?? 0u;
            bool pendingSwitch = _client?.HasPendingPresetSwitch(_selectedPetId) == true;
            float tagW = 210f;
            float presetGap = 12f;
            float presetW = Mathf.Floor((width - 48f - tagW - presetGap * 3f) / 3f);
            int presetFontSize = Mathf.Clamp(Mathf.FloorToInt(presetW / 12f), 19, 23);
            for (uint slot = 1; slot <= 3; slot++)
            {
                uint capturedSlot = slot;
                var summary = FindPresetSummary(slot);
                string tagText = PetClient.DescribePresetTag(summary?.Tag ?? 0u);
                uint usedPts = summary?.UsedPoints ?? (slot == activeSlot && EntryState.TotalPoints > EntryState.RemainingPoints
                    ? EntryState.TotalPoints - EntryState.RemainingPoints : 0u);
                bool isActive = slot == activeSlot;
                string label = $"{EntryPresetNames[slot - 1]} · {tagText} · 已用 {usedPts}";
                var btn = ActionButton("EntryPreset" + slot, _body, x + 24 + (slot - 1) * (presetW + presetGap), 235, presetW, 48,
                    label, isActive, () => SwitchEntryPresetWithDraftGuard(capturedSlot, PetClient.KeepPresetTag),
                    isActive ? "mail_tab_selected" : "mail_tab_normal", presetFontSize);
                btn.GetComponentInChildren<Text>().horizontalOverflow = HorizontalWrapMode.Overflow;
                if (isActive)
                {
                    var colors = btn.colors;
                    colors.disabledColor = Color.white;
                    btn.colors = colors;
                }
                _entryPresetButtons.Add(btn);
            }
            _entryTagButton = ActionButton("EntryPresetTag", _body, x + width - 24 - tagW, 235, tagW, 48,
                pendingSwitch ? "重试确认方案" : $"用途 {PetClient.DescribePresetTag(activeTag)} ▸", false,
                pendingSwitch ? RetryEntryPreset : ShowEntryPresetTags, null, 23);

            string[] categories = { "全部", "输出", "障碍", "辅助" }; float catW = (width - 98) / 4;
            for (int i = 0; i < categories.Length; i++) { int index = i;
                ActionButton("EntryFilter" + i, _body, x + 24 + i * (catW + 16), 288, catW, 40, categories[i], i == _entryCategory,
                    () => { _entryCategory = index; Render(); }, i == _entryCategory ? "mail_tab_selected" : "mail_tab_normal", 23);
            }
            float rowW = width - 44, current = rowW * .40f, adjust = rowW * .65f, detail = rowW * .92f;
            Art("EntryTableHeader", _body, x + 22, 334, rowW, 35, "guild_table_header");
            Label("EntryDirectionHeader", _body, x + 101, 334, 250, 35, "分配方向", 23, Muted);
            Label("EntryCurrentHeader", _body, x + 22 + current - 90, 334, 180, 35, "已保存投入", 23, Muted, TextAnchor.MiddleCenter);
            Label("EntryDraftHeader", _body, x + 22 + adjust - 90, 334, 180, 35, "调整后", 23, Muted, TextAnchor.MiddleCenter);
            Label("EntryBenefitHeader", _body, x + 22 + detail - 120, 334, 110, 35, "效果", 23, Muted, TextAnchor.MiddleCenter);
            const float rowHeight = 72f, rowStride = 72f, viewportHeight = 462f;
            Scroll("PetEntries", _body, x + 22, 368, rowW, viewportHeight, out var content);
            var entries = EntryState.Entries.Where(e => _entryCategory == 0 || e.Category == _entryCategory).OrderBy(e => e.Sort).ThenBy(e => e.Id).ToArray();
            content.sizeDelta = new Vector2(rowW, Mathf.Max(viewportHeight, (entries.Length - 1) * rowStride + rowHeight));
            for (int i = 0; i < entries.Length; i++) {
                var e = entries[i]; var row = Art("EntryRow" + e.Id, content, 0, i * rowStride, rowW, rowHeight, "mail_row_normal").rectTransform;
                var icon = Art("EntryIcon" + e.Id, row, 19, 13, 46, 46, "row_" + e.Icon);
                if (icon.sprite == null) icon.sprite = CharacterPetArt.Get("round_badge_taiji");
                icon.enabled = icon.sprite != null;
                Label("EntryName" + e.Id, row, 79, 14, current - 160, 44, e.Cap == 0 ? e.Name + " · 未开放" : e.Name, 27, Ink);
                Label("EntryCurrent" + e.Id, row, current - 50, 15, 100, 44, e.Allocated.ToString(), 26, Ink, TextAnchor.MiddleCenter);
                var state = new EntryRow { Data = e };
                state.Minus = ActionButton("EntryMinus" + e.Id, row, adjust - 105, 10, 52, 51, "", false, () => ChangeEntry(e.Id, -1), "step-minus-normal");
                Art("EntryField" + e.Id, row, adjust - 41, 14, 82, 44, "stat_field");
                state.Value = Label("EntryValue" + e.Id, row, adjust - 41, 14, 82, 44, "", 26, Ink, TextAnchor.MiddleCenter);
                state.Plus = ActionButton("EntryPlus" + e.Id, row, adjust + 53, 10, 52, 51, "", false, () => ChangeEntry(e.Id, 1), "step-plus-normal");
                state.Benefit = Label("EntryBenefit" + e.Id, row, detail - 120, 4, 110, 30, "", 21, Green, TextAnchor.MiddleCenter);
                ActionButton("EntryDetail" + e.Id, row, detail - 112, 34, 98, 33, "详情", false,
                    () => ShowEntryDetail(e), null, 20);
                GuildUiArt.Line(row, "EntryRowRule" + e.Id, 4, rowHeight - 1, rowW - 8);
                _entryRows.Add(state);
            }
            Label("EntryHelp", _body, x + 24, 825, width - 48, 34, "各方向共用条目点。", 22, Muted);
            _entryClearAll = ActionButton("EntryClearAll", _body, x + width - 712, 878, 215, 54, "一键清零", false, ClearAllEntries, null, 24);
            _save = ActionButton("EntrySave", _body, x + width - 479, 878, 215, 54, _client?.HasPendingEntries(_selectedPetId) == true ? "重试保存" : "保存分配", true, SaveEntryDraft);
            _discard = ActionButton("EntryRestore", _body, x + width - 246, 878, 224, 54, "恢复当前分配", false,
                () => { if (!CanWrite || _client.HasPendingEntries(_selectedPetId)) return; ClearEntryDraft(); _message = "已恢复服务器当前条目分配。"; _client.RequestEntries(_selectedPetId); Render(); }, null, 26);
            RefreshEntryFields();
        }

        private PetEntryPresetSummary FindPresetSummary(uint slotId)
        {
            if (EntryState?.Presets == null) return null;
            for (int i = 0; i < EntryState.Presets.Count; i++)
            {
                var p = EntryState.Presets[i];
                if (p != null && p.SlotId == slotId) return p;
            }
            return null;
        }

        private void SwitchEntryPresetWithDraftGuard(uint slotId, uint tag)
        {
            bool CanApply() => CanChangeEntryPreset && !_client.HasPendingPresetSwitch(_selectedPetId);
            if (!CanApply() || (tag == PetClient.KeepPresetTag && ActiveEntryPresetSlot == slotId)) return;
            if (HasEntryDraft) Navigate(() => ExecuteSwitchEntryPreset(slotId, tag), CanApply);
            else ExecuteSwitchEntryPreset(slotId, tag);
        }

        private void ShowEntryPresetTags()
        {
            if (!CanChangeEntryPreset || _client.HasPendingPresetSwitch(_selectedPetId)) return;
            if (_entryTagDialog != null) DestroyUi(_entryTagDialog.gameObject);
            _entryTagDialog = Rect("EntryPresetTags", _root, 0, 0, _width, 1080);
            _entryTagDialog.gameObject.AddComponent<Image>().color = Color.clear;
            const float boxWidth = 650f;
            var panel = Art("EntryPresetTagsFrame", _entryTagDialog, (_width - boxWidth) / 2f, 385, boxWidth, 295, "guild_announcement", true).rectTransform;
            uint slot = ActiveEntryPresetSlot;
            uint activeTag = FindPresetSummary(slot)?.Tag ?? 0;
            Label("EntryPresetTagsTitle", panel, 40, 29, boxWidth - 80, 53, $"{EntryPresetNames[slot - 1]}用途", 33, Green, TextAnchor.MiddleCenter);
            GuildUiArt.Line(panel, "EntryPresetTagsRule", 50, 89, boxWidth - 100);
            for (uint tag = 0; tag < 4; ++tag)
            {
                uint selectedTag = tag;
                bool isActive = tag == activeTag;
                var button = ActionButton("EntryUseTag" + tag, panel, 50 + tag * 140, 108, 130, 58,
                    PetClient.DescribePresetTag(tag), isActive, () =>
                    {
                        _entryTagDialog.gameObject.SetActive(false);
                        if (slot == ActiveEntryPresetSlot) SwitchEntryPresetWithDraftGuard(slot, selectedTag);
                    }, isActive ? "mail_tab_selected" : "mail_tab_normal", 27);
                button.interactable = !isActive;
                if (isActive)
                {
                    var colors = button.colors;
                    colors.disabledColor = Color.white;
                    button.colors = colors;
                }
            }
            ActionButton("EntryPresetTagsClose", panel, 230, 201, 190, 57, "返回", false,
                () => _entryTagDialog.gameObject.SetActive(false));
        }

        private void RetryEntryPreset()
        {
            if (!CanChangeEntryPreset || !_client.HasPendingPresetSwitch(_selectedPetId)) return;
            ExecuteSwitchEntryPreset(ActiveEntryPresetSlot, PetClient.KeepPresetTag);
        }

        private void ExecuteSwitchEntryPreset(uint slotId, uint tag)
        {
            if (!CanChangeEntryPreset) return;
            var client = _client;
            ulong pet = _selectedPetId;
            bool isRetry = client.HasPendingPresetSwitch(pet);
            _entrySaving = true;
            _message = isRetry ? "正在确认上次方案操作…" : tag == PetClient.KeepPresetTag
                ? $"正在切换至{EntryPresetNames[slotId - 1]}…" : "正在更新方案用途标签…";
            RefreshFields();
            client.SwitchEntryPreset(pet, slotId, tag, EntryState.StateVersion, EntryState.ConfigVersion,
                (success, _, error) =>
                {
                    if (_disposed || !ReferenceEquals(client, _client) || pet != _selectedPetId) return;
                    _entrySaving = false;
                    if (success)
                    {
                        ClearEntryDraft();
                        _message = isRetry ? "上次方案操作已确认。" : tag == PetClient.KeepPresetTag
                            ? $"已切换至{EntryPresetNames[slotId - 1]}。"
                            : $"{EntryPresetNames[slotId - 1]}用途已更新为「{PetClient.DescribePresetTag(tag)}」。";
                    }
                    else _message = error ?? "切换方案未成功，请重试。";
                    Render();
                });
        }

        private void ClearAllEntries()
        {
            if (!CanClearEntries) return;
            if (HasEntryDraft) Navigate(ExecuteClearAllEntries, () => CanClearEntries);
            else ExecuteClearAllEntries();
        }

        private void ExecuteClearAllEntries()
        {
            if (!CanClearEntries) return;
            var client = _client;
            ulong pet = _selectedPetId;
            var baseline = EntryState;
            _entrySaving = true;
            _message = "正在清零当前方案条目点…";
            RefreshFields();
            client.SaveEntries(pet, baseline.StateVersion, baseline.ConfigVersion, new Dictionary<uint, uint>(),
                (success, authority, error) =>
                {
                    if (_disposed || !ReferenceEquals(client, _client) || pet != _selectedPetId) return;
                    _entrySaving = false;
                    if (success)
                    {
                        ClearEntryDraft();
                        _message = authority?.UnknownUsedPoints > 0
                            ? "可调整的条目点已清零，旧规则的条目点由服务器保留。"
                            : "已免费清零当前方案条目点。";
                    }
                    else _message = error ?? "清零未成功，请重试。";
                    Render();
                });
        }
        private void ShowEntryDetail(PetEntryInfo entry)
        {
            if (_entryDetailDialog != null) DestroyUi(_entryDetailDialog.gameObject);
            _entryDetailDialog = Rect("PetEntryDetailDialog", _root, 0, 0, _width, 1080);
            _entryDetailDialog.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .3f);
            var panel = Art("PetEntryDetailFrame", _entryDetailDialog, (_width - 900) / 2, 265, 900, 550, "guild_announcement", true).rectTransform;
            Label("PetEntryDetailTitle", panel, 60, 40, 780, 60, entry.Name, 34, Ink, TextAnchor.MiddleCenter);
            string unit = entry.Channel == 5 || entry.Channel == 6 || entry.Channel >= 8 ? "个百分点" : "%";
            Label("PetEntryDetailParameters", panel, 65, 110, 770, 55,
                $"单项上限 {entry.Cap} 点    每点 +{entry.PerPointBps / 100f:0.##}{unit}    已保存 {entry.Allocated} 点", 25, Green, TextAnchor.MiddleCenter);
            Scroll("PetEntryDetailBody", panel, 70, 180, 760, 235, out var body);
            var description = Label("PetEntryDetailDescription", body, 0, 0, 760, 235, entry.Desc, 26, Ink);
            float height = Mathf.Max(235, description.preferredHeight); description.rectTransform.sizeDelta = new Vector2(760, height); body.sizeDelta = new Vector2(760, height);
            ActionButton("PetEntryDetailClose", panel, 335, 445, 230, 65, "关闭", true,
                () => _entryDetailDialog.gameObject.SetActive(false));
        }

        private void ChangeEntry(uint id, int delta)
        {
            if (!EntryEditable) return;
            var e = EntryState.Entries.FirstOrDefault(row => row.Id == id); if (e == null) return;
            uint current = EntryTarget(id);
            if (delta > 0 && (current >= e.Cap || EntryUsed >= EntryState.TotalPoints)) return;
            if (delta < 0 && current == 0) return;
            if (_entryBaseline == null) { _entryBaseline = EntryState.Clone(); foreach (var row in _entryBaseline.Entries) _entryTarget[row.Id] = row.Allocated; }
            _entryTarget[id] = delta > 0 ? current + 1 : current - 1; _entryPreview = null;
            if (!HasEntryDraft) { ClearEntryDraft(); _message = "已恢复当前分配。"; RefreshFields(); return; }
            int revision = ++_entryPreviewVersion; ulong pet = _selectedPetId;
            _message = "正在获取服务器收益预览…"; RefreshFields();
            _client.PreviewEntries(pet, _entryBaseline.StateVersion, _entryBaseline.ConfigVersion, new Dictionary<uint,uint>(_entryTarget),
                (preview, error) => { if (_disposed || revision != _entryPreviewVersion || pet != _selectedPetId) return;
                    _entryPreview = preview; _message = error ?? "预览已同步；确认保存后生效。"; RefreshFields(); });
        }
        private void SaveEntryDraft()
        {
            if (!CanWrite || _entrySaving || EntryState == null || _client.HasPendingPresetSwitch(_selectedPetId)) return;
            bool retry = _client.HasPendingEntries(_selectedPetId);
            if (!retry && (!HasEntryDraft || EntryConflict || _entryPreview == null)) return;
            var baseline = _entryBaseline ?? EntryState; ulong pet = _selectedPetId;
            _entrySaving = true; _message = "正在保存条目分配…"; RefreshFields();
            _client.SaveEntries(pet, baseline.StateVersion, baseline.ConfigVersion, new Dictionary<uint,uint>(_entryTarget),
                (success, authority, error) => { if (_disposed || pet != _selectedPetId) return;
                    _entrySaving = false;
                    if (success) { ClearEntryDraft(); _message = "条目分配已保存。"; }
                    else _message = error ?? "保存未成功，草稿已保留。";
                    // PetClient publishes authority after this callback, with version rollback protection.
                    Render(); });
        }
        private void RefreshEntryFields()
        {
            if (_page != PetCultivationPage.EntryPoints || EntryState == null) return;
            ulong used = EntryUsed; if (_entryBudget != null) _entryBudget.text = $"总点数 {EntryState.TotalPoints}       已用 {used}       剩余 {(used <= EntryState.TotalPoints ? EntryState.TotalPoints-used : 0)}";
            foreach (var row in _entryRows) {
                uint value = EntryTarget(row.Data.Id); row.Value.text = value.ToString();
                row.Minus.interactable = EntryEditable && value > 0;
                row.Plus.interactable = EntryEditable && value < row.Data.Cap && used < EntryState.TotalPoints;
                var preview = (_entryPreview ?? (!HasEntryDraft ? EntryState : null))?.Entries.FirstOrDefault(e => e.Id == row.Data.Id);
                row.Benefit.text = row.Data.Cap == 0 ? "未开放" : preview == null ? "预览 —" : $"+{preview.EffectiveBps / 100f:0.##}{(row.Data.Channel == 5 || row.Data.Channel == 6 || row.Data.Channel >= 8 ? "百分点" : "%")}";
            }
            bool pendingSwitch = _client?.HasPendingPresetSwitch(_selectedPetId) == true;
            for (int i = 0; i < _entryPresetButtons.Count; i++)
                _entryPresetButtons[i].interactable = CanChangeEntryPreset && !pendingSwitch && ActiveEntryPresetSlot != (uint)(i + 1);
            if (_entryTagButton != null) _entryTagButton.interactable = CanChangeEntryPreset;
            if (_entryClearAll != null) _entryClearAll.interactable = CanClearEntries;
            if (_save != null) _save.interactable = CanWrite && !_entrySaving && !pendingSwitch && (_client.HasPendingEntries(_selectedPetId) || (HasEntryDraft && !EntryConflict && _entryPreview != null));
            if (_discard != null) _discard.interactable = CanWrite && !_entrySaving && !_client.HasPendingEntries(_selectedPetId) && (HasEntryDraft || EntryConflict);
            if (pendingSwitch && CanChangeEntryPreset && _status != null) _status.text = "上次条目方案操作尚未确认，可点击「重试确认方案」。";
            else if (EntryConflict && !BattleLocked && _status != null) _status.text = "条目状态或规则已变化，草稿未覆盖服务器；请恢复当前分配后重新调整。";
        }
    }
}
