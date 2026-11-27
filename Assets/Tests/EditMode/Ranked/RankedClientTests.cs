using System;
using System.Collections.Generic;
using Google.Protobuf;
using Match;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Rank;
using MmorpgClient.Game.Ranked;
using MmorpgClient.Game.Team;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;
using Teampb;

namespace MmorpgClient.Tests.EditMode.Ranked
{
    public sealed class RankedClientTests
    {
        private sealed class FakeTransport : IBattleTransport
        {
            public sealed class SentCall
            {
                public uint MessageId;
                public IMessage Request;
                public Action<IMessage> Reply;
                public Action<string> Fail;
            }

            public ulong PlayerId { get; set; } = 1001UL;
            public bool IsReady { get; set; } = true;
            public event Action Disconnected;
            public readonly List<SentCall> Calls = new List<SentCall>();

            public void RegisterNotify(uint messageId, Action<MessageContent> handler) { }
            public void SendOneWay(uint messageId, IMessage request) { }

            public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                Action<TResp> onResponse, Action<string> onError) where TResp : IMessage<TResp>
            {
                Calls.Add(new SentCall
                {
                    MessageId = messageId,
                    Request = request,
                    Reply = msg => onResponse((TResp)msg),
                    Fail = onError,
                });
            }

            public void RaiseDisconnected() => Disconnected?.Invoke();
        }

        private FakeTransport _net;
        private object _conn;
        private float _now;
        private RankedClient _client;

        [SetUp]
        public void SetUp()
        {
            _net = new FakeTransport();
            _conn = new object();
            _now = 10f;
            _client = new RankedClient(_net, () => _conn, () => _now);
        }

        [TearDown]
        public void TearDown() => _client.Dispose();

        [Test]
        public void RequestProfile_And_SavePreset_And_UpdateCosmetic_OnlyCommitAfterServerConfirm()
        {
            Assert.That(_client.RequestProfile(true), Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
            Assert.That(_net.Calls[0].MessageId, Is.EqualTo(MatchServiceRpc.GetRankedProfileMessageId));

            var resp = new GetRankedProfileResponse
            {
                CurrentSeason = new RankedSeasonInfo
                {
                    SeasonId = 1, SeasonKey = "s1", SeasonName = "首测赛季",
                    RankGeneration = 1, RuleVersion = 1, State = RankedSeasonState.Active,
                    BattleConfigId = 5, WaitPromptSeconds = 120,
                },
                CurrentProgress = new RankedPlayerSeasonSummary
                {
                    SeasonId = 1, SeasonKey = "s1", Qualified = true,
                    RatingCenti = 154000, CurrentTier = RankedTier.Qingfeng, ValidMatches = 7,
                },
                SelectedPresets = new RankedPresetSelection { CharacterPresetId = 101, PetPresetId = 203 },
                CosmeticLoadout = new RankedCosmeticLoadout { EquippedAvatarPatternCode = "s1_avatar_pattern" },
            };
            resp.Presets.Add(new RankedPresetView { PresetId = 101, RoleKind = 1, Name = "破阵", Sort = 1 });
            resp.Presets.Add(new RankedPresetView { PresetId = 203, RoleKind = 2, Name = "护主", Sort = 3 });
            _net.Calls[0].Reply(resp);

            Assert.That(_client.Profile, Is.Not.Null);
            Assert.That(_client.Profile.CurrentProgress.ValidMatches, Is.EqualTo(7u));
            Assert.That(RankedFormatting.FormatRating(_client.Profile.CurrentProgress.RatingCenti), Is.EqualTo("1540"));
            Assert.That(RankedFormatting.TierDisplayName(_client.Profile.CurrentProgress.CurrentTier), Is.EqualTo("青锋"));

            // 保存预设：发送前本地不变，服务端拒单不污染，服务端确认后才更新
            Assert.That(_client.SavePreset(102, 201), Is.True);
            Assert.That(_client.SelectedCharacterPresetId, Is.EqualTo(101u));
            _net.Calls[1].Reply(new SaveRankedPresetResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)match_error.KMatchRankedPresetInvalid },
            });
            Assert.That(_client.SelectedCharacterPresetId, Is.EqualTo(101u));
            Assert.That(_client.LastError, Does.Contain("无效"));

            _now += 2f;
            Assert.That(_client.SavePreset(102, 201), Is.True);
            _net.Calls[2].Reply(new SaveRankedPresetResponse
            {
                SelectedPresets = new RankedPresetSelection { CharacterPresetId = 102, PetPresetId = 201 },
                RuleVersion = 1,
            });
            Assert.That(_client.SelectedCharacterPresetId, Is.EqualTo(102u));
            Assert.That(_client.SelectedPetPresetId, Is.EqualTo(201u));

            // 外观佩戴隐藏玉符开关
            _now += 2f;
            var nextCosmetic = new RankedCosmeticLoadout
            {
                EquippedAvatarPatternCode = "s1_avatar_pattern",
                HideTalisman = true,
            };
            Assert.That(_client.UpdateCosmetic(nextCosmetic), Is.True);
            _net.Calls[3].Reply(new UpdateRankedCosmeticResponse { Loadout = nextCosmetic });
            Assert.That(_client.Profile.CosmeticLoadout.HideTalisman, Is.True);
        }

        [Test]
        public void Settlement_PollsPendingUntilConfirmed_And_SeparatesDailyMaterialCapFromRating()
        {
            var end = new BattleEndS2C
            {
                BattleId = 8801UL,
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                SeasonId = 1,
                MaterialCreditUnitsAdded = 0, // 日额度已满 +0
                Settlement = new BattleSettlementData
                {
                    BattleId = 8801UL,
                    Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                    CompetitiveRanked = true,
                    SeasonId = 1,
                    MatchMode = (uint)MatchMode.PvpTeam,
                    TotalRounds = 5,
                },
            };
            var state = _client.BeginSettlementFromBattleEnd(end, 0, 2);
            Assert.That(state.IsRatingPending, Is.True);
            Assert.That(state.MaterialCreditConfirmed, Is.True);
            Assert.That(state.MaterialCreditUnitsAdded, Is.EqualTo(0u));
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
            Assert.That(_net.Calls[0].MessageId, Is.EqualTo(MatchServiceRpc.GetRankedBattleResultMessageId));

            // 第一次回 PENDING：不得显示推测分，安排退避轮询
            _net.Calls[0].Reply(new GetRankedBattleResultResponse
            {
                Result = new RankedBattleSettlementView
                {
                    BattleId = 8801UL, SeasonId = 1, Status = RankedMatchRatingStatus.RankedMatchRatingPending,
                },
            });
            Assert.That(_client.ActiveSettlement.IsRatingPending, Is.True);

            _now += 2.5f;
            _client.Tick();
            Assert.That(_net.Calls.Count, Is.EqualTo(2));

            // 第二次回 CONFIRMED（1540 -> 1556 跨段，同时触发刷新档案 RPC 448）
            _net.Calls[1].Reply(new GetRankedBattleResultResponse
            {
                Result = new RankedBattleSettlementView
                {
                    BattleId = 8801UL, SeasonId = 1, TeamSize = 2,
                    Status = RankedMatchRatingStatus.RankedMatchRatingConfirmed,
                    Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                    Participated = true,
                    RatingBeforeCenti = 154000, RatingAfterCenti = 155600, RatingDeltaCenti = 1600,
                    TierBefore = RankedTier.Qingfeng, TierAfter = RankedTier.Yunzhang,
                    SeasonValidMatchesAfter = 8,
                },
            });
            Assert.That(_client.ActiveSettlement.IsRatingConfirmed, Is.True);
            Assert.That(RankedFormatting.FormatRatingDelta(_client.ActiveSettlement.AuthoritativeView.RatingDeltaCenti), Is.EqualTo("+16"));
            Assert.That(_net.Calls.Count, Is.EqualTo(3));
            Assert.That(_net.Calls[2].MessageId, Is.EqualTo(MatchServiceRpc.GetRankedProfileMessageId));
        }

        [Test]
        public void DisconnectAndSessionChange_DiscardStaleCallbacks()
        {
            Assert.That(_client.RequestProfile(false), Is.True);
            var staleCall = _net.Calls[0];
            _net.RaiseDisconnected();
            Assert.That(_client.Profile, Is.Null);

            staleCall.Reply(new GetRankedProfileResponse
            {
                CurrentSeason = new RankedSeasonInfo { SeasonId = 9, State = RankedSeasonState.Settling },
            });
            Assert.That(_client.Profile, Is.Null, "断线前在途回包不得污染新会话");
        }

        [Test]
        public void BattleMatchEntry_BlocksFourStackFiveVFive_And_PassesPresetsOnSoloRankedQueue()
        {
            var teamNet = new FakeTransport();
            var battleNet = new FakeTransport();
            float t = 100f;
            var team = new TeamClient(teamNet, () => teamNet, clock: () => t);
            var battle = new BattleClient(battleNet);
            var entry = new BattleMatchEntryClient(team, battle, 1, 5, () => t);

            // 探针返回无队
            team.DrainQueued(false);
            teamNet.Calls[0].Reply(new TeamResponse { Team = new TeamView { TeamId = 0 } });
            t += 1f;

            // 单人排位 3V3：透传 102/201 预设和规则版本 1
            Assert.That(entry.StartRankedPvp(3, 102, 201, 1, 5), Is.True);
            Assert.That(battleNet.Calls.Count, Is.EqualTo(1));
            var joinReq = (JoinQueueRequest)battleNet.Calls[0].Request;
            Assert.That(joinReq.Mode, Is.EqualTo(MatchMode.PvpTeam));
            Assert.That(joinReq.BattleConfigId, Is.EqualTo(5u));
            Assert.That(joinReq.TeamSize, Is.EqualTo(3u));
            Assert.That(joinReq.CharacterPresetId, Is.EqualTo(102u));
            Assert.That(joinReq.PetPresetId, Is.EqualTo(201u));
            Assert.That(joinReq.RankedRuleVersion, Is.EqualTo(1u));

            // 4 人队选 5V5：本地明确拦截提示「再邀一位好友即可报名」
            battleNet.RaiseDisconnected();
            var fourView = new TeamView
            {
                TeamId = 77, LeaderId = 1001, Capacity = 5, Version = 2, MembershipEpoch = 1,
                PvpMatchSupported = true, MatchFillSupported = true, RankedPresetsAllConfirmed = true, RankedRuleVersion = 1,
            };
            for (ulong m = 1001; m <= 1004; m++)
                fourView.Members.Add(new TeamMemberView { PlayerId = m, IsOnline = true, RankedPresetConfirmed = true });
            team.Refresh();
            teamNet.Calls[1].Reply(new TeamResponse { Team = fourView });
            t += 1f;

            Assert.That(entry.StartRankedPvp(5, 101, 203, 1, 5), Is.False);
            Assert.That(entry.Status, Is.EqualTo("再邀一位好友即可报名"));
            Assert.That(RankClient.SeasonBoard("s1", 1UL).Period, Is.EqualTo("s1"));
            team.Dispose();
        }

        [Test]
        public void RecoveryPreservesTradableMaterials_And_UsesOwnProgressionFollowsSeasonFlag()
        {
            var recovered = _client.BeginSettlementFromRecovery(new PlayerBattleResult
            {
                BattleId = 9901UL,
                SeasonId = 1,
                MatchMode = (uint)MatchMode.PvpTeam,
                Outcome = (uint)eBattleOutcome.BattleOutcomeSideAWin,
                TeamIndex = 0,
                TotalRounds = 4,
                MaterialCreditUnitsAdded = 15,
                MaterialTradableUnitsAdded = 1,
            });
            Assert.That(recovered, Is.Not.Null);
            Assert.That(recovered.MaterialCreditUnitsAdded, Is.EqualTo(15u));
            Assert.That(recovered.MaterialTradableUnitsAdded, Is.EqualTo(1u), "重登补查结算必须保留可交易份数");

            var freePresetSnapshot = new RankedProfileSnapshot(
                new RankedSeasonInfo { SeasonId = 2, UsesOwnProgression = false },
                null, null, null, null, null, null, null, null, 0UL);
            Assert.That(freePresetSnapshot.UsesOwnProgression, Is.False,
                "UsesOwnProgression 必须严格遵循服务端字段，不能因 SeasonId!=0 恒为 true");

            var ownProgSnapshot = new RankedProfileSnapshot(
                new RankedSeasonInfo { SeasonId = 2, UsesOwnProgression = true },
                null, null, null, null, null, null, null, null, 0UL);
            Assert.That(ownProgSnapshot.UsesOwnProgression, Is.True);
        }

        [Test]
        public void V2TierHints_WinMilestone_ActivePetWarning_And_SurrenderTips()
        {
            var season = new RankedSeasonInfo
            {
                SeasonId = 1,
                RuleVersion = 2,
                TierQingfengCenti = 152000,
                TierYunzhangCenti = 160000,
                TierTianshuCenti = 172000,
                TierWushuangCenti = 185000,
                MilestoneWinMatches = 6,
            };
            var progress = new RankedPlayerSeasonSummary
            {
                SeasonId = 1,
                Qualified = true,
                RatingCenti = 150000,
                CurrentTier = RankedTier.Chushi,
                Wins = 4,
            };
            var snap = new RankedProfileSnapshot(season, progress, null, null, null, null, null, null, null, 0UL);
            Assert.That(snap.RuleVersion, Is.EqualTo(2u));
            Assert.That(snap.TierQingfengCenti, Is.EqualTo(152000u));
            Assert.That(snap.MilestoneWinMatches, Is.EqualTo(6u));
            Assert.That(snap.GetNextTierHint(), Is.EqualTo("距青锋还差 20 分"));
            Assert.That(snap.GetWinMilestoneHint(), Is.EqualTo("再赢 2 场解锁「锋芒」称号（4/6）"));
            Assert.That(RankedFormatting.RewardKindName(RankedRewardKind.RankedRewardWinMilestone), Is.EqualTo("锋芒称号"));
            Assert.That(RankedFormatting.ActivePetWarning(0UL), Does.Contain("未出战灵宠"));
            Assert.That(RankedFormatting.ActivePetWarning(9001UL), Is.Empty);
            Assert.That(RankedClient.DescribeTip(new TipInfoMessage { Id = (uint)match_error.KMatchBattleSurrenderTooEarly }),
                Does.Contain("5"));
            Assert.That(RankedClient.DescribeTip(new TipInfoMessage { Id = (uint)match_error.KMatchBattleSurrenderCooldown }),
                Does.Contain("冷却"));
        }

        [Test]
        public void V2SettlementSurrenderFlag_And_DrawerHelpers()
        {
            var surrenderedEnd = new BattleEndS2C
            {
                BattleId = 9912UL,
                Outcome = eBattleOutcome.BattleOutcomeSideBWin,
                SeasonId = 1,
                Settlement = new BattleSettlementData
                {
                    BattleId = 9912UL,
                    Outcome = eBattleOutcome.BattleOutcomeSideBWin,
                    CompetitiveRanked = true,
                    SeasonId = 1,
                    MatchMode = (uint)MatchMode.PvpTeam,
                    TotalRounds = 5,
                    Surrendered = true,
                },
            };
            var state = _client.BeginSettlementFromBattleEnd(surrenderedEnd, 0, 1);
            Assert.That(state, Is.Not.Null);
            Assert.That(state.Surrendered, Is.True);
            Assert.That(RankedFormatting.FormatAllowedSizesLabel(new uint[] { 1u, 3u }), Is.EqualTo("1V1 / 3V3 共用积分与榜"));
            Assert.That(RankedFormatting.FormatAllowedSizesLabel(null), Is.EqualTo("开放规模共用积分与榜"));
            Assert.That(RankedFormatting.FormatLocalDateRange(0UL, 0UL), Is.Empty);

            var snap = new RankedProfileSnapshot(
                new RankedSeasonInfo { SeasonId = 1 },
                null, null, null, null, null,
                new[]
                {
                    new RankedRewardEntitlement { Kind = RankedRewardKind.RankedRewardTierTitle, ItemCode = "s1_yunzhang_title" },
                    new RankedRewardEntitlement { Kind = RankedRewardKind.RankedRewardWinMilestone, ItemCode = "s1_win6_title" },
                },
                null, null, 0UL);
            var titleEnt = snap.FindEquippableTitleEntitlement();
            Assert.That(titleEnt, Is.Not.Null);
            Assert.That(titleEnt.Kind, Is.EqualTo(RankedRewardKind.RankedRewardWinMilestone));
            Assert.That(snap.ResolveTitleDisplayName("s1_win6_title"), Is.EqualTo("锋芒称号"));
        }

        [Test]
        public void RankedV4_HighlightsStreakBreakerAndRecoverySurrendered()
        {
            Assert.That(RankedFormatting.PingDisplayName(BattlePingKind.BattlePingFocus), Is.EqualTo("集火"));
            Assert.That(RankedFormatting.PingAssetKey(BattlePingKind.BattlePingProtect), Is.EqualTo("ping_protect"));
            Assert.That(RankedFormatting.HighlightDisplayName(BattleHighlightKind.BattleHighlightClutchComeback), Is.EqualTo("绝境翻盘"));
            Assert.That(RankedFormatting.HighlightAssetKey(BattleHighlightKind.BattleHighlightPrimaryFocus), Is.EqualTo("highlight_focus"));
            Assert.That(RankedFormatting.WinStreakBannerText(2u), Is.Empty);
            Assert.That(RankedFormatting.WinStreakBannerText(5u), Is.EqualTo("连胜势 ×5"));
            Assert.That(RankedFormatting.StreakBreakBannerText(4u), Is.EqualTo("破势终结 · 终结 4 连胜"));

            var liveEnd = new BattleEndS2C
            {
                BattleId = 88001UL,
                SeasonId = 1u,
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                Settlement = new BattleSettlementData
                {
                    BattleId = 88001UL,
                    CompetitiveRanked = true,
                    SeasonId = 1u,
                    MatchMode = (uint)MatchMode.PvpTeam,
                    Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                    TotalRounds = 14u,
                    TotalDamageDealt = 12800UL,
                    PrimaryFocusDamageDealt = 5400UL,
                    TotalHealingDone = 3200UL,
                    TotalControlsLanded = 2u,
                    TotalDispelsLanded = 1u,
                    DamageTaken = 6100UL,
                    HighlightKind = BattleHighlightKind.BattleHighlightClutchComeback,
                    EndedOpponentStreak = 5u,
                },
            };
            var liveState = _client.BeginSettlementFromBattleEnd(liveEnd, 0u, 3u);
            Assert.That(liveState.HasCombatHighlight, Is.True);
            Assert.That(liveState.TotalDamageDealt, Is.EqualTo(12800UL));
            Assert.That(liveState.PrimaryFocusDamageDealt, Is.EqualTo(5400UL));
            Assert.That(liveState.TotalSupportValue, Is.EqualTo(3200UL));
            Assert.That(liveState.ControlAndDispelCount, Is.EqualTo(3u));
            Assert.That(liveState.DamageTaken, Is.EqualTo(6100UL));
            Assert.That(liveState.HighlightKind, Is.EqualTo(BattleHighlightKind.BattleHighlightClutchComeback));
            Assert.That(liveState.EffectiveEndedOpponentStreak, Is.EqualTo(5u));

            // V4-4 尾项闭环：断线重登补查保留 Surrendered=true
            var recovered = _client.BeginSettlementFromRecovery(new PlayerBattleResult
            {
                BattleId = 88002UL,
                SeasonId = 1u,
                MatchMode = (uint)MatchMode.PvpTeam,
                Outcome = (uint)eBattleOutcome.BattleOutcomeSideBWin,
                TeamIndex = 0u,
                TotalRounds = 5u,
                Surrendered = true,
            });
            Assert.That(recovered.FromRecovery, Is.True);
            Assert.That(recovered.Surrendered, Is.True);
        }

        [Test]
        public void RankedV6_RivalryRevengeAndFeaturedBuildCopyPlan()
        {
            // 1. 宿敌标签与雪耻复仇印文案
            Assert.That(RankedFormatting.PrepRivalryTagText(new RankedRivalryEncounter
            {
                OpponentId = 2002UL,
                OpponentName = "玄烈",
                Wins = 1u,
                Losses = 2u,
                RevengeTarget = true,
            }), Is.EqualTo("宿敌 · 玄烈 1胜2负(可雪耻)"));
            Assert.That(RankedFormatting.RevengeStampText(true, "玄烈", 2u, 2u), Is.EqualTo("雪耻复仇 · 胜玄烈 (2胜2负)"));
            Assert.That(RankedFormatting.FormatWinRatePercent(3u, 4u), Is.EqualTo("75%"));

            // 2. Profile 透传 FeaturedBuilds 与 TopRivals
            var profileResp = new GetRankedProfileResponse
            {
                CurrentSeason = new RankedSeasonInfo { SeasonId = 1u, SeasonKey = "s1", SeasonName = "第一赛季" },
            };
            profileResp.TopRivals.Add(new RankedRivalrySummary
            {
                OpponentId = 2002UL,
                OpponentName = "玄烈",
                Wins = 2u,
                Losses = 2u,
                LastOutcome = 1u,
            });
            profileResp.FeaturedBuilds.Add(new RankedFeaturedBuildView
            {
                BuildKey = "seal102:pet201:s1001,1002",
                SeasonSealId = 102u,
                StarterPetTableId = 201u,
                Appearances = 4u,
                Wins = 3u,
                FeaturedBattleId = 99001UL,
                FeaturedPlayerName = "清风",
                FeaturedRatingCenti = 178500L,
            });
            profileResp.FeaturedBuilds[0].CoreSkillIds.Add(1001u);
            profileResp.FeaturedBuilds[0].CoreSkillIds.Add(1002u);

            var snap = RankedProfileSnapshot.FromResponse(profileResp);
            Assert.That(snap.TopRivals.Count, Is.EqualTo(1));
            Assert.That(snap.TopRivals[0].OpponentName, Is.EqualTo("玄烈"));
            Assert.That(snap.FeaturedBuilds.Count, Is.EqualTo(1));
            Assert.That(snap.FeaturedBuilds[0].FeaturedBattleId, Is.EqualTo(99001UL));

            // 3. 结算态复仇字段合并（BattleEnd 现场优先，Match 权威补齐）
            var revengeEnd = new BattleEndS2C
            {
                BattleId = 99002UL,
                SeasonId = 1u,
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                Settlement = new BattleSettlementData
                {
                    BattleId = 99002UL,
                    CompetitiveRanked = true,
                    SeasonId = 1u,
                    MatchMode = (uint)MatchMode.PvpTeam,
                    Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                    TotalRounds = 7u,
                    RevengedRival = true,
                    RevengedOpponentName = "玄烈",
                    RevengedWinsAfter = 2u,
                    RevengedLossesAfter = 2u,
                },
            };
            var revengeState = _client.BeginSettlementFromBattleEnd(revengeEnd, 0u, 1u);
            Assert.That(revengeState.EffectiveRevengedRival, Is.True);
            Assert.That(revengeState.EffectiveRevengedOpponentName, Is.EqualTo("玄烈"));
            Assert.That(revengeState.EffectiveRevengedWinsAfter, Is.EqualTo(2u));
            Assert.That(revengeState.EffectiveRevengedLossesAfter, Is.EqualTo(2u));

            // 4. 抄作业计划：已拥有赛季印 + 拥有未出战宠（选最高等级实例）
            var season = new SeasonStateSnapshot { SeasonId = 1u, Available = true, EquippedSealId = 101u };
            season.Seals.Add(new SeasonSealOption { SealId = 101u, Name = "破阵印" });
            season.Seals.Add(new SeasonSealOption { SealId = 102u, Name = "镇魂印" });
            var pets = new PetListInfo { ActivePetId = 5001UL };
            pets.Pets.Add(new PetInfo { PetId = 5001UL, PetTableId = 202u, Level = 40u, Name = "灵狐" });
            pets.Pets.Add(new PetInfo { PetId = 5002UL, PetTableId = 201u, Level = 35u, Name = "金猊甲" });
            pets.Pets.Add(new PetInfo { PetId = 5003UL, PetTableId = 201u, Level = 48u, Name = "金猊乙" });

            var copyPlan = RankedFormatting.EvaluateFeaturedBuildCopyPlan(
                snap.FeaturedBuilds[0], season, pets,
                id => id == 102u ? "镇魂印" : null,
                id => id == 201u ? "金猊" : null);
            Assert.That(copyPlan.CanEquipSeal, Is.True);
            Assert.That(copyPlan.TargetSealId, Is.EqualTo(102u));
            Assert.That(copyPlan.CanSummonPet, Is.True);
            Assert.That(copyPlan.TargetPetId, Is.EqualTo(5003UL));
            Assert.That(copyPlan.HasAnyWriteAction, Is.True);

            // 5. 抄作业差异：缺少赛季印与缺少首发宠时明确报告且不触发写操作
            var missingPlan = RankedFormatting.EvaluateFeaturedBuildCopyPlan(
                new RankedFeaturedBuildView { SeasonSealId = 104u, StarterPetTableId = 209u },
                season, pets,
                id => id == 104u ? "青冥印" : null,
                id => id == 209u ? "玄龟" : null);
            Assert.That(missingPlan.HasAnyWriteAction, Is.False);
            Assert.That(missingPlan.MissingSeal, Is.True);
            Assert.That(missingPlan.MissingPet, Is.True);
            Assert.That(missingPlan.SummaryMessage, Does.Contain("缺少赛季印「青冥印」"));
            Assert.That(missingPlan.SummaryMessage, Does.Contain("未拥有首发宠「玄龟」"));
        }

        [Test]
        public void RankedV7_LiveSpectateSortAndRematchRules()
        {
            // 1. 高段实时围观筛选与排序：仅保留正式排位（PvpTeam && RankedTeamSize > 0），按 Featured -> 分段 -> 开局时间倒序
            var raw = new[]
            {
                new BattleWatchSummary { BattleId = 1001UL, Mode = MatchMode.PveSolo, RankedTeamSize = 0u, RankedPeakRatingCenti = 200000L },
                new BattleWatchSummary { BattleId = 1002UL, Mode = MatchMode.PvpTeam, RankedTeamSize = 1u, RankedPeakRatingCenti = 162000L, CreatedAtMs = 5000UL, PlayerNames = { "青云", "玄烈" } },
                new BattleWatchSummary { BattleId = 1003UL, Mode = MatchMode.PvpTeam, RankedTeamSize = 3u, RankedPeakRatingCenti = 185000L, CreatedAtMs = 4000UL, PlayerNames = { "太虚", "剑鸣", "月华", "雷音", "素心", "沧溟" } },
                new BattleWatchSummary { BattleId = 1004UL, Mode = MatchMode.PvpTeam, RankedTeamSize = 1u, RankedPeakRatingCenti = 158000L, CreatedAtMs = 3000UL, Featured = true, PlayerNames = { "无双甲", "无双乙" } },
            };
            var sorted = RankedFormatting.FilterAndSortLiveRankedBattles(raw);
            Assert.That(sorted.Count, Is.EqualTo(3));
            Assert.That(sorted[0].BattleId, Is.EqualTo(1004UL));
            Assert.That(sorted[1].BattleId, Is.EqualTo(1003UL));
            Assert.That(sorted[2].BattleId, Is.EqualTo(1002UL));
            Assert.That(RankedFormatting.FormatLiveRankedMatchSummary(sorted[2]), Is.EqualTo("1V1 · 1620分 · 青云 vs 玄烈"));
            Assert.That(RankedFormatting.FormatLiveRankedMatchSummary(sorted[1]), Does.Contain("3V3 · 1850分 · 太虚 等 6 人激战中"));

            // 2. 1V1 结算不服再战可用性与切磋规则构造（零排位分、非免费预设、公开可观战）
            var duelRules = RankedFormatting.BuildRematchDuelRules(99701UL);
            Assert.That(duelRules.UseFreePreset, Is.False);
            Assert.That(duelRules.AllowPublicSpectate, Is.True);
            Assert.That(duelRules.RematchSourceBattleId, Is.EqualTo(99701UL));

            var live1v1 = _client.BeginSettlementFromBattleEnd(new BattleEndS2C
            {
                BattleId = 99701UL,
                SeasonId = 1u,
                Outcome = eBattleOutcome.BattleOutcomeSideBWin,
                Settlement = new BattleSettlementData
                {
                    BattleId = 99701UL,
                    CompetitiveRanked = true,
                    SeasonId = 1u,
                    MatchMode = (uint)MatchMode.PvpTeam,
                    Outcome = eBattleOutcome.BattleOutcomeSideBWin,
                    TotalRounds = 8u,
                    RematchOpponentId = 2002UL,
                    RematchOpponentName = "玄烈",
                },
            }, 0u, 1u);
            Assert.That(live1v1.CanOfferRematch, Is.True);
            Assert.That(live1v1.RematchOpponentId, Is.EqualTo(2002UL));
            Assert.That(live1v1.RematchOpponentName, Is.EqualTo("玄烈"));

            // 3V3 与重登补查不显示 1V1 再战按钮
            live1v1.TeamSize = 3u;
            Assert.That(live1v1.CanOfferRematch, Is.False);
            live1v1.TeamSize = 1u;
            live1v1.FromRecovery = true;
            Assert.That(live1v1.CanOfferRematch, Is.False);
        }
    }
}
