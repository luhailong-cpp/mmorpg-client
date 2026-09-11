#if UNITY_EDITOR
using System.IO;
using UnityEngine;
using UnityEditor.TestTools.TestRunner.Api;

/// <summary>Keeps asynchronous test callbacks in a stable editor assembly across MCP calls.</summary>
public static class GameplayUiTestRunner
{
    private static TestRunnerApi _api;
    public static void Run()
    {
        _api = ScriptableObject.CreateInstance<TestRunnerApi>();
        _api.RegisterCallbacks(new Results());
        _api.Execute(new ExecutionSettings(new Filter
        {
            testMode = TestMode.EditMode,
            testNames = new[]
            {
                "MmorpgClient.Tests.EditMode.Battle.PlayerFeaturesClientTests",
                "MmorpgClient.Tests.EditMode.Battle.GameplayWindowTests",
                "MmorpgClient.Tests.EditMode.Ugui.QdaoServerSelectPrefabTests",
                "MmorpgClient.Tests.EditMode.Battle.AttributeClientTests",
                "MmorpgClient.Tests.EditMode.Battle.PetClientTests",
                "MmorpgClient.Tests.EditMode.Battle.PetPanelInteractionTests",
                "MmorpgClient.Tests.EditMode.Battle.BattleUiLayoutTests",
                "MmorpgClient.Tests.EditMode.Battle.BattleArtCatalogCacheTests",
                "MmorpgClient.Tests.EditMode.Battle.HudCanvasLayeringTests"
            }
        }));
    }
    private sealed class Results : ICallbacks
    {
        public void RunStarted(ITestAdaptor tests) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            var path = GameplayUiVerification.OutputDirectory;
            Directory.CreateDirectory(path);
            TestRunnerApi.SaveResultToFile(result, Path.Combine(path, "tests.xml"));
            File.WriteAllText(Path.Combine(path, "tests-status.txt"),
                $"passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount} result={result.ResultState}");
            Debug.Log("GAMEPLAY_UI_TESTS_FINISHED|" + result.ResultState);
            _api.UnregisterCallbacks(this);
            Object.DestroyImmediate(_api); _api = null;
        }
    }
}
#endif
