#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>Runs only the new mail/social tests through Unity's installed NUnit runner.</summary>
public static class MailSocialTestRunner
{
    private static TestRunnerApi _api;
    private static Callbacks _callbacks;
    public static bool IsRunning { get; private set; }
    public static string OutputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../image/designs/social-ui-v1/unity-slices/qa"));
    private static readonly string[] Assemblies = { "MmorpgClient.Tests.EditMode.Mail", "MmorpgClient.Tests.EditMode.Social" };

    [MenuItem("MMORPG/UI/Mail and social/Run targeted EditMode tests")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || IsRunning)
            throw new InvalidOperationException("A stable, idle Edit mode is required.");
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToArray();
        foreach (string assembly in Assemblies)
            if (!loaded.Contains(assembly)) throw new InvalidOperationException("Required test assembly is not loaded: " + assembly);
        Directory.CreateDirectory(OutputDirectory);
        _api = ScriptableObject.CreateInstance<TestRunnerApi>();
        _callbacks = new Callbacks(OutputDirectory);
        _api.RegisterCallbacks(_callbacks);
        IsRunning = true;
        try
        {
            _api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, assemblyNames = Assemblies }));
        }
        catch
        {
            Cleanup();
            throw;
        }
    }
    private static void Cleanup()
    {
        IsRunning = false;
        if (_api != null)
        {
            if (_callbacks != null) _api.UnregisterCallbacks(_callbacks);
            UnityEngine.Object.DestroyImmediate(_api);
        }
        _api = null;
        _callbacks = null;
    }
    [Serializable] private sealed class Summary
    {
        public string status, unityVersion, completedAtUtc;
        public int passed, failed, skipped, inconclusive, assertions;
        public double seconds;
        public string[] assemblies, failures;
        public bool liveServerVerified = false;
    }
    private sealed class Callbacks : ICallbacks
    {
        private readonly string _directory;
        private readonly List<string> _failures = new();
        public Callbacks(string directory) { _directory = directory; }
        public void RunStarted(ITestAdaptor testsToRun)
        {
            File.WriteAllText(Path.Combine(_directory, "tests-running.txt"), DateTime.UtcNow.ToString("O"));
        }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result)
        {
            if (!result.HasChildren && result.FailCount > 0) _failures.Add(result.FullName + ": " + result.Message + "\n" + result.StackTrace);
        }
        public void RunFinished(ITestResultAdaptor result)
        {
            try
            {
                TestRunnerApi.SaveResultToFile(result, Path.Combine(_directory, "editmode-results.xml"));
                var summary = new Summary { status = result.FailCount == 0 && result.PassCount > 0 && result.SkipCount == 0 && result.InconclusiveCount == 0 ? "passed" : "needs-attention",
                    unityVersion = Application.unityVersion, completedAtUtc = DateTime.UtcNow.ToString("O"),
                    passed = result.PassCount, failed = result.FailCount, skipped = result.SkipCount, inconclusive = result.InconclusiveCount,
                    assertions = result.AssertCount, seconds = result.Duration, assemblies = Assemblies, failures = _failures.ToArray() };
                File.WriteAllText(Path.Combine(_directory, "editmode-results.json"), JsonUtility.ToJson(summary, true));
                Debug.Log("MAIL_SOCIAL_TESTS_DONE|passed=" + result.PassCount + "|failed=" + result.FailCount + "|skipped=" + result.SkipCount);
            }
            finally { Cleanup(); }
        }
    }
}
#endif
