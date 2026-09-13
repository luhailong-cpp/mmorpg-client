using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MmorpgClient.Game;
using MmorpgClient.UI;
using MmorpgClient.UI.Ugui.Gameplay;
using MmorpgClient.World.Tianyong;
using UnityEngine;

namespace MmorpgClient.App
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>仅命令行开启的实机验收；复用登录、传送和移动链路，不直接修改角色坐标。</summary>
    [DisallowMultipleComponent]
    public sealed class FestivalRegionAutoVerify : MonoBehaviour
    {
        private const string Prefix = "[FestivalMapVerify]";

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
            public bool festivalKeptSceneAndEntity;
            public bool festivalKeptNavigation;
            public float festivalPositionError;
            public float festivalPathError;
            public int festivalMovementReconciles;
            public int nativeWidth;
            public int nativeHeight;
            public string dayScreenshot;
            public string festivalScreenshot;
            public bool passed;
        }

        [Serializable]
        public sealed class VerificationReport
        {
            public string result = "RUNNING";
            public string phase = "login";
            public string reason = "";
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            var args = Environment.GetCommandLineArgs();
            if (!HasArgument(args, "-festivalMapVerify")) return;
            if (FindAnyObjectByType<FestivalRegionAutoVerify>() != null) return;
            var go = new GameObject("[FestivalMapVerify]");
            DontDestroyOnLoad(go);
            go.AddComponent<FestivalRegionAutoVerify>();
        }

        private void Start()
        {
            var args = Environment.GetCommandLineArgs();
            _output = ArgumentValue(args, "-festivalMapVerifyOut") ?? Path.GetFullPath("festival-map-verification");
            _quit = !HasArgument(args, "-festivalMapVerifyNoQuit");
            if (float.TryParse(ArgumentValue(args, "-festivalMapVerifyTimeout"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float timeout) && timeout > 0) _timeout = timeout;
            _startTime = Time.realtimeSinceStartup;
            _deadline = _startTime + _timeout;
            _report.startedUtc = DateTime.UtcNow.ToString("O");
            Application.runInBackground = true;
            Application.logMessageReceived += HandleLog;
            Directory.CreateDirectory(_output);
            SaveReport();
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

            foreach (uint sceneConfigId in new uint[] { 2, 3, 4, 1 })
            {
                if (_done) yield break;
                yield return Visit(sceneConfigId);
                if (_done) yield break;
            }

            if (_report.scenes.Count != 4 || _report.screenshots != 7)
            { Finish(false, "场景或截图数量不完整。"); yield break; }
            foreach (var scene in _report.scenes)
                if (!scene.passed) { Finish(false, "存在未通过的场景记录。"); yield break; }
            Finish(true, "四次真实传送、三组日景与节庆、四图移动及返城验收通过。");
        }

        private IEnumerator Visit(uint sceneConfigId)
        {
            SetPhase("travel_" + sceneConfigId);
            ResetController();
            var evidence = new SceneEvidence { sceneConfigId = sceneConfigId };
            _report.scenes.Add(evidence);
            var region = FestivalRegionMap.Find(sceneConfigId);
            var expectedSpawn = region != null ? region.Spawn : TianyongMapDefinition.DefaultSpawn;
            evidence.expectedSpawn = Coordinates(expectedSpawn);
            int notificationStart = _sceneNotifications;
            CityTravelUiRoot.Instance.Toggle();
            CityTravelUiRoot.Instance.RequestTravel(sceneConfigId, false);
            float deadline = Time.realtimeSinceStartup + 40f;
            while (!_done && Time.realtimeSinceStartup < deadline)
            {
                if (_game.CurrentSceneConfigId == sceneConfigId && _sceneNotifications > notificationStart &&
                    TryController(out _controller) && MapReady(region, false)) break;
                yield return null;
            }
            if (_done) yield break;
            if (_game.CurrentSceneConfigId != sceneConfigId || _sceneNotifications <= notificationStart ||
                !TryController(out _controller) || !MapReady(region, false))
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
                var point = new Vector3((x + .5f) * nav.CellSize, 0, (z + .5f) * nav.CellSize);
                bool walkable = nav.IsWalkable(point);
                if (walkable) ++evidence.walkableCells; else ++evidence.blockedCells;
                if (region != null && walkable != region.IsWalkable(point)) ++evidence.navigationMismatches;
            }
            Debug.Log($"{Prefix} NAV scene={sceneConfigId} walkable={evidence.walkableCells} blocked={evidence.blockedCells} " +
                $"mismatches={evidence.navigationMismatches} spawn_error={evidence.spawnError:F3}");
            if (evidence.walkableCells == 0 || evidence.blockedCells == 0 || evidence.navigationMismatches != 0)
            { Finish(false, "导航探针不完整或控制器导航与地图不一致。"); yield break; }

            SetPhase("move_" + sceneConfigId);
            int acksStart = _game.MoveAckCount;
            int reconcilesStart = _game.MoveReconcileCount;
            var wasdStart = _controller.FeetPosition;
            if (!FindStraightDirection(nav, wasdStart, out var direction))
            { Finish(false, "出生点附近找不到足够宽的 8 米合法直路。"); yield break; }
            _controller.SetDebugDirection(direction);
            yield return new WaitForSecondsRealtime(.6f);
            if (!ControllerValid()) yield break;
            _controller.SetDebugDirection(Vector3.zero);
            yield return new WaitForSecondsRealtime(.6f);
            if (!ControllerValid()) yield break;
            evidence.wasdDistance = Distance(wasdStart, _controller.FeetPosition);
            if (evidence.wasdDistance < 2f || !nav.IsWalkable(_controller.FeetPosition))
            { Finish(false, "合法方向的 WASD 移动未生效或越出道路。"); yield break; }
            if (!FindPathTarget(nav, _controller.FeetPosition, out var target))
            { Finish(false, "找不到至少 8 米外的连通寻路目标。"); yield break; }
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
            if (evidence.pathError > 1.5f || !nav.IsWalkable(_controller.FeetPosition) || evidence.movementReconciles != 0)
            { Finish(false, "合法道路寻路未抵达或出现服务端回拉。"); yield break; }
            evidence.dayScreenshot = "scene-" + sceneConfigId + "-day.png";
            yield return Capture(evidence.dayScreenshot);
            if (_done) yield break;

            if (region != null)
            {
                SetPhase("festival_" + sceneConfigId);
                var sceneInstance = _game.CurrentSceneId;
                var localEntity = _game.World.LocalEntity;
                int notificationsBeforeAppearance = _sceneNotifications;
                var beforeAppearance = _controller.FeetPosition;
                CityTravelUiRoot.Instance.RequestTravel(sceneConfigId, true);
                deadline = Time.realtimeSinceStartup + 8f;
                while (!_done && Time.realtimeSinceStartup < deadline &&
                    (!_app.WorldMap.FestivalAppearance || !MapReady(region, true) || !TryController(out _controller)))
                    yield return null;
                if (_done) yield break;
                if (!_app.WorldMap.FestivalAppearance || !MapReady(region, true) || !TryController(out _controller))
                { Finish(false, "节庆外观未正确加载。"); yield break; }
                _controller.SetScriptedInputOwner(true);
                yield return new WaitForSecondsRealtime(.8f);
                if (!ControllerValid()) yield break;
                evidence.festivalKeptSceneAndEntity = _game.CurrentSceneId == sceneInstance &&
                    _game.World.LocalEntity == localEntity && _sceneNotifications == notificationsBeforeAppearance;
                evidence.festivalKeptNavigation = ReferenceEquals(nav, _controller.Navigation);
                evidence.festivalPositionError = Distance(beforeAppearance, _controller.FeetPosition);
                var native = Resources.Load<Texture2D>(region.TexturePath(true));
                evidence.nativeWidth = native != null ? native.width : 0;
                evidence.nativeHeight = native != null ? native.height : 0;
                if (!evidence.festivalKeptSceneAndEntity || !evidence.festivalKeptNavigation ||
                    evidence.festivalPositionError > .15f ||
                    evidence.nativeWidth != 1254 || evidence.nativeHeight != 1254 ||
                    !_controller.Navigation.IsWalkable(_controller.FeetPosition))
                { Finish(false, "节庆切换改变了真实场景、位置或未使用原生完整地图。"); yield break; }
                if (!FindPathTarget(_controller.Navigation, _controller.FeetPosition, out var festivalTarget) ||
                    !_controller.ClickAt(festivalTarget))
                { Finish(false, "节庆地图无法启动合法寻路。"); yield break; }
                int festivalReconciles = _game.MoveReconcileCount;
                deadline = Time.realtimeSinceStartup + 25f;
                while (!_done && Time.realtimeSinceStartup < deadline)
                {
                    if (!ControllerValid()) yield break;
                    if (Distance(_controller.FeetPosition, festivalTarget) <= 1.2f && !_controller.IsMoving) break;
                    yield return null;
                }
                if (_done) yield break;
                yield return new WaitForSecondsRealtime(.6f);
                if (!ControllerValid()) yield break;
                evidence.festivalPathError = Distance(_controller.FeetPosition, festivalTarget);
                evidence.festivalMovementReconciles = _game.MoveReconcileCount - festivalReconciles;
                if (evidence.festivalPathError > 1.5f || evidence.festivalMovementReconciles != 0 ||
                    !_controller.Navigation.IsWalkable(_controller.FeetPosition))
                { Finish(false, "节庆地图寻路未抵达或合法道路被服务器回拉。"); yield break; }
                evidence.festivalScreenshot = "scene-" + sceneConfigId + "-festival.png";
                yield return Capture(evidence.festivalScreenshot);
                if (_done) yield break;
            }
            evidence.passed = true;
            SaveReport();
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

        private bool MapReady(FestivalRegionDefinition region, bool festival)
        {
            var map = _app?.WorldMap?.Map;
            if (map?.Root == null || !map.Root.activeInHierarchy) return false;
            if (region == null) return _game.CurrentSceneConfigId == 1;
            var expected = Resources.Load<Texture2D>(region.TexturePath(festival));
            if (expected == null || expected.width != 1254 || expected.height != 1254) return false;
            foreach (var renderer in map.Root.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && material.mainTexture == expected) return true;
            return false;
        }

        private bool ControllerValid()
        {
            if (TryController(out var current) && ReferenceEquals(current, _controller)) return true;
            Finish(false, "当前阶段的角色控制器意外消失。");
            return false;
        }

        public static bool FindStraightDirection(TianyongNavigationGrid nav, Vector3 start, out Vector3 direction)
        {
            foreach (var candidate in Directions)
            {
                var side = new Vector3(-candidate.z, 0, candidate.x) * .7f;
                bool clear = true;
                for (float distance = .5f; distance <= 8f; distance += .5f)
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

        public static bool FindPathTarget(TianyongNavigationGrid nav, Vector3 start, out Vector3 target)
        {
            foreach (float distance in new[] { 16f, 24f, 12f, 32f })
            foreach (var direction in Directions)
            {
                var candidate = start + direction * distance;
                if (!nav.TryFindNearestWalkable(candidate, out candidate) || Distance(start, candidate) < 8f) continue;
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
        { if (!_done && type == LogType.Exception) _exception = message; }

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
            if (_quit) Application.Quit(passed ? 0 : 1);
        }

        private void SaveReport()
        {
            _report.elapsedSeconds = Time.realtimeSinceStartup - _startTime;
            _report.sceneNotifications = _sceneNotifications;
            File.WriteAllText(Path.Combine(_output, "festival-map-verification.json"), JsonUtility.ToJson(_report, true));
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
