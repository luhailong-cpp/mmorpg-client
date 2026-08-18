using System.Collections.Generic;
using MmorpgClient.Core;
using MmorpgClient.Net;
using UnityEngine;

namespace MmorpgClient.UI
{
    /// <summary>
    /// Cross-screen mutable state. Owned by <see cref="AppBootstrap"/>, passed
    /// into every screen so they can read selections from prior steps and
    /// stash their own outputs without a global singleton.
    ///
    /// Durable bits (gateway url, last account, recent zones) round-trip
    /// through <see cref="ClientSettings"/> (PlayerPrefs). Secrets (password,
    /// tokens) live only in memory by design.
    /// </summary>
    public sealed class SessionModel
    {
        public string Account;
        public string Password = "";
        public string GatewayBaseUrl;

        /// <summary>Stable per-device id for risk control / queue reconnect bypass.</summary>
        public readonly string DeviceId = SystemInfo.deviceUniqueIdentifier;

        public readonly List<ServerListZone> Zones = new();

        /// <summary>Selected zone id (authoritative). 0 = none.</summary>
        public uint SelectedZoneId;

        /// <summary>Kept for legacy screens indexing into <see cref="Zones"/>.</summary>
        public int SelectedZoneIndex = -1;

        public ServerListZone SelectedZone
        {
            get
            {
                if (SelectedZoneId != 0)
                {
                    foreach (var z in Zones)
                        if (z.zone_id == SelectedZoneId) return z;
                }
                return (SelectedZoneIndex >= 0 && SelectedZoneIndex < Zones.Count) ? Zones[SelectedZoneIndex] : null;
            }
        }

        /// <summary>Most-recent-first zone ids this device entered successfully.</summary>
        public readonly List<uint> RecentZoneIds = new();

        public readonly List<AnnouncementItem> Announcements = new();

        // HTTP login result (new path): tokens + account player list.
        public string AccessToken;
        public string RefreshToken;
        public long AccessTokenExpire;
        public long RefreshTokenExpire;
        public readonly List<GatewayPlayerInfo> Players = new();

        public int    RoleArchetypeIndex = 0;
        public string RoleNickname = "云行客";

        // V3 character pack selection. -1 = none chosen yet (RoleCreateScreen
        // falls back to RoleArchetypeIndex). SelectedWeaponV3Index defaults to
        // the character's starter weapon when not overridden.
        public int    SelectedCharacterV3Index = -1;
        public int    SelectedWeaponV3Index    = -1;

        public uint  StartSceneConfigId = 1;
        public ulong StartSceneId       = 0;

        public SessionModel()
        {
            GatewayBaseUrl = ClientSettings.GatewayBaseUrl;
            // dev_ 前缀对齐 login 的 DevPasswordAuth.AllowedAccountPrefixes;
            // 密码填 LOGIN_DEV_PASSWORD_SHARED_SECRET 环境变量的值。
            Account = string.IsNullOrEmpty(ClientSettings.LastAccount) ? "dev_demo" : ClientSettings.LastAccount;
            foreach (var z in ClientSettings.ParseRecentZones())
                RecentZoneIds.Add(z);
        }

        /// <summary>Mirror the token pair the client ended up with (TCP login may rotate it).</summary>
        public void SetTokensFromClient(Game.GameClient client)
        {
            if (client == null) return;
            AccessToken = client.AccessToken;
            RefreshToken = client.RefreshToken;
        }

        /// <summary>Record a successful zone entry (front of the recent list, deduped, capped).</summary>
        public void RecordZoneEntered(uint zoneId)
        {
            if (zoneId == 0) return;
            RecentZoneIds.Remove(zoneId);
            RecentZoneIds.Insert(0, zoneId);
            const int cap = 8;
            if (RecentZoneIds.Count > cap)
                RecentZoneIds.RemoveRange(cap, RecentZoneIds.Count - cap);
            ClientSettings.SaveRecentZones(RecentZoneIds);
            ClientSettings.LastAccount = Account ?? "";
            ClientSettings.ZoneId = zoneId;
        }
    }
}
