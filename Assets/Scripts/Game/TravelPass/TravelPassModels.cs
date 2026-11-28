using System;
using System.Globalization;
using System.Text;
using MmorpgClient.Net.Generated;

namespace MmorpgClient.Game.TravelPass
{
    /// <summary>
    /// 游历手记服务端 RPC 消息号契约（由 SceneActivityClientPlayerRpc 自动生成，零手工维护）。
    /// </summary>
    public readonly struct TravelPassMessageIds
    {
        public uint GetState { get; }
        public uint UnlockPremium { get; }
        public uint ClaimReward { get; }
        public uint EquipCosmetic { get; }
        public uint SelectActive { get; }

        public bool IsSupported =>
            GetState != 0 && UnlockPremium != 0 && ClaimReward != 0 && EquipCosmetic != 0
            && GetState != UnlockPremium && GetState != ClaimReward && GetState != EquipCosmetic
            && UnlockPremium != ClaimReward && UnlockPremium != EquipCosmetic && ClaimReward != EquipCosmetic;

        public bool SupportsSelectActive => IsSupported && SelectActive != 0;

        public TravelPassMessageIds(uint getState, uint unlockPremium, uint claimReward, uint equipCosmetic, uint selectActive = 0)
        {
            GetState = getState;
            UnlockPremium = unlockPremium;
            ClaimReward = claimReward;
            EquipCosmetic = equipCosmetic;
            SelectActive = selectActive;
        }

        public static TravelPassMessageIds Generated => new TravelPassMessageIds(
            SceneActivityClientPlayerRpc.GetTravelPassStateMessageId,
            SceneActivityClientPlayerRpc.UnlockTravelPassPremiumMessageId,
            SceneActivityClientPlayerRpc.ClaimTravelPassRewardMessageId,
            SceneActivityClientPlayerRpc.EquipTravelPassCosmeticMessageId,
            SceneActivityClientPlayerRpc.SelectActiveTravelPassMessageId);
    }

    /// <summary>
    /// 解锁典藏轨瞬间回传给服务端的价格/版本快照，用于服务端防热更改价防呆核验。
    /// </summary>
    public readonly struct TravelPassUnlockQuote : IEquatable<TravelPassUnlockQuote>
    {
        public uint PassId { get; }
        public uint CurrencyType { get; }
        public ulong Price { get; }
        public uint ConfigRevision { get; }

        public bool IsValid => PassId != 0 && Price != 0;

        public TravelPassUnlockQuote(uint passId, uint currencyType, ulong price, uint configRevision)
        {
            PassId = passId;
            CurrencyType = currencyType;
            Price = price;
            ConfigRevision = configRevision;
        }

        public static TravelPassUnlockQuote Of(global::TravelPassSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Available || snapshot.PassId == 0 || snapshot.PremiumPrice == 0)
                return default;
            return new TravelPassUnlockQuote(
                snapshot.PassId,
                snapshot.PremiumCurrencyType,
                snapshot.PremiumPrice,
                snapshot.ConfigRevision);
        }

        public bool Equals(TravelPassUnlockQuote other)
            => PassId == other.PassId
                && CurrencyType == other.CurrencyType
                && Price == other.Price
                && ConfigRevision == other.ConfigRevision;

        public override bool Equals(object obj) => obj is TravelPassUnlockQuote other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(PassId, CurrencyType, Price, ConfigRevision);

        public static bool operator ==(TravelPassUnlockQuote left, TravelPassUnlockQuote right) => left.Equals(right);
        public static bool operator !=(TravelPassUnlockQuote left, TravelPassUnlockQuote right) => !left.Equals(right);
    }

    /// <summary>
    /// 青云游历手记纯 C# 展示与校验辅助（零 UnityEngine 依赖，供 UI 与 EditMode 单测复用）。
    /// </summary>
    public static class TravelPassModels
    {
        public const uint ActivityIdTravelPass = 900001;
        public const uint ActivityIdAppearanceShowcase = 900002;
        public const uint ActivityIdSpiritShopShowcase = 900003;

        public const uint CurrencyDiamond = 1;

        public const uint CosmeticSlotFrame = 1;
        public const uint CosmeticSlotTitle = 2;

        public const string FramePrefix = "frame_";
        public const string TitlePrefix = "title_";

        public static bool HasClaimable(global::TravelPassSnapshot snapshot)
            => TotalBookshelfClaimable(snapshot) > 0;

        public static uint TotalClaimable(global::TravelPassSnapshot snapshot)
            => snapshot == null || !snapshot.Available ? 0u : snapshot.ClaimableFreeCount + snapshot.ClaimablePremiumCount;

        public static uint TotalBookshelfClaimable(global::TravelPassSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Available) return 0u;
            uint sum = 0u;
            for (int i = 0; i < snapshot.Bookshelf.Count; ++i)
                sum += snapshot.Bookshelf[i].ClaimableCount;
            return sum > 0u ? sum : TotalClaimable(snapshot);
        }

        public static bool HasOwnedCosmetic(global::TravelPassSnapshot snapshot, string cosmeticKey)
        {
            if (snapshot == null || string.IsNullOrEmpty(cosmeticKey)) return false;
            var owned = snapshot.UnlockedCosmeticKeys;
            for (int i = 0; i < owned.Count; ++i)
            {
                if (string.Equals(owned[i], cosmeticKey, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static uint SlotOfCosmetic(string cosmeticKey)
        {
            if (string.IsNullOrEmpty(cosmeticKey)) return 0u;
            if (cosmeticKey.StartsWith(FramePrefix, StringComparison.Ordinal)) return CosmeticSlotFrame;
            if (cosmeticKey.StartsWith(TitlePrefix, StringComparison.Ordinal)) return CosmeticSlotTitle;
            return 0u;
        }

        public static string ResolveCosmeticName(global::TravelPassSnapshot snapshot, string cosmeticKey)
        {
            if (string.IsNullOrEmpty(cosmeticKey)) return "未佩戴";
            if (snapshot != null)
            {
                for (int i = 0; i < snapshot.Tiers.Count; ++i)
                {
                    var tier = snapshot.Tiers[i];
                    if (string.Equals(tier.FreeCosmeticKey, cosmeticKey, StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(tier.FreeCosmeticName))
                        return tier.FreeCosmeticName;
                    if (string.Equals(tier.PremiumCosmeticKey, cosmeticKey, StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(tier.PremiumCosmeticName))
                        return tier.PremiumCosmeticName;
                }
            }
            return cosmeticKey switch
            {
                "frame_travel_qingyun_s1" => "青云游历头像框",
                "title_travel_free_s1" => "游历初成",
                "title_travel_qingyun_s1" => "青云雅客",
                "frame_travel_xuanshuang_s2" => "玄霜问剑头像框",
                "title_travel_free_s2" => "问剑初成",
                "title_travel_xuanshuang_s2" => "玄霜剑客",
                "frame_travel_chiyan_s3" => "炽焰丹心头像框",
                "title_travel_free_s3" => "丹心初成",
                "title_travel_chiyan_s3" => "赤焰真君",
                "frame_travel_canglan_s4" => "沧澜听雨头像框",
                "title_travel_free_s4" => "听雨初成",
                "title_travel_canglan_s4" => "沧澜雅士",
                "frame_travel_lingmu_s5" => "灵木问道头像框",
                "title_travel_free_s5" => "问道初成",
                "title_travel_lingmu_s5" => "青木隐仙",
                "frame_travel_houtu_s6" => "厚土镇岳头像框",
                "title_travel_free_s6" => "镇岳初成",
                "title_travel_houtu_s6" => "坤岳道宗",
                "frame_lightcard_s1" => "轻享流光头像框",
                "frame_lightcard_season_s1" => "季享流光头像框",
                _ => cosmeticKey,
            };
        }

        public static string FormatTrackReward(
            uint itemId,
            uint itemCount,
            string itemName,
            ulong gold,
            string cosmeticKey,
            string cosmeticName)
        {
            var sb = new StringBuilder(64);
            if (itemId != 0 && itemCount > 0)
            {
                string name = !string.IsNullOrWhiteSpace(itemName) ? itemName : $"道具#{itemId}";
                sb.Append(name).Append('×').Append(itemCount.ToString(CultureInfo.InvariantCulture)).Append("(绑定)");
            }
            if (gold > 0)
            {
                if (sb.Length > 0) sb.Append(" + ");
                sb.Append("银两×").Append(gold.ToString(CultureInfo.InvariantCulture));
            }
            if (!string.IsNullOrEmpty(cosmeticKey))
            {
                if (sb.Length > 0) sb.Append(" + ");
                string kind = cosmeticKey.StartsWith(FramePrefix, StringComparison.Ordinal) ? "头像框·" : "称谓·";
                string name = !string.IsNullOrWhiteSpace(cosmeticName) ? cosmeticName : cosmeticKey;
                sb.Append(kind).Append(name).Append("(零战力)");
            }
            return sb.Length == 0 ? "—" : sb.ToString();
        }

        public static string DescribeTip(uint tipId) => tipId switch
        {
            (uint)travel_pass_error.KTravelPassUnavailable => "游历手记暂不可用，请稍后再试。",
            (uint)travel_pass_error.KTravelPassNotOpen => "本期游历手记尚未开放或已结束解锁窗口。",
            (uint)travel_pass_error.KTravelPassAlreadyUnlocked => "本期典藏手记已解锁，无需重复购买。",
            (uint)travel_pass_error.KTravelPassPriceChanged => "手记配置或价格已更新，已为您刷新最新页面。",
            (uint)travel_pass_error.KTravelPassCurrencyNotEnough => "灵石不足，请先充值灵石（活动赠送绑定灵石不可用于解锁典藏手记）。",
            (uint)travel_pass_error.KTravelPassNotReached => "尚未达到该档位所需游历点，继续正常游玩即可自动累计。",
            (uint)travel_pass_error.KTravelPassPremiumRequired => "该奖励属于典藏轨，解锁典藏手记后即可随时补领。",
            (uint)travel_pass_error.KTravelPassNothingToClaim => "当前没有可领取的档位奖励。",
            (uint)travel_pass_error.KTravelPassBagFull => "背包空间不足，请先清理背包后再领取。",
            (uint)travel_pass_error.KTravelPassItemBlocked => "该档位物品当前被限制发放，请稍后再试。",
            (uint)travel_pass_error.KTravelPassFrozen => "角色当前状态暂不可操作，请稍后再试。",
            (uint)travel_pass_error.KTravelPassInBattle => "战斗或观战中暂不可操作，请结束后再试。",
            (uint)travel_pass_error.KTravelPassCosmeticNotOwned => "尚未解锁该游历纪念外观。",
            (uint)common_error.KRateLimitExceeded => "操作太快了，请稍候再试。",
            (uint)common_error.KServiceUnavailable => "游历手记服务暂时繁忙，请稍后再试。",
            (uint)common_error.KInvalidParameter => "请求参数无效，已为您重新同步进度。",
            _ => $"操作失败（错误码 {tipId}）。",
        };

        public static bool TipMeansStale(uint tipId) =>
            tipId == (uint)travel_pass_error.KTravelPassNotOpen
            || tipId == (uint)travel_pass_error.KTravelPassAlreadyUnlocked
            || tipId == (uint)travel_pass_error.KTravelPassPriceChanged
            || tipId == (uint)travel_pass_error.KTravelPassNotReached
            || tipId == (uint)travel_pass_error.KTravelPassNothingToClaim
            || tipId == (uint)travel_pass_error.KTravelPassCosmeticNotOwned;
    }
}
