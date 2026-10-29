using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI.Ugui.Gameplay;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>无挂载点/真实账号也可验证常驻宿主的读取、会话和订阅生命周期。</summary>
    public sealed class QuestHudRootSessionTests
    {
        private GameObject _host;
        private QuestHudRoot _root;
        private readonly List<PlayerFeaturesClient> _clients = new();

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("QuestHudRootSessionTests");
            _root = _host.AddComponent<QuestHudRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            // EditMode不依赖场景消息调度，直接执行同一销毁回调保证失败用例也释放订阅。
            if (_root != null) DestroyLifecycle(_root);
            if (_host != null) Object.DestroyImmediate(_host);
            foreach (var client in _clients) client.Dispose();
            _clients.Clear();
        }

        private PlayerFeaturesClient Client(FakeBattleTransport net)
        {
            var client = new PlayerFeaturesClient(net);
            _clients.Add(client);
            return client;
        }

        private void Tick(PlayerFeaturesClient client, FakeBattleTransport net, float now, bool inBattle = false)
            => _root.TickSession(client, net.PlayerId, inBattle, now);

        private static GetMissionListResponse Missions(uint id = 81)
        {
            var snapshot = new GetMissionListResponse();
            snapshot.Missions.Add(new PlayerMissionInfo
            {
                MissionId = id, Scope = 2, Status = PlayerMissionStatus.PlayerMissionActive,
                Name = "受控任务", Configured = true,
            });
            return snapshot;
        }

        private static void DestroyLifecycle(QuestHudRoot root)
            => typeof(QuestHudRoot).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(root, null);

        private int RootSubscribers(PlayerFeaturesClient client)
        {
            var handlers = typeof(PlayerFeaturesClient)
                .GetField("OnChanged", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(client) as Action;
            return handlers?.GetInvocationList().Count(handler => ReferenceEquals(handler.Target, _root)) ?? 0;
        }

        private static void AssertOnlyMissionReads(FakeBattleTransport net)
        {
            Assert.That(net.Calls.All(call => call.MessageId == SceneMissionClientPlayerRpc.GetMissionListMessageId),
                Is.True, "宿主同步只读取任务，不自动接取或领奖");
            Assert.That(net.OneWays, Is.Empty);
        }

        [Test]
        public void FirstReadySessionReadsWithoutMountAndRepeatedTicksDoNotDuplicateInflightRead()
        {
            var net = new FakeBattleTransport(); var client = Client(net);
            Tick(client, net, 3);
            Assert.That(client.MissionsLoading, Is.True);
            Assert.That(net.Calls.Count, Is.EqualTo(1));
            Tick(client, net, 4); Tick(client, net, 30);
            Assert.That(net.Calls.Count, Is.EqualTo(1), "在途读取即使超出轮询间隔也不能重复发出");
            Assert.That(RootSubscribers(client), Is.EqualTo(1));
            AssertOnlyMissionReads(net);
        }

        [Test]
        public void PollingUsesTenSecondIntervalAndKeepsOneSubscription()
        {
            var net = new FakeBattleTransport(); var client = Client(net);
            Tick(client, net, 5); net.Calls[0].Respond(Missions());
            Tick(client, net, 14.99f);
            Assert.That(net.Calls.Count, Is.EqualTo(1));
            Tick(client, net, 15);
            Assert.That(net.Calls.Count, Is.EqualTo(2));
            net.Calls[1].Respond(Missions());
            Tick(client, net, 24.99f);
            Assert.That(net.Calls.Count, Is.EqualTo(2));
            Tick(client, net, 25);
            Assert.That(net.Calls.Count, Is.EqualTo(3));
            Assert.That(RootSubscribers(client), Is.EqualTo(1));
            AssertOnlyMissionReads(net);
        }

        [Test]
        public void HiddenHostAndNoVisibleMountStillReceiveSnapshotsAndContinuePolling()
        {
            var net = new FakeBattleTransport(); var client = Client(net);
            Tick(client, net, 0);
            _host.SetActive(false);
            net.Calls[0].Respond(Missions());
            var mission = client.Missions.Missions[0];
            client.MissionTracking.Track(mission);
            Tick(client, net, 10);
            var changed = Missions();
            changed.Missions[0].Objectives.Add(new MissionObjectiveInfo
                { ObjectiveIndex = 7, Progress = 3, Target = 5 });
            net.Calls[1].Respond(changed);
            Assert.That(client.Missions.Missions[0].Objectives[0].Progress, Is.EqualTo(3));
            Assert.That(client.MissionTracking.IsTracked(MissionTrackingState.Key(mission)), Is.True);
            Assert.That(RootSubscribers(client), Is.EqualTo(1));
            AssertOnlyMissionReads(net);
        }

        [Test]
        public void ReturningFromBattleRefreshesBeforeRegularInterval()
        {
            var net = new FakeBattleTransport(); var client = Client(net);
            Tick(client, net, 0); net.Calls[0].Respond(Missions());
            Tick(client, net, 1, true);
            Assert.That(net.Calls.Count, Is.EqualTo(1));
            Tick(client, net, 2);
            Assert.That(net.Calls.Count, Is.EqualTo(2));
            net.Calls[1].Respond(Missions());
            Tick(client, net, 3);
            Assert.That(net.Calls.Count, Is.EqualTo(2));
            AssertOnlyMissionReads(net);
        }

        [Test]
        public void BattleReturnWaitsForInflightReadThenRefreshes()
        {
            var net = new FakeBattleTransport(); var client = Client(net);
            Tick(client, net, 0, true);
            Tick(client, net, 1);
            Assert.That(net.Calls.Count, Is.EqualTo(1));
            net.Calls[0].Respond(Missions());
            Tick(client, net, 2);
            Assert.That(net.Calls.Count, Is.EqualTo(2));
            AssertOnlyMissionReads(net);
        }

        [Test]
        public void SameClientCharacterSwitchClearsOldInflightSessionBeforeLoadingNewCharacter()
        {
            var net = new FakeBattleTransport(); var client = Client(net);
            Tick(client, net, 0); net.Calls[0].Respond(Missions());
            client.MissionTracking.Track(client.Missions.Missions[0]);
            Tick(client, net, 10);
            Assert.That(client.MissionsLoading, Is.True);
            net.PlayerId++;
            Tick(client, net, 11);
            Assert.That(net.Calls.Count, Is.EqualTo(3), "换角须先进入Client.PrepareRequest，不能被旧loading挡住");
            Assert.That(client.Missions, Is.Null);
            Assert.That(client.MissionTracking.TrackedKeys, Is.Empty);
            net.Calls[1].Respond(Missions(82));
            Assert.That(client.Missions, Is.Null, "旧角色在途回包必须失效");
            net.Calls[2].Respond(Missions(83));
            Assert.That(client.Missions.Missions[0].MissionId, Is.EqualTo(83));
            Assert.That(RootSubscribers(client), Is.EqualTo(1));
            AssertOnlyMissionReads(net);
        }

        [Test]
        public void SameClientSamePlayerConnectionReplacementRefreshesDespiteOldInflightRead()
        {
            var net = new FakeBattleTransport();
            object connection = new object();
            var client = new PlayerFeaturesClient(net, () => connection);
            _clients.Add(client);
            // 先准备旧连接的有效快照与手动偏好，以下只统计宿主自身发出的两次读取。
            client.RequestMissions(); net.Calls[0].Respond(Missions());
            var oldMission = client.Missions.Missions[0];
            client.MissionTracking.Track(oldMission);
            net.Calls.Clear();

            _root.TickSession(client, net.PlayerId, false, 0, connection);
            Assert.That(client.MissionsLoading, Is.True);
            Assert.That(net.Calls.Count, Is.EqualTo(1));
            connection = new object();
            _root.TickSession(client, net.PlayerId, false, 1, connection);

            Assert.That(net.Calls.Count, Is.EqualTo(2), "静默换连接须先进入PrepareRequest，不能被旧loading挡住");
            Assert.That(client.Missions, Is.Null);
            Assert.That(client.MissionTracking.TrackedKeys, Is.Empty);
            Assert.That(client.MissionTracking.Track(oldMission), Is.False);
            net.Calls[0].Respond(Missions(96));
            Assert.That(client.Missions, Is.Null, "旧连接回包不能填充新连接快照");
            Assert.That(client.MissionsLoading, Is.True, "旧回包不能结束新连接的在途状态");
            net.Calls[1].Respond(Missions(97));
            Assert.That(client.Missions.Missions[0].MissionId, Is.EqualTo(97));
            Assert.That(client.MissionsLoading, Is.False);
            Assert.That(client.MissionTracking.TrackedKeys, Is.Empty);
            Assert.That(RootSubscribers(client), Is.EqualTo(1));
            AssertOnlyMissionReads(net);
        }

        [Test]
        public void ReplacingClientUnsubscribesOldSourceAndOnlyPollsNewSource()
        {
            var oldNet = new FakeBattleTransport(); var oldClient = Client(oldNet);
            var nextNet = new FakeBattleTransport { PlayerId = 2002 }; var nextClient = Client(nextNet);
            Tick(oldClient, oldNet, 0);
            Assert.That(RootSubscribers(oldClient), Is.EqualTo(1));
            Tick(nextClient, nextNet, 1);
            Assert.That(RootSubscribers(oldClient), Is.Zero);
            Assert.That(RootSubscribers(nextClient), Is.EqualTo(1));
            oldNet.Calls[0].Respond(Missions(84));
            Assert.That(nextClient.Missions, Is.Null);
            nextNet.Calls[0].Respond(Missions(85));
            Tick(nextClient, nextNet, 11);
            Assert.That(oldNet.Calls.Count, Is.EqualTo(1));
            Assert.That(nextNet.Calls.Count, Is.EqualTo(2));
            AssertOnlyMissionReads(oldNet); AssertOnlyMissionReads(nextNet);
        }

        [Test]
        public void ZeroPlayerDetachesAndDoesNotFetchUntilValidSessionReturns()
        {
            var net = new FakeBattleTransport(); var client = Client(net);
            Tick(client, net, 0); net.Calls[0].Respond(Missions());
            _root.TickSession(client, 0, false, 10);
            Assert.That(RootSubscribers(client), Is.Zero);
            _root.TickSession(client, 0, false, 20);
            Assert.That(net.Calls.Count, Is.EqualTo(1));
            Tick(client, net, 21);
            Assert.That(net.Calls.Count, Is.EqualTo(2));
            Assert.That(RootSubscribers(client), Is.EqualTo(1));
            AssertOnlyMissionReads(net);
        }

        [Test]
        public void DestroyLifecycleReleasesSubscriptionAndLateSnapshotHasNoHostCallback()
        {
            var net = new FakeBattleTransport(); var client = Client(net);
            Tick(client, net, 0);
            Assert.That(RootSubscribers(client), Is.EqualTo(1));
            DestroyLifecycle(_root);
            Assert.That(RootSubscribers(client), Is.Zero);
            Assert.DoesNotThrow(() => net.Calls[0].Respond(Missions()));
            Assert.That(client.Missions.Missions.Count, Is.EqualTo(1));
            AssertOnlyMissionReads(net);
        }
    }
}
