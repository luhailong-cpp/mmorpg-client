using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// Small deterministic A* grid used by click-to-move.  It deliberately
    /// avoids a NavMesh package dependency so the map works in the current
    /// bare Unity project and can later be replaced by server-authored navdata.
    /// </summary>
    public sealed class TianyongNavigationGrid
    {
        private static readonly Vector2Int[] Directions =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
            new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
        };

        private readonly bool[,] _walkable;

        public TianyongNavigationGrid(float cellSize = TianyongMapDefinition.NavigationCellSize)
        {
            CellSize = Mathf.Max(1f, cellSize);
            Width = Mathf.CeilToInt(TianyongMapDefinition.Width / CellSize);
            Depth = Mathf.CeilToInt(TianyongMapDefinition.Depth / CellSize);
            _walkable = new bool[Width, Depth];

            for (var x = 0; x < Width; x++)
            for (var z = 0; z < Depth; z++)
                _walkable[x, z] = true;

            // Keep actors inside the city wall.
            BlockRect(new Rect(0f, 0f, TianyongMapDefinition.Width, 5f));
            BlockRect(new Rect(0f, TianyongMapDefinition.Depth - 5f, TianyongMapDefinition.Width, 5f));
            BlockRect(new Rect(0f, 0f, 5f, TianyongMapDefinition.Depth));
            BlockRect(new Rect(TianyongMapDefinition.Width - 5f, 0f, 5f, TianyongMapDefinition.Depth));

            // Buildings use conservative padded footprints.
            foreach (var building in TianyongMapDefinition.Buildings)
                BlockRect(building.Footprint(2.5f));

            BlockRect(TianyongMapDefinition.FestivalFountainFootprint);

            // The south canal is blocked except at the three stone bridges.
            BlockRect(TianyongMapDefinition.Canal);
            foreach (var bridge in TianyongMapDefinition.Bridges)
                SetRect(bridge, true);

            // Raised martial arena in the east-central district.
            BlockRect(new Rect(314f, 119f, 55f, 55f));
        }

        /// <summary>
        /// Builds a grid whose walkability comes from an external sampler
        /// (for example a mask authored against the painted city artwork).
        /// Each cell is walkable when the sampler accepts its centre.
        /// </summary>
        public TianyongNavigationGrid(float cellSize, System.Func<Vector3, bool> walkableSampler)
        {
            CellSize = Mathf.Max(0.5f, cellSize);
            Width = Mathf.CeilToInt(TianyongMapDefinition.Width / CellSize);
            Depth = Mathf.CeilToInt(TianyongMapDefinition.Depth / CellSize);
            _walkable = new bool[Width, Depth];

            for (var x = 0; x < Width; x++)
            for (var z = 0; z < Depth; z++)
                _walkable[x, z] = walkableSampler != null &&
                                  walkableSampler(CellToWorld(new Vector2Int(x, z), 0f));
        }

        public float CellSize { get; }
        public int Width { get; }
        public int Depth { get; }

        public bool IsWalkable(Vector3 world)
        {
            var cell = WorldToCell(world);
            return IsWalkable(cell.x, cell.y);
        }

        public List<Vector3> FindPath(Vector3 start, Vector3 target)
        {
            start = TianyongMapDefinition.ClampXZ(start);
            target = TianyongMapDefinition.ClampXZ(target);

            var startCell = FindNearestWalkable(WorldToCell(start));
            var targetCell = FindNearestWalkable(WorldToCell(target));
            if (startCell.x < 0 || targetCell.x < 0)
                return new List<Vector3>();

            // A click may land on a roof, wall or the canal. Route to the
            // nearest legal cell instead of appending the blocked click point
            // as the final waypoint.
            var resolvedStart = IsWalkable(start) ? start : CellToWorld(startCell, start.y);
            var resolvedTarget = IsWalkable(target) ? target : CellToWorld(targetCell, target.y);

            var g = new float[Width, Depth];
            var closed = new bool[Width, Depth];
            var hasParent = new bool[Width, Depth];
            var parent = new Vector2Int[Width, Depth];
            for (var x = 0; x < Width; x++)
            for (var z = 0; z < Depth; z++)
                g[x, z] = float.PositiveInfinity;

            var open = new List<Vector2Int> { startCell };
            g[startCell.x, startCell.y] = 0f;

            while (open.Count > 0)
            {
                var bestIndex = 0;
                var bestScore = float.PositiveInfinity;
                for (var i = 0; i < open.Count; i++)
                {
                    var c = open[i];
                    var score = g[c.x, c.y] + Heuristic(c, targetCell);
                    if (score >= bestScore) continue;
                    bestScore = score;
                    bestIndex = i;
                }

                var current = open[bestIndex];
                open.RemoveAt(bestIndex);
                if (closed[current.x, current.y]) continue;
                closed[current.x, current.y] = true;

                if (current == targetCell)
                    return BuildWorldPath(resolvedStart, resolvedTarget, startCell, targetCell, parent, hasParent);

                foreach (var direction in Directions)
                {
                    var next = current + direction;
                    if (!IsWalkable(next.x, next.y) || closed[next.x, next.y]) continue;

                    var diagonal = direction.x != 0 && direction.y != 0;
                    if (diagonal &&
                        (!IsWalkable(current.x + direction.x, current.y) ||
                         !IsWalkable(current.x, current.y + direction.y)))
                        continue;

                    var tentative = g[current.x, current.y] + (diagonal ? 1.41421356f : 1f);
                    if (tentative >= g[next.x, next.y]) continue;

                    g[next.x, next.y] = tentative;
                    parent[next.x, next.y] = current;
                    hasParent[next.x, next.y] = true;
                    open.Add(next);
                }
            }

            return new List<Vector3>();
        }

        private List<Vector3> BuildWorldPath(
            Vector3 exactStart,
            Vector3 exactTarget,
            Vector2Int start,
            Vector2Int target,
            Vector2Int[,] parent,
            bool[,] hasParent)
        {
            var cells = new List<Vector2Int>();
            var current = target;
            cells.Add(current);
            while (current != start)
            {
                if (!hasParent[current.x, current.y]) return new List<Vector3>();
                current = parent[current.x, current.y];
                cells.Add(current);
            }
            cells.Reverse();

            var raw = new List<Vector3>(cells.Count + 2) { exactStart };
            for (var i = 1; i < cells.Count - 1; i++)
                raw.Add(CellToWorld(cells[i], exactStart.y));
            raw.Add(exactTarget);

            if (raw.Count <= 2) return raw;

            // Greedy line-of-sight smoothing removes grid stair-steps while
            // preserving obstacle clearance.
            var result = new List<Vector3> { raw[0] };
            var anchor = 0;
            while (anchor < raw.Count - 1)
            {
                var furthest = anchor + 1;
                for (var candidate = raw.Count - 1; candidate > anchor + 1; candidate--)
                {
                    if (!HasLineOfSight(raw[anchor], raw[candidate])) continue;
                    furthest = candidate;
                    break;
                }
                result.Add(raw[furthest]);
                anchor = furthest;
            }
            return result;
        }

        private bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            var distance = Vector3.Distance(from, to);
            // Sample more tightly than one world unit so the smoothed segment
            // cannot slip diagonally across the corner of a blocked grid cell
            // (most visibly at a bridge/canal boundary).
            var samples = Mathf.Max(1, Mathf.CeilToInt(distance / (CellSize * 0.2f)));
            for (var i = 0; i <= samples; i++)
            {
                var p = Vector3.Lerp(from, to, i / (float)samples);
                if (!IsWalkable(p)) return false;
            }
            return true;
        }

        private Vector2Int FindNearestWalkable(Vector2Int origin)
        {
            if (IsWalkable(origin.x, origin.y)) return origin;
            for (var radius = 1; radius <= 8; radius++)
            {
                for (var x = -radius; x <= radius; x++)
                for (var z = -radius; z <= radius; z++)
                {
                    if (Mathf.Abs(x) != radius && Mathf.Abs(z) != radius) continue;
                    var cell = new Vector2Int(origin.x + x, origin.y + z);
                    if (IsWalkable(cell.x, cell.y)) return cell;
                }
            }
            return new Vector2Int(-1, -1);
        }

        private Vector2Int WorldToCell(Vector3 world)
            => new(
                Mathf.Clamp(Mathf.FloorToInt(world.x / CellSize), 0, Width - 1),
                Mathf.Clamp(Mathf.FloorToInt(world.z / CellSize), 0, Depth - 1));

        private Vector3 CellToWorld(Vector2Int cell, float y)
            => new((cell.x + 0.5f) * CellSize, y, (cell.y + 0.5f) * CellSize);

        private bool IsWalkable(int x, int z)
            => x >= 0 && x < Width && z >= 0 && z < Depth && _walkable[x, z];

        private static float Heuristic(Vector2Int a, Vector2Int b)
        {
            var dx = Mathf.Abs(a.x - b.x);
            var dz = Mathf.Abs(a.y - b.y);
            return Mathf.Max(dx, dz) + 0.41421356f * Mathf.Min(dx, dz);
        }

        private void BlockRect(Rect rect) => SetRect(rect, false);

        private void SetRect(Rect rect, bool value)
        {
            var xMin = Mathf.Clamp(Mathf.FloorToInt(rect.xMin / CellSize), 0, Width - 1);
            var xMax = Mathf.Clamp(Mathf.CeilToInt(rect.xMax / CellSize), 0, Width);
            var zMin = Mathf.Clamp(Mathf.FloorToInt(rect.yMin / CellSize), 0, Depth - 1);
            var zMax = Mathf.Clamp(Mathf.CeilToInt(rect.yMax / CellSize), 0, Depth);
            for (var x = xMin; x < xMax; x++)
            for (var z = zMin; z < zMax; z++)
                _walkable[x, z] = value;
        }
    }
}
