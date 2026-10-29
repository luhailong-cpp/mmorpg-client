using System;
using System.Linq;
using System.Reflection;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Net.Generated;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Gameplay;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>正式07壳与08独立宿主的组合验证；只向受控Transport喂权威回包，不创建账号或网络。</summary>
    public sealed class QuestHudIntegrationTests
    {
        private GameObject _shellObject, _host;
        private WorldSideHudRoot _shell;
        private QuestHudRoot _root;
        private FakeBattleTransport _net;
        private PlayerFeaturesClient _client;

        [SetUp]
        public void SetUp()
        {
            Assert.That(WorldSideHudRoot.Instance == null, Is.True, "不替换其他测试或场景的07宿主");
            Assert.That(QuestHudRoot.Instance == null, Is.True, "不替换其他测试或场景的08宿主");
            CreateShell();
            _host = new GameObject("QuestHudIntegrationHost");
            _root = _host.AddComponent<QuestHudRoot>();
            Lifecycle(_root, "Awake");
            _root.EnsureMounted();
            _net = new FakeBattleTransport();
            _client = new PlayerFeaturesClient(_net);
        }

        [TearDown]
        public void TearDown()
        {
            DestroyHost();
            DestroyShell();
            _client?.Dispose();
            _client = null;
        }

        [Test]
        public void ProductionHostMountsExactlyOnceInsideThe07QuestMount()
        {
            var content = Content();
            Assert.That(content.parent, Is.SameAs(_shell.QuestMount));
            Assert.That(content.sizeDelta, Is.EqualTo(new Vector2(416, 582)));
            Assert.That(content.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(content.gameObject.activeInHierarchy, Is.True);
            Assert.That(_shell.TeamMount.gameObject.activeInHierarchy, Is.False);
            Load();
            for (int i = 0; i < 20; ++i)
            {
                _root.EnsureMounted();
                Tick(i * .1f);
            }
            Assert.That(Content(), Is.SameAs(content));
            Assert.That(Contents().Length, Is.EqualTo(1));
            Assert.That(RootSubscribers(), Is.EqualTo(1));
            Assert.That(ContentSubscribers(), Is.EqualTo(1));
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
            AssertOnlyReads();
        }

        [Test]
        public void TeamPageAndCollapsedShellContinueUpdatingTheSameTrackedMission()
        {
            Load();
            var content = Content();
            var mission = _client.Missions.Missions[0];
            _client.MissionTracking.Track(mission);
            _shell.SelectPage(WorldSideHudPage.Team);
            _shell.SetCollapsed(true);
            Assert.That(content.gameObject.activeInHierarchy, Is.False);

            Tick(10);
            _net.Calls[1].Respond(Snapshot(7));
            AssertText("7/10");
            Assert.That(_client.MissionTracking.IsTracked(MissionTrackingState.Key(mission)), Is.True);
            Assert.That(_client.Missions.Missions[0].Objectives[0].Progress, Is.EqualTo(7));
            _shell.SetCollapsed(false);
            Assert.That(_shell.SelectedPage, Is.EqualTo(WorldSideHudPage.Team));
            Assert.That(_shell.TeamMount.gameObject.activeInHierarchy, Is.True);
            _shell.SelectPage(WorldSideHudPage.Quests);
            Assert.That(content.gameObject.activeInHierarchy, Is.True);
            Assert.That(Content(), Is.SameAs(content));
            AssertText("取消追踪");
            Assert.That(RootSubscribers(), Is.EqualTo(1));
            Assert.That(ContentSubscribers(), Is.EqualTo(1));
            AssertOnlyReads();
        }

        [Test]
        public void Destroyed07ShellReattachesCurrentClientSnapshotWithoutDuplicatingSubscriptions()
        {
            Load();
            var oldContent = Content();
            _client.MissionTracking.Track(_client.Missions.Missions[0]);
            DestroyShell();
            _root.EnsureMounted();
            Assert.That(oldContent == null, Is.True);
            Assert.That(ContentSubscribers(), Is.Zero, "已销毁内容必须解除偏好订阅");
            Assert.That(RootSubscribers(), Is.EqualTo(1), "独立08宿主仍订阅当前Client");

            CreateShell();
            for (int i = 0; i < 10; ++i) _root.EnsureMounted();
            Assert.That(Contents().Length, Is.EqualTo(1));
            Assert.That(Content().parent, Is.SameAs(_shell.QuestMount));
            AssertText("1/10");
            AssertText("取消追踪");
            Assert.That(_net.Calls.Count, Is.EqualTo(1), "重新挂载直接使用现有快照，不新建Client或重复读取");
            Tick(10);
            _net.Calls[1].Respond(Snapshot(9));
            AssertText("9/10");
            Assert.That(RootSubscribers(), Is.EqualTo(1));
            Assert.That(ContentSubscribers(), Is.EqualTo(1));
            AssertOnlyReads();
        }

        [Test]
        public void Destroying08HostOnlyRemovesItsContentAndKeeps07MountAndClientPreferences()
        {
            Load();
            var mount = _shell.QuestMount;
            var content = Content();
            var mission = _client.Missions.Missions[0];
            _client.MissionTracking.Track(mission);
            DestroyHost();
            Assert.That(content == null, Is.True);
            Assert.That(mount == null, Is.False, "08不能销毁07的挂载点");
            Assert.That(_shell.QuestMount, Is.SameAs(mount));
            Assert.That(Contents(), Is.Empty);
            Assert.That(RootSubscribers(), Is.Zero);
            Assert.That(ContentSubscribers(), Is.Zero);
            Assert.That(_client.MissionTracking.IsTracked(MissionTrackingState.Key(mission)), Is.True);
            _client.RequestMissions();
            Assert.DoesNotThrow(() => _net.Calls[1].Respond(Snapshot(8)));
            Assert.That(_client.Missions.Missions[0].Objectives[0].Progress, Is.EqualTo(8));
            AssertOnlyReads();
        }

        [Test]
        public void CategoryAndBattleVisibilityDoNotSuspendSyncOrRecreateTaskContent()
        {
            Load();
            var content = Content();
            _shell.RefreshPresentation(true, true);
            Assert.That(content.gameObject.activeInHierarchy, Is.False);
            Tick(10);
            _net.Calls[1].Respond(Snapshot(4));
            AssertText("4/10");
            _shell.RefreshPresentation(false, false);
            Tick(11, true);
            Tick(12, false);
            Assert.That(_net.Calls.Count, Is.EqualTo(3), "离开战斗须立即刷新而非等下一周期");
            _net.Calls[2].Respond(Snapshot(6));
            _shell.RefreshPresentation(true, false);
            Assert.That(content.gameObject.activeInHierarchy, Is.True);
            Assert.That(Content(), Is.SameAs(content));
            AssertText("6/10");
            Assert.That(RootSubscribers(), Is.EqualTo(1));
            Assert.That(ContentSubscribers(), Is.EqualTo(1));
            AssertOnlyReads();
        }

        private void CreateShell()
        {
            _shellObject = new GameObject("QuestHudIntegration07Shell");
            _shell = _shellObject.AddComponent<WorldSideHudRoot>();
            _shell.EnsureBuilt();
            _shell.RefreshPresentation(true, false);
        }

        private void DestroyShell()
        {
            if (_shell != null) Lifecycle(_shell, "OnDestroy");
            if (_shellObject != null) Object.DestroyImmediate(_shellObject);
            _shell = null; _shellObject = null;
        }

        private void DestroyHost()
        {
            if (_root != null) Lifecycle(_root, "OnDestroy");
            if (_host != null) Object.DestroyImmediate(_host);
            _host = null;
        }

        private static void Lifecycle(object instance, string method) => instance.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null);
        private void Tick(float now, bool battle = false) => _root.TickSession(_client, _net.PlayerId, battle, now);
        private void Load() { Tick(0); _net.Calls[0].Respond(Snapshot(1)); Canvas.ForceUpdateCanvases(); }
        private RectTransform[] Contents() => _shell.QuestMount.GetComponentsInChildren<RectTransform>(true)
            .Where(rect => rect.name == "QuestHudContent").ToArray();
        private RectTransform Content() => Contents().Single();
        private void AssertText(string value) => Assert.That(Content().GetComponentsInChildren<TMP_Text>(true)
            .Any(text => text.text.Contains(value)), Is.True, "07实际挂载内容缺少：" + value);
        private int RootSubscribers() => Subscribers(_client, "OnChanged").Count(handler => ReferenceEquals(handler.Target, _root));
        private int ContentSubscribers() => Subscribers(_client.MissionTracking, "OnChanged")
            .Count(handler => handler.Target is QuestHudContent);
        private static Delegate[] Subscribers(object source, string field) =>
            (source.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source) as Delegate)
            ?.GetInvocationList() ?? Array.Empty<Delegate>();
        private void AssertOnlyReads()
        {
            Assert.That(_net.Calls.All(call => call.MessageId == SceneMissionClientPlayerRpc.GetMissionListMessageId), Is.True);
            Assert.That(_net.OneWays, Is.Empty);
        }
        private static GetMissionListResponse Snapshot(uint progress) => new GetMissionListResponse
        {
            Missions = { new PlayerMissionInfo
            {
                Scope = 2, MissionId = 81, MissionType = 2, Name = "真实装配离线任务",
                Status = PlayerMissionStatus.PlayerMissionActive, Configured = true,
                Objectives = { new MissionObjectiveInfo { ObjectiveIndex = 7, ConditionId = 12,
                    Description = "受控回包中的任务目标", Progress = progress, Target = 10 } }
            } }
        };
    }
}
