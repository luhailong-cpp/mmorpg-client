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

        // ── 经济：捐献 / 升级 / 商店（B5c）──────────────────────────────

        [Test] public void DonateSendsOptionIdAndAppliesReturnedGuild()
        {
            Load(); int assets = 0; _client.AssetsChanged += () => assets++;
            _client.Donate(2);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.DonateToGuild));
            Assert.That(((DonateToGuildRequest)_net.Requests.Last()).DonateId, Is.EqualTo(2u));
            var funded = Fixture(); funded.Funds = 12000;
            _net.Reply(new DonateToGuildResponse { Guild = funded, Donation = new GuildDonationView
                { OpId = 11, DonateId = 2, Status = GuildAssetOrderStatus.Applied, ContributionGain = 120, FundsGain = 12000 } });
            Assert.That(_client.Info.Funds, Is.EqualTo(12000ul));
            Assert.That(assets, Is.EqualTo(1));
            // 次数与待结算列表都在捐献页快照里:成功后紧跟一发重拉,结果文案要活过这次重拉。
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildDonateOptions));
            _net.Reply(DonateFixture());
            Assert.That(_client.Status, Is.EqualTo("捐献成功：帮贡 +120，帮会资金 +12,000"));
            Assert.That(_client.Donations, Is.Not.Null);
        }
        [Test] public void PendingDonationIsNotAnError()
        {
            Load(); int assets = 0; _client.AssetsChanged += () => assets++;
            _client.Donate(1);
            _net.Reply(new DonateToGuildResponse { Guild = Fixture(), Donation = new GuildDonationView
                { OpId = 12, DonateId = 1, Status = GuildAssetOrderStatus.Pending, ReasonTipId = GuildAssetReasons.InBattle } });
            var options = DonateFixture();
            options.PendingDonations.Add(new GuildDonationView { OpId = 12, DonateId = 1, Status = GuildAssetOrderStatus.Pending,
                ReasonTipId = GuildAssetReasons.InBattle });
            _net.Reply(options);
            Assert.That(_client.Status, Does.Contain("战斗中"));
            // 结算中不是失败:帮会快照不清、不隔离、不让背包重拉(钱还没真扣)。
            Assert.That(_client.Info, Is.Not.Null);
            Assert.That(_client.RequiresReconnect, Is.False);
            Assert.That(assets, Is.Zero);
        }
        [Test] public void RejectedDonationShowsCurrencyText()
        {
            Load(); _client.Donate(3); int before = _net.Calls.Count;
            _net.Reply(new DonateToGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildCurrencyInsufficient },
                Guild = Fixture(), Donation = new GuildDonationView { OpId = 13, DonateId = 3, Status = GuildAssetOrderStatus.Rejected,
                    ReasonTipId = GuildAssetReasons.CurrencyInsufficient } });
            Assert.That(_client.Status, Is.EqualTo("银两或灵石不足，无法捐献。"));
            // 次数已由服务端退回,本地快照不需要跟一发重拉。
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Info, Is.Not.Null);
            Assert.That(_client.RequiresReconnect, Is.False);
        }
        [Test] public void UpgradeIsSentByOfficerEvenWhenCachedFundsLookLow()
        {
            var member = Fixture(); member.LeaderId = 2; member.Members[0].Role = GuildRoles.Member; member.Members[1].Role = GuildRoles.Leader;
            member.UpgradeCostFunds = 20000;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = member });
            Assert.That(_client.CanUpgrade, Is.False);
            _client.Upgrade(5);
            Assert.That(_net.Calls.Contains(MessageIds.UpgradeGuild), Is.False);

            var officer = Fixture(); officer.LeaderId = 2; officer.Members[0].Role = GuildRoles.Officer; officer.Members[1].Role = GuildRoles.Leader;
            officer.OfficerCount = 1; officer.UpgradeCostFunds = 20000; officer.Funds = 100;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = officer });
            // 本地资金看着不够也照发:别的长老可能刚捐过,够不够以服务端事务为准。
            _client.Upgrade(5);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.UpgradeGuild));
            Assert.That(((UpgradeGuildRequest)_net.Requests.Last()).ExpectedLevel, Is.EqualTo(5u));
            var fresher = officer.Clone(); fresher.Funds = 30000;
            _net.Reply(new UpgradeGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildFundsInsufficient }, Guild = fresher });
            Assert.That(_client.Info.Funds, Is.EqualTo(30000ul));
            Assert.That(_client.Status, Is.EqualTo("帮会资金不足，暂时无法升级。"));

            _client.Upgrade(5);
            var upgraded = officer.Clone(); upgraded.Level = 6; upgraded.Funds = 10000;
            _net.Reply(new UpgradeGuildResponse { Guild = upgraded });
            Assert.That(_client.Info.Level, Is.EqualTo(6u));
            Assert.That(_client.Status, Is.EqualTo("帮会已升至 Lv.6"));
        }
        [Test] public void FundsChangedPushQueuesDonationRefreshAndRaisesAssetsChanged()
        {
            Load(); _client.RefreshDonations(); _net.Reply(DonateFixture());
            int assets = 0; _client.AssetsChanged += () => assets++;
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 555, Kind = GuildChangeKind.FundsChanged, TargetPlayerId = 1 });
            Assert.That(assets, Is.EqualTo(1));
            Assert.That(_client.DonationsQueued, Is.True);
            Assert.That(_client.RefreshQueued, Is.True);
            // 先落帮会快照(资金 / 帮贡),再重拉捐献页:后到的"已入账"文案才不会被"帮会信息已更新"盖掉。
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
            _net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildDonateOptions));
            Assert.That(_client.DonationsQueued, Is.False);
        }
        [Test] public void DeliveryDonePushWhileNotInGuildRaisesAssetsChanged()
        {
            Empty(); int assets = 0; _client.AssetsChanged += () => assets++;
            // 已离帮后才发放 / 结算的那一笔:背包要重拉,但没有帮会页可刷。
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 555, Kind = GuildChangeKind.DeliveryDone, TargetPlayerId = 1 });
            Assert.That(assets, Is.EqualTo(1));
            Assert.That(_client.RefreshQueued, Is.False);
            Assert.That(_client.ShopQueued, Is.False);
            Assert.That(_client.DonationsQueued, Is.False);
        }
        [Test] public void SettledDonationShowsRecentResult()
        {
            Load();
            _client.RefreshDonations();
            var first = DonateFixture();
            first.PendingDonations.Add(new GuildDonationView { OpId = 11, DonateId = 3, Status = GuildAssetOrderStatus.Pending });
            _net.Reply(first);
            Assert.That(_client.Status, Does.Contain("1 笔捐献结算中"));
            int assets = 0; _client.AssetsChanged += () => assets++;
            _client.RefreshDonations();
            var second = DonateFixture();
            second.RecentResults.Add(new GuildDonationView { OpId = 11, DonateId = 3, Status = GuildAssetOrderStatus.Rejected,
                ReasonTipId = GuildAssetReasons.CurrencyInsufficient });
            _net.Reply(second);
            Assert.That(_client.Status, Does.Contain("捐献未成功"));
            Assert.That(_client.Status, Does.Contain("不足"));
            Assert.That(assets, Is.EqualTo(1));
        }
        [Test] public void SettledShopOrderRaisesAssetsChanged()
        {
            Load();
            _client.RefreshShop();
            var first = ShopFixture(440);
            first.PendingOrders.Add(new GuildShopOrderView { OpId = 21, GoodsId = 101, Count = 1, Status = GuildAssetOrderStatus.Pending,
                ReasonTipId = GuildAssetReasons.BagFull });
            _net.Reply(first);
            Assert.That(_client.Status, Does.Contain("待发放"));
            int assets = 0; _client.AssetsChanged += () => assets++;
            _client.RefreshShop();
            var second = ShopFixture(440);
            second.RecentOrders.Add(new GuildShopOrderView { OpId = 21, GoodsId = 101, Count = 1, Status = GuildAssetOrderStatus.Applied });
            _net.Reply(second);
            Assert.That(_client.Status, Is.EqualTo("兑换的物品已发放，请查看背包。"));
            Assert.That(assets, Is.EqualTo(1));
        }
        [Test] public void BuyAppliedUpdatesMyBalanceAndReloadsTheShop()
        {
            var info = Fixture(); info.Members[0].ContributionBalance = 440;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = info });
            int assets = 0; _client.AssetsChanged += () => assets++;
            _client.Buy(101);
            var request = (BuyGuildShopGoodsRequest)_net.Requests.Last();
            Assert.That(request.GoodsId, Is.EqualTo(101u)); Assert.That(request.Count, Is.EqualTo(1u));
            _net.Reply(new BuyGuildShopGoodsResponse { ContributionBalance = 410, Order = new GuildShopOrderView
                { OpId = 31, GoodsId = 101, Count = 1, Status = GuildAssetOrderStatus.Applied, CostContribution = 30 } });
            // 兑换回包不带帮会快照:总览里的可用帮贡按回包的权威余额就地更新。
            Assert.That(_client.Info.Members[0].ContributionBalance, Is.EqualTo(410ul));
            Assert.That(assets, Is.EqualTo(1));
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildShop));
            _net.Reply(ShopFixture(410));
            Assert.That(_client.Status, Is.EqualTo("兑换成功，物品已放入背包"));
        }
        [Test] public void GuildChangeClearsDonationAndShopSnapshots()
        {
            Load();
            _client.RefreshDonations(); _net.Reply(DonateFixture());
            _client.RefreshShop(); _net.Reply(ShopFixture(0));
            Assert.That(_client.Donations, Is.Not.Null); Assert.That(_client.Shop, Is.Not.Null);
            // 换帮:上一个帮会的次数、帮贡、待结算都不能带进新帮会。
            var other = Fixture(); other.GuildId = 556;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = other });
            Assert.That(_client.Donations, Is.Null); Assert.That(_client.Shop, Is.Null);
            // 离帮同理。
            _client.RefreshShop(); _net.Reply(ShopFixture(0));
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } });
            Assert.That(_client.Shop, Is.Null);
        }
        /// <summary>经济的 10 个码与发号失败都要有人话;落到默认分支就会显示“暂未完成请求（码号）”。</summary>
        [TestCase((uint)guild_error.KGuildFundsInsufficient)]
        [TestCase((uint)guild_error.KGuildMaxLevel)]
        [TestCase((uint)guild_error.KGuildDonateLimit)]
        [TestCase((uint)guild_error.KGuildCurrencyInsufficient)]
        [TestCase((uint)guild_error.KGuildAssetPending)]
        [TestCase((uint)guild_error.KGuildAssetRejected)]
        [TestCase((uint)guild_error.KGuildShopGoodsNotFound)]
        [TestCase((uint)guild_error.KGuildShopLevelTooLow)]
        [TestCase((uint)guild_error.KGuildShopLimit)]
        [TestCase((uint)guild_error.KGuildContributionInsufficient)]
        [TestCase((uint)guild_error.KGuildIdGenUnavailable)]
        public void EconomyTipsHaveReadableText(uint tip)
        {
            Load();
            _client.Buy(101);
            _net.Reply(new BuyGuildShopGoodsResponse { ErrorMessage = new TipInfoMessage { Id = tip } });
            Assert.That(_client.Status, Does.Not.Contain("暂未完成请求"));
            Assert.That(_client.RequiresReconnect, Is.False);
        }
        /// <summary>
        /// 资产通道关闭 / 未决指令过多时回 kGuildAssetPending,且没有新的待结算单。本页要重拉让待结算列表收敛,
        /// 但重拉的默认文案是"捐献信息已更新 / 帮会商店已更新",像是成功了 —— 拒绝原因必须活过这次重拉。
        /// </summary>
        [Test] public void AssetPendingRejectionSurvivesThePageReload()
        {
            Load();
            _client.RefreshDonations(); _net.Reply(DonateFixture());
            _client.Donate(1);
            _net.Reply(new DonateToGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildAssetPending } });
            // 回调里带着文案直接重拉,不经 DrainQueued(那一发不带文案)。
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildDonateOptions));
            Assert.That(_client.DonationsQueued, Is.False);
            _net.Reply(DonateFixture());
            Assert.That(_client.Status, Is.EqualTo("还有未结算的帮会操作，请稍后再试。"));

            _client.RefreshShop(); _net.Reply(ShopFixture(440));
            _client.Buy(101);
            _net.Reply(new BuyGuildShopGoodsResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildAssetPending } });
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildShop));
            Assert.That(_client.ShopQueued, Is.False);
            _net.Reply(ShopFixture(440));
            Assert.That(_client.Status, Is.EqualTo("还有未结算的帮会操作，请稍后再试。"));
        }
        /// <summary>
        /// 确认框在 Lv.5 打开,确认前别的长老已升到 Lv.6、推送把本地快照刷新了:按 Lv.5 确认的请求不能
        /// 改按 Lv.6 发 —— 那会按 6→7 的花费再扣一次全帮资金、连升两级。
        /// </summary>
        [Test] public void UpgradeConfirmedAtAnOlderLevelIsNotSent()
        {
            var info = Fixture(); info.UpgradeCostFunds = 20000; info.Funds = 100000;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = info });
            var upgraded = info.Clone(); upgraded.Level = 6; upgraded.UpgradeCostFunds = 50000;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = upgraded });
            int before = _net.Calls.Count;
            _client.Upgrade(5);
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Status, Is.EqualTo("帮会等级已变化，请重新确认升级。"));
            // 按新等级重新确认后照常发出。
            _client.Upgrade(6);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.UpgradeGuild));
            Assert.That(((UpgradeGuildRequest)_net.Requests.Last()).ExpectedLevel, Is.EqualTo(6u));
        }
        /// <summary>
        /// 同一个帮会里等级或本人帮贡变了,两页快照不清空、只标过时(进页时由窗口重拉);
        /// 本页重拉成功即不再过时。等级、帮贡都没变的帮会快照(比如有人入帮)不影响它们。
        /// </summary>
        [Test] public void LevelOrBalanceChangeMarksEconomySnapshotsStale()
        {
            Load();
            _client.RefreshDonations(); _net.Reply(DonateFixture());
            _client.RefreshShop(); _net.Reply(ShopFixture(0));
            Assert.That(_client.DonationsNeedReload(0), Is.False); Assert.That(_client.ShopNeedsReload(0), Is.False);
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            Assert.That(_client.DonationsNeedReload(0), Is.False); Assert.That(_client.ShopNeedsReload(0), Is.False);

            // 捐献入账:捐献页随即重拉,商店页的可用帮贡还是旧的。
            _client.Donate(1);
            var donated = Fixture(); donated.Members[0].ContributionBalance = 10;
            _net.Reply(new DonateToGuildResponse { Guild = donated, Donation = new GuildDonationView
                { OpId = 41, DonateId = 1, Status = GuildAssetOrderStatus.Applied, ContributionGain = 10, FundsGain = 1000 } });
            _net.Reply(DonateFixture());
            Assert.That(_client.DonationsNeedReload(0), Is.False);
            Assert.That(_client.ShopNeedsReload(0), Is.True);
            Assert.That(_client.Shop, Is.Not.Null);
            _client.RefreshShop(); _net.Reply(ShopFixture(10));
            Assert.That(_client.ShopNeedsReload(0), Is.False);

            // 兑换成功:捐献页页脚的可用帮贡过时。
            _client.Buy(101);
            _net.Reply(new BuyGuildShopGoodsResponse { ContributionBalance = 0, Order = new GuildShopOrderView
                { OpId = 42, GoodsId = 101, Count = 1, Status = GuildAssetOrderStatus.Applied, CostContribution = 10 } });
            _net.Reply(ShopFixture(0));
            Assert.That(_client.DonationsNeedReload(0), Is.True);
            Assert.That(_client.ShopNeedsReload(0), Is.False);

            // 升级(本人升级回包,或别人升级的推送 → Refresh):unlocked 是服务端按拉取那一刻的等级算的,两页都过时。
            _client.RefreshDonations(); _net.Reply(DonateFixture());
            var leveled = Fixture(); leveled.Level = 6;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = leveled });
            Assert.That(_client.DonationsNeedReload(0), Is.True); Assert.That(_client.ShopNeedsReload(0), Is.True);
        }
        /// <summary>客户端开着跨过服务端给的日切点(商店另有周切点):"今日 2/2" 之类的用量已不可信。</summary>
        [Test] public void EconomySnapshotsExpireAtServerResetTimes()
        {
            Load();
            var donations = DonateFixture(); donations.NextDailyResetMs = 1000;
            _client.RefreshDonations(); _net.Reply(donations);
            Assert.That(_client.DonationsNeedReload(999), Is.False);
            Assert.That(_client.DonationsNeedReload(1000), Is.True);
            var shop = ShopFixture(0); shop.NextDailyResetMs = 2000; shop.NextWeeklyResetMs = 5000;
            _client.RefreshShop(); _net.Reply(shop);
            Assert.That(_client.ShopNeedsReload(1999), Is.False);
            Assert.That(_client.ShopNeedsReload(2000), Is.True);
            // 没给切点(0)的不按时间过期。
            _client.RefreshShop(); _net.Reply(ShopFixture(0));
            Assert.That(_client.ShopNeedsReload(ulong.MaxValue), Is.False);
        }
        /// <summary>
        /// 断线重连后 _pendingShopIds 已清空,重拉算不出"刚结算完";商店页脚又只写得下结论。没有待发放时,
        /// 状态栏要写最近一单的完整结果(含拒绝原因),进页自动拉取也看得到上一单(05 §5.32 W14)。
        /// 余额不足而未落盘的结算中捐献,状态栏说"正在确认",不说成已失败、也不许诺入账。
        /// </summary>
        [Test] public void EconomyReloadStatusCarriesTheLatestResultAndPendingReason()
        {
            Load();
            _client.RefreshShop(); _net.Reply(ShopFixture(440));
            Assert.That(_client.Status, Is.EqualTo("帮会商店已更新"));
            _net.Disconnect(); _net.IsReady = true; Load();
            var shop = ShopFixture(440);
            shop.RecentOrders.Add(new GuildShopOrderView { OpId = 21, GoodsId = 203, Count = 1, Status = GuildAssetOrderStatus.Rejected,
                ReasonTipId = GuildAssetReasons.Blocked });
            _client.RefreshShop(); _net.Reply(shop);
            Assert.That(_client.Status, Is.EqualTo("最近一单：兑换失败，帮贡与限购已退回：该物品或货币暂被限制"));

            var donations = DonateFixture();
            donations.PendingDonations.Add(new GuildDonationView { OpId = 22, DonateId = 2, Status = GuildAssetOrderStatus.Pending,
                ReasonTipId = GuildAssetReasons.CurrencyInsufficient });
            _client.RefreshDonations(); _net.Reply(donations);
            Assert.That(_client.Status, Is.EqualTo("1 笔捐献结算中：余额不足，正在确认结算结果"));
        }

        private void Empty() { _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } }); }
        private void Load() { _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = Fixture() }); }
        /// <summary>与 GuildDonate 默认三行同形;大捐今日已用满,供“次数用尽不可点”断言。</summary>
        public static GetGuildDonateOptionsResponse DonateFixture()
        {
            var response = new GetGuildDonateOptionsResponse { ContributionTotal = 440, ContributionBalance = 440 };
            response.Options.Add(new GuildDonateOptionView { DonateId = 1, Name = "银两小捐", CurrencyType = 0, CostAmount = 10000,
                ContributionGain = 10, FundsGain = 1000, DailyLimit = 5, UsedToday = 0, MinGuildLevel = 1, Unlocked = true });
            response.Options.Add(new GuildDonateOptionView { DonateId = 2, Name = "银两大捐", CurrencyType = 0, CostAmount = 100000,
                ContributionGain = 120, FundsGain = 12000, DailyLimit = 2, UsedToday = 2, MinGuildLevel = 1, Unlocked = true });
            response.Options.Add(new GuildDonateOptionView { DonateId = 3, Name = "灵石捐献", CurrencyType = 1, CostAmount = 100,
                ContributionGain = 200, FundsGain = 20000, DailyLimit = 1, UsedToday = 0, MinGuildLevel = 1, Unlocked = true });
            return response;
        }
        /// <summary>与 GuildShop 默认 11 行同形(帮会 Lv.2:103 已解锁,104 未解锁);301 今日已兑满。</summary>
        public static GetGuildShopResponse ShopFixture(ulong balance)
        {
            var response = new GetGuildShopResponse { ContributionBalance = balance };
            void Add(uint id, string name, uint category, ulong cost, uint level, uint period, uint limit, uint used = 0) =>
                response.Goods.Add(new GuildShopGoodsView { GoodsId = id, Name = name, Category = category, ItemId = id, ItemCount = 1,
                    CostContribution = cost, RequiredGuildLevel = level, Unlocked = level <= 2, LimitPeriod = period, LimitCount = limit,
                    UsedCount = used, MaxBuyCount = 1 });
            Add(101, "培元丹", 1, 30, 1, 1, 10); Add(102, "回灵散", 1, 30, 1, 1, 10);
            Add(103, "精炼石", 1, 80, 2, 1, 5); Add(104, "修行秘录残页", 1, 150, 3, 2, 5);
            Add(201, "帮会令牌", 2, 300, 3, 2, 3); Add(202, "玄铁护符", 2, 800, 4, 2, 1);
            Add(203, "灵兽口粮", 2, 120, 2, 1, 3); Add(204, "藏经阁手札", 2, 1500, 6, 2, 1);
            Add(301, "花灯", 3, 50, 1, 1, 5, 5); Add(302, "月饼礼盒", 3, 100, 1, 2, 7); Add(303, "同心结", 3, 200, 5, 0, 0);
            return response;
        }
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
        // B5c 起捐献 / 商店已开放,只剩活动;B6a-cli 删最后一例时要删整个方法(NUnit 不许带参方法没有用例,90 清单 X-11)。
        [TestCase(GuildPage.Activities)]
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
            // B5 起这一格是“可用 / 累计帮贡”(90 清单 X-12);fixture 的可用帮贡为 0。
            Assert.That(Value("GuildMyContribution"), Is.EqualTo("0 / 101"));
            var updated = GuildClientTests.Fixture();
            updated.Members.RemoveAt(6);
            updated.Members[0].ContributionTotal = 987;
            updated.Members[1].Online = true;
            _client.Refresh();
            Assert.That(Value("GuildMyContribution"), Is.EqualTo("0 / 101"));
            _net.Reply(new GetPlayerGuildResponse { Guild = updated });
            _window.SetClient(_client);
            Assert.That(Value("GuildMemberCount"), Is.EqualTo("6 / 50"));
            Assert.That(Value("GuildOnlineCount"), Is.EqualTo("4 位"));
            Assert.That(Value("GuildMyContribution"), Is.EqualTo("0 / 987"));
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

        // ── 成员显示名与按名字查找（B3b）──────────────────────────────

        /// <summary>服务端填了名字就显示名字：成员行、确认框、申请行共用 MemberDisplayName。</summary>
        [Test] public void MemberDisplayNameUsesServerName()
        {
            Assert.That(GuildWindow.MemberDisplayName(new GuildMember { PlayerId = 2, Name = "云中君" }), Is.EqualTo("云中君"));
            Assert.That(GuildWindow.MemberDisplayName(20001, "青衫客"), Is.EqualTo("青衫客"));

            _window.ApplicationsRequested += () => _client.LoadApplications();
            var info = GuildClientTests.Fixture(); info.Members[1].Name = "云中君"; info.PendingApplicationCount = 1;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = info });
            _window.SetClient(_client); _window.Show(GuildPage.Members);
            Assert.That(ActiveText(), Does.Contain("云中君"));
            Assert.That(ActiveText(), Does.Not.Contain("道友 · 2"));
            Click("GuildMemberKick_2");
            Assert.That(ActiveText(), Does.Contain("云中君 将被请离帮会。"));
            _window.Back();

            Click("GuildApplicationsToggle");
            var listed = new ListGuildApplicationsResponse();
            listed.Applicants.Add(new GuildApplicantView { PlayerId = 20001, Name = "青衫客", Online = true,
                ApplyMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ExpireMs = (ulong)DateTimeOffset.UtcNow.AddHours(48).ToUnixTimeMilliseconds() });
            _net.Reply(listed);
            _window.SetClient(_client);
            Assert.That(ActiveText(), Does.Contain("青衫客"));
            Assert.That(ActiveText(), Does.Not.Contain("道友 · 20001"));
        }
        /// <summary>B3b 之前建的老角色、或服务端取名 fail-open 时 name 为空：回落“道友 · 编号”。</summary>
        [Test] public void MemberDisplayNameFallsBackToIdWhenNameMissing()
        {
            Assert.That(GuildWindow.MemberDisplayName(new GuildMember { PlayerId = 7 }), Is.EqualTo("道友 · 7"));
            Assert.That(GuildWindow.MemberDisplayName(20001, ""), Is.EqualTo("道友 · 20001"));
            Assert.That(GuildWindow.MemberDisplayName(20001, null), Is.EqualTo("道友 · 20001"));
        }
        /// <summary>纯空白（含全角空格）不是名字，不能显示成一行空白。</summary>
        [TestCase(" ")] [TestCase("\u3000")] [TestCase(" \t ")]
        public void MemberDisplayNameFallsBackToIdWhenNameIsBlank(string blank)
        {
            Assert.That(GuildWindow.MemberDisplayName(new GuildMember { PlayerId = 3, Name = blank }), Is.EqualTo("道友 · 3"));
            Assert.That(GuildWindow.MemberDisplayName(3, blank), Is.EqualTo("道友 · 3"));
        }
        /// <summary>
        /// 查找同时匹配名字与编号；名字按服务端 norm 口径（NFKC + 转小写）比较，
        /// 输入框里保留玩家原样输入的文字。
        /// </summary>
        [Test] public void MemberSearchMatchesNameOrId()
        {
            var info = GuildClientTests.Fixture();
            info.Members[1].Name = "云中君"; info.Members[2].Name = "Alice"; info.Members[3].Name = "云游子";
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = info });
            _window.SetClient(_client); _window.Show(GuildPage.Members);
            Assert.That(ActiveText(), Does.Contain("按名字或编号查找"));

            Search("云中");
            Assert.That(ActiveText(), Does.Contain("云中君"));
            Assert.That(ActiveText(), Does.Not.Contain("云游子"));
            Assert.That(ActiveText(), Does.Not.Contain("道友 · 1"));

            Search("云");
            Assert.That(ActiveText(), Does.Contain("云中君"));
            Assert.That(ActiveText(), Does.Contain("云游子"));
            Assert.That(ActiveText(), Does.Not.Contain("Alice"));

            Search("ＡＬＩＣＥ");
            Assert.That(ActiveText(), Does.Contain("Alice"));
            Assert.That(ActiveText(), Does.Not.Contain("云中君"));
            Assert.That(SearchInput().text, Is.EqualTo("ＡＬＩＣＥ"));

            // 编号查找不因为加了名字而失效：5 号没有名字，只能靠编号找到。
            Search("5");
            Assert.That(ActiveText(), Does.Contain("道友 · 5"));
            Assert.That(ActiveText(), Does.Not.Contain("云中君"));
            Assert.That(ActiveText(), Does.Not.Contain("Alice"));
        }

        // ── 经济页（B5c）──────────────────────────────────────────────

        [Test] public void DonatePageRendersOptionsAndKeepsMaterialsDisabled()
        {
            uint donated = 0; _window.DonateRequested += id => donated = id;
            _client.RefreshDonations(); _net.Reply(GuildClientTests.DonateFixture());
            _window.SetClient(_client); _window.Show(GuildPage.Donate);
            var buttons = Buttons();
            Assert.That(buttons.Any(b => b.name == "GuildDonate_1"), Is.True);
            Assert.That(buttons.Any(b => b.name == "GuildDonate_3"), Is.True);
            Assert.That(buttons.Single(b => b.name == "GuildUnavailable_Donate_2").interactable, Is.False);
            // 大捐今日 2/2:次数用尽的那一档点不动。
            Assert.That(buttons.Single(b => b.name == "GuildDonate_2").interactable, Is.False);
            Assert.That(Label("GuildDonate_1"), Is.EqualTo("小捐"));
            Assert.That(Label("GuildDonate_3"), Is.EqualTo("捐献"));
            Assert.That(ActiveText(), Does.Contain("今日 2/2"));
            Click("GuildDonate_3");
            Assert.That(ActiveText(), Does.Contain("消耗 100 灵石"));
            Assert.That(donated, Is.Zero);
            Click("ConfirmGuildAction");
            Assert.That(donated, Is.EqualTo(3u));
        }
        [Test] public void DonatePageAutoRequestsAgainAfterReconnect()
        {
            int requests = 0; _window.DonationsRequested += () => requests++;
            _client.Refresh(); _net.Error("rpc timeout");
            _window.SetClient(_client); _window.Show(GuildPage.Donate);
            // 隔离期间请求发不出去,自动拉取也不该算“拉过了”。
            Assert.That(requests, Is.Zero);
            _net.Disconnect(); _net.IsReady = true;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = GuildClientTests.Fixture() });
            _window.SetClient(_client);
            Assert.That(requests, Is.EqualTo(1));
            // 没拉到快照(本用例没接事件到客户端)也只自动拉一次,之后交给玩家点刷新。
            _window.SetClient(_client);
            Assert.That(requests, Is.EqualTo(1));
        }
        [Test] public void ShopPageFiltersByCategoryAndPages()
        {
            _client.RefreshShop(); _net.Reply(GuildClientTests.ShopFixture(440));
            _window.SetClient(_client); _window.Show(GuildPage.Shop);
            Assert.That(Buttons().Count(b => b.name.StartsWith("GuildShopBuy_")), Is.EqualTo(4));
            Assert.That(Buttons().Single(b => b.name == "ShopNext").interactable, Is.False);
            Assert.That(ActiveText(), Does.Contain("可用帮贡 440"));
            Click("GuildShopCategory_2");
            Assert.That(Buttons().Any(b => b.name == "GuildShopBuy_201"), Is.True);
            Assert.That(Buttons().Any(b => b.name == "GuildShopBuy_101"), Is.False);
        }
        [Test] public void LockedOrUnaffordableGoodsCannotBeBought()
        {
            uint bought = 0; _window.ShopBuyRequested += id => bought = id;
            _client.RefreshShop(); _net.Reply(GuildClientTests.ShopFixture(100));
            _window.SetClient(_client); _window.Show(GuildPage.Shop);
            Assert.That(Buttons().Single(b => b.name == "GuildShopBuy_101").interactable, Is.True);
            Assert.That(Buttons().Single(b => b.name == "GuildShopBuy_104").interactable, Is.False);  // 帮会等级不够
            Assert.That(ActiveText(), Does.Contain("帮会 Lv.3 解锁"));
            Click("GuildShopCategory_2");
            Assert.That(Buttons().Single(b => b.name == "GuildShopBuy_203").interactable, Is.False); // 帮贡不够(120 > 100)
            Click("GuildShopCategory_3");
            Assert.That(Buttons().Single(b => b.name == "GuildShopBuy_301").interactable, Is.False); // 今日已兑满
            Assert.That(ActiveText(), Does.Contain("今日 5/5"));
            Click("GuildShopCategory_1");
            Click("GuildShopBuy_101");
            Assert.That(bought, Is.Zero);
            Click("ConfirmGuildAction");
            Assert.That(bought, Is.EqualTo(101u));
        }
        [Test] public void OverviewShowsFundsAndUpgradeOnlyForOfficers()
        {
            int upgrades = 0; uint confirmedLevel = 0;
            _window.UpgradeRequested += level => { upgrades++; confirmedLevel = level; };
            string Value(string name) => _root.GetComponentsInChildren<TMP_Text>().Single(t => t.name == name).text;
            var member = GuildClientTests.Fixture();
            member.LeaderId = 2; member.Members[0].Role = GuildRoles.Member; member.Members[1].Role = GuildRoles.Leader;
            member.Funds = 12000; member.UpgradeCostFunds = 20000;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = member });
            _window.SetClient(_client); _window.Show();
            Assert.That(Buttons().Any(b => b.name == "UpgradeGuild"), Is.False);
            Assert.That(Value("GuildFunds"), Is.EqualTo("12,000"));

            var officer = member.Clone(); officer.Members[0].Role = GuildRoles.Officer; officer.OfficerCount = 1;
            officer.Members[0].ContributionBalance = 440; officer.Members[0].ContributionTotal = 440;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = officer });
            _window.SetClient(_client);
            // 资金 12,000 < 20,000 也可点:够不够交给服务端判。
            Assert.That(Buttons().Single(b => b.name == "UpgradeGuild").interactable, Is.True);
            Assert.That(Value("GuildMyContribution"), Is.EqualTo("440 / 440"));
            Click("UpgradeGuild");
            Assert.That(ActiveText(), Does.Contain("需要帮会资金 20,000"));
            Click("ConfirmGuildAction");
            Assert.That(upgrades, Is.EqualTo(1));
            // 事件带出的是确认框打开时的等级,作为 expected_level 发出。
            Assert.That(confirmedLevel, Is.EqualTo(5u));

            var max = officer.Clone(); max.UpgradeCostFunds = 0;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = max });
            _window.SetClient(_client);
            Assert.That(Label("UpgradeGuild"), Is.EqualTo("已满级"));
            Assert.That(Buttons().Single(b => b.name == "UpgradeGuild").interactable, Is.False);
        }
        /// <summary>
        /// 确认框写的是"升至 Lv.6";开着时别的长老先升了,推送 → Refresh 把快照刷成 Lv.6,确认框不会因此关掉。
        /// 此时点确认,带出的仍是 Lv.5,客户端拒发 —— 不能按 6→7 的花费替玩家再扣一次。
        /// </summary>
        [Test] public void UpgradeConfirmationKeepsTheLevelItWasOpenedAt()
        {
            uint confirmedLevel = 0;
            _window.UpgradeRequested += level => { confirmedLevel = level; _client.Upgrade(level); };
            var info = GuildClientTests.Fixture(); info.UpgradeCostFunds = 20000; info.Funds = 100000;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = info });
            _window.SetClient(_client); _window.Show();
            Click("UpgradeGuild");
            Assert.That(ActiveText(), Does.Contain("升至 Lv.6"));
            var upgraded = info.Clone(); upgraded.Level = 6; upgraded.UpgradeCostFunds = 50000;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = upgraded });
            _window.SetClient(_client);
            Assert.That(_window.ModalVisible, Is.True);
            int before = _net.Calls.Count;
            Click("ConfirmGuildAction");
            Assert.That(confirmedLevel, Is.EqualTo(5u));
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Status, Does.Contain("等级已变化"));
        }
        /// <summary>
        /// 升级后(本人升级回包,或别人升级的推送 → Refresh)同一个帮会的商店快照仍在,但 unlocked 是按旧等级算的:
        /// 停在商店页时自动重拉一次,之后不连发;结算推送已排队重拉本页时让给 DrainQueued。
        /// </summary>
        [Test] public void ShopPageReloadsAfterGuildLevelChanges()
        {
            int requests = 0; _window.ShopRequested += () => requests++;
            _client.RefreshShop(); _net.Reply(GuildClientTests.ShopFixture(440));
            _window.SetClient(_client); _window.Show(GuildPage.Shop);
            Assert.That(requests, Is.Zero);
            var upgraded = GuildClientTests.Fixture(); upgraded.Level = 6;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = upgraded });
            _window.SetClient(_client);
            Assert.That(requests, Is.EqualTo(1));
            _window.SetClient(_client);
            Assert.That(requests, Is.EqualTo(1));
            // DELIVERY_DONE 同时排了 Refresh 与本页重拉:再进商店页也不自己拉,免得盖掉"已发放"文案。
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 555, Kind = GuildChangeKind.DeliveryDone, TargetPlayerId = 1 });
            Assert.That(_client.ShopQueued, Is.True);
            _window.Show(GuildPage.Shop);
            Assert.That(requests, Is.EqualTo(1));
            _client.DrainQueued(false);
            _net.Reply(new GetPlayerGuildResponse { Guild = upgraded });
            _client.DrainQueued(false);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildShop));
        }
        /// <summary>客户端一直开着跨过 05:00:快照非空,但"今日 2/2"已不可信,再进捐献页要重拉一次。</summary>
        [Test] public void DonatePageReloadsSnapshotPastDailyReset()
        {
            int requests = 0; _window.DonationsRequested += () => requests++;
            var today = GuildClientTests.DonateFixture();
            today.NextDailyResetMs = (ulong)DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeMilliseconds();
            _client.RefreshDonations(); _net.Reply(today);
            _window.SetClient(_client); _window.Show(GuildPage.Donate);
            Assert.That(requests, Is.Zero);
            var yesterday = GuildClientTests.DonateFixture();
            yesterday.NextDailyResetMs = (ulong)DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
            _client.RefreshDonations(); _net.Reply(yesterday);
            _window.Show(GuildPage.Overview); _window.Show(GuildPage.Donate);
            Assert.That(requests, Is.EqualTo(1));
            // 每次进入最多一次:拉回来的仍"过时"(本地时钟比服务端快)也不连发,之后交给刷新键。
            _window.SetClient(_client);
            Assert.That(requests, Is.EqualTo(1));
        }
        /// <summary>
        /// 停在捐献页跨过 05:00:服务端不推送、也没有回包,Render 不会发生。GuildUiRoot 每帧调的 Tick 发现快照
        /// 过了日切点,自动拉一次;拉回来前 / 仍过时都不连发;离开经济页后 Tick 什么也不做。
        /// </summary>
        [Test] public void DonatePageReloadsWhileStayingPastDailyReset()
        {
            int requests = 0; _window.DonationsRequested += () => requests++;
            var today = GuildClientTests.DonateFixture();
            today.NextDailyResetMs = (ulong)DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeMilliseconds();
            _client.RefreshDonations(); _net.Reply(today);
            _window.SetClient(_client); _window.Show(GuildPage.Donate);
            _window.Tick();
            Assert.That(requests, Is.Zero);
            // 本用例没把 Changed 接到窗口:换上"已过日切点"的快照后窗口不重建,相当于停在本页、时钟走过了 05:00。
            var yesterday = GuildClientTests.DonateFixture();
            yesterday.NextDailyResetMs = (ulong)DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
            _client.RefreshDonations(); _net.Reply(yesterday);
            _window.Tick();
            Assert.That(requests, Is.EqualTo(1));
            _window.Tick();
            Assert.That(requests, Is.EqualTo(1));
            _window.Show(GuildPage.Overview); _window.Tick();
            Assert.That(requests, Is.EqualTo(1));
        }
        /// <summary>
        /// 捐献拉取超时 → 隔离(待重新登录),Info 还在、快照仍为空:此时没有请求在途,
        /// 两页不能挂着"正在读取…"与状态栏的"请重新登录"互相矛盾。
        /// </summary>
        [Test] public void EconomyPagesShowRecoveryInsteadOfLoadingWhileQuarantined()
        {
            _client.RefreshDonations(); _net.Error("rpc timeout");
            Assert.That(_client.RequiresReconnect, Is.True);
            _window.SetClient(_client); _window.Show(GuildPage.Donate);
            Assert.That(NamedText("GuildDonateFooter"), Is.EqualTo(GuildClient.RecoveryMessage));
            Assert.That(ActiveText(), Does.Not.Contain("正在读取"));
            _window.Show(GuildPage.Shop);
            Assert.That(NamedText("GuildShopPlaceholder"), Is.EqualTo(GuildClient.RecoveryMessage));
            Assert.That(ActiveText(), Does.Not.Contain("正在读取"));
        }
        /// <summary>
        /// 商店页脚只有 800 宽(右侧 820 起是翻页键),捐献页脚 1480 宽;30 号字下任何结局、任何原因码都不能被
        /// 省略号截断(ActiveText 读的是源串,看不出截断)。按字体度量逐个核对 preferredWidth。
        /// 商店的拒绝 / 部分发放在页脚只写结论,全文在状态栏;结算中的捐献不许诺"自动入账"。
        /// </summary>
        [Test] public void EconomyFootersFitWithoutEllipsis()
        {
            uint[] reasons = { 0, 27000, 27001, 27002, 27003, 27004, 27005, 27006, 27007, 27008, 99999 };
            var outcomes = new[] { GuildAssetOrderStatus.Applied, GuildAssetOrderStatus.Rejected,
                GuildAssetOrderStatus.Aborted, GuildAssetOrderStatus.AppliedPartial };
            _window.Show(GuildPage.Shop);
            foreach (uint reason in reasons)
            {
                foreach (var outcome in outcomes)
                {
                    var recent = GuildClientTests.ShopFixture(440);
                    recent.RecentOrders.Add(new GuildShopOrderView { OpId = 8, GoodsId = 203, Count = 1, Status = outcome, ReasonTipId = reason });
                    _client.RefreshShop(); _net.Reply(recent); _window.SetClient(_client);
                    AssertFooterFits("GuildShopFooter");
                }
                var pending = GuildClientTests.ShopFixture(440);
                pending.PendingOrders.Add(new GuildShopOrderView { OpId = 9, GoodsId = 203, Count = 1,
                    Status = GuildAssetOrderStatus.Pending, ReasonTipId = reason });
                _client.RefreshShop(); _net.Reply(pending); _window.SetClient(_client);
                AssertFooterFits("GuildShopFooter");
            }
            // 先拉一份没有待发放的快照,清掉上面留下的 9 号待发放,下面走的才是"最近一单"的默认文案而不是"刚结算完"。
            _client.RefreshShop(); _net.Reply(GuildClientTests.ShopFixture(440));
            var rejected = GuildClientTests.ShopFixture(440);
            rejected.RecentOrders.Add(new GuildShopOrderView { OpId = 10, GoodsId = 203, Count = 1,
                Status = GuildAssetOrderStatus.Rejected, ReasonTipId = GuildAssetReasons.Blocked });
            _client.RefreshShop(); _net.Reply(rejected); _window.SetClient(_client);
            Assert.That(NamedText("GuildShopFooter"), Is.EqualTo("最近一单：兑换失败，帮贡与限购已退回"));
            Assert.That(_client.Status, Is.EqualTo("最近一单：兑换失败，帮贡与限购已退回：该物品或货币暂被限制"));

            _window.Show(GuildPage.Donate);
            foreach (uint reason in reasons)
            {
                var pending = GuildClientTests.DonateFixture();
                pending.PendingDonations.Add(new GuildDonationView { OpId = 11, DonateId = 2,
                    Status = GuildAssetOrderStatus.Pending, ReasonTipId = reason });
                _client.RefreshDonations(); _net.Reply(pending); _window.SetClient(_client);
                AssertFooterFits("GuildDonateFooter");
                Assert.That(NamedText("GuildDonateFooter"), Does.Not.Contain("入账"));
                foreach (var outcome in outcomes)
                {
                    var recent = GuildClientTests.DonateFixture();
                    recent.RecentResults.Add(new GuildDonationView { OpId = 12, DonateId = 2, Status = outcome, ReasonTipId = reason,
                        ContributionGain = 120, FundsGain = 12000 });
                    _client.RefreshDonations(); _net.Reply(recent); _window.SetClient(_client);
                    AssertFooterFits("GuildDonateFooter");
                }
            }
            var insufficient = GuildClientTests.DonateFixture();
            insufficient.PendingDonations.Add(new GuildDonationView { OpId = 13, DonateId = 2,
                Status = GuildAssetOrderStatus.Pending, ReasonTipId = GuildAssetReasons.CurrencyInsufficient });
            _client.RefreshDonations(); _net.Reply(insufficient); _window.SetClient(_client);
            Assert.That(NamedText("GuildDonateFooter"), Is.EqualTo("1 笔捐献结算中：余额不足，正在确认结算结果"));
        }
        /// <summary>
        /// 30 号正文一行要 (ascent − descent)×30/64 ≈ 43.4 高。框比它矮时 TMP 的 Ellipsis 在第一个字就判溢出,
        /// 连省略号都插不进去,整行一个字都不出;ActiveText 读的是源串,看不出来。这里按字体度量逐个核对
        /// 捐献页与商店页(含页脚)所有单行正文的框高。
        /// </summary>
        [Test] public void EconomyPageBodyTextBoxesFitOneLine()
        {
            var donations = GuildClientTests.DonateFixture();
            donations.PendingDonations.Add(new GuildDonationView { OpId = 7, DonateId = 3, Status = GuildAssetOrderStatus.Pending });
            _client.RefreshDonations(); _net.Reply(donations);
            var shop = GuildClientTests.ShopFixture(440);
            shop.PendingOrders.Add(new GuildShopOrderView { OpId = 8, GoodsId = 203, Count = 1, Status = GuildAssetOrderStatus.Pending });
            _client.RefreshShop(); _net.Reply(shop);
            _window.SetClient(_client); _window.Show(GuildPage.Donate);
            Assert.That(AssertBodyLinesFit(), Has.Member("GuildDonateFooter"));
            _window.Show(GuildPage.Shop);
            for (int category = 1; category <= 3; category++)
            {
                Click("GuildShopCategory_" + category);
                Assert.That(AssertBodyLinesFit(), Has.Member("GuildShopFooter"));
            }
        }
        private List<string> AssertBodyLinesFit()
        {
            var body = QdaoUguiTypography.ResolveBodyFont();
            var names = new List<string>();
            foreach (var text in _root.GetComponentsInChildren<TMP_Text>())
            {
                if (!text.gameObject.activeInHierarchy || string.IsNullOrEmpty(text.text) || text.font != body
                    || text.textWrappingMode != TextWrappingModes.NoWrap) continue;
                var face = text.font.faceInfo;
                float line = (face.ascentLine - face.descentLine) * face.scale * text.fontSize / face.pointSize;
                Assert.That(text.rectTransform.rect.height, Is.GreaterThanOrEqualTo(line), text.name + " / " + text.text);
                names.Add(text.name);
            }
            return names;
        }
        /// <summary>单行页脚的字形总宽不超框宽;超了 TMP 会以省略号截掉尾巴(页脚是 NoWrap + Ellipsis)。</summary>
        private void AssertFooterFits(string name)
        {
            var footer = _root.GetComponentsInChildren<TMP_Text>().Single(t => t.name == name);
            Assert.That(footer.GetPreferredValues(footer.text).x, Is.LessThanOrEqualTo(footer.rectTransform.rect.width), footer.text);
        }
        private string NamedText(string name) => _root.GetComponentsInChildren<TMP_Text>().Single(t => t.name == name).text;

        private TMP_InputField SearchInput() => _root.GetComponentsInChildren<TMP_InputField>().Single(i => i.name == "GuildMemberSearch");
        private void Search(string text) { SearchInput().text = text; Click("SearchGuildMembers"); }
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
        /// <summary>与 Calls 一一对应的请求体,断言“发出去的参数”用。</summary>
        public List<IMessage> Requests { get; } = new();
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
            where T : IMessage<T> { Calls.Add(id); Requests.Add(request); Pending = message => success((T)message); Error = error; }
    }
}
