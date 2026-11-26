using System.Collections.Generic;
using MmorpgClient.Game;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Role
{
    [TestFixture]
    public sealed class ProtagonistPresentationHfsmTests
    {
        [Test]
        public void InitialStateAndLocomotionTransitionsWorkInFreeMode()
        {
            using var hfsm = new ProtagonistPresentationHfsm();
            var states = new List<(ProtagonistPresentationState prev, ProtagonistPresentationState next)>();
            hfsm.OnStateChanged += (prev, next) => states.Add((prev, next));

            Assert.AreEqual(ProtagonistPresentationMode.Free, hfsm.CurrentMode);
            Assert.AreEqual(ProtagonistPresentationState.Idle, hfsm.CurrentState);
            Assert.AreEqual("Root/Free/Idle", hfsm.ActiveHierarchyPath);
            Assert.IsTrue(hfsm.CanMoveLocally);
            Assert.IsTrue(hfsm.CanStartLocalActionPresentation);

            hfsm.SetMoving(true);
            Assert.AreEqual(ProtagonistPresentationState.Move, hfsm.CurrentState);
            Assert.AreEqual("Root/Free/Move", hfsm.ActiveHierarchyPath);

            hfsm.SetMoving(false);
            Assert.AreEqual(ProtagonistPresentationState.Idle, hfsm.CurrentState);

            CollectionAssert.AreEqual(
                new[]
                {
                    (ProtagonistPresentationState.Idle, ProtagonistPresentationState.Move),
                    (ProtagonistPresentationState.Move, ProtagonistPresentationState.Idle),
                },
                states);
        }

        [Test]
        public void NeedsExitTimeHoldsActionCastUntilTimerExpiresThenResumesLocomotion()
        {
            using var hfsm = new ProtagonistPresentationHfsm();
            var ended = new List<(uint skillId, bool completed, ProtagonistPresentationState stateWhenFired)>();
            hfsm.OnCastPresentationEnded += (skillId, completed) =>
                ended.Add((skillId, completed, hfsm.CurrentState));

            Assert.IsTrue(hfsm.BeginCastPresentation(1001u, 0.40f));
            Assert.AreEqual(ProtagonistPresentationState.ActionCast, hfsm.CurrentState);
            Assert.AreEqual(1001u, hfsm.ActiveCastSkillId);
            Assert.AreEqual("Root/Free/ActionCast", hfsm.ActiveHierarchyPath);

            hfsm.Tick(0.20f);
            hfsm.SetMoving(true);
            Assert.AreEqual(ProtagonistPresentationState.ActionCast, hfsm.CurrentState);
            Assert.AreEqual(0, ended.Count);

            hfsm.Tick(0.25f);
            Assert.AreEqual(ProtagonistPresentationState.Move, hfsm.CurrentState);
            Assert.AreEqual(0u, hfsm.ActiveCastSkillId);
            Assert.AreEqual(0f, hfsm.RemainingCastSec);
            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual((1001u, true, ProtagonistPresentationState.Move), ended[0]);
        }

        [Test]
        public void ServerAuthorityPreemptionIgnoresNeedsExitTimeAndConvergesImmediately()
        {
            using var hfsm = new ProtagonistPresentationHfsm();
            var ended = new List<(uint skillId, bool completed, ProtagonistPresentationState stateWhenFired)>();
            hfsm.OnCastPresentationEnded += (skillId, completed) =>
                ended.Add((skillId, completed, hfsm.CurrentState));

            hfsm.ApplyServerState(new SelfActorStateSnapshot((ulong)ActorActionStateFlags.Combat, 0UL, 1UL));
            Assert.AreEqual(ProtagonistPresentationMode.Combat, hfsm.CurrentMode);
            Assert.IsTrue(hfsm.BeginCastPresentation(2001u, 0.50f));
            Assert.AreEqual("Root/Combat/ActionCast", hfsm.ActiveHierarchyPath);

            // Silence has highest priority and must override ActionCast.needsExitTime immediately.
            hfsm.ApplyServerState(new SelfActorStateSnapshot(
                (ulong)(ActorActionStateFlags.Combat | ActorActionStateFlags.TeamFollow),
                (ulong)ActorCombatStateFlags.Silence,
                2UL));

            Assert.AreEqual(ProtagonistPresentationMode.Controlled, hfsm.CurrentMode);
            Assert.AreEqual(ProtagonistPresentationState.Controlled, hfsm.CurrentState);
            Assert.AreEqual("Root/Controlled", hfsm.ActiveHierarchyPath);
            Assert.IsFalse(hfsm.CanMoveLocally);
            Assert.IsFalse(hfsm.CanStartLocalActionPresentation);
            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual((2001u, false, ProtagonistPresentationState.Controlled), ended[0]);
            Assert.IsFalse(hfsm.BeginCastPresentation(2002u, 0.30f));

            // Clear Silence while TeamFollow remains -> converges to TeamFollow.
            hfsm.ApplyServerState(new SelfActorStateSnapshot((ulong)ActorActionStateFlags.TeamFollow, 0UL, 3UL));
            Assert.AreEqual(ProtagonistPresentationMode.TeamFollow, hfsm.CurrentMode);
            Assert.AreEqual(ProtagonistPresentationState.TeamFollow, hfsm.CurrentState);
            Assert.IsFalse(hfsm.CanMoveLocally);
            Assert.IsFalse(hfsm.BeginCastPresentation(2003u, 0.30f));

            // Switch to Mounted -> allows local movement UX cue, blocks ground cast presentation.
            hfsm.ApplyServerState(new SelfActorStateSnapshot((ulong)ActorActionStateFlags.Mounted, 0UL, 4UL));
            Assert.AreEqual(ProtagonistPresentationMode.Mounted, hfsm.CurrentMode);
            Assert.AreEqual(ProtagonistPresentationState.Mounted, hfsm.CurrentState);
            Assert.IsTrue(hfsm.CanMoveLocally);
            Assert.IsFalse(hfsm.CanStartLocalActionPresentation);

            // Exit Mounted into Combat while keeping moving flag.
            hfsm.SetMoving(true);
            hfsm.ApplyServerState(new SelfActorStateSnapshot((ulong)ActorActionStateFlags.Combat, 0UL, 5UL));
            Assert.AreEqual(ProtagonistPresentationMode.Combat, hfsm.CurrentMode);
            Assert.AreEqual(ProtagonistPresentationState.Move, hfsm.CurrentState);
            Assert.IsTrue(hfsm.CanStartLocalActionPresentation);
        }

        [Test]
        public void FreeToCombatMidCastPreservesOngoingCastPresentation()
        {
            using var hfsm = new ProtagonistPresentationHfsm();
            var ended = new List<(uint skillId, bool completed)>();
            hfsm.OnCastPresentationEnded += (skillId, completed) => ended.Add((skillId, completed));

            Assert.IsTrue(hfsm.BeginCastPresentation(3001u, 0.40f));
            Assert.AreEqual("Root/Free/ActionCast", hfsm.ActiveHierarchyPath);

            hfsm.Tick(0.15f);
            hfsm.ApplyServerState(new SelfActorStateSnapshot((ulong)ActorActionStateFlags.Combat, 0UL, 1UL));

            Assert.AreEqual(ProtagonistPresentationMode.Combat, hfsm.CurrentMode);
            Assert.AreEqual(ProtagonistPresentationState.ActionCast, hfsm.CurrentState);
            Assert.AreEqual("Root/Combat/ActionCast", hfsm.ActiveHierarchyPath);
            Assert.AreEqual(3001u, hfsm.ActiveCastSkillId);
            Assert.AreEqual(0, ended.Count);

            hfsm.Tick(0.30f);
            Assert.AreEqual(ProtagonistPresentationState.Idle, hfsm.CurrentState);
            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual((3001u, true), ended[0]);
        }

        [Test]
        public void InterruptCastPresentationBypassesNeedsExitTimeImmediately()
        {
            using var hfsm = new ProtagonistPresentationHfsm();
            var ended = new List<(uint skillId, bool completed)>();
            hfsm.OnCastPresentationEnded += (skillId, completed) => ended.Add((skillId, completed));

            Assert.IsTrue(hfsm.BeginCastPresentation(4001u, 0.60f));
            Assert.IsTrue(hfsm.InterruptCastPresentation());
            Assert.AreEqual(ProtagonistPresentationState.Idle, hfsm.CurrentState);
            Assert.AreEqual(1, ended.Count);
            Assert.AreEqual((4001u, false), ended[0]);
            Assert.IsFalse(hfsm.InterruptCastPresentation());
        }

        [Test]
        public void GameClientIntegrationTracksSelfActorStateAndResetsSymmetrically()
        {
            var client = new GameClient("http://127.0.0.1:18080");
            try
            {
                Assert.IsTrue(client.Presentation.IsBound);
                Assert.AreEqual("Root/Free/Idle", client.Presentation.ActiveHierarchyPath);

                Assert.IsTrue(client.ApplySelfActorStateFlags((ulong)ActorActionStateFlags.Combat, 0UL, 1UL));
                Assert.AreEqual(ProtagonistPresentationMode.Combat, client.Presentation.CurrentMode);
                Assert.IsTrue(client.Presentation.BeginCastPresentation(5001u, 0.50f));
                Assert.AreEqual("Root/Combat/ActionCast", client.Presentation.ActiveHierarchyPath);

                Assert.IsTrue(client.ApplySelfActorStateFlags((ulong)ActorActionStateFlags.TeamFollow, 0UL, 2UL));
                Assert.AreEqual(ProtagonistPresentationMode.TeamFollow, client.Presentation.CurrentMode);
                Assert.AreEqual(ProtagonistPresentationState.TeamFollow, client.Presentation.CurrentState);

                client.ResetSelfActorStateFlags();
                Assert.AreEqual(ProtagonistPresentationMode.Free, client.Presentation.CurrentMode);
                Assert.AreEqual(ProtagonistPresentationState.Idle, client.Presentation.CurrentState);
                Assert.AreEqual("Root/Free/Idle", client.Presentation.ActiveHierarchyPath);
            }
            finally
            {
                client.Presentation.Dispose();
                if (client.World.Root != null) UnityEngine.Object.DestroyImmediate(client.World.Root.gameObject);
            }
        }

        [Test]
        public void DisposeUnbindsFromGameClientSymmetrically()
        {
            var client = new GameClient("http://127.0.0.1:18080");
            using var listener = new ProtagonistPresentationHfsm();
            try
            {
                listener.Bind(client);
                Assert.IsTrue(listener.IsBound);

                Assert.IsTrue(client.ApplySelfActorStateFlags((ulong)ActorActionStateFlags.Combat, 0UL, 1UL));
                Assert.AreEqual(ProtagonistPresentationMode.Combat, listener.CurrentMode);

                listener.Dispose();
                Assert.IsFalse(listener.IsBound);

                Assert.IsTrue(client.ApplySelfActorStateFlags((ulong)ActorActionStateFlags.Mounted, 0UL, 2UL));
                Assert.AreEqual(ProtagonistPresentationMode.Mounted, client.Presentation.CurrentMode);
                Assert.AreEqual(ProtagonistPresentationMode.Combat, listener.CurrentMode);
            }
            finally
            {
                client.Presentation.Dispose();
                if (client.World.Root != null) UnityEngine.Object.DestroyImmediate(client.World.Root.gameObject);
            }
        }

        [Test]
        public void ControlledSilenceModeBlocksLocalControllerDestinationAndStopsQueuedWalk()
        {
            var client = new GameClient("http://127.0.0.1:18080");
            var actor = new GameObject("LocalControlledActor");
            try
            {
                var nav = new TianyongNavigationGrid();
                var camGo = new GameObject("TestCam");
                try
                {
                    var cam = camGo.AddComponent<Camera>();
                    var ctrl = actor.AddComponent<TianyongPlayerController>();
                    ctrl.Initialize(client, nav, cam);

                    string rejectedMessage = null;
                    ctrl.NavigationRejected += msg => rejectedMessage = msg;

                    Assert.IsTrue(client.ApplySelfActorStateFlags(0UL, (ulong)ActorCombatStateFlags.Silence, 1UL));
                    Assert.AreEqual(ProtagonistPresentationMode.Controlled, client.Presentation.CurrentMode);
                    Assert.IsFalse(ctrl.SetDestination(UnityEngine.Vector3.zero));
                    Assert.IsFalse(string.IsNullOrEmpty(rejectedMessage));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(camGo);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(actor);
                client.Presentation.Dispose();
                if (client.World.Root != null) UnityEngine.Object.DestroyImmediate(client.World.Root.gameObject);
            }
        }
    }
}
