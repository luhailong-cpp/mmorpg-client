using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.UI.Ugui.Jubaozhai;
using MmorpgClient.UI.Ugui.Skills;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.EquipmentUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 装备流派方案预设 uGUI（方案 A · 7 套三合一共享流派席位：主管理弹窗 + 统一按套租用管理弹窗 + 背包防误操作角标）。
    /// 主管理弹窗按 EquipLoadoutV1 MANIFEST 1932×814 原图等比映射 1..7 方案页签，缺图自动回退通用面板；
    /// 席位租用管理复用 SkillLoadoutRentPlanner 试算与 ISkillLoadoutSource.RentLoadoutSlots，与战法/加点一次开通三合一共享。
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
        private bool _loadoutDraftSynced;
        private TMP_InputField _loadoutNameInput;
        private uint _loadoutRentTargetSlots = 1u;
        private uint _loadoutRentAddPeriods = 1u;
        private bool _loadoutRentBusy;
        private string _loadoutRentError = string.Empty;

        public void OpenEquipLoadouts()
        {
            var snap = _client?.EquipLoadouts;
            uint focusId = snap != null && snap.ActiveSchemeId >= 1u ? snap.ActiveSchemeId : 1u;
            OpenEquipLoadoutsForScheme(focusId);
        }

        private void OpenEquipLoadoutsForScheme(uint schemeId)
        {
            if (_disposed) return;
            _modal = Modal.EquipLoadout;
            _loadoutDraftSynced = false;
            _notice = string.Empty;
            var snap = _client?.EquipLoadouts;
            if (snap != null)
            {
                uint maxSchemes = EquipLoadoutModel.EffectiveMaxSchemes(snap);
                _loadoutSelectedSchemeId = schemeId >= 1u && schemeId <= maxSchemes ? schemeId : 1u;
                SyncLoadoutDraftFromSnapshot(snap, _loadoutSelectedSchemeId);
            }
            else
            {
                _loadoutSelectedSchemeId = Math.Max(1u, schemeId);
                _client?.RequestEquipLoadouts();
            }
            Render();
        }

        private void OpenEquipLoadoutRentModal(EquipLoadoutSnapshot snap)
        {
            if (_disposed) return;
            var view = EquipLoadoutModel.ToUnifiedRentView(snap);
            uint maxRentable = view.MaxRentableExtraSlots;
            uint activeRented = view.IsRentActive ? Math.Min(view.RentedExtraSlots, maxRentable) : 0u;
            if (view.IsRentActive)
            {
                if (activeRented < maxRentable)
                {
                    _loadoutRentTargetSlots = activeRented + 1u;
                    _loadoutRentAddPeriods = 0u;
                }
                else
                {
                    _loadoutRentTargetSlots = Math.Max(1u, activeRented);
                    _loadoutRentAddPeriods = 1u;
                }
            }
            else
            {
                _loadoutRentTargetSlots = 1u;
                _loadoutRentAddPeriods = 1u;
            }
            _loadoutRentError = string.Empty;
            _modal = Modal.EquipLoadoutUnlock;
            Render();
        }

        private static ISkillLoadoutSource ResolveSkillLoadouts()
            => AppBootstrap.Instance?.GameClient?.Skills;

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
                ? $"已同步共享流派席位（当前 {unlocked} 套）。"
                : "共享流派席位已同步。";
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

        private static Sprite LoadLoadoutTabSprite(bool unlocked, bool activeOrSelected, bool isCardOrRented)
        {
            if (!unlocked) return LoadEquipLoadoutSprite(EquipLoadoutModel.TabLockedKey);
            if (activeOrSelected) return LoadEquipLoadoutSprite(EquipLoadoutModel.TabActiveKey);
            if (isCardOrRented)
                return LoadEquipLoadoutSprite(EquipLoadoutModel.TabRentedKey)
                    ?? LoadEquipLoadoutSprite(EquipLoadoutModel.TabUnlockedKey);
            return LoadEquipLoadoutSprite(EquipLoadoutModel.TabUnlockedKey);
        }

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
                lbl.fontSizeMin = Mathf.Max(15f, fontSize - 10f);
                lbl.fontSizeMax = fontSize;
            }
            return btn;
        }

        private void RenderEquipLoadoutModal()
        {
            var snap = _client?.EquipLoadouts;
            if (snap != null && !_loadoutDraftSynced)
            {
                uint maxSchemes = EquipLoadoutModel.EffectiveMaxSchemes(snap);
                if (_loadoutSelectedSchemeId == 0u || _loadoutSelectedSchemeId > maxSchemes)
                    _loadoutSelectedSchemeId = snap.ActiveSchemeId >= 1u && snap.ActiveSchemeId <= maxSchemes ? snap.ActiveSchemeId : 1u;
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

            uint maxSchemes = EquipLoadoutModel.EffectiveMaxSchemes(snap);
            uint freeSchemes = EquipLoadoutModel.EffectiveFreeSchemes(snap);
            uint unlockedCount = EquipLoadoutModel.EffectiveUnlockedSchemes(snap);

            // 顶部 1..7 方案页签：在原图干净页签槽 (x=239..1675, y=105, h=64) 内等距排布 7 枚页签
            int tabCount = Math.Min(snap.Schemes.Count, (int)maxSchemes);
            for (int i = 0; i < tabCount; ++i)
            {
                var scheme = snap.Schemes[i];
                bool isSelected = scheme.SchemeId == _loadoutSelectedSchemeId;
                bool isActive = scheme.SchemeId == snap.ActiveSchemeId;
                bool isCardOrRented = scheme.Unlocked && scheme.SchemeId > freeSchemes;
                bool hasSaved = EquipLoadoutModel.HasSavedBindings(scheme);
                var tabSprite = LoadLoadoutTabSprite(scheme.Unlocked, isSelected, isCardOrRented);
                float tx = x0 + (239f + 207f * i) * s, ty = y0 + 105f * s, tw = 194f * s, th = 64f * s;
                uint capturedId = scheme.SchemeId;
                bool capturedCanPreview = scheme.Unlocked || hasSaved || isActive;
                var tabBtn = QdaoUguiFactory.CreateArtButton("LoadoutTab_" + scheme.SchemeId, _modalLayer, tx, ty, tw, th, tabSprite, out var tabImg);
                if (tabSprite != null) SeasonEconomyUiArt.ApplyHorizontalSlice(tabImg, tabSprite, th);
                else tabImg.color = !scheme.Unlocked ? new Color(0.84f, 0.80f, 0.72f, 0.92f) : (isSelected ? Jade : Cream);
                tabBtn.onClick.AddListener(() =>
                {
                    if (capturedCanPreview)
                    {
                        _loadoutSelectedSchemeId = capturedId;
                        SyncLoadoutDraftFromSnapshot(snap, capturedId);
                        Render();
                    }
                    else
                    {
                        OpenEquipLoadoutRentModal(snap);
                    }
                });
                string tabText = EquipLoadoutModel.FormatCompactTabLabel(snap, scheme, isActive);
                bool lightInk = (isSelected && scheme.Unlocked) || (isCardOrRented && tabSprite != null);
                var tabLbl = Label(tabBtn.transform, "Lbl", tabText, 10f * s, 0f, tw - 20f * s, th, 24f, false, lightInk ? Cream : Ink);
                tabLbl.alignment = TextAlignmentOptions.Center;
                tabLbl.enableAutoSizing = true;
                tabLbl.fontSizeMin = 15f;
                tabLbl.fontSizeMax = 24f;
            }

            var currentScheme = FindLoadoutScheme(snap, _loadoutSelectedSchemeId)
                ?? snap.Schemes.FirstOrDefault(sItem => sItem.Unlocked);
            if (currentScheme == null) return;
            bool editable = currentScheme.Unlocked;

            // 左半区：八槽装备对照（底图已绘 2×4 槽位框 x=259/601, y=246+94*r, 318×84）
            string slotHeading = !editable
                ? $"八槽装备对照（只读预览 · 到期只锁不删）"
                : (currentScheme.MissingCount > 0
                    ? $"八槽装备对照（缺失 {currentScheme.MissingCount} 槽）"
                    : "八槽装备对照");
            var slotsHeadLbl = Label(_modalLayer, "LoadoutSlotsHeading", slotHeading,
                x0 + 274f * s, y0 + 191f * s, 640f * s, 40f * s, 34f, false,
                (!editable || currentScheme.MissingCount > 0) ? Danger : Jade);
            slotsHeadLbl.enableAutoSizing = true;
            slotsHeadLbl.fontSizeMin = 22f;
            slotsHeadLbl.fontSizeMax = 34f;

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

            // 右半区标题 + 三合一席位状态 + 管理席位入口
            Label(_modalLayer, "LoadoutConfigHeading", "方案设置",
                x0 + 1005f * s, y0 + 191f * s, 178f * s, 40f * s, 34f, false, Jade);
            var seatSummaryLbl = Label(_modalLayer, "LoadoutSeatSummary",
                $"席位 {unlockedCount}/{maxSchemes} · 三合一共享",
                x0 + 1185f * s, y0 + 193f * s, 286f * s, 38f * s, 24f, false, Muted);
            seatSummaryLbl.alignment = TextAlignmentOptions.MidlineRight;
            seatSummaryLbl.enableAutoSizing = true;
            seatSummaryLbl.fontSizeMin = 17f;
            seatSummaryLbl.fontSizeMax = 24f;

            var manageSprite = LoadEquipLoadoutSprite(EquipLoadoutModel.TabActiveKey);
            var manageBtn = QdaoUguiFactory.CreateArtButton("LoadoutManageSeatsBtn", _modalLayer,
                x0 + 1480f * s, y0 + 186f * s, 168f * s, 48f * s, manageSprite, out var manageImg);
            if (manageSprite != null) SeasonEconomyUiArt.ApplyHorizontalSlice(manageImg, manageSprite, 48f * s);
            else manageImg.color = Jade;
            manageBtn.onClick.AddListener(() => OpenEquipLoadoutRentModal(snap));
            var manageLbl = Label(manageBtn.transform, "Lbl", "管理席位", 8f, 0f, 168f * s - 16f, 48f * s, 24f, false, Cream);
            manageLbl.alignment = TextAlignmentOptions.Center;
            manageLbl.enableAutoSizing = true;
            manageLbl.fontSizeMin = 17f;
            manageLbl.fontSizeMax = 24f;

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
                _loadoutNameInput.textComponent.color = editable ? Ink : Muted;
            }
            if (_loadoutNameInput.placeholder is TMP_Text ph)
            {
                ph.richText = false;
                ph.fontSize = 26f;
                ph.color = Muted;
            }
            _loadoutNameInput.interactable = editable;
            _loadoutNameInput.text = _loadoutNameDraft;
            _loadoutNameInput.onValueChanged.AddListener(v => { if (editable) _loadoutNameDraft = v ?? string.Empty; });

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
                    selBtn.interactable = editable;
                    selBtn.onClick.AddListener(() => { if (!editable) return; _loadoutRoleTagDraft = 0u; Render(); });
                    var lbl = Label(selBtn.transform, "Lbl", EquipLoadoutModel.RoleTagName(tag), 6f, 0f, rw - 12f, rh, 28f, false, Cream);
                    lbl.alignment = TextAlignmentOptions.Center;
                }
                else
                {
                    CreatePaintedHotspot(_modalLayer, "RoleTag_" + tag, rx, ry, rw, rh,
                        EquipLoadoutModel.RoleTagName(tag), 28f, Ink,
                        () => { if (!editable) return; _loadoutRoleTagDraft = tag; Render(); }, enabled: editable);
                }
            }

            string linkedSkillLabel = "联动战法方案：" + FormatLinkedOptionLabel(snap.SkillSchemes, _loadoutLinkedSkillDraft, "战法");
            CreatePaintedHotspot(_modalLayer, "LinkSkillCycle",
                x0 + 998f * s, y0 + 410f * s, 651f * s, 54f * s,
                linkedSkillLabel, 28f, Ink, () =>
                {
                    if (!editable) return;
                    _loadoutLinkedSkillDraft = CycleLinkedScheme(snap.SkillSchemes, _loadoutLinkedSkillDraft);
                    Render();
                }, editable, TextAlignmentOptions.MidlineLeft, 22f * s);

            string linkedAttrLabel = "联动加点方案：" + FormatLinkedOptionLabel(snap.AttributeSchemes, _loadoutLinkedAttrDraft, "加点");
            CreatePaintedHotspot(_modalLayer, "LinkAttrCycle",
                x0 + 998f * s, y0 + 476f * s, 651f * s, 54f * s,
                linkedAttrLabel, 28f, Ink, () =>
                {
                    if (!editable) return;
                    _loadoutLinkedAttrDraft = CycleLinkedScheme(snap.AttributeSchemes, _loadoutLinkedAttrDraft);
                    Render();
                }, editable, TextAlignmentOptions.MidlineLeft, 22f * s);

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

            string lockedBanner = !editable
                ? "当前方案已锁定（到期只锁不删，配装与防误卖保护永久保留）；达85级免费+1套、开轻享卡+2套或按套续租即恢复。"
                : string.Empty;
            string errOrNote = !string.IsNullOrEmpty(_client.EquipLoadoutError)
                ? _client.EquipLoadoutError
                : (!string.IsNullOrEmpty(lockedBanner) ? lockedBanner : _notice);
            if (!string.IsNullOrEmpty(errOrNote))
            {
                bool isDanger = !string.IsNullOrEmpty(_client.EquipLoadoutError) || !editable;
                var inlineLbl = Label(_modalLayer, "LoadoutInlineNotice", errOrNote,
                    x0 + 260f * s, y0 + 626f * s, 1410f * s, 36f * s, 24f, false, isDanger ? Danger : Jade);
                inlineLbl.alignment = TextAlignmentOptions.Center;
                inlineLbl.enableAutoSizing = true;
                inlineLbl.fontSizeMin = 17f;
                inlineLbl.fontSizeMax = 24f;
            }

            // 底部三枚已绘制按钮热区：保存 (386,680,386,61)、切换/续期 (805,679,382,64)、关闭/恢复 (1223,680,244,61)
            bool busy = _client.BusyEquipLoadout || _client.BusyEquipLoadoutUnlock || _client.BusyEquip || WriteBlocked;
            CreatePaintedHotspot(_modalLayer, "LoadoutSaveBtn",
                x0 + 386f * s, y0 + 680f * s, 386f * s, 61f * s,
                !editable ? "已锁定 · 只读预览" : (_client.BusyEquipLoadout ? "保存中…" : "保存当前穿着到此方案"),
                30f, Ink, () =>
                {
                    if (!editable) return;
                    string rawName = (_loadoutNameInput != null ? _loadoutNameInput.text : _loadoutNameDraft)?.Trim() ?? string.Empty;
                    if (!EquipLoadoutModel.ValidateNameDraft(rawName, out string nameErr))
                    {
                        _notice = nameErr;
                        Render();
                        return;
                    }
                    _client.SaveEquipLoadout(currentScheme.SchemeId, rawName, _loadoutRoleTagDraft,
                        captureCurrentEquipped: true, explicitSlots: null, _loadoutLinkedSkillDraft, _loadoutLinkedAttrDraft);
                }, editable && !busy);

            if (!editable)
            {
                CreatePaintedHotspot(_modalLayer, "LoadoutSwitchBtn",
                    x0 + 805f * s, y0 + 679f * s, 382f * s, 64f * s,
                    "续期 / 管理席位", 32f, Cream,
                    () => OpenEquipLoadoutRentModal(snap), true);
            }
            else
            {
                CreatePaintedHotspot(_modalLayer, "LoadoutSwitchBtn",
                    x0 + 805f * s, y0 + 679f * s, 382f * s, 64f * s,
                    _client.BusyEquipLoadout ? "切换中…" : "一键切换本方案", 32f, Cream,
                    () => _client.SwitchEquipLoadout(currentScheme.SchemeId, _loadoutApplyLinked), !busy);
            }

            if (_client.HasPendingEquipLoadoutUnlock)
            {
                CreatePaintedHotspot(_modalLayer, "LoadoutRecoverUnlock",
                    x0 + 1223f * s, y0 + 680f * s, 244f * s, 61f * s,
                    "确认上次记录", 28f, Jade, () => _client.RecoverEquipLoadoutUnlock(),
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
            Label(_modalLayer, "EquipLoadoutTitle", "装备流派方案预设（7 套三合一共享）", mx + 720f, my + 44f, 1686f, 84f, 56f, false, Jade)
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

            uint maxSchemes = EquipLoadoutModel.EffectiveMaxSchemes(snap);
            int tabCount = Math.Min(snap.Schemes.Count, (int)maxSchemes);
            float tabW = 398f, tabH = 92f, tabGap = 16f;
            float tabStartX = mx + (mw - (tabCount * tabW + Math.Max(0, tabCount - 1) * tabGap)) * 0.5f;
            for (int i = 0; i < tabCount; ++i)
            {
                var scheme = snap.Schemes[i];
                bool isSelected = scheme.SchemeId == _loadoutSelectedSchemeId;
                bool isActive = scheme.SchemeId == snap.ActiveSchemeId;
                uint capturedId = scheme.SchemeId;
                bool capturedCanPreview = scheme.Unlocked || EquipLoadoutModel.HasSavedBindings(scheme) || isActive;
                ChoiceRow(_modalLayer, "LoadoutTab_" + scheme.SchemeId,
                    EquipLoadoutModel.FormatCompactTabLabel(snap, scheme, isActive),
                    tabStartX + i * (tabW + tabGap), my + 150f, tabW, tabH, () =>
                    {
                        if (capturedCanPreview) { _loadoutSelectedSchemeId = capturedId; SyncLoadoutDraftFromSnapshot(snap, capturedId); Render(); }
                        else OpenEquipLoadoutRentModal(snap);
                    }, selected: isSelected);
            }

            var currentScheme = FindLoadoutScheme(snap, _loadoutSelectedSchemeId)
                ?? snap.Schemes.FirstOrDefault(sItem => sItem.Unlocked);
            if (currentScheme == null) return;
            bool editable = currentScheme.Unlocked;

            float leftX = mx + 120f, leftY = my + 276f;
            string leftHeading = editable ? $"八槽装备对照（缺失 {currentScheme.MissingCount} 槽）" : "八槽装备对照（只读预览 · 到期只锁不删）";
            Label(_modalLayer, "LoadoutSlotsHeading", leftHeading, leftX, leftY, 1380f, 54f, 38f, false, editable ? Jade : Danger);
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
            Button(_modalLayer, "LoadoutManageSeatsBtn", $"管理/续期共享席位（{EquipLoadoutModel.EffectiveUnlockedSchemes(snap)}/{maxSchemes}）",
                rightX, leftY, rightW, () => OpenEquipLoadoutRentModal(snap), primary: false);
            _loadoutNameInput = QdaoUguiFactory.CreateInputField("LoadoutNameInput", _modalLayer, rightX, leftY + 180f, rightW, 84f,
                $"留空默认「{EquipLoadoutModel.DefaultSchemeName(currentScheme.SchemeId)}」", EquipLoadoutModel.MaxNameCodePoints);
            if (_loadoutNameInput.textComponent != null) _loadoutNameInput.textComponent.richText = false;
            _loadoutNameInput.interactable = editable;
            _loadoutNameInput.text = _loadoutNameDraft;
            _loadoutNameInput.onValueChanged.AddListener(v => { if (editable) _loadoutNameDraft = v ?? string.Empty; });

            bool busy = _client.BusyEquipLoadout || _client.BusyEquipLoadoutUnlock || _client.BusyEquip || WriteBlocked;
            Button(_modalLayer, "LoadoutSaveBtn", !editable ? "已锁定 · 只读" : (_client.BusyEquipLoadout ? "保存中…" : "保存当前穿着到此方案"),
                mx + 180f, my + 1218f, 840f, () =>
                {
                    if (!editable) return;
                    string rawName = (_loadoutNameInput != null ? _loadoutNameInput.text : _loadoutNameDraft)?.Trim() ?? string.Empty;
                    if (!EquipLoadoutModel.ValidateNameDraft(rawName, out string nameErr)) { _notice = nameErr; Render(); return; }
                    _client.SaveEquipLoadout(currentScheme.SchemeId, rawName, _loadoutRoleTagDraft,
                        captureCurrentEquipped: true, explicitSlots: null, _loadoutLinkedSkillDraft, _loadoutLinkedAttrDraft);
                }, false, editable && !busy);
            if (!editable)
            {
                Button(_modalLayer, "LoadoutSwitchBtn", "续期 / 管理席位",
                    mx + 1140f, my + 1218f, 840f, () => OpenEquipLoadoutRentModal(snap), true, true);
            }
            else
            {
                Button(_modalLayer, "LoadoutSwitchBtn", _client.BusyEquipLoadout ? "切换中…" : "一键切换本方案",
                    mx + 1140f, my + 1218f, 840f, () => _client.SwitchEquipLoadout(currentScheme.SchemeId, _loadoutApplyLinked), true, !busy);
            }
            Button(_modalLayer, "LoadoutClose", "关闭", mx + 2180f, my + 1218f, 680f, CloseModal);
        }

        /// <summary>
        /// 方案 A 统一 7 套流派席位租用管理弹窗：
        /// 直接把 EquipLoadoutSnapshot 投影为 SkillLoadoutView 并经 SkillLoadoutRentPlanner.Plan 试算，
        /// 确认后走 ISkillLoadoutSource.RentLoadoutSlots，与战法/加点完全共享同一席位与同一到期日。
        /// </summary>
        private void RenderEquipLoadoutUnlockModal()
        {
            var snap = _client?.EquipLoadouts;
            var view = EquipLoadoutModel.ToUnifiedRentView(snap);
            const float mx = 483f, my = 80f, mw = 3000f, mh = 1420f;
            Panel(_modalLayer, "EquipLoadoutRentPaper", mx, my, mw, mh, true);

            var passIcon = LoadEquipLoadoutSprite(SkillLoadoutRentPlanner.ArtBasePath + SkillLoadoutRentPlanner.ArtSlotPassIcon);
            float titleX = passIcon != null ? mx + 186f : mx + 108f;
            if (passIcon != null)
            {
                var iconImg = QdaoUguiFactory.CreateImage("EquipRentPassIcon", _modalLayer, mx + 96f, my + 52f, 76f, 76f, passIcon, false);
                iconImg.preserveAspect = true;
            }
            var titleLbl = Label(_modalLayer, "EquipRentTitle", "流派席位租用管理（装备+战法+加点三合一 · 7套封顶）",
                titleX, my + 48f, mw - 240f, 84f, 50f, false, Jade);
            titleLbl.enableAutoSizing = true;
            titleLbl.fontSizeMin = 32f;
            titleLbl.fontSizeMax = 50f;

            uint maxRentable = view.MaxRentableExtraSlots;
            uint activeRented = view.IsRentActive ? Math.Min(view.RentedExtraSlots, SkillLoadoutRentPlanner.MaxRentedExtraSlots) : 0u;
            uint minAllowed = Math.Min(activeRented, maxRentable);
            uint unlocked = view.UnlockedSchemes;

            string freeDesc = view.FreeSchemes >= 3u
                ? $"永久免费 {view.FreeSchemes} 套（基础2+满级85赠1）"
                : $"永久免费 {view.FreeSchemes} 套（达85级再免费+1套）";
            Label(_modalLayer, "EquipRentStatus1",
                $"{freeDesc}  ·  轻享卡 +{view.LightCardBonusSchemes} 套  ·  已租 {activeRented} 套  ·  共解锁 {unlocked}/7 套",
                mx + 108f, my + 154f, 2784f, 52f, 36f, false, Ink);
            Label(_modalLayer, "EquipRentStatus2",
                $"当前额外席位租期：{SkillLoadoutRentPlanner.FormatRemaining(view.RemainingRentSeconds)}   |   当前真实灵石余额：{view.DiamondBalance}   |   三合一共享不重复收费",
                mx + 108f, my + 214f, 2784f, 48f, 32f, false, Muted);

            Label(_modalLayer, "EquipRentStep1",
                $"1. 选择额外租用席位数（单价 {view.RentUnitPriceDiamond} 真实灵石/套/{view.RentPeriodDays}天，租期内可补差价扩容）",
                mx + 108f, my + 286f, 2784f, 48f, 34f, false, Jade);

            for (uint t = 1u; t <= SkillLoadoutRentPlanner.MaxRentedExtraSlots; ++t)
            {
                uint captured = t;
                bool overCap = t > maxRentable;
                bool underActive = view.IsRentActive && t < minAllowed;
                bool enabled = !overCap && !underActive && !_loadoutRentBusy;
                bool selected = !overCap && !underActive && _loadoutRentTargetSlots == t;
                string sub = overCap
                    ? "已达7套上限"
                    : (underActive
                        ? "租期不可减"
                        : $"共 {Math.Min(7u, view.FreeSchemes + view.LightCardBonusSchemes + t)} 套");
                var optBtn = ChoiceRow(_modalLayer, "EquipRentTarget_" + t, string.Empty,
                    mx + 108f + (t - 1u) * (528f + 36f), my + 346f, 528f, 156f, () =>
                    {
                        _loadoutRentTargetSlots = captured;
                        if (!view.IsRentActive && _loadoutRentAddPeriods == 0u) _loadoutRentAddPeriods = 1u;
                        if (view.IsRentActive && _loadoutRentTargetSlots <= activeRented && _loadoutRentAddPeriods == 0u) _loadoutRentAddPeriods = 1u;
                        _loadoutRentError = string.Empty;
                        Render();
                    }, selected: selected, enabled: enabled);
                var mainLbl = Label(optBtn.transform, "Main", $"+{t} 套", 16f, 18f, 496f, 64f, 42f, false, enabled ? Jade : Muted);
                mainLbl.alignment = TextAlignmentOptions.Center;
                var subLbl = Label(optBtn.transform, "Sub", sub, 16f, 86f, 496f, 50f, 28f, false, enabled ? Ink : Muted);
                subLbl.alignment = TextAlignmentOptions.Center;
            }

            Label(_modalLayer, "EquipRentStep2",
                $"2. 选择续期时长（统一到期日，剩余最长可叠至 {view.RentMaxRemainingDays} 天，手动作单无自动续费）",
                mx + 108f, my + 530f, 2784f, 48f, 34f, false, Jade);

            (uint periods, string text)[] periodOptions =
            {
                (0u, "仅补差价扩容"),
                (1u, $"+{view.RentPeriodDays} 天"),
                (3u, $"+{view.RentPeriodDays * 3u} 天"),
                (6u, $"+{view.RentPeriodDays * 6u} 天"),
            };
            for (int i = 0; i < periodOptions.Length; ++i)
            {
                uint p = periodOptions[i].periods;
                var testQuote = SkillLoadoutRentPlanner.Plan(view, _loadoutRentTargetSlots, p);
                bool enabled = testQuote.Valid && !_loadoutRentBusy;
                bool selected = _loadoutRentAddPeriods == p;
                var pBtn = ChoiceRow(_modalLayer, "EquipRentPeriod_" + p, string.Empty,
                    mx + 108f + i * (666f + 40f), my + 590f, 666f, 114f, () =>
                    {
                        _loadoutRentAddPeriods = p;
                        _loadoutRentError = string.Empty;
                        Render();
                    }, selected: selected, enabled: enabled);
                var pLbl = Label(pBtn.transform, "Lbl", periodOptions[i].text, 16f, 0f, 634f, 114f, 36f, false, enabled ? Ink : Muted);
                pLbl.alignment = TextAlignmentOptions.Center;
            }

            var quote = SkillLoadoutRentPlanner.Plan(view, _loadoutRentTargetSlots, _loadoutRentAddPeriods);
            var quoteBox = ChoiceRow(_modalLayer, "EquipRentQuoteBox", string.Empty,
                mx + 108f, my + 732f, 2784f, 290f, null, selected: false, enabled: false);
            if (quote.Valid)
            {
                ulong newRem = quote.NewExpiresAtSec > view.ServerNowSec ? quote.NewExpiresAtSec - view.ServerNowSec : 0UL;
                Label(quoteBox.transform, "QuoteCost",
                    $"本次应付：{quote.TotalCost} 真实灵石（补差价 {quote.ProrateCost} + 续期 {quote.RenewCost}，仅扣非绑真实灵石）",
                    36f, 22f, 2712f, 58f, 38f, false, Jade);
                Label(quoteBox.transform, "QuoteEffect",
                    $"生效后总计解锁 {quote.NewUnlockedSchemes}/7 套（装备+战法+加点同步生效）   |   新租期：{SkillLoadoutRentPlanner.FormatRemaining(newRem)}",
                    36f, 92f, 2712f, 50f, 34f, false, Ink);
            }
            else
            {
                Label(quoteBox.transform, "QuoteErr", quote.Error ?? "请选择有效的席位与时长组合。",
                    36f, 48f, 2712f, 68f, 36f, false, Danger);
            }
            Label(quoteBox.transform, "QuoteGuarantee",
                "零战力保障：仅提供更多流派席位，无任何属性/伤害加成；到期只锁不删，已保存配装与防误卖保护永久保留，续期后立即恢复。",
                36f, 166f, 2712f, 104f, 28f, true, Muted);

            bool enoughDiamond = quote.Valid && view.DiamondBalance >= quote.TotalCost;
            string inlineMsg = !string.IsNullOrEmpty(_loadoutRentError)
                ? _loadoutRentError
                : (quote.Valid && !enoughDiamond
                    ? $"真实灵石不足（需 {quote.TotalCost}，当前 {view.DiamondBalance}）"
                    : string.Empty);
            if (!string.IsNullOrEmpty(inlineMsg))
            {
                var errLbl = Label(_modalLayer, "EquipRentInlineErr", inlineMsg,
                    mx + 108f, my + 1044f, 2784f, 62f, 32f, false, Danger);
                errLbl.alignment = TextAlignmentOptions.Center;
            }

            Button(_modalLayer, "EquipRentLightCardBtn",
                view.LightCardActive ? "仙途轻享卡已生效 (+2套)" : "开通仙途轻享卡 (+2套)",
                mx + 108f, my + 1140f, 780f, () =>
                {
                    if (AfkPassUiRoot.Instance != null)
                    {
                        _modal = Modal.None;
                        Render();
                        AfkPassUiRoot.Instance.ShowPanel();
                    }
                    else
                    {
                        _loadoutRentError = "请在主城「仙途轻享卡」界面查看轻享卡附赠 2 套共享流派席位。";
                        Render();
                    }
                }, primary: false, enabled: !_loadoutRentBusy);

            Button(_modalLayer, "EquipRentBackBtn", "返回方案列表",
                mx + 940f, my + 1140f, 540f, () =>
                {
                    _modal = Modal.EquipLoadout;
                    Render();
                }, primary: false, enabled: !_loadoutRentBusy);

            if (quote.Valid && !enoughDiamond)
            {
                Button(_modalLayer, "EquipRentMallBtn", "前往官方聚宝斋商城",
                    mx + 1532f, my + 1140f, 1360f, () =>
                    {
                        if (JubaozhaiUiRoot.Instance != null)
                        {
                            _modal = Modal.None;
                            Render();
                            JubaozhaiUiRoot.Instance.OpenOfficialMall();
                        }
                        else
                        {
                            _loadoutRentError = "请在主城聚宝斋官方商城获取真实灵石。";
                            Render();
                        }
                    }, primary: true, enabled: !_loadoutRentBusy);
            }
            else
            {
                bool canSubmit = quote.Valid && !_loadoutRentBusy && !WriteBlocked && ResolveSkillLoadouts() != null;
                string confirmText = _loadoutRentBusy
                    ? "开通/续期中…"
                    : (quote.Valid ? $"确认开通/续期（{quote.TotalCost} 真实灵石）" : "请调整席位或时长");
                Button(_modalLayer, "EquipRentConfirmBtn", confirmText,
                    mx + 1532f, my + 1140f, 1360f, () => SubmitEquipLoadoutRent(quote.TotalCost),
                    primary: true, enabled: canSubmit);
            }
        }

        private void SubmitEquipLoadoutRent(ulong expectedCost)
        {
            if (_loadoutRentBusy || WriteBlocked) return;
            var skills = ResolveSkillLoadouts();
            if (skills == null)
            {
                _loadoutRentError = "流派席位服务尚未就绪，请稍后重试。";
                Render();
                return;
            }
            _loadoutRentBusy = true;
            _loadoutRentError = string.Empty;
            Render();
            skills.RentLoadoutSlots(_loadoutRentTargetSlots, _loadoutRentAddPeriods, expectedCost, (ok, message) =>
            {
                if (_disposed) return;
                _loadoutRentBusy = false;
                if (ok)
                {
                    _notice = string.IsNullOrWhiteSpace(message)
                        ? "共享流派席位已生效（装备+战法+加点同步更新）。"
                        : message;
                    _loadoutRentError = string.Empty;
                    _loadoutDraftSynced = false;
                    _modal = Modal.EquipLoadout;
                    _client?.RequestEquipLoadouts();
                }
                else
                {
                    _loadoutRentError = string.IsNullOrWhiteSpace(message)
                        ? "流派席位开通未确认，请刷新查看后重试。"
                        : message;
                }
                if (IsVisible) Render();
            });
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
