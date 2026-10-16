using System;
using System.IO;
using MmorpgClient.World.Tianyong;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MmorpgClient.Editor.Tianyong
{
    public static class NpcArtShowroomBuild
    {
        private const string ScenePath = "Assets/Scenes/World/NpcArtShowroom.unity";

        public static void Build()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                    throw new InvalidOperationException("Windows Standalone build support is unavailable.");
                string output = Argument("-npcShowroomOut") ?? Path.Combine(Directory.GetCurrentDirectory(), "Builds", "NpcShowroom");
                output = Path.GetFullPath(output);
                Directory.CreateDirectory(output);
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camera = new GameObject("ShowroomCamera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.97f, 0.95f, 0.88f);
                camera.cullingMask = 0;
                new GameObject("NpcArtShowroom").AddComponent<NpcArtShowroom>();
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Cannot save NPC showroom scene.");
                AssetDatabase.SaveAssets();
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath }, target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone, options = BuildOptions.Development,
                    locationPathName = Path.Combine(output, "NpcArtShowroom.exe")
                });
                var summary = report.summary;
                Debug.Log("[NpcShowroomBuild] result=" + summary.result + " errors=" + summary.totalErrors +
                          " output=" + summary.outputPath + " bytes=" + summary.totalSize);
                if (summary.result != BuildResult.Succeeded || !File.Exists(Path.Combine(output, "NpcArtShowroom.exe")))
                    throw new InvalidOperationException("NPC showroom player build failed.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
            finally
            {
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }

        private static string Argument(string key)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("-"))
                        throw new ArgumentException(key + " requires an output directory.");
                    return args[i + 1];
                }
            return null;
        }
    }
}
