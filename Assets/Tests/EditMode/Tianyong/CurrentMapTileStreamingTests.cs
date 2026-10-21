using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MmorpgClient.World.CurrentMaps;
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

        private static CurrentMapTilesManifest Manifest(string key = "main", int count = 1)
        {
            int grid = key == "main" ? 16 : 14;
            var manifest = new CurrentMapTilesManifest { schemaVersion = 1, deliveryStatus = "partial_native_tiles",
                key = key, sceneId = key == "main" ? 1u : key == "island" ? 2u : 3u,
                layoutSha256 = Sha, columns = grid, rows = grid, tilePixels = 4096,
                worldRect = new Rect(50, 0, 300, 300), tiles = new CurrentMapTilesManifest.Entry[count] };
            for (int i = 0; i < count; i++)
            {
                int row = i / grid + 1, column = i % grid + 1;
                manifest.tiles[i] = new CurrentMapTilesManifest.Entry { row = row, column = column,
                    resourcePath = $"World/CurrentMaps20261009/{key}/tiles/r{row:00}_c{column:00}", sha256 = Sha, bytes = 1024 };
            }
            return manifest;
        }

        private static string Source(CurrentMapTilesManifest manifest)
            => $"{{\"schemaVersion\":1,\"key\":\"{manifest.key}\",\"sceneId\":{manifest.sceneId},\"layoutSha256\":\"{Sha}\"}}";

        [TestCase("main", 16)]
        [TestCase("island", 14)]
        [TestCase("village", 14)]
        public void SparseAndEmptyDeliveriesMatchCurrentLayout(string key, int grid)
        {
            var manifest = Manifest(key, 2);
            manifest.tiles[1] = new CurrentMapTilesManifest.Entry { row = grid, column = grid,
                resourcePath = $"World/CurrentMaps20261009/{key}/tiles/r{grid:00}_c{grid:00}", sha256 = Sha, bytes = 1234 };
            var parsed = CurrentMapTilesManifest.Parse(key, Source(manifest), JsonUtility.ToJson(manifest));
            Assert.That(parsed.tiles.Length, Is.EqualTo(2));
            manifest.tiles = Array.Empty<CurrentMapTilesManifest.Entry>();
            Assert.DoesNotThrow(() => CurrentMapTilesManifest.Parse(key, Source(manifest), JsonUtility.ToJson(manifest)));
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
            var manifest = Manifest(count: 2);
            string source = Source(manifest);
            if (problem == "layout-sha") manifest.layoutSha256 = new string('a', 64);
            if (problem == "source-sha") source = source.Replace(Sha, new string('b', 64));
            if (problem == "scene") manifest.sceneId = 3;
            if (problem == "grid") manifest.columns = manifest.rows = 14;
            if (problem == "path") manifest.tiles[0].resourcePath = "World/CityTiles4K/old/tiles/r01_c01";
            if (problem == "duplicate") manifest.tiles[1] = manifest.tiles[0];
            if (problem == "non-native") manifest.tilePixels = 2048;
            if (problem == "tile-sha") manifest.tiles[0].sha256 = "not-a-sha";
            if (problem == "status") manifest.deliveryStatus = "accepted_complete";
            if (problem == "world-rect") manifest.worldRect = new Rect(float.NaN, 0, 300, 300);
            Assert.Throws<InvalidOperationException>(() => CurrentMapTilesManifest.Parse("main", source, JsonUtility.ToJson(manifest)));
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
            for (int row = 1; row < manifest.rows; row++)
            for (int column = 1; column < manifest.columns; column++)
            {
                var rect = manifest.TileWorldRect(row, column);
                Assert.That(rect.xMax, Is.EqualTo(manifest.TileWorldRect(row, column + 1).xMin));
                Assert.That(rect.yMin, Is.EqualTo(manifest.TileWorldRect(row + 1, column).yMax));
            }
        }

        [Test]
        public void SelectionCapsWideViewAtEightAndPrioritizesVisibleCenter()
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
                Assert.That(selected, Is.EqualTo(new[] { 5, 6, 7, 8, 9, 10, 11, 12 }));
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
        public void GlobalBudgetIncludesAbandonedInFlightRequestsAndSharedOwners()
        {
            Assert.That(Cache.Count, Is.Zero);
            var leases = new List<object>();
            try
            {
                for (int i = 0; i < 8; i++) leases.Add(FakeLease("__budget_" + i, i >= 2, i == 0 ? 0 : 1));
                Assert.That(CurrentMapTileStreaming.GlobalLeaseCount, Is.EqualTo(8));
                Assert.That(CurrentMapTileStreaming.GlobalPendingCount, Is.EqualTo(2));
                Assert.That(Static("Acquire", "__blocked_budget"), Is.Null);
                var shared = Static("Acquire", "__budget_1");
                Assert.That(shared, Is.SameAs(leases[1]));
                Assert.That(Field(shared, "References"), Is.EqualTo(2));
                Static("Evict", leases[0]);
                Assert.That(Cache.Count, Is.EqualTo(8), "An abandoned request cannot be cancelled or stop counting early.");
                Set(leases[0], "Completed", true);
                Static("Evict", leases[0]);
                Assert.That(Cache.Count, Is.EqualTo(7));
                // 即使未达到 8 块，总并发仍不能超过 2。
                var extra = FakeLease("__pending_extra", false);
                leases.Add(extra);
                Set(leases[7], "References", 0); Static("Evict", leases[7]);
                Assert.That(Cache.Count, Is.EqualTo(7));
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
            const string path = "World/CurrentMaps20261009/__shared_async_fixture";
            var firstRoot = new GameObject("CurrentTileFirstMapTest");
            var secondRoot = new GameObject("CurrentTileSecondMapTest");
            try
            {
                object lease = null;
                foreach (var root in new[] { firstRoot, secondRoot })
                {
                    var stream = root.AddComponent<CurrentMapTileStreaming>();
                    var manifest = Manifest();
                    manifest.tiles[0].resourcePath = path;
                    Invoke(stream, "Prepare", manifest);
                    Invoke(stream, "TickView", new Rect(50, 280, 20, 20), 10f);
                    var current = Field(((Array)Field(stream, "_tiles")).GetValue(0), "Lease");
                    if (lease != null) Assert.That(current, Is.SameAs(lease));
                    lease = current;
                }
                Assert.That(Field(lease, "References"), Is.EqualTo(2));
                Object.DestroyImmediate(firstRoot);
                Assert.That(Field(lease, "References"), Is.EqualTo(1));
                float deadline = Time.realtimeSinceStartup + 10f;
                while (!(bool)Field(lease, "Completed") && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(Field(lease, "Completed"), Is.True);
                Assert.That(Cache.Contains(path), Is.True, "Completion belongs to the remaining map.");
                Object.DestroyImmediate(secondRoot);
                Assert.That(Cache.Contains(path), Is.False);
            }
            finally { Object.DestroyImmediate(firstRoot); Object.DestroyImmediate(secondRoot); }
        }

        [UnityTest]
        public IEnumerator DestroyedOwnerKeepsPendingBudgetUntilCompletionAndThenEvicts()
        {
            Assert.That(Cache.Count, Is.Zero);
            var go = new GameObject("CurrentTilePendingDestroyTest");
            var stream = go.AddComponent<CurrentMapTileStreaming>();
            var manifest = Manifest();
            // 只在隔离测试中使用不可能交付的路径来触发真实异步完成，无需发布伪 4K 图片。
            manifest.tiles[0].resourcePath = "World/CurrentMaps20261009/__missing_async_fixture";
            Invoke(stream, "Prepare", manifest);
            Invoke(stream, "TickView", new Rect(50, 280, 20, 20), 10f);
            Object.DestroyImmediate(go);
            float deadline = Time.realtimeSinceStartup + 10f;
            while (Cache.Count != 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(Cache.Count, Is.Zero);
        }
    }
}
