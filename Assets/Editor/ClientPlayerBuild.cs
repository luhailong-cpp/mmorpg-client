using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MmorpgClient.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MmorpgClient.Editor
{
    /// <summary>正式 Windows 构建入口。开发功能只由显式 Development 选项编译，不改工程设置。</summary>
    public static class ClientPlayerBuild
    {
        private const BuildTarget Target = BuildTarget.StandaloneWindows64;
        private const string Tag = "[ClientPlayerBuild]";

        [Serializable]
        private sealed class Manifest
        {
            public int schemaVersion = 1;
            public string result = "Failed";
            public string startedUtc;
            public string finishedUtc;
            public string unityVersion;
            public string target = Target.ToString();
            public string subtarget = "Player";
            public string scriptingBackend;
            public string buildFlavor;
            public string requestedBuildOptions;
            public string actualBuildOptions;
            public int requestedBuildOptionsValue;
            public int actualBuildOptionsValue;
            public bool development;
            public string[] scenes;
            public string executable;
            public string executableSha256;
            public string buildSettingsSha256;
            public string playerSettingsSha256;
            public int errors;
            public int warnings;
            public string gatewayBaseUrl;
            public string passportOrigin;
            public string termsUrl;
            public string privacyUrl;
            public ulong sizeBytes;
            public double durationSeconds;
            public string error;
        }

        // tools/build_client.ps1 使用的批处理入口；没有开发参数时始终构建正式包。
        public static void Build()
        {
            try
            {
                string output = null, gatewayUrl = null, passportOrigin = null, termsUrl = null, privacyUrl = null;
                bool development = false;
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "-clientOutputDirectory")
                        output = SingleValue(args, ref i, output);
                    else if (args[i] == "-clientGatewayUrl")
                        gatewayUrl = SingleValue(args, ref i, gatewayUrl);
                    else if (args[i] == "-clientPassportOrigin")
                        passportOrigin = SingleValue(args, ref i, passportOrigin);
                    else if (args[i] == "-clientTermsUrl")
                        termsUrl = SingleValue(args, ref i, termsUrl);
                    else if (args[i] == "-clientPrivacyUrl")
                        privacyUrl = SingleValue(args, ref i, privacyUrl);
                    else if (args[i] == "-clientDevelopment")
                    {
                        if (development) throw new ArgumentException("Duplicate development option.");
                        development = true;
                    }
                }
                if (string.IsNullOrWhiteSpace(output))
                    throw new ArgumentException("-clientOutputDirectory is required.");
                // 给了地址就写进 ClientEndpoints.json 再出包(构建完还原);没给就用工程里的(正式包会被 ClientEndpointsBuildGuard 拦下)。
                var endpoints = gatewayUrl == null && passportOrigin == null && termsUrl == null && privacyUrl == null
                    ? null
                    : new ClientEndpoints.Values
                    {
                        gatewayBaseUrl = gatewayUrl ?? string.Empty, passportOrigin = passportOrigin ?? string.Empty,
                        termsUrl = termsUrl ?? string.Empty, privacyUrl = privacyUrl ?? string.Empty,
                    };
                BuildWindows64(output, development, endpoints);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static string SingleValue(string[] args, ref int i, string current)
        {
            if (current != null || i + 1 >= args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException("Exactly one value is required for " + args[i] + ".");
            return args[++i];
        }

        /// <summary>供已协调的编辑器任务调用；输出目录必须全新或为空，不结束交互式编辑器。</summary>
        public static void BuildWindows64(string outputDirectory, bool development) => BuildWindows64(outputDirectory, development, null);

        /// <summary>
        /// endpoints 非 null 时先写进 Assets/Resources/ClientEndpoints.json 再出包,构建结束(成功或失败)还原原文
        /// (服务端 docs/design/official-passport-registration.md §3.8)。正式包的地址由 ClientEndpointsBuildGuard 在构建前核对。
        /// </summary>
        public static void BuildWindows64(string outputDirectory, bool development, ClientEndpoints.Values endpoints)
        {
            var options = development ? BuildOptions.Development : BuildOptions.None;
            var manifest = new Manifest
            {
                startedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                buildFlavor = development ? "development" : "release",
                requestedBuildOptions = options.ToString(),
                requestedBuildOptionsValue = (int)options
            };
            bool ownsDirectory = false;
            string endpointsBackup = null;
            try
            {
                if (endpoints != null)
                {
                    endpointsBackup = File.Exists(ClientEndpointsBuildGuard.ProjectFile) ? File.ReadAllText(ClientEndpointsBuildGuard.ProjectFile) : string.Empty;
                    File.WriteAllText(ClientEndpointsBuildGuard.ProjectFile, JsonUtility.ToJson(endpoints) + "\n", new UTF8Encoding(false));
                    AssetDatabase.ImportAsset(ClientEndpointsBuildGuard.ProjectFile, ImportAssetOptions.ForceUpdate);
                }
                var effective = ClientEndpointsBuildGuard.ReadProjectEndpoints();
                manifest.gatewayBaseUrl = effective.gatewayBaseUrl;
                manifest.passportOrigin = effective.passportOrigin;
                manifest.termsUrl = effective.termsUrl;
                manifest.privacyUrl = effective.privacyUrl;
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Build requires Edit mode.");
                if (EditorUserBuildSettings.activeBuildTarget != Target)
                    throw new InvalidOperationException("Start Unity with -buildTarget win64 before building.");
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, Target))
                    throw new InvalidOperationException("Windows Standalone build support is missing.");

                // 内建宏不得由项目手工强塞，否则正式包也可能包含仅供开发的实现。
                var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone)
                    .Split(new[] { ';', ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (defines.Contains("DEVELOPMENT_BUILD") || defines.Contains("UNITY_EDITOR"))
                    throw new InvalidOperationException("Reserved development/editor symbols must not be manually configured.");

                outputDirectory = Path.GetFullPath(outputDirectory);
                if (Directory.Exists(outputDirectory) && Directory.GetFileSystemEntries(outputDirectory).Length != 0)
                    throw new InvalidOperationException("Output directory must be empty: " + outputDirectory);
                Directory.CreateDirectory(outputDirectory);
                ownsDirectory = true;
                manifest.executable = Path.Combine(outputDirectory, "mmorpg.exe");
                manifest.scenes = EditorBuildSettings.scenes.Where(scene => scene != null && scene.enabled &&
                    !string.IsNullOrWhiteSpace(scene.path)).Select(scene => scene.path).ToArray();
                if (manifest.scenes.Length == 0 || manifest.scenes.Any(scene => !File.Exists(scene)))
                    throw new InvalidOperationException("Enabled saved build scenes are missing.");
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                manifest.buildSettingsSha256 = Sha256(Path.Combine(projectRoot, "ProjectSettings/EditorBuildSettings.asset"));
                manifest.playerSettingsSha256 = Sha256(Path.Combine(projectRoot, "ProjectSettings/ProjectSettings.asset"));
                manifest.scriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone).ToString();

                Debug.Log($"{Tag} target={Target} options={options} output={manifest.executable}");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = manifest.scenes,
                    locationPathName = manifest.executable,
                    target = Target,
                    targetGroup = BuildTargetGroup.Standalone,
                    subtarget = (int)StandaloneBuildSubtarget.Player,
                    options = options
                });
                if (report == null) throw new InvalidOperationException("BuildPipeline returned no report.");
                var summary = report.summary;
                manifest.actualBuildOptions = summary.options.ToString();
                manifest.actualBuildOptionsValue = (int)summary.options;
                manifest.development = (summary.options & BuildOptions.Development) != 0;
                manifest.errors = summary.totalErrors;
                manifest.warnings = summary.totalWarnings;
                manifest.sizeBytes = summary.totalSize;
                manifest.durationSeconds = summary.totalTime.TotalSeconds;
                if (summary.result != BuildResult.Succeeded || manifest.development != development)
                    throw new InvalidOperationException($"Build result={summary.result}; requested development={development}, actual={manifest.development}.");
                if (!File.Exists(manifest.executable) || !Directory.Exists(Path.Combine(outputDirectory, "mmorpg_Data")) ||
                    !File.Exists(Path.Combine(outputDirectory, "UnityPlayer.dll")))
                    throw new InvalidOperationException("Build output is incomplete.");
                manifest.executableSha256 = Sha256(manifest.executable);
                manifest.result = "Succeeded";
            }
            catch (Exception error)
            {
                manifest.error = error.ToString();
                throw;
            }
            finally
            {
                if (endpointsBackup != null)
                {
                    File.WriteAllText(ClientEndpointsBuildGuard.ProjectFile, endpointsBackup, new UTF8Encoding(false));
                    AssetDatabase.ImportAsset(ClientEndpointsBuildGuard.ProjectFile, ImportAssetOptions.ForceUpdate);
                }
                manifest.finishedUtc = DateTime.UtcNow.ToString("O");
                if (ownsDirectory)
                {
                    // 既有目录和回执绝不覆盖；失败产物也保留供检查。
                    using (var file = new FileStream(Path.Combine(outputDirectory, "build-manifest.json"), FileMode.CreateNew, FileAccess.Write))
                    using (var writer = new StreamWriter(file, new UTF8Encoding(false)))
                        writer.Write(JsonUtility.ToJson(manifest, true));
                }
            }
            Debug.Log($"{Tag} result={manifest.result} options={manifest.actualBuildOptions} manifest={Path.Combine(outputDirectory, "build-manifest.json")}");
        }

        private static string Sha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
