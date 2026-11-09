using System;
using System.Collections.Generic;
using System.Reflection;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class QdaoRunVariantAnimatorTests
    {
        private const string Id = "00_reference_topright_boy";
        private const BindingFlags InstancePrivate = BindingFlags.NonPublic | BindingFlags.Instance;
        private readonly List<Texture2D> _textures = new();
        private GameObject _actor;
        private QdaoBoySpriteAnimator _animator;
        private Sprite _standing;
        private SpriteRenderer _renderer;
        private readonly HashSet<string> _missing = new();

        private static FieldInfo Field(string name) => typeof(QdaoBoySpriteAnimator).GetField(name, InstancePrivate);
        private void Set(string name, object value) => Field(name).SetValue(_animator, value);
        private T Get<T>(string name) => (T)Field(name).GetValue(_animator);

        private Texture2D Texture()
        {
            var texture = new Texture2D(16, 16);
            _textures.Add(texture);
            return texture;
        }

        private QdaoActionResources.Lease Clip(string root, string direction, string action = "run", string id = Id)
        {
            if (_missing.Contains(root + "/" + direction)) return null;
            var count = root == QdaoRunVariants.CurrentRoot ? 8 : 16;
            var milliseconds = root == QdaoRunVariants.CurrentRoot ? 250 : 62.5;
            var json = "{\"schemaVersion\":1,\"characterId\":\"" + id +
                "\",\"pixelsPerUnit\":52,\"pivotX\":0.5,\"pivotY\":0.08,\"frameWidth\":16,\"frameHeight\":16," +
                "\"clips\":[{\"action\":\"" + action + "\",\"direction\":\"" + direction +
                "\",\"frameCount\":" + count + ",\"frameDurationMs\":" +
                milliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"eventFrame\":-1}]}";
            var acquire = typeof(QdaoActionResources).GetMethod("AcquireFromRootWithResources", BindingFlags.NonPublic | BindingFlags.Static);
            return (QdaoActionResources.Lease)acquire.Invoke(null, new object[] {
                id, action, direction, json, (Func<string, Texture2D>)(_ => Texture()), null, false, root });
        }

        [SetUp]
        public void Setup()
        {
            _actor = new GameObject("run-variant-test-actor");
            _actor.transform.position = new UnityEngine.Vector3(10, 2, -3);
            _animator = _actor.AddComponent<QdaoBoySpriteAnimator>();
            typeof(QdaoBoySpriteAnimator).GetMethod("InitializeVisuals", InstancePrivate).Invoke(_animator, null);
            _standing = Sprite.Create(Texture(), new Rect(0, 0, 16, 16), new Vector2(.5f, .08f), 52);
            var frameType = typeof(QdaoBoySpriteAnimator).GetNestedType("FrameSet", BindingFlags.NonPublic);
            var frames = Activator.CreateInstance(frameType, true);
            void Value(string name, object value) => frameType.GetField(name).SetValue(frames, value);
            Value("Id", Id); Value("Count", 8); Value("Fps", 8f);
            Value("Version", 13); Value("DedicatedIdle", true); Value("UseArchivedActions", true);
            var walk = new Sprite[8][];
            var idle = new Sprite[8];
            for (var direction = 0; direction < 8; direction++)
            {
                walk[direction] = new Sprite[8];
                for (var frame = 0; frame < 8; frame++) walk[direction][frame] = _standing;
                idle[direction] = _standing;
            }
            Value("Walk", walk); Value("Idle", idle);
            Assert.That(ApplyFramesForTest(frames), Is.True);
            Set("_animationClock", 3f);
            Set("<State>k__BackingField", QdaoBoySpriteAnimator.LocomotionState.Run);
            Set("RunClipLoader", (Func<string, string, string, QdaoActionResources.Lease>)((id, direction, root) => Clip(root, direction)));
            _renderer = Get<SpriteRenderer>("_renderer");
            _renderer.sprite = _standing;
        }

        [TearDown]
        public void Cleanup()
        {
            if (_actor != null) Object.DestroyImmediate(_actor);
            if (_standing != null) Object.DestroyImmediate(_standing);
            foreach (var texture in _textures) if (texture != null) Object.DestroyImmediate(texture);
            _textures.Clear();
            _missing.Clear();
        }

        [Test]
        public void MissingCurrentDirectionDoesNotDisplayBaseWalk()
        {
            _missing.Add(QdaoRunVariants.CurrentRoot + "/E");
            Set("_lastDirection", 2);
            _renderer.sprite = _standing; // 应用覆盖渲染前的原始姿态。
            typeof(QdaoBoySpriteAnimator).GetMethod("RenderArchivedActions", InstancePrivate)
                .Invoke(_animator, new object[] { true, true, 0f, 0f });
            Assert.That(_renderer.sprite, Is.Null);
            Assert.That(Get<QdaoActionResources.Lease>("_archivedRun"), Is.Null);
        }

        // 创建独立 FrameSet，以实际外观切换路径验证默认版本和资源隔离。
        private object FramesForIdentity(string identity)
        {
            var frameType = typeof(QdaoBoySpriteAnimator).GetNestedType("FrameSet", BindingFlags.NonPublic);
            var source = Get<object>("_frames");
            var next = Activator.CreateInstance(frameType, true);
            foreach (var name in new[] { "Count", "Fps", "Version", "DedicatedIdle", "UseArchivedActions", "Walk", "Idle" })
            {
                var field = frameType.GetField(name);
                field.SetValue(next, field.GetValue(source));
            }
            frameType.GetField("Id").SetValue(next, identity);
            return next;
        }

        private bool ApplyFramesForTest(object frames)
            => (bool)typeof(QdaoBoySpriteAnimator).GetMethod("ApplyFrames", InstancePrivate)
                .Invoke(_animator, new[] { frames });

        [Test]
        public void FirstRetainedAppearanceSelectsCurrentWithoutLoadingUntilRunning()
        {
            var next = FramesForIdentity(Id);
            Set("_frames", null);
            Assert.That(ApplyFramesForTest(next), Is.True);
            Assert.That(Get<QdaoActionResources.Lease>("_archivedRun"), Is.Null);
            Assert.That(_renderer.sprite, Is.SameAs(_standing));
        }

        [Test]
        public void PreviouslyStoredAppearanceKeepsItsOwnArchivedRun()
        {
            const string existingId = "07_moon_shadow_assassin_girl";
            Assert.That(ApplyFramesForTest(FramesForIdentity(existingId)), Is.True);
            string loadedId = null, loadedRoot = null;
            Set("RunClipLoader", (Func<string, string, string, QdaoActionResources.Lease>)((id, direction, root) =>
            {
                loadedId = id;
                loadedRoot = root;
                return Clip(root, direction, id: id);
            }));
            typeof(QdaoBoySpriteAnimator).GetMethod("RenderArchivedActions", InstancePrivate)
                .Invoke(_animator, new object[] { true, false, .1f, 0f });
            Assert.That(_animator.CharacterId, Is.EqualTo(existingId));
            Assert.That(loadedId, Is.EqualTo(existingId));
            Assert.That(loadedRoot, Is.EqualTo(QdaoActionResources.ResourceRoot));
            Assert.That(_renderer.sprite, Is.Not.Null.And.Not.SameAs(_standing));
        }

        [Test]
        public void DefaultCurrentLoadsTheActorsOwnIdentityAndRootWhileRunning()
        {
            var next = FramesForIdentity(Id);
            Set("_frames", null);
            Assert.That(ApplyFramesForTest(next), Is.True);
            string loadedId = null, loadedDirection = null, loadedRoot = null;
            Set("RunClipLoader", (Func<string, string, string, QdaoActionResources.Lease>)((id, direction, root) =>
            {
                loadedId = id; loadedDirection = direction; loadedRoot = root;
                return Clip(root, direction);
            }));
            typeof(QdaoBoySpriteAnimator).GetMethod("RenderArchivedActions", InstancePrivate)
                .Invoke(_animator, new object[] { true, false, .1f, 0f });
            Assert.That(loadedId, Is.EqualTo(Id));
            Assert.That(loadedDirection, Is.EqualTo("S"));
            Assert.That(loadedRoot, Is.EqualTo(QdaoRunVariants.CurrentRoot));
            Assert.That(_animator.FrameCount, Is.EqualTo(8));
            Assert.That(_renderer.sprite, Is.Not.Null.And.Not.SameAs(_standing));
        }

        [Test]
        public void DisableEnableCallbacksPreserveCurrentAndReacquireItsRunClip()
        {
            var next = FramesForIdentity(Id);
            Set("_frames", null);
            Assert.That(ApplyFramesForTest(next), Is.True);
            var render = typeof(QdaoBoySpriteAnimator).GetMethod("RenderArchivedActions", InstancePrivate);
            render.Invoke(_animator, new object[] { true, false, .1f, 0f });
            var first = Get<QdaoActionResources.Lease>("_archivedRun");
            Assert.That(first, Is.Not.Null);
            Assert.That(first.IsValid, Is.True);
            typeof(QdaoBoySpriteAnimator).GetMethod("OnDisable", InstancePrivate).Invoke(_animator, null);
            Assert.That(first.IsValid, Is.False);
            Assert.That(Get<QdaoActionResources.Lease>("_archivedRun"), Is.Null);
            typeof(QdaoBoySpriteAnimator).GetMethod("OnEnable", InstancePrivate).Invoke(_animator, null);
            render.Invoke(_animator, new object[] { true, false, .1f, 0f });
            var replacement = Get<QdaoActionResources.Lease>("_archivedRun");
            Assert.That(replacement, Is.Not.Null);
            Assert.That(replacement.IsValid, Is.True);
            Assert.That(replacement, Is.Not.SameAs(first));
            Assert.That(_renderer.sprite, Is.SameAs(replacement.FrameAt(Get<float>("_archivedRunSeconds"), true)));
        }

        [Test]
        public void RealAttachmentsDriveAndLoadEveryDirectionForTheApprovedRoster()
        {
            // 此固定清单对应已批准的11人；未来批准扩展角色时须同步更新。
            var expectedIds = new HashSet<string>
            {
                "00_reference_topright_boy", "01_ice_sword_girl", "02_fire_talisman_boy",
                "03_lotus_healer_girl", "04_mountain_guardian_boy", "05_celestial_musician_girl",
                "08_alchemy_prodigy_boy", "09_bamboo_archer_girl",
                "10_crimson_spear_girl", "15_water_dragon_scholar_boy", "17_ghost_script_calligrapher_boy"
            };
            var directions = new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            var observedIds = new HashSet<string>();
            var render = typeof(QdaoBoySpriteAnimator).GetMethod("RenderArchivedActions", InstancePrivate);
            foreach (var definition in QdaoCharacterCatalog.OriginalAll)
            {
                if (!QdaoRunVariants.AllowsCharacter(definition.Id)) continue;
                Assert.That(expectedIds.Contains(definition.Id), Is.True, "未批准的当前动作角色：" + definition.Id);
                Assert.That(observedIds.Add(definition.Id), Is.True, "角色身份重复：" + definition.Id);
                var actor = new GameObject("real-current-attachment");
                try
                {
                    Assert.That(QdaoBoySpriteAnimator.TryAttach(actor, definition.Id), Is.True, definition.Id);
                    var animator = actor.GetComponent<QdaoBoySpriteAnimator>();
                    Assert.That(animator, Is.Not.Null, definition.Id);
                    Assert.That(animator.CharacterId, Is.EqualTo(definition.Id));
                    var renderer = (SpriteRenderer)Field("_renderer").GetValue(animator);
                    for (var directionIndex = 0; directionIndex < directions.Length; directionIndex++)
                    {
                        var direction = directions[directionIndex];
                        // 调用真实动画器渲染路径，验证方向变化后持有当前版租约及该帧。
                        Field("_lastDirection").SetValue(animator, directionIndex);
                        render.Invoke(animator, new object[] { true, true, 0f, 0f });
                        var active = (QdaoActionResources.Lease)Field("_archivedRun").GetValue(animator);
                        Assert.That(active, Is.Not.Null, definition.Id + "/" + direction);
                        Assert.That(active.IsValid, Is.True, definition.Id + "/" + direction);
                        var activeSeconds = (float)Field("_archivedRunSeconds").GetValue(animator);
                        Assert.That(renderer.sprite, Is.SameAs(active.FrameAt(activeSeconds, true)), definition.Id + "/" + direction);
                        var firstTexture = Resources.Load<Texture2D>(
                            QdaoRunVariants.CurrentRoot + "/" + definition.Id + "/run/" + direction + "/01");
                        Assert.That(firstTexture, Is.Not.Null, definition.Id + "/" + direction);
                        Assert.That(active.Frames[0].texture, Is.SameAs(firstTexture), definition.Id + "/" + direction);
                        // 单独获取资源租约，验证实际 manifest 的帧数、逐帧时长和播放顺序。
                        using (var clip = QdaoActionResources.Acquire(definition.Id, "run", direction, QdaoRunVariants.CurrentRoot))
                        {
                            Assert.That(clip, Is.Not.Null, definition.Id + "/" + direction);
                            Assert.That(clip.IsValid, Is.True, definition.Id + "/" + direction);
                            Assert.That(clip.Frames.Length, Is.EqualTo(8));
                            Assert.That(clip.FrameDurationsSeconds.Length, Is.EqualTo(8));
                            for (var frame = 0; frame < 8; frame++)
                            {
                                Assert.That(clip.FrameDurationsSeconds[frame], Is.EqualTo(0.13333333333333333f).Within(0.0001f));
                                Assert.That(clip.FrameAt((frame + .5f) * clip.DurationSeconds / 8f, true),
                                    Is.SameAs(clip.Frames[frame]), definition.Id + "/" + direction + "/" + frame);
                            }
                        }
                    }
                }
                finally { Object.DestroyImmediate(actor); }
            }
            CollectionAssert.AreEquivalent(expectedIds, observedIds);
        }
    }
}
