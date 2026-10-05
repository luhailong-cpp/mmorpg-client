using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MmorpgClient.Game.Pet;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Pet;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using Vector3 = UnityEngine.Vector3;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class PetAppearanceIntegrationTests
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

        [Test]
        public void CatalogHasTwentyDistinctPreviewIdentitiesAndOnlyProvenNetworkBindings()
        {
            Assert.That(QdaoPetCatalog.Entries.Count, Is.EqualTo(20));
            Assert.That(QdaoPetCatalog.Entries.Select(x => x.id).Distinct().Count(), Is.EqualTo(20));
            Assert.That(QdaoPetCatalog.Resolve(1, 1001)?.id, Is.EqualTo("legacy-ling-yue"));
            Assert.That(QdaoPetCatalog.Resolve(4, 1004)?.id, Is.EqualTo("legacy-yun-jiu-jiu"));
            Assert.That(QdaoPetCatalog.Resolve(2, 1002), Is.Null, "Stone spirit has no proven new identity.");
            Assert.That(QdaoPetCatalog.Resolve(3, 1003), Is.Null);
            Assert.That(QdaoPetCatalog.Resolve(2, 1001), Is.Null, "Table identity wins over a conflicting model.");
            Assert.That(QdaoPetCatalog.Resolve(1, 1004), Is.Null);
            Assert.That(QdaoPetCatalog.Resolve(0, 1001)?.id, Is.EqualTo("legacy-ling-yue"));
        }

        [Test]
        public void EveryPreviewIdentityHasIndependentEastAndWestIdleAndNoInventedRun()
        {
            int baseline = QdaoActionResources.ResidentClipCount;
            foreach (var entry in QdaoPetCatalog.Entries)
            {
                using var east = QdaoPetCatalog.Acquire(entry.id, "idle", "E");
                using var west = QdaoPetCatalog.Acquire(entry.id, "idle", "W");
                Assert.That(east?.IsValid, Is.True, entry.id + " E");
                Assert.That(west?.IsValid, Is.True, entry.id + " W");
                Assert.That(east.Frames[0].texture, Is.Not.SameAs(west.Frames[0].texture));
                Assert.That(QdaoPetCatalog.HasAction(entry.id, "run", "E"), Is.False,
                    "This delivery has no authored pet run clips; update this contract when real frames arrive.");
            }
            Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(baseline));
        }

        [Test]
        public void BattlePetUsesTableIdentityAndNeverTheOwnersPlayerAppearance()
        {
            var pet = new BattleActorState { ActorId = 999, OwnerPlayerId = 17, PetId = 300,
                PetTableId = 1, ActorType = eBattleActorType.BattleActorTypePet,
                AppearanceId = "00_reference_topright_boy", ClassId = 1 };
            int calls = 0;
            Assert.That(BattleArtCatalog.CharacterIdFor(pet, _ => { calls++; return "29_he_xiangu"; }),
                Is.EqualTo("legacy-ling-yue"));
            Assert.That(calls, Is.Zero);
            pet.PetTableId = 2;
            Assert.That(BattleArtCatalog.CharacterIdFor(pet), Is.EqualTo(QdaoPetCatalog.UnknownIdentity));
            Assert.That(BattleArtCatalog.LoadCharacterAction(QdaoPetCatalog.UnknownIdentity, "attack", true), Is.Null);
            Assert.That(BattleArtCatalog.LoadPlayerWalk(QdaoPetCatalog.UnknownIdentity, true), Is.Null);
        }

        [Test]
        public void BattleIdleLeaseSurvivesTemporaryStripAndReleasesWithView()
        {
            int baseline = QdaoActionResources.ResidentClipCount;
            var layer = new GameObject("PetBattle", typeof(RectTransform), typeof(Canvas));
            var view = new BattleUnitView(null, layer.transform, 99, false, true, 0, null);
            try
            {
                view.Apply(new BattleActorState { ActorId = 99, ActorType = eBattleActorType.BattleActorTypePet,
                    PetTableId = 1, OwnerPlayerId = 7, MaxHealth = 100, MaxMana = 100 });
                var body = (UnityEngine.UI.Image)typeof(BattleUnitView).GetField("_body", Hidden).GetValue(view);
                Assert.That(body.sprite != null && body.sprite.texture != null, Is.True);
                Assert.That(body.sprite.name, Does.StartWith("legacy-ling-yue_archived_idle_"));
                Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(baseline + 1));
                view.StopAction();
                Assert.That(body.sprite != null, Is.True);
            }
            finally
            {
                view.StopAction();
                MmorpgClient.UI.Ugui.Tweening.RealtimeTween.Kill(typeof(BattleUnitView).GetField("_idleToken", Hidden).GetValue(view));
                Object.DestroyImmediate(layer);
            }
            Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(baseline));
        }

        [Test]
        public void PreviewSelectionDoesNotChangeAuthoritativePetOrSendRequests()
        {
            var net = new FakeBattleTransport();
            var client = new PetClient(net);
            var list = new PetListInfo { ActivePetId = 101 };
            list.Pets.Add(new PetInfo { PetId = 101, PetTableId = 1, ModelId = 1001 });
            net.PushNotify(MessageIds.NotifyPetListChanged, new PetListChangedS2C { Pets = list });
            var go = new GameObject("PetGallery", typeof(RectTransform), typeof(Canvas));
            try
            {
                var preview = go.AddComponent<PetArchivePreview>();
                preview.Build();
                go.SetActive(true);
                foreach (var entry in QdaoPetCatalog.Entries)
                {
                    preview.Select(entry.id);
                    Assert.That(preview.SelectedId, Is.EqualTo(entry.id));
                    Assert.That(preview.CurrentSprite != null, Is.True, entry.id);
                }
                Assert.That(net.Calls, Is.Empty);
                Assert.That(client.Pets.ActivePetId, Is.EqualTo(101UL));
                go.SetActive(false);
                Assert.That(preview.CurrentSprite, Is.Null);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void CompanionTracksSummonRecallDisconnectAndWorldResetWithoutCreatingAnEntity()
        {
            var net = new FakeBattleTransport();
            var pets = new PetClient(net);
            var world = new ActorWorld("PetCompanionTestWorld");
            var companion = world.Root.gameObject.AddComponent<QdaoPetCompanion>();
            companion.Bind(world, pets);
            try
            {
                // NPC-shaped fixture avoids loading unrelated character resources; it still uses the real local binding.
                world.SpawnActor(10, ActorKind.Npc, 1, Vector3.zero, Vector3.zero);
                world.SetLocalPlayer(10);
                var list = new PetListInfo { ActivePetId = 301 };
                list.Pets.Add(new PetInfo { PetId = 301, PetTableId = 4, ModelId = 1004 });
                net.PushNotify(MessageIds.NotifyPetListChanged, new PetListChangedS2C { Pets = list });
                Tick(companion);
                Assert.That(companion.PetIdentity, Is.EqualTo("legacy-yun-jiu-jiu"));
                Assert.That(world.Actors.Count, Is.EqualTo(1));
                world.Clear();
                Assert.That(companion.ActivePetId, Is.Zero, "Clear hides immediately, including reset and respawn within one frame.");
                world.SpawnActor(11, ActorKind.Npc, 1, new Vector3(15, 0, 0), Vector3.zero);
                world.SetLocalPlayer(11);
                Tick(companion);
                Assert.That(companion.ActivePetId, Is.EqualTo(301UL));
                list.ActivePetId = 0;
                net.PushNotify(MessageIds.NotifyPetListChanged, new PetListChangedS2C { Pets = list });
                Tick(companion);
                Assert.That(companion.PetIdentity, Is.Null);
                list.ActivePetId = 301;
                net.PushNotify(MessageIds.NotifyPetListChanged, new PetListChangedS2C { Pets = list });
                Tick(companion);
                net.RaiseDisconnected();
                Tick(companion);
                Assert.That(companion.ActivePetId, Is.Zero);
            }
            finally { Object.DestroyImmediate(world.Root.gameObject); }
        }

        [TestCase(512, 512)]
        [TestCase(1254, 1254)]
        [TestCase(512, 1024)]
        public void PetClipPreservesSourceCanvasAndRejectsIncompleteFrames(int width, int height)
        {
            var texture = new Texture2D(width, height);
            var json = "{\"schemaVersion\":1,\"characterId\":\"test-pet\",\"pixelsPerUnit\":160," +
                "\"pivotX\":0.5,\"pivotY\":0.08,\"clips\":[{\"action\":\"idle\",\"direction\":\"E\",\"frameCount\":1," +
                "\"frameDurationMs\":100,\"frameWidth\":" + width + ",\"frameHeight\":" + height + "}]}";
            var acquire = typeof(QdaoActionResources).GetMethod("AcquirePetWithResources", BindingFlags.NonPublic | BindingFlags.Static);
            var paths = new List<string>();
            Func<string, Texture2D> load = path => { paths.Add(path); return paths.Count == 1 ? texture : null; };
            try
            {
                using (var lease = (QdaoActionResources.Lease)acquire.Invoke(null, new object[] { "test-pet", "idle", "E", json, load, false }))
                {
                    Assert.That(lease?.IsValid, Is.True);
                    Assert.That(lease.Frames[0].rect.size, Is.EqualTo(new Vector2(width, height)));
                    Assert.That(paths[0], Is.EqualTo(QdaoPetCatalog.ResourceRoot + "/test-pet/idle/E/01"));
                }
                paths.Clear();
                using var missing = (QdaoActionResources.Lease)acquire.Invoke(null, new object[] {
                    "test-pet", "idle", "E", json.Replace("\"frameCount\":1", "\"frameCount\":2"), load, false });
                Assert.That(missing, Is.Null, "A partial authored clip must not play its first frame as a complete animation.");
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private static void Tick(QdaoPetCompanion companion)
            => typeof(QdaoPetCompanion).GetMethod("LateUpdate", Hidden).Invoke(companion, null);
    }
}
