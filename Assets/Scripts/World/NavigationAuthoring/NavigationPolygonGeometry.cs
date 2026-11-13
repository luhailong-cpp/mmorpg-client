using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MmorpgClient.World.NavigationAuthoring
{
    /// <summary>Turns authored walkable and blocking polygons into upward-facing ground triangles.</summary>
    public static class NavigationPolygonGeometry
    {
        private const int MaxPolygonVertices = 4096;
        private const int MaxPieces = 50000;
        private const int MaxClipOperations = 4000000;

        private readonly struct Point
        {
            public readonly double x, y;
            public Point(double x, double y) { this.x = x; this.y = y; }
        }

        public static Mesh Build(NavMeshAuthoringDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            draft.Validate(false);
            if (draft.polygons == null || draft.polygons.Count == 0)
                throw new InvalidOperationException("请先绘制至少一个可走区域。");
            var walkable = new List<List<Point>>();
            var blockers = new List<List<Point>>();
            double extent = Math.Max(draft.worldRect.width, draft.worldRect.height);
            double epsilon = Math.Max(1e-8, extent * 1e-8);
            foreach (var area in draft.polygons)
            {
                if (area == null || area.points == null) throw new InvalidOperationException("多边形或顶点为空。");
                var converted = new List<Point>(area.points.Count);
                foreach (var pixel in area.points)
                {
                    if (!Finite(pixel.x) || !Finite(pixel.y) || pixel.x < 0 || pixel.y < 0 ||
                        pixel.x > draft.imageWidth || pixel.y > draft.imageHeight)
                        throw new InvalidOperationException("多边形顶点超出底图范围或不是有效数字。");
                    Vector2 world = draft.PixelToWorld(pixel);
                    if (!Finite(world.x) || !Finite(world.y)) throw new InvalidOperationException("多边形坐标无法映射到世界。");
                    converted.Add(new Point(world.x, world.y));
                }
                var polygon = NormalizeAndValidate(converted, epsilon);
                var triangles = Triangulate(polygon, epsilon);
                (area.blocked ? blockers : walkable).AddRange(triangles);
            }
            if (walkable.Count == 0) throw new InvalidOperationException("没有可走区域。");

            // Every ear triangle is convex. Subtracting convex clips produces disjoint convex
            // pieces; subtract already accepted pieces too, so overlapping walks form a union.
            var accepted = new List<List<Point>>();
            int operations = 0;
            foreach (var triangle in walkable)
            {
                var pieces = new List<List<Point>> { triangle };
                foreach (var blocker in blockers)
                {
                    pieces = SubtractAll(pieces, blocker, epsilon, ref operations);
                    if (pieces.Count == 0) break;
                }
                // Capture the count before appending this triangle's resulting pieces.
                int existingCount = accepted.Count;
                for (int i = 0; i < existingCount && pieces.Count > 0; i++)
                    pieces = SubtractAll(pieces, accepted[i], epsilon, ref operations);
                accepted.AddRange(pieces);
                if (accepted.Count > MaxPieces) throw new InvalidOperationException("多边形相减后的片段过多，请简化形状。");
            }
            if (accepted.Count == 0) throw new InvalidOperationException("障碍覆盖了全部可走区域。");
            var vertices = new List<UnityEngine.Vector3>();
            var indices = new List<int>();
            foreach (var piece in accepted)
            {
                if (piece.Count < 3 || SignedArea(piece) <= epsilon * epsilon) continue;
                for (int i = 1; i + 1 < piece.Count; i++)
                {
                    if (Cross(piece[0], piece[i], piece[i + 1]) <= epsilon * epsilon) continue;
                    if (vertices.Count > 3 * MaxPieces) throw new InvalidOperationException("输出三角形过多，请简化形状。");
                    int start = vertices.Count;
                    vertices.Add(new UnityEngine.Vector3((float)piece[0].x, 0f, (float)piece[0].y));
                    vertices.Add(new UnityEngine.Vector3((float)piece[i].x, 0f, (float)piece[i].y));
                    vertices.Add(new UnityEngine.Vector3((float)piece[i + 1].x, 0f, (float)piece[i + 1].y));
                    // CCW in X/Z has a downward 3D normal; reverse for a +Y ground normal.
                    indices.Add(start); indices.Add(start + 2); indices.Add(start + 1);
                }
            }
            if (indices.Count == 0) throw new InvalidOperationException("可走区域没有有效三角形。");
            var result = new Mesh { name = "Navigation authored ground" };
            if (vertices.Count > 65535) result.indexFormat = IndexFormat.UInt32;
            result.SetVertices(vertices);
            result.SetTriangles(indices, 0);
            result.RecalculateBounds();
            result.RecalculateNormals();
            return result;
        }

        /// <summary>Checks a single editable polygon without mutating it.</summary>
        public static void ValidatePolygon(IEnumerable<Vector2> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            var copy = new List<Point>();
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (var p in points)
            {
                if (!Finite(p.x) || !Finite(p.y)) throw new InvalidOperationException("顶点必须是有限数字。");
                copy.Add(new Point(p.x, p.y));
                minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x);
                minY = Math.Min(minY, p.y); maxY = Math.Max(maxY, p.y);
            }
            double scale = Math.Max(maxX - minX, maxY - minY);
            NormalizeAndValidate(copy, Math.Max(1e-8, scale * 1e-8));
        }

        /// <summary>定位原始轮廓中相交的边，保留作者顶点顺序，包含最后一点到首点的闭合边。</summary>
        public static bool TryGetSelfIntersection(IReadOnlyList<Vector2> points, out int firstEdge, out int secondEdge)
        {
            firstEdge = secondEdge = -1;
            if (points == null || points.Count < 3 || points.Count > MaxPolygonVertices) return false;
            double x0 = double.PositiveInfinity, y0 = double.PositiveInfinity;
            double x1 = double.NegativeInfinity, y1 = double.NegativeInfinity;
            foreach (var point in points)
            {
                if (!Finite(point.x) || !Finite(point.y)) return false;
                x0 = Math.Min(x0, point.x); y0 = Math.Min(y0, point.y);
                x1 = Math.Max(x1, point.x); y1 = Math.Max(y1, point.y);
            }
            double epsilon = Math.Max(1e-8, Math.Max(x1 - x0, y1 - y0) * 1e-8);
            int count = points.Count;
            var initial = new Point(points[0].x, points[0].y);
            var last = new Point(points[count - 1].x, points[count - 1].y);
            if (DistanceSquared(initial, last) <= epsilon * epsilon) count--;
            for (int i = 0; i < count; i++)
            for (int j = i + 1; j < count; j++)
            {
                int nextI = (i + 1) % count, nextJ = (j + 1) % count;
                if (nextI == j || nextJ == i) continue;
                var a = new Point(points[i].x, points[i].y);
                var b = new Point(points[nextI].x, points[nextI].y);
                var c = new Point(points[j].x, points[j].y);
                var d = new Point(points[nextJ].x, points[nextJ].y);
                if (DistanceSquared(a, b) <= epsilon * epsilon || DistanceSquared(c, d) <= epsilon * epsilon) continue;
                if (!SegmentsIntersect(a, b, c, d, epsilon)) continue;
                firstEdge = i; secondEdge = j; return true;
            }
            return false;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static double Cross(Point a, Point b, Point p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        private static double DistanceSquared(Point a, Point b) => (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);
        private static double SignedArea(List<Point> polygon)
        {
            double sum = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                Point a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                sum += a.x * b.y - b.x * a.y;
            }
            return sum * .5;
        }

        private static List<Point> NormalizeAndValidate(List<Point> input, double eps)
        {
            if (input.Count > MaxPolygonVertices) throw new InvalidOperationException("单个多边形顶点超过 4096 个。");
            var result = new List<Point>(input.Count);
            foreach (var p in input)
                if (result.Count == 0 || DistanceSquared(p, result[result.Count - 1]) > eps * eps) result.Add(p);
            if (result.Count > 1 && DistanceSquared(result[0], result[result.Count - 1]) <= eps * eps)
                result.RemoveAt(result.Count - 1);
            bool changed = true;
            while (changed && result.Count >= 3)
            {
                changed = false;
                for (int i = 0; i < result.Count; i++)
                {
                    Point a = result[(i + result.Count - 1) % result.Count], b = result[i], c = result[(i + 1) % result.Count];
                    double abx = b.x - a.x, aby = b.y - a.y, bcx = c.x - b.x, bcy = c.y - b.y;
                    double length = Math.Sqrt(DistanceSquared(a, c));
                    if (Math.Abs(Cross(a, b, c)) <= eps * Math.Max(eps, length) && abx * bcx + aby * bcy >= -eps * eps)
                    { result.RemoveAt(i); changed = true; break; }
                }
            }
            if (result.Count < 3 || Math.Abs(SignedArea(result)) <= eps * eps)
                throw new InvalidOperationException("多边形至少需要三个不共线的顶点。");
            int intersectionChecks = 0;
            for (int i = 0; i < result.Count; i++)
            for (int j = i + 1; j < result.Count; j++)
            {
                if (++intersectionChecks > MaxClipOperations)
                    throw new InvalidOperationException("多边形边线检查过于复杂，请减少顶点。");
                int iNext = (i + 1) % result.Count, jNext = (j + 1) % result.Count;
                if (i == j || iNext == j || jNext == i) continue;
                if (SegmentsIntersect(result[i], result[iNext], result[j], result[jNext], eps))
                    throw new InvalidOperationException("多边形边线自交或重叠，请调整顶点。");
            }
            if (SignedArea(result) < 0) result.Reverse();
            return result;
        }

        private static bool SegmentsIntersect(Point a, Point b, Point c, Point d, double eps)
        {
            double ab = eps * Math.Max(eps, Math.Sqrt(DistanceSquared(a, b)));
            double cd = eps * Math.Max(eps, Math.Sqrt(DistanceSquared(c, d)));
            double c1 = Cross(a, b, c), c2 = Cross(a, b, d), c3 = Cross(c, d, a), c4 = Cross(c, d, b);
            if (((c1 > ab && c2 < -ab) || (c1 < -ab && c2 > ab)) &&
                ((c3 > cd && c4 < -cd) || (c3 < -cd && c4 > cd))) return true;
            return Math.Abs(c1) <= ab && OnSegment(a, b, c, eps) || Math.Abs(c2) <= ab && OnSegment(a, b, d, eps) ||
                Math.Abs(c3) <= cd && OnSegment(c, d, a, eps) || Math.Abs(c4) <= cd && OnSegment(c, d, b, eps);
        }
        private static bool OnSegment(Point a, Point b, Point p, double eps) =>
            p.x >= Math.Min(a.x, b.x) - eps && p.x <= Math.Max(a.x, b.x) + eps &&
            p.y >= Math.Min(a.y, b.y) - eps && p.y <= Math.Max(a.y, b.y) + eps;

        private static List<List<Point>> Triangulate(List<Point> polygon, double eps)
        {
            var remaining = new List<Point>(polygon);
            var result = new List<List<Point>>();
            int earChecks = 0;
            while (remaining.Count > 3)
            {
                bool found = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    Point a = remaining[(i + remaining.Count - 1) % remaining.Count], b = remaining[i], c = remaining[(i + 1) % remaining.Count];
                    if (Cross(a, b, c) <= eps * eps) continue;
                    bool contains = false;
                    for (int j = 0; j < remaining.Count; j++)
                    {
                        if (++earChecks > MaxClipOperations)
                            throw new InvalidOperationException("多边形三角化过于复杂，请减少顶点。");
                        if (j == i || j == (i + remaining.Count - 1) % remaining.Count || j == (i + 1) % remaining.Count) continue;
                        Point p = remaining[j];
                        if (Cross(a, b, p) >= -eps * eps && Cross(b, c, p) >= -eps * eps && Cross(c, a, p) >= -eps * eps)
                        { contains = true; break; }
                    }
                    if (contains) continue;
                    result.Add(new List<Point> { a, b, c });
                    remaining.RemoveAt(i);
                    found = true;
                    break;
                }
                if (!found) throw new InvalidOperationException("多边形无法安全三角化，请检查相交边和极窄区域。");
            }
            result.Add(remaining);
            return result;
        }

        private static List<List<Point>> SubtractAll(List<List<Point>> pieces, List<Point> clip, double eps, ref int operations)
        {
            var output = new List<List<Point>>();
            foreach (var piece in pieces)
            {
                if (++operations > MaxClipOperations) throw new InvalidOperationException("几何运算过于复杂，请简化多边形。");
                if (!BoxesOverlap(piece, clip, eps)) { output.Add(piece); continue; }
                var remainder = piece;
                for (int edge = 0; edge < clip.Count && remainder.Count >= 3; edge++)
                {
                    Point a = clip[edge], b = clip[(edge + 1) % clip.Count];
                    var outside = ClipHalfPlane(remainder, a, b, false, eps);
                    if (outside.Count >= 3 && SignedArea(outside) > eps * eps) output.Add(outside);
                    remainder = ClipHalfPlane(remainder, a, b, true, eps);
                }
                if (output.Count > MaxPieces) throw new InvalidOperationException("多边形相减后的片段过多，请简化形状。");
            }
            return output;
        }

        private static List<Point> ClipHalfPlane(List<Point> polygon, Point a, Point b, bool keepInside, double eps)
        {
            var output = new List<Point>();
            if (polygon.Count == 0) return output;
            double threshold = eps * Math.Max(eps, Math.Sqrt(DistanceSquared(a, b)));
            Point previous = polygon[polygon.Count - 1];
            double previousD = Cross(a, b, previous);
            bool previousKept = keepInside ? previousD >= -threshold : previousD <= threshold;
            foreach (Point current in polygon)
            {
                double currentD = Cross(a, b, current);
                bool currentKept = keepInside ? currentD >= -threshold : currentD <= threshold;
                if (currentKept != previousKept)
                {
                    double divisor = previousD - currentD;
                    if (Math.Abs(divisor) > 1e-30)
                    {
                        double t = Math.Max(0, Math.Min(1, previousD / divisor));
                        AddDistinct(output, new Point(previous.x + (current.x - previous.x) * t,
                            previous.y + (current.y - previous.y) * t), eps);
                    }
                }
                if (currentKept) AddDistinct(output, current, eps);
                previous = current; previousD = currentD; previousKept = currentKept;
            }
            if (output.Count > 1 && DistanceSquared(output[0], output[output.Count - 1]) <= eps * eps) output.RemoveAt(output.Count - 1);
            return output;
        }
        private static void AddDistinct(List<Point> points, Point point, double eps)
        {
            if (points.Count == 0 || DistanceSquared(points[points.Count - 1], point) > eps * eps) points.Add(point);
        }
        private static bool BoxesOverlap(List<Point> a, List<Point> b, double eps)
        {
            Bounds2(a, out double ax0, out double ay0, out double ax1, out double ay1);
            Bounds2(b, out double bx0, out double by0, out double bx1, out double by1);
            return ax0 <= bx1 + eps && ax1 >= bx0 - eps && ay0 <= by1 + eps && ay1 >= by0 - eps;
        }
        private static void Bounds2(List<Point> points, out double x0, out double y0, out double x1, out double y1)
        {
            x0 = y0 = double.PositiveInfinity; x1 = y1 = double.NegativeInfinity;
            foreach (var p in points) { x0 = Math.Min(x0, p.x); y0 = Math.Min(y0, p.y); x1 = Math.Max(x1, p.x); y1 = Math.Max(y1, p.y); }
        }
    }
}
