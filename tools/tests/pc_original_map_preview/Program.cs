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

            Check("All 36 committed tile manifests validate with exact pixel coverage and <=1024 limits", () =>
            {
                int totalTiles = 0;
                foreach (var entry in catalog.maps)
                {
                    string tilesPath = Path.Combine(repoRoot, "Assets", "Resources",
                        entry.tilesManifestResourcePath.Replace('/', Path.DirectorySeparatorChar) + ".json");
                    Require(File.Exists(tilesPath), "Missing tiles.json: " + tilesPath);
                    var manifest = JsonSerializer.Deserialize<PcOriginalTileManifest>(File.ReadAllText(tilesPath), JsonOptions);
                    Require(manifest != null, "Null tiles manifest for " + entry.mapId);
                    manifest.Validate(entry);
                    totalTiles += manifest.tiles.Length;
                }
                Require(totalTiles == 1025, $"Expected 1025 total tiles across 36 maps, got {totalTiles}.");
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
