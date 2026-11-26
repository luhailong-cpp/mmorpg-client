using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.SpiritShop;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Role
{
    public sealed class SpiritShopClientTests
    {
        private sealed class FakeTransport : IBattleTransport
        {
            public ulong PlayerId { get; set; } = 8101;
            public bool IsReady { get; set; } = true;
            public event Action Disconnected;

            public readonly List<(uint msgId, IMessage req)> Calls = new();
            private readonly Queue<Action> _pending = new();
            public Func<uint, IMessage, (IMessage resp, string err)> Responder;

            public void RegisterNotify(uint messageId, Action<MessageContent> handler) { }
            public void SendOneWay(uint messageId, IMessage request) { }

            public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                Action<TResp> onResponse, Action<string> onError) where TResp : IMessage<TResp>
            {
                Calls.Add((messageId, request));
                var (resp, err) = Responder(messageId, request);
                if (err != null) _pending.Enqueue(() => onError(err));
                else _pending.Enqueue(() => onResponse((TResp)resp));
            }

            public void Flush()
            {
                while (_pending.Count > 0) _pending.Dequeue().Invoke();
            }

            public void TriggerDisconnect()
            {
                IsReady = false;
                Disconnected?.Invoke();
            }
        }

        private static global::SpiritShopInfo SampleShopInfo(ulong diamond = 300, ulong bindDiamond = 900,
            uint groupUsed = 60, ulong unitPrice = 60)
        {
            var info = new global::SpiritShopInfo
            {
                ServerTimeMs = 1700000000000UL,
                ShopRevision = 1,
                DiamondBalance = diamond,
                BindDiamondBalance = bindDiamond,
            };
            info.LimitGroups.Add(new global::SpiritShopLimitGroupView
            {
                GroupId = SpiritShopModels.StarterLimitGroupId,
                Name = "首套修行提速终身限额",
                ResetKind = 0,
                MaxWeight = 300,
                UsedWeight = groupUsed,
            });
            var g101 = new global::SpiritShopGoodsView
            {
                GoodsId = SpiritShopModels.GoodsStarterPack60,
                TabId = SpiritShopModels.TabStarterPacks,
                SortOrder = 1,
                Name = "初阶修行提速包",
                Description = "永久绑定仅自用",
                CurrencyType = SpiritShopModels.CurrencyDiamond,
                UnitPrice = unitPrice,
                MaxBuyPerRequest = 1,
                LimitResetKind = 0,
                LimitCount = 0,
                LimitGroupId = SpiritShopModels.StarterLimitGroupId,
                LimitGroupWeight = 60,
            };
            g101.Items.Add(new global::SpiritShopItemEntry { ItemConfigId = 3001, ItemCount = 30, Bound = true });
            g101.Items.Add(new global::SpiritShopItemEntry { ItemConfigId = 3002, ItemCount = 30, Bound = true });
            info.Goods.Add(g101);

            var g201 = new global::SpiritShopGoodsView
            {
                GoodsId = SpiritShopModels.GoodsStarGuaranteeCharm,
                TabId = SpiritShopModels.TabAdvancement,
                SortOrder = 1,
                Name = "定星保底符",
                CurrencyType = SpiritShopModels.CurrencyDiamond,
                UnitPrice = 60,
                MaxBuyPerRequest = 20,
            };
            g201.Items.Add(new global::SpiritShopItemEntry { ItemConfigId = 3405, ItemCount = 1, Bound = true });
            info.Goods.Add(g201);
            return info;
        }

        [Test]
        public void RequestInfo_CachesShelfAndDualBalances_WithoutMixingBindDiamond()
        {
            var net = new FakeTransport();
            object conn = new object();
            float now = 10f;
            net.Responder = (msgId, _) =>
            {
                Assert.AreEqual(MessageIds.GetSpiritShopInfo, msgId);
                return (new global::GetSpiritShopInfoResponse { Info = SampleShopInfo(diamond: 120, bindDiamond: 9999) }, null);
            };

            using var client = new SpiritShopClient(net, () => conn, () => now);
            client.RequestInfo(force: true);
            net.Flush();

            Assert.IsTrue(client.HasInfo);
            Assert.AreEqual(120UL, client.DiamondBalance);
            Assert.AreEqual(9999UL, client.BindDiamondBalance);
            Assert.IsNotNull(client.FindGoods(SpiritShopModels.GoodsStarterPack60));
            Assert.AreEqual(240u, SpiritShopModels.RemainingGroupQuota(client.FindLimitGroup(1)));
        }

        [Test]
        public void BuyGoods_SendsNonZeroKeyAndQuote_AppliesInfoAndBagSnapshot()
        {
            var net = new FakeTransport();
            object conn = new object();
            float now = 10f;
            global::BuySpiritShopGoodsRequest sent = null;
            net.Responder = (msgId, req) =>
            {
                if (msgId == MessageIds.GetSpiritShopInfo)
                    return (new global::GetSpiritShopInfoResponse { Info = SampleShopInfo(diamond: 300, groupUsed: 60) }, null);

                sent = (global::BuySpiritShopGoodsRequest)req;
                var updatedBag = new global::BagInfo
                {
                    Layout = new global::BagLayoutInfo { BagType = 0 },
                };
                return (new global::BuySpiritShopGoodsResponse
                {
                    Info = SampleShopInfo(diamond: 240, groupUsed: 120),
                    Bag = updatedBag,
                }, null);
            };

            using var client = new SpiritShopClient(net, () => conn, () => now);
            global::BagInfo receivedBag = null;
            client.BagReceived += bag => receivedBag = bag;

            client.RequestInfo(force: true);
            net.Flush();

            var quote = client.QuoteFor(SpiritShopModels.GoodsStarterPack60);
            Assert.IsTrue(quote.IsValid);
            bool callbackResult = false;
            bool accepted = client.BuyGoods(quote, 1, ok => callbackResult = ok);
            net.Flush();

            Assert.IsTrue(accepted);
            Assert.IsTrue(callbackResult);
            Assert.IsNotNull(sent);
            Assert.AreNotEqual(0UL, sent.ClientRequestId);
            Assert.AreEqual(SpiritShopModels.CurrencyDiamond, sent.ExpectedCurrencyType);
            Assert.AreEqual(60UL, sent.ExpectedUnitPrice);
            Assert.AreEqual(2, sent.ExpectedItems.Count);
            Assert.AreEqual(3001u, sent.ExpectedItems[0].ItemConfigId);
            Assert.IsTrue(sent.ExpectedItems[0].Bound);
            Assert.AreEqual(240UL, client.DiamondBalance);
            Assert.AreEqual(120u, client.FindLimitGroup(1).UsedWeight);
            Assert.IsNotNull(receivedBag);
            Assert.IsFalse(client.RequiresReconnect);
        }

        [Test]
        public void BuyGoods_RetriesOnceOnUnknownTimeout_LocksOnlyWhenStillUnknown_NotOnCircuitOpenOrRateLimit()
        {
            var net = new FakeTransport();
            object conn = new object();
            float now = 20f;
            int mode = 0; // 0 = circuit open, 1 = rate limit 1008, 2 = timeout twice
            var buyRequestIds = new List<ulong>();

            net.Responder = (msgId, req) =>
            {
                if (msgId == MessageIds.GetSpiritShopInfo)
                    return (new global::GetSpiritShopInfoResponse { Info = SampleShopInfo(diamond: 300, groupUsed: 0) }, null);

                var buyReq = (global::BuySpiritShopGoodsRequest)req;
                buyRequestIds.Add(buyReq.ClientRequestId);
                return mode switch
                {
                    0 => (null, RpcGuard.CircuitOpenError),
                    1 => (null, "server tip=" + (uint)common_error.KRateLimitExceeded),
                    _ => (null, OutstandingCalls.TimeoutDetail),
                };
            };

            using var client = new SpiritShopClient(net, () => conn, () => now);
            client.RequestInfo(force: true);
            net.Flush();
            var quote = client.QuoteFor(SpiritShopModels.GoodsStarterPack60);

            // CircuitOpen 确定未发出，绝不重发也不锁死面板
            mode = 0;
            Assert.IsTrue(client.BuyGoods(quote, 1));
            net.Flush();
            Assert.IsFalse(client.RequiresReconnect);
            Assert.AreEqual(1, buyRequestIds.Count);

            // 1008 限流确定未达 scene，绝不重发也不锁死面板
            now += 5f;
            mode = 1;
            Assert.IsTrue(client.BuyGoods(quote, 1));
            net.Flush();
            Assert.IsFalse(client.RequiresReconnect);
            Assert.AreEqual(2, buyRequestIds.Count);

            // 超时结果不明 -> 用完全相同的幂等键原样重发 1 次；重发仍超时则置 RequiresReconnect = true
            now += 5f;
            mode = 2;
            Assert.IsTrue(client.BuyGoods(quote, 1));
            net.Flush();
            Assert.IsTrue(client.RequiresReconnect);
            Assert.AreEqual(4, buyRequestIds.Count);
            Assert.AreEqual(buyRequestIds[2], buyRequestIds[3]);
            Assert.AreNotEqual(0UL, buyRequestIds[2]);

            // 重连清零后解除恢复锁
            net.TriggerDisconnect();
            Assert.IsFalse(client.RequiresReconnect);
        }

        [Test]
        public void BuyGoods_RejectsStalePriceQuoteLocally_AndRefreshesShelf()
        {
            var net = new FakeTransport();
            object conn = new object();
            float now = 10f;
            ulong serverPrice = 60;
            net.Responder = (msgId, _) =>
            {
                if (msgId == MessageIds.GetSpiritShopInfo)
                    return (new global::GetSpiritShopInfoResponse { Info = SampleShopInfo(unitPrice: serverPrice) }, null);
                Assert.Fail("Stale local quote must be rejected without sending BuySpiritShopGoods.");
                return (null, null);
            };

            using var client = new SpiritShopClient(net, () => conn, () => now);
            client.RequestInfo(force: true);
            net.Flush();
            var oldQuote = client.QuoteFor(SpiritShopModels.GoodsStarterPack60);

            // 服务端热更改价后刷新了货架
            serverPrice = 120;
            now += 5f;
            client.RequestInfo(force: true);
            net.Flush();

            bool sent = client.BuyGoods(oldQuote, 1);
            net.Flush();

            Assert.IsFalse(sent);
            Assert.IsTrue(client.StatusIsError);
            Assert.AreEqual(120UL, client.FindGoods(SpiritShopModels.GoodsStarterPack60).UnitPrice);
        }

        [Test]
        public void Models_EvaluateGroupQuota_And_DescribeAllTips()
        {
            var info = SampleShopInfo(groupUsed: 240);
            var pack60 = SpiritShopModels.FindGoods(info, SpiritShopModels.GoodsStarterPack60);
            var group = SpiritShopModels.FindLimitGroup(info, SpiritShopModels.StarterLimitGroupId);

            Assert.AreEqual(1u, SpiritShopModels.MaxBuyableCount(pack60, group));
            Assert.AreEqual(SpiritShopBuyBlock.None, SpiritShopModels.EvaluateBuy(pack60, group, 1, true, false));
            Assert.AreEqual(SpiritShopBuyBlock.InBattle, SpiritShopModels.EvaluateBuy(pack60, group, 1, true, true));

            group.UsedWeight = 280;
            Assert.AreEqual(0u, SpiritShopModels.MaxBuyableCount(pack60, group));
            Assert.AreEqual(SpiritShopBuyBlock.GroupLimitReached, SpiritShopModels.EvaluateBuy(pack60, group, 1, true, false));

            foreach (spirit_shop_error tip in Enum.GetValues(typeof(spirit_shop_error)))
            {
                if (tip == spirit_shop_error.KSpiritShopErrorOk) continue;
                string text = SpiritShopModels.DescribeTip((uint)tip);
                Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"Missing description for {tip}");
            }
            Assert.IsTrue(SpiritShopModels.TipMeansStale((uint)spirit_shop_error.KSpiritShopPriceChanged));
            Assert.IsTrue(SpiritShopModels.TipMeansStale((uint)spirit_shop_error.KSpiritShopNotInSaleWindow));
            Assert.IsTrue(SpiritShopModels.TipMeansStale((uint)spirit_shop_error.KSpiritShopLevelNotMet));
            Assert.IsTrue(SpiritShopModels.TipMeansStale((uint)spirit_shop_error.KSpiritShopInvalidChoice));
            Assert.IsFalse(SpiritShopModels.TipMeansStale((uint)spirit_shop_error.KSpiritShopCurrencyNotEnough));
        }

        [Test]
        public void V2_ChooseOneQuoteAndBuy_SendsChosenIndexAndSingleExpectedItem()
        {
            const ulong startMs = 1801756800000UL;
            const ulong endMs = 1803139199000UL;
            var info = SampleShopInfo(diamond: 500, bindDiamond: 0);
            info.ServerTimeMs = startMs;

            var g401 = new global::SpiritShopGoodsView
            {
                GoodsId = SpiritShopModels.GoodsSpringBundle401,
                TabId = SpiritShopModels.TabFestival,
                SortOrder = 1,
                Name = "新春四喜大礼盒",
                CurrencyType = SpiritShopModels.CurrencyDiamond,
                UnitPrice = 180,
                OriginalPrice = 300,
                MaxBuyPerRequest = 1,
                GrantMode = SpiritShopModels.GrantModeAll,
                StartTimeMs = startMs,
                EndTimeMs = endMs,
                MinLevel = 10,
                LimitCount = 1,
            };
            g401.Items.Add(new global::SpiritShopItemEntry { ItemConfigId = 3001, ItemCount = 60, Bound = true });
            g401.Items.Add(new global::SpiritShopItemEntry { ItemConfigId = 3002, ItemCount = 60, Bound = true });
            g401.Items.Add(new global::SpiritShopItemEntry { ItemConfigId = 3405, ItemCount = 2, Bound = true });
            g401.Items.Add(new global::SpiritShopItemEntry { ItemConfigId = 3402, ItemCount = 1, Bound = true });
            info.Goods.Add(g401);

            var g402 = new global::SpiritShopGoodsView
            {
                GoodsId = SpiritShopModels.GoodsSpringChoice402,
                TabId = SpiritShopModels.TabFestival,
                SortOrder = 2,
                Name = "新春秘宝自选匣",
                CurrencyType = SpiritShopModels.CurrencyDiamond,
                UnitPrice = 120,
                OriginalPrice = 180,
                MaxBuyPerRequest = 1,
                GrantMode = SpiritShopModels.GrantModeChooseOne,
                StartTimeMs = startMs,
                EndTimeMs = endMs,
                MinLevel = 10,
                LimitCount = 2,
            };
            g402.Items.Add(new global::SpiritShopItemEntry { ItemConfigId = 3408, ItemCount = 1, Bound = true });
            g402.Items.Add(new global::SpiritShopItemEntry { ItemConfigId = 3412, ItemCount = 1, Bound = true });
            g402.Items.Add(new global::SpiritShopItemEntry { ItemConfigId = 3411, ItemCount = 1, Bound = true });
            g402.Items.Add(new global::SpiritShopItemEntry { ItemConfigId = 3405, ItemCount = 3, Bound = true });
            info.Goods.Add(g402);

            Assert.IsTrue(SpiritShopModels.HasTabGoods(info, SpiritShopModels.TabFestival));
            Assert.AreEqual("6折", SpiritShopModels.ComputeDiscountLabel(180, 300));
            Assert.AreEqual("6.7折", SpiritShopModels.ComputeDiscountLabel(120, 180));
            Assert.AreEqual(string.Empty, SpiritShopModels.ComputeDiscountLabel(180, 0));
            Assert.IsFalse(SpiritShopPurchaseQuote.Of(g401, chosenItemIndex: 1).IsValid);
            Assert.IsFalse(SpiritShopPurchaseQuote.Of(g402, chosenItemIndex: 4).IsValid);

            var net = new FakeTransport();
            object conn = new object();
            float now = 10f;
            global::BuySpiritShopGoodsRequest sent = null;
            net.Responder = (msgId, req) =>
            {
                if (msgId == MessageIds.GetSpiritShopInfo)
                    return (new global::GetSpiritShopInfoResponse { Info = info.Clone() }, null);
                sent = (global::BuySpiritShopGoodsRequest)req;
                return (new global::BuySpiritShopGoodsResponse { Info = info.Clone() }, null);
            };

            using var client = new SpiritShopClient(net, () => conn, () => now);
            client.RequestInfo(force: true);
            net.Flush();

            // 单调时钟推进 5 秒：EstimatedServerNowMs 精准前进 5000ms，不信本机系统时间
            now += 5f;
            Assert.AreEqual(startMs + 5000UL, client.EstimatedServerNowMs);
            Assert.That(SpiritShopModels.FormatRemainingTime(endMs, client.EstimatedServerNowMs), Does.StartWith("剩余 15天"));

            // 自选匣选择下标 2（灵宠幻化符 3411 x1）
            var quote = client.QuoteFor(SpiritShopModels.GoodsSpringChoice402, chosenItemIndex: 2);
            Assert.IsTrue(quote.IsValid);
            Assert.AreEqual(2u, quote.ChosenItemIndex);
            Assert.AreEqual(1, quote.Items.Count);
            Assert.AreEqual(3411u, quote.Items[0].ItemConfigId);

            bool ok = false;
            Assert.IsTrue(client.BuyGoods(quote, 1, r => ok = r));
            net.Flush();

            Assert.IsTrue(ok);
            Assert.IsNotNull(sent);
            Assert.AreEqual(2u, sent.ChosenItemIndex);
            Assert.AreEqual(1, sent.ExpectedItems.Count);
            Assert.AreEqual(3411u, sent.ExpectedItems[0].ItemConfigId);
            Assert.IsTrue(sent.ExpectedItems[0].Bound);

            // 模拟服务器时间跨过 endMs：本地即时拦截 SaleEnded 并自动触发货架刷新
            now += (float)((endMs - startMs) / 1000.0) + 2f;
            Assert.AreEqual("活动已结束", SpiritShopModels.FormatRemainingTime(endMs, client.EstimatedServerNowMs));
            int callsBefore = net.Calls.Count;
            Assert.IsFalse(client.BuyGoods(quote, 1));
            Assert.IsTrue(client.StatusIsError);
            Assert.Greater(net.Calls.Count, callsBefore, "Expired festival goods must trigger shelf refresh.");
        }
    }
}
