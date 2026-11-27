using System;
using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.PlayerFeatures;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    using Transform = UnityEngine.Transform;

    /// <summary>
    /// 战前准备期 1..7 一键换流派通用无状态 uGUI 快捷条（方案 A 三合一共享席位 · 零新图）：
    /// - 直接复用 EquipLoadoutV1 / SkillBuildLoadoutV1 的横向 9-slice 切片；
    /// - 严格单向数据流：输入不可变 <see cref="PreBattleLoadoutBarState"/>，点击通过回调派发 SwitchEquipLoadout / 刷新 / 席位租用轻提示；
    /// - 所有动态方案名称与状态文案强制关闭 richText，触控高度 >= 40px，不依赖 Hover。
    /// </summary>
    public static class PreBattleLoadoutQuickBar
    {
        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        private static readonly Color LightPlateBg = new Color(0.96f, 0.94f, 0.87f, 0.90f);
        private static readonly Color LightBorder = new Color(0.74f, 0.57f, 0.29f, 0.58f);
        private static readonly Color DarkPlateBg = new Color(0.11f, 0.17f, 0.16f, 0.86f);
        private static readonly Color DarkBorder = new Color(0.75f, 0.62f, 0.36f, 0.65f);
        private static readonly Color ActiveFillFallback = QdaoUguiTheme.Html("#1F5A45");
        private static readonly Color UnlockedFillFallback = QdaoUguiTheme.Html("#F7F1E2");
        private static readonly Color LockedFillFallback = new Color(0.84f, 0.80f, 0.72f, 0.90f);
        private static readonly Color HeadingJade = QdaoUguiTheme.Html("#174A3D");
        private static readonly Color Ink = QdaoUguiTheme.Html("#1C3128");
        private static readonly Color Muted = QdaoUguiTheme.Html("#5E6E64");
        private static readonly Color Cream = QdaoUguiTheme.Html("#FFF4D6");
        private static readonly Color Danger = QdaoUguiTheme.Html("#B23A2A");
        private static readonly Color DangerLight = QdaoUguiTheme.Html("#FFAD99");

        /// <summary>打开备战界面时按需补拉装备流派快照（转发至纯 C# 模型）。</summary>
        public static void EnsureRequested(PlayerFeaturesClient features)
            => EquipLoadoutModel.EnsureEquipLoadoutsRequested(features);

        /// <summary>从 <see cref="PlayerFeaturesClient"/> 与当前战斗相位构造不可变快捷条视图状态（转发至纯 C# 模型）。</summary>
        public static PreBattleLoadoutBarState BuildState(PlayerFeaturesClient features, BattlePhase battlePhase)
            => EquipLoadoutModel.BuildPreBattleBarState(features, battlePhase);

        /// <summary>供 CoreLoop 与切磋弹窗使用的便捷入口：自动推导状态并把点击路由到 SwitchEquipLoadout RPC。</summary>
        public static RectTransform RenderForFeatures(
            Transform parent,
            string name,
            float x,
            float y,
            float width,
            float height,
            PlayerFeaturesClient features,
            BattlePhase battlePhase,
            Action<string> onToast,
            bool darkBackdrop = false)
        {
            var state = BuildState(features, battlePhase);
            return Render(
                parent, name, x, y, width, height, state,
                onSwitchScheme: schemeId => features?.SwitchEquipLoadout(schemeId, applyLinkedSchemes: true),
                onLockedClicked: schemeId => onToast?.Invoke(
                    EquipLoadoutModel.FormatLockedPreBattleHint(schemeId, features?.EquipLoadouts)),
                onRefreshClicked: () => features?.RequestEquipLoadouts(),
                darkBackdrop: darkBackdrop);
        }

        /// <summary>纯快照渲染入口（供 TournamentWindow 等严格单向数据流视图调用）。</summary>
        public static RectTransform Render(
            Transform parent,
            string name,
            float x,
            float y,
            float width,
            float height,
            PreBattleLoadoutBarState state,
            Action<uint> onSwitchScheme,
            Action<uint> onLockedClicked,
            Action onRefreshClicked,
            bool darkBackdrop = false)
        {
            if (parent == null || state == null) return null;

            var root = QdaoUguiFactory.CreateRect(name, parent, x, y, width, height);
            var bg = root.gameObject.AddComponent<Image>();
            bg.color = darkBackdrop ? DarkPlateBg : LightPlateBg;
            bg.raycastTarget = false;

            Color borderCol = darkBackdrop ? DarkBorder : LightBorder;
            CreateRule(root, "BorderTop", 0f, 0f, width, 1.5f, borderCol);
            CreateRule(root, "BorderBottom", 0f, height - 1.5f, width, 1.5f, borderCol);

            bool twoRow = height >= 72f;
            bool compactMultiRow = !twoRow && width < 760f && state.Slots.Count > 5;
            if (twoRow)
            {
                RenderTwoRow(root, width, height, state, onSwitchScheme, onLockedClicked, onRefreshClicked, darkBackdrop);
            }
            else if (compactMultiRow)
            {
                RenderCompactMultiRow(root, width, height, state, onSwitchScheme, onLockedClicked, onRefreshClicked, darkBackdrop);
            }
            else
            {
                RenderSingleRow(root, width, height, state, onSwitchScheme, onLockedClicked, onRefreshClicked, darkBackdrop);
            }
            return root;
        }

        /// <summary>窄栏（544~759px）排 6..7 槽：顶栏 14~16px 放标题/状态，下方全宽排满 7 签，避免左侧标题挤压 9-slice 端头并保底 40px 触控高。</summary>
        private static void RenderCompactMultiRow(
            RectTransform root,
            float width,
            float height,
            PreBattleLoadoutBarState state,
            Action<uint> onSwitchScheme,
            Action<uint> onLockedClicked,
            Action onRefreshClicked,
            bool darkBackdrop)
        {
            const float padX = 6f;
            float innerW = Mathf.Max(120f, width - padX * 2f);
            float topH = height >= 60f ? 16f : 14f;
            Color headColor = darkBackdrop ? Cream : HeadingJade;
            Color subColor = state.StatusIsError
                ? (darkBackdrop ? DangerLight : Danger)
                : (darkBackdrop ? QdaoUguiTheme.StatusCream : Muted);

            bool alertMode = state.StatusIsError || state.BlockedByBattleState || state.Switching || !state.Synced;
            if (alertMode)
            {
                var alert = CreateSafeLabel(root, "CompactAlert", padX, 1f, innerW, topH,
                    state.StatusText, 12f, subColor, TextAlignmentOptions.Center, bold: state.StatusIsError);
                alert.fontSizeMin = 9.5f;
            }
            else
            {
                float headW = Mathf.Min(innerW * 0.56f, 260f);
                var head = CreateSafeLabel(root, "CompactHeader", padX, 1f, headW, topH,
                    state.HeaderText, 12f, headColor, TextAlignmentOptions.MidlineLeft, bold: true);
                head.fontSizeMin = 9.5f;

                var hint = CreateSafeLabel(root, "CompactHint", padX + headW + 4f, 1f, Mathf.Max(40f, innerW - headW - 4f), topH,
                    "点选切换整套流派", 11.5f, subColor, TextAlignmentOptions.MidlineRight, bold: false);
                hint.fontSizeMin = 9.5f;
            }

            float rowY = topH + 1f;
            float btnH = Mathf.Clamp(height - rowY - 1f, 38f, 42f);
            RenderSlotsOrRefresh(root, padX, rowY, innerW, btnH, state, onSwitchScheme, onLockedClicked, onRefreshClicked, darkBackdrop);
        }

        private static void RenderTwoRow(
            RectTransform root,
            float width,
            float height,
            PreBattleLoadoutBarState state,
            Action<uint> onSwitchScheme,
            Action<uint> onLockedClicked,
            Action onRefreshClicked,
            bool darkBackdrop)
        {
            const float padX = 10f;
            float innerW = Mathf.Max(120f, width - padX * 2f);
            bool tallFooter = height >= 104f;
            float headerH = tallFooter ? 26f : 22f;
            float headerFont = width >= 900f ? 19f : 16f;
            Color headColor = darkBackdrop ? Cream : HeadingJade;
            Color subColor = state.StatusIsError
                ? (darkBackdrop ? DangerLight : Danger)
                : (darkBackdrop ? QdaoUguiTheme.StatusCream : Muted);

            if (tallFooter)
            {
                var head = CreateSafeLabel(root, "Header", padX, 4f, innerW, headerH,
                    state.HeaderText, headerFont, headColor, TextAlignmentOptions.Center, bold: true);
                head.fontSizeMin = 13f;

                float rowY = headerH + 8f;
                float btnH = Mathf.Clamp(height - headerH - 36f, 40f, 50f);
                RenderSlotsOrRefresh(root, padX, rowY, innerW, btnH, state, onSwitchScheme, onLockedClicked, onRefreshClicked, darkBackdrop);

                var status = CreateSafeLabel(root, "Status", padX, rowY + btnH + 4f, innerW, 22f,
                    state.StatusText, 14f, subColor, TextAlignmentOptions.Center, bold: state.StatusIsError);
                status.fontSizeMin = 11f;
            }
            else
            {
                float halfW = innerW * 0.52f;
                var head = CreateSafeLabel(root, "Header", padX, 3f, halfW, headerH,
                    state.HeaderText, headerFont, headColor, TextAlignmentOptions.MidlineLeft, bold: true);
                head.fontSizeMin = 13f;

                var status = CreateSafeLabel(root, "Status", padX + halfW + 8f, 3f, innerW - halfW - 8f, headerH,
                    state.StatusText, width >= 900f ? 16f : 13f, subColor, TextAlignmentOptions.MidlineRight, bold: state.StatusIsError);
                status.fontSizeMin = 11f;

                float rowY = headerH + 5f;
                float btnH = Mathf.Clamp(height - rowY - 4f, 40f, 48f);
                RenderSlotsOrRefresh(root, padX, rowY, innerW, btnH, state, onSwitchScheme, onLockedClicked, onRefreshClicked, darkBackdrop);
            }
        }

        private static void RenderSingleRow(
            RectTransform root,
            float width,
            float height,
            PreBattleLoadoutBarState state,
            Action<uint> onSwitchScheme,
            Action<uint> onLockedClicked,
            Action onRefreshClicked,
            bool darkBackdrop)
        {
            const float padX = 10f;
            bool showTopInlineAlert = (state.StatusIsError || state.BlockedByBattleState || state.Switching) && width < 1100f && height >= 56f;
            float alertH = showTopInlineAlert ? 15f : 0f;
            Color headColor = darkBackdrop ? Cream : HeadingJade;
            Color alertColor = state.StatusIsError
                ? (darkBackdrop ? DangerLight : Danger)
                : (darkBackdrop ? QdaoUguiTheme.StatusCream : Muted);

            if (showTopInlineAlert)
            {
                var alert = CreateSafeLabel(root, "InlineAlert", padX, 1f, width - padX * 2f, alertH,
                    state.StatusText, 12f, alertColor, TextAlignmentOptions.Center, bold: state.StatusIsError);
                alert.fontSizeMin = 10f;
            }

            float contentY = showTopInlineAlert ? alertH + 1f : 3f;
            float contentH = Mathf.Max(36f, height - contentY - 3f);

            float labelW = width >= 1100f ? 154f : (width >= 760f ? 88f : 60f);
            if (width >= 1100f)
            {
                var title = CreateSafeLabel(root, "Title", padX, contentY, labelW, contentH * 0.56f,
                    "战前流派", 20f, headColor, TextAlignmentOptions.MidlineLeft, bold: true);
                title.fontSizeMin = 14f;

                string sub = state.StatusIsError || state.BlockedByBattleState || state.Switching || !state.Synced
                    ? state.StatusText
                    : "同步战法/加点";
                var subLbl = CreateSafeLabel(root, "Sub", padX, contentY + contentH * 0.54f, labelW, contentH * 0.44f,
                    sub, 14f, alertColor, TextAlignmentOptions.MidlineLeft, bold: state.StatusIsError);
                subLbl.fontSizeMin = 11f;
            }
            else
            {
                string shortTitle = width >= 760f ? "换流派" : "流派";
                var title = CreateSafeLabel(root, "Title", padX, contentY, labelW, contentH,
                    shortTitle, width >= 760f ? 18f : 15f, headColor, TextAlignmentOptions.MidlineLeft, bold: true);
                title.fontSizeMin = 12f;
            }

            float slotsX = padX + labelW + 6f;
            float slotsW = Mathf.Max(180f, width - slotsX - padX);
            float btnH = Mathf.Clamp(contentH - 2f, 38f, 48f);
            float btnY = contentY + (contentH - btnH) * 0.5f;
            RenderSlotsOrRefresh(root, slotsX, btnY, slotsW, btnH, state, onSwitchScheme, onLockedClicked, onRefreshClicked, darkBackdrop);
        }

        private static void RenderSlotsOrRefresh(
            RectTransform parent,
            float x,
            float y,
            float width,
            float height,
            PreBattleLoadoutBarState state,
            Action<uint> onSwitchScheme,
            Action<uint> onLockedClicked,
            Action onRefreshClicked,
            bool darkBackdrop)
        {
            if (!state.Synced)
            {
                float refreshBtnW = Mathf.Min(160f, width * 0.34f);
                float msgW = state.Loading ? width : Mathf.Max(80f, width - refreshBtnW - 10f);
                var wait = CreateSafeLabel(parent, "SyncHint", x, y, msgW, height,
                    state.StatusText, width >= 760f ? 18f : 14f,
                    darkBackdrop ? QdaoUguiTheme.StatusCream : Muted, TextAlignmentOptions.MidlineLeft, bold: false);
                wait.fontSizeMin = 12f;

                if (!state.Loading && !state.BlockedByBattleState)
                {
                    var sprite = LoadTabSprite(EquipLoadoutModel.TabUnlockedKey);
                    var btn = QdaoUguiFactory.CreateArtButton("RefreshLoadoutsBtn", parent,
                        x + width - refreshBtnW, y, refreshBtnW, height, sprite, out var img);
                    ApplyTabVisual(img, sprite, UnlockedFillFallback, enabled: true);
                    btn.onClick.AddListener(() => onRefreshClicked?.Invoke());
                    var lbl = CreateSafeLabel(btn.transform, "Lbl", 8f, 0f, refreshBtnW - 16f, height,
                        "刷新流派", width >= 760f ? 18f : 14f, Ink, TextAlignmentOptions.Center, bold: true);
                    lbl.fontSizeMin = 12f;
                }
                return;
            }

            int count = state.Slots.Count;
            if (count <= 0) return;

            float gap = width >= 900f ? 10f : (width >= 660f ? 6f : (count > 5 ? 3f : 4f));
            float tabW = (width - gap * (count - 1)) / count;
            float baseFont = tabW >= 180f ? 20f : (tabW >= 120f ? 17f : (tabW >= 84f ? 14f : 12.5f));
            float minFont = tabW >= 120f ? 12f : 9.5f;
            bool narrowSlot = tabW < 86f;

            for (int i = 0; i < count; ++i)
            {
                var slot = state.Slots[i];
                float tx = x + i * (tabW + gap);
                var sprite = LoadTabSprite(slot.SpriteKey);
                var btn = QdaoUguiFactory.CreateArtButton("PreBattleLoadout_" + slot.SchemeId, parent,
                    tx, y, tabW, height, sprite, out var img);

                Color fallback = !slot.Unlocked
                    ? LockedFillFallback
                    : (slot.Active ? ActiveFillFallback : UnlockedFillFallback);
                bool visualActive = slot.Active || slot.CanClick;
                ApplyTabVisual(img, sprite, fallback, enabled: visualActive);

                btn.interactable = slot.CanClick;
                uint capturedId = slot.SchemeId;
                var clickKind = slot.ClickKind;
                btn.onClick.AddListener(() =>
                {
                    switch (clickKind)
                    {
                        case PreBattleLoadoutClickKind.Switch:
                            onSwitchScheme?.Invoke(capturedId);
                            break;
                        case PreBattleLoadoutClickKind.UnlockHint:
                            onLockedClicked?.Invoke(capturedId);
                            break;
                        case PreBattleLoadoutClickKind.Refresh:
                            onRefreshClicked?.Invoke();
                            break;
                    }
                });

                // 与 EquipmentWindow.Overview / SkillLoadoutSubPanel 严格同口径：
                // 激活已解锁签与深底租用签(tab_build_rented)用浅色 Cream 字；到期只锁不删的激活签落浅灰锁底用深墨 Ink。
                bool isRentedVisual = slot.Unlocked && !slot.Active
                    && string.Equals(slot.SpriteKey, EquipLoadoutModel.TabRentedKey, StringComparison.Ordinal);
                bool lightInk = (slot.Active && slot.Unlocked) || (isRentedVisual && sprite != null);
                Color textColor = lightInk
                    ? Cream
                    : (slot.Active ? Ink : (!slot.Unlocked ? Muted : (slot.CanClick ? Ink : Muted)));

                float leftPad = tabW >= 140f ? 10f : (tabW >= 90f ? 6f : 3f);
                // tab_build_rented 右侧自带沙漏装饰图标，文本右缘适度左让避免叠字。
                float rightPad = leftPad + (isRentedVisual && sprite != null ? Mathf.Min(16f, tabW * 0.18f) : 0f);
                string displayLabel = EquipLoadoutModel.FormatCompactSlotLabel(slot, narrowSlot);
                var lbl = CreateSafeLabel(btn.transform, "Lbl", leftPad, 0f,
                    Mathf.Max(20f, tabW - leftPad - rightPad), height,
                    displayLabel, baseFont, textColor, TextAlignmentOptions.Center, bold: slot.Active);
                lbl.fontSizeMin = minFont;
            }
        }

        private static void ApplyTabVisual(Image img, Sprite sprite, Color fallback, bool enabled)
        {
            if (img == null) return;
            if (sprite != null)
            {
                SeasonEconomyUiArt.ApplyHorizontalSlice(img, sprite, img.rectTransform.sizeDelta.y);
                img.color = enabled ? Color.white : new Color(0.84f, 0.84f, 0.82f, 0.78f);
            }
            else
            {
                img.sprite = null;
                img.type = Image.Type.Simple;
                img.color = enabled ? fallback : new Color(fallback.r, fallback.g, fallback.b, 0.60f);
            }
        }

        private static Sprite LoadTabSprite(string resourceKey)
        {
            if (string.IsNullOrEmpty(resourceKey)) return null;
            if (SpriteCache.TryGetValue(resourceKey, out var cached)) return cached;
            var loaded = Resources.Load<Sprite>(resourceKey);
            SpriteCache[resourceKey] = loaded;
            return loaded;
        }

        private static TMP_Text CreateSafeLabel(
            Transform parent,
            string name,
            float x,
            float y,
            float w,
            float h,
            string text,
            float fontSize,
            Color color,
            TextAlignmentOptions align,
            bool bold)
        {
            var lbl = QdaoUguiFactory.CreateText(name, parent, x, y, w, h, text ?? string.Empty, fontSize, color, align);
            lbl.richText = false;
            lbl.raycastTarget = false;
            lbl.textWrappingMode = TextWrappingModes.NoWrap;
            lbl.overflowMode = TextOverflowModes.Ellipsis;
            lbl.enableAutoSizing = true;
            lbl.fontSizeMax = fontSize;
            if (bold) lbl.fontStyle = FontStyles.Bold;
            return lbl;
        }

        private static void CreateRule(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, null, false);
            img.color = color;
        }
    }
}
