using System;
using System.Collections.Generic;
using System.Reflection;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class QdaoRunVariantResourceTests
    {
        private const string Id = "00_reference_topright_boy";
        private readonly List<Texture2D> _textures = new();
        private readonly List<QdaoActionResources.Lease> _leases = new();

        private static string Manifest(int frames, string milliseconds, string direction = "N", string action = "run", string id = Id)
            => "{\"schemaVersion\":1,\"characterId\":\"" + id +
               "\",\"pixelsPerUnit\":104,\"pivotX\":0.5,\"pivotY\":0.08,\"frameWidth\":16,\"frameHeight\":16," +
               "\"clips\":[{\"action\":\"" + action + "\",\"direction\":\"" + direction +
               "\",\"frameCount\":" + frames + ",\"frameDurationMs\":" + milliseconds + ",\"eventFrame\":-1}]}";

        private Texture2D Texture()
        {
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            _textures.Add(texture);
            return texture;
        }

        private QdaoActionResources.Lease Acquire(string root, string json, Func<string, Texture2D> load,
            Action<Texture2D> unload = null, string direction = "N", string action = "run", string id = Id)
        {
            var method = typeof(QdaoActionResources).GetMethod("AcquireFromRootWithResources", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            var result = (QdaoActionResources.Lease)method.Invoke(null, new object[] { id, action, direction, json, load, unload, true, root });
            if (result != null) _leases.Add(result);
            return result;
        }

        [TearDown]
        public void Cleanup()
        {
            foreach (var lease in _leases) lease.Dispose();
            _leases.Clear();
            foreach (var texture in _textures)
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            _textures.Clear();
        }

        [Test]
        public void RootsIsolateSharedClipsAndLastLeaseReleasesOnlyItsOwnTextures()
        {
            var beforeClips = QdaoActionResources.ResidentClipCount;
            var beforeTextures = QdaoActionResources.ResidentTextureCount;
            var paths = new List<string>();
            var released = new List<Texture2D>();
            Texture2D Load(string path) { paths.Add(path); return Texture(); }
            var legacy = Acquire(QdaoRunVariants.LegacyRoot, Manifest(16, "66.6666667"), Load, released.Add);
            var current = Acquire(QdaoRunVariants.CurrentRoot, Manifest(8, "133.3333333"), Load, released.Add);
            var currentAgain = Acquire(QdaoRunVariants.CurrentRoot, Manifest(8, "133.3333333"), Load, released.Add);
            Assert.That(legacy, Is.Not.Null);
            Assert.That(current, Is.Not.Null);
            Assert.That(currentAgain.Frames[0], Is.SameAs(current.Frames[0]));
            Assert.That(legacy.Frames[0], Is.Not.SameAs(current.Frames[0]));
            Assert.That(paths.Count, Is.EqualTo(24));
            Assert.That(paths[0], Is.EqualTo(QdaoRunVariants.LegacyRoot + "/" + Id + "/run/N/01"));
            Assert.That(paths[16], Is.EqualTo(QdaoRunVariants.CurrentRoot + "/" + Id + "/run/N/01"));
            Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(beforeClips + 2));
            Assert.That(QdaoActionResources.ResidentTextureCount, Is.EqualTo(beforeTextures + 24));
            var legacyAfterimage = QdaoActionResources.Retain(legacy.Frames[0]);
            _leases.Add(legacyAfterimage);
            legacy.Dispose();
            Assert.That(released, Is.Empty);
            legacyAfterimage.Dispose();
            Assert.That(released.Count, Is.EqualTo(16));
            Assert.That(current.IsValid, Is.True);
            current.Dispose();
            Assert.That(released.Count, Is.EqualTo(16));
            currentAgain.Dispose();
            Assert.That(released.Count, Is.EqualTo(24));
            Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(beforeClips));
            Assert.That(QdaoActionResources.ResidentTextureCount, Is.EqualTo(beforeTextures));
        }

        [Test]
        public void EightAndSixteenDistinctPosesKeepTheirAuthoredCycleAndNormalizedPhase()
        {
            var legacy = Acquire(QdaoRunVariants.LegacyRoot, Manifest(16, "66.6666667"), _ => Texture());
            var current = Acquire(QdaoRunVariants.CurrentRoot, Manifest(8, "133.3333333"), _ => Texture());
            Assert.That(legacy.Frames.Length, Is.EqualTo(16));
            Assert.That(current.Frames.Length, Is.EqualTo(8));
            Assert.That(legacy.DurationSeconds, Is.EqualTo(16f / 15f).Within(.00001f));
            Assert.That(current.DurationSeconds, Is.EqualTo(legacy.DurationSeconds).Within(.00001f));
            var legacyTime = legacy.DurationSeconds * .34375f;
            var currentTime = QdaoRunVariants.RemapSeconds(legacyTime, legacy.DurationSeconds, current.DurationSeconds);
            Assert.That(legacy.FrameAt(legacyTime, true), Is.SameAs(legacy.Frames[5]));
            Assert.That(current.FrameAt(currentTime, true), Is.SameAs(current.Frames[2]));
            Assert.That(QdaoRunVariants.RemapSeconds(.75f, 1f, 2f), Is.EqualTo(1.5f));
            Assert.That(QdaoRunVariants.RemapSeconds(2.75f, 1f, 2f), Is.EqualTo(1.5f));
        }

        [Test]
        public void MissingCurrentDirectionOrTextureNeverBorrowsCachedLegacyClip()
        {
            var legacy = Acquire(QdaoRunVariants.LegacyRoot, Manifest(16, "60"), _ => Texture());
            var before = QdaoActionResources.ResidentClipCount;
            var paths = new List<string>();
            Texture2D Missing(string path) { paths.Add(path); return null; }
            Assert.That(Acquire(QdaoRunVariants.CurrentRoot, Manifest(8, "120", "S"), Missing), Is.Null);
            Assert.That(paths, Is.Empty);
            Assert.That(Acquire(QdaoRunVariants.CurrentRoot, Manifest(8, "120"), Missing), Is.Null);
            Assert.That(paths, Is.EqualTo(new[] { QdaoRunVariants.CurrentRoot + "/" + Id + "/run/N/01" }));
            Assert.That(legacy.IsValid, Is.True);
            Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(before));
        }

        [Test]
        public void CurrentRootRejectsTheSixteenFrameDevelopmentSnapshot()
        {
            var calls = 0;
            Assert.That(Acquire(QdaoRunVariants.CurrentRoot, Manifest(16, "60"), _ => { calls++; return Texture(); }), Is.Null);
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void ComparisonRootsRejectOutsidePathsRemovedCharactersAndNonRunActions()
        {
            var calls = 0;
            Texture2D Load(string _) { calls++; return Texture(); }
            Assert.That(Acquire(QdaoRunVariants.CurrentRoot + "/../legacy", Manifest(8, "120"), Load), Is.Null);
            Assert.That(Acquire("World/Characters/unapproved", Manifest(8, "120"), Load), Is.Null);
            Assert.That(Acquire(QdaoRunVariants.CurrentRoot, Manifest(8, "120", id: "07_moon_shadow_assassin_girl"), Load,
                id: "07_moon_shadow_assassin_girl"), Is.Null);
            Assert.That(Acquire(QdaoRunVariants.CurrentRoot, Manifest(8, "120", "E", "attack"), Load, direction: "E", action: "attack"), Is.Null);
            Assert.That(calls, Is.Zero);
            // 原动作目录保留现有攻击入口，双版本限制不改变战斗资源。
            Assert.That(Acquire(QdaoActionResources.ResourceRoot, Manifest(3, "40", "E", "attack"), Load,
                direction: "E", action: "attack"), Is.Not.Null);
            Assert.That(calls, Is.EqualTo(3));
        }

        [TestCase("00_reference_topright_boy", true)]
        [TestCase("17_ghost_script_calligrapher_boy", true)]
        [TestCase("07_moon_shadow_assassin_girl", false)]
        [TestCase("14_short_hair_snow_summoner_girl", false)]
        [TestCase("20_star_formation_master_girl", false)]
        [TestCase("unknown", false)]
        [TestCase(null, false)]
        public void OnlyRetainedIdentitiesCanUseComparisonRoots(string id, bool expected)
            => Assert.That(QdaoRunVariants.AllowsCharacter(id), Is.EqualTo(expected));

        [Test]
        public void InvalidPhaseInputsDoNotProduceNonFiniteTimesOrFallbackRoots()
        {
            foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
            {
                Assert.That(QdaoRunVariants.RemapSeconds(value, 1f, 2f), Is.Zero);
                Assert.That(QdaoRunVariants.RemapSeconds(.5f, value, 2f), Is.Zero);
                Assert.That(QdaoRunVariants.RemapSeconds(.5f, 1f, value), Is.Zero);
            }
            Assert.That(QdaoRunVariants.RemapSeconds(.5f, 0f, 2f), Is.Zero);
            Assert.That(QdaoRunVariants.RemapSeconds(.5f, 1f, 0f), Is.Zero);
            Assert.That(QdaoRunVariants.RemapSeconds(0f, 1f, 2f), Is.Zero);
            Assert.That(QdaoRunVariants.RemapSeconds(1f, 1f, 2f), Is.Zero);
            var extreme = QdaoRunVariants.RemapSeconds(float.MaxValue, float.Epsilon, float.MaxValue);
            Assert.That(float.IsNaN(extreme) || float.IsInfinity(extreme), Is.False);
            Assert.That(extreme, Is.GreaterThanOrEqualTo(0f).And.LessThan(float.MaxValue));
            Assert.That(QdaoRunVariants.RootFor(QdaoRunVariant.Original), Is.EqualTo(QdaoActionResources.ResourceRoot));
            Assert.That(QdaoRunVariants.RootFor(QdaoRunVariant.Legacy), Is.EqualTo(QdaoRunVariants.LegacyRoot));
            Assert.That(QdaoRunVariants.RootFor(QdaoRunVariant.Current), Is.EqualTo(QdaoRunVariants.CurrentRoot));
            Assert.That(QdaoRunVariants.RootFor((QdaoRunVariant)999), Is.Null);
        }
    }
}
