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
        public void RoofSilhouette_CoversTheReproducedShoeOverlapButExcludesAdjacentPavement()
        {
            var mesh = TianyongPaintedForeground.CreateWestLampRoofMesh();
            try
            {
                // The failed west-side screenshot overlaps this left eave pixel.
                Assert.That(CoversTilePixel(mesh, new Vector2(735f, 350f)), Is.True);
                // A rectangle cutout would incorrectly conceal this pavement.
                Assert.That(CoversTilePixel(mesh, new Vector2(724f, 350f)), Is.False);
                Assert.That(CoversTilePixel(mesh, new Vector2(800f, 300f)), Is.False);
                Assert.That(CoversTilePixel(mesh, new Vector2(770f, 385f)), Is.False);
            }
            finally { Object.DestroyImmediate(mesh); }
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
                var foreground = map.Root.GetComponentInChildren<TianyongPaintedForeground>();
                Assert.That(foreground, Is.Not.Null);
                var renderer = foreground.GetComponent<MeshRenderer>();
                var northActor = QdaoBoySpriteAnimator.WorldSortingOrder(new Vector3(185.55f, 0f, 182.98f), camera);
                var southActor = QdaoBoySpriteAnimator.WorldSortingOrder(new Vector3(187.75f, 0f, 176.54f), camera);
                Assert.That(renderer.sortingOrder, Is.GreaterThan(northActor));
                Assert.That(renderer.sortingOrder, Is.LessThan(southActor));
                Assert.That(renderer.sharedMaterial.renderQueue, Is.EqualTo(3000));
                Assert.That(foreground.GetComponent<Collider>(), Is.Null);

                Assert.That(TianyongPaintedForeground.SetWestLampVisible(map.Root.transform, false), Is.True);
                Assert.That(renderer.enabled, Is.False);
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
            }
            finally
            {
                map?.Dispose();
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(parent);
            }
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
