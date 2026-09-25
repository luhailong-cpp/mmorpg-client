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
        public Camera RenderCamera => _camera;
        public Rect WorldBounds => _bounds;
        public Vector3 Focus => _focus;
        public float RequestedZoom => _orthographicSize;

        public void SetTarget(Transform target)
        {
            _target = target;
            if (target != null)
            {
                _focus = target.position;
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

            Snap();
        }

        public void Tick(float deltaTime, bool allowScrollZoom = true)
        {
            if (_camera == null) return;
            if (allowScrollZoom)
            {
                var wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f)
                    SetZoom(_orthographicSize - wheel * 4f);
            }

            _camera.orthographicSize = FitZoomToPainting(Mathf.Lerp(_camera.orthographicSize,
                FitZoomToPainting(_orthographicSize),
                1f - Mathf.Exp(-10f * Mathf.Max(0.0001f, deltaTime))));
            if (_target != null)
                _focus = Vector3.SmoothDamp(
                    _focus,
                    ClampFocus(TargetFocus(_camera.orthographicSize), _camera.orthographicSize),
                    ref _velocity,
                    0.18f,
                    Mathf.Infinity,
                    Mathf.Max(0.0001f, deltaTime));

            // A widening view can outpace smooth follow. Correct the final
            // position using this frame's rendered size, so it needs no extra
            // inward jump based on the requested zoom's future viewport.
            _focus = ClampFocus(_focus, _camera.orthographicSize);
            _focus = KeepCloseUpFrameVisible(_focus, _camera.orthographicSize);
            PositionCamera();
        }

        public void Snap()
        {
            if (_camera == null) return;
            _camera.orthographicSize = FitZoomToPainting(_orthographicSize);
            if (_target != null) _focus = TargetFocus(_camera.orthographicSize);
            _focus = ClampFocus(_focus, _camera.orthographicSize);
            _focus = KeepCloseUpFrameVisible(_focus, _camera.orthographicSize);
            _velocity = Vector3.zero;
            PositionCamera();
        }

        /// <summary>
        /// Zoom the camera eases towards (the mouse wheel goes through here),
        /// clamped to the configured window.
        /// </summary>
        public void SetZoom(float orthographicSize)
            => _orthographicSize = Mathf.Clamp(orthographicSize, _zoomMin, _zoomMax);

        /// <summary>Only close views lift from the feet towards the authored frame centre.</summary>
        public static float CloseUpFramingWeight(float orthographicSize, float frameHeight)
            => 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(frameHeight * .51f, frameHeight * 1.2f, orthographicSize));

        private bool TryTargetFrame(out Bounds frame)
        {
            frame = default;
            if (_target == null) return false;
            var renderer = _target.Find("sprite")?.GetComponent<SpriteRenderer>();
            if (renderer?.sprite == null) return false;
            frame = renderer.sprite.bounds;
            var scale = renderer.transform.lossyScale;
            frame.center = Vector3.Scale(frame.center, scale);
            frame.size = Vector3.Scale(frame.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            // Billboard sprites sit slightly above the feet. In the isometric view that
            // lift also has a screen-up component; horizontal follow lag is not an offset.
            var rotation = _topDown ? TopDownRotation : IsometricRotation;
            frame.center += Vector3.up * Vector3.Dot(
                Vector3.up * (renderer.transform.position.y - _target.position.y), rotation * Vector3.up);
            return true;
        }

        private Vector3 TargetFocus(float zoom)
        {
            var focus = _target.position;
            if (TryTargetFrame(out var frame))
            {
                var up = (_topDown ? TopDownRotation : IsometricRotation) * Vector3.up;
                focus += GroundShift(up, frame.center.y * CloseUpFramingWeight(zoom, frame.size.y));
            }
            return focus;
        }

        private static Vector3 GroundShift(Vector3 screenAxis, float projectedDistance)
        {
            var groundAxis = new Vector3(screenAxis.x, 0f, screenAxis.z);
            return groundAxis * (projectedDistance / Mathf.Max(.001f, groundAxis.sqrMagnitude));
        }

        private Vector3 KeepCloseUpFrameVisible(Vector3 focus, float zoom)
        {
            if (!TryTargetFrame(out var frame) || CloseUpFramingWeight(zoom, frame.size.y) <= 0f) return focus;
            // At zoom 5 the 9.846 u frame almost fills the 10 u viewport. Ordinary follow lag
            // or a fast wheel gesture must not let the head leave the image before easing catches up.
            const float edge = .02f;
            var feet = _target.position;
            var rotation = _topDown ? TopDownRotation : IsometricRotation;
            var up = rotation * Vector3.up;
            var right = rotation * Vector3.right;
            float feetUp = Vector3.Dot(feet - focus, up);
            float feetRight = Vector3.Dot(feet - focus, right);
            float minUp = feetUp + frame.max.y - zoom + edge;
            float maxUp = feetUp + frame.min.y + zoom - edge;
            float halfWidth = zoom * _camera.aspect;
            float minRight = feetRight + frame.max.x - halfWidth + edge;
            float maxRight = feetRight + frame.min.x + halfWidth - edge;
            var constrained = focus;
            if (minUp <= maxUp) constrained += GroundShift(up, Mathf.Clamp(0f, minUp, maxUp));
            if (minRight <= maxRight) constrained += GroundShift(right, Mathf.Clamp(0f, minRight, maxRight));
            constrained = ClampFocus(constrained, zoom); // Painting containment remains authoritative at its boundary.
            if (constrained.x != focus.x) _velocity.x = 0f;
            if (constrained.z != focus.z) _velocity.z = 0f;
            return constrained;
        }

        /// <summary>
        /// Keeps the view inside the map. Top-down, the visible half-extents
        /// are subtracted so the painting's edge never enters the frame; the
        /// isometric view only keeps a small margin as before.
        /// </summary>
        private float FitZoomToPainting(float size)
        {
            if (!_topDown || _camera == null) return size;
            var largestSize = Mathf.Min(_bounds.height * 0.5f,
                _bounds.width * 0.5f / Mathf.Max(0.1f, _camera.aspect));
            return Mathf.Min(size, largestSize);
        }

        private Vector3 ClampFocus(Vector3 world, float orthographicSize)
        {
            float marginX = 8f, marginZ = 8f;
            if (_topDown && _camera != null)
            {
                marginZ = orthographicSize;
                marginX = orthographicSize * Mathf.Max(0.1f, _camera.aspect);
                // FitZoomToPainting limits the actual view to these extents.
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
            // Snap and normal gameplay Tick share exactly the same label layout. Captures do not
            // move or resize the character and need no screenshot-specific nameplate adjustment.
            if (_target != null)
                foreach (var label in _target.GetComponentsInChildren<WorldLabelBillboard>())
                    label.RefreshForCamera(_camera);
        }
    }
}
