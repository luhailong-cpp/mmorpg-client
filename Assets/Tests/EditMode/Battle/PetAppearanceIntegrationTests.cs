using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MmorpgClient.Game.Pet;
using MmorpgClient.Game.Battle.Presentation;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Pet;
using MmorpgClient.UI.Ugui.Tweening;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MmorpgClient.Tests.EditMode.Battle
{
    // proto 生成物在全局命名空间里有同名的 Vector3;别名必须写在命名空间内部才能盖过它。
    using Vector3 = UnityEngine.Vector3;

    public sealed class PetAppearanceIntegrationTests
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo TweenTick = typeof(RealtimeTween).GetMethod("UpdateAll", BindingFlags.NonPublic | BindingFlags.Static);
        [Serializable] private sealed class DeliveryManifest { public DeliveryClip[] clips; }
        [Serializable] private sealed class DeliveryClip { public string action, direction; }

        [TestCase("attack", 12, .36f)]
        [TestCase("hit", 6, .24f)]
        [TestCase("cast", 16, .72f)]
        public void PublishedCombatClipsLoadEveryOwnFrameAtSourceCadence(string action, int count, float seconds)
        {
            int baseline = QdaoActionResources.ResidentClipCount;
            int published = 0;
            foreach (var entry in QdaoPetCatalog.Entries)
            foreach (var direction in new[] { "E", "W" })
            {
                bool expected = Declared(entry.id, action, direction);
                Assert.That(QdaoPetCatalog.HasAction(entry.id, action, direction), Is.EqualTo(expected));
                using var strip = BattleArtCatalog.LoadCharacterAction(entry.id, action, direction == "E");
                if (!expected)
                {
                    Assert.That(strip, Is.Null, entry.id + "/" + action + "/" + direction + " must keep the procedural fallback.");
                    continue;
                }
                published++;
                Assert.That(strip, Is.Not.Null, entry.id + "/" + action + "/" + direction);
                Assert.That(strip.Count, Is.EqualTo(count));
                Assert.That(strip.DurationSeconds, Is.EqualTo(seconds).Within(.0001f));
                Assert.That(strip.Mirrored, Is.False);
                Assert.That(strip.UseWorldGeometry, Is.True);
                Assert.That(strip.EventFrame, Is.InRange(-1, count - 1));
                for (int i = 0; i < count; i++)
                {
                    Assert.That(strip.Frames[i] != null && strip.Frames[i].texture != null, Is.True);
                    Assert.That(strip.Frames[i].name, Is.EqualTo(entry.id + "_archived_" + action + "_" + direction + "_" + (i + 1).ToString("00")));
                    Assert.That(strip.FrameAt((i + .5f) / count), Is.SameAs(strip.Frames[i]));
                }
            }
            Assert.That(published, Is.GreaterThan(0));
            Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(baseline));
        }

        [TestCase(1u, true)]
        [TestCase(1u, false)]
        [TestCase(4u, true)]
        [TestCase(4u, false)]
        public void BoundPetsPlayCombatFramesFireOnceAndReturnToTheirOwnIdle(uint tableId, bool east)
        {
            int baseline = QdaoActionResources.ResidentClipCount;
            float previousSpeed = BattleTempo.Speed;
            var layer = new GameObject("PetCombatPlayback", typeof(RectTransform), typeof(Canvas));
            var view = new BattleUnitView(null, layer.transform, 99, false, !east, 0, null)
            { Fx = new BattleFxPlayer(null), Ghosts = new BattleAfterimagePool(null) };
            try
            {
                BattleTempo.Speed = 1f;
                view.Apply(new BattleActorState { ActorId = 99, ActorType = eBattleActorType.BattleActorTypePet,
                    PetTableId = tableId, OwnerPlayerId = 7, MaxHealth = 100, MaxMana = 100,
                    Attributes = new BaseAttributesComp { Health = 100, Mana = 100 } });
                view.SetPlacement(new Vector2(400f, 250f), 1f);
                var body = view.Root.Find("Body").GetComponent<UnityEngine.UI.Image>();
                var idle = body.sprite;
                foreach (var action in new[] { "attack", "hit", "cast" })
                {
                    string direction = east ? "E" : "W";
                    bool published = Declared(view.CharacterId, action, direction);
                    var seen = new HashSet<string>();
                    string prefix = view.CharacterId + "_archived_" + action + "_" + direction + "_";
                    int events = 0;
                    if (action == "attack") view.PlayAttackLunge(new Vector2(700f, 250f), () => events++);
                    else if (action == "cast") view.PlayCast(() => events++);
                    else view.PlayHit(false);
                    void Observe()
                    {
                        if (body.sprite != null && body.sprite.name.StartsWith(prefix, StringComparison.Ordinal)) seen.Add(body.sprite.name);
                    }
                    Observe();
                    Advance(view.ActionDurationSeconds(action) + .1f, Observe);
                    Assert.That(events, Is.EqualTo(action == "hit" ? 0 : 1), action + " callback count");
                    Assert.That(seen.Count, Is.EqualTo(published ? action == "attack" ? 12 : action == "hit" ? 6 : 16 : 0),
                        view.CharacterId + "/" + action + "/" + direction);
                    Assert.That(body.sprite, Is.SameAs(idle));
                    Assert.That(view.Root.anchoredPosition, Is.EqualTo(new Vector2(400f, -250f)));
                    Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(baseline + 1));
                }

                int cancelled = 0;
                view.PlayCast(() => cancelled++);
                int beforeCancel = cancelled; // A future authored frame-zero release may already have fired.
                view.StopAction();
                Advance(1f);
                Assert.That(cancelled, Is.EqualTo(beforeCancel), "Cancellation must not release a queued pet cast.");
                Assert.That(body.sprite, Is.SameAs(idle));
            }
            finally
            {
                BattleTempo.Speed = previousSpeed;
                view.StopAction();
                RealtimeTween.Kill(typeof(BattleUnitView).GetField("_idleToken", Hidden).GetValue(view));
                Object.DestroyImmediate(layer);
            }
            Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(baseline));
        }

        [Test]
        public void GalleryEnablesOnlyPublishedDirectionsAndCombatCompletionReleasesItsLease()
        {
            int baseline = QdaoActionResources.ResidentClipCount;
            var go = new GameObject("PetCombatGallery", typeof(RectTransform), typeof(Canvas));
            try
            {
                var preview = go.AddComponent<PetArchivePreview>();
                preview.Build();
                go.SetActive(true);
                var buttons = go.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                foreach (var entry in QdaoPetCatalog.Entries)
                foreach (var direction in new[] { "E", "W" })
                {
                    preview.Select(entry.id);
                    buttons.Single(x => x.name == (direction == "E" ? "FaceEastPlate" : "FaceWestPlate")).onClick.Invoke();
                    foreach (var action in new[] { "attack", "hit", "cast" })
                        Assert.That(buttons.Single(x => x.name == "Preview_" + action + "Plate").interactable,
                            Is.EqualTo(Declared(entry.id, action, direction)), entry.id + "/" + action + "/" + direction);
                }
                preview.Select("legacy-yun-jiu-jiu");
                foreach (var direction in new[] { "E", "W" })
                {
                    buttons.Single(x => x.name == (direction == "E" ? "FaceEastPlate" : "FaceWestPlate")).onClick.Invoke();
                    foreach (var action in new[] { "attack", "hit", "cast" })
                    {
                        buttons.Single(x => x.name == "Preview_" + action + "Plate").onClick.Invoke();
                        Assert.That(preview.ActiveAction, Is.EqualTo(action));
                        Assert.That(preview.CurrentSprite.name, Does.StartWith("legacy-yun-jiu-jiu_archived_" + action + "_" + direction));
                        typeof(PetArchivePreview).GetField("_elapsed", Hidden).SetValue(preview, 2f);
                        typeof(PetArchivePreview).GetMethod("Update", Hidden).Invoke(preview, null);
                        Assert.That(preview.ActiveAction, Is.EqualTo("idle"));
                        Assert.That(preview.CurrentSprite.name, Does.StartWith("legacy-yun-jiu-jiu_archived_idle_" + direction));
                        Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(baseline + 1));
                    }
                }
                go.SetActive(false);
                Assert.That(preview.CurrentSprite, Is.Null);
            }
            finally { Object.DestroyImmediate(go); }
            Assert.That(QdaoActionResources.ResidentClipCount, Is.EqualTo(baseline));
        }

        private static bool Declared(string id, string action, string direction)
        {
            var text = Resources.Load<TextAsset>(QdaoPetCatalog.ResourceRoot + "/" + id + "/manifest");
            Assert.That(text, Is.Not.Null, id + " delivery manifest");
            return JsonUtility.FromJson<DeliveryManifest>(text.text).clips.Any(x => x.action == action && x.direction == direction);
        }

        private static void Advance(float seconds, Action observe = null)
        {
            while (seconds > 0f)
            {
                float step = Mathf.Min(seconds, .005f);
                TweenTick.Invoke(null, new object[] { step, step });
                observe?.Invoke();
                seconds -= step;
            }
        }

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
