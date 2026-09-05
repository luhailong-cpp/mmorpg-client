using System.Collections.Generic;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Battle;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// BattleStage 阵型位置表测试:槽位不重叠 / 后排缩放 / 镜像对称 / 上敌下我 / 在屏内 / 槽位分配回退。
    /// </summary>
    public sealed class BattleStageTests
    {
        /// <summary>任意两个脚底点的最小间距(单位名义宽 220,允许后排被前排轻微遮挡)。</summary>
        private const float MinFootDistance = 120f;

        private static IEnumerable<(bool mine, int slot)> AllSlots()
        {
            for (int s = 0; s < BattleStage.SlotsPerTeam; s++) yield return (false, s);
            for (int s = 0; s < BattleStage.SlotsPerTeam; s++) yield return (true, s);
        }

        [Test]
        public void Slots_DoNotOverlap_AcrossBothTeams()
        {
            var all = new List<(bool mine, int slot, Vector2 pos)>();
            foreach (var (mine, slot) in AllSlots()) all.Add((mine, slot, BattleStage.SlotPosition(mine, slot)));

            for (int i = 0; i < all.Count; i++)
            {
                for (int j = i + 1; j < all.Count; j++)
                {
                    float d = Vector2.Distance(all[i].pos, all[j].pos);
                    Assert.GreaterOrEqual(d, MinFootDistance,
                        $"槽位 {(all[i].mine ? "我" : "敌")}{all[i].slot} 与 {(all[j].mine ? "我" : "敌")}{all[j].slot} 距离 {d:0} 过近");
                }
            }
        }

        [Test]
        public void BackRow_IsSmallerThanFrontRow_SameColumn_OnBothSides()
        {
            // 同列比较:后排 = 前排 × 0.85 × 深度差;我方后排更靠下(深度放大)也不得超过同列前排
            foreach (bool mine in new[] { false, true })
            {
                for (int col = 0; col < BattleStage.FrontRowCount; col++)
                {
                    float front = BattleStage.SlotScale(mine, col);
                    float back = BattleStage.SlotScale(mine, col + BattleStage.FrontRowCount);
                    Assert.Less(back, front, $"{(mine ? "我" : "敌")}方第 {col} 列后排应小于前排");
                    Assert.Less(back / front, 0.97f, "后排相对前排至少缩 3%");
                }
                Assert.IsTrue(BattleStage.IsBackRow(BattleStage.FrontRowCount));
                Assert.IsFalse(BattleStage.IsBackRow(BattleStage.FrontRowCount - 1));
            }
        }

        [Test]
        public void AllScales_StayWithinReadableRange()
        {
            // 全场缩放不夸张(spec §3:0.85~1.0 量级;含 0.85 排缩放与敌方 0.95 后允许略宽)
            foreach (var (mine, slot) in AllSlots())
            {
                float s = BattleStage.SlotScale(mine, slot);
                Assert.GreaterOrEqual(s, 0.7f, $"槽 {slot} 过小");
                Assert.LessOrEqual(s, 1.1f, $"槽 {slot} 过大");
            }
        }

        [Test]
        public void EnemyTeam_IsSmallerThanAllyTeam_SameSlot_AndBackRowIsAtMost085()
        {
            // 近大远小(帧验收 2026-09-04):敌方整体 ×0.95,前排 1.0 → 后排 0.85(spec §3)
            Assert.LessOrEqual(BattleStage.BackRowScale, 0.85f + 1e-4f);
            Assert.Less(BattleStage.EnemyTeamScale, 1f);
            for (int slot = 0; slot < BattleStage.SlotsPerTeam; slot++)
            {
                Assert.Less(BattleStage.SlotScale(false, slot), BattleStage.SlotScale(true, slot),
                    $"槽 {slot}:敌方(远端)应小于我方(近端)");
            }
            // 敌方后排每个槽都 < 敌方前排每个槽(不只同列):后排整排明显小一圈
            float minFront = float.MaxValue, maxBack = 0f;
            for (int col = 0; col < BattleStage.FrontRowCount; col++)
            {
                minFront = Mathf.Min(minFront, BattleStage.SlotScale(false, col));
                maxBack = Mathf.Max(maxBack, BattleStage.SlotScale(false, col + BattleStage.FrontRowCount));
            }
            Assert.Less(maxBack, minFront, "敌方后排最大缩放应小于前排最小缩放");
        }

        [Test]
        public void NearIsBigger_LowerOnScreenScalesUp()
        {
            // 同排内脚底 y 越大缩放越大(近大远小)
            for (int col = 1; col < BattleStage.FrontRowCount; col++)
            {
                var a = BattleStage.SlotPosition(false, col - 1);
                var b = BattleStage.SlotPosition(false, col);
                float sa = BattleStage.SlotScale(false, col - 1), sb = BattleStage.SlotScale(false, col);
                if (a.y > b.y) Assert.Greater(sa, sb); else Assert.Greater(sb, sa);
            }
        }

        [Test]
        public void Formation_IsMirroredAcrossCenterLine_ThenShiftedAlongRow()
        {
            // 我方槽位 = 敌方同槽位以中线(过 Center、方向 RowStep)为轴的镜像,再沿排方向错开 2×TeamRowShift:
            // 差向量的垂直分量 = 2×排到中线距离,沿排分量 = 2×TeamRowShift(我方往右上),中点仍在中线上
            var along = BattleStage.RowDirection;
            var perpToMine = -BattleStage.EnemyOutward;
            for (int slot = 0; slot < BattleStage.SlotsPerTeam; slot++)
            {
                var enemy = BattleStage.SlotPosition(false, slot);
                var mine = BattleStage.SlotPosition(true, slot);
                var diff = mine - enemy;
                float expected = 2f * (BattleStage.IsBackRow(slot) ? BattleStage.BackRowOffset : BattleStage.FrontRowOffset);
                Assert.AreEqual(expected, Vector2.Dot(diff, perpToMine), 1e-2f, $"槽 {slot} 到中线距离应对称");
                Assert.AreEqual(2f * BattleStage.TeamRowShift, Vector2.Dot(diff, along), 1e-2f, $"槽 {slot} 沿排错位应为 2×TeamRowShift");
                // 中点落在中线上
                var mid = (enemy + mine) * 0.5f;
                Assert.AreEqual(0f, Vector2.Dot(mid - BattleStage.Center, BattleStage.EnemyOutward), 1e-2f);
            }
        }

        [Test]
        public void TwoDiagonalBands_OffsetHorizontally_AndSpanMostOfScreen()
        {
            // 帧验收 2026-09-04:两队中心横向错开 ≥18% 屏宽(录像约 40%,本机受 HUD 带限制),
            // 战场(脚底点)横跨 ≥50% 屏宽,两条斜带之间有一条空隙(前排之间垂直于排方向的距离 ≥ 2×FrontRowOffset)
            Vector2 enemyCenter = Vector2.zero, allyCenter = Vector2.zero;
            for (int s = 0; s < BattleStage.SlotsPerTeam; s++)
            {
                enemyCenter += BattleStage.SlotPosition(false, s) / BattleStage.SlotsPerTeam;
                allyCenter += BattleStage.SlotPosition(true, s) / BattleStage.SlotsPerTeam;
            }
            Assert.GreaterOrEqual(allyCenter.x - enemyCenter.x, 0.18f * QdaoUguiTheme.DesignWidth, "两队中心横向错位不足");
            Assert.GreaterOrEqual(allyCenter.y - enemyCenter.y, 150f, "我方整体应明显更靠下");

            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var (mine, slot) in AllSlots())
            {
                var p = BattleStage.SlotPosition(mine, slot);
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
            }
            Assert.GreaterOrEqual(maxX - minX, 0.5f * QdaoUguiTheme.DesignWidth, "战场横向铺开不足屏宽一半");

            // 排方向确实是"右上→左下"的斜带(斜率 10°~35°)
            float angle = Mathf.Atan2(-BattleStage.RowStep.y, BattleStage.RowStep.x) * Mathf.Rad2Deg;
            Assert.GreaterOrEqual(angle, 10f);
            Assert.LessOrEqual(angle, 35f);
            // 同排相邻槽间距 ≥ 170,让 5 人一排铺开
            Assert.GreaterOrEqual(BattleStage.RowStep.magnitude, 170f);
        }

        [Test]
        public void Enemy_UpperLeft_Ally_LowerRight()
        {
            Vector2 enemySum = Vector2.zero, allySum = Vector2.zero;
            for (int s = 0; s < BattleStage.SlotsPerTeam; s++)
            {
                enemySum += BattleStage.SlotPosition(false, s);
                allySum += BattleStage.SlotPosition(true, s);
            }
            Assert.Less(enemySum.y, allySum.y, "敌方整体应更靠上");
            Assert.Less(enemySum.x, allySum.x, "敌方整体应更靠左");
            // 后排比前排更靠外(离中线更远)
            var frontE = BattleStage.SlotPosition(false, 2);
            var backE = BattleStage.SlotPosition(false, 7);
            Assert.Less(backE.y, frontE.y, "敌方后排应更靠上");
        }

        [Test]
        public void AllSlots_AreInsideDesignScreen_WithRoomForSprite()
        {
            foreach (var (mine, slot) in AllSlots())
            {
                var p = BattleStage.SlotPosition(mine, slot);
                float scale = BattleStage.SlotScale(mine, slot);
                Assert.GreaterOrEqual(p.x - BattleStage.UnitWidth * 0.5f * scale, 0f);
                Assert.LessOrEqual(p.x + BattleStage.UnitWidth * 0.5f * scale, QdaoUguiTheme.DesignWidth);
                Assert.GreaterOrEqual(p.y - BattleStage.UnitHeight * scale, 0f, $"槽 {slot} 头顶出屏");
                Assert.LessOrEqual(p.y + 40f, QdaoUguiTheme.DesignHeight, $"槽 {slot} 脚下名字出屏");
            }
        }

        [Test]
        public void AllSlots_KeepClearOfTopAndBottomHudBands()
        {
            // 名牌顶(脚底 − OverheadReach×缩放,含头顶 HP/MP 条与 buff 行)必须在顶部预告条/相位文字之下;
            // 脚下名字(脚底 + 40)必须在底部目标提示/确认条之上
            foreach (var (mine, slot) in AllSlots())
            {
                var p = BattleStage.SlotPosition(mine, slot);
                float scale = BattleStage.SlotScale(mine, slot);
                float plateTop = p.y - BattleUnitView.OverheadReach * scale;
                Assert.GreaterOrEqual(plateTop, BattleStage.HudTopBand,
                    $"{(mine ? "我" : "敌")}方槽 {slot} 名牌顶 {plateTop:0} 进入顶部 HUD 带({BattleStage.HudTopBand})");
                Assert.LessOrEqual(p.y + 40f, BattleStage.HudBottomBand,
                    $"{(mine ? "我" : "敌")}方槽 {slot} 名字底 {p.y + 40f:0} 进入底部 HUD 带({BattleStage.HudBottomBand})");
            }
            // 单位包围盒(含名字)不压右下命令环(圆)与右上角色卡(矩形)
            var ringCenter = new Vector2(BattleCommandRing.CenterX, BattleCommandRing.CenterY);
            float ringRadius = BattleCommandRing.RingSize * 0.5f;
            var cards = new Rect(QdaoUguiTheme.DesignWidth - BattlePartyCards.RightMargin - BattlePartyCards.CardWidth, 0f,
                BattlePartyCards.CardWidth + BattlePartyCards.RightMargin,
                BattlePartyCards.Top + BattlePartyCards.MaxCards * (BattlePartyCards.CardHeight + BattlePartyCards.Gap));
            foreach (var (mine, slot) in AllSlots())
            {
                var p = BattleStage.SlotPosition(mine, slot);
                float scale = BattleStage.SlotScale(mine, slot);
                var box = Rect.MinMaxRect(p.x - BattleStage.UnitWidth * 0.5f * scale, p.y - BattleStage.UnitHeight * scale,
                    p.x + BattleStage.UnitWidth * 0.5f * scale, p.y + 40f);
                // 盒到圆心的最近点距离 ≥ 半径 → 不相交
                var closest = new Vector2(Mathf.Clamp(ringCenter.x, box.xMin, box.xMax), Mathf.Clamp(ringCenter.y, box.yMin, box.yMax));
                Assert.GreaterOrEqual(Vector2.Distance(closest, ringCenter), ringRadius,
                    $"{(mine ? "我" : "敌")}方槽 {slot} 压到命令环");
                Assert.IsFalse(box.Overlaps(cards), $"{(mine ? "我" : "敌")}方槽 {slot} 压到右上角色卡");
            }
        }

        [Test]
        public void SlotIndex_ColumnOrder_LeftToRight()
        {
            for (int col = 1; col < BattleStage.FrontRowCount; col++)
            {
                Assert.Greater(BattleStage.SlotPosition(false, col).x, BattleStage.SlotPosition(false, col - 1).x);
                Assert.Greater(BattleStage.SlotPosition(true, col).x, BattleStage.SlotPosition(true, col - 1).x);
                Assert.AreEqual(col, BattleStage.Column(col + BattleStage.FrontRowCount));
            }
            // 越界槽位不崩,取模回落
            Assert.AreEqual(BattleStage.SlotPosition(false, 2), BattleStage.SlotPosition(false, 12));
            Assert.AreEqual(BattleStage.SlotPosition(false, 0), BattleStage.SlotPosition(false, -5));
        }

        [Test]
        public void DepthOrder_SortsByFootY()
        {
            var a = new Vector2(100f, 300f);
            var b = new Vector2(900f, 500f);
            Assert.Less(BattleStage.CompareDepth(a, b), 0);
            Assert.Greater(BattleStage.CompareDepth(b, a), 0);
            Assert.AreEqual(0, BattleStage.CompareDepth(a, a));
            Assert.AreEqual(BattleStage.SlotPosition(true, 3).y, BattleStage.SortKey(true, 3));
        }

        [Test]
        public void AssignSlots_FallsBackToActorOrder_AndSeparatesTeams()
        {
            var actors = new List<BattleActorState>
            {
                new BattleActorState { ActorId = 1, TeamIndex = 0 },
                new BattleActorState { ActorId = 2, TeamIndex = 1 },
                new BattleActorState { ActorId = 3, TeamIndex = 0 },
                new BattleActorState { ActorId = 4, TeamIndex = 1 },
                new BattleActorState { ActorId = 5, TeamIndex = 0 },
            };

            var all = BattleStage.AssignAll(actors, myTeam: 0);
            Assert.AreEqual(5, all.Count);
            Assert.AreEqual((true, 0), all[1]);
            Assert.AreEqual((true, 1), all[3]);
            Assert.AreEqual((true, 2), all[5]);
            Assert.AreEqual((false, 0), all[2]);
            Assert.AreEqual((false, 1), all[4]);
        }

        [Test]
        public void AssignSlots_HonorsFormationSlot_AndResolvesConflicts()
        {
            var actors = new List<BattleActorState>
            {
                new BattleActorState { ActorId = 1, TeamIndex = 0, FormationSlot = 7 },
                new BattleActorState { ActorId = 2, TeamIndex = 0, FormationSlot = 2 },
                new BattleActorState { ActorId = 3, TeamIndex = 0, FormationSlot = 7 },  // 与 1 冲突 → 首个空槽 0
                new BattleActorState { ActorId = 4, TeamIndex = 0, FormationSlot = 42 }, // 越界 → 回退
                new BattleActorState { ActorId = 5, TeamIndex = 1, FormationSlot = 7 },  // 另一队,不参与
            };

            var slots = BattleStage.AssignSlots(actors, 0);
            Assert.AreEqual(4, slots.Count);
            Assert.AreEqual(7, slots[1]);
            Assert.AreEqual(2, slots[2]);
            Assert.AreEqual(0, slots[3]);
            Assert.AreEqual(1, slots[4]);
            Assert.IsFalse(slots.ContainsKey(5));
            Assert.AreEqual(-1, BattleStage.PreferredSlot(actors[3]));
            Assert.AreEqual(-1, BattleStage.PreferredSlot(null));

            // 我方 5 号在敌队里按 formation_slot 落位
            var all = BattleStage.AssignAll(actors, myTeam: 0);
            Assert.AreEqual((false, 7), all[5]);
            Assert.AreEqual((true, 7), all[1]);
        }

        [Test]
        public void AssignSlots_MoreThanTenActors_WrapsWithoutThrowing()
        {
            var actors = new List<BattleActorState>();
            for (ulong i = 1; i <= 12; i++) actors.Add(new BattleActorState { ActorId = i, TeamIndex = 0 });
            var slots = BattleStage.AssignSlots(actors, 0);
            Assert.AreEqual(12, slots.Count);
            foreach (var kv in slots)
            {
                Assert.GreaterOrEqual(kv.Value, 0);
                Assert.Less(kv.Value, BattleStage.SlotsPerTeam);
            }
        }
    }
}
