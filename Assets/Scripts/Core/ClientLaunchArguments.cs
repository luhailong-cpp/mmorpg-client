using System;
using System.Collections.Generic;

namespace MmorpgClient.Core
{
    /// <summary>仅本次进程生效的启动参数，不写入已保存的设置。</summary>
    public static class ClientLaunchArguments
    {
        public static bool ManualPlay => HasFlag(Environment.GetCommandLineArgs(), "-manual-play");
        public static string GatewayOverride => GetValue(Environment.GetCommandLineArgs(), "-gateway");

        internal static bool HasFlag(IReadOnlyList<string> args, string flag)
        {
            if (args == null) return false;
            for (int i = 1; i < args.Count; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static string GetValue(IReadOnlyList<string> args, string key)
        {
            if (args == null) return null;
            for (int i = 1; i < args.Count; i++)
            {
                var arg = args[i];
                if (string.Equals(arg, key, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Count && !string.IsNullOrWhiteSpace(args[i + 1]) && !args[i + 1].StartsWith("-"))
                        return args[i + 1];
                }
                else if (arg != null && arg.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = arg.Substring(key.Length + 1);
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
            return null;
        }
    }
}
