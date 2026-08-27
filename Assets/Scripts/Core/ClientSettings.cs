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
        private const string K_LogLevel = "mmorpg.loglevel";
        private const string K_RecentZones = "mmorpg.recentzones";

        public static string GatewayBaseUrl
        {
            // Java gateway_node listens on 8081 (application.yaml server.port).
            get => PlayerPrefs.GetString(K_Gateway, "http://127.0.0.1:8081");
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

        public static MmorpgLogger.LogLevel LogLevel
        {
            get => (MmorpgLogger.LogLevel)PlayerPrefs.GetInt(K_LogLevel,
                Debug.isDebugBuild ? (int)MmorpgLogger.LogLevel.Debug : (int)MmorpgLogger.LogLevel.Info);
            set { PlayerPrefs.SetInt(K_LogLevel, (int)value); PlayerPrefs.Save(); }
        }
    }
}
