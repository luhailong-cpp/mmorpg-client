using System;
using System.Collections.Generic;
using MmorpgClient.World.NavigationAuthoring;
using UnityEngine;

namespace MmorpgClient.World.Occlusion
{
    /// <summary>Source-image pixel point (origin north-west, +X east, +Y south).</summary>
    [Serializable]
    public struct MapOcclusionPoint
    {
        public float x;
        public float y;

        public MapOcclusionPoint(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public Vector2 ToVector2() => new Vector2(x, y);

        public static MapOcclusionPoint FromVector2(Vector2 value) => new MapOcclusionPoint(value.x, value.y);
    }

    /// <summary>
    /// Static foreground occluder polygon on a flat painted map (roof, canopy, pillar, wall).
    /// Drawn only into the depth buffer using its south ground-contact baseline so actors north
    /// of the baseline render a translucent silhouette while actors south of it stand in front.
    /// </summary>
    [Serializable]
    public sealed class MapOccluderDefinition
    {
        public string id;
        public string name;
        /// <summary>Reserved toggle: true draws translucent silhouette behind this occluder.</summary>
        public bool showSilhouette = true;
        /// <summary>
        /// Optional two-point ground-contact baseline in source-image pixels (supports diagonal walls/eaves).
        /// When omitted, defaults to a horizontal baseline at the polygon's southmost Y edge.
        /// </summary>
        public MapOcclusionPoint[] baselinePixels;
        public MapOcclusionPoint[] polygonPixels;

        public void ResolveBaselinePixels(out Vector2 p0, out Vector2 p1)
        {
            if (baselinePixels != null && baselinePixels.Length >= 2)
            {
                p0 = baselinePixels[0].ToVector2();
                p1 = baselinePixels[1].ToVector2();
                return;
            }

            if (baselinePixels != null && baselinePixels.Length == 1)
            {
                var single = baselinePixels[0].ToVector2();
                p0 = single;
                p1 = single;
                return;
            }

            ComputeDefaultBaseline(polygonPixels, out p0, out p1);
        }

        public static void ComputeDefaultBaseline(IReadOnlyList<MapOcclusionPoint> polygon, out Vector2 p0, out Vector2 p1)
        {
            if (polygon == null || polygon.Count == 0)
            {
                p0 = Vector2.zero;
                p1 = Vector2.zero;
                return;
            }

            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = 0; i < polygon.Count; i++)
            {
                var pt = polygon[i];
                if (pt.x < minX) minX = pt.x;
                if (pt.x > maxX) maxX = pt.x;
                if (pt.y > maxY) maxY = pt.y;
            }

            p0 = new Vector2(minX, maxY);
            p1 = new Vector2(maxX, maxY);
        }

        public static void ComputeDefaultBaseline(IReadOnlyList<Vector2> polygon, out Vector2 p0, out Vector2 p1)
        {
            if (polygon == null || polygon.Count == 0)
            {
                p0 = Vector2.zero;
                p1 = Vector2.zero;
                return;
            }

            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = 0; i < polygon.Count; i++)
            {
                var pt = polygon[i];
                if (pt.x < minX) minX = pt.x;
                if (pt.x > maxX) maxX = pt.x;
                if (pt.y > maxY) maxY = pt.y;
            }

            p0 = new Vector2(minX, maxY);
            p1 = new Vector2(maxX, maxY);
        }
    }

    /// <summary>
    /// Optional area translucency zone (e.g. tall grass, mist). Actors whose feet enter the polygon
    /// smoothly fade to targetAlpha over fadeDurationSec; overlapping zones take the minimum alpha.
    /// </summary>
    [Serializable]
    public sealed class MapFadeZoneDefinition
    {
        public string id;
        public string name;
        public float targetAlpha = 0.5f;
        public float fadeDurationSec = 0.2f;
        public MapOcclusionPoint[] polygonPixels;
    }

    /// <summary>
    /// Client-only map visual occlusion and fade-zone contract stored in <c>occlusion.json</c>.
    /// Strictly separated from NavMesh walkability and server movement validation.
    /// </summary>
    [Serializable]
    public sealed class MapOcclusionData
    {
        public const int CurrentSchemaVersion = 1;
        public const string ResourceFileName = "occlusion";

        public int schemaVersion = CurrentSchemaVersion;
        public string mapKey = string.Empty;
        public int imageWidth;
        public int imageHeight;
        public MapOccluderDefinition[] occluders = Array.Empty<MapOccluderDefinition>();
        public MapFadeZoneDefinition[] fadeZones = Array.Empty<MapFadeZoneDefinition>();

        public bool HasOccluders => occluders != null && occluders.Length > 0;
        public bool HasFadeZones => fadeZones != null && fadeZones.Length > 0;
        public bool IsEmpty => !HasOccluders && !HasFadeZones;

        public static MapOcclusionData Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("遮挡配置 JSON 为空。");

            var data = JsonUtility.FromJson<MapOcclusionData>(json);
            if (data == null)
                throw new InvalidOperationException("遮挡配置 JSON 解析结果为空。");

            if (data.occluders == null) data.occluders = Array.Empty<MapOccluderDefinition>();
            if (data.fadeZones == null) data.fadeZones = Array.Empty<MapFadeZoneDefinition>();
            data.Validate();
            return data;
        }

        public string ToJson(bool prettyPrint = true)
        {
            if (occluders == null) occluders = Array.Empty<MapOccluderDefinition>();
            if (fadeZones == null) fadeZones = Array.Empty<MapFadeZoneDefinition>();
            return JsonUtility.ToJson(this, prettyPrint);
        }

        public bool TryValidate(out string error)
        {
            try
            {
                Validate();
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public void Validate()
        {
            if (schemaVersion != CurrentSchemaVersion)
                throw new InvalidOperationException($"不支持的遮挡配置版本：{schemaVersion}（期望 {CurrentSchemaVersion}）。");
            if (imageWidth < 1 || imageHeight < 1)
                throw new InvalidOperationException("遮挡配置缺少有效的底图像素尺寸。");

            var occluderIds = new HashSet<string>(StringComparer.Ordinal);
            if (occluders != null)
            {
                for (int i = 0; i < occluders.Length; i++)
                {
                    var item = occluders[i];
                    if (item == null)
                        throw new InvalidOperationException($"遮挡物 #{i + 1} 为空。");
                    if (string.IsNullOrWhiteSpace(item.id) || !occluderIds.Add(item.id))
                        throw new InvalidOperationException($"遮挡物标识缺失或重复：{item.id ?? $"#{i + 1}"}");

                    ValidatePixelPolygon($"遮挡物 {item.id}", item.polygonPixels, imageWidth, imageHeight);

                    if (item.baselinePixels != null && item.baselinePixels.Length > 0)
                    {
                        if (item.baselinePixels.Length != 2)
                            throw new InvalidOperationException($"遮挡物 {item.id} 的接地线必须包含 2 个端点。");
                        for (int b = 0; b < item.baselinePixels.Length; b++)
                            ValidatePixelPoint($"遮挡物 {item.id} 接地线端点", item.baselinePixels[b], imageWidth, imageHeight);
                    }
                }
            }

            var fadeZoneIds = new HashSet<string>(StringComparer.Ordinal);
            if (fadeZones != null)
            {
                for (int i = 0; i < fadeZones.Length; i++)
                {
                    var zone = fadeZones[i];
                    if (zone == null)
                        throw new InvalidOperationException($"半透明区域 #{i + 1} 为空。");
                    if (string.IsNullOrWhiteSpace(zone.id) || !fadeZoneIds.Add(zone.id))
                        throw new InvalidOperationException($"半透明区域标识缺失或重复：{zone.id ?? $"#{i + 1}"}");
                    if (!IsFinite(zone.targetAlpha) || zone.targetAlpha < 0f || zone.targetAlpha > 1f)
                        throw new InvalidOperationException($"半透明区域 {zone.id} 的目标透明度必须在 0 到 1 之间。");
                    if (!IsFinite(zone.fadeDurationSec) || zone.fadeDurationSec < 0f || zone.fadeDurationSec > 30f)
                        throw new InvalidOperationException($"半透明区域 {zone.id} 的渐变时长必须在 0 到 30 秒之间。");

                    ValidatePixelPolygon($"半透明区域 {zone.id}", zone.polygonPixels, imageWidth, imageHeight);
                }
            }
        }

        private static void ValidatePixelPolygon(string label, MapOcclusionPoint[] points, int width, int height)
        {
            if (points == null || points.Length < 3)
                throw new InvalidOperationException($"{label} 至少需要 3 个顶点。");

            var vectors = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                ValidatePixelPoint(label, points[i], width, height);
                vectors[i] = points[i].ToVector2();
            }

            NavigationPolygonGeometry.ValidatePolygon(vectors);
        }

        private static void ValidatePixelPoint(string label, MapOcclusionPoint point, int width, int height)
        {
            if (!IsFinite(point.x) || !IsFinite(point.y))
                throw new InvalidOperationException($"{label} 包含无效数值坐标。");
            if (point.x < 0f || point.y < 0f || point.x > width || point.y > height)
                throw new InvalidOperationException($"{label} 顶点超出底图范围（{width}×{height}）。");
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
