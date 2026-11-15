using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;

namespace MmorpgClient.World.CurrentMaps
{
    /// <summary>本机联调的独立门禁，不修改正式交付的 clientOnly 或 CanTravelOnline。</summary>
    public static class FestivalMapLocalTestPolicy
    {
        public const string Argument = "-festivalMapLocalTest";
        public const string YuexiBayArgument = "-yuexiBayLocalTest";
        private const string YuexiBayGatewayPortArgument = "-yuexiBayGatewayPort";
        private const string YuexiBayGatePortArgument = "-yuexiBayGatePort";
        public static readonly IReadOnlyList<string> MapIds = Array.AsReadOnly(new[]
            { "01001", "01002", "05008", "10000", "19004", "21003", "23006", "28007", "35004" });

        public static bool IsRequested(IReadOnlyList<string> arguments)
            => HasArgument(arguments, Argument);

        private static bool HasArgument(IReadOnlyList<string> arguments, string expected)
        {
            if (arguments == null) return false;
            foreach (string value in arguments)
                if (string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)) return true;
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

        /// <summary>Only an explicitly selected isolated development stack may use Yuexi Bay's client-only runtime map.</summary>
        public static bool CanUseYuexiBay(bool developmentBuild, IReadOnlyList<string> arguments, string gateway, string assignedGate)
        {
            if (!CanConnect(developmentBuild, HasArgument(arguments, YuexiBayArgument), gateway, assignedGate) ||
                !TryReadIsolatedPort(arguments, YuexiBayGatewayPortArgument, out int gatewayPort) ||
                !TryReadIsolatedPort(arguments, YuexiBayGatePortArgument, out int gatePort)) return false;
            int colon = assignedGate.LastIndexOf(':');
            return new Uri(gateway).Port == gatewayPort &&
                int.TryParse(assignedGate.Substring(colon + 1), out int actualGatePort) && actualGatePort == gatePort;
        }

        public static bool IsYuexiBayTarget(CurrentMapDefinition definition, string appearance)
            => definition != null && definition.IsModern && !definition.CanTravelOnline &&
                definition.SceneConfigId == 3 && definition.Key == "village" && definition.Revision == "yuexi-bay-20261115" &&
                (appearance == "spring" || appearance == "mid-autumn") && definition.ForAppearance(appearance) != null;

        internal static bool CanUseLocalDelivery(CurrentMapDefinition definition, string appearance,
            bool developmentBuild, IReadOnlyList<string> arguments, string gateway, string assignedGate)
        {
            if (IsYuexiBayTarget(definition, appearance))
                return CanUseYuexiBay(developmentBuild, arguments, gateway, assignedGate);
            return IsDeliveredTarget(definition, appearance) && CanConnect(developmentBuild, IsRequested(arguments), gateway, assignedGate);
        }

        private static bool TryReadIsolatedPort(IReadOnlyList<string> arguments, string option, out int port)
        {
            port = 0;
            for (int i = 0; i < arguments.Count; ++i)
            {
                if (!string.Equals(arguments[i], option, StringComparison.OrdinalIgnoreCase)) continue;
                if (port != 0 || i + 1 >= arguments.Count ||
                    !int.TryParse(arguments[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out port) ||
                    port <= 0 || port > 65535 || port == 8081 || port == 18086 || port == 28000) return false;
                ++i;
            }
            return port != 0;
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
