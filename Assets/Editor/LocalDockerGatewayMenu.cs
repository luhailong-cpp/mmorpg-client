#if UNITY_EDITOR
using MmorpgClient.Core;
using UnityEditor;
using UnityEngine;

public static class LocalDockerGatewayMenu
{
    private const string MenuPath = "MMORPG/本地联调/使用 Docker 网关（18085）";

    [MenuItem(MenuPath)]
    public static void UseDockerGateway()
    {
        ClientSettings.GatewayBaseUrl = "http://127.0.0.1:18085";
        Debug.Log("Saved local Docker gateway URL: http://127.0.0.1:18085. Stop and start Play Mode to apply it to an already running client.");
    }

    [MenuItem(MenuPath, true)]
    private static bool CanUseDockerGateway()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }
}
#endif
