using System;
using System.Collections.Generic;
using System.IO;
using MmorpgClient.Game.FestivalBeast;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class FestivalBeastClientTests
    {
        private const ulong PlayerId = 99601UL;
        private const uint InstanceId = 202601u;
        private const uint ConfigRevision = 2u;
        private const string RewardIdentity = "festival_beast_chunxishou_v1";
        private const ulong StartMs = 1795564800000UL; // 2026-11-25 00:00:00 UTC = 2026-11-25 08:00:00 UTC+8
        private const ulong EndMs = StartMs + 14UL * 86400000UL;

        private string _tempDir;
        private DurableRequestFileStore _launchStore;
        private DurableRequestFileStore _claimStore;
        private FakeBattleTransport _net;
        private FestivalBeastClient _client;
        private object _conn;
        private long _nowMs;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "festival-beast-client-test-" + Guid.NewGuid().ToString("N"));
            _launchStore = new DurableRequestFileStore(Path.Combine(_tempDir, "launch"));
            _claimStore = new DurableRequestFileStore(Path.Combine(_tempDir, "claim"));
            _net = new FakeBattleTransport { PlayerId = PlayerId, IsReady = true };
            _conn = new object();
            _nowMs = 100_000L;
            CreateClient();
        }

        private void CreateClient()
        {
            _client?.Dispose();
            _client = new FestivalBeastClient(_net, () => _conn, () => _nowMs)
            {
                LaunchRecoveryStore = _launchStore,
                ClaimRecoveryStore = _claimStore,
            };
        }

        [TearDown]
        public void TearDown()
        {
            _client?.Dispose();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }

        private static GetFestivalBeastDetailResponse MakeOpenDetail(
            bool canStart = true,
            bool canClaim = false,
            bool rewardClaimed = false,
            bool rewardDurable = false,
            string claimReqId = "",
            ulong claimedPetId = 0UL,
            FestivalBeastPendingLaunchInfo pendingLaunch = null)
        {
            var resp = new GetFestivalBeastDetailResponse
            {
                ServerNowMs = StartMs + 3600_000UL,
                Phase = FestivalBeastPhase.Open,
                ActivityInstanceId = InstanceId,
                ConfigRevision = ConfigRevision,
                ActivityName = "瑞兽迎春",
                BossName = "春禧兽",
                EffectiveStartAtMs = StartMs,
                EffectiveEndAtMs = EndMs,
                ClaimShowcaseEndAtMs = EndMs + 7UL * 86400000UL,
                CurrentWins = canClaim || rewardClaimed ? 3u : 1u,
                RequiredWins = 3u,
                RewardIdentity = RewardIdentity,
                RewardDefinitionVersion = 1u,
                RewardPetTableId = 101u,
                RewardPetModelId = 1012u,
                RewardPetName = "春禧兽",
                HasPermanentEligibility = canClaim || rewardClaimed,
                RewardClaimed = rewardClaimed,
                RewardClaimDurable = rewardDurable,
                ClaimedPetId = claimedPetId,
                ClaimRequestId = claimReqId,
                CanStartChallenge = canStart,
                CanClaimReward = canClaim,
                MatchedTierId = 3u,
                RoundLimit = 12u,
                HeavyDamagePercent = 180u,
                StateRevision = 5u,
                MaterialSummary = new FestivalBeastMaterialSummary
                {
                    QuotaMode = FestivalBeastMaterialQuotaMode.FestivalMaterialQuotaStage1Lifetime,
                    ExpectedWinUnits = 10u,
                    EarnableRemainingUnits = 650u,
                    AvailableUnits = 20UL,
                    AvailableTradableUnits = 2UL,
                    EarnedToday = 10u,
                    DailyLimit = 20u,
                },
            };
            if (pendingLaunch != null) resp.PendingLaunch = pendingLaunch;
            return resp;
        }

        private void LoadDetail(GetFestivalBeastDetailResponse resp)
        {
            _client.RefreshDetail(force: true);
            var calls = _net.CallsOf(SceneActivityClientPlayerRpc.GetFestivalBeastDetailMessageId);
            calls[calls.Count - 1].Respond(resp);
        }

        [Test]
        public void BeijingTimeFormattingCountdownAndDisplayPriorityFollowContract()
        {
            // 2026-11-25 00:00:00 UTC -> 2026-11-25 08:00:00 (UTC+8)
            Assert.That(FestivalBeastModels.FormatBeijingDateTime(StartMs), Is.EqualTo("2026-11-25 08:00:00"));
            Assert.That(FestivalBeastModels.FormatBeijingRange(StartMs, EndMs),
                Is.EqualTo("2026-11-25 08:00:00 - 2026-12-09 08:00:00 (UTC+8)"));
            Assert.That(FestivalBeastModels.ComputeRemainingMs(EndMs, EndMs - 5000UL, 2000L), Is.EqualTo(3000UL));
            Assert.That(FestivalBeastModels.ComputeRemainingMs(EndMs, EndMs - 1000UL, 5000L), Is.EqualTo(0UL));

            // 优先级：领奖确认中 > 待领奖 > 在途开局 > 开放可挑战
            var confirming = new FestivalBeastDetailSnapshot(
                MakeOpenDetail(canStart: true, canClaim: true, rewardClaimed: true, rewardDurable: false, claimReqId: Guid.NewGuid().ToString("D")), 0L);
            Assert.That(confirming.DisplayPriority, Is.EqualTo(FestivalBeastDisplayPriority.ClaimConfirming));

            var claimable = new FestivalBeastDetailSnapshot(MakeOpenDetail(canStart: true, canClaim: true), 0L);
            Assert.That(claimable.DisplayPriority, Is.EqualTo(FestivalBeastDisplayPriority.Claimable));

            var inFlight = new FestivalBeastDetailSnapshot(MakeOpenDetail(canStart: true, canClaim: false,
                pendingLaunch: new FestivalBeastPendingLaunchInfo
                {
                    ClientRequestId = Guid.NewGuid().ToString("D"),
                    LaunchState = FestivalBeastLaunchState.FestivalBeastLaunchLaunching,
                }), 0L);
            Assert.That(inFlight.DisplayPriority, Is.EqualTo(FestivalBeastDisplayPriority.InBattleOrLaunchInFlight));

            var open = new FestivalBeastDetailSnapshot(MakeOpenDetail(canStart: true, canClaim: false), 0L);
            Assert.That(open.DisplayPriority, Is.EqualTo(FestivalBeastDisplayPriority.OpenChallengeable));
        }

        [Test]
        public void StartChallengePersistsUuidReplaysSameUuidOnUnknownAndClearsOnAdmitted()
        {
            LoadDetail(MakeOpenDetail(canStart: true));
            var admitted = new List<FestivalBeastLaunchEvent>();
            _client.OnChallengeAdmitted += admitted.Add;

            Assert.That(_client.StartChallenge(), Is.True);
            var calls = _net.CallsOf(SceneActivityClientPlayerRpc.StartFestivalBeastChallengeMessageId);
            Assert.That(calls.Count, Is.EqualTo(1));
            string firstUuid = ((StartFestivalBeastChallengeRequest)calls[0].Request).ClientRequestId;
            Assert.That(Guid.TryParseExact(firstUuid, "D", out _), Is.True);
            Assert.That(_launchStore.Load(PlayerId), Is.Not.Empty);

            // 网络超时/失败：保留原 UUID，绝不生成第二个 UUID。
            calls[0].FailWith("timeout");
            Assert.That(_client.HasPendingLaunch, Is.True);
            Assert.That(_client.PendingLaunchRequestId, Is.EqualTo(firstUuid));

            _nowMs += 3000L;
            _client.Tick();
            calls = _net.CallsOf(SceneActivityClientPlayerRpc.StartFestivalBeastChallengeMessageId);
            Assert.That(calls.Count, Is.EqualTo(2));
            Assert.That(((StartFestivalBeastChallengeRequest)calls[1].Request).ClientRequestId, Is.EqualTo(firstUuid));

            // 收到 ADMITTED 后清理本机恢复槽并发布精确 64 位 ID 事件。
            calls[1].Respond(new StartFestivalBeastChallengeResponse
            {
                LaunchState = FestivalBeastLaunchState.FestivalBeastLaunchAdmitted,
                ClientRequestId = firstUuid,
                BattleId = 9007199254740993UL,
                LockedPetId = 8000000000000001UL,
                MatchedTierId = 3u,
                AdmittedAtMs = StartMs + 3600_500UL,
                Replayed = true,
            });
            Assert.That(_client.HasPendingLaunch, Is.False);
            Assert.That(_launchStore.Load(PlayerId), Is.Empty);
            Assert.That(admitted.Count, Is.EqualTo(1));
            Assert.That(admitted[0].BattleIdText, Is.EqualTo("9007199254740993"));
            Assert.That(admitted[0].LockedPetIdText, Is.EqualTo("8000000000000001"));
        }

        [Test]
        public void ClaimRewardWaitsForDurableTrueAndReusesOriginalUuidAcrossRetries()
        {
            LoadDetail(MakeOpenDetail(canStart: false, canClaim: true));
            var claimed = new List<FestivalBeastRewardClaimedEvent>();
            _client.OnRewardClaimed += claimed.Add;

            Assert.That(_client.ClaimReward(), Is.True);
            var calls = _net.CallsOf(SceneActivityClientPlayerRpc.ClaimFestivalBeastRewardMessageId);
            Assert.That(calls.Count, Is.EqualTo(1));
            string claimUuid = ((ClaimFestivalBeastRewardRequest)calls[0].Request).ClientRequestId;
            Assert.That(_claimStore.Load(PlayerId), Is.Not.Empty);

            // 第一次返回 durable=false：不能提前宣称领取成功，保留原 UUID 等自动确认。
            calls[0].Respond(new ClaimFestivalBeastRewardResponse
            {
                Outcome = FestivalBeastClaimOutcome.FestivalBeastClaimApplied,
                Durable = false,
                ClientRequestId = claimUuid,
                RewardIdentity = RewardIdentity,
                RewardDefinitionVersion = 1u,
                RewardPetTableId = 101u,
                PetId = 96000001UL,
            });
            Assert.That(claimed, Is.Empty);
            Assert.That(_client.HasPendingClaim, Is.True);
            Assert.That(_client.PendingClaimRequestId, Is.EqualTo(claimUuid));

            // Tick 触发同 UUID 自动重查，拿到 durable=true 后才公开成功并清理本机槽。
            _nowMs += 3000L;
            _client.Tick();
            calls = _net.CallsOf(SceneActivityClientPlayerRpc.ClaimFestivalBeastRewardMessageId);
            Assert.That(calls.Count, Is.EqualTo(2));
            Assert.That(((ClaimFestivalBeastRewardRequest)calls[1].Request).ClientRequestId, Is.EqualTo(claimUuid));

            calls[1].Respond(new ClaimFestivalBeastRewardResponse
            {
                Outcome = FestivalBeastClaimOutcome.FestivalBeastClaimApplied,
                Durable = true,
                Replayed = true,
                ClientRequestId = claimUuid,
                RewardIdentity = RewardIdentity,
                RewardDefinitionVersion = 1u,
                RewardPetTableId = 101u,
                PetId = 96000001UL,
                ClaimedAtMs = StartMs + 7200_000UL,
            });
            Assert.That(claimed.Count, Is.EqualTo(1));
            Assert.That(claimed[0].PetId, Is.EqualTo(96000001UL));
            Assert.That(claimed[0].PetIdText, Is.EqualTo("96000001"));
            Assert.That(claimed[0].Durable, Is.True);
            Assert.That(_client.HasPendingClaim, Is.False);
            Assert.That(_claimStore.Load(PlayerId), Is.Empty);
        }

        [Test]
        public void NewDeviceWithoutLocalStoreRecoversInFlightLaunchAndConfirmingClaimFromDetail()
        {
            // 模拟新设备：磁盘存储完全为空，直接拉详情恢复服务端已落账的在途开局与待确认领奖 UUID。
            string serverLaunchUuid = "11111111-2222-4333-8444-555555555551";
            string serverClaimUuid = "11111111-2222-4333-8444-555555555552";
            Assert.That(_launchStore.Load(PlayerId), Is.Empty);
            Assert.That(_claimStore.Load(PlayerId), Is.Empty);

            var detailResp = MakeOpenDetail(
                canStart: false,
                canClaim: false,
                rewardClaimed: true,
                rewardDurable: false,
                claimReqId: serverClaimUuid,
                claimedPetId: 96000002UL,
                pendingLaunch: new FestivalBeastPendingLaunchInfo
                {
                    ClientRequestId = serverLaunchUuid,
                    ActivityInstanceId = InstanceId,
                    ConfigRevision = ConfigRevision,
                    LockedPetId = 70001UL,
                    LaunchState = FestivalBeastLaunchState.FestivalBeastLaunchUnknown,
                });
            LoadDetail(detailResp);

            Assert.That(_client.PendingLaunchRequestId, Is.EqualTo(serverLaunchUuid));
            Assert.That(_client.PendingClaimRequestId, Is.EqualTo(serverClaimUuid));
            Assert.That(_client.Detail.ClaimedPetIdText, Is.EqualTo("96000002"));

            // 新设备发起 ClaimReward 时直接复用服务端恢复的 serverClaimUuid，不新造 UUID。
            Assert.That(_client.ClaimReward(), Is.True);
            var claimCalls = _net.CallsOf(SceneActivityClientPlayerRpc.ClaimFestivalBeastRewardMessageId);
            Assert.That(claimCalls.Count, Is.EqualTo(1));
            Assert.That(((ClaimFestivalBeastRewardRequest)claimCalls[0].Request).ClientRequestId, Is.EqualTo(serverClaimUuid));
        }

        [Test]
        public void BattleTelegraphSettlementAndSessionIsolationWorkDeterministically()
        {
            int telegraphEvents = 0;
            _client.OnTelegraphChanged += () => ++telegraphEvents;

            var state2 = new BattleStateS2C { RoundIndex = 2u };
            state2.Telegraphs.Add(new BattleTelegraphEntry
            {
                SourceActorId = 9001UL,
                TargetActorId = 8001UL,
                TelegraphRound = 2u,
                ExecuteRound = 3u,
                IntentKind = FestivalBeastModels.TelegraphKindFestivalHeavyStrike,
                Status = FestivalBeastModels.TelegraphStatusUpcoming,
                StateVersion = 1UL,
            });
            _client.HandleBattleState(state2);
            Assert.That(telegraphEvents, Is.EqualTo(1));
            Assert.That(_client.ActiveTelegraph.TargetActorIdText, Is.EqualTo("8001"));
            Assert.That(_client.ActiveTelegraph.IsUpcoming, Is.True);

            var turn3 = new TurnResultS2C { RoundIndex = 3u, State = new BattleStateS2C { RoundIndex = 4u } };
            turn3.ResolvedTelegraphs.Add(new BattleTelegraphEntry
            {
                SourceActorId = 9001UL,
                TargetActorId = 8001UL,
                TelegraphRound = 2u,
                ExecuteRound = 3u,
                IntentKind = FestivalBeastModels.TelegraphKindFestivalHeavyStrike,
                Status = FestivalBeastModels.TelegraphStatusCancelledByControl,
                StateVersion = 3UL,
            });
            _client.HandleBattleState(turn3.State, turn3);
            Assert.That(_client.ActiveTelegraph, Is.Null);
            Assert.That(_client.LastResolvedTelegraph.IsCancelledByControl, Is.True);
            Assert.That(_client.LastResolvedTelegraph.StatusLabel, Does.Contain("控制取消"));

            // 现场结算与离线补查均提取权威摘要并触发刷新详情。
            var settlements = new List<FestivalBeastSettlementSnapshot>();
            _client.OnSettlementReceived += settlements.Add;
            _client.HandleBattleSettled(new BattleEndS2C
            {
                BattleId = 55001UL,
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                FestivalBeastSettlement = new FestivalBeastSettlementSummary
                {
                    ActivityInstanceId = InstanceId,
                    RewardIdentity = RewardIdentity,
                    WinsBefore = 2u,
                    WinsAfter = 3u,
                    RequiredWins = 3u,
                    NewlyEligible = true,
                    MaterialUnitsAdded = 10u,
                    MaterialAvailableUnitsAfter = 30UL,
                    MaterialEarnableRemainingAfter = 640u,
                },
            });
            Assert.That(settlements.Count, Is.EqualTo(1));
            Assert.That(settlements[0].NewlyEligible, Is.True);
            Assert.That(settlements[0].MaterialUnitsAdded, Is.EqualTo(10u));
            Assert.That(_net.CallsOf(SceneActivityClientPlayerRpc.GetFestivalBeastDetailMessageId).Count, Is.EqualTo(1));

            // 切角隔离：切角后旧在途回包丢弃，状态复位。
            _net.PlayerId = PlayerId + 1UL;
            _client.Tick();
            Assert.That(_client.Detail, Is.Null);
            Assert.That(_client.LastSettlement, Is.Null);
            var oldCall = _net.CallsOf(SceneActivityClientPlayerRpc.GetFestivalBeastDetailMessageId)[0];
            oldCall.Respond(MakeOpenDetail());
            Assert.That(_client.Detail, Is.Null);
        }
    }
}
