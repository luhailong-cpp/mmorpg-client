using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.Pet;
using MmorpgClient.UI.Ugui.Guild;
using TMPro;
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
        private TMP_Text _entryBudget;
        private RectTransform _entryDetailDialog, _entryTagDialog;

        private sealed class EntryRow
        {
            public PetEntryInfo Data;
            public TMP_Text Value, Benefit;
            public Button Minus, Plus;
        }

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

            Label("EntryPointsTitle", _body, x + 16f, 172f, 130f, 40f, "条目点", 32f, Green, role: TypographyRole.Heading);
            GuildUiArt.Line(_body, "EntryHeadingRule", x + 144f, 192f, 112f);
            _entryBudget = Label("EntryPointsBudget", _body, x + 260f, 176f, width - 264f, 34f, "", 22f, RichInk, TextAlignmentOptions.MidlineRight, role: TypographyRole.Heading);

            // 顶部 3 套条目方案切换条 + 用途标签切换（严守安全内距与自动字号，杜绝溢框）
            uint activeSlot = ActiveEntryPresetSlot;
            uint activeTag = FindPresetSummary(activeSlot)?.Tag ?? 0u;
            bool pendingSwitch = _client?.HasPendingPresetSwitch(_selectedPetId) == true;
            const float tagW = 166f;
            const float presetGap = 10f;
            float presetW = Mathf.Floor((width - tagW - presetGap * 3f) / 3f); // 253px
            for (uint slot = 1; slot <= 3; slot++)
            {
                uint capturedSlot = slot;
                var summary = FindPresetSummary(slot);
                string tagText = PetClient.DescribePresetTag(summary?.Tag ?? 0u);
                uint usedPts = summary?.UsedPoints ?? (slot == activeSlot && EntryState.TotalPoints > EntryState.RemainingPoints
                    ? EntryState.TotalPoints - EntryState.RemainingPoints : 0u);
                bool isActive = slot == activeSlot;
                string label = $"{EntryPresetNames[slot - 1]} · {tagText} · {usedPts}点";
                var btn = ActionButton("EntryPreset" + slot, _body, x + (slot - 1) * (presetW + presetGap), 216f, presetW, 44f,
                    label, isActive, () => SwitchEntryPresetWithDraftGuard(capturedSlot, PetClient.KeepPresetTag),
                    isActive ? "mail_tab_selected" : "button_secondary", 20f, autoSize: true);
                if (isActive)
                {
                    var colors = btn.colors;
                    colors.disabledColor = Color.white;
                    btn.colors = colors;
                }
                _entryPresetButtons.Add(btn);
            }
            _entryTagButton = ActionButton("EntryPresetTag", _body, x + width - tagW, 216f, tagW, 44f,
                pendingSwitch ? "重试确认方案" : $"用途 {PetClient.DescribePresetTag(activeTag)} ▼", false,
                pendingSwitch ? RetryEntryPreset : ShowEntryPresetTags, null, 20f, autoSize: true);

            string[] categories = { "全部", "输出", "障碍", "辅助" };
            float catW = (width - 36f) * 0.25f;
            for (int i = 0; i < categories.Length; i++)
            {
                int index = i;
                ActionButton("EntryFilter" + i, _body, x + i * (catW + 12f), 266f, catW, 40f, categories[i], i == _entryCategory,
                    () => { _entryCategory = index; Render(); }, i == _entryCategory ? "mail_tab_selected" : "button_secondary", 23f);
            }

            float rowW = width, current = rowW * .43f, adjust = rowW * .66f;
            Art("EntryTableHeader", _body, x, 312f, rowW, 38f, "guild_table_header");
            Label("EntryDirectionHeader", _body, x + 64f, 312f, 240f, 38f, "分配方向", 21f, Muted);
            Label("EntryCurrentHeader", _body, x + current - 80f, 312f, 160f, 38f, "已保存投入", 21f, Muted, TextAlignmentOptions.Center);
            Label("EntryDraftHeader", _body, x + adjust - 80f, 312f, 160f, 38f, "调整后", 21f, Muted, TextAlignmentOptions.Center);
            Label("EntryBenefitHeader", _body, x + rowW - 196f, 312f, 190f, 38f, "效果 / 详情", 21f, Muted, TextAlignmentOptions.Center);

            const float rowHeight = 52f, rowStride = 53f, viewportHeight = 318f;
            Scroll("PetEntries", _body, x, 352f, rowW, viewportHeight, out var content);
            var entries = EntryState.Entries.Where(e => _entryCategory == 0 || e.Category == _entryCategory).OrderBy(e => e.Sort).ThenBy(e => e.Id).ToArray();
            content.sizeDelta = new Vector2(rowW, Mathf.Max(viewportHeight, entries.Length == 0 ? viewportHeight : (entries.Length - 1) * rowStride + rowHeight));
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                var row = Art("EntryRow" + e.Id, content, 0f, i * rowStride, rowW, rowHeight, "mail_row_normal").rectTransform;
                var icon = Art("EntryIcon" + e.Id, row, 14f, 7f, 38f, 38f, "row_" + e.Icon);
                if (icon.sprite == null) icon.sprite = CharacterPetArt.Get("round_badge_taiji");
                icon.preserveAspect = true;
                icon.enabled = icon.sprite != null;
                Label("EntryName" + e.Id, row, 64f, 8f, current - 120f, 36f, e.Cap == 0 ? e.Name + " · 未开放" : e.Name, 23f, RichInk);
                Label("EntryCurrent" + e.Id, row, current - 44f, 8f, 88f, 36f, e.Allocated.ToString(), 22f, Ink, TextAlignmentOptions.Center);
                var state = new EntryRow { Data = e };
                state.Minus = ActionButton("EntryMinus" + e.Id, row, adjust - 94f, 6f, 40f, 40f, "", false, () => ChangeEntry(e.Id, -1), "step-minus-normal");
                Art("EntryField" + e.Id, row, adjust - 46f, 8f, 74f, 36f, "stat_field");
                state.Value = Label("EntryValue" + e.Id, row, adjust - 46f, 8f, 74f, 36f, "", 22f, RichInk, TextAlignmentOptions.Center, role: TypographyRole.Heading);
                state.Plus = ActionButton("EntryPlus" + e.Id, row, adjust + 36f, 6f, 40f, 40f, "", false, () => ChangeEntry(e.Id, 1), "step-plus-normal");
                state.Benefit = Label("EntryBenefit" + e.Id, row, rowW - 196f, 8f, 102f, 36f, "", 21f, Green, TextAlignmentOptions.MidlineRight, autoSize: true, minSize: 15f, role: TypographyRole.Heading);
                ActionButton("EntryDetail" + e.Id, row, rowW - 86f, 8f, 78f, 36f, "详情", false,
                    () => ShowEntryDetail(e), null, 20f, autoSize: false);
                GuildUiArt.Line(row, "EntryRowRule" + e.Id, 4f, rowHeight - 1f, rowW - 8f);
                _entryRows.Add(state);
            }

            Label("EntryHelp", _body, x + 4f, 714f, 260f, 34f, "各方向共用条目点。", 19f, Muted);
            _entryClearAll = ActionButton("EntryClearAll", _body, x + width - 678f, FooterSecondaryY, 180f, FooterSecondaryHeight, "一键清零", false, ClearAllEntries, null, 24f);
            _save = ActionButton("EntrySave", _body, x + width - 484f, FooterPrimaryY, 252f, FooterPrimaryHeight,
                _client?.HasPendingEntries(_selectedPetId) == true ? "重试保存" : "保存分配", true, SaveEntryDraft, null, 27f);
            _discard = ActionButton("EntryRestore", _body, x + width - 218f, FooterSecondaryY, 218f, FooterSecondaryHeight, "恢复当前分配", false,
                () => { if (!CanWrite || _client.HasPendingEntries(_selectedPetId)) return; ClearEntryDraft(); _message = "已恢复服务器当前条目分配。"; _client.RequestEntries(_selectedPetId); Render(); }, null, 24f);
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
            _entryTagDialog = Rect("EntryPresetTags", _root, 0f, 0f, _width, 1080f);
            var dim = _entryTagDialog.gameObject.AddComponent<Image>();
            dim.color = ModalDim;
            dim.raycastTarget = true;
            const float boxWidth = 650f;
            var panel = Art("EntryPresetTagsFrame", _entryTagDialog, Mathf.Round((_width - boxWidth) * 0.5f), 392f, boxWidth, 295f, "guild_announcement", true).rectTransform;
            uint slot = ActiveEntryPresetSlot;
            uint activeTag = FindPresetSummary(slot)?.Tag ?? 0;
            Label("EntryPresetTagsTitle", panel, 40f, 26f, boxWidth - 80f, 52f, $"{EntryPresetNames[slot - 1]}用途", 32f, Green, TextAlignmentOptions.Center);
            GuildUiArt.Line(panel, "EntryPresetTagsRule", 50f, 86f, boxWidth - 100f);
            for (uint tag = 0; tag < 4; ++tag)
            {
                uint selectedTag = tag;
                bool isActive = tag == activeTag;
                var button = ActionButton("EntryUseTag" + tag, panel, 50f + tag * 140f, 108f, 130f, 56f,
                    PetClient.DescribePresetTag(tag), isActive, () =>
                    {
                        _entryTagDialog.gameObject.SetActive(false);
                        if (slot == ActiveEntryPresetSlot) SwitchEntryPresetWithDraftGuard(slot, selectedTag);
                    }, isActive ? "mail_tab_selected" : "mail_tab_normal", 25f);
                button.interactable = !isActive;
                if (isActive)
                {
                    var colors = button.colors;
                    colors.disabledColor = Color.white;
                    button.colors = colors;
                }
            }
            ActionButton("EntryPresetTagsClose", panel, 230f, 202f, 190f, 54f, "返回", false,
                () => _entryTagDialog.gameObject.SetActive(false), null, 24f);
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
            _entryDetailDialog = Rect("PetEntryDetailDialog", _root, 0f, 0f, _width, 1080f);
            var dim = _entryDetailDialog.gameObject.AddComponent<Image>();
            dim.color = ModalDim;
            dim.raycastTarget = true;
            var panel = Art("PetEntryDetailFrame", _entryDetailDialog, Mathf.Round((_width - 900f) * 0.5f), 265f, 900f, 550f, "guild_announcement", true).rectTransform;
            Label("PetEntryDetailTitle", panel, 60f, 36f, 780f, 56f, entry.Name, 34f, Green, TextAlignmentOptions.Center);
            string unit = entry.Channel == 5 || entry.Channel == 6 || entry.Channel >= 8 ? "个百分点" : "%";
            Label("PetEntryDetailParameters", panel, 65f, 106f, 770f, 48f,
                $"单项上限 {entry.Cap} 点    每点 +{entry.PerPointBps / 100f:0.##}{unit}    已保存 {entry.Allocated} 点",
                24f, Green, TextAlignmentOptions.Center);
            Scroll("PetEntryDetailBody", panel, 70f, 174f, 760f, 244f, out var body);
            var description = Label("PetEntryDetailDescription", body, 0f, 0f, 760f, 244f, entry.Desc, 25f, Ink,
                TextAlignmentOptions.TopLeft, wrap: true);
            description.ForceMeshUpdate();
            float height = Mathf.Max(244f, description.GetPreferredValues(entry.Desc ?? "", 760f, float.PositiveInfinity).y + 16f);
            description.rectTransform.sizeDelta = new Vector2(760f, height);
            body.sizeDelta = new Vector2(760f, height);
            ActionButton("PetEntryDetailClose", panel, 335f, 448f, 230f, 58f, "关闭", true,
                () => _entryDetailDialog.gameObject.SetActive(false), null, 25f);
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
            _client.PreviewEntries(pet, _entryBaseline.StateVersion, _entryBaseline.ConfigVersion, new Dictionary<uint, uint>(_entryTarget),
                (preview, error) =>
                {
                    if (_disposed || revision != _entryPreviewVersion || pet != _selectedPetId) return;
                    _entryPreview = preview; _message = error ?? "预览已同步；确认保存后生效。"; RefreshFields();
                });
        }

        private void SaveEntryDraft()
        {
            if (!CanWrite || _entrySaving || EntryState == null || _client.HasPendingPresetSwitch(_selectedPetId)) return;
            bool retry = _client.HasPendingEntries(_selectedPetId);
            if (!retry && (!HasEntryDraft || EntryConflict || _entryPreview == null)) return;
            var baseline = _entryBaseline ?? EntryState; ulong pet = _selectedPetId;
            _entrySaving = true; _message = "正在保存条目分配…"; RefreshFields();
            _client.SaveEntries(pet, baseline.StateVersion, baseline.ConfigVersion, new Dictionary<uint, uint>(_entryTarget),
                (success, authority, error) =>
                {
                    if (_disposed || pet != _selectedPetId) return;
                    _entrySaving = false;
                    if (success) { ClearEntryDraft(); _message = "条目分配已保存。"; }
                    else _message = error ?? "保存未成功，草稿已保留。";
                    Render();
                });
        }

        private void RefreshEntryFields()
        {
            if (_page != PetCultivationPage.EntryPoints || EntryState == null) return;
            ulong used = EntryUsed;
            if (_entryBudget != null)
                _entryBudget.text = $"总点数 {EntryState.TotalPoints}     已用 {used}     剩余 {(used <= EntryState.TotalPoints ? EntryState.TotalPoints - used : 0)}";
            foreach (var row in _entryRows)
            {
                uint value = EntryTarget(row.Data.Id);
                row.Value.text = value.ToString();
                row.Minus.interactable = EntryEditable && value > 0;
                row.Plus.interactable = EntryEditable && value < row.Data.Cap && used < EntryState.TotalPoints;
                var preview = (_entryPreview ?? (!HasEntryDraft ? EntryState : null))?.Entries.FirstOrDefault(e => e.Id == row.Data.Id);
                row.Benefit.text = row.Data.Cap == 0 ? "未开放" : preview == null ? "预览 —" :
                    $"+{preview.EffectiveBps / 100f:0.##}{(row.Data.Channel == 5 || row.Data.Channel == 6 || row.Data.Channel >= 8 ? "百分点" : "%")}";
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
