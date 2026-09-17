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
        private uint _activeSceneConfigId;
        private FestivalRegionDefinition _region;
        private bool _festivalAppearance;

        public TianyongMapInstance Map => _map;
        public TianyongTheme Theme => _map?.Theme ?? initialTheme;
        public TianyongMapConfig Config => config;
        public Camera WorldCamera => worldCamera;
        public Light DirectionalSun => directionalSun;
        public uint ActiveSceneConfigId => _activeSceneConfigId;
        public bool FestivalAppearance => _region != null && _festivalAppearance;
        public Vector3 CurrentSpawn => _region?.Spawn ?? TianyongMapDefinition.DefaultSpawn;

        public void SetFestivalAppearance(bool festival)
        {
            if (_region == null || _map == null) { _festivalAppearance = false; return; }
            var map = _map;
            var region = _region;
            FestivalRegionMap.SetAppearance(map, region, festival, () =>
            {
                if (_map != map || _region != region) return;
                // Painting, actor lighting and reported appearance commit in the same frame.
                var theme = festival && region.SceneConfigId != 4 ? TianyongTheme.Lantern : TianyongTheme.City;
                TianyongLighting.Apply(theme, directionalSun);
                _cameraController?.SetTheme(theme, true);
                _festivalAppearance = festival;
            });
        }

        public void Initialize(GameClient client)
            => Initialize(client, worldCamera, directionalSun, config);

        public void Initialize(
            GameClient client,
            Camera camera,
            Light sun,
            TianyongMapConfig mapConfig)
        {
            Unsubscribe();
            UnloadMap();

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
            {
                EnterScene(_client.CurrentSceneConfigId);
                // Initialization may occur after the actor's one-shot spawn notification.
                if (_client.World.HasLocalPlayer &&
                    _client.World.TryGetActor(_client.World.LocalEntity, out var localPlayer))
                    HandleLocalPlayerChanged(localPlayer);
            }
        }

        public void SetTheme(TianyongTheme theme)
        {
            if (_region != null)
            {
                SetFestivalAppearance(theme != TianyongTheme.City);
                return;
            }
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
            var nextRegion = FestivalRegionMap.Find(sceneConfigId);
            if (sceneConfigId != SceneConfigId && nextRegion == null)
            {
                UnloadMap();
                return;
            }

            // 先构建有效地图再替换旧地图；不能拿旧城导航继续控制新地点。
            if (_map == null || _activeSceneConfigId != sceneConfigId)
            {
                TianyongMapInstance nextMap;
                try
                {
                    nextMap = nextRegion != null
                        ? FestivalRegionMap.Build(transform, nextRegion, _festivalAppearance)
                        : TianyongMapBuilder.Build(transform, initialTheme, config);
                }
                catch
                {
                    // The server has already changed scenes. A failed local load must
                    // not leave the previous world's navigation attached to new actors.
                    UnloadMap();
                    throw;
                }
                if (_map?.Root != null) _map.Root.SetActive(false);
                _map?.Dispose();
                _map = nextMap;
                _region = nextRegion;
                _activeSceneConfigId = sceneConfigId;
                if (_region == null) _festivalAppearance = false;
            }
            _map.Root.SetActive(true);
            _map.UpdateVisibleChunks(CurrentSpawn, VisibleChunkRadius);
            if (_region != null) SetFestivalAppearance(_festivalAppearance);
            else
            {
                TianyongLighting.Apply(initialTheme, directionalSun);
                _cameraController?.SetTheme(initialTheme, TianyongPaintedCity.IsEnabledFor(initialTheme, config));
            }
            if (_localPlayer?.Go != null) AttachLocalController(_localPlayer);
            Debug.Log($"[FestivalRegion] ready scene={_activeSceneConfigId} festival={FestivalAppearance} spawn={CurrentSpawn}");
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
            var serverFeet = controller != null && controller.Motor != null
                ? controller.FeetPosition
                : view.Go.transform.position;
            var feetPosition = serverFeet;
            // The server now validates the enter position against the same
            // walk mask and respawns illegal saves, so this branch is a safety
            // net (scene without navdata, stale server data). When it fires the
            // relocation must be reported back, otherwise the server keeps the
            // illegal point and the first movement is judged from it.
            var relocated = false;
            if (!_map.Navigation.IsWalkable(feetPosition))
            {
                if (!_map.Navigation.TryFindNearestWalkable(feetPosition, out feetPosition))
                    feetPosition = CurrentSpawn;
                relocated = true;
            }

            view.HasTarget = false;
            view.Velocity = Vector3.zero;

            if (controller == null) controller = view.Go.AddComponent<TianyongPlayerController>();
            controller.enabled = true;
            controller.Initialize(_client, _map.Navigation, worldCamera, config);
            controller.WarpTo(feetPosition);
            if (relocated)
            {
                Debug.LogWarning(
                    $"[TianyongMapRuntime] server spawn {serverFeet} is not walkable; relocated local actor to {controller.FeetPosition} and reported it");
                controller.ReportPositionToServer();
            }
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
        }

        private void LateUpdate()
        {
            if (_map == null) return;
            _cameraController?.Tick(Time.deltaTime, !GameplayInputGate.IsPointerBlocked);
            _map.UpdateCityTiles(worldCamera);
        }

        private Vector3 GetFocusPosition()
        {
            if (_localPlayer?.Go == null) return CurrentSpawn;
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
            _region = null;
            _festivalAppearance = false;
            _activeSceneConfigId = 0;
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
