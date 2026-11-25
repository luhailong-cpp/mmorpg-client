using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace MmorpgClient.Net
{
    /// <summary>
    /// 官方通行证(Casdoor OIDC)公开客户端配置。全部为公开地址与公开 ClientId,不含密钥。
    /// </summary>
    public readonly struct PassportOAuthConfig
    {
        public const int DefaultTimeoutSeconds = 180;
        public const int DefaultTokenExchangeTimeoutSeconds = 15;

        public string Origin { get; }
        public string ClientId { get; }
        public string RedirectUri { get; }
        public int TimeoutSeconds { get; }

        public PassportOAuthConfig(string origin, string clientId, string redirectUri,
            int timeoutSeconds = DefaultTimeoutSeconds)
        {
            Origin = (origin ?? string.Empty).Trim().TrimEnd('/');
            ClientId = (clientId ?? string.Empty).Trim();
            RedirectUri = (redirectUri ?? string.Empty).Trim();
            TimeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : DefaultTimeoutSeconds;
        }

        public bool IsValid => TryValidate(out _);

        public bool TryValidate(out string reason)
        {
            if (string.IsNullOrEmpty(Origin) || !Uri.TryCreate(Origin, UriKind.Absolute, out var originUri))
            {
                reason = "invalid passport origin";
                return false;
            }
            if (!string.Equals(originUri.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(originUri.Scheme, "http", StringComparison.OrdinalIgnoreCase))
            {
                reason = "passport origin must use http or https";
                return false;
            }
            if (!string.IsNullOrEmpty(originUri.UserInfo) || !string.IsNullOrEmpty(originUri.Query) ||
                !string.IsNullOrEmpty(originUri.Fragment))
            {
                reason = "passport origin must not contain userinfo, query, or fragment";
                return false;
            }
            if (string.IsNullOrEmpty(ClientId))
            {
                reason = "missing passport client_id";
                return false;
            }
            if (string.IsNullOrEmpty(RedirectUri) || !Uri.TryCreate(RedirectUri, UriKind.Absolute, out var redirect))
            {
                reason = "invalid passport redirect_uri";
                return false;
            }
            if (!string.IsNullOrEmpty(redirect.Query) || !string.IsNullOrEmpty(redirect.Fragment))
            {
                reason = "passport redirect_uri must not contain query or fragment";
                return false;
            }
            reason = string.Empty;
            return true;
        }
    }

    /// <summary>一次 OAuth2 + PKCE(S256) 授权的临时内存凭据,完成后立即清零,绝不落盘或记日志。</summary>
    public readonly struct PassportPkceSession
    {
        public string CodeVerifier { get; }
        public string CodeChallenge { get; }
        public string State { get; }

        public PassportPkceSession(string codeVerifier, string codeChallenge, string state)
        {
            CodeVerifier = codeVerifier;
            CodeChallenge = codeChallenge;
            State = state;
        }
    }

    /// <summary>
    /// 官方通行证 OAuth2 + PKCE(RFC 7636 S256) 纯 C# 协议核心(零 UnityEngine 依赖,可脱离编辑器单测)。
    /// 服务端契约见 xuanming-server-mmo-cpp/docs/design/official-passport-registration.md。
    /// </summary>
    public static class PassportOAuthProtocol
    {
        public const int VerifierRandomBytes = 32;
        public const int StateRandomBytes = 16;

        public const string AuthorizePath = "/login/oauth/authorize";
        public const string TokenExchangePath = "/api/login/oauth/access_token";

        public static PassportPkceSession CreatePkceSession()
        {
            string verifier = Base64UrlEncode(RandomBytes(VerifierRandomBytes));
            string challenge = ComputeS256Challenge(verifier);
            string state = Base64UrlEncode(RandomBytes(StateRandomBytes));
            return new PassportPkceSession(verifier, challenge, state);
        }

        /// <summary>按 RFC 7636 §4.2 计算 BASE64URL-ENCODE(SHA256(ASCII(code_verifier)))。</summary>
        public static string ComputeS256Challenge(string codeVerifier)
        {
            if (string.IsNullOrEmpty(codeVerifier))
                throw new ArgumentException("codeVerifier must not be empty", nameof(codeVerifier));
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
            return Base64UrlEncode(hash);
        }

        public static string BuildAuthorizeUrl(PassportOAuthConfig config, PassportPkceSession pkce)
        {
            if (!config.TryValidate(out var reason))
                throw new ArgumentException(reason, nameof(config));
            if (string.IsNullOrEmpty(pkce.CodeChallenge) || string.IsNullOrEmpty(pkce.State))
                throw new ArgumentException("pkce session is incomplete", nameof(pkce));

            return $"{config.Origin}{AuthorizePath}"
                   + $"?client_id={Uri.EscapeDataString(config.ClientId)}"
                   + "&response_type=code"
                   + $"&redirect_uri={Uri.EscapeDataString(config.RedirectUri)}"
                   + "&scope=openid"
                   + $"&state={Uri.EscapeDataString(pkce.State)}"
                   + $"&code_challenge={Uri.EscapeDataString(pkce.CodeChallenge)}"
                   + "&code_challenge_method=S256";
        }

        public static string BuildTokenExchangeUrl(PassportOAuthConfig config)
        {
            if (!config.TryValidate(out var reason))
                throw new ArgumentException(reason, nameof(config));
            return config.Origin + TokenExchangePath;
        }

        /// <summary>
        /// 构造 Public PKCE Client 的换票表单:严格只带 code_verifier,不带 client_secret。
        /// </summary>
        public static Dictionary<string, string> BuildTokenExchangeForm(
            PassportOAuthConfig config, string code, string codeVerifier)
        {
            if (!config.TryValidate(out var reason))
                throw new ArgumentException(reason, nameof(config));
            if (string.IsNullOrEmpty(code))
                throw new ArgumentException("authorization code must not be empty", nameof(code));
            if (string.IsNullOrEmpty(codeVerifier))
                throw new ArgumentException("codeVerifier must not be empty", nameof(codeVerifier));

            return new Dictionary<string, string>(5)
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = config.ClientId,
                ["code"] = code,
                ["redirect_uri"] = config.RedirectUri,
                ["code_verifier"] = codeVerifier,
            };
        }

        /// <summary>
        /// 校验桌面本地回调地址并产出 HttpListener 前缀。只允许 http://127.0.0.1:&lt;port&gt;/...,
        /// 严禁监听 0.0.0.0、* 或任何非本机回环地址。
        /// </summary>
        public static bool TryGetLoopbackListenerPrefix(string redirectUri, out string prefix, out string expectedPath)
        {
            prefix = string.Empty;
            expectedPath = string.Empty;
            if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)) return false;
            if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal)) return false;
            if (uri.IsDefaultPort || uri.Port <= 0 || uri.Port > 65535) return false;
            if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                return false;

            expectedPath = string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
            string prefixPath = expectedPath.EndsWith("/", StringComparison.Ordinal) ? expectedPath : expectedPath + "/";
            prefix = $"http://127.0.0.1:{uri.Port}{prefixPath}";
            return true;
        }

        /// <summary>校验移动端深度链接回调是否属于配置的自定义 scheme + host + path。</summary>
        public static bool MatchesRedirectTarget(Uri callbackUri, string configuredRedirectUri)
        {
            if (callbackUri == null || !Uri.TryCreate(configuredRedirectUri, UriKind.Absolute, out var expected))
                return false;
            return string.Equals(callbackUri.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(callbackUri.Host, expected.Host, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(TrimTrailingSlash(callbackUri.AbsolutePath), TrimTrailingSlash(expected.AbsolutePath),
                       StringComparison.Ordinal);
        }

        /// <summary>
        /// 解析 OAuth2 回调 URI:先校验 state 常量时间相等,再提取一次性 code。
        /// </summary>
        public static bool TryParseCallbackUri(Uri callbackUri, string expectedState, out string code, out string errorCode)
        {
            code = string.Empty;
            errorCode = string.Empty;
            if (callbackUri == null || string.IsNullOrEmpty(expectedState))
            {
                errorCode = PlatformSignIn.ErrorFailed;
                return false;
            }

            var query = ParseQueryString(callbackUri.Query);
            query.TryGetValue("state", out var actualState);
            if (!ConstantTimeEquals(expectedState, actualState))
            {
                errorCode = PassportSignInErrors.StateMismatch;
                return false;
            }

            if (query.TryGetValue("error", out var oauthError) && !string.IsNullOrEmpty(oauthError))
            {
                errorCode = string.Equals(oauthError, "access_denied", StringComparison.OrdinalIgnoreCase)
                    ? PlatformSignIn.ErrorCanceled
                    : PlatformSignIn.ErrorFailed;
                return false;
            }

            if (!query.TryGetValue("code", out var authCode) || string.IsNullOrWhiteSpace(authCode))
            {
                errorCode = PlatformSignIn.ErrorFailed;
                return false;
            }

            code = authCode;
            return true;
        }

        /// <summary>
        /// 从 Casdoor /api/login/oauth/access_token 的 JSON 响应提取 id_token(必须是三段式紧凑 JWS)。
        /// 不引入外部 JSON 库,也不把令牌写进异常或日志。
        /// </summary>
        public static bool TryExtractIdToken(string jsonBody, out string idToken)
        {
            idToken = string.Empty;
            if (string.IsNullOrEmpty(jsonBody)) return false;
            if (!TryReadJsonStringProperty(jsonBody, "id_token", out var raw) || string.IsNullOrWhiteSpace(raw))
                return false;
            if (!LooksLikeCompactJwt(raw)) return false;
            idToken = raw;
            return true;
        }

        internal static bool LooksLikeCompactJwt(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 16384) return false;
            int firstDot = token.IndexOf('.');
            if (firstDot <= 0) return false;
            int secondDot = token.IndexOf('.', firstDot + 1);
            if (secondDot <= firstDot + 1 || secondDot >= token.Length - 1) return false;
            if (token.IndexOf('.', secondDot + 1) >= 0) return false;
            for (int i = 0; i < token.Length; i++)
            {
                if (char.IsWhiteSpace(token[i])) return false;
            }
            return true;
        }

        internal static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        private static byte[] RandomBytes(int length)
        {
            var buf = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(buf);
            return buf;
        }

        private static bool ConstantTimeEquals(string expected, string actual)
        {
            if (expected == null || actual == null) return false;
            byte[] a = Encoding.UTF8.GetBytes(expected);
            byte[] b = Encoding.UTF8.GetBytes(actual);
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static string TrimTrailingSlash(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "/") return "/";
            return path.TrimEnd('/');
        }

        private static Dictionary<string, string> ParseQueryString(string rawQuery)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(rawQuery)) return result;
            string trimmed = rawQuery[0] == '?' ? rawQuery.Substring(1) : rawQuery;
            if (trimmed.Length == 0) return result;
            foreach (var part in trimmed.Split('&'))
            {
                if (string.IsNullOrEmpty(part)) continue;
                int eq = part.IndexOf('=');
                string key = eq >= 0 ? part.Substring(0, eq) : part;
                string value = eq >= 0 ? part.Substring(eq + 1) : string.Empty;
                result[DecodeQueryComponent(key)] = DecodeQueryComponent(value);
            }
            return result;
        }

        private static string DecodeQueryComponent(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            return Uri.UnescapeDataString(raw.Replace('+', ' '));
        }

        private static bool TryReadJsonStringProperty(string json, string propertyName, out string value)
        {
            value = string.Empty;
            int idx = 0;
            int len = json.Length;
            while (idx < len)
            {
                int keyStart = json.IndexOf('"', idx);
                if (keyStart < 0) return false;
                if (!TryReadJsonStringLiteral(json, keyStart, out var key, out int afterKey)) return false;
                idx = SkipWhitespace(json, afterKey);
                if (idx >= len || json[idx] != ':')
                {
                    idx = afterKey;
                    continue;
                }
                idx = SkipWhitespace(json, idx + 1);
                if (idx >= len) return false;
                if (string.Equals(key, propertyName, StringComparison.Ordinal))
                {
                    if (json[idx] != '"') return false;
                    return TryReadJsonStringLiteral(json, idx, out value, out _);
                }
                if (json[idx] == '"')
                {
                    if (!TryReadJsonStringLiteral(json, idx, out _, out idx)) return false;
                }
                else
                {
                    idx++;
                }
            }
            return false;
        }

        private static int SkipWhitespace(string text, int start)
        {
            int i = start;
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            return i;
        }

        private static bool TryReadJsonStringLiteral(string json, int quoteIndex, out string result, out int nextIndex)
        {
            result = string.Empty;
            nextIndex = quoteIndex;
            if (quoteIndex < 0 || quoteIndex >= json.Length || json[quoteIndex] != '"') return false;
            var sb = new StringBuilder();
            int i = quoteIndex + 1;
            while (i < json.Length)
            {
                char c = json[i++];
                if (c == '"')
                {
                    result = sb.ToString();
                    nextIndex = i;
                    return true;
                }
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (i >= json.Length) return false;
                char esc = json[i++];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > json.Length) return false;
                        if (!ushort.TryParse(json.Substring(i, 4), System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out var codeUnit))
                            return false;
                        sb.Append((char)codeUnit);
                        i += 4;
                        break;
                    default:
                        return false;
                }
            }
            return false;
        }
    }

    /// <summary>官方通行证专属失败码与面向玩家的中文文案(纯 C#,不依赖 UnityEngine)。</summary>
    public static class PassportSignInErrors
    {
        public const string Timeout = "timeout";
        public const string PortInUse = "port_in_use";
        public const string StateMismatch = "state_mismatch";

        public static string Describe(string error) => error switch
        {
            "canceled" => "已取消官方通行证登录",
            Timeout => "授权已超时，请重新点击登录",
            PortInUse => "本地授权回调端口被占用，请关闭冲突程序后重试",
            StateMismatch => "授权校验失败，请重试",
            "unsupported" => "当前安装包未开启官方通行证登录",
            _ => "通行证服务暂时不可用，请稍后重试",
        };
    }
}
