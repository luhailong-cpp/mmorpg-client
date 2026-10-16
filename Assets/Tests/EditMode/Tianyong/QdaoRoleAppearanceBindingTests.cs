using System.Collections.Generic;
using System.Reflection;
using Loginpb;
using MmorpgClient.Game;
using MmorpgClient.World;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class QdaoRoleAppearanceBindingTests
    {
        private GameClient _client;

        [SetUp]
        public void SetUp() => _client = new GameClient("http://127.0.0.1:1");

        [TearDown]
        public void TearDown()
        {
            _client.Disconnect();
            if (_client.World.Root != null)
                Object.DestroyImmediate(_client.World.Root.gameObject);
        }

        [TestCase(1u, 1u, "04_mountain_guardian_boy")]
        [TestCase(1u, 2u, "01_ice_sword_girl")]
        [TestCase(2u, 1u, "06_thunder_caster_boy")]
        [TestCase(2u, 2u, "20_star_formation_master_girl")]
        [TestCase(3u, 1u, "08_alchemy_prodigy_boy")]
        [TestCase(3u, 2u, "03_lotus_healer_girl")]
        [TestCase(4u, 1u, "00_reference_topright_boy")]
        [TestCase(4u, 2u, "09_bamboo_archer_girl")]
        public void NetworkSpawn_UsesStablePlayerGuidAndAuthoritativeRole(uint classId, uint gender, string expected)
        {
            Cache(new AccountSimplePlayer { PlayerId = 812345, ClassId = classId, Gender = gender });
            SpawnNetworkActor(0, 812345);
            var actor = _client.World.Actors[0];
            Assert.That(actor.PlayerId, Is.EqualTo(812345UL));
            Assert.That(actor.CharacterId, Is.EqualTo(expected));
            Assert.That(actor.CharacterId, Is.EqualTo(QdaoCharacterCatalog.ResolveRole(classId, gender)),
                "Role selection and world loading must use the same appearance mapping.");

            _client.World.DespawnActor(0);
            SpawnNetworkActor(7001, 812345);
            Assert.That(_client.World.Actors[7001].CharacterId, Is.EqualTo(expected),
                "Respawning with another scene entity handle must preserve the player's appearance.");
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
        public void RetiredAppearance_UsesCurrentArtAcrossAccountAndAoiWithoutRewritingSavedIdentity(string saved, string expected)
        {
            var role = new AccountSimplePlayer { PlayerId = 101, ClassId = 4, Gender = 2, AppearanceId = saved };
            Cache(role);
            SpawnNetworkActor(7, 101);
            Assert.That(_client.ResolveCharacterId(101), Is.EqualTo(expected));
            Assert.That(_client.World.Actors[7].CharacterId, Is.EqualTo(expected));
            Assert.That(role.AppearanceId, Is.EqualTo(saved));
            Assert.That(role.ClassId, Is.EqualTo(4u));
            Assert.That(role.Gender, Is.EqualTo(2u));
            var cachedRoles = (Dictionary<ulong, AccountSimplePlayer>)typeof(GameClient)
                .GetField("_knownRoles", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(_client);
            Assert.That(cachedRoles, Is.Not.Null);
            Assert.That(cachedRoles[101].AppearanceId, Is.EqualTo(saved));
            Assert.That(cachedRoles[101].ClassId, Is.EqualTo(4u));
            Assert.That(cachedRoles[101].Gender, Is.EqualTo(2u));

            var remote = new ActorCreateS2C
                { Entity = 8, Guid = 102, ActorType = ActorType.Player, ClassId = 4, Gender = 2, AppearanceId = saved };
            InvokeClient("SpawnActorView", remote);
            Assert.That(_client.ResolveCharacterId(102), Is.EqualTo(expected));
            Assert.That(_client.World.Actors[8].CharacterId, Is.EqualTo(expected));
            Assert.That(remote.AppearanceId, Is.EqualTo(saved));
            Assert.That(remote.ClassId, Is.EqualTo(4u));
            Assert.That(remote.Gender, Is.EqualTo(2u));

            _client.World.DespawnActor(7);
            SpawnNetworkActor(70, 101);
            Assert.That(_client.World.Actors[70].CharacterId, Is.EqualTo(expected));
        }

        [Test]
        public void LateRoleMetadata_RefreshesAlreadySpawnedActor()
        {
            SpawnNetworkActor(51, 10001);
            Assert.That(_client.World.Actors[51].CharacterId, Is.Null);
            Cache(new AccountSimplePlayer { PlayerId = 10001, ClassId = 3, Gender = 2 });
            Assert.That(_client.World.Actors[51].CharacterId, Is.EqualTo("03_lotus_healer_girl"));
        }

        [Test]
        public void UnknownRemote_DoesNotGuessAppearanceFromEntityOrConfigId()
        {
            Cache(new AccountSimplePlayer { PlayerId = 10001, ClassId = 1, Gender = 1 });
            _client.World.SpawnActor(10001, ActorKind.Player, 24, Vector3.zero, Vector3.zero, 99999);
            Assert.That(_client.World.Actors[10001].CharacterId, Is.Null);
            Assert.That(_client.ResolveCharacterId(99999), Is.Null);
        }

        [Test]
        public void LocalBindingAfterSpawn_RefreshesLegacyGuidlessActorAndPreviousOwner()
        {
            Cache(new AccountSimplePlayer { PlayerId = 10001, ClassId = 2, Gender = 1 });
            typeof(GameClient).GetProperty(nameof(GameClient.PlayerId))?.SetValue(_client, 10001UL);
            _client.World.SpawnActor(0, ActorKind.Player, 0, Vector3.zero, Vector3.zero);
            _client.World.SpawnActor(7, ActorKind.Player, 0, Vector3.zero, Vector3.zero);
            Assert.That(_client.World.Actors[0].CharacterId, Is.Null);

            _client.World.SetLocalPlayer(0);
            Assert.That(_client.World.Actors[0].CharacterId, Is.EqualTo("06_thunder_caster_boy"));
            _client.World.SetLocalPlayer(7);
            Assert.That(_client.World.Actors[7].CharacterId, Is.EqualTo("06_thunder_caster_boy"));
            Assert.That(_client.World.Actors[0].CharacterId, Is.Null,
                "A guidless actor that loses local ownership must not retain the prior player's appearance.");
        }

        [Test]
        public void AuthoritativeRoleList_ClonesMetadataAndReplacesStaleEntries()
        {
            var role = new AccountSimplePlayer { PlayerId = 10001, ClassId = 1, Gender = 1 };
            Cache(role);
            role.ClassId = 4;
            SpawnNetworkActor(55, 10001);
            Assert.That(_client.World.Actors[55].CharacterId, Is.EqualTo("04_mountain_guardian_boy"));

            Cache(new AccountSimplePlayer { PlayerId = 10001, ClassId = 3, Gender = 2 });
            Assert.That(_client.World.Actors[55].CharacterId, Is.EqualTo("03_lotus_healer_girl"));
            Cache(new AccountSimplePlayer { PlayerId = 20001, ClassId = 4, Gender = 1 });
            Assert.That(_client.ResolveCharacterId(10001), Is.Null);
            Assert.That(_client.World.Actors[55].CharacterId, Is.Null);
        }

        [Test]
        public void Disconnect_ClearsAccountAppearanceMetadata()
        {
            Cache(new AccountSimplePlayer { PlayerId = 10001, ClassId = 1, Gender = 2 });
            _client.Disconnect();
            Assert.That(_client.ResolveCharacterId(10001), Is.Null);
        }

        private void Cache(params AccountSimplePlayer[] roles)
        {
            var wrapped = new AccountSimplePlayerWrapper[roles.Length];
            for (int i = 0; i < roles.Length; i++)
                wrapped[i] = new AccountSimplePlayerWrapper { Player = roles[i] };
            InvokeClient("CacheRoleMetadata", (object)wrapped);
        }

        private void SpawnNetworkActor(ulong entity, ulong playerId)
            => InvokeClient("SpawnActorView", new ActorCreateS2C
            {
                Entity = entity,
                Guid = playerId,
                ActorType = ActorType.Player,
            });

        private void InvokeClient(string method, object argument)
        {
            var entry = typeof(GameClient).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(entry, Is.Not.Null);
            entry.Invoke(_client, new[] { argument });
        }
    }
}
