using System;
using System.Collections.Generic;
using Google.Protobuf;
using Match;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Tournament;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tournament
{
    public sealed class TournamentClientTests
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
        private TournamentClient _client;

        [SetUp]
        public void SetUp()
        {
            _net = new FakeTransport();
            _conn = new object();
            _now = 10f;
            _client = new TournamentClient(_net, () => _conn, () => _now);
        }

        [TearDown]
        public void TearDown() => _client.Dispose();

        [Test]
        public void FullFlow_OverviewCreateLineupRegisterStandingsStartCheerAndClaim()
        {
            // 1. 拉取总览（RPC 465）
            Assert.That(_client.RequestOverview(includeHistory: true), Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
            Assert.That(_net.Calls[0].MessageId, Is.EqualTo(MatchServiceRpc.GetTournamentOverviewMessageId));

            var overviewResp = new GetTournamentOverviewResponse
            {
                CurrentSeason = new TournamentSeasonView
                {
                    SeasonId = 1,
                    SeasonKey = "t1",
                    SeasonName = "第一赛季·群仙问道赛",
                    Stage = TournamentStage.Registration,
                    Enabled = true,
                    BattleTeamSize = 3,
                    RosterMaxSize = 4,
                    QualifierStartMs = 20_000UL,
                },
                ServerTimeMs = 10_000UL,
            };
            overviewResp.MyRewards.Add(new TournamentPlayerRewardView
            {
                RewardId = 1,
                SeasonId = 1,
                TierKind = TournamentRewardTierKind.TournamentRewardTierChampion,
                DeliveryState = TournamentRewardDeliveryState.TournamentRewardDeliveryClaimable,
            });
            _net.Calls[0].Reply(overviewResp);

            Assert.That(_client.Overview, Is.Not.Null);
            Assert.That(_client.Overview.CurrentStageRemainingMs(_now + 2f), Is.EqualTo(8_000UL));
            Assert.That(_client.Overview.HasClaimableReward, Is.True);

            // 2. 创建战队（RPC 466），服务端拒单时不污染本地快照，确认后写入
            Assert.That(_client.CreateSquad("太玄剑阁"), Is.True);
            Assert.That(_net.Calls[1].MessageId, Is.EqualTo(MatchServiceRpc.CreateTournamentSquadMessageId));
            _net.Calls[1].Reply(new CreateTournamentSquadResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)match_error.KMatchTournamentSquadNameInvalid },
            });
            Assert.That(_client.Overview.HasSquad, Is.False);
            Assert.That(_client.LastError, Does.Contain("不合规"));
            Assert.That(_client.LastErrorTipId, Is.EqualTo((uint)match_error.KMatchTournamentSquadNameInvalid));

            _now += 2f;
            Assert.That(_client.CreateSquad("太玄剑阁"), Is.True);
            Assert.That(_client.LastErrorTipId, Is.EqualTo(0u));
            var sq = new TournamentSquadView
            {
                SquadId = 5001UL,
                SeasonId = 1,
                Name = "太玄剑阁",
                CaptainPlayerId = 1001UL,
                State = TournamentSquadState.Draft,
                MemberCount = 3,
            };
            _net.Calls[2].Reply(new CreateTournamentSquadResponse { Squad = sq });
            Assert.That(_client.Overview.HasSquad, Is.True);
            Assert.That(_client.Overview.IsCaptain(1001UL), Is.True);

            // 3. 调整阵容（RPC 470）& 锁定报名（RPC 471）
            _now += 2f;
            Assert.That(_client.UpdateLineup(new ulong[] { 1001UL, 1002UL, 1004UL }), Is.True);
            Assert.That(_net.Calls[3].MessageId, Is.EqualTo(MatchServiceRpc.UpdateTournamentLineupMessageId));
            _net.Calls[3].Reply(new UpdateTournamentLineupResponse { Squad = sq });

            _now += 2f;
            Assert.That(_client.RegisterSquad(), Is.True);
            Assert.That(_net.Calls[4].MessageId, Is.EqualTo(MatchServiceRpc.RegisterTournamentSquadMessageId));
            var registeredSq = sq.Clone();
            registeredSq.State = TournamentSquadState.Registered;
            _net.Calls[4].Reply(new RegisterTournamentSquadResponse { Squad = registeredSq });
            Assert.That(_client.Overview.MySquad.State, Is.EqualTo(TournamentSquadState.Registered));

            // 4. 积分榜与动态对阵树（RPC 472）
            _now += 2f;
            Assert.That(_client.RequestStandings(1, 1, 20), Is.True);
            Assert.That(_net.Calls[5].MessageId, Is.EqualTo(MatchServiceRpc.ListTournamentStandingsMessageId));
            var standingsResp = new ListTournamentStandingsResponse
            {
                Season = overviewResp.CurrentSeason.Clone(),
                TotalSquads = 4,
            };
            standingsResp.Standings.Add(registeredSq);
            standingsResp.KnockoutBracket.Add(new TournamentMatchView { MatchId = 11, RoundIndex = 1, SlotIndex = 0 });
            standingsResp.KnockoutBracket.Add(new TournamentMatchView { MatchId = 12, RoundIndex = 1, SlotIndex = 1 });
            standingsResp.KnockoutBracket.Add(new TournamentMatchView { MatchId = 13, RoundIndex = 2, SlotIndex = 0 });
            _net.Calls[5].Reply(standingsResp);
            var cols = _client.Standings.GroupKnockoutByRound();
            Assert.That(cols.Count, Is.EqualTo(2));
            Assert.That(cols[0].Count, Is.EqualTo(2));
            Assert.That(cols[1].Count, Is.EqualTo(1));
            Assert.That(TournamentFormatting.KnockoutRoundTitle(4, 1), Is.EqualTo("半决赛"));
            Assert.That(TournamentFormatting.KnockoutRoundTitle(4, 2), Is.EqualTo("全服总决赛"));

            // 5. 发起比赛（RPC 473）触发开战事件
            ulong launchedBattle = 0UL;
            _client.MatchBattleLaunched += id => launchedBattle = id;
            _now += 2f;
            Assert.That(_client.StartMatch(11UL, new ulong[] { 1001UL, 1002UL, 1004UL }), Is.True);
            Assert.That(_net.Calls[6].MessageId, Is.EqualTo(MatchServiceRpc.StartTournamentMatchMessageId));
            _net.Calls[6].Reply(new StartTournamentMatchResponse
            {
                BattleId = 77701UL,
                Match = new TournamentMatchView { MatchId = 11UL, BattleId = 77701UL, State = TournamentMatchState.InBattle },
            });
            Assert.That(launchedBattle, Is.EqualTo(77701UL));
            Assert.That(_client.LastLaunchedBattleId, Is.EqualTo(77701UL));

            // 6. 免费助威（RPC 474）同步更新概览与榜单票数
            _now += 2f;
            Assert.That(_client.CheerSquad(5001UL), Is.True);
            Assert.That(_net.Calls[7].MessageId, Is.EqualTo(MatchServiceRpc.CheerTournamentSquadMessageId));
            _net.Calls[7].Reply(new CheerTournamentSquadResponse { SquadId = 5001UL, SquadCheerCount = 9 });
            Assert.That(_client.Overview.MyCheeredSquadId, Is.EqualTo(5001UL));
            Assert.That(_client.Overview.MyCheeredSquadName, Is.EqualTo("太玄剑阁"));
            Assert.That(_client.Standings.Standings[0].CheerCount, Is.EqualTo(9u));

            // 7. 领奖（RPC 475）更新本地账单为已发至邮箱
            _now += 2f;
            Assert.That(_client.ClaimReward(1u), Is.True);
            Assert.That(_net.Calls[8].MessageId, Is.EqualTo(MatchServiceRpc.ClaimTournamentRewardMessageId));
            var claimResp = new ClaimTournamentRewardResponse();
            claimResp.ClaimedRewards.Add(new TournamentPlayerRewardView
            {
                RewardId = 1,
                SeasonId = 1,
                TierKind = TournamentRewardTierKind.TournamentRewardTierChampion,
                DeliveryState = TournamentRewardDeliveryState.TournamentRewardDeliveryClaimed,
                MailId = 90001UL,
            });
            _net.Calls[8].Reply(claimResp);
            Assert.That(_client.Overview.HasClaimableReward, Is.False);
            Assert.That(_client.Overview.MyRewards[0].MailId, Is.EqualTo(90001UL));
        }

        [Test]
        public void DisconnectAndPlayerSwitch_DiscardStaleCallbacks()
        {
            Assert.That(_client.RequestOverview(false), Is.True);
            var staleCall = _net.Calls[0];
            _net.RaiseDisconnected();
            Assert.That(_client.Overview, Is.Null);

            staleCall.Reply(new GetTournamentOverviewResponse
            {
                CurrentSeason = new TournamentSeasonView { SeasonId = 9, Stage = TournamentStage.Knockout },
            });
            Assert.That(_client.Overview, Is.Null, "断线前在途回包不得污染新会话");

            // 切号不经过断线事件时由 ObserveConnection 同样作废旧回调
            _now += 2f;
            Assert.That(_client.RequestOverview(false), Is.True);
            var prevPlayerCall = _net.Calls[1];
            _net.PlayerId = 2002UL;
            _client.ObserveConnection();
            prevPlayerCall.Reply(new GetTournamentOverviewResponse
            {
                CurrentSeason = new TournamentSeasonView { SeasonId = 8, Stage = TournamentStage.Settled },
            });
            Assert.That(_client.Overview, Is.Null, "切号前在途回包不得污染新角色");
        }

        [Test]
        public void V2_SaluteChampion_And_SpectateCheer_MergeCooldownAndReset()
        {
            // 先拉总览使 Overview 带有第 1 季总冠军
            Assert.That(_client.RequestOverview(includeHistory: true), Is.True);
            var ovResp = new GetTournamentOverviewResponse
            {
                CurrentSeason = new TournamentSeasonView
                {
                    SeasonId = 1,
                    Stage = TournamentStage.Settled,
                    Enabled = true,
                    ChampionSquadId = 5001UL,
                    ChampionSquadName = "太玄剑阁",
                    ChampionSaluteCount = 7UL,
                    SalutedToday = false,
                },
                ServerTimeMs = 100_000UL,
            };
            _net.Calls[0].Reply(ovResp);
            Assert.That(_client.Overview.Season.SalutedToday, Is.False);

            // 1. V2 RPC 506：观道致敬更新计数与今日已致敬态
            TournamentSaluteOutcome? fired = null;
            _client.ChampionSaluted += o => fired = o;
            _now += 2f;
            Assert.That(_client.SaluteChampion(1u), Is.True);
            Assert.That(_net.Calls[1].MessageId, Is.EqualTo(MatchServiceRpc.SaluteTournamentChampionMessageId));
            var salResp = new SaluteTournamentChampionResponse
            {
                SeasonId = 1u,
                ChampionSaluteCount = 8UL,
                SalutedToday = true,
                GoldAmount = 5000UL,
                MailId = 88001UL,
            };
            salResp.Items.Add(new TournamentRewardItem { ItemId = 20u, Count = 3u });
            _net.Calls[1].Reply(salResp);
            Assert.That(_client.Overview.Season.SalutedToday, Is.True);
            Assert.That(_client.Overview.Season.ChampionSaluteCount, Is.EqualTo(8UL));
            Assert.That(fired.HasValue && fired.Value.MailId == 88001UL && fired.Value.GoldAmount == 5000UL, Is.True);

            // 2. V2 RPC 507/508：预设喝彩白名单校验 + 增量合并 + 冷却 + 切场清空
            Assert.That(_client.SendSpectateCheer(77701UL, 0u, 0u), Is.False, "非法 phraseId=0 不发包");
            Assert.That(_client.SendSpectateCheer(77701UL, 7u, 0u), Is.False, "非法 phraseId=7 不发包");
            Assert.That(_client.SendSpectateCheer(77701UL, 3u, 2u), Is.False, "非法 side=2 不发包");

            _now += 2f;
            Assert.That(_client.SendSpectateCheer(77701UL, 3u, 0u), Is.True);
            Assert.That(_net.Calls[2].MessageId, Is.EqualTo(MatchServiceRpc.SendTournamentSpectateCheerMessageId));
            var sendResp = new SendTournamentSpectateCheerResponse
            {
                BattleId = 77701UL,
                CountA = 1UL,
                CountB = 0UL,
                CooldownUntilMs = 115_000UL,
                ServerTimeMs = 110_000UL,
            };
            sendResp.RecentCheers.Add(new TournamentSpectateCheerItem { Seq = 1UL, PhraseId = 3u, Side = 0u, SenderName = "道友甲" });
            _net.Calls[2].Reply(sendResp);
            Assert.That(_client.SpectateCheer, Is.Not.Null);
            Assert.That(_client.SpectateCheer.CountA, Is.EqualTo(1UL));
            Assert.That(_client.SpectateCheer.LatestSeq, Is.EqualTo(1UL));
            Assert.That(_client.SpectateCheer.CooldownRemainingMs(_now + 1f), Is.EqualTo(4_000UL));

            // 增量拉取 RPC 508 自动带 after_seq=1 并保留已有条目
            _now += 2f;
            Assert.That(_client.RequestSpectateCheer(77701UL), Is.True);
            var getReq = (GetTournamentSpectateCheerRequest)_net.Calls[3].Request;
            Assert.That(getReq.AfterSeq, Is.EqualTo(1UL));
            var getResp = new GetTournamentSpectateCheerResponse
            {
                BattleId = 77701UL,
                CountA = 1UL,
                CountB = 2UL,
                ServerTimeMs = 112_000UL,
            };
            getResp.RecentCheers.Add(new TournamentSpectateCheerItem { Seq = 2UL, PhraseId = 6u, Side = 1u, SenderName = "道友乙" });
            _net.Calls[3].Reply(getResp);
            Assert.That(_client.SpectateCheer.CountB, Is.EqualTo(2UL));
            Assert.That(_client.SpectateCheer.RecentCheers.Count, Is.EqualTo(2));
            Assert.That(_client.SpectateCheer.LatestSeq, Is.EqualTo(2UL));

            _client.ClearSpectateCheer();
            Assert.That(_client.SpectateCheer, Is.Null);
        }

        [Test]
        public void V3_EmblemWarmupPredictionAndBoard_UpdateStateAndClearOnReset()
        {
            // 先建立赛季与我的战队
            Assert.That(_client.RequestOverview(includeHistory: true), Is.True);
            var sq = new TournamentSquadView
            {
                SquadId = 5001UL,
                SeasonId = 1u,
                Name = "青云剑宗",
                CaptainPlayerId = 1001UL,
                State = TournamentSquadState.Registered,
                MemberCount = 3u,
                EmblemId = 1u,
            };
            _net.Calls[0].Reply(new GetTournamentOverviewResponse
            {
                CurrentSeason = new TournamentSeasonView { SeasonId = 1u, Stage = TournamentStage.Qualifier, Enabled = true, BattleTeamSize = 3u },
                MySquad = sq,
                ServerTimeMs = 200_000UL,
            });

            // 1. 模块 G：建队携带 emblemId 本地校验 + 更换战旗 RPC
            Assert.That(_client.CreateSquad("非法旗队", 99u), Is.False, "非法 emblemId=99 本地拦截");
            Assert.That(_client.UpdateSquadEmblem(0u), Is.False, "非法 emblemId=0 本地拦截");
            _now += 2f;
            Assert.That(_client.UpdateSquadEmblem(6u), Is.True);
            Assert.That(_net.Calls[1].MessageId, Is.EqualTo(MatchServiceRpc.UpdateTournamentSquadEmblemMessageId));
            var updatedSq = sq.Clone();
            updatedSq.EmblemId = 6u;
            _net.Calls[1].Reply(new UpdateTournamentSquadEmblemResponse { Squad = updatedSq });
            Assert.That(_client.Overview.MySquad.EmblemId, Is.EqualTo(6u));

            // 2. 模块 D：热身约战（定向 + 自选火炽灵脉）触发开战事件
            ulong launched = 0UL;
            _client.MatchBattleLaunched += bid => launched = bid;
            _now += 2f;
            Assert.That(_client.StartWarmup(6001UL, TournamentLeylineElement.TournamentLeylineFire, new ulong[] { 1001UL, 1002UL, 1003UL }), Is.True);
            Assert.That(_net.Calls[2].MessageId, Is.EqualTo(MatchServiceRpc.StartTournamentWarmupMessageId));
            var warmReq = (StartTournamentWarmupRequest)_net.Calls[2].Request;
            Assert.That(warmReq.TargetSquadId, Is.EqualTo(6001UL));
            Assert.That(warmReq.LeylineElement, Is.EqualTo(TournamentLeylineElement.TournamentLeylineFire));
            _net.Calls[2].Reply(new StartTournamentWarmupResponse
            {
                BattleId = 88801UL,
                Match = new TournamentMatchView { MatchId = 9001UL, Phase = TournamentMatchPhase.Warmup, BattleId = 88801UL },
            });
            Assert.That(launched, Is.EqualTo(88801UL));

            // 3. 模块 E：拉取对阵树后提交免费预测，立即同步对阵节点选中态与票数
            _now += 2f;
            Assert.That(_client.RequestStandings(1u, 1u, 20u), Is.True);
            var stResp = new ListTournamentStandingsResponse { Season = _client.Overview.Season.Clone(), TotalSquads = 4u };
            stResp.KnockoutBracket.Add(new TournamentMatchView
            {
                MatchId = 7001UL,
                Phase = TournamentMatchPhase.Knockout,
                RoundIndex = 2u,
                SlotIndex = 1u,
                IsThirdPlace = true,
                SquadAId = 5001UL,
                SquadBId = 6001UL,
                State = TournamentMatchState.WaitingOpponent,
            });
            _net.Calls[3].Reply(stResp);

            _now += 2f;
            Assert.That(_client.PredictMatch(7001UL, 5001UL), Is.True);
            Assert.That(_net.Calls[4].MessageId, Is.EqualTo(MatchServiceRpc.PredictTournamentMatchMessageId));
            _net.Calls[4].Reply(new PredictTournamentMatchResponse
            {
                MatchId = 7001UL,
                PredictedSquadId = 5001UL,
                PredictCountA = 12u,
                PredictCountB = 5u,
            });
            Assert.That(_client.Standings.KnockoutBracket[0].MyPredictedSquadId, Is.EqualTo(5001UL));
            Assert.That(_client.Standings.KnockoutBracket[0].PredictCountA, Is.EqualTo(12u));
            Assert.That(_client.Standings.KnockoutBracket[0].PredictCountB, Is.EqualTo(5u));

            // 4. 模块 E：拉取《神机问道榜》并在 Reset() 时清理干净
            _now += 2f;
            Assert.That(_client.RequestPredictionBoard(1u), Is.True);
            Assert.That(_net.Calls[5].MessageId, Is.EqualTo(MatchServiceRpc.GetTournamentPredictionBoardMessageId));
            var boardResp = new GetTournamentPredictionBoardResponse
            {
                SeasonId = 1u,
                MinHitsForReward = 2u,
                MyHitCount = 3u,
                MyTotalPoints = 9u,
                MyRank = 1u,
            };
            boardResp.TopPredictors.Add(new TournamentPredictionRankEntry { Rank = 1u, PlayerId = 1001UL, PlayerName = "神机道长", HitCount = 3u, TotalPredictions = 4u, TotalPoints = 9u });
            _net.Calls[5].Reply(boardResp);
            Assert.That(_client.PredictionBoard, Is.Not.Null);
            Assert.That(_client.PredictionBoard.MyTotalPoints, Is.EqualTo(9u));
            Assert.That(_client.PredictionBoard.TopPredictors.Count, Is.EqualTo(1));

            // 5. 4 个 V3 Tip 描述完整映射
            Assert.That(TournamentClient.DescribeTip(new TipInfoMessage { Id = (uint)match_error.KMatchTournamentEmblemInvalid }), Does.Contain("战旗"));
            Assert.That(TournamentClient.DescribeTip(new TipInfoMessage { Id = (uint)match_error.KMatchTournamentPredictionClosed }), Does.Contain("封盘"));
            Assert.That(TournamentClient.DescribeTip(new TipInfoMessage { Id = (uint)match_error.KMatchTournamentPredictionTargetInvalid }), Does.Contain("对阵双方"));
            Assert.That(TournamentClient.DescribeTip(new TipInfoMessage { Id = (uint)match_error.KMatchTournamentWarmupUnavailable }), Does.Contain("热身"));

            _client.Reset();
            Assert.That(_client.PredictionBoard, Is.Null);
        }
    }
}
