using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// Offline, server-independent Tianyong test harness. Open the sandbox
    /// scene and press Play to validate rendering, collision and movement.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TianyongSandboxBootstrap : MonoBehaviour
    {
        [SerializeField] private TianyongMapConfig config;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Light directionalLight;
        [SerializeField] private GameObject debugPlayerPrefab;
        [SerializeField] private TianyongTheme initialTheme = TianyongTheme.City;
        [SerializeField] private bool showHelp = true;

        private TianyongMapInstance _map;
        private TianyongCameraController _cameraController;
        private TianyongPlayerController _playerController;

        public TianyongMapInstance Map => _map;
        public GameObject Player => _playerController != null ? _playerController.gameObject : null;
        public Camera WorldCamera => worldCamera;

        /// <summary>
        /// The camera rig, so an acceptance drive can exercise zoom (the edge
        /// clamp only misbehaves while the ortho size is easing, which no
        /// screenshot can catch unless something calls SetZoom).
        /// </summary>
        public TianyongCameraController CameraRig => _cameraController;
        public Light DirectionalLight => directionalLight;

        private void Start() => BuildSandbox();

        public void BuildSandbox()
        {
            if (_map != null) return;
            if (config == null) config = TianyongMapConfig.LoadDefault();
            if (config != null) initialTheme = config.InitialTheme;

            ResolveSceneRig();
            _map = TianyongMapBuilder.Build(transform, initialTheme, config);

            var prefab = debugPlayerPrefab != null
                ? debugPlayerPrefab
                : config != null ? config.DebugPlayerPrefab : null;
            var player = prefab != null
                ? Instantiate(prefab, transform)
                : GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "[TianyongDebugPlayer]";
            if (player.transform.parent == null) player.transform.SetParent(transform, true);
            player.transform.position = TianyongMapDefinition.DefaultSpawn;

            // Same look as production actors: the qdao walk sprite when present
            // and a name label parked under the feet.
            QdaoBoySpriteAnimator.TryAttach(player);
            var label = WorldNameplate.Create(player.transform, "云行客", WorldNameplate.LocalPlayerColor);
            WorldLabelBillboard.Attach(label.gameObject);

            _playerController = player.GetComponent<TianyongPlayerController>();
            if (_playerController == null) _playerController = player.AddComponent<TianyongPlayerController>();
            _playerController.Initialize(null, _map.Navigation, worldCamera, config);

            _cameraController = config != null
                ? new TianyongCameraController(worldCamera, config.CameraZoomMin, config.CameraZoomMax, config.CameraZoomDefault)
                : new TianyongCameraController(worldCamera);
            _cameraController.SetTarget(player.transform);
            _cameraController.SetTheme(initialTheme, TianyongPaintedCity.IsEnabledFor(initialTheme, config));
            TianyongLighting.Apply(initialTheme, directionalLight);
            _map.UpdateVisibleChunks(player.transform.position, config?.VisibleChunkRadius ?? 3);

            // 离线验收:-sandboxDrive 时自动走位截图(TianyongSandboxAutoDrive);未带参数时无副作用。
            TianyongSandboxAutoDrive.TryAttach(this);
        }

        /// <summary>验收截图时隐藏左上角的操作提示框。</summary>
        public void SetHelpVisible(bool visible) => showHelp = visible;

        public void SetTheme(TianyongTheme theme)
        {
            initialTheme = theme;
            if (_map == null) return;
            var focus = Player != null ? Player.transform.position : TianyongMapDefinition.DefaultSpawn;
            _map.Dispose();
            _map = TianyongMapBuilder.Build(transform, theme, config);
            _map.UpdateVisibleChunks(focus, config?.VisibleChunkRadius ?? 3);
            _playerController.Initialize(null, _map.Navigation, worldCamera, config);
            _cameraController.SetTheme(theme, TianyongPaintedCity.IsEnabledFor(theme, config));
            TianyongLighting.Apply(theme, directionalLight);
        }

        private void Update()
        {
            if (_map == null) return;
            if (!GameplayInputGate.IsKeyboardBlocked)
            {
                if (Input.GetKeyDown(KeyCode.F1)) SetTheme(TianyongTheme.City);
                else if (Input.GetKeyDown(KeyCode.F2)) SetTheme(TianyongTheme.Market);
                else if (Input.GetKeyDown(KeyCode.F3)) SetTheme(TianyongTheme.Snow);
                else if (Input.GetKeyDown(KeyCode.F4)) SetTheme(TianyongTheme.Lantern);
            }

            var focus = Player != null ? Player.transform.position : TianyongMapDefinition.DefaultSpawn;
            _map.UpdateVisibleChunks(focus, config?.VisibleChunkRadius ?? 3);
        }

        private void LateUpdate()
        {
            if (_map == null) return;
            _cameraController?.Tick(Time.deltaTime, !GameplayInputGate.IsPointerBlocked);
        }

        private void ResolveSceneRig()
        {
            if (worldCamera == null) worldCamera = Camera.main ?? FindAnyObjectByType<Camera>();
            if (worldCamera == null)
            {
                var go = new GameObject("[MainCamera]");
                go.tag = "MainCamera";
                go.transform.SetParent(transform, false);
                worldCamera = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }

            if (directionalLight == null)
            {
                foreach (var light in FindObjectsByType<Light>())
                {
                    if (light.type != LightType.Directional) continue;
                    directionalLight = light;
                    break;
                }
            }
            if (directionalLight == null)
            {
                var go = new GameObject("[TianyongSun]");
                go.transform.SetParent(transform, false);
                directionalLight = go.AddComponent<Light>();
                directionalLight.type = LightType.Directional;
            }
        }

        private void OnGUI()
        {
            if (!showHelp) return;
            GUI.Box(new Rect(16f, 16f, 360f, 78f),
                "天墉城离线测试\nWASD / 鼠标左键移动，滚轮缩放\nF1 主城  F2 年货  F3 瑞雪  F4 灯会");
        }

        private void OnDestroy()
        {
            _map?.Dispose();
            _map = null;
            GameplayInputGate.ResetForTests();
        }
    }
}
