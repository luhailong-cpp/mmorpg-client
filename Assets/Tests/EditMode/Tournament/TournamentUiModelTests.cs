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

            // 好友/同帮/附近邀请候选过滤：剔除本人与已在队成员、去重、在线优先
            var filtered = TournamentUiModel.FilterInviteCandidates(new[]
            {
                new TournamentInviteCandidate(1001UL, "本人", "好友", true, true),
                new TournamentInviteCandidate(1002UL, "已在队", "好友", true, true),
                new TournamentInviteCandidate(3001UL, "离线道友", "同帮", false, true),
                new TournamentInviteCandidate(3002UL, "在线道友", "好友", true, true),
                new TournamentInviteCandidate(3002UL, "重复条目", "同帮", true, true),
            }, localPlayerId: 1001UL, mySquad: sq);
            Assert.That(filtered.Count, Is.EqualTo(2));
            Assert.That(filtered[0].PlayerId, Is.EqualTo(3002UL));
            Assert.That(filtered[1].PlayerId, Is.EqualTo(3001UL));
        }

        [Test]
        public void V2_IntermissionCountdown_PublicKnockoutReplay_Leylines_And_PopularitySplit()
        {
            var model = new TournamentUiModel();
            var sq = MakeSquad(TournamentSquadState.Registered, memberCount: 4);
            var interMatch = new TournamentMatchView
            {
                MatchId = 8802UL,
                Phase = TournamentMatchPhase.Knockout,
                State = TournamentMatchState.Intermission,
                SquadAId = sq.SquadId,
                SquadBId = 6002UL,
                BestOf = 3u,
                CurrentGameIndex = 2u,
                WinsA = 1u,
                WinsB = 0u,
                CurrentLeylineElement = TournamentLeylineElement.TournamentLeylineFire,
                IntermissionDeadlineMs = 210_000UL,
                PublicReplayAvailable = true,
            };
            interMatch.GameBattleIds.Add(71001UL);

            var ov = MakeOverview(TournamentStage.Knockout, sq, interMatch); // ServerTimeMs=150_000
            // 校准倒计时：初始精确 60s，走 15s 后精确剩 45s，不漂移、不用壁钟
            Assert.That(model.ComputeIntermissionRemainingMs(ov, interMatch, nowRealtimeSeconds: 5000f), Is.EqualTo(60_000UL));
            Assert.That(model.ComputeIntermissionRemainingMs(ov, interMatch, nowRealtimeSeconds: 5015f), Is.EqualTo(45_000UL));
            Assert.That(model.ComputeIntermissionRemainingMs(ov, interMatch, nowRealtimeSeconds: 5100f), Is.EqualTo(0UL));

            // 局间换阵 CTA
            var ctaCap = TournamentUiModel.DerivePrimaryCta(ov, 1001UL, false);
            Assert.That(ctaCap.Kind, Is.EqualTo(TournamentPrimaryCtaKind.IntermissionSwap));
            Assert.That(ctaCap.Label, Is.EqualTo("局间换阵备战"));
            Assert.That(TournamentFormatting.FormatBo3Score(interMatch), Is.EqualTo("BO3 1:0"));

            // 公开淘汰赛战报：路人（无战队/非参战方）允许回放，且取最新小局 battle_id
            Assert.That(TournamentUiModel.CanWatchReplay(interMatch, mySquad: null), Is.True);
            Assert.That(TournamentUiModel.ResolveLatestReplayBattleId(interMatch), Is.EqualTo(71001UL));

            // 五行灵脉与 6 条确认预设短语完整无缺失
            Assert.That(TournamentFormatting.AllLeylines.Length, Is.EqualTo(5));
            Assert.That(TournamentFormatting.GetLeylineInfo(TournamentLeylineElement.TournamentLeylineEarth).EffectSummary, Does.Contain("护盾"));
            Assert.That(TournamentFormatting.SpectateCheerPresets.Length, Is.EqualTo(6));
            Assert.That(TournamentFormatting.SpectateCheerPresets[2].Label, Is.EqualTo("妙法天成"));
            Assert.That(TournamentFormatting.SpectateCheerPresets[2].HasNativeFloatArt, Is.True);

            // 人气对推比例：0:0 中性 50/50；有数据时按真实比例计算
            var zero = TournamentFormatting.ComputePopularitySplit(0UL, 0UL);
            Assert.That(zero.isNeutral, Is.True);
            Assert.That(zero.ratioA, Is.EqualTo(0.5f));
            var real = TournamentFormatting.ComputePopularitySplit(3UL, 1UL);
            Assert.That(real.isNeutral, Is.False);
            Assert.That(real.ratioA, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(real.ratioB, Is.EqualTo(0.25f).Within(0.001f));

            // V2 新增 Tip 恢复策略
            Assert.That(TournamentUiModel.ClassifyTipRecovery((uint)match_error.KMatchTournamentAlreadySalutedToday).AutoRefreshOverviewOnce, Is.True);
            Assert.That(TournamentUiModel.ClassifyTipRecovery((uint)match_error.KMatchTournamentNoChampionToSalute).AutoRefreshOverviewOnce, Is.True);
        }

        [Test]
        public void V3_EmblemsWarmupPredictionMvpAndThirdPlace_FormattingAndGuards()
        {
            // 1. 8 枚官方道家战旗规范化与目录完整
            Assert.That(TournamentFormatting.AllEmblems.Length, Is.EqualTo(8));
            Assert.That(TournamentFormatting.NormalizeEmblemId(0u), Is.EqualTo(1u));
            Assert.That(TournamentFormatting.NormalizeEmblemId(99u), Is.EqualTo(1u));
            Assert.That(TournamentFormatting.GetEmblemInfo(8u).Name, Is.EqualTo("云鼎"));
            Assert.That(TournamentFormatting.GetEmblemInfo(8u).SpriteKey, Is.EqualTo("v3-g-emblem-08"));

            // 2. 奖励档位与场次标题（季军争夺战 / 神机问道纪念 / 热身演练）
            Assert.That(TournamentFormatting.RewardTierDisplayName(TournamentRewardTierKind.TournamentRewardTierThirdPlace), Is.EqualTo("全服季军"));
            Assert.That(TournamentFormatting.RewardTierDisplayName(TournamentRewardTierKind.TournamentRewardTierPrediction), Is.EqualTo("神机问道纪念"));
            Assert.That(TournamentFormatting.MatchPhaseDisplayName(TournamentMatchPhase.Warmup), Is.EqualTo("热身演练"));

            var thirdMatch = new TournamentMatchView
            {
                MatchId = 9901UL,
                Phase = TournamentMatchPhase.Knockout,
                RoundIndex = 2u,
                SlotIndex = 1u,
                IsThirdPlace = true,
                SquadAId = 5001UL,
                SquadBId = 6001UL,
                State = TournamentMatchState.WaitingOpponent,
                MvpPlayerId = 1001UL,
                MvpPlayerName = "问鼎剑仙",
                MvpScore = 980u,
                MvpDamage = 45000UL,
                MvpHealing = 12000UL,
                MvpControls = 4u,
            };
            Assert.That(TournamentFormatting.KnockoutMatchTitle(4u, thirdMatch), Is.EqualTo("季军争夺战"));
            Assert.That(TournamentFormatting.FormatMvpMetrics(thirdMatch), Does.Contain("980").And.Contain("45,000").And.Contain("4 次"));

            // 3. 免费竞猜资格仅放行未开战且双队齐备的淘汰赛
            Assert.That(TournamentUiModel.CanPredictMatch(thirdMatch), Is.True);
            thirdMatch.State = TournamentMatchState.InBattle;
            Assert.That(TournamentUiModel.CanPredictMatch(thirdMatch), Is.False);

            // 4. 热身约战入口仅在报名/海选期且队长满首发人数时开放
            var sq = MakeSquad(TournamentSquadState.Draft, memberCount: 3u);
            Assert.That(TournamentUiModel.CanStartWarmup(MakeOverview(TournamentStage.Registration, sq), 1001UL), Is.True);
            Assert.That(TournamentUiModel.CanStartWarmup(MakeOverview(TournamentStage.Registration, sq), 1002UL), Is.False);
            Assert.That(TournamentUiModel.CanStartWarmup(MakeOverview(TournamentStage.Knockout, sq), 1001UL), Is.False);

            // 5. V3 弹窗生命周期与 Tip 恢复
            var model = new TournamentUiModel();
            model.OpenSelectEmblemModal(5u);
            Assert.That(model.ActiveModal, Is.EqualTo(TournamentModalKind.SelectEmblem));
            Assert.That(model.SelectedEmblemDraft, Is.EqualTo(5u));
            model.OpenWarmupModal(sq, 3u);
            Assert.That(model.ActiveModal, Is.EqualTo(TournamentModalKind.WarmupSpar));
            Assert.That(model.LineupDraft.Count, Is.EqualTo(3));
            model.OpenMvpHighlightModal(thirdMatch);
            Assert.That(model.ActiveModal, Is.EqualTo(TournamentModalKind.MvpHighlight));
            Assert.That(model.InspectedMvpMatch.MvpPlayerId, Is.EqualTo(1001UL));
            model.CloseModal();
            Assert.That(model.InspectedMvpMatch, Is.Null);

            Assert.That(TournamentUiModel.ClassifyTipRecovery((uint)match_error.KMatchTournamentEmblemInvalid).KeepCreateModalInline, Is.True);
            Assert.That(TournamentUiModel.ClassifyTipRecovery((uint)match_error.KMatchTournamentPredictionClosed).AutoRefreshOverviewOnce, Is.True);
            Assert.That(TournamentUiModel.ClassifyTipRecovery((uint)match_error.KMatchTournamentWarmupUnavailable).AutoRefreshOverviewOnce, Is.True);
        }
    }
}
