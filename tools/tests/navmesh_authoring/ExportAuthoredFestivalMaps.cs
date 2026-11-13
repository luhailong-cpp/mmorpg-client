using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MmorpgClient.Editor.NavigationAuthoring;
using MmorpgClient.World.NavigationAuthoring;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>只在隔离工程导出用户已画地图，不保存或改写作者草稿。</summary>
public static class ExportAuthoredFestivalMaps
{
    private const string DraftRoot = "Assets/MapNavigationDrafts/Festival20261111/";
    private static readonly Dictionary<string, string> SourcePins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static string _source, _output;

    private sealed class Candidate
    {
        public Vector3 position;
        public float clearance;
    }

    public static void Run()
    {
        var report = new JObject { ["schemaVersion"] = 1, ["result"] = "RUNNING",
            ["scope"] = "Actual authored-map source export and sampled Unity queries; not whole-map, deployed-server or live-game acceptance",
            ["unityVersion"] = Application.unityVersion, ["maps"] = new JArray() };
        var paired = new JArray();
        try
        {
            _source = Path.GetFullPath(Argument("-navmeshSourceProject")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            _output = Path.GetFullPath(Argument("-authoredExportOut"));
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Require(!string.Equals(project, _source, StringComparison.OrdinalIgnoreCase), "Refusing to run against the author's live source project");
            Require(Directory.Exists(Path.Combine(_source, "Assets")), "Source project does not exist");
            Require(!Inside(_output, Path.Combine(_source, "Assets")), "Export output must not overwrite source Assets");
            Directory.CreateDirectory(_output);
            report["sourceProject"] = _source; report["isolatedProject"] = project;
            report["contract"] = NavMeshAuthoringDraft.BakeContractRevision;
            Require(NavMesh.CalculateTriangulation().vertices.Length == 0, "Isolated exporter already has unrelated navigation loaded");
            foreach (var item in new[] { new { key = "26000-mid-autumn", scene = 26000 }, new { key = "26008-spring", scene = 26008 } })
            {
                var result = ExportMap(item.key, item.scene);
                ((JArray)report["maps"]).Add(result.Item1); paired.Add(result.Item2);
            }
            VerifyPins();
            report["sourceFiles"] = new JArray(SourcePins.Select(p => new JObject { ["path"] = p.Key, ["sha256"] = p.Value, ["unchanged"] = true }));
            File.WriteAllText(Path.Combine(_output, "paired-unity-queries.json"), new JObject { ["cases"] = paired,
                ["scope"] = "Sampled actual authored-map Unity queries; native path corners retain engine approximation height" }.ToString());
            report["pairedUnityQueriesSha256"] = Hash(Path.Combine(_output, "paired-unity-queries.json"));
            report["result"] = "PASS";
        }
        catch (Exception error) { report["result"] = "FAIL"; report["error"] = error.ToString(); Debug.LogException(error); }
        finally
        {
            try { VerifyPins(); }
            catch (Exception error) { report["result"] = "FAIL"; report["sourceMutationError"] = error.ToString(); }
            if (!string.IsNullOrEmpty(_output))
            {
                try { File.WriteAllText(Path.Combine(_output, "delivery.json"), report.ToString()); }
                catch (Exception error) { report["result"] = "FAIL"; Debug.LogException(error); }
            }
            Debug.Log("[ExportAuthoredFestivalMaps] " + report["result"] + " " + _output);
            EditorApplication.Exit((string)report["result"] == "PASS" ? 0 : 1);
        }
    }

    private static Tuple<JObject, JObject> ExportMap(string key, int sceneId)
    {
        string assetPath = DraftRoot + key + ".asset";
        PinCopy(assetPath); PinCopy(assetPath + ".meta");
        var original = AssetDatabase.LoadAssetAtPath<NavMeshAuthoringDraft>(assetPath);
        Require(original != null && original.mapKey == key, "Unable to load the intended draft: " + key);
        original.Validate(); Require(original.polygons.Count > 0, "Draft has no authored regions: " + key);
        Require(original.bakedData != null && NavMeshAuthoringAssetIO.IsBakeCurrent(original), "Baked authoring identity is stale or absent: " + key);
        string navPath = AssetDatabase.GetAssetPath(original.bakedData);
        PinCopy(navPath); PinCopy(navPath + ".meta");
        foreach (var tile in original.backgroundTiles)
        {
            PinCopy(tile.assetPath); PinCopy(tile.assetPath + ".meta");
            Require(Hash(tile.assetPath) == tile.sha256 && Hash(Path.Combine(_source, tile.assetPath)) == tile.sha256,
                "Original/copied source PNG SHA differs from the authored identity: " + tile.assetPath);
        }
        string before = EditorJsonUtility.ToJson(original);
        var geometry = NavigationPolygonGeometry.Build(original);
        int sourceVertices = geometry.vertexCount, sourceTriangles = geometry.triangles.Length / 3;
        Require(sourceTriangles > 0 && geometry.vertices.All(p => p.y == 0 && Finite(p.x) && Finite(p.z)), "Source is not finite planar authored geometry");
        UnityEngine.Object.DestroyImmediate(geometry);
        var snapshot = UnityEngine.Object.Instantiate(original);
        var instance = NavMesh.AddNavMeshData(original.bakedData);
        Require(instance.valid, "Unable to load native NavMesh for " + key);
        try
        {
            var filter = new NavMeshQueryFilter { agentTypeID = original.agentTypeId, areaMask = NavMesh.AllAreas };
            var candidates = GridCandidates(original, filter);
            bool triangleFallback = candidates.Count == 0;
            if (triangleFallback) candidates = TriangleCandidates(filter);
            Require(candidates.Count > 0, "No safe sample point exists in " + key);
            var groups = ConnectedGroups(candidates, filter);
            var largest = groups.OrderByDescending(g => g.Count).ThenByDescending(g => g.Max(c => c.clearance)).First();
            Candidate safest = largest.OrderByDescending(c => c.clearance).ThenBy(c => c.position.x).ThenBy(c => c.position.z).First();
            string reason;
            if (original.spawnSet)
            {
                reason = "Author-saved spawn, kept unchanged";
            }
            else
            {
                snapshot.spawn = new Vector2(safest.position.x, safest.position.z); snapshot.spawnSet = true;
                reason = "Test-only snapshot spawn: maximum edge clearance within the largest sampled PathComplete-connected group; " +
                    (triangleFallback ? "triangle-centroid fallback" : "5-world-unit grid");
            }
            Vector3 spawn = new Vector3(snapshot.spawn.x, 0, snapshot.spawn.y);
            Require((bool)Point(spawn, filter)["onMesh"], "Original ground spawn fails strict XYZ tolerance for " + key);
            Require(snapshot.boundsConfirmed == original.boundsConfirmed && NavMeshAuthoringAssetIO.IsBakeCurrent(snapshot), "Snapshot changed author bounds or geometry identity");
            string directory = Path.Combine(_output, key);
            Require(!Directory.Exists(directory) || Directory.GetFileSystemEntries(directory).Length == 0, "Map export directory must be empty: " + directory);
            NavMeshAuthoringAssetIO.Export(snapshot, directory);
            var queries = Queries(original, spawn, candidates, groups, filter, directory);
            File.WriteAllText(Path.Combine(directory, "unity-queries.json"), queries.ToString());
            var source = JObject.Parse(File.ReadAllText(Path.Combine(directory, "source.json")));
            var result = new JObject {
                ["mapKey"] = key, ["sceneId"] = sceneId, ["sceneConfigId"] = sceneId, ["appearance"] = key.Substring(key.IndexOf('-') + 1),
                ["authoredAsset"] = assetPath, ["authoredAssetGuid"] = AssetDatabase.AssetPathToGUID(assetPath),
                ["authoredAssetSha256"] = SourcePins[Path.GetFullPath(Path.Combine(_source, assetPath))],
                ["sourceAssetPath"] = Path.GetFullPath(Path.Combine(_source,assetPath)),
                ["sourceAssetSha256"] = SourcePins[Path.GetFullPath(Path.Combine(_source, assetPath))],
                ["navMeshAsset"] = navPath, ["navMeshGuid"] = AssetDatabase.AssetPathToGUID(navPath), ["navMeshAssetSha256"] = Hash(navPath),
                ["bakedGeometrySha256"] = original.bakedGeometrySha256, ["authoringGeometryHash"] = original.bakedGeometrySha256, ["isBakeCurrent"] = true,
                ["worldRect"] = new JObject { ["x"] = original.worldRect.x, ["z"] = original.worldRect.y, ["width"] = original.worldRect.width, ["depth"] = original.worldRect.height },
                ["boundsConfirmed"] = original.boundsConfirmed, ["authoredSpawnSet"] = original.spawnSet,
                ["authoredSpawnUnity"] = original.spawnSet ? Position(new Vector3(original.spawn.x, 0, original.spawn.y)) : null,
                ["testSpawnUnity"] = Position(spawn), ["spawnUnity"] = Position(spawn), ["testSpawnServer"] = new JArray(spawn.z, spawn.x, spawn.y), ["spawnReason"] = reason,
                ["candidateCount"] = candidates.Count, ["sampledConnectedGroupSizes"] = new JArray(groups.Select(g => g.Count)),
                ["selectedSpawnClearance"] = Clearance(spawn, filter),
                ["polygons"] = new JArray(original.polygons.Select((p, i) => new JObject { ["index"] = i, ["name"] = p.name, ["blocked"] = p.blocked, ["points"] = p.points.Count })),
                ["sourceVertexCount"] = sourceVertices, ["sourceTriangleCount"] = sourceTriangles,
                ["exportDirectory"] = directory, ["sourceJsonSha256"] = Hash(Path.Combine(directory, "source.json")), ["sourceSha256"] = Hash(Path.Combine(directory, "source.json")),
                ["authoringJsonSha256"] = Hash(Path.Combine(directory, "navigation.json")), ["objSha256"] = Hash(Path.Combine(directory, "world-y-up.obj")),
                ["serverBakeArgumentsSha256"] = Hash(Path.Combine(directory, "server-bake-args.json")), ["unityQueriesSha256"] = Hash(Path.Combine(directory, "unity-queries.json")),
                ["agent"] = source["agent"].DeepClone(), ["bakeContract"] = source["bakeContract"].DeepClone(),
                ["serverAcceptance"] = "NOT_RUN", ["liveGameAcceptance"] = "NOT_RUN" };
            Require(EditorJsonUtility.ToJson(original) == before, "Exporter modified the copied original draft state: " + key);
            return Tuple.Create(result, queries);
        }
        finally { instance.Remove(); UnityEngine.Object.DestroyImmediate(snapshot); }
    }

    private static List<Candidate> GridCandidates(NavMeshAuthoringDraft draft, NavMeshQueryFilter filter)
    {
        var result = new List<Candidate>();
        for (float z = draft.worldRect.yMin + 2.5f; z < draft.worldRect.yMax; z += 5)
        for (float x = draft.worldRect.xMin + 2.5f; x < draft.worldRect.xMax; x += 5)
            AddCandidate(result, new Vector3(x, 0, z), filter);
        return result;
    }

    private static List<Candidate> TriangleCandidates(NavMeshQueryFilter filter)
    {
        var result = new List<Candidate>(); var mesh = NavMesh.CalculateTriangulation();
        for (int i = 0; i + 2 < mesh.indices.Length; i += 3)
        {
            var p = (mesh.vertices[mesh.indices[i]] + mesh.vertices[mesh.indices[i + 1]] + mesh.vertices[mesh.indices[i + 2]]) / 3;
            AddCandidate(result, new Vector3(p.x, 0, p.z), filter);
        }
        return result;
    }

    private static void AddCandidate(List<Candidate> result, Vector3 position, NavMeshQueryFilter filter)
    {
        if (!NavMesh.SamplePosition(position, out var hit, 2, filter) || !NavMeshAuthoringDraft.MatchesGroundPoint(position, hit.position)) return;
        result.Add(new Candidate { position = position, clearance = Clearance(position, filter) });
    }

    private static float Clearance(Vector3 position, NavMeshQueryFilter filter)
        => NavMesh.FindClosestEdge(position, out var edge, filter) ? Vector2.Distance(new Vector2(position.x,position.z), new Vector2(edge.position.x,edge.position.z)) : 0;

    private static List<List<Candidate>> ConnectedGroups(List<Candidate> candidates, NavMeshQueryFilter filter)
    {
        var groups = new List<List<Candidate>>(); var path = new NavMeshPath();
        foreach (var candidate in candidates)
        {
            var group = groups.FirstOrDefault(g => NavMesh.CalculatePath(g[0].position, candidate.position, filter, path) && path.status == NavMeshPathStatus.PathComplete);
            if (group == null) { group = new List<Candidate>(); groups.Add(group); }
            group.Add(candidate);
        }
        return groups;
    }

    private static JObject Queries(NavMeshAuthoringDraft draft, Vector3 spawn, List<Candidate> candidates, List<List<Candidate>> groups,
        NavMeshQueryFilter filter, string directory)
    {
        var positives = new List<Vector3> { spawn };
        foreach (var group in groups) AddUnique(positives, group.OrderByDescending(c => c.clearance).First().position);
        // 仅选有明确余量的采样点，避免把引擎轮廓微差伪装成参数是否同源的判断。
        var safe = candidates.Where(c => c.clearance >= Mathf.Max(.5f,draft.voxelSize * 3)).ToList();
        if (safe.Count == 0) safe = candidates;
        for (int i = 0; i < 20; i++) AddUnique(positives, safe[(int)((long)i * (safe.Count - 1) / 19)].position);
        var points = new JArray(positives.Select(p => { var q = Point(p, filter); q["role"] = "sampled-walkable"; return q; }));
        var rect = draft.worldRect;
        var negatives = new List<Vector3> { new Vector3(rect.xMin - 2,0,rect.center.y), new Vector3(rect.xMax + 2,0,rect.center.y),
            new Vector3(rect.center.x,0,rect.yMin - 2),new Vector3(rect.center.x,0,rect.yMax + 2),spawn + Vector3.up * .2f };
        foreach (var blocked in draft.polygons.Where(p => p.blocked))
        {
            Vector2? pixel = InteriorPoint(blocked.points, p => SafelyOffMesh(World(draft,p), filter));
            if (pixel.HasValue) AddUnique(negatives, World(draft,pixel.Value));
        }
        int gapCount = 0;
        for (float z = rect.yMin + 2.5f; z < rect.yMax && gapCount < 4; z += 5)
        for (float x = rect.xMin + 2.5f; x < rect.xMax && gapCount < 4; x += 5)
        {
            var p = new Vector3(x,0,z);
            if (SafelyOffMesh(p, filter)) { AddUnique(negatives,p); gapCount++; }
        }
        foreach (var p in negatives)
        {
            var query = Point(p,filter); Require(!(bool)query["onMesh"], "A negative sample unexpectedly lies on the authored navigation");
            query["role"] = "outside-obstacle-gap-or-raised-negative"; points.Add(query);
        }
        var routes = new JArray();
        var homePath = new NavMeshPath();
        var home = groups.FirstOrDefault(g => NavMesh.CalculatePath(spawn,g[0].position,filter,homePath) && homePath.status == NavMeshPathStatus.PathComplete);
        if (home != null) routes.Add(Route(spawn,home.OrderByDescending(c => (c.position-spawn).sqrMagnitude).First().position,filter));
        foreach (var target in positives.Where(p => (p-spawn).sqrMagnitude > 1).OrderByDescending(p => (p-spawn).sqrMagnitude).Take(4))
            routes.Add(Route(spawn,target,filter));
        if (groups.Count > 1) routes.Add(Route(groups[0][0].position,groups[1][0].position,filter));
        routes.Add(Route(spawn, negatives[0],filter));
        Require(routes.Any(r => (bool)r["complete"]), "Query sample set has no complete route");
        Require(routes.Any(r => !(bool)r["complete"]), "Query sample set has no rejected route");
        return new JObject { ["name"] = draft.mapKey, ["exportDirectory"] = directory, ["points"] = points, ["routes"] = routes,
            ["notes"] = "Route complete requires both strict on-mesh endpoints and native PathComplete. Raw corners keep Unity approximation height and are not actor foot positions." };
    }

    private static JObject Point(Vector3 requested, NavMeshQueryFilter filter)
    {
        bool found = NavMesh.SamplePosition(requested,out var hit,2,filter);
        return new JObject { ["input"] = Position(requested), ["onMesh"] = found && NavMeshAuthoringDraft.MatchesGroundPoint(requested,hit.position),
            ["nearest"] = found ? Position(hit.position) : null };
    }

    private static bool SafelyOffMesh(Vector3 requested, NavMeshQueryFilter filter)
        => !NavMesh.SamplePosition(requested,out var hit,2,filter) ||
            (!NavMeshAuthoringDraft.MatchesGroundPoint(requested,hit.position) &&
             Vector2.Distance(new Vector2(requested.x,requested.z),new Vector2(hit.position.x,hit.position.z)) >= .5f);

    private static JObject Route(Vector3 from, Vector3 to, NavMeshQueryFilter filter)
    {
        var path = new NavMeshPath(); bool calculated = NavMesh.CalculatePath(from,to,filter,path);
        bool validEndpoints = (bool)Point(from,filter)["onMesh"] && (bool)Point(to,filter)["onMesh"];
        return new JObject { ["from"] = Position(from), ["to"] = Position(to), ["complete"] = validEndpoints && calculated && path.status == NavMeshPathStatus.PathComplete,
            ["nativeStatus"] = path.status.ToString(), ["strictEndpoints"] = validEndpoints,
            ["rayBlocked"] = NavMesh.Raycast(from,to,out _,filter), ["corners"] = new JArray(path.corners.Select(Position)) };
    }

    private static Vector2? InteriorPoint(List<Vector2> polygon, Func<Vector2,bool> accept)
    {
        float minX = polygon.Min(p => p.x), maxX = polygon.Max(p => p.x), minY = polygon.Min(p => p.y), maxY = polygon.Max(p => p.y);
        for (int y = 0; y < 12; y++) for (int x = 0; x < 12; x++)
        {
            var p = new Vector2(Mathf.Lerp(minX,maxX,(x+.5f)/12),Mathf.Lerp(minY,maxY,(y+.5f)/12)); bool inside = false;
            for (int i = 0, j = polygon.Count-1; i < polygon.Count; j=i++)
                if ((polygon[i].y > p.y) != (polygon[j].y > p.y) && p.x < (polygon[j].x-polygon[i].x)*(p.y-polygon[i].y)/(polygon[j].y-polygon[i].y)+polygon[i].x) inside = !inside;
            if (inside && accept(p)) return p;
        }
        return null;
    }

    private static Vector3 World(NavMeshAuthoringDraft draft, Vector2 pixel) { var p = draft.PixelToWorld(pixel); return new Vector3(p.x,0,p.y); }
    private static void AddUnique(List<Vector3> points, Vector3 value) { if (!points.Any(p => (p-value).sqrMagnitude < .0001f)) points.Add(value); }
    private static JArray Position(Vector3 p) => new JArray(p.x,p.y,p.z);
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Inside(string path,string root) => string.Equals(path,root,StringComparison.OrdinalIgnoreCase) || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    private static string Argument(string key)
    {
        var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args,key);
        if (index < 0 || index+1 >= args.Length || args[index+1].StartsWith("-")) throw new ArgumentException("Missing argument: " + key);
        return args[index+1];
    }
    private static string Hash(string path) => NavMeshAuthoringAssetIO.Sha(File.ReadAllBytes(path));
    private static void PinCopy(string relative)
    {
        Require(!string.IsNullOrEmpty(relative), "Missing source asset path");
        string source = Path.GetFullPath(Path.Combine(_source,relative));
        Require(Inside(source,Path.Combine(_source,"Assets")), "Asset escapes the source project");
        string hash = Hash(source); Require(Hash(relative) == hash, "Isolated asset differs from source: " + relative);
        if (SourcePins.TryGetValue(source,out var prior)) Require(prior == hash,"Source changed during export: " + source);
        else SourcePins.Add(source,hash);
    }
    private static void VerifyPins() { foreach (var item in SourcePins) Require(Hash(item.Key) == item.Value,"Author source changed during export: " + item.Key); }
    private static void Require(bool value,string message) { if (!value) throw new InvalidOperationException(message); }
}
