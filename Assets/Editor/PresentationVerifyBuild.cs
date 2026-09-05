using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MmorpgClient.Editor
{
    /// <summary>
    /// 回合战斗表现层验证用播放器出包(batchmode -executeMethod 入口)。
    /// 构建方法与 <see cref="CrossZoneVerifyBuild"/> 相同(真实 EditorBuildSettings 场景列表、
    /// Windows 64 Development),但产物目录独立:E:/work/tmp/presentation_player/mmorpg.exe,
    /// 避免与跨区验证那条线的产物互相覆盖。
    ///
    /// 调用(在副本工程上跑,不要指向用户开着的编辑器工程):
    ///   Unity.exe -batchmode -nographics -quit -projectPath E:/work/tmp/presentation_verify_project
    ///     -executeMethod MmorpgClient.Editor.PresentationVerifyBuild.Build
    ///     [-presentationOut E:/work/tmp/presentation_player] -logFile E:/work/tmp/presentation_build.log
    /// 出包失败以退出码 1 结束编辑器进程。
    /// </summary>
    public static class PresentationVerifyBuild
    {
        public const string DefaultOutputDir = "E:/work/tmp/presentation_player";
        public const string ExeName = "mmorpg.exe";
        private const string Tag = "[PresentationVerifyBuild]";

        /// <summary>只做编译检查:batchmode 能走到这里即脚本编译通过。</summary>
        public static void CompileCheck()
        {
            Debug.Log($"{Tag} compile check ok");
        }

        public static void Build()
        {
            string outDir = ResolveOutputDir();
            Directory.CreateDirectory(outDir);

            var scenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s == null || !s.enabled || string.IsNullOrEmpty(s.path)) continue;
                scenes.Add(s.path);
            }
            if (scenes.Count == 0)
            {
                Debug.LogError($"{Tag} EditorBuildSettings 没有启用的场景,无法出包");
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log($"{Tag} scenes={string.Join(";", scenes)} out={outDir}");

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                Debug.LogError($"{Tag} 本机 Unity 未安装 Windows Standalone 构建支持");
                EditorApplication.Exit(1);
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = Path.Combine(outDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                // Development:播放器日志保留 Debug.Log 全文,便于对照演出日志逐条勾
                options = BuildOptions.Development,
            };

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
            Debug.Log($"{Tag} result={summary.result} errors={summary.totalErrors} " +
                      $"warnings={summary.totalWarnings} size={summary.totalSize} time={summary.totalTime} out={summary.outputPath}");
            if (summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
                return;
            }
            if (!File.Exists(options.locationPathName))
            {
                Debug.LogError($"{Tag} 报告成功但找不到 {options.locationPathName}");
                EditorApplication.Exit(1);
            }
        }

        private static string ResolveOutputDir()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-presentationOut", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return DefaultOutputDir;
        }
    }
}
