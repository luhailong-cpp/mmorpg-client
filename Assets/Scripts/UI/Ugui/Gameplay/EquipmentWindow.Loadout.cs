using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MmorpgClient.Game.PlayerFeatures;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.EquipmentUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 装备流派方案预设 V1 uGUI（主管理弹窗 + 付费/扩容符解锁确认弹窗 + 背包防误操作角标）。
    /// 复用 Assets/Resources/UI/Ugui/EquipLoadoutV1/ 全部 8 张无字透明切图，缺图自动回退通用面板。
    /// </summary>
    public sealed partial class EquipmentWindow
    {
        private uint _loadoutSelectedSchemeId = 1u;
        private string _loadoutNameDraft = string.Empty;
        private uint _loadoutRoleTagDraft;
        private uint _loadoutLinkedSkillDraft;
        private uint _loadoutLinkedAttrDraft;
        private bool _loadoutApplyLinked = true;
        private bool _loadoutUseBindDiamondFirst = true;
        private bool _loadoutDraftSynced;
        private TMP_InputField _loadoutNameInput;

        public void OpenEquipLoadouts()
        {
            if (_disposed) return;
            _modal = Modal.EquipLoadout;
            _loadoutDraftSynced = false;
            _notice = string.Empty;
            var snap = _client?.EquipLoadouts;
            if (snap != null)
            {
                _loadoutSelectedSchemeId = snap.ActiveSchemeId > 0u ? snap.ActiveSchemeId : 1u;
                SyncLoadoutDraftFromSnapshot(snap, _loadoutSelectedSchemeId);
            }
            else
            {
                _client?.RequestEquipLoadouts();
            }
            Render();
        }

        private void OnEquipLoadoutSwitched(SwitchEquipLoadoutResponse response)
        {
            _notice = BuildLoadoutSwitchNotice(response);
            _loadoutDraftSynced = false;
            if (response?.Snapshot != null)
            {
                _loadoutSelectedSchemeId = response.Snapshot.ActiveSchemeId > 0u ? response.Snapshot.ActiveSchemeId : 1u;
                SyncLoadoutDraftFromSnapshot(response.Snapshot, _loadoutSelectedSchemeId);
            }
            if (IsVisible)
            {
                Render();
                _attributes?.RequestPanel();
            }
        }

        private void OnEquipLoadoutUnlocked(UnlockEquipLoadoutResponse response)
        {
            uint unlocked = response?.ResultUnlockedCount ?? 0u;
            _notice = unlocked > 0u
                ? $"已解锁【{EquipLoadoutModel.DefaultSchemeName(unlocked)}】，可立即保存并一键切换流派！"
                : "新装备流派方案位已解锁！";
            _loadoutSelectedSchemeId = unlocked > 0u ? unlocked : _loadoutSelectedSchemeId;
            _loadoutDraftSynced = false;
            _modal = Modal.EquipLoadout;
            if (response?.Snapshot != null) SyncLoadoutDraftFromSnapshot(response.Snapshot, _loadoutSelectedSchemeId);
            if (IsVisible) Render();
        }

        private static EquipLoadoutSchemeInfo FindLoadoutScheme(EquipLoadoutSnapshot snap, uint schemeId)
            => snap?.Schemes.FirstOrDefault(s => s != null && s.SchemeId == schemeId);

        private static string BuildLoadoutSwitchNotice(SwitchEquipLoadoutResponse response)
        {
            if (response == null) return "装备方案已切换";
            var scheme = FindLoadoutScheme(response.Snapshot, response.Snapshot?.ActiveSchemeId ?? 0u);
            var sb = new StringBuilder();
            sb.Append(scheme != null ? $"已切换至【{EquipLoadoutModel.SchemeDisplayName(scheme)}】" : "装备方案已切换");
            string missing = EquipLoadoutModel.FormatMissingSlotsNotice(response.MissingSlots, scheme);
            if (!string.IsNullOrEmpty(missing)) sb.Append("；").Append(missing);
            if (response.LinkedNotices != null && response.LinkedNotices.Count > 0)
                sb.Append("；").Append(string.Join("；", response.LinkedNotices));
            return sb.ToString();
        }

        private void SyncLoadoutDraftFromSnapshot(EquipLoadoutSnapshot snap, uint schemeId)
        {
            var scheme = FindLoadoutScheme(snap, schemeId);
            if (scheme == null) return;
            _loadoutNameDraft = scheme.Name ?? string.Empty;
            _loadoutRoleTagDraft = scheme.RoleTag;
            _loadoutLinkedSkillDraft = scheme.LinkedSkillSchemeId;
            _loadoutLinkedAttrDraft = scheme.LinkedAttributeSchemeId;
            _loadoutDraftSynced = true;
        }

        private static Sprite LoadEquipLoadoutSprite(string resourceKey)
            => string.IsNullOrEmpty(resourceKey) ? null : Resources.Load<Sprite>(resourceKey);

        private void DrawLoadoutInUseBadge(UnityEngine.Transform parent, ulong itemGuid, float x, float y, float w = 136f, float h = 38f)
        {
            var schemes = EquipLoadoutModel.ReferencedSchemesOf(_client?.EquipLoadouts, itemGuid);
            string badgeText = EquipLoadoutModel.FormatInUseBadgeText(schemes);
            if (string.IsNullOrEmpty(badgeText)) return;
            var sprite = LoadEquipLoadoutSprite(EquipLoadoutModel.BadgeInUseKey);
            var img = QdaoUguiFactory.CreateImage("LoadoutInUseBadge", parent, x, y, w, h, sprite, false);
            if (sprite != null) SeasonEconomyUiArt.ApplyHorizontalSlice(img, sprite, h);
            else img.color = Jade;
            var lbl = Label(img.transform, "LoadoutInUseTxt", badgeText, 10f, 0f, w - 20f, h, 22f, false, Cream);
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.enableAutoSizing = true;
            lbl.fontSizeMin = 15f;
            lbl.fontSizeMax = 22f;
        }

        private void RenderEquipLoadoutModal()
        {
            var snap = _client?.EquipLoadouts;
            if (snap != null && !_loadoutDraftSynced)
            {
                if (_loadoutSelectedSchemeId == 0u || _loadoutSelectedSchemeId > snap.UnlockedSchemeCount)
                    _loadoutSelectedSchemeId = snap.ActiveSchemeId > 0u ? snap.ActiveSchemeId : 1u;
                SyncLoadoutDraftFromSnapshot(snap, _loadoutSelectedSchemeId);
            }

            const float mx = 420f, my = 80f, mw = 3126f, mh = 1420f;
            var bgSprite = LoadEquipLoadoutSprite(EquipLoadoutModel.PanelMainKey);
            if (bgSprite != null)
            {
                var bg = QdaoUguiFactory.CreateImage("EquipLoadoutMainBg", _modalLayer, mx, my, mw, mh, bgSprite, true);
                bg.type = Image.Type.Simple;
                bg.preserveAspect = false;
            }
            else
            {
                Panel(_modalLayer, "EquipLoadoutMainPaper", mx, my, mw, mh, true);
            }

            Label(_modalLayer, "EquipLoadoutTitle", "装备流派方案预设", mx + 980f, my + 44f, 1166f, 84f, 60f, false, Jade)
                .alignment = TextAlignmentOptions.Center;

            if (snap == null)
            {
                string waitText = _client?.EquipLoadoutsLoading == true ? "正在同步装备流派方案…" : "装备流派方案暂未同步，请点击刷新。";
                Label(_modalLayer, "EquipLoadoutWait", waitText, mx + 360f, my + 480f, mw - 720f, 120f, 46f, true, Muted)
                    .alignment = TextAlignmentOptions.Center;
                Button(_modalLayer, "EquipLoadoutRetry", "刷新方案", mx + 920f, my + 1230f, 520f, () => _client?.RequestEquipLoadouts(), true);
                Button(_modalLayer, "EquipLoadoutCloseEmpty", "关闭", mx + 1680f, my + 1230f, 520f, CloseModal);
                return;
            }

            // 顶部 1..5 方案页签行（复用 tab_loadout_active / unlocked / locked）
            float tabW = 532f, tabH = 92f, tabGap = 24f;
            float tabStartX = mx + (mw - (5f * tabW + 4f * tabGap)) * 0.5f;
            float tabY = my + 150f;
            for (int i = 0; i < snap.Schemes.Count && i < 5; ++i)
            {
                var scheme = snap.Schemes[i];
                bool isSelected = scheme.Unlocked && scheme.SchemeId == _loadoutSelectedSchemeId;
                bool isActive = scheme.Unlocked && scheme.SchemeId == snap.ActiveSchemeId;
                string spriteKey = !scheme.Unlocked
                    ? EquipLoadoutModel.TabLockedKey
                    : (isSelected ? EquipLoadoutModel.TabActiveKey : EquipLoadoutModel.TabUnlockedKey);
                var tabSprite = LoadEquipLoadoutSprite(spriteKey);
                float tx = tabStartX + i * (tabW + tabGap);
                uint capturedId = scheme.SchemeId;
                bool capturedUnlocked = scheme.Unlocked;
                var tabBtn = QdaoUguiFactory.CreateArtButton("LoadoutTab_" + scheme.SchemeId, _modalLayer, tx, tabY, tabW, tabH, tabSprite, out var tabImg);
                if (tabSprite != null) SeasonEconomyUiArt.ApplyHorizontalSlice(tabImg, tabSprite, tabH);
                else tabImg.color = !scheme.Unlocked ? new Color(0.84f, 0.80f, 0.72f, 0.92f) : (isSelected ? Jade : Cream);
                tabBtn.onClick.AddListener(() =>
                {
                    if (capturedUnlocked)
                    {
                        _loadoutSelectedSchemeId = capturedId;
                        SyncLoadoutDraftFromSnapshot(snap, capturedId);
                        Render();
                    }
                    else
                    {
                        _modal = Modal.EquipLoadoutUnlock;
                        Render();
                    }
                });
                string tabText = EquipLoadoutModel.FormatTabLabel(scheme, isActive) + (isActive ? " (当前)" : string.Empty);
                var tabLbl = Label(tabBtn.transform, "Lbl", tabText, 24f, 0f, tabW - 48f, tabH, 32f, false, isSelected ? Cream : Ink);
                tabLbl.alignment = TextAlignmentOptions.Center;
                tabLbl.enableAutoSizing = true;
                tabLbl.fontSizeMin = 20f;
                tabLbl.fontSizeMax = 32f;
            }

            var currentScheme = FindLoadoutScheme(snap, _loadoutSelectedSchemeId)
                ?? snap.Schemes.FirstOrDefault(s => s.Unlocked);
            if (currentScheme == null) return;

            // 左半区：8 槽装备引用明细（缺失槽叠 badge_loadout_slot_missing）
            float leftX = mx + 120f, leftY = my + 276f;
            Label(_modalLayer, "LoadoutSlotsHeading",
                $"方案装备槽位（共 {currentScheme.Slots.Count} 槽 · 缺失 {currentScheme.MissingCount} 槽）",
                leftX, leftY, 1380f, 54f, 38f, false, Jade);

            var missingSprite = LoadEquipLoadoutSprite(EquipLoadoutModel.BadgeSlotMissingKey);
            float cellW = 668f, cellH = 176f, cellGapX = 28f, cellGapY = 20f;
            for (int i = 0; i < currentScheme.Slots.Count && i < 8; ++i)
            {
                var sv = currentScheme.Slots[i];
                int col = i % 2;
                int row = i / 2;
                float sx = leftX + col * (cellW + cellGapX);
                float sy = leftY + 64f + row * (cellH + cellGapY);
                var card = ChoiceRow(_modalLayer, "LoadoutSlot_" + sv.Slot, "", sx, sy, cellW, cellH, null,
                    selected: sv.Status == EquipLoadoutSlotStatus.EquipLoadoutSlotEquipped, enabled: false);
                if (sv.Item != null && sv.Status != EquipLoadoutSlotStatus.EquipLoadoutSlotMissingOrStored)
                {
                    Icon(card.transform, sv.Item, 16f, 16f, 140f);
                    if (sv.Item.EnhanceMaxLevel > 0)
                        Label(card.transform, "Enh", "+" + sv.Item.EnhanceLevel, 108f, 10f, 76f, 42f, 28f, false, Jade);
                }
                else if (sv.Status == EquipLoadoutSlotStatus.EquipLoadoutSlotMissingOrStored)
                {
                    if (missingSprite != null)
                        QdaoUguiFactory.CreateImage("MissingIcon", card.transform, 28f, 28f, 116f, 116f, missingSprite, false).preserveAspect = true;
                    else
                        Label(card.transform, "MissingTxt", "!", 44f, 20f, 90f, 120f, 72f, false, Danger);
                }
                else
                {
                    Label(card.transform, "EmptyPlus", "—", 48f, 28f, 96f, 100f, 64f, false, Muted);
                }

                string slotTitle = $"{EquipLoadoutModel.SlotLabel(sv)} · {EquipLoadoutModel.SlotStatusText(sv.Status)}";
                string itemTitle = sv.Item != null ? Name(sv.Item) : "未指定装备";
                Label(card.transform, "SlotName", slotTitle, 174f, 16f, cellW - 190f, 48f, 30f, false,
                    sv.Status == EquipLoadoutSlotStatus.EquipLoadoutSlotMissingOrStored ? Danger : Jade);
                Label(card.transform, "ItemName", itemTitle, 174f, 72f, cellW - 190f, 84f, 30f, true, Ink);
            }

            // 右半区：方案名称、流派标签、联动战法与加点方案设置
            float rightX = mx + 1580f, rightW = mw - 1700f;
            Label(_modalLayer, "LoadoutConfigHeading", "当前方案设置（保存即记录当前身上 8 槽装备）",
                rightX, leftY, rightW, 54f, 36f, false, Jade);

            Label(_modalLayer, "NameLbl", "方案名称（最多 12 字）：", rightX, leftY + 74f, 420f, 48f, 32f, false, Ink);
            var inputBg = ChoiceRow(_modalLayer, "LoadoutNameBox", "", rightX, leftY + 130f, rightW, 84f, null, enabled: false);
            _loadoutNameInput = QdaoUguiFactory.CreateInputField("LoadoutNameInput", inputBg.transform, 16f, 10f, rightW - 32f, 64f,
                $"留空默认「{EquipLoadoutModel.DefaultSchemeName(currentScheme.SchemeId)}」", EquipLoadoutModel.MaxNameCodePoints);
            if (_loadoutNameInput.textComponent != null) _loadoutNameInput.textComponent.richText = false;
            _loadoutNameInput.text = _loadoutNameDraft;
            _loadoutNameInput.onValueChanged.AddListener(v => _loadoutNameDraft = v ?? string.Empty);

            Label(_modalLayer, "RoleLbl", "流派定位标签：", rightX, leftY + 236f, 420f, 48f, 32f, false, Ink);
            float tagW = (rightW - 4f * 14f) / 5f;
            for (uint t = 0u; t <= 4u; ++t)
            {
                uint capTag = t;
                ChoiceRow(_modalLayer, "RoleTag_" + t, EquipLoadoutModel.RoleTagName(t),
                    rightX + t * (tagW + 14f), leftY + 292f, tagW, 74f,
                    () => { _loadoutRoleTagDraft = capTag; Render(); }, selected: _loadoutRoleTagDraft == t);
            }

            string linkedSkillLabel = FormatLinkedOptionLabel(snap.SkillSchemes, _loadoutLinkedSkillDraft, "战法");
            Label(_modalLayer, "LinkSkillLbl", "联动战法方案：", rightX, leftY + 394f, 320f, 56f, 32f, false, Ink);
            ChoiceRow(_modalLayer, "LinkSkillCycle", linkedSkillLabel, rightX + 330f, leftY + 388f, rightW - 330f, 76f, () =>
            {
                _loadoutLinkedSkillDraft = CycleLinkedScheme(snap.SkillSchemes, _loadoutLinkedSkillDraft);
                Render();
            }, selected: _loadoutLinkedSkillDraft > 0u);

            string linkedAttrLabel = FormatLinkedOptionLabel(snap.AttributeSchemes, _loadoutLinkedAttrDraft, "加点");
            Label(_modalLayer, "LinkAttrLbl", "联动加点方案：", rightX, leftY + 492f, 320f, 56f, 32f, false, Ink);
            ChoiceRow(_modalLayer, "LinkAttrCycle", linkedAttrLabel, rightX + 330f, leftY + 486f, rightW - 330f, 76f, () =>
            {
                _loadoutLinkedAttrDraft = CycleLinkedScheme(snap.AttributeSchemes, _loadoutLinkedAttrDraft);
                Render();
            }, selected: _loadoutLinkedAttrDraft > 0u);

            ChoiceRow(_modalLayer, "ApplyLinkedToggle",
                (_loadoutApplyLinked ? "[已开启] " : "[未开启] ") + "一键切方案时同步切换已关联的战法与加点方案",
                rightX, leftY + 592f, rightW, 78f,
                () => { _loadoutApplyLinked = !_loadoutApplyLinked; Render(); }, selected: _loadoutApplyLinked);

            var targetForInherit = FindItem(_selectedItem) ?? _client?.Equipment?.Items.FirstOrDefault(x => x.EquipProfile > 0);
            Button(_modalLayer, "LoadoutInheritShortcut", "副套强化继承", rightX, leftY + 706f, 480f, () =>
            {
                if (targetForInherit != null) OpenInheritEnhance(targetForInherit.ItemId);
                else { _notice = "请先拥有一件可继承强化的副套装备"; Render(); }
            }, enabled: targetForInherit != null && !WriteBlocked);

            string errOrNote = !string.IsNullOrEmpty(_client.EquipLoadoutError) ? _client.EquipLoadoutError : _notice;
            Label(_modalLayer, "LoadoutInlineNotice", errOrNote, leftX, my + 1126f, mw - 240f, 64f, 34f, false,
                !string.IsNullOrEmpty(_client.EquipLoadoutError) ? Danger : Jade);

            bool busy = _client.BusyEquipLoadout || _client.BusyEquipLoadoutUnlock || _client.BusyEquip || WriteBlocked;
            Button(_modalLayer, "LoadoutSaveBtn", _client.BusyEquipLoadout ? "保存中…" : "保存当前穿着到此方案",
                mx + 180f, my + 1218f, 840f, () =>
                {
                    string rawName = (_loadoutNameInput != null ? _loadoutNameInput.text : _loadoutNameDraft)?.Trim() ?? string.Empty;
                    if (!EquipLoadoutModel.ValidateNameDraft(rawName, out string nameErr))
                    {
                        _notice = nameErr;
                        Render();
                        return;
                    }
                    _client.SaveEquipLoadout(currentScheme.SchemeId, rawName, _loadoutRoleTagDraft,
                        captureCurrentEquipped: true, explicitSlots: null, _loadoutLinkedSkillDraft, _loadoutLinkedAttrDraft);
                }, primary: false, enabled: !busy);

            Button(_modalLayer, "LoadoutSwitchBtn", _client.BusyEquipLoadout ? "切换中…" : "一键切换本方案",
                mx + 1140f, my + 1218f, 840f, () =>
                {
                    _client.SwitchEquipLoadout(currentScheme.SchemeId, _loadoutApplyLinked);
                }, primary: true, enabled: !busy);

            if (_client.HasPendingEquipLoadoutUnlock)
            {
                Button(_modalLayer, "LoadoutRecoverUnlock", "确认上次解锁", mx + 2100f, my + 1218f, 440f,
                    () => _client.RecoverEquipLoadoutUnlock(), true, !_client.BusyEquipLoadout && !_client.BusyEquipLoadoutUnlock);
                Button(_modalLayer, "LoadoutClose", "关闭", mx + 2580f, my + 1218f, 360f, CloseModal);
            }
            else
            {
                Button(_modalLayer, "LoadoutClose", "关闭", mx + 2180f, my + 1218f, 680f, CloseModal);
            }
        }

        private void RenderEquipLoadoutUnlockModal()
        {
            var snap = _client?.EquipLoadouts;
            const float mx = 420f, my = 80f, mw = 3126f, mh = 1420f;
            var bgSprite = LoadEquipLoadoutSprite(EquipLoadoutModel.PanelUnlockKey);
            if (bgSprite != null)
            {
                var bg = QdaoUguiFactory.CreateImage("EquipLoadoutUnlockBg", _modalLayer, mx, my, mw, mh, bgSprite, true);
                bg.type = Image.Type.Simple;
                bg.preserveAspect = false;
            }
            else
            {
                Panel(_modalLayer, "EquipLoadoutUnlockPaper", mx, my, mw, mh, true);
            }

            Label(_modalLayer, "UnlockTitle", "解锁新装备流派方案位", mx + 920f, my + 56f, 1286f, 86f, 60f, false, Jade)
                .alignment = TextAlignmentOptions.Center;

            var iconSprite = LoadEquipLoadoutSprite(EquipLoadoutModel.ExpandTalismanIconKey);
            if (iconSprite != null)
                QdaoUguiFactory.CreateImage("ExpandTalismanIcon", _modalLayer, mx + 260f, my + 240f, 320f, 320f, iconSprite, false).preserveAspect = true;

            uint nextSlot = (snap?.UnlockedSchemeCount ?? 1u) + 1u;
            Label(_modalLayer, "UnlockDesc", EquipLoadoutModel.FormatUnlockDescription(nextSlot),
                mx + 640f, my + 250f, mw - 860f, 310f, 40f, true, Ink);

            uint unlocked = snap?.UnlockedSchemeCount ?? 1u;
            float stepW = 500f, stepGap = 32f;
            float stepStartX = mx + (mw - (5f * stepW + 4f * stepGap)) * 0.5f;
            for (uint s = 1u; s <= 5u; ++s)
            {
                string st = s == 1u ? "免费自带" : (s <= unlocked ? "已解锁" : (s == nextSlot ? "本次解锁" : "待解锁"));
                ChoiceRow(_modalLayer, "UnlockStep_" + s, $"{EquipLoadoutModel.DefaultSchemeName(s)}\n{st}",
                    stepStartX + (s - 1u) * (stepW + stepGap), my + 630f, stepW, 132f, null,
                    selected: s <= unlocked || s == nextSlot, enabled: false);
            }

            string costSummary = $"{EquipLoadoutModel.FormatUnlockTalismanLine(snap)}\n{EquipLoadoutModel.FormatUnlockCurrencyLine(snap, _loadoutUseBindDiamondFirst)}";
            Label(_modalLayer, "UnlockCostSummary", costSummary, mx + 260f, my + 810f, mw - 520f, 116f, 36f, true, Jade);

            if (snap != null && snap.NextUnlockAllowBindDiamond && snap.UnlockItemOwned == 0UL)
            {
                ChoiceRow(_modalLayer, "UnlockBindDiamondToggle",
                    (_loadoutUseBindDiamondFirst ? "[已勾选] " : "[未勾选] ") + "第 2 套优先使用绑定灵石（不足部分用非绑灵石补足）",
                    mx + 260f, my + 942f, mw - 520f, 80f,
                    () => { _loadoutUseBindDiamondFirst = !_loadoutUseBindDiamondFirst; Render(); },
                    selected: _loadoutUseBindDiamondFirst);
            }

            if (!string.IsNullOrEmpty(_client?.EquipLoadoutError))
                Label(_modalLayer, "UnlockErr", _client.EquipLoadoutError, mx + 260f, my + 1046f, mw - 520f, 64f, 34f, false, Danger);

            bool canUnlock = snap != null && EquipLoadoutModel.CanAffordNextUnlock(snap, _loadoutUseBindDiamondFirst)
                && !_client.BusyEquipLoadout && !_client.BusyEquipLoadoutUnlock && !_client.HasPendingEquipLoadoutUnlock;
            Button(_modalLayer, "UnlockCancel", "返回方案列表", mx + 520f, my + 1200f, 760f, () =>
            {
                _modal = Modal.EquipLoadout;
                Render();
            });
            Button(_modalLayer, "UnlockConfirm", _client?.BusyEquipLoadoutUnlock == true ? "解锁中…" : $"确认解锁{EquipLoadoutModel.DefaultSchemeName(nextSlot)}",
                mx + 1780f, my + 1200f, 820f, () =>
                {
                    if (canUnlock) _client.UnlockEquipLoadout(_loadoutUseBindDiamondFirst);
                }, primary: true, enabled: canUnlock);
        }

        private static string FormatLinkedOptionLabel(IReadOnlyList<EquipLoadoutLinkedOption> options, uint selectedId, string kindName)
        {
            if (selectedId == 0u) return $"不联动{kindName}方案（点击切换）";
            if (options != null)
            {
                for (int i = 0; i < options.Count; ++i)
                {
                    var o = options[i];
                    if (o != null && o.SchemeId == selectedId)
                        return string.IsNullOrWhiteSpace(o.Name) ? $"{kindName}方案 {o.SchemeId}" : $"{o.Name}（#{o.SchemeId}）";
                }
            }
            return $"{kindName}方案 {selectedId}";
        }

        private static uint CycleLinkedScheme(IReadOnlyList<EquipLoadoutLinkedOption> options, uint currentId)
        {
            if (options == null || options.Count == 0) return 0u;
            if (currentId == 0u) return options[0].SchemeId;
            for (int i = 0; i < options.Count; ++i)
            {
                if (options[i]?.SchemeId == currentId)
                    return i + 1 < options.Count ? options[i + 1].SchemeId : 0u;
            }
            return 0u;
        }
    }
}
