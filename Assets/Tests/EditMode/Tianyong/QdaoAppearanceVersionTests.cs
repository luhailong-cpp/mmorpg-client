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

        [TestCase(0, 2)]
        [TestCase(2, 2)]
        [TestCase(3, 3)]
        public void SupportedAlignmentVersions_KeepTheSameFramesAndWorldRootContract(int declared, int expected)
        {
            var definition = QdaoCharacterCatalog.Find(Character);
            var json = Approval().Replace("\"version\":12", "\"version\":12,\"alignmentVersion\":" + declared);
            var selected = QdaoCharacterCatalog.SelectAppearance(definition, json, (_, _, _) => true);
            Assert.That(selected.AlignmentVersion, Is.EqualTo(expected));
            Assert.That(selected.Version, Is.EqualTo(12));
            Assert.That(selected.FrameCount, Is.EqualTo(8));
            Assert.That(selected.ResourceFolder, Is.EqualTo(V12.TrimEnd('/')));
            var legacy = QdaoCharacterCatalog.SelectAppearance(definition, Approval(), (_, _, _) => true);
            Assert.That(legacy.AlignmentVersion, Is.EqualTo(2));
            if (expected == 3) Assert.That(selected.CacheKey, Is.Not.EqualTo(legacy.CacheKey));
        }

        [TestCase(-1)]
        [TestCase(1)]
        [TestCase(4)]
        public void UnknownAlignmentVersions_CannotActivateArtwork(int declared)
        {
            var definition = QdaoCharacterCatalog.Find(Character);
            var json = Approval().Replace("\"version\":12", "\"version\":12,\"alignmentVersion\":" + declared);
            var calls = 0;
            var selected = QdaoCharacterCatalog.SelectAppearance(definition, json, (_, _, _) => { calls++; return true; });
            Assert.That(selected, Is.SameAs(definition.BaselineAppearance));
            Assert.That(calls, Is.Zero);
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
        private static string Approval13()
            => Approval().Replace("\"version\":12", "\"version\":13,\"alignmentVersion\":3")
                .Replace("\"frameCount\":8", "\"frameCount\":16")
                .Replace("\"frameDurationMs\":60", "\"frameDurationMs\":30");

        [Test]
        public void CompleteV13_UsesSixteenThirtyMillisecondPosesAndPreservesV12CycleDistance()
        {
            var definition = QdaoCharacterCatalog.Find(Character);
            var paths = new HashSet<string>();
            var v13 = QdaoCharacterCatalog.SelectAppearance(definition, Approval13(), Approval(), (path, w, h) =>
            {
                Assert.That(path, Does.StartWith("World/Characters/QdaoRosterV13/" + Character + "/"));
                Assert.That(w, Is.EqualTo(path.EndsWith("/portrait") ? 1024 : 512));
                Assert.That(h, Is.EqualTo(w));
                Assert.That(paths.Add(path), Is.True);
                return true;
            });
            var v12 = QdaoCharacterCatalog.SelectAppearance(definition, Approval(), (_, _, _) => true);
            Assert.That(paths.Count, Is.EqualTo(137));
            Assert.That(v13.Version, Is.EqualTo(13));
            Assert.That(v13.FrameCount, Is.EqualTo(16));
            Assert.That(v13.FrameDurationMs, Is.EqualTo(30));
            Assert.That(v13.AlignmentVersion, Is.EqualTo(3));
            Assert.That(v13.ContactFrame, Is.Zero);
            Assert.That(v13.HasDedicatedIdle, Is.True);
            Assert.That(v13.FrameCount / v13.FramesPerSecond, Is.EqualTo(.48f).Within(.000001f));
            // Animator uses frames/second divided by unchanged 9u/s reference speed.
            Assert.That(v13.FrameCount / (v13.FramesPerSecond / 9f),
                Is.EqualTo(v12.FrameCount / (v12.FramesPerSecond / 9f)).Within(.000001f));
            Assert.That(v13.FrameResourcePath("NW", 15), Does.EndWith("walk/NW/16"));
            Assert.That(v13.StripResourcePath("NW"), Does.EndWith("walk/NW/strip"));
        }

        [TestCase("\"version\":13", "\"version\":12")]
        [TestCase("\"frameCount\":16", "\"frameCount\":8")]
        [TestCase("\"frameDurationMs\":30", "\"frameDurationMs\":60")]
        [TestCase("\"alignmentVersion\":3", "\"alignmentVersion\":2")]
        [TestCase("\"alignmentVersion\":3", "\"alignmentVersion\":0")]
        [TestCase("\"contactFrame\":0", "\"contactFrame\":8")]
        [TestCase("\"dedicatedIdle\":true", "\"dedicatedIdle\":false")]
        [TestCase("\"status\":\"passed\"", "\"status\":\"processing\"")]
        [TestCase("\"visualReview\":\"passed\"", "\"visualReview\":\"required\"")]
        [TestCase("24_lu_dongbin", "29_he_xiangu")]
        public void RejectedV13_RetainsCompleteSameIdentityV12(string from, string to)
        {
            var selected = QdaoCharacterCatalog.SelectAppearance(QdaoCharacterCatalog.Find(Character),
                Approval13().Replace(from, to), Approval(), (_, _, _) => true);
            Assert.That(selected.Version, Is.EqualTo(12));
            Assert.That(selected.Id, Is.EqualTo(Character));
        }

        private static IEnumerable<string> RequiredV13Images()
        {
            yield return "portrait";
            foreach (var direction in Directions)
            {
                yield return "idle/" + direction;
                for (var f = 1; f <= 16; f++) yield return $"walk/{direction}/{f:00}";
            }
        }

        [TestCaseSource(nameof(RequiredV13Images))]
        public void EveryMissingV13Image_FallsBackToV12ThenV11WhenV12IsAlsoIncomplete(string missing)
        {
            var definition = QdaoCharacterCatalog.Find(Character);
            var prefix = "World/Characters/QdaoRosterV13/" + Character + "/";
            var selected = QdaoCharacterCatalog.SelectAppearance(definition, Approval13(), Approval(),
                (path, _, _) => path != prefix + missing);
            Assert.That(selected.Version, Is.EqualTo(12), missing);
            Assert.That(selected.Id, Is.EqualTo(Character));
            selected = QdaoCharacterCatalog.SelectAppearance(definition, Approval13(), Approval(),
                (path, _, _) => path != prefix + missing && path != V12 + "walk/S/08");
            Assert.That(selected, Is.SameAs(definition.BaselineAppearance), missing);
        }

        [TestCase(null)]
        [TestCase("{broken")]
        [TestCase("{}")]
        public void MalformedV13_DoesNotSkipCompleteV12(string metadata)
        {
            Assert.That(QdaoCharacterCatalog.SelectAppearance(QdaoCharacterCatalog.Find(Character), metadata,
                Approval(), (_, _, _) => true).Version, Is.EqualTo(12));
        }

        [Test]
        public void BothActivationRecordsAndEveryAcceptanceHash_InvalidateResolvedRevisions()
        {
            var definition = new QdaoCharacterCatalog.Definition(Character, "test");
            string v13 = null;
            var v12 = Approval();
            string Metadata(string path) => path.Contains("V13/") ? v13 : v12;
            bool Inventory(string _, int __, int ___) => true;
            var first = definition.ResolveAppearance(Metadata, Inventory);
            Assert.That(definition.ResolveAppearance(Metadata, Inventory), Is.SameAs(first));
            v13 = Approval13();
            var upgraded = definition.ResolveAppearance(Metadata, Inventory);
            Assert.That(upgraded.Version, Is.EqualTo(13));
            Assert.That(upgraded.CacheKey, Is.Not.EqualTo(first.CacheKey));
            v13 = null;
            foreach (var hash in new[] { 'a', 'b', 'c' })
            {
                var previous = definition.ResolveAppearance(Metadata, Inventory);
                v12 = v12.Replace(new string(hash, 64), new string((char)(hash + 3), 64));
                var revised = definition.ResolveAppearance(Metadata, Inventory);
                Assert.That(revised.Version, Is.EqualTo(12));
                Assert.That(revised.CacheKey, Is.Not.EqualTo(previous.CacheKey));
            }
        }

        [TestCase('a')]
        [TestCase('b')]
        [TestCase('c')]
        public void NonHexAcceptanceHashes_CannotActivateV12OrV13(char originalHash)
        {
            var definition = QdaoCharacterCatalog.Find(Character);
            var from = new string(originalHash, 64);
            var invalid = new string('g', 64);
            Assert.That(QdaoCharacterCatalog.SelectAppearance(definition,
                Approval().Replace(from, invalid), (_, _, _) => true), Is.SameAs(definition.BaselineAppearance));
            Assert.That(QdaoCharacterCatalog.SelectAppearance(definition,
                Approval13().Replace(from, invalid), Approval(), (_, _, _) => true).Version, Is.EqualTo(12));
        }

        [Test]
        public void IncompleteV13Import_IsRetriedEvenWhileV12IsUsableAndMetadataIsUnchanged()
        {
            var definition = new QdaoCharacterCatalog.Definition(Character, "test");
            var importing = true;
            string Metadata(string path) => path.Contains("V13/") ? Approval13() : Approval();
            bool Inventory(string path, int _, int __) => !importing || !path.EndsWith("V13/" + Character + "/walk/NW/16");
            Assert.That(definition.ResolveAppearance(Metadata, Inventory).Version, Is.EqualTo(12));
            importing = false;
            Assert.That(definition.ResolveAppearance(Metadata, Inventory).Version, Is.EqualTo(13));
        }

        [Test]
        public void ActualLoadFallback_AlwaysMovesDownTheSameIdentityVersionChain()
        {
            var definition = QdaoCharacterCatalog.Find(Character);
            var v13 = QdaoCharacterCatalog.SelectAppearance(definition, Approval13(), Approval(), (_, _, _) => true);
            var next = QdaoCharacterCatalog.SelectFallbackAppearance(v13, Approval(), (_, _, _) => true);
            Assert.That(next.Version, Is.EqualTo(12));
            Assert.That(next.Id, Is.EqualTo(Character));
            Assert.That(QdaoCharacterCatalog.SelectFallbackAppearance(next, Approval(), (_, _, _) => true),
                Is.SameAs(definition.BaselineAppearance));
            Assert.That(QdaoCharacterCatalog.SelectFallbackAppearance(v13, Approval(), (_, _, _) => false),
                Is.SameAs(definition.BaselineAppearance));
            Assert.That(QdaoCharacterCatalog.SelectFallbackAppearance(definition.BaselineAppearance, Approval(), (_, _, _) => true), Is.Null);
        }

        [Test]
        public void CompletedResourceRefresh_SeparatesSpriteCacheFromPriorImport()
        {
            var definition = QdaoCharacterCatalog.Find(Character);
            var first = QdaoCharacterCatalog.SelectAppearance(definition, Approval13(), Approval(), (_, _, _) => true);
            var fallbackBefore = definition.BaselineAppearance;
            QdaoCharacterCatalog.RefreshAppearances();
            var refreshed = QdaoCharacterCatalog.SelectAppearance(definition, Approval13(), Approval(), (_, _, _) => true);
            Assert.That(refreshed.CacheKey, Is.Not.EqualTo(first.CacheKey));
            Assert.That(definition.BaselineAppearance.CacheKey, Is.Not.EqualTo(fallbackBefore.CacheKey));
            Assert.That(definition.BaselineAppearance.Id, Is.EqualTo(fallbackBefore.Id));
            Assert.That(definition.BaselineAppearance.Version, Is.EqualTo(11));
        }

        [TestCase("QdaoOriginalRosterV13/01_ice_sword_girl/walk/S/strip.png", 8192, 512, 8192)]
        [TestCase("QdaoOriginalRosterV13/01_ice_sword_girl/walk/S/01.png", 512, 512, 4096)]
        [TestCase("QdaoRosterV13/24_lu_dongbin/walk/S/strip.png", 8192, 512, 8192)]
        [TestCase("QdaoRosterV13/24_lu_dongbin/walk/S/01.png", 512, 512, 4096)]
        [TestCase("QdaoRosterV13/24_lu_dongbin/walk/S/strip.png", 4096, 512, 4096)]
        [TestCase("QdaoRosterV12/24_lu_dongbin/walk/S/strip.png", 4096, 512, 4096)]
        [TestCase("QdaoHeadbandBoy/walk_S.png", 4096, 512, 4096)]
        public void OnlyFullV13ReviewStrips_ReceiveThe8192ImportLimit(string path, int width, int height, int expected)
        {
            // An asmdef test assembly cannot directly reference the predefined editor assembly.
            var importerType = System.Type.GetType(
                "MmorpgClient.Editor.Tianyong.QdaoCharacterSpriteImporter, Assembly-CSharp-Editor");
            Assert.That(importerType, Is.Not.Null, "The character texture importer must be available in the editor.");
            var requiredMaxTextureSize = importerType.GetMethod("RequiredMaxTextureSize",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null, new[] { typeof(string), typeof(int), typeof(int) }, null);
            Assert.That(requiredMaxTextureSize, Is.Not.Null,
                "The importer must expose RequiredMaxTextureSize(string, int, int).");
            Assert.That(requiredMaxTextureSize.ReturnType, Is.EqualTo(typeof(int)));

            Assert.That((int)requiredMaxTextureSize.Invoke(null,
                new object[] { "Assets/Resources/World/Characters/" + path, width, height }), Is.EqualTo(expected));
            Assert.That((int)requiredMaxTextureSize.Invoke(null,
                new object[] { "Assets/Resources/Battle/strip.png", 8192, 512 }), Is.EqualTo(4096));
        }

    }
}
