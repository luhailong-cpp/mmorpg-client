using System;
using System.Collections.Generic;
using System.IO;
using Google.Protobuf;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class ActivityVitalityClientTests
    {
        private const ulong Player = 9206;
        private const uint Rule = 71;
        private const uint Threshold = 19;
        private const ulong Period = 1800000000000;
        private const ulong Revision = 37;
        private string _directory;
        private FakeBattleTransport _net;
        private PlayerFeaturesClient _client;
        private DurableRequestFileStore _store;
        private readonly List<uint> _claimed = new();
        private long _now;
        private object _connection;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "activity-reward-test-" + Guid.NewGuid().ToString("N"));
            _store = new DurableRequestFileStore(_directory);
            _net = new FakeBattleTransport { PlayerId = Player, IsReady = true };
            _now = 10000;
            _connection = new object();
            _claimed.Clear();
            CreateClient();
        }

        private void CreateClient()
        {
            _client = new PlayerFeaturesClient(_net, () => _connection)
            {
                ActivityRewardRecovery = _store,
                NowMs = () => _now,
            };
            _client.OnActivityRewardClaimed += _claimed.Add;
        }

        [TearDown]
        public void TearDown()
        {
            _client.Dispose();
            if (!Directory.Exists(_directory)) return;
            Assert.That(Path.GetDirectoryName(Path.GetFullPath(_directory)),
                Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)));
            Assert.That(Path.GetFileName(_directory), Does.StartWith("activity-reward-test-"));
            Directory.Delete(_directory, true);
        }

        private static PlayerActivityVitalitySnapshot Snapshot(ulong period = Period, ulong revision = Revision,
            bool canClaim = true)
        {
            var snapshot = new PlayerActivityVitalitySnapshot
            {
                Available = true, RuleId = Rule, PeriodId = period, ConfigurationRevision = revision,
                PeriodStartsAtMs = period, PeriodEndsAtMs = period + 86400000, Points = 43, PointsLimit = 73,
            };
            snapshot.Rewards.Add(new PlayerActivityVitalityReward
                { ThresholdId = Threshold, RequiredPoints = 41, RewardId = 5, CanClaim = canClaim });
            return snapshot;
        }

        private void Load(ulong period = Period, ulong revision = Revision, bool canClaim = true)
        {
            _client.RequestActivities();
            var reads = _net.CallsOf(SceneActivityClientPlayerRpc.GetActivityListMessageId);
            reads[reads.Count - 1].Respond(new GetActivityListResponse { Vitality = Snapshot(period, revision, canClaim) });
        }

        private List<FakeBattleTransport.RecordedCall> Claims
            => _net.CallsOf(SceneActivityClientPlayerRpc.ClaimActivityVitalityRewardMessageId);
        private ClaimActivityVitalityRewardRequest LastRequest
            => (ClaimActivityVitalityRewardRequest)Claims[Claims.Count - 1].Request;
        private static ClaimActivityVitalityRewardRequest Decode(string record)
            => ClaimActivityVitalityRewardRequest.Parser.ParseFrom(Convert.FromBase64String(record));
        private void Claim() => _client.ClaimActivityVitalityReward(Rule, Period, Threshold, Revision);
        private void Later() => _now += 20000;
        private ClaimActivityVitalityRewardResponse Receipt(ClaimActivityVitalityRewardRequest request,
            bool claimed = true, bool durable = true)
            => new() { RequestId = request.RequestId, Claimed = claimed, Durable = durable, Snapshot = Snapshot() };

        [Test]
        public void RequestIsOnDiskBeforeTransportAndDoubleClickHasOneIntent()
        {
            Load();
            bool observedDuringChange = false;
            _client.OnChanged += () =>
            {
                if (!_client.BusyActivityReward) return;
                observedDuringChange = true;
                Assert.That(_store.Load(Player), Is.Not.Empty);
                Assert.That(Claims, Is.Empty, "落盘完成后才能进入发送入口。");
            };
            Claim();
            Assert.That(observedDuringChange, Is.True);
            Assert.That(Claims.Count, Is.EqualTo(1));
            Assert.That(Decode(_store.Load(Player)), Is.EqualTo(LastRequest));
            Assert.That(Guid.TryParseExact(LastRequest.RequestId, "D", out var id) && id != Guid.Empty, Is.True);
            Claim();
            Assert.That(Claims.Count, Is.EqualTo(1));
        }

        [Test]
        public void MissingOrUnwritableRecoveryStoreRefusesToSend()
        {
            Load();
            _client.ActivityRewardRecovery = null;
            Claim();
            Assert.That(Claims, Is.Empty);
            Assert.That(_client.ActivityRewardError, Is.Not.Empty);
            Directory.CreateDirectory(_directory);
            string blocked = Path.Combine(_directory, "file-not-directory");
            File.WriteAllText(blocked, "fixture");
            _client.ActivityRewardRecovery = new DurableRequestFileStore(blocked);
            Claim();
            Assert.That(Claims, Is.Empty);
            Assert.That(_client.ActivityRewardError, Is.Not.Empty);
        }

        [Test]
        public void ClaimableSnapshotAndExactRuleRevisionAreRequiredBeforeCreatingIntent()
        {
            Claim();
            Load(canClaim: false);
            Claim();
            Load();
            _client.ClaimActivityVitalityReward(Rule, Period, Threshold, Revision + 1);
            _client.ClaimActivityVitalityReward(Rule, Period + 1, Threshold, Revision);
            _client.ClaimActivityVitalityReward(Rule + 1, Period, Threshold, Revision);
            _client.ClaimActivityVitalityReward(Rule, Period, Threshold + 1, Revision);
            Assert.That(Claims, Is.Empty);
            Assert.That(_store.Load(Player), Is.Empty);
        }

        [Test]
        public void ClaimedButNotDurableKeepsIntentAndDoesNotNotifyOrRefreshAssets()
        {
            Load(); Claim();
            var request = LastRequest.Clone();
            Claims[0].Respond(Receipt(request, durable: false));
            Assert.That(_client.BusyActivityReward, Is.False);
            Assert.That(_client.HasPendingActivityReward, Is.True);
            Assert.That(Decode(_store.Load(Player)), Is.EqualTo(request));
            Assert.That(_claimed, Is.Empty);
            Assert.That(_net.CallsOf(SceneBagClientPlayerRpc.GetBagMessageId), Is.Empty);
            Later(); _client.RecoverActivityReward();
            Assert.That(LastRequest, Is.EqualTo(request));
        }

        [Test]
        public void DurableReceiptClearsIntentNotifiesOnceAndRefreshesAssets()
        {
            Load(); Claim();
            var call = Claims[0]; var receipt = Receipt(LastRequest);
            call.Respond(receipt);
            Assert.That(_store.Load(Player), Is.Empty);
            Assert.That(_client.HasPendingActivityReward, Is.False);
            CollectionAssert.AreEqual(new[] { Threshold }, _claimed);
            Assert.That(_net.CallsOf(SceneBagClientPlayerRpc.GetBagMessageId).Count, Is.EqualTo(1));
            Assert.That(_net.CallsOf(SceneActivityClientPlayerRpc.GetActivityListMessageId).Count, Is.EqualTo(2));
            call.Respond(receipt);
            CollectionAssert.AreEqual(new[] { Threshold }, _claimed, "重复回调不能重复发布领取成功。");
        }

        [Test]
        public void WrongReceiptIdentityCannotClearOriginalOrApplyForeignSnapshot()
        {
            Load(); Claim();
            var original = LastRequest.Clone(); var response = Receipt(original);
            response.RequestId = Guid.NewGuid().ToString("D");
            response.Snapshot.Points = 1;
            Claims[0].Respond(response);
            Assert.That(_client.Activities.Vitality.Points, Is.EqualTo(43));
            Assert.That(Decode(_store.Load(Player)), Is.EqualTo(original));
            Assert.That(_claimed, Is.Empty);
        }

        [Test]
        public void TimeoutPreservesRequestAndDifferentRewardCannotReplaceIt()
        {
            Load(); Claim(); var original = LastRequest.Clone();
            Claims[0].FailWith("rpc timeout");
            Later();
            _client.ClaimActivityVitalityReward(Rule, Period, Threshold + 1, Revision);
            Assert.That(Claims.Count, Is.EqualTo(1));
            Assert.That(Decode(_store.Load(Player)), Is.EqualTo(original));
            _client.RecoverActivityReward();
            Assert.That(Claims.Count, Is.EqualTo(2));
            Assert.That(LastRequest, Is.EqualTo(original));
        }

        [Test]
        public void RestartReadsOriginalIntentEvenWhenPeriodAndConfigurationChanged()
        {
            Load(); Claim(); var original = LastRequest.Clone();
            _client.Dispose(); CreateClient();
            Load(Period + 86400000, Revision + 1);
            Assert.That(Claims.Count, Is.EqualTo(2));
            Assert.That(LastRequest, Is.EqualTo(original));
            var response = Receipt(original); response.Snapshot = Snapshot(Period + 86400000, Revision + 1);
            Claims[1].Respond(response);
            Assert.That(_store.Load(Player), Is.Empty);
            CollectionAssert.AreEqual(new[] { Threshold }, _claimed);
        }

        [Test]
        public void DisconnectLateReceiptIsIgnoredAndReconnectReplaysPersistedIntent()
        {
            Load(); Claim(); var original = LastRequest.Clone(); var old = Claims[0];
            _net.IsReady = false; _net.RaiseDisconnected();
            old.Respond(Receipt(original));
            Assert.That(_claimed, Is.Empty);
            Assert.That(Decode(_store.Load(Player)), Is.EqualTo(original));
            _net.IsReady = true; Load();
            Assert.That(Claims.Count, Is.EqualTo(1), "断线不能清掉同客户端的发送节奏。");
            _now += 1000; _client.RecoverActivityReward();
            Assert.That(LastRequest, Is.EqualTo(original));
            Assert.That(Claims.Count, Is.EqualTo(2));
        }

        [Test]
        public void CharacterSwitchKeepsOldIntentButNeverAppliesOldReceiptToNewCharacter()
        {
            Load(); Claim(); var original = LastRequest.Clone(); var old = Claims[0];
            _net.PlayerId = Player + 1; Load();
            old.Respond(Receipt(original));
            Assert.That(_client.SnapshotPlayerId, Is.EqualTo(Player + 1));
            Assert.That(_client.HasPendingActivityReward, Is.False);
            Assert.That(_claimed, Is.Empty);
            Assert.That(Decode(_store.Load(Player)), Is.EqualTo(original));
            Assert.That(_store.Load(Player + 1), Is.Empty);
            _net.PlayerId = Player; Load();
            Assert.That(Claims.Count, Is.EqualTo(1));
            _now += 1000; _client.RecoverActivityReward();
            Assert.That(Claims.Count, Is.EqualTo(2));
            Assert.That(LastRequest, Is.EqualTo(original));
        }

        [Test]
        public void SilentConnectionReplacementRejectsReceiptAndRestoresSameIntent()
        {
            Load(); Claim(); var original = LastRequest.Clone(); var old = Claims[0];
            _connection = new object();
            old.Respond(Receipt(original));
            Assert.That(_claimed, Is.Empty);
            Assert.That(Decode(_store.Load(Player)), Is.EqualTo(original));
            Load();
            Assert.That(Claims.Count, Is.EqualTo(1));
            _now += 1000; _client.RecoverActivityReward();
            Assert.That(LastRequest, Is.EqualTo(original));
            Assert.That(Claims.Count, Is.EqualTo(2));
        }

        [Test]
        public void BoundedAutomaticRecoveryUsesOriginalRequestAndHonorsBackoff()
        {
            Load(); Claim(); var original = LastRequest.Clone();
            Claims[0].FailWith("rpc timeout");
            _client.TickActivityRewardRecovery();
            Assert.That(Claims.Count, Is.EqualTo(1));
            for (int attempt = 0; attempt < 3; ++attempt)
            {
                Later(); _client.TickActivityRewardRecovery();
                Assert.That(LastRequest, Is.EqualTo(original));
                Claims[Claims.Count - 1].FailWith("rpc timeout");
            }
            Later(); _client.TickActivityRewardRecovery();
            Assert.That(Claims.Count, Is.EqualTo(4));
            Assert.That(_client.HasPendingActivityReward, Is.True);
            _client.RecoverActivityReward();
            Assert.That(Claims.Count, Is.EqualTo(5), "自动次数耗尽仍可由玩家恢复原意图。");
        }

        [Test]
        public void CorruptRecordFailsClosedWithoutDeletingOrReplacingIt()
        {
            _store.Save(Player, "not-protobuf");
            Load(); Claim(); _client.TickActivityRewardRecovery();
            Assert.That(Claims, Is.Empty);
            Assert.That(_client.HasPendingActivityReward, Is.True);
            Assert.That(_store.Load(Player), Is.EqualTo("not-protobuf"));
            Assert.That(_client.ActivityRewardError, Is.Not.Empty);
        }

        [Test]
        public void BusinessRejectionClearsUncommittedIntentAndPermitsAnotherIntent()
        {
            Load(); Claim(); var original = LastRequest.Clone();
            var response = Receipt(original, claimed: false, durable: false);
            response.ErrorMessage = new TipInfoMessage { Id = (uint)common_error.KFeatureUnavailable };
            Claims[0].Respond(response);
            Assert.That(_claimed, Is.Empty);
            Assert.That(_store.Load(Player), Is.Empty);
            Assert.That(_client.HasPendingActivityReward, Is.False);
            Later();
            Claim();
            Assert.That(Claims.Count, Is.EqualTo(2));
            Assert.That(LastRequest.RequestId, Is.Not.EqualTo(original.RequestId));
        }

        [Test]
        public void FaultTipAndEnvelopeBusinessErrorDoNotProveNoAssetCommit()
        {
            Load(); Claim(); var original = LastRequest.Clone();
            var response = Receipt(original, claimed: false, durable: false);
            response.ErrorMessage = new TipInfoMessage { Id = (uint)common_error.KServiceUnavailable };
            Assert.That(TipFaults.IsFault(response.ErrorMessage.Id), Is.True, "夹具必须选择正式fault码。");
            Claims[0].Respond(response);
            Assert.That(Decode(_store.Load(Player)), Is.EqualTo(original));
            Later(); _client.RecoverActivityReward();
            Claims[1].FailWith("server tip=" + (uint)common_error.KFeatureUnavailable);
            Assert.That(Decode(_store.Load(Player)), Is.EqualTo(original));
            Assert.That(_claimed, Is.Empty);
        }

        [Test]
        public void ManualRecoveryRespectsMinimumIntervalAfterUnknownResult()
        {
            Load(); Claim(); var original = LastRequest.Clone();
            Claims[0].FailWith("rpc timeout");
            _now += 1999;
            _client.RecoverActivityReward();
            Assert.That(Claims.Count, Is.EqualTo(1));
            ++_now;
            _client.RecoverActivityReward();
            Assert.That(Claims.Count, Is.EqualTo(2));
            Assert.That(LastRequest, Is.EqualTo(original));
        }

        [Test]
        public void FailedLocalClearRetainsIntentUntilSameDurableReceiptCanBeReplayed()
        {
            Load(); Claim(); var original = LastRequest.Clone();
            _store.Transition(Player, _store.Load(Player), "externally-replaced-record");
            Claims[0].Respond(Receipt(original));
            Assert.That(_claimed, Is.Empty);
            Assert.That(_client.HasPendingActivityReward, Is.True);
            Assert.That(_store.Load(Player), Is.EqualTo("externally-replaced-record"));
        }

        [Test]
        public void OlderDirectoryReadCannotOverwriteClaimResponseSnapshot()
        {
            Load();
            _client.RequestActivities();
            var reads = _net.CallsOf(SceneActivityClientPlayerRpc.GetActivityListMessageId);
            var oldRead = reads[reads.Count - 1];
            Claim(); var response = Receipt(LastRequest, durable: false);
            response.Snapshot.Points = 52;
            Claims[0].Respond(response);
            var stale = Snapshot(); stale.Points = 0;
            oldRead.Respond(new GetActivityListResponse { Vitality = stale });
            Assert.That(_client.Activities.Vitality.Points, Is.EqualTo(52));
        }

        [Test]
        public void ReentrantCharacterSwitchBeforeSendNeverSubmitsForWrongCharacter()
        {
            Load();
            bool switched = false;
            _client.OnChanged += () =>
            {
                if (switched || !_client.BusyActivityReward) return;
                switched = true;
                _net.PlayerId = Player + 1;
                _client.RequestActivities();
            };
            Claim();
            Assert.That(switched, Is.True);
            Assert.That(Claims, Is.Empty);
            Assert.That(_store.Load(Player), Is.Not.Empty, "原玩家意图仍可恢复，不得删除。");
            Assert.That(_store.Load(Player + 1), Is.Empty);
            Assert.That(_client.SnapshotPlayerId, Is.EqualTo(Player + 1));
        }

        [Test]
        public void GatewayOriginsAndCharactersHaveSeparateRecoverySlots()
        {
            var firstOrigin = DurableRequestFileStore.ForOrigin(_directory, "https://first.fixture.test/");
            var secondOrigin = DurableRequestFileStore.ForOrigin(_directory, "https://second.fixture.test");
            firstOrigin.Save(Player, "original-first");
            secondOrigin.Save(Player, "original-second");
            firstOrigin.Save(Player + 1, "original-other-character");
            Assert.That(firstOrigin.Load(Player), Is.EqualTo("original-first"));
            Assert.That(secondOrigin.Load(Player), Is.EqualTo("original-second"));
            Assert.That(firstOrigin.Load(Player + 1), Is.EqualTo("original-other-character"));
            Assert.That(secondOrigin.Load(Player + 1), Is.Empty);
        }

        [Test]
        public void ConcurrentClientCannotOverwriteIntentCreatedAfterItsDirectoryRead()
        {
            Load();
            using var second = new PlayerFeaturesClient(_net, () => _connection)
                { ActivityRewardRecovery = new DurableRequestFileStore(_directory), NowMs = () => _now };
            second.RequestActivities();
            var reads = _net.CallsOf(SceneActivityClientPlayerRpc.GetActivityListMessageId);
            reads[reads.Count - 1].Respond(new GetActivityListResponse { Vitality = Snapshot() });
            Claim(); var original = LastRequest.Clone();
            second.ClaimActivityVitalityReward(Rule, Period, Threshold, Revision);
            Assert.That(Claims.Count, Is.EqualTo(1), "另一实例的保存冲突不能发送新意图。");
            Assert.That(Decode(_store.Load(Player)), Is.EqualTo(original));
            Assert.That(second.HasPendingActivityReward, Is.True);
            second.RecoverActivityReward();
            Assert.That(Claims.Count, Is.EqualTo(2));
            Assert.That(LastRequest, Is.EqualTo(original));
        }

        [Test]
        public void BusinessRejectionCannotImmediatelyMintAnotherRequestInsideSendInterval()
        {
            Load(); Claim(); var original = LastRequest.Clone();
            var response = Receipt(original, claimed: false, durable: false);
            response.ErrorMessage = new TipInfoMessage { Id = (uint)common_error.KFeatureUnavailable };
            Claims[0].Respond(response);
            Claim();
            Assert.That(Claims.Count, Is.EqualTo(1));
            Assert.That(_store.Load(Player), Is.Empty, "限流本地拒绝不能新写没有发送的意图。");
            _now += 999; Claim();
            Assert.That(Claims.Count, Is.EqualTo(1));
            ++_now; Claim();
            Assert.That(Claims.Count, Is.EqualTo(2));
            Assert.That(LastRequest.RequestId, Is.Not.EqualTo(original.RequestId));
        }

        [Test]
        public void DifferentThresholdCannotBypassSharedSendInterval()
        {
            Load();
            _client.Activities.Vitality.Rewards.Add(new PlayerActivityVitalityReward
                { ThresholdId = Threshold + 1, RequiredPoints = 42, RewardId = 6, CanClaim = true });
            Claim(); var original = LastRequest.Clone();
            var response = Receipt(original, claimed: false, durable: false);
            response.Snapshot.Rewards.Add(new PlayerActivityVitalityReward
                { ThresholdId = Threshold + 1, RequiredPoints = 42, RewardId = 6, CanClaim = true });
            response.ErrorMessage = new TipInfoMessage { Id = (uint)common_error.KFeatureUnavailable };
            Claims[0].Respond(response);
            _client.ClaimActivityVitalityReward(Rule, Period, Threshold + 1, Revision);
            Assert.That(Claims.Count, Is.EqualTo(1));
            Assert.That(_store.Load(Player), Is.Empty);
            _now += 1000;
            _client.ClaimActivityVitalityReward(Rule, Period, Threshold + 1, Revision);
            Assert.That(Claims.Count, Is.EqualTo(2));
            Assert.That(LastRequest.ThresholdId, Is.EqualTo(Threshold + 1));
        }

        [Test]
        public void CharacterSwitchDoesNotResetGlobalRewardSendInterval()
        {
            Load(); Claim(); var original = LastRequest.Clone();
            var response = Receipt(original, claimed: false, durable: false);
            response.ErrorMessage = new TipInfoMessage { Id = (uint)common_error.KFeatureUnavailable };
            Claims[0].Respond(response);
            _net.PlayerId = Player + 1; Load();
            Claim();
            Assert.That(Claims.Count, Is.EqualTo(1));
            Assert.That(_store.Load(Player + 1), Is.Empty);
            _now += 1000; Claim();
            Assert.That(Claims.Count, Is.EqualTo(2));
            Assert.That(_store.Load(Player + 1), Is.Not.Empty);
        }

        private static BagInfo Inventory(uint type, ulong itemId) => new()
        {
            Layout = new BagLayoutInfo { BagType = type, Capacity = 20 },
            Items = { new BagItemInfo { ItemId = itemId, ConfigId = 1, Count = 1 } },
        };

        [Test]
        public void RewardRefreshesActualInventoryWithoutSwitchingSelectedWarehouse()
        {
            _client.RequestBag(0);
            _net.Calls[_net.Calls.Count - 1].Respond(new GetBagResponse { Bag = Inventory(0, 11) });
            _client.RequestBag(1);
            _net.Calls[_net.Calls.Count - 1].Respond(new GetBagResponse { Bag = Inventory(1, 21) });
            Load(); Claim(); Claims[0].Respond(Receipt(LastRequest));
            Assert.That(_client.RequestedBagType, Is.EqualTo(1));
            Assert.That(_client.CharacterBag, Is.Null, "实际发放包未同步时不能保留旧库存当作新快照。");
            var reads = _net.CallsOf(SceneBagClientPlayerRpc.GetBagMessageId);
            var selectedRead = reads[reads.Count - 2]; var inventoryRead = reads[reads.Count - 1];
            Assert.That(((GetBagRequest)selectedRead.Request).BagType, Is.EqualTo(1));
            Assert.That(((GetBagRequest)inventoryRead.Request).BagType, Is.EqualTo(0));
            inventoryRead.Respond(new GetBagResponse { Bag = Inventory(0, 12) });
            selectedRead.Respond(new GetBagResponse { Bag = Inventory(1, 22) });
            Assert.That(_client.CharacterBag.Items[0].ItemId, Is.EqualTo(12));
            Assert.That(_client.Bag.Items[0].ItemId, Is.EqualTo(22));
            Assert.That(_client.RequestedBagType, Is.EqualTo(1));
        }

        [Test]
        public void LateRewardInventoryRefreshCannotOverwriteNewerInventoryReadOrAnotherRole()
        {
            _client.RequestBag(1);
            _net.Calls[_net.Calls.Count - 1].Respond(new GetBagResponse { Bag = Inventory(1, 21) });
            Load(); Claim(); Claims[0].Respond(Receipt(LastRequest));
            var reads = _net.CallsOf(SceneBagClientPlayerRpc.GetBagMessageId);
            var oldRead = reads[reads.Count - 1];
            _client.RequestBag(0);
            _net.Calls[_net.Calls.Count - 1].Respond(new GetBagResponse { Bag = Inventory(0, 99) });
            oldRead.Respond(new GetBagResponse { Bag = Inventory(0, 12) });
            Assert.That(_client.CharacterBag.Items[0].ItemId, Is.EqualTo(99));
            _net.PlayerId = Player + 1; _client.RequestActivities();
            oldRead.Respond(new GetBagResponse { Bag = Inventory(0, 13) });
            Assert.That(_client.CharacterBag, Is.Null);
        }

        [Test]
        public void LateRewardInventoryReadCannotOverwriteSuccessfulUnequipWhileViewingWarehouse()
        {
            _client.RequestBag(1);
            _net.Calls[_net.Calls.Count - 1].Respond(new GetBagResponse { Bag = Inventory(1, 21) });
            Load(); Claim(); Claims[0].Respond(Receipt(LastRequest));
            var reads = _net.CallsOf(SceneBagClientPlayerRpc.GetBagMessageId);
            var oldRead = reads[reads.Count - 1];
            _client.UnequipItem(42);
            var actions = _net.CallsOf(MessageIds.UnequipItem);
            Assert.That(actions.Count, Is.EqualTo(1));
            actions[0].Respond(new UnequipItemResponse { Bag = Inventory(0, 99), Equipment = Inventory(2, 100) });
            Assert.That(_client.BusyEquip, Is.False);
            oldRead.Respond(new GetBagResponse { Bag = Inventory(0, 12) });
            Assert.That(_client.CharacterBag.Items[0].ItemId, Is.EqualTo(99));
            Assert.That(_client.RequestedBagType, Is.EqualTo(1));
            Assert.That(_client.Bag.Items[0].ItemId, Is.EqualTo(21));
        }

        [Test]
        public void LateRewardInventoryReadCannotRestoreStaleInventoryAfterUnknownMissionClaim()
        {
            _client.RequestMissions();
            _net.Calls[_net.Calls.Count - 1].Respond(new GetMissionListResponse
            {
                Missions = { new PlayerMissionInfo { Scope = 0, MissionId = 15, CanClaim = true } },
            });
            _client.RequestBag(1);
            _net.Calls[_net.Calls.Count - 1].Respond(new GetBagResponse { Bag = Inventory(1, 21) });
            Load(); Claim(); Claims[0].Respond(Receipt(LastRequest));
            var reads = _net.CallsOf(SceneBagClientPlayerRpc.GetBagMessageId);
            var oldRead = reads[reads.Count - 1];
            _client.ClaimMissionReward(0, 15);
            var actions = _net.CallsOf(SceneMissionClientPlayerRpc.ClaimMissionRewardMessageId);
            Assert.That(actions.Count, Is.EqualTo(1));
            actions[0].FailWith("rpc timeout");
            Assert.That(_client.BusyMissionAction, Is.False);
            oldRead.Respond(new GetBagResponse { Bag = Inventory(0, 12) });
            Assert.That(_client.CharacterBag, Is.Null, "新资产操作结果未知时，不能恢复此前发出的旧库存读取。");
            Assert.That(_client.RequestedBagType, Is.EqualTo(1));
            Assert.That(_client.HasPendingActivityReward, Is.False);
            CollectionAssert.AreEqual(new[] { Threshold }, _claimed);
        }

        [Test]
        public void RewardInventoryReadFailureKeepsDurableClaimConfirmedAndInventoryUnknown()
        {
            _client.RequestBag(1);
            _net.Calls[_net.Calls.Count - 1].Respond(new GetBagResponse { Bag = Inventory(1, 21) });
            Load(); Claim(); Claims[0].Respond(Receipt(LastRequest));
            var reads = _net.CallsOf(SceneBagClientPlayerRpc.GetBagMessageId);
            reads[reads.Count - 1].FailWith("rpc timeout");
            Assert.That(_client.CharacterBag, Is.Null);
            Assert.That(_client.HasPendingActivityReward, Is.False);
            CollectionAssert.AreEqual(new[] { Threshold }, _claimed);
            Assert.That(_client.ActivityRewardError, Does.Contain("人物背包"));
            Assert.That(_store.Load(Player), Is.Empty);
        }
    }
}
