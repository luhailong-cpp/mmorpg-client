using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MmorpgClient.Editor.Tianyong
{
    /// <summary>
    /// 主城移动观感离线验收专用播放器出包(batchmode -executeMethod 入口):只打沙盒场景
    /// (<see cref="TianyongProjectSetup.SandboxScenePath"/>),播放器启动即进入天墉城离线沙盒,
    /// 配合 <c>-sandboxDrive -shotDir</c> 由 TianyongSandboxAutoDrive 自动走位截图。
    ///
    /// 调用(必须在副本工程上跑,不要指向用户开着的编辑器工程):
    ///   Unity.exe -batchmode -nographics -quit -projectPath E:/work/tmp/citymove_verify_project
    ///     -executeMethod MmorpgClient.Editor.Tianyong.TianyongSandboxVerifyBuild.Build
    ///     [-sandboxOut E:/work/tmp/citymove_player/after] -logFile ...
    /// 出包失败以退出码 1 结束编辑器进程。
    /// </summary>
    public static class TianyongSandboxVerifyBuild
    {
        public const string DefaultOutputDir = "E:/work/tmp/citymove_player";
        public const string ExeName = "mmorpg_sandbox.exe";
        private const string Tag = "[SandboxVerifyBuild]";

        public static void Build()
        {
            var outDir = ResolveOutputDir();
            Directory.CreateDirectory(outDir);

            var scene = TianyongProjectSetup.SandboxScenePath;
            if (!File.Exists(scene))
            {
                Debug.LogError($"{Tag} sandbox scene missing: {scene}");
                EditorApplication.Exit(1);
                return;
            }
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                Debug.LogError($"{Tag} Windows Standalone build support is not installed");
                EditorApplication.Exit(1);
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = Path.Combine(outDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.Development,
            };
            Debug.Log($"{Tag} scene={scene} out={options.locationPathName}");

            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
                return;
            }

            var summary = report.summary;
            Debug.Log($"{Tag} result={summary.result} errors={summary.totalErrors} warnings={summary.totalWarnings} " +
                      $"size={summary.totalSize} time={summary.totalTime} out={summary.outputPath}");
            if (summary.result != BuildResult.Succeeded || !File.Exists(options.locationPathName))
                EditorApplication.Exit(1);
        }

        private static string ResolveOutputDir()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-sandboxOut", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return DefaultOutputDir;
        }
    }
}
