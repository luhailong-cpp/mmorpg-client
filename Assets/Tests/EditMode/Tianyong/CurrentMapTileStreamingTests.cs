using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MmorpgClient.World.CurrentMaps;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Object = UnityEngine.Object;
    using Vector3 = UnityEngine.Vector3;

    public sealed class CurrentMapTileStreamingTests
    {
        private const string Sha = "47485b823db7dbe4e63883f9ae6d759f3f2fbb5897c72f16fa64e921bde6ccd7";
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Type StreamType = typeof(CurrentMapTileStreaming);
        private static IDictionary Cache => (IDictionary)StreamType.GetField("Cache", PrivateStatic).GetValue(null);
        private static object Field(object owner, string name) => owner.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(owner, value);
        private static object Invoke(object owner, string name, params object[] args)
            => owner.GetType().GetMethod(name, PrivateInstance).Invoke(owner, args);
        private static object Static(string name, params object[] args) => StreamType.GetMethod(name, PrivateStatic).Invoke(null, args);

        private static string RootFor(string key) => $"World/QdaoMaps20261102/{key}/test-rev/spring/{Sha}/";

        private static CurrentMapTilesManifest Manifest(string key = "main", int count = 1, int grid = 0)
        {
            if (grid <= 0) grid = key == "main" ? 16 : 14;
            string root = RootFor(key);
            var manifest = new CurrentMapTilesManifest { schemaVersion = 2, deliveryStatus = "formal_runtime_tiles",
                key = key, sceneId = key == "main" ? 1u : key == "island" ? 2u : 3u,
                revision = "test-rev", appearance = "spring", resourceRoot = root,
                layoutSha256 = Sha, columns = grid, rows = grid, tilePixels = 1024,
                worldRect = new Rect(50, 0, 300, 300),
                effectiveWorldRect = new MapDeliveryRect { x = 50, y = 0, width = 300, height = 300 },
                imageRect = new MapDeliveryRect { x = 0, y = 0, width = grid * 1024, height = grid * 1024 },
                referenceWidth = grid * 1024, referenceHeight = grid * 1024,
                tiles = new CurrentMapTilesManifest.Entry[count] };
            for (int i = 0; i < count; i++)
            {
                int row = i / grid + 1, column = i % grid + 1;
                manifest.tiles[i] = new CurrentMapTilesManifest.Entry { row = row, column = column,
                    resourcePath = $"{root}tiles/r{row:00}_c{column:00}", sha256 = Sha,
                    nativeSha256 = Sha, nativePixels = 1024, bytes = 1024 };
            }
            return manifest;
        }

        private static string Source(CurrentMapTilesManifest manifest)
            => $"{{\"schemaVersion\":2,\"key\":\"{manifest.key}\",\"sceneId\":{manifest.sceneId}," +
               $"\"revision\":\"{manifest.revision}\",\"appearance\":\"{manifest.appearance}\"," +
               $"\"artStatus\":\"formal_tiles_overview\",\"layoutSha256\":\"{Sha}\"," +
               $"\"worldRect\":{{\"x\":50,\"y\":0,\"width\":300,\"height\":300}}," +
               $"\"effectiveWorldRect\":{{\"x\":50,\"y\":0,\"width\":300,\"height\":300}}}}";

        [UnityTest]
        public IEnumerator FestivalVisibleEvidenceRequiresActualActiveLoadedTileQuads()
        {
            Assert.That(Cache.Count, Is.Zero);
            var root = new GameObject("FestivalVisibleTileEvidence");
            var stream = root.AddComponent<CurrentMapTileStreaming>();
            try
            {
                stream.Configure(CurrentMapCatalog.Find("map01001").ForAppearance("spring"));
                Assert.That(stream.Manifest.tiles.Length, Is.EqualTo(2));
                Invoke(stream, "SelectTiles", stream.Manifest.WorldRect, Time.unscaledTime);
                Assert.That(stream.VisibleTileCount, Is.EqualTo(2));
                Assert.That(stream.LoadedVisibleTilePaths, Is.Empty, "底图与manifest存在不能冒充运行块已加载。");
                float deadline = Time.realtimeSinceStartup + 15f;
                while (stream.LoadedVisibleTilePaths.Length < 2 && Time.realtimeSinceStartup < deadline)
                {
                    Invoke(stream, "TickView", stream.Manifest.WorldRect, Time.unscaledTime);
                    yield return null;
                }
                Assert.That(stream.LoadedVisibleTilePaths.Length, Is.EqualTo(2));
                Assert.That(stream.PendingTileCount, Is.Zero);
                root.SetActive(false);
                Assert.That(stream.LoadedVisibleTilePaths, Is.Empty, "被隐藏的旧图不能计为当前可见运行块。");
                root.SetActive(true);
                Assert.That(stream.LoadedVisibleTilePaths.Length, Is.EqualTo(2));
            }
            finally { stream.ReleaseAll(); Object.DestroyImmediate(root); }
        }

        [TestCase("main", 4)]
        [TestCase("island", 4)]
        [TestCase("village", 4)]
        public void CompleteFormalDeliveriesMatchCurrentLayoutAndRejectPartial(string key, int grid)
        {
            var manifest = Manifest(key, grid * grid, grid);
            var parsed = CurrentMapTilesManifest.Parse(key, Source(manifest), JsonUtility.ToJson(manifest),
                manifest.resourceRoot, manifest.revision, manifest.appearance);
            Assert.That(parsed.tiles.Length, Is.EqualTo(grid * grid));
            manifest.tiles = Array.Empty<CurrentMapTilesManifest.Entry>();
            Assert.Throws<InvalidOperationException>(() => CurrentMapTilesManifest.Parse(key, Source(manifest), JsonUtility.ToJson(manifest),
                manifest.resourceRoot, manifest.revision, manifest.appearance));
        }

        [TestCase("layout-sha")]
        [TestCase("source-sha")]
        [TestCase("scene")]
        [TestCase("grid")]
        [TestCase("path")]
        [TestCase("duplicate")]
        [TestCase("non-native")]
        [TestCase("tile-sha")]
        [TestCase("status")]
        [TestCase("world-rect")]
        public void WrongLayoutOrInvalidTileCannotActivate(string problem)
        {
            var manifest = Manifest("main", 16, 4);
            string source = Source(manifest);
            if (problem == "layout-sha") manifest.layoutSha256 = new string('a', 64);
            if (problem == "source-sha") source = source.Replace(Sha, new string('b', 64));
            if (problem == "scene") manifest.sceneId = 3;
            if (problem == "grid") manifest.columns = manifest.rows = 16;
            if (problem == "path") manifest.tiles[0].resourcePath = "World/QdaoMaps20261102/old/tiles/r01_c01";
            if (problem == "duplicate") manifest.tiles[1] = manifest.tiles[0];
            if (problem == "non-native") manifest.tilePixels = 2048;
            if (problem == "tile-sha") manifest.tiles[0].sha256 = "not-a-sha";
            if (problem == "status") manifest.deliveryStatus = "partial_native_tiles";
            if (problem == "world-rect") manifest.worldRect = new Rect(float.NaN, 0, 300, 300);
            Assert.Throws<InvalidOperationException>(() => CurrentMapTilesManifest.Parse("main", source, JsonUtility.ToJson(manifest),
                manifest.resourceRoot, manifest.revision, manifest.appearance));
        }

        [TestCase("main")]
        [TestCase("island")]
        public void NorthWestCoordinatesAndEveryAdjacentBoundaryAgree(string key)
        {
            var manifest = Manifest(key);
            Assert.That(manifest.TileWorldRect(1, 1).xMin, Is.EqualTo(50));
            Assert.That(manifest.TileWorldRect(1, 1).yMax, Is.EqualTo(300));
            Assert.That(manifest.TileWorldRect(manifest.rows, manifest.columns).xMax, Is.EqualTo(350));
            Assert.That(manifest.TileWorldRect(manifest.rows, manifest.columns).yMin, Is.EqualTo(0));
            for (int row = 1; row <= manifest.rows; row++)
            for (int column = 1; column <= manifest.columns; column++)
            {
                var rect = manifest.TileWorldRect(row, column);
                Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(manifest.WorldRect.xMin));
                Assert.That(rect.xMax, Is.LessThanOrEqualTo(manifest.WorldRect.xMax));
                Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(manifest.WorldRect.yMin));
                Assert.That(rect.yMax, Is.LessThanOrEqualTo(manifest.WorldRect.yMax));
                if (column < manifest.columns)
                    Assert.That(rect.xMax, Is.EqualTo(manifest.TileWorldRect(row, column + 1).xMin));
                if (row < manifest.rows)
                    Assert.That(rect.yMin, Is.EqualTo(manifest.TileWorldRect(row + 1, column).yMax));
            }
        }

        [Test]
        public void SelectionCapsWideViewAtSixAndPrioritizesVisibleCenter()
        {
            var go = new GameObject("CurrentTileSelectionTest");
            try
            {
                var stream = go.AddComponent<CurrentMapTileStreaming>();
                var manifest = Manifest(count: 16);
                Invoke(stream, "Prepare", manifest);
                Invoke(stream, "SelectTiles", new Rect(50, 280, 300, 20), 100f);
                var selected = new List<int>();
                foreach (var tile in (Array)Field(stream, "_tiles"))
                    if ((bool)Field(tile, "Wanted")) selected.Add(((CurrentMapTilesManifest.Entry)Field(tile, "Entry")).column);
                Assert.That(selected, Is.EqualTo(new[] { 6, 7, 8, 9, 10, 11 }));
                Invoke(stream, "SelectTiles", new Rect(70, 283, 10, 10), 101f);
                var ranked = (IList)Field(stream, "_ranked");
                Assert.That(((CurrentMapTilesManifest.Entry)Field(ranked[0], "Entry")).column, Is.EqualTo(2));
                Invoke(stream, "TickView", new Rect(1000, 1000, 10, 10), 200f);
                Assert.That(stream.PendingTileCount, Is.Zero);
                Assert.That(stream.Manifest, Is.SameAs(manifest), "Uncovered views do not discard the sparse layer.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static object FakeLease(string path, bool complete, int references = 1)
        {
            var lease = Activator.CreateInstance(StreamType.GetNestedType("Lease", BindingFlags.NonPublic), true);
            Set(lease, "Path", path); Set(lease, "Completed", complete); Set(lease, "References", references);
            Cache.Add(path, lease);
            return lease;
        }

        [Test]
        public void StreamingCandidatesUseHexNeighborhoodBeyondOneSquareTilePadding()
        {
            var go = new GameObject("CurrentTileHexCandidateTest");
            try
            {
                var stream = go.AddComponent<CurrentMapTileStreaming>();
                var manifest = Manifest();
                manifest.tiles[0].row = 8;
                manifest.tiles[0].column = 10;
                manifest.tiles[0].resourcePath = RootFor("main") + "tiles/r08_c10";
                Invoke(stream, "Prepare", manifest);
                Invoke(stream, "SelectTiles", new Rect(189, 158, 2, 2), 100f);
                var tile = ((Array)Field(stream, "_tiles")).GetValue(0);
                Assert.That(Field(tile, "Visible"), Is.False);
                Assert.That(Field(tile, "Wanted"), Is.True, "The rectangular tile overlaps a neighboring server hex and is a prefetch candidate.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [TestCase(184f, 6f, 9)]
        [TestCase(190f, -6f, 7)]
        public void NeighbourPrefetchFollowsMovementWithoutDisplacingVisibleTile(float startX, float movement, int nextColumn)
        {
            var go = new GameObject("CurrentTileDirectionalPrefetchTest");
            try
            {
                var stream = go.AddComponent<CurrentMapTileStreaming>();
                Invoke(stream, "Prepare", Manifest(count: 256));
                Invoke(stream, "SelectTiles", new Rect(startX - 1, 158, 2, 2), 100f);
                Invoke(stream, "SelectTiles", new Rect(startX + movement - 1, 158, 2, 2), 101f);
                // 微小反向抖动不改变预取方向。
                Invoke(stream, "SelectTiles", new Rect(startX + movement - Math.Sign(movement) * .1f - 1, 158, 2, 2), 102f);
                var ranked = (IList)Field(stream, "_ranked");
                var visible = (CurrentMapTilesManifest.Entry)Field(ranked[0], "Entry");
                var neighbour = (CurrentMapTilesManifest.Entry)Field(ranked[1], "Entry");
                Assert.That((visible.row, visible.column), Is.EqualTo((8, 8)));
                Assert.That((neighbour.row, neighbour.column), Is.EqualTo((8, nextColumn)));
                int wanted = 0;
                foreach (var tile in (Array)Field(stream, "_tiles")) if ((bool)Field(tile, "Wanted")) wanted++;
                Assert.That(wanted, Is.EqualTo(6));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ResidentHysteresisPreventsChurnButYieldsToDistantView()
        {
            Assert.That(Cache.Count, Is.Zero);
            var go = new GameObject("CurrentTileHysteresisTest");
            CurrentMapTileStreaming stream = null;
            try
            {
                stream = go.AddComponent<CurrentMapTileStreaming>();
                Invoke(stream, "Prepare", Manifest(count: 16));
                Invoke(stream, "SelectTiles", new Rect(50, 280, 300, 20), 100f);
                var tiles = (Array)Field(stream, "_tiles");
                foreach (var tile in tiles)
                    if ((bool)Field(tile, "Wanted"))
                        Set(tile, "Lease", FakeLease(((CurrentMapTilesManifest.Entry)Field(tile, "Entry")).resourcePath, true));
                Invoke(stream, "SelectTiles", new Rect(60, 280, 300, 20), 101f);
                for (int i = 0; i < tiles.Length; i++)
                    Assert.That(Field(tiles.GetValue(i), "Wanted"), Is.EqualTo(i >= 5 && i <= 10), "Resident boundary should not churn.");
                Invoke(stream, "SelectTiles", new Rect(125, 280, 300, 20), 102f);
                Assert.That(Field(tiles.GetValue(5), "Wanted"), Is.False);
                Assert.That(Field(tiles.GetValue(14), "Wanted"), Is.True);
            }
            finally { stream?.ReleaseAll(); Object.DestroyImmediate(go); }
        }

        [UnityTest]
        public IEnumerator FullBudgetStartsNewVisibleLoadBeforeReleaseDelay()
        {
            Assert.That(Cache.Count, Is.Zero);
            var go = new GameObject("CurrentTilePressureTest");
            var stream = go.AddComponent<CurrentMapTileStreaming>();
            try
            {
                var manifest = Manifest(count: 7);
                manifest.tiles[6].resourcePath = "World/QdaoMaps20261102/__pressure_async_fixture";
                Invoke(stream, "Prepare", manifest);
                var tiles = (Array)Field(stream, "_tiles");
                for (int i = 0; i < 6; i++)
                {
                    var tile = tiles.GetValue(i);
                    Set(tile, "Lease", FakeLease(manifest.tiles[i].resourcePath, true));
                    // 已显示的完成块夹具，无需为预算测试分配六张 4K 纹理。
                    Set(tile, "Quad", new GameObject("CompletedTile" + i));
                    Set(tile, "LastWanted", 100f);
                }
                Invoke(stream, "TickView", new Rect(163, 282, 16, 16), 100.1f);
                Assert.That(Field(tiles.GetValue(6), "Lease"), Is.Not.Null, "The new visible load starts before the two-second delay.");
                Assert.That(Field(tiles.GetValue(0), "Lease"), Is.Null, "The farthest unwanted completed tile yields its slot.");
                Assert.That(Field(tiles.GetValue(1), "Lease"), Is.Not.Null, "Only the slot needed for the visible request is evicted.");
                Assert.That(CurrentMapTileStreaming.GlobalLeaseCount, Is.EqualTo(6));
                Assert.That(CurrentMapTileStreaming.GlobalPendingCount, Is.LessThanOrEqualTo(2));
                stream.ReleaseAll();
                float deadline = Time.realtimeSinceStartup + 10f;
                while (Cache.Count != 0 && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(Cache.Count, Is.Zero);
            }
            finally { stream.ReleaseAll(); Object.DestroyImmediate(go); }
        }

        [Test]
        public void GlobalBudgetIncludesAbandonedInFlightRequestsAndSharedOwners()
        {
            Assert.That(Cache.Count, Is.Zero);
            var leases = new List<object>();
            try
            {
                for (int i = 0; i < 6; i++) leases.Add(FakeLease("__budget_" + i, i >= 2, i == 0 ? 0 : 1));
                Assert.That(CurrentMapTileStreaming.GlobalLeaseCount, Is.EqualTo(6));
                Assert.That(CurrentMapTileStreaming.GlobalPendingCount, Is.EqualTo(2));
                Assert.That(Static("Acquire", "__blocked_budget"), Is.Null);
                var shared = Static("Acquire", "__budget_1");
                Assert.That(shared, Is.SameAs(leases[1]));
                Assert.That(Field(shared, "References"), Is.EqualTo(2));
                Static("Evict", leases[0]);
                Assert.That(Cache.Count, Is.EqualTo(6), "An abandoned request cannot be cancelled or stop counting early.");
                Set(leases[0], "Completed", true);
                Static("Evict", leases[0]);
                Assert.That(Cache.Count, Is.EqualTo(5));
                // 即使未达到 6 块，总并发仍不能超过 2。
                var extra = FakeLease("__pending_extra", false);
                leases.Add(extra);
                Set(leases[5], "References", 0); Static("Evict", leases[5]);
                Assert.That(Cache.Count, Is.EqualTo(5));
                Assert.That(Static("Acquire", "__blocked_concurrency"), Is.Null);
            }
            finally
            {
                foreach (var lease in leases) { Set(lease, "References", 0); Set(lease, "Completed", true); Static("Evict", lease); }
            }
        }

        [Test]
        public void FailedTileCoolsDownWithoutRemovingLayoutOrOtherTiles()
        {
            var go = new GameObject("CurrentTileFailureTest");
            try
            {
                var stream = go.AddComponent<CurrentMapTileStreaming>();
                var manifest = Manifest(count: 2);
                Invoke(stream, "Prepare", manifest);
                var tiles = (Array)Field(stream, "_tiles");
                var failed = tiles.GetValue(0);
                Set(failed, "Lease", FakeLease(manifest.tiles[0].resourcePath, true));
                var other = FakeLease(manifest.tiles[1].resourcePath, false);
                Set(tiles.GetValue(1), "Lease", other);
                LogAssert.Expect(LogType.Error, "[CurrentMapTiles] 分片缺失或导入格式错误：" + manifest.tiles[0].resourcePath + "；保留当前布局底图。");
                Invoke(stream, "UpdateCompleted", 100f);
                Assert.That(Field(failed, "Lease"), Is.Null);
                Assert.That(Field(failed, "RetryAfter"), Is.EqualTo(110f));
                Assert.That(Field(tiles.GetValue(1), "Lease"), Is.SameAs(other));
                Invoke(stream, "SelectTiles", new Rect(50, 280, 40, 20), 101f);
                Assert.That(Field(failed, "Wanted"), Is.False);
                stream.ReleaseAll();
                Assert.That(Cache.Contains(manifest.tiles[1].resourcePath), Is.True);
                Set(other, "Completed", true); Static("Evict", other);
                Assert.That(Cache.Count, Is.Zero);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void BriefViewBoundaryCrossingRetainsLeaseThenReleasesAfterTwoSeconds()
        {
            var go = new GameObject("CurrentTileReleaseDelayTest");
            object lease = null;
            try
            {
                var stream = go.AddComponent<CurrentMapTileStreaming>();
                var manifest = Manifest();
                Invoke(stream, "Prepare", manifest);
                var tile = ((Array)Field(stream, "_tiles")).GetValue(0);
                lease = FakeLease(manifest.tiles[0].resourcePath, false);
                Set(tile, "Lease", lease);
                Set(tile, "LastWanted", 10f);
                Invoke(stream, "TickView", new Rect(1000, 1000, 10, 10), 11f);
                Assert.That(Field(tile, "Lease"), Is.SameAs(lease));
                Invoke(stream, "TickView", new Rect(1000, 1000, 10, 10), 12.1f);
                Assert.That(Field(tile, "Lease"), Is.Null);
                Assert.That(Field(lease, "References"), Is.Zero);
                Assert.That(Cache.Contains(manifest.tiles[0].resourcePath), Is.True, "The unfinished request still counts globally.");
            }
            finally
            {
                Object.DestroyImmediate(go);
                if (lease != null) { Set(lease, "Completed", true); Static("Evict", lease); }
            }
        }

        [Test]
        public void QuadUsesNorthUpGeometryAndDisposalReleasesItsMaterial()
        {
            var go = new GameObject("CurrentTileQuadTest");
            var texture = new Texture2D(2, 2); // 几何测试夹具，不能作为导入验收。
            try
            {
                var stream = go.AddComponent<CurrentMapTileStreaming>();
                var manifest = Manifest();
                Invoke(stream, "Prepare", manifest);
                var tile = ((Array)Field(stream, "_tiles")).GetValue(0);
                Invoke(stream, "CreateQuad", tile, texture);
                var quad = (GameObject)Field(tile, "Quad");
                var material = (Material)Field(tile, "Material");
                Assert.That(quad.transform.localPosition.y, Is.EqualTo(-.04f));
                Assert.That(quad.transform.TransformDirection(Vector3.up).z, Is.EqualTo(1).Within(.00001f));
                Assert.That(quad.GetComponent<Collider>(), Is.Null);
                Assert.That(material.renderQueue, Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.Geometry));
                stream.ReleaseAll();
                Assert.That(quad == null, Is.True);
                Assert.That(material == null, Is.True);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(texture); }
        }

        [UnityTest]
        public IEnumerator NewOwnerKeepsSharedPendingLeaseAfterPreviousMapDisposal()
        {
            const string path = "World/QdaoMaps20261102/__shared_async_fixture";
            TianyongMapInstance firstMap = null;
            TianyongMapInstance secondMap = null;
            try
            {
                firstMap = CurrentMapBuilder.Build(null, CurrentMapCatalog.Find("main"));
                secondMap = CurrentMapBuilder.Build(null, CurrentMapCatalog.Find("main"));
                object lease = null;
                foreach (var map in new[] { firstMap, secondMap })
                {
                    var stream = map.Root.GetComponent<CurrentMapTileStreaming>();
                    Assert.That(stream, Is.Not.Null);
                    stream.ReleaseAll();
                    var manifest = Manifest();
                    manifest.tiles[0].resourcePath = path;
                    Invoke(stream, "Prepare", manifest);
                    Invoke(stream, "TickView", new Rect(50, 280, 20, 20), 10f);
                    var current = Field(((Array)Field(stream, "_tiles")).GetValue(0), "Lease");
                    if (lease != null) Assert.That(current, Is.SameAs(lease));
                    lease = current;
                }
                Assert.That(Field(lease, "References"), Is.EqualTo(2));
                // 正式地图所有者的Dispose先ReleaseAll再销毁root；EditMode不能假定未Awake组件会收到OnDestroy。
                firstMap.Dispose();
                Assert.That(Field(lease, "References"), Is.EqualTo(1));
                float deadline = Time.realtimeSinceStartup + 10f;
                while (!(bool)Field(lease, "Completed") && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(Field(lease, "Completed"), Is.True);
                Assert.That(Cache.Contains(path), Is.True, "Completion belongs to the remaining map.");
                secondMap.Dispose();
                Assert.That(Cache.Contains(path), Is.False);
            }
            finally { firstMap?.Dispose(); secondMap?.Dispose(); }
        }

        [UnityTest]
        public IEnumerator DestroyedOwnerKeepsPendingBudgetUntilCompletionAndThenEvicts()
        {
            Assert.That(Cache.Count, Is.Zero);
            TianyongMapInstance map = null;
            try
            {
                map = CurrentMapBuilder.Build(null, CurrentMapCatalog.Find("main"));
                var stream = map.Root.GetComponent<CurrentMapTileStreaming>();
                Assert.That(stream, Is.Not.Null);
                stream.ReleaseAll();
                var manifest = Manifest();
                // 只在隔离测试中使用不可能交付的路径触发真实异步完成，不发布伪4K图片。
                manifest.tiles[0].resourcePath = "World/QdaoMaps20261102/__missing_async_fixture";
                Invoke(stream, "Prepare", manifest);
                Invoke(stream, "TickView", new Rect(50, 280, 20, 20), 10f);
                var lease = Field(((Array)Field(stream, "_tiles")).GetValue(0), "Lease");
                Assert.That(lease, Is.Not.Null);
                map.Dispose();
                Assert.That(Field(lease, "References"), Is.Zero);
                Assert.That(Cache.Contains(manifest.tiles[0].resourcePath), Is.EqualTo(!(bool)Field(lease, "Completed")),
                    "已无引用的真实在途请求直到完成仍占预算，不能因销毁提前取消计数。");
                float deadline = Time.realtimeSinceStartup + 10f;
                while (Cache.Count != 0 && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(Cache.Count, Is.Zero);
            }
            finally { map?.Dispose(); }
        }

        [Test]
        public void EffectiveTileQuadClipsRightBottomPaddingAndInsetsOnlyPaddedBilinearEdges()
        {
            const float tileSpan = 58.02734375f;
            var world = new Rect(0f, -56.2139892578125f, 6f * tileSpan, 8f * tileSpan);
            var effective = new Rect(0f, 0f, 340.00396728515625f, 408.0047607421875f);

            // 内部满块 r01_c01：不裁切，UV 保持精确 [0,1]×[0,1]。
            var interior = new Rect(0f, world.yMax - tileSpan, tileSpan, tileSpan);
            Assert.That(CurrentMapTileStreaming.TryComputeEffectiveTileQuad(interior, effective, 1024,
                out var q1, out var o1, out var s1), Is.True);
            Assert.That(q1, Is.EqualTo(interior));
            Assert.That(o1, Is.EqualTo(Vector2.zero));
            Assert.That(s1, Is.EqualTo(Vector2.one));

            // 右边缘块 r01_c06（有效宽 880/1024）：左/上/下保持无缝，右缘内缩半纹素避开 RGB24 填充黑边。
            var rightEdge = new Rect(5f * tileSpan, world.yMax - tileSpan, tileSpan, tileSpan);
            Assert.That(CurrentMapTileStreaming.TryComputeEffectiveTileQuad(rightEdge, effective, 1024,
                out var qR, out var oR, out var sR), Is.True);
            Assert.That(qR.xMin, Is.EqualTo(rightEdge.xMin));
            Assert.That(qR.xMax, Is.EqualTo(effective.xMax).Within(1e-4f));
            Assert.That(oR.x, Is.EqualTo(0f));
            Assert.That(oR.y, Is.EqualTo(0f));
            Assert.That(sR.x, Is.EqualTo(879.5f / 1024f).Within(1e-5f));
            Assert.That(sR.y, Is.EqualTo(1f));

            // 底边缘块 r08_c01（有效高 32/1024）：仅底缘内缩半纹素。
            var bottomEdge = new Rect(0f, world.yMin, tileSpan, tileSpan);
            Assert.That(CurrentMapTileStreaming.TryComputeEffectiveTileQuad(bottomEdge, effective, 1024,
                out var qB, out var oB, out var sB), Is.True);
            Assert.That(qB.yMin, Is.EqualTo(effective.yMin).Within(1e-4f));
            Assert.That(qB.yMax, Is.EqualTo(bottomEdge.yMax).Within(1e-4f));
            Assert.That(oB.x, Is.EqualTo(0f));
            Assert.That(oB.y, Is.EqualTo(992.5f / 1024f).Within(1e-5f));
            Assert.That(sB.x, Is.EqualTo(1f));
            Assert.That(oB.y + sB.y, Is.EqualTo(1f).Within(1e-5f));

            // 右下角块 r08_c06（有效 880×32）：右缘与底缘同时内缩半纹素，内侧拼缝 (u=0, v=1) 保持精确。
            var corner = new Rect(5f * tileSpan, world.yMin, tileSpan, tileSpan);
            Assert.That(CurrentMapTileStreaming.TryComputeEffectiveTileQuad(corner, effective, 1024,
                out var qC, out var oC, out var sC), Is.True);
            Assert.That(qC.xMax, Is.EqualTo(effective.xMax).Within(1e-4f));
            Assert.That(qC.yMin, Is.EqualTo(effective.yMin).Within(1e-4f));
            Assert.That(oC.x, Is.EqualTo(0f));
            Assert.That(oC.x + sC.x, Is.EqualTo(879.5f / 1024f).Within(1e-5f));
            Assert.That(oC.y, Is.EqualTo(992.5f / 1024f).Within(1e-5f));
            Assert.That(oC.y + sC.y, Is.EqualTo(1f).Within(1e-5f));

            // 完全落在有效范围外的切片应返回 false，不生成填充块。
            var outside = new Rect(400f, -120f, tileSpan, tileSpan);
            Assert.That(CurrentMapTileStreaming.TryComputeEffectiveTileQuad(outside, effective, 1024,
                out _, out _, out _), Is.False);
        }
    }
}
