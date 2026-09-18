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
    public sealed class QdaoMixedResolutionAppearanceTests
    {
        private const string Id = "04_mountain_guardian_boy";
        private static readonly string Sha = new string('a', 64);
        private static string Hash(string value)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
        private static string Manifest(string id = Id, int version = 14)
        {
            var rows = QdaoOriginalHdResourceIndex.RequiredRelativePaths().Select((path, index) =>
            {
                if (path == "portrait.png") return "{\"path\":\"portrait.png\",\"width\":1024,\"height\":1024,\"source_kind\":\"original-portrait\",\"sha256\":\"" + Sha + "\",\"source_sha256\":\"" + Sha + "\"}";
                var old = index % 2 == 1; var size = old ? 512 : 1024;
                return "{\"path\":\"" + path + "\",\"width\":" + size + ",\"height\":" + size +
                    ",\"pixels_per_unit\":" + (old ? 52 : 104) + ",\"pivot\":[0.5,0.08],\"root_px\":" + (old ? "[256,471]" : "[512,942]") +
                    ",\"native_cell_size\":[1254,1254],\"source_kind\":\"" + (old ? "preserved-v13" : "native-hd") +
                    "\",\"sha256\":\"" + Sha + "\",\"source_sha256\":\"" + Sha + "\",\"preserved_sha256\":\"" + Sha + "\"}";
            });
            return "{\"version\":" + version + ",\"character_id\":\"" + id + "\",\"status\":\"passed\",\"visual_review\":\"passed\",\"frame_count\":16," +
                "\"frame_duration_ms\":30,\"cycle_duration_ms\":480,\"dedicated_idle\":true,\"contact_frame\":0,\"alignment\":{\"alignment_version\":2,\"root_px\":" +
                (version == 14 ? "[512,942]" : "[256,471]") + "},\"frame_size\":[1024,1024],\"portrait_size\":[1024,1024],\"runtime_geometry\":{\"reference_frame_size\":512,\"pixels_per_unit\":104,\"pivot\":[0.5,0.08]}" +
                (version == 14 ? ",\"resolution_mode\":\"mixed-preserved-v1\",\"preserved_snapshot_sha256\":\"" + Sha + "\",\"files\":[" + string.Join(",", rows) + "]" : "") + "}";
        }
        private static string Approval(string manifest, string id = Id, int version = 14)
            => "{\"version\":" + version + ",\"characterId\":\"" + id + "\",\"status\":\"passed\",\"visualReview\":\"passed\",\"frameCount\":16," +
                "\"frameDurationMs\":30,\"cycleDurationMs\":480,\"alignmentVersion\":2,\"dedicatedIdle\":true,\"contactFrame\":0,\"frameWidth\":1024,\"frameHeight\":1024," +
                "\"portraitWidth\":1024,\"portraitHeight\":1024,\"pixelsPerUnit\":104,\"pivotX\":0.5,\"pivotY\":0.08,\"manifest_sha256\":\"" + Hash(manifest) +
                "\",\"qc_sha256\":\"" + Sha + "\",\"validation_sha256\":\"" + Sha + "\",\"sourceCommit\":\"9adcf9291e4a867601868889a5965f3cd48630ba\",\"sourceFamily\":\"original-00-22\"" +
                (version == 14 ? ",\"resolutionMode\":\"mixed-preserved-v1\"" : "") + "}";
        private static QdaoCharacterCatalog.Appearance Select(string manifest, string approval = null, string id = Id)
            => QdaoCharacterCatalog.SelectOriginalHdAppearance(QdaoCharacterCatalog.Find(id), approval ?? Approval(manifest, id), Encoding.UTF8.GetBytes(manifest), (_, _, _) => true);
        [TearDown] public void Clear() => QdaoCharacterCatalog.RefreshAppearances();

        [Test]
        public void ExplicitMixedContractUses137DeclaredDimensionsAndEqualWorldGeometry()
        {
            var manifest = Manifest(); var requests = new Dictionary<string, int>();
            var appearance = QdaoCharacterCatalog.SelectOriginalHdAppearance(QdaoCharacterCatalog.Find(Id), Approval(manifest), Encoding.UTF8.GetBytes(manifest),
                (path, width, height) => { Assert.That(height, Is.EqualTo(width)); requests.Add(path, width); return true; });
            Assert.That(appearance, Is.Not.Null); Assert.That(appearance.IsMixedResolution, Is.True);
            Assert.That(requests.Count, Is.EqualTo(137)); Assert.That(requests.Values.Distinct().OrderBy(x => x), Is.EqualTo(new[] { 512, 1024 }));
            foreach (var request in requests.Where(x => !x.Key.EndsWith("/portrait")))
            {
                var geometry = appearance.GeometryForResource(request.Key);
                Assert.That(geometry.Width, Is.EqualTo(request.Value));
                Assert.That(geometry.Height / geometry.PixelsPerUnit, Is.EqualTo(512f / 52f).Within(.00001f));
                Assert.That(geometry.Pivot, Is.EqualTo(new Vector2(.5f, .08f)));
            }
            Assert.That(appearance.FrameCount, Is.EqualTo(16)); Assert.That(appearance.FrameDurationMs, Is.EqualTo(30));
            Assert.Throws<ArgumentException>(() => appearance.GeometryForResource(appearance.ResourceFolder + "/walk/N/17"));
        }

        [TestCase("\"pixels_per_unit\":52", "\"pixels_per_unit\":104")]
        [TestCase("[256,471]", "[256,472]")]
        [TestCase("[1254,1254]", "[627,1254]")]
        [TestCase("preserved-v13", "native-hd")]
        [TestCase("\"preserved_sha256\":", "\"unbound_preserved_sha256\":")]
        [TestCase("\"source_sha256\":", "\"unbound_source_sha256\":")]
        [TestCase("\"preserved_snapshot_sha256\":", "\"unbound_snapshot\":")]
        [TestCase("walk/NW/16.png", "walk/NW/15.png")]
        [TestCase("[0.5,0.08]", "[0.5,0.09]")]
        [TestCase("\"visual_review\":\"passed\"", "\"visual_review\":\"pending\"")]
        public void MalformedMixedMetadataCannotBecomeAvailable(string from, string to)
            => Assert.That(Select(Manifest().Replace(from, to)), Is.Null);

        [Test]
        public void MixedModeCannotBeInferredFromDimensionsOrBorrowAnotherIdentity()
        {
            var manifest = Manifest();
            Assert.That(Select(manifest, Approval(manifest).Replace("mixed-preserved-v1", "")), Is.Null);
            var unknown = manifest.Replace("mixed-preserved-v1", "anything-else");
            Assert.That(Select(unknown, Approval(unknown).Replace("mixed-preserved-v1", "anything-else")), Is.Null);
            foreach (var definition in QdaoCharacterCatalog.OriginalAll.Where(x => !QdaoMixedResolutionContract.AllowedIdentity(x.Id)))
                Assert.That(Select(Manifest(definition.Id), null, definition.Id), Is.Null, definition.Id);
        }

        [Test]
        public void EveryMissingMixedImageFallsBackOnlyToCompleteSameIdentityV13AndRetriesImport()
        {
            var mixed = Manifest(); var old = Manifest(Id, 13); var definition = QdaoCharacterCatalog.Find(Id);
            string Metadata(string path) => path.EndsWith("/manifest") ? (path.Contains("V14/") ? mixed : old) :
                path.Contains("V14/") ? Approval(mixed) : Approval(old, Id, 13);
            foreach (var relative in QdaoOriginalHdResourceIndex.RequiredRelativePaths())
            {
                QdaoCharacterCatalog.RefreshAppearances();
                var missing = QdaoCharacterCatalog.OriginalV14Root + "/" + Id + "/" + relative.Substring(0, relative.Length - 4);
                var fallback = definition.ResolveAppearance(Metadata, (path, _, _) => path != missing);
                Assert.That(fallback.Id, Is.EqualTo(Id)); Assert.That(fallback.Version, Is.EqualTo(13));
                Assert.That(definition.ResolveAppearance(Metadata, (_, _, _) => true).IsMixedResolution, Is.True);
            }
        }

        [Test]
        public void MixedIndexRequiresExplicitModeBothSizesAndMatchingPerFramePpu()
        {
            var index = ScriptableObject.CreateInstance<QdaoOriginalHdResourceIndex>();
            try
            {
                var appearance = Select(Manifest()); var folder = appearance.ResourceFolder;
                index.resourceFolder = folder; index.manifestSha256 = index.activationSha256 = index.validationSha256 = Sha;
                index.resolutionMode = QdaoMixedResolutionContract.Mode;
                index.entries = QdaoOriginalHdResourceIndex.RequiredRelativePaths().Select((path, number) =>
                {
                    var geometry = path == "portrait.png" ? new QdaoMixedResolutionContract.Geometry(1024, 100) : appearance.GeometryForResource(folder + "/" + path.Substring(0, path.Length - 4));
                    return new QdaoOriginalHdResourceIndex.Entry { path = path, width = geometry.Width, height = geometry.Height, pixelsPerUnit = geometry.PixelsPerUnit,
                        assetGuid = (number + 1).ToString("x32"), sha256 = Sha, sourceBytes = 100 };
                }).ToArray();
                Assert.That(index.Validate(folder, Sha, Sha, _ => true), Is.True);
                index.resolutionMode = null; Assert.That(index.Validate(folder, Sha, Sha, _ => true), Is.False);
                index.resolutionMode = QdaoMixedResolutionContract.Mode; index.entries[1].pixelsPerUnit = 104;
                Assert.That(index.Validate(folder, Sha, Sha, _ => true), Is.False);
                index.entries[1].pixelsPerUnit = 52; index.entries[1].width = index.entries[1].height = 1024;
                Assert.That(index.Validate(folder, Sha, Sha, _ => true), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(index); }
        }
    }
}
