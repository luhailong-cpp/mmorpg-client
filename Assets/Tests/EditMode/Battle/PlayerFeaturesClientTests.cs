using System.Collections.Generic;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class PlayerFeaturesClientTests
    {
        private FakeBattleTransport _net;
        private PlayerFeaturesClient _client;
        private List<string> _errors;
        private int _changes;

        [SetUp]
        public void SetUp()
        {
            _net = new FakeBattleTransport { PlayerId = 9101, IsReady = true };
            _client = new PlayerFeaturesClient(_net);
            _errors = new List<string>();
            _client.OnError += _errors.Add;
            _client.OnChanged += () => ++_changes;
        }

        [TearDown]
        public void TearDown() => _client.Dispose();

        private static BagInfo Bag(uint type = 0, ulong itemId = 5001, uint count = 7)
        {
            var bag = new BagInfo
            {
                Layout = new BagLayoutInfo { BagType = type, Capacity = 40, CanSort = type < 2 },
                Currency = new CurrencyComp(),
            };
            bag.Items.Add(new BagItemInfo { ItemId = itemId, ConfigId = 101, Count = count, MaxStack = 99 });
            bag.Layout.Slots.Add(new BagSlotInfo { Slot = 9, ItemId = itemId, Width = 1, Height = 1 });
            bag.Currency.Values.Add(new ulong[] { 123, 45, 6 });
            return bag;
        }

        private void LoadBag(uint type = 0)
        {
            _client.RequestBag(type);
            _net.Calls[_net.Calls.Count - 1].Respond(new GetBagResponse { Bag = Bag(type) });
        }

        [Test]
        public void OpeningBagOnlyReadsAndPreservesInstanceSlotAndCurrencyData()
        {
            _client.RequestBag();
            Assert.That(_client.BagLoading, Is.True);
            Assert.That(_client.HasBag, Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
            Assert.That(_net.Calls[0].MessageId, Is.EqualTo(SceneBagClientPlayerGetBagHandler.MessageId));
            Assert.That(((GetBagRequest)_net.Calls[0].Request).BagType, Is.Zero);
            _net.Calls[0].Respond(new GetBagResponse { Bag = Bag() });

            Assert.That(_client.BagLoading, Is.False);
            Assert.That(_client.Bag.Items[0].ItemId, Is.EqualTo(5001));
            Assert.That(_client.Bag.Items[0].Name, Is.Empty, "Missing item names must not be fabricated.");
            Assert.That(_client.Bag.Layout.Slots[0].Slot, Is.EqualTo(9));
            Assert.That(_client.Bag.Layout.Slots[0].ItemId, Is.EqualTo(_client.Bag.Items[0].ItemId));
            CollectionAssert.AreEqual(new ulong[] { 123, 45, 6 }, _client.Bag.Currency.Values);
            Assert.That(_net.CallsOf(SceneBagClientPlayerSortBagHandler.MessageId), Is.Empty);
        }

        [Test]
        public void EmptyBagAndEmptyListsAreLoadedStates()
        {
            _client.RequestBag();
            _client.RequestMissions();
            _client.RequestActivities();
            _net.Calls[0].Respond(new GetBagResponse { Bag = new BagInfo { Layout = new BagLayoutInfo { Capacity = 40 } } });
            _net.Calls[1].Respond(new GetMissionListResponse());
            _net.Calls[2].Respond(new GetActivityListResponse());

            Assert.That(_client.HasBag && _client.HasMissions && _client.HasActivities, Is.True);
            Assert.That(_client.Bag.Items, Is.Empty);
            Assert.That(_client.Missions.Missions, Is.Empty);
            Assert.That(_client.Activities.Activities, Is.Empty);
            Assert.That(_client.BagLoading || _client.MissionsLoading || _client.ActivitiesLoading, Is.False);
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void SwitchingBagDoesNotDisplayPreviousTypeAndOlderResponseCannotReplaceIt()
        {
            LoadBag();
            _client.RequestBag(0);
            var older = _net.Calls[1];
            _client.RequestBag(1);
            Assert.That(_client.HasBag, Is.False);
            Assert.That(_client.RequestedBagType, Is.EqualTo(1));

            _net.Calls[2].Respond(new GetBagResponse { Bag = Bag(1, 7001) });
            older.Respond(new GetBagResponse { Bag = Bag(0, 8001) });
            Assert.That(_client.Bag.Layout.BagType, Is.EqualTo(1));
            Assert.That(_client.Bag.Items[0].ItemId, Is.EqualTo(7001));
            Assert.That(_client.BagLoading, Is.False);
        }

        [Test]
        public void WrongBagTypeResponseIsRejectedAndKeepsLastGoodSnapshot()
        {
            LoadBag();
            var previous = _client.Bag;
            _client.RequestBag();
            _net.Calls[1].Respond(new GetBagResponse { Bag = Bag(1) });
            Assert.That(_client.Bag, Is.SameAs(previous));
            Assert.That(_client.BagLoading, Is.False);
            Assert.That(_client.BagError, Is.Not.Empty);
        }

        [Test]
        public void MissingBagPayloadIsNotReportedAsAnEmptyBag()
        {
            _client.RequestBag();
            _net.Calls[0].Respond(new GetBagResponse());
            Assert.That(_client.HasBag, Is.False);
            Assert.That(_client.BagLoading, Is.False);
            Assert.That(_errors.Count, Is.EqualTo(1));
        }

        [Test]
        public void BodyRejectionPreservesSnapshotAndRetryClearsError()
        {
            LoadBag();
            var previous = _client.Bag;
            _client.RequestBag();
            _net.Calls[1].Respond(new GetBagResponse { ErrorMessage = new TipInfoMessage { Id = 5 }, Bag = Bag(0, 9999) });
            Assert.That(_client.Bag, Is.SameAs(previous));
            Assert.That(_client.BagError, Does.Contain("5"));
            Assert.That(_client.BagLoading, Is.False);
            _client.RequestBag();
            Assert.That(_client.BagError, Is.Empty);
            Assert.That(_client.BagLoading, Is.True);
        }

        [Test]
        public void SortIsExplicitSerialAndAppliesOnlyAuthoritativeResponse()
        {
            LoadBag();
            _client.SortBag();
            var sort = _net.Calls[1];
            Assert.That(sort.MessageId, Is.EqualTo(SceneBagClientPlayerSortBagHandler.MessageId));
            Assert.That(((SortBagRequest)sort.Request).BagType, Is.Zero);
            Assert.That(_client.BusySort, Is.True);
            Assert.That(_client.Bag.Layout.Slots[0].Slot, Is.EqualTo(9), "Do not predict the layout before the server reply.");

            _client.SortBag();
            _client.RequestBag(1);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(_client.RequestedBagType, Is.Zero);
            var sorted = Bag();
            sorted.Layout.Slots[0].Slot = 0;
            sort.Respond(new SortBagResponse { Bag = sorted, Changed = true });
            Assert.That(_client.BusySort, Is.False);
            Assert.That(_client.Bag.Layout.Slots[0].Slot, Is.Zero);
            Assert.That(_client.BagError, Is.Empty);
        }

        [Test]
        public void SortRejectionClearsBusyAndKeepsGoodBag()
        {
            LoadBag();
            var previous = _client.Bag;
            _client.SortBag();
            _net.Calls[1].Respond(new SortBagResponse { ErrorMessage = new TipInfoMessage { Id = 9 }, Bag = Bag(0, 9999) });
            Assert.That(_client.BusySort, Is.False);
            Assert.That(_client.Bag, Is.SameAs(previous));
            Assert.That(_client.BagError, Does.Contain("9"));
            _client.SortBag();
            _net.Calls[2].FailWith("请求超时");
            Assert.That(_client.BusySort, Is.False);
            Assert.That(_client.BagError, Is.EqualTo("请求超时"));
        }

        [Test]
        public void SortWithoutSnapshotWhileLoadingOrForEquipmentDoesNotSend()
        {
            _client.SortBag();
            Assert.That(_net.Calls, Is.Empty);
            _client.RequestBag(2);
            _client.SortBag();
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
            var equipment = Bag(2);
            equipment.Layout.CanSort = true; // A malformed capability must not enable unsupported bag kinds.
            _net.Calls[0].Respond(new GetBagResponse { Bag = equipment });
            _client.SortBag();
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
        }

        [Test]
        public void ServerSortCapabilityIsRespected()
        {
            _client.RequestBag();
            var bag = Bag();
            bag.Layout.CanSort = false;
            _net.Calls[0].Respond(new GetBagResponse { Bag = bag });
            _client.SortBag();
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
            Assert.That(_client.BusySort, Is.False);
        }

        [Test]
        public void MissionObjectiveIdentityAndServerCapabilitiesArePreserved()
        {
            _client.RequestMissions();
            var response = new GetMissionListResponse { StatePersistent = false };
            var mission = new PlayerMissionInfo { MissionId = 15, CanAccept = false, CanClaim = false, Configured = true };
            mission.Objectives.Add(new MissionObjectiveInfo { ObjectiveIndex = 0, ConditionId = 12, Progress = 1, Target = 3 });
            mission.Objectives.Add(new MissionObjectiveInfo { ObjectiveIndex = 1, ConditionId = 12, Progress = 0, Target = 2 });
            response.Missions.Add(mission);
            _net.Calls[0].Respond(response);

            Assert.That(_client.Missions.StatePersistent, Is.False);
            Assert.That(_client.Missions.Missions[0].Objectives.Count, Is.EqualTo(2));
            Assert.That(_client.Missions.Missions[0].Objectives[1].ObjectiveIndex, Is.EqualTo(1));
            Assert.That(_client.Missions.Missions[0].CanClaim, Is.False);
            Assert.That(_client.Missions.Missions[0].Name, Is.Empty);
            Assert.That(_client.MissionsLoading, Is.False);
        }

        [Test]
        public void ActivityScheduleAndParticipationAreNeverInferredFromClientDate()
        {
            _client.RequestActivities();
            var response = new GetActivityListResponse { ServerTimeMs = 1234567890 };
            response.Activities.Add(new PlayerActivityInfo { ActivityId = 15, MissionId = 15, CanParticipate = false });
            _net.Calls[0].Respond(response);
            var activity = _client.Activities.Activities[0];
            Assert.That((int)activity.Status, Is.Zero);
            Assert.That(activity.StartsAtMs, Is.Zero);
            Assert.That(activity.EndsAtMs, Is.Zero);
            Assert.That(activity.CanParticipate, Is.False);
            Assert.That(activity.Name, Is.Empty);
            Assert.That(_client.Activities.ServerTimeMs, Is.EqualTo(1234567890));
        }

        [Test]
        public void MissionAndActivityFailuresRemainIndependentAndKeepSnapshots()
        {
            _client.RequestMissions();
            _client.RequestActivities();
            _net.Calls[0].Respond(new GetMissionListResponse());
            _net.Calls[1].Respond(new GetActivityListResponse());
            var missions = _client.Missions;
            var activities = _client.Activities;
            _client.RequestMissions();
            _client.RequestActivities();
            _net.Calls[2].Respond(new GetMissionListResponse { ErrorMessage = new TipInfoMessage { Id = 7 } });
            _net.Calls[3].FailWith("活动请求超时");
            Assert.That(_client.Missions, Is.SameAs(missions));
            Assert.That(_client.Activities, Is.SameAs(activities));
            Assert.That(_client.MissionsError, Does.Contain("7"));
            Assert.That(_client.ActivitiesError, Is.EqualTo("活动请求超时"));
            Assert.That(_client.MissionsLoading || _client.ActivitiesLoading, Is.False);
            Assert.That(_client.BagError, Is.Empty);
        }

        [Test]
        public void OlderListResponsesAndErrorsCannotOverrideNewerRequests()
        {
            _client.RequestMissions();
            _client.RequestMissions();
            _client.RequestActivities();
            _client.RequestActivities();
            var missions = new GetMissionListResponse();
            missions.Missions.Add(new PlayerMissionInfo { MissionId = 22 });
            _net.Calls[1].Respond(missions);
            _net.Calls[3].Respond(new GetActivityListResponse { ServerTimeMs = 200 });
            _net.Calls[0].FailWith("旧请求超时");
            _net.Calls[2].Respond(new GetActivityListResponse { ServerTimeMs = 100 });
            Assert.That(_client.Missions.Missions[0].MissionId, Is.EqualTo(22));
            Assert.That(_client.Activities.ServerTimeMs, Is.EqualTo(200));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void DisconnectClearsAllSnapshotsAndLateCallbacksCannotRepopulate()
        {
            LoadBag();
            _client.SortBag();
            _client.RequestMissions();
            _client.RequestActivities();
            int changes = _changes;
            _net.RaiseDisconnected();
            Assert.That(_changes, Is.GreaterThan(changes));
            Assert.That(_client.HasBag || _client.HasMissions || _client.HasActivities, Is.False);
            Assert.That(_client.BusySort || _client.BagLoading || _client.MissionsLoading || _client.ActivitiesLoading, Is.False);
            _net.Calls[1].Respond(new SortBagResponse { Bag = Bag() });
            _net.Calls[2].Respond(new GetMissionListResponse());
            _net.Calls[3].FailWith("旧会话失败");
            Assert.That(_client.HasBag || _client.HasMissions || _client.HasActivities, Is.False);
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void NewCharacterDropsPreviousDataAndIgnoresPreviousCharacterResponse()
        {
            LoadBag();
            _client.RequestMissions();
            _net.PlayerId = 9202;
            _client.RequestActivities();
            Assert.That(_client.HasBag, Is.False);
            Assert.That(_client.MissionsLoading, Is.False);
            _net.Calls[1].Respond(new GetMissionListResponse());
            _net.Calls[2].Respond(new GetActivityListResponse { ServerTimeMs = 300 });
            Assert.That(_client.HasMissions, Is.False);
            Assert.That(_client.Activities.ServerTimeMs, Is.EqualTo(300));
        }

        [TestCase(false, 9101ul)]
        [TestCase(true, 0ul)]
        public void MissingGateOrUnselectedCharacterSendsNoSceneRpc(bool ready, ulong playerId)
        {
            _net.IsReady = ready;
            _net.PlayerId = playerId;
            _client.RequestBag();
            _client.RequestMissions();
            _client.RequestActivities();
            _client.SortBag();
            Assert.That(_net.Calls, Is.Empty);
            Assert.That(_errors, Is.Not.Empty);
            Assert.That(_client.BagLoading || _client.MissionsLoading || _client.ActivitiesLoading || _client.BusySort, Is.False);
        }

        [Test]
        public void InvalidBagTypeDoesNotSend()
        {
            _client.RequestBag(99);
            Assert.That(_net.Calls, Is.Empty);
            Assert.That(_client.BagError, Is.Not.Empty);
        }

        [Test]
        public void DisposeUnsubscribesAndInvalidatesCallbacks()
        {
            _client.RequestBag();
            _client.Dispose();
            int changes = _changes;
            _net.Calls[0].Respond(new GetBagResponse { Bag = Bag() });
            _net.RaiseDisconnected();
            _client.RequestMissions();
            Assert.That(_client.HasBag, Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
            Assert.That(_changes, Is.EqualTo(changes));
        }
    }
}
