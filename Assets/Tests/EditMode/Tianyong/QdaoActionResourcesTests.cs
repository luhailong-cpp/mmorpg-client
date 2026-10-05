using System;
using System.Collections.Generic;
using System.Reflection;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class QdaoActionResourcesTests
    {
        private const string Id = "00_reference_topright_boy";
        private readonly List<Texture2D> _textures = new();
        private readonly List<QdaoActionResources.Lease> _leases = new();
        private static string Manifest(string clips) => "{\"schemaVersion\":1,\"characterId\":\"" + Id +
            "\",\"pixelsPerUnit\":104,\"pivotX\":0.5,\"pivotY\":0.080078125,\"clips\":[" + clips + "]}";
        private const string Run = "{\"action\":\"run\",\"direction\":\"N\",\"frameCount\":3,\"frameDurationMs\":75,\"eventFrame\":-1}";
        private const string Attack = "{\"action\":\"attack\",\"direction\":\"E\",\"frameCount\":3," +
            "\"frameDurationsMs\":[40,80,120],\"eventFrame\":1,\"overridePivot\":true,\"pivotX\":0.45,\"pivotY\":0.09}";

        private QdaoActionResources.Lease Acquire(string action, string direction, string json,
            Func<string, Texture2D> load, bool shared = false)
        {
            var result = (QdaoActionResources.Lease)typeof(QdaoActionResources).GetMethod("AcquireWithResources",
                BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { Id, action, direction, json, load, shared });
            if (result != null) _leases.Add(result);
            return result;
        }
        private Texture2D Texture(int size = 1024)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _textures.Add(texture);
            return texture;
        }
        [TearDown]
        public void Cleanup()
        {
            foreach (var lease in _leases) lease.Dispose();
            _leases.Clear();
            foreach (var texture in _textures) UnityEngine.Object.DestroyImmediate(texture);
            _textures.Clear();
        }

        [Test]
        public void LoadsOnlyTheRequestedAuthoredDirectionAndPreservesScaleAndCadence()
        {
            var paths = new List<string>();
            var lease = Acquire("run", "N", Manifest(Run + "," + Attack), path => { paths.Add(path); return Texture(); });
            Assert.That(lease, Is.Not.Null);
            Assert.That(paths, Is.EqualTo(new[] {
                QdaoActionResources.ResourceRoot + "/" + Id + "/run/N/01",
                QdaoActionResources.ResourceRoot + "/" + Id + "/run/N/02",
                QdaoActionResources.ResourceRoot + "/" + Id + "/run/N/03" }));
            Assert.That(lease.Fps, Is.EqualTo(1000f / 75f).Within(.0001f));
            Assert.That(lease.DurationSeconds, Is.EqualTo(.225f).Within(.00001f));
            Assert.That(lease.Frames[0].pixelsPerUnit, Is.EqualTo(104f));
            Assert.That(lease.Frames[0].pivot, Is.EqualTo(new Vector2(512f, 82f)));
            Assert.That(lease.FrameAt(.074f, true), Is.SameAs(lease.Frames[0]));
            Assert.That(lease.FrameAt(.076f, true), Is.SameAs(lease.Frames[1]));
            Assert.That(lease.FrameAt(.151f, true), Is.SameAs(lease.Frames[2]));
            Assert.That(lease.FrameAt(.226f, true), Is.SameAs(lease.Frames[0]));
            Assert.That(lease.FrameAt(10f, false), Is.SameAs(lease.Frames[2]));
            Assert.That(lease.EventFrame, Is.EqualTo(-1));
            Assert.That(lease.EventTimeSeconds, Is.EqualTo(-1f));
        }

        [Test]
        public void VariableTimingAndClipRootOverridePreserveAuthoredContactTime()
        {
            var lease = Acquire("attack", "E", Manifest(Attack), _ => Texture());
            Assert.That(lease, Is.Not.Null);
            Assert.That(lease.FrameDurationsSeconds, Is.EqualTo(new[] { .04f, .08f, .12f }));
            Assert.That(lease.DurationSeconds, Is.EqualTo(.24f).Within(.00001f));
            Assert.That(lease.EventTimeSeconds, Is.EqualTo(.04f).Within(.00001f));
            Assert.That(lease.Pivot, Is.EqualTo(new Vector2(.45f, .09f)));
            Assert.That(lease.FrameAt(.039f, false), Is.SameAs(lease.Frames[0]));
            Assert.That(lease.FrameAt(.041f, false), Is.SameAs(lease.Frames[1]));
            Assert.That(lease.FrameAt(.121f, false), Is.SameAs(lease.Frames[2]));
        }

        [Test]
        public void SharedActorAndAfterimageLeasesKeepSpritesAliveUntilTheLastRelease()
        {
            var before = QdaoActionResources.ResidentClipCount;
            var loaded = 0;
            Texture2D Load(string _) { loaded++; return Texture(); }
            var first = Acquire("run", "N", Manifest(Run), Load, true);
            var second = Acquire("run", "N", Manifest(Run), Load, true);
            var sprite = first.Frames[0];
            var texture = sprite.texture;
            var afterimage = QdaoActionResources.Retain(sprite);
            _leases.Add(afterimage);
            Assert.That(loaded, Is.EqualTo(3));
            Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(before + 1));
            first.Dispose(); second.Dispose();
            Assert.That(sprite != null && afterimage.IsValid, Is.True);
            afterimage.Dispose(); afterimage.Dispose();
            Assert.That(sprite == null, Is.True);
            Assert.That(texture != null, Is.True, "A separately held Resources texture must never be forcibly unloaded.");
            Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(before));
            Assert.That(QdaoActionResources.Retain(sprite), Is.Null);
        }

        [Test]
        public void MissingFrameFailsTheWholeClipAndDoesNotDestroyBorrowedTextures()
        {
            var before = QdaoActionResources.ResidentClipCount;
            var texture = Texture();
            var calls = 0;
            var lease = Acquire("run", "N", Manifest(Run), _ => ++calls == 1 ? texture : null, true);
            Assert.That(lease, Is.Null);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(texture != null, Is.True);
            Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(before));
        }

        [Test]
        public void MissingWestNeverMirrorsEastAndUnknownActionsDoNotLoadTextures()
        {
            var calls = 0;
            Texture2D Load(string _) { calls++; return Texture(); }
            Assert.That(Acquire("attack", "W", Manifest(Attack), Load), Is.Null);
            Assert.That(Acquire("attack", "N", Manifest(Attack), Load), Is.Null);
            Assert.That(Acquire("../run", "N", Manifest(Run), Load), Is.Null);
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void OwnedTexturesSharedAcrossClipsAreReleasedOnlyAfterBothClipsAndAfterimages()
        {
            var textures = new[] { Texture(), Texture(), Texture() };
            var released = new List<Texture2D>();
            var method = typeof(QdaoActionResources).GetMethod("AcquireWithOwnedResources", BindingFlags.NonPublic | BindingFlags.Static);
            QdaoActionResources.Lease Owned(string action, string direction)
            {
                var lease = (QdaoActionResources.Lease)method.Invoke(null, new object[] { Id, action, direction,
                    Manifest(Run + "," + Attack), (Func<string, Texture2D>)(path => textures[int.Parse(path.Substring(path.Length - 2)) - 1]),
                    (Action<Texture2D>)(texture => released.Add(texture)), false });
                _leases.Add(lease);
                return lease;
            }
            var run = Owned("run", "N");
            var attack = Owned("attack", "E");
            var afterimage = QdaoActionResources.Retain(run.Frames[0]);
            _leases.Add(afterimage);
            run.Dispose(); attack.Dispose();
            Assert.That(released, Is.Empty);
            Assert.That(afterimage.IsValid, Is.True);
            afterimage.Dispose();
            Assert.That(released, Is.EquivalentTo(textures));
            Assert.That(released.Count, Is.EqualTo(3));
        }

        [Test]
        public void FailedOwnedClipReleasesItsPartialTextureAcquisitions()
        {
            var texture = Texture();
            var released = new List<Texture2D>();
            var method = typeof(QdaoActionResources).GetMethod("AcquireWithOwnedResources", BindingFlags.NonPublic | BindingFlags.Static);
            var result = method.Invoke(null, new object[] { Id, "run", "N", Manifest(Run),
                (Func<string, Texture2D>)(path => path.EndsWith("/01") ? texture : null),
                (Action<Texture2D>)(value => released.Add(value)), false });
            Assert.That(result, Is.Null);
            Assert.That(released, Is.EqualTo(new[] { texture }));
        }

        [TestCase("\"frameDurationMs\":75", "\"frameDurationMs\":0")]
        [TestCase("\"eventFrame\":-1", "\"eventFrame\":3")]
        [TestCase("\"schemaVersion\":1", "\"schemaVersion\":2")]
        [TestCase("\"pixelsPerUnit\":104", "\"pixelsPerUnit\":0")]
        [TestCase("00_reference_topright_boy", "01_ice_sword_girl")]
        public void InvalidMetadataIsRejectedBeforeTextureLoads(string from, string to)
        {
            var loads = 0;
            Assert.That(Acquire("run", "N", Manifest(Run).Replace(from, to), _ => { loads++; return Texture(); }), Is.Null);
            Assert.That(loads, Is.Zero);
        }
    }
}
