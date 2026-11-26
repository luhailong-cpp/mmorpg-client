using System.Collections.Generic;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.CoreLoop;
using MmorpgClient.UI.Ugui;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 战前每方 3 Ban 纯 C# 领域逻辑与切片资源单测：
    /// 覆盖 4 页签分组、常用 3 禁持久化及跨赛季自动求交集过滤、提名上限、同队票数统计、禁招查表与切磋规则摘要。
    /// </summary>
    public sealed class BattleSkillBanTests
    {
        [Test]
        public void PreferenceStore_RoundTrip_FiltersExpiredSeasonSkillsAndCapsAtMaxSlots()
        {
            var mem = new Dictionary<string, string>();
            BattleSkillBanModels.PreferenceStore.Save(
                1001ul,
                new uint[] { 10020u, 15001u, 10038u, 10020u, 10040u },
                (k, v) => mem[k] = v);

            // 模拟切到 S2 后 15001 已不在服务端候选池中
            var s2Candidates = new uint[] { 10020u, 10038u, 10040u, 15101u };
            var loaded = BattleSkillBanModels.PreferenceStore.LoadFiltered(
                1001ul, s2Candidates, 3u, k => mem.TryGetValue(k, out string v) ? v : string.Empty);

            Assert.That(loaded, Is.EqualTo(new[] { 10020u, 10038u, 10040u }));

            // 其他账号隔离
            var other = BattleSkillBanModels.PreferenceStore.LoadFiltered(
                2002ul, s2Candidates, 3u, k => mem.TryGetValue(k, out string v) ? v : string.Empty);
            Assert.That(other, Is.Empty);
        }

        [Test]
        public void GroupCandidatesByCategory_SplitsFourTabsAndSkipsExempt()
        {
            var candidates = new uint[] { 10002u, 10020u, 10003u, 10040u, 11013u, 10020u };
            uint Lookup(uint id) => id switch
            {
                10002u => 0u, // 基础豁免
                10020u => 1u, // 控场
                10003u => 2u, // 输出
                10040u => 3u, // 辅助
                11013u => 4u, // 灵宠
                _ => 0u,
            };

            var grouped = BattleSkillBanModels.GroupCandidatesByCategory(candidates, Lookup);
            Assert.That(grouped.Length, Is.EqualTo(4));
            Assert.That(grouped[0], Is.EqualTo(new[] { 10020u }));
            Assert.That(grouped[1], Is.EqualTo(new[] { 10003u }));
            Assert.That(grouped[2], Is.EqualTo(new[] { 10040u }));
            Assert.That(grouped[3], Is.EqualTo(new[] { 11013u }));
        }

        [Test]
        public void ToggleNomination_EnforcesSlotLimitAndRemovesExisting()
        {
            var list = new List<uint>();
            Assert.That(BattleSkillBanModels.ToggleNomination(list, 10020u, 3u), Is.True);
            Assert.That(BattleSkillBanModels.ToggleNomination(list, 10023u, 3u), Is.True);
            Assert.That(BattleSkillBanModels.ToggleNomination(list, 10038u, 3u), Is.True);
            Assert.That(BattleSkillBanModels.ToggleNomination(list, 10040u, 3u), Is.False);
            Assert.That(list, Is.EqualTo(new[] { 10020u, 10023u, 10038u }));

            // 取消已选后可再加入新提名
            Assert.That(BattleSkillBanModels.ToggleNomination(list, 10023u, 3u), Is.True);
            Assert.That(BattleSkillBanModels.ToggleNomination(list, 10040u, 3u), Is.True);
            Assert.That(list, Is.EqualTo(new[] { 10020u, 10038u, 10040u }));
        }

        [Test]
        public void BannedLookup_VoteCount_AndCustomDuelSummary()
        {
            var state = new BattleStateS2C();
            state.BannedSkillIds.Add(10020u);
            state.BannedSkillIds.Add(10038u);
            Assert.That(BattleSkillBanModels.IsSkillBanned(state, 10020u), Is.True);
            Assert.That(BattleSkillBanModels.IsSkillBanned(state, 10002u), Is.False);

            var prep = new BattlePrepState { Active = true, SkillBanSlotsPerTeam = 3u };
            var p1 = new BattlePrepPlayerState { PlayerId = 1ul, TeamIndex = 0u };
            p1.NominatedBanSkillIds.Add(10020u);
            p1.NominatedBanSkillIds.Add(10038u);
            var p2 = new BattlePrepPlayerState { PlayerId = 2ul, TeamIndex = 0u };
            p2.NominatedBanSkillIds.Add(10020u);
            prep.TeamStates.Add(p1);
            prep.TeamStates.Add(p2);

            Assert.That(BattleSkillBanModels.CountTeamVotes(prep, 10020u), Is.EqualTo(2));
            Assert.That(BattleSkillBanModels.CountTeamVotes(prep, 10038u), Is.EqualTo(1));
            Assert.That(BattleSkillBanModels.CountTeamVotes(prep, 10040u), Is.EqualTo(0));

            string summary = BattleSkillBanModels.FormatTeamBansSummary(
                new uint[] { 10020u }, new uint[] { 10038u }, id => $"技{id}");
            Assert.That(summary, Is.EqualTo("A队禁：技10020  |  B队禁：技10038"));

            var duelRules = new global::CustomDuelRules
            {
                UseFreePreset = false,
                SkillBanSlotsPerTeam = 3u,
                AllowPublicSpectate = true,
            };
            Assert.That(WuxingExpansionModels.FormatCustomDuelRulesSummary(duelRules), Does.Contain("每方 3 禁招"));
        }

        [Test]
        public void AllEightBattlePrepSkillBanSlicesLoadFromResources()
        {
            string[] expectedSlices =
            {
                "prep_ban_slot_empty",
                "prep_ban_slot_filled",
                "prep_ban_candidate_normal",
                "prep_ban_candidate_selected",
                "prep_ban_tab_active",
                "prep_ban_tab_inactive",
                "skill_banned_stamp",
                "battle_ban_strip_bg",
            };
            for (int i = 0; i < expectedSlices.Length; i++)
            {
                var sprite = SeasonEconomyUiArt.LoadBattlePrepSkillBanSlice(expectedSlices[i]);
                Assert.That(sprite, Is.Not.Null, $"缺少切片资源: UI/Ugui/BattlePrepSkillBan/{expectedSlices[i]}");
            }
        }
    }
}
