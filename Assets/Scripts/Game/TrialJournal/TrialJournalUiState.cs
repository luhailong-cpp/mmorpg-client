using System;
using System.Collections.Generic;

namespace MmorpgClient.Game.TrialJournal
{
    /// <summary>试炼志左侧目录页签。</summary>
    public enum TrialJournalNavTab
    {
        Overview = 0,
        Breakthrough = 1,
        ThreeRealms = 2,
        Seasonal = 3,
        Milestones = 4,
    }

    /// <summary>试炼志九态验收视觉相位（纯 C# 推导，脱离 UnityEngine，可被 EditMode 单测直接覆盖）。</summary>
    public enum TrialJournalVisualPhase
    {
        FirstLoad = 0,
        Refreshing = 1,
        Empty = 2,
        InProgress = 3,
        GrandmasterReached = 4,
        SeasonalIsolated = 5,
        RuleRevision = 6,
        BagFullError = 7,
        Disconnected = 8,
    }

    /// <summary>顶栏提示条等级。</summary>
    public enum TrialJournalBannerTone
    {
        None = 0,
        Info = 1,
        Warn = 2,
        Error = 3,
    }

    /// <summary>试炼志当前帧只读视觉视图模型。</summary>
    public sealed class TrialJournalUiView
    {
        public TrialJournalVisualPhase Phase { get; set; }
        public string SeasonLabel { get; set; } = "";
        public string RuleLabel { get; set; } = "";
        public string SyncPillText { get; set; } = "";
        public TrialJournalBannerTone SyncPillTone { get; set; }
        public string AlertText { get; set; } = "";
        public TrialJournalBannerTone AlertTone { get; set; }
        public string FooterText { get; set; } = "";

        public uint PermanentLitSeals { get; set; }
        public uint PermanentTotalSeals { get; set; }
        public uint GrandmasterRequiredSeals { get; set; } = 18u;
        public uint ActiveChapterLitSeals { get; set; }
        public uint ActiveChapterTotalSeals { get; set; }
        public string ActiveChapterLabel { get; set; } = "";
        public int ClaimableMilestoneCount { get; set; }
        public ulong VoucherBalance { get; set; }
        public ulong BoundSandBalance { get; set; }

        public bool OnlineWriteEnabled { get; set; }
        public bool RedeemButtonEnabled { get; set; }
        public string RedeemButtonText { get; set; } = "兑换所选法宝";
        public string VoucherHintText { get; set; } = "";

        public bool GrandmasterReached { get; set; }
        public bool GrandmasterClaimed { get; set; }
        public bool GrandmasterClaimable { get; set; }
        public string GrandmasterTitle { get; set; } = "";
        public string GrandmasterDesc { get; set; } = "";
        public string GrandmasterButtonText { get; set; } = "";
        public bool GrandmasterButtonEnabled { get; set; }
    }

    /// <summary>
    /// 试炼志纯 C# 视图推导与筛选工具（零 UnityEngine 依赖，WebGL2/IL2CPP 安全）。
    /// 一切数据严格以服务端 <see cref="global::TrialJournalSnapshot"/> 为唯一真相，不推算战法印或伪造奖励。
    /// </summary>
    public static class TrialJournalUiState
    {
        public const uint DefaultGrandmasterRequiredSeals = 18u;

        public static TrialJournalUiView Evaluate(
            global::TrialJournalSnapshot snapshot,
            bool snapshotPending,
            bool actionPending,
            bool ready,
            bool requiresReconnect,
            string clientStatus,
            bool statusIsError,
            TrialJournalNavTab activeTab,
            uint selectedEntryId)
        {
            var view = new TrialJournalUiView();
            bool hasSnapshot = snapshot != null && snapshot.Available;
            bool disconnected = requiresReconnect || !ready;

            if (!hasSnapshot)
            {
                view.Phase = disconnected ? TrialJournalVisualPhase.Disconnected : TrialJournalVisualPhase.FirstLoad;
                view.SeasonLabel = "五行试炼志";
                view.RuleLabel = "规则同步中";
                view.SyncPillText = disconnected ? "连接中断" : "首次同步中";
                view.SyncPillTone = disconnected ? TrialJournalBannerTone.Error : TrialJournalBannerTone.Warn;
                view.AlertText = disconnected
                    ? (string.IsNullOrEmpty(clientStatus) ? TrialJournalClient.RecoveryMessage : clientStatus)
                    : "";
                view.AlertTone = disconnected ? TrialJournalBannerTone.Error : TrialJournalBannerTone.None;
                view.FooterText = disconnected
                    ? "连接中断，请恢复连接后重试。"
                    : (string.IsNullOrEmpty(clientStatus) ? "正在读取试炼记录，请稍候。" : clientStatus);
                view.VoucherHintText = "等待快照同步";
                view.RedeemButtonText = "兑换所选法宝";
                view.RedeemButtonEnabled = false;
                view.OnlineWriteEnabled = false;
                view.GrandmasterTitle = "九天星阵，待君点亮";
                view.GrandmasterDesc = "集齐十八枚永久道印，免费永久解锁「星阵宗师」专属形象。";
                view.GrandmasterButtonText = "查看宗师里程碑";
                view.GrandmasterButtonEnabled = false;
                return view;
            }

            var grandmaster = TrialJournalModels.FindGrandmasterMilestone(snapshot);
            uint gmReq = grandmaster != null && grandmaster.RequiredSeals > 0
                ? grandmaster.RequiredSeals
                : DefaultGrandmasterRequiredSeals;

            var seasonChapter = TrialJournalModels.FindChapter(snapshot, TrialJournalModels.ChapterSeasonal);
            var activeChapter = ResolveFocusChapter(snapshot, activeTab);
            var selectedEntry = TrialJournalModels.FindEntry(snapshot, selectedEntryId)
                ?? FirstVisibleEntry(snapshot, activeTab);

            bool anyLitOrRecorded = snapshot.TotalPermanentLitSeals > 0
                || (seasonChapter != null && seasonChapter.LitSealCount > 0)
                || HasAnyRecord(snapshot);
            bool isBagFull = statusIsError && !string.IsNullOrEmpty(clientStatus) && clientStatus.Contains("背包");
            bool isRuleRevision = selectedEntry != null && selectedEntry.HasLegacyRecordOnly;
            bool isSeasonTab = activeTab == TrialJournalNavTab.Seasonal && seasonChapter != null;

            view.PermanentLitSeals = snapshot.TotalPermanentLitSeals;
            view.PermanentTotalSeals = snapshot.TotalPermanentSeals;
            view.GrandmasterRequiredSeals = gmReq;
            view.ActiveChapterLitSeals = activeChapter?.LitSealCount ?? (seasonChapter?.LitSealCount ?? 0u);
            view.ActiveChapterTotalSeals = activeChapter?.TotalSealCount ?? (seasonChapter?.TotalSealCount ?? 0u);
            view.ActiveChapterLabel = activeChapter?.ChapterName ?? "本季试炼";
            view.ClaimableMilestoneCount = TrialJournalModels.CountClaimableMilestones(snapshot);
            view.VoucherBalance = snapshot.VoucherBalance;
            view.BoundSandBalance = snapshot.BoundSandBalance;

            view.SeasonLabel = seasonChapter != null && !string.IsNullOrEmpty(seasonChapter.ChapterName)
                ? $"破阵之季 · {seasonChapter.ChapterName}"
                : "常驻试炼 · 道印篇";
            view.RuleLabel = selectedEntry != null && !string.IsNullOrEmpty(selectedEntry.RuleVersionName)
                ? $"{selectedEntry.RuleVersionName} v{selectedEntry.RuleVersion}"
                : "首测规则 v1";

            view.GrandmasterReached = grandmaster?.Reached ?? (snapshot.TotalPermanentLitSeals >= gmReq);
            view.GrandmasterClaimed = grandmaster?.Claimed ?? false;
            view.GrandmasterClaimable = grandmaster != null && grandmaster.Claimable;
            view.OnlineWriteEnabled = !disconnected && !actionPending && snapshot.CanOperate;

            // 九态优先级：断线 > 背包满错 > 刷新保留旧图 > 规则换代 > 季游隔离 > 18印宗师 > 真空档 > 进行中
            if (disconnected)
            {
                view.Phase = TrialJournalVisualPhase.Disconnected;
                view.SyncPillText = "连接中断";
                view.SyncPillTone = TrialJournalBannerTone.Error;
                view.AlertText = string.IsNullOrEmpty(clientStatus)
                    ? "连接中断，已保留上次同步内容；连接恢复后可重新请求。"
                    : clientStatus;
                view.AlertTone = TrialJournalBannerTone.Error;
                view.FooterText = "离线期间不推定领取或兑换成功。";
            }
            else if (isBagFull)
            {
                view.Phase = TrialJournalVisualPhase.BagFullError;
                view.SyncPillText = "待处理";
                view.SyncPillTone = TrialJournalBannerTone.Error;
                view.AlertText = clientStatus;
                view.AlertTone = TrialJournalBannerTone.Error;
                view.FooterText = "兑换以服务端成功回执为准，凭券仍保留。";
            }
            else if (snapshotPending)
            {
                view.Phase = TrialJournalVisualPhase.Refreshing;
                view.SyncPillText = "刷新中";
                view.SyncPillTone = TrialJournalBannerTone.Warn;
                view.AlertText = "正在刷新记录；上次同步的内容仍可查看，刷新完成后再更新。";
                view.AlertTone = TrialJournalBannerTone.Info;
                view.FooterText = "刷新期间保留上一份有效快照。";
            }
            else if (isRuleRevision)
            {
                view.Phase = TrialJournalVisualPhase.RuleRevision;
                view.SyncPillText = "已同步";
                view.SyncPillTone = TrialJournalBannerTone.Info;
                view.AlertText = "规则已更新：旧版记录留档，新版试炼按新规则计数，已点亮战法印永久保留。";
                view.AlertTone = TrialJournalBannerTone.Info;
                view.FooterText = TrialJournalModels.LegacyRecordOnlyHint;
            }
            else if (isSeasonTab)
            {
                view.Phase = TrialJournalVisualPhase.SeasonalIsolated;
                view.SyncPillText = "已同步";
                view.SyncPillTone = TrialJournalBannerTone.Info;
                view.AlertText = "卷三季游按赛季独立归档，错过整季不影响永久卷 24 印与「星阵宗师」解锁。";
                view.AlertTone = TrialJournalBannerTone.Info;
                view.FooterText = "每季独立展示赛季道印与纪念奖励。";
            }
            else if (view.GrandmasterReached)
            {
                view.Phase = TrialJournalVisualPhase.GrandmasterReached;
                view.SyncPillText = "已同步";
                view.SyncPillTone = TrialJournalBannerTone.Info;
                view.AlertText = statusIsError ? clientStatus : "";
                view.AlertTone = statusIsError ? TrialJournalBannerTone.Error : TrialJournalBannerTone.None;
                view.FooterText = view.GrandmasterClaimed
                    ? "已永久解锁「星阵宗师」零战力专属形象，可在易容衣柜随时切换。"
                    : "十八印俱明；宗师里程碑 M901 已达到领取条件。";
            }
            else if (!anyLitOrRecorded)
            {
                view.Phase = TrialJournalVisualPhase.Empty;
                view.SyncPillText = "已同步";
                view.SyncPillTone = TrialJournalBannerTone.Info;
                view.AlertText = statusIsError ? clientStatus : "";
                view.AlertTone = statusIsError ? TrialJournalBannerTone.Error : TrialJournalBannerTone.None;
                view.FooterText = "尚无试炼记录。完成首次挑战后，战法印与复盘快照会出现在这里。";
            }
            else
            {
                view.Phase = TrialJournalVisualPhase.InProgress;
                view.SyncPillText = actionPending ? "处理中" : "已同步";
                view.SyncPillTone = actionPending ? TrialJournalBannerTone.Warn : TrialJournalBannerTone.Info;
                view.AlertText = !string.IsNullOrEmpty(clientStatus) && statusIsError ? clientStatus : "";
                view.AlertTone = !string.IsNullOrEmpty(clientStatus) && statusIsError
                    ? TrialJournalBannerTone.Error
                    : TrialJournalBannerTone.None;
                view.FooterText = !string.IsNullOrEmpty(clientStatus)
                    ? clientStatus
                    : "提示：挑战成功后，战法印、复盘配装与里程碑会在结算后自动更新。";
            }

            // 拓印券状态
            bool canRedeem = view.OnlineWriteEnabled && TrialJournalModels.CanRedeemAnyArtifact(snapshot);
            view.RedeemButtonEnabled = canRedeem;
            if (disconnected)
            {
                view.VoucherHintText = "离线不可兑换 · 保留上次快照";
                view.RedeemButtonText = "连接恢复后兑换";
            }
            else if (isBagFull)
            {
                view.VoucherHintText = $"兑换失败 · 仍持有 {snapshot.VoucherBalance} 张凭券";
                view.RedeemButtonText = "整理背包后重试";
            }
            else if (snapshot.VoucherBalance == 0)
            {
                view.VoucherHintText = "尚未持有拓印 · 在卷一/卷二里程碑获取";
                view.RedeemButtonText = "暂无可用凭券";
            }
            else
            {
                view.VoucherHintText = $"可兑换 {snapshot.VoucherBalance} 次 · 八件任选其一（仅自用）";
                view.RedeemButtonText = "兑换所选法宝";
            }

            // 右侧 M901「星阵宗师」卡状态
            if (view.GrandmasterClaimed)
            {
                view.GrandmasterTitle = "已入藏 · 星阵宗师";
                view.GrandmasterDesc = "零战力专属形象已永久入藏易容衣柜，可随时免费切换或重读宗师跋文。";
                view.GrandmasterButtonText = "阅读宗师跋文";
                view.GrandmasterButtonEnabled = true;
            }
            else if (view.GrandmasterClaimable)
            {
                view.GrandmasterTitle = "十八印俱明 · 星阵宗师";
                view.GrandmasterDesc = "十八枚永久道印已经集齐；点击免费领取「星阵宗师」专属形象。";
                view.GrandmasterButtonText = "领取宗师里程碑";
                view.GrandmasterButtonEnabled = view.OnlineWriteEnabled;
            }
            else
            {
                uint missing = gmReq > snapshot.TotalPermanentLitSeals ? gmReq - snapshot.TotalPermanentLitSeals : 0u;
                view.GrandmasterTitle = "九天星阵，待君点亮";
                view.GrandmasterDesc = missing == 0
                    ? "已达 18 枚永久道印，请先完成前置同步后领取。"
                    : $"再点亮 {missing} 枚永久道印（总永久 {snapshot.TotalPermanentLitSeals}/{snapshot.TotalPermanentSeals}，留 6 印容错），免费解锁专属形象。";
                view.GrandmasterButtonText = "查看全部里程碑";
                view.GrandmasterButtonEnabled = !disconnected;
            }

            return view;
        }

        /// <summary>按当前页签筛选要展示的关卡格（不修改原快照，保留服务端排序）。</summary>
        public static List<global::TrialJournalEntryView> FilterEntries(
            global::TrialJournalSnapshot snapshot,
            TrialJournalNavTab tab)
        {
            var result = new List<global::TrialJournalEntryView>();
            if (snapshot?.Chapters == null || tab == TrialJournalNavTab.Milestones) return result;
            uint wantedChapter = tab switch
            {
                TrialJournalNavTab.Breakthrough => TrialJournalModels.ChapterBreakthrough,
                TrialJournalNavTab.ThreeRealms => TrialJournalModels.ChapterThreeRealms,
                TrialJournalNavTab.Seasonal => TrialJournalModels.ChapterSeasonal,
                _ => 0u,
            };
            for (int c = 0; c < snapshot.Chapters.Count; c++)
            {
                var chapter = snapshot.Chapters[c];
                if (chapter == null) continue;
                if (wantedChapter != 0u && chapter.ChapterId != wantedChapter) continue;
                if (chapter.Entries == null) continue;
                for (int i = 0; i < chapter.Entries.Count; i++)
                {
                    var entry = chapter.Entries[i];
                    if (entry != null) result.Add(entry);
                }
            }
            return result;
        }

        /// <summary>将关卡格 ID 反查其所属页签，供结算页「看试炼志」直达定位。</summary>
        public static TrialJournalNavTab ResolveTabForEntry(global::TrialJournalSnapshot snapshot, uint entryId)
        {
            if (snapshot?.Chapters == null || entryId == 0u) return TrialJournalNavTab.Overview;
            for (int c = 0; c < snapshot.Chapters.Count; c++)
            {
                var chapter = snapshot.Chapters[c];
                if (chapter?.Entries == null) continue;
                for (int i = 0; i < chapter.Entries.Count; i++)
                {
                    if (chapter.Entries[i]?.EntryId != entryId) continue;
                    return chapter.ChapterId switch
                    {
                        TrialJournalModels.ChapterBreakthrough => TrialJournalNavTab.Breakthrough,
                        TrialJournalModels.ChapterThreeRealms => TrialJournalNavTab.ThreeRealms,
                        TrialJournalModels.ChapterSeasonal => TrialJournalNavTab.Seasonal,
                        _ => TrialJournalNavTab.Overview,
                    };
                }
            }
            return TrialJournalNavTab.Overview;
        }

        private static global::TrialJournalChapterView ResolveFocusChapter(
            global::TrialJournalSnapshot snapshot,
            TrialJournalNavTab tab)
        {
            uint chapterId = tab switch
            {
                TrialJournalNavTab.Breakthrough => TrialJournalModels.ChapterBreakthrough,
                TrialJournalNavTab.ThreeRealms => TrialJournalModels.ChapterThreeRealms,
                TrialJournalNavTab.Seasonal => TrialJournalModels.ChapterSeasonal,
                _ => TrialJournalModels.ChapterSeasonal,
            };
            return TrialJournalModels.FindChapter(snapshot, chapterId)
                ?? (snapshot.Chapters != null && snapshot.Chapters.Count > 0 ? snapshot.Chapters[0] : null);
        }

        private static global::TrialJournalEntryView FirstVisibleEntry(
            global::TrialJournalSnapshot snapshot,
            TrialJournalNavTab tab)
        {
            var entries = FilterEntries(snapshot, tab == TrialJournalNavTab.Milestones ? TrialJournalNavTab.Overview : tab);
            return entries.Count > 0 ? entries[0] : null;
        }

        private static bool HasAnyRecord(global::TrialJournalSnapshot snapshot)
        {
            if (snapshot?.Chapters == null) return false;
            for (int c = 0; c < snapshot.Chapters.Count; c++)
            {
                var entries = snapshot.Chapters[c]?.Entries;
                if (entries == null) continue;
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (e != null && (e.SoloRecorded || e.CoopRecorded || e.HasLegacyRecordOnly))
                        return true;
                }
            }
            return false;
        }
    }
}
