using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MmorpgClient.World.NavigationAuthoring;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace MmorpgClient.Editor.NavigationAuthoring
{
    public static class NavMeshAuthoringAssetIO
    {
        public const string DraftRoot = "Assets/MapNavigationDrafts";
        private static readonly Regex TileName = new Regex(@"^r(\d+)_c(\d+)\.png$", RegexOptions.IgnoreCase);

        public static NavMeshAuthoringDraft CreateFromImage(string sourcePath, string assetPath)
            => Create(new[] { Tuple.Create(sourcePath, 1, 1) }, assetPath);

        public static NavMeshAuthoringDraft CreateFromTileFolder(string folder, string assetPath)
        {
            var files = new List<Tuple<string, int, int>>();
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                var match = TileName.Match(Path.GetFileName(file));
                if (match.Success) files.Add(Tuple.Create(file, int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)));
            }
            if (files.Count == 0) throw new InvalidOperationException("目录中没有r01_c01.png形式的分块。");
            int rows = files.Max(f => f.Item2), columns = files.Max(f => f.Item3);
            if (rows < 1 || columns < 1 || rows > 100 || columns > 100 || files.Count != rows * columns ||
                files.Select(f => f.Item2 + ":" + f.Item3).Distinct().Count() != files.Count)
                throw new InvalidOperationException("分块必须从r01_c01开始，行列完整且无重复，最多100×100。");
            return Create(files.OrderBy(f => f.Item2).ThenBy(f => f.Item3).ToArray(), assetPath);
        }

        private static NavMeshAuthoringDraft Create(Tuple<string, int, int>[] files, string assetPath)
        {
            RequireNewDraftPath(assetPath);
            // Validate the whole tile set before creating files.
            var sizes = files.Select(f => ReadPngSize(f.Item1)).ToArray();
            var widths = new Dictionary<int, int>(); var heights = new Dictionary<int, int>();
            for (int i = 0; i < files.Length; i++)
            {
                var f = files[i]; var s = sizes[i];
                if ((widths.TryGetValue(f.Item3, out var w) && w != s.x) || (heights.TryGetValue(f.Item2, out var h) && h != s.y))
                    throw new InvalidOperationException("同列图块宽度、同行图块高度必须一致。");
                widths[f.Item3] = s.x; heights[f.Item2] = s.y;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
            var draft = ScriptableObject.CreateInstance<NavMeshAuthoringDraft>();
            draft.mapKey = Path.GetFileNameWithoutExtension(assetPath);
            draft.displayName = draft.mapKey;
            draft.imageWidth = widths.Values.Sum(); draft.imageHeight = heights.Values.Sum();
            draft.worldRect = new Rect(0, 0, 300, 300f * draft.imageHeight / draft.imageWidth);
            var tiles = new List<NavMeshBackgroundTile>();
            try
            {
                for (int i = 0; i < files.Length; i++)
                {
                    var f = files[i]; string path = ImportImage(f.Item1, assetPath);
                    tiles.Add(new NavMeshBackgroundTile { texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path),
                        assetPath = path, sha256 = Sha(File.ReadAllBytes(path)), row = f.Item2, column = f.Item3,
                        pixelWidth = sizes[i].x, pixelHeight = sizes[i].y });
                }
                draft.backgroundTiles = tiles.ToArray(); draft.Validate();
                AssetDatabase.CreateAsset(draft, assetPath); AssetDatabase.SaveAssetIfDirty(draft);
                return draft;
            }
            catch { UnityEngine.Object.DestroyImmediate(draft); throw; }
        }

        public static NavMeshAuthoringDraft ImportExistingMap(string folder, string assetPath)
        {
            var source = JObject.Parse(File.ReadAllText(Path.Combine(folder, "source.json")));
            var nav = JObject.Parse(File.ReadAllText(Path.Combine(folder, "navigation.json")));
            if (nav["walkablePolygons"] == null || nav["obstaclePolygons"] == null)
                throw new InvalidOperationException("此目录没有可导入的多边形标注。可用整图新建并自行描绘。");
            var draft = CreateFromImage(Path.Combine(folder, "layout.png"), assetPath);
            draft.mapKey = (string)source["key"] ?? draft.mapKey;
            draft.displayName = (string)source["name"] ?? draft.displayName;
            var rect = source["worldRect"];
            draft.worldRect = new Rect((float)rect["x"], (float)rect["y"], (float)rect["width"], (float)rect["height"]);
            int aw = (int?)nav["annotationWidth"] ?? draft.imageWidth, ah = (int?)nav["annotationHeight"] ?? draft.imageHeight;
            foreach (var key in new[] { "walkablePolygons", "obstaclePolygons" })
                foreach (var p in nav[key])
                {
                    var polygon = new NavMeshAreaPolygon { name = (string)p["id"], blocked = key == "obstaclePolygons" };
                    foreach (var point in p["points"])
                        polygon.points.Add(new Vector2((float)point[0] * draft.imageWidth / aw, (float)point[1] * draft.imageHeight / ah));
                    draft.polygons.Add(polygon);
                }
            var spawn = source["spawnWorld"];
            if (spawn != null) { draft.spawn = new Vector2((float)spawn["x"], (float)spawn["z"]); draft.spawnSet = true; }
            draft.boundsConfirmed = false;
            draft.Validate(); EditorUtility.SetDirty(draft); AssetDatabase.SaveAssetIfDirty(draft);
            return draft;
        }

        public static NavMeshData Bake(NavMeshAuthoringDraft draft)
        {
            draft.Validate(); VerifyImages(draft);
            var mesh = NavigationPolygonGeometry.Build(draft);
            try
            {
                var settings = draft.BuildSettings();
                var sources = new List<NavMeshBuildSource> { new NavMeshBuildSource {
                    shape = NavMeshBuildSourceShape.Mesh, sourceObject = mesh, transform = Matrix4x4.identity, area = 0 } };
                var bounds = mesh.bounds; bounds.Expand(new UnityEngine.Vector3(settings.agentRadius * 4, settings.agentHeight * 4 + 2, settings.agentRadius * 4));
                var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, UnityEngine.Vector3.zero, Quaternion.identity);
                if (data == null) throw new InvalidOperationException("Unity未生成NavMeshData，请检查地面及角色参数。");
                var check = NavMesh.AddNavMeshData(data, new UnityEngine.Vector3(0, 30000, 0), Quaternion.identity);
                bool any = false;
                try { foreach (var v in NavMesh.CalculateTriangulation().vertices) if (Mathf.Abs(v.y - 30000) < settings.agentHeight + 1) { any = true; break; } }
                finally { if (check.valid) check.Remove(); }
                if (!any) { UnityEngine.Object.DestroyImmediate(data); throw new InvalidOperationException("烘焙结果为空；区域可能小于角色直径，请检查角色半径和地面范围。"); }
                string path = AssetDatabase.GetAssetPath(draft);
                if (string.IsNullOrEmpty(path)) { UnityEngine.Object.DestroyImmediate(data); throw new InvalidOperationException("请先保存草稿资产。"); }
                string geometryHash = GeometryHash(draft);
                // Immutable per-geometry assets keep Unity Undo from restoring a
                // reference whose native mesh has since been overwritten.
                path = Path.ChangeExtension(path, ".navmesh-" + geometryHash.Substring(0, 16) + ".asset");
                data.name = draft.mapKey + " NavMesh";
                // CreateAsset overwrites the native asset while retaining its existing .meta/GUID.
                AssetDatabase.CreateAsset(data, path);
                Undo.RecordObject(draft, "烘焙导航"); draft.bakedData = data; draft.bakedGeometrySha256 = geometryHash;
                EditorUtility.SetDirty(draft); AssetDatabase.SaveAssetIfDirty(draft); AssetDatabase.SaveAssetIfDirty(data);
                return data;
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        public static bool IsBakeCurrent(NavMeshAuthoringDraft draft)
        {
            try { return draft != null && draft.bakedData != null && draft.bakedGeometrySha256 == GeometryHash(draft); }
            catch { return false; }
        }

        public static void Export(NavMeshAuthoringDraft draft, string directory)
        {
            draft.Validate(true); VerifyImages(draft);
            string output = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string resources = Path.GetFullPath("Assets/Resources").TrimEnd(Path.DirectorySeparatorChar);
            if (output.Equals(resources, StringComparison.OrdinalIgnoreCase) || output.StartsWith(resources + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请导出到草稿目录，不要覆盖已安装Resources资源。");
            if (!IsBakeCurrent(draft)) throw new InvalidOperationException("草稿已变更，请先重新烘焙。");
            CheckSpawn(draft);
            var mesh = NavigationPolygonGeometry.Build(draft);
            try
            {
                var document = Document(draft);
                string obj = ToObj(mesh); string nav = document.ToString() + "\n"; string objHash = Sha(Encoding.UTF8.GetBytes(obj));
                var arguments = ServerBakeArguments(draft);
                string argumentJson = arguments.ToString() + "\n";
                var metadata = new JObject { ["format"] = "hand-authored-navmesh-source-v2", ["status"] = "draft",
                    ["mapKey"] = draft.mapKey, ["geometrySha256"] = objHash, ["authoringSha256"] = Sha(Encoding.UTF8.GetBytes(nav)),
                    ["serverBakeArgumentsSha256"] = Sha(Encoding.UTF8.GetBytes(argumentJson)),
                    ["coordinateSystem"] = "Unity Y-up; X/Z ground; OBJ has no axis conversion",
                    ["boundsConfirmedByAuthor"] = draft.boundsConfirmed, ["serverAcceptance"] = "NOT_RUN",
                    ["notes"] = "Shared source geometry and exposed inputs do not guarantee identical navigation. Unity NavMeshData is not a Detour MSET; validate paired spawn, obstacle, edge and route queries.",
                    ["agent"] = document["agent"].DeepClone(), ["bakeContract"] = document["bakeContract"].DeepClone() };
                Directory.CreateDirectory(output);
                Write(output, "world-y-up.obj", obj); Write(output, "navigation.json", nav);
                Write(output, "server-bake-args.json", argumentJson);
                Write(output, "source.json", metadata.ToString() + "\n");
                string command = "navmesh_baker.exe " + string.Join(" ", arguments.Values<string>().Select(a => "\"" + a + "\""));
                Write(output, "server-bake-command.txt", "Run from this exported folder with the server's matching UE5-double-ABI baker.\n" + command +
                    "\nMachine-readable arguments are in server-bake-args.json; source.json binds their SHA-256. The authored-flat profile requires a coplanar OBJ and restores its source-plane height.\n" +
                    "The server cell height 0.2 and region-merge implementation are server-only; Unity does not expose an equivalent cell-height setting. Matching shared parameters does not prove matching queries.\n");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        public static JObject Document(NavMeshAuthoringDraft d)
        {
            var tiles = new JArray(); foreach (var t in d.backgroundTiles) tiles.Add(new JObject { ["row"] = t.row, ["column"] = t.column,
                ["assetPath"] = t.assetPath, ["sha256"] = t.sha256, ["pixelWidth"] = t.pixelWidth, ["pixelHeight"] = t.pixelHeight });
            var polygons = new JArray(); foreach (var p in d.polygons) { var points = new JArray(); foreach (var v in p.points) points.Add(new JArray(v.x, v.y));
                polygons.Add(new JObject { ["name"] = p.name, ["blocked"] = p.blocked, ["points"] = points }); }
            return new JObject { ["format"] = "hand-authored-navmesh-v2", ["mapKey"] = d.mapKey, ["displayName"] = d.displayName,
                ["imageWidth"] = d.imageWidth, ["imageHeight"] = d.imageHeight, ["imagePixelOrigin"] = "north-west",
                ["worldRect"] = new JObject { ["x"] = d.worldRect.x, ["z"] = d.worldRect.y, ["width"] = d.worldRect.width, ["depth"] = d.worldRect.height },
                ["boundsConfirmedByAuthor"] = d.boundsConfirmed, ["spawnSet"] = d.spawnSet, ["spawn"] = new JArray(d.spawn.x, 0, d.spawn.y),
                ["agent"] = new JObject { ["typeId"] = d.agentTypeId, ["radius"] = d.agentRadius, ["height"] = d.agentHeight,
                    ["climb"] = d.agentClimb, ["slope"] = d.agentSlope, ["voxelSize"] = d.voxelSize },
                ["bakeContract"] = BakeContract(d), ["tiles"] = tiles, ["polygons"] = polygons };
        }

        private static JObject BakeContract(NavMeshAuthoringDraft draft)
        {
            // 按实际CLI字符串解析为double，与C++烘焙器使用相同的量化输入。
            double serverClimb = double.Parse(Number(draft.agentClimb), CultureInfo.InvariantCulture);
            double serverCellHeight = double.Parse(Number(NavMeshAuthoringDraft.ServerCellHeight), CultureInfo.InvariantCulture);
            double climbVoxels = Math.Floor(serverClimb / serverCellHeight);
            return new JObject {
                ["revision"] = NavMeshAuthoringDraft.BakeContractRevision,
                ["serverProfile"] = "authored-flat", ["tileSizeVoxels"] = NavMeshAuthoringDraft.TileSize,
                ["minRegionAreaWorld"] = NavMeshAuthoringDraft.MinRegionArea,
                ["unity"] = new JObject { ["overrideVoxelSize"] = true, ["overrideTileSize"] = true,
                    ["heightMesh"] = true, ["ledgeDropHeight"] = 0, ["maxJumpAcrossDistance"] = 0,
                    ["groundPointTolerancePerAxis"] = NavMeshAuthoringDraft.GroundPointTolerance,
                    ["cellHeight"] = "not exposed by Unity NavMeshBuildSettings" },
                ["serverOnly"] = new JObject { ["cellHeight"] = NavMeshAuthoringDraft.ServerCellHeight,
                    ["filterLedges"] = true, ["mergeRegionAreaVoxels"] = 400,
                    ["walkableClimbVoxels"] = climbVoxels, ["effectiveClimb"] = climbVoxels * serverCellHeight,
                    ["climbQuantization"] = "floor(agentClimb / cellHeight)",
                    ["heightPolicy"] = "authored-flat restores the original coplanar OBJ height" },
                ["queryEquivalence"] = "NOT_PROVEN; paired-query validation required" };
        }

        private static JArray ServerBakeArguments(NavMeshAuthoringDraft draft)
            => new JArray("--obj", "world-y-up.obj", "--out", "scene-nav.bin", "--profile", "authored-flat",
                "--cs", Number(draft.voxelSize), "--ch", Number(NavMeshAuthoringDraft.ServerCellHeight),
                "--agent-radius", Number(draft.agentRadius), "--agent-height", Number(draft.agentHeight),
                "--agent-climb", Number(draft.agentClimb), "--agent-slope", Number(draft.agentSlope),
                "--tile-size", NavMeshAuthoringDraft.TileSize.ToString(CultureInfo.InvariantCulture),
                "--min-region-area", Number(NavMeshAuthoringDraft.MinRegionArea), "--filter-ledges", "1",
                "--probe", Number(draft.spawn.x) + ",0," + Number(draft.spawn.y));

        private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        public static string GeometryHash(NavMeshAuthoringDraft draft)
        {
            var geometry = Document(draft);
            foreach (string key in new[] { "mapKey", "displayName", "boundsConfirmedByAuthor", "spawnSet", "spawn" }) geometry.Remove(key);
            return Sha(Encoding.UTF8.GetBytes(geometry.ToString()));
        }

        public static void CheckSpawn(NavMeshAuthoringDraft draft)
        {
            draft.Validate(true); VerifyImages(draft);
            if (!IsBakeCurrent(draft)) throw new InvalidOperationException("请先烘焙当前标注。");
            var instance = NavMesh.AddNavMeshData(draft.bakedData, new UnityEngine.Vector3(0, 20000, 0), Quaternion.identity);
            try
            {
                var filter = new NavMeshQueryFilter { agentTypeID = draft.agentTypeId, areaMask = NavMesh.AllAreas };
                if (!instance.valid || !NavMesh.SamplePosition(new UnityEngine.Vector3(draft.spawn.x, 20000, draft.spawn.y), out var hit, Mathf.Max(draft.agentHeight, draft.voxelSize * 2), filter) ||
                    !NavMeshAuthoringDraft.MatchesGroundPoint(new UnityEngine.Vector3(draft.spawn.x, 20000, draft.spawn.y), hit.position))
                    throw new InvalidOperationException("出生点不在当前NavMesh上，请在绿色区域内重新设置。");
            }
            finally { if (instance.valid) instance.Remove(); }
        }
        public static string Sha(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static void Write(string folder, string name, string text) => File.WriteAllText(Path.Combine(folder, name), text, new UTF8Encoding(false));

        private static string ToObj(Mesh mesh)
        {
            var b = new StringBuilder("# Hand-authored walkable geometry, Unity Y-up, obstacles subtracted\n");
            foreach (var v in mesh.vertices) b.AppendFormat(CultureInfo.InvariantCulture, "v {0:R} {1:R} {2:R}\n", v.x, v.y, v.z);
            var indices = mesh.triangles; for (int i = 0; i < indices.Length; i += 3) b.AppendFormat("f {0} {1} {2}\n", indices[i] + 1, indices[i + 1] + 1, indices[i + 2] + 1);
            return b.ToString();
        }

        private static void VerifyImages(NavMeshAuthoringDraft draft)
        {
            foreach (var tile in draft.backgroundTiles)
                if (tile.texture == null || !File.Exists(tile.assetPath) || Sha(File.ReadAllBytes(tile.assetPath)) != tile.sha256)
                    throw new InvalidOperationException("底图缺失或已变化，请重新选择底图后核对标注：" + tile.assetPath);
        }

        private static string ImportImage(string file, string draftPath)
        {
            string full = Path.GetFullPath(file); string project = Path.GetFullPath(".") + Path.DirectorySeparatorChar;
            if (full.StartsWith(project + "Assets" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                string existing = full.Substring(project.Length).Replace('\\', '/');
                AssetDatabase.ImportAsset(existing, ImportAssetOptions.ForceSynchronousImport);
                return existing;
            }
            string sha = Sha(File.ReadAllBytes(full));
            string folder = Path.GetDirectoryName(draftPath).Replace('\\', '/') + "/Sources";
            Directory.CreateDirectory(folder);
            string path = folder + "/" + sha.Substring(0, 12) + "-" + Path.GetFileName(full);
            if (!File.Exists(path)) File.Copy(full, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.mipmapEnabled = false; importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 8192; importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp; importer.SaveAndReimport();
            }
            return path;
        }

        private static void RequireNewDraftPath(string path)
        {
            string normalized = path.Replace('\\', '/');
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) || !normalized.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || File.Exists(path))
                throw new InvalidOperationException("请选择Assets内尚不存在的.asset草稿文件。");
            if (normalized.StartsWith("Assets/Resources/", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("草稿请保存在Resources之外。");
        }

        public static Vector2Int ReadPngSize(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var b = new byte[24]; if (stream.Read(b, 0, b.Length) != 24 || b[0] != 137 || b[1] != 80 || b[2] != 78 || b[3] != 71 ||
                    b[12] != 73 || b[13] != 72 || b[14] != 68 || b[15] != 82) throw new InvalidOperationException("需要有效PNG底图：" + path);
                int Read(int offset) => (b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3];
                int w = Read(16), h = Read(20); if (w < 1 || h < 1 || w > 32768 || h > 32768) throw new InvalidOperationException("底图尺寸无效或超过32768：" + path);
                return new Vector2Int(w, h);
            }
        }
    }
}
