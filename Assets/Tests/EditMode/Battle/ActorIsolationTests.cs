using System;
using System.Collections.Generic;
using Match;
using MmorpgClient.Game;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Pet;
using MmorpgClient.Game.Ranked;
using MmorpgClient.Game.Tournament;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 跨模块事件总线、核心客户端对称 Dispose 退订与只读集合视图隔离回归测试。
    /// </summary>
    public sealed class ActorIsolationTests
    {
        [TearDown]
        public void TearDown()
        {
            UiDomainEvents.ResetForTests();
        }

        [Test]
        public void UiDomainEvents_ToastPublishAndUnsubscribeWork()
        {
            var received = new List<ToastNoticeEvent>();
            Action<ToastNoticeEvent> handler = ev => received.Add(ev);
            UiDomainEvents.ToastRequested += handler;

            UiDomainEvents.PublishToast("目标不可到达", isError: true);
            UiDomainEvents.PublishToast(string.Empty, isError: false);

            Assert.That(received.Count, Is.EqualTo(1));
            Assert.That(received[0].Message, Is.EqualTo("目标不可到达"));
            Assert.That(received[0].IsError, Is.True);

            UiDomainEvents.ToastRequested -= handler;
            UiDomainEvents.PublishToast("退订后不应收到");
            Assert.That(received.Count, Is.EqualTo(1));
        }

        [Test]
        public void UiDomainEvents_RankedInterceptorsUnregisterSafely()
        {
            int endCalls = 0;
            int recoveredCalls = 0;
            Func<BattleEndS2C, uint, bool> onEnd = (_, _) => { endCalls++; return true; };
            Func<PlayerBattleResult, bool> onRec = _ => { recoveredCalls++; return true; };
            Func<BattleEndS2C, uint, bool> otherEnd = (_, _) => false;
            Func<PlayerBattleResult, bool> otherRec = _ => false;

            UiDomainEvents.RegisterRankedSettlementHandlers(onEnd, onRec);
            Assert.That(UiDomainEvents.TryInterceptRankedBattleEnd(new BattleEndS2C(), 0u), Is.True);
            Assert.That(UiDomainEvents.TryInterceptRankedRecoveredResult(new PlayerBattleResult()), Is.True);
            Assert.That(endCalls, Is.EqualTo(1));
            Assert.That(recoveredCalls, Is.EqualTo(1));

            // 非原注册者误注销不得清掉当前有效接管者
            UiDomainEvents.UnregisterRankedSettlementHandlers(otherEnd, otherRec);
            Assert.That(UiDomainEvents.TryInterceptRankedBattleEnd(new BattleEndS2C(), 0u), Is.True);
            Assert.That(UiDomainEvents.TryInterceptRankedRecoveredResult(new PlayerBattleResult()), Is.True);

            UiDomainEvents.UnregisterRankedSettlementHandlers(onEnd, onRec);
            Assert.That(UiDomainEvents.TryInterceptRankedBattleEnd(new BattleEndS2C(), 0u), Is.False);
            Assert.That(UiDomainEvents.TryInterceptRankedRecoveredResult(new PlayerBattleResult()), Is.False);
        }

        [Test]
        public void BattleClient_DisposeUnsubscribesAndIgnoresStaleNotifies()
        {
            var net = new FakeBattleTransport();
            var channel = new FakeBattleChannel();
            var client = new BattleClient(net, channel);
            int phaseChanges = 0;
            int battleStarts = 0;
            client.OnPhaseChanged += _ => phaseChanges++;
            client.OnBattleStart += _ => battleStarts++;

            client.Dispose();
            client.Dispose(); // 幂等

            net.RaiseDisconnected();
            channel.RaiseReady(100ul);
            channel.RaiseLost(100ul, BattleLinkCloseKind.BattleGone);
            net.PushNotify(MessageIds.NotifyBattleStart, new BattleStartS2C
            {
                BattleId = 100ul,
                State = new BattleStateS2C { BattleId = 100ul, RoundIndex = 1u },
            });

            Assert.That(phaseChanges, Is.EqualTo(0));
            Assert.That(battleStarts, Is.EqualTo(0));
            Assert.That(client.HasActiveBattle, Is.False);
        }

        [Test]
        public void SpectateClient_DisposeUnsubscribesAndIgnoresStaleNotifies()
        {
            var net = new FakeBattleTransport();
            var channel = new FakeBattleChannel();
            var client = new SpectateClient(net, channel);
            int stateEvents = 0;
            int phaseEvents = 0;
            client.OnSpectateState += _ => stateEvents++;
            client.OnPhaseChanged += _ => phaseEvents++;

            client.Dispose();
            client.Dispose();

            net.RaiseDisconnected();
            channel.RaiseReady(200ul);
            channel.RaiseLost(200ul, BattleLinkCloseKind.Unreachable);
            net.PushNotify(MessageIds.NotifySpectateState, new SpectateStateS2C
            {
                State = new BattleStateS2C { BattleId = 200ul, RoundIndex = 1u },
                ObserverCount = 3u,
            });

            Assert.That(stateEvents, Is.EqualTo(0));
            Assert.That(phaseEvents, Is.EqualTo(0));
            Assert.That(client.Phase, Is.EqualTo(SpectatePhase.None));
        }

        [Test]
        public void PetClient_DisposeUnsubscribesAndIgnoresStaleNotifies()
        {
            var net = new FakeBattleTransport();
            var client = new PetClient(net);
            int listEvents = 0;
            client.OnList += _ => listEvents++;

            client.Dispose();
            client.Dispose();

            net.RaiseDisconnected();
            net.PushNotify(MessageIds.NotifyPetListChanged, new PetListChangedS2C
            {
                Pets = new PetListInfo(),
            });

            Assert.That(listEvents, Is.EqualTo(0));
            Assert.That(client.HasList, Is.False);
        }

        [Test]
        public void ReadOnlyCollections_PreventDowncastMutationAcrossSnapshotsAndClients()
        {
            var net = new FakeBattleTransport();
            using var reports = new BattleReportClient(net);
            IReadOnlyList<BattleReportSummary> updatedArg = null;
            reports.OnListUpdated += list => updatedArg = list;
            Assert.That(reports.RefreshList(), Is.True);
            var resp = new ListBattleReportsResponse();
            resp.Reports.Add(new BattleReportSummary { BattleId = 77ul });
            net.CallsOf(MatchServiceRpc.ListBattleReportsMessageId)[0].Respond(resp);

            Assert.That(reports.Reports, Is.Not.InstanceOf<List<BattleReportSummary>>());
            Assert.That(updatedArg, Is.Not.InstanceOf<List<BattleReportSummary>>());
            Assert.Throws<NotSupportedException>(() => ((IList<BattleReportSummary>)reports.Reports).Clear());

            using var battle = new BattleClient(net);
            Assert.That(battle.CurrentTeamPings, Is.Not.InstanceOf<List<BattleTacticalPing>>());
            Assert.Throws<NotSupportedException>(() => ((IList<BattleTacticalPing>)battle.CurrentTeamPings).Clear());

            var mutableInvites = new List<TournamentInviteView> { new TournamentInviteView { SquadId = 10ul } };
            var tourney = new TournamentOverviewSnapshot(
                season: null,
                mySquad: null,
                pendingInvites: mutableInvites,
                myActiveMatch: null,
                featuredMatches: null,
                myCheeredSquadId: 0ul,
                myCheeredSquadName: string.Empty,
                rewardTiers: null,
                myRewards: null,
                hallOfFame: null,
                serverTimeMs: 0ul,
                capturedAtLocalSeconds: 0f);
            mutableInvites.Clear();
            Assert.That(tourney.PendingInvites.Count, Is.EqualTo(1));
            Assert.That(tourney.PendingInvites, Is.Not.InstanceOf<List<TournamentInviteView>>());
            Assert.Throws<NotSupportedException>(() => ((IList<TournamentInviteView>)tourney.PendingInvites).Clear());

            var rankedSeason = new RankedSeasonInfo { SeasonId = 1u };
            rankedSeason.AllowedTeamSizes.Add(1u);
            rankedSeason.AllowedTeamSizes.Add(3u);
            var ranked = new RankedProfileSnapshot(
                currentSeason: rankedSeason,
                currentProgress: null,
                selectedPresets: null,
                cosmeticLoadout: null,
                characterPresets: new List<RankedPresetView> { new RankedPresetView { PresetId = 101u } },
                petPresets: null,
                entitlements: null,
                history: null,
                pastSeasons: null,
                serverTimeMs: 0ul);
            Assert.That(ranked.AllowedTeamSizes, Is.Not.InstanceOf<uint[]>());
            Assert.Throws<NotSupportedException>(() => ((IList<uint>)ranked.AllowedTeamSizes).Add(5u));
            Assert.Throws<NotSupportedException>(() => ((IList<RankedPresetView>)ranked.CharacterPresets).Clear());
        }
    }
}
