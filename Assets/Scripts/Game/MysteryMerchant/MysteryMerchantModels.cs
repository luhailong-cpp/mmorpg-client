using System;
using System.Globalization;
using MmorpgClient.Game.WorldTravel;

namespace MmorpgClient.Game.MysteryMerchant
{
    /// <summary>神秘商人此刻相对于本玩家在哪。</summary>
    public enum MysteryMerchantPresence
    {
        /// <summary>还没问到服务端(刚进游戏、断线后)。界面什么都不显示。</summary>
        Unknown = 0,
        /// <summary>不在场:这一轮已经离开,或还没有任何一轮。</summary>
        Away = 1,
        /// <summary>在场,但在别的线上:只知道「在几线」,看不到货架。</summary>
        Elsewhere = 2,
        /// <summary>在场,且就在本玩家所在的这条线上。</summary>
        Here = 3,
    }

    /// <summary>一件商品此刻为什么不能买。None = 可以买。</summary>
    public enum MysteryMerchantBuyBlock
    {
        None = 0,
        /// <summary>数据层暂时发不出请求:没连上、购买在途、需要重连。</summary>
        NotReady = 1,
        /// <summary>商人不在本线(或已经离开)。</summary>
        NotHere = 2,
        /// <summary>本轮限购名额已用完。</summary>
        LimitReached = 3,
        /// <summary>战斗中。</summary>
        InBattle = 4,
    }

    /// <summary>
    /// 一份报价:玩家点下「购买」那一刻在货架上看到的东西 —— 哪一轮、哪件商品、每份多少钱、用什么货币、
    /// 拿到什么物品、每份几个。购买请求把它原样回传,服务端发现与它现算的有任何一项不一致就拒绝
    /// (BuyMysteryMerchantGoodsRequest 的 round_id / expected_*)。
    /// 「确认购买」绑定的是报价而不是商品 id:两次点击之间换了一轮、或同一个 id 的价格 / 内容变了,确认就作废。
    /// default(GoodsId == 0)= 没有报价。
    /// </summary>
    public readonly struct MysteryMerchantQuote : IEquatable<MysteryMerchantQuote>
    {
        public readonly ulong RoundId;
        public readonly uint GoodsId;
        public readonly ulong Price;
        public readonly uint CurrencyType;
        public readonly uint ItemConfigId;
        public readonly uint ItemCount;

        public MysteryMerchantQuote(ulong roundId, uint goodsId, ulong price, uint currencyType, uint itemConfigId, uint itemCount)
        {
            RoundId = roundId;
            GoodsId = goodsId;
            Price = price;
            CurrencyType = currencyType;
            ItemConfigId = itemConfigId;
            ItemCount = itemCount;
        }

        public bool IsValid => GoodsId != 0;

        /// <summary>把货架上的一件商品拍成报价;goods 为空返回 default。</summary>
        public static MysteryMerchantQuote Of(ulong roundId, global::MysteryMerchantGoodsInfo goods) =>
            goods == null
                ? default
                : new MysteryMerchantQuote(roundId, goods.GoodsId, goods.Price, goods.CurrencyType, goods.ItemConfigId, goods.ItemCount);

        public bool Equals(MysteryMerchantQuote other) =>
            RoundId == other.RoundId && GoodsId == other.GoodsId && Price == other.Price
            && CurrencyType == other.CurrencyType && ItemConfigId == other.ItemConfigId && ItemCount == other.ItemCount;

        public override bool Equals(object obj) => obj is MysteryMerchantQuote other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = RoundId.GetHashCode();
                hash = hash * 397 ^ (int)GoodsId;
                hash = hash * 397 ^ Price.GetHashCode();
                hash = hash * 397 ^ (int)CurrencyType;
                hash = hash * 397 ^ (int)ItemConfigId;
                hash = hash * 397 ^ (int)ItemCount;
                return hash;
            }
        }

        public static bool operator ==(MysteryMerchantQuote left, MysteryMerchantQuote right) => left.Equals(right);
        public static bool operator !=(MysteryMerchantQuote left, MysteryMerchantQuote right) => !left.Equals(right);
    }

    /// <summary>
    /// 神秘商人的纯规则与文案(服务端设计:docs/design/mystery-merchant.md §6)。
    /// 不引用 UnityEngine、不碰网络:数据层与界面都从这里取同一条规则,EditMode 测试直接调。
    /// 展示数据直接用 proto(<see cref="global::MysteryMerchantInfo"/> / <see cref="global::MysteryMerchantGoodsInfo"/>),
    /// 不另造一套平行结构;客户端零配表,名称、图标、价格全部来自服务端。
    /// </summary>
    public static class MysteryMerchantModels
    {
        public const string MerchantName = "神秘商人";
        /// <summary>服务端单次购买的份数上限(BuyMysteryMerchantGoodsRequest.count 的范围 1..99)。</summary>
        public const uint MaxBuyCountPerRequest = 99;

        public const string AwayText = "神秘商人已经离开。";
        public const string UnknownNextText = "神秘商人行踪不定，请留意公告。";
        public const string NotReadyText = "请稍候再试。";
        public const string NotHereText = "神秘商人不在这条线上。";
        public const string LimitReachedText = "本轮已达限购数量。";
        public const string InBattleText = "战斗中不能购买。";
        public const string ShelfChangedText = "货架已经更新，请重新查看。";
        public const string ArrivingText = "即将出现";

        /// <summary>
        /// 按服务端时刻判定商人相对本玩家在哪。
        /// 「在场」只认时间窗 [appear_at_ms, expire_at_ms):info.Active 是服务端应答那一刻的结论,
        /// 客户端拿着它过了离开时刻就不再可信,所以这里按 <paramref name="serverNowMs"/> 重判。
        /// 「在本线」只认 scene_id 相等:公告里的 here 恒为 false,不能直接用。
        /// </summary>
        public static MysteryMerchantPresence Evaluate(global::MysteryMerchantInfo info, ulong currentSceneId, ulong serverNowMs)
        {
            if (info == null) return MysteryMerchantPresence.Unknown;
            if (!info.Active || serverNowMs < info.AppearAtMs || serverNowMs >= info.ExpireAtMs)
                return MysteryMerchantPresence.Away;
            return currentSceneId != 0 && info.SceneId == currentSceneId
                ? MysteryMerchantPresence.Here
                : MysteryMerchantPresence.Elsewhere;
        }

        /// <summary>距离商人离开还有多少毫秒;不在场(或 info 为空)返回 0。</summary>
        public static long RemainingMs(global::MysteryMerchantInfo info, ulong serverNowMs)
        {
            if (info == null || !info.Active || serverNowMs >= info.ExpireAtMs || serverNowMs < info.AppearAtMs) return 0;
            return (long)(info.ExpireAtMs - serverNowMs);
        }

        /// <summary>距离下一次出现还有多少毫秒;未知(服务端还没有任何一轮的记录)返回 -1,已经到点返回 0。</summary>
        public static long UntilNextMs(global::MysteryMerchantInfo info, ulong serverNowMs)
        {
            if (info == null || info.NextAppearAtMs == 0) return -1;
            return info.NextAppearAtMs > serverNowMs ? (long)(info.NextAppearAtMs - serverNowMs) : 0;
        }

        /// <summary>线名,写法与线路面板一致(「3线」;0 = 「线路」)。</summary>
        public static string LineName(uint channelNo) => SceneChannelModels.LineName(channelNo);

        /// <summary>
        /// 折扣角标:60 →「6折」,45 →「4.5折」,100 →「原价」。服务端目前只会给 60 / 50 / 40,
        /// 其它值照规则显示而不是硬编码三档:策划加一档不必改客户端。
        /// </summary>
        public static string DiscountLabel(uint discountPercent)
        {
            if (discountPercent == 0 || discountPercent >= 100) return "原价";
            uint tens = discountPercent / 10, ones = discountPercent % 10;
            return ones == 0
                ? tens.ToString(CultureInfo.InvariantCulture) + "折"
                : tens.ToString(CultureInfo.InvariantCulture) + "." + ones.ToString(CultureInfo.InvariantCulture) + "折";
        }

        /// <summary>货币叫法,与背包窗 / 帮会窗一致:0 银两、1 灵石、2 绑定灵石。</summary>
        public static string CurrencyName(uint currencyType) => currencyType switch
        {
            0 => "银两",
            1 => "灵石",
            2 => "绑定灵石",
            _ => "货币",
        };

        /// <summary>金额千分位。固定用不变区域(理由同 GuildClient.FormatAmount)。</summary>
        public static string FormatAmount(ulong value) => value.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>
        /// 倒计时:不足一小时「mm:ss」,否则「h:mm:ss」。向上取整到秒(还剩 0.2 秒时显示 00:01,不显示 00:00)。
        /// 非正数一律「00:00」。
        /// </summary>
        public static string FormatCountdown(long milliseconds)
        {
            if (milliseconds <= 0) return "00:00";
            long totalSeconds = (milliseconds + 999) / 1000;
            long hours = totalSeconds / 3600, minutes = totalSeconds / 60 % 60, seconds = totalSeconds % 60;
            return hours > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, seconds)
                : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", minutes, seconds);
        }

        /// <summary>商品名;服务端没给名字时退回「物品 · 配置号」(与背包窗同一写法),不编造名字。</summary>
        public static string GoodsName(global::MysteryMerchantGoodsInfo goods)
        {
            if (goods == null) return "";
            return string.IsNullOrWhiteSpace(goods.Name)
                ? "物品 · " + goods.ItemConfigId.ToString(CultureInfo.InvariantCulture)
                : goods.Name;
        }

        /// <summary>这件商品本轮还能买几份。</summary>
        public static uint Remaining(global::MysteryMerchantGoodsInfo goods) =>
            goods == null || goods.BoughtCount >= goods.LimitCount ? 0 : goods.LimitCount - goods.BoughtCount;

        /// <summary>
        /// 一件商品此刻能不能买(买一份)。界面置灰与数据层发请求前的校验用的是同一条规则。
        ///
        /// **刻意不看余额**:客户端手里的余额是某次快照(开背包窗、上一次购买),之后打怪、交易、邮件都会改它,
        /// 而 scene 不推送余额变化。按一份过期的余额把按钮置灰,玩家攒够了钱也点不了,而且没有任何办法自己解开。
        /// 余额只拿来显示;买不买得起由服务端裁决(kMysteryMerchantCurrencyNotEnough),距离、背包空间同理。
        /// </summary>
        public static MysteryMerchantBuyBlock EvaluateBuy(global::MysteryMerchantGoodsInfo goods, MysteryMerchantPresence presence,
            bool ready, bool inBattle)
        {
            if (!ready) return MysteryMerchantBuyBlock.NotReady;
            // 先判在不在本线:不在本线时本来就没有货架(goods 为空),该说的是「不在这条线上」而不是「请稍候」。
            if (presence != MysteryMerchantPresence.Here) return MysteryMerchantBuyBlock.NotHere;
            if (goods == null) return MysteryMerchantBuyBlock.NotReady;
            if (inBattle) return MysteryMerchantBuyBlock.InBattle;
            if (Remaining(goods) == 0) return MysteryMerchantBuyBlock.LimitReached;
            return MysteryMerchantBuyBlock.None;
        }

        public static string DescribeBlock(MysteryMerchantBuyBlock block) => block switch
        {
            MysteryMerchantBuyBlock.None => "",
            MysteryMerchantBuyBlock.NotHere => NotHereText,
            MysteryMerchantBuyBlock.LimitReached => LimitReachedText,
            MysteryMerchantBuyBlock.InBattle => InBattleText,
            _ => NotReadyText,
        };

        /// <summary>
        /// 出现公告的文案。在本线:「神秘商人出现在本线，停留 30 分钟」;在别的线:「神秘商人出现在 3线，停留 30 分钟」。
        /// 分钟数向上取整(还剩 29 分 10 秒说 30 分钟);不足一分钟说「即将离开」。
        /// </summary>
        public static string AnnouncementText(uint channelNo, bool here, long remainingMs)
        {
            string where = here ? "本线" : LineName(channelNo);
            if (remainingMs <= 0) return MerchantName + "出现在" + where + "。";
            long minutes = (remainingMs + 59_999) / 60_000;
            return minutes <= 1
                ? MerchantName + "出现在" + where + "，即将离开。"
                : MerchantName + "出现在" + where + "，停留 " + minutes.ToString(CultureInfo.InvariantCulture) + " 分钟。";
        }

        /// <summary>
        /// HUD 角标文案;null = 不显示角标。
        ///  - 在本线:「神秘商人 本线 12:34」(倒计时是离开时刻);
        ///  - 在别的线:「神秘商人 3线 12:34」;
        ///  - 不在场且知道下一次:「神秘商人 45:10 后出现」;
        ///  - 不在场、下一次的时刻已经到了(服务端在整点后的头几秒还没排出新一轮):「神秘商人 即将出现」;
        ///  - 不在场且不知道下一次、或还没问到:不显示。
        /// </summary>
        public static string EntryLabel(MysteryMerchantPresence presence, uint channelNo, long remainingMs, long untilNextMs)
        {
            switch (presence)
            {
                case MysteryMerchantPresence.Here:
                    return MerchantName + " 本线 " + FormatCountdown(remainingMs);
                case MysteryMerchantPresence.Elsewhere:
                    return MerchantName + " " + LineName(channelNo) + " " + FormatCountdown(remainingMs);
                case MysteryMerchantPresence.Away:
                    if (untilNextMs < 0) return null;
                    return untilNextMs == 0
                        ? MerchantName + " " + ArrivingText
                        : MerchantName + " " + FormatCountdown(untilNextMs) + " 后出现";
                default:
                    return null;
            }
        }

        /// <summary>
        /// 购买被拒时的文案。mystery_merchant_error 的码逐个给;通用码里只认限流与服务不可用,
        /// 其余一律带编号兜底(不手抄别的域的数字,AGENTS §7.5)。
        /// </summary>
        public static string DescribeTip(uint tipId) => tipId switch
        {
            (uint)mystery_merchant_error.KMysteryMerchantNotPresent => "神秘商人已经离开，请留意下次出现的公告。",
            (uint)mystery_merchant_error.KMysteryMerchantNotHere => NotHereText,
            (uint)mystery_merchant_error.KMysteryMerchantTooFar => "离神秘商人太远了，走近一些再买。",
            (uint)mystery_merchant_error.KMysteryMerchantShelfChanged => ShelfChangedText,
            (uint)mystery_merchant_error.KMysteryMerchantLimitReached => LimitReachedText,
            (uint)mystery_merchant_error.KMysteryMerchantCurrencyNotEnough => "货币不足，无法购买。",
            (uint)mystery_merchant_error.KMysteryMerchantBagFull => "背包已满，无法购买。",
            (uint)mystery_merchant_error.KMysteryMerchantInBattle => InBattleText,
            (uint)mystery_merchant_error.KMysteryMerchantFrozen => "角色正在传送，暂时不能购买。",
            (uint)mystery_merchant_error.KMysteryMerchantInvalidCount => "购买数量无效。",
            (uint)mystery_merchant_error.KMysteryMerchantItemBlocked => "这件商品暂时无法购买。",
            (uint)mystery_merchant_error.KMysteryMerchantInternalError => "神秘商人暂时无法交易，请稍后再试。",
            (uint)common_error.KRateLimitExceeded => "操作太快了，请稍候再试。",
            (uint)common_error.KServiceUnavailable => "服务器繁忙，请稍后再试。",
            _ => "购买未完成（" + tipId.ToString(CultureInfo.InvariantCulture) + "），请稍后再试。",
        };

        /// <summary>这个拒绝码是不是说明本地手里的货架 / 行踪已经过时,应当立刻重新问一次。</summary>
        public static bool TipMeansStale(uint tipId) =>
            tipId == (uint)mystery_merchant_error.KMysteryMerchantNotPresent
            || tipId == (uint)mystery_merchant_error.KMysteryMerchantNotHere
            || tipId == (uint)mystery_merchant_error.KMysteryMerchantShelfChanged
            || tipId == (uint)mystery_merchant_error.KMysteryMerchantLimitReached;

        /// <summary>
        /// 查询连续失败 <paramref name="failures"/> 次(从 1 起)之后,隔多少秒自动再问一次:5、10、20、40,之后固定 60。
        /// 查询是只读的幂等请求,失败了就该自己重试 —— 否则进场那一次查询丢了,玩家要等到下一次换场景才看得到商人。
        /// </summary>
        public static float InfoRetryDelaySeconds(int failures)
        {
            if (failures <= 1) return 5f;
            if (failures >= 5) return 60f;
            return 5f * (1 << (failures - 1));
        }

        /// <summary>同一个「下一次出现时刻」最多自动查询几次。</summary>
        public const int MaxAutoRefreshAttempts = 3;
        /// <summary>自动查询按角色 id 错开的范围(秒):全服的客户端不在同一秒一起问。</summary>
        public const uint AutoRefreshJitterSeconds = 20;

        /// <summary>
        /// 到了「下一次出现」的时刻而公告没来时,第 <paramref name="attempt"/> 次(从 0 起)自动查询安排在那个时刻之后
        /// 多少毫秒;-1 = 不再自动查询。
        ///
        /// 正常情况下公告会先到(scene_manager 每 5 秒调度一拍、scene 节点每 5 秒读一次记录,最晚约 10 秒),
        /// 自动查询只是给「恰好错过公告」(那一刻正在换场景)和「这一轮没排出来」(没有可用的线)兜底:
        ///  - 第 0 次:12 秒后 —— 公告该到而没到;
        ///  - 第 1 次:45 秒后 —— 服务端在整点后有 30 秒宽限期(那段时间它回答的「下一次」仍是刚过去的这个整点),
        ///    过了宽限期再问,它会回答真正的下一轮,角标从「即将出现」变成倒计时;
        ///  - 第 2 次:120 秒后 —— 最后再确认一次。
        /// 每一次都加上按角色 id 算的 0..19 秒偏移。问回来的「下一次出现时刻」变了,次数从头算。
        /// </summary>
        public static long AutoRefreshDelayMs(int attempt, ulong playerId)
        {
            long baseSeconds;
            switch (attempt)
            {
                case 0: baseSeconds = 12; break;
                case 1: baseSeconds = 45; break;
                case 2: baseSeconds = 120; break;
                default: return -1;
            }
            return (baseSeconds + (long)(playerId % AutoRefreshJitterSeconds)) * 1000L;
        }

        /// <summary>购买成功的提示:「购买成功：回血丹 ×10」。</summary>
        public static string PurchasedText(global::MysteryMerchantGoodsInfo goods, uint count)
        {
            ulong total = goods == null ? 0 : (ulong)goods.ItemCount * count;
            return "购买成功：" + GoodsName(goods) + " ×" + total.ToString(CultureInfo.InvariantCulture);
        }
    }
}
