using MmorpgClient.Game;
using MmorpgClient.World.CurrentMaps;
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
        private CurrentMapDefinition _currentMap;
        private string _appearance = "normal";

        public TianyongMapInstance Map => _map;
        public TianyongTheme Theme => TianyongTheme.City;
        public TianyongMapConfig Config => config;
        public Camera WorldCamera => worldCamera;
        public Light DirectionalSun => directionalSun;
        public uint ActiveSceneConfigId => _activeSceneConfigId;
        public bool FestivalAppearance => _currentMap != null && _currentMap.Appearance != "normal";
        public string Appearance => _currentMap?.Appearance ?? "normal";
        public Vector3 CurrentSpawn => (_currentMap ?? CurrentMapCatalog.Find(1u)).Spawn;

        public void SetFestivalAppearance(bool festival)
        {
            TrySetAppearance(festival ? "spring" : "normal");
        }

        /// <summary>Only delivered formal appearances can switch; visual changes cannot replace navigation.</summary>
        public bool TrySetAppearance(string appearance)
        {
            if (_currentMap == null || !MapDeliveryContract.IsAppearance(appearance)) return false;
            var selected = CurrentMapCatalog.Find(_activeSceneConfigId)?.ForAppearance(appearance);
            if (selected == null) return false;
            if (selected.ContentIdentity == _currentMap.ContentIdentity) return true;
            return TryInstallMap(selected, true, true);
        }

        public void RefreshCurrentDelivery()
        {
            if (_activeSceneConfigId == 0) return;
            CurrentMapCatalog.ReloadDeliveries();
            EnterScene(_activeSceneConfigId);
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
            // Retained for callers compiled against the previous runtime. Current
            // server maps are day-only; keyboard themes cannot restore legacy maps.
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
            var nextMapDefinition = CurrentMapCatalog.Find(sceneConfigId);
            if (nextMapDefinition != null && nextMapDefinition.IsAuthoredFlat &&
                !AuthoredFestivalLocalPolicy.CanUse(_client?.GatewayBaseUrl, _client?.AssignedGate))
            {
                Debug.LogError("[CurrentMap] 手绘地图仅可连接指定本机开发服务器。");
                UnloadMap();
                return;
            }
            if (nextMapDefinition == null)
            {
                // An installed-catalog refresh is not a server scene change.
                if (_map != null && _activeSceneConfigId == sceneConfigId)
                {
                    Debug.LogError("[CurrentMap] 当前场景的新交付未就绪，保留现有地图。");
                    return;
                }
                UnloadMap();
                return;
            }
            nextMapDefinition = nextMapDefinition.ForAppearance(_appearance) ?? nextMapDefinition;

            // 先构建有效地图再替换旧地图；不能拿旧城导航继续控制新地点。
            if (_map == null || _activeSceneConfigId != sceneConfigId ||
                _currentMap.ContentIdentity != nextMapDefinition.ContentIdentity)
            {
                TryInstallMap(nextMapDefinition, _map != null && _activeSceneConfigId == sceneConfigId, false);
                return;
            }
            PrepareActiveMap();
        }

        private bool TryInstallMap(CurrentMapDefinition definition, bool preserveCurrentOnFailure, bool requireSameNavigation)
        {
            var previousMap = _map;
            var previousDefinition = _currentMap;
            var previousScene = _activeSceneConfigId;
            var previousAppearance = _appearance;
            var previousRelocated = _playerRelocated;
            var previousPlayer = _localPlayer;
            var playerObject = previousPlayer?.Go;
            var previousController = playerObject != null ? playerObject.GetComponent<TianyongPlayerController>() : null;
            var previousPosition = playerObject != null ? playerObject.transform.position : Vector3.zero;
            var previousHasTarget = previousPlayer != null && previousPlayer.HasTarget;
            var previousVelocity = previousPlayer != null ? previousPlayer.Velocity : Vector3.zero;
            System.Action restorePlayer = null;
            System.Action restoreCamera = null;
            TianyongMapInstance candidate = null;
            try
            {
                if (requireSameNavigation && !previousDefinition.SharesNavigationWith(definition))
                    throw new System.InvalidOperationException("同图外观必须保留完全相同的导航和有效范围。");
                candidate = CurrentMapBuilder.Build(transform, definition);
                candidate.Root.SetActive(false);
                // Prepare resources without registering another global NavMesh.
                var spawn = definition.Spawn;
                var bounds = definition.EffectiveWorldRect;
                TianyongCameraController.ValidateWorldBounds(bounds);
                candidate.UpdateVisibleChunks(spawn, VisibleChunkRadius);
                restorePlayer = previousController?.CaptureMapBinding();
                restoreCamera = _cameraController?.CaptureMapBinding();
                if (previousController != null)
                {
                    previousController.SuspendForMapChange();
                    previousController.enabled = false;
                }
                previousMap?.DeactivateNavigation();
                candidate.ActivateNavigation();
                if (playerObject != null)
                    TianyongPlayerController.ResolveFallbackSpawn(candidate.ActiveNavigation, playerObject.transform.position, spawn);

                _map = candidate;
                _currentMap = definition;
                _activeSceneConfigId = definition.SceneConfigId;
                _appearance = definition.Appearance;
                // Binding can still fail (for example a disappearing actor).
                // Keep the old root alive until this entire preparation succeeds.
                PrepareActiveMap(false);
            }
            catch (System.Exception error)
            {
                _map = previousMap;
                _currentMap = previousDefinition;
                _activeSceneConfigId = previousScene;
                _appearance = previousAppearance;
                _localPlayer = previousPlayer;
                _playerRelocated = previousRelocated;
                candidate?.Root.SetActive(false);
                candidate?.Dispose();
                if (preserveCurrentOnFailure)
                {
                    try { previousMap?.ActivateNavigation(); }
                    catch (System.Exception restoreError)
                    {
                        Debug.LogError("[CurrentMap] 旧导航恢复失败，禁用地图：" + restoreError.Message);
                        UnloadMap();
                        return false;
                    }
                }
                if (restorePlayer != null) restorePlayer();
                else if (previousController == null && playerObject != null)
                {
                    var added = playerObject.GetComponent<TianyongPlayerController>();
                    if (added != null) { added.enabled = false; Destroy(added); }
                    playerObject.transform.position = previousPosition;
                }
                restoreCamera?.Invoke();
                if (previousPlayer != null)
                {
                    previousPlayer.HasTarget = previousHasTarget;
                    previousPlayer.Velocity = previousVelocity;
                }
                if (preserveCurrentOnFailure)
                {
                    Debug.LogError("[CurrentMap] 新交付切换未完成，保留当前地图及导航：" + error.Message);
                    return false;
                }
                // A real server scene change must not leave old navigation attached.
                UnloadMap();
                throw;
            }

            // The candidate and its runtime bindings are ready. Retire the old
            // view only at the final commit, never on a failed same-scene refresh.
            if (previousMap?.Root != null) previousMap.Root.SetActive(false);
            previousMap?.Dispose();
            ReportRelocatedPlayer();
            return true;
        }

        private bool _playerRelocated;

        private void PrepareActiveMap(bool reportRelocation = true)
        {
            _map.Root.SetActive(true);
            _map.UpdateVisibleChunks(CurrentSpawn, VisibleChunkRadius);
            TianyongLighting.Apply(TianyongTheme.City, directionalSun);
            _cameraController?.SetTheme(TianyongTheme.City, true);
            _cameraController?.SetWorldBounds(_currentMap.EffectiveWorldRect);
            if (_localPlayer?.Go != null) AttachLocalController(_localPlayer, reportRelocation);
            Debug.Log($"[CurrentMap] ready scene={_activeSceneConfigId} map={_currentMap.Key} spawn={CurrentSpawn}");
        }

        private void HandleLocalPlayerChanged(ActorView view)
        {
            if (_localPlayer?.Go != null && _localPlayer != view)
            {
                var previousController = _localPlayer.Go.GetComponent<TianyongPlayerController>();
                if (previousController != null)
                {
                    previousController.SuspendForMapChange();
                    previousController.enabled = false;
                }
            }

            _localPlayer = view;
            if (view?.Go == null || _map == null)
            {
                _cameraController?.SetTarget(null);
                return;
            }

            try { AttachLocalController(view); }
            catch (System.Exception error)
            {
                var failedController = view.Go.GetComponent<TianyongPlayerController>();
                if (failedController != null) failedController.enabled = false;
                Debug.LogError("[CurrentMap] 人物导航绑定失败，已停止移动：" + error.Message);
            }
        }

        private void AttachLocalController(ActorView view, bool reportRelocation = true)
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
            if (!_map.ActiveNavigation.IsWalkable(feetPosition))
            {
                if (_map.ActiveNavigation.IsAuthoredFlat)
                    throw new System.InvalidOperationException("服务端脚点与手绘导航不一致，已停止本地图移动。");
                if (!_map.ActiveNavigation.TryFindNearestWalkable(feetPosition, out feetPosition))
                    feetPosition = CurrentSpawn;
                relocated = true;
            }

            view.HasTarget = false;
            view.Velocity = Vector3.zero;

            if (controller == null) controller = view.Go.AddComponent<TianyongPlayerController>();
            controller.enabled = true;
            if (controller.Motor == null)
            {
                controller.Initialize(_client, _map.ActiveNavigation, worldCamera, config, CurrentSpawn);
                controller.WarpTo(feetPosition);
            }
            else
            {
                var fallback = TianyongPlayerController.ResolveFallbackSpawn(_map.ActiveNavigation, view.Go.transform.position, CurrentSpawn);
                controller.ApplyPreparedMapNavigation(_map.ActiveNavigation, fallback, feetPosition);
            }
            if (relocated)
            {
                Debug.LogWarning(
                    $"[TianyongMapRuntime] server spawn {serverFeet} is not walkable; relocated local actor to {controller.FeetPosition}; position report follows map commit");
                _playerRelocated = true;
                if (reportRelocation) ReportRelocatedPlayer();
            }
            _cameraController?.SetTarget(view.Go.transform);
            _map.UpdateVisibleChunks(feetPosition, VisibleChunkRadius);
        }

        private void ReportRelocatedPlayer()
        {
            if (!_playerRelocated) return;
            _playerRelocated = false;
            if (_localPlayer?.Go == null) return;
            // Network reporting occurs after commit; it cannot roll back artwork.
            try { _localPlayer.Go.GetComponent<TianyongPlayerController>()?.ReportPositionToServer(); }
            catch (System.Exception error) { Debug.LogWarning("[CurrentMap] 位置同步失败：" + error.Message); }
        }

        private void Update()
        {
            if (_map == null) return;

            var focus = GetFocusPosition();
            _map.UpdateVisibleChunks(focus, VisibleChunkRadius);
        }

        private void LateUpdate()
        {
            if (_map == null) return;
            _cameraController?.Tick(Time.deltaTime, !GameplayInputGate.IsPointerBlocked);
            _map.UpdateCityTiles(worldCamera, GetFocusPosition());
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
                if (controller != null)
                {
                    controller.SuspendForMapChange();
                    controller.enabled = false;
                }
            }

            _map?.Dispose();
            _map = null;
            _currentMap = null;
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
