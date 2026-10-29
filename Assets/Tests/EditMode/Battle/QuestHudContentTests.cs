using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.UI.Ugui.Gameplay;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>通过正式原生控件观察任务栏行为，不创建会话、网络客户端或奖励写入口。</summary>
    public sealed class QuestHudContentTests
    {
        private GameObject _fixture;
        private QuestHudContent _hud;
        private MissionTrackingState _tracking;
        private readonly List<(uint Scope, uint Id)> _details = new();
        private int _listRequests, _refreshRequests;

        [SetUp]
        public void SetUp()
        {
            _fixture = new GameObject("QuestHudContentTests", typeof(RectTransform), typeof(Canvas));
            ((RectTransform)_fixture.transform).sizeDelta = new Vector2(2560, 1080);
            _tracking = new MissionTrackingState();
            _hud = new QuestHudContent(_fixture.transform);
            _details.Clear(); _listRequests = _refreshRequests = 0;
            _hud.DetailsRequested += (scope, id) => _details.Add((scope, id));
            _hud.ListRequested += () => ++_listRequests;
            _hud.RefreshRequested += () => ++_refreshRequests;
        }

        [TearDown]
        public void TearDown()
        {
            _hud?.Dispose();
            if (_fixture != null) Object.DestroyImmediate(_fixture);
            _hud = null;
        }

        [Test]
        public void MoreThanFourMissionsRemainReachableThroughScrollAndPageControls()
        {
            var snapshot = Missions(8);
            Set(snapshot);
            Assert.That(Cards().Length, Is.EqualTo(8));
            var scroll = Scroll();
            Assert.That(scroll.vertical, Is.True);
            Assert.That(scroll.horizontal, Is.False);
            Assert.That(scroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            Assert.That(Cards().Take(4).All(card => -card.anchoredPosition.y + card.rect.height <= scroll.viewport.rect.height + 1),
                Is.True, "紧凑单目标任务应完整呈现首屏四卡");
            var original = snapshot.Clone();

            for (int i = 0; i < 20; ++i) Click("QuestScrollDown");
            Canvas.ForceUpdateCanvases();
            float maxOffset = scroll.content.rect.height - scroll.viewport.rect.height;
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(maxOffset).Within(1));
            var last = Card(snapshot.Missions.Last());
            float lastBottom = -last.anchoredPosition.y + last.rect.height;
            Assert.That(lastBottom - scroll.content.anchoredPosition.y,
                Is.LessThanOrEqualTo(scroll.viewport.rect.height + 1), "末卡必须可进入可视区");

            for (int i = 0; i < 20; ++i) Click("QuestScrollUp");
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(0).Within(.1));
            Assert.That(snapshot.Equals(original), Is.True, "翻页不得改变权威任务进度");
            Assert.That(_refreshRequests, Is.Zero);
        }

        [Test]
        public void DuplicateConditionIdsKeepEveryExplicitObjectiveAndLongTextReachable()
        {
            var snapshot = Missions(1);
            var mission = snapshot.Missions[0];
            mission.Objectives.Clear();
            for (uint i = 0; i < 7; ++i)
                mission.Objectives.Add(new MissionObjectiveInfo
                {
                    ObjectiveIndex = i * 3, ConditionId = 12, Progress = i, Target = 20,
                    Completed = i == 0,
                    Description = "不同目标" + i + "：这是需要换行且仍须完整查看的真实任务说明文字。长说明继续记录后续交互条件与服务器确认要求，不能被卡片永久截断。"
                });
            Set(snapshot);
            Click("QuestExpand_" + MissionTrackingState.Key(mission));

            foreach (var objective in mission.Objectives)
            {
                var text = TextNamed($"QuestObjective_{mission.Scope}_{mission.MissionId}_{objective.ObjectiveIndex}");
                Assert.That(text.text, Does.Contain(objective.Description));
                Assert.That(text.text, Does.Contain($"{objective.Progress}/{objective.Target}"));
                Assert.That(text.textWrappingMode, Is.EqualTo(TextWrappingModes.Normal));
                Assert.That(text.rectTransform.rect.height, Is.GreaterThanOrEqualTo(26));
            }
            AssertText("目标 7 项 · 已达成 1 项");
            Assert.That(Scroll().content.rect.height, Is.GreaterThan(Scroll().viewport.rect.height));
            Click("QuestScrollDown");
            Assert.That(Scroll().content.anchoredPosition.y, Is.GreaterThan(0));
            Assert.That(_details, Is.Empty, "展开目标不应触发详情或其他动作");
        }

        [Test]
        public void CompactCardStartsWithFirstUnfinishedObjectiveAndKeepsCompletionSummary()
        {
            var snapshot = Missions(1);
            var mission = snapshot.Missions[0];
            mission.Objectives[0].Completed = true;
            mission.Objectives.Add(new MissionObjectiveInfo
            { ObjectiveIndex = 9, ConditionId = 12, Description = "尚需完成的后续目标", Progress = 1, Target = 6 });
            Set(snapshot);
            AssertText("尚需完成的后续目标");
            AssertText("目标 2 项 · 已达成 1 项");
            Assert.That(Find<TextMeshProUGUI>($"QuestObjective_0_1_0"), Is.Null);
            Click("QuestExpand_" + MissionTrackingState.Key(mission));
            Assert.That(TextNamed("QuestObjective_0_1_0").text, Does.Contain("已达成"));
        }

        [Test]
        public void UnknownTargetAndMissingMetadataNeverInventCountsOrNavigation()
        {
            var snapshot = Missions(1);
            var mission = snapshot.Missions[0];
            mission.Name = ""; mission.MissionType = 99;
            mission.Objectives.Clear();
            mission.Objectives.Add(new MissionObjectiveInfo { ObjectiveIndex = 6, Target = 0 });
            Set(snapshot);
            Assert.That(TextNamed("QuestTitle").text, Is.EqualTo("任务 · 1"));
            Assert.That(TextNamed("QuestCategory").text, Is.EqualTo("类型 99"));
            Assert.That(TextNamed("QuestObjective_0_1_6").text, Does.Contain("数量待确认"));
            Assert.That(AllText().Any(text => text.Contains("0/0")), Is.False);
            AssertText("暂无定位信息");
            Assert.That(_hud.Root.GetComponentsInChildren<Button>(true)
                .Any(button => button.GetComponentsInChildren<TMP_Text>(true).Any(text => text.text.Contains("前往"))), Is.False);
        }

        [Test]
        public void FailedMissionShowsAuthoritativeReasonAndCannotBeTracked()
        {
            var snapshot = Missions(1);
            var mission = snapshot.Missions[0];
            mission.Status = (PlayerMissionStatus)4;
            mission.UnavailableReason = "服务器确认：任务有效期已结束";
            Set(snapshot);
            Assert.That(TextNamed("QuestFailure").text, Is.EqualTo(mission.UnavailableReason));
            Assert.That(ButtonNamed("QuestTrack_" + MissionTrackingState.Key(mission)).interactable, Is.False);
            Assert.That(_tracking.Track(mission), Is.False);
            Assert.That(_tracking.TrackedKeys, Is.Empty);
            Click("QuestDetails_" + MissionTrackingState.Key(mission));
            CollectionAssert.AreEqual(new[] { (mission.Scope, mission.MissionId) }, _details);
        }

        [Test]
        public void ClaimableCardOnlyRequestsDetailsAndKeepsRewardCapabilityUntouched()
        {
            var snapshot = Missions(1);
            var mission = snapshot.Missions[0];
            mission.Status = (PlayerMissionStatus)3; mission.CanClaim = true;
            Set(snapshot);
            var original = snapshot.Clone();
            var details = ButtonNamed("QuestDetails_" + MissionTrackingState.Key(mission));
            Assert.That(details.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("可领取"));
            details.onClick.Invoke();
            CollectionAssert.AreEqual(new[] { (mission.Scope, mission.MissionId) }, _details);
            Assert.That(snapshot.Equals(original), Is.True);
            Assert.That(mission.CanClaim, Is.True);
            Assert.That(_tracking.TrackedKeys, Is.Empty);
            Assert.That(_refreshRequests + _listRequests, Is.Zero,
                "任务栏只暴露详情意图；领奖仍由原大窗口的正式能力提交");
        }

        [Test]
        public void CardAndDetailsButtonPreserveScopeWhenMissionIdsAreEqual()
        {
            var snapshot = Missions(1);
            var otherScope = snapshot.Missions[0].Clone();
            otherScope.Scope = 2; otherScope.Name = "另一归属的同编号任务";
            snapshot.Missions.Add(otherScope);
            Set(snapshot);
            Click("QuestDetails_" + MissionTrackingState.Key(otherScope));
            var cardHit = Card(snapshot.Missions[0]).GetComponentInChildren<Button>();
            cardHit.onClick.Invoke();
            CollectionAssert.AreEqual(new[] { (2u, 1u), (0u, 1u) }, _details);
        }

        [Test]
        public void SharedTrackingUpdatesHudAndBigWindowWithoutDroppingOtherManualSelections()
        {
            var snapshot = Missions(2);
            Set(snapshot);
            var preferences = new Dictionary<string, string>();
            using var window = new GameplayWindow(_fixture.transform, activityPreferences: new ActivityPreferences(
                key => preferences.TryGetValue(key, out var value) ? value : "", (key, value) => preferences[key] = value));
            window.SetMissionTracking(_tracking);
            window.SetMissions(snapshot, false, null);
            window.ShowMission(0, 1);
            var first = snapshot.Missions[0]; var second = snapshot.Missions[1];
            Click("QuestTrack_" + MissionTrackingState.Key(first));
            Click("QuestTrack_" + MissionTrackingState.Key(second));
            Assert.That(_tracking.TrackedKeys.Count, Is.EqualTo(2));
            Assert.That(WindowButton("TrackMission").GetComponentInChildren<TMP_Text>().text,
                Is.EqualTo("取消追踪"), "HUD手选必须立即反映到大窗当前任务");

            // 大窗仍定位第一任务；只取消该身份，HUD第二任务应保持手选。
            WindowButton("TrackMission").onClick.Invoke();
            Assert.That(_tracking.IsTracked(MissionTrackingState.Key(first)), Is.False);
            Assert.That(_tracking.IsTracked(MissionTrackingState.Key(second)), Is.True);
            Assert.That(ButtonNamed("QuestTrack_" + MissionTrackingState.Key(first)).GetComponentInChildren<TMP_Text>().text,
                Is.EqualTo("追踪"));
            Assert.That(ButtonNamed("QuestTrack_" + MissionTrackingState.Key(second)).GetComponentInChildren<TMP_Text>().text,
                Is.EqualTo("取消追踪"));
        }

        [Test]
        public void BigWindowMeasuresMultilineObjectivesAndKeepsTheLastLineScrollable()
        {
            var snapshot = Missions(1);
            var mission = snapshot.Missions[0];
            const string multiline = "第一段目标说明\n第二段确认条件\n第三段操作要求\n第四段完成说明\n第五段仍须完整查看";
            const string longText = "后续目标需要仔细核对所有条件，长段中文必须根据实际字体与可用宽度换行。重复条件编号不代表同一目标，因此这段目标仍须独立展示、独立记录，并且最后一行不能藏在下一个目标之后。";
            const string lastLine = "最后一个目标及其尾行";
            mission.Objectives.Clear();
            mission.Objectives.Add(new MissionObjectiveInfo { ObjectiveIndex = 0, ConditionId = 12, Description = multiline, Target = 10 });
            mission.Objectives.Add(new MissionObjectiveInfo { ObjectiveIndex = 7, ConditionId = 12, Description = longText, Target = 10 });
            mission.Objectives.Add(new MissionObjectiveInfo { ObjectiveIndex = 19, ConditionId = 12, Description = lastLine, Target = 10 });
            using var window = CreateWindow();
            window.SetMissions(snapshot, false, null);
            window.ShowMission(0, 1);
            Canvas.ForceUpdateCanvases();
            var scroll = WindowScroll("MissionObjectivesScroll");
            var labels = new[] { multiline, longText, lastLine }.Select(value => scroll.content
                .GetComponentsInChildren<TMP_Text>(true).Single(text => text.text == value)).ToArray();
            float previousBottom = 0;
            foreach (var text in labels)
            {
                float needed = text.GetPreferredValues(text.text, text.rectTransform.rect.width, float.PositiveInfinity).y;
                Assert.That(text.rectTransform.rect.height, Is.GreaterThanOrEqualTo(needed), "完整文字应在目标自己的高度内");
                float top = -text.rectTransform.anchoredPosition.y;
                Assert.That(top, Is.GreaterThanOrEqualTo(previousBottom - .1f), "多换行目标不得覆盖后续目标");
                previousBottom = top + text.rectTransform.rect.height;
            }
            Assert.That(scroll.content.rect.height, Is.GreaterThanOrEqualTo(previousBottom));
            scroll.verticalNormalizedPosition = 0;
            Canvas.ForceUpdateCanvases();
            Assert.That(previousBottom - scroll.content.anchoredPosition.y,
                Is.LessThanOrEqualTo(scroll.viewport.rect.height + 1), "末项目标尾行必须能滚入视口");

            // 通过ScrollRect公开滚动事件模拟用户已滚到末尾，再应用一次权威进度刷新。
            scroll.onValueChanged.Invoke(Vector2.zero);
            var updated = snapshot.Clone(); updated.Missions[0].Objectives[2].Progress = 2;
            window.SetMissions(updated, false, null);
            Canvas.ForceUpdateCanvases();
            Assert.That(WindowScroll("MissionObjectivesScroll").verticalNormalizedPosition, Is.EqualTo(0).Within(.01f),
                "同一任务进度刷新应保留玩家正在查看的目标位置");
        }

        [Test]
        public void BigWindowLocatesAcceptedMissionUsingTheSameFilteredListAsItsRows()
        {
            var snapshot = Missions(30);
            for (int i = 0; i < 20; ++i) snapshot.Missions[i].Status = (PlayerMissionStatus)0;
            using var window = CreateWindow();
            window.SetMissions(snapshot, false, null);
            window.ShowMission(0, 21);
            Canvas.ForceUpdateCanvases();
            var scroll = WindowScroll("MissionListScroll");
            Assert.That(scroll.content.GetComponentsInChildren<Button>(true).Count(button => button.name.StartsWith("Mission_")), Is.EqualTo(10));
            var selectedRow = scroll.content.GetComponentsInChildren<Button>(true).Single(button => button.name == "Mission_0_21");
            Assert.That(selectedRow.GetComponentsInChildren<UnityEngine.Transform>(true).Any(child => child.name == "MissionSelectionMark"), Is.True);
            var row = (RectTransform)selectedRow.transform;
            float top = -row.anchoredPosition.y - scroll.content.anchoredPosition.y;
            Assert.That(top, Is.GreaterThanOrEqualTo(-1), "首条已接任务不能因前面二十条未接任务而滚出上边界");
            Assert.That(top + row.rect.height, Is.LessThanOrEqualTo(scroll.viewport.rect.height + 1));
            Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1).Within(.01f));

            // 反向打开可接任务时同样定位该身份，不能仍显示已接列表。
            window.ShowMission(0, 1);
            Canvas.ForceUpdateCanvases();
            scroll = WindowScroll("MissionListScroll");
            var available = scroll.content.GetComponentsInChildren<Button>(true).Single(button => button.name == "Mission_0_1");
            Assert.That(available.GetComponentsInChildren<UnityEngine.Transform>(true).Any(child => child.name == "MissionSelectionMark"), Is.True);
            Assert.That(scroll.content.GetComponentsInChildren<Button>(true).Count(button => button.name.StartsWith("Mission_")), Is.EqualTo(20));
        }

        [Test]
        public void UnsynchronizedLoadingFailureStaleAndLoadedEmptyStatesRemainDistinct()
        {
            _hud.SetData(null, false, null, false, _tracking);
            AssertText("任务尚未同步"); AssertText("等待任务同步");
            _hud.SetData(null, true, null, false, _tracking);
            AssertText("正在同步任务");
            Assert.That(ButtonNamed("QuestRefresh").interactable, Is.False);
            _hud.SetData(null, false, "读取失败", false, _tracking);
            AssertText("同步失败，请刷新重试");
            Assert.That(ButtonNamed("QuestRefresh").interactable, Is.True);
            Set(new GetMissionListResponse());
            AssertText("暂无进行中的任务");

            var snapshot = Missions(1);
            Set(snapshot);
            _hud.SetData(snapshot, false, "网络超时", false, _tracking);
            AssertText("同步失败 · 显示上次记录，请刷新");
            Assert.That(Cards().Length, Is.EqualTo(1));
            AssertText(snapshot.Missions[0].Name);
            _hud.SetData(snapshot, true, null, false, _tracking);
            AssertText("正在更新任务");
            Assert.That(Cards().Length, Is.EqualTo(1));
        }

        [Test]
        public void EqualSnapshotsAndOnlyLoadingChangesPreserveCardsAndScrollPosition()
        {
            var snapshot = Missions(7);
            Set(snapshot);
            Click("QuestScrollDown");
            var card = Card(snapshot.Missions[0]);
            var position = Scroll().content.anchoredPosition;
            _hud.SetData(snapshot.Clone(), true, null, false, _tracking);
            Assert.That(Card(snapshot.Missions[0]), Is.SameAs(card), "同值快照不应重建卡树");
            Assert.That(Scroll().content.anchoredPosition, Is.EqualTo(position));
            Assert.That(ButtonNamed("QuestRefresh").interactable, Is.False);
        }

        [Test]
        public void RebindingTrackingDetachesOldModelAndDisposalRemovesOwnedControls()
        {
            var snapshot = Missions(2);
            Set(snapshot);
            var previous = _tracking;
            var replacement = new MissionTrackingState();
            replacement.UpdateSnapshot(snapshot);
            _hud.SetData(snapshot, false, null, false, replacement);
            var card = Card(snapshot.Missions[0]);
            previous.Track(snapshot.Missions[0]);
            Assert.That(Card(snapshot.Missions[0]), Is.SameAs(card), "旧主体偏好不得重画当前内容");
            replacement.Track(snapshot.Missions[0]);
            Assert.That(ButtonNamed("QuestTrack_" + MissionTrackingState.Key(snapshot.Missions[0]))
                .GetComponentInChildren<TMP_Text>().text, Is.EqualTo("取消追踪"));

            var root = _hud.Root;
            _hud.Dispose();
            Assert.That(root == null, Is.True);
            Assert.DoesNotThrow(() => replacement.Track(snapshot.Missions[1]));
            Assert.DoesNotThrow(() => _hud.SetData(snapshot, false, null, false, replacement));
            Assert.DoesNotThrow(() => _hud.Dispose());
            Assert.That(_fixture.transform.childCount, Is.Zero);
        }

        [Test]
        public void ListAndRefreshRemainExplicitAndAutoTrackingExplainsUnavailablePolicy()
        {
            Set(new GetMissionListResponse());
            Click("QuestList"); Click("QuestRefresh");
            Assert.That(_listRequests, Is.EqualTo(1));
            Assert.That(_refreshRequests, Is.EqualTo(1));
            Assert.That(ButtonNamed("QuestAutoTrackingUnavailable").interactable, Is.False);
            AssertText("自动选择规则待确认");
            Assert.That(_tracking.TrackedKeys, Is.Empty);
        }

        [Test]
        public void NotAcceptedAndRewardedCompletedTasksNeverBecomeActiveCards()
        {
            var snapshot = Missions(4);
            snapshot.Missions[0].Status = (PlayerMissionStatus)0;
            snapshot.Missions[1].Status = (PlayerMissionStatus)2;
            snapshot.Missions[2].Status = (PlayerMissionStatus)3;
            snapshot.Missions[3].Status = (PlayerMissionStatus)4;
            Set(snapshot);
            CollectionAssert.AreEquivalent(new[] { "QuestCard_0_3", "QuestCard_0_4" }, Cards().Select(card => card.name));
            Assert.That(_tracking.TrackedKeys, Is.Empty);
        }

        private void Set(GetMissionListResponse snapshot)
        {
            _tracking.UpdateSnapshot(snapshot);
            _hud.SetData(snapshot, false, null, false, _tracking);
            Canvas.ForceUpdateCanvases();
        }

        private static GetMissionListResponse Missions(int count)
        {
            var snapshot = new GetMissionListResponse { StatePersistent = true };
            for (uint id = 1; id <= count; ++id)
            {
                var mission = new PlayerMissionInfo
                { Scope = 0, MissionId = id, MissionType = 1, Name = "测试任务" + id,
                    Status = (PlayerMissionStatus)1, Configured = true };
                mission.Objectives.Add(new MissionObjectiveInfo
                { ObjectiveIndex = 0, ConditionId = 12, Description = "目标说明" + id, Progress = id, Target = 10 });
                snapshot.Missions.Add(mission);
            }
            return snapshot;
        }

        private T Find<T>(string name) where T : Component =>
            _hud.Root.GetComponentsInChildren<T>(true).FirstOrDefault(item => item.name == name);
        private Button ButtonNamed(string name)
        {
            var button = Find<Button>(name);
            Assert.That(button, Is.Not.Null, "缺少按钮 " + name);
            return button;
        }
        private Button WindowButton(string name) => _fixture.GetComponentsInChildren<Button>(true).Single(button => button.name == name);
        private ScrollRect WindowScroll(string name) => _fixture.GetComponentsInChildren<ScrollRect>(true).Single(scroll => scroll.name == name);
        private GameplayWindow CreateWindow()
        {
            var preferences = new Dictionary<string, string>();
            return new GameplayWindow(_fixture.transform, activityPreferences: new ActivityPreferences(
                key => preferences.TryGetValue(key, out var value) ? value : "", (key, value) => preferences[key] = value));
        }
        private void Click(string name)
        {
            var button = ButtonNamed(name);
            Assert.That(button.interactable, Is.True, "按钮不可交互 " + name);
            button.onClick.Invoke();
        }
        private TMP_Text TextNamed(string name)
        {
            var text = Find<TMP_Text>(name);
            Assert.That(text, Is.Not.Null, "缺少文本 " + name);
            return text;
        }
        private IEnumerable<string> AllText() => _hud.Root.GetComponentsInChildren<TMP_Text>(true).Select(text => text.text);
        private void AssertText(string value) => Assert.That(AllText().Any(text => text.Contains(value)), Is.True, "缺少文案 " + value);
        private ScrollRect Scroll() => _hud.Root.GetComponentInChildren<ScrollRect>();
        private RectTransform[] Cards() => Scroll().content.Cast<UnityEngine.Transform>().Where(item => item.name.StartsWith("QuestCard_"))
            .Select(item => (RectTransform)item).ToArray();
        private RectTransform Card(PlayerMissionInfo mission) => Cards().Single(card => card.name == $"QuestCard_{mission.Scope}_{mission.MissionId}");
    }
}
