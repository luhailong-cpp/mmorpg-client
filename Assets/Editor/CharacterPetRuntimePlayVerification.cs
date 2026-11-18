using System;
using System.IO;
using MmorpgClient.App;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MmorpgClient.Editor
{
    /// <summary>独占编辑器的真实 Bootstrap Play 验收入口；不用离线预览场景。</summary>
    [InitializeOnLoad]
    public static class CharacterPetRuntimePlayVerification
    {
        private const string Armed = "character-pet-real-runtime-armed";
        private const string Started = "character-pet-real-runtime-started";
        private static double _nextPoll;
        static CharacterPetRuntimePlayVerification()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-characterPetQaPlan") < 0) return;
            if (SessionState.GetBool(Armed, false)) EditorApplication.update += Tick;
            else EditorApplication.update += StartWhenReady;
        }

        private static void StartWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= StartWhenReady;
            if (!SessionState.GetBool(Armed, false)) Run();
        }

        public static void Run()
        {
            try
            {
                if (SessionState.GetBool(Armed, false)) return;
                Debug.Log("[CharacterPetRuntimePlayVerification] Starting production Bootstrap Play");
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Requires a fresh dedicated Edit mode session");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Refusing to replace an unsaved scene");
                string path = PlanPath();
                if (!File.Exists(path)) throw new FileNotFoundException("Explicit QA plan is required", path);
                EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity", OpenSceneMode.Single);
                CharacterPetRuntimeVerification.SetEditorGameViewResolution(2560, 1080);
                SessionState.SetBool(Armed, true);
                SessionState.SetString(Started, DateTime.UtcNow.ToString("O"));
                EditorApplication.update -= Tick; EditorApplication.update += Tick;
                Debug.Log("[CharacterPetRuntimePlayVerification] Requesting Play mode");
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + .5;
            try
            {
                if ((DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Started, DateTime.UtcNow.ToString("O")), null,
                    System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime()).TotalSeconds > 420)
                    throw new TimeoutException("Dedicated Bootstrap Play verification timed out");
                var plan = JsonUtility.FromJson<CharacterPetRuntimeVerification.Plan>(File.ReadAllText(PlanPath()));
                string reportPath = Path.Combine(plan.outputDirectory, "runtime-report.json");
                if (!File.Exists(reportPath)) return;
                var report = JsonUtility.FromJson<CharacterPetRuntimeVerification.Report>(File.ReadAllText(reportPath));
                if (report == null || report.result == "running" || EditorApplication.isPlayingOrWillChangePlaymode) return;
                SessionState.SetBool(Armed, false);
                EditorApplication.update -= Tick;
                EditorApplication.Exit(report.error == null || report.error.Length == 0 ? 0 : 1);
            }
            catch (IOException) { /* 报告正被帧末写入，下次轮询。 */ }
            catch (Exception error) { Debug.LogException(error); SessionState.SetBool(Armed, false); EditorApplication.Exit(1); }
        }

        private static string PlanPath()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-characterPetQaPlan") return args[i + 1];
            throw new ArgumentException("-characterPetQaPlan is required");
        }
    }
}
