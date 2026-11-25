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
    }
}
