using System;
using System.Collections.Generic;
using System.Globalization;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Net;
using TradePb = Trade;

namespace MmorpgClient.Game.Jubaozhai
{
    /// <summary>
    /// 聚宝斋服务端分页适配器(proto/trade/jubaozhai.proto,service ClientPlayerJubaozhai)。
    /// 会话代次、玩家、连接对象三重校验照 GuildClient,旧回包不能污染换角、重连或换 Gate 之后的会话。与 Guild 的差异:
    /// 1. 同时只发一个请求:GameClient 按消息号先进先出匹配零编号 gRPC 回包,同号请求不能并发。
    ///    在途时查询变化只记脏,回包后按 State 最新查询补发一次;回包携带发送时的 QueryVersion 注入,过期自动丢弃。
    /// 2. 只有 onError 为 "rpc timeout" 时进入 RequiresReconnect 隔离(规格 P1-11):超时分支已删掉在途记录,
    ///    迟到回包会被同连接下一个同号请求误领。信封 tip / 解析失败时回包已被消费,未连接 / 断线时连接已不可用,
    ///    这些错误不会串包,只提示文案。
    /// 请求刻意不带 player_id,身份只取 gate 注入的会话;业务结果读回包体 error_message。
    /// </summary>
    public sealed class JubaozhaiClient : IDisposable
    {
        public const string RecoveryMessage = "聚宝斋请求状态尚未确认，请重新登录角色后再试。";
        public const string LoadingMessage = "正在读取聚宝斋，请稍候…";
        public const string BusyMessage = "请稍候再试";
        public const string AuctionDisabledMessage = "拍卖尚未开放";
        public const string NotInGameMessage = "请进入角色后再打开聚宝斋。";
        public const string TransportFailureMessage = "聚宝斋请求未完成，请稍后重试。";
        // 与 GameClient.Call 超时分支 onError("rpc timeout") 的文字逐字一致;两边改动必须同步。
        public const string TimeoutError = "rpc timeout";

        private readonly IBattleTransport _net;
        private readonly JubaozhaiState _state;
        private readonly Func<object> _connectionIdentity;
        private object _observedConnection;
        private int _generation;
        private bool _disposed;
        private bool _browseDirty;

        public ulong PlayerId => _net.PlayerId;
        public JubaozhaiState State => _state;
        public bool Busy { get; private set; }
        public bool RequiresReconnect { get; private set; }
        public string Status { get; private set; } = "";
        public event Action Changed;

        public JubaozhaiClient(IBattleTransport net, JubaozhaiState state, Func<object> connectionIdentity = null)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _connectionIdentity = connectionIdentity ?? (() => _net);
            _observedConnection = _connectionIdentity();
            _net.Disconnected += HandleDisconnected;
            _state.QueryChanged += Browse;
            _state.FavoriteRequested += OnFavoriteRequested;
            _state.DetailRequested += OnDetailRequested;
        }

        /// <summary>打开窗口时调用:State 进入服务端分页模式,由 QueryChanged 触发浏览请求。</summary>
        public void Open()
        {
            if (_disposed) return;
            ObserveConnection();
            _state.EnterServerPaging();
        }

        public void Reset()
        {
            // 同 GuildClient:在途请求被作废后,它之后的超时回调会被代次校验吞掉,
            // 迟到回包就可能被同连接下一个同号请求误领,所以在途时 Reset 必须隔离到真实断线或换 Gate。
            if (Busy) RequiresReconnect = true;
            ++_generation;
            Busy = false; _browseDirty = false;
            Status = RequiresReconnect ? RecoveryMessage : "";
            Changed?.Invoke();
        }

        public void ObserveConnection()
        {
            object current = _connectionIdentity();
            if (ReferenceEquals(current, _observedConnection)) return;
            // GameClient 静默换 Gate 不发 Disconnected;真实连接对象变化才解除隔离。
            _observedConnection = current;
            Busy = false;
            RequiresReconnect = false;
            Reset();
        }

        private void HandleDisconnected()
        {
            // 仅真实连接断开时,旧连接回包才不可能再进入下一连接。
            _observedConnection = _connectionIdentity();
            Busy = false;
            RequiresReconnect = false;
            Reset();
        }

        /// <summary>按 State 当前查询发浏览请求;Section=Shelf 走 GetMyShelf。在途时只记脏,回包后补发。</summary>
        public void Browse()
        {
            if (_disposed) return;
            if (!_state.ServerPaged) { _browseDirty = false; return; }
            if (Busy) { _browseDirty = true; return; }
            _browseDirty = false;
            int version = _state.QueryVersion;
            var section = _state.Section;
            if (section == JubaozhaiSection.Shelf)
            {
                var shelf = new TradePb.GetMyShelfRequest
                {
                    Page = (uint)Math.Max(1, _state.PageNumber),
                    PageSize = (uint)_state.PageSize
                };
                Request(MessageIds.JubaozhaiGetMyShelf, shelf, TradePb.GetMyShelfResponse.Parser, response =>
                {
                    if (HasTip(response.ErrorMessage)) { FailPage(version, response.ErrorMessage.Id); return; }
                    ApplyPage(version, response.Listings, section, response.TotalCount, response.Page,
                        response.PageCount, response.ServerNowMs);
                }, message => ApplyEmptyPage(version, message, false), LoadingMessage);
                return;
            }
            Request(MessageIds.JubaozhaiBrowseListings, BuildBrowseRequest(_state), TradePb.BrowseListingsResponse.Parser, response =>
            {
                var tip = response.ErrorMessage;
                if (section == JubaozhaiSection.Auction && tip != null && tip.Id == (uint)trade_error.KTradeFeatureDisabled)
                {
                    // 竞价属于后续阶段:显示空页与明确文案,服务仍视为可用。
                    ApplyEmptyPage(version, AuctionDisabledMessage, true);
                    return;
                }
                if (HasTip(tip)) { FailPage(version, tip.Id); return; }
                ApplyPage(version, response.Listings, section, response.TotalCount, response.Page,
                    response.PageCount, response.ServerNowMs);
            }, message => ApplyEmptyPage(version, message, false), LoadingMessage);
        }

        /// <summary>业务 tip 的中文文案;未收录的编号给出带编号的兜底文案。</summary>
        public static string DescribeTip(uint tipId) => tipId switch
        {
            (uint)trade_error.KTradeListingNotFound => "商品已下架或不存在，请刷新列表。",
            (uint)trade_error.KTradeHomeZoneUnknown => "角色所属区服尚未确认，暂时无法使用聚宝斋，请联系管理员。",
            (uint)trade_error.KTradeFavoriteLimitReached => "收藏数量已达上限，请先取消部分收藏。",
            (uint)trade_error.KTradeFeatureDisabled => "该功能尚未开放。",
            (uint)common_error.KInvalidParameter => "请求参数有误，请调整筛选条件后重试。",
            (uint)common_error.KServiceUnavailable => "聚宝斋服务暂时不可用，请稍后重试。",
            _ => $"聚宝斋暂未完成请求（{tipId}），请稍后重试。"
        };

        private void OnFavoriteRequested(string id, bool favorite)
        {
            if (_disposed || !TryParseListingId(id, out ulong listingId)) return;
            // 不排队:收藏是用户显式操作,在途时提示稍后再点,避免与浏览补发交错。
            if (Busy) { ShowMessage(BusyMessage, _state.ServiceAvailable); Changed?.Invoke(); return; }
            Request(MessageIds.JubaozhaiSetFavorite, new TradePb.SetFavoriteRequest { ListingId = listingId, Favorite = favorite },
                TradePb.SetFavoriteResponse.Parser, response =>
                {
                    if (HasTip(response.ErrorMessage))
                    {
                        uint tipId = response.ErrorMessage.Id;
                        ShowMessage(DescribeTip(tipId), MarketUsable(tipId));
                        return;
                    }
                    // 以服务端确认后的状态为准,不做乐观更新。
                    _state.ApplyFavorite(id, response.Favorite);
                    ShowMessage("", true);
                    // 仅看收藏时列表内容随收藏变化,按当前查询重新浏览。
                    if (_state.FavoritesOnly) Browse();
                }, message => ShowMessage(message, _state.ServiceAvailable), null);
        }

        private void OnDetailRequested(string id)
        {
            if (_disposed || !TryParseListingId(id, out ulong listingId)) return;
            if (Busy) { ShowMessage(BusyMessage, _state.ServiceAvailable); Changed?.Invoke(); return; }
            Request(MessageIds.JubaozhaiGetListingDetail, new TradePb.GetListingDetailRequest { ListingId = listingId },
                TradePb.GetListingDetailResponse.Parser, response =>
                {
                    if (HasTip(response.ErrorMessage))
                    {
                        uint tipId = response.ErrorMessage.Id;
                        ShowMessage(DescribeTip(tipId), MarketUsable(tipId));
                        return;
                    }
                    _state.ApplyDetail(id, response.Detail?.Description ?? "");
                    ShowMessage("", true);
                }, message => ShowMessage(message, _state.ServiceAvailable), null);
        }

        private void Request<T>(uint messageId, IMessage request, MessageParser<T> parser, Action<T> success,
            Action<string> failure, string pendingStatus) where T : IMessage<T>
        {
            if (_disposed) return;
            ObserveConnection();
            if (Busy) { failure(BusyMessage); return; }
            if (RequiresReconnect) { Reject(failure, RecoveryMessage); return; }
            if (!_net.IsReady || PlayerId == 0) { Reject(failure, NotInGameMessage); return; }
            int generation = ++_generation;
            ulong player = PlayerId;
            Busy = true;
            Status = pendingStatus ?? "";
            if (pendingStatus != null) _state.SetServiceStatus(pendingStatus, _state.ServiceAvailable);
            Changed?.Invoke();
            _net.Call(messageId, request, parser, response =>
            {
                if (!Current(generation, player)) return;
                Busy = false;
                success(response);
                FlushDirtyBrowse();
                Changed?.Invoke();
            }, error =>
            {
                if (!Current(generation, player)) return;
                Busy = false;
                UnityEngine.Debug.LogWarning($"[Jubaozhai] 请求 {messageId} 失败: {error}");
                if (error == TimeoutError)
                {
                    RequiresReconnect = true;
                    // 隔离后在途期间积压的查询已无法发出:丢弃脏标记,按最新版本落空页结束读取状态,
                    // 否则发送时版本已过期的浏览失败不会写 State,Loading 会一直停在 true。
                    _browseDirty = false;
                    failure(RecoveryMessage);
                    if (_state.ServerPaged && _state.Loading) ApplyEmptyPage(_state.QueryVersion, RecoveryMessage, false);
                    else ShowMessage(RecoveryMessage, false);
                }
                else
                {
                    failure(TransportFailureMessage);
                    FlushDirtyBrowse();
                }
                Changed?.Invoke();
            });
        }

        private bool Current(int generation, ulong player) =>
            !_disposed && generation == _generation && _net.IsReady && PlayerId == player
            && ReferenceEquals(_observedConnection, _connectionIdentity());

        private void Reject(Action<string> failure, string message)
        {
            Status = message;
            failure(message);
            Changed?.Invoke();
        }

        private void FlushDirtyBrowse()
        {
            // 成功回调里可能已经发出新请求(收藏后重新浏览);那时 Busy 仍为 true,脏标记留到下一次回包。
            if (_browseDirty && !Busy && !RequiresReconnect) Browse();
        }

        private void ApplyPage(int version, IEnumerable<TradePb.ListingSummary> summaries, JubaozhaiSection section,
            uint totalCount, uint page, uint pageCount, ulong serverNowMs)
        {
            var items = new List<JubaozhaiListing>();
            foreach (var summary in summaries)
            {
                var listing = ToListing(summary, section);
                if (listing != null) items.Add(listing);
            }
            // 版本过期时查询已变化,脏标记会在回包后按最新查询补发。
            if (_state.ApplyServerPage(version, items, ClampToInt(totalCount), ClampToInt(page), ClampToInt(pageCount),
                    (long)(serverNowMs / 1000UL)))
                Status = "";
        }

        private void FailPage(int version, uint tipId) => ApplyEmptyPage(version, DescribeTip(tipId), MarketUsable(tipId));

        private void ApplyEmptyPage(int version, string message, bool available)
        {
            Status = message;
            // 失败时清空当前页,避免旧条件的条目挂在新筛选下面;过期版本不写,由补发的最新查询覆盖。
            if (_state.ApplyServerPage(version, Array.Empty<JubaozhaiListing>(), 0, 1, 1, 0))
                _state.SetServiceStatus(message, available);
        }

        private void ShowMessage(string message, bool available)
        {
            Status = message;
            _state.SetServiceStatus(message, available);
        }

        // 区服未确认、服务故障时整个市场不可用;其余业务拒绝只影响本次操作。
        private static bool MarketUsable(uint tipId) =>
            tipId != (uint)trade_error.KTradeHomeZoneUnknown && tipId != (uint)common_error.KServiceUnavailable;

        private static bool HasTip(TipInfoMessage tip) => tip != null && tip.Id != 0;

        private static bool TryParseListingId(string id, out ulong listingId) =>
            ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out listingId) && listingId != 0;

        private static int ClampToInt(uint value) => value > int.MaxValue ? int.MaxValue : (int)value;

        private static TradePb.BrowseListingsRequest BuildBrowseRequest(JubaozhaiState state)
        {
            return new TradePb.BrowseListingsRequest
            {
                // 客户端「寄售列表」对应服务端 ON_SALE;zone_filter 只在全服市场生效,客户端暂不提供该筛选。
                Tab = state.Tab == JubaozhaiTab.PublicNotice ? TradePb.ListingTab.PublicNotice : TradePb.ListingTab.OnSale,
                Section = state.Section == JubaozhaiSection.Auction ? TradePb.ListingSection.Auction : TradePb.ListingSection.Consignment,
                // 类目契约(实现规格 §4):proto 值 = 客户端 JubaozhaiCategory 值 + 1。
                Category = (TradePb.ListingCategory)((int)state.Category + 1),
                Subcategory = SubcategoryCode(state.Category, state.Subcategory),
                Search = state.Search,
                Sort = ToProtoSort(state.Sort),
                Page = (uint)Math.Max(1, state.PageNumber),
                PageSize = (uint)state.PageSize,
                FavoritesOnly = state.FavoritesOnly
            };
        }

        // 子类编码:0 = 全部;k = JubaozhaiCatalog.SubcategoriesFor(类目) 的第 k 个标签(1 起)。
        private static uint SubcategoryCode(JubaozhaiCategory category, string label)
        {
            if (string.IsNullOrEmpty(label)) return 0;
            var labels = JubaozhaiCatalog.SubcategoriesFor(category);
            for (int i = 0; i < labels.Count; i++)
                if (labels[i] == label) return (uint)(i + 1);
            return 0;
        }

        private static TradePb.ListingSort ToProtoSort(JubaozhaiSort sort) => sort switch
        {
            JubaozhaiSort.PriceAscending => TradePb.ListingSort.PriceAsc,
            JubaozhaiSort.PriceDescending => TradePb.ListingSort.PriceDesc,
            JubaozhaiSort.LevelDescending => TradePb.ListingSort.LevelDesc,
            JubaozhaiSort.RemainingTimeAscending => TradePb.ListingSort.RemainingAsc,
            _ => TradePb.ListingSort.Default
        };

        private static JubaozhaiListing ToListing(TradePb.ListingSummary summary, JubaozhaiSection section)
        {
            if (summary == null || summary.ListingId == 0) return null;
            // 9(游戏币)客户端暂无页签,与未知类目一起跳过。
            int categoryIndex = (int)summary.Category - 1;
            if (categoryIndex < 0 || categoryIndex >= JubaozhaiCatalog.AllCategories.Count) return null;
            var category = (JubaozhaiCategory)categoryIndex;
            var labels = JubaozhaiCatalog.SubcategoriesFor(category);
            string label = summary.Subcategory >= 1 && summary.Subcategory <= labels.Count
                ? labels[(int)summary.Subcategory - 1] : "";
            bool notice = summary.Phase == TradePb.ListingPhase.PublicNotice;
            return new JubaozhaiListing
            {
                Id = summary.ListingId.ToString(CultureInfo.InvariantCulture),
                Name = summary.Title,
                Category = category,
                Subcategory = label,
                // 角色类目的子类即门派,本地模式按 School 匹配,两处都写。
                School = category == JubaozhaiCategory.Character ? label : "",
                Level = (int)Math.Min(summary.Level, (uint)int.MaxValue),
                Price = summary.PriceFen / 100m,
                Summary = summary.Summary,
                Details = "",
                IconKey = summary.IconKey,
                // 公示中显示距公示结束的时间,其余显示距寄售结束的时间。
                ExpiresAtUnixSeconds = (long)((notice ? summary.NoticeEndMs : summary.SaleEndMs) / 1000UL),
                Tab = notice ? JubaozhaiTab.PublicNotice : JubaozhaiTab.Consignment,
                Section = section,
                Favorite = summary.IsFavorite,
                IsMine = summary.IsMine
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _net.Disconnected -= HandleDisconnected;
            _state.QueryChanged -= Browse;
            _state.FavoriteRequested -= OnFavoriteRequested;
            _state.DetailRequested -= OnDetailRequested;
            _disposed = true; ++_generation; Busy = false; _browseDirty = false;
        }
    }
}
