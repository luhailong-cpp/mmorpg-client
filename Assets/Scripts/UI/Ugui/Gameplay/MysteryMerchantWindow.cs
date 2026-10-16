using System;
using System.Collections.Generic;
using System.Text;
using MmorpgClient.Game.MysteryMerchant;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 神秘商人商店一次渲染要用的全部数据。只是一份快照:窗口不持有网络对象,
    /// 宿主在数据层每次变化后整份重给(<see cref="MysteryMerchantUiRoot.BuildView"/>)。
    /// 默认值(全空)渲染成「行踪不定」。
    /// </summary>
    public struct MysteryMerchantShopView
    {
        public MysteryMerchantPresence Presence;
        /// <summary>货架属于哪一轮。与商品一起构成报价(MysteryMerchantQuote),购买时原样回传给服务端核对。</summary>
        public ulong RoundId;
        /// <summary>商人所在线号(在场时有效),只用于文案。</summary>
        public uint ChannelNo;
        /// <summary>货架;只有商人在本线时才有。null 视为空。</summary>
        public IReadOnlyList<global::MysteryMerchantGoodsInfo> Goods;
        /// <summary>数据层能不能发请求(连着、有角色、不需要重连)。</summary>
        public bool Ready;
        public bool InBattle;
        /// <summary>查询在途或排队:货架还没到时显示「正在打听…」。</summary>
        public bool Loading;
        /// <summary>购买在途的商品 id;不在途为 0。在途期间所有购买按钮都不可点。</summary>
        public uint BuyingGoodsId;
        /// <summary>距离商人离开 / 下一次出现的毫秒数(口径见 MysteryMerchantModels.RemainingMs / UntilNextMs)。</summary>
        public long RemainingMs;
        public long UntilNextMs;
        /// <summary>
        /// 各货币的余额(下标 = 货币类型);null = 未知。**只用于显示**:它是某次快照,不拿它决定按钮能不能点
        /// (理由见 MysteryMerchantModels.EvaluateBuy)。
        /// </summary>
        public IReadOnlyList<ulong> Balances;
        /// <summary>商人在别的线时,「前往」按钮能不能点(切线功能就绪、不在冷却 / 战斗 / 队伍跟随中)。</summary>
        public bool CanTravel;
        /// <summary>「前往」点不了的原因(「该线路已满」「切线冷却中，3秒后…」);"" = 没有要说的。</summary>
        public string TravelBlockedText;
        /// <summary>数据层的状态行(购买进度 / 结论、查询失败);"" = 没有要说的。</summary>
        public string Status;
        public bool StatusIsError;
    }

    /// <summary>
    /// 神秘商人的商店窗口:列出本轮货架(名称、折扣、原价、折后价、限购),点「购买」发出购买意图。
    /// 只读数据 + 意图事件:不碰网络,不做本地假成功;一件商品能不能买完全由传入的视图决定。
    /// 购买是两次点击:第一次把按钮变成「确认」,第二次才发意图 —— 折扣商品一点就扣钱,
    /// 不给模态确认框,但也不让一次误触成交。确认绑定的是第一次点击时看到的**报价**(轮次、价格、货币、物品、数量):
    /// 两次点击之间货架变了,确认作废;两次点击挨得太近(<see cref="ConfirmMinIntervalSeconds"/>,多半是双击)也不算确认。
    /// 版式(设计面 2560×1080,左上原点):居中纸面主框,货架三列 × 两行,超过六件分页。
    /// </summary>
    public sealed class MysteryMerchantWindow
    {
        public const int Columns = 3;
        public const int Rows = 2;
        public const int GoodsPerPage = Columns * Rows;

        // 测试与离线截图按名字找控件。
        public const string BuyButtonPrefix = "MysteryMerchantBuy_";
        public const string CardPrefix = "MysteryMerchantGoods_";
        public const string TravelButtonName = "MysteryMerchantTravel";
        public const string CloseButtonName = "MysteryMerchantClose";
        public const string PreviousPageButtonName = "MysteryMerchantPreviousPage";
        public const string NextPageButtonName = "MysteryMerchantNextPage";
        public const string EmptyLabelName = "MysteryMerchantEmpty";

        public const string TitleText = "神秘商人";
        public const string BuyText = "购买";
        public const string ConfirmText = "确认购买";
        public const string BuyingText = "购买中…";
        public const string SoldOutText = "已购满";
        /// <summary>第一次点击之后至少过这么久,第二次点击才算确认:一次双击不该成交。</summary>
        public const float ConfirmMinIntervalSeconds = 0.4f;
        public const string LoadingText = "正在向神秘商人打听货架…";
        public const string EmptyShelfText = "神秘商人这一轮没有带货。";
        public const string PickHint = "折扣商品每轮限购，商人离开后货架清空。";

        private const float FrameWidth = 1760, FrameHeight = 924;
        private const float ListX = 76, ListY = 164;
        private const float CardWidth = 520, CardHeight = 290, CardGap = 24;
        private const float ListWidth = CardWidth * Columns + CardGap * (Columns - 1);
        private const float ListHeight = CardHeight * Rows + CardGap * (Rows - 1);
        private const float FooterY = 798;

        // 折扣角标用醒目的朱红(与线路面板的「爆满」同色);错误行与背包 / 任务窗同色。
        private static readonly Color DiscountInk = QdaoUguiTheme.Html("#C23B22");
        private static readonly Color ErrorInk = QdaoUguiTheme.Html("#9A442D");

        /// <summary>玩家确认要买(一份)。参数是他第一次点击时看到的那份报价。</summary>
        public event Action<MysteryMerchantQuote> BuyRequested;
        /// <summary>玩家点了「前往 N线」。</summary>
        public event Action TravelRequested;
        public event Action Closed;

        public bool IsVisible => _root.gameObject.activeSelf;
        public int PageIndex { get; private set; }
        public int PageCount { get; private set; } = 1;
        /// <summary>底部状态行此刻显示的文字。</summary>
        public string StatusText => _status.text;
        public bool StatusIsError { get; private set; }
        /// <summary>标题右侧那一行(在哪条线、还剩多久)此刻显示的文字。</summary>
        public string HeadlineText => _headline.text;
        /// <summary>已经点过一次「购买」、正在等第二次点击确认的商品 id;没有为 0。</summary>
        public uint ArmedGoodsId => _armedQuote.GoodsId;

        private readonly RectTransform _root, _list;
        private readonly TMP_Text _headline, _status, _balance, _pageLabel;
        private readonly Button _previous, _next, _travel;
        private readonly TMP_Text _travelLabel;
        private readonly Func<float> _clock;
        private MysteryMerchantShopView _view;
        // 待确认的报价(第一次点击时拍下的)与那一次点击的时刻;default = 没有待确认的。
        private MysteryMerchantQuote _armedQuote;
        private float _armedAt;
        // 列表区上一次画出来的内容的指纹(见 DescribeList);null = 还没画过。指纹没变就不重建卡片。
        private string _listKey;
        // 标题行上一次显示的倒计时秒数:倒计时每帧喂进来,只有整秒变了才重写文字。
        private long _countdownSecondsShown = -1;

        /// <param name="clock">秒表(只用来量两次点击的间隔);缺省用不受时间缩放影响的游戏时钟。测试传假时钟。</param>
        public MysteryMerchantWindow(UnityEngine.Transform parent, Func<float> clock = null)
        {
            _clock = clock ?? (() => Time.unscaledTime);
            _root = QdaoUguiFactory.CreateStretch("MysteryMerchantWindow", parent, Vector4.zero);
            _root.gameObject.SetActive(false);
            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .10f, .08f, .68f);
            shade.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            var frame = QdaoUguiFactory.CreateCenteredRect("MysteryMerchantFrame", _root, FrameWidth, FrameHeight);
            Art(frame, "main_frame", 0, 0, FrameWidth, FrameHeight);
            Art(frame, "title_plate", 62, -21, 480, 114);
            Text(frame, TitleText, 100, -7, 400, 84, 49, Cream, alignment: TextAlignmentOptions.Center);
            _headline = Text(frame, "", 604, 40, 1060, 58, 30, Muted);
            _headline.name = "MysteryMerchantHeadline";

            Art(frame, "content_panel", 56, 150, FrameWidth - 112, 632);
            _list = QdaoUguiFactory.CreateRect("MysteryMerchantShelf", frame, ListX, ListY, ListWidth, ListHeight);

            // 底栏是固定槽位:状态行、余额、翻页、前往、关闭;渲染只改它们的文字与可用性,不重建。
            _status = Text(frame, "", 72, FooterY, 620, 64, 28, Muted);
            _status.name = "MysteryMerchantStatus";
            _balance = Text(frame, "", 700, FooterY, 330, 64, 28, Ink, alignment: TextAlignmentOptions.MidlineRight);
            _balance.name = "MysteryMerchantBalance";
            _previous = Button(frame, "上一页", 1046, FooterY + 4, 130, 56, () => MovePage(-1), fontSize: 24);
            _previous.name = PreviousPageButtonName;
            _pageLabel = Text(frame, "", 1178, FooterY + 4, 90, 56, 24, Muted, alignment: TextAlignmentOptions.Center);
            _pageLabel.name = "MysteryMerchantPage";
            _next = Button(frame, "下一页", 1270, FooterY + 4, 130, 56, () => MovePage(1), fontSize: 24);
            _next.name = NextPageButtonName;
            _travel = Button(frame, "", 1046, FooterY, 440, 64, () => { if (_view.CanTravel) TravelRequested?.Invoke(); },
                primary: true);
            _travel.name = TravelButtonName;
            _travelLabel = _travel.GetComponentInChildren<TMP_Text>(true);
            var close = Button(frame, "关闭", 1498, FooterY, 190, 64, Hide);
            close.name = CloseButtonName;
        }

        /// <summary>换一份视图;只在窗口可见时重画,隐藏时只记下,下次 <see cref="Show"/> 再画。</summary>
        public void SetView(MysteryMerchantShopView view)
        {
            _view = view;
            // 待确认的那件商品不在架上了、此刻已经不能买、或报价变了(换了一轮 / 价格 / 内容):
            // 撤销待确认,免得下一次点击按玩家没看过的内容成交。
            if (_armedQuote.IsValid)
            {
                var armed = FindGoods(_armedQuote.GoodsId);
                if (!CanBuy(armed) || _armedQuote != MysteryMerchantQuote.Of(_view.RoundId, armed)) _armedQuote = default;
            }
            if (IsVisible) Render();
        }

        /// <summary>
        /// 宿主每帧喂进来的倒计时。倒计时走动不会让数据层发变化通知,标题行的时间靠它刷新;
        /// 只有显示出来的整秒变了才重写文字。
        /// </summary>
        public void SetCountdown(long remainingMs, long untilNextMs)
        {
            _view.RemainingMs = remainingMs;
            _view.UntilNextMs = untilNextMs;
            if (!IsVisible) return;
            long shown = CountdownSeconds(_view);
            if (shown != _countdownSecondsShown) RefreshHeadline();
        }

        public void Show()
        {
            if (!IsVisible) { PageIndex = 0; _armedQuote = default; }
            _root.gameObject.SetActive(true);
            Render();
        }

        public void Hide()
        {
            if (!IsVisible) return;
            _armedQuote = default;
            _root.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        // ── 文案(纯函数,测试直接调)────────────────────────────────────────

        /// <summary>标题右侧那一行:商人在哪、还剩多久。</summary>
        public static string DescribeHeadline(MysteryMerchantShopView view)
        {
            switch (view.Presence)
            {
                case MysteryMerchantPresence.Here:
                    return "就在本线 · " + MysteryMerchantModels.FormatCountdown(view.RemainingMs) + " 后离开";
                case MysteryMerchantPresence.Elsewhere:
                    return "正在 " + MysteryMerchantModels.LineName(view.ChannelNo) + " · "
                           + MysteryMerchantModels.FormatCountdown(view.RemainingMs) + " 后离开";
                case MysteryMerchantPresence.Away:
                    if (view.UntilNextMs < 0) return MysteryMerchantModels.AwayText;
                    // 下一次的时刻已经到了、新一轮还没排出来(整点后的头几秒):不显示「00:00 后」。
                    return view.UntilNextMs == 0
                        ? "已经离开 · " + MysteryMerchantModels.ArrivingText
                        : "已经离开 · " + MysteryMerchantModels.FormatCountdown(view.UntilNextMs) + " 后再次出现";
                default:
                    return "";
            }
        }

        /// <summary>货架区没有卡片可画时,中央的占位文字。</summary>
        public static string DescribeEmpty(MysteryMerchantShopView view)
        {
            switch (view.Presence)
            {
                case MysteryMerchantPresence.Here:
                    return view.Loading ? LoadingText : EmptyShelfText;
                case MysteryMerchantPresence.Elsewhere:
                    return "神秘商人正在 " + MysteryMerchantModels.LineName(view.ChannelNo) + "，到那条线才能看到货架。";
                case MysteryMerchantPresence.Away:
                    return view.UntilNextMs >= 0
                        ? "神秘商人已经离开，下次出现时会有公告。"
                        : MysteryMerchantModels.UnknownNextText;
                default:
                    return view.Loading ? LoadingText : MysteryMerchantModels.UnknownNextText;
            }
        }

        /// <summary>
        /// 底部状态行。自上而下第一条命中:数据层留着的失败提示 → 战斗中的说明 → 商人在别的线而「前往」点不了的原因
        /// → 数据层留着的其它文字(购买中 / 购买成功)→ 在本线且有货时的默认提示 → 空。
        /// </summary>
        public static string DescribeStatus(MysteryMerchantShopView view, out bool isError)
        {
            isError = false;
            string status = view.Status ?? "";
            if (status.Length != 0 && view.StatusIsError) { isError = true; return status; }
            if (view.Presence == MysteryMerchantPresence.Here && view.InBattle) return MysteryMerchantModels.InBattleText;
            // 按钮灰着却不说为什么,玩家只能干等:把切线功能给的原因原样摆出来。
            if (view.Presence == MysteryMerchantPresence.Elsewhere && !view.CanTravel
                && !string.IsNullOrEmpty(view.TravelBlockedText)) return view.TravelBlockedText;
            if (status.Length != 0) return status;
            return view.Presence == MysteryMerchantPresence.Here && (view.Goods?.Count ?? 0) > 0 ? PickHint : "";
        }

        /// <summary>
        /// 余额行:货架上用到的每种货币各一段(「银两 12,345」),余额未知的不列。
        /// 没有任何一种可列时返回 ""。
        /// </summary>
        public static string DescribeBalance(MysteryMerchantShopView view)
        {
            if (view.Goods == null || view.Balances == null) return "";
            var text = new StringBuilder();
            var seen = new HashSet<uint>();
            foreach (var goods in view.Goods)
            {
                if (goods == null || !seen.Add(goods.CurrencyType)) continue;
                if (goods.CurrencyType >= (uint)view.Balances.Count) continue;
                if (text.Length != 0) text.Append("   ");
                text.Append(MysteryMerchantModels.CurrencyName(goods.CurrencyType)).Append(' ')
                    .Append(MysteryMerchantModels.FormatAmount(view.Balances[(int)goods.CurrencyType]));
            }
            return text.ToString();
        }

        /// <summary>一张卡片上购买按钮的文字。</summary>
        public static string BuyButtonText(MysteryMerchantBuyBlock block, bool buying, bool armed)
        {
            if (buying) return BuyingText;
            if (block == MysteryMerchantBuyBlock.LimitReached) return SoldOutText;
            return armed ? ConfirmText : BuyText;
        }

        // ── 渲染 ────────────────────────────────────────────────────────────

        private MysteryMerchantBuyBlock BlockOf(global::MysteryMerchantGoodsInfo goods) =>
            MysteryMerchantModels.EvaluateBuy(goods, _view.Presence, _view.Ready, _view.InBattle);

        private bool CanBuy(global::MysteryMerchantGoodsInfo goods) =>
            goods != null && _view.BuyingGoodsId == 0 && BlockOf(goods) == MysteryMerchantBuyBlock.None;

        private global::MysteryMerchantGoodsInfo FindGoods(uint goodsId)
        {
            if (_view.Goods == null) return null;
            foreach (var goods in _view.Goods)
                if (goods != null && goods.GoodsId == goodsId) return goods;
            return null;
        }

        private void MovePage(int delta)
        {
            PageIndex += delta;
            _armedQuote = default;
            Render();
        }

        private void Render()
        {
            var goods = _view.Goods;
            int count = _view.Presence == MysteryMerchantPresence.Here ? goods?.Count ?? 0 : 0;
            PageCount = Math.Max(1, (count + GoodsPerPage - 1) / GoodsPerPage);
            PageIndex = Mathf.Clamp(PageIndex, 0, PageCount - 1);

            // 「前往」只在商人在别的线时出现,它与翻页控件占同一段底栏(那时没有货架,不需要翻页)。
            bool elsewhere = _view.Presence == MysteryMerchantPresence.Elsewhere;
            _travel.gameObject.SetActive(elsewhere);
            if (elsewhere)
            {
                _travel.interactable = _view.CanTravel;
                _travelLabel.text = "前往 " + MysteryMerchantModels.LineName(_view.ChannelNo);
            }
            bool paged = !elsewhere && PageCount > 1;
            _previous.gameObject.SetActive(paged);
            _next.gameObject.SetActive(paged);
            _pageLabel.gameObject.SetActive(paged);
            _previous.interactable = PageIndex > 0;
            _next.interactable = PageIndex + 1 < PageCount;
            _pageLabel.text = (PageIndex + 1) + " / " + PageCount;

            // 数据层每次变化都会整份重给视图(查询发出、应答到达、购买在途 …),多数时候卡片内容没变。
            // 卡片若跟着销毁重建,按下与抬起之间撞上的那次点击就丢了,所以指纹没变就不动它。
            string listKey = DescribeList(count);
            if (listKey != _listKey)
            {
                _listKey = listKey;
                Clear(_list);
                if (count == 0)
                {
                    var empty = Text(_list, DescribeEmpty(_view), 0, (ListHeight - 80) / 2, ListWidth, 80, 36, Muted,
                        alignment: TextAlignmentOptions.Center);
                    empty.name = EmptyLabelName;
                }
                else
                {
                    int start = PageIndex * GoodsPerPage;
                    int shown = Math.Min(GoodsPerPage, count - start);
                    for (int slot = 0; slot < shown; slot++)
                        if (goods[start + slot] != null) BuildCard(goods[start + slot], slot);
                }
            }

            _balance.text = DescribeBalance(_view);
            string status = DescribeStatus(_view, out bool isError);
            StatusIsError = isError;
            _status.text = status;
            _status.color = isError ? ErrorInk : Muted;
            RefreshHeadline();
        }

        /// <summary>
        /// 货架区此刻该画成什么样的指纹:没有卡片时是占位文字;否则是页码加当前页每张卡片的
        /// (商品、折扣、价格、已购 / 限购、按钮文字、能不能点)—— <see cref="BuildCard"/> 用到的输入都在里面
        /// (给 BuildCard 加新的输入时这里要跟着加)。
        /// </summary>
        private string DescribeList(int count)
        {
            // 占位文字是中文句子,下面的卡片指纹以数字开头,两者不会相等。
            if (count == 0) return DescribeEmpty(_view);
            int start = PageIndex * GoodsPerPage;
            int shown = Math.Min(GoodsPerPage, count - start);
            var key = new StringBuilder(48 * shown + 8);
            key.Append(PageIndex);
            for (int slot = 0; slot < shown; slot++)
            {
                var goods = _view.Goods[start + slot];
                key.Append('|');
                if (goods == null) continue;
                var block = BlockOf(goods);
                bool buying = _view.BuyingGoodsId == goods.GoodsId;
                key.Append(goods.GoodsId).Append(',').Append(goods.ItemConfigId).Append(',').Append(goods.ItemCount)
                    .Append(',').Append(goods.DiscountPercent).Append(',').Append(goods.OriginalPrice)
                    .Append(',').Append(goods.Price).Append(',').Append(goods.CurrencyType)
                    .Append(',').Append(goods.BoughtCount).Append('/').Append(goods.LimitCount)
                    .Append(',').Append(goods.Name).Append(',').Append(goods.IconKey)
                    .Append(',').Append((int)block)
                    .Append(buying ? 'b' : '-')
                    .Append(_view.BuyingGoodsId != 0 ? 'p' : '-')
                    .Append(ArmedGoodsId == goods.GoodsId ? 'a' : '-');
            }
            return key.ToString();
        }

        private void BuildCard(global::MysteryMerchantGoodsInfo goods, int slot)
        {
            float x = slot % Columns * (CardWidth + CardGap);
            float y = slot / Columns * (CardHeight + CardGap);
            var card = QdaoUguiFactory.CreateRect(CardPrefix + goods.GoodsId, _list, x, y, CardWidth, CardHeight);
            Art(card, "paper_tile", 0, 0, CardWidth, CardHeight);

            ItemIcon(card, goods.IconKey, 22, 22, 112);
            Text(card, MysteryMerchantModels.GoodsName(goods), 150, 18, 250, 46, 32);
            var discount = Text(card, MysteryMerchantModels.DiscountLabel(goods.DiscountPercent), 396, 14, 104, 54, 38,
                DiscountInk, alignment: TextAlignmentOptions.MidlineRight);
            discount.name = "Discount";
            Text(card, "每份 ×" + goods.ItemCount, 150, 66, 340, 34, 24, Muted);
            string currency = MysteryMerchantModels.CurrencyName(goods.CurrencyType);
            Text(card, "原价 " + MysteryMerchantModels.FormatAmount(goods.OriginalPrice), 150, 102, 340, 34, 24, Muted);
            var price = Text(card, MysteryMerchantModels.FormatAmount(goods.Price) + " " + currency, 150, 138, 350, 50, 36, Gold);
            price.name = "Price";
            Text(card, "限购 " + goods.BoughtCount + "/" + goods.LimitCount, 24, 216, 250, 50, 26,
                MysteryMerchantModels.Remaining(goods) == 0 ? Muted : Ink).name = "Limit";

            var block = BlockOf(goods);
            bool buying = _view.BuyingGoodsId == goods.GoodsId;
            bool armed = ArmedGoodsId == goods.GoodsId;
            uint goodsId = goods.GoodsId;
            var buy = Button(card, BuyButtonText(block, buying, armed), 290, 210, 208, 62, () => ClickBuy(goodsId),
                primary: armed, enabled: CanBuy(goods), fontSize: 28);
            buy.name = BuyButtonPrefix + goods.GoodsId;
        }

        private void ClickBuy(uint goodsId)
        {
            // 点击时按当前视图再核一遍:这件可能刚被买满,或另一件正在购买中。
            var goods = FindGoods(goodsId);
            if (!CanBuy(goods)) return;
            var quote = MysteryMerchantQuote.Of(_view.RoundId, goods);
            float now = _clock();
            if (_armedQuote != quote)
            {
                // 第一次点击(或报价已经不是上次点的那一份):只把按钮变成「确认购买」,并记下此刻看到的报价。
                _armedQuote = quote;
                _armedAt = now;
                Render();
                return;
            }
            // 第二次点击来得太快:多半是双击,不算确认(保持待确认,再点一次即可)。
            if (!(now - _armedAt >= ConfirmMinIntervalSeconds)) return;
            _armedQuote = default;
            BuyRequested?.Invoke(quote);
            Render();
        }

        private static long CountdownSeconds(MysteryMerchantShopView view)
        {
            long ms = view.Presence == MysteryMerchantPresence.Away ? view.UntilNextMs : view.RemainingMs;
            return ms <= 0 ? ms : (ms + 999) / 1000;
        }

        private void RefreshHeadline()
        {
            _headline.text = DescribeHeadline(_view);
            _countdownSecondsShown = CountdownSeconds(_view);
        }
    }
}
