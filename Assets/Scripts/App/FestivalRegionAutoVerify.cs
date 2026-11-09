using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MmorpgClient.Game;
using MmorpgClient.UI;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.World.CurrentMaps;
using MmorpgClient.World.Tianyong;
using UnityEngine;

namespace MmorpgClient.App
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>仅命令行开启的实机验收；复用登录、传送和移动链路，不直接修改角色坐标。</summary>
    [DisallowMultipleComponent]
    public sealed class FestivalRegionAutoVerify : MonoBehaviour
    {
        private const string Prefix = "[CurrentMapVerify]";

        [Serializable]
        public sealed class SceneEvidence
        {
            public uint sceneConfigId;
            public string sceneInstanceId;
            public int sceneNotifications;
            public float[] expectedSpawn;
            public float[] arrivedAt;
            public float spawnError;
            public int walkableCells;
            public int blockedCells;
            public int navigationMismatches;
            public float wasdDistance;
            public float[] pathTarget;
            public float[] pathEnd;
            public float pathError;
            public int movementAcks;
            public int movementReconciles;
            public string mapKey;
            public string appearance;
            public bool appearanceOnly;
            public int manifestTileCount;
            public int expectedVisibleTiles;
            public int loadedVisibleTiles;
            public int pendingTiles;
            public string[] visibleTileResources;
            public bool illegalMoveRejected;
            public uint illegalMoveInputSequence;
            public float[] illegalMoveReported;
            public float[] illegalMoveClientBefore;
            public float[] illegalMoveServerPosition;
            public float[] illegalMoveClientPosition;
            public float illegalMoveServerFromClientBefore;
            public float illegalMoveServerFromSpawn;
            public bool authoritativeLegalEndpointConfirmed;
            public int illegalMoveAckCount;
            public float illegalMoveElapsedSeconds;
            public string resourcePath;
            public int nativeWidth;
            public int nativeHeight;
            public string dayScreenshot;
            public bool passed;
        }

        [Serializable]
        public sealed class VerificationReport
        {
            public string result = "RUNNING";
            public string phase = "login";
            public string reason = "";
            public string scope = "current-three-maps";
            public string remainingChecks = "";
            public string playerId;
            public string gate;
            public string startedUtc;
            public string finishedUtc;
            public float elapsedSeconds;
            public int sceneNotifications;
            public int screenshots;
            public int screenWidth;
            public int screenHeight;
            public List<SceneEvidence> scenes = new();
        }

        private static readonly Vector3[] Directions = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
        private readonly VerificationReport _report = new();
        private AppBootstrap _app;
        private GameClient _game;
        private TianyongPlayerController _controller;
        private string _output;
        private bool _done;
        private bool _quit = true;
        private bool _connectedOnce;
        private float _startTime;
        private float _deadline;
        private float _timeout = 300f;
        private int _sceneNotifications;
        private string _exception;
        private bool _localFestival;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            var args = Environment.GetCommandLineArgs();
            if (!HasArgument(args, "-currentMapVerify") && !HasArgument(args, "-festivalMapVerify") &&
                !HasArgument(args, "-festivalMapLocalVerify")) return;
            if (FindAnyObjectByType<FestivalRegionAutoVerify>() != null) return;
            var go = new GameObject("[CurrentMapVerify]");
            DontDestroyOnLoad(go);
            go.AddComponent<FestivalRegionAutoVerify>();
        }

        private void Start()
        {
            var args = Environment.GetCommandLineArgs();
            _localFestival = HasArgument(args, "-festivalMapLocalVerify");
            _output = ArgumentValue(args, "-currentMapVerifyOut") ?? ArgumentValue(args, "-festivalMapVerifyOut")
                ?? Path.GetFullPath("current-map-verification");
            _quit = !HasArgument(args, "-currentMapVerifyNoQuit") && !HasArgument(args, "-festivalMapVerifyNoQuit");
            if (float.TryParse(ArgumentValue(args, "-currentMapVerifyTimeout") ?? ArgumentValue(args, "-festivalMapVerifyTimeout"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float timeout) && timeout > 0) _timeout = timeout;
            _startTime = Time.realtimeSinceStartup;
            _deadline = _startTime + _timeout;
            _report.startedUtc = DateTime.UtcNow.ToString("O");
            Application.runInBackground = true;
            Application.logMessageReceived += HandleLog;
            Directory.CreateDirectory(_output);
            SaveReport();
            if (_localFestival && (!(Application.isEditor || Debug.isDebugBuild) ||
                !FestivalMapLocalTestPolicy.IsRequested(args)))
            { Finish(false, "9 图联调验收需要 Development Build 及显式 -festivalMapLocalTest。"); return; }
            if (_localFestival)
            {
                _report.scope = "local-festival-nine-maps-fifteen-appearances";
                _report.remainingChecks = "合法移动先记录客户端行走与零纠偏；须逐图核对越界137裁决与发送前脚底的距离及authoritativeLegalEndpointConfirmed，不能仅凭整体PASS认定合法移动已获权威接受；网络越界反例可能同时命中距离/速度限制，不能单独证明精确mask校验；未覆盖缺坏导航、跨区、超时故障注入或重连；10000画面接缝待修。";
            }
            if (!DevAutoPilot.IsActive || DevAutoPilot.Current.MoveTest || !string.IsNullOrEmpty(DevAutoPilot.Current.AutoQueue))
            {
                Finish(false, "需要 -zone 自动登录，且不能同时开启 -moveTest 或 -autoQueue。");
                return;
            }
            StartCoroutine(Run());
        }

        private void Update()
        {
            if (_done) return;
            if (!string.IsNullOrEmpty(_exception)) { Finish(false, "运行时异常：" + _exception); return; }
            if (Time.realtimeSinceStartup >= _deadline) { Finish(false, "验收总超时。"); return; }
            if (_connectedOnce && (_game == null || !_game.InGame || !_game.IsGateReady))
                Finish(false, "验收过程中连接已断开。");
        }

        private IEnumerator Run()
        {
            float loginDeadline = Time.realtimeSinceStartup + 90f;
            while (!_done && Time.realtimeSinceStartup < loginDeadline)
            {
                _app = AppBootstrap.Instance;
                _game = _app?.GameClient;
                if (_game != null && _game.InGame && _game.IsGateReady && TryController(out _) &&
                    CityTravelUiRoot.Instance != null) break;
                yield return null;
            }
            if (_done) yield break;
            if (_game == null || !_game.InGame || !TryController(out _controller))
            { Finish(false, "90 秒内登录或地图控制器未就绪。"); yield break; }
            _connectedOnce = true;
            _game.OnSceneEntered += HandleSceneEntered;
            _report.playerId = _game.PlayerId.ToString(CultureInfo.InvariantCulture);
            _report.gate = _game.AssignedGate;
            yield return new WaitForSecondsRealtime(.8f);

            foreach (uint sceneConfigId in _localFestival ? LocalFestivalVerificationRoute(_game.CurrentSceneConfigId) : VerificationRoute(_game.CurrentSceneConfigId))
            {
                if (_done) yield break;
                yield return Visit(sceneConfigId);
                if (_done) yield break;
                if (_localFestival && CurrentMapCatalog.Find(sceneConfigId).IsModern)
                {
                    var current = CurrentMapCatalog.Find(sceneConfigId);
                    foreach (string appearance in new[] { "spring", "mid-autumn" })
                        if (appearance != current.Appearance && current.ForAppearance(appearance) != null)
                            yield return VerifyAppearance(current, appearance);
                }
            }

            int expectedRecords = _localFestival ? 16 : 3;
            if (_report.scenes.Count != expectedRecords || _report.screenshots != expectedRecords)
            { Finish(false, "场景或截图数量不完整。"); yield break; }
            foreach (var scene in _report.scenes)
                if (!scene.passed) { Finish(false, "存在未通过的场景记录。"); yield break; }
            Finish(true, _localFestival
                ? "9 图真实同区入场、客户端行走与零纠偏、网络越界拒绝、15 外观及返城检查通过；权威合法移动接受与正式解锁仍待验收。"
                : "三张当前地图真实传送、原图加载、导航一致性、移动及返城验收通过。");
        }

        public static uint[] LocalFestivalVerificationRoute(uint currentScene)
        {
            var route = new List<uint>();
            bool startsOnFestival = false;
            foreach (string id in FestivalMapLocalTestPolicy.MapIds)
            {
                uint scene = uint.Parse(id, CultureInfo.InvariantCulture);
                if (scene == currentScene) startsOnFestival = true;
                else route.Add(scene);
            }
            if (startsOnFestival) route.Add(currentScene);
            route.Add(1u);
            return route.ToArray();
        }

        public static uint[] VerificationRoute(uint currentScene)
        {
            // Same-scene travel deliberately sends no scene notification. Visit the
            // other maps first, then return to the account's starting map.
            return currentScene switch
            {
                2 => new uint[] { 3, 1, 2 },
                3 => new uint[] { 1, 2, 3 },
                _ => new uint[] { 2, 3, 1 },
            };
        }

        private IEnumerator Visit(uint sceneConfigId)
        {
            SetPhase("travel_" + sceneConfigId);
            ResetController();
            var evidence = new SceneEvidence { sceneConfigId = sceneConfigId };
            _report.scenes.Add(evidence);
            var region = CurrentMapCatalog.Find(sceneConfigId);
            if (region == null) { Finish(false, "目标场景没有当前地图交付。"); yield break; }
            evidence.mapKey = region.Key;
            evidence.appearance = region.Appearance;
            evidence.resourcePath = region.TexturePath;
            var expectedSpawn = region.Spawn;
            evidence.expectedSpawn = Coordinates(expectedSpawn);
            int notificationStart = _sceneNotifications;
            if (_localFestival && region.IsModern)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (!CityTravelUiRoot.Instance.RequestLocalFestivalTravel(sceneConfigId, region.Appearance))
                { Finish(false, "本机节庆联调门禁拒绝请求。"); yield break; }
#else
                Finish(false, "非开发构建不允许节庆联调。"); yield break;
#endif
            }
            else
            {
                CityTravelUiRoot.Instance.Toggle();
                CityTravelUiRoot.Instance.RequestTravel(sceneConfigId, "normal");
            }
            float deadline = Time.realtimeSinceStartup + 80f;
            while (!_done && Time.realtimeSinceStartup < deadline)
            {
                if (_game.CurrentSceneConfigId == sceneConfigId && _sceneNotifications > notificationStart &&
                    TryController(out _controller) && MapReady(region)) break;
                yield return null;
            }
            if (_done) yield break;
            if (_game.CurrentSceneConfigId != sceneConfigId || _sceneNotifications <= notificationStart ||
                !TryController(out _controller) || !MapReady(region))
            { Finish(false, "目标场景未同时收到服务端通知、地图贴图和控制器就绪。"); yield break; }
            CityTravelUiRoot.Instance.HidePanel();
            _controller.SetScriptedInputOwner(true);
            yield return new WaitForSecondsRealtime(.8f);
            if (!ControllerValid()) yield break;
            evidence.sceneNotifications = _sceneNotifications - notificationStart;
            evidence.sceneInstanceId = _game.CurrentSceneId.ToString(CultureInfo.InvariantCulture);
            evidence.arrivedAt = Coordinates(_controller.FeetPosition);
            evidence.spawnError = Distance(_controller.FeetPosition, expectedSpawn);
            var nav = _controller.Navigation;
            if (!nav.IsWalkable(_controller.FeetPosition) || evidence.spawnError > 2f)
            { Finish(false, "服务端出生点与当前地图的可走出生点不一致。"); yield break; }
            for (int x = 0; x < nav.Width; ++x)
            for (int z = 0; z < nav.Depth; ++z)
            {
                var point = NavigationProbeCenter(nav, x, z);
                bool walkable = nav.IsWalkable(point);
                if (walkable) ++evidence.walkableCells; else ++evidence.blockedCells;
                if (walkable != region.IsWalkable(point)) ++evidence.navigationMismatches;
            }
            Debug.Log($"{Prefix} NAV scene={sceneConfigId} walkable={evidence.walkableCells} blocked={evidence.blockedCells} " +
                $"mismatches={evidence.navigationMismatches} spawn_error={evidence.spawnError:F3}");
            if (evidence.walkableCells == 0 || evidence.blockedCells == 0 || evidence.navigationMismatches != 0)
            { Finish(false, "导航探针不完整或控制器导航与地图不一致。"); yield break; }

            SetPhase("move_" + sceneConfigId);
            int acksStart = _game.MoveAckCount;
            int reconcilesStart = _game.MoveReconcileCount;
            var wasdStart = _controller.FeetPosition;
            if (!FindStraightDirection(nav, wasdStart, out var direction, _localFestival ? 4f : 8f))
            { Finish(false, "出生点附近找不到本轮要求的合法直路。"); yield break; }
            _controller.SetDebugDirection(direction);
            yield return new WaitForSecondsRealtime(_localFestival ? .2f : .6f);
            if (!ControllerValid()) yield break;
            _controller.SetDebugDirection(Vector3.zero);
            yield return new WaitForSecondsRealtime(.6f);
            if (!ControllerValid()) yield break;
            evidence.wasdDistance = Distance(wasdStart, _controller.FeetPosition);
            if (evidence.wasdDistance < (_localFestival ? .5f : 2f) || !nav.IsWalkable(_controller.FeetPosition))
            { Finish(false, "合法方向的 WASD 移动未生效或越出道路。"); yield break; }
            if (!FindPathTarget(nav, _controller.FeetPosition, out var target, _localFestival ? 2f : 8f))
            { Finish(false, "找不到本轮要求距离的连通寻路目标。"); yield break; }
            evidence.pathTarget = Coordinates(target);
            if (!_controller.ClickAt(target))
            { Finish(false, "正式点击寻路接口拒绝了可达目标。"); yield break; }
            deadline = Time.realtimeSinceStartup + 25f;
            while (!_done && Time.realtimeSinceStartup < deadline)
            {
                if (!ControllerValid()) yield break;
                if (Distance(_controller.FeetPosition, target) <= 1.2f && !_controller.IsMoving) break;
                yield return null;
            }
            if (_done) yield break;
            yield return new WaitForSecondsRealtime(.8f);
            if (!ControllerValid()) yield break;
            evidence.pathEnd = Coordinates(_controller.FeetPosition);
            evidence.pathError = Distance(_controller.FeetPosition, target);
            evidence.movementAcks = _game.MoveAckCount - acksStart;
            evidence.movementReconciles = _game.MoveReconcileCount - reconcilesStart;
            Debug.Log($"{Prefix} MOVE scene={sceneConfigId} wasd={evidence.wasdDistance:F3} " +
                $"path_error={evidence.pathError:F3} acks={evidence.movementAcks} reconciles={evidence.movementReconciles}");
            // NotifyMoveAck 仅在服务器纠偏时发送；合法移动不应期待 ACK。
            // 此处只证明客户端行走且没有被回拉，权威接受需另取服务端/旁观者位置证据。
            if (evidence.pathError > 1.5f || !nav.IsWalkable(_controller.FeetPosition) || evidence.movementReconciles != 0)
            { Finish(false, "合法道路寻路未抵达或出现服务端回拉。"); yield break; }
            if (_localFestival && region.IsModern)
            {
                yield return VerifyNetworkBoundsRejection(region, evidence);
                if (_done) yield break;
            }
            evidence.dayScreenshot = "scene-" + sceneConfigId + "-" + region.Appearance + ".png";
            yield return WaitForTiles(region);
            if (_done) yield break;
            CaptureTileEvidence(evidence);
            yield return Capture(evidence.dayScreenshot);
            if (_done) yield break;

            var native = region.LoadTexture();
            evidence.nativeWidth = native.width;
            evidence.nativeHeight = native.height;
            if (!region.IsModern && (evidence.nativeWidth != CurrentMapCatalog.LayoutPixels ||
                evidence.nativeHeight != CurrentMapCatalog.LayoutPixels))
            { Finish(false, "当前地图未按原始尺寸导入。"); yield break; }
            evidence.passed = true;
            SaveReport();
        }

        /// <summary>固定有限的有效范围外点；此网络反例也可能触发距离/速度限制，不单独归因导航。</summary>
        public static Vector3 NetworkBoundsProbePoint(CurrentMapDefinition region)
        {
            var bounds = region.EffectiveWorldRect;
            return new Vector3(bounds.xMax + 4f, 0f, bounds.center.y);
        }

        private IEnumerator VerifyNetworkBoundsRejection(CurrentMapDefinition region, SceneEvidence evidence)
        {
            if (!FestivalMapLocalTestPolicy.CanConnect(Application.isEditor || Debug.isDebugBuild,
                FestivalMapLocalTestPolicy.IsRequested(Environment.GetCommandLineArgs()), _game.GatewayBaseUrl, _game.AssignedGate) ||
                !FestivalMapLocalTestPolicy.IsDeliveredTarget(region, region.Appearance) || !ControllerValid())
            { Finish(false, "网络越界反例不满足本机开发门禁。"); yield break; }
            if (_controller.IsMoving || _game.IsMoving)
            { Finish(false, "网络越界反例开始前角色尚未停止。"); yield break; }
            var rejectedPoint = NetworkBoundsProbePoint(region);
            if (!FinitePoint(rejectedPoint) || region.IsWalkable(rejectedPoint))
            { Finish(false, "网络越界反例不是有限且已确认不可走的位置。"); yield break; }
            SetPhase("network_bounds_" + region.SceneConfigId);
            uint expectedSequence = 0;
            bool received = false;
            Vector3 serverPosition = default;
            object gateIdentity = _game.GateConnectionIdentity;
            ulong instance = _game.CurrentSceneId;
            int before = _game.MoveAckCount;
            float started = Time.realtimeSinceStartup;
            Vector3 clientBefore = _controller.FeetPosition;
            evidence.illegalMoveClientBefore = Coordinates(clientBefore);
            Action<uint, Vector3> observe = (sequence, position) =>
            {
                if (sequence != expectedSequence) return;
                received = true;
                serverPosition = position;
            };
            _game.OnMoveAcknowledged += observe;
            try
            {
                // 仅发送现有MoveStop；不改本地transform。收包由下一帧正式网络Poll派发。
                _game.SendMoveStop(rejectedPoint, _controller.transform.eulerAngles);
                expectedSequence = _game.LastMoveInputSequence;
                evidence.illegalMoveInputSequence = expectedSequence;
                evidence.illegalMoveReported = Coordinates(rejectedPoint);
                while (!_done && !received && Time.realtimeSinceStartup - started < 5f) yield return null;
                if (_done) yield break;
                evidence.illegalMoveElapsedSeconds = Time.realtimeSinceStartup - started;
                evidence.illegalMoveAckCount = _game.MoveAckCount - before;
                if (!received)
                { Finish(false, "网络越界报告5秒内没有收到匹配input_seq的137裁决。"); yield break; }
                if (!ControllerValid()) yield break;
                evidence.illegalMoveServerPosition = Coordinates(serverPosition);
                evidence.illegalMoveClientPosition = Coordinates(_controller.FeetPosition);
                evidence.illegalMoveServerFromClientBefore = Distance(serverPosition, clientBefore);
                evidence.illegalMoveServerFromSpawn = Distance(serverPosition, region.Spawn);
                evidence.illegalMoveRejected = ReferenceEquals(gateIdentity, _game.GateConnectionIdentity) &&
                    instance == _game.CurrentSceneId && evidence.illegalMoveAckCount > 0 && FinitePoint(serverPosition) &&
                    region.IsWalkable(serverPosition) && _controller.Navigation.IsWalkable(_controller.FeetPosition) &&
                    Distance(serverPosition, _controller.FeetPosition) <= .25f;
                // C++ strict mask拒绝整段越界输入并保留原位；Java trace可能合法推进到障碍前。
                // 单独记录权威终点证据，避免给两个后端共用的拒绝测试增加错误硬阈值。
                evidence.authoritativeLegalEndpointConfirmed = evidence.illegalMoveRejected &&
                    evidence.illegalMoveServerFromClientBefore <= 1.5f;
                Debug.Log($"{Prefix} NETWORK_BOUNDS scene={region.SceneConfigId} input_seq={expectedSequence} " +
                    $"rejected={evidence.illegalMoveRejected} reported={rejectedPoint} authoritative={serverPosition} " +
                    $"client_before={clientBefore} endpoint_error={evidence.illegalMoveServerFromClientBefore:F3} " +
                    $"server_from_spawn={evidence.illegalMoveServerFromSpawn:F3} endpoint_confirmed={evidence.authoritativeLegalEndpointConfirmed} " +
                    $"scope=out_of_bounds_may_also_hit_distance_or_speed_limit");
                SaveReport();
                if (!evidence.illegalMoveRejected)
                    Finish(false, "网络越界裁决没有在同一连接/场景返回合法位置，或客户端未应用裁决。");
            }
            finally { _game.OnMoveAcknowledged -= observe; }
        }

        private static bool FinitePoint(Vector3 value)
            => !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) &&
               !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private IEnumerator VerifyAppearance(CurrentMapDefinition original, string appearance)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ResetController();
            if (!TryController(out _controller)) { Finish(false, "切景前控制器未就绪。"); yield break; }
            var selected = original.ForAppearance(appearance);
            ulong instance = _game.CurrentSceneId, entity = _game.World.LocalEntity;
            var before = _controller.FeetPosition;
            int notifications = _sceneNotifications;
            if (!CityTravelUiRoot.Instance.RequestLocalFestivalTravel(original.SceneConfigId, appearance))
            { Finish(false, "本机切景请求被拒绝。"); yield break; }
            yield return WaitForTiles(selected);
            if (_done) yield break;
            if (!TryController(out _controller) || _game.CurrentSceneId != instance || _game.World.LocalEntity != entity ||
                _sceneNotifications != notifications || Distance(before, _controller.FeetPosition) > .05f ||
                !original.SharesNavigationWith(selected) || !MapReady(selected))
            { Finish(false, "同图切景改变场景/实体/坐标/导航或贴图未就绪。"); yield break; }
            var texture = selected.LoadTexture();
            var evidence = new SceneEvidence
            {
                sceneConfigId = original.SceneConfigId, sceneInstanceId = instance.ToString(CultureInfo.InvariantCulture),
                mapKey = original.Key, appearance = appearance, appearanceOnly = true,
                resourcePath = selected.TexturePath, arrivedAt = Coordinates(_controller.FeetPosition),
                nativeWidth = texture.width, nativeHeight = texture.height,
                dayScreenshot = "scene-" + original.SceneConfigId + "-" + appearance + ".png"
            };
            _report.scenes.Add(evidence);
            CaptureTileEvidence(evidence);
            yield return Capture(evidence.dayScreenshot);
            if (_done) yield break;
            evidence.passed = true;
            SaveReport();
#else
            Finish(false, "非开发构建不允许节庆切景联调。"); yield break;
#endif
        }

        public static Vector3 NavigationProbeCenter(TianyongNavigationGrid nav, int x, int z)
        {
            float left = nav.Bounds.xMin + x * nav.CellSize, bottom = nav.Bounds.yMin + z * nav.CellSize;
            float right = Mathf.Min(left + nav.CellSize, nav.Bounds.xMax), top = Mathf.Min(bottom + nav.CellSize, nav.Bounds.yMax);
            // 与运行网格一致，边缘格使用有效范围交集的中心；无交集的填充格仍采原格中心。
            if (right > nav.EffectiveBounds.xMin && left < nav.EffectiveBounds.xMax &&
                top > nav.EffectiveBounds.yMin && bottom < nav.EffectiveBounds.yMax)
            {
                left = Mathf.Max(left, nav.EffectiveBounds.xMin); right = Mathf.Min(right, nav.EffectiveBounds.xMax);
                bottom = Mathf.Max(bottom, nav.EffectiveBounds.yMin); top = Mathf.Min(top, nav.EffectiveBounds.yMax);
            }
            return new Vector3((left + right) * .5f, 0, (bottom + top) * .5f);
        }

        private bool TryController(out TianyongPlayerController controller)
        {
            controller = null;
            var world = _game?.World;
            if (_app?.WorldMap?.Map == null || world == null || !world.HasLocalPlayer ||
                !world.TryGetActor(world.LocalEntity, out var view) || view.Go == null) return false;
            controller = view.Go.GetComponent<TianyongPlayerController>();
            return controller != null && controller.enabled && controller.Motor != null && controller.Navigation != null &&
                ReferenceEquals(controller.Navigation, _app.WorldMap.Map.Navigation);
        }

        private bool MapReady(CurrentMapDefinition region)
        {
            var map = _app?.WorldMap?.Map;
            if (map?.Root == null || !map.Root.activeInHierarchy) return false;
            if (region == null) return false;
            var expected = region.LoadTexture();
            if (expected == null || _app.WorldMap.ActiveSceneConfigId != region.SceneConfigId ||
                _app.WorldMap.Appearance != region.Appearance ||
                (!region.IsModern && (expected.width != 1254 || expected.height != 1254))) return false;
            if (region.IsModern)
            {
                var tiles = map.Root.GetComponent<CurrentMapTileStreaming>();
                if (tiles?.Manifest == null || tiles.Manifest.key != region.Key ||
                    tiles.Manifest.sceneId != region.SceneConfigId || tiles.Manifest.appearance != region.Appearance ||
                    tiles.Manifest.revision != region.Revision || tiles.Manifest.resourceRoot != region.ResourceFolder ||
                    tiles.VisibleTileCount == 0 || tiles.PendingTileCount != 0 ||
                    tiles.LoadedVisibleTilePaths.Length != tiles.VisibleTileCount) return false;
            }
            foreach (var renderer in map.Root.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && material.mainTexture == expected) return true;
            return false;
        }

        private IEnumerator WaitForTiles(CurrentMapDefinition region)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!_done && Time.realtimeSinceStartup < deadline && !MapReady(region)) yield return null;
            if (!_done && !MapReady(region)) Finish(false, "运行分片在20秒内未按实际视域全部就绪，底图回退不算通过。");
        }

        private void CaptureTileEvidence(SceneEvidence evidence)
        {
            var tiles = _app?.WorldMap?.Map?.Root?.GetComponent<CurrentMapTileStreaming>();
            evidence.manifestTileCount = tiles?.Manifest?.tiles?.Length ?? 0;
            evidence.expectedVisibleTiles = tiles?.VisibleTileCount ?? 0;
            evidence.visibleTileResources = tiles?.LoadedVisibleTilePaths ?? Array.Empty<string>();
            evidence.loadedVisibleTiles = evidence.visibleTileResources.Length;
            evidence.pendingTiles = tiles?.PendingTileCount ?? 0;
        }

        private bool ControllerValid()
        {
            if (TryController(out var current) && ReferenceEquals(current, _controller)) return true;
            Finish(false, "当前阶段的角色控制器意外消失。");
            return false;
        }

        public static bool FindStraightDirection(TianyongNavigationGrid nav, Vector3 start, out Vector3 direction, float requiredDistance = 8f)
        {
            foreach (var candidate in Directions)
            {
                var side = new Vector3(-candidate.z, 0, candidate.x) * .7f;
                bool clear = true;
                for (float distance = .5f; distance <= requiredDistance; distance += .5f)
                {
                    var point = start + candidate * distance;
                    if (nav.IsWalkable(point) && nav.IsWalkable(point + side) && nav.IsWalkable(point - side)) continue;
                    clear = false;
                    break;
                }
                if (!clear) continue;
                direction = candidate;
                return true;
            }
            direction = Vector3.zero;
            return false;
        }

        public static bool FindPathTarget(TianyongNavigationGrid nav, Vector3 start, out Vector3 target, float minimumDistance = 8f)
        {
            foreach (float distance in new[] { 16f, 24f, 12f, 32f, 8f, 4f })
            foreach (var direction in Directions)
            {
                var candidate = start + direction * distance;
                if (!nav.TryFindNearestWalkable(candidate, out candidate) || Distance(start, candidate) < minimumDistance) continue;
                var path = nav.FindPath(start, candidate);
                if (path.Count < 2) continue;
                float length = 0;
                for (int i = 1; i < path.Count; ++i) length += Distance(path[i - 1], path[i]);
                if (length > 100f) continue;
                target = candidate;
                return true;
            }
            target = start;
            return false;
        }

        private IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            if (_done) yield break;
            Texture2D screenshot = null;
            try
            {
                if (Screen.width < 640 || Screen.height < 360) throw new InvalidOperationException("图形窗口分辨率不足。");
                // Ask Unity for the completed game view; ReadPixels without an explicit target
                // can read a stale backbuffer on D3D12 even after WaitForEndOfFrame.
                screenshot = ScreenCapture.CaptureScreenshotAsTexture(1);
                if (screenshot == null) throw new InvalidOperationException("未取得游戏最终帧。");
                var pixels = screenshot.GetPixels32();
                byte min = 255, max = 0;
                for (int i = 0; i < pixels.Length; i += 401)
                { min = Math.Min(min, pixels[i].r); max = Math.Max(max, pixels[i].r); }
                if (max - min < 25) throw new InvalidOperationException("截图疑似空白画面。");
                File.WriteAllBytes(Path.Combine(_output, name), screenshot.EncodeToPNG());
                ++_report.screenshots;
                _report.screenWidth = Screen.width;
                _report.screenHeight = Screen.height;
                Debug.Log(Prefix + " SCREENSHOT " + name);
                SaveReport();
            }
            catch (Exception error) { Finish(false, "截图失败：" + error.Message); }
            finally { if (screenshot != null) Destroy(screenshot); }
        }

        private void SetPhase(string phase)
        {
            _report.phase = phase;
            Debug.Log(Prefix + " phase=" + phase);
            SaveReport();
        }

        private void HandleSceneEntered(SceneInfoComp scene)
        {
            ++_sceneNotifications;
            Debug.Log(Prefix + " SCENE_NOTIFY config=" + (scene?.SceneConfigId ?? 0));
        }

        private void HandleLog(string message, string trace, LogType type)
        {
            if (!_done && (type == LogType.Exception ||
                (_localFestival && type == LogType.Error && message.StartsWith("[CurrentMapTiles]", StringComparison.Ordinal))))
                _exception = message;
        }

        private void ResetController()
        {
            if (_controller != null)
            {
                _controller.SetDebugDirection(Vector3.zero);
                _controller.SetDebugIgnoreMask(false);
                _controller.SetScriptedInputOwner(false);
            }
            _controller = null;
        }

        private void Finish(bool passed, string reason)
        {
            if (_done) return;
            _done = true;
            ResetController();
            _report.result = passed ? "PASS" : "FAIL";
            _report.reason = reason;
            _report.finishedUtc = DateTime.UtcNow.ToString("O");
            SaveReport();
            Debug.Log($"{Prefix} RESULT={_report.result} phase={_report.phase} scenes={_report.scenes.Count} " +
                $"screenshots={_report.screenshots} reason={reason}");
            if (_quit)
            {
#if UNITY_EDITOR
                if (_localFestival && Application.isEditor)
                { UnityEditor.EditorApplication.Exit(passed ? 0 : 1); return; }
#endif
                Application.Quit(passed ? 0 : 1);
            }
        }

        private void SaveReport()
        {
            _report.elapsedSeconds = Time.realtimeSinceStartup - _startTime;
            _report.sceneNotifications = _sceneNotifications;
            File.WriteAllText(Path.Combine(_output, _localFestival ? "festival-local-verification.json" : "current-map-verification.json"), JsonUtility.ToJson(_report, true));
        }

        private void OnDestroy()
        {
            ResetController();
            if (_game != null) _game.OnSceneEntered -= HandleSceneEntered;
            Application.logMessageReceived -= HandleLog;
        }

        private static float[] Coordinates(Vector3 value) => new[] { value.x, value.y, value.z };
        private static float Distance(Vector3 a, Vector3 b)
        { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }
        private static bool HasArgument(string[] args, string key)
        {
            foreach (string arg in args) if (string.Equals(arg, key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        private static string ArgumentValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length; ++i)
            {
                if (args[i].StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) return args[i].Substring(key.Length + 1);
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) return args[i + 1];
            }
            return null;
        }
    }
}
