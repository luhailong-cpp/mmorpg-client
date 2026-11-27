using System;
using MmorpgClient.Net;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Net
{
    public sealed class PassportSignInTests
    {
        private static PassportOAuthConfig ValidDesktopConfig() => new PassportOAuthConfig(
            "http://127.0.0.1:8000/",
            "xuanming-game-client",
            "http://127.0.0.1:18765/passport/callback");

        [Test]
        public void ComputeS256Challenge_MatchesRfc7636AppendixBVector()
        {
            // RFC 7636 Appendix B 标准测试向量
            const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
            const string expected = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

            Assert.That(PassportOAuthProtocol.ComputeS256Challenge(verifier), Is.EqualTo(expected));
        }

        [Test]
        public void CreatePkceSession_ProducesUniqueUrlSafeVerifierChallengeAndState()
        {
            var first = PassportOAuthProtocol.CreatePkceSession();
            var second = PassportOAuthProtocol.CreatePkceSession();

            Assert.That(first.CodeVerifier.Length, Is.EqualTo(43));
            Assert.That(first.CodeChallenge, Is.EqualTo(PassportOAuthProtocol.ComputeS256Challenge(first.CodeVerifier)));
            Assert.That(first.State.Length, Is.GreaterThanOrEqualTo(22));
            Assert.That(first.CodeVerifier, Does.Not.Contain("+").And.Not.Contain("/").And.Not.Contain("="));
            Assert.That(first.State, Does.Not.Contain("+").And.Not.Contain("/").And.Not.Contain("="));
            Assert.That(first.CodeVerifier, Is.Not.EqualTo(second.CodeVerifier));
            Assert.That(first.State, Is.Not.EqualTo(second.State));
        }

        [Test]
        public void BuildAuthorizeUrl_TrimsOriginSlashAndIncludesRequiredOidcPkceParams()
        {
            var config = ValidDesktopConfig();
            var pkce = new PassportPkceSession("verifier-value", "challenge-value", "state-value");

            string url = PassportOAuthProtocol.BuildAuthorizeUrl(config, pkce);

            Assert.That(url, Does.StartWith("http://127.0.0.1:8000/login/oauth/authorize?"));
            Assert.That(url, Does.Contain("client_id=xuanming-game-client"));
            Assert.That(url, Does.Contain("response_type=code"));
            Assert.That(url, Does.Contain("scope=openid"));
            Assert.That(url, Does.Contain("redirect_uri=" + Uri.EscapeDataString("http://127.0.0.1:18765/passport/callback")));
            Assert.That(url, Does.Contain("state=state-value"));
            Assert.That(url, Does.Contain("code_challenge=challenge-value"));
            Assert.That(url, Does.Contain("code_challenge_method=S256"));
        }

        [Test]
        public void BuildTokenExchangeForm_UsesPublicPkceWithoutClientSecret()
        {
            var config = ValidDesktopConfig();
            var form = PassportOAuthProtocol.BuildTokenExchangeForm(config, "auth-code-123", "verifier-abc");

            Assert.That(PassportOAuthProtocol.BuildTokenExchangeUrl(config),
                Is.EqualTo("http://127.0.0.1:8000/api/login/oauth/access_token"));
            Assert.That(form["grant_type"], Is.EqualTo("authorization_code"));
            Assert.That(form["client_id"], Is.EqualTo("xuanming-game-client"));
            Assert.That(form["code"], Is.EqualTo("auth-code-123"));
            Assert.That(form["redirect_uri"], Is.EqualTo("http://127.0.0.1:18765/passport/callback"));
            Assert.That(form["code_verifier"], Is.EqualTo("verifier-abc"));
            Assert.That(form.ContainsKey("client_secret"), Is.False);
        }

        [Test]
        public void TryGetLoopbackListenerPrefix_OnlyAllowsExplicitIpv4LoopbackWithPort()
        {
            Assert.That(PassportOAuthProtocol.TryGetLoopbackListenerPrefix(
                "http://127.0.0.1:18765/passport/callback", out var prefix, out var path), Is.True);
            Assert.That(prefix, Is.EqualTo("http://127.0.0.1:18765/passport/callback/"));
            Assert.That(path, Is.EqualTo("/passport/callback"));

            foreach (var bad in new[]
                     {
                         "http://0.0.0.0:18765/passport/callback",
                         "http://localhost:18765/passport/callback",
                         "http://192.168.1.2:18765/passport/callback",
                         "https://127.0.0.1:18765/passport/callback",
                         "http://127.0.0.1/passport/callback",
                         "http://127.0.0.1:18765/passport/callback?x=1",
                         "xuanming://passport/callback",
                     })
            {
                Assert.That(PassportOAuthProtocol.TryGetLoopbackListenerPrefix(bad, out _, out _),
                    Is.False, $"must reject non-loopback redirect URI: {bad}");
            }
        }

        [Test]
        public void MatchesRedirectTarget_ValidatesMobileCustomScheme()
        {
            const string configured = "xuanming://passport/callback";
            Assert.That(PassportOAuthProtocol.MatchesRedirectTarget(
                new Uri("xuanming://passport/callback?code=abc&state=xyz"), configured), Is.True);
            Assert.That(PassportOAuthProtocol.MatchesRedirectTarget(
                new Uri("other://passport/callback?code=abc&state=xyz"), configured), Is.False);
            Assert.That(PassportOAuthProtocol.MatchesRedirectTarget(
                new Uri("xuanming://passport/other?code=abc&state=xyz"), configured), Is.False);
        }

        [Test]
        public void TryParseCallbackUri_ValidatesStateAndExtractsCodeOrError()
        {
            Assert.That(PassportOAuthProtocol.TryParseCallbackUri(
                new Uri("http://127.0.0.1:18765/passport/callback?code=ok_code_1&state=expected_state"),
                "expected_state", out var code, out var err), Is.True);
            Assert.That(code, Is.EqualTo("ok_code_1"));
            Assert.That(err, Is.Empty);

            Assert.That(PassportOAuthProtocol.TryParseCallbackUri(
                new Uri("http://127.0.0.1:18765/passport/callback?code=ok_code_1&state=wrong_state"),
                "expected_state", out _, out var mismatchErr), Is.False);
            Assert.That(mismatchErr, Is.EqualTo(PassportSignInErrors.StateMismatch));

            Assert.That(PassportOAuthProtocol.TryParseCallbackUri(
                new Uri("http://127.0.0.1:18765/passport/callback?error=access_denied&state=expected_state"),
                "expected_state", out _, out var deniedErr), Is.False);
            Assert.That(deniedErr, Is.EqualTo(PlatformSignIn.ErrorCanceled));

            Assert.That(PassportOAuthProtocol.TryParseCallbackUri(
                new Uri("http://127.0.0.1:18765/passport/callback?state=expected_state"),
                "expected_state", out _, out var missingCodeErr), Is.False);
            Assert.That(missingCodeErr, Is.EqualTo(PlatformSignIn.ErrorFailed));
        }

        [Test]
        public void CallbackStateMatches_OnlyAcceptsCurrentAttemptState()
        {
            var current = new Uri("http://127.0.0.1:18765/passport/callback?code=c&state=cur_state");
            Assert.That(PassportOAuthProtocol.CallbackStateMatches(current, "cur_state"), Is.True);
            Assert.That(PassportOAuthProtocol.CallbackStateMatches(
                new Uri("http://127.0.0.1:18765/passport/callback?code=c&state=old_state"), "cur_state"), Is.False);
            Assert.That(PassportOAuthProtocol.CallbackStateMatches(
                new Uri("http://127.0.0.1:18765/passport/callback?code=c"), "cur_state"), Is.False);
            Assert.That(PassportOAuthProtocol.CallbackStateMatches(current, ""), Is.False);
            Assert.That(PassportOAuthProtocol.CallbackStateMatches(null, "cur_state"), Is.False);
        }

        [Test]
        public void TryExtractIdToken_ReadsCompactJwtAndRejectsMalformedResponses()
        {
            const string sampleJwt = "eyJhbGciOiJSUzI1NiIsImtpZCI6ImNlcnQifQ.eyJzdWIiOiJ1dWlkLTEifQ.c2lnbmF0dXJl";
            string json = "{\"access_token\":\"ignored\",\"token_type\":\"Bearer\",\"id_token\":\"" + sampleJwt + "\",\"expires_in\":3600}";

            Assert.That(PassportOAuthProtocol.TryExtractIdToken(json, out var token), Is.True);
            Assert.That(token, Is.EqualTo(sampleJwt));

            foreach (var badJson in new[]
                     {
                         "",
                         "{}",
                         "{\"id_token\":\"\"}",
                         "{\"id_token\":\"not-a-jwt\"}",
                         "{\"id_token\":\"a.b\"}",
                         "{\"id_token\":\"a.b.c.d\"}",
                         "{\"id_token\":123}",
                     })
            {
                Assert.That(PassportOAuthProtocol.TryExtractIdToken(badJson, out _),
                    Is.False, $"must reject invalid token payload: {badJson}");
            }
        }

        [Test]
        public void LoginCredentialForPassport_UsesPassportAuthTypeAndDescribeFailureCoversAllCodes()
        {
            var cred = LoginCredential.ForThirdParty(LoginAuthTypes.Passport, "a.b.c");
            Assert.That(cred.AuthType, Is.EqualTo("passport"));
            Assert.That(cred.AuthToken, Is.EqualTo("a.b.c"));
            Assert.That(cred.Account, Is.Empty);
            Assert.That(cred.Password, Is.Empty);
            Assert.That(cred.IsPassword, Is.False);

            foreach (var code in new[]
                     {
                         "canceled",
                         PassportSignInErrors.Timeout,
                         PassportSignInErrors.PortInUse,
                         PassportSignInErrors.StateMismatch,
                         "unsupported",
                         "failed",
                     })
            {
                Assert.That(PassportSignInErrors.Describe(code), Is.Not.Empty, code);
            }
        }
    }
}
