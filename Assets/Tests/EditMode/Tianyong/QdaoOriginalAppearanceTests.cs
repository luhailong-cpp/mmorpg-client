using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using MmorpgClient.World;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class QdaoOriginalAppearanceTests
    {
        private const string Character = "01_ice_sword_girl";
        private static readonly string[] Directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        private static string Manifest(string id = Character)
            => "{\"version\":13,\"character_id\":\"" + id + "\",\"status\":\"passed\",\"visual_review\":\"passed\"," +
               "\"frame_count\":16,\"frame_duration_ms\":30,\"cycle_duration_ms\":480,\"dedicated_idle\":true,\"contact_frame\":0," +
               "\"alignment\":{\"alignment_version\":2,\"root_px\":[256,471]}}";
        private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
        private static string Approval(string manifest, string id = Character)
        {
            using var sha = SHA256.Create();
            var digest = BitConverter.ToString(sha.ComputeHash(Bytes(manifest))).Replace("-", "").ToLowerInvariant();
            return "{\"version\":13,\"characterId\":\"" + id + "\",\"alignmentVersion\":2,\"frameCount\":16,\"frameDurationMs\":30," +
                   "\"dedicatedIdle\":true,\"contactFrame\":0,\"status\":\"passed\",\"visualReview\":\"passed\"," +
                   "\"manifest_sha256\":\"" + digest + "\",\"qc_sha256\":\"" + new string('b', 64) +
                   "\",\"validation_sha256\":\"" + new string('c', 64) + "\"}";
        }
        private static QdaoCharacterCatalog.Appearance Select(string manifest = null, string approval = null,
            Func<string, int, int, bool> inventory = null)
        {
            manifest ??= Manifest();
            return QdaoCharacterCatalog.SelectOriginalAppearance(QdaoCharacterCatalog.Find(Character),
                approval ?? Approval(manifest), Bytes(manifest), inventory ?? ((_, _, _) => true));
        }

        [TearDown]
        public void ClearOriginalTestResolvers() => QdaoCharacterCatalog.RefreshAppearances();

        [Test]
        public void OriginalRegistryKeepsTwentyThreeIndependentIdsAndDoesNotRewriteExistingRosterOrLegacy()
        {
            Assert.That(QdaoCharacterCatalog.All.Count, Is.EqualTo(8));
            Assert.That(QdaoCharacterCatalog.OriginalAll.Count, Is.EqualTo(23));
            var ids = new HashSet<string>();
            for (var index = 0; index < QdaoCharacterCatalog.OriginalAll.Count; index++)
            {
                var entry = QdaoCharacterCatalog.OriginalAll[index];
                Assert.That(entry.Id, Does.StartWith(index.ToString("00") + "_"));
                Assert.That(ids.Add(entry.Id), Is.True);
                Assert.That(entry.IsOriginalRoster, Is.True);
                Assert.That(entry.BaselineAppearance, Is.Null, "Original IDs cannot inherit a different character's V11 body.");
                Assert.That(QdaoCharacterCatalog.Find(entry.Id), Is.SameAs(entry));
            }
            Assert.That(QdaoCharacterCatalog.OriginalAll[0].Id, Is.EqualTo("00_reference_topright_boy"));
            Assert.That(QdaoCharacterCatalog.OriginalAll[22].Id, Is.EqualTo("22_lidazui_hair_waiter_saber_boy"));
            Assert.That(QdaoCharacterCatalog.Find("q_daoist_topright_character_full_uncropped"), Is.Null);
            Assert.That(QdaoCharacterCatalog.DefaultId, Is.EqualTo("24_lu_dongbin"));
            Assert.That(QdaoCharacterCatalog.LegacyId, Is.EqualTo("QdaoHeadbandBoy"));
            Assert.That(QdaoCharacterCatalog.ResolveRole(1, 1), Is.EqualTo("24_lu_dongbin"));
            Assert.That(QdaoBoySpriteAnimator.FramesPerDirection, Is.EqualTo(8));
        }

        [Test]
        public void CompleteOriginalV13Loads137RuntimeImagesFromItsOwnRootWithThirtyMillisecondV2Contract()
        {
            var paths = new HashSet<string>();
            var selected = Select(inventory: (path, width, height) =>
            {
                Assert.That(path, Does.StartWith(QdaoCharacterCatalog.OriginalV13Root + "/" + Character + "/"));
                Assert.That(width, Is.EqualTo(path.EndsWith("/portrait") ? 1024 : 512));
                Assert.That(height, Is.EqualTo(width));
                Assert.That(paths.Add(path), Is.True);
                return true;
            });
            Assert.That(paths.Count, Is.EqualTo(137));
            Assert.That(selected.Id, Is.EqualTo(Character));
            Assert.That(selected.Version, Is.EqualTo(13));
            Assert.That(selected.AlignmentVersion, Is.EqualTo(2));
            Assert.That(selected.FrameCount, Is.EqualTo(16));
            Assert.That(selected.FrameDurationMs, Is.EqualTo(30));
            Assert.That(selected.HasDedicatedIdle, Is.True);
            Assert.That(selected.IsOriginalRoster, Is.True);
            Assert.That(selected.FrameCount / selected.FramesPerSecond, Is.EqualTo(.48f).Within(.000001f));
            Assert.That(selected.FrameCount / (selected.FramesPerSecond / 9f), Is.EqualTo(4.32f).Within(.00001f));
        }

        [Test]
        public void EachMissingOriginalFrameIdleOrPortraitPreventsActivationWithoutSubstitutingAnotherIdentity()
        {
            var required = new List<string> { "portrait" };
            foreach (var direction in Directions)
            {
                required.Add("idle/" + direction);
                for (var frame = 1; frame <= 16; frame++) required.Add($"walk/{direction}/{frame:00}");
            }
            foreach (var missing in required)
                Assert.That(Select(inventory: (path, _, _) => path != QdaoCharacterCatalog.OriginalV13Root + "/" + Character + "/" + missing),
                    Is.Null, missing);
            Assert.That(QdaoCharacterCatalog.SelectFallbackAppearance(Select(), null, (_, _, _) => true), Is.Null);
        }

        [TestCase("\"version\":13", "\"version\":12")]
        [TestCase("\"alignmentVersion\":2", "\"alignmentVersion\":3")]
        [TestCase("\"alignmentVersion\":2", "\"alignmentVersion\":0")]
        [TestCase("\"frameCount\":16", "\"frameCount\":8")]
        [TestCase("\"frameDurationMs\":30", "\"frameDurationMs\":60")]
        [TestCase("\"dedicatedIdle\":true", "\"dedicatedIdle\":false")]
        [TestCase("\"contactFrame\":0", "\"contactFrame\":8")]
        [TestCase("\"status\":\"passed\"", "\"status\":\"incomplete\"")]
        [TestCase("\"visualReview\":\"passed\"", "\"visualReview\":\"pending\"")]
        [TestCase(Character, "24_lu_dongbin")]
        public void OriginalApprovalCannotBorrowLuIdentityOrUseAnUnapprovedContract(string from, string to)
            => Assert.That(Select(approval: Approval(Manifest()).Replace(from, to)), Is.Null);

        [TestCase("\"version\":13", "\"version\":12")]
        [TestCase("\"alignment_version\":2", "\"alignment_version\":3")]
        [TestCase("\"root_px\":[256,471]", "\"root_px\":[256,470]")]
        [TestCase("\"frame_count\":16", "\"frame_count\":8")]
        [TestCase("\"frame_duration_ms\":30", "\"frame_duration_ms\":60")]
        [TestCase("\"cycle_duration_ms\":480", "\"cycle_duration_ms\":960")]
        [TestCase("\"status\":\"passed\"", "\"status\":\"passed_numeric_pending_visual\"")]
        [TestCase("\"visual_review\":\"passed\"", "\"visual_review\":\"pending\"")]
        [TestCase(Character, "02_fire_talisman_boy")]
        public void OriginalManifestMustAgreeWithTheApprovedFinalIdentityTimingAndAlignment(string from, string to)
            => Assert.That(Select(Manifest().Replace(from, to)), Is.Null);

        [Test]
        public void OriginalManifestMissingMalformedOrChangedAfterApprovalCannotActivate()
        {
            var definition = QdaoCharacterCatalog.Find(Character);
            Assert.That(QdaoCharacterCatalog.SelectOriginalAppearance(definition, Approval(Manifest()), null, (_, _, _) => true), Is.Null);
            Assert.That(Select("{broken"), Is.Null);
            Assert.That(Select(Manifest() + " ", Approval(Manifest())), Is.Null, "Hash must bind exact manifest bytes.");
            Assert.That(QdaoCharacterCatalog.SelectAppearance(definition, Approval(Manifest()), null, (_, _, _) => true), Is.Null);
        }

        [Test]
        public void OriginalV2DoesNotRelaxTheSeparateLuV13FixedHeadV3Requirement()
        {
            var lu = QdaoCharacterCatalog.Find("24_lu_dongbin");
            var approval = Approval(Manifest(), lu.Id);
            Assert.That(QdaoCharacterCatalog.SelectAppearance(lu, approval, null, (_, _, _) => true).Version, Is.EqualTo(11));
            Assert.That(Select().AlignmentVersion, Is.EqualTo(2));
            Assert.That(QdaoCharacterCatalog.SelectOriginalAppearance(lu, approval, Bytes(Manifest()), (_, _, _) => true), Is.Null);
        }

        [Test]
        public void OriginalImportRetriesAndManifestByteChangesInvalidateTheCachedApproval()
        {
            var definition = QdaoCharacterCatalog.Find(Character);
            var manifest = Manifest();
            var activation = Approval(manifest);
            var complete = false;
            string Metadata(string path) => path.StartsWith(QdaoCharacterCatalog.OriginalV14Root + "/") ? null :
                path.EndsWith("/manifest") ? manifest : activation;
            bool Inventory(string path, int _, int __) => complete || !path.EndsWith("walk/NW/16");
            Assert.That(definition.ResolveAppearance(Metadata, Inventory), Is.Null);
            complete = true;
            var approved = definition.ResolveAppearance(Metadata, Inventory);
            Assert.That(approved, Is.Not.Null);
            Assert.That(definition.ResolveAppearance(Metadata, Inventory), Is.SameAs(approved));
            manifest += " ";
            Assert.That(definition.ResolveAppearance(Metadata, Inventory), Is.Null);
            activation = Approval(manifest);
            var revised = definition.ResolveAppearance(Metadata, Inventory);
            Assert.That(revised, Is.Not.Null);
            Assert.That(revised.CacheKey, Is.Not.EqualTo(approved.CacheKey));
        }

        [Test]
        public void PlayableSelectionAddsOnlyApprovedOriginalIdsAndKeepsTheExistingEight()
        {
            var original = Select();
            var available = QdaoCharacterCatalog.SelectAvailableAppearances(entry => entry.Id == Character ? original : null);
            Assert.That(available.Count, Is.EqualTo(9));
            for (var index = 0; index < 8; index++) Assert.That(available[index], Is.SameAs(QdaoCharacterCatalog.All[index]));
            Assert.That(available[8].Id, Is.EqualTo(Character));
            Assert.That(QdaoCharacterCatalog.SelectAvailableAppearances(_ => null).Count, Is.EqualTo(8));
            Assert.That(QdaoCharacterCatalog.SelectAvailableAppearances(_ => QdaoCharacterCatalog.All[0].BaselineAppearance).Count, Is.EqualTo(8));
        }
    }
}
