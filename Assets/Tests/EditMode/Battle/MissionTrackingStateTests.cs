using System;
using System.Collections.Generic;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Battle
{
    public sealed class MissionTrackingStateTests
    {
        private static PlayerMissionInfo Mission(uint id = 71, uint scope = 0,
            PlayerMissionStatus status = PlayerMissionStatus.PlayerMissionActive)
            => new PlayerMissionInfo { MissionId = id, Scope = scope, Status = status, Configured = true };

        private static GetMissionListResponse Snapshot(params PlayerMissionInfo[] missions)
        {
            var snapshot = new GetMissionListResponse();
            snapshot.Missions.Add(missions);
            return snapshot;
        }

        [Test]
        public void ManualTrackingRequiresCurrentAuthorityAndDoesNotTrackNewCandidates()
        {
            var state = new MissionTrackingState();
            var mission = Mission();
            Assert.That(state.Track(mission), Is.False);
            state.UpdateSnapshot(Snapshot(mission));
            Assert.That(state.TrackedKeys, Is.Empty);
            Assert.That(state.Track(mission), Is.True);
            Assert.That(state.CurrentKey, Is.EqualTo(MissionTrackingState.Key(mission)));
        }

        [Test]
        public void SameIdAcrossScopesAndManyManualChoicesRemainIndependent()
        {
            var state = new MissionTrackingState();
            var a = Mission(71, 0);
            var b = Mission(71, 1);
            var c = Mission(72, 0);
            state.UpdateSnapshot(Snapshot(a, b, c));
            state.Track(a); state.Track(b); state.Track(c);
            Assert.That(state.TrackedKeys.Count, Is.EqualTo(3));
            state.Toggle(b);
            Assert.That(state.IsTracked(MissionTrackingState.Key(a)), Is.True);
            Assert.That(state.IsTracked(MissionTrackingState.Key(b)), Is.False);
            Assert.That(state.IsTracked(MissionTrackingState.Key(c)), Is.True);
        }

        [Test]
        public void TrackNullOnlyCancelsCurrentManualSelection()
        {
            var state = new MissionTrackingState();
            var a = Mission(1); var b = Mission(2);
            state.UpdateSnapshot(Snapshot(a, b)); state.Track(a); state.Track(b);
            Assert.That(state.Track(null), Is.True);
            Assert.That(state.TrackedKeys, Is.EquivalentTo(new[] { MissionTrackingState.Key(a) }));
            Assert.That(state.CurrentKey, Is.EqualTo(MissionTrackingState.Key(a)));
            Assert.That(state.Track(null), Is.True);
            Assert.That(state.Track(null), Is.False);
            Assert.That(state.CurrentKey, Is.Zero);
        }

        [Test]
        public void RepeatedTrackIsIdempotentAndReselectionOnlyChangesCurrent()
        {
            var state = new MissionTrackingState();
            var a = Mission(1); var b = Mission(2);
            state.UpdateSnapshot(Snapshot(a, b));
            int changes = 0; state.OnChanged += () => ++changes;
            state.Track(a); state.Track(a); state.Track(b); state.Track(a);
            Assert.That(changes, Is.EqualTo(3));
            Assert.That(state.TrackedKeys.Count, Is.EqualTo(2));
            Assert.That(state.CurrentKey, Is.EqualTo(MissionTrackingState.Key(a)));
            state.Untrack(MissionTrackingState.Key(a));
            Assert.That(state.CurrentKey, Is.EqualTo(MissionTrackingState.Key(b)));
        }

        [TestCase(PlayerMissionStatus.PlayerMissionNotAccepted)]
        [TestCase(PlayerMissionStatus.PlayerMissionCompleted)]
        [TestCase(PlayerMissionStatus.PlayerMissionFailed)]
        [TestCase((PlayerMissionStatus)99)]
        public void TerminalOrIneligibleStateRemovesReferenceAndRejectsStaleObject(PlayerMissionStatus status)
        {
            var state = new MissionTrackingState();
            var old = Mission();
            state.UpdateSnapshot(Snapshot(old)); state.Track(old);
            state.UpdateSnapshot(Snapshot(Mission(status: status)));
            Assert.That(state.TrackedKeys, Is.Empty);
            Assert.That(state.CurrentKey, Is.Zero);
            Assert.That(state.Track(old), Is.False);
        }

        [Test]
        public void ClaimableRemainsTrackableUntilServerSaysCompleted()
        {
            var state = new MissionTrackingState();
            var active = Mission();
            state.UpdateSnapshot(Snapshot(active)); state.Track(active);
            var claimable = Mission(status: PlayerMissionStatus.PlayerMissionClaimable);
            state.UpdateSnapshot(Snapshot(claimable));
            Assert.That(state.IsTracked(MissionTrackingState.Key(active)), Is.True);
            Assert.That(claimable.CanClaim, Is.False, "追踪不会推导领奖能力");
            state.UpdateSnapshot(Snapshot(Mission(status: PlayerMissionStatus.PlayerMissionCompleted)));
            Assert.That(state.TrackedKeys, Is.Empty);
        }

        [Test]
        public void ObjectiveProgressDoesNotChangeIdentityOrCreateAutomaticTracking()
        {
            var state = new MissionTrackingState();
            var mission = Mission();
            mission.Objectives.Add(new MissionObjectiveInfo { ObjectiveIndex = 4, ConditionId = 9, Progress = 1, Target = 2 });
            mission.Objectives.Add(new MissionObjectiveInfo { ObjectiveIndex = 8, ConditionId = 9, Progress = 0, Target = 3 });
            state.UpdateSnapshot(Snapshot(mission)); state.Track(mission);
            var updated = mission.Clone();
            updated.Objectives[0].Completed = true; updated.Objectives[0].Progress = 2;
            state.UpdateSnapshot(Snapshot(updated, Mission(72)));
            Assert.That(state.TrackedKeys, Is.EquivalentTo(new[] { MissionTrackingState.Key(mission) }));
            Assert.That(updated.Objectives.Count, Is.EqualTo(2));
            Assert.That(updated.Objectives[1].ObjectiveIndex, Is.EqualTo(8));
        }

        [Test]
        public void SnapshotRemovalAndNullSnapshotClearSelectionWithoutGhosts()
        {
            var state = new MissionTrackingState();
            var a = Mission(1); var b = Mission(2);
            state.UpdateSnapshot(Snapshot(a, b)); state.Track(a); state.Track(b);
            state.UpdateSnapshot(Snapshot(a));
            Assert.That(state.TrackedKeys, Is.EquivalentTo(new[] { MissionTrackingState.Key(a) }));
            state.UpdateSnapshot(null);
            Assert.That(state.TrackedKeys, Is.Empty);
            Assert.That(state.Track(a), Is.False);
        }

        [Test]
        public void ClearForgetsAuthorityAndOnlyNotifiesOnPreferenceChange()
        {
            var state = new MissionTrackingState(); var mission = Mission();
            state.UpdateSnapshot(Snapshot(mission)); state.Track(mission);
            int changes = 0; state.OnChanged += () => ++changes;
            state.Clear(); state.Clear();
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(state.Track(mission), Is.False);
        }

        [Test]
        public void TrackedCollectionCannotBeMutatedAndAutomaticPolicyStaysUnavailable()
        {
            var state = new MissionTrackingState();
            Assert.That(state.AutoTrackingAvailable, Is.False);
            Assert.That(state.AutoTrackingUnavailableReason, Is.Not.Empty);
            Assert.Throws<NotSupportedException>(() => ((ICollection<ulong>)state.TrackedKeys).Add(123));
            Assert.That(MissionTrackingState.CanTrack(Mission(0)), Is.False);
        }

        [Test]
        public void ClientRefreshAndFailedReadPreserveManualSelectionButNeverChangeProgress()
        {
            var net = new FakeBattleTransport();
            using var client = new PlayerFeaturesClient(net);
            var mission = Mission();
            client.RequestMissions(); net.Calls[0].Respond(Snapshot(mission));
            client.MissionTracking.Track(mission);
            client.RequestMissions(); net.Calls[1].FailWith("读取超时");
            Assert.That(client.MissionTracking.IsTracked(MissionTrackingState.Key(mission)), Is.True);
            Assert.That(client.MissionsError, Is.Not.Empty);
            Assert.That(net.Calls.Count, Is.EqualTo(2), "追踪和取消追踪不发送任务写RPC");
            client.RequestMissions(); net.Calls[2].Respond(Snapshot());
            Assert.That(client.MissionTracking.TrackedKeys, Is.Empty);
        }

        [Test]
        public void DisconnectAndLateReadCannotRepopulateTrackingAuthority()
        {
            var net = new FakeBattleTransport();
            using var client = new PlayerFeaturesClient(net);
            var mission = Mission();
            client.RequestMissions(); net.Calls[0].Respond(Snapshot(mission));
            client.MissionTracking.Track(mission);
            client.RequestMissions();
            net.RaiseDisconnected();
            net.Calls[1].Respond(Snapshot(mission));
            Assert.That(client.Missions, Is.Null);
            Assert.That(client.MissionTracking.TrackedKeys, Is.Empty);
            Assert.That(client.MissionTracking.Track(mission), Is.False);
        }

        [Test]
        public void CharacterReplacementRejectsLateSnapshotAndDoesNotInheritSameMissionPreference()
        {
            var net = new FakeBattleTransport();
            using var client = new PlayerFeaturesClient(net);
            var mission = Mission();
            client.RequestMissions(); net.Calls[0].Respond(Snapshot(mission));
            client.MissionTracking.Track(mission);
            client.RequestMissions();
            net.PlayerId++;
            client.RequestMissions();
            net.Calls[1].Respond(Snapshot(mission));
            Assert.That(client.Missions, Is.Null);
            net.Calls[2].Respond(Snapshot(mission));
            Assert.That(client.Missions, Is.Not.Null);
            Assert.That(client.MissionTracking.TrackedKeys, Is.Empty);
        }

        [Test]
        public void SameCharacterNewConnectionCannotInheritTrackingOrOldReply()
        {
            var net = new FakeBattleTransport();
            object connection = new object();
            using var client = new PlayerFeaturesClient(net, () => connection);
            var mission = Mission();
            client.RequestMissions(); net.Calls[0].Respond(Snapshot(mission));
            client.MissionTracking.Track(mission);
            client.RequestMissions();
            connection = new object();
            client.RequestMissions();
            net.Calls[1].Respond(Snapshot(mission));
            Assert.That(client.Missions, Is.Null);
            net.Calls[2].Respond(Snapshot(mission));
            Assert.That(client.MissionTracking.TrackedKeys, Is.Empty);
        }

        [Test]
        public void LatestReadWinsAndOldReadCannotRestoreRemovedReference()
        {
            var net = new FakeBattleTransport();
            using var client = new PlayerFeaturesClient(net);
            var mission = Mission();
            client.RequestMissions(); net.Calls[0].Respond(Snapshot(mission));
            client.MissionTracking.Track(mission);
            client.RequestMissions(); client.RequestMissions();
            net.Calls[2].Respond(Snapshot());
            net.Calls[1].Respond(Snapshot(mission));
            Assert.That(client.Missions.Missions, Is.Empty);
            Assert.That(client.MissionTracking.Track(mission), Is.False);
        }

        [Test]
        public void ClaimResponsePrunesCompletedTrackingAndDisposeForgetsAuthority()
        {
            var net = new FakeBattleTransport();
            var client = new PlayerFeaturesClient(net);
            var mission = Mission(status: PlayerMissionStatus.PlayerMissionClaimable); mission.CanClaim = true;
            client.RequestMissions(); net.Calls[0].Respond(Snapshot(mission));
            client.MissionTracking.Track(mission);
            client.ClaimMissionReward(0, mission.MissionId);
            Assert.That(client.MissionTracking.TrackedKeys.Count, Is.EqualTo(1), "领奖请求不能乐观清追踪");
            net.Calls[1].Respond(Snapshot(Mission(status: PlayerMissionStatus.PlayerMissionCompleted)));
            Assert.That(client.MissionTracking.TrackedKeys, Is.Empty);
            client.Dispose();
            Assert.That(client.MissionTracking.Track(mission), Is.False);
        }

        [Test]
        public void AcceptResponseUpdatesAuthorityWithoutAutomaticallyTrackingAcceptedMission()
        {
            var net = new FakeBattleTransport();
            using var client = new PlayerFeaturesClient(net);
            var mission = Mission(status: PlayerMissionStatus.PlayerMissionNotAccepted); mission.CanAccept = true;
            client.RequestMissions(); net.Calls[0].Respond(Snapshot(mission));
            Assert.That(client.MissionTracking.Track(mission), Is.False);
            client.AcceptMission(0, mission.MissionId);
            net.Calls[1].Respond(Snapshot(Mission()));
            Assert.That(client.MissionTracking.TrackedKeys, Is.Empty);
            Assert.That(client.MissionTracking.Track(mission), Is.True, "当前快照已接取；传入对象只提供身份");
        }

        [Test]
        public void LoadingNotificationMayDisposeWithoutSendingStaleMissionRead()
        {
            var net = new FakeBattleTransport();
            using var client = new PlayerFeaturesClient(net);
            bool disposed = false;
            client.OnChanged += () =>
            {
                if (disposed || !client.MissionsLoading) return;
                disposed = true;
                client.Dispose();
            };

            client.RequestMissions();

            Assert.That(disposed, Is.True);
            Assert.That(net.Calls, Is.Empty, "释放后外层读取不能继续发包");
            Assert.That(client.MissionsLoading, Is.False);
            Assert.That(client.Missions, Is.Null);
            Assert.That(client.MissionTracking.Track(Mission()), Is.False);
        }

        [Test]
        public void LoadingNotificationCharacterReplacementOnlySendsInnerCurrentMissionRead()
        {
            var net = new FakeBattleTransport();
            using var client = new PlayerFeaturesClient(net);
            bool replaced = false;
            client.OnChanged += () =>
            {
                if (replaced || !client.MissionsLoading) return;
                replaced = true;
                net.PlayerId++;
                client.RequestMissions();
            };

            client.RequestMissions();

            Assert.That(replaced, Is.True);
            Assert.That(net.Calls.Count, Is.EqualTo(1), "换角通知内的新读取发出后，外层旧意图必须作废");
            Assert.That(net.Calls[0].MessageId, Is.EqualTo(SceneMissionClientPlayerRpc.GetMissionListMessageId));
            var mission = Mission(91, 3);
            net.Calls[0].Respond(Snapshot(mission));
            Assert.That(client.SnapshotPlayerId, Is.EqualTo(net.PlayerId));
            Assert.That(client.Missions.Missions[0].MissionId, Is.EqualTo(91));
            Assert.That(client.MissionsLoading, Is.False);
            Assert.That(client.MissionTracking.Track(mission), Is.True);
        }

        [Test]
        public void LoadingNotificationConnectionReplacementOnlySendsInnerCurrentMissionRead()
        {
            var net = new FakeBattleTransport();
            object connection = new object();
            using var client = new PlayerFeaturesClient(net, () => connection);
            bool replaced = false;
            client.OnChanged += () =>
            {
                if (replaced || !client.MissionsLoading) return;
                replaced = true;
                connection = new object();
                client.RequestMissions();
            };

            client.RequestMissions();

            Assert.That(replaced, Is.True);
            Assert.That(net.Calls.Count, Is.EqualTo(1), "新连接读取发出后，外层旧连接读取必须作废");
            Assert.That(net.Calls[0].MessageId, Is.EqualTo(SceneMissionClientPlayerRpc.GetMissionListMessageId));
            var mission = Mission(92, 4);
            net.Calls[0].Respond(Snapshot(mission));
            Assert.That(client.SnapshotPlayerId, Is.EqualTo(net.PlayerId));
            Assert.That(client.Missions.Missions[0].MissionId, Is.EqualTo(92));
            Assert.That(client.MissionsLoading, Is.False);
            Assert.That(client.MissionTracking.Track(mission), Is.True);
        }

        [Test]
        public void LoadingNotificationNewerSameSubjectReadSupersedesOuterBeforeSend()
        {
            var net = new FakeBattleTransport();
            using var client = new PlayerFeaturesClient(net);
            bool requestedAgain = false;
            client.OnChanged += () =>
            {
                if (requestedAgain || !client.MissionsLoading) return;
                requestedAgain = true;
                client.RequestMissions();
            };

            client.RequestMissions();

            Assert.That(requestedAgain, Is.True);
            Assert.That(net.Calls.Count, Is.EqualTo(1), "同主体较新请求应取代尚未发出的旧请求");
            Assert.That(net.Calls[0].MessageId, Is.EqualTo(SceneMissionClientPlayerRpc.GetMissionListMessageId));
            var mission = Mission(93, 5);
            net.Calls[0].Respond(Snapshot(mission));
            Assert.That(client.SnapshotPlayerId, Is.EqualTo(net.PlayerId));
            Assert.That(client.Missions.Missions[0].MissionId, Is.EqualTo(93));
            Assert.That(client.MissionsLoading, Is.False);
            Assert.That(client.MissionTracking.Track(mission), Is.True);
        }
    }
}
