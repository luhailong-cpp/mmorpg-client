using MmorpgClient.Core;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Net
{
    /// <summary>
    /// 正式包只认构建时写进包里的地址(服务端 docs/design/official-passport-registration.md §3.8):钉住解析与正式包的地址要求。
    /// </summary>
    public sealed class ClientEndpointsTests
    {
        [Test]
        public void ParseTrimsAndToleratesBadJson()
        {
            var parsed = ClientEndpoints.Parse("{\"gatewayBaseUrl\":\" https://gateway.example.com/ \",\"passportOrigin\":\"https://passport.example.com/\"}");
            Assert.That(parsed.gatewayBaseUrl, Is.EqualTo("https://gateway.example.com"));
            Assert.That(parsed.passportOrigin, Is.EqualTo("https://passport.example.com"));
            foreach (var bad in new[] { "", null, "not json", "[]" })
            {
                var empty = ClientEndpoints.Parse(bad);
                Assert.That(empty.gatewayBaseUrl, Is.Empty, bad ?? "<null>");
                Assert.That(empty.passportOrigin, Is.Empty, bad ?? "<null>");
            }
        }

        [Test]
        public void ReleaseBuildsNeedExternalHttpsEndpoints()
        {
            Assert.That(ClientEndpoints.TryValidateForRelease(Values("https://gateway.example.com", ""), out _), Is.True);
            Assert.That(ClientEndpoints.TryValidateForRelease(Values("https://gateway.example.com", "https://passport.example.com"), out _), Is.True);
            // 协议页面必填(没有过不了商店审核),可以带查询串,但必须 https、不是本机。
            var noTerms = Values("https://gateway.example.com", "");
            noTerms.termsUrl = "";
            Assert.That(ClientEndpoints.TryValidateForRelease(noTerms, out var termsReason), Is.False);
            Assert.That(termsReason, Does.StartWith("termsUrl"));
            var plainPrivacy = Values("https://gateway.example.com", "");
            plainPrivacy.privacyUrl = "http://www.example.com/privacy";
            Assert.That(ClientEndpoints.TryValidateForRelease(plainPrivacy, out var privacyReason), Is.False);
            Assert.That(privacyReason, Does.StartWith("privacyUrl"));
            var queryTerms = Values("https://gateway.example.com", "");
            queryTerms.termsUrl = "https://www.example.com/legal?doc=terms";
            Assert.That(ClientEndpoints.TryValidateForRelease(queryTerms, out _), Is.True);
            foreach (var (gateway, passport) in new[]
                     {
                         ("", ""),                                                // 没给网关
                         ("http://127.0.0.1:8081", ""),                           // 仓库里的本机联调地址
                         ("http://gateway.example.com", ""),                      // 明文 http
                         ("https://127.0.0.1", ""),                               // 回环
                         ("https://gateway.example.com?x=1", ""),                 // 带查询串
                         ("https://gateway.example.com", "http://passport.example.com"), // 通行证明文
                         ("https://gateway.example.com", "http://127.0.0.1:8000"),
                     })
            {
                Assert.That(ClientEndpoints.TryValidateForRelease(Values(gateway, passport), out var reason), Is.False, $"{gateway} / {passport}");
                Assert.That(reason, Is.Not.Empty);
            }
            Assert.That(ClientEndpoints.TryValidateForRelease(null, out _), Is.False);
        }

        [Test]
        public void ReleaseOverridesAreCompiledOutOfPlayerBuilds()
        {
            // 编辑器与开发包可以用启动参数 / 本机设置覆盖;正式包的这个常量是 false(条件编译),这里在编辑器里只能看到 true。
            Assert.That(ClientSettings.EndpointOverridesAllowed, Is.True);
        }

        [Test]
        public void PrivacyConsentVersionOnlyGoesUp()
        {
            // 协议或隐私政策改版就把 CurrentVersion 加一:同意过旧版的要重新同意,从没同意过的(0)也要同意。
            Assert.That(PrivacyConsent.IsCurrent(0), Is.False);
            Assert.That(PrivacyConsent.IsCurrent(PrivacyConsent.CurrentVersion - 1), Is.False);
            Assert.That(PrivacyConsent.IsCurrent(PrivacyConsent.CurrentVersion), Is.True);
        }

        private static ClientEndpoints.Values Values(string gateway, string passport) =>
            new ClientEndpoints.Values
            {
                gatewayBaseUrl = gateway, passportOrigin = passport,
                termsUrl = "https://www.example.com/terms", privacyUrl = "https://www.example.com/privacy",
            };
    }
}
