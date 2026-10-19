using MmorpgClient.Game;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Net
{
    /// <summary>
    /// HTTP 登录失败文案(GameClient.DescribeLoginCode)。契约见服务端
    /// docs/design/third-party-login-end-to-end-design.md §三 与 tip-code-axis.md §2.10:
    /// 网关 HTTP 状态恒为 200,客户端只按 JSON code 分支;500 也可能带 upstream_err=&lt;tip&gt;。
    /// </summary>
    public sealed class GatewayLoginErrorTextTests
    {
        private static GatewayLoginResult Result(int code, string message)
            => new GatewayLoginResult { code = code, message = message };

        [Test]
        public void UpstreamFaultTip_On500_SaysRetryLater_NotWrongPassword()
        {
            // 2035 只是示例数字:文案不按码值分支,只认 code=500。
            string text = GameClient.DescribeLoginCode(Result(GatewayLoginResult.CodeInternal, "upstream_err=2035"));
            Assert.That(text, Does.Contain("稍后重试"));
            Assert.That(text, Does.Contain("upstream_err=2035"));
            Assert.That(text, Does.Not.Contain("密码"));
        }

        [Test]
        public void LoginUnavailable_On500_SaysRetryLater()
        {
            string text = GameClient.DescribeLoginCode(Result(GatewayLoginResult.CodeInternal, "login_unavailable"));
            Assert.That(text, Does.Contain("稍后重试"));
            Assert.That(text, Does.Contain("login_unavailable"));
        }

        [Test]
        public void EmptyMessage_On500_FallsBackToCode()
        {
            string text = GameClient.DescribeLoginCode(Result(GatewayLoginResult.CodeInternal, null));
            Assert.That(text, Does.Contain("500"));
        }

        [Test]
        public void UpstreamRejection_On401_StaysAuthRejected()
        {
            string text = GameClient.DescribeLoginCode(Result(GatewayLoginResult.CodeAuthRejected, "upstream_err=2000"));
            Assert.That(text, Is.EqualTo("账号或密码错误"));
        }

        [Test]
        public void UnknownCode_KeepsGenericFailure()
        {
            string text = GameClient.DescribeLoginCode(Result(418, "teapot"));
            Assert.That(text, Is.EqualTo("登录失败(teapot)"));
        }
    }
}
