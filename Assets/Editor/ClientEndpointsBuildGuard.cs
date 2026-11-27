using System.IO;
using MmorpgClient.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace MmorpgClient.Editor
{
    /// <summary>
    /// 正式包(不带 Development)不许带着本机联调的地址出去(服务端 docs/design/official-passport-registration.md §3.8):
    /// Assets/Resources/ClientEndpoints.json 的网关必须是 https 的外部地址,通行证留空或同样是 https,否则构建失败。
    /// 所有平台、所有出包入口(批处理 tools/build_client.ps1、编辑器里直接点构建)都经过这里;开发包不查。
    /// </summary>
    public sealed class ClientEndpointsBuildGuard : IPreprocessBuildWithReport
    {
        public const string ProjectFile = "Assets/Resources/ClientEndpoints.json";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if ((report.summary.options & BuildOptions.Development) != 0) return;
            if (!ClientEndpoints.TryValidateForRelease(ReadProjectEndpoints(), out var reason))
                throw new BuildFailedException($"[ClientEndpointsBuildGuard] release build refused: {ProjectFile} {reason}; " +
                                               "pass -GatewayUrl / -PassportOrigin to tools/build_client.ps1, or build with Development");
        }

        /// <summary>直接读工程里的文件,不走运行时的缓存(同一个编辑器会话里构建前刚改过它)。</summary>
        public static ClientEndpoints.Values ReadProjectEndpoints() =>
            ClientEndpoints.Parse(File.Exists(ProjectFile) ? File.ReadAllText(ProjectFile) : string.Empty);
    }
}
