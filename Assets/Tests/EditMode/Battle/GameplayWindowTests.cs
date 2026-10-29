using System.Collections.Generic;
using System.Linq;
using MmorpgClient.UI.Ugui.Gameplay;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>使用正式窗口和原生控件验证展示交互，不建立账号或网络会话。</summary>
    public sealed class GameplayWindowTests
    {
        private GameObject _fixture;
        private GameplayWindow _window;
        private int _bagReads, _missionReads, _activityReads, _sorts;
        private readonly List<PlayerMissionInfo> _tracking = new();
        private readonly List<(uint Scope, uint Id)> _accepts = new(), _claims = new();

        [SetUp]
        public void SetUp()
        {
            _fixture = new GameObject("GameplayWindowTests", typeof(RectTransform), typeof(Canvas));
            ((RectTransform)_fixture.transform).sizeDelta = new Vector2(2560, 1080);
            var preferences = new Dictionary<string, string>();
            _window = new GameplayWindow(_fixture.transform, activityPreferences: new ActivityPreferences(
                key => preferences.TryGetValue(key, out var value) ? value : string.Empty, (key, value) => preferences[key] = value));
            _bagReads = _missionReads = _activityReads = _sorts = 0;
            _tracking.Clear(); _accepts.Clear(); _claims.Clear();
            _window.BagRequested += _ => ++_bagReads;
            _window.MissionsRequested += () => ++_missionReads;
            _window.ActivitiesRequested += () => ++_activityReads;
            _window.SortRequested += () => ++_sorts;
            _window.TrackingChanged += _tracking.Add;
            _window.MissionAcceptRequested += (scope, id) => _accepts.Add((scope, id));
            _window.MissionClaimRequested += (scope, id) => _claims.Add((scope, id));
        }

        [TearDown]
        public void TearDown()
        {
            if (_fixture != null) Object.DestroyImmediate(_fixture);
            _window = null;
        }

        [Test]
        public void SearchAndCategoriesOnlyFilterPresentationWithoutSortingOrChangingSlots()
        {
            var bag = MakeBag();
            var original = bag.Clone();
            _window.SetBag(bag, false, null, false);
            _window.Show(GameplayPage.Bag);
            Assert.That(_bagReads, Is.EqualTo(1));
            CollectionAssert.AreEquivalent(new[] { "BagSlot_0", "BagSlot_9" }, OccupiedSlots());

            Search().text = "202";
            CollectionAssert.AreEqual(new[] { "BagSlot_9" }, OccupiedSlots());
            ClickNamed("BagTools"); Click("其他物品");
            Assert.That(OccupiedSlots(), Is.Empty);
            AssertText("没有找到符合条件的物品");
            Search().text = "";
            CollectionAssert.AreEqual(new[] { "BagSlot_0" }, OccupiedSlots());
            Click("装备");
            CollectionAssert.AreEqual(new[] { "BagSlot_9" }, OccupiedSlots());
            Click("全部");
            Search().text = "回春";
            CollectionAssert.AreEqual(new[] { "BagSlot_0" }, OccupiedSlots());

            Assert.That(_bagReads, Is.EqualTo(1), "搜索和分类不应重新读取或隐式整理背包");
            Assert.That(_sorts, Is.Zero);
            Assert.That(bag.Equals(original), Is.True, "筛选不得压缩服务器槽位或改写物品实例");
        }

        [Test]
        public void ArrangeClickEmitsOneExplicitRequestAndBusyStateDisablesButton()
        {
            var bag = MakeBag();
            _window.SetBag(bag, false, null, false);
            _window.Show(GameplayPage.Bag);
            _window.SortRequested += () => _window.SetBag(bag, false, null, true);
            Assert.That(_sorts, Is.Zero);
            Click("整理");
            Assert.That(_sorts, Is.EqualTo(1));
            Assert.That(FindButton("整理中").interactable, Is.False);
            Assert.That(FindButton("刷新").interactable, Is.False);
            Assert.That(_bagReads, Is.EqualTo(1));
            CollectionAssert.AreEquivalent(new[] { "BagSlot_0", "BagSlot_9" }, OccupiedSlots());
        }

        [Test]
        public void ResetSessionClearsSearchFiltersAndTrackedMissionBeforeAnotherCharacter()
        {
            var missions = MakeMissions(2);
            missions.Missions[1].Status = (PlayerMissionStatus)2;
            var activities = MakeActivities(2);
            activities.Activities[1].Status = (PlayerActivityStatus)2;
            _window.SetMissions(missions, false, null);
            _window.Show(GameplayPage.Missions);
            ClickNamed("TrackMission");
            Assert.That(_tracking.Last().MissionId, Is.EqualTo(1));
            Click("筛选·全部任务");
            Click("已达成");
            _window.SetActivities(activities, false, null);
            _window.Show(GameplayPage.Activities);
            ClickNamed("ActivityTools"); Click("进行中");
            _window.SetBag(MakeBag(), false, null, false);
            _window.Show(GameplayPage.Bag);
            Click("装备");
            Search().text = "不会匹配的新角色搜索";

            _window.ResetSession();
            Assert.That(_window.IsVisible, Is.False);
            Assert.That(_tracking.Last(), Is.Null);
            Assert.That(Search().text, Is.Empty);

            _window.SetBag(MakeBag(), false, null, false);
            _window.Show(GameplayPage.Bag);
            CollectionAssert.AreEquivalent(new[] { "BagSlot_0", "BagSlot_9" }, OccupiedSlots());
            _window.SetMissions(missions, false, null);
            _window.Show(GameplayPage.Missions);
            AssertText("任务一");
            AssertText("任务二");
            Assert.That(SelectedMissionTracked(), Is.False);
            _window.SetActivities(activities, false, null);
            _window.Show(GameplayPage.Activities);
            AssertText("雅集一");
            AssertText("雅集二");
            Assert.That(_sorts, Is.Zero);
        }

        [Test]
        public void EveryMissionObjectiveIsReachableThroughTheNativeScrollViewport()
        {
            var missions = MakeMissions(1);
            missions.Missions[0].Objectives.Clear();
            for (uint i = 0; i < 7; ++i)
                missions.Missions[0].Objectives.Add(new MissionObjectiveInfo { ObjectiveIndex = i, Description = "目标内容" + (i + 1), Progress = i, Target = 10 });
            _window.SetMissions(missions, false, null); _window.Show(GameplayPage.Missions);
            for (int i = 1; i <= 7; ++i) AssertText("目标内容" + i);
            var scroll = Scroll("MissionObjectivesScroll");
            Assert.That(scroll.vertical, Is.True); Assert.That(scroll.horizontal, Is.False);
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            scroll.verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases();
            Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(0), "最后一项目标必须可滚入可视区");
            Assert.That(_missionReads, Is.EqualTo(1)); Assert.That(_tracking, Is.Empty);
        }

        [Test]
        public void LongMissionListScrollsToTheLastEntryAndSelectionUsesItsRealState()
        {
            _window.SetMissions(MakeMissions(7), false, null); _window.Show(GameplayPage.Missions);
            AssertText("任务说明1"); var scroll = Scroll("MissionListScroll");
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            scroll.verticalNormalizedPosition = 0; scroll.onValueChanged.Invoke(new Vector2(0, 0)); Canvas.ForceUpdateCanvases();
            Click("任务七"); AssertText("任务说明7"); Assert.That(HasText("任务说明1"), Is.False);
            Assert.That(Scroll("MissionListScroll").verticalNormalizedPosition, Is.LessThan(.01f));
            Assert.That(_missionReads, Is.EqualTo(1), "滚动与详情选择不得发送额外RPC");
            Assert.That(_accepts, Is.Empty); Assert.That(_claims, Is.Empty);
        }

        [Test]
        public void LongActivityListKeepsApprovedCardsAndScrollsHorizontallyWithoutFixedBusinessLimits()
        {
            _window.SetActivities(MakeActivities(7), false, null); _window.Show(GameplayPage.Activities);
            for (int i = 0; i < 7; ++i) AssertText("雅集" + Numbers[i]);
            var scroll = Scroll("ActivityCardsScroll");
            Assert.That(scroll.horizontal, Is.True); Assert.That(scroll.vertical, Is.False);
            Assert.That(scroll.content.rect.width, Is.GreaterThan(scroll.viewport.rect.width));
            scroll.horizontalNormalizedPosition = 1; scroll.onValueChanged.Invoke(new Vector2(1, 0)); Canvas.ForceUpdateCanvases();
            Assert.That(scroll.content.anchoredPosition.x, Is.LessThan(0), "第七项活动必须可滚入可视区");
            var first = _fixture.GetComponentsInChildren<RectTransform>().Single(r => r.name == "Activity_1");
            var second = _fixture.GetComponentsInChildren<RectTransform>().Single(r => r.name == "Activity_2");
            var last = _fixture.GetComponentsInChildren<RectTransform>().Single(r => r.name == "Activity_7");
            Assert.That(first.sizeDelta, Is.EqualTo(new Vector2(410,495)), "批准的完整竖卡不得改成压缩列表");
            Assert.That(first.anchoredPosition.y, Is.EqualTo(second.anchoredPosition.y));
            Assert.That(second.anchoredPosition.x - first.anchoredPosition.x, Is.EqualTo(418));
            var corners = new UnityEngine.Vector3[4]; last.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                var point = scroll.viewport.InverseTransformPoint(corner);
                Assert.That(point.x, Is.InRange(scroll.viewport.rect.xMin - 1, scroll.viewport.rect.xMax + 1));
                Assert.That(point.y, Is.InRange(scroll.viewport.rect.yMin - 1, scroll.viewport.rect.yMax + 1));
            }
            Assert.That(_activityReads, Is.EqualTo(1)); Assert.That(_accepts, Is.Empty);
        }

        [Test]
        public void SameMissionIdInDifferentScopesHasIndependentDetailsAndTracking()
        {
            var missions = MakeMissions(1);
            var scoped = missions.Missions[0].Clone();
            scoped.Scope = 5;
            scoped.Name = "副本任务";
            scoped.Description = "副本中的独立进度";
            scoped.Objectives[0].Progress = 2;
            missions.Missions.Add(scoped);
            _window.SetMissions(missions, false, null);
            _window.Show(GameplayPage.Missions);
            AssertText("任务说明1");
            Click("副本任务");
            AssertText("副本中的独立进度");
            Assert.That(HasText("任务说明1"), Is.False);
            ClickNamed("TrackMission");
            Assert.That(_tracking.Last().MissionId, Is.EqualTo(1));
            Assert.That(_tracking.Last().Scope, Is.EqualTo(5));
            Click("任务一");
            AssertText("任务说明1");
            Assert.That(SelectedMissionTracked(), Is.False, "同配置不同scope不能共享追踪按钮状态");
            Click("副本任务");
            Assert.That(SelectedMissionTracked(), Is.True);

            var onlyBaseScope = new GetMissionListResponse();
            onlyBaseScope.Missions.Add(missions.Missions[0].Clone());
            _window.SetMissions(onlyBaseScope, false, null);
            Assert.That(_tracking.Last(), Is.Null, "移除被追踪scope时，同ID的另一scope不能替代它");
            AssertText("任务说明1");
            Assert.That(SelectedMissionTracked(), Is.False);
        }
        [Test]
        public void RemovingTrackedMissionClearsTrackingAndDoesNotReviveItWhenItReturns()
        {
            var missions = MakeMissions(2);
            _window.SetMissions(missions, false, null);
            _window.Show(GameplayPage.Missions);
            ClickNamed("TrackMission");
            Assert.That(_tracking.Last().MissionId, Is.EqualTo(1));
            Assert.That(SelectedMissionTracked(), Is.True);

            var remaining = new GetMissionListResponse();
            remaining.Missions.Add(missions.Missions[1].Clone());
            _window.SetMissions(remaining, false, null);
            Assert.That(_tracking.Last(), Is.Null);
            Assert.That(SelectedMissionTracked(), Is.False);
            _window.SetMissions(missions, false, null);
            Click("任务一");
            Assert.That(SelectedMissionTracked(), Is.False);
            Assert.That(_fixture.GetComponentsInChildren<Image>().Any(i => i.name == "check"), Is.False);
            Assert.That(_tracking.Count, Is.EqualTo(2));
        }

        [Test]
        public void LoadingErrorAndEmptyStatesRemainReadableAndRefreshIsExplicit()
        {
            _window.SetBag(null, true, null, false);
            _window.Show(GameplayPage.Bag);
            AssertText("正在打开行囊…");
            Assert.That(FindButton("刷新").interactable, Is.False);
            var empty = new BagInfo { Layout = new BagLayoutInfo { Capacity = 28 } };
            _window.SetBag(empty, false, null, false);
            AssertText("行囊空空，沿途拾得的灵物会收在这里");
            _window.SetBag(empty, false, "请求超时", false);
            AssertText("暂时未能同步，请点击刷新重试");
            Assert.That(HasText("行囊空空，沿途拾得的灵物会收在这里"), Is.False);
            Assert.That(FindButton("整理").interactable, Is.False);
            Click("刷新");
            Assert.That(_bagReads, Is.EqualTo(2));
            Assert.That(_sorts, Is.Zero);

            _window.SetMissions(new GetMissionListResponse(), false, null);
            _window.Show(GameplayPage.Missions);
            AssertText("这页手札还没有记录");
            Assert.That(Scroll(_window.Page == GameplayPage.Missions ? "MissionListScroll" : "ActivityCardsScroll").content.childCount, Is.Zero);
            _window.SetActivities(new GetActivityListResponse(), false, null);
            _window.Show(GameplayPage.Activities);
            AssertText("没有符合筛选的活动，可清除筛选查看");
            Assert.That(Scroll(_window.Page == GameplayPage.Missions ? "MissionListScroll" : "ActivityCardsScroll").content.childCount, Is.Zero);
        }

        [Test]
        public void MissionAcceptIsExplicitUsesScopeAndBusyDisablesActionAndRefresh()
        {
            var missions = MakeMissions(1);
            var mission = missions.Missions[0];
            mission.Scope = 5; mission.Status = (PlayerMissionStatus)0; mission.CanAccept = true;
            _window.SetMissions(missions, false, null);
            _window.Show(GameplayPage.Missions);
            Assert.That(_accepts, Is.Empty, "显示或刷新不得自动接取任务");
            _window.MissionAcceptRequested += (_, __) => _window.SetMissions(missions, false, null, true);
            Click("接取任务");
            Assert.That(_accepts.Single(), Is.EqualTo((5u, 1u)));
            Assert.That(FindButton("处理中…").interactable, Is.False);
            Assert.That(FindButton("刷新任务").interactable, Is.False);
            AssertText("正在处理任务，请稍候…");
            Assert.That(_claims, Is.Empty);
            Assert.That(_tracking, Is.Empty);
        }

        [Test]
        public void ClaimButtonFollowsServerCapabilityAndRendersActionFailure()
        {
            var missions = MakeMissions(1);
            var mission = missions.Missions[0];
            mission.Scope = 7; mission.Status = (PlayerMissionStatus)3;
            mission.UnavailableReason = "行囊已满，请先整理";
            _window.SetMissions(missions, false, null);
            _window.Show(GameplayPage.Missions);
            Assert.That(FindButton("领取奖励").interactable, Is.False);
            AssertText("行囊已满，请先整理");
            mission.CanClaim = true; mission.UnavailableReason = "";
            _window.SetMissions(missions, false, null);
            Click("领取奖励");
            Assert.That(_claims.Single(), Is.EqualTo((7u, 1u)));
            Assert.That(_accepts, Is.Empty);
            _window.SetMissions(missions, false, "领取奖励失败（错误码 7）");
            AssertText("领取奖励失败（错误码 7）");
            Assert.That(FindButton("领取奖励").interactable, Is.True);
        }

        [Test]
        public void MissingAcceptCapabilityKeepsButtonDisabledWithoutHidingReason()
        {
            var missions = MakeMissions(1);
            missions.Missions[0].Status = (PlayerMissionStatus)0;
            missions.Missions[0].UnavailableReason = "请先完成前置任务";
            _window.SetMissions(missions, false, null);
            _window.Show(GameplayPage.Missions);
            Assert.That(FindButton("接取任务").interactable, Is.False);
            AssertText("请先完成前置任务");
            Assert.That(_accepts, Is.Empty);
        }

        [Test]
        public void ActivityParticipationUsesBaseScopeAndBusyBlocksDuplicateSubmission()
        {
            var activities = MakeActivities(1);
            activities.Activities[0].Status = (PlayerActivityStatus)2;
            activities.Activities[0].CanParticipate = true;
            _window.SetMissions(new GetMissionListResponse(), false, null);
            _window.SetActivities(activities, false, null);
            _window.Show(GameplayPage.Activities);
            Assert.That(_accepts, Is.Empty);
            Assert.That(_missionReads, Is.EqualTo(1), "活动开启时同步任务，以识别已参与状态");
            _window.MissionAcceptRequested += (_, __) => _window.SetMissions(new GetMissionListResponse(), false, null, true);
            Click("前往参与");
            Assert.That(_accepts.Single(), Is.EqualTo((0u, 1u)));
            Assert.That(FindButton("处理中…").interactable, Is.False);
            Assert.That(FindButton("刷新").interactable, Is.False);
            AssertText("正在处理任务，请稍候…");
        }

        [Test]
        public void ActivityWithExistingMissionOpensCorrectMissionPageWithoutAcceptingAgain()
        {
            _window.SetMissions(MakeMissions(7), false, null);
            var activities = MakeActivities(1);
            activities.Activities[0].MissionId = 7;
            _window.SetActivities(activities, false, null);
            _window.Show(GameplayPage.Activities);
            Click("查看任务");
            Assert.That(_window.Page, Is.EqualTo(GameplayPage.Missions));
            AssertText("任务说明7");
            AssertText("任务六");
            Assert.That(_accepts, Is.Empty);
        }

        [Test]
        public void ActivityScheduleFormatsServerTimestampsInExplicitUtcPlusEightWithoutInferringState()
        {
            var activities = MakeActivities(1);
            var activity = activities.Activities[0];
            activity.Status = (PlayerActivityStatus)1;
            activity.StartsAtMs = 86400000; activity.EndsAtMs = 172800000;
            activity.UnavailableReason = "尚未到开放时间";
            _window.SetActivities(activities, false, null);
            _window.Show(GameplayPage.Activities);
            AssertText("即将开启");
            AssertText("尚未到开放时间");
            ClickNamed("ActivityDetails_1");
            Assert.That(_fixture.GetComponentsInChildren<TMP_Text>().Any(t => t.text.Contains("开始 1970/01/02 08:00\n结束 1970/01/03 08:00 北京时间（UTC+8）")), Is.True);
            AssertText("尚未到开放时间");
            ClickNamed("CloseActivityDetails_1");
            Assert.That(FindButton("前往参与").interactable, Is.False, "不因系统日期晚于排期而自行开放");
            activity.Status = (PlayerActivityStatus)0;
            _window.SetActivities(activities, false, null);
            ClickNamed("ActivityDetails_1");
            Assert.That(_fixture.GetComponentsInChildren<TMP_Text>().Any(t => t.text.Contains("尚未排期")), Is.True);
            Assert.That(HasText("开始 1970/01/02 08:00\n结束 1970/01/03 08:00 北京时间（UTC+8）"), Is.False);
        }

        [Test]
        public void InvalidScheduleBoundariesAreReadableAndResetSessionClearsActionBusy()
        {
            var activities = MakeActivities(1);
            activities.Activities[0].Status = (PlayerActivityStatus)2;
            activities.Activities[0].EndsAtMs = ulong.MaxValue;
            _window.SetActivities(activities, false, null);
            _window.Show(GameplayPage.Activities);
            ClickNamed("ActivityDetails_1");
            Assert.That(_fixture.GetComponentsInChildren<TMP_Text>().Any(t => t.text.Contains("开始 待公布\n结束 待公布 北京时间（UTC+8）")), Is.True);
            _window.SetMissions(new GetMissionListResponse(), false, null, true);
            _window.ResetSession();
            var missions = MakeMissions(1);
            missions.Missions[0].Status = (PlayerMissionStatus)0;
            missions.Missions[0].CanAccept = true;
            _window.SetMissions(missions, false, null);
            _window.Show(GameplayPage.Missions);
            Assert.That(FindButton("接取任务").interactable, Is.True);
        }
        [Test]
        public void MissionStatusFiltersPreserveUnacceptedActiveAndCompletedStatesWithoutSendingActions()
        {
            var missions = MakeMissions(3);
            missions.Missions[0].Status = (PlayerMissionStatus)0; missions.Missions[0].CanAccept = true;
            missions.Missions[1].Status = (PlayerMissionStatus)1;
            missions.Missions[2].Status = (PlayerMissionStatus)3; missions.Missions[2].CanClaim = false;
            missions.Missions[2].UnavailableReason = "等待服务器允许领奖";
            _window.SetMissions(missions, false, null); _window.Show(GameplayPage.Missions);
            AssertText("任务二"); AssertText("任务三");
            Assert.That(HasText("任务一"), Is.False, "已接任务不能混入未接任务");
            ClickNamed("AvailableMissions"); AssertText("任务一");
            Assert.That(HasText("任务二") || HasText("任务三"), Is.False);
            ClickNamed("AcceptedMissions"); AssertText("任务二"); AssertText("任务三");
            ClickNamed("MissionFilterMenu"); ClickNamed("MissionStatusFilter_1");
            AssertText("任务二"); Assert.That(HasText("任务一"), Is.False); Assert.That(HasText("任务三"), Is.False);
            ClickNamed("TrackMission"); Assert.That(_tracking.Last().MissionId, Is.EqualTo(2));
            ClickNamed("MissionFilterMenu"); ClickNamed("MissionStatusFilter_2");
            AssertText("任务三"); AssertText("等待服务器允许领奖"); Assert.That(FindButton("领取奖励").interactable, Is.False);
            ClickNamed("AvailableMissions"); AssertText("任务一"); Assert.That(FindButton("接取任务").interactable, Is.True);
            Assert.That(_accepts, Is.Empty); Assert.That(_claims, Is.Empty); Assert.That(_missionReads, Is.EqualTo(1));
        }

        [Test]
        public void CompletedGoalsUsePaintedChecksAndAllObjectivesRemainScrollable()
        {
            var missions = MakeMissions(1);
            missions.Missions[0].Objectives[0].Completed = true;
            for (uint i = 1; i < 5; ++i)
                missions.Missions[0].Objectives.Add(new MissionObjectiveInfo { ObjectiveIndex = i, Description = "目标" + i, Target = 3, Progress = 1 });
            _window.SetMissions(missions, false, null); _window.Show(GameplayPage.Missions);
            var check = _fixture.GetComponentsInChildren<Image>().Single(i => i.name == "MissionObjectiveState_0");
            Assert.That(check.sprite, Is.SameAs(EquipUiSkin.Load("checkbox_on")));
            var scroll = Scroll("MissionObjectivesScroll");
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            scroll.verticalNormalizedPosition = 0;
            Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(0));
            AssertText("目标4"); Assert.That(_accepts, Is.Empty); Assert.That(_claims, Is.Empty);
        }

        [Test]
        public void ApprovedPrimaryNavigationAndUnavailableCapabilitiesArePreserved()
        {
            _window.SetBag(MakeBag(), false, null, false); _window.Show(GameplayPage.Bag);
            foreach (string label in new[] { "全部", "装备", "丹药", "材料", "整理" }) AssertText(label);
            Assert.That(FindButton("丹药").interactable, Is.False, "协议没有物品分类时不得伪造分类结果");
            Assert.That(FindButton("材料").interactable, Is.False);
            Assert.That(FindButton("出售").interactable, Is.False, "出售不得借用装备回调");
            _window.SetMissions(MakeMissions(1), false, null); _window.Show(GameplayPage.Missions);
            // 任务开关是手动展示偏好；自动策略未确认时不得沿用误导性的自动文案。
            foreach (string label in new[] { "已接任务", "可接任务", "追踪任务", "查看地图" }) AssertText(label);
            Assert.That(HasText("自动追踪"), Is.False);
            var tracking = new MmorpgClient.Game.PlayerFeatures.MissionTrackingState();
            Assert.That(tracking.AutoTrackingAvailable, Is.False, "自动策略未开放不能伪造为启用状态");
            Assert.That(tracking.AutoTrackingUnavailableReason, Is.EqualTo("自动追踪规则尚未开放，可手动选择任务"));
            Assert.That(FindButton("查看地图").interactable, Is.False, "没有目标地点合同不能伪造传送");
            Assert.That(_accepts, Is.Empty); Assert.That(_claims, Is.Empty);
        }

        [Test]
        public void OccupiedCompactFourthRowFitsApprovedGridWithoutClippingOrMutatingSlots()
        {
            var bag = MakeBag(); bag.Layout.Capacity = 20;
            bag.Items.Add(new BagItemInfo { ItemId = 9015, ConfigId = 315, Name = "末排灵物", Count = 2, MaxStack = 99 });
            bag.Layout.Slots.Add(new BagSlotInfo { Slot = 15, ItemId = 9015, Width = 1, Height = 1 });
            var before = bag.Clone(); _window.SetBag(bag, false, null, false); _window.Show(GameplayPage.Bag);
            var cell = _fixture.GetComponentsInChildren<Button>().Single(b => b.name == "BagSlot_15");
            var rect = (RectTransform)cell.transform;
            var icon = cell.GetComponentsInChildren<Image>().Single(i => i.name == "ItemIcon").rectTransform;
            Assert.That(rect.sizeDelta.y, Is.EqualTo(80), "原稿第四排是80px紧凑格");
            Assert.That(-rect.anchoredPosition.y + rect.sizeDelta.y, Is.LessThanOrEqualTo(Scroll("BagScroll").viewport.rect.height));
            Assert.That(-icon.anchoredPosition.y + icon.sizeDelta.y, Is.LessThanOrEqualTo(rect.rect.height));
            cell.onClick.Invoke(); AssertText("末排灵物");
            Assert.That(bag.Equals(before), Is.True); Assert.That(_bagReads, Is.EqualTo(1)); Assert.That(_sorts, Is.Zero);
        }

        [Test]
        public void ActivityFooterHasNoInventedThresholdsWhenAuthoritativeRulesAreUnavailable()
        {
            _window.SetActivities(MakeActivities(1), false, null); _window.Show(GameplayPage.Activities);
            foreach (string label in new[] { "今日推荐", "每日活动", "限时活动", "节庆活动", "本期活跃", "—/—", "活动日历" }) AssertText(label);
            Assert.That(_fixture.GetComponentsInChildren<Image>().Count(i => i.name.StartsWith("ActivityVitalityUnavailableChest_")), Is.EqualTo(4));
            var track = _fixture.GetComponentsInChildren<Image>().Single(i => i.name == "ActivityVitalityTrack");
            Assert.That(track.sprite, Is.SameAs(MmorpgClient.UI.Ugui.QdaoUguiTheme.RequireSprite("UI/Ugui/GameplayFidelity20261016/activity-vitality-empty-track")));
            Assert.That(track.rectTransform.sizeDelta, Is.EqualTo(new Vector2(650,29)));
            Assert.That(track.type, Is.EqualTo(Image.Type.Sliced));
            ClickNamed("ActivityTools");
            foreach (string label in new[] { "我的收藏", "日常活动", "限时活动", "福利活动", "即将开启", "其他活动" }) AssertText(label);
            var chests = _fixture.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("ActivityVitalityChest_")).ToArray();
            Assert.That(chests, Is.Empty);
            Assert.That(FindButton("提醒").interactable, Is.False);
            Assert.That(FindButton("推送").interactable, Is.False);
            Assert.That(HasText("35/100"), Is.False); Assert.That(_accepts, Is.Empty); Assert.That(_claims, Is.Empty);
        }

        [TestCase(GameplayPage.Bag, "original-title-bag", "背 包")]
        [TestCase(GameplayPage.Missions, "original-title-missions", "任 务")]
        [TestCase(GameplayPage.Activities, "original-title-activities", "活 动")]
        public void PageSwitchUsesApprovedPaintedTitleWithRetainedSemanticLabel(GameplayPage page, string key, string label)
        {
            _window.Show(page);
            var title = _fixture.GetComponentsInChildren<Image>().Single(i => i.name == "OriginalPageTitle");
            Assert.That(title.sprite, Is.SameAs(MmorpgClient.UI.Ugui.QdaoUguiTheme.RequireSprite("UI/Ugui/GameplayFidelity20261016/" + key)));
            Assert.That(title.raycastTarget, Is.False);
            Assert.That(_fixture.GetComponentsInChildren<TMP_Text>().Single(t => t.text == label).enabled, Is.False);
        }

        [Test]
        public void PaintedMissionSelectionMarkFollowsSelectionWithoutChangingTrackingOrSendingActions()
        {
            _window.SetMissions(MakeMissions(2), false, null); _window.Show(GameplayPage.Missions);
            Assert.That(_fixture.GetComponentsInChildren<Image>().Single(i => i.name == "MissionSelectionMark").transform.parent.name, Is.EqualTo("Mission_0_1"));
            Click("任务二");
            Assert.That(_fixture.GetComponentsInChildren<Image>().Single(i => i.name == "MissionSelectionMark").transform.parent.name, Is.EqualTo("Mission_0_2"));
            Assert.That(_tracking, Is.Empty); Assert.That(_accepts, Is.Empty); Assert.That(_claims, Is.Empty);
        }

        [Test]
        public void ActivityCountsUseActivityCompletionSnapshotAndNeverTaskObjectiveProgress()
        {
            _window.SetMissions(MakeMissions(1), false, null);
            var activities = MakeActivities(2);
            activities.Activities[0].CompletionCountKnown = true; activities.Activities[0].CompletedCount = 2; activities.Activities[0].CompletionLimit = 7;
            _window.SetActivities(activities, false, null); _window.Show(GameplayPage.Activities);
            CollectionAssert.AreEquivalent(new[] { "次数 2/7", "次数 未提供" }, _fixture.GetComponentsInChildren<TMP_Text>().Where(t => t.name == "ActivityCompletionCount").Select(t => t.text));
            Assert.That(HasText("1/3"), Is.False, "任务目标计数不得冒充活动次数");
            ClickNamed("ActivityDetails_2");
            Assert.That(_fixture.GetComponentsInChildren<TMP_Text>().Any(t => t.text.Contains("尚未排期") && t.text.Contains("奖励明细未提供")), Is.True);
            ClickNamed("CloseActivityDetails_2");
            Assert.That(_accepts, Is.Empty); Assert.That(_claims, Is.Empty);
        }

        [Test]
        public void ActivityCategoryAndCompoundRewardFiltersUseOnlyAuthoritativeMetadata()
        {
            var activities = MakeActivities(3);
            activities.Activities[0].Category = (PlayerActivityCategory)1;
            activities.Activities[0].RewardTypes.Add((PlayerActivityRewardType)3);
            activities.Activities[0].RewardTypes.Add((PlayerActivityRewardType)4);
            activities.Activities[1].Category = (PlayerActivityCategory)2;
            activities.Activities[1].RewardTypes.Add((PlayerActivityRewardType)1);
            _window.SetActivities(activities, false, null); _window.Show(GameplayPage.Activities);
            ChooseActivityTool("ActivityCategory_1"); AssertText("雅集一");
            Assert.That(HasText("雅集二") || HasText("雅集三"), Is.False);
            ChooseActivityTool("ActivityRewardFilter_4"); AssertText("雅集一");
            ChooseActivityTool("ActivityRewardFilter_1");
            Assert.That(HasText("雅集一"), Is.False);
            AssertText("没有符合筛选的活动，可清除筛选查看");
            ChooseActivityTool("ClearActivityFilters");
            AssertText("雅集一"); AssertText("雅集二"); AssertText("雅集三");
            ChooseActivityTool("ActivityCategory_5"); AssertText("雅集三");
            Assert.That(HasText("雅集一") || HasText("雅集二"), Is.False, "未知分类只进入其他，不猜成日常");
            Assert.That(_accepts, Is.Empty); Assert.That(_activityReads, Is.EqualTo(1));
        }

        [Test]
        public void FavoritesAreRoleIsolatedAndReusedIdsKeepAnExplainableUnavailableEntry()
        {
            var activities = MakeActivities(1);
            _window.SetActivityContext(100);
            _window.SetActivities(activities, false, null); _window.Show(GameplayPage.Activities);
            ClickNamed("ActivityDetails_1"); ClickNamed("FavoriteActivity_1"); ClickNamed("CloseActivityDetails_1");
            ChooseActivityTool("ActivityCategory_0"); AssertText("雅集一");
            _window.SetActivityContext(101); _window.SetActivities(activities, false, null); ChooseActivityTool("ActivityCategory_0");
            AssertText("暂无收藏，可在活动详情中收藏");
            _window.SetActivityContext(100); _window.SetActivities(activities, false, null); AssertText("雅集一");
            activities.Activities[0].DefinitionKey = "a-different-content-incarnation";
            _window.SetActivities(activities, false, null);
            AssertText("已下架或配置身份已更新");
            Assert.That(_fixture.GetComponentsInChildren<Button>().Any(b => b.name == "ActivityAction_1"), Is.False);
            ClickNamed("RemoveFavorite_1"); AssertText("暂无收藏，可在活动详情中收藏");
            Assert.That(_accepts, Is.Empty);
        }

        [Test]
        public void ActivityRewardThresholdCountRatioAndIntentComeFromTheSnapshot()
        {
            var activities = MakeActivities(1);
            activities.Vitality = new PlayerActivityVitalitySnapshot { Available = true, RuleId = 9,
                ConfigurationRevision = 2, PeriodId = 100, Points = 7, PointsLimit = 20, PeriodEndsAtMs = 86400000 };
            activities.Vitality.Rewards.Add(new PlayerActivityVitalityReward { ThresholdId = 17, RequiredPoints = 3, CanClaim = true });
            activities.Vitality.Rewards.Add(new PlayerActivityVitalityReward { ThresholdId = 31, RequiredPoints = 11 });
            activities.Vitality.Rewards.Add(new PlayerActivityVitalityReward { ThresholdId = 44, RequiredPoints = 19 });
            var before = activities.Clone();
            var requests = new List<(uint, ulong, uint, ulong)>();
            _window.ActivityRewardClaimRequested += (rule, period, threshold, version) => requests.Add((rule, period, threshold, version));
            _window.SetActivities(activities, false, null); _window.Show(GameplayPage.Activities);
            AssertText("7/20");
            Assert.That(_fixture.GetComponentsInChildren<Button>().Count(b => b.name.StartsWith("ActivityVitalityChest_")), Is.EqualTo(3));
            var fill = _fixture.GetComponentsInChildren<RectTransform>().Single(r => r.name == "ActivityVitalityFill");
            Assert.That(fill.rect.width, Is.EqualTo(650f * 7 / 20).Within(.01f));
            ClickNamed("ActivityVitalityChest_17"); ClickNamed("ConfirmActivityReward");
            Assert.That(requests.Single(), Is.EqualTo((9u, 100ul, 17u, 2ul)));
            Assert.That(activities.Equals(before), Is.True, "点击不本地加分、置已领或发物");
            _window.SetActivities(activities, false, null, true);
            Assert.That(_fixture.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("ActivityVitalityChest_")).All(b => !b.interactable), Is.True);
        }

        [Test]
        public void RemovingSelectedActivityInvalidatesItsOldActionAndDetails()
        {
            var activities = MakeActivities(1);
            activities.Activities[0].Status = (PlayerActivityStatus)2; activities.Activities[0].CanParticipate = true;
            _window.SetMissions(new GetMissionListResponse(), false, null);
            _window.SetActivities(activities, false, null); _window.Show(GameplayPage.Activities);
            var action = _fixture.GetComponentsInChildren<Button>().Single(b => b.name == "ActivityAction_1").onClick;
            ClickNamed("ActivityDetails_1");
            _window.SetActivities(new GetActivityListResponse(), false, null);
            action.Invoke();
            Assert.That(_accepts, Is.Empty);
            Assert.That(_fixture.GetComponentsInChildren<RectTransform>().Any(r => r.name == "ActivityDetailsPanel"), Is.False);
        }

        [Test]
        public void CommittedActivityRewardRemainsPendingUntilDurableAndCannotSubmitAnotherClaim()
        {
            var activities = MakeActivities(1);
            activities.Vitality = new PlayerActivityVitalitySnapshot { Available = true, RuleId = 9, ConfigurationRevision = 2, PeriodId = 100, Points = 7, PointsLimit = 20 };
            activities.Vitality.Rewards.Add(new PlayerActivityVitalityReward { ThresholdId = 17, RequiredPoints = 3, Claimed = true, Durable = false });
            var claims = 0; _window.ActivityRewardClaimRequested += (_, _, _, _) => ++claims;
            _window.SetActivities(activities, false, null); _window.Show(GameplayPage.Activities);
            AssertText("3活跃\n确认中"); ClickNamed("ActivityVitalityChest_17");
            AssertText("领取确认中，请刷新恢复原请求");
            var confirm = _fixture.GetComponentsInChildren<Button>().Single(b => b.name == "ConfirmActivityReward");
            Assert.That(confirm.interactable, Is.False); confirm.onClick.Invoke(); Assert.That(claims, Is.Zero);
        }

        [Test]
        public void LongActivityDescriptionScrollsUntilItsFinalCharacterIsInsideTheViewport()
        {
            var activities = MakeActivities(1);
            activities.Activities[0].Description = string.Concat(Enumerable.Repeat("活动说明与奖励条件必须完整可读。", 80)) + "\n长说明末";
            _window.SetActivities(activities, false, null); _window.Show(GameplayPage.Activities);
            ClickNamed("ActivityDetails_1");
            var scroll = Scroll("DescriptionScroll");
            Assert.That(scroll.vertical, Is.True); Assert.That(scroll.horizontal, Is.False);
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            scroll.verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases();
            var label = scroll.content.GetComponentInChildren<TMP_Text>(); label.ForceMeshUpdate(true, true);
            Assert.That(label.text, Does.EndWith("长说明末"));
            Assert.That(label.textWrappingMode, Is.EqualTo(TextWrappingModes.Normal), "正文必须按可用宽度真实换行");
            Assert.That(label.textInfo.lineCount, Is.GreaterThan(10));
            foreach (var character in label.textInfo.characterInfo.Take(label.textInfo.characterCount).Where(c => c.isVisible))
            {
                var left = scroll.viewport.InverseTransformPoint(label.transform.TransformPoint(character.bottomLeft));
                var right = scroll.viewport.InverseTransformPoint(label.transform.TransformPoint(character.topRight));
                Assert.That(left.x, Is.GreaterThanOrEqualTo(scroll.viewport.rect.xMin - 1), "正文不得从左侧横向溢出");
                Assert.That(right.x, Is.LessThanOrEqualTo(scroll.viewport.rect.xMax + 1), "正文不得从右侧横向裁断");
            }
            var last = label.textInfo.characterInfo[label.textInfo.characterCount - 1];
            Assert.That(last.isVisible, Is.True, "正文末字必须有可绘制字形");
            var lower = scroll.viewport.InverseTransformPoint(label.transform.TransformPoint(last.bottomLeft));
            var upper = scroll.viewport.InverseTransformPoint(label.transform.TransformPoint(last.topRight));
            Assert.That(lower.y, Is.GreaterThanOrEqualTo(scroll.viewport.rect.yMin - 1), "正文末字不能被下边界裁掉");
            Assert.That(upper.y, Is.LessThanOrEqualTo(scroll.viewport.rect.yMax + 1), "正文末字必须滚入可视区");
            Assert.That(_accepts, Is.Empty); Assert.That(_claims, Is.Empty);
        }

        [Test]
        public void ChangingActivityPeriodClosesOldConfirmationAndCannotClaimTheNewPeriod()
        {
            var activities = MakeActivities(1);
            activities.Vitality = new PlayerActivityVitalitySnapshot { Available = true, RuleId = 9, ConfigurationRevision = 2, PeriodId = 100, Points = 7, PointsLimit = 20 };
            activities.Vitality.Rewards.Add(new PlayerActivityVitalityReward { ThresholdId = 17, RequiredPoints = 3, CanClaim = true });
            var claims = 0; _window.ActivityRewardClaimRequested += (_, _, _, _) => ++claims;
            _window.SetActivities(activities, false, null); _window.Show(GameplayPage.Activities);
            ClickNamed("ActivityVitalityChest_17");
            var action = _fixture.GetComponentsInChildren<Button>().Single(b => b.name == "ConfirmActivityReward").onClick;
            activities.Vitality.PeriodId = 200;
            _window.SetActivities(activities, false, null);
            Assert.That(_fixture.GetComponentsInChildren<Button>().Any(b => b.name == "ConfirmActivityReward"), Is.False);
            action.Invoke(); Assert.That(claims, Is.Zero);
            activities.Vitality.Available = false; activities.Vitality.UnavailableReason = "新周期规则未配置";
            _window.SetActivities(activities, false, null, false, "旧周期领取结果待确认，请刷新");
            AssertText("旧周期领取结果待确认，请刷新"); AssertText("新周期规则未配置");
            Assert.That(FindButton("刷新").interactable, Is.True);
        }

        [Test]
        public void ActivityToolsBackReturnsToOriginalCardsWithoutReadingOrSendingActions()
        {
            _window.SetActivities(MakeActivities(3), false, null); _window.Show(GameplayPage.Activities);
            ClickNamed("ActivityTools");
            Assert.That(_fixture.GetComponentsInChildren<RectTransform>().Any(r => r.name == "ActivityToolsPanel"), Is.True);
            _window.Back();
            Assert.That(_window.IsVisible, Is.True);
            Assert.That(_fixture.GetComponentsInChildren<RectTransform>().Any(r => r.name == "ActivityToolsPanel"), Is.False);
            Assert.That(_fixture.GetComponentsInChildren<RectTransform>().Count(r => r.name.StartsWith("Activity_") && r.sizeDelta == new Vector2(410,495)), Is.EqualTo(3));
            Assert.That(_activityReads, Is.EqualTo(1)); Assert.That(_accepts, Is.Empty); Assert.That(_claims, Is.Empty);
        }

        private void ChooseActivityTool(string name)
        {
            if (!_fixture.GetComponentsInChildren<Button>().Any(b => b.name == name)) ClickNamed("ActivityTools");
            ClickNamed(name);
        }

        private void ClickNamed(string name)
        {
            var button = _fixture.GetComponentsInChildren<Button>(true)
                .Single(b => b.name == name && b.gameObject.activeInHierarchy);
            Assert.That(button.interactable, Is.True, "按钮应可点击：" + name);
            button.onClick.Invoke();
        }

        private ScrollRect Scroll(string name)
            => _fixture.GetComponentsInChildren<ScrollRect>().Single(s => s.name == name);

        private TMP_InputField Search()
            => _fixture.GetComponentsInChildren<TMP_InputField>(true).Single(i => i.name == "BagSearch");

        private string[] OccupiedSlots()
            => _fixture.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("BagSlot_") && b.interactable)
                .Select(b => b.name).ToArray();

        private bool HasText(string text)
            => _fixture.GetComponentsInChildren<TMP_Text>().Any(t => t.text == text);

        private void AssertText(string text) => Assert.That(HasText(text), Is.True, "当前页面应显示：" + text);

        private bool SelectedMissionTracked()
            => _fixture.GetComponentsInChildren<Image>().Any(i => i.name == "AutoTrackState" && i.sprite == EquipUiSkin.Load("checkbox_on"));

        private Button FindButton(string label)
        {
            if (label == "刷新" && _window.Page == GameplayPage.Bag &&
                !_fixture.GetComponentsInChildren<Button>().Any(b => b.name == "RefreshBag")) ClickNamed("BagTools");
            if (label == "刷新" && _window.Page == GameplayPage.Activities &&
                !_fixture.GetComponentsInChildren<Button>().Any(b => b.name == "RefreshActivityFooter")) ClickNamed("ActivityTools");
            var matches = _fixture.GetComponentsInChildren<Button>()
                .Where(b => b.GetComponentsInChildren<TMP_Text>().Any(t => t.text == label)).ToArray();
            Assert.That(matches.Length, Is.EqualTo(1), "当前页面应有唯一按钮：" + label);
            return matches[0];
        }

        private void Click(string label)
        {
            var button = FindButton(label);
            Assert.That(button.interactable, Is.True, "按钮应可点击：" + label);
            button.onClick.Invoke();
        }

        private static BagInfo MakeBag()
        {
            var bag = new BagInfo { Layout = new BagLayoutInfo { BagType = 0, Capacity = 40, CanSort = true } };
            bag.Items.Add(new BagItemInfo { ItemId = 9001, ConfigId = 101, Name = "回春草", Count = 3, MaxStack = 99 });
            bag.Items.Add(new BagItemInfo { ItemId = 9002, ConfigId = 202, Name = "青竹剑", EquipKind = 1, Count = 1, MaxStack = 1 });
            bag.Layout.Slots.Add(new BagSlotInfo { Slot = 0, ItemId = 9001, Width = 1, Height = 1 });
            bag.Layout.Slots.Add(new BagSlotInfo { Slot = 9, ItemId = 9002, Width = 1, Height = 1 });
            return bag;
        }

        private static readonly string[] Numbers = { "一", "二", "三", "四", "五", "六", "七" };

        private static GetMissionListResponse MakeMissions(int count)
        {
            var response = new GetMissionListResponse();
            for (int i = 0; i < count; ++i)
            {
                var mission = new PlayerMissionInfo
                {
                    MissionId = (uint)(i + 1), Name = "任务" + Numbers[i], Description = "任务说明" + (i + 1),
                    Status = (PlayerMissionStatus)1, Configured = true,
                };
                mission.Objectives.Add(new MissionObjectiveInfo { ObjectiveIndex = 0, Target = 3, Progress = 1 });
                response.Missions.Add(mission);
            }
            return response;
        }

        private static GetActivityListResponse MakeActivities(int count)
        {
            var response = new GetActivityListResponse();
            for (int i = 0; i < count; ++i)
                response.Activities.Add(new PlayerActivityInfo
                {
                    ActivityId = (uint)(i + 1), MissionId = (uint)(i + 1), Name = "雅集" + Numbers[i],
                    Description = "雅集说明" + (i + 1), Status = (PlayerActivityStatus)0, EntryKind = (PlayerActivityEntryKind)1,
                    DefinitionKey = "fixture-activity-" + (i + 1),
                });
            return response;
        }
    }
}
