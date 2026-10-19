using MmorpgClient.Game.MysteryMerchant;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>神秘商人的纯规则与文案。样例:整点 1,000,000 出现、停留 30 分钟、每小时一轮,在 3 线(scene 300)。</summary>
    public sealed class MysteryMerchantModelsTests
    {
        private const ulong Appear = 1_000_000;
        private const ulong Stay = 1_800_000;
        private const ulong Hour = 3_600_000;
        private const ulong MerchantScene = 300;

        private static global::MysteryMerchantInfo ActiveInfo() => new global::MysteryMerchantInfo
        {
            Active = true,
            RoundId = 7,
            SceneConfigId = 1,
            SceneId = MerchantScene,
            ChannelNo = 3,
            AppearAtMs = Appear,
            ExpireAtMs = Appear + Stay,
            NextAppearAtMs = Appear + Hour,
            ServerTimeMs = Appear + 60_000,
            InteractRange = 8,
        };

        private static global::MysteryMerchantGoodsInfo Goods(uint limit = 5, uint bought = 0, ulong price = 240) =>
            new global::MysteryMerchantGoodsInfo
            {
                GoodsId = 1, ItemConfigId = 10, ItemCount = 10, Name = "回血丹", CurrencyType = 0,
                OriginalPrice = 400, Price = price, DiscountPercent = 60, LimitCount = limit, BoughtCount = bought,
            };

        // ── 在不在 ──────────────────────────────────────────────────────────

        [Test]
        public void Evaluate_NoInfo_IsUnknown()
        {
            Assert.That(MysteryMerchantModels.Evaluate(null, MerchantScene, Appear), Is.EqualTo(MysteryMerchantPresence.Unknown));
        }

        [Test]
        public void Evaluate_SameScene_IsHere_OtherScene_IsElsewhere()
        {
            var info = ActiveInfo();
            Assert.That(MysteryMerchantModels.Evaluate(info, MerchantScene, Appear + 1), Is.EqualTo(MysteryMerchantPresence.Here));
            Assert.That(MysteryMerchantModels.Evaluate(info, 100, Appear + 1), Is.EqualTo(MysteryMerchantPresence.Elsewhere));
            // 还没进任何场景(scene_id 0):不可能「在本线」。
            Assert.That(MysteryMerchantModels.Evaluate(info, 0, Appear + 1), Is.EqualTo(MysteryMerchantPresence.Elsewhere));
        }

        [Test]
        public void Evaluate_PresenceWindowIsHalfOpen_AndIgnoresTheHereFlag()
        {
            var info = ActiveInfo();
            info.Here = true;   // 公告里恒为 false、应答里是那一刻的结论:判定都不该读它
            Assert.That(MysteryMerchantModels.Evaluate(info, MerchantScene, Appear), Is.EqualTo(MysteryMerchantPresence.Here));
            Assert.That(MysteryMerchantModels.Evaluate(info, MerchantScene, Appear + Stay - 1), Is.EqualTo(MysteryMerchantPresence.Here));
            Assert.That(MysteryMerchantModels.Evaluate(info, MerchantScene, Appear + Stay), Is.EqualTo(MysteryMerchantPresence.Away),
                "离开时刻那一毫秒起就不在了,哪怕手里的结论还写着 active");
            Assert.That(MysteryMerchantModels.Evaluate(info, MerchantScene, Appear - 1), Is.EqualTo(MysteryMerchantPresence.Away));
            Assert.That(MysteryMerchantModels.Evaluate(info, 100, Appear + Stay), Is.EqualTo(MysteryMerchantPresence.Away));
        }

        [Test]
        public void Evaluate_InactiveInfo_IsAway()
        {
            var info = new global::MysteryMerchantInfo { NextAppearAtMs = Appear + Hour, ServerTimeMs = Appear + Stay + 5 };
            Assert.That(MysteryMerchantModels.Evaluate(info, MerchantScene, Appear + Stay + 5), Is.EqualTo(MysteryMerchantPresence.Away));
        }

        [Test]
        public void RemainingAndUntilNext()
        {
            var info = ActiveInfo();
            Assert.That(MysteryMerchantModels.RemainingMs(info, Appear + 60_000), Is.EqualTo((long)(Stay - 60_000)));
            Assert.That(MysteryMerchantModels.RemainingMs(info, Appear + Stay), Is.EqualTo(0));
            Assert.That(MysteryMerchantModels.RemainingMs(null, Appear), Is.EqualTo(0));

            Assert.That(MysteryMerchantModels.UntilNextMs(info, Appear + Stay), Is.EqualTo((long)(Hour - Stay)));
            Assert.That(MysteryMerchantModels.UntilNextMs(info, Appear + Hour + 5), Is.EqualTo(0), "到点了还没有新结论:0,不是负数");
            Assert.That(MysteryMerchantModels.UntilNextMs(new global::MysteryMerchantInfo(), Appear), Is.EqualTo(-1), "服务端没给下一次:未知");
            Assert.That(MysteryMerchantModels.UntilNextMs(null, Appear), Is.EqualTo(-1));
        }

        // ── 文案 ────────────────────────────────────────────────────────────

        [TestCase(60u, "6折")]
        [TestCase(50u, "5折")]
        [TestCase(40u, "4折")]
        [TestCase(45u, "4.5折")]
        [TestCase(5u, "0.5折")]
        [TestCase(100u, "原价")]
        [TestCase(0u, "原价")]
        public void DiscountLabel(uint percent, string expected)
        {
            Assert.That(MysteryMerchantModels.DiscountLabel(percent), Is.EqualTo(expected));
        }

        [TestCase(0L, "00:00")]
        [TestCase(-5L, "00:00")]
        [TestCase(1L, "00:01")]
        [TestCase(1000L, "00:01")]
        [TestCase(1001L, "00:02")]
        [TestCase(1_799_000L, "29:59")]
        [TestCase(1_800_000L, "30:00")]
        [TestCase(3_600_000L, "1:00:00")]
        [TestCase(3_661_000L, "1:01:01")]
        public void FormatCountdown_RoundsUpToWholeSeconds(long milliseconds, string expected)
        {
            Assert.That(MysteryMerchantModels.FormatCountdown(milliseconds), Is.EqualTo(expected));
        }

        [Test]
        public void CurrencyNameAndAmount()
        {
            Assert.That(MysteryMerchantModels.CurrencyName(0), Is.EqualTo("银两"));
            Assert.That(MysteryMerchantModels.CurrencyName(1), Is.EqualTo("灵石"));
            Assert.That(MysteryMerchantModels.CurrencyName(2), Is.EqualTo("绑定灵石"));
            Assert.That(MysteryMerchantModels.CurrencyName(9), Is.EqualTo("货币"));
            Assert.That(MysteryMerchantModels.FormatAmount(1234567), Is.EqualTo("1,234,567"));
        }

        [Test]
        public void GoodsName_FallsBackToConfigId_NeverInventsAName()
        {
            Assert.That(MysteryMerchantModels.GoodsName(Goods()), Is.EqualTo("回血丹"));
            var unnamed = Goods();
            unnamed.Name = " ";
            Assert.That(MysteryMerchantModels.GoodsName(unnamed), Is.EqualTo("物品 · 10"));
            Assert.That(MysteryMerchantModels.GoodsName(null), Is.EqualTo(""));
        }

        [Test]
        public void AnnouncementText_SaysWhereAndHowLong()
        {
            Assert.That(MysteryMerchantModels.AnnouncementText(3, false, 1_800_000), Is.EqualTo("神秘商人出现在3线，停留 30 分钟。"));
            Assert.That(MysteryMerchantModels.AnnouncementText(3, true, 1_800_000), Is.EqualTo("神秘商人出现在本线，停留 30 分钟。"));
            Assert.That(MysteryMerchantModels.AnnouncementText(3, false, 1_750_000), Is.EqualTo("神秘商人出现在3线，停留 30 分钟。"),
                "分钟数向上取整");
            Assert.That(MysteryMerchantModels.AnnouncementText(3, false, 40_000), Is.EqualTo("神秘商人出现在3线，即将离开。"));
            Assert.That(MysteryMerchantModels.AnnouncementText(3, false, 0), Is.EqualTo("神秘商人出现在3线。"));
        }

        [Test]
        public void EntryLabel_ByPresence()
        {
            Assert.That(MysteryMerchantModels.EntryLabel(MysteryMerchantPresence.Here, 3, 754_000, 0), Is.EqualTo("神秘商人 本线 12:34"));
            Assert.That(MysteryMerchantModels.EntryLabel(MysteryMerchantPresence.Elsewhere, 3, 754_000, 0), Is.EqualTo("神秘商人 3线 12:34"));
            Assert.That(MysteryMerchantModels.EntryLabel(MysteryMerchantPresence.Away, 0, 0, 2_710_000), Is.EqualTo("神秘商人 45:10 后出现"));
            Assert.That(MysteryMerchantModels.EntryLabel(MysteryMerchantPresence.Away, 0, 0, 0), Is.EqualTo("神秘商人 即将出现"),
                "下一次的时刻已经到了、新一轮还没排出来:不显示「00:00 后出现」");
            Assert.That(MysteryMerchantModels.EntryLabel(MysteryMerchantPresence.Away, 0, 0, -1), Is.Null, "不知道下一次:不显示角标");
            Assert.That(MysteryMerchantModels.EntryLabel(MysteryMerchantPresence.Unknown, 0, 0, 100), Is.Null);
        }

        // ── 能不能买 ────────────────────────────────────────────────────────

        [Test]
        public void Remaining_NeverUnderflows()
        {
            Assert.That(MysteryMerchantModels.Remaining(Goods(limit: 5, bought: 2)), Is.EqualTo(3u));
            Assert.That(MysteryMerchantModels.Remaining(Goods(limit: 5, bought: 5)), Is.EqualTo(0u));
            Assert.That(MysteryMerchantModels.Remaining(Goods(limit: 5, bought: 9)), Is.EqualTo(0u), "表里的限购被调小之后已购数可以比它大");
            Assert.That(MysteryMerchantModels.Remaining(null), Is.EqualTo(0u));
        }

        [Test]
        public void EvaluateBuy_FirstBlockingReasonWins()
        {
            var goods = Goods();
            Assert.That(MysteryMerchantModels.EvaluateBuy(goods, MysteryMerchantPresence.Here, true, false),
                Is.EqualTo(MysteryMerchantBuyBlock.None));
            Assert.That(MysteryMerchantModels.EvaluateBuy(goods, MysteryMerchantPresence.Here, false, false),
                Is.EqualTo(MysteryMerchantBuyBlock.NotReady));
            Assert.That(MysteryMerchantModels.EvaluateBuy(null, MysteryMerchantPresence.Here, true, false),
                Is.EqualTo(MysteryMerchantBuyBlock.NotReady));
            Assert.That(MysteryMerchantModels.EvaluateBuy(goods, MysteryMerchantPresence.Elsewhere, true, false),
                Is.EqualTo(MysteryMerchantBuyBlock.NotHere));
            Assert.That(MysteryMerchantModels.EvaluateBuy(null, MysteryMerchantPresence.Elsewhere, true, false),
                Is.EqualTo(MysteryMerchantBuyBlock.NotHere), "不在本线时本来就没有货架:该说「不在这条线上」而不是「请稍候」");
            Assert.That(MysteryMerchantModels.EvaluateBuy(goods, MysteryMerchantPresence.Away, true, false),
                Is.EqualTo(MysteryMerchantBuyBlock.NotHere));
            Assert.That(MysteryMerchantModels.EvaluateBuy(goods, MysteryMerchantPresence.Here, true, true),
                Is.EqualTo(MysteryMerchantBuyBlock.InBattle));
            Assert.That(MysteryMerchantModels.EvaluateBuy(Goods(limit: 1, bought: 1), MysteryMerchantPresence.Here, true, false),
                Is.EqualTo(MysteryMerchantBuyBlock.LimitReached));
            // 再贵也不在这里拦:余额是某次快照,买不买得起由服务端裁决。
            Assert.That(MysteryMerchantModels.EvaluateBuy(Goods(price: ulong.MaxValue), MysteryMerchantPresence.Here, true, false),
                Is.EqualTo(MysteryMerchantBuyBlock.None));
        }

        // ── 报价 ────────────────────────────────────────────────────────────

        [Test]
        public void Quote_CapturesEverythingThePlayerPaysAndGets()
        {
            var quote = MysteryMerchantQuote.Of(7, Goods());
            Assert.That(quote.IsValid, Is.True);
            Assert.That(quote.RoundId, Is.EqualTo(7ul));
            Assert.That(quote.GoodsId, Is.EqualTo(1u));
            Assert.That(quote.Price, Is.EqualTo(240ul));
            Assert.That(quote.CurrencyType, Is.EqualTo(0u));
            Assert.That(quote.ItemConfigId, Is.EqualTo(10u));
            Assert.That(quote.ItemCount, Is.EqualTo(10u));

            Assert.That(MysteryMerchantQuote.Of(7, null).IsValid, Is.False);
            Assert.That(default(MysteryMerchantQuote).IsValid, Is.False);
        }

        [Test]
        public void Quote_DiffersWhenAnyTermDiffers_ButNotWhenOnlyTheBoughtCountDoes()
        {
            var quote = MysteryMerchantQuote.Of(7, Goods());
            Assert.That(MysteryMerchantQuote.Of(7, Goods()) == quote, Is.True);
            Assert.That(MysteryMerchantQuote.Of(7, Goods()).GetHashCode(), Is.EqualTo(quote.GetHashCode()));
            // 已购数、限购、折扣角标、名字不属于报价:买过一份之后同一件商品的报价不变。
            Assert.That(MysteryMerchantQuote.Of(7, Goods(limit: 9, bought: 3)) == quote, Is.True);

            Assert.That(MysteryMerchantQuote.Of(8, Goods()) != quote, Is.True, "换了一轮");
            Assert.That(MysteryMerchantQuote.Of(7, Goods(price: 241)) != quote, Is.True, "价格变了");
            var other = Goods();
            other.CurrencyType = 1;
            Assert.That(MysteryMerchantQuote.Of(7, other) != quote, Is.True, "货币变了");
            other = Goods();
            other.ItemConfigId = 11;
            Assert.That(MysteryMerchantQuote.Of(7, other) != quote, Is.True, "物品变了");
            other = Goods();
            other.ItemCount = 5;
            Assert.That(MysteryMerchantQuote.Of(7, other) != quote, Is.True, "每份数量变了");
            other = Goods();
            other.GoodsId = 2;
            Assert.That(MysteryMerchantQuote.Of(7, other) != quote, Is.True, "不是同一件商品");
        }

        // ── 重试与自动查询的节奏 ────────────────────────────────────────────

        [Test]
        public void InfoRetryDelay_DoublesThenCaps()
        {
            Assert.That(MysteryMerchantModels.InfoRetryDelaySeconds(0), Is.EqualTo(5f));
            Assert.That(MysteryMerchantModels.InfoRetryDelaySeconds(1), Is.EqualTo(5f));
            Assert.That(MysteryMerchantModels.InfoRetryDelaySeconds(2), Is.EqualTo(10f));
            Assert.That(MysteryMerchantModels.InfoRetryDelaySeconds(3), Is.EqualTo(20f));
            Assert.That(MysteryMerchantModels.InfoRetryDelaySeconds(4), Is.EqualTo(40f));
            Assert.That(MysteryMerchantModels.InfoRetryDelaySeconds(5), Is.EqualTo(60f));
            Assert.That(MysteryMerchantModels.InfoRetryDelaySeconds(int.MaxValue), Is.EqualTo(60f), "封顶,不溢出");
        }

        [Test]
        public void AutoRefreshDelay_SpreadsPlayersApart_AndStopsAfterAFewAttempts()
        {
            Assert.That(MysteryMerchantModels.AutoRefreshDelayMs(0, 0), Is.EqualTo(12_000L));
            Assert.That(MysteryMerchantModels.AutoRefreshDelayMs(0, 7), Is.EqualTo(19_000L), "按角色 id 错开,不在同一秒一起问");
            Assert.That(MysteryMerchantModels.AutoRefreshDelayMs(0, 20), Is.EqualTo(12_000L), "偏移不超过 20 秒");
            // 第二次落在服务端 30 秒宽限期之后:那时它会回答真正的下一轮。
            Assert.That(MysteryMerchantModels.AutoRefreshDelayMs(1, 0), Is.GreaterThan(30_000L));
            Assert.That(MysteryMerchantModels.AutoRefreshDelayMs(2, 0), Is.GreaterThan(MysteryMerchantModels.AutoRefreshDelayMs(1, 19)));
            Assert.That(MysteryMerchantModels.AutoRefreshDelayMs(MysteryMerchantModels.MaxAutoRefreshAttempts, 0), Is.EqualTo(-1L));
            Assert.That(MysteryMerchantModels.AutoRefreshDelayMs(-1, 0), Is.EqualTo(-1L));
        }

        [Test]
        public void DescribeTip_CoversEveryMerchantCode_AndFallsBackWithTheNumber()
        {
            foreach (mystery_merchant_error code in System.Enum.GetValues(typeof(mystery_merchant_error)))
            {
                if (code == mystery_merchant_error.KMysteryMerchantErrorOk) continue;
                string text = MysteryMerchantModels.DescribeTip((uint)code);
                Assert.That(text, Does.Not.Contain(((uint)code).ToString()), code + " 应当有专门的文案,而不是带编号的兜底");
            }
            Assert.That(MysteryMerchantModels.DescribeTip(987654), Does.Contain("987654"));
            Assert.That(MysteryMerchantModels.DescribeTip((uint)common_error.KRateLimitExceeded), Does.Contain("太快"));
        }

        [Test]
        public void TipMeansStale_OnlyForCodesThatInvalidateTheLocalShelf()
        {
            Assert.That(MysteryMerchantModels.TipMeansStale((uint)mystery_merchant_error.KMysteryMerchantShelfChanged), Is.True);
            Assert.That(MysteryMerchantModels.TipMeansStale((uint)mystery_merchant_error.KMysteryMerchantNotPresent), Is.True);
            Assert.That(MysteryMerchantModels.TipMeansStale((uint)mystery_merchant_error.KMysteryMerchantNotHere), Is.True);
            Assert.That(MysteryMerchantModels.TipMeansStale((uint)mystery_merchant_error.KMysteryMerchantLimitReached), Is.True);
            Assert.That(MysteryMerchantModels.TipMeansStale((uint)mystery_merchant_error.KMysteryMerchantTooFar), Is.False);
            Assert.That(MysteryMerchantModels.TipMeansStale((uint)mystery_merchant_error.KMysteryMerchantBagFull), Is.False);
            Assert.That(MysteryMerchantModels.TipMeansStale((uint)mystery_merchant_error.KMysteryMerchantCurrencyNotEnough), Is.False);
        }

        [Test]
        public void PurchasedText_ShowsTotalItemCount()
        {
            Assert.That(MysteryMerchantModels.PurchasedText(Goods(), 2), Is.EqualTo("购买成功：回血丹 ×20"));
        }
    }
}
