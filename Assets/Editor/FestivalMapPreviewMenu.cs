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
        public const string WaterwheelCourtyardScenePath = "Assets/Scenes/World/WaterwheelCourtyard.unity";
        public const string SummitSnowScenePath = "Assets/Scenes/World/SummitSnow.unity";
        public const string Map19003ScenePath = "Assets/Scenes/World/Map19003.unity";
        public const string QinglanTownScenePath = "Assets/Scenes/World/QinglanTown.unity";
        public const string CanglanHarborScenePath = "Assets/Scenes/World/CanglanHarbor.unity";
        public const string LingheFerryScenePath = "Assets/Scenes/World/LingheFerry.unity";
        public const string YuexiBayScenePath = "Assets/Scenes/World/YuexiBayPreview.unity";
        public const string YuexiBayPlayableScenePath = "Assets/Scenes/World/YuexiBay.unity";
        public const string PcOriginalPreviewScenePath = "Assets/Scenes/World/PcOriginalMapPreview.unity";
        public const string GuidengPreviewScenePath = "Assets/Scenes/World/GuidengMarketPreview.unity";

        [MenuItem("MMORPG/Maps/节庆地图离线预览")]
        public static void OpenPreview() => OpenScene(ScenePath);

        [MenuItem("MMORPG/Maps/八景岛双节本体预览")]
        public static void OpenBajingBody() => OpenScene(BajingBodyScenePath);

        [MenuItem("MMORPG/Maps/碧渊水宫双节本体走图")]
        public static void OpenBiyuanBody() => OpenScene(BiyuanBodyScenePath);

        [MenuItem("MMORPG/Maps/水车庭院双节走图")]
        public static void OpenWaterwheelCourtyard() => OpenScene(WaterwheelCourtyardScenePath);

        [MenuItem("MMORPG/Maps/山顶雪区双节走图")]
        public static void OpenSummitSnow() => OpenScene(SummitSnowScenePath);

        [MenuItem("MMORPG/Maps/地图 19003 双节走图")]
        public static void OpenMap19003() => OpenScene(Map19003ScenePath);

        [MenuItem("MMORPG/Maps/青岚镇双节走图")]
        public static void OpenQinglanTown() => OpenScene(QinglanTownScenePath);

        [MenuItem("MMORPG/Maps/沧澜港双节走图")]
        public static void OpenCanglanHarbor() => OpenScene(CanglanHarborScenePath);

        [MenuItem("MMORPG/Maps/灵禾渡双节走图")]
        public static void OpenLingheFerry() => OpenScene(LingheFerryScenePath);

        [MenuItem("MMORPG/Maps/月汐湾双节离线预览")]
        public static void OpenYuexiBayPreview() => OpenScene(YuexiBayScenePath);

        [MenuItem("MMORPG/Maps/月汐湾双节走图")]
        public static void OpenYuexiBayPlayable() => OpenScene(YuexiBayPlayableScenePath);

        [MenuItem("MMORPG/Maps/端游原图静态背景预览 (36图)")]
        public static void OpenPcOriginalPreview() => OpenScene(PcOriginalPreviewScenePath);

        [MenuItem("MMORPG/Maps/桂灯集双节预览")]
        public static void OpenGuidengPreview() => OpenScene(GuidengPreviewScenePath);

        private static void OpenScene(string scene)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
        }

        [MenuItem("MMORPG/Maps/节庆地图离线预览", true)]
        [MenuItem("MMORPG/Maps/八景岛双节本体预览", true)]
        [MenuItem("MMORPG/Maps/碧渊水宫双节本体走图", true)]
        [MenuItem("MMORPG/Maps/水车庭院双节走图", true)]
        [MenuItem("MMORPG/Maps/山顶雪区双节走图", true)]
        [MenuItem("MMORPG/Maps/地图 19003 双节走图", true)]
        [MenuItem("MMORPG/Maps/青岚镇双节走图", true)]
        [MenuItem("MMORPG/Maps/沧澜港双节走图", true)]
        [MenuItem("MMORPG/Maps/灵禾渡双节走图", true)]
        [MenuItem("MMORPG/Maps/月汐湾双节离线预览", true)]
        [MenuItem("MMORPG/Maps/月汐湾双节走图", true)]
        [MenuItem("MMORPG/Maps/端游原图静态背景预览 (36图)", true)]
        [MenuItem("MMORPG/Maps/桂灯集双节预览", true)]
        private static bool CanOpenPreview() => !EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
