using System.Collections.Generic;
using MmorpgClient.Game.WorldTravel;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class SceneChannelModelsTests
    {
        // ── 映射与文案 ──────────────────────────────────────────────────────

        [Test]
        public void ToLoad_MapsEveryKnownState_AndUnknownValuesFailClosed()
        {
            Assert.That(SceneChannelModels.ToLoad(SceneChannelState.Smooth), Is.EqualTo(SceneChannelLoad.Smooth));
            Assert.That(SceneChannelModels.ToLoad(SceneChannelState.Busy), Is.EqualTo(SceneChannelLoad.Busy));
            Assert.That(SceneChannelModels.ToLoad(SceneChannelState.Full), Is.EqualTo(SceneChannelLoad.Full));
            Assert.That(SceneChannelModels.ToLoad(SceneChannelState.Closing), Is.EqualTo(SceneChannelLoad.Closing));
            Assert.That(SceneChannelModels.ToLoad(SceneChannelState.Unavailable), Is.EqualTo(SceneChannelLoad.Unavailable));
            Assert.That(SceneChannelModels.ToLoad(SceneChannelState.Unknown), Is.EqualTo(SceneChannelLoad.Unknown));
            // 服务端以后加的状态:本客户端不认识,按未知处理(不可选)。
            Assert.That(SceneChannelModels.ToLoad((SceneChannelState)99), Is.EqualTo(SceneChannelLoad.Unknown));
        }

        [Test]
        public void LineNameAndLoadText_UseEstablishedWording()
        {
            Assert.That(SceneChannelModels.LineName(1), Is.EqualTo("1线"));
            Assert.That(SceneChannelModels.LineName(12), Is.EqualTo("12线"));
            Assert.That(SceneChannelModels.LineName(0), Is.EqualTo("线路"), "目录里找不到当前线时的角标文案");

            Assert.That(SceneChannelModels.LoadText(SceneChannelLoad.Smooth), Is.EqualTo("流畅"));
            Assert.That(SceneChannelModels.LoadText(SceneChannelLoad.Busy), Is.EqualTo("繁忙"));
            Assert.That(SceneChannelModels.LoadText(SceneChannelLoad.Full), Is.EqualTo("爆满"));
            Assert.That(SceneChannelModels.LoadText(SceneChannelLoad.Closing), Is.EqualTo("回收中"));
            Assert.That(SceneChannelModels.LoadText(SceneChannelLoad.Unavailable), Is.EqualTo("暂不可用"));
            Assert.That(SceneChannelModels.LoadText(SceneChannelLoad.Unknown), Is.EqualTo("未知"));
            Assert.That(SceneChannelModels.LoadText((SceneChannelLoad)99), Is.EqualTo("未知"));
        }

        // ── 线路列表 ────────────────────────────────────────────────────────

        [Test]
        public void BuildLines_NullOrEmptyDirectory_GivesEmptyList()
        {
            Assert.That(SceneChannelModels.BuildLines(null, 100), Is.Empty);
            Assert.That(SceneChannelModels.BuildLines(new SceneChannelDirectory { SceneConfigId = 1 }, 100), Is.Empty);
            Assert.That(SceneChannelModels.NoLines, Is.Empty);
        }

        [Test]
        public void BuildLines_SortsByChannelNo_DropsDirtyEntries_KeepsFirstDuplicate()
        {
            var directory = new SceneChannelDirectory { SceneConfigId = 1 };
            directory.Channels.Add(Info(300, 3, 30, SceneChannelState.Busy));
            directory.Channels.Add(Info(100, 1, 10, SceneChannelState.Smooth));
            directory.Channels.Add(Info(0, 5, 50, SceneChannelState.Smooth));      // scene_id == 0:切不过去
            directory.Channels.Add(Info(500, 0, 50, SceneChannelState.Smooth));    // channel_no == 0:显示不出线名
            directory.Channels.Add(Info(100, 7, 70, SceneChannelState.Full));      // scene_id 重复:保留先出现的 1线
            directory.Channels.Add(Info(700, 3, 70, SceneChannelState.Full));      // 线号重复:保留先出现的 scene 300
            directory.Channels.Add(Info(200, 2, 0, SceneChannelState.Full));
            // 上面被丢弃的 (100, 7) 不占 7 这个线号:后面真正的 7线照常保留。
            directory.Channels.Add(Info(800, 7, 7, SceneChannelState.Closing));

            var lines = SceneChannelModels.BuildLines(directory, 200);

            Assert.That(lines.Count, Is.EqualTo(4));
            AssertLine(lines[0], 100, 1, 10, SceneChannelLoad.Smooth, false);
            AssertLine(lines[1], 200, 2, 0, SceneChannelLoad.Full, true);
            AssertLine(lines[2], 300, 3, 30, SceneChannelLoad.Busy, false);
            AssertLine(lines[3], 800, 7, 7, SceneChannelLoad.Closing, false);
        }

        [Test]
        public void CurrentChannelNo_FindsCurrentLine_OrZero()
        {
            var directory = new SceneChannelDirectory { SceneConfigId = 1 };
            directory.Channels.Add(Info(100, 1, 10, SceneChannelState.Smooth));
            directory.Channels.Add(Info(200, 2, 20, SceneChannelState.Busy));

            Assert.That(SceneChannelModels.CurrentChannelNo(SceneChannelModels.BuildLines(directory, 200)), Is.EqualTo(2));
            Assert.That(SceneChannelModels.CurrentChannelNo(SceneChannelModels.BuildLines(directory, 100)), Is.EqualTo(1));
            Assert.That(SceneChannelModels.CurrentChannelNo(SceneChannelModels.BuildLines(directory, 999)), Is.Zero,
                "当前 scene_id 不在目录里");
            Assert.That(SceneChannelModels.CurrentChannelNo(SceneChannelModels.BuildLines(directory, 0)), Is.Zero,
                "还没进场时不标任何一条为当前线");
            Assert.That(SceneChannelModels.CurrentChannelNo(null), Is.Zero);
            Assert.That(SceneChannelModels.CurrentChannelNo(SceneChannelModels.NoLines), Is.Zero);
        }

        [Test]
        public void FindLine_ByScene_OrNull()
        {
            var directory = new SceneChannelDirectory { SceneConfigId = 1 };
            directory.Channels.Add(Info(100, 1, 10, SceneChannelState.Smooth));
            directory.Channels.Add(Info(300, 3, 30, SceneChannelState.Busy));
            var lines = SceneChannelModels.BuildLines(directory, 100);

            Assert.That(SceneChannelModels.FindLine(lines, 300).ChannelNo, Is.EqualTo(3));
            Assert.That(SceneChannelModels.FindLine(lines, 12345), Is.Null);
            Assert.That(SceneChannelModels.FindLine(lines, 0), Is.Null);
            Assert.That(SceneChannelModels.FindLine(null, 100), Is.Null);
        }

        // ── 能不能切 ────────────────────────────────────────────────────────

        [Test]
        public void EvaluateLine_OnlySmoothAndBusyAreSelectable()
        {
            Assert.That(SceneChannelModels.EvaluateLine(Line(SceneChannelLoad.Smooth)), Is.EqualTo(SceneChannelBlockReason.None));
            Assert.That(SceneChannelModels.EvaluateLine(Line(SceneChannelLoad.Busy)), Is.EqualTo(SceneChannelBlockReason.None));
            Assert.That(SceneChannelModels.EvaluateLine(Line(SceneChannelLoad.Full)), Is.EqualTo(SceneChannelBlockReason.Full));
            Assert.That(SceneChannelModels.EvaluateLine(Line(SceneChannelLoad.Closing)), Is.EqualTo(SceneChannelBlockReason.Closing));
            Assert.That(SceneChannelModels.EvaluateLine(Line(SceneChannelLoad.Unavailable)),
                Is.EqualTo(SceneChannelBlockReason.Unavailable));
            Assert.That(SceneChannelModels.EvaluateLine(Line(SceneChannelLoad.Unknown)),
                Is.EqualTo(SceneChannelBlockReason.UnknownState));
            Assert.That(SceneChannelModels.EvaluateLine(Line((SceneChannelLoad)99)),
                Is.EqualTo(SceneChannelBlockReason.UnknownState));
            Assert.That(SceneChannelModels.EvaluateLine(null), Is.EqualTo(SceneChannelBlockReason.LineMissing));
        }

        [Test]
        public void EvaluateLine_CurrentLineWinsOverItsLoad()
        {
            Assert.That(SceneChannelModels.EvaluateLine(Line(SceneChannelLoad.Smooth, current: true)),
                Is.EqualTo(SceneChannelBlockReason.CurrentLine));
            Assert.That(SceneChannelModels.EvaluateLine(Line(SceneChannelLoad.Full, current: true)),
                Is.EqualTo(SceneChannelBlockReason.CurrentLine));
            Assert.That(SceneChannelModels.EvaluateLine(Line(SceneChannelLoad.Closing, current: true)),
                Is.EqualTo(SceneChannelBlockReason.CurrentLine));
        }

        [Test]
        public void Evaluate_ReadyContext_FallsThroughToLineReason()
        {
            var ready = Ready();
            Assert.That(SceneChannelModels.EvaluateGlobal(ready), Is.EqualTo(SceneChannelBlockReason.None));
            Assert.That(SceneChannelModels.Evaluate(Line(SceneChannelLoad.Smooth), ready), Is.EqualTo(SceneChannelBlockReason.None));
            Assert.That(SceneChannelModels.Evaluate(Line(SceneChannelLoad.Busy), ready), Is.EqualTo(SceneChannelBlockReason.None));
            Assert.That(SceneChannelModels.Evaluate(Line(SceneChannelLoad.Full), ready), Is.EqualTo(SceneChannelBlockReason.Full));
            Assert.That(SceneChannelModels.Evaluate(Line(SceneChannelLoad.Smooth, current: true), ready),
                Is.EqualTo(SceneChannelBlockReason.CurrentLine));
            Assert.That(SceneChannelModels.Evaluate(null, ready), Is.EqualTo(SceneChannelBlockReason.LineMissing));
        }

        [Test]
        public void Evaluate_DefaultContext_IsNotReady()
        {
            Assert.That(SceneChannelModels.Evaluate(Line(SceneChannelLoad.Smooth), default),
                Is.EqualTo(SceneChannelBlockReason.NotReady));
        }

        [Test]
        public void EvaluateGlobal_PriorityChain_PeelsOffOneReasonAtATime()
        {
            // 所有全局原因同时成立,然后自上而下逐个解除:每一步露出来的就是下一优先级。
            var context = new SceneChannelSwitchContext
            {
                Connected = false, InBattle = true, Travelling = true, SwitchPending = true, TeamFollower = true,
                HasDirectory = false, SwitchEnabled = false, CooldownRemainingSeconds = 3f,
            };
            Assert.That(SceneChannelModels.EvaluateGlobal(context), Is.EqualTo(SceneChannelBlockReason.NotReady), "未连接");

            context.Connected = true;
            Assert.That(SceneChannelModels.EvaluateGlobal(context), Is.EqualTo(SceneChannelBlockReason.InBattle));

            context.InBattle = false;
            Assert.That(SceneChannelModels.EvaluateGlobal(context), Is.EqualTo(SceneChannelBlockReason.Travelling));

            context.Travelling = false;
            Assert.That(SceneChannelModels.EvaluateGlobal(context), Is.EqualTo(SceneChannelBlockReason.Switching));

            context.SwitchPending = false;
            Assert.That(SceneChannelModels.EvaluateGlobal(context), Is.EqualTo(SceneChannelBlockReason.TeamMember));

            context.TeamFollower = false;
            Assert.That(SceneChannelModels.EvaluateGlobal(context), Is.EqualTo(SceneChannelBlockReason.NotReady), "目录未就绪");

            context.HasDirectory = true;
            Assert.That(SceneChannelModels.EvaluateGlobal(context), Is.EqualTo(SceneChannelBlockReason.SwitchDisabled));

            context.SwitchEnabled = true;
            Assert.That(SceneChannelModels.EvaluateGlobal(context), Is.EqualTo(SceneChannelBlockReason.CoolingDown));

            context.CooldownRemainingSeconds = 0f;
            Assert.That(SceneChannelModels.EvaluateGlobal(context), Is.EqualTo(SceneChannelBlockReason.None));
        }

        [Test]
        public void Evaluate_GlobalReasonsWinOverLineReasons()
        {
            var full = Line(SceneChannelLoad.Full);
            var current = Line(SceneChannelLoad.Smooth, current: true);

            var inBattle = Ready();
            inBattle.InBattle = true;
            Assert.That(SceneChannelModels.Evaluate(full, inBattle), Is.EqualTo(SceneChannelBlockReason.InBattle));
            Assert.That(SceneChannelModels.Evaluate(current, inBattle), Is.EqualTo(SceneChannelBlockReason.InBattle));
            Assert.That(SceneChannelModels.Evaluate(null, inBattle), Is.EqualTo(SceneChannelBlockReason.InBattle));

            var cooling = Ready();
            cooling.CooldownRemainingSeconds = 0.01f;
            Assert.That(SceneChannelModels.Evaluate(full, cooling), Is.EqualTo(SceneChannelBlockReason.CoolingDown));
            Assert.That(SceneChannelModels.Evaluate(current, cooling), Is.EqualTo(SceneChannelBlockReason.CoolingDown));

            var teamFollower = Ready();
            teamFollower.TeamFollower = true;
            Assert.That(SceneChannelModels.Evaluate(Line(SceneChannelLoad.Smooth), teamFollower),
                Is.EqualTo(SceneChannelBlockReason.TeamMember));

            var disabled = Ready();
            disabled.SwitchEnabled = false;
            Assert.That(SceneChannelModels.Evaluate(Line(SceneChannelLoad.Closing), disabled),
                Is.EqualTo(SceneChannelBlockReason.SwitchDisabled));
        }

        // ── 不可切原因的文案 ────────────────────────────────────────────────

        [Test]
        public void DescribeBlock_NoneIsEmpty_EveryOtherReasonHasItsOwnSentence()
        {
            Assert.That(SceneChannelModels.DescribeBlock(SceneChannelBlockReason.None), Is.Empty);

            var seen = new HashSet<string>();
            foreach (SceneChannelBlockReason reason in System.Enum.GetValues(typeof(SceneChannelBlockReason)))
            {
                if (reason == SceneChannelBlockReason.None) continue;
                string text = SceneChannelModels.DescribeBlock(reason, 4f);
                Assert.That(text, Is.Not.Empty, reason.ToString());
                Assert.That(seen.Add(text), Is.True, "文案重复:" + reason);
            }

            Assert.That(SceneChannelModels.DescribeBlock(SceneChannelBlockReason.TeamMember),
                Is.EqualTo("队伍中由队长切线，或先离队。"));
            // 宿主把排队匹配中也算进 InBattle:文案对战斗和匹配两种情况都要成立。
            Assert.That(SceneChannelModels.DescribeBlock(SceneChannelBlockReason.InBattle),
                Is.EqualTo("战斗或匹配中无法切线。"));
            Assert.That(SceneChannelModels.DescribeBlock(SceneChannelBlockReason.CurrentLine), Is.EqualTo("已在该线路。"));
            Assert.That(SceneChannelModels.DescribeBlock(SceneChannelBlockReason.Full), Is.EqualTo("该线路已满，请选择其他线路。"));
        }

        [Test]
        public void DescribeBlock_Cooldown_RoundsRemainingSecondsUp()
        {
            Assert.That(SceneChannelModels.DescribeBlock(SceneChannelBlockReason.CoolingDown, 2.1f),
                Is.EqualTo("切线冷却中，3秒后可再次切线。"));
            Assert.That(SceneChannelModels.DescribeBlock(SceneChannelBlockReason.CoolingDown, 3f),
                Is.EqualTo("切线冷却中，3秒后可再次切线。"));
            Assert.That(SceneChannelModels.DescribeBlock(SceneChannelBlockReason.CoolingDown, 0.01f),
                Is.EqualTo("切线冷却中，1秒后可再次切线。"));
            Assert.That(SceneChannelModels.DescribeBlock(SceneChannelBlockReason.CoolingDown, 10f),
                Is.EqualTo("切线冷却中，10秒后可再次切线。"));
            // 没给剩余秒数(或已经到点)时不显示「0秒」。
            Assert.That(SceneChannelModels.DescribeBlock(SceneChannelBlockReason.CoolingDown),
                Is.EqualTo("切线冷却中，请稍候。"));
            // 别的原因不受秒数影响。
            Assert.That(SceneChannelModels.DescribeBlock(SceneChannelBlockReason.Full, 9f),
                Is.EqualTo(SceneChannelModels.DescribeBlock(SceneChannelBlockReason.Full)));
        }

        [Test]
        public void CooldownSecondsCeil_ClampsNonPositiveAndRoundsUp()
        {
            Assert.That(SceneChannelModels.CooldownSecondsCeil(0f), Is.Zero);
            Assert.That(SceneChannelModels.CooldownSecondsCeil(-1f), Is.Zero);
            Assert.That(SceneChannelModels.CooldownSecondsCeil(float.NaN), Is.Zero);
            Assert.That(SceneChannelModels.CooldownSecondsCeil(0.001f), Is.EqualTo(1));
            Assert.That(SceneChannelModels.CooldownSecondsCeil(1f), Is.EqualTo(1));
            Assert.That(SceneChannelModels.CooldownSecondsCeil(1.5f), Is.EqualTo(2));
            Assert.That(SceneChannelModels.CooldownSecondsCeil(9.99f), Is.EqualTo(10));
            Assert.That(SceneChannelModels.CooldownSecondsCeil(float.PositiveInfinity), Is.EqualTo(int.MaxValue));
        }

        // ── 小工具 ──────────────────────────────────────────────────────────

        /// <summary>什么都不拦的上下文:已连接、目录就绪、服务端允许切线、不在冷却。</summary>
        private static SceneChannelSwitchContext Ready() =>
            new SceneChannelSwitchContext { Connected = true, HasDirectory = true, SwitchEnabled = true };

        private static SceneChannelLine Line(SceneChannelLoad load, bool current = false) =>
            new SceneChannelLine(200, 2, 20, load, current);

        private static SceneChannelInfo Info(ulong sceneId, uint channelNo, uint players, SceneChannelState state) =>
            new SceneChannelInfo { SceneId = sceneId, ChannelNo = channelNo, PlayerCount = players, State = state };

        private static void AssertLine(SceneChannelLine line, ulong sceneId, uint channelNo, uint players,
                                       SceneChannelLoad load, bool current)
        {
            Assert.That(line.SceneId, Is.EqualTo(sceneId));
            Assert.That(line.ChannelNo, Is.EqualTo(channelNo));
            Assert.That(line.PlayerCount, Is.EqualTo(players));
            Assert.That(line.Load, Is.EqualTo(load));
            Assert.That(line.IsCurrent, Is.EqualTo(current));
        }
    }
}
