using System;
using System.Collections.Generic;
using System.Text.Json;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MmorpgClient.World.CurrentMaps;

internal static class Program
{
    private static readonly List<string> Passed = new();
    private static readonly string Sha = new('a', 64);
    private static void Check(string name, Action action)
    {
        action(); Passed.Add(name);
    }
    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed.");
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Invalid delivery was accepted.");
    }
    private static MapDeliveryRegistration Map(uint scene = 21, string key = "test-map")
    {
        var value = new MapDeliveryRegistration { key = key, name = "Test", sceneId = scene, revision = "v001",
            appearances = new[] { Appearance(key, "v001", "normal", Sha) } };
        return value;
    }
    private static MapAppearanceDelivery Appearance(string key, string revision, string id, string sha)
        => new() { id = id, artStatus = "formal_art_delivery", layoutSha256 = sha,
            resourceRoot = MapDeliveryContract.ResourceRoot + key + "/" + revision + "/" + id + "/" + sha + "/" };
    private static string Geometry(int columns = 16, int rows = 8, int pixels = 1024,
        MapDeliveryRect? world = null, MapDeliveryRect? effective = null, MapDeliveryRect? image = null,
        int referenceWidth = 2, int referenceHeight = 1)
        => MapDeliveryContract.ValidateGeometry(columns, rows, pixels,
            world ?? new MapDeliveryRect { width = 160, height = 80 },
            effective ?? new MapDeliveryRect { width = 160, height = 80 },
            image ?? new MapDeliveryRect { width = 16384, height = 8192 }, referenceWidth, referenceHeight);

#if UNITY_MANAGED_CHECKS
    private static CurrentMapTilesManifest ModernManifest()
    {
        var value = new CurrentMapTilesManifest { schemaVersion = 2, deliveryStatus = "formal_runtime_tiles", key = "test-map",
            sceneId = 21, revision = "v001", appearance = "normal", resourceRoot = Map().appearances[0].resourceRoot,
            layoutSha256 = Sha, tilePixels = 1024, columns = 16, rows = 8,
            worldRect = new UnityEngine.Rect(50, -20, 160, 80),
            effectiveWorldRect = new MapDeliveryRect { x = 50, y = -20, width = 160, height = 80 },
            imageRect = new MapDeliveryRect { width = 16384, height = 8192 }, referenceWidth = 2, referenceHeight = 1,
            tiles = new CurrentMapTilesManifest.Entry[128] };
        for (int i = 0; i < value.tiles.Length; i++)
        {
            int row = i / 16 + 1, col = i % 16 + 1;
            value.tiles[i] = new CurrentMapTilesManifest.Entry { row = row, column = col,
                resourcePath = value.resourceRoot + $"tiles/r{row:00}_c{col:00}", sha256 = Sha, nativeSha256 = Sha,
                nativePixels = 1254, bytes = 1024 };
        }
        return value;
    }
    private static bool Valid(CurrentMapTilesManifest value)
        => value.Validate("test-map", 21, Sha, out _);
#endif

    public static int Main()
    {
        try
        {
            Check("128 total accepted", () => Require(Geometry() == null));
            Check("128 by1 not limited to8 or16 axes", () => Require(Geometry(128, 1,
                world: new MapDeliveryRect { width = 1280, height = 10 },
                effective: new MapDeliveryRect { width = 1280, height = 10 },
                image: new MapDeliveryRect { width = 131072, height = 1024 }, referenceWidth: 128, referenceHeight: 1) == null));
            Check("1 by128 portrait accepted", () => Require(Geometry(1, 128,
                world: new MapDeliveryRect { width = 10, height = 1280 },
                effective: new MapDeliveryRect { width = 10, height = 1280 },
                image: new MapDeliveryRect { width = 1024, height = 131072 }, referenceWidth: 1, referenceHeight: 128) == null));
            Check("129 total rejected", () => Require(Geometry(129, 1) != null));
            Check("16 by9 over cap rejected", () => Require(Geometry(16, 9) != null));
            Check("zero grid rejected", () => Require(Geometry(0, 8) != null));
            Check("negative grid rejected", () => Require(Geometry(-1, 8) != null));
            Check("multiplication overflow rejected", () => Require(Geometry(int.MaxValue, int.MaxValue) != null));
            Check("old4096 not mislabeled as new runtime", () => Require(Geometry(pixels: 4096) != null));
            Check("world stretching rejected", () => Require(Geometry(world: new MapDeliveryRect { width = 80, height = 80 }) != null));
            Check("full reference ratio preserved through padding", () => Require(Geometry(8, 8,
                world: new MapDeliveryRect { x = 50, y = -20, width = 80, height = 80 },
                effective: new MapDeliveryRect { x = 50, y = 0, width = 80, height = 40 },
                image: new MapDeliveryRect { y = 2048, width = 8192, height = 4096 }) == null));
            Check("northwest image origin maps south edge correctly", () => Require(Geometry(8, 8,
                world: new MapDeliveryRect { width = 80, height = 80 },
                effective: new MapDeliveryRect { y = 40, width = 80, height = 40 },
                image: new MapDeliveryRect { width = 8192, height = 4096 }) == null));
            Check("wrong image/world crop binding rejected", () => Require(Geometry(effective: new MapDeliveryRect { x = 1, width = 159, height = 80 }) != null));
            Check("missing reference dimensions rejected", () => Require(Geometry(referenceWidth: 0) != null));
            Check("changed aspect rejected", () => Require(Geometry(referenceWidth: 3) != null));
            Check("one-edge-pixel rounding allowed", () => Require(Geometry(referenceWidth: 16383, referenceHeight: 8192) == null));
            Check("nonfinite world rejected", () => Require(Geometry(world: new MapDeliveryRect { width = float.NaN, height = 80 }) != null));
            Check("infinite image rejected", () => Require(Geometry(image: new MapDeliveryRect { width = float.PositiveInfinity, height = 8192 }) != null));
            Check("image outside canvas rejected", () => Require(Geometry(image: new MapDeliveryRect { x = 1, width = 16384, height = 8192 }) != null));
            Check("formal normal registration accepted", () =>
            {
                var m = Map(); m.Validate(); Require(m.DefaultAppearance == "normal");
            });
            Check("empty default preserves earlier normal catalogs", () =>
            {
                var m = Map(); m.defaultAppearance = ""; m.Validate(); Require(m.DefaultAppearance == "normal");
            });
            Check("explicit spring default accepts only delivered holiday art", () =>
            {
                var m = Map(21003, "map-21003"); m.defaultAppearance = "spring";
                m.appearances = new[] { Appearance(m.key, m.revision, "spring", Sha),
                    Appearance(m.key, m.revision, "mid-autumn", new string('b', 64)) };
                m.Validate(); Require(m.DefaultAppearance == "spring" && m.Find("normal") == null);
            });
            Check("explicit mid-autumn default accepts its delivered art", () =>
            {
                var m = Map(); m.defaultAppearance = "mid-autumn";
                m.appearances = new[] { Appearance(m.key, m.revision, "mid-autumn", Sha) }; m.Validate();
            });
            Check("undelivered explicit default rejected", () =>
            {
                var m = Map(); m.defaultAppearance = "spring"; Reject(m.Validate);
            });
            Check("undefined explicit default rejected", () =>
            {
                var m = Map(); m.defaultAppearance = "night"; Reject(m.Validate);
            });
            Check("whitespace default does not silently select normal", () =>
            {
                var m = Map(); m.defaultAppearance = " "; Reject(m.Validate);
            });
            Check("all four independent source selections accepted", () =>
            {
                var m = Map(); m.appearances = new[] { Appearance(m.key, m.revision, "normal", Sha),
                    Appearance(m.key, m.revision, "spring", new string('b', 64)),
                    Appearance(m.key, m.revision, "lantern", new string('c', 64)),
                    Appearance(m.key, m.revision, "mid-autumn", new string('d', 64)) }; m.Validate();
                Require(m.Find("spring").layoutSha256 != m.Find("normal").layoutSha256);
            });
            Check("preview cannot register as game map", () => { var m = Map(); m.appearances[0].artStatus = "preview"; Reject(m.Validate); });
            Check("implicit normal missing is rejected", () => { var m = Map(); m.appearances = new[] { Appearance(m.key, m.revision, "spring", Sha) }; Reject(m.Validate); });
            Check("duplicate appearance rejected", () => { var m = Map(); m.appearances = new[] { m.appearances[0], m.appearances[0] }; Reject(m.Validate); });
            Check("undefined appearance rejected", () => { var m = Map(); m.appearances[0].id = "night"; Reject(m.Validate); });
            Check("zero server sceneId rejected", () => Reject(Map(0).Validate));
            Check("unsafe map key rejected", () => Reject(Map(key: "../test").Validate));
            Check("versioned source path mismatch rejected", () => { var m = Map(); m.appearances[0].layoutSha256 = new string('b',64); Reject(m.Validate); });
            Check("path traversal rejected", () => Require(!MapDeliveryContract.IsResourcePath(MapDeliveryContract.ResourceRoot + "../other/tiles/r01_c01")));
            Check("absolute path rejected", () => Require(!MapDeliveryContract.IsResourcePath("D:/work/image/native.png")));
            Check("duplicate scene identities rejected", () => Reject(new MapDeliveryCatalog { schemaVersion = 2, maps = new[] { Map(), Map(key: "other") } }.Validate));
            Check("duplicate keys rejected", () => Reject(new MapDeliveryCatalog { schemaVersion = 2, maps = new[] { Map(), Map(22) } }.Validate));
            Check("unknown catalog schema rejected", () => Reject(new MapDeliveryCatalog { schemaVersion = 1, maps = Array.Empty<MapDeliveryRegistration>() }.Validate));
            Check("valid empty catalog preserves old runtime option", () => new MapDeliveryCatalog { schemaVersion = 2, maps = Array.Empty<MapDeliveryRegistration>() }.Validate());
            Check("registration snapshot cannot mutate active source identity", () =>
            {
                var m = Map(); var snapshot = m.Snapshot(); m.revision = "v002"; m.appearances[0].layoutSha256 = new string('e',64);
                Require(snapshot.revision == "v001" && snapshot.appearances[0].layoutSha256 == Sha);
            });
            Check("snapshot preserves an explicit holiday default", () =>
            {
                var m = Map(); m.defaultAppearance = "spring";
                m.appearances = new[] { Appearance(m.key, m.revision, "spring", Sha) }; m.Validate();
                var snapshot = m.Snapshot(); m.defaultAppearance = "mid-autumn";
                snapshot.Validate(); Require(snapshot.DefaultAppearance == "spring" && snapshot.Find("normal") == null);
            });
            Check("client-only identity stays explicit through registration snapshot", () =>
            {
                var m = Map(21003, "map-21003"); m.clientOnly = true;
                m.Validate(); var snapshot = m.Snapshot(); m.clientOnly = false;
                snapshot.Validate(); Require(snapshot.clientOnly && !Map().clientOnly);
            });
            foreach (var file in new[] { "Assets/Scripts/World/CurrentMaps/MapDeliveryContract.cs",
                "Assets/Scripts/World/CurrentMaps/CurrentMapCatalog.cs", "Assets/Scripts/World/CurrentMaps/CurrentMapTilesManifest.cs",
                "Assets/Scripts/World/CurrentMaps/CurrentMapTileStreaming.cs", "Assets/Scripts/World/CurrentMaps/CurrentMapBuilder.cs",
                "Assets/Scripts/World/Tianyong/TianyongMapRuntime.cs", "Assets/Scripts/World/Tianyong/TianyongNavigationGrid.cs",
                "Assets/Scripts/World/Tianyong/TianyongPlayerController.cs", "Assets/Scripts/World/Tianyong/TianyongCameraController.cs",
                "Assets/Scripts/World/Tianyong/TianyongMapBuilder.cs", "Assets/Editor/CurrentMapTileImporter.cs" })
                Check("CSharp9 syntax " + Path.GetFileName(file), () => Require(!CSharpSyntaxTree.ParseText(File.ReadAllText(file),
                    new CSharpParseOptions(LanguageVersion.CSharp9)).GetDiagnostics().Any(x => x.Severity == DiagnosticSeverity.Error)));
#if UNITY_MANAGED_CHECKS
            Check("actual runtime128 manifest accepted", () => Require(Valid(ModernManifest())));
            Check("incomplete new formal set rejected", () => { var m = ModernManifest(); m.tiles = m.tiles.Take(127).ToArray(); Require(!Valid(m)); });
            Check("source layout SHA mismatch rejected", () => { var m = ModernManifest(); m.layoutSha256 = new string('b',64); Require(!Valid(m)); });
            Check("native linkage required", () => { var m = ModernManifest(); m.tiles[0].nativeSha256 = null; Require(!Valid(m)); });
            Check("native cannot be smaller than runtime", () => { var m = ModernManifest(); m.tiles[0].nativePixels = 512; Require(!Valid(m)); });
            Check("duplicate runtime coordinates rejected", () => { var m = ModernManifest(); m.tiles[1] = m.tiles[0]; Require(!Valid(m)); });
            Check("wrong runtime artifact dimensions rejected", () => { var m = ModernManifest(); m.tilePixels = 1254; Require(!Valid(m)); });
            Check("preview-like delivery gate rejected", () => { var m = ModernManifest(); m.deliveryStatus = "partial_native_tiles"; Require(!Valid(m)); });
            foreach (var key in new[] { "main", "island", "village" })
                Check("old4096 remains valid " + key, () =>
                {
                    int side = key == "main" ? 16 : 14; uint scene = key == "main" ? 1u : key == "island" ? 2u : 3u;
                    var m = new CurrentMapTilesManifest { schemaVersion = 1, key = key, sceneId = scene,
                        layoutSha256 = Sha, columns = side, rows = side, tilePixels = 4096, deliveryStatus = "partial_native_tiles",
                        worldRect = new UnityEngine.Rect(50,0,300,300), tiles = Array.Empty<CurrentMapTilesManifest.Entry>() };
                    Require(m.Validate(key,scene,Sha,out _));
                });
            Check("actual new tile shared borders stay exact", () =>
            {
                var m = ModernManifest();
                for (int row = 1; row <= m.rows; row++) for (int col = 1; col <= m.columns; col++)
                {
                    var rect = m.TileWorldRect(row,col);
                    if (col < m.columns) Require(rect.xMax == m.TileWorldRect(row,col+1).xMin);
                    if (row < m.rows) Require(rect.yMin == m.TileWorldRect(row+1,col).yMax);
                }
                Require(m.TileWorldRect(8,16).xMax == 210 && m.TileWorldRect(8,16).yMin == -20);
            });
            Check("actual navigation uses offset rectangular bounds", () =>
            {
                var n = new MmorpgClient.World.Tianyong.TianyongNavigationGrid(2, _ => true,
                    new UnityEngine.Rect(500,700,64,32));
                Require(n.Width == 32 && n.Depth == 16 && n.IsWalkable(new UnityEngine.Vector3(501,0,701)));
                Require(!n.IsWalkable(new UnityEngine.Vector3(499,0,701)) && !n.IsWalkable(new UnityEngine.Vector3(564,0,701)));
                var clamped = n.ClampXZ(new UnityEngine.Vector3(0,0,0)); Require(clamped.x == 502 && clamped.z == 702);
                var path = n.FindPath(new UnityEngine.Vector3(504,0,704),new UnityEngine.Vector3(554,0,724));
                Require(path.Count > 0 && path.All(n.IsWalkable));
            });
            Check("effective bounds reject padding inside a sampled walkable cell", () =>
            {
                var n = new MmorpgClient.World.Tianyong.TianyongNavigationGrid(2, _ => true,
                    new UnityEngine.Rect(100,200,100,100), new UnityEngine.Rect(111,211,88,88));
                Require(n.IsWalkable(new UnityEngine.Vector3(111,0,213)));
                Require(!n.IsWalkable(new UnityEngine.Vector3(110.5f,0,213)) &&
                    !n.IsWalkable(new UnityEngine.Vector3(111,0,210.5f)) &&
                    !n.IsWalkable(new UnityEngine.Vector3(199,0,213)));
                var clamped = n.ClampXZ(new UnityEngine.Vector3(100,0,200));
                Require(clamped.x == 113 && clamped.z == 213);
            });
            Check("ceil edge cell samples its actual clipped centre", () =>
            {
                var bounds = new UnityEngine.Rect(100,200,100,100);
                var sampled = new List<UnityEngine.Vector3>();
                var n = new MmorpgClient.World.Tianyong.TianyongNavigationGrid(3, p =>
                { sampled.Add(p); return bounds.Contains(new UnityEngine.Vector2(p.x,p.z)); }, bounds);
                Require(n.Width == 34 && n.Depth == 34 && sampled.Count == 34*34);
                Require(sampled.All(p => p.x >= 100 && p.x < 200 && p.z >= 200 && p.z < 300));
                Require(n.IsWalkable(new UnityEngine.Vector3(199,0,250)) &&
                    n.IsWalkable(new UnityEngine.Vector3(199.9f,0,299.9f)));
                Require(n.FindPath(new UnityEngine.Vector3(150,0,250),new UnityEngine.Vector3(199,0,250)).Count > 0);
            });
            Check("partly covered boundary cell uses effective intersection centre", () =>
            {
                var effective = new UnityEngine.Rect(111.75f,211.75f,87.25f,87.25f);
                var n = new MmorpgClient.World.Tianyong.TianyongNavigationGrid(2,
                    p => effective.Contains(new UnityEngine.Vector2(p.x,p.z)),
                    new UnityEngine.Rect(100,200,100,100), effective);
                Require(n.IsWalkable(new UnityEngine.Vector3(111.8f,0,213)) &&
                    !n.IsWalkable(new UnityEngine.Vector3(111.7f,0,213)));
            });
            Check("reviewR2 valid edge spawn can fail margin recovery before actor mutation", () =>
            {
                var n = new MmorpgClient.World.Tianyong.TianyongNavigationGrid(2,
                    p => (p.x < 102 && p.z >= 210 && p.z < 212) ||
                         (p.x >= 146 && p.x < 154 && p.z >= 246 && p.z < 254),
                    new UnityEngine.Rect(100,200,100,100));
                var edge = new UnityEngine.Vector3(101,0,211);
                var interior = new UnityEngine.Vector3(150,0,250);
                Require(n.IsWalkable(edge) && n.IsWalkable(interior));
                Reject(() => n.ResolveFallbackSpawn(interior,edge));
                Require(n.IsWalkable(n.ResolveFallbackSpawn(interior,interior)));
            });
            Check("empty modern registry retains all three legacy scene IDs", () =>
            {
                CurrentMapCatalog.ApplyDeliveries(new MapDeliveryCatalog { schemaVersion = 2, maps = Array.Empty<MapDeliveryRegistration>() });
                Require(CurrentMapCatalog.All.Count == 3 && CurrentMapCatalog.Find(1u).Key == "main" &&
                    CurrentMapCatalog.Find(2u).Key == "island" && CurrentMapCatalog.Find(3u).Key == "village");
            });
            Check("extensible registry has no16-map whitelist", () =>
            {
                var maps = Enumerable.Range(100,40).Select(i => Map((uint)i,"map-"+i)).ToArray();
                CurrentMapCatalog.ApplyDeliveries(new MapDeliveryCatalog { schemaVersion = 2, maps = maps });
                Require(CurrentMapCatalog.All.Count == 43 && CurrentMapCatalog.Find(139u).Key == "map-139");
            });
            Check("catalog snapshots source identity for revision detection", () =>
            {
                var m = Map(); CurrentMapCatalog.ApplyDeliveries(new MapDeliveryCatalog { schemaVersion = 2, maps = new[] { m } });
                var selected = CurrentMapCatalog.Find(21u); string identity = selected.ContentIdentity;
                m.revision = "v002"; Require(selected.ContentIdentity == identity && selected.Revision == "v001");
            });
            Check("changed source hash creates new selected content identity", () =>
            {
                string before = CurrentMapCatalog.Find(21u).ContentIdentity; var m = Map();
                m.appearances[0] = Appearance(m.key,m.revision,"normal",new string('b',64));
                CurrentMapCatalog.ApplyDeliveries(new MapDeliveryCatalog { schemaVersion = 2, maps = new[] { m } });
                Require(CurrentMapCatalog.Find(21u).ContentIdentity != before);
            });
            Check("actual appearance definition selects its independent resource folder", () =>
            {
                var m = Map(); m.appearances = new[] { m.appearances[0], Appearance(m.key,m.revision,"spring",new string('b',64)) };
                CurrentMapCatalog.ApplyDeliveries(new MapDeliveryCatalog { schemaVersion = 2, maps = new[] { m } });
                var normal = CurrentMapCatalog.Find(21u); var spring = normal.ForAppearance("spring");
                Require(spring != null && spring.Appearance == "spring" && spring.TexturePath != normal.TexturePath &&
                    spring.ContentIdentity != normal.ContentIdentity && normal.ForAppearance("lantern") == null);
            });
            Check("holiday-only catalog selects the explicit default without normal fallback", () =>
            {
                var m = Map(21003, "map-21003"); m.defaultAppearance = "spring";
                m.appearances = new[] { Appearance(m.key, m.revision, "spring", Sha),
                    Appearance(m.key, m.revision, "mid-autumn", new string('b',64)) };
                CurrentMapCatalog.ApplyDeliveries(new MapDeliveryCatalog { schemaVersion = 2, maps = new[] { m } });
                var selected = CurrentMapCatalog.Find(21003u);
                Require(selected.Appearance == "spring" && selected.ResourceFolder == m.appearances[0].resourceRoot &&
                    selected.ForAppearance("normal") == null && selected.ForAppearance("mid-autumn") != null &&
                    CurrentMapCatalog.Find(1u).Key == "main");
                m.defaultAppearance = "mid-autumn";
                Require(selected.Appearance == "spring");
                CurrentMapCatalog.ApplyDeliveries(new MapDeliveryCatalog { schemaVersion = 2, maps = new[] { Map() } });
            });
            Check("client-only maps cannot become online destinations through appearance selection", () =>
            {
                var maps = new[] { Map(21003, "map-21003"), Map(28007, "map-28007"), Map(35004, "map-35004") };
                foreach (var m in maps)
                {
                    m.clientOnly = true; m.defaultAppearance = "spring";
                    m.appearances = new[] { Appearance(m.key, m.revision, "spring", Sha) };
                }
                CurrentMapCatalog.ApplyDeliveries(new MapDeliveryCatalog { schemaVersion = 2, maps = maps });
                foreach (var m in maps)
                {
                    var selected = CurrentMapCatalog.Find(m.sceneId);
                    Require(!selected.CanTravelOnline && !selected.ForAppearance("spring").CanTravelOnline);
                    m.clientOnly = false; Require(!selected.CanTravelOnline);
                }
                Require(CurrentMapCatalog.Find(1u).CanTravelOnline && CurrentMapCatalog.Find(2u).CanTravelOnline &&
                    CurrentMapCatalog.Find(3u).CanTravelOnline);
                CurrentMapCatalog.ApplyDeliveries(new MapDeliveryCatalog { schemaVersion = 2, maps = new[] { Map() } });
                Require(CurrentMapCatalog.Find(21u).CanTravelOnline);
            });
            Check("invalid catalog does not mutate selected registry", () =>
            {
                string before = CurrentMapCatalog.Find(21u).ContentIdentity;
                Reject(() => CurrentMapCatalog.ApplyDeliveries(new MapDeliveryCatalog { schemaVersion = 2, maps = new[] { Map(),Map() } }));
                Require(CurrentMapCatalog.Find(21u).ContentIdentity == before);
            });
            Check("legacy key cannot silently change authoritative server scene ID", () =>
                Reject(() => CurrentMapCatalog.ApplyDeliveries(new MapDeliveryCatalog { schemaVersion = 2, maps = new[] { Map(80,"main") } })));
#endif
            Console.WriteLine(JsonSerializer.Serialize(new { passed = Passed.Count, failed = 0, scope = "Actual production contract/managed geometry and CSharp9 syntax; no Unity native JSON, renderer, device or deployment acceptance.", tests = Passed }));
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { passed = Passed.Count, failed = 1, error = error.ToString(), tests = Passed }));
            return 1;
        }
    }
}
