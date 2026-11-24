using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MmorpgClient.World.CurrentMaps;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = false
    };

    private static readonly List<string> Passed = new();

    private static void Check(string name, Action action)
    {
        action();
        Passed.Add(name);
    }

    private static void Require(bool condition, string message = "Assertion failed.")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected rejection: " + message);
    }

    public static int Main(string[] args)
    {
        try
        {
            string repoRoot = args.Length > 0
                ? Path.GetFullPath(args[0])
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

            string catalogPath = Path.Combine(repoRoot, "Assets", "Resources", "World", "PcOriginalPreview20261123", "catalog.json");
            Require(File.Exists(catalogPath), "Missing catalog.json at " + catalogPath);

            var catalog = JsonSerializer.Deserialize<PcOriginalMapCatalog>(File.ReadAllText(catalogPath), JsonOptions);
            Require(catalog != null, "Deserialized catalog is null.");

            Check("Committed catalog validates with exact 36 ready / 4 gaps / 2 HOLD", () =>
            {
                catalog.Validate();
                Require(catalog.readyCount == 36 && catalog.maps.Length == 36, "Expected 36 ready maps.");
                Require(catalog.unavailableCount == 4 && catalog.unavailableMaps.Length == 4, "Expected 4 unavailable maps.");
                Require(catalog.specialHoldCount == 2 && catalog.specialHoldCandidates.Length == 2, "Expected 2 special HOLD candidates.");
                Require(catalog.staticBackOnly && !catalog.completeGameplaySceneClaimed && !catalog.productionTravelEnabled,
                    "Catalog must remain staticBackOnly and non-online.");
            });

            Check("Expected 4 gap IDs are excluded from selectable maps", () =>
            {
                foreach (string gapId in new[] { "29005", "11005", "41049", "50024" })
                {
                    Require(catalog.FindByMapId(gapId) == null, $"Gap {gapId} must not be selectable.");
                }
            });

            Check("Map 15000 selects ordinary 1920x2298 RGBA while 8000x8000 stays HOLD", () =>
            {
                var m15000 = catalog.FindByMapId("15000");
                Require(m15000 != null, "15000 ordinary ready entry missing.");
                Require(m15000.nativeWidth == 1920 && m15000.nativeHeight == 2298 && m15000.hasAlpha,
                    "15000 ready entry must be ordinary 1920x2298 RGBA.");
                bool foundHold = false;
                foreach (var c in catalog.specialHoldCandidates)
                {
                    if (c.mapId == "15000" && c.branch == "gbits_jdhj" && c.nativeWidth == 8000 &&
                        c.nativeHeight == 8000 && c.sourceStatus == "source_valid_visual_hold" && !c.integrated)
                        foundHold = true;
                }
                Require(foundHold, "15000 8000x8000 HOLD candidate missing.");
            });

            Check("All 36 committed tile manifests (including multi-appearance variants) validate with exact pixel coverage and <=1024 limits", () =>
            {
                int totalTiles = 0;
                int totalVariants = 0;
                foreach (var entry in catalog.maps)
                {
                    var variants = new List<PcOriginalMapEntry>();
                    if (entry.HasAppearances)
                    {
                        foreach (var app in entry.appearances)
                        {
                            var resolved = entry.ResolveAppearance(app.id);
                            Require(resolved != null, $"Failed to resolve {entry.mapId}/{app.id}");
                            resolved.Validate();
                            variants.Add(resolved);
                        }
                    }
                    else
                    {
                        variants.Add(entry);
                    }

                    foreach (var variant in variants)
                    {
                        string tilesPath = Path.Combine(repoRoot, "Assets", "Resources",
                            variant.tilesManifestResourcePath.Replace('/', Path.DirectorySeparatorChar) + ".json");
                        Require(File.Exists(tilesPath), "Missing tiles.json: " + tilesPath);
                        var manifest = JsonSerializer.Deserialize<PcOriginalTileManifest>(File.ReadAllText(tilesPath), JsonOptions);
                        Require(manifest != null, "Null tiles manifest for " + variant.mapId);
                        manifest.Validate(variant);
                        totalTiles += manifest.tiles.Length;
                        totalVariants++;
                    }
                }
                Require(totalVariants == 54, $"Expected 54 total appearance variants across 36 maps, got {totalVariants}.");
                Require(totalTiles == 1395, $"Expected 1395 total tiles across all variants, got {totalTiles}.");
            });

            Check("Map 08001 keeps original 3200x2400 default while exposing 6400x4800 4x HD dual-festival candidates on identical world bounds", () =>
            {
                var m08001 = catalog.FindByMapId("08001");
                Require(m08001 != null && m08001.numericMapId == 8001, "08001 missing.");
                Require(m08001.defaultAppearance == "normal", "08001 defaultAppearance must remain 'normal' (original) before acceptance.");
                Require(m08001.artStage == PcOriginalMapContract.ArtStagePcOriginal && m08001.isTemporaryOriginal,
                    "08001 top-level active stage must remain pc_original_static_back.");
                Require(m08001.resourceRoot == "World/PcOriginalPreview20261123/08001/pc-original-20261123/",
                    "08001 default must point to untouched pc-original-20261123.");

                var orig = m08001.ResolveAppearance("normal");
                var springHd = m08001.ResolveAppearance("spring");
                var midHd = m08001.ResolveAppearance("midautumn");
                Require(orig != null && springHd != null && midHd != null, "08001 normal/spring/midautumn resolution failed.");
                Require(Math.Abs(orig.WorldWidth - 3200f / 1024f) < 1e-6f && Math.Abs(orig.WorldHeight - 2400f / 1024f) < 1e-6f,
                    "08001 original world bounds mismatch.");
                Require(Math.Abs(springHd.WorldWidth - orig.WorldWidth) < 1e-6f && Math.Abs(springHd.WorldHeight - orig.WorldHeight) < 1e-6f,
                    "08001 HD spring must keep identical world bounds to original.");
                Require(Math.Abs(midHd.WorldWidth - orig.WorldWidth) < 1e-6f && Math.Abs(midHd.WorldHeight - orig.WorldHeight) < 1e-6f,
                    "08001 HD midautumn must keep identical world bounds to original.");
                Require(springHd.nativeWidth == 6400 && springHd.nativeHeight == 4800 && springHd.hasAlpha && springHd.tileCount == 64,
                    "08001 HD spring metadata mismatch.");
                Require(midHd.nativeWidth == 6400 && midHd.nativeHeight == 4800 && midHd.hasAlpha && midHd.tileCount == 64,
                    "08001 HD midautumn metadata mismatch.");

                foreach (var hd in new[] { springHd, midHd })
                {
                    string tPath = Path.Combine(repoRoot, "Assets", "Resources",
                        hd.tilesManifestResourcePath.Replace('/', Path.DirectorySeparatorChar) + ".json");
                    var tm = JsonSerializer.Deserialize<PcOriginalTileManifest>(File.ReadAllText(tPath), JsonOptions);
                    tm.Validate(hd);
                    Require(tm.schemaVersion == 2 && tm.tiles.Length == 64, "08001 HD tiles.json must be schema 2 with 64 tiles.");
                    var first = PcOriginalViewportPlanner.TileWorldRect(tm, tm.tiles[0]);
                    var last = PcOriginalViewportPlanner.TileWorldRect(tm, tm.tiles[tm.tiles.Length - 1]);
                    Require(Math.Abs(first.XMin) < 1e-6f && Math.Abs(first.YMax - orig.WorldHeight) < 1e-5f,
                        "08001 HD top-left tile world alignment mismatch.");
                    Require(Math.Abs(last.XMax - orig.WorldWidth) < 1e-5f && Math.Abs(last.YMin) < 1e-6f,
                        "08001 HD bottom-right tile world alignment mismatch.");
                    Require(Math.Abs(first.Width - 400f / 1024f) < 1e-6f && Math.Abs(first.Height - 300f / 1024f) < 1e-6f,
                        "08001 HD 800x600 tile must span exact 400x300 original world pixels.");
                }
            });

            Check("All newly adopted completed Q-Dao maps preserve original provenance and valid appearances", () =>
            {
                var expectedDual = new (string id, int w, int h, bool alpha)[]
                {
                    ("03000", 4400, 4400, false),
                    ("07001", 6000, 4970, false),
                    ("16001", 1920, 1286, false),
                    ("19003", 1920, 1080, false),
                    ("23000", 4000, 5000, false),
                    ("33009", 4800, 3600, true),
                    ("41081", 4000, 2600, false),
                };
                foreach (var (id, w, h, alpha) in expectedDual)
                {
                    var m = catalog.FindByMapId(id);
                    Require(m != null && m.artStage == PcOriginalMapContract.ArtStageQdaoRedrawn && !m.isTemporaryOriginal, $"{id}: expected qdao stage.");
                    Require(m.nativeWidth == w && m.nativeHeight == h && m.hasAlpha == alpha, $"{id}: dimension/alpha mismatch.");
                    Require(m.defaultAppearance == "spring" && m.FindAppearance("spring") != null &&
                            m.FindAppearance("midautumn") != null && m.FindAppearance("normal") != null,
                        $"{id}: expected spring/midautumn/normal appearances.");
                }
                var m15000 = catalog.FindByMapId("15000");
                Require(m15000 != null && m15000.artStage == PcOriginalMapContract.ArtStageQdaoRedrawn &&
                        m15000.defaultAppearance == "spring" && m15000.FindAppearance("spring") != null &&
                        m15000.FindAppearance("normal") != null && m15000.FindAppearance("midautumn") == null,
                    "15000 must expose completed spring + preserved normal without unfinished midautumn.");
            });

            Check("Map 16001 3840x2572 25-tile HD worldGrid preserves 1.875x1.255859375 world bounds without changing global 1024 PPU", () =>
            {
                var m16001 = catalog.FindByMapId("16001");
                Require(m16001 != null, "16001 missing.");
                Require(m16001.ResolveAppearance("spring")?.sourceNativeSha256 == "3eefecb95acf3e4f027ef36a48af94844586e18f7c3478207a450b440c81e2b3",
                    "16001 1920x1286 spring baseline SHA mismatch.");
                Require(m16001.ResolveAppearance("midautumn")?.sourceNativeSha256 == "641b2f22570f2668ca760796228a96678c6d6b0b6b0d8f82629fc61ba48d2ef6",
                    "16001 1920x1286 midautumn baseline SHA mismatch.");
                Require(Math.Abs(m16001.WorldWidth - 1.875f) < 1e-6f && Math.Abs(m16001.WorldHeight - 1.255859375f) < 1e-6f,
                    "16001 world bounds mismatch.");

                var hdGrid = new PcOriginalWorldGrid { widthPixels = 1920, heightPixels = 1286, tileWidthPixels = 400, tileHeightPixels = 300 };
                var hdManifest = new PcOriginalTileManifest
                {
                    schemaVersion = 2,
                    mapId = "16001",
                    numericMapId = 16001,
                    revision = "qdao-spring-4x-proposed",
                    artStage = PcOriginalMapContract.ArtStageQdaoRedrawn,
                    nativeWidth = 3840,
                    nativeHeight = 2572,
                    worldGrid = hdGrid,
                    tileSize = 1024,
                    columns = 5,
                    rows = 5,
                    hasAlpha = false,
                    resourceRoot = "World/PcMapVariantReview/16001/hd-proposed/",
                    sourceNativeSha256 = new string('c', 64),
                    tiles = new PcOriginalTileEntry[25]
                };
                for (int r = 0; r < 5; r++)
                for (int c = 0; c < 5; c++)
                {
                    int x0 = hdGrid.PixelX(c, 3840), x1 = hdGrid.PixelX(c + 1, 3840);
                    int y0 = hdGrid.PixelY(r, 2572), y1 = hdGrid.PixelY(r + 1, 2572);
                    var tile = new PcOriginalTileEntry
                    {
                        row = r + 1, column = c + 1, x = x0, y = y0, width = x1 - x0, height = y1 - y0,
                        resourcePath = hdManifest.resourceRoot + $"tiles/r{r + 1:00}_c{c + 1:00}",
                        fileExtension = ".png", bytes = 1, sha256 = new string('d', 64)
                    };
                    Require(tile.width == (c == 4 ? 640 : 800) && tile.height == (r == 4 ? 172 : 600),
                        $"16001 HD tile ({r + 1},{c + 1}) size {tile.width}x{tile.height} mismatch.");
                    hdManifest.tiles[r * 5 + c] = tile;
                    var rect = PcOriginalViewportPlanner.TileWorldRect(hdManifest, tile);
                    Require(Math.Abs(rect.Width * PcOriginalMapContract.PixelsPerUnit / tile.width - 0.5f) < 1e-6f &&
                            Math.Abs(rect.Height * PcOriginalMapContract.PixelsPerUnit / tile.height - 0.5f) < 1e-6f,
                        "16001 HD tile must render at exact 0.5x scale (effective 2048 PPU).");
                }
                hdManifest.Validate();
                var topLeft = PcOriginalViewportPlanner.TileWorldRect(hdManifest, hdManifest.tiles[0]);
                var botRight = PcOriginalViewportPlanner.TileWorldRect(hdManifest, hdManifest.tiles[24]);
                Require(Math.Abs(topLeft.XMin) < 1e-6f && Math.Abs(topLeft.YMax - m16001.WorldHeight) < 1e-6f &&
                        Math.Abs(botRight.XMax - m16001.WorldWidth) < 1e-6f && Math.Abs(botRight.YMin) < 1e-6f,
                    "16001 HD 25-tile coverage must match original 1.875x1.255859375 world bounds.");
            });

            Check("Map 28002 adopts Q-Dao spring/midautumn RGBA appearances under unique mapId while preserving PC original provenance", () =>
            {
                var m28002 = catalog.FindByMapId("28002");
                Require(m28002 != null && m28002.numericMapId == 28002 && m28002.name == "地图 28002", "28002 identity mismatch.");
                Require(m28002.artStage == PcOriginalMapContract.ArtStageQdaoRedrawn && !m28002.isTemporaryOriginal,
                    "28002 must be qdao_redrawn_static_back with isTemporaryOriginal=false.");
                Require(m28002.nativeWidth == 3000 && m28002.nativeHeight == 1800 && m28002.hasAlpha && m28002.tileFormat == "png_rgba_native_1024_grid",
                    "28002 must preserve 3000x1800 RGBA PNG format.");
                Require(m28002.serverRegistrationStatus == "unregistered_local_preview_only",
                    "28002 must remain unregistered_local_preview_only without authoritative server scene config.");
                Require(m28002.pcOriginalSourceNativeSha256 == "d4f9e5d5556fb8bccc0ef27f112e956b253e35daa4965641a87a47f32a81748f" &&
                        m28002.pcOriginalSourceNativeBytes == 7843148,
                    "28002 must retain original client.dbfs provenance separately.");
                Require(m28002.HasMultipleAppearances && m28002.appearances.Length == 2 && m28002.defaultAppearance == "spring",
                    "28002 must expose spring and midautumn appearances with default spring.");

                var spring = m28002.ResolveAppearance("spring");
                var midautumn = m28002.ResolveAppearance("midautumn");
                var midautumnAlias = m28002.ResolveAppearance("mid-autumn");
                Require(spring != null && midautumn != null && midautumnAlias != null, "28002 spring/midautumn resolution failed.");
                Require(spring.resourceRoot == "World/PcOriginalPreview20261123/28002/qdao-spring-v002/", "28002 spring root mismatch.");
                Require(midautumn.resourceRoot == "World/PcOriginalPreview20261123/28002/qdao-midautumn-v002/", "28002 midautumn root mismatch.");
                Require(midautumnAlias.resourceRoot == midautumn.resourceRoot, "mid-autumn alias must resolve to midautumn.");
                Require(spring.sourceNativeSha256 == "367aee7302e076f2e68870d5a126fce8d8402380a50c46a7f30d4ebc499e570e", "28002 spring SHA mismatch.");
                Require(midautumn.sourceNativeSha256 == "c7e15bcd00a22a1f185bd8952a513d67abb43f146865da25f7cce9a3aa5209f8", "28002 midautumn SHA mismatch.");
                Require(m28002.ResolveAppearance("invalid") == null, "Unknown appearance must return null.");

                // Verify exact 3x2 remainder dimensions (952x1024 east edge, 1024x776 south edge, 952x776 SE corner) for both festivals.
                foreach (var variant in new[] { spring, midautumn })
                {
                    string tPath = Path.Combine(repoRoot, "Assets", "Resources",
                        variant.tilesManifestResourcePath.Replace('/', Path.DirectorySeparatorChar) + ".json");
                    var tm = JsonSerializer.Deserialize<PcOriginalTileManifest>(File.ReadAllText(tPath), JsonOptions);
                    tm.Validate(variant);
                    Require(tm.tiles.Length == 6, "28002 must have 6 tiles.");
                    Require(tm.tiles[2].width == 952 && tm.tiles[2].height == 1024, "r01_c03 must be 952x1024.");
                    Require(tm.tiles[3].width == 1024 && tm.tiles[3].height == 776, "r02_c01 must be 1024x776.");
                    Require(tm.tiles[5].width == 952 && tm.tiles[5].height == 776, "r02_c03 must be 952x776.");
                }
            });

            Check("Viewport planner maps top-left pixels to bottom-left world rects and respects budget", () =>
            {
                var tianyong = catalog.FindByMapId("05000");
                Require(tianyong != null && tianyong.nativeWidth == 10000 && tianyong.nativeHeight == 6774, "05000 entry invalid.");
                string tilesPath = Path.Combine(repoRoot, "Assets", "Resources",
                    tianyong.tilesManifestResourcePath.Replace('/', Path.DirectorySeparatorChar) + ".json");
                var manifest = JsonSerializer.Deserialize<PcOriginalTileManifest>(File.ReadAllText(tilesPath), JsonOptions);

                // Top-left tile (row=1, col=1) should touch northern YMax == WorldHeight and western XMin == 0.
                var firstRect = PcOriginalViewportPlanner.TileWorldRect(manifest.nativeHeight, manifest.tiles[0]);
                Require(Math.Abs(firstRect.XMin) < 1e-5f, "Top-left tile XMin should be 0.");
                Require(Math.Abs(firstRect.YMax - tianyong.WorldHeight) < 1e-4f, "Top-left tile YMax should equal WorldHeight.");

                // Bottom-right tile (row=7, col=10) should touch southern YMin == 0 and eastern XMax == WorldWidth.
                var lastRect = PcOriginalViewportPlanner.TileWorldRect(manifest.nativeHeight, manifest.tiles[manifest.tiles.Length - 1]);
                Require(Math.Abs(lastRect.YMin) < 1e-5f, "Bottom-right tile YMin should be 0.");
                Require(Math.Abs(lastRect.XMax - tianyong.WorldWidth) < 1e-4f, "Bottom-right tile XMax should equal WorldWidth.");

                // Full map view overlaps all 70 tiles, but selection clamps to maxResidentTiles=24 centered near view center.
                var fullBounds = PcOriginalViewportPlanner.MapWorldBounds(manifest.nativeWidth, manifest.nativeHeight);
                var selected = new List<int>();
                int count = PcOriginalViewportPlanner.SelectVisibleTiles(manifest, fullBounds, 24, selected, out int overlapping);
                Require(overlapping == 70, $"Expected 70 overlapping tiles on 05000 full view, got {overlapping}.");
                Require(count == 24 && selected.Count == 24, $"Expected 24 clamped tiles, got {count}.");

                // Zoomed-in 2x2 unit view near origin selects only a small corner subset.
                var zoomedCorner = new PcOriginalWorldRect(0f, 0f, 1.5f, 1.5f);
                int zoomedCount = PcOriginalViewportPlanner.SelectVisibleTiles(manifest, zoomedCorner, 24, selected, out int zoomedOverlap);
                Require(zoomedOverlap > 0 && zoomedOverlap <= 6 && zoomedCount == zoomedOverlap,
                    $"Unexpected zoomed overlap count: {zoomedOverlap}.");
            });

            Check("Catalog supports swapping an entry from PC original to Q-Dao redrawn art without code change", () =>
            {
                var swapped = JsonSerializer.Deserialize<PcOriginalMapCatalog>(File.ReadAllText(catalogPath), JsonOptions);
                var m05000 = swapped.FindByMapId("05000");
                m05000.artStage = PcOriginalMapContract.ArtStageQdaoRedrawn;
                m05000.isTemporaryOriginal = false;
                m05000.revision = "qdao-redrawn-20261201";
                m05000.resourceRoot = "World/QdaoRedrawnPreview/05000/qdao-redrawn-20261201/";
                m05000.overviewResourcePath = m05000.resourceRoot + "overview";
                m05000.tilesManifestResourcePath = m05000.resourceRoot + "tiles";
                swapped.Validate();
                Require(PcOriginalMapContract.ArtStageDisplayLabel(m05000.artStage).Contains("道家Q版"),
                    "Swapped artStage label should identify Q-Dao art.");
            });

            Check("Rejects marking unavailable gap or HOLD candidate as integrated", () =>
            {
                var badGap = JsonSerializer.Deserialize<PcOriginalMapCatalog>(File.ReadAllText(catalogPath), JsonOptions);
                badGap.unavailableMaps[0].integrated = true;
                Reject(() => badGap.Validate(), "unavailable gap integrated=true");

                var badHold = JsonSerializer.Deserialize<PcOriginalMapCatalog>(File.ReadAllText(catalogPath), JsonOptions);
                badHold.specialHoldCandidates[0].integrated = true;
                Reject(() => badHold.Validate(), "special HOLD integrated=true");
            });

            Check("Rejects claiming productionTravelEnabled or gameplay scene on static-back catalog", () =>
            {
                var badOnline = JsonSerializer.Deserialize<PcOriginalMapCatalog>(File.ReadAllText(catalogPath), JsonOptions);
                badOnline.productionTravelEnabled = true;
                Reject(() => badOnline.Validate(), "productionTravelEnabled=true");
            });

            Check("Rejects tampered tile coverage or oversized tile", () =>
            {
                var entry = catalog.FindByMapId("19003");
                string tilesPath = Path.Combine(repoRoot, "Assets", "Resources",
                    entry.tilesManifestResourcePath.Replace('/', Path.DirectorySeparatorChar) + ".json");
                var badManifest = JsonSerializer.Deserialize<PcOriginalTileManifest>(File.ReadAllText(tilesPath), JsonOptions);
                badManifest.tiles[0].width = 1025;
                Reject(() => badManifest.Validate(entry), "tile width > tileSize");
            });

            Check("Rejects invalid multi-appearance Q-Dao catalog mutations", () =>
            {
                // 1. Duplicate appearance id (including mid-autumn alias colliding with midautumn)
                var dupApp = JsonSerializer.Deserialize<PcOriginalMapCatalog>(File.ReadAllText(catalogPath), JsonOptions);
                dupApp.FindByMapId("28002").appearances[1].id = "spring";
                Reject(() => dupApp.Validate(), "duplicate appearance id");

                // 2. Duplicate appearance resourceRoot
                var dupRoot = JsonSerializer.Deserialize<PcOriginalMapCatalog>(File.ReadAllText(catalogPath), JsonOptions);
                var eRoot = dupRoot.FindByMapId("28002");
                eRoot.appearances[1].resourceRoot = eRoot.appearances[0].resourceRoot;
                eRoot.appearances[1].overviewResourcePath = eRoot.appearances[0].overviewResourcePath;
                eRoot.appearances[1].tilesManifestResourcePath = eRoot.appearances[0].tilesManifestResourcePath;
                Reject(() => dupRoot.Validate(), "duplicate appearance resourceRoot");

                // 3. Reusing old PC original SHA as Q-Dao sourceNativeSha256
                var staleSha = JsonSerializer.Deserialize<PcOriginalMapCatalog>(File.ReadAllText(catalogPath), JsonOptions);
                var eSha = staleSha.FindByMapId("28002");
                eSha.appearances[1].sourceNativeSha256 = eSha.pcOriginalSourceNativeSha256;
                Reject(() => staleSha.Validate(), "Q-Dao reusing PC original SHA");

                // 4. Q-Dao stage marked isTemporaryOriginal=true
                var badTemp = JsonSerializer.Deserialize<PcOriginalMapCatalog>(File.ReadAllText(catalogPath), JsonOptions);
                var eTemp = badTemp.FindByMapId("28002");
                eTemp.isTemporaryOriginal = true;
                eTemp.appearances[0].isTemporaryOriginal = true;
                Reject(() => badTemp.Validate(), "qdao_redrawn_static_back with isTemporaryOriginal=true");

                // 5. Top-level revision drifting from defaultAppearance
                var drift = JsonSerializer.Deserialize<PcOriginalMapCatalog>(File.ReadAllText(catalogPath), JsonOptions);
                drift.FindByMapId("28002").revision = "qdao-midautumn-v002";
                Reject(() => drift.Validate(), "top-level revision drift from defaultAppearance");

                // 6. HD appearance changing map world bounds (08001 must stay 3200x2400 world extent)
                var badExtent = JsonSerializer.Deserialize<PcOriginalMapCatalog>(File.ReadAllText(catalogPath), JsonOptions);
                badExtent.FindByMapId("08001").appearances[1].worldGrid.widthPixels = 6400;
                Reject(() => badExtent.Validate(), "HD appearance altering map world bounds");
            });

            Console.WriteLine($"PcOriginalMapPreviewTests PASS: {Passed.Count}/{Passed.Count} checks succeeded.");
            foreach (string item in Passed)
                Console.WriteLine("  [PASS] " + item);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("PcOriginalMapPreviewTests FAIL: " + error);
            return 1;
        }
    }
}
