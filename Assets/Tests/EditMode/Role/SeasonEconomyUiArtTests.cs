using System.Collections.Generic;
using MmorpgClient.UI.Ugui;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Role
{
    public sealed class SeasonEconomyUiArtTests
    {
        [Test]
        public void IntentAndBossIcons_CoverAllEightSeasonEconomyIntentAssets()
        {
            var seen = new HashSet<string>();
            for (uint kind = 1u; kind <= 4u; ++kind)
            {
                SeasonEconomyUiArt.BossCardIntentIcons(kind, out string primary, out string secondary);
                Assert.That(primary, Is.Not.Empty);
                Assert.That(secondary, Is.Not.Empty);
                seen.Add(primary);
                seen.Add(secondary);
            }
            Assert.That(seen.Count, Is.EqualTo(8), "四张首领试炼卡应完整覆盖全部 8 张意图图标");
            Assert.That(SeasonEconomyUiArt.IntentIconKey(1u), Is.EqualTo("intent_slow_cleave"));
            Assert.That(SeasonEconomyUiArt.IntentIconKey(2u), Is.EqualTo("intent_guard_heal"));
            Assert.That(SeasonEconomyUiArt.IntentIconKey(3u), Is.EqualTo("intent_stance_phys"));
            Assert.That(SeasonEconomyUiArt.IntentIconKey(4u), Is.EqualTo("intent_stance_magic"));
            Assert.That(SeasonEconomyUiArt.IntentIconKey(5u), Is.EqualTo("intent_guard_shield"));
            Assert.That(SeasonEconomyUiArt.IntentIconKey(6u), Is.EqualTo("intent_guard_bind"));
            Assert.That(SeasonEconomyUiArt.IntentIconKey(7u), Is.EqualTo("intent_magic_strike"));
            Assert.That(SeasonEconomyUiArt.IntentIconKey(8u), Is.EqualTo("intent_venom_aoe"));
            Assert.That(SeasonEconomyUiArt.IntentIconKey(99u), Is.EqualTo("intent_slow_cleave"));
        }

        [Test]
        public void QualityTagAndMasteryBadge_MapAllTiersDeterministically()
        {
            Assert.That(SeasonEconomyUiArt.QualityTagKey(0u), Is.EqualTo(string.Empty));
            Assert.That(SeasonEconomyUiArt.QualityTagKey(1u), Is.EqualTo("quality_tag_1_common"));
            Assert.That(SeasonEconomyUiArt.QualityTagKey(2u), Is.EqualTo("quality_tag_2_good"));
            Assert.That(SeasonEconomyUiArt.QualityTagKey(3u), Is.EqualTo("quality_tag_3_fine"));
            Assert.That(SeasonEconomyUiArt.QualityTagKey(4u), Is.EqualTo("quality_tag_4_precious"));
            Assert.That(SeasonEconomyUiArt.QualityTagKey(5u), Is.EqualTo("quality_tag_5_supreme"));
            Assert.That(SeasonEconomyUiArt.QualityTagKey(9u), Is.EqualTo("quality_tag_5_supreme"));

            Assert.That(SeasonEconomyUiArt.MasteryBadgeKey(0u), Is.EqualTo("mastery_badge_apprentice"));
            Assert.That(SeasonEconomyUiArt.MasteryBadgeKey(1u), Is.EqualTo("mastery_badge_apprentice"));
            Assert.That(SeasonEconomyUiArt.MasteryBadgeKey(2u), Is.EqualTo("mastery_badge_artisan"));
            Assert.That(SeasonEconomyUiArt.MasteryBadgeKey(3u), Is.EqualTo("mastery_badge_grandmaster"));

            Assert.That(SeasonEconomyUiArt.GrowthSkillIconKey(12020u), Is.EqualTo("skill_yu_zhan_yu_yong"));
            Assert.That(SeasonEconomyUiArt.GrowthSkillIconKey(12021u), Is.EqualTo("skill_shen_mou_yuan_lv"));
            Assert.That(SeasonEconomyUiArt.GrowthSkillIconKey(12022u), Is.EqualTo("skill_bu_bu_wei_ying"));
            Assert.That(SeasonEconomyUiArt.GrowthSkillIconKey(1001u), Is.EqualTo(string.Empty));
        }

        [Test]
        public void TryFindProtectedGuardHp_ReadsAuthoritativeActorHealth()
        {
            var state = new BattleStateS2C();
            state.Actors.Add(new BattleActorState
            {
                ActorId = 9002UL,
                Name = "回春护卫",
                MaxHealth = 4200UL,
                Attributes = new BaseAttributesComp { Health = 2940UL },
            });

            Assert.That(SeasonEconomyUiArt.TryFindProtectedGuardHp(state, 9002UL, out string name, out ulong hp, out ulong maxHp), Is.True);
            Assert.That(name, Is.EqualTo("回春护卫"));
            Assert.That(hp, Is.EqualTo(2940UL));
            Assert.That(maxHp, Is.EqualTo(4200UL));
            Assert.That(SeasonEconomyUiArt.TryFindProtectedGuardHp(state, 9999UL, out _, out _, out _), Is.False);
            Assert.That(SeasonEconomyUiArt.TryFindProtectedGuardHp(null, 9002UL, out _, out _, out _), Is.False);
        }

        [Test]
        public void WeeklyResetAndProgressRatio_HandleBoundaryValues()
        {
            const ulong nowMs = 1_760_000_000_000UL;
            const ulong hourMs = 3_600_000UL;
            const ulong dayMs = 24UL * hourMs;
            Assert.That(SeasonEconomyUiArt.FormatWeeklyResetRemaining(nowMs + 3UL * dayMs + 14UL * hourMs, nowMs),
                Is.EqualTo("剩余 3天14时"));
            Assert.That(SeasonEconomyUiArt.FormatWeeklyResetRemaining(nowMs + 5UL * hourMs + 20UL * 60_000UL, nowMs),
                Is.EqualTo("剩余 5小时20分"));
            Assert.That(SeasonEconomyUiArt.FormatWeeklyResetRemaining(nowMs + 10_000UL, nowMs),
                Is.EqualTo("剩余 1分"));
            Assert.That(SeasonEconomyUiArt.FormatWeeklyResetRemaining(nowMs, nowMs), Is.EqualTo(string.Empty));
            Assert.That(SeasonEconomyUiArt.FormatWeeklyResetRemaining(0UL, nowMs), Is.EqualTo(string.Empty));

            Assert.That(SeasonEconomyUiArt.ComputeProgressRatio(0UL, 0UL), Is.EqualTo(0f));
            Assert.That(SeasonEconomyUiArt.ComputeProgressRatio(7UL, 20UL), Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(SeasonEconomyUiArt.ComputeProgressRatio(19UL, 20UL, forceFull: true), Is.EqualTo(1f));
            Assert.That(SeasonEconomyUiArt.ComputeProgressRatio(999UL, 660UL), Is.EqualTo(1f));
        }
    }
}
