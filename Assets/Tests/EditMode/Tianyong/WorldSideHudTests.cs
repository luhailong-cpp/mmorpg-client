using System.Linq;
using MmorpgClient.Game.Team;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Team;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class WorldSideHudTests
    {
        private GameObject _fixture;
        private TeamHudPanel _panel;

        [TearDown]
        public void TearDown()
        {
            _panel?.Dispose();
            _panel = null;
            if (_fixture != null) Object.DestroyImmediate(_fixture);
        }

        [Test]
        public void TabsAndCollapse_KeepOneContentAndRestoreSelectionWithoutRecreatingMounts()
        {
            var root = CreateShell();
            var quests = root.QuestMount;
            var team = root.TeamMount;
            root.RefreshPresentation(true, false);
            Assert.That(quests.gameObject.activeInHierarchy, Is.True);
            Assert.That(team.gameObject.activeInHierarchy, Is.False);
            root.SelectPage(WorldSideHudPage.Team);
            Assert.That(quests.gameObject.activeInHierarchy, Is.False);
            Assert.That(team.gameObject.activeInHierarchy, Is.True);
            root.SetCollapsed(true);
            Assert.That(root.SelectedPage, Is.EqualTo(WorldSideHudPage.Team));
            Assert.That(team.gameObject.activeInHierarchy, Is.False);
            Assert.That(quests.gameObject.activeInHierarchy, Is.False);
            root.SetCollapsed(false);
            Assert.That(team.gameObject.activeInHierarchy, Is.True);
            Assert.That(root.TeamMount, Is.SameAs(team));
            Assert.That(root.QuestMount, Is.SameAs(quests));
            Assert.That(root.TeamContent.Root.parent, Is.SameAs(team));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void WorldUnavailableOrCategoriesOpen_HideBothPages(bool worldAvailable, bool categoryOpen)
        {
            var root = CreateShell();
            root.SelectPage(WorldSideHudPage.Team);
            root.RefreshPresentation(worldAvailable, categoryOpen);
            Assert.That(root.Panel.gameObject.activeInHierarchy, Is.False);
            Assert.That(root.IsPageVisible(WorldSideHudPage.Team), Is.False);
            Assert.That(root.IsPageVisible(WorldSideHudPage.Quests), Is.False);
            root.RefreshPresentation(true, false);
            Assert.That(root.Panel.gameObject.activeInHierarchy, Is.True);
            Assert.That(root.IsPageVisible(WorldSideHudPage.Team), Is.True);
        }

        [Test]
        public void VisibilityEvents_AreIdempotentAndDescribeCurrentPage()
        {
            var root = CreateShell();
            int changes = 0;
            root.VisibilityChanged += () => changes++;
            root.RefreshPresentation(true, false);
            root.RefreshPresentation(true, false);
            root.SelectPage(WorldSideHudPage.Team);
            root.SelectPage(WorldSideHudPage.Team);
            root.SetCollapsed(true);
            root.SetCollapsed(true);
            Assert.That(changes, Is.EqualTo(3));
            Assert.That(root.IsPageVisible(WorldSideHudPage.Team), Is.False);
        }

        [Test]
        public void ShellHasNoWorldSizedRaycastGraphic_AndCollapseLeavesOnlyExpandControl()
        {
            var root = CreateShell();
            root.RefreshPresentation(true, false);
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true).Where(g => g.raycastTarget))
                Assert.That(graphic.transform.IsChildOf(root.Panel), Is.True, graphic.name);
            root.SetCollapsed(true);
            var raycasts = root.GetComponentsInChildren<Graphic>().Where(g => g.raycastTarget).ToArray();
            Assert.That(raycasts.Length, Is.EqualTo(1));
            Assert.That(raycasts[0].GetComponent<Button>().name, Is.EqualTo("SideHudCollapse"));
            Assert.That(root.GetComponentInChildren<Canvas>().sortingOrder, Is.EqualTo(138));
        }

        [Test]
        public void UnknownDataAndOfflineMembers_RemainLiteralAndDoNotFabricateIdentity()
        {
            var state = State(Snapshot());
            var panel = CreatePanel(state);
            Assert.That(Text("TeamHudName_11"), Does.Contain("11"));
            Assert.That(Text("TeamHudDetail_11"), Does.Contain("等级未知").And.Contain("门派未知"));
            Assert.That(Text("TeamHudPresence_11"), Is.EqualTo("离线"));
            Assert.That(Text("TeamHudRole_11"), Is.EqualTo("队长·自己"));
            Assert.That(Text("TeamHudName_22"), Is.EqualTo("<size=90>很长很长的真实道友名称</size>"));
            Assert.That(panel.Root.GetComponentsInChildren<TMP_Text>(true).All(t => !t.richText), Is.True);
            Assert.That(Find("TeamHudMember_11"), Is.Not.Null, "离线仍保留真实成员关系");
        }

        [Test]
        public void CapacityAndOverflow_AreDrivenBySnapshotAndListScrolls()
        {
            var snapshot = Snapshot();
            snapshot.Capacity = 7;
            CreatePanel(State(snapshot));
            Assert.That(Text("TeamHudSummary"), Does.Contain("2 / 7"));
            Assert.That(_fixture.GetComponentsInChildren<RectTransform>(true).Count(r => r.name.StartsWith("TeamHudEmptySlot_")), Is.EqualTo(5));
            var scroll = _fixture.GetComponentInChildren<ScrollRect>();
            Assert.That(scroll.vertical, Is.True);
            Assert.That(scroll.horizontal, Is.False);
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1).Within(.001f), "首次展示长名单从顶部开始");
        }

        [Test]
        public void RosterRefresh_PreservesUserScrollPositionAfterInitialTop()
        {
            var snapshot = Snapshot();
            snapshot.Capacity = 7;
            var state = State(snapshot);
            CreatePanel(state);
            var scroll = _fixture.GetComponentInChildren<ScrollRect>();
            scroll.verticalNormalizedPosition = .35f;
            snapshot.Version++;
            snapshot.Members[1].Name = "刷新后的道友";
            state.SetSnapshot(snapshot);
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(.35f).Within(.001f));
        }

        [Test]
        public void SameVersionPresenceUpdate_IsRenderedAndNotDiscarded()
        {
            var snapshot = Snapshot();
            var state = State(snapshot);
            CreatePanel(state);
            Assert.That(Text("TeamHudPresence_11"), Is.EqualTo("离线"));
            snapshot.Members[0].IsOnline = true;
            Assert.That(state.SetSnapshot(snapshot), Is.True);
            Assert.That(Text("TeamHudPresence_11"), Is.EqualTo("在线"));
        }

        [Test]
        public void FreshnessFailure_PreservesRosterButDisablesLeaveAndNeverEnablesSummon()
        {
            var state = State(Snapshot());
            var panel = CreatePanel(state);
            Assert.That(Button("TeamHudLeave").interactable, Is.True);
            panel.SetState(state, false);
            Assert.That(Button("TeamHudLeave").interactable, Is.False);
            Assert.That(Find("TeamHudMember_11"), Is.Not.Null);
            Assert.That(Text("TeamHudStatus"), Does.Contain("信息待确认"));
            Assert.That(Button("TeamHudSummon").interactable, Is.False);
            Assert.That(Text("TeamHudSummonReason"), Is.EqualTo(TeamHudPanel.SummonUnavailableReason));
        }

        [Test]
        public void RebindingState_UnsubscribesOldStateAndNullBindingClearsOldIdentity()
        {
            var oldState = State(Snapshot());
            var panel = CreatePanel(oldState);
            var newSnapshot = new TeamSnapshot { LocalPlayerId = 99, MembershipEpoch = 5, Version = 1 };
            panel.SetState(State(newSnapshot), true);
            Assert.That(Text("TeamHudSummary"), Is.EqualTo("尚未加入队伍"));
            var newView = Find("TeamHudEmpty");
            var stale = Snapshot();
            stale.Members[0].Name = "旧主体不应复活";
            oldState.SetSnapshot(stale);
            Assert.That(Find("TeamHudEmpty"), Is.SameAs(newView), "旧状态事件不能触发新主体重绘");
            Assert.That(Text("TeamHudSummary"), Is.EqualTo("尚未加入队伍"));
            Assert.That(_fixture.GetComponentsInChildren<TMP_Text>(true).Any(t => t.text.Contains("旧主体不应复活")), Is.False);
            panel.Bind(null);
            Assert.That(Text("TeamHudSummary"), Is.EqualTo("正在同步队伍…"));
            Assert.That(Find("TeamHudMember_11"), Is.Null);
        }

        [Test]
        public void FirstViewAndExplicitNoTeam_HaveDifferentStatesAndNoLeaveAction()
        {
            var state = new TeamUiState(() => 0);
            CreatePanel(state);
            Assert.That(Text("TeamHudSummary"), Is.EqualTo("正在同步队伍…"));
            Assert.That(Button("TeamHudLeave").interactable, Is.False);
            state.SetSnapshot(new TeamSnapshot { LocalPlayerId = 11, MembershipEpoch = 1 });
            Assert.That(Text("TeamHudSummary"), Is.EqualTo("尚未加入队伍"));
            Assert.That(Button("TeamHudLeave").interactable, Is.False);
        }

        [TestCase(TeamFollowStatus.None, false, false, false)]
        [TestCase(TeamFollowStatus.Following, false, false, true)]
        [TestCase(TeamFollowStatus.LeaderOutOfSight, false, false, true)]
        [TestCase(TeamFollowStatus.Away, false, true, false)]
        [TestCase(TeamFollowStatus.Away, true, false, false)]
        [TestCase(TeamFollowStatus.Away, true, true, true)]
        public void FollowPermission_LocalReleaseSurvivesServiceFailureButRejoinNeedsAuthority(
            TeamFollowStatus status, bool fresh, bool serviceAvailable, bool expected)
        {
            Assert.That(TeamHudPanel.CanToggleFollow(status, fresh, serviceAvailable), Is.EqualTo(expected));
        }

        private WorldSideHudRoot CreateShell()
        {
            // 编辑模式DestroyImmediate后可留下Unity已销毁对象包装；应使用Unity的空值语义。
            Assert.That(WorldSideHudRoot.Instance == null, Is.True, "隔离测试不应替换现存运行宿主");
            _fixture = new GameObject("WorldSideHudTests");
            var root = _fixture.AddComponent<WorldSideHudRoot>();
            root.EnsureBuilt();
            return root;
        }

        private TeamHudPanel CreatePanel(TeamUiState state)
        {
            _fixture = new GameObject("TeamHudTests", typeof(RectTransform));
            _panel = new TeamHudPanel((RectTransform)_fixture.transform);
            _panel.SetState(state, true);
            return _panel;
        }

        private UnityEngine.Transform Find(string name) => _fixture.GetComponentsInChildren<UnityEngine.Transform>(true).FirstOrDefault(t => t.name == name);
        private string Text(string name) => Find(name).GetComponent<TMP_Text>().text;
        private Button Button(string name) => Find(name).GetComponent<Button>();
        private static TeamUiState State(TeamSnapshot snapshot)
        {
            var state = new TeamUiState(() => 0);
            state.SetSnapshot(snapshot);
            return state;
        }

        private static TeamSnapshot Snapshot() => new TeamSnapshot
        {
            TeamId = 100, LeaderId = 11, LocalPlayerId = 11, Capacity = 5, MembershipEpoch = 1, Version = 2,
            Members = new System.Collections.Generic.List<TeamRole>
            {
                new TeamRole { PlayerId = 11, JoinSeq = 1, Name = "", IsOnline = false },
                new TeamRole { PlayerId = 22, JoinSeq = 2, Name = "<size=90>很长很长的真实道友名称</size>", Level = 28, ClassId = 1 }
            }
        };
    }
}
