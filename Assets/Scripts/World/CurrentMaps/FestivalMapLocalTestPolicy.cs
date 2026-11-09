using System;
using System.Collections.Generic;
using System.Net;

namespace MmorpgClient.World.CurrentMaps
{
    /// <summary>本机联调的独立门禁，不修改正式交付的 clientOnly 或 CanTravelOnline。</summary>
    public static class FestivalMapLocalTestPolicy
    {
        public const string Argument = "-festivalMapLocalTest";
        public static readonly IReadOnlyList<string> MapIds = Array.AsReadOnly(new[]
            { "01001", "01002", "05008", "10000", "19004", "21003", "23006", "28007", "35004" });

        public static bool IsRequested(IReadOnlyList<string> arguments)
        {
            if (arguments == null) return false;
            foreach (string value in arguments)
                if (string.Equals(value, Argument, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool CanConnect(bool developmentBuild, bool requested, string gateway, string assignedGate)
        {
            if (!developmentBuild || !requested || !Uri.TryCreate(gateway, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !IsLoopbackHost(uri.Host)) return false;
            // AssignedGate 沿正式连接层格式 host:port；IPv6 的端口仍是最后一个冒号之后。
            int colon = assignedGate?.LastIndexOf(':') ?? -1;
            return colon > 0 && int.TryParse(assignedGate.Substring(colon + 1), out int port) &&
                port > 0 && port <= 65535 && IsLoopbackHost(assignedGate.Substring(0, colon));
        }

        public static bool IsDeliveredTarget(CurrentMapDefinition definition, string appearance)
        {
            if (definition == null || !definition.IsModern || definition.CanTravelOnline ||
                (appearance != "spring" && appearance != "mid-autumn") || definition.ForAppearance(appearance) == null)
                return false;
            foreach (string id in MapIds)
                if (definition.Key == "map" + id && definition.SceneConfigId == uint.Parse(id)) return true;
            return false;
        }

        private static bool IsLoopbackHost(string host)
        {
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            return IPAddress.TryParse(host?.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
        }
    }
}
