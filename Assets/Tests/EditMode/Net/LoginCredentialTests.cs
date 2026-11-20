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

        // 持票查询失败后的恢复重入带原票(服务端 open-server-rate-limit-design.md §11.5 第 3 条),否则等于从队尾重排。
        [Test]
        public void AssignGateReentry_CarriesTheHeldQueueToken()
        {
            var body = GameClient.BuildAssignGateRequest(3, "access-token-value", "dev-1", "queue-token-T1");

            Assert.That(body.queue_token, Is.EqualTo("queue-token-T1"));
            Assert.That(JsonUtility.ToJson(body), Does.Contain("\"queue_token\":\"queue-token-T1\""));
        }

        // §11.5 第 2 条:查询连续失败的等待下限依次 2、4、8、16、30 秒,之后保持 30 秒;没失败时是平时的 2 秒。
        [TestCase(0, 2000L)]
        [TestCase(1, 2000L)]
        [TestCase(2, 4000L)]
        [TestCase(3, 8000L)]
        [TestCase(4, 16000L)]
        [TestCase(5, 30000L)]
        [TestCase(6, 30000L)]
        [TestCase(int.MaxValue, 30000L)]
        public void QueueBackoffFloor_DoublesFromTwoSecondsAndCapsAtThirty(int consecutiveFailures, long expectedMs)
        {
            Assert.That(GameClient.QueueBackoffFloorMs(consecutiveFailures), Is.EqualTo(expectedMs));
        }

        // §11.5「未持有 queue_token」:无票 429 依次等 30、60、120 秒(次数上限由调用方的 RateLimitedMaxRetries 管)。
        [TestCase(1, 30000L)]
        [TestCase(2, 60000L)]
        [TestCase(3, 120000L)]
        public void RateLimitedBackoff_Is30Then60Then120Seconds(int retry, long expectedMs)
        {
            Assert.That(GameClient.RateLimitedBackoffMs(retry), Is.EqualTo(expectedMs));
        }
    }
}
