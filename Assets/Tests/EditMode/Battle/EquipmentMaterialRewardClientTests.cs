using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Google.Protobuf;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class EquipmentMaterialRewardClientTests
    {
        private const ulong Player = 8107;
        private string _directory;
        private DurableRequestFileStore _store, _craftStore, _activityStore;
        private FakeBattleTransport _net;
        private PlayerFeaturesClient _client;
        private object _connection;
        private long _now;
        private readonly List<ClaimEquipmentMaterialRewardResponse> _claimed = new();

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "equipment-material-test-" + Guid.NewGuid().ToString("N"));
            _store = new DurableRequestFileStore(Path.Combine(_directory, "material"));
            _craftStore = new DurableRequestFileStore(Path.Combine(_directory, "craft"));
            _activityStore = new DurableRequestFileStore(Path.Combine(_directory, "activity"));
            _net = new FakeBattleTransport { IsReady = true, PlayerId = Player };
            _connection = new object(); _now = 10000; _claimed.Clear();
            CreateClient();
        }

        private void CreateClient()
        {
            _client = new PlayerFeaturesClient(_net, () => _connection)
            {
                MaterialRewardRecovery = _store, EquipmentCraftRecovery = _craftStore,
                ActivityRewardRecovery = _activityStore, NowMs = () => _now,
            };
            _client.OnMaterialRewardClaimed += _claimed.Add;
        }

        [TearDown]
        public void TearDown()
        {
            _client.Dispose();
            if (!Directory.Exists(_directory)) return;
            Assert.That(Path.GetDirectoryName(Path.GetFullPath(_directory)), Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)));
            Assert.That(Path.GetFileName(_directory), Does.StartWith("equipment-material-test-"));
            Directory.Delete(_directory, true);
        }

        private FakeBattleTransport.RecordedCall Last => _net.Calls.Last();
        private List<FakeBattleTransport.RecordedCall> Claims => _net.CallsOf(SceneBagClientPlayerRpc.ClaimEquipmentMaterialRewardMessageId);
        private ClaimEquipmentMaterialRewardRequest Request => (ClaimEquipmentMaterialRewardRequest)Claims.Last().Request;
        private static EquipmentMaterialRewardSnapshot Snapshot(ulong revision = 7, ulong balance = 35) => new()
        {
            Available = true, CanClaim = balance != 0, Revision = revision, AvailableUnits = balance,
            BatchUnits = (uint)Math.Min(20UL, balance), EarnedToday = 20, DailyLimit = 20,
            DayStartsAtMs = 1000, NextResetAtMs = 86401000,
            CraftingMaterial = new EquipmentCraftMaterial { ConfigId = 3001, Name = "打造材料", Owned = 5 },
            EnhancementStone = new EquipmentCraftMaterial { ConfigId = 3002, Name = "百炼石", Owned = 6 },
        };
        private static BagInfo Bag(uint type = 0, uint count = 15)
        {
            var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = type, Capacity = 30, CanSort = type == 0 } };
            bag.Items.Add(new BagItemInfo { ItemId = 101, ConfigId = 3001, Count = count });
            return bag;
        }
        private void Load(EquipmentMaterialRewardSnapshot snapshot = null)
        {
            _client.RequestEquipmentMaterialReward();
            Last.Respond(new GetEquipmentMaterialRewardResponse { Snapshot = snapshot ?? Snapshot() });
        }
        private void Claim(uint craft = 12, uint stone = 8) { Load(); _client.ClaimEquipmentMaterialReward(craft, stone); }
        private ClaimEquipmentMaterialRewardResponse Receipt(bool durable = true) => new()
        {
            ClientRequestId = Request.ClientRequestId, Outcome = ClaimEquipmentMaterialRewardResponse.Types.Outcome.Applied,
            Durable = durable, ResultRevision = Request.ExpectedRevision + 1, CurrentRevision = Request.ExpectedRevision + 1,
            Snapshot = Snapshot(Request.ExpectedRevision + 1, 15), Bag = Bag(),
        };
        private void Later() => _now += 20000;

        [Test]
        public void SnapshotKeepsServerBalanceBatchAndNamesWithoutInventingDailyAllowance()
        {
            var snapshot = Snapshot(91, 1000); snapshot.EarnedToday = 10;
            Load(snapshot);
            Assert.That(_client.EquipmentMaterialReward, Is.EqualTo(snapshot));
            _client.EquipmentMaterialReward.AvailableUnits = 0;
            Assert.That(_client.EquipmentMaterialReward.AvailableUnits, Is.EqualTo(1000));
            Assert.That(_client.EquipmentMaterialReward.BatchUnits, Is.EqualTo(20));
        }

        [TestCase(20u, 0u)]
        [TestCase(0u, 20u)]
        [TestCase(12u, 8u)]
        public void CompleteAllocationIsOnDiskBeforeSendAndNotAppliedLocally(uint craft, uint stone)
        {
            _client.RequestBag(); Last.Respond(new GetBagResponse { Bag = Bag(0, 5) });
            var before = _client.CharacterBag.Clone(); Load();
            bool observed = false;
            _client.OnChanged += () =>
            {
                if (!_client.BusyMaterialReward) return;
                observed = true;
                Assert.That(_store.Load(Player), Is.Not.Empty);
                Assert.That(Claims, Is.Empty);
            };
            _client.ClaimEquipmentMaterialReward(craft, stone);
            _client.ClaimEquipmentMaterialReward(stone, craft);
            Assert.That(observed, Is.True);
            Assert.That(Claims.Count, Is.EqualTo(1));
            Assert.That(Request.ClientRequestId, Is.Not.Zero);
            Assert.That(Request.ExpectedRevision, Is.EqualTo(7));
            Assert.That(Request.CraftingMaterialCount, Is.EqualTo(craft));
            Assert.That(Request.EnhancementStoneCount, Is.EqualTo(stone));
            Assert.That(ClaimEquipmentMaterialRewardRequest.Parser.ParseFrom(Convert.FromBase64String(_store.Load(Player))), Is.EqualTo(Request));
            Assert.That(_client.CharacterBag, Is.EqualTo(before));
        }

        [TestCase(0u, 0u)]
        [TestCase(10u, 9u)]
        [TestCase(10u, 11u)]
        [TestCase(uint.MaxValue, 21u)]
        public void InvalidOrOverflowingAllocationDoesNotCreateAnIntent(uint craft, uint stone)
        {
            Load(); _client.ClaimEquipmentMaterialReward(craft, stone);
            Assert.That(Claims, Is.Empty); Assert.That(_store.Load(Player), Is.Empty);
        }

        [Test]
        public void SmallerServerBatchAndUnavailableOfferAreAuthoritative()
        {
            var snapshot = Snapshot(0, 6); snapshot.CanClaim = false;
            Load(snapshot); _client.ClaimEquipmentMaterialReward(3, 3);
            Assert.That(Claims, Is.Empty);
            snapshot.CanClaim = true; Load(snapshot); _client.ClaimEquipmentMaterialReward(3, 3);
            Assert.That(Request.ExpectedRevision, Is.Zero);
            Assert.That(Request.CraftingMaterialCount + Request.EnhancementStoneCount, Is.EqualTo(6));
        }

        [Test]
        public void MissingOrUnwritableRecoveryStoreNeverSends()
        {
            Load(); _client.MaterialRewardRecovery = null; _client.ClaimEquipmentMaterialReward(12, 8);
            Assert.That(Claims, Is.Empty);
            Directory.CreateDirectory(_directory);
            string blocked = Path.Combine(_directory, "not-directory"); File.WriteAllText(blocked, "fixture");
            _client.MaterialRewardRecovery = new DurableRequestFileStore(blocked); _client.ClaimEquipmentMaterialReward(12, 8);
            Assert.That(Claims, Is.Empty);
        }

        [Test]
        public void UnknownOutcomeCannotReplaceAllocationOrMintAnotherKey()
        {
            Claim(); var original = Request.Clone();
            Claims.Last().Respond(new ClaimEquipmentMaterialRewardResponse
            { ClientRequestId = Request.ClientRequestId, ErrorMessage = new TipInfoMessage { Id = 1 } });
            Load(Snapshot(99, 200)); _client.ClaimEquipmentMaterialReward(1, 19);
            Assert.That(Claims.Count, Is.EqualTo(1)); Assert.That(_client.HasPendingMaterialReward, Is.True);
            Later(); _client.RetryPendingMaterialReward();
            Assert.That(Request, Is.EqualTo(original)); Assert.That(_claimed, Is.Empty);
        }

        [Test]
        public void AccountQuotaSyncShowsFriendlyHintAndRetriesTheSameClaim()
        {
            Claim();
            var original = Request.Clone();
            string record = _store.Load(Player);
            var snapshot = Snapshot();
            snapshot.BatchTradableUnits = 1u;
            Claims.Last().Respond(new ClaimEquipmentMaterialRewardResponse
            {
                ClientRequestId = original.ClientRequestId,
                Outcome = ClaimEquipmentMaterialRewardResponse.Types.Outcome.Unknown,
                ErrorMessage = new TipInfoMessage { Id = (uint)common_error.KServiceUnavailable },
                Snapshot = snapshot,
            });

            Assert.That(_client.MaterialRewardError, Is.EqualTo("正在核验账号可交易额度，请稍后刷新再领"));
            Assert.That(_client.HasPendingMaterialReward, Is.True);
            Assert.That(_store.Load(Player), Is.EqualTo(record));
            Assert.That(_claimed, Is.Empty);

            Later();
            _client.RetryPendingMaterialReward();
            Assert.That(Request, Is.EqualTo(original));
            Claims.Last().Respond(Receipt());
            Assert.That(_client.MaterialRewardError, Is.Empty);
            Assert.That(_claimed, Has.Count.EqualTo(1));
        }

        [Test]
        public void AppliedWithoutDurabilityUpdatesWholeSnapshotButRetainsFrozenIntent()
        {
            Claim(); string record = _store.Load(Player); var response = Receipt(false);
            Claims.Last().Respond(response);
            Assert.That(_client.CharacterBag, Is.EqualTo(response.Bag));
            Assert.That(_client.EquipmentMaterialReward, Is.EqualTo(response.Snapshot));
            Assert.That(_store.Load(Player), Is.EqualTo(record)); Assert.That(_client.HasPendingMaterialReward, Is.True);
            Assert.That(_claimed, Is.Empty);
            Later(); _client.RetryPendingMaterialReward(); Claims.Last().Respond(Receipt());
            Assert.That(_claimed.Count, Is.EqualTo(1)); Assert.That(_store.Load(Player), Is.Empty);
        }

        [TestCase("key")]
        [TestCase("revision")]
        [TestCase("current")]
        [TestCase("snapshot")]
        [TestCase("bag")]
        [TestCase("tip")]
        public void MalformedSuccessDoesNotClearOrAnnounce(string defect)
        {
            Claim(); var response = Receipt();
            switch (defect)
            {
                case "key": ++response.ClientRequestId; break;
                case "revision": ++response.ResultRevision; break;
                case "current": response.CurrentRevision = 0; break;
                case "snapshot": response.Snapshot.Revision = 1; break;
                case "bag": response.Bag = Bag(1); break;
                case "tip": response.ErrorMessage = new TipInfoMessage { Id = 1 }; break;
            }
            Claims.Last().Respond(response);
            Assert.That(_client.HasPendingMaterialReward, Is.True); Assert.That(_store.Load(Player), Is.Not.Empty);
            Assert.That(_claimed, Is.Empty); Assert.That(_client.CharacterBag, Is.Null);
        }

        [Test]
        public void ExplicitRejectionClearsOnlyOriginalIntentAndHonorsSendInterval()
        {
            Claim(); Claims.Last().Respond(new ClaimEquipmentMaterialRewardResponse
            { ClientRequestId = Request.ClientRequestId, Outcome = ClaimEquipmentMaterialRewardResponse.Types.Outcome.Rejected, Snapshot = Snapshot() });
            Assert.That(_store.Load(Player), Is.Empty); Assert.That(_client.HasPendingMaterialReward, Is.False);
            _client.ClaimEquipmentMaterialReward(20, 0); Assert.That(Claims.Count, Is.EqualTo(1));
            Later(); _client.ClaimEquipmentMaterialReward(20, 0); Assert.That(Claims.Count, Is.EqualTo(2));
        }

        [Test]
        public void DurableSuccessIgnoresDuplicateReplyAndOlderOfferAndInventoryReads()
        {
            _client.RequestBag(); var oldBag = Last;
            Load(); _client.RequestEquipmentMaterialReward(); var oldOffer = Last;
            oldOffer.Respond(new GetEquipmentMaterialRewardResponse { Snapshot = Snapshot() });
            _client.ClaimEquipmentMaterialReward(12, 8); var call = Claims.Last(); var response = Receipt();
            call.Respond(response);
            oldBag.Respond(new GetBagResponse { Bag = Bag(0, 999) });
            oldOffer.Respond(new GetEquipmentMaterialRewardResponse { Snapshot = Snapshot(1, 999) });
            call.Respond(response);
            Assert.That(_client.CharacterBag, Is.EqualTo(response.Bag));
            Assert.That(_client.EquipmentMaterialReward, Is.EqualTo(response.Snapshot));
            Assert.That(_claimed.Count, Is.EqualTo(1));
        }

        [Test]
        public void ClaimUpdatesActualInventoryWithoutSwitchingSelectedWarehouse()
        {
            _client.RequestBag(1); Last.Respond(new GetBagResponse { Bag = Bag(1, 99) });
            Claim(); Claims.Last().Respond(Receipt());
            Assert.That(_client.RequestedBagType, Is.EqualTo(1)); Assert.That(_client.Bag.Items[0].Count, Is.EqualTo(99));
            Assert.That(_client.CharacterBag.Items[0].Count, Is.EqualTo(15));
        }

        [Test]
        public void ChangedDiskRecordCannotBeDeletedByOriginalReceipt()
        {
            Claim(); var replacement = Request.Clone(); ++replacement.ClientRequestId;
            string changed = Convert.ToBase64String(replacement.ToByteArray());
            _store.Transition(Player, _store.Load(Player), changed);
            Claims.Last().Respond(Receipt());
            Assert.That(_store.Load(Player), Is.EqualTo(changed)); Assert.That(_client.HasPendingMaterialReward, Is.True);
            Assert.That(_claimed, Is.Empty);
        }

        [Test]
        public void LocalClearFailureKeepsIntentUntilSameDurableResultCanBeRecovered()
        {
            Claim(); var original = Request.Clone();
            string guardPath = Path.Combine(_directory, "material", Player + ".pending.lock");
            using (var guard = new FileStream(guardPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                Claims.Last().Respond(Receipt());
            Assert.That(_client.HasPendingMaterialReward, Is.True); Assert.That(_claimed, Is.Empty);
            Later(); _client.RetryPendingMaterialReward(); Assert.That(Request, Is.EqualTo(original));
            Claims.Last().Respond(Receipt());
            Assert.That(_claimed.Count, Is.EqualTo(1)); Assert.That(_store.Load(Player), Is.Empty);
        }

        [Test]
        public void LateActivityInventoryRefreshCannotOverwriteMaterialClaimWhileViewingWarehouse()
        {
            _client.RequestBag(1); Last.Respond(new GetBagResponse { Bag = Bag(1, 99) });
            var activity = new PlayerActivityVitalitySnapshot
            { Available = true, RuleId = 1, PeriodId = 1, ConfigurationRevision = 1 };
            activity.Rewards.Add(new PlayerActivityVitalityReward { ThresholdId = 1, CanClaim = true });
            _client.RequestActivities(); Last.Respond(new GetActivityListResponse { Vitality = activity });
            _client.ClaimActivityVitalityReward(1, 1, 1, 1);
            var request = (ClaimActivityVitalityRewardRequest)Last.Request;
            Last.Respond(new ClaimActivityVitalityRewardResponse { RequestId = request.RequestId, Claimed = true, Durable = true, Snapshot = activity });
            var lateInventory = _net.CallsOf(SceneBagClientPlayerRpc.GetBagMessageId)
                .Last(call => ((GetBagRequest)call.Request).BagType == 0);
            Claim(); Claims.Last().Respond(Receipt());
            lateInventory.Respond(new GetBagResponse { Bag = Bag(0, 999) });
            Assert.That(_client.CharacterBag.Items[0].Count, Is.EqualTo(15));
            Assert.That(_client.RequestedBagType, Is.EqualTo(1));
        }

        [Test]
        public void CorruptRecordBlocksExistingAssetActionsBeforeRecoveryTick()
        {
            _store.Save(Player, "not-protobuf"); _client.EquipItem(101, 0);
            Assert.That(_client.HasPendingMaterialReward, Is.True); Assert.That(_net.Calls, Is.Empty);
            _client.RetryPendingMaterialReward(); Assert.That(Claims, Is.Empty);
            Assert.That(_store.Load(Player), Is.EqualTo("not-protobuf"));
        }

        [Test]
        public void PendingMaterialBlocksAllExistingAssetWritesAfterTimeout()
        {
            Claim(); Claims.Last().FailWith("timeout"); int calls = _net.Calls.Count;
            _client.EquipItem(101); _client.EquipItem(101, 0); _client.UnequipItem(101); _client.EnhanceEquip(101);
            _client.InlayGem(101, 0, 1); _client.RemoveGem(101, 0); _client.SortBag(); _client.RetrieveAllTemporaryItems();
            _client.AcceptMission(1, 1); _client.ClaimMissionReward(1, 1); _client.ClaimActivityVitalityReward(1, 1, 1, 1);
            _client.PreviewEquipmentCraft(3101); _client.CommitEquipmentCraft(null);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));
        }

        [Test]
        public void OtherAssetWriteInFlightPreventsNewMaterialIntent()
        {
            _client.RequestBag(); Last.Respond(new GetBagResponse { Bag = Bag() }); Load();
            _client.SortBag(); Assert.That(_client.BusySort, Is.True);
            _client.ClaimEquipmentMaterialReward(12, 8);
            Assert.That(Claims, Is.Empty); Assert.That(_store.Load(Player), Is.Empty);
        }

        [Test]
        public void RestartAndNewDayRecoverExactAllocationAndRevision()
        {
            Claim(); var original = Request.Clone(); Claims.Last().FailWith("timeout"); _client.Dispose(); CreateClient();
            // 跨日与新增额度不推进领取水位；余额变化也不能重写磁盘上的原分配。
            var nextDay = Snapshot(7, 1000);
            nextDay.DayStartsAtMs = 86401000; nextDay.NextResetAtMs = 172801000; nextDay.EarnedToday = 0;
            Load(nextDay); Later(); _client.RetryPendingMaterialReward();
            Assert.That(Request, Is.EqualTo(original));
            Assert.That(Request.ExpectedRevision, Is.EqualTo(7));
        }

        [Test]
        public void ReplacedConnectionRejectsOldReceiptAndResendsOriginalBusyIntent()
        {
            Claim(); var original = Request.Clone(); var stale = Claims.Last(); var response = Receipt();
            _connection = new object(); Later(); _client.TickMaterialRewardRecovery();
            Assert.That(Claims.Count, Is.EqualTo(2)); Assert.That(Request, Is.EqualTo(original));
            stale.Respond(response); Assert.That(_client.BusyMaterialReward, Is.True); Assert.That(_claimed, Is.Empty);
            Claims.Last().Respond(response); Assert.That(_claimed.Count, Is.EqualTo(1));
        }

        [Test]
        public void CharacterSwitchKeepsDiskIntentWithoutApplyingOldPlayersAssets()
        {
            Claim(); var stale = Claims.Last(); var response = Receipt();
            _net.PlayerId = Player + 1; _client.TickMaterialRewardRecovery(); stale.Respond(response);
            Assert.That(_client.CharacterBag, Is.Null); Assert.That(_claimed, Is.Empty);
            Assert.That(_store.Load(Player), Is.Not.Empty); Assert.That(_store.Load(Player + 1), Is.Empty);
            Assert.That(_client.HasPendingMaterialReward, Is.False);
        }

        [Test]
        public void AutomaticRecoveryIsBoundedAndExplicitRecoveryKeepsOriginalKey()
        {
            Claim(); var original = Request.Clone(); Claims.Last().FailWith("timeout");
            _client.TickMaterialRewardRecovery(); Assert.That(Claims.Count, Is.EqualTo(1));
            for (int i = 0; i < 3; i++)
            {
                Later(); _client.TickMaterialRewardRecovery(); Assert.That(Request, Is.EqualTo(original));
                Claims.Last().FailWith("timeout");
            }
            Later(); _client.TickMaterialRewardRecovery(); Assert.That(Claims.Count, Is.EqualTo(4));
            _client.RetryPendingMaterialReward(); Assert.That(Claims.Count, Is.EqualTo(5)); Assert.That(Request, Is.EqualTo(original));
        }

        [Test]
        public void ConcurrentWriterCannotReplaceOriginalIntentAfterSnapshotWasRead()
        {
            Load(); var other = new ClaimEquipmentMaterialRewardRequest
            { ClientRequestId = 66, ExpectedRevision = 2, CraftingMaterialCount = 3, EnhancementStoneCount = 4 };
            string record = Convert.ToBase64String(other.ToByteArray()); _store.Save(Player, record);
            _client.ClaimEquipmentMaterialReward(12, 8);
            Assert.That(Claims, Is.Empty); Assert.That(_client.HasPendingMaterialReward, Is.True);
            Later(); _client.RetryPendingMaterialReward(); Assert.That(Request, Is.EqualTo(other));
            Assert.That(_store.Load(Player), Is.EqualTo(record));
        }

        [Test]
        public void MultiplePendingDomainsRecoverInOneDirectionWithoutDeadlock()
        {
            var material = new ClaimEquipmentMaterialRewardRequest { ClientRequestId = 65, ExpectedRevision = 7, CraftingMaterialCount = 12, EnhancementStoneCount = 8 };
            var craft = new CommitEquipmentCraftRequest
            {
                ClientRequestId = 66, RulesFingerprint = ByteString.CopyFromUtf8("rules"),
                Choice = new EquipmentCraftChoice { Operation = EquipmentCraftChoice.Types.Operation.CreateItem, ConfigId = 3101 },
            };
            var activity = new ClaimActivityVitalityRewardRequest
            { RuleId = 1, PeriodId = 1, ThresholdId = 1, ConfigurationRevision = 1, RequestId = Guid.NewGuid().ToString("D") };
            _store.Save(Player, Convert.ToBase64String(material.ToByteArray()));
            _craftStore.Save(Player, Convert.ToBase64String(craft.ToByteArray()));
            _activityStore.Save(Player, Convert.ToBase64String(activity.ToByteArray()));
            _client.TickActivityRewardRecovery(); _client.TickMaterialRewardRecovery(); Assert.That(_net.Calls, Is.Empty);
            _client.TickEquipmentCraftRecovery();
            Assert.That(Last.Request, Is.TypeOf<CommitEquipmentCraftRequest>());
            Last.Respond(new CommitEquipmentCraftResponse { ClientRequestId = 66, Outcome = CommitEquipmentCraftResponse.Types.Outcome.Rejected });
            _client.TickMaterialRewardRecovery(); Assert.That(Request, Is.EqualTo(material));
            Claims.Last().Respond(Receipt());
            _client.TickActivityRewardRecovery(); Assert.That(Last.Request, Is.EqualTo(activity));
        }

        [Test]
        public void ReentrantRoleChangeBeforeSendAndAfterSuccessCannotLeakOldAction()
        {
            Load(); bool changeBefore = true;
            _client.OnChanged += () =>
            {
                if (!changeBefore || !_client.BusyMaterialReward) return;
                changeBefore = false; _net.PlayerId = Player + 1; _client.RequestBag();
            };
            _client.ClaimEquipmentMaterialReward(12, 8);
            Assert.That(Claims, Is.Empty); Assert.That(_store.Load(Player), Is.Not.Empty);
            _net.PlayerId = Player; Later(); _client.TickMaterialRewardRecovery();
            _client.OnChanged += () =>
            {
                if (_client.HasPendingMaterialReward || _net.PlayerId != Player) return;
                _net.PlayerId = Player + 1; _client.RequestBag();
            };
            Claims.Last().Respond(Receipt());
            Assert.That(_claimed, Is.Empty); Assert.That(_store.Load(Player), Is.Empty);
        }
    }
}
