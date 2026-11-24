using System;
using MmorpgClient.World.CurrentMaps;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class PcVariantWorldGridTests
    {
        private static readonly int[] X = { 0,1131,2263,3394,4526,5657,6788,7920,9051,10183,11314 };
        private static readonly int[] Y = { 0,849,1697,2546,3394,4243,5091,5940,6788,7354 };

        [Test]
        public void AllNinetySourceRectsFillTheOriginalWorldIncludingTheShortSouthRow()
        {
            var manifest = new PcOriginalTileManifest {
                schemaVersion = 2, mapId = "41081", numericMapId = 41081,
                nativeWidth = 11314, nativeHeight = 7354, tileSize = 2048, rows = 9, columns = 10,
                worldGrid = new PcOriginalWorldGrid { widthPixels = 4000, heightPixels = 2600, tileWidthPixels = 400, tileHeightPixels = 300 },
                resourceRoot = "World/PcMapVariantReview/test/", tiles = new PcOriginalTileEntry[90]
            };
            for (int row = 0; row < 9; row++)
            for (int col = 0; col < 10; col++)
            {
                var tile = new PcOriginalTileEntry { row = row + 1, column = col + 1, x = X[col], y = Y[row],
                    width = X[col + 1] - X[col], height = Y[row + 1] - Y[row],
                    resourcePath = manifest.resourceRoot + $"tiles/r{row+1}_c{col+1}", fileExtension = ".png",
                    sha256 = new string('a', 64), bytes = 1 };
                manifest.tiles[row * 10 + col] = tile;
                var rect = PcOriginalViewportPlanner.TileWorldRect(manifest, tile);
                Assert.AreEqual(col * 400, rect.XMin * 1024);
                Assert.AreEqual(2600 - row * 300, rect.YMax * 1024);
                Assert.AreEqual(400, rect.Width * 1024);
                Assert.AreEqual(row == 8 ? 200 : 300, rect.Height * 1024);
            }
            manifest.Validate();
            manifest.tiles[1].x++;
            Assert.Throws<InvalidOperationException>(() => manifest.Validate());
        }

        [Test]
        public void ReselectingAnAppearanceUsesItsCatalogGeometry()
        {
            var map = new PcOriginalMapEntry { nativeWidth = 3200, nativeHeight = 2400, tileSize = 1024,
                defaultAppearance = "normal", appearances = new[] {
                    new PcOriginalMapAppearanceEntry { id = "normal" },
                    new PcOriginalMapAppearanceEntry { id = "spring", nativeWidth = 6400, nativeHeight = 4800,
                        worldGrid = new PcOriginalWorldGrid { widthPixels = 3200, heightPixels = 2400, tileWidthPixels = 400, tileHeightPixels = 300 } }
                }
            };
            var hd = map.ResolveAppearance("spring");
            Assert.AreEqual(64, hd.tileCount);
            var original = hd.ResolveAppearance("normal");
            Assert.AreEqual(3200, original.nativeWidth);
            Assert.AreEqual(12, original.tileCount);
            Assert.IsFalse(original.HasWorldGrid);
            Assert.AreEqual(hd.WorldHeight, original.WorldHeight);
        }

        [Test]
        public void LegacyPlannerKeepsItsNativePixelWorldPosition()
        {
            var tile = new PcOriginalTileEntry { row = 3, column = 4, x = 3072, y = 2048, width = 928, height = 552 };
            var rect = PcOriginalViewportPlanner.TileWorldRect(new PcOriginalTileManifest { nativeHeight = 2600 }, tile);
            Assert.AreEqual(3072, rect.XMin * 1024);
            Assert.AreEqual(0, rect.YMin);
            Assert.AreEqual(928, rect.Width * 1024);
            Assert.AreEqual(552, rect.Height * 1024);
        }

        [Test]
        public void Map16001Hd3840x2572TwentyFiveTileWorldGridPreservesOriginalExtentAndHalfScale()
        {
            Assert.AreEqual(1024f, PcOriginalMapContract.PixelsPerUnit);
            var grid = new PcOriginalWorldGrid { widthPixels = 1920, heightPixels = 1286, tileWidthPixels = 400, tileHeightPixels = 300 };
            var manifest = new PcOriginalTileManifest
            {
                schemaVersion = 2,
                mapId = "16001",
                numericMapId = 16001,
                nativeWidth = 3840,
                nativeHeight = 2572,
                tileSize = 1024,
                columns = 5,
                rows = 5,
                hasAlpha = false,
                worldGrid = grid,
                resourceRoot = "World/PcMapVariantReview/16001/",
                tiles = new PcOriginalTileEntry[25]
            };
            for (int row = 0; row < 5; row++)
            for (int col = 0; col < 5; col++)
            {
                int x0 = grid.PixelX(col, 3840), x1 = grid.PixelX(col + 1, 3840);
                int y0 = grid.PixelY(row, 2572), y1 = grid.PixelY(row + 1, 2572);
                int w = x1 - x0, h = y1 - y0;
                Assert.AreEqual(col == 4 ? 640 : 800, w);
                Assert.AreEqual(row == 4 ? 172 : 600, h);
                var tile = new PcOriginalTileEntry
                {
                    row = row + 1, column = col + 1, x = x0, y = y0, width = w, height = h,
                    resourcePath = manifest.resourceRoot + $"tiles/r{row + 1:00}_c{col + 1:00}",
                    fileExtension = ".png", sha256 = new string('b', 64), bytes = 1
                };
                manifest.tiles[row * 5 + col] = tile;
                var rect = PcOriginalViewportPlanner.TileWorldRect(manifest, tile);
                Assert.AreEqual(0.5f, rect.Width * PcOriginalMapContract.PixelsPerUnit / tile.width, 1e-6f);
                Assert.AreEqual(0.5f, rect.Height * PcOriginalMapContract.PixelsPerUnit / tile.height, 1e-6f);
            }
            manifest.Validate();
            var first = PcOriginalViewportPlanner.TileWorldRect(manifest, manifest.tiles[0]);
            var last = PcOriginalViewportPlanner.TileWorldRect(manifest, manifest.tiles[24]);
            Assert.AreEqual(0f, first.XMin, 1e-6f);
            Assert.AreEqual(1286f / 1024f, first.YMax, 1e-6f);
            Assert.AreEqual(1920f / 1024f, last.XMax, 1e-6f);
            Assert.AreEqual(0f, last.YMin, 1e-6f);

            var map = new PcOriginalMapEntry
            {
                nativeWidth = 1920, nativeHeight = 1286, tileSize = 1024, defaultAppearance = "normal",
                appearances = new[]
                {
                    new PcOriginalMapAppearanceEntry { id = "normal" },
                    new PcOriginalMapAppearanceEntry { id = "spring", nativeWidth = 3840, nativeHeight = 2572, worldGrid = grid }
                }
            };
            var orig = map.ResolveAppearance("normal");
            var hd = map.ResolveAppearance("spring");
            Assert.AreEqual(4, orig.tileCount);
            Assert.AreEqual(25, hd.tileCount);
            Assert.AreEqual(1.875f, orig.WorldWidth, 1e-6f);
            Assert.AreEqual(1.255859375f, orig.WorldHeight, 1e-6f);
            Assert.AreEqual(orig.WorldWidth, hd.WorldWidth, 1e-6f);
            Assert.AreEqual(orig.WorldHeight, hd.WorldHeight, 1e-6f);
        }
    }
}
