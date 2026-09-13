using System;
using System.Collections.Generic;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Battle;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 森林石桥战斗舞台的行为与几何约束:
    /// 协议槽号/前后排/列顺序保持稳定;敌左上、我右下;脚位有间距且按脚底深度缩放;
    /// 完整名牌避开 HUD;宝宝跟随同列主人;槽位分配回退与归属业务保持不变。
    /// </summary>
    public sealed class BattleStageTests
    {
        /// <summary>主槽脚底至少相距 90 设计像素,允许按深度排序的立绘局部遮挡。</summary>
        private const float MinFootDistance = 90f;
        /// <summary>完整名牌包括向右延伸的 buff 图标行;同时包住常规 230px 方帧立绘。</summary>
        private const float NameplateHalfWidth = 120f;
        private const float NameplateBelowFeet = 40f;

        // 固定美术合同:从森林石桥背景的干净石台内缘独立手工描出,不是从槽位坐标生成。
        // 来源:qdao_festival_scenes_20260910/runtime/battle-platform-ground.json (v1),2560x1080,y 向下。
        // 敌方多边形还避开下沿桥头金柱;不包括水面、栏杆、桥面或植被。
        private static readonly Vector2[] EnemyStonePlatform =
        {
            new Vector2(404f, 392f),
            new Vector2(476f, 320f),
            new Vector2(614f, 286f),
            new Vector2(774f, 290f),
            new Vector2(906f, 316f),
            new Vector2(1000f, 358f),
            new Vector2(1044f, 412f),
            new Vector2(975f, 440f),
            new Vector2(884f, 488f),
            new Vector2(820f, 516f),
            new Vector2(672f, 532f),
            new Vector2(528f, 546f),
            new Vector2(424f, 532f),
            new Vector2(376f, 506f),
            new Vector2(366f, 464f),
        };

        private static readonly Vector2[] AllyStonePlatform =
        {
            new Vector2(1390f, 824f),
            new Vector2(1430f, 740f),
            new Vector2(1515f, 680f),
            new Vector2(1640f, 626f),
            new Vector2(1770f, 596f),
            new Vector2(1940f, 580f),
            new Vector2(2100f, 596f),
            new Vector2(2225f, 660f),
            new Vector2(2260f, 748f),
            new Vector2(2210f, 836f),
            new Vector2(2090f, 920f),
            new Vector2(1910f, 983f),
            new Vector2(1700f, 1000f),
            new Vector2(1555f, 970f),
            new Vector2(1450f, 914f),
        };

        private static int SlotOf(bool back, int col) => back ? col + BattleStage.FrontRowCount : col;

        private static IEnumerable<(bool mine, int slot)> AllSlots()
        {
            for (int s = 0; s < BattleStage.SlotsPerTeam; s++) yield return (false, s);
            for (int s = 0; s < BattleStage.SlotsPerTeam; s++) yield return (true, s);
        }

        /// <summary>后排主人的宝宝占同列前排位置,每方 5 个。</summary>
        private static IEnumerable<(bool mine, int ownerSlot)> BackRowPetSlots()
        {
            for (int c = 0; c < BattleStage.FrontRowCount; c++) yield return (false, c + BattleStage.FrontRowCount);
            for (int c = 0; c < BattleStage.FrontRowCount; c++) yield return (true, c + BattleStage.FrontRowCount);
        }

        private static string Tag(bool mine, int slot) => $"{(mine ? "我" : "敌")}{slot}";

        [TearDown]
        public void RestorePetResolver()
        {
            BattleStage.PetOwnerResolver = _ => 0UL;
        }

        // ── 协议槽号与前后排语义 ────────────────────────────

        [Test]
        public void Slots_KeepTenProtocolIndices_AndTwoFiveSlotRows()
        {
            Assert.AreEqual(10, BattleStage.SlotsPerTeam, "协议每队仍保留槽号 0..9");
            Assert.AreEqual(5, BattleStage.FrontRowCount, "协议前排为 0..4,后排为 5..9");
            for (int slot = 0; slot < 10; slot++)
            {
                Assert.AreEqual(slot >= 5, BattleStage.IsBackRow(slot), $"槽 {slot} 前后排语义改变");
                Assert.AreEqual(slot % 5, BattleStage.Column(slot), $"槽 {slot} 列号改变");
            }
        }

        [Test]
        public void FrontRows_AreCloserToOpposingTeam_ThanBackRows()
        {
            foreach (bool mine in new[] { false, true })
            {
                var opponentCenter = Vector2.zero;
                for (int slot = 0; slot < BattleStage.SlotsPerTeam; slot++)
                    opponentCenter += BattleStage.SlotPosition(!mine, slot) / BattleStage.SlotsPerTeam;
                for (int col = 0; col < BattleStage.FrontRowCount; col++)
                {
                    var front = BattleStage.SlotPosition(mine, col);
                    var back = BattleStage.SlotPosition(mine, col + BattleStage.FrontRowCount);
                    Assert.Less(Vector2.Distance(front, opponentCenter), Vector2.Distance(back, opponentCenter),
                        $"{(mine ? "我" : "敌")}方第 {col} 列前排应朝向对方,后排应远离对方");
                }
            }
        }

        [Test]
        public void BackRowPets_StandOnSameColumnFrontRowSpots()
        {
            // 后排主人的宝宝位始终等于同列前排槽位,两队保持同一协议语义。
            foreach (bool mine in new[] { false, true })
            {
                for (int c = 0; c < BattleStage.FrontRowCount; c++)
                {
                    var pet = BattleStage.PetSlotPosition(mine, c + BattleStage.FrontRowCount);
                    var front = BattleStage.SlotPosition(mine, c);
                    Assert.AreEqual(front, pet, $"{(mine ? "我" : "敌")}方第 {c} 列后排的宝宝位应等于同列前排");
                }
            }
        }

        // ── ① 不重叠 ────────────────────────────────────────

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
                        $"槽位 {Tag(all[i].mine, all[i].slot)} 与 {Tag(all[j].mine, all[j].slot)} 距离 {d:0} 过近");
                }
            }
        }

        [Test]
        public void PetSlots_DoNotOverlap_EachOther_OrForeignSlots()
        {
            // 后排主人的 10 个宝宝位两两不重叠;与非共享槽位保持距离 —— 除了主人自己,
            // 以及同列前排槽位(二者按定义重合;见 BackRowPets_StandOnSameColumnFrontRowSpots)
            var pets = new List<(bool mine, int owner, Vector2 pos)>();
            foreach (var (mine, owner) in BackRowPetSlots()) pets.Add((mine, owner, BattleStage.PetSlotPosition(mine, owner)));

            for (int i = 0; i < pets.Count; i++)
            {
                for (int j = i + 1; j < pets.Count; j++)
                {
                    float d = Vector2.Distance(pets[i].pos, pets[j].pos);
                    Assert.GreaterOrEqual(d, MinFootDistance, $"宝宝位 {Tag(pets[i].mine, pets[i].owner)} 与 {Tag(pets[j].mine, pets[j].owner)} 距离 {d:0} 过近");
                }
                foreach (var (mine, slot) in AllSlots())
                {
                    bool isOwner = mine == pets[i].mine && slot == pets[i].owner;
                    bool isSharedFront = mine == pets[i].mine && slot == BattleStage.Column(pets[i].owner);
                    if (isOwner || isSharedFront) continue;
                    float d = Vector2.Distance(pets[i].pos, BattleStage.SlotPosition(mine, slot));
                    Assert.GreaterOrEqual(d, MinFootDistance, $"宝宝位 {Tag(pets[i].mine, pets[i].owner)} 与槽位 {Tag(mine, slot)} 距离 {d:0} 过近");
                }
                // 后排宝宝与主人的距离由可读的前后排间距约束。
                float toOwner = Vector2.Distance(pets[i].pos, BattleStage.SlotPosition(pets[i].mine, pets[i].owner));
                Assert.That(toOwner, Is.InRange(90f, 145f), $"宝宝位 {Tag(pets[i].mine, pets[i].owner)} 到主人距离 {toOwner:0} 不像「贴着站」");
            }
        }

        // ── ② 敌左上、我右下 ─────────────────────────────────

        [Test]
        public void Enemy_UpperLeft_Ally_LowerRight()
        {
            Vector2 enemyCenter = Vector2.zero, allyCenter = Vector2.zero;
            float enemyMaxX = float.MinValue, allyMinX = float.MaxValue;
            for (int s = 0; s < BattleStage.SlotsPerTeam; s++)
            {
                var e = BattleStage.SlotPosition(false, s);
                var a = BattleStage.SlotPosition(true, s);
                enemyCenter += e / BattleStage.SlotsPerTeam;
                allyCenter += a / BattleStage.SlotsPerTeam;
                enemyMaxX = Mathf.Max(enemyMaxX, e.x);
                allyMinX = Mathf.Min(allyMinX, a.x);
            }
            Assert.Less(enemyCenter.y, allyCenter.y, "敌方整体应更靠上");
            Assert.Less(enemyCenter.x, allyCenter.x, "敌方整体应更靠左");
            // 两个石台分别在左上/右下,队伍中心保持明显分离。
            Assert.GreaterOrEqual(allyCenter.x - enemyCenter.x, 0.25f * QdaoUguiTheme.DesignWidth, "两队中心横向错位不足");
            Assert.GreaterOrEqual(allyCenter.y - enemyCenter.y, 150f, "我方整体应明显更靠下");
            // 两队主槽脚点及全部宝宝位留在各自石台所在的屏幕半区。
            Assert.LessOrEqual(enemyMaxX, QdaoUguiTheme.DesignWidth * 0.5f);
            Assert.GreaterOrEqual(allyMinX, QdaoUguiTheme.DesignWidth * 0.5f);

            // 包括前排主人预留的宝宝位,避免只检查常用的后排宝宝。
            foreach (var (mine, owner) in AllSlots())
            {
                var p = BattleStage.PetSlotPosition(mine, owner);
                if (mine) Assert.GreaterOrEqual(p.x, QdaoUguiTheme.DesignWidth * 0.5f, $"我方宝宝位 {owner} 跑到左半区");
                else Assert.LessOrEqual(p.x, QdaoUguiTheme.DesignWidth * 0.5f, $"敌方宝宝位 {owner} 跑到右半区");
            }
        }

        [Test]
        public void Rows_AreDiagonalBands_LowerLeftToUpperRight()
        {
            // 同排相邻列仍从左下向右上,允许为较浅的石台透视放平斜带。
            foreach (bool mine in new[] { false, true })
            {
                foreach (bool back in new[] { false, true })
                {
                    for (int c = 1; c < BattleStage.FrontRowCount; c++)
                    {
                        var a = BattleStage.SlotPosition(mine, SlotOf(back, c - 1));
                        var b = BattleStage.SlotPosition(mine, SlotOf(back, c));
                        var step = b - a;
                        Assert.Greater(step.x, 0f, $"{(mine ? "我" : "敌")}方{(back ? "后" : "前")}排列 {c} 应在列 {c - 1} 右侧");
                        Assert.Less(step.y, 0f, $"{(mine ? "我" : "敌")}方{(back ? "后" : "前")}排列 {c} 应在列 {c - 1} 上方");
                        float angle = Mathf.Atan2(-step.y, step.x) * Mathf.Rad2Deg;
                        Assert.Greater(angle, 0f, $"{(mine ? "我" : "敌")}方排列方向角应大于 0°");
                        Assert.Less(angle, 35f, $"{(mine ? "我" : "敌")}方排列方向角 {angle:0.0}° 过陡");
                        Assert.That(step.magnitude, Is.InRange(90f, 180f), $"{(mine ? "我" : "敌")}方{(back ? "后" : "前")}排列 {c - 1}→{c} 槽间距 {step.magnitude:0}");
                    }
                }
            }
        }

        // ── ③ 前后排深度关系与缩放 ───────────────────────────

        [Test]
        public void BackRow_IsFartherFromCenterLine_AndScaleFollowsDepth_SameColumn()
        {
            // 后排 = 远离中线的一排:敌方后排更靠上(y 更小)且更小;我方后排(玩家)更靠下(y 更大)且更大 —— 近大远小一致。
            // 后排缩放由脚底深度决定,不额外按队伍或行号缩放。
            for (int col = 0; col < BattleStage.FrontRowCount; col++)
            {
                var frontE = BattleStage.SlotPosition(false, col);
                var backE = BattleStage.SlotPosition(false, col + BattleStage.FrontRowCount);
                Assert.Less(backE.y, frontE.y, $"敌方第 {col} 列后排应更靠上");
                Assert.Less(BattleStage.SlotScale(false, col + BattleStage.FrontRowCount), BattleStage.SlotScale(false, col), $"敌方第 {col} 列后排应更小");

                var frontA = BattleStage.SlotPosition(true, col);
                var backA = BattleStage.SlotPosition(true, col + BattleStage.FrontRowCount);
                Assert.Greater(backA.y, frontA.y, $"我方第 {col} 列后排应更靠下");
                Assert.Greater(BattleStage.SlotScale(true, col + BattleStage.FrontRowCount), BattleStage.SlotScale(true, col), $"我方第 {col} 列后排应更大");
                // 同列前后排保持可读间距,并与后排宝宝贴近主人的距离一致。
                Assert.That(Vector2.Distance(frontE, backE), Is.InRange(90f, 145f));
                Assert.That(Vector2.Distance(frontA, backA), Is.InRange(90f, 145f));
            }
            Assert.IsTrue(BattleStage.IsBackRow(BattleStage.FrontRowCount));
            Assert.IsFalse(BattleStage.IsBackRow(BattleStage.FrontRowCount - 1));
        }

        [Test]
        public void Scale_IsWeakLinearDepth_NoRowOrTeamScaling()
        {
            // 保留极弱线性深度模型,不把旧背景中某一槽的深度值作为新场景真源。
            Assert.AreEqual(1f / 10000f, BattleStage.DepthScalePerPixel, 1e-8f, "深度斜率保持每 100px 缩放 1%");
            float minS = float.MaxValue, maxS = float.MinValue, maxY = float.MinValue;
            foreach (var (mine, slot) in AllSlots())
            {
                var p = BattleStage.SlotPosition(mine, slot);
                float s = BattleStage.SlotScale(mine, slot);
                Assert.AreEqual(BattleStage.DepthScale(p.y), s, 1e-5f);
                Assert.AreEqual(1f - (BattleStage.DepthScaleReferenceY - p.y) * BattleStage.DepthScalePerPixel, s, 1e-5f);
                minS = Mathf.Min(minS, s);
                maxS = Mathf.Max(maxS, s);
                maxY = Mathf.Max(maxY, p.y);
            }
            Assert.GreaterOrEqual(minS, 0.93f, "最远单位不应缩得太小");
            Assert.LessOrEqual(maxS, 1f + 1e-5f, "最靠下的单位作为 1.0 缩放基准");
            Assert.AreEqual(maxY, BattleStage.DepthScaleReferenceY, 1e-5f, "深度基准跟随全场最低脚点");
            Assert.AreEqual(1f, BattleStage.DepthScale(maxY), 1e-5f);
            Assert.AreEqual(1f, maxS, 1e-5f);

            // 任意两槽:脚底更低的不小于更高的
            var list = new List<(Vector2 p, float s)>();
            foreach (var (mine, slot) in AllSlots()) list.Add((BattleStage.SlotPosition(mine, slot), BattleStage.SlotScale(mine, slot)));
            foreach (var a in list)
            {
                foreach (var b in list)
                {
                    if (a.p.y > b.p.y) Assert.GreaterOrEqual(a.s, b.s);
                }
            }
        }

        [Test]
        public void PetScale_IsSmallerThanOwner_ByFixedRatio()
        {
            Assert.That(BattleStage.PetRelativeScale, Is.InRange(0.6f, 0.7f));
            foreach (var (mine, slot) in AllSlots())
            {
                float owner = BattleStage.SlotScale(mine, slot);
                float pet = BattleStage.PetSlotScale(mine, slot);
                Assert.AreEqual(owner * BattleStage.PetRelativeScale, pet, 1e-5f);
                Assert.Less(pet, owner);
            }
        }

        // ── ④ 在 HUD 带之间、在屏内 ──────────────────────────

        [Test]
        public void AllSlotsAndPetSlots_AreInsideScreen_AndBetweenHudBands()
        {
            void Check(string label, Vector2 p, float scale)
            {
                var box = UnitAndNameplateBounds(p, scale);
                Assert.GreaterOrEqual(box.xMin, 0f, $"{label} 左边出屏");
                Assert.LessOrEqual(box.xMax, QdaoUguiTheme.DesignWidth, $"{label} 右边出屏");
                Assert.GreaterOrEqual(box.yMin, 0f, $"{label} 头顶名牌出屏");
                Assert.LessOrEqual(box.yMax, QdaoUguiTheme.DesignHeight, $"{label} 脚下名字出屏");
                // 完整头顶块包括可见立绘、HP/MP 双条与 buff 行,不能只用 200px 的立绘高度。
                Assert.GreaterOrEqual(box.yMin, BattleStage.HudTopBand,
                    $"{label} 头顶名牌 {box.yMin:0} 压进顶部 HUD 带({BattleStage.HudTopBand})");
                Assert.LessOrEqual(box.yMax, BattleStage.HudBottomBand,
                    $"{label} 名字底 {box.yMax:0} 进入底部 HUD 带({BattleStage.HudBottomBand})");
            }

            foreach (var (mine, slot) in AllSlots())
            {
                Check($"槽位 {Tag(mine, slot)}", BattleStage.SlotPosition(mine, slot), BattleStage.SlotScale(mine, slot));
                Check($"宝宝位 {Tag(mine, slot)}", BattleStage.PetSlotPosition(mine, slot), BattleStage.PetSlotScale(mine, slot));
            }
        }

        [Test]
        public void AllSlotsAndPetSlots_KeepClearOfCommandRing_AndPartyCards()
        {
            // 完整单位/名牌包围盒不压右下命令环与右上角色卡,也覆盖所有预留宝宝位。
            var ringCenter = new Vector2(BattleCommandRing.CenterX, BattleCommandRing.CenterY);
            float ringRadius = BattleCommandRing.RingSize * 0.5f;
            var cards = new Rect(QdaoUguiTheme.DesignWidth - BattlePartyCards.RightMargin - BattlePartyCards.CardWidth, 0f,
                BattlePartyCards.CardWidth + BattlePartyCards.RightMargin,
                BattlePartyCards.Top + BattlePartyCards.MaxCards * (BattlePartyCards.CardHeight + BattlePartyCards.Gap));
            void Check(string label, Vector2 p, float scale)
            {
                var box = UnitAndNameplateBounds(p, scale);
                var closest = new Vector2(Mathf.Clamp(ringCenter.x, box.xMin, box.xMax), Mathf.Clamp(ringCenter.y, box.yMin, box.yMax));
                Assert.GreaterOrEqual(Vector2.Distance(closest, ringCenter), ringRadius, $"{label} 压到命令环");
                Assert.IsFalse(box.Overlaps(cards), $"{label} 压到右上角色卡");
            }
            foreach (var (mine, slot) in AllSlots())
            {
                Check($"槽位 {Tag(mine, slot)}", BattleStage.SlotPosition(mine, slot), BattleStage.SlotScale(mine, slot));
                Check($"宝宝位 {Tag(mine, slot)}", BattleStage.PetSlotPosition(mine, slot), BattleStage.PetSlotScale(mine, slot));
            }
            // 底部确认/取消条在底部 HUD 带内、不与提示文字重叠、都在屏内。
            Assert.GreaterOrEqual(BattleScreen.ConfirmRect.yMin, BattleStage.HudBottomBand);
            Assert.LessOrEqual(BattleScreen.ConfirmRect.yMax, BattleScreen.HintRect.yMin);
            Assert.LessOrEqual(BattleScreen.CancelRect.yMax, BattleScreen.HintRect.yMin);
            Assert.LessOrEqual(BattleScreen.TargetHintRect.yMax, BattleScreen.HintRect.yMin);
            Assert.LessOrEqual(BattleScreen.HintRect.yMax, QdaoUguiTheme.DesignHeight);
        }

        [Test]
        public void AllSlotsAndPetShadows_StayInsidePaintedStonePlatforms()
        {
            void Check(string label, Vector2 foot, float scale, Vector2[] platform)
            {
                Assert.IsTrue(IsInsidePlatform(foot, platform), $"{label} 脚底不在石台内");
                // BattleUnitView 的脚底阴影为 120x44 椭圆;中心入台仍不足以保证阴影落地。
                const int samples = 32;
                for (int sample = 0; sample < samples; sample++)
                {
                    float angle = sample * 2f * Mathf.PI / samples;
                    var edge = foot + new Vector2(60f * scale * Mathf.Cos(angle), 22f * scale * Mathf.Sin(angle));
                    Assert.IsTrue(IsInsidePlatform(edge, platform),
                        $"{label} 阴影边界采样 {sample}/{samples} ({edge.x:0.0},{edge.y:0.0}) 离开石台");
                }
            }
            foreach (var (mine, slot) in AllSlots())
            {
                var platform = mine ? AllyStonePlatform : EnemyStonePlatform;
                Check($"槽位 {Tag(mine, slot)}", BattleStage.SlotPosition(mine, slot), BattleStage.SlotScale(mine, slot), platform);
                Check($"宝宝位 {Tag(mine, slot)}", BattleStage.PetSlotPosition(mine, slot), BattleStage.PetSlotScale(mine, slot), platform);
            }
        }

        private static bool IsInsidePlatform(Vector2 point, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, previous = polygon.Length - 1; i < polygon.Length; previous = i++)
            {
                var a = polygon[previous];
                var b = polygon[i];
                var edge = b - a;
                // 边界也算地面,仅容许 0.01 设计像素的浮点误差。
                if (edge.sqrMagnitude > 0f)
                {
                    float t = Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude);
                    if ((point - (a + t * edge)).sqrMagnitude <= 0.0001f) return true;
                }
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        private static Rect UnitAndNameplateBounds(Vector2 foot, float scale)
            => Rect.MinMaxRect(foot.x - NameplateHalfWidth * scale, foot.y - BattleUnitView.OverheadReach * scale,
                foot.x + NameplateHalfWidth * scale, foot.y + NameplateBelowFeet);

        // ── ⑥ 宝宝位相对主人的偏移 ───────────────────────────

        [Test]
        public void PetOffset_FromOwner_FacesOpponent_AndFitsItsRow()
        {
            foreach (var (mine, slot) in AllSlots())
            {
                var offset = BattleStage.PetSlotPosition(mine, slot) - BattleStage.SlotPosition(mine, slot);
                if (mine)
                {
                    Assert.Less(offset.x, 0f, $"我方槽 {slot} 宝宝应在主人左侧");
                    Assert.Less(offset.y, 0f, $"我方槽 {slot} 宝宝应在主人上方");
                }
                else
                {
                    Assert.Greater(offset.x, 0f, $"敌方槽 {slot} 宝宝应在主人右侧");
                    Assert.Greater(offset.y, 0f, $"敌方槽 {slot} 宝宝应在主人下方");
                }
                // 后排宝宝站同列前排;前排主人的预留宝宝只向对方短移,避免再推出石台。
                if (BattleStage.IsBackRow(slot))
                    Assert.That(offset.magnitude, Is.InRange(90f, 145f), $"{Tag(mine, slot)} 后排宝宝离主人过远或过近");
                else
                    Assert.That(offset.magnitude, Is.InRange(30f, 65f), $"{Tag(mine, slot)} 前排预留宝宝偏移应更短");
            }
        }

        // ── 槽位语义 / 排序 ───────────────────────────────────

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
            Assert.AreEqual(BattleStage.PetSlotPosition(true, 7), BattleStage.PetSlotPosition(true, 17));
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
            // 宝宝脚底在主人上方 → 先画,被主人盖住(视频里主人头部盖住宝宝名字)
            for (int slot = 0; slot < BattleStage.SlotsPerTeam; slot++)
                Assert.Less(BattleStage.CompareDepth(BattleStage.PetSlotPosition(true, slot), BattleStage.SlotPosition(true, slot)), 0);
        }

        // ── 槽位分配 ─────────────────────────────────────────

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

        [Test]
        public void Pets_DoNotConsumeSlots_AndFollowOwnerPlacement()
        {
            // 宝宝归属由 PetOwnerResolver 决定(默认恒 0 = 无宝宝);主人在场的宝宝不占槽、条目沿用主人的 (teamIsMine, slot)
            var owners = new Dictionary<ulong, ulong> { [21] = 1, [22] = 2, [23] = 99 /* 主人不在场 → 孤儿 */ };
            BattleStage.PetOwnerResolver = a => owners.TryGetValue(a.ActorId, out var o) ? o : 0UL;

            var actors = new List<BattleActorState>
            {
                new BattleActorState { ActorId = 1, TeamIndex = 0, FormationSlot = 5 },
                new BattleActorState { ActorId = 21, TeamIndex = 0 },                     // 1 的宝宝
                new BattleActorState { ActorId = 2, TeamIndex = 0, FormationSlot = 6 },
                new BattleActorState { ActorId = 22, TeamIndex = 0 },                     // 2 的宝宝
                new BattleActorState { ActorId = 23, TeamIndex = 0 },                     // 孤儿宝宝 → 普通单位占槽 0
                new BattleActorState { ActorId = 3, TeamIndex = 1 },
            };

            var mine = BattleStage.AssignSlots(actors, 0);
            Assert.AreEqual(3, mine.Count, "两只有主的宝宝不占槽");
            Assert.AreEqual(5, mine[1]);
            Assert.AreEqual(6, mine[2]);
            Assert.AreEqual(0, mine[23]);
            Assert.IsFalse(mine.ContainsKey(21));
            Assert.IsFalse(mine.ContainsKey(22));

            var all = BattleStage.AssignAll(actors, myTeam: 0);
            Assert.AreEqual(6, all.Count);
            Assert.AreEqual((true, 5), all[21]);
            Assert.AreEqual((true, 6), all[22]);
            Assert.AreEqual((true, 0), all[23]);
            Assert.AreEqual((false, 0), all[3]);
            Assert.IsTrue(BattleStage.IsPetPlacement(actors[1], all));
            Assert.IsFalse(BattleStage.IsPetPlacement(actors[4], all), "孤儿宝宝按普通单位摆");
            Assert.IsFalse(BattleStage.IsPetPlacement(actors[0], all));
            Assert.AreEqual(0UL, BattleStage.PetOwnerOf(actors[0]));
            Assert.AreEqual(1UL, BattleStage.PetOwnerOf(actors[1]));

            // 宝宝落在主人的宝宝位 = 视频里的宝宝排(我方前排实测位)
            Assert.AreEqual(BattleStage.SlotPosition(true, 0), BattleStage.PetSlotPosition(true, all[21].slot));
            Assert.AreEqual(BattleStage.SlotPosition(true, 1), BattleStage.PetSlotPosition(true, all[22].slot));
        }

        [Test]
        public void PetOwnerResolver_Throwing_IsTreatedAsNoPet()
        {
            BattleStage.PetOwnerResolver = _ => throw new InvalidOperationException("boom");
            var actor = new BattleActorState { ActorId = 7, TeamIndex = 0 };
            Assert.AreEqual(0UL, BattleStage.PetOwnerOf(actor));
            var slots = BattleStage.AssignSlots(new[] { actor }, 0);
            Assert.AreEqual(0, slots[7]);
        }
    }
}
