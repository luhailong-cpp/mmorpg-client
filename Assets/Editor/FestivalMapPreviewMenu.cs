using UnityEditor;
using UnityEditor.SceneManagement;

namespace MmorpgClient.Editor.CurrentMaps
{
    /// <summary>Open the saved offline scene without changing or starting the live game.</summary>
    public static class FestivalMapPreviewMenu
    {
        public const string ScenePath = "Assets/Scenes/World/FestivalMaps.unity";

        [MenuItem("MMORPG/Maps/节庆地图离线预览")]
        public static void OpenPreview()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("MMORPG/Maps/节庆地图离线预览", true)]
        private static bool CanOpenPreview() => !EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
