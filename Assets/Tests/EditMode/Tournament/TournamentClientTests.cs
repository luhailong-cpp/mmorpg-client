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
    }
}
