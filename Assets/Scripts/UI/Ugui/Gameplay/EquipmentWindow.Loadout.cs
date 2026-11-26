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
    /// 按 EquipLoadoutV1 MANIFEST 1932×814 原图等比映射无字透明切图，缺图自动回退通用面板。
    /// </summary>
    public sealed partial class EquipmentWindow
    {
        private const float LoadoutCanvasX0 = 340f;
        private const float LoadoutCanvasY0 = 130f;
        private const float LoadoutCanvasW = 3280f;
        private const float LoadoutCanvasH = 1382f;
        private const float LoadoutCanvasScale = LoadoutCanvasW / 1932f;

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
            => EquipLoadoutModel.BuildSwitchToast(response);

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

        private static Button CreatePaintedHotspot(UnityEngine.Transform parent, string name, float x, float y, float w, float h,
            string text, float fontSize, Color textColor, Action click, bool enabled = true,
            TextAlignmentOptions align = TextAlignmentOptions.Center, float padX = 14f)
        {
            var rect = QdaoUguiFactory.CreateRect(name, parent, x, y, w, h);
            var hit = rect.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;
            var btn = rect.gameObject.AddComponent<Button>();
            btn.targetGraphic = hit;
            btn.transition = Selectable.Transition.None;
            btn.interactable = enabled;
            if (click != null) btn.onClick.AddListener(() => click());
            if (!string.IsNullOrEmpty(text))
            {
                var lbl = Label(rect, "Lbl", text, padX, 0f, Mathf.Max(20f, w - padX * 2f), h, fontSize, false,
                    enabled ? textColor : Muted);
                lbl.alignment = align;
                lbl.enableAutoSizing = true;
                lbl.fontSizeMin = Mathf.Max(16f, fontSize - 10f);
                lbl.fontSizeMax = fontSize;
            }
            return btn;
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

            var bgSprite = LoadEquipLoadoutSprite(EquipLoadoutModel.PanelMainKey);
            if (bgSprite != null)
            {
                RenderEquipLoadoutModalPainted(snap, bgSprite);
                return;
            }
            RenderEquipLoadoutModalFallback(snap);
        }

        private void RenderEquipLoadoutModalPainted(EquipLoadoutSnapshot snap, Sprite bgSprite)
        {
            const float x0 = LoadoutCanvasX0, y0 = LoadoutCanvasY0, w = LoadoutCanvasW, h = LoadoutCanvasH, s = LoadoutCanvasScale;
            var bg = QdaoUguiFactory.CreateImage("EquipLoadoutMainBg", _modalLayer, x0, y0, w, h, bgSprite, true);
            bg.type = Image.Type.Simple;
            bg.preserveAspect = true;

            var titleLbl = Label(_modalLayer, "EquipLoadoutTitle", "装备流派方案",
                x0 + 711f * s, y0 + 26f * s, 510f * s, 66f * s, 56f, false, Cream);
            titleLbl.alignment = TextAlignmentOptions.Center;

            // 右上朱砂关闭钮热区对齐 MANIFEST (1735, 160)
            CreatePaintedHotspot(_modalLayer, "EquipLoadoutCloseX",
                x0 + 1692f * s, y0 + 118f * s, 88f * s, 88f * s, null, 24f, Cream, CloseModal);

            if (snap == null)
            {
                string waitText = _client?.EquipLoadoutsLoading == true ? "正在同步装备流派方案…" : "装备流派方案暂未同步，请点击刷新。";
                var waitLbl = Label(_modalLayer, "EquipLoadoutWait", waitText,
                    x0 + 420f * s, y0 + 340f * s, 1092f * s, 120f * s, 44f, true, Muted);
                waitLbl.alignment = TextAlignmentOptions.Center;
                CreatePaintedHotspot(_modalLayer, "EquipLoadoutRetry",
                    x0 + 386f * s, y0 + 680f * s, 386f * s, 61f * s, "刷新方案", 34f, Ink, () => _client?.RequestEquipLoadouts());
                CreatePaintedHotspot(_modalLayer, "EquipLoadoutCloseEmpty",
                    x0 + 1223f * s, y0 + 680f * s, 244f * s, 61f * s, "关闭", 34f, Ink, CloseModal);
                return;
            }

            // 顶部 1..5 方案页签：严格对齐 MANIFEST x=239+284*i, y=105, 260×64
            for (int i = 0; i < snap.Schemes.Count && i < 5; ++i)
            {
                var scheme = snap.Schemes[i];
                bool isSelected = scheme.Unlocked && scheme.SchemeId == _loadoutSelectedSchemeId;
                bool isActive = scheme.Unlocked && scheme.SchemeId == snap.ActiveSchemeId;
                string spriteKey = !scheme.Unlocked
                    ? EquipLoadoutModel.TabLockedKey
                    : (isSelected ? EquipLoadoutModel.TabActiveKey : EquipLoadoutModel.TabUnlockedKey);
                var tabSprite = LoadEquipLoadoutSprite(spriteKey);
                float tx = x0 + (239f + 284f * i) * s, ty = y0 + 105f * s, tw = 260f * s, th = 64f * s;
                uint capturedId = scheme.SchemeId;
                bool capturedUnlocked = scheme.Unlocked;
                var tabBtn = QdaoUguiFactory.CreateArtButton("LoadoutTab_" + scheme.SchemeId, _modalLayer, tx, ty, tw, th, tabSprite, out var tabImg);
                if (tabSprite != null) SeasonEconomyUiArt.ApplyHorizontalSlice(tabImg, tabSprite, th);
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
                string tabText = EquipLoadoutModel.FormatTabLabel(scheme, isActive) + (isActive ? " · 穿" : string.Empty);
                var tabLbl = Label(tabBtn.transform, "Lbl", tabText, 15f * s, 0f, tw - 30f * s, th, 28f, false, isSelected ? Cream : Ink);
                tabLbl.alignment = TextAlignmentOptions.Center;
                tabLbl.enableAutoSizing = true;
                tabLbl.fontSizeMin = 18f;
                tabLbl.fontSizeMax = 28f;
            }

            var currentScheme = FindLoadoutScheme(snap, _loadoutSelectedSchemeId)
                ?? snap.Schemes.FirstOrDefault(sItem => sItem.Unlocked);
            if (currentScheme == null) return;

            // 左半区：八槽装备对照（底图已绘 2×4 槽位框 x=259/601, y=246+94*r, 318×84）
            string slotHeading = currentScheme.MissingCount > 0
                ? $"八槽装备对照（缺失 {currentScheme.MissingCount} 槽）"
                : "八槽装备对照";
            Label(_modalLayer, "LoadoutSlotsHeading", slotHeading,
                x0 + 274f * s, y0 + 191f * s, 640f * s, 40f * s, 36f, false,
                currentScheme.MissingCount > 0 ? Danger : Jade);

            var missingSprite = LoadEquipLoadoutSprite(EquipLoadoutModel.BadgeSlotMissingKey);
            for (int i = 0; i < currentScheme.Slots.Count && i < 8; ++i)
            {
                var sv = currentScheme.Slots[i];
                int col = i % 2;
                int row = i / 2;
                float sx = x0 + (col == 0 ? 259f : 601f) * s;
                float sy = y0 + (246f + row * 94f) * s;
                float sw = 318f * s, sh = 84f * s;
                var slotRect = QdaoUguiFactory.CreateRect("LoadoutSlot_" + sv.Slot, _modalLayer, sx, sy, sw, sh);

                bool isMissing = sv.Status == EquipLoadoutSlotStatus.EquipLoadoutSlotMissingOrStored;
                if (sv.Item != null && !isMissing)
                {
                    Icon(slotRect, sv.Item, 16f * s, 10f * s, 64f * s);
                }
                else if (isMissing)
                {
                    if (missingSprite != null)
                        QdaoUguiFactory.CreateImage("MissingIcon", slotRect, 20f * s, 14f * s, 55f * s, 55f * s, missingSprite, false).preserveAspect = true;
                    else
                    {
                        var missTxt = Label(slotRect, "MissingTxt", "!", 20f * s, 10f * s, 56f * s, 64f * s, 44f, false, Danger);
                        missTxt.alignment = TextAlignmentOptions.Center;
                    }
                }
                else
                {
                    var emptyLbl = Label(slotRect, "EmptyPlus", "—", 20f * s, 10f * s, 56f * s, 64f * s, 38f, false, Muted);
                    emptyLbl.alignment = TextAlignmentOptions.Center;
                }

                string slotTitle = EquipLoadoutModel.SlotLabel(sv)
                    + (sv.Status == EquipLoadoutSlotStatus.EquipLoadoutSlotInBag ? " · 背包" : string.Empty);
                var titleRow = Label(slotRect, "SlotName", slotTitle, 89f * s, 9f * s, 214f * s, 32f * s, 28f, false, isMissing ? Danger : Jade);
                titleRow.enableAutoSizing = true;
                titleRow.fontSizeMin = 20f;
                titleRow.fontSizeMax = 28f;

                string subLine = isMissing
                    ? "缺失 / 不在背包"
                    : (sv.Item != null
                        ? Name(sv.Item) + (sv.Item.EnhanceMaxLevel > 0 ? $" +{sv.Item.EnhanceLevel}" : string.Empty)
                        : "未指定装备");
                var subRow = Label(slotRect, "ItemName", subLine, 89f * s, 41f * s, 218f * s, 34f * s, 26f, false,
                    isMissing ? Danger : (sv.Item != null ? Ink : Muted));
                subRow.enableAutoSizing = true;
                subRow.fontSizeMin = 18f;
                subRow.fontSizeMax = 26f;
            }

            // 右半区：方案设置（底图已绘输入框、4 枚流派槽、2 条联动选择框与副套强化继承按钮）
            Label(_modalLayer, "LoadoutConfigHeading", "方案设置",
                x0 + 1005f * s, y0 + 191f * s, 300f * s, 40f * s, 36f, false, Jade);

            Label(_modalLayer, "NameLbl", "方案名称", x0 + 998f * s, y0 + 252f * s, 130f * s, 49f * s, 30f, false, Ink);
            _loadoutNameInput = QdaoUguiFactory.CreateInputField("LoadoutNameInput", _modalLayer,
                x0 + 1138f * s, y0 + 250f * s, 506f * s, 52f * s,
                $"留空默认「{EquipLoadoutModel.DefaultSchemeName(currentScheme.SchemeId)}」", EquipLoadoutModel.MaxNameCodePoints);
            var inputBg = _loadoutNameInput.GetComponent<Image>();
            if (inputBg != null) inputBg.color = new Color(1f, 1f, 1f, 0f);
            if (_loadoutNameInput.textComponent != null)
            {
                _loadoutNameInput.textComponent.richText = false;
                _loadoutNameInput.textComponent.fontSize = 30f;
                _loadoutNameInput.textComponent.color = Ink;
            }
            if (_loadoutNameInput.placeholder is TMP_Text ph)
            {
                ph.richText = false;
                ph.fontSize = 26f;
                ph.color = Muted;
            }
            _loadoutNameInput.text = _loadoutNameDraft;
            _loadoutNameInput.onValueChanged.AddListener(v => _loadoutNameDraft = v ?? string.Empty);

            Label(_modalLayer, "RoleLbl", "流派标签", x0 + 998f * s, y0 + 329f * s, 130f * s, 49f * s, 30f, false, Ink);
            var activeTagSprite = LoadEquipLoadoutSprite(EquipLoadoutModel.TabActiveKey);
            for (int idx = 0; idx < 4; ++idx)
            {
                uint tag = (uint)(idx + 1); // 1输出 / 2控场 / 3辅助 / 4生存；再点已选标签恢复 0默认
                bool selected = _loadoutRoleTagDraft == tag;
                float rx = x0 + (1135f + 132f * idx) * s, ry = y0 + 328f * s, rw = 119f * s, rh = 53f * s;
                if (selected)
                {
                    var selBtn = QdaoUguiFactory.CreateArtButton("RoleTag_" + tag, _modalLayer, rx, ry, rw, rh, activeTagSprite, out var selImg);
                    if (activeTagSprite != null) SeasonEconomyUiArt.ApplyHorizontalSlice(selImg, activeTagSprite, rh);
                    else selImg.color = Jade;
                    selBtn.onClick.AddListener(() => { _loadoutRoleTagDraft = 0u; Render(); });
                    var lbl = Label(selBtn.transform, "Lbl", EquipLoadoutModel.RoleTagName(tag), 6f, 0f, rw - 12f, rh, 28f, false, Cream);
                    lbl.alignment = TextAlignmentOptions.Center;
                }
                else
                {
                    CreatePaintedHotspot(_modalLayer, "RoleTag_" + tag, rx, ry, rw, rh,
                        EquipLoadoutModel.RoleTagName(tag), 28f, Ink, () => { _loadoutRoleTagDraft = tag; Render(); });
                }
            }

            string linkedSkillLabel = "联动战法方案：" + FormatLinkedOptionLabel(snap.SkillSchemes, _loadoutLinkedSkillDraft, "战法");
            CreatePaintedHotspot(_modalLayer, "LinkSkillCycle",
                x0 + 998f * s, y0 + 410f * s, 651f * s, 54f * s,
                linkedSkillLabel, 28f, Ink, () =>
                {
                    _loadoutLinkedSkillDraft = CycleLinkedScheme(snap.SkillSchemes, _loadoutLinkedSkillDraft);
                    Render();
                }, true, TextAlignmentOptions.MidlineLeft, 22f * s);

            string linkedAttrLabel = "联动加点方案：" + FormatLinkedOptionLabel(snap.AttributeSchemes, _loadoutLinkedAttrDraft, "加点");
            CreatePaintedHotspot(_modalLayer, "LinkAttrCycle",
                x0 + 998f * s, y0 + 476f * s, 651f * s, 54f * s,
                linkedAttrLabel, 28f, Ink, () =>
                {
                    _loadoutLinkedAttrDraft = CycleLinkedScheme(snap.AttributeSchemes, _loadoutLinkedAttrDraft);
                    Render();
                }, true, TextAlignmentOptions.MidlineLeft, 22f * s);

            // 切装同步联动开关放在「副套强化继承」左侧留白区 (998, 559, 252, 62)
            CreatePaintedHotspot(_modalLayer, "ApplyLinkedToggle",
                x0 + 998f * s, y0 + 559f * s, 252f * s, 62f * s,
                _loadoutApplyLinked ? "✓ 同步切战法/加点" : "□ 仅切换装备",
                26f, _loadoutApplyLinked ? Jade : Muted,
                () => { _loadoutApplyLinked = !_loadoutApplyLinked; Render(); }, true, TextAlignmentOptions.MidlineLeft, 8f);

            var targetForInherit = FindItem(_selectedItem) ?? _client?.Equipment?.Items.FirstOrDefault(x => x.EquipProfile > 0);
            CreatePaintedHotspot(_modalLayer, "LoadoutInheritShortcut",
                x0 + 1263f * s, y0 + 559f * s, 315f * s, 62f * s,
                "副套强化继承", 30f, Ink, () =>
                {
                    if (targetForInherit != null) OpenInheritEnhance(targetForInherit.ItemId);
                    else { _notice = "请先拥有一件可继承强化的副套装备"; Render(); }
                }, targetForInherit != null && !WriteBlocked);

            string errOrNote = !string.IsNullOrEmpty(_client.EquipLoadoutError) ? _client.EquipLoadoutError : _notice;
            if (!string.IsNullOrEmpty(errOrNote))
            {
                var inlineLbl = Label(_modalLayer, "LoadoutInlineNotice", errOrNote,
                    x0 + 260f * s, y0 + 626f * s, 1410f * s, 36f * s, 26f, false,
                    !string.IsNullOrEmpty(_client.EquipLoadoutError) ? Danger : Jade);
                inlineLbl.alignment = TextAlignmentOptions.Center;
                inlineLbl.enableAutoSizing = true;
                inlineLbl.fontSizeMin = 18f;
                inlineLbl.fontSizeMax = 26f;
            }

            // 底部三枚已绘制按钮热区：保存 (386,680,386,61)、切换 (805,679,382,64)、关闭/恢复 (1223,680,244,61)
            bool busy = _client.BusyEquipLoadout || _client.BusyEquipLoadoutUnlock || _client.BusyEquip || WriteBlocked;
            CreatePaintedHotspot(_modalLayer, "LoadoutSaveBtn",
                x0 + 386f * s, y0 + 680f * s, 386f * s, 61f * s,
                _client.BusyEquipLoadout ? "保存中…" : "保存当前穿着到此方案", 30f, Ink, () =>
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
                }, !busy);

            CreatePaintedHotspot(_modalLayer, "LoadoutSwitchBtn",
                x0 + 805f * s, y0 + 679f * s, 382f * s, 64f * s,
                _client.BusyEquipLoadout ? "切换中…" : "一键切换本方案", 32f, Cream,
                () => _client.SwitchEquipLoadout(currentScheme.SchemeId, _loadoutApplyLinked), !busy);

            if (_client.HasPendingEquipLoadoutUnlock)
            {
                CreatePaintedHotspot(_modalLayer, "LoadoutRecoverUnlock",
                    x0 + 1223f * s, y0 + 680f * s, 244f * s, 61f * s,
                    "确认上次解锁", 28f, Jade, () => _client.RecoverEquipLoadoutUnlock(),
                    !_client.BusyEquipLoadout && !_client.BusyEquipLoadoutUnlock);
            }
            else
            {
                CreatePaintedHotspot(_modalLayer, "LoadoutClose",
                    x0 + 1223f * s, y0 + 680f * s, 244f * s, 61f * s,
                    "关闭", 32f, Ink, CloseModal);
            }
        }

        private void RenderEquipLoadoutModalFallback(EquipLoadoutSnapshot snap)
        {
            const float mx = 420f, my = 80f, mw = 3126f, mh = 1420f;
            Panel(_modalLayer, "EquipLoadoutMainPaper", mx, my, mw, mh, true);
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

            float tabW = 532f, tabH = 92f, tabGap = 24f;
            float tabStartX = mx + (mw - (5f * tabW + 4f * tabGap)) * 0.5f;
            for (int i = 0; i < snap.Schemes.Count && i < 5; ++i)
            {
                var scheme = snap.Schemes[i];
                bool isSelected = scheme.Unlocked && scheme.SchemeId == _loadoutSelectedSchemeId;
                bool isActive = scheme.Unlocked && scheme.SchemeId == snap.ActiveSchemeId;
                uint capturedId = scheme.SchemeId;
                bool capturedUnlocked = scheme.Unlocked;
                ChoiceRow(_modalLayer, "LoadoutTab_" + scheme.SchemeId,
                    EquipLoadoutModel.FormatTabLabel(scheme, isActive) + (isActive ? " (当前)" : string.Empty),
                    tabStartX + i * (tabW + tabGap), my + 150f, tabW, tabH, () =>
                    {
                        if (capturedUnlocked) { _loadoutSelectedSchemeId = capturedId; SyncLoadoutDraftFromSnapshot(snap, capturedId); Render(); }
                        else { _modal = Modal.EquipLoadoutUnlock; Render(); }
                    }, selected: isSelected);
            }

            var currentScheme = FindLoadoutScheme(snap, _loadoutSelectedSchemeId)
                ?? snap.Schemes.FirstOrDefault(sItem => sItem.Unlocked);
            if (currentScheme == null) return;

            float leftX = mx + 120f, leftY = my + 276f;
            Label(_modalLayer, "LoadoutSlotsHeading", $"八槽装备对照（缺失 {currentScheme.MissingCount} 槽）", leftX, leftY, 1380f, 54f, 38f, false, Jade);
            float cellW = 668f, cellH = 176f, cellGapX = 28f, cellGapY = 20f;
            for (int i = 0; i < currentScheme.Slots.Count && i < 8; ++i)
            {
                var sv = currentScheme.Slots[i];
                var card = ChoiceRow(_modalLayer, "LoadoutSlot_" + sv.Slot, "", leftX + (i % 2) * (cellW + cellGapX),
                    leftY + 64f + (i / 2) * (cellH + cellGapY), cellW, cellH, null,
                    selected: sv.Status == EquipLoadoutSlotStatus.EquipLoadoutSlotEquipped, enabled: false);
                Label(card.transform, "SlotName", $"{EquipLoadoutModel.SlotLabel(sv)} · {EquipLoadoutModel.SlotStatusText(sv.Status)}",
                    24f, 16f, cellW - 48f, 48f, 30f, false, sv.Status == EquipLoadoutSlotStatus.EquipLoadoutSlotMissingOrStored ? Danger : Jade);
                Label(card.transform, "ItemName", sv.Item != null ? Name(sv.Item) : "未指定装备", 24f, 72f, cellW - 48f, 84f, 30f, true, Ink);
            }

            float rightX = mx + 1580f, rightW = mw - 1700f;
            _loadoutNameInput = QdaoUguiFactory.CreateInputField("LoadoutNameInput", _modalLayer, rightX, leftY + 130f, rightW, 84f,
                $"留空默认「{EquipLoadoutModel.DefaultSchemeName(currentScheme.SchemeId)}」", EquipLoadoutModel.MaxNameCodePoints);
            if (_loadoutNameInput.textComponent != null) _loadoutNameInput.textComponent.richText = false;
            _loadoutNameInput.text = _loadoutNameDraft;
            _loadoutNameInput.onValueChanged.AddListener(v => _loadoutNameDraft = v ?? string.Empty);

            bool busy = _client.BusyEquipLoadout || _client.BusyEquipLoadoutUnlock || _client.BusyEquip || WriteBlocked;
            Button(_modalLayer, "LoadoutSaveBtn", _client.BusyEquipLoadout ? "保存中…" : "保存当前穿着到此方案",
                mx + 180f, my + 1218f, 840f, () =>
                {
                    string rawName = (_loadoutNameInput != null ? _loadoutNameInput.text : _loadoutNameDraft)?.Trim() ?? string.Empty;
                    if (!EquipLoadoutModel.ValidateNameDraft(rawName, out string nameErr)) { _notice = nameErr; Render(); return; }
                    _client.SaveEquipLoadout(currentScheme.SchemeId, rawName, _loadoutRoleTagDraft,
                        captureCurrentEquipped: true, explicitSlots: null, _loadoutLinkedSkillDraft, _loadoutLinkedAttrDraft);
                }, false, !busy);
            Button(_modalLayer, "LoadoutSwitchBtn", _client.BusyEquipLoadout ? "切换中…" : "一键切换本方案",
                mx + 1140f, my + 1218f, 840f, () => _client.SwitchEquipLoadout(currentScheme.SchemeId, _loadoutApplyLinked), true, !busy);
            Button(_modalLayer, "LoadoutClose", "关闭", mx + 2180f, my + 1218f, 680f, CloseModal);
        }

        private void RenderEquipLoadoutUnlockModal()
        {
            var snap = _client?.EquipLoadouts;
            var bgSprite = LoadEquipLoadoutSprite(EquipLoadoutModel.PanelUnlockKey);
            if (bgSprite != null)
            {
                RenderEquipLoadoutUnlockModalPainted(snap, bgSprite);
                return;
            }
            RenderEquipLoadoutUnlockModalFallback(snap);
        }

        private void RenderEquipLoadoutUnlockModalPainted(EquipLoadoutSnapshot snap, Sprite bgSprite)
        {
            const float x0 = LoadoutCanvasX0, y0 = LoadoutCanvasY0, w = LoadoutCanvasW, h = LoadoutCanvasH, s = LoadoutCanvasScale;
            var bg = QdaoUguiFactory.CreateImage("EquipLoadoutUnlockBg", _modalLayer, x0, y0, w, h, bgSprite, true);
            bg.type = Image.Type.Simple;
            bg.preserveAspect = true;

            var titleLbl = Label(_modalLayer, "UnlockTitle", "解锁装备方案位",
                x0 + 699f * s, y0 + 26f * s, 534f * s, 66f * s, 56f, false, Cream);
            titleLbl.alignment = TextAlignmentOptions.Center;

            Action backToMain = () => { _modal = Modal.EquipLoadout; Render(); };
            CreatePaintedHotspot(_modalLayer, "UnlockCloseX",
                x0 + 1692f * s, y0 + 118f * s, 88f * s, 88f * s, null, 24f, Cream, backToMain);

            // 道具图标位对齐 MANIFEST (297, 213, 135, 123)
            var iconSprite = LoadEquipLoadoutSprite(EquipLoadoutModel.ExpandTalismanIconKey);
            if (iconSprite != null)
                QdaoUguiFactory.CreateImage("ExpandTalismanIcon", _modalLayer,
                    x0 + 297f * s, y0 + 213f * s, 135f * s, 123f * s, iconSprite, false).preserveAspect = true;

            uint unlocked = snap?.UnlockedSchemeCount ?? 1u;
            uint nextSlot = Math.Min(EquipLoadoutModel.MaxSchemes, unlocked + 1u);
            Label(_modalLayer, "UnlockItemTitle", "装备方案扩容符",
                x0 + 482f * s, y0 + 203f * s, 460f * s, 45f * s, 40f, false, Jade);
            Label(_modalLayer, "UnlockDesc", EquipLoadoutModel.FormatUnlockDescription(nextSlot),
                x0 + 485f * s, y0 + 249f * s, 1124f * s, 96f * s, 30f, true, Ink);

            Label(_modalLayer, "UnlockProgressHeading", "装备方案位解锁进度",
                x0 + 306f * s, y0 + 352f * s, 516f * s, 43f * s, 34f, false, Jade);

            // 五阶进度卡对齐 MANIFEST x=301+267*i, y=398, 246×101
            for (int i = 0; i < 5; ++i)
            {
                uint tier = (uint)(i + 1);
                float cx = x0 + (301f + 267f * i) * s, cy = y0 + 398f * s, cw = 246f * s;
                bool isUnlocked = tier <= unlocked;
                bool isNext = tier == unlocked + 1u;
                var head = Label(_modalLayer, "UnlockStepHead_" + tier, $"第 {tier} 套",
                    cx + 16f * s, cy + 6f * s, cw - 32f * s, 38f * s, 32f, false, isUnlocked || isNext ? Jade : Muted);
                head.alignment = TextAlignmentOptions.Center;
                string sub = tier == 1u
                    ? "免费已解锁"
                    : (isUnlocked
                        ? "已解锁"
                        : (isNext
                            ? $"当前待解锁 · {EquipLoadoutModel.TierCostDiamond(tier)}灵石"
                            : $"未解锁 · {EquipLoadoutModel.TierCostDiamond(tier)}灵石"));
                var subLbl = Label(_modalLayer, "UnlockStepSub_" + tier, sub,
                    cx + 10f * s, cy + 48f * s, cw - 20f * s, 42f * s, 24f, false,
                    isNext ? Danger : (isUnlocked ? Jade : Muted));
                subLbl.alignment = TextAlignmentOptions.Center;
                subLbl.enableAutoSizing = true;
                subLbl.fontSizeMin = 17f;
                subLbl.fontSizeMax = 24f;
            }

            Label(_modalLayer, "UnlockCostHeading", "解锁消耗",
                x0 + 306f * s, y0 + 506f * s, 350f * s, 38f * s, 32f, false, Jade);

            // 消耗行 1 (y=541) 与消耗行 2 (y=598)
            var row1 = Label(_modalLayer, "UnlockCostTalisman", EquipLoadoutModel.FormatUnlockTalismanLine(snap),
                x0 + 345f * s, y0 + 544f * s, 1230f * s, 42f * s, 28f, false, Ink);
            row1.enableAutoSizing = true;
            row1.fontSizeMin = 20f;
            row1.fontSizeMax = 28f;

            bool showBindToggle = snap != null && snap.NextUnlockAllowBindDiamond && snap.UnlockItemOwned == 0UL;
            var row2 = Label(_modalLayer, "UnlockCostCurrency",
                EquipLoadoutModel.FormatUnlockCurrencyLine(snap, _loadoutUseBindDiamondFirst),
                x0 + 345f * s, y0 + 601f * s, (showBindToggle ? 850f : 1230f) * s, 42f * s, 28f, false, Ink);
            row2.enableAutoSizing = true;
            row2.fontSizeMin = 19f;
            row2.fontSizeMax = 28f;

            if (showBindToggle)
            {
                CreatePaintedHotspot(_modalLayer, "UnlockBindDiamondToggle",
                    x0 + 1210f * s, y0 + 599f * s, 376f * s, 46f * s,
                    _loadoutUseBindDiamondFirst ? "✓ 优先使用绑定灵石" : "□ 优先使用绑定灵石",
                    26f, _loadoutUseBindDiamondFirst ? Jade : Muted,
                    () => { _loadoutUseBindDiamondFirst = !_loadoutUseBindDiamondFirst; Render(); },
                    true, TextAlignmentOptions.MidlineRight, 8f);
            }

            if (!string.IsNullOrEmpty(_client?.EquipLoadoutError))
            {
                var errLbl = Label(_modalLayer, "UnlockErr", _client.EquipLoadoutError,
                    x0 + 321f * s, y0 + 647f * s, 1277f * s, 26f * s, 24f, false, Danger);
                errLbl.alignment = TextAlignmentOptions.Center;
            }

            // 取消 (665,680,272,61) 与 确认解锁 (974,679,336,64)
            CreatePaintedHotspot(_modalLayer, "UnlockCancel",
                x0 + 665f * s, y0 + 680f * s, 272f * s, 61f * s, "取消", 32f, Ink, backToMain);

            if (_client?.HasPendingEquipLoadoutUnlock == true)
            {
                CreatePaintedHotspot(_modalLayer, "UnlockConfirm",
                    x0 + 974f * s, y0 + 679f * s, 336f * s, 64f * s,
                    "确认上次解锁", 32f, Cream, () => _client.RecoverEquipLoadoutUnlock(),
                    !_client.BusyEquipLoadout && !_client.BusyEquipLoadoutUnlock);
            }
            else
            {
                bool canUnlock = snap != null && EquipLoadoutModel.CanAffordNextUnlock(snap, _loadoutUseBindDiamondFirst)
                    && !_client.BusyEquipLoadout && !_client.BusyEquipLoadoutUnlock;
                CreatePaintedHotspot(_modalLayer, "UnlockConfirm",
                    x0 + 974f * s, y0 + 679f * s, 336f * s, 64f * s,
                    _client?.BusyEquipLoadoutUnlock == true ? "解锁中…" : "确认解锁", 32f, Cream,
                    () => { if (canUnlock) _client.UnlockEquipLoadout(_loadoutUseBindDiamondFirst); }, canUnlock);
            }
        }

        private void RenderEquipLoadoutUnlockModalFallback(EquipLoadoutSnapshot snap)
        {
            const float mx = 420f, my = 80f, mw = 3126f, mh = 1420f;
            Panel(_modalLayer, "EquipLoadoutUnlockPaper", mx, my, mw, mh, true);
            Label(_modalLayer, "UnlockTitle", "解锁新装备流派方案位", mx + 920f, my + 56f, 1286f, 86f, 60f, false, Jade)
                .alignment = TextAlignmentOptions.Center;

            uint unlocked = snap?.UnlockedSchemeCount ?? 1u;
            uint nextSlot = Math.Min(EquipLoadoutModel.MaxSchemes, unlocked + 1u);
            Label(_modalLayer, "UnlockDesc", EquipLoadoutModel.FormatUnlockDescription(nextSlot),
                mx + 260f, my + 240f, mw - 520f, 220f, 40f, true, Ink);

            string costSummary = $"{EquipLoadoutModel.FormatUnlockTalismanLine(snap)}\n{EquipLoadoutModel.FormatUnlockCurrencyLine(snap, _loadoutUseBindDiamondFirst)}";
            Label(_modalLayer, "UnlockCostSummary", costSummary, mx + 260f, my + 520f, mw - 520f, 140f, 36f, true, Jade);

            if (snap != null && snap.NextUnlockAllowBindDiamond && snap.UnlockItemOwned == 0UL)
            {
                ChoiceRow(_modalLayer, "UnlockBindDiamondToggle",
                    (_loadoutUseBindDiamondFirst ? "[已勾选] " : "[未勾选] ") + "第 2 套优先使用绑定灵石（不足部分用非绑灵石补足）",
                    mx + 260f, my + 700f, mw - 520f, 80f,
                    () => { _loadoutUseBindDiamondFirst = !_loadoutUseBindDiamondFirst; Render(); },
                    selected: _loadoutUseBindDiamondFirst);
            }

            if (!string.IsNullOrEmpty(_client?.EquipLoadoutError))
                Label(_modalLayer, "UnlockErr", _client.EquipLoadoutError, mx + 260f, my + 820f, mw - 520f, 64f, 34f, false, Danger);

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
            if (selectedId == 0u) return $"不联动{kindName}方案";
            if (options != null)
            {
                for (int i = 0; i < options.Count; ++i)
                {
                    var o = options[i];
                    if (o != null && o.SchemeId == selectedId)
                        return string.IsNullOrWhiteSpace(o.Name) ? $"{kindName}方案 {o.SchemeId}" : o.Name;
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
