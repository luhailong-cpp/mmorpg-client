using Guildpb;
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
    public sealed class GuildRenameClientTests
    {
        private GuildFakeTransport _net;
        private GuildClient _client;

        [SetUp]
        public void SetUp()
        {
            _net = new GuildFakeTransport();
            _client = new GuildClient(_net);
            _client.Refresh();
            _net.Reply(new GetPlayerGuildResponse { Guild = GuildClientTests.Fixture() });
        }

        [TearDown]
        public void TearDown()
        {
            _client.Dispose();
        }

        [Test]
        public void TipTextCoversContentSafetyAndRenameGuildErrors()
        {
            Assert.That(GuildClient.TipText(new TipInfoMessage { Id = (uint)guild_error.KGuildNameSensitive }),
                Does.Contain("不允许使用"));
            Assert.That(GuildClient.TipText(new TipInfoMessage { Id = (uint)guild_error.KGuildAnnouncementSensitive }),
                Does.Contain("不允许使用"));
            Assert.That(GuildClient.TipText(new TipInfoMessage { Id = (uint)guild_error.KGuildTextCheckUnavailable }),
                Does.Contain("安全检测"));
            Assert.That(GuildClient.TipText(new TipInfoMessage { Id = (uint)guild_error.KGuildRenameCooldown }),
                Does.Contain("冷却"));
            Assert.That(GuildClient.TipText(new TipInfoMessage { Id = (uint)guild_error.KGuildRenameSameName }),
                Does.Contain("相同"));
        }

        [Test]
        public void RenameValidatesNameSendsRequestAndAppliesSnapshot()
        {
            int callsBefore = _net.Calls.Count;
            _client.Rename("   ");
            Assert.That(_net.Calls.Count, Is.EqualTo(callsBefore));

            _client.Rename("  新青云门  ");
            Assert.That(_net.Calls.Count, Is.EqualTo(callsBefore + 1));
            Assert.That(_net.Calls[_net.Calls.Count - 1], Is.EqualTo(MessageIds.RenameGuild));
            var sent = _net.Requests[_net.Requests.Count - 1] as RenameGuildRequest;
            Assert.IsNotNull(sent);
            Assert.That(sent.GuildId, Is.EqualTo(555ul));
            Assert.That(sent.Name, Is.EqualTo("新青云门"));

            var updated = GuildClientTests.Fixture();
            updated.Name = "新青云门";
            _net.Reply(new RenameGuildResponse { Guild = updated, NextRenameTime = 1_800_604_800ul });
            Assert.That(_client.Info.Name, Is.EqualTo("新青云门"));
            Assert.That(_client.NextRenameTimeUnixSec, Is.EqualTo(1_800_604_800ul));
        }

        [Test]
        public void RenameCooldownRecordsNextRenameTime()
        {
            _client.Rename("太虚观");
            _net.Reply(new RenameGuildResponse
            {
                ErrorMessage = new TipInfoMessage { Id = (uint)guild_error.KGuildRenameCooldown },
                NextRenameTime = 1_800_604_800ul,
            });
            Assert.That(_client.NextRenameTimeUnixSec, Is.EqualTo(1_800_604_800ul));
            Assert.That(_client.Status, Does.Contain("冷却"));
        }

        [Test]
        public void WindowShowsRenameButtonOnlyForLeaderAndSubmitsTrimmedName()
        {
            var root = new GameObject("GuildRenameWindowTest", typeof(RectTransform));
            try
            {
                var design = QdaoUguiFactory.CreateCenteredRect("Design", root.transform, 2560, 1080);
                var window = new GuildWindow(design);
                window.SetClient(_client);
                window.Show();

                Button[] Buttons() => root.GetComponentsInChildren<Button>(true);
                Button renameBtn = null;
                foreach (var b in Buttons()) if (b.name == "RenameGuild") renameBtn = b;
                Assert.IsNotNull(renameBtn);
                Assert.That(renameBtn.interactable, Is.True);

                string submitted = null;
                window.RenameRequested += n => submitted = n;
                renameBtn.onClick.Invoke();
                Assert.That(window.ModalVisible, Is.True);

                TMP_InputField input = null;
                foreach (var f in root.GetComponentsInChildren<TMP_InputField>(true))
                    if (f.name == "RenameGuildName") input = f;
                Assert.IsNotNull(input);
                input.text = "  云海阁  ";
                Button confirmBtn = null;
                foreach (var b in Buttons()) if (b.name == "ConfirmRenameGuild") confirmBtn = b;
                Assert.IsNotNull(confirmBtn);
                Assert.That(confirmBtn.interactable, Is.True);
                confirmBtn.onClick.Invoke();
                Assert.That(submitted, Is.EqualTo("云海阁"));
                Assert.That(window.ModalVisible, Is.False);

                // Non-leader does not see RenameGuild button.
                var memberClient = new GuildClient(_net);
                var memberInfo = GuildClientTests.Fixture();
                memberInfo.LeaderId = 2;
                memberInfo.Members[0].Role = 0;
                memberClient.Refresh();
                _net.Reply(new GetPlayerGuildResponse { Guild = memberInfo });
                window.SetClient(memberClient);
                renameBtn = null;
                foreach (var b in Buttons()) if (b.name == "RenameGuild") renameBtn = b;
                Assert.IsNull(renameBtn);

                window.Hide();
                memberClient.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
