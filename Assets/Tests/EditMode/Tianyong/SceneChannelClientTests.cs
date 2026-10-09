using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.WorldTravel;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    /// <summary>
    /// 切线网络层的状态机。时间全部由注入的时钟推进;默认场景:地图 1 有四条线
    /// (1线 流畅 / 2线 繁忙 / 3线 爆满 / 4线 回收中),玩家在 1线,目录冷却 10 秒。
    /// </summary>
    public sealed class SceneChannelClientTests
    {
        private const uint Map = 1;
        private const ulong Line1 = 100;
        private const ulong Line2 = 200;
        private const ulong Line3 = 300;
        private const ulong Line4 = 400;

        private float _now;
        private ulong _sceneId;
        private uint _sceneConfigId;
        private SceneChannelFakeTransport _net;
        private SceneChannelClient _client;
        private int _changes;
        private readonly List<uint> _switched = new List<uint>();

        [SetUp]
        public void SetUp()
        {
            _now = 0f;
            _sceneId = Line1;
            _sceneConfigId = Map;
            _changes = 0;
            _switched.Clear();
            _net = new SceneChannelFakeTransport();
            _client = new SceneChannelClient(_net, () => _net.Identity, () => _sceneId, () => _sceneConfigId, () => _now);
            _client.Changed += () => _changes++;
            _client.Switched += channelNo => _switched.Add(channelNo);
        }

        [TearDown]
        public void TearDown() => _client.Dispose();

        // ── 构造 ────────────────────────────────────────────────────────────

        [Test]
        public void Construct_RegistersSceneInfoAndListReplyHandlers_AndSendsNothing()
        {
            Assert.That(_net.Notifies.Keys,
                Is.EquivalentTo(new[] { MessageIds.NotifySceneInfo, MessageIds.SceneInfoC2S }));
            Assert.That(_net.DisconnectedSubscriberCount, Is.EqualTo(1));
            Assert.That(_net.OneWays, Is.Empty, "构造不发请求");
            Assert.That(_net.Calls, Is.Empty);
            Assert.That(_client.Lines, Is.Empty);
            Assert.That(_client.HasDirectory, Is.False);
            Assert.That(_client.DirectoryApplicable, Is.True, "还没进场:不知道是不是副本,先当作有分线");
            Assert.That(_client.CurrentChannelNo, Is.Zero);
            Assert.That(_client.SwitchEnabled, Is.False);
            Assert.That(_client.ListPending || _client.ListQueued || _client.SwitchPending, Is.False);
            Assert.That(_client.CooldownRemainingSeconds, Is.Zero);
            Assert.That(_client.Status, Is.Empty);
            Assert.That(_client.StatusIsError, Is.False);
            Assert.That(_changes, Is.Zero);
        }

        // ── 列线:发送出口与限流 ─────────────────────────────────────────────

        [Test]
        public void RequestList_SendsSceneInfoRequest_AndMergesRepeatsUntilTheIntervalPasses()
        {
            Assert.That(_client.RequestList(), Is.True);
            Assert.That(_net.OneWays.Count, Is.EqualTo(1));
            Assert.That(_net.OneWays[0].Id, Is.EqualTo(MessageIds.SceneInfoC2S));
            Assert.That(_net.OneWays[0].Request, Is.InstanceOf<SceneInfoRequest>());
            Assert.That(((SceneInfoRequest)_net.OneWays[0].Request).WithChannelDirectory, Is.True, "不带开关服务端不会附带分线目录");
            Assert.That(_net.Calls, Is.Empty, "43 的应答类型是 Empty,不能用 Call 等");
            Assert.That(_client.ListPending, Is.True);
            Assert.That(_client.ListQueued, Is.False);
            Assert.That(_changes, Is.EqualTo(1));

            _now = 0.5f;
            PushDirectory(BuildDirectory());
            Assert.That(_client.ListPending, Is.False);

            // 2.5 秒内再要:不发,只记一个待补发标记;多次调用合并。
            _now = 1f;
            Assert.That(_client.RequestList(), Is.True);
            _now = 2f;
            Assert.That(_client.RequestList(), Is.True);
            _client.Tick();
            Assert.That(_net.OneWays.Count, Is.EqualTo(1));
            Assert.That(_client.ListQueued, Is.True);
            Assert.That(_client.ListPending, Is.True, "排着队也算尚无结论,界面照样显示「正在获取」");

            // 到点后由 Tick 补发恰好一次。
            _now = 2.5f;
            _client.Tick();
            Assert.That(_net.OneWays.Count, Is.EqualTo(2));
            Assert.That(_client.ListQueued, Is.False);
            Assert.That(_client.ListPending, Is.True);
            _client.Tick();
            _now = 4f;
            _client.Tick();
            Assert.That(_net.OneWays.Count, Is.EqualTo(2));
        }

        [Test]
        public void RequestList_WhileWaiting_IsNotResentUnlessForced_AndForceStillRespectsTheInterval()
        {
            _client.RequestList();
            _now = 3f;
            Assert.That(_client.RequestList(), Is.True, "已有请求在途,等它的结果");
            Assert.That(_net.OneWays.Count, Is.EqualTo(1));
            Assert.That(_client.ListQueued, Is.False);

            Assert.That(_client.RequestList(force: true), Is.True);
            Assert.That(_net.OneWays.Count, Is.EqualTo(2), "间隔已过,强制重发");

            _now = 3.5f;
            Assert.That(_client.RequestList(force: true), Is.True);
            Assert.That(_net.OneWays.Count, Is.EqualTo(2), "force 不绕过最小间隔");
            Assert.That(_client.ListQueued, Is.True);

            _now = 5.5f;
            _client.Tick();
            Assert.That(_net.OneWays.Count, Is.EqualTo(3));
        }

        [Test]
        public void RequestList_NoAnswerWithinFiveSeconds_TimesOut_AndLaterDirectoryClearsTheHint()
        {
            _client.RequestList();
            _now = 4.9f;
            _client.Tick();
            Assert.That(_client.ListPending, Is.True);
            Assert.That(_changes, Is.EqualTo(1));

            _now = 5f;
            _client.Tick();
            Assert.That(_client.ListPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.ListUnavailableMessage));
            Assert.That(_client.StatusIsError, Is.True);
            Assert.That(_changes, Is.EqualTo(2));
            Assert.That(_net.OneWays.Count, Is.EqualTo(1), "超时不自动重发");

            _now = 6f;
            _client.RequestList();
            Assert.That(_net.OneWays.Count, Is.EqualTo(2));
            PushDirectory(BuildDirectory());
            Assert.That(_client.HasDirectory, Is.True);
            Assert.That(_client.Status, Is.Empty, "目录到了,之前的「获取不到」收掉");
            Assert.That(_client.StatusIsError, Is.False);
        }

        [Test]
        public void RequestList_TransportNotReady_FailsWithoutSendingOrQueueing()
        {
            _net.IsReady = false;
            Assert.That(_client.RequestList(), Is.False);
            Assert.That(_net.OneWays, Is.Empty);
            Assert.That(_client.ListPending, Is.False);
            Assert.That(_client.ListQueued, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.ListUnavailableMessage));
            Assert.That(_client.StatusIsError, Is.True);
            Assert.That(_changes, Is.EqualTo(1));

            Assert.That(_client.RequestList(), Is.False);
            Assert.That(_changes, Is.EqualTo(1), "状态没变就不重复通知");

            _net.IsReady = true;
            _now = 10f;
            _client.Tick();
            Assert.That(_net.OneWays, Is.Empty, "未就绪时的请求不排队");
        }

        // ── 列线:收到 31 ────────────────────────────────────────────────────

        [Test]
        public void SceneInfo_WithDirectory_BuildsLinesAndMarksTheCurrentChannel()
        {
            _sceneId = Line2;
            LoadDirectory();

            Assert.That(_client.HasDirectory, Is.True);
            Assert.That(_client.ListPending, Is.False);
            Assert.That(_client.Lines.Count, Is.EqualTo(4));
            Assert.That(_client.Lines[0].SceneId, Is.EqualTo(Line1));
            Assert.That(_client.Lines[0].ChannelNo, Is.EqualTo(1));
            Assert.That(_client.Lines[0].PlayerCount, Is.EqualTo(12));
            Assert.That(_client.Lines[0].Load, Is.EqualTo(SceneChannelLoad.Smooth));
            Assert.That(_client.Lines[0].IsCurrent, Is.False);
            Assert.That(_client.Lines[1].SceneId, Is.EqualTo(Line2));
            Assert.That(_client.Lines[1].Load, Is.EqualTo(SceneChannelLoad.Busy));
            Assert.That(_client.Lines[1].IsCurrent, Is.True);
            Assert.That(_client.Lines[2].Load, Is.EqualTo(SceneChannelLoad.Full));
            Assert.That(_client.Lines[3].Load, Is.EqualTo(SceneChannelLoad.Closing));
            Assert.That(_client.CurrentChannelNo, Is.EqualTo(2));
            Assert.That(_client.SwitchEnabled, Is.True);
            Assert.That(_client.SwitchCooldownSeconds, Is.EqualTo(10));
            Assert.That(_client.Status, Is.Empty);
            Assert.That(_changes, Is.EqualTo(2), "发出一次 + 目录到达一次");
        }

        [Test]
        public void SceneInfo_Unsolicited_IsAppliedToo()
        {
            PushDirectory(BuildDirectory());
            Assert.That(_client.HasDirectory, Is.True);
            Assert.That(_client.CurrentChannelNo, Is.EqualTo(1));
            Assert.That(_changes, Is.EqualTo(1));
        }

        [Test]
        public void SceneInfo_CurrentSceneNotInDirectory_HasNoCurrentChannel()
        {
            _sceneId = 999;
            LoadDirectory();
            Assert.That(_client.HasDirectory, Is.True);
            Assert.That(_client.Lines.Count, Is.EqualTo(4));
            Assert.That(_client.CurrentChannelNo, Is.Zero, "角标退回显示「线路」");
        }

        [Test]
        public void SceneInfo_WithoutDirectory_MeansThisSceneHasNoChannels()
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestList();
            Assert.That(_client.ListPending, Is.True);

            var message = new SceneInfoS2C();
            message.SceneInfo.Add(Scene(Line1));
            _net.Push(MessageIds.NotifySceneInfo, message);

            Assert.That(_client.HasDirectory, Is.False);
            Assert.That(_client.Lines, Is.Empty);
            Assert.That(_client.CurrentChannelNo, Is.Zero);
            Assert.That(_client.SwitchEnabled, Is.False);
            Assert.That(_client.SwitchCooldownSeconds, Is.Zero);
            Assert.That(_client.ListPending, Is.False);
            Assert.That(_client.Status, Is.Empty);
            Assert.That(_client.DirectoryApplicable, Is.True, "大世界场景只是这次没带目录,之后还可以再问");
        }

        // ── 副本 / 镜像:没有分线,不列线 ─────────────────────────────────────

        [TestCase(0u, 7u)]
        [TestCase(9u, 0u)]
        public void SceneEntry_IntoAMirrorOrDungeon_SendsNoListRequest_UntilBackInTheWorld(uint mirrorConfigId, uint dungeonConfigId)
        {
            LoadDirectory();
            _now = 5f;
            _client.RequestList();          // 在上一个场景排着 / 等着的列线
            int before = _changes;

            _sceneId = 900;
            _client.HandleSceneEntered(new SceneInfoComp
            {
                SceneConfigId = Map, SceneId = 900, MirrorConfigId = mirrorConfigId, DungeonConfigId = dungeonConfigId,
            });

            Assert.That(_client.DirectoryApplicable, Is.False);
            Assert.That(_client.HasDirectory, Is.False);
            Assert.That(_client.Lines, Is.Empty);
            Assert.That(_client.ListPending, Is.False, "不排队也不等待:服务端对副本 / 镜像只回不带目录的 31");
            Assert.That(_client.ListQueued, Is.False);
            Assert.That(_net.OneWays.Count, Is.EqualTo(2), "进场没有发 43");
            Assert.That(_changes, Is.EqualTo(before + 1));

            // 之后谁来问都不发:补问、面板刷新走的都是这个出口。
            _now = 60f;
            Assert.That(_client.RequestList(), Is.False);
            Assert.That(_client.RequestList(force: true), Is.False);
            _client.Tick();
            Assert.That(_net.OneWays.Count, Is.EqualTo(2));
            Assert.That(_client.ListPending, Is.False);
            Assert.That(_client.Status, Is.Empty, "不是失败,不写提示;界面按没有目录自己说明");
            Assert.That(_changes, Is.EqualTo(before + 1));

            // 回到大世界:照常拉目录。
            Arrive(Line1);
            Assert.That(_client.DirectoryApplicable, Is.True);
            Assert.That(_net.OneWays.Count, Is.EqualTo(3));
            Assert.That(_client.ListPending, Is.True);
        }

        [Test]
        public void SwitchFailingAfterBeingPulledIntoADungeon_DoesNotListThere()
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestSwitch(Line2);
            // 同步应答还没到就被拉进了副本(队伍跟随):切线仍在途,但这里没有分线可列。
            _sceneId = 900;
            _client.HandleSceneEntered(new SceneInfoComp { SceneConfigId = Map, SceneId = 900, DungeonConfigId = 7 });
            Assert.That(_client.SwitchPending, Is.True);
            Assert.That(_net.OneWays.Count, Is.EqualTo(1));

            _net.Calls[0].Reply(Accepted());
            _client.HandleServerTip(Tip((uint)scene_error.KEnterSceneFailed));
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchFailedMessage));
            _now = 10f;
            _client.Tick();
            Assert.That(_net.OneWays.Count, Is.EqualTo(1), "失败后的顺手刷新在副本里不发");
            Assert.That(_client.ListPending, Is.False);
        }

        [Test]
        public void SceneInfo_SayingTheCurrentSceneIsADungeon_StopsListing()
        {
            // 宿主晚于进场才接上:错过了入场通知,靠 31 里的场景信息得知这里是副本。
            _sceneId = 900;
            Assert.That(_client.RequestList(), Is.True);
            _now = 1f;
            Assert.That(_client.RequestList(force: true), Is.True);   // 间隔未到 → 排着队
            Assert.That(_client.ListQueued, Is.True);

            // 别的场景的信息不作数。
            var other = new SceneInfoS2C();
            other.SceneInfo.Add(new SceneInfoComp { SceneConfigId = Map, SceneId = 901, DungeonConfigId = 5 });
            _net.Push(MessageIds.NotifySceneInfo, other);
            Assert.That(_client.DirectoryApplicable, Is.True);
            Assert.That(_client.ListQueued, Is.True);

            var message = new SceneInfoS2C();
            message.SceneInfo.Add(new SceneInfoComp { SceneConfigId = Map, SceneId = 900, DungeonConfigId = 5 });
            _net.Push(MessageIds.NotifySceneInfo, message);

            Assert.That(_client.DirectoryApplicable, Is.False);
            Assert.That(_client.HasDirectory, Is.False);
            Assert.That(_client.ListPending, Is.False, "排着队的补发也撤掉");
            _now = 10f;
            _client.Tick();
            Assert.That(_client.RequestList(), Is.False);
            Assert.That(_net.OneWays.Count, Is.EqualTo(1));
            Assert.That(_client.Status, Is.Empty);

            // 断线后不知道新会话在哪个场景:恢复成「有分线」,等入场通知。
            _net.Disconnect();
            Assert.That(_client.DirectoryApplicable, Is.True);
        }

        [Test]
        public void SceneInfo_ForAnotherMap_IsDiscarded()
        {
            _sceneConfigId = 2;
            _sceneId = 900;
            _client.RequestList();
            PushDirectory(BuildDirectory());   // 目录是地图 1 的
            Assert.That(_client.HasDirectory, Is.False);
            Assert.That(_client.Lines, Is.Empty);
            Assert.That(_client.ListPending, Is.False, "等待照样结束");

            // 还没进场(当前地图为 0)时,任何目录都对不上。
            _sceneConfigId = 0;
            var directory = BuildDirectory();
            directory.SceneConfigId = 0;
            PushDirectory(directory);
            Assert.That(_client.HasDirectory, Is.False);
            Assert.That(_client.Lines, Is.Empty);
        }

        [Test]
        public void SceneInfo_Malformed_NeverThrows_AndKeepsKnownLines()
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestList();

            // 字段 1 声称有 5 个字节的子消息,实际只有 1 个且是半截 varint。
            Assert.DoesNotThrow(() => _net.PushRaw(MessageIds.NotifySceneInfo, new byte[] { 0x0A, 0x05, 0x08 }));
            Assert.That(_client.ListPending, Is.False);
            Assert.That(_client.HasDirectory, Is.True, "一个坏包不清掉已有线路");
            Assert.That(_client.Lines.Count, Is.EqualTo(4));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.ListUnavailableMessage));
            Assert.That(_client.StatusIsError, Is.True);

            Assert.DoesNotThrow(() => _net.Notifies[MessageIds.NotifySceneInfo](null));
            Assert.DoesNotThrow(() => _net.Notifies[MessageIds.SceneInfoC2S](null));
            Assert.That(_client.Lines.Count, Is.EqualTo(4));
        }

        // ── 列线:43 的回包 ──────────────────────────────────────────────────

        [Test]
        public void ListReply_RateLimited_EndsWaitingWithHint_AndBacksOffAFullInterval()
        {
            _client.RequestList();
            _now = 1f;
            _net.PushEnvelopeError(MessageIds.SceneInfoC2S, (uint)common_error.KRateLimitExceeded);

            Assert.That(_client.ListPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.TooFastMessage));
            Assert.That(_client.StatusIsError, Is.True);
            Assert.That(_changes, Is.EqualTo(2));

            // 本地原先算的下次可发时刻是 2.5;被 gate 拒绝后从拒绝那一刻(1 秒)起再让出一个完整间隔。
            _now = 2.5f;
            Assert.That(_client.RequestList(), Is.True);
            Assert.That(_net.OneWays.Count, Is.EqualTo(1));
            _now = 3.5f;
            _client.Tick();
            Assert.That(_net.OneWays.Count, Is.EqualTo(2));
        }

        [Test]
        public void ListReply_WithoutError_IsIgnored_OtherErrorsEndWaiting_StaleRejectionsChangeNothing()
        {
            _client.RequestList();
            _net.Notifies[MessageIds.SceneInfoC2S](new MessageContent { MessageId = MessageIds.SceneInfoC2S, Id = 7 });
            Assert.That(_client.ListPending, Is.True, "空回包不算数据,数据走 31");
            Assert.That(_changes, Is.EqualTo(1));

            _net.PushEnvelopeError(MessageIds.SceneInfoC2S, (uint)common_error.KServiceUnavailable);
            Assert.That(_client.ListPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.ListUnavailableMessage));
            Assert.That(_changes, Is.EqualTo(2));

            // 已经不在等待:迟到的拒绝不再改状态。
            _net.PushEnvelopeError(MessageIds.SceneInfoC2S, (uint)common_error.KRateLimitExceeded);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.ListUnavailableMessage));
            Assert.That(_changes, Is.EqualTo(2));
        }

        [Test]
        public void ListReply_RateLimitedWhileNotWaiting_ChangesNoStatus_ButStillBacksOff()
        {
            LoadDirectory();                // 0 秒发出并已答复:不在等待,下次可发时刻 2.5 秒
            int before = _changes;

            _now = 2f;
            _net.PushEnvelopeError(MessageIds.SceneInfoC2S, (uint)common_error.KRateLimitExceeded);
            Assert.That(_client.Status, Is.Empty);
            Assert.That(_changes, Is.EqualTo(before));

            // 原先的间隔(2.5 秒)已过,但 gate 在 2 秒时明说了太快:要从那一刻起再让出一个完整间隔(到 4.5 秒)。
            _now = 3f;
            Assert.That(_client.RequestList(), Is.True);
            Assert.That(_net.OneWays.Count, Is.EqualTo(1), "退避没到点:只排队");
            Assert.That(_client.ListQueued, Is.True);
            _now = 4.4f;
            _client.Tick();
            Assert.That(_net.OneWays.Count, Is.EqualTo(1));
            _now = 4.5f;
            _client.Tick();
            Assert.That(_net.OneWays.Count, Is.EqualTo(2));
        }

        [Test]
        public void ListFailure_WhileSwitching_DoesNotOverwriteTheProgressText()
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestList();
            Assert.That(_client.RequestSwitch(Line2), Is.True);

            _now = 8f;   // 43 的 5 秒等待到点;63 的本地兜底(30 秒)还早
            _client.Tick();
            Assert.That(_client.ListPending, Is.False);
            Assert.That(_client.SwitchPending, Is.True);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchingText(2)));
            Assert.That(_client.StatusIsError, Is.False);
        }

        // ── 切线:前置校验与请求体 ───────────────────────────────────────────

        [Test]
        public void RequestSwitch_SendsEnterSceneForTheCurrentMapAndTargetLine()
        {
            LoadDirectory();
            int before = _changes;

            Assert.That(_client.RequestSwitch(Line2), Is.True);

            Assert.That(_net.Calls.Count, Is.EqualTo(1));
            Assert.That(_net.Calls[0].Id, Is.EqualTo(MessageIds.EnterScene));
            var request = (EnterSceneC2SRequest)_net.Calls[0].Request;
            Assert.That(request.SceneInfo.SceneConfigId, Is.EqualTo(Map));
            Assert.That(request.SceneInfo.SceneId, Is.EqualTo(Line2));
            Assert.That(request.SceneInfo.MirrorConfigId, Is.Zero);
            Assert.That(request.SceneInfo.DungeonConfigId, Is.Zero);
            Assert.That(_client.SwitchPending, Is.True);
            Assert.That(_client.SwitchTargetSceneId, Is.EqualTo(Line2));
            Assert.That(_client.SwitchTargetChannelNo, Is.EqualTo(2));
            Assert.That(_client.Status, Is.EqualTo("正在切换到 2线…"));
            Assert.That(_client.StatusIsError, Is.False);
            Assert.That(_changes, Is.EqualTo(before + 1));
            Assert.That(_switched, Is.Empty, "发出不等于切过去了");
            Assert.That(_net.OneWays.Count, Is.EqualTo(1), "切线本身不触发列线");
        }

        [Test]
        public void RequestSwitch_UnselectableLines_SendNothing()
        {
            LoadDirectory();

            AssertBlocked(Line1, SceneChannelBlockReason.CurrentLine);
            AssertBlocked(Line3, SceneChannelBlockReason.Full);
            AssertBlocked(Line4, SceneChannelBlockReason.Closing);
            AssertBlocked(999, SceneChannelBlockReason.LineMissing);
            AssertBlocked(0, SceneChannelBlockReason.LineMissing);

            Assert.That(_net.Calls, Is.Empty);
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.BlockReasonFor(_client.Lines[1]), Is.EqualTo(SceneChannelBlockReason.None));
            Assert.That(_client.BlockReasonFor(_client.Lines[2]), Is.EqualTo(SceneChannelBlockReason.Full));
        }

        [Test]
        public void LocalRejectionHint_IsClearedByTheNextDirectoryAnswer()
        {
            LoadDirectory();
            AssertBlocked(Line3, SceneChannelBlockReason.Full);

            _now = 3f;
            _client.RequestList();
            PushDirectory(BuildDirectory());
            Assert.That(_client.Status, Is.Empty, "面板开着时,一条本地拒绝提示不会一直挂着");
            Assert.That(_client.StatusIsError, Is.False);
        }

        [Test]
        public void RequestSwitch_HostStateBlocks_BattleTravelAndTeamFollower()
        {
            LoadDirectory();

            _client.InBattle = true;
            Assert.That(_client.GlobalBlockReason, Is.EqualTo(SceneChannelBlockReason.InBattle));
            AssertBlocked(Line2, SceneChannelBlockReason.InBattle);
            _client.InBattle = false;

            _client.Travelling = true;
            AssertBlocked(Line2, SceneChannelBlockReason.Travelling);
            _client.Travelling = false;

            _client.TeamFollower = true;
            AssertBlocked(Line2, SceneChannelBlockReason.TeamMember);
            Assert.That(_client.Status, Is.EqualTo("队伍中由队长切线，或先离队。"));
            _client.TeamFollower = false;

            Assert.That(_net.Calls, Is.Empty);
            Assert.That(_client.GlobalBlockReason, Is.EqualTo(SceneChannelBlockReason.None));
            Assert.That(_client.RequestSwitch(Line2), Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
        }

        [Test]
        public void HostFlags_RaiseChangedOnlyWhenTheyActuallyChange()
        {
            _client.InBattle = true;
            Assert.That(_changes, Is.EqualTo(1));
            _client.InBattle = true;
            Assert.That(_changes, Is.EqualTo(1));
            _client.InBattle = false;
            _client.Travelling = true;
            _client.TeamFollower = true;
            Assert.That(_changes, Is.EqualTo(4));
            Assert.That(_client.Travelling && _client.TeamFollower && !_client.InBattle, Is.True);
        }

        [Test]
        public void RequestSwitch_NotReady_WithoutDirectoryOrConnection()
        {
            AssertBlocked(Line2, SceneChannelBlockReason.NotReady);   // 还没有目录

            LoadDirectory();
            _net.IsReady = false;
            AssertBlocked(Line2, SceneChannelBlockReason.NotReady);   // 有目录但连接没就绪
            Assert.That(_net.Calls, Is.Empty);
        }

        [Test]
        public void RequestSwitch_ServerDisabledSwitching_BlocksEveryLine()
        {
            LoadDirectory(BuildDirectory(enabled: false));
            Assert.That(_client.SwitchEnabled, Is.False);
            Assert.That(_client.GlobalBlockReason, Is.EqualTo(SceneChannelBlockReason.SwitchDisabled));
            AssertBlocked(Line2, SceneChannelBlockReason.SwitchDisabled);
            Assert.That(_net.Calls, Is.Empty);
        }

        [Test]
        public void RequestSwitch_WhileSwitching_IsIgnored_AndKeepsTheProgressText()
        {
            LoadDirectory();
            _client.RequestSwitch(Line2);
            int before = _changes;

            Assert.That(_client.RequestSwitch(Line2), Is.False);
            Assert.That(_client.RequestSwitch(Line1), Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchingText(2)));
            Assert.That(_client.StatusIsError, Is.False);
            Assert.That(_changes, Is.EqualTo(before));
            Assert.That(_client.BlockReasonFor(_client.Lines[1]), Is.EqualTo(SceneChannelBlockReason.Switching));
        }

        // ── 切线:同步应答 ───────────────────────────────────────────────────

        [TestCase((uint)scene_error.KEnterSceneYouInCurrentScene, SceneChannelClient.AlreadyOnLineMessage)]
        [TestCase((uint)scene_error.KEnterSceneChangingScene, SceneChannelClient.ChangingSceneMessage)]
        [TestCase((uint)scene_error.KEnterSceneFailed, SceneChannelClient.SwitchRejectedMessage)]
        [TestCase((uint)common_error.KRateLimitExceeded, SceneChannelClient.TooFastMessage)]
        [TestCase((uint)common_error.KServiceUnavailable, SceneChannelClient.ServerBusyMessage)]
        [TestCase((uint)scene_error.KEnterSceneServerBusy, SceneChannelClient.ServerBusyMessage)]
        public void SyncReply_WithTip_FailsImmediatelyWithMatchingText(uint tipId, string expected)
        {
            LoadDirectory();
            _client.RequestSwitch(Line2);
            int before = _changes;

            _net.Calls[0].Reply(Rejected(tipId));

            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.SwitchTargetSceneId, Is.Zero);
            Assert.That(_client.SwitchTargetChannelNo, Is.Zero);
            Assert.That(_client.Status, Is.EqualTo(expected));
            Assert.That(_client.StatusIsError, Is.True);
            Assert.That(_changes, Is.EqualTo(before + 1));
            Assert.That(_switched, Is.Empty);
            Assert.That(_client.CooldownRemainingSeconds, Is.Zero, "没切成不起冷却");
            Assert.That(_client.Lines.Count, Is.EqualTo(4), "失败不清线路");
        }

        [Test]
        public void SyncReply_UnknownTip_FallsBackToTheBareCode()
        {
            uint tipId = (uint)scene_error.KEnterSceneParamError;
            LoadDirectory();
            _client.RequestSwitch(Line2);
            _net.Calls[0].Reply(Rejected(tipId));

            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo("切线失败（tip=" + tipId + "）。"));
            Assert.That(_client.StatusIsError, Is.True);
        }

        [Test]
        public void SyncReply_AlreadyOnThatLine_RefreshesTheStaleDirectory_OtherRejectionsDoNot()
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestSwitch(Line2);
            _net.Calls[0].Reply(Rejected((uint)scene_error.KEnterSceneChangingScene));
            Assert.That(_net.OneWays.Count, Is.EqualTo(1));
            Assert.That(_client.ListQueued, Is.False);

            _now = 4f;   // 两次 63 之间至少隔 1 秒
            Assert.That(_client.RequestSwitch(Line2), Is.True);
            _net.Calls[1].Reply(Rejected((uint)scene_error.KEnterSceneYouInCurrentScene));
            Assert.That(_net.OneWays.Count, Is.EqualTo(2), "本地把当前线标错了,刷新目录");
            Assert.That(_client.ListPending, Is.True);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.AlreadyOnLineMessage));
        }

        [Test]
        public void TransportError_FailsTheSwitch_EnvelopeTipsAreTranslatedByCode()
        {
            LoadDirectory();

            AssertTransportFailure("server tip=" + (uint)common_error.KRateLimitExceeded, SceneChannelClient.TooFastMessage);
            AssertTransportFailure("server tip=" + (uint)common_error.KServiceUnavailable, SceneChannelClient.ServerBusyMessage);
            AssertTransportFailure("server tip=" + (uint)scene_error.KEnterSceneFailed, SceneChannelClient.SwitchRejectedMessage);
            AssertTransportFailure("server tip=abc", SceneChannelClient.SwitchFailedMessage);
            AssertTransportFailure("rpc timeout", SceneChannelClient.SwitchFailedMessage);
            AssertTransportFailure("disconnected", SceneChannelClient.SwitchFailedMessage);
            AssertTransportFailure("parse response: boom", SceneChannelClient.SwitchFailedMessage);
            AssertTransportFailure(null, SceneChannelClient.SwitchFailedMessage);

            Assert.That(_switched, Is.Empty);
            Assert.That(_net.OneWays.Count, Is.EqualTo(1), "传输层失败不追加列线");
        }

        [Test]
        public void SyncTransportError_SettlesInsideRequestSwitch()
        {
            LoadDirectory();
            _net.SyncError = "not connected";

            Assert.That(_client.RequestSwitch(Line2), Is.True, "已交给传输层,结论看状态");
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchFailedMessage));
            Assert.That(_client.StatusIsError, Is.True);
        }

        // ── 切线:连点保护 ───────────────────────────────────────────────────

        [Test]
        public void RequestSwitch_WithinOneSecondOfTheLastSend_IsRejectedLocally_WithoutSendingAnything()
        {
            Assert.That(SceneChannelClient.MinSwitchIntervalSeconds, Is.EqualTo(1f));
            LoadDirectory();
            _now = 3f;
            Assert.That(_client.RequestSwitch(Line2), Is.True);
            // 同步拒绝一个往返就回来并解除在途:这正是连点会撞 gate 限流的情形。
            _net.Calls[0].Reply(Rejected((uint)scene_error.KEnterSceneChangingScene));
            Assert.That(_client.SwitchPending, Is.False);
            int before = _changes;

            Assert.That(_client.RequestSwitch(Line2), Is.False, "立刻再点:本地拒绝");
            _now = 3.9f;
            Assert.That(_client.RequestSwitch(Line1), Is.False, "原因排在连点保护之前:所在线仍说「已在该线路」");
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.AlreadyOnLineMessage));
            Assert.That(_client.RequestSwitch(Line2), Is.False);

            Assert.That(_net.Calls.Count, Is.EqualTo(1), "间隔内一个 63 都没发");
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.TooFastMessage));
            Assert.That(_client.StatusIsError, Is.True);
            Assert.That(_changes, Is.EqualTo(before + 3));
            Assert.That(_net.OneWays.Count, Is.EqualTo(1), "本地拒绝不触发列线");

            // 满一秒:放行。
            _now = 4f;
            Assert.That(_client.RequestSwitch(Line2), Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchingText(2)));
        }

        [Test]
        public void TooFastHint_IsTransient_ClearedByTheNextDirectoryAnswer()
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestSwitch(Line2);
            _net.Calls[0].Reply(Rejected((uint)scene_error.KEnterSceneChangingScene));
            Assert.That(_client.RequestSwitch(Line2), Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.TooFastMessage));

            _client.RequestList();
            PushDirectory(BuildDirectory());
            Assert.That(_client.Status, Is.Empty);
            Assert.That(_client.StatusIsError, Is.False);
        }

        [Test]
        public void RateLimitedSwitch_BacksOffAFullLimiterWindow_NotJustTheClickGuard()
        {
            Assert.That(SceneChannelClient.RateLimitBackoffSeconds, Is.EqualTo(2.5f));
            LoadDirectory();
            _now = 3f;
            _client.RequestSwitch(Line2);
            _now = 3.2f;
            // gate 限流写在信封上,传输层折成 "server tip=N"。
            _net.Calls[0].Fail("server tip=" + (uint)common_error.KRateLimitExceeded);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.TooFastMessage));

            // 连点保护的 1 秒(到 4 秒)已过,但 gate 的窗口跨两个整秒:从被拒那一刻(3.2 秒)起让出 2.5 秒。
            _now = 5.6f;
            Assert.That(_client.RequestSwitch(Line2), Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.TooFastMessage));

            _now = 5.8f;
            Assert.That(_client.RequestSwitch(Line2), Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
        }

        [Test]
        public void AcceptedReply_FollowedBySpuriousError_StaysPending()
        {
            // GameClient.Call 在回包处理抛异常后会补调一次 onError;已受理的切线不能因此被判失败。
            LoadDirectory();
            _client.RequestSwitch(Line2);
            _net.Calls[0].Reply(Accepted());
            _net.Calls[0].Fail("parse response: boom");

            Assert.That(_client.SwitchPending, Is.True);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchingText(2)));

            Arrive(Line2);
            Assert.That(_switched, Is.EqualTo(new uint[] { 2 }));
        }

        // ── 切线:受理之后 ───────────────────────────────────────────────────

        [Test]
        public void Accepted_ThenArrivalAtTarget_Succeeds_StartsCooldown_AndSchedulesARefresh()
        {
            LoadDirectory();
            _now = 1f;
            _client.RequestSwitch(Line2);
            _net.Calls[0].Reply(Accepted());
            Assert.That(_client.SwitchPending, Is.True, "受理不是抵达");
            Assert.That(_switched, Is.Empty);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchingText(2)));
            int before = _changes;

            _now = 2f;
            Arrive(Line2);

            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.SwitchTargetSceneId, Is.Zero);
            Assert.That(_client.SwitchTargetChannelNo, Is.Zero);
            Assert.That(_switched, Is.EqualTo(new uint[] { 2 }));
            Assert.That(_client.Status, Is.EqualTo("已切换到 2线。"));
            Assert.That(_client.StatusIsError, Is.False);
            Assert.That(_client.CooldownRemainingSeconds, Is.EqualTo(10f), "按目录里的 10 秒起冷却");
            Assert.That(_client.Lines, Is.Empty, "进场即清空线路");
            Assert.That(_client.HasDirectory, Is.False);
            Assert.That(_client.CurrentChannelNo, Is.Zero);
            Assert.That(_changes, Is.EqualTo(before + 1));

            // 距上次 43 才 2 秒:刷新先排队,到点由 Tick 发出。
            Assert.That(_client.ListQueued, Is.True);
            Assert.That(_net.OneWays.Count, Is.EqualTo(1));
            _now = 2.5f;
            _client.Tick();
            Assert.That(_net.OneWays.Count, Is.EqualTo(2));

            PushDirectory(BuildDirectory());
            Assert.That(_client.CurrentChannelNo, Is.EqualTo(2));
            Assert.That(_client.Status, Is.EqualTo("已切换到 2线。"), "切线的结论不被目录刷新收掉");
        }

        [Test]
        public void ArrivalBeforeTheSyncReply_SucceedsExactlyOnce()
        {
            LoadDirectory();
            _now = 1f;
            _client.RequestSwitch(Line2);

            Arrive(Line2);   // 入场通知抢在同步应答之前
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_switched, Is.EqualTo(new uint[] { 2 }));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchedText(2)));
            Assert.That(_client.CooldownRemainingSeconds, Is.EqualTo(10f));
            int before = _changes;

            // 之后才到的应答 / 报错都不能把状态改坏。
            _net.Calls[0].Reply(Accepted());
            _net.Calls[0].Reply(Rejected((uint)scene_error.KEnterSceneFailed));
            _net.Calls[0].Fail("rpc timeout");

            Assert.That(_changes, Is.EqualTo(before));
            Assert.That(_switched.Count, Is.EqualTo(1));
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchedText(2)));
            Assert.That(_client.StatusIsError, Is.False);

            // 迟到的「受理」也没有把等待重新续上:很久以后不会冒出一个超时。
            _now = 200f;
            _client.Tick();
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchedText(2)));
            Assert.That(_client.StatusIsError, Is.False);
        }

        [TestCase(SceneChannelState.Full)]
        [TestCase(SceneChannelState.Closing)]
        [TestCase(SceneChannelState.Busy)]
        [TestCase(SceneChannelState.Unavailable)]
        public void Accepted_ThenFailureTip_FailsAndRefreshes_ThenNamesTheReasonIfTheDirectoryShowsIt(
            SceneChannelState targetStateAfterRefresh)
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestSwitch(Line2);
            _net.Calls[0].Reply(Accepted());
            int before = _changes;

            _client.HandleServerTip(Tip((uint)scene_error.KEnterSceneFailed));

            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchFailedMessage));
            Assert.That(_client.StatusIsError, Is.True);
            Assert.That(_changes, Is.EqualTo(before + 1));
            Assert.That(_switched, Is.Empty);
            Assert.That(_client.CooldownRemainingSeconds, Is.Zero);
            Assert.That(_client.Lines.Count, Is.EqualTo(4), "失败不清线路,等刷新覆盖");
            Assert.That(_net.OneWays.Count, Is.EqualTo(2), "安排了一次刷新");
            Assert.That(_client.ListPending, Is.True);

            PushDirectory(BuildDirectory(line2: targetStateAfterRefresh));

            string expected = targetStateAfterRefresh == SceneChannelState.Full ? "2线已满，请选择其他线路。"
                : targetStateAfterRefresh == SceneChannelState.Closing ? "2线正在回收，请选择其他线路。"
                : SceneChannelClient.SwitchFailedMessage;
            Assert.That(_client.Status, Is.EqualTo(expected));
            Assert.That(_client.StatusIsError, Is.True);
        }

        // ── 切线:失败文案的寿命 ─────────────────────────────────────────────

        [Test]
        public void FailureNamedAfterALine_FollowsThatLine_AndIsClearedOnceTheLineRecovers()
        {
            FailSwitchToLine2ByTip();
            PushDirectory(BuildDirectory(line2: SceneChannelState.Full));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.LineFullText(2)));

            // 那条线还满着:文案留着。
            PushDirectory(BuildDirectory(line2: SceneChannelState.Full));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.LineFullText(2)));
            Assert.That(_client.StatusIsError, Is.True);
            // 状态变了但仍进不去:文案跟着变。
            PushDirectory(BuildDirectory(line2: SceneChannelState.Closing));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.LineClosingText(2)));
            Assert.That(_client.StatusIsError, Is.True);

            // 那条线恢复可选:红字不能再和列表里的「繁忙」并排挂着。
            int before = _changes;
            PushDirectory(BuildDirectory(line2: SceneChannelState.Busy));
            Assert.That(_client.Status, Is.Empty);
            Assert.That(_client.StatusIsError, Is.False);
            Assert.That(_changes, Is.EqualTo(before + 1));

            // 收掉之后不会因为它又满了而冒回来。
            PushDirectory(BuildDirectory(line2: SceneChannelState.Full));
            Assert.That(_client.Status, Is.Empty);
        }

        [Test]
        public void FailureNamedAfterALine_IsClearedWhenThatLineLeavesTheDirectory()
        {
            FailSwitchToLine2ByTip();
            PushDirectory(BuildDirectory(line2: SceneChannelState.Closing));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.LineClosingText(2)));

            var withoutLine2 = BuildDirectory();
            withoutLine2.Channels.RemoveAt(1);   // 回收完成,2线已销毁
            PushDirectory(withoutLine2);
            Assert.That(_client.Lines.Count, Is.EqualTo(3));
            Assert.That(_client.Status, Is.Empty);
        }

        [Test]
        public void GenericFailure_SurvivesTheFirstDirectory_AndIsClearedByTheSecond_WithoutLateRenaming()
        {
            FailSwitchToLine2ByTip();
            // 失败时顺手拉的那份目录:2线并没有满,说不出具体原因,笼统文案先留着(立刻收掉等于没提示过)。
            PushDirectory(BuildDirectory());
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchFailedMessage));
            Assert.That(_client.StatusIsError, Is.True);

            // 第二份目录:收掉。即使这时 2线满了也不再追认 —— 那已经说明不了当时为什么没成。
            PushDirectory(BuildDirectory(line2: SceneChannelState.Full));
            Assert.That(_client.Status, Is.Empty);
            Assert.That(_client.StatusIsError, Is.False);
        }

        [Test]
        public void SyncRejectionText_HasTheSameLifetime_AndAnAnswerWithoutDirectoryCountsToo()
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestSwitch(Line2);
            _net.Calls[0].Reply(Rejected((uint)scene_error.KEnterSceneFailed));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchRejectedMessage));

            // 第一次答复不带目录(目录暂时没发布):文案先留着。
            _net.Push(MessageIds.NotifySceneInfo, new SceneInfoS2C());
            Assert.That(_client.HasDirectory, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchRejectedMessage));
            Assert.That(_client.StatusIsError, Is.True);

            PushDirectory(BuildDirectory());
            Assert.That(_client.Status, Is.Empty);
        }

        [Test]
        public void SyncRejectionText_IsNeverRenamedAfterALine()
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestSwitch(Line2);
            // 同步拒绝自带原因(上一次换场景还没结束),与那条线满不满无关。
            _net.Calls[0].Reply(Rejected((uint)scene_error.KEnterSceneChangingScene));

            PushDirectory(BuildDirectory(line2: SceneChannelState.Full));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.ChangingSceneMessage), "不追认成「2线已满」");
            Assert.That(_client.StatusIsError, Is.True);
            PushDirectory(BuildDirectory(line2: SceneChannelState.Full));
            Assert.That(_client.Status, Is.Empty);
        }

        [Test]
        public void WrongDestinationText_HasTheSameLifetime()
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestSwitch(Line2);
            _net.Calls[0].Reply(Accepted());
            Arrive(Line3);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.WrongDestinationMessage));

            PushDirectory(BuildDirectory());   // 进场后拉的那一份
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.WrongDestinationMessage));
            Assert.That(_client.StatusIsError, Is.True);
            PushDirectory(BuildDirectory());
            Assert.That(_client.Status, Is.Empty);
        }

        [Test]
        public void SuccessText_IsNotClearedByDirectoryRefreshes()
        {
            SwitchToLine2AndReload();       // 抵达后已收到一份目录
            PushDirectory(BuildDirectory());
            PushDirectory(BuildDirectory());
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchedText(2)), "寿命规则只管没成的结论");
            Assert.That(_client.StatusIsError, Is.False);
        }

        // ── 切线:「结果未知」地收了口,之后人却到了 ───────────────────────────

        [Test]
        public void ServiceUnavailableTip_ThenArrivalAtTheTarget_IsSettledAsASuccessfulSwitch()
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestSwitch(Line2);
            _net.Calls[0].Reply(Accepted());
            // 1003 不带请求号:scene 调 scene_manager 传输失败会推它,gate 为别的模块(好友 / 聊天 …)路由失败也推它。
            _client.HandleServerTip(Tip((uint)common_error.KServiceUnavailable));
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchFailedMessage));
            Assert.That(_client.CooldownRemainingSeconds, Is.Zero);
            Assert.That(_switched, Is.Empty);

            // 这次切线其实成了(服务端的冷却也已经在计):目标线的入场通知随后到达。
            _now = 5f;
            Arrive(Line2);
            Assert.That(_switched, Is.EqualTo(new uint[] { 2 }));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchedText(2)));
            Assert.That(_client.StatusIsError, Is.False);
            Assert.That(_client.CooldownRemainingSeconds, Is.EqualTo(10f), "本地冷却照起,否则紧接着再点只会吃到服务端的冷却拒绝");

            // 只补结算一次:之后再回到这条线是普通进场。
            _now = 6f;
            Arrive(Line1);
            Arrive(Line2);
            Assert.That(_switched.Count, Is.EqualTo(1));
            Assert.That(_client.Status, Is.Empty);
            Assert.That(_client.CooldownRemainingSeconds, Is.EqualTo(9f), "已经起算的冷却不受影响");
        }

        [Test]
        public void ReplyTimeout_ThenArrivalAtTheTarget_IsSettledAsASuccessfulSwitch()
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestSwitch(Line2);
            _now = 18f;
            _net.Calls[0].Fail("rpc timeout");   // 应答丢了,服务端其实可能已经受理
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchFailedMessage));

            _now = 19f;
            Arrive(Line2);
            Assert.That(_switched, Is.EqualTo(new uint[] { 2 }));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchedText(2)));
            Assert.That(_client.CooldownRemainingSeconds, Is.EqualTo(10f));
        }

        [TestCase((uint)scene_error.KEnterSceneFailed)]
        [TestCase((uint)scene_error.KEnterSceneChangingScene)]
        public void DefiniteFailureTip_ThenArrivalAtThatLine_IsAPlainEntry(uint tipId)
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestSwitch(Line2);
            _net.Calls[0].Reply(Accepted());
            _client.HandleServerTip(Tip(tipId));   // 服务端明说没成:之后到那条线是别的原因(队伍跟随等)

            _now = 5f;
            Arrive(Line2);
            Assert.That(_switched, Is.Empty);
            Assert.That(_client.CooldownRemainingSeconds, Is.Zero);
            Assert.That(_client.Status, Is.Empty);
        }

        [Test]
        public void SyncRejection_ThenArrivalAtThatLine_IsAPlainEntry()
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestSwitch(Line2);
            _net.Calls[0].Reply(Rejected((uint)common_error.KServiceUnavailable));   // 响应体里的 1003 是明确拒绝

            _now = 5f;
            Arrive(Line2);
            Assert.That(_switched, Is.Empty);
            Assert.That(_client.CooldownRemainingSeconds, Is.Zero);
            Assert.That(_client.Status, Is.Empty);
        }

        [Test]
        public void OutcomeUnknownRecord_Expires_AndIsDroppedByTheNextSwitchOrAnyEntry()
        {
            Assert.That(SceneChannelClient.LateArrivalWindowSeconds, Is.EqualTo(75f));
            LoadDirectory();
            _now = 3f;
            _client.RequestSwitch(Line2);
            _net.Calls[0].Reply(Accepted());
            _client.HandleServerTip(Tip((uint)common_error.KServiceUnavailable));

            // 过了有效期(3 + 75 秒)才到:不会是那次切线了。
            _now = 78.5f;
            Arrive(Line2);
            Assert.That(_switched, Is.Empty);
            Assert.That(_client.CooldownRemainingSeconds, Is.Zero);
            Assert.That(_client.Status, Is.Empty);

            // 又一次「结果未知」(目标 1线),但先进了别的场景:记录作废,之后到 1线只是普通进场。
            PushDirectory(BuildDirectory());
            _now = 80f;
            Assert.That(_client.RequestSwitch(Line1), Is.True);
            _net.Calls[1].Reply(Accepted());
            _client.HandleServerTip(Tip((uint)common_error.KServiceUnavailable));
            _now = 81f;
            Arrive(Line3);
            Arrive(Line1);
            Assert.That(_switched, Is.Empty);

            // 再一次「结果未知」(目标 2线),随后新发的一次切线被明确拒绝:旧记录已被新请求顶掉。
            PushDirectory(BuildDirectory());
            _now = 90f;
            Assert.That(_client.RequestSwitch(Line2), Is.True);
            _net.Calls[2].Reply(Accepted());
            _client.HandleServerTip(Tip((uint)common_error.KServiceUnavailable));
            _now = 92f;
            Assert.That(_client.RequestSwitch(Line2), Is.True);
            _net.Calls[3].Reply(Rejected((uint)scene_error.KEnterSceneFailed));
            _now = 93f;
            Arrive(Line2);
            Assert.That(_switched, Is.Empty);
            Assert.That(_client.CooldownRemainingSeconds, Is.Zero);
        }

        [TestCase((uint)scene_error.KEnterSceneFailed)]
        [TestCase((uint)scene_error.KEnterSceneChangingScene)]
        [TestCase((uint)common_error.KServiceUnavailable)]
        [TestCase((uint)scene_error.KEnterSceneServerBusy)]
        public void FailureTip_BeforeTheSyncReply_AlsoEndsTheSwitch_AndTheLateReplyIsIgnored(uint tipId)
        {
            Assert.That(SceneChannelClient.IsSwitchFailureTip(tipId), Is.True);
            LoadDirectory();
            _client.RequestSwitch(Line2);

            _client.HandleServerTip(Tip(tipId));
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchFailedMessage));
            int before = _changes;

            _net.Calls[0].Reply(Accepted());
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_changes, Is.EqualTo(before));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchFailedMessage));
        }

        [Test]
        public void UnrelatedOrIdleTips_ChangeNothing()
        {
            Assert.That(SceneChannelClient.IsSwitchFailureTip(0), Is.False);
            Assert.That(SceneChannelClient.IsSwitchFailureTip((uint)scene_error.KZoneTravelTargetBusy), Is.False);
            Assert.That(SceneChannelClient.IsSwitchFailureTip((uint)common_error.KRateLimitExceeded), Is.False);

            LoadDirectory();
            int before = _changes;
            _client.HandleServerTip(Tip((uint)scene_error.KEnterSceneFailed));   // 不在途:与切线无关
            Assert.That(_changes, Is.EqualTo(before));
            Assert.That(_client.Status, Is.Empty);

            _client.RequestSwitch(Line2);
            _net.Calls[0].Reply(Accepted());
            before = _changes;
            _client.HandleServerTip(Tip((uint)scene_error.KZoneTravelTargetBusy));
            _client.HandleServerTip(Tip((uint)common_error.KRateLimitExceeded));
            _client.HandleServerTip(null);
            Assert.That(_client.SwitchPending, Is.True, "认不出的提示不下结论");
            Assert.That(_changes, Is.EqualTo(before));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchingText(2)));
        }

        [Test]
        public void Accepted_ButNoArrivalWithin75Seconds_TimesOut()
        {
            Assert.That(SceneChannelClient.SwitchAcceptedBudgetSeconds, Is.EqualTo(75f));
            LoadDirectory();
            _now = 1f;
            _client.RequestSwitch(Line2);
            _net.Calls[0].Reply(Accepted());   // 受理于 1 秒 → 等到 76 秒
            int before = _changes;

            _now = 31f;   // 受理前的 30 秒兜底不再适用
            _client.Tick();
            _now = 75.9f;
            _client.Tick();
            Assert.That(_client.SwitchPending, Is.True);
            Assert.That(_changes, Is.EqualTo(before));

            _now = 76f;
            _client.Tick();
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchTimeoutMessage));
            Assert.That(_client.StatusIsError, Is.True);
            Assert.That(_changes, Is.EqualTo(before + 1));
            Assert.That(_switched, Is.Empty);
            Assert.That(_net.OneWays.Count, Is.EqualTo(2), "等了这么久,顺手刷新目录");

            // 超时是「结果未知」:之后目标线的入场通知到了,说明那次切线其实成了,按成功补结算。
            _now = 80f;
            Arrive(Line2);
            Assert.That(_switched, Is.EqualTo(new uint[] { 2 }));
            Assert.That(_client.CooldownRemainingSeconds, Is.EqualTo(10f));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchedText(2)));
            Assert.That(_client.StatusIsError, Is.False);
        }

        [Test]
        public void NoSyncReplyAtAll_LocalBudgetEndsTheSwitch_AndTheLateReplyIsIgnored()
        {
            Assert.That(SceneChannelClient.SwitchRequestBudgetSeconds, Is.EqualTo(30f));
            LoadDirectory();
            _now = 1f;
            _client.RequestSwitch(Line2);

            _now = 30.9f;
            _client.Tick();
            Assert.That(_client.SwitchPending, Is.True);

            _now = 31f;
            _client.Tick();
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchTimeoutMessage));

            _net.Calls[0].Reply(Accepted());
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchTimeoutMessage));
        }

        [Test]
        public void Accepted_ThenArrivalAtAnotherLine_EndsWithWrongDestination()
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestSwitch(Line2);
            _net.Calls[0].Reply(Accepted());

            Arrive(Line3);   // 被服务器送去了别处

            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.WrongDestinationMessage));
            Assert.That(_client.StatusIsError, Is.True);
            Assert.That(_switched, Is.Empty);
            Assert.That(_client.CooldownRemainingSeconds, Is.Zero);
            Assert.That(_client.Lines, Is.Empty);
            Assert.That(_client.HasDirectory, Is.False);
            Assert.That(_net.OneWays.Count, Is.EqualTo(2), "进场照常安排列线");

            // 入场通知不带场景信息时同样判未到达。
            PushDirectory(BuildDirectory());
            _sceneId = Line3;
            _now = 10f;
            Assert.That(_client.RequestSwitch(Line2), Is.True);
            _net.Calls[1].Reply(Accepted());
            _client.HandleSceneEntered(null);
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.WrongDestinationMessage));
            Assert.That(_switched, Is.Empty);
            // 不带场景信息 = 不知道是不是副本:照常列线,不当成「没有分线」。
            Assert.That(_client.DirectoryApplicable, Is.True);
            Assert.That(_net.OneWays.Count, Is.EqualTo(3));
            Assert.That(_client.ListPending, Is.True);
        }

        [Test]
        public void NonTargetEntryBeforeTheSyncReply_DoesNotSettleTheSwitch_TheRealOutcomeStillCounts()
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestSwitch(Line2);
            int before = _changes;

            // 同步应答还没到:这条通知是服务端处理这次 63 之前发出的(队伍跟随、更早的换图 …),不是它的结果。
            Arrive(Line3);
            Assert.That(_client.SwitchPending, Is.True);
            Assert.That(_client.SwitchTargetSceneId, Is.EqualTo(Line2));
            Assert.That(_client.SwitchTargetChannelNo, Is.EqualTo(2));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchingText(2)));
            Assert.That(_client.StatusIsError, Is.False);
            Assert.That(_client.Lines, Is.Empty, "进场照常清目录");
            Assert.That(_net.OneWays.Count, Is.EqualTo(2), "并重新列线");
            Assert.That(_switched, Is.Empty);
            Assert.That(_changes, Is.EqualTo(before + 1));

            // 同步应答随后到达、照常作数;之后抵达目标线 = 成功,冷却照起。
            _net.Calls[0].Reply(Accepted());
            Assert.That(_client.SwitchPending, Is.True);
            _now = 4f;
            Arrive(Line2);
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_switched, Is.EqualTo(new uint[] { 2 }));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchedText(2)));
            Assert.That(_client.CooldownRemainingSeconds, Is.EqualTo(10f));
        }

        [Test]
        public void NonTargetEntryBeforeTheSyncReply_ThenRejection_FailsWithTheServerReason()
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestSwitch(Line2);
            Arrive(Line3);

            _net.Calls[0].Reply(Rejected((uint)scene_error.KEnterSceneFailed));
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchRejectedMessage));
            Assert.That(_client.StatusIsError, Is.True);
            Assert.That(_switched, Is.Empty);
        }

        [Test]
        public void AcceptedMark_BelongsToOneSwitch_AndDoesNotLeakIntoTheNext()
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestSwitch(Line2);
            _net.Calls[0].Reply(Accepted());
            _client.HandleServerTip(Tip((uint)scene_error.KEnterSceneFailed));   // 第一次:受理后失败
            Assert.That(_client.SwitchPending, Is.False);

            // 第二次还没等到同步应答就被拉去了 3线:上一次的「已受理」不能算在这一次头上。
            _now = 5f;
            Assert.That(_client.RequestSwitch(Line2), Is.True);
            Arrive(Line3);
            Assert.That(_client.SwitchPending, Is.True);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchingText(2)));
        }

        // ── 传输层在 Call 的调用栈里重入 ─────────────────────────────────────

        [Test]
        public void EntryNotificationDispatchedInsideTheCall_IsNotMistakenForTheSwitchResult()
        {
            LoadDirectory();
            _now = 3f;
            // 真实传输层在 Call 返回之前先派发收件箱:一条早就在路上的入场通知(被拉去 3线)在 RequestSwitch 内部重入。
            _net.OnCall = _ => Arrive(Line3);
            Assert.That(_client.RequestSwitch(Line2), Is.True);
            _net.OnCall = null;

            Assert.That(_client.SwitchPending, Is.True);
            Assert.That(_client.SwitchTargetChannelNo, Is.EqualTo(2));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchingText(2)));
            Assert.That(_client.Lines, Is.Empty);
            Assert.That(_switched, Is.Empty);

            _net.Calls[0].Reply(Accepted());
            _now = 4f;
            Arrive(Line2);
            Assert.That(_switched, Is.EqualTo(new uint[] { 2 }));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchedText(2)));
        }

        [Test]
        public void SendFailureInsideTheCall_ErrorThenDisconnect_LeavesACleanSession()
        {
            LoadDirectory();
            _now = 3f;
            // GameClient.Call 发送失败:先 onError("send failed: …"),再同步触发断线,都在 RequestSwitch 的调用栈里。
            _net.OnCall = call =>
            {
                call.Fail("send failed: socket closed");
                _net.Disconnect();
            };
            Assert.DoesNotThrow(() => _client.RequestSwitch(Line2));
            _net.OnCall = null;

            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.SwitchTargetSceneId, Is.Zero);
            Assert.That(_client.Status, Is.Empty, "断线复位盖在失败文案之后");
            Assert.That(_client.StatusIsError, Is.False);
            Assert.That(_client.Lines, Is.Empty);
            Assert.That(_client.HasDirectory, Is.False);

            // 新会话:旧请求的回调不作数;旧会话那次切线的目标线只是普通进场;连点保护随连接复位。
            _net.Calls[0].Reply(Accepted());
            _net.Reconnect();
            _now = 3.5f;
            Arrive(Line2);
            Assert.That(_switched, Is.Empty);
            Assert.That(_client.CooldownRemainingSeconds, Is.Zero);
            Assert.That(_client.Status, Is.Empty);
            PushDirectory(BuildDirectory());
            Assert.That(_client.RequestSwitch(Line1), Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
        }

        [Test]
        public void DisconnectDispatchedInsideTheCall_ResetsTheSession_AndTheErrorThatFollowsIsIgnored()
        {
            LoadDirectory();
            _now = 3f;
            // GameClient.Call 里那一轮 Tick 先派发了断线,随后它自己再报 "disconnected"。
            _net.OnCall = call =>
            {
                _net.Disconnect();
                call.Fail("disconnected");
            };
            Assert.That(_client.RequestSwitch(Line2), Is.True, "请求交给过传输层,结论看状态");
            _net.OnCall = null;
            int before = _changes;

            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.SwitchTargetSceneId, Is.Zero);
            Assert.That(_client.Status, Is.Empty);
            Assert.That(_client.Lines, Is.Empty);

            _net.Calls[0].Reply(Accepted());
            _net.Calls[0].Fail("rpc timeout");
            _now = 200f;
            _client.Tick();
            Assert.That(_changes, Is.EqualTo(before));
            Assert.That(_client.Status, Is.Empty, "复位之后没有残留的在途切线,不会冒出超时");
        }

        // ── 进场 ────────────────────────────────────────────────────────────

        [Test]
        public void UnsolicitedSceneEntry_DropsTheDirectory_ClearsOldHints_AndRefreshes()
        {
            LoadDirectory();
            _client.RequestSwitch(Line3);   // 留下一条「该线路已满」的提示
            Assert.That(_client.Status, Is.Not.Empty);
            int before = _changes;

            _now = 3f;
            Arrive(Line2);

            Assert.That(_client.Lines, Is.Empty);
            Assert.That(_client.HasDirectory, Is.False);
            Assert.That(_client.CurrentChannelNo, Is.Zero);
            Assert.That(_client.Status, Is.Empty, "上一个场景的提示不带过来");
            Assert.That(_switched, Is.Empty);
            Assert.That(_client.CooldownRemainingSeconds, Is.Zero);
            Assert.That(_net.OneWays.Count, Is.EqualTo(2));
            Assert.That(_client.ListPending, Is.True);
            Assert.That(_changes, Is.EqualTo(before + 1));

            PushDirectory(BuildDirectory());
            Assert.That(_client.CurrentChannelNo, Is.EqualTo(2), "角标显示新场景的线号");
        }

        [Test]
        public void SceneEntry_AbandonsTheListRequestOfThePreviousScene_AndQueuesAFreshOne()
        {
            _client.RequestList();          // 在上一个场景发的,还没等到回包
            Assert.That(_client.ListPending, Is.True);

            _now = 1f;
            Arrive(Line2);
            Assert.That(_client.ListQueued, Is.True, "新场景的列线排队等间隔");
            Assert.That(_client.ListPending, Is.True);
            Assert.That(_net.OneWays.Count, Is.EqualTo(1));

            // 旧请求迟到的拒绝不再作数:新场景里不该冒出一条「获取不到」。
            _now = 1.5f;
            _net.PushEnvelopeError(MessageIds.SceneInfoC2S, (uint)common_error.KServiceUnavailable);
            Assert.That(_client.Status, Is.Empty);
            Assert.That(_client.ListPending, Is.True);

            _now = 2.5f;
            _client.Tick();
            Assert.That(_net.OneWays.Count, Is.EqualTo(2));
            Assert.That(_client.ListQueued, Is.False);
            Assert.That(_client.ListPending, Is.True);

            // 等待期限从补发那一刻(2.5 秒)重新起算:旧请求的 5 秒期限不作数。
            _now = 5f;
            _client.Tick();
            Assert.That(_client.ListPending, Is.True);
            Assert.That(_client.Status, Is.Empty);
            _now = 7.5f;
            _client.Tick();
            Assert.That(_client.ListPending, Is.False);
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.ListUnavailableMessage));
        }

        // ── 断线与旧连接隔离 ────────────────────────────────────────────────

        [Test]
        public void Disconnect_ResetsEverything_IncludingCooldownAndTheQueuedRefresh()
        {
            SwitchToLine2AndReload();       // 结束于 2.5 秒:在 2线、冷却到 12 秒、43 下次可发 5 秒
            _now = 3f;
            _client.RequestList();          // 间隔未到 → 待补发
            Assert.That(_client.ListQueued, Is.True);
            Assert.That(_client.HasDirectory, Is.True);
            Assert.That(_client.CooldownRemainingSeconds, Is.EqualTo(9f));
            Assert.That(_client.Status, Is.Not.Empty);
            int before = _changes;

            _net.Disconnect();

            Assert.That(_client.Lines, Is.Empty);
            Assert.That(_client.HasDirectory, Is.False);
            Assert.That(_client.CurrentChannelNo, Is.Zero);
            Assert.That(_client.SwitchEnabled, Is.False);
            Assert.That(_client.ListPending, Is.False);
            Assert.That(_client.ListQueued, Is.False);
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.CooldownRemainingSeconds, Is.Zero);
            Assert.That(_client.Status, Is.Empty);
            Assert.That(_client.StatusIsError, Is.False);
            Assert.That(_changes, Is.EqualTo(before + 1));

            // 宿主再转发一次断线也安全。
            _client.HandleDisconnected();
            Assert.That(_changes, Is.EqualTo(before + 1));

            _now = 10f;
            _client.Tick();
            Assert.That(_net.OneWays.Count, Is.EqualTo(2), "待补发标记已清,不会在断线后冒出一个 43");
            Assert.That(_changes, Is.EqualTo(before + 1));
        }

        [Test]
        public void Disconnect_WhileSwitching_IgnoresTheLateCallbacks()
        {
            LoadDirectory();
            _client.RequestSwitch(Line2);

            _client.HandleDisconnected();   // 宿主转发的断线
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.Status, Is.Empty);
            int before = _changes;

            _net.Calls[0].Fail("disconnected");
            _net.Calls[0].Reply(Accepted());
            _net.Calls[0].Reply(Rejected((uint)scene_error.KEnterSceneFailed));

            Assert.That(_changes, Is.EqualTo(before));
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.Status, Is.Empty);
        }

        [Test]
        public void LateCallbacksFromTheOldConnection_CannotTouchTheNewSession()
        {
            LoadDirectory();
            _client.RequestSwitch(Line2);   // Calls[0]:旧连接上的切线
            _net.Disconnect();
            _net.Reconnect();

            // 断线后半秒就进了新会话:旧连接上 43 的下次可发时刻是 2.5 秒、63 的是 1 秒,都还没到。
            // 限流计时随连接重置,所以 43 立即发出、63 也不被连点保护拦下(不重置的话下面两条断言都会失败)。
            _now = 0.5f;
            Arrive(Line1);
            Assert.That(_net.OneWays.Count, Is.EqualTo(2));
            PushDirectory(BuildDirectory());
            Assert.That(_client.RequestSwitch(Line2), Is.True);   // Calls[1]:新会话的切线
            int before = _changes;

            _net.Calls[0].Fail("disconnected");
            _net.Calls[0].Reply(Rejected((uint)scene_error.KEnterSceneFailed));
            _net.Calls[0].Reply(Accepted());

            Assert.That(_changes, Is.EqualTo(before));
            Assert.That(_client.SwitchPending, Is.True);
            Assert.That(_client.SwitchTargetChannelNo, Is.EqualTo(2));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchingText(2)));

            _net.Calls[1].Reply(Accepted());
            Arrive(Line2);
            Assert.That(_switched, Is.EqualTo(new uint[] { 2 }));
        }

        [Test]
        public void SendFailureWhileHandlingArrival_ResetsAndDoesNotReportSwitched()
        {
            LoadDirectory();
            _now = 3f;
            _client.RequestSwitch(Line2);
            _net.Calls[0].Reply(Accepted());

            // 到达后补发 43 时连接恰好断了:传输层在发送调用里同步通知断线,本类随即整体复位。
            _net.DisconnectOnSend = true;
            Arrive(Line2);

            Assert.That(_net.OneWays.Count, Is.EqualTo(2));
            Assert.That(_switched, Is.Empty, "已经断线,不再报「已切换」");
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.ListPending, Is.False);
            Assert.That(_client.ListQueued, Is.False);
            Assert.That(_client.CooldownRemainingSeconds, Is.Zero);
            Assert.That(_client.Status, Is.Empty);
            Assert.That(_client.HasDirectory, Is.False);
        }

        [Test]
        public void SilentConnectionChange_IsTreatedAsADisconnect()
        {
            LoadDirectory();
            _client.RequestSwitch(Line2);
            int before = _changes;

            _net.Identity = new object();   // GameClient 静默换 gate:没有断线通知
            // 旧连接的应答抢在下一次 Tick 之前到:连接对不上,不作数(哪怕它带着拒绝码)。
            _net.Calls[0].Reply(Rejected((uint)scene_error.KEnterSceneFailed));
            _net.Calls[0].Fail("disconnected");
            Assert.That(_changes, Is.EqualTo(before));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchingText(2)));
            Assert.That(_client.StatusIsError, Is.False);

            _client.Tick();
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.Lines, Is.Empty);
            Assert.That(_client.HasDirectory, Is.False);
            Assert.That(_client.Status, Is.Empty);
            Assert.That(_changes, Is.EqualTo(before + 1));
        }

        // ── 冷却 ────────────────────────────────────────────────────────────

        [Test]
        public void Cooldown_CountsDownWithTheClock_AndRaisesChangedOnceWhenItEnds()
        {
            SwitchToLine2AndReload();       // 2.5 秒:冷却到 12 秒
            int before = _changes;

            Assert.That(_client.CooldownRemainingSeconds, Is.EqualTo(9.5f));
            Assert.That(_client.GlobalBlockReason, Is.EqualTo(SceneChannelBlockReason.CoolingDown));
            Assert.That(_client.BlockReasonFor(_client.Lines[0]), Is.EqualTo(SceneChannelBlockReason.CoolingDown));

            _now = 5f;
            Assert.That(_client.CooldownRemainingSeconds, Is.EqualTo(7f));
            _client.Tick();
            _now = 11.5f;
            Assert.That(_client.CooldownRemainingSeconds, Is.EqualTo(0.5f));
            _client.Tick();
            Assert.That(_changes, Is.EqualTo(before), "倒计时走动不触发 Changed");

            _now = 12f;
            Assert.That(_client.CooldownRemainingSeconds, Is.Zero);
            Assert.That(_client.BlockReasonFor(_client.Lines[0]), Is.EqualTo(SceneChannelBlockReason.None));
            _client.Tick();
            Assert.That(_changes, Is.EqualTo(before + 1), "到点通知一次");

            _now = 13f;
            _client.Tick();
            Assert.That(_changes, Is.EqualTo(before + 1));
            Assert.That(_client.CooldownRemainingSeconds, Is.Zero);
        }

        [Test]
        public void RequestSwitch_DuringCooldown_IsBlockedWithRemainingSeconds_ThenAllowed()
        {
            SwitchToLine2AndReload();       // 2.5 秒:在 2线,冷却到 12 秒

            _now = 5f;
            Assert.That(_client.RequestSwitch(Line1), Is.False);
            Assert.That(_client.Status, Is.EqualTo("切线冷却中，7秒后可再次切线。"));
            Assert.That(_client.StatusIsError, Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(1));

            _now = 11.2f;
            Assert.That(_client.RequestSwitch(Line1), Is.False);
            Assert.That(_client.Status, Is.EqualTo("切线冷却中，1秒后可再次切线。"), "剩 0.8 秒向上取整");

            _now = 12f;
            Assert.That(_client.RequestSwitch(Line1), Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(2));
            Assert.That(((EnterSceneC2SRequest)_net.Calls[1].Request).SceneInfo.SceneId, Is.EqualTo(Line1));
        }

        [Test]
        public void DirectoryWithoutCooldown_StartsNoCooldown()
        {
            LoadDirectory(BuildDirectory(cooldownSeconds: 0));
            _client.RequestSwitch(Line2);
            _net.Calls[0].Reply(Accepted());
            Arrive(Line2);

            Assert.That(_switched, Is.EqualTo(new uint[] { 2 }));
            Assert.That(_client.CooldownRemainingSeconds, Is.Zero);
        }

        // ── 释放 ────────────────────────────────────────────────────────────

        [Test]
        public void Dispose_Unsubscribes_AndIgnoresEverythingAfterwards()
        {
            LoadDirectory();
            _client.RequestSwitch(Line2);
            _client.Dispose();
            int before = _changes;

            Assert.That(_net.DisconnectedSubscriberCount, Is.Zero);
            Assert.DoesNotThrow(() => PushDirectory(BuildDirectory(line2: SceneChannelState.Full)));
            Assert.DoesNotThrow(() =>
                _net.PushEnvelopeError(MessageIds.SceneInfoC2S, (uint)common_error.KRateLimitExceeded));
            _net.Calls[0].Reply(Accepted());
            _net.Calls[0].Fail("disconnected");
            _client.HandleSceneEntered(Scene(Line2));
            _client.HandleServerTip(Tip((uint)scene_error.KEnterSceneFailed));
            _client.HandleDisconnected();
            _now = 500f;
            _client.Tick();

            Assert.That(_client.RequestList(), Is.False);
            Assert.That(_client.RequestSwitch(Line2), Is.False);
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_client.ListPending, Is.False);
            Assert.That(_client.Lines[1].Load, Is.EqualTo(SceneChannelLoad.Busy), "释放后的推送不再改线路");
            Assert.That(_changes, Is.EqualTo(before));
            Assert.That(_switched, Is.Empty);
            Assert.That(_net.OneWays.Count, Is.EqualTo(1));
            Assert.That(_net.Calls.Count, Is.EqualTo(1));
        }

        // ── 小工具 ──────────────────────────────────────────────────────────

        /// <summary>地图 1 的四条线:1线 流畅 / 2线(默认繁忙)/ 3线 爆满 / 4线 回收中。</summary>
        private static SceneChannelDirectory BuildDirectory(uint cooldownSeconds = 10, bool enabled = true,
                                                           SceneChannelState line2 = SceneChannelState.Busy)
        {
            var directory = new SceneChannelDirectory
            {
                ZoneId = 1, SceneConfigId = Map, UpdatedAtMs = 1000,
                SwitchCooldownSeconds = cooldownSeconds, MaxPlayersPerChannel = 100, SwitchEnabled = enabled,
            };
            directory.Channels.Add(Info(Line1, 1, 12, SceneChannelState.Smooth));
            directory.Channels.Add(Info(Line2, 2, 80, line2));
            directory.Channels.Add(Info(Line3, 3, 100, SceneChannelState.Full));
            directory.Channels.Add(Info(Line4, 4, 3, SceneChannelState.Closing));
            return directory;
        }

        private static SceneChannelInfo Info(ulong sceneId, uint channelNo, uint players, SceneChannelState state) =>
            new SceneChannelInfo { SceneId = sceneId, ChannelNo = channelNo, PlayerCount = players, State = state };

        private void PushDirectory(SceneChannelDirectory directory) =>
            _net.Push(MessageIds.NotifySceneInfo, new SceneInfoS2C { ChannelDirectory = directory });

        /// <summary>在当前时刻发一次 43 并立刻收到目录。从 0 秒调用后,43 的下次可发时刻是 2.5 秒。</summary>
        private void LoadDirectory(SceneChannelDirectory directory = null)
        {
            Assert.That(_client.RequestList(), Is.True);
            PushDirectory(directory ?? BuildDirectory());
            Assert.That(_client.HasDirectory, Is.True);
        }

        /// <summary>
        /// 走完一次成功的切线并重新拉到目录。时间线:0 秒拉目录 → 1 秒发 63 并受理 → 2 秒到达 2线
        /// (冷却 10 秒,到 12 秒)→ 2.5 秒补发 43 并收到目录。结束时 _now == 2.5,43 的下次可发时刻是 5 秒。
        /// </summary>
        private void SwitchToLine2AndReload()
        {
            LoadDirectory();
            _now = 1f;
            Assert.That(_client.RequestSwitch(Line2), Is.True);
            _net.Calls[0].Reply(Accepted());
            _now = 2f;
            Arrive(Line2);
            _now = 2.5f;
            _client.Tick();
            Assert.That(_net.OneWays.Count, Is.EqualTo(2));
            PushDirectory(BuildDirectory());
            Assert.That(_client.CurrentChannelNo, Is.EqualTo(2));
        }

        private static SceneInfoComp Scene(ulong sceneId) => new SceneInfoComp { SceneConfigId = Map, SceneId = sceneId };

        /// <summary>照 GameClient 的顺序:先改权威的当前场景,再广播入场通知。</summary>
        private void Arrive(ulong sceneId)
        {
            _sceneId = sceneId;
            _client.HandleSceneEntered(Scene(sceneId));
        }

        private static EnterSceneC2SResponse Accepted() => new EnterSceneC2SResponse();

        private static EnterSceneC2SResponse Rejected(uint tipId) =>
            new EnterSceneC2SResponse { ErrorMessage = Tip(tipId) };

        private static TipInfoMessage Tip(uint tipId) => new TipInfoMessage { Id = tipId };

        private void AssertBlocked(ulong sceneId, SceneChannelBlockReason reason)
        {
            int calls = _net.Calls.Count;
            Assert.That(_client.RequestSwitch(sceneId), Is.False, reason.ToString());
            Assert.That(_client.Status, Is.EqualTo(SceneChannelModels.DescribeBlock(reason)), reason.ToString());
            Assert.That(_client.StatusIsError, Is.True);
            Assert.That(_client.SwitchPending, Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(calls));
        }

        /// <summary>拉目录 → 3 秒切 2线并受理 → 服务端提示判失败(顺手发出一次刷新)。结束时状态行是笼统的「切线失败」。</summary>
        private void FailSwitchToLine2ByTip()
        {
            LoadDirectory();
            _now = 3f;
            Assert.That(_client.RequestSwitch(Line2), Is.True);
            _net.Calls[0].Reply(Accepted());
            _client.HandleServerTip(Tip((uint)scene_error.KEnterSceneFailed));
            Assert.That(_client.Status, Is.EqualTo(SceneChannelClient.SwitchFailedMessage));
            Assert.That(_net.OneWays.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// 发起一次切到 2线的请求,让传输层报 <paramref name="error"/>,断言失败文案。
        /// 每次先把时钟拨过 3 秒:越过连点保护(1 秒)与被限流之后的退避(2.5 秒)。
        /// </summary>
        private void AssertTransportFailure(string error, string expected)
        {
            _now += 3f;
            Assert.That(_client.RequestSwitch(Line2), Is.True, error);
            _net.Calls[_net.Calls.Count - 1].Fail(error);
            Assert.That(_client.SwitchPending, Is.False, error);
            Assert.That(_client.Status, Is.EqualTo(expected), error);
            Assert.That(_client.StatusIsError, Is.True);
        }
    }

    /// <summary>
    /// 切线用的假传输:记录单向发送与 Call(每个 Call 各自保留应答 / 报错入口,可回放旧请求的迟到回调),
    /// 可推原始字节、可模拟 gate 写在信封上的拒绝。回复先序列化再用 parser 解析,与线上路径一致。
    /// </summary>
    internal sealed class SceneChannelFakeTransport : IBattleTransport
    {
        internal sealed class SentCall
        {
            public uint Id;
            public IMessage Request;
            public Action<IMessage> Reply;
            public Action<string> Fail;
        }

        public ulong PlayerId { get; set; } = 10;
        public bool IsReady { get; set; } = true;
        public event Action Disconnected;
        /// <summary>连接身份;换成新对象即模拟 GameClient 静默换 gate。</summary>
        public object Identity = new object();
        /// <summary>非 null 时 Call 内同步调用 onError(模拟未连接 / 协程宿主未就绪)。</summary>
        public string SyncError;
        /// <summary>
        /// 非 null 时在 Call 内、登记完这次调用之后同步执行(参数是刚登记的那次调用)。
        /// 真实传输层(GameClient.Call)在第一次让出之前会先跑一轮 Tick:收件箱里已有的入场通知、31、提示、
        /// 断线都在 Call 的调用栈里派发;发送失败则是先 onError、再同步触发断线。用它模拟这些重入。
        /// </summary>
        public Action<SentCall> OnCall;
        public readonly List<(uint Id, IMessage Request)> OneWays = new List<(uint Id, IMessage Request)>();
        public readonly List<SentCall> Calls = new List<SentCall>();
        /// <summary>已注册的推送处理器;与 GameClient.OnNotify 一样,一个 id 只留最后一次注册。</summary>
        public readonly Dictionary<uint, Action<MessageContent>> Notifies = new Dictionary<uint, Action<MessageContent>>();

        public int DisconnectedSubscriberCount => Disconnected?.GetInvocationList().Length ?? 0;

        public void RegisterNotify(uint id, Action<MessageContent> handler) => Notifies[id] = handler;

        /// <summary>为 true 时单向发送「失败」:照 GameClient.TrySendOneWay,发送失败会同步触发断线通知。</summary>
        public bool DisconnectOnSend;

        public void SendOneWay(uint id, IMessage request)
        {
            OneWays.Add((id, request));
            if (DisconnectOnSend) Disconnect();
        }

        public void Call<T>(uint id, IMessage request, MessageParser<T> parser, Action<T> onResponse, Action<string> onError)
            where T : IMessage<T>
        {
            var call = new SentCall
            {
                Id = id,
                Request = request,
                Reply = message => onResponse(parser.ParseFrom(((T)message).ToByteString())),
                Fail = onError,
            };
            Calls.Add(call);
            if (SyncError != null) onError(SyncError);
            OnCall?.Invoke(call);
        }

        /// <summary>模拟服务端推送;未注册的 id 会直接抛,测试里那就是接线漏了。</summary>
        public void Push(uint id, IMessage message) => PushRaw(id, message.ToByteArray());

        public void PushRaw(uint id, byte[] bytes) =>
            Notifies[id](new MessageContent { MessageId = id, SerializedMessage = ByteString.CopyFrom(bytes) });

        /// <summary>模拟 gate 对单向请求的拒绝:回包带请求 id 与信封错误码,没有在途项可配,落到推送处理器。</summary>
        public void PushEnvelopeError(uint id, uint tipId) =>
            Notifies[id](new MessageContent { MessageId = id, Id = 77, ErrorMessage = new TipInfoMessage { Id = tipId } });

        public void Disconnect()
        {
            IsReady = false;
            Disconnected?.Invoke();
        }

        /// <summary>新连接就绪(连接身份随之换新)。</summary>
        public void Reconnect()
        {
            Identity = new object();
            IsReady = true;
        }
    }
}
