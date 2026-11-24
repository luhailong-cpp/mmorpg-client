using System;
using MmorpgClient.World.CurrentMaps;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class GuidengOriginalMapPreviewTests
    {
        private const string MapId = "original:guideng-market";

        [Test]
        public void CommittedGuidengCatalogValidatesDualFestivalsAndExactRowThreeGeometry()
        {
            var catalogAsset = Resources.Load<TextAsset>(PcOriginalMapContract.OriginalPreviewCatalogResourcePath);
            Assert.That(catalogAsset, Is.Not.Null, "Missing OriginalMapPreview20261124/catalog.json");

            var catalog = JsonUtility.FromJson<PcOriginalMapCatalog>(catalogAsset.text);
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.IsOriginalCatalog, Is.True);
            Assert.DoesNotThrow(() => catalog.Validate());
            Assert.That(catalog.staticBackOnly, Is.True);
            Assert.That(catalog.completeGameplaySceneClaimed, Is.False);
            Assert.That(catalog.productionTravelEnabled, Is.False);

            var map = catalog.FindByMapId(MapId);
            Assert.That(map, Is.Not.Null);
            Assert.That(map.numericMapId, Is.EqualTo(0u));
            Assert.That(map.artStage, Is.EqualTo(PcOriginalMapContract.ArtStageDeliveryCandidate));
            Assert.That(map.nativeWidth, Is.EqualTo(4096));
            Assert.That(map.nativeHeight, Is.EqualTo(2731));
            Assert.That(map.columns, Is.EqualTo(4));
            Assert.That(map.rows, Is.EqualTo(3));
            Assert.That(map.tileCount, Is.EqualTo(12));
            Assert.That(map.defaultAppearance, Is.EqualTo("spring"));

            var spring = map.ResolveAppearance("spring");
            var midAutumn = map.ResolveAppearance("midautumn");
            var midAutumnAlias = map.ResolveAppearance("mid-autumn");
            Assert.That(spring, Is.Not.Null);
            Assert.That(midAutumn, Is.Not.Null);
            Assert.That(midAutumnAlias, Is.Not.Null);
            Assert.That(midAutumnAlias.resourceRoot, Is.EqualTo(midAutumn.resourceRoot));
            Assert.That(map.ResolveAppearance("normal"), Is.Null);
            Assert.That(spring.WorldWidth, Is.EqualTo(4f).Within(1e-6f));
            Assert.That(spring.WorldHeight, Is.EqualTo(2731f / 1024f).Within(1e-6f));
            Assert.That(midAutumn.WorldWidth, Is.EqualTo(spring.WorldWidth).Within(1e-6f));
            Assert.That(midAutumn.WorldHeight, Is.EqualTo(spring.WorldHeight).Within(1e-6f));

            foreach (var variant in new[] { spring, midAutumn })
            {
                variant.Validate(allowOriginalId: true);
                var manifestAsset = Resources.Load<TextAsset>(variant.tilesManifestResourcePath);
                Assert.That(manifestAsset, Is.Not.Null, "Missing tiles.json for " + variant.resourceRoot);
                var manifest = JsonUtility.FromJson<PcOriginalTileManifest>(manifestAsset.text);
                Assert.That(manifest, Is.Not.Null);
                manifest.Validate(variant, allowOriginalId: true);
                Assert.That(manifest.tiles.Length, Is.EqualTo(12));

                var rects = new PcOriginalWorldRect[3, 4];
                foreach (var tile in manifest.tiles)
                {
                    int expectedHeight = tile.row == 3 ? 683 : 1024;
                    Assert.That(tile.width, Is.EqualTo(1024));
                    Assert.That(tile.height, Is.EqualTo(expectedHeight));
                    Assert.That(tile.x, Is.EqualTo((tile.column - 1) * 1024));
                    Assert.That(tile.y, Is.EqualTo((tile.row - 1) * 1024));
                    rects[tile.row - 1, tile.column - 1] = PcOriginalViewportPlanner.TileWorldRect(manifest, tile);
                }

                // Top-left (1,1) touches north/west edges; bottom-right (3,4) touches south/east edges.
                Assert.That(rects[0, 0].XMin, Is.EqualTo(0f).Within(1e-6f));
                Assert.That(rects[0, 0].YMax, Is.EqualTo(2731f / 1024f).Within(1e-6f));
                Assert.That(rects[2, 3].XMax, Is.EqualTo(4f).Within(1e-6f));
                Assert.That(rects[2, 3].YMin, Is.EqualTo(0f).Within(1e-6f));

                // Exact abutment across all internal seams, including 1024 <-> 683 row 2/3 boundary.
                for (int r = 0; r < 3; r++)
                for (int c = 0; c < 4; c++)
                {
                    Assert.That(rects[r, c].Width, Is.EqualTo(1f).Within(1e-6f));
                    Assert.That(rects[r, c].Height, Is.EqualTo((r == 2 ? 683f : 1024f) / 1024f).Within(1e-6f));
                    if (c < 3)
                        Assert.That(rects[r, c].XMax, Is.EqualTo(rects[r, c + 1].XMin).Within(1e-6f));
                    if (r < 2)
                        Assert.That(rects[r, c].YMin, Is.EqualTo(rects[r + 1, c].YMax).Within(1e-6f));
                }
            }
        }

        [Test]
        public void ImportedGuidengTexturesKeepExactEdgeTileDimensionsAndRgb24NoMipImport()
        {
            var catalog = JsonUtility.FromJson<PcOriginalMapCatalog>(
                Resources.Load<TextAsset>(PcOriginalMapContract.OriginalPreviewCatalogResourcePath).text);
            var map = catalog.FindByMapId(MapId);
            var spring = map.ResolveAppearance("spring");
            var midAutumn = map.ResolveAppearance("midautumn");

            Texture2D springEast = null;
            Texture2D midEast = null;
            try
            {
                foreach (var variant in new[] { spring, midAutumn })
                {
                    var overview = Resources.Load<Texture2D>(variant.overviewResourcePath);
                    Assert.That(overview, Is.Not.Null);
                    Assert.That(overview.width, Is.EqualTo(2048));
                    Assert.That(overview.height, Is.EqualTo(1366));
                    Assert.That(overview.mipmapCount, Is.EqualTo(1));
                    Assert.That(overview.format, Is.EqualTo(TextureFormat.RGB24));
                    Resources.UnloadAsset(overview);

                    var topTile = Resources.Load<Texture2D>(variant.resourceRoot + "tiles/r01_c01");
                    var bottomTile = Resources.Load<Texture2D>(variant.resourceRoot + "tiles/r03_c04");
                    Assert.That(topTile, Is.Not.Null);
                    Assert.That(bottomTile, Is.Not.Null);
                    Assert.That(topTile.width, Is.EqualTo(1024));
                    Assert.That(topTile.height, Is.EqualTo(1024));
                    Assert.That(bottomTile.width, Is.EqualTo(1024));
                    Assert.That(bottomTile.height, Is.EqualTo(683), "Row 3 edge tile must remain exact 683px without NPOT scaling.");
                    foreach (var tex in new[] { topTile, bottomTile })
                    {
                        Assert.That(tex.mipmapCount, Is.EqualTo(1));
                        Assert.That(tex.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
                        Assert.That(tex.filterMode, Is.EqualTo(FilterMode.Bilinear));
                        Assert.That(tex.isReadable, Is.False);
                        Assert.That(tex.format, Is.EqualTo(TextureFormat.RGB24));
                    }
                    Resources.UnloadAsset(topTile);
                    Resources.UnloadAsset(bottomTile);
                }

                springEast = Resources.Load<Texture2D>(spring.resourceRoot + "tiles/r02_c03");
                midEast = Resources.Load<Texture2D>(midAutumn.resourceRoot + "tiles/r02_c03");
                Assert.That(springEast, Is.Not.Null);
                Assert.That(midEast, Is.Not.Null);
                Assert.That(springEast, Is.Not.SameAs(midEast), "Spring and Mid-Autumn must resolve distinct east-side textures.");
            }
            finally
            {
                if (springEast != null) Resources.UnloadAsset(springEast);
                if (midEast != null) Resources.UnloadAsset(midEast);
            }
        }

        [Test]
        public void OriginalCatalogContractRejectsInvalidMutationsAndPreservesStrictPcCatalogRule()
        {
            var catalogAsset = Resources.Load<TextAsset>(PcOriginalMapContract.OriginalPreviewCatalogResourcePath);

            // 1. Existing 5-digit PC validator still rejects original slug IDs.
            Assert.That(PcOriginalMapContract.IsValidMapId(MapId), Is.False);
            Assert.That(PcOriginalMapContract.IsValidOriginalMapId(MapId), Is.True);
            Assert.That(PcOriginalMapContract.IsValidOriginalMapId("original:../bad"), Is.False);

            // 2. Original catalog rejects online travel claims.
            var badOnline = JsonUtility.FromJson<PcOriginalMapCatalog>(catalogAsset.text);
            badOnline.productionTravelEnabled = true;
            Assert.Throws<InvalidOperationException>(() => badOnline.Validate());

            // 3. Delivery candidate cannot be marked as temporary PC original.
            var badTemp = JsonUtility.FromJson<PcOriginalMapCatalog>(catalogAsset.text);
            badTemp.maps[0].isTemporaryOriginal = true;
            badTemp.maps[0].appearances[0].isTemporaryOriginal = true;
            Assert.Throws<InvalidOperationException>(() => badTemp.Validate());

            // 4. Tampering row 3 edge height from 683 to 1024 fails manifest validation.
            var catalog = JsonUtility.FromJson<PcOriginalMapCatalog>(catalogAsset.text);
            var spring = catalog.FindByMapId(MapId).ResolveAppearance("spring");
            var manifest = JsonUtility.FromJson<PcOriginalTileManifest>(
                Resources.Load<TextAsset>(spring.tilesManifestResourcePath).text);
            manifest.tiles[8].height = 1024;
            Assert.Throws<InvalidOperationException>(() => manifest.Validate(spring, allowOriginalId: true));
        }
    }
}
