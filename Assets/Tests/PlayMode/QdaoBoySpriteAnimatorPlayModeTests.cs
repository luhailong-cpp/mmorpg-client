using System.Collections;
using System.Collections.Generic;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;

namespace MmorpgClient.Tests.PlayMode
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class QdaoBoySpriteAnimatorPlayModeTests
    {
        [UnityTest]
        public IEnumerator Running_KeepsFeetAndWorldRigidWhileAdvancingAtRunCadence()
        {
            var previousCaptureFramerate = Time.captureFramerate;
            Time.captureFramerate = 60;

            var cameraObject = new GameObject("RunTestCamera");
            cameraObject.tag = "MainCamera";
            var worldCamera = cameraObject.AddComponent<Camera>();
            worldCamera.orthographic = true;
            worldCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

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

                var observedSprites = new List<string>();
                for (var frame = 0; frame < 40; frame++)
                {
                    actor.transform.position += Vector3.forward * (9f / 60f);
                    yield return null;

                    observedSprites.Add(renderer.sprite.name);
                    Assert.That(renderer.sprite.name, Does.Contain("_N_"));
                    Assert.That(billboard.localScale, Is.EqualTo(Vector3.one));
                    Assert.That(Quaternion.Angle(billboard.rotation, worldCamera.transform.rotation),
                        Is.LessThan(0.01f));
                    Assert.That(billboard.position - actor.transform.position,
                        Is.EqualTo(Vector3.up * 0.1f).Using(Vector3ComparerWithEqualsOperator.Instance));

                    Assert.That(worldDecoration.transform.position, Is.EqualTo(decorationPosition));
                    Assert.That(worldDecoration.transform.rotation, Is.EqualTo(decorationRotation));
                    Assert.That(worldDecoration.transform.localScale, Is.EqualTo(decorationScale));
                }

                Assert.That(new HashSet<string>(observedSprites).Count, Is.EqualTo(8));
                Assert.That(LongestIdenticalRun(observedSprites), Is.LessThanOrEqualTo(4),
                    "A 16 fps run sampled at 60 Hz may hold a pose for at most four rendered frames.");
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
