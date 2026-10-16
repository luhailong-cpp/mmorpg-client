using System.Collections;
using System.Collections.Generic;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;

namespace MmorpgClient.Tests.PlayMode
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class QdaoBoySpriteAnimatorPlayModeTests
    {
        // One 60 Hz frame of travel at the 9 u/s run speed.
        private const float StepPerFrame = 9f / 60f;
        private const string CurrentDefault = "00_reference_topright_boy";

        [UnityTest]
        public IEnumerator Running_KeepsFeetAndWorldRigidWhileAdvancingAtRunCadence()
        {
            var previousCaptureFramerate = Time.captureFramerate;
            Time.captureFramerate = 60;

            var cameraObject = CreateTopDownCamera();
            var worldCamera = cameraObject.GetComponent<Camera>();

            var worldDecoration = new GameObject("StaticWorldOccluder");
            worldDecoration.transform.SetPositionAndRotation(
                new Vector3(12f, 0f, 18f),
                Quaternion.Euler(0f, 37f, 0f));
            worldDecoration.transform.localScale = new Vector3(2f, 3f, 4f);
            var decorationPosition = worldDecoration.transform.position;
            var decorationRotation = worldDecoration.transform.rotation;
            var decorationScale = worldDecoration.transform.localScale;

            var actor = new GameObject("RunTestActor");
            try
            {
                Assert.That(QdaoBoySpriteAnimator.TryAttach(actor), Is.True);
                yield return null;
                Assert.That(actor.GetComponent<QdaoBoySpriteAnimator>().CharacterId, Is.EqualTo(CurrentDefault));
                using var run = QdaoActionResources.Acquire(CurrentDefault, "run", "N");
                Assert.That(run, Is.Not.Null);
                Assert.That(run.Frames.Length, Is.EqualTo(16));

                var billboard = actor.transform.Find("sprite");
                Assert.That(billboard, Is.Not.Null);
                var renderer = billboard.GetComponent<SpriteRenderer>();
                Assert.That(renderer, Is.Not.Null);

                var shadow = actor.transform.Find(QdaoBoySpriteAnimator.ShadowObjectName);
                Assert.That(shadow, Is.Not.Null, "the actor must carry a contact shadow child");
                var shadowRenderer = shadow.GetComponent<SpriteRenderer>();
                Assert.That(shadowRenderer, Is.Not.Null);
                Assert.That(shadowRenderer.sprite, Is.Not.Null);
                var expectedShadowScale = new Vector3(
                    QdaoBoySpriteAnimator.ShadowWidth,
                    QdaoBoySpriteAnimator.ShadowWidth * TianyongMapConfig.ResolveGroundDiscAspect(),
                    1f);
                // Straight-down camera, yaw 0: screen-down is world -Z.
                var expectedShadowOffset = new Vector3(
                    0f,
                    QdaoBoySpriteAnimator.ShadowLift,
                    -QdaoBoySpriteAnimator.ShadowScreenDownOffset);
                var groundRotation = Quaternion.Euler(90f, 0f, 0f);

                var observedSprites = new List<string>();
                var sampleFrames = Mathf.CeilToInt(run.DurationSeconds * 60f) + 2;
                for (var frame = 0; frame < sampleFrames; frame++)
                {
                    actor.transform.position += Vector3.forward * StepPerFrame;
                    yield return null;

                    observedSprites.Add(renderer.sprite.name);
                    Assert.That(renderer.sprite.name, Does.Contain("_N_"));
                    Assert.That(billboard.localScale, Is.EqualTo(Vector3.one));
                    Assert.That(Quaternion.Angle(billboard.rotation, worldCamera.transform.rotation),
                        Is.LessThan(0.01f));
                    Assert.That(billboard.position - actor.transform.position,
                        Is.EqualTo(Vector3.up * 0.1f).Using(Vector3ComparerWithEqualsOperator.Instance));

                    // The shadow rides the feet point only: constant offset,
                    // flat on the ground, fixed size, drawn under the sprite.
                    Assert.That(shadow.position - actor.transform.position,
                        Is.EqualTo(expectedShadowOffset).Using(Vector3ComparerWithEqualsOperator.Instance));
                    Assert.That(Quaternion.Angle(shadow.rotation, groundRotation), Is.LessThan(0.01f));
                    Assert.That(shadow.localScale,
                        Is.EqualTo(expectedShadowScale).Using(Vector3ComparerWithEqualsOperator.Instance));
                    Assert.That(shadowRenderer.sortingOrder,
                        Is.EqualTo(renderer.sortingOrder + QdaoBoySpriteAnimator.ShadowSortingOffset));

                    Assert.That(worldDecoration.transform.position, Is.EqualTo(decorationPosition));
                    Assert.That(worldDecoration.transform.rotation, Is.EqualTo(decorationRotation));
                    Assert.That(worldDecoration.transform.localScale, Is.EqualTo(decorationScale));
                }

                Assert.That(new HashSet<string>(observedSprites).Count, Is.EqualTo(run.Frames.Length));
                var longestAuthoredFrame = 0f;
                foreach (var duration in run.FrameDurationsSeconds) longestAuthoredFrame = Mathf.Max(longestAuthoredFrame, duration);
                Assert.That(LongestIdenticalRun(observedSprites), Is.LessThanOrEqualTo(Mathf.CeilToInt(longestAuthoredFrame * 60f)),
                    "At the reference speed, pose holds must follow the archived frame durations.");
            }
            finally
            {
                Time.captureFramerate = previousCaptureFramerate;
                Object.Destroy(actor);
                Object.Destroy(worldDecoration);
                Object.Destroy(cameraObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Stopping_UsesCurrentDedicatedIdleImmediately_AndStartingLeavesIt()
        {
            var previousCaptureFramerate = Time.captureFramerate;
            Time.captureFramerate = 60;

            var cameraObject = CreateTopDownCamera();
            var actor = new GameObject("SettleTestActor");
            try
            {
                Assert.That(QdaoBoySpriteAnimator.TryAttach(actor), Is.True);
                yield return null;

                var animator = actor.GetComponent<QdaoBoySpriteAnimator>();
                Assert.That(animator, Is.Not.Null);
                Assert.That(animator.CharacterId, Is.EqualTo(CurrentDefault));
                var appearance = QdaoCharacterCatalog.Find(CurrentDefault).ResolveAppearance();
                Assert.That(appearance, Is.Not.Null);
                using var northRun = QdaoActionResources.Acquire(CurrentDefault, "run", "N");
                using var southRun = QdaoActionResources.Acquire(CurrentDefault, "run", "S");
                Assert.That(northRun, Is.Not.Null);
                Assert.That(southRun, Is.Not.Null);
                var renderer = actor.transform.Find("sprite").GetComponent<SpriteRenderer>();

                // Fresh actor uses the separately authored S standing texture.
                Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Idle));
                AssertIdle(renderer.sprite, appearance, "S");

                // Start north using elapsed travel within the authored run.
                actor.transform.position += Vector3.forward * StepPerFrame;
                yield return null;
                Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Run));
                Assert.That(renderer.sprite, Is.SameAs(northRun.FrameAt(StepPerFrame / QdaoBoySpriteAnimator.ReferenceRunSpeed, true)));

                // Travel into the middle of the cycle before stopping.
                for (var frame = 0; frame < 29; frame++)
                {
                    actor.transform.position += Vector3.forward * StepPerFrame;
                    yield return null;
                    Assert.That(renderer.sprite.name, Does.Contain("_N_"));
                }
                Assert.That(renderer.sprite, Is.SameAs(northRun.FrameAt(30f * StepPerFrame / QdaoBoySpriteAnimator.ReferenceRunSpeed, true)));

                var stoppedPosition = actor.transform.position;
                yield return null;
                Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Idle),
                    "The delivered run must switch to authored idle on the first stationary frame.");
                Assert.That(animator.Direction, Is.Zero);
                Assert.That(actor.transform.position, Is.EqualTo(stoppedPosition));
                AssertIdle(renderer.sprite, appearance, "N");

                // Standing still holds the pose.
                for (var frame = 0; frame < 10; frame++)
                {
                    yield return null;
                    AssertIdle(renderer.sprite, appearance, "N");
                    Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Idle));
                }

                // Reversing uses the independently authored south run.
                actor.transform.position += Vector3.back * StepPerFrame;
                yield return null;
                Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Run));
                Assert.That(animator.Direction, Is.EqualTo(4));
                Assert.That(renderer.sprite, Is.SameAs(southRun.FrameAt(StepPerFrame / QdaoBoySpriteAnimator.ReferenceRunSpeed, true)));
            }
            finally
            {
                Time.captureFramerate = previousCaptureFramerate;
                Object.Destroy(actor);
                Object.Destroy(cameraObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryDirection_StopsOnItsOwnDedicatedIdleTexture()
        {
            var previousCaptureFramerate = Time.captureFramerate;
            Time.captureFramerate = 60;
            var cameraObject = CreateTopDownCamera();
            var actor = new GameObject("EightDirectionIdleActor");
            var directions = new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            try
            {
                Assert.That(QdaoBoySpriteAnimator.TryAttach(actor), Is.True);
                yield return null;
                var animator = actor.GetComponent<QdaoBoySpriteAnimator>();
                Assert.That(animator.CharacterId, Is.EqualTo(CurrentDefault));
                var appearance = QdaoCharacterCatalog.Find(CurrentDefault).ResolveAppearance();
                Assert.That(appearance, Is.Not.Null);
                var renderer = actor.transform.Find("sprite").GetComponent<SpriteRenderer>();
                for (var direction = 0; direction < directions.Length; direction++)
                {
                    var yaw = direction * 45f * Mathf.Deg2Rad;
                    var step = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * StepPerFrame;
                    for (var frame = 0; frame < 30; frame++)
                    {
                        actor.transform.position += step;
                        yield return null;
                    }
                    Assert.That(animator.Direction, Is.EqualTo(direction));
                    Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Run));
                    for (var frame = 0; frame < 48; frame++)
                    {
                        yield return null;
                        if (animator.State == QdaoBoySpriteAnimator.LocomotionState.Idle) break;
                    }
                    Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Idle));
                    Assert.That(animator.Direction, Is.EqualTo(direction), "stopping preserves facing");
                    AssertIdle(renderer.sprite, appearance, directions[direction]);
                }
            }
            finally
            {
                Time.captureFramerate = previousCaptureFramerate;
                Object.Destroy(actor);
                Object.Destroy(cameraObject);
            }
            yield return null;
        }

        private static void AssertIdle(Sprite sprite, QdaoCharacterCatalog.Appearance appearance, string direction)
        {
            var path = appearance.IdleResourcePath(direction);
            Assert.That(sprite, Is.Not.Null);
            Assert.That(sprite.texture, Is.SameAs(Resources.Load<Texture2D>(path)),
                "Idle must use current dedicated artwork, never a held run frame.");
            Assert.That(appearance.GeometryForResource(path).Matches(sprite), Is.True,
                "Idle must retain the delivered frame size, pixels per unit and root pivot.");
        }

        private static GameObject CreateTopDownCamera()
        {
            var cameraObject = new GameObject("RunTestCamera");
            cameraObject.tag = "MainCamera";
            var worldCamera = cameraObject.AddComponent<Camera>();
            worldCamera.orthographic = true;
            worldCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            return cameraObject;
        }

        private static int LongestIdenticalRun(IReadOnlyList<string> sprites)
        {
            var longest = 0;
            var current = 0;
            string previous = null;
            foreach (var sprite in sprites)
            {
                current = sprite == previous ? current + 1 : 1;
                if (current > longest) longest = current;
                previous = sprite;
            }
            return longest;
        }
    }
}
