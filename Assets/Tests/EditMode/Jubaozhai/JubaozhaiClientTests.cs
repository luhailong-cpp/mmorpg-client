using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Jubaozhai;
using MmorpgClient.Net;
using NUnit.Framework;
using TradePb = Trade;

namespace MmorpgClient.Tests.EditMode.Jubaozhai
{
    /// <summary>
    /// JubaozhaiClient 经假传输层的行为(聚宝斋 P1 实现规格 §10.2 / P1-11)。
    /// 断言只用生成的消息号与 tip 枚举,不写数字。
    /// </summary>
    public sealed class JubaozhaiClientTests
    {
        private const ulong ServerNowMs = 1_800_000_000_000UL;
        private JubaozhaiFakeTransport _net;
        private JubaozhaiState _state;
        private JubaozhaiClient _client;

        [SetUp]
        public void SetUp()
        {
            _net = new JubaozhaiFakeTransport();
            _state = new JubaozhaiState(clock: () => (long)(ServerNowMs / 1000UL));
            _client = new JubaozhaiClient(_net, _state);
        }

        [TearDown] public void TearDown() => _client.Dispose();

        [Test]
        public void OpenSendsBrowseMatchingTheDefaultQuery_WithoutAnyPlayerId()
        {
            _client.Open();
            Assert.That(_net.Calls, Is.EqualTo(new[] { MessageIds.JubaozhaiBrowseListings }));
            var request = (TradePb.BrowseListingsRequest)_net.LastRequest;
            Assert.That(request.Tab, Is.EqualTo(TradePb.ListingTab.OnSale));
            Assert.That(request.Section, Is.EqualTo(TradePb.ListingSection.Consignment));
            Assert.That(request.Category, Is.EqualTo(TradePb.ListingCategory.Character));
            Assert.That(request.Subcategory, Is.Zero);
            Assert.That(request.Search, Is.Empty);
            Assert.That(request.Sort, Is.EqualTo(TradePb.ListingSort.Default));
            Assert.That(request.Page, Is.EqualTo(1));
            Assert.That(request.PageSize, Is.EqualTo(_state.PageSize));
            Assert.That(request.ZoneFilter, Is.Zero);
            Assert.That(request.FavoritesOnly, Is.False);
            // 身份只取 gate 会话:请求协议里根本没有 player_id 字段。
            Assert.That(TradePb.BrowseListingsRequest.Descriptor.Fields.InDeclarationOrder().Any(f => f.Name.Contains("player")), Is.False);
            Assert.That(_client.Busy, Is.True);
            Assert.That(_state.Loading, Is.True);
        }

        [Test]
        public void QueryFieldsAndServerPageNumbersRoundTrip()
        {
            _client.Open();
            _state.SetCategory(JubaozhaiCategory.SummoningOrder);
            _state.SetSubcategory("元灵召唤令");
            _state.SetTab(JubaozhaiTab.PublicNotice);
            _state.SetSearch("  召唤令 ");
            _state.SetFavoritesOnly(true);
            _net.Reply(Page());
            var request = (TradePb.BrowseListingsRequest)_net.LastRequest;
            Assert.That(request.Category, Is.EqualTo(TradePb.ListingCategory.SummoningOrder));
            Assert.That(request.Subcategory, Is.EqualTo(2));
            Assert.That(request.Tab, Is.EqualTo(TradePb.ListingTab.PublicNotice));
            Assert.That(request.Section, Is.EqualTo(TradePb.ListingSection.Consignment));
            Assert.That(request.Search, Is.EqualTo("召唤令"));
            Assert.That(request.FavoritesOnly, Is.True);
            var paged = Page();
            paged.Page = 2; paged.PageCount = 3; paged.TotalCount = 9;
            _net.Reply(paged);
            Assert.That(_state.PageNumber, Is.EqualTo(2));
            Assert.That(_state.GetPage().TotalCount, Is.EqualTo(9));
            _state.SetPage(3);
            Assert.That(((TradePb.BrowseListingsRequest)_net.LastRequest).Page, Is.EqualTo(3));
        }

        [TestCase(JubaozhaiCategory.Character, "丹心", TradePb.ListingCategory.Character, 4u)]
        [TestCase(JubaozhaiCategory.Pet, "其他", TradePb.ListingCategory.Pet, 6u)]
        [TestCase(JubaozhaiCategory.Weapon, "枪", TradePb.ListingCategory.Weapon, 1u)]
        [TestCase(JubaozhaiCategory.Armor, "鞋子", TradePb.ListingCategory.Armor, 5u)]
        [TestCase(JubaozhaiCategory.Jewelry, "", TradePb.ListingCategory.Jewelry, 0u)]
        public void CategoryAndSubcategoryFollowTheSharedCodeTable(JubaozhaiCategory category, string label,
            TradePb.ListingCategory expectedCategory, uint expectedSubcategory)
        {
            _client.Open();
            _state.SetCategory(category);
            _state.SetSubcategory(label);
            _net.Reply(Page());
            var request = (TradePb.BrowseListingsRequest)_net.LastRequest;
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(request.Category, Is.EqualTo(expectedCategory));
            Assert.That(request.Subcategory, Is.EqualTo(expectedSubcategory));
        }

        [TestCase(JubaozhaiSort.PriceAscending, TradePb.ListingSort.PriceAsc)]
        [TestCase(JubaozhaiSort.PriceDescending, TradePb.ListingSort.PriceDesc)]
        [TestCase(JubaozhaiSort.LevelDescending, TradePb.ListingSort.LevelDesc)]
        [TestCase(JubaozhaiSort.RemainingTimeAscending, TradePb.ListingSort.RemainingAsc)]
        public void SortMapsOneToOne(JubaozhaiSort sort, TradePb.ListingSort expected)
        {
            _client.Open();
            _state.SetSort(sort);
            _net.Reply(Page());
            Assert.That(((TradePb.BrowseListingsRequest)_net.LastRequest).Sort, Is.EqualTo(expected));
        }

        [Test]
        public void QueryChangesWhileInFlightSendOnlyTheLatestQueryOnce()
        {
            _client.Open();
            _state.SetCategory(JubaozhaiCategory.Pet);
            _state.SetSearch("灵蛙");
            _state.SetSort(JubaozhaiSort.PriceAscending);
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "在途时不得并发同号请求");
            // 旧查询的回包:版本过期不写入,随后按最新查询补发一次。
            _net.Reply(Page(Summary(1)));
            Assert.That(_state.GetPage().Items, Is.Empty);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            var latest = (TradePb.BrowseListingsRequest)_net.LastRequest;
            Assert.That(latest.Category, Is.EqualTo(TradePb.ListingCategory.Pet));
            Assert.That(latest.Search, Is.EqualTo("灵蛙"));
            Assert.That(latest.Sort, Is.EqualTo(TradePb.ListingSort.PriceAsc));
            _net.Reply(Page(Summary(2, TradePb.ListingCategory.Pet)));
            Assert.That(_state.GetPage().Items.Single().Id, Is.EqualTo("2"));
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(_client.Busy, Is.False);
            Assert.That(_state.Loading, Is.False);
        }

        [Test]
        public void SummariesMapToListings_AndCurrencyOrUnknownCategoriesAreSkipped()
        {
            _client.Open();
            var role = Summary(11, favorite: true);
            role.Subcategory = 2; role.PriceFen = 12345; role.Level = 88; role.IsMine = true;
            role.Title = "青云小道"; role.Summary = "玄霄 · 88级"; role.IconKey = "character_01";
            role.Phase = TradePb.ListingPhase.PublicNotice;
            role.NoticeEndMs = ServerNowMs + 3_600_000; role.SaleEndMs = ServerNowMs + 90_000_000;
            var currency = Summary(12, TradePb.ListingCategory.Currency);
            var unknown = Summary(13, (TradePb.ListingCategory)42);
            var weapon = Summary(14, TradePb.ListingCategory.Weapon);
            weapon.Subcategory = 9; // 越界子类:条目保留,标签留空
            _net.Reply(Page(role, currency, unknown, weapon));

            var items = _state.GetPage().Items;
            Assert.That(items.Select(item => item.Id), Is.EqualTo(new[] { "11", "14" }));
            var mapped = items[0];
            Assert.That(mapped.Category, Is.EqualTo(JubaozhaiCategory.Character));
            Assert.That(mapped.Subcategory, Is.EqualTo("玄霄"));
            Assert.That(mapped.School, Is.EqualTo("玄霄"));
            Assert.That(mapped.Name, Is.EqualTo("青云小道"));
            Assert.That(mapped.Summary, Is.EqualTo("玄霄 · 88级"));
            Assert.That(mapped.IconKey, Is.EqualTo("character_01"));
            Assert.That(mapped.Level, Is.EqualTo(88));
            Assert.That(mapped.Price, Is.EqualTo(123.45m));
            Assert.That(mapped.Tab, Is.EqualTo(JubaozhaiTab.PublicNotice));
            Assert.That(mapped.Section, Is.EqualTo(JubaozhaiSection.Consignment));
            Assert.That(mapped.ExpiresAtUnixSeconds, Is.EqualTo((long)((ServerNowMs + 3_600_000) / 1000)));
            Assert.That(mapped.Favorite, Is.True);
            Assert.That(mapped.IsMine, Is.True);
            Assert.That(_state.IsFavorite("11"), Is.True);
            Assert.That(_state.RemainingTime(mapped), Is.EqualTo("1时0分"));
            Assert.That(items[1].Subcategory, Is.Empty);
            Assert.That(items[1].School, Is.Empty);
            Assert.That(items[1].Tab, Is.EqualTo(JubaozhaiTab.Consignment));
            Assert.That(items[1].ExpiresAtUnixSeconds, Is.EqualTo((long)(weapon.SaleEndMs / 1000)));
        }

        [Test]
        public void ShelfSectionUsesGetMyShelf()
        {
            OpenAndReply();
            _state.SetSection(JubaozhaiSection.Shelf);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.JubaozhaiGetMyShelf));
            var request = (TradePb.GetMyShelfRequest)_net.LastRequest;
            Assert.That(request.Page, Is.EqualTo(1));
            Assert.That(request.PageSize, Is.EqualTo(_state.PageSize));
            var mine = Summary(7, TradePb.ListingCategory.Weapon);
            mine.IsMine = true;
            _net.Reply(new TradePb.GetMyShelfResponse
            {
                TotalCount = 1, Page = 1, PageSize = 4, PageCount = 1, ServerNowMs = ServerNowMs, Listings = { mine }
            });
            var item = _state.GetPage().Items.Single();
            Assert.That(item.Id, Is.EqualTo("7"));
            Assert.That(item.IsMine, Is.True);
            Assert.That(item.Section, Is.EqualTo(JubaozhaiSection.Shelf));
        }

        [Test]
        public void AuctionSectionShowsEmptyPageAndNotOpenMessage()
        {
            OpenAndReply(Summary(1));
            _state.SetSection(JubaozhaiSection.Auction);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.JubaozhaiBrowseListings));
            Assert.That(((TradePb.BrowseListingsRequest)_net.LastRequest).Section, Is.EqualTo(TradePb.ListingSection.Auction));
            _net.Reply(new TradePb.BrowseListingsResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)trade_error.KTradeFeatureDisabled }
            });
            Assert.That(_state.GetPage().Items, Is.Empty);
            Assert.That(_state.Status, Is.EqualTo(JubaozhaiClient.AuctionDisabledMessage));
            Assert.That(_state.Status, Is.EqualTo("拍卖尚未开放"));
            Assert.That(_state.ServiceAvailable, Is.True);
            Assert.That(_state.Loading, Is.False);
            Assert.That(_client.RequiresReconnect, Is.False);
        }

        [TestCase((uint)trade_error.KTradeHomeZoneUnknown, "区服", false)]
        [TestCase((uint)common_error.KServiceUnavailable, "暂时不可用", false)]
        [TestCase((uint)common_error.KInvalidParameter, "参数", true)]
        [TestCase((uint)trade_error.KTradeListingNotFound, "下架", true)]
        public void BusinessTipInResponseBodyShowsTextWithoutQuarantine(uint tip, string text, bool available)
        {
            OpenAndReply(Summary(1));
            _state.SetSearch("换条件");
            _net.Reply(new TradePb.BrowseListingsResponse { ErrorMessage = new TipInfoMessage { Id = tip } });
            Assert.That(_state.Status, Does.Contain(text));
            Assert.That(_state.ServiceAvailable, Is.EqualTo(available));
            Assert.That(_state.GetPage().Items, Is.Empty, "失败时不能把旧条件的条目挂在新筛选下");
            Assert.That(_state.Loading, Is.False);
            Assert.That(_client.RequiresReconnect, Is.False);
            Assert.That(_client.Busy, Is.False);
            _client.Browse();
            Assert.That(_net.Calls.Count, Is.EqualTo(3));
        }

        [Test]
        public void UnknownTipFallsBackToNumberedMessage()
        {
            Assert.That(JubaozhaiClient.DescribeTip(99999), Does.Contain("聚宝斋暂未完成请求"));
            Assert.That(JubaozhaiClient.DescribeTip(99999), Does.Contain("99999"));
            Assert.That(JubaozhaiClient.DescribeTip((uint)trade_error.KTradeFavoriteLimitReached), Does.Contain("上限"));
        }

        [Test]
        public void TimeoutQuarantinesUntilRealDisconnect()
        {
            // 与 GameClient.Call 超时分支的 onError 文字逐字一致。
            Assert.That(JubaozhaiClient.TimeoutError, Is.EqualTo("rpc timeout"));
            OpenAndReply(Summary(1));
            _state.SetSearch("超时");
            _net.Error("rpc timeout");
            Assert.That(_client.RequiresReconnect, Is.True);
            Assert.That(_client.Busy, Is.False);
            Assert.That(_state.Status, Is.EqualTo(JubaozhaiClient.RecoveryMessage));
            Assert.That(_state.Loading, Is.False);
            int before = _net.Calls.Count;
            _state.SetSearch("隔离中");
            _client.Browse();
            Assert.That(_net.Calls.Count, Is.EqualTo(before), "隔离期间不得发出任何请求");
            _client.Reset();
            Assert.That(_client.RequiresReconnect, Is.True);
            _net.Disconnect(); _net.IsReady = true;
            Assert.That(_client.RequiresReconnect, Is.False);
            _client.Browse();
            Assert.That(_net.Calls.Count, Is.EqualTo(before + 1));
            Assert.That(_client.Busy, Is.True);
        }

        [Test]
        public void TimeoutWithQueuedQueryEndsLoadingWithoutSendingTheQueuedQuery()
        {
            OpenAndReply(Summary(1));
            _state.SetSearch("第一次");
            _state.SetSearch("在途期间又改");
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            // 超时的是发送时已过期的查询:隔离后不得补发,且最新查询的读取状态必须结束。
            _net.Error(JubaozhaiClient.TimeoutError);
            Assert.That(_client.RequiresReconnect, Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(_state.Loading, Is.False);
            Assert.That(_state.Status, Is.EqualTo(JubaozhaiClient.RecoveryMessage));
            Assert.That(_state.ServiceAvailable, Is.False);
            Assert.That(_state.GetPage().Items, Is.Empty);
        }

        [Test]
        public void FavoriteTimeoutQuarantinesButKeepsTheCurrentPage()
        {
            OpenAndReply(Summary(1));
            _state.ToggleFavorite("1");
            _net.Error(JubaozhaiClient.TimeoutError);
            Assert.That(_client.RequiresReconnect, Is.True);
            Assert.That(_state.IsFavorite("1"), Is.False);
            Assert.That(_state.GetPage().Items.Single().Id, Is.EqualTo("1"));
            Assert.That(_state.Status, Is.EqualTo(JubaozhaiClient.RecoveryMessage));
            int before = _net.Calls.Count;
            _state.ToggleFavorite("1");
            Assert.That(_net.Calls.Count, Is.EqualTo(before), "隔离期间收藏也不得发出");
        }

        [TestCase("server tip=1003")]
        [TestCase("parse response: bad payload")]
        [TestCase("not connected")]
        [TestCase("disconnected")]
        [TestCase("send failed: broken pipe")]
        public void NonTimeoutErrorsOnlyReportAndDoNotQuarantine(string error)
        {
            _client.Open();
            _net.Error(error);
            Assert.That(_client.RequiresReconnect, Is.False);
            Assert.That(_client.Busy, Is.False);
            Assert.That(_state.Status, Is.EqualTo(JubaozhaiClient.TransportFailureMessage));
            Assert.That(_state.Loading, Is.False);
            _client.Browse();
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(_client.Busy, Is.True);
        }

        [Test]
        public void LateReplyAfterResetIsNotAppliedAndQuarantines()
        {
            _client.Open();
            var stale = _net.Pending;
            _client.Reset();
            Assert.That(_client.RequiresReconnect, Is.True, "在途 Reset 与 GuildClient 一致:零编号回包槽位不得复用");
            stale(Page(Summary(1)));
            Assert.That(_state.GetPage().Items, Is.Empty);
        }

        [Test]
        public void ReplyAfterPlayerSwitchIsRejected()
        {
            _client.Open();
            _net.PlayerId = 99;
            _net.Reply(Page(Summary(1)));
            Assert.That(_state.GetPage().Items, Is.Empty);
        }

        [Test]
        public void NotInGameRejectsWithoutSending()
        {
            _net.IsReady = false;
            _client.Open();
            Assert.That(_net.Calls, Is.Empty);
            Assert.That(_client.Busy, Is.False);
            Assert.That(_state.Status, Is.EqualTo(JubaozhaiClient.NotInGameMessage));
            Assert.That(_state.Loading, Is.False);
        }

        [Test]
        public void OldConnectionReplyCannotApplyAfterConnectionIdentityChanges()
        {
            object identity = new object();
            var net = new JubaozhaiFakeTransport();
            var state = new JubaozhaiState();
            using var client = new JubaozhaiClient(net, state, () => identity);
            client.Open();
            var oldCallback = net.Pending;
            identity = new object();
            oldCallback(Page(Summary(1)));
            Assert.That(state.GetPage().Items, Is.Empty);
            client.ObserveConnection();
            Assert.That(client.Busy, Is.False);
            Assert.That(client.RequiresReconnect, Is.False);
            client.Browse();
            Assert.That(net.Calls.Count, Is.EqualTo(2));
        }

        [Test]
        public void SilentGameClientGateReplacementClearsQuarantineWithoutDisconnectNotification()
        {
            var game = new MmorpgClient.Game.GameClient("http://127.0.0.1:1");
            var field = typeof(MmorpgClient.Game.GameClient).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic);
            var reset = typeof(MmorpgClient.Game.GameClient).GetMethod("ResetConnectionState", BindingFlags.Instance | BindingFlags.NonPublic);
            int notifications = 0;
            game.OnDisconnected += () => notifications++;
            JubaozhaiClient client = null;
            try
            {
                field.SetValue(game, new GateTcpClient(new MuduoCodec()));
                var net = new JubaozhaiFakeTransport();
                client = new JubaozhaiClient(net, new JubaozhaiState(), () => game.GateConnectionIdentity);
                client.Open();
                net.Error("rpc timeout");
                Assert.That(client.RequiresReconnect, Is.True);
                reset.Invoke(game, null); // 与 EnterZone/RedirectFlow 相同的 notify:false 路径。
                Assert.That(notifications, Is.Zero);
                field.SetValue(game, new GateTcpClient(new MuduoCodec()));
                client.ObserveConnection();
                Assert.That(client.RequiresReconnect, Is.False);
                client.Browse();
                Assert.That(net.Calls.Count, Is.EqualTo(2));
                Assert.That(client.Busy, Is.True);
            }
            finally
            {
                client?.Dispose();
                game.Disconnect();
                if (game.World.Root != null) UnityEngine.Object.DestroyImmediate(game.World.Root.gameObject);
            }
        }

        [Test]
        public void FavoriteIsSentAndAppliedOnlyAfterServerConfirmation()
        {
            OpenAndReply(Summary(1));
            Assert.That(_state.ToggleFavorite("1"), Is.False);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.JubaozhaiSetFavorite));
            var request = (TradePb.SetFavoriteRequest)_net.LastRequest;
            Assert.That(request.ListingId, Is.EqualTo(1));
            Assert.That(request.Favorite, Is.True);
            Assert.That(_state.IsFavorite("1"), Is.False, "不做乐观更新");
            _net.Reply(new TradePb.SetFavoriteResponse { ListingId = 1, Favorite = true });
            Assert.That(_state.IsFavorite("1"), Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(2), "非仅看收藏时不重新浏览");
            Assert.That(_client.Busy, Is.False);
        }

        [Test]
        public void FavoriteWhileBusyAsksToRetry_AndFavoritesOnlyRebrowsesAfterConfirmation()
        {
            OpenAndReply(Summary(1, favorite: true));
            _state.SetFavoritesOnly(true);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(_state.ToggleFavorite("1"), Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(2), "在途时收藏不排队");
            Assert.That(_state.Status, Is.EqualTo(JubaozhaiClient.BusyMessage));
            _net.Reply(Page(Summary(1, favorite: true)));

            _state.ToggleFavorite("1");
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.JubaozhaiSetFavorite));
            Assert.That(((TradePb.SetFavoriteRequest)_net.LastRequest).Favorite, Is.False);
            _net.Reply(new TradePb.SetFavoriteResponse { ListingId = 1, Favorite = false });
            Assert.That(_state.IsFavorite("1"), Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(4));
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.JubaozhaiBrowseListings));
            Assert.That(((TradePb.BrowseListingsRequest)_net.LastRequest).FavoritesOnly, Is.True);
            _net.Reply(Page());
            Assert.That(_state.GetPage().Items, Is.Empty);
        }

        [Test]
        public void FavoriteRejectionShowsTextAndKeepsState()
        {
            OpenAndReply(Summary(1));
            _state.ToggleFavorite("1");
            _net.Reply(new TradePb.SetFavoriteResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)trade_error.KTradeFavoriteLimitReached }
            });
            Assert.That(_state.IsFavorite("1"), Is.False);
            Assert.That(_state.Status, Does.Contain("上限"));
            Assert.That(_client.RequiresReconnect, Is.False);
            Assert.That(_state.GetPage().Items.Single().Id, Is.EqualTo("1"));
        }

        [Test]
        public void DetailIsFetchedOnceAndWrittenBack()
        {
            OpenAndReply(Summary(5));
            _state.RequestDetail("5");
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.JubaozhaiGetListingDetail));
            Assert.That(((TradePb.GetListingDetailRequest)_net.LastRequest).ListingId, Is.EqualTo(5));
            _net.Reply(new TradePb.GetListingDetailResponse
            {
                Detail = new TradePb.ListingDetail { Summary = Summary(5), Description = "服务端描述" },
                ServerNowMs = ServerNowMs
            });
            Assert.That(_state.GetPage().Items.Single().Details, Is.EqualTo("服务端描述"));
            int calls = _net.Calls.Count;
            _state.RequestDetail("5");
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));
        }

        [Test]
        public void DetailNotFoundShowsTextWithoutQuarantine()
        {
            OpenAndReply(Summary(5));
            _state.RequestDetail("5");
            _net.Reply(new TradePb.GetListingDetailResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)trade_error.KTradeListingNotFound }
            });
            Assert.That(_state.Status, Does.Contain("下架"));
            Assert.That(_client.RequiresReconnect, Is.False);
            Assert.That(_client.Busy, Is.False);
        }

        [Test]
        public void DisposedClientStopsListeningToState()
        {
            _client.Dispose();
            _state.EnterServerPaging();
            Assert.That(_net.Calls, Is.Empty);
        }

        private void OpenAndReply(params TradePb.ListingSummary[] listings)
        {
            _client.Open();
            _net.Reply(Page(listings));
        }

        private static TradePb.BrowseListingsResponse Page(params TradePb.ListingSummary[] listings)
        {
            var response = new TradePb.BrowseListingsResponse
            {
                TotalCount = (uint)listings.Length, Page = 1, PageSize = 4, PageCount = 1,
                MarketScope = TradePb.MarketScope.Zone, ServerNowMs = ServerNowMs
            };
            response.Listings.AddRange(listings);
            return response;
        }

        private static TradePb.ListingSummary Summary(ulong id,
            TradePb.ListingCategory category = TradePb.ListingCategory.Character, bool favorite = false)
            => new()
            {
                ListingId = id, Category = category, Title = "商品" + id, Level = 80, PriceFen = 10000,
                Phase = TradePb.ListingPhase.OnSale, NoticeEndMs = ServerNowMs - 1000, SaleEndMs = ServerNowMs + 86_400_000,
                MarketZone = 1, IsFavorite = favorite, Summary = "摘要", IconKey = "weapon_01"
            };
    }

    /// <summary>复制自 Guild 测试的假传输层(那边是 internal,跨程序集不可见),多记录 LastRequest 以断言请求字段。</summary>
    internal sealed class JubaozhaiFakeTransport : IBattleTransport
    {
        public ulong PlayerId { get; set; } = 1;
        public bool IsReady { get; set; } = true;
        public event Action Disconnected;
        public List<uint> Calls { get; } = new();
        public IMessage LastRequest;
        public Action<IMessage> Pending;
        public Action<string> Error;
        public void Reply(IMessage response) => Pending(response);
        public void Disconnect() { IsReady = false; Disconnected?.Invoke(); }
        public void RegisterNotify(uint id, Action<MessageContent> action) { }
        public void SendOneWay(uint id, IMessage request) { }
        public void Call<T>(uint id, IMessage request, MessageParser<T> parser, Action<T> success, Action<string> error)
            where T : IMessage<T>
        {
            Calls.Add(id);
            LastRequest = request;
            Pending = message => success((T)message);
            Error = error;
        }
    }
}
