using MmorpgClient.World.CurrentMaps;
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
        [SerializeField] private uint deliveredMapId;
        [SerializeField] private string deliveredAppearance = "spring";
        private TMPro.TMP_Text _playerLabel;

        private TianyongMapInstance _map;
        private TianyongCameraController _cameraController;
        private TianyongPlayerController _playerController;
        private CurrentMapDefinition _deliveredDefinition;
        private string _deliveryStatus = string.Empty;
        private static readonly uint[] DeliveredPreviewMapIds = { 21003, 28007, 35004 };

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
        public uint DeliveredMapId => _deliveredDefinition?.SceneConfigId ?? 0;
        public string DeliveredAppearance => _deliveredDefinition?.Appearance;

        private void Start() => BuildSandbox();

        public void BuildSandbox()
        {
            if (_map != null) return;
            if (config == null) config = TianyongMapConfig.LoadDefault();
            if (config != null) initialTheme = config.InitialTheme;

            ResolveSceneRig();
            if (deliveredMapId != 0)
            {
                _cameraController = CreateCameraController();
                SelectDeliveredMap(deliveredMapId, deliveredAppearance);
                return;
            }
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
            characterId = QdaoCharacterCatalog.ResolveVisualId(characterId);
            QdaoBoySpriteAnimator.TryAttach(player, characterId);
            _playerLabel = WorldNameplate.Create(player.transform,
                QdaoCharacterCatalog.Find(characterId)?.Name ?? "云行客", WorldNameplate.LocalPlayerColor);
            WorldLabelBillboard.Attach(_playerLabel.gameObject);

            _playerController = player.GetComponent<TianyongPlayerController>();
            if (_playerController == null) _playerController = player.AddComponent<TianyongPlayerController>();
            _playerController.Initialize(null, _map.Navigation, worldCamera, config);

            _cameraController = CreateCameraController();
            _cameraController.SetTarget(player.transform);
            _cameraController.SetTheme(initialTheme, TianyongPaintedCity.IsEnabledFor(initialTheme, config));
            TianyongLighting.Apply(initialTheme, directionalLight);
            _map.UpdateVisibleChunks(player.transform.position, config?.VisibleChunkRadius ?? 3);

            // 离线验收:-sandboxDrive 时自动走位截图(TianyongSandboxAutoDrive);未带参数时无副作用。
            TianyongSandboxHudVerification.TryAttach(this);
            TianyongSandboxAutoDrive.TryAttach(this);
        }

        private TianyongCameraController CreateCameraController()
            => config != null
                ? new TianyongCameraController(worldCamera, config.CameraZoomMin, config.CameraZoomMax, config.CameraZoomDefault)
                : new TianyongCameraController(worldCamera);

        /// <summary>Load a delivered map locally; never sends a scene or movement request to a server.</summary>
        public bool SelectDeliveredMap(uint mapId, string appearance = null)
        {
            TianyongMapInstance candidate = null;
            GameObject newPlayer = null;
            System.Action restorePlayer = null;
            System.Action restoreCamera = null;
            var previousMap = _map;
            var previousDefinition = _deliveredDefinition;
            var controller = _playerController;
            var label = _playerLabel;
            try
            {
                if (System.Array.IndexOf(DeliveredPreviewMapIds, mapId) < 0)
                    throw new System.InvalidOperationException("本场景仅预览已接入的三张节庆地图。");
                var installed = CurrentMapCatalog.Find(mapId);
                var selected = installed?.ForAppearance(appearance ?? installed.Appearance);
                if (selected == null || !selected.IsModern)
                    throw new System.InvalidOperationException("所选地图或外观的正式资源尚未导入。");
                if (previousDefinition?.ContentIdentity == selected.ContentIdentity) return true;
                bool sameMap = previousDefinition != null && previousDefinition.SceneConfigId == mapId;
                if (sameMap && !previousDefinition.SharesNavigationWith(selected))
                    throw new System.InvalidOperationException("同图外观必须保留相同的导航和有效范围。");

                candidate = CurrentMapBuilder.Build(transform, selected);
                candidate.Root.SetActive(false);
                var spawn = selected.Spawn;
                var feet = sameMap && controller != null ? controller.FeetPosition : spawn;
                var fallback = TianyongPlayerController.ResolveFallbackSpawn(candidate.Navigation, feet, spawn);
                if (!candidate.Navigation.IsWalkable(feet)) feet = fallback;
                TianyongCameraController.ValidateWorldBounds(selected.EffectiveWorldRect);
                candidate.UpdateVisibleChunks(feet, config?.VisibleChunkRadius ?? 3);
                restoreCamera = _cameraController?.CaptureMapBinding();

                if (controller == null)
                {
                    var prefab = debugPlayerPrefab != null ? debugPlayerPrefab : config?.DebugPlayerPrefab;
                    newPlayer = prefab != null ? Instantiate(prefab, transform) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    newPlayer.name = "[FestivalMapDebugPlayer]";
                    newPlayer.SetActive(false);
                    if (newPlayer.transform.parent == null) newPlayer.transform.SetParent(transform, true);
                    newPlayer.transform.position = feet;
                    characterId = QdaoCharacterCatalog.ResolveVisualId(characterId);
                    QdaoBoySpriteAnimator.TryAttach(newPlayer, characterId);
                    label = WorldNameplate.Create(newPlayer.transform,
                        QdaoCharacterCatalog.Find(characterId)?.Name ?? "云行客", WorldNameplate.LocalPlayerColor);
                    WorldLabelBillboard.Attach(label.gameObject);
                    controller = newPlayer.GetComponent<TianyongPlayerController>();
                    if (controller == null) controller = newPlayer.AddComponent<TianyongPlayerController>();
                    controller.Initialize(null, candidate.Navigation, worldCamera, config, spawn);
                    controller.WarpTo(feet);
                }
                else
                {
                    restorePlayer = controller.CaptureMapBinding();
                    controller.ApplyPreparedMapNavigation(candidate.Navigation, fallback, feet);
                }

                if (_cameraController == null) _cameraController = CreateCameraController();
                _cameraController.SetTheme(TianyongTheme.City, true);
                _cameraController.SetWorldBounds(selected.EffectiveWorldRect);
                _cameraController.SetTarget(controller.transform);
                candidate.Root.SetActive(true);
                if (newPlayer != null) newPlayer.SetActive(true);

                // Every fallible source, navigation and binding step has completed.
                _map = candidate;
                _deliveredDefinition = selected;
                _playerController = controller;
                _playerLabel = label;
                deliveredMapId = mapId;
                deliveredAppearance = selected.Appearance;
                _deliveryStatus = selected.Name + " · " + AppearanceName(selected.Appearance);
            }
            catch (System.Exception error)
            {
                if (candidate?.Root != null) candidate.Root.SetActive(false);
                candidate?.Dispose();
                if (newPlayer != null)
                {
                    newPlayer.SetActive(false);
                    if (Application.isPlaying) Destroy(newPlayer); else DestroyImmediate(newPlayer);
                }
                restorePlayer?.Invoke();
                restoreCamera?.Invoke();
                _deliveryStatus = "未切换：" + error.Message;
                Debug.LogError("[FestivalMapPreview] " + _deliveryStatus);
                return false;
            }

            if (previousMap?.Root != null) previousMap.Root.SetActive(false);
            previousMap?.Dispose();
            TianyongLighting.Apply(TianyongTheme.City, directionalLight);
            Debug.Log($"[FestivalMapPreview] ready map={deliveredMapId} appearance={deliveredAppearance} spawn={_deliveredDefinition.Spawn}");
            return true;
        }

        private static string AppearanceName(string appearance)
            => appearance == "spring" ? "春节" : appearance == "mid-autumn" ? "中秋" : appearance;

        /// <summary>Switch the offline playable appearance without rebuilding the map or controller.</summary>
        public bool SelectCharacter(string id)
        {
            id = QdaoCharacterCatalog.ResolveVisualId(id);
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
            if (deliveredMapId != 0) return;
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
                if (deliveredMapId == 0 && Input.GetKeyDown(KeyCode.F1)) SetTheme(TianyongTheme.City);
                else if (deliveredMapId == 0 && Input.GetKeyDown(KeyCode.F2)) SetTheme(TianyongTheme.Market);
                else if (deliveredMapId == 0 && Input.GetKeyDown(KeyCode.F3)) SetTheme(TianyongTheme.Snow);
                else if (deliveredMapId == 0 && Input.GetKeyDown(KeyCode.F4)) SetTheme(TianyongTheme.Lantern);
                else if (Input.GetKeyDown(KeyCode.F5))
                {
                    var roster = AvailableCharacters;
                    if (roster.Count == 0) return;
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
            _map.UpdateCityTiles(worldCamera, _playerController != null ? _playerController.FeetPosition : TianyongMapDefinition.DefaultSpawn);
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
            if (deliveredMapId != 0)
            {
                DrawDeliveredMapControls();
                return;
            }
            GUI.Box(new Rect(16f, 16f, 400f, 102f),
                "天墉城离线测试\nWASD / 鼠标左键移动，滚轮缩放\nF1 主城  F2 年货  F3 瑞雪  F4 灯会\nF5 切换已完成角色");
        }

        private void DrawDeliveredMapControls()
        {
            GUI.Box(new Rect(16, 16, 560, 208), "节庆地图离线预览");
            GUI.Label(new Rect(30, 43, 532, 24), "WASD / 鼠标左键移动，滚轮缩放，F5 切换角色");
            for (int i = 0; i < DeliveredPreviewMapIds.Length; i++)
            {
                uint id = DeliveredPreviewMapIds[i];
                var map = CurrentMapCatalog.Find(id);
                GUI.enabled = map != null;
                if (GUI.Button(new Rect(30 + i * 178, 74, 170, 32), map?.Name ?? id.ToString()))
                    SelectDeliveredMap(id);
            }
            var installed = CurrentMapCatalog.Find(deliveredMapId);
            var appearances = new[] { "spring", "mid-autumn" };
            for (int i = 0; i < appearances.Length; i++)
            {
                string appearance = appearances[i];
                GUI.enabled = installed?.ForAppearance(appearance) != null;
                if (GUI.Button(new Rect(30 + i * 125, 116, 116, 32), AppearanceName(appearance)))
                    SelectDeliveredMap(deliveredMapId, appearance);
            }
            GUI.enabled = true;
            GUI.Label(new Rect(30, 158, 532, 50), _deliveryStatus);
        }

        private void OnDestroy()
        {
            _map?.Dispose();
            _map = null;
            GameplayInputGate.ResetForTests();
        }
    }
}
