using System;
using MmorpgClient.Game;
using MmorpgClient.Net;
using NUnit.Framework;
using UnityEngine;

namespace MmorpgClient.Tests.EditMode.Net
{
    /// <summary>
    /// 登录凭据与登录后的请求(服务端 docs/design/third-party-login-end-to-end-design.md §十一、login-queue-2026-05.md
    /// 「AssignGate 鉴权」):第三方登录只带登录方式与渠道凭证,账号由服务端算出;分配 gate 只带 access token,不带账号名。
    /// </summary>
    public sealed class LoginCredentialTests
    {
        [Test]
        public void PasswordCredential_BuildsPasswordLoginRequest()
        {
            var body = GameClient.BuildHttpLoginRequest(7, LoginCredential.ForPassword("robot_1", "pw"), "dev-1");

            Assert.That(body.zone_id, Is.EqualTo(7u));
            Assert.That(body.auth_type, Is.EqualTo(LoginAuthTypes.Password));
            Assert.That(body.account, Is.EqualTo("robot_1"));
            Assert.That(body.password, Is.EqualTo("pw"));
            Assert.That(body.auth_token, Is.Empty);
            Assert.That(body.device_id, Is.EqualTo("dev-1"));
        }

        [Test]
        public void ThirdPartyCredential_SendsOnlyAuthTypeAndChannelCredential()
        {
            var credential = LoginCredential.ForThirdParty(LoginAuthTypes.WeChatMiniGame, "wx-login-code");
            var body = GameClient.BuildHttpLoginRequest(7, credential, "dev-1");

            Assert.That(credential.IsPassword, Is.False);
            Assert.That(body.auth_type, Is.EqualTo("wechat_minigame"));
            Assert.That(body.auth_token, Is.EqualTo("wx-login-code"));
            Assert.That(body.account, Is.Empty, "账号由服务端从凭证算出,客户端不填");
            Assert.That(body.password, Is.Empty);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(LoginAuthTypes.Password)]
        [TestCase(LoginAuthTypes.AccessToken)]
        public void ThirdPartyCredential_RejectsNonProviderAuthTypes(string authType)
        {
            Assert.Throws<ArgumentException>(() => LoginCredential.ForThirdParty(authType, "token"));
        }

        [TestCase(null)]
        [TestCase("")]
        public void ThirdPartyCredential_RejectsEmptyChannelCredential(string token)
        {
            Assert.Throws<ArgumentException>(() => LoginCredential.ForThirdParty(LoginAuthTypes.Apple, token));
        }

        [Test]
        public void AssignGateRequest_CarriesAccessTokenAndNoAccountName()
        {
            var body = GameClient.BuildAssignGateRequest(3, "access-token-value", "dev-1");
            string json = JsonUtility.ToJson(body);

            Assert.That(body.access_token, Is.EqualTo("access-token-value"));
            Assert.That(body.queue_token, Is.Empty, "首次分配不带 queue_token");
            Assert.That(json, Does.Contain("\"access_token\":\"access-token-value\""));
            Assert.That(json, Does.Not.Contain("\"account\""), "服务端不再接受自报的账号名");
        }

        [Test]
        public void AssignGateRequest_NullTokenSerializesAsEmpty()
        {
            var body = GameClient.BuildAssignGateRequest(3, null, null);

            Assert.That(body.access_token, Is.Empty);
            Assert.That(body.device_id, Is.Empty);
        }

        [Test]
        public void AssignGateAuthRejected_AsksPlayerToLogInAgain()
        {
            string text = GameClient.DescribeAssignCode(new AssignGateResult { code = AssignGateResult.CodeAuthRejected, error = "access token invalid" });

            Assert.That(text, Does.Contain("重新登录"));
        }
    }
}
