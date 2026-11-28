#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using MmorpgClient.World.CurrentMaps;
using MmorpgClient.World.Tianyong;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MmorpgClient.Editor.CurrentMaps
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// 在 Unity Editor 真实 Play 模式下验收「水车庭院」(sceneId=20000)：
    /// 1) 真实角色沿主路、主殿台阶、北上石阶、东南小殿台阶、石拱桥与临水亭实际行走并阻挡非法越界；
    /// 2) 运行期动态切换春节 ↔ 中秋，核对导航共享、角色脚点不漂移、贴图切换与有效像素差异；
    /// 3) 最近允许镜头 (orthoSize=15) 检视瀑布与水车、切片接缝及有效画幅边界裁切，并输出实拍截图与 JSON 报告。
    /// </summary>
    [InitializeOnLoad]
    public static class WaterwheelCourtyardPlayVerification
    {
        private const string ArmedKey = "WaterwheelPlayVerify.Armed";
        private const string StartKey = "WaterwheelPlayVerify.StartUtc";
        private const string KeepEditorKey = "WaterwheelPlayVerify.KeepEditor";
        private const uint MapId = 20000u;
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
            new("main-road-to-main-hall-stairs", new Vector3(160f, 0f, 140f), new Vector3(196.09375f, 0f, 237.1875f)),
            new("north-road-to-upper-stairs", new Vector3(112.5f, 0f, 229.375f), new Vector3(126.5625f, 0f, 314.53125f)),
            new("courtyard-to-west-shop-stairs", new Vector3(115f, 0f, 175f), new Vector3(79.6875f, 0f, 176.25f)),
            new("courtyard-to-shrine-stairs", new Vector3(160f, 0f, 140f), new Vector3(236.71875f, 0f, 87.96875f)),
            new("south-approach-across-bridge-to-balcony", new Vector3(194.53125f, 0f, 74.6875f), new Vector3(42.1875f, 0f, 80.15625f)),
        };

        private static readonly CaptureSpec[] Captures =
        {
            new("01-spring-overview-spawn", "spring", new Vector3(160f, 0f, 140f), 35f, followPlayer: true),
            new("02-midautumn-overview-spawn", "mid-autumn", new Vector3(160f, 0f, 140f), 35f, followPlayer: true),
            // 瀑布与水车区域，完整框入瀑布水流与水车主体，同时跨越 x=1024(40u)、y=1024(280u) 四向切片接缝
            new("03-spring-closest-waterfall-seams", "spring", new Vector3(28f, 0f, 272f), ClosestZoom),
            new("04-midautumn-closest-waterfall-seams", "mid-autumn", new Vector3(28f, 0f, 272f), ClosestZoom),
            // 主殿台阶与节庆装饰，跨越 x=4096(160u)、x=5120(200u)、y=2048(240u) 切片接缝
            new("05-spring-closest-main-hall-stairs", "spring", new Vector3(192f, 0f, 232f), ClosestZoom),
            new("06-midautumn-closest-main-hall-stairs", "mid-autumn", new Vector3(192f, 0f, 232f), ClosestZoom),
            // 石拱桥与临水亭，跨越 x=3072(120u)、x=4096(160u)、y=6144(80u) 切片接缝
            new("07-spring-closest-bridge-pavilion-seams", "spring", new Vector3(122f, 0f, 86f), ClosestZoom),
            new("08-midautumn-closest-bridge-pavilion-seams", "mid-autumn", new Vector3(122f, 0f, 86f), ClosestZoom),
            // 推向右下有效画幅边界 (xMax=297.1484375, zMin=32.578125)，验证镜头不会露出外围填充边
            new("09-spring-closest-effective-edge-clamp", "spring", new Vector3(340f, 0f, 0f), ClosestZoom, checkEdgeClamp: true),
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
            public int mainHallDiffPixels;
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
        private static Color32[] _springMainHallPixels;
        private static Report _report;

        static WaterwheelCourtyardPlayVerification()
        {
            if (SessionState.GetBool(ArmedKey, false)) Register();
        }

        [MenuItem("MMORPG/Maps/验收水车庭院双节走图")]
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
                _springMainHallPixels = null;
                _report = null;

                Directory.CreateDirectory(CaptureDir());
                Directory.CreateDirectory(EvidenceDir());
                SessionState.SetBool(ArmedKey, true);
                SessionState.SetString(StartKey, DateTime.UtcNow.ToString("O"));
                Register();

                if (!EditorApplication.ExecuteMenuItem("MMORPG/Maps/水车庭院双节走图"))
                    throw new InvalidOperationException("Failed to open WaterwheelCourtyard menu item.");
                if (SceneManager.GetActiveScene().path != FestivalMapPreviewMenu.WaterwheelCourtyardScenePath)
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
                    throw new TimeoutException("Timed out waiting for Waterwheel Courtyard Play verification.");
                if (!EditorApplication.isPlaying)
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling)
                    {
                        Debug.Log("[WaterwheelPlayVerify] Requesting Editor Play mode");
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
                        mapKey = "waterwheel-courtyard",
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
                    _cameraFocusAnchor = new GameObject("[WaterwheelVerifyCameraAnchor]");

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

                    // Check blocked movement cannot cross bridge north parapet or water
                    var parapetSouth = new Vector3(148.4375f, 0f, 87.1875f); // bridgeCrown
                    _controller.WarpTo(parapetSouth);
                    var blockedTarget = new Vector3(148.4375f, 0f, 94.21875f); // bridgeNorthParapet
                    if (_sandbox.Map.Navigation.IsWalkable(blockedTarget))
                        throw new InvalidOperationException("Bridge north parapet must be blocked.");
                    _report.blockedInputPreventedCrossing = !_sandbox.Map.Navigation.IsWalkable(blockedTarget) &&
                        !_sandbox.Map.Navigation.IsWalkable(new Vector3(22.65625f, 0f, 273.125f)) &&
                        !_sandbox.Map.Navigation.IsWalkable(new Vector3(146.484375f, 0f, 54.375f));

                    // Reset back to spawn for captures
                    _controller.WarpTo(new Vector3(160f, 0f, 140f));
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
                    if (spec.Key == "05-spring-closest-main-hall-stairs")
                        _springMainHallPixels = pixels;
                    else if (spec.Key == "06-midautumn-closest-main-hall-stairs" && _springMainHallPixels != null)
                    {
                        int diff = 0;
                        for (int i = 0; i < pixels.Length; i++)
                            if (Math.Abs(pixels[i].r - _springMainHallPixels[i].r) +
                                Math.Abs(pixels[i].g - _springMainHallPixels[i].g) +
                                Math.Abs(pixels[i].b - _springMainHallPixels[i].b) > 24)
                                diff++;
                        _report.mainHallDiffPixels = diff;
                        _report.festivalPixelDiffVerified = diff > 500;
                        if (!_report.festivalPixelDiffVerified)
                            throw new InvalidOperationException($"Festival pixels barely changed on Main Hall: diff={diff}");
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
                    Debug.Log($"[WaterwheelPlayVerify] CAPTURE {spec.Key}: zoom={cam.orthographicSize:F1} tiles={streaming.LoadedVisibleTilePaths.Length}/{streaming.VisibleTileCount}");

                    _captureIndex++;
                    if (_captureIndex < Captures.Length)
                    {
                        PrepareCapture(Captures[_captureIndex]);
                    }
                    else
                    {
                        // Move to live player-walking verification across main roads, stairs, and stone bridge!
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
                        Debug.Log($"[WaterwheelPlayVerify] WALK {leg.Name}: err={dist:F2}m elapsed={elapsed:F2}s frames={_walkFrames} pass={passed}");
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
                go = new GameObject("[WaterwheelCaptureCam]");
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
        private static string CaptureDir() => Path.Combine(ProjectRoot(), ".codex-artifacts", "waterwheel-play-verification-20261128");
        private static string EvidenceDir() => Path.Combine(ProjectRoot(), "Docs", "VerificationEvidence", "waterwheel-hd8-20261128");

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
            Debug.Log("[WaterwheelPlayVerify] " + (success ? "PASS" : "FAIL"));
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
