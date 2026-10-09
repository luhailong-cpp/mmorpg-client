using System.Collections.Generic;
using MmorpgClient.Game.WorldTravel;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Gameplay;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>
    /// 线路面板:只读视图进、意图事件出。默认样例是七条线
    /// (1线 流畅·当前 / 2线 繁忙 / 3线 爆满 / 4线 回收中 / 5线 暂不可用 / 6线 未知 / 7线 流畅),
    /// 能切的只有 2线 和 7线。
    /// </summary>
    public sealed class SceneChannelWindowTests
    {
        private GameObject _root;
        private SceneChannelWindow _window;
        private readonly List<ulong> _switches = new List<ulong>();
        private int _refreshes;
        private int _closes;

        [SetUp]
        public void SetUp()
        {
            _switches.Clear();
            _refreshes = 0;
            _closes = 0;
            _root = new GameObject("SceneChannelWindowTest", typeof(RectTransform));
            var design = QdaoUguiFactory.CreateCenteredRect("Design", _root.transform, 2560, 1080);
            _window = new SceneChannelWindow(design);
            _window.SwitchRequested += sceneId => _switches.Add(sceneId);
            _window.RefreshRequested += () => _refreshes++;
            _window.Closed += () => _closes++;
        }

        [TearDown]
        public void TearDown()
        {
            _window?.Hide();
            if (_root != null) Object.DestroyImmediate(_root);
        }

        // ── 行 ──────────────────────────────────────────────────────────────

        [Test]
        public void Show_BuildsOneNamedRowPerLine_LeftThenRightThenNextRow()
        {
            _window.SetView(View(SevenLines()));
            _window.Show();

            Assert.That(_window.IsVisible, Is.True);
            Assert.That(LineNames(), Is.EqualTo(Names(1, 7)));
            Assert.That(Texts(FindButton(LineName(2))), Is.EqualTo(new[] { "2线", "繁忙" }));
            Assert.That(Texts(FindButton(LineName(3))), Is.EqualTo(new[] { "3线", "爆满" }));
            Assert.That(Texts(FindButton(LineName(4))), Is.EqualTo(new[] { "4线", "回收中" }));
            Assert.That(Texts(FindButton(LineName(5))), Is.EqualTo(new[] { "5线", "暂不可用" }));
            Assert.That(Texts(FindButton(LineName(6))), Is.EqualTo(new[] { "6线", "未知" }));

            // 两列:1线 左上,2线 在它右边同一行,3线 回到左边下一行。
            Vector2 first = Position(LineName(1)), second = Position(LineName(2)), third = Position(LineName(3));
            Assert.That(first, Is.EqualTo(Vector2.zero));
            Assert.That(second.x, Is.GreaterThan(first.x));
            Assert.That(second.y, Is.EqualTo(first.y));
            Assert.That(third.x, Is.EqualTo(first.x));
            Assert.That(third.y, Is.LessThan(first.y), "界面坐标 y 向下为负");
        }

        [Test]
        public void CurrentLine_IsNotClickable_CarriesTheCurrentTag_AndUsesTheSelectedArt()
        {
            _window.SetView(View(SevenLines()));
            _window.Show();

            var current = FindButton(LineName(1));
            Assert.That(current.interactable, Is.False);
            Assert.That(Texts(current), Is.EqualTo(new[] { "1线", SceneChannelWindow.CurrentTagText, "流畅" }));
            Assert.That(((Image)current.targetGraphic).sprite, Is.SameAs(GameplayUiArt.Load("tab_selected")));
            Assert.That(current.colors.disabledColor, Is.EqualTo(Color.white), "所在线不可点但不置灰");

            var other = FindButton(LineName(7));
            Assert.That(Texts(other), Is.EqualTo(new[] { "7线", "流畅" }), "同样流畅、但不是所在线:没有标记");
            Assert.That(((Image)other.targetGraphic).sprite, Is.SameAs(GameplayUiArt.Load("tab_normal")));

            Assert.That(Label("SceneChannelCurrent").text, Is.EqualTo("当前：1线"));
            Assert.That(CountLabels(SceneChannelWindow.CurrentTagName), Is.EqualTo(1));
        }

        [Test]
        public void CurrentLineMissingFromTheDirectory_SubtitleSaysUnknown_AndNoRowIsTagged()
        {
            var lines = new List<SceneChannelLine> { Line(1, SceneChannelLoad.Smooth), Line(2, SceneChannelLoad.Busy) };
            _window.SetView(View(lines));
            _window.Show();

            Assert.That(Label("SceneChannelCurrent").text, Is.EqualTo("当前：未知"));
            Assert.That(CountLabels(SceneChannelWindow.CurrentTagName), Is.Zero);
            Assert.That(FindButton(LineName(1)).interactable, Is.True);
            Assert.That(FindButton(LineName(2)).interactable, Is.True);
        }

        [Test]
        public void OnlySmoothAndBusyLines_AreClickable()
        {
            _window.SetView(View(SevenLines()));
            _window.Show();

            Assert.That(FindButton(LineName(1)).interactable, Is.False, "所在线");
            Assert.That(FindButton(LineName(2)).interactable, Is.True, "繁忙");
            Assert.That(FindButton(LineName(3)).interactable, Is.False, "爆满");
            Assert.That(FindButton(LineName(4)).interactable, Is.False, "回收中");
            Assert.That(FindButton(LineName(5)).interactable, Is.False, "暂不可用");
            Assert.That(FindButton(LineName(6)).interactable, Is.False, "未知");
            Assert.That(FindButton(LineName(7)).interactable, Is.True, "流畅");
        }

        [Test]
        public void ClickingASelectableLine_EmitsItsSceneId_UnselectableLinesEmitNothing()
        {
            _window.SetView(View(SevenLines()));
            _window.Show();

            Click(LineName(2));
            Assert.That(_switches, Is.EqualTo(new[] { SceneId(2) }));
            Click(LineName(7));
            Assert.That(_switches, Is.EqualTo(new[] { SceneId(2), SceneId(7) }));

            // onClick.Invoke 会绕过 interactable;窗口自己还要按规则再核一遍,不能把意图发出去。
            Click(LineName(1));
            Click(LineName(3));
            Click(LineName(4));
            Click(LineName(5));
            Click(LineName(6));
            Assert.That(_switches.Count, Is.EqualTo(2));
        }

        [Test]
        public void GlobalBlocks_DisableEveryRow_AndTheStatusLineExplainsWhy()
        {
            var follower = Ready();
            follower.TeamFollower = true;
            AssertWholePanelBlocked(follower, SceneChannelBlockReason.TeamMember);
            Assert.That(_window.StatusText, Is.EqualTo("队伍中由队长切线，或先离队。"));

            var inBattle = Ready();
            inBattle.InBattle = true;
            AssertWholePanelBlocked(inBattle, SceneChannelBlockReason.InBattle);

            var travelling = Ready();
            travelling.Travelling = true;
            AssertWholePanelBlocked(travelling, SceneChannelBlockReason.Travelling);

            var disabled = Ready();
            disabled.SwitchEnabled = false;
            AssertWholePanelBlocked(disabled, SceneChannelBlockReason.SwitchDisabled);

            Assert.That(_switches, Is.Empty);
        }

        [Test]
        public void SwitchInFlight_DisablesEveryRow_MarksTheTargetRow_AndShowsTheProgressText()
        {
            var context = Ready();
            context.SwitchPending = true;
            var view = View(SevenLines(), context, SceneChannelClient.SwitchingText(2));
            view.SwitchTargetSceneId = SceneId(2);
            _window.SetView(view);
            _window.Show();

            AssertNoRowIsClickable();
            Assert.That(Texts(FindButton(LineName(2))), Is.EqualTo(new[] { "2线", SceneChannelWindow.SwitchingRowText }));
            Assert.That(Texts(FindButton(LineName(7))), Is.EqualTo(new[] { "7线", "流畅" }), "其它行照常显示状态");
            Assert.That(_window.StatusText, Is.EqualTo("正在切换到 2线…"));
            Assert.That(_window.StatusIsError, Is.False);
            Assert.That(FindButton(SceneChannelWindow.RefreshButtonName).interactable, Is.False);

            Click(LineName(7));
            Assert.That(_switches, Is.Empty);
        }

        // ── 刷新 / 关闭 ─────────────────────────────────────────────────────

        [Test]
        public void Refresh_EmitsOnlyWhenAListRequestCanGoOut()
        {
            _window.SetView(View(SevenLines()));
            _window.Show();
            var refresh = FindButton(SceneChannelWindow.RefreshButtonName);
            Assert.That(refresh.interactable, Is.True);
            Assert.That(Texts(refresh), Is.EqualTo(new[] { SceneChannelWindow.RefreshText }));
            Click(SceneChannelWindow.RefreshButtonName);
            Assert.That(_refreshes, Is.EqualTo(1));

            // 列线尚无结论:按钮置灰并改口,即使被调用也不再发意图。
            var loading = View(SevenLines());
            loading.Loading = true;
            _window.SetView(loading);
            Assert.That(refresh.interactable, Is.False);
            Assert.That(Texts(refresh), Is.EqualTo(new[] { SceneChannelWindow.RefreshingText }));
            Click(SceneChannelWindow.RefreshButtonName);
            Assert.That(_refreshes, Is.EqualTo(1));

            // 未连接:发不出去。
            var offline = Ready();
            offline.Connected = false;
            _window.SetView(View(SevenLines(), offline));
            Assert.That(refresh.interactable, Is.False);
            Click(SceneChannelWindow.RefreshButtonName);
            Assert.That(_refreshes, Is.EqualTo(1));

            // 队伍成员不能切线,但可以刷新着看。
            var follower = Ready();
            follower.TeamFollower = true;
            _window.SetView(View(SevenLines(), follower));
            Assert.That(refresh.interactable, Is.True);
            Click(SceneChannelWindow.RefreshButtonName);
            Assert.That(_refreshes, Is.EqualTo(2));
        }

        [Test]
        public void Close_HidesTheWindow_AndRaisesClosedOncePerVisibleSession()
        {
            _window.Hide();
            Assert.That(_closes, Is.Zero, "没打开过,关闭不发事件");

            _window.SetView(View(SevenLines()));
            _window.Show();
            Click(SceneChannelWindow.CloseButtonName);
            Assert.That(_window.IsVisible, Is.False);
            Assert.That(_closes, Is.EqualTo(1));

            _window.Hide();
            Assert.That(_closes, Is.EqualTo(1));
        }

        [Test]
        public void SetView_WhileHidden_OnlyRemembersIt_RowsAreBuiltOnShow()
        {
            _window.SetView(View(SevenLines()));
            Assert.That(LineNames(), Is.Empty, "隐藏时不建行");

            _window.Show();
            Assert.That(LineNames().Count, Is.EqualTo(7));

            _window.Hide();
            _window.SetView(View(ManyLines(3)));
            Assert.That(LineNames().Count, Is.EqualTo(7), "隐藏时不重建");

            _window.Show();
            Assert.That(LineNames(), Is.EqualTo(Names(1, 3)));
        }

        [Test]
        public void SetView_WhenOnlyTheFooterChanges_KeepsTheSameRowButtons()
        {
            _window.SetView(View(SevenLines()));
            _window.Show();
            var row = FindButton(LineName(2));
            var currentRow = FindButton(LineName(1));

            // 面板每 5 秒刷新一次目录:先是「正在获取」翻转(行没变),再是内容相同的新目录到达(线路对象全是新的)。
            // 行按钮若跟着销毁重建,按下与抬起之间撞上的那次点击就丢了。
            var loading = View(SevenLines());
            loading.Loading = true;
            _window.SetView(loading);
            Assert.That(FindButton(LineName(2)), Is.SameAs(row));
            Assert.That(Texts(FindButton(SceneChannelWindow.RefreshButtonName)),
                Is.EqualTo(new[] { SceneChannelWindow.RefreshingText }), "底栏照常更新");
            _window.SetView(View(SevenLines()));
            Assert.That(FindButton(LineName(2)), Is.SameAs(row));
            Assert.That(FindButton(LineName(1)), Is.SameAs(currentRow));
            Assert.That(Texts(FindButton(SceneChannelWindow.RefreshButtonName)),
                Is.EqualTo(new[] { SceneChannelWindow.RefreshText }));

            // 只有状态行变了:同样不动行。
            _window.SetView(View(SevenLines(), Ready(), SceneChannelClient.SwitchFailedMessage, true));
            Assert.That(FindButton(LineName(2)), Is.SameAs(row));
            Assert.That(_window.StatusText, Is.EqualTo(SceneChannelClient.SwitchFailedMessage));
            Assert.That(_window.StatusIsError, Is.True);

            // 留下来的行照样能点。
            Click(LineName(2));
            Assert.That(_switches, Is.EqualTo(new[] { SceneId(2) }));
            Assert.That(LineNames(), Is.EqualTo(Names(1, 7)), "没有重复建行");
        }

        [Test]
        public void SetView_WhenARowActuallyChanges_RebuildsTheRows()
        {
            _window.SetView(View(SevenLines()));
            _window.Show();
            var row = FindButton(LineName(2));

            // 一条线的状态变了。
            var lines = SevenLines();
            lines[1] = Line(2, SceneChannelLoad.Full);
            _window.SetView(View(lines));
            Assert.That(FindButton(LineName(2)), Is.Not.SameAs(row));
            Assert.That(FindButton(LineName(2)).interactable, Is.False);
            Assert.That(Texts(FindButton(LineName(2))), Is.EqualTo(new[] { "2线", "爆满" }));

            // 线没变,但整张面板的可点状态变了(进了战斗)。
            row = FindButton(LineName(7));
            var inBattle = Ready();
            inBattle.InBattle = true;
            _window.SetView(View(lines, inBattle));
            Assert.That(FindButton(LineName(7)), Is.Not.SameAs(row));
            Assert.That(FindButton(LineName(7)).interactable, Is.False);

            // 切线目标变了(「切换中…」标在哪一行)。
            row = FindButton(LineName(7));
            var pending = Ready();
            pending.SwitchPending = true;
            var switching = View(lines, pending, SceneChannelClient.SwitchingText(7));
            switching.SwitchTargetSceneId = SceneId(7);
            _window.SetView(switching);
            Assert.That(FindButton(LineName(7)), Is.Not.SameAs(row));
            Assert.That(Texts(FindButton(LineName(7))), Is.EqualTo(new[] { "7线", SceneChannelWindow.SwitchingRowText }));
            Assert.That(LineNames(), Is.EqualTo(Names(1, 7)));
        }

        // ── 分页 ────────────────────────────────────────────────────────────

        [Test]
        public void MoreThanSixteenLines_ArePaged_AndShrinkingTheListClampsThePage()
        {
            _window.SetView(View(ManyLines(20)));
            _window.Show();
            var previous = FindButton(SceneChannelWindow.PreviousPageButtonName);
            var next = FindButton(SceneChannelWindow.NextPageButtonName);

            Assert.That(SceneChannelWindow.LinesPerPage, Is.EqualTo(16));
            Assert.That(_window.PageCount, Is.EqualTo(2));
            Assert.That(_window.PageIndex, Is.Zero);
            Assert.That(LineNames(), Is.EqualTo(Names(1, 16)));
            Assert.That(previous.gameObject.activeSelf, Is.True);
            Assert.That(previous.interactable, Is.False);
            Assert.That(next.interactable, Is.True);
            Assert.That(Label("SceneChannelPage").text, Is.EqualTo("1 / 2"));

            Click(SceneChannelWindow.NextPageButtonName);
            Assert.That(_window.PageIndex, Is.EqualTo(1));
            Assert.That(LineNames(), Is.EqualTo(Names(17, 20)));
            Assert.That(previous.interactable, Is.True);
            Assert.That(next.interactable, Is.False);
            Assert.That(Label("SceneChannelPage").text, Is.EqualTo("2 / 2"));
            // 第二页的第一条回到左上角。
            Assert.That(Position(LineName(17)), Is.EqualTo(Vector2.zero));

            // 目录刷新后只剩十条:页码收回第一页,翻页控件整组隐藏。
            _window.SetView(View(ManyLines(10)));
            Assert.That(_window.PageCount, Is.EqualTo(1));
            Assert.That(_window.PageIndex, Is.Zero);
            Assert.That(LineNames(), Is.EqualTo(Names(1, 10)));
            Assert.That(previous.gameObject.activeSelf, Is.False);
            Assert.That(next.gameObject.activeSelf, Is.False);
            Assert.That(Label("SceneChannelPage").gameObject.activeSelf, Is.False);

            // 恰好十六条仍是一页,十七条才有第二页。
            _window.SetView(View(ManyLines(16)));
            Assert.That(_window.PageCount, Is.EqualTo(1));
            _window.SetView(View(ManyLines(17)));
            Assert.That(_window.PageCount, Is.EqualTo(2));
        }

        [Test]
        public void Show_OpensOnThePageOfTheCurrentLine_ButDoesNotJumpWhileAlreadyOpen()
        {
            _window.SetView(View(ManyLines(20, current: 18)));
            _window.Show();
            Assert.That(_window.PageIndex, Is.EqualTo(1));
            Assert.That(LineNames(), Is.EqualTo(Names(17, 20)));

            Click(SceneChannelWindow.PreviousPageButtonName);
            Assert.That(_window.PageIndex, Is.Zero);
            _window.Show();
            Assert.That(_window.PageIndex, Is.Zero, "已经开着:玩家翻到哪页就留在哪页");

            _window.Hide();
            _window.Show();
            Assert.That(_window.PageIndex, Is.EqualTo(1));
        }

        // ── 状态行 ──────────────────────────────────────────────────────────

        [Test]
        public void StatusLine_DefaultHints_DependOnHowManyLinesThereAre()
        {
            _window.SetView(View(SevenLines()));
            _window.Show();
            Assert.That(_window.StatusText, Is.EqualTo(SceneChannelWindow.PickHint));
            Assert.That(_window.StatusIsError, Is.False);

            _window.SetView(View(new List<SceneChannelLine> { Line(1, SceneChannelLoad.Smooth, true) }));
            Assert.That(_window.StatusText, Is.EqualTo(SceneChannelWindow.SingleLineHint));
            Assert.That(_window.StatusIsError, Is.False);
        }

        [Test]
        public void StatusLine_ShowsTheDataLayerText_AndFlagsFailures()
        {
            _window.SetView(View(SevenLines(), Ready(), SceneChannelClient.SwitchFailedMessage, true));
            _window.Show();
            Assert.That(_window.StatusText, Is.EqualTo("切线失败，请稍后再试。"));
            Assert.That(_window.StatusIsError, Is.True);
            Assert.That(FindButton(LineName(2)).interactable, Is.True, "失败之后可以直接再选");

            _window.SetView(View(SevenLines(), Ready(), SceneChannelClient.SwitchedText(2)));
            Assert.That(_window.StatusText, Is.EqualTo("已切换到 2线。"));
            Assert.That(_window.StatusIsError, Is.False);
        }

        [Test]
        public void DescribeStatus_FollowsItsPriorityOrder()
        {
            var lines = SevenLines();
            bool isError;

            // 1. 切线在途:数据层的进度文字;数据层没给时退回通用说法。
            var pending = Ready();
            pending.SwitchPending = true;
            pending.TeamFollower = true;
            Assert.That(SceneChannelWindow.DescribeStatus(View(lines, pending, "正在切换到 2线…"), out isError),
                Is.EqualTo("正在切换到 2线…"));
            Assert.That(isError, Is.False);
            Assert.That(SceneChannelWindow.DescribeStatus(View(lines, pending), out isError),
                Is.EqualTo(SceneChannelModels.DescribeBlock(SceneChannelBlockReason.Switching)));

            // 2. 宿主给出的整面板原因排在留着的失败提示之前。
            var follower = Ready();
            follower.TeamFollower = true;
            Assert.That(SceneChannelWindow.DescribeStatus(View(lines, follower, "切线失败，请稍后再试。", true), out isError),
                Is.EqualTo(SceneChannelModels.DescribeBlock(SceneChannelBlockReason.TeamMember)));
            Assert.That(isError, Is.False);

            // 3. 没有整面板原因时,失败提示原样显示并标红。
            Assert.That(SceneChannelWindow.DescribeStatus(View(lines, Ready(), "3线已满，请选择其他线路。", true), out isError),
                Is.EqualTo("3线已满，请选择其他线路。"));
            Assert.That(isError, Is.True);

            // 4. 尚未就绪:列表空着时由列表区的占位文字说明,状态行不重复;有失败提示则照常显示。
            var noDirectory = new SceneChannelSwitchContext { Connected = true };
            Assert.That(SceneChannelWindow.DescribeStatus(View(SceneChannelModels.NoLines, noDirectory), out isError),
                Is.Empty);
            Assert.That(SceneChannelWindow.DescribeStatus(
                    View(SceneChannelModels.NoLines, noDirectory, SceneChannelClient.ListUnavailableMessage, true), out isError),
                Is.EqualTo("线路信息暂时获取不到。"));
            Assert.That(isError, Is.True);
            //    刚切完线、新目录还没到:「已切换」不是失败提示,不抢在占位文字前面。
            Assert.That(SceneChannelWindow.DescribeStatus(
                    View(SceneChannelModels.NoLines, noDirectory, SceneChannelClient.SwitchedText(2)), out isError),
                Is.Empty);
            //    列表还在、但连接没就绪:给一句说明。
            var offline = Ready();
            offline.Connected = false;
            Assert.That(SceneChannelWindow.DescribeStatus(View(lines, offline), out isError),
                Is.EqualTo(SceneChannelModels.DescribeBlock(SceneChannelBlockReason.NotReady)));
            Assert.That(isError, Is.False);

            // 默认视图(什么都没有)不抛。
            Assert.That(SceneChannelWindow.DescribeStatus(default, out isError), Is.Empty);
        }

        [Test]
        public void Cooldown_ShowsRemainingSeconds_TicksWithSetCooldown_AndReenablesRowsWhenItEnds()
        {
            var cooling = Ready();
            cooling.CooldownRemainingSeconds = 7.4f;
            _window.SetView(View(SevenLines(), cooling, SceneChannelClient.SwitchedText(2)));
            _window.Show();

            Assert.That(_window.StatusText, Is.EqualTo("切线冷却中，8秒后可再次切线。"), "向上取整");
            Assert.That(_window.StatusIsError, Is.False);
            AssertNoRowIsClickable();

            _window.SetCooldown(7.2f);
            Assert.That(_window.StatusText, Is.EqualTo("切线冷却中，8秒后可再次切线。"));
            _window.SetCooldown(6.9f);
            Assert.That(_window.StatusText, Is.EqualTo("切线冷却中，7秒后可再次切线。"));
            AssertNoRowIsClickable();
            Click(LineName(2));
            Assert.That(_switches, Is.Empty);

            // 冷却走完:各行按线自己的状态恢复可点,状态行回到数据层留着的那句话。
            _window.SetCooldown(0f);
            Assert.That(FindButton(LineName(2)).interactable, Is.True);
            Assert.That(FindButton(LineName(3)).interactable, Is.False);
            Assert.That(_window.StatusText, Is.EqualTo("已切换到 2线。"));
            Click(LineName(2));
            Assert.That(_switches, Is.EqualTo(new[] { SceneId(2) }));

            // 隐藏时只记数,不重画;再打开时按记下的值画。
            _window.Hide();
            _window.SetCooldown(3f);
            _window.Show();
            Assert.That(_window.StatusText, Is.EqualTo("切线冷却中，3秒后可再次切线。"));
            AssertNoRowIsClickable();
        }

        // ── 没有线路可列 ────────────────────────────────────────────────────

        [Test]
        public void NoLines_ShowsAPlaceholderInsteadOfRows()
        {
            var view = new SceneChannelPanelView
            {
                Lines = SceneChannelModels.NoLines,
                Context = new SceneChannelSwitchContext { Connected = true },
                Loading = true,
            };
            _window.SetView(view);
            _window.Show();
            Assert.That(LineNames(), Is.Empty);
            Assert.That(Label(SceneChannelWindow.EmptyLabelName).text, Is.EqualTo(SceneChannelWindow.LoadingText));
            Assert.That(Label("SceneChannelCurrent").text, Is.Empty);
            Assert.That(_window.StatusText, Is.Empty);
            Assert.That(_window.PageCount, Is.EqualTo(1));
            Assert.That(FindButton(SceneChannelWindow.RefreshButtonName).interactable, Is.False);

            // 服务端答复了、但这个场景没有目录(副本 / 镜像 / 目录未发布)。
            view.Loading = false;
            _window.SetView(view);
            Assert.That(Label(SceneChannelWindow.EmptyLabelName).text, Is.EqualTo(SceneChannelWindow.NoDirectoryText));
            Assert.That(CountLabels(SceneChannelWindow.EmptyLabelName), Is.EqualTo(1), "占位文字随列表重建,不叠加");
            Assert.That(FindButton(SceneChannelWindow.RefreshButtonName).interactable, Is.True);

            // 默认视图:没有连接。
            _window.SetView(default);
            Assert.That(Label(SceneChannelWindow.EmptyLabelName).text,
                Is.EqualTo(SceneChannelModels.DescribeBlock(SceneChannelBlockReason.NotReady)));

            // 目录到了:占位文字让位给行。
            _window.SetView(View(SevenLines()));
            Assert.That(CountLabels(SceneChannelWindow.EmptyLabelName), Is.Zero);
            Assert.That(LineNames().Count, Is.EqualTo(7));
        }

        // ── 与数据层接起来 ──────────────────────────────────────────────────

        [Test]
        public void DrivenByTheClient_ClickSendsEnterSceneForThatLine_AndThePanelFollowsTheSwitchToItsEnd()
        {
            float now = 0f;
            ulong sceneId = 100;
            var net = new SceneChannelFakeTransport();
            var client = new SceneChannelClient(net, () => net.Identity, () => sceneId, () => 1u, () => now);
            var arrived = new List<uint>();
            try
            {
                // 接线照 SceneChannelUiRoot:变化通知 → 整份视图;面板意图 → 数据层。
                client.Changed += () => _window.SetView(SceneChannelUiRoot.BuildView(client));
                client.Switched += channelNo => arrived.Add(channelNo);
                _window.SwitchRequested += id => client.RequestSwitch(id);
                _window.SetView(SceneChannelUiRoot.BuildView(client));
                _window.Show();
                Assert.That(Label(SceneChannelWindow.EmptyLabelName).text, Is.EqualTo(SceneChannelWindow.NoDirectoryText));

                Assert.That(client.RequestList(), Is.True);
                Assert.That(Label(SceneChannelWindow.EmptyLabelName).text, Is.EqualTo(SceneChannelWindow.LoadingText));

                net.Push(MessageIds.NotifySceneInfo, new SceneInfoS2C { ChannelDirectory = ThreeLineDirectory() });
                Assert.That(LineNames(), Is.EqualTo(Names(1, 3)));
                Assert.That(Label("SceneChannelCurrent").text, Is.EqualTo("当前：1线"));
                Assert.That(FindButton(LineName(1)).interactable, Is.False);
                Assert.That(FindButton(LineName(2)).interactable, Is.True);
                Assert.That(FindButton(LineName(3)).interactable, Is.False);

                // 点 2线:发出去的是 EnterScene(当前地图, 2线的 scene_id);面板进入「切换中」。
                Click(LineName(2));
                Assert.That(net.Calls.Count, Is.EqualTo(1));
                Assert.That(net.Calls[0].Id, Is.EqualTo(MessageIds.EnterScene));
                var request = (EnterSceneC2SRequest)net.Calls[0].Request;
                Assert.That(request.SceneInfo.SceneConfigId, Is.EqualTo(1u));
                Assert.That(request.SceneInfo.SceneId, Is.EqualTo(200UL));
                Assert.That(_window.StatusText, Is.EqualTo("正在切换到 2线…"));
                AssertNoRowIsClickable();
                Assert.That(Texts(FindButton(LineName(2))), Is.EqualTo(new[] { "2线", SceneChannelWindow.SwitchingRowText }));
                Assert.That(FindButton(SceneChannelWindow.RefreshButtonName).interactable, Is.False);

                // 受理 → 抵达:旧目录作废,新目录排队(离上次列线不足 2.5 秒),面板显示正在获取。
                net.Calls[0].Reply(new EnterSceneC2SResponse());
                now = 1f;
                sceneId = 200;
                client.HandleSceneEntered(new SceneInfoComp { SceneConfigId = 1, SceneId = 200 });
                Assert.That(arrived, Is.EqualTo(new[] { 2u }));
                Assert.That(LineNames(), Is.Empty);
                Assert.That(Label(SceneChannelWindow.EmptyLabelName).text, Is.EqualTo(SceneChannelWindow.LoadingText));
                Assert.That(_window.StatusText, Is.Empty);

                // 新目录到:所在线变成 2线,整张面板因冷却置灰(10 秒冷却从 1 秒起算,此刻剩 8.5 秒)。
                now = 2.5f;
                client.Tick();
                Assert.That(net.OneWays.Count, Is.EqualTo(2));
                net.Push(MessageIds.NotifySceneInfo, new SceneInfoS2C { ChannelDirectory = ThreeLineDirectory() });
                Assert.That(Label("SceneChannelCurrent").text, Is.EqualTo("当前：2线"));
                Assert.That(Texts(FindButton(LineName(2))), Is.EqualTo(new[] { "2线", SceneChannelWindow.CurrentTagText, "繁忙" }));
                AssertNoRowIsClickable();
                Assert.That(_window.StatusText, Is.EqualTo("切线冷却中，9秒后可再次切线。"));

                // 冷却到点:数据层发一次变化通知,1线恢复可点。
                now = 11f;
                client.Tick();
                Assert.That(FindButton(LineName(1)).interactable, Is.True);
                Assert.That(FindButton(LineName(2)).interactable, Is.False, "所在线");
                Assert.That(FindButton(LineName(3)).interactable, Is.False, "爆满");
                Assert.That(_window.StatusText, Is.EqualTo("已切换到 2线。"));
            }
            finally
            {
                client.Dispose();
            }
        }

        [Test]
        public void BuildView_NullClient_IsTheNotReadyDefault()
        {
            var view = SceneChannelUiRoot.BuildView(null);
            Assert.That(view.Lines, Is.Null);
            Assert.That(view.Context.Connected, Is.False);
            Assert.That(view.Loading, Is.False);
            Assert.That(view.Status, Is.Null);

            _window.SetView(view);
            _window.Show();
            Assert.That(LineNames(), Is.Empty);
            Assert.That(Label(SceneChannelWindow.EmptyLabelName).text,
                Is.EqualTo(SceneChannelModels.DescribeBlock(SceneChannelBlockReason.NotReady)));
            Assert.That(_window.StatusText, Is.Empty);
        }

        // ── 角标 ────────────────────────────────────────────────────────────

        [Test]
        public void EntryLabel_ShowsTheLineNumber_OrHidesWithoutADirectory()
        {
            Assert.That(SceneChannelUiRoot.EntryLabel(false, 0, false), Is.Null, "没有目录:不显示角标");
            // 线名后面带快捷键提示,写法与其它 HUD 入口一致(「组队 [T]」)。
            Assert.That(SceneChannelUiRoot.EntryLabel(true, 3, false), Is.EqualTo("3线 [L]"));
            Assert.That(SceneChannelUiRoot.EntryLabel(true, 12, false), Is.EqualTo("12线 [L]"));
            Assert.That(SceneChannelUiRoot.EntryLabel(true, 0, false), Is.EqualTo("线路 [L]"), "目录里找不到所在线");
            Assert.That(SceneChannelUiRoot.SwitchingEntryText, Is.EqualTo("切换中…"), "切换中不带快捷键提示");
            Assert.That(SceneChannelUiRoot.EntryLabel(true, 3, true), Is.EqualTo(SceneChannelUiRoot.SwitchingEntryText));
            Assert.That(SceneChannelUiRoot.EntryLabel(false, 0, true), Is.EqualTo(SceneChannelUiRoot.SwitchingEntryText));
        }

        [Test]
        public void EntryLabel_JustArrivedByOwnSwitch_ShowsThatLineUntilTheDirectoryArrives()
        {
            Assert.That(SceneChannelUiRoot.EntryLabel(false, 0, false, 2), Is.EqualTo("2线 [L]"), "已抵达 2线,新目录还没到");
            Assert.That(SceneChannelUiRoot.EntryLabel(true, 3, false, 2), Is.EqualTo("3线 [L]"), "目录到了,以目录为准");
            Assert.That(SceneChannelUiRoot.EntryLabel(true, 0, false, 2), Is.EqualTo("线路 [L]"), "目录里找不到所在线,也以目录为准");
            Assert.That(SceneChannelUiRoot.EntryLabel(false, 0, true, 2), Is.EqualTo(SceneChannelUiRoot.SwitchingEntryText));
        }

        [Test]
        public void CreateEntry_StartsWithTheHotkeyHint_LikeTheOtherHudEntries()
        {
            var entry = SceneChannelUiRoot.CreateEntry(_root.transform, null);
            Assert.That(entry.name, Is.EqualTo(SceneChannelUiRoot.EntryName));
            Assert.That(Texts(entry), Is.EqualTo(new[] { "线路 [L]" }));
        }

        [Test]
        public void BackgroundRetryDelay_BacksOffAndStopsAfterThreeAttempts()
        {
            // 面板关着、角标缺线号时的补问:进场后 15 / 30 / 60 秒各一次,之后不再问(正无穷 = 永不到点)。
            Assert.That(SceneChannelUiRoot.BackgroundRetryLimit, Is.EqualTo(3));
            Assert.That(SceneChannelUiRoot.BackgroundRetryDelay(0), Is.EqualTo(15f));
            Assert.That(SceneChannelUiRoot.BackgroundRetryDelay(1), Is.EqualTo(30f));
            Assert.That(SceneChannelUiRoot.BackgroundRetryDelay(2), Is.EqualTo(60f));
            Assert.That(float.IsPositiveInfinity(SceneChannelUiRoot.BackgroundRetryDelay(3)), Is.True);
            Assert.That(float.IsPositiveInfinity(SceneChannelUiRoot.BackgroundRetryDelay(100)), Is.True);
            Assert.That(float.IsPositiveInfinity(SceneChannelUiRoot.BackgroundRetryDelay(-1)), Is.True);
        }

        // ── 小工具 ──────────────────────────────────────────────────────────

        private static ulong SceneId(uint channelNo) => 1000UL + channelNo;

        private static string LineName(uint channelNo) => SceneChannelWindow.LineButtonPrefix + channelNo;

        /// <summary>线号 first..last 对应的行按钮名。</summary>
        private static List<string> Names(uint first, uint last)
        {
            var names = new List<string>();
            for (uint channelNo = first; channelNo <= last; channelNo++) names.Add(LineName(channelNo));
            return names;
        }

        private static SceneChannelLine Line(uint channelNo, SceneChannelLoad load, bool current = false) =>
            new SceneChannelLine(SceneId(channelNo), channelNo, 10 * channelNo, load, current);

        /// <summary>1线 流畅·当前 / 2线 繁忙 / 3线 爆满 / 4线 回收中 / 5线 暂不可用 / 6线 未知 / 7线 流畅。</summary>
        private static List<SceneChannelLine> SevenLines() => new List<SceneChannelLine>
        {
            Line(1, SceneChannelLoad.Smooth, true),
            Line(2, SceneChannelLoad.Busy),
            Line(3, SceneChannelLoad.Full),
            Line(4, SceneChannelLoad.Closing),
            Line(5, SceneChannelLoad.Unavailable),
            Line(6, SceneChannelLoad.Unknown),
            Line(7, SceneChannelLoad.Smooth),
        };

        /// <summary>线号 1..count,全部流畅,<paramref name="current"/> 是所在线。</summary>
        private static List<SceneChannelLine> ManyLines(uint count, uint current = 1)
        {
            var lines = new List<SceneChannelLine>();
            for (uint channelNo = 1; channelNo <= count; channelNo++)
                lines.Add(Line(channelNo, SceneChannelLoad.Smooth, channelNo == current));
            return lines;
        }

        /// <summary>可以切线的基准状态:已连接、目录就绪、服务端开放切线、不在冷却。</summary>
        private static SceneChannelSwitchContext Ready() =>
            new SceneChannelSwitchContext { Connected = true, HasDirectory = true, SwitchEnabled = true };

        private static SceneChannelPanelView View(IReadOnlyList<SceneChannelLine> lines) => View(lines, Ready());

        private static SceneChannelPanelView View(IReadOnlyList<SceneChannelLine> lines, SceneChannelSwitchContext context,
            string status = "", bool isError = false) =>
            new SceneChannelPanelView { Lines = lines, Context = context, Status = status, StatusIsError = isError };

        /// <summary>地图 1 的三条线:scene 100 = 1线 流畅,200 = 2线 繁忙,300 = 3线 爆满;冷却 10 秒。</summary>
        private static SceneChannelDirectory ThreeLineDirectory()
        {
            var directory = new SceneChannelDirectory
            {
                ZoneId = 1, SceneConfigId = 1, SwitchCooldownSeconds = 10, MaxPlayersPerChannel = 100, SwitchEnabled = true,
            };
            directory.Channels.Add(new SceneChannelInfo { SceneId = 100, ChannelNo = 1, PlayerCount = 5, State = SceneChannelState.Smooth });
            directory.Channels.Add(new SceneChannelInfo { SceneId = 200, ChannelNo = 2, PlayerCount = 70, State = SceneChannelState.Busy });
            directory.Channels.Add(new SceneChannelInfo { SceneId = 300, ChannelNo = 3, PlayerCount = 100, State = SceneChannelState.Full });
            return directory;
        }

        /// <summary>整张面板因 <paramref name="reason"/> 置灰:没有一行可点,状态行是对应说明且不标红。</summary>
        private void AssertWholePanelBlocked(SceneChannelSwitchContext context, SceneChannelBlockReason reason)
        {
            _window.SetView(View(SevenLines(), context));
            _window.Show();
            AssertNoRowIsClickable();
            Assert.That(_window.StatusText, Is.EqualTo(SceneChannelModels.DescribeBlock(reason)), reason.ToString());
            Assert.That(_window.StatusText, Is.Not.Empty, reason.ToString());
            Assert.That(_window.StatusIsError, Is.False, reason.ToString());
            Click(LineName(2));
            Click(LineName(7));
        }

        private void AssertNoRowIsClickable()
        {
            var rows = LineButtons();
            Assert.That(rows, Is.Not.Empty);
            foreach (var row in rows) Assert.That(row.interactable, Is.False, row.name);
        }

        private List<Button> LineButtons()
        {
            var rows = new List<Button>();
            foreach (var button in _root.GetComponentsInChildren<Button>(true))
                if (button.name.StartsWith(SceneChannelWindow.LineButtonPrefix, System.StringComparison.Ordinal)) rows.Add(button);
            return rows;
        }

        /// <summary>行按钮名,按建出来的先后(即线号)排列。</summary>
        private List<string> LineNames()
        {
            var names = new List<string>();
            foreach (var row in LineButtons()) names.Add(row.name);
            return names;
        }

        /// <summary>一个按钮里的全部文字,按层级先后。</summary>
        private static List<string> Texts(Button button)
        {
            var texts = new List<string>();
            foreach (var label in button.GetComponentsInChildren<TMP_Text>(true)) texts.Add(label.text);
            return texts;
        }

        private Vector2 Position(string buttonName) =>
            ((RectTransform)FindButton(buttonName).transform).anchoredPosition;

        private void Click(string name) => FindButton(name).onClick.Invoke();

        private Button FindButton(string name)
        {
            foreach (var button in _root.GetComponentsInChildren<Button>(true))
                if (button.name == name) return button;
            Assert.Fail("未找到按钮：" + name);
            return null;
        }

        private TMP_Text Label(string name)
        {
            foreach (var label in _root.GetComponentsInChildren<TMP_Text>(true))
                if (label.name == name) return label;
            Assert.Fail("未找到文字：" + name);
            return null;
        }

        private int CountLabels(string name)
        {
            int count = 0;
            foreach (var label in _root.GetComponentsInChildren<TMP_Text>(true))
                if (label.name == name) count++;
            return count;
        }
    }
}
