using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using MmorpgClient.World.CurrentMaps;
using MmorpgClient.World.Tianyong;
using UnityEngine;

internal static class Program
{
    private static int Assertions;
    private static readonly List<string> Passed = new();
    private static readonly List<object> Deliveries = new();
    private static void Require(bool value, string message = "Assertion failed")
    { Assertions++; if (!value) throw new InvalidOperationException(message); }
    private static void Check(string name, Action action) { action(); Passed.Add(name); }
    private static void Reject(Action action)
    {
        Assertions++;
        try { action(); } catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Invalid production input was accepted.");
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string ResourceFile(string resource, string extension)
        => Path.Combine(Resources.Root, resource.Replace('/', Path.DirectorySeparatorChar)) + extension;
    private static Vector3 Pixel(JsonElement spec, JsonElement point)
    {
        var world = spec.GetProperty("worldRect");
        var canvas = spec.GetProperty("canvasPixels");
        return new Vector3(world.GetProperty("x").GetSingle() + point[0].GetSingle() * world.GetProperty("width").GetSingle() / canvas[0].GetSingle(), 0,
            world.GetProperty("y").GetSingle() + (canvas[1].GetSingle() - point[1].GetSingle()) * world.GetProperty("height").GetSingle() / canvas[1].GetSingle());
    }
    private static void VerifyPath(TianyongNavigationGrid nav, Vector3 start, Vector3 target)
    {
        var path = nav.FindPath(start, target);
        Require(path.Count >= 2, "Production A* did not find the authored floor route.");
        Require(Vector3.Distance(path[0], start) < .01f && Vector3.Distance(path[^1], target) < .01f);
        for (int segment = 1; segment < path.Count; segment++)
        {
            int steps = Math.Max(1, (int)Math.Ceiling(Vector3.Distance(path[segment - 1], path[segment]) / .1f));
            for (int sample = 0; sample <= steps; sample++)
                Require(nav.IsWalkable(Vector3.Lerp(path[segment - 1], path[segment], sample / (float)steps)),
                    "Production smoothed path crossed a blocked navigation cell.");
        }
    }

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 && args.Length != 5)
                throw new ArgumentException("Usage: FestivalMapDelivery <client-root> <result-json> [expected-maps expected-appearances expected-tiles]");
            int expectedMaps = args.Length == 5 ? int.Parse(args[2]) : 3;
            int expectedAppearances = args.Length == 5 ? int.Parse(args[3]) : 5;
            int expectedTiles = args.Length == 5 ? int.Parse(args[4]) : 36;
            string root = Path.GetFullPath(args[0]);
            Resources.Root = Path.Combine(root, "Assets", "Resources");
            string catalogText = File.ReadAllText(ResourceFile(MapDeliveryContract.CatalogPath, ".json"));
            var catalog = JsonUtility.FromJson<MapDeliveryCatalog>(catalogText);
            Check("Real production catalog parses all requested client-only holiday registrations", () =>
            {
                catalog.Validate();
                Require(catalog.maps.Length == expectedMaps);
                Require(catalog.maps.All(m => m.clientOnly && m.Find(m.DefaultAppearance) != null));
                Require(catalog.maps.Sum(m => m.appearances.Length) == expectedAppearances);
                CurrentMapCatalog.ApplyDeliveries(catalog);
                Require(CurrentMapCatalog.All.Count == expectedMaps + 3);
                Require(CurrentMapCatalog.Find(1u).Key == "main" && CurrentMapCatalog.Find(2u).Key == "island" && CurrentMapCatalog.Find(3u).Key == "village");
            });

            int tileCount = 0, appearanceCount = 0;
            Check("Local development entry rejects release, implicit opt-in, remote and malformed endpoints", () =>
            {
                Require(FestivalMapLocalTestPolicy.CanConnect(true, true, "http://127.0.0.1:8081", "127.0.0.1:10001"));
                Require(FestivalMapLocalTestPolicy.CanConnect(true, true, "http://localhost:8081", "[::1]:10001"));
                Require(!FestivalMapLocalTestPolicy.CanConnect(false, true, "http://127.0.0.1:8081", "127.0.0.1:10001"));
                Require(!FestivalMapLocalTestPolicy.CanConnect(true, false, "http://127.0.0.1:8081", "127.0.0.1:10001"));
                foreach (string gateway in new[] { null, "file:///localhost/test", "http://localhost.example:8081", "http://192.168.1.1:8081", "http://name@localhost:8081" })
                    Require(!FestivalMapLocalTestPolicy.CanConnect(true, true, gateway, "127.0.0.1:10001"));
                foreach (string gate in new[] { null, "127.0.0.1", "127.0.0.1:0", "127.0.0.1:65536", "localhost.example:10001", "192.168.1.1:10001" })
                    Require(!FestivalMapLocalTestPolicy.CanConnect(true, true, "http://127.0.0.1:8081", gate));
                Require(!FestivalMapLocalTestPolicy.IsDeliveredTarget(CurrentMapCatalog.Find(1u), "spring"));
                Require(FestivalMapLocalTestPolicy.IsRequested(new[] { "game.exe", "-festivalMapLocalTest" }));
                Require(!FestivalMapLocalTestPolicy.IsRequested(new[] { "game.exe", "-festivalMapLocalTest=false" }));
            });
            foreach (var registration in catalog.maps)
            {
                string mapId = registration.key.Substring("map".Length);
                using var sourceDocument = JsonDocument.Parse(File.ReadAllText(
                    ResourceFile(registration.appearances[0].resourceRoot + "source", ".json")));
                string planPath = Path.Combine(root, sourceDocument.RootElement.GetProperty("navigationPlanPath").GetString());
                using var planDocument = JsonDocument.Parse(File.ReadAllText(planPath));
                var spec = planDocument.RootElement.GetProperty("maps").GetProperty(mapId);
                var selected = CurrentMapCatalog.Find(registration.sceneId);
                Check(mapId + " stays client-only across real appearance definitions", () =>
                {
                    Require(selected.IsModern && !selected.CanTravelOnline && selected.Appearance == registration.DefaultAppearance);
                    Require(selected.ForAppearance("normal") == null && selected.ForAppearance("lantern") == null);
                    Require(FestivalMapLocalTestPolicy.IsDeliveredTarget(selected, selected.Appearance));
                    Require(!FestivalMapLocalTestPolicy.IsDeliveredTarget(selected, "normal"));
                    if (mapId == "19004" || mapId == "23006")
                        Require(!FestivalMapLocalTestPolicy.IsDeliveredTarget(selected, "spring"));
                    if (mapId == "28007")
                        Require(!FestivalMapLocalTestPolicy.IsDeliveredTarget(selected, "mid-autumn"));
                });
                byte[] firstMask = null;
                CurrentMapDefinition firstDefinition = null;
                foreach (var appearance in registration.appearances)
                {
                    appearanceCount++;
                    string label = mapId + "/" + appearance.id;
                    string sourceText = File.ReadAllText(ResourceFile(appearance.resourceRoot + "source", ".json"));
                    string manifestText = File.ReadAllText(ResourceFile(appearance.resourceRoot + "tiles", ".json"));
                    string maskText = File.ReadAllText(ResourceFile(appearance.resourceRoot + "walkmask", ".txt"));
                    byte[] mask = Convert.FromBase64String(maskText.Trim());
                    var definition = selected.ForAppearance(appearance.id);
                    CurrentMapData data = null;
                    CurrentMapTilesManifest manifest = null;
                    TianyongNavigationGrid nav = null;
                    Check(label + " real source/tiles/mask parse through production runtime", () =>
                    {
                        data = CurrentMapData.Parse(registration.key, registration.sceneId, sourceText, maskText);
                        manifest = CurrentMapTilesManifest.Parse(registration.key, sourceText, manifestText,
                            appearance.resourceRoot, registration.revision, appearance.id);
                        Require(data.SchemaVersion == 2 && data.Appearance == appearance.id && data.Revision == registration.revision);
                        Require(data.LayoutSha256 == appearance.layoutSha256);
                        Require(definition != null && !definition.CanTravelOnline && definition.Appearance == appearance.id);
                        Require(definition.ResourceFolder == appearance.resourceRoot);
                        Require(definition.LoadTexture().width == data.LayoutWidth && definition.LoadTexture().height == data.LayoutHeight);
                        Require(Hash(ResourceFile(appearance.resourceRoot + "layout", ".png")) == appearance.layoutSha256);
                        Require(definition.IsWalkable(definition.Spawn) && data.IsWalkable(data.Spawn));
                        nav = definition.CreateNavigation();
                        Require(nav.IsWalkable(definition.Spawn));
                        Require(nav.Bounds == data.WorldRect && nav.EffectiveBounds == data.EffectiveWorldRect);
                    });
                    Check(label + " all actual runtime PNG identities and square world geometry match", () =>
                    {
                        var canvas = spec.GetProperty("canvasPixels");
                        Require(manifest.tiles.Length == canvas[0].GetInt32() / 1024 * (canvas[1].GetInt32() / 1024));
                        foreach (var tile in manifest.tiles)
                        {
                            string path = ResourceFile(tile.resourcePath, ".png");
                            Require(File.Exists(path) && new FileInfo(path).Length == tile.bytes);
                            Require(Hash(path) == tile.sha256);
                            var image = Resources.Load<Texture2D>(tile.resourcePath);
                            Require(image.width == 1024 && image.height == 1024 && tile.nativePixels >= image.width);
                            var bounds = manifest.TileWorldRect(tile.row, tile.column);
                            Require(Math.Abs(bounds.width - bounds.height) < .001f && bounds.xMin >= data.WorldRect.xMin - .001f && bounds.yMin >= data.WorldRect.yMin - .001f && bounds.xMax <= data.WorldRect.xMax + .001f && bounds.yMax <= data.WorldRect.yMax + .001f);
                            tileCount++;
                        }
                    });
                    Check(label + " actual source bit order equals production navigation sampler on every cell", () =>
                    {
                        Require(nav.Width == data.Columns && nav.Depth == data.Rows);
                        Require(mask.Length == (data.Columns * data.Rows + 7) / 8);
                        int bits = data.Columns * data.Rows;
                        if (bits % 8 != 0) Require((mask[^1] & ((1 << (8 - bits % 8)) - 1)) == 0);
                        for (int z = 0; z < nav.Depth; z++)
                        for (int x = 0; x < nav.Width; x++)
                        {
                            var point = new Vector3(data.WorldRect.xMin + x * data.CellSize + data.CellSize / 2, 0,
                                data.WorldRect.yMin + z * data.CellSize + data.CellSize / 2);
                            Require(data.IsWalkable(point) == nav.IsWalkable(point));
                        }
                    });
                    Check(label + " walls/objects/padding are blocked by real CurrentMapData and navigation", () =>
                    {
                        foreach (var blocked in spec.GetProperty("blockedLandmarkPixels").EnumerateObject())
                        {
                            Vector3 point = Pixel(spec, blocked.Value);
                            Require(!data.IsWalkable(point) && !nav.IsWalkable(point), blocked.Name + " was traversable.");
                        }
                    });
                    Check(label + " production A* and smoothing reach every listed legal landmark route", () =>
                    {
                        foreach (var pair in spec.GetProperty("connectedLandmarkPairs").EnumerateArray())
                        {
                            Vector3 start = Pixel(spec, spec.GetProperty("landmarkPixels").GetProperty(pair[0].GetString()));
                            Vector3 end = Pixel(spec, spec.GetProperty("landmarkPixels").GetProperty(pair[1].GetString()));
                            VerifyPath(nav, start, end);
                        }
                        foreach (var pair in spec.GetProperty("disconnectedLandmarkPairs").EnumerateArray())
                        {
                            Vector3 start = Pixel(spec, spec.GetProperty("landmarkPixels").GetProperty(pair[0].GetString()));
                            Vector3 end = Pixel(spec, spec.GetProperty("landmarkPixels").GetProperty(pair[1].GetString()));
                            Require(nav.FindPath(start, end).Count == 0, "Cloud platforms acquired an invisible path.");
                        }
                    });
                    Check(label + " real parser rejects wrong source identity/missing mask/incomplete set", () =>
                    {
                        Reject(() => CurrentMapData.Parse(registration.key, registration.sceneId + 1, sourceText, maskText));
                        Reject(() => CurrentMapData.Parse(registration.key, registration.sceneId, sourceText, ""));
                        var mutated = JsonNode.Parse(manifestText).AsObject();
                        mutated["tiles"].AsArray().RemoveAt(0);
                        Reject(() => CurrentMapTilesManifest.Parse(registration.key, sourceText, mutated.ToJsonString(),
                            appearance.resourceRoot, registration.revision, appearance.id));
                        Reject(() => CurrentMapTilesManifest.Parse(registration.key, sourceText, manifestText,
                            appearance.resourceRoot, registration.revision, "normal"));
                    });
                    if (firstMask != null)
                        Check(mapId + " real festival definitions and mask bytes share navigation", () =>
                        {
                            Require(firstMask.SequenceEqual(mask));
                            Require(firstDefinition.SharesNavigationWith(definition));
                            Require(firstDefinition.ContentIdentity != definition.ContentIdentity && firstDefinition.TexturePath != definition.TexturePath);
                        });
                    firstMask ??= mask;
                    firstDefinition ??= definition;
                    Deliveries.Add(new { mapId, appearance = appearance.id, tiles = manifest.tiles.Length,
                        walkmaskColumns = data.Columns, walkmaskRows = data.Rows, spawn = new { x = data.Spawn.x, y = data.Spawn.y, z = data.Spawn.z },
                        productionParserAndNavigation = true, clientOnly = !definition.CanTravelOnline });
                }
            }
            Check("All requested resources are covered and no loader error was hidden", () =>
            {
                Require(tileCount == expectedTiles && appearanceCount == expectedAppearances);
                Require(Debug.Errors.Count == 0, string.Join("\n", Debug.Errors));
                CurrentMapCatalog.ReloadDeliveries();
                Require(CurrentMapCatalog.All.Count == expectedMaps + 3 && CurrentMapCatalog.Find(35004u).Appearance == "spring");
            });
            var result = new { ok = true, assertions = Assertions, checks = Passed.Count, passed = Passed,
                productionSourcesLinked = new[] { "MapDeliveryContract.cs", "CurrentMapCatalog.cs", "CurrentMapTilesManifest.cs", "CityTileManifest.cs", "TianyongNavigationGrid.cs", "TianyongMapDefinition.cs" },
                realManagedGeometry = "UnityEngine.CoreModule.dll", nativeAdaptersOnly = new[] { "JsonUtility (System.Text.Json field adapter)", "Resources (read-only local files)", "Texture2D (PNG dimensions only)", "TextAsset", "Debug" },
                tileCount, appearanceCount, deliveries = Deliveries, engineExecuted = false, renderedScreenAccepted = false,
                serverRequestsSent = 0, serverNavigationPublished = false };
            string output = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Console.WriteLine(JsonSerializer.Serialize(new { result.ok, result.assertions, result.checks, result.tileCount, result.appearanceCount, output }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(JsonSerializer.Serialize(new { ok = false, assertions = Assertions, passed = Passed }));
            return 1;
        }
    }
}
