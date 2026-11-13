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
