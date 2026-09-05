using System.Collections.Generic;
using MmorpgClient.UI.Ugui.Battle;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>伤害数字 / 特效贴放 / 结算文案的纯逻辑测试。</summary>
    public sealed class DamageNumberLayoutTests
    {
        [Test]
        public void FormatText_ByKind()
        {
            Assert.AreEqual("-120", DamageNumberLayout.FormatText(120, NumberKind.Normal));
            Assert.AreEqual("-120", DamageNumberLayout.FormatText(-120, NumberKind.Normal));
            Assert.AreEqual("+35", DamageNumberLayout.FormatText(35, NumberKind.Heal));
            Assert.AreEqual("闪", DamageNumberLayout.FormatText(0, NumberKind.Miss));
            Assert.AreEqual("暴击-999", DamageNumberLayout.FormatText(999, NumberKind.Crit));
        }

        [Test]
        public void CritGlyphScale_Is1Point3TimesNormal()
        {
            Assert.AreEqual(DamageNumberLayout.BaseGlyphScale * 1.3f, DamageNumberLayout.GlyphScale(NumberKind.Crit), 1e-5f);
            Assert.AreEqual(DamageNumberLayout.BaseGlyphScale, DamageNumberLayout.GlyphScale(NumberKind.Normal), 1e-5f);
            Assert.AreEqual(DamageNumberLayout.BaseGlyphScale, DamageNumberLayout.GlyphScale(NumberKind.Heal), 1e-5f);
        }

        [Test]
        public void GlyphScale_GivesSpecSizedDigits_AndNormalIsTintedRed()
        {
            // 64 高字格 × 缩放 ≈ 58~64px → 字形约 40px+、描边 ≥3px(spec §1);暴击再 ×1.3
            float cell = 64f * DamageNumberLayout.GlyphScale(NumberKind.Normal);
            Assert.GreaterOrEqual(cell, 56f);
            Assert.LessOrEqual(cell, 70f);
            Assert.GreaterOrEqual(3.2f * DamageNumberLayout.BaseGlyphScale, 3f, "描边(源 3.2px)缩放后应 ≥3px");
            var tint = DamageNumberLayout.GlyphTint(NumberKind.Normal);
            Assert.Greater(tint.r, 0.9f);
            Assert.Less(tint.g, 0.4f);
            Assert.Less(tint.b, 0.4f);
            Assert.AreEqual(Color.white, DamageNumberLayout.GlyphTint(NumberKind.Crit));
            Assert.AreEqual(Color.white, DamageNumberLayout.GlyphTint(NumberKind.Heal));
        }

        [Test]
        public void GroupOffsetX_AlternatesSides_ByTargetIndex()
        {
            Assert.AreEqual(0f, DamageNumberLayout.GroupOffsetX(0));
            Assert.AreEqual(20f, DamageNumberLayout.GroupOffsetX(1));
            Assert.AreEqual(-20f, DamageNumberLayout.GroupOffsetX(2));
            Assert.AreEqual(40f, DamageNumberLayout.GroupOffsetX(3));
            Assert.AreEqual(-40f, DamageNumberLayout.GroupOffsetX(4));
        }

        [Test]
        public void AvoidOffset_AlternatesSides_AndRises()
        {
            Assert.AreEqual(Vector2.zero, DamageNumberLayout.AvoidOffset(0));
            var o1 = DamageNumberLayout.AvoidOffset(1);
            var o2 = DamageNumberLayout.AvoidOffset(2);
            var o3 = DamageNumberLayout.AvoidOffset(3);
            Assert.Greater(o1.x, 0f);
            Assert.Less(o2.x, 0f);
            Assert.Greater(o3.x, o1.x, "第三个再往外错一档");
            Assert.Less(o1.y, 0f, "越叠越往上(y 向下为正)");
            Assert.Less(o2.y, o1.y);
            Assert.Less(o3.y, o2.y);
        }

        [Test]
        public void CountCollisions_RespectsWindowAndRadius()
        {
            var recent = new List<(float x, float time)>
            {
                (100f, 10f),   // 近 & 新
                (120f, 9.9f),  // 近 & 新
                (100f, 1f),    // 太旧
                (900f, 10f),   // 太远
            };
            Assert.AreEqual(2, DamageNumberLayout.CountCollisions(recent, 110f, 10.1f));
            Assert.AreEqual(0, DamageNumberLayout.CountCollisions(null, 110f, 10.1f));
            Assert.AreEqual(1, DamageNumberLayout.CountCollisions(recent, 900f, 10.1f));
        }

        [Test]
        public void FxPlacement_CenterPivot_And_FeetPivot()
        {
            var center = BattleFxPlayer.PlacementRect(new Vector2(500f, 400f), 300f, new Vector2(0.5f, 0.5f));
            Assert.AreEqual(350f, center.x, 1e-3f);
            Assert.AreEqual(250f, center.y, 1e-3f);
            Assert.AreEqual(300f, center.width, 1e-3f);

            // 地面型 pivot (0.5, 0.1):锚点在脚底,贴图 90% 在脚底之上
            var feet = BattleFxPlayer.PlacementRect(new Vector2(500f, 400f), 300f, new Vector2(0.5f, 0.1f));
            Assert.AreEqual(350f, feet.x, 1e-3f);
            Assert.AreEqual(400f - 270f, feet.y, 1e-3f);
        }

        [Test]
        public void ResultRewardLines_ListEachRewardAndState()
        {
            var settlement = new BattleSettlementData { ExpGain = 120, GoldGain = 30, Health = 55, Mana = 12, IsDead = true };
            settlement.ItemsGained.Add(new BattleItemEntry { ItemTableId = 1001, Count = 2 });
            settlement.ItemsConsumed.Add(new BattleItemEntry { ItemTableId = 7, Count = 1 });
            var lines = BattleResultPanel.BuildRewardLines(settlement);
            Assert.AreEqual(5, lines.Count);
            StringAssert.Contains("+120", lines[0]);
            StringAssert.Contains("+30", lines[1]);
            StringAssert.Contains("1001", lines[2]);
            StringAssert.Contains("道具7", lines[3]);
            StringAssert.Contains("阵亡", lines[4]);

            var none = BattleResultPanel.BuildRewardLines(null);
            Assert.AreEqual(1, none.Count);
        }
    }
}
