using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// Edit-mode checks for the click-to-move ground ring. Update does not
    /// run here, so every assertion is about the state Spawn leaves behind.
    /// </summary>
    public sealed class TianyongClickMarkerTests
    {
        private static readonly Vector3 GroundPoint = new(150f, 0f, 180f);

        private GameObject _cameraObject;
        private Camera _camera;

        [SetUp]
        public void SetUp()
        {
            TianyongClickMarker.ResetStaticCache();
            // The painted city's straight-down camera: screen up = world +Z.
            _cameraObject = new GameObject("ClickMarkerTestCamera", typeof(Camera));
            _cameraObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _camera = _cameraObject.GetComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var marker in FindAllMarkers())
            {
                if (marker != null) Object.DestroyImmediate(marker.gameObject);
            }
            TianyongClickMarker.ResetStaticCache();

            if (_cameraObject != null)
            {
                Object.DestroyImmediate(_cameraObject);
                _cameraObject = null;
                _camera = null;
            }
        }

        [Test]
        public void Spawn_FiveTimes_KeepsAtMostTwoAlive()
        {
            for (var i = 0; i < 5; i++)
                TianyongClickMarker.Spawn(GroundPoint + Vector3.right * i, _camera);

            Assert.That(TianyongClickMarker.LiveCount, Is.LessThanOrEqualTo(TianyongClickMarker.MaxLive));
            Assert.That(TianyongClickMarker.LiveCount, Is.LessThanOrEqualTo(2));
            var inScene = FindAllMarkers();
            Assert.That(inScene.Length, Is.LessThanOrEqualTo(2),
                "retired markers must be destroyed, not merely forgotten");
        }

        [Test]
        public void Spawn_RetiresTheOldestMarkerFirst()
        {
            var first = TianyongClickMarker.Spawn(GroundPoint, _camera);
            var second = TianyongClickMarker.Spawn(GroundPoint + Vector3.right, _camera);
            var third = TianyongClickMarker.Spawn(GroundPoint + Vector3.right * 2f, _camera);

            Assert.That(first == null, Is.True, "the oldest marker is retired when a third arrives");
            Assert.That(second != null, Is.True);
            Assert.That(third != null, Is.True);
            Assert.That(TianyongClickMarker.LiveCount, Is.EqualTo(2));
        }

        [Test]
        public void Spawn_SquashesTheDiscByTheGroundAspect()
        {
            var aspect = TianyongMapConfig.ResolveGroundDiscAspect();
            var marker = TianyongClickMarker.Spawn(GroundPoint, _camera);

            var scale = marker.transform.localScale;
            Assert.That(scale.x, Is.GreaterThan(0f));
            Assert.That(scale.y / scale.x, Is.EqualTo(aspect).Within(0.001f),
                "local Y (world +Z after the 90 degree tilt) carries the ground-disc aspect");
            Assert.That(scale.z, Is.EqualTo(1f).Within(0.001f));
            // Pops in from 0.7x of the base diameter; must stay below the ~3.2 u contact shadow.
            Assert.That(scale.x, Is.LessThan(TianyongClickMarker.BaseDiameter + 0.001f));
        }

        [Test]
        public void Spawn_AnchorsOnTheGroundPointWithLift()
        {
            var marker = TianyongClickMarker.Spawn(GroundPoint, _camera);

            var expected = GroundPoint + Vector3.up * TianyongClickMarker.GroundLift;
            var position = marker.transform.position;
            Assert.That(position.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(position.y, Is.EqualTo(expected.y).Within(0.0001f));
            Assert.That(position.y, Is.EqualTo(GroundPoint.y + 0.06f).Within(0.0001f));
            Assert.That(position.z, Is.EqualTo(expected.z).Within(0.0001f));
            Assert.That(marker.transform.parent, Is.Null, "a scene-root object cannot be dragged by camera or actor");
        }

        [Test]
        public void Spawn_LiesFlatOnTheGround()
        {
            var marker = TianyongClickMarker.Spawn(GroundPoint, _camera);

            var angle = Quaternion.Angle(marker.transform.rotation, Quaternion.Euler(90f, 0f, 0f));
            Assert.That(angle, Is.LessThan(0.01f));
        }

        [Test]
        public void Spawn_SortsBelowTheActorStandingOnThatPoint()
        {
            var marker = TianyongClickMarker.Spawn(GroundPoint, _camera);

            var renderer = marker.GetComponent<SpriteRenderer>();
            Assert.That(renderer, Is.Not.Null);
            var actorOrder = QdaoBoySpriteAnimator.WorldSortingOrder(GroundPoint, _camera);
            Assert.That(renderer.sortingOrder, Is.EqualTo(actorOrder - 5));
        }

        [Test]
        public void Spawn_UsesTheProceduralRingWithTintOnlyForFade()
        {
            var marker = TianyongClickMarker.Spawn(GroundPoint, _camera);

            var renderer = marker.GetComponent<SpriteRenderer>();
            Assert.That(renderer.sprite, Is.Not.Null);
            Assert.That(renderer.sprite.texture.width, Is.EqualTo(160));
            Assert.That(renderer.sprite.texture.height, Is.EqualTo(160));
            Assert.That(renderer.sprite.pixelsPerUnit, Is.EqualTo(160f).Within(0.001f),
                "one world unit across at scale 1 so localScale is the diameter");
            Assert.That(renderer.color.r, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(renderer.color.g, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(renderer.color.b, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(renderer.color.a, Is.EqualTo(1f).Within(0.0001f), "fully visible at spawn");
        }

        [Test]
        public void DestroyImmediate_DropsTheMarkerFromLiveCount()
        {
            var first = TianyongClickMarker.Spawn(GroundPoint, _camera);
            TianyongClickMarker.Spawn(GroundPoint + Vector3.right, _camera);
            Assert.That(TianyongClickMarker.LiveCount, Is.EqualTo(2));

            Object.DestroyImmediate(first.gameObject);

            Assert.That(TianyongClickMarker.LiveCount, Is.EqualTo(1),
                "a marker destroyed from outside must not linger in the live list");
            // A fresh spawn after an external destroy must not trip over the dead entry.
            TianyongClickMarker.Spawn(GroundPoint + Vector3.right * 2f, _camera);
            Assert.That(TianyongClickMarker.LiveCount, Is.EqualTo(2));
        }

        [Test]
        public void ResetStaticCache_ForgetsLiveMarkers()
        {
            TianyongClickMarker.Spawn(GroundPoint, _camera);
            Assert.That(TianyongClickMarker.LiveCount, Is.EqualTo(1));

            foreach (var marker in FindAllMarkers())
                Object.DestroyImmediate(marker.gameObject);
            TianyongClickMarker.ResetStaticCache();

            Assert.That(TianyongClickMarker.LiveCount, Is.Zero);
        }

        private static TianyongClickMarker[] FindAllMarkers()
        {
#if UNITY_6000_6_OR_NEWER
            return Object.FindObjectsByType<TianyongClickMarker>(FindObjectsInactive.Include);
#else
            return Object.FindObjectsByType<TianyongClickMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#endif
        }
    }
}
