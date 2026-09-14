using System.Collections;
using System.Collections.Generic;
using System.IO;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MmorpgClient.Tests.PlayMode
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class QdaoRosterAnimatorPlayModeTests
    {
        private const float StepPerFrame = 9f / 60f;
        private static readonly string[] Directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        [UnityTest]
        public IEnumerator EveryCharacter_WalksItsDeclaredFramesInAllEightDirections_AndSettlesOnItsIdlePose()
        {
            var previousCapture = Time.captureFramerate;
            Time.captureFramerate = 60;
            var cameraObject = CreateCamera();
            GameObject actor = null;
            try
            {
                foreach (var definition in QdaoCharacterCatalog.All)
                {
                    actor = new GameObject(definition.Id);
                    Assert.That(QdaoBoySpriteAnimator.TryAttach(actor, definition.Id), Is.True, definition.Id);
                    yield return null;
                    var animator = actor.GetComponent<QdaoBoySpriteAnimator>();
                    var renderer = actor.transform.Find("sprite").GetComponent<SpriteRenderer>();
                    Assert.That(animator.CharacterId, Is.EqualTo(definition.Id));
                    Assert.That(animator.FrameCount, Is.EqualTo(definition.FrameCount));
                    Assert.That(animator.ArtworkVersion, Is.EqualTo(definition.Version));
                    for (var direction = 0; direction < Directions.Length; direction++)
                    {
                        var radians = direction * 45f * Mathf.Deg2Rad;
                        var step = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * StepPerFrame;
                        var poses = new HashSet<Sprite>();
                        Sprite previousPose = null;
                        var transitions = 0;
                        var heldFrames = 0;
                        var longestHold = 0;
                        var directionFrames = new HashSet<Texture2D>();
                        for (var frameIndex = 1; frameIndex <= definition.FrameCount; frameIndex++)
                        {
                            var texture = Resources.Load<Texture2D>($"{definition.ResourceFolder}/walk/{Directions[direction]}/{frameIndex:00}");
                            Assert.That(texture, Is.Not.Null);
                            Assert.That(texture.width, Is.EqualTo(512));
                            Assert.That(texture.height, Is.EqualTo(512));
                            directionFrames.Add(texture);
                        }
                        Assert.That(directionFrames.Count, Is.EqualTo(definition.FrameCount));
                        for (var frame = 0; frame < 48; frame++)
                        {
                            actor.transform.position += step;
                            yield return null;
                            Assert.That(animator.Direction, Is.EqualTo(direction), $"{definition.Id}/{Directions[direction]}");
                            Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Run));
                            Assert.That(directionFrames.Contains(renderer.sprite.texture), Is.True, "Walk animation must use an individually imported direction frame.");
                            Assert.That(renderer.sprite.rect.x, Is.Zero);
                            Assert.That(renderer.sprite.rect.width, Is.EqualTo(512));
                            Assert.That(renderer.sprite.pivot.y, Is.EqualTo(512f * 0.08f).Within(0.01f));
                            poses.Add(renderer.sprite);
                            if (previousPose != null && previousPose != renderer.sprite) transitions++;
                            heldFrames = previousPose == renderer.sprite ? heldFrames + 1 : 1;
                            longestHold = Mathf.Max(longestHold, heldFrames);
                            previousPose = renderer.sprite;
                        }
                        Assert.That(poses.Count, Is.EqualTo(definition.FrameCount), $"{definition.Id}/{Directions[direction]} skips or invents a pose.");
                        var expectedTransitions = .8f * definition.FramesPerSecond;
                        Assert.That(transitions, Is.InRange(Mathf.FloorToInt(expectedTransitions) - 1, Mathf.CeilToInt(expectedTransitions)),
                            "Animation cadence must follow actual distance while keeping the declared cycle duration.");
                        Assert.That(longestHold, Is.LessThanOrEqualTo(Mathf.CeilToInt(60f / definition.FramesPerSecond)),
                            "Each pose hold must respect the selected appearance's 60/120 ms frame duration.");
                        var stoppedPosition = actor.transform.position;
                        for (var frame = 0; frame < 48; frame++)
                        {
                            yield return null;
                            if (animator.State == QdaoBoySpriteAnimator.LocomotionState.Idle) break;
                        }
                        Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Idle));
                        Assert.That(animator.Direction, Is.EqualTo(direction));
                        Assert.That(actor.transform.position, Is.EqualTo(stoppedPosition), "Settling animation must not move the actor root.");
                        Assert.That(renderer.sprite.texture, Is.SameAs(Resources.Load<Texture2D>(
                            definition.IdleResourcePath(Directions[direction]))), "Standing uses the selected version's authored idle or V11 contact fallback.");
                        Assert.That(renderer.sprite.rect.x, Is.Zero);
                        var standing = renderer.sprite;
                        for (var frame = 0; frame < 3; frame++)
                        {
                            yield return null;
                            Assert.That(renderer.sprite, Is.SameAs(standing));
                        }
                    }
                    Object.Destroy(actor);
                    actor = null;
                    yield return null;
                }
            }
            finally
            {
                Time.captureFramerate = previousCapture;
                if (actor != null) Object.Destroy(actor);
                Object.Destroy(cameraObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator V12_StoppingAtDifferentPhases_ShowsDirectionIdleOnTheNextFrame()
        {
            var previousCapture = Time.captureFramerate;
            Time.captureFramerate = 60;
            var cameraObject = CreateCamera();
            var actor = new GameObject("V12ImmediateStopActor");
            try
            {
                var definition = QdaoCharacterCatalog.Find("24_lu_dongbin");
                var appearance = definition.ResolveAppearance();
                Assert.That(appearance.Version, Is.EqualTo(12), "This regression requires the published eight-frame set.");
                Assert.That(appearance.HasDedicatedIdle, Is.True);
                Assert.That(QdaoBoySpriteAnimator.TryAttach(actor, definition.Id), Is.True);
                yield return null;
                var animator = actor.GetComponent<QdaoBoySpriteAnimator>();
                var renderer = actor.transform.Find("sprite").GetComponent<SpriteRenderer>();

                // Reach each phase through real, below-warp-threshold travel.
                // 1.1 used to keep walking in place for ~0.42 s; the other
                // phases cover the opposite step and the end of the cycle.
                foreach (var phase in new[] { 1.1f, 4.2f, 7.8f })
                for (var direction = 0; direction < Directions.Length; direction++)
                {
                    var radians = direction * 45f * Mathf.Deg2Rad;
                    var heading = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
                    var remaining = phase * 9f / appearance.FramesPerSecond;
                    while (remaining > 0.0001f)
                    {
                        var distance = Mathf.Min(StepPerFrame, remaining);
                        actor.transform.position += heading * distance;
                        remaining -= distance;
                        yield return null;
                    }
                    Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Run));
                    var expectedFrame = (appearance.ContactFrame + Mathf.FloorToInt(phase)) % appearance.FrameCount;
                    Assert.That(renderer.sprite.texture, Is.SameAs(Resources.Load<Texture2D>(
                        appearance.FrameResourcePath(Directions[direction], expectedFrame))),
                        $"The stop must begin at the intended phase {phase}, direction {Directions[direction]}.");

                    var stoppedPosition = actor.transform.position;
                    yield return null;

                    Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Idle),
                        $"V12 must not finish a walk cycle after stopping at phase {phase}.");
                    Assert.That(animator.Direction, Is.EqualTo(direction));
                    Assert.That(actor.transform.position, Is.EqualTo(stoppedPosition));
                    Assert.That(renderer.sprite.texture, Is.SameAs(Resources.Load<Texture2D>(
                        appearance.IdleResourcePath(Directions[direction]))),
                        "The first stationary frame must show the dedicated standing texture.");
                }
            }
            finally
            {
                Time.captureFramerate = previousCapture;
                Object.Destroy(actor);
                Object.Destroy(cameraObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator SwitchingAppearance_PreservesActorTransformFacingAndChildren_AndDoesNotChangeOtherActors()
        {
            var previousCapture = Time.captureFramerate;
            Time.captureFramerate = 60;
            var cameraObject = CreateCamera();
            var first = new GameObject("AppearanceSwitchActor");
            var second = new GameObject("IndependentAppearanceActor");
            try
            {
                Assert.That(QdaoBoySpriteAnimator.TryAttach(first, "24_lu_dongbin"), Is.True);
                Assert.That(QdaoBoySpriteAnimator.TryAttach(second, "24_lu_dongbin"), Is.True);
                yield return null;
                first.transform.position += Vector3.right * StepPerFrame;
                yield return null;
                var animator = first.GetComponent<QdaoBoySpriteAnimator>();
                var renderer = first.transform.Find("sprite").GetComponent<SpriteRenderer>();
                var otherRenderer = second.transform.Find("sprite").GetComponent<SpriteRenderer>();
                var otherSprite = otherRenderer.sprite;
                var position = first.transform.position;
                var rotation = first.transform.rotation;
                var scale = first.transform.localScale;
                var direction = animator.Direction;
                var children = first.transform.childCount;
                var billboard = renderer.transform;
                var shadow = first.transform.Find(QdaoBoySpriteAnimator.ShadowObjectName);

                foreach (var definition in QdaoCharacterCatalog.All)
                {
                    Assert.That(animator.SetAppearance(definition.Id), Is.True);
                    Assert.That(QdaoBoySpriteAnimator.TryAttach(first, definition.Id), Is.True, "Repeated attach must be idempotent.");
                    yield return null;
                    Assert.That(animator.CharacterId, Is.EqualTo(definition.Id));
                    Assert.That(animator.Direction, Is.EqualTo(direction));
                    Assert.That(first.transform.position, Is.EqualTo(position));
                    Assert.That(first.transform.rotation, Is.EqualTo(rotation));
                    Assert.That(first.transform.localScale, Is.EqualTo(scale));
                    Assert.That(first.transform.childCount, Is.EqualTo(children));
                    Assert.That(first.GetComponents<QdaoBoySpriteAnimator>().Length, Is.EqualTo(1));
                    Assert.That(first.transform.Find("sprite"), Is.SameAs(billboard));
                    Assert.That(first.transform.Find(QdaoBoySpriteAnimator.ShadowObjectName), Is.SameAs(shadow));
                    Assert.That(renderer.sprite.texture, Is.SameAs(Resources.Load<Texture2D>(
                        definition.IdleResourcePath(Directions[direction]))));
                    Assert.That(otherRenderer.sprite, Is.SameAs(otherSprite), "One actor's appearance must not overwrite the shared legacy singleton cache.");
                }
                Assert.That(animator.SetAppearance("not-an-approved-character"), Is.True);
                yield return null;
                Assert.That(animator.CharacterId, Is.EqualTo("QdaoHeadbandBoy"));
                Assert.That(animator.FrameCount, Is.EqualTo(8));
                Assert.That(first.transform.childCount, Is.EqualTo(children));
                Assert.That(renderer.sprite.texture, Is.SameAs(Resources.Load<Texture2D>(
                    $"World/Characters/QdaoHeadbandBoy/idle_{Directions[direction]}")));
            }
            finally
            {
                Time.captureFramerate = previousCapture;
                Object.Destroy(first);
                Object.Destroy(second);
                Object.Destroy(cameraObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ApprovedRoster_RendersTogetherWithIndependentTextures()
        {
            var cameraObject = CreateCamera();
            var camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0f, 50f, 2f);
            camera.orthographicSize = 19f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.045f, 0.08f, 0.1f, 1f);
            var actors = new List<GameObject>();
            RenderTexture target = null;
            Texture2D capture = null;
            var oldActive = RenderTexture.active;
            try
            {
                var textures = new HashSet<Texture>();
                for (var index = 0; index < QdaoCharacterCatalog.All.Count; index++)
                {
                    var definition = QdaoCharacterCatalog.All[index];
                    var actor = new GameObject(definition.Id);
                    actors.Add(actor);
                    actor.transform.position = new Vector3((index % 4 - 1.5f) * 12f, 0f, index < 4 ? 5f : -11f);
                    Assert.That(QdaoBoySpriteAnimator.TryAttach(actor, definition.Id), Is.True);
                }
                yield return null;
                foreach (var actor in actors)
                {
                    var renderer = actor.transform.Find("sprite").GetComponent<SpriteRenderer>();
                    Assert.That(renderer.sprite, Is.Not.Null);
                    textures.Add(renderer.sprite.texture);
                }
                Assert.That(textures.Count, Is.EqualTo(8), "The eight simultaneous actors must retain eight different appearances.");

                // This is a controlled runtime test scene, not evidence of an online login.
                // Keep graphic capture opt-in so headless CI still runs the behavior assertions.
                var outputDirectory = System.Environment.GetEnvironmentVariable("QDAO_ROSTER_CAPTURE_DIR");
                if (!string.IsNullOrEmpty(outputDirectory) && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                {
                    Directory.CreateDirectory(outputDirectory);
                    target = new RenderTexture(1600, 1000, 24);
                    camera.targetTexture = target;
                    camera.Render();
                    RenderTexture.active = target;
                    capture = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
                    capture.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
                    capture.Apply();
                    var path = Path.Combine(outputDirectory, "approved-roster-runtime.png");
                    File.WriteAllBytes(path, capture.EncodeToPNG());
                    var labels = new List<string> { "Controlled Unity PlayMode scene. Top row then bottom row, left to right:" };
                    foreach (var definition in QdaoCharacterCatalog.All) labels.Add(definition.Id + " " + definition.Name + " V" + definition.Version + " frames=" + definition.FrameCount);
                    File.WriteAllLines(Path.Combine(outputDirectory, "approved-roster-runtime.txt"), labels);
                    Debug.Log("[QdaoRosterCapture] " + path);
                    Assert.That(new FileInfo(path).Length, Is.GreaterThan(10000));
                }
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = oldActive;
                if (capture != null) Object.Destroy(capture);
                if (target != null) { target.Release(); Object.Destroy(target); }
                foreach (var actor in actors) Object.Destroy(actor);
                Object.Destroy(cameraObject);
            }
            yield return null;
        }

        private static GameObject CreateCamera()
        {
            var cameraObject = new GameObject("RosterTestCamera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 50f, 0f);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            return cameraObject;
        }
    }
}
