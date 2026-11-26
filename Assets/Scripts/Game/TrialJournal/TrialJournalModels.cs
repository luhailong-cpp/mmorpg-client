using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MmorpgClient.Game.TrialJournal
{
    /// <summary>试炼志「前往挑战」跳转目标类型。</summary>
    public enum TrialJumpKind
    {
        None = 0,
        BossTrial = 1,
        Polarity5v5 = 2,
        RankedPvp = 3,
    }

    /// <summary>解析自 TrialJournalEntry.jump_target 的结构化挑战入口。</summary>
    public readonly struct TrialJumpTarget
    {
        public TrialJumpKind Kind { get; }
        public uint DungeonConfigId { get; }
        public uint Difficulty { get; }
        public string VariantKey { get; }
        public string DisplayHint { get; }
        /// <summary>卷四三境明雷绑定的野外地图场景配置 ID（2=八景岛 / 5=碧渊水宫 / 3=月汐湾；其余为 0）。</summary>
        public uint RequiredSceneConfigId { get; }

        public bool IsValid => Kind != TrialJumpKind.None && DungeonConfigId != 0;

        public TrialJumpTarget(TrialJumpKind kind, uint dungeonConfigId, uint difficulty, string variantKey, string displayHint,
            uint requiredSceneConfigId = 0u)
        {
            Kind = kind;
            DungeonConfigId = dungeonConfigId;
            Difficulty = difficulty;
            VariantKey = variantKey ?? "";
            DisplayHint = displayHint ?? "";
            RequiredSceneConfigId = requiredSceneConfigId;
        }
    }

    /// <summary>已解锁的留灯院试炼跋/评语只读快照（严格以服务端 Claimed 里程碑为准）。</summary>
    public sealed class TrialJournalCommentary
    {
        public uint MilestoneId { get; }
        public string Key { get; }
        public string Title { get; }
        public string Body { get; }

        public TrialJournalCommentary(uint milestoneId, string key, string title, string body)
        {
            MilestoneId = milestoneId;
            Key = key ?? "";
            Title = title ?? "";
            Body = body ?? "";
        }
    }

    /// <summary>
    /// 五行试炼志 V2 的纯 C# 推导与文案模型（零 UnityEngine 依赖，WebGL2/IL2CPP 安全，可直接被 EditMode 单测与 uGUI 层调用）。
    /// 所有点亮状态、最佳回合、里程碑达标/已领与拓印余额严格以服务端 <see cref="global::TrialJournalSnapshot"/> 为唯一真相。
    /// </summary>
    public static class TrialJournalModels
    {
        public const uint ScopeAllPermanent = 0u;
        public const uint ChapterBreakthrough = 1u;
        public const uint ChapterThreeRealms = 2u;
        public const uint ChapterSeasonal = 3u;
        public const uint ChapterWildRealms = 4u;

        // 五行试炼志 V3 自选试炼誓约位掩码（与服务端 trial_journal_rules.h 逐位一致）
        public const uint VowNone = 0u;
        public const uint VowBanControl = 1u << 0;
        public const uint VowSingleArtifact = 1u << 1;
        public const uint VowNoPet = 1u << 2;
        public const uint VowTightRounds = 1u << 3;
        public const uint VowMaskAll = VowBanControl | VowSingleArtifact | VowNoPet | VowTightRounds;

        public static readonly uint[] AllVowBits = { VowBanControl, VowSingleArtifact, VowNoPet, VowTightRounds };

        public const string ChallengeCardTagPrefix = "#TJC:";
        public const string ChallengeCardTagSuffix = "#";

        public static bool IsValidVowMask(uint mask) => (mask & ~VowMaskAll) == 0u;

        public static uint CountVows(uint mask)
        {
            uint valid = mask & VowMaskAll;
            uint count = 0u;
            for (int i = 0; i < 4; i++)
            {
                if ((valid & (1u << i)) != 0u) count++;
            }
            return count;
        }

        public static string VowShortLabel(uint vowBit) => vowBit switch
        {
            VowBanControl => "禁控硬破",
            VowSingleArtifact => "单宝轻装",
            VowNoPet => "无宠孤剑",
            VowTightRounds => "紧缩回合",
            _ => "誓约",
        };

        public static string VowDescription(uint vowBit) => vowBit switch
        {
            VowBanControl => "本方禁用控制技，硬解首领机制",
            VowSingleArtifact => "本场仅首槽法宝生效",
            VowNoPet => "本场不召唤灵宠出战",
            VowTightRounds => "回合上限压缩约三成（至少 6 回合）",
            _ => "",
        };

        public static string FormatVowSummary(uint vowMask)
        {
            uint valid = vowMask & VowMaskAll;
            if (valid == 0u) return "无誓约";
            var parts = new List<string>(4);
            for (int i = 0; i < AllVowBits.Length; i++)
            {
                uint bit = AllVowBits[i];
                if ((valid & bit) != 0u) parts.Add(VowShortLabel(bit));
            }
            return string.Join("+", parts);
        }

        public static uint TightenRoundLimit(uint baseRounds)
        {
            if (baseRounds <= 6u) return baseRounds;
            uint scaled = (baseRounds * 7u + 9u) / 10u;
            return scaled < 6u ? 6u : scaled;
        }

        public static bool TryGetWildSceneBossInfo(uint sceneConfigId, out uint entryId, out uint dungeonConfigId, out string bossTitle)
        {
            switch (sceneConfigId)
            {
                case 2u:
                    entryId = 401u; dungeonConfigId = 113u; bossTitle = "风眼妖尊"; return true;
                case 5u:
                    entryId = 402u; dungeonConfigId = 114u; bossTitle = "水镜妖尊"; return true;
                case 3u:
                    entryId = 403u; dungeonConfigId = 115u; bossTitle = "潮汐妖尊"; return true;
                default:
                    entryId = 0u; dungeonConfigId = 0u; bossTitle = ""; return false;
            }
        }

        public static string WildSceneName(uint sceneConfigId) => sceneConfigId switch
        {
            2u => "八景岛",
            3u => "月汐湾",
            5u => "碧渊水宫",
            _ => $"野外地图#{sceneConfigId}",
        };

        /// <summary>生成可直接投入世界/队伍/私聊频道的约战帖消息（总长严格 <= 120 字，内嵌 #TJC:card_id#）。</summary>
        public static string FormatChallengeCardChatDraft(Match.TrialChallengeCardView card, global::TrialJournalSnapshot snapshot = null)
        {
            if (card == null || card.CardId == 0ul) return "";
            var entry = FindEntry(snapshot, card.JournalEntryId);
            string title = entry != null && !string.IsNullOrEmpty(entry.DisplayName)
                ? entry.DisplayName
                : $"试炼格#{card.JournalEntryId}";
            string vowText = card.VowMask != 0u ? $" · 誓约:{FormatVowSummary(card.VowMask)}" : "";
            string partyText = card.PartySize > 1u ? $"{card.PartySize}人" : "单人";
            string text = $"【试炼约战】{title} {partyText}{card.TotalRounds}回合{vowText} 来破纪录 {ChallengeCardTagPrefix}{card.CardId}{ChallengeCardTagSuffix}";
            if (text.Length > 120)
            {
                text = $"【试炼约战】{title} {card.TotalRounds}回合 {ChallengeCardTagPrefix}{card.CardId}{ChallengeCardTagSuffix}";
            }
            return text;
        }

        /// <summary>从聊天消息中解析权威约战帖 card_id（格式 #TJC:&lt;uint64&gt;#）。</summary>
        public static bool TryParseChallengeCardId(string text, out ulong cardId)
        {
            cardId = 0ul;
            if (string.IsNullOrEmpty(text)) return false;
            int start = text.IndexOf(ChallengeCardTagPrefix, StringComparison.Ordinal);
            if (start < 0) return false;
            int numStart = start + ChallengeCardTagPrefix.Length;
            int end = text.IndexOf(ChallengeCardTagSuffix, numStart, StringComparison.Ordinal);
            if (end <= numStart) return false;
            string slice = text.Substring(numStart, end - numStart);
            return ulong.TryParse(slice, NumberStyles.None, CultureInfo.InvariantCulture, out cardId) && cardId != 0ul;
        }

        /// <summary>移除展示正文中的 #TJC:card_id# 协议标记，保留可读邀请文案。</summary>
        public static string StripChallengeCardTag(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int start = text.IndexOf(ChallengeCardTagPrefix, StringComparison.Ordinal);
            if (start < 0) return text;
            int end = text.IndexOf(ChallengeCardTagSuffix, start + ChallengeCardTagPrefix.Length, StringComparison.Ordinal);
            if (end < 0) return text;
            string before = text.Substring(0, start).TrimEnd();
            string after = text.Substring(end + ChallengeCardTagSuffix.Length).TrimStart();
            if (before.Length == 0) return after;
            if (after.Length == 0) return before;
            return before + " " + after;
        }

        public const uint BoundReforgeSandItemId = 3403u;
        public const uint BoundArtifactVoucherItemId = 3404u;

        public const string LegacyRecordOnlyHint = "本版暂无纪录，战法印已保留";

        /// <summary>将服务端 trial_journal_error 与通用错误码转换为中文提示（禁止手写数字）。</summary>
        public static string DescribeTip(uint tipId) => tipId switch
        {
            (uint)trial_journal_error.KTrialJournalUnavailable => "试炼志服务暂不可用，请稍后再试。",
            (uint)trial_journal_error.KTrialJournalFrozen => "当前角色状态暂不可操作试炼志，请稍后再试。",
            (uint)trial_journal_error.KTrialJournalInBattle => "战斗中暂不能领取里程碑或兑换法宝，请在结算后重试。",
            (uint)trial_journal_error.KTrialJournalMilestoneNotFound => "目标里程碑不存在或已下线。",
            (uint)trial_journal_error.KTrialJournalMilestoneNotReached => "尚未达标，继续点亮更多战法印后再来领取。",
            (uint)trial_journal_error.KTrialJournalMilestoneAlreadyClaimed => "该里程碑奖励已领取。",
            (uint)trial_journal_error.KTrialJournalBagFull => "人物背包空间不足，请先腾出空位后再试。",
            (uint)trial_journal_error.KTrialJournalVoucherNotEnough => "自用法宝拓印不足，请在试炼志里程碑中收集。",
            (uint)trial_journal_error.KTrialJournalArtifactInvalid => "目标法宝不可使用自用法宝拓印兑换。",
            (uint)common_error.KRateLimitExceeded => "操作太快了，请稍候再试。",
            (uint)common_error.KServiceUnavailable => "服务暂时不可用，请稍后再试。",
            (uint)common_error.KInvalidParameter => "请求参数无效，请刷新页面后再试。",
            _ => $"操作失败（{tipId}）。",
        };

        /// <summary>战法印槽位短标签（1=通，2=守，3=巧，4=速）。</summary>
        public static string SealSlotLabel(uint sealSlotKind) => sealSlotKind switch
        {
            1u => "通",
            2u => "守",
            3u => "巧",
            4u => "速",
            _ => "印",
        };

        /// <summary>查找指定卷册视图。</summary>
        public static global::TrialJournalChapterView FindChapter(global::TrialJournalSnapshot snapshot, uint chapterId)
        {
            if (snapshot?.Chapters == null) return null;
            for (int i = 0; i < snapshot.Chapters.Count; i++)
            {
                var chapter = snapshot.Chapters[i];
                if (chapter != null && chapter.ChapterId == chapterId) return chapter;
            }
            return null;
        }

        /// <summary>按 entry_id 查找关卡格视图。</summary>
        public static global::TrialJournalEntryView FindEntry(global::TrialJournalSnapshot snapshot, uint entryId)
        {
            if (snapshot?.Chapters == null || entryId == 0) return null;
            for (int c = 0; c < snapshot.Chapters.Count; c++)
            {
                var entries = snapshot.Chapters[c]?.Entries;
                if (entries == null) continue;
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry != null && entry.EntryId == entryId) return entry;
                }
            }
            return null;
        }

        /// <summary>按 milestone_id 查找里程碑视图。</summary>
        public static global::TrialJournalMilestoneView FindMilestone(global::TrialJournalSnapshot snapshot, uint milestoneId)
        {
            if (snapshot?.Milestones == null || milestoneId == 0) return null;
            for (int i = 0; i < snapshot.Milestones.Count; i++)
            {
                var m = snapshot.Milestones[i];
                if (m != null && m.MilestoneId == milestoneId) return m;
            }
            return null;
        }

        /// <summary>筛选某卷册对应的里程碑（chapterId == 0 返回全部）。</summary>
        public static List<global::TrialJournalMilestoneView> FilterMilestones(
            global::TrialJournalSnapshot snapshot,
            uint scopeChapterId = ScopeAllPermanent)
        {
            var result = new List<global::TrialJournalMilestoneView>();
            if (snapshot?.Milestones == null) return result;
            for (int i = 0; i < snapshot.Milestones.Count; i++)
            {
                var m = snapshot.Milestones[i];
                if (m == null) continue;
                if (scopeChapterId == ScopeAllPermanent || m.ScopeChapterId == scopeChapterId)
                    result.Add(m);
            }
            return result;
        }

        /// <summary>统计当前可立即领取的里程碑数量（用于页签红点与总览提醒）。</summary>
        public static int CountClaimableMilestones(
            global::TrialJournalSnapshot snapshot,
            uint scopeChapterId = ScopeAllPermanent)
        {
            if (snapshot?.Milestones == null) return 0;
            int count = 0;
            for (int i = 0; i < snapshot.Milestones.Count; i++)
            {
                var m = snapshot.Milestones[i];
                if (m == null || !m.Claimable) continue;
                if (scopeChapterId == ScopeAllPermanent || m.ScopeChapterId == scopeChapterId)
                    count++;
            }
            return count;
        }

        /// <summary>是否有可兑换的自用法宝拓印且存在可选项。</summary>
        public static bool CanRedeemAnyArtifact(global::TrialJournalSnapshot snapshot)
        {
            return snapshot != null
                && snapshot.Available
                && snapshot.CanOperate
                && snapshot.VoucherBalance > 0
                && snapshot.RedeemableArtifacts != null
                && snapshot.RedeemableArtifacts.Count > 0;
        }

        /// <summary>
        /// 汇总单人破阵的轻量阵容快照文案（宠 + 至多 2 法宝 + 赛季印）。
        /// 名称解析由调用方按表 ID 现查（禁止长期缓存表行引用）。
        /// </summary>
        public static string FormatSoloLoadoutSummary(
            global::TrialJournalLoadoutSnapshot loadout,
            Func<uint, string> petNameLookup = null,
            Func<uint, string> artifactNameLookup = null,
            Func<uint, string> seasonSealNameLookup = null)
        {
            if (loadout == null) return "";
            var sb = new StringBuilder(64);
            if (loadout.PetTableId != 0)
            {
                string petName = ResolveName(petNameLookup, loadout.PetTableId, $"灵宠#{loadout.PetTableId}");
                sb.Append("灵宠·").Append(petName);
            }
            if (loadout.ArtifactEffectIds != null && loadout.ArtifactEffectIds.Count > 0)
            {
                if (sb.Length > 0) sb.Append("  |  ");
                sb.Append("法宝·");
                for (int i = 0; i < loadout.ArtifactEffectIds.Count; i++)
                {
                    if (i > 0) sb.Append(" / ");
                    uint effectId = loadout.ArtifactEffectIds[i];
                    sb.Append(ResolveName(artifactNameLookup, effectId, $"法宝#{effectId}"));
                }
            }
            if (loadout.SeasonSealId != 0)
            {
                if (sb.Length > 0) sb.Append("  |  ");
                string sealName = ResolveName(seasonSealNameLookup, loadout.SeasonSealId, $"赛季印#{loadout.SeasonSealId}");
                sb.Append("赛季印·").Append(sealName);
            }
            return sb.ToString();
        }

        /// <summary>汇总好友合战最佳纪录文案（回合数 + 人数 + 同行队友名）。</summary>
        public static string FormatCoopSummary(global::TrialJournalCoopRecord coop)
        {
            if (coop == null || coop.BestRounds == 0) return "";
            var sb = new StringBuilder(64);
            sb.Append(coop.BestRounds).Append("回合");
            if (coop.PartySize > 0) sb.Append(" · ").Append(coop.PartySize).Append("人合战");
            if (coop.TeammateNames != null && coop.TeammateNames.Count > 0)
            {
                sb.Append("（同行：");
                for (int i = 0; i < coop.TeammateNames.Count; i++)
                {
                    if (i > 0) sb.Append("、");
                    sb.Append(coop.TeammateNames[i]);
                }
                sb.Append("）");
            }
            return sb.ToString();
        }

        /// <summary>收集当前已通过权威里程碑解锁的留灯院评语/跋文。</summary>
        public static List<TrialJournalCommentary> CollectUnlockedCommentaries(global::TrialJournalSnapshot snapshot)
        {
            var result = new List<TrialJournalCommentary>();
            if (snapshot?.Milestones == null) return result;
            for (int i = 0; i < snapshot.Milestones.Count; i++)
            {
                var m = snapshot.Milestones[i];
                if (m == null || !m.Claimed || string.IsNullOrWhiteSpace(m.CommentaryTitle)) continue;
                result.Add(new TrialJournalCommentary(m.MilestoneId, m.CommentaryKey, m.CommentaryTitle, m.CommentaryBody));
            }
            return result;
        }

        /// <summary>查找跨永久卷宗师形象里程碑（scope_chapter_id == 0 且带专属形象奖励，即 M901 五行宗师）。</summary>
        public static global::TrialJournalMilestoneView FindGrandmasterMilestone(global::TrialJournalSnapshot snapshot)
        {
            if (snapshot?.Milestones == null) return null;
            for (int i = 0; i < snapshot.Milestones.Count; i++)
            {
                var m = snapshot.Milestones[i];
                if (m != null && m.ScopeChapterId == ScopeAllPermanent && !string.IsNullOrEmpty(m.GrantAppearanceId))
                    return m;
            }
            return null;
        }

        /// <summary>
        /// 解析 TrialJournalEntry.jump_target 为结构化挑战入口（覆盖卷一/卷二/卷三全部 11 格）。
        /// </summary>
        public static bool TryParseJumpTarget(string jumpTarget, out TrialJumpTarget target)
        {
            target = default;
            if (string.IsNullOrWhiteSpace(jumpTarget)) return false;
            string raw = jumpTarget.Trim();
            switch (raw)
            {
                case "boss_trial:normal:rejuvenation":
                    target = new TrialJumpTarget(TrialJumpKind.BossTrial, 101u, 1u, "rejuvenation", "单人 / 组队均可挑战");
                    return true;
                case "boss_trial:hard:rejuvenation":
                    target = new TrialJumpTarget(TrialJumpKind.BossTrial, 102u, 2u, "rejuvenation", "单人 / 组队均可挑战（需先通任意普通试炼）");
                    return true;
                case "boss_trial:normal:bulwark":
                    target = new TrialJumpTarget(TrialJumpKind.BossTrial, 103u, 1u, "bulwark", "单人 / 组队均可挑战");
                    return true;
                case "boss_trial:hard:bulwark":
                    target = new TrialJumpTarget(TrialJumpKind.BossTrial, 104u, 2u, "bulwark", "单人 / 组队均可挑战（需先通任意普通试炼）");
                    return true;
                case "boss_trial:normal:barrier":
                    target = new TrialJumpTarget(TrialJumpKind.BossTrial, 105u, 1u, "barrier", "单人 / 组队均可挑战");
                    return true;
                case "boss_trial:hard:barrier":
                    target = new TrialJumpTarget(TrialJumpKind.BossTrial, 106u, 2u, "barrier", "单人 / 组队均可挑战（需先通任意普通试炼）");
                    return true;
                case "boss_trial:normal:venom":
                    target = new TrialJumpTarget(TrialJumpKind.BossTrial, 107u, 1u, "venom", "单人 / 组队均可挑战");
                    return true;
                case "boss_trial:hard:venom":
                    target = new TrialJumpTarget(TrialJumpKind.BossTrial, 108u, 2u, "venom", "单人 / 组队均可挑战（需先通任意普通试炼）");
                    return true;
                case "boss_trial:hard:dual_guard":
                    target = new TrialJumpTarget(TrialJumpKind.BossTrial, 112u, 2u, "dual_guard", "单人 / 组队均可挑战（仅困难 · 需先通任意普通试炼）");
                    return true;
                case "boss_trial:normal:rescue":
                    target = new TrialJumpTarget(TrialJumpKind.BossTrial, 109u, 1u, "rescue", "单人 / 组队均可挑战");
                    return true;
                case "boss_trial:wild:wind":
                    target = new TrialJumpTarget(TrialJumpKind.BossTrial, 113u, 1u, "wild_wind", "需前往八景岛挑战明雷首领（1–5人）", 2u);
                    return true;
                case "boss_trial:wild:bell":
                    target = new TrialJumpTarget(TrialJumpKind.BossTrial, 114u, 1u, "wild_bell", "需前往碧渊水宫挑战明雷首领（1–5人）", 5u);
                    return true;
                case "boss_trial:wild:tide":
                    target = new TrialJumpTarget(TrialJumpKind.BossTrial, 115u, 1u, "wild_tide", "需前往月汐湾挑战明雷首领（1–5人）", 3u);
                    return true;
            }
            const string polarityPrefix = "polarity_5v5:";
            if (raw.StartsWith(polarityPrefix, StringComparison.Ordinal)
                && uint.TryParse(raw.Substring(polarityPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out uint polarityDungeon)
                && polarityDungeon != 0)
            {
                target = new TrialJumpTarget(TrialJumpKind.Polarity5v5, polarityDungeon, 0u, "polarity_5v5", "单人报名物法易势 5V5 匹配");
                return true;
            }
            const string rankedPrefix = "ranked_pvp:";
            if (raw.StartsWith(rankedPrefix, StringComparison.Ordinal)
                && uint.TryParse(raw.Substring(rankedPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out uint rankedDungeon)
                && rankedDungeon != 0)
            {
                target = new TrialJumpTarget(TrialJumpKind.RankedPvp, rankedDungeon, 0u, "ranked_pvp", "前往论剑排位界面选择档位");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 按关卡格 ID 返回对应的首领与护卫怪物表 ID（供 UI 层调用 BattleArtCatalog.LoadMonsterPortrait 复用宝宝立绘）。
        /// PVP 关卡格无怪物立绘时返回 false。
        /// </summary>
        public static bool TryGetEntryMonsterIds(uint entryId, out uint bossMonsterId, out uint guardMonsterId)
        {
            switch (entryId)
            {
                case 101u:
                case 103u:
                case 301u:
                    bossMonsterId = 101u;
                    guardMonsterId = 102u;
                    return true;
                case 102u:
                case 104u:
                    bossMonsterId = 101u;
                    guardMonsterId = 103u;
                    return true;
                case 105u:
                    bossMonsterId = 101u;
                    guardMonsterId = 102u;
                    return true;
                case 201u:
                case 203u:
                    bossMonsterId = 104u;
                    guardMonsterId = 105u;
                    return true;
                case 202u:
                case 204u:
                    bossMonsterId = 106u;
                    guardMonsterId = 107u;
                    return true;
                case 401u:
                    bossMonsterId = 111u;
                    guardMonsterId = 112u;
                    return true;
                case 402u:
                    bossMonsterId = 113u;
                    guardMonsterId = 114u;
                    return true;
                case 403u:
                    bossMonsterId = 115u;
                    guardMonsterId = 116u;
                    return true;
                default:
                    bossMonsterId = 0u;
                    guardMonsterId = 0u;
                    return false;
            }
        }

        /// <summary>返回关卡格关联的全部核心护卫怪物表 ID（105 双卫同阵返回 [102, 103]，其余单卫返回 1 项，PVP 返回空）。</summary>
        public static IReadOnlyList<uint> GetEntryGuardMonsterIds(uint entryId)
        {
            if (entryId == 105u) return new[] { 102u, 103u };
            return TryGetEntryMonsterIds(entryId, out _, out uint guardId) && guardId != 0u
                ? new[] { guardId }
                : Array.Empty<uint>();
        }

        /// <summary>
        /// 汇总战斗结算面板的试炼志单行提示（首次点亮战法印与刷新单人/合战回合纪录）。
        /// 非试炼志对局或无新增亮点时返回空串。
        /// </summary>
        public static string FormatBattleResultSummary(
            global::PlayerBattleResult result,
            global::TrialJournalSnapshot snapshot = null)
        {
            if (result == null || result.TrialJournalEntryId == 0) return "";
            var sb = new StringBuilder(96);
            if (result.TrialNewlyLitSealIds != null && result.TrialNewlyLitSealIds.Count > 0)
            {
                sb.Append("新点亮战法印：");
                var entry = FindEntry(snapshot, result.TrialJournalEntryId);
                for (int i = 0; i < result.TrialNewlyLitSealIds.Count; i++)
                {
                    if (i > 0) sb.Append("、");
                    uint sealId = result.TrialNewlyLitSealIds[i];
                    string title = FindSealTitle(entry, sealId);
                    sb.Append(string.IsNullOrEmpty(title) ? $"战法印#{sealId}" : title);
                }
            }
            if (result.TrialNewSoloBest)
            {
                if (sb.Length > 0) sb.Append("  ·  ");
                sb.Append("刷新单人破阵纪录");
                if (result.TotalRounds > 0) sb.Append('（').Append(result.TotalRounds).Append("回合）");
            }
            if (result.TrialNewCoopBest)
            {
                if (sb.Length > 0) sb.Append("  ·  ");
                sb.Append("刷新好友合战纪录");
                if (result.TotalRounds > 0) sb.Append('（').Append(result.TotalRounds).Append("回合）");
            }
            if (result.TrialNewVowBest && result.TrialVowMask != 0u)
            {
                if (sb.Length > 0) sb.Append("  ·  ");
                sb.Append("刷新誓约纪录（").Append(FormatVowSummary(result.TrialVowMask)).Append('）');
            }
            else if (result.TrialVowMask != 0u)
            {
                if (sb.Length > 0) sb.Append("  ·  ");
                sb.Append("试炼誓约：").Append(FormatVowSummary(result.TrialVowMask));
            }
            return sb.ToString();
        }

        private static string FindSealTitle(global::TrialJournalEntryView entry, uint sealId)
        {
            if (entry?.Seals == null) return "";
            for (int i = 0; i < entry.Seals.Count; i++)
            {
                var seal = entry.Seals[i];
                if (seal != null && seal.SealId == sealId) return seal.Title ?? "";
            }
            return "";
        }

        private static string ResolveName(Func<uint, string> lookup, uint id, string fallback)
        {
            if (lookup == null) return fallback;
            string name = lookup(id);
            return string.IsNullOrWhiteSpace(name) ? fallback : name;
        }
    }
}
