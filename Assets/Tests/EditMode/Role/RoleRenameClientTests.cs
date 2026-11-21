using System.Collections.Generic;
using MmorpgClient.Game;
using MmorpgClient.Game.Role;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Role
{
    public sealed class RoleRenameClientTests
    {
        [Test]
        public void RenameTipTextCoversAllRoleRenameErrors()
        {
            Assert.That(RoleNameRules.RenameTipText((uint)login_error.KRoleNameInvalid, new List<string> { "2", "12" }),
                Is.EqualTo("角色名需为 2–12 个字，仅限汉字、字母与数字"));
            Assert.That(RoleNameRules.RenameTipText((uint)login_error.KRoleNameTaken, null),
                Is.EqualTo("该角色名已被使用，请换一个"));
            Assert.That(RoleNameRules.RenameTipText((uint)login_error.KRoleNameSensitive, null),
                Is.EqualTo("角色名包含不允许使用的词语，请修改"));
            Assert.That(RoleNameRules.RenameTipText((uint)login_error.KRoleNameCheckUnavailable, null),
                Is.EqualTo("角色名安全检测暂时不可用，请稍后重试"));
            Assert.That(RoleNameRules.RenameTipText((uint)login_error.KRoleRenameNoCard, null),
                Is.EqualTo("背包中没有可用的改名卡"));
            Assert.That(RoleNameRules.RenameTipText((uint)login_error.KRoleRenameInProgress, null),
                Is.EqualTo("已有角色改名正在进行中，请稍后再试"));
            Assert.That(RoleNameRules.RenameTipText((uint)login_error.KRoleRenameFrozen, null),
                Is.EqualTo("当前角色状态暂时不能改名，请稍后再试"));
            Assert.That(RoleNameRules.RenameTipText((uint)login_error.KRoleRenameSameName, null),
                Is.EqualTo("新角色名与当前名字相同"));
            Assert.That(RoleNameRules.RenameTipText((uint)login_error.KRoleRenameExpired, null),
                Is.EqualTo("本次改名超时已失效，请重新发起"));

            string cooldownWithTime = RoleNameRules.RenameTipText((uint)login_error.KRoleRenameCooldown, null, 1_800_000_000UL);
            Assert.That(cooldownWithTime, Does.Contain("冷却中").And.Contains("2027-"));
            Assert.That(RoleNameRules.RenameTipText((uint)login_error.KRoleRenameCooldown, null, 0),
                Is.EqualTo("角色改名冷却中，请稍后再试"));
        }

        [Test]
        public void RequestRoleRenameRejectsWhenNotInGameAndPublishesOutcome()
        {
            var client = new GameClient("http://127.0.0.1:1");
            bool eventCalled = false;
            bool eventSuccess = true;
            string eventMessage = null;
            client.OnRoleRenameOutcome += (ok, _, msg, __) =>
            {
                eventCalled = true;
                eventSuccess = ok;
                eventMessage = msg;
            };

            bool callbackSuccess = true;
            string callbackMessage = null;
            bool sent = client.RequestRoleRename("青冥剑仙", (ok, msg) =>
            {
                callbackSuccess = ok;
                callbackMessage = msg;
            });

            Assert.IsFalse(sent);
            Assert.IsFalse(client.RoleRenameInFlight);
            Assert.IsFalse(callbackSuccess);
            Assert.That(callbackMessage, Is.EqualTo("尚未进入游戏，暂时无法改名"));
            Assert.IsTrue(eventCalled);
            Assert.IsFalse(eventSuccess);
            Assert.That(eventMessage, Is.EqualTo(callbackMessage));
        }
    }
}
