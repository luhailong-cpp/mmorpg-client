using System.Collections.Generic;
using MmorpgClient.Net;
using UnityEngine;

namespace MmorpgClient.Core
{
    /// <summary>
    /// PlayerPrefs-backed user-facing settings (gateway URL, last account,
    /// preferred zone). All keys are namespaced under "mmorpg." to avoid
    /// collisions with other Unity scripts.
    ///
    /// Anything secret (refresh tokens, passwords) MUST NOT be stored here:
    /// PlayerPrefs is plain-text on every supported platform. Use OS keystore
    /// integrations (Keychain on iOS, EncryptedSharedPreferences on Android,
    /// DPAPI on Windows) for credentials -- see <c>SecureStore</c> stub
    /// follow-up in the production roadmap.
    /// </summary>
    public static class ClientSettings
    {
        private const string K_Gateway  = "mmorpg.gateway";
        private const string K_Account  = "mmorpg.account";
        private const string K_Zone     = "mmorpg.zone";
        private const string K_RecentZones = "mmorpg.recentzones";
        private const string K_LoginMethod = "mmorpg.loginmethod";
        private const string K_PassportOrigin = "mmorpg.passport.origin";
        private const string K_PassportClientId = "mmorpg.passport.clientid";
        private const string K_PassportRedirectUri = "mmorpg.passport.redirecturi";

        /// <summary>
        /// 网关与官方通行证的地址:正式包只认构建时写进包里的 <see cref="ClientEndpoints"/>;启动参数(-gateway / -passport-origin /
        /// -passport-client-id / -passport-redirect-uri)与本机设置只在编辑器和开发包里生效(服务端 docs/design/official-passport-registration.md §3.8),
        /// 免得带参数的快捷方式或改过的本机设置把正版客户端带到钓鱼站。
        /// </summary>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public static readonly bool EndpointOverridesAllowed = true;
#else
        public static readonly bool EndpointOverridesAllowed = false;
#endif

        /// <summary>官方通行证的公开 ClientId(与服务端 AuthProviders.Passport.ClientId、通行证初始化数据一致)。只存公开值,不存任何密钥。</summary>
        public const string DefaultPassportClientId = "xuanming-game-client";
        /// <summary>桌面回调的首选地址;端口被占时依次试后面四个(PassportOAuthProtocol.LoopbackRedirectCandidates),通行证里五个都登记了。</summary>
        public const string DefaultDesktopPassportRedirectUri = "http://127.0.0.1:18765/passport/callback";
        /// <summary>iOS 系统认证会话与安卓开发包用的回调;安卓正式包改走 App Links(<see cref="PassportOAuthProtocol.AppLinkRedirectUri"/>)。</summary>
        public const string DefaultMobilePassportRedirectUri = "xuanming://passport/callback";

        public static string PassportOrigin
        {
            get => Overridden(ClientLaunchArguments.PassportOriginOverride, K_PassportOrigin, ClientEndpoints.Current.passportOrigin);
            set { PlayerPrefs.SetString(K_PassportOrigin, value ?? ""); PlayerPrefs.Save(); }
        }

        public static string PassportClientId
        {
            get => Overridden(ClientLaunchArguments.PassportClientIdOverride, K_PassportClientId, DefaultPassportClientId);
            set { PlayerPrefs.SetString(K_PassportClientId, value ?? ""); PlayerPrefs.Save(); }
        }

        public static string PassportRedirectUri
        {
            get => Overridden(ClientLaunchArguments.PassportRedirectUriOverride, K_PassportRedirectUri, DefaultPassportRedirectUriFor(PassportOrigin));
            set { PlayerPrefs.SetString(K_PassportRedirectUri, value ?? ""); PlayerPrefs.Save(); }
        }

        /// <summary>
        /// 本平台缺省的通行证回调:桌面是本机回环;iOS 是系统认证会话认的自定义协议头(回调只交给发起会话的本 App);
        /// 安卓在通行证是 https 时用 App Links(系统按 assetlinks.json 核对归属,别的 App 抢注不了),本机联调的 http 通行证退回自定义协议头。
        /// </summary>
        public static string DefaultPassportRedirectUriFor(string passportOrigin)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return PassportOAuthProtocol.AppLinkRedirectUri(passportOrigin) ?? DefaultMobilePassportRedirectUri;
#elif UNITY_IOS && !UNITY_EDITOR
            return DefaultMobilePassportRedirectUri;
#else
            return DefaultDesktopPassportRedirectUri;
#endif
        }

        /// <summary>编辑器与开发包:启动参数优先,其次本机设置,最后 fallback;正式包只回 fallback。</summary>
        private static string Overridden(string launchArgument, string prefsKey, string fallback)
        {
            if (!EndpointOverridesAllowed) return fallback;
            return launchArgument ?? PlayerPrefs.GetString(prefsKey, fallback);
        }

        /// <summary>
        /// Google 登录(安卓 Credential Manager)的 serverClientId,构建常量:必须等于服务端 AuthProviders.Google.ClientId,
        /// 即 Web 类型的 OAuth 客户端 ID;填安卓类型的客户端 ID 时令牌的 aud 对不上,登录一律被拒
        /// (服务端 docs/design/account-deletion.md §3.2)。空串 = 这个安装包不提供 Google 登录,按钮不显示。
        /// </summary>
        public const string GoogleServerClientId = "";

        /// <summary>
        /// 玩家最近一次登录用的方式(拿到凭据、发起 /api/login 时记;LoginAuthTypes 的取值,不含任何凭据)。
        /// 注销账号时按它重新认证:Apple / Google 重新拉起渠道,其余重输口令。
        /// </summary>
        public static string LastLoginMethod
        {
            get => PlayerPrefs.GetString(K_LoginMethod, "");
            set { PlayerPrefs.SetString(K_LoginMethod, value ?? ""); PlayerPrefs.Save(); }
        }

        public static string GatewayBaseUrl
        {
            // 仓库里的 ClientEndpoints.json 是本机联调的 Java gateway_node(application.yaml server.port 8081);正式包写的是构建时给的地址。
            get => Overridden(ClientLaunchArguments.GatewayOverride, K_Gateway, ClientEndpoints.Current.gatewayBaseUrl);
            set { PlayerPrefs.SetString(K_Gateway, value ?? ""); PlayerPrefs.Save(); }
        }

        public static string LastAccount
        {
            get => PlayerPrefs.GetString(K_Account, "");
            set { PlayerPrefs.SetString(K_Account, value ?? ""); PlayerPrefs.Save(); }
        }

        public static uint ZoneId
        {
            get => (uint)PlayerPrefs.GetInt(K_Zone, 1);
            set { PlayerPrefs.SetInt(K_Zone, (int)value); PlayerPrefs.Save(); }
        }

        /// <summary>Comma-separated most-recent-first zone ids (e.g. "3,1,7").</summary>
        public static string RecentZones
        {
            get => PlayerPrefs.GetString(K_RecentZones, "");
            set { PlayerPrefs.SetString(K_RecentZones, value ?? ""); PlayerPrefs.Save(); }
        }

        public static List<uint> ParseRecentZones()
        {
            var result = new List<uint>();
            var raw = RecentZones;
            if (string.IsNullOrEmpty(raw)) return result;
            foreach (var part in raw.Split(','))
            {
                if (uint.TryParse(part.Trim(), out var z) && z != 0 && !result.Contains(z))
                    result.Add(z);
            }
            return result;
        }

        public static void SaveRecentZones(IReadOnlyList<uint> zones)
        {
            RecentZones = zones == null ? "" : string.Join(",", zones);
        }

        /// <summary>某区最近一次进入的角色 id(0 = 无记录)。key: mmorpg.lastplayer.{zoneId}。</summary>
        public static ulong GetLastPlayer(uint zoneId)
        {
            var raw = PlayerPrefs.GetString($"mmorpg.lastplayer.{zoneId}", "");
            return ulong.TryParse(raw, out var v) ? v : 0;
        }

        public static void SetLastPlayer(uint zoneId, ulong playerId)
        {
            PlayerPrefs.SetString($"mmorpg.lastplayer.{zoneId}", playerId.ToString());
            PlayerPrefs.Save();
        }
    }
}
