using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using MmorpgClient.UI.Ugui.Battle;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// HUD 纯逻辑测试:行动预告序(服务端序 / 速度回退)、命令环排布、头像分派、角色卡顺序、PVP 判定、
    /// 战斗直连横幅档位、补拉后的战斗屏补刷判定与主城入口借用档位(turn-based §22 D74)。
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
        public void PartyCardOrder_SkipsPetsSoTeammatesKeepTheirCards()
        {
            // 宝宝(owner_player_id != 0)跟着主人站,不是队友;占卡的话人类队友会被挤掉
            var pet = Actor(400, 0, type: eBattleActorType.BattleActorTypePet);
            pet.OwnerPlayerId = 7UL;
            var actors = new List<BattleActorState> { Actor(7, 0), pet, Actor(1, 0), Actor(2, 0), Actor(3, 0) };

            var cards = BattleHudLogic.PartyCardOrder(actors, myId: 7, myTeam: 0, maxCards: 4);

            Assert.AreEqual(4, cards.Count);
            CollectionAssert.AreEqual(new ulong[] { 7UL, 1UL, 2UL, 3UL },
                cards.ConvertAll(a => a.ActorId));
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

        // ── 战斗直连(turn-based §22 D74) ─────────────────

        [Test]
        public void DecideChannelBanner_HiddenOutsideBattleOrWhenReady()
        {
            // 不在回合内(未开局 / 已收尾 / 观战):连不上也不显示,横幅只服务本人参战的回合
            Assert.AreEqual(BattleChannelBanner.Hidden, BattleHudLogic.DecideChannelBanner(false, false, false, 99f));
            Assert.AreEqual(BattleChannelBanner.Hidden, BattleHudLogic.DecideChannelBanner(false, false, true, 99f));
            // 直连就绪:不显示
            Assert.AreEqual(BattleChannelBanner.Hidden, BattleHudLogic.DecideChannelBanner(true, true, false, 0f));
        }

        [Test]
        public void DecideChannelBanner_ConnectingOnlyAfterGracePeriod()
        {
            const float delay = BattleHudLogic.ChannelConnectingBannerDelaySeconds;
            // 宽限期内不亮(开局握手通常一秒内完成,免得每局开场横幅闪一下)
            Assert.AreEqual(BattleChannelBanner.Hidden, BattleHudLogic.DecideChannelBanner(true, false, false, 0f));
            Assert.AreEqual(BattleChannelBanner.Hidden, BattleHudLogic.DecideChannelBanner(true, false, false, delay - 0.01f));
            // 到期(含恰好等于宽限期)起亮「正在连接」
            Assert.AreEqual(BattleChannelBanner.Connecting, BattleHudLogic.DecideChannelBanner(true, false, false, delay));
            Assert.AreEqual(BattleChannelBanner.Connecting, BattleHudLogic.DecideChannelBanner(true, false, false, delay + 10f));
        }

        [Test]
        public void DecideChannelBanner_FailedIgnoresGracePeriod()
        {
            // 已判定连不上:不自动恢复,立即给失败横幅 +「重新连接」,不等宽限期
            Assert.AreEqual(BattleChannelBanner.Failed, BattleHudLogic.DecideChannelBanner(true, false, true, 0f));
            Assert.AreEqual(BattleChannelBanner.Failed, BattleHudLogic.DecideChannelBanner(true, false, true, 99f));
        }

        [Test]
        public void NeedsScreenResync_OnlyForNewStateObjectInRound()
        {
            var shown = new BattleStateS2C { RoundIndex = 1 };
            var pulled = new BattleStateS2C { RoundIndex = 2 };

            // 屏上就是 BattleClient.State 这个对象(开局 / 回合结果路径):不重复刷
            Assert.IsFalse(BattleHudLogic.NeedsScreenResync(true, false, BattlePhase.WaitingAction, shown, shown));
            // 直连就绪补拉换了新对象:回合内都要补刷(相位可能不变,没有事件会把它交给屏)
            Assert.IsTrue(BattleHudLogic.NeedsScreenResync(true, false, BattlePhase.WaitingAction, pulled, shown));
            Assert.IsTrue(BattleHudLogic.NeedsScreenResync(true, false, BattlePhase.Resolving, pulled, shown));
            // 按引用比较,不按内容:内容相同的新对象同样补刷一次(之后屏上即为该对象,不再重复)
            var samePayload = new BattleStateS2C { RoundIndex = 1 };
            Assert.IsTrue(BattleHudLogic.NeedsScreenResync(true, false, BattlePhase.WaitingAction, samePayload, shown));
            // 回合播放中不插手;屏没开 / 不在回合内 / 无状态 都不刷
            Assert.IsFalse(BattleHudLogic.NeedsScreenResync(true, true, BattlePhase.WaitingAction, pulled, shown));
            Assert.IsFalse(BattleHudLogic.NeedsScreenResync(false, false, BattlePhase.WaitingAction, pulled, shown));
            Assert.IsFalse(BattleHudLogic.NeedsScreenResync(true, false, BattlePhase.None, pulled, shown));
            Assert.IsFalse(BattleHudLogic.NeedsScreenResync(true, false, BattlePhase.Ended, pulled, shown));
            Assert.IsFalse(BattleHudLogic.NeedsScreenResync(true, false, BattlePhase.WaitingAction, null, shown));
        }

        [Test]
        public void DecideEntryMode_BorrowsCityEntryOnlyWhileOwnBattleIsNotOnScreen()
        {
            // 参数序:battleOpen, hasActiveBattle, channelReady, channelFailed, phase
            // 战斗屏已开(入口本就隐藏)/ 没有对局:「战斗」(排队)
            Assert.AreEqual(BattleUiRoot.BattleEntryMode.Battle,
                BattleUiRoot.DecideEntryMode(true, true, false, true, BattlePhase.None));
            Assert.AreEqual(BattleUiRoot.BattleEntryMode.Battle,
                BattleUiRoot.DecideEntryMode(false, false, false, false, BattlePhase.None));
            Assert.AreEqual(BattleUiRoot.BattleEntryMode.Battle,
                BattleUiRoot.DecideEntryMode(false, false, true, false, BattlePhase.Queued));
            // 已判定连不上:可点的「重新连接战斗」
            Assert.AreEqual(BattleUiRoot.BattleEntryMode.Reconnect,
                BattleUiRoot.DecideEntryMode(false, true, false, true, BattlePhase.None));
            // 有对局、直连未就绪(含大厅重连后首次建连、入口发起的重连):不可点的「连接战斗中…」
            Assert.AreEqual(BattleUiRoot.BattleEntryMode.Connecting,
                BattleUiRoot.DecideEntryMode(false, true, false, false, BattlePhase.None));
            // 有对局、直连已就绪而相位仍 None(就绪补拉在途,或以传输错误失败后不会再自动补拉):可点的「返回战斗」
            Assert.AreEqual(BattleUiRoot.BattleEntryMode.ReturnToBattle,
                BattleUiRoot.DecideEntryMode(false, true, true, false, BattlePhase.None));
            // 已在回合内:战斗屏由相位事件 / Update 兜底打开,入口不借用
            Assert.AreEqual(BattleUiRoot.BattleEntryMode.Battle,
                BattleUiRoot.DecideEntryMode(false, true, true, false, BattlePhase.WaitingAction));
        }
    }
}
