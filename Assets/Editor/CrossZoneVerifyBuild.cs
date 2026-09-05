using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MmorpgClient.Editor
{
    /// <summary>
    /// 跨区匹配验证用播放器出包(batchmode -executeMethod 入口)。
    /// 用主工程真实的 EditorBuildSettings 场景列表构建 Windows 64 Development 播放器
    /// 到 E:/work/tmp/crosszone_player/mmorpg.exe;两份实例由
    /// tools/run_crosszone_pair.ps1 以不同 -zone/-account 启动(见 DevAutoPilot)。
    ///
    /// 调用(tools/build_crosszone_player.ps1 封装):
    ///   Unity.exe -batchmode -nographics -quit -projectPath E:/work/mmorpg-client
    ///     -executeMethod MmorpgClient.Editor.CrossZoneVerifyBuild.Build
    ///     [-crossZoneOut E:/work/tmp/crosszone_player] -logFile E:/work/tmp/crosszone_build.log
    /// 出包失败以退出码 1 结束编辑器进程(照 TianyongVerifyBuild 的做法)。
    /// </summary>
    public static class CrossZoneVerifyBuild
    {
        public const string DefaultOutputDir = "E:/work/tmp/crosszone_player";
        public const string ExeName = "mmorpg.exe";

        /// <summary>只做编译检查:batchmode 能走到这里即脚本编译通过。</summary>
        public static void CompileCheck()
        {
            Debug.Log("[CrossZoneVerifyBuild] compile check ok");
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
                Debug.LogError("[CrossZoneVerifyBuild] EditorBuildSettings 没有启用的场景,无法出包");
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log($"[CrossZoneVerifyBuild] scenes={string.Join(";", scenes)} out={outDir}");

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                Debug.LogError("[CrossZoneVerifyBuild] 本机 Unity 未安装 Windows Standalone 构建支持");
                EditorApplication.Exit(1);
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = Path.Combine(outDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                // Development:播放器日志保留 Debug.Log 全文(DevAutoPilot 的 RESULT 行靠它解析)
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
            Debug.Log($"[CrossZoneVerifyBuild] result={summary.result} errors={summary.totalErrors} " +
                      $"warnings={summary.totalWarnings} size={summary.totalSize} time={summary.totalTime} out={summary.outputPath}");
            if (summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
                return;
            }
            if (!File.Exists(options.locationPathName))
            {
                Debug.LogError($"[CrossZoneVerifyBuild] 报告成功但找不到 {options.locationPathName}");
                EditorApplication.Exit(1);
            }
        }

        private static string ResolveOutputDir()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-crossZoneOut", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return DefaultOutputDir;
        }
    }
}
