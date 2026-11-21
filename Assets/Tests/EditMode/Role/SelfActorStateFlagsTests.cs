using System.Collections.Generic;
using MmorpgClient.Game;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Role
{
    public sealed class SelfActorStateFlagsTests
    {
        [Test]
        public void ApplyFlagsUpdatesSnapshotAndNotifiesSubscribersWithExactDiff()
        {
            var client = new GameClient("http://127.0.0.1:1");
            var history = new List<(SelfActorStateSnapshot Prev, SelfActorStateSnapshot Next)>();
            client.OnSelfActorStateChanged += (prev, next) => history.Add((prev, next));

            ulong followAndMounted = (ulong)(ActorActionStateFlags.TeamFollow | ActorActionStateFlags.Mounted);
            Assert.IsTrue(client.ApplySelfActorStateFlags(followAndMounted, 0UL, 1UL));
            Assert.That(client.SelfActionStateMask, Is.EqualTo(followAndMounted));
            Assert.That(client.SelfCombatStateMask, Is.EqualTo(0UL));
            Assert.IsTrue(client.SelfActorState.IsTeamFollowing);
            Assert.IsTrue(client.SelfActorState.IsMounted);
            Assert.IsFalse(client.SelfActorState.IsInCombat);
            Assert.IsFalse(client.SelfActorState.IsSilenced);

            // 放技能打断跟随+骑乘并切入战斗，同时施加沉默
            ulong combat = (ulong)ActorActionStateFlags.Combat;
            ulong silence = (ulong)ActorCombatStateFlags.Silence;
            Assert.IsTrue(client.ApplySelfActorStateFlags(combat, silence, 2UL));
            Assert.IsTrue(client.SelfActorState.IsInCombat);
            Assert.IsTrue(client.SelfActorState.IsSilenced);
            Assert.IsFalse(client.SelfActorState.IsTeamFollowing);
            Assert.IsFalse(client.SelfActorState.IsMounted);

            Assert.That(history.Count, Is.EqualTo(2));
            Assert.That(history[0].Prev.ActionStateMask, Is.EqualTo(0UL));
            Assert.That(history[0].Next.ActionStateMask, Is.EqualTo(followAndMounted));
            Assert.That(history[1].Prev.ActionStateMask, Is.EqualTo(followAndMounted));
            Assert.That(history[1].Next.ActionStateMask, Is.EqualTo(combat));
            Assert.That(history[1].Next.CombatStateMask, Is.EqualTo(silence));
        }

        [Test]
        public void StaleSequenceIsDroppedAndUnchangedMasksSuppressEvent()
        {
            var client = new GameClient("http://127.0.0.1:1");
            int notifications = 0;
            client.OnSelfActorStateChanged += (_, __) => notifications++;

            ulong combat = (ulong)ActorActionStateFlags.Combat;
            ulong mounted = (ulong)ActorActionStateFlags.Mounted;

            Assert.IsTrue(client.ApplySelfActorStateFlags(combat, 0UL, 5UL));
            Assert.That(notifications, Is.EqualTo(1));

            // 乱序旧包与重复序号包必须丢弃，不得回退状态
            Assert.IsFalse(client.ApplySelfActorStateFlags(mounted, 0UL, 4UL));
            Assert.IsFalse(client.ApplySelfActorStateFlags(mounted, 0UL, 5UL));
            Assert.That(client.SelfActionStateMask, Is.EqualTo(combat));
            Assert.That(client.SelfActorState.StateSeq, Is.EqualTo(5UL));
            Assert.That(notifications, Is.EqualTo(1));

            // 序号递增但位图未变：更新序号但不重复触发变更事件
            Assert.IsFalse(client.ApplySelfActorStateFlags(combat, 0UL, 6UL));
            Assert.That(client.SelfActorState.StateSeq, Is.EqualTo(6UL));
            Assert.That(notifications, Is.EqualTo(1));
        }

        [Test]
        public void ResetClearsFlagsNotifiesOnceAndReopensSequenceWindowForNextScene()
        {
            var client = new GameClient("http://127.0.0.1:1");
            var events = new List<(SelfActorStateSnapshot Prev, SelfActorStateSnapshot Next)>();
            client.OnSelfActorStateChanged += (prev, next) => events.Add((prev, next));

            ulong mounted = (ulong)ActorActionStateFlags.Mounted;
            Assert.IsTrue(client.ApplySelfActorStateFlags(mounted, 0UL, 10UL));
            Assert.That(events.Count, Is.EqualTo(1));

            // 首次复位：从非零回到 0，触发一次通知
            client.ResetSelfActorStateFlags();
            Assert.That(client.SelfActionStateMask, Is.EqualTo(0UL));
            Assert.That(client.SelfCombatStateMask, Is.EqualTo(0UL));
            Assert.That(client.SelfActorState.StateSeq, Is.EqualTo(0UL));
            Assert.That(events.Count, Is.EqualTo(2));
            Assert.That(events[1].Prev.ActionStateMask, Is.EqualTo(mounted));
            Assert.That(events[1].Next.ActionStateMask, Is.EqualTo(0UL));

            // 再次复位全 0 态不再发冗余事件
            client.ResetSelfActorStateFlags();
            Assert.That(events.Count, Is.EqualTo(2));

            // 进新场景后服务端新实体从 seq=1 重新推送，必须能正常接收
            ulong combat = (ulong)ActorActionStateFlags.Combat;
            Assert.IsTrue(client.ApplySelfActorStateFlags(combat, 0UL, 1UL));
            Assert.That(client.SelfActionStateMask, Is.EqualTo(combat));
            Assert.That(events.Count, Is.EqualTo(3));
        }
    }
}
