using System;
using System.Collections.Generic;
using MmorpgClient.ThirdParty.RedBlobGames;
using UnityEngine;

namespace MmorpgClient.World.CurrentMaps
{
    using Vector3 = UnityEngine.Vector3;

    public readonly struct CurrentMapHexCell : IEquatable<CurrentMapHexCell>
    {
        public CurrentMapHexCell(int q, int r) { Q = q; R = r; S = checked(-q - r); }
        public int Q { get; }
        public int R { get; }
        public int S { get; }
        public bool Equals(CurrentMapHexCell other) => Q == other.Q && R == other.R;
        public override bool Equals(object value) => value is CurrentMapHexCell other && Equals(other);
        public override int GetHashCode() => unchecked(Q * 397 ^ R);
        public override string ToString() => $"({Q},{R},{S})";
    }

    /// <summary>使用服务器同源 Red Blob 算法，保持 server XY 与 Unity XZ 的轴交换契约。</summary>
    public static class CurrentMapHexGrid
    {
        public const double CellSize = 20.0;
        internal static readonly Layout ServerLayout = new(Layout.flat, new Point(CellSize, CellSize), new Point(0, 0));

        public static CurrentMapHexCell FromServer(double x, double y)
        {
            RequireCoordinate(x); RequireCoordinate(y);
            var hex = ServerLayout.PixelToHexRounded(new Point(x, y));
            return new CurrentMapHexCell(hex.q, hex.r);
        }

        public static CurrentMapHexCell FromUnity(Vector3 point)
        {
            var server = WorldCoordinateConverter.UnityToServer(point);
            return FromServer(server.X, server.Y);
        }

        public static CurrentMapHexCell RoundFractional(double q, double r, double s)
        {
            RequireCoordinate(q); RequireCoordinate(r); RequireCoordinate(s);
            var hex = new FractionalHex(q, r, s).HexRound();
            return new CurrentMapHexCell(hex.q, hex.r);
        }

        public static CurrentMapHexCell Neighbor(CurrentMapHexCell center, int direction)
        {
            if (direction < 0 || direction >= 6) throw new ArgumentOutOfRangeException(nameof(direction));
            var hex = new Hex(center.Q, center.R, center.S).Neighbor(direction);
            return new CurrentMapHexCell(hex.q, hex.r);
        }

        public static void Neighborhood(CurrentMapHexCell center, List<CurrentMapHexCell> output)
        {
            output.Clear();
            output.Add(center);
            for (int i = 0; i < 6; i++) output.Add(Neighbor(center, i));
        }

        public static Vector3 CenterInUnity(CurrentMapHexCell cell)
        {
            var point = ServerLayout.HexToPixel(new Hex(cell.Q, cell.R, cell.S));
            return WorldCoordinateConverter.ServerToUnity(point.x, point.y, 0);
        }

        private static void RequireCoordinate(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > 1000000000)
                throw new ArgumentOutOfRangeException(nameof(value), "六边形坐标必须有限且位于整数网格范围内。");
        }
    }

    /// <summary>方形贴图可覆盖多个六边形；中心七格和可见视野共同查询同一空间索引。</summary>
    public sealed class CurrentMapHexTileIndex
    {
        private sealed class Cell
        {
            public Point[] Polygon;
            public readonly List<int> Tiles = new();
        }

        private readonly Dictionary<CurrentMapHexCell, Cell> _cells = new();
        private readonly Matrix4x4 _localToWorld;
        private readonly HashSet<int> _seen = new();
        private readonly List<CurrentMapHexCell> _neighbors = new(7);
        private int _minQ = int.MaxValue, _maxQ = int.MinValue, _minR = int.MaxValue, _maxR = int.MinValue;
        public int CellCount => _cells.Count;

        public CurrentMapHexTileIndex(IReadOnlyList<Rect> tiles) : this(tiles, Matrix4x4.identity) { }

        public CurrentMapHexTileIndex(IReadOnlyList<Rect> tiles, Matrix4x4 localToWorld)
        {
            _localToWorld = localToWorld;
            for (int i = 0; i < tiles.Count; i++)
            {
                var polygon = MapRectangle(tiles[i]);
                Bounds(polygon, out int minQ, out int maxQ, out int minR, out int maxR);
                for (int q = minQ; q <= maxQ; q++)
                for (int r = minR; r <= maxR; r++)
                {
                    var key = new CurrentMapHexCell(q, r);
                    _cells.TryGetValue(key, out var cell);
                    var hex = cell?.Polygon ?? HexPolygon(key);
                    if (!Intersects(polygon, hex)) continue;
                    if (cell == null)
                    {
                        cell = new Cell { Polygon = hex };
                        _cells.Add(key, cell);
                        _minQ = Math.Min(_minQ, q); _maxQ = Math.Max(_maxQ, q);
                        _minR = Math.Min(_minR, r); _maxR = Math.Max(_maxR, r);
                    }
                    cell.Tiles.Add(i);
                }
            }
        }

        public void Query(Rect view, List<int> output)
            => Query(view, view.center, output);

        public void Query(Rect view, Vector2 focus, List<int> output)
        {
            output.Clear(); _seen.Clear();
            if (_cells.Count == 0) return;
            var center = MapPoint(focus);
            CurrentMapHexGrid.Neighborhood(CurrentMapHexGrid.FromServer(center.x, center.y), _neighbors);
            foreach (var key in _neighbors) if (_cells.TryGetValue(key, out var cell)) Append(cell, output);

            // 大视野不能被中心七格截断：把真实视野覆盖的六边形也并入候选。
            var polygon = MapRectangle(view);
            Bounds(polygon, out int minQ, out int maxQ, out int minR, out int maxR);
            minQ = Math.Max(minQ, _minQ); maxQ = Math.Min(maxQ, _maxQ);
            minR = Math.Max(minR, _minR); maxR = Math.Min(maxR, _maxR);
            for (int q = minQ; q <= maxQ; q++)
            for (int r = minR; r <= maxR; r++)
                if (_cells.TryGetValue(new CurrentMapHexCell(q, r), out var cell) && Intersects(polygon, cell.Polygon))
                    Append(cell, output);
        }

        private void Append(Cell cell, List<int> output)
        {
            foreach (int tile in cell.Tiles) if (_seen.Add(tile)) output.Add(tile);
        }

        private Point MapPoint(Vector2 point)
        {
            var world = _localToWorld.MultiplyPoint3x4(new Vector3(point.x, 0, point.y));
            var server = WorldCoordinateConverter.UnityToServer(world);
            return new Point(server.X, server.Y);
        }

        private Point[] MapRectangle(Rect rect) => new[] {
            MapPoint(new Vector2(rect.xMin, rect.yMin)), MapPoint(new Vector2(rect.xMax, rect.yMin)),
            MapPoint(new Vector2(rect.xMax, rect.yMax)), MapPoint(new Vector2(rect.xMin, rect.yMax)) };

        private static Point[] HexPolygon(CurrentMapHexCell key)
            => CurrentMapHexGrid.ServerLayout.PolygonCorners(new Hex(key.Q, key.R, key.S)).ToArray();

        private static void Bounds(Point[] polygon, out int minQ, out int maxQ, out int minR, out int maxR)
        {
            double loQ = double.PositiveInfinity, hiQ = double.NegativeInfinity;
            double loR = double.PositiveInfinity, hiR = double.NegativeInfinity;
            foreach (var point in polygon)
            {
                // 使用原作者同一逆变换；六边形顶点离中心不足一个轴向单位。
                var fractional = CurrentMapHexGrid.ServerLayout.PixelToHexFractional(point);
                loQ = Math.Min(loQ, fractional.q); hiQ = Math.Max(hiQ, fractional.q);
                loR = Math.Min(loR, fractional.r); hiR = Math.Max(hiR, fractional.r);
            }
            minQ = checked((int)Math.Floor(loQ) - 1); maxQ = checked((int)Math.Ceiling(hiQ) + 1);
            minR = checked((int)Math.Floor(loR) - 1); maxR = checked((int)Math.Ceiling(hiR) + 1);
        }

        // 凸多边形分离轴测试，同时检查矩形和六边形的边法向，边界接触也纳入候选。
        private static bool Intersects(Point[] a, Point[] b) => !Separated(a, a, b) && !Separated(b, a, b);

        private static bool Separated(Point[] edges, Point[] a, Point[] b)
        {
            for (int i = 0; i < edges.Length; i++)
            {
                var p = edges[i]; var next = edges[(i + 1) % edges.Length];
                double axisX = -(next.y - p.y), axisY = next.x - p.x;
                Project(a, axisX, axisY, out double minA, out double maxA);
                Project(b, axisX, axisY, out double minB, out double maxB);
                if (maxA < minB - 0.0000001 || maxB < minA - 0.0000001) return true;
            }
            return false;
        }

        private static void Project(Point[] polygon, double x, double y, out double min, out double max)
        {
            min = double.PositiveInfinity; max = double.NegativeInfinity;
            foreach (var point in polygon)
            {
                double value = point.x * x + point.y * y;
                min = Math.Min(min, value); max = Math.Max(max, value);
            }
        }
    }
}
