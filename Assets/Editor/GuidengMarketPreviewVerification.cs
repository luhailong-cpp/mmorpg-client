using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using MmorpgClient.World.CurrentMaps;
using MmorpgClient.World.Tianyong;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MmorpgClient.Editor.CurrentMaps
{
    /// <summary>
    /// Unity Editor / Batchmode verification for original map "桂灯集" (original:guideng-market)
    /// dual-festival (spring / midautumn) delivery-candidate preview.
    /// Verifies scene binding, SHA-256 integrity, exact TextureImporter &amp; Texture2D settings
    /// (including 1024x683 bottom row without NPOT scaling), async native tile loading,
    /// SpriteRenderer world bounds alignment, appearance switching with preserved camera state,
    /// and renders real Unity camera screenshots at overview, 1:1 native zoom, and closest zoom.
    /// </summary>
    public static class GuidengMarketPreviewVerification
    {
        public const string ScenePath = FestivalMapPreviewMenu.GuidengPreviewScenePath;
        public const string MapId = "original:guideng-market";
        private const int CaptureWidth = 1600;
        private const int CaptureHeight = 900;
        private const float BoundsTolerance = 0.0001f;

        private readonly struct ViewSpec
        {
            public readonly string Key;
            public readonly Vector2 Focus;
            public readonly float Zoom;
            public ViewSpec(string key, float worldX, float worldY, float zoom)
            {
                Key = key;
                Focus = new Vector2(worldX, worldY);
                Zoom = zoom;
            }
        }

        // World extent is 4.0 x (2731 / 1024 = 2.6669921875). Top-left pixel (px, py) maps to (px/1024, (2731-py)/1024).
        private static readonly ViewSpec[] Views =
        {
            // 1. Full map fit (aspect 16:9 -> vertical fit half-height = 2.6669922 / 2)
            new ViewSpec("01-overview", 2.0f, 2731f / 2048f, 2731f / 2048f),
            // 2. Normal 1:1 native pixel zoom (900 / 2048 = 0.439453125) on eastern shops + main road
            new ViewSpec("02-normal-1to1-east-shops", 2750f / 1024f, (2731f - 1365f) / 1024f, CaptureHeight / 2048f),
            // 3. Closest zoom right on the 4-tile cross seam (x=2048, y=2048) where 1024x1024 meets 1024x683
            new ViewSpec("03-closest-row3-seam", 2048f / 1024f, (2731f - 2048f) / 1024f, 0.18f),
            // 4. Closest zoom on eastern teahouse & pastry shop (spans x=3072 vertical seam & y=1024 horizontal seam)
            new ViewSpec("04-closest-festival-shops", 3020f / 1024f, (2731f - 1400f) / 1024f, 0.28f),
            // 5. Closest zoom on southwest stone bridge, craft stall & road (spans x=1024 & y=2048 683px seam)
            new ViewSpec("05-closest-sw-bridge-road", 920f / 1024f, (2731f - 2100f) / 1024f, 0.30f),
            // 6. Closest zoom on northeast gate & courtyard branch road (spans x=3072 seam)
            new ViewSpec("06-closest-ne-gate-road", 3220f / 1024f, (2731f - 540f) / 1024f, 0.30f),
            // 7. Closest zoom on northwest wooden bridge & approaches (protected rect [290,212,875,570])
            new ViewSpec("07-closest-nw-wood-bridge", 582f / 1024f, (2731f - 391f) / 1024f, 0.28f),
            // 8. Closest zoom on teahouse eaves, steps & shopfront transition (protected steps [2550,1064,2760,1180])
            new ViewSpec("08-closest-teahouse-steps", 2750f / 1024f, (2731f - 1030f) / 1024f, 0.24f),
        };

        [Serializable]
        private sealed class PlatformSettingInfo
        {
            public string platform;
            public bool overridden;
            public int effectiveMaxSize;
        }

        [Serializable]
        private sealed class TileImportInfo
        {
            public string resourcePath;
            public int row;
            public int column;
            public int expectedWidth;
            public int expectedHeight;
            public int actualWidth;
            public int actualHeight;
            public string format;
            public string graphicsFormat;
            public int maxTextureSize;
            public string npotScale;
            public string compression;
            public int mipmapCount;
            public string wrapMode;
            public string filterMode;
            public bool isReadable;
            public string sha256;
            public float worldXMin;
            public float worldYMin;
            public float worldWidth;
            public float worldHeight;
        }

        [Serializable]
        private sealed class CaptureInfo
        {
            public string key;
            public string file;
            public string sha256;
            public float focusX;
            public float focusY;
            public float zoom;
            public int loadedNativeTiles;
            public int activeFallbacks;
        }

        [Serializable]
        private sealed class AppearanceResult
        {
            public string appearance;
            public string displayName;
            public string revision;
            public string resourceRoot;
            public int overviewWidth;
            public int overviewHeight;
            public string overviewFormat;
            public int overviewMipmapCount;
            public bool spriteBoundsExact;
            public List<TileImportInfo> tiles = new List<TileImportInfo>();
            public List<PlatformSettingInfo> platformOverrides = new List<PlatformSettingInfo>();
            public List<CaptureInfo> captures = new List<CaptureInfo>();
        }

        [Serializable]
        private sealed class Report
        {
            public string result = "FAIL";
            public string scope = "Unity Editor batchmode visual & import verification for original:guideng-market/festivals-20261127-v001 (formal_art_delivery static preview); no navigation/collision/online acceptance";
            public string startedUtc;
            public string finishedUtc;
            public string unityVersion;
            public string buildTarget;
            public string graphicsDevice;
            public string colorSpace;
            public string scenePath;
            public string mapId;
            public bool standaloneSceneVerified;
            public bool legacyCandidatePreserved;
            public bool appearanceSwitchPreservedCamera;
            public bool eastTilesSwitchedTextures;
            public bool roundTripSwitchVerified;
            public int festivalShopsPixelDiffCount;
            public int festivalShopsTotalPixels;
            public string captureDirectory;
            public string error;
            public List<AppearanceResult> appearances = new List<AppearanceResult>();
        }

        private static Report _report;
        private static string _captureDir;
        private static string _evidenceDir;
        private static PcOriginalMapPreviewViewer _viewer;
        private static RenderTexture _renderTarget;
        private static readonly string[] Sequence = { "spring", "midautumn", "spring" };
        private static int _seqIndex;
        private static int _viewIndex;
        private static double _deadline;
        private static int _lastReadyCount = -1;
        private static readonly Dictionary<string, Texture2D> _springEastTextures = new Dictionary<string, Texture2D>();
        private static Color32[] _springFestivalShopPixels;
        private static Vector2 _preSwitchFocus;
        private static float _preSwitchZoom;
        private static bool _awaitingSwitchCheck;

        [MenuItem("MMORPG/Maps/验收桂灯集双节预览")]
        public static void RunMenuVerification()
        {
            StartVerification(exitOnComplete: false);
        }

        public static void RunBatchVerification()
        {
            StartVerification(exitOnComplete: true);
        }

        private static bool _exitOnComplete;

        private static void StartVerification(bool exitOnComplete)
        {
            _exitOnComplete = exitOnComplete;
            _springEastTextures.Clear();
            _springFestivalShopPixels = null;
            _awaitingSwitchCheck = false;
            _seqIndex = 0;
            _viewIndex = 0;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            _captureDir = Path.Combine(projectRoot, ".codex-artifacts", "guideng-preview-verification-20261128");
            _evidenceDir = Path.Combine(projectRoot, "Docs", "VerificationEvidence", "guideng-market-festivals-20261127-v001");
            Directory.CreateDirectory(_captureDir);
            Directory.CreateDirectory(_evidenceDir);

            _report = new Report
            {
                startedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                graphicsDevice = SystemInfo.graphicsDeviceName,
                colorSpace = QualitySettings.activeColorSpace.ToString(),
                scenePath = ScenePath,
                mapId = MapId,
                captureDirectory = _captureDir
            };

            try
            {
                if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                    throw new InvalidOperationException("Graphics device with RenderTexture support required; do not pass -nographics.");

                // 1. Open saved scene & verify component binding.
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                if (!scene.IsValid())
                    throw new InvalidOperationException("Failed to open scene: " + ScenePath);
                _viewer = UnityEngine.Object.FindAnyObjectByType<PcOriginalMapPreviewViewer>();
                if (_viewer == null || !_viewer.IsStandalone)
                    throw new InvalidOperationException("Scene is missing standalone PcOriginalMapPreviewViewer.");
                _report.standaloneSceneVerified = true;

                // 2. Inspect catalog, manifests, files, and Unity TextureImporter / Texture2D settings for both festivals.
                var catalogAsset = Resources.Load<TextAsset>(PcOriginalMapContract.OriginalPreviewCatalogResourcePath);
                if (catalogAsset == null)
                    throw new InvalidOperationException("Missing catalog TextAsset: " + PcOriginalMapContract.OriginalPreviewCatalogResourcePath);
                var catalog = JsonUtility.FromJson<PcOriginalMapCatalog>(catalogAsset.text);
                if (catalog == null || !catalog.IsOriginalCatalog)
                    throw new InvalidOperationException("Catalog did not parse as CLIENT_ORIGINAL_MAP_PREVIEW_CATALOG.");
                catalog.Validate();

                var mapEntry = catalog.FindByMapId(MapId);
                if (mapEntry == null)
                    throw new InvalidOperationException("Missing map entry: " + MapId);
                if (mapEntry.artStage != PcOriginalMapContract.ArtStageFormalDelivery ||
                    !mapEntry.resourceRoot.Contains("/festivals-20261127-v001/"))
                    throw new InvalidOperationException(
                        $"Expected formal_art_delivery at festivals-20261127-v001, got stage={mapEntry.artStage} root={mapEntry.resourceRoot}");
                string legacySpringTiles = Path.Combine(projectRoot, "Assets", "Resources", "World",
                    "OriginalMapPreview20261124", "guideng-market", "delivery-candidate-v001", "spring", "tiles.json");
                string legacyMidTiles = Path.Combine(projectRoot, "Assets", "Resources", "World",
                    "OriginalMapPreview20261124", "guideng-market", "delivery-candidate-v001", "midautumn", "tiles.json");
                if (!File.Exists(legacySpringTiles) || !File.Exists(legacyMidTiles))
                    throw new InvalidOperationException("Preserved legacy delivery-candidate-v001 directory is missing.");
                _report.legacyCandidatePreserved = true;

                foreach (string appId in new[] { "spring", "midautumn" })
                {
                    var resolved = mapEntry.ResolveAppearance(appId);
                    if (resolved == null)
                        throw new InvalidOperationException("Missing appearance: " + appId);
                    _report.appearances.Add(InspectImports(resolved, appId));
                }

                // 3. Drive real viewer initialization, async tile streaming, rendering, and appearance switching.
                _renderTarget = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                if (!_renderTarget.Create())
                    throw new InvalidOperationException("Failed to create 1600x900 RenderTexture.");

                _viewer.SetControlsVisible(false);
                _viewer.InitializeFromConfiguredCatalog("spring");
                AttachRenderTarget();
                BeginCurrentStep();
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
            }
            catch (Exception error)
            {
                Finish(error);
            }
        }

        private static AppearanceResult InspectImports(PcOriginalMapEntry map, string appId)
        {
            map.Validate(allowOriginalId: true);
            VerifyFileSha256("Assets/Resources/" + map.tilesManifestResourcePath + ".json", map.tilesManifestSha256);
            VerifyFileSha256("Assets/Resources/" + map.overviewResourcePath + map.overviewExtension, map.overviewSha256);

            var manifestAsset = Resources.Load<TextAsset>(map.tilesManifestResourcePath);
            var manifest = JsonUtility.FromJson<PcOriginalTileManifest>(manifestAsset.text);
            manifest.Validate(map, allowOriginalId: true);

            string ovAssetPath = "Assets/Resources/" + map.overviewResourcePath + map.overviewExtension;
            var ovImporter = (TextureImporter)AssetImporter.GetAtPath(ovAssetPath);
            var ovTex = AssetDatabase.LoadAssetAtPath<Texture2D>(ovAssetPath);
            if (ovImporter == null || ovTex == null)
                throw new InvalidOperationException("Failed to load overview asset: " + ovAssetPath);
            if (ovTex.width != map.overviewWidth || ovTex.height != map.overviewHeight ||
                ovTex.mipmapCount != 1 || ovTex.format != TextureFormat.RGB24 ||
                ovImporter.maxTextureSize != 2048 || ovImporter.npotScale != TextureImporterNPOTScale.None ||
                ovImporter.textureCompression != TextureImporterCompression.Uncompressed)
                throw new InvalidOperationException(
                    $"Overview import mismatch for {ovAssetPath}: {ovTex.width}x{ovTex.height} fmt={ovTex.format} mip={ovTex.mipmapCount} max={ovImporter.maxTextureSize} npot={ovImporter.npotScale} comp={ovImporter.textureCompression}");

            var result = new AppearanceResult
            {
                appearance = appId,
                displayName = map.FindAppearance(appId)?.DisplayLabel ?? appId,
                revision = map.revision,
                resourceRoot = map.resourceRoot,
                overviewWidth = ovTex.width,
                overviewHeight = ovTex.height,
                overviewFormat = ovTex.format.ToString(),
                overviewMipmapCount = ovTex.mipmapCount
            };
            Resources.UnloadAsset(ovTex);

            foreach (var tile in manifest.tiles)
            {
                string assetPath = "Assets/Resources/" + tile.resourcePath + tile.fileExtension;
                VerifyFileSha256(assetPath, tile.sha256);
                var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                try
                {
                    if (importer == null || tex == null)
                        throw new InvalidOperationException("Missing tile asset: " + assetPath);
                    int expHeight = tile.row == 3 ? 683 : 1024;
                    if (tile.width != 1024 || tile.height != expHeight ||
                        tex.width != tile.width || tex.height != tile.height)
                        throw new InvalidOperationException(
                            $"Tile dimension mismatch {assetPath}: actual={tex.width}x{tex.height} expected=1024x{expHeight}");
                    if (tex.mipmapCount != 1 || tex.format != TextureFormat.RGB24 ||
                        tex.wrapMode != TextureWrapMode.Clamp || tex.filterMode != FilterMode.Bilinear ||
                        tex.isReadable || importer.maxTextureSize != 1024 ||
                        importer.npotScale != TextureImporterNPOTScale.None ||
                        importer.textureCompression != TextureImporterCompression.Uncompressed)
                        throw new InvalidOperationException(
                            $"Tile import setting mismatch {assetPath}: fmt={tex.format} mip={tex.mipmapCount} wrap={tex.wrapMode} filter={tex.filterMode} readable={tex.isReadable} max={importer.maxTextureSize} npot={importer.npotScale} comp={importer.textureCompression}");

                    if (result.platformOverrides.Count == 0)
                    {
                        foreach (string platform in new[] { "Standalone", "Android", "iPhone", "WebGL" })
                        {
                            var ps = importer.GetPlatformTextureSettings(platform);
                            if (ps.overridden)
                                throw new InvalidOperationException($"Unexpected platform override {platform} on {assetPath}");
                            result.platformOverrides.Add(new PlatformSettingInfo
                            {
                                platform = platform,
                                overridden = ps.overridden,
                                effectiveMaxSize = ps.overridden ? ps.maxTextureSize : importer.maxTextureSize
                            });
                        }
                    }

                    var rect = PcOriginalViewportPlanner.TileWorldRect(manifest, tile);
                    result.tiles.Add(new TileImportInfo
                    {
                        resourcePath = tile.resourcePath,
                        row = tile.row,
                        column = tile.column,
                        expectedWidth = tile.width,
                        expectedHeight = tile.height,
                        actualWidth = tex.width,
                        actualHeight = tex.height,
                        format = tex.format.ToString(),
                        graphicsFormat = tex.graphicsFormat.ToString(),
                        maxTextureSize = importer.maxTextureSize,
                        npotScale = importer.npotScale.ToString(),
                        compression = importer.textureCompression.ToString(),
                        mipmapCount = tex.mipmapCount,
                        wrapMode = tex.wrapMode.ToString(),
                        filterMode = tex.filterMode.ToString(),
                        isReadable = tex.isReadable,
                        sha256 = tile.sha256,
                        worldXMin = rect.XMin,
                        worldYMin = rect.YMin,
                        worldWidth = rect.Width,
                        worldHeight = rect.Height
                    });
                }
                finally
                {
                    if (tex != null) Resources.UnloadAsset(tex);
                }
            }
            return result;
        }

        private static void AttachRenderTarget()
        {
            var cam = _viewer.PreviewCamera;
            if (cam == null) throw new InvalidOperationException("PreviewCamera is null.");
            cam.targetTexture = _renderTarget;
            cam.aspect = CaptureWidth / (float)CaptureHeight;
        }

        private static void BeginCurrentStep()
        {
            if (_seqIndex < 2)
            {
                var spec = Views[_viewIndex];
                _viewer.SetView(spec.Focus, spec.Zoom);
            }
            else
            {
                // Final round-trip return to spring at overview to verify all 12 native tiles reload cleanly.
                _viewer.SetView(Views[0].Focus, Views[0].Zoom);
            }
            // Delay deadline start until the first Tick so startup EditorApplication.delayCall hooks don't eat the timeout.
            _deadline = 0.0;
            _lastReadyCount = -1;
        }

        private static void Tick()
        {
            try
            {
                if (_viewer == null || _viewer.PreviewCamera == null || _viewer.CurrentTileManifest == null)
                    throw new InvalidOperationException("Viewer state lost during verification.");

                if (_deadline <= 0.0)
                    _deadline = EditorApplication.timeSinceStartup + 60.0;

                // Keep streaming advancing each editor tick.
                _viewer.SetView(_viewer.Focus, _viewer.Zoom);
                AttachRenderTarget();

                var cam = _viewer.PreviewCamera;
                float halfH = cam.orthographicSize;
                float halfW = halfH * cam.aspect;
                var viewBounds = new PcOriginalWorldRect(_viewer.Focus.x - halfW, _viewer.Focus.y - halfH, halfW * 2f, halfH * 2f);

                int overlapping = 0;
                int ready = 0;
                foreach (var tile in _viewer.CurrentTileManifest.tiles)
                {
                    var expectedRect = PcOriginalViewportPlanner.TileWorldRect(_viewer.CurrentTileManifest, tile);
                    if (!expectedRect.Overlaps(viewBounds)) continue;
                    overlapping++;
                    var go = _viewer.VisualRoot.transform.Find($"Tile_r{tile.row:00}_c{tile.column:00}");
                    if (go == null) continue;
                    var sr = go.GetComponent<SpriteRenderer>();
                    if (sr == null || sr.sprite == null || sr.sprite.texture == null) continue;
                    var b = sr.bounds;
                    if (Mathf.Abs(b.min.x - expectedRect.XMin) > BoundsTolerance ||
                        Mathf.Abs(b.max.x - expectedRect.XMax) > BoundsTolerance ||
                        Mathf.Abs(b.min.y - expectedRect.YMin) > BoundsTolerance ||
                        Mathf.Abs(b.max.y - expectedRect.YMax) > BoundsTolerance)
                        throw new InvalidOperationException(
                            $"SpriteRenderer bounds mismatch on r{tile.row:00}_c{tile.column:00}: " +
                            $"actual=[{b.min.x:F5},{b.min.y:F5}]-[{b.max.x:F5},{b.max.y:F5}] " +
                            $"expected=[{expectedRect.XMin:F5},{expectedRect.YMin:F5}]-[{expectedRect.XMax:F5},{expectedRect.YMax:F5}]");
                    var fb = _viewer.VisualRoot.transform.Find($"OverviewBase/Fallback_r{tile.row:00}_c{tile.column:00}");
                    if (fb != null && fb.gameObject.activeSelf)
                        throw new InvalidOperationException($"Fallback quad remained active after native tile loaded: r{tile.row:00}_c{tile.column:00}");
                    ready++;
                }

                if (overlapping == 0 || ready != overlapping)
                {
                    if (ready > _lastReadyCount)
                    {
                        _lastReadyCount = ready;
                        _deadline = EditorApplication.timeSinceStartup + 30.0;
                    }
                    if (EditorApplication.timeSinceStartup > _deadline)
                        throw new TimeoutException($"Timed out waiting for native tiles ({ready}/{overlapping}) on seq={_seqIndex} view={_viewIndex}.");
                    return;
                }

                if (_awaitingSwitchCheck)
                {
                    _awaitingSwitchCheck = false;
                    // Verify every east tile switched to a new Texture2D instance.
                    int switchedEast = 0;
                    foreach (var kv in _springEastTextures)
                    {
                        var go = _viewer.VisualRoot.transform.Find("Tile_" + kv.Key);
                        if (go != null)
                        {
                            var tex = go.GetComponent<SpriteRenderer>()?.sprite?.texture;
                            if (tex != null && tex != kv.Value) switchedEast++;
                        }
                    }
                    if (switchedEast == 0)
                        throw new InvalidOperationException("East tiles did not switch Texture2D instances on spring->midautumn switch.");
                    _report.eastTilesSwitchedTextures = true;
                }

                if (_seqIndex == 2)
                {
                    // Final round-trip return to spring complete with all 12 native tiles verified.
                    if (_viewer.LoadedNativeTileCount != 12 || _viewer.ActiveOverviewFallbackCount != 0)
                        throw new InvalidOperationException($"Round-trip spring expected 12 native tiles, got {_viewer.LoadedNativeTileCount}.");
                    _report.roundTripSwitchVerified = true;
                    Finish(null);
                    return;
                }

                string appId = Sequence[_seqIndex];
                var appResult = _report.appearances[_seqIndex];
                if (_viewIndex == 0)
                {
                    if (_viewer.LoadedNativeTileCount != 12 || _viewer.ActiveOverviewFallbackCount != 0)
                        throw new InvalidOperationException($"{appId} overview expected 12/12 native tiles, got {_viewer.LoadedNativeTileCount}.");
                    appResult.spriteBoundsExact = true;
                    if (appId == "spring")
                    {
                        foreach (string stem in new[] { "r01_c03", "r01_c04", "r02_c03", "r02_c04", "r03_c03", "r03_c04" })
                        {
                            var go = _viewer.VisualRoot.transform.Find("Tile_" + stem);
                            var tex = go != null ? go.GetComponent<SpriteRenderer>()?.sprite?.texture : null;
                            if (tex != null) _springEastTextures[stem] = tex;
                        }
                    }
                }

                var spec = Views[_viewIndex];
                string fileName = $"{appId}-{spec.Key}.png";
                string fullPath = Path.Combine(_captureDir, fileName);
                Color32[] pixels = CaptureCamera(cam, fullPath, out string pngSha);

                if (spec.Key == "04-closest-festival-shops")
                {
                    if (appId == "spring")
                    {
                        _springFestivalShopPixels = pixels;
                    }
                    else if (_springFestivalShopPixels != null)
                    {
                        int diffs = 0;
                        for (int i = 0; i < pixels.Length; i++)
                        {
                            int d = Math.Abs(pixels[i].r - _springFestivalShopPixels[i].r) +
                                    Math.Abs(pixels[i].g - _springFestivalShopPixels[i].g) +
                                    Math.Abs(pixels[i].b - _springFestivalShopPixels[i].b);
                            if (d > 12) diffs++;
                        }
                        _report.festivalShopsPixelDiffCount = diffs;
                        _report.festivalShopsTotalPixels = pixels.Length;
                        if (diffs < 1000)
                            throw new InvalidOperationException($"Spring vs Mid-Autumn festival shop view diff too small ({diffs} pixels).");
                    }
                }

                appResult.captures.Add(new CaptureInfo
                {
                    key = spec.Key,
                    file = fileName,
                    sha256 = pngSha,
                    focusX = _viewer.Focus.x,
                    focusY = _viewer.Focus.y,
                    zoom = _viewer.Zoom,
                    loadedNativeTiles = _viewer.LoadedNativeTileCount,
                    activeFallbacks = _viewer.ActiveOverviewFallbackCount
                });

                if (++_viewIndex < Views.Length)
                {
                    BeginCurrentStep();
                    return;
                }

                // Finished all views for current appearance; advance sequence.
                _seqIndex++;
                _viewIndex = 0;
                string nextApp = Sequence[_seqIndex];
                if (_seqIndex == 1)
                {
                    // Switch from spring to midautumn at view 04 (festival shops) first to verify camera preservation!
                    _viewer.SetView(Views[3].Focus, Views[3].Zoom);
                    _preSwitchFocus = _viewer.Focus;
                    _preSwitchZoom = _viewer.Zoom;
                    if (!_viewer.SelectAppearance(nextApp))
                        throw new InvalidOperationException("Failed to switch appearance to " + nextApp);
                    if (Vector2.Distance(_viewer.Focus, _preSwitchFocus) > 1e-4f ||
                        Mathf.Abs(_viewer.Zoom - _preSwitchZoom) > 1e-4f)
                        throw new InvalidOperationException("Appearance switch did not preserve camera focus/zoom.");
                    _report.appearanceSwitchPreservedCamera = true;
                    _awaitingSwitchCheck = true;
                    AttachRenderTarget();
                    BeginCurrentStep();
                }
                else
                {
                    if (!_viewer.SelectAppearance(nextApp))
                        throw new InvalidOperationException("Failed round-trip switch to " + nextApp);
                    AttachRenderTarget();
                    BeginCurrentStep();
                }
            }
            catch (Exception error)
            {
                Finish(error);
            }
        }

        private static Color32[] CaptureCamera(Camera cam, string outPath, out string sha256)
        {
            cam.Render();
            var prev = RenderTexture.active;
            var readback = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false, false);
            try
            {
                RenderTexture.active = _renderTarget;
                readback.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0, false);
                readback.Apply(false, false);
                Color32[] data = readback.GetPixels32();
                bool varied = false;
                Color32 first = data[0];
                for (int i = 1; i < data.Length; i += 97)
                {
                    if (Math.Abs(data[i].r - first.r) + Math.Abs(data[i].g - first.g) + Math.Abs(data[i].b - first.b) > 20)
                    {
                        varied = true;
                        break;
                    }
                }
                if (!varied) throw new InvalidOperationException("Captured image is uniform/blank: " + outPath);
                byte[] png = readback.EncodeToPNG();
                File.WriteAllBytes(outPath, png);
                using (var hash = SHA256.Create())
                    sha256 = BitConverter.ToString(hash.ComputeHash(png)).Replace("-", "").ToLowerInvariant();
                return data;
            }
            finally
            {
                RenderTexture.active = prev;
                UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        private static void VerifyFileSha256(string assetPath, string expected)
        {
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(assetPath))
            {
                string actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"SHA-256 mismatch on {assetPath}: actual={actual} expected={expected}");
            }
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            if (_viewer != null && _viewer.PreviewCamera != null)
                _viewer.PreviewCamera.targetTexture = null;
            if (_renderTarget != null)
            {
                _renderTarget.Release();
                UnityEngine.Object.DestroyImmediate(_renderTarget);
                _renderTarget = null;
            }
            if (_report != null)
            {
                _report.result = error == null ? "PASS" : "FAIL";
                _report.finishedUtc = DateTime.UtcNow.ToString("O");
                _report.error = error?.ToString();
                string json = JsonUtility.ToJson(_report, true) + "\n";
                File.WriteAllText(Path.Combine(_captureDir, "unity-verification.json"), json);
                File.WriteAllText(Path.Combine(_evidenceDir, "unity-verification.json"), json);
                Debug.Log($"[GuidengMarketPreviewVerification] {_report.result} captures={_captureDir} diffPixels={_report.festivalShopsPixelDiffCount}");
            }
            if (error != null) Debug.LogError("[GuidengMarketPreviewVerification] " + error);
            if (_exitOnComplete) EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
