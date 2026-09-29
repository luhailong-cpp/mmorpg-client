using System;
using System.Security.Cryptography;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>Run after importing the seven accepted full-HD original deliveries.</summary>
    public sealed class QdaoDeliveredOriginalV14ResourcesTests
    {
        private static readonly string[] Directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        [SetUp]
        public void RefreshBeforeTest() => QdaoCharacterCatalog.RefreshAppearances();

        [TearDown]
        public void RefreshAfterTest() => QdaoCharacterCatalog.RefreshAppearances();

        [TestCase("06_thunder_caster_boy")]
        [TestCase("08_alchemy_prodigy_boy")]
        [TestCase("09_bamboo_archer_girl")]
        [TestCase("10_crimson_spear_girl")]
        [TestCase("14_short_hair_snow_summoner_girl")]
        [TestCase("17_ghost_script_calligrapher_boy")]
        [TestCase("20_star_formation_master_girl")]
        public void AcceptedDeliveryResolvesOwnCompleteHdResources(string id)
        {
            var folder = QdaoCharacterCatalog.OriginalV14Root + "/" + id;
            var manifest = Resources.Load<TextAsset>(folder + "/manifest");
            var activation = Resources.Load<TextAsset>(folder + "/appearance");
            var index = Resources.Load<QdaoOriginalHdResourceIndex>(folder + "/runtime-index");
            Assert.That(manifest, Is.Not.Null, id + " manifest");
            Assert.That(activation, Is.Not.Null, id + " appearance");
            Assert.That(index, Is.Not.Null, id + " derived index");
            Assert.That(index.entries, Is.Not.Null);
            Assert.That(index.entries.Length, Is.EqualTo(137), id + " indexed PNG inventory");
            Assert.That(QdaoOriginalHdResourceIndex.IsComplete(folder, Hash(manifest.bytes), Hash(activation.bytes)),
                Is.True, id + " index and validation must bind the current resource bytes");

            var appearance = QdaoCharacterCatalog.Find(id)?.ResolveAppearance();
            Assert.That(appearance, Is.Not.Null, id + " did not become available");
            Assert.That(appearance.Id, Is.EqualTo(id));
            Assert.That(appearance.ResourceFolder, Is.EqualTo(folder));
            Assert.That(appearance.IsOriginalRoster && appearance.IsHd && !appearance.IsMixedResolution, Is.True);
            Assert.That(appearance.Version, Is.EqualTo(14));
            Assert.That(appearance.FrameCount, Is.EqualTo(16));
            Assert.That(appearance.FrameDurationMs, Is.EqualTo(30));
            Assert.That(appearance.FrameCount / appearance.FramesPerSecond, Is.EqualTo(.48f).Within(.000001f));
            Assert.That(appearance.HasDedicatedIdle, Is.True);
            Assert.That(appearance.FrameWidth, Is.EqualTo(1024));
            Assert.That(appearance.FrameHeight, Is.EqualTo(1024));
            Assert.That(appearance.PixelsPerUnit, Is.EqualTo(104f));
            Assert.That(appearance.FrameHeight / appearance.PixelsPerUnit,
                Is.EqualTo(512f / 52f).Within(.00001f));
            Assert.That(appearance.FrameHeight / appearance.PixelsPerUnit,
                Is.EqualTo(QdaoBoySpriteAnimator.FrameWorldHeight).Within(.00001f));
            Assert.That(appearance.Pivot, Is.EqualTo(new Vector2(.5f, .08f)));

            // One real texture at a time checks all eight directions without retaining 952 textures.
            foreach (var direction in Directions)
            {
                var path = appearance.FrameResourcePath(direction, 0);
                var texture = Resources.Load<Texture2D>(path);
                try
                {
                    Assert.That(texture, Is.Not.Null, path);
                    Assert.That(texture.width, Is.EqualTo(1024), path);
                    Assert.That(texture.height, Is.EqualTo(1024), path);
                }
                finally
                {
                    if (texture != null) Resources.UnloadAsset(texture);
                }
            }
        }

        private static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
