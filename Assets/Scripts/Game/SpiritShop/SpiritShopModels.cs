using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MmorpgClient.Net;
using MmorpgClient.Table;

namespace MmorpgClient.Game.SpiritShop
{
    /// <summary>
    /// 官方灵石百宝阁 V1 的服务端契约（docs/design/spirit-shop-and-starter-packs-v1.md §2.5）。
    /// 0 表示当前服务端尚未分配该方法；两个消息号均有效且互异时才启用。
    /// </summary>
    public readonly struct SpiritShopMessageIds
    {
        public uint GetInfo { get; }
        public uint BuyGoods { get; }

        public bool IsSupported => GetInfo != 0 && BuyGoods != 0 && GetInfo != BuyGoods;

        public SpiritShopMessageIds(uint getInfo, uint buyGoods)
        {
            GetInfo = getInfo;
            BuyGoods = buyGoods;
        }

        public static SpiritShopMessageIds Generated => new SpiritShopMessageIds(
            MessageIds.GetSpiritShopInfo,
            MessageIds.BuySpiritShopGoods);
    }

    public readonly struct SpiritShopQuoteItem : IEquatable<SpiritShopQuoteItem>
    {
        public uint ItemConfigId { get; }
        public uint ItemCount { get; }
        public bool Bound { get; }

        public SpiritShopQuoteItem(uint itemConfigId, uint itemCount, bool bound)
        {
            ItemConfigId = itemConfigId;
            ItemCount = itemCount;
            Bound = bound;
        }

        public bool Equals(SpiritShopQuoteItem other)
            => ItemConfigId == other.ItemConfigId
                && ItemCount == other.ItemCount
                && Bound == other.Bound;

        public override bool Equals(object obj) => obj is SpiritShopQuoteItem other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ItemConfigId, ItemCount, Bound);

        public static bool operator ==(SpiritShopQuoteItem left, SpiritShopQuoteItem right) => left.Equals(right);
        public static bool operator !=(SpiritShopQuoteItem left, SpiritShopQuoteItem right) => !left.Equals(right);
    }

    /// <summary>
    /// 购买瞬间向服务端回传的报价快照，用于服务端防热更改价/改内容/换顺序防呆核验（V-3 / V2-2）。
    /// </summary>
    public readonly struct SpiritShopPurchaseQuote : IEquatable<SpiritShopPurchaseQuote>
    {
        private readonly SpiritShopQuoteItem[] _items;

        public uint GoodsId { get; }
        public uint CurrencyType { get; }
        public ulong UnitPrice { get; }
        public uint ChosenItemIndex { get; }
        public IReadOnlyList<SpiritShopQuoteItem> Items => _items ?? Array.Empty<SpiritShopQuoteItem>();
        public bool IsValid => GoodsId != 0 && UnitPrice != 0 && _items != null && _items.Length > 0;

        private SpiritShopPurchaseQuote(uint goodsId, uint currencyType, ulong unitPrice, uint chosenItemIndex,
            SpiritShopQuoteItem[] items)
        {
            GoodsId = goodsId;
            CurrencyType = currencyType;
            UnitPrice = unitPrice;
            ChosenItemIndex = chosenItemIndex;
            _items = items;
        }

        public static SpiritShopPurchaseQuote Of(global::SpiritShopGoodsView goods, uint chosenItemIndex = 0)
        {
            if (goods == null || goods.GoodsId == 0 || goods.UnitPrice == 0 || goods.Items == null || goods.Items.Count == 0)
                return default;
            if (goods.GrantMode == SpiritShopModels.GrantModeChooseOne)
            {
                if (goods.Items.Count < 2 || chosenItemIndex >= (uint)goods.Items.Count) return default;
                var chosen = goods.Items[(int)chosenItemIndex];
                if (chosen == null || chosen.ItemConfigId == 0 || chosen.ItemCount == 0) return default;
                return new SpiritShopPurchaseQuote(goods.GoodsId, goods.CurrencyType, goods.UnitPrice, chosenItemIndex,
                    new[] { new SpiritShopQuoteItem(chosen.ItemConfigId, chosen.ItemCount, chosen.Bound) });
            }
            if (goods.GrantMode != SpiritShopModels.GrantModeAll || chosenItemIndex != 0)
                return default;
            var copied = new SpiritShopQuoteItem[goods.Items.Count];
            for (int i = 0; i < goods.Items.Count; i++)
            {
                var entry = goods.Items[i];
                if (entry == null || entry.ItemConfigId == 0 || entry.ItemCount == 0) return default;
                copied[i] = new SpiritShopQuoteItem(entry.ItemConfigId, entry.ItemCount, entry.Bound);
            }
            return new SpiritShopPurchaseQuote(goods.GoodsId, goods.CurrencyType, goods.UnitPrice, 0u, copied);
        }

        public void WriteExpectedItems(Google.Protobuf.Collections.RepeatedField<global::SpiritShopItemEntry> target)
        {
            if (target == null || _items == null) return;
            for (int i = 0; i < _items.Length; i++)
            {
                target.Add(new global::SpiritShopItemEntry
                {
                    ItemConfigId = _items[i].ItemConfigId,
                    ItemCount = _items[i].ItemCount,
                    Bound = _items[i].Bound,
                });
            }
        }

        public bool Equals(SpiritShopPurchaseQuote other)
        {
            if (GoodsId != other.GoodsId || CurrencyType != other.CurrencyType
                || UnitPrice != other.UnitPrice || ChosenItemIndex != other.ChosenItemIndex)
                return false;
            int leftLen = _items?.Length ?? 0;
            int rightLen = other._items?.Length ?? 0;
            if (leftLen != rightLen) return false;
            for (int i = 0; i < leftLen; i++)
            {
                if (!_items[i].Equals(other._items[i])) return false;
            }
            return true;
        }

        public override bool Equals(object obj) => obj is SpiritShopPurchaseQuote other && Equals(other);

        public override int GetHashCode()
        {
            var hc = new HashCode();
            hc.Add(GoodsId);
            hc.Add(CurrencyType);
            hc.Add(UnitPrice);
            hc.Add(ChosenItemIndex);
            if (_items != null)
            {
                for (int i = 0; i < _items.Length; i++) hc.Add(_items[i]);
            }
            return hc.ToHashCode();
        }

        public static bool operator ==(SpiritShopPurchaseQuote left, SpiritShopPurchaseQuote right) => left.Equals(right);
        public static bool operator !=(SpiritShopPurchaseQuote left, SpiritShopPurchaseQuote right) => !left.Equals(right);
    }

    public enum SpiritShopBuyBlock
    {
        None = 0,
        NotReady = 1,
        InBattle = 2,
        InvalidGoods = 3,
        InvalidCount = 4,
        LimitReached = 5,
        GroupLimitReached = 6,
        SaleEnded = 7,
    }

    /// <summary>
    /// 官方灵石百宝阁纯 C# 展示与校验辅助（零 UnityEngine 依赖，供 UI 与 EditMode 单测复用）。
    /// </summary>
    public static class SpiritShopModels
    {
        public const uint TabStarterPacks = 1;
        public const uint TabAdvancement = 2;
        public const uint TabAppearanceAndConvenience = 3;
        public const uint TabFestival = 4;

        public const uint GrantModeAll = 0;
        public const uint GrantModeChooseOne = 1;

        public const uint CurrencyGold = 0;
        public const uint CurrencyDiamond = 1;
        public const uint CurrencyBindDiamond = 2;

        public const uint GoodsStarterPack60 = 101;
        public const uint GoodsStarterPack180 = 102;
        public const uint GoodsStarterPack300 = 103;
        public const uint GoodsStarGuaranteeCharm = 201;
        public const uint GoodsCelestialInk = 202;
        public const uint GoodsAppearanceCard = 301;
        public const uint GoodsRenameCard = 302;
        public const uint GoodsPetMorphCard = 303;
        public const uint GoodsSkillVfxCard = 304;
        public const uint GoodsSpringBundle401 = 401;
        public const uint GoodsSpringChoice402 = 402;

        public const uint ItemCraftMaterial = 3001;
        public const uint ItemRefineStone = 3002;
        public const uint ItemRenameCard = 3401;
        public const uint ItemAppearanceCard = 3402;
        public const uint ItemStarGuaranteeCharm = 3405;
        public const uint ItemCelestialInk = 3408;
        public const uint ItemPetMorphCard = 3411;
        public const uint ItemSkillVfxCard = 3412;

        public const uint StarterLimitGroupId = 1;
        public const uint MaxBuyPerRequestHardCap = 99;

        public static string TabName(uint tabId) => tabId switch
        {
            TabStarterPacks => "首套提速",
            TabAdvancement => "精进配套",
            TabAppearanceAndConvenience => "外观便利",
            TabFestival => "节庆特惠",
            _ => "百宝阁",
        };

        public static bool IsChooseOne(global::SpiritShopGoodsView goods)
            => goods != null && goods.GrantMode == GrantModeChooseOne;

        public static bool HasTabGoods(global::SpiritShopInfo info, uint tabId)
        {
            if (info?.Goods == null || tabId == 0) return false;
            for (int i = 0; i < info.Goods.Count; i++)
            {
                var g = info.Goods[i];
                if (g != null && g.TabId == tabId) return true;
            }
            return false;
        }

        public static bool IsExpired(global::SpiritShopGoodsView goods, ulong serverNowMs)
            => goods != null && goods.EndTimeMs > 0 && serverNowMs > 0 && serverNowMs >= goods.EndTimeMs;

        public static string ComputeDiscountLabel(ulong unitPrice, ulong originalPrice)
        {
            if (originalPrice == 0 || unitPrice == 0 || originalPrice <= unitPrice) return string.Empty;
            double fold = Math.Round((double)unitPrice * 10.0 / originalPrice, 1, MidpointRounding.AwayFromZero);
            fold = Math.Max(0.1, Math.Min(9.9, fold));
            double roundedInt = Math.Round(fold);
            if (Math.Abs(fold - roundedInt) < 0.05)
                return $"{((int)roundedInt).ToString(CultureInfo.InvariantCulture)}折";
            return $"{fold.ToString("0.0", CultureInfo.InvariantCulture)}折";
        }

        public static string FormatRemainingTime(ulong endTimeMs, ulong serverNowMs)
        {
            if (endTimeMs == 0) return string.Empty;
            if (serverNowMs == 0 || serverNowMs >= endTimeMs) return "活动已结束";
            ulong sec = (endTimeMs - serverNowMs) / 1000UL;
            ulong days = sec / 86400UL;
            ulong hours = (sec % 86400UL) / 3600UL;
            ulong mins = (sec % 3600UL) / 60UL;
            ulong remSec = sec % 60UL;
            if (days > 0) return $"剩余 {days}天{hours}小时";
            if (hours > 0) return $"剩余 {hours}小时{mins}分";
            if (mins > 0) return $"剩余 {mins}分{remSec}秒";
            return $"剩余 {Math.Max(1UL, remSec)}秒";
        }

        public static string CurrencyName(uint currencyType) => MmorpgClient.Game.Common.CurrencyLabels.Name(currencyType);

        public static uint EffectiveMaxBuyPerRequest(global::SpiritShopGoodsView goods)
        {
            if (goods == null || goods.MaxBuyPerRequest == 0) return 1;
            return Math.Min(goods.MaxBuyPerRequest, MaxBuyPerRequestHardCap);
        }

        public static uint RemainingGoodsQuota(global::SpiritShopGoodsView goods)
        {
            if (goods == null || goods.LimitCount == 0) return uint.MaxValue;
            return goods.LimitCount > goods.BoughtCount ? goods.LimitCount - goods.BoughtCount : 0;
        }

        public static uint RemainingGroupQuota(global::SpiritShopLimitGroupView group)
        {
            if (group == null) return uint.MaxValue;
            return group.MaxWeight > group.UsedWeight ? group.MaxWeight - group.UsedWeight : 0;
        }

        public static global::SpiritShopLimitGroupView FindLimitGroup(global::SpiritShopInfo info, uint groupId)
        {
            if (info == null || groupId == 0 || info.LimitGroups == null) return null;
            for (int i = 0; i < info.LimitGroups.Count; i++)
            {
                var g = info.LimitGroups[i];
                if (g != null && g.GroupId == groupId) return g;
            }
            return null;
        }

        public static global::SpiritShopGoodsView FindGoods(global::SpiritShopInfo info, uint goodsId)
        {
            if (info == null || goodsId == 0 || info.Goods == null) return null;
            for (int i = 0; i < info.Goods.Count; i++)
            {
                var g = info.Goods[i];
                if (g != null && g.GoodsId == goodsId) return g;
            }
            return null;
        }

        public static List<global::SpiritShopGoodsView> CollectGoodsForTab(global::SpiritShopInfo info, uint tabId)
        {
            var result = new List<global::SpiritShopGoodsView>();
            if (info?.Goods == null) return result;
            for (int i = 0; i < info.Goods.Count; i++)
            {
                var g = info.Goods[i];
                if (g != null && g.TabId == tabId) result.Add(g);
            }
            result.Sort((a, b) =>
            {
                int cmp = a.SortOrder.CompareTo(b.SortOrder);
                return cmp != 0 ? cmp : a.GoodsId.CompareTo(b.GoodsId);
            });
            return result;
        }

        public static uint MaxBuyableCount(global::SpiritShopGoodsView goods, global::SpiritShopLimitGroupView group)
        {
            if (goods == null) return 0;
            uint cap = EffectiveMaxBuyPerRequest(goods);
            if (goods.LimitCount > 0)
                cap = Math.Min(cap, RemainingGoodsQuota(goods));
            if (goods.LimitGroupId > 0 && goods.LimitGroupWeight > 0 && group != null)
            {
                uint remGroup = RemainingGroupQuota(group);
                cap = Math.Min(cap, remGroup / goods.LimitGroupWeight);
            }
            return cap;
        }

        public static SpiritShopBuyBlock EvaluateBuy(
            global::SpiritShopGoodsView goods,
            global::SpiritShopLimitGroupView group,
            uint count,
            bool ready,
            bool inBattle,
            ulong serverNowMs = 0)
        {
            if (!ready) return SpiritShopBuyBlock.NotReady;
            if (inBattle) return SpiritShopBuyBlock.InBattle;
            if (goods == null || goods.GoodsId == 0 || goods.UnitPrice == 0 || goods.Items == null || goods.Items.Count == 0)
                return SpiritShopBuyBlock.InvalidGoods;
            if (IsExpired(goods, serverNowMs))
                return SpiritShopBuyBlock.SaleEnded;
            uint perRequestMax = EffectiveMaxBuyPerRequest(goods);
            if (count == 0 || count > perRequestMax) return SpiritShopBuyBlock.InvalidCount;
            if (goods.LimitCount > 0 && RemainingGoodsQuota(goods) < count)
                return SpiritShopBuyBlock.LimitReached;
            if (goods.LimitGroupId > 0 && group != null)
            {
                ulong needWeight = (ulong)Math.Max(1u, goods.LimitGroupWeight) * count;
                if (needWeight > RemainingGroupQuota(group))
                    return SpiritShopBuyBlock.GroupLimitReached;
            }
            return SpiritShopBuyBlock.None;
        }

        public static string DescribeBlock(SpiritShopBuyBlock block) => block switch
        {
            SpiritShopBuyBlock.NotReady => "尚未连接到游戏。",
            SpiritShopBuyBlock.InBattle => "战斗或观战中无法购买，请结束后再试。",
            SpiritShopBuyBlock.InvalidGoods => "该商品已下架或暂不出售。",
            SpiritShopBuyBlock.InvalidCount => "购买数量不合法。",
            SpiritShopBuyBlock.LimitReached => "该商品已达限购上限。",
            SpiritShopBuyBlock.GroupLimitReached => "该类提速包已达终身限购总额。",
            SpiritShopBuyBlock.SaleEnded => "该限时商品活动已结束，请刷新货架。",
            _ => string.Empty,
        };

        public static string DescribeTip(uint tipId) => tipId switch
        {
            (uint)spirit_shop_error.KSpiritShopUnavailable => "灵石百宝阁暂不可用，请稍后再试。",
            (uint)spirit_shop_error.KSpiritShopGoodsOffShelf => "该商品已下架或暂不出售。",
            (uint)spirit_shop_error.KSpiritShopPriceChanged => "商品价格或内容已更新，已为您刷新货架。",
            (uint)spirit_shop_error.KSpiritShopInvalidCount => "购买数量不合法。",
            (uint)spirit_shop_error.KSpiritShopLimitReached => "该商品已达限购上限。",
            (uint)spirit_shop_error.KSpiritShopGroupLimitReached => "该类提速包已达终身限购总额。",
            (uint)spirit_shop_error.KSpiritShopCurrencyNotEnough => "灵石不足，请先充值灵石（活动赠送绑定灵石不可用于官方直售）。",
            (uint)spirit_shop_error.KSpiritShopBagFull => "背包空间不足，请清理后再试。",
            (uint)spirit_shop_error.KSpiritShopFrozen => "角色当前状态无法购买，请稍后再试。",
            (uint)spirit_shop_error.KSpiritShopInBattle => "战斗中无法购买，请待战斗结束后再试。",
            (uint)spirit_shop_error.KSpiritShopItemBlocked => "该物品当前被禁止获取。",
            (uint)spirit_shop_error.KSpiritShopNotInSaleWindow => "该商品暂未开售或活动已结束。",
            (uint)spirit_shop_error.KSpiritShopLevelNotMet => "角色等级未满足该商品购买条件。",
            (uint)spirit_shop_error.KSpiritShopInvalidChoice => "所选礼盒物品无效，请重新选择。",
            (uint)common_error.KRateLimitExceeded => "操作太快了，请稍候再试。",
            (uint)common_error.KServiceUnavailable => "百宝阁服务暂时不可用，请稍后再试。",
            (uint)common_error.KInvalidParameter => "请求参数无效，请刷新货架后重试。",
            _ => $"购买失败（错误码 {tipId}）。",
        };

        public static bool TipMeansStale(uint tipId) =>
            tipId == (uint)spirit_shop_error.KSpiritShopGoodsOffShelf
            || tipId == (uint)spirit_shop_error.KSpiritShopPriceChanged
            || tipId == (uint)spirit_shop_error.KSpiritShopInvalidCount
            || tipId == (uint)spirit_shop_error.KSpiritShopLimitReached
            || tipId == (uint)spirit_shop_error.KSpiritShopGroupLimitReached
            || tipId == (uint)spirit_shop_error.KSpiritShopNotInSaleWindow
            || tipId == (uint)spirit_shop_error.KSpiritShopLevelNotMet
            || tipId == (uint)spirit_shop_error.KSpiritShopInvalidChoice;

        public static string ResolveItemName(uint itemConfigId)
        {
            var row = itemConfigId != 0 ? ItemTableManager.Instance.FindById(itemConfigId) : null;
            if (row != null && !string.IsNullOrWhiteSpace(row.Name)) return row.Name;
            return itemConfigId switch
            {
                ItemCraftMaterial => "打造材料",
                ItemRefineStone => "百炼石",
                ItemRenameCard => "角色改名卡",
                ItemAppearanceCard => "角色形象卡",
                ItemStarGuaranteeCharm => "定星保底符",
                ItemCelestialInk => "天工秘墨",
                ItemPetMorphCard => "灵宠幻化符",
                ItemSkillVfxCard => "技能特效卷",
                _ => $"道具#{itemConfigId}",
            };
        }

        public static string FormatSingleItem(global::SpiritShopItemEntry item, uint count = 1)
        {
            if (item == null || item.ItemConfigId == 0) return string.Empty;
            uint safeCount = Math.Max(1u, count);
            ulong total = (ulong)item.ItemCount * safeCount;
            string suffix = item.Bound ? "(绑定)" : string.Empty;
            return $"{ResolveItemName(item.ItemConfigId)}×{total.ToString(CultureInfo.InvariantCulture)}{suffix}";
        }

        public static string FormatItemsSummary(global::SpiritShopGoodsView goods, uint count = 1, int chosenIndex = -1)
        {
            if (goods?.Items == null || goods.Items.Count == 0) return string.Empty;
            uint safeCount = Math.Max(1u, count);
            if (IsChooseOne(goods))
            {
                if (chosenIndex >= 0 && chosenIndex < goods.Items.Count)
                    return FormatSingleItem(goods.Items[chosenIndex], safeCount);
                var sbChoice = new StringBuilder(96);
                sbChoice.Append("任选 1 项：");
                bool first = true;
                for (int i = 0; i < goods.Items.Count; i++)
                {
                    string part = FormatSingleItem(goods.Items[i], safeCount);
                    if (string.IsNullOrEmpty(part)) continue;
                    if (!first) sbChoice.Append(" / ");
                    sbChoice.Append(part);
                    first = false;
                }
                return sbChoice.ToString();
            }
            var sb = new StringBuilder(64);
            for (int i = 0; i < goods.Items.Count; i++)
            {
                string part = FormatSingleItem(goods.Items[i], safeCount);
                if (string.IsNullOrEmpty(part)) continue;
                if (sb.Length > 0) sb.Append(" + ");
                sb.Append(part);
            }
            return sb.ToString();
        }

        public static string LimitLabel(global::SpiritShopGoodsView goods, global::SpiritShopLimitGroupView group)
        {
            if (goods == null) return string.Empty;
            if (goods.LimitGroupId > 0 && group != null)
            {
                string cycle = group.ResetKind == 1 ? "本周" : "终身";
                return $"{cycle}共享限购：已用 {group.UsedWeight}/{group.MaxWeight}（本次占 {goods.LimitGroupWeight}/份）";
            }
            if (goods.LimitCount > 0)
            {
                string cycle = goods.LimitResetKind == 1 ? "本周" : "终身";
                return $"{cycle}限购：{goods.BoughtCount}/{goods.LimitCount} 份";
            }
            return "不限购（单次最多 " + EffectiveMaxBuyPerRequest(goods) + " 份）";
        }

        public static string PurchasedText(global::SpiritShopGoodsView goods, uint count)
        {
            string name = !string.IsNullOrWhiteSpace(goods?.Name) ? goods.Name : "商品";
            return count <= 1 ? $"已购买「{name}」，物品已发放至背包。" : $"已购买「{name}」×{count}，物品已发放至背包。";
        }
    }
}
