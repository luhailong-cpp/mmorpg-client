#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using MmorpgClient.World.CurrentMaps;
using MmorpgClient.World.Tianyong;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MmorpgClient.Editor.CurrentMaps
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// 在 Unity Editor 真实 Play 模式下验收「地图 19003」(sceneId=19003, pc19003-hd8-20261128)：
    /// 1) 真实角色沿东侧主院、亭台前阶、东侧主门与东入口、东北宽石桥与北入口、北岸弯道与西侧短木桥、西入口、南侧台阶、南入口与西南药圃小院实际行走并阻挡水渠/桥栏/双节装饰并集/填充区；
    /// 2) 运行期动态切换春节 ↔ 中秋，核对导航共享、角色脚点不漂移、贴图切换与有效像素差异；
    /// 3) 最近允许镜头 (orthoSize=15) 检视木桥、石桥、亭台、药圃小院的切片接缝及东南有效画幅边界裁切，并输出实拍截图与 JSON 报告。
    /// </summary>
    [InitializeOnLoad]
    public static class Map19003PlayVerification
    {
        private const string ArmedKey = "Map19003PlayVerify.Armed";
        private const string StartKey = "Map19003PlayVerify.StartUtc";
        private const string KeepEditorKey = "Map19003PlayVerify.KeepEditor";
        private const uint MapId = 19003u;
        private const float ClosestZoom = 15f;
        private const int CaptureWidth = 1600;
        private const int CaptureHeight = 900;

        private enum Phase
        {
            WaitBootstrap,
            WaitTilesThenCapture,
            WalkLeg,
            Done
        }

        private readonly struct WalkLegSpec
        {
            public readonly string Name;
            public readonly Vector3 Start;
            public readonly Vector3 Target;
            public WalkLegSpec(string name, Vector3 start, Vector3 target)
            { Name = name; Start = start; Target = target; }
        }

        private readonly struct CaptureSpec
        {
            public readonly string Key;
            public readonly string Appearance;
            public readonly Vector3 Focus;
            public readonly float Zoom;
            public readonly bool FollowPlayer;
            public readonly bool CheckEdgeClamp;
            public CaptureSpec(string key, string appearance, Vector3 focus, float zoom, bool followPlayer = false, bool checkEdgeClamp = false)
            { Key = key; Appearance = appearance; Focus = focus; Zoom = zoom; FollowPlayer = followPlayer; CheckEdgeClamp = checkEdgeClamp; }
        }

        private static readonly WalkLegSpec[] WalkLegs =
        {
            new("spawn-to-pavilion-front-stairs", new Vector3(125f, 0f, 51.25f), new Vector3(125f, 0f, 71.7578125f)),
            new("spawn-via-east-gate-to-east-entry", new Vector3(125f, 0f, 51.25f), new Vector3(206.25f, 0f, 58.28125f)),
            new("east-gate-across-stone-bridge-to-north-entry", new Vector3(173.4375f, 0f, 59.0625f), new Vector3(156.640625f, 0f, 116.875f)),
            new("north-bank-across-wood-bridge-to-west-entry", new Vector3(125.78125f, 0f, 110.8203125f), new Vector3(5.46875f, 0f, 93.6328125f)),
            new("south-courtyard-stairs-to-herb-garden", new Vector3(148.2421875f, 0f, 35.0390625f), new Vector3(51.5625f, 0f, 36.2109375f)),
            new("south-entry-to-lower-walkway", new Vector3(107.8125f, 0f, 5.15625f), new Vector3(78.90625f, 0f, 24.296875f)),
        };

        private static readonly CaptureSpec[] Captures =
        {
            new("01-spring-overview-spawn", "spring", new Vector3(125f, 0f, 51.25f), 27f, followPlayer: true),
            new("02-midautumn-overview-spawn", "mid-autumn", new Vector3(125f, 0f, 51.25f), 27f, followPlayer: true),
            // 短木桥与西路口，跨越 x=1024(40u)、y=1024(80u) 四向切片接缝
            new("03-spring-closest-woodbridge-seams", "spring", new Vector3(46f, 0f, 80f), ClosestZoom),
            new("04-midautumn-closest-woodbridge-seams", "mid-autumn", new Vector3(46f, 0f, 80f), ClosestZoom),
            // 亭台、东院桌案装饰与东北宽石桥，跨越 x=4096(160u)、y=1024(80u) 四向切片接缝
            new("05-spring-closest-pavilion-stonebridge-seams", "spring", new Vector3(148f, 0f, 76f), ClosestZoom),
            new("06-midautumn-closest-pavilion-stonebridge-seams", "mid-autumn", new Vector3(148f, 0f, 76f), ClosestZoom),
            // 西南药圃小院与绕园步道，跨越 x=1024(40u)、y=2048(40u) 四向切片接缝
            new("07-spring-closest-herbgarden-seams", "spring", new Vector3(52f, 0f, 40f), ClosestZoom),
            new("08-midautumn-closest-herbgarden-seams", "mid-autumn", new Vector3(52f, 0f, 40f), ClosestZoom),
            // 推向右下有效画幅边界 (xMax=212.5, zMin=0.46875)，验证镜头不会露出右 704px 与下 12px 填充边
            new("09-spring-closest-effective-edge-clamp", "spring", new Vector3(240f, 0f, 0f), ClosestZoom, checkEdgeClamp: true),
        };

        [Serializable]
        private sealed class WalkResult
        {
            public string name;
            public float[] start;
            public float[] target;
            public float[] arrived;
            public float errorMeters;
            public float elapsedSeconds;
            public int frames;
            public bool passed;
        }

        [Serializable]
        private sealed class CaptureResult
        {
            public string key;
            public string appearance;
            public string file;
            public float requestedZoom;
            public float actualZoom;
            public float[] cameraPos;
            public int visibleTiles;
            public int loadedVisibleTiles;
            public int pendingTiles;
            public string[] loadedTilePaths;
            public bool edgeClampVerified;
        }

        [Serializable]
        private sealed class Report
        {
            public string result = "RUNNING";
            public string unityVersion;
            public string graphicsDevice;
            public string scenePath;
            public uint sceneId;
            public string mapKey;
            public string startedUtc;
            public string finishedUtc;
            public float minimumZoomObserved;
            public bool switchPreservedFeet;
            public bool switchPreservedNavigation;
            public bool festivalPixelDiffVerified;
            public int pavilionStoneBridgeDiffPixels;
            public bool blockedInputPreventedCrossing;
            public string captureDirectory;
            public string error;
            public List<WalkResult> walks = new();
            public List<CaptureResult> captures = new();
            public List<string> runtimeErrors = new();
        }

        private static Phase _phase;
        private static int _captureIndex;
        private static int _walkIndex;
        private static int _waitFrames;
        private static int _lastFrame = -1;
        private static int _walkFrames;
        private static float _legStartTime;
        private static bool _legStarted;
        private static TianyongSandboxBootstrap _sandbox;
        private static TianyongPlayerController _controller;
        private static GameObject _cameraFocusAnchor;
        private static Color32[] _springPavilionPixels;
        private static Report _report;

        static Map19003PlayVerification()
        {
            if (SessionState.GetBool(ArmedKey, false)) Register();
        }

        [MenuItem("MMORPG/Maps/验收地图 19003 双节走图")]
        public static void RunInteractive()
        {
            SessionState.SetBool(KeepEditorKey, true);
            StartRun();
        }

        public static void RunBatch()
        {
            SessionState.SetBool(KeepEditorKey, false);
            StartRun();
        }

        private static void StartRun()
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Verification requires Edit mode.");
                _phase = Phase.WaitBootstrap;
                _captureIndex = 0;
                _walkIndex = 0;
                _waitFrames = 0;
                _lastFrame = -1;
                _legStarted = false;
                _sandbox = null;
                _controller = null;
                _cameraFocusAnchor = null;
                _springPavilionPixels = null;
                _report = null;

                Directory.CreateDirectory(CaptureDir());
                Directory.CreateDirectory(EvidenceDir());
                SessionState.SetBool(ArmedKey, true);
                SessionState.SetString(StartKey, DateTime.UtcNow.ToString("O"));
                Register();

                if (!EditorApplication.ExecuteMenuItem("MMORPG/Maps/地图 19003 双节走图"))
                    throw new InvalidOperationException("Failed to open Map19003 menu item.");
                if (SceneManager.GetActiveScene().path != FestivalMapPreviewMenu.Map19003ScenePath)
                    throw new InvalidOperationException("Unexpected active scene: " + SceneManager.GetActiveScene().path);
                EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
            }
            catch (Exception ex)
            {
                Finish(false, ex);
            }
        }

        private static void Register()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= HandleLog;
            Application.logMessageReceived += HandleLog;
        }

        private static void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (_report == null) return;
            if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception)
                _report.runtimeErrors.Add(type + ": " + condition + "\n" + stackTrace);
        }

        private static void Tick()
        {
            try
            {
                DateTime started = DateTime.Parse(SessionState.GetString(StartKey, ""), null,
                    System.Globalization.DateTimeStyles.RoundtripKind);
                if ((DateTime.UtcNow - started.ToUniversalTime()).TotalSeconds > 300)
                    throw new TimeoutException("Timed out waiting for Map 19003 Play verification.");
                if (!EditorApplication.isPlaying)
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling)
                    {
                        Debug.Log("[Map19003PlayVerify] Requesting Editor Play mode");
                        EditorApplication.isPlaying = true;
                    }
                    return;
                }
                EditorApplication.QueuePlayerLoopUpdate();
                int frame = Time.frameCount;
                if (frame == _lastFrame) return;
                _lastFrame = frame;

                if (_report == null)
                {
                    _report = new Report
                    {
                        unityVersion = Application.unityVersion,
                        graphicsDevice = SystemInfo.graphicsDeviceName,
                        scenePath = SceneManager.GetActiveScene().path,
                        sceneId = MapId,
                        mapKey = "map19003",
                        startedUtc = started.ToUniversalTime().ToString("O"),
                        captureDirectory = CaptureDir(),
                    };
                }

                if (_phase == Phase.WaitBootstrap)
                {
                    _sandbox = UnityEngine.Object.FindFirstObjectByType<TianyongSandboxBootstrap>();
                    if (_sandbox == null || _sandbox.Map == null || _sandbox.Player == null || _sandbox.CameraRig == null)
                        return;
                    _controller = _sandbox.Player.GetComponent<TianyongPlayerController>();
                    if (_controller == null || _controller.Navigation == null) return;
                    _sandbox.SetHelpVisible(false);
                    _controller.SetScriptedInputOwner(true);
                    _cameraFocusAnchor = new GameObject("[Map19003VerifyCameraAnchor]");

                    // Check minimum zoom contract
                    _sandbox.CameraRig.SetZoom(1f);
                    _report.minimumZoomObserved = _sandbox.CameraRig.RequestedZoom;
                    if (Mathf.Abs(_report.minimumZoomObserved - ClosestZoom) > 0.01f)
                        throw new InvalidOperationException($"Expected min zoom {ClosestZoom}, got {_report.minimumZoomObserved}");

                    // Check live appearance switch preserves feet & navigation
                    var feetBefore = _controller.FeetPosition;
                    var navBefore = _sandbox.Map.Navigation;
                    if (!_sandbox.SelectDeliveredMap(MapId, "mid-autumn"))
                        throw new InvalidOperationException("Failed to switch spring -> mid-autumn.");
                    _report.switchPreservedFeet = Vector3.Distance(feetBefore, _controller.FeetPosition) < 0.001f;
                    _report.switchPreservedNavigation = _sandbox.Map.Navigation.Width == navBefore.Width &&
                        _sandbox.Map.Navigation.Depth == navBefore.Depth &&
                        _sandbox.Map.Navigation.EffectiveBounds == navBefore.EffectiveBounds;
                    if (!_report.switchPreservedFeet || !_report.switchPreservedNavigation)
                        throw new InvalidOperationException("Appearance switch altered feet or navigation.");
                    if (!_sandbox.SelectDeliveredMap(MapId, "spring"))
                        throw new InvalidOperationException("Failed to switch mid-autumn -> spring.");

                    // Check blocked probes: bridge rail, canal water, seasonal prop union, right/bottom padding
                    var blockedCanal = new Vector3(100.0f, 0f, 91.09375f);
                    var blockedWoodRail = new Vector3(56.25f, 0f, 87.96875f);
                    var blockedPropUnion = new Vector3(157.03125f, 0f, 65.3125f);
                    var blockedRightPad = new Vector3(226.5625f, 0f, 61.40625f);
                    var blockedBottomPad = new Vector3(105.46875f, 0f, 0.234375f);
                    _report.blockedInputPreventedCrossing =
                        !_sandbox.Map.Navigation.IsWalkable(blockedCanal) &&
                        !_sandbox.Map.Navigation.IsWalkable(blockedWoodRail) &&
                        !_sandbox.Map.Navigation.IsWalkable(blockedPropUnion) &&
                        !_sandbox.Map.Navigation.IsWalkable(blockedRightPad) &&
                        !_sandbox.Map.Navigation.IsWalkable(blockedBottomPad);
                    if (!_report.blockedInputPreventedCrossing)
                        throw new InvalidOperationException("Expected blocked probes to stay unwalkable.");

                    _controller.WarpTo(new Vector3(125f, 0f, 51.25f));
                    _captureIndex = 0;
                    PrepareCapture(Captures[0]);
                    _phase = Phase.WaitTilesThenCapture;
                    return;
                }

                if (_phase == Phase.WaitTilesThenCapture)
                {
                    var spec = Captures[_captureIndex];
                    var streaming = _sandbox.Map.Root.GetComponent<CurrentMapTileStreaming>();
                    var cam = _sandbox.WorldCamera;
                    cam.aspect = CaptureWidth / (float)CaptureHeight;
                    _sandbox.CameraRig.SetZoom(spec.Zoom);
                    _sandbox.CameraRig.Snap();
                    var focusPos = spec.FollowPlayer ? _controller.FeetPosition : _cameraFocusAnchor.transform.position;
                    streaming.Tick(cam, focusPos);
                    _waitFrames++;
                    bool tilesReady = streaming.VisibleTileCount > 0 &&
                        streaming.PendingTileCount == 0 &&
                        (spec.Zoom > ClosestZoom + 0.5f
                            ? streaming.LoadedVisibleTilePaths.Length >= Math.Min(streaming.VisibleTileCount, CurrentMapTileStreaming.MaxResidentAndPendingTiles)
                            : streaming.LoadedVisibleTilePaths.Length == streaming.VisibleTileCount);
                    if (!tilesReady && _waitFrames < 180) return;
                    if (!tilesReady)
                        throw new InvalidOperationException(
                            $"Tiles not ready for {spec.Key}: visible={streaming.VisibleTileCount}, loaded={streaming.LoadedVisibleTilePaths.Length}, pending={streaming.PendingTileCount}");

                    var pixels = CapturePng(cam, Path.Combine(CaptureDir(), spec.Key + ".png"));
                    if (spec.Key == "05-spring-closest-pavilion-stonebridge-seams")
                        _springPavilionPixels = pixels;
                    else if (spec.Key == "06-midautumn-closest-pavilion-stonebridge-seams" && _springPavilionPixels != null)
                    {
                        int diff = 0;
                        for (int i = 0; i < pixels.Length; i++)
                            if (Math.Abs(pixels[i].r - _springPavilionPixels[i].r) +
                                Math.Abs(pixels[i].g - _springPavilionPixels[i].g) +
                                Math.Abs(pixels[i].b - _springPavilionPixels[i].b) > 24)
                                diff++;
                        _report.pavilionStoneBridgeDiffPixels = diff;
                        _report.festivalPixelDiffVerified = diff > 500;
                        if (!_report.festivalPixelDiffVerified)
                            throw new InvalidOperationException($"Festival pixels barely changed on Pavilion/Courtyard: diff={diff}");
                    }

                    bool edgeOk = false;
                    if (spec.CheckEdgeClamp)
                    {
                        var bounds = CurrentMapCatalog.Find(MapId).EffectiveWorldRect;
                        float halfH = cam.orthographicSize;
                        float halfW = halfH * cam.aspect;
                        float xMax = cam.transform.position.x + halfW;
                        float zMin = cam.transform.position.z - halfH;
                        edgeOk = xMax <= bounds.xMax + 0.02f && zMin >= bounds.yMin - 0.02f;
                        if (!edgeOk)
                            throw new InvalidOperationException($"Camera exceeded EffectiveWorldRect: xMax={xMax} (max {bounds.xMax}), zMin={zMin} (min {bounds.yMin})");
                    }

                    _report.captures.Add(new CaptureResult
                    {
                        key = spec.Key,
                        appearance = spec.Appearance,
                        file = spec.Key + ".png",
                        requestedZoom = spec.Zoom,
                        actualZoom = cam.orthographicSize,
                        cameraPos = new[] { cam.transform.position.x, cam.transform.position.y, cam.transform.position.z },
                        visibleTiles = streaming.VisibleTileCount,
                        loadedVisibleTiles = streaming.LoadedVisibleTilePaths.Length,
                        pendingTiles = streaming.PendingTileCount,
                        loadedTilePaths = streaming.LoadedVisibleTilePaths,
                        edgeClampVerified = edgeOk,
                    });
                    Debug.Log($"[Map19003PlayVerify] CAPTURE {spec.Key}: zoom={cam.orthographicSize:F1} tiles={streaming.LoadedVisibleTilePaths.Length}/{streaming.VisibleTileCount}");

                    _captureIndex++;
                    if (_captureIndex < Captures.Length)
                    {
                        PrepareCapture(Captures[_captureIndex]);
                    }
                    else
                    {
                        _sandbox.SelectDeliveredMap(MapId, "spring");
                        _sandbox.CameraRig.SetTarget(_sandbox.Player.transform);
                        _sandbox.CameraRig.SetZoom(27f);
                        _walkIndex = 0;
                        _legStarted = false;
                        _phase = Phase.WalkLeg;
                    }
                    return;
                }

                if (_phase == Phase.WalkLeg)
                {
                    var leg = WalkLegs[_walkIndex];
                    if (!_legStarted)
                    {
                        _controller.WarpTo(leg.Start);
                        bool planned = _controller.SetDestination(leg.Target);
                        if (!planned)
                            throw new InvalidOperationException($"SetDestination failed for walk leg {leg.Name}: {leg.Start} -> {leg.Target}");
                        _walkFrames = 0;
                        _legStartTime = Time.time;
                        _legStarted = true;
                        return;
                    }
                    _walkFrames++;
                    float elapsed = Time.time - _legStartTime;
                    float dist = Vector3.Distance(
                        new Vector3(_controller.FeetPosition.x, 0f, _controller.FeetPosition.z),
                        new Vector3(leg.Target.x, 0f, leg.Target.z));
                    bool stopped = !_controller.IsMoving && !_controller.PathDestination.HasValue;
                    if (stopped || dist <= 0.8f || elapsed > 45f)
                    {
                        bool passed = dist <= 1.2f && _sandbox.Map.Navigation.IsWalkable(_controller.FeetPosition);
                        _report.walks.Add(new WalkResult
                        {
                            name = leg.Name,
                            start = new[] { leg.Start.x, leg.Start.y, leg.Start.z },
                            target = new[] { leg.Target.x, leg.Target.y, leg.Target.z },
                            arrived = new[] { _controller.FeetPosition.x, _controller.FeetPosition.y, _controller.FeetPosition.z },
                            errorMeters = dist,
                            elapsedSeconds = elapsed,
                            frames = _walkFrames,
                            passed = passed,
                        });
                        Debug.Log($"[Map19003PlayVerify] WALK {leg.Name}: err={dist:F2}m elapsed={elapsed:F2}s frames={_walkFrames} pass={passed}");
                        if (!passed)
                            throw new InvalidOperationException($"Walk leg {leg.Name} did not reach target: err={dist:F2}m at {_controller.FeetPosition}");
                        _walkIndex++;
                        _legStarted = false;
                        if (_walkIndex >= WalkLegs.Length)
                        {
                            if (_report.runtimeErrors.Count != 0)
                                throw new InvalidOperationException("Unity reported runtime errors during Play verification.");
                            _phase = Phase.Done;
                            Finish(true, null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Finish(false, ex);
            }
        }

        private static void PrepareCapture(CaptureSpec spec)
        {
            if (_sandbox.DeliveredAppearance != spec.Appearance)
            {
                if (!_sandbox.SelectDeliveredMap(MapId, spec.Appearance))
                    throw new InvalidOperationException("Failed to switch appearance to " + spec.Appearance);
            }
            if (spec.FollowPlayer)
            {
                _controller.WarpTo(spec.Focus);
                _sandbox.CameraRig.SetTarget(_sandbox.Player.transform);
            }
            else
            {
                _cameraFocusAnchor.transform.position = spec.Focus;
                _sandbox.CameraRig.SetTarget(_cameraFocusAnchor.transform);
            }
            _sandbox.WorldCamera.aspect = CaptureWidth / (float)CaptureHeight;
            _sandbox.CameraRig.SetZoom(spec.Zoom);
            _sandbox.CameraRig.Snap();
            _waitFrames = 0;
        }

        private static Color32[] CapturePng(Camera sourceCam, string path)
        {
            var oldActive = RenderTexture.active;
            GameObject go = null;
            Camera capCam = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                go = new GameObject("[Map19003CaptureCam]");
                capCam = go.AddComponent<Camera>();
                capCam.CopyFrom(sourceCam);
                capCam.enabled = false;
                capCam.aspect = CaptureWidth / (float)CaptureHeight;
                capCam.transform.SetPositionAndRotation(sourceCam.transform.position, sourceCam.transform.rotation);
                rt = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                if (!rt.Create()) throw new InvalidOperationException("Failed to create RenderTexture.");
                capCam.targetTexture = rt;
                capCam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false, false);
                tex.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0, false);
                tex.Apply(false, false);
                var data = tex.GetPixels32();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                return data;
            }
            finally
            {
                if (capCam != null) capCam.targetTexture = null;
                RenderTexture.active = oldActive;
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static string ProjectRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string CaptureDir() => Path.Combine(ProjectRoot(), ".codex-artifacts", "pc19003-play-verification-20261128");
        private static string EvidenceDir() => Path.Combine(ProjectRoot(), "Docs", "VerificationEvidence", "pc19003-hd8-20261128");

        private static void Finish(bool success, Exception error)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= HandleLog;
            SessionState.EraseBool(ArmedKey);
            if (_report == null) _report = new Report { unityVersion = Application.unityVersion };
            _report.result = success ? "PASS" : "FAIL";
            _report.finishedUtc = DateTime.UtcNow.ToString("O");
            _report.error = error?.ToString();
            try
            {
                Directory.CreateDirectory(EvidenceDir());
                File.WriteAllText(Path.Combine(EvidenceDir(), "play-verification.json"), JsonUtility.ToJson(_report, true) + "\n");
            }
            catch (Exception writeErr) { Debug.LogException(writeErr); success = false; }
            if (error != null) Debug.LogException(error);
            Debug.Log("[Map19003PlayVerify] " + (success ? "PASS" : "FAIL"));
            bool keepEditor = SessionState.GetBool(KeepEditorKey, false);
            SessionState.EraseBool(KeepEditorKey);
            if (keepEditor)
                EditorApplication.isPlaying = false;
            else
                EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
#endif
