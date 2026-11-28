using System;
using System.Collections.Generic;
using System.Globalization;
using MmorpgClient.Game.SpiritShop;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.SpiritShop
{
    /// <summary>
    /// 官方灵石百宝阁 V1 可交互 uGUI 窗口（docs/design/spirit-shop-and-starter-packs-v1.md §2.2 / §8）。
    ///
    /// 设计要点：
    /// - 零配表硬编码：所有商品名、描述、价格、限购与灵石/绑定灵石双余额均来自 <see cref="SpiritShopClient"/> 权威下发；
    /// - 皮肤槽位预留：优先尝试加载 Assets/Resources/UI/Ugui/SpiritShopV1/ 专属切片，缺省无缝回落至 GameplayUiArt；
    /// - 防误触闭环：所有购买均先进入二次确认浮层（显示绑定仅自用提示与单笔总额），不在列表卡片一点即扣灵石。
    /// </summary>
    public sealed class SpiritShopWindow : IDisposable
    {
        public const string SkinRoot = "UI/Ugui/SpiritShopV1/";
        public const string CloseButtonName = "SpiritShopClose";
        public const string RefreshButtonName = "SpiritShopRefresh";
        public const string RechargeButtonName = "SpiritShopRecharge";
        public const string AfkPassCardButtonName = "SpiritShopAfkPassCard";
        public const string ConfirmBuyButtonName = "SpiritShopConfirmBuy";
        public const string ConfirmCancelButtonName = "SpiritShopConfirmCancel";
        public const string ConfirmRechargeButtonName = "SpiritShopConfirmRecharge";

        private const float FrameWidth = 1860f;
        private const float FrameHeight = 940f;
        private const float ContentX = 56f;
        private const float ContentY = 238f;
        private const float ContentWidth = FrameWidth - 112f;
        private const float ContentHeight = 590f;

        private static readonly Color DangerInk = QdaoUguiTheme.Html("#A94735");
        private static readonly Color GoldAccent = QdaoUguiTheme.Html("#B8893A");
        private static readonly Color FocusTint = new Color(0.99f, 0.96f, 0.87f, 1f);
        private static readonly Color CardTint = new Color(0.97f, 0.94f, 0.86f, 0.96f);

        private readonly RectTransform _root, _frame, _contentHost, _confirmOverlay, _confirmChoiceHost;
        private readonly TMP_Text _diamondText, _bindDiamondText, _statusText;
        private readonly TMP_Text _confirmTitle, _confirmBody, _confirmNotice;
        private readonly Button _rechargeButton, _adSupplyButton, _confirmBuyButton, _confirmRechargeButton;
        private readonly Button[] _tabButtons = new Button[4];
        private readonly Dictionary<uint, uint> _selectedCounts = new();
        private readonly List<(TMP_Text label, string prefix, ulong endTimeMs)> _countdownLabels = new();

        private SpiritShopClient _client;
        private Action _openRechargeAction;
        private Action _openAfkPassAction;
        private uint _activeTab = SpiritShopModels.TabStarterPacks;
        private uint _focusedGoodsId;
        private uint _confirmGoodsId;
        private uint _confirmCount;
        private uint _confirmChosenIndex;
        private SpiritShopPurchaseQuote _confirmQuote;
        private float _lastCountdownTick = float.NegativeInfinity;
        private bool _disposed;

        public RectTransform Root => _root;
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;
        public uint ActiveTab => _activeTab;
        public uint FocusedGoodsId => _focusedGoodsId;

        public event Action Closed;

        public SpiritShopWindow(UnityEngine.Transform parent, Action openRechargeAction = null, Action openAfkPassAction = null)
        {
            _openRechargeAction = openRechargeAction;
            _openAfkPassAction = openAfkPassAction;

            _root = QdaoUguiFactory.CreateStretch("SpiritShopWindow", parent, Vector4.zero);
            _root.gameObject.SetActive(false);

            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(0.025f, 0.10f, 0.08f, 0.72f);
            shade.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();

            _frame = QdaoUguiFactory.CreateCenteredRect("SpiritShopFrame", _root, FrameWidth, FrameHeight);
            CreateSkinOrFallbackArt(_frame, "SpiritShopFrameBg", "ui/window_frame", "main_frame", 0f, 0f, FrameWidth, FrameHeight);

            bool hasDedicatedTitlePlate = LoadOptionalSkin("ui/title_plate") != null;
            var plaque = CreateSkinOrFallbackArt(_frame, "SpiritShopTitlePlaque", "ui/title_plate", "title_plate", 56f, -20f, 480f, 110f);
            // 重绘 ui/title_plate 为浅宣纸心匾额，须配深墨绿字保高对比；回退旧深青底时维持暖白字
            var title = Text(plaque.transform, "灵石百宝阁",
                hasDedicatedTitlePlate ? 76f : 32f,
                hasDedicatedTitlePlate ? 24f : 10f,
                hasDedicatedTitlePlate ? 328f : 416f,
                hasDedicatedTitlePlate ? 62f : 84f,
                hasDedicatedTitlePlate ? 38f : 46f,
                hasDedicatedTitlePlate ? Ink : Cream,
                alignment: TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            title.raycastTarget = false;

            // 双余额独立展示：严禁把绑定灵石混算成可购买额度（V-4）
            _diamondText = Text(_frame, "灵石：0", 570f, 30f, 360f, 44f, 30f, Ink);
            _diamondText.name = "SpiritShopDiamondBalance";
            _diamondText.raycastTarget = false;

            _bindDiamondText = Text(_frame, "绑定灵石：0（活动赠送，不可用于官方直售）", 570f, 76f, 720f, 36f, 22f, Muted);
            _bindDiamondText.name = "SpiritShopBindDiamondBalance";
            _bindDiamondText.raycastTarget = false;

            _adSupplyButton = Button(_frame, MmorpgClient.Game.RewardedAd.RewardedAdModel.EntryButtonText,
                990f, 24f, 316f, 48f, () =>
                {
                    RewardedAdUiRoot.Instance?.OpenConfirmForSlot(
                        MmorpgClient.Game.RewardedAd.RewardedAdModel.DefaultSlotId,
                        0u, string.Empty,
                        ok => { if (ok) _client?.RequestInfo(force: true); });
                }, primary: false, fontSize: 19f);
            _adSupplyButton.name = "SpiritShopRewardedAdSupply";
            RewardedAdUiRoot.AttachVideoBadge(_adSupplyButton.transform, 24f);
            _adSupplyButton.gameObject.SetActive(false);

            var rechargeSprite = LoadOptionalSkin("ui/btn_recharge_gold");
            _rechargeButton = rechargeSprite != null
                ? CreateOptionalButton(_frame, RechargeButtonName, "充值灵石", 1320f, 30f, 240f, 68f, rechargeSprite, HandleRechargeClicked, Ink, 28f)
                : Button(_frame, "充值灵石", 1320f, 30f, 240f, 68f, HandleRechargeClicked, primary: true, fontSize: 28f);
            _rechargeButton.name = RechargeButtonName;

            var refreshBtn = Button(_frame, "刷新", 1574f, 30f, 120f, 68f, () => _client?.RequestInfo(force: true), fontSize: 26f);
            refreshBtn.name = RefreshButtonName;

            var closeBtn = Button(_frame, "关闭", 1708f, 30f, 110f, 68f, Hide, fontSize: 26f);
            closeBtn.name = CloseButtonName;

            // 顶部分页：1 首套提速 / 2 精进配套 / 3 外观便利 / 4 节庆特惠（平时无限时商品自动隐藏）
            uint[] tabIds =
            {
                SpiritShopModels.TabStarterPacks,
                SpiritShopModels.TabAdvancement,
                SpiritShopModels.TabAppearanceAndConvenience,
                SpiritShopModels.TabFestival,
            };
            for (int i = 0; i < tabIds.Length; i++)
            {
                uint capturedTab = tabIds[i];
                var btn = Button(_frame, SpiritShopModels.TabName(capturedTab), 56f + i * 280f, 146f, 260f, 72f,
                    () => SelectTab(capturedTab), key: "tab_normal", fontSize: 30f);
                btn.name = "SpiritShopTab_" + capturedTab.ToString(CultureInfo.InvariantCulture);
                if (capturedTab == SpiritShopModels.TabFestival) btn.gameObject.SetActive(false);
                _tabButtons[i] = btn;
            }

            CreateSkinOrFallbackArt(_frame, "SpiritShopContentBg", "ui/content_panel", "content_panel",
                ContentX, ContentY, ContentWidth, ContentHeight);
            _contentHost = QdaoUguiFactory.CreateRect("SpiritShopContentHost", _frame,
                ContentX + 18f, ContentY + 18f, ContentWidth - 36f, ContentHeight - 36f);

            _statusText = Text(_frame, "", 60f, 848f, 1740f, 64f, 26f, Muted);
            _statusText.name = "SpiritShopStatus";
            _statusText.raycastTarget = false;

            // 二次确认浮层（支持 ALL 1..4 项确认与 CHOOSE_ONE 多选一单选交互）
            _confirmOverlay = QdaoUguiFactory.CreateStretch("SpiritShopConfirmOverlay", _root, Vector4.zero);
            _confirmOverlay.gameObject.SetActive(false);
            var modalDim = _confirmOverlay.gameObject.AddComponent<Image>();
            modalDim.color = new Color(0.02f, 0.06f, 0.05f, 0.72f);
            modalDim.raycastTarget = true;

            var modalFrame = QdaoUguiFactory.CreateCenteredRect("SpiritShopConfirmFrame", _confirmOverlay, 960f, 620f);
            CreateSkinOrFallbackArt(modalFrame, "SpiritShopConfirmBg", "ui/window_frame", "main_frame", 0f, 0f, 960f, 620f);

            _confirmTitle = Text(modalFrame, "确认购买", 56f, 32f, 848f, 52f, 36f, Ink, alignment: TextAlignmentOptions.Center);
            _confirmTitle.raycastTarget = false;

            _confirmBody = Text(modalFrame, "", 64f, 96f, 832f, 160f, 26f, Ink, wrap: true);
            _confirmBody.raycastTarget = false;

            _confirmChoiceHost = QdaoUguiFactory.CreateRect("SpiritShopConfirmChoices", modalFrame, 64f, 258f, 832f, 164f);
            _confirmChoiceHost.gameObject.SetActive(false);

            _confirmNotice = Text(modalFrame, "", 64f, 430f, 832f, 76f, 23f, DangerInk, wrap: true);
            _confirmNotice.raycastTarget = false;

            var cancelBtn = Button(modalFrame, "取消", 64f, 518f, 230f, 74f, CloseConfirmModal, fontSize: 28f);
            cancelBtn.name = ConfirmCancelButtonName;

            Action confirmRechargeClick = () =>
            {
                CloseConfirmModal();
                HandleRechargeClicked();
            };
            _confirmRechargeButton = rechargeSprite != null
                ? CreateOptionalButton(modalFrame, ConfirmRechargeButtonName, "前往充值", 320f, 518f, 250f, 74f,
                    rechargeSprite, confirmRechargeClick, Ink, 26f)
                : Button(modalFrame, "前往充值", 320f, 518f, 250f, 74f, confirmRechargeClick, fontSize: 26f);
            _confirmRechargeButton.name = ConfirmRechargeButtonName;

            var confirmBuySprite = LoadOptionalSkin("ui/btn_buy_primary");
            _confirmBuyButton = confirmBuySprite != null
                ? CreateOptionalButton(modalFrame, ConfirmBuyButtonName, "确认购买", 596f, 518f, 300f, 74f,
                    confirmBuySprite, ExecuteConfirmBuy, Cream, 28f)
                : Button(modalFrame, "确认购买", 596f, 518f, 300f, 74f, ExecuteConfirmBuy, primary: true, fontSize: 28f);
            _confirmBuyButton.name = ConfirmBuyButtonName;
        }

        public void SetCallbacks(Action openRechargeAction, Action openAfkPassAction)
        {
            _openRechargeAction = openRechargeAction;
            _openAfkPassAction = openAfkPassAction;
        }

        public void Bind(SpiritShopClient client)
        {
            if (_client == client) return;
            if (_client != null) _client.Changed -= HandleClientChanged;
            _client = client;
            if (_client != null) _client.Changed += HandleClientChanged;
            if (IsVisible) Render();
        }

        public void Show(uint tabId = SpiritShopModels.TabStarterPacks, uint focusGoodsId = 0)
        {
            if (_root == null) return;
            ApplySafeArea();
            if (tabId == SpiritShopModels.TabStarterPacks
                || tabId == SpiritShopModels.TabAdvancement
                || tabId == SpiritShopModels.TabAppearanceAndConvenience
                || tabId == SpiritShopModels.TabFestival)
                _activeTab = tabId;
            _focusedGoodsId = focusGoodsId;
            CloseConfirmModal();
            _root.gameObject.SetActive(true);
            _client?.RequestInfo(force: true);
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

        public void SelectTab(uint tabId)
        {
            _activeTab = tabId;
            _focusedGoodsId = 0;
            CloseConfirmModal();
            Render();
        }

        public void TickCountdown(float unscaledTime)
        {
            if (_disposed || !IsVisible || _client == null || _countdownLabels.Count == 0) return;
            if (unscaledTime - _lastCountdownTick < 1f) return;
            _lastCountdownTick = unscaledTime;
            ulong serverNow = _client.EstimatedServerNowMs;
            for (int i = 0; i < _countdownLabels.Count; i++)
            {
                var entry = _countdownLabels[i];
                if (entry.label == null) continue;
                string timePart = SpiritShopModels.FormatRemainingTime(entry.endTimeMs, serverNow);
                entry.label.text = string.IsNullOrEmpty(timePart)
                    ? entry.prefix
                    : $"{entry.prefix}  ·  {timePart}";
            }
        }

        public void Render()
        {
            if (_disposed || _root == null || !_root.gameObject.activeSelf) return;

            var info = _client?.Info;
            if (_activeTab == SpiritShopModels.TabFestival
                && info != null
                && !SpiritShopModels.HasTabGoods(info, SpiritShopModels.TabFestival))
            {
                _activeTab = SpiritShopModels.TabStarterPacks;
            }
            _diamondText.text = $"灵石：{(info?.DiamondBalance ?? 0).ToString(CultureInfo.InvariantCulture)}";
            _bindDiamondText.text = $"绑定灵石：{(info?.BindDiamondBalance ?? 0).ToString(CultureInfo.InvariantCulture)}（仅活动赠送，不抵扣直售商品）";
            if (_rechargeButton != null)
                _rechargeButton.gameObject.SetActive(MmorpgClient.Game.RewardedAd.ClientDistributionPolicy.Current.IapEnabled);
            if (_adSupplyButton != null)
            {
                var adRoot = RewardedAdUiRoot.Instance;
                _adSupplyButton.gameObject.SetActive(adRoot != null
                    && adRoot.CanShowSlot(MmorpgClient.Game.RewardedAd.RewardedAdModel.DefaultSlotId, out _));
            }

            UpdateTabVisuals(info);
            UpdateStatusLine();
            RebuildBody(info);
            RefreshConfirmModalState();
        }

        private void HandleClientChanged()
        {
            if (_disposed || _root == null || !_root.gameObject.activeSelf) return;
            Render();
        }

        private void UpdateTabVisuals(global::SpiritShopInfo info)
        {
            uint[] tabIds =
            {
                SpiritShopModels.TabStarterPacks,
                SpiritShopModels.TabAdvancement,
                SpiritShopModels.TabAppearanceAndConvenience,
                SpiritShopModels.TabFestival,
            };
            bool hasFestival = SpiritShopModels.HasTabGoods(info, SpiritShopModels.TabFestival);
            Sprite activeSprite = LoadOptionalSkin("ui/tab_active") ?? Load("tab_selected");
            Sprite normalSprite = LoadOptionalSkin("ui/tab_inactive") ?? Load("tab_normal");
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                var btn = _tabButtons[i];
                if (btn == null) continue;
                if (tabIds[i] == SpiritShopModels.TabFestival)
                {
                    btn.gameObject.SetActive(hasFestival);
                    if (!hasFestival) continue;
                }
                bool selected = tabIds[i] == _activeTab;
                var img = btn.GetComponent<Image>();
                if (img != null)
                {
                    img.sprite = selected ? activeSprite : normalSprite;
                    img.type = img.sprite != null && img.sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
                }
                var lbl = btn.GetComponentInChildren<TMP_Text>(true);
                if (lbl != null)
                {
                    lbl.color = selected ? Cream : Ink;
                    lbl.raycastTarget = false;
                }
            }
        }

        private void UpdateStatusLine()
        {
            string msg = _client?.Status ?? string.Empty;
            bool isErr = _client?.StatusIsError ?? false;
            if (string.IsNullOrEmpty(msg) && _client != null && _client.InBattle)
            {
                msg = "战斗或观战期间仅可浏览货架，无法购买商品。";
                isErr = false;
            }
            _statusText.text = msg;
            _statusText.color = isErr ? DangerInk : Muted;
        }

        private void RebuildBody(global::SpiritShopInfo info)
        {
            _countdownLabels.Clear();
            Clear(_contentHost);
            float viewW = ContentWidth - 36f;
            float viewH = ContentHeight - 36f;

            if (_client == null || !_client.IsSupported)
            {
                var msg = Text(_contentHost, SpiritShopClient.UnsupportedMessage, 40f, 120f, viewW - 80f, 100f,
                    32f, Muted, alignment: TextAlignmentOptions.Center);
                msg.raycastTarget = false;
                return;
            }
            if (info == null)
            {
                string tip = _client.InfoPending ? SpiritShopClient.InfoPendingMessage : "正在等待百宝阁货架同步…";
                var msg = Text(_contentHost, tip, 40f, 120f, viewW - 80f, 100f, 32f, Muted, alignment: TextAlignmentOptions.Center);
                msg.raycastTarget = false;
                return;
            }

            // 创建 ScrollRect 容器支持后续配表热更上架任意多SKU
            var viewport = QdaoUguiFactory.CreateRect("SpiritShopViewport", _contentHost, 0f, 0f, viewW, viewH);
            var maskImg = viewport.gameObject.AddComponent<Image>();
            maskImg.color = new Color(1f, 1f, 1f, 0.001f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = QdaoUguiFactory.CreateRect("SpiritShopScrollContent", viewport, 0f, 0f, viewW, viewH);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;

            float y = 6f;

            // 分页 1：显示首套提速终身 300 份共享组进度条
            if (_activeTab == SpiritShopModels.TabStarterPacks)
            {
                var group = FindPrimaryLimitGroup(info, SpiritShopModels.StarterLimitGroupId);
                if (group != null)
                {
                    DrawQuotaBanner(content, group, 8f, y, viewW - 16f);
                    y += 108f;
                }
            }

            // 分页 3：置顶仙途轻享卡（月卡）聚合卡片，直通现有 AfkPassUiRoot（V2.2 口径）
            if (_activeTab == SpiritShopModels.TabAppearanceAndConvenience)
            {
                DrawAfkPassAggregateCard(content, 8f, y, viewW - 16f);
                y += 126f;
                if (MmorpgClient.Game.RewardedAd.ClientDistributionPolicy.Current.SteamDlcEnabled)
                {
                    DrawFounderSupporterBannerCard(content, 8f, y, viewW - 16f);
                    y += 174f;
                }
            }

            var items = SpiritShopModels.CollectGoodsForTab(info, _activeTab);
            if (items.Count == 0)
            {
                var empty = Text(content, "本分页当前暂无在售商品。", 40f, y + 60f, viewW - 80f, 80f,
                    30f, Muted, alignment: TextAlignmentOptions.Center);
                empty.raycastTarget = false;
                content.sizeDelta = new Vector2(viewW, Math.Max(viewH, y + 180f));
                return;
            }

            const float cardHeight = 166f;
            const float cardGap = 14f;
            for (int i = 0; i < items.Count; i++)
            {
                var g = items[i];
                var group = SpiritShopModels.FindLimitGroup(info, g.LimitGroupId);
                DrawGoodsCard(content, info, g, group, 8f, y, viewW - 16f, cardHeight);
                y += cardHeight + cardGap;
            }
            content.sizeDelta = new Vector2(viewW, Math.Max(viewH, y + 8f));
        }

        private static global::SpiritShopLimitGroupView FindPrimaryLimitGroup(global::SpiritShopInfo info, uint preferredGroupId)
        {
            var found = SpiritShopModels.FindLimitGroup(info, preferredGroupId);
            if (found != null) return found;
            return info?.LimitGroups != null && info.LimitGroups.Count > 0 ? info.LimitGroups[0] : null;
        }

        private void DrawQuotaBanner(RectTransform parent, global::SpiritShopLimitGroupView group, float x, float y, float w)
        {
            var banner = QdaoUguiFactory.CreateImage("SpiritShopQuotaBanner", parent, x, y, w, 92f, null);
            banner.color = new Color(0.92f, 0.88f, 0.77f, 0.9f);
            banner.raycastTarget = false;

            uint rem = SpiritShopModels.RemainingGroupQuota(group);
            string cycle = group.ResetKind == 1 ? "本周" : "终身";
            var title = Text(banner.transform,
                $"{group.Name}（{cycle}共享封顶）：已购 {group.UsedWeight} / {group.MaxWeight} 份 · 还可购 {rem} 份（可自由拼单）",
                20f, 10f, w - 40f, 34f, 24f, Ink);
            title.raycastTarget = false;

            float barX = 20f, barY = 52f, barW = w - 40f, barH = 24f;
            Sprite barBg = LoadOptionalSkin("ui/quota_bar_bg");
            var bgImg = QdaoUguiFactory.CreateImage("QuotaBarBg", banner.transform, barX, barY, barW, barH, barBg);
            if (barBg != null && barBg.border.sqrMagnitude > 0f) bgImg.type = Image.Type.Sliced;
            else if (barBg == null) bgImg.color = new Color(0.78f, 0.74f, 0.64f, 1f);
            bgImg.raycastTarget = false;

            float ratio = group.MaxWeight > 0 ? Mathf.Clamp01((float)group.UsedWeight / group.MaxWeight) : 0f;
            if (ratio > 0.001f)
            {
                Sprite barFill = LoadOptionalSkin("ui/quota_bar_fill");
                var fillImg = QdaoUguiFactory.CreateImage("QuotaBarFill", banner.transform, barX + 2f, barY + 2f,
                    Math.Max(32f, (barW - 4f) * ratio), barH - 4f, barFill);
                if (barFill != null && barFill.border.sqrMagnitude > 0f) fillImg.type = Image.Type.Sliced;
                else if (barFill == null) fillImg.color = new Color(0.20f, 0.56f, 0.42f, 1f);
                fillImg.raycastTarget = false;
            }
        }

        private void DrawAfkPassAggregateCard(RectTransform parent, float x, float y, float w)
        {
            Sprite customBg = LoadOptionalSkin("ui/card_bg_featured");
            Sprite bgSprite = customBg ?? Load("pet_card_normal");
            var card = QdaoUguiFactory.CreateImage("SpiritShopAfkPassBanner", parent, x, y, w, 112f, bgSprite);
            card.color = customBg != null ? Color.white : FocusTint;
            if (bgSprite != null && bgSprite.border.sqrMagnitude > 0f) card.type = Image.Type.Sliced;
            card.raycastTarget = false;

            var nameTxt = Text(card.transform, "仙途轻享卡（挂机月卡） · 300 灵石", 24f, 14f, w - 340f, 40f, 28f, Ink);
            nameTxt.fontStyle = FontStyles.Bold;
            nameTxt.raycastTarget = false;

            var descTxt = Text(card.transform, "断线原地离线托管不掉线 + 阶段二每场胜局额外解锁 1 份可交易精进材料（沿用 180 天上限）",
                24f, 58f, w - 340f, 42f, 22f, Muted);
            descTxt.raycastTarget = false;

            var openBtn = Button(card.transform, "前往月卡详情", w - 286f, 22f, 260f, 68f, HandleOpenAfkPassClicked,
                primary: true, enabled: true, fontSize: 25f);
            openBtn.name = AfkPassCardButtonName;
        }

        private void DrawFounderSupporterBannerCard(RectTransform parent, float x, float y, float w)
        {
            const float h = 160f;
            Sprite bannerBg = AppearanceUiArt.LoadFounderSupporterBanner();
            Sprite bgSprite = bannerBg ?? LoadOptionalSkin("ui/card_bg_featured") ?? Load("pet_card_normal");
            var card = QdaoUguiFactory.CreateImage("SpiritShopFounderSupporterBanner", parent, x, y, w, h, bgSprite);
            card.color = bannerBg != null ? Color.white : FocusTint;
            card.preserveAspect = false;
            if (bannerBg == null && bgSprite != null && bgSprite.border.sqrMagnitude > 0f) card.type = Image.Type.Sliced;
            card.raycastTarget = false;

            float textX = bannerBg != null ? w * 0.14f : 24f;
            float textW = bannerBg != null ? w * 0.45f : w - 340f;
            var nameTxt = Text(card.transform, "留灯院创始典藏包 · Steam DLC", textX, 34f, textW, 40f, 27f, Ink);
            nameTxt.fontStyle = FontStyles.Bold;
            nameTxt.raycastTarget = false;

            var descTxt = Text(card.transform, "含永久限定「星河初启阵纹」等纯视觉纪念收藏（零战力加成，Steam 商店激活自动到账）",
                textX, 78f, textW, 48f, 21f, Muted, wrap: true);
            descTxt.raycastTarget = false;

            var previewBtn = Button(card.transform, "衣柜预览阵纹", w - 286f, 46f, 250f, 68f, () =>
            {
                Hide();
                AppearanceUiRoot.Instance?.ShowPanel();
            }, primary: false, enabled: true, fontSize: 23f);
            previewBtn.name = "SpiritShopFounderPreviewBtn";
        }

        private void DrawGoodsCard(
            RectTransform parent,
            global::SpiritShopInfo info,
            global::SpiritShopGoodsView goods,
            global::SpiritShopLimitGroupView group,
            float x,
            float y,
            float w,
            float h)
        {
            bool focused = goods.GoodsId == _focusedGoodsId;
            bool featured = focused || goods.TabId == SpiritShopModels.TabFestival;
            Sprite customBg = LoadOptionalSkin(featured ? "ui/card_bg_featured" : "ui/card_bg_normal");
            Sprite bgSprite = customBg ?? Load("pet_card_normal");
            var card = QdaoUguiFactory.CreateImage("SpiritShopCard_" + goods.GoodsId.ToString(CultureInfo.InvariantCulture),
                parent, x, y, w, h, bgSprite);
            card.color = customBg != null ? Color.white : (featured ? FocusTint : CardTint);
            if (bgSprite != null && bgSprite.border.sqrMagnitude > 0f) card.type = Image.Type.Sliced;
            card.raycastTarget = false;

            var iconSprite = ResolveGoodsIcon(goods);
            var iconImg = QdaoUguiFactory.CreateImage("Icon", card.transform, 20f, 27f, 112f, 112f, iconSprite);
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            float textX = 152f;
            float textW = w - 620f;
            string displayName = string.IsNullOrWhiteSpace(goods.Name) ? $"商品#{goods.GoodsId}" : goods.Name;
            if (!string.IsNullOrWhiteSpace(goods.BadgeText))
                displayName += $"  [{goods.BadgeText}]";
            else if (focused)
                displayName += "  [当前急需]";
            string discountTag = SpiritShopModels.ComputeDiscountLabel(goods.UnitPrice, goods.OriginalPrice);
            if (!string.IsNullOrEmpty(discountTag) && !displayName.Contains("折", StringComparison.Ordinal))
                displayName += $"  [{discountTag}]";

            var title = Text(card.transform, displayName, textX, 12f, textW, 38f, 28f, featured ? GoldAccent : Ink);
            title.fontStyle = FontStyles.Bold;
            title.raycastTarget = false;

            uint qty = GetSelectedCount(goods, group);
            bool denseItems = (goods.Items != null && goods.Items.Count >= 3) || SpiritShopModels.IsChooseOne(goods);
            string itemsLine = SpiritShopModels.FormatItemsSummary(goods, qty);
            var itemsTxt = Text(card.transform, itemsLine, textX, 50f, textW, denseItems ? 48f : 34f,
                denseItems ? 20f : 23f, Ink, wrap: denseItems);
            itemsTxt.raycastTarget = false;

            string desc = string.IsNullOrWhiteSpace(goods.Description) ? "购买后全部物品均为绑定仅自用。" : goods.Description;
            var descTxt = Text(card.transform, desc, textX, denseItems ? 100f : 92f, textW, denseItems ? 26f : 32f,
                denseItems ? 18f : 20f, Muted);
            descTxt.raycastTarget = false;

            string limitStr = SpiritShopModels.LimitLabel(goods, group);
            string timeStr = goods.EndTimeMs > 0
                ? SpiritShopModels.FormatRemainingTime(goods.EndTimeMs, _client?.EstimatedServerNowMs ?? 0)
                : string.Empty;
            string metaLine = string.IsNullOrEmpty(timeStr) ? limitStr : $"{limitStr}  ·  {timeStr}";
            var limitTxt = Text(card.transform, metaLine, textX, denseItems ? 130f : 126f, textW, 28f,
                denseItems ? 19f : 20f, goods.EndTimeMs > 0 ? DangerInk : Muted);
            limitTxt.raycastTarget = false;
            if (goods.EndTimeMs > 0)
            {
                _countdownLabels.Add((limitTxt, limitStr, goods.EndTimeMs));
            }

            uint maxBuy = SpiritShopModels.MaxBuyableCount(goods, group);
            bool inBattle = _client?.InBattle ?? false;
            bool blockedQuota = maxBuy == 0;
            bool busy = _client != null && _client.BuyPending;

            // 多份步进器（不限购或单次允许 > 1 时显示 - / qty / +）
            if (!blockedQuota && maxBuy > 1)
            {
                var minus = Button(card.transform, "-", w - 456f, 52f, 56f, 58f, () => StepCount(goods, group, -1),
                    enabled: qty > 1 && !busy, fontSize: 28f);
                minus.name = "SpiritShopMinus_" + goods.GoodsId;

                var countLbl = Text(card.transform, qty.ToString(CultureInfo.InvariantCulture), w - 394f, 52f, 64f, 58f,
                    28f, Ink, alignment: TextAlignmentOptions.Center);
                countLbl.raycastTarget = false;

                var plus = Button(card.transform, "+", w - 324f, 52f, 56f, 58f, () => StepCount(goods, group, 1),
                    enabled: qty < maxBuy && !busy, fontSize: 28f);
                plus.name = "SpiritShopPlus_" + goods.GoodsId;
            }

            ulong totalCost = goods.UnitPrice * Math.Max(1u, qty);
            string priceStr = goods.OriginalPrice > goods.UnitPrice
                ? $"<size=19><color=#7A7265><s>{(goods.OriginalPrice * Math.Max(1u, qty)).ToString(CultureInfo.InvariantCulture)}</s></color></size>  {totalCost.ToString(CultureInfo.InvariantCulture)} {SpiritShopModels.CurrencyName(goods.CurrencyType)}"
                : $"{totalCost.ToString(CultureInfo.InvariantCulture)} {SpiritShopModels.CurrencyName(goods.CurrencyType)}";
            var priceLbl = Text(card.transform, priceStr, w - 274f, 18f, 260f, 34f, 24f, GoldAccent,
                alignment: TextAlignmentOptions.Center);
            priceLbl.fontStyle = FontStyles.Bold;
            priceLbl.raycastTarget = false;

            var block = SpiritShopModels.EvaluateBuy(goods, group, Math.Max(1u, qty), _client?.Ready ?? false, inBattle,
                _client?.EstimatedServerNowMs ?? 0);
            bool canOpenConfirm = block == SpiritShopBuyBlock.None && !busy && !(_client?.RequiresReconnect ?? false);
            string btnText = busy && _client.BuyingGoodsId == goods.GoodsId ? "购买中…"
                : block == SpiritShopBuyBlock.SaleEnded ? "已结束"
                : block == SpiritShopBuyBlock.GroupLimitReached || block == SpiritShopBuyBlock.LimitReached ? "已达上限"
                : block == SpiritShopBuyBlock.InBattle ? "战斗中"
                : SpiritShopModels.IsChooseOne(goods) ? "自选购买"
                : "购买";

            Sprite buyBg = LoadOptionalSkin("ui/btn_buy_primary");
            var buyBtn = buyBg != null
                ? CreateOptionalButton(card.transform, "SpiritShopBuy_" + goods.GoodsId, btnText,
                    w - 252f, 64f, 224f, 76f, buyBg, () => OpenConfirmModal(goods, Math.Max(1u, qty)), Cream, 28f, canOpenConfirm)
                : Button(card.transform, btnText, w - 252f, 64f, 224f, 76f,
                    () => OpenConfirmModal(goods, Math.Max(1u, qty)), primary: true, enabled: canOpenConfirm, fontSize: 28f);
            buyBtn.name = "SpiritShopBuy_" + goods.GoodsId.ToString(CultureInfo.InvariantCulture);
        }

        private uint GetSelectedCount(global::SpiritShopGoodsView goods, global::SpiritShopLimitGroupView group)
        {
            uint maxBuy = SpiritShopModels.MaxBuyableCount(goods, group);
            if (maxBuy == 0) return 1;
            if (_selectedCounts.TryGetValue(goods.GoodsId, out uint stored))
            {
                uint clamped = Math.Min(Math.Max(1u, stored), maxBuy);
                _selectedCounts[goods.GoodsId] = clamped;
                return clamped;
            }
            return 1;
        }

        private void StepCount(global::SpiritShopGoodsView goods, global::SpiritShopLimitGroupView group, int delta)
        {
            uint maxBuy = Math.Max(1u, SpiritShopModels.MaxBuyableCount(goods, group));
            uint current = GetSelectedCount(goods, group);
            long next = (long)current + delta;
            uint clamped = (uint)Math.Max(1L, Math.Min(next, maxBuy));
            _selectedCounts[goods.GoodsId] = clamped;
            Render();
        }

        private void OpenConfirmModal(global::SpiritShopGoodsView goods, uint count)
        {
            if (goods == null || _confirmOverlay == null) return;
            var quote = SpiritShopPurchaseQuote.Of(goods, 0u);
            if (!quote.IsValid) return;

            _confirmGoodsId = goods.GoodsId;
            _confirmCount = count;
            _confirmChosenIndex = 0u;
            _confirmQuote = quote;
            _confirmOverlay.gameObject.SetActive(true);
            RefreshConfirmModalState();
        }

        private void CloseConfirmModal()
        {
            _confirmGoodsId = 0;
            _confirmCount = 0;
            _confirmChosenIndex = 0;
            _confirmQuote = default;
            if (_confirmChoiceHost != null)
            {
                Clear(_confirmChoiceHost);
                _confirmChoiceHost.gameObject.SetActive(false);
            }
            if (_confirmOverlay != null) _confirmOverlay.gameObject.SetActive(false);
        }

        private void SelectConfirmChoice(global::SpiritShopGoodsView goods, uint index)
        {
            if (goods == null || !SpiritShopModels.IsChooseOne(goods)) return;
            var nextQuote = SpiritShopPurchaseQuote.Of(goods, index);
            if (!nextQuote.IsValid) return;
            _confirmChosenIndex = index;
            _confirmQuote = nextQuote;
            RefreshConfirmModalState();
        }

        private void RefreshConfirmModalState()
        {
            if (_confirmOverlay == null || !_confirmOverlay.gameObject.activeSelf) return;
            var goods = _client?.FindGoods(_confirmGoodsId);
            if (goods == null)
            {
                CloseConfirmModal();
                return;
            }
            bool chooseOne = SpiritShopModels.IsChooseOne(goods);
            if (chooseOne && _confirmChosenIndex >= (uint)goods.Items.Count)
            {
                _confirmChosenIndex = 0u;
            }
            ulong totalCost = goods.UnitPrice * _confirmCount;
            string currencyName = SpiritShopModels.CurrencyName(goods.CurrencyType);
            string itemsSummary = chooseOne
                ? SpiritShopModels.FormatItemsSummary(goods, _confirmCount, (int)_confirmChosenIndex)
                : SpiritShopModels.FormatItemsSummary(goods, _confirmCount);
            ulong balance = goods.CurrencyType == SpiritShopModels.CurrencyDiamond ? (_client?.DiamondBalance ?? 0) : 0;
            bool shortBalance = goods.CurrencyType == SpiritShopModels.CurrencyDiamond && balance < totalCost;

            string priceSummary = goods.OriginalPrice > goods.UnitPrice
                ? $"{totalCost.ToString(CultureInfo.InvariantCulture)} {currencyName}（原价 <s>{(goods.OriginalPrice * _confirmCount).ToString(CultureInfo.InvariantCulture)}</s> · 当前持有：{balance.ToString(CultureInfo.InvariantCulture)}）"
                : $"{totalCost.ToString(CultureInfo.InvariantCulture)} {currencyName}（当前持有：{balance.ToString(CultureInfo.InvariantCulture)}）";

            _confirmTitle.text = chooseOne ? $"自选购买「{goods.Name}」" : $"确认购买「{goods.Name}」";
            _confirmBody.text =
                $"购买份数：{_confirmCount} 份\n" +
                (chooseOne ? $"当前已选：{itemsSummary}\n" : $"发放内容：{itemsSummary}\n") +
                $"合计消耗：{priceSummary}";

            if (_confirmChoiceHost != null)
            {
                Clear(_confirmChoiceHost);
                _confirmChoiceHost.gameObject.SetActive(chooseOne);
                if (chooseOne)
                {
                    var hint = Text(_confirmChoiceHost, "请任选 1 项奖励（买后直接发放绑定成品）：", 0f, 0f, 832f, 26f, 21f, Muted);
                    hint.raycastTarget = false;
                    Sprite activeSprite = LoadOptionalSkin("ui/tab_active") ?? Load("tab_selected");
                    Sprite normalSprite = LoadOptionalSkin("ui/tab_inactive") ?? Load("tab_normal");
                    for (int i = 0; i < goods.Items.Count; i++)
                    {
                        uint capturedIdx = (uint)i;
                        bool selected = capturedIdx == _confirmChosenIndex;
                        int col = i % 2;
                        int row = i / 2;
                        string label = (selected ? "✓ " : "") + SpiritShopModels.FormatSingleItem(goods.Items[i], _confirmCount);
                        var choiceBtn = CreateOptionalButton(
                            _confirmChoiceHost,
                            "SpiritShopChoice_" + capturedIdx.ToString(CultureInfo.InvariantCulture),
                            label,
                            col * 424f,
                            30f + row * 68f,
                            408f,
                            58f,
                            selected ? activeSprite : normalSprite,
                            () => SelectConfirmChoice(goods, capturedIdx),
                            selected ? Cream : Ink,
                            21f,
                            enabled: !(_client?.BuyPending ?? false));
                        choiceBtn.name = "SpiritShopChoice_" + capturedIdx.ToString(CultureInfo.InvariantCulture);
                    }
                }
            }

            _confirmNotice.text = shortBalance
                ? "提示：当前灵石余额不足，请先点击「前往充值」补足灵石；\n全部百宝阁道具均为永久绑定仅自用（不可挂集市或交易）。"
                : "安全提示：所购物品全部为绑定仅自用，确认价格与内容无误后点击「确认购买」。";

            if (_confirmRechargeButton != null)
                _confirmRechargeButton.gameObject.SetActive(shortBalance && MmorpgClient.Game.RewardedAd.ClientDistributionPolicy.Current.IapEnabled);
            if (_confirmBuyButton != null)
                _confirmBuyButton.interactable = !(_client?.BuyPending ?? false);
        }

        private void ExecuteConfirmBuy()
        {
            if (_client == null || !_confirmQuote.IsValid || _confirmCount == 0) return;
            var quote = _confirmQuote;
            uint count = _confirmCount;
            CloseConfirmModal();
            _client.BuyGoods(quote, count);
        }

        private void HandleRechargeClicked()
        {
            Hide();
            _openRechargeAction?.Invoke();
        }

        private void HandleOpenAfkPassClicked()
        {
            Hide();
            _openAfkPassAction?.Invoke();
        }

        private Sprite ResolveGoodsIcon(global::SpiritShopGoodsView goods)
        {
            if (goods != null && !string.IsNullOrWhiteSpace(goods.IconKey) && !goods.IconKey.Contains(".."))
            {
                var custom = LoadOptionalSkin("icons/" + goods.IconKey) ?? LoadOptionalSkin(goods.IconKey)
                    ?? QdaoUguiTheme.LoadSprite(goods.IconKey);
                if (custom != null) return custom;
            }
            if (goods != null && goods.TabId == SpiritShopModels.TabFestival)
            {
                var festivalFallback = SpiritShopModels.IsChooseOne(goods)
                    ? (LoadOptionalSkin("pack_festival_choice") ?? SeasonEconomyUiArt.LoadCraftIcon("item_celestial_ink"))
                    : (LoadOptionalSkin("pack_festival_bundle") ?? LoadOptionalSkin("pack_starter_300"));
                if (festivalFallback != null) return festivalFallback;
            }
            uint firstItemId = goods?.Items != null && goods.Items.Count > 0 ? goods.Items[0].ItemConfigId : 0u;
            if (firstItemId == SpiritShopModels.ItemStarGuaranteeCharm)
            {
                var charm = SeasonEconomyUiArt.LoadCraftIcon("item_star_guarantee_charm");
                if (charm != null) return charm;
            }
            if (firstItemId == SpiritShopModels.ItemCelestialInk)
            {
                var ink = SeasonEconomyUiArt.LoadCraftIcon("item_celestial_ink");
                if (ink != null) return ink;
            }
            if (firstItemId == SpiritShopModels.ItemAppearanceCard)
            {
                var card = QdaoUguiTheme.LoadSprite("UI/Ugui/AppearanceV1/item_appearance_card")
                    ?? QdaoUguiTheme.LoadSprite("UI/Ugui/AppearanceChronicleV2/item_appearance_card")
                    ?? QdaoUguiTheme.LoadSprite("UI/Icons/item_appearance_card");
                if (card != null) return card;
            }
            if (firstItemId == SpiritShopModels.ItemRenameCard)
            {
                var rename = QdaoUguiTheme.LoadSprite("UI/Icons/item_rename_card");
                if (rename != null) return rename;
            }
            if (firstItemId == SpiritShopModels.ItemPetMorphCard)
            {
                var morph = QdaoUguiTheme.LoadSprite("UI/Ugui/WardrobeCosmetics/item_pet_morph_charm");
                if (morph != null) return morph;
            }
            if (firstItemId == SpiritShopModels.ItemSkillVfxCard)
            {
                var vfx = QdaoUguiTheme.LoadSprite("UI/Ugui/WardrobeCosmetics/item_skill_vfx_scroll");
                if (vfx != null) return vfx;
            }
            if (firstItemId == SpiritShopModels.ItemShowcaseVoucher)
            {
                var voucher = AppearanceUiArt.LoadShowcaseVoucherIcon();
                if (voucher != null) return voucher;
            }
            return Load("icon_scroll");
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

        private static Button CreateOptionalButton(UnityEngine.Transform parent, string name, string label,
            float x, float y, float w, float h, Sprite bg, Action click, Color ink, float fontSize, bool enabled = true)
        {
            var btn = QdaoUguiFactory.CreateArtButton(name, parent, x, y, w, h, bg, out var image);
            if (bg != null && bg.border.sqrMagnitude > 0f) image.type = Image.Type.Sliced;
            btn.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            btn.interactable = enabled;
            if (click != null) btn.onClick.AddListener(() => click());
            var txt = Text(btn.transform, label, 16f, 0f, w - 32f, h, fontSize, ink, alignment: TextAlignmentOptions.Center);
            txt.raycastTarget = false;
            return btn;
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
