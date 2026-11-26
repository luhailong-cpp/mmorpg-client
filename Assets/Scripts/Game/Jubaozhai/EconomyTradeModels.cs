using System;
using System.Collections.Generic;
using System.Globalization;
using Paymentpb;
using Trade;

namespace MmorpgClient.Game.Jubaozhai
{
    /// <summary>
    /// 经济防骗与交易金额格式化工具：银两严格使用千分位整数明文显示，禁止“万/亿/K”缩写。
    /// </summary>
    public static class EconomyFormat
    {
        public static string FormatInteger(ulong amount)
            => amount.ToString("#,0", CultureInfo.InvariantCulture);

        public static string FormatSilver(ulong amount)
            => FormatInteger(amount) + " 银两";

        public static string CurrencyName(uint type)
            => MmorpgClient.Game.Common.CurrencyLabels.Name(type, "货币 " + type.ToString(CultureInfo.InvariantCulture));

        public static string FormatCurrency(ulong amount, uint currencyType)
            => FormatInteger(amount) + " " + CurrencyName(currencyType);

        public static string FormatMoney(ulong amount, string currency)
        {
            string code = (currency ?? string.Empty).Trim().ToUpperInvariant();
            switch (code)
            {
                case "CNY":
                    return FormatInteger(amount / 100UL) + "." + (amount % 100UL).ToString("D2", CultureInfo.InvariantCulture) + " 元";
                case "USD":
                case "EUR":
                case "HKD":
                case "GBP":
                case "SGD":
                case "TWD":
                    return FormatInteger(amount / 100UL) + "." + (amount % 100UL).ToString("D2", CultureInfo.InvariantCulture) + " " + code;
                case "JPY":
                case "KRW":
                    return FormatInteger(amount) + " " + code;
                case "0":
                case "GOLD":
                    return FormatSilver(amount);
                case "1":
                    return FormatInteger(amount) + " 灵石";
                case "2":
                    return FormatInteger(amount) + " 绑定灵石";
                case "3":
                    return FormatInteger(amount) + " 绑定银两";
                default:
                    return FormatInteger(amount) + (code.Length == 0 ? string.Empty : " " + code);
            }
        }

        public static string FormatInstanceTag(ulong instanceId)
        {
            if (instanceId == 0UL) return string.Empty;
            string hex = instanceId <= 0xFFFFUL
                ? instanceId.ToString("X4", CultureInfo.InvariantCulture)
                : instanceId.ToString("X", CultureInfo.InvariantCulture);
            return "实例#" + hex;
        }

        public static string FormatDateCn(ulong unixMs)
        {
            if (unixMs == 0UL) return "未记录";
            try
            {
                var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)unixMs).ToOffset(TimeSpan.FromHours(8));
                return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
                return "未记录";
            }
        }
    }

    /// <summary>
    /// 当面交易条款变更差异快照：任一方修改条款或版本变化时，清空本地锁定确认并明文高亮差异。
    /// </summary>
    public sealed class FaceToFaceTradeDiffResult
    {
        public static readonly FaceToFaceTradeDiffResult None = new(false, false, Array.Empty<string>(), string.Empty);

        public bool HasChanges { get; }
        public bool LockCleared { get; }
        public IReadOnlyList<string> DiffLines { get; }
        public string BannerText { get; }

        public FaceToFaceTradeDiffResult(bool hasChanges, bool lockCleared, IReadOnlyList<string> diffLines, string bannerText)
        {
            HasChanges = hasChanges;
            LockCleared = lockCleared;
            DiffLines = diffLines ?? Array.Empty<string>();
            BannerText = bannerText ?? string.Empty;
        }
    }

    public static class FaceToFaceTradeDiffModel
    {
        public static FaceToFaceTradeDiffResult Compare(
            CommerceOrderResponse previous,
            CommerceOrderResponse current,
            ulong viewerPlayerId)
        {
            if (previous?.Order == null || current?.Order == null) return FaceToFaceTradeDiffResult.None;
            if (!string.Equals(previous.Order.OrderId, current.Order.OrderId, StringComparison.Ordinal))
                return FaceToFaceTradeDiffResult.None;

            bool viewerIsSeller = viewerPlayerId != 0UL && current.Order.SellerPlayerId == viewerPlayerId;
            var beforeCounterAssets = viewerIsSeller ? previous.BuyerAssets : previous.SellerAssets;
            var afterCounterAssets = viewerIsSeller ? current.BuyerAssets : current.SellerAssets;
            var beforeCounterItems = viewerIsSeller ? previous.BuyerItemInstances : previous.SellerItemInstances;
            var afterCounterItems = viewerIsSeller ? current.BuyerItemInstances : current.SellerItemInstances;
            var beforeCounterPet = viewerIsSeller ? previous.BuyerPet : previous.SellerPet;
            var afterCounterPet = viewerIsSeller ? current.BuyerPet : current.SellerPet;

            var diffs = new List<string>();
            ulong beforeCurrency = beforeCounterAssets?.CurrencyAmount ?? 0UL;
            ulong afterCurrency = afterCounterAssets?.CurrencyAmount ?? 0UL;
            uint beforeCurrType = beforeCounterAssets?.CurrencyType ?? 0u;
            uint afterCurrType = afterCounterAssets?.CurrencyType ?? 0u;
            if (beforeCurrency != afterCurrency || beforeCurrType != afterCurrType)
            {
                diffs.Add($"对方{EconomyFormat.CurrencyName(afterCurrType)}变更："
                    + $"{EconomyFormat.FormatCurrency(beforeCurrency, beforeCurrType)} → "
                    + $"{EconomyFormat.FormatCurrency(afterCurrency, afterCurrType)}");
            }

            string beforeItemsText = DescribeInstances(beforeCounterAssets, beforeCounterItems, beforeCounterPet);
            string afterItemsText = DescribeInstances(afterCounterAssets, afterCounterItems, afterCounterPet);
            if (!string.Equals(beforeItemsText, afterItemsText, StringComparison.Ordinal))
            {
                diffs.Add($"对方物品变更：{beforeItemsText} → {afterItemsText}");
            }

            bool versionChanged = current.Order.Version > previous.Order.Version;
            bool confirmationChanged = previous.Order.SellerConfirmed != current.Order.SellerConfirmed
                || previous.Order.BuyerConfirmed != current.Order.BuyerConfirmed;
            if (diffs.Count == 0 && !versionChanged && !confirmationChanged)
                return FaceToFaceTradeDiffResult.None;

            if (diffs.Count == 0 && (versionChanged || confirmationChanged))
            {
                diffs.Add($"订单版本已更新（v{previous.Order.Version} → v{current.Order.Version}），请重新核对双方条款。");
            }

            return new FaceToFaceTradeDiffResult(
                hasChanges: true,
                lockCleared: true,
                diffLines: diffs,
                bannerText: "任一方修改条款：双方锁定已清空，请重新核对金额、强化等级与实例号后再确认。");
        }

        public static string DescribeOfferItem(TradeOfferInstance item)
        {
            if (item == null) return "道具";
            string name = string.IsNullOrWhiteSpace(item.Name) ? $"道具#{item.ConfigId}" : item.Name;
            string levelPart = item.Level > 0 ? $" +{item.Level}" : string.Empty;
            string countPart = item.Count > 1 ? $" × {EconomyFormat.FormatInteger(item.Count)}" : string.Empty;
            string instPart = item.InstanceId != 0UL ? $" ({EconomyFormat.FormatInstanceTag(item.InstanceId)})" : string.Empty;
            return name + levelPart + countPart + instPart;
        }

        public static string DescribeInstances(
            TradeAssetSelection assets,
            IEnumerable<TradeOfferInstance> instances,
            TradeOfferInstance pet)
        {
            var parts = new List<string>();
            var describedConfigs = new HashSet<uint>();
            var describedGuids = new HashSet<ulong>();
            if (instances != null)
            {
                foreach (var inst in instances)
                {
                    if (inst == null) continue;
                    parts.Add(DescribeOfferItem(inst));
                    describedConfigs.Add(inst.ConfigId);
                    if (inst.InstanceId != 0UL) describedGuids.Add(inst.InstanceId);
                }
            }
            if (assets != null)
            {
                foreach (var kv in assets.Items)
                    if (!describedConfigs.Contains(kv.Key))
                        parts.Add($"材料#{kv.Key} × {EconomyFormat.FormatInteger(kv.Value)}");
                foreach (var guid in assets.ItemUuids)
                    if (!describedGuids.Contains(guid))
                        parts.Add($"独立道具 ({EconomyFormat.FormatInstanceTag(guid)})");
            }
            if (pet != null && pet.InstanceId != 0UL)
            {
                string petName = string.IsNullOrWhiteSpace(pet.Name) ? $"宠物#{pet.InstanceId}" : pet.Name;
                parts.Add($"{petName} Lv.{pet.Level} ({EconomyFormat.FormatInstanceTag(pet.InstanceId)})");
            }
            else if (assets != null && assets.PetId != 0UL)
            {
                parts.Add($"宠物 ({EconomyFormat.FormatInstanceTag(assets.PetId)})");
            }
            return parts.Count == 0 ? "无" : string.Join("、", parts);
        }

        public static string FormatQuotaSummary(GetPlayerTradeQuotaResponse quota)
        {
            if (quota == null)
                return "正在读取当面交易防骗额度…";
            string scope = quota.Mode == "acquaintance_locked"
                ? "方案B：熟人好友锁 + 参考价偏离保护"
                : "方案A：低价耗材与单日小额银两互助";
            if (quota.DailyCurrencyLimit == 0UL)
                return $"{scope} · 当前无单日银两限额配置";
            return $"{scope} · 今日剩余额度：{EconomyFormat.FormatCurrency(quota.DailyCurrencyRemaining, quota.CurrencyType)} / {EconomyFormat.FormatCurrency(quota.DailyCurrencyLimit, quota.CurrencyType)}";
        }
    }

    public enum SellerCreditAlertMode
    {
        None = 0,
        OfficialTransfer = 1,
        RemoteNewDevice = 2,
    }

    /// <summary>
    /// 卖家店铺信用档案纯数据视图模型：严格基于服务端脱敏下发的 SellerCreditView 映射星级、档位徽标与高危警示。
    /// </summary>
    public sealed class SellerCreditDisplayModel
    {
        public ulong SellerPlayerId { get; }
        public string StarText { get; }
        public string ScoreText { get; }
        public string TierLabel { get; }
        public string TierBadgeKey { get; }
        public string PositiveRateText { get; }
        public string ReviewCountsText { get; }
        public string HolderSinceText { get; }
        public string SummaryLine { get; }
        public string[] StarSliceKeys { get; }
        public SellerCreditAlertMode AlertMode { get; }
        public string AlertBannerText { get; }
        public string AlertDetailText { get; }
        public string AntiWashNote { get; }

        private SellerCreditDisplayModel(
            ulong sellerPlayerId,
            string starText,
            string scoreText,
            string tierLabel,
            string tierBadgeKey,
            string positiveRateText,
            string reviewCountsText,
            string holderSinceText,
            string summaryLine,
            string[] starSliceKeys,
            SellerCreditAlertMode alertMode,
            string alertBannerText,
            string alertDetailText)
        {
            SellerPlayerId = sellerPlayerId;
            StarText = starText;
            ScoreText = scoreText;
            TierLabel = tierLabel;
            TierBadgeKey = tierBadgeKey;
            PositiveRateText = positiveRateText;
            ReviewCountsText = reviewCountsText;
            HolderSinceText = holderSinceText;
            SummaryLine = summaryLine;
            StarSliceKeys = starSliceKeys;
            AlertMode = alertMode;
            AlertBannerText = alertBannerText;
            AlertDetailText = alertDetailText;
            AntiWashNote = "同号 / 同网 / 同设备及极小额交易不计信用分；7 日限 1 次。";
        }

        public static SellerCreditDisplayModel FromProto(SellerCreditView view)
        {
            if (view == null)
            {
                return new SellerCreditDisplayModel(
                    0UL, "3.0★", "60 分", "标准档", "badge_tier_standard",
                    "暂无评价", "0 / 0 / 0", "未记录", "3.0★ / 60 分 · 标准档",
                    ComputeStarSlices(30u), SellerCreditAlertMode.None, string.Empty, string.Empty);
            }

            uint tenths = view.StarRatingTenths == 0u ? 10u : Math.Min(50u, view.StarRatingTenths);
            string starText = (tenths / 10.0).ToString("0.0", CultureInfo.InvariantCulture) + "★";
            string scoreText = view.CreditScore.ToString(CultureInfo.InvariantCulture) + " 分";
            string tierKey = MapTierBadgeKey(view.RiskTier, view.EnvCreditCleared, view.OwnershipChanged);
            string tierLabel = MapTierLabel(tierKey);

            ulong totalReviews = view.PositiveReviews + view.NeutralReviews + view.NegativeReviews;
            string positiveRate = totalReviews == 0UL
                ? "暂无评价"
                : (view.PositiveRateBps / 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";
            string counts = $"{EconomyFormat.FormatInteger(view.PositiveReviews)} / {EconomyFormat.FormatInteger(view.NeutralReviews)} / {EconomyFormat.FormatInteger(view.NegativeReviews)}";
            string holderSince = EconomyFormat.FormatDateCn(view.EpochStartedAtMs > 0UL ? view.EpochStartedAtMs : view.OwnershipChangedAtMs);

            SellerCreditAlertMode alertMode = SellerCreditAlertMode.None;
            string alertBanner = string.Empty;
            string alertDetail = string.Empty;
            if (view.EnvCreditCleared)
            {
                alertMode = SellerCreditAlertMode.RemoteNewDevice;
                alertBanner = "检测到异地陌生新设备登录，历史正面信用已直接清 0";
                alertDetail = $"当前已落至 {starText} / {scoreText}（{tierLabel}）。原有差评（{view.NegativeReviews}）与核实违约（{view.ViolationCount}）黑历史永久保留，不得洗白。\n"
                    + "说明：坐高铁或切换 5G 仅换 IP 不扣分；换新机建议先连接常用 Wi-Fi 后再登录。";
            }
            else if (view.OwnershipChanged)
            {
                alertMode = SellerCreditAlertMode.OfficialTransfer;
                alertBanner = "持有者已变更，历史信用已清空重算";
                alertDetail = $"当前状态：{starText} / {scoreText}（{tierLabel}）。官方过户后自新持有者起算日（{holderSince}）重新累计，过户前正面信用不沿用。";
            }

            string summary = $"{starText} / {scoreText} · {tierLabel}";
            return new SellerCreditDisplayModel(
                view.SellerPlayerId, starText, scoreText, tierLabel, tierKey,
                positiveRate, counts, holderSince, summary,
                ComputeStarSlices(tenths), alertMode, alertBanner, alertDetail);
        }

        public static string[] ComputeStarSlices(uint starRatingTenths)
        {
            uint clamped = Math.Min(50u, starRatingTenths);
            var result = new string[5];
            for (int i = 0; i < 5; i++)
            {
                uint fullThreshold = (uint)((i + 1) * 10);
                uint halfThreshold = (uint)(i * 10 + 5);
                if (clamped >= fullThreshold) result[i] = "credit_star_full";
                else if (clamped >= halfThreshold) result[i] = "credit_star_half";
                else result[i] = "credit_star_empty";
            }
            return result;
        }

        private static string MapTierBadgeKey(string riskTier, bool envCleared, bool ownershipChanged)
        {
            if (envCleared) return "badge_tier_restricted";
            string normalized = (riskTier ?? string.Empty).Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "trusted": return "badge_tier_trusted";
                case "restricted": return "badge_tier_restricted";
                case "new_or_transferred":
                case "new_transfer": return "badge_tier_new_transfer";
                case "standard": return "badge_tier_standard";
                default: return ownershipChanged ? "badge_tier_new_transfer" : "badge_tier_standard";
            }
        }

        private static string MapTierLabel(string badgeKey) => badgeKey switch
        {
            "badge_tier_trusted" => "守信档",
            "badge_tier_restricted" => "受限档",
            "badge_tier_new_transfer" => "换人新店保护期",
            _ => "标准档",
        };
    }

    /// <summary>
    /// 银两集市近 7 日真实成交 p50 与官方指定买家分层公示规则纯数据模型。
    /// </summary>
    public sealed class MarketP50DisplayModel
    {
        public const uint MinValidSamples = 10u;
        public const ulong QuickNoticeMaxFen = 5000UL;

        public bool HideReferenceCurve { get; }
        public string Headline { get; }
        public string SampleText { get; }
        public string SubNotice { get; }

        public MarketP50DisplayModel(bool hideReferenceCurve, string headline, string sampleText, string subNotice)
        {
            HideReferenceCurve = hideReferenceCurve;
            Headline = headline;
            SampleText = sampleText;
            SubNotice = subNotice;
        }

        public static MarketP50DisplayModel FromProto(GetMarketReferencePriceResponse p50)
        {
            if (p50 == null)
            {
                return new MarketP50DisplayModel(
                    true,
                    "正在查询近 7 日真实成交 p50 中位参考价…",
                    "有效样本量：待同步",
                    "样本不足 10 笔时将自动隐藏参考行情。");
            }

            uint windowDays = p50.WindowDays == 0u ? 7u : p50.WindowDays;
            bool insufficient = p50.InsufficientSample || p50.SampleCount < MinValidSamples || p50.MedianUnitPrice == 0UL;
            if (insufficient)
            {
                return new MarketP50DisplayModel(
                    true,
                    "样本不足 10 笔，隐藏参考行情",
                    $"近 {windowDays} 日有效样本量：{EconomyFormat.FormatInteger(p50.SampleCount)} 笔",
                    "为防少量异常单误导定价，有效样本满 10 笔后才展示中位参考价。");
            }

            return new MarketP50DisplayModel(
                false,
                $"近 {windowDays} 日真实成交 p50 中位参考价：{EconomyFormat.FormatCurrency(p50.MedianUnitPrice, p50.CurrencyType)}",
                $"有效样本量：{EconomyFormat.FormatInteger(p50.SampleCount)} 笔",
                "价格源自服务端真实成交记录，仅作参考，最终以实时盘口为准。");
        }

        public static string DescribeNoticeTier(bool isUniqueValuable, ulong rmbPriceFen = 0UL)
        {
            if (!isUniqueValuable && rmbPriceFen <= QuickNoticeMaxFen)
                return "小额标准品：0–2 小时快速公示";
            return "唯一贵重品：48 小时公示";
        }
    }

    /// <summary>
    /// 装备混用绑定材料时的防手滑「长按 2 秒确认绑定」纯 C# 计时状态机（无 Unity 依赖，可单测）。
    /// </summary>
    public sealed class HoldToConfirmTimer
    {
        public const float DefaultRequiredSeconds = 2.0f;

        public float RequiredSeconds { get; }
        public float ElapsedSeconds { get; private set; }
        public bool IsHolding { get; private set; }
        public bool Completed { get; private set; }
        public float Progress => RequiredSeconds <= 0f ? 1f : Math.Min(1f, Math.Max(0f, ElapsedSeconds / RequiredSeconds));

        public HoldToConfirmTimer(float requiredSeconds = DefaultRequiredSeconds)
        {
            RequiredSeconds = requiredSeconds > 0.05f ? requiredSeconds : DefaultRequiredSeconds;
        }

        public void BeginHold()
        {
            if (Completed) return;
            IsHolding = true;
        }

        public bool Advance(float deltaSeconds)
        {
            if (!IsHolding || Completed) return false;
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds <= 0f) return false;
            ElapsedSeconds += deltaSeconds;
            if (ElapsedSeconds >= RequiredSeconds)
            {
                ElapsedSeconds = RequiredSeconds;
                IsHolding = false;
                Completed = true;
                return true;
            }
            return false;
        }

        public void CancelHold()
        {
            IsHolding = false;
            if (!Completed) ElapsedSeconds = 0f;
        }

        public void Reset()
        {
            IsHolding = false;
            Completed = false;
            ElapsedSeconds = 0f;
        }
    }

    public enum OfficialShopTab
    {
        LightCard = 0,
        PermanentWardrobe = 1,
        ArtisanSupport = 2,
        CatchupBundle = 3,
    }

    /// <summary>
    /// 官方 7:3 小额直售商城分栏与合规规则模型（iOS IAP 隔离，商城陈列页严禁微信/支付宝/扫码外链图标）。
    /// </summary>
    public static class OfficialShopCatalogModel
    {
        public static readonly string[] TabTitles =
        {
            "仙途轻享卡",
            "永久衣柜",
            "匠心配套",
            "限量追赶包",
        };

        public static OfficialShopTab ClassifySku(OfficialSkuView sku)
        {
            string title = (sku?.Title ?? string.Empty).Trim();
            if (title.Contains("轻享卡") || title.Contains("月卡") || title.Contains("挂机卡"))
                return OfficialShopTab.LightCard;
            if (title.Contains("外观") || title.Contains("衣柜") || title.Contains("时装") || title.Contains("形象"))
                return OfficialShopTab.PermanentWardrobe;
            if (title.Contains("追赶") || title.Contains("礼匣") || title.Contains("首套"))
                return OfficialShopTab.CatchupBundle;
            return OfficialShopTab.ArtisanSupport;
        }

        /// <summary>
        /// 商城商品展示卡片严格禁止绘出微信/支付宝/扫码等外链支付图标；具体支付渠道仅在订单付款阶段按平台安全拉起。
        /// </summary>
        public static bool AllowThirdPartyPaymentIconsInShop(PayClientPlatform platform) => false;

        /// <summary>iOS 端只允许展示与拉起 App Store IAP。</summary>
        public static bool IsChannelAllowedOnPlatform(PayChannel channel, PayClientPlatform platform)
        {
            if (platform == PayClientPlatform.IosApp)
                return channel == PayChannel.Apple;
            return channel != PayChannel.Unspecified;
        }

        public static string PlatformPurchaseComplianceNote(PayClientPlatform platform)
        {
            return platform == PayClientPlatform.IosApp
                ? "iOS 端仅支持 App Store 应用内购买（IAP），不展示任何外部扫码或第三方支付入口。"
                : "商城内不展示第三方扫码外链图标；创建订单后将按当前发行平台调起官方合规收银台。";
        }
    }

    public enum RmbConsignmentSubTab
    {
        All = 0,
        PublicNotice = 1,
        DesignatedOnly = 2,
        Publish = 3,
        Income = 4,
    }

    public readonly struct RmbIncomeSummary
    {
        public readonly ulong WithdrawableFen;
        public readonly ulong ReservedFen;
        public readonly ulong DisputeFrozenFen;
        public readonly string FirstDisputeCaseId;

        public RmbIncomeSummary(ulong withdrawableFen, ulong reservedFen, ulong disputeFrozenFen, string firstDisputeCaseId)
        {
            WithdrawableFen = withdrawableFen;
            ReservedFen = reservedFen;
            DisputeFrozenFen = disputeFrozenFen;
            FirstDisputeCaseId = firstDisputeCaseId ?? string.Empty;
        }
    }

    /// <summary>珍宝阁(RMB)寄售筛选与卖家收益汇总纯 C# 推导模型（与 EconomyTradeV2 预览 01/02 对齐，零 Unity 依赖）。</summary>
    public static class RmbConsignmentViewModel
    {
        public static readonly string[] SubTabTitles =
        {
            "全服寄售",
            "公示中",
            "指定买家专属",
            "上架寄售",
            "我的收益与提现",
        };

        public static bool IsUniqueValuable(RmbConsignmentView item)
        {
            if (item == null) return false;
            return string.Equals(item.AssetKind, "CHARACTER", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.AssetKind, "PET", StringComparison.OrdinalIgnoreCase)
                || item.PriceFen > MarketP50DisplayModel.QuickNoticeMaxFen;
        }

        public static bool IsInNoticePhase(RmbConsignmentView item)
        {
            if (item == null) return false;
            string phase = item.Phase ?? string.Empty;
            string state = item.State ?? string.Empty;
            return phase.IndexOf("NOTICE", StringComparison.OrdinalIgnoreCase) >= 0
                || state.IndexOf("NOTICE", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static List<RmbConsignmentView> FilterListings(IReadOnlyList<RmbConsignmentView> source, RmbConsignmentSubTab tab)
        {
            var result = new List<RmbConsignmentView>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                var item = source[i];
                if (item == null) continue;
                if (tab == RmbConsignmentSubTab.PublicNotice && !IsInNoticePhase(item)) continue;
                if (tab == RmbConsignmentSubTab.DesignatedOnly && !item.DesignatedOnly) continue;
                result.Add(item);
            }
            return result;
        }

        public static RmbIncomeSummary SummarizeIncomes(IReadOnlyList<RmbSellerIncome> incomes)
        {
            ulong withdrawable = 0UL;
            ulong reserved = 0UL;
            ulong frozen = 0UL;
            string firstCase = string.Empty;
            if (incomes != null)
            {
                for (int i = 0; i < incomes.Count; i++)
                {
                    var inc = incomes[i];
                    if (inc == null) continue;
                    reserved += inc.ReservedFen;
                    frozen += inc.DisputeFrozenFen;
                    if (inc.DisputeFrozenFen > 0UL && string.IsNullOrEmpty(firstCase))
                        firstCase = inc.DisputeCaseId ?? string.Empty;
                    ulong unavailable = inc.PaidFen + inc.ReservedFen + inc.DisputeFrozenFen;
                    if (inc.NetFen > unavailable) withdrawable += inc.NetFen - unavailable;
                }
            }
            return new RmbIncomeSummary(withdrawable, reserved, frozen, firstCase);
        }

        public static string FormatIncomeRowStatus(RmbSellerIncome inc)
        {
            if (inc == null) return "未知";
            if (inc.DisputeFrozenFen > 0UL) return "争议冻结";
            if (inc.ReservedFen > 0UL) return "结算期预留";
            ulong unavailable = inc.PaidFen + inc.ReservedFen + inc.DisputeFrozenFen;
            return inc.NetFen > unavailable ? "可提现" : "已结清";
        }
    }

    public readonly struct MarketWishlistAlert
    {
        public readonly bool HasTarget;
        public readonly ulong TargetPrice;
        public readonly bool HasValidP50;
        public readonly ulong CurrentP50;
        public readonly bool BelowOrAtTarget;

        public MarketWishlistAlert(bool hasTarget, ulong targetPrice, bool hasValidP50, ulong currentP50, bool belowOrAtTarget)
        {
            HasTarget = hasTarget;
            TargetPrice = targetPrice;
            HasValidP50 = hasValidP50;
            CurrentP50 = currentP50;
            BelowOrAtTarget = belowOrAtTarget;
        }
    }

    /// <summary>集市心愿单与到价飞鸽提醒纯 C# 模型：仅在服务端近 7 日 p50 样本充足（≥10 笔）且 ≤ 目标价时触发到价提醒。</summary>
    public sealed class MarketWishlistModel
    {
        private readonly Dictionary<string, ulong> _targets = new Dictionary<string, ulong>(StringComparer.Ordinal);

        public static string KeyOf(uint itemConfigId, uint currencyType)
            => itemConfigId.ToString(CultureInfo.InvariantCulture) + ":" + currencyType.ToString(CultureInfo.InvariantCulture);

        public void SetTarget(uint itemConfigId, uint currencyType, ulong targetPrice)
        {
            string key = KeyOf(itemConfigId, currencyType);
            if (itemConfigId == 0u || targetPrice == 0UL) _targets.Remove(key);
            else _targets[key] = targetPrice;
        }

        public void RemoveTarget(uint itemConfigId, uint currencyType) => _targets.Remove(KeyOf(itemConfigId, currencyType));

        public void Clear() => _targets.Clear();

        public bool TryGetTarget(uint itemConfigId, uint currencyType, out ulong targetPrice)
            => _targets.TryGetValue(KeyOf(itemConfigId, currencyType), out targetPrice);

        public MarketWishlistAlert Evaluate(uint itemConfigId, uint currencyType, GetMarketReferencePriceResponse p50)
        {
            bool hasTarget = TryGetTarget(itemConfigId, currencyType, out ulong target);
            bool validP50 = p50 != null && !p50.InsufficientSample
                && p50.SampleCount >= MarketP50DisplayModel.MinValidSamples && p50.MedianUnitPrice > 0UL;
            ulong median = validP50 ? p50.MedianUnitPrice : 0UL;
            bool hit = hasTarget && validP50 && median <= target;
            return new MarketWishlistAlert(hasTarget, target, validP50, median, hit);
        }

        public string Serialize()
        {
            if (_targets.Count == 0) return string.Empty;
            var parts = new List<string>(_targets.Count);
            foreach (var kv in _targets)
                parts.Add(kv.Key + "=" + kv.Value.ToString(CultureInfo.InvariantCulture));
            return string.Join(";", parts);
        }

        public void LoadSerialized(string raw)
        {
            _targets.Clear();
            if (string.IsNullOrWhiteSpace(raw)) return;
            var parts = raw.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0 || eq >= parts[i].Length - 1) continue;
                string key = parts[i].Substring(0, eq).Trim();
                if (ulong.TryParse(parts[i].Substring(eq + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong price) && price > 0UL)
                    _targets[key] = price;
            }
        }
    }

    public enum PaymentCashierBadgeKind
    {
        None = 0,
        Waiting = 1,
        Settled = 2,
        Revoked = 3,
    }

    /// <summary>官方直售商城与订单支付收银台纯 C# 推导模型（零 Unity 依赖，严禁以本地轮询窗代替服务端订单截止时间）。</summary>
    public static class PaymentCashierViewModel
    {
        /// <summary>pay_countdown_fill.png 左右九宫角帽总和 16+16=32px；宽度小于此阈值时须隐藏填充，避免角帽挤压。</summary>
        public const float CountdownMinVisibleWidthPx = 32f;

        public static PaymentCashierBadgeKind ResolveBadge(CommerceOrderState state, bool paymentWatching)
        {
            if (state == CommerceOrderState.Delivered || state == CommerceOrderState.Settled)
                return PaymentCashierBadgeKind.Settled;
            if (state == CommerceOrderState.Reversing || state == CommerceOrderState.Reversed
                || state == CommerceOrderState.Refunded || state == CommerceOrderState.Manual
                || state == CommerceOrderState.Cancelled)
                return PaymentCashierBadgeKind.Revoked;
            if (paymentWatching
                || state == CommerceOrderState.AwaitingPayment
                || state == CommerceOrderState.WaitingConfirmation
                || state == CommerceOrderState.Escrowing
                || state == CommerceOrderState.Delivering
                || state == CommerceOrderState.CancelPending
                || state == CommerceOrderState.RefundPending)
                return PaymentCashierBadgeKind.Waiting;
            return PaymentCashierBadgeKind.None;
        }

        public static string BadgeSliceKey(PaymentCashierBadgeKind kind) => kind switch
        {
            PaymentCashierBadgeKind.Waiting => "badge_pay_waiting",
            PaymentCashierBadgeKind.Settled => "badge_pay_settled",
            PaymentCashierBadgeKind.Revoked => "badge_pay_revoked",
            _ => string.Empty,
        };

        public static string ResolveBadgeSliceKey(CommerceOrderState state, bool paymentWatching)
            => BadgeSliceKey(ResolveBadge(state, paymentWatching));

        public static float ComputeCountdownFillWidth(
            float trackWidthPx,
            ulong remainingMs,
            ulong totalWindowMs,
            float minVisibleWidthPx = CountdownMinVisibleWidthPx)
        {
            if (trackWidthPx <= 0f || minVisibleWidthPx <= 0f || trackWidthPx < minVisibleWidthPx
                || totalWindowMs == 0UL || remainingMs == 0UL)
                return 0f;
            double ratio = remainingMs >= totalWindowMs ? 1d : (double)remainingMs / (double)totalWindowMs;
            float width = (float)(trackWidthPx * ratio);
            return width < minVisibleWidthPx ? 0f : width;
        }

        public static string FormatCountdownText(ulong remainingMs)
        {
            if (remainingMs == 0UL) return "支付时限已到，请刷新订单确认最新状态";
            ulong totalSeconds = (remainingMs + 999UL) / 1000UL;
            ulong hours = totalSeconds / 3600UL;
            ulong minutes = (totalSeconds % 3600UL) / 60UL;
            ulong seconds = totalSeconds % 60UL;
            if (hours > 0UL)
                return $"剩余支付时间：{hours}时{minutes:D2}分{seconds:D2}秒";
            return $"剩余支付时间：{minutes:D2}分{seconds:D2}秒";
        }

        public static bool IsUnsealTalismanSku(OfficialSkuView sku)
        {
            string title = sku?.Title ?? string.Empty;
            return title.Contains("解封") || title.Contains("剥离");
        }

        public static bool IsSkuRowHighlighted(OfficialSkuView sku, ulong selectedSkuId, int rowIndex)
        {
            if (sku == null) return false;
            if (selectedSkuId != 0UL) return sku.SkuId == selectedSkuId;
            return rowIndex == 0 && sku.Available > 0UL;
        }
    }
}
