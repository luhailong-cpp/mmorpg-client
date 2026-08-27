using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Transform = UnityEngine.Transform;
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// Follow camera for the playable town. 3D themes use the classic
    /// elevated isometric angle; the painted main city is viewed straight
    /// down (the perspective is already baked into the artwork) and the view
    /// is clamped to the painting so its edges never scroll into sight.
    /// </summary>
    public sealed class TianyongCameraController
    {
        private static readonly Quaternion IsometricRotation = Quaternion.Euler(55f, 45f, 0f);
        private static readonly Quaternion TopDownRotation = Quaternion.Euler(90f, 0f, 0f);
        private const float CameraDistance = 110f;

        private readonly Camera _camera;
        private readonly float _zoomMin;
        private readonly float _zoomMax;
        private Transform _target;
        private Vector3 _focus = TianyongMapDefinition.DefaultSpawn;
        private Vector3 _velocity;
        private float _orthographicSize;
        private bool _topDown;
        private Rect _bounds = new(0f, 0f, TianyongMapDefinition.Width, TianyongMapDefinition.Depth);

        public TianyongCameraController(Camera camera)
            : this(camera, 24f, 55f, 35f)
        {
        }

        public TianyongCameraController(Camera camera, float zoomMin, float zoomMax, float zoomDefault)
        {
            _camera = camera;
            _zoomMin = Mathf.Max(1f, zoomMin);
            _zoomMax = Mathf.Max(_zoomMin, zoomMax);
            _orthographicSize = Mathf.Clamp(zoomDefault, _zoomMin, _zoomMax);
            ConfigureCamera();
        }

        public Transform Target => _target;
        public bool IsTopDown => _topDown;

        public void SetTarget(Transform target)
        {
            _target = target;
            if (target != null)
            {
                _focus = ClampFocus(target.position);
                Snap();
            }
        }

        public void SetTheme(TianyongTheme theme)
            => SetTheme(theme, TianyongPaintedCity.IsEnabledFor(theme, TianyongMapConfig.LoadDefault()));

        public void SetTheme(TianyongTheme theme, bool paintedCity)
        {
            if (_camera == null) return;
            _camera.backgroundColor = theme == TianyongTheme.Lantern
                ? new Color(0.025f, 0.055f, 0.11f)
                : theme == TianyongTheme.Snow
                    ? new Color(0.60f, 0.76f, 0.88f)
                    : new Color(0.35f, 0.72f, 0.92f);

            _topDown = paintedCity;
            _bounds = paintedCity
                ? TianyongPaintedCity.PaintingWorldRect
                : new Rect(0f, 0f, TianyongMapDefinition.Width, TianyongMapDefinition.Depth);

            // Flat sprites all sit at the same height under the top-down
            // camera, so sort them by Z instead of view depth: larger Z
            // (north) draws first, the actor further south draws in front.
            _camera.transparencySortMode = paintedCity
                ? TransparencySortMode.CustomAxis
                : TransparencySortMode.Default;
            _camera.transparencySortAxis = Vector3.forward;

            _focus = ClampFocus(_focus);
            Snap();
        }

        public void Tick(float deltaTime, bool allowScrollZoom = true)
        {
            if (_camera == null) return;
            if (_target != null)
                _focus = Vector3.SmoothDamp(
                    _focus,
                    ClampFocus(_target.position),
                    ref _velocity,
                    0.18f,
                    Mathf.Infinity,
                    Mathf.Max(0.0001f, deltaTime));

            if (allowScrollZoom)
            {
                var wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f)
                    _orthographicSize = Mathf.Clamp(_orthographicSize - wheel * 4f, _zoomMin, _zoomMax);
            }

            _camera.orthographicSize = Mathf.Lerp(_camera.orthographicSize, _orthographicSize,
                1f - Mathf.Exp(-10f * Mathf.Max(0.0001f, deltaTime)));
            PositionCamera();
        }

        public void Snap()
        {
            if (_camera == null) return;
            _camera.orthographicSize = _orthographicSize;
            PositionCamera();
        }

        /// <summary>
        /// Keeps the view inside the map. Top-down, the visible half-extents
        /// are subtracted so the painting's edge never enters the frame; the
        /// isometric view only keeps a small margin as before.
        /// </summary>
        private Vector3 ClampFocus(Vector3 world)
        {
            float marginX = 8f, marginZ = 8f;
            if (_topDown && _camera != null)
            {
                marginZ = _orthographicSize;
                marginX = _orthographicSize * Mathf.Max(0.1f, _camera.aspect);
                // A view wider than the painting just centres on it.
                marginX = Mathf.Min(marginX, _bounds.width * 0.5f);
                marginZ = Mathf.Min(marginZ, _bounds.height * 0.5f);
            }

            world.x = Mathf.Clamp(world.x, _bounds.xMin + marginX, _bounds.xMax - marginX);
            world.z = Mathf.Clamp(world.z, _bounds.yMin + marginZ, _bounds.yMax - marginZ);
            world.y = 0f; // always frame the ground plane
            return world;
        }

        private void ConfigureCamera()
        {
            if (_camera == null) return;
            _camera.orthographic = true;
            _camera.orthographicSize = _orthographicSize;
            _camera.nearClipPlane = 0.3f;
            _camera.farClipPlane = 500f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.allowHDR = true;
            _camera.transform.rotation = IsometricRotation;
            SetTheme(TianyongTheme.City);
            Snap();
        }

        private void PositionCamera()
        {
            var rotation = _topDown ? TopDownRotation : IsometricRotation;
            _camera.transform.rotation = rotation;
            _camera.transform.position = _focus + rotation * (Vector3.back * CameraDistance);
        }
    }
}
