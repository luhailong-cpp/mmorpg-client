using MmorpgClient.World.CurrentMaps;
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
        public void CurrentMainGround_DrawsBelowActorsWithoutLegacyForegrounds()
        {
            var parent = new GameObject("CurrentMapGroundOrdering");
            TianyongMapInstance map = null;
            try
            {
                map = CurrentMapBuilder.Build(parent.transform, CurrentMapCatalog.Find("main"));
                var ground = map.Root.transform.Find(CurrentMapBuilder.GroundName).GetComponent<MeshRenderer>();
                Assert.That(ground.enabled, Is.True);
                Assert.That(ground.sharedMaterial.renderQueue, Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.Geometry));
                Assert.That(ground.sharedMaterial.renderQueue, Is.LessThan((int)UnityEngine.Rendering.RenderQueue.Transparent));
                Assert.That(ground.GetComponent<Collider>(), Is.Null);
                Assert.That(map.Root.GetComponentsInChildren<TianyongPaintedForeground>(true), Is.Empty);
                Assert.That(map.Root.transform.Find(TianyongPaintedCity.RootName), Is.Null);
                Assert.That(map.Navigation.IsWalkable(CurrentMapCatalog.Find("main").Spawn), Is.True);
            }
            finally { map?.Dispose(); Object.DestroyImmediate(parent); }
        }

        [Test]
        public void LegacyForegroundToggle_CannotMutateCurrentMapGroundOrNavigation()
        {
            var parent = new GameObject("CurrentMapRejectLegacyAppearance");
            TianyongMapInstance map = null;
            try
            {
                var main = CurrentMapCatalog.Find("main");
                map = CurrentMapBuilder.Build(parent.transform, main);
                var navigation = map.Navigation;
                var ground = map.Root.transform.Find(CurrentMapBuilder.GroundName).GetComponent<MeshRenderer>();
                var material = ground.sharedMaterial;
                foreach (var name in new[] { TianyongPaintedForeground.EastLampObjectName, TianyongPaintedForeground.WestLampObjectName })
                foreach (var visible in new[] { false, true })
                {
                    Assert.That(TianyongPaintedForeground.SetVisible(map.Root.transform, name, visible), Is.False);
                    Assert.That(ground.enabled, Is.True);
                    Assert.That(ground.sharedMaterial, Is.SameAs(material));
                    Assert.That(map.Navigation, Is.SameAs(navigation));
                    Assert.That(navigation.IsWalkable(main.Spawn), Is.True);
                }
                Assert.That(map.Root.GetComponentsInChildren<TianyongPaintedForeground>(true), Is.Empty);
            }
            finally { map?.Dispose(); Object.DestroyImmediate(parent); }
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
