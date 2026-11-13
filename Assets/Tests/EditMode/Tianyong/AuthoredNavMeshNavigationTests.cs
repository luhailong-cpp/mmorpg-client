using System;
using System.Collections.Generic;
using System.IO;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class AuthoredNavMeshNavigationTests
    {
        private const string Root = "Assets/MapNavigationDrafts/Festival20261111/";
        private const string Reference = "Docs/VerificationEvidence/authored-festival-delivery-20261112/paired-unity-queries.json";

        [Serializable] private sealed class Receipt { public MapCase[] cases; }
        [Serializable] private sealed class MapCase { public string name; public Point[] points; public Route[] routes; }
        [Serializable] private sealed class Point { public float[] input; public bool onMesh; }
        [Serializable] private sealed class Route { public float[] from; public float[] to; public bool complete; public bool rayBlocked; }

        [TestCase("26000-mid-autumn", "26000-mid-autumn.navmesh-9d2185138bdcb22c.asset", 240f, 110.921211f, 33.47244f)]
        [TestCase("26008-spring", "26008-spring.navmesh-504096e2f15fb98b.asset", 225f, 122.5f, 127.5f)]
        public void RealMap_RespectsReferencePointsPathsAndFlatFootHeight(
            string key, string asset, float depth, float spawnX, float spawnZ)
        {
            var data = AssetDatabase.LoadAssetAtPath<NavMeshData>(Root + asset);
            Assert.That(data, Is.Not.Null, "The original native NavMesh asset must be available.");
            var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(Reference));
            var reference = Array.Find(receipt.cases, item => item.name == key);
            Assert.That(reference, Is.Not.Null);
            var spawn = new Vector3(spawnX, 0f, spawnZ);
            using var nav = new AuthoredNavMeshNavigation(data, new Rect(0, 0, 300, depth), spawn);
            Assert.That(nav.IsWalkable(spawn), Is.False, "Construction must not register native data.");
            nav.Activate();
            Assert.That(nav.IsWalkable(spawn), Is.True);
            Assert.That(nav.IsWalkable(spawn + Vector3.up * .03f), Is.False);
            Assert.That(nav.IsWalkable(spawn + Vector3.down * .03f), Is.False);
            Assert.That(nav.IsWalkable(new Vector3(300, 0, spawn.z)), Is.False, "Upper bounds are exclusive.");
            Assert.That(nav.ClampXZ(new Vector3(-.1f, 0, spawn.z)), Is.EqualTo(new Vector3(-.1f, 0, spawn.z)));
            foreach (var point in reference.points)
                Assert.That(nav.IsWalkable(V(point.input)), Is.EqualTo(point.onMesh), $"point {V(point.input)}");

            var aroundObstacles = 0;
            foreach (var route in reference.routes)
            {
                var from = V(route.from);
                var to = V(route.to);
                var path = nav.FindPath(from, to);
                Assert.That(path.Count > 1, Is.EqualTo(route.complete), $"path {from} -> {to}");
                if (!route.complete) continue;
                if (route.rayBlocked)
                {
                    aroundObstacles++;
                    Assert.That(nav.IsSegmentWalkable(from, to), Is.False, "The direct segment crosses an obstacle.");
                }
                Assert.That(path[0], Is.EqualTo(from));
                Assert.That(path[path.Count - 1], Is.EqualTo(to));
                for (var index = 1; index < path.Count; index++)
                {
                    Assert.That(path[index].y, Is.EqualTo(0f));
                    Assert.That(nav.IsSegmentWalkable(path[index - 1], path[index]), Is.True,
                        $"The actual returned corner segment {index} must remain traversable.");
                }
            }
            Assert.That(aroundObstacles, Is.GreaterThan(0), "Receipt must exercise real obstacle corners.");
        }

        [Test]
        public void InstanceOwnership_SwitchesAndRollsBackWithoutDoubleRegistration()
        {
            var aData = AssetDatabase.LoadAssetAtPath<NavMeshData>(Root + "26000-mid-autumn.navmesh-9d2185138bdcb22c.asset");
            var bData = AssetDatabase.LoadAssetAtPath<NavMeshData>(Root + "26008-spring.navmesh-504096e2f15fb98b.asset");
            using var a = new AuthoredNavMeshNavigation(aData, new Rect(0, 0, 300, 240), new Vector3(110.921211f, 0, 33.47244f));
            using var b = new AuthoredNavMeshNavigation(bData, new Rect(0, 0, 300, 225), new Vector3(122.5f, 0, 127.5f));
            a.Activate();
            Assert.Throws<InvalidOperationException>(() => b.Activate());
            Assert.That(a.IsActive, Is.True);
            Assert.That(b.IsActive, Is.False);
            a.Deactivate();
            b.Activate();
            Assert.That(a.IsActive, Is.False);
            Assert.That(b.IsActive, Is.True);
            b.Deactivate();
            a.Activate();
            Assert.That(a.IsActive, Is.True, "The previous map can be restored on failed commit.");
            a.Dispose();
            a.Dispose();
            Assert.That(a.IsActive, Is.False);
            Assert.Throws<ObjectDisposedException>(() => a.Activate());
        }

        [Test]
        public void SyntheticHoleAndIsland_RejectsCrossingAndPartialPath()
        {
            // Four coplanar strips surround a hole; the fifth floor is an
            // isolated island. Endpoints on both sides are valid in each case.
            var sources = new List<NavMeshBuildSource>
            {
                Floor(0, 0, 10, 4), Floor(0, 6, 10, 4),
                Floor(0, 4, 4, 2), Floor(6, 4, 4, 2),
                Floor(20, 0, 10, 10),
            };
            var settings = NavMesh.GetSettingsByID(0);
            settings.minRegionArea = 0;
            settings.overrideVoxelSize = true;
            settings.voxelSize = .1f;
            settings.buildHeightMesh = true;
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources,
                new Bounds(new Vector3(15, 0, 5), new Vector3(32, 4, 12)), Vector3.zero, Quaternion.identity);
            Assert.That(data, Is.Not.Null);
            try
            {
                var start = new Vector3(2, 0, 5);
                var acrossHole = new Vector3(8, 0, 5);
                var island = new Vector3(25, 0, 5);
                using var nav = new AuthoredNavMeshNavigation(data, new Rect(0, 0, 30, 10), start);
                nav.Activate();
                Assert.That(nav.IsWalkable(start) && nav.IsWalkable(acrossHole) && nav.IsWalkable(island), Is.True);
                Assert.That(nav.IsSegmentWalkable(start, acrossHole), Is.False);
                Assert.That(nav.FindPath(start, acrossHole), Is.Not.Empty, "A complete route around the hole exists.");
                Assert.That(nav.FindPath(start, island), Is.Empty, "A partial path to another island must be rejected.");
            }
            finally { UnityEngine.Object.DestroyImmediate(data); }
        }

        [Test]
        public void ExistingGrid_PreservesOriginalDefaultBoundsAndSpawn()
        {
            var grid = new TianyongNavigationGrid();
            IMapNavigation nav = grid;
            Assert.That(nav.IsAuthoredFlat, Is.False);
            Assert.That(grid.Width, Is.EqualTo(100));
            Assert.That(grid.Depth, Is.EqualTo(75));
            Assert.That(nav.IsWalkable(TianyongMapDefinition.DefaultSpawn), Is.True);
            Assert.That(nav.ResolveFallbackSpawn(TianyongMapDefinition.DefaultSpawn, null),
                Is.EqualTo(TianyongMapDefinition.DefaultSpawn));
        }

        private static Vector3 V(float[] p) => new Vector3(p[0], p[1], p[2]);

        private static NavMeshBuildSource Floor(float x, float z, float width, float depth) => new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            size = new Vector3(width, .2f, depth),
            transform = Matrix4x4.Translate(new Vector3(x + width * .5f, -.1f, z + depth * .5f)),
            area = 0,
        };
    }
}
