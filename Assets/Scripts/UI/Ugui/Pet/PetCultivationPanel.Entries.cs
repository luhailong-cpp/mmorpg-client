using System;
using System.Collections.Generic;
using System.Linq;
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
        private Text _entryBudget;
        private RectTransform _entryDetailDialog;
        private sealed class EntryRow { public PetEntryInfo Data; public Text Value, Benefit; public Button Minus, Plus; }
        private PetEntriesInfo EntryState => Selected?.Entries?.ConfigVersion > 0 ? Selected.Entries : null;
        private bool HasEntryDraft => _entryBaseline != null && _entryBaseline.Entries.Any(e => EntryTarget(e.Id) != e.Allocated);
        private uint EntryTarget(uint id) => _entryTarget.TryGetValue(id, out var value) ? value : _entryBaseline?.Entries.FirstOrDefault(e => e.Id == id)?.Allocated ?? EntryState?.Entries.FirstOrDefault(e => e.Id == id)?.Allocated ?? 0;
        private bool EntryConflict => _entryBaseline != null && (EntryState?.PetId != _entryBaseline.PetId || EntryState.StateVersion != _entryBaseline.StateVersion || EntryState.ConfigVersion != _entryBaseline.ConfigVersion);
        private ulong EntryUsed => (EntryState?.UnknownUsedPoints ?? 0) + (EntryState?.Entries.Aggregate<PetEntryInfo, ulong>(0, (sum, e) => sum + EntryTarget(e.Id)) ?? 0);
        private bool EntryEditable => CanWrite && !_entrySaving && EntryState != null && !EntryConflict && _client?.HasPendingEntries(_selectedPetId) != true;

        private void ClearEntryDraft()
        {
            _entrySaving = false; _entryBaseline = null; _entryTarget.Clear(); _entryPreview = null; ++_entryPreviewVersion;
        }
        private void BuildEntryPoints(float x, float width)
        {
            _entryRows.Clear(); _entryBudget = null;
            if (EntryState == null) { BuildUnavailableEntries(x, width); return; }
            Label("EntryPointsTitle", _root, x + 30, 197, 120, 45, "条目点", 28, Ink);
            _entryBudget = Label("EntryPointsBudget", _root, x + 155, 198, width - 190, 45, "", 26, Ink);
            string[] categories = { "全部", "输出", "障碍", "辅助" }; float catW = (width - 98) / 4;
            for (int i = 0; i < categories.Length; i++) { int index = i;
                ActionButton("EntryFilter" + i, _root, x + 24 + i * (catW + 16), 253, catW, 47, categories[i], i == _entryCategory,
                    () => { _entryCategory = index; Render(); }, i == _entryCategory ? "mail_tab_selected" : "mail_tab_normal", 24);
            }
            float rowW = width - 44, current = rowW * .40f, adjust = rowW * .65f, detail = rowW * .92f;
            Label("EntryDirectionHeader", _root, x + 101, 318, 250, 36, "分配方向", 22, Muted);
            Label("EntryCurrentHeader", _root, x + 22 + current - 90, 318, 180, 36, "已保存投入", 22, Muted, TextAnchor.MiddleCenter);
            Label("EntryDraftHeader", _root, x + 22 + adjust - 90, 318, 180, 36, "调整后", 22, Muted, TextAnchor.MiddleCenter);
            Scroll("PetEntries", _root, x + 22, 359, rowW, 468, out var content);
            var entries = EntryState.Entries.Where(e => _entryCategory == 0 || e.Category == _entryCategory).OrderBy(e => e.Sort).ThenBy(e => e.Id).ToArray();
            content.sizeDelta = new Vector2(rowW, Mathf.Max(468, entries.Length * 81));
            for (int i = 0; i < entries.Length; i++) {
                var e = entries[i]; var row = Art("EntryRow" + e.Id, content, 0, i * 81, rowW, 72, "mail_row_normal").rectTransform;
                Art("EntryIcon" + e.Id, row, 19, 13, 46, 46, "row_" + e.Icon);
                Label("EntryName" + e.Id, row, 79, 14, current - 160, 44, e.Cap == 0 ? e.Name + " · 未开放" : e.Name, 25, Ink);
                Label("EntryCurrent" + e.Id, row, current - 50, 15, 100, 44, e.Allocated.ToString(), 24, Ink, TextAnchor.MiddleCenter);
                var state = new EntryRow { Data = e };
                state.Minus = ActionButton("EntryMinus" + e.Id, row, adjust - 105, 10, 52, 51, "", false, () => ChangeEntry(e.Id, -1), "step-minus-normal");
                Art("EntryField" + e.Id, row, adjust - 41, 14, 82, 44, "stat_field");
                state.Value = Label("EntryValue" + e.Id, row, adjust - 41, 14, 82, 44, "", 23, Ink, TextAnchor.MiddleCenter);
                state.Plus = ActionButton("EntryPlus" + e.Id, row, adjust + 53, 10, 52, 51, "", false, () => ChangeEntry(e.Id, 1), "step-plus-normal");
                state.Benefit = Label("EntryBenefit" + e.Id, row, detail - 120, 4, 110, 30, "", 18, Green, TextAnchor.MiddleCenter);
                ActionButton("EntryDetail" + e.Id, row, detail - 112, 34, 98, 33, "详情", false,
                    () => ShowEntryDetail(e), null, 18);
                _entryRows.Add(state);
            }
            Label("EntryHelp", _root, x + 24, 839, width - 48, 40, "各方向共用条目点；可免费减点重分配，0 点仍可使用已学技能。", 20, Muted);
            _save = ActionButton("EntrySave", _root, x + width - 479, 915, 215, 60, _client?.HasPendingEntries(_selectedPetId) == true ? "重试保存" : "保存分配", true, SaveEntryDraft);
            _discard = ActionButton("EntryRestore", _root, x + width - 246, 915, 224, 60, "恢复当前分配", false,
                () => { if (!CanWrite || _client.HasPendingEntries(_selectedPetId)) return; ClearEntryDraft(); _message = "已恢复服务器当前条目分配。"; _client.RequestEntries(_selectedPetId); Render(); }, null, 26);
            RefreshEntryFields();
        }
        private void ShowEntryDetail(PetEntryInfo entry)
        {
            if (_entryDetailDialog != null) DestroyUi(_entryDetailDialog.gameObject);
            _entryDetailDialog = Rect("PetEntryDetailDialog", _root, 0, 0, _width, 1080);
            _entryDetailDialog.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .3f);
            var panel = Art("PetEntryDetailFrame", _entryDetailDialog, (_width - 900) / 2, 265, 900, 550, "window_frame", true).rectTransform;
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
            if (!CanWrite || _entrySaving || EntryState == null) return;
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
            if (_save != null) _save.interactable = CanWrite && !_entrySaving && (_client.HasPendingEntries(_selectedPetId) || (HasEntryDraft && !EntryConflict && _entryPreview != null));
            if (_discard != null) _discard.interactable = CanWrite && !_entrySaving && !_client.HasPendingEntries(_selectedPetId) && (HasEntryDraft || EntryConflict);
            if (EntryConflict && !BattleLocked && _status != null) _status.text = "条目状态或规则已变化，草稿未覆盖服务器；请恢复当前分配后重新调整。";
        }
    }
}
