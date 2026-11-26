using System;
using System.Collections.Generic;

namespace MmorpgClient.Game.Appearance
{
    /// <summary>已集齐解锁的人物收藏系列合篇快照（纯外观与叙事展示，零数值属性加成）。</summary>
    public sealed class AppearanceSeriesEpilogue
    {
        public uint SeriesId { get; }
        public string SeriesKey { get; }
        public string SeriesName { get; }
        public string BadgeName { get; }
        public string EpilogueTitle { get; }
        public string EpilogueBody { get; }

        public AppearanceSeriesEpilogue(
            uint seriesId,
            string seriesKey,
            string seriesName,
            string badgeName,
            string epilogueTitle,
            string epilogueBody)
        {
            SeriesId = seriesId;
            SeriesKey = seriesKey ?? "";
            SeriesName = seriesName ?? "";
            BadgeName = badgeName ?? "";
            EpilogueTitle = epilogueTitle ?? "";
            EpilogueBody = epilogueBody ?? "";
        }
    }

    /// <summary>
    /// 人物收藏系列图鉴的纯 C# 查询、筛选与合篇解锁门禁（零 UnityEngine 依赖，供 UI 层直接接线）。
    /// 所有集齐与点亮状态严格以服务端 <see cref="global::PlayerAppearanceInfo.SeriesProgress"/> 为唯一真相，
    /// 试用不计入收集进度，也不产生任何战斗或成长属性加成。
    /// </summary>
    public static class AppearanceSeriesModels
    {
        public const uint AllSeriesId = 0u;
        public const float DefaultTabViewportWidth = 612f;
        public const float DefaultTabGap = 7f;
        public const float AllSeriesTabWidth = 76f;

        /// <summary>
        /// 计算横向系列筛选页签宽度（索引 0 为「全部」，其余与 <paramref name="seriesProgress"/> 一一对应）。
        /// 当全部页签自然总宽不超过视口时等比补齐至单行同屏展示；超过视口时保留舒适宽度由横向 ScrollRect 滚动。
        /// </summary>
        public static float[] ComputeSeriesTabWidths(
            IReadOnlyList<global::CharacterAppearanceSeriesProgress> seriesProgress,
            float viewportWidth = DefaultTabViewportWidth,
            float gap = DefaultTabGap)
        {
            int seriesCount = seriesProgress?.Count ?? 0;
            var widths = new float[1 + seriesCount];
            widths[0] = AllSeriesTabWidth;
            if (seriesCount == 0) return widths;

            float total = widths[0] + gap * seriesCount;
            for (int i = 0; i < seriesCount; i++)
            {
                string name = seriesProgress[i]?.Name;
                int charCount = string.IsNullOrEmpty(name) ? 4 : name.Length;
                // 预留「系列名 X/Y 印」单行安全宽度（即使后续集齐追加「印」字也不挤出边界）
                float natural = Math.Min(210f, Math.Max(148f, 78f + charCount * 19f));
                widths[i + 1] = natural;
                total += natural;
            }

            if (total < viewportWidth)
            {
                float spare = viewportWidth - total;
                float perSeries = (float)Math.Floor(spare / seriesCount);
                float remainder = spare - perSeries * seriesCount;
                for (int i = 0; i < seriesCount; i++)
                {
                    widths[i + 1] += perSeries + (i < remainder ? 1f : 0f);
                }
            }
            return widths;
        }

        /// <summary>按系列筛选衣柜列表：selectedSeriesId == 0 返回全部，否则仅返回属于该系列的启用成员。</summary>
        public static List<global::CharacterAppearanceEntry> FilterBySeries(
            global::PlayerAppearanceInfo info,
            uint selectedSeriesId)
        {
            var result = new List<global::CharacterAppearanceEntry>();
            if (info == null || info.Appearances == null) return result;
            for (int i = 0; i < info.Appearances.Count; i++)
            {
                var entry = info.Appearances[i];
                if (entry == null) continue;
                if (selectedSeriesId == AllSeriesId || entry.SeriesId == selectedSeriesId)
                    result.Add(entry);
            }
            return result;
        }

        /// <summary>按系列 ID 查找服务端权威系列进度。</summary>
        public static global::CharacterAppearanceSeriesProgress FindSeries(
            global::PlayerAppearanceInfo info,
            uint seriesId)
        {
            if (info == null || info.SeriesProgress == null || seriesId == AllSeriesId) return null;
            for (int i = 0; i < info.SeriesProgress.Count; i++)
            {
                var item = info.SeriesProgress[i];
                if (item != null && item.SeriesId == seriesId) return item;
            }
            return null;
        }

        /// <summary>统计已点亮集齐的系列数量。</summary>
        public static int CountCompletedSeries(global::PlayerAppearanceInfo info)
        {
            if (info == null || info.SeriesProgress == null) return 0;
            int count = 0;
            for (int i = 0; i < info.SeriesProgress.Count; i++)
            {
                var item = info.SeriesProgress[i];
                if (item != null && item.Completed && item.TotalCount > 0) count++;
            }
            return count;
        }

        /// <summary>
        /// 严格按权威 <c>Completed == true</c> 校验并读取已解锁的系列合篇故事（正文由客户端查本地配表）。
        /// 未集齐、缺篇名或正文为空时一律 fail-closed 返回 false。
        /// </summary>
        public static bool TryGetUnlockedEpilogue(
            global::PlayerAppearanceInfo info,
            uint seriesId,
            Func<uint, string> epilogueBodyLookup,
            out AppearanceSeriesEpilogue epilogue)
        {
            epilogue = null;
            var series = FindSeries(info, seriesId);
            if (series == null || !series.Completed || series.TotalCount == 0) return false;
            if (string.IsNullOrWhiteSpace(series.EpilogueTitle) || epilogueBodyLookup == null) return false;
            string body = epilogueBodyLookup(seriesId);
            if (string.IsNullOrWhiteSpace(body)) return false;
            epilogue = new AppearanceSeriesEpilogue(
                series.SeriesId,
                series.SeriesKey,
                series.Name,
                series.CompletionBadgeName,
                series.EpilogueTitle,
                body);
            return true;
        }
    }
}
