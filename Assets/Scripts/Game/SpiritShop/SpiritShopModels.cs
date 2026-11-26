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
    /// 购买瞬间向服务端回传的报价快照，用于服务端防热更改价/改内容防呆核验（V-3）。
    /// </summary>
    public readonly struct SpiritShopPurchaseQuote : IEquatable<SpiritShopPurchaseQuote>
    {
        private readonly SpiritShopQuoteItem[] _items;

        public uint GoodsId { get; }
        public uint CurrencyType { get; }
        public ulong UnitPrice { get; }
        public IReadOnlyList<SpiritShopQuoteItem> Items => _items ?? Array.Empty<SpiritShopQuoteItem>();
        public bool IsValid => GoodsId != 0 && UnitPrice != 0 && _items != null && _items.Length > 0;

        private SpiritShopPurchaseQuote(uint goodsId, uint currencyType, ulong unitPrice, SpiritShopQuoteItem[] items)
        {
            GoodsId = goodsId;
            CurrencyType = currencyType;
            UnitPrice = unitPrice;
            _items = items;
        }

        public static SpiritShopPurchaseQuote Of(global::SpiritShopGoodsView goods)
        {
            if (goods == null || goods.GoodsId == 0 || goods.UnitPrice == 0 || goods.Items == null || goods.Items.Count == 0)
                return default;
            var copied = new SpiritShopQuoteItem[goods.Items.Count];
            for (int i = 0; i < goods.Items.Count; i++)
            {
                var entry = goods.Items[i];
                if (entry == null || entry.ItemConfigId == 0 || entry.ItemCount == 0) return default;
                copied[i] = new SpiritShopQuoteItem(entry.ItemConfigId, entry.ItemCount, entry.Bound);
            }
            return new SpiritShopPurchaseQuote(goods.GoodsId, goods.CurrencyType, goods.UnitPrice, copied);
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
            if (GoodsId != other.GoodsId || CurrencyType != other.CurrencyType || UnitPrice != other.UnitPrice)
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
    }

    /// <summary>
    /// 官方灵石百宝阁纯 C# 展示与校验辅助（零 UnityEngine 依赖，供 UI 与 EditMode 单测复用）。
    /// </summary>
    public static class SpiritShopModels
    {
        public const uint TabStarterPacks = 1;
        public const uint TabAdvancement = 2;
        public const uint TabAppearanceAndConvenience = 3;

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
            _ => "百宝阁",
        };

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
            bool inBattle)
        {
            if (!ready) return SpiritShopBuyBlock.NotReady;
            if (inBattle) return SpiritShopBuyBlock.InBattle;
            if (goods == null || goods.GoodsId == 0 || goods.UnitPrice == 0 || goods.Items == null || goods.Items.Count == 0)
                return SpiritShopBuyBlock.InvalidGoods;
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
            || tipId == (uint)spirit_shop_error.KSpiritShopGroupLimitReached;

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

        public static string FormatItemsSummary(global::SpiritShopGoodsView goods, uint count = 1)
        {
            if (goods?.Items == null || goods.Items.Count == 0) return string.Empty;
            uint safeCount = Math.Max(1u, count);
            var sb = new StringBuilder(64);
            for (int i = 0; i < goods.Items.Count; i++)
            {
                var item = goods.Items[i];
                if (item == null || item.ItemConfigId == 0) continue;
                if (sb.Length > 0) sb.Append(" + ");
                ulong total = (ulong)item.ItemCount * safeCount;
                sb.Append(ResolveItemName(item.ItemConfigId))
                    .Append('×')
                    .Append(total.ToString(CultureInfo.InvariantCulture));
                if (item.Bound) sb.Append("(绑定)");
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
