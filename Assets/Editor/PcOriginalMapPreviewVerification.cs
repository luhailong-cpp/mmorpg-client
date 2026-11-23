using System;
using System.Collections.Generic;
using System.IO;
using MmorpgClient.World.CurrentMaps;
using MmorpgClient.World.Tianyong;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MmorpgClient.Editor.CurrentMaps
{
    /// <summary>
    /// Editor/Batchmode verification for all 36 PC original static background maps:
    /// checks scene binding, catalog validation, overview textures, corner/edge native tiles,
    /// texture import settings (RGB vs RGBA alpha), and gap/HOLD exclusions.
    /// </summary>
    public static class PcOriginalMapPreviewVerification
    {
        public const string ScenePath = "Assets/Scenes/World/PcOriginalMapPreview.unity";

        [Serializable]
        private sealed class MapVerificationItem
        {
            public string mapId;
            public string name;
            public int nativeWidth;
            public int nativeHeight;
            public int overviewWidth;
            public int overviewHeight;
            public int tileCount;
            public bool hasAlpha;
            public int sampledTilesVerified;
            public string resourceRoot;
        }

        [Serializable]
        private sealed class VerificationReport
        {
            public string result;
            public string unityVersion;
            public string scenePath;
            public string catalogResourcePath;
            public int readyCount;
            public int unavailableCount;
            public int specialHoldCount;
            public int totalSampledTilesVerified;
            public string error;
            public MapVerificationItem[] maps;
        }

        [MenuItem("MMORPG/Maps/验收端游原图预览 (36图)")]
        public static void RunMenuVerification()
        {
            var report = ExecuteVerification(writeToLogs: true);
            Debug.Log($"[PcOriginalMapPreviewVerification] {report.result}: ready={report.readyCount} sampledTiles={report.totalSampledTilesVerified}");
        }

        public static void RunBatchVerification()
        {
            bool ok = false;
            try
            {
                var report = ExecuteVerification(writeToLogs: true);
                ok = report.result == "PASS";
                Debug.Log($"[PcOriginalMapPreviewVerification] BATCH {report.result}: ready={report.readyCount} sampledTiles={report.totalSampledTilesVerified}");
            }
            catch (Exception error)
            {
                Debug.LogError("[PcOriginalMapPreviewVerification] BATCH FAIL: " + error);
            }
            EditorApplication.Exit(ok ? 0 : 1);
        }

        private static VerificationReport ExecuteVerification(bool writeToLogs)
        {
            var report = new VerificationReport
            {
                result = "FAIL",
                unityVersion = Application.unityVersion,
                scenePath = ScenePath,
                catalogResourcePath = PcOriginalMapContract.DefaultCatalogResourcePath,
                maps = Array.Empty<MapVerificationItem>()
            };

            try
            {
                // 1. Open standalone preview scene and verify viewer component binding.
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                if (!scene.IsValid())
                    throw new InvalidOperationException("Failed to open scene: " + ScenePath);
                var viewer = UnityEngine.Object.FindAnyObjectByType<PcOriginalMapPreviewViewer>();
                if (viewer == null || !viewer.IsStandalone)
                    throw new InvalidOperationException("Scene is missing standalone PcOriginalMapPreviewViewer.");

                // 2. Load and validate catalog via Unity Resources.
                var catalogAsset = Resources.Load<TextAsset>(PcOriginalMapContract.DefaultCatalogResourcePath);
                if (catalogAsset == null)
                    throw new InvalidOperationException("Missing catalog TextAsset: " + PcOriginalMapContract.DefaultCatalogResourcePath);
                var catalog = JsonUtility.FromJson<PcOriginalMapCatalog>(catalogAsset.text);
                if (catalog == null)
                    throw new InvalidOperationException("Failed to deserialize catalog JSON.");
                catalog.Validate();

                if (catalog.readyCount != 36 || catalog.unavailableCount != 4 || catalog.specialHoldCount != 2)
                    throw new InvalidOperationException(
                        $"Expected 36 ready / 4 unavailable / 2 special HOLD, got {catalog.readyCount}/{catalog.unavailableCount}/{catalog.specialHoldCount}.");

                // 3. Verify all 36 maps: manifest, overview texture, and representative native tiles (first + last edge tile).
                var items = new List<MapVerificationItem>(catalog.maps.Length);
                int totalSampled = 0;

                for (int i = 0; i < catalog.maps.Length; i++)
                {
                    var map = catalog.maps[i];
                    var tilesAsset = Resources.Load<TextAsset>(map.tilesManifestResourcePath);
                    if (tilesAsset == null)
                        throw new InvalidOperationException($"{map.mapId}: missing tiles manifest {map.tilesManifestResourcePath}");
                    var manifest = JsonUtility.FromJson<PcOriginalTileManifest>(tilesAsset.text);
                    if (manifest == null)
                        throw new InvalidOperationException($"{map.mapId}: failed to parse tiles manifest.");
                    manifest.Validate(map);

                    var overview = Resources.Load<Texture2D>(map.overviewResourcePath);
                    if (overview == null)
                        throw new InvalidOperationException($"{map.mapId}: failed to load overview texture {map.overviewResourcePath}");
                    if (overview.width != map.overviewWidth || overview.height != map.overviewHeight)
                        throw new InvalidOperationException(
                            $"{map.mapId}: overview size mismatch {overview.width}x{overview.height} != {map.overviewWidth}x{map.overviewHeight}");
                    if (overview.mipmapCount != 1)
                        throw new InvalidOperationException($"{map.mapId}: overview mipmapCount={overview.mipmapCount}, expected 1.");
                    Resources.UnloadAsset(overview);

                    // Sample top-left tile, bottom-right remainder tile, and center tile to verify exact dimensions & alpha format.
                    var sampleIndices = new HashSet<int> { 0, manifest.tiles.Length - 1, manifest.tiles.Length / 2 };
                    int sampledForMap = 0;
                    foreach (int tileIndex in sampleIndices)
                    {
                        var tile = manifest.tiles[tileIndex];
                        var tex = Resources.Load<Texture2D>(tile.resourcePath);
                        if (tex == null)
                            throw new InvalidOperationException($"{map.mapId}: failed to load tile {tile.resourcePath}");
                        if (tex.width != tile.width || tex.height != tile.height)
                            throw new InvalidOperationException(
                                $"{map.mapId}: tile {tile.resourcePath} size {tex.width}x{tex.height} != {tile.width}x{tile.height}");
                        if (tex.mipmapCount != 1)
                            throw new InvalidOperationException($"{map.mapId}: tile {tile.resourcePath} mipmapCount={tex.mipmapCount}, expected 1.");
                        if (map.hasAlpha && tex.format != TextureFormat.RGBA32)
                            throw new InvalidOperationException($"{map.mapId}: RGBA tile {tile.resourcePath} format={tex.format}, expected RGBA32.");
                        if (!map.hasAlpha && tex.format != TextureFormat.RGB24)
                            throw new InvalidOperationException($"{map.mapId}: RGB tile {tile.resourcePath} format={tex.format}, expected RGB24.");
                        Resources.UnloadAsset(tex);
                        sampledForMap++;
                        totalSampled++;
                    }

                    items.Add(new MapVerificationItem
                    {
                        mapId = map.mapId,
                        name = map.DisplayName,
                        nativeWidth = map.nativeWidth,
                        nativeHeight = map.nativeHeight,
                        overviewWidth = map.overviewWidth,
                        overviewHeight = map.overviewHeight,
                        tileCount = map.tileCount,
                        hasAlpha = map.hasAlpha,
                        sampledTilesVerified = sampledForMap,
                        resourceRoot = map.resourceRoot
                    });
                }

                report.readyCount = catalog.readyCount;
                report.unavailableCount = catalog.unavailableCount;
                report.specialHoldCount = catalog.specialHoldCount;
                report.totalSampledTilesVerified = totalSampled;
                report.maps = items.ToArray();
                report.result = "PASS";
            }
            catch (Exception error)
            {
                report.error = error.ToString();
                report.result = "FAIL";
                if (writeToLogs) WriteReport(report);
                throw;
            }

            if (writeToLogs) WriteReport(report);
            return report;
        }

        private static void WriteReport(VerificationReport report)
        {
            string logDir = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
            Directory.CreateDirectory(logDir);
            string outPath = Path.Combine(logDir, "pc-original-unity-verification.json");
            File.WriteAllText(outPath, JsonUtility.ToJson(report, true));
        }
    }
}
