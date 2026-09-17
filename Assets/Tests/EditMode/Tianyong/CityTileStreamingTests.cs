using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class CityTileStreamingTests
    {
        private static CityTileManifest Manifest(int columns = 6, int rows = 6)
        {
            var result = new CityTileManifest
            {
                columns = columns, rows = rows,
                worldRect = new Rect(50f, 0f, 300f * columns / rows, 300f),
                tiles = new string[columns * rows]
            };
            for (int i = 0; i < result.tiles.Length; i++)
                result.tiles[i] = $"World/CityTiles4K/test/day/tiles/r{i / columns + 1:00}_c{i % columns + 1:00}";
            return result;
        }

        [TestCase(3, 3, 12288, 12288)]
        [TestCase(5, 4, 20480, 16384)]
        [TestCase(6, 6, 24576, 24576)]
        public void FinalResolutionComesFromActualGrid(int columns, int rows, int width, int height)
        {
            var manifest = Manifest(columns, rows);
            Assert.That(manifest.Validate(out _), Is.True);
            Assert.That(manifest.PixelWidth, Is.EqualTo(width));
            Assert.That(manifest.PixelHeight, Is.EqualTo(height));
        }

        [Test]
        public void IncompleteDuplicateAndNon4KDeliveriesCannotActivate()
        {
            var manifest = Manifest();
            manifest.tiles[3] = null;
            Assert.That(manifest.Validate(out _), Is.False);
            manifest = Manifest();
            manifest.tiles[3] = manifest.tiles[0].ToUpperInvariant();
            Assert.That(manifest.Validate(out _), Is.False);
            manifest = Manifest();
            manifest.tilePixels = 1024;
            Assert.That(manifest.Validate(out _), Is.False);
            manifest = Manifest();
            manifest.tiles = new[] { manifest.tiles[0] };
            Assert.That(manifest.Validate(out _), Is.False);
        }

        [Test]
        public void InvalidWorldBoundsAndResourcePathsCannotActivate()
        {
            var manifest = Manifest();
            manifest.worldRect = new Rect(50, 0, float.NaN, 300);
            Assert.That(manifest.Validate(out _), Is.False);
            foreach (var path in new[] { "../other", "World/CityTiles4K/test.png", "World/CityTiles4K/../x", "World/Other/test" })
            {
                manifest = Manifest(); manifest.tiles[0] = path;
                Assert.That(manifest.Validate(out _), Is.False, path);
            }
        }

        [Test]
        public void SourceRowsRunNorthToSouthAndAdjacentTilesShareWorldEdges()
        {
            var manifest = Manifest();
            Assert.That(manifest.TileWorldRect(0, 0), Is.EqualTo(new Rect(50, 250, 50, 50)));
            Assert.That(manifest.TileWorldRect(5, 5), Is.EqualTo(new Rect(300, 0, 50, 50)));
            Assert.That(manifest.TileWorldRect(2, 2).xMax, Is.EqualTo(manifest.TileWorldRect(2, 3).xMin));
            Assert.That(manifest.TileWorldRect(2, 2).yMin, Is.EqualTo(manifest.TileWorldRect(3, 2).yMax));
        }

        [Test]
        public void WideViewportPlusNeighbourRingIsNotTruncatedToNineTiles()
        {
            var manifest = Manifest();
            var view = new Rect(136, 123, 128, 54); // default 2560 x 1080 orthographic camera
            Assert.That(manifest.VisibleRange(view, 0), Is.EqualTo(new RectInt(1, 2, 4, 2)));
            Assert.That(manifest.VisibleRange(view, 1), Is.EqualTo(new RectInt(0, 1, 6, 4)));
        }

        [Test]
        public void ViewAtMapBoundaryClampsPrefetchAndOutsideViewLoadsNothing()
        {
            var manifest = Manifest();
            Assert.That(manifest.VisibleRange(new Rect(50, 250, 50, 50), 0), Is.EqualTo(new RectInt(0, 0, 1, 1)));
            Assert.That(manifest.VisibleRange(new Rect(50, 250, 50, 50), 1), Is.EqualTo(new RectInt(0, 0, 2, 2)));
            Assert.That(manifest.VisibleRange(new Rect(-100, 350, 40, 40), 1).size, Is.EqualTo(Vector2Int.zero));
        }

        [Test]
        public void HandWrittenJsonUsesPublishedRectFieldNames()
        {
            const string json = "{\"schemaVersion\":1,\"tilePixels\":4096,\"columns\":1,\"rows\":1,\"worldRect\":{\"x\":50,\"y\":0,\"width\":300,\"height\":300},\"tiles\":[\"World/CityTiles4K/tianyong/festival/tiles/r01_c01\"],\"legacyForegroundCompatible\":true}";
            var manifest = JsonUtility.FromJson<CityTileManifest>(json);
            Assert.That(manifest.Validate(out var error), Is.True, error);
            Assert.That(manifest.WorldRect, Is.EqualTo(new Rect(50f, 0f, 300f, 300f)));
            Assert.That(manifest.legacyForegroundCompatible, Is.True);
        }

        [Test]
        public void SquareTilesCannotBeStretchedIntoANonMatchingWorldGrid()
        {
            var manifest = Manifest(5, 4);
            manifest.worldRect = TianyongPaintedCity.PaintingWorldRect;
            Assert.That(manifest.Validate(out _), Is.False);
        }
        [Test]
        public void JsonContractRoundTripsWithoutChangingWorldCoordinates()
        {
            var input = Manifest(3, 3);
            var output = JsonUtility.FromJson<CityTileManifest>(JsonUtility.ToJson(input));
            Assert.That(output.Validate(out _), Is.True);
            Assert.That(output.worldRect, Is.EqualTo(input.worldRect));
            Assert.That(output.tiles, Is.EqualTo(input.tiles));
        }

        [UnityTest]
        public IEnumerator ResidentTextureSurvivesOneOwnerReleaseAndCacheEvictsAfterTheLast()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var type = typeof(CityTileStreaming);
            var acquire = type.GetMethod("Acquire", flags);
            var evict = type.GetMethod("Evict", flags);
            const string path = "World/Tianyong/SceneTiles6x6/Tiles/tianyong_r01_c01";
            var first = acquire.Invoke(null, new object[] { path });
            var second = acquire.Invoke(null, new object[] { path.ToUpperInvariant() });
            var completed = first.GetType().GetField("Completed");
            for (int frame = 0; frame < 120 && !(bool)completed.GetValue(first); frame++) yield return null;
            Assert.That((bool)completed.GetValue(first), Is.True);
            var texture = (Texture2D)first.GetType().GetField("Texture").GetValue(first);
            Assert.That(texture != null, Is.True);
            var references = first.GetType().GetField("References");
            references.SetValue(first, 1);
            evict.Invoke(null, new[] { first });
            Assert.That(texture != null, Is.True, "Another view still owns the texture.");
            references.SetValue(first, 0);
            evict.Invoke(null, new[] { second });
            Assert.That(first.GetType().GetField("Texture").GetValue(first), Is.Null);
            var cache = (IDictionary)type.GetField("Cache", flags).GetValue(null);
            Assert.That(cache.Contains(path), Is.False, "Last release must evict the cache and its owned native reference.");
            // Editor persistent asset wrappers can remain non-null after Resources.UnloadAsset;
            // testing wrapper equality is not a reliable measurement of native GPU residency.
        }
        [UnityTest]
        public IEnumerator SharedPendingRequestIsReleasedOnlyAfterLastOwnerAndCompletion()
        {
            // A missing Resources asset exercises Unity's real asynchronous completion lifecycle.
            // No fixture image or production 4K manifest is published by this test.
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var type = typeof(CityTileStreaming);
            var acquire = type.GetMethod("Acquire", flags);
            var evict = type.GetMethod("Evict", flags);
            var cache = (IDictionary)type.GetField("Cache", flags).GetValue(null);
            const string path = "World/CityTiles4K/__missing_request_lifecycle_fixture";
            var first = acquire.Invoke(null, new object[] { path });
            var second = acquire.Invoke(null, new object[] { path.ToUpperInvariant() });
            Assert.That(second, Is.SameAs(first));
            var references = first.GetType().GetField("References");
            references.SetValue(first, 1);
            evict.Invoke(null, new[] { first });
            Assert.That(cache.Contains(path), Is.True, "The remaining owner must retain the request.");
            references.SetValue(first, 0);
            evict.Invoke(null, new[] { first });
            for (int frame = 0; frame < 120 && cache.Contains(path); frame++) yield return null;
            Assert.That(cache.Contains(path), Is.False, "A request completing after disposal must clean itself up.");
        }
    }
}
