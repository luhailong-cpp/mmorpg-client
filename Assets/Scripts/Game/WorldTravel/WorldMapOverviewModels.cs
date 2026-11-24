using System;
using System.Collections.Generic;
using System.Text;

namespace MmorpgClient.Game.WorldTravel
{
    /// <summary>单张已开放大世界地图的聚合热度与玩法标记快照（纯数据，无 Unity 依赖）。</summary>
    public sealed class WorldMapOverviewItem
    {
        public uint SceneConfigId { get; }
        public uint TotalPlayerCount { get; }
        public uint ChannelCount { get; }
        public global::WorldMapHeatLevel HeatLevel { get; }
        public ulong RecommendedSceneId { get; }
        public uint RecommendedChannelNo { get; }
        public bool IsTideMap { get; }
        public bool HasMysteryMerchant { get; }

        public WorldMapOverviewItem(uint sceneConfigId, uint totalPlayerCount, uint channelCount,
            global::WorldMapHeatLevel heatLevel, ulong recommendedSceneId, uint recommendedChannelNo,
            bool isTideMap, bool hasMysteryMerchant)
        {
            SceneConfigId = sceneConfigId;
            TotalPlayerCount = totalPlayerCount;
            ChannelCount = channelCount;
            HeatLevel = heatLevel;
            RecommendedSceneId = recommendedSceneId;
            RecommendedChannelNo = recommendedChannelNo;
            IsTideMap = isTideMap;
            HasMysteryMerchant = hasMysteryMerchant;
        }
    }

    /// <summary>全服微观灵潮轮换快照。</summary>
    public sealed class WorldTideSnapshotModel
    {
        public bool Active { get; }
        public ulong RoundId { get; }
        public uint SceneConfigId { get; }
        public ulong RecommendedSceneId { get; }
        public uint RecommendedChannelNo { get; }
        public ulong StartsAtMs { get; }
        public ulong EndsAtMs { get; }
        public ulong NextStartsAtMs { get; }

        public WorldTideSnapshotModel(bool active, ulong roundId, uint sceneConfigId, ulong recommendedSceneId,
            uint recommendedChannelNo, ulong startsAtMs, ulong endsAtMs, ulong nextStartsAtMs)
        {
            Active = active;
            RoundId = roundId;
            SceneConfigId = sceneConfigId;
            RecommendedSceneId = recommendedSceneId;
            RecommendedChannelNo = recommendedChannelNo;
            StartsAtMs = startsAtMs;
            EndsAtMs = endsAtMs;
            NextStartsAtMs = nextStartsAtMs;
        }
    }

    /// <summary>本区大世界地图热度、灵潮与今日旺地总览快照。</summary>
    public sealed class WorldMapOverviewSnapshot
    {
        public uint ZoneId { get; }
        public ulong UpdatedAtMs { get; }
        public ulong ServerTimeMs { get; }
        public IReadOnlyList<WorldMapOverviewItem> Maps { get; }
        public WorldTideSnapshotModel Tide { get; }
        public uint DailyHotspotSceneConfigId { get; }

        public WorldMapOverviewSnapshot(uint zoneId, ulong updatedAtMs, ulong serverTimeMs,
            IReadOnlyList<WorldMapOverviewItem> maps, WorldTideSnapshotModel tide, uint dailyHotspotSceneConfigId)
        {
            ZoneId = zoneId;
            UpdatedAtMs = updatedAtMs;
            ServerTimeMs = serverTimeMs;
            Maps = maps ?? Array.Empty<WorldMapOverviewItem>();
            Tide = tide;
            DailyHotspotSceneConfigId = dailyHotspotSceneConfigId;
        }

        public WorldMapOverviewItem FindMap(uint sceneConfigId)
        {
            if (sceneConfigId == 0) return null;
            for (int i = 0; i < Maps.Count; ++i)
            {
                if (Maps[i].SceneConfigId == sceneConfigId) return Maps[i];
            }
            return null;
        }
    }

    /// <summary>大地图顶部灵潮横幅的纯渲染视图（无 Unity 依赖，供高保真 UI 直接绑定）。</summary>
    public readonly struct WorldMapTideBannerView
    {
        public bool Visible { get; }
        public bool Active { get; }
        public ulong RoundId { get; }
        public uint TargetSceneConfigId { get; }
        public ulong RecommendedSceneId { get; }
        public uint RecommendedChannelNo { get; }
        public string TargetMapName { get; }
        public long RemainingSeconds { get; }
        public string Headline { get; }
        public string CountdownText { get; }
        public bool IsOnTargetMap { get; }
        public bool CanQuickTravel { get; }
        public string ActionLabel { get; }

        public WorldMapTideBannerView(bool visible, bool active, ulong roundId, uint targetSceneConfigId,
            ulong recommendedSceneId, uint recommendedChannelNo, string targetMapName, long remainingSeconds,
            string headline, string countdownText, bool isOnTargetMap, bool canQuickTravel, string actionLabel)
        {
            Visible = visible;
            Active = active;
            RoundId = roundId;
            TargetSceneConfigId = targetSceneConfigId;
            RecommendedSceneId = recommendedSceneId;
            RecommendedChannelNo = recommendedChannelNo;
            TargetMapName = targetMapName ?? "";
            RemainingSeconds = remainingSeconds;
            Headline = headline ?? "";
            CountdownText = countdownText ?? "";
            IsOnTargetMap = isOnTargetMap;
            CanQuickTravel = canQuickTravel;
            ActionLabel = actionLabel ?? "";
        }
    }

    /// <summary>单张地图锚点的纯状态视图（热度/灵潮/今日旺地/神秘商人/推荐聚人线，供底图锚点直接绑定）。</summary>
    public readonly struct WorldMapPinView
    {
        public uint SceneConfigId { get; }
        public bool OnlineAvailable { get; }
        public bool HasOverviewEntry { get; }
        public global::WorldMapHeatLevel HeatLevel { get; }
        public string HeatLabel { get; }
        public uint TotalPlayerCount { get; }
        public uint ChannelCount { get; }
        public ulong RecommendedSceneId { get; }
        public uint RecommendedChannelNo { get; }
        public bool IsCurrentScene { get; }
        public bool IsTideMap { get; }
        public bool IsDailyHotspot { get; }
        public bool HasMysteryMerchant { get; }

        public WorldMapPinView(uint sceneConfigId, bool onlineAvailable, bool hasOverviewEntry,
            global::WorldMapHeatLevel heatLevel, string heatLabel, uint totalPlayerCount, uint channelCount,
            ulong recommendedSceneId, uint recommendedChannelNo, bool isCurrentScene, bool isTideMap,
            bool isDailyHotspot, bool hasMysteryMerchant)
        {
            SceneConfigId = sceneConfigId;
            OnlineAvailable = onlineAvailable;
            HasOverviewEntry = hasOverviewEntry;
            HeatLevel = heatLevel;
            HeatLabel = heatLabel ?? "";
            TotalPlayerCount = totalPlayerCount;
            ChannelCount = channelCount;
            RecommendedSceneId = recommendedSceneId;
            RecommendedChannelNo = recommendedChannelNo;
            IsCurrentScene = isCurrentScene;
            IsTideMap = isTideMap;
            IsDailyHotspot = isDailyHotspot;
            HasMysteryMerchant = hasMysteryMerchant;
        }
    }

    /// <summary>世界地图总览的纯转换与文案规则（EditMode 测试直接调用）。</summary>
    public static class WorldMapOverviewModels
    {
        public static WorldMapOverviewSnapshot FromProto(global::WorldMapOverview proto)
        {
            if (proto == null) return null;
            var items = new List<WorldMapOverviewItem>(proto.Maps.Count);
            for (int i = 0; i < proto.Maps.Count; ++i)
            {
                var entry = proto.Maps[i];
                if (entry == null || entry.SceneConfigId == 0) continue;
                items.Add(new WorldMapOverviewItem(
                    entry.SceneConfigId,
                    entry.TotalPlayerCount,
                    entry.ChannelCount,
                    entry.HeatLevel,
                    entry.RecommendedSceneId,
                    entry.RecommendedChannelNo,
                    entry.IsTideMap,
                    entry.HasMysteryMerchant));
            }
            WorldTideSnapshotModel tide = null;
            if (proto.Tide != null && (proto.Tide.SceneConfigId != 0 || proto.Tide.Active))
            {
                tide = new WorldTideSnapshotModel(
                    proto.Tide.Active,
                    proto.Tide.RoundId,
                    proto.Tide.SceneConfigId,
                    proto.Tide.RecommendedSceneId,
                    proto.Tide.RecommendedChannelNo,
                    proto.Tide.StartsAtMs,
                    proto.Tide.EndsAtMs,
                    proto.Tide.NextStartsAtMs);
            }
            return new WorldMapOverviewSnapshot(
                proto.ZoneId,
                proto.UpdatedAtMs,
                proto.ServerTimeMs,
                items,
                tide,
                proto.DailyHotspotSceneConfigId);
        }

        public static string HeatLabel(global::WorldMapHeatLevel level)
        {
            switch (level)
            {
                case global::WorldMapHeatLevel.Crowded: return "热闹";
                case global::WorldMapHeatLevel.Active: return "有人";
                case global::WorldMapHeatLevel.Quiet: return "清幽";
                default: return "";
            }
        }

        /// <summary>将某张地图的热度、灵潮、今日旺地与神秘商人状态拼成简短标签串；无总览数据时返回空串。</summary>
        public static string FormatDestinationBadges(WorldMapOverviewSnapshot overview, uint sceneConfigId)
        {
            if (overview == null || sceneConfigId == 0) return "";
            var item = overview.FindMap(sceneConfigId);
            var sb = new StringBuilder();
            if (item != null)
            {
                AppendTag(sb, HeatLabel(item.HeatLevel));
                if (item.IsTideMap) AppendTag(sb, "灵潮中");
            }
            if (overview.DailyHotspotSceneConfigId == sceneConfigId) AppendTag(sb, "今日旺地");
            if (item != null && item.HasMysteryMerchant) AppendTag(sb, "神秘商人");
            return sb.ToString();
        }

        /// <summary>格式化某张地图的动态态势提示（推荐聚人线 + 灵潮剩余时间）；无信息时返回空串。</summary>
        public static string FormatDestinationHint(WorldMapOverviewSnapshot overview, uint sceneConfigId)
        {
            if (overview == null || sceneConfigId == 0) return "";
            var item = overview.FindMap(sceneConfigId);
            if (item == null) return "";
            var sb = new StringBuilder();
            if (item.RecommendedChannelNo != 0)
            {
                sb.Append("推荐落脚 ").Append(SceneChannelModels.LineName(item.RecommendedChannelNo));
            }
            if (item.IsTideMap && overview.Tide != null && overview.Tide.EndsAtMs > overview.ServerTimeMs)
            {
                ulong remainSec = (overview.Tide.EndsAtMs - overview.ServerTimeMs) / 1000UL;
                ulong remainMin = Math.Max(1UL, (remainSec + 59UL) / 60UL);
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append("灵潮余约 ").Append(remainMin).Append(" 分");
            }
            if (item.HasMysteryMerchant)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append("神秘商人正在此图现身");
            }
            return sb.ToString();
        }

        /// <summary>根据快照服务端时间戳与本地经过秒数估算当前服务端毫秒时间，供每秒倒计时刷新（无需额外发包）。</summary>
        public static ulong EstimateServerNowMs(WorldMapOverviewSnapshot overview, float elapsedSinceSnapshotSeconds)
        {
            if (overview == null) return 0UL;
            ulong baseMs = overview.ServerTimeMs > 0UL ? overview.ServerTimeMs : overview.UpdatedAtMs;
            if (baseMs == 0UL || !(elapsedSinceSnapshotSeconds > 0f)) return baseMs;
            double delta = Math.Floor((double)elapsedSinceSnapshotSeconds * 1000.0);
            if (delta <= 0.0 || double.IsNaN(delta) || double.IsInfinity(delta)) return baseMs;
            ulong deltaMs = delta >= (double)(ulong.MaxValue - baseMs) ? ulong.MaxValue - baseMs : (ulong)delta;
            return baseMs + deltaMs;
        }

        public static string FormatMinutesSeconds(long totalSeconds)
        {
            long clamped = totalSeconds > 0 ? totalSeconds : 0;
            long minutes = clamped / 60;
            long seconds = clamped % 60;
            return minutes.ToString("00") + ":" + seconds.ToString("00");
        }

        /// <summary>生成大地图顶部灵潮横幅视图；无总览或无灵潮数据时 Visible=false，UI 整条隐藏。</summary>
        public static WorldMapTideBannerView BuildTideBanner(WorldMapOverviewSnapshot overview,
            uint currentSceneConfigId, Func<uint, string> mapNameResolver, ulong serverNowMs,
            bool canTravelToTideMap, bool busy = false)
        {
            var tide = overview?.Tide;
            if (tide == null) return default;

            ulong nowMs = serverNowMs > 0UL ? serverNowMs : (overview.ServerTimeMs > 0UL ? overview.ServerTimeMs : overview.UpdatedAtMs);
            string mapName = tide.SceneConfigId != 0 && mapNameResolver != null ? (mapNameResolver(tide.SceneConfigId) ?? "") : "";
            if (tide.Active)
            {
                long remainSec = RemainingSecondsCeil(tide.EndsAtMs, nowMs);
                bool onTarget = tide.SceneConfigId != 0 && tide.SceneConfigId == currentSceneConfigId;
                bool canQuick = tide.SceneConfigId != 0 && !onTarget && canTravelToTideMap && !busy;
                string headline = string.IsNullOrEmpty(mapName) ? "灵潮涌动" : "灵潮涌动 · " + mapName;
                string countdown = remainSec > 0 ? "剩余 " + FormatMinutesSeconds(remainSec) : "即将结束";
                string action = onTarget ? "已在此地" : busy ? "正在传送…" : canTravelToTideMap ? "立即前往" : "";
                return new WorldMapTideBannerView(true, true, tide.RoundId, tide.SceneConfigId,
                    tide.RecommendedSceneId, tide.RecommendedChannelNo, mapName, remainSec,
                    headline, countdown, onTarget, canQuick, action);
            }

            long nextSec = RemainingSecondsCeil(tide.NextStartsAtMs, nowMs);
            string restCountdown = nextSec > 0 ? "下轮倒计时 " + FormatMinutesSeconds(nextSec) : "即将开启";
            return new WorldMapTideBannerView(true, false, tide.RoundId, tide.SceneConfigId,
                tide.RecommendedSceneId, tide.RecommendedChannelNo, mapName, nextSec,
                "灵潮休歇", restCountdown, false, false, "");
        }

        /// <summary>生成单张地图锚点的聚合热度与标记视图；无总览数据时 HasOverviewEntry=false。</summary>
        public static WorldMapPinView BuildPinView(WorldMapOverviewSnapshot overview, uint sceneConfigId,
            uint currentSceneConfigId, bool onlineAvailable)
        {
            var item = overview?.FindMap(sceneConfigId);
            bool isCurrent = sceneConfigId != 0 && sceneConfigId == currentSceneConfigId;
            bool isHotspot = overview != null && sceneConfigId != 0 && overview.DailyHotspotSceneConfigId == sceneConfigId;
            if (item == null)
            {
                return new WorldMapPinView(sceneConfigId, onlineAvailable, false,
                    global::WorldMapHeatLevel.Unknown, "", 0u, 0u, 0UL, 0u, isCurrent, false, isHotspot, false);
            }
            return new WorldMapPinView(sceneConfigId, onlineAvailable, true,
                item.HeatLevel, HeatLabel(item.HeatLevel), item.TotalPlayerCount, item.ChannelCount,
                item.RecommendedSceneId, item.RecommendedChannelNo, isCurrent,
                item.IsTideMap, isHotspot, item.HasMysteryMerchant);
        }

        /// <summary>主界面「地图」入口是否展示灵潮提示角标：本轮灵潮进行中、玩家未在灵潮图、且本轮未打开过地图。</summary>
        public static bool ShouldShowMapEntryTideBadge(WorldMapOverviewSnapshot overview,
            uint currentSceneConfigId, ulong dismissedTideRoundId)
        {
            var tide = overview?.Tide;
            if (tide == null || !tide.Active || tide.RoundId == 0UL || tide.SceneConfigId == 0u) return false;
            if (tide.RoundId == dismissedTideRoundId) return false;
            return tide.SceneConfigId != currentSceneConfigId;
        }

        private static long RemainingSecondsCeil(ulong targetMs, ulong nowMs)
        {
            if (targetMs <= nowMs) return 0L;
            ulong diff = targetMs - nowMs;
            return (long)((diff + 999UL) / 1000UL);
        }

        private static void AppendTag(StringBuilder sb, string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(tag);
        }
    }
}
