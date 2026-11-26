using System;
using System.Globalization;
using MmorpgClient.Game.TravelPass;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.TravelPass
{
    /// <summary>
    /// 青云游历手记可交互 uGUI 窗口（零强制、零日常签到、先玩后买、解锁永不过期）。
    /// 优先加载 Assets/Resources/UI/Ugui/TravelPassV1/ 专属皮肤切片，缺省无缝回落至 GameplayUiArt。
    /// </summary>
    public sealed class TravelPassWindow : IDisposable
    {
        public const string SkinRoot = "UI/Ugui/TravelPassV1/";
        public const string CloseButtonName = "TravelPassClose";
        public const string RefreshButtonName = "TravelPassRefresh";
        public const string ClaimAllButtonName = "TravelPassClaimAll";
        public const string UnlockButtonName = "TravelPassUnlockPremium";
        public const string ConfirmUnlockButtonName = "TravelPassConfirmUnlock";
        public const string ConfirmCancelButtonName = "TravelPassConfirmCancel";
        public const string ConfirmRechargeButtonName = "TravelPassConfirmRecharge";

        private const float FrameWidth = 1860f;
        private const float FrameHeight = 960f;
        private const float ContentX = 56f;
        private const float ContentY = 278f;
        private const float ContentWidth = FrameWidth - 112f;
        private const float ContentHeight = 584f;

        private static readonly Color DangerInk = QdaoUguiTheme.Html("#A94735");
        private static readonly Color JadeAccent = QdaoUguiTheme.Html("#1F6B52");
        private static readonly Color GoldAccent = QdaoUguiTheme.Html("#9A6B22");
        private static readonly Color ReachedTint = new Color(0.96f, 0.94f, 0.85f, 0.96f);
        private static readonly Color LockedTint = new Color(0.92f, 0.90f, 0.84f, 0.88f);
        private static readonly Color PremiumTint = new Color(0.99f, 0.95f, 0.84f, 0.98f);

        private readonly RectTransform _root, _frame, _contentHost, _confirmOverlay, _progressFill;
        private readonly TMP_Text _titleText, _diamondText, _bindDiamondText, _progressText, _statusText;
        private readonly TMP_Text _confirmTitle, _confirmBody, _confirmNotice;
        private readonly Button _claimAllButton, _unlockButton, _confirmUnlockButton, _confirmRechargeButton;
        private readonly TMP_Text _claimAllLabel, _unlockLabel;

        private TravelPassClient _client;
        private Action _openRechargeAction;
        private TravelPassUnlockQuote _confirmQuote;
        private bool _disposed;

        public RectTransform Root => _root;
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        public event Action Closed;

        public TravelPassWindow(UnityEngine.Transform parent, Action openRechargeAction = null)
        {
            _openRechargeAction = openRechargeAction;

            _root = QdaoUguiFactory.CreateStretch("TravelPassWindow", parent, Vector4.zero);
            _root.gameObject.SetActive(false);

            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(0.025f, 0.10f, 0.08f, 0.72f);
            shade.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();

            _frame = QdaoUguiFactory.CreateCenteredRect("TravelPassFrame", _root, FrameWidth, FrameHeight);
            CreateSkinOrFallbackArt(_frame, "TravelPassFrameBg", "window_frame", "main_frame", 0f, 0f, FrameWidth, FrameHeight);

            var plaque = CreateSkinOrFallbackArt(_frame, "TravelPassTitlePlaque", "title_plate", "title_plate", 56f, -20f, 520f, 110f);
            _titleText = Text(plaque.transform, "青云游历手记", 28f, 10f, 464f, 84f, 40f, Cream, alignment: TextAlignmentOptions.Center);
            _titleText.raycastTarget = false;

            // 双余额独立展示：严禁把绑定灵石混算成可解锁额度
            _diamondText = Text(_frame, "灵石：0", 600f, 24f, 340f, 40f, 28f, Ink);
            _diamondText.name = "TravelPassDiamondBalance";
            _diamondText.raycastTarget = false;

            _bindDiamondText = Text(_frame, "绑定灵石：0（仅活动赠送，不抵扣典藏解锁）", 600f, 68f, 700f, 32f, 21f, Muted);
            _bindDiamondText.name = "TravelPassBindDiamondBalance";
            _bindDiamondText.raycastTarget = false;

            var refreshBtn = Button(_frame, "刷新", 1564f, 26f, 120f, 66f, () => _client?.RequestState(force: true), fontSize: 25f);
            refreshBtn.name = RefreshButtonName;

            var closeBtn = Button(_frame, "关闭", 1698f, 26f, 110f, 66f, Hide, fontSize: 25f);
            closeBtn.name = CloseButtonName;

            // 承诺条与游历进度条
            var bannerBg = QdaoUguiFactory.CreateImage("TravelPassBanner", _frame, 56f, 114f, ContentWidth, 148f,
                LoadOptionalSkin("travel_pass_banner") ?? LoadOptionalSkin("hero_banner"));
            if (bannerBg.sprite == null) bannerBg.color = new Color(0.93f, 0.89f, 0.79f, 0.94f);
            bannerBg.raycastTarget = false;

            var promise = Text(bannerBg.transform,
                "不肝不逼氪承诺：零日常 · 零签到 · 正常游玩自动累计游历点 · 先玩后买随时补领 · 解锁典藏永不过期 · 全部物品绑定仅自用零战力突破",
                24f, 12f, ContentWidth - 48f, 34f, 22f, JadeAccent);
            promise.fontStyle = FontStyles.Bold;
            promise.raycastTarget = false;

            _progressText = Text(bannerBg.transform, "游历点：0 / 300（常规胜 +20 · 高难/竞技胜 +30 · 有效竞技 +15）",
                24f, 52f, ContentWidth - 620f, 36f, 24f, Ink);
            _progressText.name = "TravelPassProgressLabel";
            _progressText.raycastTarget = false;

            var trackBg = QdaoUguiFactory.CreateImage("TravelPassProgressTrack", bannerBg.transform, 24f, 98f,
                ContentWidth - 620f, 28f, LoadOptionalSkin("progress_track"));
            if (trackBg.sprite == null) trackBg.color = new Color(0.76f, 0.72f, 0.62f, 1f);
            trackBg.raycastTarget = false;

            var fillImg = QdaoUguiFactory.CreateImage("TravelPassProgressFill", bannerBg.transform, 26f, 100f,
                0f, 24f, LoadOptionalSkin("progress_fill"));
            if (fillImg.sprite == null) fillImg.color = JadeAccent;
            fillImg.raycastTarget = false;
            _progressFill = fillImg.rectTransform;

            _claimAllButton = Button(bannerBg.transform, "一键领取", ContentWidth - 576f, 56f, 254f, 70f,
                HandleClaimAllClicked, primary: true, fontSize: 25f);
            _claimAllButton.name = ClaimAllButtonName;
            _claimAllLabel = _claimAllButton.GetComponentInChildren<TMP_Text>(true);

            _unlockButton = Button(bannerBg.transform, "解锁典藏 (180灵石)", ContentWidth - 306f, 56f, 282f, 70f,
                OpenUnlockConfirmModal, primary: true, fontSize: 25f);
            _unlockButton.name = UnlockButtonName;
            _unlockLabel = _unlockButton.GetComponentInChildren<TMP_Text>(true);

            CreateSkinOrFallbackArt(_frame, "TravelPassContentBg", "content_panel", "content_panel",
                ContentX, ContentY, ContentWidth, ContentHeight);
            _contentHost = QdaoUguiFactory.CreateRect("TravelPassContentHost", _frame,
                ContentX + 16f, ContentY + 14f, ContentWidth - 32f, ContentHeight - 28f);

            _statusText = Text(_frame, "", 60f, 876f, 1740f, 58f, 25f, Muted);
            _statusText.name = "TravelPassStatus";
            _statusText.raycastTarget = false;

            // 典藏解锁二次确认浮层
            _confirmOverlay = QdaoUguiFactory.CreateStretch("TravelPassUnlockConfirmOverlay", _root, Vector4.zero);
            _confirmOverlay.gameObject.SetActive(false);
            var modalDim = _confirmOverlay.gameObject.AddComponent<Image>();
            modalDim.color = new Color(0.02f, 0.06f, 0.05f, 0.72f);
            modalDim.raycastTarget = true;

            var modalFrame = QdaoUguiFactory.CreateCenteredRect("TravelPassConfirmFrame", _confirmOverlay, 980f, 560f);
            Art(modalFrame, "main_frame", 0f, 0f, 980f, 560f);

            _confirmTitle = Text(modalFrame, "解锁典藏游历手记", 56f, 34f, 868f, 52f, 36f, Ink, alignment: TextAlignmentOptions.Center);
            _confirmTitle.raycastTarget = false;

            _confirmBody = Text(modalFrame, "", 64f, 108f, 852f, 206f, 26f, Ink, wrap: true);
            _confirmBody.raycastTarget = false;

            _confirmNotice = Text(modalFrame, "", 64f, 326f, 852f, 86f, 23f, DangerInk, wrap: true);
            _confirmNotice.raycastTarget = false;

            var cancelBtn = Button(modalFrame, "先玩着看看", 64f, 442f, 240f, 74f, CloseConfirmModal, fontSize: 26f);
            cancelBtn.name = ConfirmCancelButtonName;

            _confirmRechargeButton = Button(modalFrame, "前往充值", 332f, 442f, 240f, 74f, () =>
            {
                CloseConfirmModal();
                HandleRechargeClicked();
            }, fontSize: 26f);
            _confirmRechargeButton.name = ConfirmRechargeButtonName;

            _confirmUnlockButton = Button(modalFrame, "确认解锁典藏", 600f, 442f, 316f, 74f, ExecuteConfirmUnlock, primary: true, fontSize: 27f);
            _confirmUnlockButton.name = ConfirmUnlockButtonName;
        }

        public void SetRechargeCallback(Action openRechargeAction) => _openRechargeAction = openRechargeAction;

        public void Bind(TravelPassClient client)
        {
            if (_client == client) return;
            if (_client != null) _client.Changed -= HandleClientChanged;
            _client = client;
            if (_client != null) _client.Changed += HandleClientChanged;
            if (IsVisible) Render();
        }

        public void Show()
        {
            if (_root == null) return;
            ApplySafeArea();
            CloseConfirmModal();
            _root.gameObject.SetActive(true);
            _client?.RequestState(force: true);
            Render();
        }

        public void Hide()
        {
            if (_root == null) return;
            bool wasVisible = _root.gameObject.activeSelf;
            CloseConfirmModal();
            _root.gameObject.SetActive(false);
            if (wasVisible)
            {
                EventSystem.current?.SetSelectedGameObject(null);
                Closed?.Invoke();
            }
        }

        public void Render()
        {
            if (_disposed || _root == null || !_root.gameObject.activeSelf) return;

            var snap = _client?.State;
            _titleText.text = !string.IsNullOrWhiteSpace(snap?.Name) ? snap.Name : "青云游历手记";
            _diamondText.text = $"灵石：{(snap?.DiamondBalance ?? 0).ToString(CultureInfo.InvariantCulture)}";
            _bindDiamondText.text = $"绑定灵石：{(snap?.BindDiamondBalance ?? 0).ToString(CultureInfo.InvariantCulture)}（仅活动赠送，不抵扣典藏解锁）";

            uint pts = snap?.ProgressPoints ?? 0u;
            uint maxPts = snap != null && snap.MaxPoints > 0 ? snap.MaxPoints : 300u;
            uint pvePts = snap != null && snap.PveWinPoints > 0 ? snap.PveWinPoints : 20u;
            uint hardPts = snap != null && snap.HardOrPvpWinPoints > 0 ? snap.HardOrPvpWinPoints : 30u;
            uint qualPts = snap != null && snap.PvpQualifiedOtherPoints > 0 ? snap.PvpQualifiedOtherPoints : 15u;
            _progressText.text = $"游历点：{pts} / {maxPts}（常规胜 +{pvePts} · 高难/竞技胜 +{hardPts} · 有效竞技 +{qualPts}）";

            float ratio = maxPts > 0 ? Mathf.Clamp01((float)pts / maxPts) : 0f;
            float maxFillW = ContentWidth - 624f;
            _progressFill.sizeDelta = new Vector2(ratio > 0.001f ? Math.Max(8f, maxFillW * ratio) : 0f, 24f);

            uint claimable = TravelPassModels.TotalClaimable(snap);
            bool busy = _client?.AnyWritePending ?? false;
            bool inBattle = _client?.InBattle ?? false;

            if (_claimAllLabel != null)
                _claimAllLabel.text = claimable > 0 ? $"一键领取 ({claimable})" : "暂无可领奖励";
            _claimAllButton.interactable = snap != null && snap.Available && claimable > 0 && !busy && !inBattle;

            if (snap != null && snap.PremiumUnlocked)
            {
                if (_unlockLabel != null) _unlockLabel.text = "已解锁典藏 · 永不过期";
                _unlockButton.interactable = false;
            }
            else
            {
                ulong price = snap != null && snap.PremiumPrice > 0 ? snap.PremiumPrice : 180UL;
                if (_unlockLabel != null) _unlockLabel.text = $"解锁典藏 ({price}灵石)";
                _unlockButton.interactable = snap != null && snap.Available && snap.OpenForUnlock && !busy && !inBattle;
            }

            UpdateStatusLine();
            RebuildBody(snap);
            RefreshConfirmModalState();
        }

        private void HandleClientChanged()
        {
            if (_disposed || _root == null || !_root.gameObject.activeSelf) return;
            Render();
        }

        private void UpdateStatusLine()
        {
            string msg = _client?.Status ?? string.Empty;
            bool isErr = _client?.StatusIsError ?? false;
            if (string.IsNullOrEmpty(msg) && _client != null && _client.InBattle)
            {
                msg = "战斗或观战期间仅可浏览游历手记，结束后即可领取或解锁。";
                isErr = false;
            }
            _statusText.text = msg;
            _statusText.color = isErr ? DangerInk : Muted;
        }

        private void RebuildBody(global::TravelPassSnapshot snap)
        {
            Clear(_contentHost);
            float viewW = ContentWidth - 32f;
            float viewH = ContentHeight - 28f;

            if (_client == null || !_client.IsSupported)
            {
                Text(_contentHost, TravelPassClient.UnsupportedMessage, 40f, 120f, viewW - 80f, 90f,
                    30f, Muted, alignment: TextAlignmentOptions.Center).raycastTarget = false;
                return;
            }
            if (snap == null)
            {
                string tip = _client.StatePending ? TravelPassClient.StatePendingMessage : "正在等待游历手记进度同步…";
                Text(_contentHost, tip, 40f, 120f, viewW - 80f, 90f,
                    30f, Muted, alignment: TextAlignmentOptions.Center).raycastTarget = false;
                return;
            }
            if (!snap.Available)
            {
                string reason = string.IsNullOrWhiteSpace(snap.UnavailableReason) ? "当前暂未开启游历手记。" : snap.UnavailableReason;
                Text(_contentHost, reason, 40f, 120f, viewW - 80f, 90f,
                    30f, Muted, alignment: TextAlignmentOptions.Center).raycastTarget = false;
                return;
            }

            // 顶部表头
            var header = QdaoUguiFactory.CreateRect("TravelPassHeaderRow", _contentHost, 0f, 0f, viewW, 42f);
            Text(header, "档位 / 游历点", 18f, 4f, 210f, 34f, 24f, Ink).raycastTarget = false;
            Text(header, "免费游历轨（零门槛全员可领）", 248f, 4f, 690f, 34f, 24f, JadeAccent).raycastTarget = false;
            Text(header, "典藏进阶轨（180灵石 · 先玩后买 · 解锁永不过期 · 零战力突破）", 968f, 4f, 730f, 34f, 24f, GoldAccent).raycastTarget = false;

            float scrollTop = 46f;
            float cosmeticBarH = snap.UnlockedCosmeticKeys.Count > 0 ? 74f : 0f;
            float scrollH = viewH - scrollTop - cosmeticBarH;

            var viewport = QdaoUguiFactory.CreateRect("TravelPassViewport", _contentHost, 0f, scrollTop, viewW, scrollH);
            var maskImg = viewport.gameObject.AddComponent<Image>();
            maskImg.color = new Color(1f, 1f, 1f, 0.001f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = QdaoUguiFactory.CreateRect("TravelPassScrollContent", viewport, 0f, 0f, viewW, scrollH);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;

            const float rowH = 92f;
            const float rowGap = 10f;
            float y = 4f;
            bool busy = _client.AnyWritePending;
            bool inBattle = _client.InBattle;

            for (int i = 0; i < snap.Tiers.Count; ++i)
            {
                var tier = snap.Tiers[i];
                DrawTierRow(content, snap, tier, 6f, y, viewW - 12f, rowH, busy, inBattle);
                y += rowH + rowGap;
            }
            content.sizeDelta = new Vector2(viewW, Math.Max(scrollH, y + 6f));

            // 底部纪念外显佩戴栏（头像框/称谓，零战力纯外显，随时切换或卸下）
            if (cosmeticBarH > 0f)
            {
                DrawCosmeticBar(_contentHost, snap, 0f, scrollTop + scrollH + 8f, viewW, cosmeticBarH - 8f, busy, inBattle);
            }
        }

        private void DrawTierRow(
            RectTransform parent,
            global::TravelPassSnapshot snap,
            global::TravelPassTierView tier,
            float x,
            float y,
            float w,
            float h,
            bool busy,
            bool inBattle)
        {
            var rowBg = QdaoUguiFactory.CreateImage("TravelPassTier_" + tier.TierIndex, parent, x, y, w, h,
                LoadOptionalSkin(tier.Reached ? "tier_row_reached" : "tier_row_locked"));
            if (rowBg.sprite == null) rowBg.color = tier.Reached ? ReachedTint : LockedTint;
            rowBg.raycastTarget = false;

            // 左栏：档位与所需点数
            string tierBadge = $"第 {tier.TierIndex} 档\n{tier.RequiredPoints} 游历点";
            var badgeTxt = Text(rowBg.transform, tierBadge, 14f, 10f, 200f, h - 20f, 24f,
                tier.Reached ? JadeAccent : Muted, alignment: TextAlignmentOptions.Center, wrap: true);
            badgeTxt.fontStyle = tier.Reached ? FontStyles.Bold : FontStyles.Normal;
            badgeTxt.raycastTarget = false;

            // 中栏：免费轨
            float freeX = 228f, freeW = 716f;
            var freeBox = QdaoUguiFactory.CreateImage("FreeBox_" + tier.TierIndex, rowBg.transform, freeX, 8f, freeW, h - 16f, null);
            freeBox.color = new Color(1f, 0.99f, 0.95f, 0.85f);
            freeBox.raycastTarget = false;

            string freeSummary = TravelPassModels.FormatTrackReward(
                tier.FreeItemId, tier.FreeItemCount, tier.FreeItemName, tier.FreeGold,
                tier.FreeCosmeticKey, tier.FreeCosmeticName);
            Text(freeBox.transform, freeSummary, 16f, 10f, freeW - 178f, h - 36f, 22f, Ink, wrap: true).raycastTarget = false;

            uint capturedTier = tier.TierIndex;
            string freeBtnLabel = tier.FreeClaimed ? "已领取" : tier.FreeCanClaim ? "领取" : "未达成";
            var freeBtn = Button(freeBox.transform, freeBtnLabel, freeW - 154f, 14f, 140f, 48f,
                () => _client?.ClaimReward(capturedTier, false),
                primary: tier.FreeCanClaim, enabled: tier.FreeCanClaim && !busy && !inBattle, fontSize: 22f);
            freeBtn.name = $"ClaimFreeTier_{tier.TierIndex}";

            // 右栏：典藏轨
            float premX = 958f, premW = w - premX - 12f;
            var premBox = QdaoUguiFactory.CreateImage("PremiumBox_" + tier.TierIndex, rowBg.transform, premX, 8f, premW, h - 16f, null);
            premBox.color = snap.PremiumUnlocked ? PremiumTint : new Color(0.95f, 0.91f, 0.82f, 0.72f);
            premBox.raycastTarget = false;

            string premSummary = TravelPassModels.FormatTrackReward(
                tier.PremiumItemId, tier.PremiumItemCount, tier.PremiumItemName, tier.PremiumGold,
                tier.PremiumCosmeticKey, tier.PremiumCosmeticName);
            Text(premBox.transform, premSummary, 16f, 10f, premW - 178f, h - 36f, 22f, Ink, wrap: true).raycastTarget = false;

            string premBtnLabel = tier.PremiumClaimed ? "已领取"
                : tier.PremiumCanClaim ? "领取"
                : !snap.PremiumUnlocked ? (tier.Reached ? "解锁可领" : "典藏专属")
                : "未达成";
            var premBtn = Button(premBox.transform, premBtnLabel, premW - 154f, 14f, 140f, 48f,
                () => _client?.ClaimReward(capturedTier, true),
                primary: tier.PremiumCanClaim, enabled: tier.PremiumCanClaim && !busy && !inBattle, fontSize: 21f);
            premBtn.name = $"ClaimPremiumTier_{tier.TierIndex}";
        }

        private void DrawCosmeticBar(
            RectTransform parent,
            global::TravelPassSnapshot snap,
            float x,
            float y,
            float w,
            float h,
            bool busy,
            bool inBattle)
        {
            var bar = QdaoUguiFactory.CreateImage("TravelPassCosmeticBar", parent, x, y, w, h, null);
            bar.color = new Color(0.91f, 0.88f, 0.79f, 0.92f);
            bar.raycastTarget = false;

            Text(bar.transform, "游历纪念外显（零战力 · 免费切换）：", 18f, 14f, 380f, 38f, 22f, JadeAccent).raycastTarget = false;

            float cursorX = 406f;
            for (int i = 0; i < snap.UnlockedCosmeticKeys.Count; ++i)
            {
                string key = snap.UnlockedCosmeticKeys[i];
                uint slot = TravelPassModels.SlotOfCosmetic(key);
                if (slot == 0u) continue;
                bool equipped = (slot == TravelPassModels.CosmeticSlotFrame && string.Equals(snap.EquippedFrameKey, key, StringComparison.Ordinal))
                    || (slot == TravelPassModels.CosmeticSlotTitle && string.Equals(snap.EquippedTitleKey, key, StringComparison.Ordinal));
                string displayName = TravelPassModels.ResolveCosmeticName(snap, key);
                string btnText = equipped ? $"已佩戴：{displayName}（点此卸下）" : $"佩戴：{displayName}";
                float btnW = Math.Min(360f, Math.Max(220f, btnText.Length * 22f + 36f));
                if (cursorX + btnW > w - 16f) break;

                uint capturedSlot = slot;
                string nextKey = equipped ? string.Empty : key;
                var btn = Button(bar.transform, btnText, cursorX, 10f, btnW, 46f,
                    () => _client?.EquipCosmetic(capturedSlot, nextKey),
                    primary: equipped, enabled: !busy && !inBattle, fontSize: 20f);
                btn.name = "EquipCosmetic_" + key;
                cursorX += btnW + 14f;
            }
        }

        private void HandleClaimAllClicked() => _client?.ClaimReward(0u, false);

        private void OpenUnlockConfirmModal()
        {
            if (_client == null || _client.State == null) return;
            _confirmQuote = _client.CurrentUnlockQuote();
            if (!_confirmQuote.IsValid) return;
            _confirmOverlay.gameObject.SetActive(true);
            RefreshConfirmModalState();
        }

        private void CloseConfirmModal()
        {
            _confirmQuote = default;
            if (_confirmOverlay != null) _confirmOverlay.gameObject.SetActive(false);
        }

        private void RefreshConfirmModalState()
        {
            if (_confirmOverlay == null || !_confirmOverlay.gameObject.activeSelf) return;
            var snap = _client?.State;
            if (snap == null || !snap.Available || snap.PremiumUnlocked)
            {
                CloseConfirmModal();
                return;
            }

            ulong price = snap.PremiumPrice;
            ulong diamond = snap.DiamondBalance;
            bool shortBalance = diamond < price;
            uint retro = snap.RetroactivePremiumCount;

            _confirmTitle.text = $"解锁「{snap.Name}」典藏轨";
            _confirmBody.text =
                $"解锁费用：{price.ToString(CultureInfo.InvariantCulture)} 灵石（约 ¥{price / 10}，仅扣除充值灵石，当前持有 {diamond.ToString(CultureInfo.InvariantCulture)}）\n" +
                $"先玩后买：当前已达成 {snap.ProgressPoints}/{snap.MaxPoints} 游历点，解锁后可立即补领 {retro} 档典藏奖励。\n" +
                $"永不过期：解锁后即使活动窗口结束，已解锁手记的进度与领奖永久保留，无过期压力。";

            _confirmNotice.text = shortBalance
                ? "提示：当前充值灵石余额不足，请先点击「前往充值」补足灵石；\n全部典藏奖励均为绑定仅自用，0 战力上限突破，不破坏交易行与竞技公平。"
                : "公平承诺：全部典藏奖励均为绑定仅自用（不可挂集市或交易），0 战力上限突破。确认价格无误后点击「确认解锁典藏」。";

            if (_confirmRechargeButton != null)
                _confirmRechargeButton.gameObject.SetActive(shortBalance);
            if (_confirmUnlockButton != null)
                _confirmUnlockButton.interactable = !(_client?.AnyWritePending ?? false);
        }

        private void ExecuteConfirmUnlock()
        {
            if (_client == null || !_confirmQuote.IsValid) return;
            var quote = _confirmQuote;
            CloseConfirmModal();
            _client.UnlockPremium(quote);
        }

        private void HandleRechargeClicked()
        {
            Hide();
            _openRechargeAction?.Invoke();
        }

        private static Sprite LoadOptionalSkin(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            return Resources.Load<Sprite>(SkinRoot + relativePath.TrimStart('/'));
        }

        private static Image CreateSkinOrFallbackArt(RectTransform parent, string name, string skinRelPath,
            string fallbackGameplayKey, float x, float y, float w, float h)
        {
            Sprite sprite = LoadOptionalSkin(skinRelPath) ?? Load(fallbackGameplayKey);
            var img = QdaoUguiFactory.CreateImage(name, parent, x, y, w, h, sprite);
            img.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            img.pixelsPerUnitMultiplier = 1f;
            img.raycastTarget = false;
            return img;
        }

        private void ApplySafeArea()
        {
            if (_frame == null || Screen.width <= 0 || Screen.height <= 0) return;
            var safe = Screen.safeArea;
            float topPad = Mathf.Clamp((Screen.height - (safe.y + safe.height)) / Screen.height * 1080f, 0f, 60f);
            _frame.anchoredPosition = new Vector2(0f, -topPad * 0.5f);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Bind(null);
            Closed = null;
            if (_root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_root.gameObject);
                else UnityEngine.Object.DestroyImmediate(_root.gameObject);
            }
        }
    }
}
