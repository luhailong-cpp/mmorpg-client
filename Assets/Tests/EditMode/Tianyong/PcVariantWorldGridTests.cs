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
    }
}
