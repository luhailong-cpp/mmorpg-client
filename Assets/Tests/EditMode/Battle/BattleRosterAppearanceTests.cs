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
            public void Dispose()
            {
                // EditMode 不调度运行时 OnDestroy；先走组件的真实释放入口。
                try { if (Component != null) Component.Clear(); }
                finally { UnityEngine.Object.DestroyImmediate(_object); }
            }
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
            Assert.That(id, Is.EqualTo("03_lotus_healer_girl"));
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

        [TestCase(null, "00_reference_topright_boy")]
        [TestCase("", "00_reference_topright_boy")]
        [TestCase("24_crane_hermit", "24_crane_hermit")]
        [TestCase("future_unavailable_identity", "future_unavailable_identity")]
        public void MissingAppearanceUsesCurrentDefaultButExplicitUnknownIdentityIsPreserved(string resolved, string expected)
        {
            var actor = new BattleActorState { ActorId = 501, ActorType = eBattleActorType.BattleActorTypePlayer };
            Assert.That(BattleArtCatalog.CharacterIdFor(actor, _ => resolved),
                Is.EqualTo(expected));
            if (string.IsNullOrEmpty(resolved)) return;
            Assert.That(BattleArtCatalog.LoadPlayerPortrait(actor, _ => resolved), Is.Null);
            Assert.That(BattleArtCatalog.LoadPlayerWalk(resolved, true), Is.Null);
        }

        [TestCase("QdaoHeadbandBoy", "00_reference_topright_boy")]
        [TestCase("11_jade_fist_flat_top_boy", "04_mountain_guardian_boy")]
        [TestCase("12_iron_saber_flat_top_boy", "00_reference_topright_boy")]
        [TestCase("13_short_hair_wind_blade_girl", "01_ice_sword_girl")]
        [TestCase("16_golden_bell_dancer_girl", "05_celestial_musician_girl")]
        [TestCase("18_desert_sun_monk_girl", "10_crimson_spear_girl")]
        [TestCase("19_spirit_beast_tamer_boy", "06_thunder_caster_boy")]
        [TestCase("21_lidazui_hair_cook_boy", "08_alchemy_prodigy_boy")]
        [TestCase("22_lidazui_hair_waiter_saber_boy", "00_reference_topright_boy")]
        [TestCase("23_lantern_courier", "01_ice_sword_girl")]
        [TestCase("24_lu_dongbin", "04_mountain_guardian_boy")]
        [TestCase("25_lion_drum_guard", "00_reference_topright_boy")]
        [TestCase("26_osmanthus_healer", "09_bamboo_archer_girl")]
        [TestCase("27_ink_kite_ranger", "08_alchemy_prodigy_boy")]
        [TestCase("28_moon_rabbit_artificer", "20_star_formation_master_girl")]
        [TestCase("29_he_xiangu", "03_lotus_healer_girl")]
        [TestCase("30_han_xiangzi", "06_thunder_caster_boy")]
        public void RetiredSnapshotAndAccountIdentityResolveToSameCurrentBodyAndPortrait(string saved, string expected)
        {
            var actor = new BattleActorState
                { ActorId = 501, ActorType = eBattleActorType.BattleActorTypePlayer, AppearanceId = saved, ClassId = 4, Gender = 2 };
            Assert.That(BattleArtCatalog.CharacterIdFor(actor, _ => "future_unavailable_identity"), Is.EqualTo(expected));
            Assert.That(actor.AppearanceId, Is.EqualTo(saved));
            Assert.That(actor.ClassId, Is.EqualTo(4u));
            Assert.That(actor.Gender, Is.EqualTo(2u));
            var portrait = QdaoCharacterCatalog.LoadPortrait(expected);
            Assert.That(portrait, Is.Not.Null, expected);
            Assert.That(BattleArtCatalog.LoadPlayerPortrait(actor, null), Is.SameAs(portrait));
            using var walk = BattleArtCatalog.LoadPlayerWalk(saved, true);
            Assert.That(walk, Is.Not.Null, expected);
            Assert.That(walk.Frames[0].name, Does.StartWith(expected + "_"));

            actor.AppearanceId = "";
            actor.ClassId = 0;
            actor.Gender = 0;
            Assert.That(BattleArtCatalog.CharacterIdFor(actor, _ => saved), Is.EqualTo(expected));
            Assert.That(BattleArtCatalog.LoadPlayerPortrait(actor, _ => saved), Is.SameAs(portrait));
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
                if (walk.UseWorldGeometry)
                {
                    Assert.That(walk.Count, Is.EqualTo(16), entry.Id);
                    Assert.That(walk.DurationSeconds, Is.EqualTo(QdaoActionResources.GetDurationSeconds(
                        entry.Id, "run", east ? "E" : "W")).Within(.001f), entry.Id);
                }
                else
                {
                    Assert.That(walk.Count, Is.EqualTo(entry.FrameCount), entry.Id);
                    Assert.That(walk.Fps, Is.EqualTo(entry.FramesPerSecond).Within(.001f), entry.Id);
                }
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
                string direction = east ? "E" : "W";
                AssertGeometry(idle.Frames[0], appearance.GeometryForResource(appearance.IdleResourcePath(direction)), entry.Id);
                if (!walk.UseWorldGeometry)
                    for (int frame = 0; frame < walk.Count; frame++)
                        AssertGeometry(walk.Frames[frame], appearance.GeometryForResource(appearance.FrameResourcePath(direction, frame)),
                            entry.Id + "/walk/" + direction + "/" + frame);
            }
            using var first = BattleArtCatalog.LoadCharacterAction("00_reference_topright_boy", "idle", true);
            using var second = BattleArtCatalog.LoadCharacterAction("01_ice_sword_girl", "idle", true);
            Assert.That(first.Frames[0].texture, Is.Not.SameAs(second.Frames[0].texture));
        }

        private static void AssertGeometry(UnityEngine.Sprite sprite, QdaoMixedResolutionContract.Geometry geometry, string identity)
        {
            Assert.That(sprite.texture.width, Is.EqualTo(geometry.Width), identity);
            Assert.That(sprite.texture.height, Is.EqualTo(geometry.Height), identity);
            Assert.That(sprite.rect.width, Is.EqualTo((float)geometry.Width), identity);
            Assert.That(sprite.rect.height, Is.EqualTo((float)geometry.Height), identity);
            Assert.That(sprite.pixelsPerUnit, Is.EqualTo(geometry.PixelsPerUnit), identity);
            Assert.That(sprite.pivot.x, Is.EqualTo(geometry.Pivot.x * geometry.Width).Within(.001f), identity);
            Assert.That(sprite.pivot.y, Is.EqualTo(geometry.Pivot.y * geometry.Height).Within(.001f), identity);
        }

        [Test]
        public void UnapprovedOriginalIdsDoNotBorrowRosterBodiesOrPortraitsInBattle()
        {
            var actor = new BattleActorState { ActorId = 101, ActorType = eBattleActorType.BattleActorTypePlayer };
            foreach (var entry in QdaoCharacterCatalog.RetainedOriginalAll)
            {
                Assert.That(BattleArtCatalog.CharacterIdFor(actor, _ => entry.Id), Is.EqualTo(entry.Id));
                if (entry.ResolveAppearance() != null) continue;
                Assert.That(BattleArtCatalog.LoadPlayerWalk(entry.Id, true), Is.Null);
                Assert.That(QdaoCharacterCatalog.LoadPortrait(entry.Id), Is.Null);
                var portrait = BattleArtCatalog.LoadPlayerPortrait(actor, _ => entry.Id);
                Assert.That(portrait, Is.Null, entry.Id);
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
            {
                using var actual = BattleArtCatalog.LoadCharacterAction(entry.Id, action, east);
                if (QdaoActionResources.GetDurationSeconds(entry.Id, action, east ? "E" : "W") <= 0f)
                    Assert.That(actual, Is.Null, entry.Id + "/" + action);
                else
                {
                    Assert.That(actual, Is.Not.Null, entry.Id + "/" + action);
                    Assert.That(actual.UseWorldGeometry, Is.True);
                    Assert.That(actual.Mirrored, Is.False);
                    foreach (var frame in actual.Frames)
                        Assert.That(frame.name, Does.StartWith(entry.Id + "_archived_" + action + "_" + (east ? "E" : "W") + "_"));
                }
            }
        }

        [Test]
        public void PlayerPortraitBeforeSnapshotUsesCurrentDefaultInsteadOfActorIdRotation()
        {
            var expected = QdaoCharacterCatalog.LoadPortrait("00_reference_topright_boy");
            Assert.That(expected, Is.Not.Null);
            foreach (ulong actorId in new[] { 0UL, 1UL, 101UL, 999UL })
                Assert.That(BattleArtCatalog.LoadPlayerPortrait(actorId), Is.SameAs(expected));
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
