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
            Object.DestroyImmediate(_cameraObject);
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
    }
}
