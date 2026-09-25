using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Guildpb;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Guild;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Guild;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Tests.EditMode.Tianyong
{
    public sealed class GuildClientTests
    {
        private GuildFakeTransport _net;
        private GuildClient _client;
        [SetUp] public void SetUp() { _net = new GuildFakeTransport(); _client = new GuildClient(_net); }
        [TearDown] public void TearDown() => _client.Dispose();

        [Test] public void NotInGuildIsLoadedEmptyState()
        {
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } });
            Assert.That(_client.HasLoaded, Is.True); Assert.That(_client.Info, Is.Null); Assert.That(_client.Busy, Is.False);
        }
        [Test] public void LateSnapshotAfterResetCannotRepopulateTheSession()
        {
            _client.Refresh(); var callback = _net.Pending;
            _client.Reset(); callback(new GetPlayerGuildResponse { Guild = Fixture() });
            Assert.That(_client.Info, Is.Null); Assert.That(_client.HasLoaded, Is.False);
        }
        [Test] public void SnapshotFromAnotherPlayerIsRejected()
        {
            _client.Refresh(); _net.PlayerId = 99;
            _net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            Assert.That(_client.Info, Is.Null);
        }
        [Test] public void UnconfirmedMembershipCannotSendApply()
        {
            _client.ApplyToJoin(555); Assert.That(_net.Calls, Is.Empty);
        }
        [Test] public void DuplicateApplyIsBlockedWhileBusyAndDoesNotSetMembership()
        {
            // Empty() 的 NotInGuild 回包会把 MyApplicationsQueued 置真，但本用例不调用
            // DrainQueued，所以那面旗子不会变成请求，Calls 里只该有申请这一发。
            Empty(); _client.ApplyToJoin(555); _client.ApplyToJoin(556);
            Assert.That(_net.Calls.Count(x => x == MessageIds.ApplyJoinGuild), Is.EqualTo(1));
            Assert.That(_client.Info, Is.Null);
            _net.Reply(new ApplyJoinGuildResponse());
            // 申请不等于入帮：回包只能带来“已提交”，Info 必须仍为空，等审批通过后的推送 / 重拉。
            Assert.That(_client.Info, Is.Null);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.ListMyGuildApplications));
            Assert.That(_client.Status, Does.Contain("等待帮主或长老审批"));
        }
        [TestCase(0u, false)] [TestCase(1u, true)] [TestCase(2u, false)] [TestCase(3u, true)]
        public void AnnouncementPermissionMatchesAuthoritativeServerRoles(uint role, bool allowed)
        {
            var info = Fixture(); info.Members[0].Role = role;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = info });
            Assert.That(_client.CanEditAnnouncement, Is.EqualTo(allowed));
            _client.SaveAnnouncement("新的公告");
            Assert.That(_net.Calls.Contains(MessageIds.SetGuildAnnouncement), Is.EqualTo(allowed));
        }
        [Test] public void FailedAnnouncementDoesNotOverwriteTheSnapshot()
        {
            Load(); _client.SaveAnnouncement("更改");
            _net.Reply(new SetAnnouncementResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNoPermission } });
            Assert.That(_client.Info.Announcement, Is.EqualTo("同道相聚，月满灯明。"));
            Assert.That(_client.Status, Does.Contain("无权"));
        }
        [Test] public void DisconnectedCallIsNotSent()
        {
            _net.IsReady = false; _client.Refresh();
            Assert.That(_net.Calls, Is.Empty); Assert.That(_client.Busy, Is.False);
        }
        [Test] public void LeaderCannotUseLeaveAndMemberCannotDisband()
        {
            Load(); _client.Leave(); Assert.That(_net.Calls.Contains(MessageIds.LeaveGuild), Is.False);
            _client.Reset(); var info = Fixture(); info.LeaderId = 2; info.Members[0].Role = 0;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = info });
            _client.Disband(); Assert.That(_net.Calls.Contains(MessageIds.DisbandGuild), Is.False);
        }
        [Test] public void TransportFailurePreservesDataButBlocksRetryUntilRealDisconnect()
        {
            Load(); _client.Refresh(); _net.Error("rpc timeout");
            Assert.That(_client.Info.GuildId, Is.EqualTo(555)); Assert.That(_client.Busy, Is.False);
            int before = _net.Calls.Count;
            _client.Refresh(); _client.Browse(); _client.SaveAnnouncement("不可发送");
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.RequiresReconnect, Is.True);
            _client.Reset(); Assert.That(_client.RequiresReconnect, Is.True);
            _net.Disconnect(); _net.IsReady = true; _client.Refresh();
            Assert.That(_client.RequiresReconnect, Is.False);
            Assert.That(_client.Busy, Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(before + 1));
        }
        [Test] public void ResetWhilePendingDoesNotReuseAZeroIdResponseSlot()
        {
            _client.Refresh(); var oldResponse = _net.Pending; _client.Reset();
            _client.Refresh(); Assert.That(_net.Calls.Count, Is.EqualTo(1));
            Assert.That(_client.RequiresReconnect, Is.True);
            oldResponse(new GetPlayerGuildResponse { Guild = Fixture() });
            Assert.That(_client.Info, Is.Null);
        }
        [Test] public void SilentGameClientGateReplacementClearsQuarantineWithoutDisconnectNotification()
        {
            var game = new MmorpgClient.Game.GameClient("http://127.0.0.1:1");
            var field = typeof(MmorpgClient.Game.GameClient).GetField("_gate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var reset = typeof(MmorpgClient.Game.GameClient).GetMethod("ResetConnectionState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            int notifications = 0; game.OnDisconnected += () => notifications++;
            GuildClient client = null;
            try
            {
                var firstGate = new GateTcpClient(new MuduoCodec());
                field.SetValue(game, firstGate);
                Assert.That(game.GateConnectionIdentity, Is.SameAs(firstGate));
                var net = new GuildFakeTransport();
                client = new GuildClient(net, () => game.GateConnectionIdentity);
                client.Refresh(); net.Error("rpc timeout");
                client.Reset(); Assert.That(client.RequiresReconnect, Is.True);
                reset.Invoke(game, null); // 与 EnterZone/RedirectFlow 相同的 notify:false 路径。
                Assert.That(notifications, Is.Zero);
                Assert.That(game.GateConnectionIdentity, Is.Null);
                var secondGate = new GateTcpClient(new MuduoCodec());
                field.SetValue(game, secondGate);
                client.ObserveConnection();
                Assert.That(client.RequiresReconnect, Is.False);
                client.Refresh();
                Assert.That(net.Calls.Count, Is.EqualTo(2));
                Assert.That(client.Busy, Is.True);
            }
            finally
            {
                client?.Dispose(); game.Disconnect();
                if (game.World.Root != null) UnityEngine.Object.DestroyImmediate(game.World.Root.gameObject);
            }
        }
        [Test] public void OldConnectionCallbackCannotApplyBeforeNextUiObservation()
        {
            object identity = new object();
            var net = new GuildFakeTransport();
            using var client = new GuildClient(net, () => identity);
            client.Refresh(); var oldCallback = net.Pending;
            identity = new object();
            oldCallback(new GetPlayerGuildResponse { Guild = Fixture() });
            Assert.That(client.Info, Is.Null);
            client.ObserveConnection();
            Assert.That(client.Busy, Is.False);
            Assert.That(client.RequiresReconnect, Is.False);
            client.Refresh(); Assert.That(net.Calls.Count, Is.EqualTo(2));
        }
        [Test] public void AnnouncementThatWouldExceedTheGatePacketLimitIsNotSent()
        {
            Load(); _client.SaveAnnouncement(new string('汉', GuildClient.MaxAnnouncementChars + 1));
            Assert.That(_net.Calls.Contains(MessageIds.SetGuildAnnouncement), Is.False);
            Assert.That(_client.Status, Does.Contain(GuildClient.MaxAnnouncementChars.ToString()));
            _client.SaveAnnouncement(new string('汉', GuildClient.MaxAnnouncementChars));
            Assert.That(_net.Calls.Contains(MessageIds.SetGuildAnnouncement), Is.True);
        }
        [Test] public void ZoneAndNameRejectionsKeepTheEmptyStateWithReadableMessages()
        {
            Empty(); _client.Create("青云门", 1);
            _net.Reply(new CreateGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNameTaken } });
            Assert.That(_client.Status, Does.Contain("已被使用")); Assert.That(_client.Info, Is.Null);
            _client.Create("青云门二", 1);
            _net.Reply(new CreateGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildHomeZoneUnknown } });
            Assert.That(_client.Status, Does.Contain("区服")); Assert.That(_client.Info, Is.Null);
            Assert.That(_client.RequiresReconnect, Is.False);
        }

        // ── 职位与权限（纯规则，不发请求）────────────────────────────────

        /// <summary>客户端 Rank 表必须与 go/guild constants.Rank 同值；2 号（副帮主）不启用，落 RankNone。</summary>
        [Test] public void RankHelperMatchesServer()
        {
            Assert.That(GuildRoles.Rank(GuildRoles.Member), Is.EqualTo(GuildRoles.RankMember));
            Assert.That(GuildRoles.Rank(GuildRoles.Officer), Is.EqualTo(GuildRoles.RankOfficer));
            Assert.That(GuildRoles.Rank(GuildRoles.Leader), Is.EqualTo(GuildRoles.RankLeader));
            Assert.That(GuildRoles.Rank(2u), Is.EqualTo(GuildRoles.RankNone));
            Assert.That(GuildRoles.Rank(99u), Is.EqualTo(GuildRoles.RankNone));
        }
        /// <summary>请离必须严格高一级：长老不能请离长老，也不能请离帮主；未启用的 2 号谁也请离不了。</summary>
        [TestCase(3u, 1u, true)] [TestCase(3u, 0u, true)] [TestCase(1u, 0u, true)]
        [TestCase(1u, 1u, false)] [TestCase(1u, 3u, false)] [TestCase(0u, 0u, false)] [TestCase(2u, 0u, false)]
        public void KickPermissionFollowsRank(uint actor, uint target, bool expected)
            => Assert.That(GuildRoles.CanKick(actor, target), Is.EqualTo(expected));

        // ── 写响应直接应用快照（不再补一发 GetPlayerGuild）──────────────

        [Test] public void WriteResponseAppliesSnapshotWithoutExtraRefresh()
        {
            Load();
            _client.SetMemberRole(2, GuildRoles.Officer);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.SetGuildMemberRole));
            var promoted = Fixture(); promoted.Members[1].Role = GuildRoles.Officer; promoted.OfficerCount = 1;
            _net.Reply(new SetGuildMemberRoleResponse { Guild = promoted });
            Assert.That(_client.Info.Members[1].Role, Is.EqualTo(GuildRoles.Officer));
            Assert.That(_client.Info.OfficerCount, Is.EqualTo(1u));
            // 快照已经是权威结果，多发一次 GetPlayerGuild 只会多一次等待与闪烁。
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.SetGuildMemberRole));
            Assert.That(_client.Busy, Is.False);
        }
        [Test] public void SaveAnnouncementAppliesSnapshotWithoutRefresh()
        {
            var officer = Fixture();
            officer.LeaderId = 2; officer.Members[0].Role = GuildRoles.Officer; officer.Members[1].Role = GuildRoles.Leader;
            officer.OfficerCount = 1;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = officer });
            Assert.That(_client.CanEditAnnouncement, Is.True);
            _client.SaveAnnouncement("新公告");
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.SetGuildAnnouncement));
            var updated = Fixture();
            updated.LeaderId = 2; updated.Members[0].Role = GuildRoles.Officer; updated.Members[1].Role = GuildRoles.Leader;
            updated.Announcement = "新公告";
            _net.Reply(new SetAnnouncementResponse { Guild = updated });
            Assert.That(_client.Info.Announcement, Is.EqualTo("新公告"));
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.SetGuildAnnouncement));
        }
        [Test] public void ReviewReloadsApplicants()
        {
            Load();
            _client.Review(20001, true);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.ReviewGuildApplication));
            _net.Reply(new ReviewGuildApplicationResponse { Guild = Fixture() });
            // 审批改的是别人的在册状态，本帮快照回带了，但待审列表只能重拉。
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.ListGuildApplications));
            // 审批结果必须活过列表刷新:Request 发出时会同步写“正在读取帮会…”,
            // 只断言“最后发的是哪条消息”看不出文案被盖掉(这正是它曾经被盖掉的原因)。
            Assert.That(_client.Status, Does.Contain("已同意"));
        }

        // ── 推送（NotifyGuildChanged）与排队重拉 ────────────────────────

        [Test] public void NotifyForAnotherGuildIsIgnored()
        {
            Load();
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C { GuildId = 999, Kind = GuildChangeKind.MemberLeft });
            Assert.That(_client.RefreshQueued, Is.False);
            Assert.That(_client.ApplicantsQueued, Is.False);
            Assert.That(_client.MyApplicationsQueued, Is.False);
        }
        [Test] public void KickedNotifyQueuesExactlyOneRefresh()
        {
            Load();
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 555, Kind = GuildChangeKind.MemberKicked, TargetPlayerId = 1 });
            Assert.That(_client.RefreshQueued, Is.True);
            int before = _net.Calls.Count;
            // 每帧都会调 DrainQueued：第一次发出去之后必须自己关掉旗子，否则会连发。
            _client.DrainQueued(false);
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(before + 1));
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
        }
        [Test] public void NotifyWhileBusyDefersUntilIdle()
        {
            Load();
            _client.Browse(); // 占住唯一的在途请求位
            Assert.That(_client.Busy, Is.True);
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C { GuildId = 555, Kind = GuildChangeKind.RoleChanged });
            int before = _net.Calls.Count;
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.RefreshQueued, Is.True);
            _net.Reply(new GetGuildRankResponse { Page = 1, PageSize = 5 });
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
        }
        [Test] public void ApprovedNotifyWhileOutsideGuildRefreshes()
        {
            Empty();
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 777, Kind = GuildChangeKind.MemberJoined, TargetPlayerId = 1 });
            Assert.That(_client.RefreshQueued, Is.True);
            Assert.That(_client.Status, Does.Contain("入帮申请已通过"));
        }
        /// <summary>
        /// 申请视图开着就重拉列表；关着则重拉 GetPlayerGuild——服务端会重算
        /// pending_application_count，成员页那颗角标才跟得上。
        /// </summary>
        [TestCase(true)] [TestCase(false)]
        public void ApplicationReceivedReloadsListOrBadge(bool listVisible)
        {
            Load();
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 555, Kind = GuildChangeKind.ApplicationReceived, TargetPlayerId = 9 });
            Assert.That(_client.ApplicantsQueued, Is.True);
            Assert.That(_client.RefreshQueued, Is.False);
            _client.DrainQueued(listVisible);
            Assert.That(_net.Calls.Last(), Is.EqualTo(listVisible ? MessageIds.ListGuildApplications : MessageIds.GetPlayerGuild));
            if (listVisible) return;
            var updated = Fixture(); updated.PendingApplicationCount = 4;
            _net.Reply(new GetPlayerGuildResponse { Guild = updated });
            Assert.That(_client.Info.PendingApplicationCount, Is.EqualTo(4u));
        }
        /// <summary>9 个新错误码都要有人话；落到默认分支就会显示“暂未完成请求（码号）”。</summary>
        [TestCase((uint)guild_error.KGuildZoneMerging)]
        [TestCase((uint)guild_error.KGuildTargetNotMember)]
        [TestCase((uint)guild_error.KGuildCannotTargetSelf)]
        [TestCase((uint)guild_error.KGuildRankTooLow)]
        [TestCase((uint)guild_error.KGuildOfficerLimit)]
        [TestCase((uint)guild_error.KGuildApplicationNotFound)]
        [TestCase((uint)guild_error.KGuildApplicationLimit)]
        [TestCase((uint)guild_error.KGuildApplicationQueueFull)]
        [TestCase((uint)guild_error.KGuildBusyRetry)]
        public void NewTipsHaveReadableText(uint tip)
        {
            Load();
            _client.SetMemberRole(2, GuildRoles.Officer);
            _net.Reply(new SetGuildMemberRoleResponse { ErrorMessage = new TipInfoMessage { Id = tip } });
            Assert.That(_client.Status, Does.Not.Contain("暂未完成请求"));
            // 业务 tip 走的是 success 回调，连接完好，不能把帮会功能隔离掉。
            Assert.That(_client.RequiresReconnect, Is.False);
        }
        [Test] public void JoinedThenLeftReloadsMyApplications()
        {
            Empty();
            Assert.That(_client.MyApplicationsQueued, Is.True);
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.ListMyGuildApplications));
            var listed = new ListMyGuildApplicationsResponse();
            listed.Applications.Add(new GuildApplicationView { GuildId = 555, GuildName = "清风明月" });
            _net.Reply(listed);
            Assert.That(_client.MyApplications, Is.Not.Null);
            Assert.That(_client.MyApplications.Count, Is.EqualTo(1));
            Assert.That(_client.MyApplicationsQueued, Is.False);
            Assert.That(_client.HasApplied(555), Is.True);
            Assert.That(_client.HasApplied(556), Is.False);
            // 入帮成功：服务端已删光本人全部申请，本地列表必须作废（不是清成空列表）。
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            Assert.That(_client.MyApplications, Is.Null);
            // 退帮 / 被踢之后重新回到未入帮，列表要再排一次队重拉。
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } });
            Assert.That(_client.MyApplicationsQueued, Is.True);
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.ListMyGuildApplications));
        }
        [Test] public void KickNoticeSurvivesRefresh()
        {
            Load();
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 555, Kind = GuildChangeKind.MemberKicked, TargetPlayerId = 1 });
            _client.DrainQueued(false);
            _net.Reply(new GetPlayerGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } });
            Assert.That(_client.Status, Does.Contain("请离"));
            // 提示只用一次：再刷新一次就该回到普通的未入帮文案，不能一直顶着“你已被请离”。
            _client.Refresh();
            _net.Reply(new GetPlayerGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } });
            Assert.That(_client.Status, Is.EqualTo("尚未加入帮会，和同道相聚于此。"));
        }

        private void Empty() { _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } }); }
        private void Load() { _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = Fixture() }); }
        public static GuildInfo Fixture()
        {
            var info = new GuildInfo { GuildId = 555, Name = "清风明月", LeaderId = 1, Level = 5, MaxMembers = 50,
                Announcement = "同道相聚，月满灯明。", ZoneId = 1, OfficerCount = 0, MaxOfficers = 2 };
            for (ulong i = 1; i <= 7; i++) info.Members.Add(new GuildMember { PlayerId = i, Role = i == 1 ? 3u : 0u,
                ContributionTotal = 100 + i, Online = i % 2 == 1 });
            return info;
        }
    }

    public sealed class GuildWindowTests
    {
        private GameObject _root;
        private GuildWindow _window;
        private GuildClient _client;
        private GuildFakeTransport _net;
        [SetUp] public void SetUp()
        {
            _root = new GameObject("GuildWindowTest", typeof(RectTransform));
            var design = QdaoUguiFactory.CreateCenteredRect("Design", _root.transform, 2560, 1080);
            _window = new GuildWindow(design); _net = new GuildFakeTransport(); _client = new GuildClient(_net);
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = GuildClientTests.Fixture() });
            _window.SetClient(_client);
        }
        [TearDown] public void TearDown() { _window.Hide(); _client.Dispose(); UnityEngine.Object.DestroyImmediate(_root); }
        [TestCase(GuildPage.Donate)] [TestCase(GuildPage.Activities)] [TestCase(GuildPage.Shop)]
        public void UnsupportedActionsAreClearlyDisabled(GuildPage page)
        {
            _window.Show(page);
            var locked = Buttons().Where(b => b.name.StartsWith("GuildUnavailable_")).ToArray();
            Assert.That(locked.Length, Is.EqualTo(3)); Assert.That(locked.All(b => !b.interactable), Is.True);
            Assert.That(ActiveText(), Does.Contain("暂未开放"));
        }
        [Test] public void OverviewStatisticsUpdateOnlyFromTheGuildSnapshot()
        {
            _window.Show();
            string Value(string name) => _root.GetComponentsInChildren<TMP_Text>().Single(t => t.name == name).text;
            Assert.That(Value("GuildMemberCount"), Is.EqualTo("7 / 50"));
            Assert.That(Value("GuildOnlineCount"), Is.EqualTo("4 位"));
            Assert.That(Value("GuildMyContribution"), Is.EqualTo("101"));
            var updated = GuildClientTests.Fixture();
            updated.Members.RemoveAt(6);
            updated.Members[0].ContributionTotal = 987;
            updated.Members[1].Online = true;
            _client.Refresh();
            Assert.That(Value("GuildMyContribution"), Is.EqualTo("101"));
            _net.Reply(new GetPlayerGuildResponse { Guild = updated });
            _window.SetClient(_client);
            Assert.That(Value("GuildMemberCount"), Is.EqualTo("6 / 50"));
            Assert.That(Value("GuildOnlineCount"), Is.EqualTo("4 位"));
            Assert.That(Value("GuildMyContribution"), Is.EqualTo("987"));
        }
        [Test] public void MemberPaginationAndOnlineFilterConsumeTheRealSnapshot()
        {
            _window.Show(GuildPage.Members);
            Assert.That(ActiveText(), Does.Contain("道友 · 1")); Assert.That(ActiveText(), Does.Not.Contain("道友 · 7"));
            Click("MembersNext"); Assert.That(ActiveText(), Does.Contain("道友 · 7"));
            Click("OnlineGuildMembers"); Assert.That(ActiveText(), Does.Not.Contain("道友 · 2"));
            Assert.That(ActiveText(), Does.Contain("道友 · 7"));
        }
        [Test] public void EscapeBackClosesConfirmationBeforeClosingTheWindow()
        {
            _window.Show(); Click("LeaveOrDisbandGuild");
            Assert.That(_window.ModalVisible, Is.True);
            _window.Back(); Assert.That(_window.ModalVisible, Is.False); Assert.That(_window.IsVisible, Is.True);
            _window.Back(); Assert.That(_window.IsVisible, Is.False);
        }
        [Test] public void DestructiveActionRequiresAnExplicitConfirmation()
        {
            int calls = 0; _window.DisbandRequested += () => calls++;
            _window.Show(); Click("LeaveOrDisbandGuild");
            Assert.That(calls, Is.Zero);
            Click("ConfirmGuildAction"); Assert.That(calls, Is.EqualTo(1));
            Assert.That(_client.Info, Is.Not.Null);
        }
        [Test] public void SessionResetClearsMembersAndClosesModal()
        {
            _window.Show(); Click("EditGuildAnnouncement"); _window.ResetSession();
            Assert.That(_window.ModalVisible, Is.False); Assert.That(_window.IsVisible, Is.False);
        }
        [Test] public void RecoveryDisablesRequestsButKeepsReadOnlyNavigationAvailable()
        {
            _client.Refresh(); _net.Error("rpc timeout"); _window.SetClient(_client);
            _window.Show();
            Assert.That(Buttons().Single(b => b.name == "RefreshGuild").interactable, Is.False);
            Assert.That(Buttons().Single(b => b.name == "EditGuildAnnouncement").interactable, Is.False);
            Assert.That(Buttons().Single(b => b.name == "ReadGuildAnnouncement").interactable, Is.True);
            Assert.That(ActiveText(), Does.Contain("重新登录"));
            _window.Show(GuildPage.Members);
            Assert.That(Buttons().Single(b => b.name == "MembersNext").interactable, Is.True);
        }
        [Test] public void ModalDisablesBackgroundControlsAndRestoresThemOnBack()
        {
            _window.Show(); Click("LeaveOrDisbandGuild");
            var refresh = Buttons().Single(b => b.name == "RefreshGuild");
            Assert.That(refresh.IsInteractable(), Is.False);
            _window.Back(); Assert.That(refresh.IsInteractable(), Is.True);
        }
        [Test] public void GuildArtImportsAsRealSpritesWithTheIntendedInputBorder()
        {
            foreach (string key in new[] { "crest", "lantern", "furnace", "sword", "pill", "talisman", "scroll", "notice", "stat_field",
                "window_frame", "title_plate", "button_primary", "button_secondary", "close_button", "close_tassel",
                "round_badge_lotus", "round_badge_compass", "round_badge_pagoda" })
                Assert.That(Resources.Load<Sprite>("UI/Ugui/GuildV2/" + key), Is.Not.Null, key);
            Assert.That(Resources.Load<Sprite>("UI/Ugui/GuildV2/stat_field").border, Is.EqualTo(new Vector4(12, 12, 12, 12)));
            _window.Show();
            var crest = _root.GetComponentsInChildren<Image>().Single(i => i.name == "GuildCrest");
            Assert.That(crest.sprite, Is.SameAs(Resources.Load<Sprite>("UI/Ugui/GuildV2/crest")));
            var images = _root.GetComponentsInChildren<Image>();
            Assert.That(images.Single(i => i.name == "title_plate").preserveAspect, Is.True);
            Assert.That(images.Single(i => i.name == "close_tassel").raycastTarget, Is.False);
            Assert.That(images.Single(i => i.name == "window_frame").sprite.border, Is.EqualTo(new Vector4(97, 74, 97, 99)));
        }
        [Test] public void ServerStringsAreNotRichText()
        {
            _window.Show(GuildPage.Members);
            Assert.That(_root.GetComponentsInChildren<TMP_Text>(true).All(t => !t.richText), Is.True);
        }

        // ── 成员管理与申请审批（B2c）────────────────────────────────────

        /// <summary>帮主对别人有任免 / 转让 / 请离三槽，对自己一个都不能有。</summary>
        [Test] public void LeaderSeesActionsButNotOnSelf()
        {
            _window.Show(GuildPage.Members);
            var names = Buttons().Select(b => b.name).ToArray();
            Assert.That(names.Contains("GuildMemberPromote_2"), Is.True);
            Assert.That(names.Contains("GuildMemberTransfer_2"), Is.True);
            Assert.That(names.Contains("GuildMemberKick_2"), Is.True);
            Assert.That(names.Contains("GuildMemberPromote_1"), Is.False);
            Assert.That(names.Contains("GuildMemberTransfer_1"), Is.False);
            Assert.That(names.Contains("GuildMemberKick_1"), Is.False);
        }
        /// <summary>长老只剩“请离帮众”一槽：请离不了帮主，也没有任免与转让。</summary>
        [Test] public void OfficerOnlySeesKickForMembers()
        {
            var info = GuildClientTests.Fixture();
            info.LeaderId = 2; info.Members[0].Role = GuildRoles.Officer; info.Members[1].Role = GuildRoles.Leader;
            info.OfficerCount = 1;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = info });
            _window.SetClient(_client); _window.Show(GuildPage.Members);
            var names = Buttons().Select(b => b.name).ToArray();
            Assert.That(names.Contains("GuildApplicationsToggle"), Is.True);
            Assert.That(names.Any(n => n.StartsWith("GuildMemberPromote_")), Is.False);
            Assert.That(names.Any(n => n.StartsWith("GuildMemberDemote_")), Is.False);
            Assert.That(names.Any(n => n.StartsWith("GuildMemberTransfer_")), Is.False);
            Assert.That(names.Contains("GuildMemberKick_3"), Is.True);
            Assert.That(names.Contains("GuildMemberKick_2"), Is.False);
            Assert.That(ActiveText(), Does.Contain("长老可请离帮众"));
        }
        /// <summary>长老满员时“任长老”要点不动，并给出为什么点不动。</summary>
        [Test] public void PromoteDisabledAtOfficerCap()
        {
            var info = GuildClientTests.Fixture();
            info.Members[1].Role = GuildRoles.Officer; info.Members[2].Role = GuildRoles.Officer;
            info.OfficerCount = 2; info.MaxOfficers = 2;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = info });
            _window.SetClient(_client); _window.Show(GuildPage.Members);
            Assert.That(Buttons().Single(b => b.name == "GuildMemberDemote_2").interactable, Is.True);
            Assert.That(Buttons().Single(b => b.name == "GuildMemberPromote_4").interactable, Is.False);
            Assert.That(ActiveText(), Does.Contain("长老已满（2/2）"));
        }
        [Test] public void KickRequiresConfirmation()
        {
            ulong kicked = 0;
            _window.KickRequested += id => kicked = id;
            _window.Show(GuildPage.Members);
            Click("GuildMemberKick_2");
            Assert.That(_window.ModalVisible, Is.True);
            Assert.That(kicked, Is.Zero);
            Assert.That(ActiveText(), Does.Contain("道友 · 2 将被请离帮会。"));
            Click("ConfirmGuildAction");
            Assert.That(kicked, Is.EqualTo(2ul));
        }
        [Test] public void ApplicationsToggleOnlyForOfficersAndRequestsList()
        {
            int requests = 0; _window.ApplicationsRequested += () => requests++;
            var plain = GuildClientTests.Fixture();
            plain.LeaderId = 2; plain.Members[0].Role = GuildRoles.Member; plain.Members[1].Role = GuildRoles.Leader;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = plain });
            _window.SetClient(_client); _window.Show(GuildPage.Members);
            Assert.That(Buttons().Any(b => b.name == "GuildApplicationsToggle"), Is.False);
            Assert.That(ActiveText(), Does.Contain("帮众可查看同道名册"));

            var leader = GuildClientTests.Fixture(); leader.PendingApplicationCount = 3;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = leader });
            _window.SetClient(_client); _window.Show(GuildPage.Members);
            Assert.That(ActiveText(), Does.Contain("入帮申请 3"));
            Assert.That(_window.ShowingApplications, Is.False);
            Click("GuildApplicationsToggle");
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(_window.ShowingApplications, Is.True);
            Assert.That(ActiveText(), Does.Contain("正在读取入帮申请…"));
            // 切回成员列表不该再拉一次：关着的视图不占那唯一的在途请求位。
            Click("GuildApplicationsToggle");
            Assert.That(_window.ShowingApplications, Is.False);
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(ActiveText(), Does.Contain("道友 · 1"));
        }
        [Test] public void ApplicationRowsDriveReview()
        {
            ulong reviewed = 0; bool approved = false;
            _window.ApplicationsRequested += () => _client.LoadApplications();
            _window.ReviewRequested += (id, approve) => { reviewed = id; approved = approve; };
            var info = GuildClientTests.Fixture(); info.PendingApplicationCount = 1;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = info });
            _window.SetClient(_client); _window.Show(GuildPage.Members);
            Click("GuildApplicationsToggle");
            var listed = new ListGuildApplicationsResponse();
            listed.Applicants.Add(new GuildApplicantView { PlayerId = 20001, Online = true,
                ApplyMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ExpireMs = (ulong)DateTimeOffset.UtcNow.AddHours(48).ToUnixTimeMilliseconds() });
            _net.Reply(listed);
            _window.SetClient(_client);
            Assert.That(ActiveText(), Does.Contain("道友 · 20001"));
            Assert.That(ActiveText(), Does.Contain("剩余 48 小时"));
            // 审批可反复进行（拒绝后对方还能再申请），所以不加确认框。
            Click("GuildApplicationApprove_20001");
            Assert.That(_window.ModalVisible, Is.False);
            Assert.That(reviewed, Is.EqualTo(20001ul));
            Assert.That(approved, Is.True);
            Click("GuildApplicationReject_20001");
            Assert.That(reviewed, Is.EqualTo(20001ul));
            Assert.That(approved, Is.False);
        }
        [Test] public void RankingShowsApplyAndCancel()
        {
            ulong applied = 0, cancelled = 0;
            _window.ApplyRequested += id => applied = id;
            _window.CancelApplicationRequested += id => cancelled = id;
            _client.Reset();
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } });
            _client.Browse();
            var ranks = new GetGuildRankResponse { Page = 1, PageSize = 5, TotalCount = 2 };
            ranks.Entries.Add(new GuildRankEntry { GuildId = 88001, Name = "清风明月", LeaderId = 10001, Level = 5, MemberCount = 32, Score = 88600, Rank = 1 });
            ranks.Entries.Add(new GuildRankEntry { GuildId = 88002, Name = "云水同心", LeaderId = 10002, Level = 4, MemberCount = 29, Score = 84400, Rank = 2 });
            _net.Reply(ranks);
            _window.SetClient(_client); _window.Show(GuildPage.Ranking);
            Assert.That(Label("ApplyGuild_88001"), Is.EqualTo("申请"));
            Click("ApplyGuild_88001");
            Assert.That(ActiveText(), Does.Contain("确认申请加入"));
            Assert.That(applied, Is.Zero);
            Click("ConfirmGuildAction");
            Assert.That(applied, Is.EqualTo(88001ul));

            // 本人申请列表到货后，已申请的那一行要换成“撤回申请”。
            _client.LoadMyApplications();
            var mine = new ListMyGuildApplicationsResponse();
            mine.Applications.Add(new GuildApplicationView { GuildId = 88002, GuildName = "云水同心" });
            _net.Reply(mine);
            _window.SetClient(_client);
            Assert.That(Label("ApplyGuild_88002"), Is.EqualTo("撤回申请"));
            Assert.That(Label("ApplyGuild_88001"), Is.EqualTo("申请"));
            Click("ApplyGuild_88002");
            Assert.That(ActiveText(), Does.Contain("确认撤回申请"));
            Click("ConfirmGuildAction");
            Assert.That(cancelled, Is.EqualTo(88002ul));
        }

        private Button[] Buttons() => _root.GetComponentsInChildren<Button>().Where(b => b.gameObject.activeInHierarchy).ToArray();
        private void Click(string name) => Buttons().Single(b => b.name == name).onClick.Invoke();
        private string Label(string name) => Buttons().Single(b => b.name == name).GetComponentInChildren<TMP_Text>().text;
        private string ActiveText() => string.Join("\n", _root.GetComponentsInChildren<TMP_Text>()
            .Where(t => t.gameObject.activeInHierarchy).Select(t => t.text));
    }

    internal sealed class GuildFakeTransport : IBattleTransport
    {
        public ulong PlayerId { get; set; } = 1;
        public bool IsReady { get; set; } = true;
        public event Action Disconnected;
        public List<uint> Calls { get; } = new();
        public Action<IMessage> Pending;
        public Action<string> Error;
        /// <summary>已注册的 S2C 推送处理器；与 GameClient.OnNotify 一样，一个 id 只留最后一次注册。</summary>
        public readonly Dictionary<uint, Action<MessageContent>> Notifies = new();
        public void Reply(IMessage response) => Pending(response);
        public void Disconnect() { IsReady = false; Disconnected?.Invoke(); }
        /// <summary>模拟服务端推送；未注册的 id 会直接抛，测试里那就是接线漏了。</summary>
        public void Push(uint id, IMessage message) =>
            Notifies[id](new MessageContent { MessageId = id, SerializedMessage = message.ToByteString() });
        public void RegisterNotify(uint id, Action<MessageContent> action) => Notifies[id] = action;
        public void SendOneWay(uint id, IMessage request) { }
        public void Call<T>(uint id, IMessage request, MessageParser<T> parser, Action<T> success, Action<string> error)
            where T : IMessage<T> { Calls.Add(id); Pending = message => success((T)message); Error = error; }
    }
}
