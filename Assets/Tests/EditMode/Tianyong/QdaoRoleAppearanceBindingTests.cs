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

        [TestCase(1u, 1u, "24_lu_dongbin")]
        [TestCase(1u, 2u, "23_lantern_courier")]
        [TestCase(2u, 1u, "30_han_xiangzi")]
        [TestCase(2u, 2u, "28_moon_rabbit_artificer")]
        [TestCase(3u, 1u, "27_ink_kite_ranger")]
        [TestCase(3u, 2u, "29_he_xiangu")]
        [TestCase(4u, 1u, "25_lion_drum_guard")]
        [TestCase(4u, 2u, "26_osmanthus_healer")]
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

        [Test]
        public void LateRoleMetadata_RefreshesAlreadySpawnedActor()
        {
            SpawnNetworkActor(51, 10001);
            Assert.That(_client.World.Actors[51].CharacterId, Is.Null);
            Cache(new AccountSimplePlayer { PlayerId = 10001, ClassId = 3, Gender = 2 });
            Assert.That(_client.World.Actors[51].CharacterId, Is.EqualTo("29_he_xiangu"));
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
            Assert.That(_client.World.Actors[0].CharacterId, Is.EqualTo("30_han_xiangzi"));
            _client.World.SetLocalPlayer(7);
            Assert.That(_client.World.Actors[7].CharacterId, Is.EqualTo("30_han_xiangzi"));
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
            Assert.That(_client.World.Actors[55].CharacterId, Is.EqualTo("24_lu_dongbin"));

            Cache(new AccountSimplePlayer { PlayerId = 10001, ClassId = 3, Gender = 2 });
            Assert.That(_client.World.Actors[55].CharacterId, Is.EqualTo("29_he_xiangu"));
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
