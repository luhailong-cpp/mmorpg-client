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

        [SetUp]
        public void SetUp()
        {
            _fixture = new GameObject("GameplayWindowTests", typeof(RectTransform), typeof(Canvas));
            ((RectTransform)_fixture.transform).sizeDelta = new Vector2(2560, 1080);
            _window = new GameplayWindow(_fixture.transform);
            _bagReads = _missionReads = _activityReads = _sorts = 0;
            _tracking.Clear();
            _window.BagRequested += _ => ++_bagReads;
            _window.MissionsRequested += () => ++_missionReads;
            _window.ActivitiesRequested += () => ++_activityReads;
            _window.SortRequested += () => ++_sorts;
            _window.TrackingChanged += _tracking.Add;
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
            Click("其他物品");
            Assert.That(OccupiedSlots(), Is.Empty);
            AssertText("没有找到符合条件的物品");
            Search().text = "";
            CollectionAssert.AreEqual(new[] { "BagSlot_0" }, OccupiedSlots());
            Click("装备");
            CollectionAssert.AreEqual(new[] { "BagSlot_9" }, OccupiedSlots());
            Click("全部物品");
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
            Click("整理背包");
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
            Click("追踪任务");
            Assert.That(_tracking.Last().MissionId, Is.EqualTo(1));
            Click("已达成");
            _window.SetActivities(activities, false, null);
            _window.Show(GameplayPage.Activities);
            Click("进行中");
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
            Assert.That(HasText("取消追踪"), Is.False);
            _window.SetActivities(activities, false, null);
            _window.Show(GameplayPage.Activities);
            AssertText("雅集一");
            AssertText("雅集二");
            Assert.That(_sorts, Is.Zero);
        }

        [Test]
        public void AllMissionObjectivesCanBeViewedThroughNativeGroupButtons()
        {
            var missions = MakeMissions(1);
            missions.Missions[0].Objectives.Clear();
            for (uint i = 0; i < 7; ++i)
                missions.Missions[0].Objectives.Add(new MissionObjectiveInfo
                {
                    ObjectiveIndex = i, ConditionId = 100, Description = "目标内容" + (i + 1),
                    Progress = i, Target = 10,
                });
            _window.SetMissions(missions, false, null);
            _window.Show(GameplayPage.Missions);
            AssertText("目标内容1");
            Assert.That(HasText("目标内容4"), Is.False);
            Assert.That(FindButton("上组").interactable, Is.False);
            Click("下组");
            AssertText("目标内容4");
            AssertText("目标内容6");
            Assert.That(HasText("目标内容1"), Is.False);
            Click("下组");
            AssertText("目标内容7");
            Assert.That(FindButton("下组").interactable, Is.False);
            Click("上组");
            AssertText("目标内容4");
            Assert.That(_missionReads, Is.EqualTo(1));
            Assert.That(_tracking, Is.Empty);
        }

        [Test]
        public void MissionPaginationSelectsAnEntryOnTheVisiblePageAndChangesDetails()
        {
            _window.SetMissions(MakeMissions(7), false, null);
            _window.Show(GameplayPage.Missions);
            AssertText("任务说明1");
            Click("下一页");
            AssertText("任务六");
            AssertText("任务说明6");
            Assert.That(HasText("任务说明1"), Is.False);
            Assert.That(FindButton("下一页").interactable, Is.False);
            Click("任务七");
            AssertText("任务说明7");
            Click("上一页");
            AssertText("任务说明1");
            Assert.That(_missionReads, Is.EqualTo(1), "切页与查看详情不应发送额外RPC");
        }

        [Test]
        public void ActivityPaginationSelectsAnEntryOnTheVisiblePageAndChangesDetails()
        {
            _window.SetActivities(MakeActivities(5), false, null);
            _window.Show(GameplayPage.Activities);
            AssertText("雅集说明1");
            Click("下一页");
            AssertText("雅集四");
            AssertText("雅集说明4");
            Assert.That(HasText("雅集说明1"), Is.False);
            Click("查看详情");
            AssertText("雅集说明5");
            Click("上一页");
            AssertText("雅集说明1");
            Assert.That(_activityReads, Is.EqualTo(1));
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
            Click("追踪任务");
            Assert.That(_tracking.Last().MissionId, Is.EqualTo(1));
            Assert.That(_tracking.Last().Scope, Is.EqualTo(5));
            Click("任务一");
            AssertText("任务说明1");
            Assert.That(HasText("取消追踪"), Is.False, "同配置不同scope不能共享追踪按钮状态");
            Click("副本任务");
            Assert.That(HasText("取消追踪"), Is.True);

            var onlyBaseScope = new GetMissionListResponse();
            onlyBaseScope.Missions.Add(missions.Missions[0].Clone());
            _window.SetMissions(onlyBaseScope, false, null);
            Assert.That(_tracking.Last(), Is.Null, "移除被追踪scope时，同ID的另一scope不能替代它");
            AssertText("任务说明1");
            Assert.That(HasText("取消追踪"), Is.False);
        }
        [Test]
        public void RemovingTrackedMissionClearsTrackingAndDoesNotReviveItWhenItReturns()
        {
            var missions = MakeMissions(2);
            _window.SetMissions(missions, false, null);
            _window.Show(GameplayPage.Missions);
            Click("追踪任务");
            Assert.That(_tracking.Last().MissionId, Is.EqualTo(1));
            Assert.That(HasText("取消追踪"), Is.True);

            var remaining = new GetMissionListResponse();
            remaining.Missions.Add(missions.Missions[1].Clone());
            _window.SetMissions(remaining, false, null);
            Assert.That(_tracking.Last(), Is.Null);
            Assert.That(HasText("取消追踪"), Is.False);
            _window.SetMissions(missions, false, null);
            Click("任务一");
            Assert.That(HasText("取消追踪"), Is.False);
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
            Assert.That(FindButton("整理背包").interactable, Is.False);
            Click("刷新");
            Assert.That(_bagReads, Is.EqualTo(2));
            Assert.That(_sorts, Is.Zero);

            _window.SetMissions(new GetMissionListResponse(), false, null);
            _window.Show(GameplayPage.Missions);
            AssertText("这页手札还没有记录");
            Assert.That(FindButton("下一页").interactable, Is.False);
            _window.SetActivities(new GetActivityListResponse(), false, null);
            _window.Show(GameplayPage.Activities);
            AssertText("此时暂无活动，静候下一场相聚");
            Assert.That(FindButton("下一页").interactable, Is.False);
        }

        private TMP_InputField Search()
            => _fixture.GetComponentsInChildren<TMP_InputField>(true).Single(i => i.name == "BagSearch");

        private string[] OccupiedSlots()
            => _fixture.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("BagSlot_") && b.interactable)
                .Select(b => b.name).ToArray();

        private bool HasText(string text)
            => _fixture.GetComponentsInChildren<TMP_Text>().Any(t => t.text == text);

        private void AssertText(string text) => Assert.That(HasText(text), Is.True, "当前页面应显示：" + text);

        private Button FindButton(string label)
        {
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
                    Description = "雅集说明" + (i + 1), Status = (PlayerActivityStatus)0,
                });
            return response;
        }
    }
}
