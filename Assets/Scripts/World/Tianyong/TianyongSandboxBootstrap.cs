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
        [SerializeField] private string characterId = QdaoCharacterCatalog.DefaultId;
        private TMPro.TMP_Text _playerLabel;

        private TianyongMapInstance _map;
        private TianyongCameraController _cameraController;
        private TianyongPlayerController _playerController;

        public TianyongMapInstance Map => _map;
        public GameObject Player => _playerController != null ? _playerController.gameObject : null;
        public Camera WorldCamera => worldCamera;
        public System.Collections.Generic.IReadOnlyList<QdaoCharacterCatalog.Definition> AvailableCharacters
            => QdaoCharacterCatalog.AvailableAll;

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
            QdaoBoySpriteAnimator.TryAttach(player, characterId);
            _playerLabel = WorldNameplate.Create(player.transform,
                QdaoCharacterCatalog.Find(characterId)?.Name ?? "云行客", WorldNameplate.LocalPlayerColor);
            WorldLabelBillboard.Attach(_playerLabel.gameObject);

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
            TianyongSandboxHudVerification.TryAttach(this);
            TianyongSandboxAutoDrive.TryAttach(this);
        }

        /// <summary>Switch the offline playable appearance without rebuilding the map or controller.</summary>
        public bool SelectCharacter(string id)
        {
            if (QdaoCharacterCatalog.Find(id) == null || Player == null) return false;
            var previousId = Player.GetComponent<QdaoBoySpriteAnimator>()?.CharacterId;
            if (!QdaoBoySpriteAnimator.TryAttach(Player, id)) return false;
            var animator = Player.GetComponent<QdaoBoySpriteAnimator>();
            if (animator.CharacterId != id)
            {
                // Incomplete imports may resolve to the legacy fallback. Keep
                // the previous visible character/name and report the failed switch.
                animator.SetAppearance(previousId);
                return false;
            }
            characterId = id;
            if (_playerLabel != null) _playerLabel.text = QdaoCharacterCatalog.Find(id).Name;
            return true;
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
                else if (Input.GetKeyDown(KeyCode.F5))
                {
                    var roster = AvailableCharacters;
                    var index = 0;
                    for (var i = 0; i < roster.Count; i++)
                        if (roster[i].Id == characterId) { index = (i + 1) % roster.Count; break; }
                    SelectCharacter(roster[index].Id);
                }
            }

            var focus = Player != null ? Player.transform.position : TianyongMapDefinition.DefaultSpawn;
            _map.UpdateVisibleChunks(focus, config?.VisibleChunkRadius ?? 3);
        }

        private void LateUpdate()
        {
            if (_map == null) return;
            _cameraController?.Tick(Time.deltaTime, !GameplayInputGate.IsPointerBlocked);
            _map.UpdateCityTiles(worldCamera);
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
            GUI.Box(new Rect(16f, 16f, 400f, 102f),
                "天墉城离线测试\nWASD / 鼠标左键移动，滚轮缩放\nF1 主城  F2 年货  F3 瑞雪  F4 灯会\nF5 切换已完成角色");
        }

        private void OnDestroy()
        {
            _map?.Dispose();
            _map = null;
            GameplayInputGate.ResetForTests();
        }
    }
}
