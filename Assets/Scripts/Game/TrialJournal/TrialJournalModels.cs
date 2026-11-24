using System;
using System.Collections.Generic;
using System.Text;

namespace MmorpgClient.Game.TrialJournal
{
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

        private static string ResolveName(Func<uint, string> lookup, uint id, string fallback)
        {
            if (lookup == null) return fallback;
            string name = lookup(id);
            return string.IsNullOrWhiteSpace(name) ? fallback : name;
        }
    }
}
