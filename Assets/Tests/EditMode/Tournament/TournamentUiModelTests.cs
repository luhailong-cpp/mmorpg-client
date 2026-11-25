using Match;
using MmorpgClient.Game.Tournament;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tournament
{
    public sealed class TournamentUiModelTests
    {
        private static TournamentOverviewSnapshot MakeOverview(
            TournamentStage stage,
            TournamentSquadView mySquad = null,
            TournamentMatchView activeMatch = null,
            bool claimable = false,
            bool claimed = false,
            bool enabled = true)
        {
            var resp = new GetTournamentOverviewResponse
            {
                CurrentSeason = new TournamentSeasonView
                {
                    SeasonId = 2,
                    SeasonKey = "t2",
                    SeasonName = "第二赛季·群仙问道赛",
                    Stage = stage,
                    Enabled = enabled,
                    BattleTeamSize = 3,
                    RosterMaxSize = 4,
                    QualifierMaxMatches = 6,
                    ParticipationMinMatches = 2,
                    KnockoutTeamCount = 4,
                    QualifierStartMs = 200_000UL,
                    KnockoutStartMs = 300_000UL,
                    SettleStartMs = 400_000UL,
                    EndMs = 500_000UL,
                },
                MySquad = mySquad,
                MyActiveMatch = activeMatch,
                ServerTimeMs = 150_000UL,
            };
            if (claimable || claimed)
            {
                resp.MyRewards.Add(new TournamentPlayerRewardView
                {
                    RewardId = 1,
                    SeasonId = 2,
                    TierKind = TournamentRewardTierKind.TournamentRewardTierTop4,
                    DeliveryState = claimable
                        ? TournamentRewardDeliveryState.TournamentRewardDeliveryClaimable
                        : TournamentRewardDeliveryState.TournamentRewardDeliveryClaimed,
                });
            }
            resp.HallOfFame.Add(new TournamentSeasonView
            {
                SeasonId = 1,
                SeasonName = "第一赛季",
                ChampionSquadId = 8001UL,
                ChampionSquadName = "玉衡星河",
            });
            return TournamentOverviewSnapshot.FromResponse(resp, localSeconds: 12f);
        }

        private static TournamentSquadView MakeSquad(TournamentSquadState state, uint memberCount = 4, uint played = 2)
        {
            var sq = new TournamentSquadView
            {
                SquadId = 5001UL,
                SeasonId = 2,
                Name = "云上问道",
                CaptainPlayerId = 1001UL,
                State = state,
                MemberCount = memberCount,
                QualifierPlayed = played,
            };
            for (uint i = 0; i < memberCount; i++)
            {
                sq.Members.Add(new TournamentMemberView
                {
                    PlayerId = 1001UL + i,
                    PlayerName = $"道友{i + 1}",
                    Role = i == 0 ? TournamentMemberRole.Captain
                        : (i < 3 ? TournamentMemberRole.Starter : TournamentMemberRole.Substitute),
                    StarterSlot = i < 3 ? i + 1u : 0u,
                });
            }
            return sq;
        }

        [Test]
        public void CalibratedCountdown_UsesRealtimeDeltaFromObservationRatherThanRawRealtime()
        {
            var model = new TournamentUiModel();
            var ov = MakeOverview(TournamentStage.Registration); // ServerTimeMs=150s, QualifierStartMs=200s, CapturedAtLocalSeconds=12s
            // 即使 Unity Time.realtimeSinceStartup 已跑了 9999 秒，新快照到达瞬间剩余仍精确为 50_000ms
            Assert.That(model.ComputeStageRemainingMs(ov, nowRealtimeSeconds: 9999f), Is.EqualTo(50_000UL));
            // 经过 7 秒后精确减少 7_000ms
            Assert.That(model.ComputeStageRemainingMs(ov, nowRealtimeSeconds: 10006f), Is.EqualTo(43_000UL));
        }

        [Test]
        public void DerivePrimaryCta_CoversAllFourStagesAndRoles()
        {
            // 报名期：未入队 -> 创建战队
            var cUnjoined = TournamentUiModel.DerivePrimaryCta(MakeOverview(TournamentStage.Registration), 1001UL, false);
            Assert.That(cUnjoined.Kind, Is.EqualTo(TournamentPrimaryCtaKind.CreateSquad));
            Assert.That(cUnjoined.Interactable, Is.True);

            // 报名期：队长人未满 -> 置灰写明缺额
            var cShort = TournamentUiModel.DerivePrimaryCta(
                MakeOverview(TournamentStage.Registration, MakeSquad(TournamentSquadState.Draft, memberCount: 2)), 1001UL, false);
            Assert.That(cShort.Interactable, Is.False);
            Assert.That(cShort.Label, Does.Contain("还差 1 名队员"));

            // 报名期：队长满 3 人 -> 确认锁定报名；队员 -> 等待队长确认报名
            var fullDraft = MakeOverview(TournamentStage.Registration, MakeSquad(TournamentSquadState.Draft, memberCount: 4));
            Assert.That(TournamentUiModel.DerivePrimaryCta(fullDraft, 1001UL, false).Kind, Is.EqualTo(TournamentPrimaryCtaKind.RegisterSquad));
            Assert.That(TournamentUiModel.DerivePrimaryCta(fullDraft, 1002UL, false).Kind, Is.EqualTo(TournamentPrimaryCtaKind.PreviewSquad));

            // 海选期：队长剩 4 场 -> 发起海选匹配；打满 6 场 -> 置灰完成态
            var qualOpen = MakeOverview(TournamentStage.Qualifier, MakeSquad(TournamentSquadState.Registered, played: 2));
            var cQual = TournamentUiModel.DerivePrimaryCta(qualOpen, 1001UL, false);
            Assert.That(cQual.Kind, Is.EqualTo(TournamentPrimaryCtaKind.StartQualifierMatch));
            Assert.That(cQual.Label, Does.Contain("剩 4 场"));

            var qualDone = MakeOverview(TournamentStage.Qualifier, MakeSquad(TournamentSquadState.Registered, played: 6));
            Assert.That(TournamentUiModel.DerivePrimaryCta(qualDone, 1001UL, false).Interactable, Is.False);

            // 淘汰期：有活跃待开场次 -> 进入淘汰赛备战；己方已就绪 -> 等待对手
            var koMatch = new TournamentMatchView
            {
                MatchId = 8801UL,
                Phase = TournamentMatchPhase.Knockout,
                State = TournamentMatchState.WaitingOpponent,
                SquadAId = 5001UL,
                SquadBId = 6001UL,
            };
            var cKo = TournamentUiModel.DerivePrimaryCta(
                MakeOverview(TournamentStage.Knockout, MakeSquad(TournamentSquadState.Registered), koMatch), 1001UL, false);
            Assert.That(cKo.Kind, Is.EqualTo(TournamentPrimaryCtaKind.StartKnockoutMatch));
            Assert.That(cKo.MatchId, Is.EqualTo(8801UL));

            koMatch.ReadyA = true;
            var cKoWait = TournamentUiModel.DerivePrimaryCta(
                MakeOverview(TournamentStage.Knockout, MakeSquad(TournamentSquadState.Registered), koMatch), 1001UL, false);
            Assert.That(cKoWait.Interactable, Is.False);
            Assert.That(cKoWait.Label, Does.Contain("等待对手"));

            // 结算期：可领奖 -> 领取赛季奖励；已领 -> 置灰已发至邮箱
            var cClaim = TournamentUiModel.DerivePrimaryCta(
                MakeOverview(TournamentStage.Settled, MakeSquad(TournamentSquadState.Eliminated), claimable: true), 1001UL, false);
            Assert.That(cClaim.Kind, Is.EqualTo(TournamentPrimaryCtaKind.ClaimReward));
            Assert.That(cClaim.Interactable, Is.True);

            var cClaimed = TournamentUiModel.DerivePrimaryCta(
                MakeOverview(TournamentStage.Settled, MakeSquad(TournamentSquadState.Eliminated), claimed: true), 1001UL, false);
            Assert.That(cClaimed.Interactable, Is.False);
            Assert.That(cClaimed.Label, Does.Contain("已发至邮箱"));
        }

        [Test]
        public void TipRecovery_LineupDraft_ReplayGuard_And_SeasonChoices()
        {
            Assert.That(TournamentUiModel.ClassifyTipRecovery((uint)match_error.KMatchTournamentNotOpen).ShowNotOpenEmptyState, Is.True);
            Assert.That(TournamentUiModel.ClassifyTipRecovery((uint)match_error.KMatchTournamentSquadNameInvalid).KeepCreateModalInline, Is.True);
            Assert.That(TournamentUiModel.ClassifyTipRecovery((uint)match_error.KMatchTournamentLineupInvalid).KeepLineupModalInline, Is.True);
            Assert.That(TournamentUiModel.ClassifyTipRecovery((uint)match_error.KMatchTournamentStageClosed).AutoRefreshOverviewOnce, Is.True);
            Assert.That(TournamentUiModel.ClassifyTipRecovery((uint)match_error.KMatchTournamentAlreadyCheered).AutoRefreshOverviewOnce, Is.True);

            var model = new TournamentUiModel();
            var sq = MakeSquad(TournamentSquadState.Registered, memberCount: 4);
            model.OpenLineupModal(sq, 3u, 8801UL, forMatchStart: true);
            Assert.That(model.LineupDraft, Is.EqualTo(new ulong[] { 1001UL, 1002UL, 1003UL }));
            Assert.That(model.ToggleLineupMember(1004UL, 3u), Is.False, "满 3 人时不能再多选第 4 人");
            Assert.That(model.ToggleLineupMember(1003UL, 3u), Is.True);
            Assert.That(model.ToggleLineupMember(1004UL, 3u), Is.True);
            Assert.That(model.IsLineupDraftComplete(3u), Is.True);

            Assert.That(TournamentUiModel.ValidateSquadNameLength("云上问道", out int len) && len == 4, Is.True);
            Assert.That(TournamentUiModel.ValidateSquadNameLength("单", out _), Is.False);
            Assert.That(TournamentUiModel.TryParsePlayerId("10086", out ulong parsed) && parsed == 10086UL, Is.True);
            Assert.That(TournamentUiModel.TryParsePlayerId("abc", out _), Is.False);

            // 战报回放仅对本队已结束且 battle_id != 0 的场次放行
            var finishedMine = new TournamentMatchView { State = TournamentMatchState.Finished, BattleId = 9001UL, SquadAId = 5001UL, SquadBId = 6001UL };
            var finishedOther = new TournamentMatchView { State = TournamentMatchState.Finished, BattleId = 9002UL, SquadAId = 7001UL, SquadBId = 6001UL };
            Assert.That(TournamentUiModel.CanWatchReplay(finishedMine, sq), Is.True);
            Assert.That(TournamentUiModel.CanWatchReplay(finishedOther, sq), Is.False);

            var choices = TournamentUiModel.BuildSeasonChoices(MakeOverview(TournamentStage.Settled, sq));
            Assert.That(choices.Count, Is.EqualTo(2));
            Assert.That(choices[0].SeasonId, Is.EqualTo(0u));
            Assert.That(choices[1].SeasonId, Is.EqualTo(1u));
        }
    }
}
