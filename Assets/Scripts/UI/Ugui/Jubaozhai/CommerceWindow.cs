using System;
using System.Collections.Generic;
using System.Globalization;
using Google.Protobuf;
using MmorpgClient.Game;
using MmorpgClient.Game.Jubaozhai;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.World.Tianyong;
using TMPro;
using Trade;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Jubaozhai
{
    /// <summary>真实交易合同窗口。浏览、确认、托管和到账都以服务端响应为准。</summary>
    public sealed class CommerceWindow : IDisposable
    {
        private readonly RectTransform _root, _frame, _body;
        private readonly TMP_Text _status;
        private readonly Func<GameClient> _game;
        private readonly List<Button> _writeButtons = new();
        private readonly List<Button> _paymentButtons = new();
        private Button _cancelOrderButton, _stopWaitButton;
        private string _paymentLayoutKey = "";
        private CommerceClient _client;
        private PaymentClient _payment;
        // 正在显示的订单详情;支付方式到了或支付有结局时按它重画。别的页面一画(Clear)就清掉。
        private CommerceOrderResponse _shownOrder;
        private int _tab;
        private int _officialSubTab;
        private ulong _selectedOfficialSkuId;
        private EconomyTradeUiViews.OfficialCashierCountdownHandles _countdownHandles;
        private int _lastCountdownSecond = -1;
        private readonly List<OfficialSkuView> _officialSkus = new();
        private RmbConsignmentSubTab _rmbSubTab = RmbConsignmentSubTab.All;
        private ulong _rmbSelectedListingId;
        private readonly List<RmbConsignmentView> _rmbListings = new();
        private readonly MarketWishlistModel _wishlist = new();
        private ulong _wishlistLoadedPlayerId;
        private string _cursor = "", _nextCursor = "";
        private ulong _afterSku, _nextSku;
        private TradeAssetSelection _selection = new();
        private string _selectedName = "尚未选择资产";
        private TradeMarketView _market;
        private GetMarketDepthResponse _lastDepth;
        private int _marketPage;
        private readonly List<TradeMarketView> _markets = new();
        private readonly Dictionary<string, CommerceOrderResponse> _previousTradeSnapshots = new(StringComparer.Ordinal);
        private readonly HashSet<string> _refPriceRequestedKeys = new(StringComparer.Ordinal);
        private bool _quotaRequested;
        private FaceToFaceTradeDiffResult _activeTradeDiff = FaceToFaceTradeDiffResult.None;
        private string _activeTradeDiffOrderId = "";
        private string _lockedTradeKey = "";
        private ulong _pendingCreditSellerId;
        private SellerCreditView _viewingCreditProfile;
        private Action _creditBackAction;
        private CommerceOrderView _reviewOrder;
        private SellerReviewRating _reviewRating = SellerReviewRating.Positive;
        private string _reviewComment = "";
        private Action _confirmed;
        private bool _disposed;
        public RectTransform Root => _root;
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;

        public CommerceWindow(UnityEngine.Transform parent, Func<GameClient> game)
        {
            _game = game;
            _root = QdaoUguiFactory.CreateStretch("CommerceWindow", parent, Vector4.zero);
            _root.gameObject.AddComponent<Image>().color = new Color(.025f, .10f, .08f, .9f);
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            _frame = QdaoUguiFactory.CreateRect("CommerceFrame", _root, 180, 60, 2200, 960);
            GameplayUiArt.Art(_frame, "main_frame", 0, 0, 2200, 960);
            GameplayUiArt.Text(_frame, "万象阁 · 全球交易", 36, 16, 1100, 60, 36);
            GameplayUiArt.Button(_frame, "关闭", 1990, 20, 160, 58, Hide);
            string[] tabs = { "官方商城", "寄售", "竞拍", "材料市场", "我的订单", "上架与交易", "珍宝阁(RMB)" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int tab = i;
                GameplayUiArt.Button(_frame, tabs[i], 36 + i * 304, 94, 294, 60, () => SelectTab(tab), fontSize: 25);
            }
            _body = QdaoUguiFactory.CreateRect("CommerceBody", _frame, 36, 174, 2120, 660);
            _status = GameplayUiArt.Text(_frame, "", 36, 847, 1640, 72, 25);
            _status.richText = false;
            GameplayUiArt.Button(_frame, "恢复待确认请求", 1690, 844, 450, 64, () => _client?.RetryPending(), fontSize: 26);
            _root.gameObject.SetActive(false);
        }

        private void ApplySafeArea()
        {
            if (_frame == null || Screen.width <= 0 || Screen.height <= 0) return;
            var safe = Screen.safeArea;
            float topRatio = Mathf.Clamp01((Screen.height - (safe.y + safe.height)) / Screen.height);
            float rightRatio = Mathf.Clamp01((Screen.width - (safe.x + safe.width)) / Screen.width);
            float topY = Mathf.Max(60f, topRatio * 1080f + 16f);
            // 顶部避让刘海与小游戏右上胶囊；桌面全屏时保持原始 (180, 60)
            float leftX = Mathf.Max(40f, 180f - rightRatio * 2560f * 0.5f);
            _frame.anchoredPosition = new Vector2(leftX, -Mathf.Min(topY, 110f));
        }

        public void Bind(CommerceClient client, PaymentClient payment = null)
        {
            if (_client != null) { _client.Changed -= Changed; _client.Response -= OnResponse; }
            if (_payment != null) { _payment.Changed -= PaymentChanged; _payment.Settled -= PaymentSettled; }
            _client = client; _payment = payment;
            if (client != null) { client.Changed += Changed; client.Response += OnResponse; }
            if (payment != null) { payment.Changed += PaymentChanged; payment.Settled += PaymentSettled; }
            _selection = new TradeAssetSelection(); _selectedName = "尚未选择资产";
            _selectedOfficialSkuId = 0UL;
            _countdownHandles = default; _lastCountdownSecond = -1;
            _officialSkus.Clear(); _lastDepth = null;
            _previousTradeSnapshots.Clear(); _refPriceRequestedKeys.Clear(); _quotaRequested = false;
            _activeTradeDiff = FaceToFaceTradeDiffResult.None; _activeTradeDiffOrderId = ""; _lockedTradeKey = "";
            _pendingCreditSellerId = 0; _viewingCreditProfile = null; _creditBackAction = null;
            _reviewOrder = null; _reviewRating = SellerReviewRating.Positive; _reviewComment = "";
            Hide();
        }
        public void Show() { ApplySafeArea(); _root.gameObject.SetActive(true); SelectTab(_tab); }
        public void ShowTab(int tab) { ApplySafeArea(); _root.gameObject.SetActive(true); SelectTab(tab); }
        public void ShowOfficialShopSubTab(int subTab)
        {
            _officialSubTab = Mathf.Clamp(subTab, 0, OfficialShopCatalogModel.TabTitles.Length - 1);
            ApplySafeArea();
            _root.gameObject.SetActive(true);
            SelectTab(0);
        }
        public void ShowSellerCredit(ulong sellerPlayerId, Action onBack = null)
        {
            if (sellerPlayerId == 0UL) return;
            ApplySafeArea();
            _root.gameObject.SetActive(true);
            OpenSellerCredit(sellerPlayerId, onBack ?? Hide);
        }
        public void Hide() { if (_root != null) _root.gameObject.SetActive(false); _confirmed = null; }
        public void Back()
        {
            if (_viewingCreditProfile != null) { var back = _creditBackAction; _viewingCreditProfile = null; _creditBackAction = null; if (back != null) back(); else SelectTab(_tab); return; }
            if (_reviewOrder != null) { var backOrder = _reviewOrder.OrderId; _reviewOrder = null; _client?.GetOrder(backOrder); return; }
            if (_confirmed != null) SelectTab(_tab);
            else Hide();
        }
        private void Changed()
        {
            if (_disposed || _root == null) return;
            _status.text = _client?.Status ?? "请先进入角色。";
            RefreshButtonStates();
        }
        private void RefreshButtonStates()
        {
            bool writeEnabled = _client != null && !_client.Busy && !_client.RequiresReconnect && !_client.HasPendingWrite;
            bool payLocked = _payment != null && (_payment.Busy || _payment.ChannelsLoading || _payment.PendingOrder.Length != 0);
            foreach (var button in _writeButtons)
                if (button != null) button.interactable = writeEnabled && (button != _cancelOrderButton || !payLocked);
            foreach (var button in _paymentButtons)
                if (button != null) button.interactable = !payLocked;
            if (_stopWaitButton != null)
                _stopWaitButton.interactable = _payment != null && !_payment.Busy && _payment.PendingOrder.Length != 0;
        }
        private void Clear()
        {
            _confirmed = null; _shownOrder = null; _paymentLayoutKey = "";
            _countdownHandles = default; _lastCountdownSecond = -1;
            _viewingCreditProfile = null; _reviewOrder = null;
            _cancelOrderButton = null; _stopWaitButton = null;
            _writeButtons.Clear(); _paymentButtons.Clear(); GameplayUiArt.Clear(_body);
        }
        /// <summary>主线程每帧更新官方收银台服务端倒计时条（仅整秒变化时原地改宽与文本，零重建）。</summary>
        public void Tick()
        {
            if (_disposed || !IsVisible || _payment == null || !_countdownHandles.IsValid) return;
            if (!_payment.TryGetServerDeadline(_countdownHandles.OrderId, out ulong remainingMs, out ulong totalWindowMs)) return;
            int wholeSeconds = (int)Math.Min(int.MaxValue, (remainingMs + 999UL) / 1000UL);
            if (wholeSeconds == _lastCountdownSecond) return;
            _lastCountdownSecond = wholeSeconds;
            _countdownHandles.Label.text = PaymentCashierViewModel.FormatCountdownText(remainingMs);
            float fillW = PaymentCashierViewModel.ComputeCountdownFillWidth(_countdownHandles.TrackWidthPx, remainingMs, totalWindowMs);
            if (fillW > 0f)
            {
                var size = _countdownHandles.FillRect.sizeDelta;
                _countdownHandles.FillRect.sizeDelta = new Vector2(fillW, size.y);
                _countdownHandles.FillObject.SetActive(true);
            }
            else
            {
                _countdownHandles.FillObject.SetActive(false);
            }
        }
        private string PaymentLayoutKey(CommerceOrderView order)
        {
            if (_payment == null || order == null || order.Kind != CommerceOrderKind.Official) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _payment.Channels.Count && i < 6; i++) sb.Append((int)_payment.Channels[i]).Append(',');
            sb.Append('|').Append(_payment.PendingOrder == order.OrderId ? '1' : '0');
            sb.Append('|').Append(_payment.TryGetServerDeadline(order.OrderId, out _, out _) ? '1' : '0');
            return sb.ToString();
        }
        private void PaymentChanged()
        {
            if (_disposed || _root == null || !IsVisible || _payment == null) return;
            var shown = _shownOrder;
            if (shown != null && shown.Order.State == CommerceOrderState.AwaitingPayment)
            {
                string key = PaymentLayoutKey(shown.Order);
                if (key != _paymentLayoutKey) ShowOrder(shown); // 仅渠道列表或等待态变化时重绘,避免每 2s 轮询重建整个界面
                else RefreshButtonStates();
            }
            if (!string.IsNullOrEmpty(_payment.Status)) _status.text = _payment.Status;
        }
        // 支付有了结局:仅当玩家停在对应订单详情或「我的订单」且当前不忙时刷新,避免后台补单劫持其它页签/表单。
        private void PaymentSettled(string orderId)
        {
            if (!IsVisible || _client == null || _client.Busy || string.IsNullOrEmpty(orderId)) return;
            if (_shownOrder?.Order?.OrderId == orderId || _tab == 4) _client.GetOrder(orderId);
        }
        // 待付款的官方订单:列出当前平台的支付方式。付没付以服务端同步的订单状态为准,这里不改任何资产。
        private void ShowPayment(CommerceOrderView order)
        {
            _paymentLayoutKey = PaymentLayoutKey(order);
            if (_payment == null || order.Kind != CommerceOrderKind.Official)
            { Text("等待正式支付渠道确认。付款结果会由服务端同步，请稍后刷新订单。", 24, 490, 1980, 90); return; }
            if (_payment.PendingOrder == order.OrderId)
            {
                Text("正在等待支付渠道确认，请稍候；如未完成付款可停止等待后重试。", 24, 490, 1460, 56);
                _stopWaitButton = Action("停止等待", 1520, 490, 260, _payment.CancelWatching);
                return;
            }
            if (_payment.Channels.Count == 0)
            {
                _paymentButtons.Add(Action("选择支付方式", 24, 490, 300, _payment.ListChannels));
                return;
            }
            int rendered = 0;
            for (int i = 0; i < _payment.Channels.Count && rendered < 6; i++)
            {
                var channel = _payment.Channels[i];
                if (!OfficialShopCatalogModel.IsChannelAllowedOnPlatform(channel, PaymentClient.Platform)) continue;
                _paymentButtons.Add(Action(PaymentClient.ChannelName(channel), 24 + rendered * 330, 490, 300, () => _payment.Pay(order.OrderId, channel)));
                rendered++;
            }
            if (rendered == 0)
            {
                Text(OfficialShopCatalogModel.PlatformPurchaseComplianceNote(PaymentClient.Platform), 24, 490, 1980, 64, 24);
            }
        }
        private void SelectTab(int tab)
        {
            _tab = tab; _cursor = ""; _afterSku = 0; Load();
        }
        private void Load()
        {
            Clear();
            if (_client == null) { Changed(); return; }
            if (_tab == 0) _client.ListSkus(_afterSku);
            else if (_tab == 1 || _tab == 2) _client.BrowseOffers(_tab == 1 ? CommerceOrderKind.Consignment : CommerceOrderKind.AuctionLot, _cursor);
            else if (_tab == 3) ShowMarket();
            else if (_tab == 4) _client.ListOrders(_cursor);
            else if (_tab == 5) ShowSellForm();
            else _client.ListRmbConsignments(_cursor);
            Changed();
        }
        private void Text(string value, float x, float y, float w = 1500, float h = 55, float size = 27)
        {
            GameplayUiArt.Text(_body, value, x, y, w, h, size).richText = false;
        }
        private Button Action(string label, float x, float y, float w, System.Action callback, bool write = false)
        {
            var button = GameplayUiArt.Button(_body, label, x, y, w, 54, callback, fontSize: 25);
            if (write) _writeButtons.Add(button);
            return button;
        }
        private TMP_InputField Input(string label, float x, float y, float w = 330, string initial = "")
        {
            Text(label, x, y, w, 38, 23);
            var field = QdaoUguiFactory.CreateInputField(label, _body, x, y + 40, w, 52, "", 40);
            field.SetTextWithoutNotify(initial); field.textComponent.richText = false;
            return field;
        }
        private bool Positive(TMP_InputField field, out ulong number)
        {
            if (ulong.TryParse(field.text, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0) return true;
            _client?.SetStatus("请输入有效的正整数。"); return false;
        }
        private void Confirm(string description, System.Action action)
        {
            Clear(); _confirmed = action;
            Text(description, 24, 36, 1980, 260, 29);
            Action("确认提交", 24, 350, 300, () => { var send = _confirmed; _confirmed = null; send?.Invoke(); }, true);
            Action("返回", 350, 350, 240, () => SelectTab(_tab)); Changed();
        }
        private void Pagination()
        {
            Action("首页", 24, 592, 210, () => { _cursor = ""; _afterSku = 0; Load(); });
            Action("刷新", 254, 592, 210, Load);
            var next = Action("下一页", 484, 592, 230, () => { _cursor = _nextCursor; _afterSku = _nextSku; Load(); });
            next.interactable = _tab == 0 ? _nextSku != 0 : !string.IsNullOrEmpty(_nextCursor);
        }
        private void OnResponse(IMessage response)
        {
            if (!IsVisible) return;
            if (response is ListOfficialSkusResponse skus && _tab == 0)
            {
                _nextSku = skus.NextSkuId;
                _officialSkus.Clear();
                for (int i = 0; i < skus.Skus.Count; i++) _officialSkus.Add(skus.Skus[i].Clone());
                RenderOfficialShopPage();
            }
            else if (response is BrowseCommerceOffersResponse offers && (_tab == 1 || _tab == 2))
            {
                Clear(); _nextCursor = offers.NextCursor;
                ulong me = _game()?.PlayerId ?? 0;
                for (int i = 0; i < offers.Offers.Count; i++)
                {
                    var offer = offers.Offers[i].Clone(); float y = i * 68;
                    if (offer.Order == null) continue;
                    var badgeT = SeasonEconomyUiArt.LoadTradeSlice("badge_item_tradable_t");
                    float textX = 24f;
                    if (badgeT != null)
                    {
                        var img = QdaoUguiFactory.CreateImage("OfferBadgeT_" + i, _body, 24f, y + 6f, 42f, 42f, badgeT, false);
                        img.preserveAspect = true;
                        textX = 76f;
                    }
                    string description = Describe(offer, me);
                    if (offer.Order.SellerPlayerId != 0 && _client.SellerCredits.TryGetValue(offer.Order.SellerPlayerId, out var cachedCredit))
                        description += " · " + SellerCreditDisplayModel.FromProto(cachedCredit).SummaryLine;
                    Text(description, textX, y, 1340 - (textX - 24f), 55, 24);
                    if (offer.Order.SellerPlayerId != 0)
                    {
                        ulong sellerId = offer.Order.SellerPlayerId;
                        Action("卖家信用", 1380, y, 240, () => OpenSellerCredit(sellerId, () => SelectTab(_tab)));
                    }
                    if (_tab == 1)
                    {
                        bool lockedToOther = offer.Order.DesignatedBuyerPlayerId != 0 && offer.Order.DesignatedBuyerPlayerId != me;
                        var buyBtn = Action(lockedToOther ? "指定买家专属" : "查看并购买", 1640, y, 370,
                            () => Confirm(description + "\n确认后按该合同原价购买。物品与扣款由服务端托管处理。", () => _client.BuyConsignment(offer.Order.OrderId)),
                            write: !lockedToOther);
                        if (lockedToOther) buyBtn.interactable = false;
                    }
                    else if (offer.Auction != null)
                        Action("查看竞价", 1640, y, 370, () => _client.GetLot(offer.Auction.LotId));
                }
                if (offers.Offers.Count == 0) Text("当前没有已托管的在售合同。", 24, 24);
                Pagination();
            }
            else if (response is ListCommerceOrdersResponse orders && _tab == 4)
            {
                Clear(); _nextCursor = orders.NextCursor;
                for (int i = 0; i < orders.Orders.Count; i++)
                {
                    var order = orders.Orders[i].Clone(); float y = i * 68;
                    string designatedTag = order.DesignatedBuyerPlayerId != 0 ? $" · 定向 #{order.DesignatedBuyerPlayerId}" : "";
                    Text(Kind(order.Kind) + " · " + State(order.State) + " · " + Money(order.Amount, order.Currency) + designatedTag, 24, y, 1470);
                    Action("查看订单", 1640, y, 370, () => _client.GetOrder(order.OrderId));
                }
                if (orders.Orders.Count == 0) Text("暂无订单。", 24, 24);
                Pagination();
            }
            else if (response is CommerceOrderResponse one && one.Order != null) ShowOrder(one);
            else if (response is AuctionLotResponse auction && auction.Lot != null) ShowLot(auction.Lot, auction.ServerNowMs);
            else if (response is ListMarketOrdersResponse marketOrders) ShowMarketOrders(marketOrders);
            else if (response is ListTradeMarketsResponse markets)
            {
                _markets.Clear(); foreach (var market in markets.Markets) _markets.Add(market.Clone());
                _marketPage = 0; ShowMarketCatalog();
            }
            else if (response is MarketOrderResponse marketOrder && marketOrder.Order != null) _client.ListMarket();
            else if (response is GetMarketDepthResponse depth) ShowDepth(depth);
            else if (response is GetMarketReferencePriceResponse p50)
            {
                if (_tab == 3 && _market != null && _lastDepth != null && _market.ItemConfigId == p50.ItemConfigId)
                    ShowDepth(_lastDepth);
            }
            else if (response is GetPlayerTradeQuotaResponse)
            {
                if (_shownOrder?.Order?.Kind == CommerceOrderKind.PlayerTrade) ShowOrder(_shownOrder);
                else if (_tab == 5 && _confirmed == null && _viewingCreditProfile == null) { Clear(); ShowSellForm(); }
            }
            else if (response is SellerCreditResponse credit && credit.Profile != null)
            {
                if (_pendingCreditSellerId == 0 || _pendingCreditSellerId == credit.Profile.SellerPlayerId)
                {
                    _pendingCreditSellerId = 0;
                    ShowSellerCreditModal(credit.Profile);
                }
            }
            else if (response is SubmitSellerReviewResponse review && review.Profile != null)
            {
                _reviewOrder = null;
                _status.text = review.Replayed
                    ? "该订单已评价，已同步最新卖家信用。"
                    : (review.CountedForScore ? "评价已提交，已计入卖家信用档案。" : "评价已记录（命中防刷分规则，不计信用分）。");
                ShowSellerCreditModal(review.Profile);
            }
            else if (response is ListRmbConsignmentsResponse rmbList && _tab == 6)
            {
                _nextCursor = rmbList.NextCursor;
                _rmbListings.Clear();
                for (int i = 0; i < rmbList.Listings.Count; i++) _rmbListings.Add(rmbList.Listings[i].Clone());
                if (_rmbSubTab == RmbConsignmentSubTab.Publish || _rmbSubTab == RmbConsignmentSubTab.Income)
                    _rmbSubTab = RmbConsignmentSubTab.All;
                RenderRmbListingsPage();
            }
            else if (response is ListSellerIncomesResponse incomes)
            {
                _rmbSubTab = RmbConsignmentSubTab.Income;
                Clear();
                _nextCursor = incomes.NextCursor;
                EconomyTradeUiViews.RenderRmbSellerIncomes(
                    _body,
                    incomes.Incomes,
                    SelectRmbSubTab,
                    fen => Confirm(
                        $"确认申请官方提现 {EconomyFormat.FormatMoney(fen, "CNY")}？争议冻结中的金额不可提现，到账以服务端打款状态为准。",
                        () => _client.CreateWithdrawal(fen)),
                    btn => _writeButtons.Add(btn));
            }
            else if (response is RmbConsignmentResponse rmbOne && rmbOne.Listing != null)
            {
                _status.text = $"珍宝阁单 #{rmbOne.Listing.ListingId} 状态已更新：{rmbOne.Listing.Phase} / {rmbOne.Listing.State}";
                _rmbSubTab = RmbConsignmentSubTab.All;
                _client.ListRmbConsignments();
            }
            else if (response is WithdrawalResponse wd && wd.Withdrawal != null)
            {
                Clear();
                Text($"提现申请已提交\n提现单号：{wd.Withdrawal.WithdrawalId}\n申请金额：{EconomyFormat.FormatMoney(wd.Withdrawal.AmountFen, "CNY")}\n状态：{wd.Withdrawal.State}",
                    24, 36, 1960, 260, 27);
                Action("返回我的收益与提现", 24, 330, 360, () => _client.ListSellerIncomes());
                Action("返回珍宝阁列表", 410, 330, 320, () => SelectRmbSubTab(RmbConsignmentSubTab.All));
            }
            Changed();
        }

        private void SelectRmbSubTab(RmbConsignmentSubTab subTab)
        {
            _rmbSubTab = subTab;
            if (subTab == RmbConsignmentSubTab.Publish)
            {
                Clear();
                ShowRmbPublishForm();
                Changed();
                return;
            }
            if (subTab == RmbConsignmentSubTab.Income)
            {
                _client?.ListSellerIncomes();
                return;
            }
            if (_rmbListings.Count > 0)
            {
                RenderRmbListingsPage();
                Changed();
            }
            else
            {
                _client?.ListRmbConsignments(_cursor);
            }
        }

        private void RenderRmbListingsPage()
        {
            Clear();
            EconomyTradeUiViews.RenderRmbListings(
                _body,
                _rmbListings,
                _rmbSubTab,
                _rmbSelectedListingId,
                _game()?.PlayerId ?? 0,
                _client?.SellerCredits,
                SelectRmbSubTab,
                lid => { _rmbSelectedListingId = lid; RenderRmbListingsPage(); Changed(); },
                sid => OpenSellerCredit(sid, () => { RenderRmbListingsPage(); Changed(); }),
                item => Confirm(
                    $"确认官方担保购买「{item.Title}」（#{item.ListingId}），价格 {EconomyFormat.FormatMoney(item.PriceFen, "CNY")}？\n资金由官方托管并按公示/核查规则结算，请按当前发行平台完成合规支付。",
                    () => _client.BuyRmbConsignment(item.ListingId)),
                item => Confirm(
                    $"确认撤回 RMB 托管寄售「{item.Title}」（#{item.ListingId}）？撤回后资产按服务端规则退回。",
                    () => _client.CancelRmbConsignment(item.ListingId)),
                btn => _writeButtons.Add(btn));
            Pagination();
        }

        private void RenderOfficialShopPage()
        {
            Clear();
            EconomyTradeUiViews.RenderOfficialShop(
                _body,
                _officialSubTab,
                _officialSkus,
                subTab => { _officialSubTab = subTab; _selectedOfficialSkuId = 0UL; RenderOfficialShopPage(); Changed(); },
                sku => Confirm("购买「" + sku.Title + "」一份，价格 " + Money(sku.PriceMinor, sku.Currency)
                    + "。创建订单后，请按当前发行平台完成官方合规支付；到账以服务端确认和订单状态为准。", () => _client.BuySku(sku.SkuId, 1)),
                () =>
                {
                    if (AfkPassUiRoot.Instance != null) AfkPassUiRoot.Instance.ShowPanel();
                    else _client?.SetStatus("请进入主城后查看「仙途轻享卡」。");
                },
                btn => _writeButtons.Add(btn),
                selectedSkuId: _selectedOfficialSkuId,
                onSelectSku: sku => { _selectedOfficialSkuId = sku.SkuId; RenderOfficialShopPage(); Changed(); });
            Pagination();
        }

        private void OpenSellerCredit(ulong sellerPlayerId, Action onBack)
        {
            if (sellerPlayerId == 0 || _client == null) return;
            _creditBackAction = onBack;
            if (_client.SellerCredits.TryGetValue(sellerPlayerId, out var cached) && _client.Busy)
            {
                ShowSellerCreditModal(cached);
                return;
            }
            _pendingCreditSellerId = sellerPlayerId;
            _client.GetSellerCredit(sellerPlayerId);
        }

        private void ShowSellerCreditModal(SellerCreditView profile)
        {
            var back = _creditBackAction ?? (() => SelectTab(_tab));
            Clear();
            _viewingCreditProfile = profile.Clone();
            _creditBackAction = back;
            EconomyTradeUiViews.RenderSellerCreditProfile(
                _body,
                _viewingCreditProfile,
                () => { _pendingCreditSellerId = profile.SellerPlayerId; _client?.GetSellerCredit(profile.SellerPlayerId); },
                () => { _viewingCreditProfile = null; back(); });
            RefreshButtonStates();
        }

        private void OpenSellerReview(CommerceOrderView order)
        {
            if (order == null || _client == null) return;
            Clear();
            _reviewOrder = order.Clone();
            EconomyTradeUiViews.RenderSellerReviewDialog(
                _body,
                _reviewOrder,
                _reviewRating,
                _reviewComment,
                rating => { _reviewRating = rating; OpenSellerReview(_reviewOrder); },
                (rating, comment) =>
                {
                    _reviewRating = rating;
                    _reviewComment = comment ?? "";
                    _client.SubmitSellerReview(_reviewOrder.OrderId, rating, _reviewComment);
                },
                () => { string orderId = _reviewOrder.OrderId; _reviewOrder = null; _client.GetOrder(orderId); },
                btn => _writeButtons.Add(btn));
            RefreshButtonStates();
        }

        private void ShowOrder(CommerceOrderResponse response)
        {
            var order = response.Order;
            ulong player = _game()?.PlayerId ?? 0;

            if (order.Kind == CommerceOrderKind.PlayerTrade)
            {
                if (!string.Equals(_activeTradeDiffOrderId, order.OrderId, StringComparison.Ordinal))
                {
                    _activeTradeDiff = FaceToFaceTradeDiffResult.None;
                    _activeTradeDiffOrderId = order.OrderId;
                }
                if (_previousTradeSnapshots.TryGetValue(order.OrderId, out var prev))
                {
                    var diff = FaceToFaceTradeDiffModel.Compare(prev, response, player);
                    if (diff.HasChanges)
                    {
                        _activeTradeDiff = diff;
                        _activeTradeDiffOrderId = order.OrderId;
                        if (diff.LockCleared) _lockedTradeKey = "";
                    }
                }
                _previousTradeSnapshots[order.OrderId] = response.Clone();

                Clear();
                _shownOrder = response;

                bool canConfirm = order.State == CommerceOrderState.WaitingConfirmation
                    && response.SellerAssets != null && response.BuyerAssets != null
                    && ((order.BuyerPlayerId == player && !order.BuyerConfirmed && HasTrustedSummary(response.SellerAssets, response.SellerItemInstances, response.SellerPet))
                        || (order.SellerPlayerId == player && order.BuyerConfirmed && !order.SellerConfirmed && HasTrustedSummary(response.BuyerAssets, response.BuyerItemInstances, response.BuyerPet)));

                string lockKey = order.OrderId + ":" + order.Version.ToString(CultureInfo.InvariantCulture);
                bool localLocked = canConfirm && string.Equals(_lockedTradeKey, lockKey, StringComparison.Ordinal);
                GetPlayerTradeQuotaResponse quota = null;
                _client?.TryGetTradeQuota(0u, out quota);
                if (quota == null && _client != null && !_client.Busy && !_quotaRequested)
                {
                    _quotaRequested = true;
                    _client.GetPlayerTradeQuota(0u);
                }

                ulong counterpartyId = order.SellerPlayerId == player ? order.BuyerPlayerId : order.SellerPlayerId;
                EconomyTradeUiViews.RenderFaceToFaceOrder(
                    _body,
                    response,
                    player,
                    quota,
                    _activeTradeDiff,
                    localLocked,
                    canConfirm,
                    onLockTerms: () =>
                    {
                        if (!canConfirm) return;
                        _lockedTradeKey = localLocked ? "" : lockKey;
                        if (!localLocked) _activeTradeDiff = FaceToFaceTradeDiffResult.None;
                        ShowOrder(response);
                    },
                    onFinalConfirm: () =>
                    {
                        if (!canConfirm || !string.Equals(_lockedTradeKey, lockKey, StringComparison.Ordinal)) return;
                        string terms = "卖方交付：" + DescribeTerms(response.SellerAssets, response.SellerItemInstances, response.SellerPet)
                            + "\n买方交付：" + DescribeTerms(response.BuyerAssets, response.BuyerItemInstances, response.BuyerPet);
                        Confirm(terms + "\n最终确认以上不可变条款？买方确认将托管买方资产；卖方最终确认后执行交换。",
                            () => _client.ConfirmTrade(order.OrderId));
                    },
                    onRefresh: () => _client.GetOrder(order.OrderId),
                    onCancel: () => Confirm("确认取消该当面交易？", () => _client.CancelOrder(order.OrderId)),
                    onViewCounterpartyCredit: () => OpenSellerCredit(counterpartyId, () => ShowOrder(response)),
                    onBackToOrders: () => SelectTab(4),
                    registerWriteButton: btn => _writeButtons.Add(btn),
                    cancelButton: out _cancelOrderButton);
                RefreshButtonStates();
                return;
            }

            if (order.Kind == CommerceOrderKind.Official)
            {
                Clear();
                _shownOrder = response;
                _paymentLayoutKey = PaymentLayoutKey(order);
                _countdownHandles = EconomyTradeUiViews.RenderOfficialOrderCashier(
                    _body,
                    order,
                    Kind(order.Kind),
                    State(order.State),
                    Money(order.Amount, order.Currency),
                    _payment,
                    onRefreshOrder: () => _client.GetOrder(order.OrderId),
                    onCancelOrder: () => Confirm("确认取消该订单？已支付订单由服务端确定退款状态。", () => _client.CancelOrder(order.OrderId)),
                    onBackOrders: () => SelectTab(4),
                    registerWriteButton: btn => _writeButtons.Add(btn),
                    registerPaymentButton: btn => _paymentButtons.Add(btn),
                    cancelOrderButton: out _cancelOrderButton,
                    stopWaitButton: out _stopWaitButton);
                RefreshButtonStates();
                return;
            }

            Clear();
            _shownOrder = response;
            string designatedLine = order.DesignatedBuyerPlayerId != 0
                ? "\n指定买家角色编号：#" + order.DesignatedBuyerPlayerId.ToString(CultureInfo.InvariantCulture)
                : "";
            Text(Kind(order.Kind) + " · " + State(order.State) + "\n订单 " + order.OrderId + "\n金额 " + Money(order.Amount, order.Currency)
                + designatedLine
                + "\n卖方确认：" + (order.SellerConfirmed ? "是" : "否") + "  买方确认：" + (order.BuyerConfirmed ? "是" : "否"), 24, 0, 1950, 220);

            Action("刷新订单", 24, 410, 280, () => _client.GetOrder(order.OrderId));
            float nextX = 330f;
            if (order.State == CommerceOrderState.WaitingConfirmation || order.State == CommerceOrderState.AwaitingPayment)
            {
                _cancelOrderButton = Action("取消订单", nextX, 410, 280, () => Confirm("确认取消该订单？已支付订单由服务端确定退款状态。", () => _client.CancelOrder(order.OrderId)), true);
                nextX += 306f;
            }
            bool activeConsignment = order.State != CommerceOrderState.Delivered && order.State != CommerceOrderState.Cancelled
                && order.State != CommerceOrderState.Refunded && order.State != CommerceOrderState.Settled
                && order.State != CommerceOrderState.Reversed;
            if (order.Kind == CommerceOrderKind.Consignment && activeConsignment && order.SellerPlayerId == player)
            {
                Action("撤回寄售", nextX, 410, 280, () => Confirm("确认撤回该寄售？资产返回以到账状态为准。", () => _client.CancelConsignment(order.OrderId)), true);
                nextX += 306f;
            }
            if (order.SellerPlayerId != 0)
            {
                Action("卖家信用", nextX, 410, 260, () => OpenSellerCredit(order.SellerPlayerId, () => ShowOrder(response)));
                nextX += 286f;
            }
            bool canReviewSeller = order.State == CommerceOrderState.Settled
                && order.BuyerPlayerId == player && order.SellerPlayerId != 0 && order.SellerPlayerId != player
                && (order.Kind == CommerceOrderKind.Consignment || order.Kind == CommerceOrderKind.AuctionLot || order.Kind == CommerceOrderKind.RmbConsignment);
            if (canReviewSeller)
            {
                Action("评价卖家（7日内）", nextX, 410, 340, () =>
                {
                    _reviewRating = SellerReviewRating.Positive;
                    _reviewComment = "";
                    OpenSellerReview(order);
                }, true);
            }
            if (order.State == CommerceOrderState.AwaitingPayment) ShowPayment(order);
            Action("我的订单", 24, 592, 300, () => SelectTab(4));
            RefreshButtonStates();
        }

        private static string DescribeTerms(TradeAssetSelection assets, IEnumerable<TradeOfferInstance> instances, TradeOfferInstance pet)
        {
            var pieces = new List<string>();
            string itemsDesc = FaceToFaceTradeDiffModel.DescribeInstances(assets, instances, pet);
            if (itemsDesc != "无") pieces.Add(itemsDesc);
            if (assets != null && assets.CurrencyAmount != 0)
                pieces.Add(EconomyFormat.FormatCurrency(assets.CurrencyAmount, assets.CurrencyType));
            return pieces.Count == 0 ? "无" : string.Join("、", pieces);
        }

        private static bool HasTrustedSummary(TradeAssetSelection assets, IEnumerable<TradeOfferInstance> instances, TradeOfferInstance pet)
        {
            if (assets == null) return false;
            var configs = new Dictionary<uint, uint>(); var guids = new HashSet<ulong>();
            foreach (var item in instances)
            {
                if (string.IsNullOrEmpty(item.Name) || item.Count == 0) return false;
                configs[item.ConfigId] = item.Count; guids.Add(item.InstanceId);
            }
            foreach (var selected in assets.Items) if (!configs.TryGetValue(selected.Key, out var count) || count < selected.Value) return false;
            foreach (var guid in assets.ItemUuids) if (!guids.Contains(guid)) return false;
            return assets.PetId == 0 || (pet != null && pet.InstanceId == assets.PetId && !string.IsNullOrEmpty(pet.Name));
        }

        private void ShowLot(AuctionLotView lot, ulong now)
        {
            Clear();
            ulong minimum;
            try { minimum = lot.HighestBid == 0 ? lot.OpeningPrice : checked(lot.HighestBid + lot.MinimumIncrement); }
            catch (OverflowException) { Text("当前价格已到达上限。", 24, 24); return; }
            Text("竞拍 " + lot.LotId + "\n当前出价 " + EconomyFormat.FormatCurrency(lot.HighestBid, lot.CurrencyType)
                + "  最低加价 " + EconomyFormat.FormatCurrency(lot.MinimumIncrement, lot.CurrencyType)
                + "\n剩余 " + EconomyFormat.FormatInteger(lot.EndMs > now ? (lot.EndMs - now) / 1000 : 0) + " 秒 · " + lot.State, 24, 20, 1850, 220);
            var amount = Input("出价", 24, 260, 620, minimum.ToString(CultureInfo.InvariantCulture));
            Action("提交出价", 690, 300, 320, () => { if (Positive(amount, out var bid)) Confirm("确认出价 " + EconomyFormat.FormatCurrency(bid, lot.CurrencyType) + "？有效出价将托管对应货币，退回或成交请查看订单。", () => _client.Bid(lot.LotId, bid)); }, true);
            Action("刷新竞拍", 1040, 300, 320, () => _client.GetLot(lot.LotId));
            if (lot.SellerPlayerId != 0) Action("卖家信用", 1390, 300, 240, () => OpenSellerCredit(lot.SellerPlayerId, () => ShowLot(lot, now)));
            if (lot.SellerPlayerId == _game()?.PlayerId) Action("撤回竞拍", 1660, 300, 280, () => Confirm("确认撤回此拍品？有有效出价时以服务端规则为准。", () => _client.CancelLot(lot.LotId)), true);
            Action("返回列表", 24, 592, 300, () => SelectTab(2));
        }

        private void ShowSellForm()
        {
            EconomyTradeUiViews.DrawPanelBackdrop(_body, "panel_05_market_order", 0.16f);
            GetPlayerTradeQuotaResponse quota = null;
            _client?.TryGetTradeQuota(0u, out quota);
            if (quota == null && _client != null && !_client.Busy && !_quotaRequested)
            {
                _quotaRequested = true;
                _client.GetPlayerTradeQuota(0u);
            }

            Text("已选择：" + _selectedName + "  |  " + MarketP50DisplayModel.DescribeNoticeTier(_selection.ItemUuids.Count > 0 || _selection.PetId != 0), 24, 4, 1520, 44, 23);
            Action("我的店铺信用", 1560, 0, 260, () => OpenSellerCredit(_game()?.PlayerId ?? 0, () => { Clear(); ShowSellForm(); Changed(); }));
            Action("刷新额度", 1840, 0, 240, () => { _quotaRequested = true; _client?.GetPlayerTradeQuota(0u); });

            Text(FaceToFaceTradeDiffModel.FormatQuotaSummary(quota) + "（好友不代表官方担保）", 24, 54, 2050, 40, 21);

            Action("从背包选择", 24, 108, 320, () => ShowAssets(false, 0));
            Action("从宠物选择", 368, 108, 320, () => ShowAssets(true, 0));

            var price = Input("价格 / 起拍价（银两）", 24, 184, 380);
            var hours = Input("上架小时数", 424, 184, 220, "24");
            var increment = Input("竞拍最低加价", 664, 184, 240, "1");
            var designatedBuyer = Input("指定买家角色编号（选填）", 924, 184, 460);
            System.Action<bool> publish = auction =>
            {
                if (!HasAssets()) { _client.SetStatus("请先选择要交易的资产。"); return; }
                if (!Positive(price, out var amount) || !Positive(hours, out var hour) || hour > 720) { _client.SetStatus("请填写有效价格及1至720小时的时长。"); return; }
                ulong designatedId = 0;
                string rawDesignated = (designatedBuyer.text ?? "").Trim();
                if (!auction && rawDesignated.Length > 0)
                {
                    if (!ulong.TryParse(rawDesignated, NumberStyles.None, CultureInfo.InvariantCulture, out designatedId) || designatedId == 0)
                    { _client.SetStatus("指定买家角色编号必须为正整数，或留空公开寄售。"); return; }
                }
                ulong duration = hour * 3600000;
                ulong step = 0;
                if (auction && !Positive(increment, out step)) return;
                var assets = _selection.Clone();
                string designatedNote = designatedId != 0 ? $"，仅限指定买家角色编号 #{designatedId} 购买" : "";
                string noticeNote = MarketP50DisplayModel.DescribeNoticeTier(assets.ItemUuids.Count > 0 || assets.PetId != 0);
                Confirm("上架「" + _selectedName + "」，" + (auction ? "起拍价 " : "价格 ") + EconomyFormat.FormatSilver(amount)
                    + "，时长 " + hour + " 小时" + designatedNote + "。\n公示规则：" + noticeNote + "。资产将移入官方托管。",
                    () => { if (auction) _client.CreateLot(assets, 0, amount, step, duration); else _client.Consign(assets, 0, amount, duration, designatedId); });
            };
            Action("创建寄售 / 定向托管单", 24, 296, 440, () => publish(false), true);
            Action("创建竞拍", 494, 296, 300, () => publish(true), true);

            Text("游戏内当面交易（仅限低价耗材与当日小额银两互助；贵重装备请走上方官方指定买家托管单）：", 24, 380, 2040, 38, 21);
            var target = Input("对方服务端角色编号", 24, 424, 520);
            var wanted = Input("向对方收取银两（千分位核对）", 574, 424, 480);
            Action("发起当面交易", 1084, 464, 420, () =>
            {
                if (!HasAssets()) { _client.SetStatus("请先选择要交易的资产。"); return; }
                if (!Positive(target, out var buyer) || !Positive(wanted, out var gold)) return;
                var seller = _selection.Clone();
                Confirm("向服务端角色编号 #" + buyer + " 发起当面交易：交付「" + _selectedName + "」，收取 " + EconomyFormat.FormatSilver(gold)
                    + "。\n好友不代表官方担保；任一方修改条款均会清空双方锁定。",
                    () => _client.CreateTrade(buyer, seller, new TradeAssetSelection { CurrencyType = 0, CurrencyAmount = gold }));
            }, true);
        }

        private void ShowActivePublishForm()
        {
            if (_tab == 6) ShowRmbPublishForm();
            else ShowSellForm();
        }

        private void ShowRmbPublishForm()
        {
            _rmbSubTab = RmbConsignmentSubTab.Publish;
            EconomyTradeUiViews.DrawPanelBackdrop(_body, "panel_08_zhenbaoge_rmb", 0.30f);
            EconomyTradeUiViews.RenderRmbSubTabs(_body, RmbConsignmentSubTab.Publish, SelectRmbSubTab);
            bool uniqueValuable = _selection.ItemUuids.Count > 0 || _selection.PetId != 0;
            Text("已选托管资产：" + _selectedName, 24, 68, 1200, 44, 24);
            Action("从背包选择", 1360, 66, 330, () => ShowAssets(false, 0));
            Action("从宠物选择", 1720, 66, 330, () => ShowAssets(true, 0));

            var titleField = Input("寄售展示标题", 24, 146, 520, _selectedName == "尚未选择资产" ? "" : _selectedName);
            var priceFenField = Input("价格（单位：分，100分=1元）", 574, 146, 460, "1000");
            var designatedBuyer = Input("指定买家角色编号（选填，留空公开）", 1064, 146, 520);

            Text("分层公示说明：" + MarketP50DisplayModel.DescribeNoticeTier(uniqueValuable)
                + "\n所有资金与货品均由官方托管，若交易触发争议核查将自动冻结对应待提现金额直至结案。",
                24, 266, 2040, 88, 21);

            Action("提交官方托管 RMB 寄售", 24, 384, 520, () =>
            {
                if (!HasAssets()) { _client.SetStatus("请先从背包或宠物选择要上架的资产。"); return; }
                string title = (titleField.text ?? "").Trim();
                if (string.IsNullOrEmpty(title)) { _client.SetStatus("请填写展示标题。"); return; }
                if (!Positive(priceFenField, out ulong priceFen)) return;
                ulong designatedId = 0;
                string rawDesignated = (designatedBuyer.text ?? "").Trim();
                if (rawDesignated.Length > 0 && (!ulong.TryParse(rawDesignated, NumberStyles.None, CultureInfo.InvariantCulture, out designatedId) || designatedId == 0))
                {
                    _client.SetStatus("指定买家角色编号必须为正整数，或留空公开寄售。");
                    return;
                }
                var assets = _selection.Clone();
                string noticeNote = MarketP50DisplayModel.DescribeNoticeTier(uniqueValuable, priceFen);
                string designatedNote = designatedId != 0 ? $"\n仅限指定买家角色编号 #{designatedId} 购买。" : "";
                Confirm($"确认上架珍宝阁 RMB 托管单「{title}」，价格 {EconomyFormat.FormatMoney(priceFen, "CNY")}？\n公示规则：{noticeNote}{designatedNote}",
                    () => _client.CreateRmbConsignment(assets, title, priceFen, designatedId));
            }, true);
        }

        private void EnsureWishlistLoaded()
        {
            ulong pid = _game()?.PlayerId ?? 0UL;
            if (pid == 0UL || pid == _wishlistLoadedPlayerId) return;
            _wishlistLoadedPlayerId = pid;
            _wishlist.LoadSerialized(PlayerPrefs.GetString("CommerceWishlist_" + pid.ToString(CultureInfo.InvariantCulture), ""));
        }

        private void SaveWishlist()
        {
            ulong pid = _game()?.PlayerId ?? 0UL;
            if (pid == 0UL) return;
            _wishlistLoadedPlayerId = pid;
            PlayerPrefs.SetString("CommerceWishlist_" + pid.ToString(CultureInfo.InvariantCulture), _wishlist.Serialize());
            PlayerPrefs.Save();
        }

        private bool HasAssets() => _selection.Items.Count != 0 || _selection.ItemUuids.Count != 0 || _selection.PetId != 0;
        private void ShowAssets(bool pets, int page)
        {
            Clear(); var game = _game(); int start = page * 7;
            if (pets)
            {
                var list = game?.Pets?.Pets?.Pets;
                if (list == null) { game?.Pets?.RequestList(); Text("正在读取宠物，请稍后刷新。", 24, 24); }
                else for (int i = start; i < list.Count && i < start + 7; i++)
                {
                    var pet = list[i]; float y = (i - start) * 70;
                    Text(pet.Name + " · 等级 " + pet.Level + (pet.IsActive ? " · 出战中" : ""), 24, y, 1450);
                    var button = Action("选择", 1600, y, 350, () => { _selection = new TradeAssetSelection { PetId = pet.PetId }; _selectedName = pet.Name; Clear(); ShowActivePublishForm(); Changed(); });
                    button.interactable = !pet.IsActive;
                }
            }
            else
            {
                var bag = game?.Features?.CharacterBag;
                var list = bag?.Items;
                if (list == null) { game?.Features?.RequestBag(); Text("正在读取背包，请稍后刷新。", 24, 24); }
                else for (int i = start; i < list.Count && i < start + 7; i++)
                {
                    var item = list[i]; float y = (i - start) * 70;
                    // 押在还没结算的战斗里的数量拿不出来(服务端战斗道具预留):只让选没被占住的部分,全被占住就不让选。
                    // 选了也过不去 —— 服务器会一直回「稍后重试」,直到那一局结算。
                    ulong held = EquipBagModel.HeldCountOf(bag, item.ConfigId);
                    uint available = (uint)EquipBagModel.AvailableInStack(bag, item);
                    Text(item.Name + " × " + item.Count + (item.MaxStack == 1 ? " · 独立实例" : "")
                        + (held != 0 ? " · 战斗结算中 " + held + "，可交易 " + available : ""), 24, y, 1450);
                    var choose = Action("选择", 1600, y, 350, () =>
                    {
                        _selection = new TradeAssetSelection();
                        if (item.MaxStack == 1) _selection.ItemUuids.Add(item.ItemId); else _selection.Items[item.ConfigId] = available;
                        _selectedName = item.Name + " × " + available; Clear(); ShowActivePublishForm(); Changed();
                    });
                    choose.interactable = available != 0;
                }
            }
            Action("上一页", 24, 592, 240, () => ShowAssets(pets, Math.Max(0, page - 1)));
            Action("下一页", 294, 592, 240, () => ShowAssets(pets, page + 1));
            Action("刷新", 564, 592, 240, () => { if (pets) game?.Pets?.RequestList(); else game?.Features?.RequestBag(); ShowAssets(pets, page); });
            Action("返回", 834, 592, 240, () => { Clear(); ShowActivePublishForm(); Changed(); });
        }

        // 市场目录由服务端白名单返回；不让玩家猜测或提交内部编号。
        private void ShowMarket()
        {
            _client.ListMarkets();
        }
        private void ShowMarketCatalog()
        {
            Clear();
            EnsureWishlistLoaded();
            var doveSprite = SeasonEconomyUiArt.LoadTradeV2Slice("icon_wishlist_dove");
            for (int i = _marketPage * 7; i < _markets.Count && i < (_marketPage + 1) * 7; i++)
            {
                var market = _markets[i].Clone(); float y = (i % 7) * 68;
                GetMarketReferencePriceResponse cachedP50 = null;
                _client?.TryGetMarketReferencePrice(market.ItemConfigId, market.CurrencyType, out cachedP50);
                var wishAlert = _wishlist.Evaluate(market.ItemConfigId, market.CurrencyType, cachedP50);
                float textX = 24f;
                if (wishAlert.HasTarget && doveSprite != null)
                {
                    var doveImg = QdaoUguiFactory.CreateImage("CatalogDove_" + i, _body, 24f, y + 6f, 44f, 44f, doveSprite, false);
                    doveImg.preserveAspect = true;
                    textX = 78f;
                }
                string wishSuffix = wishAlert.BelowOrAtTarget
                    ? $"  ·  【已跌破心愿价 {EconomyFormat.FormatCurrency(wishAlert.TargetPrice, market.CurrencyType)}】"
                    : (wishAlert.HasTarget ? $"  ·  心愿价 {EconomyFormat.FormatCurrency(wishAlert.TargetPrice, market.CurrencyType)}" : "");
                Text(market.Title + wishSuffix, textX, y, 1520f - (textX - 24f));
                Action("查看买卖盘", 1640, y, 370, () => { _market = market; _client.MarketDepth(market.MarketId, market.ItemConfigId); });
            }
            if (_markets.Count == 0) Text("当前没有开放的材料市场。", 24, 24);
            Action("上一页", 24, 592, 240, () => { _marketPage = Math.Max(0, _marketPage - 1); ShowMarketCatalog(); });
            var next = Action("下一页", 294, 592, 240, () => { ++_marketPage; ShowMarketCatalog(); });
            next.interactable = (_marketPage + 1) * 7 < _markets.Count;
            Action("我的材料挂单", 564, 592, 430, () => _client.ListMarket());
            Action("刷新目录", 1024, 592, 350, () => _client.ListMarkets());
        }
        private void ShowMarketOrders(ListMarketOrdersResponse response)
        {
            Clear(); _nextCursor = response.NextCursor;
            for (int i = 0; i < response.Orders.Count; i++)
            {
                var order = response.Orders[i]; float y = i * 68;
                Text((order.Side == MarketOrderSide.Buy ? "买入" : "卖出") + " · 数量 " + EconomyFormat.FormatInteger(order.Quantity)
                    + " · 已成交 " + EconomyFormat.FormatInteger(order.FilledQuantity)
                    + " · 单价 " + EconomyFormat.FormatCurrency(order.UnitPrice, order.CurrencyType) + " · " + order.State, 24, y, 1520);
                Action("撤单", 1640, y, 370, () => Confirm("确认撤回尚未成交的部分？", () => _client.CancelMarket(order.MarketOrderId)), true);
            }
            Action("首页", 24, 592, 260, () => _client.ListMarket());
            var next = Action("下一页", 314, 592, 260, () => _client.ListMarket(_nextCursor));
            next.interactable = !string.IsNullOrEmpty(_nextCursor);
        }
        private void ShowDepth(GetMarketDepthResponse response)
        {
            Clear(); if (_market == null) return;
            EnsureWishlistLoaded();
            _lastDepth = response;
            var market = _market.Clone();
            EconomyTradeUiViews.DrawPanelBackdrop(_body, "panel_05_market_order", 0.16f);

            GetMarketReferencePriceResponse p50 = null;
            _client?.TryGetMarketReferencePrice(market.ItemConfigId, market.CurrencyType, out p50);
            string refKey = market.ItemConfigId.ToString(CultureInfo.InvariantCulture) + ":" + market.CurrencyType.ToString(CultureInfo.InvariantCulture);
            if (p50 == null && _client != null && !_client.Busy && _refPriceRequestedKeys.Add(refKey))
            {
                _client.GetMarketReferencePrice(market.ItemConfigId, market.CurrencyType);
            }

            EconomyTradeUiViews.RenderMarketP50Strip(_body, p50, () => _client?.GetMarketReferencePrice(market.ItemConfigId, market.CurrencyType));

            Text(market.Title + " · 卖盘", 24, 92, 750, 42, 26);
            Text("买盘", 900, 92, 750, 42, 26);
            int rows = Math.Min(response.Asks.Count, 5);
            for (int i = 0; i < rows; i++)
                Text(EconomyFormat.FormatCurrency(response.Asks[i].UnitPrice, market.CurrencyType) + " × " + EconomyFormat.FormatInteger(response.Asks[i].Quantity), 24, 140 + i * 38, 750, 36, 23);
            int bidRows = Math.Min(response.Bids.Count, 5);
            for (int i = 0; i < bidRows; i++)
                Text(EconomyFormat.FormatCurrency(response.Bids[i].UnitPrice, market.CurrencyType) + " × " + EconomyFormat.FormatInteger(response.Bids[i].Quantity), 900, 140 + i * 38, 750, 36, 23);

            var wishAlert = _wishlist.Evaluate(market.ItemConfigId, market.CurrencyType, p50);
            EconomyTradeUiViews.RenderMarketWishlistStrip(
                _body,
                market.Title,
                market.CurrencyType,
                wishAlert,
                targetPrice =>
                {
                    _wishlist.SetTarget(market.ItemConfigId, market.CurrencyType, targetPrice);
                    SaveWishlist();
                    _client?.SetStatus($"已设置「{market.Title}」心愿目标价：{EconomyFormat.FormatCurrency(targetPrice, market.CurrencyType)}。");
                    ShowDepth(response);
                },
                () =>
                {
                    _wishlist.RemoveTarget(market.ItemConfigId, market.CurrencyType);
                    SaveWishlist();
                    _client?.SetStatus($"已清除「{market.Title}」心愿目标价。");
                    ShowDepth(response);
                });

            var quantity = Input("数量", 24, 420, 380, "1");
            var price = Input("每件限价（银两）", 434, 420, 440);
            System.Action<MarketOrderSide> place = side =>
            {
                if (!Positive(quantity, out var count) || count > uint.MaxValue || !Positive(price, out var unitPrice)) return;
                ulong total; try { total = checked(count * unitPrice); } catch (OverflowException) { _client.SetStatus("订单总价超过支持范围。"); return; }
                Confirm((side == MarketOrderSide.Buy ? "买入" : "卖出") + market.Title + " × " + EconomyFormat.FormatInteger(count)
                    + "，每件限价 " + EconomyFormat.FormatCurrency(unitPrice, market.CurrencyType)
                    + "，总额上限 " + EconomyFormat.FormatCurrency(total, market.CurrencyType)
                    + "。价格相同时按服务端提交顺序撮合；未成交部分可撤单。",
                    () => _client.PlaceMarket(market.MarketId, market.ItemConfigId, side, (uint)count, unitPrice, market.CurrencyType));
            };
            Action("限价买入", 904, 460, 320, () => place(MarketOrderSide.Buy), true);
            Action("限价卖出", 1254, 460, 320, () => place(MarketOrderSide.Sell), true);
            Action("刷新买卖盘", 1604, 460, 390, () => _client.MarketDepth(market.MarketId, market.ItemConfigId));
            Action("返回市场", 24, 592, 360, ShowMarketCatalog); Changed();
        }
        private static string Describe(CommerceOfferView offer, ulong viewerPlayerId = 0)
        {
            var pieces = new List<string>();
            foreach (var item in offer.ItemInstances) pieces.Add(FaceToFaceTradeDiffModel.DescribeOfferItem(item));
            if (offer.Pet != null && offer.Pet.InstanceId != 0) pieces.Add(offer.Pet.Name + " · 等级 " + offer.Pet.Level);
            if (offer.Assets != null) foreach (var item in offer.Assets.Items) pieces.Add("材料 " + item.Key + " × " + EconomyFormat.FormatInteger(item.Value));
            string designatedTag = "";
            if (offer.Order != null && offer.Order.DesignatedBuyerPlayerId != 0)
            {
                designatedTag = offer.Order.DesignatedBuyerPlayerId == viewerPlayerId
                    ? " · 【专属定向单】"
                    : $" · 【定向单 #{offer.Order.DesignatedBuyerPlayerId}】";
            }
            return string.Join("、", pieces) + " · " + Money(offer.Order.Amount, offer.Order.Currency) + designatedTag;
        }
        private static string CurrencyName(uint type) => EconomyFormat.CurrencyName(type);
        private static string Money(ulong amount, string currency) => EconomyFormat.FormatMoney(amount, currency);
        private static string Kind(CommerceOrderKind kind) => kind switch
        {
            CommerceOrderKind.Official => "官方商品",
            CommerceOrderKind.PlayerTrade => "玩家交易",
            CommerceOrderKind.Consignment => "寄售",
            CommerceOrderKind.AuctionLot => "竞拍",
            CommerceOrderKind.RmbConsignment => "人民币寄售",
            _ => "材料成交"
        };
        private static string State(CommerceOrderState state) => state switch {
            CommerceOrderState.WaitingConfirmation => "等待双方确认", CommerceOrderState.AwaitingPayment => "等待付款确认", CommerceOrderState.Escrowing => "资产托管中",
            CommerceOrderState.Delivering => "资产到账中", CommerceOrderState.Delivered => "已到账", CommerceOrderState.CancelPending => "取消处理中",
            CommerceOrderState.Cancelled => "已取消", CommerceOrderState.RefundPending => "退款处理中", CommerceOrderState.Refunded => "已退款",
            CommerceOrderState.Manual => "等待人工核查", CommerceOrderState.Settled => "已结算",
            CommerceOrderState.Reversing => "付款已被渠道撤销，正在核对发放", CommerceOrderState.Reversed => "付款已被渠道撤销，发放已收回", _ => "正在确认" };
        public void Dispose() { if (_disposed) return; _disposed = true; Bind(null); if (_root != null) UnityEngine.Object.Destroy(_root.gameObject); }
    }
}
