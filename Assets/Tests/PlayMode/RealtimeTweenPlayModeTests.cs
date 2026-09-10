using System.Collections;
using MmorpgClient.UI.Ugui.Tweening;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MmorpgClient.Tests.PlayMode
{
    public sealed class RealtimeTweenPlayModeTests
    {
        [UnityTest]
        public IEnumerator UnscaledTween_CompletesWhileTimeScaleIsZero()
        {
            float previousTimeScale = Time.timeScale;
            var token = new object();
            bool completed = false;
            float value = 0f;

            try
            {
                Time.timeScale = 0f;
                RealtimeTween.To(0f, 1f, 0.04f)
                    .SetTarget(token)
                    .SetEase(RealtimeEase.Linear)
                    .OnUpdate(tweener => value = tweener.Value.x)
                    .OnComplete(() => completed = true);

                float deadline = Time.realtimeSinceStartup + 1f;
                while (!completed && Time.realtimeSinceStartup < deadline) yield return null;

                Assert.That(completed, Is.True, "An unscaled UI tween must keep running while gameplay time is paused.");
                Assert.That(value, Is.EqualTo(1f).Within(0.0001f));
            }
            finally
            {
                RealtimeTween.Kill(token);
                Time.timeScale = previousTimeScale;
            }
        }

        [UnityTest]
        public IEnumerator Kill_DoesNotInvokeOnComplete()
        {
            float previousTimeScale = Time.timeScale;
            var token = new object();
            bool completed = false;

            try
            {
                var tweener = RealtimeTween.To(0f, 1f, 1f)
                    .SetTarget(token)
                    .OnComplete(() => completed = true);

                RealtimeTween.Kill(token);
                yield return null;

                Assert.That(tweener.IsActive, Is.False);
                Assert.That(tweener.Completed, Is.False);
                Assert.That(completed, Is.False, "Cancellation must not masquerade as successful completion.");
                Assert.That(RealtimeTween.IsTweening(token), Is.False);
            }
            finally
            {
                RealtimeTween.Kill(token);
                Time.timeScale = previousTimeScale;
            }
        }

        [UnityTest]
        public IEnumerator Kill_CancelsEveryTweenWithTheSameTarget()
        {
            float previousTimeScale = Time.timeScale;
            var token = new object();
            int completionCount = 0;

            try
            {
                var first = RealtimeTween.To(0f, 1f, 1f)
                    .SetTarget(token)
                    .OnComplete(() => completionCount++);
                var second = RealtimeTween.To(10f, 20f, 1f)
                    .SetTarget(token)
                    .OnComplete(() => completionCount++);

                Assert.That(RealtimeTween.IsTweening(token), Is.True);
                RealtimeTween.Kill(token);
                yield return null;

                Assert.That(first.IsActive, Is.False);
                Assert.That(second.IsActive, Is.False);
                Assert.That(completionCount, Is.Zero);
                Assert.That(RealtimeTween.IsTweening(token), Is.False);
            }
            finally
            {
                RealtimeTween.Kill(token);
                Time.timeScale = previousTimeScale;
            }
        }

        [UnityTest]
        public IEnumerator PauseSnapshot_IsReferenceCountedAndResumesAfterEverySnapshot()
        {
            float previousTimeScale = Time.timeScale;
            var token = new object();
            RealtimeTweenPauseSnapshot outer = null;
            RealtimeTweenPauseSnapshot inner = null;
            bool completed = false;
            float value = 0f;

            try
            {
                Time.timeScale = 0f;
                var tweener = RealtimeTween.To(0f, 1f, 0.05f)
                    .SetTarget(token)
                    .SetEase(RealtimeEase.Linear)
                    .OnUpdate(current => value = current.Value.x)
                    .OnComplete(() => completed = true);

                outer = RealtimeTween.PauseSnapshot();
                inner = RealtimeTween.PauseSnapshot();
                Assert.That(tweener.IsPaused, Is.True);

                outer.Resume();
                yield return WaitRealtime(0.06f);
                Assert.That(tweener.IsPaused, Is.True, "The inner snapshot must retain its own pause.");
                Assert.That(completed, Is.False);
                Assert.That(value, Is.EqualTo(0f).Within(0.0001f));

                inner.Resume();
                float deadline = Time.realtimeSinceStartup + 1f;
                while (!completed && Time.realtimeSinceStartup < deadline) yield return null;

                Assert.That(tweener.IsPaused, Is.False);
                Assert.That(completed, Is.True, "Releasing the last snapshot must resume the tween.");
                Assert.That(value, Is.EqualTo(1f).Within(0.0001f));
            }
            finally
            {
                inner?.Resume();
                outer?.Resume();
                RealtimeTween.Kill(token);
                Time.timeScale = previousTimeScale;
            }
        }

        [UnityTest]
        public IEnumerator RepeatWithYoyo_ReturnsToItsStartingValue()
        {
            float previousTimeScale = Time.timeScale;
            var token = new object();
            bool completed = false;
            int completionCount = 0;
            float value = -1f;

            try
            {
                Time.timeScale = 0f;
                var tweener = RealtimeTween.To(0f, 1f, 0.04f)
                    .SetTarget(token)
                    .SetEase(RealtimeEase.Linear)
                    .SetRepeat(1, true)
                    .OnUpdate(current => value = current.Value.x)
                    .OnComplete(() =>
                    {
                        completed = true;
                        completionCount++;
                    });

                float deadline = Time.realtimeSinceStartup + 1f;
                while (!completed && Time.realtimeSinceStartup < deadline) yield return null;

                Assert.That(tweener.Completed, Is.True);
                Assert.That(completionCount, Is.EqualTo(1));
                Assert.That(value, Is.EqualTo(0f).Within(0.0001f),
                    "One yoyo repeat must finish back at the original value.");
            }
            finally
            {
                RealtimeTween.Kill(token);
                Time.timeScale = previousTimeScale;
            }
        }

        private static IEnumerator WaitRealtime(float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline) yield return null;
        }
    }
}
