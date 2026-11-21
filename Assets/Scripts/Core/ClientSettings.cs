using System.Collections.Generic;
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
            // Java gateway_node listens on 8081 (application.yaml server.port).
            get => ClientLaunchArguments.GatewayOverride ?? PlayerPrefs.GetString(K_Gateway, "http://127.0.0.1:8081");
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
