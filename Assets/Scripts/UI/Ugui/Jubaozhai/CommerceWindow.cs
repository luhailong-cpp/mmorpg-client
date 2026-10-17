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
        private readonly RectTransform _root, _body;
        private readonly TMP_Text _status;
        private readonly Func<GameClient> _game;
        private readonly List<Button> _writeButtons = new();
        private CommerceClient _client;
        private int _tab;
        private string _cursor = "", _nextCursor = "";
        private ulong _afterSku, _nextSku;
        private TradeAssetSelection _selection = new();
        private string _selectedName = "尚未选择资产";
        private TradeMarketView _market;
        private int _marketPage;
        private readonly List<TradeMarketView> _markets = new();
        private Action _confirmed;
        private bool _disposed;
        public bool IsVisible => _root.gameObject.activeSelf;

        public CommerceWindow(UnityEngine.Transform parent, Func<GameClient> game)
        {
            _game = game;
            _root = QdaoUguiFactory.CreateStretch("CommerceWindow", parent, Vector4.zero);
            _root.gameObject.AddComponent<Image>().color = new Color(.025f, .10f, .08f, .9f);
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            var frame = QdaoUguiFactory.CreateRect("CommerceFrame", _root, 180, 60, 2200, 960);
            GameplayUiArt.Art(frame, "panel", 0, 0, 2200, 960);
            GameplayUiArt.Text(frame, "聚宝斋 · 全球交易", 36, 16, 1100, 60, 36);
            GameplayUiArt.Button(frame, "关闭", 1990, 20, 160, 58, Hide);
            string[] tabs = { "官方商城", "寄售", "竞拍", "材料市场", "我的订单", "上架与交易" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int tab = i;
                GameplayUiArt.Button(frame, tabs[i], 36 + i * 310, 94, 296, 60, () => SelectTab(tab), fontSize: 27);
            }
            _body = QdaoUguiFactory.CreateRect("CommerceBody", frame, 36, 174, 2120, 660);
            _status = GameplayUiArt.Text(frame, "", 36, 847, 1640, 72, 25);
            _status.richText = false;
            GameplayUiArt.Button(frame, "恢复待确认请求", 1690, 844, 450, 64, () => _client?.RetryPending(), fontSize: 26);
            _root.gameObject.SetActive(false);
        }

        public void Bind(CommerceClient client)
        {
            if (_client != null) { _client.Changed -= Changed; _client.Response -= OnResponse; }
            _client = client;
            if (client != null) { client.Changed += Changed; client.Response += OnResponse; }
            _selection = new TradeAssetSelection(); _selectedName = "尚未选择资产";
            Hide();
        }
        public void Show() { _root.gameObject.SetActive(true); SelectTab(_tab); }
        public void Hide() { _root.gameObject.SetActive(false); _confirmed = null; }
        public void Back() { if (_confirmed != null) SelectTab(_tab); else Hide(); }
        private void Changed()
        {
            _status.text = _client?.Status ?? "请先进入角色。";
            bool enabled = _client != null && !_client.Busy && !_client.RequiresReconnect && !_client.HasPendingWrite;
            foreach (var button in _writeButtons) if (button != null) button.interactable = enabled;
        }
        private void Clear()
        {
            _confirmed = null; _writeButtons.Clear(); GameplayUiArt.Clear(_body);
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
            else ShowSellForm();
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
                Clear(); _nextSku = skus.NextSkuId;
                for (int i = 0; i < skus.Skus.Count; i++)
                {
                    var sku = skus.Skus[i].Clone(); float y = i * 68;
                    Text(sku.Title + " · " + Money(sku.PriceMinor, sku.Currency) + " · 可售 " + sku.Available, 24, y, 1530);
                    Action("购买一份", 1640, y, 370, () => Confirm("购买「" + sku.Title + "」一份，价格 " + Money(sku.PriceMinor, sku.Currency)
                        + "。创建订单后，请在正式支付渠道付款；到账以支付确认和订单状态为准。", () => _client.BuySku(sku.SkuId, 1)), true);
                }
                if (skus.Skus.Count == 0) Text("当前没有可售商品。", 24, 24);
                Pagination();
            }
            else if (response is BrowseCommerceOffersResponse offers && (_tab == 1 || _tab == 2))
            {
                Clear(); _nextCursor = offers.NextCursor;
                for (int i = 0; i < offers.Offers.Count; i++)
                {
                    var offer = offers.Offers[i].Clone(); float y = i * 68;
                    if (offer.Order == null) continue;
                    string description = Describe(offer);
                    Text(description, 24, y, 1510);
                    if (_tab == 1)
                        Action("查看并购买", 1640, y, 370, () => Confirm(description + "\n确认后按该合同原价购买。物品与扣款由服务端托管处理。", () => _client.BuyConsignment(offer.Order.OrderId)), true);
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
                    Text(Kind(order.Kind) + " · " + State(order.State) + " · " + Money(order.Amount, order.Currency), 24, y, 1470);
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
            Changed();
        }

        private void ShowOrder(CommerceOrderResponse response)
        {
            var order = response.Order;
            Clear();
            Text(Kind(order.Kind) + " · " + State(order.State) + "\n订单 " + order.OrderId + "\n金额 " + Money(order.Amount, order.Currency)
                + "\n卖方确认：" + (order.SellerConfirmed ? "是" : "否") + "  买方确认：" + (order.BuyerConfirmed ? "是" : "否"), 24, 0, 1950, 190);
            string terms = "卖方交付：" + DescribeTerms(response.SellerAssets, response.SellerItemInstances, response.SellerPet)
                + "\n买方交付：" + DescribeTerms(response.BuyerAssets, response.BuyerItemInstances, response.BuyerPet);
            if (order.Kind == CommerceOrderKind.PlayerTrade) Text(terms, 24, 200, 1980, 190, 25);
            ulong player = _game()?.PlayerId ?? 0;
            bool canConfirm = order.Kind == CommerceOrderKind.PlayerTrade && order.State == CommerceOrderState.WaitingConfirmation
                && response.SellerAssets != null && response.BuyerAssets != null
                && ((order.BuyerPlayerId == player && !order.BuyerConfirmed && HasTrustedSummary(response.SellerAssets, response.SellerItemInstances, response.SellerPet))
                    || (order.SellerPlayerId == player && order.BuyerConfirmed && !order.SellerConfirmed && HasTrustedSummary(response.BuyerAssets, response.BuyerItemInstances, response.BuyerPet)));
            if (canConfirm)
                Action("核对并确认交易", 24, 410, 360, () => Confirm(terms + "\n确认以上不可变条款？买方确认会托管买方资产；卖方最终确认后执行交换。", () => _client.ConfirmTrade(order.OrderId)), true);
            Action("刷新订单", 414, 410, 300, () => _client.GetOrder(order.OrderId));
            if (order.State == CommerceOrderState.WaitingConfirmation || order.State == CommerceOrderState.AwaitingPayment)
                Action("取消订单", 744, 410, 300, () => Confirm("确认取消该订单？已支付订单由服务端确定退款状态。", () => _client.CancelOrder(order.OrderId)), true);
            if (order.Kind == CommerceOrderKind.Consignment && order.SellerPlayerId == _game()?.PlayerId)
                Action("撤回寄售", 1074, 410, 300, () => Confirm("确认撤回该寄售？资产返回以到账状态为准。", () => _client.CancelConsignment(order.OrderId)), true);
            if (order.State == CommerceOrderState.AwaitingPayment) Text("等待正式支付渠道确认。付款结果会由服务端同步，请稍后刷新订单。", 24, 490, 1980, 90);
            Action("我的订单", 24, 592, 300, () => SelectTab(4));
        }

        private static string DescribeTerms(TradeAssetSelection assets, IEnumerable<TradeOfferInstance> instances, TradeOfferInstance pet)
        {
            var pieces = new List<string>(); var described = new HashSet<uint>(); var guids = new HashSet<ulong>();
            foreach (var item in instances)
            {
                pieces.Add((string.IsNullOrEmpty(item.Name) ? "道具" : item.Name) + " × " + item.Count + (item.Level == 0 ? "" : " · 等级 " + item.Level));
                described.Add(item.ConfigId); guids.Add(item.InstanceId);
            }
            if (assets != null)
            {
                foreach (var item in assets.Items) if (!described.Contains(item.Key)) pieces.Add("材料 " + item.Key + " × " + item.Value);
                foreach (var id in assets.ItemUuids) if (!guids.Contains(id)) pieces.Add("独立道具 " + id + "（待托管核实详情）");
                if (assets.CurrencyAmount != 0) pieces.Add(assets.CurrencyAmount + (assets.CurrencyType == 0 ? " 金币" : " 货币 " + assets.CurrencyType));
                if (assets.PetId != 0 && (pet == null || pet.InstanceId == 0)) pieces.Add("宠物 " + assets.PetId + "（待托管核实详情）");
            }
            if (pet != null && pet.InstanceId != 0) pieces.Add(pet.Name + " · 等级 " + pet.Level);
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
            Text("竞拍 " + lot.LotId + "\n当前出价 " + lot.HighestBid + "  最低加价 " + lot.MinimumIncrement
                + "\n剩余 " + (lot.EndMs > now ? (lot.EndMs - now) / 1000 : 0) + " 秒 · " + lot.State, 24, 20, 1850, 220);
            var amount = Input("出价", 24, 260, 620, minimum.ToString(CultureInfo.InvariantCulture));
            Action("提交出价", 690, 300, 320, () => { if (Positive(amount, out var bid)) Confirm("确认出价 " + bid + "？有效出价将托管对应货币，退回或成交请查看订单。", () => _client.Bid(lot.LotId, bid)); }, true);
            Action("刷新竞拍", 1040, 300, 320, () => _client.GetLot(lot.LotId));
            if (lot.SellerPlayerId == _game()?.PlayerId) Action("撤回竞拍", 1390, 300, 320, () => Confirm("确认撤回此拍品？有有效出价时以服务端规则为准。", () => _client.CancelLot(lot.LotId)), true);
            Action("返回列表", 24, 592, 300, () => SelectTab(2));
        }

        private void ShowSellForm()
        {
            Text("选择自己背包的道具或未出战宠物；可交易范围由服务端配置决定。", 24, 0, 2000);
            Text("已选择：" + _selectedName, 24, 64, 2000);
            Action("从背包选择", 24, 130, 360, () => ShowAssets(false, 0));
            Action("从宠物选择", 414, 130, 360, () => ShowAssets(true, 0));
            var price = Input("价格 / 起拍价（金币）", 24, 220, 500);
            var hours = Input("上架小时数", 554, 220, 350, "24");
            var increment = Input("竞拍最低加价", 934, 220, 350, "1");
            System.Action<bool> publish = auction =>
            {
                if (!HasAssets()) { _client.SetStatus("请先选择要交易的资产。"); return; }
                if (!Positive(price, out var amount) || !Positive(hours, out var hour) || hour > 720) { _client.SetStatus("请填写有效价格及1至720小时的时长。"); return; }
                ulong duration = hour * 3600000;
                ulong step = 0;
                if (auction && !Positive(increment, out step)) return;
                var assets = _selection.Clone();
                Confirm("上架「" + _selectedName + "」，" + (auction ? "起拍价 " : "价格 ") + amount + " 金币，时长 " + hour + " 小时。资产将移入托管。",
                    () => { if (auction) _client.CreateLot(assets, 0, amount, step, duration); else _client.Consign(assets, 0, amount, duration); });
            };
            Action("固定价寄售", 24, 354, 360, () => publish(false), true);
            Action("创建竞拍", 414, 354, 360, () => publish(true), true);
            var target = Input("交易对象角色号", 24, 444, 600);
            var wanted = Input("向对方收取金币", 654, 444, 450);
            Action("发起双方确认交易", 1134, 484, 650, () =>
            {
                if (!HasAssets()) { _client.SetStatus("请先选择要交易的资产。"); return; }
                if (!Positive(target, out var buyer) || !Positive(wanted, out var gold)) return;
                var seller = _selection.Clone();
                Confirm("向角色 " + buyer + " 交付「" + _selectedName + "」，收取 " + gold + " 金币。双方确认后开始托管。",
                    () => _client.CreateTrade(buyer, seller, new TradeAssetSelection { CurrencyType = 0, CurrencyAmount = gold }));
            }, true);
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
                    var button = Action("选择", 1600, y, 350, () => { _selection = new TradeAssetSelection { PetId = pet.PetId }; _selectedName = pet.Name; Clear(); ShowSellForm(); Changed(); });
                    button.interactable = !pet.IsActive;
                }
            }
            else
            {
                var list = game?.Features?.CharacterBag?.Items;
                if (list == null) { game?.Features?.RequestBag(); Text("正在读取背包，请稍后刷新。", 24, 24); }
                else for (int i = start; i < list.Count && i < start + 7; i++)
                {
                    var item = list[i]; float y = (i - start) * 70;
                    Text(item.Name + " × " + item.Count + (item.MaxStack == 1 ? " · 独立实例" : ""), 24, y, 1450);
                    Action("选择", 1600, y, 350, () =>
                    {
                        _selection = new TradeAssetSelection();
                        if (item.MaxStack == 1) _selection.ItemUuids.Add(item.ItemId); else _selection.Items[item.ConfigId] = item.Count;
                        _selectedName = item.Name + " × " + item.Count; Clear(); ShowSellForm(); Changed();
                    });
                }
            }
            Action("上一页", 24, 592, 240, () => ShowAssets(pets, Math.Max(0, page - 1)));
            Action("下一页", 294, 592, 240, () => ShowAssets(pets, page + 1));
            Action("刷新", 564, 592, 240, () => { if (pets) game?.Pets?.RequestList(); else game?.Features?.RequestBag(); ShowAssets(pets, page); });
            Action("返回", 834, 592, 240, () => { Clear(); ShowSellForm(); Changed(); });
        }

        // 市场目录由服务端白名单返回；不让玩家猜测或提交内部编号。
        private void ShowMarket()
        {
            _client.ListMarkets();
        }
        private void ShowMarketCatalog()
        {
            Clear();
            for (int i = _marketPage * 7; i < _markets.Count && i < (_marketPage + 1) * 7; i++)
            {
                var market = _markets[i].Clone(); float y = (i % 7) * 68;
                Text(market.Title, 24, y, 1490);
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
                Text((order.Side == MarketOrderSide.Buy ? "买入" : "卖出") + " · 数量 " + order.Quantity + " · 已成交 " + order.FilledQuantity + " · 单价 " + order.UnitPrice + " · " + order.State, 24, y, 1520);
                Action("撤单", 1640, y, 370, () => Confirm("确认撤回尚未成交的部分？", () => _client.CancelMarket(order.MarketOrderId)), true);
            }
            Action("首页", 24, 592, 260, () => _client.ListMarket());
            Action("下一页", 314, 592, 260, () => _client.ListMarket(_nextCursor));
        }
        private void ShowDepth(GetMarketDepthResponse response)
        {
            Clear(); if (_market == null) return;
            Text(_market.Title + " · 卖盘", 24, 10); Text("买盘", 900, 10);
            for (int i = 0; i < response.Asks.Count; i++) Text(response.Asks[i].UnitPrice + " × " + response.Asks[i].Quantity, 24, 70 + i * 42, 750, 40);
            for (int i = 0; i < response.Bids.Count; i++) Text(response.Bids[i].UnitPrice + " × " + response.Bids[i].Quantity, 900, 70 + i * 42, 750, 40);
            var quantity = Input("数量", 24, 420, 380, "1");
            var price = Input("每件限价", 434, 420, 440);
            var market = _market.Clone();
            System.Action<MarketOrderSide> place = side =>
            {
                if (!Positive(quantity, out var count) || count > uint.MaxValue || !Positive(price, out var unitPrice)) return;
                ulong total; try { total = checked(count * unitPrice); } catch (OverflowException) { _client.SetStatus("订单总价超过支持范围。"); return; }
                Confirm((side == MarketOrderSide.Buy ? "买入" : "卖出") + market.Title + " × " + count + "，每件限价 " + unitPrice + "，总额上限 " + total
                    + "。价格相同时按服务端提交顺序撮合；未成交部分可撤单。", () => _client.PlaceMarket(market.MarketId, market.ItemConfigId, side, (uint)count, unitPrice, market.CurrencyType));
            };
            Action("限价买入", 904, 460, 320, () => place(MarketOrderSide.Buy), true);
            Action("限价卖出", 1254, 460, 320, () => place(MarketOrderSide.Sell), true);
            Action("刷新买卖盘", 1604, 460, 390, () => _client.MarketDepth(market.MarketId, market.ItemConfigId));
            Action("返回市场", 24, 592, 360, ShowMarketCatalog); Changed();
        }
        private static string Describe(CommerceOfferView offer)
        {
            var pieces = new List<string>();
            foreach (var item in offer.ItemInstances) pieces.Add((string.IsNullOrEmpty(item.Name) ? "道具" : item.Name) + " × " + item.Count);
            if (offer.Pet != null && offer.Pet.InstanceId != 0) pieces.Add(offer.Pet.Name + " · 等级 " + offer.Pet.Level);
            if (offer.Assets != null) foreach (var item in offer.Assets.Items) pieces.Add("材料 " + item.Key + " × " + item.Value);
            return string.Join("、", pieces) + " · " + Money(offer.Order.Amount, offer.Order.Currency);
        }
        private static string Money(ulong amount, string currency) => currency == "CNY"
            ? (amount / 100).ToString(CultureInfo.InvariantCulture) + "." + (amount % 100).ToString("D2", CultureInfo.InvariantCulture) + " 元"
            : amount.ToString(CultureInfo.InvariantCulture) + " " + (currency == "0" || currency == "gold" ? "金币" : currency);
        private static string Kind(CommerceOrderKind kind) => kind switch { CommerceOrderKind.Official => "官方商品", CommerceOrderKind.PlayerTrade => "玩家交易", CommerceOrderKind.Consignment => "寄售", CommerceOrderKind.AuctionLot => "竞拍", _ => "材料成交" };
        private static string State(CommerceOrderState state) => state switch {
            CommerceOrderState.WaitingConfirmation => "等待双方确认", CommerceOrderState.AwaitingPayment => "等待付款确认", CommerceOrderState.Escrowing => "资产托管中",
            CommerceOrderState.Delivering => "资产到账中", CommerceOrderState.Delivered => "已到账", CommerceOrderState.CancelPending => "取消处理中",
            CommerceOrderState.Cancelled => "已取消", CommerceOrderState.RefundPending => "退款处理中", CommerceOrderState.Refunded => "已退款",
            CommerceOrderState.Manual => "等待人工核查", CommerceOrderState.Settled => "已结算", _ => "正在确认" };
        public void Dispose() { if (_disposed) return; _disposed = true; Bind(null); UnityEngine.Object.Destroy(_root.gameObject); }
    }
}
