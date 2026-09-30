using System;
using System.Linq;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>Checks the imported 04 mixed snapshot; does not grant artwork approval.</summary>
    public sealed class Qdao04MixedDeliveryResourcesTests
    {
        private const string Id = "04_mountain_guardian_boy";
        private const string Folder = QdaoCharacterCatalog.OriginalV14Root + "/" + Id;

        [Serializable]
        private sealed class Manifest
        {
            public bool formal_approval;
            public string source_snapshot_status;
            public FileRow[] files;
        }

        [Serializable]
        private sealed class FileRow
        {
            public string path, source_kind;
            public int width, height;
            public float pixels_per_unit;
        }

        [SetUp]
        public void RefreshBeforeTest() => QdaoCharacterCatalog.RefreshAppearances();

        [TearDown]
        public void RefreshAfterTest() => QdaoCharacterCatalog.RefreshAppearances();

        [Test]
        public void GuardianResolvesCompleteOwnIdentityWith112PreservedAnd24NativeActions()
        {
            var appearance = QdaoCharacterCatalog.Find(Id)?.ResolveAppearance();
            Assert.That(appearance, Is.Not.Null, "04 must have a complete generated resource index");
            Assert.That(appearance.Id, Is.EqualTo(Id));
            Assert.That(appearance.ResourceFolder, Is.EqualTo(Folder));
            Assert.That(appearance.IsHd && appearance.IsMixedResolution, Is.True);
            Assert.That(appearance.FrameCount, Is.EqualTo(16));
            Assert.That(appearance.FrameDurationMs, Is.EqualTo(30));
            Assert.That(appearance.HasDedicatedIdle, Is.True);
            var text = Resources.Load<TextAsset>(Folder + "/manifest");
            Assert.That(text, Is.Not.Null);
            var manifest = JsonUtility.FromJson<Manifest>(text.text);
            Assert.That(manifest.formal_approval, Is.False);
            Assert.That(manifest.source_snapshot_status, Is.EqualTo("inventory_complete_visual_pending"));
            Assert.That(manifest.files.Select(row => row.path),
                Is.EquivalentTo(QdaoOriginalHdResourceIndex.RequiredRelativePaths()));
            Assert.That(manifest.files.Count(row => row.source_kind == "preserved-v13"), Is.EqualTo(112));
            Assert.That(manifest.files.Count(row => row.source_kind == "native-hd"), Is.EqualTo(24));
            foreach (var row in manifest.files.Where(row => row.path != "portrait.png"))
            {
                var resource = Folder + "/" + row.path.Substring(0, row.path.Length - 4);
                var geometry = appearance.GeometryForResource(resource);
                Assert.That(geometry.Width, Is.EqualTo(row.width), row.path);
                Assert.That(geometry.Height, Is.EqualTo(row.height), row.path);
                Assert.That(geometry.PixelsPerUnit, Is.EqualTo(row.pixels_per_unit), row.path);
                Assert.That(geometry.Height / geometry.PixelsPerUnit,
                    Is.EqualTo(QdaoBoySpriteAnimator.FrameWorldHeight).Within(.00001f), row.path);
            }
            Assert.That(QdaoCharacterCatalog.AvailableAll.Any(row => row.Id == Id), Is.True);
            Assert.That(QdaoCharacterCatalog.LoadPortrait(Id), Is.Not.Null);
        }

        [Test]
        public void SouthwestLoadsPreservedAndNativeFramesAtEqualWorldScale()
        {
            var appearance = QdaoCharacterCatalog.Find(Id)?.ResolveAppearance();
            Assert.That(appearance, Is.Not.Null);
            using var lease = QdaoHdResources.Acquire(appearance, 5);
            Assert.That(lease, Is.Not.Null);
            Assert.That(lease.IsValid, Is.True);
            Assert.That(lease.Walk.Length, Is.EqualTo(16));
            Assert.That(lease.Walk.Any(sprite => sprite.texture.width == 512), Is.True);
            Assert.That(lease.Walk.Any(sprite => sprite.texture.width == 1024), Is.True);
            Assert.That(lease.Idle, Is.Not.Null);
            foreach (var sprite in lease.Walk.Append(lease.Idle))
            {
                Assert.That(sprite.rect.height / sprite.pixelsPerUnit,
                    Is.EqualTo(QdaoBoySpriteAnimator.FrameWorldHeight).Within(.00001f));
                Assert.That(sprite.pivot.y / sprite.rect.height, Is.EqualTo(.08f).Within(.00001f));
            }
        }
    }
}
