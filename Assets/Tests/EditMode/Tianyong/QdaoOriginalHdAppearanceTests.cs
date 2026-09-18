using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class QdaoOriginalHdAppearanceTests
    {
        private const string Id = "03_lotus_healer_girl";
        private static readonly string Sha = new string('a', 64);
        private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
        private static string Hash(string value)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Bytes(value))).Replace("-", "").ToLowerInvariant();
        }
        private static string Manifest(int version = 14)
            => "{\"version\":" + version + ",\"character_id\":\"" + Id + "\",\"status\":\"passed\",\"visual_review\":\"passed\"," +
               "\"frame_count\":16,\"frame_duration_ms\":30,\"cycle_duration_ms\":480,\"dedicated_idle\":true,\"contact_frame\":0," +
               "\"alignment\":{\"alignment_version\":2,\"root_px\":" + (version == 14 ? "[512,942]" : "[256,471]") + "}," +
               "\"frame_size\":[1024,1024],\"portrait_size\":[1024,1024],\"runtime_geometry\":{\"reference_frame_size\":512,\"pixels_per_unit\":104,\"pivot\":[0.5,0.08]}}";
        private static string Approval(string manifest, int version = 14)
            => "{\"version\":" + version + ",\"characterId\":\"" + Id + "\",\"status\":\"passed\",\"visualReview\":\"passed\"," +
               "\"frameCount\":16,\"frameDurationMs\":30,\"cycleDurationMs\":480,\"alignmentVersion\":2,\"dedicatedIdle\":true,\"contactFrame\":0," +
               "\"frameWidth\":1024,\"frameHeight\":1024,\"portraitWidth\":1024,\"portraitHeight\":1024,\"pixelsPerUnit\":104,\"pivotX\":0.5,\"pivotY\":0.08," +
               "\"manifest_sha256\":\"" + Hash(manifest) + "\",\"qc_sha256\":\"" + Sha + "\",\"validation_sha256\":\"" + Sha + "\"," +
               "\"sourceCommit\":\"9adcf9291e4a867601868889a5965f3cd48630ba\",\"sourceFamily\":\"original-00-22\"}";
        [TearDown] public void Clear() => QdaoCharacterCatalog.RefreshAppearances();

        [Test]
        public void CompleteHdHas137Separate1024ImagesAndPreservesWorldSizeCadenceAndPivot()
        {
            var calls = new List<string>();
            var manifest = Manifest();
            var selected = QdaoCharacterCatalog.SelectOriginalHdAppearance(QdaoCharacterCatalog.Find(Id), Approval(manifest), Bytes(manifest),
                (path, width, height) => { Assert.That(width, Is.EqualTo(1024)); Assert.That(height, Is.EqualTo(1024)); calls.Add(path); return true; });
            Assert.That(selected, Is.Not.Null);
            Assert.That(calls.Count, Is.EqualTo(137));
            Assert.That(calls.Distinct().Count(), Is.EqualTo(137));
            Assert.That(calls.Any(path => path.Contains("strip")), Is.False);
            Assert.That(selected.IsHd && selected.IsOriginalRoster, Is.True);
            Assert.That(selected.StripResourcePath("N"), Is.Null);
            Assert.That(selected.Version, Is.EqualTo(14));
            Assert.That(selected.FrameCount, Is.EqualTo(16));
            Assert.That(selected.FrameDurationMs, Is.EqualTo(30));
            Assert.That(selected.FrameWidth / selected.PixelsPerUnit, Is.EqualTo(512f / 52f).Within(.00001f));
            Assert.That(selected.Pivot, Is.EqualTo(new Vector2(.5f, .08f)));
            Assert.That(selected.FrameCount / selected.FramesPerSecond, Is.EqualTo(.48f).Within(.000001f));
        }

        [TestCase("\"frameWidth\":1024", "\"frameWidth\":512")]
        [TestCase("\"frameHeight\":1024", "\"frameHeight\":2048")]
        [TestCase("\"pixelsPerUnit\":104", "\"pixelsPerUnit\":52")]
        [TestCase("\"pivotY\":0.08", "\"pivotY\":0.1")]
        [TestCase("\"cycleDurationMs\":480", "\"cycleDurationMs\":960")]
        [TestCase("\"status\":\"passed\"", "\"status\":\"pending\"")]
        [TestCase("original-00-22", "other-family")]
        [TestCase("03_lotus_healer_girl", "24_lu_dongbin")]
        public void WrongHdActivationCannotBecomeAvailable(string from, string to)
        {
            var manifest = Manifest();
            Assert.That(QdaoCharacterCatalog.SelectOriginalHdAppearance(QdaoCharacterCatalog.Find(Id),
                Approval(manifest).Replace(from, to), Bytes(manifest), (_, _, _) => true), Is.Null);
        }

        [TestCase("[1024,1024]", "[512,512]")]
        [TestCase("[512,942]", "[512,943]")]
        [TestCase("\"reference_frame_size\":512", "\"reference_frame_size\":1024")]
        [TestCase("\"pixels_per_unit\":104", "\"pixels_per_unit\":52")]
        [TestCase("[0.5,0.08]", "[0.5,0.1]")]
        public void WrongHdManifestCannotBorrowAValidApproval(string from, string to)
        {
            var manifest = Manifest().Replace(from, to);
            Assert.That(QdaoCharacterCatalog.SelectOriginalHdAppearance(QdaoCharacterCatalog.Find(Id),
                Approval(manifest), Bytes(manifest), (_, _, _) => true), Is.Null);
        }

        [Test]
        public void EveryMissingHdImageFallsBackAtomicallyToSameIdentityV13AndNeverAnUnrelatedBody()
        {
            var definition = QdaoCharacterCatalog.Find(Id);
            var hd = Manifest(); var old = Manifest(13);
            string Metadata(string path) => path.EndsWith("/manifest") ? (path.Contains("V14/") ? hd : old) :
                path.Contains("V14/") ? Approval(hd) : Approval(old, 13);
            foreach (var relative in QdaoOriginalHdResourceIndex.RequiredRelativePaths())
            {
                QdaoCharacterCatalog.RefreshAppearances();
                var missing = QdaoCharacterCatalog.OriginalV14Root + "/" + Id + "/" + relative.Substring(0, relative.Length - 4);
                var selected = definition.ResolveAppearance(Metadata, (path, _, _) => path != missing);
                Assert.That(selected.Id, Is.EqualTo(Id), relative);
                Assert.That(selected.Version, Is.EqualTo(13), relative);
                Assert.That(selected.ResourceFolder, Does.StartWith(QdaoCharacterCatalog.OriginalV13Root), relative);
                QdaoCharacterCatalog.RefreshAppearances();
                Assert.That(definition.ResolveAppearance(Metadata, (path, _, _) => path.Contains("V14/") && path != missing), Is.Null, relative);
            }
        }

        [Test]
        public void MissingHdIndexDoesNotProbeTexturePixelsAndImportCompletionUpgradesFallback()
        {
            var definition = QdaoCharacterCatalog.Find(Id);
            var hd = Manifest(); var old = Manifest(13); var complete = false; var hdTextureProbes = 0;
            string Metadata(string path) => path.EndsWith("/manifest") ? (path.Contains("V14/") ? hd : old) :
                path.Contains("V14/") ? Approval(hd) : Approval(old, 13);
            bool Inventory(string path, int _, int __) { if (path.Contains("V14/")) hdTextureProbes++; return true; }
            var fallback = definition.ResolveAppearance(Metadata, Inventory, null, (_, _, _) => complete);
            Assert.That(fallback.Version, Is.EqualTo(13)); Assert.That(hdTextureProbes, Is.Zero);
            complete = true;
            var selected = definition.ResolveAppearance(Metadata, Inventory, null, (_, _, _) => complete);
            Assert.That(selected.Version, Is.EqualTo(14)); Assert.That(hdTextureProbes, Is.EqualTo(137));
            Assert.That(selected.CacheKey, Is.Not.EqualTo(fallback.CacheKey));
            hd += " ";
            // The metadata is recomputed in this fixture, so this is a new accepted revision and must invalidate the cache.
            var revision = definition.ResolveAppearance(Metadata, Inventory, null, (_, _, _) => complete);
            Assert.That(revision.CacheKey, Is.Not.EqualTo(selected.CacheKey));
        }

        [Test]
        public void ActualLoadRejectionKeepsAllConsumersOnSameIdV13UntilImportRefresh()
        {
            var definition = QdaoCharacterCatalog.Find(Id);
            var hd = Manifest(); var old = Manifest(13);
            string Metadata(string path) => path.EndsWith("/manifest") ? (path.Contains("V14/") ? hd : old) :
                path.Contains("V14/") ? Approval(hd) : Approval(old, 13);
            var selected = definition.ResolveAppearance(Metadata, (_, _, _) => true);
            Assert.That(selected.Version, Is.EqualTo(14));
            typeof(QdaoCharacterCatalog.Definition).GetMethod("RejectHdRevision",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(definition, new object[] { selected });
            Assert.That(definition.ResolveAppearance(Metadata, (_, _, _) => true).Version, Is.EqualTo(13));
            Assert.That(definition.ResolveAppearance(Metadata, (_, _, _) => true).Id, Is.EqualTo(Id));
            var attemptedLoads = 0;
            var lease = typeof(QdaoHdResources).GetMethod("AcquireWithResources", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new object[] { selected, 2, (Func<string, Texture2D>)(_ => { attemptedLoads++; return null; }),
                    (Action<Texture2D>)(_ => { }), true });
            Assert.That(lease, Is.Null);
            Assert.That(attemptedLoads, Is.Zero, "Other consumers cannot reacquire an already rejected HD revision.");
            QdaoCharacterCatalog.RefreshAppearances();
            Assert.That(definition.ResolveAppearance(Metadata, (_, _, _) => true).Version, Is.EqualTo(14));
        }

        [Test]
        public void OldActorLoadFailureCannotRejectANewerAcceptedHdRevision()
        {
            var definition = QdaoCharacterCatalog.Find(Id);
            var hd = Manifest(); var old = Manifest(13);
            string Metadata(string path) => path.EndsWith("/manifest") ? (path.Contains("V14/") ? hd : old) :
                path.Contains("V14/") ? Approval(hd) : Approval(old, 13);
            var prior = definition.ResolveAppearance(Metadata, (_, _, _) => true);
            hd += " ";
            var current = definition.ResolveAppearance(Metadata, (_, _, _) => true);
            Assert.That(current.CacheKey, Is.Not.EqualTo(prior.CacheKey));
            typeof(QdaoCharacterCatalog.Definition).GetMethod("RejectHdRevision",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(definition, new object[] { prior });
            Assert.That(definition.ResolveAppearance(Metadata, (_, _, _) => true), Is.SameAs(current));
            Assert.That(current.Version, Is.EqualTo(14));
        }

        [Test]
        public void DerivedIndexRejectsStaleRevisionDuplicateMissingAndChangedDimensionsWithoutTextureLoads()
        {
            var index = ScriptableObject.CreateInstance<QdaoOriginalHdResourceIndex>();
            try
            {
                var folder = QdaoCharacterCatalog.OriginalV14Root + "/" + Id;
                index.resourceFolder = folder; index.manifestSha256 = index.activationSha256 = index.validationSha256 = Sha;
                index.entries = QdaoOriginalHdResourceIndex.RequiredRelativePaths().Select((path, number) => new QdaoOriginalHdResourceIndex.Entry
                { path = path, width = 1024, height = 1024, assetGuid = (number + 1).ToString("x32"), sha256 = Sha, sourceBytes = 100 }).ToArray();
                var probes = 0;
                Assert.That(index.Validate(folder, Sha, Sha, _ => { probes++; return true; }), Is.True);
                Assert.That(probes, Is.EqualTo(137));
                Assert.That(index.Validate(folder, new string('c', 64), Sha, _ => true), Is.False);
                Assert.That(index.Validate(folder, Sha, Sha, entry => entry.path != "walk/NW/16.png"), Is.False);
                index.entries[1].width = 512;
                Assert.That(index.Validate(folder, Sha, Sha, _ => true), Is.False);
                index.entries[1].width = 1024;
                var secondGuid = index.entries[1].assetGuid;
                index.entries[1].assetGuid = index.entries[0].assetGuid;
                Assert.That(index.Validate(folder, Sha, Sha, _ => true), Is.False);
                index.entries[1].assetGuid = secondGuid;
                index.entries[1].path = index.entries[0].path;
                Assert.That(index.Validate(folder, Sha, Sha, _ => true), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(index); }
        }

        [Test]
        public void IndexHasNoTextureOrObjectReferenceFieldsAndMissingIndexNeverLoadsArtwork()
        {
            foreach (var field in typeof(QdaoOriginalHdResourceIndex.Entry).GetFields())
                Assert.That(typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType), Is.False, field.Name);
            Assert.That(QdaoOriginalHdResourceIndex.IsComplete(QdaoCharacterCatalog.OriginalV14Root + "/not-a-character", Sha, Sha), Is.False);
            Assert.That(QdaoOriginalHdResourceIndex.TextureMatches(QdaoCharacterCatalog.OriginalV14Root + "/not-a-character/walk/N/01", 1024, 1024), Is.False);
            Assert.That(QdaoOriginalHdResourceIndex.RequiredRelativePaths().Count(), Is.EqualTo(137));
        }
    }
}
