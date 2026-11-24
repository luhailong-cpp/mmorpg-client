using System;
using System.Collections.Generic;

namespace MmorpgClient.Game.Appearance
{
    /// <summary>单个形象的候选人物档案与短篇故事（纯只读数据，不覆盖玩家昵称，可独立改稿）。</summary>
    public sealed class AppearanceBiography
    {
        public string RawAppearanceId { get; }
        public string ArchiveName { get; }
        public string Nickname { get; }
        public string OriginalTitle { get; }
        public string BackgroundSummary { get; }
        public string StoryTitle { get; }
        public string StoryBody { get; }

        public string DisplayArchiveName => string.IsNullOrEmpty(Nickname)
            ? ArchiveName
            : $"{ArchiveName}（{Nickname}）";

        public AppearanceBiography(
            string rawAppearanceId,
            string archiveName,
            string nickname,
            string originalTitle,
            string backgroundSummary,
            string storyTitle,
            string storyBody)
        {
            RawAppearanceId = rawAppearanceId ?? "";
            ArchiveName = archiveName ?? "";
            Nickname = nickname ?? "";
            OriginalTitle = originalTitle ?? "";
            BackgroundSummary = backgroundSummary ?? "";
            StoryTitle = storyTitle ?? "";
            StoryBody = storyBody ?? "";
        }
    }

    /// <summary>
    /// 人物背景与故事候选文案目录。
    /// 严格按条目原始稳定 <c>appearance_id</c> 精确匹配，绝不使用列表下标或历史视觉替代 ID。
    /// </summary>
    public static class AppearanceBiographyCatalog
    {
        private static readonly AppearanceBiography[] Entries =
        {
            new AppearanceBiography(
                rawAppearanceId: "00_reference_topright_boy",
                archiveName: "陆照禾",
                nickname: "阿禾",
                originalTitle: "金发带Q道童",
                backgroundSummary:
                    "母亲在归元城开面铺，祖母膝盖疼，把留灯院的钥匙交给阿禾。金葫芦里装的是井水。他记得谁吃面不放葱，却老忘了自己也能开口求人。",
                storyTitle: "第四只碗",
                storyBody:
                    "屋顶又漏了。阿禾一手举着碗，一手还端着给巷口送的面。\n\n" +
                    "停云扶住门框：“你还要走？”\n\n" +
                    "他迟疑了一下：“能替我看一会儿院子吗？”\n\n" +
                    "她把第四只碗往左挪了半寸：“成。回来带葱。”"),

            new AppearanceBiography(
                rawAppearanceId: "10_crimson_spear_girl",
                archiveName: "祝停云",
                nickname: "",
                originalTitle: "赤枪少女",
                backgroundSummary:
                    "月汐湾摆渡人家的女儿。父亲掌舵，母亲修船，她把旧船绳编进枪穗，想挣来修船的钱，也想堂堂正正赢一场。败局册里记给自己的问题，字总是特别小。",
                storyTitle: "纸也赢过一回",
                storyBody:
                    "她连输三场回院，枪穗往砚台里滴水。\n\n" +
                    "闻砚生写“惜败”，她说：“去掉惜字。”\n\n" +
                    "自己把“对手拖”划成“我又急了”。\n\n" +
                    "吃到半碗面，她拿筷子比了两步：“什么时候该等？”\n\n" +
                    "他抬眼：“昨天谁说我纸上谈兵？”\n\n" +
                    "“今天纸赢了。你讲。”"),

            new AppearanceBiography(
                rawAppearanceId: "09_bamboo_archer_girl",
                archiveName: "桑听雨",
                nickname: "",
                originalTitle: "竹弓少女",
                backgroundSummary:
                    "月汐湾竹器人家的女儿，想去八景岛画潮汐鸟谱。家里早备好行李，她却把船票让给了别人。竹筒里还留着旧票角，和一张没落过笔的纸。",
                storyTitle: "没有画的那一页",
                storyBody:
                    "阿禾调好了青色：“岛上的鸟脚也是青的吗？”\n\n" +
                    "听雨看看他摊开的画纸：“岸边这只青。岛上的……替我空一页吧。”\n\n" +
                    "饭后她独自走到渡口，问船家，一个人带行李该坐哪一边。"),
        };

        private static readonly Dictionary<string, AppearanceBiography> ByRawId = BuildIndex();

        public static IReadOnlyList<AppearanceBiography> All => Entries;

        /// <summary>
        /// 仅按原始稳定 <paramref name="rawAppearanceId"/> 精确查找，绝不经视觉别名映射。
        /// </summary>
        public static bool TryGetByRawAppearanceId(string rawAppearanceId, out AppearanceBiography biography)
        {
            if (string.IsNullOrEmpty(rawAppearanceId))
            {
                biography = null;
                return false;
            }
            return ByRawId.TryGetValue(rawAppearanceId, out biography);
        }

        private static Dictionary<string, AppearanceBiography> BuildIndex()
        {
            var map = new Dictionary<string, AppearanceBiography>(Entries.Length, StringComparer.Ordinal);
            for (int i = 0; i < Entries.Length; i++)
                map[Entries[i].RawAppearanceId] = Entries[i];
            return map;
        }
    }

    public enum AppearanceOverlayKind
    {
        None = 0,
        SwitchConfirm = 1,
        NoCardGuide = 2,
        StoryReader = 3,
        Biography = 4,
        PortraitPreview = 5,
        SeriesEpilogue = 6,
    }

    public enum AppearanceSwitchIntentOutcome
    {
        Ignored = 0,
        OpenedNoCardGuide = 1,
        OpenedSwitchConfirm = 2,
        ReadyForFreeSwitch = 3,
        LockedExclusive = 4,
    }

    public enum AppearanceStoryCloseReason
    {
        Back = 0,
        Skip = 1,
        Finished = 2,
        Escape = 3,
    }

    /// <summary>
    /// 易容面板的纯 C# 视图交互状态机（零 Unity 依赖）：
    /// 负责选中态、列表滚动位置保留、故事阅读层生命周期与防误扣卡确认清理。
    /// </summary>
    public sealed class AppearancePanelState
    {
        public string SelectedAppearanceId { get; private set; } = "";
        public uint SelectedSeriesId { get; private set; }
        public AppearanceOverlayKind ActiveOverlay { get; private set; } = AppearanceOverlayKind.None;
        public string PendingSwitchTargetId { get; private set; } = "";
        public string ReadingAppearanceId { get; private set; } = "";
        public uint ReadingSeriesEpilogueId { get; private set; }
        public float SavedListScrollNormalized { get; private set; } = 1f;
        public int StoryOpenCount { get; private set; }
        public float SavedStoryScrollNormalized { get; private set; } = 1f;
        private bool _storyReturnsToBiography;
        private AppearanceOverlayKind _previewReturnOverlay;

        // 保留显式武装态字段，确保任何历史或外部调用点在进入阅读层后都能彻底清零。
        public string ArmedSwitchAppearanceId { get; private set; } = "";
        public float ArmedSwitchAtSeconds { get; private set; } = float.NegativeInfinity;

        public bool HasOverlay => ActiveOverlay != AppearanceOverlayKind.None;
        public bool IsStoryOpen => ActiveOverlay == AppearanceOverlayKind.StoryReader;
        public bool IsSwitchConfirmOpen => ActiveOverlay == AppearanceOverlayKind.SwitchConfirm;
        public bool IsNoCardGuideOpen => ActiveOverlay == AppearanceOverlayKind.NoCardGuide;
        public bool IsBiographyOpen => ActiveOverlay == AppearanceOverlayKind.Biography;
        public bool IsPortraitPreviewOpen => ActiveOverlay == AppearanceOverlayKind.PortraitPreview;
        public bool IsSeriesEpilogueOpen => ActiveOverlay == AppearanceOverlayKind.SeriesEpilogue;
        public bool IsReadOnlyOverlay => IsStoryOpen || IsBiographyOpen || IsPortraitPreviewOpen || IsSeriesEpilogueOpen;
        public bool HasPendingSwitchConfirmation =>
            ActiveOverlay == AppearanceOverlayKind.SwitchConfirm || !string.IsNullOrEmpty(ArmedSwitchAppearanceId);

        public bool TryGetSelectedBiography(out AppearanceBiography biography)
            => AppearanceBiographyCatalog.TryGetByRawAppearanceId(SelectedAppearanceId, out biography);

        public bool TryGetReadingBiography(out AppearanceBiography biography)
        {
            if (!IsStoryOpen)
            {
                biography = null;
                return false;
            }
            return AppearanceBiographyCatalog.TryGetByRawAppearanceId(ReadingAppearanceId, out biography);
        }

        public void SyncAvailableAppearances(IReadOnlyList<string> validAppearanceIds, string fallbackActiveId,
            string permanentAppearanceId)
        {
            if (validAppearanceIds == null || validAppearanceIds.Count == 0)
            {
                SelectedAppearanceId = "";
                CloseAllOverlaysAndClearSwitchConfirmation();
                return;
            }

            if (!ContainsId(validAppearanceIds, SelectedAppearanceId))
            {
                SelectedAppearanceId = ContainsId(validAppearanceIds, fallbackActiveId)
                    ? fallbackActiveId
                    : validAppearanceIds[0];
                ClearPendingSwitchConfirmation();
                SavedStoryScrollNormalized = 1f;
            }

            // 若正在阅读的原始条目失效（被移除或无档案），立刻收起阅读层，不残留旧正文。
            if (IsReadOnlyOverlay && !IsSeriesEpilogueOpen && (!ContainsId(validAppearanceIds, ReadingAppearanceId) ||
                (!IsPortraitPreviewOpen && !AppearanceBiographyCatalog.TryGetByRawAppearanceId(ReadingAppearanceId, out _))))
            {
                ReadingAppearanceId = "";
                ActiveOverlay = AppearanceOverlayKind.None;
            }

            if (IsSwitchConfirmOpen && (!ContainsId(validAppearanceIds, PendingSwitchTargetId) ||
                string.Equals(PendingSwitchTargetId, permanentAppearanceId, StringComparison.Ordinal)))
            {
                PendingSwitchTargetId = "";
                ActiveOverlay = AppearanceOverlayKind.None;
            }
        }

        public void SelectAppearance(string rawAppearanceId, float currentListScrollNormalized)
        {
            if (string.IsNullOrEmpty(rawAppearanceId)) return;
            SavedListScrollNormalized = Clamp01(currentListScrollNormalized);
            bool changed = !string.Equals(SelectedAppearanceId, rawAppearanceId, StringComparison.Ordinal);
            SelectedAppearanceId = rawAppearanceId;
            ClearPendingSwitchConfirmation();
            if (changed)
            {
                SavedStoryScrollNormalized = 1f;
                _storyReturnsToBiography = false;
                _previewReturnOverlay = AppearanceOverlayKind.None;
                ReadingAppearanceId = "";
                if (IsReadOnlyOverlay) ActiveOverlay = AppearanceOverlayKind.None;
            }
        }

        public void ArmLegacySwitchConfirmation(string targetAppearanceId, float nowSeconds)
        {
            if (string.IsNullOrEmpty(targetAppearanceId)) return;
            ArmedSwitchAppearanceId = targetAppearanceId;
            ArmedSwitchAtSeconds = nowSeconds;
        }

        public AppearanceSwitchIntentOutcome RequestSwitch(
            string targetAppearanceId,
            string permanentAppearanceId,
            ulong ownedCardCount,
            uint requiredCardCount,
            bool targetOwned = false,
            bool targetCardUnlockable = true)
        {
            if (IsReadOnlyOverlay) return AppearanceSwitchIntentOutcome.Ignored;
            if (string.IsNullOrEmpty(targetAppearanceId) ||
                string.Equals(targetAppearanceId, permanentAppearanceId, StringComparison.Ordinal))
            {
                ClearPendingSwitchConfirmation();
                return AppearanceSwitchIntentOutcome.Ignored;
            }

            ArmedSwitchAppearanceId = "";
            ArmedSwitchAtSeconds = float.NegativeInfinity;

            if (targetOwned)
            {
                ClearPendingSwitchConfirmation();
                return AppearanceSwitchIntentOutcome.ReadyForFreeSwitch;
            }

            if (!targetCardUnlockable)
            {
                ClearPendingSwitchConfirmation();
                return AppearanceSwitchIntentOutcome.LockedExclusive;
            }

            uint needed = requiredCardCount > 0 ? requiredCardCount : 1u;
            if (ownedCardCount < needed)
            {
                PendingSwitchTargetId = targetAppearanceId;
                ActiveOverlay = AppearanceOverlayKind.NoCardGuide;
                return AppearanceSwitchIntentOutcome.OpenedNoCardGuide;
            }

            PendingSwitchTargetId = targetAppearanceId;
            ActiveOverlay = AppearanceOverlayKind.SwitchConfirm;
            return AppearanceSwitchIntentOutcome.OpenedSwitchConfirm;
        }

        public bool TryConsumePendingSwitchConfirm(out string targetAppearanceId)
        {
            if (ActiveOverlay != AppearanceOverlayKind.SwitchConfirm || string.IsNullOrEmpty(PendingSwitchTargetId))
            {
                targetAppearanceId = "";
                return false;
            }

            targetAppearanceId = PendingSwitchTargetId;
            ClearPendingSwitchConfirmation();
            return true;
        }

        public bool OpenStory(float currentListScrollNormalized)
        {
            bool fromBiography = IsBiographyOpen;
            // 进入阅读层时，无论是否成功打开，一律清除尚未提交的旧扣卡确认。
            ClearPendingSwitchConfirmation();
            if (!AppearanceBiographyCatalog.TryGetByRawAppearanceId(SelectedAppearanceId, out _))
                return false;

            SavedListScrollNormalized = Clamp01(currentListScrollNormalized);
            ReadingAppearanceId = SelectedAppearanceId;
            ActiveOverlay = AppearanceOverlayKind.StoryReader;
            _storyReturnsToBiography = fromBiography;
            StoryOpenCount++;
            return true;
        }

        public bool CloseStory(AppearanceStoryCloseReason reason)
        {
            if (!IsStoryOpen) return false;
            bool returnToBiography = _storyReturnsToBiography &&
                (reason == AppearanceStoryCloseReason.Back || reason == AppearanceStoryCloseReason.Escape);
            ReadingAppearanceId = returnToBiography ? SelectedAppearanceId : "";
            ActiveOverlay = returnToBiography ? AppearanceOverlayKind.Biography : AppearanceOverlayKind.None;
            // 返回后清空所有旧确认态，保证首次点击“切换”必须重新进入确认。
            ClearPendingSwitchConfirmation();
            return true;
        }

        public bool DismissActiveOverlay()
        {
            if (IsPortraitPreviewOpen) return ClosePortraitPreview();
            if (IsSeriesEpilogueOpen) return CloseSeriesEpilogue();
            if (IsBiographyOpen) return CloseBiography();
            if (IsStoryOpen) return CloseStory(AppearanceStoryCloseReason.Escape);
            if (IsSwitchConfirmOpen || IsNoCardGuideOpen)
            {
                ClearPendingSwitchConfirmation();
                return true;
            }
            return false;
        }

        public void SelectSeries(uint seriesId, float currentListScrollNormalized)
        {
            SavedListScrollNormalized = Clamp01(currentListScrollNormalized);
            if (SelectedSeriesId != seriesId)
            {
                SelectedSeriesId = seriesId;
                if (IsReadOnlyOverlay) CloseAllOverlaysAndClearSwitchConfirmation();
            }
            ClearPendingSwitchConfirmation();
        }

        public void SyncSeriesSelection(IReadOnlyList<uint> validSeriesIds, Func<uint, bool> isCompleted = null)
        {
            if (SelectedSeriesId != 0 && !ContainsSeriesId(validSeriesIds, SelectedSeriesId))
            {
                SelectedSeriesId = 0;
            }
            if (IsSeriesEpilogueOpen && (!ContainsSeriesId(validSeriesIds, ReadingSeriesEpilogueId) ||
                (isCompleted != null && !isCompleted(ReadingSeriesEpilogueId))))
            {
                ReadingSeriesEpilogueId = 0;
                ActiveOverlay = AppearanceOverlayKind.None;
            }
        }

        public bool OpenSeriesEpilogue(uint seriesId, bool seriesCompleted, string epilogueBody, float currentListScrollNormalized)
        {
            // 进入合篇阅读层时一律清除尚未提交的旧扣卡确认。
            ClearPendingSwitchConfirmation();
            if (seriesId == 0 || !seriesCompleted || string.IsNullOrWhiteSpace(epilogueBody))
                return false;
            SavedListScrollNormalized = Clamp01(currentListScrollNormalized);
            ReadingAppearanceId = "";
            ReadingSeriesEpilogueId = seriesId;
            ActiveOverlay = AppearanceOverlayKind.SeriesEpilogue;
            return true;
        }

        public bool CloseSeriesEpilogue()
        {
            if (!IsSeriesEpilogueOpen) return false;
            ReadingSeriesEpilogueId = 0;
            ActiveOverlay = AppearanceOverlayKind.None;
            ClearPendingSwitchConfirmation();
            return true;
        }

        public void ClearPendingSwitchConfirmation()
        {
            PendingSwitchTargetId = "";
            ArmedSwitchAppearanceId = "";
            ArmedSwitchAtSeconds = float.NegativeInfinity;
            if (ActiveOverlay == AppearanceOverlayKind.SwitchConfirm ||
                ActiveOverlay == AppearanceOverlayKind.NoCardGuide)
            {
                ActiveOverlay = AppearanceOverlayKind.None;
            }
        }

        public void ResetForCloseOrDisconnect()
        {
            CloseAllOverlaysAndClearSwitchConfirmation();
            SavedStoryScrollNormalized = 1f;
        }

        public void ResetForRoleChange()
        {
            ResetForCloseOrDisconnect();
            SelectedAppearanceId = "";
            SelectedSeriesId = 0;
            SavedListScrollNormalized = 1f;
        }

        public bool OpenBiography(float currentListScrollNormalized)
        {
            ClearPendingSwitchConfirmation();
            if (!TryGetSelectedBiography(out _)) return false;
            SavedListScrollNormalized = Clamp01(currentListScrollNormalized);
            ReadingAppearanceId = SelectedAppearanceId;
            ActiveOverlay = AppearanceOverlayKind.Biography;
            return true;
        }

        public bool CloseBiography()
        {
            if (!IsBiographyOpen) return false;
            ReadingAppearanceId = "";
            ActiveOverlay = AppearanceOverlayKind.None;
            ClearPendingSwitchConfirmation();
            return true;
        }

        public bool OpenPortraitPreview(float currentListScrollNormalized)
        {
            var returnOverlay = IsReadOnlyOverlay ? ActiveOverlay : AppearanceOverlayKind.None;
            ClearPendingSwitchConfirmation();
            if (string.IsNullOrEmpty(SelectedAppearanceId)) return false;
            if (IsPortraitPreviewOpen) return true;
            SavedListScrollNormalized = Clamp01(currentListScrollNormalized);
            _previewReturnOverlay = returnOverlay;
            ReadingAppearanceId = SelectedAppearanceId;
            ActiveOverlay = AppearanceOverlayKind.PortraitPreview;
            return true;
        }

        public bool ClosePortraitPreview()
        {
            if (!IsPortraitPreviewOpen) return false;
            ActiveOverlay = _previewReturnOverlay;
            if (ActiveOverlay == AppearanceOverlayKind.None) ReadingAppearanceId = "";
            _previewReturnOverlay = AppearanceOverlayKind.None;
            ClearPendingSwitchConfirmation();
            return true;
        }

        public void SaveStoryPosition(float normalizedPosition)
            => SavedStoryScrollNormalized = Clamp01(normalizedPosition);

        private void CloseAllOverlaysAndClearSwitchConfirmation()
        {
            ReadingAppearanceId = "";
            ReadingSeriesEpilogueId = 0;
            PendingSwitchTargetId = "";
            ArmedSwitchAppearanceId = "";
            ArmedSwitchAtSeconds = float.NegativeInfinity;
            ActiveOverlay = AppearanceOverlayKind.None;
            _storyReturnsToBiography = false;
            _previewReturnOverlay = AppearanceOverlayKind.None;
        }

        private static bool ContainsSeriesId(IReadOnlyList<uint> ids, uint target)
        {
            if (ids == null || target == 0) return false;
            for (int i = 0; i < ids.Count; i++)
                if (ids[i] == target) return true;
            return false;
        }

        private static bool ContainsId(IReadOnlyList<string> ids, string target)
        {
            if (ids == null || string.IsNullOrEmpty(target)) return false;
            for (int i = 0; i < ids.Count; i++)
                if (string.Equals(ids[i], target, StringComparison.Ordinal)) return true;
            return false;
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 1f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
