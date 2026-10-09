using System.Collections;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MmorpgClient.Tests.PlayMode
{
    // proto 生成物在全局命名空间里有同名的 Vector3;别名必须写在命名空间内部才能盖过它。
    using Vector3 = UnityEngine.Vector3;

    public sealed class QdaoArchivedActionsPlayModeTests
    {
        private const string Id = "00_reference_topright_boy";

        [UnityTest]
        public IEnumerator ArchivedRunUsesDistanceAndSourceCadenceThenReturnsToExistingIdle()
        {
            var priorCapture = Time.captureFramerate;
            Time.captureFramerate = 60;
            var camera = new GameObject("ArchivedActionsCamera");
            camera.tag = "MainCamera";
            camera.AddComponent<Camera>().orthographic = true;
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var actor = new GameObject("ArchivedRunActor");
            var residentBefore = QdaoActionResources.ResidentClipCount;
            QdaoActionResources.Lease expected = null;
            try
            {
                Assert.That(QdaoBoySpriteAnimator.TryAttach(actor, Id), Is.True);
                yield return null;
                var animator = actor.GetComponent<QdaoBoySpriteAnimator>();
                var renderer = actor.transform.Find("sprite").GetComponent<SpriteRenderer>();
                var rootPosition = actor.transform.position;
                expected = QdaoActionResources.Acquire(Id, "run", "N");
                Assert.That(expected, Is.Not.Null);
                Assert.That(expected.Frames.Length, Is.EqualTo(16));
                var distance = 0f;
                // Half speed takes twice as much wall-clock time to reach a given authored phase.
                for (var frame = 0; frame < 20; frame++)
                {
                    var step = QdaoBoySpriteAnimator.ReferenceRunSpeed * .5f / 60f;
                    distance += step;
                    actor.transform.position += Vector3.forward * step;
                    yield return null;
                    Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Run));
                    Assert.That(renderer.sprite, Is.SameAs(expected.FrameAt(distance / QdaoBoySpriteAnimator.ReferenceRunSpeed, true)));
                    Assert.That(renderer.flipX, Is.False);
                    Assert.That(actor.transform.Find("sprite").localScale, Is.EqualTo(Vector3.one));
                }
                Assert.That(actor.transform.position, Is.EqualTo(rootPosition + Vector3.forward * distance));
                yield return null;
                Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Idle));
                Assert.That(renderer.sprite.name, Is.EqualTo(Id + "_idle_N_00"));
                expected.Dispose(); expected = null;
                Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(residentBefore));
                actor.transform.position += Vector3.forward * (QdaoBoySpriteAnimator.ReferenceRunSpeed / 60f);
                yield return null;
                Assert.That(renderer.sprite.name, Does.Contain("_archived_run_N_"));
                actor.transform.position += Vector3.forward * 100f;
                yield return null;
                Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Idle));
                Assert.That(renderer.sprite.name, Is.EqualTo(Id + "_idle_N_00"),
                    "A teleport must not expose the superseded run artwork for a settling frame.");
                Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(residentBefore));
            }
            finally
            {
                expected?.Dispose();
                Time.captureFramerate = priorCapture;
                Object.Destroy(actor);
                Object.Destroy(camera);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AttackHitAndCastUseBothAuthoredSidesAndReleaseAfterCompletionOrDisable()
        {
            var priorCapture = Time.captureFramerate;
            Time.captureFramerate = 60;
            var actor = new GameObject("ArchivedActionsActor");
            var residentBefore = QdaoActionResources.ResidentClipCount;
            try
            {
                Assert.That(QdaoBoySpriteAnimator.TryAttach(actor, Id), Is.True);
                yield return null;
                var animator = actor.GetComponent<QdaoBoySpriteAnimator>();
                var renderer = actor.transform.Find("sprite").GetComponent<SpriteRenderer>();
                var idle = renderer.sprite;
                foreach (var action in new[] { "attack", "hit", "cast" })
                foreach (var direction in new[] { "E", "W" })
                {
                    var duration = QdaoActionResources.GetDurationSeconds(Id, action, direction);
                    Assert.That(duration, Is.GreaterThan(0f));
                    Assert.That(animator.PlayAction(action, direction), Is.True);
                    Assert.That(renderer.sprite.name, Is.EqualTo(Id + "_archived_" + action + "_" + direction + "_01"));
                    Assert.That(renderer.flipX, Is.False);
                    Assert.That(animator.ActiveAction, Is.EqualTo(action));
                    for (var frame = 0; frame < Mathf.CeilToInt(duration * 60f) + 3; frame++) yield return null;
                    Assert.That(animator.ActiveAction, Is.Null);
                    Assert.That(renderer.sprite, Is.SameAs(idle));
                    Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(residentBefore));
                }
                Assert.That(animator.PlayAction("cast", "W"), Is.True);
                Assert.That(animator.PlayAction("hit"), Is.True);
                Assert.That(renderer.sprite.name, Does.Contain("_hit_W_"), "Follow-up actions keep the most recent explicit facing.");
                animator.enabled = false;
                Assert.That(animator.ActiveAction, Is.Null);
                Assert.That(renderer.sprite, Is.SameAs(idle));
                Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(residentBefore));
                actor.transform.position += Vector3.right * .2f;
                animator.enabled = true;
                yield return null;
                Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Idle),
                    "Re-enabling must not animate placement that happened while disabled.");
                Assert.That(renderer.sprite, Is.SameAs(idle));
            }
            finally
            {
                Time.captureFramerate = priorCapture;
                Object.Destroy(actor);
            }
            yield return null;
        }
    }
}
