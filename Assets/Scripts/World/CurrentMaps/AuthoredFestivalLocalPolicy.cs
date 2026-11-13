using System;
using System.Net;

namespace MmorpgClient.World.CurrentMaps
{
    /// <summary>仅授权本地开发栈使用手绘测试地图。</summary>
    public static class AuthoredFestivalLocalPolicy
    {
        public static bool CanUse(string gateway, string gate)
            => AuthoredMapRegistry.IsEnabled && MatchesEndpoints(gateway, gate);

        public static bool MatchesEndpoints(string gateway, string gate)
        {
            if (!Uri.TryCreate(gateway, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo) || uri.Port != 18085 || !IsLoopback(uri.Host)) return false;
            int colon = gate?.LastIndexOf(':') ?? -1;
            return colon > 0 && int.TryParse(gate.Substring(colon + 1), out int port) &&
                port == 28000 && IsLoopback(gate.Substring(0, colon));
        }

        private static bool IsLoopback(string host)
            => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               (IPAddress.TryParse(host?.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
    }
}
