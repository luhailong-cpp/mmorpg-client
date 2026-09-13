using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class TianyongPaintedForegroundTests
    {
        [Test]
        public void RoofUv_ProjectsOntoTheSameOriginalPaintingPixels()
        {
            var mesh = TianyongPaintedForeground.CreateWestLampRoofMesh();
            try
            {
                var anchor = TianyongPaintedCity.PaintingToWorld(TianyongPaintedForeground.WestLampBasePixel);
                var vertices = mesh.vertices;
                var uv = mesh.uv;
                for (var i = 0; i < vertices.Length; i++)
                {
                    // r03_c03's actual placement in the original 6144px image.
                    var sourceX = 2048f + uv[i].x * 1024f;
                    var sourceY = 2048f + (1f - uv[i].y) * 1024f;
                    Assert.That(anchor.x + vertices[i].x, Is.EqualTo(50f + sourceX / 20.48f).Within(0.0001f));
                    Assert.That(anchor.z + vertices[i].z, Is.EqualTo(300f - sourceY / 20.48f).Within(0.0001f));
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void EveryCutout_UvProjectsOntoItsOwnSourceTilePixels()
        {
            foreach (var cutout in TianyongPaintedForeground.Cutouts)
            {
                var mesh = TianyongPaintedForeground.CreateMesh(cutout);
                try
                {
                    var anchor = TianyongPaintedCity.PaintingToWorld(cutout.BasePixel);
                    var tileX = cutout.TileColumn * 1024f;
                    var tileY = cutout.TileRow * 1024f;
                    var vertices = mesh.vertices;
                    var uv = mesh.uv;
                    for (var i = 0; i < vertices.Length; i++)
                    {
                        var sourceX = tileX + uv[i].x * 1024f;
                        var sourceY = tileY + (1f - uv[i].y) * 1024f;
                        Assert.That(anchor.x + vertices[i].x, Is.EqualTo(50f + sourceX / 20.48f).Within(0.0001f), cutout.Name);
                        Assert.That(anchor.z + vertices[i].z, Is.EqualTo(300f - sourceY / 20.48f).Within(0.0001f), cutout.Name);
                    }
                }
                finally { Object.DestroyImmediate(mesh); }
            }
        }

        [Test]
        public void EveryCutout_HasTopToBottomRowsInsideItsTileAndAboveItsBase()
        {
            Assert.That(TianyongPaintedForeground.Cutouts.Count, Is.GreaterThanOrEqualTo(2));
            foreach (var cutout in TianyongPaintedForeground.Cutouts)
            {
                Assert.That(cutout.Rows.Length, Is.GreaterThanOrEqualTo(2), cutout.Name);
                for (var i = 0; i < cutout.Rows.Length; i++)
                {
                    var row = cutout.Rows[i];
                    if (i > 0) Assert.That(row.x, Is.GreaterThan(cutout.Rows[i - 1].x), cutout.Name + " rows must run top to bottom");
                    Assert.That(row.x, Is.InRange(0f, 1024f), cutout.Name);
                    Assert.That(row.y, Is.LessThan(row.z), cutout.Name);
                    Assert.That(row.y, Is.GreaterThanOrEqualTo(0f), cutout.Name);
                    Assert.That(row.z, Is.LessThanOrEqualTo(1024f), cutout.Name);
                }
                // The silhouette is the part standing up out of the ground, so
                // it lies north of (above) its own ground contact.
                var baseY = cutout.BasePixel.y - cutout.TileRow * 1024f;
                Assert.That(cutout.Rows[cutout.Rows.Length - 1].x, Is.LessThan(baseY), cutout.Name);
            }
        }

        [Test]
        public void WestLampSilhouette_CoversFestivalPavilionButExcludesAdjacentPavement()
        {
            var mesh = TianyongPaintedForeground.CreateWestLampRoofMesh();
            try
            {
                // r03_c03: jade eave and the red festival banner are foreground.
                Assert.That(CoversTilePixel(mesh, new Vector2(302f, 200f)), Is.True);
                // A rectangle cutout would incorrectly conceal this pavement.
                Assert.That(CoversTilePixel(mesh, new Vector2(274f, 200f)), Is.False);
                Assert.That(CoversTilePixel(mesh, new Vector2(402f, 70f)), Is.False);
                Assert.That(CoversTilePixel(mesh, new Vector2(350f, 310f)), Is.False);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void EastLampSilhouette_CoversBannerAndEaveButNotAdjacentPavement()
        {
            var mesh = TianyongPaintedForeground.CreateMesh(TianyongPaintedForeground.EastLamp);
            try
            {
                Assert.That(CoversTilePixel(mesh, new Vector2(666f, 90f)), Is.True, "red banner");
                Assert.That(CoversTilePixel(mesh, new Vector2(633f, 190f)), Is.True, "jade eave");
                Assert.That(CoversTilePixel(mesh, new Vector2(620f, 90f)), Is.False, "pavement west of banner");
                Assert.That(CoversTilePixel(mesh, new Vector2(715f, 90f)), Is.False, "pavement east of banner");
                Assert.That(CoversTilePixel(mesh, new Vector2(600f, 190f)), Is.False, "pavement west of eave");
                Assert.That(CoversTilePixel(mesh, new Vector2(675f, 310f)), Is.False, "pavement south of base");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void FestivalLampPieces_MeetAtTileSeamAndShareTheirGroundSortingAnchor()
        {
            var lower = new[] { TianyongPaintedForeground.WestLamp, TianyongPaintedForeground.EastLamp };
            var upper = new[] { TianyongPaintedForeground.WestLampUpper, TianyongPaintedForeground.EastLampUpper };
            for (var i = 0; i < lower.Length; i++)
            {
                var last = upper[i].Rows[upper[i].Rows.Length - 1];
                var first = lower[i].Rows[0];
                Assert.That(last.x, Is.EqualTo(1024f));
                Assert.That(first.x, Is.EqualTo(0f));
                Assert.That(last.y, Is.EqualTo(first.y));
                Assert.That(last.z, Is.EqualTo(first.z));
                Assert.That(upper[i].BasePixel, Is.EqualTo(lower[i].BasePixel));
                Assert.That(upper[i].VisibilityGroup, Is.EqualTo(lower[i].VisibilityGroup));
            }
        }

        [Test]
        public void BuiltForeground_SortsBetweenNorthAndSouthActorsAndLeavesGroundEnabled()
        {
            var parent = new GameObject("PaintedForegroundTest");
            var cameraObject = new GameObject("PaintedForegroundTestCamera", typeof(Camera));
            TianyongMapInstance map = null;
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                map = TianyongMapBuilder.Build(parent.transform, TianyongTheme.City, null);
                Assert.That(map.Root.GetComponentsInChildren<TianyongPaintedForeground>().Length,
                    Is.EqualTo(TianyongPaintedForeground.Cutouts.Count));
                var renderer = FindCutoutRenderer(map, TianyongPaintedForeground.WestLampObjectName);
                var northActor = QdaoBoySpriteAnimator.WorldSortingOrder(new Vector3(163f, 0f, 189f), camera);
                var southActor = QdaoBoySpriteAnimator.WorldSortingOrder(new Vector3(167f, 0f, 182f), camera);
                Assert.That(renderer.sortingOrder, Is.GreaterThan(northActor));
                Assert.That(renderer.sortingOrder, Is.LessThan(southActor));
                Assert.That(renderer.sharedMaterial.renderQueue, Is.EqualTo(3000));
                Assert.That(renderer.GetComponent<Collider>(), Is.Null);

                Assert.That(TianyongPaintedForeground.SetWestLampVisible(map.Root.transform, false), Is.True);
                Assert.That(renderer.enabled, Is.False);
                Assert.That(FindCutoutRenderer(map, TianyongPaintedForeground.WestLampUpper.Name).enabled, Is.False);
                var groundTiles = 0;
                foreach (var ground in map.Root.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!ground.name.StartsWith("Tile_r")) continue;
                    groundTiles++;
                    Assert.That(ground.enabled, Is.True);
                }
                Assert.That(groundTiles, Is.EqualTo(36));
                Assert.That(TianyongPaintedForeground.SetWestLampVisible(map.Root.transform, true), Is.True);
                Assert.That(renderer.enabled, Is.True);
                Assert.That(FindCutoutRenderer(map, TianyongPaintedForeground.WestLampUpper.Name).enabled, Is.True);
            }
            finally
            {
                map?.Dispose();
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void BuiltEastLamp_SortsBetweenNorthAndSouthActorsAndTogglesIndependently()
        {
            var parent = new GameObject("PaintedForegroundEastTest");
            var cameraObject = new GameObject("PaintedForegroundEastTestCamera", typeof(Camera));
            TianyongMapInstance map = null;
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                map = TianyongMapBuilder.Build(parent.transform, TianyongTheme.City, null);
                var east = FindCutoutRenderer(map, TianyongPaintedForeground.EastLampObjectName);
                var west = FindCutoutRenderer(map, TianyongPaintedForeground.WestLampObjectName);
                // Same legal side position used by the festival acceptance drive.
                var northActor = QdaoBoySpriteAnimator.WorldSortingOrder(new Vector3(227f, 0f, 189f), camera);
                var southActor = QdaoBoySpriteAnimator.WorldSortingOrder(new Vector3(233f, 0f, 182f), camera);
                Assert.That(east.sortingOrder, Is.GreaterThan(northActor));
                Assert.That(east.sortingOrder, Is.LessThan(southActor));
                Assert.That(east.sharedMaterial.renderQueue, Is.EqualTo(3000));
                Assert.That(east.GetComponent<Collider>(), Is.Null, "must not intercept click-to-move rays");

                Assert.That(TianyongPaintedForeground.SetVisible(map.Root.transform,
                    TianyongPaintedForeground.EastLampObjectName, false), Is.True);
                Assert.That(east.enabled, Is.False);
                Assert.That(FindCutoutRenderer(map, TianyongPaintedForeground.EastLampUpper.Name).enabled, Is.False);
                Assert.That(FindCutoutRenderer(map, TianyongPaintedForeground.WestLampUpper.Name).enabled, Is.True);
                Assert.That(west.enabled, Is.True, "toggling one cutout must leave the others alone");
                Assert.That(TianyongPaintedForeground.SetVisible(map.Root.transform,
                    TianyongPaintedForeground.EastLampObjectName, true), Is.True);
                Assert.That(east.enabled, Is.True);
                Assert.That(FindCutoutRenderer(map, TianyongPaintedForeground.EastLampUpper.Name).enabled, Is.True);
                Assert.That(TianyongPaintedForeground.SetVisible(map.Root.transform, "NoSuchCutout", false), Is.False);
            }
            finally
            {
                map?.Dispose();
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(parent);
            }
        }

        private static MeshRenderer FindCutoutRenderer(TianyongMapInstance map, string name)
        {
            foreach (var foreground in map.Root.GetComponentsInChildren<TianyongPaintedForeground>(true))
                if (foreground.name == name) return foreground.GetComponent<MeshRenderer>();
            Assert.Fail("cutout not built: " + name);
            return null;
        }

        private static bool CoversTilePixel(Mesh mesh, Vector2 sourcePixel)
        {
            var point = new Vector2(sourcePixel.x / 1024f, 1f - sourcePixel.y / 1024f);
            var uv = mesh.uv;
            var triangles = mesh.triangles;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = uv[triangles[i]];
                var b = uv[triangles[i + 1]];
                var c = uv[triangles[i + 2]];
                var ab = Cross(b - a, point - a);
                var bc = Cross(c - b, point - b);
                var ca = Cross(a - c, point - c);
                if ((ab >= 0f && bc >= 0f && ca >= 0f) ||
                    (ab <= 0f && bc <= 0f && ca <= 0f)) return true;
            }
            return false;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
