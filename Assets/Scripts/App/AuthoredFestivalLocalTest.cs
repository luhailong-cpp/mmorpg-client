#if UNITY_EDITOR || DEVELOPMENT_BUILD
using MmorpgClient.UI;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.World.CurrentMaps;
using MmorpgClient.World.Tianyong;
using UnityEngine;

namespace MmorpgClient.App
{
    /// <summary>显式开启的手绘地图联调入口，所有入场均交给真实服务器。</summary>
    public sealed class AuthoredFestivalLocalTest : MonoBehaviour
    {
        private bool _expanded = true;
        private GameplayInputBlocker _blocker;
        private string _status = "登录本机测试服后选择地图。";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (!AuthoredMapRegistry.IsEnabled || FindAnyObjectByType<AuthoredFestivalLocalTest>() != null) return;
            var root = new GameObject("[AuthoredFestivalLocalTest]");
            DontDestroyOnLoad(root);
            root.AddComponent<AuthoredFestivalLocalTest>();
        }

        private void Awake()
        {
            _blocker = gameObject.AddComponent<GameplayInputBlocker>();
            _blocker.enabled = false;
        }

        private void Update()
        {
            var game = AppBootstrap.Instance?.GameClient;
            _blocker.enabled = game != null && game.InGame &&
                (_expanded || (CityTravelUiRoot.Instance?.IsLocalTestTravelPending ?? false));
        }

        private void OnGUI()
        {
            var app = AppBootstrap.Instance;
            var game = app?.GameClient;
            if (game == null || !game.InGame) return;
            if (!_expanded)
            {
                if (GUI.Button(new Rect(12, 12, 210, 34), "展开：手绘地图试走")) _expanded = true;
                return;
            }
            var travel = CityTravelUiRoot.Instance;
            bool available = game.IsGateReady && travel != null &&
                AuthoredFestivalLocalPolicy.CanUse(game.GatewayBaseUrl, game.AssignedGate);
            GUILayout.BeginArea(new Rect(12, 12, 420, 245), GUI.skin.box);
            GUILayout.Label("手绘地图 · 本机联调");
            GUILayout.Label(available
                ? $"服务器场景：{game.CurrentSceneConfigId} / 实例：{game.CurrentSceneId}\n当前外观：{app.WorldMap?.Appearance}"
                : "等待连接本机 18085 网关和 28000 游戏服。", GUILayout.Height(40));
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && available && !travel.IsLocalTestTravelPending;
            if (GUILayout.Button("进入 26000 中秋")) Request(travel, 26000);
            if (GUILayout.Button("进入 26008 春节")) Request(travel, 26008);
            GUI.enabled = previousEnabled;
            GUILayout.Label(travel?.LocalTestTravelStatus ?? "", GUILayout.Height(26));
            GUILayout.Label(_status, GUILayout.Height(26));
            if (GUILayout.Button("收起面板，开始试走")) _expanded = false;
            GUILayout.EndArea();
        }

        private void Request(CityTravelUiRoot travel, uint sceneConfigId)
        {
            _status = travel.RequestLocalAuthoredTravel(sceneConfigId)
                ? "请求已发出，以服务器抵达通知为准。"
                : "无法请求，请确认已登录、非战斗且地图已就绪。";
        }

        private void OnDisable()
        {
            if (_blocker != null) _blocker.enabled = false;
        }
    }
}
#endif
