#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Batch-only capture of the production gameplay window; no account or network owner.</summary>
public static class InventoryLightControlVerification
{
    public static void CaptureAll()
    {
        if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("This verification entry is for batch Edit Mode only.");
        // Unity cannot add an Editor fixture to an unnamed, unsaved initial scene.
        // Opening the saved scene in Edit Mode does not start AppBootstrap gameplay.
        if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity", OpenSceneMode.Single);
        string previousOutput = GameplayUiVerification.OutputDirectory;
        try
        {
            GameplayUiVerification.OutputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../image/designs/ui-light-control-20261023/inventory/qa/production-render"));
            GameplayUiVerification.CaptureAll();
        }
        finally
        {
            GameplayUiVerification.OutputDirectory = previousOutput;
        }
    }
}
#endif
