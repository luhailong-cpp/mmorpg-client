using System;
using System.Collections.Generic;
using MmorpgClient.World;
using MmorpgClient.World.CurrentMaps;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class CurrentMapHexGridTests
    {
        [TestCase(0, 0, 0, 0)]
        [TestCase(30, 17.32050807568877, 1, 0)]
        [TestCase(180, 200, 6, 3)]
        [TestCase(170, 200, 6, 3)]
        [TestCase(190, 200, 6, 3)]
        [TestCase(180, 210, 6, 3)]
        [TestCase(180, 185, 6, 2)]
        [TestCase(-180, -200, -6, -3)]
        public void ServerFlatGridGoldensMatch(double x, double y, int q, int r)
            => Assert.That(CurrentMapHexGrid.FromServer(x, y), Is.EqualTo(new CurrentMapHexCell(q, r)));

        [TestCase(.5, 0, -.5, 1, 0)]
        [TestCase(-.5, 0, .5, -1, 0)]
        public void FractionalHalfValuesFollowCppAwayFromZero(double q, double r, double s, int expectedQ, int expectedR)
            => Assert.That(CurrentMapHexGrid.RoundFractional(q, r, s), Is.EqualTo(new CurrentMapHexCell(expectedQ, expectedR)));

        [Test]
        public void UnityConversionUsesExistingServerAxisOrderAndIgnoresAltitude()
        {
            var unity = new Vector3(200, 37, 180);
            var server = WorldCoordinateConverter.UnityToServer(unity);
            Assert.That(server.X, Is.EqualTo(180));
            Assert.That(server.Y, Is.EqualTo(200));
            Assert.That(CurrentMapHexGrid.FromUnity(unity), Is.EqualTo(new CurrentMapHexCell(6, 3)));
            Assert.That(WorldCoordinateConverter.ServerToUnity(server.X, server.Y, server.Z), Is.EqualTo(unity));
            for (int q = -8; q <= 8; q++)
            for (int r = -8; r <= 8; r++)
            {
                var cell = new CurrentMapHexCell(q, r);
                Assert.That(CurrentMapHexGrid.FromUnity(CurrentMapHexGrid.CenterInUnity(cell)), Is.EqualTo(cell));
            }
        }

        [Test]
        public void NeighborhoodIsCenterAndSixDistinctServerDirections()
        {
            var cells = new List<CurrentMapHexCell>();
            CurrentMapHexGrid.Neighborhood(new CurrentMapHexCell(0, 0), cells);
            Assert.That(cells, Is.EqualTo(new[] { new CurrentMapHexCell(0, 0), new CurrentMapHexCell(1, 0),
                new CurrentMapHexCell(1, -1), new CurrentMapHexCell(0, -1), new CurrentMapHexCell(-1, 0),
                new CurrentMapHexCell(-1, 1), new CurrentMapHexCell(0, 1) }));
            Assert.That(new HashSet<CurrentMapHexCell>(cells).Count, Is.EqualTo(7));
            Assert.Throws<ArgumentOutOfRangeException>(() => CurrentMapHexGrid.FromServer(double.NaN, 0));
        }

        [Test]
        public void LargeRectangleIsIndexedInEveryIntersectedHexRatherThanOnlyItsCenter()
        {
            var index = new CurrentMapHexTileIndex(new[] { new Rect(-120, -10, 240, 20), new Rect(-160, -160, 5, 5) });
            var found = new List<int>();
            index.Query(new Rect(110, -2, 4, 4), found);
            Assert.That(found, Does.Contain(0));
            Assert.That(found.Contains(1), Is.False);
            Assert.That(index.CellCount, Is.GreaterThan(7));
        }

        [Test]
        public void WideViewIncludesVisibleEdgeHexesBeyondCenterNeighborhood()
        {
            var index = new CurrentMapHexTileIndex(new[] { new Rect(-120, -2, 4, 4), new Rect(116, -2, 4, 4), new Rect(-2, -2, 4, 4) });
            var found = new List<int>();
            index.Query(new Rect(-125, -10, 250, 20), found);
            Assert.That(found, Is.EquivalentTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void PlayerNeighborhoodAndOffsetCameraViewBothContributeCandidates()
        {
            var index = new CurrentMapHexTileIndex(new[] { new Rect(-120, -2, 4, 4), new Rect(116, -2, 4, 4) });
            var found = new List<int>();
            index.Query(new Rect(-122, -5, 10, 10), new Vector2(118, 0), found);
            Assert.That(found, Is.EquivalentTo(new[] { 0, 1 }));
        }

        [TestCase(14)]
        [TestCase(16)]
        public void SparseIndexNeverDropsAnActuallyVisibleRectangleAcrossGridBoundaries(int grid)
        {
            var rectangles = new List<Rect>();
            float span = 300f / grid;
            for (int row = 0; row < grid; row++)
            for (int column = 0; column < grid; column++)
                if ((row + column) % 3 != 0) rectangles.Add(new Rect(50 + column * span, row * span, span, span));
            var index = new CurrentMapHexTileIndex(rectangles);
            var found = new List<int>();
            foreach (var size in new[] { new Vector2(2, 2), new Vector2(96, 54), new Vector2(250, 25) })
            for (int x = 40; x <= 360; x += 20)
            for (int y = -10; y <= 310; y += 20)
            {
                var view = new Rect(x - size.x / 2, y - size.y / 2, size.x, size.y);
                index.Query(view, found);
                Assert.That(new HashSet<int>(found).Count, Is.EqualTo(found.Count));
                for (int tile = 0; tile < rectangles.Count; tile++)
                    if (rectangles[tile].Overlaps(view)) Assert.That(found, Does.Contain(tile), $"View {view} omitted tile {tile}.");
            }
        }
    }
}
