using System.Collections;
using System;
using MmorpgClient.Game;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui;
using MmorpgClient.World;
using MmorpgClient.World.Tianyong;
using UnityEngine;

namespace MmorpgClient.UI
{
    /// <summary>
    /// Single entry point for the production client. Normally serialized in
    /// the AppRoot prefab, with auto-spawn retained for empty test scenes.
    /// Bootstraps the native Unity uGUI runtime,
    /// owns the long-lived <see cref="GameClient"/> + <see cref="GatewayHttpClient"/>
    /// and drives the production client lifecycle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AppBootstrap : MonoBehaviour
    {
        public const string GameObjectName = "[MmorpgClient]";

        private static AppBootstrap _instance;

        [Header("Persistent scene rig")]
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Light directionalSun;
        [SerializeField] private TianyongMapConfig mapConfig;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            // The sandbox owns its own camera, light and offline player. A
            // production client root would cover it with login UI and make
            // scene tests depend on network state.
            if (FindAnyObjectByType<TianyongSandboxBootstrap>() != null) return;
            if (FindAnyObjectByType<AppBootstrap>() != null) return;
            var go = new GameObject(GameObjectName);
            go.AddComponent<AppBootstrap>();
        }

        public static AppBootstrap Instance => _instance;

        public SessionModel Session { get; private set; }
        public GameClient   GameClient { get; private set; }
        public GatewayHttpClient Gateway { get; private set; }
        public QdaoUguiRuntime Ugui { get; private set; }
        public TianyongMapRuntime WorldMap { get; private set; }

        private Action<string> _gameLogHandler;
        private UnityEngine.Transform _actorWorldRoot;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                var duplicateRoot = transform.root;
                if (_instance.transform.root == duplicateRoot) Destroy(this);
                else Destroy(duplicateRoot.gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(transform.root.gameObject);
            EnsureSceneRig();

            if (mapConfig == null) mapConfig = TianyongMapConfig.LoadDefault();

            Session    = new SessionModel();  // gateway URL / last account come from ClientSettings (PlayerPrefs)
            // 自动驾驶(-zone 命令行)激活时 gateway/account/password 以命令行为准,
            // 不读 PlayerPrefs 默认值(双实例同机共用 PlayerPrefs)
            App.DevAutoPilot.ApplyToSession(Session);
            Gateway    = new GatewayHttpClient(Session.GatewayBaseUrl);
            GameClient = new GameClient(Session.GatewayBaseUrl);
            GameClient.World.SetRootParent(_actorWorldRoot);
            // 名牌显示名:本地玩家取会话里的角色名;远端玩家/NPC 的名字协议
            // (ActorCreateS2C)尚未下发,交给 ActorWorld 的种类回退("玩家"/"NPC")。
            GameClient.World.DisplayNameProvider = ResolveActorDisplayName;
            GameClient.CoroutineRunner = Run; // RedirectToGateNotify 重连流程需要宿主协程
            _gameLogHandler = s => Debug.Log("[GameClient] " + s);
            GameClient.OnLog += _gameLogHandler;
            // 无人值守流程(选区→登录→排队→自动战斗→退出);未带 -zone 时不挂,零影响。
            // 其 Start 在下一帧跑,晚于下面 RoleFlowUi.Attach,故能把 PlayerChooser 置回 null。
            App.DevAutoPilot.Attach(this);
            WorldMap = GetComponent<TianyongMapRuntime>();
            if (WorldMap == null) WorldMap = gameObject.AddComponent<TianyongMapRuntime>();
            WorldMap.Initialize(GameClient, worldCamera, directionalSun, mapConfig);

            Debug.Log("[AppBootstrap] Awake (native uGUI)");
            try
            {
                Ugui = QdaoUguiRuntime.Create(this);
                // 选角/建角界面:挂接 GameClient.PlayerChooser,EnterZone 管线
                // 在 TCP Login 后弹出(见 RoleFlowUi)
                MmorpgClient.UI.Ugui.Role.RoleFlowUi.Attach(this);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Debug.LogError(
                    "[AppBootstrap] Native uGUI bootstrap failed. " +
                    "Destroying the incomplete persistent root.");
                enabled = false;
                // Do not leave a disabled DontDestroyOnLoad singleton that
                // blocks a clean bootstrap after a scene reload. OnDestroy
                // owns the normal client/event cleanup and clears _instance.
                Destroy(transform.root.gameObject);
            }
        }

        private void Update()
        {
            GameClient?.Tick();
            GameClient?.World?.Tick();
            Ugui?.Tick(Time.deltaTime);
        }

        public Coroutine Run(IEnumerator routine) => StartCoroutine(routine);

        /// <summary>
        /// Readable nameplate text for the local player: the gateway player
        /// list entry matching <see cref="Game.GameClient.PlayerId"/>, else the
        /// nickname chosen in the role flow. Remote actors return null so the
        /// world falls back to its kind label until the protocol carries names.
        /// </summary>
        private string ResolveActorDisplayName(ActorView view)
        {
            var client = GameClient;
            var session = Session;
            if (view == null || client == null || session == null) return null;
            if (view.Kind != ActorKind.Player) return null;
            var world = client.World;
            if (world == null || !world.HasLocalPlayer || view.Entity != world.LocalEntity) return null;

            var playerId = client.PlayerId;
            if (playerId != 0)
            {
                foreach (var p in session.Players)
                {
                    if (p == null || p.player_id != playerId) continue;
                    if (!string.IsNullOrWhiteSpace(p.name)) return p.name;
                }
            }
            return string.IsNullOrWhiteSpace(session.RoleNickname) ? null : session.RoleNickname;
        }

        private void EnsureSceneRig()
        {
            if (worldCamera == null)
                worldCamera = GetComponentInChildren<Camera>(true) ?? Camera.main;
            if (worldCamera == null)
            {
                var camGo = new GameObject("[MainCamera]");
                camGo.tag = "MainCamera";
                camGo.transform.SetParent(transform, false);
                worldCamera = camGo.AddComponent<Camera>();
                worldCamera.clearFlags = CameraClearFlags.SolidColor;
                worldCamera.backgroundColor = new Color(0.20f, 0.23f, 0.19f);
                worldCamera.transform.position = new UnityEngine.Vector3(0f, 4f, -10f);
                worldCamera.transform.rotation = Quaternion.Euler(15f, 0f, 0f);
                camGo.AddComponent<AudioListener>();
            }
            else
            {
                AdoptIntoPersistentRoot(worldCamera.transform);
                if (FindAnyObjectByType<AudioListener>() == null)
                    worldCamera.gameObject.AddComponent<AudioListener>();
            }

            if (directionalSun == null)
            {
                foreach (var light in GetComponentsInChildren<Light>(true))
                {
                    if (light.type != LightType.Directional) continue;
                    directionalSun = light;
                    break;
                }
            }
            if (directionalSun == null)
            {
                foreach (var light in FindObjectsByType<Light>())
                {
                    if (light.type != LightType.Directional) continue;
                    directionalSun = light;
                    break;
                }
            }
            if (directionalSun == null)
            {
                var lightGo = new GameObject("[DirLight]");
                lightGo.transform.SetParent(transform, false);
                directionalSun = lightGo.AddComponent<Light>();
                directionalSun.type = LightType.Directional;
                directionalSun.intensity = 1.1f;
                lightGo.transform.rotation = Quaternion.Euler(40f, 30f, 0f);
            }
            else
            {
                AdoptIntoPersistentRoot(directionalSun.transform);
            }

            _actorWorldRoot = transform.Find("[ActorWorld]");
            if (_actorWorldRoot == null)
            {
                var existing = GameObject.Find("[ActorWorld]");
                _actorWorldRoot = existing != null ? existing.transform : null;
            }
            if (_actorWorldRoot == null)
            {
                var actorRootObject = new GameObject("[ActorWorld]");
                actorRootObject.transform.SetParent(transform, false);
                _actorWorldRoot = actorRootObject.transform;
            }
            else
            {
                AdoptIntoPersistentRoot(_actorWorldRoot);
            }
        }

        private void AdoptIntoPersistentRoot(UnityEngine.Transform child)
        {
            if (child == null || child == transform || child.IsChildOf(transform)) return;
            child.SetParent(transform, true);
        }

        private void OnDestroy()
        {
            if (_instance != this) return;
            _instance = null;

            StopAllCoroutines();

            if (GameClient != null)
            {
                GameClient.CoroutineRunner = null;
                if (_gameLogHandler != null) GameClient.OnLog -= _gameLogHandler;
                GameClient.Disconnect();
            }

            _gameLogHandler = null;
            WorldMap = null;
            Ugui = null;
            GameClient = null;
            Gateway = null;
            Session = null;
            _actorWorldRoot = null;
        }

    }
}
