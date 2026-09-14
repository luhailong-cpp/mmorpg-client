#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Runs the authored roster checks in the existing editor and keeps callbacks across domain reloads.</summary>
[InitializeOnLoad]
public static class QdaoNaturalLuVerification
{
    private const string Key = "QdaoNaturalLuVerification.";
    private const string CaptureVariable = "QDAO_ROSTER_CAPTURE_DIR";
    private static Results callbacks;
    private static readonly MethodInfo IsRunActiveMethod = typeof(TestRunnerApi).GetMethod(
        "IsRunActive", BindingFlags.NonPublic | BindingFlags.Static);

    static QdaoNaturalLuVerification()
    {
        if (SessionState.GetBool(Key + "active", false)) RegisterCallbacks();
    }

    public static string OutputDirectory => Path.GetFullPath(
        Path.Combine(Application.dataPath, "../Docs/ArtEvidence/v12-natural-lu"));

    public static string DescribeState()
    {
        var blockers = GetBlockers();
        return JsonUtility.ToJson(new State
        {
            project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
            unityVersion = Application.unityVersion,
            isPlaying = EditorApplication.isPlayingOrWillChangePlaymode,
            isCompiling = EditorApplication.isCompiling,
            isUpdating = EditorApplication.isUpdating,
            activeRun = SessionState.GetString(Key + "runId", ""),
            mode = SessionState.GetString(Key + "mode", ""),
            outputDirectory = OutputDirectory,
            blockers = blockers.ToArray()
        });
    }

    public static string RunEditMode() => Run(TestMode.EditMode);
    public static string RunPlayMode() => Run(TestMode.PlayMode);

    private static List<string> GetBlockers()
    {
        var blockers = new List<string>();
        if (EditorApplication.isPlayingOrWillChangePlaymode) blockers.Add("Editor is playing or changing PlayMode.");
        if (EditorApplication.isCompiling) blockers.Add("Editor is compiling.");
        if (EditorApplication.isUpdating) blockers.Add("Editor is importing or updating assets.");
        if (SessionState.GetBool(Key + "active", false)) blockers.Add("A Qdao verification run is already active.");
        if (IsRunActiveMethod == null) blockers.Add("Cannot inspect TestRunnerApi.IsRunActive; refusing to start.");
        else if ((bool)IsRunActiveMethod.Invoke(null, null)) blockers.Add("Another Unity test run is active.");
        for (var index = 0; index < SceneManager.sceneCount; index++)
        {
            var scene = SceneManager.GetSceneAt(index);
            if (scene.isDirty) blockers.Add("Dirty scene: " + (string.IsNullOrEmpty(scene.path) ? scene.name + " (unsaved)" : scene.path));
        }
        var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage != null && prefabStage.scene.isDirty)
            blockers.Add("Dirty prefab scene: " + prefabStage.assetPath);
        return blockers;
    }

    private static string Run(TestMode mode)
    {
        var blockers = GetBlockers();
        if (blockers.Count != 0) throw new InvalidOperationException(string.Join("\n", blockers));
        var label = mode == TestMode.EditMode ? "editmode" : "playmode";
        Directory.CreateDirectory(OutputDirectory);
        var oldCapture = Environment.GetEnvironmentVariable(CaptureVariable);
        SessionState.SetBool(Key + "captureWasSet", oldCapture != null);
        SessionState.SetString(Key + "oldCapture", oldCapture ?? "");
        SessionState.SetString(Key + "mode", label);
        SessionState.SetString(Key + "runId", "pending");
        SessionState.SetBool(Key + "active", true);
        if (mode == TestMode.PlayMode) Environment.SetEnvironmentVariable(CaptureVariable, OutputDirectory);
        RegisterCallbacks();
        TestRunnerApi api = null;
        try
        {
            WriteStatus("started", "pending");
            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var filter = mode == TestMode.EditMode
                ? new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { "MmorpgClient.Tests.EditMode.Tianyong" } }
                : new Filter
                {
                    testMode = TestMode.PlayMode,
                    testNames = new[]
                    {
                        "MmorpgClient.Tests.PlayMode.QdaoBoySpriteAnimatorPlayModeTests",
                        "MmorpgClient.Tests.PlayMode.QdaoRosterAnimatorPlayModeTests",
                        "MmorpgClient.Tests.PlayMode.QdaoRosterSandboxPlayModeTests"
                    }
                };
            var runId = api.Execute(new ExecutionSettings(filter));
            if (SessionState.GetBool(Key + "active", false))
            {
                SessionState.SetString(Key + "runId", runId);
                WriteStatus("started", runId);
            }
            return "QDAO_NATURAL_LU_STARTED|" + label + "|" + runId;
        }
        catch (Exception error)
        {
            try { WriteStatus("error", error.ToString()); }
            finally { ClearRun(); }
            throw;
        }
        finally
        {
            if (api != null) UnityEngine.Object.DestroyImmediate(api);
        }
    }

    private static void RegisterCallbacks()
    {
        if (callbacks != null) return;
        callbacks = new Results();
        TestRunnerApi.RegisterTestCallback(callbacks);
    }

    private static void WriteStatus(string state, string detail)
    {
        var mode = SessionState.GetString(Key + "mode", "unknown");
        File.WriteAllText(Path.Combine(OutputDirectory, mode + "-status.txt"),
            "state=" + state + "\nmode=" + mode + "\nrunId=" + SessionState.GetString(Key + "runId", "") +
            "\nutc=" + DateTime.UtcNow.ToString("O") + "\n" + detail + "\n");
    }

    private static void ClearRun()
    {
        Environment.SetEnvironmentVariable(CaptureVariable,
            SessionState.GetBool(Key + "captureWasSet", false) ? SessionState.GetString(Key + "oldCapture", "") : null);
        SessionState.EraseBool(Key + "active");
        SessionState.EraseBool(Key + "captureWasSet");
        SessionState.EraseString(Key + "oldCapture");
        SessionState.EraseString(Key + "runId");
        SessionState.EraseString(Key + "mode");
        if (callbacks != null) TestRunnerApi.UnregisterTestCallback(callbacks);
        callbacks = null;
    }

    private sealed class Results : IErrorCallbacks
    {
        public void RunStarted(ITestAdaptor tests) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            try
            {
                var mode = SessionState.GetString(Key + "mode", "unknown");
                TestRunnerApi.SaveResultToFile(result, Path.Combine(OutputDirectory, mode + "-tests.xml"));
                WriteStatus("finished", "passed=" + result.PassCount + " failed=" + result.FailCount +
                    " skipped=" + result.SkipCount + " inconclusive=" + result.InconclusiveCount + " result=" + result.ResultState);
                Debug.Log("QDAO_NATURAL_LU_FINISHED|" + mode + "|" + result.ResultState);
            }
            finally { ClearRun(); }
        }
        public void OnError(string message)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            try { WriteStatus("error", message); }
            finally { ClearRun(); }
        }
    }

    [Serializable]
    private sealed class State
    {
        public string project, unityVersion, activeRun, mode, outputDirectory;
        public bool isPlaying, isCompiling, isUpdating;
        public string[] blockers;
    }
}
#endif
