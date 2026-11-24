using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using MmorpgClient.World.CurrentMaps;
using MmorpgClient.World.Tianyong;
using UnityEditor;
using UnityEngine;

namespace MmorpgClient.Editor.CurrentMaps
{
    // 只读取显式指定的本地检查目录。批处理使用真实导入对象与现有查看器的相机、流送和 SpriteRenderer。
    public static class PcMapVariantReview
    {
        [Serializable] private sealed class Request
        {
            public string mapId;
            public int width, height;
            public Version[] versions;
            public View[] views;
        }
        [Serializable] private sealed class Version { public string catalog, appearance; }
        [Serializable] private sealed class View { public float worldX, worldY, orthographicSize; }
        [Serializable] private sealed class ImportedTile
        {
            public string resource, sha256, format, graphicsFormat, compression, npot, filter, wrap;
            public int width, height, importCap, mipCount;
            public float mipBias, xMin, yMin, worldWidth, worldHeight;
        }
        [Serializable] private sealed class Capture
        {
            public string file;
            public float focusX, focusY, zoom;
            public int scaledWidth, scaledHeight, nativeSpritesInView;
        }
        [Serializable] private sealed class PlatformImport
        {
            public string target;
            public bool installedInEditor, textureOverride;
            public int effectiveMaxSize;
        }
        [Serializable] private sealed class VariantResult
        {
            public string catalog, appearance, revision, nativeSha256;
            public float worldWidth, worldHeight;
            public List<ImportedTile> imports = new List<ImportedTile>();
            public List<PlatformImport> platformSettings = new List<PlatformImport>();
            public List<Capture> captures = new List<Capture>();
        }
        [Serializable] private sealed class Report
        {
            public string status, error, unity, buildTarget, graphicsDevice, quality, colorSpace, mapId;
            public int mipLimit;
            public float fixedDpiFactor, dynamicWidthScale, dynamicHeightScale;
            public List<VariantResult> variants = new List<VariantResult>();
        }
        private static Request request;
        private static Report report;
        private static string output;
        private static PcOriginalMapPreviewViewer viewer;
        private static RenderTexture target;
        private static int versionIndex, viewIndex;
        private static double deadline;

        [MenuItem("Assets/MMORPG/用选中目录比较当前地图")]
        public static void OpenSelectedCatalog()
        {
            var active = UnityEngine.Object.FindAnyObjectByType<PcOriginalMapPreviewViewer>();
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!EditorApplication.isPlaying || active == null || active.CurrentMap == null ||
                !path.StartsWith(PcOriginalMapPreviewImporter.VariantReviewAssetRoot, StringComparison.Ordinal) ||
                !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("在 Play 中打开地图预览，选择独立比较目录的 catalog.json。");
            string resource = path.Substring("Assets/Resources/".Length);
            active.SelectReviewCatalog(resource.Substring(0, resource.Length - 5), active.CurrentMap.mapId);
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch mode required.");
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "-pcMapReview");
                if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Missing -pcMapReview config.json.");
                output = Path.Combine("Logs", "PcMapVariantReview", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(output);
                report = new Report { unity = Application.unityVersion, buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                    graphicsDevice = SystemInfo.graphicsDeviceName, quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
                    colorSpace = QualitySettings.activeColorSpace.ToString(),
                    mipLimit = QualitySettings.globalTextureMipmapLimit, fixedDpiFactor = QualitySettings.resolutionScalingFixedDPIFactor,
                    dynamicWidthScale = ScalableBufferManager.widthScaleFactor, dynamicHeightScale = ScalableBufferManager.heightScaleFactor };
                request = JsonUtility.FromJson<Request>(File.ReadAllText(args[index + 1]));
                if (request?.versions == null || request.versions.Length == 0 || request.views == null || request.views.Length == 0 ||
                    !PcOriginalMapContract.IsValidMapId(request.mapId) || request.width <= 0 || request.height <= 0 ||
                    request.width > SystemInfo.maxTextureSize || request.height > SystemInfo.maxTextureSize)
                    throw new ArgumentException("Invalid map review request or render target size.");
                report.mapId = request.mapId;
                foreach (var version in request.versions) report.variants.Add(InspectVersion(version));
                viewer = new GameObject("MapComparison").AddComponent<PcOriginalMapPreviewViewer>();
                target = new RenderTexture(request.width, request.height, 24, RenderTextureFormat.ARGB32);
                if (!target.Create()) throw new InvalidOperationException("Failed to create review render target.");
                OpenVersion();
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }

        private static VariantResult InspectVersion(Version version)
        {
            if (version == null || !PcOriginalMapContract.IsSafeResourcePath(version.catalog) ||
                !version.catalog.StartsWith(PcOriginalMapContract.VariantReviewRoot, StringComparison.Ordinal))
                throw new InvalidDataException("Invalid review catalog path.");
            var catalog = JsonUtility.FromJson<PcOriginalMapCatalog>(Resources.Load<TextAsset>(version.catalog).text);
            catalog.Validate();
            var map = catalog.FindByMapId(request.mapId)?.ResolveAppearance(version.appearance);
            if (map == null) throw new InvalidDataException("Missing requested map or appearance.");
            map.Validate();
            var result = new VariantResult { catalog = version.catalog, appearance = version.appearance, revision = map.revision,
                nativeSha256 = map.sourceNativeSha256, worldWidth = map.WorldWidth, worldHeight = map.WorldHeight };
            VerifyFile("Assets/Resources/" + map.tilesManifestResourcePath + ".json", map.tilesManifestSha256);
            VerifyFile("Assets/Resources/" + map.overviewResourcePath + map.overviewExtension, map.overviewSha256);
            var manifest = JsonUtility.FromJson<PcOriginalTileManifest>(Resources.Load<TextAsset>(map.tilesManifestResourcePath).text);
            manifest.Validate(map);
            foreach (var tile in manifest.tiles)
            {
                string asset = "Assets/Resources/" + tile.resourcePath + tile.fileExtension;
                VerifyFile(asset, tile.sha256);
                var importer = (TextureImporter)AssetImporter.GetAtPath(asset);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
                try
                {
                    if (texture == null || importer == null || texture.width != tile.width || texture.height != tile.height ||
                        texture.mipmapCount != 1 || texture.format != (map.hasAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24) ||
                        texture.filterMode != FilterMode.Bilinear || texture.wrapMode != TextureWrapMode.Clamp ||
                        importer.npotScale != TextureImporterNPOTScale.None || importer.textureCompression != TextureImporterCompression.Uncompressed)
                        throw new InvalidDataException($"Unexpected import for {asset}: {texture?.width}x{texture?.height}, {texture?.format}, {texture?.graphicsFormat}, {texture?.mipmapCount} mips.");
                    if (result.imports.Count == 0)
                    {
                        foreach (var platform in new[] { BuildTarget.StandaloneWindows64, BuildTarget.Android, BuildTarget.iOS, BuildTarget.WebGL })
                        {
                            var group = BuildPipeline.GetBuildTargetGroup(platform);
                            string key = platform == BuildTarget.iOS ? "iPhone" : group.ToString();
                            var settings = importer.GetPlatformTextureSettings(key);
                            result.platformSettings.Add(new PlatformImport { target = platform.ToString(), textureOverride = settings.overridden,
                                installedInEditor = BuildPipeline.IsBuildTargetSupported(group, platform),
                                effectiveMaxSize = settings.overridden ? settings.maxTextureSize : importer.maxTextureSize });
                        }
                    }
                    var rect = PcOriginalViewportPlanner.TileWorldRect(manifest, tile);
                    result.imports.Add(new ImportedTile { resource = tile.resourcePath, sha256 = tile.sha256, width = texture.width, height = texture.height,
                        xMin = rect.XMin, yMin = rect.YMin, worldWidth = rect.Width, worldHeight = rect.Height,
                        format = texture.format.ToString(), graphicsFormat = texture.graphicsFormat.ToString(), importCap = importer.maxTextureSize,
                        compression = importer.textureCompression.ToString(), npot = importer.npotScale.ToString(), mipCount = texture.mipmapCount,
                        mipBias = texture.mipMapBias, filter = texture.filterMode.ToString(), wrap = texture.wrapMode.ToString() });
                }
                finally
                {
                    if (texture != null) Resources.UnloadAsset(texture);
                }
            }
            return result;
        }

        private static void OpenVersion()
        {
            var version = request.versions[versionIndex];
            viewer.SelectReviewCatalog(version.catalog, request.mapId, version.appearance);
            viewer.PreviewCamera.targetTexture = target;
            viewer.PreviewCamera.aspect = (float)request.width / request.height;
            viewIndex = 0;
            BeginView();
        }

        private static void VerifyFile(string path, string expected)
        {
            using (var hash = SHA256.Create())
            using (var input = File.OpenRead(path))
                if (!string.Equals(BitConverter.ToString(hash.ComputeHash(input)).Replace("-", ""), expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 mismatch: " + path);
        }

        private static void BeginView()
        {
            var view = request.views[viewIndex];
            viewer.SetView(new Vector2(view.worldX, view.worldY), view.orthographicSize);
            deadline = EditorApplication.timeSinceStartup + 60;
        }

        private static void Tick()
        {
            try
            {
                viewer.SetView(viewer.Focus, viewer.Zoom);
                var cam = viewer.PreviewCamera;
                float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
                var bounds = new PcOriginalWorldRect(viewer.Focus.x - halfW, viewer.Focus.y - halfH, halfW * 2, halfH * 2);
                int ready = 0, visible = 0;
                foreach (var tile in viewer.CurrentTileManifest.tiles)
                {
                    var rect = PcOriginalViewportPlanner.TileWorldRect(viewer.CurrentTileManifest, tile);
                    if (!rect.Overlaps(bounds)) continue;
                    visible++;
                    var go = viewer.VisualRoot.transform.Find($"Tile_r{tile.row:00}_c{tile.column:00}");
                    if (go == null) continue;
                    var actual = go.GetComponent<SpriteRenderer>().bounds;
                    if (Mathf.Abs(actual.min.x - rect.XMin) > 0.00001f || Mathf.Abs(actual.max.x - rect.XMax) > 0.00001f ||
                        Mathf.Abs(actual.min.y - rect.YMin) > 0.00001f || Mathf.Abs(actual.max.y - rect.YMax) > 0.00001f)
                        throw new InvalidDataException("Rendered tile bounds differ from manifest: " + tile.resourcePath);
                    ready++;
                }
                if (visible == 0 || ready != visible)
                {
                    if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException($"Only {ready}/{visible} native sprites visible.");
                    return;
                }
                string file = $"v{versionIndex:D2}-view{viewIndex:D2}.png";
                cam.Render();
                var previous = RenderTexture.active;
                var readback = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                try
                {
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    readback.Apply();
                    File.WriteAllBytes(Path.Combine(output, file), readback.EncodeToPNG());
                }
                finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(readback); }
                report.variants[versionIndex].captures.Add(new Capture { file = file, zoom = cam.orthographicSize,
                    focusX = viewer.Focus.x, focusY = viewer.Focus.y, scaledWidth = cam.scaledPixelWidth,
                    scaledHeight = cam.scaledPixelHeight, nativeSpritesInView = ready });
                if (++viewIndex < request.views.Length) BeginView();
                else if (++versionIndex < request.versions.Length) OpenVersion();
                else Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            if (viewer != null) UnityEngine.Object.DestroyImmediate(viewer.gameObject);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (report != null)
            {
                report.status = error == null ? "CAPTURED_FOR_REVIEW" : "FAILED";
                report.error = error?.ToString();
                File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
                Debug.Log("[PcMapVariantReview] " + Path.GetFullPath(output));
            }
            if (error != null) Debug.LogError(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
