#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>在同一个验证副本中保存地图与正式窗口截图，再构建真实游戏播放器。</summary>
public static class FestivalIntegrationBuild
{
    public static void CaptureAndBuild()
    {
        // 批处理测试可能留下未命名空场景；只在验证副本批处理时打开正式已保存场景。
        if (Application.isBatchMode)
        {
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                {
                    EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
                    break;
                }
        }
        FestivalRegionMapVerification.CaptureAll();
        CityTravelUiVerification.CaptureAll();
        Debug.Log("[FestivalIntegrationBuild] 地图与传送窗口截图完成，开始正式播放器构建");
        MmorpgClient.Editor.CrossZoneVerifyBuild.Build();
    }
}
#endif
