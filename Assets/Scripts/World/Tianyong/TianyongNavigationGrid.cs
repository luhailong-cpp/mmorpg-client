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
    public sealed class TianyongNavigationGrid : IMapNavigation
    {
        private static readonly Vector2Int[] Directions =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
            new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
        };

        private readonly bool[,] _walkable;

        public TianyongNavigationGrid(float cellSize = TianyongMapDefinition.NavigationCellSize)
        {
            Bounds = new Rect(0, 0, TianyongMapDefinition.Width, TianyongMapDefinition.Depth);
            EffectiveBounds = Bounds;
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
            : this(cellSize, walkableSampler, new Rect(0, 0, TianyongMapDefinition.Width, TianyongMapDefinition.Depth)) { }

        public TianyongNavigationGrid(float cellSize, System.Func<Vector3, bool> walkableSampler, Rect bounds)
            : this(cellSize, walkableSampler, bounds, bounds) { }

        public TianyongNavigationGrid(float cellSize, System.Func<Vector3, bool> walkableSampler, Rect bounds, Rect effectiveBounds)
        {
            if (float.IsNaN(bounds.x) || float.IsNaN(bounds.y) || float.IsNaN(bounds.width) || float.IsNaN(bounds.height) ||
                float.IsInfinity(bounds.x) || float.IsInfinity(bounds.y) || float.IsInfinity(bounds.width) || float.IsInfinity(bounds.height) ||
                bounds.width <= 0 || bounds.height <= 0)
                throw new System.ArgumentException("A finite positive navigation rectangle is required.", nameof(bounds));
            if (float.IsNaN(effectiveBounds.x) || float.IsNaN(effectiveBounds.y) ||
                float.IsNaN(effectiveBounds.width) || float.IsNaN(effectiveBounds.height) ||
                float.IsInfinity(effectiveBounds.xMax) || float.IsInfinity(effectiveBounds.yMax) ||
                effectiveBounds.width <= 0 || effectiveBounds.height <= 0 ||
                effectiveBounds.xMin < bounds.xMin || effectiveBounds.yMin < bounds.yMin ||
                effectiveBounds.xMax > bounds.xMax || effectiveBounds.yMax > bounds.yMax)
                throw new System.ArgumentException("The effective navigation rectangle must be inside its grid.", nameof(effectiveBounds));
            Bounds = bounds;
            EffectiveBounds = effectiveBounds;
            CellSize = Mathf.Max(0.5f, cellSize);
            Width = Mathf.CeilToInt(bounds.width / CellSize);
            Depth = Mathf.CeilToInt(bounds.height / CellSize);
            _walkable = new bool[Width, Depth];

            for (var x = 0; x < Width; x++)
            for (var z = 0; z < Depth; z++)
                _walkable[x, z] = CellIntersectsEffectiveBounds(x, z) && walkableSampler != null &&
                                  walkableSampler(CellToWorld(new Vector2Int(x, z), 0f));
        }

        public float CellSize { get; }
        public Rect Bounds { get; }
        public Rect EffectiveBounds { get; }
        public int Width { get; }
        public int Depth { get; }
        public bool IsAuthoredFlat => false;

        // The existing movement controller retains its original grid behavior;
        // this query is available to callers that explicitly need a segment test.
        public bool IsSegmentWalkable(Vector3 from, Vector3 to) => HasLineOfSight(from, to);

        public bool IsWalkable(Vector3 world)
        {
            if (float.IsNaN(world.x) || float.IsNaN(world.z) || world.x < EffectiveBounds.xMin || world.x >= EffectiveBounds.xMax ||
                world.z < EffectiveBounds.yMin || world.z >= EffectiveBounds.yMax) return false;
            var cell = WorldToCell(world);
            return IsWalkable(cell.x, cell.y);
        }

        /// <summary>
        /// Nearest legal feet point to <paramref name="world"/> (the point
        /// itself when already walkable, otherwise the centre of the closest
        /// walkable cell within the search ring). Used to recover from an
        /// authoritative snap that lands off the client mask; false means
        /// nothing walkable exists nearby and the caller should fall back to
        /// the map's default spawn.
        /// </summary>
        public bool TryFindNearestWalkable(Vector3 world, out Vector3 result)
        {
            world = ClampXZ(world);
            if (IsWalkable(world))
            {
                result = world;
                return true;
            }

            var cell = FindNearestWalkable(WorldToCell(world));
            if (cell.x < 0)
            {
                result = world;
                return false;
            }

            result = CellToWorld(cell, world.y);
            return true;
        }

        public Vector3 ResolveFallbackSpawn(Vector3 initialFeet, Vector3? requested)
        {
            var candidate = requested ?? (IsWalkable(initialFeet) ? initialFeet : TianyongMapDefinition.DefaultSpawn);
            candidate = ClampXZ(candidate, 2f);
            if (IsWalkable(candidate)) return candidate;
            if (TryFindNearestWalkable(candidate, out var nearest))
            {
                nearest = ClampXZ(nearest, 2f);
                if (IsWalkable(nearest)) return nearest;
            }
            throw new System.InvalidOperationException(
                $"[CurrentMap] No walkable recovery spawn near {candidate}; supply this map's valid spawn.");
        }

        public List<Vector3> FindPath(Vector3 start, Vector3 target)
        {
            start = ClampXZ(start);
            target = ClampXZ(target);

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

        /// <summary>
        /// Lateral clearance a smoothed segment keeps from blocked cells. The
        /// walk mask is a point test but the actor is a capsule that drifts a
        /// few centimetres while following a segment; a line that merely
        /// grazes a blocked cell corner (2026-09-08: the single-cell pillar at
        /// (212,148)) would then step into it and stall. Cell-centre waypoints
        /// are always at least one metre from any wall, so a segment that fails
        /// this test simply falls back to them.
        /// </summary>
        private const float SmoothingClearance = 0.35f;

        private bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            var distance = Vector3.Distance(from, to);
            // Sample more tightly than one world unit so the smoothed segment
            // cannot slip diagonally across the corner of a blocked grid cell
            // (most visibly at a bridge/canal boundary).
            var samples = Mathf.Max(1, Mathf.CeilToInt(distance / (CellSize * 0.2f)));
            var dir = to - from;
            dir.y = 0f;
            var side = dir.sqrMagnitude > 0.0001f
                ? new Vector3(-dir.z, 0f, dir.x).normalized * SmoothingClearance
                : Vector3.zero;
            for (var i = 0; i <= samples; i++)
            {
                var p = Vector3.Lerp(from, to, i / (float)samples);
                if (!IsWalkable(p) || !IsWalkable(p + side) || !IsWalkable(p - side)) return false;
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
                Mathf.Clamp(Mathf.FloorToInt((world.x - Bounds.xMin) / CellSize), 0, Width - 1),
                Mathf.Clamp(Mathf.FloorToInt((world.z - Bounds.yMin) / CellSize), 0, Depth - 1));

        private Vector3 CellToWorld(Vector2Int cell, float y)
        {
            // A ceil-sized edge cell can be smaller than CellSize, or partly
            // covered by padding. Sample the centre of its actual usable area.
            float west = Mathf.Max(Bounds.xMin + cell.x * CellSize, EffectiveBounds.xMin);
            float east = Mathf.Min(Bounds.xMin + (cell.x + 1) * CellSize, EffectiveBounds.xMax);
            float south = Mathf.Max(Bounds.yMin + cell.y * CellSize, EffectiveBounds.yMin);
            float north = Mathf.Min(Bounds.yMin + (cell.y + 1) * CellSize, EffectiveBounds.yMax);
            return new Vector3((west + east) * .5f, y, (south + north) * .5f);
        }

        private bool CellIntersectsEffectiveBounds(int x, int z)
            => Bounds.xMin + x * CellSize < EffectiveBounds.xMax &&
               Bounds.xMin + (x + 1) * CellSize > EffectiveBounds.xMin &&
               Bounds.yMin + z * CellSize < EffectiveBounds.yMax &&
               Bounds.yMin + (z + 1) * CellSize > EffectiveBounds.yMin;

        public Vector3 ClampXZ(Vector3 world, float margin = 2f)
        {
            float mx = Mathf.Min(Mathf.Max(0, margin), EffectiveBounds.width * .5f);
            float mz = Mathf.Min(Mathf.Max(0, margin), EffectiveBounds.height * .5f);
            world.x = Mathf.Clamp(world.x, EffectiveBounds.xMin + mx, EffectiveBounds.xMax - mx);
            world.z = Mathf.Clamp(world.z, EffectiveBounds.yMin + mz, EffectiveBounds.yMax - mz);
            return world;
        }

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
