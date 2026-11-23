using UnityEditor;
using UnityEditor.SceneManagement;

namespace MmorpgClient.Editor.CurrentMaps
{
    /// <summary>Open the saved offline scene without changing or starting the live game.</summary>
    public static class FestivalMapPreviewMenu
    {
        public const string ScenePath = "Assets/Scenes/World/FestivalMaps.unity";
        public const string BajingBodyScenePath = "Assets/Scenes/World/BajingBody.unity";
        public const string BiyuanBodyScenePath = "Assets/Scenes/World/BiyuanBody.unity";
        public const string YuexiBayScenePath = "Assets/Scenes/World/YuexiBayPreview.unity";
        public const string YuexiBayPlayableScenePath = "Assets/Scenes/World/YuexiBay.unity";
        public const string PcOriginalPreviewScenePath = "Assets/Scenes/World/PcOriginalMapPreview.unity";

        [MenuItem("MMORPG/Maps/节庆地图离线预览")]
        public static void OpenPreview() => OpenScene(ScenePath);

        [MenuItem("MMORPG/Maps/八景岛双节本体预览")]
        public static void OpenBajingBody() => OpenScene(BajingBodyScenePath);

        [MenuItem("MMORPG/Maps/碧渊水宫双节本体走图")]
        public static void OpenBiyuanBody() => OpenScene(BiyuanBodyScenePath);

        [MenuItem("MMORPG/Maps/月汐湾双节离线预览")]
        public static void OpenYuexiBayPreview() => OpenScene(YuexiBayScenePath);

        [MenuItem("MMORPG/Maps/月汐湾双节走图")]
        public static void OpenYuexiBayPlayable() => OpenScene(YuexiBayPlayableScenePath);

        [MenuItem("MMORPG/Maps/端游原图静态背景预览 (36图)")]
        public static void OpenPcOriginalPreview() => OpenScene(PcOriginalPreviewScenePath);

        private static void OpenScene(string scene)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
        }

        [MenuItem("MMORPG/Maps/节庆地图离线预览", true)]
        [MenuItem("MMORPG/Maps/八景岛双节本体预览", true)]
        [MenuItem("MMORPG/Maps/碧渊水宫双节本体走图", true)]
        [MenuItem("MMORPG/Maps/月汐湾双节离线预览", true)]
        [MenuItem("MMORPG/Maps/月汐湾双节走图", true)]
        [MenuItem("MMORPG/Maps/端游原图静态背景预览 (36图)", true)]
        private static bool CanOpenPreview() => !EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
