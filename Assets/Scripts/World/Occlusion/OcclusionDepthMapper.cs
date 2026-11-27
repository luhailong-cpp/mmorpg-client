using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.World.Occlusion
{
    /// <summary>
    /// Immutable world-space fade zone compiled from <see cref="MapFadeZoneDefinition"/> for fast,
    /// allocation-free runtime containment checks.
    /// </summary>
    public sealed class CompiledFadeZone
    {
        public string Id { get; }
        public float TargetAlpha { get; }
        public float FadeDurationSec { get; }
        public Rect WorldBounds { get; }
        public IReadOnlyList<Vector2> PolygonWorldXZ => _polygonWorldXZ;

        private readonly Vector2[] _polygonWorldXZ;

        public CompiledFadeZone(string id, float targetAlpha, float fadeDurationSec, Vector2[] polygonWorldXZ)
        {
            Id = id ?? string.Empty;
            TargetAlpha = Mathf.Clamp01(targetAlpha);
            FadeDurationSec = Mathf.Max(0f, fadeDurationSec);
            _polygonWorldXZ = polygonWorldXZ ?? Array.Empty<Vector2>();
            WorldBounds = ComputeBounds(_polygonWorldXZ);
        }

        private static Rect ComputeBounds(Vector2[] polygon)
        {
            if (polygon.Length == 0) return default;
            float minX = float.PositiveInfinity;
            float minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxZ = float.NegativeInfinity;
            for (int i = 0; i < polygon.Length; i++)
            {
                var p = polygon[i];
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minZ) minZ = p.y;
                if (p.y > maxZ) maxZ = p.y;
            }
            return Rect.MinMaxRect(minX, minZ, maxX, maxZ);
        }
    }

    /// <summary>
    /// Pure C# depth-mapping and 2D containment math for top-down orthographic 2.5D occlusion.
    /// Keeps logic independent of Unity lifecycle for deterministic headless testing.
    /// </summary>
    public static class OcclusionDepthMapper
    {
        /// <summary>
        /// Depth-band Y range in world units under the straight-down orthographic camera (Y=110 looking -Y).
        /// A 20-unit span gives >2,600 distinct levels even on worst-case 16-bit mobile/WebGL depth buffers
        /// while staying well above ground tiles (Y <= 0) and well below nameplates (Y = 22).
        /// </summary>
        public const float DepthYMin = 1f;
        public const float DepthYMax = 21f;

        /// <summary>Forward clearance along -camera.forward so nameplates and bubbles stay above all occluders.</summary>
        public const float LabelClearanceY = 22f;

        /// <summary>Slight offset keeping contact shadows beneath their owner yet still occluded behind south roofs.</summary>
        public const float ShadowDepthOffset = -0.01f;

        /// <summary>Slight offset keeping click markers beneath shadows yet occluded behind south roofs.</summary>
        public const float ClickMarkerDepthOffset = -0.02f;

        /// <summary>Default silhouette opacity when an actor is behind an occluder.</summary>
        public const float DefaultOccludedAlpha = 0.45f;

        /// <summary>Maps world Z (north = yMax, south = yMin) into top-down depth Y (south closer to camera).</summary>
        public static float WorldZToDepthY(float worldZ, Rect worldRect)
        {
            float span = worldRect.height;
            if (span <= 1e-4f || float.IsNaN(worldZ) || float.IsInfinity(worldZ))
                return DepthYMin;

            float t = Mathf.Clamp01((worldRect.yMax - worldZ) / span);
            return DepthYMin + t * (DepthYMax - DepthYMin);
        }

        /// <summary>Converts source-image pixel coordinates (origin north-west, +Y south) to world X/Z.</summary>
        public static Vector2 PixelToWorld(Vector2 pixel, int imageWidth, int imageHeight, Rect worldRect)
        {
            if (imageWidth <= 0 || imageHeight <= 0) return worldRect.center;
            float x = worldRect.xMin + pixel.x / imageWidth * worldRect.width;
            float z = worldRect.yMax - pixel.y / imageHeight * worldRect.height;
            return new Vector2(x, z);
        }

        /// <summary>
        /// Evaluates the ground-contact baseline Z at a given world X.
        /// Supports horizontal props as well as diagonal walls/eaves without segment cracking.
        /// </summary>
        public static float EvaluateBaselineZ(Vector2 p0World, Vector2 p1World, float worldX)
        {
            float dx = p1World.x - p0World.x;
            if (Mathf.Abs(dx) <= 1e-4f)
                return Mathf.Min(p0World.y, p1World.y);

            float t = Mathf.Clamp01((worldX - p0World.x) / dx);
            return Mathf.Lerp(p0World.y, p1World.y, t);
        }

        /// <summary>Compiles pixel-space fade zones into world-space AABB + polygon descriptors.</summary>
        public static CompiledFadeZone[] CompileFadeZones(MapOcclusionData data, Rect worldRect)
        {
            if (data == null || !data.HasFadeZones)
                return Array.Empty<CompiledFadeZone>();

            var compiled = new CompiledFadeZone[data.fadeZones.Length];
            for (int i = 0; i < data.fadeZones.Length; i++)
            {
                var zone = data.fadeZones[i];
                var worldPoints = new Vector2[zone.polygonPixels.Length];
                for (int p = 0; p < zone.polygonPixels.Length; p++)
                    worldPoints[p] = PixelToWorld(zone.polygonPixels[p].ToVector2(), data.imageWidth, data.imageHeight, worldRect);

                compiled[i] = new CompiledFadeZone(zone.id, zone.targetAlpha, zone.fadeDurationSec, worldPoints);
            }
            return compiled;
        }

        /// <summary>
        /// Evaluates all fade zones at an actor's feet (world X/Z). When multiple zones overlap,
        /// selects the lowest targetAlpha so leaving one zone cannot prematurely restore full opacity.
        /// </summary>
        public static bool EvaluateFadeZones(
            Vector2 worldXZ,
            IReadOnlyList<CompiledFadeZone> zones,
            out float targetAlpha,
            out float fadeDurationSec)
        {
            targetAlpha = 1f;
            fadeDurationSec = 0f;
            if (zones == null || zones.Count == 0) return false;

            bool matched = false;
            for (int i = 0; i < zones.Count; i++)
            {
                var zone = zones[i];
                if (zone == null) continue;
                if (!zone.WorldBounds.Contains(worldXZ)) continue;
                if (!PointInPolygon(worldXZ, zone.PolygonWorldXZ)) continue;

                if (!matched || zone.TargetAlpha < targetAlpha)
                {
                    targetAlpha = zone.TargetAlpha;
                    fadeDurationSec = zone.FadeDurationSec;
                    matched = true;
                }
            }
            return matched;
        }

        /// <summary>Even-odd ray casting test for simple 2D polygons (zero heap allocations).</summary>
        public static bool PointInPolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3) return false;

            bool inside = false;
            int count = polygon.Count;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];

                if (IsPointOnSegment(point, a, b, 1e-5f))
                    return true;

                bool crossesY = (a.y > point.y) != (b.y > point.y);
                if (!crossesY) continue;

                float intersectX = a.x + (point.y - a.y) * (b.x - a.x) / (b.y - a.y);
                if (point.x < intersectX)
                    inside = !inside;
            }
            return inside;
        }

        private static bool IsPointOnSegment(Vector2 p, Vector2 a, Vector2 b, float epsilon)
        {
            float cross = (p.y - a.y) * (b.x - a.x) - (p.x - a.x) * (b.y - a.y);
            if (Mathf.Abs(cross) > epsilon * Mathf.Max(1f, (b - a).magnitude)) return false;

            float minX = Mathf.Min(a.x, b.x) - epsilon;
            float maxX = Mathf.Max(a.x, b.x) + epsilon;
            float minY = Mathf.Min(a.y, b.y) - epsilon;
            float maxY = Mathf.Max(a.y, b.y) + epsilon;
            return p.x >= minX && p.x <= maxX && p.y >= minY && p.y <= maxY;
        }
    }

    /// <summary>
    /// Pure C# smooth alpha state machine for area fade zones (scenario ①).
    /// Driven by monotonic unscaled delta time; avoids abrupt pops on overlap transitions.
    /// </summary>
    public sealed class ActorTranslucencyModel
    {
        public const float DefaultFadeDurationSec = 0.2f;

        private float _currentAlpha = 1f;
        private float _startAlpha = 1f;
        private float _goalAlpha = 1f;
        private float _durationSec = DefaultFadeDurationSec;
        private float _lastZoneFadeDurationSec = DefaultFadeDurationSec;
        private float _elapsedSec;

        public float CurrentAlpha => _currentAlpha;
        public float GoalAlpha => _goalAlpha;

        public void Reset()
        {
            _currentAlpha = 1f;
            _startAlpha = 1f;
            _goalAlpha = 1f;
            _durationSec = DefaultFadeDurationSec;
            _lastZoneFadeDurationSec = DefaultFadeDurationSec;
            _elapsedSec = 0f;
        }

        public float Step(bool insideZone, float zoneTargetAlpha, float zoneFadeDurationSec, float deltaTimeSec)
        {
            float desiredAlpha = insideZone ? Mathf.Clamp01(zoneTargetAlpha) : 1f;
            float transitionDuration = insideZone
                ? Mathf.Max(0f, zoneFadeDurationSec)
                : _lastZoneFadeDurationSec;

            if (insideZone)
                _lastZoneFadeDurationSec = Mathf.Max(0f, zoneFadeDurationSec);

            if (Mathf.Abs(desiredAlpha - _goalAlpha) > 1e-4f)
            {
                _startAlpha = _currentAlpha;
                _goalAlpha = desiredAlpha;
                _durationSec = transitionDuration;
                _elapsedSec = 0f;
            }

            if (Mathf.Abs(_currentAlpha - _goalAlpha) <= 1e-4f)
            {
                _currentAlpha = _goalAlpha;
                return _currentAlpha;
            }

            if (_durationSec <= 1e-4f)
            {
                _currentAlpha = _goalAlpha;
                return _currentAlpha;
            }

            float dt = float.IsNaN(deltaTimeSec) || float.IsInfinity(deltaTimeSec) ? 0f : Mathf.Max(0f, deltaTimeSec);
            _elapsedSec += dt;
            float t = Mathf.Clamp01(_elapsedSec / _durationSec);
            _currentAlpha = Mathf.Lerp(_startAlpha, _goalAlpha, t);
            return _currentAlpha;
        }
    }
}
