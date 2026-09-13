using System.Collections.Generic;
using MmorpgClient.World;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class QdaoAppearanceVersionTests
    {
        private const string Character = "24_lu_dongbin";
        private const string V12 = "World/Characters/QdaoRosterV12/" + Character + "/";
        private static readonly string[] Directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        private static string Approval(string id = Character, int contact = 0, string revision = null)
        {
            var sha = revision ?? new string('a', 64);
            return "{\"version\":12,\"characterId\":\"" + id + "\",\"frameCount\":8,\"frameDurationMs\":60," +
                   "\"dedicatedIdle\":true,\"contactFrame\":" + contact + ",\"status\":\"passed\",\"visualReview\":\"passed\"," +
                   "\"manifest_sha256\":\"" + sha + "\",\"qc_sha256\":\"" + new string('b', 64) +
                   "\",\"validation_sha256\":\"" + new string('c', 64) + "\"}";
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("{broken")]
        public void MissingOrMalformedUpgrade_KeepsTheSameFourPoseCharacter(string json)
        {
            var definition = QdaoCharacterCatalog.Find(Character);
            var calls = 0;
            var selected = QdaoCharacterCatalog.SelectAppearance(definition, json, (_, _, _) => { calls++; return true; });
            Assert.That(selected, Is.SameAs(definition.BaselineAppearance));
            Assert.That(selected.Id, Is.EqualTo(Character));
            Assert.That(selected.FrameCount, Is.EqualTo(4));
            // Whether the fallback has standing poses depends only on the V11
            // folder's own idle textures, never on the rejected upgrade record.
            Assert.That(selected.HasDedicatedIdle, Is.EqualTo(definition.BaselineAppearance.HasDedicatedIdle));
            Assert.That(calls, Is.Zero, "An unapproved record must not begin loading upgrade body art.");
        }

        [TestCase("\"version\":12", "\"version\":11")]
        [TestCase("\"frameCount\":8", "\"frameCount\":4")]
        [TestCase("\"frameCount\":8", "\"frameCount\":16")]
        [TestCase("\"frameDurationMs\":60", "\"frameDurationMs\":120")]
        [TestCase("\"dedicatedIdle\":true", "\"dedicatedIdle\":false")]
        [TestCase("\"contactFrame\":0", "\"contactFrame\":6")]
        [TestCase("\"status\":\"passed\"", "\"status\":\"processing\"")]
        [TestCase("\"visualReview\":\"passed\"", "\"visualReview\":\"required\"")]
        public void IncompleteOrIncompatibleApproval_DoesNotActivateEightPoseArtwork(string oldValue, string newValue)
        {
            var definition = QdaoCharacterCatalog.Find(Character);
            var selected = QdaoCharacterCatalog.SelectAppearance(definition, Approval().Replace(oldValue, newValue), (_, _, _) => true);
            Assert.That(selected, Is.SameAs(definition.BaselineAppearance));
        }

        [Test]
        public void AnApprovalForAnotherIdentityOrWithoutAcceptanceHashes_CannotReplaceThisCharacter()
        {
            var definition = QdaoCharacterCatalog.Find(Character);
            Assert.That(QdaoCharacterCatalog.SelectAppearance(definition, Approval("29_he_xiangu"), (_, _, _) => true),
                Is.SameAs(definition.BaselineAppearance));
            Assert.That(QdaoCharacterCatalog.SelectAppearance(definition, Approval(revision: "unfinished"), (_, _, _) => true),
                Is.SameAs(definition.BaselineAppearance));
        }

        private static IEnumerable<string> RequiredRuntimeImages()
        {
            yield return "portrait";
            foreach (var direction in Directions)
            {
                yield return "idle/" + direction;
                for (var frame = 1; frame <= 8; frame++) yield return $"walk/{direction}/{frame:00}";
            }
        }

        [TestCaseSource(nameof(RequiredRuntimeImages))]
        public void EverySingleMissingDirectionPoseIdleOrPortrait_PreventsPartialActivation(string missing)
        {
            var definition = QdaoCharacterCatalog.Find(Character);
            var selected = QdaoCharacterCatalog.SelectAppearance(definition, Approval(),
                (path, _, _) => path != V12 + missing);
            Assert.That(selected, Is.SameAs(definition.BaselineAppearance), missing);
        }

        [TestCase(0)]
        [TestCase(4)]
        public void CompleteUpgrade_UsesEightPosesAndDedicatedIdleAtTheSameCycleDuration(int contact)
        {
            var definition = QdaoCharacterCatalog.Find(Character);
            var checkedPaths = new HashSet<string>();
            var selected = QdaoCharacterCatalog.SelectAppearance(definition, Approval(contact: contact), (path, width, height) =>
            {
                Assert.That(path, Does.StartWith(V12));
                Assert.That(width, Is.EqualTo(path.EndsWith("/portrait") ? 1024 : 512));
                Assert.That(height, Is.EqualTo(width));
                Assert.That(checkedPaths.Add(path), Is.True, "Each real pose has its own texture path.");
                return true;
            });
            Assert.That(checkedPaths.Count, Is.EqualTo(73));
            Assert.That(selected.Version, Is.EqualTo(12));
            Assert.That(selected.FrameCount, Is.EqualTo(8));
            Assert.That(selected.HasDedicatedIdle, Is.True);
            Assert.That(selected.ContactFrame, Is.EqualTo(contact));
            Assert.That(selected.FrameCount / selected.FramesPerSecond, Is.EqualTo(.48f).Within(.00001f));
            Assert.That(definition.BaselineAppearance.FrameCount / definition.BaselineAppearance.FramesPerSecond,
                Is.EqualTo(selected.FrameCount / selected.FramesPerSecond).Within(.00001f));
            Assert.That(selected.IdleResourcePath("NW"), Is.EqualTo(V12 + "idle/NW"));
            Assert.That(selected.FrameResourcePath("NW", 7), Is.EqualTo(V12 + "walk/NW/08"));
        }

        [Test]
        public void MixedVersionsAndNewAcceptedRevisions_DoNotShareSpriteCacheKeys()
        {
            var definition = QdaoCharacterCatalog.Find(Character);
            var accepted = QdaoCharacterCatalog.SelectAppearance(definition, Approval(), (_, _, _) => true);
            var revised = QdaoCharacterCatalog.SelectAppearance(definition, Approval(revision: new string('d', 64)), (_, _, _) => true);
            var alternateContact = QdaoCharacterCatalog.SelectAppearance(definition, Approval(contact: 4), (_, _, _) => true);
            Assert.That(new HashSet<string> { definition.BaselineAppearance.CacheKey, accepted.CacheKey,
                revised.CacheKey, alternateContact.CacheKey }.Count, Is.EqualTo(4));
            var other = QdaoCharacterCatalog.Find("29_he_xiangu");
            Assert.That(QdaoCharacterCatalog.SelectAppearance(other, null, (_, _, _) => true), Is.SameAs(other.BaselineAppearance));
            Assert.That(accepted.Id, Is.EqualTo(Character));
            Assert.That(definition.BaselineAppearance.Id, Is.EqualTo(Character));
        }
    }
}
