using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// Top-down follow camera for the painted city: the painting's edge must
    /// never scroll into view (also while a wheel zoom is still easing), and
    /// the follow lags the runner by about speed x smooth time without
    /// overshooting when it stops.
    /// </summary>
    public sealed class TianyongCameraControllerTests
    {
        private const float Aspect = 16f / 9f;
        private const float FrameTime = 1f / 60f;
        private const float RunSpeed = 9f;

        private GameObject _cameraObject;
        private GameObject _target;
        private Camera _camera;
        private Texture2D _geometryTexture;
        private Sprite _bodySprite, _labelSprite;
        private RenderTexture _viewport;

        [SetUp]
        public void SetUp()
        {
            _cameraObject = new GameObject("TianyongCameraControllerTests_Camera", typeof(Camera));
            _camera = _cameraObject.GetComponent<Camera>();
            _camera.aspect = Aspect;
            _target = new GameObject("TianyongCameraControllerTests_Target");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_target);
            _camera.targetTexture = null;
            Object.DestroyImmediate(_cameraObject);
            if (_bodySprite != null) Object.DestroyImmediate(_bodySprite);
            if (_labelSprite != null) Object.DestroyImmediate(_labelSprite);
            if (_geometryTexture != null) Object.DestroyImmediate(_geometryTexture);
            if (_viewport != null) Object.DestroyImmediate(_viewport);
        }

        private TianyongCameraController CreateTopDown(float zoomDefault, Vector3 feet)
        {
            var controller = new TianyongCameraController(_camera, 5f, 30f, zoomDefault);
            controller.SetTheme(TianyongTheme.City, paintedCity: true);
            _target.transform.position = feet;
            controller.SetTarget(_target.transform);
            return controller;
        }

        private void AssertPaintingEdgeOutOfView(string when)
        {
            var bounds = TianyongPaintedCity.PaintingWorldRect;
            var halfWidth = _camera.orthographicSize * _camera.aspect;
            var halfHeight = _camera.orthographicSize;
            var position = _camera.transform.position; // straight down: view centre = (x, z)
            Assert.That(position.x - halfWidth, Is.GreaterThanOrEqualTo(bounds.xMin - 0.001f), when + ": west edge in view");
            Assert.That(position.x + halfWidth, Is.LessThanOrEqualTo(bounds.xMax + 0.001f), when + ": east edge in view");
            Assert.That(position.z - halfHeight, Is.GreaterThanOrEqualTo(bounds.yMin - 0.001f), when + ": south edge in view");
            Assert.That(position.z + halfHeight, Is.LessThanOrEqualTo(bounds.yMax + 0.001f), when + ": north edge in view");
        }

        // Feet pinned against each painting edge (the walk mask reaches
        // x 60..340 / z 16..280, well inside the 27-zoom clamp window of
        // x 98..302 / z 27..273, so these spots are reachable in play).
        [TestCase(23f, 30f, 62f, 150f, TestName = "ZoomOut_AtWestEdge_KeepsPaintingEdgeOutOfView")]
        [TestCase(23f, 30f, 338f, 150f, TestName = "ZoomOut_AtEastEdge_KeepsPaintingEdgeOutOfView")]
        [TestCase(23f, 30f, 200f, 16f, TestName = "ZoomOut_AtSouthEdge_KeepsPaintingEdgeOutOfView")]
        [TestCase(23f, 30f, 200f, 280f, TestName = "ZoomOut_AtNorthEdge_KeepsPaintingEdgeOutOfView")]
        [TestCase(30f, 23f, 62f, 150f, TestName = "ZoomIn_AtWestEdge_KeepsPaintingEdgeOutOfView")]
        public void Zoom_AtPaintingEdge_KeepsPaintingEdgeOutOfView(float fromZoom, float toZoom, float x, float z)
        {
            var controller = CreateTopDown(fromZoom, new Vector3(x, 0f, z));
            AssertPaintingEdgeOutOfView("after snap");

            controller.SetZoom(toZoom);
            for (var frame = 0; frame < 90; frame++)
            {
                controller.Tick(FrameTime, allowScrollZoom: false);
                AssertPaintingEdgeOutOfView($"frame {frame}");
            }

            Assert.That(_camera.orthographicSize, Is.EqualTo(toZoom).Within(0.05f));
        }

        [Test]
        public void FastWheelFlickOut_AtWestEdge_KeepsPaintingEdgeOutOfView()
        {
            var controller = CreateTopDown(5f, new Vector3(62f, 0f, 150f));
            for (var frame = 0; frame < 90; frame++)
            {
                // One wheel notch (4 units) per frame from the closest zoom to the widest.
                if (frame < 7) controller.SetZoom(5f + 4f * (frame + 1));
                controller.Tick(FrameTime, allowScrollZoom: false);
                AssertPaintingEdgeOutOfView($"frame {frame}");
            }

            Assert.That(_camera.orthographicSize, Is.EqualTo(30f).Within(0.05f));
        }


        [Test]
        public void Snap_AfterZoomOutAtEdge_ReclampsImmediately()
        {
            var controller = CreateTopDown(12f, new Vector3(50f, 0f, 0f));
            controller.SetZoom(30f);
            controller.Snap();
            AssertPaintingEdgeOutOfView("snap after zoom out, before any Tick");
            Assert.That(_camera.orthographicSize, Is.EqualTo(30f).Within(0.001f));
        }

        [Test]
        public void AspectChange_ToViewportWiderThanPainting_RefitsZoomBeforeRendering()
        {
            var controller = CreateTopDown(30f, new Vector3(50f, 0f, 0f));
            _camera.aspect = 10f;
            controller.Tick(FrameTime, allowScrollZoom: false);
            AssertPaintingEdgeOutOfView("first frame after aspect change");
            Assert.That(_camera.orthographicSize, Is.EqualTo(15f).Within(0.001f));
            controller.Snap();
            AssertPaintingEdgeOutOfView("snap with oversized requested zoom");
        }

        [Test]
        public void Zoom_AllEdgesAndCorners_StaysInsidePaintingEveryFrame(
            [Values(9f / 16f, 16f / 9f, 21f / 9f, 6f)] float aspect,
            [Values(1f / 30f, 1f / 60f, 1f / 144f)] float deltaTime)
        {
            _camera.aspect = aspect;
            var bounds = TianyongPaintedCity.PaintingWorldRect;
            var feet = new[]
            {
                new Vector3(bounds.xMin, 0f, bounds.center.y),
                new Vector3(bounds.xMax, 0f, bounds.center.y),
                new Vector3(bounds.center.x, 0f, bounds.yMin),
                new Vector3(bounds.center.x, 0f, bounds.yMax),
                new Vector3(bounds.xMin, 0f, bounds.yMin),
                new Vector3(bounds.xMax, 0f, bounds.yMin),
                new Vector3(bounds.xMin, 0f, bounds.yMax),
                new Vector3(bounds.xMax, 0f, bounds.yMax),
            };
            foreach (var point in feet)
            {
                var controller = CreateTopDown(5f, point);
                AssertPaintingEdgeOutOfView($"initial aspect={aspect} point={point}");
                foreach (var zoom in new[] { 30f, 12f, 30f })
                {
                    controller.SetZoom(zoom);
                    for (var frame = 0; frame < 180; frame++)
                    {
                        controller.Tick(deltaTime, allowScrollZoom: false);
                        AssertPaintingEdgeOutOfView($"aspect={aspect} point={point} zoom={zoom} frame={frame}");
                    }
                }
            }
        }

        [Test]
        public void SetZoom_IsClampedToTheConfiguredWindow()
        {
            var controller = CreateTopDown(27f, new Vector3(200f, 0f, 150f));

            controller.SetZoom(100f);
            for (var frame = 0; frame < 120; frame++) controller.Tick(FrameTime, allowScrollZoom: false);
            Assert.That(_camera.orthographicSize, Is.EqualTo(30f).Within(0.01f));

            controller.SetZoom(1f);
            for (var frame = 0; frame < 120; frame++) controller.Tick(FrameTime, allowScrollZoom: false);
            Assert.That(_camera.orthographicSize, Is.EqualTo(5f).Within(0.01f));
        }

        [Test]
        public void Follow_AtRunSpeed_LagsBySmoothTimeAndSettlesWithoutOvershoot()
        {
            var controller = CreateTopDown(27f, new Vector3(150f, 0f, 150f));

            // Two seconds east at run speed: the critically damped follow
            // settles about speed x 0.18 s (~1.55 u at 60 fps) behind the feet.
            for (var frame = 0; frame < 120; frame++)
            {
                _target.transform.position += new Vector3(RunSpeed * FrameTime, 0f, 0f);
                controller.Tick(FrameTime, allowScrollZoom: false);
            }
            var lag = _target.transform.position.x - _camera.transform.position.x;
            Assert.That(lag, Is.InRange(1.3f, 1.7f), "steady-state follow lag at 9 u/s");

            // Stop: within one 1080p screen pixel (0.05 u at zoom 27) in half
            // a second and never past the feet.
            for (var frame = 0; frame < 30; frame++)
            {
                controller.Tick(FrameTime, allowScrollZoom: false);
                Assert.That(_camera.transform.position.x,
                    Is.LessThanOrEqualTo(_target.transform.position.x + 0.001f), $"overshoot at frame {frame}");
            }
            Assert.That(Mathf.Abs(_target.transform.position.x - _camera.transform.position.x), Is.LessThan(0.05f));
        }

        // Geometry fixtures exercise gameplay framing only. Actual authored art and TMP nameplates
        // are checked separately by the graphics-enabled RosterSandbox capture assertions.
        private TianyongCameraController CreateCharacter(bool topDown, float zoom,
            out SpriteRenderer body, out SpriteRenderer nameplate)
        {
            _viewport = new RenderTexture(1920, 1080, 0);
            _camera.targetTexture = _viewport;
            var controller = new TianyongCameraController(_camera, 5f, 30f, zoom);
            controller.SetTheme(TianyongTheme.City, topDown);
            _target.transform.position = new Vector3(180f, 0f, 140f);
            _geometryTexture = new Texture2D(512, 512);
            _bodySprite = Sprite.Create(_geometryTexture, new Rect(0f, 0f, 512f, 512f),
                new Vector2(.5f, .08f), 52f, 0, SpriteMeshType.FullRect);
            body = new GameObject("sprite", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
            body.transform.SetParent(_target.transform, false);
            body.sprite = _bodySprite;
            body.transform.position = _target.transform.position + Vector3.up * .1f;
            body.transform.rotation = _camera.transform.rotation;

            var label = new GameObject(WorldNameplate.ObjectName);
            label.transform.SetParent(_target.transform, false);
            nameplate = new GameObject("NameplateBackdrop", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
            nameplate.transform.SetParent(label.transform, false);
            _labelSprite = Sprite.Create(_geometryTexture, new Rect(0f, 0f, 64f, 64f),
                new Vector2(.5f, .5f), 64f, 0, SpriteMeshType.FullRect);
            nameplate.sprite = _labelSprite;
            nameplate.transform.localScale = new Vector3(6f,
                WorldNameplate.WorldEmHeight + 2f * WorldNameplate.BackdropPaddingY, 1f);
            WorldLabelBillboard.Attach(label);
            controller.SetTarget(_target.transform);
            return controller;
        }

        private Vector2 ProjectedLimits(SpriteRenderer renderer, bool vertical)
        {
            var bounds = renderer.sprite.bounds;
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (var x in new[] { bounds.min.x, bounds.max.x })
            foreach (var y in new[] { bounds.min.y, bounds.max.y })
            {
                var point = _camera.WorldToViewportPoint(renderer.transform.TransformPoint(new Vector3(x, y, 0f)));
                float value = vertical ? point.y : point.x;
                min = Mathf.Min(min, value);
                max = Mathf.Max(max, value);
                Assert.That(point.z, Is.GreaterThan(0f));
            }
            return new Vector2(min, max);
        }

        private void AssertVisible(SpriteRenderer renderer, string when)
        {
            foreach (bool vertical in new[] { false, true })
            {
                var limits = ProjectedLimits(renderer, vertical);
                Assert.That(limits.x, Is.GreaterThanOrEqualTo(0f), when + " lower edge");
                Assert.That(limits.y, Is.LessThanOrEqualTo(1f), when + " upper edge");
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ClosestZoom_FramesWholeCharacterAndReadableNameplate_WithoutChangingGeometry(bool topDown)
        {
            var controller = CreateCharacter(topDown, 5f, out var body, out var nameplate);
            AssertVisible(body, "full authored frame at zoom 5");
            AssertVisible(nameplate, "nameplate at zoom 5");
            var feet = _camera.WorldToViewportPoint(_target.transform.position);
            Assert.That(feet.y, Is.InRange(0f, 1f));
            var nameHeight = ProjectedLimits(nameplate, true);
            Assert.That((nameHeight.y - nameHeight.x) * 1080f, Is.InRange(40f, 52f), "readable label and backdrop");
            Assert.That(_camera.orthographicSize, Is.EqualTo(5f));
            Assert.That(controller.RequestedZoom, Is.EqualTo(5f));
            Assert.That(body.sprite.pixelsPerUnit, Is.EqualTo(52f));
            Assert.That(body.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(body.sprite.pivot, Is.EqualTo(new Vector2(256f, 40.96f)));
            Assert.That(_target.transform.position, Is.EqualTo(new Vector3(180f, 0f, 140f)));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void NormalZoom_PreservesFootCenteredCameraAndOriginalNameplateLayout(bool topDown)
        {
            CreateCharacter(topDown, 27f, out var body, out var nameplate);
            var feet = _camera.WorldToViewportPoint(_target.transform.position);
            Assert.That(feet.x, Is.EqualTo(.5f).Within(.0001f));
            Assert.That(feet.y, Is.EqualTo(.5f).Within(.0001f));
            var label = nameplate.transform.parent;
            Assert.That(label.localScale, Is.EqualTo(Vector3.one));
            Assert.That(Vector3.Dot(_target.transform.position - label.position, _camera.transform.up),
                Is.EqualTo(WorldLabelBillboard.DefaultOffsetBelowFeet).Within(.0001f));
            AssertVisible(body, "normal body");
            AssertVisible(nameplate, "normal nameplate");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ZoomTransitionAndRunSpeed_KeepFullCharacterAndNameplateVisible(bool topDown)
        {
            var controller = CreateCharacter(topDown, 27f, out var body, out var nameplate);
            var up = _camera.transform.up;
            var heading = new Vector3(up.x, 0f, up.z).normalized;
            controller.SetZoom(5f);
            bool sawIntermediate = false;
            for (var frame = 0; frame < 240; frame++)
            {
                _target.transform.position += heading * (RunSpeed * FrameTime * (frame < 120 ? 1f : -1f));
                body.transform.position = _target.transform.position + Vector3.up * .1f;
                controller.Tick(FrameTime, allowScrollZoom: false);
                AssertVisible(body, "running body at frame " + frame);
                AssertVisible(nameplate, "running nameplate at frame " + frame);
                float scale = nameplate.transform.parent.localScale.x;
                if (scale > .31f && scale < .99f) sawIntermediate = true;
                Assert.That(body.transform.localScale, Is.EqualTo(Vector3.one));
            }
            Assert.That(sawIntermediate, Is.True, "near label layout must transition during actual zoom easing");
            Assert.That(_camera.orthographicSize, Is.EqualTo(5f).Within(.001f));
            controller.SetZoom(27f);
            for (var frame = 0; frame < 120; frame++)
            {
                controller.Tick(FrameTime, allowScrollZoom: false);
                AssertVisible(body, "zoom-out body at frame " + frame);
                AssertVisible(nameplate, "zoom-out nameplate at frame " + frame);
            }
            Assert.That(nameplate.transform.parent.localScale, Is.EqualTo(Vector3.one));
        }
    }
}
