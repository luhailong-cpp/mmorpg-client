using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Net
{
    /// <summary>
    /// Apple / Google 原生桥交回 C# 的 JSON(服务端 docs/design/account-deletion.md §3.3):解析与错误码映射是纯函数。
    /// 拿不准就当没登录上:格式不对、ok 却没有令牌、认不出的错误串一律 failed,不抛异常。
    /// </summary>
    public sealed class PlatformSignInTests
    {
        [Test]
        public void AppleSuccess_CarriesIdentityTokenAndAuthorizationCode()
        {
            var r = PlatformSignIn.Parse("{\"ok\":true,\"identity_token\":\"eyJ.apple.token\",\"authorization_code\":\"c0de.0.abc\"}");

            Assert.That(r.Ok, Is.True);
            Assert.That(r.IdentityToken, Is.EqualTo("eyJ.apple.token"));
            Assert.That(r.AuthorizationCode, Is.EqualTo("c0de.0.abc"));
            Assert.That(r.Error, Is.Empty);
        }

        [Test]
        public void GoogleSuccess_HasNoAuthorizationCode()
        {
            var r = PlatformSignIn.Parse("{\"ok\":true,\"identity_token\":\"eyJ.google.token\"}");

            Assert.That(r.Ok, Is.True);
            Assert.That(r.IdentityToken, Is.EqualTo("eyJ.google.token"));
            Assert.That(r.AuthorizationCode, Is.Empty);
        }

        [TestCase("{\"ok\":true}")]
        [TestCase("{\"ok\":true,\"identity_token\":\"\"}")]
        public void OkWithoutToken_IsFailure(string json)
        {
            var r = PlatformSignIn.Parse(json);

            Assert.That(r.Ok, Is.False);
            Assert.That(r.Error, Is.EqualTo(PlatformSignIn.ErrorFailed));
            Assert.That(r.IdentityToken, Is.Empty);
        }

        [TestCase("canceled", PlatformSignIn.ErrorCanceled)]
        [TestCase("no_account", PlatformSignIn.ErrorNoAccount)]
        [TestCase("unsupported", PlatformSignIn.ErrorUnsupported)]
        [TestCase("failed", PlatformSignIn.ErrorFailed)]
        [TestCase("some_future_error", PlatformSignIn.ErrorFailed)]
        public void FailureError_MapsToKnownCodes(string error, string expected)
        {
            var r = PlatformSignIn.Parse("{\"ok\":false,\"error\":\"" + error + "\"}");

            Assert.That(r.Ok, Is.False);
            Assert.That(r.Error, Is.EqualTo(expected));
            Assert.That(r.IdentityToken, Is.Empty);
        }

        [Test]
        public void FailureWithoutError_IsFailed()
        {
            Assert.That(PlatformSignIn.Parse("{\"ok\":false}").Error, Is.EqualTo(PlatformSignIn.ErrorFailed));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not json")]
        [TestCase("{\"ok\":true,\"identity_token\":")]
        [TestCase("[]")]
        [TestCase("42")]
        public void MalformedInput_IsFailedNotThrown(string json)
        {
            PlatformSignInResult r = default;
            Assert.DoesNotThrow(() => r = PlatformSignIn.Parse(json));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Error, Is.EqualTo(PlatformSignIn.ErrorFailed));
        }

        [Test]
        public void Editor_HasNoChannelAndReportsUnsupportedImmediately()
        {
            Assert.That(PlatformSignIn.AppleAvailable, Is.False);
            Assert.That(PlatformSignIn.GoogleAvailable, Is.False);

            PlatformSignInResult? apple = null, google = null;
            PlatformSignIn.RequestApple(r => apple = r);
            PlatformSignIn.RequestGoogle(r => google = r);

            Assert.That(apple.HasValue && !apple.Value.Ok, Is.True, "编辑器里同步回调一次,且不是成功");
            Assert.That(apple.Value.Error, Is.EqualTo(PlatformSignIn.ErrorUnsupported));
            Assert.That(google.HasValue && !google.Value.Ok, Is.True);
            Assert.That(google.Value.Error, Is.EqualTo(PlatformSignIn.ErrorUnsupported));
            Assert.DoesNotThrow(PlatformSignIn.ClearGoogleCredentialState);
        }

        [TestCase(PlatformSignIn.ErrorCanceled, "取消")]
        [TestCase(PlatformSignIn.ErrorNoAccount, "Google 账号")]
        [TestCase(PlatformSignIn.ErrorUnsupported, "不支持")]
        [TestCase(PlatformSignIn.ErrorFailed, "稍后重试")]
        [TestCase("some_future_error", "稍后重试")]
        public void FailureText_IsChineseForPlayers(string error, string fragment)
        {
            Assert.That(PlatformSignIn.DescribeFailure(error), Does.Contain(fragment));
        }
    }
}
