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

        private static void AppendTag(StringBuilder sb, string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(tag);
        }
    }
}
