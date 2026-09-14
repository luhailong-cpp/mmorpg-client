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
        [Test] public void UnconfirmedMembershipCannotSendJoin()
        {
            _client.Join(555); Assert.That(_net.Calls, Is.Empty);
        }
        [Test] public void DuplicateJoinIsBlockedAndDoesNotOptimisticallySetMembership()
        {
            Empty(); _client.Join(555); _client.Join(556);
            Assert.That(_net.Calls.Count(x => x == MessageIds.JoinGuild), Is.EqualTo(1));
            Assert.That(_client.Info, Is.Null);
            _net.Reply(new JoinGuildResponse());
            Assert.That(_net.Calls.Last(), Is.EqualTo(MessageIds.GetPlayerGuild));
            Assert.That(_client.Info, Is.Null);
            _net.Reply(new GetPlayerGuildResponse { Guild = Fixture() });
            Assert.That(_client.Info.GuildId, Is.EqualTo(555));
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
        }        private void Empty() { _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildNotInGuild } }); }
        private void Load() { _client.Refresh(); _net.Reply(new GetPlayerGuildResponse { Guild = Fixture() }); }
        public static GuildInfo Fixture()
        {
            var info = new GuildInfo { GuildId = 555, Name = "清风明月", LeaderId = 1, Level = 5, MaxMembers = 50,
                Announcement = "同道相聚，月满灯明。", ZoneId = 1 };
            for (ulong i = 1; i <= 7; i++) info.Members.Add(new GuildMember { PlayerId = i, Role = i == 1 ? 3u : 0u,
                Contribution = 100 + i, Online = i % 2 == 1 });
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
            updated.Members[0].Contribution = 987;
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
        }        [Test] public void GuildArtImportsAsRealSpritesWithTheIntendedInputBorder()
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
        }        [Test] public void ServerStringsAreNotRichText()
        {
            _window.Show(GuildPage.Members);
            Assert.That(_root.GetComponentsInChildren<TMP_Text>(true).All(t => !t.richText), Is.True);
        }
        private Button[] Buttons() => _root.GetComponentsInChildren<Button>().Where(b => b.gameObject.activeInHierarchy).ToArray();
        private void Click(string name) => Buttons().Single(b => b.name == name).onClick.Invoke();
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
        public void Reply(IMessage response) => Pending(response);
        public void Disconnect() { IsReady = false; Disconnected?.Invoke(); }
        public void RegisterNotify(uint id, Action<MessageContent> action) { }
        public void SendOneWay(uint id, IMessage request) { }
        public void Call<T>(uint id, IMessage request, MessageParser<T> parser, Action<T> success, Action<string> error)
            where T : IMessage<T> { Calls.Add(id); Pending = message => success((T)message); Error = error; }
    }
}
