#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using MmorpgClient.UI;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.World.CurrentMaps;
using MmorpgClient.World.Tianyong;
using UnityEngine;

namespace MmorpgClient.App
{
    /// <summary>仅显式本机开发联调用；入场与移动均走真实服务器协议。</summary>
    public sealed class FestivalMapLocalTest : MonoBehaviour
    {
        private bool _expanded = true;
        private GameplayInputBlocker _blocker;
        private string _lastAction = "登录本机测试服后选择地图和已交付外观。";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (!FestivalMapLocalTestPolicy.IsRequested(Environment.GetCommandLineArgs()) ||
                FindAnyObjectByType<FestivalMapLocalTest>() != null) return;
            var root = new GameObject("[FestivalMapLocalTest]");
            DontDestroyOnLoad(root);
            root.AddComponent<FestivalMapLocalTest>();
        }

        private void Awake()
        {
            foreach (string argument in Environment.GetCommandLineArgs())
                if (string.Equals(argument, "-festivalMapLocalVerify", StringComparison.OrdinalIgnoreCase)) _expanded = false;
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
            if (!_expanded)
            {
                if (GUI.Button(new Rect(12, 12, 210, 34), "展开：9 图本机联调")) _expanded = true;
                return;
            }
            var app = AppBootstrap.Instance;
            var game = app?.GameClient;
            var travel = CityTravelUiRoot.Instance;
            bool available = game != null && game.InGame && game.IsGateReady && travel != null &&
                FestivalMapLocalTestPolicy.CanConnect(Application.isEditor || Debug.isDebugBuild, true,
                    game.GatewayBaseUrl, game.AssignedGate);
            GUILayout.BeginArea(new Rect(12, 12, 440, 520), GUI.skin.box);
            GUILayout.Label("节庆地图 · 本机开发联调（未验收）");
            GUILayout.Label("正式传送保持关闭；仅连接回环测试服。\n19004 仅 48 格；10000 两域、35004 三域不连通。\n10000 画面接缝仍待修。", GUILayout.Height(58));
            GUILayout.Label(available
                ? $"服务器场景：{game.CurrentSceneConfigId} / 实例：{game.CurrentSceneId}\n当前外观：{app.WorldMap?.Appearance}"
                : "等待登录本机测试服（gateway 与 gate 均须回环地址）。", GUILayout.Height(40));
            GUI.enabled = available && !travel.IsLocalTestTravelPending;
            foreach (string mapId in FestivalMapLocalTestPolicy.MapIds)
            {
                var map = CurrentMapCatalog.Find("map" + mapId);
                GUILayout.BeginHorizontal();
                GUILayout.Label(mapId, GUILayout.Width(65));
                DrawAppearance(travel, map, "spring", "春节");
                DrawAppearance(travel, map, "mid-autumn", "中秋");
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("返回主城（正式传送请求）")) travel.RequestTravel(1u, "normal");
            GUI.enabled = true;
            GUILayout.Label(travel?.LocalTestTravelStatus ?? "", GUILayout.Height(40));
            GUILayout.Label(_lastAction, GUILayout.Height(40));
            if (GUILayout.Button("收起面板，测试 WASD / 点击行走")) _expanded = false;
            GUILayout.EndArea();
        }

        private void DrawAppearance(CityTravelUiRoot travel, CurrentMapDefinition map, string appearance, string label)
        {
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && FestivalMapLocalTestPolicy.IsDeliveredTarget(map, appearance);
            if (GUILayout.Button(label))
                _lastAction = travel.RequestLocalFestivalTravel(map.SceneConfigId, appearance)
                    ? "请求已发出；以服务器抵达通知与实际移动结果为准。"
                    : "当前无法请求，请确认已登录、非战斗且没有在途传送。";
            GUI.enabled = wasEnabled;
        }

        private void OnDisable()
        {
            if (_blocker != null) _blocker.enabled = false;
        }
    }
}
#endif
