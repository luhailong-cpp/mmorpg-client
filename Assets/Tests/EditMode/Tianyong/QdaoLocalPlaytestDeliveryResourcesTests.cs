using System;
using System.Security.Cryptography;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>只验证07/15本次候选的实际运行合同，不把技术成功视为美术验收。</summary>
    public sealed class QdaoLocalPlaytestDeliveryResourcesTests
    {
        [Serializable]
        private sealed class ReviewRecord
        {
            public string status;
            public string visualReview;
            public string visual_review;
            public string integration_mode;
            public string source_document_sha256;
            public string source_manifest_sha256;
            public bool source_art_approval_claimed;
            public bool source_dynamic_review_complete;
        }

        [SetUp]
        public void RefreshBeforeTest() => QdaoCharacterCatalog.RefreshAppearances();

        [TearDown]
        public void RefreshAfterTest() => QdaoCharacterCatalog.RefreshAppearances();

        [TestCase("07_moon_shadow_assassin_girl")]
        [TestCase("15_water_dragon_scholar_boy")]
        public void CompleteCandidateLoadsOwnEightDirectionsAndPreservesPendingReview(string id)
        {
            var folder = QdaoCharacterCatalog.OriginalV14Root + "/" + id;
            var manifest = Resources.Load<TextAsset>(folder + "/manifest");
            var activation = Resources.Load<TextAsset>(folder + "/appearance");
            var validation = Resources.Load<TextAsset>(folder + "/validation");
            var index = Resources.Load<QdaoOriginalHdResourceIndex>(folder + "/runtime-index");
            Assert.That(manifest, Is.Not.Null);
            Assert.That(activation, Is.Not.Null);
            Assert.That(validation, Is.Not.Null);
            Assert.That(index, Is.Not.Null);
            Assert.That(index.entries.Length, Is.EqualTo(137));
            Assert.That(QdaoOriginalHdResourceIndex.IsComplete(folder, Hash(manifest.bytes), Hash(activation.bytes)), Is.True);
            foreach (var asset in new[] { manifest, activation, validation })
            {
                var record = JsonUtility.FromJson<ReviewRecord>(asset.text);
                Assert.That(record.status, Is.EqualTo("passed"), "技术结构检查");
                Assert.That(record.integration_mode, Is.EqualTo(QdaoLocalPlaytestContract.Mode));
                Assert.That(record.source_art_approval_claimed, Is.False);
                Assert.That(record.source_dynamic_review_complete, Is.False);
                Assert.That(record.visualReview ?? record.visual_review, Is.EqualTo(QdaoLocalPlaytestContract.PendingReview));
                Assert.That(record.source_document_sha256, Has.Length.EqualTo(64));
                Assert.That(record.source_manifest_sha256, Has.Length.EqualTo(64));
            }

            var appearance = QdaoCharacterCatalog.Find(id)?.ResolveAppearance();
            Assert.That(appearance, Is.Not.Null);
            Assert.That(appearance.Id, Is.EqualTo(id));
            Assert.That(appearance.ResourceFolder, Is.EqualTo(folder));
            Assert.That(appearance.Version, Is.EqualTo(14));
            Assert.That(appearance.FrameCount, Is.EqualTo(16));
            Assert.That(appearance.FrameDurationMs, Is.EqualTo(30));
            Assert.That(appearance.HasDedicatedIdle, Is.True);
            Assert.That(appearance.IsHd && !appearance.IsMixedResolution, Is.True);
            foreach (var direction in new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" })
            foreach (var path in new[] { appearance.FrameResourcePath(direction, 0),
                         appearance.FrameResourcePath(direction, 15), appearance.IdleResourcePath(direction) })
            {
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

        [TestCase("07_moon_shadow_assassin_girl")]
        [TestCase("15_water_dragon_scholar_boy")]
        public void PendingCandidateRejectsReleaseWrongIdentityAndAnyChangedEvidence(string id)
        {
            var folder = QdaoCharacterCatalog.OriginalV14Root + "/" + id;
            var manifest = Resources.Load<TextAsset>(folder + "/manifest");
            var activation = Resources.Load<TextAsset>(folder + "/appearance");
            Assert.That(manifest, Is.Not.Null);
            Assert.That(activation, Is.Not.Null);
            Assert.That(QdaoLocalPlaytestContract.MatchesPinnedDelivery(id, 14, activation.text, manifest.bytes, true), Is.True);
            Assert.That(QdaoLocalPlaytestContract.MatchesPinnedDelivery(id, 14, activation.text, manifest.bytes, false), Is.False);
            Assert.That(QdaoLocalPlaytestContract.MatchesPinnedDelivery(id, 13, activation.text, manifest.bytes, true), Is.False);
            Assert.That(QdaoLocalPlaytestContract.MatchesPinnedDelivery("11_jade_fist_flat_top_boy", 14,
                activation.text, manifest.bytes, true), Is.False);
            var changed = (byte[])manifest.bytes.Clone();
            changed[changed.Length - 1] ^= 1;
            Assert.That(QdaoLocalPlaytestContract.MatchesPinnedDelivery(id, 14, activation.text, changed, true), Is.False);
            var fakeApproval = activation.text.Replace(QdaoLocalPlaytestContract.PendingReview, "passed");
            Assert.That(QdaoLocalPlaytestContract.MatchesPinnedDelivery(id, 14, fakeApproval, manifest.bytes, true), Is.False);
            Assert.That(QdaoCharacterCatalog.SelectOriginalHdAppearance(QdaoCharacterCatalog.Find(id),
                fakeApproval, manifest.bytes, (_, _, _) => true), Is.Null,
                "不能通过单改activation中的审批字段绕过来源manifest仍pending的合同");
        }

        private static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
