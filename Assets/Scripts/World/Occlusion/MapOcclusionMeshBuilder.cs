using System;
using System.Collections.Generic;
using MmorpgClient.World.NavigationAuthoring;
using UnityEngine;
using UnityEngine.Rendering;

namespace MmorpgClient.World.Occlusion
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// Builds one merged static depth-mask mesh for all foreground occluders on a map.
    /// Each occluder vertex receives its top-down depth Y interpolated along the occluder's
    /// south ground-contact baseline, supporting horizontal and diagonal walls/eaves in 1 draw call.
    /// </summary>
    public static class MapOcclusionMeshBuilder
    {
        public static Mesh BuildMergedMesh(MapOcclusionData data, Rect worldRect)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            data.Validate();
            if (worldRect.width <= 0f || worldRect.height <= 0f)
                throw new InvalidOperationException("地图世界矩形尺寸无效。");
            if (!data.HasOccluders) return null;

            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var worldPolygon = new List<Vector2>();

            for (int i = 0; i < data.occluders.Length; i++)
            {
                var occluder = data.occluders[i];
                if (occluder == null || occluder.polygonPixels == null || occluder.polygonPixels.Length < 3)
                    continue;

                worldPolygon.Clear();
                for (int p = 0; p < occluder.polygonPixels.Length; p++)
                {
                    worldPolygon.Add(OcclusionDepthMapper.PixelToWorld(
                        occluder.polygonPixels[p].ToVector2(),
                        data.imageWidth,
                        data.imageHeight,
                        worldRect));
                }

                occluder.ResolveBaselinePixels(out var basePixel0, out var basePixel1);
                var baseWorld0 = OcclusionDepthMapper.PixelToWorld(basePixel0, data.imageWidth, data.imageHeight, worldRect);
                var baseWorld1 = OcclusionDepthMapper.PixelToWorld(basePixel1, data.imageWidth, data.imageHeight, worldRect);

                var triVertices = NavigationPolygonGeometry.TriangulateSimplePolygon(worldPolygon);
                for (int t = 0; t + 2 < triVertices.Count; t += 3)
                {
                    int start = vertices.Count;
                    for (int k = 0; k < 3; k++)
                    {
                        var v2 = triVertices[t + k];
                        float baseZ = OcclusionDepthMapper.EvaluateBaselineZ(baseWorld0, baseWorld1, v2.x);
                        float depthY = OcclusionDepthMapper.WorldZToDepthY(baseZ, worldRect);
                        vertices.Add(new Vector3(v2.x, depthY, v2.y));
                    }

                    // CCW in XZ maps to downward normal; swap (1, 2) for upward +Y normal.
                    indices.Add(start);
                    indices.Add(start + 2);
                    indices.Add(start + 1);
                }
            }

            if (indices.Count == 0) return null;

            var mesh = new Mesh
            {
                name = string.IsNullOrEmpty(data.mapKey)
                    ? "MapOcclusionDepthMaskMesh"
                    : $"MapOcclusionDepthMask_{data.mapKey}"
            };
            if (vertices.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
