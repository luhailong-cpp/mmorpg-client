using System.Collections.Generic;
using MmorpgClient.UI.Ugui.Battle;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// HUD 纯逻辑测试:行动预告序(服务端序 / 速度回退)、命令环排布、头像分派、角色卡顺序、PVP 判定。
    /// </summary>
    public sealed class BattleHudLogicTests
    {
        private static BattleActorState Actor(ulong id, uint team, ulong speed = 0, bool dead = false, bool fled = false,
            eBattleActorType type = eBattleActorType.BattleActorTypePlayer)
        {
            return new BattleActorState
            {
                ActorId = id,
                TeamIndex = team,
                IsDead = dead,
                Fled = fled,
                ActorType = type,
                Attributes = new BaseAttributesComp { Speed = speed },
            };
        }

        [Test]
        public void ResolveActionOrder_PrefersServerOrder_AndDropsUnknownIds()
        {
            var actors = new List<BattleActorState> { Actor(1, 0, 10), Actor(2, 1, 50), Actor(3, 0, 30) };
            var order = BattleHudLogic.ResolveActionOrder(actors, new List<ulong> { 3, 99, 1, 2 });
            CollectionAssert.AreEqual(new List<ulong> { 3, 1, 2 }, order);
        }

        [Test]
        public void ResolveActionOrder_FallsBackToSpeedDesc_SkippingDeadAndFled()
        {
            var actors = new List<BattleActorState>
            {
                Actor(1, 0, 10), Actor(2, 1, 50), Actor(3, 0, 30), Actor(4, 1, 99, dead: true), Actor(5, 0, 30), Actor(6, 1, 70, fled: true),
            };
            var order = BattleHudLogic.ResolveActionOrder(actors, new List<ulong>());
            // 同速(3 与 5)按 actor_id 升序
            CollectionAssert.AreEqual(new List<ulong> { 2, 3, 5, 1 }, order);
            CollectionAssert.AreEqual(order, BattleHudLogic.ResolveActionOrder(actors, null));
        }

        [Test]
        public void ResolveActionOrder_EmptyInputs_DoNotThrow()
        {
            Assert.AreEqual(0, BattleHudLogic.ResolveActionOrder(null, null).Count);
            // 没有 actors 但有服务端序:原样透传(不知道谁是谁也先排上)
            CollectionAssert.AreEqual(new List<ulong> { 7, 8 }, BattleHudLogic.ResolveActionOrder(null, new List<ulong> { 7, 8 }));
        }

        [Test]
        public void RingPosition_StartsAtTop_ClockwiseEvenlySpaced_OnRadius()
        {
            const float r = 160f;
            var positions = new List<Vector2>();
            for (int i = 0; i < BattleHudLogic.CommandCount; i++)
                positions.Add(BattleHudLogic.RingPosition(i, BattleHudLogic.CommandCount, r));

            Assert.AreEqual(0f, positions[0].x, 1e-3f);
            Assert.AreEqual(-r, positions[0].y, 1e-3f, "0 号在正上方(y 向下为正)");
            Assert.Greater(positions[1].x, 0f, "顺时针:1 号在右侧");
            foreach (var p in positions) Assert.AreEqual(r, p.magnitude, 1e-2f);
            for (int i = 0; i < positions.Count; i++)
            {
                for (int j = i + 1; j < positions.Count; j++)
                    Assert.Greater(Vector2.Distance(positions[i], positions[j]), 100f, $"{i} 与 {j} 过近");
            }
            Assert.AreEqual(Vector2.zero, BattleHudLogic.RingPosition(0, 0, r));
        }

        [Test]
        public void PortraitIndex_IsStable_AndInRange()
        {
            var seen = new HashSet<int>();
            for (ulong id = 1; id <= 200; id++)
            {
                int a = BattleHudLogic.PortraitIndexFor(id, 22);
                int b = BattleHudLogic.PortraitIndexFor(id, 22);
                Assert.AreEqual(a, b);
                Assert.GreaterOrEqual(a, 0);
                Assert.Less(a, 22);
                seen.Add(a);
            }
            Assert.Greater(seen.Count, 10, "200 个 id 应散到多张立绘上");
            Assert.AreEqual(0, BattleHudLogic.PortraitIndexFor(5, 0));
        }

        [Test]
        public void PartyCardOrder_SelfFirst_ThenTeammatesById_Capped()
        {
            var actors = new List<BattleActorState>
            {
                Actor(9, 0), Actor(3, 0), Actor(5, 1), Actor(1, 0), Actor(7, 0), Actor(2, 0),
            };
            var cards = BattleHudLogic.PartyCardOrder(actors, myId: 7, myTeam: 0, maxCards: 4);
            Assert.AreEqual(4, cards.Count);
            Assert.AreEqual(7UL, cards[0].ActorId);
            Assert.AreEqual(1UL, cards[1].ActorId);
            Assert.AreEqual(2UL, cards[2].ActorId);
            Assert.AreEqual(3UL, cards[3].ActorId);

            // 观战:无本人,取该队前几位
            var spectate = BattleHudLogic.PartyCardOrder(actors, myId: 0, myTeam: 1, maxCards: 4);
            Assert.AreEqual(1, spectate.Count);
            Assert.AreEqual(5UL, spectate[0].ActorId);
        }

        [Test]
        public void IsPvp_TrueOnlyWhenEnemyHasPlayer()
        {
            var pve = new List<BattleActorState> { Actor(1, 0), Actor(2, 1, type: eBattleActorType.BattleActorTypeMonster) };
            var pvp = new List<BattleActorState> { Actor(1, 0), Actor(2, 1) };
            Assert.IsFalse(BattleHudLogic.IsPvp(pve, 0));
            Assert.IsTrue(BattleHudLogic.IsPvp(pvp, 0));
            Assert.IsFalse(BattleHudLogic.IsPvp(null, 0));
        }
    }
}
