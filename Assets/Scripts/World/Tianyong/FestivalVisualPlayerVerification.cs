using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MmorpgClient.World.Tianyong
{
    /// <summary>由显式启动参数启用，在独立播放器的真实帧中复检离线地图并保存完整窗口截图。</summary>
    public sealed class FestivalVisualPlayerVerification : MonoBehaviour
    {
        private const float Tolerance = .002f;
        private const double TimeoutSeconds = 180;
        private static readonly uint[] MapIds = { 26000, 26008, 26008 };
        private static readonly string[] Appearances = { "mid-autumn", "spring", "mid-autumn" };
        private static readonly int[] Columns = { 5, 4, 4 };
        private static readonly int[] Rows = { 4, 3, 3 };
        private static FestivalVisualPlayerVerification _active;

        [Serializable]
        private sealed class ScreenshotResult
        {
            public string file;
            public string sha256;
            public int width;
            public int height;
            public int frame;
            public int updateFramesObserved;
            public float[] cameraPosition;
            public float orthographicSize;
            public bool nonUniformMapArea;
        }

        [Serializable]
        private sealed class MapResult
        {
            public uint mapId;
            public string appearance;
            public int columns;
            public int rows;
            public int loadedTiles;
            public int rendererCount;
            public bool validBoundsKnown;
            public float[] visualBounds;
            public int selectedFrame;
            public int verifiedFrame;
            public bool coordinatesVerified;
            public bool edgeClampVerified;
            public bool zoomVerified;
            public bool minimumZoomVerified;
            public string firstTileTexture;
            public string firstTileTextureEntityId;
            public ScreenshotResult overview;
            public ScreenshotResult lowerLeft;
            public ScreenshotResult upperRight;
        }

        [Serializable]
        private sealed class Report
        {
            public string result = "RUNNING";
            public string scope = "Standalone player; offline visual preview only; no navigation, collision, network or reference-pixel validation";
            public string screenshotMethod = "Actual player framebuffer via ScreenCapture.CaptureScreenshotAsTexture after WaitForEndOfFrame; includes OnGUI";
            public string runId;
            public string startedUtc;
            public string finishedUtc;
            public string unityVersion;
            public string applicationVersion;
            public string buildGuid;
            public int processId;
            public bool isEditor;
            public bool developmentBuild;
            public string scene;
            public string graphicsDeviceType;
            public string graphicsDeviceName;
            public string graphicsDeviceVersion;
            public string operatingSystem;
            public int screenWidth;
            public int screenHeight;
            public int startedFrame;
            public int finishedFrame;
            public int updateFramesObserved;
            public bool appearanceSwitchVerified;
            public string error;
            public string[] runtimeErrors;
            public MapResult[] maps = new MapResult[3];
        }

        private readonly List<string> _runtimeErrors = new List<string>();
        private readonly System.Diagnostics.Stopwatch _timer = new System.Diagnostics.Stopwatch();
        private Report _report;
        private string _outputDirectory;
        private FestivalMapVisualPreview _preview;
        private Texture _springTexture;
        private bool _finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AttachWhenRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-festivalPreviewVerify") < 0 || _active != null) return;
            var host = new GameObject("[FestivalVisualPlayerVerification]");
            DontDestroyOnLoad(host);
            _active = host.AddComponent<FestivalVisualPlayerVerification>();
        }

        private void Awake()
        {
            _timer.Start();
            _report = new Report {
                runId = Guid.NewGuid().ToString("N"),
                startedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                applicationVersion = Application.version,
                buildGuid = Application.buildGUID,
                processId = System.Diagnostics.Process.GetCurrentProcess().Id,
                isEditor = Application.isEditor,
                developmentBuild = Debug.isDebugBuild,
                graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                graphicsDeviceName = SystemInfo.graphicsDeviceName,
                graphicsDeviceVersion = SystemInfo.graphicsDeviceVersion,
                operatingSystem = SystemInfo.operatingSystem,
                startedFrame = Time.frameCount
            };
            Application.logMessageReceived += HandleLog;
        }

        private void Start()
        {
            Application.runInBackground = true;
            StartCoroutine(GuardedVerification());
        }

        private void Update()
        {
            if (_finished) return;
            _report.updateFramesObserved++;
            if (_runtimeErrors.Count != 0)
                Finish(false, new InvalidOperationException("Runtime errors were recorded; see runtimeErrors."));
            else if (_timer.Elapsed.TotalSeconds > TimeoutSeconds)
                Finish(false, new TimeoutException("Timed out waiting for actual player frames and map screenshots."));
        }

        private void HandleLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception)
                _runtimeErrors.Add(type + ": " + condition + "\n" + stackTrace);
        }

        // 手动推进验证迭代器，让任何加载、断言或抓屏异常都生成失败报告，而不是只中止协程。
        private IEnumerator GuardedVerification()
        {
            var verification = Verify();
            while (!_finished)
            {
                bool moved = false;
                object current = null;
                Exception failure = null;
                try
                {
                    moved = verification.MoveNext();
                    if (moved) current = verification.Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null) { Finish(false, failure); yield break; }
                if (!moved) { Finish(true, null); yield break; }
                yield return current;
            }
        }

        private IEnumerator Verify()
        {
            string[] args = Environment.GetCommandLineArgs();
            int outputIndex = Array.IndexOf(args, "-festivalPreviewCaptureDir");
            if (outputIndex < 0 || outputIndex + 1 >= args.Length || !Path.IsPathRooted(args[outputIndex + 1]))
                throw new ArgumentException("Provide -festivalPreviewCaptureDir <absolute output directory>.");
            _outputDirectory = Path.GetFullPath(args[outputIndex + 1]);
            Directory.CreateDirectory(_outputDirectory);
            WriteReport();
            // 窗口帧末抓屏需要实际图形设备；batchmode 不保证推进 WaitForEndOfFrame。
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Framebuffer verification requires a windowed player; omit -batchmode and -nographics.");
            if (Application.isEditor)
                throw new InvalidOperationException("This verification entry point requires a built standalone player.");

            TianyongSandboxBootstrap sandbox = null;
            while (sandbox == null)
            {
                sandbox = UnityEngine.Object.FindAnyObjectByType<TianyongSandboxBootstrap>();
                yield return null;
            }
            // 等待场景 Start 和至少三个真实帧，不能用 Editor 更新次数冒充播放器执行。
            for (int wait = 0; wait < 3; wait++) yield return null;
            _report.scene = SceneManager.GetActiveScene().path;
            _report.screenWidth = Screen.width;
            _report.screenHeight = Screen.height;
            if (Screen.width < 640 || Screen.height < 360)
                throw new InvalidOperationException("The player framebuffer must be at least 640x360.");

            for (int index = 0; index < MapIds.Length; index++)
            {
                int selectedFrame = Time.frameCount;
                if (index == 0)
                {
                    FestivalMapVisualPreview.Open(sandbox, MapIds[index], Appearances[index]);
                    _preview = sandbox.GetComponent<FestivalMapVisualPreview>();
                }
                else if (_preview == null || !_preview.Select(MapIds[index], Appearances[index]))
                    throw new InvalidOperationException("Unable to select map " + MapIds[index] + "/" + Appearances[index]);
                for (int wait = 0; wait < 3; wait++) yield return null;

                MapResult result = ValidateMap(index, selectedFrame);
                _report.maps[index] = result;
                Camera camera = _preview.PreviewCamera;
                camera.aspect = Screen.width / (float)Screen.height;
                Rect bounds = _preview.VisualBounds;
                float fit = Mathf.Max(bounds.height * .5f, bounds.width * .5f / camera.aspect);
                float near = Mathf.Min(bounds.height * .24f, bounds.width * .24f / camera.aspect);
                if (near < .15f) throw new InvalidOperationException("Map bounds are too small for edge clamp verification.");

                _preview.SetView(bounds.center, .01f);
                CheckNear(_preview.Zoom, .15f, "minimum zoom");
                CheckNear(camera.orthographicSize, .15f, "minimum camera zoom");
                result.minimumZoomVerified = true;

                string stem = MapIds[index] + "-" + Appearances[index];
                // 每个视角均在 Update 后设置，在该帧渲染及 OnGUI 完成后读取真实窗口。
                yield return null;
                _preview.SetView(bounds.center, fit);
                CheckCamera(bounds.center, fit);
                yield return new WaitForEndOfFrame();
                CheckCamera(bounds.center, fit);
                result.overview = Capture(stem + "-overview.png");

                yield return null;
                var lowerLeft = new Vector2(bounds.xMin + near * camera.aspect, bounds.yMin + near);
                _preview.SetView(new Vector2(bounds.xMin - 100, bounds.yMin - 100), near);
                CheckCamera(lowerLeft, near);
                yield return new WaitForEndOfFrame();
                CheckCamera(lowerLeft, near);
                result.lowerLeft = Capture(stem + "-lower-left.png");

                yield return null;
                var upperRight = new Vector2(bounds.xMax - near * camera.aspect, bounds.yMax - near);
                _preview.SetView(new Vector2(bounds.xMax + 100, bounds.yMax + 100), near);
                CheckCamera(upperRight, near);
                yield return new WaitForEndOfFrame();
                CheckCamera(upperRight, near);
                result.upperRight = Capture(stem + "-upper-right.png");
                result.edgeClampVerified = true;
                result.zoomVerified = true;
                result.verifiedFrame = Time.frameCount;
                _preview.SetView(bounds.center, fit);
                WriteReport();
                Debug.Log($"[FestivalVisualPlayerVerification] PASS {stem}: {result.loadedTiles} tiles and three framebuffer screenshots");
            }
            if (!_report.appearanceSwitchVerified)
                throw new InvalidOperationException("Spring and Mid-Autumn map textures did not switch.");
            // 再推进一帧，收集最后一张地图在后续 Update 中产生的异常。
            yield return null;
        }

        private MapResult ValidateMap(int index, int selectedFrame)
        {
            if (_preview == null || _preview.CurrentMapId != MapIds[index] || _preview.CurrentAppearance != Appearances[index])
                throw new InvalidOperationException("Selected map or appearance does not match the requested case.");
            int columns = Columns[index], rows = Rows[index];
            if (_preview.Columns != columns || _preview.Rows != rows || _preview.LoadedTileCount != columns * rows)
                throw new InvalidOperationException("Unexpected grid or loaded tile count.");
            GameObject root = _preview.VisualRoot;
            if (root == null || !root.activeInHierarchy) throw new InvalidOperationException("Visual map root is not active.");
            var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
            if (renderers.Length != columns * rows) throw new InvalidOperationException("Unexpected tile renderer count.");
            Texture firstTexture = null;
            for (int row = 1; row <= rows; row++)
            for (int column = 1; column <= columns; column++)
            {
                string name = $"r{row:00}_c{column:00}";
                UnityEngine.Transform tile = root.transform.Find(name);
                if (tile == null) throw new InvalidOperationException("Missing tile " + name);
                var renderer = tile.GetComponent<SpriteRenderer>();
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sprite == null ||
                    renderer.sprite.texture == null || renderer.sprite.texture.width != 1024 || renderer.sprite.texture.height != 1024)
                    throw new InvalidOperationException("Invalid tile renderer or image dimensions: " + name);
                CheckNear(tile.localPosition.x, column - .5f, name + " x");
                CheckNear(tile.localPosition.y, rows - row + .5f, name + " y");
                CheckNear(renderer.sprite.bounds.size.x, 1, name + " width");
                CheckNear(renderer.sprite.bounds.size.y, 1, name + " height");
                if (row == 1 && column == 1) firstTexture = renderer.sprite.texture;
            }
            Camera camera = _preview.PreviewCamera;
            if (camera == null || !camera.orthographic || !camera.enabled || !camera.gameObject.activeInHierarchy)
                throw new InvalidOperationException("The player preview camera is not active.");
            Rect bounds = _preview.VisualBounds;
            CheckFinite(bounds.x, "bounds x"); CheckFinite(bounds.y, "bounds y");
            CheckFinite(bounds.width, "bounds width"); CheckFinite(bounds.height, "bounds height");
            if (bounds.width <= 0 || bounds.height <= 0 || bounds.xMin < -Tolerance || bounds.yMin < -Tolerance ||
                bounds.xMax > columns + Tolerance || bounds.yMax > rows + Tolerance)
                throw new InvalidOperationException("Visual bounds extend outside the tile grid.");
            if (index == 1) _springTexture = firstTexture;
            if (index == 2)
                _report.appearanceSwitchVerified = _springTexture != null && firstTexture != null && firstTexture != _springTexture;
            return new MapResult {
                mapId = MapIds[index], appearance = Appearances[index], columns = columns, rows = rows,
                loadedTiles = _preview.LoadedTileCount, rendererCount = renderers.Length,
                validBoundsKnown = _preview.ValidBoundsKnown,
                visualBounds = new[] { bounds.x, bounds.y, bounds.width, bounds.height },
                selectedFrame = selectedFrame, coordinatesVerified = true,
                firstTileTexture = firstTexture != null ? firstTexture.name : "",
                firstTileTextureEntityId = firstTexture != null ? firstTexture.GetEntityId().ToString() : null
            };
        }

        private void CheckCamera(Vector2 focus, float zoom)
        {
            Camera camera = _preview.PreviewCamera;
            CheckNear(_preview.Zoom, zoom, "preview zoom");
            CheckNear(camera.orthographicSize, zoom, "camera zoom");
            CheckNear(_preview.Focus.x, focus.x, "focus x");
            CheckNear(_preview.Focus.y, focus.y, "focus y");
            CheckNear(camera.transform.position.x, focus.x, "camera x");
            CheckNear(camera.transform.position.y, focus.y, "camera y");
        }

        private ScreenshotResult Capture(string file)
        {
            Texture2D pixels = null;
            try
            {
                pixels = ScreenCapture.CaptureScreenshotAsTexture();
                if (pixels == null || pixels.width != Screen.width || pixels.height != Screen.height)
                    throw new InvalidOperationException("Unable to capture the actual player framebuffer.");
                Color32[] colors = pixels.GetPixels32();
                bool varied = false;
                Color32 first = colors[0];
                // 排除左上角菜单，避免仅靠 UI 文字把空白地图误判为非空抓屏。
                for (int y = 0; y < pixels.height && !varied; y += 17)
                for (int x = 0; x < pixels.width; x += 19)
                {
                    if (x < 620 && y > pixels.height - 210) continue;
                    Color32 color = colors[y * pixels.width + x];
                    if (Math.Abs(color.r - first.r) + Math.Abs(color.g - first.g) + Math.Abs(color.b - first.b) > 20)
                    { varied = true; break; }
                }
                if (!varied) throw new InvalidOperationException("The rendered map area is blank: " + file);
                byte[] png = pixels.EncodeToPNG();
                File.WriteAllBytes(Path.Combine(_outputDirectory, file), png);
                string hash;
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(png)).Replace("-", "").ToLowerInvariant();
                UnityEngine.Vector3 cameraPosition = _preview.PreviewCamera.transform.position;
                return new ScreenshotResult {
                    file = file, sha256 = hash, width = pixels.width, height = pixels.height,
                    frame = Time.frameCount, updateFramesObserved = _report.updateFramesObserved,
                    cameraPosition = new[] { cameraPosition.x, cameraPosition.y, cameraPosition.z },
                    orthographicSize = _preview.PreviewCamera.orthographicSize, nonUniformMapArea = varied
                };
            }
            finally { if (pixels != null) Destroy(pixels); }
        }

        private static void CheckFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Non-finite " + name);
        }

        private static void CheckNear(float actual, float expected, string name)
        {
            CheckFinite(actual, name); CheckFinite(expected, name + " expected");
            if (Mathf.Abs(actual - expected) > Tolerance)
                throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
        }

        private void WriteReport()
        {
            _report.runtimeErrors = _runtimeErrors.ToArray();
            File.WriteAllText(Path.Combine(_outputDirectory, "player-verification.json"), JsonUtility.ToJson(_report, true));
        }

        private void Finish(bool success, Exception error)
        {
            if (_finished) return;
            _finished = true;
            Application.logMessageReceived -= HandleLog;
            if (_runtimeErrors.Count != 0)
            {
                success = false;
                if (error == null) error = new InvalidOperationException("Runtime errors were recorded; see runtimeErrors.");
            }
            _report.result = success ? "PASS" : "FAIL";
            _report.finishedUtc = DateTime.UtcNow.ToString("O");
            _report.finishedFrame = Time.frameCount;
            _report.error = error?.ToString();
            try { if (_outputDirectory != null) WriteReport(); }
            catch (Exception reportError)
            {
                success = false;
                Debug.LogError("[FestivalVisualPlayerVerification] Unable to write report: " + reportError);
            }
            if (error != null) Debug.LogError("[FestivalVisualPlayerVerification] " + error);
            Debug.Log("[FestivalVisualPlayerVerification] " + (success ? "PASS" : "FAIL"));
            // 此组件只由显式参数创建；这里只退出当前验证播放器，不查找或操作其他客户端。
            Application.Quit(success ? 0 : 1);
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= HandleLog;
            if (_active == this) _active = null;
        }
    }
}
