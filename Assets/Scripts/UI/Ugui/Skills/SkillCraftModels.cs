using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MmorpgClient.Game.PlayerFeatures;

namespace MmorpgClient.UI.Ugui.Skills
{
    /// <summary>
    /// 典籍制书、未学书架与主城名匠代打摊位的纯 C# 展示与资格推导模型（不依赖 UnityEngine，可直接无头单测）。
    /// 仅基于服务端下发的 SkillCraftingStateSnapshot / CraftProxyStallInfo 推导按钮可用态与文案，不本地扣费或改快照。
    /// </summary>
    public static class SkillCraftModels
    {
        public const ulong ProxyFeeMinGold = 50;
        public const ulong ProxyFeeMaxGold = 100;
        public const uint MasterTier = 3;

        public static string FormatMastery(SkillCraftMasterySnapshot mastery, bool isScroll)
        {
            string title = isScroll ? "制卷手艺" : "铸器手艺";
            if (mastery == null || mastery.Tier == 0) return $"{title}：未同步";
            string tierName = string.IsNullOrWhiteSpace(mastery.TierName) ? $"档位 {mastery.Tier}" : mastery.TierName;
            string rateLabel = isScroll ? "额外返页率" : "极品率";
            string progress = mastery.NextTierExp == 0 ? "已满宗师" : $"{mastery.Exp}/{mastery.NextTierExp}";
            return $"{title} · {tierName}（{rateLabel} {EquipDisplay.FormatSuccessRate(mastery.BonusRateBps)} · {progress}）";
        }

        public static bool CanCraftRegularRandom(SkillCraftingStateSnapshot state, SkillScrollCatalogInfo scroll,
            ulong proxyFeeGold, out string reason)
        {
            reason = string.Empty;
            if (state == null || scroll == null) { reason = "卷宗信息未同步"; return false; }
            if (scroll.RarityTier != 0) { reason = "稀世绝学卷仅支持定向制书"; return false; }
            if (state.FragmentOwned < scroll.RandomFragmentCount)
            { reason = $"悟道残页不足（需 {scroll.RandomFragmentCount}，持有 {state.FragmentOwned}）"; return false; }
            ulong totalGold = scroll.RandomGoldCost + proxyFeeGold;
            if (state.GoldBalance < totalGold)
            { reason = $"银两不足（需 {totalGold}，持有 {state.GoldBalance}）"; return false; }
            return true;
        }

        public static bool CanCraftRegularTargeted(SkillCraftingStateSnapshot state, SkillScrollCatalogInfo scroll,
            uint targetSkillId, ulong proxyFeeGold, out string reason)
        {
            reason = string.Empty;
            if (state == null || scroll == null) { reason = "卷宗信息未同步"; return false; }
            if (scroll.RarityTier != 0) { reason = "稀世绝学卷请使用稀世定向制书"; return false; }
            if (targetSkillId == 0 || !scroll.SkillTableIds.Contains(targetSkillId))
            { reason = "请先在本卷宗内选择目标技能"; return false; }
            if (state.FragmentOwned < scroll.TargetedFragmentCount)
            { reason = $"悟道残页不足（需 {scroll.TargetedFragmentCount}，持有 {state.FragmentOwned}）"; return false; }
            ulong totalGold = scroll.TargetedGoldCost + proxyFeeGold;
            if (state.GoldBalance < totalGold)
            { reason = $"银两不足（需 {totalGold}，持有 {state.GoldBalance}）"; return false; }
            return true;
        }

        public static bool CanCraftRareTargeted(SkillCraftingStateSnapshot state, SkillScrollCatalogInfo scroll,
            uint targetSkillId, ulong proxyFeeGold, out string reason)
        {
            reason = string.Empty;
            if (state == null || scroll == null) { reason = "卷宗信息未同步"; return false; }
            if (scroll.RarityTier != 1) { reason = "该卷宗不是稀世绝学卷"; return false; }
            if (targetSkillId == 0 || !scroll.SkillTableIds.Contains(targetSkillId))
            { reason = "请先选择要定向合成的稀世绝学"; return false; }
            if (state.RareScrollOwned < scroll.RareScrollCount)
            { reason = $"绝学残卷不足（需 {scroll.RareScrollCount}，持有 {state.RareScrollOwned}）"; return false; }
            if (state.RareInkOwned < scroll.RareInkCount)
            { reason = $"天工秘墨不足（需 {scroll.RareInkCount}，持有 {state.RareInkOwned}；可玩法长线积累或官方商城直购）"; return false; }
            ulong totalGold = scroll.RareGoldCost + proxyFeeGold;
            if (state.GoldBalance < totalGold)
            { reason = $"银两不足（需 {totalGold}，持有 {state.GoldBalance}）"; return false; }
            return true;
        }

        public static bool CanDismantleBook(CraftedSkillBookEntry book) => book != null && book.RarityTier == 0;

        public static bool CanOpenProxyStall(SkillCraftingStateSnapshot state) => state != null &&
            ((state.ForgeMastery?.Tier ?? 0) >= MasterTier || (state.ScrollMastery?.Tier ?? 0) >= MasterTier);

        public static bool IsValidProxyFee(ulong feeGold) => feeGold >= ProxyFeeMinGold && feeGold <= ProxyFeeMaxGold;

        public static string ScrollNameOf(SkillCraftingStateSnapshot state, uint scrollId)
        {
            if (scrollId == 0) return string.Empty;
            var scroll = state?.Scrolls.FirstOrDefault(s => s != null && s.ScrollId == scrollId);
            if (scroll != null && !string.IsNullOrWhiteSpace(scroll.Name)) return scroll.Name;
            return $"卷宗 · {scrollId.ToString(CultureInfo.InvariantCulture)}";
        }

        public static uint DismantleRefundOf(SkillCraftingStateSnapshot state, CraftedSkillBookEntry book)
        {
            if (book == null || book.RarityTier != 0) return 0;
            var scroll = state?.Scrolls.FirstOrDefault(s => s != null && s.ScrollId == book.ScrollId);
            return scroll?.DismantleFragmentCount ?? 16u;
        }

        public static string DescribeBook(CraftedSkillBookEntry book, SkillCraftingStateSnapshot state,
            Func<uint, string> skillNameResolver = null)
        {
            if (book == null) return string.Empty;
            string skillName = skillNameResolver?.Invoke(book.SkillTableId);
            if (string.IsNullOrWhiteSpace(skillName)) skillName = $"技能 · {book.SkillTableId}";
            string scrollName = ScrollNameOf(state, book.ScrollId);
            string rarity = book.RarityTier == 1 ? "稀世绝学" : "常规";
            string bind = book.Bound ? "仅自用" : "可交易";
            string signer = string.IsNullOrWhiteSpace(book.CrafterName) ? string.Empty : $" · 宗师·{book.CrafterName} 亲手撰";
            return $"{skillName}（{scrollName} · {rarity} · {bind}{signer}）";
        }

        public static string DescribeProxyStall(CraftProxyStallInfo stall)
        {
            if (stall == null) return string.Empty;
            string name = string.IsNullOrWhiteSpace(stall.MasterName) ? $"宗师#{stall.MasterPlayerId}" : stall.MasterName;
            var parts = new List<string> { name };
            if (stall.ForgeMasteryTier >= MasterTier && stall.ProxyForgeFeeGold > 0)
                parts.Add($"铸器宗师（极品率 {EquipDisplay.FormatSuccessRate(stall.TopQualityRateBps)} · 代打 {stall.ProxyForgeFeeGold} 银两）");
            if (stall.ScrollMasteryTier >= MasterTier && stall.ProxyScrollFeeGold > 0)
                parts.Add($"制卷宗师（返页率 {EquipDisplay.FormatSuccessRate(stall.ScrollBonusRateBps)} · 代制 {stall.ProxyScrollFeeGold} 银两）");
            return string.Join(" | ", parts);
        }
    }
}
