using System;
using System.Collections.Generic;
using System.Reflection;
using MmorpgClient.Game;
using MmorpgClient.Net;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    public sealed class ActorWorldFoundationTests
    {
        private ActorWorld _world;
        private GameObject _parent;

        [TearDown]
        public void TearDown()
        {
            if (_world != null)
            {
                _world.Clear();
                if (_world.Root != null)
                    UnityEngine.Object.DestroyImmediate(_world.Root.gameObject);
                _world = null;
            }

            if (_parent != null)
            {
                UnityEngine.Object.DestroyImmediate(_parent);
                _parent = null;
            }
        }

        [Test]
        public void SetRootParent_NormalizesPersistentActorRoot()
        {
            _world = CreateWorld();
            _world.SpawnActor(1, ActorKind.Player, 0, new Vector3(3f, 0f, 4f), Vector3.zero);
            _parent = new GameObject("ActorWorldTestParent");
            _parent.transform.position = new Vector3(10f, 0f, 20f);

            _world.SetRootParent(_parent.transform);

            Assert.That(_world.Root.parent, Is.SameAs(_parent.transform));
            Assert.That(_world.Root.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(_world.Root.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(_world.Root.localScale, Is.EqualTo(Vector3.one));
            Assert.That(_world.Actors[1].Go.transform.localPosition, Is.EqualTo(new Vector3(3f, 0f, 4f)));
        }

        [Test]
        public void LocalActor_DoesNotEnterRemoteInterpolationPath()
        {
            _world = CreateWorld();
            var start = new Vector3(5f, 0f, 7f);
            _world.SpawnActor(11, ActorKind.Player, 0, start, Vector3.zero);
            var view = _world.Actors[11];

            _world.ApplyMove(11, new Vector3(30f, 0f, 40f), Vector3.zero, Vector3.one);
            Assert.That(view.HasTarget, Is.True, "The fixture must first prove the remote path is active.");

            _world.SetLocalPlayer(11);
            Assert.That(view.HasTarget, Is.False, "Binding local ownership must cancel stale remote interpolation.");

            _world.ApplyMove(11, new Vector3(60f, 0f, 70f), Vector3.zero, Vector3.one);
            _world.Tick();

            Assert.That(view.HasTarget, Is.False);
            Assert.That(view.Go.transform.localPosition, Is.EqualTo(start));
        }

        [Test]
        public void Teleport_PreservesEnabledCharacterController()
        {
            _world = CreateWorld();
            _world.SpawnActor(21, ActorKind.Player, 0, Vector3.zero, Vector3.zero);
            var actor = _world.Actors[21].Go;
            actor.GetComponent<Collider>().enabled = false;
            var motor = actor.AddComponent<CharacterController>();
            motor.enabled = true;

            var target = new Vector3(12f, 1f, 18f);
            var euler = new Vector3(0f, 135f, 0f);
            _world.Teleport(21, target, euler);

            Assert.That(motor.enabled, Is.True);
            Assert.That(actor.transform.localPosition, Is.EqualTo(target));
            Assert.That(actor.transform.localEulerAngles.y, Is.EqualTo(euler.y).Within(0.01f));
            Assert.That(_world.Actors[21].HasTarget, Is.False);
        }

        [Test]
        public void Teleport_LocalTianyongActor_TreatsProtocolPositionAsFeet()
        {
            _world = CreateWorld();
            _world.SpawnActor(22, ActorKind.Player, 0, Vector3.zero, Vector3.zero);
            var actor = _world.Actors[22].Go;
            actor.GetComponent<Collider>().enabled = false;
            var motor = actor.AddComponent<CharacterController>();
            motor.height = 1.8f;
            motor.center = Vector3.zero;
            var movement = actor.AddComponent<TianyongPlayerController>();
            typeof(TianyongPlayerController)
                .GetField("_motor", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(movement, motor);
            _world.SetLocalPlayer(22);

            var feetTarget = new Vector3(20f, 0.25f, 25f);
            _world.Teleport(22, feetTarget, new Vector3(0f, 45f, 0f));

            Assert.That(movement.FeetPosition.x, Is.EqualTo(feetTarget.x).Within(0.001f));
            Assert.That(movement.FeetPosition.y, Is.EqualTo(feetTarget.y).Within(0.001f));
            Assert.That(movement.FeetPosition.z, Is.EqualTo(feetTarget.z).Within(0.001f));
            Assert.That(actor.transform.localEulerAngles.y, Is.EqualTo(45f).Within(0.01f));
        }

        [Test]
        public void Recolor_UsesPropertyBlockWithoutInstantiatingMaterial()
        {
            _world = CreateWorld();
            _world.SpawnActor(31, ActorKind.Player, 0, Vector3.zero, Vector3.zero);
            var renderer = _world.Actors[31].Go.GetComponent<Renderer>();
            var sharedMaterial = renderer.sharedMaterial;

            _world.SetLocalPlayer(31);

            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Assert.That(renderer.sharedMaterial, Is.SameAs(sharedMaterial));
            var actual = properties.GetColor(Shader.PropertyToID("_Color"));
            var expected = new Color(0.2f, 0.9f, 0.3f);
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.00001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.00001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.00001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.00001f));
        }

        [Test]
        public void MovementSend_WithoutConnectedGate_IsSafeNoOp()
        {
            var client = new GameClient("http://127.0.0.1:1");
            try
            {
                Assert.DoesNotThrow(() => client.SendMoveStart(
                    new Vector3(1f, 2f, 3f),
                    new Vector3(0f, 90f, 0f),
                    Vector3.forward));
                Assert.That(client.IsMoving, Is.False);

                Assert.DoesNotThrow(() => client.SendMoveSync(
                    new Vector3(2f, 2f, 4f),
                    new Vector3(0f, 90f, 0f),
                    Vector3.forward));
                Assert.DoesNotThrow(() => client.SendMoveStop(
                    new Vector3(2f, 2f, 4f),
                    new Vector3(0f, 90f, 0f)));
                Assert.That(client.IsMoving, Is.False);
            }
            finally
            {
                client.Disconnect();
                if (client.World.Root != null)
                    UnityEngine.Object.DestroyImmediate(client.World.Root.gameObject);
            }
        }

        [Test]
        public void Disconnect_ClearsStateBeforeRaisingExactlyOneNotification()
        {
            var client = new GameClient("http://127.0.0.1:1");
            try
            {
                client.World.SpawnActor(41, ActorKind.Player, 0, Vector3.zero, Vector3.zero);
                typeof(GameClient)
                    .GetField("_disconnectNotificationSent", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(client, false);

                var notifications = 0;
                client.OnDisconnected += () =>
                {
                    notifications++;
                    Assert.That(client.InGame, Is.False);
                    Assert.That(client.TokenVerified, Is.False);
                    Assert.That(client.IsMoving, Is.False);
                    Assert.That(client.IsGateReady, Is.False);
                    Assert.That(client.World.Actors, Is.Empty);
                };

                client.Disconnect();
                client.Disconnect();

                Assert.That(notifications, Is.EqualTo(1));
            }
            finally
            {
                client.Disconnect();
                if (client.World.Root != null)
                    UnityEngine.Object.DestroyImmediate(client.World.Root.gameObject);
            }
        }

        [Test]
        public void MovementEmptyReplies_AreConsumedWithoutFourHertzUnhandledLogs()
        {
            var client = new GameClient("http://127.0.0.1:1");
            var logs = new List<string>();
            client.OnLog += logs.Add;
            try
            {
                var dispatch = typeof(GameClient).GetMethod(
                    "DispatchInbound",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(dispatch, Is.Not.Null);

                foreach (var messageId in new[]
                         {
                             MessageIds.MoveStart,
                             MessageIds.MoveSync,
                             MessageIds.MoveStop,
                         })
                {
                    dispatch.Invoke(client, new object[]
                    {
                        new MessageContent { Id = 42, MessageId = messageId },
                    });
                }

                Assert.That(logs, Has.None.Contains("[unhandled]"),
                    "Expected Empty movement replies must not emit a main-thread stack trace.");

                dispatch.Invoke(client, new object[]
                {
                    new MessageContent
                    {
                        Id = 43,
                        MessageId = MessageIds.MoveSync,
                        ErrorMessage = new TipInfoMessage { Id = 42 },
                    },
                });
                Assert.That(logs, Has.Some.EqualTo("[movement] MoveSync rejected: tip=42"));

                dispatch.Invoke(client, new object[]
                {
                    new MessageContent { Id = 44, MessageId = uint.MaxValue },
                });
                Assert.That(logs, Has.Some.Contains($"[unhandled] message_id={uint.MaxValue}"),
                    "Unknown messages must remain observable.");
            }
            finally
            {
                client.Disconnect();
                if (client.World.Root != null)
                    UnityEngine.Object.DestroyImmediate(client.World.Root.gameObject);
            }
        }

        private static ActorWorld CreateWorld()
            => new($"[ActorWorld.Test.{Guid.NewGuid():N}]");
    }
}
