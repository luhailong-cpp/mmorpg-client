using System;
using System.Collections.Generic;
using MmorpgClient.World.NavigationAuthoring;
using UnityEngine;
using UnityEngine.AI;

namespace MmorpgClient.World.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// Queries one authored-flat map. Construction is inert: the map owner must
    /// remove the previous instance before activating this map's global data.
    /// </summary>
    public sealed class AuthoredNavMeshNavigation : IMapNavigation, IDisposable
    {
        private const float SampleRadius = 1.8f;
        private const float RecoveryRadius = 1f;
        private const int MaximumPathCorners = 128;
        private const int MaximumPathRaycasts = 20000;
        private static AuthoredNavMeshNavigation s_active;

        private readonly NavMeshData _data;
        private readonly Rect _bounds;
        private readonly Vector3 _spawn;
        private readonly NavMeshQueryFilter _filter;
        private NavMeshDataInstance _instance;
        private bool _disposed;
        private Vector3[] _triVertices;
        private int[] _triIndices;
        private int[] _triDomains;
        private bool _triangulationBuilt;

        public AuthoredNavMeshNavigation(NavMeshData data, Rect bounds, Vector3 spawn)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (!Finite(bounds.xMin) || !Finite(bounds.yMin) || !Finite(bounds.xMax) ||
                !Finite(bounds.yMax) || bounds.width <= 0f || bounds.height <= 0f)
                throw new ArgumentException("A finite, positive authored map rectangle is required.", nameof(bounds));
            if (!Finite(spawn) || spawn.y != 0f || !Contains(bounds, spawn))
                throw new ArgumentException("The shared authored map spawn must lie on the original plane and inside its bounds.", nameof(spawn));
            _data = data;
            _bounds = bounds;
            _spawn = spawn;
            // Both delivered authored maps were baked for agent type 0.
            // NavMeshData in the pinned Unity API does not expose that value.
            _filter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
        }

        public bool IsAuthoredFlat => true;
        public bool IsActive => !_disposed && _instance.valid && ReferenceEquals(s_active, this);

        public void Activate()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AuthoredNavMeshNavigation));
            if (IsActive) return;
            if (s_active != null && s_active.IsActive)
                throw new InvalidOperationException("The previous authored NavMesh must be deactivated before activating another map.");

            _instance = NavMesh.AddNavMeshData(_data);
            if (!_instance.valid)
                throw new InvalidOperationException("The authored NavMesh could not be activated.");
            s_active = this;
            if (IsWalkable(_spawn)) return;
            Deactivate();
            throw new InvalidOperationException("The shared authored map spawn does not match the activated NavMesh.");
        }

        public void Deactivate()
        {
            if (_instance.valid) _instance.Remove();
            _instance = default;
            _triVertices = null;
            _triIndices = null;
            _triDomains = null;
            _triangulationBuilt = false;
            if (ReferenceEquals(s_active, this)) s_active = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Deactivate();
            _disposed = true;
        }

        public bool IsWalkable(Vector3 world)
        {
            if (!IsActive || !Finite(world) || !Contains(_bounds, world) ||
                Mathf.Abs(world.y) > NavMeshAuthoringDraft.GroundPointTolerance) return false;
            return NavMesh.SamplePosition(world, out var hit, SampleRadius, _filter) &&
                   Contains(_bounds, hit.position) &&
                   Mathf.Abs(hit.position.y) <= NavMeshAuthoringDraft.GroundPointTolerance &&
                   NavMeshAuthoringDraft.MatchesGroundPoint(world, hit.position);
        }

        // Explicit recovery only; normal click and movement validation use IsWalkable.
        public bool TryFindNearestWalkable(Vector3 world, out Vector3 result)
        {
            result = world;
            if (!IsActive || !Finite(world) || !Contains(_bounds, world) ||
                Mathf.Abs(world.y) > NavMeshAuthoringDraft.GroundPointTolerance ||
                !NavMesh.SamplePosition(world, out var hit, RecoveryRadius, _filter)) return false;
            var candidate = new Vector3(hit.position.x, 0f, hit.position.z);
            if (!IsWalkable(candidate)) return false;
            result = candidate;
            return true;
        }

        // Authored bounds are checked, never silently moved to a different target.
        public Vector3 ClampXZ(Vector3 world, float margin = 2f) => world;

        public Vector3 ResolveFallbackSpawn(Vector3 initialFeet, Vector3? requested)
        {
            var candidate = requested ?? _spawn;
            if (IsWalkable(candidate)) return new Vector3(candidate.x, 0f, candidate.z);
            throw new InvalidOperationException("The requested authored map spawn is not on its active NavMesh.");
        }

        public bool IsSegmentWalkable(Vector3 from, Vector3 to)
        {
            if (!IsWalkable(from) || !IsWalkable(to)) return false;
            return !NavMesh.Raycast(from, to, out _, _filter);
        }

        public List<Vector3> FindPath(Vector3 start, Vector3 target)
        {
            var result = new List<Vector3>();
            if (!IsWalkable(start) || !IsWalkable(target)) return result;
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(start, target, _filter, path) || path.status != NavMeshPathStatus.PathComplete)
                return result;
            var corners = path.corners;
            if (corners.Length == 0 || corners.Length > MaximumPathCorners) return result;
            // These two maps are explicitly flat Y=0. Unity's raw corner Y can
            // differ from its HeightMesh sampling; never inherit a stale foot Y.
            var rays = 0;
            var layers = new List<List<Vector3>> { new List<Vector3> { new Vector3(start.x, 0f, start.z) } };
            for (var i = 1; i < corners.Length - 1; i++)
            {
                var corner = new Vector3(corners[i].x, 0f, corners[i].z);
                var layer = new List<Vector3>();
                foreach (var radius in new[] { .2f, .4f, .08f, .03f })
                    for (var angle = 0; angle < 16; angle++)
                    {
                        var radians = angle * Mathf.PI / 8f;
                        AddCandidate(layer, corner + new Vector3(Mathf.Cos(radians) * radius, 0f, Mathf.Sin(radians) * radius));
                    }
                AddCandidate(layer, corner);
                if (layer.Count == 0) return result;
                layers.Add(layer);
            }
            layers.Add(new List<Vector3> { new Vector3(target.x, 0f, target.z) });
            var parents = new List<int[]> { new[] { 0 } };
            for (var layerIndex = 1; layerIndex < layers.Count; layerIndex++)
            {
                var layer = layers[layerIndex];
                var previous = layers[layerIndex - 1];
                var predecessor = new int[layer.Count];
                for (var candidate = 0; candidate < layer.Count; candidate++)
                {
                    predecessor[candidate] = -1;
                    for (var from = 0; from < previous.Count; from++)
                    {
                        if (parents[layerIndex - 1][from] < 0) continue;
                        if (++rays > MaximumPathRaycasts) return new List<Vector3>();
                        if (NavMesh.Raycast(previous[from], layer[candidate], out _, _filter)) continue;
                        predecessor[candidate] = from;
                        break;
                    }
                }
                parents.Add(predecessor);
            }
            int cursor = 0;
            if (parents[parents.Count - 1][cursor] < 0) return result;
            for (var layerIndex = layers.Count - 1; layerIndex >= 0; layerIndex--)
            {
                result.Add(layers[layerIndex][cursor]);
                cursor = parents[layerIndex][cursor];
            }
            result.Reverse();
            return result;
        }

        public List<Vector3> FindApproachPath(Vector3 start, Vector3 target)
        {
            var empty = new List<Vector3>();
            if (!IsActive || !Finite(start) || !Finite(target) || !IsWalkable(start)) return empty;

            var flatStart = new Vector3(start.x, 0f, start.z);
            var flatTarget = new Vector3(target.x, 0f, target.z);
            if (IsWalkable(flatTarget))
            {
                if (HorizontalDistanceSq(flatStart, flatTarget) <= 0.0025f)
                    return new List<Vector3> { flatStart };
                var direct = FindPath(flatStart, flatTarget);
                if (direct.Count > 0) return direct;
            }

            // 优先基于 NavMesh 三角网连通分量精确投影：无 128 多边形截断，也不被孤岛吸偏。
            EnsureTriangulation();
            if (_triDomains != null && _triDomains.Length > 0 &&
                TryFindStartDomain(flatStart, out var startDomain))
            {
                var triCount = _triDomains.Length;
                var candidates = new List<(float distSq, float startDistSq, Vector3 point, Vector3 centroid)>(16);
                for (var t = 0; t < triCount; t++)
                {
                    if (_triDomains[t] != startDomain) continue;
                    var a = _triVertices[_triIndices[t * 3]];
                    var b = _triVertices[_triIndices[t * 3 + 1]];
                    var c = _triVertices[_triIndices[t * 3 + 2]];
                    var closest = ClosestPointOnTriangleXZ(flatTarget, a, b, c);
                    var distSq = HorizontalDistanceSq(closest, flatTarget);
                    var startDistSq = HorizontalDistanceSq(closest, flatStart);
                    var centroid = new Vector3((a.x + b.x + c.x) / 3f, 0f, (a.z + b.z + c.z) / 3f);
                    candidates.Add((distSq, startDistSq, closest, centroid));
                }

                candidates.Sort((x, y) =>
                {
                    if (Mathf.Abs(x.distSq - y.distSq) > 1e-4f) return x.distSq.CompareTo(y.distSq);
                    return x.startDistSq.CompareTo(y.startDistSq);
                });

                var maxTry = Mathf.Min(candidates.Count, 12);
                for (var i = 0; i < maxTry; i++)
                {
                    var cand = candidates[i];
                    var path = TryPlanToBoundaryCandidate(flatStart, cand.point, cand.centroid, flatTarget);
                    if (path.Count > 0) return path;
                }
            }

            // 兜底：使用 Unity NavMesh 分级 SamplePosition + PathPartial。
            float[] radii = { 2f, 6f, 18f, 50f, 150f, 400f };
            for (var i = 0; i < radii.Length; i++)
            {
                if (!NavMesh.SamplePosition(flatTarget, out var hit, radii[i], _filter)) continue;
                var navPath = new NavMeshPath();
                if (!NavMesh.CalculatePath(flatStart, hit.position, _filter, navPath)) continue;
                if ((navPath.status != NavMeshPathStatus.PathComplete &&
                     navPath.status != NavMeshPathStatus.PathPartial) ||
                    navPath.corners.Length == 0) continue;

                var end = new Vector3(navPath.corners[navPath.corners.Length - 1].x, 0f,
                    navPath.corners[navPath.corners.Length - 1].z);
                var pullToward = navPath.corners.Length >= 2
                    ? new Vector3(navPath.corners[navPath.corners.Length - 2].x, 0f,
                        navPath.corners[navPath.corners.Length - 2].z)
                    : flatStart;
                var fallback = TryPlanToBoundaryCandidate(flatStart, end, pullToward, flatTarget);
                if (fallback.Count > 0) return fallback;
            }

            return empty;
        }

        private List<Vector3> TryPlanToBoundaryCandidate(
            Vector3 flatStart, Vector3 boundaryPoint, Vector3 interiorHint, Vector3 rawTarget)
        {
            var probes = new List<Vector3>(24) { boundaryPoint };
            var inward = interiorHint - boundaryPoint;
            inward.y = 0f;
            var inwardLen = inward.magnitude;
            if (inwardLen > 1e-4f)
            {
                var dir = inward / inwardLen;
                foreach (var inset in new[] { 0.03f, 0.08f, 0.2f, 0.4f })
                {
                    if (inset < inwardLen) probes.Add(boundaryPoint + dir * inset);
                }
                probes.Add(interiorHint);
            }
            foreach (var radius in new[] { 0.05f, 0.15f, 0.35f })
            {
                for (var angle = 0; angle < 8; angle++)
                {
                    var rad = angle * Mathf.PI / 4f;
                    probes.Add(boundaryPoint + new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius));
                }
            }

            probes.Sort((a, b) => HorizontalDistanceSq(a, rawTarget).CompareTo(HorizontalDistanceSq(b, rawTarget)));
            for (var i = 0; i < probes.Count; i++)
            {
                var probe = new Vector3(probes[i].x, 0f, probes[i].z);
                if (!IsWalkable(probe)) continue;
                if (HorizontalDistanceSq(flatStart, probe) <= 0.0025f)
                    return new List<Vector3> { flatStart };
                var path = FindPath(flatStart, probe);
                if (path.Count > 0) return path;
            }
            return new List<Vector3>();
        }

        private void EnsureTriangulation()
        {
            if (_triangulationBuilt) return;
            _triangulationBuilt = true;
            var tri = NavMesh.CalculateTriangulation();
            if (tri.vertices == null || tri.indices == null || tri.indices.Length < 3) return;

            var triCount = tri.indices.Length / 3;
            _triVertices = tri.vertices;
            _triIndices = tri.indices;
            _triDomains = new int[triCount];

            // 按 1mm 量化顶点坐标焊接跨 tile 公共边，构建共享边邻接表后 BFS 标记连通域。
            var edgeOwners = new Dictionary<(long, long), List<int>>(triCount * 3);
            var vertKeys = new long[_triVertices.Length];
            for (var i = 0; i < _triVertices.Length; i++)
            {
                var v = _triVertices[i];
                var qx = (long)Mathf.RoundToInt(v.x * 1000f);
                var qz = (long)Mathf.RoundToInt(v.z * 1000f);
                vertKeys[i] = (qx << 32) ^ (uint)qz;
            }

            var adj = new List<int>[triCount];
            for (var t = 0; t < triCount; t++)
            {
                adj[t] = new List<int>(3);
                var k0 = vertKeys[_triIndices[t * 3]];
                var k1 = vertKeys[_triIndices[t * 3 + 1]];
                var k2 = vertKeys[_triIndices[t * 3 + 2]];
                RegisterEdge(edgeOwners, adj, t, k0, k1);
                RegisterEdge(edgeOwners, adj, t, k1, k2);
                RegisterEdge(edgeOwners, adj, t, k2, k0);
            }

            var queue = new int[triCount];
            var nextLabel = 0;
            for (var t = 0; t < triCount; t++)
            {
                if (_triDomains[t] != 0) continue;
                _triDomains[t] = ++nextLabel;
                var head = 0;
                var tail = 0;
                queue[tail++] = t;
                while (head < tail)
                {
                    var cur = queue[head++];
                    var neighbors = adj[cur];
                    for (var i = 0; i < neighbors.Count; i++)
                    {
                        var next = neighbors[i];
                        if (_triDomains[next] != 0) continue;
                        _triDomains[next] = nextLabel;
                        queue[tail++] = next;
                    }
                }
            }
        }

        private static void RegisterEdge(
            Dictionary<(long, long), List<int>> edgeOwners, List<int>[] adj, int tri, long a, long b)
        {
            if (a == b) return;
            var key = a < b ? (a, b) : (b, a);
            if (!edgeOwners.TryGetValue(key, out var owners))
            {
                owners = new List<int>(2) { tri };
                edgeOwners[key] = owners;
                return;
            }
            for (var i = 0; i < owners.Count; i++)
            {
                var other = owners[i];
                adj[tri].Add(other);
                adj[other].Add(tri);
            }
            owners.Add(tri);
        }

        private bool TryFindStartDomain(Vector3 flatStart, out int domain)
        {
            domain = 0;
            var bestDistSq = float.PositiveInfinity;
            var bestDomain = 0;
            for (var t = 0; t < _triDomains.Length; t++)
            {
                var a = _triVertices[_triIndices[t * 3]];
                var b = _triVertices[_triIndices[t * 3 + 1]];
                var c = _triVertices[_triIndices[t * 3 + 2]];
                var closest = ClosestPointOnTriangleXZ(flatStart, a, b, c);
                var distSq = HorizontalDistanceSq(flatStart, closest);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestDomain = _triDomains[t];
                    if (distSq <= 1e-8f) break;
                }
            }
            if (bestDomain == 0 || bestDistSq > 0.25f) return false;
            domain = bestDomain;
            return true;
        }

        private static Vector3 ClosestPointOnTriangleXZ(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var abx = b.x - a.x; var abz = b.z - a.z;
            var acx = c.x - a.x; var acz = c.z - a.z;
            var apx = p.x - a.x; var apz = p.z - a.z;
            var d1 = abx * apx + abz * apz;
            var d2 = acx * apx + acz * apz;
            if (d1 <= 0f && d2 <= 0f) return new Vector3(a.x, 0f, a.z);

            var bpx = p.x - b.x; var bpz = p.z - b.z;
            var d3 = abx * bpx + abz * bpz;
            var d4 = acx * bpx + acz * bpz;
            if (d3 >= 0f && d4 <= d3) return new Vector3(b.x, 0f, b.z);

            var vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                var v = d1 / (d1 - d3);
                return new Vector3(a.x + v * abx, 0f, a.z + v * abz);
            }

            var cpx = p.x - c.x; var cpz = p.z - c.z;
            var d5 = abx * cpx + abz * cpz;
            var d6 = acx * cpx + acz * cpz;
            if (d6 >= 0f && d5 <= d6) return new Vector3(c.x, 0f, c.z);

            var vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                var w = d2 / (d2 - d6);
                return new Vector3(a.x + w * acx, 0f, a.z + w * acz);
            }

            var va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                var w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return new Vector3(b.x + w * (c.x - b.x), 0f, b.z + w * (c.z - b.z));
            }

            var denom = 1f / (va + vb + vc);
            var v2 = vb * denom;
            var w2 = vc * denom;
            return new Vector3(a.x + abx * v2 + acx * w2, 0f, a.z + abz * v2 + acz * w2);
        }

        private static float HorizontalDistanceSq(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private void AddCandidate(List<Vector3> candidates, Vector3 point)
        {
            if (IsWalkable(point)) candidates.Add(point);
        }

        private static bool Contains(Rect bounds, Vector3 value) =>
            value.x >= bounds.xMin && value.x < bounds.xMax && value.z >= bounds.yMin && value.z < bounds.yMax;

        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
