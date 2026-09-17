using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
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
                foreach (var definition in QdaoCharacterCatalog.AvailableAll)
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
                            "Each pose hold must respect the selected appearance's 30/60/120 ms frame duration.");
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
                var definition = QdaoCharacterCatalog.Find("23_lantern_courier");
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

                foreach (var definition in QdaoCharacterCatalog.AvailableAll)
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
        public IEnumerator OriginalIdsAwaitingApproval_DoNotReplaceTheCurrentActorWithLegacyOrAnotherRosterIdentity()
        {
            var actor = new GameObject("OriginalApprovalGuardActor");
            try
            {
                Assert.That(QdaoBoySpriteAnimator.TryAttach(actor, "24_lu_dongbin"), Is.True);
                yield return null;
                var animator = actor.GetComponent<QdaoBoySpriteAnimator>();
                var renderer = actor.transform.Find("sprite").GetComponent<SpriteRenderer>();
                var identity = animator.CharacterId;
                var sprite = renderer.sprite;
                var position = actor.transform.position;
                foreach (var entry in QdaoCharacterCatalog.OriginalAll)
                {
                    if (entry.ResolveAppearance() != null) continue;
                    Assert.That(QdaoBoySpriteAnimator.TryAttach(actor, entry.Id), Is.False, entry.Id);
                    Assert.That(animator.SetAppearance(entry.Id), Is.False, entry.Id);
                    Assert.That(animator.CharacterId, Is.EqualTo(identity));
                    Assert.That(renderer.sprite, Is.SameAs(sprite));
                    Assert.That(actor.transform.position, Is.EqualTo(position));
                }
            }
            finally { Object.Destroy(actor); }
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
                    foreach (var definition in QdaoCharacterCatalog.AvailableAll) labels.Add(definition.Id + " " + definition.Name + " V" + definition.Version + " frames=" + definition.FrameCount);
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

        // In-memory test textures exercise the real loader and animator while V13
        // art is still unpublished. They are never written to Resources or review output.
        private sealed class MockAppearanceResources : System.IDisposable
        {
            private const string Character = "24_lu_dongbin";
            private readonly Dictionary<int, Texture2D> _walk = new();
            private readonly Dictionary<int, Texture2D> _idle = new();
            private readonly Texture2D _portrait = new(1024, 1024, TextureFormat.RGBA32, false);
            private readonly HashSet<Sprite> _sprites = new();
            private readonly HashSet<object> _sets = new();
            private readonly List<Texture2D> _retiredTextures = new();
            private readonly HashSet<string> _missing = new();
            private static readonly MethodInfo Loader = typeof(QdaoBoySpriteAnimator).GetMethod(
                "LoadFrameSetWithResources", BindingFlags.Static | BindingFlags.NonPublic);
            private static readonly MethodInfo Apply = typeof(QdaoBoySpriteAnimator).GetMethod(
                "ApplyFrames", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo Clock = typeof(QdaoBoySpriteAnimator).GetField(
                "_animationClock", BindingFlags.Instance | BindingFlags.NonPublic);

            public MockAppearanceResources()
            {
                foreach (var version in new[] { 11, 12, 13 })
                {
                    _walk[version] = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                    _idle[version] = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                }
            }

            public static string Metadata(int version)
                => "{\"version\":" + version + ",\"characterId\":\"" + Character + "\",\"alignmentVersion\":3," +
                   "\"frameCount\":" + (version == 13 ? 16 : 8) + ",\"frameDurationMs\":" + (version == 13 ? 30 : 60) +
                   ",\"dedicatedIdle\":true,\"contactFrame\":0,\"status\":\"passed\",\"visualReview\":\"passed\"," +
                   "\"manifest_sha256\":\"" + new string('a', 64) + "\",\"qc_sha256\":\"" + new string('b', 64) +
                   "\",\"validation_sha256\":\"" + new string('c', 64) + "\"}";

            public void Remove(int version, string relativePath)
                => _missing.Add($"World/Characters/QdaoRosterV{version}/{Character}/{relativePath}");

            public Texture2D Walk(int version) => _walk[version];
            public Texture2D Idle(int version) => _idle[version];
            public static float Phase(QdaoBoySpriteAnimator animator)
                => (float)Clock.GetValue(animator) / animator.FrameCount;

            private Texture2D Load(string path)
            {
                if (_missing.Contains(path)) return null;
                var version = path.Contains("V13/") ? 13 : path.Contains("V12/") ? 12 : 11;
                return path.EndsWith("/portrait") ? _portrait : path.Contains("/idle/") ? _idle[version] : _walk[version];
            }

            public void ReplaceWalkTexture(int version)
            {
                _retiredTextures.Add(_walk[version]);
                _walk[version] = new Texture2D(512, 512, TextureFormat.RGBA32, false);
            }

            public object Build(int version, bool useCache = false)
            {
                var definition = QdaoCharacterCatalog.Find(Character);
                var selected = QdaoCharacterCatalog.SelectAppearance(definition,
                    version == 13 ? Metadata(13) : null, Metadata(12), (_, _, _) => true);
                System.Func<string, Texture2D> load = Load;
                System.Func<QdaoCharacterCatalog.Appearance, QdaoCharacterCatalog.Appearance> fallback = rejected =>
                    QdaoCharacterCatalog.SelectFallbackAppearance(rejected, Metadata(12), (_, _, _) => true);
                var result = Loader.Invoke(null, new object[] { selected, load, fallback, useCache });
                if (result != null)
                {
                    _sets.Add(result);
                    foreach (var row in (Sprite[][])Field(result, "Walk")) foreach (var sprite in row) _sprites.Add(sprite);
                    foreach (var sprite in (Sprite[])Field(result, "Idle")) _sprites.Add(sprite);
                }
                return result;
            }

            public QdaoBoySpriteAnimator Attach(GameObject actor, int version)
            {
                var animator = actor.AddComponent<QdaoBoySpriteAnimator>();
                Apply.Invoke(animator, new[] { Build(version) });
                return animator;
            }

            public static object Field(object set, string name) => set.GetType().GetField(name).GetValue(set);

            public void Dispose()
            {
                var shared = (System.Collections.IDictionary)typeof(QdaoBoySpriteAnimator).GetField(
                    "SharedFrames", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                var keys = new List<object>();
                foreach (System.Collections.DictionaryEntry entry in shared)
                    if (_sets.Contains(entry.Value)) keys.Add(entry.Key);
                foreach (var key in keys) shared.Remove(key);
                foreach (var texture in _retiredTextures) Object.Destroy(texture);
                foreach (var sprite in _sprites) Object.Destroy(sprite);
                foreach (var texture in _walk.Values) Object.Destroy(texture);
                foreach (var texture in _idle.Values) Object.Destroy(texture);
                Object.Destroy(_portrait);
            }
        }

        [TestCase("walk/NW/16", false)]
        [TestCase("idle/S", false)]
        [TestCase("portrait", false)]
        [TestCase("walk/NW/16", true)]
        [TestCase("idle/S", true)]
        [TestCase("portrait", true)]
        public void V13_ResourceDisappearingAfterValidation_LoadsOneWholeFallbackVersion(string missing, bool v12AlsoMissing)
        {
            using var resources = new MockAppearanceResources();
            resources.Remove(13, missing);
            LogAssert.Expect(LogType.Warning, new Regex("Incomplete V13 artwork: .*Retaining 24_lu_dongbin V12"));
            if (v12AlsoMissing)
            {
                resources.Remove(12, "walk/NE/08");
                LogAssert.Expect(LogType.Warning, new Regex("Incomplete V12 artwork: .*Retaining 24_lu_dongbin V11"));
            }
            var result = resources.Build(13);
            var expected = v12AlsoMissing ? 11 : 12;
            Assert.That(result, Is.Not.Null);
            Assert.That(MockAppearanceResources.Field(result, "Id"), Is.EqualTo("24_lu_dongbin"));
            Assert.That(MockAppearanceResources.Field(result, "Version"), Is.EqualTo(expected));
            Assert.That(MockAppearanceResources.Field(result, "Count"), Is.EqualTo(expected == 12 ? 8 : 4));
            foreach (var row in (Sprite[][])MockAppearanceResources.Field(result, "Walk"))
                foreach (var frame in row) Assert.That(frame.texture, Is.SameAs(resources.Walk(expected)));
        }

        [Test]
        public void V13_CacheHitRevalidatesResourcesAndRebuildsReplacedTexturesBeforeReturningSprites()
        {
            using var resources = new MockAppearanceResources();
            var first = resources.Build(13, true);
            Assert.That(resources.Build(13, true), Is.SameAs(first));
            resources.ReplaceWalkTexture(13);
            var revised = resources.Build(13, true);
            Assert.That(revised, Is.Not.SameAs(first));
            foreach (var row in (Sprite[][])MockAppearanceResources.Field(revised, "Walk"))
                foreach (var frame in row) Assert.That(frame.texture, Is.SameAs(resources.Walk(13)));
            resources.Remove(13, "walk/S/16");
            LogAssert.Expect(LogType.Warning, new Regex("Incomplete V13 artwork: .*Retaining 24_lu_dongbin V12"));
            var fallback = resources.Build(13, true);
            Assert.That(MockAppearanceResources.Field(fallback, "Version"), Is.EqualTo(12));
            foreach (var row in (Sprite[][])MockAppearanceResources.Field(fallback, "Walk"))
                foreach (var frame in row) Assert.That(frame.texture, Is.SameAs(resources.Walk(12)));
        }

        [UnityTest]
        public IEnumerator V13AndV12_SameTravelMaintainsCyclePhaseAndSwitchingDirectionDoesNotRestartIt()
        {
            var previousCapture = Time.captureFramerate;
            Time.captureFramerate = 60;
            var cameraObject = CreateCamera();
            var first = new GameObject("SixteenPoseMockActor");
            var second = new GameObject("EightPoseMockActor");
            using var resources = new MockAppearanceResources();
            try
            {
                var sixteen = resources.Attach(first, 13);
                var eight = resources.Attach(second, 12);
                yield return null;
                Assert.That(sixteen.FrameCount, Is.EqualTo(16));
                Assert.That(eight.FrameCount, Is.EqualTo(8));
                var sixteenPoses = new HashSet<Sprite>();
                var eightPoses = new HashSet<Sprite>();
                for (var frame = 0; frame < 58; frame++)
                {
                    // Change heading continuously through all eight strips without
                    // changing distance, including the 15 -> 0 cycle seam.
                    var direction = (frame / 7) % 8;
                    var radians = direction * 45f * Mathf.Deg2Rad;
                    var step = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * StepPerFrame;
                    var previousPhase = MockAppearanceResources.Phase(sixteen);
                    first.transform.position += step;
                    second.transform.position += step;
                    yield return null;
                    var expectedPhase = Mathf.Repeat(previousPhase + StepPerFrame / 4.32f, 1f);
                    Assert.That(MockAppearanceResources.Phase(sixteen), Is.EqualTo(expectedPhase).Within(.00001f));
                    Assert.That(MockAppearanceResources.Phase(sixteen),
                        Is.EqualTo(MockAppearanceResources.Phase(eight)).Within(.00001f));
                    Assert.That(sixteen.Direction, Is.EqualTo(direction));
                    Assert.That(eight.Direction, Is.EqualTo(direction));
                    Assert.That(first.transform.position, Is.EqualTo(second.transform.position));
                    sixteenPoses.Add(first.transform.Find("sprite").GetComponent<SpriteRenderer>().sprite);
                    eightPoses.Add(second.transform.Find("sprite").GetComponent<SpriteRenderer>().sprite);
                }
                Assert.That(sixteenPoses.Count, Is.GreaterThan(eightPoses.Count));
                Assert.That(sixteen.ArtworkVersion, Is.EqualTo(13));
                Assert.That(eight.ArtworkVersion, Is.EqualTo(12));
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
        public IEnumerator V13_AllSixteenPhases_StopImmediatelyOnIndependentIdleAndPreserveActorGeometry()
        {
            var previousCapture = Time.captureFramerate;
            Time.captureFramerate = 60;
            var cameraObject = CreateCamera();
            var actor = new GameObject("V13PhaseStopMockActor");
            var collider = actor.AddComponent<CapsuleCollider>();
            collider.radius = .75f;
            collider.height = 3f;
            using var resources = new MockAppearanceResources();
            try
            {
                var animator = resources.Attach(actor, 13);
                yield return null;
                var renderer = actor.transform.Find("sprite").GetComponent<SpriteRenderer>();
                var shadow = actor.transform.Find(QdaoBoySpriteAnimator.ShadowObjectName);
                var shadowScale = shadow.localScale;
                var poses = new HashSet<Sprite>();
                for (var phase = 0; phase < 16; phase++)
                {
                    var remaining = (phase + .2f) * (9f / (1000f / 30f));
                    while (remaining > .0001f)
                    {
                        var travel = Mathf.Min(.15f, remaining);
                        actor.transform.position += Vector3.forward * travel;
                        remaining -= travel;
                        yield return null;
                    }
                    Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Run));
                    Assert.That(renderer.sprite.texture, Is.SameAs(resources.Walk(13)));
                    Assert.That(renderer.sprite.name, Does.EndWith("_" + phase.ToString("00")));
                    poses.Add(renderer.sprite);
                    var position = actor.transform.position;
                    yield return null;
                    Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Idle));
                    Assert.That(renderer.sprite.texture, Is.SameAs(resources.Idle(13)));
                    Assert.That(actor.transform.position, Is.EqualTo(position));
                    Assert.That(collider.radius, Is.EqualTo(.75f));
                    Assert.That(collider.height, Is.EqualTo(3f));
                    Assert.That(actor.transform.Find(QdaoBoySpriteAnimator.ShadowObjectName), Is.SameAs(shadow));
                    Assert.That(shadow.localScale, Is.EqualTo(shadowScale));
                }
                Assert.That(poses.Count, Is.EqualTo(16));
            }
            finally
            {
                Time.captureFramerate = previousCapture;
                Object.Destroy(actor);
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
