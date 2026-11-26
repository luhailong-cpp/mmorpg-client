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

        [Test]
        public void AllEightEconomyTradeV2SlicesAndMetasLoadCleanly()
        {
            string assetsDir = ResolveAssetsDirectory();
            string root = Path.Combine(assetsDir, "Resources", "UI", "Ugui", "EconomyTradeV2");
            var expected = new (string Key, int Width, int Height)[]
            {
                ("badge_designated_only", 200, 64),
                ("badge_notice_48h", 220, 64),
                ("badge_notice_fast", 220, 64),
                ("icon_inherit_talisman", 512, 512),
                ("icon_master_seal", 160, 160),
                ("icon_wishlist_dove", 160, 160),
                ("panel_08_zhenbaoge_rmb", 1932, 814),
                ("panel_09_equip_inherit", 1932, 814),
            };
            Assert.That(MmorpgClient.UI.Ugui.SeasonEconomyUiArt.EconomyTradeV2AssetKeys.Length, Is.EqualTo(8));
            foreach (var (key, w, h) in expected)
            {
                Assert.That(File.Exists(Path.Combine(root, key + ".png")), Is.True, "Missing EconomyTradeV2 PNG: " + key);
                Assert.That(File.Exists(Path.Combine(root, key + ".png.meta")), Is.True, "Missing EconomyTradeV2 meta: " + key);
                var sprite = MmorpgClient.UI.Ugui.SeasonEconomyUiArt.LoadTradeV2Slice(key);
                Assert.That(sprite != null && sprite.texture != null, Is.True, "Failed to load EconomyTradeV2 sprite: " + key);
                Assert.That(sprite.texture.width, Is.EqualTo(w), key + " width");
                Assert.That(sprite.texture.height, Is.EqualTo(h), key + " height");
                Assert.That(sprite.border, Is.EqualTo(UnityEngine.Vector4.zero), key + " border");
            }
        }

        [Test]
        public void RmbConsignmentViewModelFiltersTabsAndSummarizesDisputeFrozenIncomes()
        {
            var listings = new[]
            {
                new RmbConsignmentView { ListingId = 1, AssetKind = "ITEM", PriceFen = 2800, Phase = "SALE", DesignatedOnly = false },
                new RmbConsignmentView { ListingId = 2, AssetKind = "PET", PriceFen = 68800, Phase = "NOTICE", DesignatedOnly = true },
                new RmbConsignmentView { ListingId = 3, AssetKind = "ITEM", PriceFen = 28600, Phase = "NOTICE", DesignatedOnly = false },
            };

            Assert.That(RmbConsignmentViewModel.FilterListings(listings, RmbConsignmentSubTab.All).Count, Is.EqualTo(3));
            Assert.That(RmbConsignmentViewModel.FilterListings(listings, RmbConsignmentSubTab.PublicNotice).Count, Is.EqualTo(2));
            var designated = RmbConsignmentViewModel.FilterListings(listings, RmbConsignmentSubTab.DesignatedOnly);
            Assert.That(designated.Count, Is.EqualTo(1));
            Assert.That(designated[0].ListingId, Is.EqualTo(2UL));
            Assert.That(RmbConsignmentViewModel.IsUniqueValuable(listings[0]), Is.False);
            Assert.That(RmbConsignmentViewModel.IsUniqueValuable(listings[1]), Is.True);
            Assert.That(RmbConsignmentViewModel.IsUniqueValuable(listings[2]), Is.True);

            var incomes = new[]
            {
                new RmbSellerIncome { OrderId = "ZBG-1", NetFen = 38600, PaidFen = 0, ReservedFen = 0, DisputeFrozenFen = 0 },
                new RmbSellerIncome { OrderId = "ZBG-2", NetFen = 12000, PaidFen = 0, ReservedFen = 12000, DisputeFrozenFen = 0 },
                new RmbSellerIncome { OrderId = "ZBG-3", NetFen = 9800, PaidFen = 0, ReservedFen = 0, DisputeFrozenFen = 9800, DisputeCaseId = "DISP-20261125" },
            };
            var summary = RmbConsignmentViewModel.SummarizeIncomes(incomes);
            Assert.That(summary.WithdrawableFen, Is.EqualTo(38600UL));
            Assert.That(summary.ReservedFen, Is.EqualTo(12000UL));
            Assert.That(summary.DisputeFrozenFen, Is.EqualTo(9800UL));
            Assert.That(summary.FirstDisputeCaseId, Is.EqualTo("DISP-20261125"));
            Assert.That(RmbConsignmentViewModel.FormatIncomeRowStatus(incomes[0]), Is.EqualTo("可提现"));
            Assert.That(RmbConsignmentViewModel.FormatIncomeRowStatus(incomes[1]), Is.EqualTo("结算期预留"));
            Assert.That(RmbConsignmentViewModel.FormatIncomeRowStatus(incomes[2]), Is.EqualTo("争议冻结"));
        }

        [Test]
        public void MarketWishlistHonorsSampleThresholdAndRoundTripsSerialization()
        {
            var wishlist = new MarketWishlistModel();
            wishlist.SetTarget(3002u, 0u, 24000UL);

            // 样本不足 10 笔时绝不误报到价提醒
            var lowSample = wishlist.Evaluate(3002u, 0u, new GetMarketReferencePriceResponse
            {
                ItemConfigId = 3002u, CurrencyType = 0u, MedianUnitPrice = 20000UL, SampleCount = 9u, InsufficientSample = false,
            });
            Assert.That(lowSample.HasTarget, Is.True);
            Assert.That(lowSample.HasValidP50, Is.False);
            Assert.That(lowSample.BelowOrAtTarget, Is.False);

            // 样本充足且 p50 <= 心愿价时触发到价提醒
            var hit = wishlist.Evaluate(3002u, 0u, new GetMarketReferencePriceResponse
            {
                ItemConfigId = 3002u, CurrencyType = 0u, MedianUnitPrice = 23500UL, SampleCount = 18u, InsufficientSample = false,
            });
            Assert.That(hit.HasValidP50, Is.True);
            Assert.That(hit.BelowOrAtTarget, Is.True);

            // 序列化与反序列化保持一致
            string encoded = wishlist.Serialize();
            var reloaded = new MarketWishlistModel();
            reloaded.LoadSerialized(encoded);
            Assert.That(reloaded.TryGetTarget(3002u, 0u, out ulong restored), Is.True);
            Assert.That(restored, Is.EqualTo(24000UL));

            // 3409/3410 道具图标映射校验
            Assert.That(MmorpgClient.UI.Ugui.Gameplay.EquipmentScreenModel.IconKey(new BagItemInfo { ConfigId = 3409u }),
                Does.EndWith("icon_unseal_talisman"));
            Assert.That(MmorpgClient.UI.Ugui.Gameplay.EquipmentScreenModel.IconKey(new BagItemInfo { ConfigId = 3410u }),
                Does.EndWith("icon_inherit_talisman"));
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
