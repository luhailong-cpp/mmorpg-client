using System;
using System.Collections.Generic;
using System.IO;
using MmorpgClient.Game.Jubaozhai;
using NUnit.Framework;
using Paymentpb;
using Trade;

namespace MmorpgClient.Tests.EditMode.Jubaozhai
{
    public sealed class EconomyTradeModelsTests
    {
        private sealed class MemoryRecovery : ICommerceRecoveryStore
        {
            public readonly Dictionary<ulong, string> Saved = new();
            public string Load(ulong player) => Saved.TryGetValue(player, out var data) ? data : "";
            public void Save(ulong player, string value) => Saved[player] = value;
            public void Clear(ulong player, string expected)
            {
                if (Saved.TryGetValue(player, out var current) && current != expected) throw new InvalidOperationException();
                Saved.Remove(player);
            }
        }

        [Test]
        public void SilverFormattingAlwaysUsesThousandsSeparatorNeverWanOrK()
        {
            Assert.That(EconomyFormat.FormatSilver(0), Is.EqualTo("0 银两"));
            Assert.That(EconomyFormat.FormatSilver(7500), Is.EqualTo("7,500 银两"));
            Assert.That(EconomyFormat.FormatSilver(100000), Is.EqualTo("100,000 银两"));
            Assert.That(EconomyFormat.FormatMoney(1800, "CNY"), Is.EqualTo("18.00 元"));
            Assert.That(EconomyFormat.FormatMoney(24800, "0"), Is.EqualTo("24,800 银两"));
            Assert.That(EconomyFormat.FormatInstanceTag(0xA8F2), Is.EqualTo("实例#A8F2"));
        }

        [Test]
        public void FaceToFaceDiffDetectsSilverDropAndEquipmentSwapAndClearsLock()
        {
            var before = new CommerceOrderResponse
            {
                Order = new CommerceOrderView { OrderId = "pt-1", Kind = CommerceOrderKind.PlayerTrade, Version = 1, SellerPlayerId = 1001, BuyerPlayerId = 2002 },
                BuyerAssets = new TradeAssetSelection { CurrencyType = 0, CurrencyAmount = 100000 },
                BuyerItemInstances = { new TradeOfferInstance { ConfigId = 3001, InstanceId = 0xA8F2, Name = "青云灵剑", Level = 5, Count = 1 } },
            };
            var after = new CommerceOrderResponse
            {
                Order = new CommerceOrderView { OrderId = "pt-1", Kind = CommerceOrderKind.PlayerTrade, Version = 2, SellerPlayerId = 1001, BuyerPlayerId = 2002 },
                BuyerAssets = new TradeAssetSelection { CurrencyType = 0, CurrencyAmount = 10000 },
                BuyerItemInstances = { new TradeOfferInstance { ConfigId = 3001, InstanceId = 0xB109, Name = "青云灵剑", Level = 0, Count = 1 } },
            };

            var diff = FaceToFaceTradeDiffModel.Compare(before, after, viewerPlayerId: 1001);
            Assert.That(diff.HasChanges, Is.True);
            Assert.That(diff.LockCleared, Is.True);
            Assert.That(diff.DiffLines.Count, Is.EqualTo(2));
            Assert.That(diff.DiffLines[0], Does.Contain("100,000 银两 → 10,000 银两"));
            Assert.That(diff.DiffLines[1], Does.Contain("青云灵剑 +5 (实例#A8F2) → 青云灵剑 (实例#B109)"));

            var unchanged = FaceToFaceTradeDiffModel.Compare(after, after.Clone(), viewerPlayerId: 1001);
            Assert.That(unchanged.HasChanges, Is.False);
            Assert.That(unchanged.LockCleared, Is.False);
        }

        [Test]
        public void SellerCreditDisplayMapsTrustedTransferAndRemoteNewDeviceAlerts()
        {
            var trusted = SellerCreditDisplayModel.FromProto(new SellerCreditView
            {
                SellerPlayerId = 7001,
                CreditScore = 94,
                StarRatingTenths = 48,
                PositiveRateBps = 9780,
                PositiveReviews = 247,
                NeutralReviews = 15,
                NegativeReviews = 6,
                RiskTier = "trusted",
            });
            Assert.That(trusted.StarText, Is.EqualTo("4.8★"));
            Assert.That(trusted.TierBadgeKey, Is.EqualTo("badge_tier_trusted"));
            Assert.That(trusted.AlertMode, Is.EqualTo(SellerCreditAlertMode.None));
            Assert.That(trusted.StarSliceKeys, Is.EqualTo(new[]
            {
                "credit_star_full", "credit_star_full", "credit_star_full", "credit_star_full", "credit_star_half",
            }));

            var transfer = SellerCreditDisplayModel.FromProto(new SellerCreditView
            {
                SellerPlayerId = 7002,
                CreditScore = 60,
                StarRatingTenths = 30,
                OwnershipChanged = true,
                RiskTier = "new_or_transferred",
            });
            Assert.That(transfer.AlertMode, Is.EqualTo(SellerCreditAlertMode.OfficialTransfer));
            Assert.That(transfer.TierBadgeKey, Is.EqualTo("badge_tier_new_transfer"));
            Assert.That(transfer.AlertBannerText, Does.Contain("持有者已变更"));

            var remoteClear = SellerCreditDisplayModel.FromProto(new SellerCreditView
            {
                SellerPlayerId = 7003,
                CreditScore = 0,
                StarRatingTenths = 10,
                EnvCreditCleared = true,
                NegativeReviews = 2,
                ViolationCount = 1,
                RiskTier = "restricted",
            });
            Assert.That(remoteClear.AlertMode, Is.EqualTo(SellerCreditAlertMode.RemoteNewDevice));
            Assert.That(remoteClear.TierBadgeKey, Is.EqualTo("badge_tier_restricted"));
            Assert.That(remoteClear.AlertBannerText, Does.Contain("异地陌生新设备"));
            Assert.That(remoteClear.AlertDetailText, Does.Contain("黑历史永久保留"));
            Assert.That(remoteClear.AlertDetailText, Does.Contain("仅换 IP 不扣分"));
        }

        [Test]
        public void MarketP50HidesCurveUnderTenSamplesAndFormatsValidMedian()
        {
            var low = MarketP50DisplayModel.FromProto(new GetMarketReferencePriceResponse
            {
                ItemConfigId = 3403,
                CurrencyType = 0,
                MedianUnitPrice = 24800,
                SampleCount = 9,
                InsufficientSample = true,
                WindowDays = 7,
            });
            Assert.That(low.HideReferenceCurve, Is.True);
            Assert.That(low.Headline, Is.EqualTo("样本不足 10 笔，隐藏参考行情"));

            var valid = MarketP50DisplayModel.FromProto(new GetMarketReferencePriceResponse
            {
                ItemConfigId = 3403,
                CurrencyType = 0,
                MedianUnitPrice = 24800,
                SampleCount = 26,
                InsufficientSample = false,
                WindowDays = 7,
            });
            Assert.That(valid.HideReferenceCurve, Is.False);
            Assert.That(valid.Headline, Does.Contain("24,800 银两"));
            Assert.That(valid.SampleText, Does.Contain("26 笔"));

            Assert.That(MarketP50DisplayModel.DescribeNoticeTier(false, 2000), Does.Contain("0–2 小时"));
            Assert.That(MarketP50DisplayModel.DescribeNoticeTier(true, 2000), Does.Contain("48 小时"));
        }

        [Test]
        public void HoldToConfirmTimerRequiresFullTwoSecondsAndResetsOnRelease()
        {
            var timer = new HoldToConfirmTimer(2.0f);
            timer.BeginHold();
            Assert.That(timer.Advance(1.2f), Is.False);
            Assert.That(timer.Progress, Is.InRange(0.59f, 0.61f));
            timer.CancelHold();
            Assert.That(timer.Progress, Is.EqualTo(0f));
            Assert.That(timer.Completed, Is.False);

            timer.BeginHold();
            Assert.That(timer.Advance(-1f), Is.False);
            Assert.That(timer.Advance(float.NaN), Is.False);
            Assert.That(timer.Advance(1.5f), Is.False);
            Assert.That(timer.Advance(0.5f), Is.True);
            Assert.That(timer.Completed, Is.True);
            Assert.That(timer.Progress, Is.EqualTo(1f));
            Assert.That(timer.Advance(0.5f), Is.False, "完成后不得重复触发");
        }

        [Test]
        public void OfficialShopCatalogClassifiesTabsAndEnforcesIosIapIsolation()
        {
            Assert.That(OfficialShopCatalogModel.ClassifySku(new OfficialSkuView { Title = "仙途轻享卡（30天）" }), Is.EqualTo(OfficialShopTab.LightCard));
            Assert.That(OfficialShopCatalogModel.ClassifySku(new OfficialSkuView { Title = "永久外观 · 青云道袍" }), Is.EqualTo(OfficialShopTab.PermanentWardrobe));
            Assert.That(OfficialShopCatalogModel.ClassifySku(new OfficialSkuView { Title = "解封剥离符" }), Is.EqualTo(OfficialShopTab.ArtisanSupport));
            Assert.That(OfficialShopCatalogModel.ClassifySku(new OfficialSkuView { Title = "首套追赶礼匣" }), Is.EqualTo(OfficialShopTab.CatchupBundle));

            Assert.That(OfficialShopCatalogModel.AllowThirdPartyPaymentIconsInShop(PayClientPlatform.IosApp), Is.False);
            Assert.That(OfficialShopCatalogModel.AllowThirdPartyPaymentIconsInShop(PayClientPlatform.AndroidApp), Is.False);
            Assert.That(OfficialShopCatalogModel.IsChannelAllowedOnPlatform(PayChannel.Apple, PayClientPlatform.IosApp), Is.True);
            Assert.That(OfficialShopCatalogModel.IsChannelAllowedOnPlatform(PayChannel.WechatPay, PayClientPlatform.IosApp), Is.False);
            Assert.That(OfficialShopCatalogModel.IsChannelAllowedOnPlatform(PayChannel.Alipay, PayClientPlatform.IosApp), Is.False);
        }

        [Test]
        public void CommerceClientSupportsCreditReviewP50QuotaAndDesignatedBuyerConsignment()
        {
            var net = new JubaozhaiFakeTransport();
            var store = new MemoryRecovery();
            object connection = new();
            using var client = new CommerceClient(net, store, () => connection);

            // 1) Designated-buyer consignment carries designated_buyer_player_id
            client.Consign(new TradeAssetSelection { PetId = 99 }, 0, 50000, 3600000, designatedBuyerPlayerId: 8888);
            var consignReq = (CreateConsignmentRequest)net.LastRequest;
            Assert.That(consignReq.DesignatedBuyerPlayerId, Is.EqualTo(8888UL));
            net.Reply(new CommerceOrderResponse { Order = new CommerceOrderView { OrderId = "c-1", Version = 1, DesignatedBuyerPlayerId = 8888 } });
            Assert.That(store.Load(1), Is.Empty);

            // 2) GetSellerCredit caches profile
            client.GetSellerCredit(7001);
            net.Reply(new SellerCreditResponse { Profile = new SellerCreditView { SellerPlayerId = 7001, CreditScore = 94, StarRatingTenths = 48 } });
            Assert.That(client.SellerCredits[7001].CreditScore, Is.EqualTo(94U));

            // 3) SubmitSellerReview is durable write & clears recovery when Profile arrives
            client.SubmitSellerReview("commerce:123", SellerReviewRating.Positive, "发货快");
            Assert.That(client.HasPendingWrite, Is.True);
            net.Reply(new SubmitSellerReviewResponse { CountedForScore = true, Profile = new SellerCreditView { SellerPlayerId = 7001, CreditScore = 96, StarRatingTenths = 49 } });
            Assert.That(client.HasPendingWrite, Is.False);
            Assert.That(client.SellerCredits[7001].CreditScore, Is.EqualTo(96U));

            // 4) GetMarketReferencePrice & GetPlayerTradeQuota cache results
            client.GetMarketReferencePrice(3403, 0);
            net.Reply(new GetMarketReferencePriceResponse { ItemConfigId = 3403, CurrencyType = 0, MedianUnitPrice = 24800, SampleCount = 26, WindowDays = 7 });
            Assert.That(client.TryGetMarketReferencePrice(3403, 0, out var p50), Is.True);
            Assert.That(p50.MedianUnitPrice, Is.EqualTo(24800UL));

            client.GetPlayerTradeQuota(0);
            net.Reply(new GetPlayerTradeQuotaResponse { CurrencyType = 0, Mode = "strict_consumables", DailyCurrencyLimit = 10000, DailyCurrencyUsed = 2500, DailyCurrencyRemaining = 7500 });
            Assert.That(client.TryGetTradeQuota(0, out var quota), Is.True);
            Assert.That(quota.DailyCurrencyRemaining, Is.EqualTo(7500UL));

            // 5) Player switch clears all economy caches
            net.PlayerId = 2;
            connection = new object();
            client.ObserveConnection();
            Assert.That(client.SellerCredits, Is.Empty);
            Assert.That(client.TryGetMarketReferencePrice(3403, 0, out _), Is.False);
            Assert.That(client.TryGetTradeQuota(0, out _), Is.False);
        }

        [Test]
        public void CommerceClientRmbConsignmentAndWithdrawalFollowDurableRecoveryContract()
        {
            var net = new JubaozhaiFakeTransport();
            var store = new MemoryRecovery();
            object connection = new();
            using var client = new CommerceClient(net, store, () => connection);

            // 1) CreateRmbConsignment forwards designated buyer and clears recovery on RmbConsignmentResponse
            var assets = new TradeAssetSelection();
            assets.ItemUuids.Add(99001);
            client.CreateRmbConsignment(assets, "青云灵剑 +10", 12800, designatedBuyerPlayerId: 8888);
            Assert.That(client.HasPendingWrite, Is.True);
            var createReq = (CreateRmbConsignmentRequest)net.LastRequest;
            Assert.That(createReq.PriceFen, Is.EqualTo(12800UL));
            Assert.That(createReq.DesignatedBuyerPlayerId, Is.EqualTo(8888UL));
            net.Reply(new RmbConsignmentResponse { Listing = new RmbConsignmentView { ListingId = 701, Title = "青云灵剑 +10", PriceFen = 12800, DesignatedOnly = true } });
            Assert.That(client.HasPendingWrite, Is.False);

            // 2) ListRmbConsignments + BuyRmbConsignment + ListSellerIncomes + CreateWithdrawal
            client.ListRmbConsignments();
            net.Reply(new ListRmbConsignmentsResponse { Listings = { new RmbConsignmentView { ListingId = 701, PriceFen = 12800 } } });
            Assert.That(client.Busy, Is.False);

            client.BuyRmbConsignment(701);
            Assert.That(client.HasPendingWrite, Is.True);
            net.Reply(new CommerceOrderResponse { Order = new CommerceOrderView { OrderId = "rmb:701", State = CommerceOrderState.AwaitingPayment } });
            Assert.That(client.HasPendingWrite, Is.False);

            client.ListSellerIncomes();
            net.Reply(new ListSellerIncomesResponse { Incomes = { new RmbSellerIncome { OrderId = "rmb:701", NetFen = 12000, DisputeFrozenFen = 3000, DisputeCaseId = "DISP-1" } } });
            Assert.That(client.Busy, Is.False);

            client.CreateWithdrawal(5000);
            Assert.That(client.HasPendingWrite, Is.True);
            var wdReq = (CreateWithdrawalRequest)net.LastRequest;
            Assert.That(wdReq.AmountFen, Is.EqualTo(5000UL));
            net.Reply(new WithdrawalResponse { Withdrawal = new RmbWithdrawalView { WithdrawalId = "wd:1", AmountFen = 5000, State = "PENDING" } });
            Assert.That(client.HasPendingWrite, Is.False);
        }

        [Test]
        public void AllNineteenEconomyTradeV1SlicesAndMetasExistInResources()
        {
            string assetsDir = ResolveAssetsDirectory();
            string root = Path.Combine(assetsDir, "Resources", "UI", "Ugui", "EconomyTradeV1");
            string[] expected =
            {
                "badge_item_bound_b.png",
                "badge_item_tradable_t.png",
                "badge_tier_new_transfer.png",
                "badge_tier_restricted.png",
                "badge_tier_standard.png",
                "badge_tier_trusted.png",
                "credit_star_empty.png",
                "credit_star_full.png",
                "credit_star_half.png",
                "diff_alert_banner.png",
                "hold_confirm_button_track.png",
                "icon_unseal_talisman.png",
                "panel_01_trade_guard.png",
                "panel_02_diff_confirm.png",
                "panel_03_credit_review.png",
                "panel_04_credit_alerts.png",
                "panel_05_market_order.png",
                "panel_06_equipment_isolation.png",
                "panel_07_official_shop.png",
            };
            foreach (string file in expected)
            {
                Assert.That(File.Exists(Path.Combine(root, file)), Is.True, "Missing PNG slice: " + file);
                Assert.That(File.Exists(Path.Combine(root, file + ".meta")), Is.True, "Missing PNG meta: " + file);
            }
        }

        private static string ResolveAssetsDirectory()
        {
            string[] candidates =
            {
                Path.Combine(Environment.CurrentDirectory, "Assets"),
                Path.Combine(Environment.CurrentDirectory, "mmorpg-client", "Assets"),
            };
            foreach (string candidate in candidates)
                if (Directory.Exists(Path.Combine(candidate, "Resources", "UI", "Ugui", "EconomyTradeV1")))
                    return candidate;
            return UnityEngine.Application.dataPath;
        }
    }
}
