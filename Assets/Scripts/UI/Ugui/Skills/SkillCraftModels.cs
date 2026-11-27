using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Google.Protobuf;
using MmorpgClient.Game.PlayerFeatures;
using TradePb = Trade;

namespace MmorpgClient.UI.Ugui.Skills
{
    public enum ProxyStallSortMode { Reputation = 0, SupremeCount = 1, LowestFee = 2, HotRank = 3 }
    public enum ProxyStallFilterMode { All = 0, ForgeMaster = 1, ScrollMaster = 2, TeamFree = 3 }

    /// <summary>
    /// 典籍制书、未学书架与主城名匠代打摊位的纯 C# 展示与资格推导模型（不依赖 UnityEngine，可直接无头单测）。
    /// 仅基于服务端下发的 SkillCraftingStateSnapshot / CraftProxyStallInfo 推导按钮可用态与文案，不本地扣费或改快照。
    /// </summary>
    public static class SkillCraftModels
    {
        public const ulong ProxyFeeMinGold = 50;
        public const ulong ProxyFeeMaxGold = 100;
        public const uint MasterTier = 3;
        public const uint SealedSkillBookConfigId = 3414;
        public const uint MaxShowcaseTag = 4;
        public const uint MaxBatchDismantleCount = 20;
        public const ulong WantedOrderMinGold = 100;
        public const ulong WantedOrderMaxGold = 50000;
        public const int MaxActiveWantedOrders = 3;

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

        public static bool CanSealBook(CraftedSkillBookEntry book) => book != null && book.RarityTier == 0 && !book.Bound;

        public static string DescribeSealedBook(BagItemInfo item, Func<uint, string> skillNameResolver = null)
        {
            var sealedInfo = item?.SealedSkillBook;
            if (sealedInfo == null) return string.Empty;
            string skillName = !string.IsNullOrWhiteSpace(sealedInfo.SkillName)
                ? sealedInfo.SkillName
                : skillNameResolver?.Invoke(sealedInfo.SkillTableId);
            if (string.IsNullOrWhiteSpace(skillName)) skillName = $"技能 · {sealedInfo.SkillTableId}";
            string scrollName = !string.IsNullOrWhiteSpace(sealedInfo.ScrollName)
                ? sealedInfo.ScrollName
                : (sealedInfo.ScrollId != 0 ? $"卷宗 · {sealedInfo.ScrollId.ToString(CultureInfo.InvariantCulture)}" : "常规卷宗");
            string bind = item.Bound ? "仅自用" : "可交易";
            string signer = string.IsNullOrWhiteSpace(sealedInfo.CrafterName) ? string.Empty
                : $" · {FormatCrafterSignature(sealedInfo.CrafterName)} 亲手撰";
            return $"【背包封卷】{skillName}（{scrollName} · {bind}{signer}）";
        }

        public static string FormatCrafterSignature(string crafterName)
        {
            if (string.IsNullOrWhiteSpace(crafterName)) return string.Empty;
            return crafterName.Contains("·") ? crafterName : $"宗师·{crafterName}";
        }

        public static string ShowcaseTagName(uint tag) => tag switch
        {
            1 => "专攻极品铸器",
            2 => "专攻典籍制卷",
            3 => "平价快单接单",
            4 => "双绝天工名匠",
            _ => "综合宗师",
        };

        public static string CraftTitleName(uint titleId) => titleId switch
        {
            1 => "铸器宗师",
            2 => "百炼宗师",
            3 => "天工神匠",
            4 => "制卷宗师",
            5 => "万卷书圣",
            6 => "双绝天工",
            _ => "默认宗师",
        };

        public static bool CanOpenProxyStall(SkillCraftingStateSnapshot state) => state != null &&
            ((state.ForgeMastery?.Tier ?? 0) >= MasterTier || (state.ScrollMastery?.Tier ?? 0) >= MasterTier);

        public static bool IsValidProxyFee(ulong feeGold) => feeGold >= ProxyFeeMinGold && feeGold <= ProxyFeeMaxGold;

        public static bool IsValidWantedRewardGold(ulong gold) => gold >= WantedOrderMinGold && gold <= WantedOrderMaxGold;

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
            string signer = string.IsNullOrWhiteSpace(book.CrafterName) ? string.Empty
                : $" · {FormatCrafterSignature(book.CrafterName)} 亲手撰";
            return $"{skillName}（{scrollName} · {rarity} · {bind}{signer}）";
        }

        public static List<CraftProxyStallInfo> SortAndFilterProxyStalls(IEnumerable<CraftProxyStallInfo> stalls,
            ProxyStallSortMode sortMode, ProxyStallFilterMode filterMode)
        {
            if (stalls == null) return new List<CraftProxyStallInfo>();
            var filtered = stalls.Where(s => s != null && s.MasterPlayerId != 0 && filterMode switch
            {
                ProxyStallFilterMode.ForgeMaster => s.ForgeMasteryTier >= MasterTier && s.ProxyForgeFeeGold > 0,
                ProxyStallFilterMode.ScrollMaster => s.ScrollMasteryTier >= MasterTier && s.ProxyScrollFeeGold > 0,
                ProxyStallFilterMode.TeamFree => s.ViewerGetsFreeProxy || s.TeamFreeProxy,
                _ => true,
            });
            return sortMode switch
            {
                ProxyStallSortMode.HotRank => filtered
                    .OrderBy(s => s.HotRank > 0 && s.HotRank <= 3 ? s.HotRank : uint.MaxValue)
                    .ThenByDescending(s => HotRankScore(s))
                    .ThenByDescending(s => s.Recent24HSupreme)
                    .ThenByDescending(s => s.SupremeForgedCount)
                    .ThenByDescending(s => s.ProxyOrdersCompleted)
                    .ThenBy(s => s.MasterPlayerId).ToList(),
                ProxyStallSortMode.SupremeCount => filtered
                    .OrderByDescending(s => s.SupremeForgedCount)
                    .ThenByDescending(s => s.ProxyOrdersCompleted)
                    .ThenBy(s => s.MasterPlayerId).ToList(),
                ProxyStallSortMode.LowestFee => filtered
                    .OrderBy(s => s.ViewerGetsFreeProxy ? 0UL : MinActiveFee(s))
                    .ThenByDescending(s => s.ProxyOrdersCompleted)
                    .ThenBy(s => s.MasterPlayerId).ToList(),
                _ => filtered
                    .OrderByDescending(s => s.ViewerGetsFreeProxy)
                    .ThenByDescending(s => s.ProxyOrdersCompleted)
                    .ThenByDescending(s => s.SupremeForgedCount)
                    .ThenBy(s => s.MasterPlayerId).ToList(),
            };
        }

        public static ulong HotRankScore(CraftProxyStallInfo s)
            => s == null ? 0UL : (ulong)s.Recent24HSupreme * 3UL + s.Recent24HOrders;

        public static List<CraftProxyStallInfo> TopHotStalls(IEnumerable<CraftProxyStallInfo> stalls)
        {
            if (stalls == null) return new List<CraftProxyStallInfo>();
            return stalls
                .Where(s => s != null && s.MasterPlayerId != 0 && s.HotRank >= 1 && s.HotRank <= 3)
                .OrderBy(s => s.HotRank)
                .ThenBy(s => s.MasterPlayerId)
                .Take(3)
                .ToList();
        }

        public static string HotRankLabel(uint rank) => rank switch
        {
            1 => "榜首",
            2 => "榜眼",
            3 => "探花",
            _ => string.Empty,
        };

        public static List<string> DecodeUnlockedTitleNames(uint newlyUnlockedTitleMask)
        {
            var names = new List<string>();
            for (uint id = 1; id <= 6; ++id)
            {
                if ((newlyUnlockedTitleMask & (1u << (int)(id - 1))) != 0u)
                    names.Add(CraftTitleName(id));
            }
            return names;
        }

        public static string FormatDigestLines(CraftProxyDigestSummary digest)
        {
            if (digest == null) return "暂无未读名匠代打营业战报。";
            var lines = new List<string>
            {
                $"离线/跨线期间共接单：{digest.OrdersCompleted} 单",
                $"产出金标极品：{digest.SupremeForgedCount} 件  |  撰出稀世绝学：{digest.RareBooksCraftedCount} 本",
                $"累计税后净收益（95%）：{digest.NetGoldEarned} 银两（已自动寄往邮箱，随时可领）",
            };
            if (!string.IsNullOrWhiteSpace(digest.LastBuyerName))
                lines.Add($"最近下单侠士：{digest.LastBuyerName}");
            var titles = DecodeUnlockedTitleNames(digest.NewlyUnlockedTitleMask);
            if (titles.Count > 0)
                lines.Add($"新解锁零属性名匠雅号：{string.Join("、", titles)}（可在摊位设置中免费佩戴）");
            return string.Join("\n", lines);
        }

        private static ulong MinActiveFee(CraftProxyStallInfo s)
        {
            ulong min = ulong.MaxValue;
            if (s.ForgeMasteryTier >= MasterTier && s.ProxyForgeFeeGold > 0) min = Math.Min(min, s.ProxyForgeFeeGold);
            if (s.ScrollMasteryTier >= MasterTier && s.ProxyScrollFeeGold > 0) min = Math.Min(min, s.ProxyScrollFeeGold);
            return min == ulong.MaxValue ? 0UL : min;
        }

        public static string DescribeProxyStall(CraftProxyStallInfo stall)
        {
            if (stall == null) return string.Empty;
            string name = string.IsNullOrWhiteSpace(stall.MasterName) ? $"宗师#{stall.MasterPlayerId}" : stall.MasterName;
            string displayName = stall.EquippedCraftTitleId != 0
                ? $"{CraftTitleName(stall.EquippedCraftTitleId)}·{name}"
                : name;
            var parts = new List<string>();
            if (stall.HotRank >= 1 && stall.HotRank <= 3) parts.Add($"【热榜{HotRankLabel(stall.HotRank)}】");
            parts.Add($"{displayName}【{ShowcaseTagName(stall.MasterShowcaseTag)}】");
            if (stall.ViewerGetsFreeProxy) parts.Add("★同队0费情义代打");
            else if (stall.TeamFreeProxy) parts.Add("支持同队免税");
            string repPart = $"口碑 {stall.ProxyOrdersCompleted} 单 · 极品 {stall.SupremeForgedCount} · 稀世 {stall.RareBooksCraftedCount}";
            if (stall.Recent24HOrders > 0) repPart += $" · 近24h {stall.Recent24HOrders}单/{stall.Recent24HSupreme}极品";
            parts.Add(repPart);
            if (stall.ForgeMasteryTier >= MasterTier && stall.ProxyForgeFeeGold > 0)
            {
                string feeText = stall.ViewerGetsFreeProxy ? "同队免费" : $"{stall.ProxyForgeFeeGold} 银两";
                parts.Add($"铸器({EquipDisplay.FormatSuccessRate(stall.TopQualityRateBps)}·{feeText})");
            }
            if (stall.ScrollMasteryTier >= MasterTier && stall.ProxyScrollFeeGold > 0)
            {
                string feeText = stall.ViewerGetsFreeProxy ? "同队免费" : $"{stall.ProxyScrollFeeGold} 银两";
                parts.Add($"制卷({EquipDisplay.FormatSuccessRate(stall.ScrollBonusRateBps)}·{feeText})");
            }
            return string.Join(" | ", parts);
        }

        // ── V4 典籍藏经阁：里程碑位图、批量安全拆页与 6 大流派缺书体检 ──
        public static bool HasBit(ByteString bits, uint bitIndex)
        {
            if (bits == null || bits.Length == 0) return false;
            int byteIdx = (int)(bitIndex / 8u);
            if (byteIdx < 0 || byteIdx >= bits.Length) return false;
            return (bits[byteIdx] & (1 << (int)(bitIndex % 8u))) != 0;
        }

        public static uint ScrollHalfThreshold(uint totalSkills) => totalSkills == 0 ? 0u : (totalSkills + 1u) / 2u;

        public readonly struct ScrollMilestoneStatus
        {
            public readonly uint ScrollId;
            public readonly uint UnlockedCount;
            public readonly uint TotalCount;
            public readonly uint HalfThreshold;
            public readonly bool HalfClaimed;
            public readonly bool FullClaimed;
            public bool HalfClaimable => TotalCount > 0 && !HalfClaimed && UnlockedCount >= HalfThreshold;
            public bool FullClaimable => TotalCount > 0 && !FullClaimed && UnlockedCount >= TotalCount;

            public ScrollMilestoneStatus(uint scrollId, uint unlockedCount, uint totalCount, uint halfThreshold,
                bool halfClaimed, bool fullClaimed)
            {
                ScrollId = scrollId;
                UnlockedCount = unlockedCount;
                TotalCount = totalCount;
                HalfThreshold = halfThreshold;
                HalfClaimed = halfClaimed;
                FullClaimed = fullClaimed;
            }
        }

        public static ScrollMilestoneStatus GetScrollMilestoneStatus(SkillCraftingStateSnapshot state, SkillScrollCatalogInfo scroll)
        {
            if (state == null || scroll == null || scroll.ScrollId == 0)
                return new ScrollMilestoneStatus(0, 0, 0, 0, false, false);
            var unlockedSet = new HashSet<uint>(state.BookUnlockedSkillIds);
            uint total = (uint)scroll.SkillTableIds.Count;
            uint unlocked = 0;
            foreach (uint sid in scroll.SkillTableIds)
                if (unlockedSet.Contains(sid)) ++unlocked;
            uint halfBit = (scroll.ScrollId - 1u) * 2u;
            uint fullBit = halfBit + 1u;
            return new ScrollMilestoneStatus(scroll.ScrollId, unlocked, total, ScrollHalfThreshold(total),
                HasBit(state.ClaimedScrollMilestoneBits, halfBit),
                HasBit(state.ClaimedScrollMilestoneBits, fullBit));
        }

        /// <summary>V4 安全一键勾选可拆常规书：仅选已习得或书架多余重复的常规书，永不误选孤本未学书或稀世绝学。</summary>
        public static List<ulong> SelectSafeBatchDismantleBookIds(SkillCraftingStateSnapshot state)
        {
            var result = new List<ulong>();
            if (state == null || state.SkillBooks.Count == 0) return result;
            var unlocked = new HashSet<uint>(state.BookUnlockedSkillIds);
            var keptUnlearned = new HashSet<uint>();
            foreach (var book in state.SkillBooks.OrderBy(b => b.BookId))
            {
                if (book == null || book.RarityTier != 0) continue;
                if (unlocked.Contains(book.SkillTableId) || !keptUnlearned.Add(book.SkillTableId))
                {
                    result.Add(book.BookId);
                    if (result.Count >= MaxBatchDismantleCount) break;
                }
            }
            return result;
        }

        public sealed class BuildBookArchetype
        {
            public readonly string Name;
            public readonly string Summary;
            public readonly uint[] SkillIds;
            public BuildBookArchetype(string name, string summary, params uint[] skillIds)
            { Name = name; Summary = summary; SkillIds = skillIds; }
        }

        public static readonly BuildBookArchetype[] BuildArchetypes =
        {
            new BuildBookArchetype("物理破阵流", "前期横戈速推 + 中后期愈战破甲收割", 10002u, 10039u, 12001u, 12020u, 12024u),
            new BuildBookArchetype("法术爆发流", "群法压血 + 深谋叠伤 + 玄镜/法反博弈", 10008u, 10041u, 12002u, 12021u, 12019u),
            new BuildBookArchetype("反击铁壁流", "灵鼎护元援护抗压 + 反戈承锋越打越痛", 10014u, 10036u, 12003u, 12009u, 12022u, 12023u),
            new BuildBookArchetype("毒火持续流", "蔓毒焚心铺场 + 蚀骨愈深叠伤 + 毒火引爆", 10019u, 10029u, 10030u, 10031u, 12025u),
            new BuildBookArchetype("护盾治疗流", "太清灵盾厚泽叠盾 + 青莲/生生不息保核", 10010u, 10012u, 10040u, 10026u, 12005u, 12027u),
            new BuildBookArchetype("控场解控流", "双封锁招 + 清心驱散 + 磐石道心抗控", 10021u, 10022u, 10033u, 10034u, 12026u),
        };

        public readonly struct MissingSkillTarget
        {
            public readonly uint SkillId;
            public readonly uint ScrollId;
            public MissingSkillTarget(uint skillId, uint scrollId) { SkillId = skillId; ScrollId = scrollId; }
        }

        public sealed class BuildBookAdvice
        {
            public readonly BuildBookArchetype Archetype;
            public readonly int UnlockedCount;
            public readonly List<MissingSkillTarget> MissingSkills;
            public BuildBookAdvice(BuildBookArchetype archetype, int unlockedCount, List<MissingSkillTarget> missing)
            { Archetype = archetype; UnlockedCount = unlockedCount; MissingSkills = missing; }
        }

        public static List<BuildBookAdvice> EvaluateBuildBookAdvisor(SkillCraftingStateSnapshot state)
        {
            var unlocked = state != null ? new HashSet<uint>(state.BookUnlockedSkillIds) : new HashSet<uint>();
            var skillToScroll = new Dictionary<uint, uint>();
            if (state != null)
            {
                foreach (var sc in state.Scrolls)
                {
                    if (sc == null) continue;
                    foreach (uint sid in sc.SkillTableIds) skillToScroll[sid] = sc.ScrollId;
                }
            }
            var list = new List<BuildBookAdvice>(BuildArchetypes.Length);
            foreach (var arch in BuildArchetypes)
            {
                int own = 0;
                var missing = new List<MissingSkillTarget>();
                foreach (uint sid in arch.SkillIds)
                {
                    if (unlocked.Contains(sid)) ++own;
                    else missing.Add(new MissingSkillTarget(sid, skillToScroll.TryGetValue(sid, out uint scId) ? scId : 0u));
                }
                list.Add(new BuildBookAdvice(arch, own, missing));
            }
            return list;
        }

        // ── V5 西市悬赏求书板(trade 求购单):文案与匹配交卷来源检索 ──
        // 揭榜净得用服务端给的 seller_net_gold,客户端不自己算税。
        public static string DescribeWantedOrder(TradePb.WantedSkillBookOrderView order, Func<uint, string> skillNameResolver = null)
        {
            if (order == null) return string.Empty;
            string skill = ResolveWantedSkillName(order, skillNameResolver);
            string sig = order.RequireMasterSignature ? " · 须宗师署名" : string.Empty;
            string poster = string.IsNullOrWhiteSpace(order.PosterName) ? $"道友#{order.PosterPlayerId}" : order.PosterName;
            return $"求购【{skill}】{sig} | 悬赏 {order.RewardGold} 银两（揭榜净得 {order.SellerNetGold} 银两） | 发榜：{poster}";
        }

        public static string ResolveWantedSkillName(TradePb.WantedSkillBookOrderView order, Func<uint, string> skillNameResolver = null)
            => order == null ? string.Empty
                : !string.IsNullOrWhiteSpace(order.SkillName) ? order.SkillName
                : (skillNameResolver?.Invoke(order.SkillTableId) ?? $"技能 · {order.SkillTableId}");

        /// <summary>求购单阶段文案(服务端按合同状态推导的 WantedSkillBookOrderPhase)。</summary>
        public static string DescribeWantedPhase(TradePb.WantedSkillBookOrderPhase phase) => phase switch
        {
            TradePb.WantedSkillBookOrderPhase.Escrowing => "托管中",
            TradePb.WantedSkillBookOrderPhase.Open => "挂榜中",
            TradePb.WantedSkillBookOrderPhase.Fulfilling => "交易中",
            TradePb.WantedSkillBookOrderPhase.Delivering => "交付中",
            TradePb.WantedSkillBookOrderPhase.Settled => "已成交",
            TradePb.WantedSkillBookOrderPhase.Cancelling => "撤单中",
            TradePb.WantedSkillBookOrderPhase.Cancelled => "已撤单",
            TradePb.WantedSkillBookOrderPhase.Manual => "转人工核对",
            _ => "状态未知",
        };

        /// <summary>只有托管中与挂榜中可撤;有人交书(交易中)或交付中撤不了。</summary>
        public static bool CanCancelWantedOrder(TradePb.WantedSkillBookOrderView order)
            => order != null && (order.Phase == TradePb.WantedSkillBookOrderPhase.Escrowing
                                 || order.Phase == TradePb.WantedSkillBookOrderPhase.Open);

        public readonly struct WantedFulfillMatch
        {
            public readonly ulong SealedItemId;
            public readonly ulong BookshelfBookId;
            public bool Found => SealedItemId != 0 || BookshelfBookId != 0;
            public WantedFulfillMatch(ulong sealedItemId, ulong bookshelfBookId)
            { SealedItemId = sealedItemId; BookshelfBookId = bookshelfBookId; }
        }

        /// <summary>检索玩家当前可用于揭榜的未绑定匹配技能书（优先匹配背包 3414 封卷道籍，其次书架未绑定常规书）。</summary>
        public static WantedFulfillMatch FindMatchingFulfillSource(TradePb.WantedSkillBookOrderView order,
            SkillCraftingStateSnapshot state, BagInfo characterBag)
        {
            if (order == null || order.SkillTableId == 0) return default;
            if (characterBag != null)
            {
                foreach (var it in characterBag.Items)
                {
                    if (it == null || it.ItemId == 0 || it.Count == 0 || it.Bound) continue;
                    if (it.ConfigId != SealedSkillBookConfigId || it.SealedSkillBook == null) continue;
                    var sb = it.SealedSkillBook;
                    if (sb.RarityTier != 0 || sb.SkillTableId != order.SkillTableId) continue;
                    if (order.RequireMasterSignature && (string.IsNullOrWhiteSpace(sb.CrafterName) || sb.CrafterPlayerId == 0)) continue;
                    return new WantedFulfillMatch(it.ItemId, 0);
                }
            }
            if (state != null)
            {
                foreach (var book in state.SkillBooks)
                {
                    if (book == null || book.BookId == 0 || book.Bound || book.RarityTier != 0) continue;
                    if (book.SkillTableId != order.SkillTableId) continue;
                    if (order.RequireMasterSignature && (string.IsNullOrWhiteSpace(book.CrafterName) || book.CrafterPlayerId == 0)) continue;
                    return new WantedFulfillMatch(0, book.BookId);
                }
            }
            return default;
        }
    }
}
