using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MmorpgClient.Editor.CurrentMaps
{
    /// <summary>
    /// 只构建已保存的节庆地图离线场景，不修改工程的场景清单或脚本后端。
    /// 批处理入口：-buildTarget Win64 -executeMethod
    /// MmorpgClient.Editor.CurrentMaps.FestivalVisualPreviewBuild.Build
    /// [-festivalBuildOut &lt;完整 exe 路径&gt;]；输出目录必须为空，避免覆盖其他客户端。
    /// </summary>
    public static class FestivalVisualPreviewBuild
    {
        public const string DefaultOutput = "Builds/festival-maps-offline-20261112/player/FestivalMaps.exe";
        private const string Tag = "[FestivalVisualPreviewBuild]";
        private const BuildTarget Target = BuildTarget.StandaloneWindows64;

        [Serializable]
        private sealed class Report
        {
            public string result = "Failed";
            public string scope = "Offline FestivalMaps visual preview only; no online or navigation acceptance";
            public string startedUtc;
            public string finishedUtc;
            public string unityVersion;
            public string[] scenes;
            public string target;
            public string scriptingBackend;
            public string buildOptions;
            public string executable;
            public string executableSha256;
            public string sceneSha256;
            public string previewCatalogSha256;
            public string error;
            public int errors;
            public int warnings;
            public ulong sizeBytes;
            public double durationSeconds;
        }

        [MenuItem("MMORPG/Maps/打包三套离线视觉预览")]
        public static void Build()
        {
            string outputDirectory = null;
            bool ownsOutputDirectory = false;
            var report = new Report
            {
                startedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                scenes = new[] { FestivalMapPreviewMenu.ScenePath },
                target = Target.ToString(),
                buildOptions = BuildOptions.Development.ToString()
            };
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Build requires an Edit mode session.");
                if (EditorUserBuildSettings.activeBuildTarget != Target)
                    throw new InvalidOperationException("Start Unity with -buildTarget Win64 before building this player.");
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, Target))
                    throw new InvalidOperationException("Windows Standalone build support is not installed.");

                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                report.executable = ResolveOutput(projectRoot);
                if (!string.Equals(Path.GetExtension(report.executable), ".exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("-festivalBuildOut must name a Windows .exe file.");
                outputDirectory = Path.GetDirectoryName(report.executable);
                // UnityPlayer.dll 和数据目录也不能覆盖；不只检查同名 exe。
                if (Directory.Exists(outputDirectory) && Directory.GetFileSystemEntries(outputDirectory).Length != 0)
                    throw new InvalidOperationException("Output directory must be empty: " + outputDirectory);
                Directory.CreateDirectory(outputDirectory);
                ownsOutputDirectory = true;

                string scenePath = Path.Combine(projectRoot, FestivalMapPreviewMenu.ScenePath);
                if (!File.Exists(scenePath))
                    throw new FileNotFoundException("Saved offline scene is missing.", scenePath);
                report.sceneSha256 = Sha256(scenePath);
                string catalogPath = Path.Combine(projectRoot,
                    "Assets/Resources/World/FestivalPreview20261111/preview-catalog.json");
                if (File.Exists(catalogPath)) report.previewCatalogSha256 = Sha256(catalogPath);
                report.scriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone).ToString();

                Debug.Log($"{Tag} scene={FestivalMapPreviewMenu.ScenePath} target={Target} " +
                          $"backend={report.scriptingBackend} out={report.executable}");
                BuildReport build = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = report.scenes,
                    locationPathName = report.executable,
                    target = Target,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.Development
                });
                if (build == null) throw new InvalidOperationException("BuildPipeline returned no BuildReport.");
                var summary = build.summary;
                report.result = summary.result.ToString();
                report.errors = summary.totalErrors;
                report.warnings = summary.totalWarnings;
                report.sizeBytes = summary.totalSize;
                report.durationSeconds = summary.totalTime.TotalSeconds;
                if (summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException($"Build result={summary.result}, errors={summary.totalErrors}.");
                if (!File.Exists(report.executable))
                    throw new FileNotFoundException("Build succeeded but executable is missing.", report.executable);
                report.executableSha256 = Sha256(report.executable);
            }
            catch (Exception error)
            {
                report.result = "Failed";
                report.error = error.ToString();
                Debug.LogException(error);
            }
            finally
            {
                report.finishedUtc = DateTime.UtcNow.ToString("O");
                // 已有包的目录不属于本次构建，失败报告也不能写入其中。
                if (ownsOutputDirectory)
                {
                    try
                    {
                        File.WriteAllText(Path.Combine(outputDirectory, "build-summary.json"),
                            JsonUtility.ToJson(report, true), new UTF8Encoding(false));
                    }
                    catch (Exception error)
                    {
                        report.result = "Failed";
                        report.error = "Could not write build summary: " + error;
                        Debug.LogException(error);
                    }
                }
            }

            Debug.Log($"{Tag} result={report.result} errors={report.errors} warnings={report.warnings} " +
                      $"size={report.sizeBytes} out={report.executable}");
            if (report.result == BuildResult.Succeeded.ToString()) return;
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw new InvalidOperationException(report.error ?? "Offline build failed.");
        }

        private static string ResolveOutput(string projectRoot)
        {
            string output = DefaultOutput;
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (!string.Equals(arguments[i], "-festivalBuildOut", StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[i + 1]) || arguments[i + 1][0] == '-')
                    throw new ArgumentException("-festivalBuildOut requires an executable path.");
                output = arguments[++i];
            }
            return Path.GetFullPath(Path.IsPathRooted(output) ? output : Path.Combine(projectRoot, output));
        }

        private static string Sha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
