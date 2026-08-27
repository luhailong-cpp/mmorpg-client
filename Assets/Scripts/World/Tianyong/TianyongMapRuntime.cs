using MmorpgClient.Game;
using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// Binds the scene protocol to the playable Tianyong world. Scene, camera,
    /// lighting and map data are explicit dependencies so the same runtime can
    /// be hosted by the production AppRoot prefab and deterministic tests.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TianyongMapRuntime : MonoBehaviour
    {
        [SerializeField] private TianyongMapConfig config;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Light directionalSun;
        [SerializeField] private TianyongTheme initialTheme = TianyongTheme.City;

        private GameClient _client;
        private TianyongMapInstance _map;
        private TianyongCameraController _cameraController;
        private ActorView _localPlayer;

        public TianyongMapInstance Map => _map;
        public TianyongTheme Theme => _map?.Theme ?? initialTheme;
        public TianyongMapConfig Config => config;
        public Camera WorldCamera => worldCamera;
        public Light DirectionalSun => directionalSun;

        public void Initialize(GameClient client)
            => Initialize(client, worldCamera, directionalSun, config);

        public void Initialize(
            GameClient client,
            Camera camera,
            Light sun,
            TianyongMapConfig mapConfig)
        {
            Unsubscribe();

            _client = client;
            config = mapConfig != null ? mapConfig : config != null ? config : TianyongMapConfig.LoadDefault();
            if (config != null) initialTheme = config.InitialTheme;

            worldCamera = camera != null ? camera : worldCamera != null ? worldCamera : ResolveWorldCamera();
            directionalSun = sun != null ? sun : directionalSun != null ? directionalSun : ResolveDirectionalSun();
            _cameraController = worldCamera != null
                ? new TianyongCameraController(
                    worldCamera,
                    config != null ? config.CameraZoomMin : 24f,
                    config != null ? config.CameraZoomMax : 55f,
                    config != null ? config.CameraZoomDefault : 35f)
                : null;

            if (_client == null) return;
            _client.OnSceneEntered += HandleSceneEntered;
            _client.OnDisconnected += HandleDisconnected;
            _client.World.OnLocalPlayerChanged += HandleLocalPlayerChanged;

            if (_client.InGame)
                EnterScene(_client.CurrentSceneConfigId);
        }

        public void SetTheme(TianyongTheme theme)
        {
            initialTheme = theme;
            if (_map == null) return;

            var focus = GetFocusPosition();
            _map.Dispose();
            _map = TianyongMapBuilder.Build(transform, theme, config);
            _map.UpdateVisibleChunks(focus, VisibleChunkRadius);
            TianyongLighting.Apply(theme, directionalSun);
            _cameraController?.SetTheme(theme, TianyongPaintedCity.IsEnabledFor(theme, config));
            if (_localPlayer?.Go != null) AttachLocalController(_localPlayer);
        }

        private int VisibleChunkRadius => config != null ? config.VisibleChunkRadius : 3;
        private uint SceneConfigId => config != null
            ? config.SceneConfigId
            : TianyongMapDefinition.DefaultSceneConfigId;

        private void HandleSceneEntered(SceneInfoComp sceneInfo)
            => EnterScene(sceneInfo?.SceneConfigId ?? 0);

        private void HandleDisconnected() => UnloadMap();

        private void EnterScene(uint sceneConfigId)
        {
            // The current first-enter packet may omit scene_conf_id. Only the
            // configured default map accepts that compatibility value.
            if (sceneConfigId == 0) sceneConfigId = SceneConfigId;
            if (sceneConfigId != SceneConfigId)
            {
                UnloadMap();
                return;
            }

            if (_map == null)
                _map = TianyongMapBuilder.Build(transform, initialTheme, config);
            _map.Root.SetActive(true);
            _map.UpdateVisibleChunks(TianyongMapDefinition.DefaultSpawn, VisibleChunkRadius);
            TianyongLighting.Apply(initialTheme, directionalSun);
            _cameraController?.SetTheme(initialTheme, TianyongPaintedCity.IsEnabledFor(initialTheme, config));
        }

        private void HandleLocalPlayerChanged(ActorView view)
        {
            if (_localPlayer?.Go != null && _localPlayer != view)
            {
                var previousController = _localPlayer.Go.GetComponent<TianyongPlayerController>();
                if (previousController != null) previousController.enabled = false;
            }

            _localPlayer = view;
            if (view?.Go == null || _map == null)
            {
                _cameraController?.SetTarget(null);
                return;
            }

            AttachLocalController(view);
        }

        private void AttachLocalController(ActorView view)
        {
            var controller = view.Go.GetComponent<TianyongPlayerController>();
            var feetPosition = controller != null && controller.Motor != null
                ? controller.FeetPosition
                : view.Go.transform.position;
            if (!_map.Navigation.IsWalkable(feetPosition))
                feetPosition = TianyongMapDefinition.DefaultSpawn;

            view.HasTarget = false;
            view.Velocity = Vector3.zero;

            if (controller == null) controller = view.Go.AddComponent<TianyongPlayerController>();
            controller.enabled = true;
            controller.Initialize(_client, _map.Navigation, worldCamera, config);
            controller.WarpTo(feetPosition);
            _cameraController?.SetTarget(view.Go.transform);
            _map.UpdateVisibleChunks(feetPosition, VisibleChunkRadius);
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

            var focus = GetFocusPosition();
            _map.UpdateVisibleChunks(focus, VisibleChunkRadius);
            _cameraController?.Tick(Time.deltaTime, !GameplayInputGate.IsPointerBlocked);
        }

        private Vector3 GetFocusPosition()
        {
            if (_localPlayer?.Go == null) return TianyongMapDefinition.DefaultSpawn;
            var controller = _localPlayer.Go.GetComponent<TianyongPlayerController>();
            return controller != null && controller.Motor != null
                ? controller.FeetPosition
                : _localPlayer.Go.transform.position;
        }

        private Camera ResolveWorldCamera()
            => GetComponentInChildren<Camera>(true) ?? Camera.main;

        private Light ResolveDirectionalSun()
        {
            foreach (var light in GetComponentsInChildren<Light>(true))
            {
                if (light.type == LightType.Directional) return light;
            }

            foreach (var light in FindObjectsByType<Light>())
            {
                if (light.type == LightType.Directional) return light;
            }

            return null;
        }

        private void UnloadMap()
        {
            if (_localPlayer?.Go != null)
            {
                var controller = _localPlayer.Go.GetComponent<TianyongPlayerController>();
                if (controller != null) controller.enabled = false;
            }

            _map?.Dispose();
            _map = null;
            _localPlayer = null;
            _cameraController?.SetTarget(null);
        }

        private void Unsubscribe()
        {
            if (_client == null) return;
            _client.OnSceneEntered -= HandleSceneEntered;
            _client.OnDisconnected -= HandleDisconnected;
            _client.World.OnLocalPlayerChanged -= HandleLocalPlayerChanged;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            UnloadMap();
        }
    }
}
