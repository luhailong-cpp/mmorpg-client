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
                for (var frame = 0; frame < 40; frame++)
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

                // 40 frames x 0.15 u = 6 u x 1.444 frames/u = 8.7 cycle frames: every pose shows.
                Assert.That(new HashSet<string>(observedSprites).Count, Is.EqualTo(8));
                Assert.That(LongestIdenticalRun(observedSprites), Is.LessThanOrEqualTo(5),
                    "A ~13 fps run (9 u/s x 1.444 frames/u) sampled at 60 Hz may hold a pose for at most five rendered frames.");
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
        public IEnumerator Stopping_FinishesTheCycleIntoDedicatedIdle_AndStartingLeavesIt()
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
                var renderer = actor.transform.Find("sprite").GetComponent<SpriteRenderer>();

                // Fresh actor uses the separately authored S standing texture.
                Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Idle));
                Assert.That(renderer.sprite.name, Is.EqualTo("qdao_idle_S_00"));

                // Start north: the first step is taken from the N standing pose (frame 2).
                actor.transform.position += Vector3.forward * StepPerFrame;
                yield return null;
                Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Run));
                Assert.That(renderer.sprite.name, Is.EqualTo("qdao_run_N_02"));

                // 29 more frames: 4.5 u total = 6.5 cycle frames past pose 2 -> pose 0 (mid-stride).
                for (var frame = 0; frame < 29; frame++)
                {
                    actor.transform.position += Vector3.forward * StepPerFrame;
                    yield return null;
                    Assert.That(renderer.sprite.name, Does.Contain("_N_"));
                }
                Assert.That(renderer.sprite.name, Is.EqualTo("qdao_run_N_00"));

                // Stop. The cycle must play on (0 -> 1 -> 2) at run cadence
                // instead of snapping, then show the separate N idle texture.
                var settling = new List<string>();
                for (var frame = 0; frame < 60; frame++)
                {
                    yield return null;
                    settling.Add(renderer.sprite.name);
                    Assert.That(renderer.sprite.name, Does.Contain("_N_"), "stopping keeps the last facing");
                    if (animator.State == QdaoBoySpriteAnimator.LocomotionState.Idle) break;
                }
                Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Idle),
                    "the stop must settle within one lap of the cycle");
                Assert.That(settling[^1], Is.EqualTo("qdao_idle_N_00"));
                Assert.That(settling.Count, Is.InRange(2, 20),
                    "1.5 frames at ~13 fps take several 60 Hz frames: no instant jump, no long wait");
                Assert.That(settling.IndexOf("qdao_run_N_01"), Is.GreaterThanOrEqualTo(0)
                    .And.LessThan(settling.IndexOf("qdao_idle_N_00")),
                    "the cycle walks through pose 1 on its way to the standing pose");

                // Standing still holds the pose.
                for (var frame = 0; frame < 10; frame++)
                {
                    yield return null;
                    Assert.That(renderer.sprite.name, Is.EqualTo("qdao_idle_N_00"));
                    Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Idle));
                }

                // Reverse: the strip flips at once and the step starts from the S standing pose.
                actor.transform.position += Vector3.back * StepPerFrame;
                yield return null;
                Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Run));
                Assert.That(renderer.sprite.name, Is.EqualTo("qdao_run_S_02"));
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
                    Assert.That(renderer.sprite.name, Is.EqualTo($"qdao_idle_{directions[direction]}_00"));
                    Assert.That(renderer.sprite.texture, Is.SameAs(Resources.Load<Texture2D>(
                        $"World/Characters/QdaoHeadbandBoy/idle_{directions[direction]}")),
                        "Idle must use dedicated artwork, never a held run frame.");
                    Assert.That(renderer.sprite.pixelsPerUnit, Is.EqualTo(QdaoBoySpriteAnimator.PixelsPerUnit));
                    Assert.That(renderer.sprite.pivot.y, Is.EqualTo(512f * 0.08f).Within(0.01f));
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
