using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MmorpgClient.World;
using MmorpgClient.UI.Ugui.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Vector3 = UnityEngine.Vector3;

namespace MmorpgClient.Tests.PlayMode
{
    /// <summary>Synthetic in-memory texture fixtures test the real loaders/lifetimes; they never approve or publish artwork.</summary>
    public sealed class QdaoHdResourceLifetimePlayModeTests
    {
        private static readonly BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly string[] Directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        private static readonly MethodInfo Create = typeof(QdaoBoySpriteAnimator).GetMethod("CreateHdFrameSet", HiddenStatic);
        private static readonly MethodInfo Apply = typeof(QdaoBoySpriteAnimator).GetMethod("ApplyFrames", HiddenInstance);
        private static readonly MethodInfo Acquire = typeof(QdaoHdResources).GetMethod("AcquireWithResources", HiddenStatic);
        private static readonly MethodInfo BattleLoad = typeof(BattleArtCatalog).GetMethod("LoadRosterDirectionWithResources", HiddenStatic);
        private static readonly FieldInfo Frames = typeof(QdaoBoySpriteAnimator).GetField("_frames", HiddenInstance);
        private static readonly FieldInfo Clock = typeof(QdaoBoySpriteAnimator).GetField("_animationClock", HiddenInstance);

        private sealed class ResourcesFixture : IDisposable
        {
            public readonly HashSet<string> Missing = new();
            public readonly HashSet<string> WrongSize = new();
            private readonly Dictionary<string, Texture2D> _live = new();
            private readonly List<Texture2D> _created = new();
            public readonly List<string> Requested = new();
            public int Released, MaximumLive;
            public int Live => _live.Count;
            public Texture2D Load(string path)
            {
                if (!path.StartsWith(QdaoCharacterCatalog.OriginalV14Root + "/", StringComparison.Ordinal))
                    return UnityEngine.Resources.Load<Texture2D>(path);
                Requested.Add(path);
                if (Missing.Contains(path)) return null;
                if (_live.TryGetValue(path, out var existing)) return existing;
                var size = WrongSize.Contains(path) ? 512 : 1024;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = path };
                texture.Apply(false, true);
                _live[path] = texture; _created.Add(texture);
                MaximumLive = Math.Max(MaximumLive, Live);
                return texture;
            }
            public void Release(Texture2D texture)
            {
                if (_live.TryGetValue(texture.name, out var current) && current == texture) _live.Remove(texture.name);
                Released++;
                // Test-owned textures only; the production callback uses Resources.UnloadAsset.
                Object.DestroyImmediate(texture);
            }
            public QdaoHdResources.Lease Lease(QdaoCharacterCatalog.Appearance appearance, int direction)
                => (QdaoHdResources.Lease)Acquire.Invoke(null, new object[] { appearance, direction,
                    (Func<string, Texture2D>)Load, (Action<Texture2D>)Release, true });
            public object FrameSet(QdaoCharacterCatalog.Appearance appearance, bool fallback = true)
                => Create.Invoke(null, new object[] { appearance, (Func<string, Texture2D>)Load, (Action<Texture2D>)Release,
                    (Func<QdaoCharacterCatalog.Appearance, QdaoCharacterCatalog.Appearance>)(fallback ? V13Fallback : _ => null), true });
            public QdaoBoySpriteAnimator Attach(GameObject actor, QdaoCharacterCatalog.Appearance appearance)
            {
                var animator = actor.AddComponent<QdaoBoySpriteAnimator>();
                Assert.That((bool)Apply.Invoke(animator, new[] { FrameSet(appearance) }), Is.True);
                return animator;
            }
            public StripAnim Battle(QdaoCharacterCatalog.Appearance appearance, bool walk, bool fallback = true)
                => (StripAnim)BattleLoad.Invoke(null, new object[] { appearance, "E", walk,
                    (Func<string, Texture2D>)Load, (Action<Texture2D>)Release,
                    (Func<QdaoCharacterCatalog.Appearance, QdaoCharacterCatalog.Appearance>)(fallback ? V13Fallback : _ => null), true });
            public void Dispose()
            {
                foreach (var texture in _created) if (texture != null) Object.DestroyImmediate(texture);
                _live.Clear();
            }
        }

        private static QdaoCharacterCatalog.Appearance Hd(string id = "03_lotus_healer_girl")
        {
            var manifest = "{\"version\":14,\"character_id\":\"" + id + "\",\"status\":\"passed\",\"visual_review\":\"passed\"," +
                "\"frame_count\":16,\"frame_duration_ms\":30,\"cycle_duration_ms\":480,\"dedicated_idle\":true,\"contact_frame\":0," +
                "\"alignment\":{\"alignment_version\":2,\"root_px\":[512,942]},\"frame_size\":[1024,1024],\"portrait_size\":[1024,1024]," +
                "\"runtime_geometry\":{\"reference_frame_size\":512,\"pixels_per_unit\":104,\"pivot\":[0.5,0.08]},\"test_nonce\":\"" + Guid.NewGuid().ToString("N") + "\"}";
            var bytes = Encoding.UTF8.GetBytes(manifest);
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            var activation = "{\"version\":14,\"characterId\":\"" + id + "\",\"status\":\"passed\",\"visualReview\":\"passed\"," +
                "\"frameCount\":16,\"frameDurationMs\":30,\"cycleDurationMs\":480,\"alignmentVersion\":2,\"dedicatedIdle\":true,\"contactFrame\":0," +
                "\"frameWidth\":1024,\"frameHeight\":1024,\"portraitWidth\":1024,\"portraitHeight\":1024,\"pixelsPerUnit\":104,\"pivotX\":0.5,\"pivotY\":0.08," +
                "\"manifest_sha256\":\"" + hash + "\",\"qc_sha256\":\"" + new string('a', 64) + "\",\"validation_sha256\":\"" + new string('b', 64) + "\"," +
                "\"sourceCommit\":\"9adcf9291e4a867601868889a5965f3cd48630ba\",\"sourceFamily\":\"original-00-22\"}";
            var appearance = QdaoCharacterCatalog.SelectOriginalHdAppearance(QdaoCharacterCatalog.Find(id), activation, bytes, (_, _, _) => true);
            Assert.That(appearance, Is.Not.Null);
            return appearance;
        }

        private static QdaoCharacterCatalog.Appearance V13Fallback(QdaoCharacterCatalog.Appearance rejected)
        {
            var folder = QdaoCharacterCatalog.OriginalV13Root + "/" + rejected.Id;
            var metadata = UnityEngine.Resources.Load<TextAsset>(folder + "/appearance");
            var manifest = UnityEngine.Resources.Load<TextAsset>(folder + "/manifest");
            return QdaoCharacterCatalog.SelectOriginalAppearance(QdaoCharacterCatalog.Find(rejected.Id), metadata?.text, manifest?.bytes,
                (path, w, h) => { var texture = UnityEngine.Resources.Load<Texture2D>(path); return texture != null && texture.width == w && texture.height == h; });
        }

        private static GameObject CameraObject()
        {
            var go = new GameObject("HdLifetimeCamera"); go.tag = "MainCamera";
            var camera = go.AddComponent<Camera>(); camera.orthographic = true;
            go.transform.position = new Vector3(0, 50, 0); go.transform.rotation = Quaternion.Euler(90, 0, 0);
            return go;
        }

        [UnityTest]
        public IEnumerator HdLoadsOneDirectionAndOneObservationWithoutChangingVisibleSprite()
        {
            using var resources = new ResourcesFixture();
            var baseline = QdaoHdResources.ResidentDirectionCount;
            var actor = new GameObject("HdBoundedInspectionActor");
            try
            {
                var appearance = Hd();
                var pending = resources.FrameSet(appearance);
                Assert.That(resources.Requested, Is.Empty, "Creating HD metadata must not load every direction or portrait.");
                var animator = actor.AddComponent<QdaoBoySpriteAnimator>();
                Assert.That((bool)Apply.Invoke(animator, new[] { pending }), Is.True);
                yield return null;
                Assert.That(resources.Live, Is.EqualTo(17));
                var renderer = actor.transform.Find("sprite").GetComponent<SpriteRenderer>();
                var standing = renderer.sprite;
                Assert.That(standing.rect.height, Is.EqualTo(1024));
                Assert.That(standing.pixelsPerUnit, Is.EqualTo(104));
                Assert.That(standing.bounds.size.y, Is.EqualTo(512f / 52f).Within(.0001f));
                Assert.That(standing.pivot.y / standing.rect.height, Is.EqualTo(.08f).Within(.00001f));
                for (var direction = 0; direction < 8; direction++)
                {
                    Assert.That(animator.EnsureDirectionFrames(direction), Is.True);
                    Assert.That(animator.Direction, Is.EqualTo(4));
                    Assert.That(renderer.sprite, Is.SameAs(standing));
                    Assert.That(standing.texture, Is.Not.Null);
                    var set = Frames.GetValue(animator);
                    var walk = (Sprite[][])set.GetType().GetField("Walk").GetValue(set);
                    Assert.That(walk[direction].Length, Is.EqualTo(16));
                    Assert.That(walk[direction].Select(s => s.texture).Distinct().Count(), Is.EqualTo(16));
                    Assert.That(animator.ResidentHdDirections, Is.LessThanOrEqualTo(2));
                    Assert.That(resources.Live, Is.LessThanOrEqualTo(34));
                    yield return null;
                }
                animator.ReleaseObservedDirection();
                Assert.That(resources.Live, Is.EqualTo(17));
                Assert.That(resources.Requested.Any(path => path.Contains("strip") || path.EndsWith("/portrait")), Is.False);
                Assert.That(resources.MaximumLive, Is.LessThanOrEqualTo(34));
            }
            finally { Object.DestroyImmediate(actor); }
            Assert.That(resources.Live, Is.Zero);
            Assert.That(QdaoHdResources.ResidentDirectionCount, Is.EqualTo(baseline));
            yield return null;
        }

        [UnityTest]
        public IEnumerator HdAllDirectionsKeepThirtyMillisecondCadenceAndStopOnIndependentIdle()
        {
            using var resources = new ResourcesFixture();
            var camera = CameraObject(); var actor = new GameObject("HdCadenceActor");
            var oldCapture = Time.captureFramerate; Time.captureFramerate = 60;
            try
            {
                var animator = resources.Attach(actor, Hd());
                var collider = actor.AddComponent<CapsuleCollider>(); collider.height = 3f; collider.radius = .75f;
                yield return null;
                var renderer = actor.transform.Find("sprite").GetComponent<SpriteRenderer>();
                for (var direction = 0; direction < 8; direction++)
                {
                    var radians = direction * 45f * Mathf.Deg2Rad;
                    var step = new Vector3(Mathf.Sin(radians), 0, Mathf.Cos(radians)) * (9f / 60f);
                    var poses = new HashSet<string>();
                    for (var frame = 0; frame < 32; frame++)
                    {
                        var before = (float)Clock.GetValue(animator) / 16f;
                        actor.transform.position += step;
                        yield return null;
                        Assert.That(animator.Direction, Is.EqualTo(direction));
                        Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Run));
                        Assert.That((float)Clock.GetValue(animator) / 16f,
                            Is.EqualTo(Mathf.Repeat(before + .15f / 4.32f, 1f)).Within(.0001f));
                        Assert.That(renderer.sprite.texture.width, Is.EqualTo(1024));
                        Assert.That(animator.ResidentHdDirections, Is.EqualTo(1));
                        poses.Add(renderer.sprite.name);
                    }
                    Assert.That(poses.Count, Is.EqualTo(16), Directions[direction]);
                }
                var stopped = actor.transform.position;
                yield return null;
                Assert.That(animator.State, Is.EqualTo(QdaoBoySpriteAnimator.LocomotionState.Idle));
                Assert.That(renderer.sprite.name, Does.Contain("_idle_NW_"));
                Assert.That(actor.transform.position, Is.EqualTo(stopped));
                Assert.That(collider.height, Is.EqualTo(3f)); Assert.That(collider.radius, Is.EqualTo(.75f));
            }
            finally { Time.captureFramerate = oldCapture; Object.DestroyImmediate(actor); Object.DestroyImmediate(camera); }
            Assert.That(resources.Live, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SwitchingAllTwentyThreeIdentitiesAndDirectionsDoesNotAccumulateInactiveHdFrames()
        {
            using var resources = new ResourcesFixture();
            var baseline = QdaoHdResources.ResidentDirectionCount;
            var actor = new GameObject("HdRosterSwitchActor");
            try
            {
                var animator = actor.AddComponent<QdaoBoySpriteAnimator>();
                foreach (var definition in QdaoCharacterCatalog.OriginalAll)
                {
                    var appearance = Hd(definition.Id);
                    Assert.That((bool)Apply.Invoke(animator, new[] { resources.FrameSet(appearance) }), Is.True);
                    Assert.That(animator.CharacterId, Is.EqualTo(definition.Id));
                    Assert.That(animator.EnsureDirectionFrames(2), Is.True);
                    Assert.That(animator.EnsureDirectionFrames(7), Is.True);
                    Assert.That(animator.ResidentHdDirections, Is.EqualTo(2));
                    Assert.That(QdaoHdResources.ResidentDirectionCount, Is.EqualTo(baseline + 2));
                    Assert.That(resources.Live, Is.EqualTo(34));
                    yield return null;
                }
                Assert.That(resources.MaximumLive, Is.LessThanOrEqualTo(34));
            }
            finally { Object.DestroyImmediate(actor); }
            Assert.That(QdaoHdResources.ResidentDirectionCount, Is.EqualTo(baseline));
            Assert.That(resources.Live, Is.Zero);
            yield return null;
        }

        [TestCase("walk/E/01", false)]
        [TestCase("walk/E/08", false)]
        [TestCase("walk/E/16", false)]
        [TestCase("idle/E", false)]
        [TestCase("walk/E/16", true)]
        public void PartialOrWrongSizeHdDirectionReleasesEveryAcquiredTexture(string missing, bool wrongSize)
        {
            using var resources = new ResourcesFixture(); var appearance = Hd();
            var baseline = QdaoHdResources.ResidentDirectionCount;
            (wrongSize ? resources.WrongSize : resources.Missing).Add(appearance.ResourceFolder + "/" + missing);
            Assert.That(resources.Lease(appearance, 2), Is.Null);
            Assert.That(resources.Live, Is.Zero);
            Assert.That(QdaoHdResources.ResidentDirectionCount, Is.EqualTo(baseline));
        }

        [UnityTest]
        public IEnumerator MissingHdDirectionFallsBackAtomicallyToSameIdV13()
        {
            using var resources = new ResourcesFixture(); var appearance = Hd();
            var actor = new GameObject("HdMissingDirectionActor");
            try
            {
                var animator = resources.Attach(actor, appearance);
                yield return null;
                resources.Missing.Add(appearance.FrameResourcePath("N", 15));
                LogAssert.Expect(LogType.Warning, new Regex("Incomplete V14 artwork: .*Retaining 03_lotus_healer_girl V13"));
                Assert.That(animator.EnsureDirectionFrames(0), Is.False);
                Assert.That(animator.CharacterId, Is.EqualTo(appearance.Id));
                Assert.That(animator.ArtworkVersion, Is.EqualTo(13));
                Assert.That(animator.ResidentHdDirections, Is.Zero);
                Assert.That(resources.Live, Is.Zero);
                var set = Frames.GetValue(animator);
                foreach (var row in (Sprite[][])set.GetType().GetField("Walk").GetValue(set))
                    foreach (var sprite in row) Assert.That(sprite.texture.width, Is.EqualTo(512));
                Assert.That(actor.transform.Find("sprite").GetComponent<SpriteRenderer>().sprite.texture.width, Is.EqualTo(512));
            }
            finally { Object.DestroyImmediate(actor); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator FailedFirstHdLoadWithoutFallbackPreservesTheExistingActorVisual()
        {
            using var resources = new ResourcesFixture(); var appearance = Hd();
            var actor = new GameObject("HdMissingInitialActor");
            try
            {
                Assert.That(QdaoBoySpriteAnimator.TryAttach(actor, appearance.Id), Is.True);
                yield return null;
                var animator = actor.GetComponent<QdaoBoySpriteAnimator>();
                var renderer = actor.transform.Find("sprite").GetComponent<SpriteRenderer>(); var original = renderer.sprite;
                resources.Missing.Add(appearance.IdleResourcePath("S"));
                LogAssert.Expect(LogType.Warning, new Regex("Missing artwork for 03_lotus_healer_girl: .*Keeping the actor's current visual"));
                Assert.That((bool)Apply.Invoke(animator, new[] { resources.FrameSet(appearance, false) }), Is.False);
                Assert.That(renderer.sprite, Is.SameAs(original));
                Assert.That(animator.CharacterId, Is.EqualTo(appearance.Id));
                Assert.That(resources.Live, Is.Zero);
            }
            finally { Object.DestroyImmediate(actor); }
            yield return null;
        }

        [Test]
        public void NewRevisionSharingTexturesCannotBeUnloadedByRetiringTheOldRevision()
        {
            using var resources = new ResourcesFixture();
            var first = resources.Lease(Hd(), 2); var next = resources.Lease(Hd(), 2);
            try
            {
                Assert.That(first.Walk[0].texture, Is.SameAs(next.Walk[0].texture));
                first.Dispose();
                Assert.That(resources.Released, Is.Zero);
                Assert.That(next.IsValid, Is.True);
                Assert.That(resources.Live, Is.EqualTo(17));
            }
            finally { first.Dispose(); next.Dispose(); }
            Assert.That(resources.Live, Is.Zero);
            Assert.That(resources.Released, Is.EqualTo(17));
        }

        [UnityTest]
        public IEnumerator RealBattleViewAndAfterimageKeepTheirSpritesUntilTheirOwnLastLeaseEnds()
        {
            using var resources = new ResourcesFixture(); var appearance = Hd();
            var baseline = QdaoHdResources.ResidentDirectionCount;
            var layerObject = new GameObject("HdBattleLeaseCanvas", typeof(RectTransform), typeof(Canvas));
            var layer = layerObject.GetComponent<RectTransform>();
            var view = new BattleUnitView(null, layer, 7UL, true, true, 0, null);
            var ghosts = new BattleAfterimagePool(layer);
            try
            {
                using var idle = resources.Battle(appearance, false);
                Assert.That(idle.Count, Is.EqualTo(1));
                Assert.That(idle.RequiresLease, Is.True);
                var sprite = idle.Frames[0];
                typeof(BattleUnitView).GetMethod("ApplyBodySprite", HiddenInstance).Invoke(view, new object[] { sprite, false });
                idle.Dispose();
                var body = view.Root.Find("Body").GetComponent<Image>();
                Assert.That(body.sprite, Is.SameAs(sprite));
                Assert.That(body.rectTransform.sizeDelta.y, Is.EqualTo(BattleUnitView.PlayerHeight));
                BattleArtCatalog.ResetCaches();
                Assert.That(sprite.texture, Is.Not.Null, "Resetting unrelated art caches cannot destroy active Image sprites.");
                ghosts.Spawn(sprite, Vector2.zero, new Vector2(230, 230), false, 0, 30f);
                var ghost = layer.GetComponentsInChildren<Image>().First(image => image.name == "Afterimage");
                view.Destroy();
                yield return null;
                Assert.That(ghost.sprite, Is.SameAs(sprite));
                Assert.That(ghost.sprite.texture.width, Is.EqualTo(1024));
                Assert.That(resources.Live, Is.EqualTo(17));
                ghosts.Clear();
                Assert.That(ghost.sprite, Is.Null);
                Assert.That(resources.Live, Is.Zero);
                Assert.That(QdaoHdResources.ResidentDirectionCount, Is.EqualTo(baseline));
            }
            finally { ghosts.Dispose(); view.Destroy(); Object.DestroyImmediate(layerObject); }
            yield return null;
        }

        [Test]
        public void BattleMissingHdFrameReturnsOnlySameIdentityLegacyFramesOrNothing()
        {
            using var resources = new ResourcesFixture(); var appearance = Hd();
            resources.Missing.Add(appearance.FrameResourcePath("E", 15));
            using var fallback = resources.Battle(appearance, true);
            Assert.That(fallback, Is.Not.Null);
            Assert.That(fallback.Count, Is.EqualTo(16));
            Assert.That(fallback.RequiresLease, Is.False);
            foreach (var frame in fallback.Frames)
            {
                Assert.That(frame.texture.width, Is.EqualTo(512));
                Assert.That(frame.name, Does.Contain(appearance.Id));
                Assert.That(frame.name, Does.Contain("QdaoOriginalRosterV13"));
            }
            Assert.That(resources.Battle(appearance, true, false), Is.Null);
            Assert.That(resources.Live, Is.Zero);
        }
    }
}
