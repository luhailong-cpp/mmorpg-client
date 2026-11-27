using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MmorpgClient.World.NavigationAuthoring;
using MmorpgClient.World.Occlusion;
using UnityEditor;
using UnityEngine;

namespace MmorpgClient.Editor.NavigationAuthoring
{
    /// <summary>
    /// Editor-only sidecar storage and export for map occlusion and fade zones (<c>occlusion.json</c>).
    /// Strictly separated from <see cref="NavMeshAuthoringDraft"/> so editing visual occlusion never
    /// invalidates baked NavMeshData or server MSET geometry hashes.
    /// </summary>
    public static class MapOcclusionAuthoringIO
    {
        [Serializable]
        public sealed class EditableOccluder
        {
            public string id;
            public string name;
            public bool showSilhouette = true;
            public bool customBaseline;
            public Vector2 baselineP0;
            public Vector2 baselineP1;
            public List<Vector2> points = new List<Vector2>();

            public void ResolveBaseline(out Vector2 p0, out Vector2 p1)
            {
                if (customBaseline)
                {
                    p0 = baselineP0;
                    p1 = baselineP1;
                    return;
                }
                MapOccluderDefinition.ComputeDefaultBaseline(points, out p0, out p1);
            }
        }

        [Serializable]
        public sealed class EditableFadeZone
        {
            public string id;
            public string name;
            public float targetAlpha = 0.5f;
            public float fadeDurationSec = 0.2f;
            public List<Vector2> points = new List<Vector2>();
        }

        [Serializable]
        public sealed class EditableDocument
        {
            public string mapKey = string.Empty;
            public int imageWidth;
            public int imageHeight;
            public List<EditableOccluder> occluders = new List<EditableOccluder>();
            public List<EditableFadeZone> fadeZones = new List<EditableFadeZone>();

            public bool IsEmpty => (occluders == null || occluders.Count == 0) &&
                                   (fadeZones == null || fadeZones.Count == 0);

            public MapOcclusionData ToContractData(NavMeshAuthoringDraft draft)
            {
                int width = draft != null && draft.imageWidth > 0 ? draft.imageWidth : imageWidth;
                int height = draft != null && draft.imageHeight > 0 ? draft.imageHeight : imageHeight;
                string key = draft != null && !string.IsNullOrWhiteSpace(draft.mapKey) ? draft.mapKey : mapKey;

                var occluderList = new List<MapOccluderDefinition>();
                if (occluders != null)
                {
                    for (int i = 0; i < occluders.Count; i++)
                    {
                        var item = occluders[i];
                        if (item == null) continue;
                        item.ResolveBaseline(out var p0, out var p1);
                        occluderList.Add(new MapOccluderDefinition
                        {
                            id = string.IsNullOrWhiteSpace(item.id) ? $"occluder_{i + 1:000}" : item.id.Trim(),
                            name = string.IsNullOrWhiteSpace(item.name) ? $"遮挡体{i + 1}" : item.name.Trim(),
                            showSilhouette = item.showSilhouette,
                            baselinePixels = new[]
                            {
                                MapOcclusionPoint.FromVector2(p0),
                                MapOcclusionPoint.FromVector2(p1)
                            },
                            polygonPixels = ToPoints(item.points)
                        });
                    }
                }

                var fadeZoneList = new List<MapFadeZoneDefinition>();
                if (fadeZones != null)
                {
                    for (int i = 0; i < fadeZones.Count; i++)
                    {
                        var zone = fadeZones[i];
                        if (zone == null) continue;
                        fadeZoneList.Add(new MapFadeZoneDefinition
                        {
                            id = string.IsNullOrWhiteSpace(zone.id) ? $"fade_zone_{i + 1:000}" : zone.id.Trim(),
                            name = string.IsNullOrWhiteSpace(zone.name) ? $"半透明区{i + 1}" : zone.name.Trim(),
                            targetAlpha = zone.targetAlpha,
                            fadeDurationSec = zone.fadeDurationSec,
                            polygonPixels = ToPoints(zone.points)
                        });
                    }
                }

                return new MapOcclusionData
                {
                    schemaVersion = MapOcclusionData.CurrentSchemaVersion,
                    mapKey = key ?? string.Empty,
                    imageWidth = width,
                    imageHeight = height,
                    occluders = occluderList.ToArray(),
                    fadeZones = fadeZoneList.ToArray()
                };
            }

            public static EditableDocument FromContractData(MapOcclusionData data)
            {
                var doc = new EditableDocument
                {
                    mapKey = data?.mapKey ?? string.Empty,
                    imageWidth = data?.imageWidth ?? 0,
                    imageHeight = data?.imageHeight ?? 0
                };
                if (data == null) return doc;

                if (data.occluders != null)
                {
                    for (int i = 0; i < data.occluders.Length; i++)
                    {
                        var src = data.occluders[i];
                        if (src == null) continue;
                        bool hasCustomBaseline = src.baselinePixels != null && src.baselinePixels.Length >= 2;
                        src.ResolveBaselinePixels(out var p0, out var p1);
                        doc.occluders.Add(new EditableOccluder
                        {
                            id = src.id,
                            name = src.name,
                            showSilhouette = src.showSilhouette,
                            customBaseline = hasCustomBaseline,
                            baselineP0 = p0,
                            baselineP1 = p1,
                            points = FromPoints(src.polygonPixels)
                        });
                    }
                }

                if (data.fadeZones != null)
                {
                    for (int i = 0; i < data.fadeZones.Length; i++)
                    {
                        var src = data.fadeZones[i];
                        if (src == null) continue;
                        doc.fadeZones.Add(new EditableFadeZone
                        {
                            id = src.id,
                            name = src.name,
                            targetAlpha = src.targetAlpha,
                            fadeDurationSec = src.fadeDurationSec,
                            points = FromPoints(src.polygonPixels)
                        });
                    }
                }
                return doc;
            }

            private static MapOcclusionPoint[] ToPoints(List<Vector2> vectors)
            {
                if (vectors == null || vectors.Count == 0) return Array.Empty<MapOcclusionPoint>();
                var result = new MapOcclusionPoint[vectors.Count];
                for (int i = 0; i < vectors.Count; i++)
                    result[i] = MapOcclusionPoint.FromVector2(vectors[i]);
                return result;
            }

            private static List<Vector2> FromPoints(MapOcclusionPoint[] points)
            {
                var result = new List<Vector2>(points != null ? points.Length : 0);
                if (points != null)
                {
                    for (int i = 0; i < points.Length; i++)
                        result.Add(points[i].ToVector2());
                }
                return result;
            }
        }

        public static string SidecarPathForDraft(NavMeshAuthoringDraft draft)
        {
            if (draft == null) return null;
            string assetPath = AssetDatabase.GetAssetPath(draft);
            if (string.IsNullOrEmpty(assetPath)) return null;
            return Path.ChangeExtension(assetPath, ".occlusion.json").Replace('\\', '/');
        }

        public static EditableDocument LoadOrCreate(NavMeshAuthoringDraft draft)
        {
            if (draft == null) return new EditableDocument();
            string path = SidecarPathForDraft(draft);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var raw = JsonUtility.FromJson<MapOcclusionData>(json);
                if (raw != null)
                {
                    var loaded = EditableDocument.FromContractData(raw);
                    loaded.mapKey = draft.mapKey;
                    loaded.imageWidth = draft.imageWidth;
                    loaded.imageHeight = draft.imageHeight;
                    return loaded;
                }
            }

            return new EditableDocument
            {
                mapKey = draft.mapKey ?? string.Empty,
                imageWidth = draft.imageWidth,
                imageHeight = draft.imageHeight
            };
        }

        public static string SaveSidecar(NavMeshAuthoringDraft draft, EditableDocument document)
        {
            if (draft == null) throw new InvalidOperationException("请先选择地图草稿。");
            string path = SidecarPathForDraft(draft);
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("请先保存地图草稿资产。");

            var contract = (document ?? new EditableDocument()).ToContractData(draft);
            string json = contract.ToJson(true) + "\n";
            File.WriteAllText(path, json, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return path;
        }

        public static string ExportRuntimeJson(NavMeshAuthoringDraft draft, EditableDocument document, string outputFilePath)
        {
            if (draft == null) throw new InvalidOperationException("请先选择地图草稿。");
            if (string.IsNullOrWhiteSpace(outputFilePath)) throw new InvalidOperationException("导出路径为空。");

            var contract = (document ?? new EditableDocument()).ToContractData(draft);
            contract.Validate();
            string json = contract.ToJson(true) + "\n";
            string dir = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outputFilePath, json, new UTF8Encoding(false));
            if (outputFilePath.Replace('\\', '/').StartsWith(Application.dataPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                AssetDatabase.Refresh();
            return outputFilePath;
        }

        public static string BuildRuntimeJsonForPreview(NavMeshAuthoringDraft draft, EditableDocument document)
        {
            if (draft == null || document == null || document.IsEmpty) return null;
            var contract = document.ToContractData(draft);
            contract.Validate();
            return contract.ToJson(false);
        }
    }
}
