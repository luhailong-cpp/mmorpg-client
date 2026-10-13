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

        // ── 活动：灯会 / 团圆（B6a-cli）────────────────────────────────

        [Test] public void RefreshActivitiesStoresResponse()
        {
            Load(); _client.RefreshActivities();
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildActivities));
            _net.Reply(ActivitiesFixture());
            Assert.That(_client.Activities.Activities.Count, Is.EqualTo(3));
            Assert.That(_client.Status, Is.EqualTo("帮会活动已更新"));
            Assert.That(_client.ActivitiesNeedReload(0), Is.False);
        }
        /// <summary>
        /// 点灯成功只回本活动的视图:就地换掉那一条,帮贡 / 资金靠补拉 GetPlayerGuild。补拉不能在回调里连发
        /// (排队交给 DrainQueued),提示要活过这次补拉,且只用一次。
        /// </summary>
        [Test] public void LanternWriteQueuesGuildRefreshKeepingNotice()
        {
            Load(); LoadActivities();
            _client.LightLantern(1);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.LightGuildLantern));
            Assert.That(((LightGuildLanternRequest)_net.Requests.Last()).ActivityId, Is.EqualTo(1u));
            int sent = _net.Calls.Count;
            _net.Reply(new LightGuildLanternResponse { Activity = LitLantern() });
            Assert.That(_client.Activities.Activities[0].MyUsedCount, Is.EqualTo(1u));
            Assert.That(_client.Activities.Activities.Count, Is.EqualTo(3));
            Assert.That(_client.RefreshQueued, Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
            Assert.That(_client.Busy, Is.False);
            Assert.That(_client.Status, Is.EqualTo("花灯已点亮，帮贡已到账。"));
            _client.DrainQueued(false, true);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
            var funded = Fixture(); funded.Funds = 500; funded.Members[0].ContributionBalance = 20;
            _net.Reply(new GetPlayerGuildResponse { Guild = funded });
            Assert.That(_client.Info.Funds, Is.EqualTo(500ul));
            Assert.That(_client.Status, Is.EqualTo("花灯已点亮，帮贡已到账。"));
            // 没有别的排队项:这一帧不再发请求。
            _client.DrainQueued(false, true);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
            Assert.That(_client.Busy, Is.False);
            // 提示只用一次:之后不相干的刷新回到普通文案。
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = funded });
            Assert.That(_client.Status, Is.EqualTo("帮会信息已更新"));
        }
        [Test] public void ReunionClaimSendsActivityIdAndReloadsTheBag()
        {
            Load(); LoadActivities();
            int assets = 0; _client.AssetsChanged += () => assets++;
            _client.ClaimReunion(2);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.ClaimGuildReunion));
            Assert.That(((ClaimGuildReunionRequest)_net.Requests.Last()).ActivityId, Is.EqualTo(2u));
            var claimed = ActivitiesFixture().Activities[1];
            claimed.MyUsedCount = 1; claimed.ThresholdReached = true; claimed.Progress = 0;
            claimed.BlockedTipId = (uint)guild_error.KGuildActivityAlreadyClaimed;
            _net.Reply(new ClaimGuildReunionResponse { Activity = claimed });
            Assert.That(_client.Activities.Activities[1].ThresholdReached, Is.True);
            // 团圆礼带物品:可能已同步进包,背包要重拉(scene 没有背包推送)。
            Assert.That(assets, Is.EqualTo(1));
            Assert.That(_client.RefreshQueued, Is.True);
            Assert.That(_client.Status, Is.EqualTo("团圆礼已领取，物品稍后到账。"));
        }
        [Test] public void ActivityPushWhileBusyDrainsNextFrame()
        {
            Load();
            _client.Refresh(); // 占住唯一的在途请求位
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C { GuildId = 555, Kind = GuildChangeKind.ActivityChanged });
            Assert.That(_client.ActivitiesQueued, Is.True);
            // 活动推送只重拉活动视图,不排 GetPlayerGuild。
            Assert.That(_client.RefreshQueued, Is.False);
            int before = _net.Calls.Count;
            _client.DrainQueued(false, true);
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            _net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            // 活动页不可见时不拉,标志留着等进页。
            _client.DrainQueued(false, false);
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.ActivitiesQueued, Is.True);
            _client.DrainQueued(false, true);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildActivities));
            Assert.That(_client.ActivitiesQueued, Is.False);
        }
        /// <summary>
        /// 打开窗口那一发 GetPlayerGuild 还在路上时进活动页:请求只排队、不被 Busy 丢掉,
        /// 回包后的下一帧自动发出(页面不会卡在"正在读取")。
        /// </summary>
        [Test] public void ActivitiesRequestedWhileToggleRefreshInFlight()
        {
            Load();
            _client.Refresh();
            int before = _net.Calls.Count;
            _client.QueueActivities();
            Assert.That(_client.ActivitiesQueued, Is.True);
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            _net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            _client.DrainQueued(false, true);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildActivities));
            _net.Reply(ActivitiesFixture());
            Assert.That(_client.Activities, Is.Not.Null);
            // 发出后标志已清,每帧调用也不连发。
            _client.DrainQueued(false, true);
            Assert.That(_net.Calls.Count, Is.EqualTo(before + 1));
        }
        [Test] public void QueueActivitiesOutsideGuildDoesNothing()
        {
            Empty();
            _client.QueueActivities();
            Assert.That(_client.ActivitiesQueued, Is.False);
            int before = _net.Calls.Count;
            _client.RefreshActivities();
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Status, Does.Contain("加入帮会后"));
        }
        [Test] public void InvitePushSetsPending()
        {
            Load();
            // target = 0:全帮的进度变化(本档期首次达阈值),不是邀请。
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C { GuildId = 555, Kind = GuildChangeKind.ActivityChanged });
            Assert.That(_client.TrialInvitePending, Is.False);
            Assert.That(_client.ActivitiesQueued, Is.True);
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 555, Kind = GuildChangeKind.ActivityChanged, ActorPlayerId = 2, TargetPlayerId = 1 });
            Assert.That(_client.TrialInvitePending, Is.True);
            Assert.That(_client.Status, Does.Contain("历练邀请"));
            _client.ConsumeTrialInvite();
            Assert.That(_client.TrialInvitePending, Is.False);
            // 别的帮会的活动推送与本人无关。
            _client.RefreshActivities(); _net.Reply(ActivitiesFixture());
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 999, Kind = GuildChangeKind.ActivityChanged, TargetPlayerId = 1 });
            Assert.That(_client.TrialInvitePending, Is.False);
            Assert.That(_client.ActivitiesQueued, Is.False);
        }
        /// <summary>
        /// 活动的 10 个码(连同会在领奖时出现的 kGuildAssetPending)都要有人话,带参数的把数字填进去;
        /// 历练的两个码(名单不合法、被婉拒)把参数里的角色编号换成名字(样例成员没有名字,落到"道友 · 编号")。
        /// 被拒后本页带着拒绝文案直接重拉一次:文案要活过这次重拉,不能被盖成"帮会活动已更新"。
        /// parameters 用逗号分隔;不带参数的码,服务端会塞一段英文说明当唯一参数。
        /// </summary>
        [TestCase((uint)guild_error.KGuildActivityNotOpen, "guild activity not open", "该活动暂未开放。")]
        [TestCase((uint)guild_error.KGuildActivityAlreadyClaimed, "guild activity daily limit reached", "今日已参与，明日 05:00 后再来。")]
        [TestCase((uint)guild_error.KGuildActivityThresholdNotReached, "2,3", "同时在线的同道不足（2/3），再等等大家吧。")]
        [TestCase((uint)guild_error.KGuildActivityLevelTooLow, "3", "帮会达到 3 级后才能参与。")]
        [TestCase((uint)guild_error.KGuildActivityJoinTooRecent, "24", "入帮满 24 小时后才能参与帮会活动。")]
        [TestCase((uint)guild_error.KGuildAssetPending, "too many pending asset ops", "还有未结算的帮会操作，请稍后再试。")]
        [TestCase((uint)guild_error.KGuildTrialTeamInvalid, "offline,42", "道友 · 42 当前不在线。")]
        [TestCase((uint)guild_error.KGuildTrialInviteExpired, "", "邀请已失效。")]
        [TestCase((uint)guild_error.KGuildTrialInviteDeclined, "42", "道友 · 42 婉拒了同道历练邀请。")]
        [TestCase((uint)guild_error.KGuildTrialInviteCooldown, "7", "发起太频繁，请 7 秒后再试。")]
        [TestCase((uint)guild_error.KGuildTrialServiceBusy, "trial service busy", "历练服务繁忙，请稍后再试。")]
        public void ActivityTipsMapToReadableStatus(uint tip, string parameters, string expected)
        {
            Load(); LoadActivities();
            _client.LightLantern(1);
            var message = new TipInfoMessage { Id = tip };
            foreach (string parameter in parameters.Split(',')) if (parameter.Length > 0) message.Parameters.Add(parameter);
            _net.Reply(new LightGuildLanternResponse { ErrorMessage = message });
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildActivities));
            Assert.That(_client.ActivitiesQueued, Is.False);
            _net.Reply(ActivitiesFixture());
            Assert.That(_client.Status, Is.EqualTo(expected));
            // 业务拒绝走的是 success 回调:连接完好,不隔离,也不为它补拉 GetPlayerGuild ——
            // 唯一的例外是"名单里有人已离线 / 已不在帮会":成员快照过时了,要补拉(见 TrialTeamInvalidShowsMemberName)。
            Assert.That(_client.RequiresReconnect, Is.False);
            Assert.That(_client.RefreshQueued, Is.EqualTo(tip == (uint)guild_error.KGuildTrialTeamInvalid));
        }
        /// <summary>
        /// 带参数的码,参数缺失或不是数字(服务端别的分支塞了英文说明)时回落到不带数字的说法,
        /// 不把英文拼进文案,也不抛异常。团圆人数要两个数都在才写比值。
        /// </summary>
        [TestCase((uint)guild_error.KGuildActivityThresholdNotReached, "同时在线的同道不足，再等等大家吧。")]
        [TestCase((uint)guild_error.KGuildActivityLevelTooLow, "帮会等级不足，暂时不能参与。")]
        [TestCase((uint)guild_error.KGuildActivityJoinTooRecent, "入帮时间不足，暂时不能参与帮会活动。")]
        [TestCase((uint)guild_error.KGuildTrialInviteCooldown, "发起太频繁，请稍后再试。")]
        public void ActivityTipWithoutNumericParametersFallsBackToGenericText(uint tip, string expected)
        {
            Assert.That(GuildClient.TipText(new TipInfoMessage { Id = tip }), Is.EqualTo(expected));
            var english = new TipInfoMessage { Id = tip }; english.Parameters.Add("guild activity blocked");
            Assert.That(GuildClient.TipText(english), Is.EqualTo(expected));
            var signed = new TipInfoMessage { Id = tip }; signed.Parameters.Add("-1"); signed.Parameters.Add("x");
            Assert.That(GuildClient.TipText(signed), Is.EqualTo(expected));
            var half = new TipInfoMessage { Id = (uint)guild_error.KGuildActivityThresholdNotReached }; half.Parameters.Add("2");
            Assert.That(GuildClient.TipText(half), Is.EqualTo("同时在线的同道不足，再等等大家吧。"));
        }
        /// <summary>写操作被拒且回"已不在帮会":不重拉活动页(必然再被拒),只排 GetPlayerGuild,活动快照随之清掉。</summary>
        [Test] public void NotInGuildClearsActivitiesAndNotice()
        {
            Load(); LoadActivities();
            _client.LightLantern(1);
            _net.Reply(new LightGuildLanternResponse { Activity = LitLantern() });
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 555, Kind = GuildChangeKind.ActivityChanged, TargetPlayerId = 1 });
            Assert.That(_client.ActivitiesQueued, Is.True); Assert.That(_client.TrialInvitePending, Is.True);
            // 点完灯随即被请离:补拉的 GetPlayerGuild 回"未入帮"。
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 555, Kind = GuildChangeKind.MemberKicked, TargetPlayerId = 1 });
            _client.DrainQueued(false, true);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
            _net.Reply(new GetPlayerGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } });
            Assert.That(_client.Activities, Is.Null);
            Assert.That(_client.ActivitiesQueued, Is.False);
            Assert.That(_client.TrialInvitePending, Is.False);
            // 未入帮分支优先说"被请离"(B2 的提示),"花灯已点亮"此时已无意义(90 清单 X-06)。
            Assert.That(_client.Status, Does.Contain("请离"));
            // 写操作的提示不留给以后:重新入帮后的刷新不会冒出"花灯已点亮"。
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            Assert.That(_client.Status, Is.EqualTo("帮会信息已更新"));

            LoadActivities();
            _client.LightLantern(1); int sent = _net.Calls.Count;
            _net.Reply(new LightGuildLanternResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } });
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
            Assert.That(_client.RefreshQueued, Is.True);
        }
        [Test] public void ResetClearsActivities()
        {
            Load(); LoadActivities();
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 555, Kind = GuildChangeKind.ActivityChanged, TargetPlayerId = 1 });
            Assert.That(_client.ActivitiesQueued, Is.True); Assert.That(_client.TrialInvitePending, Is.True);
            _client.Reset();
            Assert.That(_client.Activities, Is.Null);
            Assert.That(_client.ActivitiesQueued, Is.False);
            Assert.That(_client.TrialInvitePending, Is.False);
            Assert.That(_client.ActivitiesNeedReload(0), Is.True);
            Assert.That(_client.ActivityServerNowMs, Is.Zero);
        }
        /// <summary>
        /// 成功却没带回本活动的视图(服务端提交后重建视图失败,写已生效):本页带着提示重拉,
        /// 否则按钮还停在"可点";帮贡 / 资金照常排队补拉。
        /// </summary>
        [Test] public void ActivityWriteWithoutReturnedViewReloadsThePage()
        {
            Load(); LoadActivities();
            _client.LightLantern(1);
            _net.Reply(new LightGuildLanternResponse());
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildActivities));
            Assert.That(_client.RefreshQueued, Is.True);
            var reloaded = ActivitiesFixture(); reloaded.Activities[0] = LitLantern();
            _net.Reply(reloaded);
            Assert.That(_client.Activities.Activities[0].MyUsedCount, Is.EqualTo(1u));
            Assert.That(_client.Status, Is.EqualTo("花灯已点亮，帮贡已到账。"));
        }
        /// <summary>
        /// 同一个帮会里等级变了:视图里的"能不能参与"是按旧等级算的,快照不清、只标过时,本页重拉成功即不再过时。
        /// 换帮则整份作废(进度与今日次数都是上一个帮会的)。
        /// </summary>
        [Test] public void GuildLevelChangeMarksActivitiesStaleAndGuildChangeClearsThem()
        {
            Load(); LoadActivities();
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            Assert.That(_client.ActivitiesNeedReload(0), Is.False);
            var leveled = Fixture(); leveled.Level = 6;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = leveled });
            Assert.That(_client.Activities, Is.Not.Null);
            Assert.That(_client.ActivitiesNeedReload(0), Is.True);
            LoadActivities();
            Assert.That(_client.ActivitiesNeedReload(0), Is.False);
            var other = Fixture(); other.GuildId = 556;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = other });
            Assert.That(_client.Activities, Is.Null);
        }
        /// <summary>
        /// 过没过游戏日切点按服务端时钟估算:视图的 server_time_ms + 收到后流逝的时间(单调时钟),不读本机墙钟。
        /// 写回包换进来的视图带着更新的时刻,锚随之校准。
        /// </summary>
        [Test] public void ActivitiesExpireAtTheServerResetTime()
        {
            long clock = 1000;
            var net = new GuildFakeTransport();
            using var client = new GuildClient(net, null, () => clock);
            client.Refresh(); net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            var response = ActivitiesFixture();
            foreach (var view in response.Activities) { view.ServerTimeMs = 5_000_000; view.NextResetMs = 5_060_000; }
            client.RefreshActivities(); net.Reply(response);
            Assert.That(client.ActivityServerNowMs, Is.EqualTo(5_000_000ul));
            Assert.That(client.ActivityNextResetMs, Is.EqualTo(5_060_000ul));
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.False);
            clock += 59_999;
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.False);
            clock += 1;
            Assert.That(client.ActivityServerNowMs, Is.EqualTo(5_060_000ul));
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.True);

            // 过了切点后点灯:回包里的这一条已是新一天的视图,但另外两条仍是昨天的 —— 整页仍要重拉。
            client.LightLantern(1);
            var lit = LitLantern(); lit.ServerTimeMs = 5_061_000; lit.NextResetMs = 91_460_000;
            net.Reply(new LightGuildLanternResponse { Activity = lit });
            Assert.That(client.ActivityServerNowMs, Is.EqualTo(5_061_000ul));
            Assert.That(client.ActivityNextResetMs, Is.EqualTo(5_060_000ul));
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.True);
            // 没带时刻的快照(样例 / 替身)不按时间判过期。
            client.Refresh(); net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            client.RefreshActivities(); net.Reply(ActivitiesFixture());
            Assert.That(client.ActivityServerNowMs, Is.Zero);
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.False);
        }
        /// <summary>
        /// 活动的物品奖励与商店兑换发同一种推送(DELIVERY_DONE):活动页上挂着"待发放"时排队重拉本页,
        /// 没有待发放时(那是商店的单)不白拉。
        /// </summary>
        [Test] public void DeliveryDonePushReloadsActivitiesOnlyWhenARewardIsPending()
        {
            Load(); LoadActivities();
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 555, Kind = GuildChangeKind.DeliveryDone, TargetPlayerId = 1 });
            Assert.That(_client.ActivitiesQueued, Is.False);
            var pending = ActivitiesFixture();
            pending.Activities[1].MyPendingRewardCount = 1; pending.Activities[1].MyPendingReasonTipId = GuildAssetReasons.BagFull;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            _client.RefreshActivities(); _net.Reply(pending);
            int assets = 0; _client.AssetsChanged += () => assets++;
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 555, Kind = GuildChangeKind.DeliveryDone, TargetPlayerId = 1 });
            Assert.That(assets, Is.EqualTo(1));
            Assert.That(_client.ActivitiesQueued, Is.True);
            // 帮会快照在前,活动视图在后(90 清单 X-07)。
            _client.DrainQueued(false, true);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
            _net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            _client.DrainQueued(false, true);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildActivities));
        }
        /// <summary>
        /// 写操作的结果文案不能被紧随其后的活动视图重拉盖成"帮会活动已更新":点灯成功的同时别人让本期达了阈值
        /// (活动推送也排了队),或带着拒绝原因的那一发重拉还在路上时又来了推送。只沿用一次,之后回到普通文案。
        /// </summary>
        [Test] public void ActivityWriteResultSurvivesTheNextQueuedActivitiesReloadOnce()
        {
            Load(); LoadActivities();
            _client.LightLantern(1);
            _net.Reply(new LightGuildLanternResponse { Activity = LitLantern() });
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C { GuildId = 555, Kind = GuildChangeKind.ActivityChanged });
            _client.DrainQueued(false, true);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
            _net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            _client.DrainQueued(false, true);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildActivities));
            _net.Reply(ActivitiesFixture());
            Assert.That(_client.Status, Is.EqualTo("花灯已点亮，帮贡已到账。"));
            _client.RefreshActivities(); _net.Reply(ActivitiesFixture());
            Assert.That(_client.Status, Is.EqualTo("帮会活动已更新"));

            _client.LightLantern(1);
            _net.Reply(new LightGuildLanternResponse
                { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildActivityAlreadyClaimed } });
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildActivities));
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C { GuildId = 555, Kind = GuildChangeKind.ActivityChanged });
            _net.Reply(ActivitiesFixture());
            int sent = _net.Calls.Count;
            _client.DrainQueued(false, true);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent + 1));
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildActivities));
            _net.Reply(ActivitiesFixture());
            Assert.That(_client.Status, Is.EqualTo("今日已参与，明日 05:00 后再来。"));
            // 状态栏被别的文案换掉之后,重拉不再把旧结果翻出来。
            _client.LightLantern(1);
            _net.Reply(new LightGuildLanternResponse { Activity = LitLantern() });
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            _client.Browse(); _net.Reply(new GetGuildRankResponse { Page = 1, PageSize = 5 });
            _client.RefreshActivities(); _net.Reply(ActivitiesFixture());
            Assert.That(_client.Status, Is.EqualTo("帮会活动已更新"));
        }
        /// <summary>
        /// 档期切点没有任何推送:未开始的活动到了开始时刻、进行中的到了结束时刻,快照就过时(状态与按钮都要换)。
        /// 已结束 / 未开放 / 常开(起止都为 0)的没有切点。时刻同样按服务端时钟估算。
        /// </summary>
        [Test] public void ActivitiesExpireWhenAScheduleBoundaryPasses()
        {
            long clock = 0;
            var net = new GuildFakeTransport();
            using var client = new GuildClient(net, null, () => clock);
            client.Refresh(); net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            var response = ActivitiesFixture();
            foreach (var view in response.Activities) { view.ServerTimeMs = 5_000_000; view.NextResetMs = 9_000_000; }
            // 灯会 1 分钟后开始;团圆进行中,2 分钟后结束;历练未开放(起止时刻不参与)。
            var lantern = response.Activities[0];
            lantern.State = GuildActivityState.Upcoming; lantern.StartAtMs = 5_060_000; lantern.EndAtMs = 8_000_000;
            lantern.BlockedTipId = (uint)guild_error.KGuildActivityNotOpen;
            response.Activities[1].StartAtMs = 4_000_000; response.Activities[1].EndAtMs = 5_120_000;
            response.Activities[2].StartAtMs = 4_000_000; response.Activities[2].EndAtMs = 5_000_001;
            client.RefreshActivities(); net.Reply(response);
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.False);
            clock += 59_999;
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.False);
            clock += 1;
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.True);

            // 重拉回来灯会已开:下一个切点是团圆的结束时刻。
            var opened = response.Clone();
            foreach (var view in opened.Activities) view.ServerTimeMs = 5_060_000;
            opened.Activities[0].State = GuildActivityState.Open; opened.Activities[0].BlockedTipId = 0;
            client.RefreshActivities(); net.Reply(opened);
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.False);
            clock += 59_999;
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.False);
            clock += 1;
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.True);

            // 团圆已结束:它不再有切点;灯会要到 8_000_000 才结束。
            var ended = opened.Clone();
            foreach (var view in ended.Activities) view.ServerTimeMs = 5_120_000;
            ended.Activities[1].State = GuildActivityState.Ended; ended.Activities[1].BlockedTipId = (uint)guild_error.KGuildActivityNotOpen;
            client.RefreshActivities(); net.Reply(ended);
            clock += 1_000_000;
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.False);
        }

        // ── 活动：同道历练的建房与应答（B6b-cli）──────────────────────────

        /// <summary>
        /// 建房前只挡"明显发不出去"的名单:没有历练视图、活动 id 不是历练、自己不在首位、人数越界、重复、编号为 0。
        /// 在不在线、是不是本帮由服务端核对。发出去的请求带活动 id 与整份名单(顺序不变)。
        /// </summary>
        [Test] public void StartTrialValidatesLocally()
        {
            Load();
            _client.StartTrial(3, new ulong[] { 1, 3 });
            Assert.That(_net.Calls.Contains(MessageIds.StartGuildTrial), Is.False);
            Assert.That(_client.Status, Does.Contain("已过期"));
            _client.RefreshActivities(); _net.Reply(TrialFixture(teamMax: 3));
            int before = _net.Calls.Count;
            _client.StartTrial(1, new ulong[] { 1, 3 });        // 灯会的 id
            Assert.That(_client.Status, Does.Contain("已过期"));
            _client.StartTrial(3, null);
            _client.StartTrial(3, new ulong[0]);
            _client.StartTrial(3, new ulong[] { 3, 1 });        // 自己不在首位
            _client.StartTrial(3, new ulong[] { 1 });           // 少于 team_size_min(2)
            _client.StartTrial(3, new ulong[] { 1, 3, 5, 7 });  // 多于 team_size_max(3)
            _client.StartTrial(3, new ulong[] { 1, 3, 3 });     // 重复
            _client.StartTrial(3, new ulong[] { 1, 0 });        // 无效编号
            Assert.That(_net.Calls.Count, Is.EqualTo(before));
            Assert.That(_client.Status, Does.Contain("符合人数要求"));
            Assert.That(_client.Busy, Is.False);
            _client.StartTrial(3, new ulong[] { 1, 5, 3 });
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.StartGuildTrial));
            var request = (StartGuildTrialRequest)_net.Requests.Last();
            Assert.That(request.ActivityId, Is.EqualTo(3u));
            Assert.That(request.MemberPlayerIds.ToArray(), Is.EqualTo(new ulong[] { 1, 5, 3 }));
        }
        /// <summary>
        /// 建房成功只动邀请房间:就地换掉历练视图,不补拉帮会快照、不重拉背包、回调里不连发请求。
        /// 之后别人同意 / 婉拒经推送触发重拉,状态栏按房间的新状态写,不被"邀请已发出"占着(历练的成功提示不沿用)。
        /// </summary>
        [Test] public void StartTrialAppliesLobbyWithoutQueueingGuildRefresh()
        {
            Load(); _client.RefreshActivities(); _net.Reply(TrialFixture());
            int assets = 0; _client.AssetsChanged += () => assets++;
            _client.StartTrial(3, new ulong[] { 1, 3, 5 });
            int sent = _net.Calls.Count;
            _net.Reply(new StartGuildTrialResponse { Activity = TrialFixture(Lobby(1, 3, 5)).Activities[2] });
            Assert.That(_client.Activities.Activities[2].TrialLobby.LobbyId, Is.EqualTo(77ul));
            Assert.That(_client.Activities.Activities.Count, Is.EqualTo(3));
            Assert.That(_client.Status, Is.EqualTo("邀请已发出，等待同道确认。"));
            Assert.That(_client.RefreshQueued, Is.False);
            Assert.That(assets, Is.Zero);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
            Assert.That(_client.Busy, Is.False);

            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C { GuildId = 555, Kind = GuildChangeKind.ActivityChanged, ActorPlayerId = 3 });
            Assert.That(_client.TrialInvitePending, Is.False);
            _client.DrainQueued(false, true);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildActivities));
            var two = Lobby(1, 3, 5); two.AcceptedPlayerIds.Add(3);
            _net.Reply(TrialFixture(two));
            Assert.That(_client.Status, Is.EqualTo("同道历练邀请确认中（2/3）。"));

            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C { GuildId = 555, Kind = GuildChangeKind.ActivityChanged, ActorPlayerId = 5 });
            _client.DrainQueued(false, true);
            _net.Reply(TrialFixture(EndedLobby(Lobby(1, 3, 5), guild_error.KGuildTrialInviteDeclined, "5")));
            Assert.That(_client.Status, Is.EqualTo("道友 · 5 婉拒了同道历练邀请。"));
        }
        /// <summary>
        /// 名单被拒带 [reason, player_id]:状态栏指名道姓。"有人已离线 / 已不在帮会"说明成员快照过时(在线状态没有推送,
        /// 活动页的刷新键也只刷活动视图),排队补拉一次帮会快照,拒绝原因要活过本页重拉与那一发补拉。
        /// </summary>
        [Test] public void TrialTeamInvalidShowsMemberName()
        {
            Load(); _client.RefreshActivities(); _net.Reply(TrialFixture());
            _client.StartTrial(3, new ulong[] { 1, 3 });
            var tip = new TipInfoMessage { Id = (uint)guild_error.KGuildTrialTeamInvalid };
            tip.Parameters.Add("offline"); tip.Parameters.Add("42");
            _net.Reply(new StartGuildTrialResponse { ErrorMessage = tip });
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildActivities));
            _net.Reply(TrialFixture());
            Assert.That(_client.Status, Is.EqualTo("道友 · 42 当前不在线。"));
            Assert.That(_client.RefreshQueued, Is.True);
            _client.DrainQueued(false, true);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
            var refreshed = Fixture(); refreshed.Members[2].Online = false;
            _net.Reply(new GetPlayerGuildResponse { Guild = refreshed });
            Assert.That(_client.Info.Members[2].Online, Is.False);
            Assert.That(_client.Status, Is.EqualTo("道友 · 42 当前不在线。"));
            Assert.That(_client.RequiresReconnect, Is.False);
            // 提示只用一次。
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = refreshed });
            Assert.That(_client.Status, Is.EqualTo("帮会信息已更新"));
        }
        /// <summary>
        /// reason 全集逐个成句:有名字用名字、是自己说"你"、与具体某人无关的不带主语。
        /// 参数异常(缺失、不是数字、带符号、溢出、被塞了英文说明、不认识的 reason)一律不抛异常,落到不指名或通用的说法。
        /// </summary>
        [TestCase("offline", "3", "阿青 当前不在线。")]
        [TestCase("not_member", "3", "阿青 不是本帮成员。")]
        [TestCase("join_recent", "3", "阿青 入帮时间不足。")]
        [TestCase("busy", "3", "阿青 正在响应其他历练邀请。")]
        [TestCase("in_battle", "1", "你正在战斗中。")]
        [TestCase("not_ready", "3", "阿青 暂时无法入场（不在场景中或正在排队）。")]
        [TestCase("size", "0", "队伍人数不符合要求。")]
        [TestCase("duplicate", "3", "队伍名单有重复或无效成员。")]
        [TestCase("initiator_missing", "0", "发起人必须在队伍中。")]
        [TestCase("invalid", "0", "队伍信息无效，请刷新后重试。")]
        [TestCase("offline", "", "有同道当前不在线。")]
        [TestCase("offline", "abc", "有同道当前不在线。")]
        [TestCase("offline", "-3", "有同道当前不在线。")]
        [TestCase("offline", "99999999999999999999999", "有同道当前不在线。")]
        [TestCase("guild trial team invalid", "", "队伍信息无效，请刷新后重试。")]
        [TestCase("", "", "队伍信息无效，请刷新后重试。")]
        public void TrialTeamInvalidReasonsReadNaturally(string reason, string playerId, string expected)
        {
            var info = Fixture(); info.Members[2].Name = "阿青";
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = info });
            _client.RefreshActivities(); _net.Reply(TrialFixture());
            _client.StartTrial(3, new ulong[] { 1, 3 });
            var tip = new TipInfoMessage { Id = (uint)guild_error.KGuildTrialTeamInvalid };
            if (reason.Length > 0) tip.Parameters.Add(reason);
            if (playerId.Length > 0) tip.Parameters.Add(playerId);
            _net.Reply(new StartGuildTrialResponse { ErrorMessage = tip });
            _net.Reply(TrialFixture());
            Assert.That(_client.Status, Is.EqualTo(expected));
            Assert.That(_client.RequiresReconnect, Is.False);
        }
        /// <summary>
        /// 应答带的是界面传入的房间 id。同意之后是"还在等别人"还是别的,看回包里的房间;应答只动邀请房间,
        /// 不补拉帮会快照,回调里不连发请求。
        /// </summary>
        [Test] public void RespondTrialInviteSendsTheLobbyAndReportsWhatHappened()
        {
            Load(); _client.RefreshActivities(); _net.Reply(TrialFixture(Lobby(3, 1, 5)));
            Assert.That(_client.Status, Is.EqualTo("道友 · 3 邀请你同往历练，请在活动页响应。"));
            _client.RespondTrialInvite(0, true);
            Assert.That(_net.Calls.Contains(MessageIds.RespondGuildTrialInvite), Is.False);
            Assert.That(_client.Status, Is.EqualTo("邀请已失效。"));
            _client.RespondTrialInvite(77, true);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.RespondGuildTrialInvite));
            var request = (RespondGuildTrialInviteRequest)_net.Requests.Last();
            Assert.That(request.LobbyId, Is.EqualTo(77ul));
            Assert.That(request.Accept, Is.True);
            int sent = _net.Calls.Count;
            var waiting = Lobby(3, 1, 5); waiting.AcceptedPlayerIds.Add(1);
            _net.Reply(new RespondGuildTrialInviteResponse { Activity = TrialFixture(waiting).Activities[2] });
            Assert.That(_client.Activities.Activities[2].TrialLobby.AcceptedPlayerIds.Count, Is.EqualTo(2));
            Assert.That(_client.Status, Is.EqualTo("已同意同道历练，等待其他同道确认（2/3）。"));
            Assert.That(_client.RefreshQueued, Is.False);
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
            // 房间没了(已过期)且没带回视图:带着原因重拉本页。
            _client.RespondTrialInvite(77, true);
            _net.Reply(new RespondGuildTrialInviteResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildTrialInviteExpired } });
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildActivities));
            _net.Reply(TrialFixture());
            Assert.That(_client.Status, Is.EqualTo("邀请已失效。"));
            Assert.That(_client.RequiresReconnect, Is.False);
        }
        /// <summary>
        /// 最后一个人同意的那次调用负责开战。成功:回包里房间已开战、本人有进行中的对局。失败:服务端回 tip 并**同时**带回
        /// 已解散的房间 —— 先应用视图,状态栏写成与其他成员看到的同一句(带"未能开战"),本页不必再拉一次。
        /// </summary>
        [Test] public void LastAcceptLaunchesOrReportsWhyItCouldNot()
        {
            Load(); _client.RefreshActivities(); _net.Reply(TrialFixture(Lobby(3, 1)));
            _client.RespondTrialInvite(77, true);
            var launched = Lobby(3, 1); launched.AcceptedPlayerIds.Add(1);
            launched.State = GuildTrialLobbyState.Launched; launched.BattleId = 9001;
            var fighting = TrialFixture(launched).Activities[2]; fighting.MyTrialBattleId = 9001;
            _net.Reply(new RespondGuildTrialInviteResponse { Activity = fighting });
            Assert.That(_client.Status, Is.EqualTo("全员已同意，同道历练开战。"));
            Assert.That(_client.Activities.Activities[2].MyTrialBattleId, Is.EqualTo(9001ul));
            Assert.That(_client.RefreshQueued, Is.False);

            _client.RefreshActivities(); _net.Reply(TrialFixture(Lobby(3, 1)));
            _client.RespondTrialInvite(77, true);
            int sent = _net.Calls.Count;
            var tip = new TipInfoMessage { Id = (uint)guild_error.KGuildTrialTeamInvalid };
            tip.Parameters.Add("in_battle"); tip.Parameters.Add("3");
            _net.Reply(new RespondGuildTrialInviteResponse { ErrorMessage = tip,
                Activity = TrialFixture(EndedLobby(Lobby(3, 1), guild_error.KGuildTrialTeamInvalid, "in_battle", "3")).Activities[2] });
            Assert.That(_client.Activities.Activities[2].TrialLobby.State, Is.EqualTo(GuildTrialLobbyState.Ended));
            Assert.That(_client.Status, Is.EqualTo("历练未能开战：道友 · 3 正在战斗中。"));
            Assert.That(_net.Calls.Count, Is.EqualTo(sent));
            Assert.That(_client.RefreshQueued, Is.False);
            Assert.That(_client.RequiresReconnect, Is.False);
        }
        /// <summary>被邀请人说"不"是婉拒,发起人说"不"是取消;之后的重拉按房间里记的结束原因写(是"你"做的)。</summary>
        [Test] public void DecliningOrCancellingATrialInviteSaysWhichOneItWas()
        {
            Load(); _client.RefreshActivities(); _net.Reply(TrialFixture(Lobby(3, 1, 5)));
            _client.RespondTrialInvite(77, false);
            Assert.That(((RespondGuildTrialInviteRequest)_net.Requests.Last()).Accept, Is.False);
            var declined = TrialFixture(EndedLobby(Lobby(3, 1, 5), guild_error.KGuildTrialInviteDeclined, "1"));
            _net.Reply(new RespondGuildTrialInviteResponse { Activity = declined.Activities[2] });
            Assert.That(_client.Status, Is.EqualTo("已婉拒同道历练邀请。"));
            _client.RefreshActivities(); _net.Reply(declined.Clone());
            Assert.That(_client.Status, Is.EqualTo("你婉拒了同道历练邀请。"));

            _client.RefreshActivities(); _net.Reply(TrialFixture(Lobby(1, 3, 5)));
            _client.RespondTrialInvite(77, false);
            var cancelled = TrialFixture(EndedLobby(Lobby(1, 3, 5), guild_error.KGuildTrialInviteDeclined, "1"));
            _net.Reply(new RespondGuildTrialInviteResponse { Activity = cancelled.Activities[2] });
            Assert.That(_client.Status, Is.EqualTo("已取消同道历练邀请。"));
            _client.RefreshActivities(); _net.Reply(cancelled.Clone());
            Assert.That(_client.Status, Is.EqualTo("你取消了同道历练。"));
        }
        /// <summary>
        /// 发起人点"取消邀请"时房间可能已经不等人了:最后一票刚到(正在开战 / 已开战,开战完成后才给发起人推送,
        /// 这段时间他的按钮还是"取消邀请"),或者别人先一步婉拒、邀请已过期。服务端对"本人早已同意"的应答不看 accept,
        /// 按成功回视图 —— 房间并不是被这一下取消的。提示按回包里的房间写(与随后重拉的默认文案同一句),不说"已取消";
        /// 回包带了视图,不再多拉一次。回包没带回这个房间时无从判断,才用固定提示。
        /// </summary>
        [Test] public void CancellingATrialThatAlreadyMovedOnReportsTheLobbyInstead()
        {
            const string cancelledText = "已取消同道历练邀请。";
            Load();
            void CancelAndGet(GuildTrialLobbyView answer, ulong myBattle, string expected)
            {
                _client.RefreshActivities(); _net.Reply(TrialFixture(Lobby(1, 3, 5)));
                _client.RespondTrialInvite(77, false);
                Assert.That(((RespondGuildTrialInviteRequest)_net.Requests.Last()).Accept, Is.False);
                int sent = _net.Calls.Count;
                var view = TrialFixture(answer).Activities[2]; view.MyTrialBattleId = myBattle;
                _net.Reply(new RespondGuildTrialInviteResponse { Activity = view });
                Assert.That(_client.Status, Is.EqualTo(expected));
                Assert.That(_client.Status, Is.Not.EqualTo(cancelledText));
                Assert.That(_net.Calls.Count, Is.EqualTo(sent));
                Assert.That(_client.RefreshQueued, Is.False);
                Assert.That(_client.RequiresReconnect, Is.False);
            }
            GuildTrialLobbyView Agreed(GuildTrialLobbyState state)
            {
                var lobby = Lobby(1, 3, 5); lobby.AcceptedPlayerIds.Add(3); lobby.AcceptedPlayerIds.Add(5);
                lobby.State = state;
                return lobby;
            }
            CancelAndGet(Agreed(GuildTrialLobbyState.Launching), 0, "全员已同意，正在开战…");
            Assert.That(_client.Activities.Activities[2].TrialLobby.State, Is.EqualTo(GuildTrialLobbyState.Launching));
            var launched = Agreed(GuildTrialLobbyState.Launched); launched.BattleId = 9001;
            CancelAndGet(launched, 0, "同道历练已开启。");
            CancelAndGet(launched.Clone(), 9001, "同道历练进行中。");
            // 别人先一步婉拒 / 邀请已过期 / 全员同意后开战失败:写真正的解散原因。
            CancelAndGet(EndedLobby(Lobby(1, 3, 5), guild_error.KGuildTrialInviteDeclined, "5"), 0, "道友 · 5 婉拒了同道历练邀请。");
            CancelAndGet(EndedLobby(Lobby(1, 3, 5), guild_error.KGuildTrialInviteExpired), 0, "邀请已过期，有同道未及时响应。");
            CancelAndGet(EndedLobby(Lobby(1, 3, 5), guild_error.KGuildTrialTeamInvalid, "offline", "5"), 0, "历练未能开战：道友 · 5 当前不在线。");
            // 记了"被婉拒"却没记是谁(参数缺失):不能认定是本人这一下,用不指名的说法。
            CancelAndGet(EndedLobby(Lobby(1, 3, 5), guild_error.KGuildTrialInviteDeclined), 0, "同道历练邀请已被婉拒或取消。");

            // 回包里是另一个房间(没带回本人应答的那个):无从判断,回落到固定提示。
            _client.RefreshActivities(); _net.Reply(TrialFixture(Lobby(1, 3, 5)));
            _client.RespondTrialInvite(77, false);
            var other = Lobby(5, 1); other.LobbyId = 78;
            _net.Reply(new RespondGuildTrialInviteResponse { Activity = TrialFixture(other).Activities[2] });
            Assert.That(_client.Status, Is.EqualTo(cancelledText));
        }
        /// <summary>
        /// 房间解散的六种原因(proto 列出的 end_tip_id)在状态栏各有一句完整的话;别人的操作经推送触发的重拉,默认文案就是它。
        /// 通用的 tip 文案是对"我这次操作"说的,这里说的往往是别人(次数用完的是发起人),所以有几个码换了说法。
        /// </summary>
        [TestCase((uint)guild_error.KGuildTrialInviteExpired, "", "邀请已过期，有同道未及时响应。")]
        [TestCase((uint)guild_error.KGuildTrialInviteDeclined, "5", "道友 · 5 婉拒了同道历练邀请。")]
        [TestCase((uint)guild_error.KGuildTrialInviteDeclined, "3", "道友 · 3 取消了同道历练。")]
        [TestCase((uint)guild_error.KGuildTrialInviteDeclined, "", "同道历练邀请已被婉拒或取消。")]
        [TestCase((uint)guild_error.KGuildTrialTeamInvalid, "offline,5", "历练未能开战：道友 · 5 当前不在线。")]
        [TestCase((uint)guild_error.KGuildTrialTeamInvalid, "invalid,0", "历练未能开战：队伍信息无效，请刷新后重试。")]
        [TestCase((uint)guild_error.KGuildTrialServiceBusy, "", "历练服务繁忙，未能开战，请稍后再试。")]
        [TestCase((uint)guild_error.KGuildActivityAlreadyClaimed, "", "发起人今日次数已满，历练未能开战。")]
        [TestCase((uint)guild_error.KGuildActivityNotOpen, "", "活动已关闭，历练未能开战。")]
        [TestCase(0u, "", "同道历练邀请已结束。")]
        public void EndedLobbyExplainsItselfInTheStatusBar(uint endTip, string parameters, string expected)
        {
            Load();
            var lobby = Lobby(3, 1, 5); lobby.State = GuildTrialLobbyState.Ended; lobby.EndTipId = endTip;
            foreach (string parameter in parameters.Split(',')) if (parameter.Length > 0) lobby.EndParameters.Add(parameter);
            _client.RefreshActivities(); _net.Reply(TrialFixture(lobby));
            Assert.That(_client.Status, Is.EqualTo(expected));
            Assert.That(_client.LobbyEndText(lobby), Is.EqualTo(expected));
        }
        /// <summary>
        /// 邀请房间到期没有推送(服务端在读取时按截止时刻折算):等待确认的房间过了 expire_at_ms、"正在开战"的过了
        /// 开战窗口(视图生成时刻 + 5 秒),快照就算过时,由窗口排一次重拉。已开战 / 已解散的房间没有截止时刻。
        /// </summary>
        [Test] public void TrialLobbyDeadlinesExpireTheSnapshot()
        {
            long clock = 0;
            var net = new GuildFakeTransport();
            using var client = new GuildClient(net, null, () => clock);
            client.Refresh(); net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            var pending = Lobby(1, 3); pending.ExpireAtMs = 5_030_000;
            var first = TrialFixture(pending);
            foreach (var view in first.Activities) { view.ServerTimeMs = 5_000_000; view.NextResetMs = 9_000_000; }
            client.RefreshActivities(); net.Reply(first);
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.False);
            clock += 29_999;
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.False);
            clock += 1;
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.True);

            var launching = Lobby(1, 3); launching.AcceptedPlayerIds.Add(3);
            launching.State = GuildTrialLobbyState.Launching; launching.ExpireAtMs = 5_030_000;
            var second = TrialFixture(launching);
            foreach (var view in second.Activities) { view.ServerTimeMs = 5_030_000; view.NextResetMs = 9_000_000; }
            client.RefreshActivities(); net.Reply(second);
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.False);
            clock += 4_999;
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.False);
            clock += 1;
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.True);

            var ended = TrialFixture(EndedLobby(Lobby(1, 3), guild_error.KGuildTrialInviteExpired));
            foreach (var view in ended.Activities) { view.ServerTimeMs = 5_035_000; view.NextResetMs = 9_000_000; }
            client.RefreshActivities(); net.Reply(ended);
            clock += 1_000_000;
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.False);
            // 没带服务端时刻的样例不按时间判。
            client.RefreshActivities(); net.Reply(TrialFixture(Lobby(1, 3)));
            Assert.That(client.ActivitiesNeedReload(client.ActivityServerNowMs), Is.False);
        }
        /// <summary>
        /// 帮会窗口这次登录还没开过时没有帮会快照(窗口关着不拉),这正是被邀请人的常态:邀请照样记下,并先补拉帮会。
        /// 第一次读到帮会快照会按"换了帮会"清活动状态,属于这个帮会的邀请不能跟着丢;落定的是别的帮会才作废。
        /// </summary>
        [Test] public void InvitePushBeforeTheGuildIsLoadedStillOpensTheInvite()
        {
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 555, Kind = GuildChangeKind.ActivityChanged, ActorPlayerId = 3, TargetPlayerId = 1 });
            Assert.That(_client.TrialInvitePending, Is.True);
            Assert.That(_client.RefreshQueued, Is.True);
            Assert.That(_client.Status, Does.Contain("历练邀请"));
            // 没有帮会快照时活动视图排不上(也发不出去),等快照落定后由窗口进页的自动拉取补上。
            _client.QueueActivities();
            Assert.That(_client.ActivitiesQueued, Is.False);
            _client.DrainQueued(false, true);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
            _net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            Assert.That(_client.TrialInvitePending, Is.True);
            _client.ConsumeTrialInvite();
            Assert.That(_client.TrialInvitePending, Is.False);

            _client.Reset();
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 999, Kind = GuildChangeKind.ActivityChanged, TargetPlayerId = 1 });
            Assert.That(_client.TrialInvitePending, Is.True);
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            Assert.That(_client.TrialInvitePending, Is.False);

            // 不是定向给本人的活动推送:没有快照时与本人无关。
            _client.Reset();
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C
                { GuildId = 555, Kind = GuildChangeKind.ActivityChanged, TargetPlayerId = 3 });
            Assert.That(_client.TrialInvitePending, Is.False);
            Assert.That(_client.RefreshQueued, Is.False);
        }
        /// <summary>
        /// 界面会推迟打开活动页(战斗中、别的界面开着、正在打字)。推迟太久邀请早已过期、服务端连房间记录都不留了,
        /// 标志自己失效,不在几分钟后凭空弹出一个空页面;活动视图的重拉照常排着。
        /// </summary>
        [Test] public void TrialInviteStopsAskingToOpenThePageOnceItIsLongExpired()
        {
            long clock = 0;
            var net = new GuildFakeTransport();
            using var client = new GuildClient(net, null, () => clock);
            client.Refresh(); net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            var invite = new GuildChangedS2C { GuildId = 555, Kind = GuildChangeKind.ActivityChanged, ActorPlayerId = 3, TargetPlayerId = 1 };
            net.Push(MessageIds.NotifyGuildChanged, invite);
            Assert.That(client.TrialInvitePending, Is.True);
            clock += GuildClient.TrialInviteNoticeMs;
            Assert.That(client.TrialInvitePending, Is.True);
            clock += 1;
            Assert.That(client.TrialInvitePending, Is.False);
            Assert.That(client.ActivitiesQueued, Is.True);
            // 新的邀请重新计时。
            net.Push(MessageIds.NotifyGuildChanged, invite);
            Assert.That(client.TrialInvitePending, Is.True);
        }
        /// <summary>
        /// 战斗结束时(界面层按战斗层的显隐通知):快照里有在途的历练 —— 进行中的对局,或还没解散的房间 —— 就标记过时,
        /// 下次进活动页重拉(结算推送可能丢,也可能晚到)。没有在途历练的普通战斗不白拉。
        /// </summary>
        [Test] public void BattleEndMarksActivitiesStaleOnlyWhenATrialWasInFlight()
        {
            Load();
            _client.NoteBattleEnded();
            LoadActivities();
            _client.NoteBattleEnded();
            Assert.That(_client.ActivitiesNeedReload(0), Is.False);

            var launched = Lobby(3, 1); launched.AcceptedPlayerIds.Add(1);
            launched.State = GuildTrialLobbyState.Launched; launched.BattleId = 9001;
            var fighting = TrialFixture(launched); fighting.Activities[2].MyTrialBattleId = 9001;
            _client.RefreshActivities(); _net.Reply(fighting);
            Assert.That(_client.Status, Is.EqualTo("同道历练进行中。"));
            Assert.That(_client.ActivitiesNeedReload(0), Is.False);
            _client.NoteBattleEnded();
            Assert.That(_client.ActivitiesNeedReload(0), Is.True);

            _client.RefreshActivities(); _net.Reply(TrialFixture(EndedLobby(Lobby(3, 1), guild_error.KGuildTrialInviteExpired)));
            Assert.That(_client.ActivitiesNeedReload(0), Is.False);
            _client.NoteBattleEnded();
            Assert.That(_client.ActivitiesNeedReload(0), Is.False);
            // 开战前没来得及重拉、快照还停在"等待确认":同样算在途。
            _client.RefreshActivities(); _net.Reply(TrialFixture(Lobby(3, 1)));
            _client.NoteBattleEnded();
            Assert.That(_client.ActivitiesNeedReload(0), Is.True);
        }

        private void LoadActivities() { _client.RefreshActivities(); _net.Reply(ActivitiesFixture()); }
        /// <summary>点过灯之后服务端回的灯会视图:本人今日 1 / 1,本期 3 / 3 达阈值、资金已发。</summary>
        public static GuildActivityView LitLantern()
        {
            var view = ActivitiesFixture().Activities[0];
            view.MyUsedCount = 1; view.Progress = 3; view.ThresholdReached = true; view.FundsGranted = true;
            view.BlockedTipId = (uint)guild_error.KGuildActivityAlreadyClaimed;
            return view;
        }
        /// <summary>
        /// 与 GuildActivity 默认三行同形(开发期常开,服务端 data/GuildActivity.xlsx):灯会本期已点 2 / 3、本人未点;
        /// 团圆合格在线 2 / 3 未齐;历练在 B6a 被服务端固定为"未开放"。不带服务端时刻(不按时间判过期)。
        /// </summary>
        public static GetGuildActivitiesResponse ActivitiesFixture()
        {
            var response = new GetGuildActivitiesResponse();
            response.Activities.Add(new GuildActivityView { ActivityId = 1, Type = GuildActivityType.Lantern, Name = "元宵灯会",
                State = GuildActivityState.Open, MinGuildLevel = 1, PersonalContribution = 20, GuildFunds = 500, GuildThreshold = 3,
                DailyLimit = 1, Progress = 2 });
            var reunion = new GuildActivityView { ActivityId = 2, Type = GuildActivityType.Reunion, Name = "中秋团圆",
                State = GuildActivityState.Open, MinGuildLevel = 1, PersonalContribution = 30, GuildThreshold = 3,
                DailyLimit = 1, Progress = 2, BlockedTipId = (uint)guild_error.KGuildActivityThresholdNotReached };
            reunion.RewardItems.Add(new GuildRewardItem { ItemId = 1, Count = 4 });
            response.Activities.Add(reunion);
            var trial = new GuildActivityView { ActivityId = 3, Type = GuildActivityType.Trial, Name = "同道历练",
                State = GuildActivityState.Disabled, MinGuildLevel = 1, PersonalContribution = 50, GuildFunds = 300, GuildThreshold = 3,
                DailyLimit = 2, DungeonId = 1, TeamSizeMin = 2, TeamSizeMax = 5,
                BlockedTipId = (uint)guild_error.KGuildActivityNotOpen };
            trial.RewardItems.Add(new GuildRewardItem { ItemId = 2, Count = 1 });
            response.Activities.Add(trial);
            return response;
        }
        /// <summary>
        /// 历练已开放的活动快照(服务端 B6b 落地后的样子):本人今日未参与、可发起,队伍 2..teamMax 人(含发起人)。
        /// lobby 非空时把它挂成本人所在的邀请房间。
        /// </summary>
        public static GetGuildActivitiesResponse TrialFixture(GuildTrialLobbyView lobby = null, uint teamMax = 5)
        {
            var response = ActivitiesFixture();
            var trial = response.Activities[2];
            trial.State = GuildActivityState.Open; trial.BlockedTipId = 0; trial.TeamSizeMax = teamMax;
            if (lobby != null) trial.TrialLobby = lobby;
            return response;
        }
        /// <summary>等待确认的邀请房间(编号 77):发起人在名单首位、建房即同意,其余人尚未应答。不带截止时刻。</summary>
        public static GuildTrialLobbyView Lobby(ulong initiator, params ulong[] invited)
        {
            var lobby = new GuildTrialLobbyView { LobbyId = 77, InitiatorPlayerId = initiator, State = GuildTrialLobbyState.Pending };
            lobby.MemberPlayerIds.Add(initiator); lobby.AcceptedPlayerIds.Add(initiator);
            foreach (ulong id in invited) lobby.MemberPlayerIds.Add(id);
            return lobby;
        }
        /// <summary>把一个房间改成已解散,原因用 tip 码与它的参数表达(同服务端 end_tip_id / end_parameters)。</summary>
        public static GuildTrialLobbyView EndedLobby(GuildTrialLobbyView lobby, guild_error reason, params string[] parameters)
        {
            lobby.State = GuildTrialLobbyState.Ended; lobby.EndTipId = (uint)reason;
            foreach (string parameter in parameters) lobby.EndParameters.Add(parameter);
            return lobby;
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

        // ── 活动页（B6a-cli）──────────────────────────────────────────

        /// <summary>
        /// 打开窗口那一发 GetPlayerGuild 还在路上(Busy)时进活动页:读取只是排队,所以照样触发一次,
        /// 页面写"正在读取";同一次进入不连发。
        /// </summary>
        [Test] public void ActivitiesPageQueuesLoadEvenWhenBusy()
        {
            int requests = 0; _window.ActivitiesRequested += () => requests++;
            _client.Refresh();
            Assert.That(_client.Busy, Is.True);
            _window.SetClient(_client); _window.Show(GuildPage.Activities);
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(_window.ShowingActivities, Is.True);
            Assert.That(NamedText("GuildActivityPlaceholder"), Is.EqualTo("正在读取帮会活动…"));
            _window.SetClient(_client); _window.Tick();
            Assert.That(requests, Is.EqualTo(1));
            // 自动拉过却仍没有快照(本用例没把事件接到客户端):不再挂着"正在读取",请玩家点刷新。
            _net.Reply(new GetPlayerGuildResponse { Guild = GuildClientTests.Fixture() });
            _window.SetClient(_client);
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(NamedText("GuildActivityPlaceholder"), Is.EqualTo("点击右下角刷新读取帮会活动。"));
            // 离开再进算一次新的进入。
            _window.Show(GuildPage.Overview);
            Assert.That(_window.ShowingActivities, Is.False);
            _window.Show(GuildPage.Activities);
            Assert.That(requests, Is.EqualTo(2));
        }
        /// <summary>推送已经排过队(由 DrainQueued 发出)时,进页不再重复触发事件。</summary>
        [Test] public void ActivitiesPageDoesNotRequestTwiceWhenAPushAlreadyQueuedIt()
        {
            int requests = 0; _window.ActivitiesRequested += () => requests++;
            _net.Push(MessageIds.NotifyGuildChanged, new GuildChangedS2C { GuildId = 555, Kind = GuildChangeKind.ActivityChanged });
            Assert.That(_client.ActivitiesQueued, Is.True);
            _window.SetClient(_client); _window.Show(GuildPage.Activities);
            Assert.That(requests, Is.Zero);
            Assert.That(NamedText("GuildActivityPlaceholder"), Is.EqualTo("正在读取帮会活动…"));
            _client.DrainQueued(_window.ShowingApplications, _window.ShowingActivities);
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetGuildActivities));
        }
        [Test] public void ActivitiesPageOutsideGuildOffersRanking()
        {
            int requests = 0; _window.ActivitiesRequested += () => requests++;
            _client.Reset();
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } });
            _window.SetClient(_client); _window.Show(GuildPage.Activities);
            Assert.That(requests, Is.Zero);
            Assert.That(NamedText("GuildActivityPlaceholder"), Is.EqualTo("加入帮会后可参与帮会活动。"));
            Assert.That(Buttons().Any(b => b.name.StartsWith("GuildActivityAction_")), Is.False);
            Click("BrowseGuildsFromActivities");
            Assert.That(_window.Page, Is.EqualTo(GuildPage.Ranking));
        }
        /// <summary>三张卡片全部由视图驱动:名称、状态、进度、奖励、我的次数与按钮;不再有"暂未开放"的占位按钮。</summary>
        [Test] public void ActivitiesPageRendersDataDrivenPanels()
        {
            int requests = 0; _window.ActivitiesRequested += () => requests++;
            uint lantern = 0, reunion = 0;
            _window.LanternRequested += id => lantern = id; _window.ReunionRequested += id => reunion = id;
            LoadActivities(GuildClientTests.ActivitiesFixture());
            // 已有新鲜快照:进页不自动拉。
            Assert.That(requests, Is.Zero);
            for (int type = 1; type <= 3; type++)
                Assert.That(Buttons().Count(b => b.name == "GuildActivityAction_" + type), Is.EqualTo(1));
            Assert.That(Buttons().Any(b => b.name.StartsWith("GuildUnavailable_")), Is.False);
            Assert.That(ActiveText(), Does.Contain("本期点灯 2 / 3"));
            Assert.That(NamedText("GuildActivityName_1"), Is.EqualTo("中秋团圆"));
            Assert.That(NamedText("GuildActivityState_0"), Is.EqualTo("常开"));
            Assert.That(NamedText("GuildActivityState_2"), Is.EqualTo("未开放"));
            Assert.That(NamedText("GuildActivityProgress_1"), Is.EqualTo("同时在线 2 / 3"));
            Assert.That(NamedText("GuildActivityProgress_2"), Is.EqualTo("今日资金胜场 0 / 3"));
            Assert.That(NamedText("GuildActivityReward_0"), Is.EqualTo("帮贡 +20 · 达成后帮会资金 +500"));
            Assert.That(NamedText("GuildActivityReward_1"), Is.EqualTo("帮贡 +30 · 物品 #1×4"));
            Assert.That(NamedText("GuildActivityReward_2"), Is.EqualTo("帮贡 +50 · 胜利帮会资金 +300 · 物品 #2×1"));
            Assert.That(NamedText("GuildActivityMine_0"), Is.EqualTo("今日 0 / 1"));
            Assert.That(NamedText("GuildActivityMine_2"), Is.EqualTo("今日 0 / 2"));
            // 样例不带服务端时刻:只写规则,不编一个倒计时。
            Assert.That(NamedText("GuildActivityReset"), Is.EqualTo("每日 05:00 重置参与次数。"));
            Assert.That(Label("GuildActivityAction_1"), Is.EqualTo("点亮花灯"));
            Assert.That(Buttons().Single(b => b.name == "GuildActivityAction_1").interactable, Is.True);
            // 团圆人数未齐、历练未开放:按钮说明原因且点不动。
            Assert.That(Label("GuildActivityAction_2"), Is.EqualTo("人数未齐"));
            Assert.That(Buttons().Single(b => b.name == "GuildActivityAction_2").interactable, Is.False);
            Assert.That(Label("GuildActivityAction_3"), Is.EqualTo("未开放"));
            Assert.That(Buttons().Single(b => b.name == "GuildActivityAction_3").interactable, Is.False);
            // 没有花费,不加确认框;带出的是视图里的 activity_id。
            Click("GuildActivityAction_1");
            Assert.That(_window.ModalVisible, Is.False);
            Assert.That(lantern, Is.EqualTo(1u));

            var ready = GuildClientTests.ActivitiesFixture();
            ready.Activities[1].Progress = 3; ready.Activities[1].BlockedTipId = 0;
            LoadActivities(ready);
            Assert.That(Label("GuildActivityAction_2"), Is.EqualTo("领取团圆礼"));
            Click("GuildActivityAction_2");
            Assert.That(reunion, Is.EqualTo(2u));
        }
        /// <summary>服务端少下发一种活动(配表没有可见行):按类型落位,缺的那张卡写"暂无活动",后面的卡不错位。</summary>
        [Test] public void MissingActivityTypeKeepsItsCardAsUnavailable()
        {
            var partial = GuildClientTests.ActivitiesFixture();
            partial.Activities.RemoveAt(0);
            LoadActivities(partial);
            Assert.That(Label("GuildActivityAction_1"), Is.EqualTo("暂无活动"));
            Assert.That(Buttons().Single(b => b.name == "GuildActivityAction_1").interactable, Is.False);
            Assert.That(NamedText("GuildActivityName_0"), Is.EqualTo("元宵灯会"));
            Assert.That(NamedText("GuildActivityName_1"), Is.EqualTo("中秋团圆"));
            Assert.That(NamedText("GuildActivityProgress_1"), Is.EqualTo("同时在线 2 / 3"));
            Assert.That(Label("GuildActivityAction_3"), Is.EqualTo("未开放"));
        }
        /// <summary>
        /// 历练按钮的五种状态(06 §6.38),按顺序取第一条:进行中 / 取消邀请 / 响应邀请 / 等待同道(含正在开战)/ 组队历练。
        /// 前四种先于 blocked_tip_id;第五种不可参与时写原因。"我的状态"的第二行写在途状态的短结论。
        /// 每一例都重新进一次活动页(Hide 会清掉"已弹过邀请框"的记录,与玩家重开窗口一致)。
        /// </summary>
        [Test] public void TrialButtonStates()
        {
            ulong responded = 0; bool accepted = true; int responses = 0;
            _window.TrialInviteResponded += (id, ok) => { responded = id; accepted = ok; responses++; };
            void Enter(GetGuildActivitiesResponse activities) { _window.Hide(); LoadActivities(activities); }
            Button TrialAction() => Buttons().Single(b => b.name == "GuildActivityAction_3");

            // ① 本人有进行中的历练对局:只读。
            var launched = GuildClientTests.Lobby(1, 3); launched.AcceptedPlayerIds.Add(3);
            launched.State = GuildTrialLobbyState.Launched; launched.BattleId = 9001;
            var fighting = GuildClientTests.TrialFixture(launched); fighting.Activities[2].MyTrialBattleId = 9001;
            Enter(fighting);
            Assert.That(Label("GuildActivityAction_3"), Is.EqualTo("历练进行中"));
            Assert.That(TrialAction().interactable, Is.False);
            Assert.That(NamedText("GuildActivityMine_2"), Is.EqualTo("今日 0 / 2\n历练进行中"));

            // ② 等待确认、本人是发起人:取消邀请,直接发出,带的是画按钮时看到的房间。
            Enter(GuildClientTests.TrialFixture(GuildClientTests.Lobby(1, 3, 5)));
            Assert.That(Label("GuildActivityAction_3"), Is.EqualTo("取消邀请"));
            Assert.That(TrialAction().interactable, Is.True);
            Assert.That(NamedText("GuildActivityMine_2"), Is.EqualTo("今日 0 / 2\n等待同道确认 1/3"));
            Assert.That(_window.ModalVisible, Is.False);
            Click("GuildActivityAction_3");
            Assert.That(responses, Is.EqualTo(1));
            Assert.That(responded, Is.EqualTo(77ul));
            Assert.That(accepted, Is.False);

            // ③ 等待确认、本人被邀请还没应答:响应邀请(邀请框已自动弹出);今日次数已满也照样能应答。
            var invited = GuildClientTests.TrialFixture(GuildClientTests.Lobby(3, 1, 5));
            invited.Activities[2].MyUsedCount = 2; invited.Activities[2].BlockedTipId = (uint)guild_error.KGuildActivityAlreadyClaimed;
            Enter(invited);
            Assert.That(Label("GuildActivityAction_3"), Is.EqualTo("响应邀请"));
            Assert.That(NamedText("GuildActivityMine_2"), Is.EqualTo("今日 2 / 2\n收到邀请，待你响应"));
            Assert.That(_window.ModalVisible, Is.True);

            // ④ 本人已同意:等待同道;全员已同意、正在开战:正在开战。都点不动,也不弹邀请框。
            var waiting = GuildClientTests.Lobby(3, 1, 5); waiting.AcceptedPlayerIds.Add(1);
            Enter(GuildClientTests.TrialFixture(waiting));
            Assert.That(Label("GuildActivityAction_3"), Is.EqualTo("等待同道"));
            Assert.That(TrialAction().interactable, Is.False);
            Assert.That(NamedText("GuildActivityMine_2"), Is.EqualTo("今日 0 / 2\n等待同道确认 2/3"));
            Assert.That(_window.ModalVisible, Is.False);
            var launching = GuildClientTests.Lobby(3, 1); launching.AcceptedPlayerIds.Add(1);
            launching.State = GuildTrialLobbyState.Launching;
            Enter(GuildClientTests.TrialFixture(launching));
            Assert.That(Label("GuildActivityAction_3"), Is.EqualTo("正在开战"));
            Assert.That(TrialAction().interactable, Is.False);
            Assert.That(NamedText("GuildActivityMine_2"), Is.EqualTo("今日 0 / 2\n正在开战…"));

            // ⑤ 其余:可参与时组队历练(打开选人框);不可参与时写原因。
            Enter(GuildClientTests.TrialFixture());
            Assert.That(NamedText("GuildActivityState_2"), Is.EqualTo("常开"));
            Assert.That(Label("GuildActivityAction_3"), Is.EqualTo("组队历练"));
            Assert.That(TrialAction().interactable, Is.True);
            Assert.That(NamedText("GuildActivityMine_2"), Is.EqualTo("今日 0 / 2"));
            Click("GuildActivityAction_3");
            Assert.That(Buttons().Any(b => b.name == "ConfirmGuildTrial"), Is.True);
            var used = GuildClientTests.TrialFixture();
            used.Activities[2].MyUsedCount = 2; used.Activities[2].BlockedTipId = (uint)guild_error.KGuildActivityAlreadyClaimed;
            Enter(used);
            Assert.That(Label("GuildActivityAction_3"), Is.EqualTo("今日次数已满"));
            Assert.That(NamedText("GuildActivityMine_2"), Is.EqualTo("今日 2 / 2"));
            Assert.That(TrialAction().interactable, Is.False);
            Enter(GuildClientTests.ActivitiesFixture());
            Assert.That(Label("GuildActivityAction_3"), Is.EqualTo("未开放"));
            Assert.That(TrialAction().interactable, Is.False);
            // 房间显示"已开战"但本人没有进行中的对局(战斗已结束,或集结失败):可以再发起。
            var over = GuildClientTests.Lobby(1, 3); over.AcceptedPlayerIds.Add(3);
            over.State = GuildTrialLobbyState.Launched; over.BattleId = 9001;
            Enter(GuildClientTests.TrialFixture(over));
            Assert.That(Label("GuildActivityAction_3"), Is.EqualTo("组队历练"));
            Assert.That(NamedText("GuildActivityMine_2"), Is.EqualTo("今日 0 / 2\n历练已开启"));
            Assert.That(responses, Is.EqualTo(1));
        }
        [Test] public void LanternButtonDisabledAfterClaim()
        {
            _window.LanternRequested += id => _client.LightLantern(id);
            LoadActivities(GuildClientTests.ActivitiesFixture());
            Click("GuildActivityAction_1");
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.LightGuildLantern));
            // 请求在途:所有活动按钮点不动,不会连点两次。
            _window.SetClient(_client);
            Assert.That(Buttons().Single(b => b.name == "GuildActivityAction_1").interactable, Is.False);
            _net.Reply(new LightGuildLanternResponse { Activity = GuildClientTests.LitLantern() });
            _window.SetClient(_client);
            Assert.That(Label("GuildActivityAction_1"), Is.EqualTo("今日已点灯"));
            Assert.That(Buttons().Single(b => b.name == "GuildActivityAction_1").interactable, Is.False);
            Assert.That(NamedText("GuildActivityMine_0"), Is.EqualTo("今日 1 / 1"));
            Assert.That(NamedText("GuildActivityProgress_0"), Is.EqualTo("本期点灯 3 / 3"));
            Assert.That(NamedText("GuildActivityReward_0"), Is.EqualTo("帮贡 +20 · 帮会资金 +500 已入库"));
        }
        /// <summary>背包满时物品奖励保持待发放:不是错误,"我的状态"写原因与件数;永久拒绝只在没有待发放时提示。</summary>
        [Test] public void PendingRewardShowsBagFullReason()
        {
            var activities = GuildClientTests.ActivitiesFixture();
            var reunion = activities.Activities[1];
            reunion.MyUsedCount = 1; reunion.ThresholdReached = true; reunion.Progress = 0;
            reunion.BlockedTipId = (uint)guild_error.KGuildActivityAlreadyClaimed;
            reunion.MyPendingRewardCount = 1; reunion.MyPendingReasonTipId = GuildAssetReasons.BagFull;
            LoadActivities(activities);
            Assert.That(NamedText("GuildActivityMine_1"), Is.EqualTo("今日 1 / 1 · 背包已满，腾出空间后自动发放(1)"));
            Assert.That(NamedText("GuildActivityProgress_1"), Is.EqualTo("本期已团圆"));
            Assert.That(Label("GuildActivityAction_2"), Is.EqualTo("今日已领取"));
            Assert.That(_client.RequiresReconnect, Is.False);

            var rejected = GuildClientTests.ActivitiesFixture();
            rejected.Activities[1].MyUsedCount = 1; rejected.Activities[1].MyLastRewardRejectTipId = GuildAssetReasons.Blocked;
            LoadActivities(rejected);
            Assert.That(NamedText("GuildActivityMine_1"), Is.EqualTo("今日 1 / 1 · 上次物品发放失败"));
        }
        /// <summary>开放状态与不可参与的按钮标签逐种核对(纯函数,不建界面)。时间按 UTC+8 显示。</summary>
        [Test] public void ActivityStateAndBlockedLabelsCoverEveryCase()
        {
            // 2026-03-05 05:00 (UTC+8) = 2026-03-04 21:00 UTC。
            ulong when = (ulong)new DateTimeOffset(2026, 3, 4, 21, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
            var view = new GuildActivityView { Type = GuildActivityType.Lantern, State = GuildActivityState.Open, MinGuildLevel = 1 };
            Assert.That(GuildWindow.ActivityStateText(view, 5), Is.EqualTo("常开"));
            view.StartAtMs = when - 86400000; view.EndAtMs = when;
            Assert.That(GuildWindow.ActivityStateText(view, 5), Is.EqualTo("至 03-05 05:00 结束"));
            view.MinGuildLevel = 6;
            Assert.That(GuildWindow.ActivityStateText(view, 5), Is.EqualTo("进行中 · 需帮会 6 级"));
            view.State = GuildActivityState.Upcoming; view.StartAtMs = when;
            Assert.That(GuildWindow.ActivityStateText(view, 5), Is.EqualTo("03-05 05:00 开启"));
            view.State = GuildActivityState.Ended;
            Assert.That(GuildWindow.ActivityStateText(view, 5), Is.EqualTo("已结束"));
            view.State = GuildActivityState.Disabled;
            Assert.That(GuildWindow.ActivityStateText(view, 5), Is.EqualTo("未开放"));
            // 配表把时间填坏也不让界面抛异常。
            view.State = GuildActivityState.Upcoming; view.StartAtMs = ulong.MaxValue;
            Assert.That(GuildWindow.ActivityStateText(view, 5), Is.EqualTo("-- 开启"));
            // "9999-12-31 23:59:59 UTC"(常被拿来表示长期开放)本身还在 DateTimeOffset 的范围内,加上 8 小时的显示偏移才越界:同样不抛。
            const ulong endOfTimeUtc = 253402300799000;
            view.StartAtMs = endOfTimeUtc;
            Assert.That(GuildWindow.ActivityStateText(view, 5), Is.EqualTo("-- 开启"));
            view.State = GuildActivityState.Open; view.MinGuildLevel = 1; view.StartAtMs = when; view.EndAtMs = endOfTimeUtc;
            Assert.That(GuildWindow.ActivityStateText(view, 5), Is.EqualTo("至 -- 结束"));
            // 边界:加完偏移正好落在 9999-12-31 23:59:59.999(UTC+8)的那一毫秒还能显示,再晚一毫秒就写"--"。
            view.EndAtMs = 253402271999999;
            Assert.That(GuildWindow.ActivityStateText(view, 5), Is.EqualTo("至 12-31 23:59 结束"));
            view.EndAtMs = 253402272000000;
            Assert.That(GuildWindow.ActivityStateText(view, 5), Is.EqualTo("至 -- 结束"));

            string Blocked(guild_error tip, GuildActivityState state) => GuildWindow.ActivityBlockedLabel(
                new GuildActivityView { BlockedTipId = (uint)tip, State = state }, "今日已点灯");
            Assert.That(Blocked(guild_error.KGuildActivityAlreadyClaimed, GuildActivityState.Open), Is.EqualTo("今日已点灯"));
            Assert.That(Blocked(guild_error.KGuildActivityNotOpen, GuildActivityState.Disabled), Is.EqualTo("未开放"));
            Assert.That(Blocked(guild_error.KGuildActivityNotOpen, GuildActivityState.Upcoming), Is.EqualTo("尚未开启"));
            Assert.That(Blocked(guild_error.KGuildActivityNotOpen, GuildActivityState.Ended), Is.EqualTo("已结束"));
            Assert.That(Blocked(guild_error.KGuildActivityLevelTooLow, GuildActivityState.Open), Is.EqualTo("帮会等级不足"));
            Assert.That(Blocked(guild_error.KGuildActivityJoinTooRecent, GuildActivityState.Open), Is.EqualTo("入帮时间不足"));
            Assert.That(Blocked(guild_error.KGuildActivityThresholdNotReached, GuildActivityState.Open), Is.EqualTo("人数未齐"));
            // 不认识的原因码(服务端日后新增):按钮仍然点不动,给一个不误导的说法。
            Assert.That(Blocked(guild_error.KGuildBusyRetry, GuildActivityState.Open), Is.EqualTo("暂不可参与"));
        }
        [Test] public void ActivitiesRefreshButtonReloadsOnlyThisPage()
        {
            int activities = 0, guild = 0;
            _window.ActivitiesRequested += () => activities++; _window.RefreshRequested += () => guild++;
            LoadActivities(GuildClientTests.ActivitiesFixture());
            Click("RefreshGuild");
            Assert.That(activities, Is.EqualTo(1));
            Assert.That(guild, Is.Zero);
        }
        /// <summary>
        /// 倒计时以视图的 server_time_ms 为基准、按流逝时间往前走(Tick 只改这一行文字);停在本页跨过 05:00 时
        /// 自动排一次重拉,拉回来之前不连发,离开活动页后 Tick 不再管它。
        /// </summary>
        [Test] public void ActivitiesPageCountsDownAndReloadsPastDailyReset()
        {
            long clock = 0;
            var net = new GuildFakeTransport();
            using var client = new GuildClient(net, null, () => clock);
            client.Refresh(); net.Reply(new GetPlayerGuildResponse { Guild = GuildClientTests.Fixture() });
            var activities = GuildClientTests.ActivitiesFixture();
            foreach (var view in activities.Activities) { view.ServerTimeMs = 1_000_000_000; view.NextResetMs = 1_000_000_000 + 200 * 60000; }
            client.RefreshActivities(); net.Reply(activities);
            int requests = 0; _window.ActivitiesRequested += () => requests++;
            _window.SetClient(client); _window.Show(GuildPage.Activities);
            Assert.That(NamedText("GuildActivityReset"), Is.EqualTo("每日 05:00 重置 · 距下次重置 3 小时 20 分"));
            clock += 30 * 60000; _window.Tick();
            Assert.That(NamedText("GuildActivityReset"), Is.EqualTo("每日 05:00 重置 · 距下次重置 2 小时 50 分"));
            Assert.That(requests, Is.Zero);
            clock += 170 * 60000; _window.Tick();
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(NamedText("GuildActivityReset"), Does.Contain("已到重置时间"));
            _window.Tick();
            Assert.That(requests, Is.EqualTo(1));
            _window.Show(GuildPage.Overview); _window.Tick();
            Assert.That(requests, Is.EqualTo(1));
            // 交还 SetUp 的客户端:TearDown 里窗口不再碰这个已释放的替身。
            _window.SetClient(_client);
        }
        /// <summary>
        /// 活动页所有文字按 30 号正文核框(设计稿的 24–27 号字格子会被 GuildUiArt.Text 抬到 30):单行的框高够一行、
        /// 字形总宽不超框宽(否则被省略号截掉);两行的(奖励 / 我的状态)排版后总高不超框高。取各格最长的现实文案。
        /// </summary>
        [Test] public void ActivityPageTextBoxesFitAtBodySize()
        {
            long clock = 0;
            var net = new GuildFakeTransport();
            using var client = new GuildClient(net, null, () => clock);
            var info = GuildClientTests.Fixture();
            client.Refresh(); net.Reply(new GetPlayerGuildResponse { Guild = info });
            var activities = GuildClientTests.ActivitiesFixture();
            ulong now = 1_800_000_000_000;
            foreach (var view in activities.Activities) { view.ServerTimeMs = now; view.NextResetMs = now + (23 * 60 + 59) * 60000ul; }
            var lantern = activities.Activities[0];
            lantern.StartAtMs = now - 86400000; lantern.EndAtMs = now + 86400000;
            lantern.PersonalContribution = 120; lantern.GuildFunds = 120000; lantern.GuildThreshold = 100; lantern.Progress = 100;
            lantern.ThresholdReached = true; lantern.FundsGranted = true; lantern.MyUsedCount = 1;
            lantern.BlockedTipId = (uint)guild_error.KGuildActivityAlreadyClaimed;
            var reunion = activities.Activities[1];
            reunion.GuildFunds = 120000; reunion.GuildThreshold = 100; reunion.Progress = 99; reunion.MyUsedCount = 1;
            reunion.RewardItems.Add(new GuildRewardItem { ItemId = 1001, Count = 20 });
            reunion.RewardItems.Add(new GuildRewardItem { ItemId = 1002, Count = 5 });
            // 最长的暂时原因:战斗中暂不结算。
            reunion.MyPendingRewardCount = 16; reunion.MyPendingReasonTipId = GuildAssetReasons.InBattle;
            var trial = activities.Activities[2];
            trial.State = GuildActivityState.Open; trial.EndAtMs = now + 86400000; trial.MinGuildLevel = 10;
            trial.BlockedTipId = (uint)guild_error.KGuildActivityLevelTooLow;
            trial.GuildFunds = 120000; trial.GuildThreshold = 10; trial.Progress = 10; trial.DailyLimit = 10; trial.MyUsedCount = 10;
            trial.MyLastRewardRejectTipId = GuildAssetReasons.Blocked;
            client.RefreshActivities(); net.Reply(activities);
            _window.SetClient(client); _window.Show(GuildPage.Activities);
            Assert.That(NamedText("GuildActivityState_2"), Is.EqualTo("进行中 · 需帮会 10 级"));

            var checkedNames = AssertBodyLinesFit();
            foreach (string name in new[] { "GuildActivityReset", "GuildActivityState_0", "GuildActivityProgress_2", "GuildActivityFooter" })
                Assert.That(checkedNames, Has.Member(name));
            int wrapped = 0;
            foreach (var text in _root.GetComponentsInChildren<TMP_Text>().Where(t => t.name.StartsWith("GuildActivity")))
            {
                var rect = text.rectTransform.rect;
                if (text.textWrappingMode == TextWrappingModes.NoWrap)
                    Assert.That(text.GetPreferredValues(text.text).x, Is.LessThanOrEqualTo(rect.width), text.name + " / " + text.text);
                else
                {
                    wrapped++;
                    Assert.That(text.GetPreferredValues(text.text, rect.width, 0).y, Is.LessThanOrEqualTo(rect.height), text.name + " / " + text.text);
                }
            }
            // 三张卡片各有奖励与我的状态两格。
            Assert.That(wrapped, Is.EqualTo(6));
            _window.SetClient(_client);
        }
        /// <summary>活动快照还没拉到时被隔离(请求超时,待重新登录):不挂"正在读取",写恢复提示。</summary>
        [Test] public void ActivitiesPageShowsRecoveryWhileQuarantined()
        {
            int requests = 0; _window.ActivitiesRequested += () => requests++;
            _client.RefreshActivities(); _net.Error("rpc timeout");
            Assert.That(_client.RequiresReconnect, Is.True);
            _window.SetClient(_client); _window.Show(GuildPage.Activities);
            Assert.That(NamedText("GuildActivityPlaceholder"), Is.EqualTo(GuildClient.RecoveryMessage));
            Assert.That(ActiveText(), Does.Not.Contain("正在读取"));
            Assert.That(requests, Is.Zero);
        }

        // ── 同道历练：选人框与邀请框（B6b-cli）──────────────────────────

        /// <summary>
        /// 选人框:候选是在线且不是自己的成员(按编号升序);队伍上限 3 人时只能再选 2 位,选满后再点别人不生效;
        /// 发出时自己在首位、其余按点选顺序,带的是活动 id。
        /// </summary>
        [Test] public void TrialPickerLimitsSelectionAndRaisesEvent()
        {
            uint activity = 0; IReadOnlyList<ulong> roster = null; int raised = 0;
            _window.TrialRequested += (id, members) => { activity = id; roster = members; raised++; };
            LoadActivities(GuildClientTests.TrialFixture(teamMax: 3));
            Click("GuildActivityAction_3");
            Assert.That(_window.ModalVisible, Is.True);
            // 样例里 1、3、5、7 在线,自己是 1。
            var candidates = Buttons().Select(b => b.name).Where(n => n.StartsWith("GuildTrialMember_")).ToArray();
            Assert.That(candidates, Is.EqualTo(new[] { "GuildTrialMember_3", "GuildTrialMember_5", "GuildTrialMember_7" }));
            Assert.That(ActiveText(), Does.Contain("选择 1–2 位在线同道"));
            Assert.That(Label("GuildTrialMember_3"), Is.EqualTo("道友 · 3"));
            // 还没选人:人数不够,不能发出。
            Assert.That(Buttons().Single(b => b.name == "ConfirmGuildTrial").interactable, Is.False);
            Click("GuildTrialMember_5"); Click("GuildTrialMember_3"); Click("GuildTrialMember_7");
            Assert.That(ActiveText(), Does.Contain("已选 2 位"));
            Assert.That(Buttons().Single(b => b.name == "ConfirmGuildTrial").interactable, Is.True);
            // 换人要先点掉一个。
            Click("GuildTrialMember_5"); Click("GuildTrialMember_7");
            Click("ConfirmGuildTrial");
            Assert.That(raised, Is.EqualTo(1));
            Assert.That(activity, Is.EqualTo(3u));
            Assert.That(roster.ToArray(), Is.EqualTo(new ulong[] { 1, 3, 7 }));
            Assert.That(_window.ModalVisible, Is.False);
            // 取消 / 重新打开都从空名单开始。
            Click("GuildActivityAction_3");
            Assert.That(ActiveText(), Does.Contain("已选 0 位"));
            Click("GuildTrialMember_3"); Click("CancelGuildTrial");
            Assert.That(_window.ModalVisible, Is.False);
            Assert.That(raised, Is.EqualTo(1));
            // 没有在线同道:说明原因,发不出去。
            var alone = GuildClientTests.Fixture();
            foreach (var member in alone.Members) member.Online = member.PlayerId == 1;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = alone }); _window.SetClient(_client);
            Click("GuildActivityAction_3");
            Assert.That(NamedText("GuildTrialPickerEmpty"), Does.Contain("暂无在线同道"));
            Assert.That(Buttons().Single(b => b.name == "ConfirmGuildTrial").interactable, Is.False);
        }
        /// <summary>
        /// 选人框开着时:请求在途(推送触发的重拉)就发不出去,"发出邀请"置灰,硬点也不关框、不丢已选的人;
        /// 成员快照换过(有人下线)时,确认前按最新名单筛一遍,人少了先重画给玩家看,不直接发一份他没看过的名单。
        /// 翻页保留已选。
        /// </summary>
        [Test] public void TrialPickerWaitsWhileBusyAndDropsMembersWhoLeft()
        {
            int raised = 0; IReadOnlyList<ulong> roster = null;
            _window.TrialRequested += (id, members) => { raised++; roster = members; };
            LoadActivities(GuildClientTests.TrialFixture());
            Click("GuildActivityAction_3"); Click("GuildTrialMember_3"); Click("GuildTrialMember_5");
            _client.Refresh(); _window.SetClient(_client);
            Assert.That(Buttons().Single(b => b.name == "ConfirmGuildTrial").interactable, Is.False);
            Click("ConfirmGuildTrial");
            Assert.That(raised, Is.Zero);
            Assert.That(_window.ModalVisible, Is.True);
            var info = GuildClientTests.Fixture(); info.Members[4].Online = false;
            _net.Reply(new GetPlayerGuildResponse { Guild = info }); _window.SetClient(_client);
            Assert.That(Buttons().Single(b => b.name == "ConfirmGuildTrial").interactable, Is.True);
            Click("ConfirmGuildTrial");
            Assert.That(raised, Is.Zero);
            Assert.That(Buttons().Any(b => b.name == "GuildTrialMember_5"), Is.False);
            Assert.That(ActiveText(), Does.Contain("已选 1 位"));
            Click("ConfirmGuildTrial");
            Assert.That(raised, Is.EqualTo(1));
            Assert.That(roster.ToArray(), Is.EqualTo(new ulong[] { 1, 3 }));

            // 在线同道多于一页(6 人):翻页后再选,前一页选的人还在。
            var crowded = GuildClientTests.Fixture();
            for (ulong i = 8; i <= 16; i++) crowded.Members.Add(new GuildMember { PlayerId = i, Online = true });
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = crowded }); _window.SetClient(_client);
            Click("GuildActivityAction_3");
            Assert.That(ActiveText(), Does.Contain("（第 1/2 页）"));
            Assert.That(Buttons().Count(b => b.name.StartsWith("GuildTrialMember_")), Is.EqualTo(GuildWindow.TrialMembersPerPage));
            Assert.That(Buttons().Single(b => b.name == "GuildTrialPrevious").interactable, Is.False);
            Click("GuildTrialMember_3"); Click("GuildTrialNext");
            Assert.That(ActiveText(), Does.Contain("（第 2/2 页）"));
            Assert.That(Buttons().Single(b => b.name == "GuildTrialNext").interactable, Is.False);
            Click("GuildTrialMember_16"); Click("ConfirmGuildTrial");
            Assert.That(raised, Is.EqualTo(2));
            Assert.That(roster.ToArray(), Is.EqualTo(new ulong[] { 1, 3, 16 }));
        }
        /// <summary>
        /// 被邀请且未应答:进活动页即弹邀请框(谁邀请、队伍几人);同一个房间只自动弹一次,重画不重复弹;
        /// 玩家按 Esc 关掉后可从卡片的"响应邀请"再打开;点同意带出的是打开时看到的房间 id。
        /// </summary>
        [Test] public void TrialInviteModalShownOnceAndResponds()
        {
            ulong lobby = 0; bool accepted = false; int responses = 0;
            _window.TrialInviteResponded += (id, ok) => { lobby = id; accepted = ok; responses++; };
            LoadActivities(GuildClientTests.TrialFixture(GuildClientTests.Lobby(3, 1, 5)));
            Assert.That(_window.ModalVisible, Is.True);
            Assert.That(Buttons().Count(b => b.name == "AcceptGuildTrial"), Is.EqualTo(1));
            Assert.That(Buttons().Count(b => b.name == "DeclineGuildTrial"), Is.EqualTo(1));
            Assert.That(ActiveText(), Does.Contain("道友 · 3 邀请你同往历练（队伍共 3 人）。"));
            Assert.That(ActiveText(), Does.Not.Contain("次数已满"));
            // 样例不带服务端时刻:不编倒计时。
            Assert.That(NamedText("GuildTrialInviteCountdown"), Is.EqualTo("请尽快响应，邀请超时后自动失效。"));
            _window.SetClient(_client);
            Assert.That(Buttons().Count(b => b.name == "AcceptGuildTrial"), Is.EqualTo(1));
            _window.Back();
            Assert.That(_window.ModalVisible, Is.False);
            Assert.That(_window.IsVisible, Is.True);
            _window.SetClient(_client); _window.Tick();
            Assert.That(_window.ModalVisible, Is.False);
            Assert.That(responses, Is.Zero);
            Click("GuildActivityAction_3");
            Assert.That(_window.ModalVisible, Is.True);
            Click("AcceptGuildTrial");
            Assert.That(responses, Is.EqualTo(1));
            Assert.That(lobby, Is.EqualTo(77ul));
            Assert.That(accepted, Is.True);
            Assert.That(_window.ModalVisible, Is.False);
        }
        /// <summary>
        /// 邀请框跟着房间走:今日次数已满的人也能应答(框里说清"胜利不再得奖");请求在途时两个按钮置灰,硬点不关框也不发事件;
        /// 房间不再等本人应答(发起人取消了)时框自己关掉,卡片写短结论、状态栏写完整的一句;
        /// 换了一个新房间再弹一次,应答带的是新房间的 id。
        /// </summary>
        [Test] public void TrialInviteModalFollowsTheLobby()
        {
            int responses = 0; ulong responded = 0; bool accepted = true;
            _window.TrialInviteResponded += (id, ok) => { responses++; responded = id; accepted = ok; };
            var invited = GuildClientTests.TrialFixture(GuildClientTests.Lobby(3, 1));
            invited.Activities[2].MyUsedCount = 2; invited.Activities[2].BlockedTipId = (uint)guild_error.KGuildActivityAlreadyClaimed;
            LoadActivities(invited);
            Assert.That(ActiveText(), Does.Contain("你今日次数已满，胜利不再得奖。"));
            _client.Refresh(); _window.SetClient(_client);
            Assert.That(Buttons().Single(b => b.name == "AcceptGuildTrial").interactable, Is.False);
            Assert.That(Buttons().Single(b => b.name == "DeclineGuildTrial").interactable, Is.False);
            Click("AcceptGuildTrial");
            Assert.That(responses, Is.Zero);
            Assert.That(_window.ModalVisible, Is.True);
            _net.Reply(new GetPlayerGuildResponse { Guild = GuildClientTests.Fixture() }); _window.SetClient(_client);
            Assert.That(Buttons().Single(b => b.name == "AcceptGuildTrial").interactable, Is.True);

            _client.RefreshActivities();
            _net.Reply(GuildClientTests.TrialFixture(GuildClientTests.EndedLobby(GuildClientTests.Lobby(3, 1),
                guild_error.KGuildTrialInviteDeclined, "3")));
            _window.SetClient(_client);
            Assert.That(_window.ModalVisible, Is.False);
            Assert.That(NamedText("GuildActivityMine_2"), Is.EqualTo("今日 0 / 2\n邀请已取消"));
            Assert.That(_client.Status, Is.EqualTo("道友 · 3 取消了同道历练。"));
            Assert.That(Label("GuildActivityAction_3"), Is.EqualTo("组队历练"));

            var again = GuildClientTests.Lobby(5, 1); again.LobbyId = 78;
            _client.RefreshActivities(); _net.Reply(GuildClientTests.TrialFixture(again)); _window.SetClient(_client);
            Assert.That(_window.ModalVisible, Is.True);
            Assert.That(ActiveText(), Does.Contain("道友 · 5 邀请你同往历练（队伍共 2 人）。"));
            Click("DeclineGuildTrial");
            Assert.That(responses, Is.EqualTo(1));
            Assert.That(responded, Is.EqualTo(78ul));
            Assert.That(accepted, Is.False);
        }
        /// <summary>
        /// 邀请框的倒计时按房间的 expire_at_ms 与估算的服务端时刻走(Tick 只改这一行字)。到点时服务端不推送:
        /// 窗口自动排一次重拉,回来之前不连发;重拉回来房间已过期,邀请框关掉,卡片与状态栏说明原因,快照不再判过时。
        /// </summary>
        [Test] public void TrialInviteCountsDownAndClosesWhenTheLobbyExpires()
        {
            long clock = 0;
            var net = new GuildFakeTransport();
            using var client = new GuildClient(net, null, () => clock);
            client.Refresh(); net.Reply(new GetPlayerGuildResponse { Guild = GuildClientTests.Fixture() });
            ulong now = 1_000_000_000, reset = now + 200 * 60000;
            var lobby = GuildClientTests.Lobby(3, 1); lobby.ExpireAtMs = now + 30_000;
            var activities = GuildClientTests.TrialFixture(lobby);
            foreach (var view in activities.Activities) { view.ServerTimeMs = now; view.NextResetMs = reset; }
            client.RefreshActivities(); net.Reply(activities);
            int requests = 0; _window.ActivitiesRequested += () => requests++;
            _window.SetClient(client); _window.Show(GuildPage.Activities);
            Assert.That(NamedText("GuildTrialInviteCountdown"), Is.EqualTo("请在 30 秒内响应，超时邀请自动失效。"));
            clock += 12_500; _window.Tick();
            Assert.That(NamedText("GuildTrialInviteCountdown"), Is.EqualTo("请在 18 秒内响应，超时邀请自动失效。"));
            Assert.That(requests, Is.Zero);
            clock += 17_500; _window.Tick();
            Assert.That(NamedText("GuildTrialInviteCountdown"), Is.EqualTo("邀请已到期，正在确认…"));
            Assert.That(requests, Is.EqualTo(1));
            _window.Tick();
            Assert.That(requests, Is.EqualTo(1));

            var expired = GuildClientTests.TrialFixture(GuildClientTests.EndedLobby(GuildClientTests.Lobby(3, 1),
                guild_error.KGuildTrialInviteExpired));
            foreach (var view in expired.Activities) { view.ServerTimeMs = now + 30_100; view.NextResetMs = reset; }
            client.RefreshActivities(); net.Reply(expired); _window.SetClient(client);
            Assert.That(_window.ModalVisible, Is.False);
            Assert.That(NamedText("GuildActivityMine_2"), Is.EqualTo("今日 0 / 2\n邀请已过期"));
            Assert.That(client.Status, Is.EqualTo("邀请已过期，有同道未及时响应。"));
            _window.Tick();
            Assert.That(requests, Is.EqualTo(1));
            // 交还 SetUp 的客户端:TearDown 里窗口不再碰这个已释放的替身。
            _window.SetClient(_client);
        }
        /// <summary>
        /// 邀请框什么时候不弹:不在活动页(由 GuildUiRoot 把窗口带过来)、别的弹窗开着(等它关掉,由 Tick 补弹)、
        /// 已隔离(应答发不出去)。
        /// </summary>
        [Test] public void TrialInviteWaitsForTheActivitiesPageAndForOtherModals()
        {
            _client.RefreshActivities(); _net.Reply(GuildClientTests.TrialFixture(GuildClientTests.Lobby(3, 1)));
            _window.SetClient(_client); _window.Show(GuildPage.Overview);
            Assert.That(_window.ModalVisible, Is.False);
            _window.Show(GuildPage.Activities);
            Assert.That(Buttons().Any(b => b.name == "AcceptGuildTrial"), Is.True);

            _window.Hide();
            LoadActivities(GuildClientTests.TrialFixture());
            Click("GuildActivityAction_3");
            _client.RefreshActivities(); _net.Reply(GuildClientTests.TrialFixture(GuildClientTests.Lobby(3, 1)));
            _window.SetClient(_client);
            Assert.That(Buttons().Any(b => b.name == "ConfirmGuildTrial"), Is.True);
            Assert.That(Buttons().Any(b => b.name == "AcceptGuildTrial"), Is.False);
            Click("CancelGuildTrial");
            Assert.That(_window.ModalVisible, Is.False);
            _window.Tick();
            Assert.That(Buttons().Any(b => b.name == "AcceptGuildTrial"), Is.True);

            _window.Hide();
            _client.Refresh(); _net.Error("rpc timeout");
            Assert.That(_client.RequiresReconnect, Is.True);
            _window.SetClient(_client); _window.Show(GuildPage.Activities);
            Assert.That(_window.ModalVisible, Is.False);
            Assert.That(Buttons().Single(b => b.name == "GuildActivityAction_3").interactable, Is.False);
        }
        /// <summary>
        /// 历练新增的文字按 30 号正文(按钮 32 号)核框:卡片第二行的各种短结论不折成第三行;状态栏的完整原因在 1660 宽以内;
        /// 邀请框的说明两行以内、倒计时一行;选人框的说明、空态与成员按钮(名字最长 12 个字;取名失败的兜底名带 18–20 位编号)不被截断。
        /// </summary>
        [Test] public void TrialTextBoxesFitAtBodySize()
        {
            const string longName = "名字最长是十二个汉字的人";
            var info = GuildClientTests.Fixture();
            info.Members[2].Name = longName; info.Members[4].Name = longName;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = info });
            Assert.That(longName.Length, Is.EqualTo(12));
            var lobbies = new List<GuildTrialLobbyView>();
            var hosting = GuildClientTests.Lobby(1, 2, 3, 4, 5); lobbies.Add(hosting);
            lobbies.Add(GuildClientTests.Lobby(3, 1, 5, 6, 7));
            var launching = GuildClientTests.Lobby(3, 1); launching.AcceptedPlayerIds.Add(1);
            launching.State = GuildTrialLobbyState.Launching; lobbies.Add(launching);
            var launched = GuildClientTests.Lobby(3, 1); launched.AcceptedPlayerIds.Add(1);
            launched.State = GuildTrialLobbyState.Launched; lobbies.Add(launched);
            lobbies.Add(GuildClientTests.EndedLobby(GuildClientTests.Lobby(3, 1), guild_error.KGuildTrialInviteDeclined, "5"));
            lobbies.Add(GuildClientTests.EndedLobby(GuildClientTests.Lobby(3, 1), guild_error.KGuildTrialInviteDeclined, "3"));
            lobbies.Add(GuildClientTests.EndedLobby(GuildClientTests.Lobby(3, 1), guild_error.KGuildTrialInviteExpired));
            lobbies.Add(GuildClientTests.EndedLobby(GuildClientTests.Lobby(3, 1), guild_error.KGuildTrialTeamInvalid, "not_ready", "3"));
            lobbies.Add(GuildClientTests.EndedLobby(GuildClientTests.Lobby(3, 1), guild_error.KGuildTrialServiceBusy));
            lobbies.Add(GuildClientTests.EndedLobby(GuildClientTests.Lobby(3, 1), guild_error.KGuildActivityAlreadyClaimed));
            lobbies.Add(GuildClientTests.EndedLobby(GuildClientTests.Lobby(3, 1), guild_error.KGuildActivityNotOpen));
            lobbies.Add(GuildClientTests.EndedLobby(GuildClientTests.Lobby(3, 1), guild_error.KGuildBusyRetry));
            foreach (var lobby in lobbies)
            {
                _window.Hide();
                var activities = GuildClientTests.TrialFixture(lobby);
                activities.Activities[2].DailyLimit = 10; activities.Activities[2].MyUsedCount = 10;
                LoadActivities(activities);
                var mine = _root.GetComponentsInChildren<TMP_Text>().Single(t => t.name == "GuildActivityMine_2");
                var rect = mine.rectTransform.rect;
                string[] lines = mine.text.Split('\n');
                Assert.That(lines.Length, Is.EqualTo(2), mine.text);
                foreach (string line in lines)
                    Assert.That(mine.GetPreferredValues(line).x, Is.LessThanOrEqualTo(rect.width), line);
                Assert.That(mine.GetPreferredValues(mine.text, rect.width, 0).y, Is.LessThanOrEqualTo(rect.height), mine.text);
                // 状态栏与这一格同字体同字号(30 号正文),借它量完整原因的宽度。
                Assert.That(mine.GetPreferredValues(_client.Status).x, Is.LessThanOrEqualTo(1660f), _client.Status);
                if (!_window.ModalVisible) continue;
                // 邀请框(本人被邀请的那几例):说明换行、倒计时一行。
                AssertModalTextFits();
            }

            // 邀请框:最长的说明(12 个字的名字 + 次数已满的提醒)与带数字的倒计时。
            long clock = 0;
            var net = new GuildFakeTransport();
            using var client = new GuildClient(net, null, () => clock);
            client.Refresh(); net.Reply(new GetPlayerGuildResponse { Guild = info });
            var invite = GuildClientTests.Lobby(3, 1, 5, 6, 7); invite.ExpireAtMs = 1_000_030_000;
            var invited = GuildClientTests.TrialFixture(invite);
            foreach (var view in invited.Activities) { view.ServerTimeMs = 1_000_000_000; view.NextResetMs = 1_000_000_000 + 200 * 60000; }
            invited.Activities[2].MyUsedCount = 2; invited.Activities[2].BlockedTipId = (uint)guild_error.KGuildActivityAlreadyClaimed;
            client.RefreshActivities(); net.Reply(invited);
            _window.Hide(); _window.SetClient(client); _window.Show(GuildPage.Activities);
            Assert.That(_window.ModalVisible, Is.True);
            Assert.That(ActiveText(), Does.Contain(longName + " 邀请你同往历练（队伍共 5 人）。你今日次数已满，胜利不再得奖。"));
            Assert.That(NamedText("GuildTrialInviteCountdown"), Does.StartWith("请在 30 秒内响应"));
            AssertModalTextFits();
            Assert.That(AssertBodyLinesFit(), Has.Member("GuildTrialInviteCountdown"));
            clock += 60_000; _window.Tick();
            AssertModalTextFits();
            _window.Hide(); _window.SetClient(_client);

            // 选人框:两页、选满时的说明;成员按钮上 12 个字的名字;翻页与底部按钮。
            var crowded = info.Clone();
            for (ulong i = 8; i <= 16; i++) crowded.Members.Add(new GuildMember { PlayerId = i, Online = true, Name = longName });
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = crowded });
            LoadActivities(GuildClientTests.TrialFixture());
            Click("GuildActivityAction_3");
            Click("GuildTrialMember_3"); Click("GuildTrialMember_5"); Click("GuildTrialMember_7"); Click("GuildTrialMember_8");
            Assert.That(ActiveText(), Does.Contain("已选 4 位。（第 1/2 页）"));
            AssertModalTextFits();
            foreach (var button in Buttons().Where(b => b.name.StartsWith("GuildTrial") || b.name.EndsWith("GuildTrial")))
            {
                var caption = button.GetComponentInChildren<TMP_Text>();
                var box = caption.rectTransform.rect;
                Assert.That(caption.GetPreferredValues(caption.text).x, Is.LessThanOrEqualTo(box.width), button.name + " / " + caption.text);
                var face = caption.font.faceInfo;
                float line = (face.ascentLine - face.descentLine) * face.scale * caption.fontSize / face.pointSize;
                Assert.That(box.height, Is.GreaterThanOrEqualTo(line), button.name);
            }
            // 取名失败的兜底名"道友 · 编号":存量 snowflake 号是 18–19 位,ulong 的上限是 20 位。成员按钮缩到正文下限 30 号时
            // 整串都要放得下(按钮的伪粗体放不下,所以兜底名用正文字重),编号的尾巴不能被省略号截掉。
            _window.Hide();
            var legacy = GuildClientTests.Fixture();
            ulong[] longIds = { 281253138764136448, 9223372036854775807, ulong.MaxValue };
            foreach (ulong id in longIds) legacy.Members.Add(new GuildMember { PlayerId = id, Online = true });
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = legacy });
            LoadActivities(GuildClientTests.TrialFixture());
            Click("GuildActivityAction_3");
            foreach (ulong id in longIds)
            {
                var caption = Buttons().Single(b => b.name == "GuildTrialMember_" + id).GetComponentInChildren<TMP_Text>();
                Assert.That(caption.text, Is.EqualTo("道友 · " + id));
                Assert.That(caption.fontSizeMin, Is.GreaterThanOrEqualTo(30f));
                Assert.That(WidthAtSmallestSize(caption), Is.LessThanOrEqualTo(caption.rectTransform.rect.width), caption.text);
            }
            // 没有在线同道时的空态一行。
            _window.Hide();
            var alone = GuildClientTests.Fixture();
            foreach (var member in alone.Members) member.Online = member.PlayerId == 1;
            _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = alone });
            LoadActivities(GuildClientTests.TrialFixture());
            Click("GuildActivityAction_3");
            AssertModalTextFits();
            Assert.That(AssertBodyLinesFit(), Has.Member("GuildTrialPickerEmpty"));
        }
        /// <summary>
        /// 一行文字"缩到允许的最小字号时"的宽度:比框宽还大,才会被省略号截断。自动缩字的文字,GetPreferredValues 按上限字号量,
        /// 而字宽与字距都随字号等比变化,所以按比例折到下限;没开自动缩字的就是当前字号下的宽度。
        /// </summary>
        private static float WidthAtSmallestSize(TMP_Text text)
        {
            float width = text.GetPreferredValues(text.text).x;
            return text.enableAutoSizing ? width * text.fontSizeMin / text.fontSizeMax : width;
        }
        /// <summary>
        /// 弹窗里的正文(GuildModalFrame 下、不在按钮上的文字):单行的字形总宽不超框宽,换行的排版总高不超框高。
        /// 单行的框高由 AssertBodyLinesFit 统一核。
        /// </summary>
        private void AssertModalTextFits()
        {
            var frame = _root.GetComponentsInChildren<RectTransform>().Single(r => r.name == "GuildModalFrame");
            int checkedTexts = 0;
            foreach (var text in frame.GetComponentsInChildren<TMP_Text>())
            {
                if (string.IsNullOrEmpty(text.text) || text.GetComponentInParent<Button>() != null) continue;
                var rect = text.rectTransform.rect;
                if (text.textWrappingMode == TextWrappingModes.NoWrap)
                    Assert.That(text.GetPreferredValues(text.text).x, Is.LessThanOrEqualTo(rect.width), text.text);
                else
                    Assert.That(text.GetPreferredValues(text.text, rect.width, 0).y, Is.LessThanOrEqualTo(rect.height), text.text);
                checkedTexts++;
            }
            // 至少有标题与说明两段。
            Assert.That(checkedTexts, Is.GreaterThanOrEqualTo(2));
        }
        /// <summary>拉一份活动快照并进入活动页(本类的用例没把 Changed 接到窗口,所以手动 SetClient)。</summary>
        private void LoadActivities(GetGuildActivitiesResponse activities)
        {
            _client.RefreshActivities(); _net.Reply(activities);
            _window.SetClient(_client); _window.Show(GuildPage.Activities);
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
