using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.World;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class BattleRosterAppearanceTests
    {
        private sealed class IdleLeaseOwner : System.IDisposable
        {
            private readonly UnityEngine.GameObject _object = new("BattleIdleLeaseTest");
            public QdaoHdSpriteLeaseOwner Component { get; }
            public IdleLeaseOwner() => Component = _object.AddComponent<QdaoHdSpriteLeaseOwner>();
            public void Dispose() => UnityEngine.Object.DestroyImmediate(_object);
        }

        [SetUp]
        public void SetUp() => BattleArtCatalog.ResetCaches();

        [Test]
        public void KnownPlayerUsesItsAccountRoleAndNotPetOwnerField()
        {
            var actor = new BattleActorState
            {
                ActorId = 101,
                OwnerPlayerId = 999,
                ActorType = eBattleActorType.BattleActorTypePlayer,
            };
            ulong queried = 0;
            var id = BattleArtCatalog.CharacterIdFor(actor, playerId =>
            {
                queried = playerId;
                return "29_he_xiangu";
            });
            Assert.That(queried, Is.EqualTo(101ul));
            Assert.That(id, Is.EqualTo("29_he_xiangu"));
        }

        [TestCase(eBattleActorType.BattleActorTypePet)]
        [TestCase(eBattleActorType.BattleActorTypeMonster)]
        [TestCase(eBattleActorType.BattleActorTypeNone)]
        public void NonPlayerNeverUsesItsOwnersAppearance(eBattleActorType type)
        {
            var actor = new BattleActorState { ActorId = 201, OwnerPlayerId = 101, ActorType = type };
            int calls = 0;
            var id = BattleArtCatalog.CharacterIdFor(actor, _ => { calls++; return "24_lu_dongbin"; });
            Assert.That(calls, Is.Zero);
            Assert.That(id, Is.EqualTo(BattleArtCatalog.CharacterIdFor(actor)));
        }

        [TestCase(null)]
        [TestCase("24_crane_hermit")]
        public void UnknownOrRejectedAppearanceKeepsExistingFallback(string resolved)
        {
            var actor = new BattleActorState { ActorId = 501, ActorType = eBattleActorType.BattleActorTypePlayer };
            Assert.That(BattleArtCatalog.CharacterIdFor(actor, _ => resolved),
                Is.EqualTo(BattleArtCatalog.CharacterIdFor(actor)));
        }

        [Test]
        public void EveryCharacterUsesItsOwnUnmirroredDirectionalIdleAndDeclaredWalkFrames()
        {
            using var owner = new IdleLeaseOwner();
            foreach (var entry in QdaoCharacterCatalog.AvailableAll)
            foreach (bool east in new[] { false, true })
            {
                using var walk = BattleArtCatalog.LoadPlayerWalk(entry.Id, east);
                Assert.That(walk, Is.Not.Null, entry.Id);
                Assert.That(walk.Count, Is.EqualTo(entry.FrameCount), entry.Id);
                Assert.That(walk.Fps, Is.EqualTo(entry.FramesPerSecond).Within(.001f), entry.Id);
                using var idle = BattleArtCatalog.LoadCharacterAction(entry.Id, "idle", east);
                Assert.That(idle, Is.Not.Null, entry.Id);
                Assert.That(idle.Count, Is.EqualTo(1), entry.Id);
                Assert.That(idle.Mirrored, Is.False, entry.Id);
                if (entry.HasDedicatedIdle)
                    Assert.That(idle.Frames[0].texture, Is.Not.SameAs(walk.Frames[0].texture), entry.Id);
                else
                    Assert.That(idle.Frames[0], Is.SameAs(walk.Frames[0]), entry.Id);
                var directIdle = BattleArtCatalog.LoadPlayerIdle(entry.Id, east, out var mirrored, owner.Component);
                Assert.That(directIdle, Is.SameAs(idle.Frames[0]));
                Assert.That(mirrored, Is.False);
                var contactTexture = UnityEngine.Resources.Load<UnityEngine.Texture2D>(entry.IdleResourcePath(east ? "E" : "W"));
                Assert.That(idle.Frames[0].texture, Is.SameAs(contactTexture), entry.Id);
                var appearance = entry.ResolveAppearance();
                Assert.That(idle.Frames[0].texture.width, Is.EqualTo(appearance.FrameWidth), entry.Id);
                Assert.That(idle.Frames[0].texture.height, Is.EqualTo(appearance.FrameHeight), entry.Id);
                Assert.That(idle.Frames[0].rect.width, Is.EqualTo((float)appearance.FrameWidth), entry.Id);
                if (appearance.IsHd) Assert.That(idle.Frames[0].pixelsPerUnit, Is.EqualTo(104f));
            }
            var first = BattleArtCatalog.LoadCharacterAction(QdaoCharacterCatalog.All[0].Id, "idle", true);
            var second = BattleArtCatalog.LoadCharacterAction(QdaoCharacterCatalog.All[1].Id, "idle", true);
            Assert.That(first.Frames[0].texture, Is.Not.SameAs(second.Frames[0].texture));
        }

        [Test]
        public void UnapprovedOriginalIdsDoNotBorrowRosterBodiesOrPortraitsInBattle()
        {
            var actor = new BattleActorState { ActorId = 101, ActorType = eBattleActorType.BattleActorTypePlayer };
            foreach (var entry in QdaoCharacterCatalog.OriginalAll)
            {
                Assert.That(BattleArtCatalog.CharacterIdFor(actor, _ => entry.Id), Is.EqualTo(entry.Id));
                if (entry.ResolveAppearance() != null) continue;
                Assert.That(BattleArtCatalog.LoadPlayerWalk(entry.Id, true), Is.Null);
                Assert.That(QdaoCharacterCatalog.LoadPortrait(entry.Id), Is.Null);
                var expectedPortrait = UnityEngine.Resources.Load<UnityEngine.Texture2D>(
                    BattleArtCatalog.PortraitsRoot + "/" + entry.Id + "_v3");
                var portrait = BattleArtCatalog.LoadPlayerPortrait(actor, _ => entry.Id);
                Assert.That(portrait?.texture, Is.SameAs(expectedPortrait), entry.Id);
                foreach (bool east in new[] { false, true })
                foreach (string action in BattleArtCatalog.CharacterActions)
                {
                    var path = BattleArtCatalog.CharactersRoot + "/" + entry.Id + "/" + action;
                    var direct = UnityEngine.Resources.Load<UnityEngine.Texture2D>(path + (east ? "_E_strip" : "_W_strip"));
                    var expected = direct ?? UnityEngine.Resources.Load<UnityEngine.Texture2D>(path + (east ? "_W_strip" : "_E_strip"));
                    var actual = BattleArtCatalog.LoadCharacterAction(entry.Id, action, east);
                    Assert.That(actual?.Frames[0].texture, Is.SameAs(expected), entry.Id + "/" + action);
                    if (action != "idle") continue;
                    var idle = BattleArtCatalog.LoadPlayerIdle(entry.Id, east, out var mirrored);
                    Assert.That(idle?.texture, Is.SameAs(expected), entry.Id);
                    Assert.That(mirrored, Is.EqualTo(expected != null && direct == null), entry.Id);
                }
            }
        }

        [Test]
        public void WalkArtIsNeverReturnedAsAuthoredCombatActions()
        {
            foreach (var entry in QdaoCharacterCatalog.AvailableAll)
            foreach (string action in new[] { "attack", "cast", "hit", "die", "win" })
            foreach (bool east in new[] { false, true })
                Assert.That(BattleArtCatalog.LoadCharacterAction(entry.Id, action, east), Is.Null, entry.Id + "/" + action);
        }

        [Test]
        public void BattlePortraitUsesTheSameApprovedCharacterAsTheBody()
        {
            var actor = new BattleActorState { ActorId = 101, ActorType = eBattleActorType.BattleActorTypePlayer };
            foreach (var entry in QdaoCharacterCatalog.AvailableAll)
            {
                var portrait = BattleArtCatalog.LoadPlayerPortrait(actor, _ => entry.Id);
                Assert.That(portrait, Is.Not.Null, entry.Id);
                Assert.That(portrait, Is.SameAs(QdaoCharacterCatalog.LoadPortrait(entry.Id)), entry.Id);
                Assert.That(portrait.name, Is.EqualTo(entry.Id + "_portrait"));
            }
        }
    }
}
