using System;
using System.Collections.Generic;
using Google.Protobuf.WellKnownTypes;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.CityNpc;
using MmorpgClient.Game.Team;
using MmorpgClient.Tests.EditMode.Tianyong;
using NUnit.Framework;
using Teampb;

namespace MmorpgClient.Tests.EditMode.CityNpc
{
    /// <summary>
    /// 归元城 8 位原创核心 NPC、全仓去问道化口径、演武木桩/残局/奇谈、顺路捎物、
    /// 灵潮五行签文、人物江湖名帖与收藏系列同游气泡的纯 C# 单元测试。
    /// </summary>
    public sealed class CityNpcPlayTests
    {
        private static readonly string[] OfficialNineAppearanceIds =
        {
            "00_reference_topright_boy",
            "01_ice_sword_girl",
            "02_fire_talisman_boy",
            "03_lotus_healer_girl",
            "05_celestial_musician_girl",
            "09_bamboo_archer_girl",
            "10_crimson_spear_girl",
            "15_water_dragon_scholar_boy",
            "20_star_formation_master_girl"
        };

        private static readonly string[] BannedLegacyNames =
        {
            "天墉城", "八仙岛", "东海渔村", "揽仙镇", "聚宝斋",
            "杨镖头", "屠娇娇", "董老头", "辛仁皑", "段铁心",
            "无想僧", "无意僧", "北斗星使", "赤灵尊神", "仙界神捕",
            "管神工", "天机老人", "多闻道人", "问道"
        };

        private static readonly (float X, float Z)[] MysteryMerchantCitySpots =
        {
            (222.5f, 58.5f),
            (125.0f, 136.0f),
            (116.25f, 186.0f),
            (261.25f, 185.375f),
            (80.0f, 53.5f),
            (220.0f, 124.125f)
        };

        [Test]
        public void Catalog_HasEightOriginalNpcs_ZeroLegacyIpNames_AndNonOverlappingCoordinates()
        {
            Assert.That(CityNpcCatalog.Npcs.Count, Is.EqualTo(8));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < CityNpcCatalog.Npcs.Count; ++i)
            {
                var npc = CityNpcCatalog.Npcs[i];
                Assert.That(ids.Add(npc.Id), Is.True, $"Duplicate NPC id: {npc.Id}");
                Assert.That(names.Add(npc.Name), Is.True, $"Duplicate NPC name: {npc.Name}");
                AssertNoBannedLegacyName(npc.Name);
                AssertNoBannedLegacyName(npc.RoleTitle);
                AssertNoBannedLegacyName(npc.LandmarkLabel);
                AssertNoBannedLegacyName(npc.DefaultGreeting);
                AssertNoBannedLegacyName(npc.AmbientBubble);

                // 坐标均在归元城 biyuan-body-20261123 合法可走区域内，且与神秘商人 6 处主城点错开
                Assert.That(npc.WorldX, Is.InRange(30f, 300f));
                Assert.That(npc.WorldZ, Is.InRange(20f, 260f));
                for (int m = 0; m < MysteryMerchantCitySpots.Length; ++m)
                {
                    float dx = npc.WorldX - MysteryMerchantCitySpots[m].X;
                    float dz = npc.WorldZ - MysteryMerchantCitySpots[m].Z;
                    float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                    Assert.That(dist, Is.GreaterThanOrEqualTo(10f),
                        $"NPC {npc.Name} overlaps mystery merchant spot #{m} (dist={dist:F2})");
                }

                for (int j = i + 1; j < CityNpcCatalog.Npcs.Count; ++j)
                {
                    var other = CityNpcCatalog.Npcs[j];
                    float dx = npc.WorldX - other.WorldX;
                    float dz = npc.WorldZ - other.WorldZ;
                    float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                    Assert.That(dist, Is.GreaterThanOrEqualTo(20f),
                        $"NPC {npc.Name} too close to {other.Name} (dist={dist:F2})");
                }
            }

            // 地名映射表同样 100% 去问道化
            Assert.That(CityNpcCatalog.ResolveMapDisplayName(1u), Is.EqualTo("归元城"));
            Assert.That(CityNpcCatalog.ResolveMapDisplayName(2u), Is.EqualTo("八景岛"));
            Assert.That(CityNpcCatalog.ResolveMapDisplayName(3u), Is.EqualTo("月汐湾"));
            Assert.That(CityNpcCatalog.ResolveMapDisplayName(4u), Is.EqualTo("青石渡"));
            Assert.That(CityNpcCatalog.ResolveMapDisplayName(5u), Is.EqualTo("碧渊水宫"));
        }

        [Test]
        public void CharacterGreetings_CoverAllNineOfficialCharactersAcrossAllEightNpcs()
        {
            for (int n = 0; n < CityNpcCatalog.Npcs.Count; ++n)
            {
                var npc = CityNpcCatalog.Npcs[n];
                for (int c = 0; c < OfficialNineAppearanceIds.Length; ++c)
                {
                    string appearanceId = OfficialNineAppearanceIds[c];
                    bool matched = npc.TryGetCharacterGreeting(appearanceId, out string line);
                    Assert.That(matched, Is.True, $"NPC {npc.Name} missing greeting for {appearanceId}");
                    Assert.That(string.IsNullOrWhiteSpace(line), Is.False);
                    AssertNoBannedLegacyName(line);
                }

                bool unknownMatched = npc.TryGetCharacterGreeting("unknown_custom_id", out string fallback);
                Assert.That(unknownMatched, Is.False);
                Assert.That(fallback, Is.EqualTo(npc.DefaultGreeting));
            }
        }

        [Test]
        public void EncountersAndErrands_RespectPresetAndZeroLegacyRules()
        {
            Assert.That(CityNpcCatalog.Encounters.Count, Is.EqualTo(8));
            int dummies = 0, puzzles = 0, tales = 0;
            for (int i = 0; i < CityNpcCatalog.Encounters.Count; ++i)
            {
                var e = CityNpcCatalog.Encounters[i];
                AssertNoBannedLegacyName(e.Title);
                AssertNoBannedLegacyName(e.Subtitle);
                AssertNoBannedLegacyName(e.StoryIntro);
                AssertNoBannedLegacyName(e.HiddenEpilogueBody);

                switch (e.Category)
                {
                    case CityNpcEncounterCategory.PracticeDummy:
                        ++dummies;
                        Assert.That(e.AllowsFreePreset, Is.False, "演武试招木桩必须强制本人真实养成");
                        Assert.That(e.RecommendedCharacterPresetId, Is.EqualTo(0u));
                        Assert.That(e.RecommendedPetPresetId, Is.EqualTo(0u));
                        break;
                    case CityNpcEncounterCategory.TacticalPuzzle:
                        ++puzzles;
                        Assert.That(e.AllowsFreePreset, Is.True);
                        Assert.That(e.RecommendedCharacterPresetId, Is.GreaterThan(0u));
                        Assert.That(e.RecommendedPetPresetId, Is.GreaterThan(0u));
                        break;
                    case CityNpcEncounterCategory.CharacterTale:
                        ++tales;
                        Assert.That(e.AllowsFreePreset, Is.True);
                        Assert.That(string.IsNullOrWhiteSpace(e.HiddenEpilogueTitle), Is.False);
                        Assert.That(string.IsNullOrWhiteSpace(e.HiddenEpilogueBody), Is.False);
                        break;
                }
            }
            Assert.That(dummies, Is.EqualTo(2));
            Assert.That(puzzles, Is.EqualTo(3));
            Assert.That(tales, Is.EqualTo(3));
            Assert.That(CityNpcCatalog.Errands.Count, Is.EqualTo(3));
            for (int i = 0; i < CityNpcCatalog.Errands.Count; ++i)
            {
                AssertNoBannedLegacyName(CityNpcCatalog.Errands[i].Title);
                AssertNoBannedLegacyName(CityNpcCatalog.Errands[i].TargetSceneName);
                AssertNoBannedLegacyName(CityNpcCatalog.Errands[i].ParcelDesc);
                AssertNoBannedLegacyName(CityNpcCatalog.Errands[i].CompletionNote);
            }
        }

        [Test]
        public void DrawWuxingFortune_IsDeterministicPerPlayerDayAndTide()
        {
            ulong day1UtcMs = 1_764_120_000_000UL;
            ulong day2UtcMs = day1UtcMs + 86_400_000UL;

            var f1 = CityNpcCatalog.DrawWuxingFortune(10001UL, day1UtcMs, 2u);
            var f1Again = CityNpcCatalog.DrawWuxingFortune(10001UL, day1UtcMs + 3_600_000UL, 2u);
            var fTide5 = CityNpcCatalog.DrawWuxingFortune(10001UL, day1UtcMs, 5u);
            var fDay2 = CityNpcCatalog.DrawWuxingFortune(10001UL, day2UtcMs, 2u);

            Assert.That(f1.DateKeyUtc8, Is.EqualTo(f1Again.DateKeyUtc8));
            Assert.That(f1.ElementName, Is.EqualTo(f1Again.ElementName));
            Assert.That(f1.SignGrade, Is.EqualTo(f1Again.SignGrade));
            Assert.That(f1.OverheadTag, Is.EqualTo(f1Again.OverheadTag));
            Assert.That(f1.TravelHint, Does.Contain("八景岛"));
            Assert.That(fTide5.TravelHint, Does.Contain("碧渊水宫"));
            Assert.That(fDay2.DateKeyUtc8, Is.Not.EqualTo(f1.DateKeyUtc8));
        }

        [Test]
        public void DeriveBuildStyleTag_DetectsPhysicalMagicSpeedTankSupportAndBalanced()
        {
            Assert.That(CityNpcCatalog.DeriveBuildStyleTag(null), Is.EqualTo("五行均衡"));
            Assert.That(CityNpcCatalog.DeriveBuildStyleTag(MakePanel(0, 0, 0, 0)), Is.EqualTo("初入归元"));
            Assert.That(CityNpcCatalog.DeriveBuildStyleTag(MakePanel(10, 10, 10, 10)), Is.EqualTo("五行均衡"));
            Assert.That(CityNpcCatalog.DeriveBuildStyleTag(MakePanel(5, 5, 40, 10)), Is.EqualTo("锋芒力破"));
            Assert.That(CityNpcCatalog.DeriveBuildStyleTag(MakePanel(5, 45, 5, 5)), Is.EqualTo("玄法通灵"));
            Assert.That(CityNpcCatalog.DeriveBuildStyleTag(MakePanel(5, 5, 10, 40)), Is.EqualTo("疾敏先手"));
            Assert.That(CityNpcCatalog.DeriveBuildStyleTag(MakePanel(42, 6, 6, 6)), Is.EqualTo("固本玄守"));
            Assert.That(CityNpcCatalog.DeriveBuildStyleTag(MakePanel(25, 25, 0, 10, healBps: 1500, shieldBps: 800)),
                Is.EqualTo("济世护主"));
        }

        [Test]
        public void TryMatchSeriesCoopBubble_MatchesAllThreeSeriesAndRejectsUnrelatedCompanions()
        {
            Assert.That(CityNpcCatalog.TryMatchSeriesCoopBubble(
                "00_reference_topright_boy", new[] { "10_crimson_spear_girl" }, 1, out var s1), Is.True);
            Assert.That(s1.SeriesName, Is.EqualTo("留灯院旧友"));

            Assert.That(CityNpcCatalog.TryMatchSeriesCoopBubble(
                "01_ice_sword_girl", new[] { "02_fire_talisman_boy" }, 2, out var s2), Is.True);
            Assert.That(s2.SeriesName, Is.EqualTo("水火冰锋"));

            Assert.That(CityNpcCatalog.TryMatchSeriesCoopBubble(
                "03_lotus_healer_girl", new[] { "05_celestial_musician_girl" }, 3, out var s3), Is.True);
            Assert.That(s3.SeriesName, Is.EqualTo("清音济世"));

            Assert.That(CityNpcCatalog.TryMatchSeriesCoopBubble(
                "20_star_formation_master_girl", new[] { "01_ice_sword_girl" }, 1, out _), Is.False);
        }

        [Test]
        public void CityNpcState_Lifecycle_Errands_BattleVictory_FortuneAndPlayerIsolation()
        {
            var state = new CityNpcState();
            state.BindPlayer(8801UL);

            // 1. 顺路捎物：接单 -> 进入无关图不变 -> 进入目标图八景岛(2)送达 -> 归档计数 +1
            Assert.That(state.AcceptErrand("errand_bajing_ink"), Is.True);
            Assert.That(state.AcceptErrand("errand_bajing_ink"), Is.False);
            Assert.That(state.GetErrandStatus("errand_bajing_ink"), Is.EqualTo(CityNpcErrandStatus.Active));
            Assert.That(state.ObserveAuthoritativeScene(1u), Is.False);
            Assert.That(state.ObserveAuthoritativeScene(3u), Is.False);
            Assert.That(state.ObserveAuthoritativeScene(2u), Is.True);
            Assert.That(state.GetErrandStatus("errand_bajing_ink"), Is.EqualTo(CityNpcErrandStatus.ReadyToArchive));
            Assert.That(state.ArchiveErrand("errand_bajing_ink"), Is.True);
            Assert.That(state.ArchivedErrandCount, Is.EqualTo(1));

            // 2. 战斗结算：木桩(301)/逃跑/战败不点亮；残局(311)与奇谈(321)权威胜利点亮一次
            Assert.That(state.ObserveAuthoritativeBattleEnd(MakeBattleEnd(301u, win: true)), Is.False);
            Assert.That(state.ObserveAuthoritativeBattleEnd(MakeBattleEnd(311u, win: false)), Is.False);
            Assert.That(state.ObserveAuthoritativeBattleEnd(MakeBattleEnd(311u, win: true, fled: true)), Is.False);
            Assert.That(state.ObserveAuthoritativeBattleEnd(MakeBattleEnd(311u, win: true)), Is.True);
            Assert.That(state.ObserveAuthoritativeBattleEnd(MakeBattleEnd(311u, win: true)), Is.False);
            Assert.That(state.IsPuzzleCleared(311u), Is.True);
            Assert.That(state.ClearedPuzzleCount, Is.EqualTo(1));

            Assert.That(state.ObserveAuthoritativeBattleEnd(MakeBattleEnd(321u, win: true, playerTeamIndex: 1u)), Is.True);
            Assert.That(state.IsTaleCleared(321u), Is.True);
            Assert.That(state.ClearedTaleCount, Is.EqualTo(1));

            // 3. 签文 30 分钟有效气泡 + 同行之约预设轮换
            var f = state.DrawFortune(1_764_120_000_000UL, 3u, 100d);
            Assert.That(state.ActiveFortuneOverheadTag(500d), Is.EqualTo(f.OverheadTag));
            Assert.That(state.ActiveFortuneOverheadTag(1901d), Is.EqualTo(""));

            string oath0 = state.SelectedCompanionOathPhrase;
            state.CycleCompanionOath();
            Assert.That(state.SelectedCompanionOathPhrase, Is.Not.EqualTo(oath0));
            Assert.That(CityNpcCatalog.CompanionOathPresets, Does.Contain(state.SelectedCompanionOathPhrase));

            // 4. 江湖名帖文本生成
            var card = new CharacterCallingCardView(
                "青莲小道", 65u, "苏青莲（青莲）", "03_lotus_healer_girl",
                "济世护主", state.SelectedCompanionOathPhrase, 6, 9,
                new[] { "清音济世" }, new[] { "初季登峰" }, 12, "碧玉灵狐",
                state.ClearedPuzzleCount, state.ClearedTaleCount, state.ArchivedErrandCount);
            string shareText = card.FormatShareText();
            Assert.That(shareText, Does.Contain("青莲小道"));
            Assert.That(shareText, Does.Contain("济世护主"));
            Assert.That(shareText, Does.Contain("清音济世"));
            Assert.That(shareText, Does.Contain("演武残局：1/3"));
            Assert.That(shareText, Does.Contain("归元奇谈：1/3"));
            AssertNoBannedLegacyName(shareText);

            // 5. 换号/断线必须完全清空会话态
            state.BindPlayer(8802UL);
            Assert.That(state.ClearedPuzzleCount, Is.EqualTo(0));
            Assert.That(state.ClearedTaleCount, Is.EqualTo(0));
            Assert.That(state.ArchivedErrandCount, Is.EqualTo(0));
            Assert.That(state.LastFortune.HasValue, Is.False);
        }

        [Test]
        public void BattleMatchEntryClient_StartNpcEncounter_QueuesSoloWithOptionalPresetAndRejectsTeam()
        {
            float now = 100f;
            var teamNet = new TeamFakeTransport();
            var battleNet = new TeamFakeTransport();
            using var team = new TeamClient(teamNet, () => teamNet.Identity, _ => { }, _ => { }, () => now);
            var battle = new BattleClient(battleNet);
            var entry = new BattleMatchEntryClient(team, battle, 1u, 5u, () => now);

            // 初始化无队状态
            team.DrainQueued(false);
            teamNet.Reply(new TeamResponse { Team = new TeamView { TeamId = 0, Version = 1, MembershipEpoch = 1 } });
            now += 1f;

            // 1. 演武木桩 301：无预设入队
            Assert.That(entry.StartNpcEncounter(301u), Is.True);
            Assert.That(battleNet.Calls.Count, Is.EqualTo(1));
            var dummyReq = (Match.JoinQueueRequest)battleNet.Calls[0].Request;
            Assert.That(dummyReq.Mode, Is.EqualTo(Match.MatchMode.PveSolo));
            Assert.That(dummyReq.BattleConfigId, Is.EqualTo(301u));
            Assert.That(dummyReq.CharacterPresetId, Is.EqualTo(0u));
            Assert.That(dummyReq.PetPresetId, Is.EqualTo(0u));

            battleNet.Reply(new Match.JoinQueueResponse { QueueTicket = "npc-dummy-1" });
            Assert.That(entry.Cancel(), Is.True);
            battleNet.Reply(new Empty());
            now += 2f;

            // 2. 残局 / 奇谈 321：携带免费预设 (2, 5)
            Assert.That(entry.StartNpcEncounter(321u, 2u, 5u), Is.True);
            var taleReq = (Match.JoinQueueRequest)battleNet.Calls[2].Request;
            Assert.That(taleReq.Mode, Is.EqualTo(Match.MatchMode.PveSolo));
            Assert.That(taleReq.BattleConfigId, Is.EqualTo(321u));
            Assert.That(taleReq.CharacterPresetId, Is.EqualTo(2u));
            Assert.That(taleReq.PetPresetId, Is.EqualTo(5u));

            battleNet.Reply(new Match.JoinQueueResponse { QueueTicket = "npc-tale-1" });
            Assert.That(entry.Cancel(), Is.True);
            battleNet.Reply(new Empty());
            now += 2f;

            // 3. 组队状态必须拒绝并提示单人体验
            var teamedView = new TeamView { TeamId = 99, LeaderId = 1, Version = 2, MembershipEpoch = 2 };
            teamedView.Members.Add(new TeamMemberView { PlayerId = 1, Name = "队长" });
            teamedView.Members.Add(new TeamMemberView { PlayerId = 2, Name = "队员" });
            team.RequestRefresh();
            team.DrainQueued(false);
            teamNet.Reply(new TeamResponse { Team = teamedView });
            now += 1f;

            Assert.That(entry.StartNpcEncounter(311u, 1u, 4u), Is.False);
            Assert.That(entry.Status, Does.Contain("单人"));
        }

        private static global::AttributePanelInfo MakePanel(
            uint body, uint spirit, uint power, uint agility, double healBps = 0d, double shieldBps = 0d)
        {
            var panel = new global::AttributePanelInfo { Level = 60 };
            panel.Dimensions.Add(new global::AttributeDimensionInfo { Name = "体质", Allocated = body });
            panel.Dimensions.Add(new global::AttributeDimensionInfo { Name = "灵力", Allocated = spirit });
            panel.Dimensions.Add(new global::AttributeDimensionInfo { Name = "力量", Allocated = power });
            panel.Dimensions.Add(new global::AttributeDimensionInfo { Name = "敏捷", Allocated = agility });
            if (healBps > 0d || shieldBps > 0d)
            {
                panel.SupportEffects = new global::SupportEffectSummary
                {
                    Healing = new global::SupportEffectBreakdown { Complete = true, TotalBonusBps = healBps },
                    Shield = new global::SupportEffectBreakdown { Complete = true, TotalBonusBps = shieldBps }
                };
            }
            return panel;
        }

        private static global::BattleEndS2C MakeBattleEnd(
            uint dungeonId, bool win, bool fled = false, uint playerTeamIndex = 0u)
        {
            var outcome = win
                ? (playerTeamIndex == 0u ? global::eBattleOutcome.BattleOutcomeSideAWin : global::eBattleOutcome.BattleOutcomeSideBWin)
                : (playerTeamIndex == 0u ? global::eBattleOutcome.BattleOutcomeSideBWin : global::eBattleOutcome.BattleOutcomeSideAWin);
            return new global::BattleEndS2C
            {
                Outcome = outcome,
                Settlement = new global::BattleSettlementData
                {
                    BattleConfigId = dungeonId,
                    PlayerTeamIndex = playerTeamIndex,
                    TotalRounds = 3u,
                    Fled = fled
                }
            };
        }

        private static void AssertNoBannedLegacyName(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            for (int i = 0; i < BannedLegacyNames.Length; ++i)
            {
                Assert.That(text, Does.Not.Contain(BannedLegacyNames[i]),
                    $"Player-visible text contains legacy IP name '{BannedLegacyNames[i]}': {text}");
            }
        }
    }
}
