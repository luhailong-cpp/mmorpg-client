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
        private readonly int[] _domainIds;

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
            _domainIds = LabelConnectedDomains();
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

        public TianyongNavigationGrid(float cellSize, System.Func<Vector3, bool> walkableSampler, Rect bounds, Rect effectiveBounds,
            bool preciseNetworkMovement = false)
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
            PreciseNetworkMovement = preciseNetworkMovement;
            CellSize = Mathf.Max(0.5f, cellSize);
            Width = Mathf.CeilToInt(bounds.width / CellSize);
            Depth = Mathf.CeilToInt(bounds.height / CellSize);
            _walkable = new bool[Width, Depth];

            for (var x = 0; x < Width; x++)
            for (var z = 0; z < Depth; z++)
                _walkable[x, z] = CellIntersectsEffectiveBounds(x, z) && walkableSampler != null &&
                                  walkableSampler(CellToWorld(new Vector2Int(x, z), 0f));
            _domainIds = LabelConnectedDomains();
        }

        public float CellSize { get; }
        public Rect Bounds { get; }
        public Rect EffectiveBounds { get; }
        public int Width { get; }
        public int Depth { get; }
        public bool IsAuthoredFlat => false;
        internal bool PreciseNetworkMovement { get; }

        public bool IsSegmentWalkable(Vector3 from, Vector3 to)
            => PreciseNetworkMovement ? IsMaskSegmentWalkable(from, to) : HasLineOfSight(from, to);

        // Match the server's FMASK segment rule. The path smoother's side clearance is
        // intentionally wider and can reject legal movement through a single grid cell.
        private bool IsMaskSegmentWalkable(Vector3 from, Vector3 to)
        {
            if (!IsWalkable(from) || !IsWalkable(to)) return false;
            var cell = WorldToCell(from);
            var end = WorldToCell(to);
            double dx = (double)to.x - from.x, dz = (double)to.z - from.z;
            int stepX = System.Math.Sign(dx), stepZ = System.Math.Sign(dz);
            double deltaX = stepX == 0 ? double.PositiveInfinity : CellSize / System.Math.Abs(dx);
            double deltaZ = stepZ == 0 ? double.PositiveInfinity : CellSize / System.Math.Abs(dz);
            double nextX = cell.x == end.x ? double.PositiveInfinity :
                (Bounds.xMin + (cell.x + (stepX > 0 ? 1 : 0)) * (double)CellSize - from.x) / dx;
            double nextZ = cell.y == end.y ? double.PositiveInfinity :
                (Bounds.yMin + (cell.y + (stepZ > 0 ? 1 : 0)) * (double)CellSize - from.z) / dz;
            while (cell != end)
            {
                bool corner = System.Math.Abs(nextX - nextZ) <= 1e-12;
                bool advanceX = corner || nextX < nextZ;
                if (corner && (!IsWalkable(cell.x + stepX, cell.y) || !IsWalkable(cell.x, cell.y + stepZ)))
                    return false;
                if (advanceX)
                {
                    cell.x += stepX;
                    nextX = cell.x == end.x ? double.PositiveInfinity : nextX + deltaX;
                }
                if (corner || !advanceX)
                {
                    cell.y += stepZ;
                    nextZ = cell.y == end.y ? double.PositiveInfinity : nextZ + deltaZ;
                }
                if (!IsWalkable(cell.x, cell.y)) return false;
            }
            return true;
        }

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
            if (!Finite(start) || !Finite(target)) return new List<Vector3>();
            start = ClampXZ(start);
            target = ClampXZ(target);

            var startCell = FindNearestWalkable(WorldToCell(start));
            var targetCell = FindNearestWalkable(WorldToCell(target));
            if (startCell.x < 0 || targetCell.x < 0)
                return new List<Vector3>();
            if (_domainIds[CellIndex(startCell.x, startCell.y)] != _domainIds[CellIndex(targetCell.x, targetCell.y)])
                return new List<Vector3>();

            // A click may land on a roof, wall or the canal. Route to the
            // nearest legal cell instead of appending the blocked click point
            // as the final waypoint.
            var resolvedStart = IsWalkable(start) ? start : CellToWorld(startCell, start.y);
            var resolvedTarget = IsWalkable(target) ? target : CellToWorld(targetCell, target.y);

            return RunAStar(resolvedStart, resolvedTarget, startCell, targetCell);
        }

        public List<Vector3> FindApproachPath(Vector3 start, Vector3 target)
        {
            if (!Finite(start) || !Finite(target)) return new List<Vector3>();

            Vector2Int startCell;
            Vector3 resolvedStart;
            if (IsWalkable(start))
            {
                startCell = WorldToCell(start);
                resolvedStart = start;
            }
            else
            {
                startCell = FindNearestWalkable(WorldToCell(ClampXZ(start, 0f)));
                if (startCell.x < 0) return new List<Vector3>();
                resolvedStart = CellToWorld(startCell, start.y);
            }

            var startDomain = _domainIds[CellIndex(startCell.x, startCell.y)];
            if (startDomain == 0) return new List<Vector3>();

            // 快速路径：目标本身合法且与起点处于同一连通域，直接走精确目标点。
            if (IsWalkable(target))
            {
                var directCell = WorldToCell(target);
                if (_domainIds[CellIndex(directCell.x, directCell.y)] == startDomain)
                {
                    var exactTarget = new Vector3(target.x, resolvedStart.y, target.z);
                    if (directCell == startCell && HorizontalDistanceSq(resolvedStart, exactTarget) <= 0.0025f)
                        return new List<Vector3> { resolvedStart };
                    return RunAStar(resolvedStart, exactTarget, startCell, directCell);
                }
            }

            // 阻挡/越界/孤岛目标：在起点连通域内按几何下界环扩求严格欧氏最近可走格。
            if (!TryFindNearestReachableCell(startDomain, resolvedStart, target, out var targetCell))
                return new List<Vector3>();

            var resolvedTarget = CellToWorld(targetCell, resolvedStart.y);
            if (targetCell == startCell && HorizontalDistanceSq(resolvedStart, resolvedTarget) <= 0.0025f)
                return new List<Vector3> { resolvedStart };

            return RunAStar(resolvedStart, resolvedTarget, startCell, targetCell);
        }

        private List<Vector3> RunAStar(
            Vector3 resolvedStart,
            Vector3 resolvedTarget,
            Vector2Int startCell,
            Vector2Int targetCell)
        {
            if (startCell == targetCell)
                return new List<Vector3> { resolvedStart, resolvedTarget };

            var total = Width * Depth;
            var g = new float[total];
            var closed = new bool[total];
            var hasParent = new bool[total];
            var parent = new int[total];
            for (var i = 0; i < total; i++) g[i] = float.PositiveInfinity;

            var startIdx = CellIndex(startCell.x, startCell.y);
            var targetIdx = CellIndex(targetCell.x, targetCell.y);
            g[startIdx] = 0f;

            var heapCells = new int[64];
            var heapScores = new float[64];
            var heapCount = 0;
            HeapPush(ref heapCells, ref heapScores, ref heapCount, startIdx, Heuristic(startCell, targetCell));

            while (heapCount > 0)
            {
                var currentIdx = HeapPop(heapCells, heapScores, ref heapCount);
                if (closed[currentIdx]) continue;
                closed[currentIdx] = true;

                if (currentIdx == targetIdx)
                    return BuildWorldPath(resolvedStart, resolvedTarget, startIdx, targetIdx, parent, hasParent);

                var cx = currentIdx % Width;
                var cz = currentIdx / Width;
                var currentG = g[currentIdx];

                foreach (var direction in Directions)
                {
                    var nx = cx + direction.x;
                    var nz = cz + direction.y;
                    if (!IsWalkable(nx, nz)) continue;
                    var nextIdx = CellIndex(nx, nz);
                    if (closed[nextIdx]) continue;

                    var diagonal = direction.x != 0 && direction.y != 0;
                    if (diagonal &&
                        (!IsWalkable(cx + direction.x, cz) ||
                         !IsWalkable(cx, cz + direction.y)))
                        continue;

                    var tentative = currentG + (diagonal ? 1.41421356f : 1f);
                    if (tentative >= g[nextIdx]) continue;

                    g[nextIdx] = tentative;
                    parent[nextIdx] = currentIdx;
                    hasParent[nextIdx] = true;
                    HeapPush(ref heapCells, ref heapScores, ref heapCount,
                        nextIdx, tentative + Heuristic(new Vector2Int(nx, nz), targetCell));
                }
            }

            return new List<Vector3>();
        }

        private List<Vector3> BuildWorldPath(
            Vector3 exactStart,
            Vector3 exactTarget,
            int startIdx,
            int targetIdx,
            int[] parent,
            bool[] hasParent)
        {
            var cells = new List<Vector2Int>();
            var current = targetIdx;
            cells.Add(new Vector2Int(current % Width, current / Width));
            while (current != startIdx)
            {
                if (!hasParent[current]) return new List<Vector3>();
                current = parent[current];
                cells.Add(new Vector2Int(current % Width, current / Width));
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
                    if (PreciseNetworkMovement && !IsMaskSegmentWalkable(raw[anchor], raw[candidate])) continue;
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

        private int CellIndex(int x, int z) => z * Width + x;

        // 加载期预计算 4-连通域编号：因对角步禁止穿角（要求两正交邻格同时可走），
        // 8-邻无穿角的可达等价类与 4-连通域严格一致。
        private int[] LabelConnectedDomains()
        {
            var domains = new int[Width * Depth];
            var queue = new int[domains.Length];
            var nextLabel = 0;
            for (var start = 0; start < domains.Length; start++)
            {
                if (domains[start] != 0) continue;
                var sx = start % Width;
                var sz = start / Width;
                if (!_walkable[sx, sz]) continue;

                domains[start] = ++nextLabel;
                var head = 0;
                var tail = 0;
                queue[tail++] = start;
                while (head < tail)
                {
                    var cur = queue[head++];
                    var cx = cur % Width;
                    var cz = cur / Width;
                    for (var dir = 0; dir < 4; dir++)
                    {
                        var nx = cx + (dir == 0 ? 1 : dir == 1 ? -1 : 0);
                        var nz = cz + (dir == 2 ? 1 : dir == 3 ? -1 : 0);
                        if (!IsWalkable(nx, nz)) continue;
                        var next = CellIndex(nx, nz);
                        if (domains[next] != 0) continue;
                        domains[next] = nextLabel;
                        queue[tail++] = next;
                    }
                }
            }
            return domains;
        }

        private bool TryFindNearestReachableCell(int startDomain, Vector3 resolvedStart, Vector3 target, out Vector2Int bestCell)
        {
            bestCell = default;
            var seedX = Mathf.Clamp(Mathf.FloorToInt((target.x - Bounds.xMin) / CellSize), 0, Width - 1);
            var seedZ = Mathf.Clamp(Mathf.FloorToInt((target.z - Bounds.yMin) / CellSize), 0, Depth - 1);
            var maxRadius = Mathf.Max(
                Mathf.Max(seedX, Width - 1 - seedX),
                Mathf.Max(seedZ, Depth - 1 - seedZ));

            var outsideX = target.x < EffectiveBounds.xMin ? EffectiveBounds.xMin - target.x
                : target.x > EffectiveBounds.xMax ? target.x - EffectiveBounds.xMax : 0f;
            var outsideZ = target.z < EffectiveBounds.yMin ? EffectiveBounds.yMin - target.z
                : target.z > EffectiveBounds.yMax ? target.z - EffectiveBounds.yMax : 0f;

            var found = false;
            var candidateBest = default(Vector2Int);
            var bestDistSq = float.PositiveInfinity;
            var bestStartDistSq = float.PositiveInfinity;

            void Evaluate(int x, int z)
            {
                if (_domainIds[CellIndex(x, z)] != startDomain) return;
                var center = CellToWorld(new Vector2Int(x, z), resolvedStart.y);
                var dx = center.x - target.x;
                var dz = center.z - target.z;
                var distSq = dx * dx + dz * dz;
                var sx = center.x - resolvedStart.x;
                var sz = center.z - resolvedStart.z;
                var startDistSq = sx * sx + sz * sz;
                if (!found || distSq < bestDistSq - 1e-4f ||
                    (Mathf.Abs(distSq - bestDistSq) <= 1e-4f && startDistSq < bestStartDistSq))
                {
                    found = true;
                    candidateBest = new Vector2Int(x, z);
                    bestDistSq = distSq;
                    bestStartDistSq = startDistSq;
                }
            }

            for (var r = 0; r <= maxRadius; r++)
            {
                if (found && r > 0)
                {
                    var minVertXGap = float.PositiveInfinity;
                    if (seedX + r < Width)
                        minVertXGap = Mathf.Min(minVertXGap, Mathf.Max(0f, Bounds.xMin + (seedX + r) * CellSize - target.x));
                    if (seedX - r >= 0)
                        minVertXGap = Mathf.Min(minVertXGap, Mathf.Max(0f, target.x - (Bounds.xMin + (seedX - r + 1) * CellSize)));

                    var minHorizZGap = float.PositiveInfinity;
                    if (seedZ + r < Depth)
                        minHorizZGap = Mathf.Min(minHorizZGap, Mathf.Max(0f, Bounds.yMin + (seedZ + r) * CellSize - target.z));
                    if (seedZ - r >= 0)
                        minHorizZGap = Mathf.Min(minHorizZGap, Mathf.Max(0f, target.z - (Bounds.yMin + (seedZ - r + 1) * CellSize)));

                    var vertMinSq = float.IsPositiveInfinity(minVertXGap) ? float.PositiveInfinity
                        : minVertXGap * minVertXGap + outsideZ * outsideZ;
                    var horizMinSq = float.IsPositiveInfinity(minHorizZGap) ? float.PositiveInfinity
                        : outsideX * outsideX + minHorizZGap * minHorizZGap;
                    if (Mathf.Min(vertMinSq, horizMinSq) > bestDistSq + 1e-4f) break;
                }

                if (r == 0)
                {
                    Evaluate(seedX, seedZ);
                    continue;
                }

                var minX = Mathf.Max(0, seedX - r);
                var maxX = Mathf.Min(Width - 1, seedX + r);
                var minZ = Mathf.Max(0, seedZ - r + 1);
                var maxZ = Mathf.Min(Depth - 1, seedZ + r - 1);

                var topZ = seedZ + r;
                if (topZ < Depth)
                    for (var x = minX; x <= maxX; x++) Evaluate(x, topZ);

                var bottomZ = seedZ - r;
                if (bottomZ >= 0)
                    for (var x = minX; x <= maxX; x++) Evaluate(x, bottomZ);

                var leftX = seedX - r;
                if (leftX >= 0)
                    for (var z = minZ; z <= maxZ; z++) Evaluate(leftX, z);

                var rightX = seedX + r;
                if (rightX < Width)
                    for (var z = minZ; z <= maxZ; z++) Evaluate(rightX, z);
            }

            bestCell = candidateBest;
            return found;
        }

        private static void HeapPush(ref int[] cells, ref float[] scores, ref int count, int cell, float score)
        {
            if (count == cells.Length)
            {
                System.Array.Resize(ref cells, cells.Length * 2);
                System.Array.Resize(ref scores, scores.Length * 2);
            }
            var index = count++;
            while (index > 0)
            {
                var parent = (index - 1) >> 1;
                if (scores[parent] <= score) break;
                cells[index] = cells[parent];
                scores[index] = scores[parent];
                index = parent;
            }
            cells[index] = cell;
            scores[index] = score;
        }

        private static int HeapPop(int[] cells, float[] scores, ref int count)
        {
            var top = cells[0];
            count--;
            if (count > 0)
            {
                var lastCell = cells[count];
                var lastScore = scores[count];
                var index = 0;
                while (true)
                {
                    var left = (index << 1) + 1;
                    if (left >= count) break;
                    var right = left + 1;
                    var smallest = right < count && scores[right] < scores[left] ? right : left;
                    if (lastScore <= scores[smallest]) break;
                    cells[index] = cells[smallest];
                    scores[index] = scores[smallest];
                    index = smallest;
                }
                cells[index] = lastCell;
                scores[index] = lastScore;
            }
            return top;
        }

        private static float HorizontalDistanceSq(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static bool Finite(Vector3 value)
            => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);

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
