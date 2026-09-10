using System;
using System.Collections.Generic;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Battle;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// BattleStage 逐槽位表测试(2026-09-05 按用户录制视频 a58f3434….mp4 量测):
    /// ① 槽位/宝宝位互不重叠;② 敌方左上、我方右下;③ 前后排的深度关系与缩放单调;④ 全部在 HUD 带之间且在屏内;
    /// ⑤ 每个槽位与量测表逐值相等(表是真源);⑥ 宝宝位相对主人的偏移方向与量测规律一致;以及槽位分配回退/宝宝不占槽。
    /// </summary>
    public sealed class BattleStageTests
    {
        /// <summary>任意两个脚底点的最小间距(视频槽间距 159 设计 px;允许后排被前排轻微遮挡)。</summary>
        private const float MinFootDistance = 90f;

        /// <summary>
        /// 量测表(设计坐标;视频像素 → 设计:s = 1080/592,dx = (vx − 640)·s + 1280,dy = vy·s)。
        /// 与 BattleStage 里的常量各自独立抄录,测试 ⑤ 用它对照 BattleStage 查表结果。
        /// row: true = back(远离中线,槽位 5-9),false = front(靠中线,槽位 0-4);col 0..4 沿斜带左下→右上。
        /// </summary>
        private static readonly (bool mine, bool back, int col, float x, float y, float petX, float petY)[] Measured =
        {
            // 敌方后排(邪仙爪牙 / 火地邪仙飘渺 ×3 / 邪仙爪牙)
            (false, true, 0, 581.3f, 669.5f, 718.1f, 757.1f),
            (false, true, 1, 714.5f, 587.4f, 854.9f, 671.4f),
            (false, true, 2, 854.9f, 501.7f, 986.3f, 589.3f),
            (false, true, 3, 979f, 415.9f, 1124.9f, 501.7f),
            (false, true, 4, 1119.5f, 330.2f, 1259f, 417.8f),
            // 敌方前排(邪仙爪牙 ×4 + 外推空槽)
            (false, false, 0, 718.1f, 757.1f, 856.7f, 843.2f),
            (false, false, 1, 854.9f, 671.4f, 993.5f, 757.5f),
            (false, false, 2, 986.3f, 589.3f, 1124.9f, 675.4f),
            (false, false, 3, 1124.9f, 501.7f, 1263.6f, 587.8f),
            (false, false, 4, 1259f, 417.8f, 1397.6f, 503.9f),
            // 我方后排(玩家 梦醒时夜续う / 逆天丶哀浪 / 陶の金帝 / jay一晴天 / 时光巷陌っ)
            (true, true, 0, 1398.6f, 948.6f, 1258.1f, 833.7f),
            (true, true, 1, 1546.4f, 866.6f, 1394.9f, 748f),
            (true, true, 2, 1668.6f, 773.5f, 1526.3f, 660.4f),
            (true, true, 3, 1803.6f, 696.9f, 1663.1f, 582f),
            (true, true, 4, 1945.9f, 605.7f, 1794.5f, 499.9f),
            // 我方前排(宝宝 雪女 / 酷酷龙 ×3 / 水神)
            (true, false, 0, 1258.1f, 833.7f, 1112.9f, 720.2f),
            (true, false, 1, 1394.9f, 748f, 1249.7f, 634.5f),
            (true, false, 2, 1526.3f, 660.4f, 1381.1f, 546.9f),
            (true, false, 3, 1663.1f, 582f, 1517.9f, 468.5f),
            (true, false, 4, 1794.5f, 499.9f, 1649.3f, 386.4f),
        };

        private static int SlotOf(bool back, int col) => back ? col + BattleStage.FrontRowCount : col;

        private static IEnumerable<(bool mine, int slot)> AllSlots()
        {
            for (int s = 0; s < BattleStage.SlotsPerTeam; s++) yield return (false, s);
            for (int s = 0; s < BattleStage.SlotsPerTeam; s++) yield return (true, s);
        }

        /// <summary>视频里有实例的宝宝位:后排(玩家排)主人的宝宝,每方 5 个。</summary>
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

        // ── ⑤ 表就是真源 ─────────────────────────────────────

        [Test]
        public void SlotPositions_MatchMeasuredTable_Exactly()
        {
            Assert.AreEqual(2 * BattleStage.SlotsPerTeam, Measured.Length, "量测表应覆盖两方各 10 槽");
            foreach (var m in Measured)
            {
                int slot = SlotOf(m.back, m.col);
                var pos = BattleStage.SlotPosition(m.mine, slot);
                Assert.AreEqual(m.x, pos.x, 1e-3f, $"{Tag(m.mine, slot)} x");
                Assert.AreEqual(m.y, pos.y, 1e-3f, $"{Tag(m.mine, slot)} y");
                var pet = BattleStage.PetSlotPosition(m.mine, slot);
                Assert.AreEqual(m.petX, pet.x, 1e-3f, $"{Tag(m.mine, slot)} 宝宝 x");
                Assert.AreEqual(m.petY, pet.y, 1e-3f, $"{Tag(m.mine, slot)} 宝宝 y");
                Assert.AreEqual(m.back, BattleStage.IsBackRow(slot));
                Assert.AreEqual(m.col, BattleStage.Column(slot));
            }
        }

        [Test]
        public void VideoToDesign_Conversion_ReproducesTable()
        {
            // 换算公式复核:我方玩家排 slot0 视频 (705,520) → (1398.6, 948.6);敌后排 slot4 视频 (552,181) → (1119.5, 330.2)
            float s = BattleStage.VideoToDesignScale;
            Assert.AreEqual(1.8243f, s, 1e-3f);
            Assert.AreEqual(112.4f, BattleStage.VideoOffsetX, 0.2f);
            Assert.AreEqual(1398.6f, (705f - 640f) * s + 1280f, 0.6f);
            Assert.AreEqual(948.6f, 520f * s, 0.6f);
            Assert.AreEqual(1119.5f, (552f - 640f) * s + 1280f, 0.6f);
            Assert.AreEqual(330.2f, 181f * s, 0.6f);
        }

        [Test]
        public void BackRowPets_StandOnFrontRowSpots_AsInVideo()
        {
            // 视频里我方前排就是 5 只宝宝:后排(玩家)主人的宝宝位 = 同列前排槽位;敌方同理(敌前排是敌后排的"宝宝位")
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
            // 视频里有实例的 10 个宝宝位(后排主人的宝宝)两两不重叠;与任何槽位不重叠 —— 除了主人自己,
            // 以及同列前排槽位(视频里前排就是宝宝排,二者按定义重合;见 BackRowPets_StandOnFrontRowSpots_AsInVideo)
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
                // 宝宝紧贴主人:脚底距离在一个槽间距上下(视频 ≈ 184 设计 px)
                float toOwner = Vector2.Distance(pets[i].pos, BattleStage.SlotPosition(pets[i].mine, pets[i].owner));
                Assert.That(toOwner, Is.InRange(150f, 200f), $"宝宝位 {Tag(pets[i].mine, pets[i].owner)} 到主人距离 {toOwner:0} 不像「贴着站」");
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
            // 视频:两阵后排中心 敌 (850,501)、我 (1673,778);全 10 槽中心 敌 (919,544)、我 (1600,722)
            // → 中心横向错开 ≈ 27% 屏宽、纵向 ≈ 177
            Assert.GreaterOrEqual(allyCenter.x - enemyCenter.x, 0.25f * QdaoUguiTheme.DesignWidth, "两队中心横向错位不足");
            Assert.GreaterOrEqual(allyCenter.y - enemyCenter.y, 150f, "我方整体应明显更靠下");
            // 敌方全部脚底在屏幕中线(x=1280)左侧,我方全部在其右侧;敌方最右点(外推槽 1259)与我方最左点(宝宝排 1258)几乎同一竖线
            Assert.LessOrEqual(enemyMaxX, QdaoUguiTheme.DesignWidth * 0.5f);
            Assert.GreaterOrEqual(allyMinX, QdaoUguiTheme.DesignWidth * 0.49f);

            // 每方的宝宝位也在各自半区
            foreach (var (mine, owner) in BackRowPetSlots())
            {
                var p = BattleStage.PetSlotPosition(mine, owner);
                if (mine) Assert.GreaterOrEqual(p.x, QdaoUguiTheme.DesignWidth * 0.49f, $"我方宝宝位 {owner} 跑到左半区");
                else Assert.LessOrEqual(p.x, QdaoUguiTheme.DesignWidth * 0.5f, $"敌方宝宝位 {owner} 跑到右半区");
            }
        }

        [Test]
        public void Rows_AreDiagonalBands_LowerLeftToUpperRight()
        {
            // 排方向:同排相邻槽 x 递增、y 递减,方向角 25°~40°(视频 32.2°),槽间距 140~180(视频 159)
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
                        Assert.That(angle, Is.InRange(25f, 40f), $"{(mine ? "我" : "敌")}方{(back ? "后" : "前")}排列 {c - 1}→{c} 方向角 {angle:0.0}°");
                        Assert.That(step.magnitude, Is.InRange(140f, 180f), $"{(mine ? "我" : "敌")}方{(back ? "后" : "前")}排列 {c - 1}→{c} 槽间距 {step.magnitude:0}");
                    }
                }
            }
        }

        // ── ③ 前后排深度关系与缩放 ───────────────────────────

        [Test]
        public void BackRow_IsFartherFromCenterLine_AndScaleFollowsDepth_SameColumn()
        {
            // 后排 = 远离中线的一排:敌方后排更靠上(y 更小)且更小;我方后排(玩家)更靠下(y 更大)且更大 —— 近大远小一致。
            // (视频里两边不是镜像:我方后排是玩家、在屏幕最下方;"后排一律更小"只对敌方成立)
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
                // 排间垂直距离:敌 146、我 173(设计 px),同列平移量(含沿排错位)在 140~200(实测 158~192)
                Assert.That(Vector2.Distance(frontE, backE), Is.InRange(140f, 200f));
                Assert.That(Vector2.Distance(frontA, backA), Is.InRange(140f, 200f));
            }
            Assert.IsTrue(BattleStage.IsBackRow(BattleStage.FrontRowCount));
            Assert.IsFalse(BattleStage.IsBackRow(BattleStage.FrontRowCount - 1));
        }

        [Test]
        public void Scale_IsWeakLinearDepth_NoRowOrTeamScaling()
        {
            // 视频里几乎没有近大远小:全场缩放 0.93~1.0,脚底 y 越大越大,且与 y 严格线性(无排/队整体缩放)
            float minS = float.MaxValue, maxS = float.MinValue;
            foreach (var (mine, slot) in AllSlots())
            {
                var p = BattleStage.SlotPosition(mine, slot);
                float s = BattleStage.SlotScale(mine, slot);
                Assert.AreEqual(BattleStage.DepthScale(p.y), s, 1e-5f);
                Assert.AreEqual(1f - (BattleStage.DepthScaleReferenceY - p.y) * BattleStage.DepthScalePerPixel, s, 1e-5f);
                minS = Mathf.Min(minS, s);
                maxS = Mathf.Max(maxS, s);
            }
            Assert.GreaterOrEqual(minS, 0.93f, "最远单位不应缩得太小");
            Assert.LessOrEqual(maxS, 1f + 1e-5f, "以我方玩家排 slot0 为 1.0 基准");
            Assert.AreEqual(1f, BattleStage.SlotScale(true, BattleStage.FrontRowCount), 1e-5f, "我方后排 slot0(全场最低点)= 1.0");
            Assert.AreEqual(0.938f, BattleStage.SlotScale(false, BattleStage.SlotsPerTeam - 1), 1e-3f, "敌方后排 slot4(全场最高点)≈ 0.938");

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
                Assert.GreaterOrEqual(p.x - BattleStage.UnitWidth * 0.5f * scale, 0f, $"{label} 左边出屏");
                Assert.LessOrEqual(p.x + BattleStage.UnitWidth * 0.5f * scale, QdaoUguiTheme.DesignWidth, $"{label} 右边出屏");
                Assert.GreaterOrEqual(p.y - BattleStage.UnitHeight * scale, 0f, $"{label} 头顶出屏");
                Assert.LessOrEqual(p.y + 40f, QdaoUguiTheme.DesignHeight, $"{label} 脚下名字出屏");
                // 脚底在两条 HUD 带之间:立绘顶(脚底 − UnitHeight×缩放)不进顶部预告条,脚下名字(脚底 + 40)不进底部确认条
                Assert.GreaterOrEqual(p.y - BattleStage.UnitHeight * scale, BattleStage.HudTopBand - 40f, $"{label} 立绘顶 {p.y - BattleStage.UnitHeight * scale:0} 压进顶部 HUD 带");
                Assert.GreaterOrEqual(p.y, BattleStage.HudTopBand, $"{label} 脚底 {p.y:0} 高于顶部 HUD 带");
                Assert.LessOrEqual(p.y + 40f, BattleStage.HudBottomBand, $"{label} 名字底 {p.y + 40f:0} 进入底部 HUD 带({BattleStage.HudBottomBand})");
            }

            foreach (var (mine, slot) in AllSlots())
            {
                Check($"槽位 {Tag(mine, slot)}", BattleStage.SlotPosition(mine, slot), BattleStage.SlotScale(mine, slot));
                Check($"宝宝位 {Tag(mine, slot)}", BattleStage.PetSlotPosition(mine, slot), BattleStage.PetSlotScale(mine, slot));
            }
        }

        [Test]
        public void AllSlots_KeepClearOfCommandRing_AndPartyCards()
        {
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
                var closest = new Vector2(Mathf.Clamp(ringCenter.x, box.xMin, box.xMax), Mathf.Clamp(ringCenter.y, box.yMin, box.yMax));
                Assert.GreaterOrEqual(Vector2.Distance(closest, ringCenter), ringRadius, $"{Tag(mine, slot)} 压到命令环");
                Assert.IsFalse(box.Overlaps(cards), $"{Tag(mine, slot)} 压到右上角色卡");
            }
            // 底部确认/取消条在底部 HUD 带内、不与提示文字重叠、都在屏内
            Assert.GreaterOrEqual(BattleScreen.ConfirmRect.yMin, BattleStage.HudBottomBand);
            Assert.LessOrEqual(BattleScreen.ConfirmRect.yMax, BattleScreen.HintRect.yMin);
            Assert.LessOrEqual(BattleScreen.CancelRect.yMax, BattleScreen.HintRect.yMin);
            Assert.LessOrEqual(BattleScreen.TargetHintRect.yMax, BattleScreen.HintRect.yMin);
            Assert.LessOrEqual(BattleScreen.HintRect.yMax, QdaoUguiTheme.DesignHeight);
        }

        // ── ⑥ 宝宝位相对主人的偏移 ───────────────────────────

        [Test]
        public void PetOffset_FromOwner_MatchesMeasuredDirection()
        {
            // 我方:宝宝在主人"左上方"(朝敌方前方偏左),实测均值 (−145, −113) 设计 px;
            // 敌方:宝宝位在主人"右下方"(朝我方),实测前排−后排均值 (+139, +86)。两边不是严格镜像(我方排间距 173 > 敌方 146)
            for (int slot = 0; slot < BattleStage.SlotsPerTeam; slot++)
            {
                var dA = BattleStage.PetSlotPosition(true, slot) - BattleStage.SlotPosition(true, slot);
                Assert.Less(dA.x, 0f, $"我方槽 {slot} 宝宝应在主人左侧");
                Assert.Less(dA.y, 0f, $"我方槽 {slot} 宝宝应在主人上方");
                Assert.AreEqual(-145f, dA.x, 12f, $"我方槽 {slot} 宝宝 Δx");
                Assert.AreEqual(-113f, dA.y, 12f, $"我方槽 {slot} 宝宝 Δy");

                var dE = BattleStage.PetSlotPosition(false, slot) - BattleStage.SlotPosition(false, slot);
                Assert.Greater(dE.x, 0f, $"敌方槽 {slot} 宝宝位应在主人右侧");
                Assert.Greater(dE.y, 0f, $"敌方槽 {slot} 宝宝位应在主人下方");
                Assert.AreEqual(139f, dE.x, 12f, $"敌方槽 {slot} 宝宝 Δx");
                Assert.AreEqual(86f, dE.y, 12f, $"敌方槽 {slot} 宝宝 Δy");
            }
            // 分解到排方向(32.2°):垂直排方向朝中线推一排(我 ≈173 / 敌 ≈147)+ 沿排方向错位
            // (我方宝宝朝左下 ≈62、敌前排朝右上 ≈71,由 (−145,−113)/(+139,+86) 投影直接算出,约 0.4 槽)
            var along = new Vector2(134.5f, -85f).normalized;              // 左下→右上
            var towardAlly = new Vector2(85f, 134.5f).normalized;          // 垂直排方向,指向右下(我方)
            var meanA = Vector2.zero; var meanE = Vector2.zero;
            for (int c = 0; c < BattleStage.FrontRowCount; c++)
            {
                meanA += (BattleStage.PetSlotPosition(true, c + 5) - BattleStage.SlotPosition(true, c + 5)) / 5f;
                meanE += (BattleStage.PetSlotPosition(false, c + 5) - BattleStage.SlotPosition(false, c + 5)) / 5f;
            }
            Assert.AreEqual(-173f, Vector2.Dot(meanA, towardAlly), 10f, "我方宝宝位应朝中线(左上)推约 173");
            Assert.AreEqual(-62f, Vector2.Dot(meanA, along), 10f, "我方宝宝位应沿排方向朝左下退约 62");
            Assert.AreEqual(147f, Vector2.Dot(meanE, towardAlly), 10f, "敌方宝宝位应朝中线(右下)推约 147");
            Assert.AreEqual(71f, Vector2.Dot(meanE, along), 10f, "敌方宝宝位应沿排方向朝右上错约 71");
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
