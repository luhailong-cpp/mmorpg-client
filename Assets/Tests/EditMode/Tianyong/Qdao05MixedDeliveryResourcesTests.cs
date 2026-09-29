using System;
using System.Linq;
using System.Security.Cryptography;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>Run after the 05 mixed delivery has been imported into this client project.</summary>
    public sealed class Qdao05MixedDeliveryResourcesTests
    {
        private const string Id = "05_celestial_musician_girl";
        private const string Folder = QdaoCharacterCatalog.OriginalV14Root + "/" + Id;

        [Serializable]
        private sealed class Manifest
        {
            public string resolution_mode;
            public string preserved_snapshot_sha256;
            public FileRow[] files;
        }

        [Serializable]
        private sealed class FileRow
        {
            public string path, sha256, source_kind, source_sha256, preserved_sha256;
            public int width, height;
            public float pixels_per_unit;
            public float[] pivot;
            public int[] root_px, native_cell_size;
        }

        [SetUp]
        public void RefreshBeforeTest() => QdaoCharacterCatalog.RefreshAppearances();

        [TearDown]
        public void RefreshAfterTest() => QdaoCharacterCatalog.RefreshAppearances();

        [Test]
        public void DeliveryResolvesOwnCompleteMixedGeometry()
        {
            var manifestAsset = Resources.Load<TextAsset>(Folder + "/manifest");
            var activation = Resources.Load<TextAsset>(Folder + "/appearance");
            var index = Resources.Load<QdaoOriginalHdResourceIndex>(Folder + "/runtime-index");
            Assert.That(manifestAsset, Is.Not.Null, "05 mixed manifest has not been imported");
            Assert.That(activation, Is.Not.Null, "05 mixed appearance has not been imported");
            Assert.That(index, Is.Not.Null, "05 derived resource index has not been built");
            Assert.That(index.resolutionMode, Is.EqualTo(QdaoMixedResolutionContract.Mode));
            Assert.That(index.entries, Is.Not.Null);
            Assert.That(index.entries.Length, Is.EqualTo(137));
            Assert.That(QdaoOriginalHdResourceIndex.IsComplete(Folder, Hash(manifestAsset.bytes), Hash(activation.bytes)),
                Is.True, "05 index, validation, and current PNG inventory must agree");

            var manifest = JsonUtility.FromJson<Manifest>(manifestAsset.text);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.resolution_mode, Is.EqualTo(QdaoMixedResolutionContract.Mode));
            Assert.That(manifest.preserved_snapshot_sha256, Has.Length.EqualTo(64));
            Assert.That(manifest.files, Is.Not.Null);
            Assert.That(manifest.files.Length, Is.EqualTo(137));
            Assert.That(QdaoMixedResolutionContract.TryRead(Id, manifestAsset.bytes,
                QdaoMixedResolutionContract.Mode, out var parsedGeometry), Is.True);
            Assert.That(parsedGeometry.Count, Is.EqualTo(136));

            var appearance = QdaoCharacterCatalog.Find(Id)?.ResolveAppearance();
            Assert.That(appearance, Is.Not.Null, "05 must resolve through its own complete V14 resources");
            Assert.That(appearance.Id, Is.EqualTo(Id));
            Assert.That(appearance.ResourceFolder, Is.EqualTo(Folder));
            Assert.That(appearance.IsOriginalRoster && appearance.IsHd && appearance.IsMixedResolution, Is.True);
            Assert.That(appearance.Version, Is.EqualTo(14));
            Assert.That(appearance.FrameCount, Is.EqualTo(16));
            Assert.That(appearance.FrameDurationMs, Is.EqualTo(30));
            Assert.That(appearance.FrameCount / appearance.FramesPerSecond, Is.EqualTo(.48f).Within(.000001f));
            Assert.That(appearance.HasDedicatedIdle, Is.True);
            Assert.That(appearance.Pivot, Is.EqualTo(new Vector2(.5f, .08f)));

            var manifestRows = manifest.files.ToDictionary(row => row.path, StringComparer.Ordinal);
            var indexRows = index.entries.ToDictionary(row => row.path, StringComparer.Ordinal);
            Assert.That(manifestRows.Keys, Is.EquivalentTo(QdaoOriginalHdResourceIndex.RequiredRelativePaths()));
            Assert.That(indexRows.Keys, Is.EquivalentTo(manifestRows.Keys));
            Assert.That(manifestRows["portrait.png"].source_kind, Is.EqualTo("original-portrait"));
            Assert.That(manifestRows["portrait.png"].width, Is.EqualTo(1024));
            Assert.That(manifestRows["portrait.png"].height, Is.EqualTo(1024));

            var oldCount = 0;
            var nativeCount = 0;
            foreach (var row in manifest.files.Where(row => row.path != "portrait.png"))
            {
                var old = row.source_kind == "preserved-v13";
                Assert.That(old || row.source_kind == "native-hd", Is.True, row.path);
                if (old) oldCount++; else nativeCount++;
                var size = old ? 512 : 1024;
                var ppu = old ? 52f : 104f;
                Assert.That(row.width, Is.EqualTo(size), row.path);
                Assert.That(row.height, Is.EqualTo(size), row.path);
                Assert.That(row.pixels_per_unit, Is.EqualTo(ppu), row.path);
                Assert.That(row.pivot, Is.EqualTo(new[] { .5f, .08f }), row.path);
                Assert.That(row.root_px, Is.EqualTo(old ? new[] { 256, 471 } : new[] { 512, 942 }), row.path);
                Assert.That(row.native_cell_size, Has.Length.EqualTo(2), row.path);
                Assert.That(row.native_cell_size.All(value => value >= (old ? 1 : 1024)), Is.True, row.path);
                if (old) Assert.That(row.preserved_sha256, Is.EqualTo(row.sha256), row.path);

                var indexed = indexRows[row.path];
                Assert.That(indexed.width, Is.EqualTo(size), row.path);
                Assert.That(indexed.height, Is.EqualTo(size), row.path);
                Assert.That(indexed.pixelsPerUnit, Is.EqualTo(ppu), row.path);
                Assert.That(indexed.sha256, Is.EqualTo(row.sha256), row.path);

                var resource = Folder + "/" + row.path.Substring(0, row.path.Length - 4);
                var geometry = appearance.GeometryForResource(resource);
                Assert.That(geometry.Width, Is.EqualTo(size), row.path);
                Assert.That(geometry.Height, Is.EqualTo(size), row.path);
                Assert.That(geometry.PixelsPerUnit, Is.EqualTo(ppu), row.path);
                Assert.That(geometry.Pivot, Is.EqualTo(new Vector2(.5f, .08f)), row.path);
                Assert.That(geometry.Height / geometry.PixelsPerUnit,
                    Is.EqualTo(QdaoBoySpriteAnimator.FrameWorldHeight).Within(.00001f), row.path);
            }
            Assert.That(oldCount, Is.EqualTo(60));
            Assert.That(nativeCount, Is.EqualTo(76));
        }

        [Test]
        public void OnePreservedAndOneNativeWalkTextureLoadAtDeclaredSize()
        {
            var appearance = QdaoCharacterCatalog.Find(Id)?.ResolveAppearance();
            Assert.That(appearance, Is.Not.Null);
            Assert.That(appearance.Version, Is.EqualTo(14));
            Assert.That(appearance.IsMixedResolution, Is.True);
            AssertTexture(appearance, "walk/N/01.png", 512, 52f);
            AssertTexture(appearance, "walk/NE/02.png", 1024, 104f);
        }

        private static void AssertTexture(QdaoCharacterCatalog.Appearance appearance, string relative,
            int size, float pixelsPerUnit)
        {
            var resource = Folder + "/" + relative.Substring(0, relative.Length - 4);
            var geometry = appearance.GeometryForResource(resource);
            var texture = Resources.Load<Texture2D>(resource);
            try
            {
                Assert.That(texture, Is.Not.Null, resource);
                Assert.That(texture.width, Is.EqualTo(size), resource);
                Assert.That(texture.height, Is.EqualTo(size), resource);
                Assert.That(geometry.Width, Is.EqualTo(size), resource);
                Assert.That(geometry.Height, Is.EqualTo(size), resource);
                Assert.That(geometry.PixelsPerUnit, Is.EqualTo(pixelsPerUnit), resource);
                Assert.That(QdaoOriginalHdResourceIndex.TextureMatches(resource, size, size), Is.True, resource);
            }
            finally
            {
                if (texture != null) Resources.UnloadAsset(texture);
            }
        }

        private static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
